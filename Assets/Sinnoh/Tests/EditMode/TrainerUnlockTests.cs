using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「해금 뼈대」(rulings A20-2~5·A20-8 · G1, TrainerRules): 트레이너 카드 등급 올리기 · 해금 옵션 여섯(값마다 1개 이상) · CardUnlocked · 능력 물음 · 덮개(능력 102 + 전용 아이템 11).
    /// 실제 흐름으로 보는 것: 야생 격파 · 트레이너 배틀 격파 · 포획 · 진화 · 거래(등가 교환은 ShrineTests). 등급·순서의 갈래는 장면에서 고리(Defeated·Obtained)를 바로 부른다.
    /// </summary>
    public class TrainerUnlockTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const string Weak = "399:0", Moderate = "388:0", Strong = "445:0", Starter = "387:0", Legendary = "389:2", UltraBeast = "793:0", Commander = "42:0@jupiter";

        /// <summary>장면 게임(좌석마다 고향 마을 · 브론즈 트레이너 카드 — 좌석 i = 카드 표 i번째).</summary>
        GameState Game(RuleOptions opt = null, int seats = 2)
        {
            var s = Battles.Game(ctx, seats, 7, opt);
            for (int i = 0; i < seats; i++)
            {
                s.Seats[i].TrainerCard = i;
                s.Seats[i].TrainerCardTier = TrainerRules.Bronze;
            }
            return s;
        }

        static RuleOptions Opt(System.Action<RuleOptions> set)
        {
            var o = new RuleOptions();
            set(o);
            return o;
        }

        /// <summary>좌석 0이 한쪽인 배틀에서 상대(owner)의 포켓몬 key가 기절한 자리의 고리(BattleFlow.ReportFaint가 부르는 것).</summary>
        EventLog Faint(GameState s, string key, int owner = SeatId.Wild, bool alpha = false, BattleKind kind = BattleKind.Wild)
        {
            var log = new EventLog();
            int foe = owner >= 0 ? Scene.Give(s, ctx, owner, key) : Scene.Spawn(s, ctx, key, owner);
            s.Pokemon[foe].Alpha = alpha;
            if (PokemonRules.PartyCount(s.Seats[0]) == 0) Scene.Give(s, ctx, 0, Strong);
            var b = Scene.Battle(s, kind, 0, owner, foe);
            TrainerRules.Defeated(s, ctx, Scene.Fx(s, log, 0, ActionKind.BattleAction), b, 1, foe);
            s.Battle = null;
            s.Phase = Phase.Turn;
            return log;
        }

        EventLog Obtain(GameState s, string key, ObtainCause cause = ObtainCause.Capture, bool alpha = false)
        {
            var log = new EventLog();
            int p = Scene.Spawn(s, ctx, key, 0);
            s.Pokemon[p].Alpha = alpha;
            TrainerRules.Obtained(s, ctx, Scene.Fx(s, log, 0, ActionKind.BattleAction), 0, p, cause);
            return log;
        }

        static int Tier(GameState s, int seat = 0) => s.Seats[seat].TrainerCardTier;

        // ── A20-2: 등급 ──

        [Test]
        public void A20_2_보통야생을쓰러뜨리면실버_파랑이풀리고_CardUnlocked값()
        {
            var s = Game();
            Scene.Give(s, ctx, 0, Strong);
            int wild = Scene.Spawn(s, ctx, Moderate, SeatId.Wild);
            Battles.SetHp(s, ctx, wild, 1);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild);
            Battles.Drive(s, ctx, log);
            Assert.IsTrue(log.OfType<Fainted>().Exists(f => f.PokemonId == wild), "야생이 기절했다");
            Assert.AreEqual(TrainerRules.Silver, Tier(s), "「Once you defeat or obtain 1 Moderate Pokémon, upgrade to the Silver card」");
            var e = log.OfType<CardUnlocked>();
            Assert.AreEqual(1, e.Count);
            Assert.AreEqual(0, e[0].Seat);
            Assert.AreEqual(s.Seats[0].TrainerCard, e[0].CardKey);
            Assert.AreEqual(new SourceRef(SourceKind.TrainerCard, s.Seats[0].TrainerCard), e[0].Source);
            Assert.AreEqual(1, e[0].TierBefore);
            Assert.AreEqual(2, e[0].TierAfter);
            Assert.AreEqual(wild, e[0].CausePokemonId);
            Assert.AreEqual(EncounterTier.Moderate, e[0].CauseGrade);
            CollectionAssert.AreEqual(new[] { 1 }, e[0].AbilitySlots, "파랑(칸 1)");
            Assert.Less(log.Events.IndexOf(log.OfType<Fainted>().Find(f => f.PokemonId == wild)), log.Events.IndexOf(e[0]), "기절 알림 뒤");
            string key = ctx.Cards.TrainerCards[s.Seats[0].TrainerCard].Key;
            Assert.IsTrue(TrainerRules.Has(s, ctx, 0, key, AbilityColor.Blue));
            Assert.IsFalse(TrainerRules.Has(s, ctx, 0, key, AbilityColor.Red), "빨강은 골드부터");
            Assert.AreEqual(TrainerRules.Bronze, Tier(s, 1), "남의 좌석은 그대로");
        }

        [Test]
        public void A20_2_실버에서강함을쓰러뜨리면골드_골드에서는더오르지도내려가지도않는다()
        {
            var s = Game();
            s.Seats[0].TrainerCardTier = TrainerRules.Silver;
            Assert.AreEqual(0, Faint(s, Moderate).OfType<CardUnlocked>().Count, "실버에서의 보통은 무시");
            Assert.AreEqual(0, Faint(s, Weak).OfType<CardUnlocked>().Count, "약한 포켓몬");
            var e = Faint(s, Strong).OfType<CardUnlocked>();
            Assert.AreEqual(TrainerRules.Gold, Tier(s), "「once you defeat or obtain 1 Strong Pokémon, upgrade to the Gold card」");
            Assert.AreEqual(1, e.Count);
            Assert.AreEqual(EncounterTier.Strong, e[0].CauseGrade);
            CollectionAssert.AreEqual(new[] { 2 }, e[0].AbilitySlots, "빨강(칸 2)");
            Assert.AreEqual(0, Faint(s, Strong).OfType<CardUnlocked>().Count + Faint(s, Moderate).OfType<CardUnlocked>().Count, "골드면 무시");
            Assert.AreEqual(TrainerRules.Gold, Tier(s));
        }

        // ── A20-3: 쓰러뜨림·얻음의 범위 ──

        [Test]
        public void A20_3_갤럭시단_스타터_약한포켓몬_울트라비스트는세지않는다()
        {
            var s = Game();
            s.Seats[0].TrainerCardTier = TrainerRules.Silver; // 보통·강함 어느 쪽으로도 오를 수 있는 자리에서
            Assert.AreEqual(EncounterTier.Commander, ctx.Cards.Pokemon[Scene.Def(ctx, Commander)].Tier, "갤럭시단 카드는 전용 아이콘(등급이 Moderate·Strong이 아니다)");
            Faint(s, Commander, SeatId.Galactic, kind: BattleKind.Galactic);
            Faint(s, Starter);
            Faint(s, Weak);
            Faint(s, UltraBeast, SeatId.UltraBeast, kind: BattleKind.UltraBeast);
            Obtain(s, Starter);
            Obtain(s, Weak);
            Assert.AreEqual(TrainerRules.Silver, Tier(s));
            s.Seats[0].TrainerCardTier = TrainerRules.Bronze;
            Faint(s, Commander, SeatId.Galactic, kind: BattleKind.Galactic);
            Obtain(s, Starter);
            Assert.AreEqual(TrainerRules.Bronze, Tier(s));
        }

        /// <summary>트레이너 배틀: 좌석 0의 한카리아스가 좌석 1의 체력 1짜리 그로우(보통)를 쓰러뜨릴 때까지.</summary>
        GameState BeatOtherTrainer(RuleOptions opt, EventLog log)
        {
            var s = Game(opt);
            Scene.Give(s, ctx, 0, Strong);
            int foe = Scene.Give(s, ctx, 1, Moderate);
            Battles.SetHp(s, ctx, foe, 1);
            Battles.Start(s, ctx, log, BattleKind.Trainer, 0, 1, -1);
            Battles.Drive(s, ctx, log);
            Assert.IsTrue(log.OfType<Fainted>().Exists(f => f.PokemonId == foe), "남의 그로우가 기절했다");
            return s;
        }

        [Test]
        public void A20_3_남의보통포켓몬을쓰러뜨려도실버_UnlockDefeatOtherTrainer켬()
        {
            var log = new EventLog();
            var s = BeatOtherTrainer(null, log);
            Assert.IsTrue(s.Options.UnlockDefeatOtherTrainer, "기본 켬(원작 커뮤니티 확인)");
            Assert.AreEqual(TrainerRules.Silver, Tier(s));
            Assert.AreEqual(1, log.OfType<CardUnlocked>().Count);
        }

        [Test]
        public void A20_3_UnlockDefeatOtherTrainer끔이면남의포켓몬은세지않는다_야생은센다()
        {
            var log = new EventLog();
            var s = BeatOtherTrainer(Opt(o => o.UnlockDefeatOtherTrainer = false), log);
            Assert.AreEqual(TrainerRules.Bronze, Tier(s));
            Assert.AreEqual(0, log.OfType<CardUnlocked>().Count);
            Faint(s, Moderate);
            Assert.AreEqual(TrainerRules.Silver, Tier(s), "야생은 옵션과 상관없다");
        }

        GameState Capture(ulong seed, EventLog log, int partyExtra, out int wild)
        {
            var s = Game();
            int mine = Scene.Give(s, ctx, 0, Strong);
            for (int i = 0; i < partyExtra; i++) Scene.Give(s, ctx, 0, Weak);
            s.Seats[0].Balls[(int)Ball.Keystone] = 1;
            wild = Scene.Spawn(s, ctx, Moderate, SeatId.Wild);
            Battles.Wild(s, ctx, log, 0, wild);
            if (partyExtra > 0) Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(mine), log);
            s.Rng = new Pcg32(seed);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Keystone), log);
            return s;
        }

        [Test]
        public void A20_3_보통포켓몬을포획하면실버_파티가가득해도잡은순간()
        {
            foreach (int extra in new[] { 0, 2 })
            {
                ulong seed = Battles.Seed(sd =>
                {
                    var l = new EventLog();
                    Capture(sd, l, extra, out _);
                    var a = l.OfType<CaptureAttempt>();
                    return a.Count == 1 && a[0].Success;
                });
                var log = new EventLog();
                var s = Capture(seed, log, extra, out int wild);
                Assert.AreEqual(TrainerRules.Silver, Tier(s), $"파티 {1 + extra}: 「obtain」 = 포획");
                var e = log.OfType<CardUnlocked>();
                Assert.AreEqual(1, e.Count);
                Assert.AreEqual(wild, e[0].CausePokemonId);
                Assert.Less(log.Events.IndexOf(log.OfType<CaptureAttempt>()[0]), log.Events.IndexOf(e[0]), "포획 판정 뒤");
            }
        }

        /// <summary>같은 칸 거래: 좌석 0이 그로우(보통)를 주고 좌석 1이 JP 1을 준다.</summary>
        GameState TradeModerate(RuleOptions opt, EventLog log)
        {
            var s = Game(opt);
            int cell = Flow.StepInto(s, ctx, s.Seats[0].Cell);
            s.Seats[0].Cell = s.Seats[1].Cell = cell;
            Scene.Give(s, ctx, 0, Starter);
            int grotle = Scene.Give(s, ctx, 0, Moderate);
            Scene.Give(s, ctx, 1, Starter);
            s.Seats[1].Jp = 3;
            var map = Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = 0 });
            var offer = TradeRules.Offer(1, 0, (int)TradeKind.Pokemon, grotle, 1, 1, (int)TradeKind.Jp, -1, 1);
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(map.Id, offer), log), "거래 제안");
            var respond = s.Pending.Find(c => c.Kind == ChoiceKind.Respond);
            Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(respond.Id, ChoiceAnswer.Of(AnswerKind.Accept)), log), "거래 받아들임");
            Assert.AreEqual(1, log.OfType<TradeDone>().Count);
            Assert.AreEqual(1, s.Pokemon[grotle].Owner);
            return s;
        }

        [Test]
        public void A20_3_거래로받은보통포켓몬은UnlockTrade끔이면얻음이아니다()
        {
            var log = new EventLog();
            var s = TradeModerate(null, log);
            Assert.IsFalse(s.Options.UnlockTrade, "기본 끔(결정 76 ⑦ T3 나)");
            Assert.AreEqual(TrainerRules.Bronze, Tier(s, 1));
            Assert.AreEqual(0, log.OfType<CardUnlocked>().Count);
        }

        [Test]
        public void A20_3_UnlockTrade켬이면거래로받은보통포켓몬으로실버()
        {
            var log = new EventLog();
            var s = TradeModerate(Opt(o => o.UnlockTrade = true), log);
            Assert.AreEqual(TrainerRules.Silver, Tier(s, 1), "받은 좌석");
            Assert.AreEqual(TrainerRules.Bronze, Tier(s, 0), "준 좌석은 그대로");
            var e = log.OfType<CardUnlocked>();
            Assert.AreEqual(1, e.Count);
            Assert.AreEqual(1, e[0].Seat);
            Assert.Less(log.Events.IndexOf(log.OfType<TradeDone>()[0]), log.Events.IndexOf(e[0]), "거래 결과 뒤");
        }

        // ── A20-4: 진화 · 전설 · 우두머리 ──

        /// <summary>실제 판의 첫 맵 행동에서 비버니(약한)를 비버통(보통)으로 진화시킨다.</summary>
        GameState EvolveToModerate(RuleOptions opt, EventLog log)
        {
            var s = Flow.Started(ctx, 2, 37, null, opt);
            int seat = s.ActiveSeat;
            Assert.AreEqual(TrainerRules.Bronze, Tier(s, seat), "셋업 뒤 브론즈");
            int bidoof = Scene.Give(s, ctx, seat, Weak);
            s.Seats[seat].Jp = 10;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Evolve, bidoof), log);
            Assert.AreEqual(EncounterTier.Moderate, ctx.Cards.Pokemon[s.Pokemon[bidoof].Def].Tier, "비버통 = 보통");
            return s;
        }

        [Test]
        public void A20_4_진화로보통이되면얻음_UnlockByEvolution켬()
        {
            var log = new EventLog();
            var s = EvolveToModerate(null, log);
            Assert.IsTrue(s.Options.UnlockByEvolution, "기본 켬(원작 커뮤니티 확인)");
            Assert.AreEqual(TrainerRules.Silver, Tier(s, s.ActiveSeat));
            var e = log.OfType<CardUnlocked>();
            Assert.AreEqual(1, e.Count);
            Assert.Less(log.Events.IndexOf(log.OfType<Evolved>()[0]), log.Events.IndexOf(e[0]), "진화 뒤");
        }

        [Test]
        public void A20_4_UnlockByEvolution끔이면진화는얻음이아니다()
        {
            var log = new EventLog();
            var s = EvolveToModerate(Opt(o => o.UnlockByEvolution = false), log);
            Assert.AreEqual(TrainerRules.Bronze, Tier(s, s.ActiveSeat));
            Assert.AreEqual(0, log.OfType<CardUnlocked>().Count);
        }

        [Test]
        public void A20_4_전설은강함이아니다_UnlockLegendaryAsStrong끔()
        {
            var s = Game();
            s.Seats[0].TrainerCardTier = TrainerRules.Silver;
            Assert.AreEqual(EncounterTier.Legendary, ctx.Cards.Pokemon[Scene.Def(ctx, Legendary)].Tier);
            Assert.IsFalse(s.Options.UnlockLegendaryAsStrong, "기본 끔(원작 커뮤니티 확인)");
            Faint(s, Legendary);
            Obtain(s, Legendary);
            Assert.AreEqual(TrainerRules.Silver, Tier(s), "「it specifically mentions a Strong Pokemon」");
        }

        [Test]
        public void A20_4_UnlockLegendaryAsStrong켬이면전설이강함조건을채운다()
        {
            var s = Game(Opt(o => o.UnlockLegendaryAsStrong = true));
            Faint(s, Legendary);
            Assert.AreEqual(TrainerRules.Bronze, Tier(s), "강함으로 세므로 브론즈에서는 순서 규칙(SilverOnly) 그대로");
            s.Seats[0].TrainerCardTier = TrainerRules.Silver;
            var e = Faint(s, Legendary).OfType<CardUnlocked>();
            Assert.AreEqual(TrainerRules.Gold, Tier(s));
            Assert.AreEqual(EncounterTier.Strong, e[0].CauseGrade, "센 등급");
        }

        [Test]
        public void A20_4_우두머리는강함이아니다_UnlockAlphaAsStrong끔()
        {
            var s = Game();
            s.Seats[0].TrainerCardTier = TrainerRules.Silver;
            Assert.IsFalse(s.Options.UnlockAlphaAsStrong, "기본 끔(원작 커뮤니티 확인)");
            Faint(s, Strong, alpha: true);
            Obtain(s, Strong, alpha: true);
            Assert.AreEqual(TrainerRules.Silver, Tier(s), "강함 카드여도 우두머리 토큰이 붙으면 전설");
            Faint(s, Strong);
            Assert.AreEqual(TrainerRules.Gold, Tier(s), "토큰 없는 같은 카드는 강함");
        }

        [Test]
        public void A20_4_UnlockAlphaAsStrong켬이면우두머리가강함조건을채운다()
        {
            var s = Game(Opt(o => o.UnlockAlphaAsStrong = true));
            s.Seats[0].TrainerCardTier = TrainerRules.Silver;
            Obtain(s, Strong, alpha: true);
            Assert.AreEqual(TrainerRules.Gold, Tier(s));
        }

        // ── A20-5: 순서 ──

        [Test]
        public void A20_5_SilverOnly_브론즈에서강함은아무일도없고_실버뒤의강함으로골드()
        {
            var s = Game();
            Assert.AreEqual(UnlockOrder.SilverOnly, s.Options.UnlockOrder, "기본(plan §5 #68 ①)");
            Assert.AreEqual(0, Faint(s, Strong).OfType<CardUnlocked>().Count);
            Assert.AreEqual(TrainerRules.Bronze, Tier(s), "원작 커뮤니티 확인");
            Faint(s, Moderate);
            Assert.AreEqual(TrainerRules.Silver, Tier(s), "먼저 한 강함은 기억하지 않는다(원작 커뮤니티 확인)");
            Obtain(s, Strong);
            Assert.AreEqual(TrainerRules.Gold, Tier(s));
        }

        [Test]
        public void A20_5_RememberThenGold_먼저채운강함을기억했다가보통에서바로골드()
        {
            var s = Game(Opt(o => o.UnlockOrder = UnlockOrder.RememberThenGold));
            Assert.AreEqual(0, Faint(s, Strong).OfType<CardUnlocked>().Count);
            Assert.AreEqual(TrainerRules.Bronze, Tier(s));
            Assert.IsTrue(s.Seats[0].StrongRemembered);
            var e = Obtain(s, Moderate).OfType<CardUnlocked>();
            Assert.AreEqual(TrainerRules.Gold, Tier(s), "원작 커뮤니티 확인");
            Assert.AreEqual(1, e.Count);
            Assert.AreEqual(1, e[0].TierBefore);
            Assert.AreEqual(3, e[0].TierAfter);
            CollectionAssert.AreEqual(new[] { 1, 2 }, e[0].AbilitySlots, "파랑·빨강 둘 다(파랑 먼저)");
            Assert.IsFalse(s.Seats[0].StrongRemembered);

            // 기억이 없으면 보통은 실버까지만
            var o = Game(Opt(x => x.UnlockOrder = UnlockOrder.RememberThenGold));
            Faint(o, Moderate);
            Assert.AreEqual(TrainerRules.Silver, Tier(o));
        }

        [Test]
        public void A20_5_StraightToGold_브론즈에서강함이면바로골드()
        {
            var s = Game(Opt(o => o.UnlockOrder = UnlockOrder.StraightToGold));
            var e = Faint(s, Strong).OfType<CardUnlocked>();
            Assert.AreEqual(TrainerRules.Gold, Tier(s));
            Assert.AreEqual(EncounterTier.Strong, e[0].CauseGrade);
            CollectionAssert.AreEqual(new[] { 1, 2 }, e[0].AbilitySlots);
            var o = Game(Opt(x => x.UnlockOrder = UnlockOrder.StraightToGold));
            Faint(o, Moderate);
            Assert.AreEqual(TrainerRules.Silver, Tier(o), "보통은 실버");
        }

        // ── A20-8 · 능력 물음 ──

        [Test]
        public void A20_8_트레이너카드끄면해금없음()
        {
            var off = Opt(o => o.TrainerCards = false);
            var s = Flow.Started(ctx, 2, 7, null, off);
            Assert.AreEqual(-1, s.Seats[0].TrainerCard);
            Assert.AreEqual(0, Tier(s));
            var log = new EventLog();
            int p = Scene.Spawn(s, ctx, Moderate, 0);
            TrainerRules.Obtained(s, ctx, Scene.Fx(s, log, 0, ActionKind.BattleAction), 0, p, ObtainCause.Capture);
            Assert.AreEqual(0, Tier(s));
            Assert.AreEqual(0, log.OfType<CardUnlocked>().Count);
            Assert.IsFalse(TrainerRules.Has(s, ctx, 0, "ace_trainer", AbilityColor.Blue));

            // 카드를 쥔 판이어도 옵션이 꺼져 있으면 오르지 않는다
            var g = Game(off);
            Assert.AreEqual(0, Faint(g, Moderate).OfType<CardUnlocked>().Count);
            Assert.AreEqual(TrainerRules.Bronze, Tier(g));
        }

        [Test]
        public void A20_2_능력물음은카드와등급으로센다()
        {
            var s = Game();
            string mine = ctx.Cards.TrainerCards[0].Key, other = ctx.Cards.TrainerCards[1].Key;
            foreach (int tier in new[] { 1, 2, 3 })
            {
                s.Seats[0].TrainerCardTier = tier;
                Assert.AreEqual(tier >= 2, TrainerRules.Has(s, ctx, 0, mine, AbilityColor.Blue), $"등급 {tier} 파랑");
                Assert.AreEqual(tier >= 3, TrainerRules.Has(s, ctx, 0, mine, AbilityColor.Red), $"등급 {tier} 빨강");
                Assert.IsFalse(TrainerRules.Has(s, ctx, 0, other, AbilityColor.Blue), "남의 카드");
            }
            Assert.IsFalse(TrainerRules.Has(s, ctx, SeatId.Wild, mine, AbilityColor.Blue), "가상 좌석");
            Assert.Throws<KeyNotFoundException>(() => TrainerRules.Has(s, ctx, 0, "no_such_card", AbilityColor.Blue));

            var log = new EventLog();
            TrainerRules.Used(s, Scene.Fx(s, log, 0, ActionKind.UseAbility), 0, AbilityColor.Red, 5);
            var u = log.OfType<AbilityUsed>()[0];
            Assert.AreEqual(0, u.Seat);
            Assert.AreEqual(0, u.CardKey);
            Assert.AreEqual(AbilityColor.Red, u.Ability);
            CollectionAssert.AreEqual(new[] { 5 }, u.Targets);
        }

        // ── 덮개 · 전용 아이템 ──

        /// <summary>
        /// plan M6 완료 조건 3의 덮개: 능력 102 + 전용 아이템 11 가운데 TrainerRules.Ported에 없는 것 = 미구현. 묶음마다 이 숫자를 줄여 「전용 아이템 능력」 묶음에서 0으로 만든다
        /// (지금 93 = 113 − 표시용 주사위 2 − 상시 보정 18).
        /// </summary>
        [Test]
        public void M6_능력덮개_미구현수()
        {
            var all = TrainerRules.AllKeys(ctx);
            Assert.AreEqual(113, all.Count, "능력 102 + 전용 아이템 11");
            Assert.AreEqual(102, all.FindAll(k => k.StartsWith("trainer_ability.")).Count);
            Assert.AreEqual(all.Count, new HashSet<string>(all).Count, "겹친 키 없음");
            var strings = TestData.LoadStrings("en");
            foreach (string k in all) Assert.IsTrue(strings.Contains(k), $"{k}: 문자열 표의 키와 같다");
            foreach (var kv in TrainerRules.Ported)
            {
                CollectionAssert.Contains(all, kv.Key, "등록부의 키는 능력·전용 아이템 키");
                Assert.IsNotEmpty(kv.Value, $"{kv.Key}: 어디서 처리하나(또는 효과 없음 근거)");
            }
            var left = TrainerRules.Unported(ctx);
            TestContext.WriteLine($"[M6 능력 덮개] 미구현 {left.Count}/113: {string.Join(" ", left)}");
            Assert.AreEqual(0, left.FindAll(k => k.StartsWith("trainer_ability.")).Count, "남은 능력");
            Assert.AreEqual(0, left.FindAll(k => k.StartsWith("item.")).Count, "남은 전용 아이템");
            Assert.AreEqual(0, left.Count, "남은 미구현 수 — 능력을 옮긴 묶음이 이 숫자를 줄인다");
        }

        [Test]
        public void G1_전용아이템11행_표시용주사위둘은효과없음으로닫는다()
        {
            var items = new List<ItemDef>();
            foreach (var it in ctx.Items.Items)
                if (it.Kind == ItemKind.TrainerItem) items.Add(it);
            CollectionAssert.AreEquivalent(new[]
            {
                "item.apex_dragon", "item.substitute", "item.weather_wheel", "item.weather_wheel_base", "item.amplifier", "item.catch_size_dice",
                "item.health_dice", "item.initiative_dice", "item.cherish_ball", "item.landmark_tour", "item.follower_of_arceus",
            }, items.ConvertAll(i => i.Key), "원작 가방 save 22ec55의 11개");
            var ko = TestData.LoadStrings("ko");
            foreach (var it in items)
            {
                Assert.IsNotNull(it.En, it.Key);
                Assert.IsNull(it.Recipe, $"{it.Key}: 제작하지 않는다");
                Assert.IsTrue(ko.Contains(it.Key) && ko.Contains(it.Key + ".text"), $"{it.Key}: 이름·설명 문자열");
            }
            Assert.AreEqual(44, ctx.Items.Ids.IndexOf(items[0].Key), "앞의 44행(세이브 토큰·스튜·스탬프)의 색인은 그대로 — 새 행은 뒤에 붙는다");
            Assert.IsTrue(TrainerRules.Ported.ContainsKey("item.health_dice") && TrainerRules.Ported.ContainsKey("item.initiative_dice"), "표시용 — 규칙 효과 없음(rulings G1)");
            StringAssert.Contains("+6 health, +2 initiative and +1 attack strength", ctx.Items.Items[ctx.Items.Ids.IndexOf("item.apex_dragon")].En);
        }
    }
}
