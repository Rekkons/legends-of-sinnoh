using System;
using System.Collections.Generic;
using System.IO;
using Sinnoh.Rules.Session;

namespace Sinnoh.Client
{
    // 무르기 · 되돌리기의 화면 쪽(결정 149 · 154 — Docs/Design/undo.md §3): 보드 HUD [무르기](board-hud-undo) · Ctrl+Z · 게임 메뉴 「무르기」 · 「되돌리기」 쪽이 같은 함수를 부른다.
    // 할 수 있나는 손님이 받은 제안(SeatClient.Undo — 세션이 좌석마다 보낸다)을 그대로 읽는다. 화면이 규칙을 세지 않는다.
    public sealed partial class GameFlow
    {
        UndoOffer fakeUndo;
        List<UndoPoint> fakePoints;
        ulong diceSeedBase;

        /// <summary>지금 무르기 제안(마지막 갱신의 <see cref="SeatClient.Undo"/>). 무르기를 끈 판 · 튜토리얼 · 게임 밖이면 null.</summary>
        public UndoOffer Undo => fakeUndo ?? Client?.Undo;

        /// <summary>무르기 단추 · 메뉴 줄을 세우나(무르기를 켠 판 — 끈 판 · 튜토리얼에는 아예 없다).</summary>
        public bool UndoShown => OnBoard && Undo != null && Undo.Mode != UndoMode.Off;

        /// <summary>한 수 무를 수 있나(제안의 목표가 있다 — 게임 메뉴 「무르기」 줄. 배틀 중에도). 방이 일시정지 중이면 못 한다(제안은 다음 갱신에야 Paused로 온다 — 받은 시계를 함께 본다).</summary>
        public bool UndoReady => UndoShown && Undo.StepTo >= 0 && !RoomPaused;

        /// <summary>보드 HUD [무르기] · Ctrl+Z가 켜지나: 무를 수 있고 보드 입력이 열렸다(연출이 끝났고 창 · 메뉴가 없다 — HUD의 다른 단추와 같다). 방에서 보낸 무르기의 답을 기다리는 동안은 꺼진다(<see cref="UndoWaiting"/>).</summary>
        public bool UndoOpen => UndoReady && InputOpen && !UndoWaiting;

        /// <summary>방 판에서 보낸 무르기를 기다리는 최대 시간(ms) — 그 안에 되돌림 · 거절이 오지 않으면 다시 누를 수 있다.</summary>
        public const long UndoWaitMs = 5000;

        int undoWaitEpoch = -1;
        long undoWaitAt, undoWaitLive;

        /// <summary>
        /// 방 판: 보낸 무르기의 답을 기다리는 중인가 — 그 Epoch의 되돌림 갱신(Epoch가 바뀐다) · UNDO_DENIED 알림 · <see cref="UndoWaitMs"/>가 지날 때까지 다시 보내지 않는다
        /// (두 번 누르면 방장이 둘째를 낡은 To로 거절해 「지금은 무를 수 없습니다」가 함께 뜬다). 혼자 하기는 바로 되돌리므로 늘 false.
        /// </summary>
        public bool UndoWaiting
        {
            get
            {
                if (undoWaitEpoch < 0 || !Room.InGame || Undo == null || Undo.Epoch != undoWaitEpoch || NowMs - undoWaitAt >= UndoWaitMs) return false;
                foreach (var n in Room.Live.Notices)
                    if (n.Code == NetCode.UndoDenied && n.AtMs >= undoWaitLive) return false;
                return true;
            }
        }

        /// <summary>못 무르는 까닭의 툴팁 글(무를 수 있으면 null).</summary>
        public string UndoBlockText =>
            Undo == null || UndoReady ? null
            : Stage.T(UndoRules.BlockKey(RoomPaused ? UndoBlock.Paused : Undo.Block == UndoBlock.None ? UndoBlock.Off : Undo.Block));

        // 방 판(가짜 방 포함)이 일시정지 중인가(받은 시계의 PausedBy — 혼자 하기는 늘 아니다)
        bool RoomPaused => Room.InGame && Room.Live.Paused;

        /// <summary>「되돌리기」 목록을 열 수 있나(자유 + 솔로 플레이어 · 방장 — <see cref="UndoOffer.HostList"/>).</summary>
        public bool RewindShown => UndoShown && Undo.Mode == UndoMode.Free && Undo.HostList;

