using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Sinnoh.Rules;

namespace Sinnoh.Data
{
    /// <summary>칸 덮개 디퓨즈 텍스처(원작 GitHub assets/bases/tile_textures, board.json diffuse). 화면만 쓴다.</summary>
    public enum TileDiffuse { Standard, ForestCold, ForestTemperate, ForestWarm, Secret }

    /// <summary>
    /// 칸의 화면용 값(board.json 중 엔진 Board에 넣지 않는 것): 세이브 월드 좌표, 덮개 틴트 "#rrggbbaa", 디퓨즈,
    /// 덮개 밑 기둥 조각(두께 0.78)의 틴트 아래 → 위(M4-2b, 층 나누기는 BoardLayout).
    /// </summary>
    public sealed class CellLook
    {
        public readonly decimal X;
        public readonly decimal Z;
        public readonly string Tint;
        public readonly TileDiffuse Diffuse;
        public readonly IReadOnlyList<string> Pillars;

        public CellLook(decimal x, decimal z, string tint, TileDiffuse diffuse, IReadOnlyList<string> pillars)
        {
            X = x;
            Z = z;
            Tint = tint ?? throw new ArgumentNullException(nameof(tint));
            Diffuse = diffuse;
            Pillars = pillars ?? throw new ArgumentNullException(nameof(pillars));
        }
    }

    /// <summary>
    /// board.json(Tools/data/build.mjs가 세이브 → map_cells.csv에서 만듦, 03 §1-3) → Board(엔진) + CellLook(화면), 그리고 그 반대(Write).
    /// 경로는 받지 않는다(01 §11): Client는 TextAsset.text, 시험은 파일을 읽어 넘긴다.
    /// 엄격하게 읽는다(TableJson.Parse): 빠진 필드·모르는 필드·같은 필드 두 번·형이 다른 값·모르는 enum 값·카드 전용 지형 값(Ruins·Noble·Space)·
    /// key 불일치·틴트 형식은 FormatException.
    /// Write는 build.mjs와 같은 바이트를 쓴다(plan M2 조건 3).
    /// </summary>
    public static class BoardLoader
    {
        static readonly Regex Tint = new Regex("^#[0-9a-f]{8}$", RegexOptions.CultureInvariant);

        public static Board Load(Stream stream) => Load(TableJson.ReadAll(stream, nameof(stream)));

        public static Board Load(string json) => Load(json, out _);

        /// <summary>엔진 Board와 같은 칸 순서의 화면용 값(CellLook)을 함께 읽는다.</summary>
        public static Board Load(string json, out IReadOnlyList<CellLook> looks)
        {
            // level·x·z는 decimal로 읽는다(01 §0 결정성)
            var rows = TableJson.Parse<CellRow>(json, "board.json");

            var cells = new Cell[rows.Count];
            var lookRows = new CellLook[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                var r = rows[i];
                if (r.Key != Key(r.Col, r.Row)) throw new FormatException($"board.json 행 {i}: key {r.Key} ≠ {r.Col},{r.Row}");
                decimal quarters = r.Level * 4;
                if (quarters != decimal.Truncate(quarters)) throw new FormatException($"board.json {r.Key}: level {r.Level}이 0.25 단위가 아니다");
                var biome = ParseEnum<Biome>(r.Biome, r.Key);
                var climate = ParseEnum<Climate>(r.Climate, r.Key);
                // Ruins·Noble·Space는 포켓몬 카드 태그 전용이다(Board.cs). 칸에는 없다
                if (biome == Biome.Ruins || climate == Climate.Noble || climate == Climate.Space)
                    throw new FormatException($"board.json {r.Key}: 카드 전용 값 {r.Biome ?? "null"}/{r.Climate ?? "null"}은 칸에 쓰지 않는다");
                cells[i] = new Cell(new HexCoord(r.Col, r.Row), (int)quarters, biome, climate, r.Wild, r.Distortion, r.Region);
                if (r.Tint == null || !Tint.IsMatch(r.Tint)) throw new FormatException($"board.json {r.Key}: 틴트 \"{r.Tint}\"가 #rrggbbaa가 아니다");
                if (r.Diffuse == "None" || !Enum.TryParse(r.Diffuse, false, out TileDiffuse diffuse) || diffuse.ToString() != r.Diffuse)
                    throw new FormatException($"board.json {r.Key}: 모르는 디퓨즈 \"{r.Diffuse}\"");
                foreach (var p in r.Pillars)
                    if (p == null || !Tint.IsMatch(p)) throw new FormatException($"board.json {r.Key}: 기둥 틴트 \"{p}\"가 #rrggbbaa가 아니다");
                lookRows[i] = new CellLook(r.X, r.Z, r.Tint, diffuse, r.Pillars.ToArray());
            }
            looks = lookRows;
            return new Board(cells);
        }

