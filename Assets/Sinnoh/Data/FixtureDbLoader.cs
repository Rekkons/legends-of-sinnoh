using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Sinnoh.Rules;

namespace Sinnoh.Data
{
    /// <summary>
    /// fixtures.json(build.mjs가 세이브 월드 좌표를 가장 가까운 칸에 붙여 만듦, 03 §1-3) → FixtureDb, 그리고 그 반대(Write).
    /// 칸·전설·유물·스타터·짝은 문자열 키로 적혀 있고 Board·CardDb·ItemDb 색인으로 바뀐다. 경로는 받지 않는다(01 §11).
    /// 엄격하다: 빠진·모르는 필드, 모르는 enum 값, 키 형식(접두 = 종류 snake), 없는 칸·키 참조, 종류와 맞지 않는 필드는 FormatException.
    /// Write는 build.mjs와 같은 바이트를 쓴다(plan M2 조건 3).
    /// </summary>
    public static class FixtureDbLoader
    {
        const string File = "fixtures.json";
        static readonly Regex CellKey = new Regex(@"^(-?[0-9]+),(-?[0-9]+)$", RegexOptions.CultureInvariant);

        public static FixtureDb Load(Stream fixtures, Board board, CardDb cardDb, ItemDb items) =>
            Load(TableJson.ReadAll(fixtures, nameof(fixtures)), board, cardDb, items);

        public static FixtureDb Load(string fixturesJson, Board board, CardDb cardDb, ItemDb items)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (cardDb == null) throw new ArgumentNullException(nameof(cardDb));
            if (items == null) throw new ArgumentNullException(nameof(items));
            var rows = TableJson.Parse<FixtureRow>(fixturesJson, File);
            // 게이트 짝은 뒤 행을 가리킬 수 있어 키 표를 먼저 만든다
            var keys = new string[rows.Count];
            for (int i = 0; i < keys.Length; i++) keys[i] = rows[i].Key;
            var ids = TableJson.Wrap(File, () => new IdTable("fixtures", keys));

            var defs = new FixtureDef[rows.Count];
            for (int i = 0; i < defs.Length; i++)
            {
                var r = rows[i];
                string at = $"{File} {r.Key}";
                var kind = TableJson.ParseEnum<FixtureKind>(r.Kind, at);
                TableJson.CheckKey(r.Key, TableJson.Snake(kind.ToString()), File, i);
                int cell = Cell(board, r.Cell, at);
                EncounterTier? tier = r.Tier == null ? (EncounterTier?)null : TableJson.ParseEnum<EncounterTier>(r.Tier, at);
                var apricorns = new ApricornColor[r.Apricorns.Count];
                for (int k = 0; k < apricorns.Length; k++) apricorns[k] = TableJson.ParseEnum<ApricornColor>(r.Apricorns[k], at);
                var legendary = new int[r.Legendary.Count];
                for (int k = 0; k < legendary.Length; k++) legendary[k] = TableJson.Index(cardDb.PokemonIds, r.Legendary[k], at);
                var artifact = new int[r.Artifact.Count];
                for (int k = 0; k < artifact.Length; k++) artifact[k] = TableJson.Index(items.Ids, r.Artifact[k], at);
                var starters = new int[r.Starters.Count];
                for (int k = 0; k < starters.Length; k++) starters[k] = TableJson.Index(cardDb.PokemonIds, r.Starters[k], at);
                int pair = r.Pair == null ? FixtureDef.NoPair : TableJson.Index(ids, r.Pair, at);
                defs[i] = TableJson.Wrap(File, () => new FixtureDef(r.Key, kind, cell, tier, apricorns, legendary, artifact, starters, pair));
            }
            return TableJson.Wrap(File, () => new FixtureDb(defs));
        }

        static int Cell(Board board, string key, string at)
        {
            var m = key == null ? null : CellKey.Match(key);
            if (m == null || !m.Success) throw new FormatException($"{at}: 칸 \"{key}\"가 \"col,row\" 꼴이 아니다");
            int col = int.Parse(m.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            int row = int.Parse(m.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (((col + row) & 1) != 0 || !board.TryGetIndex(new HexCoord(col, row), out int cell))
                throw new FormatException($"{at}: 보드에 칸 {key}가 없다");
            return cell;
        }

        static string CellKeyOf(Board board, int cell)
        {
            var c = board[cell].Coord;
            return TableJson.Num(c.Col) + "," + TableJson.Num(c.Row);
        }

        public static string Write(FixtureDb db, Board board, CardDb cardDb, ItemDb items) => TableJson.WriteTable(db.Fixtures, (w, f) =>
        {
            w.WriteStartObject();
            TableJson.Prop(w, "key", f.Key);
            TableJson.Prop(w, "kind", f.Kind.ToString());
            TableJson.Prop(w, "cell", CellKeyOf(board, f.Cell));
            TableJson.Prop(w, "tier", f.Tier.HasValue ? f.Tier.Value.ToString() : null);
            var apricorns = new List<string>();
            foreach (var a in f.Apricorns) apricorns.Add(a.ToString());
            TableJson.Strings(w, "apricorns", apricorns);
            var legendary = new List<string>();
            foreach (int p in f.Legendary) legendary.Add(cardDb.PokemonIds[p]);
            TableJson.Strings(w, "legendary", legendary);
            var artifact = new List<string>();
            foreach (int it in f.Artifact) artifact.Add(items.Ids[it]);
            TableJson.Strings(w, "artifact", artifact);
            var starters = new List<string>();
            foreach (int p in f.Starters) starters.Add(cardDb.PokemonIds[p]);
            TableJson.Strings(w, "starters", starters);
            TableJson.Prop(w, "pair", f.Pair == FixtureDef.NoPair ? null : db.Ids[f.Pair]);
            w.WriteEndObject();
        });

#pragma warning disable 0649 // Newtonsoft가 반사로 채운다
        // 필드 이름은 build.mjs(lib/save.mjs buildFixtures) 출력과 같다
        [JsonObject(ItemRequired = Required.Always)]
        sealed class FixtureRow
        {
            public string Key;
            public string Kind;
            public string Cell;
            [JsonProperty(Required = Required.AllowNull)] public string Tier;
            public List<string> Apricorns;
            public List<string> Legendary;
            public List<string> Artifact;
            public List<string> Starters;
            [JsonProperty(Required = Required.AllowNull)] public string Pair;
        }
#pragma warning restore 0649
    }
}
