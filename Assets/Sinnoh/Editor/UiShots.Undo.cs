using System;
using System.Collections.Generic;
using System.Linq;
using Sinnoh.Client;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 무르기 · 되돌리기 화면(결정 149 · 154 — Docs/Design/undo.md §3 · §5): 보드 HUD [무르기](켜짐 · 못 무르는 까닭 툴팁 · 걷기 전 확인 중 · 카드 칩이 선 머리 줄) · 게임 메뉴 「무르기」 · 「되돌리기」 줄(혼자 · 방 판) ·
    /// 지난 턴 목록 쪽 · 확인 물음 · 혼자 하기 설정의 「무르기」 줄 · 실제로 무른 뒤의 보드(<c>undo-resumed</c> — <c>board-resumed</c>와 같은 판 · 같은 자리) · 기록 창.
    /// 혼자 판 장면은 무르기를 켠 실제 판에서 규칙 핵이 보낸 제안을 읽는다. 방 판 메뉴(조각 D2 전) · 13줄 목록 쪽(겉모양 — 줄 수 · 쪽 넘김 · 한 일 넘침)은 가짜 제안 · 목록(<see cref="GameFlow.FakeUndo"/>).
    /// 장면마다 요소가 있는지 · 켜짐/흐림 · 툴팁 글 · 판이 화면 안 · 글 넘침을 단정한다. 모두 보드 위 화면이다. 그림 <c>Logs/Review/m10/undo/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] UndoScreens =
        {
            Screen("undo-hud", s => UndoHud(s, false, false)),          // 한 칸 걸은 뒤 [무르기] 켜짐 — [스탬프] 바로 왼쪽
            Screen("undo-hud-off-tip", s => UndoHud(s, true, false)),   // 숨겨진 장소를 공개한 뒤(운이 드러남): 흐린 단추 + 까닭 툴팁
            Screen("undo-hud-walk", s => UndoHud(s, false, true)),      // 걷기 전 확인 중(오른쪽 클릭 안내) — [무르기]는 남는다
            Screen("undo-hud-chips", UndoHudChips),                     // 앰프 칩(guitarist)이 선 머리 줄 + 걷기 전 확인 — 안내는 빠지고 [무르기]가 줄 끝, 1080에서도 행동 판 안
            Screen("undo-menu", s => UndoMenu(s, false)),               // 게임 메뉴: 「무르기」 · 「되돌리기」(자유)
            Screen("undo-menu-tip", s => UndoMenu(s, true)),            // 못 무르는 「무르기」 줄(이번 턴의 처음) + 까닭 툴팁
            Screen("undo-menu-room", UndoMenuRoom),                     // 방장 게임 메뉴(단추가 가장 많다)에도 두 줄 — 판이 화면 안
            Screen("undo-rewind-page", s => UndoRewind(s, 0, -1)),      // 지난 턴 목록 첫 쪽(10줄 · 지금 턴 꼬리표 · 한 일 셋 + 외 n)
            Screen("undo-rewind-page2", s => UndoRewind(s, 1, -1)),     // 둘째 쪽
            Screen("undo-rewind-tip", s => UndoRewind(s, 0, 1)),        // 줄 마우스 오버 = 그 턴에 한 일 모두
            Screen("undo-rewind-ask", s => UndoRewind(s, 0, -2)),       // [되돌리기] → 확인 물음
            Screen("undo-resumed", UndoResumed),                         // 두 걸음 → 한 수 더 → [무르기]: 보기에서 다시 선 보드(board-resumed와 같은 판 · 같은 자리)
            Screen("undo-log", Fit(UndoLog)),                            // 그 판의 기록 창: 지운 수의 줄은 없고 「○○은(는) 무르기를 했다」
            Screen("settings-solo-undo", UndoSolo),                      // 혼자 하기 설정 「무르기」 줄(새 판 기본 = 내 턴만) + 「자유」 툴팁
            // 방(조각 D2 — 가짜 방 RoomLive)
            Screen("room-settings-undo", UndoRoomSettings),              // 로비 방 설정 「무르기」 줄(방장이 「자유」로 — 로비에서만) + 방 몫 「자유」 툴팁
            Screen("undo-room-notice", UndoRoomNotice),                  // 방 알림 띠: 「○○님이 무르기를 했습니다.」 · 「○○님이 n라운드로 되돌렸습니다.」 · 「지금은 무를 수 없습니다.」
            Screen("undo-room-paused", UndoRoomPaused),                  // 일시정지 중: 받은 제안이 무를 수 있어도 [무르기] 흐림 + 「일시정지 중에는 무를 수 없습니다.」
            Screen("undo-room-rewind-loading", s => UndoRoomRewind(s, false)),   // 방장 메뉴 「되돌리기」: 목록을 달라고 보낸 뒤 「불러오는 중…」
            Screen("undo-room-rewind", s => UndoRoomRewind(s, true)),            // 방장 세션이 보낸 목록이 오면 그 쪽을 다시 그린다
        };

        // 가짜 로비(방장 · 4자리 — 둘 참가): 방 설정 「무르기」 = 자유
        static void UndoRoomSettings(UiShot s)
        {
            var flow = s.Flow();
            flow.OpenTitle();
            var live = new RoomLive(() => RoomNow)
            {
                IsHost = true, Seat = 0, JoinCode = "Q7K2MZ", Listed = true, RoomAtMs = RoomNow,
                Room = new RoomState
                {
                    Phase = RoomPhase.Lobby, RoomId = "room-shot", Settings = new RoomSettings { Seats = 4, Undo = UndoMode.Free },
                    Seats = new List<RoomSeat>
                    {
                        new RoomSeat { Nickname = "방장트레이너", Host = true, Connected = true, Protagonist = "pt_player_male" },
                        new RoomSeat { Nickname = "빛나", Ready = true, Connected = true, Protagonist = "pt_player_female" },
                        null, null, null, null,
                    },
                },
            };
            flow.Room.ShowLobby(live);
            var lobby = flow.Stage.Current as LobbyScreen ?? throw new InvalidOperationException("로비가 열리지 않았다");
            lobby.Sync();
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            if (root.Q("room-set-row-" + RoomSettingsPanel.Undo) == null) throw new InvalidOperationException("방 설정에 「무르기」 줄이 없다");
            foreach (var v in new[] { "off", "turn", "free" })
            {
                var o = root.Q("room-set-opt-" + RoomSettingsPanel.Undo + "-" + v) ?? throw new InvalidOperationException("「무르기」 값 " + v + "이 없다");
                if (o.ClassListContains("sn-opt-picked") != (v == "free")) throw new InvalidOperationException("「무르기」 골라진 값이 「자유」가 아니다(" + v + ")");
            }
            RoomFit(s, flow, "lobby-col");
            RoomFit(s, flow, "lobby-set");
            TipIs(flow, root.Q("room-set-opt-" + RoomSettingsPanel.Undo + "-free"), flow.Stage.T("screen.room.set.undo.free.tip"));
        }

        static void UndoRoomNotice(UiShot s)
        {
            var flow = RoomGame(s, l =>
            {
                l.Note(new RoomNotice { Code = NetCode.Undone, Seat = 1, AtMs = RoomNow - 1500 });
                l.Note(new RoomNotice { Code = NetCode.Rewound, Seat = 0, Round = 3, AtMs = RoomNow - 800 });
                l.Note(new RoomNotice { Code = NetCode.UndoDenied, Seat = 0, AtMs = RoomNow - 200 });
            });
            var t = flow.Stage;
            string round = "3";   // 알림이 실어 온 되돌린 라운드
            var want = new[]
            {
                t.T("screen.room.notice.undone").Replace("{name}", "빛나"),
                t.T("screen.room.notice.rewound").Replace("{name}", "방장트레이너").Replace("{round}", round),
                t.T("screen.room.notice.undo_denied"),
            };
            var shown = flow.Stage.Root.Query<Label>(className: "sn-room-strip-text").ToList().Where(l => l.name != null && l.name.StartsWith("room-notice-", StringComparison.Ordinal)).Select(l => l.text.Replace("⁠", "")).ToList();
            foreach (var w in want)
                if (!shown.Contains(w.Replace("⁠", ""))) throw new InvalidOperationException("알림 띠에 「" + w + "」이 없다: " + string.Join(" / ", shown));
            var bad = WorkUi.Overflows(flow.Stage.Root.Q("room-notices"));
            if (bad.Count > 0) throw new InvalidOperationException("알림 띠 글 넘침: " + string.Join(" / ", bad));
        }

        static void UndoRoomPaused(UiShot s)
        {
            var flow = RoomGame(s, l => { RunClock(l, 0, ClockKind.MapTurn, 83000, 120000, 0); l.Clock.PausedBy.Add(0); });
            flow.FakeUndo(UndoOn(UndoMode.Turn));
            Pass(flow, 0);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (flow.UndoReady || flow.UndoOpen) throw new InvalidOperationException("일시정지 중인데 무를 수 있다");
            var undo = flow.Stage.Root.Q<Button>(BoardHud.UndoButton) ?? throw new InvalidOperationException("[무르기]가 없다");
            if (!undo.ClassListContains(UiStage.OffClass)) throw new InvalidOperationException("일시정지 중 [무르기]가 흐리지 않다");
            if (flow.PressUndo()) throw new InvalidOperationException("일시정지 중 [무르기]가 들었다");
            TipIs(flow, undo, flow.Stage.T(UndoRules.BlockKey(UndoBlock.Paused)));
        }

        // 방장 메뉴 「되돌리기」 쪽: 열면 방장 세션에 목록을 달라고 보낸다(가짜 방 — 보낸 것은 Sent) → arrived면 받은 목록으로 다시 그린 쪽
        static void UndoRoomRewind(UiShot s, bool arrived)
        {
            var flow = RoomGame(s, l => RunClock(l, 0, ClockKind.MapTurn, 83000, 120000, 0));
            var live = flow.Room.Live;
            flow.FakeUndo(UndoOn(UndoMode.Free));
            flow.OpenMenu();
            var menu = flow.Stage.Current as BoardMenuScreen ?? throw new InvalidOperationException("게임 메뉴가 열리지 않았다");
            if (!menu.OpenRewind()) throw new InvalidOperationException("되돌리기 쪽이 열리지 않았다");
            if (!live.Sent.Any(m => m is RequestUndoPoints)) throw new InvalidOperationException("쪽을 열 때 목록을 달라고 보내지 않았다");
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            if (!arrived)
            {
                if (menu.RewindPoints != null || root.Q("board-menu-rewind-loading") == null) throw new InvalidOperationException("받기 전인데 「불러오는 중」이 아니다");
                RoomFit(s, flow, "board-menu-panel");
                return;
            }
            live.UndoPoints = UndoFakePoints(flow);
            Pass(flow, 0);   // 방 판 덮개 층(매 프레임)이 받은 목록을 보고 쪽을 다시 그린다(BoardMenuScreen.SyncRoom)
            s.Panel.Tick(3);
            if (!ReferenceEquals(menu.RewindPoints, live.UndoPoints) || root.Q("board-menu-rewind-loading") != null) throw new InvalidOperationException("받은 목록으로 다시 그리지 않았다");
            if (root.Query(className: "sn-menu-rewind-row").ToList().Count != BoardMenuScreen.RewindRows) throw new InvalidOperationException("목록 줄 수");
            if (root.Q("board-menu-rewind-row-0-now") == null) throw new InvalidOperationException("「지금」 꼬리표");
            RoomFit(s, flow, "board-menu-panel");
        }

        const int UndoPointCount = 13;

        static SoloSettings UndoSettings(UndoMode mode) => new SoloSettings { Undo = mode };

        // 가짜 지난 턴 목록(새것부터 — 지금 턴이 맨 위): 라운드마다 한 줄, 한 일은 줄마다 0~5개
        static List<UndoPoint> UndoFakePoints(GameFlow flow)
        {
            var kinds = (UndoDeedKind[])Enum.GetValues(typeof(UndoDeedKind));
            int mon = 0;
            var mons = flow.Ctx.Cards.Pokemon;
            for (int i = 0; i < mons.Count; i++) if (mons[i].Key == "starly") { mon = i; break; }
            var list = new List<UndoPoint>();
            for (int i = 0; i < UndoPointCount; i++)
            {
                var p = new UndoPoint { To = 100 - i * 7, Round = UndoPointCount - i, TurnId = UndoPointCount - i, Seat = 0, Current = i == 0 };
                int n = i == 0 ? 1 : i % 6;
                for (int k = 0; k < n; k++) p.Deeds.Add(new UndoDeed { Kind = kinds[(i + k) % kinds.Length], Def = mon });
                list.Add(p);
            }
            return list;
        }

        static UndoOffer UndoOn(UndoMode mode) => new UndoOffer { Mode = mode, StepTo = 3, Block = UndoBlock.None, HostList = mode == UndoMode.Free };

        // 걸어도 운이 드러나지 않는 칸(길 가운데 · 도착 칸에 뒷면 숨겨진 장소가 없다)
        static int UndoCell(GameFlow flow, int not = -1)
        {
            var me = flow.Client.View.Seats[flow.Client.Seat];
            var host = flow.Session.HostCopy();
            var view = flow.Client.View;
            var steps = MoveRules.Reachable(flow.Ctx.Board, me.Cell, me.MovesLeft, view.Options.ElevationMode);
            foreach (int c in flow.Hud.Reach.OrderBy(c => steps[c]).ThenBy(c => c))   // 가까운 칸부터(이동력을 남겨 한 번 더 걷는다)
            {
                if (c == me.Cell || c == not || flow.Hud.StopsBefore(c).Count > 0) continue;
                if (host.Tokens.Any(t => t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell == c)) continue;
                return c;
            }
            throw new InvalidOperationException("운이 드러나지 않는 걸을 칸이 없다(갈 수 있는 칸 " + flow.Hud.Reach.Count + " · 입력 " + flow.InputOpen + " · 선택 " + (flow.Client.FirstChoice() != null ? flow.Client.FirstChoice().Kind.ToString() : "없음") + " · 화면 " + (flow.Stage.Current != null ? flow.Stage.Current.Name : "보드") + " · 연출 " + flow.Fx.Sequencer.Queued.Count() + " · 이동력 " + me.MovesLeft + " · 라운드 " + flow.Client.View.Round + ")");
        }

        static void UndoWalk(GameFlow flow, int cell)
        {
            if (!flow.Hud.Walk(cell)) throw new InvalidOperationException("칸 " + cell + "으로 걷기가 거절됐다");
            flow.Stage.Tweens.Advance(BoardReadyMs);
            if (flow.Client.View.Seats[flow.Client.Seat].Cell != cell) throw new InvalidOperationException("칸 " + cell + "에 닿지 않았다");
        }

        // 운이 드러난 판(undo.md §1-2 ③ — 뒷면 숨겨진 장소가 앞면이 된다): 길에 다른 숨겨진 장소가 없는 뒷면 숨겨진 장소로 걸어가 [공개].
        // 고르는 순서는 MouseProbe board-spot-reveal과 같다(규토리 더미 > 유물 > 떨어진 별 > 야영지 — 우두머리는 배틀이 열려 뺀다). 그 라운드에 없으면 턴을 넘긴다(최대 3라운드).
        // 찾아보기(조우 카드) · 야생 배틀은 혼자 판에서 턴을 끝내므로(돌아오면 다음 턴 처음 — 까닭 TurnStart) 운 장면에 쓰지 않는다
        internal static void UndoReveal(GameFlow flow)
        {
            for (int r = 0; r < 3; r++)
            {
                int cell = -1, rank = -1;
                var host = flow.Session.HostCopy();
                foreach (int c in flow.Hud.Reach)
                {
                    if (flow.Hud.StopsBefore(c).Count > 0) continue;
                    foreach (var t in host.Tokens)
                    {
                        if (t.Kind != TokenKind.HiddenSpot || t.FaceUp || t.Destroyed || t.Cell != c) continue;
                        var item = flow.Ctx.Items.Items[t.Def];
                        int k = HiddenSpotRules.Stash(flow.Ctx, t.Def, out _, out _) ? 3 : item.Kind == ItemKind.Artifact ? 2 : item.Key == HiddenSpotRules.FallenStar ? 1 : item.Key == HiddenSpotRules.Campsite ? 0 : -1;
                        if (k > rank) { rank = k; cell = c; }
                    }
                }
                if (cell >= 0)
                {
                    if (!flow.Hud.Walk(cell)) throw new InvalidOperationException("숨겨진 장소 칸 " + cell + "으로 걷기가 거절됐다");
                    Pass(flow, BoardReadyMs);
                    if (flow.Client.Choice(ChoiceKind.HiddenSpot) == null || !flow.Hud.Press(BoardHud.Reveal)) throw new InvalidOperationException("칸 " + cell + "에서 [공개]를 누르지 못했다");
                    for (int i = 0; i < 40 && !(flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null); i++) Pass(flow, 250);
                    if (!(flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null)) throw new InvalidOperationException("공개 뒤 보드 입력이 열리지 않았다");
                    Pass(flow, BoardReadyMs);
                    return;
                }
                int round = flow.Client.View.Round;
                if (!flow.Hud.Press(BoardHud.EndTurn)) throw new InvalidOperationException("턴 종료가 거절됐다");
                for (int i = 0; i < 40 && !(flow.InputOpen && flow.Client.View.Round == round + 1); i++) Pass(flow, 250);
            }
            throw new InvalidOperationException("3라운드 동안 공개할 뒷면 숨겨진 장소가 갈 수 있는 칸에 없다");
        }

        // 받은 제안이 바라는 꼴인가(실제 판 — 규칙 핵이 센 값)
        static void UndoIs(GameFlow flow, bool can, UndoBlock why = UndoBlock.None)
        {
            var o = flow.Undo ?? throw new InvalidOperationException("무르기 제안이 오지 않았다");
            if (can ? o.StepTo < 0 : o.StepTo >= 0 || o.Block != why) throw new InvalidOperationException("제안 = 목표 " + o.StepTo + " · 까닭 " + o.Block + " · 바라는 것 " + (can ? "무를 수 있음" : why.ToString()));
        }

        // 그 툴팁의 본문 글(머리 · 본문 Label 가운데 마지막)
        static string TipBody(GameFlow flow)
        {
            var labels = flow.Stage.Tips.Element.Query<Label>().ToList();
            return labels.Count > 0 ? labels[labels.Count - 1].text.Replace("⁠", "") : null;
        }

        static void TipIs(GameFlow flow, VisualElement target, string want)
        {
            flow.Stage.Tips.Show(target);
            if (flow.Stage.Tips.Target != target) throw new InvalidOperationException(target.name + "에 툴팁이 뜨지 않았다");
            string body = TipBody(flow);
            if (body != want.Replace("⁠", "")) throw new InvalidOperationException(target.name + " 툴팁 = 「" + body + "」 · 바라는 것 「" + want + "」");
        }

        // 혼자 판의 보드 HUD: blocked = 찾아보기 · 야생 배틀 뒤(운) · walking = 한 칸 걸은 뒤 다른 칸을 골라 걷기 전 확인 중
        static void UndoHud(UiShot s, bool blocked, bool walking)
        {
            var flow = ToBoard(s, BoardReadyMs, UndoSettings(UndoMode.Turn));
            if (blocked)
            {
                UndoReveal(flow);
                UndoIs(flow, false, UndoBlock.Luck);
            }
            else
            {
                UndoWalk(flow, UndoCell(flow));
                UndoIs(flow, true);
                if (walking && !flow.Hud.PickCell(UndoCell(flow))) throw new InvalidOperationException("걷기 전 확인이 서지 않았다");
            }
            UndoHead(s, flow, blocked, walking ? BoardHud.MoveHint : BoardHud.StampButton);
            if (blocked) TipIs(flow, flow.Stage.Root.Q(BoardHud.UndoButton), flow.Stage.T(UndoRules.BlockKey(UndoBlock.Luck)));
        }

        // 「행동」 머리 줄: [무르기] → 바로 오른쪽 right([스탬프] · 걷기 전 확인 중에는 오른쪽 클릭 안내 · null = [무르기]가 줄 끝) · 모두 행동 판 안 · 글 넘침 없음
        static void UndoHead(UiShot s, GameFlow flow, bool blocked, string right)
        {
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var undo = root.Q<Button>(BoardHud.UndoButton) ?? throw new InvalidOperationException("[무르기]가 없다");
            if (!undo.enabledSelf) throw new InvalidOperationException("입력이 열렸는데 [무르기]가 꺼져 있다");
            if (undo.ClassListContains(UiStage.OffClass) != blocked) throw new InvalidOperationException("[무르기] 흐림 = " + !blocked + "이어야 한다");
            var next = right != null ? root.Q(right) ?? throw new InvalidOperationException(right + "가 없다") : undo;
            if (right == null && undo.parent.IndexOf(undo) != undo.parent.childCount - 1) throw new InvalidOperationException("[무르기]가 머리 줄 끝이 아니다");
            if (right != null && (next.parent != undo.parent || next.parent.IndexOf(next) != next.parent.IndexOf(undo) + 1)) throw new InvalidOperationException(right + "가 [무르기] 바로 오른쪽이 아니다");
            if (right != null) Apart(root, BoardHud.UndoButton, right);
            var actions = root.Q("board-hud-actions");
            if (undo.worldBound.xMax > actions.worldBound.xMax + 0.5f || next.worldBound.xMax > actions.worldBound.xMax + 0.5f) throw new InvalidOperationException("머리 줄이 행동 판 밖으로 넘친다");
            var bad = WorkUi.Overflows(undo.parent);
            if (bad.Count > 0) throw new InvalidOperationException("행동 머리 줄 글 넘침: " + string.Join(" / ", bad) + " — 판 폭 " + actions.worldBound.width.ToString("0") + " · 줄 " + string.Join(" ", undo.parent.Children().Select(c => (c.name ?? c.GetType().Name) + "=" + c.worldBound.width.ToString("0") + "/" + c.resolvedStyle.minWidth)));
        }

        // 카드 칩이 선 머리 줄(1차 1080 걷기 확인에서 빠듯했다): guitarist 골드 · 앰프 3 → 한 칸 걷고 → 다른 칸을 골라 걷기 전 확인.
        // 칩 + [무르기] 줄에는 오른쪽 클릭 안내가 들지 않아(1080 — 칩 하나만큼 넘쳤다) 빠지고 [무르기]가 줄 끝에 선다(BoardHud — 같은 말은 [취소] 툴팁)
        static void UndoHudChips(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = s.Flow();
            flow.StartSeats(UndoSettings(UndoMode.Turn), 1, Seed, AiSeed, state =>
            {
                var st = state.Seats[0];
                st.TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("guitarist");
                st.TrainerCardTier = TrainerRules.Gold;
                st.Amp = 3;
            });
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            UndoWalk(flow, UndoCell(flow));
            UndoIs(flow, true);
            if (!flow.Hud.PickCell(UndoCell(flow))) throw new InvalidOperationException("걷기 전 확인이 서지 않았다");
            if (flow.Stage.Root.Q(BoardHud.CardState + "-amp") == null) throw new InvalidOperationException("머리 줄에 앰프 칩이 없다");
            if (flow.Stage.Root.Q(BoardHud.MoveHint) != null) throw new InvalidOperationException("칩 + [무르기] 줄에 오른쪽 클릭 안내가 섰다");
            if (flow.Stage.Root.Q(BoardHud.MoveCancel) == null) throw new InvalidOperationException("[취소]가 없다");
            UndoHead(s, flow, false, null);
            Apart(flow.Stage.Root, BoardHud.CardState + "-amp", BoardHud.UndoButton);
        }

        static void UndoMenu(UiShot s, bool blocked)
        {
            var flow = ToBoard(s, BoardReadyMs, UndoSettings(blocked ? UndoMode.Turn : UndoMode.Free));
            if (blocked) UndoIs(flow, false, UndoBlock.TurnStart);   // 턴 처음
            else
            {
                UndoWalk(flow, UndoCell(flow));
                UndoIs(flow, true);
            }
            flow.OpenMenu();
            if (!(flow.Stage.Current is BoardMenuScreen)) throw new InvalidOperationException("게임 메뉴가 열리지 않았다");
            var root = flow.Stage.Root;
            var undo = root.Q<Button>("board-menu-undo") ?? throw new InvalidOperationException("메뉴에 「무르기」가 없다");
            if (undo.ClassListContains(UiStage.OffClass) != blocked) throw new InvalidOperationException("메뉴 「무르기」 흐림이 맞지 않다");
            if ((root.Q("board-menu-rewind") != null) == blocked) throw new InvalidOperationException("「되돌리기」 줄은 자유 + 목록을 열 수 있을 때만");
            RoomFit(s, flow, "board-menu-panel");
            if (blocked) TipIs(flow, undo, flow.Stage.T(UndoRules.BlockKey(UndoBlock.TurnStart)));
        }

        static void UndoMenuRoom(UiShot s)
        {
            var flow = RoomGame(s, l => RunClock(l, 0, ClockKind.MapTurn, 83000, 120000, 0), true);
            flow.FakeUndo(UndoOn(UndoMode.Free), UndoFakePoints(flow));
            flow.OpenMenu();
            Pass(flow, 0);
            if (flow.Stage.Root.Q("board-menu-undo") == null || flow.Stage.Root.Q("board-menu-rewind") == null) throw new InvalidOperationException("방 판 메뉴에 「무르기」 · 「되돌리기」가 없다");
            RoomFit(s, flow, "board-menu-panel");
        }

        // page = 볼 쪽, act = −1 그대로 · −2 둘째 줄의 확인 물음 · n ≥ 0 그 줄 마우스 오버
        static void UndoRewind(UiShot s, int page, int act)
        {
            var flow = ToBoard(s, BoardReadyMs);
            flow.FakeUndo(UndoOn(UndoMode.Free), UndoFakePoints(flow));
            flow.OpenMenu();
            var menu = flow.Stage.Current as BoardMenuScreen ?? throw new InvalidOperationException("게임 메뉴가 열리지 않았다");
            if (!menu.OpenRewind()) throw new InvalidOperationException("되돌리기 쪽이 열리지 않았다");
            if (page > 0 && !menu.TurnRewindPage(page)) throw new InvalidOperationException("쪽을 넘기지 못했다");
            var root = flow.Stage.Root;
            if (act == -2)
            {
                if (!menu.AskRewind(1)) throw new InvalidOperationException("확인 물음이 서지 않았다");
                s.Panel.Tick(3);
                if (root.Q("board-menu-rewind-ask") == null || root.Q("board-menu-rewind-yes") == null || root.Q("board-menu-rewind-no") == null) throw new InvalidOperationException("확인 물음 · 단추가 없다");
                RoomFit(s, flow, "board-menu-panel");
                return;
            }
            s.Panel.Tick(3);
            int from = page * BoardMenuScreen.RewindRows, to = Math.Min(UndoPointCount, from + BoardMenuScreen.RewindRows);
            var rows = root.Query(className: "sn-menu-rewind-row").ToList();
            if (rows.Count != to - from) throw new InvalidOperationException("줄 수 " + rows.Count + " · 바라는 것 " + (to - from));
            var panel = root.Q("board-menu-panel");
            for (int i = from; i < to; i++)
            {
                string name = "board-menu-rewind-row-" + i;
                var row = root.Q(name) ?? throw new InvalidOperationException(name + "가 없다");
                var face = root.Q(name + "-face");
                if (FaceBox.Shown(face) != flow.Protagonist(0)) throw new InvalidOperationException(name + " 얼굴 = " + FaceBox.Shown(face) + " · 주인공 " + flow.Protagonist(0));
                if (root.Q(name + "-go") == null) throw new InvalidOperationException(name + "에 [되돌리기]가 없다");
                if ((root.Q(name + "-now") != null) != (i == 0)) throw new InvalidOperationException("「지금」 꼬리표는 지금 턴 줄에만");
                if (row.worldBound.xMax > panel.worldBound.xMax + 0.5f || row.worldBound.yMax > panel.worldBound.yMax + 0.5f) throw new InvalidOperationException(name + "이(가) 판 밖으로 나간다");
            }
            if (root.Q("board-menu-rewind-pager") == null) throw new InvalidOperationException("13줄인데 쪽 넘김이 없다");
            RoomFit(s, flow, "board-menu-panel");
            if (act >= 0)
            {
                var p = menu.RewindPoints[from + act];
                var text = root.Q("board-menu-rewind-row-" + (from + act) + "-head").parent;
                TipIs(flow, text, menu.RewindDeeds(p, -1));
            }
        }

        // board-resumed와 같은 판 · 같은 걸음(두 걸음)에 한 수를 더 두고 [무르기] — 보기에서 다시 선 보드가 두 걸음 자리(같은 쉬는 때)에 선다
        static GameFlow UndoBack(UiShot s)
        {
            var flow = ToBoard(s, BoardReadyMs, UndoSettings(UndoMode.Turn));
            int two = TwoSteps(flow);
            if (!flow.Hud.Walk(two)) throw new InvalidOperationException("이동이 거절됐다");
            flow.Stage.Tweens.Advance(BoardReadyMs);
            int kept = flow.Session.CommandCount;
            ulong keptHash = flow.Session.Hash;
            // 이 판의 두 걸음 칸은 뒷면 숨겨진 장소다(그 물음도 쉬는 때 — board-resumed도 이 물음 앞으로 이어 받는다): [지나가기](운이 드러나지 않는다) → 무르면 물음 앞으로
            if (flow.Client.Choice(ChoiceKind.HiddenSpot) != null)
            {
                if (!flow.Hud.Press(BoardHud.Pass)) throw new InvalidOperationException("[지나가기]가 거절됐다");
                flow.Stage.Tweens.Advance(BoardReadyMs);
                if (flow.Session.CommandCount != kept + 1) throw new InvalidOperationException("[지나가기] 답이 나가지 않았다");
            }
            else UndoWalk(flow, UndoCell(flow));
            var hud = flow.Hud;
            if (!flow.UndoOpen || !flow.PressUndo()) throw new InvalidOperationException("[무르기]가 듣지 않았다(목표 " + flow.Undo?.StepTo + " · 까닭 " + flow.Undo?.Block + ")");
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            if (flow.Session.CommandCount != kept || flow.Session.Hash != keptHash) throw new InvalidOperationException("두 걸음 뒤 상태로 돌아가지 않았다(명령 " + flow.Session.CommandCount + " · 바라는 것 " + kept + ")");
            if (flow.Client.View.Seats[0].Cell != two) throw new InvalidOperationException("말 칸 " + flow.Client.View.Seats[0].Cell + " · 바라는 것 " + two);
            var piece = s.Board.Pieces.Pieces[0].Spot;
            if (piece == null || piece.Cell != two) throw new InvalidOperationException("보드의 말이 두 걸음 칸에 다시 서지 않았다(" + (piece != null ? piece.Cell : -1) + ")");
            if (flow.Hud == null || flow.Hud == hud) throw new InvalidOperationException("HUD를 새로 세우지 않았다");
            if (flow.Fx.Sequencer.Queued.Any()) throw new InvalidOperationException("되돌린 뒤 연출이 남았다");
            if (!flow.InputOpen) throw new InvalidOperationException("되돌린 뒤 입력이 열리지 않았다");
            return flow;
        }

        static void UndoResumed(UiShot s)
        {
            var flow = UndoBack(s);
            UndoIs(flow, true);   // 두 걸음 걷기도 같은 턴 안이라 또 무를 수 있다
            UndoHead(s, flow, false, BoardHud.StampButton);
        }

        static void UndoLog(UiShot s)
        {
            var flow = UndoBack(s);
            var lines = flow.Log.Lines;
            var last = lines.Count > 0 ? lines[lines.Count - 1] : null;
            if (last == null || last.Line.Key != Sinnoh.Fx.BoardLog.K + "undo" || last.Seat != 0) throw new InvalidOperationException("기록 끝 줄이 「무르기를 했다」가 아니다(" + (last != null ? last.Line.Key : "없음") + ")");
            int next = flow.Session.NextSeq;
            if (lines.Any(l => l.Seq != Sinnoh.Fx.BoardLog.ResumedSeq && l.Seq >= next)) throw new InvalidOperationException("지운 수의 기록 줄이 남았다");
            flow.OpenLog();
        }

        static void UndoSolo(UiShot s)
        {
            var flow = s.Flow();
            flow.OpenSolo();
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            if (root.Q("solo-row-" + SoloScreen.Undo) == null) throw new InvalidOperationException("혼자 하기 설정에 「무르기」 줄이 없다");
            var solo = (SoloScreen)flow.Stage.Current;
            if (solo.Settings.Undo != SoloGame.NewGameUndo || !root.Q("solo-opt-" + SoloScreen.Undo + "-turn").ClassListContains("sn-opt-picked")) throw new InvalidOperationException("새 판의 무르기 처음 값이 「내 턴만」이 아니다");
            RoomFit(s, flow, "solo-col");
            TipIs(flow, root.Q("solo-opt-" + SoloScreen.Undo + "-free"), flow.Stage.T("screen.solo.undo.free.tip"));
        }
    }
}
