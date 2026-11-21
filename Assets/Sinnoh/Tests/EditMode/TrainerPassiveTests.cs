using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「상시 보정」(rulings A32, TrainerRules의 「상시 보정」 절): 트레이너 카드 능력 18 — 우선도 · 최대 체력 · 공격력 · 효과 피해 · 받는 피해(감소·문턱·상한) · 회복 +1 · 유물.
    /// 효과의 근거는 xlsx `trainer_cards` 영문 문구다(시험마다 인용). 우선도·공격력·체력은 셈 함수로 바로 보고, 피해·회복·포획은 실제 배틀 흐름으로 돈다
    /// (굴림은 그 면이 나오는 난수 시드를 찾아 정한다 — 시험은 결정적이다). 공격하는 포켓몬은 한카리아스 445(드래곤/땅, 강함, 체력 9, 우선도 11) — 좌석 0이 먼저다.
    /// 두번치기 double_hit = 노말, 공격력 2, 일반 타격마다 +1.
    /// </summary>
    public class TrainerPassiveTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const AttackDieFace S = AttackDieFace.StatusSymbol;
        const AttackDieFace R = AttackDieFace.RegularHit;
        const AttackDieFace C = AttackDieFace.CriticalHit;
        const string Garchomp = "445:0", Weak = "399:0", Starter = "387:0", Moderate = "388:0";

        // ── 장면 ──

        /// <summary>좌석에 그 카드를 그 등급으로 쥐여 준다(최대 체력 보정도 다시 센다).</summary>
        SourceRef Card(GameState s, int seat, string key, int tier = TrainerRules.Gold)
        {
            int card = ctx.Cards.TrainerCardIds.IndexOf(key);
            s.Seats[seat].TrainerCard = card;
            s.Seats[seat].TrainerCardTier = tier;
            TrainerRules.Refresh(s, ctx);
            return new SourceRef(SourceKind.TrainerCard, card);
        }

        int Mv(string key)
        {
            int m = Scene.Move(ctx, key);
            Assert.GreaterOrEqual(m, 0, key);
            return m;
        }

        int Order(GameState s, int pokemon, out List<Mod> mods)
        {
            mods = new List<Mod>();
            return BattleRules.OrderValue(s, ctx, pokemon, mods, out _);
        }

        int Order(GameState s, int pokemon) => Order(s, pokemon, out _);

        int Power(GameState s, string move, int attacker, int defender, out List<Mod> mods)
        {
            mods = new List<Mod>();
            return BattleRules.RollPower(s, ctx, Mv(move), attacker, defender, mods);
        }

        int Power(GameState s, string move, int attacker, int defender) => Power(s, move, attacker, defender, out _);

        int MaxHp(GameState s, int pokemon) => PokemonRules.MaxHp(ctx, s.Pokemon[pokemon]);

        sealed class Arena
        {
            public GameState S;
            public EventLog Log = new EventLog();
            public int A, D;
            public SourceRef Card0 = SourceRef.None, Card1 = SourceRef.None;
        }

        /// <summary>트레이너전: 좌석 0의 a 대 좌석 1의 d(각 한 마리), 좌석마다 카드(없으면 null). 좌석 0의 기술 고르기가 열려 있다.</summary>
        Arena Duel(string a, string d, string card0 = null, int tier0 = TrainerRules.Gold, string card1 = null, int tier1 = TrainerRules.Gold)
        {
            var t = new Arena { S = Battles.Game(ctx, 2) };
            t.A = Scene.Give(t.S, ctx, 0, a);
            t.D = Scene.Give(t.S, ctx, 1, d);
            if (card0 != null) t.Card0 = Card(t.S, 0, card0, tier0);
            if (card1 != null) t.Card1 = Card(t.S, 1, card1, tier1);
            Battles.Start(t.S, ctx, t.Log, BattleKind.Trainer, 0, 1, -1);
            if (t.S.Pending.Count == 1 && t.S.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(t.S, ctx, t.S.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "좌석 0이 먼저");
            return t;
        }

        /// <summary>굴림이 정확히 그 면들로 나오는 난수 시드(굴림은 그 행동의 첫 난수다).</summary>
        static ulong SeedFor(AttackDieFace[] faces)
        {
            for (ulong seed = 0; seed < 2000000; seed++)
            {
                var roll = AttackDice.Roll(new Pcg32(seed), faces.Length);
                bool same = true;
                for (int i = 0; i < roll.Length && same; i++) same = roll[i] == faces[i];
                if (same) return seed;
            }
            Assert.Fail("그 굴림이 나오는 시드가 없다");
            return 0;
        }

        /// <summary>좌석 0의 포켓몬이 그 기술(배운 기술 칸 0에 넣는다)을 그 굴림으로 쓴다. 반환 = 그 답의 이벤트.</summary>
        List<GameEvent> Attack(Arena t, string move, params AttackDieFace[] faces)
        {
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            int m = Mv(move);
            t.S.Pokemon[t.A].Moves[0] = m;
            t.S.Rng = new Pcg32(SeedFor(faces));
            int from = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ch, Battles.Move(m), t.Log);
            var events = t.Log.Events.GetRange(from, t.Log.Events.Count - from);
            CollectionAssert.AreEqual(faces, One<DiceRolled>(events).Faces, $"{move}: 굴림");
            return events;
        }

        static T One<T>(List<GameEvent> events) where T : GameEvent
        {
            var l = events.FindAll(e => e is T);
            Assert.AreEqual(1, l.Count, typeof(T).Name);
            return (T)l[0];
        }

        static List<DamageStep> Steps(DamageBreakdown d, DamageStepKind kind, SourceRef source) =>
            new List<DamageStep>(d.Steps).FindAll(x => x.Kind == kind && x.Source.Equals(source));

        // ── 우선도 · 최대 체력 ──

        [Test]
        public void A20_6_ace_trainer_빨강_강함포켓몬우선도더하기1_우두머리는아니다()
        {
            // 「Your Strong Pokémon gain +1 initiative.」 · 원작 커뮤니티 확인
            var s = Battles.Game(ctx, 2);
            int strong = Scene.Give(s, ctx, 0, Garchomp), moderate = Scene.Give(s, ctx, 0, Moderate), other = Scene.Give(s, ctx, 1, Garchomp);
            var card = Card(s, 0, "ace_trainer");
            Assert.AreEqual(12, Order(s, strong, out var mods));
            Assert.AreEqual(1, mods.Count);
            Assert.AreEqual(card, mods[0].Source, "출처 = 트레이너 카드");
            Assert.AreEqual(1, mods[0].Delta);
            Assert.AreEqual(6, Order(s, moderate), "보통은 그대로");
            Assert.AreEqual(11, Order(s, other), "남의 포켓몬");
            s.Pokemon[strong].Alpha = true;
            Assert.AreEqual(11, Order(s, strong), "우두머리는 전설로 친다");
        }

        [Test]
        public void A20_6_bug_catcher_빨강_벌레포켓몬우선도2와벌레기술공격력1()
        {
            // 「Your Bug-type Pokémon gain +2 initiative and +1 attack strength to their Bug-type moves.」
            var s = Battles.Game(ctx, 2);
            int bug = Scene.Give(s, ctx, 0, "412:0"), normal = Scene.Give(s, ctx, 0, Weak), foe = Scene.Give(s, ctx, 1, Weak);
            var card = Card(s, 0, "bug_catcher");
            Scene.Battle(s, BattleKind.Trainer, 0, 1);
            Assert.AreEqual(3 + 2, Order(s, bug));
            Assert.AreEqual(4, Order(s, normal), "벌레가 아니면 그대로");
            Assert.AreEqual(3 + 1, Power(s, "bug_bite", bug, foe, out var mods));
            Assert.IsTrue(mods.Exists(m => m.Source.Equals(card) && m.Delta == 1));
            Assert.AreEqual(2, Power(s, "tackle", bug, foe), "벌레 기술이 아니면 그대로");
            Assert.AreEqual(3, Power(s, "bug_bite", normal, foe), "벌레 포켓몬이 아니면 그대로");
        }

        [Test]
        public void A20_6_gardener_빨강_풀바위포켓몬우선도3과체력빼기2_쓰러지지는않는다()
        {
            // 「Your Grass-type Pokémon and Rock-type Pokémon gain +3 initiative but -2 health.」
            var s = Battles.Game(ctx, 2);
            s.Seats[0].OpenSlots = 6;
            int grass = Scene.Give(s, ctx, 0, Starter), rock = Scene.Give(s, ctx, 0, "410:0"), normal = Scene.Give(s, ctx, 0, Weak), hurt = Scene.Give(s, ctx, 0, Moderate);
            Battles.SetHp(s, ctx, hurt, 1);
            Card(s, 0, "gardener");
            Assert.AreEqual(6 - 2, MaxHp(s, grass));
            Assert.AreEqual(4 + 3, Order(s, grass));
            Assert.AreEqual(7 - 2, MaxHp(s, rock));
            Assert.AreEqual(4 + 3, Order(s, rock));
            Assert.AreEqual(5, MaxHp(s, normal));
            Assert.AreEqual(4, Order(s, normal));
            Assert.AreEqual(8 - 2, MaxHp(s, hurt));
            Assert.AreEqual(1, Battles.Hp(s, ctx, hurt), "보정이 줄어도 체력 1로 남는다 [가정 A32]");
        }

        [Test]
        public void A20_6_hiker_빨강_바위땅포켓몬체력4와우선도빼기1_골드가되는순간부터()
        {
            // 「Your Rock-type Pokémon and Ground-type Pokémon gain +4 health but -1 initiative.」
            var s = Battles.Game(ctx, 2);
            int ground = Scene.Give(s, ctx, 0, Garchomp), normal = Scene.Give(s, ctx, 0, Weak);
            Card(s, 0, "hiker", TrainerRules.Silver);
            Assert.AreEqual(9, MaxHp(s, ground), "실버에서는 빨강이 잠겨 있다");
            Assert.AreEqual(11, Order(s, ground));
            // 강함 야생을 쓰러뜨려 골드가 되는 순간(BattleFlow.ReportFaint가 부르는 고리)
            int foe = Scene.Spawn(s, ctx, Garchomp, SeatId.Wild);
            var b = Scene.Battle(s, BattleKind.Wild, 0, SeatId.Wild, foe);
            TrainerRules.Defeated(s, ctx, Scene.Fx(s, new EventLog(), 0, ActionKind.BattleAction), b, 1, foe);
            Assert.AreEqual(TrainerRules.Gold, s.Seats[0].TrainerCardTier);
            Assert.AreEqual(9 + 4, MaxHp(s, ground));
            Assert.AreEqual(13, Battles.Hp(s, ctx, ground), "받은 피해는 그대로 — 남은 체력도 4 는다");
            Assert.AreEqual(11 - 1, Order(s, ground));
            Assert.AreEqual(5, MaxHp(s, normal));
            Assert.AreEqual(9, MaxHp(s, foe), "야생은 그대로");
            // 남에게 넘어가면 보정이 사라진다 — 체력 3(13 가운데)이던 포켓몬은 1로 남는다
            s.Battle = null;
            s.Pokemon[ground].Damage = 10;
            s.Pokemon[ground].Owner = 1;
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(9, MaxHp(s, ground));
            Assert.AreEqual(1, Battles.Hp(s, ctx, ground));
        }

        [Test]
        public void A20_6_hiker_명령이끝나면체력보정을다시센다()
        {
            // Engine.Submit 끝의 TrainerRules.Refresh: 장면이 손으로 바꾼 카드도 다음 명령 뒤에는 맞는다
            var t = Duel(Garchomp, "389:1");
            int card = ctx.Cards.TrainerCardIds.IndexOf("hiker");
            t.S.Seats[1].TrainerCard = card;
            t.S.Seats[1].TrainerCardTier = TrainerRules.Gold;
            Assert.AreEqual(10, MaxHp(t.S, t.D), "아직 다시 세지 않았다");
            Attack(t, "double_hit", S, S);
            Assert.AreEqual(10 + 4, MaxHp(t.S, t.D), "토대부기(풀/땅)");
        }

        [Test]
        public void A20_6_youngster_파랑_약한포켓몬체력1과우선도1_스타터도()
        {
            // 「Your Weak Pokémon gain +1 health and +1 initiative.」 · 스타터 = 약함 [가정 A32]
            var s = Battles.Game(ctx, 2);
            int weak = Scene.Give(s, ctx, 0, Weak), starter = Scene.Give(s, ctx, 0, Starter), moderate = Scene.Give(s, ctx, 0, Moderate);
            var card = Card(s, 0, "youngster", TrainerRules.Silver);
            Assert.AreEqual(5 + 1, MaxHp(s, weak));
            Assert.AreEqual(4 + 1, Order(s, weak, out var mods));
            Assert.AreEqual(card, mods[0].Source);
            Assert.AreEqual(6 + 1, MaxHp(s, starter));
            Assert.AreEqual(4 + 1, Order(s, starter));
            Assert.AreEqual(8, MaxHp(s, moderate));
            Assert.AreEqual(6, Order(s, moderate));
        }

        [Test]
        public void M6_상시보정은잠긴등급에서는걸리지않는다()
        {
            // A20-2: 파랑은 실버부터, 빨강은 골드부터 — 브론즈·실버에서 빨강(hiker), 브론즈에서 파랑(youngster)은 없다
            foreach (int tier in new[] { TrainerRules.Bronze, TrainerRules.Silver })
            {
                var s = Battles.Game(ctx, 2);
                int ground = Scene.Give(s, ctx, 0, Garchomp);
                Card(s, 0, "hiker", tier);
                Assert.AreEqual(9, MaxHp(s, ground), $"등급 {tier}");
                Assert.AreEqual(11, Order(s, ground, out var mods), $"등급 {tier}");
                Assert.AreEqual(0, mods.Count);
            }
            var g = Battles.Game(ctx, 2);
            int weak = Scene.Give(g, ctx, 0, Weak);
            Card(g, 0, "youngster", TrainerRules.Bronze);
            Assert.AreEqual(5, MaxHp(g, weak));
            Assert.AreEqual(4, Order(g, weak));
            // 받는 피해 보정도 같다: 실버의 롤러스케이트(빨강)는 상한이 없다
            var t = Duel(Garchomp, "242:0", null, 0, "roller_skater", TrainerRules.Silver);
            var d = One<DamageBreakdown>(Attack(t, "double_edge", C, C, R, R, R));
            Assert.AreEqual(11, d.Final, "눈 7 + 크리티컬마다 +2");
            // 트레이너 카드 확장을 끄면 좌석에 카드가 없다(A20-8)
            var off = Battles.Game(ctx, 2, 7, new RuleOptions { TrainerCards = false });
            Assert.AreEqual(-1, TrainerRules.CardOf(off, 0));
            Assert.IsFalse(TrainerRules.Has(off, ctx, 0, "hiker", AbilityColor.Red));
        }

        // ── 공격력 ──

        [Test]
        public void A20_6_artist_빨강_파티기술이모든타입이면공격력2_타입없음은아무타입()
        {
            // 「If your party has a move of every type, all your moves gain +2 attack strength. Typeless moves and abilities act as wildcards …」
            var s = Battles.Game(ctx, 2);
            s.Seats[0].OpenSlots = 6;
            var party = new List<int>();
            foreach (string key in new[] { Weak, Moderate, Garchomp, Starter, "393:0", "390:0" }) party.Add(Scene.Give(s, ctx, 0, key)); // 시그니처: 노말·악·드래곤·풀·물·불꽃
            int foe = Scene.Give(s, ctx, 1, Weak);
            var card = Card(s, 0, "artist");
            Scene.Battle(s, BattleKind.Trainer, 0, 1);
            var have = new HashSet<PokemonType>();
            foreach (int p in party) have.Add(ctx.Cards.Moves[ctx.Cards.Pokemon[s.Pokemon[p].Def].Signature].Type);
            Assert.AreEqual(6, have.Count);
            Assert.IsFalse(TrainerRules.Masterpiece(s, ctx, 0));
            Assert.AreEqual(2, Power(s, "tackle", party[0], foe), "아직 여섯 타입");
            // 남은 열두 타입을 배운 기술 12칸에 하나씩
            int slot = 0;
            for (int m = 0; m < ctx.Cards.Moves.Count && slot < 12; m++)
            {
                var type = ctx.Cards.Moves[m].Type;
                if (type == PokemonType.Typeless || !have.Add(type)) continue;
                s.Pokemon[party[slot / 2]].Moves[slot % 2] = m;
                slot++;
            }
            Assert.AreEqual(18, have.Count);
            Assert.IsTrue(TrainerRules.Masterpiece(s, ctx, 0));
            Assert.AreEqual(2 + 2, Power(s, "tackle", party[0], foe, out var mods));
            Assert.IsTrue(mods.Exists(x => x.Source.Equals(card) && x.Delta == 2));
            Assert.AreEqual(2, Power(s, "tackle", foe, party[0]), "상대의 기술은 그대로");
            // 한 칸을 비우면 17타입 — 타입없음 기술(잠재파워)을 넣으면 그 칸이 아무 타입이 된다
            s.Pokemon[party[5]].Moves[1] = -1;
            Assert.IsFalse(TrainerRules.Masterpiece(s, ctx, 0));
            Assert.AreEqual(2, Power(s, "tackle", party[0], foe));
            s.Pokemon[party[5]].Moves[1] = Mv("hidden_power");
            Assert.IsTrue(TrainerRules.Masterpiece(s, ctx, 0), "「Typeless moves … act as wildcards」");
            // 남아 있는 전설 특수기술(알파 스트라이크 · 비스트 부스트)도 하나씩(원작 커뮤니티 확인)
            s.Pokemon[party[5]].Moves[0] = -1;
            s.Pokemon[party[5]].Moves[1] = -1;
            Assert.IsFalse(TrainerRules.Masterpiece(s, ctx, 0));
            s.Pokemon[party[5]].HasLegendaryMove = true;
            Assert.IsTrue(TrainerRules.Masterpiece(s, ctx, 0));
        }

        [Test]
        public void A20_6_sky_trainer_빨강_비행포켓몬공격력1_상대가비행이면아니다()
        {
            // 「Your Flying-type Pokémon gain +1 attack strength to all their moves so long as they are not against another Flying-type Pokémon.」
            var s = Battles.Game(ctx, 2);
            int bird = Scene.Give(s, ctx, 0, "398:1"), ground = Scene.Give(s, ctx, 0, Garchomp);
            int foe = Scene.Give(s, ctx, 1, Weak), foeBird = Scene.Give(s, ctx, 1, "396:0");
            var card = Card(s, 0, "sky_trainer");
            Scene.Battle(s, BattleKind.Trainer, 0, 1);
            Assert.AreEqual(5 + 1, Power(s, "close_combat", bird, foe, out var mods));
            Assert.IsTrue(mods.Exists(m => m.Source.Equals(card) && m.Delta == 1));
            Assert.AreEqual(2 + 1, Power(s, "tackle", bird, foe), "「all their moves」");
            Assert.AreEqual(5, Power(s, "close_combat", bird, foeBird), "받는 쪽이 비행");
            Assert.AreEqual(5, Power(s, "close_combat", ground, foe), "비행 포켓몬이 아니다");
        }

        [TestCase("team_aqua", "bubble_beam", "393:0", "ember", "390:0")]
        [TestCase("team_magma", "ember", "390:0", "bubble_beam", "393:0")]
        public void A20_6_team_aqua_team_magma_빨강_내배틀에서는양쪽모두그타입기술공격력1과그타입포켓몬1덜(string key, string move, string mon, string otherMove, string otherMon)
        {
            // 「During your battles, all Water-type(Fire-type) moves gain +1 attack strength and all Water-type(Fire-type) Pokémon take 1 less damage when attacked.」 · 원작 커뮤니티 확인
            var s = Battles.Game(ctx, 3);
            int mine = Scene.Give(s, ctx, 0, Garchomp), foe = Scene.Give(s, ctx, 1, mon), far = Scene.Give(s, ctx, 2, mon);
            var card = Card(s, 0, key);
            Scene.Battle(s, BattleKind.Trainer, 0, 1);
            int basePower = ctx.Cards.Moves[Mv(move)].AttackStrength.Value;
            Assert.AreEqual(basePower + 1, Power(s, move, mine, foe, out var mods));
            Assert.IsTrue(mods.Exists(m => m.Source.Equals(card) && m.Delta == 1));
            Assert.AreEqual(basePower + 1, Power(s, move, foe, mine), "상대의 기술도");
            Assert.AreEqual(ctx.Cards.Moves[Mv(otherMove)].AttackStrength.Value, Power(s, otherMove, mine, foe), "다른 타입은 그대로");
            var taken = TrainerRules.TakenMods(s, ctx, foe, true);
            Assert.AreEqual(1, taken.Reduction, "상대의 그 타입 포켓몬도 1 덜 받는다");
            Assert.AreEqual(card, taken.Reductions[0].Source);
            Assert.AreEqual(0, TrainerRules.TakenMods(s, ctx, mine, true).Reduction, "그 타입이 아니다");
            Assert.AreEqual(0, TrainerRules.TakenMods(s, ctx, foe, false).Reduction, "「when attacked」 — 곁 피해에는 없다");
            // 그 좌석이 끼지 않은 배틀(좌석 1 대 좌석 2)에는 없다
            Scene.Battle(s, BattleKind.Trainer, 1, 2);
            Assert.AreEqual(basePower, Power(s, move, foe, far));
            Assert.AreEqual(0, TrainerRules.TakenMods(s, ctx, far, true).Reduction);
            Assert.GreaterOrEqual(Scene.Def(ctx, otherMon), 0);
        }

        [Test]
        public void A20_6_team_aqua_빨강_물포켓몬이공격받으면피해내역에감소1()
        {
            var t = Duel(Garchomp, "393:0", "team_aqua");
            var d = One<DamageBreakdown>(Attack(t, "double_hit", R, R));
            Assert.AreEqual(4 - 1, d.Final, "눈 2 + 일반 타격마다 +1 − 1");
            var steps = Steps(d, DamageStepKind.Reduction, t.Card0);
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(-1, steps[0].Value);
        }

        [Test]
        public void A20_6_ruin_maniac_빨강_유물보정이1더붙고_트레이너전보상으로빼앗기지않는다()
        {
            // 「Your Artifacts gain +1 attack strength to their bonuses and cannot be taken by other trainers.」
            var s = Battles.Game(ctx, 2);
            int mine = Scene.Give(s, ctx, 0, Garchomp), foe = Scene.Give(s, ctx, 1, Weak);
            int stone = Scene.Item(ctx, "item.magma_stone"); // 불꽃·땅 +1
            s.Pokemon[mine].HeldItem = stone;
            Scene.Battle(s, BattleKind.Trainer, 0, 1);
            Assert.AreEqual(2 + 1, Power(s, "ember", mine, foe), "카드 없이 유물 +1");
            var card = Card(s, 0, "ruin_maniac");
            Assert.AreEqual(2 + 2, Power(s, "ember", mine, foe, out var mods));
            Assert.IsTrue(mods.Exists(m => m.Source.Equals(new SourceRef(SourceKind.Relic, stone)) && m.Delta == 1));
            Assert.IsTrue(mods.Exists(m => m.Source.Equals(card) && m.Delta == 1));
            Assert.AreEqual(2, Power(s, "tackle", mine, foe), "유물의 타입이 아니면 카드 보정도 없다");
            // 트레이너전에서 졌다: 이긴 좌석 1의 보상 답에 유물이 없다(운명 카드는 그대로)
            s.Battle = null;
            s.Phase = Phase.Turn;
            s.Seats[0].Bag.Add(Scene.Item(ctx, "item.odd_keystone"));
            s.Seats[0].FateHand.Add(0);
            var reward = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.TrainerReward, Seat = 1, Subject = 0, Window = Window.BattleEnd };
            var legal = Scene.Legal(s, ctx, reward);
            Assert.AreEqual(0, legal.FindAll(a => a.Kind == AnswerKind.Pick).Count, "「cannot be taken by other trainers」");
            Assert.AreEqual(1, legal.FindAll(a => a.Kind == AnswerKind.Accept).Count);
            s.Seats[0].TrainerCardTier = TrainerRules.Silver;
            Assert.AreEqual(2, Scene.Legal(s, ctx, reward).FindAll(a => a.Kind == AnswerKind.Pick).Count, "실버면 가방·지닌 유물 둘 다 가져갈 수 있다");
        }

        [Test]
        public void A20_6_ruin_maniac_빨강_보정이없는유물에는아무것도붙지않는다()
        {
            // 원작 커뮤니티 확인
            var s = Battles.Game(ctx, 2);
            int mine = Scene.Give(s, ctx, 0, Garchomp), foe = Scene.Give(s, ctx, 1, Weak);
            s.Pokemon[mine].HeldItem = Scene.Item(ctx, "item.adamant_artifact"); // 천상의 유물 — 공격력 보정이 없다
            Scene.Battle(s, BattleKind.Trainer, 0, 1);
            int ember = Power(s, "ember", mine, foe), tackle = Power(s, "tackle", mine, foe);
            var card = Card(s, 0, "ruin_maniac");
            Assert.AreEqual(ember, Power(s, "ember", mine, foe, out var mods));
            Assert.IsFalse(mods.Exists(m => m.Source.Equals(card)));
            Assert.AreEqual(tackle, Power(s, "tackle", mine, foe, out mods));
            Assert.IsFalse(mods.Exists(m => m.Source.Equals(card)));
        }

        // ── 효과 피해 · 상성 ──

        [Test]
        public void A20_6_beauty_파랑_크리티컬은2더_심볼은1덜()
        {
            // 「During all your rolls, critical hits deal +2 damage but status symbols deal -1 damage.」
            var t = Duel(Garchomp, "799:0", "beauty", TrainerRules.Silver);
            var d = One<DamageBreakdown>(Attack(t, "double_hit", C, S));
            var steps = Steps(d, DamageStepKind.Effect, t.Card0);
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(2 - 1, steps[0].Value);
            Assert.AreEqual(2 + 1, d.Final, "눈 2 + (2 − 1)");
            // 남의 굴림에는 없다(좌석 1이 좌석 0을 친다)
            var u = Duel(Garchomp, "799:0", null, 0, "beauty", TrainerRules.Silver);
            Assert.AreEqual(2, One<DamageBreakdown>(Attack(u, "double_hit", C, S)).Final);
        }

        [Test]
        public void A20_6_beauty_빨강_노말악기술이면심볼이1더()
        {
            // 「When you attack with a Normal-type or Dark-type move, status symbols deal +1 damage.」 — 파랑과 더한다 [가정 A32]
            var t = Duel(Garchomp, "799:0", "beauty");
            var d = One<DamageBreakdown>(Attack(t, "double_hit", C, S));
            var steps = Steps(d, DamageStepKind.Effect, t.Card0);
            Assert.AreEqual(2, steps.Count, "파랑 +1 · 빨강 +1");
            Assert.AreEqual(2 + 1 + 1, d.Final, "눈 2 + (2 − 1) + 심볼 1");
            // 노말·악이 아닌 기술(물)에는 빨강이 없다
            var u = Duel(Garchomp, "799:0", "beauty");
            var w = One<DamageBreakdown>(Attack(u, "bubble_beam", C, S, R));
            var only = Steps(w, DamageStepKind.Effect, u.Card0);
            Assert.AreEqual(1, only.Count);
            Assert.AreEqual(2 - 1, only[0].Value);
        }

        GameState Throw(AttackDieFace[] faces, string card, EventLog log)
        {
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, Garchomp);
            if (card != null) Card(s, 0, card, TrainerRules.Silver);
            s.Seats[0].Balls[(int)Ball.Regular] = 1;
            int wild = Scene.Spawn(s, ctx, Moderate, SeatId.Wild);
            Battles.Wild(s, ctx, log, 0, wild);
            s.Rng = new Pcg32(SeedFor(faces));
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), log);
            return s;
        }

        [Test]
        public void A20_6_beauty_파랑_포획굴림에도걸린다()
        {
            // 「During all your rolls」 — 원작 커뮤니티 확인. 보통 포켓몬 = 포획률 4, 몬스터볼 = 주사위 3
            var log = new EventLog();
            Throw(new[] { C, R, S }, "beauty", log);
            var a = log.OfType<CaptureAttempt>()[0];
            Assert.AreEqual(3 + 2 - 1, a.Sum);
            Assert.IsTrue(a.Success);
            var plain = new EventLog();
            Throw(new[] { C, R, S }, null, plain);
            Assert.AreEqual(3, plain.OfType<CaptureAttempt>()[0].Sum);
            Assert.IsFalse(plain.OfType<CaptureAttempt>()[0].Success);
            var low = new EventLog();
            Throw(new[] { R, R, S }, "beauty", low);
            Assert.AreEqual(2 - 1, low.OfType<CaptureAttempt>()[0].Sum, "심볼은 1 덜");
        }

        [Test]
        public void A20_6_scientist_빨강_상성이양수일때만1더()
        {
            // 「When attacking, your damage from positive type effectiveness is increased by 1.」 · 원작 커뮤니티 확인
            var t = Duel(Garchomp, "390:0", "scientist");
            var d = One<DamageBreakdown>(Attack(t, "bubble_beam", R, R, R));
            var steps = Steps(d, DamageStepKind.Effectiveness, t.Card0);
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(1, steps[0].Value);
            Assert.AreEqual(3 + 2 + 1, d.Final, "눈 3 + 물 → 불꽃 +2 + 1");
            var u = Duel(Garchomp, "390:0", "scientist");
            var n = One<DamageBreakdown>(Attack(u, "double_hit", R, R));
            Assert.AreEqual(0, Steps(n, DamageStepKind.Effectiveness, u.Card0).Count, "상성 0이면 없다");
            Assert.AreEqual(4, n.Final);
        }

        // ── 받는 피해 ──

        [Test]
        public void A20_6_bird_keeper_빨강_비행포켓몬은2이하피해를받지않는다()
        {
            // 「Your Flying-type Pokémon take no damage from attacks that would only deal 2 or less damage to them.」
            var t = Duel(Garchomp, "396:0", null, 0, "bird_keeper");
            var d = One<DamageBreakdown>(Attack(t, "double_hit", R, S));
            Assert.AreEqual(0, d.Final, "눈 1 + 1 = 2 → 0");
            var steps = Steps(d, DamageStepKind.Threshold, t.Card1);
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(2, steps[0].Value);
            Assert.AreEqual(4, Battles.Hp(t.S, ctx, t.D));
            var u = Duel(Garchomp, "396:0", null, 0, "bird_keeper");
            var over = One<DamageBreakdown>(Attack(u, "double_hit", R, R));
            Assert.AreEqual(4, over.Final, "3 이상은 그대로");
            Assert.AreEqual(0, Steps(over, DamageStepKind.Threshold, u.Card1).Count);
            var v = Duel(Garchomp, Weak, null, 0, "bird_keeper");
            Assert.AreEqual(2, One<DamageBreakdown>(Attack(v, "double_hit", R, S)).Final, "비행 포켓몬이 아니다");
        }

        [Test]
        public void A20_6_delinquent_빨강_악독포켓몬은공격에서2덜받는다_곁피해도()
        {
            // 「Your Dark-type Pokémon and Poison-type Pokémon take 2 less damage from attacks.」
            var t = Duel(Garchomp, "799:0", null, 0, "delinquent");
            var d = One<DamageBreakdown>(Attack(t, "double_hit", R, R));
            Assert.AreEqual(4 - 2, d.Final);
            var steps = Steps(d, DamageStepKind.Reduction, t.Card1);
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(-2, steps[0].Value);
            // 기술 효과가 벤치에 주는 고정 피해(earthquake 「Deal 2 damage to a Pokémon in the target party that is not the target」)에도 — 원작 커뮤니티 확인
            int poison = Scene.Give(t.S, ctx, 1, "315:0"), normal = Scene.Give(t.S, ctx, 1, Weak);
            Assert.AreEqual(0, TrainerRules.SideDamage(t.S, ctx, poison, 2));
            Assert.AreEqual(1, TrainerRules.SideDamage(t.S, ctx, poison, 3));
            Assert.AreEqual(2, TrainerRules.SideDamage(t.S, ctx, normal, 2), "악·독이 아니다");
        }

        [Test]
        public void A20_6_poke_fan_파랑_약한포켓몬은한번에3까지만받는다()
        {
            // 「Your Weak Pokémon can at most take 3 damage from a single attack.」
            var t = Duel(Garchomp, Weak, null, 0, "poke_fan", TrainerRules.Silver);
            var d = One<DamageBreakdown>(Attack(t, "double_hit", C, C));
            Assert.AreEqual(3, d.Final, "눈 4 → 3");
            var steps = Steps(d, DamageStepKind.Cap, t.Card1);
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(3, steps[0].Value);
            var u = Duel(Garchomp, Moderate, null, 0, "poke_fan", TrainerRules.Silver);
            Assert.AreEqual(4, One<DamageBreakdown>(Attack(u, "double_hit", C, C)).Final, "약한 포켓몬이 아니다");
        }

        [Test]
        public void A20_6_roller_skater_빨강_내포켓몬은한번에6까지만받는다()
        {
            // 「Your Pokémon can at most take 6 damage from a single attack.」
            var t = Duel(Garchomp, "242:0", null, 0, "roller_skater");
            var d = One<DamageBreakdown>(Attack(t, "double_edge", C, C, R, R, R));
            Assert.AreEqual(6, d.Final, "눈 7 + 크리티컬마다 +2 = 11 → 6");
            var steps = Steps(d, DamageStepKind.Cap, t.Card1);
            Assert.AreEqual(1, steps.Count);
            Assert.AreEqual(6, steps[0].Value);
            Assert.AreEqual(14 - 6, Battles.Hp(t.S, ctx, t.D));
            Assert.AreEqual(6, TrainerRules.SideDamage(t.S, ctx, t.D, 9), "곁 피해도 「a single attack」");
        }

        // ── 회복 ──

        [Test]
        public void A20_6_fairy_tale_girl_파랑_회복할때마다1더()
        {
            // 「Your Pokémon heal 1 extra damage whenever they are healed by any effect.」 — soft_boiled 「Heal 4 damage.」
            var t = Duel(Garchomp, "799:0", "fairy_tale_girl", TrainerRules.Silver);
            t.S.Pokemon[t.A].Damage = 6;
            Attack(t, "soft_boiled", S);
            Assert.AreEqual(6 - (4 + 1), t.S.Pokemon[t.A].Damage);
            var u = Duel(Garchomp, "799:0");
            u.S.Pokemon[u.A].Damage = 6;
            Attack(u, "soft_boiled", S);
            Assert.AreEqual(6 - 4, u.S.Pokemon[u.A].Damage, "카드가 없으면 4");
        }
    }
}
