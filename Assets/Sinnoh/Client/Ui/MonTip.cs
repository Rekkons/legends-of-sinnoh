using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 야생 포켓몬 카드의 마우스 오버 카드(M9 다듬기 P22 — 결정 116): 이름만 적힌 목록(사당 판의 교환할 야생 카드)에서
    /// 그 카드의 모습(도트) · 타입 · 체력 · 스피드 · 등급 · 포획률 · 고유 기술(효과 문구)을 보인다. 도감 상세 쪽(<see cref="DexScreen"/>)과 같은 값 · 같은 글 키를 쓴다.
    /// 공개 정보 카드(<see cref="InfoCard"/>)와 같은 순서로 선다: 무대 뿌리에 붙고, 크기가 정해지면 대상 옆에 자리를 잡아 보인다. 마우스를 받지 않는다.
    /// 요소 이름: <c>mon-tip</c>·<c>mon-tip-name</c>·<c>mon-tip-rate</c>·<c>mon-tip-move</c>.
    /// </summary>
    public sealed class MonTip
    {
        readonly UiStage stage;
        readonly Ctx ctx;
        readonly VisualElement card;
        readonly MonArt mons;
        VisualElement target, clear;
        TipSide side;

        public VisualElement Element => card;

        /// <summary>지금 떠 있는 카드(포켓몬 표 색인 — 없으면 −1).</summary>
        public int Shown { get; private set; } = -1;

        public MonTip(UiStage stage, Ctx ctx)
        {
            this.stage = stage;
            this.ctx = ctx;
            mons = MonArt.Load();
            card = Kit.Panel(stage.Skin);
            card.name = "mon-tip";
            card.AddToClassList("sn-mon-tip");
            card.pickingMode = PickingMode.Ignore;
            card.style.display = DisplayStyle.None;
            card.RegisterCallback<GeometryChangedEvent>(e => Place());
        }

        /// <summary>그 요소에 마우스를 올리면 카드를 띄운다. clear = 덮지 않을 판(그 판의 옆에 선다 — 없으면 요소 옆).</summary>
        public void Hook(VisualElement el, int def, TipSide side, VisualElement clear = null)
        {
            el.RegisterCallback<PointerEnterEvent>(e => Show(el, def, side, clear));
            el.RegisterCallback<PointerLeaveEvent>(e => { if (target == el) Hide(); });
            el.RegisterCallback<DetachFromPanelEvent>(e => { if (target == el) Hide(); });
        }

        /// <summary>그 요소 옆에 그 카드를 띄운다(포인터 콜백 · 캡처 · 시험).</summary>
        public void Show(VisualElement el, int def, TipSide side, VisualElement clear = null)
        {
            var root = stage.Root;
            if (el == null || root == null || def < 0 || def >= ctx.Cards.Pokemon.Count) return;
            target = el;
            this.clear = clear;
            this.side = side;
            Shown = def;
            Fill(ctx.Cards.Pokemon[def], def);
            Unpick(card);
            if (card.parent != root) root.Add(card);
            card.BringToFront();
            stage.Tips.Element.BringToFront();   // 툴팁은 카드 위
            card.style.visibility = Visibility.Hidden;   // 크기가 정해지면(GeometryChanged) 자리를 잡아 보인다
            card.style.display = DisplayStyle.Flex;
            Place();
        }

        public void Hide()
        {
            if (Shown < 0) return;
            Shown = -1;
            target = null;
            card.style.display = DisplayStyle.None;
        }

        /// <summary>뿌리에서 뗀다(판이 닫힐 때).</summary>
        public void Detach()
        {
            Hide();
            card.RemoveFromHierarchy();
        }

        void Fill(PokemonDef def, int index)
        {
            var inv = CultureInfo.InvariantCulture;
            card.Clear();
            var head = Add(card, new VisualElement(), null, "sn-mon-tip-head");
            head.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
            var info = Add(head, new VisualElement(), null, "sn-mon-tip-info");
            Add(info, Kit.Wrapped(stage.T("pokemon." + def.Key), Kit.F2), "mon-tip-name", "kit-title");
            var stats = Add(info, new VisualElement(), null, "sn-mon-tip-line");
            foreach (var t in def.Types) MonParts.TypeIcon(stats, t);
            stats.Add(Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.HealthIconKey), def.Health.ToString(inv)));
            stats.Add(Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.SpeedIconKey), def.Initiative.ToString(inv)));
            var facts = Add(info, new VisualElement(), null, "sn-mon-tip-line");
            string tier = stage.TOrNull("screen.encounter.grade." + MapActions.GradeOf(ctx, index).ToString().ToLowerInvariant());
            if (tier != null) facts.Add(Kit.Badge(tier, KitBadgeKind.Plain, Kit.F2));
            int rate = DexBook.CatchRate(ctx, index);
            Add(facts, Kit.Text(stage.T("screen.dex.rate"), Kit.F2), null, "kit-muted");
            Add(facts, rate > 0 ? Kit.Badge(stage.T("screen.dex.rate.value").Replace("{n}", rate.ToString(inv)), KitBadgeKind.Gold, Kit.F2) : Kit.Badge(stage.T("screen.dex.rate.none"), KitBadgeKind.Plain, Kit.F2), "mon-tip-rate", null);
            Add(card, Kit.Text(stage.T("screen.mon.sig"), Kit.F2), null, "kit-muted");
            var move = Add(card, new VisualElement(), "mon-tip-move", "sn-mon-move");
            move.Add(MonParts.MoveRow(stage, ctx, def.Signature, true, null));
            string text = stage.TOrNull(BattleFeed.MoveNameKey(ctx, def.Signature) + ".text");
            if (text != null) Add(move, Kit.Wrapped(text, Kit.F2), null, "sn-mon-text");
        }

        void Place()
        {
            var root = stage.Root;
            if (Shown < 0 || target == null || target.panel == null || root == null || card.panel == null) return;
            var size = card.layout;
            if (float.IsNaN(size.width) || float.IsNaN(size.height) || size.width <= 0f) return;
            Rect at = root.WorldToLocal(target.worldBound);
            if (clear != null && clear.panel != null)
            {
                // 판을 덮지 않게: 가로는 판의 폭, 세로는 대상의 자리
                Rect box = root.WorldToLocal(clear.worldBound);
                at = new Rect(box.x, at.y, box.width, at.height);
            }
            var spot = TipPlace.Place(at.x, at.y, at.width, at.height, size.width, size.height, root.layout.width, root.layout.height, side, InfoCard.Gap);
            card.style.left = Mathf.Round(spot.X);
            card.style.top = Mathf.Round(spot.Y);
            card.style.visibility = Visibility.Visible;
        }

        static void Unpick(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var child in e.Children()) Unpick(child);
        }

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
