using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace Rekkon.UiKit
{
    /// <summary>9-slice 창틀 그림 하나: 그림과 조각 크기(그림 px, 네 변 같음). 조각 배율은 토큰 벌이 정한다(<c>-unity-slice-scale</c>).</summary>
    public readonly struct KitFrame
    {
        public readonly Texture2D Image;
        public readonly int Slice;
        public KitFrame(Texture2D image, int slice) { Image = image; Slice = slice; }
    }

    /// <summary>
    /// 쓰는 프로젝트가 넣는 글꼴과 그림. 키트 저장소에는 글꼴 파일도 그림도 없다(USS에도 경로를 적지 않는다) — 토큰(색·크기·간격)만 키트 것이다.
    /// </summary>
    public sealed class KitSkin
    {
        /// <summary>모든 글자의 글꼴(<see cref="KitFont.CreatePixel"/>로 만든 도트 글꼴).</summary>
        public FontAsset Font;

        /// <summary>1080 벌 창 제목(<see cref="Kit.FTitle"/> 36px)의 글꼴 — 같은 TTF를 표본 36으로 만든 것. 없으면(null) 제목도 <see cref="Font"/>로 그린다.</summary>
        public FontAsset TitleFont;

        /// <summary>큰 판(창·독)의 창틀.</summary>
        public KitFrame Panel;

        /// <summary>툴팁의 창틀.</summary>
        public KitFrame Tip;
    }

    public static class KitFont
    {
        /// <summary>
        /// 도트 글꼴(TTF)을 UI Toolkit용 동적 FontAsset으로 만든다: <c>RASTER_HINTED</c>(안티에일리어스 없음), 아틀라스 Point 필터.
        /// 표본 크기는 화면에서 쓸 가장 큰 글자 크기(px)로 준다 — 그 크기와 정수로 나눈 크기(24 → 12)만 선명하다.
        /// 파일로 저장하지 않고 메모리에만 둔다(글자는 쓰일 때 아틀라스에 들어간다). 다 쓰면 만든 쪽이 Destroy한다.
        /// 아틀라스 한 장이 차서 새 장이 생기면 그 장은 Point 필터가 아니다 — 글자 수가 많은 프로젝트는 <paramref name="atlasSize"/>를 키운다.
        /// </summary>
        public static FontAsset CreatePixel(Font source, int samplingPointSize, int padding, int atlasSize = 2048)
        {
            var font = FontAsset.CreateFontAsset(source, samplingPointSize, padding, GlyphRenderMode.RASTER_HINTED, atlasSize, atlasSize, AtlasPopulationMode.Dynamic, true);
            if (font == null) return null;
            font.name = source.name + " Pixel";
            font.atlasTexture.filterMode = FilterMode.Point;
            return font;
        }
    }
}
