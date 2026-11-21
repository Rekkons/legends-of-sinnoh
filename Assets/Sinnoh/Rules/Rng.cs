using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 규칙의 모든 우연(섞기·뽑기·주사위)이 거치는 난수(01 §0·§9-3). 게임은 시드 있는 Pcg32를 쓰고,
    /// 시험은 ScriptedRng, AI는 표본·강제 주사위 구현을 바꿔 끼운다.
    /// </summary>
    public interface IRng
    {
        /// <summary>[0, maxExclusive) 균등. maxExclusive ≥ 1.</summary>
        int Next(int maxExclusive);

        /// <summary>공격 주사위 한 번(R9: 면 6개 = 상태이상 심볼 1·일반 타격 4·크리티컬 히트 1).</summary>
        AttackDieFace RollAttackDie();
    }

    /// <summary>
    /// PCG32(XSH RR 64/32, O'Neill pcg32_random_r·pcg32_srandom_r·pcg32_boundedrand_r와 같은 수열).
    /// 정수 연산만 써서 dotnet과 Unity(Mono·IL2CPP·WebGL)가 같은 수열을 낸다. 상태는 public 필드라 세이브·복제가 된다(01 §2-2 Rng).
    /// </summary>
    public sealed class Pcg32 : IRng
    {
        const ulong Multiplier = 6364136223846793005UL;

        public ulong State;

        /// <summary>수열(stream) 선택값. 늘 홀수.</summary>
        public ulong Increment;

        public Pcg32(ulong seed, ulong stream = 0)
        {
            State = 0;
            Increment = unchecked((stream << 1) | 1UL);
            NextUInt();
            State = unchecked(State + seed);
            NextUInt();
        }

        public void CopyFrom(Pcg32 other)
        {
            State = other.State;
            Increment = other.Increment;
        }

        public uint NextUInt()
        {
            unchecked
            {
                ulong old = State;
                State = old * Multiplier + Increment;
                uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
                int rot = (int)(old >> 59);
                return (xorShifted >> rot) | (xorShifted << (-rot & 31));
            }
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive < 1) throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "1 이상이어야 한다");
            uint bound = (uint)maxExclusive;
            uint threshold = unchecked(0u - bound) % bound; // 2^32 mod bound보다 작은 값을 버려 치우침을 없앤다
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return (int)(r % bound);
            }
        }

        public AttackDieFace RollAttackDie() => AttackDice.FaceOf(Next(AttackDice.Faces));
    }

    /// <summary>
    /// 시험용(01 §12): 정해 둔 값을 차례로 낸다. Next 값과 주사위 면은 따로 줄 선다.
    /// 대본이 모자라거나 값이 범위 밖이면 InvalidOperationException — 시험이 예상보다 많이 굴린 것을 잡는다.
    /// </summary>
    public sealed class ScriptedRng : IRng
    {
        readonly Queue<int> ints;
        readonly Queue<AttackDieFace> dice;

        public ScriptedRng(IEnumerable<int> ints, IEnumerable<AttackDieFace> dice)
        {
            this.ints = new Queue<int>(ints ?? Array.Empty<int>());
            this.dice = new Queue<AttackDieFace>(dice ?? Array.Empty<AttackDieFace>());
        }

        public static ScriptedRng Dice(params AttackDieFace[] faces) => new ScriptedRng(null, faces);
        public static ScriptedRng Ints(params int[] values) => new ScriptedRng(values, null);

        /// <summary>대본을 다 썼는가.</summary>
        public bool Exhausted => ints.Count == 0 && dice.Count == 0;

        public int Next(int maxExclusive)
        {
            if (maxExclusive < 1) throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "1 이상이어야 한다");
            if (ints.Count == 0) throw new InvalidOperationException("ScriptedRng: Next 대본이 모자라다");
            int v = ints.Dequeue();
            if (v < 0 || v >= maxExclusive) throw new InvalidOperationException($"ScriptedRng: 대본 값 {v}이 [0, {maxExclusive}) 밖이다");
            return v;
        }

        public AttackDieFace RollAttackDie()
        {
            if (dice.Count == 0) throw new InvalidOperationException("ScriptedRng: 주사위 대본이 모자라다");
            return dice.Dequeue();
        }
    }
}
