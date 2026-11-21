using System;
using Sinnoh.Rules;

namespace Sinnoh.Ai
{
    /// <summary>
    /// AI 자기 난수(01 §9-1·§9-3, M8): 표본을 뽑고(<see cref="Determinizer"/>) 채운 판의 게임 난수를 새로 심는다(<see cref="Seed"/>).
    /// 씨는 AI 예산의 <see cref="Sinnoh.Rules.Session.AgentBudget.Seed"/>(방 값 + 선택 Id)에서만 온다 — 게임 난수(보기에서는 0으로 가려진다)와 이어지지 않는다.
    /// 같은 씨·같은 표본 번호면 같은 수열이다(결정성).
    /// </summary>
    public sealed class SampledRng : IRng
    {
        readonly Pcg32 rng;

        public SampledRng(ulong seed, ulong stream = 0)
        {
            rng = new Pcg32(Mix(seed), stream);
        }

        /// <summary>표본 번호마다 갈린 난수(같은 AI 씨의 표본 0, 1, 2 …는 서로 다른 수열).</summary>
        public static SampledRng ForSample(ulong aiSeed, int sample)
        {
            if (sample < 0) throw new ArgumentOutOfRangeException(nameof(sample), sample, "표본 번호는 0 이상");
            return new SampledRng(unchecked(aiSeed + 0x9E3779B97F4A7C15UL * (ulong)(sample + 1)), (ulong)sample);
        }

        public int Next(int maxExclusive) => rng.Next(maxExclusive);

        public AttackDieFace RollAttackDie() => rng.RollAttackDie();

        public ulong NextULong() => ((ulong)rng.NextUInt() << 32) | rng.NextUInt();

        /// <summary>채운 판의 게임 난수를 이 난수에서 뽑은 씨로 새로 심는다 — 굴리기의 주사위·섞기가 표본마다 다르고, 같은 표본에서는 같다.</summary>
        public void Seed(GameState sim)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            ulong seed = NextULong();
            sim.Rng.CopyFrom(new Pcg32(seed, NextULong() >> 1));
        }

        /// <summary>SplitMix64 마무리 섞기(이웃한 씨가 이웃한 수열을 내지 않게).</summary>
        public static ulong Mix(ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }
    }
}
