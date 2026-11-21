using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Sinnoh.Rules;

namespace Sinnoh.Data
{
    /// <summary>
    /// decks.json + cards.json(build.mjs가 세이브 덱 17개에서 만듦, 03 §1-3) → DeckDb, 그리고 그 반대(WriteDecks·WriteCards).
    /// 덱 항목의 카드 키는 덱 종류가 정한 표에서 찾는다: Pokemon → CardDb pokemon, TrainerCard → CardDb trainer_cards, Card → cards.json.
    /// 경로는 받지 않는다(01 §11). 엄격하다: 빠진·모르는 필드, 모르는 kind, 키 형식(덱 deck., 카드 &lt;덱&gt;.), 없는 키 참조,
    /// 카드 행과 덱 항목이 다른 것, 면 규칙(CheckFaces)을 어긴 것은 FormatException. Write*는 build.mjs와 같은 바이트를 쓴다(plan M2 조건 3).
    /// </summary>
    public static class DeckDbLoader
    {
        const string DecksFile = "decks.json";
        const string CardsFile = "cards.json";

        public static DeckDb Load(Stream decks, Stream cards, CardDb cardDb) =>
            Load(TableJson.ReadAll(decks, nameof(decks)), TableJson.ReadAll(cards, nameof(cards)), cardDb);

        public static DeckDb Load(string decksJson, string cardsJson, CardDb cardDb)
        {
            if (cardDb == null) throw new ArgumentNullException(nameof(cardDb));
            var deckRows = TableJson.Parse<DeckRow>(decksJson, DecksFile);
            var cardRows = TableJson.Parse<CardRow>(cardsJson, CardsFile);

            var deckKeys = new string[deckRows.Count];
            for (int i = 0; i < deckKeys.Length; i++)
            {
                TableJson.CheckKey(deckRows[i].Key, "deck", DecksFile, i);
                deckKeys[i] = deckRows[i].Key;
            }
            var deckIds = TableJson.Wrap(DecksFile, () => new IdTable("decks", deckKeys));

            var cards = new CardDef[cardRows.Count];
            var cardKeys = new string[cards.Length];
            for (int i = 0; i < cards.Length; i++)
            {
                var r = cardRows[i];
                int deck = TableJson.Index(deckIds, r.Deck, $"{CardsFile} {r.Key}");
                TableJson.CheckKey(r.Key, r.Deck.Substring("deck.".Length), CardsFile, i);
                string at = $"{CardsFile} {r.Key}";
                if (r.Src == null) throw new FormatException($"{at}: src가 null이다");
                if (!VerifiedDate.IsMatch(r.Verified ?? "")) throw new FormatException($"{at}: verified \"{r.Verified}\"(YYYY-MM-DD)");
                CardTrade[] trades = null;
                if (r.Trades != null)
                {
                    trades = new CardTrade[r.Trades.Count];
                    for (int k = 0; k < trades.Length; k++)
                    {
                        var t = r.Trades[k] ?? throw new FormatException($"{at}: 거래 {k}가 null이다");
                        var give = Colors(t.Give, at);
                        var get = Colors(t.Get, at);
                        trades[k] = TableJson.Wrap(CardsFile, () => new CardTrade(give, get));
                    }
                }
                cards[i] = TableJson.Wrap(CardsFile, () => new CardDef(r.Key, deck, r.Copies, r.En, trades, r.Src.Ko, r.Src.En, r.Verified));
                cardKeys[i] = r.Key;
            }
            var cardIds = TableJson.Wrap(CardsFile, () => new IdTable("cards", cardKeys));

            var decks = new DeckDef[deckRows.Count];
            for (int i = 0; i < decks.Length; i++)
            {
                var r = deckRows[i];
                string at = $"{DecksFile} {r.Key}";
                var kind = TableJson.ParseEnum<DeckKind>(r.Kind, at);
                var ids = kind == DeckKind.Pokemon ? cardDb.PokemonIds : kind == DeckKind.TrainerCard ? cardDb.TrainerCardIds : cardIds;
                var entries = Entries(r.Cards, ids, at);
                var alts = Entries(r.AltStates, ids, at);
                CheckFaces(kind, entries, alts, cardDb, at);
                decks[i] = TableJson.Wrap(DecksFile, () => new DeckDef(r.Key, kind, entries, alts));
            }
            return TableJson.Wrap(DecksFile, () => new DeckDb(decks, cards));
        }

        static DeckEntry[] Entries(List<EntryRow> rows, IdTable ids, string at)
        {
            var a = new DeckEntry[rows.Count];
            for (int i = 0; i < a.Length; i++)
            {
                var e = rows[i] ?? throw new FormatException($"{at}: 항목 {i}이 null이다");
                int card = TableJson.Index(ids, e.Card, at);
                for (int k = 0; k < i; k++)
                    if (a[k].Card == card) throw new FormatException($"{at}: 카드 {e.Card}가 두 번 나온다");
                a[i] = TableJson.Wrap(DecksFile, () => new DeckEntry(card, e.Copies));
            }
            return a;
        }