        /// <summary>
        /// 확인용(캡처 · 시험): 받은 제안 대신 이 제안 · 목록을 쓴다(null = 받은 것 그대로). 판이 바뀌면 풀린다. 누르기는 세션이 받지 않는 목표면 아무 일도 없다.
        /// </summary>
        public void FakeUndo(UndoOffer offer, List<UndoPoint> points = null)
        {
            fakeUndo = offer;
            fakePoints = points;
            Hud?.Refresh();
        }

        /// <summary>
        /// 한 수 무르기(보드 HUD [무르기] · Ctrl+Z · 게임 메뉴 「무르기」): 솔로 = 세션에 바로, 방 = 방장에게 <see cref="UndoStep"/>. 확인 물음은 없다(한 수씩 — 결정 154 [가정]).
        /// 보냈으면(솔로는 되돌렸으면) true. 되돌린 화면은 되돌림 표지가 실린 갱신이 다시 세운다(<see cref="Rewound"/>).
        /// </summary>
        public bool PressUndo()
        {
            if (!UndoReady || UndoWaiting) return false;
            int to = Undo.StepTo;
            if (Session != null) return GuardUndo(() => Session.Undo(Client.Seat, to, NowMs));
            if (!Room.InGame) return false;
            Room.Live.Send(new UndoStep { To = to });
            undoWaitEpoch = Undo.Epoch;
            undoWaitAt = NowMs;
            undoWaitLive = Room.Live.NowMs;
            Hud?.Refresh();
            return true;
        }

        /// <summary>Ctrl+Z(<see cref="ScreenBoot"/>): 보드 HUD [무르기]가 켜져 있고 창이 없을 때만.</summary>
        public bool UndoKey() => UndoOpen && PressUndo();

        /// <summary>
        /// 「되돌리기」 목록(새것부터): 솔로 = 세션에서 바로, 방 = 방장이 보낸 목록(받기 전 · 방 길이 아직 없으면 null — 화면은 「불러오는 중」).
        /// </summary>
        public List<UndoPoint> UndoPoints()
        {
            if (fakePoints != null) return fakePoints;
            if (!RewindShown) return new List<UndoPoint>();
            return Room.InGame ? Room.Live.UndoPoints : Session != null ? Session.UndoPoints() : null;
        }

        /// <summary>
        /// 방 판: 되돌리기 목록을 방장 세션에 달라고 한다(게임 메뉴의 「되돌리기」 쪽을 열 때 — 받으면 <see cref="UndoPoints"/>). 솔로 · 가짜 목록이면 아무 일도 없다.
        /// keep = 받아 둔 목록을 새 목록이 올 때까지 그대로 보인다(쪽이 열린 동안 판이 흘러 다시 받을 때 — 「불러오는 중」으로 깜빡이지 않게).
        /// </summary>
        public void RequestUndoPoints(bool keep = false)
        {
            if (fakePoints != null || !RewindShown || !Room.InGame) return;
            Room.Live.RequestUndoPoints(keep);
        }

        /// <summary>방장(솔로 플레이어)의 되돌리기: 그 턴 시작으로(솔로 = 세션 · 방 = <see cref="Sinnoh.Rules.Session.RewindTo"/> 메시지). 보냈으면 true.</summary>
        public bool RewindTo(int to)
        {
            if (!RewindShown) return false;
            if (Session != null) return GuardUndo(() => Session.Rewind(to, Client.Seat, NowMs));
            if (!Room.InGame) return false;
            Room.Live.Send(new Sinnoh.Rules.Session.RewindTo { To = to });
            return true;
        }

        // 되돌림은 저장을 거꾸로 고친다(스냅샷 → 로그 자르기 — undo.md §1-5 ③): 쓰지 못하면 세션은 그대로이고 화면은 「저장 실패」로 받는다(Guard와 같은 꼴).
        // 되돌렸으면 스냅샷 · 로그가 되돌린 판과 맞게 쓰였다 — 앞 명령이 남긴 「저장 실패」를 지운다(Guard가 받아들인 명령에서 지우는 것과 같다)
        bool GuardUndo(Func<bool> act)
        {
            try
            {
                bool done = act();
                if (done && Session != null && Session.Saving) SaveError = null;
                return done;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                SaveError = e.Message;
                return false;
            }
        }

