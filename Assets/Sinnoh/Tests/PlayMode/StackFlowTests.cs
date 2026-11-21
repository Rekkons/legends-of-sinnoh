using System.Collections;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 한 칸에 말 여럿(결정 66 ①, 04 §3-1): 좌석 여럿인 확인용 판(<see cref="GameFlow.StartSeats"/>)에서 말 셋을 한 칸에 모아 — 하나만 서고 묶음 표지가 뜨는지,
    /// 표지가 마우스를 막지 않고 공개 정보 카드를 띄우는지, 걷는 말이 묶음에서 빠지고 돌아와 묶이는지 본다. Gen 겉모양·말 그림이 있어야 돈다.
    /// </summary>
    public sealed class StackFlowTests
    {
        const int W = 1920, H = 1080;

        ScreenBoot boot;
        GameFlow flow;
        BoardBoot board;
        RenderTexture rt;

        IEnumerator Open(int seats)
        {
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            flow = boot.Flow;
            boot.PointerFromMouse = false;
            board = Object.FindAnyObjectByType<BoardBoot>();
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            if (board.Pieces.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저 — 말 확인 건너뜀(03 §7)");
            flow.StartSeats(new SoloSettings(), seats, 20261112, 7, state =>
            {
                for (int i = 1; i < seats; i++) state.Seats[i].Cell = state.Seats[0].Cell;
            });
            for (int i = 0; i < 6; i++) yield return null;
            yield return Settle();
        }

        IEnumerator Settle()
        {
            for (int i = 0; i < 200 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty); i++)
            {
                flow.Skip(100);
                yield return null;
            }
            yield return null;
            yield return null;
        }

        [TearDown]
        public void Shut()
        {
            var b = Object.FindAnyObjectByType<ScreenBoot>();
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
            }
            rt = null;
            Tunables.Shared.Reset();
        }

        [UnityTest]
        public IEnumerator M4_한칸에말여럿이면하나만서고묶음표지가뜬다()
        {
            yield return Open(3);
            var view = flow.Client.View;
            int cell = view.Seats[0].Cell;
            var pieces = board.Pieces;
            Assert.That(view.Seats.Length, Is.EqualTo(3));
            Assert.That(pieces.Pieces.Count, Is.EqualTo(3));

            // 말: 내 말(좌석 1)만 서고 나머지 둘은 묶음에 가려 숨는다
            Assert.That(pieces.Stacks.Count, Is.EqualTo(1));
            var stack = pieces.StackAt(cell);
            Assert.That(stack, Is.Not.Null);
            Assert.That(stack.Seats, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(stack.Stand, Is.EqualTo(0), "그 칸에 내 좌석이 있으면 내 말이 선다(턴인 좌석 " + view.ActiveSeat + "보다 먼저)");
            Assert.That(pieces.Of(0).Root.activeSelf, Is.True);
            Assert.That(pieces.Of(1).Root.activeSelf, Is.False);
            Assert.That(pieces.Of(2).Root.activeSelf, Is.False);
            Assert.That(pieces.Tucked(1) && pieces.Tucked(2) && !pieces.Tucked(0), Is.True);
            Assert.That(pieces.Of(1).Spot.Cell, Is.EqualTo(cell), "숨은 말도 자리는 그 칸이다");

            // 표지: 하나, 사람 수 3, 세워 둔 말의 핀 위, 시각에 따라 한 명씩
            var hud = flow.Hud;
            var badges = hud.Stacks;
            flow.StackMs = 0;
            hud.ShowStacks();
            yield return null;
            Assert.That(badges.Count, Is.EqualTo(1));
            Assert.That(badges.Total(cell), Is.EqualTo(3));
            Assert.That(boot.Root.Stage.Root.Q<Label>("board-stack-count").text, Is.EqualTo("×3"));
            Assert.That(badges.ShownSeat(cell), Is.EqualTo(0));
            Assert.That(badges.RectOf(cell, out var rect), Is.True);
            Assert.That(flow.HeadPoint(pieces.Of(0), out var head), Is.True);
            Assert.That(rect.yMax, Is.EqualTo(head.y - StackBadges.Gap).Within(0.01f), "표지 아랫변 = 핀 윗변 위 틈");
            Assert.That(rect.center.x, Is.EqualTo(head.x).Within(1f));
            int scale = boot.Root.Stage.Grade.PanelScale, tokens = boot.Root.Stage.Grade.TokenHeight;
            var art = boot.Root.Stage.Root.Q<Image>("board-stack-art");
            Assert.That(art.image, Is.Not.Null);
            Assert.That(rect.width, Is.EqualTo(art.image.width * StackArt.Mul(pieces.Of(0).Style, tokens, flow.ZoomStep)), "그림 텍셀의 정수배");
            Assert.That(rect.x, Is.EqualTo(Mathf.Round(rect.x)), "정수 자리");
            var first = art.image;
            int dwell = PieceStacks.DwellMs.Value;
            flow.StackMs = dwell;
            hud.ShowStacks();
            Assert.That(badges.ShownSeat(cell), Is.EqualTo(1), "머무는 시간이 지나면 다음 좌석");
            Assert.That(art.image, Is.Not.SameAs(first), "좌석마다 그림(주인공·좌석 색)이 다르다");
            flow.StackMs = 2L * dwell;
            hud.ShowStacks();
            Assert.That(badges.ShownSeat(cell), Is.EqualTo(2));
            flow.StackMs = 3L * dwell;
            hud.ShowStacks();
            Assert.That(badges.ShownSeat(cell), Is.EqualTo(0));

            // 마우스: 표지는 마우스를 받지 않는다(칸 고르기·휠 그대로) — 올리면 그 칸의 좌석 셋(내 좌석 포함)이 뜬다
            float x = rect.center.x * scale, y = H - rect.center.y * scale;
            var picked = boot.Root.Stage.Root.panel.Pick(rect.center);
            Assert.That(flow.OverUi(x, y), Is.False, "표지 위는 PC 층이 마우스를 받지 않는다: " + (picked != null ? picked.name + "/" + picked.GetType().Name + "/" + string.Join(" ", picked.GetClasses()) : "없음"));
            Assert.That(badges.CellAt(rect.center), Is.EqualTo(cell));
            Assert.That(badges.CellAt(new Vector2(rect.xMin + 0.5f, rect.yMin + 0.5f)), Is.EqualTo(-1), "동그란 표지의 모서리는 표지 밖");
            int under = flow.Pointer(x, y, false);
            Assert.That(Camera.main.GetComponent<BoardCameraRig>().WheelEnabled, Is.True, "표지 위에서도 휠이 듣는다");
            Assert.That(under, Is.EqualTo(hud.HoverAny), "칸 고르기는 표지 밑의 칸 그대로");
            Assert.That(hud.Info.Subject, Is.EqualTo("cell-" + cell));
            yield return null;
            var card = hud.Info.Element;
            Assert.That(card.Q("info-card-stack"), Is.Not.Null);
            for (int s = 1; s <= 3; s++) Assert.That(card.Q("info-card-seat-" + s), Is.Not.Null, "좌석 " + s);
            flow.Pointer(4f, H * 0.5f, false);
            Assert.That(hud.Info.Subject, Is.Not.EqualTo("cell-" + cell), "표지를 벗어나면 그 칸의 카드는 걷힌다");

            // 줌 셋 · 회전: 표지는 늘 핀 위(전체 줌은 한 단계 작게)
            var rig = Camera.main.GetComponent<BoardCameraRig>();
            foreach (BoardCameraRig.Zoom zoom in System.Enum.GetValues(typeof(BoardCameraRig.Zoom)))
                for (int yaw = 0; yaw < 6; yaw += 2)
                {
                    rig.Set(zoom, yaw, rig.Target);
                    flow.Layout(W, H);
                    Assert.That(badges.RectOf(cell, out var r), Is.True, zoom + " " + yaw);
                    Assert.That(flow.HeadPoint(pieces.Of(0), out var h), Is.True, zoom + " " + yaw);
                    Assert.That(r.yMax, Is.EqualTo(h.y - StackBadges.Gap).Within(0.01f), zoom + " " + yaw);
                    Assert.That(r.width, Is.EqualTo(StackBadges.Size(pieces.Of(0), tokens, BoardPieces.ZoomK(zoom))), zoom.ToString());
                    // 머리 위에 띄우는 것(뜬 숨겨진 장소 토큰)은 표지 위다
                    Assert.That(flow.SpotHover(cell), Is.GreaterThanOrEqualTo(BoardTokens.HoverHeight));
                }

            // 직업 그림 양식: 다시 세워도 묶음은 그대로, 표지 안은 얼굴 조각(30 + 테두리 6)
            flow.PieceStyle = PieceStyle.Job;
            yield return null;
            pieces = board.Pieces;
            Assert.That(pieces.Stacks.Count, Is.EqualTo(1));
            Assert.That(pieces.Of(0).Style, Is.EqualTo(PieceStyle.Job));
            Assert.That(pieces.Of(1).Root.activeSelf, Is.False);
            flow.StackMs = 0;
            flow.Layout(W, H);
            Assert.That(art.image.width, Is.EqualTo(pieces.Of(0).Face.width + 2 * StackArt.Rim));
            Assert.That(badges.Total(cell), Is.EqualTo(3));
            flow.PieceStyle = PieceStyle.Dot;
        }

        [UnityTest]
        public IEnumerator M4_걷는말은묶음에서빠지고_돌아오면다시묶인다()
        {
            yield return Open(3);
            var view = flow.Client.View;
            int cell = view.Seats[0].Cell, walker = view.ActiveSeat;
            var pieces = board.Pieces;
            var badges = flow.Hud.Stacks;
            Assert.That(walker, Is.InRange(0, 2));
            var steps = MoveRules.Reachable(flow.Ctx.Board, cell, view.Seats[walker].MovesLeft, view.Options.ElevationMode);
            int to = -1;
            foreach (int c in flow.ActiveReach())
            {
                // 숨겨진 장소가 없는 칸으로(밟으면 멈춰 서서 묻는다 — 돌아오는 걸음을 보려는 시험이다)
                bool spot = false;
                foreach (var t in view.Tokens) if (t.Cell == c && !t.Destroyed && t.Kind == TokenKind.HiddenSpot) spot = true;
                if (steps[c] == 1 && !spot) { to = c; break; }
            }
            Assert.That(to, Is.GreaterThanOrEqualTo(0), "한 걸음 떨어진, 숨겨진 장소가 없는 갈 수 있는 칸");

            // 걸어 나간다: 걷는 동안 그 말은 따로 보이고, 남은 둘이 묶여 있다
            Assert.That(flow.WalkActive(to), Is.True);
            flow.Skip(120);
            yield return null;
            Assert.That(pieces.Of(walker).Root.activeSelf, Is.True, "걷는 말은 묶음에서 빠져 따로 걷는다");
            Assert.That(pieces.Tucked(walker), Is.False);
            var left = pieces.StackAt(cell);
            Assert.That(left, Is.Not.Null);
            Assert.That(left.Count, Is.EqualTo(2));
            Assert.That(System.Array.IndexOf(left.Seats, walker), Is.LessThan(0));
            Assert.That(left.Stand, Is.EqualTo(walker == 0 ? 1 : 0), "내 말이 남았으면 내 말, 내가 걸어 나갔으면 번호가 앞선 말");
            Assert.That(badges.Total(cell), Is.EqualTo(2));
            yield return Settle();
            Assert.That(pieces.Of(walker).Spot.Cell, Is.EqualTo(to));
            Assert.That(pieces.Of(walker).Root.activeSelf, Is.True);
            Assert.That(pieces.StackAt(to), Is.Null, "혼자 선 칸에는 묶음이 없다");
            Assert.That(pieces.StackAt(cell).Count, Is.EqualTo(2));
            Assert.That(badges.Count, Is.EqualTo(1));

            // 돌아온다: 걸음이 끝나면(대기열이 비기 전이라도) 묶인다
            Assert.That(flow.ActiveReach(), Does.Contain(cell), "돌아갈 이동력이 남았다: 턴 " + flow.Client.View.ActiveSeat + " 이동력 " + flow.Client.View.Seats[walker].MovesLeft + " 내 선택 " + (flow.Client.FirstChoice() != null ? flow.Client.FirstChoice().Kind.ToString() : "없음"));
            Assert.That(flow.WalkActive(cell), Is.True);
            flow.Skip(60);
            yield return null;
            Assert.That(pieces.StackAt(cell).Count, Is.EqualTo(2), "걷는 중에는 아직 묶이지 않았다");
            yield return Settle();
            var back = pieces.StackAt(cell);
            Assert.That(back.Count, Is.EqualTo(3));
            Assert.That(back.Stand, Is.EqualTo(0));
            Assert.That(pieces.Of(0).Root.activeSelf, Is.True);
            Assert.That(pieces.Of(1).Root.activeSelf, Is.False);
            Assert.That(pieces.Of(2).Root.activeSelf, Is.False);
            flow.Layout(W, H);
            Assert.That(badges.Total(cell), Is.EqualTo(3));
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator M4_여섯이한칸이어도말은하나_묶지않으면여섯자리()
        {
            yield return Open(6);
            var pieces = board.Pieces;
            int cell = flow.Client.View.Seats[0].Cell, shown = 0;
            foreach (var p in pieces.Pieces) if (p.Root.activeSelf) shown++;
            Assert.That(pieces.Pieces.Count, Is.EqualTo(6));
            Assert.That(shown, Is.EqualTo(1));
            Assert.That(pieces.StackAt(cell).Count, Is.EqualTo(6));
            flow.Layout(W, H);
            Assert.That(flow.Hud.Stacks.Total(cell), Is.EqualTo(6));
            // 공개 정보 카드: 좌석 여섯이 화면 안에 든다
            flow.Hud.Hover(-1, cell);
            yield return null;
            yield return null;
            var card = flow.Hud.Info.Element;
            Assert.That(card.Q("info-card-seat-6"), Is.Not.Null);
            Assert.That(card.worldBound.yMax, Is.LessThanOrEqualTo(boot.Root.Stage.Root.worldBound.yMax + 0.5f), "카드가 화면 아래로 넘치지 않는다");
            Assert.That(card.worldBound.yMin, Is.GreaterThanOrEqualTo(-0.5f));

            // 묶지 않는 길(M4-2 기준 그림): 칸 안 6자리로 모두 선다
            pieces.Stacking = false;
            pieces.Arrange(new[] { cell, cell, cell, cell, cell, cell });
            shown = 0;
            foreach (var p in pieces.Pieces) if (p.Root.activeSelf) shown++;
            Assert.That(shown, Is.EqualTo(6));
            Assert.That(pieces.Stacks.Count, Is.EqualTo(0));
            pieces.Stacking = true;
        }

        [UnityTest]
        public IEnumerator M4_공개정보카드는사람수배지를덮지않고_표지위에서끌면표지카드가걷힌다()
        {
            yield return Open(3);
            int cell = flow.Client.View.Seats[0].Cell;
            var hud = flow.Hud;
            var badges = hud.Stacks;
            flow.StackMs = 0;
            hud.ShowStacks();
            yield return null;
            yield return null;
            Assert.That(badges.RectOf(cell, out var rect), Is.True);
            Assert.That(badges.BoundsOf(cell, out var bounds), Is.True);
            Assert.That(bounds.xMax, Is.GreaterThan(rect.xMax + 4f), "사람 수 배지는 표지 원 밖 오른쪽에 붙는다");
            int scale = boot.Root.Stage.Grade.PanelScale;
            float x = rect.center.x * scale, y = H - rect.center.y * scale;
            flow.Pointer(x, y, false);
            Assert.That(hud.Info.Subject, Is.EqualTo("cell-" + cell));
            for (int i = 0; i < 3; i++) yield return null;
            var card = boot.Root.Stage.Root.WorldToLocal(hud.Info.Element.worldBound);
            Assert.That(card.width, Is.GreaterThan(0f));
            Assert.That(card.Overlaps(bounds), Is.False, "카드 " + card + "가 표지·배지 " + bounds + "를 덮는다");

            // 표지 위에서 끌기 시작: 잡은 땅이 커서를 따라와 표지는 계속 커서 밑이지만 카드는 걷힌다(칸 카드와 같다)
            flow.Pointer(x, y, true, false);
            Assert.That(hud.Info.Subject, Is.EqualTo("cell-" + cell), "누르기만 해서는 그대로");
            flow.Pointer(x + 40f, y + 20f, true, false);
            Assert.That(flow.Dragging, Is.True);
            Assert.That(hud.Info.Subject, Is.Null, "끄는 동안에는 표지 카드도 띄우지 않는다");
            flow.Layout(W, H);
            flow.Pointer(x + 60f, y + 30f, true, false);
            Assert.That(hud.Info.Subject, Is.Null);
            flow.Pointer(x + 60f, y + 30f, false, false);
            Assert.That(flow.Dragging, Is.False);
        }
    }
}
