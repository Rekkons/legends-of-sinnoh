using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「무녀·카드 바꾸기」(rulings A40 — 능력 8 + 아르세우스의 추종자): diamond·pearl·platinum_acolyte B·R · undivided_acolyte B · youngster R.
    /// 해금은 실제 고리로 일으킨다 — 카드 효과로 얻음(TrainerRules.Obtained) 또는 야생 배틀 격파. 미뤄 둔 고르기(카드 바꾸기 · 파티 가득 놓아주기)는 배틀 뒤 턴 끝 앞에 열린다.
    /// </summary>
    public class TrainerAcolyteTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const string Moderate = "388:0", Strong = "445:0", SlowStrong = "242:0", Weak = "399:0";

        static readonly string[] Acolytes = { "diamond_acolyte", "pearl_acolyte", "platinum_acolyte" };
        static readonly SinnohGod[] Gods = { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina };
        static readonly FateCard[] GodCards = { FateCard.Dialga, FateCard.Palkia, FateCard.Giratina };

        int CardId(string key) => ctx.Cards.TrainerCardIds.IndexOf(key);

        GameState Game(string card, int tier, RuleOptions opt = null)
        {
            var s = Battles.Game(ctx, 2, 7, opt);
            s.Seats[0].TrainerCard = CardId(card);
            s.Seats[0].TrainerCardTier = tier;
            s.Decks[(int)DeckId.TrainerCard].Remove(CardId(card));
            Scene.Give(s, ctx, 0, Weak);
            return s;
        }

        /// <summary>카드 효과로 얻은 것처럼 등급 하나를 올린다(브론즈 + 보통 → 실버 · 실버 + 강함 → 골드).</summary>
        EventLog Unlock(GameState s, string key)
        {
            var log = new EventLog();
            int p = Scene.Spawn(s, ctx, key, 0);
            TrainerRules.Obtained(s, ctx, Scene.Fx(s, log, 0, ActionKind.BattleAction), 0, p, ObtainCause.CardEffect);
            return log;
        }

        static void Hand(GameState s, Ctx ctx, params FateCard[] cards)
        {
            s.Seats[0].FateHand.Clear();
            foreach (var c in cards) s.Seats[0].FateHand.Add(FateRules.CardKey(ctx, c));
        }

        /// <summary>좌석 0이 1 체력 야생 key를 이긴다(일반 타격 넷) — 배틀 중 해금되고 배틀 뒤 턴 끝 앞의 물음에서 멈춘다(배틀 뒤 가르치기는 안 가르침).</summary>
        void WinAgainst(GameState s, string key, EventLog log)
        {
            int mine = Scene.Give(s, ctx, 0, Strong);
            int wild = Scene.Spawn(s, ctx, key, SeatId.Wild);
            Battles.SetHp(s, ctx, wild, 1);
            var party = s.Seats[0].Party;
            for (int i = 0; i < party.Length; i++)
                if (party[i] == mine && i > 0) // 이길 포켓몬을 맨 앞에
                {
                    party[i] = party[0];
                    party[0] = mine;
                }
            Battles.Wild(s, ctx, log, 0, wild);
            for (int guard = 0; guard < 8 && s.Battle != null; guard++)
            {
                var c = s.Pending.Find(x => !x.Answered);
                if (c.Kind == ChoiceKind.BattleAction)
                {
                    var roll = new[] { AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.RegularHit };
                    s.Rng = new Pcg32(Battles.Seed(seed => Same(AttackDice.Roll(new Pcg32(seed), 4), roll)));
                    Battles.Answer(s, ctx, c, Battles.Move(ctx.Cards.Pokemon[s.Pokemon[mine].Def].Signature), log);
                }
                else Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            }
            for (int guard = 0; guard < 8; guard++)
            {
                var c = s.Pending.Find(x => !x.Answered && x.Window == Window.BattleEnd);
                if (c == null) break;
                Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            }
            Assert.IsNull(s.Battle, "배틀이 끝났다");
        }

        static bool Same(AttackDieFace[] a, AttackDieFace[] b)
        {
            for (int i = 0; i < b.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        PendingChoice CardAsk(GameState s) => s.Pending.Find(c => !c.Answered && c.Source.Kind == SourceKind.TrainerCard);

        // ── 무녀 셋 파랑 ──

        [Test]
        public void A20_6_무녀셋파랑_해금되면그신의추종자로고정_3장효과는그신카드나아르세우스1장()
        {
            for (int i = 0; i < Acolytes.Length; i++)
            {
                var s = Game(Acolytes[i], TrainerRules.Bronze);
                Hand(s, ctx, GodCards[i]);
                Assert.IsEmpty(FateRules.WildSplits(s, ctx, 0, GodCards[i]), $"{Acolytes[i]}: 해금 전에는 1장으로 3장 효과를 못 낸다");
                var log = Unlock(s, Moderate);
                Assert.AreEqual(Gods[i], s.Seats[0].Follower, $"{Acolytes[i]}: 「become a Follower of {Gods[i]} for the rest of the game」");
                Assert.IsTrue(log.OfType<AbilityUsed>().Exists(e => e.Ability == AbilityColor.Blue));
                Assert.AreEqual(2, FateRules.FollowerWorth(s, ctx, 0, GodCards[i]), "「always counts as 2 of the cards needed」");
                Assert.AreEqual(0, FateRules.FollowerWorth(s, ctx, 0, GodCards[(i + 1) % 3]), "다른 신은 따르지 않는다");
                CollectionAssert.AreEqual(new[] { 0 }, FateRules.WildSplits(s, ctx, 0, GodCards[i]), "그 신 카드 1장으로 3장 효과");
                var pay = new List<int>();
                Assert.IsTrue(FateRules.Payment(s, ctx, 0, GodCards[i], true, pay));
                Assert.AreEqual(1, pay.Count, "1장만 버린다");
                Hand(s, ctx, FateCard.Arceus);
                CollectionAssert.AreEqual(new[] { 1 }, FateRules.WildSplits(s, ctx, 0, GodCards[i]), "늦은 제작자 답: 그 신 카드 대신 아르세우스 1장도 [가정 §5 #139 ③]");
                Assert.IsTrue(TrainerRules.FixedFollower(s, ctx, 0));
            }
        }

        [Test]
        public void A20_6_무녀파랑_천상의균열비밀선택은그대로만_아르세우스의추종자도()
        {
            var s = Game("diamond_acolyte", TrainerRules.Silver);
            s.Seats[0].Follower = SinnohGod.Dialga;
            s.Seats[1].TrainerCard = CardId("undivided_acolyte");
            s.Seats[1].TrainerCardTier = TrainerRules.Silver;
            s.Seats[1].Follower = SinnohGod.Arceus;
            var log = new EventLog();
            FractureRules.Open(s, ctx, Scene.Fx(s, log, 0), 0, -1);
            var mine = s.Pending.Find(c => c.Seat == 0 && c.Sub == ChoiceSub.Follower);
            var other = s.Pending.Find(c => c.Seat == 1 && c.Sub == ChoiceSub.Follower);
            var a = Scene.Legal(s, ctx, mine);
            Assert.AreEqual(1, a.Count, "고정 — 바꿀 수 없다");
            Assert.AreEqual(ChoiceAnswer.Pick((int)SinnohGod.Dialga).ToString(), a[0].ToString());
            var b = Scene.Legal(s, ctx, other);
            Assert.AreEqual(1, b.Count);
            Assert.AreEqual(AnswerKind.Decline, b[0].Kind, "아르세우스의 추종자 그대로");
            Assert.IsTrue(ChoiceRules.Contains(a, mine.Default) && ChoiceRules.Contains(b, other.Default), "기본 답도 합법");
        }

        // ── 무녀 셋 빨강 ──

        [Test]
        public void A20_6_무녀셋빨강_전설덱의신을파티로_신토큰은빠지고스탬프는없다()
        {
            for (int i = 0; i < Acolytes.Length; i++)
            {
                var s = Game(Acolytes[i], TrainerRules.Silver);
                int card = FractureRules.GodCard(ctx, Gods[i]);
                // 시작의 사당에 그 신 토큰이 내려와 있다
                s.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = card, Cell = HosFlow.OriginShrineCell(ctx), FaceUp = true });
                Assert.IsTrue(FractureRules.InGame(s, ctx, Gods[i]));
                var log = Unlock(s, Strong);
                Assert.AreEqual(TrainerRules.Gold, s.Seats[0].TrainerCardTier);
                var gained = log.OfType<PokemonGained>();
                Assert.AreEqual(1, gained.Count, $"{Acolytes[i]}: 「search the Legendary Pokémon deck for {Gods[i]} and add it to your party」");
                Assert.AreEqual(card, gained[0].Species);
                Assert.GreaterOrEqual(PokemonRules.PartyIndexOf(s.Seats[0], gained[0].PokemonId), 0);
                Assert.IsTrue(s.Pokemon[gained[0].PokemonId].Chosen);
                Assert.IsFalse(FractureRules.InGame(s, ctx, Gods[i]), "전설 덱에서 빠졌다(잡은 것처럼)");
                Assert.AreEqual(SinnohGod.None, FractureRules.OriginGod(s, ctx), "신 토큰은 맵에서 빠진다");
                Assert.AreEqual(1, log.OfType<TokenRemoved>().Count);
                Assert.AreEqual(0, log.OfType<StampGained>().Count, "「전설 포획」 스탬프가 아니다(A33)");
            }
        }

        [Test]
        public void G2_무녀빨강_다시세기_없는조각만큼약하고조각을얻으면회복()
        {
            var s = Game("pearl_acolyte", TrainerRules.Silver);
            Unlock(s, Strong);
            int god = s.Seats[0].Party[PokemonRules.PartyCount(s.Seats[0]) - 1];
            var def = ctx.Cards.Pokemon[s.Pokemon[god].Def];
            Assert.AreEqual(3, TrainerRules.Toll(s, 0), "조각 0개 = 셋이 없다");
            Assert.AreEqual(def.Health - 3, PokemonRules.MaxHp(ctx, s.Pokemon[god]), "체력 −3");
            Assert.AreEqual(-3, Priority(s, god), "우선도 −3");
            s.Seats[0].Shards[0] = true;
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(def.Health - 2, PokemonRules.MaxHp(ctx, s.Pokemon[god]), "「Temporarily」 — 조각을 얻는 만큼 회복(G2 Recalculate)");
            Assert.AreEqual(-2, Priority(s, god));
            s.Seats[0].Heart = true;
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(def.Health, PokemonRules.MaxHp(ctx, s.Pokemon[god]), "심장을 되살리면 없다");
            // 남이 데려가면(거래) 그 좌석에는 걸리지 않는다
            s.Seats[0].Heart = false;
            s.Pokemon[god].Owner = 1;
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(def.Health, PokemonRules.MaxHp(ctx, s.Pokemon[god]));
        }

        [Test]
        public void G2_무녀빨강_해금때고정_조각을얻어도그대로()
        {
            var s = Game("platinum_acolyte", TrainerRules.Silver, new RuleOptions { AcolyteRedTemporary = AcolyteRedTemporary.FixedAtUnlock });
            s.Seats[0].Shards[1] = true;
            Unlock(s, Strong);
            int god = s.Seats[0].Party[PokemonRules.PartyCount(s.Seats[0]) - 1];
            var def = ctx.Cards.Pokemon[s.Pokemon[god].Def];
            Assert.AreEqual(2, s.Seats[0].AcolyteToll);
            Assert.AreEqual(def.Health - 2, PokemonRules.MaxHp(ctx, s.Pokemon[god]));
            s.Seats[0].Shards[0] = s.Seats[0].Shards[2] = true;
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(def.Health - 2, PokemonRules.MaxHp(ctx, s.Pokemon[god]), "FixedAtUnlock: 해금 때 값 그대로");
            Assert.AreEqual(-2, Priority(s, god));
        }

        [Test]
        public void G2_무녀빨강_UB판은못쓰러뜨린UB3마리마다_격파수로센다()
        {
            // 「decrease … by 1 for each Shard you are currently missing or for every 3 Ultra Beasts undefeated」(울트라 버스트 판 = (9 − 쓰러뜨린 UB) ÷ 3, rulings A40 · G2)
            var s = UbScenes.Bare(ctx, 2);
            s.Seats[0].TrainerCard = CardId("diamond_acolyte");
            s.Seats[0].TrainerCardTier = TrainerRules.Silver;
            s.Decks[(int)DeckId.TrainerCard].Remove(CardId("diamond_acolyte"));
            Scene.Give(s, ctx, 0, Weak);
            Unlock(s, Strong);
            int god = s.Seats[0].Party[PokemonRules.PartyCount(s.Seats[0]) - 1];
            Assert.IsTrue(s.Pokemon[god].Chosen, "데려온 신");
            var def = ctx.Cards.Pokemon[s.Pokemon[god].Def];
            Assert.AreEqual(3, TrainerRules.Toll(s, 0), "UB 9마리 모두 남음 = 3");
            Assert.AreEqual(def.Health - 3, PokemonRules.MaxHp(ctx, s.Pokemon[god]));
            Assert.AreEqual(-3, Priority(s, god));
            s.Ub.Defeated = 2;
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(2, TrainerRules.Toll(s, 0), "7마리 남음 → 2(내림)");
            Assert.AreEqual(def.Health - 2, PokemonRules.MaxHp(ctx, s.Pokemon[god]));
            s.Ub.Defeated = 7;
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(0, TrainerRules.Toll(s, 0), "2마리 남음 → 0");
            Assert.AreEqual(def.Health, PokemonRules.MaxHp(ctx, s.Pokemon[god]));
            s.Seats[0].Shards[0] = false;
            Assert.AreEqual(0, TrainerRules.Toll(s, 0), "울트라 버스트 판은 조각을 세지 않는다");
        }

        [Test]
        public void A20_6_비취무녀파랑_UB판에서아르세우스1장과아르세우스의추종자면축복하나()
        {
            // 아르세우스의 추종자 = 아르세우스 3장 효과의 2장 몫(원작 커뮤니티 확인 — RuleOptions.ArceusFollowerBlessing, rulings A40) ·
            // 아르세우스 3장 = 울트라 버스트 판의 축복 +1(p.19, rulings A54 ⑦). 두 가지의 처리기·몫이 맞물린다(M6 「울트라 버스트 규칙 합침」).
            var s = UbScenes.Started(ctx, 2, 3);
            int seat = s.ActiveSeat;
            s.Seats[seat].Follower = SinnohGod.Arceus;
            int key = FateRules.CardKey(ctx, FateCard.Arceus);
            s.Seats[seat].FateHand.Clear();
            s.Seats[seat].FateHand.Add(key);
            int before = s.Ub.Blessings;
            var play = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Arceus);
            Assert.IsNotNull(play, "아르세우스 1장 + 추종자 토큰(2장 몫)으로 3장 효과");
            Assert.AreEqual(FateRules.Play3Count, play.Arg);
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, play, log);
            Assert.AreEqual(before + 1, s.Ub.Blessings, "축복 +1");
            Assert.AreEqual(0, s.Seats[seat].FateHand.Count, "아르세우스 1장만 버림 더미로");
            var played = log.OfType<FatePlayed>();
            Assert.AreEqual(1, played.Count);
            Assert.IsTrue(played[0].FollowerCounted);
            Assert.AreEqual(1, played[0].CardKeys.Length);
            Assert.AreEqual(1, log.OfType<BlessingGained>().Count);

            // 옵션을 끄면(문구대로) 1장으로는 못 낸다
            var off = UbScenes.Started(ctx, 2, 3, null, new RuleOptions { ArceusFollowerBlessing = false });
            off.Seats[off.ActiveSeat].Follower = SinnohGod.Arceus;
            off.Seats[off.ActiveSeat].FateHand.Clear();
            off.Seats[off.ActiveSeat].FateHand.Add(key);
            Assert.IsFalse(Flow.MapLegal(off, ctx).Exists(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Arceus));
        }

        int Priority(GameState s, int pokemon)
        {
            var mods = new List<Mod>();
            TrainerRules.PriorityMods(s, ctx, pokemon, mods);
            int d = 0;
            foreach (var m in mods) d += m.Delta;
            return d;
        }

        [Test]
        public void G3_무녀빨강_신이전설덱에없으면아무것도하지않는다()
        {
            var s = Game("diamond_acolyte", TrainerRules.Silver);
            s.Decks[(int)DeckId.Legendary].Remove(FractureRules.GodCard(ctx, SinnohGod.Dialga)); // 남이 이미 잡았다
            int before = PokemonRules.PartyCount(s.Seats[0]);
            var log = Unlock(s, Strong);
            Assert.AreEqual(TrainerRules.Gold, s.Seats[0].TrainerCardTier, "해금은 된다");
            Assert.AreEqual(before, PokemonRules.PartyCount(s.Seats[0]), "효과 없이 넘어간다");
            Assert.AreEqual(0, log.OfType<PokemonGained>().Count);
            Assert.AreEqual(0, log.OfType<AbilityUsed>().Count);
        }

        [Test]
        public void A20_6_무녀빨강_파티가가득이면배틀뒤에놓아줄포켓몬을묻는다()
        {
            var s = Game("diamond_acolyte", TrainerRules.Silver);
            while (PokemonRules.PartyCount(s.Seats[0]) < s.Seats[0].OpenSlots - 1) Scene.Give(s, ctx, 0, Weak);
            var log = new EventLog();
            WinAgainst(s, SlowStrong, log); // 이길 포켓몬으로 파티가 가득 찬다
            Assert.AreEqual(TrainerRules.Gold, s.Seats[0].TrainerCardTier, "배틀 중 강함을 쓰러뜨려 골드");
            var c = CardAsk(s);
            Assert.IsNotNull(c, "배틀 뒤 턴 끝 앞에 묻는다");
            Assert.AreEqual(ChoiceKind.Reward, c.Kind);
            Assert.AreEqual(ChoiceSub.PartyFull, c.Sub);
            int god = c.Subject;
            Assert.AreEqual(FractureRules.GodCard(ctx, SinnohGod.Dialga), s.Pokemon[god].Def);
            Assert.Less(PokemonRules.PartyIndexOf(s.Seats[0], god), 0, "그동안 파티 밖");
            Assert.AreEqual(ChoiceAnswer.Pick(god).ToString(), c.Default.ToString(), "기본 답 = 새 포켓몬(포획의 파티 가득과 같다)");
            int weak = FindDef(s, Weak);
            log.Clear();
            Battles.Answer(s, ctx, c, ChoiceAnswer.Pick(weak), log);
            Assert.AreEqual(1, log.OfType<Released>().Count);
            Assert.GreaterOrEqual(PokemonRules.PartyIndexOf(s.Seats[0], god), 0, "신이 파티에");
            Assert.AreEqual(1, log.OfType<PokemonGained>().Count);
            Assert.IsNull(CardAsk(s), "물음이 끝났다");
            Assert.AreEqual(1, log.OfType<TurnEnded>().Count, "그 뒤 배틀 뒤 턴 끝을 잇는다");
        }

        int FindDef(GameState s, string key)
        {
            foreach (int p in s.Seats[0].Party)
                if (p >= 0 && s.Pokemon[p].Def == Scene.Def(ctx, key)) return p;
            return -1;
        }

        // ── undivided_acolyte B ──

        [Test]
        public void A20_6_비취무녀파랑_아르세우스의추종자는세신모두의추종자()
        {
            var s = Game("undivided_acolyte", TrainerRules.Bronze);
            s.Seats[0].Follower = SinnohGod.Giratina;
            Unlock(s, Moderate);
            Assert.AreEqual(SinnohGod.Arceus, s.Seats[0].Follower, "「replace your Follower token with the Follower of Arceus token」");
            CollectionAssert.Contains(s.Seats[0].CardItems, Scene.Item(ctx, "item.follower_of_arceus"));
            var n = FractureRules.Counts(s);
            Assert.AreEqual(1, n[(int)SinnohGod.Dialga]);
            Assert.AreEqual(1, n[(int)SinnohGod.Palkia]);
            Assert.AreEqual(1, n[(int)SinnohGod.Giratina], "「counted as a Follower of all three Gods of Sinnoh at the same time」");
            foreach (var card in GodCards) Assert.AreEqual(1, FateRules.FollowerWorth(s, ctx, 0, card), "세 신 모두 보통 추종자처럼 1장 몫");
            s.Seats[1].Follower = SinnohGod.Palkia;
            Assert.IsTrue(TradeRules.SameGod(s, 0, 1), "원격 거래 — 「they can do both with any other Follower」");
            Assert.IsTrue(TradeRules.DifferentGod(s, 0, 1), "원격 도전도");
            s.Seats[1].Follower = SinnohGod.None;
            Assert.IsFalse(TradeRules.SameGod(s, 0, 1), "상대가 추종자가 아니면 없다");
            Assert.IsTrue(FractureRules.Follows(SinnohGod.Arceus, SinnohGod.Dialga));
            Assert.IsTrue(TrainerRules.FixedFollower(s, ctx, 0));
        }

        [Test]
        public void A20_6_비취무녀파랑_아르세우스3장효과는옵션이면2장몫()
        {
            foreach (bool on in new[] { true, false })
            {
                var s = Game("undivided_acolyte", TrainerRules.Silver, new RuleOptions { ArceusFollowerBlessing = on });
                s.Seats[0].Follower = SinnohGod.Arceus;
                Hand(s, ctx, FateCard.Arceus);
                Assert.AreEqual(on ? 2 : 0, FateRules.FollowerWorth(s, ctx, 0, FateCard.Arceus), $"ArceusFollowerBlessing {on}");
                Assert.AreEqual(on ? 1 : 0, FateRules.WildSplits(s, ctx, 0, FateCard.Arceus).Count, on ? "아르세우스 1장 + 토큰 = 3장 효과(권고)" : "문구대로 — 3장이 있어야");
            }
        }

        // ── youngster R ──

        [Test]
        public void A20_5_반바지꼬마_다섯장중하나와바꾸고새카드는골드_해금효과둘다()
        {
            var s = Game("youngster", TrainerRules.Silver);
            var deck = s.Decks[(int)DeckId.TrainerCard];
            int lady = CardId("lady");
            deck.Remove(lady);
            deck.Add(lady); // 맨 위
            int size = deck.Count;
            var log = new EventLog();
            WinAgainst(s, SlowStrong, log);
            int jp = s.Seats[0].Jp;
            Assert.AreEqual(TrainerRules.Gold, s.Seats[0].TrainerCardTier);
            Assert.AreEqual(size - TrainerRules.CareerDraw, deck.Count, "5장을 뽑았다");
            var c = CardAsk(s);
            Assert.IsNotNull(c, "배틀 뒤 턴 끝 앞에 고른다");
            Assert.AreEqual(ChoiceKind.Reaction, c.Kind);
            Assert.AreEqual(ChoiceSub.EffectPick, c.Sub);
            Assert.AreEqual(1, c.Count, "강제 고르기");
            Assert.AreEqual(TrainerRules.CareerDraw, c.Options.Count);
            CollectionAssert.Contains(c.Options, lady);
            log.Clear();
            Battles.Answer(s, ctx, c, ChoiceAnswer.Pick(lady), log);
            Assert.AreEqual(lady, s.Seats[0].TrainerCard, "「swap this card with one of the drawn cards」");
            Assert.AreEqual(TrainerRules.Gold, s.Seats[0].TrainerCardTier, "새 카드는 골드(A20-5)");
            var sw = log.OfType<TrainerCardSwapped>();
            Assert.AreEqual(1, sw.Count);
            Assert.AreEqual(CardId("youngster"), sw[0].FromCard);
            Assert.AreEqual(lady, sw[0].ToCard);
            Assert.AreEqual(jp + TrainerRules.HeritageJp, s.Seats[0].Jp, "「Triggers the \"when unlocked\" effects on the new trainer card」 — lady B JP +7");
            Assert.AreEqual(size, deck.Count, "반바지 꼬마 + 고르지 않은 4장이 덱으로");
            CollectionAssert.Contains(deck, CardId("youngster"));
            CollectionAssert.DoesNotContain(deck, lady);
            Assert.IsEmpty(s.Seats[0].TrainerCardOffer);
            Assert.AreEqual(1, log.OfType<TurnEnded>().Count, "배틀 뒤 턴 끝을 잇는다");
        }

        [Test]
        public void A20_5_반바지꼬마_후보는본인만_기본답은첫후보()
        {
            var s = Game("youngster", TrainerRules.Silver);
            var log = new EventLog();
            WinAgainst(s, SlowStrong, log);
            var c = CardAsk(s);
            Assert.IsTrue(c.Secret, "본인만 보는 후보");
            Assert.AreEqual(ChoiceAnswer.Pick(c.Options[0]).ToString(), c.Default.ToString(), "시간 초과 = 첫 후보(강제 고르기, A33)");
            var v = Engine.ViewFor(s, 1);
            var seen = v.Pending.Find(x => x.Id == c.Id);
            Assert.IsTrue(seen == null || seen.Options.Count == 0 || seen.Options.TrueForAll(o => o == Engine.Unknown), "남에게는 후보가 가려진다");
            Assert.IsTrue(v.Seats[0].TrainerCardOffer.TrueForAll(o => o == Engine.Unknown) || v.Seats[0].TrainerCardOffer.Count == 0);
            int first = c.Options[0];
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), log));
            Assert.AreEqual(first, s.Seats[0].TrainerCard);
        }
    }
}
