using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 공격 주사위 면(00 §5 용어 30). 값 = 눈(A7: 상태 0, 일반 1, 크리 2).
    /// None = 면 없음(−1): 효과가 지운 주사위(마비 「remove all regular hits」)의 자리와 더한 주사위의 앞 면. 굴림에서는 나오지 않고 합·개수에 들지 않는다(G14, M3-f).
    /// </summary>
    public enum AttackDieFace
    {
        None = -1,
        StatusSymbol = 0,
        RegularHit = 1,
        CriticalHit = 2,
    }

    /// <summary>
    /// n개를 굴린 결과의 (s, r, c) = (상태이상 심볼, 일반 타격, 크리티컬 히트) 개수 묶음(01 §6-3).
    /// Weight = 6^n가지 중 이 묶음이 나오는 경우 수 = n!/(s!·r!·c!) × 4^r.
    /// </summary>
    public readonly struct DiceOutcome
    {
        public readonly int StatusSymbols;
        public readonly int RegularHits;
        public readonly int CriticalHits;
        public readonly int Weight;

        public DiceOutcome(int statusSymbols, int regularHits, int criticalHits, int weight)
        {
            StatusSymbols = statusSymbols;
            RegularHits = regularHits;
            CriticalHits = criticalHits;
            Weight = weight;
        }

        public int Dice => StatusSymbols + RegularHits + CriticalHits;

        /// <summary>눈 합 r + 2c.</summary>
        public int Sum => RegularHits + 2 * CriticalHits;

        public override string ToString() => $"(s{StatusSymbols}, r{RegularHits}, c{CriticalHits}) ×{Weight}";
    }

    /// <summary>
    /// 공격 주사위(R9 면 구성, R10 1~9개)와 정확한 확률 모델(01 §6-3). 모두 int로 센다: 6^9 = 10,077,696.
    /// 포획 굴림의 타입없음 주사위도 같은 주사위다(A7).
    /// </summary>
    public static class AttackDice
    {
        /// <summary>면 수. 면 색인 0 = 상태이상 심볼, 1~4 = 일반 타격, 5 = 크리티컬 히트(R9).</summary>
        public const int Faces = 6;

        /// <summary>한 번에 굴리는 주사위 수의 범위(R10, FAQ 최대 공격력 9).</summary>
        public const int MinDice = 1;
        public const int MaxDice = 9;

        public static AttackDieFace FaceOf(int faceIndex)
        {
            if (faceIndex < 0 || faceIndex >= Faces) throw new ArgumentOutOfRangeException(nameof(faceIndex), faceIndex, "면 색인은 0~5");
            return faceIndex == 0 ? AttackDieFace.StatusSymbol
                : faceIndex == Faces - 1 ? AttackDieFace.CriticalHit
                : AttackDieFace.RegularHit;
        }

        /// <summary>실제 굴림: rng.RollAttackDie()를 dice번. 면 순서 그대로 돌려준다(이벤트에 싣는다, 01 §6-3).</summary>
        public static AttackDieFace[] Roll(IRng rng, int dice)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            CheckDice(dice);
            var faces = new AttackDieFace[dice];
            for (int i = 0; i < dice; i++) faces[i] = rng.RollAttackDie();
            Rig.Current?.Rolled(faces); // 튜토리얼 주사위 대본(07 §6-2) — 난수는 평소대로 쓰고 면만 바꾼다. 평소 null
            return faces;
        }

        /// <summary>분모 6^dice.</summary>
        public static int TotalWeight(int dice)
        {
            CheckDice(dice);
            int w = 1;
            for (int i = 0; i < dice; i++) w *= Faces;
            return w;
        }

        /// <summary>
        /// 묶음 전부((dice+1)(dice+2)/2개). 순서: 크리 수 오름차순, 그 안에서 상태 심볼 수 내림차순.
        /// 면 변환(졸림·마비·아이템 등)과 효과 조건은 이 묶음 위에서 계산한다.
        /// </summary>
        public static DiceOutcome[] Outcomes(int dice)
        {
            CheckDice(dice);
            var list = new DiceOutcome[(dice + 1) * (dice + 2) / 2];
            int k = 0;
            for (int c = 0; c <= dice; c++)
                for (int s = dice - c; s >= 0; s--)
                {
                    int r = dice - c - s;
                    int fours = 1;
                    for (int i = 0; i < r; i++) fours *= 4;
                    list[k++] = new DiceOutcome(s, r, c, Choose(dice, c) * Choose(dice - c, s) * fours);
                }
            return list;
        }

        /// <summary>눈 합 0~2·dice의 경우 수(길이 2·dice + 1). (1 + 4x + x²)^dice의 계수와 같다.</summary>
        public static int[] SumWeights(int dice)
        {
            var outcomes = Outcomes(dice); // 범위 확인도 여기서
            var w = new int[2 * dice + 1];
            foreach (var o in outcomes) w[o.Sum] += o.Weight;
            return w;
        }

        /// <summary>눈 합(None은 뺀다). 공격 피해 ①·포획 판정이 쓴다.</summary>
        public static int Sum(IReadOnlyList<AttackDieFace> faces)
        {
            int n = 0;
            foreach (var f in faces)
                if (f != AttackDieFace.None) n += (int)f;
            return n;
        }

        /// <summary>그 면의 개수(효과 조건 「심볼이 나오면」·「크리티컬마다」, 01 §6-3 (s, r, c)).</summary>
        public static int Count(IReadOnlyList<AttackDieFace> faces, AttackDieFace face)
        {
            int n = 0;
            foreach (var f in faces)
                if (f == face) n++;
            return n;
        }

        /// <summary>(s, r, c) 개수 3칸(DiceFacesChanged.SrcBefore·SrcAfter).</summary>
        public static int[] Src(IReadOnlyList<AttackDieFace> faces) =>
            new[] { Count(faces, AttackDieFace.StatusSymbol), Count(faces, AttackDieFace.RegularHit), Count(faces, AttackDieFace.CriticalHit) };

        static void CheckDice(int dice)
        {
            if (dice < MinDice || dice > MaxDice)
                throw new ArgumentOutOfRangeException(nameof(dice), dice, $"공격 주사위는 {MinDice}~{MaxDice}개(R10)");
        }

        static int Choose(int n, int k)
        {
            int r = 1;
            for (int i = 1; i <= k; i++) r = r * (n - k + i) / i;
            return r;
        }
    }
}
