using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 판 손보기 갈고리(M6.5 각본 튜토리얼 — 07 §6-1·§6-2). 규칙은 바꾸지 않는다: 섞기·굴림은 평소처럼 난수를 쓰고, 그 결과의 순서·면만 바꿀 기회를 준다.
    /// 평소 판에서는 <see cref="Rig.Current"/>가 null이라 아무 일도 없다. 상태 칸이 아니라 세이브·복제·해시·보기에 실리지 않는다.
    /// </summary>
    public interface IRig
    {
        /// <summary>Fisher–Yates를 마친 더미(<see cref="Piles.Shuffle"/>). 덱이 아닌 목록(칸 후보·방향)도 온다 — 손보는 쪽이 참조로 가린다.</summary>
        void Shuffled(List<int> pile);

        /// <summary>공격·포획 주사위를 굴린 면(<see cref="AttackDice.Roll"/> — 난수는 이미 썼다). 면만 바꿀 수 있다.</summary>
        void Rolled(AttackDieFace[] faces);
    }

    /// <summary>지금 걸린 손보기(스레드마다 하나). 세션이 엔진을 부르는 동안만 <see cref="Use"/>로 건다.</summary>
    public static class Rig
    {
        [ThreadStatic] static IRig current;

        public static IRig Current => current;

        /// <summary>rig를 건다(null이면 아무것도 안 건다). 돌려준 값을 Dispose하면 앞의 것으로 돌아간다.</summary>
        public static Scope Use(IRig rig)
        {
            var prev = current;
            current = rig;
            return new Scope(prev);
        }

        public readonly struct Scope : IDisposable
        {
            readonly IRig prev;

            internal Scope(IRig prev) { this.prev = prev; }

            public void Dispose() => current = prev;
        }
    }
}
