using System;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>방 화면(S05~S08)이 같이 쓰는 부품: 줄(이름 + 값 칸들 — 솔로 설정의 <c>sn-solo-row</c>·<c>sn-opt</c>) · 글 칸 · 실패 칸.</summary>
    static class RoomUi
    {
        public static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }

        /// <summary>줄 하나(이름 칸 + 값 칸 자리). 돌려주는 것은 값 칸 자리.</summary>
        public static VisualElement Row(VisualElement parent, UiStage stage, string name, string labelKey)
        {
            var row = Add(parent, new VisualElement(), name, "sn-solo-row");
            Add(row, Kit.Text(stage.T(labelKey), Kit.F2), null, "sn-solo-label");
            return Add(row, new VisualElement(), null, "sn-solo-opts");
        }

        /// <summary>
        /// 값 칸 하나: 고른 값은 금색(<c>sn-opt-picked</c>), 못 고르는 값은 흐림(<c>sn-off</c>). tap이 null이면 읽기만(누르지 않는다 — 손님의 방 설정).
        /// 마우스 오버: 이름 + 설명(없으면 이름만).
        /// </summary>
        public static VisualElement Opt(VisualElement opts, UiStage stage, string name, string text, bool picked, bool ready, string tip, Action tap)
        {
            var opt = Add(opts, new VisualElement(), name, "sn-opt");
            opt.Add(Kit.Text(text, Kit.F2));
            opt.EnableInClassList("sn-opt-picked", picked);
            opt.EnableInClassList("sn-off", !ready);
            if (tap != null && ready) stage.OnTap(opt, SoundKeys.Select, e => tap());
            stage.Tips.Hook(opt, text, tip, TipSide.Below, reading: true);
            return opt;
        }

        /// <summary>글 칸(엔진 기본 TextField — 모양은 <c>sn-field</c>). 글자 수는 max까지.</summary>
        public static TextField Field(VisualElement parent, string name, string value, int max, bool wide = false)
        {
            var field = new TextField { value = value ?? "", maxLength = max, isDelayed = false };
            field.AddToClassList("sn-field");
            if (wide) field.AddToClassList("sn-field-wide");
            return Add(parent, field, name, null);
        }

        /// <summary>실패 칸(02 §2-5): 짧은 문구 한 줄(자세한 것은 마우스 오버 — <see cref="NetFailText"/>).</summary>
        public static VisualElement Fail(VisualElement parent, UiStage stage, string name, string code, string detail, int port)
        {
            var box = Add(parent, new VisualElement(), name, "sn-room-fail");
            string key = NetFailText.Key(code);
            var line = Add(box, Kit.Wrapped(NetFailText.Fill(stage.T(key), code, detail, port), Kit.F2), name + "-text", "sn-room-fail-text");
            string tip = stage.TOrNull(key + ".tip");
            if (tip != null) stage.Tips.Hook(line, line.text, NetFailText.Fill(tip, code, detail, port), TipSide.Below, reading: true);
            return box;
        }
    }

    /// <summary>
    /// S05 방 만들기(04 §1 · 02 §2-1 · §3-1 결정 95): 닉네임 · 연결 [인터넷(추천)]/[직접 연결](결정 92 — 늘 보인다) · 방 목록 올리기/올리지 않기([인터넷]만 — 기본 올리기 [가정 M7-6]) ·
    /// 자리 수 2~6 · 캠페인 → [방 만들기]. 나머지 방 설정은 로비의 방 설정 판(S08). [인터넷] 방을 여는 동안 「방을 여는 중…」, 실패하면 문구 칸 —
    /// Unity 서버를 쓸 수 없으면(<see cref="RoomFlow.SuggestDirect"/>) [직접 연결로]. 뒤의 보드 전경은 타이틀과 같다.
    /// 요소 이름: <c>room-create</c> · <c>room-create-panel</c> · <c>room-create-nick</c> · <c>room-create-opt-&lt;줄&gt;-&lt;값&gt;</c>(줄 mode·list·seats·campaign) · <c>room-create-note</c> ·
    /// <c>room-create-status</c> · <c>room-create-fail</c> · <c>room-create-to-direct</c> · <c>room-create-back</c> · <c>room-create-start</c>.
    /// </summary>
    public sealed class RoomCreateScreen : IUiScreen, IOverBoard
    {
        /// <summary>판 오른쪽 변(720 토큰 px — <c>sn-room-col</c>의 left 60 + width 400).</summary>
        public const float ColumnRight = 460f;

        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel, rows, foot;
        TextField nick;

        public RoomCreateScreen(GameFlow flow) => this.flow = flow ?? throw new ArgumentNullException(nameof(flow));

        public string Name => "room-create";
        public float PanelRight => ColumnRight + SoloScreen.EnWiden(flow);
        RoomFlow Room => flow.Room;
        RoomCreateOptions Opts => flow.Room.Create;

        /// <summary>S04에서 고른 방 저장을 다시 여는 「방 다시 열기」인가(02 §6-3 — 연결 방식만 고르고 자리 수 · 캠페인은 저장된 값).</summary>
        public bool Reopening => Room.ResumeFrom != null;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement();
            var col = RoomUi.Add(page, new VisualElement(), "room-create-col", "sn-room-col");
            RoomUi.Add(col, Kit.Text(stage.T(Reopening ? "screen.room.reopen.title" : "screen.room.create.title"), Kit.FTitle), "room-create-head", "sn-head");
            panel = RoomUi.Add(col, Kit.Panel(stage.Skin), "room-create-panel", "sn-solo-panel");
            var nickRow = RoomUi.Row(panel, stage, "room-create-row-nick", "screen.room.nickname");
            nick = RoomUi.Field(nickRow, "room-create-nick", Room.Nickname, RoomFlow.NicknameMax);
            stage.Tips.Hook(nick, stage.T("screen.room.nickname"), stage.T("screen.room.nickname.tip"), TipSide.Below, reading: true);
            nick.RegisterValueChangedCallback(e => Room.Nickname = e.newValue);
            rows = RoomUi.Add(panel, new VisualElement(), null, null);
            foot = RoomUi.Add(col, new VisualElement(), "room-create-more", null);
            Refresh();
            return page;
        }

        public void Close() { }

        // 값을 고를 때마다 줄·안내·단추를 다시 세운다
        void Refresh()
        {
            if (stage == null) return;
            rows.Clear();
            foot.Clear();
            bool busy = Room.Busy;
            var o = RoomUi.Row(rows, stage, "room-create-row-mode", "screen.room.create.mode");
            RoomUi.Opt(o, stage, "room-create-opt-mode-internet", stage.T("screen.room.create.mode.internet"), Opts.Internet, !busy, stage.T("screen.room.create.mode.internet.tip"), () => SetInternet(true));
            RoomUi.Opt(o, stage, "room-create-opt-mode-direct", stage.T("screen.room.create.mode.direct"), !Opts.Internet, !busy, stage.T("screen.room.create.mode.direct.tip"), () => SetInternet(false));
            if (Reopening)
            {
                // 「방 다시 열기」(S04 → S05 → S10): 저장한 게임 한 줄(캠페인 · 막 · 라운드 · n명). 이어 연 방은 방 목록에 올리지 않는다(돌아올 사람만 — 참가 코드 · 주소로) [가정 M7-53]
                var head = Room.ResumeFrom.Head;
                string line = flow.SaveLine(head) + " · " + stage.T("screen.saves.room").Replace("{n}", head.SeatCount.ToString(CultureInfo.InvariantCulture));
                var r = RoomUi.Row(rows, stage, "room-create-row-resume", "screen.room.reopen.save");
                RoomUi.Add(r, Kit.Text(line, Kit.F2), "room-create-resume", null);
                nick.SetEnabled(!busy);
                string reNote = Opts.Internet ? stage.T("screen.room.reopen.note.internet") : stage.T("screen.room.reopen.note.direct").Replace("{p}", Room.Port.ToString(CultureInfo.InvariantCulture));
                RoomUi.Add(foot, Kit.Wrapped(reNote, Kit.F2), "room-create-note", "sn-room-note");
                Status();
                var reButtons = RoomUi.Add(foot, new VisualElement(), "room-create-foot", "sn-foot-row");
                RoomUi.Add(reButtons, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, Back), "room-create-back", null);
                var reopen = RoomUi.Add(reButtons, Kit.Button(stage.T("screen.room.reopen.start"), KitButtonKind.Main, () => Start()), "room-create-start", null);
                reopen.SetEnabled(!busy);
                return;
            }
            o = RoomUi.Row(rows, stage, "room-create-row-list", "screen.room.create.list");
            string offTip = stage.T("screen.room.create.list.direct");
            RoomUi.Opt(o, stage, "room-create-opt-list-public", stage.T("screen.room.create.list.public"), Opts.Internet && Opts.Public, Opts.Internet && !busy,
                Opts.Internet ? stage.T("screen.room.create.list.public.tip") : offTip, () => SetPublic(true));
            RoomUi.Opt(o, stage, "room-create-opt-list-private", stage.T("screen.room.create.list.private"), Opts.Internet && !Opts.Public, Opts.Internet && !busy,
                Opts.Internet ? stage.T("screen.room.create.list.private.tip") : offTip, () => SetPublic(false));
            o = RoomUi.Row(rows, stage, "room-create-row-seats", "screen.room.create.seats");
            for (int n = RoomSettingsMin; n <= SeatId.MaxSeats; n++)
            {
                int k = n;
                string text = stage.T("screen.room.create.seats.n").Replace("{n}", n.ToString(CultureInfo.InvariantCulture));
                RoomUi.Opt(o, stage, "room-create-opt-seats-" + n, text, Opts.Seats == n, !busy, null, () => SetSeats(k));
            }
            o = RoomUi.Row(rows, stage, "room-create-row-campaign", "screen.solo.campaign");
            RoomUi.Opt(o, stage, "room-create-opt-campaign-hos", stage.T("screen.solo.campaign.hos"), Opts.Campaign == Campaign.HeartOfSinnoh, !busy, stage.T("screen.solo.campaign.hos.tip"), () => SetCampaign(Campaign.HeartOfSinnoh));
            bool ub = Sinnoh.Rules.Session.SoloGame.CampaignReady(Campaign.UltraBurst);
            RoomUi.Opt(o, stage, "room-create-opt-campaign-ub", stage.T("screen.solo.campaign.ub"), Opts.Campaign == Campaign.UltraBurst, ub && !busy,
                ub ? stage.T("screen.solo.campaign.ub.tip") : stage.T("screen.common.not_ready"), () => SetCampaign(Campaign.UltraBurst));
            nick.SetEnabled(!busy);

            string note = Opts.Internet ? stage.T("screen.room.create.note.internet") : stage.T("screen.room.create.note.direct").Replace("{p}", Room.Port.ToString(CultureInfo.InvariantCulture));
            RoomUi.Add(foot, Kit.Wrapped(note, Kit.F2), "room-create-note", "sn-room-note");
            Status();
            var buttons = RoomUi.Add(foot, new VisualElement(), "room-create-foot", "sn-foot-row");
            RoomUi.Add(buttons, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, Back), "room-create-back", null);
            var start = RoomUi.Add(buttons, Kit.Button(stage.T("screen.room.create.start"), KitButtonKind.Main, () => Start()), "room-create-start", null);
            start.SetEnabled(!busy);
        }

        // 여는 중 · 실패 칸(Unity 서버를 쓸 수 없으면 [직접 연결로] — 결정 92)
        void Status()
        {
            bool busy = Room.Busy;
            if (busy) RoomUi.Add(foot, Kit.Text(stage.T("screen.room.create.opening"), Kit.F2), "room-create-status", "sn-room-status");
            else if (Room.FailCode != null)
            {
                var fail = RoomUi.Fail(foot, stage, "room-create-fail", Room.FailCode, Room.FailDetail, Room.Port);
                if (Room.SuggestDirect)
                {
                    var to = RoomUi.Add(fail, Kit.Button(stage.T("screen.room.to_direct"), KitButtonKind.Main, () => ToDirect()), "room-create-to-direct", null);
                    stage.Tips.Hook(to, stage.T("screen.room.to_direct"), stage.T("screen.room.to_direct.tip"), TipSide.Below, reading: true);
                }
            }
        }

        const int RoomSettingsMin = Sinnoh.Rules.Session.RoomSettings.MinSeats;

        /// <summary>연결 방식(true = [인터넷]).</summary>
        public void SetInternet(bool on)
        {
            if (Room.Busy) return;
            Opts.Internet = on;
            Refresh();
        }

        public void SetPublic(bool on)
        {
            if (Room.Busy || !Opts.Internet) return;
            Opts.Public = on;
            Refresh();
        }

        public void SetSeats(int n)
        {
            if (Room.Busy || n < RoomSettingsMin || n > SeatId.MaxSeats) return;
            Opts.Seats = n;
            Refresh();
        }

        public void SetCampaign(Campaign c)
        {
            if (Room.Busy || !Sinnoh.Rules.Session.SoloGame.CampaignReady(c)) return;
            Opts.Campaign = c;
            Refresh();
        }

        /// <summary>닉네임 칸에 쓴다(사람이 친 것처럼).</summary>
        public void SetNickname(string text)
        {
            if (nick != null) nick.value = text ?? "";
            else Room.Nickname = text;
        }

        /// <summary>[방 만들기].</summary>
        public bool Start() => Room.Host();

        /// <summary>[직접 연결로](결정 92).</summary>
        public bool ToDirect() => Room.ToDirect();

        /// <summary>[뒤로]: 타이틀의 「방 만들기」 줄로(「방 다시 열기」면 저장 목록 S04로).</summary>
        public void Back()
        {
            if (Reopening) Room.BackToSaves();
            else flow.OpenTitle(TitleScreen.Host);
        }
    }

    /// <summary>
    /// S06 참가하기(04 §1 · 02 §2-1 · §3-1 결정 95): 닉네임 · 참가 칸 하나(코드 6자 · 초대 링크 · 직접 연결 주소 <c>IP[:포트]</c> — 결정 92, 늘 보인다) + [참가] ·
    /// 공개 방 목록(UGS Lobby — 화면을 열 때 한 번 + [새로 고침], 빈도 막기 <see cref="RefreshGate"/> · 줄을 누르면 그 방의 코드로 들어감 · 가득 참·버전 다름은 흐린 줄 [가정 M7-6]).
    /// 실패는 문구 칸(02 §2-5) — Unity 서버를 쓸 수 없으면 「방장에게 직접 연결 주소를 받아 …」.
    /// 요소 이름: <c>room-join</c> · <c>room-join-nick</c> · <c>room-join-target</c> · <c>room-join-go</c> · <c>room-join-status</c> · <c>room-join-fail</c> · <c>room-join-direct-hint</c> ·
    /// <c>room-join-list</c> · <c>room-join-refresh</c> · <c>room-join-row-&lt;n&gt;</c> · <c>room-join-list-note</c> · <c>room-join-back</c>.
    /// </summary>
    public sealed class RoomJoinScreen : IUiScreen, IOverBoard
    {
        /// <summary>판 오른쪽 변(720 토큰 px — <c>sn-room-col</c> left 60 + <c>sn-room-col-wide</c> width 520).</summary>
        public const float ColumnRight = 580f;
        /// <summary>목록에 보이는 줄 수 [가정 M7-43].</summary>
        public const int MaxRows = 8;

        readonly GameFlow flow;
        UiStage stage;
        VisualElement status, list;
        TextField nick, target;
        Button refresh, go, tutorial;
        long shownLeft = -1;

        public RoomJoinScreen(GameFlow flow) => this.flow = flow ?? throw new ArgumentNullException(nameof(flow));

        public string Name => "room-join";
        public float PanelRight => ColumnRight + SoloScreen.EnWiden(flow);
        RoomFlow Room => flow.Room;

        /// <summary>목록에 보인 줄(받은 순서 — 들어갈 수 있는 방 → 가득 참 → 버전 다름).</summary>
        public System.Collections.Generic.IReadOnlyList<ListedRoom> Rows { get; private set; } = new ListedRoom[0];

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement();
            var col = RoomUi.Add(page, new VisualElement(), "room-join-col", "sn-room-col");
            col.AddToClassList("sn-room-col-wide");
            RoomUi.Add(col, Kit.Text(stage.T("screen.room.join.title"), Kit.FTitle), "room-join-head", "sn-head");
            var panel = RoomUi.Add(col, Kit.Panel(stage.Skin), "room-join-panel", "sn-solo-panel");
            var nickRow = RoomUi.Row(panel, stage, "room-join-row-nick", "screen.room.nickname");
            nick = RoomUi.Field(nickRow, "room-join-nick", Room.Nickname, RoomFlow.NicknameMax);
            stage.Tips.Hook(nick, stage.T("screen.room.nickname"), stage.T("screen.room.nickname.tip"), TipSide.Below, reading: true);
            nick.RegisterValueChangedCallback(e => Room.Nickname = e.newValue);
            var targetRow = RoomUi.Row(panel, stage, "room-join-row-target", "screen.room.join.target");
            target = RoomUi.Field(targetRow, "room-join-target", lastText, 200, wide: true);
            target.RegisterValueChangedCallback(e => lastText = e.newValue);
            go = RoomUi.Add(targetRow, Kit.Button(stage.T("screen.room.join.go"), KitButtonKind.Main, () => Join()), "room-join-go", "sn-room-go");
            RoomUi.Add(panel, Kit.Text(stage.T("screen.room.join.hint"), Kit.F1), "room-join-hint", "sn-room-hint").AddToClassList("kit-muted");

            status = RoomUi.Add(col, new VisualElement(), "room-join-state", null);
            list = RoomUi.Add(col, Kit.Panel(stage.Skin), "room-join-list", "sn-room-list");
            var buttons = RoomUi.Add(col, new VisualElement(), "room-join-foot", "sn-foot-row");
            RoomUi.Add(buttons, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, Back), "room-join-back", null);
            // 튜토리얼 입구(결정 38 [가정 M7-71]): 여기서 들어갔다가 이 화면으로 돌아온다
            if (flow.Tutorial.Available)
            {
                tutorial = RoomUi.Add(buttons, Kit.Button(stage.T("screen.room.tutorial"), KitButtonKind.Normal, () => Tutorial()), "room-join-tutorial", null);
                stage.Tips.Hook(tutorial, stage.T("screen.room.tutorial"), stage.T("screen.room.tutorial.join.tip"), TipSide.Above, reading: true);
            }
            Refresh();
            return page;
        }

        // 참가 칸의 글은 화면을 다시 세워도(접속 중 · 실패) 그대로 남는다 — 실행하는 동안만
        static string lastText = "";

        /// <summary>지금 참가 칸 글(시험).</summary>
        public string TargetText => target != null ? target.value : lastText;

        public void Close() { }

        /// <summary>상태(연결 중 · 실패)와 방 목록을 지금 값으로 다시 세운다.</summary>
        public void Refresh()
        {
            if (stage == null) return;
            bool busy = Room.Busy;
            nick.SetEnabled(!busy);
            target.SetEnabled(!busy);
            go.SetEnabled(!busy);
            tutorial?.SetEnabled(Room.CanTutorial);
            status.Clear();
            if (busy) RoomUi.Add(status, Kit.Text(stage.T("screen.room.join.joining"), Kit.F2), "room-join-status", "sn-room-status");
            else if (Room.FailCode != null)
            {
                var fail = RoomUi.Fail(status, stage, "room-join-fail", Room.FailCode, Room.FailDetail, Room.Port);
                if (Room.SuggestDirect) RoomUi.Add(fail, Kit.Wrapped(stage.T("screen.room.join.direct_hint"), Kit.F2), "room-join-direct-hint", "sn-room-fail-more");
            }

            list.Clear();
            var head = RoomUi.Add(list, new VisualElement(), "room-join-list-head", "sn-room-list-head");
            head.Add(Kit.Text(stage.T("screen.room.join.list"), Kit.F2));
            refresh = RoomUi.Add(head, Kit.Button(stage.T("screen.room.join.refresh"), KitButtonKind.Normal, () => PressRefresh()), "room-join-refresh", null);
            shownLeft = -1;
            TickRefresh();
            var q = Room.Listing;
            string note = Room.Browsing ? stage.T("screen.room.join.loading") : q == null ? null : !q.Ok ? null : q.Rooms.Count == 0 ? stage.T("screen.room.join.empty") : null;
            if (q != null && !q.Ok && !Room.Browsing) RoomUi.Fail(list, stage, "room-join-list-fail", q.FailCode, null, 0);
            var rows = new System.Collections.Generic.List<ListedRoom>();
            if (q != null && q.Ok && !Room.Browsing)
                for (int i = 0; i < q.Rooms.Count && rows.Count < MaxRows; i++) rows.Add(q.Rooms[i]);
            Rows = rows;
            string stamp = Room.Stamp;
            for (int i = 0; i < rows.Count; i++) AddRow(i, rows[i], stamp);
            if (note != null) RoomUi.Add(list, Kit.Text(note, Kit.F2), "room-join-list-note", "kit-muted");
        }

        void AddRow(int index, ListedRoom r, string stamp)
        {
            bool ok = r.Joinable(stamp) && !Room.Busy;
            // 두 줄: 방장 닉네임 + 배지 / 작은 글 「n/m명 · 캠페인 · 제한 시간」
            var row = RoomUi.Add(list, new VisualElement(), "room-join-row-" + index, "sn-room-row");
            row.EnableInClassList("sn-off", !r.Joinable(stamp));
            var top = RoomUi.Add(row, new VisualElement(), null, "sn-room-row-top");
            RoomUi.Add(top, Kit.Text(r.Host, Kit.F2), "room-join-row-" + index + "-host", "sn-room-cell-name");
            string people = stage.T("screen.room.join.people").Replace("{n}", r.Members.ToString(CultureInfo.InvariantCulture)).Replace("{m}", r.Seats.ToString(CultureInfo.InvariantCulture));
            string sub = people + " · " + stage.T(r.Campaign == Campaign.UltraBurst ? "screen.solo.campaign.ub" : "screen.solo.campaign.hos") + " · " + stage.T(LobbyScreen.ClockKey(r.Clock));
            RoomUi.Add(row, Kit.Text(sub, Kit.F1), "room-join-row-" + index + "-sub", "kit-muted");
            var tags = RoomUi.Add(top, new VisualElement(), null, "sn-room-tags");
            string tip = stage.T("screen.room.join.row.tip");
            if (r.OtherVersion(stamp))
            {
                tags.Add(Kit.Badge(stage.T("screen.room.join.version"), KitBadgeKind.Warn));
                tip = stage.T("screen.room.join.version.tip");
            }
            else if (r.Full)
            {
                tags.Add(Kit.Badge(stage.T("screen.room.join.full"), KitBadgeKind.Plain));
                tip = stage.T("screen.room.join.full.tip");
            }
            stage.Tips.Hook(row, r.Host, tip, TipSide.Below, reading: true, clear: list);
            if (ok) stage.OnTap(row, SoundKeys.Decide, e => PressRow(index), () => r.Joinable(Room.Stamp) && !Room.Busy);
        }

        /// <summary>[새로 고침]의 켜짐·남은 초 툴팁(매 프레임 — RoomFlow.Tick).</summary>
        public void TickRefresh()
        {
            if (refresh == null) return;
            long left = Room.Browsing ? RefreshGate.GapMs : Room.RefreshLeftMs;
            long secs = (left + 999) / 1000;
            if (secs == shownLeft) return;
            shownLeft = secs;
            refresh.SetEnabled(secs == 0);
            stage.Tips.Hook(refresh, stage.T("screen.room.join.refresh"),
                secs > 0 ? stage.T("screen.room.join.refresh.wait").Replace("{s}", secs.ToString(CultureInfo.InvariantCulture)) : null, TipSide.Below, reading: true);
        }

        /// <summary>참가 칸에 쓴다(사람이 친 것처럼).</summary>
        public void SetTarget(string text)
        {
            lastText = text ?? "";
            if (target != null) target.value = lastText;
        }

        /// <summary>닉네임 칸에 쓴다.</summary>
        public void SetNickname(string text)
        {
            if (nick != null) nick.value = text ?? "";
            else Room.Nickname = text;
        }

        /// <summary>[참가]: 참가 칸의 글로 붙는다.</summary>
        public bool Join() => Room.Join(lastText);

        /// <summary>[새로 고침](빈도 막기 안이면 false).</summary>
        public bool PressRefresh() => Room.Refresh();

        /// <summary>목록 줄을 누른다(들어갈 수 없는 줄이면 false).</summary>
        public bool PressRow(int index) => index >= 0 && index < Rows.Count && Room.JoinListed(Rows[index]);

        /// <summary>[튜토리얼](<see cref="RoomFlow.OpenTutorial"/> — 끝내면 이 화면으로).</summary>
        public bool Tutorial() => Room.OpenTutorial();

        /// <summary>[뒤로]: 타이틀의 「참가하기」 줄로.</summary>
        public void Back() => flow.OpenTitle(TitleScreen.Join);
    }
}
