using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Client
{
    /// <summary>포켓몬 도감의 덱 거르기(04 §3-14, 결정 105 ③): 원작의 포켓몬 덱 — 갤럭시단 덱 셋(조무래기 · 간부 · 보스)은 하나로 묶는다 [가정].</summary>
    public enum DexDeck { Starter, Weak, Moderate, Strong, Legendary, Evolution, Galactic }

    /// <summary>도감의 카드 한 줄: 포켓몬 표 색인 · 장수(덱 하나를 보면 그 덱의 장수, 전체를 보면 모든 덱의 합).</summary>
    public readonly struct DexEntry
    {
        public readonly int Def, Copies;

        public DexEntry(int def, int copies)
        {
            Def = def;
            Copies = copies;
        }
    }

    /// <summary>
    /// 포켓몬 도감이 보이는 것(결정 105 ③): <b>덱 구성</b> — 덱마다 원래 든 카드와 장수.
    /// 읽는 것은 정적 데이터(<see cref="Ctx"/>의 카드 표 · 덱 표)뿐이다 — 판의 상태(<see cref="GameState"/>)를 받지 않으므로 지금 덱에 남은 카드 · 순서 · 누가 뽑았는지는 알 길이 없다.
    /// </summary>
    public static class DexBook
    {
        /// <summary>덱 거르기 순서.</summary>
        public static readonly DexDeck[] Tabs = { DexDeck.Starter, DexDeck.Weak, DexDeck.Moderate, DexDeck.Strong, DexDeck.Legendary, DexDeck.Evolution, DexDeck.Galactic };

        /// <summary>덱의 이름 마디(요소 이름 · 문자열 키 <c>screen.dex.deck.&lt;마디&gt;</c>). 전체(null)는 <c>all</c>.</summary>
        public static string Id(DexDeck? deck) => deck.HasValue ? deck.Value.ToString().ToLowerInvariant() : "all";

        /// <summary>그 덱 거르기가 읽는 덱 표의 키(decks.json).</summary>
        public static string[] DeckKeys(DexDeck deck)
        {
            switch (deck)
            {
                case DexDeck.Starter: return new[] { "deck.starter" };
                case DexDeck.Weak: return new[] { "deck.weak" };
                case DexDeck.Moderate: return new[] { "deck.moderate" };
                case DexDeck.Strong: return new[] { "deck.strong" };
                case DexDeck.Legendary: return new[] { "deck.legendary" };
                case DexDeck.Evolution: return new[] { "deck.evolution" };
                default: return new[] { "deck.galactic_grunt", "deck.galactic_commander", "deck.galactic_boss" };
            }
        }

        /// <summary>
        /// 그 덱의 카드(null = 모든 덱 — 같은 카드는 한 줄, 장수는 합). 순서는 전국도감 번호 → 면(상태) → 카드 키(같은 종의 갤럭시단 카드는 야생 카드 뒤).
        /// </summary>
        public static List<DexEntry> Cards(Ctx ctx, DexDeck? deck)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var copies = new Dictionary<int, int>();
            foreach (var d in Tabs)
            {
                if (deck.HasValue && deck.Value != d) continue;
                foreach (string key in DeckKeys(d))
                    foreach (var e in ctx.Decks.Decks[ctx.Decks.DeckIds.IndexOf(key)].Cards)
                    {
                        copies.TryGetValue(e.Card, out int n);
                        copies[e.Card] = n + e.Copies;
                    }
            }
            var list = new List<DexEntry>();
            foreach (var kv in copies) list.Add(new DexEntry(kv.Key, kv.Value));
            list.Sort((a, b) =>
            {
                var x = ctx.Cards.Pokemon[a.Def];
                var y = ctx.Cards.Pokemon[b.Def];
                if (x.Dex != y.Dex) return x.Dex.CompareTo(y.Dex);
                if (x.State != y.State) return x.State.CompareTo(y.State);
                return string.CompareOrdinal(x.Key, y.Key);
            });
            return list;
        }

        /// <summary>그 카드가 든 덱과 장수(덱 거르기 순서 — 든 덱이 없으면 빈 목록: 카드의 다른 면).</summary>
        public static List<KeyValuePair<DexDeck, int>> Decks(Ctx ctx, int def)
        {
            var list = new List<KeyValuePair<DexDeck, int>>();
            foreach (var d in Tabs)
            {
                int n = 0;
                foreach (string key in DeckKeys(d))
                    foreach (var e in ctx.Decks.Decks[ctx.Decks.DeckIds.IndexOf(key)].Cards)
                        if (e.Card == def) n += e.Copies;
                if (n > 0) list.Add(new KeyValuePair<DexDeck, int>(d, n));
            }
            return list;
        }

        /// <summary>장수 합.</summary>
        public static int Count(IReadOnlyList<DexEntry> cards)
        {
            int n = 0;
            foreach (var e in cards) n += e.Copies;
            return n;
        }

        /// <summary>거르기: 타입(null = 전체 — 카드의 타입 둘 가운데 하나) · 이름(빈 글 = 전체 — 화면 이름이나 영문 이름의 한 부분, 대소문자 무시).</summary>
        public static List<DexEntry> Filter(Ctx ctx, List<DexEntry> cards, PokemonType? type, string name, Func<string, string> text)
        {
            var list = new List<DexEntry>();
            string want = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            foreach (var e in cards)
                if (Matches(ctx, e.Def, type, want, text)) list.Add(e);
            return list;
        }

        /// <summary>카드 한 장이 거르기에 맞나(<see cref="Filter"/>와 같은 규칙 — 사당 판의 야생 카드 찾기도 쓴다, M9 다듬기 P22).</summary>
        public static bool Matches(Ctx ctx, int index, PokemonType? type, string name, Func<string, string> text)
        {
            var def = ctx.Cards.Pokemon[index];
            if (type.HasValue && !Has(def, type.Value)) return false;
            string want = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            return want == null || def.Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0 || (text != null && text("pokemon." + def.Key).IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static bool Has(PokemonDef def, PokemonType type)
        {
            foreach (var t in def.Types) if (t == type) return true;
            return false;
        }

        /// <summary>카드의 포획률(등급별 — <see cref="BattleRules.CatchRate"/>). 잡을 수 없는 카드(갤럭시단 · 울트라 비스트)는 0.</summary>
        public static int CatchRate(Ctx ctx, int def) => ctx.Cards.Pokemon[def].Uncapturable ? 0 : BattleRules.CatchRate(MapActions.GradeOf(ctx, def), false);

        /// <summary>같은 카드의 면들(폼 — 같은 도감 번호 · 같은 주인, 상태 순). 면이 하나뿐이면 빈 목록.</summary>
        public static List<int> Faces(Ctx ctx, int def)
        {
            var me = ctx.Cards.Pokemon[def];
            var list = new List<int>();
            if (me.State == 0) return list;
            for (int i = 0; i < ctx.Cards.Pokemon.Count; i++)
            {
                var d = ctx.Cards.Pokemon[i];
                if (d.Dex == me.Dex && d.Trainer == me.Trainer && d.State != 0) list.Add(i);
            }
            list.Sort((a, b) => ctx.Cards.Pokemon[a].State.CompareTo(ctx.Cards.Pokemon[b].State));
            return list.Count > 1 ? list : new List<int>();
        }

        /// <summary>갤럭시단 카드의 주인 이름 키(<c>galactic.&lt;주인&gt;</c>). 야생 카드면 null.</summary>
        public static string OwnerKey(PokemonDef def)
        {
            int at = def.Key.IndexOf('@');
            return at >= 0 ? "galactic." + def.Key.Substring(at + 1) : null;
        }
    }
}
