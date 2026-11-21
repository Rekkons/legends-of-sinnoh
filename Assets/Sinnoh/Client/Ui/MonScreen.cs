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
    /// 카드의 고정 값(타입 · 체력 · 스피드 · 배우는 타입 · 서식지 · 등급 · 진화와 그 비용)과 지금 값(체력 · 상태 · 잡은 볼), 기술 셋(★ 고유 기술 + 배운 칸 — 효과 문구를 줄로 다 적는다)과 지닌물건(문구)을 보인다.
    /// 모두 보기에 있는 공개 정보라 남 좌석의 포켓몬도 같은 화면이다. 고치는 것은 없다. [닫기]·창 밖 누르기는 그 좌석의 정비 판으로 돌아간다.
    /// 요소 이름: <c>mon-detail</c>·<c>mon-detail-panel</c>·<c>mon-detail-name</c>·<c>mon-detail-move-&lt;0|1|2&gt;</c>·<c>mon-detail-item</c>·<c>mon-detail-evolve</c>·<c>mon-detail-close</c>.
    /// </summary>
    public sealed class MonScreen : IUiScreen
    {
        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel;
        int painted = -1;

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
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 HUD)의 입력을 먹지 않는다 — 창 밖 누르기는 어둠이 받는다(T35)
            var scrim = new VisualElement { name = "mon-detail-scrim" };
            scrim.AddToClassList("sn-party-scrim");
            scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseMon());
            page.Add(scrim);
            panel = Kit.Panel(stage.Skin);
            panel.name = "mon-detail-panel";
            panel.AddToClassList("sn-mon-panel");
            page.Add(panel);
            painted = -1;
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
        }

        /// <summary>보기가 바뀌었다: 다시 그린다. 그 포켓몬이 파티를 떠났으면(놓아주기 등) 정비 판으로 돌아간다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null || flow.Client == null) return;
            var view = flow.Client.View;
            if (view == null || Seat < 0 || Seat >= view.Seats.Length || PokemonRules.PartyIndexOf(view.Seats[Seat], Pokemon) < 0)
            {
                flow.CloseMon();
                return;
            }
            if (painted == flow.Client.Version) return;
            painted = flow.Client.Version;
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            var pk = view.Pokemon[Pokemon];
            var def = ctx.Cards.Pokemon[pk.Def];
            panel.Clear();

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
            int hp = flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Hp(Pokemon), PokemonRules.Hp(ctx, pk)) : PokemonRules.Hp(ctx, pk);
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
            var evolve = Fact(facts, "screen.mon.evolve");
            evolve.name = "mon-detail-evolve";
            if (def.EvolveInto.Count == 0) Add(evolve, Kit.Text(stage.T("screen.mon.evolve.none"), Kit.F2), null, "kit-muted");
            else
            {
                foreach (int into in def.EvolveInto) evolve.Add(Kit.Text(stage.T("pokemon." + ctx.Cards.Pokemon[into].Key), Kit.F2));
                if (def.EvolveCost.HasValue) evolve.Add(Kit.Badge(stage.T("screen.mon.evolve.cost").Replace("{n}", def.EvolveCost.Value.ToString(inv)), KitBadgeKind.Plain, Kit.F2));
            }
            if (pk.Ball >= 0)
            {
                var ball = Fact(facts, "screen.mon.ball");
                var icon = Add(ball, new VisualElement(), null, "sn-icon");
                var tex = GenAssets.Load<Texture2D>(GenAssets.ItemIcons + BattleFeed.BallItem(pk.Ball) + ".png");
                if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
                ball.Add(Kit.Text(stage.T(BattleFeed.BallKey(pk.Ball)), Kit.F2));
            }

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
