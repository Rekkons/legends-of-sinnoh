using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sinnoh.Editor
{
    /// <summary>
    /// Gen/ 아래 그림·소리의 들여오기 설정을 폴더로 정한다(03 §2-0 표, isekai ArtImport 방식). 손으로 바꾸지 않는다.
    /// <list type="bullet">
    /// <item>픽셀 그림(Gen/Sprites·Stage·Trainers·Fonts·Field·Tutorial): Texture2D, Point, 밉맵 없음, 무압축 RGBA32(글꼴 아틀라스 pt_*.png는 R8·선형), Clamp, 최대 4096, 크기 그대로(NPOT 그대로).</item>
    /// <item>일반 그림(Gen/Cards·Board·Rulebook):Bilinear, Clamp, alphaIsTransparency 켬(투명 칸에 이웃 색을 번지게 — 투명 RGB가 검정이라 밉맵·쌍선형 가장자리가 어두워지는 것을 막는다).
    /// 원본 가로·세로가 4의 배수면 PC BC7·WebGL DXT1(불투명 = PNG 색 형식에 알파 없음)/DXT5, 아니면 무압축. Board만 밉맵.
    /// 헥스 디퓨즈(Board/hex_*.png, 4157²)는 가장 가까운 2의 거듭제곱으로 맞춘 뒤 최대 1024 [가정 M4-1e, 03 §2-4 「크기·압축은 M4-1e」].</item>
    /// <item>피규어 텍스처(Gen/Figures): 번들 원본(DXT1/DXT5)을 PNG로 뽑은 것 → PC·WebGL 모두 DXT1(불투명)/DXT5, 밉맵, Repeat, Bilinear, 최대 2048.</item>
    /// <item>BGM(Gen/Audio/Bgm): Vorbis 품질 0.5, Streaming(03 §3-2). Pt 원곡 WAV(M9 소리 ② — 앞부분 · 반복 클립).</item>
    /// </list>
    /// 불투명 판정은 PNG 머리(IHDR)의 색 형식으로 한다(0 회색·2 RGB = 알파 없음). 그림을 Unity에서 자르지 않는다(sprite 사각형 없음, 03 §2-0).
    /// </summary>
    public sealed class GenImport : AssetPostprocessor
    {
        public const string GenRoot = "Assets/Sinnoh/Gen/";
        public const string Standalone = "Standalone";
        public const string WebGL = "WebGL";

        public enum Kind { None, Pixel, FontAtlas, General, Figure }

        static readonly string[] PixelDirs = { "Sprites/", "Stage/", "Trainers/", "Fonts/", "Field/", "Tutorial/" };
        static readonly string[] GeneralDirs = { "Cards/", "Board/", "Rulebook/" };   // Rulebook = 설명서 그림(M9 — PDF에서 자른 그림이라 픽셀 그림이 아니다, plan §5 #341)

        // 1: M4-1e 처음(03 §2-0 표). 2: 일반 그림 alphaIsTransparency 켬(검토 반영 M4-1)
        public override uint GetVersion() => 2;

        /// <summary>경로 → 종류. Gen 밖·모르는 폴더는 None(설정을 건드리지 않는다).</summary>
        public static Kind KindOf(string path)
        {
            if (path == null || !path.StartsWith(GenRoot)) return Kind.None;
            string rel = path.Substring(GenRoot.Length);
            if (rel.StartsWith("Figures/")) return Kind.Figure;
            if (rel.StartsWith("Fonts/") && Path.GetFileName(rel).StartsWith("pt_") && rel.EndsWith(".png")) return Kind.FontAtlas;
            foreach (var d in PixelDirs) if (rel.StartsWith(d)) return Kind.Pixel;
            foreach (var d in GeneralDirs) if (rel.StartsWith(d)) return Kind.General;
            return Kind.None;
        }

        /// <summary>일반 그림 중 밉맵을 쓰는 것: 보드(3D 판 위, 멀리서 본다).</summary>
        public static bool BoardMips(string path) => path.StartsWith(GenRoot + "Board/");

        /// <summary>헥스 디퓨즈: 2의 거듭제곱으로 맞추고 최대 1024.</summary>
        public static bool IsHexDiffuse(string path) => path.StartsWith(GenRoot + "Board/hex_") && path.EndsWith(".png");

        public const int PixelMax = 4096, GeneralMax = 4096, HexMax = 1024, FigureMax = 2048;

        void OnPreprocessTexture()
        {
            var kind = KindOf(assetPath);
            if (kind == Kind.None) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.isReadable = false;
            ti.alphaIsTransparency = kind == Kind.General;   // 픽셀·글꼴·피규어는 투명 픽셀 RGB를 건드리지 않는다(03 §2-0)
            PngInfo(assetPath, out int w, out int h, out bool hasAlpha);
            switch (kind)
            {
                case Kind.Pixel:
                case Kind.FontAtlas:
                {
                    bool font = kind == Kind.FontAtlas;
                    ti.npotScale = TextureImporterNPOTScale.None;
                    ti.mipmapEnabled = false;
                    ti.filterMode = FilterMode.Point;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.sRGBTexture = !font;   // 글꼴 아틀라스 = 팔레트 번호(0~3), 색이 아니다
                    ti.alphaSource = font ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                    ti.maxTextureSize = PixelMax;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    var fmt = font ? TextureImporterFormat.R8 : TextureImporterFormat.RGBA32;
                    ti.SetPlatformTextureSettings(Platform(ti, Standalone, fmt, PixelMax, TextureImporterCompression.Uncompressed));
                    ti.SetPlatformTextureSettings(Platform(ti, WebGL, fmt, PixelMax, TextureImporterCompression.Uncompressed));
                    break;
                }
                case Kind.General:
                {
                    bool hex = IsHexDiffuse(assetPath);
                    int max = hex ? HexMax : GeneralMax;
                    ti.npotScale = hex ? TextureImporterNPOTScale.ToNearest : TextureImporterNPOTScale.None;
                    ti.mipmapEnabled = BoardMips(assetPath);
                    ti.filterMode = FilterMode.Bilinear;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.sRGBTexture = true;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.maxTextureSize = max;
                    // 헥스 디퓨즈는 2의 거듭제곱으로 맞춰지므로 4의 배수
                    bool compress = hex || Compresses(w, h);
                    SetCompression(ti, compress, TextureImporterFormat.BC7, hasAlpha ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1, max);
                    break;
                }
                case Kind.Figure:
                {
                    ti.npotScale = TextureImporterNPOTScale.None;
                    ti.mipmapEnabled = true;
                    ti.filterMode = FilterMode.Bilinear;
                    ti.wrapMode = TextureWrapMode.Repeat;
                    ti.sRGBTexture = true;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.maxTextureSize = FigureMax;
                    var dxt = hasAlpha ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1;
                    SetCompression(ti, Compresses(w, h), dxt, dxt, FigureMax);
                    break;
                }
            }
        }

        void OnPreprocessAudio()
        {
            // 효과음(Gen/Audio/Se, 결정 66 ④): ADPCM·Decompress On Load(03 §3-2) — 짧은 것이라 바로 낸다
            if (assetPath.StartsWith(GenRoot + "Audio/Se/"))
            {
                var se = (AudioImporter)assetImporter;
                se.forceToMono = true;
                se.loadInBackground = false;
                var ss = se.defaultSampleSettings;
                ss.loadType = AudioClipLoadType.DecompressOnLoad;
                ss.compressionFormat = AudioCompressionFormat.ADPCM;
                ss.preloadAudioData = true;
                se.defaultSampleSettings = ss;
                return;
            }
            // 울음(Gen/Audio/Cry, M9 소리 ③): PokeAPI Ogg — Vorbis 0.5 · Decompress On Load(짧다 — 번들이 오면 바로 낸다)
            if (assetPath.StartsWith(GenRoot + "Audio/Cry/"))
            {
                var cry = (AudioImporter)assetImporter;
                cry.forceToMono = true;
                cry.loadInBackground = false;
                var cs = cry.defaultSampleSettings;
                cs.loadType = AudioClipLoadType.DecompressOnLoad;
                cs.compressionFormat = AudioCompressionFormat.Vorbis;
                cs.quality = 0.5f;
                cs.preloadAudioData = true;
                cry.defaultSampleSettings = cs;
                return;
            }
            if (!assetPath.StartsWith(GenRoot + "Audio/Bgm/")) return;
            var ai = (AudioImporter)assetImporter;
            ai.forceToMono = false;
            ai.loadInBackground = true;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.5f;
            s.preloadAudioData = false;
            ai.defaultSampleSettings = s;
        }

        /// <summary>BC7·DXT는 4×4 블록 — 원본 가로·세로가 모두 4의 배수일 때만 압축.</summary>
        public static bool Compresses(int w, int h) => w > 0 && h > 0 && w % 4 == 0 && h % 4 == 0;

        static void SetCompression(TextureImporter ti, bool compress, TextureImporterFormat pc, TextureImporterFormat web, int max)
        {
            var c = compress ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
            ti.textureCompression = c;
            ti.SetPlatformTextureSettings(Platform(ti, Standalone, compress ? pc : TextureImporterFormat.RGBA32, max, c));
            ti.SetPlatformTextureSettings(Platform(ti, WebGL, compress ? web : TextureImporterFormat.RGBA32, max, c));
        }

        static TextureImporterPlatformSettings Platform(TextureImporter ti, string platform, TextureImporterFormat format, int max, TextureImporterCompression c)
        {
            var s = ti.GetPlatformTextureSettings(platform);
            s.overridden = true;
            s.format = format;
            s.maxTextureSize = max;
            s.textureCompression = c;
            s.crunchedCompression = false;
            return s;
        }

        /// <summary>PNG 머리(IHDR): 크기와 알파 유무(색 형식 0 회색·2 RGB = 없음, 3 팔레트는 있음으로 친다). PNG가 아니면 0·알파 있음.</summary>
        public static bool PngInfo(string path, out int w, out int h, out bool hasAlpha)
        {
            w = h = 0; hasAlpha = true;
            var b = new byte[26];
            try
            {
                using (var f = File.OpenRead(path))
                    if (f.Read(b, 0, 26) < 26) return false;
            }
            catch (IOException) { return false; }
            if (b[1] != 'P' || b[2] != 'N' || b[3] != 'G' || b[12] != 'I' || b[13] != 'H' || b[14] != 'D' || b[15] != 'R') return false;
            w = b[16] << 24 | b[17] << 16 | b[18] << 8 | b[19];
            h = b[20] << 24 | b[21] << 16 | b[22] << 8 | b[23];
            hasAlpha = !(b[25] == 0 || b[25] == 2);
            return true;
        }
    }
}
