using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 울트라 온슬롯(실험 선택 규칙, rulings A21-1·A21-2 · A56 — M6 「울트라 온슬롯」). 옵션 RuleOptions.UltraOnslaught(울트라 버스트 전용, 기본 끔).
    /// - 뽑기: UB·네크로즈마와 배틀할 때 그쪽의 배틀 턴이 시작될 때마다 덱 맨 위 1장을 뽑아 바로 적용하고 버린다(세이브 덱 설명 「draw and play 1 Ultra Onslaught card at the start of each of their turns.
    ///   Discard any played cards. If the deck is out of cards, shuffle the discard pile back into the deck」). 버림 더미는 배틀 사이에도 남는다(A21-1 [추정]).
    ///   호수 수호신·실버디 배틀에는 없다(원작 커뮤니티 확인).
    /// - 카드 여섯(영문 카드 문구, 키 ultra_onslaught.1~6 — 앞면에 이름이 없어 번호가 키다):
    ///   1 「Do not attack this turn. Heal 4 damage and all status effects.」 = 그 배틀 턴에 행동이 없다(직전 기술 없음 · 턴 끝 효과는 그대로) · 상태이상을 먼저 풀고 4 회복(독이 회복을 막지 않게 — UB에게 유리하게).
    ///   2 「Replace all status symbols with critical hits.」 = 그 배틀 턴의 굴림에서 다른 면 바꾸기(졸림·마비·지우기)가 다 끝난 뒤 남은 상태이상 심볼을 모두 크리티컬로 —
    ///     졸림이 만든 심볼도 바뀐다(원작 커뮤니티 확인). 다시 굴린 주사위(디아루가)에도 피해 계산 직전에 한 번 더.
    ///   3 「Immediately trigger the effects of Beast Boost. If Beast Boost is already active, add to its effect.」 = 피로 −3 · 다음 공격 공격력 +1(쌓인다 — LegendRules).
    ///   4 「When next attacked, all regular hits damage the attacker as well.」 = 다음에 공격받을 때 그 굴림의 일반 타격 하나마다 공격한 포켓몬도 1 피해(그 턴 피해 버퍼 — 일반 타격이 없어도 쓰인다).
    ///   5 「Use Alpha Strike twice this turn.」 = 그 배틀 턴의 행동은 알파 스트라이크, 이어서 한 번 더(둘째는 공격력 2 — 원작 커뮤니티 확인 같은 메시지;
    ///     첫 공격으로 상대가 쓰러졌으면 다음 포켓몬에게 — 「in favour of the Ultra Beasts」). 타입은 원칙 AI(PrincipleRules.AlphaType)가 공격마다 고른다.
    ///   6 「Deal double damage to the opposing Pokémon if they have any Distortion Breach cards attached.」 = 그 배틀 턴의 UB 공격은 받는 포켓몬에 균열 카드가 붙어 있으면 2배.
    /// </summary>
    public static class UbOnslaught
    {
        public const int Rest = 1, Crits = 2, Boost = 3, Thorns = 4, TwinStrike = 5, Rend = 6;
        public const int Cards = 6;
        /// <summary>카드 1의 회복량.</summary>
        public const int RestHeal = 4;

        const string KeyPrefix = "ultra_onslaught.";

        /// <summary>그 번호(1~6)의 카드 색인(카드 표).</summary>
        public static int CardOf(Ctx ctx, int number) => ctx.Decks.CardIds.IndexOf(KeyPrefix + number);

        /// <summary>그 카드의 번호(1~6). 온슬롯 카드가 아니면 0.</summary>
        public static int NumberOf(Ctx ctx, int card)
        {
            for (int n = 1; n <= Cards; n++)
                if (CardOf(ctx, n) == card) return n;
            return 0;
        }

        static SourceRef Source(Ctx ctx, int number) => new SourceRef(SourceKind.Onslaught, CardOf(ctx, number));

        /// <summary>이 배틀에서 온슬롯 카드를 뽑나: 울트라 버스트 판 · 옵션 켬 · UB 배틀이나 네크로즈마 배틀(A21-2 — 신오의 심장에서는 옵션이 켜져 있어도 무시).</summary>
        public static bool On(GameState s, BattleState b) =>
            s.Ub != null && s.Options.UltraOnslaught && b != null && (b.Kind == BattleKind.UltraBeast || b.Kind == BattleKind.Necrozma);

        /// <summary>지금 차례 쪽이 UB·네크로즈마이고 이번 배틀 턴의 카드가 그 번호인가.</summary>
        static bool Playing(BattleState b, int number) =>
            b != null && b.Onslaught == number && b.ActiveSide >= 0 && b.Sides[b.ActiveSide].Owner == SeatId.UltraBeast;

        /// <summary>
        /// 배틀 턴 시작(BattleFlow.SelectStep — 그 턴의 임시값을 비운 뒤): 앞 턴의 카드를 지우고, UB·네크로즈마의 차례면 맨 위 1장을 뽑아 버림 더미에 놓고(OnslaughtDrawn) 바로 적용한다.
        /// 덱이 비었으면 버림 더미를 섞어 덱으로(PileShuffled). 뽑은 카드 번호(없으면 0) — 1(행동 없음)·5(알파 스트라이크 두 번)는 부르는 쪽이 그 턴의 행동을 정한다.
        /// </summary>
        internal static int TurnStarted(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            b.Onslaught = 0;
            var side = b.Sides[b.ActiveSide];
            if (!On(s, b) || side.Owner != SeatId.UltraBeast || side.Active < 0) return 0;
            var deck = s.Decks[(int)DeckId.UltraOnslaught];
            var discard = s.Discards[(int)DeckId.UltraOnslaught];
            if (deck.Count == 0)
            {
                if (discard.Count == 0) return 0;
                deck.AddRange(discard);
                discard.Clear();
                Piles.Shuffle(deck, s.Rng);
                if (fx.On) fx.Add(new PileShuffled { Seat = SeatId.System, Pile = DeckId.UltraOnslaught });
            }
            int card = deck[deck.Count - 1];
            deck.RemoveAt(deck.Count - 1);
            discard.Add(card);
            int number = NumberOf(ctx, card);
            b.Onslaught = number;
            fx.Begin(side.Responder, ActionKind.BattleAction, AutoKind.Auto); // 카드 한 장 = 가상 쪽의 자동 행동 하나(01 §3-6 ①)
            var src = Source(ctx, number);
            if (fx.On) fx.Add(new OnslaughtDrawn { Seat = side.Owner, Source = src, CardKey = card, Effect = number });
            var p = s.Pokemon[side.Active];
            switch (number)
            {
                case Rest:
                {
                    // 상태이상을 먼저 푼다(독이면 회복이 0이 된다 — UB에게 유리하게, rulings A56 ②). 회복은 그 턴 피해 버퍼로(p.13)
                    var before = p.Status;
                    StatusRules.Clear(p);
                    int hp = PokemonRules.Hp(ctx, p);
                    if (fx.On) fx.Add(new Healed { Seat = side.Owner, Source = src, Pokemon = new[] { new HealEntry { Id = side.Active, HpBefore = hp, HpAfter = hp, StatusBefore = before } } });
                    BattleFlow.Buffer(b, side.Active, RestHeal, src);
                    break;
                }
                case Boost:
                {
                    int before = p.Fatigue;
                    p.Fatigue = System.Math.Max(0, p.Fatigue - LegendRules.BeastBoostFatigue);
                    side.BeastBoost += LegendRules.BeastBoostPower;
                    if (fx.On) fx.Add(new FatigueChanged { Source = src, PokemonId = side.Active, Before = before, After = p.Fatigue, Cause = FatigueCause.BeastBoost });
                    break;
                }
                case Thorns:
                    b.OnslaughtThorns = true;
                    break;
            }
            return number;
        }

        /// <summary>카드 5의 알파 스트라이크 답(원칙 AI가 고른 타입 — 지금 받는 포켓몬에게).</summary>
        internal static ChoiceAnswer Strike(GameState s, Ctx ctx, BattleState b)
        {
            int k = b.ActiveSide;
            int def = b.Sides[1 - k].Active;
            return ChoiceRules.AlphaStrikeAnswer(PrincipleRules.AlphaType(ctx, s.Pokemon[b.Sides[k].Active], def >= 0 ? s.Pokemon[def] : null));
        }

        /// <summary>카드 5의 둘째 알파 스트라이크인가(공격력 2 — LegendRules.PowerMods).</summary>
        public static bool Repeats(GameState s, int attacker)
        {
            var b = s.Battle;
            return Playing(b, TwinStrike) && b.InSecond && b.Sides[b.ActiveSide].Active == attacker;
        }

        /// <summary>
        /// 카드 2: 지금 굴림의 상태이상 심볼을 모두 크리티컬로(DiceFacesChanged, 출처 = 그 카드). 다른 면 바꾸기가 다 돈 뒤(굴린 뒤 창 앞)와 피해 계산 직전에 부른다 — 바꿀 심볼이 없으면 아무 일 없다.
        /// </summary>
        internal static void Sharpen(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            if (!Playing(b, Crits)) return;
            var faces = b.Faces;
            var before = AttackDice.Src(faces);
            List<FaceChange> changes = null;
            for (int i = 0; i < faces.Count; i++)
            {
                if (faces[i] != AttackDieFace.StatusSymbol) continue;
                faces[i] = AttackDieFace.CriticalHit;
                (changes ?? (changes = new List<FaceChange>())).Add(new FaceChange { Index = i, Before = AttackDieFace.StatusSymbol, After = AttackDieFace.CriticalHit });
            }
            if (changes != null && fx.On) fx.Add(new DiceFacesChanged { Source = Source(ctx, Crits), Changes = changes.ToArray(), SrcBefore = before, SrcAfter = AttackDice.Src(faces) });
        }

        /// <summary>
        /// 카드 4(BattleFlow.DamageStep — 공격의 마지막 굴림으로): UB·네크로즈마가 공격받았고 그 카드가 기다리고 있으면 일반 타격 하나마다 공격한 포켓몬에게 1 피해를 버퍼에 넣고 카드를 쓴 것으로 한다.
        /// </summary>
        internal static void Attacked(GameState s, Ctx ctx, BattleState b, int attacker, int defender)
        {
            if (!b.OnslaughtThorns || defender < 0 || s.Pokemon[defender].Owner != SeatId.UltraBeast) return;
            b.OnslaughtThorns = false;
            int n = AttackDice.Count(b.Faces, AttackDieFace.RegularHit);
            if (n > 0) BattleFlow.Buffer(b, attacker, -n, Source(ctx, Thorns));
        }

        /// <summary>카드 6: 이번 배틀 턴의 UB 공격이고 받는 포켓몬에 균열 카드가 붙어 있다 — 피해 2배(BattleFlow.Damage).</summary>
        public static bool Doubles(GameState s, BattleState b, int defender) =>
            Playing(b, Rend) && defender >= 0 && BreachRules.Any(s.Pokemon[defender]);
    }
}
