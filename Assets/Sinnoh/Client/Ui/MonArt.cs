using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 포켓몬 정지 도트를 읽는다 — 앞모습을 PC 층 요소로 만들고(<see cref="Front"/>), DS 층에는 앞·뒷모습 조각을 준다(<see cref="TryPiece"/>)(03 §2-1 — Gen <c>Sprites/Static/static.json</c> 아틀라스, 키 = pokemon.json 키 「dex:state」).
    /// 요소 셋 겹: 칸(<c>sn-mon</c>, USS가 크기·잘라 보이기) → 원본 캔버스 96×96(<c>sn-mon-canvas</c>, USS가 벌마다 정수배로 늘린다 — 도트는 정수배만 선명하다) → 그림(아틀라스 조각).
    /// 그림의 자리·크기는 아틀라스 표의 텍셀 값 그대로다(화면 설계 값이 아니라 데이터). 그림은 아틀라스를 통째로 바탕에 깔고 조각만 보이게 자른다 — 따로 만드는 Sprite가 없어 치울 것도 없다.
    /// </summary>
    public sealed class MonArt
    {
        public const string TableKey = "Sprites/Static/static.json";
        const string Dir = "Sprites/Static/";

        [Serializable] sealed class Page { public string file; public int w; public int h; }
        [Serializable] sealed class KeyRow { public string key; public string sprite; }
        [Serializable] sealed class Row { public string sprite; public string side; public int page; public int[] rect; public int[] off; public int[] canvas; public int[] pivot; }
        [Serializable] sealed class Table { public Page[] pages; public KeyRow[] keys; public Row[] rows; }

        readonly Table table;
        readonly Texture2D[] pages;
        readonly Dictionary<string, Row> fronts = new Dictionary<string, Row>(StringComparer.Ordinal);
        readonly Dictionary<string, Row> backs = new Dictionary<string, Row>(StringComparer.Ordinal);

        MonArt(Table table, Texture2D[] pages)
        {
            this.table = table;
            this.pages = pages;
            var front = new Dictionary<string, Row>(StringComparer.Ordinal);
            var back = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (var r in table.rows) (r.side == "back" ? back : front)[r.sprite] = r;
            foreach (var k in table.keys)
            {
                if (front.TryGetValue(k.sprite, out var row)) fronts[k.key] = row;
                if (back.TryGetValue(k.sprite, out var rear)) backs[k.key] = rear;
            }
        }

        // 읽은 표(같은 TextAsset이면 다시 파싱하지 않는다 — 카드 · 마우스 오버가 카드마다 부른다, 표 약 113 KB). 도메인 리로드를 끈 Play에서도 비운다(CLAUDE.md)
        static MonArt cached;
        static TextAsset cachedJson;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            cached = null;
            cachedJson = null;
        }

        /// <summary>아틀라스 표와 그림을 읽는다(같은 표면 읽어 둔 것). Gen 본체가 없으면 null.</summary>
        public static MonArt Load()
        {
            var json = GenAssets.Load<TextAsset>(TableKey);
            if (json == null) return null;
            if (cached != null && ReferenceEquals(json, cachedJson) && Array.TrueForAll(cached.pages, p => p != null)) return cached;
            var table = JsonUtility.FromJson<Table>(json.text);
            var pages = new Texture2D[table.pages.Length];
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i] = GenAssets.Load<Texture2D>(Dir + table.pages[i].file);
                if (pages[i] == null) return null;
            }
            cachedJson = json;
            return cached = new MonArt(table, pages);
        }

        /// <summary>포켓몬 표의 키 → 도트 표의 키: 갤럭시단 카드는 같은 종의 그림을 쓴다(「436:0@male_grunt」 → 「436:0」).</summary>
        public static string ArtKey(string pokemonKey)
        {
            int at = pokemonKey != null ? pokemonKey.IndexOf('@') : -1;
            return at >= 0 ? pokemonKey.Substring(0, at) : pokemonKey;
        }

        public bool Has(string pokemonKey) => fronts.ContainsKey(ArtKey(pokemonKey));

        /// <summary>
        /// DS 층이 직접 찍을 조각: 아틀라스 쪽 그림 · 그 안 사각형(텍셀, y 아래로) · 발 자리(조각 안 — x = 캔버스 가운데, y = 가장 아래 불투명 줄, 04 §4-5).
        /// 표에 없는 키·쪽이면 false.
        /// </summary>
        public bool TryPiece(string pokemonKey, bool back, out Texture2D page, out RectInt rect, out Vector2Int pivot)
        {
            page = null;
            rect = default;
            pivot = default;
            if (!(back ? backs : fronts).TryGetValue(ArtKey(pokemonKey), out var row) || row.pivot == null || row.pivot.Length < 2) return false;
            page = pages[row.page];
            rect = new RectInt(row.rect[0], row.rect[1], row.rect[2], row.rect[3]);
            pivot = new Vector2Int(row.pivot[0], row.pivot[1]);
            return true;
        }

        /// <summary>그 포켓몬의 앞모습 칸. 표에 없는 키면 빈 칸(자리만).</summary>
        public VisualElement Front(string pokemonKey)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("sn-mon");
            if (!fronts.TryGetValue(ArtKey(pokemonKey), out var row)) return box;
            var canvas = new VisualElement { pickingMode = PickingMode.Ignore };
            canvas.AddToClassList("sn-mon-canvas");
            box.Add(canvas);
            // 조각: 캔버스 안 자리 = off, 크기 = rect의 폭·높이(텍셀)
            var clip = new VisualElement { pickingMode = PickingMode.Ignore };
            clip.style.position = Position.Absolute;
            clip.style.left = row.off[0];
            clip.style.top = row.off[1];
            clip.style.width = row.rect[2];
            clip.style.height = row.rect[3];
            clip.style.overflow = Overflow.Hidden;
            canvas.Add(clip);
            var sheet = new VisualElement { pickingMode = PickingMode.Ignore };
            sheet.style.position = Position.Absolute;
            sheet.style.left = -row.rect[0];
            sheet.style.top = -row.rect[1];
            sheet.style.width = table.pages[row.page].w;
            sheet.style.height = table.pages[row.page].h;
            sheet.style.backgroundImage = new StyleBackground(pages[row.page]);
            clip.Add(sheet);
            return box;
        }
    }
}
