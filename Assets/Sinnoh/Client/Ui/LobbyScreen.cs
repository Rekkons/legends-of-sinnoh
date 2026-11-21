using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S07 로비 + S08 방 설정(02 §3-1·§3-2, 04 §1 · §10-5, 목업 r4/lobby — 결정 95): 왼쪽에 초대 칸(방장 — [인터넷] 참가 코드 · 목록에 올림 / [직접 연결] 내 주소·포트,
    /// 손님 — 「○○의 방」) · 자리 6줄(번호 · 닉네임 · 방장 · 나 · PC판/웹 · 준비 완료/준비 중 · 방장의 [내보내기] · 빈 자리 · 닫힌 자리) · 안내 한 줄 ·
    /// 단추(방장 [게임 시작](2명 이상이 모두 준비하면 켜짐) [방 닫기] / 손님 [준비 완료]·[준비 취소] [나가기]). 오른쪽에 방 설정 판(<see cref="RoomSettingsPanel"/> — 방장 편집 / 손님 읽기).
    /// 방 상태는 <see cref="ILobbySource"/>에서만 읽고 바뀌면(<see cref="Sync"/> — RoomFlow.Tick이 매 프레임) 다시 세운다.
    /// 좌석의 주인공(M9 다듬기 P06 — 결정 112 Q4): 자리 줄마다 그 좌석 주인공의 필드 도트(<c>lobby-seat-&lt;n&gt;-proto</c>), 자리 판 아래 「내 주인공」 여덟 칸(<c>lobby-proto</c> · <c>lobby-proto-&lt;키&gt;</c> —
    /// 누르면 <see cref="PickProtagonist"/>, 표시는 방이 돌려준 방 상태로 · 겹쳐 골라도 된다 [가정 M7-94]). 로비에는 핑이 없다.
    /// 요소 이름: <c>lobby</c> · <c>lobby-invite</c> · <c>lobby-code</c> · <c>lobby-listed</c> · <c>lobby-address-&lt;n&gt;</c> · <c>lobby-port</c> · <c>lobby-room-of</c> ·
    /// <c>lobby-seat-&lt;n&gt;</c>(+ <c>-name</c> · <c>-host</c> · <c>-me</c> · <c>-state</c> · <c>-kick</c>) · <c>lobby-records</c>(방장이 플레이 기록을 보낼 때 — 결정 126 ④) · <c>lobby-hint</c> · <c>lobby-start</c> · <c>lobby-close</c> · <c>lobby-ready</c> · <c>lobby-leave</c> · <c>lobby-set</c>.
    /// </summary>
    public sealed class LobbyScreen : IUiScreen, IOverBoard
    {
        /// <summary>오른쪽 판의 오른쪽 변(720 토큰 px — <c>sn-lobby-set</c>의 left 520 + width 380 — P06에서 자리 줄에 주인공 도트를 넣으며 왼쪽 기둥을 40 넓혔다).</summary>
        public const float PanelEdge = 900f;

        readonly GameFlow flow;
        readonly ILobbySource source;
        UiStage stage;
        VisualElement left, right;
        RoomSettingsPanel settings;
        RoomState shown;
        string shownCode;
        bool shownListed;
        string shownListFail;
        IReadOnlyList<string> shownWeb;
        string shownNotice;
        bool shownPending;

        public LobbyScreen(GameFlow flow, ILobbySource source)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public string Name => "lobby";
        public float PanelRight => PanelEdge;
        public ILobbySource Source => source;

        /// <summary>방 설정 판(S08).</summary>
        public RoomSettingsPanel Settings => settings;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement();
            left = RoomUi.Add(page, new VisualElement(), "lobby-col", "sn-lobby-col");
            right = RoomUi.Add(page, Kit.Panel(stage.Skin), "lobby-set", "sn-lobby-set");
            settings = new RoomSettingsPanel(stage, source);
            // 방 채팅(결정 140 ②): 방 설정 판 오른쪽에 펼친 판 — 방이 있는 로비만(가짜 로비 · 다른 방 출처는 없음). 다시 세우기와 따로 한 번만 만든다(글 칸 포커스 · 한글 조합)
            if (source is RoomLive live)
            {
                chat = new ChatBox(flow, live, foldable: false);
                page.Add(chat.Build(stage, true));
            }
            shown = null;
            Rebuild();
            return page;
        }

        public void Close() => chat?.Close();

        ChatBox chat;

        /// <summary>로비의 채팅 창(방이 있는 로비만 — 아니면 null).</summary>
        public ChatBox Chat => chat;

        /// <summary>방 상태가 바뀌었으면 다시 세운다(갱신은 새 RoomState로 온다 — 같은 것이면 그대로).</summary>
        public void Sync()
        {
            if (stage == null) return;
            chat?.Tick();
            if (ReferenceEquals(shown, source.Room) && shownCode == source.JoinCode && shownListed == source.Listed && shownListFail == source.ListFail
                && ReferenceEquals(shownWeb, source.WebLinks) && shownNotice == source.WebNotice && shownPending == source.WebPending) return;
            Rebuild();
        }

        /// <summary>이어하기 대기실(S10 — 방 단계 Waiting, 02 §6-3)로 그렸나.</summary>
        public bool Waiting => shown != null && shown.Phase == RoomPhase.Waiting;

        void Rebuild()
        {
            shown = source.Room;
            shownCode = source.JoinCode;
            shownListed = source.Listed;
            shownListFail = source.ListFail;
            shownWeb = source.WebLinks;
            shownNotice = source.WebNotice;
            shownPending = source.WebPending;
            copies.Clear();
            left.Clear();
            RoomUi.Add(left, Kit.Text(stage.T(Waiting ? "screen.room.wait.title" : "screen.room.lobby.title"), Kit.FTitle), "lobby-head", "sn-head");
            Invite();
            if (Waiting)
            {
                WaitSeats();
                Claims();
                Records();
                WaitFoot();
            }
            else
            {
                Seats();
                Picker();
                Records();
                Foot();
            }
            settings.Build(right, shown);
        }

        // 플레이 기록 알림(결정 126 ④): 방장이 보내기를 켰으면 한 줄(방장 · 손님 모두) + 마우스 오버로 자세히
        void Records()
        {
            if (shown == null || !shown.Records) return;
            var line = RoomUi.Add(left, Kit.Wrapped(stage.T("screen.room.lobby.records"), Kit.F2), "lobby-records", "sn-lobby-records");
            line.AddToClassList("kit-muted");
            stage.Tips.Hook(line, stage.T("screen.title.notice.title"), stage.T("screen.room.lobby.records.tip"), TipSide.Below, reading: true);
        }

        // ── 이어하기 대기실(S10 [가정 M7-52]) ──

        // 저장된 좌석 줄: 번호 · 닉네임 · 방장 · 나 · PC판/웹 · 돌아옴/기다리는 중/내보냄
        void WaitSeats()
        {
            var box = RoomUi.Add(left, Kit.Panel(stage.Skin), "lobby-seats", null);
            int n = shown != null ? shown.Seats.Count : 0;
            for (int i = 0; i < n; i++)
            {
                var seat = shown.Seats[i];
                var row = RoomUi.Add(box, new VisualElement(), "lobby-seat-" + i, "sn-lobby-seat");
                RoomUi.Add(row, Kit.Text(stage.T("screen.room.lobby.seat_no").Replace("{n}", (i + 1).ToString(CultureInfo.InvariantCulture)), Kit.F2), "lobby-seat-" + i + "-no", "sn-lobby-no");
                if (seat == null) continue;
                bool me = i == source.Seat;
                row.EnableInClassList("sn-lobby-seat-me", me);
                row.EnableInClassList("sn-off", seat.Kicked);
                Dot(row, i, seat);
                RoomUi.Add(row, Kit.Text(seat.Nickname ?? "", Kit.F2), "lobby-seat-" + i + "-name", "sn-lobby-name");
                var badges = RoomUi.Add(row, new VisualElement(), null, "sn-lobby-badges");
                if (seat.Host) RoomUi.Add(badges, Kit.Badge(stage.T("screen.room.lobby.host"), KitBadgeKind.Gold), "lobby-seat-" + i + "-host", null);
                if (me) RoomUi.Add(badges, Kit.Badge(stage.T("screen.room.lobby.me"), KitBadgeKind.Silver), "lobby-seat-" + i + "-me", null);
                badges.Add(Kit.Badge(stage.T(seat.Platform == Platform.Web ? "screen.room.lobby.web" : "screen.room.lobby.pc"), KitBadgeKind.Plain));
                string state = seat.Kicked ? "screen.room.wait.kicked" : seat.Connected ? "screen.room.wait.back" : "screen.room.wait.waiting";
                RoomUi.Add(row, Kit.Badge(stage.T(state), seat.Connected && !seat.Kicked ? KitBadgeKind.Ok : KitBadgeKind.Plain), "lobby-seat-" + i + "-state", null);
            }
        }

        // 자리 확인 대기(방장만 — 02 §6-3 · M7-22): 저장에 없는 토큰으로 온 사람. 추천 자리(닉네임이 같은, 없으면 첫 빈 자리)에 앉히거나 들이지 않는다
        void Claims()
        {
            if (!source.IsHost || shown == null || shown.Claims.Count == 0) return;
            var box = RoomUi.Add(left, Kit.Panel(stage.Skin), "room-wait-claims", "sn-wait-claims");
            var head = RoomUi.Add(box, Kit.Text(stage.T("screen.room.wait.claims"), Kit.F2), "room-wait-claims-head", null);
            stage.Tips.Hook(head, stage.T("screen.room.wait.claims"), stage.T("screen.room.wait.claims.tip"), TipSide.Below, reading: true);
            for (int i = 0; i < shown.Claims.Count; i++)
            {
                var c = shown.Claims[i];
                int seat = ClaimSeat(c);
                // 두 줄: 닉네임 + PC판/웹 / [n번 자리에 앉히기] [들이지 않기]
                var item = RoomUi.Add(box, new VisualElement(), "room-wait-claim-" + i, null);
                var row = RoomUi.Add(item, new VisualElement(), null, "sn-lobby-seat");
                RoomUi.Add(row, Kit.Text(c.Nickname ?? "", Kit.F2), "room-wait-claim-" + i + "-name", "sn-lobby-name");
                var badges = RoomUi.Add(row, new VisualElement(), null, "sn-lobby-badges");
                badges.Add(Kit.Badge(stage.T(c.Platform == Platform.Web ? "screen.room.lobby.web" : "screen.room.lobby.pc"), KitBadgeKind.Plain));
                var buttons = RoomUi.Add(item, new VisualElement(), null, "sn-foot-row");
                int id = c.Id;
                if (seat >= 0)
                    RoomUi.Add(buttons, Kit.Button(stage.T("screen.room.wait.assign").Replace("{n}", (seat + 1).ToString(CultureInfo.InvariantCulture)), KitButtonKind.Main, () => Assign(id, seat)), "room-wait-claim-" + i + "-assign", null);
                RoomUi.Add(buttons, Kit.Button(stage.T("screen.room.wait.refuse"), KitButtonKind.Normal, () => Refuse(id)), "room-wait-claim-" + i + "-refuse", null);
            }
        }

        // 맡길 자리: 방이 추천한 자리(닉네임이 같은 접속하지 않은 좌석), 없으면 접속하지 않은 첫 좌석(내보낸 좌석 뺌). 없으면 −1
        int ClaimSeat(RoomClaim c)
        {
            if (c.Suggested >= 0) return c.Suggested;
            for (int i = 0; i < shown.Seats.Count; i++)
                if (shown.Seats[i] != null && !shown.Seats[i].Connected && !shown.Seats[i].Kicked) return i;
            return -1;
        }

        void WaitFoot()
        {
            string hint = source.IsHost ? "screen.room.wait.hint.host" : "screen.room.wait.hint.guest";
            RoomUi.Add(left, Kit.Wrapped(stage.T(hint), Kit.F2), "lobby-hint", "sn-lobby-hint");
            var buttons = RoomUi.Add(left, new VisualElement(), "lobby-foot", "sn-foot-row");
            if (source.IsHost)
            {
                var now = RoomUi.Add(buttons, Kit.Button(stage.T("screen.room.wait.resume"), KitButtonKind.Main, () => ResumeNow()), "room-wait-resume", null);
                stage.Tips.Hook(now, stage.T("screen.room.wait.resume"), stage.T("screen.room.wait.resume.tip"), TipSide.Below, reading: true);
                RoomUi.Add(buttons, Kit.Button(stage.T("screen.room.lobby.close"), KitButtonKind.Normal, Leave), "lobby-close", null);
            }
            else RoomUi.Add(buttons, Kit.Button(stage.T("screen.room.lobby.leave"), KitButtonKind.Normal, Leave), "lobby-leave", null);
        }

        /// <summary>[지금 재개](방장 — 대기실): 돌아오지 않은 좌석은 「끊김」으로 두고 시작한다(02 §6-3).</summary>
        public bool ResumeNow()
        {
            if (!source.IsHost || !Waiting) return false;
            source.Send(new StartGame());
            return true;
        }

        /// <summary>자리 확인 대기의 [n번 자리에 앉히기](방장).</summary>
        public bool Assign(int claimId, int seat)
        {
            if (!source.IsHost || shown == null) return false;
            source.Send(new AssignSeat { ClaimId = claimId, Seat = seat });
            return true;
        }

        /// <summary>자리 확인 대기의 [들이지 않기](방장).</summary>
        public bool Refuse(int claimId)
        {
            if (!source.IsHost || shown == null) return false;
            source.Send(new RefuseClaim { ClaimId = claimId });
            return true;
        }

        // 초대 칸: 방장 — 참가 코드(인터넷) · 내 주소(직접 연결), 손님 — 「○○의 방」
        void Invite()
        {
            var box = RoomUi.Add(left, Kit.Panel(stage.Skin), "lobby-invite", "sn-lobby-invite");
            if (!source.IsHost)
            {
                string host = HostName(shown);
                RoomUi.Add(box, Kit.Text(stage.T("screen.room.lobby.room_of").Replace("{name}", host ?? "?"), Kit.F2), "lobby-room-of", null);
                return;
            }
            if (source.JoinCode != null)
            {
                // 참가 코드(Relay) 또는 두 번째 터널의 wss 주소(Relay 실패 — 02 §2-1)
                string key = source.Tunneled ? "screen.room.lobby.tunnel" : "screen.room.lobby.code";
                var label = RoomUi.Add(box, Kit.Text(stage.T(key), Kit.F2), "lobby-code-label", null);
                stage.Tips.Hook(label, stage.T(key), stage.T(key + ".tip"), TipSide.Below, reading: true);
                var row = RoomUi.Add(box, new VisualElement(), "lobby-code-row", "sn-lobby-code-row");
                var code = RoomUi.Add(row, Kit.Text(source.Tunneled ? ShortLink(source.JoinCode) : source.JoinCode, source.Tunneled ? Kit.F1 : Kit.F3), "lobby-code", source.Tunneled ? "sn-lobby-link" : "sn-lobby-code");
                if (source.Tunneled)
                {
                    stage.Tips.Hook(code, null, source.JoinCode, TipSide.Below, reading: true);
                    Copy(row, "lobby-code-copy", source.JoinCode);
                }
                else if (source.ListFail != null) RoomUi.Fail(box, stage, "lobby-list-fail", source.ListFail, null, 0);
                else RoomUi.Add(row, Kit.Badge(stage.T(source.Listed ? "screen.room.lobby.listed" : "screen.room.lobby.unlisted"), source.Listed ? KitBadgeKind.Ok : KitBadgeKind.Plain), "lobby-listed", null);
                WebLink(box);
                return;
            }
            var head = RoomUi.Add(box, Kit.Text(stage.T("screen.room.lobby.direct"), Kit.F2), "lobby-direct-label", null);
            stage.Tips.Hook(head, stage.T("screen.room.lobby.direct"), stage.T("screen.room.lobby.direct.tip"), TipSide.Below, reading: true);
            string port = source.Port.ToString(CultureInfo.InvariantCulture);
            var list = source.Addresses;
            if (list.Count == 0) RoomUi.Add(box, Kit.Text(stage.T("screen.room.lobby.no_address"), Kit.F2), "lobby-address-none", "kit-muted");
            for (int i = 0; i < list.Count && i < 3; i++)
                RoomUi.Add(box, Kit.Text(list[i].Key + ":" + port + " · " + list[i].Value, Kit.F2), "lobby-address-" + i, null);
            RoomUi.Add(box, Kit.Text(stage.T("screen.room.lobby.port").Replace("{p}", port), Kit.F1), "lobby-port", "kit-muted");
            if (source.Port > 0) WebLink(box);
        }

        // 웹 손님 링크(02 §2-2): 첫 링크 + [복사](주소가 여럿이면 나머지는 마우스 오버) · 만드는 중 · 없는 까닭(짧은 줄 + 마우스 오버)
        void WebLink(VisualElement box)
        {
            var links = source.WebLinks;
            if (links.Count == 0 && source.WebNotice == null && !source.WebPending) return;
            var head = RoomUi.Add(box, Kit.Text(stage.T("screen.room.lobby.weblink"), Kit.F2), "lobby-web-label", "kit-mt2");
            string tip = stage.T("screen.room.lobby.weblink.tip");
            if (links.Count > 1) tip += "\n" + string.Join("\n", links);
            stage.Tips.Hook(head, stage.T("screen.room.lobby.weblink"), tip, TipSide.Below, reading: true);
            if (links.Count > 0)
            {
                var row = RoomUi.Add(box, new VisualElement(), "lobby-web-row", "sn-lobby-code-row");
                var link = RoomUi.Add(row, Kit.Text(ShortLink(links[0]), Kit.F1), "lobby-web-link", "sn-lobby-link");
                stage.Tips.Hook(link, null, links[0], TipSide.Below, reading: true);
                Copy(row, "lobby-web-copy", links[0]);
            }
            else if (source.WebPending) RoomUi.Add(box, Kit.Text(stage.T("screen.room.lobby.weblink.wait"), Kit.F1), "lobby-web-wait", "kit-muted");
            else RoomUi.Fail(box, stage, "lobby-web-fail", source.WebNotice, null, 0);
        }

        /// <summary>링크 · 주소의 화면 글: 주소의 호스트[:포트]만(긴 링크는 칸을 넘는다 — 전체는 [복사]와 마우스 오버). <see cref="ShortMax"/>자를 넘으면 뒤를 「…」로.</summary>
        public static string ShortLink(string link)
        {
            if (string.IsNullOrEmpty(link)) return "";
            string s = link;
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) s = s.Substring(scheme + 3);
            int end = s.IndexOfAny(new[] { '/', '?' });
            if (end >= 0) s = s.Substring(0, end);
            return s.Length > ShortMax ? s.Substring(0, ShortMax - 1) + "…" : s;
        }

        /// <summary>링크 화면 글의 최대 글자 수(720 초대 칸 한 줄 [가정 M7-66]).</summary>
        public const int ShortMax = 44;

        // [복사]: 클립보드에 넣고 단추 글을 「복사함」으로(방장은 PC판 — 웹에는 이 칸이 없다, 02 §8)
        void Copy(VisualElement row, string name, string text)
        {
            Button b = null;
            Action press = () =>
            {
                Clipboard(text);
                Copied = text;
                b.text = stage.T("screen.room.lobby.copied");
            };
            b = RoomUi.Add(row, Kit.Button(stage.T("screen.room.lobby.copy"), KitButtonKind.Normal, press), name, "sn-lobby-copy");
            copies[name] = press;
        }

        readonly Dictionary<string, Action> copies = new Dictionary<string, Action>();

        /// <summary>그 [복사] 단추(<c>lobby-web-copy</c> · <c>lobby-code-copy</c>)를 누른다. 없으면 false.</summary>
        public bool PressCopy(string name)
        {
            if (!copies.TryGetValue(name, out var press)) return false;
            press();
            return true;
        }

        /// <summary>클립보드에 넣는 곳(시험은 바꿔 끼운다 — 사용자의 클립보드를 건드리지 않게).</summary>
        public static Action<string> Clipboard = SystemClipboard;

        static void SystemClipboard(string text) => UnityEngine.GUIUtility.systemCopyBuffer = text;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clipboard = SystemClipboard;

        /// <summary>마지막으로 [복사]한 글(시험).</summary>
        public string Copied { get; private set; }

        static string HostName(RoomState room)
        {
            if (room == null) return null;
            foreach (var s in room.Seats)
                if (s != null && s.Host) return s.Nickname;
            return null;
        }

        // 자리 6줄(02 §3-1 [가정 M7-10]): 들어온 순서대로 빈 자리에 · 자리 수 밖은 닫힌 자리
        void Seats()
        {
            var box = RoomUi.Add(left, Kit.Panel(stage.Skin), "lobby-seats", null);
            int seats = shown != null && shown.Settings != null ? shown.Settings.Seats : SeatId.MaxSeats;
            for (int i = 0; i < SeatId.MaxSeats; i++)
            {
                var seat = shown != null && i < shown.Seats.Count ? shown.Seats[i] : null;
                var row = RoomUi.Add(box, new VisualElement(), "lobby-seat-" + i, "sn-lobby-seat");
                RoomUi.Add(row, Kit.Text(stage.T("screen.room.lobby.seat_no").Replace("{n}", (i + 1).ToString(CultureInfo.InvariantCulture)), Kit.F2), "lobby-seat-" + i + "-no", "sn-lobby-no");
                if (seat == null)
                {
                    var empty = RoomUi.Add(row, Kit.Text(stage.T(i < seats ? "screen.room.lobby.empty" : "screen.room.lobby.closed"), Kit.F2), "lobby-seat-" + i + "-name", "sn-lobby-name");
                    empty.AddToClassList("kit-muted");
                    continue;
                }
                bool me = i == source.Seat;
                row.EnableInClassList("sn-lobby-seat-me", me);
                Dot(row, i, seat);
                RoomUi.Add(row, Kit.Text(seat.Nickname ?? "", Kit.F2), "lobby-seat-" + i + "-name", "sn-lobby-name");
                var badges = RoomUi.Add(row, new VisualElement(), null, "sn-lobby-badges");
                if (seat.Host) RoomUi.Add(badges, Kit.Badge(stage.T("screen.room.lobby.host"), KitBadgeKind.Gold), "lobby-seat-" + i + "-host", null);
                if (me) RoomUi.Add(badges, Kit.Badge(stage.T("screen.room.lobby.me"), KitBadgeKind.Silver), "lobby-seat-" + i + "-me", null);
                badges.Add(Kit.Badge(stage.T(seat.Platform == Platform.Web ? "screen.room.lobby.web" : "screen.room.lobby.pc"), KitBadgeKind.Plain));
                // 방장은 준비가 없다(02 §3-1)
                if (!seat.Host)
                    RoomUi.Add(row, Kit.Badge(stage.T(seat.Ready ? "screen.room.lobby.ready" : "screen.room.lobby.waiting"), seat.Ready ? KitBadgeKind.Ok : KitBadgeKind.Plain), "lobby-seat-" + i + "-state", null);
                if (source.IsHost && !seat.Host)
                {
                    int k = i;
                    var kick = RoomUi.Add(row, Kit.Button(stage.T("screen.room.lobby.kick"), KitButtonKind.End, () => Kick(k)), "lobby-seat-" + i + "-kick", "sn-lobby-kick");
                    stage.Tips.Hook(kick, stage.T("screen.room.lobby.kick"), stage.T("screen.room.lobby.kick.tip"), TipSide.Below, reading: true);
                }
            }
        }

        // 자리 줄의 주인공 도트(P06 — 방 상태에 주인공이 없으면 그리지 않는다: 옛 방 · 값을 채우지 않은 가짜)
        void Dot(VisualElement row, int i, RoomSeat seat)
        {
            if (!Protagonists.Has(seat.Protagonist)) return;
            var dot = RoomUi.Add(row, SoloScreen.FieldDot(seat.Protagonist), "lobby-seat-" + i + "-proto", "sn-lobby-proto");
            dot.userData = seat.Protagonist;
            dot.pickingMode = PickingMode.Position;
            stage.Tips.Hook(dot, stage.T("screen.solo.protagonist." + seat.Protagonist), null, TipSide.Below, reading: true);
        }

        /// <summary>그 자리 줄에 그려진 주인공(키 — 없으면 null). 시험·확인이 본다.</summary>
        public string ProtagonistShown(int seat) => left?.Q("lobby-seat-" + seat + "-proto")?.userData as string;

        // 「내 주인공」 여덟 칸(P06): 고른 값 = 방 상태의 내 좌석 값(금색). 준비한 뒤에도 바꿀 수 있다(겉모양뿐)
        void Picker()
        {
            var mine = MySeat;
            if (mine == null || !Protagonists.Has(mine.Protagonist)) return;
            var box = RoomUi.Add(left, new VisualElement(), "lobby-proto", "sn-lobby-pick");
            var label = RoomUi.Add(box, Kit.Text(stage.T("screen.room.lobby.protagonist"), Kit.F2), "lobby-proto-label", "sn-lobby-pick-label");
            stage.Tips.Hook(label, stage.T("screen.room.lobby.protagonist"), stage.T("screen.room.lobby.protagonist.tip"), TipSide.Below, reading: true);
            var opts = RoomUi.Add(box, new VisualElement(), "lobby-proto-opts", "sn-lobby-pick-opts");
            foreach (string key in Protagonists.Keys)
            {
                string k = key;
                var opt = RoomUi.Add(opts, new VisualElement(), "lobby-proto-" + k, "sn-opt");
                opt.AddToClassList("sn-proto-opt");
                opt.Add(SoloScreen.FieldDot(k));
                opt.EnableInClassList("sn-opt-picked", mine.Protagonist == k);
                stage.OnTap(opt, SoundKeys.Select, e => PickProtagonist(k));
                stage.Tips.Hook(opt, stage.T("screen.solo.protagonist." + k), null, TipSide.Below, reading: true);   // 이름만(결정 70 ⑤)
            }
        }

        /// <summary>「내 주인공」 칸을 누른다(로비 — 내 자리가 있을 때). 보냈으면 true.</summary>
        public bool PickProtagonist(string key)
        {
            if (shown == null || shown.Phase != RoomPhase.Lobby || MySeat == null || !Protagonists.Has(key)) return false;
            if (ReferenceEquals(flow.Room.Lobby, source)) return flow.Room.PickProtagonist(key);
            source.Send(new PickProtagonist { Key = key });
            return true;
        }

        void Foot()
        {
            string hint;
            var mine = MySeat;
            if (source.IsHost) hint = Members(shown) < 2 ? "screen.room.lobby.hint.alone" : "screen.room.lobby.hint.host";
            else hint = mine != null && mine.Ready ? "screen.room.lobby.hint.guest" : "screen.room.lobby.hint.unready";
            if (!(source.IsHost && CanStart)) RoomUi.Add(left, Kit.Wrapped(stage.T(hint), Kit.F2), "lobby-hint", "sn-lobby-hint");
            var buttons = RoomUi.Add(left, new VisualElement(), "lobby-foot", "sn-foot-row");
            if (source.IsHost)
            {
                var start = RoomUi.Add(buttons, Kit.Button(stage.T("screen.room.lobby.start"), KitButtonKind.Main, () => Start()), "lobby-start", null);
                start.SetEnabled(CanStart);
                RoomUi.Add(buttons, Kit.Button(stage.T("screen.room.lobby.close"), KitButtonKind.Normal, Leave), "lobby-close", null);
            }
            else
            {
                bool ready = mine != null && mine.Ready;
                RoomUi.Add(buttons, Kit.Button(stage.T(ready ? "screen.room.lobby.ready_off" : "screen.room.lobby.ready_on"), ready ? KitButtonKind.Normal : KitButtonKind.Main, () => ToggleReady()), "lobby-ready", null);
                RoomUi.Add(buttons, Kit.Button(stage.T("screen.room.lobby.leave"), KitButtonKind.Normal, Leave), "lobby-leave", null);
                // 튜토리얼 입구(결정 38 [가정 M7-71]): 손님은 자리를 둔 채 튜토리얼을 하고 돌아온다 — 방 흐름이 받는다(가짜 방 · 튜토리얼 데이터 없음이면 단추 없음)
                if (flow.Room.CanTutorial && ReferenceEquals(flow.Room.Lobby, source))
                {
                    var tut = RoomUi.Add(buttons, Kit.Button(stage.T("screen.room.tutorial"), KitButtonKind.Normal, () => Tutorial()), "lobby-tutorial", null);
                    stage.Tips.Hook(tut, stage.T("screen.room.tutorial"), stage.T("screen.room.tutorial.lobby.tip"), TipSide.Above, reading: true);
                }
            }
        }

        /// <summary>[튜토리얼](손님 — 자리를 둔 채, <see cref="RoomFlow.OpenTutorial"/>).</summary>
        public bool Tutorial() => flow.Room.OpenTutorial();

        RoomSeat MySeat => shown != null && source.Seat >= 0 && source.Seat < shown.Seats.Count ? shown.Seats[source.Seat] : null;

        static int Members(RoomState room)
        {
            int n = 0;
            if (room != null) foreach (var s in room.Seats) if (s != null) n++;
            return n;
        }

        /// <summary>[게임 시작]을 누를 수 있나(방 상태의 CanStart — 2명 이상 · 손님 모두 준비 · 모두 접속).</summary>
        public bool CanStart => shown != null && shown.CanStart && shown.Phase == RoomPhase.Lobby;

        /// <summary>[게임 시작](방장).</summary>
        public bool Start()
        {
            if (!source.IsHost || !CanStart) return false;
            source.Send(new StartGame());
            return true;
        }

        /// <summary>[준비 완료]·[준비 취소](손님).</summary>
        public bool ToggleReady()
        {
            var mine = MySeat;
            if (source.IsHost || mine == null) return false;
            source.Send(new SetReady { Ready = !mine.Ready });
            return true;
        }

        /// <summary>[내보내기](방장 — 확인 창 없이 바로, 02 §3-1).</summary>
        public bool Kick(int seat)
        {
            if (!source.IsHost || shown == null || seat <= 0 || seat >= shown.Seats.Count || shown.Seats[seat] == null) return false;
            source.Send(new KickFromLobby { Seat = seat });
            return true;
        }

        /// <summary>[방 닫기](방장) · [나가기](손님): 방을 떠나 타이틀로.</summary>
        public void Leave() => flow.Room.Leave();

        /// <summary>시계 프리셋 이름 키(S06 방 목록 줄 · S08).</summary>
        public static string ClockKey(ClockPreset p) => p == ClockPreset.Relaxed ? "screen.room.set.clock.relaxed" : p == ClockPreset.Normal ? "screen.room.set.clock.normal" : p == ClockPreset.Custom ? "screen.room.set.clock.custom" : "screen.room.set.clock.off";
    }

    /// <summary>
    /// S08 방 설정 판(02 §3-2 표 · 04 §10-5): 캠페인 · 자리 수 · 트레이너 카드 · 울트라 온슬롯·난이도(울트라 버스트만 — 신오의 심장이면 줄째 흐림) · 갤럭시단 조종 · UB·네크로즈마 조종(울트라 버스트만) ·
    /// 갤럭시단 AI(강함은 M8 — 흐림) · 제한 시간(끔 · 느긋하게 · 보통 — 기본 느긋하게, 결정 25) · 채팅(기본 끔 — 게임 중에도, 결정 140 ②) · 거래. 방장은 값을 눌러 바꾸고(<see cref="ChangeSettings"/> — 방이 검사한다),
    /// 손님은 읽기만(「방장만 바꿀 수 있습니다.」). 규칙 세부·저장은 줄이 없다(기본값 · 자동 — 02 §3-2). 게임 중 바꾸기는 화면 ②(S22).
    /// 제한 시간 세부(결정 140 ①): 시계가 켜져 있으면 프리셋 줄 아래 두 열 칸 열(<c>room-set-clock-grid</c>) — 칸마다 이름 · 방장 [−] 값 [+] / 손님 값 + 「변경됨」.
    /// 요소 이름: <c>room-set-row-&lt;줄&gt;</c> · <c>room-set-opt-&lt;줄&gt;-&lt;값&gt;</c>(시계 줄 값 custom = 직접 설정) · <c>room-set-clock-&lt;칸&gt;</c>(+ <c>-label</c> · <c>-value</c> · <c>-minus</c> · <c>-plus</c> · <c>-changed</c>) · <c>room-set-clock-changed</c> · <c>room-set-readonly</c>.
    /// </summary>
    public sealed class RoomSettingsPanel
    {
        public const string Campaign = "campaign", Seats = "seats", TrainerCards = "trainer_cards", Onslaught = "onslaught", UbDifficulty = "ub_difficulty",
            Galactic = "galactic", Ultra = "ultra", GalacticAi = "galactic_ai", Clock = "clock", Trading = "trading", Pause = "pause", Chat = "chat", Undo = "undo";

        readonly UiStage stage;
        readonly ILobbySource source;
        RoomSettings now;
        int members;

        public RoomSettingsPanel(UiStage stage, ILobbySource source)
        {
            this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>
        /// 판을 그 방 상태로 다시 세운다. 방장은 로비에서 모든 칸, 이어하기 대기실 · 게임 중에는 게임 중에 바꿀 수 있는 칸(제한 시간 · 상대 조작 · 갤럭시단 AI — <see cref="RoomSettings.InGameChange"/>, [가정 M7-13 · M7-23])만 바꾼다.
        /// liveOnly = 게임 중에 바꿀 수 있는 줄만 보인다(게임 메뉴 S22의 [방 설정]).
        /// </summary>
        public void Build(VisualElement into, RoomState room, bool liveOnly = false)
        {
            into.Clear();
            now = room != null && room.Settings != null ? room.Settings : new RoomSettings();
            members = 0;
            if (room != null) foreach (var s in room.Seats) if (s != null) members++;
            bool all = source.IsHost && room != null && room.Phase == RoomPhase.Lobby;
            bool live = all || (source.IsHost && room != null && (room.Phase == RoomPhase.Waiting || room.Phase == RoomPhase.InGame));
            bool ub = now.Campaign == Sinnoh.Rules.Campaign.UltraBurst;
            RoomUi.Add(into, Kit.Text(stage.T("screen.room.set.title"), Kit.FTitle), "room-set-head", null);
            if (!liveOnly) Fixed(into, all, ub);
            Live(into, live, ub);
            ChatRow(into, live);   // 채팅(결정 140 ②) — 게임 중에도 바꾼다
            if (!liveOnly) TradingRow(into, all);
            if (!liveOnly) PauseRow(into, all);
            if (!liveOnly) UndoRow(into, all);
            if (!source.IsHost) RoomUi.Add(into, Kit.Text(stage.T("screen.room.set.readonly"), Kit.F1), "room-set-readonly", "sn-room-readonly").AddToClassList("kit-muted");
            else if (!all) RoomUi.Add(into, Kit.Wrapped(stage.T("screen.room.set.live_only"), Kit.F1), "room-set-live-only", "sn-room-readonly").AddToClassList("kit-muted");
        }

        // 게임 전에만 바꾸는 줄(캠페인 · 자리 수 · 트레이너 카드 · 온슬롯 · 난이도 — 거래는 TradingRow)
        void Fixed(VisualElement into, bool edit, bool ub)
        {
            var o = Row(into, Campaign, "screen.solo.campaign", true);
            Opt(o, Campaign, "hos", "screen.solo.campaign.hos", now.Campaign == Sinnoh.Rules.Campaign.HeartOfSinnoh, true, "screen.solo.campaign.hos.tip", edit, s => s.Campaign = Sinnoh.Rules.Campaign.HeartOfSinnoh);
            bool ubReady = SoloGame.CampaignReady(Sinnoh.Rules.Campaign.UltraBurst);
            Opt(o, Campaign, "ub", "screen.solo.campaign.ub", ub, ubReady, ubReady ? "screen.solo.campaign.ub.tip" : "screen.common.not_ready", edit, s => s.Campaign = Sinnoh.Rules.Campaign.UltraBurst);
            o = Row(into, Seats, "screen.room.create.seats", true);
            for (int n = RoomSettings.MinSeats; n <= SeatId.MaxSeats; n++)
            {
                int k = n;
                string text = stage.T("screen.room.create.seats.n").Replace("{n}", n.ToString(CultureInfo.InvariantCulture));
                bool ok = n >= members;
                Add(o, Seats, n.ToString(CultureInfo.InvariantCulture), text, now.Seats == n, ok, ok ? null : stage.T("screen.room.set.seats.below"), edit, s => s.Seats = k);
            }
            o = Row(into, TrainerCards, "screen.solo.trainer_cards", true);
            Opt(o, TrainerCards, "on", "screen.common.on", now.TrainerCards, true, "screen.solo.trainer_cards.on.tip", edit, s => s.TrainerCards = true);   // 「테스트 중인 기능」(결정 134)
            Opt(o, TrainerCards, "off", "screen.common.off", !now.TrainerCards, true, "screen.solo.trainer_cards.tip", edit, s => s.TrainerCards = false);
            o = Row(into, Onslaught, "screen.solo.onslaught", ub);
            Opt(o, Onslaught, "off", "screen.common.off", ub && !now.UltraOnslaught, ub, ub ? "screen.solo.onslaught.tip" : "screen.solo.ub_only", edit, s => s.UltraOnslaught = false);
            Opt(o, Onslaught, "on", "screen.common.on", ub && now.UltraOnslaught, ub, ub ? "screen.solo.onslaught.tip" : "screen.solo.ub_only", edit, s => s.UltraOnslaught = true);
            o = Row(into, UbDifficulty, "screen.solo.ub_difficulty", ub);
            Opt(o, UbDifficulty, "new", "screen.solo.ub_difficulty.new", ub && now.UbDifficulty == Sinnoh.Rules.UbDifficulty.NewTrainer, ub, ub ? "screen.solo.ub_difficulty.new.tip" : "screen.solo.ub_only", edit, s => s.UbDifficulty = Sinnoh.Rules.UbDifficulty.NewTrainer);
            Opt(o, UbDifficulty, "veteran", "screen.solo.ub_difficulty.veteran", ub && now.UbDifficulty == Sinnoh.Rules.UbDifficulty.VeteranTrainer, ub, ub ? "screen.solo.ub_difficulty.veteran.tip" : "screen.solo.ub_only", edit, s => s.UbDifficulty = Sinnoh.Rules.UbDifficulty.VeteranTrainer);
            Opt(o, UbDifficulty, "master", "screen.solo.ub_difficulty.master", ub && now.UbDifficulty == Sinnoh.Rules.UbDifficulty.MasterTrainer, ub, ub ? "screen.solo.ub_difficulty.master.tip" : "screen.solo.ub_only", edit, s => s.UbDifficulty = Sinnoh.Rules.UbDifficulty.MasterTrainer);
        }

        void TradingRow(VisualElement into, bool edit)
        {
            var o = Row(into, Trading, "screen.solo.trading", true);
            bool on = GameFlow.TradingReady && now.Trading;   // 결정 124: 준비 중에는 값과 상관없이 「사용 안 함」이 골라져 보인다(거래가 켜진 채 저장된 방 설정 · 손님 읽기도 — 판은 TradeLocked로 잠근다)
            Opt(o, Trading, "on", "screen.common.on", on, GameFlow.TradingReady, GameFlow.TradingReady ? "screen.room.set.trading.tip" : "screen.common.not_ready", edit, s => s.Trading = true);
            Opt(o, Trading, "off", "screen.common.off", !on, true, "screen.room.set.trading.tip", edit, s => s.Trading = false);
        }

        // 방 채팅(결정 140 ② — 기본 끔, 로비 · 게임 중 모두 방장이 바꾼다 [가정 plan §5 #750])
        void ChatRow(VisualElement into, bool edit)
        {
            var o = Row(into, Chat, "screen.room.set.chat", true);
            Opt(o, Chat, "on", "screen.common.on", now.Chat, true, "screen.room.set.chat.tip", edit, s => s.Chat = true);
            Opt(o, Chat, "off", "screen.common.off", !now.Chat, true, "screen.room.set.chat.tip", edit, s => s.Chat = false);
        }

        // 일시정지를 거는 사람(결정 112 Q6 — isekai PauseMode, 로비에서만): 방장만 · 모두 · 투표
        void PauseRow(VisualElement into, bool edit)
        {
            var o = Row(into, Pause, "screen.room.set.pause", true);
            Opt(o, Pause, "host", "screen.room.set.pause.host", now.Pause == PauseMode.HostOnly, true, "screen.room.set.pause.host.tip", edit, s => s.Pause = PauseMode.HostOnly);
            Opt(o, Pause, "everyone", "screen.room.set.pause.everyone", now.Pause == PauseMode.Everyone, true, "screen.room.set.pause.everyone.tip", edit, s => s.Pause = PauseMode.Everyone);
            Opt(o, Pause, "vote", "screen.room.set.pause.vote", now.Pause == PauseMode.Vote, true, "screen.room.set.pause.vote.tip", edit, s => s.Pause = PauseMode.Vote);
        }

        // 무르기(결정 149 · 154 — 새 방 기본 끔, 로비에서만): 사용 안 함 · 내 턴만 · 자유(방장은 메뉴의 「되돌리기」로 모두를 지난 턴으로). 글은 혼자 하기 줄과 같고 「자유」 툴팁만 방 몫
        void UndoRow(VisualElement into, bool edit)
        {
            var o = Row(into, Undo, "screen.solo.undo", true);
            Opt(o, Undo, "off", "screen.solo.undo.off", now.Undo == UndoMode.Off, true, "screen.solo.undo.off.tip", edit, s => s.Undo = UndoMode.Off);
            Opt(o, Undo, "turn", "screen.solo.undo.turn", now.Undo == UndoMode.Turn, true, "screen.solo.undo.turn.tip", edit, s => s.Undo = UndoMode.Turn);
            Opt(o, Undo, "free", "screen.solo.undo.free", now.Undo == UndoMode.Free, true, "screen.room.set.undo.free.tip", edit, s => s.Undo = UndoMode.Free);
        }

        // 게임 중에도 바꾸는 줄(상대 조작 둘 · 갤럭시단 AI · 제한 시간)
        void Live(VisualElement into, bool edit, bool ub)
        {
            // 울트라 버스트 모드는 상대를 사람이 조종하지 않는다(결정 121): 두 줄 모두 「AI 조종」에 잠근다 — 사람 조종으로 저장된 판도 AI로 보인다
            var o = Row(into, Galactic, "screen.room.set.galactic", !ub);
            Opt(o, Galactic, "ai", "screen.room.set.control.ai", ub || now.Galactic == Control.Ai, !ub, ub ? "screen.room.set.control.ub_lock.tip" : "screen.room.set.control.ai.tip", edit, s => s.Galactic = Control.Ai);
            Opt(o, Galactic, "human", "screen.room.set.control.human", !ub && now.Galactic == Control.Human, !ub, ub ? "screen.room.set.control.ub_lock.tip" : "screen.room.set.control.human.tip", edit, s => s.Galactic = Control.Human);
            o = Row(into, Ultra, "screen.room.set.ultra", false);
            Opt(o, Ultra, "ai", "screen.room.set.control.ai", ub, false, ub ? "screen.room.set.control.ub_lock.tip" : "screen.solo.ub_only", edit, s => s.UltraBeast = Control.Ai);
            Opt(o, Ultra, "human", "screen.room.set.control.human", false, false, ub ? "screen.room.set.control.ub_lock.tip" : "screen.solo.ub_only", edit, s => s.UltraBeast = Control.Human);
            o = Row(into, GalacticAi, "screen.solo.galactic_ai", true);
            Opt(o, GalacticAi, "basic", "screen.solo.galactic_ai.basic", now.GalacticAi == GalacticAiLevel.Principle, SoloGame.AiReady(GalacticAiLevel.Principle), "screen.solo.galactic_ai.basic.tip", edit, s => s.GalacticAi = GalacticAiLevel.Principle);
            bool strong = SoloGame.AiReady(GalacticAiLevel.Strong);
            Opt(o, GalacticAi, "strong", "screen.solo.galactic_ai.strong", now.GalacticAi == GalacticAiLevel.Strong, strong, strong ? "screen.solo.galactic_ai.strong.tip" : "screen.common.not_ready", edit, s => s.GalacticAi = GalacticAiLevel.Strong);
            o = Row(into, Clock, "screen.room.set.clock", true);
            o.parent.AddToClassList("sn-room-clock-row");   // 「직접 설정」 · 「변경됨」이 붙으면 값 칸이 다음 줄로 넘어간다(결정 140 ①)
            foreach (var p in new[] { ClockPreset.Off, ClockPreset.Relaxed, ClockPreset.Normal })
            {
                var k = p;
                string key = LobbyScreen.ClockKey(p);
                Opt(o, Clock, p.ToString().ToLowerInvariant(), key, now.Clock == p, true, key + ".tip", edit, s => { s.Clock = k; s.Custom = null; });
            }
            // 결정 140 ①: 칸을 고쳐 프리셋과 달라지면 「직접 설정」(누르는 칸이 아니다 — 프리셋을 누르면 그 값으로 돌아간다)
            if (now.Clock == ClockPreset.Custom) Opt(o, Clock, "custom", "screen.room.set.clock.custom", true, true, "screen.room.set.clock.custom.tip", false, null);
            ClockGrid(into, o, edit);
        }

        // ── 제한 시간 세부(결정 140 ① — isekai 방 설정 스테퍼): 두 열 칸, 방장은 [−] 값 [+], 손님은 값 + 바뀐 칸 「변경됨」 ──

        RoomSettings seen;
        bool wasReady;
        readonly HashSet<string> changed = new HashSet<string>();

        /// <summary>손님 화면에 「변경됨」이 붙은 칸(시계 줄 = <see cref="Clock"/>, 칸 = <see cref="ClockField.Key"/>). 손님이 [준비 완료]를 누르면 비운다.</summary>
        public IReadOnlyCollection<string> Changed => changed;

        void ClockGrid(VisualElement into, VisualElement presetRow, bool edit)
        {
            var lengths = now.ClockLengths();
            Mark(lengths);
            if (!edit && changed.Contains(Clock)) ChangedBadge(presetRow, "room-set-clock-changed");
            if (!lengths.On) return;
            var grid = RoomUi.Add(into, new VisualElement(), "room-set-clock-grid", "sn-room-clock-grid");
            foreach (var f in ClockSettings.Fields)
            {
                string name = "room-set-clock-" + f.Key;
                var cell = RoomUi.Add(grid, new VisualElement(), name, "sn-room-clock-cell");
                var label = RoomUi.Add(cell, Kit.Text(stage.T("screen.room.set.clock." + f.Key), Kit.F2), name + "-label", "sn-room-clock-label");
                string range = stage.T("screen.room.set.clock.range").Replace("{min}", Span(f.MinMs)).Replace("{max}", Span(f.MaxMs)).Replace("{step}", Span(f.StepMs));
                stage.Tips.Hook(label, label.text, stage.T("screen.room.set.clock." + f.Key + ".tip") + " " + range, TipSide.Below, reading: true);
                int v = f.Get(lengths);
                if (edit) StepButton(cell, f.Key, -1, v > f.MinMs);
                RoomUi.Add(cell, Kit.Text(ClockView.Text(v), Kit.F2), name + "-value", "sn-room-clock-value");
                if (edit) StepButton(cell, f.Key, +1, v < f.MaxMs);
                else if (changed.Contains(f.Key)) ChangedBadge(cell, name + "-changed");
            }
        }

        // 손님: 지난번에 그린 방 설정과 견줘 바뀐 칸을 모은다(켜고 끈 것은 시계 줄만) — 내가 [준비 완료]를 누르면 비운다
        void Mark(ClockSettings lengths)
        {
            if (source.IsHost)
            {
                changed.Clear();
                seen = now;
                return;
            }
            var room = source.Room;
            bool ready = room != null && source.Seat >= 0 && source.Seat < room.Seats.Count && room.Seats[source.Seat] != null && room.Seats[source.Seat].Ready;
            if (ready && !wasReady) changed.Clear();
            wasReady = ready;
            if (seen != null && !ReferenceEquals(seen, now))
            {
                var old = seen.ClockLengths();
                if (seen.Clock != now.Clock) changed.Add(Clock);
                if (old.On && lengths.On)
                    foreach (var f in ClockSettings.Fields)
                        if (f.Get(old) != f.Get(lengths)) changed.Add(f.Key);
            }
            seen = now;
        }

        void ChangedBadge(VisualElement into, string name)
        {
            var badge = RoomUi.Add(into, Kit.Badge(stage.T("screen.room.set.changed"), KitBadgeKind.Gold), name, "sn-room-clock-changed");
            stage.Tips.Hook(badge, null, stage.T("screen.room.set.changed.tip"), TipSide.Below, reading: true);
        }

        // [−] · [+]: 값 칸 모양(sn-opt) — 끝에 닿으면 흐림
        void StepButton(VisualElement cell, string key, int dir, bool ready)
        {
            var b = RoomUi.Add(cell, new VisualElement(), "room-set-clock-" + key + (dir < 0 ? "-minus" : "-plus"), "sn-opt");
            b.AddToClassList("sn-room-clock-step");
            b.Add(Kit.Text(dir < 0 ? "-" : "+", Kit.F2));
            b.EnableInClassList("sn-off", !ready);
            if (ready) stage.OnTap(b, SoundKeys.Select, e => StepClock(key, dir));
        }

        // 범위 글의 시간(15초 · 5분 / 15s · 5 min)
        string Span(int ms)
        {
            int s = ms / 1000;
            return s > 0 && s % 60 == 0 ? stage.T("screen.room.set.clock.min").Replace("{n}", (s / 60).ToString(CultureInfo.InvariantCulture)) : stage.T("screen.room.set.clock.sec").Replace("{n}", s.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>방장: 시계 칸 하나를 한 번 옮겨 보낸다(<see cref="RoomSettings.StepClock"/> — 프리셋과 같아지면 그 프리셋). 끝에 닿았거나 손님이면 false.</summary>
        public bool StepClock(string key, int dir)
        {
            if (!source.IsHost || now == null || !now.Clone().StepClock(key, dir)) return false;
            return Change(s => s.StepClock(key, dir));
        }

        /// <summary>방장: 제한 시간 프리셋을 누른다(직접 설정을 비운다).</summary>
        public bool PickClock(ClockPreset preset) => preset != ClockPreset.Custom && Change(s => { s.Clock = preset; s.Custom = null; });

        VisualElement Row(VisualElement into, string id, string labelKey, bool on)
        {
            var opts = RoomUi.Row(into, stage, "room-set-row-" + id, labelKey);
            opts.parent.EnableInClassList("sn-off", !on);
            return opts;
        }

        void Opt(VisualElement opts, string row, string id, string textKey, bool picked, bool ready, string tipKey, bool edit, Action<RoomSettings> pick) =>
            Add(opts, row, id, stage.T(textKey), picked, ready, tipKey != null ? stage.T(tipKey) : null, edit, pick);

        void Add(VisualElement opts, string row, string id, string text, bool picked, bool ready, string tip, bool edit, Action<RoomSettings> pick) =>
            RoomUi.Opt(opts, stage, "room-set-opt-" + row + "-" + id, text, picked, ready, tip, edit ? () => Change(pick) : (Action)null);

        /// <summary>방장: 지금 설정에서 그 칸만 바꿔 보낸다(틀린 값이면 보내지 않는다 — 방이 다시 검사한다). 보냈으면 true.</summary>
        public bool Change(Action<RoomSettings> pick)
        {
            if (!source.IsHost || now == null || pick == null) return false;
            var next = now.Clone();
            pick(next);
            if (next.Problem(members) != null) return false;
            source.Send(new ChangeSettings { Settings = next });
            RoomIdentity.ClockMemo = next.ClockMemo();   // 다음 새 방의 제한 시간(결정 140 ①)
            return true;
        }
    }
}
