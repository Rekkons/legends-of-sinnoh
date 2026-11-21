using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rekkon.UiKit
{
    /// <summary>
    /// 툴팁 하나(화면 뿌리마다 하나 만든다). UI Toolkit의 <c>tooltip</c>은 에디터 전용이라 실행 중에는 이것을 쓴다.
    /// <see cref="Hook"/>으로 대상에 걸면 마우스를 올렸을 때 뜨고, <see cref="Show"/>로 포인터 없이 띄울 수도 있다(캡처·튜토리얼).
    /// 자리는 대상의 배치 사각형(대상 자신의 translate·scale·rotate는 뺀다 — 눌림·튐 연출을 따라 움직이지 않게) 기준으로
    /// <see cref="TipPlace"/>가 정한다: 고른 쪽에 붙이고 화면 밖으로 나가면 반대편. 대상이 든 판을 덮지 않게 하려면 그 판을 <c>clear</c>로 준다 — 판 밖에 붙는다.
    /// </summary>
    public sealed class KitTooltip
    {
        struct Entry { public string Title, Body; public TipSide Side; public bool Reading; public VisualElement Clear; }

        static readonly CustomStyleProperty<float> GapProperty = new CustomStyleProperty<float>("--kit-tip-gap");

        readonly VisualElement root, tip;
        readonly Label title, body;
        readonly Dictionary<VisualElement, Entry> hooks = new Dictionary<VisualElement, Entry>();
        VisualElement shown;

        /// <param name="root"><see cref="Kit.Attach"/>한 화면 뿌리(패널을 덮는 요소). 툴팁은 이 요소의 맨 위 자식으로 올라간다.</param>
        public KitTooltip(VisualElement root, KitSkin skin)
        {
            this.root = root;
            tip = new VisualElement { name = "kit-tip", pickingMode = PickingMode.Ignore };
            tip.AddToClassList("kit-tip");
            Kit.SetFrame(tip, skin.Tip);
            title = Kit.Text("", Kit.F2);
            title.AddToClassList("kit-title");
            body = Kit.Wrapped("", Kit.F1);
            title.pickingMode = body.pickingMode = PickingMode.Ignore;
            tip.Add(title);
            tip.Add(body);
            tip.style.display = DisplayStyle.None;
            tip.RegisterCallback<GeometryChangedEvent>(e => Place());
            root.Add(tip);
        }

        /// <summary>툴팁 요소(이름 <c>kit-tip</c>).</summary>
        public VisualElement Element => tip;

        /// <summary>지금 툴팁이 떠 있는 대상. 없으면 null.</summary>
        public VisualElement Target => shown;

        /// <summary>
        /// 대상에 툴팁을 건다. 같은 대상에 다시 부르면 내용만 바뀐다(콜백은 한 번만 걸린다).
        /// <paramref name="title"/>·<paramref name="body"/> 중 하나는 null이어도 된다. 본문은 여러 줄로 감긴다.
        /// 본문 글자는 작은 크기(f1 — 짧은 수치 설명)이고, <paramref name="reading"/>이면 본문 크기(f2 — 읽을 문장. 1080 벌에서 24px)다.
        /// <paramref name="clear"/>(대상을 담은 판 등)를 주면 붙는 방향으로는 그 요소의 끝에 붙는다 — 위·아래면 그 요소의 위·아래 변 밖, 왼쪽·오른쪽이면 왼쪽·오른쪽 변 밖
        /// (판 안의 다른 줄을 덮지 않는다). 나란한 방향의 자리는 그대로 대상에 맞춘다.
        /// </summary>
        public void Hook(VisualElement target, string title, string body, TipSide side = TipSide.Below, bool reading = false, VisualElement clear = null)
        {
            if (!hooks.ContainsKey(target))
            {
                target.RegisterCallback<PointerEnterEvent>(OnEnter);
                target.RegisterCallback<PointerLeaveEvent>(OnLeave);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }
            hooks[target] = new Entry { Title = title, Body = body, Side = side, Reading = reading, Clear = clear };
            if (shown == target) Show(target);
        }

        public void Unhook(VisualElement target)
        {
            if (!hooks.Remove(target)) return;
            target.UnregisterCallback<PointerEnterEvent>(OnEnter);
            target.UnregisterCallback<PointerLeaveEvent>(OnLeave);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            if (shown == target) Hide();
        }

        /// <summary>걸어 둔 대상의 툴팁을 띄운다. 걸지 않은 대상이면 아무 일도 없다.</summary>
        public void Show(VisualElement target)
        {
            if (!hooks.TryGetValue(target, out var entry)) return;
            shown = target;
            title.text = entry.Title ?? "";
            title.style.display = string.IsNullOrEmpty(entry.Title) ? DisplayStyle.None : DisplayStyle.Flex;
            body.text = KoText.JoinWords(entry.Body);
            body.EnableInClassList(Kit.F1, !entry.Reading);
            body.EnableInClassList(Kit.F2, entry.Reading);
            body.style.display = string.IsNullOrEmpty(entry.Body) ? DisplayStyle.None : DisplayStyle.Flex;
            body.EnableInClassList("kit-mt4", !string.IsNullOrEmpty(entry.Title) && !string.IsNullOrEmpty(entry.Body));
            // 크기가 정해지기 전에는 보이지 않게 두고, 배치가 끝나면(GeometryChanged) 자리를 잡아 보인다
            tip.style.visibility = Visibility.Hidden;
            tip.style.display = DisplayStyle.Flex;
            tip.BringToFront();
            Place();
        }

        public void Hide()
        {
            shown = null;
            tip.style.display = DisplayStyle.None;
        }

        void OnEnter(PointerEnterEvent e) => Show((VisualElement)e.currentTarget);

        void OnLeave(PointerLeaveEvent e) { if (shown == e.currentTarget) Hide(); }

        void OnDetach(DetachFromPanelEvent e) { if (shown == e.currentTarget) Hide(); }

        void Place()
        {
            if (shown == null || shown.panel == null || tip.panel == null) return;
            var size = tip.layout;
            if (float.IsNaN(size.width) || float.IsNaN(size.height) || float.IsNaN(shown.layout.width)) return;   // 아직 배치 전
            // 대상의 배치 사각형: 부모 좌표의 layout을 패널 좌표로(대상 자신의 transform은 들어가지 않는다) → 뿌리 좌표로
            var parent = shown.hierarchy.parent;
            Rect target = root.WorldToLocal(parent != null ? parent.LocalToWorld(shown.layout) : shown.worldBound);
            if (!tip.customStyle.TryGetValue(GapProperty, out float gap)) gap = 0;
            var entry = hooks[shown];
            if (entry.Clear != null && entry.Clear.panel != null)
            {
                // 붙는 방향의 변만 피할 요소의 것으로 바꾼다(나란한 방향은 대상 그대로)
                Rect clear = root.WorldToLocal(entry.Clear.worldBound);
                if (entry.Side == TipSide.Above || entry.Side == TipSide.Below) target = new Rect(target.x, clear.y, target.width, clear.height);
                else target = new Rect(clear.x, target.y, clear.width, target.height);
            }
            var spot = TipPlace.Place(target.x, target.y, target.width, target.height, size.width, size.height, root.layout.width, root.layout.height, entry.Side, gap);
            tip.style.left = spot.X;
            tip.style.top = spot.Y;
            tip.style.visibility = Visibility.Visible;
        }
    }
}
