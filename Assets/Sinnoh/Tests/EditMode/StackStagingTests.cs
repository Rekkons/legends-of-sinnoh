using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 한 칸에 말 여럿(결정 66 ①, 04 §3-1)의 순수 셈: 묶기 · 세워 두는 말 고르기 · 걷는 말 빼기 · 표지 안에서 돌아가는 그림(시각만 받는다).
    /// </summary>
    public class StackStagingTests
    {
        [TearDown]
        public void Reset() => Tunables.Shared.Reset();

        [Test]
        public void M4_한칸에둘이상이면묶음하나_혼자선말은묶지않는다()
        {
            // 좌석 0·2·5가 칸 40, 좌석 1이 칸 7, 좌석 3은 판에 없음, 좌석 4가 칸 9
            var stacks = PieceStacks.Group(new[] { 40, 7, 40, -1, 9, 40 }, viewer: -1, active: -1);
            Assert.AreEqual(1, stacks.Count);
            Assert.AreEqual(40, stacks[0].Cell);
            CollectionAssert.AreEqual(new[] { 0, 2, 5 }, stacks[0].Seats, "좌석 번호 순 — 세워 둔 말 포함");
            Assert.AreEqual(3, stacks[0].Count);
            Assert.AreEqual(0, stacks[0].Stand, "보는 좌석도 턴인 좌석도 없으면 번호가 가장 앞선 말");
            Assert.IsFalse(PieceStacks.Tucked(stacks, 0), "세워 둔 말은 보인다");
            Assert.IsTrue(PieceStacks.Tucked(stacks, 2));
            Assert.IsTrue(PieceStacks.Tucked(stacks, 5));
            Assert.IsFalse(PieceStacks.Tucked(stacks, 1), "혼자 선 말");
            Assert.IsNull(PieceStacks.At(stacks, 7));
            Assert.AreSame(stacks[0], PieceStacks.At(stacks, 40));
            Assert.AreSame(stacks[0], PieceStacks.Of(stacks, 5));
            Assert.IsNull(PieceStacks.Of(stacks, 3));
        }

        [Test]
        public void M4_세워두는말은내말_없으면턴인좌석_없으면앞선번호()
        {
            var cells = new[] { 12, 12, 12, 30, 30, 30 };
            var mine = PieceStacks.Group(cells, viewer: 2, active: 1);
            Assert.AreEqual(2, mine[0].Stand, "칸 12: 내 좌석(2)이 있다 — 턴인 좌석(1)보다 먼저");
            Assert.AreEqual(3, mine[1].Stand, "칸 30: 내 좌석도 턴인 좌석도 없다 — 번호가 앞선 3");
            var turn = PieceStacks.Group(cells, viewer: 0, active: 5);
            Assert.AreEqual(0, turn[0].Stand);
            Assert.AreEqual(5, turn[1].Stand, "칸 30: 내 좌석이 없으면 턴인 좌석");
            Assert.AreEqual(-1, PieceStacks.Stand(new int[0], 0, 0));
            Assert.AreEqual(4, PieceStacks.Stand(new[] { 5, 4 }, viewer: 9, active: -1), "준 순서와 무관하게 가장 앞선 번호");
        }

        [Test]
        public void M4_걷는말은묶음에서빠지고_둘이던묶음은풀린다()
        {
            var cells = new[] { 12, 12, 12, 30, 30, -1 };
            var walking = new HashSet<int> { 0, 4 };
            var stacks = PieceStacks.Group(cells, viewer: 0, active: 0, moving: walking);
            Assert.AreEqual(1, stacks.Count, "칸 30은 좌석 4가 걸어 나가 혼자 남았다 — 묶음이 풀린다");
            Assert.AreEqual(12, stacks[0].Cell);
            CollectionAssert.AreEqual(new[] { 1, 2 }, stacks[0].Seats);
            Assert.AreEqual(1, stacks[0].Stand, "내 말(0)이 걸어 나갔고 턴인 좌석(0)도 없다 — 번호가 앞선 1");
            Assert.IsFalse(PieceStacks.Tucked(stacks, 0), "걷는 말은 가려지지 않는다");
            Assert.IsFalse(PieceStacks.Tucked(stacks, 3));
            // 도착하면 묶인다: 좌석 4가 칸 12에 닿았다
            var landed = PieceStacks.Group(new[] { 12, 12, 12, 30, 12, -1 }, viewer: 0, active: 4);
            Assert.AreEqual(1, landed.Count);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 4 }, landed[0].Seats);
            Assert.AreEqual(0, landed[0].Stand);
        }

        [Test]
        public void M4_묶음은칸번호순_여섯이한칸이면여섯()
        {
            var two = PieceStacks.Group(new[] { 50, 3, 50, 3 }, -1, -1);
            Assert.AreEqual(3, two[0].Cell);
            Assert.AreEqual(50, two[1].Cell);
            var six = PieceStacks.Group(new[] { 8, 8, 8, 8, 8, 8 }, viewer: 3, active: 0);
            Assert.AreEqual(1, six.Count);
            Assert.AreEqual(6, six[0].Count);
            Assert.AreEqual(3, six[0].Stand);
            Assert.AreEqual(0, PieceStacks.Group(null, 0, 0).Count);
        }

        [Test]
        public void M4_표지안그림은시각만으로돌아간다_한명당머무는시간은조정값()
        {
            Assert.AreEqual(TunableScope.Local, PieceStacks.DwellMs.Scope);
            Assert.AreSame(PieceStacks.DwellMs, Tunables.Shared.Find("fx.stackDwellMs"));
            int dwell = PieceStacks.DwellMs.Value;
            var stack = new PieceStack(40, 2, new[] { 0, 2, 5 });
            Assert.AreEqual(0, PieceStacks.Shown(stack, 0));
            Assert.AreEqual(0, PieceStacks.Shown(stack, dwell - 1));
            Assert.AreEqual(2, PieceStacks.Shown(stack, dwell));
            Assert.AreEqual(5, PieceStacks.Shown(stack, 2 * dwell));
            Assert.AreEqual(0, PieceStacks.Shown(stack, 3 * dwell), "한 바퀴 돌면 처음 좌석");
            Assert.AreEqual(PieceStacks.Shown(stack, 7 * dwell + 5), PieceStacks.Shown(stack, 7 * dwell + 5), "같은 시각이면 같은 그림");
            Assert.AreEqual(0, PieceStacks.Shown(stack, -500), "음수 시각은 0");

            Assert.IsTrue(Tunables.Shared.Set("fx.stackDwellMs", 400));
            Assert.AreEqual(2, PieceStacks.Shown(stack, 400), "머무는 시간을 줄이면 그만큼 빨리 넘어간다");
            Assert.AreEqual(5, PieceStacks.Shown(stack, 800));

            Assert.AreEqual(0, PieceStacks.Turn(0, 1000, 100));
            Assert.AreEqual(1, PieceStacks.Turn(2, 150, 100));
            Assert.AreEqual(0, PieceStacks.Turn(2, 250, 100));
            Assert.AreEqual(1, PieceStacks.Turn(6, long.MaxValue / 2, 0) >= 0 ? 1 : 0, "아주 큰 시각·0 간격에도 범위 안");
        }
    }
}
