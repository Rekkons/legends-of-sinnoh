using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 운명의 변화 카드 한 장의 모양(M5 「운명·응답 화면」 — 손패 창 <see cref="FateScreen"/> · 배틀 독의 운명 줄 · 카드 연출이 같이 쓴다):
    /// 원작 카드 그림(<c>Cards/Fate/&lt;키&gt;.png</c>)의 그림 부분 + 이름 + 앞면 큰 글씨 한 줄(문자열 표 <c>change_of_fate.&lt;키&gt;</c>·<c>.text.1</c>, 04 §10-4) · 마우스 오버 = 1장·3장 효과.
    /// 크기·색은 USS(<c>sn-fate-card*</c>).
    /// </summary>
    public static class FateCards
    {
        /// <summary>카드 키 끝(카드 데이터 순서 = <see cref="FateCard"/>).</summary>
        public static readonly string[] Keys = { "dialga", "palkia", "giratina", "arceus" };

        public static string Key(FateCard card) => Keys[(int)card];

        /// <summary>문자열 표의 카드 이름 키.</summary>
        public static string NameKey(FateCard card) => "change_of_fate." + Key(card);

        public static Texture2D Art(FateCard card) => GenAssets.Load<Texture2D>(GenAssets.FateArt + Key(card) + ".png");

        public static Texture2D Back() => GenAssets.Load<Texture2D>(GenAssets.FateArt + "back.png");

        /// <summary>원작 카드(560×980)에서 그림 자리(x 60~500 · y 140~540)만 — UV(아래가 0).</summary>
        public static readonly Rect ArtUv = new Rect(60 / 560f, 1 - 540 / 980f, 440 / 560f, 400 / 980f);

        /// <summary>카드 한 장(세로): 그림 · 이름 · 큰 글씨 한 줄. tag가 있으면 아래에 꼬리표(까닭 · 「새 카드」).</summary>
        public static VisualElement Make(UiStage stage, FateCard card, string name, string tag = null, KitBadgeKind tagKind = KitBadgeKind.Plain)
        {
            var box = new VisualElement { name = name };
            box.AddToClassList("sn-fate-card");
            var art = new Image { image = Art(card), scaleMode = ScaleMode.ScaleAndCrop, uv = ArtUv, pickingMode = PickingMode.Ignore };
            art.AddToClassList("sn-fate-card-art");
            box.Add(art);
            var title = Kit.Text(stage.T(NameKey(card)), Kit.F2);
            title.pickingMode = PickingMode.Ignore;
            box.Add(title);
            var line = Kit.Text(stage.T(NameKey(card) + ".text.1"), Kit.F2);
            line.AddToClassList("kit-title");
            line.pickingMode = PickingMode.Ignore;
            box.Add(line);
            if (tag != null)
            {
                var badge = Kit.Badge(tag, tagKind, Kit.F1);
                badge.pickingMode = PickingMode.Ignore;
                badge.AddToClassList("sn-fate-card-tag");
                box.Add(badge);
            }
            return box;
        }

        /// <summary>그 카드의 마우스 오버 글: 1장 효과 · 3장 효과(문자열 표 그대로).</summary>
        public static string Tip(UiStage stage, FateCard card) => stage.T(NameKey(card) + ".text.1") + "\n" + stage.T(NameKey(card) + ".text.2");

        /// <summary>손패를 종류별로(카드 데이터 순서) 센 장수.</summary>
        public static int[] Count(Ctx ctx, IReadOnlyList<int> hand)
        {
            var n = new int[Keys.Length];
            if (hand == null) return n;
            foreach (int c in hand)
            {
                var k = FateRules.KindOf(ctx, c);
                if ((int)k >= 0 && (int)k < n.Length) n[(int)k]++;
            }
            return n;
        }
    }
}
