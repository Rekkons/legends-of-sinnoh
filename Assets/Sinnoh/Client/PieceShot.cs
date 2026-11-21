using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 말 캡처 판정(M4-2d, d3-3 §2-3 (b)(c)): 캡처한 화면 사각형이 원본 그림을 k배 최근접으로 키운 것과 픽셀까지 같은지 센다.
    /// 에디터 배치 캡처(Sinnoh.Editor.BoardShots)·PlayMode 시험이 같이 쓴다(OffscreenShot.Stats와 같은 자리).
    /// </summary>
    public static class PieceShot
    {
        /// <summary>
        /// shot(캡처, 아래가 0행)의 rect를 src(원본, 아래가 0행) k배와 견준다. 원본 불투명(a ≥ 128) 텍셀의 k×k 픽셀만 센다.
        /// pixels = 센 픽셀, mismatched = RGB가 하나라도 다른 픽셀, absDiff = 채널 차 합(MAE = absDiff / (3 × pixels)), offscreen = 화면 밖이라 못 센 픽셀.
        /// </summary>
        public static void Compare(Color32[] shot, int shotW, int shotH, Color32[] src, int w, int h, RectInt rect, int k,
            out int pixels, out int mismatched, out long absDiff, out int offscreen)
        {
            pixels = mismatched = offscreen = 0;
            absDiff = 0;
            for (int ty = 0; ty < h; ty++)
                for (int tx = 0; tx < w; tx++)
                {
                    var s = src[ty * w + tx];
                    if (s.a < 128) continue;
                    for (int j = 0; j < k; j++)
                        for (int i = 0; i < k; i++)
                        {
                            int x = rect.x + tx * k + i, y = rect.y + ty * k + j;
                            if (x < 0 || y < 0 || x >= shotW || y >= shotH) { offscreen++; continue; }
                            var c = shot[y * shotW + x];
                            int d = Mathf.Abs(c.r - s.r) + Mathf.Abs(c.g - s.g) + Mathf.Abs(c.b - s.b);
                            pixels++;
                            absDiff += d;
                            if (d != 0) mismatched++;
                        }
                }
        }

        /// <summary>원본 불투명 텍셀 수와 그 상자 높이(텍셀).</summary>
        public static int Opaque(Color32[] src, int w, int h, out int boxHeight)
        {
            int n = 0, top = -1, bottom = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (src[y * w + x].a >= 128)
                    {
                        n++;
                        if (bottom < 0) bottom = y;
                        top = y;
                    }
            boxHeight = n == 0 ? 0 : top - bottom + 1;
            return n;
        }

#if UNITY_EDITOR
        /// <summary>Gen 그림 원본 픽셀(아래가 0행)을 파일에서 읽는다(들인 텍스처는 isReadable 0). 없으면 null.</summary>
        public static Color32[] LoadSource(string genKey, out int w, out int h)
        {
            w = h = 0;
            string path = GenAssets.Root + genKey;
            if (!System.IO.File.Exists(path)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!t.LoadImage(System.IO.File.ReadAllBytes(path))) return null;
                w = t.width;
                h = t.height;
                return t.GetPixels32();
            }
            finally { Object.DestroyImmediate(t); }
        }
#endif
    }
}
