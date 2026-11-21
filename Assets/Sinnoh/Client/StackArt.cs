using System.Collections.Generic;
using Sinnoh.Fx;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 묶음 표지(결정 66 ①, 04 §3-1 「한 칸에 말 여럿」) 안의 동그란 그림을 픽셀 그림으로 만든다: 좌석 색 테두리(바깥부터 어두운 선 1 · 좌석 색 2 텍셀 — 발밑 고리와 같은 겹) 안에
    /// 그 말의 얼굴·윗부분을 원으로 잘라 넣는다. 화면은 이 그림을 정수배로만 늘린다(<see cref="Mul"/>).
    /// <list type="bullet">
    /// <item>주인공 도트 말: 앞모습으로 선 프레임의 머리 줄부터 <see cref="DotCrop"/>² 텍셀(머리와 어깨) → 지름 <see cref="DotCrop"/> + 6.</item>
    /// <item>직업 그림 말: 얼굴 조각(표 <c>trainers.json</c> fronts 행의 <c>face</c> — 전체 줌에서 말로 그리는 그 조각, <see cref="BoardPieces.ShapeAt"/>) → 지름 = 조각 폭 + 6.</item>
    /// </list>
    /// 들인 Gen 그림은 읽을 수 없어(isReadable 0) RenderTexture로 한 번 옮겨 읽는다. 만든 그림은 (그림 · 조각 · 좌석 색)마다 하나씩 쥐고 다시 쓴다.
    /// </summary>
    public static class StackArt
    {
        /// <summary>주인공 도트에서 잘라 넣는 조각의 한 변(텍셀) [가정]: 32×32 걷기 그림의 머리와 어깨.</summary>
        public const int DotCrop = 16;
        /// <summary>테두리 굵기(텍셀): 어두운 선 1 + 좌석 색 2.</summary>
        public const int Rim = 3;

        static readonly Dictionary<string, Texture2D> made = new Dictionary<string, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => made.Clear();

        /// <summary>그 말에서 잘라 넣는 조각(시트 안 px, 위가 0행).</summary>
        public static RectInt Crop(BoardPieces.Piece p)
        {
            if (p.Style == PieceStyle.Job) return p.Face;
            int front = p.Cycles != null ? p.Cycles[(int)Facing.South][0] : 0;
            int size = Mathf.Min(DotCrop, Mathf.Min(p.Width, p.Height));
            int x = Mathf.Clamp(p.Foot.x - size / 2, 0, p.Width - size);
            int y = Mathf.Clamp(p.Head, 0, p.Height - size);
            return new RectInt(x, front * p.Height + y, size, size);
        }

        /// <summary>그 말의 표지 그림 한 변(텍셀) = 조각 폭 + 테두리 둘.</summary>
        public static int Size(BoardPieces.Piece p) => Crop(p).width + 2 * Rim;

        /// <summary>
        /// 표지 그림을 패널 px로 늘리는 정수배 [가정]. 지역·근접 줌: 주인공 도트(22텍셀) 720 벌 ×2 · 1080 벌 ×3(44 · 66px), 직업 그림(36텍셀) ×1 · ×2(36 · 72px — 얼굴 상자의 작은 상자와 같은 배수).
        /// 전체 줌(줌 단계 1 — 칸 폭이 720에서 34px)은 한 단계 작게: 주인공 도트 ×1 · ×2(22 · 44px), 직업 그림 ×1 · ×1(36px).
        /// </summary>
        public static int Mul(PieceStyle style, int tokenHeight, int zoomStep)
        {
            bool big = tokenHeight >= 1080, full = zoomStep <= 1;
            if (style == PieceStyle.Dot) return (big ? 3 : 2) - (full ? 1 : 0);
            return big && !full ? 2 : 1;
        }

        /// <summary>그 말의 표지 그림(없으면 null — 말 그림이 없다). 아래 줄이 0행.</summary>
        public static Texture2D Face(BoardPieces.Piece p)
        {
            if (p == null || p.Texture == null) return null;
            var crop = Crop(p);
            if (crop.width <= 0) return null;
            string key = p.TextureKey + "|" + crop.x + "," + crop.y + "," + crop.width + "|" + p.Color.r + "," + p.Color.g + "," + p.Color.b;
            if (made.TryGetValue(key, out var tex) && tex != null) return tex;
            var cut = Read(p.Texture, crop);
            int n = crop.width + 2 * Rim;
            var px = new Color32[n * n];
            float c = n * 0.5f, r = n * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c, d2 = dx * dx + dy * dy;
                    Color32 col;
                    if (d2 > r * r) col = new Color32(0, 0, 0, 0);
                    else if (d2 > (r - 1) * (r - 1)) col = SeatColors.Edge;
                    else if (d2 > (r - Rim) * (r - Rim)) col = p.Color;
                    else
                    {
                        col = SeatColors.Paper;
                        int sx = x - Rim, sy = y - Rim;
                        if (sx >= 0 && sy >= 0 && sx < crop.width && sy < crop.height)
                        {
                            var s = cut[sy * crop.width + sx];
                            if (s.a >= 128) col = new Color32(s.r, s.g, s.b, 255);
                        }
                    }
                    px[y * n + x] = col;
                }
            tex = new Texture2D(n, n, TextureFormat.RGBA32, false, false)
            {
                name = "Stack " + p.TextureKey, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(px);
            tex.Apply(false);
            made[key] = tex;
            return tex;
        }

        // 그림의 조각(위가 0행인 자리)을 읽는다 — 돌려주는 줄은 아래가 0행
        static Color32[] Read(Texture2D src, RectInt crop)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            var tmp = new Texture2D(crop.width, crop.height, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.DontSave };
            try
            {
                rt.filterMode = FilterMode.Point;
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                tmp.ReadPixels(new Rect(crop.x, src.height - crop.y - crop.height, crop.width, crop.height), 0, 0);
                return tmp.GetPixels32();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (Application.isPlaying) Object.Destroy(tmp);
                else Object.DestroyImmediate(tmp);
            }
        }
    }
}
