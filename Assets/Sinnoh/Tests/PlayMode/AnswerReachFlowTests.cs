using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M9 실행 감사 ② 화면 도달 검사(<see cref="AnswerReach"/>): 길잡이(FlowPilot · SoloPilot)로 솔로 판을 돌리며, 내 선택이 열려 화면이 받을 차례가 될 때마다
    /// 합법 답 하나하나가 지금 화면(또는 한 번 눌러 여는 창)의 누를 수 있는 단추로 나오는지 본다. 「합법 답인데 낼 화면이 없다」는 실패가 아니라 목록이다(고치지 않는다 — 감사 결과 README).
    /// 관문에는 작은 판(찾아 둔 판 셋 · 짧게)과 G1-1(새 휘슬) 장면의 자기 확인, 전체는 [Explicit]: 카드 51장을 골드로 쥔 판 → <c>Logs/Review/m9/exec-audit/reach.tsv</c>(git 밖).
    /// </summary>
    public abstract class AnswerReachBase
    {
        protected GameFlow flow;
        protected readonly List<string> errors = new List<string>();

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
        }

        RenderTexture rt;

        protected IEnumerator Boot()
        {
            errors.Clear();
            Application.logMessageReceived -= Grab;
            Application.logMessageReceived += Grab;
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            ScreenBoot boot = null;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            flow = boot.Flow;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            var title = (TitleScreen)boot.Root.Stage.Current;
            int solo = -1;
            for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Solo) solo = i;
            Assert.That(title.Activate(solo), Is.True);
            yield return null;
        }

        [TearDown]
        public void Shut()
        {
            Application.logMessageReceived -= Grab;
            var b = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                rt = null;
            }
        }

        /// <summary>판을 지나며 본 것: 본 선택 수 · 덮지 않는 선택 수 · 「누를 데 없음」(까닭 + 선택 꼴 + 답 → 횟수 · 첫 장면).</summary>
        protected sealed class Survey
        {
            public int Checked, Uncovered, Answers;
            public readonly List<string> Errors = new List<string>();
            public readonly Dictionary<string, int> UncoveredKinds = new Dictionary<string, int>();
            public readonly Dictionary<string, (int Count, string First)> Missing = new Dictionary<string, (int, string)>();
            /// <summary>판 동안 한 번도 「받을 차례」가 되지 않은 선택(꼴 → 수) — 받는 화면이 끝내 안 섰거나 검사가 기다리는 조건을 못 본 것.</summary>
            public readonly Dictionary<string, int> NeverReady = new Dictionary<string, int>();
            /// <summary>본 선택의 꼴마다(덮은 범위 표): 본 수 · 합법 답 · 못 누름.</summary>
            public readonly SortedDictionary<string, (int Seen, int Legal, int Missing)> Kinds = new SortedDictionary<string, (int, int, int)>(StringComparer.Ordinal);
        }

        /// <summary>검사 결과 하나를 판 기록에 더한다.</summary>
        protected static void Record(Survey sv, string label, PendingChoice c, AnswerReach.Result r, Sinnoh.Rules.Session.SeatClient client)
        {
            sv.Checked++;
            sv.Answers += r.Legal;
            string kind = $"{c.Kind}/{c.Sub} @{r.Where ?? "덮지 않음"}";
            (int Seen, int Legal, int Missing) was = sv.Kinds.TryGetValue(kind, out var w) ? w : (0, 0, 0);
            sv.Kinds[kind] = (was.Seen + 1, was.Legal + r.Legal, was.Missing + r.Missing.Count);
            if (r.Uncovered)
            {
                sv.Uncovered++;
                string k = $"{c.Kind}/{c.Sub}";
                sv.UncoveredKinds[k] = (sv.UncoveredKinds.TryGetValue(k, out int n) ? n : 0) + 1;
            }
            foreach (var (a, why) in r.Missing)
            {
                string key = $"{why}\t{c.Kind}/{c.Sub}\t{a.Kind}";
                string at = $"{label} · 라운드 {client.View.Round} · 칸 {client.View.Seats[client.Seat].Cell} · 답 {a}";
                sv.Missing[key] = sv.Missing.TryGetValue(key, out var m) ? (m.Count + 1, m.First) : (1, at);
            }
        }

        /// <summary>판 한 벌: 시작 → (손보기) → 걸음마다 검사 → 길잡이 한 걸음. steps 걸음까지.</summary>
        protected IEnumerator Run(SoloTrail trail, Survey sv, int steps, string label, Action<Ctx, GameState> rebase = null)
        {
            SoloPilot pilot;
            try
            {
                pilot = FlowPilot.Start(flow, trail);
                if (rebase != null) flow.Rebase(s => rebase(flow.Ctx, s));
            }
            catch (Exception e)
            {
                sv.Errors.Add($"{label}: 시작 못 함 — {e.GetType().Name}: {e.Message}");
                yield break;
            }
            var done = new HashSet<int>();
            var waited = new Dictionary<int, string>();
            for (int i = 0; i < steps; i++)
            {
                flow.Skip(500);
                if (i % 4 == 0) yield return null;
                try
                {
                    var client = flow.Client;
                    if (client == null || client.View == null || client.View.Phase == Phase.GameOver) break;
                    var c = client.FirstChoice();
                    if (c != null && !done.Contains(c.Id))
                    {
                        var r = AnswerReach.Check(flow, c);
                        if (r.NotReady) waited[c.Id] = $"{c.Kind}/{c.Sub}";
                        else
                        {
                            done.Add(c.Id);
                            Record(sv, label, c, r, client);
                        }
                    }
                    FlowPilot.Step(flow, pilot);
                }
                catch (Exception e)
                {
                    sv.Errors.Add($"{label}: 걸음 {i} — {e.GetType().Name}: {e.Message}");
                    break;
                }
            }
            foreach (var kv in waited)
                if (!done.Contains(kv.Key)) sv.NeverReady[kv.Value] = (sv.NeverReady.TryGetValue(kv.Value, out int n) ? n : 0) + 1;
        }

        protected static string Report(Survey sv)
        {
            var sb = new StringBuilder();
            sb.Append("why\tchoice\tanswer\tcount\tfirst\n");
            foreach (var kv in sv.Missing) sb.Append($"{kv.Key}\t{kv.Value.Count}\t{kv.Value.First}\n");
            sb.Append($"# 본 선택 {sv.Checked} · 본 합법 답 {sv.Answers} · 덮지 않는 선택 {sv.Uncovered}:");
            foreach (var kv in sv.UncoveredKinds) sb.Append($" {kv.Key} {kv.Value}");
            sb.Append('\n');
            sb.Append("# 받을 차례를 한 번도 못 본 선택:");
            foreach (var kv in sv.NeverReady) sb.Append($" {kv.Key} {kv.Value}");
            sb.Append('\n');
            sb.Append("# 꼴마다(본 선택 · 합법 답 · 못 누름):");
            foreach (var kv in sv.Kinds) sb.Append($" {kv.Key} {kv.Value.Seen}·{kv.Value.Legal}·{kv.Value.Missing}");
            sb.Append('\n');
            foreach (var e in sv.Errors) sb.Append("# 오류 ").Append(e).Append('\n');
            return sb.ToString();
        }

    }

    /// <summary>화면 도달 검사의 관문 시험(찾아 둔 판 하나 · G1-1 자기 확인). 판 돌리기는 <see cref="AnswerReachBase"/>.</summary>
    public sealed class AnswerReachFlowTests : AnswerReachBase
    {
        [UnityTest]
        public IEnumerator M9_실행감사_화면도달_찾아둔판셋을돌며합법답마다단추를본다()
        {
            // 관문용: 격파 · 제작 · 사당 판을 짧게 — 검사가 예외 없이 돌고, 선택을 실제로 보고, 에러 로그 0(「누를 데 없음」은 목록일 뿐 실패가 아니다)
            yield return Boot();
            var sv = new Survey();
            yield return Run(SoloTrail.Defeat, sv, 160, "defeat");
            foreach (var line in Report(sv).Split('\n')) TestContext.WriteLine(line);
            Assert.That(sv.Checked, Is.GreaterThan(5), "선택을 봤다");
            Assert.That(sv.Answers, Is.GreaterThan(sv.Checked), "합법 답을 셌다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M9_실행감사_화면도달_창갈래_사당판과종막창이받는선택을본다()
        {
            // 창 갈래(AnswerReach.Screens): 사당 판(카드 고르기 · 규토리 · 스튜 · 교환 · 추종자)과 종막 창이 받는 선택을 실제로 보고(덮지 않음 0), 에러 로그 0
            yield return Boot();
            var sv = new Survey();
            yield return Run(SoloTrail.Shrine, sv, 160, "shrine");
            yield return Boot();
            yield return Run(SoloTrail.Ending, sv, 120, "ending");
            foreach (var line in Report(sv).Split('\n')) TestContext.WriteLine(line);
            Assert.That(sv.Errors, Is.Empty, string.Join("\n", sv.Errors));
            Assert.That(sv.Uncovered, Is.EqualTo(0), "덮지 않는 선택");
            bool Saw(string where) { foreach (var k in sv.Kinds.Keys) if (k.EndsWith("@" + where, StringComparison.Ordinal)) return true; return false; }
            Assert.That(Saw("사당 판"), Is.True, "사당 판이 받는 선택을 봤다");
            Assert.That(Saw("종막 창"), Is.True, "종막 창이 받는 선택을 봤다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M9_실행감사_화면도달_염력원격공물_떨어진사당판이7점더까지낸다_X3()
        {
            // 앞 전체 판 X3(psychic 골드 · 격파 판 첫 턴: 원격 공물 답이 능력 고르기 1/4쪽 밖에 숨음 80회 — 플레이 시험 「JP 7 더」 꼴). 11-20 main(9f7ee644)에서 다시 보니
            // 그 칸의 일이 공물뿐이면 칸 누르기가 떨어진 사당 판을 연다(T33) — 검사가 그 길을 따라 판이 안쪽 답(색 · 7점 더)을 내는지 보고, 「7점 더」 답을 실제로 누른다
            yield return Boot();
            FlowPilot.Start(flow, SoloTrail.Defeat);
            flow.Rebase(s =>
            {
                GiveCard(flow.Ctx, s, "psychic", TrainerRules.Gold);
                for (int c = 0; c < s.Seats[0].Apricorns.Length; c++) s.Seats[0].Apricorns[c] = 3;
                s.Seats[0].Jp = 20;
            });
            PendingChoice choice = null;
            for (int i = 0; i < 200 && (choice == null || !flow.InputOpen); i++)
            {
                flow.Skip(500);
                yield return null;
                choice = flow.Client.Choice(ChoiceKind.MapAction);
            }
            Assert.That(choice, Is.Not.Null, "내 맵 행동");
            var legal = flow.Client.Legal(choice);
            var reach = legal.FindAll(a => ShrineScreen.IsReachOffer(a, a.Target));
            var extra = reach.Find(a => TrainerRules.ReachInner(a).Values.Count > 0);
            Assert.That(extra, Is.Not.Null, "엔진은 떨어진 사당에 여행 점수 7을 더 바치는 염력 답을 낸다");
            var r = AnswerReach.Check(flow, choice);
            Assert.That(r.NotReady || r.Uncovered, Is.False);
            var miss = r.Missing.FindAll(m => m.Answer.Kind == AnswerKind.UseAbility);
            foreach (var m in miss) TestContext.WriteLine($"[X3] {m.Answer} — {m.Why}");
            Assert.That(miss, Is.Empty, "염력 답이 모두 누를 데가 있다(검사가 창을 열고 닫은 뒤에도 — 다른 단추 판정이 흔들리지 않는다)");
            Assert.That(r.Missing, Is.Empty, "같은 선택의 다른 답도");
            Assert.That(flow.Stage.Current, Is.Null, "검사가 연 창은 닫혔다");
            Assert.That(FlowPilot.Press(flow, choice, extra), Is.True, "「7점 더」 공물을 화면으로 실제로 낸다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        /// <summary>seat 0의 카드를 바꾼다(덱의 그 카드와 맞바꿔 카드 수를 지킨다) · 등급.</summary>
        internal static void GiveCard(Ctx ctx, GameState s, string key, int tier)
        {
            var st = s.Seats[0];
            int card = ctx.Cards.TrainerCardIds.IndexOf(key);
            var deck = s.Decks[(int)DeckId.TrainerCard];
            int at = deck.IndexOf(card);
            if (at >= 0 && st.TrainerCard >= 0) deck[at] = st.TrainerCard;
            st.TrainerCard = card;
            st.TrainerCardTier = tier;
        }

        /// <summary>그 칸 둘레에서 장소(마을 · 야영지 · 사당 …)가 없는 이웃 칸.</summary>
        internal static int PlainNeighbor(Ctx ctx, int cell)
        {
            for (int d = 0; d < Board.Directions; d++)
            {
                int nb = ctx.Board.Neighbor(cell, d);
                if (nb == Board.NoCell) continue;
                bool taken = false;
                foreach (var f in ctx.Fixtures.Fixtures) taken |= f.Cell == nb;
                if (!taken) return nb;
            }
            return -1;
        }

        [UnityTest]
        public IEnumerator M9_실행감사_화면도달_새휘슬공중날기_검사판정과실제누르기가같다_G1_1()
        {
            // 읽기 감사 G1-1: bird_keeper 파랑(실버) · 규토리 · 마을이 아닌 칸 → 엔진은 Fly 답을 낸다. 검사가 「누를 데 없음」이라 하면 FlowPilot.Press도 못 눌러야 하고, 단추가 있다 하면 눌려야 한다
            // (고친 뒤에도 이 시험은 그대로 — 검사 도구가 화면과 같은 판정을 내는지 본다 · 062156cc에서 고쳐 이제 둘 다 「눌린다」). 고치기 전(3fd4a7f9)은 「[마을] 단추 없음」이 나와야 한다 — 그 확인은 보고 줄로
            yield return Boot();
            FlowPilot.Start(flow, SoloTrail.Defeat);
            flow.Rebase(s =>
            {
                GiveCard(flow.Ctx, s, "bird_keeper", TrainerRules.Silver);
                for (int c = 0; c < s.Seats[0].Apricorns.Length; c++) s.Seats[0].Apricorns[c] = 2;
                int plain = PlainNeighbor(flow.Ctx, s.Seats[0].Cell);
                Assert.That(plain, Is.GreaterThanOrEqualTo(0), "장소 없는 이웃 칸");
                s.Seats[0].Cell = plain;
            });
            PendingChoice choice = null;
            for (int i = 0; i < 200 && (choice == null || !flow.InputOpen); i++)
            {
                flow.Skip(500);
                yield return null;
                choice = flow.Client.Choice(ChoiceKind.MapAction);
            }
            Assert.That(choice, Is.Not.Null, "내 맵 행동");
            var fly = flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.Fly);
            Assert.That(fly, Is.Not.Null, "엔진은 마을 밖에서도 새 휘슬 Fly 답을 낸다(MapActions)");
            var r = AnswerReach.Check(flow, choice);
            Assert.That(r.NotReady || r.Uncovered, Is.False);
            var miss = r.Missing.Find(m => m.Answer.SameAs(fly));
            TestContext.WriteLine($"[G1-1] 검사: {(miss.Answer != null ? "누를 데 없음 — " + miss.Why : "단추 있음")}");
            // T18 ①: 새 휘슬 창은 처음부터 보드에서 고르는 꼴(오른쪽 기둥 · 가림막 없음 · [취소] 없이 [닫기]) — 그 Fly 답의 마을이 칠해지고, Esc 한 번 = 닫기 · 카메라는 연 자리로
            var stage = flow.Stage;
            var look = flow.SaveLook();
            Assert.That(flow.OpenWhistle(), Is.True);
            yield return null;
            var whistle = (VillageScreen)stage.Current;
            Assert.That(whistle.Whistle && whistle.Picking, Is.True, "새 휘슬 창 = 보드에서 고르기");
            Assert.That(flow.FlyPick, Is.SameAs(whistle));
            Assert.That(whistle.EndMap(), Is.False, "새 휘슬 창은 돌아갈 창 꼴이 없다");
            Assert.That(whistle.BeginMap(), Is.False);
            Assert.That(stage.Root.Q("board-village-scrim").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "가림막 없음");
            Assert.That(stage.Root.Q("board-village-cancel"), Is.Null, "[취소] 없음");
            Assert.That(stage.Root.Q("board-village-close"), Is.Not.Null, "[닫기]");
            Assert.That(whistle.Lit, Does.Contain(flow.Ctx.Fixtures.Fixtures[fly.Target].Cell), "그 Fly 답의 마을이 칠해졌다");
            Assert.That(flow.Escape(), Is.True, "Esc");
            yield return null;
            Assert.That(stage.Current, Is.Null, "Esc 한 번 = 닫기");
            Assert.That(flow.BoardMarks.Dims.Count, Is.EqualTo(0), "보드 칠하기를 걷었다");
            var after = flow.SaveLook();
            Assert.That((after.Zoom, after.Target), Is.EqualTo((look.Zoom, look.Target)), "카메라는 연 자리로");
            bool pressed = FlowPilot.Press(flow, choice, fly);
            Assert.That(pressed, Is.EqualTo(miss.Answer == null), "검사 판정 = 실제로 화면 함수로 눌리나");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M9_실행감사_화면도달_배낭여행자회복은결과창확인뒤보드카드물음이받는다_X6()
        {
            // 전체 판 X6(backpacker 골드 · 격파 판 첫 배틀 뒤 「배틀 화면에 없음(Reaction/EffectPick) Pick」 2회): 그 선택 = backpacker B 「After any battle, fully restore a Pokémon」 —
            // BattleFlow.Close 처음의 강제 고르기(Reaction/EffectPick, 출처 = 카드, Count 1, 「안 함」 없음 — rulings A38). 엔진은 이미 배틀을 끝냈고(Ended), 결과 창은 배틀 뒤 보상만 받는다(BattleScreen.Refresh).
            // [확인](GameFlow.CloseBattle — Reward가 남지 않았으면 닫힌다) 뒤 보드의 카드 물음 단추(BoardHud — 합법 답 그대로)가 받는다 → 검사의 빈틈이었다(화면 버그 아님). 이 시험이 그 길을 실제로 누른다
            yield return Boot();
            var pilot = FlowPilot.Start(flow, SoloTrail.Defeat);
            int card = flow.Ctx.Cards.TrainerCardIds.IndexOf("backpacker");
            flow.Rebase(s =>
            {
                GiveCard(flow.Ctx, s, "backpacker", TrainerRules.Gold);
                for (int c = 0; c < s.Seats[0].Apricorns.Length; c++) s.Seats[0].Apricorns[c] = 3;
                s.Seats[0].Jp = 20;
            });
            PendingChoice tent = null;
            for (int i = 0; i < 400 && tent == null; i++)
            {
                flow.Skip(500);
                if (i % 4 == 0) yield return null;
                var c = flow.Client.FirstChoice();
                if (c != null && c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.EffectPick && c.Source.Kind == SourceKind.TrainerCard && c.Source.Id == card) { tent = c; break; }
                FlowPilot.Step(flow, pilot);
            }
            Assert.That(tent, Is.Not.Null, "격파 판 첫 배틀 뒤 backpacker B 회복 물음");
            for (int i = 0; i < 200 && !flow.BattleOver; i++)
            {
                flow.Skip(500);
                yield return null;
            }
            var battle = flow.Stage.Current as BattleScreen;
            Assert.That(battle, Is.Not.Null, "결과 창(배틀 화면)이 떠 있다");
            Assert.That(flow.BattleOver, Is.True);
            var legal = flow.Client.Legal(tent);
            Assert.That(legal.Exists(a => a.Kind == AnswerKind.Decline), Is.False, "강제 고르기 — 안 함 없음");
            Assert.That(battle.Picks.Count, Is.EqualTo(0), "결과 창은 이 선택을 받지 않는다(끝난 배틀 — 배틀 뒤 보상만)");
            var before = AnswerReach.Check(flow, tent);
            Assert.That(before.NotReady, Is.True, "검사: 결과 창 [확인] 뒤에 받는다 — 여기서는 받을 차례가 아님(전에는 「배틀 화면에 없음」)");
            Assert.That(battle.Confirm(), Is.True, "[확인]이 눌린다(남은 보상 없음)");
            for (int i = 0; i < 60 && !(flow.Hud != null && flow.Hud.CardAsking && flow.InputOpen); i++)
            {
                flow.Skip(500);
                yield return null;
            }
            Assert.That(flow.Hud.CardAsking, Is.True, "보드의 카드 물음");
            var shown = new List<ChoiceAnswer>(flow.Hud.CardAnswers);
            foreach (var a in legal) Assert.That(shown.Exists(x => x.SameAs(a)), Is.True, "보드 카드 물음 단추에 " + a);
            var after = AnswerReach.Check(flow, tent);
            Assert.That(after.NotReady || after.Uncovered, Is.False);
            Assert.That(after.Missing, Is.Empty, "검사: 단추가 모두 있다");
            Assert.That(FlowPilot.Press(flow, tent, legal[0]), Is.True, "보드 단추로 실제로 낸다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

    }

    /// <summary>화면 도달 검사 전체(손으로 — 클래스가 따로라 관문의 <c>--playmode "AnswerReachFlowTests"</c>에 걸리지 않는다).</summary>
    public sealed class AnswerReachSweepTests : AnswerReachBase
    {
        [UnityTest, Explicit("오래 걸린다 — 손으로 돌리는 실행 감사"), Timeout(3600000)]
        public IEnumerator M9_실행감사_화면도달_전체()
        {
            // 찾아 둔 판 여럿 + 카드 51장(골드)을 하나씩 쥔 격파 판 → reach.tsv
            var sv = new Survey();
            // 창 갈래(사당 · 균열 · 종막 · 카드 바꾸기 · UB 축복 · 균열 카드)에 닿는 판도 — 11-20 넓히기
            foreach (var trail in new[]
            {
                SoloTrail.Catch, SoloTrail.Kit, SoloTrail.Work, SoloTrail.Shrine, SoloTrail.Fate, SoloTrail.Swarm, SoloTrail.Market,
                SoloTrail.Coral, SoloTrail.Two, SoloTrail.Workshop, SoloTrail.Lake, SoloTrail.Heart, SoloTrail.Ending, SoloTrail.Duo, SoloTrail.FateGiratina,
                SoloTrail.ShrineLegend, SoloTrail.Fracture, SoloTrail.Stamp, SoloTrail.CardSwap, SoloTrail.UbBoard, SoloTrail.UbBattle, SoloTrail.UbTrial,
                // Duel은 두 좌석 판(방 — PrepareDuel이 좌석 1을 쓴다)이라 솔로 길잡이로는 시작하지 못해 뺀다
            })
            {
                yield return Boot();
                yield return Run(trail, sv, 400, trail.Name);
                Save(sv);
            }
            // 진화 분기 · 옛 기술(봇 길이 이브이를 진화시키지 않는다): 작업 판에서 [진화] → 이브이 → 갈래 → 첫 갈래 → (옛 기술)
            yield return Boot();
            yield return EvolveScene(sv);
            Save(sv);
            // 울트라 버스트 판(마우스 확인 판 — 첫 라운드 끝 축복 물음 → UB 턴 뒤 균열 카드 창): 내 턴은 바로 [턴 종료]
            yield return Boot();
            MouseProbe.StartPeekGame(flow, 20261112, 7);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            yield return Drive(sv, "scene ub-peek", 600, null, null);
            Save(sv);
            // 종막에서 배신자를 고른다(봇 길은 영웅) → 배신자 순서(S63)
            yield return Boot();
            var ending = FlowPilot.Start(flow, SoloTrail.Ending);
            yield return Drive(sv, "scene ending-traitor", 400, ending, (c, legal) => c.Kind == ChoiceKind.Ending && c.Sub == ChoiceSub.EndingChoice ? legal.Find(a => a.Kind == AnswerKind.Pick && a.Target == 1) : null);
            Save(sv);
            for (int k = 0; k < flow.Ctx.Cards.TrainerCards.Count; k++)
            {
                string key = flow.Ctx.Cards.TrainerCards[k].Key;
                yield return Boot();
                yield return Run(SoloTrail.Defeat, sv, 200, "card " + key, (ctx, s) =>
                {
                    AnswerReachFlowTests.GiveCard(ctx, s, key, TrainerRules.Gold);
                    for (int c = 0; c < s.Seats[0].Apricorns.Length; c++) s.Seats[0].Apricorns[c] = 3;
                    s.Seats[0].Jp = 20;
                });
                Save(sv);
            }
            foreach (var line in Report(sv).Split('\n')) TestContext.WriteLine(line);
            Assert.That(sv.Checked, Is.GreaterThan(0));
        }

        /// <summary>
        /// 장면 판을 돌린다: 내 선택마다 검사 → 답(pick이 고르면 그 답, 아니면 길잡이 한 걸음, 길잡이가 없으면 맵 행동 = [턴 종료] · 그 밖 = 첫 합법 답 — 세션에 바로).
        /// 검사가 받을 차례를 못 본 선택은 40걸음 기다린 뒤 그냥 답한다(받을 차례를 한 번도 못 본 선택으로 남는다).
        /// </summary>
        IEnumerator Drive(Survey sv, string label, int steps, SoloPilot pilot, Func<PendingChoice, List<ChoiceAnswer>, ChoiceAnswer> pick)
        {
            var done = new HashSet<int>();
            var waited = new Dictionary<int, string>();
            int stuck = 0, last = -1;
            for (int i = 0; i < steps; i++)
            {
                flow.Skip(300);
                if (i % 2 == 0) yield return null;
                try
                {
                    var client = flow.Client;
                    if (client == null || client.View == null || client.View.Phase == Phase.GameOver) break;
                    var c = client.FirstChoice();
                    if (c == null) continue;
                    if (!done.Contains(c.Id))
                    {
                        var r = AnswerReach.Check(flow, c);
                        if (!r.NotReady) { done.Add(c.Id); Record(sv, label, c, r, client); }
                        else waited[c.Id] = $"{c.Kind}/{c.Sub}";
                    }
                    stuck = c.Id == last ? stuck + 1 : 0;
                    last = c.Id;
                    if (!done.Contains(c.Id) && stuck < 40) continue;
                    if (!flow.AnswerOpen) continue;
                    var legal = client.Legal(c);
                    var a = pick != null ? pick(c, legal) : null;
                    if (a == null && pilot != null) { FlowPilot.Step(flow, pilot); continue; }
                    if (a == null) a = c.Kind == ChoiceKind.MapAction ? legal.Find(x => x.Kind == AnswerKind.EndTurn && x.Target < 0) ?? legal[0] : legal[0];
                    flow.Answer(c, a);
                }
                catch (Exception e)
                {
                    sv.Errors.Add($"{label}: 걸음 {i} — {e.GetType().Name}: {e.Message}");
                    break;
                }
            }
            foreach (var kv in waited)
                if (!done.Contains(kv.Key)) sv.NeverReady[kv.Value] = (sv.NeverReady.TryGetValue(kv.Value, out int n) ? n : 0) + 1;
        }

        IEnumerator EvolveScene(Survey sv)
        {
            const string label = "scene evolve-branch";
            FlowPilot.Start(flow, SoloTrail.Work);
            yield return null;
            flow.Skip(flow.OpeningMs);
            for (int i = 0; i < 200 && !flow.InputOpen; i++) flow.Skip(100);
            if (!flow.InputOpen || !flow.Hud.Press(BoardHud.Evolve)) { sv.Errors.Add(label + ": [진화] 못 누름"); yield break; }
            yield return null;
            var evolve = flow.Stage.Current as EvolveScreen;
            if (evolve == null || evolve.Listed.Count == 0 || !evolve.PressEvolve(evolve.Listed[0])) { sv.Errors.Add(label + ": 진화 창 · 이브이 못 누름"); yield break; }
            yield return null;
            var seen = new HashSet<int>();
            for (int i = 0; i < 120 && flow.Stage.Current == evolve; i++)
            {
                flow.Skip(100);
                if (i % 4 == 0) yield return null;
                var c = flow.Client.FirstChoice();
                if (c == null || !EvolveScreen.Owns(c) || seen.Contains(c.Id)) continue;
                var r = AnswerReach.Check(flow, c);
                if (r.NotReady) continue;
                seen.Add(c.Id);
                Record(sv, label, c, r, flow.Client);
                bool pressed = c.Sub == ChoiceSub.EvolveBranch ? evolve.PressBranch(0) : evolve.PressTeach(-1);
                if (!pressed) { sv.Errors.Add($"{label}: {c.Sub} 못 누름"); yield break; }
            }
            if (seen.Count == 0) sv.Errors.Add(label + ": 갈래 선택을 못 봄");
        }

        /// <summary>판마다 reach.tsv를 다시 쓴다(중간에 멈춰도 그때까지의 표가 남는다).</summary>
        static void Save(Survey sv)
        {
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Review", "m9", "exec-audit");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "reach.tsv"), Report(sv), new UTF8Encoding(false));
        }
    }
}
