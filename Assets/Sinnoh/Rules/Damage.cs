namespace Sinnoh.Rules
{
    /// <summary>
    /// 공격 한 번의 피해 계산 입력(01 §6-4). 굴림·면 변환·효과 처리가 끝난 값을 채운다.
    /// 같은 단계에 효과가 여럿 걸리면 부르는 쪽이 합친다: 감소는 더하고, 문턱은 큰 값, 상한은 작은 값.
    /// </summary>
    public sealed class DamageInput
    {
        /// <summary>① 면 변환 뒤 주사위 눈 합(상태이상 심볼 0 / 일반 타격 1 / 크리티컬 히트 2).</summary>
        public int DiceSum;

        /// <summary>② 기술·아이템 효과의 증감(리프블레이드 「크리티컬 히트는 +1」 등). 음수도 된다.</summary>
        public int EffectDamage;

        /// <summary>③ 기술 타입. Typeless면 상성 0(S7_4).</summary>
        public PokemonType MoveType;

        /// <summary>③ 방어 포켓몬 타입. 단일 타입이면 DefenderType2 = Typeless.</summary>
        public PokemonType DefenderType1;
        public PokemonType DefenderType2;

        /// <summary>③ 「상성 +1」 류 카드 보정(연구원 약점 분석 등). 붙는 조건은 효과 쪽이 TypeChart로 판단해 넣는다(G4).</summary>
        public int EffectivenessBonus;

        /// <summary>④ 2배 기술.</summary>
        public bool Double;

        /// <summary>⑤ 총 데미지 감소 합(화상 2, 별 방패 2, 프리즘 아머 floor(n/2) 턴당 1번). 0 이상.</summary>
        public int Reduction;

        /// <summary>⑥ 문턱: 이 값 이하면 0(「2 이하 무효」 = 2). 0 = 문턱 없음.</summary>
        public int NullifyAtOrBelow;

        /// <summary>⑦ 상한(3·6). 0 = 상한 없음.</summary>
        public int Cap;
    }

    /// <summary>피해 계산(01 §6-4, rulings G4 [가정], rules-summary §7.3).</summary>
    public static class DamageRules
    {
        /// <summary>굴릴 공격력 = clamp(기술 공격력 + 보정, 1, 9)(S7_3, R10).</summary>
        public static int AttackStrength(int moveStrength, int modifier)
        {
            int s = moveStrength + modifier;
            return s < AttackDice.MinDice ? AttackDice.MinDice : s > AttackDice.MaxDice ? AttackDice.MaxDice : s;
        }

        /// <summary>
        /// ① 주사위 합 + ② 효과 + ③ 상성 → ④ × 2 → ⑤ − 총 데미지 감소 → ⑥ 문턱 → ⑦ 상한 → 0 미만은 0.
        /// capStage = BeforeReduction이면 상한을 ④ 뒤·⑤ 전에 건다(G4 옵션).
        /// </summary>
        public static int Calculate(DamageInput d, DamageCapStage capStage)
        {
            int damage = d.DiceSum + d.EffectDamage
                + TypeChart.Bonus(d.MoveType, d.DefenderType1, d.DefenderType2) + d.EffectivenessBonus;
            if (d.Double) damage *= 2;
            if (capStage == DamageCapStage.BeforeReduction) damage = ApplyCap(damage, d.Cap);
            damage -= d.Reduction;
            if (damage <= d.NullifyAtOrBelow) damage = 0; // 문턱 0이면 0 미만 → 0과 같다
            if (capStage == DamageCapStage.Last) damage = ApplyCap(damage, d.Cap);
            return damage < 0 ? 0 : damage;
        }

        static int ApplyCap(int damage, int cap) => cap > 0 && damage > cap ? cap : damage;
    }
}
