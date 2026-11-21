using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 원작 자산 비트맵 글꼴 하나(03 §2-2 — Gen <c>Fonts/pt_*.json|png</c>): 칸 16×16 아틀라스(R8 = 팔레트 번호: 1 글자 · 2 그림자 · 3 바탕)와 글자표 <c>rows: [[유니코드, 글리프, 폭], …]</c>.
    /// DS 층(<see cref="DsStage"/>)이 글자를 한 칸씩 찍는 데 쓴다. 글자표에 없는 글자는 대체 글리프(<c>fallback</c>)로 찍는다.
    /// </summary>
    public sealed class DsFont
    {
        public struct Glyph
        {
            /// <summary>아틀라스 안 칸의 왼쪽 위(텍셀, y 아래로).</summary>
            public int X, Y;
            /// <summary>다음 글자까지의 폭(px).</summary>
            public int Advance;
        }

        public readonly Texture2D Atlas;
        public readonly int Cell;
        readonly Dictionary<int, Glyph> glyphs = new Dictionary<int, Glyph>();
        readonly Glyph fallback;

        DsFont(Texture2D atlas, int cell, int columns, string fallbackChar, List<int[]> rows)
        {
            Atlas = atlas;
            Cell = cell;
            foreach (var r in rows)
                glyphs[r[0]] = new Glyph { X = r[1] % columns * cell, Y = r[1] / columns * cell, Advance = r[2] };
            if (string.IsNullOrEmpty(fallbackChar) || !glyphs.TryGetValue(fallbackChar[0], out fallback)) glyphs.TryGetValue('?', out fallback);
        }

        /// <summary>글꼴을 읽는다(이름 = <c>pt_message</c>·<c>pt_system</c>). Gen 본체가 없으면 null.</summary>
        public static DsFont Load(string name)
        {
            var json = GenAssets.Load<TextAsset>(TableKey(name));
            var atlas = GenAssets.Load<Texture2D>(AtlasKey(name));
            if (json == null || atlas == null) return null;
            return Parse(json.text, atlas);
        }

        public static string TableKey(string name) => "Fonts/" + name + ".json";
        public static string AtlasKey(string name) => "Fonts/" + name + ".png";

        /// <summary>글자표 JSON을 읽는다. <c>rows</c>가 수 셋짜리 배열의 배열이라 JsonUtility로 못 읽어 직접 훑는다.</summary>
        public static DsFont Parse(string json, Texture2D atlas)
        {
            int cell = Int(json, "cell"), columns = Int(json, "columns");
            if (cell <= 0 || columns <= 0) throw new FormatException("글꼴 표에 cell·columns가 없다");
            string fb = null;
            int f = json.IndexOf("\"fallback\"", StringComparison.Ordinal);
            if (f >= 0)
            {
                int q1 = json.IndexOf('"', json.IndexOf(':', f) + 1), q2 = json.IndexOf('"', q1 + 1);
                if (q1 >= 0 && q2 > q1) fb = json.Substring(q1 + 1, q2 - q1 - 1);
            }
            var rows = new List<int[]>();
            int at = json.IndexOf("\"rows\"", StringComparison.Ordinal);
            if (at < 0) throw new FormatException("글꼴 표에 rows가 없다");
            at = json.IndexOf('[', at) + 1;
            while (true)
            {
                int open = json.IndexOf('[', at);
                if (open < 0) break;
                int close = json.IndexOf(']', open);
                var parts = json.Substring(open + 1, close - open - 1).Split(',');
                if (parts.Length == 3)
                    rows.Add(new[] { int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(parts[2], CultureInfo.InvariantCulture) });
                at = close + 1;
            }
            return new DsFont(atlas, cell, columns, fb, rows);
        }

        static int Int(string json, string name)
        {
            int at = json.IndexOf("\"" + name + "\"", StringComparison.Ordinal);
            if (at < 0) return 0;
            at = json.IndexOf(':', at) + 1;
            int end = at;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == ' ')) end++;
            return int.Parse(json.Substring(at, end - at).Trim(), CultureInfo.InvariantCulture);
        }

        public bool Has(char c) => glyphs.ContainsKey(c);

        public Glyph Of(char c) => glyphs.TryGetValue(c, out var g) ? g : fallback;

        /// <summary>그 글의 폭(px).</summary>
        public int Width(string text)
        {
            int w = 0;
            if (text != null) foreach (char c in text) w += Of(c).Advance;
            return w;
        }
    }
}