        /// <summary>
        /// 되돌림이 남긴 창 가운데 판 상태와 무관한 보기 창인가(게임 메뉴와 그 쪽 — 버그 신고 · 방 설정 · 투표 · 메뉴에서 연 설정 · 룰 설명서 · 도감 · 상성표 · 기록 창은 갱신마다 다시 그린다):
        /// 이것들은 되돌림(남의 무르기 · 방장 되돌리기)에 걷지 않는다 — 쓰던 버그 신고 글도 그대로다. 답을 내는 창 · 정비 · 배틀 화면 · 그 좌석 판의 창은 되돌린 판과 어긋나니 걷는다.
        /// 내가 메뉴에서 한 무르기 · 되돌리기는 메뉴를 먼저 닫고 보낸다(<see cref="BoardMenuScreen.PressUndo"/> · <see cref="BoardMenuScreen.ConfirmRewind"/>).
        /// </summary>
        public static bool KeptOnRewind(IUiScreen screen) =>
            screen is BoardMenuScreen || screen is SettingsScreen || screen is RulebookScreen || screen is DexScreen || screen is TypeChartScreen || screen is DiscardScreen || screen is LogScreen;

        /// <summary>
        /// 되돌림 표지가 실린 갱신(undo.md §3-5): 연출 대기열 · 창 · 배틀 화면을 걷고 보기에서 다시 세운다(이어하기와 같은 <see cref="Rebuild"/>). 판과 무관한 보기 창은 남긴다(<see cref="KeptOnRewind"/>).
        /// 기록 창은 지운 미래의 줄을 빼고 라운드 머리 + 무르기 줄을 더한다(<see cref="Sinnoh.Fx.BoardLog.Rewound"/>).
        /// 끊긴 동안 일어난 되돌림(표지 없는 재접속 스냅샷의 Epoch가 앞섰다 — <see cref="SeatClient.MissedRewind"/>)도 같은 정리를 한다: 누가 했는지 모르니 기록의 무르기 줄만 없다.
        /// </summary>
        void Rewound(GameUpdate update)
        {
            var view0 = Client.View;
            var note = update.Rewind ?? new RewindNote { Kind = RewindKind.Step, By = -1, To = -1, Round = view0 != null ? view0.Round : 0, NextSeq = view0 != null ? view0.NextSeq : 0, Epoch = Client.Epoch };
            // 걷기(Leave의 화면 몫 — 손님 · 링크 · 세션은 그대로)
            if (Fx != null) Fx.Sequencer.Clear();
            Battle = null;
            WatchingWild = false;
            battleEnd = null;
            battleHeld = false;
            openWaiting = false;
            pulsing = false;
            wipeBackAt = long.MinValue;
            if (wipe != null) wipe.Set(0f);
            camStep = null;
            heldStep = null;
            heldEntry = null;
            drag.Kill();
            if (Stage.Info != null && !KeptOnRewind(Stage.Info)) Stage.ShutInfo();
            if (Stage.Current != null && !KeptOnRewind(Stage.Current)) Stage.Shut();
            ShutWatch();       // 남의 행동 보기 창: 사용자 접기([닫기])가 아니다 — 되돌린 같은 턴에 같은 갈래가 다시 열리게 접은 표시도 비운다
            watchShut = null;
            Deals?.Show(null);
            Cards?.Show(null);
            if (Sheets != null)
            {
                Sheets.Begin();
                Sheets.End();
            }
            if (board != null && board.Tokens != null) board.Tokens.Raise(-1);
            if (board != null && board.Marks != null) board.Marks.Hide();
            Stage.ShutHud();   // HUD의 걷기 고르기 · 능력 고르기 · 순간이동 · 프롬프트도 함께 — Sync가 새로 연다
            promptSeen.Clear();
            TimedOutAtMs = long.MinValue;
            fateAuto = false;
            warpHidden = -1;
            uploadOffered = false;   // 끝난 판을 되돌려 다시 끝내면 다시 올린다
            // 세우기 — 굴림판 녹화본 고르기(겉모양)는 되돌린 자리마다 다르게 섞는다(이어하기와 같은 꼴)
            int seats = Session != null ? Session.SeatCount : roomSeats;
            Rebuild(Client.Seat, seats, diceSeedBase ^ ((ulong)Math.Max(0, note.To) * 0x9E3779B97F4A7C15UL));
            if (MenuUp && !HostPaced) Fx.SetPaused(true);   // 남긴 게임 메뉴 뒤에서 새 대기열이 흐르지 않게(혼자 하는 판 — OpenMenu와 같다)
            var view = Client.View;
            Log?.Rewound(note.NextSeq, view != null ? view.Round : 0, actsRevealed, note.By, note.Kind == RewindKind.Host);
            // 방: 선택 Id · 기록 줄 번호로 기억하던 방 몫(남의 고르기 보기 · 도장 · 받은 목록 · 시계 소리)도 비운다 — 되돌리면 같은 번호가 다시 쓰인다
            if (Room.InGame) Room.Live.Rewound(note.NextSeq);
            roomLayer?.Rewound();
            Sync();
        }
    }
}
