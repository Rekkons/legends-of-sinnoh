using System;
using System.Collections.Generic;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 도트 이펙트 시트(결정 69 — Gen <c>Stage/Fx</c>, <see cref="FxSheet"/>)를 읽어 둔다: 표는 한 번, 그림은 처음 쓸 때. Gen 본체가 없으면 아무것도 그리지 않는다.
    /// 어느 프레임을 그리나는 받은 시간만으로 정한다(<see cref="FxSheet.FrameAt"/>) — 여기에는 시계가 없다.
    /// </summary>
    public static class SheetArt
    {
        static Dictionary<string, FxSheet> sheets;
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            sheets = null;
            textures.Clear();
        }

        public static string TextureKey(FxSheet sheet) => "Stage/" + sheet.File;

        /// <summary>그 키의 시트(표에 없거나 Gen이 없으면 null).</summary>
        public static FxSheet Of(string key)
        {
            if (sheets == null)
            {
                sheets = new Dictionary<string, FxSheet>(StringComparer.Ordinal);
                var table = GenAssets.Load<TextAsset>(FxSheet.TableKey);
                if (table != null)
                    foreach (var s in FxSheet.ParseAll(table.text)) sheets[s.Key] = s;
            }
            return key != null && sheets.TryGetValue(key, out var sheet) ? sheet : null;
        }

        public static Texture2D Texture(FxSheet sheet)
        {
            if (sheet == null) return null;
            if (!textures.TryGetValue(sheet.Key, out var t) || t == null) textures[sheet.Key] = t = GenAssets.Load<Texture2D>(TextureKey(sheet));
            return t;
        }

        /// <summary>DS 화면에 찍을 조각(그 시각에 그릴 프레임이 없으면 null): 고정점이 (x, y)에 온다.</summary>
        public static DsSprite Ds(FxStamp stamp)
        {
            var sheet = Of(stamp.Key);
            int f = sheet != null ? sheet.FrameAt(stamp.Ms) : -1;
            if (f < 0) return null;
            return new DsSprite { Key = TextureKey(sheet), Sx = f * sheet.Fw, Sy = 0, Sw = sheet.Fw, Sh = sheet.Fh, X = stamp.X - sheet.Ox, Y = stamp.Y - sheet.Oy };
        }
    }

    /// <summary>
    /// PC 층(UI Toolkit)에 도트 이펙트를 찍는 판: 프레임마다 <see cref="Begin"/> → <see cref="Add"/>(시트 키 · 시작한 뒤의 시간 · 고정점의 패널 자리 · 텍셀 하나의 패널 크기) → <see cref="End"/>.
    /// 요소는 마우스를 받지 않는다(요소마다 <c>PickingMode.Ignore</c> — 칸 고르기·휠을 막지 않는다). 크기·자리는 그때그때 셈이라 USS가 아니라 인라인이다.
    /// 요소 이름: <c>fx-sheets</c>·<c>fx-sheet-&lt;n&gt;</c>.
    /// </summary>
    public sealed class SheetLayer
    {
        readonly VisualElement layer;
        readonly List<Image> pool = new List<Image>();
        readonly List<string> shown = new List<string>();
        int used;

        public VisualElement Element => layer;

        /// <summary>지금 그려진 이펙트(「키#프레임」 — 그린 순서). 시험·확인 도구가 본다.</summary>
        public IReadOnlyList<string> Shown => shown;

        public SheetLayer(string name = "fx-sheets")
        {
            layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
        }

        public void Begin()
        {
            used = 0;
            shown.Clear();
        }

        /// <param name="x">고정점이 놓일 패널 자리.</param>
        /// <param name="texel">텍셀 하나의 패널 크기(물리 px로 정수배가 되게 부르는 쪽이 고른다).</param>
        public void Add(string key, long ms, float x, float y, float texel)
        {
            var sheet = SheetArt.Of(key);
            int f = sheet != null ? sheet.FrameAt(ms) : -1;
            var tex = f >= 0 ? SheetArt.Texture(sheet) : null;
            if (tex == null || texel <= 0f) return;
            if (used == pool.Count)
            {
                var made = new Image { name = "fx-sheet-" + used.ToString(System.Globalization.CultureInfo.InvariantCulture), pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
                made.style.position = Position.Absolute;
                layer.Add(made);
                pool.Add(made);
            }
            var img = pool[used++];
            img.image = tex;
            img.sourceRect = new Rect(f * sheet.Fw, 0, sheet.Fw, sheet.Fh);
            img.style.left = x - sheet.Ox * texel;
            img.style.top = y - sheet.Oy * texel;
            img.style.width = sheet.Fw * texel;
            img.style.height = sheet.Fh * texel;
            img.style.display = DisplayStyle.Flex;
            shown.Add(key + "#" + f.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void End()
        {
            for (int i = used; i < pool.Count; i++) pool[i].style.display = DisplayStyle.None;
        }
    }
}
