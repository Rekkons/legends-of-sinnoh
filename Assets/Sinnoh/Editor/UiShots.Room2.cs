using System;
using System.Collections.Generic;
using System.IO;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M7 「화면 ② 대기실 · 재접속 · 메뉴 · 응답 시계」 확인 장면(04 S10 · S11 · S22 · 02 §5-3 · §5-7 · §6 · §7): 망 없이 — 게임 화면 몫은 가짜 방(<see cref="RoomLive"/> 가짜 · <see cref="RoomFlow.ShowGame"/>)을
    /// 혼자 하는 확인용 판(좌석 셋) 위에, 대기실은 가짜 로비(Waiting), 방 저장은 이 장면이 임시 폴더에 방(HostSession — 연결 없음)을 열어 쓴다(사용자 저장 폴더 금지).
    /// 실제 접속 · 재접속 · 투표 · 이어 열기는 PlayMode <c>RoomGameFlowTests</c>. 장면마다 그 판의 글 넘침 · 화면 안을 단정한다(<see cref="RoomFit"/>).
    /// </summary>
    public static partial class UiShots
    {
        const long RoomNow = 600000;

        public static readonly KeyValuePair<string, Action<UiShot>>[] Room2Screens =
        {
            Screen("room-wait-host", s => RoomWait(s, true)),                       // S10 방장: 저장된 좌석(참가 중 · 기다리는 중 · 강퇴됨) · 자리 확인 한 줄 · [지금 재개] · 손님 몫 방 설정(게임 중 칸만 바꿈)
            Screen("room-wait-guest", s => RoomWait(s, false)),                     // S10 손님: 「방장이 게임을 다시 시작하기를 기다리는 중입니다.」 · [나가기]
            Screen("room-reopen", s =>                                              // S04 방 저장 [이어하기] → S05 「방 다시 열기」(연결 방식만 · 저장한 게임 한 줄)
            {
                var flow = RoomSaveFlow(s, out string id);
                if (!flow.Room.OpenResume(id)) throw new InvalidOperationException("방 저장을 열지 못했다");
                if (!(flow.Stage.Current is RoomCreateScreen c) || !c.Reopening) throw new InvalidOperationException("「방 다시 열기」가 아니다");
                RoomFit(s, flow, "room-create-col");
            }),
            Screen("saves-room", s =>                                               // S04: 방 저장 칸 「방 3명 · 신오의 심장 · 게임 준비」 / 「방장트레이너 외 2명 · 시각」
            {
                var flow = RoomSaveFlow(s, out string id);
                flow.OpenSaves();
                var list = flow.Stage.Current as SaveListScreen ?? throw new InvalidOperationException("저장 목록이 열리지 않았다");
                if (!list.IsRoom(id) || flow.Stage.Root.Q("saves-resume-0") == null) throw new InvalidOperationException("방 저장 칸 · [이어하기]가 없다");
                RoomFit(s, flow, "saves-col");
            }),
            Screen("reconnect-trying", s => Reconnect(s, l => l.FakeReconnecting(RoomNow - 12000))),          // S11: 다시 연결하는 중 · 12초 · [타이틀로]
            Screen("reconnect-lost", s => Reconnect(s, l => l.FakeFailed(RoomLive.LostCode))),                     // 3분 안에 못 붙음: 이유 + [다시 시도] [타이틀로]
            Screen("reconnect-closed", s => Reconnect(s, l => l.FakeFailed(NetCode.HostClosed, RoomPhase.InGame))),   // 「방장이 방을 닫았습니다.」 + [다시 시도]
            Screen("reconnect-kicked", s => Reconnect(s, l => l.FakeFailed(NetCode.Kicked))),                 // 「방에서 내보내졌습니다.」 — [타이틀로]만
            Screen("room-clock-mine", s => BandClock(RoomGame(s, l => RunClock(l, 0, ClockKind.MapTurn, 83000, 120000, 0)), Strings()["screen.room.clock.mine"].Replace("{t}", "1:23") + " · " + Strings()["screen.room.clock.bank_short"].Replace("{t}", "2:00"))),   // 내 턴: 위 띠 안 턴 배지 앞(결정 112 Q5) 「내 시간 1:23 · 예비 2:00」
            Screen("room-clock-wait", s => BandClock(RoomGame(s, l => RunClock(l, 1, ClockKind.Respond, 23000, 120000, 0)), Strings()["screen.room.clock.band_wait"].Replace("{t}", "0:23").Replace("{name}", "빛나"))),        // 남의 차례: 위 띠 안 「대기 0:23 · 빛나」
            Screen("room-clock-low", s => RoomGame(s, l => { RunClock(l, 0, ClockKind.Reaction, 16000, 120000, 9000); RunClock(l, 2, ClockKind.Reaction, 16000, 0, 9000); })),   // 반응 창 남은 7초(뱅크 없음): 내 시간 · 응답 대기 줄 모두 빨강
            Screen("room-pause", s =>                                                                                                        // 일시정지 띠(결정 112 Q6 — isekai): 「일시정지 중 · 건 사람: 빛나, 용식 · 0:00 지남」 + 방장 [모두 해제] · 재접속 덮개 없이 뒤 보드 그대로
            {
                var flow = RoomGame(s, l => { RunClock(l, 0, ClockKind.MapTurn, 83000, 120000, 0); l.Room.Settings.Pause = PauseMode.Everyone; l.Clock.PausedBy.Add(1); l.Clock.PausedBy.Add(2); });
                string text = flow.RoomLayer.PauseText.Replace("⁠", "");
                if (!text.Contains("빛나, 용식") || !text.Contains(Strings()["screen.room.pause.since"].Replace("{t}", ""))) throw new InvalidOperationException("일시정지 띠 글: " + text);
                if (flow.Stage.Root.Q("room-pause-all") == null || flow.Stage.Root.Q("room-pause-off") != null) throw new InvalidOperationException("방장(건 사람 아님)에게 [모두 해제]만 있어야 한다");
            }),
            Screen("room-pause-mine", s =>                                                                                                   // 내가 건 정지(손님): [일시정지 풀기]만 · 메뉴에도 [일시정지 풀기]
            {
                var flow = RoomGame(s, l => { RunClock(l, 0, ClockKind.MapTurn, 83000, 120000, 0); l.Room.Settings.Pause = PauseMode.Everyone; l.Clock.PausedBy.Add(0); }, host: false);
                if (flow.Stage.Root.Q("room-pause-off") == null || flow.Stage.Root.Q("room-pause-all") != null) throw new InvalidOperationException("내 정지에는 [일시정지 풀기]만");
            }),
            Screen("room-pause-reserved", s =>                                                                                               // 「모두」 방의 예약: 「일시정지 예약: 방장트레이너, 용식」 + 내 예약 [예약 취소] · 남의 예약이 있어 방장 [모두 해제]
            {
                var flow = RoomGame(s, l => { RunClock(l, 1, ClockKind.MapTurn, 83000, 120000, 0); l.Room.Settings.Pause = PauseMode.Everyone; l.Room.PauseReserved.Add(0); l.Room.PauseReserved.Add(2); });
                if (!flow.RoomLayer.PauseText.Replace("⁠", "").StartsWith(Strings()["screen.room.pause.reserved"].Replace("{name}", ""), StringComparison.Ordinal)) throw new InvalidOperationException("예약 줄: " + flow.RoomLayer.PauseText);
                if (flow.Stage.Root.Q("room-pause-cancel") == null || flow.Stage.Root.Q("room-pause-all") == null) throw new InvalidOperationException("[예약 취소] · [모두 해제]");
            }),
            Screen("room-vote-pause", s => RoomGame(s, l => { l.Room.Settings.Pause = PauseMode.Vote; Vote(l, VoteKind.Pause, 1); })),        // 「투표」 방: 「일시정지 투표: 빛나」 · 찬성 1/2 · [찬성] [반대]
            Screen("room-vote", s => RoomGame(s, l => Vote(l, VoteKind.Kick, 2))),                                                           // 강퇴 투표 판: 「강퇴 투표: 용식」 · 찬성 1/2 · 남은 시간 · [찬성] [반대]
            Screen("room-vote-tip", s =>                                                                                                     // 투표 제목의 자세한 말(마우스 오버)
            {
                var flow = RoomGame(s, l => Vote(l, VoteKind.Away, 1));
                flow.Stage.Tips.Show(flow.Stage.Root.Q("room-vote-title"));
            }),
            Screen("room-notice", s => RoomGame(s, l =>                                                                                      // 알림 줄 셋(끊김 · 시간 초과 · 투표 가결)
            {
                l.Note(new RoomNotice { Code = NetCode.PlayerLeft, Seat = 2, AtMs = RoomNow - 3000 });
                l.Note(new RoomNotice { Code = NetCode.AutoResolved, Seat = 2, AtMs = RoomNow - 2000 });
                l.Note(new RoomNotice { Code = NetCode.VotePassed, Seat = 2, Vote = VoteKind.Away, AtMs = RoomNow - 500 });
                l.Note(new RoomNotice { Code = NetCode.PlayerBack, Seat = 1, AtMs = RoomNow - 9000 });   // 5초 지난 알림은 보이지 않는다
            })),
            Screen("room-ping", s =>                                                                                               // 핑 찍기(결정 97): 세 좌석이 찍은 칸 — 좌석 색 고리 · 물결 · 이름(막 찍은 것 · 1.4초 · 3.7초 지나 옅어지는 것)
            {
                var flow = RoomGame(s, l => RunClock(l, 1, ClockKind.MapTurn, 83000, 120000, 0));
                var live = flow.Room.Live;
                var open = new List<int>();
                for (int cell = 0; cell < flow.Ctx.Board.Count && open.Count < 3; cell++)
                {
                    if (!flow.CellPoint(cell, out var at) || at.x < 380 || at.x > 1150 || at.y < 130 || at.y > 540) continue;   // 720 토큰 px — HUD 판 밖의 보드 가운데
                    if (open.Exists(c => flow.CellPoint(c, out var o) && UnityEngine.Vector2.Distance(o, at) < 150)) continue;
                    open.Add(cell);
                }
                if (open.Count < 3) throw new InvalidOperationException("핑을 찍을 칸 셋을 찾지 못했다");
                live.AddPing(0, open[0], RoomNow - 100);
                live.AddPing(1, open[1], RoomNow - 1400);
                live.AddPing(2, open[2], RoomNow - 3700);
                Pass(flow, 0);
                s.Panel.Tick(3);
                if (flow.RoomLayer.PingsShown != 3 || flow.Stage.Root.Q("room-ping-2-name") == null) throw new InvalidOperationException("핑 표시 셋이 그려지지 않았다: " + flow.RoomLayer.PingsShown);
            }),
            // ── 소통(결정 102, 02 §4-8) — 그림 Logs/Review/m7/social/ ──
            Screen("social-ping-kinds", s =>                                                       // 핑 종류 넷(여기 봐 · ? · 좋아 · 조심): 보드의 누른 자리(칸 가운데가 아니다) — 고리 가운데 글자 · 「이름 · 종류」
            {
                var flow = RoomGame(s, null);
                var live = flow.Room.Live;
                var open = SocialCells(flow, 4);
                var off = new[] { (0, 0), (600, -300), (-700, 400), (300, 800) };
                for (int i = 0; i < 4; i++) live.AddPing(i % 3, i, PingTarget.Cell(open[i]).At2(off[i].Item1, off[i].Item2), RoomNow - 150 - 250 * i);
                SocialSettle(s, flow);
                if (flow.RoomLayer.PingsShown != 4) throw new InvalidOperationException("핑 넷이 그려지지 않았다: " + flow.RoomLayer.PingsShown);
                for (int i = 0; i < 4; i++)
                {
                    if (!flow.CellSpot(open[i], off[i].Item1, off[i].Item2, out var want) || UnityEngine.Vector2.Distance(want, flow.RoomLayer.PingPoint(i)) > 1.5f) throw new InvalidOperationException("핑 " + i + "이 누른 자리에 있지 않다");
                    string tag = SocialText(flow, "room-ping-" + i + "-name");
                    if (tag != flow.RoomLayer.Element.Q<Label>("room-ping-" + i + "-name").text.Replace("⁠", "") || !tag.EndsWith(" · " + flow.RoomLayer.PingKind(i), StringComparison.Ordinal)) throw new InvalidOperationException("핑 이름 띠: " + tag);
                }
                if (flow.CellPoint(open[1], out var centre) && UnityEngine.Vector2.Distance(centre, flow.RoomLayer.PingPoint(1)) < 4f) throw new InvalidOperationException("누른 자리가 칸 가운데와 같다");
            }),
            Screen("social-ping-spots", s =>                                                       // 보드 밖 공개 대상의 핑: 좌석 칩 · 위 띠 라운드 배지 · 내 파티 칸 · 남의 파티 칸(→ 그 좌석 칩) · 기록 판
            {
                var flow = RoomGame(s, null);
                var live = flow.Room.Live;
                int round = PingSpots.Find("board-hud-round", out _), mon = PingSpots.Find("board-hud-mon-0", out _), log = PingSpots.Find("board-hud-log", out _);
                live.AddPing(1, 3, PingTarget.OfSeat(1).At2(800, 500), RoomNow - 200);
                live.AddPing(2, 1, PingTarget.Spot(round).At2(500, 500), RoomNow - 400);
                live.AddPing(0, 2, PingTarget.Spot(mon, 0, 0).At2(300, 400), RoomNow - 600);
                live.AddPing(2, 0, PingTarget.Spot(mon, 0, 2).At2(300, 400), RoomNow - 800);
                live.AddPing(1, 0, PingTarget.Spot(log).At2(700, 600), RoomNow - 1000);
                SocialSettle(s, flow);
                if (flow.RoomLayer.PingsShown != 5) throw new InvalidOperationException("핑 다섯이 그려지지 않았다: " + flow.RoomLayer.PingsShown);
                SocialIn(flow, 0, "board-hud-seat-2");
                SocialIn(flow, 1, "board-hud-round");
                SocialIn(flow, 2, "board-hud-mon-0");
                SocialIn(flow, 3, "board-hud-seat-3");
                SocialIn(flow, 4, "board-hud-log");
                // 보내는 쪽: 같은 요소 위의 점에서 같은 대상을 찾는다 · 목록 밖 요소([메뉴])는 대상이 아니다
                var t = flow.RoomLayer.TargetAt(flow.Stage.Root.Q("board-hud-round").worldBound.center);
                if (t == null || t.At != PingAt.Spot || t.N != round) throw new InvalidOperationException("라운드 배지 위에서 대상을 찾지 못했다");
                if (flow.RoomLayer.TargetAt(flow.Stage.Root.Q("board-hud-menu").worldBound.center) != null) throw new InvalidOperationException("[메뉴]가 핑 대상이다");
            }),
            Screen("social-ping-wheel", s =>                                                       // 종류 고르기: Alt를 누른 채 누르고 끌면 뜨는 고리(위 여기 봐 · 오른쪽 ? · 아래 좋아 · 왼쪽 조심) — 오른쪽을 가리키는 중
            {
                var flow = RoomGame(s, null);
                var open = SocialCells(flow, 1);
                if (!flow.CellPoint(open[0], out var at)) throw new InvalidOperationException("칸 자리");
                float scale = flow.Stage.Grade.PanelScale;
                float x = at.x * scale, y = s.Height - at.y * scale;
                flow.PingKey = true;
                flow.Pointer(x, y, true, false);
                flow.Pointer(x + 44 * scale, y - 6 * scale, true, false);
                flow.PingKey = false;
                SocialSettle(s, flow);
                if (!flow.RoomLayer.WheelUp || flow.RoomLayer.WheelSector != 1) throw new InvalidOperationException("종류 고리가 뜨지 않았다: " + flow.RoomLayer.WheelUp + " / " + flow.RoomLayer.WheelSector);
                var wheel = flow.Stage.Root.Q("room-ping-wheel");
                if (wheel == null || wheel.resolvedStyle.display == DisplayStyle.None || !flow.Stage.Root.Q("room-ping-wheel-1").ClassListContains("sn-room-wheel-on")) throw new InvalidOperationException("고리의 「?」가 밝지 않다");
                var root = flow.Stage.Root.worldBound;
                for (int i = 0; i < SocialCodes.Ping; i++)
                {
                    var item = flow.Stage.Root.Q("room-ping-wheel-" + i).worldBound;
                    if (item.xMin < root.xMin || item.yMin < root.yMin || item.xMax > root.xMax || item.yMax > root.yMax) throw new InvalidOperationException("고리 칸 " + i + "이 화면 밖이다");
                }
                if (flow.Room.Live.Sent.Exists(m => m is SendSocial)) throw new InvalidOperationException("떼기 전에 보냈다");
            }),
            Screen("social-ping-battle", s =>                                                      // 배틀 화면의 핑: 상대 쪽 정보 기둥 · DS 화면 · 기술 줄
            {
                var flow = ToBattle(s);
                flow.Room.ShowGame(FakeGame(flow, false));
                var live = flow.Room.Live;
                int info = PingSpots.Find("battle-info-foe", out _), ds = PingSpots.Find("battle-ds", out _), move = PingSpots.Find("battle-move-1", out int moveK);
                int side = flow.Battle.MySide;
                live.AddPing(1, 1, PingTarget.Spot(info, 1 - side).At2(300, 450), RoomNow - 200);
                live.AddPing(2, 3, PingTarget.Spot(ds, side).At2(720, 380), RoomNow - 500);
                live.AddPing(1, 2, PingTarget.Spot(move, moveK).At2(500, 500), RoomNow - 800);
                SocialSettle(s, flow);
                if (flow.RoomLayer.PingsShown != 3) throw new InvalidOperationException("배틀 화면 핑 셋이 그려지지 않았다: " + flow.RoomLayer.PingsShown);
                SocialIn(flow, 0, "battle-info-foe");
                SocialIn(flow, 1, "battle-ds");
                SocialIn(flow, 2, "battle-move-1");
                var t = flow.RoomLayer.TargetAt(flow.Stage.Root.Q("battle-info-foe").worldBound.center);
                if (t == null || t.N != info || t.K != 1 - side) throw new InvalidOperationException("상대 쪽 정보 기둥 위에서 대상을 찾지 못했다");
            }),
            Screen("social-emote-menu", s =>                                                       // [감정 표현] 메뉴: 문구 일곱 줄 · 연타 제한 안내 · 상대별 [반응 끄기]·[핑 끄기](한 좌석은 꺼 둔 모습)
            {
                var flow = RoomGame(s, null);
                if (flow.RoomLayer.ChipMenu(1) || !flow.RoomLayer.ChipMenu(flow.Client.Seat)) throw new InvalidOperationException("내 좌석 칩 오른쪽 클릭으로만 열린다(결정 112 Q12)");
                if (!flow.RoomLayer.ToggleMute(2, false)) throw new InvalidOperationException("반응 끄기가 거절됐다");
                SocialSettle(s, flow);
                if (flow.Stage.Root.Q("room-emote") != null) throw new InvalidOperationException("가운데 줄의 [감정 표현] 단추가 남았다");
                var chip = flow.Stage.Root.Q("board-hud-seat-" + (flow.Client.Seat + 1)).worldBound;
                var menuBox = flow.Stage.Root.Q("room-emote-menu").worldBound;
                if (menuBox.yMin < chip.yMax - 0.5f || UnityEngine.Mathf.Abs(menuBox.xMin - chip.xMin) > 2f) throw new InvalidOperationException("메뉴가 내 칩 아래가 아니다: " + menuBox + " / " + chip);
                for (int i = 0; i < SocialCodes.Emote; i++)
                    if (SocialText(flow, "room-emote-line-" + i) != flow.Stage.T("screen.room.emote.line." + i)) throw new InvalidOperationException("문구 " + i + ": " + SocialText(flow, "room-emote-line-" + i));
                if (SocialText(flow, "room-emote-mute-2") != flow.Stage.T("screen.room.emote.unmute") || SocialText(flow, "room-emote-mute-1") != flow.Stage.T("screen.room.emote.mute")) throw new InvalidOperationException("상대별 끄기 단추 글");
                if (flow.Stage.Root.Q("room-emote-mute-0") != null) throw new InvalidOperationException("내 줄이 있다");
                RoomFit(s, flow, "room-emote-menu");
            }),
            Screen("social-emote-wait", s =>                                                       // 메뉴에서 한 줄을 보낸 직후 다시 연 모습: 2초 동안 줄이 꺼지고 「잠시 뒤에 보낼 수 있습니다.」
            {
                var flow = RoomGame(s, null);
                if (!flow.RoomLayer.ToggleEmotes()) throw new InvalidOperationException("메뉴가 열리지 않았다");
                SocialSettle(s, flow);
                if (!flow.RoomLayer.PressEmote(5) || flow.RoomLayer.EmotesOpen || flow.RoomLayer.PressEmote(0)) throw new InvalidOperationException("보내기 · 연타 제한");
                if (!(flow.Room.Live.Sent.FindLast(m => m is SendSocial) is SendSocial sent) || sent.Kind != SocialKind.Emote || sent.Code != 5 || sent.Target != null) throw new InvalidOperationException("SendSocial을 보내지 않았다");
                flow.RoomLayer.ToggleEmotes();
                SocialSettle(s, flow);
                if (SocialText(flow, "room-emote-wait") != flow.Stage.T("screen.room.emote.wait")) throw new InvalidOperationException("「잠시 뒤에」 안내가 없다");
                if (flow.Stage.Root.Q<Button>("room-emote-line-0").enabledSelf) throw new InvalidOperationException("기다리는 동안 줄이 켜져 있다");
                RoomFit(s, flow, "room-emote-menu");
            }),
            Screen("social-emote-lines", s => SocialBubbles(s, 6, new[] { 0, 1, 2, 3, 4, 5 })),    // 말풍선 — 좌석 여섯이 한꺼번에(문구 0~5): 좌석 칩 얼굴 아래 · 옆 말풍선과 겹치면 한 줄 아래
            Screen("social-emote-long", s => SocialBubbles(s, 3, new[] { 6, 5, 3 })),              // 말풍선 — 긴 문구(「제 포켓몬이 …」 · 「아르세우스가 …」)
            Screen("social-mark", s =>                                                             // 도장: 기록 창 — 남의 행동 줄에 좌석 둘의 칩(대단해 · 아깝다) + 다른 줄에 연 도장 메뉴
            {
                var flow = SocialActed(s);
                var live = flow.Room.Live;
                int me = flow.Client.Seat;
                var theirs = new List<Sinnoh.Fx.BoardLogLine>();
                foreach (var line in flow.Log.Lines)
                    if (line.Kind == Sinnoh.Fx.BoardLogKind.Line && line.Seat >= 0 && line.Seat != me && line.Seq >= 0 && !theirs.Exists(o => o.Seq == line.Seq)) theirs.Add(line);
                if (theirs.Count < 1) throw new InvalidOperationException("남의 행동 기록 줄이 없다");
                flow.OpenLog();
                SocialSettle(s, flow);
                int seq = theirs[theirs.Count - 1].Seq, owner = theirs[theirs.Count - 1].Seat;
                live.AddMark(me, seq, owner, 0);
                live.AddMark(owner == 2 ? 1 : 2, seq, owner, 2);
                live.AddMark(me, seq, owner, 1);   // 한 사람 1개 — 다시 찍으면 바뀐다(대단해 → 헉)
                SocialSettle(s, flow);
                var shown = flow.RoomLayer.MarkShown(seq, owner);
                if (shown.Count != 2) throw new InvalidOperationException("도장 칩 둘이 아니다: " + string.Join(",", shown));
                if (flow.RoomLayer.MarksShown != 1) throw new InvalidOperationException("기록 창에 가린 HUD 기록 판의 줄에도 칩을 그렸다: " + flow.RoomLayer.MarksShown);
                if (shown[0].Replace("⁠", "") != flow.Stage.T("screen.room.mark.kind.1")) throw new InvalidOperationException("다시 찍은 도장으로 바뀌지 않았다: " + shown[0]);
                // 내 행동 줄은 메뉴가 뜨지 않는다 · 남의 줄은 뜬다 → 누르면 보낸다
                foreach (var line in flow.Log.Lines)
                    if (line.Kind == Sinnoh.Fx.BoardLogKind.Line && line.Seat == me && line.Seq >= 0 && flow.RoomLayer.OpenMarks(line.Seq, me)) throw new InvalidOperationException("내 행동 줄에 도장 메뉴가 떴다");
                if (!flow.RoomLayer.OpenMarks(theirs[0].Seq, theirs[0].Seat)) throw new InvalidOperationException("도장 메뉴가 열리지 않았다");
                SocialSettle(s, flow);
                var menu = flow.Stage.Root.Q("room-mark-menu");
                if (menu.resolvedStyle.display == DisplayStyle.None) throw new InvalidOperationException("도장 메뉴가 보이지 않는다");
                RoomFit(s, flow, "room-mark-menu");
                var chips = flow.Stage.Root.Q("room-mark-0").worldBound;
                var panel = flow.Stage.Root.Q("board-log-panel").worldBound;
                if (chips.xMax > panel.xMax + 1f || chips.xMin < panel.xMin) throw new InvalidOperationException("도장 칩이 기록 창 밖이다: " + chips + " / " + panel);
            }),
            Screen("social-mark-hud", s =>                                                         // 도장: HUD 기록 판의 줄 끝 칩 + 도장을 보낸 뒤
            {
                var flow = SocialActed(s);
                var live = flow.Room.Live;
                int me = flow.Client.Seat;
                SocialSettle(s, flow);
                Sinnoh.Fx.BoardLogLine last = null;
                flow.Stage.Root.Q("board-hud-log-lines").Query<VisualElement>(className: "sn-log-row").ForEach(e =>
                {
                    if (e.userData is Sinnoh.Fx.BoardLogLine line && line.Seat >= 0 && line.Seat != me) last = line;
                });
                if (last == null) throw new InvalidOperationException("HUD 기록 판에 남의 행동 줄이 없다");
                if (!flow.RoomLayer.OpenMarks(last.Seq, last.Seat) || !flow.RoomLayer.PressMark(0)) throw new InvalidOperationException("도장을 보내지 못했다");
                if (!(live.Sent.FindLast(m => m is SendSocial) is SendSocial sent) || sent.Kind != SocialKind.Mark || sent.Seq != last.Seq || sent.Owner != last.Seat) throw new InvalidOperationException("SendSocial(도장)이 틀리다");
                live.AddMark(me, last.Seq, last.Seat, 0);
                live.AddMark(last.Seat == 1 ? 2 : 1, last.Seq, last.Seat, 1);
                SocialSettle(s, flow);
                if (flow.RoomLayer.MarkShown(last.Seq, last.Seat).Count != 2) throw new InvalidOperationException("HUD 기록 판의 도장 칩 둘이 아니다");
                var chips = flow.Stage.Root.Q("room-mark-0").worldBound;
                var box = flow.Stage.Root.Q("board-hud-log").worldBound;
                if (chips.xMax > box.xMax + 1f || chips.yMin < box.yMin - 1f || chips.yMax > box.yMax + 1f) throw new InvalidOperationException("도장 칩이 기록 판 밖이다: " + chips + " / " + box);
            }),
            Screen("room-menu-host", s => RoomMenu(s, true, BoardMenuScreen.MainPage, false)),     // S22 방장: 저장 줄 · 돌아가기 · 일시정지 · 투표 · 방 설정 · 설정 · 방 닫기
            Screen("room-menu-guest", s => RoomMenu(s, false, BoardMenuScreen.MainPage, false)),   // S22 손님: 「방장의 PC에 저장됩니다.」 · 방에서 나가기
            Screen("room-menu-vote", s => RoomMenu(s, true, BoardMenuScreen.VotePage, false)),     // 투표 쪽: 좌석마다 [강퇴 투표] [자리 비움 투표] · 끊긴 좌석은 방장 [자리 비움으로]
            Screen("room-menu-set", s => RoomMenu(s, true, BoardMenuScreen.SetPage, false)),       // 게임 중 방 설정: 상대 조작 · 갤럭시단 AI · 제한 시간(방장만 바꿈)
            Screen("room-menu-quit", s => RoomMenu(s, false, BoardMenuScreen.MainPage, true)),     // 「방에서 나가겠습니까?」 + 「같은 기기로 다시 참가하면 원래 자리로 돌아옵니다.」
            Screen("room-arrive6", s => Arrive6(s)),                                              // 6인 방 판의 좌석 도착(2행 — plan M7 완료 조건 6): 다섯이 내려앉고 여섯째 말이 떨어지는 중 · 좌석 띠 = 방 닉네임
            Screen("room-seats6-long", s => SeatsLong(s, 6, true)),                              // 6인 모두 12자 닉네임 · 좌석 2의 턴: 칩 이름 = 한글 6자 폭(결정 112 Q15) · 1080 벌은 턴 배지 숨김 · 응답 시계는 가운데 줄(좌석 다섯 이상)
            Screen("room-seats4-long", s => SeatsLong(s, 4, true)),                              // 4인 모두 12자 · 좌석 2의 턴: 응답 시계가 위 띠 안(턴 배지 앞 — 결정 112 Q5) · 상태 판·[메뉴]가 화면 안
            Battle("battle-spectate", s => Spectate(s, false)),                                   // M7 마감 감사 1: 남(빛나)의 배틀을 보는 기둥 = 빛나의 이름 · 얼굴 · 대기, 내 [운명의 변화] 없음
            Battle("battle-spectate-result", s => Spectate(s, true)),
            Screen("room-wild-aside", s => WildAside(s, false)),                                  // 결정 114 · 120: 남(빛나)의 야생 배틀 — 내 정비 창이 떠 있어 가운데 필드는 그대로, 위 띠 아래 「빛나 야생 배틀 중」 [보기]
            Battle("battle-wild-watch", s => WildAside(s, true)),                                 // 창이 없으면 저절로 그 배틀 화면 + 「빛나 야생 배틀 관전 중」 [보드로](접기)                            // 그 배틀의 결과 창 머리 = 「빛나의 승리!」/「빛나의 패배…」
            Screen("room-setup-leave", s =>                                                        // 방 판의 셋업: [뒤로] 대신 [방에서 나가기]
            {
                var flow = s.Flow();
                flow.StartSolo(new SoloSettings(), Seed, AiSeed);
                flow.Room.ShowGame(FakeGame(flow, false));
                flow.Stage.Bind(flow.Stage.Root, flow.Stage.Grade);   // 셋업 화면을 방 판 모양으로 다시 세운다
                if (!(flow.Stage.Current is SetupVillageScreen)) throw new InvalidOperationException("고향 마을 화면이 아니다");
                string back = (flow.Stage.Root.Q<Button>("setup-village-back")?.text ?? "").Replace("⁠", "");
                if (back != flow.Stage.T("screen.room.menu.leave")) throw new InvalidOperationException("[방에서 나가기]가 아니다: " + back);
                RoomFit(s, flow, "setup-village");
            }),
            Screen("room-setup-leave-ask", s =>                                                    // [방에서 나가기] → 묻는 창(결정 112 Q8) 「방에서 나가겠습니까?」 + [방에서 나가기] [취소]
            {
                var flow = s.Flow();
                flow.StartSolo(new SoloSettings(), Seed, AiSeed);
                flow.Room.ShowGame(FakeGame(flow, false));
                flow.Stage.Bind(flow.Stage.Root, flow.Stage.Grade);
                var village = flow.Stage.Current as SetupVillageScreen ?? throw new InvalidOperationException("고향 마을 화면이 아니다");
                village.Back();
                if (!village.Asking || !flow.Room.InGame) throw new InvalidOperationException("묻지 않고 나갔다");
                RoomFit(s, flow, "setup-village-ask-panel");
            }),
            Screen("room-tut-back", s =>                                                           // 참가 화면에서 들어간 튜토리얼의 끝내기 창: [타이틀로] 자리가 「돌아가기」(결정 112 Q14)
            {
                var flow = s.Flow();
                flow.OpenTitle();
                flow.Room.Board = RoomFlow.NoRooms;
                flow.Room.OpenJoin();
                if (!((RoomJoinScreen)flow.Stage.Current).Tutorial() || !flow.Tutorial.Running) throw new InvalidOperationException("튜토리얼이 열리지 않았다");
                Pass(flow, 0);
                if (!flow.Tutorial.OpenQuit()) throw new InvalidOperationException("끝내기 창이 열리지 않았다");
                s.Panel.Tick(3);
                string label = (flow.Stage.Root.Q<Button>("tut-quit-title")?.text ?? "").Replace("⁠", "");
                if (label != flow.Stage.T("screen.tutorial.menu.to_back")) throw new InvalidOperationException("「돌아가기」가 아니다: " + label);
                RoomFit(s, flow, "tut-quit-panel");
            }),
            Screen("battle-autopass", s =>                                                         // 방 판의 운명 창: [넘기기] 옆 [이번 배틀 묻지 않기]
            {
                var flow = FateBattle(s, SoloTrail.Fate, false);
                flow.Room.ShowGame(FakeGame(flow, false));
                var b = (BattleScreen)flow.Stage.Current;
                b.Refresh();
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
                if (flow.Stage.Root.Q(BattleScreen.AutoPassButton) == null) throw new InvalidOperationException("[이번 배틀 묻지 않기]가 없다");
                if (!b.PressAutoPass() || !(flow.Room.Live.Sent.Find(m => m is SetAutoPass) is SetAutoPass)) throw new InvalidOperationException("SetAutoPass를 보내지 않았다");
                var bad = WorkUi.Overflows(flow.Stage.Root.Q("battle-dock"));
                if (bad.Count > 0) throw new InvalidOperationException("배틀 독 글 넘침: " + string.Join(" / ", bad));
            }),
        };

        // 가짜 방(손님 둘 — 빛나 · 용식): 내 자리 = 그 흐름의 좌석. host = 내가 방장인가
        static RoomLive FakeGame(GameFlow flow, bool host)
        {
            int me = flow.Client != null ? flow.Client.Seat : 0;
            var live = new RoomLive(() => RoomNow)
            {
                IsHost = host, Seat = me, Saving = host, SavedAtMs = SaveClock, ClockAtMs = RoomNow, RoomAtMs = RoomNow,
                Room = new RoomState
                {
                    Phase = RoomPhase.InGame, RoomId = "room-shot", Settings = new RoomSettings { Seats = 3, Clock = ClockPreset.Relaxed },
                    Seats = new List<RoomSeat>
                    {
                        new RoomSeat { Nickname = "방장트레이너", Host = true, Connected = true },
                        new RoomSeat { Nickname = "빛나", Connected = true },
                        new RoomSeat { Nickname = "용식", Connected = false, Platform = Platform.Pc },
                    },
                },
                Clock = new ClockState { Preset = ClockPreset.Relaxed, Seats = new List<SeatClock> { new SeatClock { BankMs = 120000 }, new SeatClock { BankMs = 120000 }, new SeatClock { BankMs = 90000, Connected = false } } },
            };
            return live;
        }

        // 6인 방 판(셋업부터 이 흐름이 본다 — 다른 좌석의 답은 AnswerSeat) + 가짜 방 닉네임 여섯: 모두 고향 마을을 고른 뒤 여섯째 좌석의 말이 떨어지는 순간
        static GameFlow Arrive6(UiShot s)
        {
            string[] nicks = { "방장트레이너", "빛나", "용식", "멀리서온손님열두글자", "Dawn", "여섯째" };
            var flow = s.Flow();
            flow.StartSeats(new SoloSettings(), 6, Seed, AiSeed, null, liveSetup: true);
            var live = FakeGame(flow, true);
            live.Room.Settings.Seats = 6;
            live.Room.Seats.Clear();
            live.Clock.Seats.Clear();
            for (int i = 0; i < 6; i++)
            {
                live.Room.Seats.Add(new RoomSeat { Nickname = nicks[i], Host = i == 0, Connected = true, Platform = Platform.Pc });
                live.Clock.Seats.Add(new SeatClock { BankMs = 120000 });
            }
            flow.Room.ShowGame(live);
            flow.Layout(s.Width, s.Height);
            // 고향 마을은 고르는 순서대로 하나씩(내 차례면 화면으로, 아니면 그 좌석의 첫 답) — 앞 좌석의 도착이 끝난 뒤 다음 좌석이 고르고, 마지막 좌석의 도착에서 멈춘다
            int answered = 0, last = -1;
            for (int guard = 0; answered < 6; guard++)
            {
                if (guard > 60) throw new InvalidOperationException("6인 판의 고향 마을 고르기가 끝나지 않았다(" + answered + "명)");
                if (flow.Stage.Current is SetupVillageScreen village && flow.Client.Choice(Sinnoh.Rules.ChoiceKind.VillagePick) != null)
                {
                    village.Pick(0);
                    if (!village.Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
                    last = flow.Client.Seat;
                    answered++;
                }
                else
                {
                    for (int seat = 1; seat < 6; seat++)
                        if (flow.AnswerSeat(seat))
                        {
                            last = seat;
                            answered++;
                            break;
                        }
                }
                if (answered < 6)
                {
                    flow.Stage.Tweens.Advance(flow.QueueLeftMs);
                    flow.Layout(s.Width, s.Height);
                }
            }
            flow.Stage.Tweens.Advance(flow.MsUntil(st => st.Row == Sinnoh.Fx.SetupRows.Village && Sinnoh.Fx.VillageBeats.Picked(st) is var p && p != null && p.Seat == last, 620));
            s.Panel.Tick(3);
            var drop = flow.Stage.Current as SetupDropScreen ?? throw new InvalidOperationException("좌석 도착 화면이 아니다: " + flow.Stage.Current?.Name);
            if (drop.Seat != last) throw new InvalidOperationException("마지막 좌석의 도착이 아니다: " + drop.Seat + " / " + last);
            if (flow.SeatName(last) != nicks[last] || flow.SeatName(3) != nicks[3]) throw new InvalidOperationException("좌석 이름이 방 닉네임이 아니다: " + flow.SeatName(last));
            RoomFit(s, flow, "setup-drop-band");
            return flow;
        }

        // n인 방 판(좌석 2의 턴) + 가짜 방 닉네임 모두 12자(RoomFlow.NicknameMax — 한국어 · 영문 · 섞음, 넓은 글자 W·M 포함): 마지막 칩이 상태 판과 겹치지 않고, 상태 판·[메뉴]가 화면 안,
        // 이름은 칩 밖으로 나가지 않고 한글 6자 폭(결정 112 Q15 — 넘으면 「…」 · 마우스 오버 = 전체 이름). clock = 좌석 2의 응답 시계를 흐르게(결정 112 Q5 — 좌석 넷 이하면 위 띠 안, 다섯 이상이면 가운데 줄)
        static void SeatsLong(UiShot s, int count, bool clock)
        {
            string[] all = { "열두글자방장닉네임입니다", "WWWWWWMMMMMM", "빛나는포켓몬트레이너다요", "Sinnoh한국어Mix", "LegendsOfSin", "먼손님Guest열두자다" };
            var nicks = new string[count];
            Array.Copy(all, nicks, count);
            foreach (var n in nicks)
                if (n.Length != RoomFlow.NicknameMax) throw new InvalidOperationException("12자가 아닌 닉네임: " + n + " (" + n.Length + ")");
            var flow = MyTurn(s, count, null);
            EndActive(flow);   // 좌석 2의 턴: 상태 판의 턴 배지에도 12자 이름(「내 턴」보다 넓다 — 가장 빠듯한 때)
            Pass(flow, BoardReadyMs * 8);
            DealSettle(s, flow, f => f.BoardOpen && f.Hud != null && f.Client.View.ActiveSeat == 1);
            var live = FakeGame(flow, true);
            live.Room.Settings.Seats = count;
            live.Room.Seats.Clear();
            live.Clock.Seats.Clear();
            for (int i = 0; i < count; i++)
            {
                live.Room.Seats.Add(new RoomSeat { Nickname = nicks[i], Host = i == 0, Connected = true, Platform = Platform.Pc });
                live.Clock.Seats.Add(new SeatClock { BankMs = 120000 });
            }
            if (clock) RunClock(live, 1, ClockKind.MapTurn, 83000, 120000, 0);
            flow.Room.ShowGame(live);
            flow.Stage.Bind(flow.Stage.Root, flow.Stage.Grade);   // HUD를 방 판 모양으로 다시 세운다(좌석 칩 이름 = 방 닉네임)
            Pass(flow, 0);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            for (int seat = 0; seat < count; seat++)
            {
                var chip = flow.Stage.Root.Q("board-hud-seat-" + (seat + 1)) ?? throw new InvalidOperationException("좌석 " + (seat + 1) + " 칩이 없다");
                var name = chip.Q<Label>(className: "sn-hud-seat-name");
                if (name == null || name.text.Replace("⁠", "") != nicks[seat]) throw new InvalidOperationException("좌석 " + (seat + 1) + " 칩 이름이 방 닉네임이 아니다: " + name?.text);
            }
            var last = flow.Stage.Root.Q("board-hud-seat-" + count).worldBound;
            var status = flow.Stage.Root.Q("board-hud-status").worldBound;
            if (last.xMax > status.xMin) throw new InvalidOperationException("좌석 칩이 상태 판과 겹친다: 칩 오른쪽 " + last.xMax.ToString("0") + " > 상태 판 왼쪽 " + status.xMin.ToString("0"));
            var whole = flow.Stage.Root.worldBound;
            var menu = flow.Stage.Root.Q("board-hud-menu").worldBound;
            if (status.xMax > whole.xMax + 0.5f || menu.xMax > status.xMax) throw new InvalidOperationException("상태 판이 화면 밖으로 밀렸거나 [메뉴]가 판 밖이다: 판 오른쪽 " + status.xMax.ToString("0") + " · [메뉴] 오른쪽 " + menu.xMax.ToString("0") + " · 화면 " + whole.xMax.ToString("0"));
            // 이름은 칩 안에서 「…」로 줄 수 있다(USS): 칩 밖으로 나가거나 옆과 겹치면 실패, 보이는 폭은 한글 6자(결정 112 Q15), 줄어든 이름은 마우스 오버가 전체 이름
            var bad = WorkUi.Overflows(flow.Stage.Root.Q("board-hud-seats")).FindAll(m => !m.Contains("글이 잘린다"));
            if (bad.Count > 0) throw new InvalidOperationException("좌석 칩 글 넘침: " + string.Join(" / ", bad));
            // 보기 단추 묶음(도감 · 상성 · 설명서): 늘 보이고 상태 판 안 · 좌석 넷 이상의 1080 벌은 둘째 줄이라 첫 줄 배지 · 시계 · [메뉴]와 겹치지 않는다(plan §5 #607)
            // 버림 더미(결정 196): 좌석 다섯 이상의 720 벌은 위 띠에 자리가 없어 기록 판 머리의 단추 — 둘 중 하나가 보인다
            var shownDiscards = flow.Stage.Root.Q("board-hud-discards");
            bool discardsUp = shownDiscards != null && shownDiscards.resolvedStyle.display != DisplayStyle.None;
            var logDiscards = flow.Stage.Root.Q("board-hud-log-discards");
            if (!discardsUp && (logDiscards == null || logDiscards.resolvedStyle.display == DisplayStyle.None)) throw new InvalidOperationException("[버림 더미]가 위 띠에도 기록 판에도 없다(좌석 " + count + ")");
            foreach (var b in new[] { "board-hud-dex", "board-hud-types", "board-hud-discards", "board-hud-book" })
            {
                if (b == "board-hud-discards" && !discardsUp) continue;
                var e = flow.Stage.Root.Q(b);
                if (e == null || e.resolvedStyle.display == DisplayStyle.None) throw new InvalidOperationException(b + "이(가) 보이지 않는다(좌석 " + count + ")");
                var r = e.worldBound;
                if (r.xMin < status.xMin - 0.5f || r.xMax > status.xMax + 0.5f || r.yMin < status.yMin - 0.5f || r.yMax > status.yMax + 0.5f) throw new InvalidOperationException(b + "이(가) 상태 판 밖이다: " + r + " / 판 " + status);
            }
            foreach (var other in new[] { "room-clock", "board-hud-turn", "board-hud-round", "board-hud-time", "board-hud-weather", "board-hud-act", "board-hud-menu" }) Apart(flow.Stage.Root, "board-hud-tools", other);
            int cut = 0;
            float six = 0f;
            for (int seat = 0; seat < count; seat++)
            {
                var name = flow.Stage.Root.Q("board-hud-seat-" + (seat + 1)).Q<Label>(className: "sn-hud-seat-name");
                var need = name.MeasureTextSize(name.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                if (need.x > name.contentRect.width + 1f) cut++;
                six = name.MeasureTextSize("가나다라마바", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                if (name.contentRect.width + 0.5f < six) throw new InvalidOperationException("좌석 " + (seat + 1) + " 이름 칸이 한글 6자보다 좁다: " + name.contentRect.width.ToString("0") + " < " + six.ToString("0"));
                flow.Stage.Tips.Show(name);
                var tipTitle = flow.Stage.Tips.Element.Q<Label>(className: "kit-title");
                if (flow.Stage.Tips.Target != name || tipTitle == null || tipTitle.text != nicks[seat]) throw new InvalidOperationException("좌석 " + (seat + 1) + " 이름의 마우스 오버가 전체 이름이 아니다: " + tipTitle?.text);
            }
            // 턴 배지: 좌석 다섯 이상의 1080 벌은 숨긴다(결정 112 Q15 — 칩 테두리 · 넘김 띠가 알린다), 그 밖은 6자 자리까지 + 마우스 오버
            var turnBadge = flow.Stage.Root.Q<Label>("board-hud-turn");
            bool hidden = count >= BoardHud.FollowIconSeats && (flow.Stage.Grade.TokenHeight == 1080 || flow.Lang == "en");   // 영어는 720 벌도(plan §5 #607)
            if (hidden != (turnBadge.resolvedStyle.display == DisplayStyle.None)) throw new InvalidOperationException("턴 배지 숨김이 맞지 않다: " + turnBadge.resolvedStyle.display + " (좌석 " + count + " · " + flow.Stage.Grade.TokenHeight + ")");
            if (!hidden)
            {
                flow.Stage.Tips.Show(turnBadge);
                var turnTip = flow.Stage.Tips.Element.Q<Label>(className: "kit-title");
                if (turnBadge.text.Replace("⁠", "") != nicks[1] || turnTip == null || turnTip.text.Replace("⁠", "") != nicks[1]) throw new InvalidOperationException("턴 배지 · 마우스 오버가 좌석 2의 전체 이름이 아니다: " + turnBadge.text + " / " + turnTip?.text);
                if (turnBadge.worldBound.xMin < status.xMin || turnBadge.worldBound.xMax > status.xMax) throw new InvalidOperationException("턴 배지가 상태 판 밖이다");
            }
            flow.Stage.Tips.Hide();
            if (clock)
            {
                bool band = count <= RoomLayer.BandClockSeats;
                if (flow.RoomLayer.ClockInBand != band) throw new InvalidOperationException("응답 시계 자리: 위 띠 " + flow.RoomLayer.ClockInBand + " (좌석 " + count + ")");
                var strip = flow.Stage.Root.Q("room-clock");
                if (strip == null || strip.resolvedStyle.display == DisplayStyle.None) throw new InvalidOperationException("응답 시계 줄이 없다");
                if (band)
                {
                    var t = flow.Stage.Root.Q("board-hud-turn").worldBound;
                    if (strip.worldBound.xMax > t.xMin + 0.5f || strip.worldBound.xMin < status.xMin) throw new InvalidOperationException("시계가 상태 판 안 턴 배지 앞이 아니다: " + strip.worldBound + " / 턴 " + t);
                }
                else RoomFit(s, flow, "room-layer-col");
            }
            UnityEngine.Debug.Log("[UiShots] room-seats" + count + "-long 줄어든 이름 " + cut + "/" + count + " · 한글 6자 " + six.ToString("0") + " · 좌석 칩 끝 " + last.xMax.ToString("0") + " · 상태 판 " + status.xMin.ToString("0") + "~" + status.xMax.ToString("0") + " · 화면 " + whole.xMax.ToString("0"));
        }

        // 좌석 둘 판(+ 그 순간에 가짜 방 — 좌석 2 = 빛나): 내 턴을 넘기고 좌석 2가 자기 고향 마을의 조무래기와 싸우는 것을 본다(내 손패 둘 — 섞이면 내 「운명의 변화 2/5」가 그 기둥에 보인다).
        // result = 결과 창까지, 아니면 앞 연출이 다 돌고 좌석 2가 기술을 고를 차례. 기둥 내 쪽 = 좌석 2(이름 · 얼굴), 내 [운명의 변화] 없음, 결과 창 머리 = 그 좌석의 승패
        static void Spectate(UiShot s, bool result)
        {
            var flow = Seats(s, 2, st =>
            {
                var ctx = s.Made.Ctx;
                var deck = st.Decks[(int)DeckId.ChangeOfFate];
                foreach (int key in new[] { FateRules.CardKey(ctx, FateCard.Dialga), FateRules.CardKey(ctx, FateCard.Palkia) })
                    if (deck.Remove(key)) st.Seats[0].FateHand.Add(key);
                var two = st.Seats[1];   // 좌석 2: 조무래기의 첫 공격에 쓰러지지 않게 · 대기 둘
                two.OpenSlots = two.Party.Length;
                int starter = two.Party[0];
                for (int i = 0; i < two.Party.Length; i++) two.Party[i] = -1;
                foreach (string key in new[] { "483:2", "484:2" }) PokemonRules.AddToParty(two, PokemonRules.Spawn(st, ctx.Cards.PokemonIds.IndexOf(key), 1, (int)DeckId.Legendary));
                if (starter >= 0) PokemonRules.AddToParty(two, starter);
            });
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != 1; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.BoardOpen && f.Hud != null && f.Client.View.ActiveSeat == 1);
            if (!flow.AnswerSeat(1, a => a.Kind == AnswerKind.Challenge && a.Target != 0)) throw new InvalidOperationException("좌석 2의 조무래기 도전이 거절됐다");
            BattleScreen screen = null;
            for (int i = 0; i < 1500; i++)
            {
                Pass(flow, 100);
                screen = flow.Stage.Current as BattleScreen;
                if (screen != null && (result ? screen.ShowsResult
                    : screen.InfoSeat >= 0 && screen.ShownMon(flow.Battle.MySide).Exists && screen.ShownMon(1 - flow.Battle.MySide).Exists && flow.Fx.Sequencer.IsEmpty && flow.Client.View.Battle != null)) break;
                if (flow.Fx != null && flow.Fx.Sequencer.InputOpen && !flow.AnswerSeat(1, a => a.Kind == AnswerKind.UseMove && a.Arg < 0) && !flow.AnswerSeat(1, a => a.Kind == AnswerKind.Decline)) flow.AnswerSeat(1);
            }
            if (screen == null || (result && !screen.ShowsResult))
                throw new InvalidOperationException("좌석 2의 배틀이 " + (result ? "결과 창까지 가지 않았다" : "열리지 않았다") + " — 화면 " + (flow.Stage.Current?.Name ?? "-") + " · 배틀 " + (flow.Battle == null ? "-" : "끝 " + flow.Battle.Ended + " · 끝 보임 " + flow.BattleOver)
                    + " · 대기열 " + (flow.Fx == null ? "-" : flow.Fx.Sequencer.IsEmpty + " 입력 " + flow.Fx.Sequencer.InputOpen) + " · 보기 배틀 " + (flow.Client.View.Battle != null) + " · 선택 " + string.Join(",", flow.Client.View.Pending.ConvertAll(p => p.Seat + ":" + p.Kind)));
            if (flow.Battle.Mine) throw new InvalidOperationException("내가 당사자인 배틀이다");
            // 가짜 방은 그 순간에 얹는다(이름 = 방 닉네임 — 판 처음부터 얹으면 확인용 판의 배틀 끝 대기열이 서지 않았다)
            flow.Room.ShowGame(FakeGame(flow, false));
            flow.Hud?.Refresh();
            screen.Refresh();
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (screen.InfoSeat != 1) throw new InvalidOperationException("기둥 내 쪽이 싸우는 좌석(2)이 아니다: " + (screen.InfoSeat + 1));
            var block = flow.Stage.Root.Q("battle-info-me");
            if (!block.Query<Label>().ToList().Exists(l => l.text == flow.SeatName(1))) throw new InvalidOperationException("기둥 내 쪽에 「" + flow.SeatName(1) + "」이 없다");
            if (block.Query<Label>().ToList().Exists(l => l.text == flow.SeatName(0))) throw new InvalidOperationException("기둥 내 쪽에 내 이름이 섞였다");
            if (FaceBox.Shown(flow.Stage.Root.Q("battle-me-face")) != flow.Protagonist(1)) throw new InvalidOperationException("기둥 얼굴이 좌석 2의 주인공이 아니다");
            if (flow.Stage.Root.Q(BattleScreen.FateButton) != null || screen.FateOn) throw new InvalidOperationException("남의 배틀 기둥에 내 [운명의 변화]가 있다");
            if (!result)
            {
                string turnTitle = flow.Stage.Root.Q<Label>("battle-turn-title")?.text;
                if (turnTitle == flow.Stage.T("screen.battle.turn.foe")) throw new InvalidOperationException("좌석 2가 고르는 중인데 독이 「상대의 차례」다");
                return;
            }
            string title = flow.Stage.Root.Q<Label>("battle-result-title").text.Replace("⁠", "");   // Kit.Wrapped의 낱말 잇기 글자를 뺀다
            string want = Sinnoh.Fx.KoJosa.Fix(flow.Stage.T(flow.Battle.Drawn ? "screen.battle.result.draw" : flow.Battle.Won ? "screen.battle.result.win.seat" : "screen.battle.result.lose.seat").Replace("{name}", flow.SeatName(1)));
            if (title != want) throw new InvalidOperationException("결과 창 머리: 「" + title + "」 ≠ 「" + want + "」");
        }

        // 좌석 둘 판(결정 114): 내 턴을 넘기고 좌석 2가 찾아보기 → 야생 배틀(필요하면 한 칸 걸어서) — 방 판처럼 남의 야생 배틀을 대기열에 넣고(WatchWildBattles) 배틀 안의 한 순간까지.
        // open = [보기]로 가운데 필드에 연 화면, 아니면 보드 그대로(표시 줄만)
        static void WildAside(UiShot s, bool open)
        {
            var flow = Seats(s, 2);
            flow.WatchWildBattles = true;
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != 1; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.BoardOpen && f.Hud != null && f.Client.View.ActiveSeat == 1);
            // 결정 120: 창이 없으면 남의 야생 배틀도 저절로 열린다 — 표시 줄만 뜨는 모습은 내 창(정비)을 열어 둔 동안
            if (!open && !flow.OpenParty()) throw new InvalidOperationException("정비 창이 열리지 않았다");
            bool inside = false;
            for (int i = 0; i < 2000 && !inside; i++)
            {
                Pass(flow, 100);
                if (flow.WildAside && flow.Fx.Sequencer.Current != null && flow.Fx.Sequencer.Current.Steps.Exists(st => st.Events.Exists(e => e is DiceRolled))
                    && (open ? flow.WatchingWild && flow.Stage.Current is BattleScreen : flow.Stage.Current is PartyScreen)) inside = true;
                if (inside || flow.Fx == null || !flow.Fx.Sequencer.InputOpen && !flow.WildAside) continue;
                if (flow.AnswerSeat(1, a => a.Kind == AnswerKind.Search)) continue;
                if (flow.AnswerSeat(1, a => a.Kind == AnswerKind.UseMove && a.Arg < 0)) continue;
                if (flow.Client.View.Battle == null && flow.AnswerSeat(1, a => a.Kind == AnswerKind.Move)) continue;
                if (!flow.AnswerSeat(1, a => a.Kind == AnswerKind.Decline)) flow.AnswerSeat(1);
            }
            if (!inside) throw new InvalidOperationException("좌석 2의 야생 배틀 안까지 가지 않았다 — 화면 " + (flow.Stage.Current?.Name ?? "-") + " · 배틀 " + (flow.Battle == null ? "-" : flow.Battle.Kind + " 내 것 " + flow.Battle.Mine));
            if (!open && flow.Stage.Current is BattleScreen) throw new InvalidOperationException("창을 열어 둔 동안 남의 야생 배틀이 가운데 필드를 바꿨다");
            flow.Room.ShowGame(FakeGame(flow, false));   // 가짜 방은 그 순간에 얹는다(Spectate와 같다 — 이름 = 방 닉네임 · 방 판 덮개)
            Pass(flow, 0);
            if (open && !flow.WatchingWild) throw new InvalidOperationException("창이 없는데 남의 야생 배틀이 저절로 열리지 않았다(결정 120)");
            Pass(flow, 0);
            flow.Hud?.Refresh();
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            string text = flow.RoomLayer.WildText.Replace("⁠", "");
            if (!text.StartsWith(Strings()[open ? "screen.room.wild.watching" : "screen.room.wild.on"].Replace("{name}", flow.SeatName(1)), StringComparison.Ordinal)) throw new InvalidOperationException("표시 줄: " + text);
            if (open)
            {
                var screen = flow.Stage.Current as BattleScreen ?? throw new InvalidOperationException("배틀 화면이 아니다");
                screen.Refresh();
                s.Panel.Tick(3);
                if (screen.InfoSeat != 1) throw new InvalidOperationException("기둥 내 쪽이 싸우는 좌석(2)이 아니다: " + (screen.InfoSeat + 1));
                if (flow.Stage.Root.Q("room-wild-back") == null) throw new InvalidOperationException("[보드로]가 없다");
            }
            else
            {
                if (!(flow.Stage.Current is PartyScreen)) throw new InvalidOperationException("정비 창이 아니다: " + (flow.Stage.Current?.Name ?? "보드"));
                if (flow.Stage.Root.Q<Button>("room-wild-open") == null || !flow.Stage.Root.Q<Button>("room-wild-open").enabledSelf) throw new InvalidOperationException("[보기]가 없거나 꺼져 있다");
            }
            RoomFit(s, flow, "room-layer-col");
        }

        // ── 소통 장면의 부품 ──

        // 화면 안 · HUD 판 밖의 보드 칸 n개(서로 떨어진 것 — 720 토큰 px)
        static List<int> SocialCells(GameFlow flow, int n)
        {
            var open = new List<int>();
            for (int cell = 0; cell < flow.Ctx.Board.Count && open.Count < n; cell++)
            {
                if (!flow.CellPoint(cell, out var at) || at.x < 380 || at.x > 1150 || at.y < 170 || at.y > 520) continue;
                if (open.Exists(c => flow.CellPoint(c, out var o) && UnityEngine.Vector2.Distance(o, at) < 190)) continue;
                open.Add(cell);
            }
            if (open.Count < n) throw new InvalidOperationException("핑을 찍을 칸 " + n + "개를 찾지 못했다");
            return open;
        }

        // 좌석 셋 판: 내 턴을 넘기고 남 좌석 둘이 한 칸씩 걸은 뒤(기록에 남의 행동 줄이 선다) 가짜 방을 얹는다
        static GameFlow SocialActed(UiShot s)
        {
            var flow = Seats(s, 3);
            int me = flow.Client.Seat;
            for (int turn = 0; turn < 3; turn++)
            {
                int active = flow.Client.View.ActiveSeat;
                if (active != me) flow.AnswerSeat(active, a => a.Kind == Sinnoh.Rules.AnswerKind.Move);
                Pass(flow, BoardReadyMs * 8);
                for (int k = 0; k < 8 && flow.Client.View.ActiveSeat == active; k++)
                {
                    if (active == me) flow.Hud.Press(BoardHud.EndTurn);
                    else if (!flow.AnswerSeat(active, a => a.Kind == Sinnoh.Rules.AnswerKind.EndTurn)) flow.AnswerSeat(active);
                    Pass(flow, BoardReadyMs * 8);
                }
            }
            DealSettle(s, flow, f => f.Hud != null && f.Fx.Sequencer.IsEmpty);
            flow.Room.ShowGame(FakeGame(flow, false));
            return flow;
        }

        static void SocialSettle(UiShot s, GameFlow flow)
        {
            for (int i = 0; i < 2; i++)
            {
                Pass(flow, 0);
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
            }
        }

        static string SocialText(GameFlow flow, string name)
        {
            var e = flow.Stage.Root.Q(name) ?? throw new InvalidOperationException("요소 없음: " + name);
            return ((e as TextElement)?.text ?? e.Q<TextElement>()?.text ?? "").Replace("⁠", "");
        }

        // i번째 핑 표시가 그 요소의 사각형 안에 있나
        static void SocialIn(GameFlow flow, int i, string name)
        {
            var e = flow.Stage.Root.Q(name) ?? throw new InvalidOperationException("요소 없음: " + name);
            var at = flow.RoomLayer.PingPoint(i);
            if (!e.worldBound.Contains(at)) throw new InvalidOperationException("핑 " + i + "이 " + name + " 안에 있지 않다: " + at + " / " + e.worldBound);
        }

        // 좌석마다 감정 표현 말풍선 하나(lines[좌석] = 문구 번호): 좌석 칩 아래 · 화면 안 · 글 그대로 · 서로 겹치지 않는다
        static void SocialBubbles(UiShot s, int seats, int[] lines)
        {
            var flow = Seats(s, seats);
            flow.Room.ShowGame(FakeGame(flow, false));
            var live = flow.Room.Live;
            for (int seat = 0; seat < lines.Length; seat++) live.AddEmote(seat, lines[seat], RoomNow - 400);
            SocialSettle(s, flow);
            SocialSettle(s, flow);
            var root = flow.Stage.Root.worldBound;
            var boxes = new List<UnityEngine.Rect>();
            for (int seat = 0; seat < lines.Length; seat++)
            {
                string want = flow.Stage.T("screen.room.emote.line." + lines[seat]);
                string got = (flow.RoomLayer.EmoteShown(seat) ?? "").Replace("⁠", "");
                if (got != want) throw new InvalidOperationException("좌석 " + (seat + 1) + " 말풍선: 「" + got + "」 ≠ 「" + want + "」");
                var box = flow.Stage.Root.Q("room-emote-" + seat).worldBound;
                var chip = flow.Stage.Root.Q("board-hud-seat-" + (seat + 1)).worldBound;
                if (box.yMin < chip.yMax) throw new InvalidOperationException("말풍선이 좌석 칩을 덮는다: 좌석 " + (seat + 1));
                if (box.xMin < root.xMin || box.xMax > root.xMax) throw new InvalidOperationException("말풍선이 화면 밖이다: 좌석 " + (seat + 1) + " " + box);
                var label = flow.Stage.Root.Q<Label>("room-emote-" + seat + "-text");
                if (label.worldBound.height > 1.6f * label.resolvedStyle.fontSize + 8f) throw new InvalidOperationException("말풍선 글이 줄바꿈됐다: 좌석 " + (seat + 1));
                if (boxes.Exists(o => o.Overlaps(box))) throw new InvalidOperationException("말풍선이 서로 겹친다: 좌석 " + (seat + 1));
                boxes.Add(box);
            }
        }

        // 그 좌석의 시계를 흐르게: 남은 기본 시간 · 뱅크, 받은 뒤 지난 ms
        static void RunClock(RoomLive l, int seat, ClockKind kind, long leftMs, long bankMs, long elapsed)
        {
            var c = l.Clock.Seats[seat];
            c.Running = kind;
            c.ChoiceId = 100 + seat;
            c.LeftMs = leftMs;
            c.BankMs = bankMs;
            l.ClockAtMs = RoomNow - elapsed;
        }

        static void Vote(RoomLive l, VoteKind kind, int target)
        {
            var v = new RoomVote { Id = 1, Kind = kind, Target = target, By = 1, LeftMs = 42000 };
            v.Yes.Add(1);
            for (int i = 0; i < l.Room.Seats.Count; i++)
                if (i != target && i != 1) v.Waiting.Add(i);
            l.Room.Vote = v;
            l.Room.Seats[target].Connected = true;
        }

        // 혼자 하는 확인용 판(좌석 셋) + 가짜 방의 게임 화면 몫
        static GameFlow RoomGame(UiShot s, Action<RoomLive> act, bool host = true)
        {
            var flow = Seats(s, 3);
            var live = FakeGame(flow, host);
            act?.Invoke(live);
            flow.Room.ShowGame(live);
            flow.Stage.Bind(flow.Stage.Root, flow.Stage.Grade);   // HUD를 방 판 모양으로 다시 세운다(좌석 칩 · 기록의 이름 = 방 닉네임)
            Pass(flow, 0);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (flow.RoomLayer == null || flow.RoomLayer.Element == null || flow.RoomLayer.Element.parent == null) throw new InvalidOperationException("방 판 덮개 층이 붙지 않았다");
            if (flow.RoomLayer.CoverUp) RoomFit(s, flow, "room-reconnect");
            else RoomFit(s, flow, "room-layer-col");
            return flow;
        }

        // 응답 시계 줄이 위 띠 안(상태 판의 턴 배지 앞 — 결정 112 Q5)에 그 글로 있다
        static void BandClock(GameFlow flow, string text)
        {
            if (!flow.RoomLayer.ClockInBand) throw new InvalidOperationException("응답 시계가 위 띠 안이 아니다");
            var strip = flow.Stage.Root.Q("room-clock");
            var label = strip.Q<Label>(className: "sn-room-strip-text");
            if (label == null || label.text.Replace("⁠", "") != text) throw new InvalidOperationException("위 띠 시계 글: " + label?.text);
            var turn = flow.Stage.Root.Q("board-hud-turn").worldBound;
            var status = flow.Stage.Root.Q("board-hud-status").worldBound;
            if (strip.worldBound.xMax > turn.xMin + 0.5f || strip.worldBound.xMin < status.xMin || strip.worldBound.yMin < status.yMin - 0.5f || strip.worldBound.yMax > status.yMax + 0.5f) throw new InvalidOperationException("시계가 상태 판 안 턴 배지 앞이 아니다: " + strip.worldBound + " / " + turn);
            var bad = WorkUi.Overflows(strip);
            if (bad.Count > 0) throw new InvalidOperationException("위 띠 시계 글 넘침: " + string.Join(" / ", bad));
        }

        static void Reconnect(UiShot s, Action<RoomLive> act)
        {
            var flow = RoomGame(s, act, host: false);
            if (!flow.RoomLayer.CoverUp) throw new InvalidOperationException("재접속 덮개가 뜨지 않았다");
            var live = flow.Room.Live;
            if (live.CanRetry != (flow.Stage.Root.Q("room-reconnect-retry") != null)) throw new InvalidOperationException("[다시 시도]가 쓸 수 있는 실패와 맞지 않다");
        }

        static void RoomMenu(UiShot s, bool host, int page, bool ask)
        {
            var flow = RoomGame(s, l => RunClock(l, 0, ClockKind.MapTurn, 83000, 120000, 0), host);
            flow.OpenMenu();
            var menu = flow.Stage.Current as BoardMenuScreen ?? throw new InvalidOperationException("게임 메뉴가 열리지 않았다");
            if (page != BoardMenuScreen.MainPage) menu.Show(page);
            if (ask) menu.AskQuit();
            Pass(flow, 0);
            RoomFit(s, flow, "board-menu-panel");
        }

        static void RoomWait(UiShot s, bool host)
        {
            var flow = s.Flow();
            flow.OpenTitle();
            var lobby = new RoomLive(() => RoomNow)
            {
                IsHost = host, Seat = host ? 0 : 1, Port = host ? 7777 : 0,
                Addresses = host ? new[] { new KeyValuePair<string, string>("100.101.102.103", "Tailscale") } : new KeyValuePair<string, string>[0],
                Room = new RoomState
                {
                    Phase = RoomPhase.Waiting, RoomId = "room-shot", Settings = new RoomSettings { Seats = 4, Clock = ClockPreset.Normal },
                    Seats = new List<RoomSeat>
                    {
                        new RoomSeat { Nickname = "방장트레이너", Host = true, Connected = true },
                        new RoomSeat { Nickname = "빛나", Connected = true },
                        new RoomSeat { Nickname = "열두글자닉네임입니다요요", Platform = Platform.Pc },
                        new RoomSeat { Nickname = "Barry", Kicked = true },
                    },
                },
            };
            if (host) lobby.Room.Claims.Add(new RoomClaim { Id = 1, Nickname = "열두글자닉네임입니다요요", Platform = Platform.Pc, Suggested = 2 });
            flow.Room.ShowLobby(lobby);
            var screen = flow.Stage.Current as LobbyScreen ?? throw new InvalidOperationException("대기실이 열리지 않았다");
            if (!screen.Waiting) throw new InvalidOperationException("이어하기 대기실 모양이 아니다");
            if (host && (flow.Stage.Root.Q("room-wait-resume") == null || flow.Stage.Root.Q("room-wait-claim-0-assign") == null)) throw new InvalidOperationException("[지금 재개] · 자리 확인 줄이 없다");
            RoomFit(s, flow, "lobby-col");
            RoomFit(s, flow, "lobby-set");
            Apart(flow.Stage.Root, "lobby-col", "lobby-set");
        }

        sealed class NoLink : IRoomLink
        {
            public void Send(int conn, ServerMsg msg) { }
            public void Close(int conn, string code) { }
        }

        // 임시 저장 폴더(…/saves) 옆 room-saves에 3인 방 저장 하나(셋업 — 방을 열고 손님 둘이 준비해 시작한 뒤 닫았다)를 쓴 흐름
        static GameFlow RoomSaveFlow(UiShot s, out string id)
        {
            string root = Path.Combine(Path.GetTempPath(), "sinnoh-uishots-room-" + Guid.NewGuid().ToString("N"), "saves");
            Directory.CreateDirectory(root);
            var flow = s.Flow(root, () => SaveClock);
            string rooms = RoomFlow.RoomSaves(root);
            id = "room-20261118-090000";
            const string build = "shot";
            var cfg = new HostConfig
            {
                OwnerToken = "own", OwnerNickname = "방장트레이너", RoomId = id, BuildId = build, ContentHash = flow.ContentHash ?? "", Seed = Seed, AiSeed = AiSeed,
                VirtualAgent = new PrincipleAgent(), Settings = new RoomSettings { Seats = 3, Clock = ClockPreset.Off }, SavedAt = () => SaveClock,
                Store = sid => new SaveStore(Path.Combine(rooms, sid)),
            };
            var host = new HostSession(flow.Ctx, new NoLink(), cfg, 0);
            Hello Hi(string token, string nick) => new Hello { BuildId = build, ContentHash = flow.ContentHash ?? "", Token = token, Nickname = nick, Platform = Platform.Pc };
            if (!host.Approve(0, Hi("own", "방장트레이너"), 0).Ok || !host.Approve(1, Hi("g1", "빛나"), 0).Ok || !host.Approve(2, Hi("g2", "용식"), 0).Ok) throw new InvalidOperationException("방 승인 실패");
            host.OnMessage(1, new SetReady { Ready = true }, 0);
            host.OnMessage(2, new SetReady { Ready = true }, 0);
            host.OnMessage(0, new StartGame(), 0);
            if (host.Phase != RoomPhase.InGame) throw new InvalidOperationException("방 게임이 시작되지 않았다");
            host.Close(1000);
            flow.OpenTitle();
            return flow;
        }
    }
}
