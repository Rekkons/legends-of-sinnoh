using System;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 울트라 버스트 UB 턴의 수식(automation R4·R6·R7·R8, rulings B4). Lua `raw/lua/d66d964ab6.lua`를 그대로 옮긴다(01 §8-2).
    /// 이동 점수표·동점·멈춤(R4 나머지, R5)은 M6.
    /// </summary>
    public static class UltraBurstRules
    {
        /// <summary>균열 강도 분모 = 사당 13 + 규토리 나무·산호 118(세이브 집계, R7). Lua는 토큰 수를 그때그때 센다.</summary>
        public const int BreachTargets = 131;

        /// <summary>이 균열 강도 이상이면 패배(R8, Lua BREACH_STRENGTH_LIMIT).</summary>
        public const int LossThreshold = 70;

        /// <summary>이번 UB 턴 총 이동 칸 수 = 4 + floor(균열/10)(R4, Lua move_ultra_beasts). 균열은 턴 시작 값.</summary>
        public static int TotalMoves(int breachStrength) => 4 + breachStrength / 10;

        /// <summary>네크로즈마가 파괴하는 나무·사당 수 = ceil(인원/2)(R6, Lua ultra_necrozma_attacks). 남은 대상이 더 적으면 고르는 쪽이 남은 만큼만 파괴한다.</summary>
        public static int NecrozmaDestroyCount(int seatCount)
        {
            if (seatCount < 1 || seatCount > 6) throw new ArgumentOutOfRangeException(nameof(seatCount), seatCount, "인원은 1~6");
            return (seatCount + 1) / 2;
        }

        /// <summary>
        /// 균열 강도(%) = round(100 × 파괴 수 / 대상 수)(R7). Lua round(x) = floor(x + 0.5)를 정수식
        /// floor((200·파괴 + 대상) / (2·대상))로 옮겼다.
        /// </summary>
        public static int BreachStrength(int destroyed, int targets = BreachTargets)
        {
            if (targets < 1) throw new ArgumentOutOfRangeException(nameof(targets), targets, "1 이상");
            if (destroyed < 0 || destroyed > targets) throw new ArgumentOutOfRangeException(nameof(destroyed), destroyed, $"0~{targets}");
            return (200 * destroyed + targets) / (2 * targets);
        }

        /// <summary>균열이 70% 이상이면 패배(R8).</summary>
        public static bool IsLost(int breachStrength) => breachStrength >= LossThreshold;

        /// <summary>
        /// UB 턴 끝에 좌석마다 받는 왜곡 균열 카드 수. 턴 시작보다 넘은 10% 구간 수(B4 [가정], 룰북 p.24 「At every increment of 10%」),
        /// OncePerTurn이면 넘었을 때 1장(Lua handle_breach_cards). 패배(70% 이상)면 Lua처럼 주지 않는다.
        /// </summary>
        public static int BreachCardsPerSeat(int breachBefore, int breachAfter, BreachCardsPerBracket mode)
        {
            if (breachAfter < breachBefore) throw new ArgumentOutOfRangeException(nameof(breachAfter), breachAfter, "균열은 줄지 않는다");
            if (IsLost(breachAfter)) return 0;
            int crossed = breachAfter / 10 - breachBefore / 10;
            return mode == BreachCardsPerBracket.OncePerTurn && crossed > 1 ? 1 : crossed;
        }
    }
}
