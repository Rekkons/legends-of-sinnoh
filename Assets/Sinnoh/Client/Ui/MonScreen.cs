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
    /// S38 포켓몬 카드 확대(04 §3-3 「누름 — 확대 S38(카드 고정 값)·기술 효과」): 정비 판(<see cref="PartyScreen"/>)의 포켓몬 카드를 누르면 뜨는 큰 보기.
    /// 결정 206 ①(목업 B-party): 원작 TTS 카드를 크게 — 카드(<see cref="CardStack"/> 옆으로 펼친 짜임: 지금 체력/최대 · 스피드 · 상태 주사위 · 토큰) + 오른쪽 기둥(배운 기술 · 균열 · 전술 판 띠 ·
    /// 지닌물건 글 상자) + 카드에 없는 값(진화할 포켓몬 · 잡은 볼)과 [닫기]. 카드의 자리(★ 고유 기술 · 띠 · 토큰 · 상태)에 마우스를 올리면 효과 글. 화면 높이에 넘치면 카드를 줄인다.
    /// 카드 그림이 없으면(짝 없는 카드) 예전 판(도트 + 사실 줄 + 기술 줄 — 효과 문구를 줄로 다 적는다)으로 그린다.
    /// 모두 보기에 있는 공개 정보라 남 좌석의 포켓몬도 같은 화면이다. 고치는 것은 없다. [닫기]·창 밖 누르기는 그 좌석의 정비 판으로 돌아간다(카드를 눌러서는 닫히지 않는다).
    /// 요소 이름: <c>mon-detail</c>·<c>mon-detail-panel</c>·<c>mon-detail-move-0</c>(★ 고유 기술 자리)·<c>mon-detail-evolve</c>·<c>mon-detail-ball</c>·<c>mon-detail-close</c> — 카드 안은 <see cref="CardStack"/>의 이름.
    /// 예전 판은 그 밖에 <c>mon-detail-name</c>·<c>mon-detail-move-&lt;1|2&gt;</c>·<c>mon-detail-item</c>.
    /// </summary>
    public sealed class MonScreen : IUiScreen
    {
        /// <summary>카드 폭(카드 한 장 = 16 cm — 패널 px, USS <c>.kit-root</c>의 값 · 목업 B-party 크기 표). 화면 높이에 넘치면 이보다 작게 그린다.</summary>
        static readonly CustomStyleProperty<float> ZoomWidth = new CustomStyleProperty<float>("--sn-card-zoom-w");

        readonly GameFlow flow;
        UiStage stage;
        VisualElement page, panel;
        int painted = -1, shrunk;
        float width, designWidth;

        /// <summary>지금 그린 원작 카드(예전 판이면 null — 캡처 · 시험).</summary>
        public CardStack Stack { get; private set; }

        /// <summary>보는 포켓몬(인스턴스 색인) · 그 주인 좌석(닫으면 그 좌석의 정비 판으로).</summary>
        public readonly int Pokemon, Seat;

        public MonScreen(GameFlow flow, int pokemon, int seat)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Pokemon = pokemon;
            Seat = seat;
        }

        public string Name => "mon-detail";

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 HUD)의 입력을 먹지 않는다 — 창 밖 누르기는 어둠이 받는다(T35)
            var scrim = new VisualElement { name = "mon-detail-scrim" };
            scrim.AddToClassList("sn-party-scrim");
            scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseMon());
            page.Add(scrim);
            painted = -1;
            width = designWidth = stage.Root != null && stage.Root.customStyle.TryGetValue(ZoomWidth, out float w) && w > 0f ? w : 352f * stage.Grade.TokenHeight / 720f;
            shrunk = 0;
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            page = null;
            panel = null;
            zoomHead = null;
            Stack = null;
        }

        /// <summary>보기가 바뀌었다: 다시 그린다. 그 포켓몬이 파티를 떠났으면(놓아주기 등) 정비 판으로 돌아간다.</summary>
        public void Refresh()
        {
            if (stage == null || page == null || flow.Client == null) return;
            var view = flow.Client.View;
            if (view == null || Seat < 0 || Seat >= view.Seats.Length || PokemonRules.PartyIndexOf(view.Seats[Seat], Pokemon) < 0)
            {
                flow.CloseMon();
                return;
            }
            if (painted == flow.Client.Version) return;
            painted = flow.Client.Version;
            Paint();
        }

        int Hp()
        {
            var pk = flow.Client.View.Pokemon[Pokemon];
            return flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Hp(Pokemon), PokemonRules.Hp(flow.Ctx, pk)) : PokemonRules.Hp(flow.Ctx, pk);
        }

        void Paint()
        {
            panel?.RemoveFromHierarchy();
            Stack = null;
            var mon = CardMon.Of(flow.Ctx, flow.Client.View, Pokemon, hp: Hp());
            if (CardStack.CanDraw(stage, flow.Ctx, mon)) Zoom(mon);
            else Plain();
        }

        // 원작 카드 확대(목업 B-party): 화면 가운데 카드 + 오른쪽 기둥 · [닫기]는 기둥 맨 아래
        void Zoom(CardMon mon)
        {
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            var pk = mon.Pk;
            var def = ctx.Cards.Pokemon[pk.Def];
            panel = new VisualElement { name = "mon-detail-panel", pickingMode = PickingMode.Ignore };
            panel.AddToClassList("sn-mon-zoom");
            page.Add(panel);
            // 카드 묶음(머리 줄의 [닫기] + 카드 + 기둥): 위 띠 아래 ~ 아래 판 위 자리 가운데 — [닫기]는 묶음 오른쪽 위(아래 판 단추를 덮지 않는다)
            var block = Add(panel, new VisualElement { pickingMode = PickingMode.Position }, "mon-detail-block", null);   // 묶음을 눌러도 창 밖 누르기(닫기)가 되지 않는다
            zoomHead = Add(block, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-card-zoomhead");
            var close = Add(zoomHead, Kit.Button(stage.T("screen.mon.close"), KitButtonKind.Main, () => flow.CloseMon()), "mon-detail-close", "sn-card-close");
            close.pickingMode = PickingMode.Position;
            Stack = CardStack.Build(stage, ctx, mon, width, CardLayout.Spread);
            block.Add(Stack.Root);
            Stack.HookTips(TipSide.Below);
            foreach (var part in Stack.Parts)
                if (part.Kind == CardPartKind.Signature) part.Element.name = "mon-detail-move-0";
            var col = Stack.Column;
            // 카드에 없는 값: 진화할 포켓몬(카드에는 비용만 — 「진화 → ○○」 칩, 없으면 「진화하지 않음」 한 마디) · 잡은 볼
            var facts = Add(col, new VisualElement(), null, "sn-card-facts");
            var evolve = Add(facts, new VisualElement(), "mon-detail-evolve", "sn-card-evolve");
            if (def.EvolveInto.Count == 0) Add(evolve, Kit.Text(stage.T("screen.mon.evolve.none"), Kit.F2), null, "kit-muted");
            else
            {
                foreach (int into in def.EvolveInto)
                    evolve.Add(Kit.Badge(stage.T("screen.mon.evolve.into").Replace("{name}", stage.T("pokemon." + ctx.Cards.Pokemon[into].Key)), KitBadgeKind.Plain, Kit.F2));
                if (def.EvolveCost.HasValue) evolve.Add(Kit.Badge(stage.T("screen.mon.evolve.cost").Replace("{n}", def.EvolveCost.Value.ToString(inv)), KitBadgeKind.Plain, Kit.F2));
            }
            if (pk.Ball >= 0) Ball(Fact(facts, "screen.mon.ball"), pk).name = "mon-detail-ball";
            panel.RegisterCallback<GeometryChangedEvent>(Fit);
        }

        VisualElement zoomHead;

        // 자리(위 띠 아래 ~ 아래 판 위)에 넘치면 한 규칙으로(목업 B — 글 상자는 띠 밑 · 묶음은 가운데): ① 카드 자체가 높으면 그만큼 줄인다
        // ② 그래도 기둥(띠 + 글 상자 + 진화 줄)이 넘치면 그 비율로 더 줄인다 — 카드가 처음 폭의 3/4 밑으로 줄어야 할 때만 띠 밑 상자를 옆 기둥으로. 글 상자 글은 줄지 않으니 몇 번 되풀이한다
        void Fit(GeometryChangedEvent e)
        {
            if (Stack == null || panel == null || shrunk >= 6) return;
            var box = panel.layout;
            var size = Stack.Root.layout;
            if (float.IsNaN(size.height) || size.height <= 0f || box.height <= 0f) return;
            float head = zoomHead != null && !float.IsNaN(zoomHead.layout.height) ? zoomHead.layout.height + zoomHead.resolvedStyle.marginBottom : 0f;
            float maxH = box.height - 2f * InfoCard.Gap - head, maxW = box.width - 2f * InfoCard.Gap;
            if (Stack.Height > maxH + 0.5f)
            {
                shrunk++;
                width = Mathf.Floor(width * Mathf.Min(maxH / Stack.Height, 0.98f));
                Paint();
                return;
            }
            float k = Mathf.Min(maxH / size.height, maxW / size.width);
            if (k >= 1f) return;
            // 줄여도 카드가 너무 작아지면(글 상자가 길어 줄일수록 길어진다 — 영어 · 1080 벌) 그때만 띠 밑 상자들을 옆 기둥으로
            if (width * k < designWidth * 0.75f && Stack.SplitColumn()) return;
            shrunk++;
            width = Mathf.Floor(width * Mathf.Min(k, 0.98f));
            Paint();
        }

        VisualElement Ball(VisualElement row, PokemonState pk)
        {
            var icon = Add(row, new VisualElement(), null, "sn-icon");
            var tex = GenAssets.Load<Texture2D>(GenAssets.ItemIcons + BattleFeed.BallItem(pk.Ball) + ".png");
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            row.Add(Kit.Text(stage.T(BattleFeed.BallKey(pk.Ball)), Kit.F2));
            return row;
        }

        // 예전 판(카드 그림이 없을 때 — 짝 없는 카드)
        void Plain()
        {
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            var view = flow.Client.View;
            var pk = view.Pokemon[Pokemon];
            var def = ctx.Cards.Pokemon[pk.Def];
            panel = Kit.Panel(stage.Skin);
            panel.name = "mon-detail-panel";
            panel.AddToClassList("sn-mon-panel");
            page.Add(panel);

            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            var title = Add(head, new VisualElement(), null, "sn-party-line");
            Add(title, Kit.Text(stage.T("pokemon." + def.Key), Kit.FTitle), "mon-detail-name", "kit-title");
            foreach (var t in def.Types) MonParts.TypeIcon(title, t);
            Add(head, Kit.Button(stage.T("screen.mon.close"), KitButtonKind.Main, () => flow.CloseMon()), "mon-detail-close", null);

            var body = Add(panel, new VisualElement(), null, "sn-mon-body");
            var art = Add(body, new VisualElement(), null, "sn-mon-art");
            var mons = MonArt.Load();
            art.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
            var facts = Add(body, new VisualElement(), null, "sn-mon-facts");

            var stats = Add(facts, new VisualElement(), null, "sn-party-line");
            int max = PokemonRules.MaxHp(ctx, pk);
            int hp = Hp();
            var hpPill = Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.HealthIconKey), hp.ToString(inv), hp * 2 <= max ? KitTone.Low : KitTone.Base, max.ToString(inv));
            var spPill = Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.SpeedIconKey), def.Initiative.ToString(inv));
            stats.Add(hpPill);
            stats.Add(spPill);
            stage.Tips.Hook(hpPill, stage.T("screen.common.health"), null, TipSide.Below);
            stage.Tips.Hook(spPill, stage.T("screen.common.speed"), null, TipSide.Below);
            string status = hp <= 0 ? "screen.info.fainted" : MonParts.StatusKey(pk.Status);
            if (status != null) stats.Add(Kit.Badge(stage.T(status), hp <= 0 ? KitBadgeKind.Red : KitBadgeKind.Warn, Kit.F2));
            MonParts.Passives(stage, Add(facts, new VisualElement(), null, "sn-party-line"), pk, true, "mon-detail-passive");   // 우두머리 · 비스트 부스트(T23 · T27)

            var learn = Fact(facts, "screen.party.learn");
            foreach (var t in def.LearnTypes) MonParts.TypeIcon(learn, t);
            // 서식지 · 등급(카드에 적힌 값 — 조우 창과 같은 말)
            string biome = def.Biome != Biome.None ? stage.TOrNull("screen.encounter.biome." + def.Biome.ToString().ToLowerInvariant()) : null;
            string climate = def.Climate != Climate.None ? stage.TOrNull("screen.encounter.climate." + def.Climate.ToString().ToLowerInvariant()) : null;
            if (biome != null || climate != null) Fact(facts, "screen.mon.habitat").Add(Kit.Text(biome != null && climate != null ? biome + " · " + climate : biome ?? climate, Kit.F2));
            string tier = stage.TOrNull("screen.encounter.grade." + def.Tier.ToString().ToLowerInvariant());
            if (tier != null) Fact(facts, "screen.mon.tier").Add(Kit.Text(tier, Kit.F2));
            var evolve = Add(facts, new VisualElement(), "mon-detail-evolve", "sn-party-line");   // 카드 확대와 같은 말(「진화 → ○○」 · 「진화하지 않음」)
            if (def.EvolveInto.Count == 0) Add(evolve, Kit.Text(stage.T("screen.mon.evolve.none"), Kit.F2), null, "kit-muted");
            else
            {
                foreach (int into in def.EvolveInto)
                    evolve.Add(Kit.Badge(stage.T("screen.mon.evolve.into").Replace("{name}", stage.T("pokemon." + ctx.Cards.Pokemon[into].Key)), KitBadgeKind.Plain, Kit.F2));
                if (def.EvolveCost.HasValue) evolve.Add(Kit.Badge(stage.T("screen.mon.evolve.cost").Replace("{n}", def.EvolveCost.Value.ToString(inv)), KitBadgeKind.Plain, Kit.F2));
            }
            if (pk.Ball >= 0) Ball(Fact(facts, "screen.mon.ball"), pk);

            // 기술: ★ 고유 기술 + 배운 칸(효과 문구를 줄로 적는다 — 큰 보기라 툴팁에 숨기지 않는다)
            Add(panel, Kit.Text(stage.T("screen.mon.sig"), Kit.F2), null, "kit-muted");
            Move(0, def.Signature, true);
            Add(panel, Kit.Text(stage.T("screen.mon.learned"), Kit.F2), null, "kit-muted");
            for (int k = 0; k < pk.Moves.Length; k++) Move(k + 1, pk.Moves[k], false, k < pk.Breach.Length ? pk.Breach[k] : -1, pk);

            // 지닌물건
            Add(panel, Kit.Text(stage.T("screen.mon.item"), Kit.F2), null, "kit-muted");
            var item = Add(panel, new VisualElement(), "mon-detail-item", "sn-mon-item");
            if (pk.Alpha)
            {
                // 우두머리 토큰(rulings A17 · T23): 뗄 수 없는 지닌물건 — 효과 문구를 줄로
                MonParts.AlphaToken(stage, Add(item, new VisualElement(), "mon-detail-alpha", "sn-hud-line"));
                string alphaText = stage.TOrNull("item.alpha_pokemon.text");
                if (alphaText != null) item.Add(Kit.Wrapped(alphaText, Kit.F2));
            }
            else if (pk.HeldItem < 0 && pk.HeldItem2 < 0) Add(item, Kit.Text(stage.T("screen.party.item.none"), Kit.F2), null, "kit-muted");
            foreach (int held in new[] { pk.HeldItem, pk.HeldItem2 })   // 둘째 칸 = backpacker R(rulings A39)
            {
                if (held < 0 || held >= ctx.Items.Items.Count) continue;
                string key = ctx.Items.Items[held].Key;
                var itemHead = Add(item, new VisualElement(), null, "sn-hud-line");   // 그림(지닌물건 · 유물 — 결정 80 L14 나) + 이름
                string icon = WorkUi.IconKey(ctx.Items.Items[held]);
                if (icon != null) WorkUi.Icon(itemHead, icon, "sn-icon");
                Add(itemHead, Kit.Text(stage.T(key), Kit.F2), null, "kit-title");
                string text = stage.TOrNull(key + ".text");
                if (text != null) item.Add(Kit.Wrapped(text, Kit.F2));
            }
        }

        void Move(int at, int move, bool star, int breach = -1, PokemonState pk = null)
        {
            var box = Add(panel, new VisualElement(), "mon-detail-move-" + at.ToString(CultureInfo.InvariantCulture), "sn-mon-move");
            if (move < 0 && breach >= 0)
            {
                // 균열 카드가 막은 칸(rulings A52 ①): 카드 그림 · 이름 + 효과 문구를 줄로(큰 보기라 툴팁에 숨기지 않는다)
                box.Add(MonParts.BreachRow(stage, flow.Ctx, breach, null));
                string key = flow.Ctx.Decks.CardIds[breach];
                Add(box, Kit.Wrapped(MonParts.BreachText(stage, key), Kit.F2), null, "sn-mon-text");
                return;
            }
            var legend = move < 0 && pk != null && at > 0 ? MonParts.TacticRow(stage, flow.Ctx, pk, at - 1, null) : null;
            if (legend != null)
            {
                // 전설 특수기술이 차지한 빈 칸(결정 195): 이름 · 타입 그림 + 효과 · 채우면 사라진다를 줄로(큰 보기라 툴팁에 숨기지 않는다)
                box.Add(legend);
                Add(box, Kit.Wrapped(MonParts.TacticTip(stage, LegendRules.TacticAt(pk, at - 1)), Kit.F2), null, "sn-mon-text");
                return;
            }
            if (move < 0)
            {
                var empty = Add(box, new VisualElement(), null, "sn-party-slot-empty");
                Add(empty, Kit.Text(stage.T("screen.party.slot.empty"), Kit.F2), null, "kit-muted");
                stage.Tips.Hook(empty, stage.T("screen.party.slot.kind"), stage.T("screen.party.slot.kind.tip"), TipSide.Below, reading: true);   // 결정 87 V5
                return;
            }
            box.Add(MonParts.MoveRow(stage, flow.Ctx, move, star, null));
            string text = stage.TOrNull(BattleFeed.MoveNameKey(flow.Ctx, move) + ".text");
            if (text != null) Add(box, Kit.Wrapped(text, Kit.F2), null, "sn-mon-text");
        }

        VisualElement Fact(VisualElement parent, string labelKey)
        {
            var row = Add(parent, new VisualElement(), null, "sn-party-line");
            Add(row, Kit.Text(stage.T(labelKey), Kit.F2), null, "kit-muted");
            return row;
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
