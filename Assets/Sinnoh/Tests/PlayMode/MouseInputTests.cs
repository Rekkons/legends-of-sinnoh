using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 보드 입력의 장치 쪽 길(plan §4 「마우스 조작 확인」): 가상 마우스·키보드(<see cref="VirtualInput"/>)의 상태 이벤트 → <c>Mouse.current</c> → <see cref="ScreenBoot"/> → 칸 고르기,
    /// <c>Keyboard.current</c>·휠 → <see cref="BoardCameraRig"/>. <see cref="GameFlow.Pointer"/>를 직접 부르지 않는다(<see cref="ScreenBoot.PointerFromMouse"/> 켠 채).
    /// 보드까지는 화면의 함수로 간다 — 창 없는 에디터에는 게임 뷰가 없어 UI Toolkit 패널이 포인터 이벤트를 받지 못한다(단추·툴팁을 마우스로 누르는 길은
    /// 플레이어 확인 <see cref="MouseProbe"/> · <c>Tools/window-probe.mjs</c>가 본다). 같은 까닭으로 포커스 없는 에디터가 장치 입력을 버리지 않게 이 시험 동안만 입력 설정 둘을 푼다.
    /// 패널은 RT가 아니라 화면에 그린다 — 마우스 좌표가 그 크기(창 없는 에디터는 640×480) 기준이다.
    /// </summary>
    public sealed class MouseInputTests
    {
        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        [UnityTest]
        public IEnumerator M4_가상마우스로칸을올리고눌러걷고HUD위휠은막히고보드위휠과QE는듣는다()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            VirtualInput input = null;
            var settings = InputSystem.settings;
            var backgroundWas = settings.backgroundBehavior;
#if UNITY_EDITOR
            var editorWas = settings.editorInputBehaviorInPlayMode;
#endif
            try
            {
                yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
                ScreenBoot boot = null;
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    boot = Object.FindAnyObjectByType<ScreenBoot>();
                    if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
                }
                Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
                Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                Assert.That(boot.PointerFromMouse, Is.True, "화면 시작점이 장치의 마우스를 흐름에 넘긴다");
                var root = boot.Root;
                var stage = root.Stage;
                var flow = boot.Flow;
                var board = Object.FindAnyObjectByType<BoardBoot>();
                var cam = Camera.main;
                var rig = cam.GetComponent<BoardCameraRig>();

                // 보드까지: 화면의 함수로(고정 시드)
                flow.StartSolo(new SoloSettings(), 11, 99);
                yield return Frames(2);
                var village = (SetupVillageScreen)stage.Current;
                village.Pick(0);
                Assert.That(village.Confirm(), Is.True);
                flow.Skip(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
                yield return Frames(2);
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(0);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                flow.Skip(flow.OpeningMs);   // 게임 시작의 막 카드 · 조무래기 놓기(약 9초)는 건너뛴다 — 그 모습은 SoloFlowTests가 본다
                yield return Until(() => flow.InputOpen, 15f);
                Assert.That(flow.InputOpen, Is.True, "내 턴 컷신 뒤 입력이 열린다");
                yield return Frames(3);
                var hud = flow.Hud;
                var client = flow.Client;
                int w = root.Size.x, h = root.Size.y;
                float scale = stage.Grade.PanelScale;

                // 포커스 없는 에디터가 장치 입력을 버리지 않게(이 시험 동안만)
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
                input = new VirtualInput();
                yield return Frames(3);

                IEnumerator Move(Vector2 px)
                {
                    input.Point(px);
                    yield return Frames(3);
                }

                // 화면에서 바로 고를 수 있는 한 걸음 칸(가운데가 화면 안 · HUD 판 밖 · 앞의 높은 칸에 가리지 않음). HUD 판 밖인데 PC 층이 마우스를 받으면 실패
                int from = client.View.Seats[client.Seat].Cell;
                var dist = MoveRules.Reachable(flow.Ctx.Board, from, client.View.Seats[client.Seat].MovesLeft, client.View.Options.ElevationMode);
                int target = -1;
                Vector2 targetPx = default;
                foreach (int cell in hud.Reach)
                {
                    if (!BoardPick.Screen(board, cam, cell, w, h, out var px) || px.x < 0 || px.x >= w || px.y < 0 || px.y >= h) continue;
                    if (BoardHud.UnderPanel(stage.Root, new Vector2(px.x / scale, (h - px.y) / scale))) continue;
                    Assert.That(flow.OverUi(px.x, px.y), Is.False, "칸 " + cell + " " + px + "은 HUD 판 밖인데 PC 층이 마우스를 받는다");
                    if (BoardPick.Cell(board, cam, px.x, px.y, w, h) != cell) continue;
                    var engine = cam.WorldToScreenPoint(board.CellTop(cell));
                    Assert.That(Vector2.Distance(px, new Vector2(engine.x, engine.y)), Is.LessThan(1.5f), "칸 " + cell + ": 내 셈과 엔진 카메라의 자리가 어긋난다");
                    if (target < 0 && dist[cell] == 1) { target = cell; targetPx = px; }
                }
                Assert.That(target, Is.GreaterThanOrEqualTo(0), "화면(" + w + "x" + h + ")에서 고를 수 있는 한 걸음 칸");

                // 올리기: 장치의 자리 → 그 칸이 칠해진다. 누르기: 고른 칸(걷기 전 확인 — 결정 70 ⑱) → Esc로 풀고 → 다시 골라 한 번 더 누르면 이동 답 하나
                int commands = flow.Session.CommandCount;
                yield return Move(targetPx);
                Assert.That(Mouse.current, Is.SameAs(input.Mouse));
                Assert.That(Mouse.current.position.ReadValue(), Is.EqualTo(targetPx), "장치가 넣은 자리를 읽는다(포커스 없는 에디터에서 입력이 버려지지 않았다)");
                Assert.That(hud.HoverCell, Is.EqualTo(target), "마우스를 올린 칸");
                Assert.That(board.Marks.Target, Is.EqualTo(target));
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "올리기만 해서는 답을 내지 않는다");
                input.Point(targetPx, down: true);
                yield return Frames(2);
                input.Point(targetPx);
                yield return Frames(2);
                Assert.That((hud.PickedCell, flow.Session.CommandCount), Is.EqualTo((target, commands)), "누름 한 번 = 고르기(걷지 않는다)");
                Assert.That(stage.Root.Q(BoardHud.MoveGo), Is.Not.Null, "[이동]이 올라온다");
                input.Hold(Key.Escape);
                yield return Frames(2);
                input.Release();
                yield return Frames(2);
                Assert.That(hud.PickedCell, Is.EqualTo(-1), "Esc: 고른 칸이 풀린다");
                for (int press = 0; press < 2; press++)
                {
                    input.Point(targetPx, down: true);
                    yield return Frames(2);
                    input.Point(targetPx);
                    yield return Frames(2);
                }
                Assert.That(client.View.Seats[client.Seat].Cell, Is.EqualTo(target), "고른 칸을 한 번 더 누르면 간다");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1), "누름 한 번 = 이동 답 하나(누르고 있는 프레임마다 내지 않는다)");
                yield return Until(() => flow.InputOpen, 15f);
                Assert.That(flow.InputOpen, Is.True);

                // 휠: 보드 위에서는 한 칸에 줌 한 단, HUD 위에서는 그대로
                var boardPx = targetPx;
                var dock = stage.Root.Q("board-hud-party").worldBound.center;
                var dockPx = new Vector2(dock.x * scale, h - dock.y * scale);
                Assert.That(flow.OverUi(dockPx.x, dockPx.y), Is.True);
                Assert.That(flow.OverUi(boardPx.x, boardPx.y), Is.False);
                IEnumerator Wheel(Vector2 px, float notches)
                {
                    input.Point(px, wheel: notches * 120f);
                    yield return Frames(2);
                    input.Point(px);
                    yield return Frames(2);
                }
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Region));
                yield return Move(boardPx);
                yield return Wheel(boardPx, 1);
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Close), "보드 위 휠 위로 한 칸");
                yield return Wheel(boardPx, -1);
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Region), "보드 위 휠 아래로 한 칸 = 한 단만");
                yield return Move(dockPx);
                Assert.That(rig.WheelEnabled, Is.False);
                yield return Wheel(dockPx, 1);
                yield return Wheel(dockPx, -1);
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Region), "아래 판 위에서 굴린 휠은 보드 줌을 바꾸지 않는다");
                // 보드에서 HUD 위로 옮기는 바로 그 이벤트에 휠이 실려도 새지 않는다(카메라가 화면 시작점 뒤에 휠을 읽는다)
                for (int i = 0; i < 3; i++)
                {
                    yield return Move(boardPx);
                    yield return Wheel(dockPx, 1);
                    Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Region), "보드 → HUD로 옮기며 굴린 휠이 보드로 샜다(" + i + ")");
                }

                // Q·E: 60°씩
                yield return Move(boardPx);
                input.Hold(Key.E);
                yield return Frames(2);
                input.Release();
                yield return Frames(2);
                Assert.That(rig.Yaw, Is.EqualTo(1), "E 한 번 = 한 칸(누르고 있는 프레임마다 돌지 않는다)");
                input.Hold(Key.Q);
                yield return Frames(2);
                input.Release();
                yield return Frames(2);
                Assert.That(rig.Yaw, Is.EqualTo(0));

                // 창(게임 메뉴)이 덮으면 장치 입력이 보드로 가지 않는다
                flow.OpenMenu();
                yield return Frames(2);
                commands = flow.Session.CommandCount;
                input.Hold(Key.E);
                yield return Frames(2);
                input.Release();
                yield return Wheel(boardPx, 1);
                input.Point(boardPx, down: true);
                yield return Frames(2);
                input.Point(boardPx);
                yield return Frames(2);
                Assert.That(rig.Yaw, Is.EqualTo(0), "메뉴 뒤: E를 듣지 않는다");
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Region), "메뉴 뒤: 휠을 듣지 않는다");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "메뉴 뒤: 칸이 눌리지 않는다");
                flow.OpenTitle();
                yield return Frames(2);
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                input?.Dispose();
                settings.backgroundBehavior = backgroundWas;
#if UNITY_EDITOR
                settings.editorInputBehaviorInPlayMode = editorWas;
#endif
            }
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
