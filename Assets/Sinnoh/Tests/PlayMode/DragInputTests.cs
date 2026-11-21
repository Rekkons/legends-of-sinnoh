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
    /// 마우스로 보드 끌기(결정 66 ②, 04 §3-2): 장치 쪽 길(가상 마우스 <see cref="VirtualInput"/> → <c>Mouse.current</c> → <see cref="ScreenBoot"/> → <see cref="GameFlow.Pointer(float, float, bool, bool)"/>)로
    /// 끌면 보는 점이 옮겨지고 칸이 눌리지 않는지, 끌지 않고 떼면 칸이 눌리는지, HUD 위에서 시작하거나 창이 덮으면 옮겨지지 않는지, 범위 밖으로 나가지 않는지 본다.
    /// 걷는 말 따라가기와의 관계는 흐름의 함수를 직접 불러 연출 시각만으로 본다(프레임 길이에 기대지 않게).
    /// 패널은 화면에 그린다 — 마우스 좌표가 그 크기(창 없는 에디터는 640×480) 기준이다(<see cref="MouseInputTests"/>와 같다).
    /// </summary>
    public sealed class DragInputTests
    {
        ScreenBoot boot;
        UiStage stage;
        GameFlow flow;
        BoardBoot board;
        Camera cam;
        BoardCameraRig rig;
        int w, h;
        float scale;

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        // 보드까지: 화면의 함수로(고정 시드)
        IEnumerator ToBoard(bool toTurn = true)
        {
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            boot = null;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            stage = boot.Root.Stage;
            flow = boot.Flow;
            board = Object.FindAnyObjectByType<BoardBoot>();
            cam = Camera.main;
            rig = cam.GetComponent<BoardCameraRig>();
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
            if (!toTurn)
            {
                yield return Frames(2);
                w = boot.Root.Size.x;
                h = boot.Root.Size.y;
                scale = stage.Grade.PanelScale;
                yield break;
            }
            flow.Skip(flow.OpeningMs);
            yield return Until(() => flow.InputOpen, 15f);
            Assert.That(flow.InputOpen, Is.True, "내 턴 컷신 뒤 입력이 열린다");
            yield return Frames(3);
            w = boot.Root.Size.x;
            h = boot.Root.Size.y;
            scale = stage.Grade.PanelScale;
        }

        // 화면에서 바로 고를 수 있는 한 걸음 칸(가운데가 화면 안 · HUD 판 밖 · 앞의 높은 칸에 가리지 않음 · 숨겨진 장소 없음)
        bool Near(out int target, out Vector2 targetPx, bool oneStep = true)
        {
            var client = flow.Client;
            int from = client.View.Seats[client.Seat].Cell;
            var dist = MoveRules.Reachable(flow.Ctx.Board, from, client.View.Seats[client.Seat].MovesLeft, client.View.Options.ElevationMode);
            target = -1;
            targetPx = default;
            foreach (int cell in flow.Hud.Reach)
            {
                if (oneStep && dist[cell] != 1) continue;
                bool spot = false;   // 숨겨진 장소 칸은 뺀다(걸으면 선택이 열려 다음 걸음을 바로 낼 수 없다)
                foreach (var t in client.View.Tokens) if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell == cell) spot = true;
                if (spot) continue;
                if (!BoardPick.Screen(board, cam, cell, w, h, out var px) || px.x < 0 || px.x >= w || px.y < 0 || px.y >= h) continue;
                if (BoardHud.UnderPanel(stage.Root, new Vector2(px.x / scale, (h - px.y) / scale)) || flow.OverUi(px.x, px.y)) continue;
                if (BoardPick.Cell(board, cam, px.x, px.y, w, h) != cell) continue;
                target = cell;
                targetPx = px;
                return true;
            }
            return false;
        }

        // 보는 점의 범위: 보이는 칸들의 가운데가 이루는 네모
        void Bounds(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = minZ = float.MaxValue;
            maxX = maxZ = float.MinValue;
            for (int c = 0; c < board.Board.Count; c++)
            {
                if (board.Board[c].DistortionWorld && !board.DistortionWorldVisible) continue;
                var top = board.CellTop(c);
                minX = Mathf.Min(minX, top.x);
                maxX = Mathf.Max(maxX, top.x);
                minZ = Mathf.Min(minZ, top.z);
                maxZ = Mathf.Max(maxZ, top.z);
            }
        }

        [UnityTest]
        public IEnumerator M4_가상마우스로끌면보는점이옮겨지고칸은안눌리고_안끌고떼면눌리고_HUD위시작과창뒤는안옮겨진다()
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
                yield return ToBoard();
                Assert.That(boot.PointerFromMouse, Is.True, "화면 시작점이 장치의 마우스를 흐름에 넘긴다");
                var client = flow.Client;
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
                input = new VirtualInput();
                yield return Frames(3);

                IEnumerator Point(Vector2 px, bool down = false, bool right = false)
                {
                    input.Point(px, down, right: right);
                    yield return Frames(2);
                }

                Assert.That(Near(out int near0, out var start), Is.True, "화면(" + w + "x" + h + ")에서 고를 수 있는 한 걸음 칸");
                yield return Point(start);
                Assert.That(flow.Hud.HoverCell, Is.EqualTo(near0));
                int commands = flow.Session.CommandCount;
                int seatCell = client.View.Seats[client.Seat].Cell;
                var target0 = rig.Target;
                int yaw0 = rig.Yaw;
                var zoom0 = rig.CurrentZoom;

                // ① 왼쪽 단추로 끈다: 문턱 안에서는 그대로, 넘으면 잡은 땅이 커서를 따라온다. 칸은 눌리지 않는다
                Assert.That(BoardPick.Ground(board, cam, start.x, start.y, w, h, rig.Target.y, out var grab), Is.True);
                yield return Point(start, down: true);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "누르는 것만으로는 칸이 눌리지 않는다(떼어야 누르기)");
                yield return Point(start + new Vector2(2f, 1f), down: true);
                Assert.That(flow.Dragging, Is.False, "문턱 안");
                Assert.That(rig.Target, Is.EqualTo(target0));
                Vector2 cursor = start;
                for (int i = 1; i <= 3; i++)
                {
                    cursor = start + new Vector2(20f, 10f) * i;
                    yield return Point(cursor, down: true);
                    Assert.That(flow.Dragging, Is.True, "문턱을 넘었다");
                    Assert.That(BoardPick.GroundAt(cam, cursor.x, cursor.y, w, h, grab.y, out var under), Is.True);
                    Assert.That(Vector3.Distance(under, grab), Is.LessThan(0.02f), "잡은 땅이 커서 아래에 있다(" + i + ")");
                    Assert.That(flow.Hud.HoverCell, Is.LessThan(0), "끄는 동안에는 칸을 칠하지 않는다");
                }
                Assert.That(Vector3.Distance(rig.Target, target0), Is.GreaterThan(0.5f), "보는 점이 옮겨졌다");
                Assert.That(rig.Target.y, Is.EqualTo(target0.y), "보는 점의 높이는 그대로");
                Assert.That(rig.Yaw, Is.EqualTo(yaw0));
                Assert.That(rig.CurrentZoom, Is.EqualTo(zoom0));
                yield return Point(cursor);
                Assert.That(flow.Dragging, Is.False);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "끌고 떼면 칸이 눌리지 않는다");
                Assert.That(client.View.Seats[client.Seat].Cell, Is.EqualTo(seatCell));
                var dragged = rig.Target;

                // ② HUD 판 위에서 시작한 끌기는 보드 위로 나와도 옮기지 않는다
                var dock = stage.Root.Q("board-hud-party").worldBound.center;
                var dockPx = new Vector2(dock.x * scale, h - dock.y * scale);
                Assert.That(flow.OverUi(dockPx.x, dockPx.y), Is.True);
                yield return Point(dockPx);
                yield return Point(dockPx, down: true);
                yield return Point(start, down: true);
                yield return Point(start + new Vector2(30f, 0f), down: true);
                Assert.That(flow.Dragging, Is.False, "HUD 위에서 시작한 끌기");
                Assert.That(rig.Target, Is.EqualTo(dragged), "HUD 위에서 시작하면 안 옮겨진다");
                yield return Point(start + new Vector2(30f, 0f));
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "HUD 위에서 누르고 보드 위에서 떼도 칸이 눌리지 않는다");
                Assert.That(stage.Current, Is.Null);

                // ③ 오른쪽 단추도 끈다 — 끌지 않고 떼도 칸은 눌리지 않는다
                yield return Point(start);
                yield return Point(start, right: true);
                yield return Point(start);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "오른쪽 단추는 칸을 누르지 않는다");
                yield return Point(start, right: true);
                yield return Point(start + new Vector2(-25f, 15f), right: true);
                Assert.That(flow.Dragging, Is.True, "오른쪽 단추로 끌기");
                Assert.That(Vector3.Distance(rig.Target, dragged), Is.GreaterThan(0.1f));
                yield return Point(start + new Vector2(-25f, 15f));
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands));

                // ④ 창(게임 메뉴)이 덮으면 끌어도 옮겨지지 않는다
                flow.OpenMenu();
                yield return Frames(2);
                var behind = rig.Target;
                yield return Point(start, down: true);
                yield return Point(start + new Vector2(40f, 20f), down: true);
                Assert.That(flow.Dragging, Is.False, "메뉴 뒤");
                Assert.That(rig.Target, Is.EqualTo(behind), "메뉴 뒤: 보드가 옮겨지지 않는다");
                yield return Point(start + new Vector2(40f, 20f));
                flow.CloseMenu();
                yield return Frames(3);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands));

                // ⑤ 옮겨진 화면에서 끌지 않고 떼면 그 칸이 눌린다(처음은 고르기 — 오른쪽 단추를 끌지 않고 떼면 풀리고, 다시 골라 한 번 더 누르면 걷는다, 결정 70 ⑱) → 걷는 말을 다시 따라가 도착 칸을 본다
                Assert.That(Near(out int near1, out var px1), Is.True, "끈 뒤 화면에서 고를 수 있는 한 걸음 칸");
                yield return Point(px1);
                Assert.That(flow.Hud.HoverCell, Is.EqualTo(near1), "옮겨진 화면에서 마우스를 올린 칸");
                yield return Point(px1, down: true);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "떼기 전");
                yield return Point(px1);
                Assert.That((flow.Hud.PickedCell, flow.Session.CommandCount), Is.EqualTo((near1, commands)), "끌지 않고 떼면 그 칸을 고른다(걷지 않는다)");
                yield return Point(px1, right: true);
                yield return Point(px1);
                Assert.That((flow.Hud.PickedCell, flow.Session.CommandCount), Is.EqualTo((-1, commands)), "오른쪽 단추를 끌지 않고 떼면 고른 칸이 풀린다");
                for (int press = 0; press < 2; press++)
                {
                    yield return Point(px1, down: true);
                    yield return Point(px1);
                }
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1), "고르고 한 번 더 누르면 이동 답 하나");
                Assert.That(client.View.Seats[client.Seat].Cell, Is.EqualTo(near1), "누른 칸으로 간다");
                yield return Until(() => flow.InputOpen, 15f);
                Assert.That(flow.InputOpen, Is.True);
                Assert.That(Vector3.Distance(rig.Target, board.CellTop(near1)), Is.LessThan(1e-3f), "다음 걸음에는 다시 걷는 말을 따라간다");

                // ⑥ 보는 점은 보드 밖으로 나가지 않는다: 한쪽으로 계속 끌면 범위 끝에서 멈춘다
                Bounds(out float minX, out float maxX, out float minZ, out float maxZ);
                Vector2 free = default;
                bool haveFree = false;
                for (int gy = 3; gy < 8 && !haveFree; gy++)
                    for (int gx = 2; gx < 9 && !haveFree; gx++)
                    {
                        free = new Vector2(w * gx / 10f, h * gy / 10f);
                        haveFree = !flow.OverUi(free.x, free.y);
                    }
                Assert.That(haveFree, Is.True, "PC 층이 덮지 않은 화면 자리");
                for (int i = 0; i < 60; i++)
                {
                    var was = rig.Target;
                    yield return Point(free);
                    yield return Point(free, down: true);
                    yield return Point(free + new Vector2(w * 0.4f, h * 0.3f), down: true);
                    yield return Point(free + new Vector2(w * 0.4f, h * 0.3f));
                    Assert.That(rig.Target.x, Is.InRange(minX - 1e-3f, maxX + 1e-3f), "보는 점 x가 범위 밖");
                    Assert.That(rig.Target.z, Is.InRange(minZ - 1e-3f, maxZ + 1e-3f), "보는 점 z가 범위 밖");
                    if (rig.Target == was) break;
                }
                bool edgeX = Mathf.Abs(rig.Target.x - minX) < 1e-3f || Mathf.Abs(rig.Target.x - maxX) < 1e-3f;
                bool edgeZ = Mathf.Abs(rig.Target.z - minZ) < 1e-3f || Mathf.Abs(rig.Target.z - maxZ) < 1e-3f;
                Assert.That(edgeX && edgeZ, Is.True, "한쪽으로 계속 끌면 범위의 모서리에서 멈춘다(" + rig.Target + ")");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1), "끌기만 해서는 답이 나가지 않는다");
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

        [UnityTest]
        public IEnumerator M4_걷는중에끌면따라가기를쉬고_다음걸음에다시따라간다()
        {
            yield return ToBoard();
            boot.PointerFromMouse = false;
            var client = flow.Client;

            IEnumerator Settle()
            {
                for (int i = 0; i < 200 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty || !flow.InputOpen); i++)
                {
                    flow.Skip(100);
                    yield return null;
                }
                yield return Frames(3);   // HUD가 갈 수 있는 칸을 다시 칠한다
            }

            // 걷기를 낸 바로 그 프레임에(연출 시각 0) 보드를 끈다 — 입력이 닫힌 연출 중에도 보기 조작은 된다
            Assert.That(Near(out int near0, out var px0), Is.True);
            int commands = flow.Session.CommandCount;
            flow.Pointer(px0.x, px0.y, true);
            Assert.That((flow.Hud.PickedCell, flow.Session.CommandCount), Is.EqualTo((near0, commands)), "한 번 누르면 고르기만(결정 70 ⑱)");
            flow.Pointer(px0.x, px0.y, true);
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1));
            Assert.That(flow.InputOpen, Is.False, "걷는 연출 중");
            flow.Skip(50);
            var free = new Vector2(w * 0.5f, h * 0.5f);
            Assert.That(flow.OverUi(free.x, free.y), Is.False);
            flow.Pointer(free.x, free.y, true, false);
            flow.Pointer(free.x + 30f, free.y + 20f, true, false);
            Assert.That(flow.Dragging, Is.True, "연출 중에도 끌 수 있다");
            var held = rig.Target;
            flow.Skip(50);
            Assert.That(rig.Target, Is.EqualTo(held), "끄는 동안 카메라가 걷는 말을 따라가지 않는다");
            flow.Pointer(free.x + 30f, free.y + 20f, false, false);
            Assert.That(flow.Dragging, Is.False);
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1), "끌고 떼면 칸이 눌리지 않는다");
            yield return Settle();
            Assert.That(flow.InputOpen, Is.True);
            Assert.That(client.View.Seats[client.Seat].Cell, Is.EqualTo(near0));
            Assert.That(rig.Target, Is.EqualTo(held), "끈 걸음이 끝나도 보는 점을 되돌리지 않는다");
            Assert.That(Vector3.Distance(rig.Target, board.CellTop(near0)), Is.GreaterThan(0.1f));

            // 다음 걸음: 지금 보는 점에서 걷는 말로 다시 다가가 도착 칸을 본다
            Assert.That(Near(out int near1, out var px1, oneStep: false), Is.True, "끈 뒤 화면에서 고를 수 있는 칸(갈 수 있는 칸 " + flow.Hud.Reach.Count + ")");
            flow.Pointer(px1.x, px1.y, true);
            flow.Pointer(px1.x, px1.y, true);
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 2));
            yield return Settle();
            // 길 위의 뒷면 숨겨진 장소에서 멈췄으면(결정 71 ③) 그 칸이 도착 칸이다
            int landed = client.View.Seats[client.Seat].Cell;
            Assert.That(landed == near1 || client.Choice(ChoiceKind.HiddenSpot) != null, Is.True, "고른 칸에 닿았거나 길 위 숨겨진 장소에서 멈췄다");
            Assert.That(Vector3.Distance(rig.Target, board.CellTop(landed)), Is.LessThan(1e-3f), "다음 걸음에는 다시 따라간다");
            flow.OpenTitle();
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator M4_게임시작의전체줌으로미끄러지는동안끌면되돌아가지않고_첫내턴컷신이내말로데려온다()
        {
            yield return ToBoard(toTurn: false);
            boot.PointerFromMouse = false;
            // 숨겨진 장소 배치(04 §8-8 5행) 한가운데: 카메라가 전체 줌으로 미끄러지는 중
            flow.Skip(flow.MsUntil(s => Sinnoh.Fx.SpotDropBeats.Spots(s) != null, 500));
            Assert.That(flow.Hud, Is.Not.Null, "보드 HUD가 떠 있다");
            Assert.That(stage.Current, Is.Null);
            Vector2 free = default;
            bool haveFree = false;
            for (int gy = 4; gy < 8 && !haveFree; gy++)
                for (int gx = 3; gx < 8 && !haveFree; gx++)
                {
                    free = new Vector2(w * gx / 10f, h * gy / 10f);
                    haveFree = !flow.OverUi(free.x, free.y);
                }
            Assert.That(haveFree, Is.True, "PC 층이 덮지 않은 화면 자리");
            flow.Pointer(free.x, free.y, true, false);
            flow.Pointer(free.x + 40f, free.y + 25f, true, false);
            Assert.That(flow.Dragging, Is.True, "배치 연출 중에도 끌 수 있다");
            var held = rig.Target;
            flow.Skip(16);
            Assert.That(rig.Target, Is.EqualTo(held), "끄는 동안 연출이 보는 점을 덮어쓰지 않는다");
            flow.Pointer(free.x + 40f, free.y + 25f, false, false);
            Assert.That(flow.Dragging, Is.False);
            flow.Skip(16);
            Assert.That(rig.Target, Is.EqualTo(held), "떼어도 연출 자리로 튀어 돌아가지 않는다");
            // 배치가 끝나도 보는 점은 끈 자리 그대로, 줌은 전체 줌
            flow.Skip(flow.MsUntil(s => Sinnoh.Fx.SpotDropBeats.Spots(s) != null, 1000) + 1);
            Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Full));
            Assert.That(rig.Target, Is.EqualTo(held), "배치가 끝나도 되돌리지 않는다");
            Assert.That(rig.Target, Is.Not.EqualTo(flow.WideLook()));
            // 첫 내 턴 컷신(다음 단계)은 지역 줌으로 내 말에 데려온다
            flow.Skip(flow.OpeningMs);
            yield return Until(() => flow.InputOpen, 15f);
            Assert.That(flow.InputOpen, Is.True);
            Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Region));
            var client = flow.Client;
            Assert.That(Vector3.Distance(rig.Target, board.CellTop(client.View.Seats[client.Seat].Cell)), Is.LessThan(1e-3f), "첫 내 턴 컷신이 내 말로 데려온다");
            flow.OpenTitle();
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator M4_누르고멀리서바로떼면이동답이나가지않는다()
        {
            yield return ToBoard();
            boot.PointerFromMouse = false;
            Assert.That(Near(out int near, out var px), Is.True);
            int commands = flow.Session.CommandCount;
            // 눌린 채 들어온 프레임 없이(빠른 끌기 · 끊긴 프레임) 갈 수 있는 칸 위에서 뗐다 — 누른 자리는 문턱 밖
            float thr = (float)Sinnoh.Fx.BoardDrag.Threshold(h);
            var from = px + new Vector2(thr * 3f, thr * 2f);
            if (flow.OverUi(from.x, from.y)) from = px - new Vector2(thr * 3f, thr * 2f);
            Assert.That(flow.OverUi(from.x, from.y), Is.False);
            flow.Pointer(from.x, from.y, true, false);
            flow.Pointer(px.x, px.y, false, false);
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "멀리서 누르고 바로 뗀 것은 칸 누르기가 아니다");
            Assert.That(flow.Hud.PickedCell, Is.EqualTo(-1), "칸 누르기가 아니면 고르지도 않는다");
            // 같은 자리에서 누르고 떼면 누르기 — 처음은 고르기(걷기 전 확인)
            flow.Pointer(px.x, px.y, true, false);
            flow.Pointer(px.x, px.y, false, false);
            Assert.That((flow.Hud.PickedCell, flow.Session.CommandCount), Is.EqualTo((near, commands)));
            // 오른쪽 단추를 끌지 않고 눌렀다 떼면 풀린다 · 끌면(보드 옮기기) 풀리지 않는다
            flow.Pointer(px.x, px.y, false, true);
            flow.Pointer(px.x, px.y, false, false);
            Assert.That(flow.Hud.PickedCell, Is.EqualTo(-1), "오른쪽 클릭 = 풀기");
            flow.Pointer(px.x, px.y, true, false);
            flow.Pointer(px.x, px.y, false, false);
            Assert.That(flow.Hud.PickedCell, Is.EqualTo(near));
            flow.Pointer(px.x, px.y, true, false);
            flow.Pointer(px.x, px.y, false, false);
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1), "고른 칸을 한 번 더 누르면 걷는다");
            Assert.That(flow.Client.View.Seats[flow.Client.Seat].Cell, Is.EqualTo(near));
            flow.OpenTitle();
            yield return Frames(2);
        }
    }
}
