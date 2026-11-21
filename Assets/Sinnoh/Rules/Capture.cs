using System;

namespace Sinnoh.Rules
{
    /// <summary>볼 등급(00 §5 용어 22). 굴릴 타입없음 공격 주사위 수 = 3 + 값(A7: 3 / 4 / 5 / 6).</summary>
    public enum Ball
    {
        Regular,
        Greater,
        Ultimate,
        Keystone,
    }

    /// <summary>
    /// 포획 판정(A7, rules-summary §8). 야생 배틀 내 차례에 공격 대신 볼 등급만큼 타입없음 공격 주사위를 굴려
    /// 눈 합(상태이상 심볼 0 / 일반 타격 1 / 크리티컬 히트 2)이 포획률 이상이면 성공. 포획 굴림의 피로는 FatigueRules(A10).
    /// </summary>
    public static class CaptureRules
    {
        /// <summary>유물 없이 시작의 사당에서 만난 신오의 신(천상의 오라)의 포획률(S5_6, 룰북 p.18 「roll of 7」).</summary>
        public const int CelestialAuraRate = 7;

        public static int Dice(Ball ball) => 3 + (int)ball;

        /// <summary>천상의 오라면 7, 아니면 카드의 포획률(약한 3 · 적당한 4 · 강한 5 · 전설 6, S5_2).</summary>
        public static int Rate(int cardRate, bool celestialAura) => celestialAura ? CelestialAuraRate : cardRate;

        /// <summary>정확도(A7): 대상이 상태이상이면 상태이상 심볼 1개를 일반 타격 1개로 바꾼 새 배열. 아니면 복사본.</summary>
        public static AttackDieFace[] ApplyAccuracy(AttackDieFace[] faces, bool targetHasStatus)
        {
            if (faces == null) throw new ArgumentNullException(nameof(faces));
            var result = (AttackDieFace[])faces.Clone();
            if (!targetHasStatus) return result;
            int i = Array.IndexOf(result, AttackDieFace.StatusSymbol);
            if (i >= 0) result[i] = AttackDieFace.RegularHit;
            return result;
        }

        /// <summary>눈 합 ≥ 포획률. faces는 ApplyAccuracy를 거친 면이다.</summary>
        public static bool Succeeds(AttackDieFace[] faces, int rate)
        {
            if (faces == null) throw new ArgumentNullException(nameof(faces));
            int sum = 0;
            foreach (var f in faces) sum += (int)f;
            return sum >= rate;
        }

        /// <summary>6^dice가지 중 성공하는 경우 수(01 §6-3). 상태이상이면 심볼이 있는 묶음의 합이 1 늘어난다.</summary>
        public static int SuccessWeight(int dice, int rate, bool targetHasStatus)
        {
            int w = 0;
            foreach (var o in AttackDice.Outcomes(dice))
            {
                int sum = o.Sum + (targetHasStatus && o.StatusSymbols > 0 ? 1 : 0);
                if (sum >= rate) w += o.Weight;
            }
            return w;
        }
    }
}