        /// <summary>
        /// 면 규칙(build.mjs lib/save.mjs buildDecks와 같다). 포켓몬 덱: 바탕 카드는 첫 면(state ≤ 1), 다른 면은 state ≥ 2이고
        /// 같은 종·주인의 바탕 카드가 그 덱에 있다. 트레이너 카드 덱: 직업마다 바탕 1장, 다른 면(실버·골드)은 그 직업에 2.
        /// </summary>
        static void CheckFaces(DeckKind kind, DeckEntry[] cards, DeckEntry[] alts, CardDb db, string at)
        {
            if (kind == DeckKind.Pokemon)
            {
                foreach (var e in cards)
                    if (db.Pokemon[e.Card].State > 1) throw new FormatException($"{at}: 바탕 카드 {db.PokemonIds[e.Card]}가 첫 면이 아니다");
                foreach (var a in alts)
                {
                    var p = db.Pokemon[a.Card];
                    if (p.State < 2) throw new FormatException($"{at}: 다른 면 {p.Key}가 첫 면이다");
                    bool based = false;
                    foreach (var e in cards)
                        if (db.Pokemon[e.Card].Dex == p.Dex && db.Pokemon[e.Card].Trainer == p.Trainer) based = true;
                    if (!based) throw new FormatException($"{at}: 다른 면 {p.Key}의 바탕 카드가 덱에 없다");
                }
            }
            else if (kind == DeckKind.TrainerCard)
            {
                foreach (var e in cards)
                    if (e.Copies != 1) throw new FormatException($"{at}: 직업 {db.TrainerCardIds[e.Card]}의 바탕이 {e.Copies}장(1장이어야 한다)");
                foreach (var a in alts)
                {
                    bool based = false;
                    foreach (var e in cards)
                        if (e.Card == a.Card) based = true;
                    if (!based || a.Copies != 2)
                        throw new FormatException($"{at}: 직업 {db.TrainerCardIds[a.Card]}의 다른 면 {a.Copies}개(덱에 있는 직업마다 실버·골드 2)");
                }
            }
        }

        static readonly System.Text.RegularExpressions.Regex VerifiedDate =
            new System.Text.RegularExpressions.Regex(@"^20[0-9]{2}-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        static ApricornColor[] Colors(List<string> names, string at)
        {
            var a = new ApricornColor[names.Count];
            for (int i = 0; i < a.Length; i++) a[i] = TableJson.ParseEnum<ApricornColor>(names[i], at);
            return a;
        }

        static IEnumerable<string> Names(IReadOnlyList<ApricornColor> colors)
        {
            foreach (var c in colors) yield return c.ToString();
        }

        public static string WriteDecks(DeckDb db, CardDb cardDb) => TableJson.WriteTable(db.Decks, (w, d) =>
        {
            var ids = d.Kind == DeckKind.Pokemon ? cardDb.PokemonIds : d.Kind == DeckKind.TrainerCard ? cardDb.TrainerCardIds : db.CardIds;
            w.WriteStartObject();
            TableJson.Prop(w, "key", d.Key);
            TableJson.Prop(w, "kind", d.Kind.ToString());
            WriteEntries(w, "cards", d.Cards, ids);
            WriteEntries(w, "altStates", d.AltStates, ids);
            w.WriteEndObject();
        });

        static void WriteEntries(JsonWriter w, string name, IReadOnlyList<DeckEntry> entries, IdTable ids)
        {
            w.WritePropertyName(name);
            w.WriteStartArray();
            foreach (var e in entries)
            {
                w.WriteStartObject();
                TableJson.Prop(w, "card", ids[e.Card]);
                TableJson.Prop(w, "copies", e.Copies);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        public static string WriteCards(DeckDb db) => TableJson.WriteTable(db.Cards, (w, c) =>
        {
            w.WriteStartObject();
            TableJson.Prop(w, "key", c.Key);
            TableJson.Prop(w, "deck", db.DeckIds[c.Deck]);
            TableJson.Prop(w, "copies", c.Copies);
            if (c.En == null) TableJson.Prop(w, "en", (string)null);
            else TableJson.Strings(w, "en", c.En);
            w.WritePropertyName("trades");
            if (c.Trades == null) w.WriteNull();
            else
            {
                w.WriteStartArray();
                foreach (var t in c.Trades)
                {
                    w.WriteStartObject();
                    TableJson.Strings(w, "give", Names(t.Give));
                    TableJson.Strings(w, "get", Names(t.Get));
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            w.WritePropertyName("src");
            w.WriteStartObject();
            TableJson.Prop(w, "ko", c.SrcKo);
            TableJson.Prop(w, "en", c.SrcEn);
            w.WriteEndObject();
            TableJson.Prop(w, "verified", c.Verified);
            w.WriteEndObject();
        });

#pragma warning disable 0649 // Newtonsoft가 반사로 채운다
        // 필드 이름은 build.mjs(lib/save.mjs buildDecks + lib/authored.mjs mergeCards) 출력과 같다
        [JsonObject(ItemRequired = Required.Always)]
        sealed class DeckRow
        {
            public string Key;
            public string Kind;
            public List<EntryRow> Cards;
            public List<EntryRow> AltStates;
        }

        [JsonObject(ItemRequired = Required.Always)]
        sealed class EntryRow
        {
            public string Card;
            public int Copies;
        }

        [JsonObject(ItemRequired = Required.Always)]
        sealed class CardRow
        {
            public string Key;
            public string Deck;
            public int Copies;
            [JsonProperty(Required = Required.AllowNull)] public List<string> En;
            [JsonProperty(Required = Required.AllowNull)] public List<TradeRow> Trades;
            public SrcRow Src;
            public string Verified;
        }

        [JsonObject(ItemRequired = Required.Always)]
        sealed class TradeRow
        {
            public List<string> Give;
            public List<string> Get;
        }

        [JsonObject(ItemRequired = Required.Always)]
        sealed class SrcRow
        {
            public string Ko;
            public string En;
        }
#pragma warning restore 0649
    }
}
