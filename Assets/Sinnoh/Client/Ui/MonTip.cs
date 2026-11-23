using System;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 포켓몬 마우스 오버 카드(M9 다듬기 P22 — 결정 116 · 결정 169 · 결정 206 ①): 이름만 적힌 목록(사당 교환 · 버림 더미 · 고향 마을 고르기 · 공개 정보 카드의 명단 줄)과
    /// 배틀 독의 포켓몬 칸(내 포켓몬 · 무리 · 명단)에 마우스를 올리면 뜨는 카드. 원작 TTS 카드(<see cref="CardStack"/>)로 그린다 —
    /// 야생 카드 = 카드 앞면 + 카드에 없는 포획률 칩(목업 B-wild), 판의 포켓몬 = 옆으로 펼친 짜임(<see cref="CardLayout.Spread"/> — 카드 + 오른쪽 기둥의 띠 · 지닌물건 글, 목업 B-battle).
    /// 마우스를 받지 않는 카드라 효과 글은 띠(구운 글)와 지닌물건 글 상자가 다 보인다. 카드 그림이 없으면(웹 손님판 · 짝 없는 카드) 도트 카드(도감 상세 쪽과 같은 값 · 같은 글 키).
    /// 공개 정보 카드(<see cref="InfoCard"/>)와 같은 순서로 선다: 무대 뿌리에 붙고, 크기가 정해지면 대상 옆에 자리를 잡아 보인다(화면에 넘치면 카드를 줄여 다시 그린다).
    /// 요소 이름: <c>mon-tip</c>(떠 있는 상자)·<c>mon-tip-rate</c>(야생 카드의 포획률) — 카드 안은 <see cref="CardStack"/>의 이름, 도트 카드는 <c>mon-tip-dot</c>·<c>mon-tip-name</c>·<c>mon-tip-move</c>·<c>mon-tip-move-n</c>.
    /// </summary>
    public sealed class MonTip
    {
        /// <summary>카드 폭(카드 한 장 = 16 cm — 패널 px, USS <c>.kit-root</c>의 값 · 목업 B 크기 표). 화면에 넘치면 이보다 작게 그린다.</summary>
        static readonly CustomStyleProperty<float> MonWidth = new CustomStyleProperty<float>("--sn-card-tip-w"), WildWidth = new CustomStyleProperty<float>("--sn-card-wild-w");

        readonly UiStage stage;
        readonly Ctx ctx;
        readonly VisualElement card, dot;
        readonly MonArt mons;
        VisualElement target, clear, over;
        TipSide side;
        Action<float> paint;
        float width;
        int shrunk;

        /// <summary>떠 있는 상자(<c>mon-tip</c>).</summary>
        public VisualElement Element => card;

        /// <summary>지금 떠 있는 카드(포켓몬 표 색인 — 없으면 −1).</summary>
        public int Shown { get; private set; } = -1;

        /// <summary>지금 그린 원작 카드(도트 카드면 null — 캡처 · 시험).</summary>
        public CardStack Stack { get; private set; }

        public MonTip(UiStage stage, Ctx ctx)
        {
            this.stage = stage;
            this.ctx = ctx;
            mons = MonArt.Load();
            card = new VisualElement { name = "mon-tip", pickingMode = PickingMode.Ignore };
            card.AddToClassList("sn-card-tip");
            card.style.display = DisplayStyle.None;
            card.RegisterCallback<GeometryChangedEvent>(e => Place());
            dot = Kit.Panel(stage.Skin);
            dot.name = "mon-tip-dot";
            dot.AddToClassList("sn-mon-tip");
            dot.pickingMode = PickingMode.Ignore;
        }

        /// <summary>그 요소에 마우스를 올리면 카드를 띄운다. clear = 덮지 않을 판(그 판의 옆에 선다 — 없으면 요소 옆).</summary>
        public void Hook(VisualElement el, int def, TipSide side, VisualElement clear = null)
        {
            el.RegisterCallback<PointerEnterEvent>(e => Show(el, def, side, clear));
            el.RegisterCallback<PointerLeaveEvent>(e => { if (target == el) Hide(); });
            el.RegisterCallback<DetachFromPanelEvent>(e => { if (target == el) Hide(); });
        }

        /// <summary>그 요소 옆에 그 야생 카드를 띄운다(포인터 콜백 · 캡처 · 시험).</summary>
        public void Show(VisualElement el, int def, TipSide side, VisualElement clear = null)
        {
            if (el == null || stage.Root == null || def < 0 || def >= ctx.Cards.Pokemon.Count) return;
            target = el;
            this.clear = clear;
            over = null;
            this.side = side;
            Shown = def;
            paint = w => PaintWild(def, w);
            Open(WildWidth);
        }

        /// <summary>
        /// 내 포켓몬 한 마리의 카드(결정 169 — 배틀 독의 포켓몬 고르기 줄 · 무리 · 명단 칸): 정비 판 카드 확대(<see cref="MonScreen"/>)와 같은 짜임 — 카드(지금 체력/최대 · 스피드 · 상태 · 토큰) +
        /// 오른쪽 기둥(배운 기술 · 균열 · 전술 판 띠 · 지닌물건 글). 모두 보기의 공개 값이다. hp · speed = 화면이 그리는 지금 값.
        /// 카드는 <paramref name="on"/> 판의 아래 끝에 바닥을 맞춰 얹는다(판보다 넓으면 판 오른쪽 끝에 맞춰 왼쪽으로 — 배틀은 기록 기둥 위에서 DS 쪽, 독을 가리지 않게).
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
            paint = w => PaintMon(pk, hp, speed, w);
            Open(MonWidth);
        }

        void Open(CustomStyleProperty<float> want)
        {
            var root = stage.Root;
            // 카드 폭(USS — 등급 벌마다). 뿌리 스타일이 아직 풀리지 않았으면 720 벌 값을 등급에 맞춘다
            width = root.customStyle.TryGetValue(want, out float w) && w > 0f ? w : 288f * stage.Grade.TokenHeight / 720f;
            shrunk = 0;
            if (card.parent != root) root.Add(card);
            paint(width);
            card.BringToFront();
            stage.Tips.Element.BringToFront();   // 툴팁은 카드 위
            card.style.visibility = Visibility.Hidden;   // 크기가 정해지면(GeometryChanged) 자리를 잡아 보인다
            card.style.display = DisplayStyle.Flex;
            Place();
        }

        void PaintWild(int def, float w)
        {
            card.Clear();
            Stack = null;
            var mon = CardMon.Wild(ctx, def);
            if (!CardStack.CanDraw(stage, ctx, mon))
            {
                Fill(ctx.Cards.Pokemon[def], def);
                card.Add(Inline());
                return;
            }
            Stack = CardStack.Build(stage, ctx, mon, w);
            card.Add(Stack.Root);
            // 카드에 없는 값 = 포획률(도감 · 조우 창과 같은 말)
            var inv = CultureInfo.InvariantCulture;
            int rate = DexBook.CatchRate(ctx, def);
            // 잡을 수 없으면 「포획 불가」 한 마디(「포획률 포획 불가」로 겹치지 않게 — 검토 지적 10)
            string text = rate > 0 ? stage.T("screen.dex.rate") + " " + stage.T("screen.dex.rate.value").Replace("{n}", rate.ToString(inv)) : stage.T("screen.dex.rate.none");
            var chip = Kit.Badge(text, rate > 0 ? KitBadgeKind.Gold : KitBadgeKind.Plain, Kit.F2);
            chip.name = "mon-tip-rate";
            chip.AddToClassList("sn-card-tip-rate");
            card.Add(chip);
            Unpick(card);
        }

        void PaintMon(PokemonState pk, int hp, int speed, float w)
        {
            card.Clear();
            Stack = null;
            var mon = CardMon.Of(ctx, pk, hp, speed);
            if (!CardStack.CanDraw(stage, ctx, mon))
            {
                FillMon(pk, hp, speed);
                card.Add(Inline());
                return;
            }
            Stack = CardStack.Build(stage, ctx, mon, w, CardLayout.Spread);
            card.Add(Stack.Root);
            Unpick(card);
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

        /// <summary>띄우지 않고 내 포켓몬 도트 카드 요소만(원작 카드 그림이 없을 때 <see cref="CardStack"/>가 대신 쓴다 — 웹 손님판 · 짝 없는 카드).</summary>
        internal VisualElement Dot(PokemonState pk, int hp, int speed)
        {
            FillMon(pk, hp, speed);
            return Inline();
        }

        /// <summary>띄우지 않고 야생 도트 카드 요소만(<see cref="Dot"/>의 야생 카드 판).</summary>
        internal VisualElement DotWild(int def)
        {
            Fill(ctx.Cards.Pokemon[def], def);
            return Inline();
        }

        VisualElement Inline()
        {
            dot.style.position = Position.Relative;
            dot.style.display = DisplayStyle.Flex;
            Unpick(dot);
            return dot;
        }

        void Fill(PokemonDef def, int index)
        {
            var inv = CultureInfo.InvariantCulture;
            dot.Clear();
            var head = Add(dot, new VisualElement(), null, "sn-mon-tip-head");
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
            Add(dot, Kit.Text(stage.T("screen.mon.sig"), Kit.F2), null, "kit-muted");
            var move = Add(dot, new VisualElement(), "mon-tip-move", "sn-mon-move");
            move.Add(MonParts.MoveRow(stage, ctx, def.Signature, true, null));
            string text = stage.TOrNull(BattleFeed.MoveNameKey(ctx, def.Signature) + ".text");
            if (text != null) Add(move, Kit.Wrapped(text, Kit.F2), null, "sn-mon-text");
        }

        void FillMon(PokemonState pk, int hp, int speed)
        {
            var inv = CultureInfo.InvariantCulture;
            var def = ctx.Cards.Pokemon[pk.Def];
            dot.Clear();
            var head = Add(dot, new VisualElement(), null, "sn-mon-tip-head");
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
            Add(dot, Kit.Text(stage.T("screen.mon.sig"), Kit.F2), null, "kit-muted");
            MonMove(0, def.Signature, true, -1);
            Add(dot, Kit.Text(stage.T("screen.mon.learned"), Kit.F2), null, "kit-muted");
            for (int k = 0; k < pk.Moves.Length; k++) MonMove(k + 1, pk.Moves[k], false, k < pk.Breach.Length ? pk.Breach[k] : -1, pk);
            // 지닌물건(있을 때만 — 우두머리 토큰 · 아이템 둘째 칸)
            foreach (int held in new[] { pk.HeldItem, pk.HeldItem2 })
            {
                if (held < 0 || held >= ctx.Items.Items.Count) continue;
                string key = ctx.Items.Items[held].Key;
                var item = Add(dot, new VisualElement(), "mon-tip-item", "sn-mon-item");
                var itemHead = Add(item, new VisualElement(), null, "sn-hud-line");
                string icon = WorkUi.IconKey(ctx.Items.Items[held]);
                if (icon != null) WorkUi.Icon(itemHead, icon, "sn-icon");
                Add(itemHead, Kit.Text(stage.T(key), Kit.F2), null, "kit-title");
                string text = stage.TOrNull(key + ".text");
                if (text != null) item.Add(Kit.Wrapped(text, Kit.F2));
            }
            if (pk.Alpha)
            {
                var alpha = Add(dot, new VisualElement(), "mon-tip-alpha", "sn-mon-item");
                string text = stage.TOrNull("item.alpha_pokemon.text");
                Add(alpha, Kit.Text(stage.T("item.alpha_pokemon"), Kit.F2), null, "kit-title");
                if (text != null) alpha.Add(Kit.Wrapped(text, Kit.F2));
            }
        }

        // 기술 칸 하나: 기술 줄 + 효과 문구 · 균열 카드가 막은 칸 · 빈 칸
        void MonMove(int at, int move, bool star, int breach, PokemonState pk = null)
        {
            var box = Add(dot, new VisualElement(), "mon-tip-move-" + at.ToString(CultureInfo.InvariantCulture), "sn-mon-move");
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
            Rect pane = root.layout;
            if (over != null && over.panel != null)
            {
                // 그 판의 아래 끝에 바닥을 맞추고 위로(화면 위 끝까지). 판 폭 안이면 가운데, 넓으면 판 오른쪽 끝에 맞춰 왼쪽으로(목업 B-battle — DS 쪽을 덮는다)
                Rect box = root.WorldToLocal(over.worldBound);
                if (Shrink(box.yMax - InfoCard.Gap, pane.width - 2f * InfoCard.Gap, size)) return;
                float x = size.width <= box.width ? box.x + (box.width - size.width) / 2f : box.xMax - size.width;
                card.style.left = Mathf.Round(Mathf.Clamp(x, 0f, Mathf.Max(0f, pane.width - size.width)));
                card.style.top = Mathf.Round(Mathf.Max(0f, box.yMax - size.height));
                card.style.visibility = Visibility.Visible;
                return;
            }
            if (Shrink(pane.height - 2f * InfoCard.Gap, pane.width - 2f * InfoCard.Gap, size)) return;
            Rect at = root.WorldToLocal(target.worldBound);
            if (clear != null && clear.panel != null)
            {
                // 판을 덮지 않게: 가로는 판의 폭, 세로는 대상의 자리
                Rect box = root.WorldToLocal(clear.worldBound);
                at = new Rect(box.x, at.y, box.width, at.height);
            }
            var spot = TipPlace.Place(at.x, at.y, at.width, at.height, size.width, size.height, pane.width, pane.height, side, InfoCard.Gap);
            card.style.left = Mathf.Round(spot.X);
            // 아래 끝(포획률 칩)이 화면 아래 끝에 걸리지 않게 여백 몫을 남긴다(검토 지적 10)
            card.style.top = Mathf.Round(Mathf.Max(InfoCard.Gap, Mathf.Min(spot.Y, pane.height - InfoCard.Gap - size.height)));
            card.style.visibility = Visibility.Visible;
        }

        // 원작 카드가 자리(높이 · 폭)를 넘으면: 길어진 기둥의 글 상자를 옆 기둥으로 먼저 옮기고, 그래도 넘치면 그 비율로 줄여 다시 그린다(글 상자 글은 줄지 않으니 몇 번까지)
        // — 바꿨으면 참(배치가 끝나면 다시 Place)
        bool Shrink(float maxH, float maxW, Rect size)
        {
            if (Stack == null || !Stack.Art || shrunk >= 4) return false;
            float k = Mathf.Min(maxH / size.height, maxW / size.width);
            if (k >= 1f) return false;
            if (size.height > maxH && Stack.SplitColumn()) return true;
            shrunk++;
            width = Mathf.Floor(width * Mathf.Min(k, 0.98f));
            paint(width);
            return true;
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
