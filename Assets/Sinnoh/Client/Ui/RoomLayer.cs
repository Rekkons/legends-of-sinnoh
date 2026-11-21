using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Net;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 방 판의 화면 덮개 층(M7 「화면 ②」 — 02 §5-3 끝 줄 · §5-7 · §6, 04 S11 · S22): 방 판(<see cref="RoomFlow.InGame"/>)에서만 문서 뿌리 맨 위(툴팁 바로 아래)에 붙는다. 매 프레임 <see cref="Tick"/>(GameFlow.Tick).
    /// ① 응답 시계: 좌석 넷 이하면 위 띠 상태 판의 턴 배지 앞 한 줄(결정 112 Q5 — 「내 시간 1:23 · 예비 2:00」 · 「대기 0:23 · ○○」), 다섯 이상은 위 띠 아래 가운데 줄 「내 시간 1:23 · 예비 시간 2:00」 · 「응답 대기 중: ○○ 0:23」(남은 10초 = 빨강 + 내 시계는 소리 한 번 [가정 M7-54]) ·
    /// ② 일시정지 띠(결정 112 Q6 — 건 사람 모두 · 지난 시간 · 내 것 [일시정지 풀기] · 방장 [모두 해제] / 예약 줄 + 내 예약 [예약 취소]) · ③ 투표 판(강퇴 · 자리 비움 — 찬성 수 · 남은 시간 · 투표할 사람에게 [찬성]·[반대]) · ④ 알림 줄(5초 — 끊김 · 돌아옴 · 시간 초과 · 자리 비움 · 투표 결과 · 저장 실패).
    /// 끊기면 화면 전체를 옅게 덮는(결정 112 Q6 — 뒤 보드가 보인다) S11 재접속 덮개: 다시 붙는 중(3초마다 · 지난 초) → 실패면 이유(<see cref="NetFailText"/> — 방장이 방을 닫음 · 내보내짐 · 3분 안에 못 붙음 …) + [다시 시도](쓸 수 있을 때) · [타이틀로].
    /// ⑥ 남의 고르기 보기(M9 다듬기 P09, 02 §4-10 — RoomLayer.Peek.cs): 다른 좌석의 공개 선택(조우 카드 · 사당 카드 · 은행상회 줄) 후보 줄 + 그 좌석이 가리키는 · 누른 칸의 좌석 색 표시.
    /// ⑤ 소통(결정 97 · 102, 02 §4-8 — RoomLayer.Social.cs): 핑(보드의 누른 자리 · 보드 밖 공개 대상 · 종류 넷 · 종류 고리) · 감정 표현(내 좌석 칩 오른쪽 클릭 · 칩 아래 메뉴 · 좌석 얼굴 옆 말풍선 · 상대별 끄기 — 결정 112 Q12) · 도장(기록 줄).
    /// 줄의 글은 문자열 표 <c>screen.room.clock.*</c>·<c>pause.*</c>·<c>vote.*</c>·<c>notice.*</c>·<c>reconnect.*</c>. 자리·색은 <c>sn-room-layer*</c>.
    /// 요소 이름: <c>room-layer</c> · <c>room-clock</c>(+ <c>-mine</c> · <c>-wait</c>) · <c>room-pause</c>(+ <c>-text</c> · <c>-reserved</c> · <c>-off</c> · <c>-cancel</c> · <c>-all</c>) · <c>room-vote</c>(+ <c>-title</c> · <c>-count</c> · <c>-yes</c> · <c>-no</c> · <c>-state</c>) ·
    /// <c>room-notice-&lt;n&gt;</c> · <c>room-reconnect</c>(+ <c>-title</c> · <c>-status</c> · <c>-fail</c> · <c>-retry</c> · <c>-leave</c>).
    /// </summary>
    public sealed partial class RoomLayer
    {
        readonly GameFlow flow;
        VisualElement layer, col, strip, pause, wild, vote, notes, cover, pings;
        string stripKey, pauseKey, wildKey, voteKey, notesKey, coverKey;
        int lowFor = -1, problemsSeen;
        long problemAt = -1;
        bool pausedSeen, undoWaitSeen;

        public RoomLayer(GameFlow flow) => this.flow = flow ?? throw new ArgumentNullException(nameof(flow));

        UiStage Stage => flow.Stage;

        /// <summary>층 요소(붙어 있지 않으면 parent = null).</summary>
        public VisualElement Element => layer;

        /// <summary>재접속 덮개(S11)가 떠 있나.</summary>
        public bool CoverUp => cover != null && cover.parent != null && cover.style.display != DisplayStyle.None;

        /// <summary>응답 시계 줄의 지금 글(시험 · 확인 — 없으면 빈 글).</summary>
        public string ClockText { get; private set; } = "";

        /// <summary>내 시계가 남은 10초 안에 들어 소리를 낸 수(확인용).</summary>
        public int LowCues { get; private set; }

        public void Tick()
        {
            var live = flow.Room.InGame ? flow.Room.Live : null;
            if (live == null || Stage.Root == null)
            {
                if (layer != null && layer.parent != null) layer.RemoveFromHierarchy();
                if (strip != null && col != null && strip.parent != col) col.Insert(0, strip);   // 위 띠에 옮겨 둔 시계 줄도 걷는다(결정 112 Q5)
                stripKey = pauseKey = wildKey = voteKey = notesKey = coverKey = peekKey = null;
                ClockText = "";
                SocialOff();
                ChatGone();
                return;
            }
            Ensure();
            Attach();
            bool covered = Cover(live);
            col.style.display = covered ? DisplayStyle.None : DisplayStyle.Flex;
            Strip(live);
            Pause(live);
            Wild(live);
            Peek(live);
            Vote(live);
            Notes(live);
            if (live.Paused != pausedSeen || flow.UndoWaiting != undoWaitSeen)
            {
                pausedSeen = live.Paused;
                undoWaitSeen = flow.UndoWaiting;
                flow.Hud?.Refresh();   // [무르기]는 일시정지 중 꺼진다(GameFlow.UndoReady — 제안은 다음 갱신에야 Paused로 온다) · 보낸 무르기를 기다리다 거절 · 시간으로 풀리면 다시 켠다
            }
            Social(live, covered);
            (Stage.Info as BoardMenuScreen ?? Stage.Current as BoardMenuScreen)?.SyncRoom(live.Room);   // 게임 메뉴의 방 설정 쪽(결정 140 ①)
            ChatTick(live, covered);
        }

        /// <summary>화면 글의 언어가 바뀌었다(<see cref="GameFlow.Lang"/>): 층을 걷어 다음 <see cref="Tick"/>이 새 표의 글로 다시 만든다(소통 메뉴 단추 글까지 — 받은 핑 · 말풍선은 방 상태에서 다시 그린다).</summary>
        public void Relabel()
        {
            if (layer == null) return;
            SocialOff();
            ChatOff();
            layer.RemoveFromHierarchy();
            layer = null;
            bubbles.Clear();
            pingMarks.Clear();
            markChips.Clear();
            stripKey = pauseKey = wildKey = voteKey = notesKey = coverKey = peekKey = null;
        }

        void Ensure()
        {
            if (layer != null) return;
            layer = new VisualElement { name = "room-layer", pickingMode = PickingMode.Ignore };
            layer.AddToClassList("sn-room-layer");
            pings = Add(layer, new VisualElement { pickingMode = PickingMode.Ignore }, "room-pings", "sn-room-layer");   // 핑 표시(줄 · 덮개 밑)
            col = Add(layer, new VisualElement { pickingMode = PickingMode.Ignore }, "room-layer-col", "sn-room-layer-col");
            SocialEnsure();
            strip = Add(col, new VisualElement { pickingMode = PickingMode.Ignore }, "room-clock", "sn-room-strip");
            pause = Add(col, new VisualElement(), "room-pause", "sn-room-strip");
            wild = Add(col, new VisualElement { pickingMode = PickingMode.Ignore }, "room-wild", "sn-room-strip");
            peek = Add(col, new VisualElement { pickingMode = PickingMode.Ignore }, "room-peek", "sn-room-strip");
            peek.AddToClassList("sn-room-peek");   // 남의 고르기 보기(P09 — RoomLayer.Peek.cs)
            vote = Add(col, Kit.Panel(Stage.Skin), "room-vote", "sn-room-vote");
            notes = Add(col, new VisualElement { pickingMode = PickingMode.Ignore }, "room-notices", null);
            cover = Add(layer, new VisualElement(), "room-reconnect-page", "sn-room-cover");
        }

        // 문서 뿌리 맨 위(툴팁 층 바로 아래) — 화면 · 윗층(연출 덮개)이 나중에 붙어도 늘 위로(튜토리얼 층과 같은 꼴)
        void Attach()
        {
            var root = Stage.Root;
            var tips = Stage.Tips != null ? Stage.Tips.Element : null;
            if (layer.parent != root) root.Add(layer);
            if (tips != null && tips.parent == root)
            {
                if (root.IndexOf(layer) != root.IndexOf(tips) - 1) layer.PlaceBehind(tips);
            }
            else if (root.IndexOf(layer) != root.childCount - 1) layer.BringToFront();
        }

        // ── ① 응답 시계 ──

        void Strip(RoomLive live)
        {
            var c = live.Clock;
            long elapsed = ClockView.Elapsed(c, live.NowMs - live.ClockAtMs);
            int me = live.Seat;
            string mine = null, wait = null;
            bool mineLow = false, waitLow = false;
            var mySeat = c != null && me >= 0 && me < c.Seats.Count ? c.Seats[me] : null;
            if (mySeat != null && mySeat.Running != ClockKind.None)
            {
                mine = Stage.T("screen.room.clock.mine").Replace("{t}", ClockView.Text(ClockView.LeftMs(mySeat, elapsed)));
                if (ClockView.UsesBank(mySeat.Running)) mine += " · " + Stage.T("screen.room.clock.bank").Replace("{t}", ClockView.Text(ClockView.BankMs(mySeat, elapsed)));
                mineLow = ClockView.Low(mySeat, elapsed);
                if (mineLow && lowFor != mySeat.ChoiceId)
                {
                    lowFor = mySeat.ChoiceId;
                    LowCues++;
                    Stage.Tap(SoundKeys.Timer);   // 남은 10초 소리(M9 소리 ③ — 응답 시계 키 timer, [가정 M7-54])
                }
            }
            var waiting = ClockView.Waiting(c, me);
            if (waiting.Count > 0)
            {
                var s = c.Seats[waiting[0]];
                wait = Stage.T("screen.room.clock.wait").Replace("{name}", Name(live, waiting[0])).Replace("{t}", ClockView.Text(ClockView.TotalMs(s, elapsed)));
                waitLow = ClockView.Low(s, elapsed);
            }
            else if (mine == null && (c == null || c.Preset == ClockPreset.Off))
            {
                // 시계를 끈 방: 남은 시간 없이 누구를 기다리는지만(열린 선택이 있는 남 좌석 — 가상 응답자는 뺀다)
                int seat = Pending(me);
                if (seat >= 0) wait = Stage.T("screen.room.clock.wait_off").Replace("{name}", Name(live, seat));
            }
            if (wait != null && waiting.Count > 1) wait += " " + Stage.T("screen.room.clock.more").Replace("{k}", (waiting.Count - 1).ToString(CultureInfo.InvariantCulture));
            ClockText = mine != null && wait != null ? mine + " / " + wait : mine ?? wait ?? "";
            bool band = PlaceStrip(live);
            if (band)
            {
                // 위 띠 안(결정 112 Q5): 한 줄 — 내 시계가 돌면 그것만 「내 시간 1:23 · 예비 2:00」, 아니면 「대기 0:23 · 이름」(넘치면 이름 쪽이 「…」 — 시간은 앞에 둔다) [가정 M7-90]
                if (mine != null)
                {
                    mine = Stage.T("screen.room.clock.mine").Replace("{t}", ClockView.Text(ClockView.LeftMs(mySeat, elapsed)));
                    if (ClockView.UsesBank(mySeat.Running)) mine += " · " + Stage.T("screen.room.clock.bank_short").Replace("{t}", ClockView.Text(ClockView.BankMs(mySeat, elapsed)));
                    wait = null;
                }
                else if (waiting.Count > 0)
                {
                    wait = Stage.T("screen.room.clock.band_wait").Replace("{t}", ClockView.Text(ClockView.TotalMs(c.Seats[waiting[0]], elapsed))).Replace("{name}", Name(live, waiting[0]));
                    if (waiting.Count > 1) wait += " " + Stage.T("screen.room.clock.more").Replace("{k}", (waiting.Count - 1).ToString(CultureInfo.InvariantCulture));
                }
            }
            string key = (mine ?? "") + "|" + mineLow + "|" + (wait ?? "") + "|" + waitLow + "|" + band;
            if (key == stripKey) return;
            stripKey = key;
            strip.Clear();
            strip.style.display = mine != null || wait != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (mine != null) Line(strip, "room-clock-mine", mine, mineLow);
            if (wait != null) Line(strip, "room-clock-wait", wait, waitLow);
        }

        /// <summary>위 띠 안에 시계를 둘 좌석 수의 상한(결정 112 Q5): 이보다 많으면 1080 벌 위 띠에 좌석 칩(한글 6자 고정 폭 — Q15)과 상태 판만으로 자리가 없어 옛 자리(위 띠 아래 가운데)에 둔다 [가정 M7-90].</summary>
        public const int BandClockSeats = 4;

        /// <summary>응답 시계 줄이 위 띠 안(상태 판의 턴 배지 바로 앞)에 있나.</summary>
        public bool ClockInBand => strip != null && strip.parent != null && strip.parent.name == "board-hud-status";

        // 응답 시계 줄의 자리(결정 112 Q5): 좌석이 BandClockSeats 이하이고 보드 HUD가 있으면 상태 판의 턴 배지 앞, 아니면 가운데 줄 맨 위. 위 띠에 있으면 true
        bool PlaceStrip(RoomLive live)
        {
            int seats = live.Room != null ? live.Room.Seats.Count : 0;
            var status = seats <= BandClockSeats ? Stage.Root.Q("board-hud-status") : null;
            var turn = status != null ? status.Q("board-hud-turn") : null;
            if (turn != null && turn.parent == status)
            {
                if (strip.parent != status || status.IndexOf(strip) != status.IndexOf(turn) - 1)
                {
                    strip.RemoveFromHierarchy();
                    status.Insert(status.IndexOf(turn), strip);
                }
                strip.EnableInClassList("sn-room-band-clock", true);
                return true;
            }
            if (strip.parent != col)
            {
                strip.RemoveFromHierarchy();
                col.Insert(0, strip);
            }
            strip.EnableInClassList("sn-room-band-clock", false);
            return false;
        }

        // 내 보기에서 답을 기다리는 남 좌석(사람 좌석만 — 시계를 끈 방)
        int Pending(int me)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (view == null) return -1;
            foreach (var p in view.Pending)
                if (!p.Answered && p.Seat != me && p.Seat >= 0 && p.Seat < view.Seats.Length) return p.Seat;
            return -1;
        }

        void Line(VisualElement into, string name, string text, bool low)
        {
            var l = Add(into, Kit.Text(text, Kit.F2), name, "sn-room-strip-text");
            l.pickingMode = PickingMode.Ignore;
            l.EnableInClassList("sn-room-clock-low", low);
        }

        string Name(RoomLive live, int seat) => live.NameOf(seat) ?? flow.SeatName(seat);

        // ── ② 일시정지 ──

        long pausedSince = -1;

        /// <summary>일시정지 띠의 지금 글(시험 · 확인 — 멈추지도 예약하지도 않았으면 빈 글).</summary>
        public string PauseText { get; private set; } = "";

        // 일시정지 띠(결정 112 Q6 — isekai PauseBand를 옮김): 「일시정지 중 · 건 사람: A, B · 0:42 지남」 + 내가 건 것이면 [일시정지 풀기] · 방장은 남의 것이 있으면 [모두 해제].
        // 멈추지 않았는데 예약이 있으면 「일시정지 예약: A」(+ 내 예약이면 [예약 취소]). 띠는 마우스를 받지 않고 단추만 받는다 — 보드 · 카드 · 기록은 그대로 본다
        void Pause(RoomLive live)
        {
            var c = live.Clock;
            bool on = live.Paused;
            if (!on) pausedSince = -1;
            else if (pausedSince < 0) pausedSince = live.NowMs;
            var reserved = live.PauseReserved;
            string text = null;
            if (on)
            {
                var names = new List<string>();
                bool system = false;
                foreach (int s in c.PausedBy)
                    if (s == HostSession.SystemPause) system = true;
                    else names.Add(Name(live, s));
                string who = names.Count > 0 ? Stage.T("screen.room.pause.by").Replace("{name}", string.Join(", ", names)) : null;
                if (system) who = who == null ? Stage.T("screen.room.pause.system") : who + " · " + Stage.T("screen.room.pause.system");
                text = Stage.T("screen.room.pause.title") + " · " + who + " · " + Stage.T("screen.room.pause.since").Replace("{t}", ClockView.Text(Math.Max(0, live.NowMs - pausedSince)));
            }
            else if (reserved.Count > 0)
            {
                var names = new List<string>();
                foreach (int s in reserved) names.Add(Name(live, s));
                text = Stage.T("screen.room.pause.reserved").Replace("{name}", string.Join(", ", names));
            }
            bool mineReserved = !on && reserved.Contains(live.Seat);
            string key = (text ?? "") + "|" + live.CanUnpause + "|" + live.CanClearAll + "|" + mineReserved;
            PauseText = text ?? "";
            if (key == pauseKey) return;
            pauseKey = key;
            pause.Clear();
            pause.style.display = text != null ? DisplayStyle.Flex : DisplayStyle.None;
            pause.pickingMode = PickingMode.Ignore;
            if (text == null) return;
            var line = Add(pause, Kit.Text(text, Kit.F2), on ? "room-pause-text" : "room-pause-reserved", "sn-room-strip-text");
            if (!on) Stage.Tips.Hook(line, null, Stage.T("screen.room.pause.reserved.tip"), TipSide.Below, reading: true);
            else line.pickingMode = PickingMode.Ignore;
            if (live.CanUnpause) Add(pause, Kit.Button(Stage.T("screen.room.pause.off"), KitButtonKind.Normal, () => Unpause()), "room-pause-off", "sn-room-strip-btn");
            if (mineReserved) Add(pause, Kit.Button(Stage.T("screen.room.pause.cancel"), KitButtonKind.Normal, () => Unpause()), "room-pause-cancel", "sn-room-strip-btn");
            if (live.CanClearAll) Add(pause, Kit.Button(Stage.T("screen.room.pause.all"), KitButtonKind.Normal, () => ClearPauses()), "room-pause-all", "sn-room-strip-btn");
        }

        /// <summary>[일시정지 풀기] · [예약 취소](내가 건 정지 · 예약 · 요청 — 방장은 시스템 정지도). 보냈으면 true.</summary>
        public bool Unpause()
        {
            var live = flow.Room.Live;
            if (live == null || !(live.CanUnpause || live.PauseMine)) return false;
            live.Send(new SetPaused { On = false });
            return true;
        }

        /// <summary>방장의 [모두 해제](남이 건 정지 · 예약까지). 보냈으면 true.</summary>
        public bool ClearPauses()
        {
            var live = flow.Room.Live;
            if (live == null || !live.CanClearAll) return false;
            live.Send(new SetPaused { All = true });
            return true;
        }

        // ── 남의 야생 배틀(결정 114) ──

        /// <summary>「○○ 야생 배틀 중」 줄의 지금 글(없으면 빈 글 — 시험 · 확인).</summary>
        public string WildText { get; private set; } = "";

        // 남의 야생 배틀을 뒤에서 그리는 동안(GameFlow.WildAside): 「○○ 야생 배틀 중」 + [보기](내 창이 떠 있으면 닫고 가운데 필드로 연다 — 결정 120) · 보는 중이면 「○○ 야생 배틀 관전 중」 + [보드로](= Esc, 접기)
        void Wild(RoomLive live)
        {
            bool aside = flow.WildAside;
            bool watching = flow.WatchingWild;
            bool free = !flow.MenuUp;
            int seat = aside ? flow.WildSeat : -1;
            string text = aside ? Stage.T(watching ? "screen.room.wild.watching" : "screen.room.wild.on").Replace("{name}", seat >= 0 ? Name(live, seat) : "") : null;
            bool low = !watching && flow.OwnWindowUp;   // 내 창이 떠 있으면 아래 판 바로 위로(창 제목 줄을 가리지 않게 — 영어 띠가 정비 창 제목 줄에 걸쳤다)
            string key = (text ?? "") + "|" + watching + "|" + free + "|" + low;
            WildText = text ?? "";
            if (key == wildKey) return;
            wildKey = key;
            wild.Clear();
            wild.EnableInClassList("sn-room-strip-low", low);
            wild.style.display = text != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (text == null) return;
            var line = Add(wild, Kit.Text(text, Kit.F2), "room-wild-text", "sn-room-strip-text");
            Stage.Tips.Hook(line, null, Stage.T("screen.room.wild.tip"), TipSide.Below, reading: true);
            if (watching) Add(wild, Kit.Button(Stage.T("screen.room.wild.back"), KitButtonKind.Normal, () => flow.CloseWildWatch()), "room-wild-back", "sn-room-strip-btn");
            else
            {
                var open = Add(wild, Kit.Button(Stage.T("screen.room.wild.open"), KitButtonKind.Normal, () => flow.OpenWildWatch()), "room-wild-open", "sn-room-strip-btn");
                open.SetEnabled(free);
            }
        }

        // ── ③ 투표(S22) ──

        /// <summary>열린 투표(없으면 null).</summary>
        public RoomVote OpenVote => flow.Room.Live?.Room?.Vote;

        /// <summary>내가 그 투표에 찬반을 낼 수 있나(투표할 좌석 — 대상이 아니고 아직 내지 않았다).</summary>
        public bool CanVote => OpenVote != null && OpenVote.Waiting.Contains(flow.Room.Live.Seat);

        void Vote(RoomLive live)
        {
            var v = live.Room?.Vote;
            long left = v != null ? Math.Max(0, v.LeftMs - (live.NowMs - live.RoomAtMs)) : 0;
            string key = v == null ? "" : v.Id + "|" + v.Yes.Count + "|" + v.Waiting.Count + "|" + ClockView.Text(left) + "|" + CanVote;
            if (key == voteKey) return;
            voteKey = key;
            vote.Clear();
            vote.style.display = v != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (v == null) return;
            string name = Name(live, v.Target);
            string kind = v.Kind == VoteKind.Kick ? "kick" : v.Kind == VoteKind.Pause ? "pause" : "away";   // 일시정지 투표(결정 112 Q6) — 대상 = 요청한 사람
            var title = Add(vote, Kit.Text(Stage.T("screen.room.vote." + kind).Replace("{name}", name), Kit.F2), "room-vote-title", "kit-title");
            Stage.Tips.Hook(title, null, Stage.T("screen.room.vote." + kind + ".tip"), TipSide.Below, reading: true);
            int voters = v.Yes.Count + v.Waiting.Count;
            Add(vote, Kit.Text(Stage.T("screen.room.vote.count").Replace("{y}", v.Yes.Count.ToString(CultureInfo.InvariantCulture)).Replace("{n}", voters.ToString(CultureInfo.InvariantCulture)).Replace("{t}", ClockView.Text(left)), Kit.F2), "room-vote-count", "kit-muted");
            if (CanVote)
            {
                var row = Add(vote, new VisualElement(), "room-vote-buttons", "sn-foot-row");
                Add(row, Kit.Button(Stage.T("screen.room.vote.yes"), KitButtonKind.Main, () => Cast(true)), "room-vote-yes", null);
                Add(row, Kit.Button(Stage.T("screen.room.vote.no"), KitButtonKind.Normal, () => Cast(false)), "room-vote-no", null);
            }
            else Add(vote, Kit.Wrapped(Stage.T(v.Target != live.Seat ? "screen.room.vote.done" : v.Kind == VoteKind.Pause ? "screen.room.vote.pause_mine" : "screen.room.vote.target"), Kit.F2), "room-vote-state", null);
        }

        /// <summary>투표 판의 [찬성]·[반대]. 보냈으면 true.</summary>
        public bool Cast(bool yes)
        {
            var v = OpenVote;
            if (v == null || !CanVote) return false;
            flow.Room.Live.Send(new CastVote { VoteId = v.Id, Yes = yes });
            return true;
        }

        // ── ④ 알림 ──

        void Notes(RoomLive live)
        {
            long now = live.NowMs;
            var lines = new List<string>();
            foreach (var n in live.Notices)
            {
                if (now - n.AtMs >= RoomLive.NoticeMs) continue;
                string text = NoticeText(live, n);
                if (text != null) lines.Add(text);
            }
            // 방장: 저장 쓰기 실패(02 §7 SaveWriter.Problems — 다시 쓰는 중)
            if (live.IsHost && live.SaveProblems.Count > problemsSeen)
            {
                problemsSeen = live.SaveProblems.Count;
                problemAt = now;
            }
            if (problemAt >= 0 && now - problemAt < RoomLive.NoticeMs) lines.Add(Stage.T("screen.room.notice.save_fail"));
            string key = string.Join("\n", lines);
            if (key == notesKey) return;
            notesKey = key;
            notes.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                var band = Add(notes, new VisualElement { pickingMode = PickingMode.Ignore }, "room-notice-" + i, "sn-room-strip");
                Line(band, "room-notice-" + i + "-text", lines[i], false);
            }
        }

        /// <summary>알림 한 줄(모르는 코드 · 화면에 보일 것이 없는 알림 = null).</summary>
        public string NoticeText(RoomLive live, RoomNotice n)
        {
            string key;
            switch (n.Code)
            {
                case NetCode.PlayerLeft: key = "screen.room.notice.left"; break;
                case NetCode.PlayerBack: key = "screen.room.notice.back"; break;
                case NetCode.AutoResolved: key = "screen.room.notice.auto"; break;
                case NetCode.Away: key = "screen.room.notice.away"; break;
                case NetCode.AwayCleared: key = "screen.room.notice.away_cleared"; break;
                case NetCode.Paused: key = "screen.room.notice.paused"; break;
                case NetCode.Resumed: key = "screen.room.notice.resumed"; break;
                case NetCode.VotePassed: key = n.Vote == VoteKind.Kick ? "screen.room.notice.kick_passed" : n.Vote == VoteKind.Pause ? "screen.room.notice.pause_passed" : "screen.room.notice.away_passed"; break;
                case NetCode.VoteFailed: key = n.Vote == VoteKind.Pause ? "screen.room.notice.pause_failed" : "screen.room.notice.vote_failed"; break;
                case NetCode.VoteDenied: key = n.Vote == VoteKind.Pause ? "screen.room.notice.pause_busy" : "screen.room.notice.vote_denied"; break;
                case NetCode.PauseDenied: key = "screen.room.notice.pause_denied"; break;
                case NetCode.Undone: key = "screen.room.notice.undone"; break;
                case NetCode.Rewound: key = "screen.room.notice.rewound"; break;
                case NetCode.UndoDenied: key = "screen.room.notice.undo_denied"; break;
                default: return null;
            }
            string text = Stage.T(key).Replace("{name}", n.Seat >= 0 ? Name(live, n.Seat) : "");
            if (n.Code == NetCode.Rewound) text = text.Replace("{round}", n.Round.ToString(CultureInfo.InvariantCulture));   // 그 알림이 실어 온 라운드(줄마다 제 것)
            return text;
        }

        /// <summary>판이 되돌려졌다(GameFlow.Rewound): 선택 Id로 기억하던 것(내 시계 남은 10초 소리 · 남의 고르기 줄)을 비우고 도장 칩을 다시 그린다(지운 미래의 도장 — 소리는 내지 않는다) — 같은 선택 Id · 기록 줄 번호가 다시 쓰인다.</summary>
        public void Rewound()
        {
            lowFor = -1;
            peekKey = null;
            marksKey = null;
        }

        // ── S11 재접속 덮개 ──

        bool Cover(RoomLive live)
        {
            bool lost = live.Status == NetStatus.Reconnecting || live.Status == NetStatus.Failed;
            long secs = live.LostAtMs >= 0 ? Math.Max(0, live.NowMs - live.LostAtMs) / 1000 : 0;
            string key = !lost ? "" : live.Status + "|" + live.FailCode + "|" + live.FailDetail + "|" + live.CanRetry + "|" + (live.Status == NetStatus.Reconnecting ? secs.ToString(CultureInfo.InvariantCulture) : "");
            if (key == coverKey) return lost;
            coverKey = key;
            cover.Clear();
            cover.style.display = lost ? DisplayStyle.Flex : DisplayStyle.None;
            if (!lost) return false;
            Add(cover, new VisualElement(), "room-reconnect-scrim", "sn-room-cover-scrim");   // 뒤의 게임 화면을 누르지 못하게 덮는다 — 옅게(뒤의 보드가 보인다, 결정 112 Q6)
            var panel = Add(cover, Kit.Panel(Stage.Skin), "room-reconnect", "sn-menu-panel");
            Add(panel, Kit.Text(Stage.T("screen.room.reconnect.title"), Kit.FTitle), "room-reconnect-title", "kit-title");
            if (live.Status == NetStatus.Reconnecting)
            {
                var status = Add(panel, Kit.Wrapped(Stage.T("screen.room.reconnect.trying").Replace("{s}", secs.ToString(CultureInfo.InvariantCulture)), Kit.F2), "room-reconnect-status", null);
                Stage.Tips.Hook(status, null, Stage.T("screen.room.reconnect.trying.tip"), TipSide.Below, reading: true);
            }
            else RoomUi.Fail(panel, Stage, "room-reconnect-fail", live.FailCode ?? NetFail.Lost, live.FailDetail, 0);
            var row = Add(panel, new VisualElement(), "room-reconnect-foot", "sn-foot-row");
            if (live.CanRetry) Add(row, Kit.Button(Stage.T("screen.room.reconnect.retry"), KitButtonKind.Main, () => Retry()), "room-reconnect-retry", null);
            Add(row, Kit.Button(Stage.T("screen.room.reconnect.leave"), KitButtonKind.Normal, () => Leave()), "room-reconnect-leave", null);
            return true;
        }

        /// <summary>덮개의 [다시 시도]: 같은 토큰으로 다시 붙는다(02 §6-1 — 3분 창을 다시 연다). 쓸 수 없는 실패면 false.</summary>
        public bool Retry()
        {
            var live = flow.Room.Live;
            if (live == null || !live.CanRetry) return false;
            live.Retry();
            coverKey = null;
            return true;
        }

        /// <summary>덮개의 [타이틀로]: 방을 떠나 타이틀로.</summary>
        public void Leave()
        {
            coverKey = null;
            flow.Room.Leave();
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
