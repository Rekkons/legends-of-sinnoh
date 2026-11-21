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
        VisualElement target, clear, over;
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
            if (el == null || stage.Root == null || def < 0 || def >= ctx.Cards.Pokemon.Count) return;
            target = el;
            this.clear = clear;
            over = null;
            this.side = side;
            Shown = def;
            Fill(ctx.Cards.Pokemon[def], def);
            Open();
        }

        /// <summary>
        /// 내 포켓몬 한 마리의 카드(결정 169 — 배틀 독의 포켓몬 고르기 줄): 정비 판 카드 확대(<see cref="MonScreen"/>)와 같은 내용 — 도트 · 타입 · 체력(지금/최대) · 스피드 · 상태 ·
        /// 늘 걸린 효과 · ★ 고유 기술과 배운 칸(효과 문구를 줄로 다) · 지닌물건. 모두 보기의 공개 값이다. hp · speed = 화면이 그리는 지금 값.
        /// 카드는 <paramref name="on"/> 판 안 아래쪽에 얹는다(그 판만 덮는다 — 배틀은 기록 기둥, 독 · DS를 가리지 않게).
        /// </summary>
        public void HookMon(VisualElement el, PokemonState pk, int hp, int speed, VisualElement on)
        {
            el.RegisterCallback<PointerEnterEvent>(e => ShowMon(el, pk, hp, speed, on));
            el.RegisterCallback<PointerLeaveEvent>(e => { if (target == el) Hide(); });
            el.RegisterCallback<DetachFromPanelEvent>(e => { if (target == el) Hide(); });
        }

        /// <summary><see cref="HookMon"/>의 카드를 지금 띄운다(포인터 콜백 · 캡처 · 시험).</summary>
        public void ShowMon(VisualElement el, PokemonState pk, int hp, int speed, VisualElement on)
        {
            if (el == null || pk == null || stage.Root == null || pk.Def < 0 || pk.Def >= ctx.Cards.Pokemon.Count) return;
            target = el;
            clear = null;
            over = on;
            side = TipSide.Above;
            Shown = pk.Def;
            FillMon(pk, hp, speed);
            Open();
        }

        void Open()
        {
            var root = stage.Root;
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

        void FillMon(PokemonState pk, int hp, int speed)
        {
            var inv = CultureInfo.InvariantCulture;
            var def = ctx.Cards.Pokemon[pk.Def];
            card.Clear();
            var head = Add(card, new VisualElement(), null, "sn-mon-tip-head");
            head.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
            var info = Add(head, new VisualElement(), null, "sn-mon-tip-info");
            Add(info, Kit.Wrapped(stage.T("pokemon." + def.Key), Kit.F2), "mon-tip-name", "kit-title");
            var types = Add(info, new VisualElement(), null, "sn-mon-tip-line");
            foreach (var t in def.Types) MonParts.TypeIcon(types, t);
            // 배울 수 있는 타입(결정 182 — 알파 스트라이크는 이 가운데 하나로 쓰인다, rulings A26): 정비 판 · 도감과 같은 말
            var learn = Add(info, new VisualElement(), "mon-tip-learn", "sn-mon-tip-line");
            Add(learn, Kit.Text(stage.T("screen.party.learn"), Kit.F2), null, "kit-muted");
            foreach (var t in def.LearnTypes) MonParts.TypeIcon(learn, t);
            var stats = Add(info, new VisualElement(), "mon-tip-stats", "sn-mon-tip-line");
            int max = PokemonRules.MaxHp(ctx, pk);
            stats.Add(Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.HealthIconKey), hp.ToString(inv), hp * 2 <= max ? KitTone.Low : KitTone.Base, max.ToString(inv)));
            stats.Add(Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.SpeedIconKey), speed.ToString(inv), speed < def.Initiative ? KitTone.Low : speed > def.Initiative ? KitTone.High : KitTone.Base));
            string status = hp <= 0 ? "screen.info.fainted" : MonParts.StatusKey(pk.Status);
            if (status != null) stats.Add(Kit.Badge(stage.T(status), hp <= 0 ? KitBadgeKind.Red : KitBadgeKind.Warn, Kit.F2));
            MonParts.Passives(stage, info, pk, true, "mon-tip-passive");   // 우두머리 · 비스트 부스트(칩 — 마우스를 받지 않는 카드라 글은 기술 줄이 대신한다)
            // 기술: ★ 고유 기술 + 배운 칸 — 효과 문구를 줄로 다 적는다(MonScreen과 같다)
            Add(card, Kit.Text(stage.T("screen.mon.sig"), Kit.F2), null, "kit-muted");
            MonMove(0, def.Signature, true, -1);
            Add(card, Kit.Text(stage.T("screen.mon.learned"), Kit.F2), null, "kit-muted");
            for (int k = 0; k < pk.Moves.Length; k++) MonMove(k + 1, pk.Moves[k], false, k < pk.Breach.Length ? pk.Breach[k] : -1, pk);
            // 지닌물건(있을 때만 — 우두머리 토큰 · 아이템 둘째 칸)
            foreach (int held in new[] { pk.HeldItem, pk.HeldItem2 })
            {
                if (held < 0 || held >= ctx.Items.Items.Count) continue;
                string key = ctx.Items.Items[held].Key;
                var item = Add(card, new VisualElement(), "mon-tip-item", "sn-mon-item");
                var itemHead = Add(item, new VisualElement(), null, "sn-hud-line");
                string icon = WorkUi.IconKey(ctx.Items.Items[held]);
                if (icon != null) WorkUi.Icon(itemHead, icon, "sn-icon");
                Add(itemHead, Kit.Text(stage.T(key), Kit.F2), null, "kit-title");
                string text = stage.TOrNull(key + ".text");
                if (text != null) item.Add(Kit.Wrapped(text, Kit.F2));
            }
            if (pk.Alpha)
            {
                var alpha = Add(card, new VisualElement(), "mon-tip-alpha", "sn-mon-item");
                string text = stage.TOrNull("item.alpha_pokemon.text");
                Add(alpha, Kit.Text(stage.T("item.alpha_pokemon"), Kit.F2), null, "kit-title");
                if (text != null) alpha.Add(Kit.Wrapped(text, Kit.F2));
            }
        }

        // 기술 칸 하나: 기술 줄 + 효과 문구 · 균열 카드가 막은 칸 · 빈 칸
        void MonMove(int at, int move, bool star, int breach, PokemonState pk = null)
        {
            var box = Add(card, new VisualElement(), "mon-tip-move-" + at.ToString(CultureInfo.InvariantCulture), "sn-mon-move");
            if (move < 0 && breach >= 0)
            {
                box.Add(MonParts.BreachRow(stage, ctx, breach, null));
                Add(box, Kit.Wrapped(MonParts.BreachText(stage, ctx.Decks.CardIds[breach]), Kit.F2), null, "sn-mon-text");
                return;
            }
            var legend = move < 0 && pk != null && at > 0 ? MonParts.TacticRow(stage, ctx, pk, at - 1, null) : null;
            if (legend != null)
            {
                // 전설 특수기술이 차지한 빈 칸(결정 195): 이름 · 타입 그림 + 효과 · 채우면 사라진다를 줄로(마우스를 받지 않는 카드)
                box.Add(legend);
                Add(box, Kit.Wrapped(MonParts.TacticTip(stage, LegendRules.TacticAt(pk, at - 1)), Kit.F2), null, "sn-mon-text");
                return;
            }
            if (move < 0)
            {
                Add(box, Kit.Text(stage.T("screen.party.slot.empty"), Kit.F2), null, "kit-muted");
                return;
            }
            box.Add(MonParts.MoveRow(stage, ctx, move, star, null));
            string text = stage.TOrNull(BattleFeed.MoveNameKey(ctx, move) + ".text");
            if (text != null) Add(box, Kit.Wrapped(text, Kit.F2), null, "sn-mon-text");
        }

        void Place()
        {
            var root = stage.Root;
            if (Shown < 0 || target == null || target.panel == null || root == null || card.panel == null) return;
            var size = card.layout;
            if (float.IsNaN(size.width) || float.IsNaN(size.height) || size.width <= 0f) return;
            if (over != null && over.panel != null)
            {
                // 그 판 안 아래쪽 가운데(판보다 크면 위로 — 화면 위 끝에서 멈춘다)
                Rect box = root.WorldToLocal(over.worldBound);
                card.style.left = Mathf.Round(Mathf.Clamp(box.x + (box.width - size.width) / 2f, 0f, Mathf.Max(0f, root.layout.width - size.width)));
                card.style.top = Mathf.Round(Mathf.Max(0f, box.yMax - size.height));
                card.style.visibility = Visibility.Visible;
                return;
            }
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
