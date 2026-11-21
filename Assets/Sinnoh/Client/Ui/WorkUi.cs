using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 행동을 낸 그 창이 연출도 그리는 보드 위 창(M5 「제작·은행·진화 화면」 — 마을 창의 은행상회 18행 · 제작 창 36·37행 · 진화 창 39행).
    /// <see cref="GameFlow"/>가 갱신·입력이 바뀔 때 <see cref="Refresh"/>, 화면 대기열이 도는 동안 프레임마다 <see cref="ShowFx"/>를 부르고,
    /// 받은 단계 가운데 이 묶음의 행(<see cref="WorkRows"/>)은 지금 열린 창이 그릴 때만(<see cref="Draws"/>) 남긴다 — 그릴 창이 없으면 대기열에서 덜어 낸다.
    /// </summary>
    public interface IWorkScreen : IUiScreen
    {
        void Refresh();

        void ShowFx(FxSequencer seq);

        /// <summary>그 단계(이 묶음의 행)를 이 창이 그리나.</summary>
        bool Draws(FxStep step);
    }

    /// <summary>제작·은행·진화 창이 같이 쓰는 작은 부품(아이템 그림 키 · 규토리 아이콘 · 요소 더하기).</summary>
    public static class WorkUi
    {
        /// <summary>기호(두 언어에 같다 — 배틀 화면의 「→」·「×」·「+」·「−」와 같은 글리프라 문자열 표에 두지 않는다).</summary>
        public const string Arrow = "→", Times = "×", Plus = "+", Minus = "−";

        /// <summary>부호 붙은 수(+2 · −1 · 0).</summary>
        public static string Signed(int n) => n > 0 ? Plus + n.ToString(System.Globalization.CultureInfo.InvariantCulture) : n < 0 ? Minus + (-n).ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";

        /// <summary>
        /// 아이템 그림(Gen <c>Cards/Tokens/item_icons/</c>): 볼 = 「&lt;이름&gt;.png」 · 지닌물건 = 「battle_item_&lt;이름&gt;.png」(수호신의 서 · 신오의 심장은 접두사 없는 파일) · 스튜 = 「stew_&lt;맛&gt;.png」(회복스튜의 그림 이름은 refreshing) ·
        /// 유물·천상의 유물 = <see cref="GenAssets.RelicIconKey"/>(결정 80 L14 나) · 트레이너 카드 전용 아이템 = <see cref="GenAssets.TrainerItemIcons"/>「&lt;이름&gt;.png」. 화면이 읽는 그림(<see cref="GenAssets.IsItemIcon"/>)이 없는 아이템(우두머리 등)은 null — 아이콘 칸을 만들지 않는다.
        /// </summary>
        public static string IconKey(ItemDef item)
        {
            if (item == null) return null;
            string name = item.Key.StartsWith("item.", StringComparison.Ordinal) ? item.Key.Substring("item.".Length) : item.Key;
            switch (item.Kind)
            {
                case ItemKind.Ball: return GenAssets.ItemIcons + name + ".png";
                case ItemKind.Stew:
                    string taste = name.EndsWith("_stew", StringComparison.Ordinal) ? name.Substring(0, name.Length - "_stew".Length) : name;
                    return GenAssets.ItemIcons + "stew_" + (taste == "restoring" ? "refreshing" : taste) + ".png";
                case ItemKind.Held:
                    string file = name + ".png";
                    return GenAssets.ItemIcons + (file == GenAssets.HeartIcon || file == GenAssets.ScrollIcon ? file : "battle_item_" + file);
                case ItemKind.Artifact:
                case ItemKind.CelestialArtifact: return GenAssets.RelicIconKey(item.Key);
                case ItemKind.TrainerItem: return GenAssets.TrainerItemIcons + name + ".png";   // 트레이너 카드 전용 아이템(rulings G1 — M6 그림 에셋)
                default: return null;
            }
        }

        /// <summary>그림 칸 하나(그림이 없으면 빈 칸 — 자리만).</summary>
        public static VisualElement Icon(VisualElement parent, string key, string cls)
        {
            var icon = Add(parent, new VisualElement(), null, cls);
            var tex = key != null ? GenAssets.Load<Texture2D>(key) : null;
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            return icon;
        }

        /// <summary>규토리 한 색의 아이콘.</summary>
        public static VisualElement Apricorn(VisualElement parent, ApricornColor color, string cls = "sn-icon") => Icon(parent, EncounterScreen.ApricornKey(color), cls);

        /// <summary>규토리 색의 이름 키(screen.apricorn.&lt;색&gt;).</summary>
        public static string ColorKey(ApricornColor color) => "screen.apricorn." + color.ToString().ToLowerInvariant();

        /// <summary>보이는 값(화면 대기열이 붙잡은 값 — 없으면 보기의 값): 그 좌석의 그 색 규토리 수.</summary>
        public static int ShownApricorns(GameFlow flow, int seat, int color)
        {
            var view = flow.Client.View;
            int value = view.Seats[seat].Apricorns[color];
            return flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Resource(seat, ResKind.Apricorn, color), value) : value;
        }

        /// <summary>
        /// 글자가 잘리거나 칸 밖으로 넘치는 곳(확인용 — UiShots 자리 단언 · PlayMode가 본다): root 아래 보이는 글(Label)마다
        /// ① 칸이 글보다 좁다(한 줄 글은 폭, 줄바꿈 글은 그 폭에서의 높이) ② 부모 칸 밖으로 나간다 ③ 같은 부모 안의 흐름 요소(절대 배치가 아닌 형제)와 겹친다.
        /// 투명한 것(opacity 0 — 연출 전의 꼬리표)·절대 배치 꼬리표·빈 글은 뺀다. 배치가 선 뒤에 부른다. 문제마다 한 줄(없으면 빈 목록).
        /// </summary>
        public static List<string> Overflows(VisualElement root)
        {
            var found = new List<string>();
            if (root == null) return found;
            root.Query<TextElement>().ForEach(l =>
            {
                if (string.IsNullOrEmpty(l.text) || l.resolvedStyle.position == Position.Absolute || Hidden(l) || Scrolled(l)) return;
                var box = l.contentRect;
                if (float.IsNaN(box.width) || box.width <= 0f) return;
                bool wrap = l.resolvedStyle.whiteSpace == WhiteSpace.Normal;
                var need = l.MeasureTextSize(l.text, box.width, wrap ? VisualElement.MeasureMode.Exactly : VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                string who = Who(l);
                // 「…」로 줄이는 칸(text-overflow: ellipsis — 정비 칸의 긴 기술 이름 · 보드 칸 정보 줄처럼 일부러 줄이고 전체는 마우스 오버)은 한 줄 넘침으로 치지 않는다(M9 언어 ② — 영어 이름)
                bool ellipsis = !wrap && l.resolvedStyle.textOverflow == TextOverflow.Ellipsis;
                if (!ellipsis && (wrap ? need.y > box.height + 1f : need.x > box.width + 1f)) found.Add(who + ": 글이 잘린다(칸 " + box.width.ToString("0") + "×" + box.height.ToString("0") + " < 글 " + need.x.ToString("0") + "×" + need.y.ToString("0") + ")");
                var me = l.worldBound;
                var p = l.parent != null ? l.parent.worldBound : me;
                if (me.xMin < p.xMin - 1f || me.xMax > p.xMax + 1f || me.yMin < p.yMin - 1f || me.yMax > p.yMax + 1f) found.Add(who + ": 부모 칸 밖으로 나간다");
                if (l.parent != null)
                    foreach (var sib in l.parent.Children())
                    {
                        if (sib == l || sib.resolvedStyle.position == Position.Absolute || Hidden(sib)) continue;
                        var o = sib.worldBound;
                        if (o.width <= 0f || o.height <= 0f) continue;
                        float w = Mathf.Min(me.xMax, o.xMax) - Mathf.Max(me.xMin, o.xMin), h = Mathf.Min(me.yMax, o.yMax) - Mathf.Max(me.yMin, o.yMin);
                        if (w > 1f && h > 1f) found.Add(who + ": 옆 요소(" + Who(sib) + ")와 겹친다");
                    }
            });
            return found;
        }

        /// <summary>세로 막대가 있는 여러 줄 글 칸 안의 글(넘치면 글 칸이 굴린다 — 버그 신고 설명, 결정 136): 넘침으로 치지 않는다.</summary>
        static bool Scrolled(VisualElement e)
        {
            for (var x = e.parent; x != null; x = x.parent)
                if (x is TextField f) return f.multiline && f.verticalScrollerVisibility != ScrollerVisibility.Hidden;
            return false;
        }

        static bool Hidden(VisualElement e)
        {
            for (var x = e; x != null; x = x.parent)
                if (x.resolvedStyle.display == DisplayStyle.None || x.resolvedStyle.opacity <= 0f || x.resolvedStyle.visibility == Visibility.Hidden) return true;
            return false;
        }

        static string Who(VisualElement e)
        {
            string text = e is TextElement l ? "「" + l.text + "」" : "";
            for (var x = e; x != null; x = x.parent)
                if (!string.IsNullOrEmpty(x.name)) return x.name + text;
            return text;
        }

        public static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
