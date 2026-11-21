using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 신오의 심장 2막·3막·종막(01 §8-1, rulings A16·G17, 원작 커뮤니티 확인). 1막은 BattleTests(A1).
    /// 장면: 좌석을 호수·시작의 사당·신오신전 칸에 놓고 맵 행동부터 실제 명령으로 돈다. 이기고 지는 것은 체력을 고쳐 정하고 주사위는 시드를 찾는다.
    /// </summary>
    public class ActTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static readonly ChoiceAnswer ChallengeGalactic = ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic);

        /// <summary>이기는 장면에 쓰는 포켓몬(체력 36 · 우선도 15) — 체력을 1로 고친 상대 명단을 끝까지 버틴다.</summary>
        const string Sturdy = "800:4";

        /// <summary>막 카드를 act막까지 공개한 장면 게임(좌석은 고향 마을, 차례 좌석 0).</summary>
        GameState Game(int seats, int act, ulong seed = 7)
        {
            var s = Battles.Game(ctx, seats, seed);
            var fx = Scene.Fx(s, null);
            while (s.Hos.Act < act) SetupFlow.RevealAct(s, ctx, fx, -1);
            return s;
        }

        /// <summary>좌석을 그 칸에 놓고(이동력 1) 맵 행동을 연다. 좌석에 포켓몬이 없으면 한 마리 준다.</summary>
        void Stand(GameState s, int seat, int cell, IEventSink log = null)
        {
            var st = s.Seats[seat];
            if (st.Party[0] < 0) Scene.Give(s, ctx, seat, "387:0");
            st.Cell = cell;
            st.MovesLeft = 1;
            s.Pending.Clear();
            s.ActiveSeat = seat;
            TurnFlow.OpenMapAction(s, ctx, Scene.Fx(s, log, seat), seat);
        }

        /// <summary>좌석이 1막을 끝내고 조각 held개(앞에서부터)를 가진 것으로.</summary>
        static void Progress(GameState s, int seat, int held, bool heart = false)
        {
            var st = s.Seats[seat];
            st.ActProgress = held == SeatState.ShardCount ? 2 : 1;
            for (int i = 0; i < held; i++) st.Shards[i] = true;
            st.OpenSlots = CampaignRules.OpenPartySlots(s.Campaign, held);
            st.Heart = heart;
        }

        bool CanChallenge(GameState s) => Flow.MapLegal(s, ctx).Exists(a => a.SameAs(ChallengeGalactic));

        /// <summary>
        /// 지금 칸의 간부·태홍에게 도전해 명단(Default)을 받고 끝까지 돈다. win이면 상대 명단의 체력을 1로, 아니면 내 파티의 체력을 1로 고친다.
        /// 배틀 뒤 종막 선택이 열리면 거기서 멈춘다.
        /// </summary>
        void Fight(GameState s, ulong seed, EventLog log, bool win)
        {
            s.Rng = new Pcg32(seed);
            int seat = s.ActiveSeat;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChallengeGalactic, log);
            var lineup = Battles.Only(s, ChoiceKind.Lineup);
            Battles.Answer(s, ctx, lineup, lineup.Default.Clone(), log);
            if (win)
                foreach (int p in s.Battle.Sides[1].Roster) Battles.SetHp(s, ctx, p, 1);
            else
                foreach (int p in s.Seats[seat].Party)
                    if (p >= 0) Battles.SetHp(s, ctx, p, 1);
            Battles.Drive(s, ctx, log);
        }

        /// <summary>좌석 0이 shard 호수에서 조각 held개를 가진 채 싸운 판(2인).</summary>
        GameState Lake(ulong seed, EventLog log, Shard shard, int held, bool win, int seats = 2)
        {
            var s = Game(seats, 2);
            Progress(s, 0, held);
            Scene.Give(s, ctx, 0, Sturdy);
            Stand(s, 0, HosFlow.LakeCell(ctx, shard), log);
            Fight(s, seed, log, win);
            return s;
        }

        /// <summary>좌석 0이 심장을 들고 신오신전에서 태홍과 싸운 판.</summary>
        GameState Temple(ulong seed, EventLog log, bool win, int seats = 2)
        {
            var s = Game(seats, 3);
            Progress(s, 0, 3, true);
            Scene.Give(s, ctx, 0, Sturdy);
            for (int i = 1; i < seats; i++) Scene.Give(s, ctx, i, "393:0");
            Stand(s, 0, HosFlow.TempleCell(ctx), log);
            Fight(s, seed, log, win);
            return s;
        }

        ulong WinSeed(Func<ulong, GameState> play, Func<GameState, bool> ok) => Battles.Seed(sd => ok(play(sd)), 200);

        // ── 자리 ──

        [Test]
        public void M5_막고정물대응표()
        {
            // p.21·22: 진실호수 = 마스 = 감정, 예지호수 = 주피터 = 지식, 입지호수 = 새턴 = 의지 — 호수 칸 = 그 호수 동굴 고정물의 칸(지역 이름이 호수 이름), 간부마다 5마리 · 태홍 8마리
            var lakes = new[] { ("Lake Verity", GalacticTrainer.Mars, "481:0@mars"), ("Lake Acuity", GalacticTrainer.Jupiter, "480:0@jupiter"), ("Lake Valor", GalacticTrainer.Saturn, "482:0@saturn") };
            var village = -1;
            foreach (var f in ctx.Fixtures.Fixtures)
                if (f.Kind == FixtureKind.Village && !ctx.Board[f.Cell].DistortionWorld && village < 0) village = f.Cell;
            var reach = MoveRules.Reachable(ctx.Board, village, ctx.Board.Count, new RuleOptions().ElevationMode);
            for (int i = 0; i < lakes.Length; i++)
            {
                int cell = HosFlow.LakeCell(ctx, (Shard)i);
                Assert.AreEqual(lakes[i].Item1, ctx.Board[cell].Region, $"{(Shard)i}");
                Assert.AreEqual(i, HosFlow.LakeShard(ctx, cell));
                Assert.AreEqual(lakes[i].Item2, HosFlow.CommanderOf((Shard)i));
                Assert.AreNotEqual(MoveRules.Unreachable, reach[cell], $"{lakes[i].Item1}까지 걸어갈 수 있다");
                int own = 0;
                bool guardian = false;
                foreach (var e in ctx.Deck(DeckId.GalacticCommander).Cards)
                {
                    if (ctx.Cards.Pokemon[e.Card].Trainer != lakes[i].Item2) continue;
                    own++;
                    guardian |= ctx.Cards.PokemonIds[e.Card] == lakes[i].Item3;
                }
                Assert.AreEqual(5, own, $"{lakes[i].Item2}의 포켓몬 5마리");
                Assert.IsTrue(guardian, $"{lakes[i].Item2}의 명단에 그 호수의 수호신");
            }
            Assert.AreEqual(8, ctx.Deck(DeckId.GalacticBoss).Cards.Count, "태홍 8마리");
            Assert.AreEqual("Mount Coronet Summit", ctx.Board[HosFlow.OriginShrineCell(ctx)].Region);
            Assert.AreEqual("Mount Coronet Summit", ctx.Board[HosFlow.TempleCell(ctx)].Region);
            Assert.AreNotEqual(MoveRules.Unreachable, reach[HosFlow.OriginShrineCell(ctx)], "시작의 사당까지 걸어갈 수 있다");
            Assert.AreNotEqual(MoveRules.Unreachable, reach[HosFlow.TempleCell(ctx)], "신오신전까지 걸어갈 수 있다");
            Assert.AreEqual(ItemKind.Held, ctx.Items.Items[ctx.Items.Ids.IndexOf(HosFlow.HeartItemKey)].Kind);
            foreach (var key in new[] { HosFlow.ActTwoKey, HosFlow.ActThreeKey, HosFlow.EndingKey }) Assert.GreaterOrEqual(ctx.Decks.CardIds.IndexOf(key), 0, key);
        }

        // ── 2막 ──

        [Test]
        public void S16_2_2막_간부도전은1막을끝낸좌석이조각없는호수에서()
        {
            var s = Game(2, 2);
            int lake = HosFlow.LakeCell(ctx, Shard.Emotion);
            Stand(s, 0, lake);
            Assert.IsFalse(CanChallenge(s), "1막을 끝내지 않았다(p.21 「Other trainers still must finish Act I」)");
            Progress(s, 0, 0);
            Stand(s, 0, lake);
            Assert.IsTrue(CanChallenge(s), "2막: 호수의 간부");
            s.Seats[0].Shards[(int)Shard.Emotion] = true;
            Stand(s, 0, lake);
            Assert.IsFalse(CanChallenge(s), "이미 얻은 조각의 호수");
            Stand(s, 0, HosFlow.LakeCell(ctx, Shard.Willpower));
            Assert.IsTrue(CanChallenge(s), "다른 호수는 된다(순서 무관)");
            Stand(s, 0, HosFlow.TempleCell(ctx));
            Assert.IsFalse(CanChallenge(s), "신오신전은 3막");

            var act1 = Game(2, 1);
            Progress(act1, 0, 0);
            act1.Seats[0].ActProgress = 0;
            Stand(act1, 0, lake);
            Assert.IsFalse(CanChallenge(act1), "2막 공개 전");
        }

        [Test]
        public void S16_2_2막_간부명단은조각수더하기2()
        {
            for (int held = 0; held < SeatState.ShardCount; held++)
            {
                var s = Game(2, 2);
                Progress(s, 0, held);
                var shard = (Shard)held; // 아직 없는 조각의 호수
                Stand(s, 0, HosFlow.LakeCell(ctx, shard));
                var log = new EventLog();
                Flow.Answer(s, ctx, ChoiceKind.MapAction, ChallengeGalactic, log);
                var c = Battles.Only(s, ChoiceKind.Lineup);
                Assert.AreEqual(SeatId.Galactic, c.Seat, "조종자가 고른다(A16)");
                Assert.AreEqual(2 + held, c.Count, $"조각 {held}개");
                Assert.AreEqual(5, c.Options.Count);
                foreach (int card in c.Options) Assert.AreEqual(HosFlow.CommanderOf(shard), ctx.Cards.Pokemon[card].Trainer);
                Assert.AreEqual(held == 0 ? 10 : held == 1 ? 10 : 5, Scene.Legal(s, ctx, c).Count, "C(5, k)");
                Assert.AreEqual(2 + held, c.Default.Values.Count, "앞 k마리(G9)");
                Assert.IsNull(s.Battle, "명단을 고르기 전에는 배틀이 없다");
                Assert.AreEqual(15, s.Decks[(int)DeckId.GalacticCommander].Count, "카드는 답할 때까지 덱에");

                // 원작 커뮤니티 확인: 명단을 앞면으로 놓은 다음 도전자가 첫 포켓몬을 낸다
                Scene.Give(s, ctx, 0, "399:0");
                Battles.Answer(s, ctx, c, ChoiceAnswer.PickMany(held == 0 ? new List<int> { 0, 4 } : c.Default.Values), log);
                var gb = log.OfType<GalacticBattle>()[0];
                Assert.AreEqual(GalacticLeader.Commander, gb.Leader);
                Assert.AreEqual(2 + held, gb.LineupCount);
                if (held == 0) CollectionAssert.AreEqual(new[] { c.Options[0], c.Options[4] }, gb.Lineup, "고른 카드가 앞면으로");
                Assert.AreEqual(BattleKind.Galactic, log.OfType<BattleStarted>()[0].Kind);
                Assert.AreEqual((int)BattleBgm.GalacticCommander, log.OfType<BattleStarted>()[0].BgmKey);
                var send = log.OfType<ChoiceRequested>().Find(r => r.Kind == ChoiceKind.SendIn);
                Assert.AreEqual(0, send.Seat, "도전자가 먼저 낸다");
                Assert.Less(log.Events.IndexOf(gb), log.Events.IndexOf(send), "명단 공개 → 도전자의 첫 포켓몬(F12)");
                Assert.AreEqual(15 - (2 + held), s.Decks[(int)DeckId.GalacticCommander].Count);
                Assert.AreEqual(2 + held, s.Battle.Sides[1].Roster.Count);
            }
        }

        [Test]
        public void S16_2_2막_간부를이기면조각과파티칸()
        {
            ulong seed = WinSeed(sd => Lake(sd, new EventLog(), Shard.Knowledge, 0, true), s => s.Seats[0].Shards[(int)Shard.Knowledge]);
            var log = new EventLog();
            var s = Lake(seed, log, Shard.Knowledge, 0, true);
            var st = s.Seats[0];
            CollectionAssert.AreEqual(new[] { false, true, false }, st.Shards);
            Assert.AreEqual(4, st.OpenSlots, "조각 1개마다 파티 칸 +1(A2·A3)");
            Assert.AreEqual(1, st.ActProgress, "조각 셋을 모아야 2막 목표 달성");
            var got = log.OfType<ShardGained>()[0];
            Assert.AreEqual(0, got.Seat);
            Assert.AreEqual(Shard.Knowledge, got.Shard);
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(HosFlow.LakeCell(ctx, Shard.Knowledge), got.Cell);
            var slot = log.OfType<PartySlotUnlocked>()[0];
            Assert.AreEqual(3, slot.Before);
            Assert.AreEqual(4, slot.After);
            Assert.AreEqual(0, log.OfType<ActGoalMet>().Count);
            Assert.AreEqual(0, log.OfType<ActRevealed>().Count);
            Assert.AreEqual(2, s.Hos.Act);
            var end = log.OfType<BattleEnded>()[0];
            Assert.AreEqual(0, end.Winner);
            Assert.AreEqual(0, end.Changes.Length, "갤럭시단전은 여행 점수가 없다(F12)");
            Assert.AreEqual(15, s.Decks[(int)DeckId.GalacticCommander].Count, "간부 카드는 덱으로");
            Assert.AreEqual(1, s.ActiveSeat, "배틀 뒤 턴 끝 → 다음 좌석");
            Assert.AreEqual(ChoiceKind.MapAction, Battles.Only(s, ChoiceKind.MapAction).Kind);
        }

        [Test]
        public void S16_2_2막_지거나비기면조각없음()
        {
            ulong seed = WinSeed(sd => Lake(sd, new EventLog(), Shard.Emotion, 0, false), s => s.Battle == null);
            var log = new EventLog();
            var s = Lake(seed, log, Shard.Emotion, 0, false);
            Assert.AreNotEqual(0, log.OfType<BattleEnded>()[0].Winner);
            CollectionAssert.AreEqual(new[] { false, false, false }, s.Seats[0].Shards);
            Assert.AreEqual(3, s.Seats[0].OpenSlots);
            Assert.AreEqual(0, log.OfType<ShardGained>().Count);
            Assert.AreEqual(0, log.OfType<Whiteout>()[0].Seat, "바닥나면 고향 마을로");
            Assert.AreEqual(15, s.Decks[(int)DeckId.GalacticCommander].Count);
        }

        [Test]
        public void S16_2_2막_조각셋을처음모은좌석이3막을공개()
        {
            ulong seed = WinSeed(sd => Lake(sd, new EventLog(), Shard.Willpower, 2, true, 3), s => s.Seats[0].ActProgress == 2);
            var log = new EventLog();
            var s = Lake(seed, log, Shard.Willpower, 2, true, 3);
            Assert.AreEqual(6, s.Seats[0].OpenSlots);
            var goal = log.OfType<ActGoalMet>()[0];
            Assert.AreEqual(2, goal.Act);
            Assert.IsTrue(goal.First);
            Assert.AreEqual(0, s.Hos.FirstSeat[2]);
            var act = log.OfType<ActRevealed>()[0];
            Assert.AreEqual(ctx.Decks.CardIds.IndexOf(HosFlow.ActThreeKey), act.ActKey);
            Assert.AreEqual(0, act.FirstSeat);
            Assert.AreEqual(3, s.Hos.Act);
            Assert.Less(log.Events.IndexOf(log.OfType<TurnEnded>()[0]), log.Events.IndexOf(act), "턴 끝 → 막 공개 → 다음 턴");

            // 둘째로 모은 좌석은 목표만 달성하고 막은 그대로(p.21 「Other trainers still must finish Act II」)
            Progress(s, 1, 2);
            Scene.Give(s, ctx, 1, Sturdy);
            Stand(s, 1, HosFlow.LakeCell(ctx, Shard.Willpower));
            ulong second = Battles.Seed(sd =>
            {
                var t = s.Clone();
                Fight(t, sd, new EventLog(), true);
                return t.Seats[1].ActProgress == 2;
            }, 200);
            var log2 = new EventLog();
            Fight(s, second, log2, true);
            Assert.IsFalse(log2.OfType<ActGoalMet>()[0].First);
            Assert.AreEqual(0, log2.OfType<ActRevealed>().Count);
            Assert.AreEqual(0, s.Hos.FirstSeat[2]);
            Assert.AreEqual(3, s.Hos.Act);
        }

        [Test]
        public void A16_간부포켓몬은쓰러뜨린직후교체못함()
        {
            // 내 포켓몬 둘 중 첫째를 체력 1로: 간부가 쓰러뜨리면 교체 창 대신 잠김(GalacticNoSwitchAfterKo)
            SwitchLocked Locked(ulong sd, bool option)
            {
                var s = Game(2, 2);
                s.Options.GalacticNoSwitchAfterKo = option;
                Progress(s, 0, 0);
                int first = Scene.Give(s, ctx, 0, "387:0");
                Scene.Give(s, ctx, 0, "399:0");
                Stand(s, 0, HosFlow.LakeCell(ctx, Shard.Emotion));
                var log = new EventLog();
                s.Rng = new Pcg32(sd);
                Flow.Answer(s, ctx, ChoiceKind.MapAction, ChallengeGalactic, log);
                var lineup = Battles.Only(s, ChoiceKind.Lineup);
                Battles.Answer(s, ctx, lineup, lineup.Default.Clone(), log);
                Battles.SetHp(s, ctx, first, 1);
                Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(first), log);
                Battles.Drive(s, ctx, log, (g, c) => log.OfType<Fainted>().Count > 0 && c.Kind != ChoiceKind.NextPokemon && c.Seat == 0 ? ChoiceAnswer.Of(AnswerKind.Forfeit) : null);
                var faint = log.OfType<Fainted>();
                if (faint.Count == 0 || faint[0].Seat != 0) return null;
                var locks = log.OfType<SwitchLocked>();
                bool asked = log.OfType<ChoiceRequested>().Exists(r => r.Kind == ChoiceKind.SwitchWindow && r.Seat == SeatId.Galactic);
                if (!option) return asked ? new SwitchLocked() : null;
                return locks.Count > 0 && !asked ? locks[0] : null;
            }
            ulong seed = Battles.Seed(sd => Locked(sd, true) != null, 200);
            Assert.AreEqual(SwitchLockReason.GalacticNoSwitchAfterKo, Locked(seed, true).Reason);
            Assert.IsNotNull(Locked(seed, false), "옵션을 끄면 간부도 교체 창을 받는다(룰북 「more challenging battles」)");
        }

        // ── 3막 ──

        [Test]
        public void S16_2_3막_시작의사당공물로심장회복()
        {
            var s = Game(2, 3);
            Progress(s, 0, 3);
            int shrine = HosFlow.OriginShrineCell(ctx);
            var st = s.Seats[0];
            for (int c = 0; c < st.Apricorns.Length; c++) st.Apricorns[c] = 0;
            Stand(s, 0, shrine);
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Offer), "바칠 규토리가 없다");
            st.Apricorns[(int)ApricornColor.Blue] = 2;
            st.Apricorns[(int)ApricornColor.Black] = 1;
            Stand(s, 0, shrine);
            var offers = Flow.MapLegal(s, ctx).FindAll(a => a.Kind == AnswerKind.Offer);
            Assert.AreEqual(2, offers.Count, "가진 색마다 하나");
            Assert.AreEqual(HosFlow.OriginShrine(ctx), offers[0].Target);
            Assert.AreEqual((int)ApricornColor.Blue, offers[0].Arg);

            // 신오의 신 토큰이 있으면 바칠 수 없다(p.22)
            var god = new TokenState { Kind = TokenKind.God, Def = 0, Cell = shrine, FaceUp = true };
            s.Tokens.Add(god);
            Stand(s, 0, shrine);
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Offer), "신 토큰이 있는 동안");
            s.Tokens.Remove(god);

            Stand(s, 0, HosFlow.TempleCell(ctx));
            Assert.IsFalse(CanChallenge(s), "심장이 없으면 태홍과 싸울 수 없다(p.22)");

            Stand(s, 0, shrine);
            var log = new EventLog();
            // 심장 공물도 사당 공물이라 사당 이벤트 카드를 뽑는다(ShrineRules, rulings A27) — 고를 것이 없는 평온한 순간을 맨 위에
            var pile = s.Decks[(int)DeckId.ShrineEvent];
            int calm = ShrineRules.CardKey(ctx, ShrineRules.TranquilMoment);
            pile.Remove(calm);
            pile.Add(calm);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, offers[0], log);
            Assert.AreEqual(calm, log.OfType<ShrineCardsDrawn>()[0].CardKeys[0], "심장 공물로도 카드를 뽑는다(원작 커뮤니티 확인)");
            Assert.IsTrue(st.Heart);
            Assert.AreEqual(1, st.Apricorns[(int)ApricornColor.Blue]);
            CollectionAssert.AreEqual(new[] { true, true, true }, st.Shards, "조각은 버리지 않는다");
            int item = ctx.Items.Ids.IndexOf(HosFlow.HeartItemKey);
            CollectionAssert.Contains(st.Bag, item);
            var e = log.OfType<HeartRestored>()[0];
            Assert.AreEqual(0, e.Seat);
            Assert.AreEqual(item, e.ItemKey);
            Assert.AreEqual(shrine, e.Cell);
            Assert.AreEqual(1, log.OfType<ApricornSpent>()[0].Changes.Length);
            var next = Battles.Only(s, ChoiceKind.MapAction);
            Assert.AreEqual(0, next.Seat, "턴은 이어진다");
            Assert.IsFalse(Flow.Has(Scene.Legal(s, ctx, next), AnswerKind.Offer), "사당 공물은 턴당 1번(p.17)");
            Assert.IsFalse(HosFlow.CanRestoreHeart(s, ctx, 0), "심장은 한 번만");

            // 조각 셋을 모으기 전에도 시작의 사당은 보통 사당 공물을 받는다(p.17 「any Shrine」) — 심장은 회복하지 않는다
            var early = Game(2, 2);
            Progress(early, 0, 2);
            early.Seats[0].Apricorns[0] = 1;
            Stand(early, 0, shrine);
            Assert.IsFalse(HosFlow.CanRestoreHeart(early, ctx, 0), "조각 셋을 모으기 전");
            Assert.IsTrue(Flow.Has(Flow.MapLegal(early, ctx), AnswerKind.Offer), "보통 사당 공물");
        }

        [Test]
        public void S16_2_3막_심장은지닌포켓몬과같은타입기술에공격력더하기1()
        {
            var s = Game(2, 3);
            int mon = Scene.Give(s, ctx, 0, "387:0");
            int foe = Scene.Give(s, ctx, 1, "393:0");
            var def = ctx.Cards.Pokemon[s.Pokemon[mon].Def];
            int same = def.Signature, other = -1;
            Assert.Contains(ctx.Cards.Moves[same].Type, new List<PokemonType>(def.Types), "시그니처가 제 타입인 포켓몬을 골랐다");
            for (int m = 0; m < ctx.Cards.Moves.Count && other < 0; m++)
            {
                var mv = ctx.Cards.Moves[m];
                if (mv.AttackStrength.HasValue && mv.AttackStrength.Value < 8 && mv.Type != PokemonType.Typeless && !new List<PokemonType>(def.Types).Contains(mv.Type)) other = m;
            }
            var mods = new List<Mod>();
            int baseSame = BattleRules.RollPower(s, ctx, same, mon, foe, mods);
            int baseOther = BattleRules.RollPower(s, ctx, other, mon, foe, mods);
            int planB = BattleRules.RollPower(s, ctx, BattleRules.PlanB, mon, foe, mods);

            int item = ctx.Items.Ids.IndexOf(HosFlow.HeartItemKey);
            s.Seats[0].Bag.Add(item);
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.Anytime(new Maintenance { Kind = MaintKind.HoldItem, Pokemon = mon, Value = item }), null), "심장을 지닌다");
            Assert.AreEqual(baseSame + 1, BattleRules.RollPower(s, ctx, same, mon, foe, mods), "같은 타입 +1(p.22)");
            Assert.IsTrue(mods.Exists(m => m.Source.Same(new SourceRef(SourceKind.Item, item)) && m.Delta == 1), "보정 출처 = 심장");
            Assert.AreEqual(baseOther, BattleRules.RollPower(s, ctx, other, mon, foe, mods), "다른 타입은 그대로");
            Assert.AreEqual(planB, BattleRules.RollPower(s, ctx, BattleRules.PlanB, mon, foe, mods));
        }

        [Test]
        public void S16_2_3막_태홍은8마리중6마리를낸다()
        {
            var s = Game(2, 3);
            Progress(s, 0, 3, true);
            Stand(s, 0, HosFlow.TempleCell(ctx));
            Assert.IsTrue(CanChallenge(s));
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChallengeGalactic, log);
            var c = Battles.Only(s, ChoiceKind.Lineup);
            Assert.AreEqual(SeatId.Galactic, c.Seat);
            Assert.AreEqual(6, c.Count);
            Assert.AreEqual(8, c.Options.Count);
            Assert.AreEqual(28, Scene.Legal(s, ctx, c).Count, "C(8, 6)");
            Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            var gb = log.OfType<GalacticBattle>()[0];
            Assert.AreEqual(GalacticLeader.Cyrus, gb.Leader);
            Assert.AreEqual(6, gb.LineupCount);
            Assert.AreEqual((int)BattleBgm.GalacticBoss, log.OfType<BattleStarted>()[0].BgmKey);
            Assert.AreEqual(2, s.Decks[(int)DeckId.GalacticBoss].Count);
        }

        [Test]
        public void S16_2_3막_태홍을이기면종막카드와선택()
        {
            ulong seed = WinSeed(sd => Temple(sd, new EventLog(), true), s => s.Seats[0].ActProgress == 3);
            var log = new EventLog();
            var s = Temple(seed, log, true);
            Assert.AreEqual(8, s.Decks[(int)DeckId.GalacticBoss].Count, "태홍 카드는 덱으로");
            var goal = log.OfType<ActGoalMet>()[0];
            Assert.AreEqual(3, goal.Act);
            Assert.AreEqual(0, s.Hos.FirstSeat[3]);
            Assert.AreEqual(4, s.Hos.Act);
            Assert.AreEqual(ctx.Decks.CardIds.IndexOf(HosFlow.EndingKey), log.OfType<ActRevealed>()[0].ActKey);
            var c = Battles.Only(s, ChoiceKind.Ending, ChoiceSub.EndingChoice);
            Assert.AreEqual(0, c.Seat, "태홍을 이긴 좌석이 고른다");
            Assert.AreEqual(2, Scene.Legal(s, ctx, c).Count);
            Assert.AreEqual(0, c.Default.Target, "시간 초과 = 영웅(G9)");
            Assert.AreEqual(0, log.OfType<TurnStarted>().Count, "턴이 멈춘다 — 다음 좌석의 턴을 열지 않는다");
            Assert.AreEqual(Phase.Turn, s.Phase);

            // 지면 그대로 3막
            ulong lose = WinSeed(sd => Temple(sd, new EventLog(), false), t => t.Battle == null && t.Seats[0].ActProgress == 2);
            var lost = Temple(lose, new EventLog(), false);
            Assert.AreEqual(3, lost.Hos.Act);
            Assert.AreEqual(ChoiceKind.MapAction, Battles.Only(lost, ChoiceKind.MapAction).Kind);
        }

        // ── 종막 ──

        /// <summary>태홍을 이겨 종막 선택이 열린 판(좌석 0이 고른다).</summary>
        GameState Ending(int seats, EventLog log)
        {
            ulong seed = WinSeed(sd => Temple(sd, new EventLog(), true, seats), s => s.Seats[0].ActProgress == 3);
            return Temple(seed, log, true, seats);
        }

        [Test]
        public void S16_3_종막_영웅은즉시승리()
        {
            var log = new EventLog();
            var s = Ending(3, log);
            Battles.Answer(s, ctx, ChoiceKind.Ending, ChoiceAnswer.Pick(0), log);
            Assert.AreEqual(Phase.GameOver, s.Phase);
            Assert.AreEqual(0, s.Hos.Winner);
            Assert.AreEqual(-1, s.Hos.Traitor);
            Assert.IsFalse(log.OfType<EndingChosen>()[0].Traitor);
            var end = log.OfType<GameEnded>()[0];
            Assert.AreEqual(GameEndReason.Hero, end.Reason);
            CollectionAssert.AreEqual(new[] { 0 }, end.Winners);
            Assert.AreEqual(0, s.Pending.Count);
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.Anytime(new Maintenance { Kind = MaintKind.PartyOrder }), null), RejectReason.GameOver, "끝난 판");
        }

        /// <summary>배신자 연전을 끝까지: 배틀마다 loser 쪽(0 = 배신자, 1 = 상대)의 포켓몬 체력을 1로 고치며 돈다.</summary>
        void DriveFinale(GameState s, EventLog log, Func<int, int> loserOfBattle)
        {
            for (int guard = 0; guard < 2000 && s.Phase != Phase.GameOver; guard++)
            {
                var c = s.Pending.Find(x => !x.Answered);
                Assert.IsNotNull(c, "끝나지 않았는데 열린 선택이 없다");
                if (s.Battle != null)
                {
                    int loser = loserOfBattle(s.Hos.TraitorIndex);
                    foreach (int p in s.Seats[s.Battle.Sides[loser].Owner].Party)
                        if (p >= 0 && Battles.Hp(s, ctx, p) > 1) Battles.SetHp(s, ctx, p, 1);
                }
                Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            }
            Assert.AreEqual(Phase.GameOver, s.Phase, "종막이 끝나지 않는다");
        }

        [Test]
        public void S16_3_종막_배신자는모두회복뒤고른순서로연전해모두이기면승리()
        {
            var log = new EventLog();
            var s = Ending(3, log);
            Battles.SetHp(s, ctx, s.Seats[1].Party[0], 1);
            s.Pokemon[s.Seats[2].Party[0]].Status = StatusEffect.Poisoned;
            log.Clear();
            Battles.Answer(s, ctx, ChoiceKind.Ending, ChoiceAnswer.Pick(1), log);
            Assert.IsTrue(log.OfType<EndingChosen>()[0].Traitor);
            Assert.AreEqual(0, s.Hos.Traitor);
            Assert.AreEqual(3, log.OfType<Healed>().Count, "모든 트레이너의 파티 회복(p.22)");
            for (int i = 0; i < 3; i++)
                foreach (int p in s.Seats[i].Party)
                    if (p >= 0)
                    {
                        Assert.AreEqual(0, s.Pokemon[p].Damage);
                        Assert.AreEqual(StatusEffect.None, s.Pokemon[p].Status);
                    }
            var order = Battles.Only(s, ChoiceKind.Ending, ChoiceSub.TraitorOrder);
            Assert.AreEqual(0, order.Seat);
            CollectionAssert.AreEqual(new[] { 1, 2 }, order.Options);
            Assert.AreEqual(2, Scene.Legal(s, ctx, order).Count);
            CollectionAssert.AreEqual(new[] { 1, 2 }, order.Default.Values, "배신자 다음 좌석부터 시계 방향(G9)");
            Assert.AreEqual(0, log.OfType<TurnStarted>().Count, "턴이 멈춘다");

            Battles.Answer(s, ctx, order, ChoiceAnswer.PickMany(new List<int> { 2, 1 }), log);
            CollectionAssert.AreEqual(new[] { 2, 1 }, log.OfType<TraitorOrderSet>()[0].Seats);
            Assert.AreEqual(BattleKind.Traitor, s.Battle.Kind);
            Assert.AreEqual(0, s.Battle.Sides[0].Owner, "배신자가 도전자(우선권 토큰)");
            Assert.AreEqual(2, s.Battle.Sides[1].Owner, "고른 순서의 첫 상대");
            var issued = log.OfType<ChallengeIssued>()[0];
            Assert.IsTrue(issued.Remote);
            Assert.IsFalse(issued.CanRefuse);

            DriveFinale(s, log, _ => 1);
            var battles = log.OfType<BattleStarted>();
            Assert.AreEqual(2, battles.Count);
            Assert.AreEqual(1, battles[1].Sides[1].Owner, "둘째 상대");
            Assert.AreEqual(0, log.OfType<Whiteout>().Count, "연전에는 귀환이 없다");
            Assert.AreEqual(3 + 2, log.OfType<Healed>().Count, "이길 때마다 배신자 회복");
            var end = log.OfType<GameEnded>()[0];
            Assert.AreEqual(GameEndReason.TraitorWon, end.Reason);
            CollectionAssert.AreEqual(new[] { 0 }, end.Winners);
            Assert.AreEqual(0, s.Hos.Winner);
            Assert.IsNull(s.Battle);
        }

        [Test]
        public void A11_종막_배신자가이길때마다쓴운명카드가손으로돌아온다()
        {
            // p.22 「Each time you defeat a trainer, fully restore your party and return any Change of Fate cards used to your hand.」
            var log = new EventLog();
            var s = Ending(3, log);
            Battles.Answer(s, ctx, ChoiceKind.Ending, ChoiceAnswer.Pick(1), log);
            var order = Battles.Only(s, ChoiceKind.Ending, ChoiceSub.TraitorOrder);
            Battles.Answer(s, ctx, order, order.Default.Clone(), log);
            Assert.AreEqual(BattleKind.Traitor, s.Battle.Kind);
            // 첫 배틀에서 배신자가 디아루가 1장을 낸다(효과는 다음 묶음 — 아무 일도 하지 않는 처리기로)
            var table = new Dictionary<string, EffectHandler> { { "fate.dialga", (gs, c, fx, f) => EffectStepResult.Done } };
            var cx = new Ctx(ctx.Board, ctx.Cards, ctx.Decks, ctx.Fixtures, ctx.Items, new EffectDb(new List<EffectDef>(ctx.Effects.DataEffects()), ctx.Cards.Moves.Count, table));
            int dialga = FateRules.CardKey(ctx, FateCard.Dialga);
            Assert.IsTrue(s.Decks[(int)DeckId.ChangeOfFate].Remove(dialga));
            s.Seats[0].FateHand.Add(dialga);
            Assert.IsTrue(FateRules.Play(s, cx, Scene.Fx(s, log, 0, ActionKind.Reaction), 0, FateCard.Dialga, false));
            Assert.AreEqual(0, s.Seats[0].FateHand.Count);
            CollectionAssert.AreEqual(new[] { dialga }, s.Hos.TraitorFate);
            CollectionAssert.Contains(s.Discards[(int)DeckId.ChangeOfFate], dialga);
            log.Clear();
            DriveFinale(s, log, _ => 1);
            CollectionAssert.AreEqual(new[] { dialga }, s.Seats[0].FateHand, "이긴 뒤 쓴 카드가 손으로");
            CollectionAssert.DoesNotContain(s.Discards[(int)DeckId.ChangeOfFate], dialga, "버림 더미에서 나온다");
            Assert.AreEqual(0, s.Hos.TraitorFate.Count);
            var back = log.OfType<FateDealt>();
            Assert.AreEqual(1, back.Count, "쓴 카드가 없던 둘째 승리에는 없다");
            Assert.IsTrue(back[0].Source.Same(SourceRef.Rule(RuleSource.TraitorReturn)));
            CollectionAssert.AreEqual(new[] { dialga }, back[0].PerSeat[0].CardKeys);
            Assert.AreEqual(GameEndReason.TraitorWon, log.OfType<GameEnded>()[0].Reason);
        }

        [Test]
        public void S16_3_종막_배신자를이긴좌석이승리()
        {
            var log = new EventLog();
            var s = Ending(3, log);
            s.Pokemon[s.Seats[2].Party[0]].Def = Scene.Def(ctx, Sturdy); // 둘째 상대는 배신자와 같은 포켓몬 — 체력 1이 된 배신자를 쓰러뜨린다
            Battles.Answer(s, ctx, ChoiceKind.Ending, ChoiceAnswer.Pick(1), log);
            var order = Battles.Only(s, ChoiceKind.Ending, ChoiceSub.TraitorOrder);
            Battles.Answer(s, ctx, order, order.Default.Clone(), log);
            log.Clear();
            DriveFinale(s, log, battle => battle == 0 ? 1 : 0); // 첫 상대는 지고, 둘째 상대가 배신자를 이긴다
            var end = log.OfType<GameEnded>()[0];
            Assert.AreEqual(GameEndReason.TraitorDefeated, end.Reason);
            CollectionAssert.AreEqual(new[] { 2 }, end.Winners);
            Assert.AreEqual(2, s.Hos.Winner);
            Assert.AreEqual(Phase.GameOver, s.Phase);
        }

        [Test]
        public void G17_종막_배신자연전무승부는상대승리()
        {
            // rulings G17 [가정]: 함께 쓰러지면 도전한 쪽(배신자)이 이기지 못한 것 — 그 상대가 승리한다(갤럭시단전 무승부 = 패배, 원작 커뮤니티 확인와 같은 꼴)
            GameState Play(ulong sd, EventLog log)
            {
                var s = Ending(2, new EventLog());
                Battles.Answer(s, ctx, ChoiceKind.Ending, ChoiceAnswer.Pick(1), null);
                Assert.AreEqual(BattleKind.Traitor, s.Battle.Kind, "상대가 하나면 순서를 묻지 않는다");
                s.Rng = new Pcg32(sd);
                int mine = s.Battle.Sides[0].Active, foe = s.Battle.Sides[1].Active;
                // 배신자의 포켓몬은 피로가 가득이고 체력 1: 공격하면 피로 피해로 함께 쓰러진다(동시 적용 p.13)
                s.Pokemon[mine].Fatigue = FatigueRules.Max;
                Battles.SetHp(s, ctx, mine, 1);
                Battles.SetHp(s, ctx, foe, 1);
                Battles.Drive(s, ctx, log);
                return s;
            }
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                Play(sd, l);
                var e = l.OfType<BattleEnded>();
                return e.Count == 1 && e[0].Result == BattleResult.Draw;
            }, 200);
            var log2 = new EventLog();
            var end = Play(seed, log2);
            Assert.AreEqual(Phase.GameOver, end.Phase);
            var ge = log2.OfType<GameEnded>()[0];
            Assert.AreEqual(GameEndReason.TraitorDefeated, ge.Reason);
            CollectionAssert.AreEqual(new[] { 1 }, ge.Winners);
        }

        [Test]
        public void S16_3_종막_상대가없으면배신자가곧바로승리()
        {
            // rulings G17 [가정]: 혼자 하는 판에서 배신자를 고르면 싸울 상대가 없다 — 「모두 이기면 승리」가 곧바로 참이다
            var log = new EventLog();
            var s = Ending(1, log);
            log.Clear();
            Battles.Answer(s, ctx, ChoiceKind.Ending, ChoiceAnswer.Pick(1), log);
            Assert.AreEqual(Phase.GameOver, s.Phase);
            Assert.AreEqual(0, log.OfType<TraitorOrderSet>()[0].Seats.Length);
            Assert.AreEqual(GameEndReason.TraitorWon, log.OfType<GameEnded>()[0].Reason);
            Assert.AreEqual(0, log.OfType<BattleStarted>().Count);
        }

        [Test]
        public void M5_막진행상태는복제와해시에든다()
        {
            var a = Game(2, 3);
            var b = a.Clone();
            Assert.AreEqual(StateHash.Of(a), StateHash.Of(b));
            b.Hos.Winner = 1;
            Assert.AreNotEqual(StateHash.Of(a), StateHash.Of(b), "이긴 좌석이 해시에 든다");
            var c = b.Clone();
            Assert.AreEqual(1, c.Hos.Winner);
        }
    }
}
