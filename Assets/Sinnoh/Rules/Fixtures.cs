using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>맵 고정물 종류(fixtures.json kind, 03 §1-3). 174개 = 마을 9·나무 86·산호 32·왜곡된 나무 6·전설 장소 12·전설 사당 7·사당 13·게이트 8·택시 1.</summary>
    public enum FixtureKind { Village, ApricornTree, ApricornReef, DistortedTree, LegendarySite, LegendaryShrine, Shrine, Gate, Taxi }

    /// <summary>규토리 7색.</summary>
    public enum ApricornColor { Red, Blue, Yellow, Pink, Green, White, Black }

    /// <summary>
    /// 고정물 하나(fixtures.json 행). 세이브 월드 좌표에서 가장 가까운 칸에 붙였다(Tools/data/reports/fixtures-cells.tsv).
    /// 키는 "&lt;종류&gt;.&lt;영문 snake&gt;"(03 §1-2): 마을 village.stark, 나무 apricorn_tree.red_1(색마다 칸 순 번호), 산호 apricorn_reef.weak_1,
    /// 게이트는 본맵 쪽 _main·깨어진 세계 쪽 _distortion, 택시 taxi.bird(원작 config guids.BIRD).
    /// </summary>
    public sealed class FixtureDef
    {
        public readonly string Key;
        public readonly FixtureKind Kind;
        /// <summary>Board 칸 색인.</summary>
        public readonly int Cell;
        /// <summary>나무·산호의 조우 등급(Weak·Moderate·Strong). 다른 종류는 null.</summary>
        public readonly EncounterTier? Tier;
        /// <summary>나무·산호가 지키는 규토리 3개(rulings A8: 나무 = 제 색 3, 산호 = 등급별 그림). 다른 종류는 빈 목록.</summary>
        public readonly IReadOnlyList<ApricornColor> Apricorns;
        /// <summary>전설 장소·전설 사당에 나오는 전설(포켓몬 색인). 다른 종류는 빈 목록.</summary>
        public readonly IReadOnlyList<int> Legendary;
        /// <summary>전설 장소에 들어가는 데 필요한 유물(아이템 색인, 여럿이면 그중 하나). 다른 종류는 빈 목록.</summary>
        public readonly IReadOnlyList<int> Artifact;
        /// <summary>마을의 고향 마을 카드에 그려진 스타터 두 마리(포켓몬 색인, p.3 셋업 2-a). 다른 종류는 빈 목록.</summary>
        public readonly IReadOnlyList<int> Starters;
        /// <summary>게이트 짝(고정물 색인). 게이트가 아니면 NoPair.</summary>
        public readonly int Pair;

        public const int NoPair = -1;

        public FixtureDef(string key, FixtureKind kind, int cell, EncounterTier? tier, IReadOnlyList<ApricornColor> apricorns,
            IReadOnlyList<int> legendary, IReadOnlyList<int> artifact, IReadOnlyList<int> starters, int pair)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Apricorns = apricorns ?? throw new ArgumentNullException(nameof(apricorns));
            Legendary = legendary ?? throw new ArgumentNullException(nameof(legendary));
            Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
            Starters = starters ?? throw new ArgumentNullException(nameof(starters));
            bool tree = kind == FixtureKind.ApricornTree || kind == FixtureKind.ApricornReef;
            if (tree != tier.HasValue) throw new ArgumentException($"{key}: 조우 등급은 나무·산호에만 있다", nameof(tier));
            if (tier.HasValue && tier.Value != EncounterTier.Weak && tier.Value != EncounterTier.Moderate && tier.Value != EncounterTier.Strong)
                throw new ArgumentException($"{key}: 나무·산호 등급 {tier}", nameof(tier));
            if (apricorns.Count != (tree ? 3 : 0)) throw new ArgumentException($"{key}: 규토리 {apricorns.Count}개", nameof(apricorns));
            bool legend = kind == FixtureKind.LegendarySite || kind == FixtureKind.LegendaryShrine;
            if (legend != (legendary.Count > 0)) throw new ArgumentException($"{key}: 전설은 전설 장소·전설 사당에만(하나 이상) 있다", nameof(legendary));
            if (artifact.Count > 0 && kind != FixtureKind.LegendarySite) throw new ArgumentException($"{key}: 유물 조건은 전설 장소에만 있다", nameof(artifact));
            if (starters.Count != (kind == FixtureKind.Village ? 2 : 0)) throw new ArgumentException($"{key}: 스타터 {starters.Count}마리(마을만 2)", nameof(starters));
            if ((kind == FixtureKind.Gate) != (pair != NoPair)) throw new ArgumentException($"{key}: 짝은 게이트에만 있다", nameof(pair));
            Kind = kind;
            Cell = cell;
            Tier = tier;
            Pair = pair;
        }
    }

    /// <summary>고정물 표(정적 데이터). 읽기는 Sinnoh.Data FixtureDbLoader. 게이트 짝이 서로를 가리키는지 확인한다.</summary>
    public sealed class FixtureDb
    {
        public readonly IReadOnlyList<FixtureDef> Fixtures;
        public readonly IdTable Ids;

        public FixtureDb(IReadOnlyList<FixtureDef> fixtures)
        {
            if (fixtures == null) throw new ArgumentNullException(nameof(fixtures));
            var a = new FixtureDef[fixtures.Count];
            var keys = new string[fixtures.Count];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = fixtures[i] ?? throw new ArgumentException($"고정물 행 {i}이 null이다", nameof(fixtures));
                keys[i] = a[i].Key;
            }
            for (int i = 0; i < a.Length; i++)
            {
                int p = a[i].Pair;
                if (p == FixtureDef.NoPair) continue;
                if ((uint)p >= (uint)a.Length || p == i || a[p].Pair != i || a[p].Kind != FixtureKind.Gate)
                    throw new ArgumentException($"{a[i].Key}: 게이트 짝 {p}이 서로를 가리키지 않는다");
            }
            Fixtures = a;
            Ids = new IdTable("fixtures", keys);
        }
    }
}
