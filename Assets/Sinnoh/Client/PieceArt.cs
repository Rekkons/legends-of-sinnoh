using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 말 발밑 고리와 머리 위 좌석 번호 핀을 픽셀 그림으로 만든다(M4-2d, 04 §3-1 「발밑 그림자 + 좌석 색 고리(두 겹), 머리 위 좌석 번호 핀」, 목업 r2 pieceSVG).
    /// 말 그림과 같은 정수배 k로 화면 공간에 그린다(BoardPiece 셰이더). 크기·층 [가정 M4-2d]:
    /// - 고리: 땅에 누운 타원(반지름 RingA × RingB 텍셀, 세로/가로 ≈ sin 55° — 직업 그림 80×80용. 주인공 도트 32×32는 DotRingA × DotRingB [가정]). 바깥부터 어두운 선 1 · 좌석 색 2 · 흰 선 1 · 그림자 채움.
    ///   반투명은 Linear 보정(04 §8-6): 그림자 채움 목업 0.38 → 1 − 0.62^2.2 ≈ 0.65, 어두운 바탕 위 흰 선 0.75 → 0.75^2.2 ≈ 0.53.
    /// - 핀: PinSize² 둥근 사각형(모서리 1텍셀 깎음), 어두운 테두리 1 · 좌석 색 채움 · 5×7 숫자(글꼴 자산은 M4-3, 03 §8 #15 — 그때까지 코드 글리프).
    /// </summary>
    public static class PieceArt
    {
        public const int RingA = 14;
        public const int RingB = 11;
        /// <summary>주인공 도트 말(32×32)의 고리 반지름 [가정]: 도트 몸 폭(약 16텍셀)을 조금 넘는다.</summary>
        public const int DotRingA = 10;
        public const int DotRingB = 8;
        public const int PinSize = 11;

        const byte ShadowAlpha = 166;   // 0.65
        const byte InnerLineAlpha = 135; // 0.53

        // 5×7 숫자 1~6(위 줄부터, '#' = 글자)
        static readonly string[][] Digits =
        {
            new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            new[] { ".###.", "#...#", "....#", "..##.", "....#", "#...#", ".###." },
            new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
        };

        /// <summary>고리 텍셀 층: 0 = 밖, 1 = 어두운 선, 2·3 = 좌석 색, 4 = 흰 선, 5 = 그림자 채움. 반지름을 1씩 줄인 타원 몇 개 안에 드나로 센다(선 굵기가 가로·세로 1텍셀).</summary>
        public static int RingLayer(int x, int y) => RingLayer(x, y, RingA, RingB);

        /// <summary>반지름 ra × rb 고리의 텍셀 층.</summary>
        public static int RingLayer(int x, int y, int ra, int rb)
        {
            float dx = x + 0.5f - ra, dy = y + 0.5f - rb;
            int layer = 0;
            for (int i = 0; i < 5; i++)
            {
                float a = ra - i, b = rb - i;
                if (dx * dx / (a * a) + dy * dy / (b * b) <= 1f) layer = i + 1;
                else break;
            }
            return layer;
        }

        /// <summary>고리 그림(2·RingA × 2·RingB, 가운데 = 발 점). 아래 줄이 0행(Unity 텍스처 순서).</summary>
        public static Texture2D Ring(Color32 seat) => Ring(seat, RingA, RingB);

        /// <summary>반지름 ra × rb 고리 그림(2·ra × 2·rb).</summary>
        public static Texture2D Ring(Color32 seat, int ra, int rb)
        {
            int w = ra * 2, h = rb * 2;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color32 c;
                    switch (RingLayer(x, y, ra, rb))
                    {
                        case 0: c = new Color32(0, 0, 0, 0); break;
                        case 1: c = SeatColors.Edge; break;
                        case 2:
                        case 3: c = seat; break;
                        case 4: c = new Color32(0xFF, 0xFF, 0xFF, InnerLineAlpha); break;
                        default: c = new Color32(0, 0, 0, ShadowAlpha); break;
                    }
                    px[y * w + x] = c;
                }
            return Make("Ring", w, h, px);
        }

        /// <summary>좌석 번호 핀(PinSize², number 1~6). 아래 줄이 0행.</summary>
        public static Texture2D Pin(Color32 seat, int number)
        {
            if (number < 1 || number > Digits.Length) throw new System.ArgumentOutOfRangeException(nameof(number), number, "1~6");
            int n = PinSize, last = n - 1;
            var ink = SeatColors.Ink(seat);
            var glyph = Digits[number - 1];
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int row = last - y; // 위에서 몇째 줄
                    bool corner = (x == 0 || x == last) && (row == 0 || row == last);
                    bool border = x == 0 || x == last || row == 0 || row == last;
                    int gx = x - 3, gy = row - 2; // 글자 5×7을 가운데(열 3~7, 줄 2~8)에
                    Color32 c = corner ? new Color32(0, 0, 0, 0) : border ? SeatColors.Edge : seat;
                    if (gx >= 0 && gx < 5 && gy >= 0 && gy < 7 && glyph[gy][gx] == '#') c = ink;
                    px[y * n + x] = c;
                }
            return Make("Pin " + number, n, n, px);
        }

        static Texture2D Make(string name, int w, int h, Color32[] px)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
            };
            t.SetPixels32(px);
            t.Apply(false);
            return t;
        }
    }
}
