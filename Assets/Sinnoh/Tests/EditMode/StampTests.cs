using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「스탬프」(룰북 p.15 Journey Stamps, rulings A30, StampRules): 조건 7개(M5_스탬프_&lt;n&gt; — n = 시트 칸, 윗줄 왼쪽부터) · 보상 7단계 · 수호신의 서(보상 7)의 수호신 폼.
    /// 신·무리에 기대는 조건(무리 배틀 승리)은 고리(StampRules.OnSwarmWon)를 바로 불러 본다 — 실제 무리 배틀에서 받는 것은 GodSwarmTests(A30_무리배틀…).
    /// </summary>
    public class StampTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static Command Hold(int p, int item) => Command.Anytime(new Maintenance { Kind = MaintKind.HoldItem, Pokemon = p, Value = item });

        static Command Form(int p, int to) => Command.Anytime(new Maintenance { Kind = MaintKind.ChangeForm, Pokemon = p, Value = to });

        int Card(string key) => Scene.Def(ctx, key);

        // ── 조건 7 ──

        /// <summary>한카리아스로 야생 비버니에게 몬스터볼을 두 번 던진다(그 사이 야생 차례는 기본 답). 돌려주는 값 = 던진 뒤 상태.</summary>
        GameState TwoThrows(ulong seed, EventLog log)
        {
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "445:0");
            s.Seats[0].Balls[(int)Ball.Regular] = 2;
            Battles.Wild(s, ctx, log, 0, Scene.Spawn(s, ctx, "399:0", SeatId.Wild));
            s.Rng = new Pcg32(seed);
            int throws = 0;
            for (int guard = 0; guard < 40 && s.Battle != null; guard++)
            {
                var c = s.Pending.Find(x => !x.Answered);
                if (c == null) break;
                if (c.Kind == ChoiceKind.BattleAction && c.Seat == 0)
                {
                    if (throws == 2) break;
                    throws++;
                    Battles.Answer(s, ctx, c, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), log);
                }
                else Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            }
            return s;
        }

        [Test]
        public void M5_스탬프_1_배틀에서포획을2턴이어실패하면받고_기술을쓰면끊긴다()
        {
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                var t = TwoThrows(sd, l);
                return t.Battle != null && l.OfType<CaptureAttempt>().Count == 2;
            }, 1000);
            var log = new EventLog();
            var s = TwoThrows(seed, log);
            var st = s.Seats[0];
            Assert.IsTrue(st.Stamps[StampRules.FailedCaptures], "「You fail to capture a wild Pokémon 2 turns in a row during a battle」");
            var g = log.OfType<StampGained>();
            Assert.AreEqual(1, g.Count);
            Assert.AreEqual(0, g[0].Seat);
            Assert.AreEqual(StampRules.ItemOf(ctx, StampRules.FailedCaptures), g[0].StampKey);
            Assert.AreEqual(new SourceRef(SourceKind.Stamp, g[0].StampKey), g[0].Source);
            Assert.AreEqual(0, g[0].CountBefore);
            Assert.AreEqual(1, g[0].CountAfter);
            Assert.AreEqual(3, g[0].Changes.Length, "보상 1 = 빨강·파랑·노랑 1");
            Assert.AreEqual(1, st.Apricorns[(int)ApricornColor.Red]);
            Assert.AreEqual(1, st.Apricorns[(int)ApricornColor.Blue]);
            Assert.AreEqual(1, st.Apricorns[(int)ApricornColor.Yellow]);
            Assert.Less(log.Events.IndexOf(log.OfType<CaptureAttempt>()[1]), log.Events.IndexOf(g[0]), "두 번째 실패 뒤");

            // 던짐 → 기술 → 던짐은 이어지지 않는다(원작 커뮤니티 확인)
            var o = Scene.Game(ctx, 2);
            Scene.Give(o, ctx, 0, "445:0");
            var b = Scene.Battle(o, BattleKind.Wild, 0, SeatId.Wild, Scene.Spawn(o, ctx, "445:0", SeatId.Wild));
            var fx = Scene.Fx(o, null, 0);
            StampRules.CaptureFailed(o, ctx, fx, b, 0);
            StampRules.Acted(b, 0);
            StampRules.CaptureFailed(o, ctx, fx, b, 0);
            Assert.IsFalse(o.Seats[0].Stamps[StampRules.FailedCaptures], "사이에 기술을 썼다");
            StampRules.CaptureFailed(o, ctx, fx, b, 0);
            Assert.IsTrue(o.Seats[0].Stamps[StampRules.FailedCaptures]);
        }

        [Test]
        public void M5_스탬프_2_트레이너나UB를이기면받고_갤럭시단은혼자일때만()
        {
            bool Won(int seats, BattleKind kind, BattleResult result, int winner)
            {
                var s = Scene.Game(ctx, seats);
                Scene.Give(s, ctx, 0, "445:0");
                BattleState b;
                if (kind == BattleKind.Trainer)
                {
                    Scene.Give(s, ctx, 1, "399:0");
                    b = Scene.Battle(s, kind, 0, 1);
                }
                else b = Scene.Battle(s, kind, 0, kind == BattleKind.Galactic ? SeatId.Galactic : kind == BattleKind.UltraBeast ? SeatId.UltraBeast : SeatId.Wild, Scene.Spawn(s, ctx, "399:0", SeatId.Wild));
                StampRules.BattleEnded(s, ctx, Scene.Fx(s, null), b, result, winner);
                return s.Seats[0].Stamps[StampRules.TrainerWin];
            }
            Assert.IsTrue(Won(2, BattleKind.Trainer, BattleResult.Win, 0), "다른 트레이너를 이겼다");
            Assert.IsTrue(Won(2, BattleKind.Trainer, BattleResult.Forfeited, 0), "기권승도 승리");
            Assert.IsFalse(Won(2, BattleKind.Trainer, BattleResult.Win, 1), "진 쪽은 아니다");
            Assert.IsFalse(Won(2, BattleKind.Trainer, BattleResult.Draw, -1), "무승부는 승리가 아니다");
            Assert.IsTrue(Won(2, BattleKind.UltraBeast, BattleResult.Win, 0), "「or an Ultra Beast」");
            Assert.IsFalse(Won(2, BattleKind.Galactic, BattleResult.Win, 0), "여럿이면 조무래기는 트레이너가 아니다(원작 커뮤니티 확인)");
            Assert.IsTrue(Won(1, BattleKind.Galactic, BattleResult.Win, 0), "「Only if playing solo!」");
            Assert.IsFalse(Won(2, BattleKind.Wild, BattleResult.Win, 0), "야생은 아니다");

            // 실제 배틀 끝(Conclude)에서: 상대 좌석 포켓몬 체력 1
            var t = Battles.Game(ctx, 2);
            Scene.Give(t, ctx, 0, "445:0");
            int foe = Scene.Give(t, ctx, 1, "399:0");
            Battles.SetHp(t, ctx, foe, 1);
            var log = new EventLog();
            Battles.Start(t, ctx, log, BattleKind.Trainer, 0, 1, -1);
            Battles.Drive(t, ctx, log);
            var end = log.OfType<BattleEnded>()[0];
            Assert.AreEqual(0, end.Winner);
            Assert.IsTrue(t.Seats[0].Stamps[StampRules.TrainerWin]);
            Assert.IsFalse(t.Seats[1].Stamps[StampRules.TrainerWin]);
            Assert.Less(log.Events.IndexOf(end), log.Events.IndexOf(log.OfType<StampGained>()[0]), "배틀 결과 뒤");
        }

        [Test]
        public void M5_스탬프_3_가방에규토리일곱색이모두있으면명령끝에받는다_남의차례에도()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 12);
            int me = s.ActiveSeat, other = 1 - me;
            for (int c = 0; c < SeatState.ApricornColors; c++)
            {
                s.Seats[me].Apricorns[c] = Math.Max(1, s.Seats[me].Apricorns[c]);
                s.Seats[other].Apricorns[c] = Math.Max(1, s.Seats[other].Apricorns[c]);
            }
            s.Seats[other].Apricorns[(int)ApricornColor.Black] = 0;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
            Assert.IsTrue(s.Seats[me].Stamps[StampRules.AllApricorns], "「at least 1 of every Apricorn type」");
            Assert.IsFalse(s.Seats[other].Stamps[StampRules.AllApricorns], "검정이 없다");
            var g = log.OfType<StampGained>();
            Assert.AreEqual(1, g.Count);
            Assert.AreEqual(me, g[0].Seat);
            Assert.AreEqual(StampRules.ItemOf(ctx, StampRules.AllApricorns), g[0].StampKey);
            Assert.AreEqual(1, StampRules.Count(s.Seats[me]));
        }

        [Test]
        public void M5_스탬프_4_전설이나우두머리를볼로잡으면받는다()
        {
            int legend = -1;
            for (int i = 0; i < ctx.Cards.Pokemon.Count && legend < 0; i++)
                if (ctx.Cards.Pokemon[i].Legendary && ctx.Cards.Pokemon[i].Climate != Climate.Noble) legend = i;
            bool Caught(int def, bool alpha)
            {
                var s = Scene.Game(ctx, 2);
                int p = PokemonRules.Spawn(s, def, 0, (int)DeckId.Strong);
                s.Pokemon[p].Alpha = alpha;
                StampRules.OnCaptured(s, ctx, Scene.Fx(s, null), 0, p);
                return s.Seats[0].Stamps[StampRules.LegendaryCaught];
            }
            Assert.IsTrue(Caught(legend, false), "전설");
            Assert.IsTrue(Caught(Card("445:0"), true), "우두머리 = 전설(원작 커뮤니티 확인)");
            Assert.IsFalse(Caught(Card("445:0"), false), "강한 포켓몬은 아니다");

            // 실제 포획(CaptureResolve): 우두머리 토큰을 단 약한 비버니에게 마스터볼
            EventLog Scene4(ulong seed, out GameState st)
            {
                var l = new EventLog();
                var s = Battles.Game(ctx, 2);
                Scene.Give(s, ctx, 0, "445:0");
                s.Seats[0].Balls[(int)Ball.Keystone] = 1;
                int wild = Scene.Spawn(s, ctx, "399:0", SeatId.Wild);
                s.Pokemon[wild].Alpha = true;
                Battles.Wild(s, ctx, l, 0, wild);
                s.Rng = new Pcg32(seed);
                Battles.Answer(s, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Keystone), l);
                st = s;
                return l;
            }
            ulong ok = Battles.Seed(sd => Scene4(sd, out var t).OfType<CaptureAttempt>()[0].Success);
            var log = Scene4(ok, out var won);
            Assert.IsTrue(won.Seats[0].Stamps[StampRules.LegendaryCaught]);
            Assert.Less(log.Events.IndexOf(log.OfType<StampGained>()[0]), log.Events.IndexOf(log.OfType<BattleEnded>()[0]), "포획 뒤, 배틀 결과 앞");
        }

        [Test]
        public void M5_스탬프_5_파티가지닌물건이셋이면받는다()
        {
            var s = Scene.Game(ctx, 2);
            var st = s.Seats[0];
            var mons = new[] { Scene.Give(s, ctx, 0, "390:0"), Scene.Give(s, ctx, 0, "399:0"), Scene.Give(s, ctx, 0, "443:0") };
            var items = new[] { Scene.Item(ctx, "item.choice_band"), Scene.Item(ctx, "item.lucky_egg"), Scene.Item(ctx, "item.gracidea_flower") };
            st.Bag.AddRange(items);
            var log = new EventLog();
            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(st.Stamps[StampRules.HeldItems], $"{i}개");
                Scene.Accept(Engine.Submit(s, ctx, 0, Hold(mons[i], items[i]), log));
            }
            Assert.IsTrue(st.Stamps[StampRules.HeldItems], "「3 or more held items equipped in your party」 — 유물도 센다");
            Assert.AreEqual(1, log.OfType<StampGained>().Count);
            Assert.AreEqual(3, StampRules.HeldCount(s, st));
        }

        [Test]
        public void M5_스탬프_6_무리배틀승리고리()
        {
            var s = Scene.Game(ctx, 2);
            var log = new EventLog();
            StampRules.OnSwarmWon(s, ctx, Scene.Fx(s, log), 1);
            Assert.IsTrue(s.Seats[1].Stamps[StampRules.SwarmWin], "「You have been victorious at a Swarm Battle」 — 「신·무리·우두머리」가 잇는다");
            Assert.AreEqual(1, log.OfType<StampGained>()[0].Seat);
            StampRules.OnSwarmWon(s, ctx, Scene.Fx(s, log), 1);
            Assert.AreEqual(1, log.OfType<StampGained>().Count, "이미 받은 스탬프는 다시 받지 않는다");
        }

        [Test]
        public void M5_스탬프_7_손패에서로다른운명3장이면받는다_아르세우스는아르세우스한종()
        {
            bool Has(params FateCard[] hand)
            {
                var s = Scene.Game(ctx, 2);
                foreach (var c in hand) s.Seats[0].FateHand.Add(FateRules.CardKey(ctx, c));
                StampRules.Sweep(s, ctx, Scene.Fx(s, null));
                return s.Seats[0].Stamps[StampRules.FateCards];
            }
            Assert.IsTrue(Has(FateCard.Dialga, FateCard.Palkia, FateCard.Giratina));
            Assert.IsFalse(Has(FateCard.Dialga, FateCard.Dialga, FateCard.Palkia), "둘뿐");
            Assert.IsTrue(Has(FateCard.Dialga, FateCard.Palkia, FateCard.Arceus), "아르세우스도 한 종");
            Assert.IsFalse(Has(FateCard.Arceus, FateCard.Arceus, FateCard.Dialga), "결정 76 ④ F4 나: 아르세우스는 와일드카드가 아니다 — 두 종");
            Assert.IsFalse(Has(FateCard.Arceus, FateCard.Arceus, FateCard.Arceus), "한 종");
            Assert.IsFalse(Has(FateCard.Giratina, FateCard.Arceus));
        }

        // ── 보상 ──

        [Test]
        public void A30_보상은모은수의단계_규토리셋_카드5장셋_수호신의서()
        {
            var s = Scene.Game(ctx, 1);
            var st = s.Seats[0];
            int weak = s.Decks[(int)DeckId.Weak].Count, moderate = s.Decks[(int)DeckId.Moderate].Count, strong = s.Decks[(int)DeckId.Strong].Count;
            var log = new EventLog();
            // 칸 순서와 상관없이 모은 수가 단계를 정한다(「Stamps can be claimed in any order」)
            var order = new[] { StampRules.SwarmWin, StampRules.TrainerWin, StampRules.LegendaryCaught, StampRules.AllApricorns, StampRules.FailedCaptures, StampRules.FateCards, StampRules.HeldItems };
            foreach (int stamp in order) Assert.IsTrue(StampRules.Gain(s, ctx, Scene.Fx(s, log), 0, stamp));
            var g = log.OfType<StampGained>();
            Assert.AreEqual(7, g.Count);
            for (int i = 0; i < 7; i++)
            {
                Assert.AreEqual(i, g[i].CountBefore);
                Assert.AreEqual(i + 1, g[i].CountAfter);
                Assert.AreEqual(StampRules.ItemOf(ctx, order[i]), g[i].StampKey);
                Assert.AreEqual(i % 2 == 1 && i < 6 ? StampRules.CardsDrawn : 0, g[i].MoveKeys.Length, $"{i + 1}번째");
            }
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 1, 1, 1, 3 }, st.Apricorns, "1 빨강·파랑·노랑 · 3 분홍·초록·하양 · 5 검정 3");
            Assert.AreEqual(weak - 5, s.Decks[(int)DeckId.Weak].Count, "2 약한 5장");
            Assert.AreEqual(moderate - 5, s.Decks[(int)DeckId.Moderate].Count, "4 보통 5장");
            Assert.AreEqual(strong - 5, s.Decks[(int)DeckId.Strong].Count, "6 강한 5장");
            Assert.AreEqual(15, st.StampMoves.Count, "기술 풀에 둔다(「You may teach these cards as moves」)");
            Assert.AreEqual(30, st.StampCards.Count, "카드는 곁에 두고 조우 풀에서 빠진다");
            for (int i = 0; i < st.StampCards.Count; i += 2)
                Assert.AreEqual(ctx.Cards.Pokemon[st.StampCards[i + 1]].Signature, st.StampMoves[i / 2], "풀의 기술 = 그 카드의 시그니처");
            CollectionAssert.Contains(st.Bag, Scene.Item(ctx, StampRules.ScrollOfNobility), "7 수호신의 서");
            Assert.AreEqual(ResKind.Item, g[6].Changes[0].Res);
            Assert.IsFalse(StampRules.Gain(s, ctx, Scene.Fx(s, log), 0, StampRules.SwarmWin), "한 번 받은 스탬프는 끝까지(「kept for the remainder of the game」)");
        }

        [Test]
        public void A30_보상규토리로채운조건은이어받는다()
        {
            var s = Scene.Game(ctx, 2);
            var st = s.Seats[0];
            foreach (var c in new[] { ApricornColor.Pink, ApricornColor.Green, ApricornColor.White, ApricornColor.Black }) st.Apricorns[(int)c] = 1;
            var log = new EventLog();
            StampRules.OnSwarmWon(s, ctx, Scene.Fx(s, log), 0);
            Assert.IsTrue(st.Stamps[StampRules.AllApricorns], "보상 1(빨강·파랑·노랑)로 일곱 색이 찼다");
            Assert.AreEqual(2, log.OfType<StampGained>().Count);
            Assert.AreEqual(StampRules.CardsDrawn, log.OfType<StampGained>()[1].MoveKeys.Length, "둘째 = 약한 5장");
        }

        [Test]
        public void A30_보상기술은정비로가르친다()
        {
            var s = Scene.Game(ctx, 2, 7, new RuleOptions { StampMoveTypeLimit = false }); // 뽑힌 5장의 타입과 상관없이(G10 U15-2는 MaintenanceTests)
            int mon = Scene.Give(s, ctx, 0, "390:0");
            StampRules.Gain(s, ctx, Scene.Fx(s, null), 0, StampRules.SwarmWin);
            StampRules.Gain(s, ctx, Scene.Fx(s, null), 0, StampRules.TrainerWin);
            var st = s.Seats[0];
            int move = st.StampMoves[0];
            int before = st.StampMoves.Count;
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.Anytime(new Maintenance { Kind = MaintKind.SetMove, Pokemon = mon, Slot = 0, Value = move }), null));
            Assert.AreEqual(move, s.Pokemon[mon].Moves[0]);
            Assert.AreEqual(before - 1, st.StampMoves.Count);
        }

        // ── 수호신의 서(보상 7) ──

        [Test]
        public void A30_수호신의서를지닌완전진화스타팅은수호신폼으로_빼면기본폼()
        {
            var s = Flow.Started(ctx, 2, 12);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            st.OpenSlots = 6;
            int ape = Scene.Give(s, ctx, seat, "392:1");
            int turtle = Scene.Give(s, ctx, seat, "389:1");
            int chomp = Scene.Give(s, ctx, seat, "445:0");
            int scroll = Scene.Item(ctx, StampRules.ScrollOfNobility);
            st.Bag.Add(scroll);
            Assert.AreEqual(Card("392:2"), StampRules.NobleForm(ctx, Card("392:1")));
            Assert.AreEqual(-1, StampRules.NobleForm(ctx, Card("445:0")), "수호신 폼이 없는 종");
            Scene.Reject(Engine.Submit(s, ctx, seat, Form(ape, Card("392:2")), null), RejectReason.Illegal, "서를 지녀야(「when held」)");
            Scene.Accept(Engine.Submit(s, ctx, seat, Hold(ape, scroll), null));
            Scene.Reject(Engine.Submit(s, ctx, seat, Form(turtle, Card("389:2")), null), RejectReason.Illegal, "「Just the one its equipped to!」");
            var log = new EventLog();
            Scene.Accept(Engine.Submit(s, ctx, seat, Form(ape, Card("392:2")), log));
            Assert.AreEqual(Card("392:2"), s.Pokemon[ape].Def);
            Assert.IsTrue(ctx.Cards.Pokemon[s.Pokemon[ape].Def].Legendary, "수호신 폼은 전설(원작 커뮤니티 확인)");
            Assert.AreEqual(1, log.OfType<FormChanged>().Count);
            // 서를 한카리아스로 옮기면 기본 폼으로 돌아가고, 한카리아스는 바꿀 폼이 없다
            log.Clear();
            Scene.Accept(Engine.Submit(s, ctx, seat, Hold(chomp, scroll), log));
            Assert.AreEqual(Card("392:1"), s.Pokemon[ape].Def, "서를 더 지니지 않는다");
            Assert.AreEqual(1, log.OfType<FormChanged>().Count);
            Scene.Reject(Engine.Submit(s, ctx, seat, Form(chomp, Card("392:2")), null), RejectReason.Illegal, "다른 종의 폼");
            // 다시 지니고 바꾼 뒤 기본 폼으로는 언제든
            Scene.Accept(Engine.Submit(s, ctx, seat, Hold(ape, scroll), null));
            Scene.Accept(Engine.Submit(s, ctx, seat, Form(ape, Card("392:2")), null));
            Scene.Accept(Engine.Submit(s, ctx, seat, Form(ape, Card("392:1")), null));
            Assert.AreEqual(Card("392:1"), s.Pokemon[ape].Def);
        }

        // ── 봇 판 ──

        const int BotSeeds = 5;

        [Test]
        public void M5_스탬프_봇판에서받는다()
        {
            // 1~6인 × 시드 BotSeeds: 예외 없이 끝나고(카드 총수 보존 — 곁에 둔 카드 포함) 스탬프를 받는다. 칸마다 받은 수를 적는다.
            var counts = new long[SeatState.StampCount];
            int games = 0;
            for (int seats = 1; seats <= SeatId.MaxSeats; seats++)
                for (ulong seed = 0; seed < BotSeeds; seed++)
                {
                    var g = Playout.Run(ctx, seats, seed);
                    games++;
                    for (int k = 0; k < counts.Length; k++) counts[k] += g.Stamps[k];
                }
            long total = 0;
            foreach (long n in counts) total += n;
            TestContext.WriteLine($"[M5 스탬프 봇 판] {games}판, 받은 스탬프 {total}: " + string.Join(" · ", Array.ConvertAll(counts, n => n.ToString())));
            Assert.Greater(total, 0);
        }
    }
}
