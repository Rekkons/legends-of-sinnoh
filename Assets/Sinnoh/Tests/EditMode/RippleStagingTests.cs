using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>공간 일그러짐(M9 다듬기 P39)의 순수 박자 <see cref="RippleBeats"/>: 보드(잔물결 · 칸 파괴 · 신 강림) · DS(기라티나의 그림자 · 균열 전환).</summary>
    public class RippleStagingTests
    {
        static FxEntry Entry(params FxStep[] steps)
        {
            var e = new FxEntry();
            foreach (var s in steps) e.Steps.Add(s);
            return e;
        }

        static FxStep Step(int row, int start, int ms, params GameEvent[] events)
        {
            var s = new FxStep { Row = row, StartMs = start, Ms = ms };
            s.Events.AddRange(events);
            return s;
        }

        [Test]
        public void M9_보드의일그러짐은깨어진세계의잔물결이고_칸파괴와신강림에터졌다잦아든다_P39()
        {
            // 깨어진 세계가 보이지 않는 판(신오의 심장)은 평소에 없다
            Assert.IsFalse(RippleBeats.Board(null, 0, 1234, false).On);
            // 보이는 판: 대기열이 비어 있어도 잔물결 — 깨어진 세계 전체 · 위상은 시계에서만
            var calm = RippleBeats.Board(null, 0, 600, true);
            Assert.IsTrue(calm.On);
            Assert.AreEqual(RippleBeats.WholeWorld, calm.Cell);
            Assert.AreEqual(RippleBeats.Ambient, calm.Amp);
            Assert.AreEqual(0.25f, calm.Phase, 1e-6f);
            Assert.AreEqual(calm.Phase, RippleBeats.Board(null, 0, 600 + RippleBeats.PeriodMs * 7L, true).Phase, 1e-6f, "한 바퀴마다 같은 그림");
            Assert.AreEqual(RippleBeats.Board(null, 0, 600, true).Phase, RippleBeats.Board(null, 0, 600, true).Phase, "같은 시각이면 같은 값");

            // 칸 파괴(830): 그 칸 둘레가 단계 길이 동안 솟았다 가라앉는다 — 잔물결보다 크고 한도 안
            var broke = Entry(Step(UbRows.Move, 0, 400), Step(UbRows.Destroy, 400, 1000, new CellDestroyed { Cell = 77 }));
            Assert.AreEqual(RippleBeats.WholeWorld, RippleBeats.Board(broke, 200, 0, true).Cell, "이동 단계 동안은 잔물결");
            var mid = RippleBeats.Board(broke, 900, 0, true);
            Assert.AreEqual(77, mid.Cell);
            Assert.AreEqual(RippleBeats.Burst, mid.Amp, 1e-4f, "단계 가운데에서 가장 크다");
            Assert.AreEqual(RippleBeats.BurstCells, mid.Cells);
            Assert.That(RippleBeats.Board(broke, 450, 0, true).Amp, Is.GreaterThan(0f).And.LessThan(mid.Amp));
            Assert.That(RippleBeats.Board(broke, 1350, 0, true).Amp, Is.GreaterThan(0f).And.LessThan(mid.Amp));
            Assert.AreEqual(RippleBeats.WholeWorld, RippleBeats.Board(broke, 1400, 0, true).Cell, "단계가 끝나면 잔물결로");
            Assert.That(RippleBeats.Burst, Is.GreaterThan(RippleBeats.Ambient * 2));

            // 신 강림(341): 첫 번개에 터져 착지 고리가 끝날 때까지 잦아든다 — 신오의 심장 판에서도(깨어진 세계가 안 보여도)
            var descend = Entry(Step(DescendBeats.Row, 100, 4400, new GodDescended { God = 1, Cell = 12 }));
            Assert.IsFalse(RippleBeats.Board(descend, 100 + DescendBeats.BoltAt - 1, 0, false).On, "번개 전에는 없다");
            var bolt = RippleBeats.Board(descend, 100 + DescendBeats.BoltAt + 150, 0, false);
            Assert.AreEqual(12, bolt.Cell);
            Assert.That(bolt.Amp, Is.GreaterThan(RippleBeats.Burst * 0.8f).And.LessThanOrEqualTo(RippleBeats.Burst));
            var late = RippleBeats.Board(descend, 100 + DescendBeats.LandAt, 0, false);
            Assert.That(late.Amp, Is.GreaterThan(0f).And.LessThan(bolt.Amp), "잦아든다");
            Assert.IsFalse(RippleBeats.Board(descend, 100 + DescendBeats.LandAt + DescendBeats.RingMs, 0, false).On, "착지 고리가 끝나면 없다");
        }

        [Test]
        public void M9_DS의일그러짐은기라티나의그림자와균열전환에만있다_P39()
        {
            Assert.AreEqual(0f, RippleBeats.Ds(null, 0));
            // 기라티나(440): 그림자가 덮인 만큼
            var shade = Entry(Step(FateRows.Sacrifice, 300, 1500));
            Assert.AreEqual(0f, RippleBeats.Ds(shade, 299));
            Assert.AreEqual(RippleBeats.DsShadowPx, RippleBeats.Ds(shade, 300 + 1500 * SacrificeBeats.CoverPermille / 1000), 1e-4f, "다 덮였을 때 가장 크다");
            Assert.That(RippleBeats.Ds(shade, 300 + 200), Is.GreaterThan(0f).And.LessThan(RippleBeats.DsShadowPx));
            Assert.AreEqual(0f, RippleBeats.Ds(shade, 1800));

            // 균열 전환(22행 — 울트라 비스트 · 네크로즈마): 화면이 바뀐 때(500‰)부터 단계 끝 + 꼬리까지 잦아든다. 다른 상대의 전환에는 없다
            foreach (BattleKind kind in System.Enum.GetValues(typeof(BattleKind)))
            {
                var enter = Entry(Step(EnterRows.Wipe, 0, 2000, new BattleStarted { Kind = kind }));
                bool rift = kind == BattleKind.UltraBeast || kind == BattleKind.Necrozma;
                Assert.AreEqual(0f, RippleBeats.Ds(enter, 999), kind + ": 화면이 바뀌기 전");
                Assert.AreEqual(rift ? RippleBeats.DsRiftPx : 0f, RippleBeats.Ds(enter, 1000), 1e-4f, kind + ": 화면이 바뀐 때");
                if (!rift) continue;
                Assert.That(RippleBeats.Ds(enter, 2000), Is.GreaterThan(0f).And.LessThan(RippleBeats.DsRiftPx), "단계가 끝난 뒤에도 꼬리가 남는다");
                Assert.AreEqual(0f, RippleBeats.Ds(enter, 2000 + RippleBeats.RiftTailMs));
            }
            Assert.That(RippleBeats.DsKeepRows, Is.EqualTo(48), "메시지 창 줄(아래 48줄)은 밀지 않는다");
        }
    }
}
