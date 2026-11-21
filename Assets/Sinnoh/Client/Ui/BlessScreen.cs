using System;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S59 아르세우스의 축복(04 §2 — M6 「UB 화면 묶음 2」, 사용자 결정 84 M4 가 · U2 나): 울트라 버스트 판의 축복 창. 세 꼴이 같은 창을 쓴다.
    /// <list type="bullet">
    /// <item>판(<see cref="GameFlow.OpenBless"/> — 위 띠의 축복 칩 <see cref="BoardUb.Bless"/>을 누르면 연다, 결정 84 M4 가): 위 띠 오른쪽 아래의 작은 판(좌석 다섯 이상이면 UB 칩 판 아래) — 「아르세우스의 축복 n개」 · [UB 턴 건너뛰기] · 탈락한 좌석마다 [부활] · [닫기].
    /// 쓰는 것은 정비 명령 그대로(<see cref="UbBlessing.SkipCommand"/>·<see cref="UbBlessing.ReviveCommand"/> — 단추는 엔진이 받아들일 때만 켜진다). 여럿 판이면 다른 좌석에게 위 띠 물음이 간다(U1 나 — <see cref="BoardHud"/>).</item>
    /// <item>물음(Respond/UbSkip — 라운드가 끝나고 축복이 남았다, U2 나): 가운데 작은 창 「UB 턴을 건너뛰겠습니까?」 · [건너뛰기] · [건너뛰지 않음](기본) · 남은 시간.
    /// 여럿 판은 탈락하지 않은 모든 좌석의 다수결이라(사용자 결정 87 R3 — rulings A44 ②) 과반 규칙 한 줄 <c>board-bless-rule</c>과 좌석별 답 현황 <c>board-bless-seat-&lt;n&gt;</c>(고르는 중 · 건너뛰기 · 그대로 —
    /// 균열 카드 창의 좌석 현황 꼴, 확인 <see cref="VoteShown"/>)을 싣고, 내가 답한 뒤에도 묶음이 끝날 때까지 「다른 플레이어를 기다리는 중」<c>board-bless-wait</c>으로 남는다.</item>
    /// <item>부활 고르기(Reward/Revive — 모두 탈락했고 축복이 남았다, rulings A54 ⑥): 가운데 작은 창 · 탈락한 좌석마다 [부활].</item>
    /// </list>
    /// 물음·부활 고르기는 저절로 열리고 답하면 닫힌다(<see cref="GameFlow"/>의 SyncBless). 판만 [닫기]·Esc로 닫는다.
    /// 요소 이름: <c>board-bless</c>·<c>board-bless-panel</c>·<c>board-bless-count</c>·<c>board-bless-skip</c>·<c>board-bless-revive-&lt;n&gt;</c>(n = 좌석 번호, 1부터)·<c>board-bless-yes</c>·<c>board-bless-no</c>·<c>board-bless-close</c>·<c>board-bless-clock</c>.
    /// </summary>
    public sealed class BlessScreen : IUiScreen
    {
        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel, clock, clockBar;
        Label clockLeft;

        public BlessScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-bless";

        /// <summary>내가 아직 답하지 않은 축복 물음(UbSkip) · 부활 고르기(Revive). 없으면 null(판 꼴).</summary>
        public static PendingChoice ChoiceOf(Sinnoh.Rules.Session.SeatClient client)
        {
            var view = client != null ? client.View : null;
            if (view == null) return null;
            foreach (var c in view.Pending)
                if (c.Seat == client.Seat && !c.Answered && Owns(c)) return c;
            return null;
        }

        /// <summary>이 창이 받는 선택인가.</summary>
        public static bool Owns(PendingChoice c) =>
            c != null && ((c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.UbSkip) || (c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.Revive));

        /// <summary>그 좌석이 낀 건너뛰기 다수결이 아직 열려 있나(내가 답했어도 남이 고르는 중이면 참 — 창이 남는다).</summary>
        public static bool Voting(Sinnoh.Rules.Session.SeatClient client)
        {
            var view = client != null ? client.View : null;
            if (view == null) return false;
            foreach (var c in view.Pending)
                if (c.Seat == client.Seat && c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.UbSkip) return true;
            return false;
        }

        /// <summary>다수결 현황에 지금 적힌 그 좌석의 답(확인용): 0 = 고르는 중 · 1 = 건너뛰기 · 2 = 그대로 · −1 = 그 좌석 줄이 없다.</summary>
        public int VoteShown(int seat) => seat >= 0 && seat < voteShown.Length ? voteShown[seat] : -1;
        int[] voteShown = new int[0];

        /// <summary>판 꼴인가(물음·부활 고르기가 아니다).</summary>
        public bool IsPanel { get; private set; }

        /// <summary>물음 창이 접혔나(결정 87 V19 ① — 머리 줄만 남은 띠: 가운데 말이 보인다). [접기]·[펼치기] <c>board-bless-fold</c>.</summary>
        public bool Folded { get; private set; }

        public bool ToggleFold()
        {
            if (stage == null || IsPanel) return false;
            Folded = !Folded;
            Refresh();
            flow.Refold();   // 접힌 동안 보드 카메라가 움직인다(T21)
            return true;
        }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 · 보드)의 입력을 먹지 않는다(T35)
            panel = Kit.Panel(stage.Skin);
            panel.name = "board-bless-panel";
            page.Add(panel);
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
            clock = clockBar = null;
            clockLeft = null;
        }

        /// <summary>보기·입력이 바뀌었다: 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null) return;
            var client = flow.Client;
            var view = client != null ? client.View : null;
            if (view == null || view.Ub == null) return;
            var inv = CultureInfo.InvariantCulture;
            var choice = ChoiceOf(client);
            bool voting = Voting(client);
            IsPanel = choice == null && !voting;
            voteShown = new int[view.Seats.Length];
            for (int i = 0; i < voteShown.Length; i++) voteShown[i] = -1;
            panel.EnableInClassList("sn-bless-side", IsPanel);
            panel.EnableInClassList("sn-bless-low", IsPanel && view.Seats.Length >= BoardUb.RowSeats);   // 좌석 다섯 이상: 위 띠 아래 UB 칩 판 밑으로
            panel.EnableInClassList("sn-bless-centre", !IsPanel);
            stage.Tips.Hide();
            panel.Clear();
            bool open = flow.BoardOpen;

            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            Add(head, Kit.Text(stage.T("screen.bless.title"), Kit.FTitle), "board-bless-title", "kit-title");
            var count = Add(head, Kit.Badge(stage.T("screen.bless.count").Replace("{n}", view.Ub.Blessings.ToString(inv)), KitBadgeKind.Gold, Kit.F2), "board-bless-count", null);
            stage.Tips.Hook(count, null, stage.T("screen.bless.count.tip"), TipSide.Below, reading: true, clear: panel);
            Add(head, new VisualElement(), null, "sn-breach-grow");
            if (IsPanel) Add(head, Kit.Button(stage.T("screen.bless.close"), KitButtonKind.Normal, () => flow.CloseBless()), "board-bless-close", null);
            else
            {
                clock = Add(head, new VisualElement(), "board-bless-clock", "sn-breach-clock");
                var track = Add(clock, new VisualElement(), null, "sn-breach-track");
                clockBar = Add(track, new VisualElement(), null, "sn-breach-bar");
                clockLeft = Add(clock, Kit.Text("", Kit.F2), null, null);
                Tick();
                Add(head, Kit.Button(stage.T(Folded ? "screen.common.unfold" : "screen.common.fold"), KitButtonKind.Normal, () => ToggleFold()), "board-bless-fold", null);
                if (Folded) return;   // 띠만 — 다시 누르면 펼친다
            }

            if (voting)
            {
                var box = Add(panel, new VisualElement(), null, "sn-bless-box");
                box.Add(Kit.Text(stage.T("screen.bless.ask"), Kit.F3));
                Wrapped(box, stage.T("screen.bless.skip.text"));
                var votes = view.Pending.FindAll(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.UbSkip);
                if (votes.Count > 1)
                {
                    // 여럿 판: 과반 규칙 한 줄 + 좌석별 답 현황(공개 — 결정 87 R3)
                    Wrapped(box, stage.T("screen.bless.vote.rule")).name = "board-bless-rule";
                    var seats = Add(panel, new VisualElement(), "board-bless-seats", "sn-bless-seats");
                    foreach (var c in votes)
                    {
                        string n = (c.Seat + 1).ToString(inv);
                        int shown = !c.Answered ? 0 : c.Answer.Kind == AnswerKind.Accept ? 1 : 2;
                        voteShown[c.Seat] = shown;
                        var chip = Add(seats, new VisualElement(), "board-bless-seat-" + n, "sn-breach-seat");
                        var face = Add(chip, FaceBox.Make(), "board-bless-seat-" + n + "-face", null);
                        FaceBox.Paint(face, c.Seat, flow.SeatColor(c.Seat), flow.Protagonist(c.Seat));
                        stage.Tips.Hook(chip, flow.SeatName(c.Seat), null, TipSide.Above, clear: panel);
                        chip.Add(Kit.Badge(stage.T(shown == 0 ? "screen.breach.picking" : shown == 1 ? "screen.bless.vote.yes" : "screen.bless.vote.no"), shown == 1 ? KitBadgeKind.Gold : shown == 2 ? KitBadgeKind.Ok : KitBadgeKind.Plain, Kit.F2));
                    }
                }
                // 내 표(T29): 다 모이기 전에는 답한 뒤에도 바꿀 수 있다 — 고른 쪽은 금색 · 몇 명이 남았는지
                var mine = MyVote(client);
                int left = votes.FindAll(c => !c.Answered).Count;
                if (votes.Count > 1) Add(panel, Kit.Text(stage.T("screen.bless.vote.left").Replace("{n}", left.ToString(inv)), Kit.F2), "board-bless-left", "kit-muted");
                if (mine == null || (mine.Answered && !UbBlessing.CanRevote(view, mine)))
                {
                    Add(Add(panel, new VisualElement(), null, "sn-bless-buttons"), Kit.Text(stage.T("screen.breach.wait"), Kit.F2), "board-bless-wait", "kit-muted");
                    return;
                }
                var row = Add(panel, new VisualElement(), null, "sn-bless-buttons");
                var legal = client.Legal(mine);
                var yes = legal.Find(a => a.Kind == AnswerKind.Accept);
                var no = legal.Find(a => a.Kind == AnswerKind.Decline);
                bool saidYes = mine.Answered && mine.Answer.Kind == AnswerKind.Accept, saidNo = mine.Answered && mine.Answer.Kind == AnswerKind.Decline;
                var noButton = Add(row, Kit.Button(stage.T("screen.bless.ask.no"), saidNo ? KitButtonKind.Main : KitButtonKind.Normal, () => Answer(no)), "board-bless-no", null);
                noButton.SetEnabled(open && no != null && !saidNo);
                var yesButton = Add(row, Kit.Button(stage.T("screen.bless.ask.yes"), saidNo ? KitButtonKind.Normal : KitButtonKind.Main, () => Answer(yes)), "board-bless-yes", null);
                yesButton.SetEnabled(open && yes != null && !saidYes);
                if (mine.Answered) Add(panel, Kit.Text(stage.T("screen.bless.vote.change"), Kit.F2), "board-bless-change", "kit-muted");
                return;
            }
            if (choice != null)
            {
                // 부활 고르기(모두 탈락): Options = 탈락한 좌석
                Wrapped(Add(panel, new VisualElement(), null, "sn-bless-box"), stage.T("screen.bless.revive.pick"));
                foreach (var a in client.Legal(choice))
                    if (a.Kind == AnswerKind.Pick) SeatRow(view, a.Target, () => Answer(a), open);
                return;
            }
            // 판: [UB 턴 건너뛰기] · 탈락한 좌석마다 [부활]
            var skipBox = Add(panel, new VisualElement(), null, "sn-bless-box");
            skipBox.Add(Kit.Text(stage.T("screen.bless.skip"), Kit.F2));
            Wrapped(skipBox, stage.T("screen.bless.skip.text"));
            var skipRow = Add(skipBox, new VisualElement(), null, "sn-bless-buttons");
            var skipCmd = UbBlessing.SkipCommand();
            var skip = Add(skipRow, Kit.Button(stage.T("screen.bless.skip.go"), KitButtonKind.Main, () => Use(skipCmd)), "board-bless-skip", null);
            skip.SetEnabled(open && flow.CanMaintain(skipCmd) == RejectReason.None);
            var reviveBox = Add(panel, new VisualElement(), null, "sn-bless-box");
            reviveBox.Add(Kit.Text(stage.T("screen.bless.revive"), Kit.F2));
            int outs = 0;
            for (int seat = 0; seat < view.Seats.Length; seat++)
            {
                if (!view.Seats[seat].Eliminated) continue;
                outs++;
                var cmd = UbBlessing.ReviveCommand(seat);
                SeatRow(view, seat, () => Use(cmd), open && flow.CanMaintain(cmd) == RejectReason.None, reviveBox);
            }
            if (outs == 0) Add(reviveBox, Kit.Text(stage.T("screen.bless.revive.none"), Kit.F2), null, "kit-muted");
        }

        /// <summary>프레임마다: 물음의 남은 시간 막대 · 초(시계가 없는 판이면 숨긴다).</summary>
        public void Tick()
        {
            if (stage == null || clock == null) return;
            var choice = ChoiceOf(flow.Client);
            int total = 0;
            int left = choice != null ? flow.ClockLeftMs(choice, out total) : -1;
            clock.style.display = left >= 0 && total > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (left < 0 || total <= 0) return;
            clockBar.style.width = Length.Percent(100f * left / total);
            clockLeft.text = stage.T("screen.prompt.left").Replace("{n}", ((left + 999) / 1000).ToString(CultureInfo.InvariantCulture));
        }

        // 좌석 한 줄: 얼굴 상자 · 이름 · 「탈락」 · [부활]
        void SeatRow(GameState view, int seat, Action press, bool on, VisualElement into = null)
        {
            string n = (seat + 1).ToString(CultureInfo.InvariantCulture);
            var row = Add(into ?? panel, new VisualElement(), null, "sn-challenge-row");
            var who = Add(row, new VisualElement(), null, "sn-challenge-who");
            var face = Add(who, FaceBox.Make(), "board-bless-face-" + n, null);
            FaceBox.Paint(face, seat, flow.SeatColor(seat), flow.Protagonist(seat));
            who.Add(Kit.Text(flow.SeatName(seat), Kit.F2));
            who.Add(Kit.Badge(stage.T("screen.board.seat.out"), KitBadgeKind.Plain, Kit.F2));
            Add(row, Kit.Button(stage.T("screen.bless.revive.go"), KitButtonKind.Main, () => press()), "board-bless-revive-" + n, null).SetEnabled(on);
        }

        static Label Wrapped(VisualElement parent, string text)
        {
            var l = Kit.Wrapped(text, Kit.F2);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.AddToClassList("kit-muted");
            parent.Add(l);
            return l;
        }

        // ── 누르기(길잡이·시험도 부른다) ──

        /// <summary>물음의 [건너뛰기](true) · [건너뛰지 않음](false).</summary>
        public bool PressAsk(bool skip)
        {
            var choice = ChoiceOf(flow.Client);
            if (choice == null || choice.Sub != ChoiceSub.UbSkip) return false;
            return Answer(flow.Client.Legal(choice).Find(a => a.Kind == (skip ? AnswerKind.Accept : AnswerKind.Decline)));
        }

        /// <summary>판의 [건너뛰기] · 부활 고르기·판의 그 좌석 [부활].</summary>
        public bool PressSkip() => IsPanel && Use(UbBlessing.SkipCommand());

        public bool PressRevive(int seat)
        {
            var choice = ChoiceOf(flow.Client);
            if (choice == null) return IsPanel && Use(UbBlessing.ReviveCommand(seat));
            return choice.Sub == ChoiceSub.Revive && Answer(flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.Pick && a.Target == seat));
        }

        // 내 건너뛰기 표(답했든 아니든 — 묶음이 열려 있는 동안). 없으면 null
        static PendingChoice MyVote(Sinnoh.Rules.Session.SeatClient client)
        {
            var view = client != null ? client.View : null;
            if (view == null) return null;
            foreach (var c in view.Pending)
                if (c.Seat == client.Seat && c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.UbSkip) return c;
            return null;
        }

        /// <summary>다수결에서 내 표를 그 쪽으로 낸다(답한 뒤에도 다 모이기 전이면 바꾼다 — T29). 받아들여졌으면 true — 확인용(화면 단추와 같은 길).</summary>
        public bool Vote(bool skip)
        {
            var mine = MyVote(flow.Client);
            if (mine == null) return false;
            var a = flow.Client.Legal(mine).Find(x => x.Kind == (skip ? AnswerKind.Accept : AnswerKind.Decline));
            return Answer(a);
        }

        bool Answer(ChoiceAnswer answer)
        {
            var choice = ChoiceOf(flow.Client);
            var vote = MyVote(flow.Client);
            if (choice == null && vote != null && UbBlessing.CanRevote(flow.Client.View, vote)) choice = vote;   // 이미 낸 표를 바꾼다(T29)
            if (choice == null || answer == null || !flow.BoardOpen) return false;
            stage?.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        bool Use(Maintenance m)
        {
            if (!flow.BoardOpen || flow.CanMaintain(m) != RejectReason.None) return false;
            stage?.Tips.Hide();
            bool ok = flow.Maintain(m).Accepted;
            if (ok) flow.CloseBless();
            return ok;
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
