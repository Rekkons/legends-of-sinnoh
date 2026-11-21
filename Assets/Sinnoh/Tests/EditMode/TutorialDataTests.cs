using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Tutorial;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6.5 각본 데이터(07 §7·§8): 각본 파일(`Data/Json/tutorial/*.json` — build.mjs가 `Tools/data/authored/tutorial/`에서 만든다) · 로더(TutorialLoader) ·
    /// 데이터 검사(완료 조건 2 — 근거 · <b>숫자</b> ↔ expect · 앵커 문법 · 봇 답 덮임 · 말풍선 길이 · 마박사 말투 · 금지어).
    /// </summary>
    public class TutorialDataTests
    {
        static Ctx ctx;
        static StringTable ko;

        [OneTimeSetUp]
        public void Load()
        {
            ctx = TestData.LoadCtx();
            ko = TestData.LoadStrings("ko");
        }

        static string[] ScriptFiles() => Directory.GetFiles(TestData.JsonPath("tutorial"), "*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();

        static string MainJson() => TestData.ReadText("tutorial/main.json");

        static TutorialScript MainScript() => TutorialLoader.Load(MainJson(), ctx, "tutorial/main.json");

        /// <summary>막 앞 판 손보기 이름 → Ai 막 길(<see cref="SoloTrail.AfterNamed"/> — 화면 쪽 조립 지점 <c>TutorialMenu</c>도 같은 함수를 꽂는다, 07 T25).</summary>
        internal static Action<Ctx, GameState> TrailAfter(string name) => SoloTrail.AfterNamed(name);

        static string ReplaceFirst(string s, string from, string to)
        {
            int i = s.IndexOf(from, StringComparison.Ordinal);
            Assert.GreaterOrEqual(i, 0, $"견본에 「{from}」가 없다");
            return s.Substring(0, i) + to + s.Substring(i + from.Length);
        }

        [Test]
        public void M65_각본로더_키를색인으로_엄격하다()
        {
            var s = MainScript();
            Assert.AreEqual("main", s.Id);
            Assert.AreEqual(650UL, s.Start.Seed);
            Assert.AreEqual(1, s.Start.Seats);
            Assert.IsNull(s.Bots);
            var byId = s.Steps.ToDictionary(x => x.Id);
            Assert.AreEqual(ctx.Fixtures.Ids.IndexOf("village.canalave"), byId["S101"].Allow[0].Target, "마을 키 → 고정물 색인");
            Assert.AreEqual(ctx.Cards.TrainerCardIds.IndexOf("hiker"), byId["S104"].Auto[1].Target, "trainer_card.<키> → 트레이너 카드 색인");
            Assert.AreEqual(ctx.Cards.PokemonIds.IndexOf("410:0"), byId["S304"].Auto[0].Target, "pokemon.<키> → 포켓몬 색인");
            Assert.AreEqual(StepAdvance.Next, byId["S001"].Advance);
            Assert.AreEqual("tutorial.main.S101.hint", byId["S101"].Hint);
            Assert.IsNull(byId["S001"].Hint);
            CollectionAssert.AreEqual(new[] { AttackDieFace.CriticalHit, AttackDieFace.CriticalHit, AttackDieFace.CriticalHit }, byId["S306"].Dice);
            Assert.AreEqual("movesLeft=3", byId["S301"].Expect.Single().ToString());

            // demo · screen · 정비 꼴 · after · 봇 큐
            string extra = "{\"schema\":1,\"id\":\"x\",\"start\":{\"campaign\":\"HeartOfSinnoh\",\"seats\":2,\"seed\":3,\"trainerCards\":null,\"tops\":[],\"spots\":[],\"after\":\"lake\"},"
                + "\"bots\":[null,[{\"kind\":\"EndTurn\"}]],\"steps\":[\n"
                + Step("A1", "\"advance\":\"screen\",\"screen\":\"board-hud-log\",\"botSeat\":-1,\"botAnswers\":0,\"allow\":[],\"auto\":[]") + ",\n"
                + Step("A2", "\"advance\":\"demo\",\"screen\":null,\"botSeat\":-1,\"botAnswers\":0,\"allow\":[],\"auto\":[{\"maint\":\"HoldItem\",\"party\":1,\"value\":\"item.choice_band\"}]") + ",\n"
                + Step("A3", "\"advance\":\"bot\",\"screen\":null,\"botSeat\":1,\"botAnswers\":1,\"allow\":[],\"auto\":[]") + "\n]}\n";
            var x = TutorialLoader.Load(extra, ctx, "x.json");
            Assert.AreEqual("lake", x.Start.After);
            Assert.AreEqual(StepAdvance.Screen, x.Steps[0].Advance);
            Assert.AreEqual("board-hud-log", x.Steps[0].Screen);
            Assert.AreEqual(StepAdvance.Demo, x.Steps[1].Advance);
            var m = x.Steps[1].Auto[0];
            Assert.IsTrue(m.IsMaint);
            Assert.AreEqual(MaintKind.HoldItem, m.Maint);
            Assert.AreEqual(1, m.Party);
            Assert.AreEqual(ctx.Items.Ids.IndexOf("item.choice_band"), m.Value, "정비 value의 키도 색인으로");
            Assert.AreEqual(1, x.Bots[1].Count);
            Assert.AreEqual(1, x.Steps[2].BotSeat);

            // 엄격: 틀린 데이터는 FormatException(파일 이름이 붙는다)
            string j = MainJson();
            var bad = new Dictionary<string, string>
            {
                ["모르는 칸"] = ReplaceFirst(j, "\"ch\":0,", "\"ch\":0,\"chapter\":0,"),
                ["같은 칸 두 번"] = ReplaceFirst(j, "\"ch\":0,", "\"ch\":0,\"ch\":1,"),
                ["모르는 advance"] = ReplaceFirst(j, "\"advance\":\"next\"", "\"advance\":\"later\""),
                ["모르는 expect 키"] = ReplaceFirst(j, "{\"movesLeft\":3}", "{\"moves\":3}"),
                ["expect 값이 문자열"] = ReplaceFirst(j, "{\"movesLeft\":3}", "{\"movesLeft\":\"3\"}"),
                ["없는 포켓몬 키"] = ReplaceFirst(j, "\"pokemon.410:0\"", "\"pokemon.no_such\""),
                ["어느 표에도 없는 키"] = ReplaceFirst(j, "\"village.canalave\"", "\"village.atlantis\""),
                ["대사 키가 단계와 다르다"] = ReplaceFirst(j, "\"text\":\"tutorial.main.S001\"", "\"text\":\"tutorial.main.S002\""),
                ["막 앞 판이 아닌 각본의 말없는 단계"] = ReplaceFirst(j, "\"text\":\"tutorial.main.S001\"", "\"text\":null"),
                ["단계 id 겹침"] = ReplaceFirst(j, "\"id\":\"S102\"", "\"id\":\"S101\""),
                ["command에 allow가 없다"] = ReplaceFirst(j, "\"allow\":[{\"kind\":\"Search\"}]", "\"allow\":[]"),
                ["next에 auto"] = ReplaceFirst(j, "\"advance\":\"next\",\"screen\":null,\"botSeat\":-1,\"botAnswers\":0,\"allow\":[],\"auto\":[]", "\"advance\":\"next\",\"screen\":null,\"botSeat\":-1,\"botAnswers\":0,\"allow\":[],\"auto\":[{\"kind\":\"EndTurn\"}]"),
                ["봇 없는 bot 단계"] = ReplaceFirst(j, "\"advance\":\"next\",\"screen\":null,\"botSeat\":-1,\"botAnswers\":0", "\"advance\":\"bot\",\"screen\":null,\"botSeat\":1,\"botAnswers\":1"),
                ["모르는 주사위 면"] = ReplaceFirst(j, "\"CriticalHit\"", "\"Crit\""),
                ["없는 트레이너 카드(판 손보기)"] = ReplaceFirst(j, "\"hiker\"", "\"nobody\""),
                ["정비 꼴에 choice"] = ReplaceFirst(j, "{\"kind\":\"Search\"}]", "{\"kind\":\"Search\",\"maint\":\"PartyOrder\"}]"),
                ["답 꼴에 party"] = ReplaceFirst(j, "{\"kind\":\"Search\"}]", "{\"kind\":\"Search\",\"party\":0}]"),
                ["after 문법"] = ReplaceFirst(j, "\"after\":null", "\"after\":\"Lake Trail\""),
                ["schema"] = ReplaceFirst(j, "\"schema\":1", "\"schema\":2"),
            };
            foreach (var kv in bad)
            {
                var e = Assert.Throws<FormatException>(() => TutorialLoader.Load(kv.Value, ctx, "tutorial/main.json"), kv.Key);
                StringAssert.Contains("tutorial/main.json", e.Message, kv.Key);
            }
        }

        static string Step(string id, string rest) =>
            $"{{\"id\":\"{id}\",\"ch\":0,\"text\":\"tutorial.x.{id}\",\"hint\":null,\"anchor\":null,{rest},\"dice\":[],\"expect\":{{}},\"ref\":[\"07 §7\"]}}";

        [Test]
        public void M65_각본파일_모두_끝까지_단계마다expect가엔진값과같다()
        {
            var manifest = ContentManifest.Load(TestData.ReadText("manifest.json"));
            var files = ScriptFiles();
            Assert.Greater(files.Length, 0, "각본 파일이 있다");
            foreach (var f in files)
            {
                string name = Path.GetFileName(f);
                var s = TutorialLoader.Load(File.ReadAllText(f, new System.Text.UTF8Encoding(false)), ctx, "tutorial/" + name);
                Assert.AreEqual(Path.GetFileNameWithoutExtension(f), s.Id, "파일 이름 = 각본 id");
                var table = manifest.Find("tutorial." + s.Id);
                Assert.AreEqual("tutorial/" + name, table.File);
                Assert.AreEqual(s.Steps.Count, table.Rows, "manifest rows = 단계 수");
                CollectionAssert.DoesNotContain(manifest.ContentTables, "tutorial." + s.Id, "각본은 엔진 표가 아니다(세이브 ContentHash 밖)");

                var run = new TutorialRunner(ctx, s, new PrincipleAgent(), TrailAfter);
                run.PlayToEnd();
                Assert.IsTrue(run.Finished, s.Id);
                CollectionAssert.IsEmpty(run.Failures, s.Id + "\n" + string.Join("\n", run.Failures));
                Assert.AreEqual(0, run.Deal.DiceLeft, s.Id + ": 주사위 대본을 다 썼다");
                Assert.AreEqual(0, run.Blocked, s.Id + ": 자동 답은 거르개를 지난다");
                Assert.IsFalse(run.Session.Saving, s.Id + ": 저장하지 않는다");
            }
            Assert.AreEqual(files.Length, manifest.Tables.Count(t => t.Name.StartsWith("tutorial.", StringComparison.Ordinal)), "manifest의 각본 = 파일");
        }

        /// <summary>
        /// 본편 0~6장(07 §4-2 — 1인, 서막~1막): 장이 0부터 차례로 모두 있고, 봇으로 끝까지 돌리면 단계마다 expect가 엔진 값과 같고(완료 조건 1),
        /// 끝 국면이 1막 목표를 이룬 판이다(2막 · 고향 운하시티 · 파티 = 팽도리 + 잡은 비버니). 막 맛보기(7장)는 따로 연다(07 §4-3).
        /// </summary>
        [Test]
        public void M65_본편_각본_끝까지()
        {
            var s = MainScript();
            var chapters = s.Steps.Select(x => x.Chapter).Distinct().ToList();
            CollectionAssert.AreEqual(Enumerable.Range(0, 8).ToList(), chapters, "본편 0~6장 + 막 맛보기 7-1(7장)이 차례로");
            for (int i = 1; i < s.Steps.Count; i++)
                Assert.GreaterOrEqual(s.Steps[i].Chapter, s.Steps[i - 1].Chapter, $"{s.Steps[i].Id}: 장 순서가 거꾸로");
            Assert.AreEqual(1, s.Start.Seats, "본편은 혼자 하기(결정 90 Q3)");
            Assert.Greater(s.Steps.Count(x => x.Advance == StepAdvance.Command), 15, "내 조작 단계(07 §4-2 약 18)");

            var run = new TutorialRunner(ctx, s, new PrincipleAgent(), TrailAfter);
            run.PlayToEnd();
            CollectionAssert.IsEmpty(run.Failures, string.Join("\n", run.Failures));
            Assert.AreEqual(0, run.Deal.DiceLeft, "주사위 대본을 다 썼다");
            Assert.AreEqual(0, run.Blocked);
            var h = run.Session.HostCopy();
            var me = h.Seats[TutorialRunner.Me];
            Assert.AreEqual(2, h.Hos.Act, "1막 목표를 이뤄 2막(A1)");
            Assert.AreEqual(Phase.Turn, h.Phase);
            Assert.AreEqual(ctx.Fixtures.Ids.IndexOf("village.canalave"), me.HomeVillage);
            var party = me.Party.Where(id => id >= 0).Select(id => ctx.Cards.PokemonIds[h.Pokemon[id].Def]).ToList();
            CollectionAssert.AreEquivalent(new[] { "393:0", "399:0" }, party, "팽도리 + 잡은 비버니");
            Assert.IsFalse(run.Session.Saving, "저장하지 않는다");
        }

        /// <summary>
        /// 결정 104 ①(07 T190): 배틀 결과 창이 떠 있는 동안에는 그 창을 말하는 단계만 뜬다 — 「결과 창 [확인]을 먼저 누르고 다른 곳」을 가리키는 앵커
        /// (<c>battle-result-ok+…</c>)는 쓰지 않는다. 결과 창 안을 가리키는 앵커(<c>battle-result…</c> · <c>battle-reward…</c>)는 한 부분뿐이고, 그 가운데 결과 창 판 통짜
        /// (<c>battle-result-panel</c> — [확인]을 품는다)를 가리키는 [다음] 단계는 대사가 [확인]을 말한다. 굴림 값(matchup · damage · diceSum · captureSum)은 뒤따르는 [다음] 단계까지 남는다(T192).
        /// </summary>
        [Test]
        public void M65_결정104_결과창을말하는단계만결과창앵커_확인을말한다()
        {
            int closing = 0;
            foreach (string file in ScriptFiles())
            {
                string id = Path.GetFileNameWithoutExtension(file);
                var s = TutorialLoader.Load(File.ReadAllText(file), ctx, id + ".json");
                foreach (var st in s.Steps)
                {
                    if (st.Anchor == null) continue;
                    var parts = st.Anchor.Split('+');
                    bool inResult = parts[0].StartsWith("battle-result", StringComparison.Ordinal) || parts[0].StartsWith("battle-reward", StringComparison.Ordinal);
                    for (int i = 0; i < parts.Length; i++)
                        if (parts[i] == "battle-result-ok") Assert.AreEqual(1, parts.Length, $"{id}.{st.Id}: 결과 창 [확인]을 앞에 둔 앵커 {st.Anchor} — 결과 창이 닫힌 뒤에 뜨는 단계는 [확인] 부분을 두지 않는다");
                    if (!inResult) continue;
                    Assert.AreEqual(1, parts.Length, $"{id}.{st.Id}: 결과 창 단계의 앵커는 한 부분({st.Anchor})");
                    if (st.Advance == StepAdvance.Next && parts[0] == "battle-result-panel")
                    {
                        closing++;
                        StringAssert.Contains("[확인]", Line(st.Text), $"{id}.{st.Id}: 결과 창을 닫으면 끝나는 단계는 대사가 [확인]을 말한다");
                    }
                }
            }
            Assert.GreaterOrEqual(closing, 3, "본편의 결과 창 단계(3장 · 4장 · 6장)");

            // 굴린 뒤의 [다음] 단계가 그 굴림 값을 말한다(본편 S307 — 눈 합 · 데미지)
            var main = MainScript();
            var recap = main.Steps.Find(x => x.Id == "S307");
            Assert.AreEqual(StepAdvance.Next, recap.Advance);
            CollectionAssert.AreEquivalent(new[] { "diceSum=6", "damage=8" }, recap.Expect.Select(e => e.ToString()).ToList());
            CollectionAssert.Contains(main.Steps.Find(x => x.Id == "S306").Expect.Select(e => e.ToString()).ToList(), "matchup=2", "물 기술 × 바위·강철 = +2(01 §6-4)");
        }

        /// <summary>
        /// 막 맛보기(07 §4-3): 7-1은 본편 각본 끝(위 시험), 7-2 = preview2(막 길 heart — 조각 셋 · 3막인 판에서 2막 → 3막을 대사로만, 결정 96 Q9 나),
        /// 7-3 = preview3(막 길 ending — 종막 선택이 열린 판). 둘 다 사람 명령 단계가 없고(보기만, 결정 90 Q4) 끝까지 expect가 맞는다.
        /// </summary>
        [Test]
        public void M65_막맛보기_각본_끝까지()
        {
            foreach (string id in new[] { "preview2", "preview3" })
            {
                var s = TutorialLoader.Load(TestData.ReadText($"tutorial/{id}.json"), ctx, id + ".json");
                Assert.IsTrue(s.Steps.All(x => x.Chapter == 7), id + ": 모두 7장");
                Assert.IsFalse(s.Steps.Any(x => x.Advance == StepAdvance.Command || x.Advance == StepAdvance.Free), id + ": 보기만(사람 명령 없음)");
                Assert.IsNotNull(s.Start.After, id + ": 막 앞 판");
                var run = new TutorialRunner(ctx, s, new PrincipleAgent(), TrailAfter);
                run.PlayToEnd();
                CollectionAssert.IsEmpty(run.Failures, id + "\n" + string.Join("\n", run.Failures));
                Assert.IsTrue(run.Rebased, id + ": 막 앞 판 손보기를 걸었다");
                Assert.AreEqual(0, run.Blocked, id);
                var h = run.Session.HostCopy();
                if (id == "preview2")
                {
                    Assert.AreEqual(3, h.Hos.Act, "조각 셋 · 3막인 판(A2)");
                    Assert.IsTrue(s.Steps.Where(x => x.Text != null).All(x => x.Advance == StepAdvance.Next), "대사로만 — 마박사가 대신 두는 배틀이 없다(결정 96 Q9 나)");
                    Assert.AreEqual(0, h.NextBattleId, "배틀을 하지 않았다");
                    Assert.AreEqual(3, h.Seats[0].Shards.Count(b => b));
                }
                else
                {
                    var ending = run.Client.View.Pending.FirstOrDefault(c => c.Seat == 0 && c.Kind == ChoiceKind.Ending);
                    Assert.IsNotNull(ending, "종막 선택이 열린 채 끝난다(고르지 않는다)");
                }
                Assert.IsFalse(run.Session.Saving, id);
            }
        }

        /// <summary>
        /// 확장 장 하나(07 §5): 혼자 하기 · 한 장(단계 ch가 모두 같다) · 사람 조작 단계가 있다 · 판 차리기 demo 둘로 시작 → 봇으로 끝까지 돌려 expect가 모두 맞고,
        /// 주사위 대본을 다 쓰고, 거르개에 막힌 자동 답이 없고, 저장하지 않는다. 끝 국면(호스트 사본)과 진행기를 돌려준다.
        /// </summary>
        static (GameState host, TutorialRunner run) PlayExtra(string id)
        {
            var s = TutorialLoader.Load(TestData.ReadText($"tutorial/{id}.json"), ctx, id + ".json");
            Assert.AreEqual(id, s.Id);
            Assert.AreEqual(1, s.Start.Seats, id + ": 혼자 하기(07 §5-2 인원 1)");
            Assert.IsNull(s.Bots, id);
            Assert.AreEqual(1, s.Steps.Select(x => x.Chapter).Distinct().Count(), id + ": 각본 하나가 장 하나");
            Assert.AreEqual(StepAdvance.Demo, s.Steps[0].Advance, id + ": 판 차리기 demo(07 T56)");
            Assert.AreEqual(StepAdvance.Demo, s.Steps[1].Advance, id);
            Assert.Greater(s.Steps.Count(x => x.Advance == StepAdvance.Command), 0, id + ": 지정 행동이 있다");
            var run = new TutorialRunner(ctx, s, new PrincipleAgent(), TrailAfter);
            run.PlayToEnd();
            CollectionAssert.IsEmpty(run.Failures, id + "\n" + string.Join("\n", run.Failures));
            Assert.AreEqual(0, run.Deal.DiceLeft, id + ": 주사위 대본을 다 썼다");
            Assert.AreEqual(0, run.Blocked, id + ": 자동 답은 거르개를 지난다");
            Assert.AreEqual(s.Start.After != null, run.Rebased, id + ": 막 앞 판 손보기");
            Assert.IsFalse(run.Session.Saving, id + ": 저장하지 않는다");
            return (run.Session.HostCopy(), run);
        }

        static string Key(GameState h, int pokemon) => ctx.Cards.PokemonIds[h.Pokemon[pokemon].Def];

        /// <summary>E1 운명의 변화(막 길 fate): 6장째 버리기 → 펄기아로 칸 49 → 디아루가 3장(아르세우스) · 기라티나 1장으로 흔들풍손을 쓰러뜨리고, 떨어진 별로 다음 턴에 1장.</summary>
        [Test]
        public void M65_E1_각본_끝까지()
        {
            var (h, _) = PlayExtra("e01");
            var me = h.Seats[TutorialRunner.Me];
            Assert.AreEqual(Phase.Turn, h.Phase);
            Assert.AreEqual(2, h.Round, "배틀로 턴이 끝나 2라운드");
            Assert.AreEqual(49, me.Cell, "펄기아 순간이동 칸(A24)");
            Assert.AreEqual(1, me.FateHand.Count, "디아루가 3장 · 기라티나 1장을 쓰고 떨어진 별로 1장(A25)");
            Assert.IsTrue(me.Stamps[StampRules.FateCards], "스탬프 7 「서로 다른 운명 카드 3장」(A30)");
            Assert.AreEqual(3, me.Jp, "약한 야생 기절 1 + 2(A8 · p.9)");
        }

        /// <summary>E2 제작(막 길 workshop): 자극적인 스튜(이동력 6) → 별 방패 → 팽도리가 지닌다.</summary>
        [Test]
        public void M65_E2_각본_끝까지()
        {
            var (h, _) = PlayExtra("e02");
            var me = h.Seats[TutorialRunner.Me];
            Assert.AreEqual(6, me.MovesLeft, "자극적인 스튜 +3");
            int piplup = me.Party[0];
            Assert.AreEqual("393:0", Key(h, piplup));
            Assert.AreEqual(ctx.Items.Ids.IndexOf("item.star_shield"), h.Pokemon[piplup].HeldItem, "별 방패를 지녔다");
            CollectionAssert.IsEmpty(me.Bag, "가방에서 꺼내 지녔다");
        }

        /// <summary>E3 은행상회·공중날기(막 길 market): 영원시티에서 두 줄 거래(A4) → 빨강 1개로 봉신마을(A5).</summary>
        [Test]
        public void M65_E3_각본_끝까지()
        {
            var (h, _) = PlayExtra("e03");
            var me = h.Seats[TutorialRunner.Me];
            Assert.AreEqual(ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf("village.celestic")].Cell, me.Cell, "공중날기로 봉신마을");
            Assert.AreEqual(4, me.Apricorns[(int)ApricornColor.Black], "두 줄 거래의 검정");
            Assert.AreEqual(1, h.Round, "거래·공중날기로는 턴이 끝나지 않는다");
        }

        /// <summary>E4 진화(막 길 workshop): 우러난 스튜 반값으로 이브이 → 리피아, 옛 시그니처 전광석화를 첫 칸에.</summary>
        [Test]
        public void M65_E4_각본_끝까지()
        {
            var (h, _) = PlayExtra("e04");
            var me = h.Seats[TutorialRunner.Me];
            int leafeon = me.Party[1];
            Assert.AreEqual("470:0", Key(h, leafeon), "분기 고르기(R14)");
            Assert.AreEqual(16, me.Jp, "비용 8의 반값(A9)");
            Assert.AreEqual(ctx.Cards.MoveIds.IndexOf("quick_attack"), h.Pokemon[leafeon].Moves[0], "옛 시그니처 가르치기");
            Assert.AreEqual(0, h.Pokemon[leafeon].Damage, "완전 회복");
        }

        /// <summary>E5 숨겨진 장소(시드 3 + 정체 손보기): 유물 마나피의 알을 지니고 우두머리 포푸니라를 몬스터볼로 잡는다(A17 — 여행 점수 4 · 스탬프 4).</summary>
        [Test]
        public void M65_E5_각본_끝까지()
        {
            var (h, _) = PlayExtra("e05");
            var me = h.Seats[TutorialRunner.Me];
            Assert.AreEqual(Phase.Turn, h.Phase);
            Assert.AreEqual(ctx.Items.Ids.IndexOf("item.seafarer_egg"), h.Pokemon[me.Party[0]].HeldItem, "팽도리가 유물을 지녔다(A25)");
            int alpha = me.Party.First(id => id >= 0 && h.Pokemon[id].Alpha);
            Assert.AreEqual("461:0", Key(h, alpha), "잡은 우두머리");
            Assert.AreEqual(4, me.Jp, "우두머리 포획 4(A17)");
            Assert.IsTrue(me.Stamps[StampRules.LegendaryCaught], "우두머리 포획 = 전설 포획 스탬프(A30)");
        }

        /// <summary>E6 배틀 더 알기(막 길 duo): 조무래기 배틀 — 교체 · 우선권 토큰 · 독 · 은하의 응징, 이겨서 2막.</summary>
        [Test]
        public void M65_E6_각본_끝까지()
        {
            var (h, _) = PlayExtra("e06");
            var me = h.Seats[TutorialRunner.Me];
            Assert.AreEqual(2, h.Hos.Act, "1막 목표(A1)");
            Assert.AreEqual(Phase.Turn, h.Phase);
            int eevee = me.Party[1];
            Assert.AreEqual("133:0", Key(h, eevee));
            Assert.AreEqual(StatusEffect.Poisoned, h.Pokemon[eevee].Status, "맹독엄니의 독이 남는다(S7_6 — 상태 이상은 배틀 뒤에도)");
            Assert.AreEqual(3, PokemonRules.MaxHp(ctx, h.Pokemon[eevee]) - h.Pokemon[eevee].Damage);
        }

        [Test]
        public void M65_데이터검사()
        {
            int steps = 0, numbered = 0;
            foreach (var f in ScriptFiles())
            {
                var s = TutorialLoader.Load(File.ReadAllText(f, new System.Text.UTF8Encoding(false)), ctx, "tutorial/" + Path.GetFileName(f));
                var problems = TutorialDataCheck.Problems(s, ctx, Line);
                CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
                steps += s.Steps.Count;
                numbered += s.Steps.Count(x => x.Expect.Count > 0);
            }
            Assert.Greater(steps, 0);
            Assert.Greater(numbered, 0, "숫자 대조가 실제로 도는 단계가 있다");
            // 쓰이지 않는 대사가 없다(build.mjs도 막는다) — 표의 tutorial.* = 단계의 text·hint
            var used = new HashSet<string>();
            foreach (var f in ScriptFiles())
                foreach (var x in TutorialLoader.Load(File.ReadAllText(f, new System.Text.UTF8Encoding(false)), ctx, f).Steps)
                {
                    if (x.Text != null) used.Add(x.Text);
                    if (x.Hint != null) used.Add(x.Hint);
                }
            CollectionAssert.AreEquivalent(used, ko.Keys.Where(k => k.StartsWith("tutorial.", StringComparison.Ordinal)));
        }

        /// <summary>
        /// 막 앞 판 각본(07 T62 · T160): 화면은 손보기가 걸릴 때까지 빨리감기로 지난다(목차와 같은 길 — <c>PlayWhile(!Rebased)</c>, <c>TutorialMenu.Launch</c>).
        /// 지나는 단계 = 말없는 판 차리기(demo·bot — 대사·hint·앵커·expect 없음)뿐이고, 화면에 뜨는 단계는 모두 대사가 있다 · 확장 장은 처음 뜨는 말풍선이 장 첫 말풍선(「흐음!」).
        /// 막 앞 판이 아닌 각본은 첫 단계부터 뜨니 모든 단계에 대사가 있다.
        /// </summary>
        [Test]
        public void M65_막앞판각본_빨리감기로지나는단계는말없는판차리기뿐_뜨는단계는모두대사가있다()
        {
            int skipped = 0, scripts = 0;
            foreach (var f in ScriptFiles())
            {
                var s = TutorialLoader.Load(File.ReadAllText(f, new System.Text.UTF8Encoding(false)), ctx, "tutorial/" + Path.GetFileName(f));
                int from = 0;
                if (s.Start.After != null)
                {
                    var run = new TutorialRunner(ctx, s, new PrincipleAgent(), TrailAfter);
                    run.PlayWhile(st => !run.Rebased);
                    Assert.IsTrue(run.Rebased, s.Id + ": 손보기가 걸렸다");
                    CollectionAssert.IsEmpty(run.Failures, s.Id + "\n" + string.Join("\n", run.Failures));
                    from = run.Index;
                    scripts++;
                }
                for (int i = 0; i < s.Steps.Count; i++)
                {
                    var st = s.Steps[i];
                    string at = s.Id + "." + st.Id;
                    if (i >= from)
                    {
                        Assert.IsNotNull(st.Text, at + ": 화면에 뜨는 단계에 대사가 없다");
                        continue;
                    }
                    Assert.IsTrue(st.Advance == StepAdvance.Demo || st.Advance == StepAdvance.Bot, $"{at}: 빨리감기로 지나는 단계가 사람 몫({st.Advance})");
                    Assert.IsNull(st.Text, at + ": 빨리감기로 지나는 단계의 대사는 화면에 뜨지 않는다");
                    Assert.IsNull(st.Hint, at);
                    Assert.IsNull(st.Anchor, at + ": 빨리감기로 지나는 단계의 앵커");
                    Assert.AreEqual(0, st.Expect.Count, at + ": 빨리감기로 지나는 단계의 expect(숫자는 뜨는 단계의 대사로)");
                }
                skipped += from;
                if (s.Id.StartsWith("e", StringComparison.Ordinal)) StringAssert.StartsWith("흐음!", Line(s.Steps[from].Text), s.Id + ": 처음 뜨는 말풍선 = 장 첫 말풍선");
            }
            Assert.AreEqual(16, scripts, "막 앞 판 각본(확장 장 14 · 막 맛보기 2)");
            Assert.Greater(skipped, 0);
        }

        static string Line(string key) => ko.TryGet(key, out var t) ? t : null;

        static StringTable en;
        static HashSet<string> enLabels;

        static string LineEn(string key) => en.TryGet(key, out var t) ? t : null;

        // 영어 표의 화면 글(대사 밖) — 대사의 [단추 이름]이 이 가운데 하나여야 한다(07 T211)
        static bool LabelEn(string text)
        {
            if (enLabels == null)
            {
                enLabels = new HashSet<string>();
                foreach (string k in en.Keys)
                    if (!k.StartsWith("tutorial.", StringComparison.Ordinal)) enLabels.Add(en[k]);
            }
            return enLabels.Contains(text);
        }

        /// <summary>
        /// M9 「튜토리얼 잇기」(07 §8 조건 2를 영어에도): 각본 18개의 영어 대사 · hint가 한국어와 같은 검사를 지난다 — 단계마다 <c>&lt;b&gt;숫자&lt;/b&gt;</c> = expect(양쪽) · hint 숫자 ⊂ expect ·
        /// 태그 뺀 <see cref="TutorialDataCheck.MaxLengthEn"/>자 이하 · [단추 이름]은 영어 화면 글 그대로. 영어 대사 키 = 한국어 대사 키.
        /// </summary>
        [Test]
        public void M9_튜토리얼영어_데이터검사_숫자길이단추글이한국어와같은규칙()
        {
            en = en ?? TestData.LoadStrings("en");
            int lines = 0, numbered = 0, scripts = 0;
            foreach (var f in ScriptFiles())
            {
                var s = TutorialLoader.Load(File.ReadAllText(f, new System.Text.UTF8Encoding(false)), ctx, "tutorial/" + Path.GetFileName(f));
                var problems = TutorialDataCheck.Problems(s, ctx, LineEn, english: true, label: LabelEn);
                CollectionAssert.IsEmpty(problems, s.Id + " (en)\n" + string.Join("\n", problems));
                scripts++;
                foreach (var st in s.Steps)
                {
                    if (st.Text != null) lines++;
                    if (st.Hint != null) lines++;
                    if (st.Text != null && st.Expect.Count > 0) numbered++;
                }
            }
            Assert.AreEqual(18, scripts, "각본 18(본편 · 막 맛보기 둘 · 확장 15)");
            CollectionAssert.AreEquivalent(ko.Keys.Where(k => k.StartsWith("tutorial.", StringComparison.Ordinal)), en.Keys.Where(k => k.StartsWith("tutorial.", StringComparison.Ordinal)), "두 언어 대사 키가 같다");
            foreach (string k in en.Keys.Where(k => k.StartsWith("tutorial.", StringComparison.Ordinal)))
                Assert.AreNotEqual(ko[k], en[k], k + ": 한국어 그대로");
            Assert.Greater(numbered, 50, "숫자 대조가 영어에도 도는 단계");
            TestContext.WriteLine($"[M9 튜토리얼 영어] 각본 {scripts} · 대사+hint {lines} · 숫자 대조 단계 {numbered}");
        }

        [Test]
        public void M9_튜토리얼영어_틀린대사를잡는다()
        {
            en = en ?? TestData.LoadStrings("en");
            var main = MainScript();
            CollectionAssert.IsEmpty(TutorialDataCheck.Problems(main, ctx, LineEn, true, LabelEn), "영어 견본은 깨끗하다");
            void Caught(string what, string step, string marker, string key, string text)
            {
                var problems = TutorialDataCheck.Problems(main, ctx, k => k == key ? text : LineEn(k), true, LabelEn);
                Assert.IsTrue(problems.Any(p => p.StartsWith(step + ":", StringComparison.Ordinal) && p.Contains(marker)), $"{what}: {step}·「{marker}」를 잡지 못했다\n{string.Join("\n", problems)}");
            }
            Caught("대사 숫자 ≠ expect", "S301", "<b>", "tutorial.main.S301", "Your first turn! You have <b>4</b> Movement.");
            Caught("expect 값이 대사에 없다", "S302", "<b>", "tutorial.main.S302", "Take one step to the tree next door.");
            Caught("hint 숫자가 expect 밖", "S302", "hint", "tutorial.main.S302.hint", "Click tile <b>5</b>.");
            Caught("말풍선 길이", "S304", "말풍선", "tutorial.main.S304", new string('a', TutorialDataCheck.MaxLengthEn) + "!");
            Caught("없는 단추 글", "S303", "단추", "tutorial.main.S303", "Press [Look Around] at the tree.");
            Caught("대사 키가 표에 없다", "S305", "없다", "tutorial.main.S305", null);
        }

        [Test]
        public void M65_데이터검사_틀린대사를잡는다()
        {
            CollectionAssert.IsEmpty(TutorialDataCheck.Problems(MainScript(), ctx, Line), "견본은 깨끗하다");

            void Caught(string what, string step, string marker, Action<TutorialScript> edit = null, Dictionary<string, string> lines = null)
            {
                var s = MainScript();
                edit?.Invoke(s);
                var problems = TutorialDataCheck.Problems(s, ctx, k => lines != null && lines.TryGetValue(k, out var v) ? v : Line(k));
                Assert.IsTrue(problems.Any(p => p.StartsWith(step + ":", StringComparison.Ordinal) && p.Contains(marker)), $"{what}: {step}·「{marker}」를 잡지 못했다\n{string.Join("\n", problems)}");
            }
            Dictionary<string, string> L(string key, string text) => new Dictionary<string, string> { [key] = text };

            Caught("대사 숫자 ≠ expect", "S301", "<b>", lines: L("tutorial.main.S301", "이번 턴의 이동력은 <b>4</b>이다."));
            Caught("expect 값이 대사에 없다", "S302", "<b>", lines: L("tutorial.main.S302", "옆 칸으로 한 걸음 걸어 보거라."));
            Caught("expect 없는 단계의 <b>숫자</b>", "S001", "<b>", lines: L("tutorial.main.S001", "흐음! 자네는 <b>1</b>번째 여행인가?"));
            Caught("<b> 안이 숫자가 아님", "S301", "<b>", lines: L("tutorial.main.S301", "이번 턴의 이동력은 <b>셋</b>이다. 남은 것은 <b>3</b>이다."));
            Caught("hint 숫자가 expect에 없다", "S302", "hint", lines: L("tutorial.main.S302.hint", "<b>5</b>번 칸을 눌러 보거라."));
            Caught("근거 없음", "S303", "ref", s => s.Steps.Find(x => x.Id == "S303").Ref.Clear());
            Caught("앵커 문법", "S301", "앵커", s => s.Steps.Find(x => x.Id == "S301").Anchor = "Board HUD");
            Caught("보드 밖 칸", "S302", "앵커", s => s.Steps.Find(x => x.Id == "S302").Anchor = "cell:999");
            Caught("해요체", "S301", "말투", lines: L("tutorial.main.S301", "이번 턴의 이동력은 <b>3</b>이에요."));
            Caught("합쇼체", "S302", "말투", lines: L("tutorial.main.S302", "걷고 나면 이동력이 <b>2</b> 남습니다!"));
            Caught("합쇼체 물음", "S303", "말투", lines: L("tutorial.main.S303", "[찾아보기]를 하겠습니까?"));
            Caught("hint 말투", "S101", "말투", lines: L("tutorial.main.S101.hint", "운하시티를 고르세요."));
            Caught("첫 말풍선 밖의 「자네」", "S101", "자네", lines: L("tutorial.main.S101", "자네, 고향 마을을 정하거라."));
            Caught("금지어", "S303", "금지어", lines: L("tutorial.main.S303", "맵에서 [찾아보기]를 하거라."));
            Caught("말풍선 길이", "S304", "말풍선", lines: L("tutorial.main.S304", new string('가', 58) + "구나. 그렇지!"));
            Caught("대사 키가 표에 없다", "S305", "없다", lines: L("tutorial.main.S305", null));
            Caught("모르는 막 앞 판 손보기", "start", "after", s => s.Start.After = "nowhere");
            Caught("봇 답이 bot 단계로 덮이지 않았다", "bots", "좌석 1", s =>
            {
                s.Start.Seats = 2;
                s.Bots = new[] { null, new List<AnswerPattern> { AnswerPattern.Any(), AnswerPattern.Any() } };
                s.Steps.Add(new TutorialStep { Id = "B1", Text = "tutorial.main.S001", Advance = StepAdvance.Bot, BotSeat = 1, BotAnswers = 1, Ref = { "07 §6-4" } });
            });
        }
    }

    /// <summary>
    /// 각본 데이터 검사(07 §1-1 · §3-2 · §8 완료 조건 2): 대사 내용을 엔진 없이 본다. 문제마다 「&lt;단계 id&gt;: …」 한 줄.
    /// <list type="bullet">
    /// <item>근거 `ref`가 비지 않았다 · 대사 키(text·hint)가 문자열 표에 있다</item>
    /// <item>숫자 대조(07 T23): 대사의 &lt;b&gt;숫자&lt;/b&gt; 값 집합 = 그 단계 expect 값 집합, hint의 &lt;b&gt;는 그 부분집합, &lt;b&gt; 안은 정수만</item>
    /// <item>앵커 문법: 요소 이름 · `cell:&lt;n&gt;[,&lt;n&gt;…]`(보드 칸 안) · `ds:&lt;이름&gt;` — 부분 여럿은 `+`로 잇는다(07 T130)</item>
    /// <item>봇 답 덮임: 좌석마다 bot 단계의 botAnswers 합 = 그 좌석 봇 큐 길이 · after 이름을 꽂을 수 있다</item>
    /// <item>말풍선: 태그 뺀 60자 이하(07 T24) · 마박사 말투 — 문장 끝에 해요체·합쇼체(「요」·「니다」·「니까」) 0, 「자네」는 본편 첫 단계만(07 T28) · 화면 금지어(ui-text-guide §5)</item>
    /// </list>
    /// </summary>
    static class TutorialDataCheck
    {
        public const int MaxLength = 60;
        /// <summary>영어 말풍선·hint의 글자 수 한도(태그 뺀 — M9 「튜토리얼 잇기」 [가정 07 T210]): 라틴 글자는 Galmuri11에서 한글의 절반 남짓이라 같은 2줄에 약 1.7배가 든다 — UiShots <c>tut-layer-lines -uiLang en</c>이 실제 줄 수를 센다.</summary>
        public const int MaxLengthEn = 100;
        static readonly Regex Bold = new Regex("<b>(.*?)</b>", RegexOptions.CultureInvariant);
        static readonly Regex Tag = new Regex("<[^>]+>", RegexOptions.CultureInvariant);
        static readonly Regex Sentence = new Regex(@"[^.!?…]+[.!?…]*", RegexOptions.CultureInvariant);
        static readonly Regex Polite = new Regex(@"(요|니다|니까)$", RegexOptions.CultureInvariant);
        static readonly Regex Bracket = new Regex(@"\[([^\]]+)\]", RegexOptions.CultureInvariant);

        /// <summary>
        /// 문제 목록. <paramref name="english"/> = 영어 표의 대사(M9): 숫자 대조 · 근거 · 앵커는 같고, 길이 한도는 <see cref="MaxLengthEn"/>, 말투 검사(해라체 · 「자네」 · 한국어 금지어) 대신
        /// [대괄호] 안 단추 이름이 영어 표의 글(<paramref name="label"/> — 화면에 실제로 있는 단추 글)인지 본다(07 §3-2 ⑤ 「단추는 [대괄호]로 부른다」의 영어판 [가정 T211]).
        /// </summary>
        public static List<string> Problems(TutorialScript s, Ctx ctx, Func<string, string> line, bool english = false, Func<string, bool> label = null)
        {
            var p = new List<string>();
            if (s.Start.After != null && TutorialDataTests.TrailAfter(s.Start.After) == null) p.Add($"start: after 「{s.Start.After}」를 꽂을 막 길이 없다");
            if (s.Bots != null)
                for (int seat = 0; seat < s.Bots.Length; seat++)
                {
                    if (s.Bots[seat] == null) continue;
                    int released = s.Steps.Where(x => x.Advance == StepAdvance.Bot && x.BotSeat == seat).Sum(x => x.BotAnswers);
                    if (released != s.Bots[seat].Count) p.Add($"bots: 좌석 {seat} 봇 큐 {s.Bots[seat].Count}개 ≠ bot 단계가 풀어 주는 답 {released}개");
                }
            for (int i = 0; i < s.Steps.Count; i++)
            {
                var st = s.Steps[i];
                void Add(string msg) => p.Add($"{st.Id}: {msg}");
                if (st.Ref.Count == 0 || st.Ref.Any(string.IsNullOrWhiteSpace)) Add("근거 ref가 없다");
                if (st.Anchor != null && !IsAnchor(st.Anchor, ctx)) Add($"앵커 문법 「{st.Anchor}」");
                if (st.Text == null)
                {
                    // 말없는 단계(07 T160 — 화면이 빨리감기로 지나는 판 차리기): 보일 것이 없다. 빨리감기로 지나는 단계와 같은지는 M65_막앞판각본_… 가 진행기로 본다
                    if (s.Start.After == null || st.Advance != StepAdvance.Demo && st.Advance != StepAdvance.Bot || st.Hint != null || st.Anchor != null || st.Expect.Count > 0)
                        Add("말없는 단계는 막 앞 판 각본의 demo·bot만(hint·anchor·expect 없이)");
                    continue;
                }
                var expect = new SortedSet<int>(st.Expect.Select(e => e.Value));
                string text = line(st.Text);
                if (text == null) { Add($"대사 {st.Text}가 문자열 표에 없다"); continue; }
                var bold = Numbers(text, Add, "대사");
                if (!bold.SetEquals(expect)) Add($"대사 <b>숫자</b> {{{string.Join(",", bold)}}} ≠ expect {{{string.Join(",", expect)}}}");
                int max = english ? MaxLengthEn : MaxLength;
                if (english) Labels(text, label, "대사", Add); else Voice(text, s.Id == "main" && i == 0, "대사", Add);
                int len = Tag.Replace(text, "").Length;
                if (len > max) Add($"말풍선 {len}자 > {max}자");
                if (st.Hint != null)
                {
                    string hint = line(st.Hint);
                    if (hint == null) { Add($"hint {st.Hint}가 문자열 표에 없다"); continue; }
                    var hb = Numbers(hint, Add, "hint");
                    if (!hb.IsSubsetOf(expect)) Add($"hint <b>숫자</b> {{{string.Join(",", hb)}}}가 expect {{{string.Join(",", expect)}}} 밖");
                    if (english) Labels(hint, label, "hint", Add); else Voice(hint, false, "hint", Add);
                    if (Tag.Replace(hint, "").Length > max) Add($"hint {Tag.Replace(hint, "").Length}자 > {max}자");
                }
            }
            return p;
        }

        static SortedSet<int> Numbers(string text, Action<string> add, string what)
        {
            var set = new SortedSet<int>();
            foreach (Match m in Bold.Matches(text))
            {
                if (int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int n)) set.Add(n);
                else add($"{what} <b>{m.Groups[1].Value}</b> — <b> 안은 정수만(expect 값)");
            }
            return set;
        }

        static void Voice(string text, bool first, string what, Action<string> add)
        {
            string plain = Tag.Replace(text, "");
            foreach (Match m in Sentence.Matches(plain))
            {
                string end = m.Value.Trim().TrimEnd('.', '!', '?', '…', '~', ' ', '」', ')', ']');
                if (end.Length > 0 && Polite.IsMatch(end)) add($"{what} 말투 — 해라체가 아니다(「…{end.Substring(Math.Max(0, end.Length - 6))}」, 07 §3-2 ①)");
            }
            if (!first && plain.Contains("자네")) add($"{what} 「자네」는 본편 첫 말풍선만(07 §3-2 ①)");
            foreach (var w in StringTableTests.BannedUiWords)
                if (plain.Contains(w)) add($"{what} 금지어 「{w}」(ui-text-guide §5)");
        }

        // 영어 대사의 [단추 이름]은 화면의 단추 글 그대로(T211)
        static void Labels(string text, Func<string, bool> label, string what, Action<string> add)
        {
            if (label == null) return;
            foreach (Match m in Bracket.Matches(Tag.Replace(text, "")))
                if (!label(m.Groups[1].Value)) add($"{what} [{m.Groups[1].Value}] — 영어 표에 그런 단추 글이 없다");
        }

        // 부분 여럿은 「+」로 잇는다(07 T130) — 부분마다 요소 이름 · cell: · ds:
        static bool IsAnchor(string a, Ctx ctx) => a.Split('+').All(p => IsPart(p, ctx));

        static bool IsPart(string a, Ctx ctx)
        {
            if (a.StartsWith("cell:", StringComparison.Ordinal))
            {
                var cells = a.Substring(5).Split(',');
                return cells.All(c => Regex.IsMatch(c, "^[0-9]+$") && int.TryParse(c, out int n) && n < ctx.Board.Count);
            }
            if (a.StartsWith("ds:", StringComparison.Ordinal)) return TutorialLoader.Name.IsMatch(a.Substring(3));
            return TutorialLoader.Name.IsMatch(a);
        }
    }
}
