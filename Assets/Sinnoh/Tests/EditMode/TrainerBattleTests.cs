using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「배틀 굴림·공격력·타입」(rulings A34 — 능력 12): chef R · essentia R · ginkgo_merchant R · hex_maniac B·R · ninja_boy B·R · poke_maniac B · psychic R ·
    /// swimmer R · triathlete R · veteran R. 물음은 Reaction/EffectOption(쓸까)·EffectPick(고르기)로 열리고 출처가 그 카드다. 「may」의 기본 답은 「안 함」, 강제 고르기는 첫 합법 답(A33).
    /// </summary>
    public class TrainerBattleTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const AttackDieFace S = AttackDieFace.StatusSymbol;
        const AttackDieFace R = AttackDieFace.RegularHit;
        const AttackDieFace C = AttackDieFace.CriticalHit;
        const string Garchomp = "445:0", Bidoof = "399:0", Turtwig = "387:0", Vulpix = "37:0";

        // ── 장면 ──

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

        sealed class Arena
        {
            public GameState S;
            public EventLog Log = new EventLog();
            public int A, D;
            public SourceRef Card0 = SourceRef.None;
            public int From;
        }

        /// <summary>트레이너전: 좌석 0의 a 대 좌석 1의 d, 좌석 0에 카드. token = 우선권 토큰 물음에 좌석 0이 쓴다고 답한다(동률일 때). 좌석 0의 기술 고르기가 열려 있다.</summary>
        Arena Duel(string a, string d, string card, int tier = TrainerRules.Gold, bool settle = true)
        {
            var t = new Arena { S = Battles.Game(ctx, 2) };
            t.A = Scene.Give(t.S, ctx, 0, a);
            t.D = Scene.Give(t.S, ctx, 1, d);
            if (card != null) t.Card0 = Card(t.S, 0, card, tier);
            Battles.Start(t.S, ctx, t.Log, BattleKind.Trainer, 0, 1, -1);
            if (!settle) return t;
            if (t.S.Pending.Count == 1 && t.S.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(t.S, ctx, t.S.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "좌석 0이 먼저");
            return t;
        }

        static ulong SeedFor(AttackDieFace[] faces)
        {
            for (ulong seed = 0; seed < 4000000; seed++)
            {
                var roll = AttackDice.Roll(new Pcg32(seed), faces.Length);
                bool same = true;
                for (int i = 0; i < roll.Length && same; i++) same = roll[i] == faces[i];
                if (same) return seed;
            }
            Assert.Fail("그 굴림이 나오는 시드가 없다");
            return 0;
        }

        /// <summary>좌석 0이 그 기술(배운 기술 칸 0에 넣는다)을 고른다. 여기서부터의 이벤트는 <see cref="Events"/>.</summary>
        void Begin(Arena t, string move, int slot = 0)
        {
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            int m = Mv(move);
            if (ctx.Cards.Pokemon[t.S.Pokemon[t.A].Def].Signature != m) t.S.Pokemon[t.A].Moves[slot] = m;
            t.From = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ch, Battles.Move(m), t.Log);
        }

        /// <summary>다음 난수로 굴릴 면을 정한다(굴림이 다음 답의 첫 난수일 때).</summary>
        void Next(Arena t, params AttackDieFace[] faces) => t.S.Rng = new Pcg32(SeedFor(faces));

        PendingChoice Ask(Arena t, ChoiceSub sub)
        {
            var c = Battles.Only(t.S, ChoiceKind.Reaction, sub);
            Assert.AreEqual(t.Card0, c.Source, "물음의 출처 = 그 카드");
            Assert.AreEqual(0, c.Seat);
            return c;
        }

        void Reply(Arena t, PendingChoice c, ChoiceAnswer a) => Battles.Answer(t.S, ctx, c, a, t.Log);

        List<GameEvent> Events(Arena t) => t.Log.Events.GetRange(t.From, t.Log.Events.Count - t.From);

        static List<T> All<T>(List<GameEvent> events) where T : GameEvent
        {
            var r = new List<T>();
            foreach (var e in events)
                if (e is T x) r.Add(x);
            return r;
        }

        static T One<T>(List<GameEvent> events) where T : GameEvent
        {
            var l = All<T>(events);
            Assert.AreEqual(1, l.Count, typeof(T).Name);
            return l[0];
        }

        bool Asking(Arena t) => t.S.Pending.Count == 1 && t.S.Pending[0].Kind == ChoiceKind.Reaction && t.S.Pending[0].Source.Equals(t.Card0);

        int Power(GameState s, string move, int attacker, int defender) => BattleRules.RollPower(s, ctx, Mv(move), attacker, defender, new List<Mod>());

        static List<int> Picks(List<ChoiceAnswer> legal)
        {
            var r = new List<int>();
            foreach (var a in legal)
                if (a.Kind == AnswerKind.Pick) r.Add(a.Target);
            return r;
        }

        // ── 굴리기 전 ──

        /// <summary>chef 장면: 규토리 일곱 색을 쥐면 스탬프(보상 규토리)가 나오니 스탬프는 이미 다 받은 것으로 둔다.</summary>
        Arena Chef()
        {
            var t = Duel(Garchomp, Garchomp, "chef");
            for (int i = 0; i < SeatState.StampCount; i++) t.S.Seats[0].Stamps[i] = true;
            return t;
        }

        [Test]
        public void A20_6_chef_빨강_규토리모든색하나씩으로_같은타입기술공격력1_배틀끝까지()
        {
            // 「Costs 1 of every Apricorn: Your Pokémon gain +1 attack strength to their moves that are the same type as them. Lasts until the end of battle.」
            var t = Chef();
            for (int c = 0; c < SeatState.ApricornColors; c++) t.S.Seats[0].Apricorns[c] = 2;
            t.S.Pokemon[t.A].Moves[1] = Mv("tackle");
            Begin(t, "twister");
            var ask = Ask(t, ChoiceSub.EffectOption);
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind, "「may」가 없는 비용 능력 — 쓰는 것은 선택, 기본 답은 안 씀");
            Next(t, R, R, R);
            Reply(t, ask, ChoiceAnswer.Of(AnswerKind.Accept));
            var ev = Events(t);
            Assert.AreEqual(2 + 1, One<DiceRolled>(ev).Power, "드래곤 포켓몬의 드래곤 기술 +1");
            Assert.AreEqual(SeatState.ApricornColors, All<ApricornSpent>(ev).Count);
            foreach (int n in t.S.Seats[0].Apricorns) Assert.AreEqual(1, n);
            Assert.AreEqual(1, All<AbilityUsed>(ev).Count);
            Assert.IsTrue(t.S.Battle.Sides[0].Masterwork);
            Assert.AreEqual(2, Power(t.S, "tackle", t.A, t.D), "포켓몬과 타입이 다른 기술에는 없다");
            // 같은 배틀에서 다시 묻지 않는다(원작 커뮤니티 확인) — 상대 턴을 넘기고 다시 공격
            Battles.Answer(t.S, ctx, Battles.Only(t.S, ChoiceKind.BattleAction), Battles.Move(Mv("dragon_rush")), t.Log);
            if (t.S.Battle == null) return;
            Begin(t, "twister");
            Assert.IsFalse(Asking(t), "배틀당 한 번");
        }

        [Test]
        public void A20_6_chef_빨강_규토리가모자라면묻지않고_안쓰면그대로()
        {
            var t = Chef();
            for (int c = 1; c < SeatState.ApricornColors; c++) t.S.Seats[0].Apricorns[c] = 1;
            Begin(t, "twister");
            Assert.IsFalse(Asking(t), "빨강 규토리가 없다");
            t = Chef();
            for (int c = 0; c < SeatState.ApricornColors; c++) t.S.Seats[0].Apricorns[c] = 1;
            Begin(t, "twister");
            var ask = Ask(t, ChoiceSub.EffectOption);
            Reply(t, ask, ask.Default.Clone());
            Assert.AreEqual(2, One<DiceRolled>(Events(t)).Power);
            Assert.AreEqual(1, t.S.Seats[0].Apricorns[0]);
        }

        [TestCase(ApricornColor.Red, 4)]
        [TestCase(ApricornColor.Pink, 5)]
        [TestCase(ApricornColor.Black, 6)]
        public void A20_6_ginkgo_merchant_빨강_규토리1개로기본공격력을4_5_6으로(ApricornColor color, int strength)
        {
            // 「Before attacking, you may discard 1 Apricorn to temporarily change the base attack strength of a move this turn to 4/5/6 based on the tier of the Apricorn used.」
            var t = Duel(Garchomp, Garchomp, "ginkgo_merchant");
            t.S.Seats[0].Apricorns[(int)ApricornColor.Red] = 1;
            t.S.Seats[0].Apricorns[(int)ApricornColor.Pink] = 1;
            t.S.Seats[0].Apricorns[(int)ApricornColor.Black] = 1;
            Begin(t, "tackle");
            var ask = Ask(t, ChoiceSub.EffectPick);
            var legal = Scene.Legal(t.S, ctx, ask);
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind, "「you may」 — 안 함이 첫 답");
            CollectionAssert.AreEqual(new[] { (int)ApricornColor.Red, (int)ApricornColor.Pink, (int)ApricornColor.Black }, Picks(legal), "가진 색만");
            Reply(t, ask, ChoiceAnswer.Pick((int)color));
            var ev = Events(t);
            Assert.AreEqual(strength, One<DiceRolled>(ev).Power);
            Assert.AreEqual(0, t.S.Seats[0].Apricorns[(int)color]);
            Assert.AreEqual(1, All<ApricornSpent>(ev).Count);
        }

        [Test]
        public void A34_ginkgo_merchant_빨강_기술의공격력은n이있으면그값이다()
        {
            // 감사 unclear 5(11-20): 기술 효과의 「공격력 = n」과 규토리로 바꾼 기본 공격력이 둘 다 Delta = 값 − 기본으로 들어가 기본이 두 번 빠졌다(파괴광선 2 + 분홍 5 − 6 = 1).
            // 「change the base attack strength」 = 기본값을 바꾼다 — 기술의 조건부 「= n」은 그 위에서 n으로 정한다 [가정 — 결정 대기]
            var t = Duel(Garchomp, Garchomp, "ginkgo_merchant");
            t.S.Seats[0].Apricorns[(int)ApricornColor.Pink] = 1;
            t.S.Pokemon[t.A].LastMove = Mv("hyper_beam");
            Begin(t, "hyper_beam");
            var ask = Ask(t, ChoiceSub.EffectPick);
            Reply(t, ask, ChoiceAnswer.Pick((int)ApricornColor.Pink));
            Assert.AreEqual(2, One<DiceRolled>(Events(t)).Power, "직전 턴에 쓴 파괴광선 = 2");
        }

        [Test]
        public void A20_6_swimmer_빨강_물기술의타입을벌레나비행으로()
        {
            // 「When you attack with a Water-type move, you may change its type to either Bug-type or Flying-type.」
            var t = Duel(Garchomp, Turtwig, "swimmer");
            Begin(t, "water_gun");
            var ask = Ask(t, ChoiceSub.EffectPick);
            CollectionAssert.AreEqual(new[] { (int)PokemonType.Bug, (int)PokemonType.Flying }, Picks(Scene.Legal(t.S, ctx, ask)));
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind);
            Next(t, R, R);
            Reply(t, ask, ChoiceAnswer.Pick((int)PokemonType.Flying));
            var ev = Events(t);
            Assert.AreEqual(PokemonType.Flying, One<TypeMatchup>(ev).Type);
            Assert.AreEqual(2, One<TypeMatchup>(ev).Delta, "비행 → 풀 +2");
            // 물 기술이 아니면 묻지 않는다
            t = Duel(Garchomp, Turtwig, "swimmer");
            Begin(t, "tackle");
            Assert.IsFalse(Asking(t));
        }

        [Test]
        public void A20_6_triathlete_빨강_세타입의기술을알면공격타입을그중하나로()
        {
            // 「When your Pokémon knows moves of 3 different types, you may change the type of your attack to any of those 3 types. Works for Typeless moves too.」
            var t = Duel(Garchomp, Turtwig, "triathlete");
            t.S.Pokemon[t.A].Moves[1] = Mv("water_gun"); // 시그니처 dragon_rush(드래곤) + water_gun(물) + tackle(노말)
            Begin(t, "tackle");
            var ask = Ask(t, ChoiceSub.EffectPick);
            CollectionAssert.AreEquivalent(new[] { (int)PokemonType.Dragon, (int)PokemonType.Water }, Picks(Scene.Legal(t.S, ctx, ask)), "지금 타입(노말)을 뺀 둘");
            Next(t, R, R);
            Reply(t, ask, ChoiceAnswer.Pick((int)PokemonType.Dragon));
            Assert.AreEqual(PokemonType.Dragon, One<TypeMatchup>(Events(t)).Type);
            // 타입이 둘뿐이면 묻지 않는다
            t = Duel(Garchomp, Turtwig, "triathlete");
            Begin(t, "tackle");
            Assert.IsFalse(Asking(t));
            Assert.AreEqual(0, TrainerRules.TripleTypes(ctx, t.S.Pokemon[t.A]).Count);
        }

        // ── 굴림 · 굴린 뒤 ──

        [Test]
        public void A20_6_veteran_빨강_얼음드래곤기술이면상태심볼을둘까지더한다()
        {
            // 「When you attack with an Ice-type or Dragon-type move, you may add up to 2 status symbols to roll.」 — dragon_rush: 심볼 2개 이상이면 마비
            var t = Duel(Garchomp, Garchomp, "veteran");
            Next(t, R, R, R, R, R);
            Begin(t, "dragon_rush");
            var ask = Ask(t, ChoiceSub.EffectPick);
            CollectionAssert.AreEqual(new[] { 1, 2 }, Picks(Scene.Legal(t.S, ctx, ask)));
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind);
            Reply(t, ask, ChoiceAnswer.Pick(2));
            var ev = Events(t);
            var change = All<DiceFacesChanged>(ev).Find(x => x.Source.Equals(t.Card0));
            Assert.IsNotNull(change);
            Assert.AreEqual(2, change.Changes.Length);
            Assert.AreEqual(AttackDieFace.StatusSymbol, change.Changes[0].After);
            Assert.IsTrue(StatusRules.Has(t.S.Pokemon[t.D], StatusEffect.Paralysed), "더한 심볼 둘로 dragon_rush의 조건이 찬다");
            // 다른 타입이면 묻지 않는다
            t = Duel(Garchomp, Garchomp, "veteran");
            Begin(t, "tackle");
            Assert.IsFalse(Asking(t));
        }

        // psychic R(사용자 결정 131): 굴린 뒤 반응 창의 반응 — 좌석 0의 Reaction/Fate 선택에 UseAbility(바꿀 면, 새 면) 답

        static ChoiceAnswer Mind(AttackDieFace from, AttackDieFace to) => ChoiceAnswer.Of(AnswerKind.UseAbility, (int)from, (int)to);

        void Hand(GameState s, int seat, params FateCard[] cards)
        {
            s.Seats[seat].FateHand.Clear();
            foreach (var c in cards) s.Seats[seat].FateHand.Add(FateRules.CardKey(ctx, c));
        }

        static PendingChoice React(GameState s, int seat) => s.Pending.Find(x => x.Kind == ChoiceKind.Reaction && x.Sub == ChoiceSub.Fate && x.Seat == seat && !x.Answered);

        /// <summary>굴린 뒤 창까지: 굴리기 전 창(운명 손패가 있는 좌석)은 「안 씀」으로 넘긴다. 좌석 0의 굴린 뒤 반응 선택을 돌려준다.</summary>
        PendingChoice ToAfterRoll(Arena t)
        {
            for (var c = t.S.Pending.Find(x => x.Kind == ChoiceKind.Reaction && x.Window == Window.BeforeRoll && !x.Answered); c != null; c = t.S.Pending.Find(x => x.Kind == ChoiceKind.Reaction && x.Window == Window.BeforeRoll && !x.Answered))
                Reply(t, c, ChoiceAnswer.Of(AnswerKind.Decline));
            var ask = React(t.S, 0);
            Assert.IsNotNull(ask, "좌석 0의 굴린 뒤 반응");
            Assert.AreEqual(Window.AfterRoll, ask.Window);
            return ask;
        }

        [Test]
        public void A20_6_psychic_빨강_에스퍼고스트기술이면주사위하나를다른면으로()
        {
            // 「When you attack with a Psychic-type or Ghost-type move, you may change the result of 1 dice rolled to any other value.」 — 결정 131: 굴린 뒤 반응 창의 반응
            var t = Duel(Garchomp, Garchomp, "psychic");
            Hand(t.S, 0);
            Hand(t.S, 1);
            Next(t, S, R);
            Begin(t, "confusion");
            var ask = ToAfterRoll(t);
            Assert.AreEqual(t.Card0, ask.Source, "운명 손패가 없으면 출처 = 그 카드");
            Assert.IsFalse(ask.HandGated, "공개 반응 — 숨은 손패 창이 아니다");
            var legal = Scene.Legal(t.S, ctx, ask);
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind, "「you may」 — 안 씀이 첫 답");
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind);
            // 바꿀 면 × 새 면: 심볼 → 일반 · 크리티컬, 일반 → 심볼 · 크리티컬
            Assert.AreEqual(1 + 4, legal.Count);
            foreach (var a in new[] { Mind(S, R), Mind(S, C), Mind(R, S), Mind(R, C) }) Assert.IsTrue(ChoiceRules.Contains(legal, a), a.ToString());
            Assert.IsTrue(Sinnoh.Ai.PrincipleAgent.Choose(t.S, ctx, ask, legal).Answer.SameAs(Mind(S, C)), "봇(원칙 AI): 심볼 → 크리티컬 [가정 §5 #665]");
            Reply(t, ask, Mind(S, C));
            var ev = Events(t);
            var change = All<DiceFacesChanged>(ev).Find(x => x.Source.Equals(t.Card0));
            Assert.AreEqual(1, change.Changes.Length);
            Assert.AreEqual(0, change.Changes[0].Index, "그 면의 첫 주사위");
            Assert.AreEqual(S, change.Changes[0].Before);
            Assert.AreEqual(C, change.Changes[0].After);
            Assert.AreEqual(AbilityColor.Red, One<AbilityUsed>(ev).Ability);
            Assert.AreEqual(2 + 1, One<DamageBreakdown>(ev).Final, "크리티컬 2 + 일반 1");
            Assert.IsNull(React(t.S, 0), "공격당 한 번 — 쓴 뒤 바퀴에 다시 묻지 않는다");
            t = Duel(Garchomp, Garchomp, "psychic");
            Hand(t.S, 0);
            Hand(t.S, 1);
            Begin(t, "tackle");
            Assert.IsNull(React(t.S, 0), "노말 기술");
            Assert.IsFalse(Asking(t));
        }

        [Test]
        public void G6_결정131_psychic빨강을안쓰고받는쪽이디아루가를내면둘째바퀴에공격자가바꾼다()
        {
            // 공격하는 쪽 → 받는 쪽 순서로 한꺼번에 묻고, 누가 쓰면 한 바퀴 더(G6) — 디아루가로 다시 굴린 뒤에도 psychic R을 쓸 수 있다(「attacker decides」 F11)
            var t = Duel(Garchomp, Garchomp, "psychic");
            Hand(t.S, 0);
            Hand(t.S, 1, FateCard.Dialga);
            Next(t, S, R, C);                                   // 굴림 둘 → 디아루가가 다시 굴린 눈 = 크리티컬
            Begin(t, "confusion");
            var mine = ToAfterRoll(t);
            var theirs = React(t.S, 1);
            Assert.IsNotNull(theirs, "받는 쪽의 디아루가 창 — 같은 창 · 같은 바퀴");
            Assert.AreEqual(mine.GroupId, theirs.GroupId, "한 묶음(G6 한꺼번에)");
            Reply(t, mine, ChoiceAnswer.Of(AnswerKind.Decline));
            var dialga = ChoiceAnswer.Of(AnswerKind.PlayFate, (int)FateCard.Dialga, 1);
            dialga.Values.Add(1);
            Reply(t, theirs, dialga);
            CollectionAssert.AreEqual(new[] { S, C }, t.S.Battle.Faces, "디아루가가 일반 타격을 다시 굴렸다");
            var again = React(t.S, 0);
            Assert.IsNotNull(again, "둘째 바퀴: 공격자가 다시 받는다(한도 안)");
            Assert.IsNull(React(t.S, 1), "받는 쪽은 이번 차례에 운명을 썼다");
            var legal = Scene.Legal(t.S, ctx, again);
            Assert.IsTrue(ChoiceRules.Contains(legal, Mind(S, C)));
            Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.UseAbility && a.Target == (int)R), "일반 타격은 이제 없다");
            Reply(t, again, Mind(S, C));
            var ev = Events(t);
            var order = All<DiceFacesChanged>(ev).ConvertAll(x => x.Source.Kind);
            CollectionAssert.AreEqual(new[] { SourceKind.Fate, SourceKind.TrainerCard }, order, "디아루가 → (다음 바퀴) psychic R");
            Assert.AreEqual(2 + 2, One<DamageBreakdown>(ev).Final, "크리티컬 둘");
            Assert.IsNull(React(t.S, 0), "셋째 바퀴는 없다");
        }

        [Test]
        public void G6_결정131_psychic빨강을쓴뒤받는쪽이디아루가를내면공격자는다시못바꾼다_공격쪽부터적용()
        {
            var t = Duel(Garchomp, Garchomp, "psychic");
            Hand(t.S, 0);
            Hand(t.S, 1, FateCard.Dialga);
            Next(t, S, R, S);                                   // 디아루가가 다시 굴린 눈 = 심볼
            Begin(t, "confusion");
            var mine = ToAfterRoll(t);
            var theirs = React(t.S, 1);
            var dialga = ChoiceAnswer.Of(AnswerKind.PlayFate, (int)FateCard.Dialga, 1);
            dialga.Values.Add(0);                               // 받는 쪽은 앞 주사위(지금은 심볼)를 고른다 — 공격 쪽 반응이 먼저 적용돼 크리티컬이 된 주사위다
            Reply(t, theirs, dialga);                           // 답하는 차례와 상관없이 적용은 공격하는 쪽부터(G6)
            Reply(t, mine, Mind(S, C));
            var ev = Events(t);
            var changes = All<DiceFacesChanged>(ev);
            CollectionAssert.AreEqual(new[] { SourceKind.TrainerCard, SourceKind.Fate }, changes.ConvertAll(x => x.Source.Kind), "공격하는 쪽 → 받는 쪽");
            Assert.AreEqual(C, changes[1].Changes[0].Before, "디아루가는 psychic R이 바꾼 크리티컬을 다시 굴렸다");
            Assert.IsNull(React(t.S, 0), "둘째 바퀴: 이미 쓴 psychic R은 없다(공격당 한 번)");
            Assert.IsNull(React(t.S, 1));
            Assert.AreEqual(0 + 1, One<DamageBreakdown>(ev).Final, "심볼 0 + 일반 1");
        }

        [Test]
        public void A34_결정131_psychic빨강_주사위가고정된공격과포획굴림에는후보가없다()
        {
            var t = Duel(Garchomp, Garchomp, "psychic");
            Hand(t.S, 0);
            Hand(t.S, 1);
            Next(t, S, R);
            Begin(t, "confusion");
            var ask = ToAfterRoll(t);
            Assert.IsTrue(TrainerRules.PsychicOpen(t.S, ctx, 0));
            var b = t.S.Battle;
            int move = b.Move;
            // sacred_sword 「cannot be replaced or removed by any effect」(DiceLocked): 에스퍼 타입으로 바꿔 둔 공격이어도 없다
            b.Move = Mv("sacred_sword");
            b.TypeTo = (int)PokemonType.Psychic;
            Assert.IsTrue(EffectRules.DiceLocked(t.S, ctx));
            Assert.IsFalse(TrainerRules.PsychicOpen(t.S, ctx, 0));
            var cands = new List<ReactionCandidate>();
            Reactions.Candidates(t.S, ctx, Window.AfterRoll, cands);
            Assert.IsFalse(cands.Exists(c => c.Seat == 0), "DiceLocked면 후보 없음");
            Assert.IsFalse(Scene.Legal(t.S, ctx, ask).Exists(a => a.Kind == AnswerKind.UseAbility));
            b.Move = move;
            b.TypeTo = -1;
            // 포획 굴림(Step = Capture)도 공격이 아니다
            var step = b.Step;
            b.Step = BattleStep.Capture;
            Assert.IsFalse(TrainerRules.PsychicOpen(t.S, ctx, 0));
            b.Step = step;
            // 받는 쪽 좌석에는 없다
            Card(t.S, 1, "psychic");
            Assert.IsFalse(TrainerRules.PsychicOpen(t.S, ctx, 1));
        }

        // ── 상태: hex_maniac ──

        [Test]
        public void A20_6_hex_maniac_파랑_상태를걸때상태주사위로대신_면역이어도굴릴수있다()
        {
            // 「When inflicting any status effects, you may instead roll the status dice to inflict the new status effect it lands on.」
            // ember(화상) → 불꽃 타입 상대는 화상 면역이지만 다시 굴릴 수 있다(원작 커뮤니티 확인)
            var t = Duel(Garchomp, Vulpix, "hex_maniac", TrainerRules.Silver);
            Next(t, R, R);
            Begin(t, "ember");
            var ask = Ask(t, ChoiceSub.EffectOption);
            Assert.AreEqual(1, ask.Count, "파랑 칸의 물음");
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind);
            ulong seed = Battles.Seed(x =>
            {
                var st = StatusRules.RollDie(new Pcg32(x));
                return st != StatusEffect.Burned && StatusRules.Check(ctx, t.S.Pokemon[t.D], st) == StatusResult.Applied;
            });
            var rolled = StatusRules.RollDie(new Pcg32(seed));
            t.S.Rng = new Pcg32(seed);
            Reply(t, ask, ChoiceAnswer.Of(AnswerKind.Accept));
            var applied = One<StatusApplied>(Events(t));
            Assert.AreEqual(rolled, applied.Status);
            Assert.AreEqual(StatusResult.Applied, applied.Result);
            Assert.IsTrue(StatusRules.Has(t.S.Pokemon[t.D], rolled));
            Assert.AreEqual(AbilityColor.Blue, One<AbilityUsed>(Events(t)).Ability);
        }

        [Test]
        public void A20_6_hex_maniac_파랑_안쓰면원래상태()
        {
            var t = Duel(Garchomp, Garchomp, "hex_maniac", TrainerRules.Silver);
            Next(t, R, R);
            Begin(t, "twister");
            var ask = Ask(t, ChoiceSub.EffectOption);
            Reply(t, ask, ask.Default.Clone());
            Assert.AreEqual(StatusEffect.Paralysed, One<StatusApplied>(Events(t)).Status);
            Assert.AreEqual(0, All<AbilityUsed>(Events(t)).Count);
        }

        [Test]
        public void A20_6_hex_maniac_빨강_이미상태가있어도다른상태를더건다_고스트기술공격력1()
        {
            // 「You may inflict additional status effects onto a target so long as the target does not have that status effect already. Additionally, your Ghost-type moves gain +1 attack strength.」
            var t = Duel(Garchomp, Garchomp, "hex_maniac");
            var target = t.S.Pokemon[t.D];
            target.Status = StatusEffect.Poisoned;
            Assert.AreEqual(2 + 1, Power(t.S, "lick", t.A, t.D), "고스트 기술 +1");
            Assert.AreEqual(2, Power(t.S, "tackle", t.A, t.D));
            Next(t, R, R);
            Begin(t, "twister");
            var blue = Ask(t, ChoiceSub.EffectOption);
            Assert.AreEqual(1, blue.Count);
            Reply(t, blue, ChoiceAnswer.Of(AnswerKind.Decline));
            var red = Ask(t, ChoiceSub.EffectOption);
            Assert.AreEqual(2, red.Count, "빨강 칸의 물음");
            Assert.AreEqual(AnswerKind.Decline, red.Default.Kind, "「You may」");
            Reply(t, red, ChoiceAnswer.Of(AnswerKind.Accept));
            Assert.AreEqual(StatusResult.Applied, One<StatusApplied>(Events(t)).Result);
            Assert.IsTrue(StatusRules.Has(target, StatusEffect.Poisoned) && StatusRules.Has(target, StatusEffect.Paralysed));
            // 수 제한이 없다: 셋째 상태도(둘로 좁히지 않는다) · 같은 상태는 다시 걸리지 않는다
            Assert.AreEqual(StatusResult.Applied, StatusRules.InflictExtra(ctx, target, StatusEffect.Burned));
            Assert.AreEqual(StatusResult.Already, StatusRules.InflictExtra(ctx, target, StatusEffect.Burned));
            var list = new List<StatusEffect>();
            StatusRules.Statuses(target, list);
            CollectionAssert.AreEqual(new[] { StatusEffect.Poisoned, StatusEffect.Paralysed, StatusEffect.Burned }, list.GetRange(0, 3));
            StatusRules.Remove(target, StatusEffect.Poisoned);
            StatusRules.Statuses(target, list);
            Assert.AreEqual(StatusEffect.Paralysed, list[0]);
            Assert.IsFalse(StatusRules.Has(target, StatusEffect.Poisoned));
            Assert.IsTrue(StatusRules.Has(target, StatusEffect.Burned));
            StatusRules.Clear(target);
            StatusRules.Statuses(target, list);
            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void A20_6_hex_maniac_빨강_안쓰면이미걸린상태가막는다()
        {
            var t = Duel(Garchomp, Garchomp, "hex_maniac");
            t.S.Pokemon[t.D].Status = StatusEffect.Poisoned;
            Next(t, R, R);
            Begin(t, "twister");
            Reply(t, Ask(t, ChoiceSub.EffectOption), ChoiceAnswer.Of(AnswerKind.Decline));
            Reply(t, Ask(t, ChoiceSub.EffectOption), ChoiceAnswer.Of(AnswerKind.Decline));
            Assert.AreEqual(StatusResult.Already, One<StatusApplied>(Events(t)).Result);
            Assert.IsFalse(StatusRules.Has(t.S.Pokemon[t.D], StatusEffect.Paralysed));
        }

        // ── 우선권 토큰: ninja_boy ──

        [Test]
        public void A20_6_ninja_boy_파랑_토큰을쓰면우선도가낮아도먼저()
        {
            // 「When you have Initiative Advantage, you may use it to go first regardless of actual initiative values.」
            var t = Duel(Bidoof, Garchomp, "ninja_boy", TrainerRules.Silver, false);
            var ask = Ask2(t);
            Reply(t, ask, ChoiceAnswer.Of(AnswerKind.Accept));
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "우선도 4 대 11이지만 먼저");
            Assert.IsTrue(t.S.Battle.Sides[1].InitiativeToken, "쓴 토큰은 상대에게");
            var used = One<AbilityUsed>(Events(t));
            Assert.AreEqual(AbilityColor.Blue, used.Ability, "느린 쪽이 토큰으로 먼저 가면 파랑 능력을 쓴 것(AbilityUsed — M6 봇 판의 능력 집계)");
            // 안 쓰면 우선도대로
            t = Duel(Bidoof, Garchomp, "ninja_boy", TrainerRules.Silver, false);
            Reply(t, Ask2(t), ChoiceAnswer.Of(AnswerKind.Decline));
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
            Assert.IsTrue(t.S.Battle.Sides[0].InitiativeToken);
            Assert.IsFalse(Events(t).Exists(e => e is AbilityUsed), "안 쓰면 능력도 안 씀");
            // 카드가 없으면 묻지 않는다
            t = Duel(Bidoof, Garchomp, null, TrainerRules.Silver, false);
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
            // 파랑만 있고 이미 빠르면 묻지 않는다(쓸 까닭이 없다)
            t = Duel(Garchomp, Bidoof, "ninja_boy", TrainerRules.Silver, false);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
        }

        PendingChoice Ask2(Arena t)
        {
            var c = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.InitiativeToken);
            Assert.AreEqual(0, c.Seat);
            return c;
        }

        [Test]
        public void A20_6_ninja_boy_빨강_토큰을쓰면독혼란졸림중하나를반드시건다()
        {
            // 「Inflict either the Poisoned, Confused or Drowsy status effects on the opposing Pokémon whenever you use Initiative Advantage.」 — 원작 커뮤니티 확인
            var t = Duel(Garchomp, Bidoof, "ninja_boy", TrainerRules.Gold, false);
            t.From = t.Log.Events.Count;
            Reply(t, Ask2(t), ChoiceAnswer.Of(AnswerKind.Accept)); // 이미 빠르지만 토큰을 쓸 수 있다(「regardless of actual initiative values」)
            var pick = Ask(t, ChoiceSub.EffectPick);
            var legal = Scene.Legal(t.S, ctx, pick);
            Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.Decline), "강제 — 안 함이 없다");
            CollectionAssert.AreEquivalent(new[] { (int)StatusEffect.Poisoned, (int)StatusEffect.Confused, (int)StatusEffect.Drowsy }, Picks(legal));
            Assert.AreEqual(AnswerKind.Pick, pick.Default.Kind, "기본 답 = 첫 합법 답(rulings A33)");
            Reply(t, pick, ChoiceAnswer.Pick((int)StatusEffect.Drowsy));
            Assert.IsTrue(StatusRules.Has(t.S.Pokemon[t.D], StatusEffect.Drowsy));
            Assert.AreEqual(AbilityColor.Red, One<AbilityUsed>(Events(t)).Ability);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
            // 안 쓰면 걸지 않는다(쓰는 것은 선택 — 원작 커뮤니티 확인)
            t = Duel(Garchomp, Bidoof, "ninja_boy", TrainerRules.Gold, false);
            Reply(t, Ask2(t), ChoiceAnswer.Of(AnswerKind.Decline));
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.D].Status);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
        }

        // ── 상대의 기술 · 첫 공격 2배 ──

        [Test]
        public void A20_6_poke_maniac_파랑_상대의시그니처기술을쓸수있다()
        {
            // 「When attacking, you may use the signature move of the opposing Pokémon against them.」
            var t = Duel(Garchomp, Turtwig, "poke_maniac", TrainerRules.Silver);
            int leaf = Mv("razor_leaf");
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            Assert.IsTrue(Scene.Legal(t.S, ctx, ch).Exists(a => a.Kind == AnswerKind.UseMove && a.Target == leaf));
            t.From = t.Log.Events.Count;
            Reply(t, ch, Battles.Move(leaf));
            var ev = Events(t);
            Assert.AreEqual(leaf, One<MoveUsed>(ev).MoveKey);
            Assert.AreEqual(t.D, One<MoveUsed>(ev).TargetId, "「against them」");
            Assert.AreEqual(AbilityColor.Blue, One<AbilityUsed>(ev).Ability);
            // 카드가 없거나 브론즈면 없다
            t = Duel(Garchomp, Turtwig, "poke_maniac", TrainerRules.Bronze);
            Assert.IsFalse(Scene.Legal(t.S, ctx, Battles.Only(t.S, ChoiceKind.BattleAction)).Exists(a => a.Kind == AnswerKind.UseMove && a.Target == leaf));
        }

        [Test]
        public void A20_6_poke_maniac_파랑_어느시그니처기술이든예외없이돈다()
        {
            var seen = new HashSet<int>();
            int tried = 0;
            var never = new List<string>(); // 좌석 0이 한 번도 못 고른 판(먼저 쓰러짐)
            foreach (var def in ctx.Cards.Pokemon)
            {
                if (!seen.Add(def.Signature) || def.Signature == ctx.Cards.Pokemon[Scene.Def(ctx, Garchomp)].Signature) continue;
                var t = new Arena { S = Battles.Game(ctx, 2) };
                t.A = Scene.Give(t.S, ctx, 0, Garchomp);
                t.D = Scene.Give(t.S, ctx, 1, def.Key);
                t.Card0 = Card(t.S, 0, "poke_maniac", TrainerRules.Silver);
                Battles.Start(t.S, ctx, t.Log, BattleKind.Trainer, 0, 1, -1);
                int used = 0;
                Battles.Drive(t.S, ctx, t.Log, (s, c) =>
                {
                    if (c.Kind != ChoiceKind.BattleAction || c.Seat != 0 || used >= 3) return used >= 3 && c.Kind == ChoiceKind.BattleAction && c.Seat == 0 && Scene.Legal(s, ctx, c).Exists(a => a.Kind == AnswerKind.Forfeit) ? ChoiceAnswer.Of(AnswerKind.Forfeit) : null;
                    var a2 = Scene.Legal(s, ctx, c).Find(a => a.Kind == AnswerKind.UseMove && a.Target == ctx.Cards.Pokemon[s.Pokemon[s.Battle.Sides[1].Active].Def].Signature);
                    if (a2 != null) used++;
                    return a2;
                }, 400);
                tried++;
                if (used == 0) never.Add(def.Key);
            }
            TestContext.WriteLine($"[poke_maniac] 시그니처 기술 {tried}종 · 못 쓴 판 {never.Count}: {string.Join(" ", never)}");
            Assert.Greater(tried, 200);
            Assert.Less(never.Count, tried / 10, "거의 모든 시그니처 기술을 실제로 써 봤다");
        }

        [Test]
        public void A20_6_essentia_빨강_움직이지않고건야생배틀의첫공격은2배()
        {
            // 「When you start a battle against a wild Pokémon, if you have not moved anywhere yet this turn, your first attack against them deals double damage.」
            Arena Wild(bool moved)
            {
                var t = new Arena { S = Battles.Game(ctx, 2) };
                t.A = Scene.Give(t.S, ctx, 0, Garchomp);
                t.D = Scene.Spawn(t.S, ctx, Garchomp, SeatId.Wild);
                t.Card0 = Card(t.S, 0, "essentia");
                t.S.Seats[0].Moved = moved;
                Battles.Wild(t.S, ctx, t.Log, 0, t.D);
                if (t.S.Pending.Count == 1 && t.S.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(t.S, ctx, t.S.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
                Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
                return t;
            }
            var w = Wild(false);
            Next(w, R, R);
            Begin(w, "tackle");
            var d = All<DamageBreakdown>(Events(w)).Find(x => x.TargetId == w.D);
            Assert.AreEqual(4, d.Final, "(일반 2) × 2");
            Assert.IsTrue(new List<DamageStep>(d.Steps).Exists(x => x.Kind == DamageStepKind.Double));
            Assert.AreEqual(1, All<AbilityUsed>(Events(w)).Count);
            Assert.IsTrue(w.S.Battle.Sides[0].FirstAttackDone);
            Assert.IsFalse(TrainerRules.Opportune(w.S, ctx, w.S.Battle), "첫 공격만");
            // 이번 턴에 움직였으면 없다
            w = Wild(true);
            Next(w, R, R);
            Begin(w, "tackle");
            Assert.AreEqual(2, All<DamageBreakdown>(Events(w)).Find(x => x.TargetId == w.D).Final);
            // 트레이너전에는 없다(「against a wild Pokémon」)
            var t2 = Duel(Garchomp, Garchomp, "essentia");
            Assert.IsFalse(TrainerRules.Opportune(t2.S, ctx, t2.S.Battle));
        }

        [Test]
        public void M6_배틀능력은잠긴등급에서는묻지않는다()
        {
            foreach (string key in new[] { "swimmer", "psychic", "veteran", "hex_maniac" })
            {
                var t = Duel(Garchomp, Garchomp, key, key == "hex_maniac" ? TrainerRules.Bronze : TrainerRules.Silver);
                Begin(t, key == "swimmer" ? "water_gun" : key == "psychic" ? "confusion" : "twister");
                Assert.IsFalse(Asking(t), key);
            }
        }
    }
}
