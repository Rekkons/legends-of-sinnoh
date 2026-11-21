using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Ai
{
    /// <summary>
    /// 공격 주사위 정해 넣기(01 §9-3 「ForcedDice(§6-3 묶음을 정해 넣어 기대값 계산)」, M8 「탐색」): 판 손보기 갈고리(<see cref="IRig"/>)로 굴린 면을 바꾼다 — 규칙은 그대로 돌고 난수도 평소대로 쓴다(면만 바꾼다).
    /// 넣을 묶음(<see cref="Push"/>)이 있으면 다음 굴림을 그 묶음(s, r, c)의 정해진 꼴(<see cref="Apply"/>)로, 없으면 <see cref="Free"/> 난수로 면을 새로 굴린다(null이면 그대로).
    /// 섞기는 손대지 않는다. 탐색(<see cref="StrongAgent"/>)은 자기 트리가 이 갈고리를 직접 쓴다 — 이 클래스는 시험 · 1수 셈이 쓰는 꼴이다.
    /// </summary>
    public sealed class ForcedDice : IRig
    {
        readonly Queue<DiceOutcome> queue = new Queue<DiceOutcome>();

        /// <summary>넣을 묶음이 없을 때 면을 새로 굴릴 난수(null = 굴린 면 그대로).</summary>
        public IRng Free;
        /// <summary>지난 굴림 수 · 정해 넣은 굴림 수 · 첫 굴림 · 마지막 굴림의 주사위 수.</summary>
        public int Rolls, Forced, FirstDice, LastDice;
        /// <summary>넣을 묶음의 주사위 수가 굴림과 달라 넣지 못한 수(있으면 부르는 쪽 셈이 틀렸다).</summary>
        public int Mismatch;

        /// <summary>다음 굴림에 넣을 묶음(차례대로).</summary>
        public void Push(DiceOutcome o) => queue.Enqueue(o);

        public void Clear()
        {
            queue.Clear();
            Rolls = Forced = FirstDice = LastDice = Mismatch = 0;
        }

        public void Shuffled(List<int> pile) { }

        public void Rolled(AttackDieFace[] faces)
        {
            if (Rolls++ == 0) FirstDice = faces.Length;
            LastDice = faces.Length;
            if (queue.Count > 0)
            {
                var o = queue.Dequeue();
                if (o.Dice == faces.Length)
                {
                    Apply(faces, o);
                    Forced++;
                    return;
                }
                Mismatch++;
            }
            if (Free != null)
                for (int i = 0; i < faces.Length; i++) faces[i] = Free.RollAttackDie();
        }

        /// <summary>묶음(s, r, c)의 정해진 꼴: 왼쪽부터 상태 심볼 s · 일반 r · 크리티컬 c [가정 01 §13-1 M8-18]. 면 수는 굴림과 같아야 한다.</summary>
        public static void Apply(AttackDieFace[] faces, DiceOutcome o)
        {
            int i = 0;
            for (int k = 0; k < o.StatusSymbols; k++) faces[i++] = AttackDieFace.StatusSymbol;
            for (int k = 0; k < o.RegularHits; k++) faces[i++] = AttackDieFace.RegularHit;
            for (int k = 0; k < o.CriticalHits; k++) faces[i++] = AttackDieFace.CriticalHit;
        }

        static readonly DiceOutcome[][] outcomes = new DiceOutcome[AttackDice.MaxDice + 1][];

        /// <summary>주사위 n개의 묶음 표(<see cref="AttackDice.Outcomes"/> — 한 번 만들어 돌려 쓴다, 읽기만).</summary>
        public static DiceOutcome[] Outcomes(int dice)
        {
            if (dice < AttackDice.MinDice || dice > AttackDice.MaxDice) throw new ArgumentOutOfRangeException(nameof(dice), dice, "주사위 수는 1~9");
            var o = outcomes[dice];
            if (o == null) outcomes[dice] = o = AttackDice.Outcomes(dice);
            return o;
        }
    }
}
