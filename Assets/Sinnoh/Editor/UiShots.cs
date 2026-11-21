using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Rekkon.UiKit;
using R = Sinnoh.Rules;
using Sinnoh.Ai;
using Sinnoh.Client;

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Sinnoh.Editor
{
    // UI 캡처(M4-3 「PC 층 기반」, 04 §9-6, 결정 39 ③): PC 층(UI Toolkit) 화면을 창 없이 RenderTexture에 그려 PNG로 쓴다. 플레이 없이 편집 모드에서 돈다.
    //   메뉴 Sinnoh/Capture/Ui(모든 화면 × 등급 넷, 두 번 찍어 견줌),
    //   배치: Unity.exe -batchmode -projectPath . -executeMethod Sinnoh.Editor.UiShots.Run -logFile <log> [-uiScreen <이름[,이름…]|all>] [-uiGrade <720|1080|1440|2160|all>] [-uiSize <폭>x<높이>[,…]] [-uiOut <폴더>] [-uiRepeat] [-uiLang <ko|en>] [-uiOverflow]
    //   -uiLang: 화면 글의 언어(M9 언어 ② — 기본 ko). -uiOverflow: 찍은 화면마다 WorkUi.Overflows 줄을 <폴더>/overflows.txt에 모은다(실패로 치지 않는다 — 언어끼리 견주기).
    //   -uiSize: 등급의 기준 크기가 아닌 창 크기로 찍는다(등급은 그 창이 고르는 것 — UiGrade.For, 그림 이름 <화면>-<폭>x<높이>.png). 주면 -uiGrade의 기본은 「없음」.
    //   (-nographics 없이 — 그래픽 장치가 있어야 그린다. 배치는 끝나면 종료 코드 0/1로 나간다)
    // 출력 Logs/UiShots/(git 밖, Temp/는 배치가 끝나면 지워진다 — -uiOut으로 바꾼다): <화면>-<등급>.png, ui-shots.json(그림마다 크기·색 수·두 번 찍기 다른 픽셀·걸린 시간).
    //   -uiRepeat: 같은 장면을 처음부터(겉모양·패널을 새로 만들어) 한 번 더 찍어 픽셀을 견준다. 다르면 <화면>-<등급>-repeat.png를 남기고 실패.
    // 판정(로그 [UiShots] 줄): 그림이 비지 않음(색이 둘 이상), 「Layout update failed to stabilize」 로그 0, -uiRepeat면 다른 픽셀 0.
    // 화면을 더하려면 Screens에 이름과 만드는 함수를 한 줄 넣는다(화면 뿌리에 요소를 붙이고, 올린 상태 같은 것은 Tick 뒤에 만든다).
    //   게임 화면은 UiShot.Flow()(실행 중과 같은 GameFlow)로 연다. 보드 위 화면이면 OverBoard에도 이름을 넣는다 — 보드는 그 흐름이 놓은 말·카메라대로 그려진다.
    public static partial class UiShots
    {
        public const string OutDir = "Logs/UiShots";

        /// <summary>등급 넷(04 §9-3): 화면 높이 720 · 1080 · 1440(720 토큰 × 2) · 2160(1080 토큰 × 2).</summary>
        public static readonly UiGrade[] Grades = { new UiGrade(720, 1), new UiGrade(1080, 1), new UiGrade(720, 2), new UiGrade(1080, 2) };

        /// <summary>
        /// 찍을 수 있는 화면. 키트 갤러리는 올리지 않은 모습과 툴팁 자리 셋(아래 · 위에 자리가 없어 아래로 넘김 · 위).
        /// 게임 화면은 실행 중과 같은 길(<see cref="GameFlow"/>)로 연다 — 고정 시드로 실제 게임을 시작해 엔진이 연 선택지가 그대로 찍힌다.
        /// </summary>
        public static readonly KeyValuePair<string, Action<UiShot>>[] Screens =
        {
            Screen("kit-gallery", Gallery(null)),
            Screen("kit-gallery-tip-end", Gallery("gallery-btn-end")),
            Screen("kit-gallery-tip-speed", Gallery("gallery-pill-speed")),
            Screen("kit-gallery-tip-heart", Gallery("gallery-pill-heart-low")),
            Screen("title", s => s.Flow().OpenTitle(TitleScreen.Solo)),
            Screen("title-web", s =>                                          // 웹 손님판 타이틀(결정 112 Q18): 참가하기 · 튜토리얼 · 도감 · 설정
            {
                var flow = s.Flow();
                flow.OpenTitle();
                flow.Stage.Open(new TitleScreen(web: true, pick: TitleScreen.Join, tutorial: () => { }, join: () => { }, settings: () => { }, dex: () => { }));
                if (flow.Stage.Root.Q("title-item-tutorial") == null || flow.Stage.Root.Q("title-item-solo") != null) throw new System.InvalidOperationException("웹 타이틀 줄이 맞지 않다");
            }),
            Screen("title-host", s => s.Flow().OpenTitle(TitleScreen.Host)),   // 목업 r3 title-1080과 같은 상태(「방 만들기」를 고름)
            Screen("title-update", s =>                                       // 새 판 알림(결정 148 ①): 아래 띠 위 오른쪽 「새 버전 x.y.z」 + [받기], 아래 띠 오른쪽 끝 판 번호
            {
                var flow = s.Flow();
                flow.OpenUrl = url => throw new System.InvalidOperationException("캡처가 주소를 열었다: " + url);
                flow.OpenTitle(TitleScreen.Solo);
                flow.ShowRelease("0.2.0");
                var root = flow.Stage.Root;
                if (!((TitleScreen)flow.Stage.Current).UpdateShown || root.Q<Label>("title-update-text")?.text.Contains("0.2.0") != true) throw new System.InvalidOperationException("새 판 알림이 없다");
                if (root.Q<Label>("title-version")?.text != TitleScreen.VersionText) throw new System.InvalidOperationException("아래 띠에 판 번호가 없다");
                RoomFit(s, flow, "title-update");
                RoomFit(s, flow, "title-foot");
                foreach (var other in new[] { "title-menu", "title-desc", "title-foot", "title-logo" }) Apart(root, "title-update", other);
            }),
            // 자동 업데이트(결정 161 4-3): [업데이트] 알림 · 받는 중(메뉴를 막는 덮개 + 막대 + MB + [취소]) · 실패(까닭 한 줄 + [다시 시도] · [받기]) — 서버 없이 상자만(GameFlow.FakeUpdate)
            Screen("title-update-auto", s => UpdateBox(s, UpdateStep.Idle, "title-update-do")),
            Screen("title-update-progress", s => UpdateBox(s, UpdateStep.Fetch, "title-update-cancel")),
            Screen("title-update-fail", s => UpdateBox(s, UpdateStep.Failed, "title-update-retry")),
            // ── 저장 화면(04 §9-12): 임시 폴더에 저장을 만들어 찍고 지운다(사용자의 저장 폴더에 쓰지 않는다) ──
            Screen("title-continue", s => WithSaves(s, flow => flow.OpenTitle())),   // 저장이 있는 타이틀: 「이어하기」가 켜지고 가장 최근 판의 요약이 적힌다
            Screen("save-list", s => WithSaves(s, flow => flow.OpenSaves())),        // S04: 판 둘 + 읽을 수 없는 저장 하나
            Screen("save-delete-ask", s => WithSaves(s, flow =>                      // [삭제]를 누른 칸: 한 번 더 묻는다
            {
                flow.OpenSaves();
                var list = (SaveListScreen)flow.Stage.Current;
                list.AskDelete(list.Entries[0].SaveId);
            })),
            Screen("save-list-empty", s => WithSaves(s, flow => flow.OpenSaves(), games: 0)),   // 다 지운 뒤의 목록
            Screen("solo", s => s.Flow().OpenSolo()),
            Screen("solo-overwrite", s => WithSaves(s, flow => flow.OpenSolo(), games: 2)),   // 자동 저장된 판이 있을 때의 솔로 설정: 「새 게임을 시작하면 자동 저장된 게임을 덮어씁니다.」(결정 70 ③)
            Screen("solo-tip", s =>                                             // 아직 고를 수 없는 값에 마우스를 올린 모습
            {
                var flow = s.Flow();
                flow.OpenSolo();
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("solo-opt-" + SoloScreen.Rules + "-custom"));   // 세부 규칙 「직접 설정」 — 강한 AI는 M8 「연결」(m8-solo-strong), 울트라 버스트는 M6 「UB 시작」부터 고를 수 있다
            }),
            Screen("settings", s => s.Flow().OpenSettings()),                // S02(결정 59): 모니터 3840×2160에서 창이 찍는 크기일 때 — 그 크기가 목록에 있으면 골라져 있다
            Screen("settings-fx", s =>                                          // 「연출」 줄에서 「끄기」를 고르고 마우스를 올린 모습(설명 툴팁)
            {
                var flow = s.Flow();
                flow.OpenSettings();
                ((SettingsScreen)flow.Stage.Current).PickFx(Sinnoh.Fx.FxSpeed.Off);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("settings-opt-fx-off"));
            }),
            Screen("solo-protagonist", s =>                                     // 「주인공」 줄에서 다른 주인공(BW 여자)을 고르고 마우스를 올린 모습 — 툴팁은 그 주인공의 이름뿐(결정 70 ⑤)
            {
                var flow = s.Flow();
                flow.OpenSolo();
                ((SoloScreen)flow.Stage.Current).Set(SoloScreen.Protagonist, "bw_001_hilda");
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("solo-opt-" + SoloScreen.Protagonist + "-bw_001_hilda"));
            }),
            Screen("settings-job", s =>                                         // 「말 그림」을 직업 그림으로 바꾼 모습 — 뒤의 보드 말이 직업 그림으로 바뀐다
            {
                var flow = s.Flow();
                flow.OpenSettings();
                ((SettingsScreen)flow.Stage.Current).PickPiece(PieceStyle.Job);
            }),
            Screen("settings-lang", s =>                                        // 「언어」 줄(M9 언어 ② — 맨 위)에서 다른 언어를 고른 모습 + 「English」에 마우스: 화면이 그 언어로 다시 그려진다
            {
                var flow = s.Flow();
                flow.OpenSettings();
                string other = flow.Lang == "en" ? "ko" : "en";
                ((SettingsScreen)flow.Stage.Current).PickLang(other);
                if (flow.Lang != other || flow.Stage.Root.Q("settings-head").Q<Label>().text != flow.Stage.T("screen.settings.title")) throw new InvalidOperationException("언어를 바꿔도 설정 화면이 다시 그려지지 않았다");
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("settings-opt-lang-en"));
            }),
            Screen("menu-settings-lang", s =>                                   // 게임 중(게임 메뉴의 설정)에 언어를 바꾼 모습: 뒤의 보드 HUD도 그 언어로 다시 그려진다
            {
                var flow = ToBoard(s, BoardReadyMs);
                flow.OpenMenu();
                flow.OpenMenuSettings();
                string other = flow.Lang == "en" ? "ko" : "en";
                ((SettingsScreen)flow.Stage.Current).PickLang(other);
                if (flow.Hud == null || flow.Stage.Root.Q("board-hud") == null) throw new InvalidOperationException("언어를 바꾼 뒤 보드 HUD가 없다");
            }),
            Screen("setup-village", s => Started(s)),
            Screen("setup-village-picked", s => PickVillage(Started(s))),
            Screen("setup-pick", s => ToPick(s)),
            // 셋업 연출(04 §8-8 1~3·5행, 결정 66 ③): 마을 [확정] 뒤 · 고르는 화면이 열린 뒤 · [확정] 뒤의 한 순간(연출 시각만 보낸다)
            Screen("setup-village-glide", s => VillageDrop(s, 320)),             // 카메라가 고른 마을로 다가가는 중(전체 줌 → 지역 줌), 마을 칸 맥박 · 좌석 띠(회색)
            Screen("setup-village-drop", s => VillageDrop(s, 1000)),             // 말이 위에서 떨어지는 중
            Screen("setup-village-landed", s => VillageDrop(s, 1300)),           // 내려앉았다: 좌석 띠가 마을 색으로
            Screen("setup-deal", s => ToPick(s, dealMs: 430)),                   // 트레이너 카드 3장이 덱 쪽에서 뒷면으로 들어오는 중(첫 장은 뒤집히는 중)
            Screen("setup-pick-locked", s => Picked(s, s2 => s2.Row == Sinnoh.Fx.SetupRows.Chosen, 600)),                                                  // [확정] 뒤: 고르지 않은 스타팅 포켓몬이 물러나고 고른 것이 볼로 들어가는 중
            Screen("setup-card-locked", s => Picked(s, s2 => s2.Row == Sinnoh.Fx.SetupRows.Chosen && Equals(s2.Tag, Sinnoh.Rules.ChoiceKind.TrainerCardPick), 1000)),   // 스타팅 포켓몬은 볼 · 고른 카드는 뒷면, 나머지 둘은 덱으로
            Screen("setup-starter-open", s => Picked(s, s2 => s2.Row == Sinnoh.Fx.SetupRows.Starter, 900)),                                                // 볼이 열리고 흰 빛 속에서 도트가 나타나는 중
            Screen("setup-starter-shown", s => Picked(s, s2 => s2.Row == Sinnoh.Fx.SetupRows.Starter, 1300)),                                               // 도트가 다 나타났다
            Screen("setup-card-reveal", s => Picked(s, s2 => s2.Row == Sinnoh.Fx.SetupRows.Reveal, 850)),                                                  // 카드가 앞면으로 뒤집히고 금속광이 지나가는 중
            Screen("board-spots-drop", s => Picked(s, s2 => Sinnoh.Fx.SpotDropBeats.Spots(s2) != null, 620)),                                              // 게임 시작: 전체 줌 · 숨겨진 장소 토큰이 칸 색인 순서로 떨어지는 중
            Screen("setup-pick-picked", s =>                                    // 둘 다 고르고 고유 기술에 마우스를 올린 모습(능력 문구는 칸에 바로 적혀 있다)
            {
                var flow = ToPick(s);
                var pick = (SetupPickScreen)flow.Stage.Current;
                pick.PickStarter(0);
                pick.PickCard(1);
                s.Panel.Tick(3);
                string starter = flow.Ctx.Cards.Pokemon[pick.Starters[0]].Key.Replace(':', '-');
                flow.Stage.Tips.Show(flow.Stage.Root.Q("setup-pick-starter-" + starter + "-move"));
            }),
            Screen("setup-pick-long", s =>                                      // 능력 문구가 가장 긴 카드(다이아 무녀 — 238자)가 든 판: 칸 안에 다 들어가는지
            {
                var flow = s.Flow();
                flow.StartSolo(new Sinnoh.Rules.Session.SoloSettings(), LongCardSeed(flow.Ctx), AiSeed);
                PickVillage(flow);
                if (!((SetupVillageScreen)flow.Stage.Current).Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
                flow.Stage.Tweens.Advance(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
                var pick = (SetupPickScreen)flow.Stage.Current;
                pick.PickStarter(0);
                for (int i = 0; i < pick.Cards.Count; i++) if (flow.Ctx.Cards.TrainerCards[pick.Cards[i]].Key == LongCard) pick.PickCard(i);
            }),
            Screen("game-board", s => ToBoard(s, 0)),                           // 셋업을 막 끝낸 보드(HUD가 뜨고 내 턴 컷신이 시작되기 전 — 입력은 아직 닫혀 있다)
            Screen("board-hud-myturn", s => ToBoard(s, 250)),                   // 게임 첫 턴: 내 턴 컷신(한 좌석 판 500, 결정 57 ③) 한가운데
            Screen("board-hud", s => ToBoard(s, BoardReadyMs)),                 // 내 턴: 갈 수 있는 칸 · 행동 단추
            Screen("board-hud-job", s => { s.Flow().PieceStyle = PieceStyle.Job; ToBoard(s, BoardReadyMs, flow: s.Made); }),   // 말 그림 = 직업 그림(04 §3-1 양식 ②)
            Screen("board-hud-walk-job", s =>                                   // 직업 그림 말은 걷기 그림이 없어 통통 튄다
            {
                s.Flow().PieceStyle = PieceStyle.Job;
                var flow = ToBoard(s, BoardReadyMs, flow: s.Made);
                if (!flow.Hud.Walk(TwoSteps(flow))) throw new InvalidOperationException("이동이 거절됐다");
                flow.Stage.Tweens.Advance(555);
            }),
            Screen("board-hud-nocards", s =>                                    // 트레이너 카드를 끈 판: 말 그림이 직업 그림이어도 주인공 도트(고른 주인공 = B2W2 여자)
            {
                s.Flow().PieceStyle = PieceStyle.Job;
                ToBoard(s, BoardReadyMs, new Sinnoh.Rules.Session.SoloSettings { TrainerCards = false, Protagonist = "b2w2_001_rosa" }, s.Made);
            }),
            Screen("board-hud-hover", s =>                                      // 두 걸음 떨어진 칸에 마우스를 올린 모습(경로 · 고른 칸)
            {
                var flow = ToBoard(s, BoardReadyMs);
                flow.Hud.Hover(TwoSteps(flow));
            }),
            Screen("board-hud-picked", s =>                                     // 걷기 전 확인(결정 70 ⑱): 두 걸음 떨어진 칸을 한 번 누른 모습 — 길이 칠해지고 고른 칸은 금색 테두리, 아래 판에 그 칸의 정보 줄 · 「이동력 2 사용 · 1 남음」 · [이동]
            {
                var flow = ToBoard(s, BoardReadyMs);
                if (!flow.Hud.PickCell(TwoSteps(flow))) throw new InvalidOperationException("칸 고르기가 거절됐다");
            }),
            Screen("board-hud-picked-tip", s =>                                 // 같은 모습에서 [이동] 단추에 마우스를 올린 모습: 툴팁(아래 판 밖 위)
            {
                var flow = ToBoard(s, BoardReadyMs);
                if (!flow.Hud.PickCell(TwoSteps(flow))) throw new InvalidOperationException("칸 고르기가 거절됐다");
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q(BoardHud.MoveGo));
            }),
            Screen("board-hud-picked-cancel-tip", s =>                          // 결정 73 ③: 같은 모습에서 [취소] 단추에 마우스를 올린 모습(모두 취소 · 오른쪽 클릭·Esc는 하나씩) — 「행동」 머리 줄 끝에 오른쪽 클릭 안내
            {
                var flow = ToBoard(s, BoardReadyMs);
                if (!flow.Hud.PickCell(TwoSteps(flow))) throw new InvalidOperationException("칸 고르기가 거절됐다");
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q(BoardHud.MoveCancel));
            }),
            Screen("board-hud-tip", s =>                                        // 행동 단추([마을])에 마우스를 올린 모습 — 툴팁은 아래 판 밖(위)
            {
                var flow = ToBoard(s, BoardReadyMs);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("board-hud-act-" + BoardHud.Village));
            }),
            Screen("board-hud-walk", s =>                                       // 걷는 중: 둘째 걸음 한가운데(예비 150 + 270 + 135) — 카메라가 말을 따라간다(결정 57 ①)
            {
                var flow = ToBoard(s, BoardReadyMs);
                if (!flow.Hud.Walk(TwoSteps(flow))) throw new InvalidOperationException("이동이 거절됐다");
                flow.Stage.Tweens.Advance(555);
            }),
            Screen("board-hud-walked", s =>                                     // 걸은 뒤: 말이 화면 가운데, 남은 이동력 1, 다시 갈 수 있는 칸
            {
                var flow = ToBoard(s, BoardReadyMs);
                if (!flow.Hud.Walk(TwoSteps(flow))) throw new InvalidOperationException("이동이 거절됐다");
                flow.Stage.Tweens.Advance(BoardReadyMs);
            }),
            // 이어 받은 보드(plan §4 「저장 기반·자동 저장」): 두 걸음 걸은 판을 임시 폴더에 저장하며 하다가 타이틀로 나가 이어 받는다 — board-hud-walked와 같은 그림이어야 한다
            Screen("board-resumed", s =>
            {
                string root = Path.Combine(Path.GetTempPath(), "sinnoh-uishots-save-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var flow = ToBoard(s, BoardReadyMs, flow: s.Flow(root, () => SaveClock));
                    if (!flow.Hud.Walk(TwoSteps(flow))) throw new InvalidOperationException("이동이 거절됐다");
                    flow.Stage.Tweens.Advance(BoardReadyMs);
                    string id = flow.SaveId ?? throw new InvalidOperationException("저장하지 않는 판이다");
                    flow.OpenTitle();
                    if (!flow.Resume(id)) throw new InvalidOperationException("이어 받지 못했다: " + flow.ResumeError);
                    flow.Layout(s.Width, s.Height);
                    flow.Stage.Tweens.Advance(BoardReadyMs);
                }
                finally
                {
                    if (Directory.Exists(root)) Directory.Delete(root, true);   // 찍는 동안 더 쓰지 않는다
                }
            }),
            // 턴 종료(한 좌석 판 · 주운 것 없음 — 결정 57 ③): 요약 없이 넘김 띠 0~500 → 내 턴 컷신 500~1000 → 클릭 막기 300 뒤 다음 라운드의 내 턴.
            // 요약 판은 지금 솔로 흐름에서 나오지 않는다(주울 수 있는 길 = 찾아보기가 아직 꺼져 있다) — 모습은 FxShots turn-band-gain·turn-band-2p가 찍는다
            Screen("board-hud-band", s => EndTurn(s, 250)),                     // → 넘김 띠(머묾 125~375의 한가운데)
            Screen("board-hud-cut", s => EndTurn(s, 750)),                      // → 내 턴 컷신(머묾)
            Screen("board-hud-round2", s => EndTurn(s, 1300)),                  // → 다음 라운드의 내 턴(1000 + 클릭 막기 300 — 입력이 열리는 바로 그때)
            Screen("board-hud-round3", s => AtRound(s, 2, false)),              // 3라운드: 위 띠의 시간대 배지 「밤」(1라운드 낮 = board-hud · 2라운드 저녁 = board-hud-round2 — 결정 70 ⑥)
            Screen("battle-time-evening", s => AtRound(s, 1, true)),            // 2라운드의 배틀: 저녁 무대(낮 = battle-select)
            Screen("battle-time-night", s => AtRound(s, 2, true)),              // 3라운드의 배틀: 밤 무대
            Screen("battle-order", s => { OrderAsk(s, -1); }),                     // 결정 72: 마비된 내 포켓몬 대 졸린 상대 — 굴린 뒤 면 바꾸기의 순서를 고르는 줄(순서 · 그 결과 굴림 · 데미지)
            Screen("battle-order-hover", s =>                                   // 순서 줄의 툴팁(줄 위)
            {
                var flow = OrderAsk(s, -1);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("battle-order-1"));
            }),
            Screen("battle-order-spare", s => { OrderAsk(s, 1); }),
            Screen("battle-token-ask", s => Trail(s, SoloTrail.Defeat, TokenAsk)),       // 결정 73 ⑦: 스피드 동률 — 우선권 토큰을 쓸지 묻는 동안 DS 내 상자 옆 토큰이 빛나고 기둥 내 줄에 토큰 그림
            Screen("battle-token-hover", s =>                                   // 기둥의 토큰 그림 툴팁: 이름표 「우선권 토큰」 + 짧은 설명
            {
                var flow = Trail(s, SoloTrail.Defeat, TokenAsk);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("battle-me-token"));
            }),                // 둘째 줄(마비 먼저 — 일부러 덜 준다)을 고른 뒤: 계산 줄의 데미지가 그 줄의 값
            Screen("board-hud-tip-end", s =>                                    // [턴 종료] 단추의 툴팁: 아래 판 밖(위)에 뜬다
            {
                var flow = ToBoard(s, BoardReadyMs);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("board-hud-act-" + BoardHud.EndTurn));
            }),
            // ── 보드 위에 늘 보이는 것(plan §4 같은 이름): 막 카드 S14 · 기록 S21 · 보드 위 토큰 · 공개 정보 카드 ──
            Screen("board-spots-placed", s => Opening(s, 0)),                    // 숨겨진 장소 토큰이 다 놓였다(전체 줌) — 서막 카드가 시작되기 직전
            Screen("act-card-drop", s => Opening(s, 560)),                      // 서막 카드가 위에서 떨어지며 가로로 펴지는 중(어둠은 다 내려왔다)
            Screen("act-card-prologue", s => Opening(s, 1600)),                 // 서막 카드: 제목 글자가 다 박히고 목표 문장이 떴다
            Screen("board-grunts-drop", s => Opening(s, 3600 + 480)),           // 서막 카드 뒤: 고향 마을에 조무래기 토큰이 떨어지는 중
            Screen("act-card-1", s => Opening(s, 3600 + 1800 + 1600)),          // 1막 카드: 목표 문장 + 보상 줄(빨강 규토리 +3), 위 띠 막 배지는 아직 서막
            Screen("board-log", Fit(s =>                                          // 기록 창(S21): 셋업 · 서막 · 1막 · 1라운드 · 두 칸 걸은 뒤
            {
                var flow = ToBoard(s, BoardReadyMs);
                if (!flow.Hud.Walk(TwoSteps(flow))) throw new InvalidOperationException("이동이 거절됐다");
                flow.Stage.Tweens.Advance(BoardReadyMs);
                flow.OpenLog();
            })),
            Screen("board-info-seat", s =>                                      // 좌석 칩에 마우스를 올린 모습: 공개 정보 카드(칩 아래)
            {
                var flow = ToBoard(s, BoardReadyMs);
                s.Panel.Tick(3);
                flow.Hud.ShowSeatInfo(0);
            }),
            Screen("board-info-grunt", s =>                                     // 고향 마을 칸(조무래기 토큰)에 마우스를 올린 모습: 토큰 정보 + 아래 판의 칸 정보 줄
            {
                var flow = ToBoard(s, BoardReadyMs);
                s.Panel.Tick(3);
                flow.Hud.Hover(flow.Client.View.Seats[0].Cell);
                if (flow.Hud.Info.Subject == null) throw new InvalidOperationException("고향 마을 칸에 조무래기 토큰 정보가 뜨지 않았다");
            }),
            Screen("board-info-spot", s =>                                      // 전체 줌에서 숨겨진 장소 「?」 토큰에 마우스를 올린 모습
            {
                var flow = ToBoard(s, BoardReadyMs);
                s.Rig.Set(BoardCameraRig.Zoom.Full, 0, s.Rig.Target);
                s.Panel.Tick(3);
                int cell = TokenCell(flow, s, Sinnoh.Rules.TokenKind.HiddenSpot);
                if (cell < 0) throw new InvalidOperationException("화면 안에 숨겨진 장소 토큰이 없다");
                flow.Hud.Hover(cell);
            }),
            Screen("board-tokens-close", s =>                                   // 근접 줌의 토큰(숨겨진 장소 「?」 · 조무래기)
            {
                ToBoard(s, BoardReadyMs);
                s.Rig.Set(BoardCameraRig.Zoom.Close, 0, s.Rig.Target);
            }),
            Screen("board-tokens-yaw", s =>                                     // 카메라를 120° 돌린 모습: 조무래기 토큰이 그 방향에서 본 왼쪽 앞 모서리에 바로 선다
            {
                ToBoard(s, BoardReadyMs);
                s.Rig.Set(BoardCameraRig.Zoom.Region, 2, s.Rig.Target);
            }),
            // ── 한 칸에 말 여럿(결정 66 ① — 묶음 표지): 좌석 여럿인 확인용 판(GameFlow.StartSeats)에서 n명을 좌석 1의 고향 마을 칸에 모은다 ──
            Screen("board-stack-2", s => Stack(s, 2)),                          // 둘: 내 말이 서고 머리 위 표지 「×2」(지역 줌 · 주인공 도트)
            Screen("board-stack-3", s => Stack(s, 3)),                          // 셋
            Screen("board-stack-6", s => Stack(s, 6)),                          // 여섯(한 칸 최대)
            Screen("board-stack-3-next", s => Stack(s, 3, ms: Sinnoh.Fx.PieceStacks.DwellMs.Value)),          // 한 명 머무는 시간이 지나 다음 좌석의 그림으로 넘어갔다
            Screen("board-stack-3-full", s => Stack(s, 3, BoardCameraRig.Zoom.Full)),                         // 전체 줌
            Screen("board-stack-3-close", s => Stack(s, 3, BoardCameraRig.Zoom.Close)),                       // 근접 줌
            Screen("board-stack-3-yaw", s => Stack(s, 3, yaw: 2)),                                            // 120° 돌린 모습(조무래기 표지가 그 방향의 왼쪽)
            Screen("board-stack-6-job", s => Stack(s, 6, style: PieceStyle.Job)),                             // 직업 그림 양식: 표지 안은 얼굴 조각
            Screen("board-stack-6-job-full", s => Stack(s, 6, BoardCameraRig.Zoom.Full, style: PieceStyle.Job)),
            Screen("board-stack-6-job-close", s => Stack(s, 6, BoardCameraRig.Zoom.Close, style: PieceStyle.Job, ms: 2 * Sinnoh.Fx.PieceStacks.DwellMs.Value)),
            Screen("board-stack-4-split", s => Stack(s, 4, together: 2)),       // 넷 가운데 둘만 한 칸: 나머지 둘은 제 고향 마을에 혼자 서 있다(표지 없음)
            Screen("board-stack-3-hover", s => StackHover(Stack(s, 3))),        // 표지에 마우스를 올린 모습: 그 칸의 좌석 셋(내 좌석 포함) + 조무래기 토큰
            Screen("board-stack-6-hover", s => StackHover(Stack(s, 6, style: PieceStyle.Job))),   // 여섯: 줄인 칸 여섯이 화면 안에 든다
            Screen("board-stack-3-walk", s =>                                   // 턴인 좌석의 말이 묶음에서 걸어 나가는 중: 남은 둘이 묶여 있다(「×2」)
            {
                var flow = Stack(s, 3);
                int from = flow.Client.View.Seats[0].Cell, walker = flow.Client.View.ActiveSeat;
                var reach = flow.ActiveReach();
                if (reach.Count == 0 || !flow.WalkActive(reach[reach.Count - 1])) throw new InvalidOperationException("턴인 좌석이 걷지 못했다");
                flow.Stage.Tweens.Advance(330);
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
                if (flow.Hud.Stacks.Total(from) != 2 || flow.BoardPieces.Tucked(walker)) throw new InvalidOperationException("걸어 나간 뒤 남은 묶음이 둘이 아니다");
            }),
            // ── 남은 M4 연출 행(결정 70 ② A2 — 04 §8-8 6·9·13·19·99·102행): 좌석 여럿인 확인용 판에서 그 이벤트가 엔진에서 나오게 한 뒤 그 단계의 한 순간으로 ──
            Screen("row-06-banner", s => TurnStart(s, 3, false, 0, 450)),        // 남의 턴 시작: 위쪽 배너 「플레이어 n의 턴」 + 카메라가 그 말로(라운드의 첫 턴이 아닌 때를 고른다)
            Screen("row-06-chips", s => TurnStart(s, 2, true, 0, 100)),          // 내 턴 시작(컷신 바로 뒤 — 단계 처음부터): 「남은 이동력」이 하나씩 차오르는 중(둘째)
            Screen("row-102-round", s => TurnStart(s, 2, false, 1, 400)),        // 새 라운드의 첫 턴: 라운드 띠 「n라운드」가 지나가는 중(+ 그 턴의 배너)
            Screen("row-09-shield", s => Shield(s, -1)),                         // 보호 배지가 붙어 있는 좌석 칩(풀리기 전)
            Screen("row-09-shield-swell", s => Shield(s, 120)),                  // 턴 시작: 배지가 밝아진다(늘리지 않는다)
            Screen("row-09-shield-fall", s => Shield(s, 400)),                   // 금이 가고 아래로 떨어지며 흐려지는 중
            Screen("row-13-meet", s => Meet(s, 250)),                            // 남의 말이 선 칸에 걸어 들어감: 닿는 때 머리 위 「!」, 두 말은 묶음 표지로 묶인다
            Screen("row-19-drop", s => Apricorns(s, 1150)),                      // 규토리 줍기: 하나씩 떨어져 튕기는 중
            Screen("row-19-gain", s => Apricorns(s, 2000)),                      // 말 쪽으로 모이며 「규토리 +3」
            Screen("row-99-picking", s => PickSeats(s, 3, 0, 0)),                // 셋업(좌석 셋): 나와 좌석 2는 「선택 완료」, 좌석 3은 「고르는 중」
            Screen("row-99-flip", s => PickSeats(s, 3, 1, 320)),                 // 모두 골랐다: 좌석 칩이 60ms씩 어긋나 뒤집히는 중
            Screen("row-99-revealed", s => PickSeats(s, 3, 1, 900)),             // 다 뒤집혔다: 좌석마다 고른 스타팅 포켓몬
            Screen("board-menu", Fit(s => WithSaves(s, flow => ToBoard(s, BoardReadyMs, flow: flow).OpenMenu(), games: 0))),   // 게임 메뉴(저장하는 판 — 「자동 저장됨 · 시각」)
            Screen("board-menu-ask", s => WithSaves(s, flow =>                  // 타이틀로 나가기 전에 묻는 모습(저장된 판)
            {
                ToBoard(s, BoardReadyMs, flow: flow).OpenMenu();
                ((BoardMenuScreen)flow.Stage.Current).AskQuit();
            }, games: 0)),
            Screen("board-menu-settings", s =>                                  // 게임 메뉴의 [설정](결정 70 ⑨): 같은 설정 화면이 HUD 위에(뒤를 덮는다 — 카메라는 보드 그대로)
            {
                var flow = ToBoard(s, BoardReadyMs);
                flow.OpenMenu();
                ((BoardMenuScreen)flow.Stage.Current).OpenSettings();
            }),
            Screen("board-menu-unsaved", s => ToBoard(s, BoardReadyMs).OpenMenu()),   // 저장하지 않는 판(확인용)의 게임 메뉴
            Screen("board-menu-ask-unsaved", s =>                               // 저장하지 않는 판에서 나가기를 묻는 모습(「사라집니다」는 이 판에서만)
            {
                var flow = ToBoard(s, BoardReadyMs);
                flow.OpenMenu();
                ((BoardMenuScreen)flow.Stage.Current).AskQuit();
            }),
            Screen("board-menu-failed", s => MenuFailed(s, false)),             // 저장하는 판인데 마지막 쓰기가 실패한 때: 저장 상태 줄 「저장하지 못했습니다.」
            Screen("board-menu-ask-failed", s => MenuFailed(s, true)),          // 그 판에서 나가기를 묻는 모습(사라지지 않는다 — 마지막으로 저장된 곳부터)
            // ── 배틀 화면(04 §4-3·§4-4): 고향 마을에서 [도전] → 1막 조무래기 배틀(실제 판 — 고정 시드) ──
            Screen("battle-select", s => ToBattle(s)),                          // 내 차례: 기술 줄 · 「○○은(는) 무엇을 할까?」
            Screen("battle-hover", s =>                                         // 기술 줄의 툴팁(줄 위)
            {
                var flow = ToBattle(s);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("battle-move-1"));
            }),
            Screen("battle-roll", s =>                                          // 기술을 고른 뒤 굴림이 멈추고 다 센 모습(굴림판 — 주사위 48px · 720 벌 32px, 결과 줄 · 합계)
            {
                var flow = ToBattle(s);
                if (!((BattleScreen)flow.Stage.Current).PressMove(0)) throw new InvalidOperationException("기술 답이 거절됐다");
                flow.Stage.Tweens.Advance(RollShotMs);
            }),
            Screen("battle-enemy-turn", s => ToEnemyTurn(s)),                   // 내 공격의 연출이 다 돈 뒤 상대(AI)의 답을 기다리는 모습 — 독은 「상대의 차례」, 기록·계산 줄이 찼다
            Screen("battle-result", s => ToBattleEnd(s)),                       // 배틀 끝: 결과 창 + [확인](이 시드는 진다 — 고향 마을로)
            Screen("battle-result-win", s => Win(s)),                           // 이긴 판의 결과 창(1막 목표 달성 · 2막) — 찾아 둔 판(WinSeed)
            // ── 조우 고르기(S45, 04 §4-1)와 배틀의 나머지 선택(04 §4-8): 찾아 둔 판(SoloTrail)을 길잡이가 화면으로 지난다 — 가짜 카드·가짜 상태 없음 ──
            Screen("board-hud-search", s => Trail(s, SoloTrail.Defeat, f => f.InputOpen && f.Hud.Shows(BoardHud.Search))),   // 약한 나무 칸: [찾아보기]가 켜져 있다
            Screen("encounter", s => Trail(s, SoloTrail.Defeat, Encounter)),      // 약한 나무: 카드 3장
            Screen("encounter-2", s => Trail(s, SoloTrail.Two, Encounter)),       // 적당한 나무: 2장
            Screen("encounter-1", s => Trail(s, SoloTrail.One, Encounter)),       // 강한 나무: 1장
            Screen("encounter-hover", s =>                                      // 고유 기술 줄의 툴팁(줄 아래, §10-2)
            {
                var flow = Trail(s, SoloTrail.Defeat, Encounter);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("encounter-card-2-move"));
            }),
            Screen("battle-wild", s => Trail(s, SoloTrail.Defeat, f => Battle(f, b => b.Open && b.Moves.Count > 0))),   // 야생 배틀의 내 차례(볼이 없어 [가방]은 회색 — 실제 진행)
            Screen("battle-calc-held", s => Trail(s, SoloTrail.Defeat, f => Battle(f, b => b.Rolling && b.CalcHeld))),   // 야생 배틀: 다음 굴림판이 떠도 앞 공격의 계산 줄(데미지 · 체력 변화)이 남아 있다(결정 70 ⑧)
            Screen("battle-teach", s => Trail(s, SoloTrail.Defeat, f => Battle(f, b => b.ShowsResult && b.AskingReward && b.Ready))),            // 격파 뒤: 기술 가르치기 묻기
            Screen("battle-teach-tip", BattleTeachTip),                                                                                           // 결정 186: 칸 1에 기술이 든 격파 판 — 원래 기술 칸 단추에 마우스(이름 + 효과)
            Screen("battle-taught", s => Trail(s, SoloTrail.Defeat, f => Battle(f, b => b.ShowsResult && !b.AskingReward && f.Battle.Taught != null), false)),   // 가르친 뒤의 결과 창
            Screen("battle-coral", s => Trail(s, SoloTrail.Coral, f => Battle(f, b => b.AskingReward && b.Ready))),             // 야생이 도망칠 때: 받을 규토리 색 묻기
            Screen("battle-fled", s => Trail(s, SoloTrail.Coral, f => Battle(f, b => b.ShowsResult && !b.AskingReward), false)),      // 도망친 뒤의 결과 창
            // 아래는 볼을 쥐여 준 판(SoloTrail.Catch — 볼을 얻는 길(제작, M5)이 아직 없다)
            Screen("battle-bag", s =>                                           // [가방]을 연 모습: 가진 볼 줄 + [뒤로]
            {
                var flow = Trail(s, SoloTrail.Catch, f => Battle(f, b => b.BagOn));
                if (!((BattleScreen)flow.Stage.Current).OpenBag()) throw new InvalidOperationException("[가방]이 열리지 않았다");
            }),
            // 배틀 중 정비(04 §3-5): 기술 카드·지닌물건을 쥐여 준 판(SoloTrail.Kit)의 첫 야생 배틀 — 독의 [정비](T) → 배틀 화면 위의 정비 판
            Screen("battle-maint", s => Trail(s, SoloTrail.Kit, f => Battle(f, b => b.MaintOn && b.Moves.Count > 0))),   // 내 차례: 독에 [정비]가 켜져 있다
            Screen("battle-maint-party", s =>                                   // [정비]를 눌러 연 정비 판에서 기술 카드를 집은 모습(지닌물건·놓아주기는 배틀 밖에서만 — 꺼진 칸)
            {
                var flow = Trail(s, SoloTrail.Kit, f => Battle(f, b => b.MaintOn && b.Moves.Count > 0));
                if (!flow.OpenParty() || !(flow.Top is PartyScreen party) || !party.PickMove(0)) throw new InvalidOperationException("배틀 화면 위에 정비 판이 열리지 않았다");
            }),
            Screen("battle-maint-drag", s =>                                    // 배틀 위 정비 판에서도 끌어 놓는다(결정 70 ⑪)
            {
                var flow = Trail(s, SoloTrail.Kit, f => Battle(f, b => b.MaintOn && b.Moves.Count > 0));
                if (!flow.OpenParty() || !(flow.Top is PartyScreen party) || !party.BeginDrag(PartyScreen.Hold.Move, 0)) throw new InvalidOperationException("배틀 화면 위 정비 판에서 끌지 못했다");
                s.Panel.Tick(3);
                party.DragTo(s.Made.Stage.Root.Q("board-party-slot-0-1").worldBound.center);
            }),
            Screen("battle-captured", s => Trail(s, SoloTrail.Catch, f => Battle(f, b => b.ShowsResult && !b.AskingReward && f.Battle.Result == Sinnoh.Rules.BattleResult.Captured), false)),   // 잡은 뒤의 결과 창
            Screen("battle-sendin", s => Trail(s, SoloTrail.Catch, f => Battle(f, b => b.Open && b.Bench.Count > 0 && !b.CanStay))),   // 파티 둘: 출전 포켓몬 고르기
            // 남은 M4 연출 행(04 §8-8 8·17·71·73행): 실제 진행 판(Defeat)에서 그 연출의 한 순간(연출 시각만 보낸다)
            Screen("board-heal-flash", s => DefeatMoment(s, FxShots.HealNext, 100)),            // 고향 [전체 회복]: 파티 칸이 반짝인다(체력은 징글 끝에 찬다)
            Screen("board-whiteout-dark", s => DefeatMoment(s, FxShots.WhiteoutResult, 1100)),  // 조무래기에게 진 뒤 결과 창 [확인]: 보드가 다 어둡고 말이 고향에 다시 섰다
            Screen("board-whiteout-clear", s => DefeatMoment(s, FxShots.WhiteoutResult, 1700)), // 어둠이 걷히는 중
            Screen("board-respawn-heal", s => DefeatMoment(s, FxShots.WhiteoutResult, 3100)),   // 이어 내 턴 시작의 귀환 회복(8행): 파티 칸이 반짝인다
            Screen("battle-ai-line", s => AiMoment(s, 300)),                                    // 조무래기의 기술 선택: DS 위 가장자리의 AI 자막
            Screen("battle-sendin-6", SendInSix),                                                                                    // 파티 여섯(이름 다섯 글자 포함): 출전 포켓몬 고르기 — 여섯 칸이 독 폭 안에(M5 마감 검토)
            Screen("battle-switch", s => Trail(s, SoloTrail.Catch, f => Battle(f, b => b.Open && b.CanStay))),                       // 교체 창: 대기 포켓몬 + [교체 안 함]
            Screen("battle-partyfull", s => Trail(s, SoloTrail.Catch, f => Battle(f, b => b.AskingReward && b.Ready && f.Client.Choice(Sinnoh.Rules.ChoiceKind.Reward).Sub == Sinnoh.Rules.ChoiceSub.PartyFull))),   // 셋째 포획: 놓아줄 포켓몬 고르기
            // ── 들어가고 나오는 연출(04 §8-8 20·21·22·53·68·70행)의 한 순간들 — 찾아 둔 판에서 그 답을 누른 뒤 연출 시각만 보낸다(움직임은 FxShots가 찍는다) ──
            Screen("board-hud-bang", s => Searching(s, 300)),                     // [찾아보기]를 누른 뒤: 말 머리 위 「!」 + 찾는 칸 테두리
            Screen("encounter-flip", s => Searching(s, 1750 + 675)),              // 조우 창: 첫 장은 앞면, 둘째 장이 뒤집히는 중, 셋째 장은 뒷면
            Screen("enter-wipe", s => Picking(s, 1500)),                   // 카드를 고른 뒤: 고른 카드가 커지고 가운데 필드의 줄무늬가 닫히는 중
            Screen("battle-intro", s => Picking(s, 3250)),                        // 배틀 화면: 야생 포켓몬이 미끄러져 들어오는 중 「앗! 야생 ○○이(가) 튀어나왔다!」
            Screen("battle-capture", s => Throwing(s, 450)),                      // 볼을 던진 뒤: 내 주인공 뒷모습 · 날아가는 볼 · 구르는 포획 주사위
            Screen("battle-capture-shake", s => Throwing(s, 1800)),               // 볼이 흔들리는 중(굴림은 멈췄고 아직 세기 전)
            Screen("battle-capture-ok", s => Throwing(s, 3200)),                  // 판정: 볼이 어두워지고 별 · 계산 줄 「성공」
            Screen("battle-result-lines", s => Throwing(s, 5800)),                // 결과 창: 받은 것이 하나씩 내려앉는 중([확인]은 아직)
            // ── 정비(plan §4 「정비」, 04 §3-5·§10-3): S18 내 판 · S28 기술 가르치기 · S29 지닌물건 · S38 카드 확대 ──
            Screen("board-party-plain", s =>                                     // 실제 진행의 정비 판: 스타팅 포켓몬 하나 · 기술 카드·지닌물건 없음
            {
                var flow = ToBoard(s, BoardReadyMs);
                if (!flow.OpenParty()) throw new InvalidOperationException("정비 판이 열리지 않았다");
            }),
            // 아래는 기술 카드·지닌물건을 쥐여 준 판(SoloTrail.Kit — 얻는 길(트레이너전 보상·제작)은 M5), 두 마리를 잡아 파티 셋
            Screen("board-party", Fit(s => Maint(s))),                              // 정비 판: 파티 셋 · 잠긴 칸 셋 · 기술 카드 6장 · 가방의 지닌물건 셋
            Screen("board-party-teach", s =>                                     // 기술 카드를 집은 모습: 배울 수 있는 포켓몬의 기술 칸만 빛난다
            {
                if (!Maint(s).PickMove(0)) throw new InvalidOperationException("기술 카드가 집히지 않았다");
            }),
            Screen("legend-slot-party", Fit(s => LegendMaint(s))),               // 결정 195: 전설 특수기술을 가진 스타팅 포켓몬 — 빈 칸 1 = 알파 스트라이크(배우는 타입 그림) · 칸 2 = 비스트 부스트
            Screen("legend-slot-teach", s =>                                     // 기술 카드를 집고 칸 1에 마우스: 「이 칸을 채우면 알파 스트라이크가 사라집니다.」
            {
                var party = LegendMaint(s);
                if (!party.PickMove(0)) throw new InvalidOperationException("기술 카드가 집히지 않았다");
                s.Made.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
                s.Made.Stage.Tips.Show(s.Made.Stage.Root.Q("board-party-slot-0-1"));
            }),
            Screen("legend-slot-mon", s =>                                       // 카드 확대(MonScreen): 칸 1 · 2 = 전설 특수기술 + 효과 · 채우면 사라진다 줄
            {
                LegendMaint(s);
                var flow = s.Made;
                if (!flow.OpenMon(flow.Client.View.Seats[flow.Client.Seat].Party[0], flow.Client.Seat)) throw new InvalidOperationException("카드 확대가 열리지 않았다");
                if (flow.Stage.Root.Q("mon-detail-move-1")?.Q(className: MonParts.LegendSlotClass) == null) throw new InvalidOperationException("카드 확대 칸 1이 전설 특수기술이 아니다");
            }),
            Screen("board-party-drag", s =>                                      // 끌어 놓는 중(결정 70 ⑪): 기술 카드를 끌어 빛나는 칸 위 — 카드 그림이 포인터를 따라온다(결정 73 ④)
            {
                var party = Maint(s);
                if (!party.BeginDrag(PartyScreen.Hold.Move, 0)) throw new InvalidOperationException("기술 카드를 끌지 못했다");
                s.Panel.Tick(3);
                party.DragTo(s.Made.Stage.Root.Q("board-party-slot-0-1").worldBound.center);
            }),
            Screen("board-party-drag-item", s =>                                 // 지닌물건을 끌어 지닌물건 칸 위
            {
                var party = Maint(s);
                if (!party.BeginDrag(PartyScreen.Hold.Item, 0)) throw new InvalidOperationException("지닌물건을 끌지 못했다");
                s.Panel.Tick(3);
                party.DragTo(s.Made.Stage.Root.Q("board-party-item-1").worldBound.center);
            }),
            Screen("board-party-ask", s =>                                       // 찬 칸에 다른 기술을 넣으려 할 때: 물음 줄 「○○을(를) 잊고 △△을(를) 가르치겠습니까?」
            {
                var party = Maint(s);
                Taught(party);
                int other = OtherLearnable(party);
                if (!party.PickMove(other) || !party.PressSlot(0, 0) || !party.Asking) throw new InvalidOperationException("찬 칸에 넣는 물음이 뜨지 않았다");
            }),
            Screen("board-party-item", s =>                                      // 지닌물건: 하나는 이미 지니게 했고, 가방의 다른 것을 집어 지닌물건 칸이 빛난다
            {
                var party = Maint(s);
                Taught(party);
                if (!party.PickItem(0) || !party.PressItemSlot(0) || !party.PickItem(0)) throw new InvalidOperationException("지닌물건을 지니게 하지 못했다");
            }),
            Screen("mon-detail", s =>                                            // 카드 확대(S38): 기술을 가르치고 지닌물건을 지니게 한 스타팅 포켓몬
            {
                var party = Maint(s);
                Taught(party);
                if (!party.PickItem(0) || !party.PressItemSlot(0) || !party.PressMon(0)) throw new InvalidOperationException("카드 확대가 열리지 않았다");
            }),
            // ── 장소·카드(plan §4 「장소·카드」, 04 §3-9): S23 마을 창(S25 공중날기) · S34 숨겨진 장소 · S40 트레이너 카드 판 ──
            Screen("board-village", s =>                                         // 첫 턴의 고향 마을: [전체 회복]은 켜져 있고 공중날기는 조무래기 점거로 막혔다
            {
                var flow = ToBoard(s, BoardReadyMs);
                if (!flow.Hud.Press(BoardHud.Village)) throw new InvalidOperationException("마을 창이 열리지 않았다");
            }),
            Screen("board-village-used", s =>                                    // [전체 회복]을 누른 뒤: 단추가 꺼지고 까닭이 선다, 아래 판 「이번 턴」에 전체 회복
            {
                var flow = ToBoard(s, BoardReadyMs);
                if (!flow.Hud.Press(BoardHud.Village) || !((VillageScreen)flow.Stage.Current).PressHeal()) throw new InvalidOperationException("전체 회복이 거절됐다");
                flow.Stage.Tweens.Advance(BoardReadyMs);
            }),
            Screen("board-village-fly", s =>                                     // 조무래기를 이긴 뒤의 고향 마을: 목적지 하나를 고른 모습 — [출발]이 켜졌다
            {
                var village = Freed(s);
                if (!village.PickDest(FirstDest(village))) throw new InvalidOperationException("목적지가 골라지지 않았다");
            }),
            // T18 ① 보드에서 고르기: [지도에서 고르기] → 창이 오른쪽 기둥으로 비키고 판 전체 · 갈 수 있는 마을 칠 + 테두리 · 갈 수 없는 마을 흐림
            Screen("board-fly-map", Fit(s => FlyMap(s, v => { if (!v.ClickCell(s.Made.Ctx.Fixtures.Fixtures[FirstDest(v)].Cell)) throw new InvalidOperationException("보드의 마을 칸을 눌러도 목적지가 골라지지 않았다"); }))),   // 보드에서 누른 목적지 = 금색 테두리
            Screen("board-fly-map-why", Fit(s => FlyMap(s, v =>                 // 지금 선 마을(흐림)에 마우스를 올린 모습: 안내 줄에 까닭
            {
                int here = s.Made.Client.View.Seats[s.Made.Client.Seat].Cell;
                v.HoverCell(here);
                if (v.WhyAt(here) != "screen.village.fly.why.here" || v.HintText == s.Made.Stage.T("screen.village.fly.map.hint")) throw new InvalidOperationException("흐린 마을의 까닭이 안내 줄에 없다");
            }))),
            Screen("board-fly-map-chip", Fit(s => FlyMap(s, v =>                // 목적지 칩에 마우스를 올린 모습: 보드 강조가 그 마을로(판 전체에 보이면 카메라는 그대로 — 가려 있으면 그 마을로)
            {
                int dest = -1;
                foreach (int f in v.Dests) if (v.CanGo(f) && f != FirstDest(v)) { dest = f; break; }
                if (dest < 0) dest = FirstDest(v);
                v.HoverDest(dest);
                if (s.Made.BoardMarks.Target != s.Made.Ctx.Fixtures.Fixtures[dest].Cell || s.Made.BoardMarks.Held) throw new InvalidOperationException("칩에 올려도 보드 강조가 따라가지 않는다");
            }))),
            Screen("board-fly", s =>                                             // [출발] 뒤: 말이 호를 그리며 날아가는 한가운데(카메라가 따라간다)
            {
                var village = Freed(s);
                var flow = s.Made;
                if (!village.PickDest(FirstDest(village)) || !village.PressFly()) throw new InvalidOperationException("공중날기가 거절됐다");
                flow.Stage.Tweens.Advance(Sinnoh.Fx.FlyBeats.CutinMs + 1600);
                if (!flow.BirdShown(out _)) throw new InvalidOperationException("공중날기 동안 찌르호크가 말을 태우고 날지 않는다(결정 126)");
            }),
            Screen("board-flown", s =>                                           // 날아간 뒤: 도착 마을 · 「이번 턴」에 공중날기 · 기록 줄
            {
                var village = Freed(s);
                var flow = s.Made;
                if (!village.PickDest(FirstDest(village)) || !village.PressFly()) throw new InvalidOperationException("공중날기가 거절됐다");
                flow.Stage.Tweens.Advance(Sinnoh.Rules.Session.PlayTiming.FlyMs + BoardReadyMs);
            }),
            // ── 제작·은행·진화(M5 「제작·은행·진화 화면」 — 04 S24·S26·S27 · §8-8 18·21·36·37·39행 · 16행 Seek): 손본 판(SoloTrail.Work — 규토리 일곱 색 셋씩 · 여행 점수 20 · 파티 첫 칸 이브이)의 첫 턴 ──
            Screen("board-craft", Fit(s => CraftOpen(s))),                                                                    // 제작 창(S26): 가진 규토리 · 볼 · 스튜(턴당 1회) · 지닌물건
            Screen("board-craft-suck", Fit(s => Crafted(s, "item.choice_band", Sinnoh.Fx.CraftBeats.SuckFromMs + 220))),     // 결단의 머리띠 [제작] 뒤: 재료 여섯이 어긋나 빨려 드는 중(줄이 빛남)
            Screen("board-craft-make", Fit(s => Crafted(s, "item.poke_ball", Sinnoh.Fx.CraftBeats.FlashAt + 40))),          // 몬스터볼 [제작] 뒤 펑: 완성품 둘레가 하얗고 커지는 중 · 「+1」
            Screen("board-craft-plus", Fit(s => Crafted(s, "item.poke_ball", 1400))),                                      // 「+1」이 떠올라 머무는 때
            Screen("board-craft-stew", Fit(s => Crafted(s, Sinnoh.Rules.CraftRules.ZestyStew, 1500 + Sinnoh.Fx.StewBeats.BadgeAt + 200))),   // 자극적인 스튜: 그릇이 튄 뒤 「이동력 +3」 꼬리표
            Screen("board-craft-done", Fit(s => Crafted(s, Sinnoh.Rules.CraftRules.ZestyStew, 3000 + BoardReadyMs))),      // 만든 뒤: 「이번 턴 사용」 · 가진 규토리 하양·노랑 2 · 아래 판 이동력 +3
            Screen("encounter-craft-button", Fit(s => Trail(s, SoloTrail.Work, Encounter))),                                // 조우 창(S45) 아래 줄의 [제작](21행)
            Screen("encounter-craft", Fit(s =>                                                                             // 조우 창 위에 연 제작 창 — 닫으면 조우 창이 다시 선다
            {
                var flow = Trail(s, SoloTrail.Work, Encounter);
                if (!flow.OpenCraft() || !(flow.Stage.Current is CraftScreen)) throw new InvalidOperationException("조우 창의 [제작]이 제작 창을 열지 않았다");
            })),
            Screen("board-village-bank", Fit(s => BankOpen(s))),                                                           // 마을 창의 은행상회 칸(S24): 맨 위 거래 카드의 두 줄 · [두 줄 모두]
            Screen("board-village-bank-trade", Fit(s => Traded(s, Sinnoh.Fx.BankBeats.GetFromMs + 150))),                  // [거래] 뒤: 낸 규토리가 빨려 들었고 받은 규토리가 튀어나오는 중
            Screen("board-village-bank-flip", Fit(s => Traded(s, Sinnoh.Fx.BankBeats.FlipAt + 60))),                      // 순증감 숫자 · 카드가 뒤집히는 중(가운데 전)
            Screen("board-village-bank-used", Fit(s => Traded(s, 2500 + BoardReadyMs))),                                   // 거래 뒤: 새 맨 위 카드 · 「이번 턴에 이미 사용했습니다.」
            Screen("board-evolve", Fit(s => EvolveOpen(s))),                                                               // 진화 창(S27): 이브이(8가지 모습) · 스타팅 포켓몬 · 비용
            Screen("board-evolve-half", Fit(s =>                                                                          // 우러난 스튜를 먹은 뒤: [우러난 스튜 반값]을 켜 비용이 반
            {
                var craft = CraftOpen(s);
                var flow = s.Made;
                if (!craft.PressMake(craft.ItemOf(Sinnoh.Rules.CraftRules.InfusedStew))) throw new InvalidOperationException("우러난 스튜를 만들지 못했다");
                flow.Stage.Tweens.Advance(3000 + BoardReadyMs);
                flow.CloseCraft();
                if (!EvolveOpen(s, flow).SetHalf(true)) throw new InvalidOperationException("[반값으로]가 켜지지 않았다");
            })),
            Screen("board-evolve-branch", Fit(s =>                                                                        // 이브이 [진화] 뒤: 진화할 모습 고르기(R14 — 여덟)
            {
                var evolve = EvolveOpen(s);
                if (!evolve.PressEvolve(evolve.Listed[0]) || evolve.Now != EvolveScreen.Mode.Branch) throw new InvalidOperationException("이브이의 진화 갈래가 열리지 않았다");
                s.Made.Stage.Tweens.Advance(BoardReadyMs);   // 클릭 막기가 지나 카드가 켜진 모습
                s.Panel.Tick(3);
                // 이름이 도트 칸과 띄어져 있다(결정 87 V1 ① — 큰 도트의 발에 닿았다)
                foreach (var card in s.Made.Stage.Root.Query(className: "sn-evolve-branch").ToList())
                {
                    var art = card.Q(className: "sn-mon");
                    var name = card.Q(className: "sn-evolve-branch-name");
                    if (art == null || name == null || name.worldBound.yMin < art.worldBound.yMax + 1f) throw new InvalidOperationException(card.name + ": 이름이 도트 칸에 닿는다(" + (art != null ? art.worldBound.ToString() : "도트 없음") + " · " + (name != null ? name.worldBound.ToString() : "이름 없음") + ")");
                }
            })),
            Screen("board-evolve-start", Fit(s => Evolving(s, 300))),                                                     // 스타팅 포켓몬 [진화] 뒤: 「…오잉!?」 · 여행 점수 −4가 빠져나감
            Screen("board-evolve-white", Fit(s => Evolving(s, 2500))),                                                    // 흰 실루엣이 두 모습 사이를 오가는 중(결정 75 C2 가)
            Screen("board-evolve-flash", Fit(s => Evolving(s, Sinnoh.Fx.EvolveBeats.FlashAt(Sinnoh.Rules.Session.PlayTiming.EvolveMs) + 120))),   // 흰 빛 · 「축하합니다!」
            Screen("board-evolve-done", Fit(s => Evolving(s, Sinnoh.Rules.Session.PlayTiming.EvolveMs - 100))),          // 진화 뒤 모습의 제 색
            Screen("board-evolve-skip", Fit(EvolveSkipped)),                                                             // 결정 84 L11: 오가는 중 스페이스 바 → 착지(진화 뒤 제 색 + 「축하합니다!」)
            Screen("board-evolve-teach", Fit(s => Evolving(s, Sinnoh.Rules.Session.PlayTiming.EvolveMs + BoardReadyMs))), // 옛 기술(p.5): 칸 1 · 칸 2에 가르치거나 [가르치지 않기]
            Screen("board-evolve-teach-now", Fit(s => EvolveTeachNow(s, false))),                                       // 결정 181: 해금 덮개가 끝난 옛 기술 물음 — 진화형의 ★ 고유 기술 + 칸 1·2(지금 기술)
            Screen("board-evolve-teach-now-filled", Fit(s => EvolveTeachNow(s, true))),                                 // 결정 181: 칸 1에 기술이 있는 판 — 바꿀 기술이 칸에 보인다
            Screen("board-evolve-teach-tip", Fit(s =>                                                                     // 결정 186: 그 판의 칸 1(지금 기술)에 마우스 — 기술 이름 + 효과 + 안내 한 줄
            {
                EvolveTeachNow(s, true);
                var p = s.Made.Client.View.Pokemon[s.Made.Client.Choice(R.ChoiceKind.Reward).Options[0]];
                MoveTipShown(s, s.Made.Stage.Root.Q("board-evolve-teach-0"), p.Moves[0], s.Made.Stage.Root.Q("board-evolve-panel"));
            })),
            Screen("board-fly-lift", s => Flying(s, Sinnoh.Fx.FlyBeats.CutinMs + 300)),                                                           // 16행 Seek: [출발] 뒤 제자리에서 떠오르는 중
            Screen("board-fly-land", s => Flying(s, Sinnoh.Fx.FlyBeats.CutinMs + 3050)),                                                          // 16행 Seek: 도착 마을에 내려앉는 중
            Screen("board-trainer", Fit(s =>                                       // 트레이너 카드 판(S40): 브론즈 — 능력 둘 다 잠김
            {
                var flow = ToBoard(s, BoardReadyMs);
                if (!flow.OpenTrainer()) throw new InvalidOperationException("트레이너 카드 판이 열리지 않았다");
            })),
            // 도트 이펙트(결정 69, pixelfx — 보드 위 자리): 착지 먼지 둘(걷기 끝은 결정 70 ⑬ ⑥으로 뺐다) · 숨겨진 장소 공개 반짝임 · 회복 반짝임 · 스타팅 볼 열림 빛
            Screen("fx-dust-village", s => VillageDrop(s, 1116 + 60)),           // 고향 마을에 말이 내려앉은 바로 뒤
            Screen("fx-dust-fly", s =>                                           // 공중날기로 내려앉은 바로 뒤(항목이 끝난 뒤의 꼬리)
            {
                var village = Freed(s);
                var flow = s.Made;
                if (!village.PickDest(FirstDest(village)) || !village.PressFly()) throw new InvalidOperationException("공중날기가 거절됐다");
                flow.Stage.Tweens.Advance(Sinnoh.Rules.Session.PlayTiming.FlyMs + 60);
            }),
            Screen("fx-spot-star", s => Reveal(s, ToSpot(s), 192 + 130, true)),     // [공개] 뒤 앞면으로 바뀐 바로 뒤: 토큰 둘레의 반짝임
            // HUD·셋업 화면 위의 이펙트는 칸의 자리(배치)를 읽는다 — 그 순간 1ms 앞에서 패널을 한 번 돌려 배치를 세운 뒤 마저 보낸다
            Screen("fx-heal", s =>                                               // [전체 회복]: 파티 칸의 도트 위로 오르는 반짝임
            {
                DefeatMoment(s, FxShots.HealNext, 199);
                s.Panel.Tick(3);
                s.Made.Stage.Tweens.Advance(1);
            }),
            Screen("fx-starter-open", s =>                                       // 스타팅 볼이 열리는 순간의 빛
            {
                var flow = Picked(s, s2 => s2.Row == Sinnoh.Fx.SetupRows.Starter, 660 + 79);
                s.Panel.Tick(3);
                flow.Stage.Tweens.Advance(1);
            }),
            Screen("board-spot", s => Raised(s, ToSpot(s), false)),              // 숨겨진 장소에 멈춘 모습: 토큰이 떠 있고 아래 판에서 [공개]·[지나가기]를 묻는다
            Screen("board-spot-flip", s => Reveal(s, ToSpot(s), SpotFlipMs, false)),          // [공개]를 누른 뒤: 토큰이 더 떠올라 가로로 뒤집히는 중(뒷면 — 뒤집기 가운데 전)
            Screen("board-spot-revealed", s => Reveal(s, ToSpot(s), SpotFaceMs, true)),       // 뒤집힌 뒤 앞면으로 머무는 때: 앞면 그림 · 칸 정보 줄과 기록에 그 장소의 이름
            // 숨겨진 장소 결과 다섯(rulings A25)마다 뒤집히는 중 · 앞면 — 야영지만 그 칸에 남고, 나머지 넷은 공개한 명령 안에서 맵에서 빠져도 공개 연출이 끝날 때까지 떠 있다(규칙 끝 감사)
            Screen("board-spot-stash-flip", s => Reveal(s, SpotOf(s, "stash"), SpotFlipMs, false)),
            Screen("board-spot-stash-face", s => Reveal(s, SpotOf(s, "stash"), SpotFaceMs, true)),
            Screen("board-spot-star-flip", s => Reveal(s, SpotOf(s, "star"), SpotFlipMs, false)),
            Screen("board-spot-star-face", s => Reveal(s, SpotOf(s, "star"), SpotFaceMs, true)),
            Screen("board-spot-relic-flip", s => Reveal(s, SpotOf(s, "relic"), SpotFlipMs, false)),
            Screen("board-spot-relic-face", s => Reveal(s, SpotOf(s, "relic"), SpotFaceMs, true, art: false)),   // 앞면 그림이 Gen에 아직 없다(뒷면 그대로 뒤집힌다 — 규칙 끝 감사 반영 보고)
            Screen("board-spot-alpha-flip", s => Reveal(s, SpotOf(s, "alpha"), SpotFlipMs, false)),
            Screen("board-spot-alpha-face", s => Reveal(s, SpotOf(s, "alpha"), SpotFaceMs, true, art: false)),   // 앞면 그림이 Gen에 아직 없다(뒷면 그대로 뒤집힌다 — 규칙 끝 감사 반영 보고)
            Screen("board-spot-camp-flip", s => Reveal(s, SpotOf(s, "camp"), SpotFlipMs, false)),
            Screen("board-spot-camp-face", s => Reveal(s, SpotOf(s, "camp"), SpotFaceMs, true)),
            Screen("board-path-spot", s =>                                       // 결정 71 ①: 고른 칸까지의 최단 길 가운데에 뒷면 숨겨진 장소 — 그 칸에 하늘색 표지, 안내 「… · 숨겨진 장소 n곳」
            {
                var flow = ToPassPair(s, out _, out _);
                int dest = SpotOnShortest(flow);
                if (dest < 0 || !flow.Hud.PickCell(dest)) throw new InvalidOperationException("길 가운데에 숨겨진 장소가 있는 칸이 없다");
            }),
            Screen("board-path-spot-tip", s =>                                   // T15: 같은 모습에서 안내 줄에 마우스를 올린 모습 — 「숨겨진 장소 — 우두머리 주의」(공개하면 바로 배틀 · 자극적인 스튜는 공개 전에)
            {
                var flow = ToPassPair(s, out _, out _);
                int dest = SpotOnShortest(flow);
                if (dest < 0 || !flow.Hud.PickCell(dest)) throw new InvalidOperationException("길 가운데에 숨겨진 장소가 있는 칸이 없다");
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("board-hud-guide"));
            }),
            // 결정 71 걷는 길 고르기: 칸을 고른 뒤 뒷면 숨겨진 장소 칸을 눌러 경유 칸으로 — 길이 그 칸을 거치고(흰 테두리) 길 위 숨겨진 장소에 하늘색 표지, 안내 「… · 숨겨진 장소 n곳」
            Screen("board-path-via", s =>
            {
                var flow = ToPassPair(s, out int spot, out int beyond);
                if (!flow.Hud.PickCell(beyond) || !flow.Hud.AddVia(spot)) throw new InvalidOperationException("경유 칸 고르기가 거절됐다");
            }),
            Screen("board-path-via-tip", s =>                                    // 같은 모습에서 [이동] 툴팁(다른 칸 = 경유 · 오른쪽 클릭·Esc = 하나씩 취소 · 숨겨진 장소에서 멈춤)
            {
                var flow = ToPassPair(s, out int spot, out int beyond);
                if (!flow.Hud.PickCell(beyond) || !flow.Hud.AddVia(spot)) throw new InvalidOperationException("경유 칸 고르기가 거절됐다");
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q(BoardHud.MoveGo));
            }),
            Screen("board-spot-walk", s => ToPassAsk(s)),                        // 그 길로 걷다가 숨겨진 장소에서 멈춘 모습: 「지나는 길에 …」 · 남은 길(금색 테두리 = 고른 칸) · [공개]·[지나가기]
            Screen("board-spot-walk-tip", s =>                                   // 같은 모습에서 [지나가기] 툴팁(남은 길을 이어서 이동)
            {
                var flow = ToPassAsk(s);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("board-hud-act-" + BoardHud.Pass));
            }),
            Screen("board-spot-walk-passed", s =>                                // [지나가기] 뒤 남은 길을 이어 걸어 고른 칸에 닿은 모습
            {
                var flow = ToPassAsk(s);
                if (!flow.Hud.Press(BoardHud.Pass)) throw new InvalidOperationException("[지나가기]가 거절됐다");
                flow.Stage.Tweens.Advance(BoardReadyMs * 2);
            }),
            // ── 막·종막 화면(M5 — 04 §8-8 4·94·95·96·97행, S14·S62·S63·S64): 막 길(SoloTrail.Lake·Heart — 셋업 뒤 손본 판)과 종막 앞 판(SoloTrail.PrepareEnding) ──
            Screen("row-94-heart-offer", s =>                                    // 3막 · 시작의 사당: 아래 판에 [공물](심장 회복) — 마우스를 올린 툴팁
            {
                var flow = HeartOffer(s);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("board-hud-act-" + BoardHud.Offer));
            }),
            Screen("row-94-heart-spin", s => HeartStep(s, 900)),                // [공물] 뒤: 조각 셋이 사당 위에서 돌며 좁혀 든다
            Screen("row-94-heart-merge", s => HeartStep(s, 1550)),              // 모였다: 신오의 심장이 톡 커졌다 자리 잡는 중
            Screen("row-04-act3", s => ActCardOf(s, SoloTrail.Lake, 1900)),     // 2막 목표 달성 뒤 3막 카드(처음 달성한 좌석 「플레이어 1 달성」)
            Screen("row-04-finale", s => ActCardOf(s, SoloTrail.Heart, 1900)),  // 태홍을 이긴 뒤 종막 카드
            Screen("row-95-ask", s => EndingAsk(s, 1)),                          // S62: 영웅·배신자 두 카드
            Screen("row-95-ask-tip", s =>                                        // 배신자 카드에 마우스를 올린 툴팁(연전 규칙)
            {
                var flow = EndingAsk(s, 1);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("ending-card-1"));
            }),
            Screen("row-95-hero-burn", s => EndingChose(s, 1, 0, 1100)),       // [영웅] 뒤: 배신자 카드가 타 들어가는 중
            Screen("row-95-traitor-red", s => EndingChose(s, 1, 1, 2000)),     // [배신자] 뒤: 영웅 카드는 타 버렸고 화면이 붉게 물든다
            Screen("row-96-order", Fit(s =>                                        // S63(좌석 셋): 배신자의 순서 고르기 — 좌석 3을 먼저 골랐다
            {
                var flow = TraitorOrder(s);
                ((EndingScreen)flow.Stage.Current).PickSeat(2);
            })),
            Screen("row-96-chips", s =>                                          // [확정] 뒤: 고른 순서가 칩 줄로 하나씩 놓이는 중(96행)
            {
                var flow = TraitorOrder(s);
                var screen = (EndingScreen)flow.Stage.Current;
                if (!screen.PickSeat(2) || !screen.PickSeat(1) || !screen.ConfirmOrder()) throw new InvalidOperationException("배신자 순서가 거절됐다");
                var step = Queued(flow, st => st.Row == Sinnoh.Fx.FinaleRows.Order);
                if (step == null) throw new InvalidOperationException("순서 공개 단계(96행)가 대기열에 없다");
                Into(s, flow, step, 1000);
            }),
            Screen("row-97-result-mons", s => EndingChose(s, 1, 0, 2800 + 3300)),   // [영웅] 뒤 결과(97행): 파티 포켓몬이 하나씩 나오는 중
            Screen("row-97-result", s => EndingChose(s, 1, 0, 2800 + 8000 + 400)),  // 결과 창: 통계까지 다 보였고 [타이틀로]가 켜졌다
            Screen("row-97-traitor-won", s => EndingChose(s, 1, 1, 12000 + 4000)),  // 혼자 하는 판의 배신자: 상대가 없어 곧바로 배신자의 승리
            Screen("row-97-trail", s =>                                          // 막 길(Heart): 사람 혼자 하는 판이 종막을 [영웅]으로 끝낸 결과 창(실제 진행)
            {
                var flow = Trail(s, SoloTrail.Heart, f => f.Stage.Current is ResultScreen r && r.Ready);
                flow.Layout(s.Width, s.Height);
            }),
            // ── M5 「운명·응답 화면」(04 §8-8 7·8·14·42~45·98행 · S30 · S41) — 운명 길 SoloTrail.Fate·FateGiratina, 응답은 좌석 둘 확인 판 ──
            Screen("row-42-discard", s => FateDiscard(s, false)),                // S30 6장째: 손패 창이 저절로 열려 버릴 카드를 묻는다(가장 나중에 받은 카드에 「새 카드」)
            Screen("row-42-discard-pick", s => FateDiscard(s, true)),            // 새 카드를 골랐다: [버리기]가 켜진다
            Screen("row-42-tear", s =>                                           // [버리기] 뒤: 버린 카드가 [운명의 변화] 위로 떠올라 찢기듯 사라지는 중(42행)
            {
                var flow = FateDiscard(s, true);
                if (!((FateScreen)flow.Top).Discard()) throw new InvalidOperationException("버리기가 거절됐다");
                FateInto(s, flow, Sinnoh.Fx.FateRows.Discard, 420);
            }),
            Screen("board-fate-hand", Fit(s => FateHand(s, false))),                // S30 손패 창(결정 75 A2 가): 펄기아를 골랐다 — 못 쓰는 카드는 흐리게 + 까닭
            Screen("board-fate-hand-tip", s =>                                   // 카드에 마우스를 올린 툴팁(1장·3장 효과)
            {
                var flow = FateHand(s, false);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("board-fate-card-3"));
            }),
            Screen("row-14-warp-pick", s => FateHand(s, true)),                  // [1장 사용](펄기아): 창이 닫히고 갈 수 있는 칸(3걸음 안)이 칠해진다
            Screen("row-45-pass", s => FateWarp(s, Sinnoh.Fx.FateRows.Play, 300)),           // 칸을 누른 뒤: 펄기아 카드가 가운데로 튀어나와 지나가는 중(45행 카드 지나가기)
            Screen("row-14-warp", s => FateWarp(s, Sinnoh.Fx.FateRows.Teleport, 600)),       // 말이 떠올라 틈으로 사라지는 중(출발 칸의 분홍 고리)
            Screen("row-14-land", s => FateWarp(s, Sinnoh.Fx.FateRows.Teleport, 1350)),      // 도착 칸 위에서 떨어져 내려앉는 중(도착 칸의 분홍 고리)
            Screen("row-43-ask", s => FateBattle(s, SoloTrail.Fate, true)),      // 굴린 뒤 창: 독의 운명 줄 — 주사위 칩(첫째·둘째를 골랐다) · 디아루가 [1장]·[3장] · 아르세우스 칩 · [넘기기]
            Screen("row-45-pass3", s => FateBattlePlayed(s, SoloTrail.Fate, Sinnoh.Fx.FateRows.Play, 600)),     // 디아루가 3장: 세 장이 돌며 하나로 합쳐지는 중(금빛 둘레 — 아르세우스 섞임)
            Screen("row-43-reroll", s => FateBattlePlayed(s, SoloTrail.Fate, Sinnoh.Fx.FateRows.Reroll, 750)), // 재굴림: 독의 주사위 줄에서 고른 주사위가 글리치 중
            Screen("row-43-locked", s => FateBattlePlayed(s, SoloTrail.Fate, Sinnoh.Fx.FateRows.Reroll, 2100)), // 첫 주사위는 새 면으로 고정, 둘째가 글리치 중
            Screen("row-43-end", s => FateBattlePlayed(s, SoloTrail.Fate, Sinnoh.Fx.FateRows.Reroll, -200)),    // 결정 190: 단계 끝 200ms 전 — 모두 새 면으로 고정, 다시 세기
            Screen("battle-fate-button", Clean(s => FateView(s, false), "battle-dock")),   // 결정 80 N3 나: [운명의 변화 n/5](손패 훑어보기 — 결정 170로 독 차례 칸 맨 아래) · 운명 창이 열린 붐비는 차례 칸
            Screen("battle-fate-view", Fit(s => FateView(s, true))),                       // 누른 뒤: 배틀 화면 위의 손패 창 — 보기만(사용 단추 없음 · 「배틀 중에는 확인만 …」)
            Screen("row-44-ask", s => FateBattle(s, SoloTrail.FateGiratina, false)),   // 굴리기 전 창: 기라티나 [1장]
            Screen("row-44-shadow", s => FateBattlePlayed(s, SoloTrail.FateGiratina, Sinnoh.Fx.FateRows.Sacrifice, 1300)),   // 검은 그림자가 감싸고 체력이 빠진 뒤 위력 +1
            Screen("row-07-star", s => FateStar(s, 700)),                        // 다음 턴 시작: 떨어진 별 카드가 위 띠에서 [운명의 변화]로 날아오는 중(7행)
            Screen("row-07-star-flip", s => FateStar(s, 820)),                   // 내 손에 닿아 앞면으로 뒤집히는 중
            Screen("row-08-fate", s => FateRespawn(s, 520)),                    // 귀환 회복 끝: 운명 1장이 [운명의 변화]로 날아오는 중(8행 — 회복 단계 끝의 뒤에서 ms)
            Screen("row-98-challenge", Clean(s => PromptSeats(s, false, 0), "board-hud-prompt")),           // S41(좌석 둘 · 응답 시계 30초): 남의 도전 — 「행동」 칸 자리의 프롬프트 · 남은 시간 막대
            Screen("row-98-trade", Clean(s => PromptSeats(s, true, 0), "board-hud-prompt")),   // 남의 거래 제안: 받기·주기 두 줄 · [수락]·[거절] — 글 넘침 단정(M5 마감 검토: 1080에서 「주기」가 잘렸다)
            Screen("row-98-trade-many", Clean(s => PromptMany(s), "board-hud-prompt")),   // 결정 84 L8 나: 규토리·볼은 아이콘 + 수 — 받기 줄이 넘치면 넘친 조각을 감추고 「…」(마우스 오버 = 전체)
            Screen("row-98-timeout", s => PromptSeats(s, false, 30500)),
            // ── M5 「스탬프·도전·거래 화면」(S35·S36·S37 · 04 §8-8 47·51·52행 · 70행 스탬프 줄 · S40 무녀 2종) ──
            Screen("board-stamp", Fit(s => Stamps(s, 3))),                       // S37 스탬프 판: 셋 받음(규토리 → 약한 5장 → 규토리) · 다음 보상 금색 · 받은 기술 카드 5장
            Screen("board-stamp-none", Fit(s => Stamps(s, 0))),                  // 아직 하나도 없는 판
            Screen("board-stamp-full", Fit(s =>                                  // 일곱 다 받음: 기술 카드 15장(글자 넘침 단정의 가장 긴 판 — 아래 판을 덮지 않는지도)
            {
                Stamps(s, 7);
                s.Panel.Tick(3);
                var root = s.Made.Stage.Root;
                float bottom = root.Q("board-stamp-panel").worldBound.yMax, dock = root.Q("board-hud-dock").worldBound.yMin;
                if (bottom > dock) throw new InvalidOperationException("스탬프 판이 아래 판을 덮는다(판 아래 " + bottom + " > 아래 판 위 " + dock + ")");
            })),
            Screen("board-stamp-tip", s =>                                       // 칸에 마우스를 올린 툴팁(룰북 문구 + 혼자 하는 판의 갤럭시단)
            {
                var flow = Stamps(s, 3);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("board-stamp-slot-2"));
            }),
            Screen("row-47-drop", s => StampInto(s, 0, 180)),                    // 47행 Seek: 도장이 위에서 내려오는 중(1.8배)
            Screen("row-47-slam", s => StampInto(s, 0, Sinnoh.Fx.StampBeats.SlamAt + 40)),   // 쾅: 잉크가 번지고 판이 한 프레임 흔들린다
            Screen("row-47-rewards", Clean(s => StampInto(s, 0, 1500), "deal-stamp")),              // 스탬프 수가 오르고 보상(규토리 세 색)이 차례로 내려앉았다
            Screen("row-47-cards", s => StampInto(s, 1, 1300)),                  // 둘째 스탬프: 기술 카드 +5
            Screen("row-70-stamp", s =>                                          // 70행: 이긴 판의 결과 창에 스탬프 줄(혼자 하는 판 — 갤럭시단 승리 = 스탬프 2) + 그 위의 도장 덮개
            {
                var flow = Win(s);
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
                if (flow.Battle.Stamps.Count != 1 || flow.Stage.Root.Q("battle-result-stamp") == null) throw new InvalidOperationException("결과 창에 스탬프 줄이 없다");
            }),
            Screen("board-challenge", Fit(s =>                                   // S35 도전할 상대: 같은 칸(마을 안 — 거절 가능) · 원격(다른 신의 추종자)
            {
                var flow = Rivals(s);
                if (!flow.Hud.Press(BoardHud.Challenge) || !(flow.Stage.Current is ChallengeScreen)) throw new InvalidOperationException("[도전]이 상대 고르는 창을 열지 않았다");
            })),
            Screen("row-51-bang", s => Versus(s, 200)),                          // 51행 Seek: 「!」 · 띠가 들어옴
            Screen("row-51-slide", s => Versus(s, 650)),                         // 양쪽 얼굴이 미끄러져 들어오는 중
            Screen("row-51-vs", s => Versus(s, Sinnoh.Fx.VsBeats.VsAt + 60)),    // 가운데 VS가 박히는 순간(흰 빛)
            Screen("row-51-hold", s => Versus(s, 1800)),                         // 박힌 뒤
            Screen("board-trade-button", s =>                                    // 같은 칸의 상대: 아래 판에 [거래]
            {
                var flow = MyTurn(s, 2, st => TradeKit(s.Made.Ctx, st, false));
                if (!flow.Hud.Shows(BoardHud.Trade)) throw new InvalidOperationException("[거래]가 없다");
            }),
            Screen("board-trade", Fit(s => TradeOpen(s, false))),                // S36 거래 창: 아직 아무것도 올리지 않음
            Screen("board-trade-picked", Fit(s => TradePut(s, TradeOpen(s, false)))),   // 올린 것(금색 칩) · [제안] 켜짐
            Screen("board-trade-full", Fit(s =>
            {
                TradePut(s, TradeOpen(s, true));
                s.Panel.Tick(3);
                var root = s.Made.Stage.Root;
                if (root.Q("board-trade-panel").worldBound.yMax > root.Q("board-hud-dock").worldBound.yMin) throw new InvalidOperationException("거래 창이 아래 판을 덮는다");
            })),  // 가진 것이 많은 판(규토리 일곱 색 · 볼 넷 · 아이템 · 파티 여섯 — 글자 넘침 단정)
            Screen("board-trade-bad", Fit(s =>                                   // 맞지 않는 내용(포켓몬을 다 줌): 안내 줄 · [제안] 꺼짐
            {
                var trade = TradeOpen(s, false);
                var view = s.Made.Client.View;
                foreach (int id in view.Seats[s.Made.Client.Seat].Party)
                    if (id >= 0) trade.Set(0, R.TradeKind.Pokemon, id, 1);
                if (trade.CanPropose) throw new InvalidOperationException("파티를 다 주는 거래가 맞다고 나왔다");
            })),
            Screen("board-trade-wait", s =>                                      // 제안한 뒤: 「상대의 응답을 기다리는 중입니다.」
            {
                var trade = TradePut(s, TradeOpen(s, false));
                if (!trade.Propose()) throw new InvalidOperationException("[제안]이 거절됐다");
                Pass(s.Made, BoardReadyMs);
                s.Made.Layout(s.Width, s.Height);
                if (!s.Made.TradeWaiting) throw new InvalidOperationException("응답을 기다리는 중이 아니다");
            }),
            Screen("row-52-gather", s => SeatTraded(s, 350)),                        // 52행 Seek: 양쪽이 올린 것이 가운데로 모임
            Screen("row-52-cross", s => SeatTraded(s, 1100)),                        // 엇갈려 건너가는 중
            Screen("row-52-glow", s => SeatTraded(s, Sinnoh.Fx.TradeBeats.GlowAt + 200)),   // 받는 쪽이 빛남
            Screen("board-trade-offturn", s =>                                   // 남의 턴: 같은 신의 추종자와는 [거래]가 선다(결정 75 B5 나)
            {
                var flow = TheirTurn(s, st =>
                {
                    TradeKit(s.Made.Ctx, st, false);
                    st.Seats[0].Follower = st.Seats[1].Follower = R.SinnohGod.Dialga;
                });
                if (!flow.Hud.Shows(BoardHud.Trade)) throw new InvalidOperationException("남의 턴에 [거래]가 없다");
            }),
            Screen("board-trade-pick", Fit(s => TradePick(s, false))),           // 수락 때 줄 운명의 변화 고르기: 제안 내용 · 내 손패 · [수락] 꺼짐
            Screen("board-trade-pick-picked", Fit(s => TradePick(s, true))),     // 2장을 골랐다 — [수락] 켜짐
            Screen("board-trainer-platinum", s => Acolyte(s, "platinum_acolyte")),   // S40 무녀 2종 색(§5 #3 ③): 백금 무녀(다이아 무녀 그림의 색 바꿈)
            Screen("board-trainer-undivided", s => Acolyte(s, "undivided_acolyte")), // 비취 무녀(펄 무녀 그림의 색 바꿈)         // 시간 초과: 기본값(거절)이 저절로 눌리고 「시간 초과 · 사용 안 함」
            // ── 사당·균열(M5 「사당·균열 화면」 — 04 S31~S33 · §8-8 31~34행): 사당 길(SoloTrail.Shrine — 풀밭 사당 · 덱 맨 위 뜻밖의 선물 → 천상의 균열 → 평온한 순간) + 좌석 여럿 견본 ──
            Screen("board-shrine-button", s =>                                    // 사당 칸: 아래 판에 [공물] — 마우스를 올린 툴팁
            {
                var flow = ShrineBoard(s);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("board-hud-act-" + BoardHud.Offer));
            }),
            Screen("board-shrine-offer", Fit(s => ShrineOpen(s))),                // S31 사당 판(오른쪽 기둥): 단계 줄 셋 · 여행 점수 7 · [바치기](아직 안 고름 — 꺼짐)
            Screen("board-shrine-offer-named", Fit(s =>                           // 결정 80 K11: 길어진 사당 이름(「천관의 사당」)이 사당 판 제목에 들어가는지 — 좌석 둘 판의 내 턴
            {
                var flow = Seats(s, 2, st =>
                {
                    SoloTrail.PrepareShrine(s.Made.Ctx, st, 0);
                    st.Seats[0].Cell = s.Made.Ctx.Fixtures.Fixtures[s.Made.Ctx.Fixtures.Ids.IndexOf("shrine.coronet_shrine")].Cell;
                });
                for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
                {
                    EndActive(flow);
                    Pass(flow, BoardReadyMs * 8);
                }
                ShrineSettle(s, flow, f => f.InputOpen && f.Hud != null && f.Hud.Shows(BoardHud.Offer));
                ShrineOpen(s, flow);
            })),
            Screen("board-shrine-offer-picked", Fit(s => ShrinePicked(s))),       // 분홍(2단계) + 「7점 더 바치기」를 고른 때
            Screen("row-31-fly", s => ShrineOffered(s, Sinnoh.Fx.OfferBeats.FlyFromMs + 200)),          // [바치기] 뒤: 바친 규토리가 빨려 오르는 중 · 여행 점수가 줄어드는 중
            Screen("row-31-pillar", Fit(s => ShrineOffered(s, Sinnoh.Fx.OfferBeats.PillarAt + 300))),        // 2단계 빛기둥이 선 때
            Screen("row-31-cards", s => ShrineOffered(s, Sinnoh.Fx.OfferBeats.FlipAt(1) + 90)),         // 3장: 첫 장은 앞면 · 둘째 장이 앞면으로 펴지는 중 · 셋째 장은 뒷면으로 내려앉았다
            Screen("board-shrine-pick", Fit(s => ShrineChoice(s, ShrineScreen.Mode.Cards))),            // S32 3장 중 한 장 고르기(뜻밖의 선물 · 천상의 균열 · 평온한 순간)
            Screen("board-shrine-pick-tip", s =>                                  // 카드에 마우스를 올리면 카드 글
            {
                var shrine = ShrineChoice(s, ShrineScreen.Mode.Cards);
                s.Made.Stage.Tips.Show(s.Made.Stage.Root.Q("board-shrine-card-2"));
            }),
            Screen("row-32-bless", Fit(s =>                                       // 뜻밖의 선물을 고른 뒤(32행): 카드가 빛나고 「사당의 축복을 받았다!」
            {
                var shrine = ShrineChoice(s, ShrineScreen.Mode.Cards);
                if (!shrine.PickOption(0) || !shrine.Confirm()) throw new InvalidOperationException("카드 고르기가 거절됐다");
                ShrineInto(s, Sinnoh.Fx.ShrineRows.Blessing, Sinnoh.Fx.BlessBeats.BadgeAt + 250);
            })),
            Screen("board-shrine-apricorns", Fit(s =>                             // 받을 규토리 2개 중 검정 하나를 고른 때(1/2)
            {
                var shrine = ShrineChoice(s, ShrineScreen.Mode.Apricorns);
                if (!shrine.AddUnit((int)Sinnoh.Rules.ApricornColor.Black)) throw new InvalidOperationException("규토리 고르기가 거절됐다");
                s.Panel.Tick(3);
            })),
            Screen("board-shrine-follower", Fit(s => ShrineChoice(s, ShrineScreen.Mode.Follower))),     // S33 천상의 균열(혼자): 내 추종자 — 처음이라 「고르지 않기」가 골라져 있다
            Screen("row-34-flip", s => ShrineTally(s, Sinnoh.Fx.TallyBeats.FlipMs / 2)),                // 펄기아를 [확정]한 뒤: 좌석 줄이 뒤집히는 가운데
            Screen("row-34-tally", Fit(s => ShrineTally(s, Sinnoh.Fx.TallyBeats.LeadAt + 200))),        // 막대가 차고 펄기아 줄이 빛난다
            Screen("row-34-bolt", s => ShrineDescend(s, Sinnoh.Fx.DescendBeats.BoltAt + 30)),           // 신 강림: 시작의 사당으로 간 카메라 · 어두운 하늘 · 번개
            Screen("row-34-fall", s => ShrineDescend(s, Sinnoh.Fx.DescendBeats.FallFromMs + 700)),      // 펄기아 피규어가 내려오는 중
            Screen("row-34-land", s => ShrineDescend(s, Sinnoh.Fx.DescendBeats.LandAt + 150)),          // 내려앉음: 착지 고리
            Screen("row-34-spark", s =>
            {
                var flow = ShrineDescend(s, Sinnoh.Fx.DescendBeats.SparkAt + 300);
                if (flow.Hud.ShrineFx.MoveShown != 1) throw new InvalidOperationException("「이동력 +1」 꼬리표가 하나가 아니다: " + flow.Hud.ShrineFx.MoveShown + " · 단계 안 " + (flow.Fx.Sequencer.Current != null ? flow.Fx.Sequencer.LocalMs.ToString() : "-"));
            }),        // 추종자 칩 아래 「이동력 +1」
            Screen("row-34-chip", s =>                                            // 강림 뒤의 보드: 좌석 칩의 추종자 아이콘(마우스를 올린 툴팁) · 사당 판은 닫혔다
            {
                var flow = ShrineDescend(s, 4000 + BoardReadyMs);
                ShrineSettle(s, flow, f => f.Stage.Current == null);
                if (!flow.Hud.FollowerShown(0)) throw new InvalidOperationException("좌석 칩에 추종자 아이콘이 없다");
                if (flow.Hud.FollowTone(0) != Sinnoh.Rules.SinnohGod.Palkia) throw new InvalidOperationException("좌석 칩의 이름 바탕이 펄기아 색이 아니다(" + flow.Hud.FollowTone(0) + ")");
                var chipBox = flow.Stage.Root.Q("board-hud-seat-1").worldBound;
                var iconBox = flow.Stage.Root.Q("board-hud-seat-1-follower").worldBound;
                if (iconBox.width < 39f || iconBox.xMax > chipBox.xMax || iconBox.yMax > chipBox.yMax + 0.5f || iconBox.yMin < chipBox.yMin - 0.5f) throw new InvalidOperationException("좌석 칩의 신 아이콘이 작거나 칩 밖이다: " + iconBox + " · 칩 " + chipBox);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("board-hud-seat-1-follower"));
            }),
            // 좌석 여럿 견본(4인 이상 나눔 · 균열의 선택 현황 · 동률 · 전설 이벤트)
            Screen("row-32-share", Fit(s => ShrineSeats(s, 4, ShrineScreen.Mode.Share, null, Sinnoh.Rules.ShrineRules.TranquilMoment))),   // 4인: 평온한 순간을 받은 뒤 함께 받을 트레이너 1명
            Screen("row-32-beam", s =>                                            // 좌석 3을 고른 뒤: 사당 판에서 그 좌석 칩으로 가는 빛 · 「플레이어 3도 축복을 받았다!」
            {
                var shrine = ShrineSeats(s, 4, ShrineScreen.Mode.Share, null, Sinnoh.Rules.ShrineRules.TranquilMoment);
                if (!shrine.PickOption(1) || !shrine.Confirm()) throw new InvalidOperationException("나눌 좌석 고르기가 거절됐다");
                ShrineInto(s, Sinnoh.Fx.ShrineRows.Blessing, Sinnoh.Fx.BlessBeats.BeamFromMs + 250);
            }),
            Screen("row-34-seats", Fit(s =>                                       // 6인 균열: 좌석 2·3이 먼저 골랐다(완료) · 나는 디아루가를 고르는 중
            {
                var shrine = ShrineSeats(s, 6, ShrineScreen.Mode.Follower, null, Sinnoh.Rules.ShrineRules.CelestialFracture);
                var flow = s.Made;
                for (int seat = 1; seat <= 2; seat++)
                    if (!flow.AnswerSeat(seat, a => a.Kind == Sinnoh.Rules.AnswerKind.Pick)) throw new InvalidOperationException("좌석 " + (seat + 1) + "의 추종자 답이 거절됐다");
                ShrineSettle(s, flow, f => shrine.Open);
                if (!shrine.PickOption(1)) throw new InvalidOperationException("추종자 고르기가 거절됐다");
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
                // 6인의 좌석 줄까지 넣은 판이 아래 판을 덮지 않는다
                var panel = flow.Stage.Root.Q("board-shrine-panel").worldBound;
                var dock = flow.Stage.Root.Q("board-hud-dock").worldBound;
                if (panel.yMax > dock.yMin) throw new InvalidOperationException("사당 판이 아래 판을 덮는다: 판 아래 " + panel.yMax.ToString("0") + " > 아래 판 위 " + dock.yMin.ToString("0"));
            })),
            Screen("row-34-chips6", s =>                                          // 6인이 모두 공개한 뒤(결정 80 L13 나): 좌석 칩의 이름 바탕이 저마다의 신 색 + 큰 신 아이콘 — 위 띠가 상태 판과 겹치지 않는다
            {
                var shrine = ShrineSeats(s, 6, ShrineScreen.Mode.Follower, null, Sinnoh.Rules.ShrineRules.CelestialFracture);
                var flow = s.Made;
                var gods = new[] { Sinnoh.Rules.SinnohGod.Palkia, Sinnoh.Rules.SinnohGod.Dialga, Sinnoh.Rules.SinnohGod.Palkia, Sinnoh.Rules.SinnohGod.Giratina, Sinnoh.Rules.SinnohGod.Palkia, Sinnoh.Rules.SinnohGod.Giratina };
                for (int seat = 1; seat < 6; seat++)
                {
                    int god = (int)gods[seat];
                    if (!flow.AnswerSeat(seat, a => a.Kind == Sinnoh.Rules.AnswerKind.Pick && a.Target == god)) throw new InvalidOperationException("좌석 " + (seat + 1) + "의 추종자 답이 거절됐다");
                }
                ShrineSettle(s, flow, f => shrine.Open);
                if (!shrine.Press(Sinnoh.Rules.ChoiceAnswer.Pick((int)gods[0]))) throw new InvalidOperationException("추종자 [확정]이 거절됐다");
                ShrineSettle(s, flow, f => f.Hud != null && f.Hud.FollowerShown(5) && (f.Fx == null || f.Fx.Sequencer.IsEmpty));
                for (int seat = 0; seat < 6; seat++)
                    if (flow.Hud.FollowTone(seat) != gods[seat]) throw new InvalidOperationException("좌석 " + (seat + 1) + " 칩의 이름 바탕이 " + gods[seat] + " 색이 아니다(" + flow.Hud.FollowTone(seat) + ")");
                var last = flow.Stage.Root.Q("board-hud-seat-6").worldBound;
                var status = flow.Stage.Root.Q("board-hud-status").worldBound;
                if (last.xMax > status.xMin) throw new InvalidOperationException("좌석 칩이 상태 판과 겹친다: 칩 오른쪽 " + last.xMax.ToString("0") + " > 상태 판 왼쪽 " + status.xMin.ToString("0"));
                var whole = flow.Stage.Root.worldBound;
                var menu = flow.Stage.Root.Q("board-hud-menu").worldBound;
                if (status.xMax > whole.xMax + 0.5f || menu.xMax > status.xMax) throw new InvalidOperationException("상태 판이 화면 밖으로 밀렸거나 [메뉴]가 판 밖이다: 판 오른쪽 " + status.xMax.ToString("0") + " · [메뉴] 오른쪽 " + menu.xMax.ToString("0") + " · 화면 " + whole.xMax.ToString("0"));
                var bad = WorkUi.Overflows(flow.Stage.Root.Q("board-hud-seats"));
                if (bad.Count > 0) throw new InvalidOperationException("글자 넘침(좌석 칩) " + bad.Count + "곳: " + string.Join(" / ", bad));
            }),
            Screen("row-34-wait", Fit(s =>                                        // 내가 [확정]한 뒤: 내 추종자만 보이고 남을 기다린다
            {
                var shrine = ShrineSeats(s, 4, ShrineScreen.Mode.Follower, null, Sinnoh.Rules.ShrineRules.CelestialFracture);
                bool pickedGod = shrine.PickOption(2);
                if (!pickedGod || !shrine.Confirm()) throw new InvalidOperationException("추종자 [확정]이 거절됐다: 고름 " + pickedGod + " · 답 " + shrine.Options.Count + " · 열림 " + shrine.Open + " · 꼴 " + shrine.Shown + " · 거절 " + (s.Made.Client.LastRejected != null ? s.Made.Client.LastRejected.Reason.ToString() : "-"));
                ShrineSettle(s, s.Made, f => f.Stage.Current is ShrineScreen sh && sh.Shown == ShrineScreen.Mode.Wait && f.BoardOpen);
            })),
            Screen("row-34-tie", Fit(s =>                                         // 2인 동률(디아루가 1 · 펄기아 1): 뽑은 내가 같은 판에서 강림할 신을 고른다(결정 75 A4 가)
            {
                var shrine = ShrineSeats(s, 2, ShrineScreen.Mode.Follower, null, Sinnoh.Rules.ShrineRules.CelestialFracture);
                var flow = s.Made;
                if (!flow.AnswerSeat(1, a => a.Kind == Sinnoh.Rules.AnswerKind.Pick && a.Target == (int)Sinnoh.Rules.SinnohGod.Dialga)) throw new InvalidOperationException("좌석 2의 추종자 답이 거절됐다");
                ShrineSettle(s, flow, f => shrine.Open);
                if (!shrine.Press(Sinnoh.Rules.ChoiceAnswer.Pick((int)Sinnoh.Rules.SinnohGod.Palkia))) throw new InvalidOperationException("추종자 [확정]이 거절됐다");
                ShrineSettle(s, flow, f => f.Stage.Current is ShrineScreen sh && sh.Shown == ShrineScreen.Mode.Tie && sh.Open);
            })),
            Screen("row-33-dark", s => ShrineLegend(s, Sinnoh.Fx.LegendBeats.DarkMs)),                       // 4인 전설 이벤트(서리의 노래): 카메라가 서리노래 사당으로 · 하늘이 어두워짐
            Screen("row-33-fall", s => ShrineLegend(s, Sinnoh.Fx.LegendBeats.FallFromMs + 600)),             // 프리져 피규어가 내려오는 중
            Screen("row-33-land", s => ShrineLegend(s, Sinnoh.Fx.LegendBeats.LandAt + 120)),                 // 내려앉음: 착지 고리
            Screen("row-33-rain", s => ShrineLegend(s, Sinnoh.Fx.LegendBeats.RainFromMs + 420)),             // 좌석 칩 위로 파랑 규토리가 차례로 떨어지고 「+3」
            // 나머지 카드의 고르기(S32)
            Screen("board-shrine-stews", Fit(s =>                                 // 화려한 만찬 2단계: 스튜 둘 고르기(하나 고른 때)
            {
                var shrine = ShrineSeats(s, 2, ShrineScreen.Mode.Stews, null, Sinnoh.Rules.ShrineRules.SplendidBanquet);
                if (!shrine.ToggleUnit(s.Made.Ctx.Items.Ids.IndexOf(Sinnoh.Rules.CraftRules.ZestyStew))) throw new InvalidOperationException("스튜 고르기가 거절됐다");
                s.Panel.Tick(3);
            })),
            Screen("board-shrine-exchange", Fit(s => ShrineSeats(s, 2, ShrineScreen.Mode.Exchange, null, Sinnoh.Rules.ShrineRules.EquivalentExchange))),   // 동등한 교환: 교환할 포켓몬 · [교환하지 않기]
            Screen("board-shrine-exchange-card", Fit(s =>                         // 교환할 야생 카드 목록(쪽 넘김 — 덱 / 버림 더미)
            {
                var shrine = ShrineSeats(s, 2, ShrineScreen.Mode.Exchange, null, Sinnoh.Rules.ShrineRules.EquivalentExchange);
                if (!shrine.PickOption(0) || !shrine.Confirm()) throw new InvalidOperationException("교환할 포켓몬 고르기가 거절됐다");
                ShrineSettle(s, s.Made, f => f.Stage.Current is ShrineScreen sh && sh.Shown == ShrineScreen.Mode.ExchangeCard && sh.Open);
                if (!((ShrineScreen)s.Made.Stage.Current).PickOption(1)) throw new InvalidOperationException("야생 카드 고르기가 거절됐다");
                s.Panel.Tick(3);
            })),
            Screen("board-shrine-move", Fit(s => ShrineSeats(s, 2, ShrineScreen.Mode.Move, ShrineDiscards, Sinnoh.Rules.ShrineRules.AncientWisdom))),   // 고대의 지혜: 버려진 포켓몬의 기술 목록 · [배우지 않기]
            Screen("board-shrine-teach", Fit(s =>                                 // 배울 기술을 고른 뒤: 가르칠 포켓몬 · 바꿀 기술 칸
            {
                var shrine = ShrineSeats(s, 2, ShrineScreen.Mode.Move, ShrineDiscards, Sinnoh.Rules.ShrineRules.AncientWisdom);
                if (!shrine.PickOption(0) || !shrine.Confirm()) throw new InvalidOperationException("배울 기술 고르기가 거절됐다");
                ShrineSettle(s, s.Made, f => f.Stage.Current is ShrineScreen sh && sh.Shown == ShrineScreen.Mode.Teach && sh.Open);
            })),
            Screen("board-shrine-teach-tip", Fit(s =>                             // 결정 186: 칸 1에 기술(전설의 고유 기술)이 든 포켓몬 — 바꿀 원래 기술 칩에 마우스(이름 + 효과)
            {
                int given = -1;
                var shrine = ShrineSeats(s, 2, ShrineScreen.Mode.Move, st =>
                {
                    ShrineDiscards(st);
                    var ctx = s.Made.Ctx;
                    given = ctx.Cards.Pokemon[ctx.Deck(Sinnoh.Rules.DeckId.Legendary).Cards[0].Card].Signature;
                    st.Pokemon[st.Seats[0].Party[0]].Moves[0] = given;
                }, Sinnoh.Rules.ShrineRules.AncientWisdom);
                if (!shrine.PickOption(0) || !shrine.Confirm()) throw new InvalidOperationException("배울 기술 고르기가 거절됐다");
                ShrineSettle(s, s.Made, f => f.Stage.Current is ShrineScreen sh && sh.Shown == ShrineScreen.Mode.Teach && sh.Open);
                var teach = (ShrineScreen)s.Made.Stage.Current;
                VisualElement chip = null;
                for (int i = 0; i < teach.Options.Count && chip == null; i++)
                    if (teach.Options[i].Kind == R.AnswerKind.Pick && s.Made.Client.View.Pokemon[teach.Options[i].Target].Moves[teach.Options[i].Arg] == given)
                        chip = s.Made.Stage.Root.Q("board-shrine-opt-" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                MoveTipShown(s, chip, given, s.Made.Stage.Root.Q("board-shrine-panel"));
            })),
            Screen("board-hud-party-1", s => PartyOf(s, 1)),                     // 아래 판 파티 1·3·5·6마리(사용자 지적 11-16 — 5·6마리에서 이름·체력·스피드가 잘리지 않게)
            Screen("board-hud-party-3", s => PartyOf(s, 3)),
            Screen("board-hud-party-5", s => PartyOf(s, 5)),
            Screen("board-hud-party-6", s => PartyOf(s, 6)),
            // 파티 칸의 상태 이상(결정 180): 첫 칸 독 + 마비 · 끝 칸(스타팅) 화상 — 배틀과 같은 그림이 도트 위 칸 안에, 정비 판(S18) · 남 좌석 판(S39)도 같은 그림
            Screen("board-hud-party-status", s => PartyOf(s, 6, status: true)),
            Screen("board-hud-party-status-3", s => PartyOf(s, 3, status: true)),
            Screen("board-hud-party-status-tip", s => { PartyOf(s, 6, status: true); StatusTip(s, "board-hud-mon-0-status-poisoned"); }),
            Screen("board-party-status", Fit(s => { PartyOf(s, 6, status: true); PartyStatus(s, -1); })),
            Screen("board-party-other-status", Fit(s => PartyStatus(s, 1))),
            Screen("board-party-max", Fit(PartyMax)),                            // 정비 판의 가장 긴 열(PlayMode LeftoverFlowTests): 전설 셋 — 상태 둘 · 빈 칸 둘 = 전설 특수기술 · 지닌물건 둘 · 유물 [폼체인지] · 배우는 타입 넷 — 가방 칸이 판 안
            // 내 자원 줄(결정 98 — 아래 판 왼쪽 위 띠: 규토리 일곱 색 · 여행 점수 · 볼 넷이 늘 보인다): 기본 판 · 두 자리 수 + 파티 여섯 · 6인 · UB 6인(칩 판) · 튜토리얼 S203(앵커)
            Screen("board-hud-res", s => { ToBoard(s, BoardReadyMs); ResClear(s); }),
            Screen("board-hud-res-rich", s => PartyOf(s, 6, true)),
            Screen("board-hud-res-6", s => { Seats(s, 6); ResClear(s); }),
            Screen("board-hud-res-ub6", s => { Gauge(s, UbGame(s, null, 6), false); ResClear(s); }),
            Screen("board-hud-res-tut", s => { TutAt(s, "S203"); ResClear(s); }),
        };

        // ── 운명·응답(M5 「운명·응답 화면」) ──

        // 운명 길: 셋업 뒤 6장째 버리기 창이 열린 순간(pick = 가장 나중에 받은 카드를 골랐다)
        static GameFlow FateDiscard(UiShot s, bool pick)
        {
            var flow = Trail(s, SoloTrail.Fate, f => f.Top is FateScreen fs && fs.Discarding && f.BoardOpen);
            var screen = (FateScreen)flow.Top;
            if (pick && !screen.Pick(screen.Hand.Count - 1)) throw new InvalidOperationException("새 카드를 고르지 못했다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            return flow;
        }

        // 운명 길: 버린 뒤 내 맵 행동 — [운명의 변화]로 손패 창을 열고 펄기아를 고른다(warp = [1장 사용]까지 눌러 보드에서 칸을 고르는 중)
        static GameFlow FateHand(UiShot s, bool warp)
        {
            var flow = Trail(s, SoloTrail.Fate, f => f.Client.FirstChoice() != null && f.Client.FirstChoice().Kind == Sinnoh.Rules.ChoiceKind.MapAction && f.InputOpen);
            if (!flow.OpenFate()) throw new InvalidOperationException("손패 창이 열리지 않았다");
            var screen = (FateScreen)flow.Top;
            int palkia = -1;
            for (int i = 0; i < screen.Hand.Count; i++)
                if (Sinnoh.Rules.FateRules.KindOf(flow.Ctx, screen.Hand[i]) == Sinnoh.Rules.FateCard.Palkia) palkia = i;
            if (palkia < 0 || !screen.Pick(palkia)) throw new InvalidOperationException("펄기아가 손패에 없다");
            if (warp)
            {
                if (!screen.Use(1) || !flow.Hud.Warping) throw new InvalidOperationException("[1장 사용](펄기아)이 거절됐다");
                flow.Hud.Hover(flow.Hud.Reach[flow.Hud.Reach.Count - 1]);
            }
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            return flow;
        }

        // 운명 길: 펄기아로 가장 먼 칸(길잡이와 같은 답)을 누른 뒤 그 행의 단계 시작에서 ms
        static void FateWarp(UiShot s, int row, int ms)
        {
            var flow = FateHand(s, true);
            var reach = flow.Hud.Reach;
            if (!flow.Hud.Warp(reach[reach.Count - 1])) throw new InvalidOperationException("순간이동 칸이 거절됐다");
            FateInto(s, flow, row, ms);
        }

        // 대기열에서 그 행의 첫 단계를 찾아 그 시작에서 ms 지난 순간으로
        static void FateInto(UiShot s, GameFlow flow, int row, int ms)
        {
            var step = Queued(flow, st => st.Row == row);
            if (step == null) throw new InvalidOperationException(row + "행 단계가 대기열에 없다");
            TickedInto(s, flow, step, ms < 0 ? step.Ms + ms : ms);   // 음수 = 단계 끝에서 거꾸로
        }

        // 그 단계의 시작에서 ms 지난 순간으로 — 단계 시작에서 한 번 배치를 세운 뒤 보낸다(운명 카드가 날아가는 [운명의 변화] 단추의 자리는 배치에서 읽는다 — 한 번에 길게 보내면 아직 배치 전이다)
        static void TickedInto(UiShot s, GameFlow flow, Sinnoh.Fx.FxStep step, int ms)
        {
            flow.Stage.Tweens.Advance(flow.MsUntil(st => ReferenceEquals(st, step)));
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            flow.Stage.Tweens.Advance(1);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            flow.Stage.Tweens.Advance(Math.Max(0, ms - 1));
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
        }

        // 운명 길의 배틀: 내 운명 창이 열린 순간(dice = 주사위 칩 첫째·둘째를 골랐다)
        static GameFlow FateBattle(UiShot s, SoloTrail trail, bool dice)
        {
            var window = trail == SoloTrail.FateGiratina ? Sinnoh.Rules.Window.BeforeRoll : Sinnoh.Rules.Window.AfterRoll;   // 디아루가 = 굴린 뒤 창 · 기라티나 = 굴리기 전 창
            var flow = Trail(s, trail, f => Battle(f, b => b.Open && b.AskingFate && f.Client.FirstChoice().Window == window));
            var screen = (BattleScreen)flow.Stage.Current;
            if (dice && (!screen.ToggleFateDie(0) || !screen.ToggleFateDie(1))) throw new InvalidOperationException("주사위 칩이 거절됐다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (dice)
                for (int i = 1; i <= 2; i++)
                {
                    var chip = flow.Stage.Root.Q("battle-fate-die-" + i);
                    if (chip == null || !chip.ClassListContains("sn-battle-fate-die-picked"))
                        throw new InvalidOperationException("고른 주사위 칩 " + i + "이 표시되지 않았다(고른 것 " + string.Join(",", screen.FateDice) + ")");
                }
            return flow;
        }

        // 결정 80 N3 나: 배틀 중 손패 훑어보기 — 운명 길의 배틀(운명 창이 열린 순간)에서 기둥의 [운명의 변화 n/5] · open이면 누른 뒤의 보기만 하는 손패 창
        static GameFlow FateView(UiShot s, bool open)
        {
            var flow = FateBattle(s, SoloTrail.Fate, false);
            var screen = (BattleScreen)flow.Stage.Current;
            if (!screen.FateOn || flow.Stage.Root.Q(BattleScreen.FateButton) == null) throw new InvalidOperationException("배틀 기둥의 [운명의 변화]가 없거나 꺼져 있다");
            if (!open) return flow;
            if (!flow.OpenFate() || !(flow.Top is FateScreen fate)) throw new InvalidOperationException("배틀 화면 위에 손패 창이 열리지 않았다");
            if (fate.Hand.Count == 0) throw new InvalidOperationException("손패가 비어 있다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (flow.Stage.Root.Q("board-fate-use-1") != null || flow.Stage.Root.Q("board-fate-close") == null) throw new InvalidOperationException("배틀 중 손패 창은 보기만 한다([사용] 없음 · [닫기] 있음)");
            return flow;
        }

        // 운명 창에서 길잡이의 답(디아루가 3장 · 기라티나 1장)을 독으로 누른 뒤 그 행의 단계 시작에서 ms
        static void FateBattlePlayed(UiShot s, SoloTrail trail, int row, int ms)
        {
            var flow = FateBattle(s, trail, false);
            var screen = (BattleScreen)flow.Stage.Current;
            var choice = flow.Client.FirstChoice();
            var pilot = trail.Pilot();
            var answer = pilot.Decide(flow.Ctx, flow.Client.View, choice, flow.Client.Legal(choice));
            if (answer == null || answer.Kind != Sinnoh.Rules.AnswerKind.PlayFate || !screen.Press(answer)) throw new InvalidOperationException("운명 카드가 거절됐다: " + answer);
            FateInto(s, flow, row, ms);
        }

        // 운명 길: 다음 턴 시작의 떨어진 별 운명(7행)이 대기열에 들어온 뒤 그 단계에서 ms
        static void FateStar(UiShot s, int ms)
        {
            var flow = s.Flow();
            var pilot = FlowPilot.Start(flow, SoloTrail.Fate);
            flow.Layout(s.Width, s.Height);
            bool Star() => flow.Fx != null && Queued(flow, st => st.Row == Sinnoh.Fx.FateRows.Star) != null;
            void Wait(int x)
            {
                flow.Tick(x);
                if (!Star()) flow.Stage.Tweens.Advance(x);
            }
            if (!FlowPilot.Run(flow, pilot, Star, Wait, BattleStepMs, BattleStepMax * 10)) throw new InvalidOperationException("fate: 떨어진 별 운명까지 가지 못했다");
            if (flow.Stage.Current is BattleScreen battle)   // 배틀로 턴이 끝났다 — 다음 턴 시작은 결과 창 뒤
            {
                // 진 배틀이면 떨어진 별 운명이 배틀 끝과 같은 갱신에 온다(대기열에 든 때 결과 창은 아직 없다) — 결과 창이 설 때까지 보낸다
                for (int i = 0; i < BattleStepMax * 10 && !battle.ShowsResult; i++) Pass(flow, BattleStepMs);
                if (!battle.Confirm()) throw new InvalidOperationException("fate: 결과 창 [확인]이 거절됐다");
            }
            FateInto(s, flow, Sinnoh.Fx.FateRows.Star, ms);
        }

        // 조우 배틀에 진 뒤 결과 창 [확인] → 다음 내 턴 시작의 귀환 회복(8행): 그 회복 단계(운명 1장이 실린) 끝에서 back ms 앞
        static void FateRespawn(UiShot s, int back)
        {
            var flow = s.Flow();
            var pilot = FlowPilot.Start(flow, SoloTrail.Defeat);
            flow.Layout(s.Width, s.Height);
            if (!FlowPilot.Run(flow, pilot, () => FxShots.WhiteoutResult(flow, pilot), x => Pass(flow, x), BattleStepMs, BattleStepMax * 10)) throw new InvalidOperationException("defeat: 결과 창까지 가지 못했다");
            if (!FlowPilot.Step(flow, pilot, true)) throw new InvalidOperationException("defeat: 결과 창 [확인]이 거절됐다");
            var step = Queued(flow, st => st.Row == 80 && Sinnoh.Fx.FateRows.First<Sinnoh.Rules.FateDealt>(st) != null);
            if (step == null) throw new InvalidOperationException("귀환 회복에 운명 1장이 실리지 않았다");
            TickedInto(s, flow, step, step.Ms - back);
        }

        // 좌석 둘 확인 판(응답 시계 30초): 내 턴을 끝내고 좌석 2가 같은 칸에서 도전·거래를 건다. late = 그 뒤 세션 시각을 더 보낸다(시간 초과)
        static GameFlow PromptSeats(UiShot s, bool trade, int late, bool many = false)
        {
            var flow = s.Flow();
            var clocks = new int[(int)Sinnoh.Rules.ClockKind.Respond + 1];
            clocks[(int)Sinnoh.Rules.ClockKind.Respond] = 30000;
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings(), 2, Seed, AiSeed, st =>
            {
                st.Seats[1].Cell = st.Seats[0].Cell;
                st.Seats[1].Apricorns[(int)Sinnoh.Rules.ApricornColor.Blue] = 2;
                if (!many) return;
                // 결정 84 L8 나 견본: 좌석 2가 일곱 색 규토리 · 볼 셋 종류를 주고 내 빨강 · 몬스터볼 · 여행 점수를 받는다(받기 줄이 넘친다)
                for (int c = 0; c < st.Seats[1].Apricorns.Length; c++) st.Seats[1].Apricorns[c] = Math.Max(st.Seats[1].Apricorns[c], 1);
                for (int b = 0; b < 3; b++) st.Seats[1].Balls[b] = Math.Max(st.Seats[1].Balls[b], 2);
                st.Seats[0].Apricorns[(int)Sinnoh.Rules.ApricornColor.Red] = Math.Max(st.Seats[0].Apricorns[(int)Sinnoh.Rules.ApricornColor.Red], 1);
                st.Seats[0].Balls[(int)Sinnoh.Rules.Ball.Regular] = Math.Max(st.Seats[0].Balls[(int)Sinnoh.Rules.Ball.Regular], 1);
                st.Seats[0].Jp = Math.Max(st.Seats[0].Jp, 3);
            }, false, clocks);
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            for (int k = 0; k < 3 && flow.Client.View.ActiveSeat != 1; k++)   // 좌석 2의 턴까지(첫 좌석은 판마다 다르다)
            {
                EndActive(flow);
                flow.Stage.Tweens.Advance(BoardReadyMs * 4);
            }
            bool asked;
            if (trade)
            {
                var offer = Sinnoh.Rules.ChoiceAnswer.Of(Sinnoh.Rules.AnswerKind.ProposeTrade, flow.Client.Seat);
                if (!many) offer.Values.AddRange(new[] { 0, (int)Sinnoh.Rules.TradeKind.Apricorn, (int)Sinnoh.Rules.ApricornColor.Blue, 2, 1, (int)Sinnoh.Rules.TradeKind.Apricorn, (int)Sinnoh.Rules.ApricornColor.Red, 1 });
                else
                {
                    for (int c = 0; c < 7; c++) offer.Values.AddRange(new[] { 0, (int)Sinnoh.Rules.TradeKind.Apricorn, c, 1 });
                    for (int b = 0; b < 3; b++) offer.Values.AddRange(new[] { 0, (int)Sinnoh.Rules.TradeKind.Ball, b, 1 });
                    offer.Values.AddRange(new[] { 1, (int)Sinnoh.Rules.TradeKind.Apricorn, (int)Sinnoh.Rules.ApricornColor.Red, 1, 1, (int)Sinnoh.Rules.TradeKind.Jp, -1, 3, 1, (int)Sinnoh.Rules.TradeKind.Ball, (int)Sinnoh.Rules.Ball.Regular, 1 });   // 정규 순서(주는 쪽 · 종류 · 키)
                }
                asked = flow.AnswerSeatWith(1, offer);
            }
            else asked = flow.AnswerSeat(1, a => a.Kind == Sinnoh.Rules.AnswerKind.Challenge && a.Target == flow.Client.Seat);
            if (!asked) throw new InvalidOperationException("좌석 2의 " + (trade ? "거래 제안" : "도전") + "이 거절됐다");
            // 도전이면 VS 연출(51행 — 2800) 뒤에 프롬프트가 선다
            for (int k = 0; k < 40 && !flow.Hud.Prompting; k++)
            {
                flow.Stage.Tweens.Advance(250);
                flow.Tick(250);
            }
            if (!flow.Hud.Prompting) throw new InvalidOperationException("응답 프롬프트가 뜨지 않았다");
            if (late > 0)
            {
                // 시계는 방장 멈춤(연출) 뒤에 시작한다 — 시간 초과 답이 날 때까지 세션 시각을 보낸다
                for (int k = 0; k < 120 && flow.TimedOutAtMs == long.MinValue; k++) flow.Tick(late / 30);
                flow.Tick(300);
                flow.Stage.Tweens.Advance(BoardReadyMs);
                if (!flow.Hud.TimeoutShown) throw new InvalidOperationException("시간 초과 표시가 뜨지 않았다");
            }
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            return flow;
        }

        // 결정 84 L8 나: 받기 열 조각(일곱 색 규토리 · 볼 셋) · 주기 셋(빨강 · 몬스터볼 · 여행 점수) — 받기 줄은 「…」로 끝나야 한다
        static GameFlow PromptMany(UiShot s)
        {
            var flow = PromptSeats(s, true, 0, true);
            if (!flow.Hud.PromptCut("board-hud-prompt-line")) throw new InvalidOperationException("받기 줄이 넘치지 않았다(「…」 없음)");
            if (flow.Hud.PromptCut("board-hud-prompt-line2")) throw new InvalidOperationException("주기 줄(셋)이 잘렸다");
            return flow;
        }

        // 아래 판의 파티 n마리(막 길의 전설 다섯 + 스타팅 포켓몬에서 앞 n마리): 칸 안의 이름·체력·스피드가 칸 밖으로 넘치지 않는지 단정한다
        // 파티 여섯으로 첫 야생 배틀에 들어간 판: 출전 포켓몬 고르기 줄에 여섯 칸(이름이 다섯 글자인 한카리아스·레지기가스 포함)
        static void SendInSix(UiShot s)
        {
            var flow = s.Flow();
            var pilot = FlowPilot.Start(flow, SoloTrail.Catch);
            flow.Rebase(st =>
            {
                var seat = st.Seats[0];
                seat.OpenSlots = 6;
                foreach (string key in new[] { "445:0", "486:0", "462:0", "472:0", "483:2" })
                    Sinnoh.Rules.PokemonRules.AddToParty(seat, Sinnoh.Rules.PokemonRules.Spawn(st, flow.Ctx.Cards.PokemonIds.IndexOf(key), 0, (int)Sinnoh.Rules.DeckId.Legendary));
            });
            flow.Layout(s.Width, s.Height);
            if (!FlowPilot.Run(flow, pilot, () => Battle(flow, b => b.Open && b.Bench.Count >= 6), ms => Pass(flow, ms), BattleStepMs, BattleStepMax * 10, true))
                throw new InvalidOperationException("파티 여섯의 출전 포켓몬 고르기까지 가지 못했다(화면 " + (flow.Stage.Current != null ? flow.Stage.Current.Name : "보드") + ")");
        }

        static void PartyOf(UiShot s, int n, bool rich = false, bool status = false)
        {
            var flow = s.Flow();
            var pilot = FlowPilot.Start(flow, SoloTrail.Kit);
            flow.Rebase(st =>
            {
                var seat = st.Seats[0];
                seat.OpenSlots = 6;
                int starter = seat.Party[0];
                for (int i = 0; i < seat.Party.Length; i++) seat.Party[i] = -1;
                for (int i = 0; i < n - 1 && i < SoloTrail.Team.Length; i++)
                    Sinnoh.Rules.PokemonRules.AddToParty(seat, Sinnoh.Rules.PokemonRules.Spawn(st, flow.Ctx.Cards.PokemonIds.IndexOf(SoloTrail.Team[i]), 0, (int)Sinnoh.Rules.DeckId.Legendary));
                Sinnoh.Rules.PokemonRules.AddToParty(seat, starter);
                if (status) Afflicted(st, 0);
                if (!rich) return;
                for (int c = 0; c < seat.Apricorns.Length; c++) seat.Apricorns[c] = 12;   // 자원 줄의 가장 넓은 꼴: 두 자리 수 · 여행 점수 세 자리
                for (int b = 0; b < seat.Balls.Length; b++) seat.Balls[b] = 10;
                seat.Jp = 120;
            });
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var party = root.Q("board-hud-party");
            for (int i = 0; i < n; i++)
            {
                var tile = root.Q("board-hud-mon-" + i);
                if (tile == null) throw new InvalidOperationException("파티 칸 " + i + "이 없다");
                foreach (string part in new[] { "-hp", "-speed", "-name", "-passive" })   // -passive = 우두머리 · 비스트 부스트 칩 줄(전설만 — T27)
                {
                    var e = root.Q("board-hud-mon-" + i + part);
                    if (e == null && part == "-passive") continue;
                    if (e == null) throw new InvalidOperationException("파티 칸 " + i + part + "이 없다");
                    var box = e.worldBound;
                    if (box.xMax > tile.worldBound.xMax + 0.5f || box.xMax > party.worldBound.xMax + 0.5f || box.yMax > party.worldBound.yMax + 0.5f)
                        throw new InvalidOperationException("파티 " + n + "마리: 칸 " + i + part + "이 칸 밖으로 넘친다(" + box + " · 칸 " + tile.worldBound + ")");
                    if (part != "-name") continue;
                    var label = (Label)e;
                    var need = label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                    if (need.x > box.width + 1f) throw new InvalidOperationException("파티 " + n + "마리: 칸 " + i + " 이름 「" + label.text + "」이 잘린다(" + need.x + " > " + box.width + ")");
                }
                foreach (string pill in new[] { "-hp", "-speed" })
                {
                    var e = root.Q("board-hud-mon-" + i + pill);
                    foreach (var label in e.Query<Label>().ToList())
                    {
                        var need = label.MeasureTextSize(label.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                        if (need.x > label.worldBound.width + 1f) throw new InvalidOperationException("파티 " + n + "마리: 칸 " + i + pill + " 숫자 「" + label.text + "」이 잘린다");
                    }
                }
            }
            HudStatus(s, flow);
            ResClear(s);
        }

        // 결정 180 견본: 그 좌석 파티의 첫 칸 = 독 + 마비(동시에 둘 — Status2), 끝 칸 = 화상(파티가 하나면 첫 칸만)
        static void Afflicted(Sinnoh.Rules.GameState st, int seat)
        {
            var party = st.Seats[seat].Party;
            int first = -1, last = -1;
            foreach (int id in party)
            {
                if (id < 0) continue;
                if (first < 0) first = id;
                last = id;
            }
            st.Pokemon[first].Status = Sinnoh.Rules.StatusEffect.Poisoned;
            st.Pokemon[first].Status2 = Sinnoh.Rules.StatusEffect.Paralysed;
            if (last != first) st.Pokemon[last].Status = Sinnoh.Rules.StatusEffect.Burned;
        }

        // 그 칸의 상태 아이콘(결정 180)이 걸린 상태와 같은 수 · 같은 순서 · 지금 언어의 그림(IconArt)인지, 자리가 칸 안인지 단정한다. 아이콘 수
        static int StatusIconsCheck(GameFlow flow, VisualElement box, VisualElement within, Sinnoh.Rules.PokemonState pk, string what)
        {
            var want = new List<Sinnoh.Rules.StatusEffect>();
            Sinnoh.Rules.StatusRules.Statuses(pk, want);
            var icons = box != null ? box.Query(className: "sn-status-icon").ToList() : new List<VisualElement>();
            if (icons.Count != want.Count) throw new InvalidOperationException(what + ": 상태 아이콘 " + icons.Count + " ≠ 걸린 상태 " + want.Count);
            for (int k = 0; k < want.Count; k++)
            {
                var tex = GenAssets.Load<Texture2D>(IconArt.Status(want[k]));
                if (tex == null || icons[k].style.backgroundImage.value.texture != tex) throw new InvalidOperationException(what + ": 상태 아이콘 " + k + "이 " + IconArt.Status(want[k]) + " 그림이 아니다");
                var b = icons[k].worldBound;
                var w = within.worldBound;
                if (b.width <= 0f || b.xMin < w.xMin - 0.5f || b.xMax > w.xMax + 0.5f || b.yMin < w.yMin - 0.5f || b.yMax > w.yMax + 0.5f)
                    throw new InvalidOperationException(what + ": 상태 아이콘 " + icons[k].name + "이 칸 밖이다(" + b + " · 칸 " + w + ")");
            }
            return want.Count;
        }

        // 아래 판 파티 칸(결정 180): 상태가 있는 칸만 아이콘 줄이 있고, 아이콘은 도트 위(칸 안 · 글 칸을 덮지 않는다)
        static void HudStatus(UiShot s, GameFlow flow)
        {
            var root = flow.Stage.Root;
            var me = flow.Client.View.Seats[flow.Client.Seat];
            int total = 0;
            for (int i = 0; i < me.Party.Length; i++)
            {
                if (me.Party[i] < 0) continue;
                var pk = flow.Client.View.Pokemon[me.Party[i]];
                var tile = root.Q("board-hud-mon-" + i);
                var row = root.Q("board-hud-mon-" + i + "-status");
                if ((row != null) != (pk.Status != Sinnoh.Rules.StatusEffect.None)) throw new InvalidOperationException("파티 칸 " + i + " 상태 줄 " + (row != null ? "있음" : "없음") + " · 상태 " + pk.Status);
                if (row == null) continue;
                total += StatusIconsCheck(flow, row, tile, pk, "파티 칸 " + i);
                var info = tile.Q(className: "sn-hud-mon-info");
                foreach (var icon in row.Children())
                    if (info != null && icon.worldBound.Overlaps(info.worldBound)) throw new InvalidOperationException("파티 칸 " + i + ": 상태 아이콘이 이름 · 체력 칸을 덮는다");
            }
            if (total > 0) UnityEngine.Debug.Log("[UiShots] party-status 아이콘 " + total);
        }

        // 아이콘의 마우스 오버(이름 + 짧은 효과): 툴팁이 화면 안 · 아래 판을 덮지 않는다
        static void StatusTip(UiShot s, string icon)
        {
            var flow = s.Made;
            var root = flow.Stage.Root;
            var target = root.Q(icon) ?? throw new InvalidOperationException("요소 없음: " + icon);
            flow.Stage.Tips.Show(target);
            s.Panel.Tick(3);
            var t = flow.Stage.Tips.Element.worldBound;
            if (t.yMin < -0.5f || t.yMax > root.worldBound.yMax + 0.5f || t.xMin < -0.5f || t.xMax > root.worldBound.xMax + 0.5f || t.Overlaps(root.Q("board-hud-dock").worldBound))
                throw new InvalidOperationException("상태 툴팁이 화면 밖이거나 아래 판을 덮는다(" + t + ")");
            var text = string.Join(" ", flow.Stage.Tips.Element.Query<Label>().ToList().ConvertAll(l => l.text.Replace("⁠", "")));   // 한글 줄바꿈 이음표(U+2060)를 뺀다
            if (!text.Contains(flow.Stage.T("screen.status.poisoned.tip"))) throw new InvalidOperationException("상태 툴팁에 효과 글이 없다: " + text);
        }

        // 정비 판(S18 — seat < 0) · 남 좌석 판(S39 — 좌석 2): 카드의 상태 줄 board-party-status-<i>가 같은 그림
        static void PartyStatus(UiShot s, int seat)
        {
            var flow = seat < 0 ? s.Made : MyTurn(s, 2, st => Afflicted(st, seat));
            if (!flow.OpenParty(seat)) throw new InvalidOperationException("정비 판이 열리지 않았다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var view = flow.Client.View;
            var party = view.Seats[seat < 0 ? flow.Client.Seat : seat].Party;
            int total = 0;
            for (int i = 0; i < party.Length; i++)
            {
                if (party[i] < 0) continue;
                var pk = view.Pokemon[party[i]];
                var row = root.Q("board-party-status-" + i);
                if ((row != null) != (pk.Status != Sinnoh.Rules.StatusEffect.None)) throw new InvalidOperationException("정비 판 칸 " + i + " 상태 줄 " + (row != null ? "있음" : "없음") + " · 상태 " + pk.Status);
                if (row == null) continue;
                total += StatusIconsCheck(flow, row, root.Q("board-party-mon-" + i), pk, "정비 판 칸 " + i);
            }
            if (total < 2) throw new InvalidOperationException("정비 판 상태 아이콘 " + total + " < 2");
            if (!(flow.Front is PartyScreen)) throw new InvalidOperationException("맨 앞 창이 정비 판이 아니다");
            if (seat >= 0) OwnerCheck(flow, root.Q(FaceBox.OwnerName), seat, flow.Front.Name);
        }

        // 정비 판의 가장 긴 열: 전설 셋(전설 특수기술 있음 · 기술 없음 = 빈 칸 둘이 전설 특수기술 ·배우는 타입 넷 · 독 + 화상 · 유물 + 둘째 지닌물건(backpacker R) · [폼체인지]) + 찌르꼬(애크러뱃).
        // 열이 길어도 아래 가방 칸의 마지막 줄까지 판 안에 든다(글 넘침은 Fit — 가방 줄이 부모 칸 밖이면 거기서 걸린다)
        static void PartyMax(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 1, state =>
            {
                var st = state.Seats[0];
                st.TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("backpacker");
                st.TrainerCardTier = R.TrainerRules.Gold;
                GiveTeam(ctx, state, "483:1", "484:1", "487:1", "396:0");
                string[] relics = { "item.adamant_artifact", "item.lustrous_artifact", "item.griseous_artifact" };
                for (int i = 0; i < relics.Length; i++)
                {
                    var pk = state.Pokemon[st.Party[i]];
                    pk.HeldItem = ItemOf(ctx, relics[i]);
                    pk.HeldItem2 = ItemOf(ctx, i == 0 ? "item.choice_band" : "item.lucky_egg");
                    pk.Status = R.StatusEffect.Poisoned;
                    pk.Status2 = R.StatusEffect.Burned;
                    pk.HasLegendaryMove = true;   // 전설 특수기술(알파 스트라이크 · 비스트 부스트)이 빈 칸 둘을 차지한다
                }
                state.Pokemon[st.Party[3]].Moves[0] = ctx.Cards.MoveIds.IndexOf("acrobatics");
            });
            if (!flow.OpenParty()) throw new InvalidOperationException("정비 판이 열리지 않았다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            for (int i = 0; i < 3; i++)
            {
                if (root.Q("board-party-status-" + i)?.Query(className: "sn-status-icon").ToList().Count != 2) throw new InvalidOperationException("칸 " + i + ": 상태 아이콘 둘이 아니다");
                if (root.Q("board-party-form-" + i) == null) throw new InvalidOperationException("칸 " + i + ": [폼체인지]가 없다");
                if (root.Q("board-party-item2-" + i) == null) throw new InvalidOperationException("칸 " + i + ": 둘째 지닌물건 칸이 없다");
                for (int k = 1; k <= 2; k++)
                    if (root.Q("board-party-slot-" + i + "-" + k)?.ClassListContains(MonParts.LegendSlotClass) != true) throw new InvalidOperationException("칸 " + i + "-" + k + ": 전설 특수기술 줄이 아니다");
            }
            // 물음 줄까지 선 모습(PlayMode 시험이 넘침을 보는 순간 — [폼체인지] → 「…폼체인지하겠습니까?」)
            if (!((PartyScreen)flow.Stage.Current).PressForm(0)) throw new InvalidOperationException("[폼체인지] 물음이 뜨지 않았다");
            s.Panel.Tick(3);
            var panel = root.Q("board-party-panel").worldBound;
            var bag = root.Q("board-party-bag");
            var last = bag[bag.childCount - 1].worldBound;
            var col = root.Q("board-party-col-0");
            var rows = new List<string>();
            foreach (var c in col.Children()) rows.Add(c.worldBound.height.ToString("0"));
            Debug.Log("[UiShots] party-max 열 " + col.worldBound.height.ToString("0") + "(" + string.Join("+", rows) + ") · 물음 " + root.Q("board-party-ask").worldBound.height.ToString("0") + " · 가방 " + bag.worldBound.height.ToString("0") + " · 가방 끝 " + last.yMax.ToString("0") + " · 판 끝 " + panel.yMax.ToString("0"));
            if (last.yMax > bag.worldBound.yMax + 0.5f || bag.worldBound.yMax > panel.yMax + 0.5f) throw new InvalidOperationException("가방 칸의 끝 줄이 판 밖이다(" + last + " · 가방 " + bag.worldBound + " · 판 " + panel + ")");
        }

        // 내 자원 줄(결정 98): 줄이 보이고 화면 안에 있고, 아래 판 · 기록 판 · 위 띠 · UB 칩 판과 겹치지 않고, 칸 열둘(규토리 7 · 여행 점수 · 볼 4)이 줄 안에 있으며 글이 잘리지 않는다
        static void ResClear(UiShot s)
        {
            s.Panel.Tick(3);
            var root = s.Made.Stage.Root;
            var res = root.Q(BoardHud.Res);
            if (res == null || res.resolvedStyle.display == DisplayStyle.None || res.worldBound.width <= 0f) throw new InvalidOperationException("내 자원 줄이 보이지 않는다");
            var box = res.worldBound;
            var screen = s.Panel.Root.worldBound;
            if (box.xMin < screen.xMin - 0.5f || box.yMin < screen.yMin - 0.5f || box.xMax > screen.xMax + 0.5f || box.yMax > screen.yMax + 0.5f) throw new InvalidOperationException("내 자원 줄이 화면 밖으로 나간다(" + box + ")");
            foreach (string other in new[] { "board-hud-dock", "board-hud-log", "board-hud-seats", "board-hud-status", "board-hud-ub-row" }) Apart(root, BoardHud.Res, other);
            if (res.childCount != 12) throw new InvalidOperationException("내 자원 줄의 칸 " + res.childCount + " ≠ 12");
            foreach (var item in res.Children())
            {
                var b = item.worldBound;
                if (b.width <= 0f || b.xMin < box.xMin - 0.5f || b.xMax > box.xMax + 0.5f || b.yMin < box.yMin - 0.5f || b.yMax > box.yMax + 0.5f) throw new InvalidOperationException("내 자원 줄: " + item.name + " 칸이 줄 밖으로 나간다(" + b + " · 줄 " + box + ")");
            }
            var bad = WorkUi.Overflows(res);
            if (bad.Count > 0) throw new InvalidOperationException("내 자원 줄 글 넘침: " + string.Join(" / ", bad));
        }

        // ── 사당·균열 화면 장면(M5) ──

        // 사당 길: 풀밭 사당에 서서 [공물]을 누를 수 있을 때
        static GameFlow ShrineBoard(UiShot s)
        {
            var flow = Trail(s, SoloTrail.Shrine, f => f.InputOpen && f.Hud != null && f.Hud.Shows(BoardHud.Offer));
            flow.Layout(s.Width, s.Height);
            return flow;
        }

        // [공물] → 사당 판(S31)
        static ShrineScreen ShrineOpen(UiShot s, GameFlow flow = null)
        {
            flow = flow ?? ShrineBoard(s);
            if (!flow.Hud.Press(BoardHud.Offer) || !(flow.Stage.Current is ShrineScreen shrine)) throw new InvalidOperationException("[공물]이 사당 판을 열지 않았다");
            s.Panel.Tick(3);
            return shrine;
        }

        // 분홍 규토리 + 여행 점수 7을 고른 판
        static ShrineScreen ShrinePicked(UiShot s)
        {
            var shrine = ShrineOpen(s);
            if (!shrine.PickColor((int)Sinnoh.Rules.ApricornColor.Pink) || !shrine.SetExtra(true)) throw new InvalidOperationException("공물 고르기가 거절됐다");
            s.Panel.Tick(3);
            return shrine;
        }

        // [바치기] 뒤 공물 단계(310)의 ms
        static void ShrineOffered(UiShot s, int ms)
        {
            if (!ShrinePicked(s).PressOffer()) throw new InvalidOperationException("[바치기]가 거절됐다");
            ShrineInto(s, Sinnoh.Fx.ShrineRows.Offer, ms);
        }

        // 대기열의 그 행 단계의 시작에서 ms 지난 순간으로
        static void ShrineInto(UiShot s, int row, int ms)
        {
            var flow = s.Made;
            var step = Queued(flow, st => st.Row == row);
            if (step == null) throw new InvalidOperationException(row + "행 단계가 대기열에 없다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            Into(s, flow, step, ms);
        }

        // 그 모습이 될 때까지 세션·연출 시각을 보낸다
        static void ShrineSettle(UiShot s, GameFlow flow, Func<GameFlow, bool> until)
        {
            for (int i = 0; i < 200 && !until(flow); i++) Pass(flow, 250);
            if (!until(flow))
            {
                var choice = flow.Client.FirstChoice();
                throw new InvalidOperationException("사당 판이 그 모습이 되지 않았다: 화면 " + (flow.Stage.Current != null ? flow.Stage.Current.Name : "보드") + " · 꼴 " + ((flow.Stage.Current as ShrineScreen)?.Shown.ToString() ?? "-")
                    + " · 선택 " + (choice != null ? choice.Kind + "/" + choice.Sub : "없음") + " · 입력 " + flow.BoardOpen);
            }
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
        }

        // 사당 길을 길잡이로 그 고르기까지(입력이 열린 사당 판)
        static ShrineScreen ShrineChoice(UiShot s, ShrineScreen.Mode mode)
        {
            var flow = Trail(s, SoloTrail.Shrine, f => f.Stage.Current is ShrineScreen sh && sh.Shown == mode && sh.Open);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            return (ShrineScreen)flow.Stage.Current;
        }

        // 사당 길: 추종자를 펄기아로 [확정]한 뒤 집계 단계(340)의 ms
        static GameFlow ShrineTally(UiShot s, int ms)
        {
            var shrine = ShrineChoice(s, ShrineScreen.Mode.Follower);
            if (!shrine.Press(Sinnoh.Rules.ChoiceAnswer.Pick((int)Sinnoh.Rules.SinnohGod.Palkia))) throw new InvalidOperationException("추종자 [확정]이 거절됐다");
            ShrineInto(s, Sinnoh.Fx.ShrineRows.Tally, ms);
            return s.Made;
        }

        // 사당 길: 신 강림 단계(341)의 ms
        static GameFlow ShrineDescend(UiShot s, int ms)
        {
            var shrine = ShrineChoice(s, ShrineScreen.Mode.Follower);
            if (!shrine.Press(Sinnoh.Rules.ChoiceAnswer.Pick((int)Sinnoh.Rules.SinnohGod.Palkia))) throw new InvalidOperationException("추종자 [확정]이 거절됐다");
            ShrineInto(s, Sinnoh.Fx.ShrineRows.Descend, ms);
            return s.Made;
        }

        // 고대의 지혜 견본: 적당한 조우 덱의 카드 열두 장을 버림 더미에(배울 기술이 생긴다)
        static void ShrineDiscards(Sinnoh.Rules.GameState st)
        {
            var deck = st.Decks[(int)Sinnoh.Rules.DeckId.Moderate];
            var pile = st.Discards[(int)Sinnoh.Rules.DeckId.Moderate];
            for (int i = 0; i < 12 && deck.Count > 0; i++)
            {
                pile.Add(deck[deck.Count - 1]);
                deck.RemoveAt(deck.Count - 1);
            }
        }

        // 좌석 여럿 견본: 내 좌석(0)을 풀밭 사당에 세우고 덱 맨 위를 top으로 — 내 턴까지 넘긴 뒤 분홍 규토리(2단계)를 바치고 그 고르기가 열릴 때까지
        static ShrineScreen ShrineSeats(UiShot s, int seats, ShrineScreen.Mode mode, Action<Sinnoh.Rules.GameState> more, params string[] top)
        {
            var flow = Seats(s, seats, st =>
            {
                SoloTrail.PrepareShrine(s.Made.Ctx, st, 0, top);
                more?.Invoke(st);
            });
            for (int k = 0; k < seats * 2 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            ShrineSettle(s, flow, f => f.InputOpen && f.Hud != null && f.Hud.Shows(BoardHud.Offer));
            var shrine = ShrineOpen(s, flow);
            if (!shrine.PickColor((int)Sinnoh.Rules.ApricornColor.Pink) || !shrine.PressOffer()) throw new InvalidOperationException("[바치기]가 거절됐다");
            ShrineSettle(s, flow, f => f.Stage.Current is ShrineScreen sh && sh.Shown == mode && sh.Open);
            return (ShrineScreen)flow.Stage.Current;
        }

        // 4인 전설 이벤트(서리의 노래): 전설 단계(330)의 ms
        static GameFlow ShrineLegend(UiShot s, int ms)
        {
            GameFlow flow = Seats(s, 4, st => SoloTrail.PrepareShrine(s.Made.Ctx, st, 0, "shrine_event.song_of_frost"));
            for (int k = 0; k < 8 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            ShrineSettle(s, flow, f => f.InputOpen && f.Hud != null && f.Hud.Shows(BoardHud.Offer));
            var shrine = ShrineOpen(s, flow);
            if (!shrine.PickColor((int)Sinnoh.Rules.ApricornColor.Pink) || !shrine.PressOffer()) throw new InvalidOperationException("[바치기]가 거절됐다");
            ShrineInto(s, Sinnoh.Fx.ShrineRows.Legend, ms);
            return flow;
        }

        // ── 스탬프·도전·거래 화면 장면(M5) ──

        // 스탬프 순서(견본): 실제 규칙의 받기(StampRules.Gain — 보상 규토리 · 그 덱의 무작위 5장)를 그대로 돌린다
        static readonly int[] StampOrder = { R.StampRules.TrainerWin, R.StampRules.HeldItems, R.StampRules.FateCards, R.StampRules.FailedCaptures, R.StampRules.LegendaryCaught, R.StampRules.SwarmWin, R.StampRules.AllApricorns };

        // 혼자 하는 판에 스탬프 n개를 쥐여 주고 스탬프 판을 연다
        static GameFlow Stamps(UiShot s, int n)
        {
            var flow = ToBoard(s, BoardReadyMs);
            flow.Rebase(st =>
            {
                var fx = new R.Emitter(st, null);
                for (int i = 0; i < n; i++) R.StampRules.Gain(st, flow.Ctx, fx, 0, StampOrder[i]);
            });
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            if (!flow.OpenStamp() || !(flow.Stage.Current is StampScreen)) throw new InvalidOperationException("스탬프 판이 열리지 않았다");
            return flow;
        }

        // 47행의 한 순간: 규토리 일곱 색을 쥔 판(have = 이미 가진 스탬프 수)에서 [턴 종료] — 그 명령 끝에 스탬프 3을 받는다
        static GameFlow StampInto(UiShot s, int have, int ms)
        {
            var flow = ToBoard(s, BoardReadyMs);
            flow.Rebase(st =>
            {
                var fx = new R.Emitter(st, null);
                for (int i = 0; i < have; i++) R.StampRules.Gain(st, flow.Ctx, fx, 0, StampOrder[i + 1]);
                for (int c = 0; c < st.Seats[0].Apricorns.Length; c++) st.Seats[0].Apricorns[c] = Math.Max(1, st.Seats[0].Apricorns[c]);
            });
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            if (!flow.Hud.Press(BoardHud.EndTurn)) throw new InvalidOperationException("[턴 종료]가 거절됐다");
            var step = Queued(flow, st => st.Row == Sinnoh.Fx.DealRows.Stamp);
            if (step == null) throw new InvalidOperationException("스탬프 단계가 대기열에 없다");
            Into(s, flow, step, ms);
            if (flow.Deals == null || !flow.Deals.StampShown) throw new InvalidOperationException("스탬프 덮개가 보이지 않는다");
            return flow;
        }

        // 좌석 여럿 판을 내 턴(입력이 열린 때)까지 넘긴다
        static GameFlow MyTurn(UiShot s, int seats, Action<R.GameState> prepare)
        {
            var flow = Seats(s, seats, prepare);
            for (int k = 0; k < seats * 2 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.InputOpen && f.Hud != null && f.Client.View.ActiveSeat == f.Client.Seat && f.Client.Choice(R.ChoiceKind.MapAction) != null);
            return flow;
        }

        // 좌석 둘 판을 좌석 2의 턴(내 입력이 열린 때)까지 넘긴다
        static GameFlow TheirTurn(UiShot s, Action<R.GameState> prepare)
        {
            var flow = Seats(s, 2, prepare);
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat == flow.Client.Seat; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.InputOpen && f.Hud != null && f.Client.View.ActiveSeat != f.Client.Seat);
            return flow;
        }

        static void DealSettle(UiShot s, GameFlow flow, Func<GameFlow, bool> until)
        {
            for (int i = 0; i < 200 && !until(flow); i++) Pass(flow, 250);
            if (!until(flow))
            {
                var choice = flow.Client.FirstChoice();
                throw new InvalidOperationException("그 모습이 되지 않았다: 화면 " + (flow.Stage.Current != null ? flow.Stage.Current.Name : "보드") + " · 선택 " + (choice != null ? choice.Kind + "/" + choice.Sub : "없음") + " · 턴 좌석 " + flow.Client.View.ActiveSeat + " · 입력 " + flow.BoardOpen);
            }
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
        }

        // 좌석 셋: 좌석 2는 같은 칸(고향 마을 — 거절 가능), 좌석 3은 다른 신의 추종자(원격 도전)
        static GameFlow Rivals(UiShot s) => MyTurn(s, 3, st =>
        {
            st.Seats[1].Cell = st.Seats[0].Cell;
            st.Seats[0].Follower = R.SinnohGod.Dialga;
            st.Seats[2].Follower = R.SinnohGod.Palkia;
        });

        // 51행의 한 순간: 좌석 3에게 원격 도전을 건 뒤 VS 단계의 ms
        static GameFlow Versus(UiShot s, int ms)
        {
            var flow = Rivals(s);
            if (!flow.Hud.Press(BoardHud.Challenge) || !(flow.Stage.Current is ChallengeScreen pick)) throw new InvalidOperationException("[도전]이 상대 고르는 창을 열지 않았다");
            if (!pick.PressSeat(2)) throw new InvalidOperationException("원격 도전이 거절됐다");
            ShrineInto(s, Sinnoh.Fx.DealRows.Vs, ms);
            if (flow.Deals == null || !flow.Deals.VsShown) throw new InvalidOperationException("VS 띠가 보이지 않는다");
            return flow;
        }

        // 거래 견본(실제 데이터): 두 좌석을 같은 칸에 세우고 주고받을 것을 쥐여 준다. rich = 가진 것이 많은 판(글자 넘침 단정용)
        static void TradeKit(R.Ctx ctx, R.GameState st, bool rich)
        {
            st.Seats[1].Cell = st.Seats[0].Cell;
            var me = st.Seats[0];
            var them = st.Seats[1];
            me.Apricorns[(int)R.ApricornColor.Red] = 2;
            me.Apricorns[(int)R.ApricornColor.Blue] = 3;
            me.Jp = 6;
            me.Balls[(int)R.Ball.Regular] = 2;
            me.Bag.Add(ctx.Items.Ids.IndexOf("item.choice_band"));
            them.Apricorns[(int)R.ApricornColor.Yellow] = 2;
            them.Apricorns[(int)R.ApricornColor.Green] = 1;
            them.Jp = 4;
            them.Balls[(int)R.Ball.Greater] = 1;
            them.Bag.Add(ctx.Items.Ids.IndexOf("item.swiftness_sash"));
            var fates = st.Decks[(int)R.DeckId.ChangeOfFate];
            for (int i = 0; i < 5 && fates.Count > 0; i++)
            {
                (i < 3 ? me : them).FateHand.Add(fates[fates.Count - 1]);
                fates.RemoveAt(fates.Count - 1);
            }
            for (int k = 0; k < 2; k++)
            {
                var seat = st.Seats[k];
                seat.OpenSlots = 6;
                int extra = rich ? 4 : 1;   // 거래로 하나 받아도 칸(6)을 넘지 않게 다섯까지
                for (int i = 0; i < extra; i++)
                    R.PokemonRules.AddToParty(seat, R.PokemonRules.Spawn(st, ctx.Cards.PokemonIds.IndexOf(SoloTrail.Team[(i + k) % SoloTrail.Team.Length]), k, (int)R.DeckId.Legendary));
                if (!rich) continue;
                for (int c = 0; c < seat.Apricorns.Length; c++) seat.Apricorns[c] = Math.Max(seat.Apricorns[c], 12);
                for (int b = 0; b < seat.Balls.Length; b++) seat.Balls[b] = 10;
                seat.Jp = 28;
                foreach (string item in new[] { "item.choice_band", "item.swiftness_sash", "item.scroll_of_nobility", "item.gracidea_flower" })   // 유물 칩의 그림(결정 80 L14 나)
                    if (!seat.Bag.Contains(ctx.Items.Ids.IndexOf(item))) seat.Bag.Add(ctx.Items.Ids.IndexOf(item));
            }
        }

        // 내 턴에 [거래] → 거래 창
        static TradeScreen TradeOpen(UiShot s, bool rich)
        {
            var flow = MyTurn(s, 2, st => TradeKit(s.Made.Ctx, st, rich));
            if (!flow.Hud.Press(BoardHud.Trade) || !(flow.Stage.Current is TradeScreen trade)) throw new InvalidOperationException("[거래]가 거래 창을 열지 않았다");
            s.Panel.Tick(3);
            return trade;
        }

        // 견본 거래: 파랑 규토리 2 + 포켓몬 하나를 주고 여행 점수 3 + 운명의 변화 1장을 받는다
        static TradeScreen TradePut(UiShot s, TradeScreen trade)
        {
            var flow = s.Made;
            var view = flow.Client.View;
            var mine = view.Seats[flow.Client.Seat];
            int mon = mine.Party[R.PokemonRules.PartyCount(mine) - 1];
            if (!trade.Set(0, R.TradeKind.Apricorn, (int)R.ApricornColor.Blue, 2) || !trade.Set(0, R.TradeKind.Pokemon, mon, 1) || !trade.Set(1, R.TradeKind.Jp, -1, 3) || !trade.Set(1, R.TradeKind.Fate, -1, 1))
                throw new InvalidOperationException("견본 거래를 올리지 못했다");
            if (!trade.CanPropose) throw new InvalidOperationException("견본 거래가 맞지 않는다");
            return trade;
        }

        // 52행의 한 순간: 견본 거래를 제안하고 좌석 2가 수락한 뒤 거래 단계의 ms
        static GameFlow SeatTraded(UiShot s, int ms)
        {
            var trade = TradePut(s, TradeOpen(s, false));
            var flow = s.Made;
            if (!trade.Propose()) throw new InvalidOperationException("[제안]이 거절됐다");
            if (!flow.AnswerSeat(1, a => a.Kind == R.AnswerKind.Accept)) throw new InvalidOperationException("좌석 2의 수락이 거절됐다");
            ShrineInto(s, Sinnoh.Fx.DealRows.Trade, ms);
            if (flow.Deals == null || !flow.Deals.TradeShown) throw new InvalidOperationException("거래 띠가 보이지 않는다");
            return flow;
        }

        // 좌석 2가 내 운명의 변화 2장을 바라는 거래를 건다 → 프롬프트의 [수락] → 줄 카드 고르는 창(picked = 두 장을 고른 모습)
        static GameFlow TradePick(UiShot s, bool picked)
        {
            var flow = TheirTurn(s, st => TradeKit(s.Made.Ctx, st, false));
            var offer = R.TradeRules.Offer(flow.Client.Seat, 0, (int)R.TradeKind.Apricorn, (int)R.ApricornColor.Yellow, 2, 0, (int)R.TradeKind.Jp, -1, 2, 1, (int)R.TradeKind.Fate, -1, 2);
            if (!flow.AnswerSeatWith(1, offer)) throw new InvalidOperationException("좌석 2의 거래 제안이 거절됐다");
            DealSettle(s, flow, f => f.InputOpen && f.Hud != null && f.Hud.Prompting);
            if (!flow.Hud.PressPrompt(true) || !(flow.Stage.Current is TradePickScreen pick)) throw new InvalidOperationException("[수락]이 고르는 창을 열지 않았다");
            if (picked && (!pick.Pick(0) || !pick.Pick(2))) throw new InvalidOperationException("카드를 고르지 못했다");
            s.Panel.Tick(3);
            return flow;
        }

        // 트레이너 카드 판(S40)의 그 직업 카드(무녀 2종 색 — §5 #3 ③)
        static void Acolyte(UiShot s, string card)
        {
            var flow = ToBoard(s, BoardReadyMs);
            flow.Rebase(st => st.Seats[0].TrainerCard = flow.Ctx.Cards.TrainerCardIds.IndexOf(card));
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            if (!flow.OpenTrainer()) throw new InvalidOperationException("트레이너 카드 판이 열리지 않았다");
        }

        // ── 막·종막 화면 장면(M5) ──

        // 막 길 Heart: 시작의 사당에 서서 [공물]을 누를 수 있을 때
        static GameFlow HeartOffer(UiShot s)
        {
            var flow = Trail(s, SoloTrail.Heart, f => f.InputOpen && f.HeartOffer && f.Hud != null);
            flow.Layout(s.Width, s.Height);
            flow.Hud.Refresh();
            return flow;
        }

        // [공물] 뒤 심장 회복(94행 943)의 ms
        static GameFlow HeartStep(UiShot s, int ms)
        {
            var flow = HeartOffer(s);
            // [공물] → 사당 판(S31 — M5 「사당·균열 화면」)에서 첫 색을 고르고 [바치기]
            var shrine = ShrineOpen(s, flow);
            var first = shrine.Offers().Find(a => a.Values.Count == 0);
            if (first == null || !shrine.Press(first)) throw new InvalidOperationException("[공물] → [바치기]가 거절됐다");
            var step = Queued(flow, st => st.Row == Sinnoh.Fx.FinaleRows.Heart);
            if (step == null) throw new InvalidOperationException("심장 회복 단계(943)가 대기열에 없다");
            Into(s, flow, step, ms);
            return flow;
        }

        // 막 길에서 처음 달성한 좌석이 있는 막 카드(4행)의 ms — 배틀 결과 창 [확인] 뒤 대기열에 온다
        static GameFlow ActCardOf(UiShot s, SoloTrail trail, int ms)
        {
            var flow = Trail(s, trail, f => Queued(f, st => st.Row == Sinnoh.Fx.ActBeats.Row && st.Tag is ActCardInfo info && info.FirstSeat >= 0) != null);   // 막 카드가 대기열에 오는 것은 배틀 끝 갱신(결과 창 전)
            // 막 카드는 배틀 결과 창의 [확인] 뒤에 돈다(결과 창이 대기열을 세워 둔다)
            for (int i = 0; i < BattleStepMax && flow.Stage.Current is BattleScreen b; i++)
                if (!(b.ShowsResult && b.Confirm())) Pass(flow, BattleStepMs);
            var step = Queued(flow, st => st.Row == Sinnoh.Fx.ActBeats.Row && st.Tag is ActCardInfo info && info.FirstSeat >= 0);
            Into(s, flow, step, ms);
            return flow;
        }

        // 종막 앞 판(좌석 seats — 태홍을 이긴 좌석 1이 고른다): 종막 선택 창이 열리고 입력이 열린 때
        static GameFlow EndingAsk(UiShot s, int seats)
        {
            var flow = Seats(s, seats, st => SoloTrail.PrepareEnding(FxShots.LoadCtx(), st));
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            if (!(flow.Stage.Current is EndingScreen screen) || !screen.Asking) throw new InvalidOperationException("종막 선택 창이 열리지 않았다");
            return flow;
        }

        // 종막 선택에서 pick(0 영웅 · 1 배신자)을 누른 뒤 ms(연출 시각 — 95행 2800 · 97행 8000이 이어진다)
        static GameFlow EndingChose(UiShot s, int seats, int pick, int ms)
        {
            var flow = EndingAsk(s, seats);
            if (!((EndingScreen)flow.Stage.Current).Pick(pick)) throw new InvalidOperationException("종막 선택이 거절됐다");
            flow.Stage.Tweens.Advance(ms);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            return flow;
        }

        // 종막 앞 판(좌석 셋)에서 [배신자] → 모두 회복이 끝나 순서를 묻는 때
        static GameFlow TraitorOrder(UiShot s)
        {
            var flow = EndingChose(s, 3, 1, 0);
            flow.Stage.Tweens.Advance(flow.QueueLeftMs + BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            if (!(flow.Stage.Current is EndingScreen screen) || !screen.Ordering) throw new InvalidOperationException("배신자 순서 고르기가 열리지 않았다");
            return flow;
        }

        // 결정 71: 지금 칸에서 뒷면 숨겨진 장소 spot을 경유 칸으로 거쳐 그 이웃 beyond(뒷면 숨겨진 장소 없음)로 가는 길이 남은 이동력 안이고 그 길에서 처음 멈출 칸이 spot인 짝.
        // 그림이 읽히게 고른 칸은 길 끝에서만 밟는 짝을 먼저, 그 가운데 최단 길이 spot을 거치지 않는 짝(경유 칸이 길을 바꾼다)을 더 먼저
        static bool PassPair(GameFlow flow, out int spot, out int beyond)
        {
            var view = flow.Client.View;
            var me = view.Seats[flow.Client.Seat];
            var reach = new List<int>(flow.Hud.Reach);
            spot = beyond = -1;
            int best = 0;
            foreach (var t in view.Tokens)
            {
                if (t.Kind != Sinnoh.Rules.TokenKind.HiddenSpot || t.FaceUp || t.Destroyed || t.Cell < 0 || !reach.Contains(t.Cell)) continue;
                for (int d = 0; d < Sinnoh.Rules.Board.Directions; d++)
                {
                    int b = flow.Ctx.Board.Neighbor(t.Cell, d);
                    if (b == Sinnoh.Rules.Board.NoCell || b == me.Cell || !reach.Contains(b) || Sinnoh.Rules.MapActions.FaceDownSpotAt(view, b) >= 0) continue;
                    var route = Sinnoh.Rules.MoveRules.Route(flow.Ctx.Board, me.Cell, new[] { t.Cell }, b, me.MovesLeft, view.Options.ElevationMode);
                    if (route == null) continue;
                    var stops = Sinnoh.Rules.MapActions.StopsOn(view, flow.Ctx, flow.Client.Seat, route.GetRange(1, route.Count - 1));
                    if (stops.Count == 0 || stops[0] != t.Cell) continue;
                    var shortest = Sinnoh.Rules.MoveRules.Path(flow.Ctx.Board, me.Cell, b, me.MovesLeft, view.Options.ElevationMode);
                    int score = 1 + (route.IndexOf(b) == route.Count - 1 ? 2 : 0) + (shortest != null && !shortest.Contains(t.Cell) ? 1 : 0);
                    if (score <= best) continue;
                    best = score;
                    spot = t.Cell;
                    beyond = b;
                }
            }
            return best > 0;
        }

        // 결정 71 ①: 고르기만 해도(경유 칸 없이) 최단 길 가운데에 뒷면 숨겨진 장소가 있는 칸. 없으면 −1
        static int SpotOnShortest(GameFlow flow)
        {
            var view = flow.Client.View;
            var me = view.Seats[flow.Client.Seat];
            foreach (int cell in flow.Hud.Reach)
            {
                if (Sinnoh.Rules.MapActions.FaceDownSpotAt(view, cell) >= 0) continue;
                var path = Sinnoh.Rules.MoveRules.Path(flow.Ctx.Board, me.Cell, cell, me.MovesLeft, view.Options.ElevationMode);
                if (path != null && Sinnoh.Rules.MapActions.StopsOn(view, flow.Ctx, flow.Client.Seat, path.GetRange(1, path.Count - 1)).Count > 0) return cell;
            }
            return -1;
        }

        // 결정 71: 경유 칸 짝이 생길 때까지 가장 가까운 뒷면 숨겨진 장소 쪽으로 걷는다(턴을 넘겨 가며 — 다가가는 길에 멈추면 지나간다)
        static GameFlow ToPassPair(UiShot s, out int spot, out int beyond)
        {
            var flow = ToBoard(s, BoardReadyMs);
            for (int i = 0; i < 80; i++)
            {
                if (!flow.InputOpen) { Pass(flow, BattleStepMs); continue; }
                var client = flow.Client;
                if (client.Choice(Sinnoh.Rules.ChoiceKind.HiddenSpot) != null)
                {
                    if (!flow.Hud.Press(BoardHud.Pass)) throw new InvalidOperationException("[지나가기]가 거절됐다");
                    continue;
                }
                if (PassPair(flow, out spot, out beyond)) return flow;
                var view = client.View;
                int me = view.Seats[client.Seat].Cell, goal = -1, best = -1, bestLeft = int.MaxValue;
                var from = Sinnoh.Rules.MoveRules.Reachable(flow.Ctx.Board, me, 400, view.Options.ElevationMode);
                foreach (var t in view.Tokens)
                    if (t.Kind == Sinnoh.Rules.TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0 && from[t.Cell] > 1 && (goal < 0 || from[t.Cell] < from[goal])) goal = t.Cell;
                if (goal < 0) throw new InvalidOperationException("닿을 수 있는 숨겨진 장소가 없다");
                foreach (int cell in flow.Hud.Reach)
                {
                    int left = Sinnoh.Rules.MoveRules.Reachable(flow.Ctx.Board, cell, 400, view.Options.ElevationMode)[goal];
                    if (cell != goal && left != Sinnoh.Rules.MoveRules.Unreachable && left >= 1 && left < bestLeft) { best = cell; bestLeft = left; }
                }
                if (best >= 0 && bestLeft < from[goal] && view.Seats[client.Seat].MovesLeft == Sinnoh.Rules.MoveRules.BaseMovement) { if (!flow.Hud.Walk(best)) throw new InvalidOperationException("이동이 거절됐다"); }
                else if (!flow.Hud.Press(BoardHud.EndTurn)) throw new InvalidOperationException("턴 종료가 거절됐다");
            }
            throw new InvalidOperationException("경유 칸 짝에 닿지 못했다");
        }

        // 결정 71 ③: 경유 칸 짝의 길로 걷다가 숨겨진 장소에서 멈춘 모습(물음이 열리고 입력이 열린 뒤)
        static GameFlow ToPassAsk(UiShot s)
        {
            var flow = ToPassPair(s, out int spot, out int beyond);
            if (!flow.Hud.WalkVia(beyond, spot)) throw new InvalidOperationException("경유 칸 길 걷기가 거절됐다");
            for (int i = 0; i < 40 && !flow.InputOpen; i++) Pass(flow, 100);
            var ask = flow.Client.Choice(Sinnoh.Rules.ChoiceKind.HiddenSpot);
            if (ask == null || ask.Options.Count == 0 || flow.Client.View.Seats[flow.Client.Seat].Cell != spot) throw new InvalidOperationException("길 가운데의 숨겨진 장소에서 멈추지 않았다");
            return flow;
        }

        // 찾아 둔 이기는 판(WinSeed): 고향 마을에서 [도전] → 첫 기술만 눌러 조무래기 둘을 이긴다 → 결과 창
        static GameFlow Win(UiShot s)
        {
            var flow = s.Flow();
            flow.StartSolo(new Sinnoh.Rules.Session.SoloSettings(), WinSeed, WinAiSeed);
            var village = (SetupVillageScreen)flow.Stage.Current;
            village.Pick(WinVillage);
            if (!village.Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
            flow.Stage.Tweens.Advance(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
            var pick = (SetupPickScreen)flow.Stage.Current;
            pick.PickStarter(WinStarter);
            pick.PickCard(0);
            if (!pick.Confirm()) throw new InvalidOperationException("셋업 확정이 거절됐다");
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다");
            for (int i = 0; i < BattleStepMax; i++)
            {
                if (!MyBattleTurn(flow)) break;
                if (!((BattleScreen)flow.Stage.Current).PressMove(0)) throw new InvalidOperationException("기술 답이 거절됐다");
            }
            if (!flow.Battle.Won) throw new InvalidOperationException("찾아 둔 판인데 이기지 못했다(규칙·데이터가 바뀌었나 — BattleFeedTests.WinGame을 다시 찾는다)");
            return flow;
        }

        // 이긴 판의 다음 내 턴(조무래기가 물러난 고향 마을 · 1막 보상 빨강 규토리 3개)에 마을 창을 연다 — 공중날기가 열려 있다
        static VillageScreen Freed(UiShot s)
        {
            var flow = Win(s);
            for (int i = 0; i < BattleStepMax && !flow.InputOpen; i++)
            {
                var battle = flow.Stage.Current as BattleScreen;
                if (battle != null && battle.ShowsResult && !battle.AskingReward) battle.Confirm();
                else Pass(flow, BattleStepMs);
            }
            if (!flow.InputOpen || !flow.Hud.Press(BoardHud.Village)) throw new InvalidOperationException("이긴 뒤 마을 창이 열리지 않았다");
            var village = (VillageScreen)flow.Stage.Current;
            if (!village.CanFly) throw new InvalidOperationException("조무래기가 물러났는데 공중날기를 쓸 수 없다(" + village.FlyWhy + ")");
            return village;
        }

        // ── 제작·은행·진화(M5): 손본 판(SoloTrail.Work)의 첫 턴 — 고향 마을, 입력이 열렸다 ──
        static GameFlow WorkBoard(UiShot s) => Trail(s, SoloTrail.Work, f => f.InputOpen && f.Hud != null && f.Hud.Shows(BoardHud.Craft));

        // 글자 자리 단언: 찍기 전에 배치를 세우고, 열린 창 안의 글이 잘리거나 칸 밖으로 넘치거나 옆 요소에 덮이면 실패한다(WorkUi.Overflows — 720·1080 모두)
        static Action<UiShot> Fit(Action<UiShot> build) => s =>
        {
            build(s);
            s.Panel.Tick(3);
            var top = s.Made.Stage.Current;
            var bad = WorkUi.Overflows(top != null ? s.Made.Stage.Root.Q(top.Name) : null);
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + bad.Count + "곳: " + string.Join(" / ", bad));
        };

        // 글자 자리 단언(이름으로 고른 칸들 — 창이 아닌 HUD 칸·배틀 독 등): 배치를 세운 뒤 그 칸 안의 글이 잘리거나 넘치거나 덮이면 실패한다
        static Action<UiShot> Clean(Action<UiShot> build, params string[] names) => s =>
        {
            build(s);
            s.Panel.Tick(3);
            foreach (string name in names)
            {
                var box = s.Made.Stage.Root.Q(name);
                if (box == null) throw new InvalidOperationException("글자 자리 단언: " + name + " 칸이 없다");
                var bad = WorkUi.Overflows(box);
                if (bad.Count > 0) throw new InvalidOperationException("글자 넘침(" + name + ") " + bad.Count + "곳: " + string.Join(" / ", bad));
            }
        };

        // [제작] → 제작 창(배치를 세운다 — 빨려 드는 재료가 완성품의 자리를 읽는다)
        static CraftScreen CraftOpen(UiShot s)
        {
            var flow = WorkBoard(s);
            if (!flow.Hud.Press(BoardHud.Craft) || !(flow.Stage.Current is CraftScreen craft)) throw new InvalidOperationException("[제작]이 제작 창을 열지 않았다");
            s.Panel.Tick(3);
            return craft;
        }

        // 그 아이템을 만들고 ms만큼 보낸다(36·37행)
        static void Crafted(UiShot s, string item, int ms)
        {
            var craft = CraftOpen(s);
            if (!craft.PressMake(craft.ItemOf(item))) throw new InvalidOperationException(item + " [제작]이 거절됐다");
            s.Made.Stage.Tweens.Advance(ms);
        }

        // [마을] → 은행상회 칸(S24)
        static VillageScreen BankOpen(UiShot s)
        {
            var flow = WorkBoard(s);
            if (!flow.Hud.Press(BoardHud.Village) || !(flow.Stage.Current is VillageScreen village) || village.Bank == null) throw new InvalidOperationException("마을 창의 은행상회 칸이 열리지 않았다");
            s.Panel.Tick(3);
            return village;
        }

        // 은행상회의 할 수 있는 첫 줄을 거래하고 ms만큼 보낸다(18행)
        static void Traded(UiShot s, int ms)
        {
            var bank = BankOpen(s).Bank;
            if (!bank.Press(bank.CanTrade(Sinnoh.Rules.BankRules.Top) ? Sinnoh.Rules.BankRules.Top : Sinnoh.Rules.BankRules.Bottom)) throw new InvalidOperationException("은행상회 거래가 거절됐다");
            s.Made.Stage.Tweens.Advance(ms);
        }

        // [진화] → 진화 창(S27)
        static EvolveScreen EvolveOpen(UiShot s, GameFlow flow = null)
        {
            flow = flow ?? WorkBoard(s);
            if (!flow.Hud.Press(BoardHud.Evolve) || !(flow.Stage.Current is EvolveScreen evolve)) throw new InvalidOperationException("[진화]가 진화 창을 열지 않았다");
            return evolve;
        }

        // 스타팅 포켓몬(파티 둘째 — 첫 칸은 이브이)을 진화시키고 ms만큼 보낸다(39행)
        static void Evolving(UiShot s, int ms)
        {
            var evolve = EvolveOpen(s);
            if (!evolve.PressEvolve(evolve.Listed[1])) throw new InvalidOperationException("스타팅 포켓몬의 진화가 거절됐다");
            s.Made.Stage.Tweens.Advance(ms);
        }

        // 결정 181: 스타팅 포켓몬 진화 뒤 옛 기술 물음 — 해금 덮개(48행)가 끝날 때까지 보낸 뒤, 진화형의 ★ 고유 기술 줄(누르지 않음) + 칸 1·2(누를 수 있음)가 판 안 · 화면 안
        //   filled = 스타팅 포켓몬의 칸 1에 이브이의 고유 기술을 넣어 둔 판(바꿀 기술이 있는 칸)
        static void EvolveTeachNow(UiShot s, bool filled)
        {
            var flow = WorkBoard(s);
            if (filled)
            {
                flow.Rebase(st =>
                {
                    var party = st.Seats[0].Party;
                    st.Pokemon[party[1]].Moves[0] = flow.Ctx.Cards.Pokemon[st.Pokemon[party[0]].Def].Signature;
                });
                flow.Stage.Tweens.Advance(BoardReadyMs);
                flow.Layout(s.Width, s.Height);
            }
            var opened = EvolveOpen(s, flow);
            if (!opened.PressEvolve(opened.Listed[1])) throw new InvalidOperationException("스타팅 포켓몬의 진화가 거절됐다");
            flow.Stage.Tweens.Advance(Sinnoh.Rules.Session.PlayTiming.EvolveMs + BoardReadyMs);
            flow.Stage.Tweens.Advance(20000);
            s.Panel.Tick(3);
            var evolve = flow.Stage.Current as EvolveScreen;
            if (evolve == null || evolve.Now != EvolveScreen.Mode.Teach) throw new InvalidOperationException("옛 기술 물음이 아니다: " + (evolve != null ? evolve.Now.ToString() : "창 없음"));
            if (flow.Fx != null && flow.Fx.Sequencer.Current != null) throw new InvalidOperationException("연출이 아직 돈다");
            var root = flow.Stage.Root;
            var panel = root.Q("board-evolve-panel") ?? throw new InvalidOperationException("진화 판이 없다");
            var sig = root.Q("board-evolve-sig") ?? throw new InvalidOperationException("고유 기술 줄이 없다");
            if (root.Q("board-evolve-now") == null) throw new InvalidOperationException("「지금 기술」 머리가 없다");
            if (sig.ClassListContains("sn-party-press")) throw new InvalidOperationException("고유 기술 줄을 누를 수 있다");
            var ctx = flow.Ctx;
            int pokemon = flow.Client.Choice(R.ChoiceKind.Reward).Options[0];
            string sigName = flow.Stage.T(Sinnoh.Fx.BattleFeed.MoveNameKey(ctx, ctx.Cards.Pokemon[flow.Client.View.Pokemon[pokemon].Def].Signature));
            if (!sig.Query<Label>().ToList().Exists(l => l.text.Contains(sigName))) throw new InvalidOperationException("고유 기술 줄에 " + sigName + "이(가) 없다");
            int had = flow.Client.View.Pokemon[pokemon].Moves[0];
            if (filled != (had >= 0)) throw new InvalidOperationException("칸 1 기술 = " + had);
            if (filled && root.Q("board-evolve-teach-0").Q(className: "sn-enc-move") == null) throw new InvalidOperationException("칸 1에 지금 기술 줄이 없다");
            var screen = root.worldBound;
            foreach (string name in new[] { "board-evolve-sig", "board-evolve-teach-0", "board-evolve-teach-1", "board-evolve-keep" })
            {
                var e = root.Q(name) ?? throw new InvalidOperationException("요소 없음: " + name);
                if (name.StartsWith("board-evolve-teach-", StringComparison.Ordinal) && !e.ClassListContains("sn-party-press")) throw new InvalidOperationException(name + "을(를) 누를 수 없다");
                var at = e.worldBound;
                if (at.xMin < panel.worldBound.xMin - 0.5f || at.xMax > panel.worldBound.xMax + 0.5f || at.yMax > panel.worldBound.yMax + 0.5f) throw new InvalidOperationException(name + "이(가) 판 밖으로 나간다");
            }
            if (panel.worldBound.yMax > screen.yMax + 1f) throw new InvalidOperationException("진화 판이 화면 밖으로 나간다: " + panel.worldBound + " / " + screen);
        }

        // 결정 186: 그 요소에 마우스를 올린 모습 — 툴팁이 걸렸고 제목 = 기술 이름 · 본문이 있고 · 툴팁이 화면 안 · 판(clear)을 덮지 않는다
        static void MoveTipShown(UiShot s, VisualElement target, int move, VisualElement panel)
        {
            var flow = s.Made;
            if (target == null) throw new InvalidOperationException("툴팁을 볼 요소가 없다");
            s.Panel.Tick(3);
            flow.Stage.Tips.Show(target);
            s.Panel.Tick(3);
            var tips = flow.Stage.Tips;
            if (tips.Target != target) throw new InvalidOperationException(target.name + "에 툴팁이 걸리지 않았다");
            var labels = tips.Element.Query<Label>().ToList();
            string want = flow.Stage.T(Sinnoh.Fx.BattleFeed.MoveNameKey(flow.Ctx, move));
            if (labels.Count < 2 || labels[0].text != want || string.IsNullOrEmpty(labels[labels.Count - 1].text)) throw new InvalidOperationException(target.name + " 툴팁 = 「" + (labels.Count > 0 ? labels[0].text : "") + "」 ≠ " + want);
            var at = tips.Element.worldBound;
            var screen = flow.Stage.Root.worldBound;
            if (at.xMin < screen.xMin - 1f || at.yMin < screen.yMin - 1f || at.xMax > screen.xMax + 1f || at.yMax > screen.yMax + 1f) throw new InvalidOperationException("툴팁이 화면 밖: " + at);
            if (panel != null && at.Overlaps(panel.worldBound)) throw new InvalidOperationException("툴팁이 판을 덮는다: " + at + " / " + panel.worldBound);
        }

        // 결정 186: 스타팅 포켓몬의 칸 1에 기술을 넣어 둔 격파 판(SoloTrail.Defeat) — 결과 창의 기술 가르치기에서 원래 기술이 든 칸 단추에 마우스
        static void BattleTeachTip(UiShot s)
        {
            var flow = s.Flow();
            var pilot = FlowPilot.Start(flow, SoloTrail.Defeat);
            int given = -1;
            flow.Rebase(st =>
            {
                var p = st.Pokemon[st.Seats[0].Party[0]];
                int sig = flow.Ctx.Cards.Pokemon[p.Def].Signature;
                for (int m = 0; m < flow.Ctx.Cards.Moves.Count && given < 0; m++)
                    if (m != sig && flow.Stage.TOrNull(Sinnoh.Fx.BattleFeed.MoveNameKey(flow.Ctx, m) + ".text") != null) given = m;
                p.Moves[0] = given;
            });
            flow.Layout(s.Width, s.Height);
            if (!FlowPilot.Run(flow, pilot, () => Battle(flow, b => b.ShowsResult && b.AskingReward && b.Ready), ms => Pass(flow, ms), BattleStepMs, BattleStepMax * 10, true))
                throw new InvalidOperationException("칸 1에 기술을 넣은 격파 판이 기술 가르치기까지 가지 못했다(화면 " + (flow.Stage.Current != null ? flow.Stage.Current.Name : "보드") + ")");
            var battle = (BattleScreen)flow.Stage.Current;
            VisualElement button = null;
            for (int i = 0; i < battle.Rewards.Count; i++)
            {
                var a = battle.Rewards[i];
                if (a.Kind == R.AnswerKind.Pick && flow.Client.View.Pokemon[a.Target].Moves[a.Arg] == given) button = flow.Stage.Root.Q("battle-reward-" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            MoveTipShown(s, button, given, flow.Stage.Root.Q("battle-result-panel"));
        }

        // 결정 84 L11: 흰 실루엣이 오가는 중(2000ms)에 스페이스 바(GameFlow.SkipKey) → 착지로 건너뛴 뒤 한 프레임
        static void EvolveSkipped(UiShot s)
        {
            Evolving(s, 2000);
            var flow = s.Made;
            var evolve = flow.Stage.Current as EvolveScreen;
            if (evolve == null || evolve.Now != EvolveScreen.Mode.Show || !flow.SkipKey()) throw new InvalidOperationException("스페이스 바가 진화 연출을 건너뛰지 않았다");
            flow.Stage.Tweens.Advance(16);
            if (!evolve.Pose.To || evolve.Pose.Message != 2 || evolve.Pose.White != 0f) throw new InvalidOperationException("건너뛴 뒤가 착지 모습이 아니다(" + evolve.PoseMs + "ms)");
        }

        // 조무래기를 이긴 판의 공중날기 [출발] 뒤 ms(16행 — 컷인 1400 + 택시 3200, 결정 167)
        static void Flying(UiShot s, int ms)
        {
            var village = Freed(s);
            if (!village.PickDest(FirstDest(village)) || !village.PressFly()) throw new InvalidOperationException("공중날기가 거절됐다");
            s.Made.Stage.Tweens.Advance(ms);
        }

        // 첫 갈 수 있는 목적지
        // T18 ①: 이긴 판의 마을 창 → [지도에서 고르기] → then. 창이 오른쪽 기둥 · 가림막 없음 · 칠한 칸 = 합법 Fly 답의 마을 · 창이 아래 판 · 위 띠와 겹치지 않는다
        static void FlyMap(UiShot s, Action<VillageScreen> then)
        {
            var village = Freed(s);
            var flow = s.Made;
            if (!village.BeginMap() || flow.FlyPick != village) throw new InvalidOperationException("[지도에서 고르기]가 보드에서 고르기로 들어가지 않았다");
            var lit = new List<int>();
            foreach (int f in village.Dests) if (village.CanGo(f) && !lit.Contains(flow.Ctx.Fixtures.Fixtures[f].Cell)) lit.Add(flow.Ctx.Fixtures.Fixtures[f].Cell);
            if (lit.Count == 0 || flow.BoardMarks.Reach.Count != lit.Count || !lit.TrueForAll(c => HasCell(flow.BoardMarks.Reach, c))) throw new InvalidOperationException("보드에 칠한 마을이 합법 Fly 답과 다르다");
            if (flow.BoardMarks.Dims.Count == 0) throw new InvalidOperationException("갈 수 없는 마을(지금 선 마을)이 흐리지 않다");
            s.Panel.Tick(3);   // 창이 오른쪽 기둥에 자리를 잡고 카메라가 그 왼쪽으로 맞춘 뒤
            then(village);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            if (root.Q("board-village-scrim").resolvedStyle.display != DisplayStyle.None) throw new InvalidOperationException("보드에서 고르는데 가림막이 덮는다");
            Apart(root, "board-village-panel", "board-hud-dock");
            Apart(root, "board-village-panel", "board-hud-status");
        }

        static bool HasCell(IReadOnlyList<int> list, int v)
        {
            foreach (int x in list) if (x == v) return true;
            return false;
        }

        static int FirstDest(VillageScreen village)
        {
            foreach (int f in village.Dests) if (village.CanGo(f)) return f;
            throw new InvalidOperationException("갈 수 있는 마을이 없다");
        }

        /// <summary>[공개]를 누른 뒤 공개 단계(25행 700ms) 안의 두 순간: 뒤집기 가운데 전(뒷면) · 앞면으로 머무는 때(뒤집기 550‰ ~ 머묾 850‰ 사이).</summary>
        const int SpotFlipMs = 170, SpotFaceMs = 480;

        // [공개]를 누르고 그 시각만큼 보낸 뒤 띄운 토큰이 그려졌는지 본다(front = 앞면 그림까지)
        static void Reveal(UiShot s, GameFlow flow, int ms, bool front, bool art = true)
        {
            if (!flow.Hud.Press(BoardHud.Reveal)) throw new InvalidOperationException("[공개]가 거절됐다");
            flow.Stage.Tweens.Advance(ms);
            Raised(s, flow, front, art);
        }

        // 띄운 숨겨진 장소 토큰이 실제로 그려졌나 — 「앞 그림과 다르기만 하면 통과」로는 공개한 명령 안에서 빠진 토큰의 뒤집기가 사라진 것을 못 잡았다(규칙 끝 감사).
        // front = 다 뒤집혔다(art면 앞면 그림까지 — Gen board.json에 앞면이 있는 결과: 규토리 더미 · 떨어진 별 · 야영지)
        static GameFlow Raised(UiShot s, GameFlow flow, bool front, bool art = true)
        {
            var tokens = s.Board != null ? s.Board.Tokens : null;
            if (tokens == null || !tokens.RaisedDrawn) throw new InvalidOperationException("띄운 숨겨진 장소 토큰이 그려지지 않았다(RaisedCell " + (tokens != null ? tokens.RaisedCell : -9) + ")");
            if (front && (art && !tokens.RaisedHasFront || tokens.RaisedFlip < 0.5f)) throw new InvalidOperationException($"띄운 토큰이 앞면이 아니다(flip {tokens.RaisedFlip}, 앞면 그림 {tokens.RaisedHasFront})");
            if (!front && tokens.RaisedFlip >= 0.5f) throw new InvalidOperationException($"띄운 토큰이 아직 뒷면이어야 한다(flip {tokens.RaisedFlip})");
            return flow;
        }

        /// <summary>
        /// 결과 kind(stash · star · relic · alpha · camp)의 숨겨진 장소에 멈춘 모습: 혼자 판(StartSeats 1)을 열 때 내 말에서 가장 가까운 뒷면 숨겨진 장소의 정체를 그 결과의 토큰과 맞바꾼다
        /// (뒷면 토큰끼리 Def만 바꾼다 — 토큰 수 · 종류 수는 그대로) → <see cref="WalkToSpot"/>.
        /// </summary>
        static GameFlow SpotOf(UiShot s, string kind)
        {
            var ctx = s.Board.Ctx;
            bool Want(int def)
            {
                var item = ctx.Items.Items[def];
                switch (kind)
                {
                    case "stash": return Sinnoh.Rules.HiddenSpotRules.Stash(ctx, def, out _, out _);
                    case "star": return item.Key == Sinnoh.Rules.HiddenSpotRules.FallenStar;
                    case "relic": return item.Kind == Sinnoh.Rules.ItemKind.Artifact;
                    case "alpha": return item.Kind == Sinnoh.Rules.ItemKind.AlphaPokemon;
                    case "camp": return item.Key == Sinnoh.Rules.HiddenSpotRules.Campsite;
                    default: throw new ArgumentException(kind);
                }
            }
            var flow = Seats(s, 1, state =>
            {
                int me = state.Seats[0].Cell;
                var from = Sinnoh.Rules.MoveRules.Reachable(ctx.Board, me, 400, state.Options.ElevationMode);
                Sinnoh.Rules.TokenState goal = null, swap = null;
                foreach (var t in state.Tokens)
                    if (t.Kind == Sinnoh.Rules.TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0 && from[t.Cell] > 0 && from[t.Cell] != Sinnoh.Rules.MoveRules.Unreachable && (goal == null || from[t.Cell] < from[goal.Cell])) goal = t;
                if (goal == null) throw new InvalidOperationException("닿을 수 있는 숨겨진 장소가 없다");
                if (Want(goal.Def)) return;
                foreach (var t in state.Tokens)
                    if (t != goal && t.Kind == Sinnoh.Rules.TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && Want(t.Def)) { swap = t; break; }
                if (swap == null) throw new InvalidOperationException($"뒷면 {kind} 토큰이 없다");
                (goal.Def, swap.Def) = (swap.Def, goal.Def);
            });
            return WalkToSpot(flow);
        }

        // 가장 가까운 뒷면 숨겨진 장소까지 걸어가 멈춘다(턴을 넘겨 가며) → 「숨겨진 장소를 공개하겠습니까?」가 열린 모습
        static GameFlow ToSpot(UiShot s) => WalkToSpot(ToBoard(s, BoardReadyMs));

        static GameFlow WalkToSpot(GameFlow flow)
        {
            for (int i = 0; i < 60; i++)
            {
                if (!flow.InputOpen) { Pass(flow, BattleStepMs); continue; }
                var client = flow.Client;
                if (client.Choice(Sinnoh.Rules.ChoiceKind.HiddenSpot) != null) return flow;
                var view = client.View;
                int me = view.Seats[client.Seat].Cell, goal = -1, best = -1, bestLeft = int.MaxValue;
                var from = Sinnoh.Rules.MoveRules.Reachable(flow.Ctx.Board, me, 400, view.Options.ElevationMode);
                foreach (var t in view.Tokens)
                    if (t.Kind == Sinnoh.Rules.TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0 && from[t.Cell] > 0 && from[t.Cell] != Sinnoh.Rules.MoveRules.Unreachable && (goal < 0 || from[t.Cell] < from[goal])) goal = t.Cell;
                if (goal < 0) throw new InvalidOperationException("닿을 수 있는 숨겨진 장소가 없다");
                foreach (int cell in flow.Hud.Reach)
                {
                    int left = cell == goal ? 0 : Sinnoh.Rules.MoveRules.Reachable(flow.Ctx.Board, cell, 400, view.Options.ElevationMode)[goal];
                    if (left != Sinnoh.Rules.MoveRules.Unreachable && left < bestLeft) { best = cell; bestLeft = left; }
                }
                if (best >= 0 && bestLeft < from[goal]) { if (!flow.Hud.Walk(best)) throw new InvalidOperationException("이동이 거절됐다"); }
                else if (!flow.Hud.Press(BoardHud.EndTurn)) throw new InvalidOperationException("턴 종료가 거절됐다");
            }
            throw new InvalidOperationException("숨겨진 장소에 닿지 못했다");
        }

        // 쥐여 준 판(SoloTrail.Kit)에서 두 마리를 잡아 파티 셋이 된 내 턴에 정비 판을 연다
        static PartyScreen Maint(UiShot s)
        {
            var flow = Trail(s, SoloTrail.Kit, f => f.InputOpen && Sinnoh.Rules.PokemonRules.PartyCount(f.Client.View.Seats[f.Client.Seat]) >= 3);
            if (!flow.OpenParty()) throw new InvalidOperationException("정비 판이 열리지 않았다");
            return (PartyScreen)flow.Stage.Current;
        }

        // 결정 195: Maint 판에서 첫 포켓몬(스타팅)에게 전설 특수기술을 주고 배운 기술을 비운 뒤 정비 판을 다시 연다 — 칸 1 · 2가 전설 특수기술 줄인지 단정
        static PartyScreen LegendMaint(UiShot s)
        {
            Maint(s);
            var flow = s.Made;
            int starter = flow.Client.View.Seats[flow.Client.Seat].Party[0];
            flow.Rebase(st =>
            {
                var pk = st.Pokemon[starter];
                pk.HasLegendaryMove = true;
                for (int k = 0; k < pk.Moves.Length; k++) pk.Moves[k] = -1;
            });
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            for (int i = 0; i < 200 && !flow.CanOpenParty; i++) Pass(flow, 250);
            if (!flow.OpenParty()) throw new InvalidOperationException("정비 판이 다시 열리지 않았다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            foreach (var (slot, key) in new[] { ("board-party-slot-0-1", "screen.battle.move.alpha_strike"), ("board-party-slot-0-2", "screen.passive.beast_boost") })
            {
                var row = flow.Stage.Root.Q(slot);
                if (row == null || !row.ClassListContains(MonParts.LegendSlotClass) || !row.Query<Label>().ToList().Exists(l => l.text == flow.Stage.T(key)))
                    throw new InvalidOperationException(slot + "이 전설 특수기술 줄(" + flow.Stage.T(key) + ")이 아니다");
            }
            return (PartyScreen)flow.Stage.Current;
        }

        // 첫 포켓몬(스타팅 팽도리)의 기술 칸 1에 풀의 첫 카드(파도타기 — 물 타입)를 가르친다
        static void Taught(PartyScreen party)
        {
            if (!party.PickMove(0) || !party.PressSlot(0, 0)) throw new InvalidOperationException("기술 가르치기가 거절됐다");
        }

        // 풀에 남은 카드 가운데 첫 포켓몬이 배울 수 있는 첫 카드(엔진 검사 그대로 — 찬 칸에 넣으면 물음이 뜬다)
        static int OtherLearnable(PartyScreen party)
        {
            for (int n = 0; n < party.Pool.Count; n++)
            {
                if (!party.PickMove(n)) continue;
                bool ok = party.CanPut(0, 0);
                party.Cancel();
                if (ok) return n;
            }
            throw new InvalidOperationException("첫 포켓몬이 배울 수 있는 다른 기술 카드가 풀에 없다");
        }

        // 찾아 둔 판(볼을 쥐여 준 판)에서 [찾아보기]를 누르고 ms 뒤
        static GameFlow Searching(UiShot s, int ms)
        {
            var flow = Trail(s, SoloTrail.Catch, f => f.InputOpen && f.Hud.Shows(BoardHud.Search));
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException("[찾아보기]가 거절됐다");
            flow.Stage.Tweens.Advance(ms);
            return flow;
        }

        // 조우 창에서 첫 카드를 고르고 ms 뒤
        static GameFlow Picking(UiShot s, int ms)
        {
            var flow = Trail(s, SoloTrail.Catch, Encounter);
            if (!((EncounterScreen)flow.Stage.Current).Pick(0)) throw new InvalidOperationException("카드 고르기가 거절됐다");
            flow.Stage.Tweens.Advance(ms);
            return flow;
        }

        // 야생 배틀에서 첫 볼을 던지고 ms 뒤
        static GameFlow Throwing(UiShot s, int ms)
        {
            var flow = Trail(s, SoloTrail.Catch, f => Battle(f, b => b.BagOn));
            var screen = (BattleScreen)flow.Stage.Current;
            if (!screen.OpenBag() || !screen.PressBall(0)) throw new InvalidOperationException("볼 던지기가 거절됐다");
            flow.Stage.Tweens.Advance(ms);
            return flow;
        }

        static bool Encounter(GameFlow flow) => flow.Stage.Current is EncounterScreen e && e.Open;

        // 실제 진행 판(Defeat)을 그 모습까지 간 뒤 길잡이의 다음 걸음 하나(마을 [전체 회복] · 결과 창 [확인])를 누르고 연출 시각만 ms 보낸다
        static void DefeatMoment(UiShot s, Func<GameFlow, SoloPilot, bool> until, int ms)
        {
            var flow = s.Flow();
            var pilot = FlowPilot.Start(flow, SoloTrail.Defeat);
            flow.Layout(s.Width, s.Height);
            if (!FlowPilot.Run(flow, pilot, () => until(flow, pilot), x => Pass(flow, x), BattleStepMs, BattleStepMax * 10)) throw new InvalidOperationException("defeat: 그 모습까지 가지 못했다");
            if (!FlowPilot.Step(flow, pilot, true)) throw new InvalidOperationException("defeat: 다음 걸음이 거절됐다");
            flow.Stage.Tweens.Advance(ms);
        }

        // 조무래기의 기술 선택(사유가 있는 AI 답)이 화면 대기열에 들어오면 그 단계 시작까지 건너뛰고 연출 시각만 ms 보낸다
        static void AiMoment(UiShot s, int ms)
        {
            var flow = s.Flow();
            var pilot = FlowPilot.Start(flow, SoloTrail.Defeat);
            flow.Layout(s.Width, s.Height);
            void Wait(int x)
            {
                flow.Tick(x);
                if (!FxShots.AiQueued(flow)) flow.Stage.Tweens.Advance(x);
            }
            if (!FlowPilot.Run(flow, pilot, () => FxShots.AiQueued(flow), Wait, BattleStepMs, BattleStepMax * 10)) throw new InvalidOperationException("defeat: AI 자막까지 가지 못했다");
            flow.Skip(flow.MsUntil(st => st.Row == Sinnoh.Fx.AiLineBeats.Row && Sinnoh.Fx.AiLineBeats.Answer(st) != null));
            flow.Stage.Tweens.Advance(ms);
        }

        static bool Battle(GameFlow flow, Func<BattleScreen, bool> when) => flow.Stage.Current is BattleScreen b && flow.Battle != null && when(b);

        // 우선권 토큰을 묻는 내 차례(Defeat 판의 첫 야생 배틀 — 스피드 동률, SoloPilotTests가 지킨다)
        static bool TokenAsk(GameFlow flow) => Battle(flow, b => b.Open && b.AskingToken);

        // 찾아 둔 판을 시작해(셋업은 화면으로) 길잡이가 화면의 함수로 눌러 가며 그 모습이 될 때까지 간다. confirm = 결과 창의 [확인]도 누른다
        static GameFlow Trail(UiShot s, SoloTrail trail, Func<GameFlow, bool> until, bool confirm = true)
        {
            var flow = s.Flow();
            var pilot = FlowPilot.Start(flow, trail);
            flow.Layout(s.Width, s.Height);
            if (!FlowPilot.Run(flow, pilot, () => until(flow), ms => Pass(flow, ms), BattleStepMs, BattleStepMax * 10, confirm))
            {
                var choice = flow.Client.FirstChoice();
                var answer = choice != null ? pilot.Decide(flow.Ctx, flow.Client.View, choice, flow.Client.Legal(choice)) : null;
                throw new InvalidOperationException(trail.Name + ": 그 모습까지 가지 못했다(찾아 둔 판이 달라졌나 — SoloPilotTests). 받을 화면 없는 선택 " + flow.NotReadyShown + "번 · 멈춘 곳: 선택 "
                    + (choice != null ? choice.Kind + "/" + choice.Sub : "없음") + " · 길잡이의 답 " + answer + " · 화면 " + (flow.Stage.Current != null ? flow.Stage.Current.Name : "보드") + " · 입력 " + flow.InputOpen);
            }
            return flow;
        }

        /// <summary>
        /// 스타팅 포켓몬 하나로 조무래기 둘을 이기는 판(드물다 — 시험 <c>BattleFeedTests.WinGame</c>과 같은 판): 시드 · AI 시드 · 고향 마을 색인(합법 답 순서) · 스타팅 포켓몬 색인, 첫 트레이너 카드, 늘 첫 기술.
        /// </summary>
        public const ulong WinSeed = 179, WinAiSeed = 99;
        public const int WinVillage = 0, WinStarter = 1;

        /// <summary>배틀 캡처에서 한 번에 보내는 시간(ms)과 걸음 수 상한 — 방장 멈춤(연출 길이)이 지나야 AI가 답한다.</summary>
        public const int BattleStepMs = 500, BattleStepMax = 400;

        /// <summary>기술을 누른 뒤 굴림이 멈추고 주사위 둘을 다 센 때(던짐 400 + 굴림 위쪽 끝 1600 + 틈 250 + 130 → 2380, 고리 100이 꺼진 뒤 — 굴림 단계는 2510에 끝난다).</summary>
        public const int RollShotMs = 2500;

        // 세션 시각과 연출 시각을 함께 보낸다(편집 모드에는 프레임이 없다)
        internal static void Pass(GameFlow flow, int ms)
        {
            flow.Tick(ms);
            flow.Stage.Tweens.Advance(ms);
        }

        // 보드에서 [도전]을 눌러 배틀 화면을 열고, 내가 기술을 고를 차례가 될 때까지 보낸다
        static GameFlow ToBattle(UiShot s)
        {
            var flow = ToBoard(s, BoardReadyMs);
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을 칸에서 1막 조무래기 배틀을 열 수 있어야 한다)");
            MyBattleTurn(flow);
            return flow;
        }

        // 내 배틀 선택(기술 고르기)이 열리고 입력이 열릴 때까지. 우선권 토큰을 물으면 쓴다. 배틀이 끝나면 false
        internal static bool MyBattleTurn(GameFlow flow)
        {
            for (int i = 0; i < BattleStepMax; i++)
            {
                var screen = flow.Stage.Current as BattleScreen;
                if (screen == null)
                {
                    // 배틀 화면은 조우 전환(22행)의 가운데에 열린다 — 그때까지 보낸다
                    if (flow.Battle == null) throw new InvalidOperationException("배틀 화면이 열리지 않았다");
                    Pass(flow, BattleStepMs);
                    continue;
                }
                if (screen.ShowsResult) return false;
                if (screen.Open && screen.AskingToken) screen.PressToken(true);
                else if (screen.Open && screen.AskingOrder) screen.PressOrder(0);   // 결정 72 효과 순서: 기본 순서
                else if (screen.Open && screen.AskingFate) screen.PressFatePass();   // 운명 창: 안 씀(기본값)
                else if (screen.Open && screen.AskingAsk) screen.PressAsk(false);
                else if (screen.Open && screen.AskingPick) { if (!screen.PressPick(-1)) screen.PressPick(0); }
                else if (screen.Open && screen.Moves.Count > 0) return true;
                else Pass(flow, BattleStepMs);
            }
            throw new InvalidOperationException("배틀에서 내 차례가 오지 않았다");
        }

        // 기술을 내고, 상대(AI)가 답하기 전의 모습에서 멈춘다(내 선택이 없고 배틀이 살아 있다). 연출 시각만 보내 내 공격의 연출과 클릭 막기를 지난다(세션 시각은 그대로 — AI는 아직 답하지 않는다)
        static GameFlow ToEnemyTurn(UiShot s)
        {
            var flow = ToBattle(s);
            for (int i = 0; i < BattleStepMax; i++)
            {
                var screen = (BattleScreen)flow.Stage.Current;
                if (!screen.PressMove(0)) throw new InvalidOperationException("기술 답이 거절됐다");
                for (int k = 0; k < BattleStepMax && !screen.Ready; k++) flow.Stage.Tweens.Advance(BattleStepMs);
                if (!screen.Ready) throw new InvalidOperationException("공격 연출이 끝나지 않았다");
                if (flow.Battle.Ended) throw new InvalidOperationException("상대 차례가 오기 전에 배틀이 끝났다(이 시드에서는 「상대의 차례」를 찍을 수 없다)");
                if (flow.Client.FirstChoice() == null) return flow;
                if (!MyBattleTurn(flow)) throw new InvalidOperationException("상대 차례가 오기 전에 배틀이 끝났다");
            }
            throw new InvalidOperationException("상대 차례가 오지 않았다");
        }

        // 첫 기술만 눌러 배틀을 끝까지 간다 → 결과 창
        static GameFlow ToBattleEnd(UiShot s)
        {
            var flow = ToBattle(s);
            for (int i = 0; i < BattleStepMax; i++)
            {
                if (!MyBattleTurn(flow)) return flow;
                if (!((BattleScreen)flow.Stage.Current).PressMove(0)) throw new InvalidOperationException("기술 답이 거절됐다");
            }
            throw new InvalidOperationException("배틀이 끝나지 않았다");
        }

        /// <summary>셋업을 끝낸 뒤 입력이 열릴 때까지 넉넉히 보내는 시간(첫 턴의 내 턴 컷신 500 + 클릭 막기 300, 걷기 항목은 보기 시간 1500 + 클릭 막기 300).</summary>
        public const int BoardReadyMs = 2500;

        /// <summary>능력 문구가 가장 긴 트레이너 카드(다이아 무녀: 86 + 152자 — 백금 무녀도 같은 길이).</summary>
        public const string LongCard = "diamond_acolyte";

        // 그 카드를 받는 판의 시드를 찾는다(실제 게임 상태로 찍으려고 — 화면에 가짜 카드를 넣지 않는다). 셋업의 첫 보기만 본다
        static ulong LongCardSeed(Sinnoh.Rules.Ctx ctx)
        {
            for (ulong seed = 1; seed < 5000; seed++)
            {
                var session = Sinnoh.Rules.Session.SoloGame.Start(ctx, new Sinnoh.Rules.Session.SoloSettings(), seed, AiSeed, new NoAgent(), 0);
                foreach (int card in session.ViewFor(Sinnoh.Rules.Session.SoloGame.Seat).Seats[0].TrainerCardOffer)
                    if (ctx.Cards.TrainerCards[card].Key == LongCard) return seed;
            }
            throw new InvalidOperationException(LongCard + "를 받는 시드를 찾지 못했다");
        }

        // 시드를 찾는 판은 셋업의 첫 보기만 본다 — 가상 응답자가 답할 일이 없다
        sealed class NoAgent : Sinnoh.Rules.Session.IAgent
        {
            public Sinnoh.Rules.Session.AgentDecision Decide(Sinnoh.Rules.Session.AgentInput input, Sinnoh.Rules.Session.AgentBudget budget) =>
                throw new InvalidOperationException("시드 찾기 판에서는 AI가 답하지 않는다");
        }

        // 셋업을 끝내 보드로 가고(고정 시드 · 무쇠시티 · 첫 스타팅 포켓몬 · 둘째 트레이너 카드), 카메라를 놓은 뒤 연출 시각을 ms만큼 보낸다
        static GameFlow ToBoard(UiShot s, int ms, Sinnoh.Rules.Session.SoloSettings settings = null, GameFlow flow = null, bool opening = false)
        {
            flow = ToPick(s, settings, flow);
            var pick = (SetupPickScreen)flow.Stage.Current;
            pick.PickStarter(0);
            pick.PickCard(1);
            if (!pick.Confirm()) throw new InvalidOperationException("셋업 확정이 거절됐다");
            flow.Layout(s.Width, s.Height);
            // 게임 시작의 고른 뒤·공개 → 숨겨진 장소 배치 → 서막 카드 → 조무래기 놓기 → 1막 카드를 지나 첫 턴의 항목 앞까지. opening이면 서막 카드 앞까지만
            flow.Stage.Tweens.Advance(opening ? flow.MsUntil(step => step.Row == Sinnoh.Fx.ActBeats.Row) : flow.OpeningMs);
            if (ms > 0) flow.Stage.Tweens.Advance(ms);
            return flow;
        }

        // 게임 시작의 연출(서막 카드 3600 → 조무래기 놓기 1800 → 1막 카드 3600) 안의 한 순간: 서막 카드가 시작된 뒤 ms(그 앞의 셋업 연출 · 숨겨진 장소 배치는 지나서)
        static GameFlow Opening(UiShot s, int ms) => ToBoard(s, ms, opening: true);

        // 고향 마을 연출(2행)의 한 순간: 마을 [확정]을 누른 뒤 ms
        static GameFlow VillageDrop(UiShot s, int ms)
        {
            var flow = PickVillage(Started(s));
            flow.Layout(s.Width, s.Height);
            if (!((SetupVillageScreen)flow.Stage.Current).Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
            flow.Stage.Tweens.Advance(ms);
            return flow;
        }

        // 고른 뒤의 연출(1·3·5행)의 한 순간: [확정]을 누른 뒤 그 단계의 ms 지점
        static GameFlow Picked(UiShot s, Func<Sinnoh.Fx.FxStep, bool> step, int ms)
        {
            var flow = ToPick(s);
            var pick = (SetupPickScreen)flow.Stage.Current;
            pick.PickStarter(0);
            pick.PickCard(1);
            if (!pick.Confirm()) throw new InvalidOperationException("셋업 확정이 거절됐다");
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.MsUntil(step));
            flow.Stage.Tweens.Advance(ms);
            return flow;
        }

        // 좌석 여럿인 확인용 판: seats명 가운데 together명(기본 = 모두)을 좌석 1의 고향 마을 칸에 모으고, 카메라·말 양식·표지 시각을 놓는다
        static GameFlow Stack(UiShot s, int seats, BoardCameraRig.Zoom zoom = BoardCameraRig.Zoom.Region, int yaw = 0, PieceStyle style = PieceStyle.Dot, long ms = 0, int together = -1)
        {
            var flow = s.Flow();
            flow.PieceStyle = style;
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings(), seats, Seed, AiSeed, state =>
            {
                int n = together < 0 ? seats : together;
                for (int i = 1; i < n; i++) state.Seats[i].Cell = state.Seats[0].Cell;
            });
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            s.Rig.Set(zoom, yaw, s.Rig.Target);
            flow.StackMs = ms;
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (flow.Hud == null || flow.Hud.Stacks.Count != 1) throw new InvalidOperationException("묶음 표지가 하나가 아니다: " + (flow.Hud == null ? "HUD 없음" : flow.Hud.Stacks.Count.ToString()));
            return flow;
        }

        // ── 남은 M4 연출 행(6·9·13·19·99·102행) ──

        // 좌석 여럿인 확인용 판을 열어 입력이 열릴 때까지 보낸다
        internal static GameFlow Seats(UiShot s, int seats, Action<Sinnoh.Rules.GameState> prepare = null)
        {
            var flow = s.Flow();
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings(), seats, Seed, AiSeed, prepare);
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            return flow;
        }

        // 지금 턴인 좌석의 턴을 끝낸다(내 좌석이면 HUD의 [턴 종료], 아니면 그 좌석의 답으로)
        internal static void EndActive(GameFlow flow)
        {
            if (DeclineUbSkip(flow)) return;   // 라운드 끝의 「UB 턴을 건너뛰겠습니까?」(결정 84 U2) — 건너뛰지 않는다(UiShots.Ub2.cs)
            int active = flow.Client.View.ActiveSeat;
            bool ok = active == flow.Client.Seat ? flow.Hud.Press(BoardHud.EndTurn) : flow.AnswerSeat(active, a => a.Kind == Sinnoh.Rules.AnswerKind.EndTurn);
            if (!ok) throw new InvalidOperationException("좌석 " + (active + 1) + "의 턴 종료가 거절됐다");
        }

        // 대기열에서 그 조건의 단계를 찾는다(없으면 null)
        static Sinnoh.Fx.FxStep Queued(GameFlow flow, Func<Sinnoh.Fx.FxStep, bool> match)
        {
            foreach (var entry in flow.Fx.Sequencer.Queued)
                foreach (var st in entry.Steps)
                    if (match(st)) return st;
            return null;
        }

        // 그 단계의 시작에서 ms 지난 순간으로 보낸다
        static void Into(UiShot s, GameFlow flow, Sinnoh.Fx.FxStep step, int ms)
        {
            flow.Stage.Tweens.Advance(flow.MsUntil(st => ReferenceEquals(st, step)));
            flow.Stage.Tweens.Advance(ms);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
        }

        // 턴 시작(6행)의 한 순간: 턴을 넘겨 가며 조건(내 턴인가 · round > 0이면 라운드의 첫 턴, 0이면 아닌 턴)에 맞는 턴 시작 단계를 찾아 그 안의 ms로
        static GameFlow TurnStart(UiShot s, int seats, bool mine, int round, int ms)
        {
            var flow = Seats(s, seats);
            for (int i = 0; i < seats * 3; i++)
            {
                EndActive(flow);
                var step = Queued(flow, st => st.Row == Sinnoh.Fx.TurnStartBeats.Row && st.Tag is TurnStartInfo info && info.Mine == mine && (info.Round > 0) == (round > 0));
                if (step != null)
                {
                    Into(s, flow, step, ms);
                    return flow;
                }
                flow.Stage.Tweens.Advance(flow.QueueLeftMs);
            }
            throw new InvalidOperationException("그 조건의 턴 시작 단계가 대기열에 오지 않았다(좌석 여럿인 판은 턴 시작 6행을 그린다)");
        }

        // 도전 보호 해제(9행): 다음 턴인 좌석에 보호를 걸어 둔 판(시험용 상태 — 실제로는 트레이너 배틀이 건다)에서 턴을 넘겨 그 단계의 ms로. ms < 0이면 넘기기 전(배지가 붙어 있는 모습)
        static GameFlow Shield(UiShot s, int ms)
        {
            var flow = Seats(s, 2, state => state.Seats[(state.ActiveSeat + 1) % state.Seats.Length].Protected = true);
            if (ms < 0)
            {
                s.Panel.Tick(3);
                return flow;
            }
            EndActive(flow);
            var step = Queued(flow, st => st.Row == Sinnoh.Fx.ShieldBeats.Row);
            if (step == null) throw new InvalidOperationException("보호 해제 단계(9행)가 대기열에 없다");
            Into(s, flow, step, ms);
            return flow;
        }

        // 결정 72 효과 순서 창: 좌석 둘 판(확인용)의 두 좌석을 마을 밖 한 칸에 세우고 트레이너전. 좌석 2의 포켓몬은 졸음 + 배운 칸에 핥기(늘 마비) —
        // 핥기로 마비된 내 포켓몬이 바로 다음 차례에 공격하면 순서를 묻는다. 주사위는 prepare에서 같은 답으로 미리 돌려(사본) 묻게 되는 시드를 고른다(가짜 화면이 아니라 실제 판).
        // pick ≥ 0이면 그 줄을 누르고 공격 연출이 다 돈 뒤(계산 줄)에서 멈춘다
        static GameFlow OrderAsk(UiShot s, int pick)
        {
            var flow = s.Flow();
            var ctx = flow.Ctx;
            int lick = ctx.Cards.MoveIds.IndexOf("lick");
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings(), 2, Seed, AiSeed, state => OrderStage(ctx, state, lick));
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            for (int i = 0; i < BattleStepMax; i++)
            {
                var screen = flow.Stage.Current as BattleScreen;
                if (screen != null && screen.ShowsResult) throw new InvalidOperationException("순서를 묻기 전에 배틀이 끝났다(prepare의 미리 돌리기와 화면 흐름이 어긋났다)");
                if (screen != null && screen.Open && screen.AskingOrder)
                {
                    if (pick < 0) return flow;
                    if (!screen.PressOrder(pick)) throw new InvalidOperationException("순서 줄이 거절됐다");
                    for (int k = 0; k < BattleStepMax && !screen.Ready; k++) flow.Stage.Tweens.Advance(BattleStepMs);
                    return flow;
                }
                var mine = flow.Client.FirstChoice();
                if (mine != null && mine.Kind == Sinnoh.Rules.ChoiceKind.MapAction && flow.InputOpen)
                    flow.Answer(mine, flow.Client.Legal(mine).Find(a => OrderPick(0, a, lick)));
                else if (screen != null && screen.Open && screen.AskingToken) screen.PressToken(true);
                else if (screen != null && screen.Open && screen.Moves.Count > 0) screen.PressMove(0);
                else flow.AnswerSeat(1, a => OrderPick(1, a, lick));
                Pass(flow, BattleStepMs);
            }
            throw new InvalidOperationException("효과 순서를 묻는 데까지 가지 못했다");
        }

        // 순서 창 판의 답(미리 돌리기와 화면이 같은 답을 낸다): 도전 · 토큰 사용 · 좌석 2 = 핥기, 좌석 1 = 첫 기술(화면의 첫 기술 줄)
        static bool OrderPick(int seat, Sinnoh.Rules.ChoiceAnswer a, int lick)
        {
            switch (a.Kind)
            {
                case Sinnoh.Rules.AnswerKind.Challenge: return a.Target >= 0;
                case Sinnoh.Rules.AnswerKind.UseMove: return seat != 1 || a.Target == lick;
                case Sinnoh.Rules.AnswerKind.Accept:
                case Sinnoh.Rules.AnswerKind.Pick: return true;
                default: return false;
            }
        }

        static void OrderStage(Sinnoh.Rules.Ctx ctx, Sinnoh.Rules.GameState state, int lick)
        {
            // 두 좌석을 첫 좌석 고향 마을 옆의 마을 밖 칸에(마을 밖이면 도전을 거절하지 못한다 — 응답 선택 없이 바로 배틀)
            int home = state.Seats[0].Cell, cell = -1;
            for (int dir = 0; dir < 6 && cell < 0; dir++)
            {
                int n = ctx.Board.Neighbor(home, dir);
                if (n >= 0 && !Sinnoh.Rules.BattleRules.InVillage(ctx, n)) cell = n;
            }
            if (cell < 0) throw new InvalidOperationException("고향 마을 옆에 마을 밖 칸이 없다");
            foreach (var seat in state.Seats) seat.Cell = cell;
            var foe = state.Pokemon[state.Seats[1].Party[0]];
            foe.Moves[0] = lick;
            Sinnoh.Rules.StatusRules.Inflict(ctx, foe, Sinnoh.Rules.StatusEffect.Drowsy);
            for (ulong seed = 1; seed < 4000; seed++)
            {
                var t = state.Clone();
                t.Rng = new Sinnoh.Rules.Pcg32(seed);
                if (!OrderReached(ctx, t, lick)) continue;
                state.Rng = new Sinnoh.Rules.Pcg32(seed);
                return;
            }
            throw new InvalidOperationException("순서를 묻게 되는 시드가 없다");
        }

        // 사본에서 같은 답으로 돌려 좌석 1에게 효과 순서를 묻게 되나
        static bool OrderReached(Sinnoh.Rules.Ctx ctx, Sinnoh.Rules.GameState t, int lick)
        {
            var legal = new List<Sinnoh.Rules.ChoiceAnswer>();
            for (int step = 0; step < 40; step++)
            {
                if (t.Pending.Count == 0) return false;
                var c = t.Pending[0];
                if (c.Kind == Sinnoh.Rules.ChoiceKind.Reaction && c.Sub == Sinnoh.Rules.ChoiceSub.EffectOrder) return c.Seat == 0;
                if (step > 0 && t.Battle == null) return false;
                Sinnoh.Rules.Engine.Legal(t, ctx, c, legal);
                var a = legal.Find(x => OrderPick(c.Seat, x, lick));
                if (a == null || !Sinnoh.Rules.Engine.Submit(t, ctx, c.Seat, Sinnoh.Rules.Command.AnswerTo(c.Id, a), null).Accepted) return false;
            }
            return false;
        }

        // 같은 칸의 말을 만남(13행): 턴이 아닌 좌석을 턴인 좌석에서 두 걸음 떨어진 칸에 세워 두고, 턴인 좌석이 그 칸으로 걸어 들어가 닿은 뒤 ms
        static GameFlow Meet(UiShot s, int ms)
        {
            int target = -1;
            var flow = s.Flow();
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings(), 2, Seed, AiSeed, state =>
            {
                var walker = state.Seats[state.ActiveSeat];
                var steps = Sinnoh.Rules.MoveRules.Reachable(flow.Ctx.Board, walker.Cell, walker.MovesLeft, state.Options.ElevationMode);
                for (int c = 0; c < steps.Length && target < 0; c++) if (steps[c] == 2) target = c;
                if (target < 0) throw new InvalidOperationException("두 걸음 떨어진 칸이 없다");
                state.Seats[(state.ActiveSeat + 1) % state.Seats.Length].Cell = target;
            });
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            bool walked = flow.Client.View.ActiveSeat == flow.Client.Seat ? flow.Hud.Walk(target) : flow.WalkActive(target);
            if (!walked) throw new InvalidOperationException("턴인 좌석이 남의 말이 선 칸으로 걷지 못했다");
            var walk = Queued(flow, st => st.Row == 110);
            flow.StackMs = 0;   // 묶음 표지의 시각을 붙든다(두 번 찍어 같은 그림)
            Into(s, flow, walk, Sinnoh.Fx.FxWalk.LandedAt((Sinnoh.Rules.Moved)walk.Events[0]) + ms);
            if (!flow.Hud.MeetShown) throw new InvalidOperationException("남의 말이 선 칸에 닿았는데 「!」가 뜨지 않았다");
            return flow;
        }

        // 규토리 줍기(19행): 조우 덱 셋을 비운 한 좌석 판(시험용 상태 — 실제 판에서는 그 칸에 맞는 카드가 없을 때만)에서 나무 칸에 서서 [찾아보기] → 조우 없음 → [규토리 줍기], 그 단계의 ms
        static GameFlow Apricorns(UiShot s, int ms)
        {
            var flow = s.Flow();
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings(), 1, Seed, AiSeed, state => Sinnoh.Fx.ApricornBeats.Stage(flow.Ctx, state, state.ActiveSeat));
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException("[찾아보기]가 거절됐다(나무 칸에 서 있어야 한다)");
            flow.Stage.Tweens.Advance(flow.QueueLeftMs);
            flow.Layout(s.Width, s.Height);
            if (!flow.Hud.Press(BoardHud.TakeApricorns)) throw new InvalidOperationException("[규토리 줍기]가 거절됐다(빈 찾아보기 뒤에 열린다)");
            var step = Queued(flow, st => st.Row == Sinnoh.Fx.ApricornBeats.Row);
            if (step == null) throw new InvalidOperationException("규토리 줍기 단계(19행)가 대기열에 없다");
            Into(s, flow, step, ms);
            return flow;
        }

        // 동시 공개(99행): 좌석 여럿인 판을 셋업부터 화면으로 연다 — 고향 마을은 좌석마다 첫 답, 고르는 화면에서 내가 [확정]하고 좌석 2가 답한 모습(stage 0).
        // stage 1이면 마지막 좌석까지 답해 공개 단계의 ms로
        static GameFlow PickSeats(UiShot s, int seats, int stage, int ms)
        {
            var flow = s.Flow();
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings(), seats, Seed, AiSeed, liveSetup: true);
            flow.Layout(s.Width, s.Height);
            for (int guard = 0; !(flow.Stage.Current is SetupPickScreen); guard++)
            {
                if (guard > 40) throw new InvalidOperationException("좌석 여럿인 판의 셋업이 고르는 화면까지 가지 못했다");
                if (flow.Stage.Current is SetupVillageScreen village && village.Watched < 0)   // 남이 고르는 동안의 보기 화면(T2)은 넘긴다
                {
                    village.Pick(0);
                    if (!village.Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
                }
                else
                {
                    bool any = false;
                    for (int seat = 1; seat < seats && !any; seat++) any = flow.AnswerSeat(seat);
                    if (!any && flow.Fx.Sequencer.IsEmpty) throw new InvalidOperationException("셋업에서 아무 좌석도 답하지 못했다");
                }
                flow.Stage.Tweens.Advance(flow.QueueLeftMs);
                flow.Layout(s.Width, s.Height);
            }
            var pick = (SetupPickScreen)flow.Stage.Current;
            pick.PickStarter(0);
            pick.PickCard(1);
            if (!pick.Confirm()) throw new InvalidOperationException("셋업 확정이 거절됐다");
            flow.Stage.Tweens.Advance(flow.QueueLeftMs);
            for (int seat = 1; seat < seats - 1 + stage; seat++)
                for (int k = 0; k < 2; k++)
                    if (!flow.AnswerSeat(seat)) throw new InvalidOperationException("좌석 " + (seat + 1) + "의 고르기가 거절됐다");
            if (stage == 0)
            {
                flow.Stage.Tweens.Advance(flow.QueueLeftMs);
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
                if (pick.SeatsDone != seats - 1) throw new InvalidOperationException("「선택 완료」 좌석 수: " + pick.SeatsDone);
                return flow;
            }
            var step = Queued(flow, st => st.Row == Sinnoh.Fx.RevealAllBeats.Row);
            if (step == null) throw new InvalidOperationException("동시 공개 단계(99행)가 대기열에 없다");
            Into(s, flow, step, ms);
            return flow;
        }

        // 묶음 표지 한가운데에 마우스를 올린다(칸 고르기는 표지 밑의 칸 그대로 — GameFlow.Pointer와 같은 길)
        static GameFlow StackHover(GameFlow flow)
        {
            int cell = flow.Client.View.Seats[0].Cell;
            if (!flow.Hud.Stacks.RectOf(cell, out var rect)) throw new InvalidOperationException("좌석 1의 칸에 묶음 표지가 없다");
            int at = flow.Hud.Stacks.CellAt(rect.center);
            flow.Hud.Hover(-1, at);
            if (flow.Hud.Info.Subject == null) throw new InvalidOperationException("묶음 표지에 올려도 공개 정보 카드가 뜨지 않았다");
            return flow;
        }

        // 지금 화면 안(HUD 판 밖)에 보이는 토큰 칸 가운데 화면 가운데에 가장 가까운 것 — 없으면 −1
        static int TokenCell(GameFlow flow, UiShot s, Sinnoh.Rules.TokenKind kind, bool notMine = true)
        {
            var view = flow.Client.View;
            float scale = flow.Stage.Grade.PanelScale, w = s.Width / scale, h = s.Height / scale, best = float.MaxValue;
            int found = -1;
            foreach (var t in view.Tokens)
            {
                if (t.Kind != kind || t.Cell < 0 || t.Destroyed || (notMine && t.Cell == view.Seats[flow.Client.Seat].Cell)) continue;
                if (!flow.CellPoint(t.Cell, out var at) || at.x < 40f || at.x > w - 440f || at.y < h * 0.3f || at.y > h * 0.7f) continue;
                float d = (at - new Vector2(w * 0.4f, h * 0.5f)).sqrMagnitude;
                if (d < best) { best = d; found = t.Cell; }
            }
            return found;
        }

        // 지금 갈 수 있는 칸 가운데 두 걸음 떨어진 첫 칸(엔진의 합법 답 순서)
        static int TwoSteps(GameFlow flow)
        {
            var view = flow.Client.View;
            var me = view.Seats[flow.Client.Seat];
            var steps = Sinnoh.Rules.MoveRules.Reachable(flow.Ctx.Board, me.Cell, me.MovesLeft, view.Options.ElevationMode);
            foreach (int cell in flow.Hud.Reach) if (steps[cell] == 2) return cell;
            throw new InvalidOperationException("두 걸음 떨어진 갈 수 있는 칸이 없다");
        }

        // 턴을 turns번 넘긴 뒤의 내 턴(혼자 하기: 턴 하나 = 라운드 하나 — 시간대가 라운드마다 하나씩 지난다, 결정 70 ⑥). battle = 거기서 [도전]해 내 배틀 차례까지
        static GameFlow AtRound(UiShot s, int turns, bool battle)
        {
            var flow = ToBoard(s, BoardReadyMs);
            for (int i = 0; i < turns; i++)
            {
                if (!flow.Hud.Press(BoardHud.EndTurn)) throw new InvalidOperationException("턴 종료가 거절됐다");
                flow.Stage.Tweens.Advance(BoardReadyMs);
            }
            if (!battle) return flow;
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다");
            MyBattleTurn(flow);
            return flow;
        }

        static GameFlow EndTurn(UiShot s, int ms)
        {
            var flow = ToBoard(s, BoardReadyMs);
            if (!flow.Hud.Press(BoardHud.EndTurn)) throw new InvalidOperationException("턴 종료가 거절됐다");
            flow.Stage.Tweens.Advance(ms);
            return flow;
        }

        /// <summary>보드 위에 얹히는 화면(패널 바탕이 투명): 보드 씬을 같은 크기로 그린 그림 위에 겹쳐 찍는다 — 실행 중에는 같은 씬의 3D 보드가 패널 아래에 보인다.</summary>
        public static readonly HashSet<string> OverBoard = new HashSet<string>
        {
            "title", "title-web", "title-host", "title-update", "title-update-auto", "title-update-progress", "title-update-fail", "solo", "solo-overwrite", "solo-tip", "settings", "solo-protagonist", "settings-job", "settings-lang", "menu-settings-lang", "board-hud-job", "board-hud-walk-job", "board-hud-nocards", "setup-village", "setup-village-picked", "setup-pick", "setup-pick-picked", "setup-pick-long", "game-board",
            "setup-village-glide", "setup-village-drop", "setup-village-landed", "setup-deal", "setup-pick-locked", "setup-card-locked", "setup-starter-open", "setup-starter-shown", "setup-card-reveal", "board-spots-drop", "board-spots-placed",
            "board-hud-myturn", "board-hud", "board-hud-hover", "board-hud-tip", "board-hud-walk", "board-hud-walked", "board-hud-band", "board-hud-cut", "board-hud-round2", "board-hud-tip-end",
            "board-menu", "board-menu-ask", "board-menu-unsaved", "board-menu-ask-unsaved", "board-menu-failed", "board-menu-ask-failed", "board-resumed", "title-continue", "save-list", "save-delete-ask", "save-list-empty",
            "board-stack-2", "board-stack-3", "board-stack-6", "board-stack-3-next", "board-stack-3-full", "board-stack-3-close", "board-stack-3-yaw", "board-stack-6-job", "board-stack-6-job-full", "board-stack-6-job-close",
            "board-stack-4-split", "board-stack-3-hover", "board-stack-6-hover", "board-stack-3-walk",
            "act-card-drop", "act-card-prologue", "board-grunts-drop", "act-card-1", "board-log", "board-info-seat", "board-info-grunt", "board-info-spot", "board-tokens-close", "board-tokens-yaw",
            "battle-select", "battle-hover", "battle-roll", "battle-enemy-turn", "battle-result", "battle-result-win",
            "board-hud-search", "encounter", "encounter-2", "encounter-1", "encounter-hover",
            "battle-wild", "battle-teach", "battle-teach-tip", "battle-taught", "battle-coral", "battle-fled", "battle-bag", "battle-captured", "battle-sendin", "battle-sendin-6", "battle-switch", "battle-partyfull",
            "board-hud-bang", "encounter-flip", "enter-wipe", "battle-intro", "battle-capture", "battle-capture-shake", "battle-capture-ok", "battle-result-lines",
            "board-party-plain", "board-party", "board-party-teach", "board-party-ask", "board-party-item", "mon-detail",
            "board-village", "board-village-used", "board-village-fly", "board-fly-map", "board-fly-map-why", "board-fly-map-chip", "board-fly", "board-flown", "board-trainer", "board-spot", "board-spot-flip", "board-spot-revealed",
            "board-craft", "board-craft-suck", "board-craft-make", "board-craft-plus", "board-craft-stew", "board-craft-done", "encounter-craft-button", "encounter-craft",
            "board-village-bank", "board-village-bank-trade", "board-village-bank-flip", "board-village-bank-used",
            "board-evolve", "board-evolve-half", "board-evolve-branch", "board-evolve-start", "board-evolve-white", "board-evolve-flash", "board-evolve-done", "board-evolve-skip", "board-evolve-teach", "board-evolve-teach-now", "board-evolve-teach-now-filled", "board-evolve-teach-tip", "board-fly-lift", "board-fly-land",
            "board-spot-stash-flip", "board-spot-stash-face", "board-spot-star-flip", "board-spot-star-face", "board-spot-relic-flip", "board-spot-relic-face",
            "board-spot-alpha-flip", "board-spot-alpha-face", "board-spot-camp-flip", "board-spot-camp-face",
            "board-path-spot", "board-path-spot-tip", "board-path-via", "board-path-via-tip", "board-spot-walk", "board-spot-walk-tip", "board-spot-walk-passed",
            "row-06-banner", "row-06-chips", "row-102-round", "row-09-shield", "row-09-shield-swell", "row-09-shield-fall", "row-13-meet", "row-19-drop", "row-19-gain",
            "row-99-picking", "row-99-flip", "row-99-revealed",
            "board-heal-flash", "board-whiteout-dark", "board-whiteout-clear", "board-respawn-heal", "battle-ai-line",
            "battle-maint", "battle-maint-party", "settings-fx", "board-menu-settings", "battle-calc-held", "board-hud-picked", "board-hud-picked-tip", "board-hud-picked-cancel-tip", "board-hud-round3", "battle-time-evening", "battle-time-night", "battle-order", "battle-order-hover", "battle-order-spare", "battle-token-ask", "battle-token-hover", "board-party-drag", "board-party-drag-item", "battle-maint-drag",
            "fx-dust-village", "fx-dust-fly", "fx-spot-star", "fx-heal", "fx-starter-open",
            "row-94-heart-offer", "row-94-heart-spin", "row-94-heart-merge", "row-04-act3", "row-04-finale", "row-95-ask", "row-95-ask-tip", "row-95-hero-burn", "row-95-traitor-red",
            "row-96-order", "row-96-chips", "row-97-result-mons", "row-97-result", "row-97-traitor-won", "row-97-trail",
            "row-42-discard", "row-42-discard-pick", "row-42-tear", "board-fate-hand", "board-fate-hand-tip", "row-14-warp-pick", "row-45-pass", "row-14-warp", "row-14-land",
            "battle-fate-button", "battle-fate-view", "row-34-chips6", "row-43-ask", "row-45-pass3", "row-43-reroll", "row-43-locked", "row-43-end", "legend-slot-party", "legend-slot-teach", "legend-slot-mon", "row-44-ask", "row-44-shadow",
            "row-07-star", "row-07-star-flip", "row-08-fate", "row-98-challenge", "row-98-trade", "row-98-trade-many", "row-98-timeout",
            "board-stamp", "board-stamp-none", "board-stamp-full", "board-stamp-tip", "row-47-drop", "row-47-slam", "row-47-rewards", "row-47-cards", "row-70-stamp", "board-challenge", "row-51-bang", "row-51-slide", "row-51-vs", "row-51-hold", "board-trade-button", "board-trade", "board-trade-picked", "board-trade-full", "board-trade-bad", "board-trade-wait", "row-52-gather", "row-52-cross", "row-52-glow", "board-trade-offturn", "board-trade-pick", "board-trade-pick-picked", "board-trainer-platinum", "board-trainer-undivided",
            "board-shrine-button", "board-shrine-offer", "board-shrine-offer-named", "board-shrine-offer-picked", "row-31-fly", "row-31-pillar", "row-31-cards", "board-shrine-pick", "board-shrine-pick-tip", "row-32-bless",
            "board-shrine-apricorns", "board-shrine-follower", "row-34-flip", "row-34-tally", "row-34-bolt", "row-34-fall", "row-34-land", "row-34-spark", "row-34-chip",
            "row-32-share", "row-32-beam", "row-34-seats", "row-34-wait", "row-34-tie", "row-33-dark", "row-33-fall", "row-33-land", "row-33-rain",
            "board-shrine-stews", "board-shrine-exchange", "board-shrine-exchange-card", "board-shrine-move", "board-shrine-teach", "board-shrine-teach-tip",
            "board-hud-party-1", "board-hud-party-3", "board-hud-party-5", "board-hud-party-6",
            "board-hud-party-status", "board-hud-party-status-3", "board-hud-party-status-tip", "board-party-status", "board-party-other-status", "board-party-max",
            "board-hud-res", "board-hud-res-rich", "board-hud-res-6", "board-hud-res-ub6", "board-hud-res-tut",
            "tut-layer-center", "tut-layer-hole", "tut-layer-cell", "tut-layer-hint", "tut-layer-lost", "tut-layer-bot", "tut-layer-lines",
            "tut-main-ch0", "tut-main-ch1", "tut-main-ch2", "tut-main-ch3", "tut-main-ch4", "tut-main-ch5", "tut-main-ch6", "tut-main-ch7", "tut-main-ch7-preview2", "tut-main-ch7-preview3", "tut-anchors", "tut-ext-e01", "tut-ext-e02", "tut-ext-e08",
            "tut-click-s101", "tut-click-s104", "tut-click-s308", "tut-click-s309", "tut-click-s404", "tut-click-s406", "tut-click-s407", "tut-click-s504", "tut-click-s506", "tut-click-s610", "tut-click-s701",
            "tut-click-e01-s004", "tut-click-e01-s007", "tut-click-e01-s014",
            "tut-part-s101-2", "tut-part-s104-2", "tut-part-s104-3", "tut-part-s404-2", "tut-part-e01-s014-2", "tut-part-e01-s014-3", "tut-part-e10-s008-2",
            "tut-a96-full", "tut-a96-face", "tut-a96-face-center", "tut-a96-band-yongsik", "tut-a96-band-bitna", "tut-a96-preview2-act", "tut-a96-preview2-lake",
        };

        /// <summary>캡처용 게임 시드(고정 — 같은 선택지가 찍힌다).</summary>
        public const ulong Seed = 20261112, AiSeed = 7;

        /// <summary>캡처용 저장 시각(고정, UTC ms — 2026-11-13 09:00 KST).</summary>
        public const long SaveClock = 1794528000000;

        public const string StringsPath = "Assets/Sinnoh/Data/Json/strings.ko.json";

        /// <summary>
        /// 저장 화면 캡처: 임시 폴더를 저장 폴더로 쓰는 흐름을 만들고, 거기에 저장을 만든 뒤(games 2 = 두 걸음 걸은 판 · 한 시간 뒤의 다른 주인공·마을 판(턴을 두 번 넘김) + 읽을 수 없는 저장 하나, 0 = 빈 폴더)
        /// <paramref name="then"/>으로 화면을 연다. 화면이 만들어진 뒤 폴더를 지운다 — 화면은 목록을 만들 때 읽은 것으로 그린다.
        /// </summary>
        static void WithSaves(UiShot s, Action<GameFlow> then, int games = 2)
        {
            string root = Path.Combine(Path.GetTempPath(), "sinnoh-uishots-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                long now = SaveClock;
                var flow = s.Flow(root, () => now);
                if (games >= 2)
                {
                    ToBoard(s, BoardReadyMs, flow: flow);
                    if (!flow.Hud.Walk(TwoSteps(flow))) throw new InvalidOperationException("이동이 거절됐다");
                    flow.Stage.Tweens.Advance(BoardReadyMs);
                    flow.OpenTitle();
                    // 자동 저장은 한 칸이다(결정 70 ③) — 첫 판은 전에 판마다 만들던 이름의 저장으로 옮겨 둔다(그런 저장은 지워지지 않고 목록에 남는다)
                    Directory.Move(Path.Combine(root, GameFlow.SoloSlot), Path.Combine(root, "solo-20261112-090000-0a11"));
                    now += 3600000;
                    flow.StartSolo(new Sinnoh.Rules.Session.SoloSettings { Protagonist = "hgss_001_lyra" }, Seed + 1, AiSeed);
                    var village = (SetupVillageScreen)flow.Stage.Current;
                    village.Pick(village.Villages.Count - 1);
                    if (!village.Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
                    flow.Stage.Tweens.Advance(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
                    var pick = (SetupPickScreen)flow.Stage.Current;
                    pick.PickStarter(1);
                    pick.PickCard(0);
                    if (!pick.Confirm()) throw new InvalidOperationException("셋업 확정이 거절됐다");
                    flow.Stage.Tweens.Advance(flow.OpeningMs + BoardReadyMs);
                    // 턴을 두 번 넘긴다(3라운드) — 두 칸의 요약이 다르게 보이게
                    for (int t = 0; t < 2; t++)
                    {
                        if (!flow.Hud.Press(BoardHud.EndTurn)) throw new InvalidOperationException("턴 종료가 거절됐다");
                        for (int i = 0; i < 40 && !flow.InputOpen; i++) Pass(flow, 500);
                    }
                    string made = flow.SaveId;
                    flow.OpenTitle();
                    Directory.CreateDirectory(Path.Combine(root, "solo-20261101-120000-0bad"));
                    File.WriteAllText(Path.Combine(root, "solo-20261101-120000-0bad", Sinnoh.Data.SaveStore.HeadFile), "{");
                    // 다른 형식 판의 저장(「다른 버전에서 저장한 게임입니다.」): 방금 판의 머리에서 판 숫자만 올린다
                    string head = File.ReadAllText(Path.Combine(root, made, Sinnoh.Data.SaveStore.HeadFile));
                    string format = "\"FormatVersion\": " + Sinnoh.Rules.Session.SaveRecord.CurrentFormat;
                    if (!head.Contains(format)) throw new InvalidOperationException("머리에 형식 판이 없다");
                    Directory.CreateDirectory(Path.Combine(root, "solo-20261102-090000-0new"));
                    File.WriteAllText(Path.Combine(root, "solo-20261102-090000-0new", Sinnoh.Data.SaveStore.HeadFile), head.Replace(format, "\"FormatVersion\": " + (Sinnoh.Rules.Session.SaveRecord.CurrentFormat + 1)));
                }
                then(flow);
                flow.Layout(s.Width, s.Height);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);   // 찍는 동안 더 쓰지 않는다
            }
        }

        // 쓰기 실패한 판의 게임 메뉴: 머리 파일을 잠근 채 턴을 넘겨 스냅샷·머리 쓰기를 실패시킨다(로그 줄은 쓰인다 — 판은 이어 간다)
        static void MenuFailed(UiShot s, bool ask) => WithSaves(s, flow =>
        {
            ToBoard(s, BoardReadyMs, flow: flow);
            using (new FileStream(Path.Combine(flow.SaveRoot, flow.SaveId, Sinnoh.Data.SaveStore.HeadFile), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                if (!flow.Hud.Press(BoardHud.EndTurn)) throw new InvalidOperationException("턴 종료가 거절됐다");
                for (int i = 0; i < 40 && !flow.InputOpen; i++) Pass(flow, 500);
            }
            if (flow.SaveStatus != SaveStatus.Failed) throw new InvalidOperationException("쓰기 실패가 나지 않았다(" + flow.SaveStatus + ")");
            flow.OpenMenu();
            if (ask) ((BoardMenuScreen)flow.Stage.Current).AskQuit();
        }, games: 0);

        static GameFlow Started(UiShot s, Sinnoh.Rules.Session.SoloSettings settings = null, GameFlow flow = null)
        {
            flow = flow ?? s.Flow();
            flow.StartSolo(settings ?? new Sinnoh.Rules.Session.SoloSettings(), Seed, AiSeed);
            return flow;
        }

        // 목업 r4/setup과 같은 마을(무쇠시티)을 고른다
        internal static GameFlow PickVillage(GameFlow flow)
        {
            var screen = (SetupVillageScreen)flow.Stage.Current;
            int stark = flow.Ctx.Fixtures.Ids.IndexOf("village.stark");
            for (int i = 0; i < screen.Villages.Count; i++) if (screen.Villages[i] == stark) screen.Pick(i);
            if (screen.Picked < 0) throw new InvalidOperationException("무쇠시티가 선택지에 없다");
            return flow;
        }

        // 고르는 화면까지: 마을을 확정하고 고향 마을 연출(2행)을 지난다. dealMs = 카드가 들어오는 연출(1행)의 그 시각에서 멈춘다(−1 = 다 들어오고 입력이 열린 뒤)
        static GameFlow ToPick(UiShot s, Sinnoh.Rules.Session.SoloSettings settings = null, GameFlow made = null, int dealMs = -1)
        {
            var flow = PickVillage(Started(s, settings, made));
            if (!((SetupVillageScreen)flow.Stage.Current).Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
            if (dealMs < 0) flow.Stage.Tweens.Advance(flow.QueueLeftMs);
            else
            {
                flow.Stage.Tweens.Advance(flow.MsUntil(step => step.Row == Sinnoh.Fx.SetupRows.Deal));
                flow.Stage.Tweens.Advance(dealMs);
            }
            return flow;
        }

        static KeyValuePair<string, Action<UiShot>> Screen(string name, Action<UiShot> build) => new KeyValuePair<string, Action<UiShot>>(name, build);

        // 자동 업데이트 알림 상자 한 단계(결정 161 — 서버 · 설치 없이 GameFlow.FakeUpdate): 상자 · 그 단추가 있고 화면 안 · 글 넘침 없음 · 메뉴 · 설명 창 · 아래 띠 · 로고와 안 겹침,
        // 받는 중이면 덮개가 화면을 덮고 메뉴를 누르지 못한다, 실패는 까닭 한 줄(가장 긴 「한도」 줄). 주소를 열면 실패
        static void UpdateBox(UiShot s, UpdateStep step, string button)
        {
            var flow = s.Flow();
            flow.OpenUrl = url => throw new InvalidOperationException("캡처가 주소를 열었다: " + url);
            flow.OpenTitle(TitleScreen.Solo);
            flow.FakeUpdate("0.2.0", step, 12900000, 30198989, step == UpdateStep.Failed ? flow.Stage.T("screen.title.update.fail.later") : null);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var title = (TitleScreen)flow.Stage.Current;
            if (!title.UpdateShown || title.UpdateStepShown != step) throw new InvalidOperationException("알림 상자가 " + step + " 단계가 아니다");
            if (root.Q<Button>(button) == null) throw new InvalidOperationException("단추 없음: " + button);
            RoomFit(s, flow, "title-update");
            foreach (var other in new[] { "title-menu", "title-desc", "title-foot", "title-logo" }) Apart(root, "title-update", other);
            bool blocked = root.Q("title-update-block") != null;
            if (blocked != (step == UpdateStep.Fetch)) throw new InvalidOperationException("덮개가 " + (blocked ? "있다" : "없다") + ": " + step);
            if (step == UpdateStep.Fetch)
            {
                if (root.Q<Label>("title-update-amount")?.text != "12.3 / 28.8 MB") throw new InvalidOperationException("MB 글: " + root.Q<Label>("title-update-amount")?.text);
                var fill = root.Q("title-update-fill").worldBound.width / root.Q("title-update-bar").worldBound.width;
                if (fill < 0.38f || fill > 0.48f) throw new InvalidOperationException("막대 채움 " + fill);
                if (title.Activate(0)) throw new InvalidOperationException("받는 동안 메뉴가 눌렸다");
                var block = root.Q("title-update-block").worldBound;
                if (!block.Contains(root.Q("title-menu").worldBound.center)) throw new InvalidOperationException("덮개가 메뉴를 덮지 않는다");
            }
            if (step == UpdateStep.Failed && root.Q<Button>("title-update-get") == null) throw new InvalidOperationException("실패에 [받기] 없음");
        }

        static Action<UiShot> Gallery(string tipTarget) => s =>
        {
            var tips = KitGallery.Build(s.Panel.Root, s.Skin, s.Grade);
            if (tipTarget == null) return;
            s.Panel.Tick(3);   // 대상이 배치된 뒤에 올려야 툴팁 자리가 잡힌다
            tips.Show(s.Panel.Root.Q(tipTarget));
        };

        [MenuItem("Sinnoh/Capture/Ui")]
        static void Menu()
        {
            // 보드 위 화면(OverBoard)을 찍으려고 보드 씬을 연다 — 열려 있던 씬의 저장 안 한 변경을 먼저 묻는다(BoardShots와 같다)
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Capture("all", "all", "", OutDir, true);
        }

        public static void Run()
        {
            bool ok = false;
            try
            {
                string sizes = Arg("-uiSize", "");
                Lang = Arg("-uiLang", "ko");
                if (Strings(Lang) == null) throw new ArgumentException("-uiLang은 ko · en: " + Lang);
                overflowSink = Array.IndexOf(Environment.GetCommandLineArgs(), "-uiOverflow") >= 0 ? new List<string>() : null;
                ok = Capture(Arg("-uiScreen", "all"), Arg("-uiGrade", sizes == "" ? "all" : "none"), sizes, Arg("-uiOut", OutDir), Array.IndexOf(Environment.GetCommandLineArgs(), "-uiRepeat") >= 0);
            }
            catch (Exception e) { Debug.LogException(e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        /// <summary>찍을 크기 하나: 등급의 기준 크기(이름 = 높이)이거나 -uiSize로 준 창 크기(이름 = 폭x높이, 등급은 그 창이 고르는 것).</summary>
        struct Shot
        {
            public string Label;
            public int Width, Height;
            public UiGrade Grade;
        }

        static bool Capture(string screen, string grade, string sizes, string outDir, bool repeat)
        {
            try { return CaptureAll(screen, grade, sizes, outDir, repeat); }
            finally { CloseBoard(); }
        }

        static bool CaptureAll(string screen, string grade, string sizes, string outDir, bool repeat)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogError("[UiShots] 그래픽 장치 없음(-nographics): 찍지 않는다");
                return false;
            }
            var screens = new List<string>();
            foreach (string want in screen.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                foreach (var s in AllScreens) if ((want == "all" || s.Key == want) && !screens.Contains(s.Key)) screens.Add(s.Key);
            var grades = new List<Shot>();
            foreach (var g in Grades) if (grade == "all" || g.Height.ToString() == grade) grades.Add(new Shot { Label = g.Height.ToString(), Width = g.Width, Height = g.Height, Grade = g });
            foreach (string size in sizes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var wh = size.Split('x');
                if (wh.Length != 2 || !int.TryParse(wh[0], out int w) || !int.TryParse(wh[1], out int h) || w < 1 || h < 1) { grades.Clear(); break; }
                grades.Add(new Shot { Label = w + "x" + h, Width = w, Height = h, Grade = UiGrade.For(w, h) });
            }
            if (screens.Count == 0 || grades.Count == 0)
            {
                var names = new List<string>();
                foreach (var s in AllScreens) names.Add(s.Key);
                Debug.LogError($"[UiShots] 모르는 화면·등급·크기: -uiScreen {screen} -uiGrade {grade} -uiSize {sizes} — 화면: all, {string.Join(", ", names)}(쉼표로 여럿) / 등급: all, 720, 1080, 1440, 2160 / 크기: <폭>x<높이>[,…]");
                return false;
            }

            Directory.CreateDirectory(outDir);
            var fails = new List<string>();
            var layoutLogs = new List<string>();
            var json = new StringBuilder("{\"images\":[");
            int images = 0;
            foreach (string name in screens)
                foreach (var shot in grades)
                {
                    var g = shot.Grade;
                    string file = name + "-" + shot.Label;
                    var sw = Stopwatch.StartNew();
                    Color32[] px;
                    try { px = Shoot(name, g, layoutLogs, shot.Width, shot.Height, fails); }
                    catch (Exception e) when (overflowSink != null)
                    {
                        // 넘침 보고(-uiOverflow — 언어끼리 견주기)에서는 장면 하나의 단정 예외(글자 넘침 Fit 등)로 멈추지 않고 적은 뒤 다음 장면으로
                        fails.Add($"{file} 예외: {e.Message}");
                        overflowSink.Add(name + "-" + shot.Width + "x" + shot.Height + "\t예외: " + e.Message);
                        continue;
                    }
                    long ms = sw.ElapsedMilliseconds;
                    if (px == null) { Debug.LogError("[UiShots] Gen 없음(글꼴·창틀): rebuild-all 먼저"); return false; }
                    WritePng(Path.Combine(outDir, file + ".png"), px, shot.Width, shot.Height);
                    int colors = Colors(px);
                    if (colors < 2) fails.Add($"{file} 빈 그림(색 {colors})");
                    int diff = -1;
                    string again = Path.Combine(outDir, file + "-repeat.png");
                    File.Delete(again);
                    if (repeat)
                    {
                        var px2 = Shoot(name, g, layoutLogs, shot.Width, shot.Height);
                        diff = 0;
                        for (int i = 0; i < px.Length; i++) if (!px[i].Equals(px2[i])) diff++;
                        if (diff > 0)
                        {
                            WritePng(again, px2, shot.Width, shot.Height);
                            fails.Add($"{file} 두 번 찍어 다른 픽셀 {diff}");
                        }
                    }
                    if (images++ > 0) json.Append(',');
                    json.Append("{\"name\":\"").Append(file).Append("\",\"screen\":\"").Append(name).Append("\",\"grade\":").Append(g.Height)
                        .Append(",\"tokens\":").Append(g.TokenHeight).Append(",\"scale\":").Append(g.PanelScale)
                        .Append(",\"width\":").Append(shot.Width).Append(",\"height\":").Append(shot.Height).Append(",\"colors\":").Append(colors)
                        .Append(",\"repeatDiff\":").Append(repeat ? diff.ToString() : "null").Append(",\"ms\":").Append(ms).Append('}');
                }
            foreach (string log in layoutLogs) fails.Add("배치 흔들림: " + log);

            json.Append("],\"layoutLogs\":").Append(layoutLogs.Count).Append(",\"ok\":").Append(fails.Count == 0 ? "true" : "false").Append(",\"fails\":[");
            for (int i = 0; i < fails.Count; i++) json.Append(i > 0 ? "," : "").Append('"').Append(fails[i].Replace("\\", "/").Replace("\"", "'")).Append('"');
            json.Append("]}\n");
            File.WriteAllText(Path.Combine(outDir, "ui-shots.json"), json.ToString(), new UTF8Encoding(false));
            if (overflowSink != null)
            {
                File.WriteAllLines(Path.Combine(outDir, "overflows.txt"), overflowSink, new UTF8Encoding(false));
                Debug.Log($"[UiShots] 글 넘침 보고({Lang}): {overflowSink.Count}줄 → {outDir}/overflows.txt");
            }

            if (fails.Count == 0) Debug.Log($"[UiShots] 통과: 그림 {images}장{(repeat ? " 두 번 찍어 다른 픽셀 0" : "")} → {outDir}/ui-shots.json");
            else Debug.LogError($"[UiShots] 실패 {fails.Count}: {string.Join("; ", fails)}");
            return fails.Count == 0;
        }

        /// <summary>
        /// 화면 하나를 등급 크기 RT에 처음부터 만들어 그리고 픽셀을 돌려준다(폭 × 높이 = 등급의 기준 크기, 아래 줄부터 — <c>GetPixels32</c> 순서).
        /// Gen 본체(글꼴·창틀)가 없으면 null. 그리는 동안 나온 「Layout update …」 로그는 <paramref name="layoutLogs"/>에 모은다.
        /// 보드 위 화면(<see cref="OverBoard"/>)은 보드 씬을 열어(편집 모드) 화면 흐름이 놓은 말·카메라대로 보드를 그리고 그 위에 겹친다 — 시험에서는 부르지 않는다.
        /// </summary>
        /// <param name="fails">찍은 화면에서 더 본 것 가운데 어긋난 것을 여기에 더한다(<see cref="Inspect"/> — 배틀 화면의 DS 자리). null이면 보지 않는다.</param>
        public static Color32[] Shoot(string screen, UiGrade grade, List<string> layoutLogs, int width = 0, int height = 0, List<string> fails = null)
        {
            if (width <= 0) width = grade.Width;
            if (height <= 0) height = grade.Height;
            Action<UiShot> build = null;
            foreach (var s in AllScreens) if (s.Key == screen) build = s.Value;
            if (build == null) throw new ArgumentException("모르는 화면: " + screen, nameof(screen));
            return Shoot(build, OverBoard.Contains(screen), grade, layoutLogs, width, height, screen, fails);
        }

        /// <summary>Screens에 없는 장면(목업 — <c>UiMockups</c>)도 같은 길로 찍는다. 크기는 꼭 준다.</summary>
        internal static Color32[] Shoot(Action<UiShot> build, bool overBoard, UiGrade grade, List<string> layoutLogs, int width, int height, string screen = null, List<string> fails = null)
        {
            // 보드 씬은 겉모양을 만들기 전에 연다 — 씬을 열면 안 쓰는 에셋 정리가 돌아, 먼저 만든 실행 중 글꼴 자산이 지워지고 글자가 기본 글꼴로 그려진다
            BoardBoot board = null;
            BoardCameraRig rig = null;
            if (overBoard) OpenBoard(out board, out rig);
            var skin = UiSkin.Load();
            if (skin == null) return null;
            Application.LogCallback watch = (message, stack, type) => { if (message.Contains("Layout update")) layoutLogs.Add(message); };
            Application.logMessageReceived += watch;
            try
            {
                using (var panel = new UiShotPanel(width, height, grade.PanelScale, overBoard))
                {
                    var shot = new UiShot { Panel = panel, Skin = skin, Grade = grade, Width = width, Height = height, Board = board, Rig = rig };
                    if (overBoard) board.ShowDemoSeats();   // 앞 장면이 세운 말을 처음(시험 판)으로
                    build(shot);
                    panel.Tick(6);
                    if (shot.Made != null) shot.Made.Layout(width, height);
                    var px = panel.Read();
                    if (fails != null) Inspect(screen, shot, px, fails);
                    // 글 넘침 보고(-uiOverflow — M9 언어 ②): 찍은 화면 전체의 WorkUi.Overflows를 줄로 모은다(실패로 치지 않는다 — 언어끼리 견줘 영어에서만 넘치는 칸을 찾는다)
                    if (overflowSink != null && screen != null)
                    {
                        foreach (string bad in WorkUi.Overflows(panel.Root)) overflowSink.Add(screen + "-" + width + "x" + height + "\t" + bad);
                        // 떠 있는 툴팁(kit-tip)이 화면 밖으로 나갔나(M9 영어 넘침 전수 — 툴팁 표본: 글 넘침은 위 줄이 본다)
                        panel.Root.Query<VisualElement>("kit-tip").ForEach(tip =>
                        {
                            if (tip.resolvedStyle.display == DisplayStyle.None || tip.resolvedStyle.visibility != Visibility.Visible) return;
                            Rect t = tip.worldBound, r = panel.Root.worldBound;
                            bool inside = t.xMin >= r.xMin - 0.5f && t.yMin >= r.yMin - 0.5f && t.xMax <= r.xMax + 0.5f && t.yMax <= r.yMax + 0.5f;
                            overflowSink.Add(screen + "-" + width + "x" + height + "\t" + (inside ? "툴팁 화면 안" : "툴팁 화면 밖: " + t + " · 화면 " + r));
                        });
                    }
                    return overBoard ? Over(Board(shot.Rig, width, height), px) : px;
                }
            }
            finally
            {
                Application.logMessageReceived -= watch;
                UiSkin.Release(skin);
                IconArt.Lang = null;   // 영문 아이콘 장면(UiShots.EnIcons.cs)이 넣은 언어 갈고리를 되돌린다
            }
        }

        /// <summary>
        /// 배틀 화면의 DS 요소가 설 자리(물리 px, 왼쪽 위 기준 — 04 §4-3): 256×192의 정수배(720 벌 ×2 · 1080 벌 ×3, 패널 배율만큼 더)이고
        /// 가운데 필드(위 띠 = 720 벌 50 · 1080 벌 95(결정 64) · 독 150을 뺀 자리) 한가운데. 표준 크기에서는 §4-3 표의 값이다 — (384, 118)·512×384 / (576, 187)·768×576 / (768, 236)·1024×768 / (1152, 374)·1536×1152.
        /// </summary>
        public static RectInt DsRect(UiGrade grade, int width, int height)
        {
            int unit = grade.TokenHeight / 360 * grade.PanelScale;   // DS 배율: 720 벌 2 · 1080 벌 3, × 패널 배율
            int top = (grade.TokenHeight == 720 ? 50 : 95) * grade.PanelScale, dock = 150 * grade.TokenHeight / 720 * grade.PanelScale;
            int w = DsStage.Width * unit, h = DsStage.Height * unit;
            return new RectInt((width - w) / 2, top + (height - top - dock - h) / 2, w, h);
        }

        // 그려지나: 자신은 보이고(visibility), 자신과 위 요소 모두 display가 있고 불투명도가 0이 아니다
        static bool Drawn(VisualElement el)
        {
            if (el.resolvedStyle.visibility != Visibility.Visible) return false;
            for (var e = el; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.opacity <= 0f) return false;
            return true;
        }

        // 찍은 화면에서 더 보는 것: 배틀 화면이면 DS 요소의 자리·크기(§4-3)와 그 안이 그려졌는지(무대·도트·상자 — 색이 여럿)
        static void Inspect(string screen, UiShot shot, Color32[] px, List<string> fails)
        {
            var ds = shot.Panel.Root.Q("battle-ds");
            if (ds == null)
            {
                if (screen.StartsWith("battle-", StringComparison.Ordinal)) fails.Add($"{screen}-{shot.Width}x{shot.Height} DS 요소가 없다");
                return;
            }
            int scale = shot.Grade.PanelScale;
            var wb = ds.worldBound;
            var got = new Rect(wb.x * scale, wb.y * scale, wb.width * scale, wb.height * scale);
            var want = DsRect(shot.Grade, shot.Width, shot.Height);
            string at = $"{screen}-{shot.Width}x{shot.Height}";
            if (got.x != want.x || got.y != want.y || got.width != want.width || got.height != want.height)
                fails.Add($"{at} DS 자리 {got.x},{got.y} {got.width}×{got.height} ≠ {want.x},{want.y} {want.width}×{want.height}(04 §4-3)");
            var colors = new HashSet<Color32>();
            for (int y = want.y; y < want.yMax && y < shot.Height; y += 3)
                for (int x = want.x; x < want.xMax && x < shot.Width; x += 3)
                    colors.Add(px[(shot.Height - 1 - y) * shot.Width + x]);
            if (colors.Count < 16) fails.Add($"{at} DS 화면이 비었다(색 {colors.Count} — 셰이더·Gen 무대 그림)");
            // 글자 자리(배틀 화면 장면 모두): DS 층의 글(체력 상자 이름 · 메시지)은 DsFont 폭으로, 독·기둥의 글은 WorkUi.Overflows로 — 잘리거나 넘치거나 겹치면 실패
            var battle = shot.Made != null ? shot.Made.Stage.Current as BattleScreen : null;
            if (battle != null)
            {
                if (battle.Ds != null) foreach (string cut in battle.Ds.Cut) fails.Add($"{at} DS 글 잘림: {cut}");
                // 톡 커지는 중인 칩(scale — 응징 배지·자물쇠)은 제 크기로 돌려 놓고 잰다(그림은 이미 찍었다 — 늘어난 크기는 배치가 아니다)
                foreach (string line in new[] { "battle-me-extra", "battle-foe-extra" })
                {
                    var extra = shot.Panel.Root.Q(line);
                    if (extra != null) foreach (var child in extra.Children()) child.style.scale = StyleKeyword.Null;
                }
                shot.Panel.Tick(3);
                foreach (string name in new[] { "battle-dock", "battle-info" })
                    foreach (string bad in WorkUi.Overflows(shot.Panel.Root.Q(name))) fails.Add($"{at} 글자 넘침({name}): {bad}");
                // 결정 170: [운명의 변화 n/5]는 독 차례 칸 안(스피드 줄 아래) — 칸 밖으로 밀려나거나 위 줄과 겹치지 않는다
                var fateButton = shot.Panel.Root.Q(BattleScreen.FateButton);
                var turnBox = shot.Panel.Root.Q("battle-turn");
                if (fateButton != null)
                {
                    // 단추는 차례 칸의 마지막 줄(혼자 서거나 · 단추 하나뿐인 맨 아래 단추 줄 옆)
                    var line = fateButton.parent == turnBox ? fateButton : fateButton.parent;
                    if (turnBox == null || line.parent != turnBox) fails.Add($"{at} [운명의 변화]가 독 차례 칸에 없다");
                    else
                    {
                        var fb = fateButton.worldBound;
                        var tb = turnBox.worldBound;
                        if (fb.xMin < tb.xMin - 0.5f || fb.xMax > tb.xMax + 0.5f || fb.yMin < tb.yMin - 0.5f || fb.yMax > tb.yMax + 0.5f) fails.Add($"{at} [운명의 변화]가 차례 칸 밖으로 나간다({fb} · 칸 {tb})");
                        int at2 = turnBox.IndexOf(line);
                        if (at2 > 0 && turnBox[at2 - 1].worldBound.yMax > line.worldBound.yMin + 0.5f) fails.Add($"{at} [운명의 변화] 줄이 위 줄({turnBox[at2 - 1].name})과 겹친다");
                    }
                }
                // 결정 169: 포켓몬 칸의 마우스 오버 카드는 화면 안 · 독과 DS를 가리지 않는다
                var monTip = battle.MonTipElement;
                if (battle.MonTipShown >= 0 && monTip != null && monTip.panel != null)
                {
                    var mt = monTip.worldBound;
                    var screenBox = shot.Panel.Root.worldBound;
                    if (mt.xMin < screenBox.xMin - 0.5f || mt.yMin < screenBox.yMin - 0.5f || mt.xMax > screenBox.xMax + 0.5f || mt.yMax > screenBox.yMax + 0.5f) fails.Add($"{at} 포켓몬 카드가 화면 밖으로 나간다({mt})");
                    foreach (string other in new[] { "battle-dock", "battle-ds-frame" })
                    {
                        var o = shot.Panel.Root.Q(other);
                        if (o != null && o.worldBound.Overlaps(mt)) fails.Add($"{at} 포켓몬 카드가 {other}을 가린다(카드 {mt} · {o.worldBound})");
                    }
                    foreach (string bad in WorkUi.Overflows(monTip)) fails.Add($"{at} 글자 넘침(mon-tip): {bad}");
                }
                // 독의 포켓몬 고르기 줄(내보내기·교체): 칸이 줄 밖으로 넘치면 말없이 잘려 누를 수 없다(M5 마감 검토 — 파티 6)
                shot.Panel.Root.Query<VisualElement>(className: "sn-battle-bench").ForEach(row =>
                {
                    foreach (var tile in row.Children())
                        if (tile.worldBound.xMax > row.worldBound.xMax + 0.5f || tile.worldBound.yMax > row.worldBound.yMax + 0.5f)
                            fails.Add($"{at} 포켓몬 고르기 칸 {tile.name}이 줄 밖으로 넘친다({tile.worldBound} · 줄 {row.worldBound})");
                });
                // 기라티나 「위력 +n」 칩(44행 — PC 층)이 DS 메시지 창(아래 48/192)을 덮지 않는다(DsStage.Cut은 PC 층이 덮는 것을 못 본다)
                var power = shot.Panel.Root.Q("battle-fate-power");
                var shadow = shot.Panel.Root.Q("battle-fate-shadow");
                if (power != null && shadow != null && battle.ShadowShown > 0f && power.resolvedStyle.opacity > 0f)
                {
                    float messageTop = shadow.worldBound.yMin + shadow.worldBound.height * DsStage.MessageRect.y / (DsStage.MessageRect.y + DsStage.MessageRect.height);
                    if (power.worldBound.yMax > messageTop + 0.5f) fails.Add($"{at} 「위력 +n」 칩이 DS 메시지 창을 덮는다({power.worldBound} · 메시지 창 위 {messageTop})");
                }
                // 좌석 칩의 「보호」 배지(위 띠 — HUD 층)가 배틀 화면의 덮개(위 띠 아래부터 덮는다)에 걸려 잘리지 않는다(M5 마감 답 반영 나-2 — looks-bugs 5)
                var scrim = shot.Panel.Root.Q(className: "sn-battle-scrim");
                if (scrim != null)
                    shot.Panel.Root.Query<VisualElement>(className: "sn-hud-shield").ForEach(badge =>
                    {
                        if (badge.worldBound.yMax > scrim.worldBound.yMin + 0.5f) fails.Add($"{at} 보호 배지 {badge.name}이 배틀 화면 덮개에 가린다(아래 {badge.worldBound.yMax} · 덮개 위 {scrim.worldBound.yMin})");
                    });
                // 덮개는 반투명이라 그 아래 보드 HUD 요소(board-hud-*)는 숨었거나 배틀 판(독 · 기둥 · DS 틀)에 다 가려야 한다(M10 — 아래 판 위 자원 줄 글자가 독 위로 비쳤다)
                var hud = shot.Panel.Root.Q("board-hud-dock")?.parent;
                if (scrim != null && hud != null)
                {
                    var covers = new List<Rect>();
                    foreach (string name in new[] { "battle-dock", "battle-info", "battle-log", "battle-ds-frame" })
                    {
                        var c = shot.Panel.Root.Q(name);
                        if (c != null && Drawn(c)) covers.Add(c.worldBound);
                    }
                    float top = scrim.worldBound.yMin;
                    hud.Query<VisualElement>().ForEach(el =>
                    {
                        if (el.name == null || !el.name.StartsWith("board-hud", StringComparison.Ordinal)) return;
                        var r = el.worldBound;
                        if (r.width < 1f || r.height < 1f || r.yMin < top - 0.5f || !Drawn(el)) return;
                        foreach (var c in covers)
                            if (r.xMin >= c.xMin - 0.5f && r.yMin >= c.yMin - 0.5f && r.xMax <= c.xMax + 0.5f && r.yMax <= c.yMax + 0.5f) return;
                        fails.Add($"{at} HUD 요소 {el.name}이 배틀 화면 덮개 아래로 비친다({r})");
                    });
                }
                // 기둥의 도트 창이 바로 밑 줄(「대기」·「기술」)에 닿지 않는다(큰 도트는 창을 아래 끝까지 채운다 — looks-bugs 4)
                foreach (string side in new[] { "battle-info-me", "battle-info-foe" })
                {
                    var block = shot.Panel.Root.Q(side);
                    var art = block != null ? block.Q(className: "sn-mon") : null;
                    if (art == null) continue;
                    VisualElement row = art.parent;
                    int next = block.IndexOf(row) + 1;
                    if (row.parent == block && next < block.childCount && block[next].worldBound.yMin < art.worldBound.yMax + 2f)
                        fails.Add($"{at} {side}의 도트 창이 아래 줄에 닿는다(도트 아래 {art.worldBound.yMax} · 다음 줄 위 {block[next].worldBound.yMin})");
                }
                // 배틀 기록: 위로 넘친 옛 줄이 반쯤 잘려 보이지 않는다(통째로 감춘다 — looks-bugs 18)
                var lines = shot.Panel.Root.Q("battle-log-lines");
                if (lines != null)
                    foreach (var row in lines.Children())
                        if (row.layout.y < -0.5f && row.resolvedStyle.visibility == Visibility.Visible) fails.Add($"{at} 배틀 기록의 윗줄이 잘려 보인다(y {row.layout.y})");
            }

            // 굴림판(spikes/dice-roll §5): 주사위 그림 요소 = 격자 × 도트(720 벌 412×124 · 1080 벌 618×186 — 주사위 32px · 48px)의 패널 배율배, 정수 자리, 그림이 붙어 있다
            var board = shot.Panel.Root.Q("battle-roll-board");
            if (board == null)
            {
                if (screen == "battle-roll") fails.Add($"{at} 굴림판이 없다");
                return;
            }
            var bb = board.worldBound;
            int bw = (shot.Grade.TokenHeight >= 1080 ? 618 : 412) * scale, bh = (shot.Grade.TokenHeight >= 1080 ? 186 : 124) * scale;
            float bx = bb.x * scale, by = bb.y * scale;
            if (bb.width * scale != bw || bb.height * scale != bh) fails.Add($"{at} 굴림판 {bb.width * scale}×{bb.height * scale} ≠ {bw}×{bh}(spikes/dice-roll §5)");
            if (bx != Mathf.Round(bx) || by != Mathf.Round(by)) fails.Add($"{at} 굴림판 자리 {bx},{by}가 정수 px가 아니다(도트가 번진다)");
            if (board.resolvedStyle.backgroundImage.renderTexture == null) fails.Add($"{at} 굴림판에 주사위 그림이 없다(녹화본 묶음 · 주사위 셰이더)");
            var dice = new HashSet<Color32>();
            for (int y = (int)by; y < by + bh && y < shot.Height; y += 2)
                for (int x = (int)bx; x < bx + bw && x < shot.Width; x += 2)
                    dice.Add(px[(shot.Height - 1 - y) * shot.Width + x]);
            if (dice.Count < 6) fails.Add($"{at} 굴림판이 비었다(색 {dice.Count})");
        }

        static BoardBoot liveBoard;
        static readonly Dictionary<string, Sinnoh.Data.StringTable> tables = new Dictionary<string, Sinnoh.Data.StringTable>();

        /// <summary>
        /// 찍는 화면 글의 언어(M9 언어 ② — 배치 인자 <c>-uiLang &lt;ko|en&gt;</c>, 기본 ko). 찍는 모든 무대가 이 언어의 표로 시작한다(<see cref="Strings()"/>).
        /// </summary>
        public static string Lang = "ko";

        // -uiOverflow: 찍은 화면마다 글 넘침 줄(없으면 null — 모으지 않는다)
        static List<string> overflowSink;

        /// <summary>화면 문자열 표(<see cref="Lang"/>의 것 — strings.ko · strings.en, 추적 파일).</summary>
        public static Sinnoh.Data.StringTable Strings() => Strings(Lang);

        /// <summary>그 언어의 화면 문자열 표(<c>ko</c> · <c>en</c> — 없으면 null). 무대의 <see cref="UiStage.Tables"/>로 넣는다(S02 「언어」 줄 · 언어 바꾸기 장면).</summary>
        public static Sinnoh.Data.StringTable Strings(string lang)
        {
            if (lang != "ko" && lang != "en") return null;
            if (tables.TryGetValue(lang, out var t)) return t;
            string path = StringsPath.Replace("strings.ko.json", "strings." + lang + ".json");
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path) ?? throw new FileNotFoundException("문자열 표 없음: " + path);
            return tables[lang] = Sinnoh.Data.StringTable.Load(asset.text);
        }

        /// <summary>보드 씬을 열고(편집 모드) 보드를 만든다. 한 번의 캡처 실행 안에서는 한 번만 — 끝나면 <see cref="CloseBoard"/>.</summary>
        static void OpenBoard(out BoardBoot boot, out BoardCameraRig rig)
        {
            if (liveBoard == null)
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(BoardShots.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
                liveBoard = Object.FindAnyObjectByType<BoardBoot>();
                if (liveBoard != null) liveBoard.Build();
            }
            boot = liveBoard;
            var cam = Camera.main;
            rig = cam != null ? cam.GetComponent<BoardCameraRig>() : null;
            if (boot == null || rig == null) throw new InvalidOperationException("보드 씬에 BoardBoot·BoardCameraRig가 없다");
        }

        internal static void CloseBoard()
        {
            if (liveBoard != null) liveBoard.Teardown();
            liveBoard = null;
            tables.Clear();
        }

        /// <summary>
        /// 지금 보드(화면 흐름이 놓은 말·카메라)를 이 크기로 그린 그림.
        /// 편집 모드에서는 카메라 뼈대의 <c>Awake</c>가 돌지 않으므로 <c>Apply</c>를 직접 부른다 — 말 정수배 전역 값(<c>_SinnohPieceZoomK</c>)이 거기서 들어간다(안 부르면 2160에서 말이 절반 크기).
        /// </summary>
        static Color32[] Board(BoardCameraRig rig, int width, int height)
        {
            var cam = rig.GetComponent<Camera>();
            rig.Apply();
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.DontSave };
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            try
            {
                OffscreenShot.Render(cam, rt, tex);   // 예열(BoardShots와 같다)
                OffscreenShot.Render(cam, rt, tex);
                return tex.GetPixels32();
            }
            finally
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }

        /// <summary>투명 바탕 패널 그림(색에 알파가 곱해져 있다)을 바탕 그림 위에 겹친다. 반투명 픽셀(덮는 막)은 sRGB 값으로 섞는 어림이다 — 실행 중의 Linear 섞기보다 조금 밝거나 어둡다.</summary>
        static Color32[] Over(Color32[] under, Color32[] ui)
        {
            var px = new Color32[ui.Length];
            for (int i = 0; i < px.Length; i++)
            {
                var u = ui[i];
                var b = under[i];
                int keep = 255 - u.a;
                px[i] = new Color32((byte)Math.Min(255, u.r + b.r * keep / 255), (byte)Math.Min(255, u.g + b.g * keep / 255), (byte)Math.Min(255, u.b + b.b * keep / 255), 255);
            }
            return px;
        }

        static int Colors(Color32[] px)
        {
            var seen = new HashSet<int>();
            foreach (var p in px) seen.Add(p.r << 24 | p.g << 16 | p.b << 8 | p.a);
            return seen.Count;
        }

        internal static void WritePng(string path, Color32[] px, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            tex.SetPixels32(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }

    /// <summary>화면 하나를 찍는 자리: 창 없는 패널 + 겉모양 + 등급·크기. 보드 위 화면이면 보드와 카메라 뼈대도 있다.</summary>
    public sealed class UiShot
    {
        public UiShotPanel Panel;
        public KitSkin Skin;
        public UiGrade Grade;
        public int Width, Height;
        public BoardBoot Board;
        public BoardCameraRig Rig;

        /// <summary>이 장면이 만든 화면 흐름(없으면 null) — 찍기 전에 보드 전경을 이 크기로 놓는다.</summary>
        public GameFlow Made { get; private set; }

        /// <summary>실행 중과 같은 화면 흐름: 패널 뿌리에 무대를 묶고, 보드 씬의 게임 데이터·보드·카메라를 쥔다. 시드는 고정이다.</summary>
        /// <param name="saves">저장 폴더(임시 폴더를 넣는다 — 사용자의 저장 폴더를 넣지 않는다). null이면 저장하지 않는다.</param>
        /// <param name="clock">저장 시각(고정 값).</param>
        public GameFlow Flow(string saves = null, Func<long> clock = null)
        {
            var stage = new UiStage(Skin, UiShots.Strings()) { Tables = UiShots.Strings };
            stage.Bind(Panel.Root, Grade);
            Made = new GameFlow(stage, Board != null ? Board.Ctx : null, Board, Rig, () => UiShots.Seed, () => { }, web: false, window: new FixedWindow(new Vector2Int(3840, 2160), new Vector2Int(Width, Height)),
                dice: FxShots.LoadDiceLibrary(), saves: saves, clock: clock);
            return Made;
        }
    }

    /// <summary>
    /// 창 없는 UI Toolkit 패널 하나: <c>PanelSettings</c>(<c>targetTexture</c> = RT, <c>ConstantPixelSize</c> 정수 배율) + <c>PanelRenderer</c>.
    /// 편집 모드에는 플레이어 루프가 없어 패널 갱신·그리기를 내부 정적 메서드(<c>UIElementsRuntimeUtility</c>)로 직접 부른다(<see cref="Tick"/>) —
    /// 공개 API가 아니라 Unity를 올리면 바뀔 수 있다(04 §9-6). 시험 <c>UiShotsTests</c>가 이름·인자와 그려진 픽셀을 본다.
    /// </summary>
    public sealed class UiShotPanel : IDisposable
    {
        public const string UtilityType = "UnityEngine.UIElements.UIElementsRuntimeUtility";

        /// <summary>
        /// 패널에 거는 실행용 테마(Unity 기본 테마를 들여오는 한 줄짜리, 추적 파일). 에디터에서 <c>PanelSettings</c>를 만들면 Unity가 <c>Assets/</c>에서
        /// 테마를 찾고 없으면 이 자리에 새로 만든다(<c>Reset</c>) — 캡처·시험이 돌 때마다 추적 안 된 파일이 생기지 않게 추적해 두고, 어느 테마인지 여기서 못박는다.
        /// 빈 테마를 걸면 뿌리 요소 높이가 0이 된다(기본 테마의 <c>.unity-ui-document__root</c>가 뿌리를 늘린다).
        /// </summary>
        public const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        // 한 프레임에 부르는 순서 그대로(이름, 인자) — RepaintPanels(false) = 화면 밖 패널까지 다시 그린다
        static readonly KeyValuePair<string, object[]>[] Steps =
        {
            new KeyValuePair<string, object[]>("PreUpdatePanelRenderers", new object[0]),
            new KeyValuePair<string, object[]>("UpdatePanelRenderers", new object[0]),
            new KeyValuePair<string, object[]>("UpdatePanels", new object[0]),
            new KeyValuePair<string, object[]>("RepaintPanels", new object[] { false }),
            new KeyValuePair<string, object[]>("RenderOffscreenPanels", new object[0]),
        };

        readonly RenderTexture rt;
        readonly PanelSettings settings;
        readonly GameObject host;

        /// <summary>패널을 덮는 뿌리 요소. 크기는 폭 ÷ 배율 × 높이 ÷ 배율(패널 좌표).</summary>
        public VisualElement Root { get; private set; }

        /// <param name="transparent">바탕을 투명하게 비운다(보드 위에 얹히는 화면). 기본은 검정.</param>
        public UiShotPanel(int width, int height, int scale, bool transparent = false)
        {
            var missing = MissingInternals();
            if (missing.Count > 0) throw new MissingMethodException(UtilityType + ": " + string.Join(", ", missing));
            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "UiShot", hideFlags = HideFlags.DontSave };
            rt.Create();
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.hideFlags = HideFlags.DontSave;
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (settings.themeStyleSheet == null)
            {
                Dispose();
                throw new FileNotFoundException("실행용 테마 없음(추적 파일): " + ThemePath);
            }
            settings.targetTexture = rt;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = scale;
            settings.clearColor = true;
            settings.colorClearValue = transparent ? Color.clear : Color.black;
            host = new GameObject("UiShot Panel") { hideFlags = HideFlags.HideAndDontSave };
            var renderer = host.AddComponent<PanelRenderer>();
            renderer.panelSettings = settings;
            renderer.RegisterUIReloadCallback((PanelRenderer.VersionedUIReloadCallback)((r, root, version) => Root = root));
            try
            {
                Tick(2);
                if (Root == null) throw new InvalidOperationException("PanelRenderer가 뿌리 요소를 주지 않았다");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>프레임을 <paramref name="frames"/>번 돌린다(갱신 → 배치 → 그리기). 이 프로세스의 실행 중 패널이 모두 함께 돈다.</summary>
        public void Tick(int frames = 1)
        {
            for (int f = 0; f < frames; f++)
                foreach (var step in Steps)
                    Find(step.Key, step.Value).Invoke(null, step.Value);
        }

        /// <summary>지금 RT의 픽셀(아래 줄부터 — <c>GetPixels32</c> 순서).</summary>
        public Color32[] Read()
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        public void Dispose()
        {
            if (host) Object.DestroyImmediate(host);
            if (settings) Object.DestroyImmediate(settings);
            if (rt) { rt.Release(); Object.DestroyImmediate(rt); }
        }

        /// <summary>이 Unity에 없는 내부 메서드(이름과 인자 수). 비어 있어야 캡처가 돈다.</summary>
        public static List<string> MissingInternals()
        {
            var missing = new List<string>();
            foreach (var step in Steps)
                if (Find(step.Key, step.Value) == null) missing.Add(step.Key + "(" + step.Value.Length + ")");
            return missing;
        }

        static MethodInfo Find(string name, object[] args)
        {
            var type = typeof(VisualElement).Assembly.GetType(UtilityType);
            if (type == null) return null;
            foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length != args.Length) continue;
                bool same = true;
                for (int i = 0; i < ps.Length; i++) same &= ps[i].ParameterType == args[i].GetType();
                if (same) return m;
            }
            return null;
        }
    }
}
