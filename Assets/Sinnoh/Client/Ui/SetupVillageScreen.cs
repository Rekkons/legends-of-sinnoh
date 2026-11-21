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
    /// S12 고향 마을 고르기(04 §1·§2·§7 — 목업 r4/setup ②, 01 §3-5): 엔진이 연 선택(<see cref="ChoiceKind.VillagePick"/>)의 합법 답을 그대로 줄로 올린다.
    /// 줄 = 마을 색 · 이름 · 스타팅 포켓몬 둘의 이름. 줄을 누르면 아래 판에 마을 카드 그림과 스타팅 포켓몬 둘이 보이고, [확정]이 그 마을을 답으로 낸다(되돌릴 수 없다).
    /// 고른 마을의 색이 좌석 색이 되고 말이 그 마을에 선다 — [확정] 뒤 이 화면이 걷히고 카메라가 그 마을로 다가가 말이 떨어져 내려앉는다(04 §8-8 2행 — <see cref="SetupDropScreen"/>).
    /// 뒤의 보드 전경은 그대로 보인다(말은 아직 없다). 화면 대기열이 도는 동안(여럿이 하는 판의 남의 마을 연출)에는 고르기·[확정]이 듣지 않는다.
    /// 이름은 문자열 표(<c>village.*</c>·<c>pokemon.*</c>), 그림은 Gen(<c>Cards/Village/</c>, 정지 도트 아틀라스). 마을 색은 좌석 색 표(<see cref="SeatColors"/>)의 값이다(화면 설계 색이 아니라 데이터).
    /// <para>마을 자리 표시(결정 106): 뒤의 보드 전경 위, 고를 수 있는 마을 칸마다 마을 색 점(<c>setup-village-pin-&lt;마을&gt;</c>)을 찍고
    /// 줄을 가리키거나(<see cref="Hover"/>) 고른 마을(<see cref="Picked"/>)의 점은 금색 고리 + 이름표로 키운다(<see cref="Marked"/> — 둘이 다르면 둘 다). 자리는 프레임마다 카메라로 비춘 칸 가운데(<see cref="GameFlow.CellRect"/>) —
    /// 보드 없이 도는 흐름이면 점을 만들지 않는다. 이미 남이 고른 마을은 합법 답에 없어 점이 없고 그 좌석의 말이 서 있다.</para>
    /// 요소 이름: <c>setup-village</c>·<c>setup-village-list</c>·<c>setup-village-item-&lt;마을&gt;</c>·<c>setup-village-detail</c>·<c>setup-village-back</c>·<c>setup-village-confirm</c>·<c>setup-village-map</c>·<c>setup-village-pin-&lt;마을&gt;</c>(+ <c>-name</c>).
    /// </summary>
    public sealed class SetupVillageScreen : IUiScreen, IOverBoard
    {
        /// <summary>판 오른쪽 변(720 토큰 px — <c>sn-setup-col</c>의 left 60 + width 320).</summary>
        public const float ColumnRight = 380f;

        public const string ArtDir = "Cards/Village/";

        readonly GameFlow flow;
        readonly List<int> villages = new List<int>();
        readonly List<VisualElement> rows = new List<VisualElement>();
        readonly List<VisualElement> pins = new List<VisualElement>();
        VisualElement map, page, ask;
        UiStage stage;
        MonArt mons;
        VisualElement detail;
        Button confirm;

        /// <param name="watch">보는 좌석(결정 120 ① · 플레이 시험 T2 — 남이 고향 마을을 고르는 동안 같은 화면을 보기만, −1 = 내가 고른다). 보기 화면은 머리 줄이 「○○이 고르는 중」이고 [확정]이 없다 —
        /// 줄을 눌러 그 마을의 스타팅 포켓몬을 내 화면에서만 훑어볼 수 있다(답을 내지 않는다). 그 좌석이 고르면 고향 마을 연출(2행)로 넘어간다.</param>
        public SetupVillageScreen(GameFlow flow, int watch = -1)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.watch = watch;
        }

        readonly int watch;
        MonTip monTip;

        /// <summary>보는 좌석(−1 = 내가 고르는 화면).</summary>
        public int Watched => watch;

        /// <summary>그 좌석이 아직 답하지 않은 고향 마을 고르기(공개 — 없으면 null).</summary>
        public static PendingChoice PickOf(GameState view, int seat)
        {
            if (view == null) return null;
            foreach (var c in view.Pending)
                if (c.Seat == seat && !c.Answered && c.Kind == ChoiceKind.VillagePick && !c.Options.Contains(Engine.Unknown)) return c;
            return null;
        }

        public string Name => "setup-village";

        public float PanelRight => ColumnRight + SoloScreen.EnWiden(flow);

        /// <summary>고를 수 있는 마을(고정물 색인, 엔진의 합법 답 순서).</summary>
        public IReadOnlyList<int> Villages => villages;

        /// <summary>지금 고른 줄(<see cref="Villages"/> 색인). 아직 없으면 −1.</summary>
        public int Picked { get; private set; } = -1;

        /// <summary>마우스로 가리킨 줄(<see cref="Villages"/> 색인, 없으면 −1) — 그 마을의 자리 점이 커진다(결정 106).</summary>
        public int Hover { get; private set; } = -1;

        /// <summary>보기 화면: 그 좌석이 지금 가리키는 마을 줄(방 판의 「남이 가리키는 것」 — 결정 120 · Protocol 10, 없으면 −1).</summary>
        public int PeekShown { get; private set; } = -1;

        /// <summary>보드 위에 크게 표시한 마을인가(가리킨 줄 · 고른 줄 — 둘이 다르면 둘 다).</summary>
        public bool Marked(int index) => index >= 0 && (index == Hover || index == Picked || index == PeekShown);

        /// <summary>지금 보드 위에 찍힌 자리 점의 수(보드가 없거나 칸이 카메라 밖이면 0).</summary>
        public int PinsShown { get; private set; }

        /// <summary>마을 고정물 키(<c>village.x</c>) → 마을 카드 그림 Gen 키.</summary>
        public static string ArtKey(string villageKey) => ArtDir + villageKey.Substring("village.".Length) + ".png";

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            villages.Clear();
            rows.Clear();
            pins.Clear();
            int was = Picked;
            Picked = -1;
            Hover = -1;
            PeekShown = -1;
            mons = MonArt.Load();
            var ctx = flow.Ctx;
            var choice = watch >= 0 ? PickOf(flow.Client.View, watch) : flow.Client.Choice(ChoiceKind.VillagePick);
            if (choice != null)   // 보기면 그 좌석의 합법 답(남은 마을 — 보기 사본에서 같은 엔진 함수로 센다)
                foreach (var a in flow.Client.Legal(choice)) if (a.Kind == AnswerKind.Pick) villages.Add(a.Target);
            monTip?.Detach();
            monTip = new MonTip(stage, ctx);

            page = new VisualElement();
            ask = null;
            map = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "setup-village-map", "sn-village-map");
            var col = Add(page, new VisualElement(), "setup-village-col", "sn-setup-col");
            Add(col, Kit.Text(stage.T("screen.setup.title"), Kit.FTitle), null, "sn-head");
            var guide = Add(col, Kit.Text(watch >= 0 ? KoJosa.Fix(stage.T("screen.setup.village.watch").Replace("{name}", flow.SeatName(watch))) : stage.T("screen.setup.village.guide"), Kit.F2), "setup-village-guide", "sn-guide");
            stage.Tips.Hook(guide, null, stage.T("screen.setup.village.tip"), TipSide.Below, reading: true);
            if (watch >= 0) FaceBox.Owner(flow, watch, guide);

            var list = Add(col, Kit.Panel(stage.Skin), "setup-village-list", "sn-setup-list");
            for (int i = 0; i < villages.Count; i++)
            {
                int index = i;
                var f = ctx.Fixtures.Fixtures[villages[i]];
                var row = Add(list, new VisualElement(), "setup-village-item-" + f.Key.Substring("village.".Length), "sn-setup-item");
                var chip = Add(row, new VisualElement(), null, "sn-seat-chip");
                chip.style.backgroundColor = new StyleColor((Color)SeatColors.ForVillage(f.Key));
                Add(row, Kit.Text(stage.T(f.Key), Kit.F2), null, "sn-setup-item-name");
                var names = new List<string>();
                foreach (int p in f.Starters) names.Add(stage.T("pokemon." + ctx.Cards.Pokemon[p].Key));
                Add(row, Kit.Text(string.Join(" · ", names), Kit.F2), null, "kit-muted");
                stage.OnTap(row, SoundKeys.Select, e => Pick(index), () => watch >= 0 || flow.SetupOpen);
                row.RegisterCallback<PointerEnterEvent>(e => Point(index));
                row.RegisterCallback<PointerLeaveEvent>(e => { if (Hover == index) Point(-1); });
                rows.Add(row);
                // 자리 점(결정 106): 마을 색 점 + 이름표(강조했을 때만 보인다)
                var pin = Add(map, new VisualElement { pickingMode = PickingMode.Ignore }, "setup-village-pin-" + f.Key.Substring("village.".Length), "sn-village-pin");
                pin.style.backgroundColor = new StyleColor((Color)SeatColors.ForVillage(f.Key));
                pin.style.display = DisplayStyle.None;
                var tag = Add(pin, Kit.Text(stage.T(f.Key), Kit.F1), pin.name + "-name", "sn-village-pin-name");
                tag.pickingMode = PickingMode.Ignore;
                pins.Add(pin);
            }

            detail = Add(col, Kit.Panel(stage.Skin), "setup-village-detail", "sn-setup-detail");

            var foot = Add(col, new VisualElement(), "setup-village-foot", "sn-foot-row");
            // 방 판(M7 화면 ②): [뒤로](솔로 설정으로) 대신 [방에서 나가기] · 방장 [방 닫기] — 셋업에는 게임 메뉴가 없다
            var room = flow.Room.InGame ? flow.Room.Live : null;
            string backKey = room == null ? "screen.common.back" : room.IsHost ? "screen.room.lobby.close" : "screen.room.menu.leave";
            Add(foot, Kit.Button(stage.T(backKey), KitButtonKind.Normal, Back), "setup-village-back", null);
            confirm = Add(foot, Kit.Button(stage.T("screen.common.confirm"), KitButtonKind.Main, () => Confirm()), "setup-village-confirm", null);
            confirm.SetEnabled(false);
            if (watch >= 0) confirm.style.display = DisplayStyle.None;   // 보기 화면은 답을 내지 않는다
            if (was >= 0 && was < villages.Count) Pick(was);   // 패널이 뿌리를 다시 줘 다시 만들 때 고른 줄을 잇는다
            else ShowDetail();
            flow.Ticked -= PlacePins;
            flow.Ticked += PlacePins;
            return page;
        }

        public void Close()
        {
            flow.Ticked -= PlacePins;
            monTip?.Detach();
            monTip = null;
        }

        /// <summary>줄을 가리킨다(마우스 오버 — −1 = 뗌): 그 마을의 자리 점을 키운다. 고르지는 않는다.</summary>
        public void Point(int index)
        {
            Hover = index >= 0 && index < rows.Count ? index : -1;
            if (watch < 0) flow.Room.Peek(Sinnoh.Rules.Session.PeekSpot.Village, Hover >= 0 ? villages[Hover] : -1);   // 방 판: 내가 가리키는 마을(남의 같은 화면에 — 결정 120)
            PlacePins();
        }

        /// <summary>자리 점을 카메라로 비춘 마을 칸 가운데에 놓고 강조한 마을의 점을 키운다(프레임마다 — 카메라·창 크기를 따라간다).</summary>
        public void PlacePins()
        {
            if (map == null || map.panel == null) return;
            if (watch >= 0) PaintPeek();
            int shown = 0;
            for (int i = 0; i < pins.Count; i++)
            {
                var pin = pins[i];
                bool on = flow.CellRect(flow.Ctx.Fixtures.Fixtures[villages[i]].Cell, out var rect);
                pin.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                shown++;
                var at = map.WorldToLocal(rect.center);
                pin.style.left = Mathf.Round(at.x);
                pin.style.top = Mathf.Round(at.y);
                pin.EnableInClassList("sn-village-pin-on", Marked(i));
                if (i == (Hover >= 0 ? Hover : Picked) && map.IndexOf(pin) != map.childCount - 1) pin.BringToFront();
            }
            PinsShown = shown;
        }

        // 보기 화면: 그 좌석이 가리키는 마을(방 판 소식 — 결정 120)의 줄에 좌석 색 테두리 · 점을 키운다
        void PaintPeek()
        {
            var m = flow.PeekOf(watch, Sinnoh.Rules.Session.PeekSpot.Village);
            int pointed = m != null ? villages.IndexOf(m.Index) : -1;
            if (pointed == PeekShown) return;
            PeekShown = pointed;
            var tone = new StyleColor((Color)flow.SeatColor(watch));
            for (int i = 0; i < rows.Count; i++)
            {
                bool on = i == pointed;
                rows[i].EnableInClassList(PeekClass, on);
                var color = on ? tone : new StyleColor(StyleKeyword.Null);
                rows[i].style.borderLeftColor = rows[i].style.borderRightColor = rows[i].style.borderTopColor = rows[i].style.borderBottomColor = color;
            }
        }

        /// <summary>남이 가리키는 마을 줄의 클래스.</summary>
        public const string PeekClass = "sn-setup-item-peek";

        /// <summary>줄을 고른다(눌렀다): 노란 바탕 + 아래 판에 그 마을. 아직 답을 내지 않는다.</summary>
        public void Pick(int index)
        {
            if (index < 0 || index >= rows.Count || (watch < 0 && !flow.SetupOpen)) return;
            if (Picked >= 0) rows[Picked].RemoveFromClassList("sn-picked");
            Picked = index;
            rows[index].AddToClassList("sn-picked");
            confirm.SetEnabled(watch < 0);
            if (watch < 0) flow.Room.Peek(Sinnoh.Rules.Session.PeekSpot.Village, villages[index], true);
            ShowDetail();
            PlacePins();
        }

        /// <summary>[확정]: 고른 마을을 엔진에 낸다. 고른 줄이 없거나 거절되면 false.</summary>
        public bool Confirm()
        {
            if (Picked < 0 || watch >= 0 || !flow.SetupOpen) return false;
            var choice = flow.Client.Choice(ChoiceKind.VillagePick);
            return choice != null && flow.Answer(choice, ChoiceAnswer.Pick(villages[Picked])).Accepted;
        }

        /// <summary>[뒤로]: 이 게임을 그만두고 솔로 설정으로(아직 아무것도 고르지 않았다). 방 판이면 게임 메뉴의 나가기와 같은 물음 창을 먼저 띄운다(결정 112 Q8 — <see cref="Leave"/>).</summary>
        public void Back()
        {
            if (!flow.Room.InGame)
            {
                flow.OpenSolo();
                return;
            }
            if (Asking || page == null) return;
            var live = flow.Room.Live;
            bool host = live != null && live.IsHost;
            ask = Add(page, new VisualElement(), "setup-village-ask", "sn-screen");
            Add(ask, new VisualElement(), null, "kit-scrim");   // 뒤의 고르기를 누르지 못하게
            var panel = Add(ask, Kit.Panel(stage.Skin), "setup-village-ask-panel", "sn-menu-panel");
            panel.AddToClassList("sn-menu-panel-room");
            Add(panel, Kit.Text(stage.T(host ? "screen.room.menu.close.ask" : "screen.room.menu.leave.ask"), Kit.F2), null, null);
            Add(panel, Kit.Wrapped(stage.T(host ? "screen.room.setup.close.note" : "screen.room.menu.leave.note"), Kit.F2), "setup-village-ask-note", "kit-muted");
            var row = Add(panel, new VisualElement(), null, "sn-foot-row");
            Add(row, Kit.Button(stage.T(host ? "screen.room.lobby.close" : "screen.room.lobby.leave"), KitButtonKind.End, Leave), "setup-village-ask-yes", null);
            Add(row, Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, CancelLeave), "setup-village-ask-no", null);
        }

        /// <summary>방 판의 나가기 물음 창이 떠 있나(결정 112 Q8).</summary>
        public bool Asking => ask != null && ask.parent != null;

        /// <summary>물음 창의 [방에서 나가기] · [방 닫기]: 방에서 나간다(방장 = 방 닫기 — 마지막 쓰기, 02 §6-3) → 타이틀.</summary>
        public void Leave()
        {
            ask?.RemoveFromHierarchy();
            if (flow.Room.InGame) flow.Room.Leave();
        }

        /// <summary>물음 창의 [취소]: 창을 닫고 고르기로.</summary>
        public void CancelLeave() => ask?.RemoveFromHierarchy();

        // 아래 판: 고른 마을(색 · 이름 · 카드 그림 · 스타팅 포켓몬 둘). 고르기 전에는 같은 자리를 비워 두고 안내만 적는다 — 고르는 순간 판 크기가 바뀌지 않게 같은 틀을 쓴다.
        void ShowDetail()
        {
            detail.Clear();
            var ctx = flow.Ctx;
            var f = Picked >= 0 ? ctx.Fixtures.Fixtures[villages[Picked]] : null;
            var head = Add(detail, new VisualElement(), null, "kit-row");
            var chip = Add(head, new VisualElement(), null, "sn-seat-chip");
            if (f != null) chip.style.backgroundColor = new StyleColor((Color)SeatColors.ForVillage(f.Key));
            else chip.style.visibility = Visibility.Hidden;
            Add(head, Kit.Text(f != null ? stage.T(f.Key) : "", Kit.F2), null, "kit-title");
            var body = Add(detail, new VisualElement(), null, "sn-setup-detail-body");
            var art = Add(body, new VisualElement(), "setup-village-art", "sn-village-art");
            var tex = f != null ? GenAssets.Load<Texture2D>(ArtKey(f.Key)) : null;
            if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
            for (int i = 0; i < 2; i++)
            {
                int index = f != null && i < f.Starters.Count ? f.Starters[i] : -1;
                var def = index >= 0 ? ctx.Cards.Pokemon[index] : null;
                var tile = Add(body, new VisualElement(), "setup-village-starter-" + (i + 1), "sn-mon-tile");
                if (def != null && mons != null) tile.Add(mons.Front(def.Key));
                else Add(tile, new VisualElement(), null, "sn-mon");
                Add(tile, Kit.Text(def != null ? stage.T("pokemon." + def.Key) : "", Kit.F2), null, null);
                if (def == null) continue;
                // 스타팅 스펙(T3 — 이 게임 데이터): 타입 · 체력 · 스피드 · ★ 고유 기술(이름 · 위력). 자세한 것(기술 효과 · 배울 수 있는 타입)은 마우스 오버 카드
                var types = Add(tile, new VisualElement(), "setup-village-starter-" + (i + 1) + "-types", "sn-setup-spec");
                foreach (var t in def.Types) MonParts.TypeIcon(types, t);
                var stats = Add(tile, new VisualElement(), "setup-village-starter-" + (i + 1) + "-stats", "sn-setup-spec");
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                stats.Add(Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.HealthIconKey), def.Health.ToString(inv), KitTone.Base, def.Health.ToString(inv)));
                stats.Add(Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.SpeedIconKey), def.Initiative.ToString(inv)));
                monTip?.Hook(tile, index, TipSide.Right, detail);
            }
            // ★ 고유 기술 줄(타입 · 이름 · 위력 — 효과 글은 줄의 마우스 오버): 타일 아래 판 폭으로 하나씩(타일이 좁아 이름이 넘친다)
            for (int i = 0; f != null && i < 2 && i < f.Starters.Count; i++)
                Add(detail, MonParts.MoveRow(stage, ctx, ctx.Cards.Pokemon[f.Starters[i]].Signature, true, TipSide.Right, detail), "setup-village-starter-" + (i + 1) + "-move", "sn-setup-spec-row");
            if (f == null) Add(body, Kit.Wrapped(stage.T("screen.setup.village.tip"), Kit.F1), "setup-village-hint", "sn-setup-hint").AddToClassList("kit-muted");
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