        /// <summary>build.mjs와 같은 바이트(행 하나 = 한 줄, 필드 순서 고정). looks는 Load가 준 것(칸 순서가 같다).</summary>
        public static string Write(Board board, IReadOnlyList<CellLook> looks)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (looks == null || looks.Count != board.Count) throw new ArgumentException("looks는 칸마다 하나", nameof(looks));
            var rows = new int[board.Count];
            for (int i = 0; i < rows.Length; i++) rows[i] = i;
            return TableJson.WriteTable(rows, (w, i) =>
            {
                var c = board[i];
                var look = looks[i];
                w.WriteStartObject();
                TableJson.Prop(w, "key", Key(c.Coord.Col, c.Coord.Row));
                TableJson.Prop(w, "col", c.Coord.Col);
                TableJson.Prop(w, "row", c.Coord.Row);
                TableJson.Prop(w, "x", look.X);
                TableJson.Prop(w, "z", look.Z);
                TableJson.Prop(w, "level", c.LevelQuarters / 4m);
                TableJson.Prop(w, "biome", c.Biome == Biome.None ? null : c.Biome.ToString());
                TableJson.Prop(w, "climate", c.Climate == Climate.None ? null : c.Climate.ToString());
                TableJson.Prop(w, "wild", c.Wild);
                TableJson.Prop(w, "region", c.Region);
                TableJson.Prop(w, "distortion", c.DistortionWorld);
                TableJson.Prop(w, "tint", look.Tint);
                TableJson.Prop(w, "diffuse", look.Diffuse.ToString());
                TableJson.Strings(w, "pillars", look.Pillars);
                w.WriteEndObject();
            });
        }

        static string Key(int col, int row) =>
            col.ToString(CultureInfo.InvariantCulture) + "," + row.ToString(CultureInfo.InvariantCulture);

        /// <summary>null → None(0). 그 밖에는 enum 이름과 정확히 같아야 한다(숫자·쉼표 조합·"None" 문자열 거부).</summary>
        static T ParseEnum<T>(string value, string key) where T : struct, Enum
        {
            if (value == null) return default;
            if (value != "None" && Enum.TryParse(value, false, out T parsed) && parsed.ToString() == value) return parsed;
            throw new FormatException($"board.json {key}: 모르는 {typeof(T).Name} 값 \"{value}\"");
        }

#pragma warning disable 0649 // Newtonsoft가 반사로 채운다
        // 필드 순서·이름은 build.mjs 출력과 같다. x·z·tint·diffuse·pillars는 화면용이라 엔진 Board에 넣지 않는다(CellLook)
        [JsonObject(ItemRequired = Required.Always)]
        sealed class CellRow
        {
            public string Key;
            public int Col;
            public int Row;
            public decimal X;
            public decimal Z;
            public decimal Level;
            [JsonProperty(Required = Required.AllowNull)] public string Biome;
            [JsonProperty(Required = Required.AllowNull)] public string Climate;
            public bool Wild;
            [JsonProperty(Required = Required.AllowNull)] public string Region;
            public bool Distortion;
            public string Tint;
            public string Diffuse;
            public List<string> Pillars;
        }
#pragma warning restore 0649
    }
}
