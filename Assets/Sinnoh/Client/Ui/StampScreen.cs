using System;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S37 스탬프 판(04 §2 — M5 「스탬프·도전·거래 화면」, rulings A30): 보드 HUD의 [스탬프 n/7]로 여는 가운데 창(남 좌석의 것은 그 좌석 정비 판 S39의 「여행 스탬프 n/7」에서 — 결정 80 N2 나). 보기만 한다 — 스탬프는 조건이 맞는 순간 저절로 받고 보상도 저절로 받는다(plan §5 #90 ①).
    /// <list type="bullet">
    /// <item>왼쪽: 원작 여행 스탬프 시트 그림(M9 다듬기 P12 — 결정 80 L18 나) — 동그라미 일곱(윗줄 1~4 · 아랫줄 5~7, 자리 <see cref="Spots"/>) 위의 보이지 않는 칸(마우스 오버 = 룰북 문구). 받은 칸에는 도장(밀랍 봉인, −8°)이 찍혀 있다.</item>
    /// <item>오른쪽: 보상 일곱 단계(모은 수마다 — 받음 · 다음). 기술 카드 단계와 수호신의 서는 마우스 오버에 쓰임.</item>
    /// <item>아래: 받은 기술 카드(스탬프 보상 기술 풀 — 정비에서 가르친다). 기술 줄은 정비 판과 같은 부품(<see cref="MonParts.MoveRow"/>).</item>
    /// </list>
    /// 요소 이름: <c>board-stamp</c>·<c>board-stamp-panel</c>·<c>board-stamp-title</c>·<c>board-stamp-close</c>·<c>board-stamp-slot-&lt;n&gt;</c>·<c>board-stamp-reward-&lt;n&gt;</c>·<c>board-stamp-pool</c>.
    /// </summary>
    public sealed class StampScreen : IUiScreen
    {
        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel;
        int painted = -1;

        /// <summary>시트의 주인 좌석.</summary>
        public readonly int Seat;

        public StampScreen(GameFlow flow, int seat)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Seat = seat;
        }

        public string Name => "board-stamp";

        /// <summary>원작 시트 그림(4163×2084) 안의 동그라미 일곱의 가운데(칸 순서 = 스탬프 1~7)와 반지름 — 그림 좌표. 화면은 백분율로 놓는다(<see cref="Place"/>).</summary>
        public static readonly Vector2Int[] Spots = { new Vector2Int(618, 650), new Vector2Int(1594, 650), new Vector2Int(2569, 650), new Vector2Int(3543, 650), new Vector2Int(1106, 1433), new Vector2Int(2082, 1433), new Vector2Int(3055, 1433) };
        public const int SheetW = 4163, SheetH = 2084, SpotR = 435;

        /// <summary>시트 그림을 바탕으로 깐 요소 안에서 그 칸의 동그라미 자리에 놓는다(백분율 — 시트의 크기와 무관).</summary>
        public static void Place(VisualElement e, int slot)
        {
            var c = Spots[slot];
            e.style.position = Position.Absolute;
            e.style.left = Length.Percent((c.x - SpotR) * 100f / SheetW);
            e.style.top = Length.Percent((c.y - SpotR) * 100f / SheetH);
            e.style.width = Length.Percent(SpotR * 200f / SheetW);
            e.style.height = Length.Percent(SpotR * 200f / SheetH);
        }

        /// <summary>그 요소의 바탕에 Gen 그림을 깐다(없으면 그대로).</summary>
        public static void Art(VisualElement e, string key)
        {
            var tex = GenAssets.Load<Texture2D>(key);
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
        }

        /// <summary>작은 도장 하나(찍힌 모습 — 번호 = 칸 + 1): 결과 창의 스탬프 줄이 「무슨 스탬프」를 보일 때 쓴다(결정 101 ④). 마우스를 받지 않는다.</summary>
        public static VisualElement Seal(int slot)
        {
            var seal = new VisualElement { pickingMode = PickingMode.Ignore };
            seal.AddToClassList("sn-stamp-icon");
            var n = Kit.Text((slot + 1).ToString(CultureInfo.InvariantCulture), Kit.F2);
            n.pickingMode = PickingMode.Ignore;
            seal.Add(n);
            return seal;
        }

        /// <summary>지금 그린 스탬프 수 · 기술 카드 수.</summary>
        public int Count { get; private set; }
        public int Moves { get; private set; }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 HUD)의 입력을 먹지 않는다 — 창 밖 누르기는 어둠이 받는다(T35)
            var scrim = Add(page, new VisualElement(), "board-stamp-scrim", "sn-party-scrim");
            scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseStamp());   // 창 밖을 누르면 닫힌다
            panel = Add(page, Kit.Panel(stage.Skin), "board-stamp-panel", "sn-stamp-panel");
            painted = -1;
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
        }

        /// <summary>보기가 바뀌었다: 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null || flow.Client == null || flow.Client.View == null) return;
            if (painted == flow.Client.Version) return;
            painted = flow.Client.Version;
            var view = flow.Client.View;
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            var seat = view.Seats[Seat];
            Count = StampRules.Count(seat);
            Moves = seat.StampMoves.Count;
            stage.Tips.Hide();
            panel.Clear();

            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            // 남 좌석의 시트(정비 판 S39의 「여행 스탬프 n/7」에서 — 결정 80 N2 나)는 제목에 그 좌석 이름
            string title = Seat == flow.Client.Seat ? stage.T("screen.stamp.title") : stage.T("screen.stamp.title.seat").Replace("{name}", flow.SeatName(Seat));
            var titleText = Add(head, Kit.Text(title.Replace("{n}", Count.ToString(inv)), Kit.FTitle), "board-stamp-title", "kit-title");
            if (Seat != flow.Client.Seat) FaceBox.Owner(flow, Seat, titleText);
            Add(head, Kit.Button(stage.T("screen.stamp.close"), KitButtonKind.Main, () => flow.CloseStamp()), "board-stamp-close", null);

            var body = Add(panel, new VisualElement(), null, "sn-stamp-body");
            var sheet = Add(body, new VisualElement(), "board-stamp-sheet", "sn-stamp-sheet");
            Art(sheet, GenAssets.StampSheet);
            bool solo = view.SeatCount == 1;
            for (int i = 0; i < SeatState.StampCount; i++)
            {
                string n = (i + 1).ToString(inv);
                var slot = Add(sheet, new VisualElement(), "board-stamp-slot-" + n, "sn-stamp-slot");
                Place(slot, i);
                if (seat.Stamps[i]) slot.AddToClassList("sn-stamp-slot-on");
                var seal = Add(slot, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-stamp-seal");
                Art(seal, GenAssets.StampSeal);
                // 마우스 오버 = 룰북 문구(아이템 표 stamp.n) · 받은 칸은 머리에 「획득」 · 혼자 하는 판의 스탬프 2는 갤럭시단도(rulings A30 ② 2)
                string tip = stage.T("stamp." + n + ".text");
                if (solo && i == StampRules.TrainerWin) tip += "\n" + stage.T("screen.stamp.solo.2.tip");
                string label = stage.T("stamp." + n) + (seat.Stamps[i] ? " · " + stage.T("screen.stamp.got") : "");
                stage.Tips.Hook(slot, label, tip, TipSide.Below, reading: true, clear: panel);
            }

            var rewards = Add(body, new VisualElement(), "board-stamp-rewards", "sn-stamp-rewards");
            var rhead = Add(rewards, Kit.Text(stage.T("screen.stamp.rewards"), Kit.F2), null, "kit-title");
            stage.Tips.Hook(rhead, null, stage.T("screen.stamp.rewards.tip"), TipSide.Below, reading: true, clear: panel);
            for (int k = 1; k <= SeatState.StampCount; k++)
            {
                string n = k.ToString(inv);
                var row = Add(rewards, new VisualElement(), "board-stamp-reward-" + n, "sn-stamp-reward");
                bool done = k <= Count, next = k == Count + 1;
                if (next) row.AddToClassList("sn-stamp-reward-next");
                else if (!done) row.AddToClassList("sn-stamp-reward-off");
                Add(row, Kit.Text(stage.T("screen.stamp.reward.n").Replace("{n}", n), Kit.F2), null, "sn-stamp-reward-n");
                Add(row, Kit.Text(stage.T("screen.stamp.reward." + n), Kit.F2), null, "sn-stamp-reward-text");
                if (done) row.Add(Kit.Badge(stage.T("screen.stamp.reward.done"), KitBadgeKind.Ok, Kit.F2));
                else if (next) row.Add(Kit.Badge(stage.T("screen.stamp.reward.next"), KitBadgeKind.Gold, Kit.F2));
                string tip = k == SeatState.StampCount ? stage.T("screen.stamp.reward.7.tip") : k % 2 == 0 ? stage.T("screen.stamp.reward.cards.tip") : null;
                if (tip != null) stage.Tips.Hook(row, null, tip, TipSide.Below, reading: true, clear: panel);
            }

            // 받은 기술 카드(스탬프 보상 기술 풀): 가르치기는 정비 판에서
            var pool = Add(panel, new VisualElement(), "board-stamp-pool", "sn-stamp-pool");
            var ptitle = Add(pool, Kit.Text(stage.T("screen.stamp.pool").Replace("{n}", Moves.ToString(inv)), Kit.F2), null, "kit-title");
            stage.Tips.Hook(ptitle, null, stage.T("screen.stamp.pool.tip"), TipSide.Above, reading: true, clear: panel);
            if (Moves == 0) Add(pool, Kit.Text(stage.T("screen.stamp.pool.none"), Kit.F2), null, "kit-muted");
            foreach (int move in seat.StampMoves)
                if (move >= 0 && move < ctx.Cards.Moves.Count) pool.Add(MonParts.MoveRow(stage, ctx, move, false, TipSide.Above, panel));
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
