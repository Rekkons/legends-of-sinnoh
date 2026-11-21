using System;

namespace Sinnoh.Rules
{
    /// <summary>
    /// UB 배틀과 프리즘 아머(룰북 p.24·p.25, 01 §6-4·§6-6, rulings A53 — M6 「UB 배틀·프리즘 아머」).
    /// - UB 배틀: UB 토큰이 있는 칸에서 찾아보기(MapAction Search, Target = 그 토큰 — p.25 「click "Search" whilst standing on one of their tokens」) → BattleKind.UltraBeast.
    ///   UB 카드는 덱 밖의 새 인스턴스라 도전마다 완전 회복이다(p.25 「fully restored between battle attempts」). 포획 불가(카드 `uncapturable` · ChoiceRules.CaptureAllowed) ·
    ///   전설 특수기술(알파 스트라이크·비스트 부스트 — BattleRules.LegendaryMovesEligible) · 가상 응답자 SeatId.UltraBeast(p.25 원칙 = PrincipleRules) ·
    ///   내 포켓몬을 모두 쓰러뜨리면 UB 승리(도망·규토리 없음 — 토큰은 그대로 남는다).
    /// - 이기면(<see cref="Defeated"/>): UB 토큰 제거 · 격파 수 +1 · 프리즘 아머 1개가 울트라 버스트 쪽 → 울트라 비스트 쪽(p.24) · 전설 보상 JP 6과 UB의 기술 가르치기
    ///   (p.25 「treated like any other Legendary Pokémon for tactics and rewards」 · 원작 커뮤니티 확인) · 스탬프 2(StampRules) ·
    ///   균열 카드 버리기·옮기기(BreachRules.OpenRelease) → 턴 끝(추가 턴 없음 — 원작 커뮤니티 확인).
    /// - 프리즘 아머(p.24 「For every 2 Prism Armour tokens: Reduce the total damage taken per turn by 1」): 배틀 턴 하나(한쪽의 차례)마다 UB·네크로즈마가 받는 피해의 합에서 ⌊개수/2⌋까지 줄인다
    ///   — 그 턴에 피해가 적용되는 순서대로 쓴다(공격·피로 → 턴 끝의 독 …, BattleState.PrismUsed). 내 차례에는 공격 피해가, UB 차례에는 자해·피로·상태 피해가 줄어든다(원작 커뮤니티 확인). 2배 뒤의 총 피해에서 뺀다(FAQ p.13 · 원작 커뮤니티 확인 — 피해 버퍼의 값이 이미 2배 뒤다).
    ///   multi_attack 「Ignores the effects of Prism Armour」의 피해는 줄지 않고 그 턴의 몫도 쓰지 않는다(EffectRules.IgnoresPrism).
    ///   UB 배틀은 울트라 비스트 쪽 개수(처음 0), 네크로즈마 배틀은 울트라 버스트 쪽 개수(처음 9 = −4)를 읽는다.
    /// </summary>
    public static class UbBattle
    {
        /// <summary>UB 격파 보상 JP(전설 4 + 쓰러뜨림 2 — BattleRules.RewardJp).</summary>
        public const int RewardJp = 6;

        /// <summary>그 칸의 UB 토큰(토큰 색인). 없으면 −1.</summary>
        public static int TokenAt(GameState s, int cell)
        {
            if (s.Ub == null || cell < 0) return -1;
            for (int i = 0; i < s.Tokens.Count; i++)
                if (s.Tokens[i].Kind == TokenKind.UltraBeast && s.Tokens[i].Cell == cell) return i;
            return -1;
        }

        /// <summary>그 토큰이 보드 위의 UB 토큰인가(MapAction Search의 Target 갈래).</summary>
        public static bool IsToken(GameState s, int token) =>
            token >= 0 && token < s.Tokens.Count && s.Tokens[token].Kind == TokenKind.UltraBeast && s.Tokens[token].Cell >= 0;

        /// <summary>총 피해 감소 = ⌊프리즘 아머 개수 / 2⌋(p.24).</summary>
        public static int Reduction(int prism) => prism < 0 ? 0 : prism / 2;

        /// <summary>그 배틀의 가상 쪽(UB·네크로즈마)을 지키는 프리즘 아머 개수. 그 밖의 배틀은 0.</summary>
        public static int Prism(GameState s, BattleState b)
        {
            if (s.Ub == null || b == null) return 0;
            return b.Kind == BattleKind.UltraBeast ? s.Ub.PrismUltraBeast : b.Kind == BattleKind.Necrozma ? s.Ub.PrismUltraBurst : 0;
        }

        /// <summary>
        /// 그 포켓몬이 지금 받을 피해 amount에서 프리즘 아머가 줄일 양(BattleFlow.ApplyBuffer — 적용 직전). 이번 배틀 턴에 남은 몫에서 쓴다(BattleState.PrismUsed).
        /// UB·네크로즈마 쪽(Owner = SeatId.UltraBeast) 포켓몬만 · 프리즘 아머를 무시하는 기술(multi_attack)의 피해는 0.
        /// </summary>
        internal static int Reduce(GameState s, Ctx ctx, BattleState b, int pokemon, int amount, SourceRef src)
        {
            if (amount <= 0 || s.Pokemon[pokemon].Owner != SeatId.UltraBeast) return 0;
            int left = Reduction(Prism(s, b)) - b.PrismUsed;
            if (left <= 0) return 0;
            if (src.Kind == SourceKind.Move && src.Id >= 0 && EffectRules.IgnoresPrism(ctx, src.Id)) return 0;
            int cut = Math.Min(left, amount);
            b.PrismUsed += cut;
            return cut;
        }

        /// <summary>
        /// UB를 쓰러뜨렸다(BattleFlow.Conclude): 그 UB 토큰을 맵에서 빼고(TokenRemoved) 격파 수를 올리고 프리즘 아머 1개를 울트라 버스트 쪽에서 울트라 비스트 쪽으로 옮긴다(PrismMoved —
        /// p.24 「When an Ultra Beast is defeated, move a Prism Armour token from the Ultra Burst side to the Ultra Beast side」 · 원작 커뮤니티 확인: 남은 UB가 세지고 네크로즈마가 약해진다).
        /// </summary>
        internal static void Defeated(GameState s, Emitter fx, int seat, int card)
        {
            foreach (var t in s.Tokens)
            {
                if (t.Kind != TokenKind.UltraBeast || t.Def != card || t.Cell < 0) continue;
                int cell = t.Cell;
                t.Cell = -1;
                if (fx.On) fx.Add(new TokenRemoved { Seat = seat, Cell = cell, TokenKind = TokenKind.UltraBeast });
                break;
            }
            var ub = s.Ub;
            if (ub == null) return;
            ub.Defeated++;
            if (ub.PrismUltraBurst <= 0) return;
            var before = new[] { ub.PrismUltraBurst, ub.PrismUltraBeast };
            ub.PrismUltraBurst--;
            ub.PrismUltraBeast++;
            if (fx.On) fx.Add(new PrismMoved { Seat = SeatId.System, Before = before, After = new[] { ub.PrismUltraBurst, ub.PrismUltraBeast } });
        }
    }
}
