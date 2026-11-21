using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using Sinnoh.Rules.Tutorial;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6.5 각본 튜토리얼 엔진 뼈대(07 §6·§8): 정해진 판(TutorialDeal) · 주사위 대본(ForcedDice) · 명령 거르개 · 봇 각본 큐 · 단계 모형(expect 대조).
    /// 판: 시드 650 혼자 하기 — 캐널브 마을(고정물 0, 칸 33)에서 시작해 이웃 칸 32의 규토리 나무에서 찾아보기.
    /// </summary>
    public class TutorialTests
    {
        const ulong Seed = 650;
        const int Village = 0;   // village.canalave @ 33
        const int Tree = 32;     // 캐널브 옆 규토리 나무
        static readonly string[] Cards = { "hiker", "picnicker", "ace_trainer" };

        static Ctx ctx;

        [OneTimeSetUp]
        public void Load() => ctx = TestData.LoadCtx();

        static LocalSession Solo(TutorialDeal deal) =>
            LocalSession.Start(ctx, new RuleOptions(), 1, Seed, Campaign.HeartOfSinnoh, new SessionConfig { VirtualAgent = new PrincipleAgent(), Rig = deal }, 0);

        /// <summary>셋업을 같은 답으로 끝낸다: 마을 0 · 첫 스타터 · 첫 트레이너 카드.</summary>
        static void Setup(LocalSession session)
        {
            var me = new SeatClient(session.Link, ctx, 0);
            for (int guard = 0; me.View.Phase == Phase.Setup; guard++)
            {
                Assert.Less(guard, 20);
                var c = me.FirstChoice();
                var a = c.Kind == ChoiceKind.VillagePick ? ChoiceAnswer.Pick(Village) : me.Legal(c)[0];
                Assert.IsTrue(me.Answer(c, a, 0).Accepted, c.Kind.ToString());
            }
            me.Close();
        }

        static List<int> Sorted(List<int> pile)
        {
            var l = new List<int>(pile);
            l.Sort();
            return l;
        }

        static int CardId(string key) => ctx.Cards.TrainerCardIds.IndexOf(key);

        [Test]
        public void M65_정해진판_트레이너카드와숨겨진장소가지켜지고_나머지는같은시드판과같다()
        {
            // 평소 판에서 시드가 칸 48에 놓는 정체와 다른 것을 고른다 — 비밀의 열쇠(유물)
            var plain = Solo(null);
            Setup(plain);
            var p = plain.HostCopy();
            var at48 = p.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && t.Cell == 48);
            Assert.IsNotNull(at48, "시드 650은 칸 48에 숨겨진 장소를 놓는다");
            string want = "item.secret_key";
            Assert.AreNotEqual(want, ctx.Items.Items[at48.Def].Key);

            var deal = new TutorialDeal(ctx).TrainerCards(Cards).HiddenSpot(48, want);
            var rigged = Solo(deal);
            var r0 = rigged.HostCopy().Seats[0];
            CollectionAssert.AreEqual(new[] { CardId("hiker"), CardId("picnicker"), CardId("ace_trainer") }, r0.TrainerCardOffer, "받은 3장 = 지정 순서");
            Setup(rigged);
            var r = rigged.HostCopy();

            Assert.AreEqual(CardId("hiker"), r.Seats[0].TrainerCard, "첫 카드를 골랐다");
            var spot = r.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && t.Cell == 48);
            Assert.AreEqual(want, ctx.Items.Items[spot.Def].Key, "칸 48의 정체");
            Assert.AreEqual(ctx.Items.Items[spot.Def].Kind != ItemKind.HiddenSpot, spot.Special, "금색 ? 여부도 정체를 따른다");

            // 섞기·굴림의 난수 소비가 같다 → 시드가 정한 나머지(난수 상태 · 놓인 칸 · 덱 구성 · 말 자리)는 같은 시드 판과 같다
            Assert.AreEqual(p.Rng.State, r.Rng.State, "난수 소비량이 같다");
            CollectionAssert.AreEqual(Cells(p), Cells(r), "숨겨진 장소가 놓인 칸");
            for (int d = 0; d < GameState.DeckCount; d++)
            {
                var dp = new List<int>(p.Decks[d]);
                var dr = new List<int>(r.Decks[d]);
                if (d == (int)DeckId.TrainerCard) { dp.Add(p.Seats[0].TrainerCard); dr.Add(r.Seats[0].TrainerCard); } // 고른 카드만 다르다
                CollectionAssert.AreEqual(Sorted(dp), Sorted(dr), $"{(DeckId)d} 구성");
            }
            Assert.AreEqual(p.Seats[0].Cell, r.Seats[0].Cell);
            var defsP = new List<int>();
            var defsR = new List<int>();
            foreach (var t in p.Tokens) if (t.Kind == TokenKind.HiddenSpot) defsP.Add(t.Def);
            foreach (var t in r.Tokens) if (t.Kind == TokenKind.HiddenSpot) defsR.Add(t.Def);
            CollectionAssert.AreEquivalent(defsP, defsR, "토큰 정체는 맞바꿨을 뿐이다");
        }

        static List<int> Cells(GameState s)
        {
            var l = new List<int>();
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.HiddenSpot && t.Cell >= 0) l.Add(t.Cell);
            l.Sort();
            return l;
        }

        [Test]
        public void M65_정해진판_없는키_두번쓴카드_거절()
        {
            Assert.Throws<ArgumentException>(() => new TutorialDeal(ctx).Top(DeckId.Weak, "pokemon.no_such"), "없는 포켓몬 키");
            Assert.Throws<ArgumentException>(() => new TutorialDeal(ctx).TrainerCards(new[] { "hiker", "hiker", "picnicker" }), "한 장뿐인 카드를 두 번");
            Assert.Throws<ArgumentException>(() => new TutorialDeal(ctx).TrainerCards(new[] { "hiker", "picnicker" }), "3장이 아니다");
            Assert.Throws<ArgumentException>(() => new TutorialDeal(ctx).Top(DeckId.Weak, Weakest(1)[0]).Top(DeckId.Strong, Weakest(1)[0]), "그 덱의 카드가 아니다");
            Assert.Throws<ArgumentException>(() => new TutorialDeal(ctx).HiddenSpot(48, "item.no_such"), "없는 아이템 키");
            Assert.Throws<ArgumentException>(() => new TutorialDeal(ctx).HiddenSpot(48, "item.secret_key").HiddenSpot(48, "item.odd_keystone"), "한 칸을 두 번");
            Assert.Throws<ArgumentException>(() => new TutorialDeal(ctx).HiddenSpot(48, "item.secret_key").HiddenSpot(49, "item.secret_key"), "1개뿐인 토큰을 두 칸에");
        }

        [Test]
        public void M65_덱윗장_섞을때마다지정카드가맨위_난수소비는같다()
        {
            string[] top = Weakest(2);
            var deal = new TutorialDeal(ctx).Top(DeckId.Weak, top);
            GameState a, b;
            using (Rig.Use(deal))
            {
                a = Engine.NewGame(ctx, new RuleOptions(), 1, Seed);
                deal.Attach(a);
            }
            b = Engine.NewGame(ctx, new RuleOptions(), 1, Seed);
            var weak = a.Decks[(int)DeckId.Weak];
            Assert.AreEqual(ctx.Cards.PokemonIds.IndexOf(top[0]), weak[weak.Count - 1], "붙을 때 맨 위 = 첫째");
            Assert.AreEqual(ctx.Cards.PokemonIds.IndexOf(top[1]), weak[weak.Count - 2]);
            using (Rig.Use(deal)) Piles.Shuffle(weak, a.Rng);
            Piles.Shuffle(b.Decks[(int)DeckId.Weak], b.Rng);
            Assert.AreEqual(ctx.Cards.PokemonIds.IndexOf(top[0]), weak[weak.Count - 1], "다시 섞어도 맨 위");
            Assert.AreEqual(b.Rng.State, a.Rng.State, "섞기 난수 소비가 같다");
            CollectionAssert.AreEqual(Sorted(b.Decks[(int)DeckId.Weak]), Sorted(weak));
            using (Rig.Use(deal)) Piles.Shuffle(a.Decks[(int)DeckId.Strong], a.Rng);
            Piles.Shuffle(b.Decks[(int)DeckId.Strong], b.Rng);
            CollectionAssert.AreEqual(b.Decks[(int)DeckId.Strong], a.Decks[(int)DeckId.Strong], "정하지 않은 덱은 시드 그대로");
        }

        /// <summary>약한 덱에서 체력이 가장 낮은 카드 n장의 키(그 나무 칸의 지형·기후에 맞는 것 — 찾아보기가 처음 맞는 카드를 뽑는다, R1).</summary>
        static string[] Weakest(int n)
        {
            var c = ctx.Board[Tree];
            var list = new List<PokemonDef>();
            var seen = new HashSet<int>();
            foreach (var e in ctx.Deck(DeckId.Weak).Cards)
            {
                var p = ctx.Cards.Pokemon[e.Card];
                if (p.Biome == c.Biome && p.Climate == c.Climate && seen.Add(e.Card)) list.Add(p);
            }
            list.Sort((x, y) => x.Health != y.Health ? x.Health.CompareTo(y.Health) : string.CompareOrdinal(x.Key, y.Key));
            Assert.GreaterOrEqual(list.Count, n, "칸 32 지형에 맞는 약한 카드");
            var keys = new string[n];
            for (int i = 0; i < n; i++) keys[i] = list[i].Key;
            return keys;
        }

        [Test]
        public void M65_주사위대본_면만바꾸고_난수소비는같다()
        {
            var a = new Pcg32(Seed);
            var b = new Pcg32(Seed);
            var deal = new TutorialDeal(ctx).Force(AttackDieFace.CriticalHit, AttackDieFace.StatusSymbol, AttackDieFace.CriticalHit);
            AttackDieFace[] got;
            using (Rig.Use(deal)) got = AttackDice.Roll(a, 3);
            AttackDice.Roll(b, 3);
            CollectionAssert.AreEqual(new[] { AttackDieFace.CriticalHit, AttackDieFace.StatusSymbol, AttackDieFace.CriticalHit }, got);
            Assert.AreEqual(b.State, a.State, "대본은 면만 바꾼다 — Pcg32는 평소대로 돌았다");
            Assert.AreEqual(0, deal.DiceLeft);
            Assert.AreEqual(3, deal.DiceForced);
            using (Rig.Use(deal)) CollectionAssert.AreEqual(AttackDice.Roll(new Pcg32(Seed), 3), AttackDice.Roll(new Pcg32(Seed), 3), "대본이 비면 평소 굴림");
            deal.Force(AttackDieFace.RegularHit);
            using (Rig.Use(deal)) Assert.Throws<InvalidOperationException>(() => AttackDice.Roll(new Pcg32(Seed), 2), "대본이 모자라면 각본 오류");
            Assert.IsNull(Rig.Current, "범위를 벗어나면 풀린다");
            Assert.Throws<ArgumentException>(() => deal.Force(AttackDieFace.None));
        }

        /// <summary>혼자 하기 작은 각본: 인사 → 마을 → 스타터·트레이너 카드 → 걷기 → 찾아보기(정해진 조우) → 배틀(정해진 주사위 — 한 번에 쓰러뜨리고 턴이 끝난다) → 다음 턴 종료.</summary>
        static TutorialScript SoloScript()
        {
            string wild = Weakest(1)[0];
            var s = new TutorialScript { Id = "mini" };
            s.Start.Seed = Seed;
            s.Start.TrainerCards = new[] { Cards };
            s.Start.Tops.Add(new DeckTop { Deck = DeckId.Weak, Keys = new[] { wild } });
            s.Steps.Add(new TutorialStep { Id = "S001", Advance = StepAdvance.Next, Ref = { "07 §3-3" } });
            s.Steps.Add(Cmd("S002", AnswerPattern.Pick(ChoiceKind.VillagePick, Village)));
            var picks = Cmd("S003", AnswerPattern.Pick(ChoiceKind.StarterPick), AnswerPattern.Pick(ChoiceKind.TrainerCardPick, CardId("hiker")));
            picks.Expect.Add(new ExpectValue("party", 1));
            picks.Expect.Add(new ExpectValue("tier", 1));
            picks.Expect.Add(new ExpectValue("movesLeft", 3));
            picks.Expect.Add(new ExpectValue("apricorn.red", 3));
            picks.Expect.Add(new ExpectValue("act", 1));
            picks.Expect.Add(new ExpectValue("round", 1));
            s.Steps.Add(picks);
            var walk = Cmd("S004", AnswerPattern.Of(AnswerKind.Move, Tree));
            walk.Expect.Add(new ExpectValue("movesLeft", 2));
            s.Steps.Add(walk);
            s.Steps.Add(Cmd("S005", AnswerPattern.Of(AnswerKind.Search)));
            s.Steps.Add(Cmd("S006", AnswerPattern.Pick(ChoiceKind.EncounterPick, ctx.Cards.PokemonIds.IndexOf(wild))));
            var attack = Cmd("S007", AnswerPattern.Of(AnswerKind.UseMove));
            attack.Dice.AddRange(new[] { AttackDieFace.CriticalHit, AttackDieFace.CriticalHit, AttackDieFace.CriticalHit });
            attack.Expect.Add(new ExpectValue("power", 3));
            attack.Expect.Add(new ExpectValue("diceSum", 6));
            attack.Expect.Add(new ExpectValue("round", 2)); // 배틀이 끝나면 턴도 끝난다 — 다음 턴(혼자 하기 = 다음 라운드)
            attack.Expect.Add(new ExpectValue("movesLeft", 3));
            s.Steps.Add(attack);
            var end = Cmd("S008", AnswerPattern.Of(AnswerKind.EndTurn));
            end.Expect.Add(new ExpectValue("round", 3));
            s.Steps.Add(end);
            return s;
        }

        static TutorialStep Cmd(string id, params AnswerPattern[] auto)
        {
            var st = new TutorialStep { Id = id, Advance = StepAdvance.Command, Ref = { "07 §4-2" } };
            st.Allow.AddRange(auto);
            st.Auto.AddRange(auto);
            return st;
        }

        [Test]
        public void M65_작은각본_끝까지_단계마다expect가엔진값과같다()
        {
            var run = new TutorialRunner(ctx, SoloScript(), new PrincipleAgent());
            run.PlayToEnd();
            Assert.IsTrue(run.Finished);
            CollectionAssert.IsEmpty(run.Failures, string.Join("\n", run.Failures));
            Assert.AreEqual(0, run.Deal.DiceLeft);
            Assert.AreEqual(3, run.Deal.DiceForced, "배틀 주사위 3개가 대본대로");
            Assert.AreEqual(0, run.Blocked, "자동 답은 거르개를 지난다(allow가 auto를 덮는다)");
            Assert.IsNull(run.Session.HostCopy().Battle, "한 번에 쓰러뜨려 배틀이 끝났다");
            Assert.IsFalse(run.Session.Saving, "튜토리얼 판은 저장하지 않는다");

            // 틀린 expect는 실패로 잡는다(조용히 지나가지 않는다)
            var wrong = SoloScript();
            wrong.Steps[3].Expect[0].Value = 1;
            var bad = new TutorialRunner(ctx, wrong, new PrincipleAgent());
            bad.PlayToEnd();
            Assert.AreEqual(1, bad.Failures.Count, string.Join("\n", bad.Failures));
            StringAssert.Contains("S004: movesLeft 기대 1 ≠ 엔진 2", bad.Failures[0]);
        }

        [Test]
        public void M65_거르개_거절은엔진에안가고_Tutorial이유()
        {
            var run = new TutorialRunner(ctx, SoloScript(), new PrincipleAgent());
            run.Begin();
            var me = run.Client;
            var village = me.FirstChoice();
            Assert.AreEqual(ChoiceKind.VillagePick, village.Kind);
            int before = run.Session.CommandCount;
            Assert.AreEqual(StepAdvance.Next, run.Step.Advance);
            var r = me.Answer(village, ChoiceAnswer.Pick(Village), 0);
            Assert.AreEqual(RejectReason.Tutorial, r.Reason, "[다음] 단계는 명령을 받지 않는다");
            run.Finish(); // [다음]
            r = me.Answer(village, ChoiceAnswer.Pick(Village + 1), 0);
            Assert.IsFalse(r.Accepted);
            Assert.AreEqual(RejectReason.Tutorial, r.Reason, "엔진 합법 답이어도 allow 밖이면 막는다");
            Assert.AreEqual(RejectReason.Tutorial, me.LastRejected?.Reason, "좌석에 거절이 간다(화면이 hint를 띄운다)");
            Assert.AreEqual(before, run.Session.CommandCount, "엔진에 가지 않았다 — 명령 수 그대로");
            Assert.AreEqual(2, run.Blocked);
            var mt = Command.Anytime(new Maintenance { Kind = MaintKind.PartyOrder });
            Assert.IsFalse(run.Allows(0, mt), "정비는 단계가 허락할 때만");
            Assert.IsTrue(run.Allows(1, mt), "다른 좌석은 거르지 않는다");
            Assert.IsTrue(me.Answer(village, ChoiceAnswer.Pick(Village), 0).Accepted, "지정 행동은 받는다");
            Assert.AreEqual(before + 1, run.Session.CommandCount);
        }

        /// <summary>둘째 좌석이 봇인 판: 첫 마을 고르기 좌석이 나(0)인 시드를 고른다(정해진 판은 난수 소비가 같아 평소 판으로 본다).</summary>
        static ulong SeedMeFirst()
        {
            for (ulong seed = 1; seed < 200; seed++)
            {
                var s = Engine.NewGame(ctx, new RuleOptions(), 2, seed);
                Engine.Start(s, ctx, null);
                if (s.Pending.Count == 1 && s.Pending[0].Seat == 0) return seed;
            }
            throw new AssertionException("첫 좌석이 0인 시드가 없다");
        }

        [Test]
        public void M65_봇각본큐_풀어준만큼만답하고_큐대로끝까지()
        {
            var s = new TutorialScript { Id = "bots" };
            s.Start.Seats = 2;
            s.Start.Seed = SeedMeFirst();
            s.Start.TrainerCards = new[] { Cards, new[] { "youngster", "lady", "fisherman" } };
            s.Bots = new[]
            {
                null,
                new List<AnswerPattern>
                {
                    AnswerPattern.Pick(ChoiceKind.VillagePick, Village + 1),
                    AnswerPattern.Pick(ChoiceKind.StarterPick),
                    AnswerPattern.Pick(ChoiceKind.TrainerCardPick, CardId("lady")),
                    AnswerPattern.Of(AnswerKind.EndTurn),
                },
            };
            s.Steps.Add(Cmd("B001", AnswerPattern.Pick(ChoiceKind.VillagePick, Village)));
            s.Steps.Add(new TutorialStep { Id = "B002", Advance = StepAdvance.Bot, BotSeat = 1, BotAnswers = 1 });
            s.Steps.Add(Cmd("B003", AnswerPattern.Pick(ChoiceKind.StarterPick), AnswerPattern.Pick(ChoiceKind.TrainerCardPick, CardId("ace_trainer"))));
            s.Steps.Add(new TutorialStep { Id = "B004", Advance = StepAdvance.Bot, BotSeat = 1, BotAnswers = 2 });
            var botTurn = new TutorialStep { Id = "B005", Advance = StepAdvance.Bot, BotSeat = 1, BotAnswers = 1 };
            botTurn.Expect.Add(new ExpectValue("movesLeft", 3));
            s.Steps.Add(botTurn);
            s.Steps.Add(Cmd("B006", AnswerPattern.Of(AnswerKind.EndTurn)));

            var run = new TutorialRunner(ctx, s, new PrincipleAgent());
            run.Begin();
            var bot = run.Bot(1);
            run.Play(); // B001: 내 마을
            Assert.AreEqual(0, bot.Consumed, "봇은 풀어 주기 전에는 답하지 않는다");
            Assert.AreEqual(-1, run.Session.HostCopy().Seats[1].HomeVillage);
            Assert.AreEqual(long.MaxValue, run.Session.NextDueMs, "붙잡힌 봇 답은 다음 일정에 없다");
            run.Play(); // B002: 봇 마을
            Assert.AreEqual(1, bot.Consumed);
            Assert.AreEqual(Village + 1, run.Session.HostCopy().Seats[1].HomeVillage);
            run.PlayToEnd();
            CollectionAssert.IsEmpty(run.Failures, string.Join("\n", run.Failures));
            Assert.AreEqual(0, bot.Left, "봇 큐를 다 썼다");
            var host = run.Session.HostCopy();
            Assert.AreEqual(CardId("lady"), host.Seats[1].TrainerCard, "봇은 각본대로 골랐다");
            Assert.AreEqual(CardId("ace_trainer"), host.Seats[0].TrainerCard);
        }

        /// <summary>
        /// 화면 쪽 진행(M6.5 화면 ① — 07 §9-1): 진행기가 화면 흐름의 손님을 같이 쓰고(attach — 링크는 좌석마다 손님 하나) 세션을 스스로 돌리지 않는다.
        /// 받아들여진 지정 행동만 센다(Taken — 막힌 것 · 엔진이 거절한 것은 아니다), bot 단계는 풀어 준 만큼만 봇이 답한다(ReleaseBot → 세션 Tick → BotDone).
        /// </summary>
        [Test]
        public void M65_화면쪽진행_손님을함께쓰고_받은지정행동만세고_봇은풀어준만큼()
        {
            SeatClient screen = null;
            int attached = 0;
            var s = new TutorialScript { Id = "bots" };
            s.Start.Seats = 2;
            s.Start.Seed = SeedMeFirst();
            s.Start.TrainerCards = new[] { Cards, new[] { "youngster", "lady", "fisherman" } };
            s.Bots = new[] { null, new List<AnswerPattern> { AnswerPattern.Pick(ChoiceKind.VillagePick, Village + 1) } };
            s.Steps.Add(Cmd("B001", AnswerPattern.Pick(ChoiceKind.VillagePick, Village)));
            s.Steps.Add(new TutorialStep { Id = "B002", Advance = StepAdvance.Bot, BotSeat = 1, BotAnswers = 1 });
            var run = new TutorialRunner(ctx, s, new PrincipleAgent(), attach: session =>
            {
                attached++;
                return screen = new SeatClient(session.Link, ctx, 0);
            });
            run.Begin();
            Assert.AreEqual(1, attached);
            Assert.AreSame(screen, run.Client, "화면의 손님을 같이 쓴다");
            var village = screen.FirstChoice();
            Assert.IsFalse(screen.Answer(village, ChoiceAnswer.Pick(Village + 1), 0).Accepted, "allow 밖");
            Assert.AreEqual(0, run.Taken, "막힌 명령은 세지 않는다");
            Assert.AreEqual(1, run.Blocked);
            Assert.IsTrue(screen.Answer(village, ChoiceAnswer.Pick(Village), 0).Accepted);
            Assert.AreEqual(1, run.Taken, "받아들여진 지정 행동");
            run.Finish();
            Assert.AreEqual(StepAdvance.Bot, run.Step.Advance);
            Assert.AreEqual(0, run.Taken, "단계마다 0부터");
            Assert.IsFalse(run.BotDone);
            Assert.AreEqual(long.MaxValue, run.Session.NextDueMs, "풀기 전 봇 답은 붙잡혀 있다");
            run.ReleaseBot();
            long due = run.Session.NextDueMs;
            Assert.AreNotEqual(long.MaxValue, due, "풀면 봇 답이 일정에 든다");
            Assert.IsFalse(run.BotDone, "화면 쪽은 세션을 스스로 돌리지 않는다");
            run.Session.Tick(due);
            Assert.IsTrue(run.BotDone);
            Assert.AreEqual(Village + 1, run.Session.HostCopy().Seats[1].HomeVillage);
            run.Finish();
            Assert.IsTrue(run.Finished);
            CollectionAssert.IsEmpty(run.Failures, string.Join("\n", run.Failures));
        }

        [Test]
        public void M65_봇각본큐_비었거나맞는답이없으면_각본오류()
        {
            var s = Engine.NewGame(ctx, new RuleOptions(), 2, 1);
            Engine.Start(s, ctx, null);
            var c = s.Pending[0];
            var view = Engine.ViewFor(s, c.Seat);
            var input = new AgentInput { View = view, Choice = view.Pending.Find(x => x.Id == c.Id), Ctx = ctx };
            Assert.Throws<InvalidOperationException>(() => new ScriptAgent(c.Seat, null).Decide(input, new AgentBudget()), "큐가 비었다");
            Assert.Throws<InvalidOperationException>(() => new ScriptAgent(c.Seat, new[] { AnswerPattern.Of(AnswerKind.Search) }).Decide(input, new AgentBudget()), "마을 고르기에 찾아보기는 없다");
            var ok = new ScriptAgent(c.Seat, new[] { AnswerPattern.Pick(ChoiceKind.VillagePick, 3) });
            Assert.AreEqual(3, ok.Decide(input, new AgentBudget()).Answer.Target);
            Assert.AreEqual(1, ok.Consumed);
        }

        [Test]
        public void M65_단계모형_expect키문법_답표기부분일치()
        {
            foreach (var k in new[] { "movesLeft", "jp", "apricorn.red", "apricorn.black", "balls.regular", "balls.keystone", "party", "hp.0", "hp.5", "power", "diceSum", "matchup", "damage", "captureRate", "captureSum", "act", "shards", "fateHand", "stamps", "tier", "round", "blessing", "breach", "prism" })
                Assert.IsTrue(TutorialExpect.IsKey(k), k);
            foreach (var k in new[] { "", "moves", "apricorn.purple", "apricorn.0", "balls.master", "hp.6", "hp.x", "hp", "breach.0", "prism.ub" })
                Assert.IsFalse(TutorialExpect.IsKey(k), k);
            var p = AnswerPattern.Pick(ChoiceKind.EncounterPick, 7);
            var c = new PendingChoice { Kind = ChoiceKind.EncounterPick };
            Assert.IsTrue(p.Matches(c, ChoiceAnswer.Pick(7, 0)), "적은 칸만 본다(Arg는 아무 값)");
            Assert.IsFalse(p.Matches(c, ChoiceAnswer.Pick(8, 0)));
            Assert.IsFalse(p.Matches(new PendingChoice { Kind = ChoiceKind.StarterPick }, ChoiceAnswer.Pick(7)), "선택 종류가 다르다");
            Assert.IsTrue(AnswerPattern.Any().Matches(c, ChoiceAnswer.Of(AnswerKind.Decline)));
            Assert.IsFalse(AnswerPattern.Maintain().Matches(c, ChoiceAnswer.Pick(7)), "정비 꼴은 답에 맞지 않는다");
            Assert.IsTrue(AnswerPattern.Maintain(MaintKind.HoldItem).Matches(new Maintenance { Kind = MaintKind.HoldItem }));
            Assert.IsFalse(AnswerPattern.Maintain(MaintKind.HoldItem).Matches(new Maintenance { Kind = MaintKind.Release }));
        }

        /// <summary>셋업 두 단계(마을 · 스타터·카드)로 시작하는 작은 각본.</summary>
        static TutorialScript SetupScript(string id)
        {
            var s = new TutorialScript { Id = id };
            s.Start.Seed = Seed;
            s.Start.TrainerCards = new[] { Cards };
            s.Steps.Add(Cmd("S001", AnswerPattern.Pick(ChoiceKind.VillagePick, Village)));
            s.Steps.Add(Cmd("S002", AnswerPattern.Pick(ChoiceKind.StarterPick), AnswerPattern.Pick(ChoiceKind.TrainerCardPick, CardId("hiker"))));
            return s;
        }

        [Test]
        public void M65_demo단계는사람명령을막고진행기가대신둔다_screen단계는명령을받지않는다()
        {
            var s = SetupScript("demo");
            s.Steps.Add(new TutorialStep { Id = "S003", Advance = StepAdvance.Screen, Screen = "board-hud-log", Ref = { "07 §7" } });
            var demo = new TutorialStep { Id = "S004", Advance = StepAdvance.Demo, Ref = { "07 §4-3" } };
            demo.Auto.Add(AnswerPattern.Of(AnswerKind.Move, Tree));
            demo.Expect.Add(new ExpectValue("movesLeft", 2));
            s.Steps.Add(demo);

            var run = new TutorialRunner(ctx, s, new PrincipleAgent());
            run.Begin();
            run.Play();
            run.Play();
            var me = run.Client;
            var map = me.Choice(ChoiceKind.MapAction);
            Assert.IsNotNull(map, "셋업이 끝나 첫 맵 행동");
            var walk = me.Legal(map).Find(a => a.Kind == AnswerKind.Move && a.Target == Tree);
            Assert.AreEqual(StepAdvance.Screen, run.Step.Advance);
            Assert.AreEqual(RejectReason.Tutorial, me.Answer(map, walk, 0).Reason, "screen 단계는 명령을 받지 않는다");
            run.Finish(); // 화면이 열렸다
            Assert.AreEqual(StepAdvance.Demo, run.Step.Advance);
            Assert.AreEqual(RejectReason.Tutorial, me.Answer(map, walk, 0).Reason, "demo 단계의 사람 명령은 auto와 같아도 막는다");
            Assert.AreEqual(2, run.Blocked);
            run.Play(); // 마박사가 대신 둔다
            Assert.IsTrue(run.Finished);
            CollectionAssert.IsEmpty(run.Failures, string.Join("\n", run.Failures));
            Assert.AreEqual(Tree, run.Session.HostCopy().Seats[0].Cell, "진행기가 낸 auto는 거르개를 지났다");
            Assert.AreEqual(2, run.Blocked, "진행기 답은 막히지 않았다");
        }

        [Test]
        public void M65_막앞판손보기after_셋업뒤한번_정비자동답은파티칸으로()
        {
            int bite = ctx.Cards.MoveIds.IndexOf("bite");
            Action<Ctx, GameState> giveMove = (c, st) =>
            {
                st.Pokemon[st.Seats[0].Party[0]].Moves[0] = bite;
                st.Seats[0].Jp = 5;
            };
            var s = SetupScript("after");
            s.Start.After = "give-move";
            s.Steps[1].Expect.Add(new ExpectValue("jp", 5)); // 셋업이 끝난 단계의 대조는 손본 판의 값
            var forget = new AnswerPattern { IsMaint = true, Maint = MaintKind.SetMove, Party = 0, Slot = 0, Value = -1 };
            var step = Cmd("S003", forget);
            s.Steps.Add(step);
            s.Steps.Add(Cmd("S004", AnswerPattern.Of(AnswerKind.EndTurn)));

            Assert.Throws<ArgumentException>(() => new TutorialRunner(ctx, s, new PrincipleAgent()).Begin(), "꽂지 않은 after는 각본 오류");
            Assert.Throws<ArgumentException>(() => new TutorialRunner(ctx, s, new PrincipleAgent(), name => null).Begin(), "모르는 이름");

            var run = new TutorialRunner(ctx, s, new PrincipleAgent(), name => name == "give-move" ? giveMove : null);
            run.Begin();
            run.Play();
            Assert.IsFalse(run.Rebased, "셋업 중에는 걸지 않는다");
            run.Play();
            Assert.IsTrue(run.Rebased, "셋업이 끝난 단계 끝에 걸었다");
            var host = run.Session.HostCopy();
            int mon = host.Seats[0].Party[0];
            Assert.AreEqual(bite, host.Pokemon[mon].Moves[0]);

            // 정비 표기: 파티 칸 0 = 그 포켓몬 — 다른 칸·다른 기술 칸은 거르개가 막는다
            var party = run.Client.View.Seats[0].Party;
            Assert.IsTrue(forget.Matches(new Maintenance { Kind = MaintKind.SetMove, Pokemon = mon, Slot = 0, Value = -1 }, party));
            Assert.IsFalse(forget.Matches(new Maintenance { Kind = MaintKind.SetMove, Pokemon = mon, Slot = 1, Value = -1 }, party));
            Assert.IsFalse(forget.Matches(new Maintenance { Kind = MaintKind.SetMove, Pokemon = mon + 1, Slot = 0, Value = -1 }, party));
            Assert.IsFalse(run.Allows(0, Command.Anytime(new Maintenance { Kind = MaintKind.SetMove, Pokemon = mon, Slot = 1, Value = -1 })));
            Assert.IsTrue(run.Allows(0, Command.Anytime(forget.ToMaintenance(party))));
            var order = new AnswerPattern { IsMaint = true, Maint = MaintKind.PartyOrder, Values = new[] { 0 } };
            CollectionAssert.AreEqual(new[] { mon }, order.ToMaintenance(party).Values, "PartyOrder의 Values는 파티 칸 → 포켓몬");
            Assert.Throws<ArgumentException>(() => new AnswerPattern { IsMaint = true, Maint = MaintKind.SetMove, Party = 1 }.ToMaintenance(party), "빈 파티 칸");

            run.PlayToEnd();
            CollectionAssert.IsEmpty(run.Failures, string.Join("\n", run.Failures));
            Assert.AreEqual(0, run.Blocked);
            host = run.Session.HostCopy();
            Assert.AreEqual(-1, host.Pokemon[mon].Moves[0], "자동 정비로 기술을 잊었다");
            Assert.AreEqual(2, host.Round, "손본 판에서 턴 종료까지");
            Assert.IsFalse(run.Session.Saving);
        }

        [Test]
        public void M65_손본판은_저장과함께쓰지않는다()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sinnoh-m65-" + System.IO.Path.GetRandomFileName());
            try
            {
                var store = new Sinnoh.Data.SaveStore(dir);
                Assert.Throws<ArgumentException>(() => LocalSession.Start(ctx, new RuleOptions(), 1, Seed, Campaign.HeartOfSinnoh,
                    new SessionConfig { VirtualAgent = new PrincipleAgent(), Rig = new TutorialDeal(ctx), Store = store }, 0));
                Assert.AreEqual(0, System.IO.Directory.GetFiles(dir).Length, "아무것도 쓰지 않았다");
            }
            finally
            {
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
        }
    }
}
