using System;
using System.Collections.Generic;

namespace Sinnoh.Data
{
    /// <summary>
    /// 룰 설명서 게임 중 옆 판(S43 C — 결정 111 ①)의 「지금 칸」 칩: 상황 이름 → 절 키 [가정 plan §5 #370].
    /// 상황 이름은 화면이 지금 판에서 고른다 — 맨 위 창(<c>screen.*</c>) · 내 칸의 고정물(<c>cell.&lt;고정물 종류 snake&gt;</c>) · 내 칸의 토큰 · 같은 칸의 다른 트레이너 · 캠페인 · 늘 붙는 <see cref="Turn"/>.
    /// 엔진 무관 표라 두 언어가 같다(절 키는 두 언어 공통 — 완료 조건 2). 표의 절 키가 설명서에 있는지는 시험이 본다(<c>M9_설명서칩_</c>).
    /// </summary>
    public static class RulebookChips
    {
        /// <summary>늘 붙는 상황(보드에서 아무 일도 없을 때 — 턴 행동 · 목표).</summary>
        public const string Turn = "turn";

        /// <summary>칩을 몇 개까지 보이나(옆 판 한 줄).</summary>
        public const int Max = 4;

        /// <summary>상황 → 절 키(앞이 먼저 — 상황을 앞에서부터 모아 겹친 키는 한 번만).</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Table = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // 맨 위 창
            ["screen.encounter"] = new[] { "pokemon.encounter", "pokemon.capture" },
            ["screen.battle"] = new[] { "battle.order", "battle.action", "battle.types", "battle.status" },
            ["screen.craft"] = new[] { "crafting.intro", "recipes.balls", "recipes.stews", "recipes.items" },
            ["screen.evolve"] = new[] { "pokemon.evolution" },
            ["screen.party"] = new[] { "pokemon.card", "crafting.items" },
            ["screen.stamp"] = new[] { "stamps.conditions", "stamps.rewards" },
            ["screen.fate"] = new[] { "shrines.fate", "shrines.fate_effects" },
            ["screen.shrine"] = new[] { "shrines.events", "shrines.blessings" },
            ["screen.trade"] = new[] { "overview.turn" },
            ["screen.challenge"] = new[] { "overview.turn", "battle.start" },
            ["screen.village"] = new[] { "overview.village", "automation.fly" },
            ["screen.breach"] = new[] { "ultra_burst.breach" },
            ["screen.bless"] = new[] { "ultra_burst.overview" },
            // 내 칸의 고정물(FixtureKind)
            ["cell.village"] = new[] { "overview.village", "automation.fly" },
            ["cell.apricorn_tree"] = new[] { "pokemon.encounter", "pokemon.apricorns", "pokemon.tiers" },
            ["cell.apricorn_reef"] = new[] { "pokemon.encounter", "pokemon.apricorns", "pokemon.tiers" },
            ["cell.distorted_tree"] = new[] { "ultra_burst.warped", "pokemon.encounter" },
            ["cell.legendary_site"] = new[] { "pokemon.legendary", "hidden_spots.artifacts" },
            ["cell.legendary_shrine"] = new[] { "shrines.legendary", "shrines.events" },
            ["cell.shrine"] = new[] { "shrines.events", "shrines.blessings" },
            ["cell.gate"] = new[] { "ultra_burst.gates" },
            ["cell.taxi"] = new[] { "automation.fly" },
            // 내 칸의 토큰 · 다른 트레이너
            ["cell.hidden_spot"] = new[] { "hidden_spots.intro" },
            ["cell.grunt"] = new[] { "heart.tactics", "battle.start" },
            ["cell.ultra_beast"] = new[] { "ultra_burst.battles", "ultra_burst.movement" },
            ["cell.rival"] = new[] { "overview.turn" },
            // 캠페인 · 늘
            ["campaign.ultra_burst"] = new[] { "ultra_burst.overview" },
            [Turn] = new[] { "overview.turn", "overview.goal" },
        };

        /// <summary>상황들(앞이 먼저) → 칩의 절 키(겹침 없이 <see cref="Max"/>개까지). 모르는 상황은 건너뛴다.</summary>
        public static List<string> For(IEnumerable<string> situations)
        {
            var keys = new List<string>();
            foreach (string s in situations)
            {
                if (s == null || !Table.TryGetValue(s, out var list)) continue;
                foreach (string k in list)
                {
                    if (keys.Count >= Max) return keys;
                    if (!keys.Contains(k)) keys.Add(k);
                }
            }
            return keys;
        }
    }
}
