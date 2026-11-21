using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「보상·남의 행동 반응」(rulings A38 — 능력 11 + 월척 주사위): aroma_lady B · fisherman B·R · artist B · backpacker B · black_belt B · bug_catcher B · gentleman B ·
    /// delinquent B · ginkgo_merchant B · interviewers B.
    /// </summary>
    public class TrainerRewardTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const string Bidoof = "399:0", Starly = "396:0", Kricketot = "401:0", Garchomp = "445:0", Turtwig = "387:0";

        SourceRef Card(GameState s, int seat, string key, int tier = TrainerRules.Gold)
        {
            int card = ctx.Cards.TrainerCardIds.IndexOf(key);
            s.Seats[seat].TrainerCard = card;
            s.Seats[seat].TrainerCardTier = tier;
            TrainerRules.Refresh(s, ctx);
            return new SourceRef(SourceKind.TrainerCard, card);
        }

        int Token(GameState s, TokenKind kind)
        {
            int i = s.Tokens.FindIndex(t => t.Kind == kind && t.Cell >= 0);
            Assert.GreaterOrEqual(i, 0, kind.ToString());
            return i;
        }

        /// <summary>좌석 0이 그 토큰(−1 = 없음)에서 야생 wild와 배틀하고 볼을 던진다(시드 = 볼을 던지기 직전의 난수).</summary>
        GameState Throw(ulong seed, EventLog log, string card, string wild, int tokenKind, out int mine, out int target, bool damaged = false)
        {
            var s = Battles.Game(ctx, 2);
            if (card != null) Card(s, 0, card);
            mine = Scene.Give(s, ctx, 0, Starly);
            if (damaged) s.Pokemon[mine].Damage = 2;
            s.Seats[0].Balls[(int)Ball.Regular] = 1;
            target = Scene.Spawn(s, ctx, wild, SeatId.Wild);
            int token = tokenKind < 0 ? -1 : Token(s, (TokenKind)tokenKind);
            Battles.Wild(s, ctx, log, 0, target, token);
            s.Rng = new Pcg32(seed);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), log);
            return s;
        }

        static AttackDieFace[] Raw(EventLog log) => log.OfType<CaptureAttempt>()[0].Faces;

        static bool HasSymbol(AttackDieFace[] f) => System.Array.IndexOf(f, AttackDieFace.StatusSymbol) >= 0;

        static List<AbilityUsed> Used(EventLog log, SourceRef card) => log.OfType<AbilityUsed>().FindAll(e => e.CardKey == card.Id);

        // ── aroma_lady B · fisherman B 앞 절반 ──

        [Test]
        public void A20_6_아로마아가씨_나무의포획굴림은심볼하나를일반타격으로()
        {
            ulong seed = Battles.Seed(sd => HasSymbol(Raw(Log(l => Throw(sd, l, null, Bidoof, (int)TokenKind.ApricornTree, out _, out _)))));
            foreach (var kind in new[] { TokenKind.ApricornTree, TokenKind.ApricornReef })
            {
                var log = new EventLog();
                var s = Throw(seed, log, "aroma_lady", Bidoof, (int)kind, out _, out _);
                var raw = Raw(log);
                var ev = log.OfType<CaptureAttempt>()[0];
                bool tree = kind == TokenKind.ApricornTree;
                Assert.AreEqual(AttackDice.Sum(tree ? CraftRules.Allured(raw) : raw), ev.Sum, tree ? "나무: 심볼 하나 → 일반 타격" : "산호에서는 없다");
                Assert.AreEqual(tree ? 1 : 0, Used(log, new SourceRef(SourceKind.TrainerCard, s.Seats[0].TrainerCard)).Count);
            }
        }

        static EventLog Log(System.Action<EventLog> run)
        {
            var log = new EventLog();
            run(log);
            return log;
        }

        [Test]
        public void A20_6_낚시꾼_산호의포획굴림_잡으면월척주사위()
        {
            ulong seed = Battles.Seed(sd => HasSymbol(Raw(Log(l => Throw(sd, l, null, Bidoof, (int)TokenKind.ApricornReef, out _, out _)))));
            var log = new EventLog();
            Throw(seed, log, "fisherman", Bidoof, (int)TokenKind.ApricornReef, out _, out _);
            Assert.AreEqual(AttackDice.Sum(CraftRules.Allured(Raw(log))), log.OfType<CaptureAttempt>()[0].Sum, "산호: 심볼 하나 → 일반 타격");

            ulong ok = Battles.Seed(sd => Throw(sd, new EventLog(), "fisherman", Bidoof, (int)TokenKind.ApricornReef, out _, out _).Battle == null);
            log = new EventLog();
            var s = Throw(ok, log, "fisherman", Bidoof, (int)TokenKind.ApricornReef, out _, out int caught);
            Assert.AreEqual(0, s.Pokemon[caught].Owner, "잡았다");
            Assert.That(s.Pokemon[caught].CatchSize, Is.InRange(0, 5), "월척 주사위를 굴려 붙인다");
            var used = log.OfType<AbilityUsed>().FindAll(e => e.Targets.Length == 2 && e.Targets[0] == caught);
            Assert.AreEqual(1, used.Count);
            Assert.AreEqual(s.Pokemon[caught].CatchSize, used[0].Targets[1]);

            ulong tree = Battles.Seed(sd => Throw(sd, new EventLog(), "fisherman", Bidoof, (int)TokenKind.ApricornTree, out _, out _).Battle == null);
            var t = Throw(tree, new EventLog(), "fisherman", Bidoof, (int)TokenKind.ApricornTree, out _, out int other);
            Assert.AreEqual(-1, t.Pokemon[other].CatchSize, "나무에서 잡으면 없다");
        }

        [Test]
        public void A20_6_낚시꾼빨강_월척만큼체력_JP2로한단계()
        {
            var s = Battles.Game(ctx, 2);
            int p = Scene.Give(s, ctx, 0, Bidoof);
            int baseHp = PokemonRules.MaxHp(ctx, s.Pokemon[p]);
            s.Pokemon[p].CatchSize = 0;
            Card(s, 0, "fisherman", TrainerRules.Silver);
            Assert.AreEqual(baseHp, PokemonRules.MaxHp(ctx, s.Pokemon[p]), "실버(파랑만)에서는 체력 보정이 없다");
            Card(s, 0, "fisherman");
            Assert.AreEqual(baseHp - 1, PokemonRules.MaxHp(ctx, s.Pokemon[p]), "Tiny = −1");

            var grow = new Maintenance { Kind = MaintKind.CatchSize, Pokemon = p, Value = 1 };
            s.Seats[0].Jp = 1;
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, grow, new EventLog()), RejectReason.Illegal, "JP 2가 없다");
            s.Seats[0].Jp = 5;
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, new Maintenance { Kind = MaintKind.CatchSize, Pokemon = p, Value = 2 }, new EventLog()), RejectReason.Illegal, "한 단계씩");
            var log = new EventLog();
            Scene.Accept(MaintenanceRules.Submit(s, ctx, 0, grow, log));
            Assert.AreEqual(3, s.Seats[0].Jp);
            Assert.AreEqual(1, s.Pokemon[p].CatchSize);
            Assert.AreEqual(baseHp, PokemonRules.MaxHp(ctx, s.Pokemon[p]), "Average = 0");
            Assert.AreEqual(1, log.OfType<AbilityUsed>().Count);
            s.Pokemon[p].CatchSize = 5;
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(baseHp + 6, PokemonRules.MaxHp(ctx, s.Pokemon[p]), "Gargantuan = 6");
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, new Maintenance { Kind = MaintKind.CatchSize, Pokemon = p, Value = 6 }, new EventLog()), RejectReason.Illegal, "끝 면");
            int none = Scene.Give(s, ctx, 0, Turtwig);
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, new Maintenance { Kind = MaintKind.CatchSize, Pokemon = none, Value = 0 }, new EventLog()), RejectReason.Illegal, "주사위가 없는 포켓몬");
        }

        // ── artist B ──

        [Test]
        public void A20_6_화가_배틀뒤가르치기는배울수있는타입과상관없이()
        {
            var s = Battles.Game(ctx, 2);
            int p = Scene.Give(s, ctx, 0, Bidoof); // 노말
            int move = -1;
            for (int m = 0; m < ctx.Cards.Moves.Count && move < 0; m++)
                if (!ChoiceRules.CanLearn(ctx, s.Pokemon[p], ctx.Cards.Moves[m].Type)) move = m;
            Assert.GreaterOrEqual(move, 0);
            PendingChoice Teach(Window w) => new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.TeachMove, Seat = 0, Subject = move, Window = w };
            Assert.AreEqual(1, Scene.Legal(s, ctx, Teach(Window.BattleEnd)).Count, "카드 없음: 안 가르침뿐");
            Card(s, 0, "artist", TrainerRules.Silver);
            Assert.AreEqual(1 + PokemonState.MoveSlots, Scene.Legal(s, ctx, Teach(Window.BattleEnd)).Count, "배틀 뒤: 타입과 상관없이 두 칸");
            Assert.AreEqual(1, Scene.Legal(s, ctx, Teach(Window.None)).Count, "배틀 뒤가 아닌 가르치기(scientist B 등)는 그대로");
        }

        // ── 배틀 뒤 보상: black_belt B · gentleman B ──

        /// <summary>좌석 0이 나무 토큰의 야생(체력 1)을 쓰러뜨린다. 배틀 뒤 선택은 기본 답.</summary>
        GameState Defeat(string card, EventLog log)
        {
            var s = Battles.Game(ctx, 2);
            if (card != null) Card(s, 0, card);
            Scene.Give(s, ctx, 0, Garchomp);
            int wild = Scene.Spawn(s, ctx, Bidoof, SeatId.Wild);
            Battles.SetHp(s, ctx, wild, 1);
            Battles.Wild(s, ctx, log, 0, wild, Token(s, TokenKind.ApricornTree));
            Battles.Drive(s, ctx, log);
            Assert.IsNull(s.Battle);
            Assert.AreEqual(BattleResult.Win, log.OfType<BattleEnded>()[0].Result);
            return s;
        }

        static int Apricorns(SeatState st)
        {
            int n = 0;
            foreach (int a in st.Apricorns) n += a;
            return n;
        }

        [Test]
        public void A20_6_검은띠_야생을쓰러뜨리면JP2더()
        {
            var plain = Defeat(null, new EventLog());
            var log = new EventLog();
            var s = Defeat("black_belt", log);
            Assert.AreEqual(plain.Seats[0].Jp + TrainerRules.TrainingJp, s.Seats[0].Jp);
            Assert.AreEqual(1, log.OfType<AbilityUsed>().Count);
            Assert.AreEqual(plain.Seats[0].Jp + TrainerRules.TrainingJp, System.Array.FindLast(log.OfType<BattleEnded>()[0].Changes, c => c.Res == ResKind.Jp).After, "BattleEnded의 자원 변화에 든다");
        }

        /// <summary>좌석 0이 UB(체력 1)를 쓰러뜨린다. 배틀 뒤 선택은 기본 답.</summary>
        GameState DefeatUb(string card, EventLog log)
        {
            var s = Battles.Game(ctx, 2);
            if (card != null) Card(s, 0, card);
            Scene.Give(s, ctx, 0, Garchomp);
            int ub = Scene.Spawn(s, ctx, "797:0", SeatId.UltraBeast);
            Battles.SetHp(s, ctx, ub, 1);
            Battles.Start(s, ctx, log, BattleKind.UltraBeast, 0, SeatId.UltraBeast, -1, ub);
            Battles.Drive(s, ctx, log);
            Assert.IsNull(s.Battle);
            Assert.AreEqual(BattleResult.Win, log.OfType<BattleEnded>()[0].Result);
            return s;
        }

        [Test]
        public void A38_검은띠_UB는야생이아니다_JP2가없다()
        {
            // 감사 G2-3(11-20): 「After defeating a wild Pokémon」 — UB는 「wild Pokémon」이 아니다(essentia R과 같은 읽기 — rulings A34 ② · A38 ④). 호수 수호신(전설 · 야생 쪽)은 그대로 든다
            var plain = DefeatUb(null, new EventLog());
            var log = new EventLog();
            var s = DefeatUb("black_belt", log);
            Assert.AreEqual(plain.Seats[0].Jp, s.Seats[0].Jp, "UB 격파에는 +2 없음");
            Assert.AreEqual(0, log.OfType<AbilityUsed>().FindAll(e => e.CardKey == s.Seats[0].TrainerCard).Count);
        }

        [Test]
        public void A20_6_신사_야생격파의규토리두배()
        {
            var plain = Defeat(null, new EventLog());
            Assert.Greater(Apricorns(plain.Seats[0]), 0, "나무의 규토리를 받는다");
            var s = Defeat("gentleman", new EventLog());
            Assert.AreEqual(2 * Apricorns(plain.Seats[0]), Apricorns(s.Seats[0]));
            CollectionAssert.AreEqual(System.Array.ConvertAll(plain.Seats[0].Apricorns, a => 2 * a), s.Seats[0].Apricorns, "같은 색으로 두 배");
        }

        // ── bug_catcher B ──

        [Test]
        public void A20_6_곤충채집소년_벌레를잡으면JP2와추가턴_추가턴에는다시안됨()
        {
            ulong ok = Battles.Seed(sd => Throw(sd, new EventLog(), null, Kricketot, -1, out _, out _).Battle == null);
            var plain = Throw(ok, new EventLog(), null, Kricketot, -1, out _, out _);
            var log = new EventLog();
            var s = Throw(ok, log, "bug_catcher", Kricketot, -1, out _, out _);
            Assert.AreEqual(plain.Seats[0].Jp + TrainerRules.FrenzyJp, s.Seats[0].Jp);
            var start = log.OfType<TurnStarted>();
            Assert.AreEqual(1, start.Count);
            Assert.AreEqual(0, start[0].Seat, "같은 좌석의 추가 턴");
            Assert.IsTrue(start[0].IsExtra);
            Assert.AreEqual(s.TurnId, s.Seats[0].FrenzyTurn);

            Assert.AreEqual(plain.Seats[0].Jp, Throw(Battles.Seed(sd => Throw(sd, new EventLog(), null, Bidoof, -1, out _, out _).Battle == null), new EventLog(), "bug_catcher", Bidoof, -1, out _, out _).Seats[0].Jp, "벌레가 아니면 없다");

            // 그 추가 턴에는 다시 안 된다
            var again = Battles.Game(ctx, 2);
            Card(again, 0, "bug_catcher");
            again.Seats[0].FrenzyTurn = again.TurnId;
            Scene.Give(again, ctx, 0, Starly);
            again.Seats[0].Balls[(int)Ball.Regular] = 1;
            var alog = new EventLog();
            Battles.Wild(again, ctx, alog, 0, Scene.Spawn(again, ctx, Kricketot, SeatId.Wild));
            if (again.Pending.Count == 1 && again.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(again, ctx, again.Pending[0], ChoiceAnswer.Of(AnswerKind.Decline), alog);
            again.Rng = new Pcg32(ok);
            Battles.Answer(again, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), alog);
            Assert.AreEqual(plain.Seats[0].Jp, again.Seats[0].Jp, "JP도 없다");
            Assert.AreEqual(1, alog.OfType<TurnStarted>()[0].Seat, "다음 좌석으로");
        }

        // ── backpacker B ──

        [Test]
        public void A20_6_배낭여행자_배틀뒤한마리완전회복_강제고르기()
        {
            ulong ok = Battles.Seed(sd => Throw(sd, new EventLog(), "backpacker", Bidoof, -1, out _, out _, true).Battle == null);
            var log = new EventLog();
            var s = Throw(ok, log, "backpacker", Bidoof, -1, out int mine, out _, true);
            var c = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            Assert.AreEqual(new SourceRef(SourceKind.TrainerCard, s.Seats[0].TrainerCard), c.Source);
            var legal = Scene.Legal(s, ctx, c);
            Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.Decline), "「may」가 없다 — 안 함 없음");
            CollectionAssert.AreEqual(new[] { mine }, legal.ConvertAll(a => a.Target), "다친 포켓몬만(잡은 포켓몬은 멀쩡하다)");
            Assert.AreEqual(mine, c.Default.Target, "시간 초과 = 첫 합법 답");
            Assert.AreEqual(0, log.OfType<TurnEnded>().Count, "회복을 고르기 전에는 턴이 끝나지 않는다");
            Battles.Answer(s, ctx, c, ChoiceAnswer.Pick(mine), log);
            Assert.AreEqual(0, s.Pokemon[mine].Damage);
            Assert.AreEqual(1, log.OfType<Healed>().Count);
            Assert.AreEqual(1, log.OfType<TurnEnded>().Count, "고른 뒤 턴이 끝난다");

            var none = Throw(ok, new EventLog(), "backpacker", Bidoof, -1, out _, out _);
            Assert.AreEqual(0, none.Pending.FindAll(p => p.Kind == ChoiceKind.Reaction).Count, "다친 포켓몬이 없으면 묻지 않는다");
        }

        // ── delinquent B · ginkgo_merchant B ──

        [Test]
        public void A20_6_불량배_남이바친규토리를얻는다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 12, log);
            int seat = s.ActiveSeat, other = 1 - seat;
            var card = Card(s, other, "delinquent", TrainerRules.Silver);
            int shrine = -1;
            for (int f = 0; f < ctx.Fixtures.Fixtures.Count && shrine < 0; f++)
                if (ctx.Fixtures.Fixtures[f].Kind == FixtureKind.Shrine) shrine = f;
            s.Seats[seat].Cell = ctx.Fixtures.Fixtures[shrine].Cell;
            s.Seats[seat].Apricorns[(int)ApricornColor.Pink] = 1;
            int before = s.Seats[other].Apricorns[(int)ApricornColor.Pink];
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Offer, shrine, (int)ApricornColor.Pink), log);
            Assert.AreEqual(0, s.Seats[seat].Apricorns[(int)ApricornColor.Pink]);
            Assert.AreEqual(before + 1, s.Seats[other].Apricorns[(int)ApricornColor.Pink], "바친 분홍을 얻는다");
            var gained = log.OfType<ApricornsGained>().FindAll(e => e.Source.Equals(card));
            Assert.AreEqual(1, gained.Count);
            Assert.AreEqual(other, gained[0].Seat);

            // 자기가 바치면 없다
            var own = Flow.Started(ctx, 2, 12);
            int me = own.ActiveSeat;
            Card(own, me, "delinquent", TrainerRules.Silver);
            own.Seats[me].Cell = ctx.Fixtures.Fixtures[shrine].Cell;
            own.Seats[me].Apricorns[(int)ApricornColor.Pink] = 1;
            Flow.Answer(own, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Offer, shrine, (int)ApricornColor.Pink), null);
            Assert.AreEqual(0, own.Seats[me].Apricorns[(int)ApricornColor.Pink], "「another trainer」");
        }

        [Test]
        public void A20_6_은행상인_남이은행거래에낸규토리를얻는다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 43, log);
            int seat = s.ActiveSeat, other = 1 - seat;
            Card(s, other, "ginkgo_merchant", TrainerRules.Silver);
            var trades = ctx.Decks.Cards[BankRules.Card(s)].Trades;
            foreach (var c in trades[0].Give) s.Seats[seat].Apricorns[(int)c]++;
            var before = (int[])s.Seats[other].Apricorns.Clone();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Ginkgo, BankRules.Top), log);
            foreach (var c in trades[0].Give) before[(int)c]++;
            CollectionAssert.AreEqual(before, s.Seats[other].Apricorns, "낸 규토리(왼쪽)를 얻는다");
            Assert.AreEqual(seat, Flow.Only(s, ChoiceKind.MapAction).Seat, "거래한 좌석의 턴은 이어진다");
        }

        // ── interviewers B ──

        /// <summary>좌석 0(차례)이 마을 밖 칸에서 스탬프를 얻고, 좌석 1이 interviewers B. 좌석 1의 물음이 열린 판.</summary>
        GameState Scoop(EventLog log, bool village, out PendingChoice ask)
        {
            var s = Battles.Game(ctx, 2);
            Card(s, 1, "interviewers", TrainerRules.Silver);
            Scene.Give(s, ctx, 0, Bidoof);
            Scene.Give(s, ctx, 1, Garchomp);
            if (!village)
                for (int c = 0; c < ctx.Board.Count; c++)
                    if (ctx.Board[c].Wild && !ctx.Board[c].DistortionWorld && !BattleRules.InVillage(ctx, c)) { s.Seats[0].Cell = c; break; }
            StampRules.Gain(s, ctx, Scene.Fx(s, log, 0), 0, StampRules.AllApricorns);
            TurnFlow.OpenMapAction(s, ctx, Scene.Fx(s, log, 0), 0);
            ask = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.EffectOption);
            return s;
        }

        [Test]
        public void A20_6_인터뷰어_남이스탬프를얻으면가서도전_이기면그스탬프_도전당한턴은끝()
        {
            var log = new EventLog();
            var s = Scoop(log, false, out var ask);
            Assert.AreEqual(1, ask.Seat);
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind, "「may」 — 안 감이 기본 답");
            Battles.Answer(s, ctx, ask, ChoiceAnswer.Of(AnswerKind.Accept), log);
            Assert.AreEqual(s.Seats[0].Cell, s.Seats[1].Cell, "그 좌석의 칸으로 곧바로");
            Assert.AreEqual(1, log.OfType<Flew>().Count);
            Assert.IsNotNull(s.Battle, "마을 밖이라 바로 배틀");
            Assert.AreEqual(BattleKind.Trainer, s.Battle.Kind);
            Assert.AreEqual(1, s.Battle.Sides[0].Owner, "인터뷰어가 도전자");
            Battles.SetHp(s, ctx, s.Seats[0].Party[0], 1);
            Battles.Drive(s, ctx, log);
            Assert.IsNull(s.Battle);
            Assert.IsTrue(s.Seats[1].Stamps[StampRules.AllApricorns], "이겼으면 그 스탬프도");
            Assert.AreEqual(-1, s.Seats[1].ScoopTarget);
            var ended = log.OfType<TurnEnded>();
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual(0, ended[0].Seat, "도전당한 좌석의 턴이 끝난다(원작 커뮤니티 확인)");
        }

        [Test]
        public void A20_6_인터뷰어_안가면맵행동이이어지고_마을안에서는거절할수있다()
        {
            var log = new EventLog();
            var s = Scoop(log, false, out var ask);
            Battles.Answer(s, ctx, ask, ChoiceAnswer.Of(AnswerKind.Decline), log);
            Assert.AreEqual(0, Battles.Only(s, ChoiceKind.MapAction).Seat, "스탬프를 얻은 좌석의 맵 행동");
            Assert.AreEqual(-1, s.Seats[1].ScoopTarget);

            var v = Scoop(new EventLog(), true, out var vask);
            Battles.Answer(v, ctx, vask, ChoiceAnswer.Of(AnswerKind.Accept), new EventLog());
            var respond = Battles.Only(v, ChoiceKind.Respond, ChoiceSub.Challenge);
            Assert.AreEqual(0, respond.Seat, "마을 안: 도전받은 좌석이 거절할 수 있다");
            Battles.Answer(v, ctx, respond, ChoiceAnswer.Of(AnswerKind.Decline), new EventLog());
            Assert.AreEqual(0, Battles.Only(v, ChoiceKind.MapAction).Seat, "거절하면 그 좌석의 턴이 이어진다");
            Assert.IsFalse(v.Seats[1].Stamps[StampRules.AllApricorns]);
        }

        [Test]
        public void A20_6_인터뷰어_턴밖에서얻은스탬프에도도전_배틀만하고끝낼턴은없다_결정84()
        {
            // 사용자 결정 84 T14 나: 「Whenever another trainer gains a Journey Stamp」 — 그 좌석의 턴 밖이어도. 그때는 배틀만 하고 끝낼 턴은 없다(차례 좌석의 턴이 이어진다)
            var s = Battles.Game(ctx, 3);
            Card(s, 2, "interviewers", TrainerRules.Silver);
            Scene.Give(s, ctx, 0, Bidoof);
            int target = Scene.Give(s, ctx, 1, Bidoof);
            Scene.Give(s, ctx, 2, Garchomp);
            for (int c = 0; c < ctx.Board.Count; c++)
                if (ctx.Board[c].Wild && !ctx.Board[c].DistortionWorld && !BattleRules.InVillage(ctx, c)) { s.Seats[1].Cell = c; break; }
            var log = new EventLog();
            Assert.AreEqual(0, s.ActiveSeat);
            StampRules.Gain(s, ctx, Scene.Fx(s, log, 0), 1, StampRules.AllApricorns); // 차례가 아닌 좌석 1이 얻었다
            TurnFlow.OpenMapAction(s, ctx, Scene.Fx(s, log, 0), 0);
            var ask = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.EffectOption);
            Assert.AreEqual(2, ask.Seat);
            Assert.AreEqual(1, ask.Subject, "턴 밖에서 얻은 좌석에도 묻는다");
            Battles.Answer(s, ctx, ask, ChoiceAnswer.Of(AnswerKind.Accept), log);
            Assert.IsNotNull(s.Battle);
            Assert.AreEqual(2, s.Battle.Sides[0].Owner);
            Assert.AreEqual(1, s.Battle.Sides[1].Owner);
            Battles.SetHp(s, ctx, target, 1);
            Battles.Drive(s, ctx, log);
            Assert.IsNull(s.Battle);
            Assert.IsTrue(s.Seats[2].Stamps[StampRules.AllApricorns], "이기면 그 스탬프도");
            Assert.AreEqual(0, log.OfType<TurnEnded>().Count, "끝낼 턴이 없다");
            Assert.AreEqual(0, s.ActiveSeat);
            Assert.AreEqual(0, Battles.Only(s, ChoiceKind.MapAction).Seat, "차례 좌석의 맵 행동이 이어진다");
            Assert.AreEqual(0, s.Seats[2].ScoopAt);
            Assert.AreEqual(-1, s.Seats[2].ScoopTarget);
        }

        // ── 4인 판 순서 ──

        [Test]
        public void G6_4인판_배틀뒤카드물음순서_회복다음도전_그뒤턴끝()
        {
            var s = Battles.Game(ctx, 4);
            s.ActiveSeat = 1;
            var tent = Card(s, 1, "backpacker", TrainerRules.Silver);
            var scoop = Card(s, 3, "interviewers", TrainerRules.Silver);
            Card(s, 2, "gentleman", TrainerRules.Silver);
            foreach (int seat in new[] { 0, 2, 3 }) Scene.Give(s, ctx, seat, Garchomp);
            int mine = Scene.Give(s, ctx, 1, Garchomp);
            s.Pokemon[mine].Damage = 3;
            var log = new EventLog();
            int wild = Scene.Spawn(s, ctx, Bidoof, SeatId.Wild);
            Battles.SetHp(s, ctx, wild, 1);
            Battles.Wild(s, ctx, log, 1, wild);
            StampRules.Gain(s, ctx, Scene.Fx(s, log, 1), 1, StampRules.AllApricorns); // 배틀 도중에 받은 스탬프 — 배틀 뒤에 묻는다
            var order = new List<string>();
            for (int i = 0; i < 40 && s.Pending.Count > 0; i++)
            {
                var c = s.Pending[0];
                if (c.Kind == ChoiceKind.MapAction) break;
                if (c.Kind == ChoiceKind.Reaction && c.Source.Kind == SourceKind.TrainerCard) order.Add($"{c.Seat}:{c.Sub}");
                Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            }
            CollectionAssert.AreEqual(new[] { "1:EffectPick", "3:EffectOption" }, order, "배틀에 나온 좌석의 회복 → 배틀 밖 좌석의 도전(G6 — 배틀 쪽 먼저)");
            Assert.AreEqual(0, s.Pokemon[mine].Damage);
            var ended = log.OfType<TurnEnded>();
            Assert.AreEqual(1, ended[0].Seat);
            Assert.AreEqual(2, log.OfType<TurnStarted>()[0].Seat, "안 가면 그 턴이 끝나고 다음 좌석");
            Assert.IsTrue(log.OfType<AbilityUsed>().TrueForAll(e => e.CardKey == tent.Id), "좌석 0·2는 묻지 않는다 · 인터뷰어는 안 갔다");
            Assert.AreEqual(-1, s.Seats[3].ScoopTarget);
            Assert.AreNotEqual(scoop, tent);
        }
    }
}
