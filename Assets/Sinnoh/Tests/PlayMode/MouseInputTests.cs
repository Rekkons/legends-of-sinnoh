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

        static readonly System.Reflection.MethodInfo ClickInvoke = typeof(Clickable).GetMethod("Invoke", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, new[] { typeof(EventBase) }, null);

        // 키트 단추를 누른다(그 단추의 Clickable — 마우스를 뗄 때와 같은 동작, TutorialClickPath와 같은 꼴)
        static void Invoke(Button button)
        {
            Assert.That(ClickInvoke, Is.Not.Null, "Clickable.Invoke(EventBase)를 찾지 못했다(Unity 판이 바뀌었다)");
            using (var e = ClickEvent.GetPooled())
            {
                e.target = button;
                ClickInvoke.Invoke(button.clickable, new object[] { e });
            }
        }

        // 보드 씬 · 화면 시작점(장치 입력을 받도록 입력 설정 둘을 푼다 — 끝에서 되돌린다)
        sealed class KeyHarness : System.IDisposable
        {
            public ScreenBoot Boot;
            public VirtualInput Input;
            public readonly List<string> Errors = new List<string>();
            readonly InputSettings.BackgroundBehavior backgroundWas;
#if UNITY_EDITOR
            readonly InputSettings.EditorInputBehaviorInPlayMode editorWas;
#endif
            public KeyHarness()
            {
                Application.logMessageReceived += Grab;
                var settings = InputSystem.settings;
                backgroundWas = settings.backgroundBehavior;
#if UNITY_EDITOR
                editorWas = settings.editorInputBehaviorInPlayMode;
#endif
            }

            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(type + ": " + msg);
            }

            public IEnumerator Load()
            {
                yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    Boot = Object.FindAnyObjectByType<ScreenBoot>();
                    if (Boot != null && Boot.Root != null && Boot.Root.Stage.Root != null) break;
                }
                Assert.That(Boot, Is.Not.Null, "씬에 ScreenBoot 없음");
                Assert.That(Boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            }

            public IEnumerator Devices()
            {
                var settings = InputSystem.settings;
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
                Input = new VirtualInput();
                yield return Frames(3);
            }

            public IEnumerator Press(Key key)
            {
                Input.Hold(key);
                yield return Frames(2);
                Input.Release();
                yield return Frames(2);
            }

            public void Dispose()
            {
                Application.logMessageReceived -= Grab;
                Input?.Dispose();
                var settings = InputSystem.settings;
                settings.backgroundBehavior = backgroundWas;
#if UNITY_EDITOR
                settings.editorInputBehaviorInPlayMode = editorWas;
#endif
            }
        }

        [UnityTest]
        public IEnumerator M10_보드돌리기단추는QE와같은함수이고키를못받으면숨는다()
        {
            using (var t = new KeyHarness())
            {
                yield return t.Load();
                var stage = t.Boot.Root.Stage;
                var flow = t.Boot.Flow;
                var cam = Camera.main.GetComponent<BoardCameraRig>();
                flow.StartSolo(new SoloSettings(), 11, 99);
                yield return Frames(2);
                var village = (SetupVillageScreen)stage.Current;
                village.Pick(0);
                Assert.That(village.Confirm(), Is.True);
                flow.Skip(flow.QueueLeftMs);
                yield return Frames(2);
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(0);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                flow.Skip(flow.OpeningMs);
                yield return Until(() => flow.InputOpen, 15f);
                yield return Frames(3);
                yield return t.Devices();
                var hud = flow.Hud;
                var root = stage.Root;
                var left = root.Q<Button>(BoardHud.RotateLeft);
                var right = root.Q<Button>(BoardHud.RotateRight);
                Assert.That(left, Is.Not.Null, "↺ 단추");
                Assert.That(right, Is.Not.Null, "↻ 단추");
                Assert.That(left.text, Does.Contain("Q"), "↺ 단추에 키 글자 Q");
                Assert.That(right.text, Does.Contain("E"), "↻ 단추에 키 글자 E");
                Assert.That((hud.RotateShown, flow.CanTurn), Is.EqualTo((true, true)), "보드: 단추가 보이고 키를 받는다");
                Assert.That(root.Q(BoardHud.Rotate).ClassListContains(BoardHud.RotateOff), Is.False);

                // 단추 = 키: ↺ 한 번과 Q 한 번이 같은 칸만큼 돈다 · ↻ = E(줌 · 보는 점은 그대로)
                int yaw0 = cam.Yaw;
                var zoom0 = cam.CurrentZoom;
                var target0 = cam.Target;
                int commands = flow.Session.CommandCount;
                Invoke(left);
                Assert.That(cam.Yaw, Is.EqualTo((yaw0 + 5) % 6), "↺ = 방향 −1");
                yield return t.Press(Key.Q);
                Assert.That(cam.Yaw, Is.EqualTo((yaw0 + 4) % 6), "Q = 방향 −1(단추와 같다)");
                Invoke(right);
                yield return t.Press(Key.E);
                Assert.That(cam.Yaw, Is.EqualTo(yaw0), "↻ · E = 방향 +1씩");
                Assert.That((cam.CurrentZoom, cam.Target), Is.EqualTo((zoom0, target0)), "돌리기는 줌 · 보는 점을 바꾸지 않는다");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "돌리기는 답을 내지 않는다");

                // 게임 메뉴가 덮으면: 단추가 숨고 키 · 단추 함수 모두 돌리지 않는다 → Esc로 닫으면 다시
                flow.OpenMenu();
                yield return Frames(2);
                Assert.That((hud.RotateShown, flow.CanTurn), Is.EqualTo((false, false)), "메뉴 위: 단추 숨김 · 키 안 받음");
                Assert.That(root.Q(BoardHud.Rotate).ClassListContains(BoardHud.RotateOff), Is.True);
                Assert.That(flow.Turn(1), Is.False);
                yield return t.Press(Key.E);
                Assert.That(cam.Yaw, Is.EqualTo(yaw0), "메뉴 위의 E · 단추 함수는 돌리지 않는다");
                yield return t.Press(Key.Escape);
                Assert.That(flow.MenuUp, Is.False);
                yield return Frames(2);
                Assert.That(hud.RotateShown, Is.True, "메뉴를 닫으면 다시 보인다");

                // 글 칸에 글을 치는 동안(채팅 · 찾기 — GameFlow.Typing): 단추 숨김 · Q 안 받음
                flow.Typing = true;
                yield return Frames(2);
                Assert.That((hud.RotateShown, flow.CanTurn), Is.EqualTo((false, false)), "글 치는 중");
                yield return t.Press(Key.Q);
                Assert.That(cam.Yaw, Is.EqualTo(yaw0), "글 치는 중의 Q는 돌리지 않는다");
                flow.Typing = false;
                yield return Frames(2);
                Assert.That(hud.RotateShown, Is.True);
                flow.OpenTitle();
                yield return Frames(2);
                Assert.That(t.Errors, Is.Empty, string.Join("\n", t.Errors));
            }
        }

        // 보드까지: 화면의 함수로(고정 시드 — 위 돌리기 시험과 같은 길)
        static IEnumerator ToBoard(GameFlow flow, UiStage stage)
        {
            flow.StartSolo(new SoloSettings(), 11, 99);
            yield return Frames(2);
            var village = (SetupVillageScreen)stage.Current;
            village.Pick(0);
            Assert.That(village.Confirm(), Is.True);
            flow.Skip(flow.QueueLeftMs);
            yield return Frames(2);
            var pick = (SetupPickScreen)stage.Current;
            pick.PickStarter(0);
            pick.PickCard(0);
            Assert.That(pick.Confirm(), Is.True);
            flow.Skip(flow.OpeningMs);
            yield return Until(() => flow.InputOpen, 15f);
            Assert.That(flow.InputOpen, Is.True, "내 턴 컷신 뒤 입력이 열린다");
            yield return Frames(3);
        }

        // 글쇠 하나를 글 칸에(패널이 포커스 요소에 보내는 엔진 이벤트 — 창 없는 에디터의 패널은 장치 키를 글로 받지 못한다) + 같은 키를 가상 키보드로(ScreenBoot · 카메라가 읽는 길)
        static IEnumerator Type(KeyHarness t, TextField field, char c, Key key)
        {
            var target = field.panel.focusController.focusedElement as VisualElement ?? field;
            using (var e = KeyDownEvent.GetPooled(c, KeyCode.None, EventModifiers.None)) target.SendEvent(e);
            yield return t.Press(key);
        }

        [UnityTest]
        public IEnumerator M10_결정175_글칸에포커스가있으면단축키글자가칸에들어가고도감과옆판이그대로다()
        {
            using (var t = new KeyHarness())
            {
                yield return t.Load();
                var boot = t.Boot;
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                boot.PointerFromMouse = false;   // 가상 마우스가 칸 고르기로 새지 않게(키보드만 — 보드 누르기는 흐름 함수로)
                var cam = Camera.main.GetComponent<BoardCameraRig>();
                yield return t.Devices();

                // ① 타이틀에서 연 도감(보드 없음): 찾기 칸에 T · Y · Q · E · 스페이스 · 1 → 도감 그대로 · 글자는 칸에
                flow.OpenTitleDex();
                yield return Frames(2);
                var titleDex = stage.Current as DexScreen;
                Assert.That(titleDex, Is.Not.Null);
                var field = stage.Root.Q<TextField>("dex-name");
                field.Focus();
                yield return Frames(2);
                Assert.That(flow.Typing, Is.True, "도감 찾기 칸 포커스 = 글 치기(따로 걸지 않아도 — 결정 175)");
                yield return Type(t, field, 't', Key.T);
                yield return Type(t, field, 'y', Key.Y);
                yield return Type(t, field, 'q', Key.Q);
                yield return Type(t, field, 'e', Key.E);
                yield return Type(t, field, ' ', Key.Space);
                yield return Type(t, field, '1', Key.Digit1);
                Assert.That(stage.Current, Is.SameAs(titleDex), "타이틀 도감: 단축키 글자에 닫히지 않는다");
                Assert.That((field.value, titleDex.Search), Is.EqualTo(("tyqe 1", "tyqe 1")), "친 글자가 칸에 들어가 찾기를 건다");
                flow.CloseDex();
                yield return Frames(2);
                Assert.That(flow.Typing, Is.False, "칸이 사라지면 글 치기도 끝");

                // ② 게임 중 도감: T(정비) · Y(상성) · 스페이스가 도감을 바꾸지 않는다
                yield return ToBoard(flow, stage);
                Assert.That(flow.OpenDex(), Is.True);
                yield return Frames(2);
                var dex = (DexScreen)stage.Current;
                field = stage.Root.Q<TextField>("dex-name");
                field.Focus();
                yield return Frames(2);
                Assert.That(flow.Typing, Is.True);
                yield return Type(t, field, 't', Key.T);
                yield return Type(t, field, 'y', Key.Y);
                yield return Type(t, field, ' ', Key.Space);
                Assert.That((flow.Front, stage.Info), Is.EqualTo(((IUiScreen)dex, (IUiScreen)null)), "게임 중 도감: T · Y에 정비 판 · 상성 창이 덮지 않는다");
                Assert.That(field.value, Is.EqualTo("ty "));
                // 포커스를 놓으면 단축키는 다시 듣는다(Y = 상성 창 — 정보 창 층 위)
                field.Blur();
                yield return Frames(2);
                Assert.That(flow.Typing, Is.False);
                yield return t.Press(Key.Y);
                Assert.That(flow.Front, Is.InstanceOf<TypeChartScreen>(), "칸 밖에서는 Y가 상성 창을 연다");
                yield return t.Press(Key.Y);
                Assert.That(flow.Front, Is.SameAs(dex));
                flow.CloseDex();
                yield return Frames(2);
                Assert.That(stage.Current, Is.Null);

                // ③ 룰 설명서 옆 판(창이 아니다 — 카메라가 Q · E를 받는 자리): 찾기 칸에 Q · E · T → 보드가 돌지 않고 정비 판도 안 열림
                Assert.That(flow.ToggleBookSide(), Is.True);
                yield return Frames(2);
                field = stage.Root.Q<TextField>("rulebook-side-find");
                Assert.That(field, Is.Not.Null, "옆 판의 찾기 칸");
                field.Focus();
                yield return Frames(2);
                Assert.That((flow.Typing, flow.CanTurn), Is.EqualTo((true, false)), "옆 판 찾기 칸 포커스 = 글 치기 · 카메라 키 끔");
                int yaw = cam.Yaw;
                yield return Type(t, field, 'q', Key.Q);
                yield return Type(t, field, 'e', Key.E);
                yield return Type(t, field, 't', Key.T);
                Assert.That(cam.Yaw, Is.EqualTo(yaw), "글 치는 중의 Q · E는 보드를 돌리지 않는다");
                Assert.That((stage.Current, stage.Info), Is.EqualTo(((IUiScreen)null, (IUiScreen)null)), "글 치는 중의 T는 정비 판을 열지 않는다");
                Assert.That(field.value, Is.EqualTo("qet"));
                // 보드(PC 층 밖)를 누르면 칸이 포커스를 놓는다 → Q가 다시 돈다
                Vector2 spot = default;
                bool found = false;
                for (int gy = 2; gy < 9 && !found; gy++)
                    for (int gx = 2; gx < 9 && !found; gx++)
                    {
                        var p = new Vector2(boot.Root.Size.x * gx / 10f, boot.Root.Size.y * gy / 10f);
                        if (!flow.OverUi(p.x, p.y)) { spot = p; found = true; }
                    }
                Assert.That(found, Is.True, "PC 층 밖의 보드 자리");
                flow.Pointer(spot.x, spot.y, true, false);
                flow.Pointer(spot.x, spot.y, false, false);
                yield return Frames(2);
                Assert.That(flow.Typing, Is.False, "보드를 누르면 글 치기 끝");
                flow.Hud.Back();   // 누른 칸의 고르기를 푼다
                yield return t.Press(Key.Q);
                Assert.That(cam.Yaw, Is.EqualTo((yaw + 5) % 6), "칸 밖에서는 Q가 돈다");
                flow.OpenTitle();
                yield return Frames(2);
                Assert.That(t.Errors, Is.Empty, string.Join("\n", t.Errors));
            }
        }

        [UnityTest]
        public IEnumerator M10_결정176_정비운명트레이너스탬프단추는다시누르면닫히고창이떠있어도가려지지않는다()
        {
            var rts = new List<RenderTexture>();
            using (var t = new KeyHarness())
            {
                try
                {
                    yield return t.Load();
                    var boot = t.Boot;
                    var flow = boot.Flow;
                    var stage = boot.Root.Stage;
                    boot.PointerFromMouse = false;
                    yield return ToBoard(flow, stage);
                    var cases = new (string id, System.Type screen)[]
                    {
                        ("board-hud-maint", typeof(PartyScreen)),
                        (BoardHud.FateButton, typeof(FateScreen)),
                        ("board-hud-trainer", typeof(TrainerScreen)),
                        (BoardHud.StampButton, typeof(StampScreen)),
                    };
                    foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) })
                    {
                        var rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                        rt.Create();
                        rts.Add(rt);
                        boot.Root.SetTarget(rt);
                        yield return Frames(4);
                        int commands = flow.Session.CommandCount;
                        foreach (var (id, screen) in cases)
                        {
                            string at = size.y + " " + id;
                            var button = stage.Root.Q<Button>(id);
                            Assert.That(button, Is.Not.Null, at);
                            Assert.That(button.enabledInHierarchy, Is.True, at + " 창이 없을 때 켜짐");
                            Invoke(button);
                            yield return Frames(2);
                            Assert.That(flow.Front, Is.InstanceOf(screen), at + " 한 번 = 열림");
                            button = stage.Root.Q<Button>(id);   // 아래 판이 다시 그려졌을 수 있다
                            Assert.That(button.enabledInHierarchy, Is.True, at + " 창이 떠 있어도 다시 누를 수 있다");
                            var hit = stage.Root.panel.Pick(button.worldBound.center);
                            Assert.That(hit != null && (hit == button || button.Contains(hit)), Is.True, at + " 창이 단추를 덮지 않는다(맨 위 요소 " + (hit != null ? hit.name : "없음") + ")");
                            Invoke(button);
                            yield return Frames(2);
                            Assert.That((stage.Current, stage.Info), Is.EqualTo(((IUiScreen)null, (IUiScreen)null)), at + " 두 번 = 닫힘");
                            Assert.That(flow.InputOpen, Is.True, at);
                        }
                        Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "열고 닫기만 — 답을 내지 않는다");
                    }
                    flow.OpenTitle();
                    yield return Frames(2);
                    Assert.That(t.Errors, Is.Empty, string.Join("\n", t.Errors));
                }
                finally
                {
                    if (t.Boot != null && t.Boot.Root != null) t.Boot.Root.SetTarget(null);
                    foreach (var rt in rts) rt.Release();
                }
            }
        }

        [UnityTest]
        public IEnumerator M10_배틀독단축키는단추글자와같고Q는보드를돌리지않는다()
        {
            RenderTexture rt = null;
            using (var t = new KeyHarness())
            {
                try
                {
                    yield return t.Load();
                    var boot = t.Boot;
                    var flow = boot.Flow;
                    boot.PointerFromMouse = false;   // 가상 마우스가 칸 고르기로 새지 않게(키보드만 본다)
                    var stage = boot.Root.Stage;
                    rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    rt.Create();
                    boot.Root.SetTarget(rt);
                    yield return Frames(4);
                    var cam = Camera.main.GetComponent<BoardCameraRig>();
                    var title = (TitleScreen)stage.Current;
                    int soloIndex = -1;
                    for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Solo) soloIndex = i;
                    Assert.That(title.Activate(soloIndex), Is.True);
                    yield return null;
                    var pilot = FlowPilot.Start(flow, Sinnoh.Ai.SoloTrail.Catch);   // 볼을 쥐여 준 판 — 야생 배틀에서 [가방]이 켜진다
                    yield return null;
                    BattleScreen b = null;
                    float t0 = Time.realtimeSinceStartup;
                    for (int guard = 0; guard < 40000 && Time.realtimeSinceStartup - t0 < 240f; guard++)
                    {
                        flow.Skip(150);
                        yield return null;
                        if (stage.Current is BattleScreen s && s.Open && s.Over == null && s.BagOn) { b = s; break; }
                        FlowPilot.Step(flow, pilot);
                    }
                    Assert.That(b, Is.Not.Null, "야생 배틀의 내 차례([가방] 켜짐)에 닿지 못했다");
                    yield return t.Devices();
                    var root = stage.Root;
                    int yaw = cam.Yaw;
                    int commands = flow.Session.CommandCount;
                    Assert.That(flow.CanTurn, Is.False, "배틀 화면 위: 카메라가 Q · E를 받지 않는다");
                    Assert.That(flow.Hud == null || !flow.Hud.RotateShown, Is.True, "배틀 화면 위: 돌리기 단추 숨김");
                    // 독의 키 글자 = 누르는 요소
                    Assert.That(b.KeyTarget('Q'), Is.SameAs(root.Q("battle-bag")), "Q = [가방]");
                    Assert.That(b.KeyTarget('1'), Is.SameAs(root.Q("battle-move-1")), "1 = 기술 줄 1");

                    // Q = [가방] → 볼 줄(보드는 돌지 않는다) · 볼 줄에서 1은 기술을 누르지 않는다 · Q 다시 = 기술 줄
                    yield return t.Press(Key.Q);
                    Assert.That(b.BagOpen, Is.True, "Q로 [가방]이 열린다");
                    Assert.That(root.Q("battle-ball-1"), Is.Not.Null, "볼 줄");
                    Assert.That(cam.Yaw, Is.EqualTo(yaw), "배틀 중의 Q는 보드를 돌리지 않는다");
                    yield return t.Press(Key.Digit1);
                    Assert.That((b.BagOpen, flow.Session.CommandCount), Is.EqualTo((true, commands)), "볼 줄에서 1은 아무 일도 없다");
                    yield return t.Press(Key.Q);
                    Assert.That(b.BagOpen, Is.False, "Q 다시 = 기술 줄로");
                    Assert.That(root.Q("battle-move-1"), Is.Not.Null);
                    // E = [기권] — 켜지지 않은 때(내 포켓몬이 기절하기 전)는 아무 일도 없다
                    if (!b.ForfeitOn)
                    {
                        Assert.That(b.KeyTarget('E'), Is.Null);
                        yield return t.Press(Key.E);
                        Assert.That(stage.Current, Is.SameAs(b), "꺼진 [기권]의 E: 배틀 화면 그대로");
                        Assert.That((flow.Session.CommandCount, cam.Yaw), Is.EqualTo((commands, yaw)), "꺼진 [기권]의 E는 답도 회전도 없다");
                    }
                    // T = [정비](배틀 화면 위 정비 판) → T 다시 = 닫기
                    yield return t.Press(Key.T);
                    Assert.That(flow.Front, Is.InstanceOf<PartyScreen>(), "T로 정비 판(배틀 화면 위 — 정비를 못 받는 때는 보기만 하는 정보 창 층)");
                    yield return t.Press(Key.Q);
                    Assert.That(b.BagOpen, Is.False, "정비 판이 위에 있으면 Q는 독을 누르지 않는다");
                    yield return t.Press(Key.T);
                    Assert.That(flow.Front, Is.SameAs(b), "T 다시 = 닫기");
                    yield return Until(() => b.Open && b.KeyTarget('1') != null, 5f);
                    // 1 = 기술 줄 1(누른 것과 같은 답)
                    commands = flow.Session.CommandCount;
                    yield return t.Press(Key.Digit1);
                    Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1), "1 = 기술 줄 1의 답");
                    Assert.That(cam.Yaw, Is.EqualTo(yaw));
                    flow.OpenTitle();
                    yield return Frames(2);
                    Assert.That(t.Errors, Is.Empty, string.Join("\n", t.Errors));
                }
                finally
                {
                    if (rt != null) rt.Release();
                }
            }
        }
    }
}
