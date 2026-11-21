using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M4-3 「주사위 굴림」(결정 50, spikes/dice-roll §2~§4, 04 §8-8 43·56·57행): 녹화본 형식·묶음, 고르기(섞기 주머니 + 앞뒤 뒤집기), 면 붙이기, Seek 재생(면 바꾸기·글리치 재굴림), 시간표.
    /// 엔진·실시각 없음.
    /// </summary>
    public class FxDiceTests
    {
        const AttackDieFace S = AttackDieFace.StatusSymbol, R = AttackDieFace.RegularHit, C = AttackDieFace.CriticalHit;

        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        /// <summary>지어낸 녹화본: 주사위마다 다른 가로 자리에, 아무 요각 × 아무 대칭으로 바닥에 멈춘다. 형식(적기 → 읽기)을 지난다.</summary>
        static DiceClip Synth(int dice, IRng rng, int frames = 12)
        {
            var pos = new V3[dice * frames];
            var rot = new Q4[dice * frames];
            var thrown = new bool[dice * frames];
            var xs = Enumerable.Range(0, dice).Select(i => 1f + i * 1.2f).OrderBy(_ => rng.Next(1000)).ToArray();
            for (int i = 0; i < dice; i++)
            {
                var rest = Q4.AngleAxis(rng.Next(360), V3.Up) * DiceCube.Syms[rng.Next(24)];
                var axis = DiceCube.Normal[rng.Next(6)];
                float z = 0.6f + rng.Next(150) / 100f;
                for (int f = 0; f < frames; f++)
                {
                    int left = Math.Max(0, frames - 4 - f);   // 마지막 네 프레임은 멈춰 있다
                    pos[f * dice + i] = new V3(xs[i] - left * 0.3f, 0.5f + left * 0.2f, z);
                    rot[f * dice + i] = (Q4.AngleAxis(37f * left, axis) * rest).Normalized;
                    thrown[f * dice + i] = f >= i % 3;
                }
            }
            var bytes = new List<byte>();
            DiceClip.Write(bytes, dice, frames, 1f / 60f, (frames - 4) / 60f, pos, rot, thrown);
            int at = 0;
            var clip = DiceClip.Read(bytes.ToArray(), ref at);
            Assert.AreEqual(bytes.Count, at);
            return clip;
        }

        static AttackDieFace[] Faces(int dice, IRng rng) => Enumerable.Range(0, dice).Select(_ => rng.RollAttackDie()).ToArray();

        [Test]
        public void R9_굴림판주사위의면구성은규칙주사위와같다()
        {
            var rule = Enumerable.Range(0, AttackDice.Faces).Select(AttackDice.FaceOf).OrderBy(f => f).ToArray();
            CollectionAssert.AreEqual(rule, DiceCube.FaceAt.OrderBy(f => f).ToArray());
            // 급소와 심볼이 맞은편 [가정]
            Assert.AreEqual(C, DiceCube.FaceAt[2]);
            Assert.AreEqual(S, DiceCube.FaceAt[3]);
        }

        [Test]
        public void M4_정육면체대칭은24개이고서로다르다()
        {
            Assert.AreEqual(24, DiceCube.Syms.Length);
            for (int i = 0; i < 24; i++)
                for (int j = 0; j < i; j++)
                    Assert.IsFalse(Q4.Same(DiceCube.Syms[i], DiceCube.Syms[j]), $"{i} = {j}");
            // 면마다 네 가지씩 위로 온다
            foreach (int face in Enumerable.Range(0, 6))
                Assert.AreEqual(4, DiceCube.Syms.Count(s => DiceCube.TopFace(s) == face));
        }

        [Test]
        public void M4_녹화본은적은대로읽힌다()
        {
            var pos = new[] { new V3(0.5f, 2.25f, 1.5f), new V3(-0.6f, 1.5f, 1f), new V3(11.9f, 0.5f, 2.2f), new V3(3f, 0.5f, 0.6f) };
            var rot = new[] { Q4.Identity, Q4.AngleAxis(33, V3.Up), Q4.AngleAxis(121, new V3(0.6f, 0.8f, 0f)), Q4.AngleAxis(250, V3.Forward) };
            var thrown = new[] { true, false, true, true };
            var bytes = new List<byte>();
            DiceClip.Write(bytes, 2, 2, 1f / 60f, 1.25f, pos, rot, thrown);
            Assert.AreEqual(DiceClip.HeadBytes + 4 * DiceClip.DieFrameBytes, bytes.Count);
            var lib = DiceLibrary.Parse(DiceLibrary.Build(new[] { bytes.ToArray() }));
            Assert.AreEqual(1, lib.Total);
            Assert.AreEqual(1, lib.Count(2));
            var c = lib.Clip(2, 0);
            Assert.AreEqual(2, c.Frames);
            Assert.AreEqual(1.25f, c.SettleSec);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(thrown[i], c.Thrown[i]);
                if (!thrown[i]) continue;
                Assert.AreEqual(pos[i].X, c.Pos[i].X, 0.001f);
                Assert.AreEqual(pos[i].Y, c.Pos[i].Y, 0.001f);
                Assert.AreEqual(pos[i].Z, c.Pos[i].Z, 0.001f);
                Assert.IsTrue(Q4.Same(rot[i], c.Rot[i], 1e-5f), $"회전 {i}");
            }
            // 아직 안 던진 주사위는 프레임 사이 뒤쪽 절반에서 나타난다
            Assert.IsFalse(c.Sample(1, 0.2f / 60f, out _, out _));
            Assert.IsTrue(c.Sample(1, 0.7f / 60f, out _, out _));
            Assert.Throws<FormatException>(() => DiceLibrary.Parse(new byte[] { 1, 2, 3, 4, 0, 0 }));
        }

        [Test]
        public void M4_섞기주머니는한바퀴에한벌씩이고연달아같은벌이없다()
        {
            var counts = new[] { 0, 12, 24, 24, 24, 24, 12, 12, 12, 12 };
            var bag = new DiceBag(new Pcg32(7), counts);
            int prev = -1, mirrors = 0;
            for (int lap = 0; lap < 40; lap++)
            {
                var seen = new HashSet<int>();
                for (int i = 0; i < 24; i++)
                {
                    var pick = bag.Next(3);
                    Assert.AreEqual(3, pick.Dice);
                    Assert.IsTrue(seen.Add(pick.Clip), "한 바퀴 안에서 같은 벌");
                    Assert.AreNotEqual(prev, pick.Clip, "연달아 같은 벌");
                    prev = pick.Clip;
                    if (pick.Mirror) mirrors++;
                }
                Assert.AreEqual(24, seen.Count);
            }
            Assert.That(mirrors, Is.InRange(400, 560), "앞뒤 뒤집기는 반반");
            // 주사위 수마다 주머니가 따로다 · 같은 씨앗이면 같은 순서
            var a = new DiceBag(new Pcg32(99), counts);
            var b = new DiceBag(new Pcg32(99), counts);
            foreach (int n in new[] { 2, 5, 2, 9, 1, 2, 5 })
            {
                var x = a.Next(n);
                var y = b.Next(n);
                Assert.AreEqual((x.Dice, x.Clip, x.Mirror), (y.Dice, y.Clip, y.Mirror));
                Assert.Less(x.Clip, counts[n]);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => bag.Next(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DiceBag(new Pcg32(1), new[] { 0, 12 }).Next(2));
        }

        [Test]
        public void M4_멈춘주사위는왼쪽부터엔진결과를보인다()
        {
            var rng = new Pcg32(11);
            var scratch = new DiePose[DiceGeo.MaxDice];
            for (int round = 0; round < 200; round++)
            {
                int n = 1 + round % DiceGeo.MaxDice;
                var clip = Synth(n, rng);
                var faces = Faces(n, rng);
                var scene = new DiceRollScene(clip, round % 2 == 1, faces, round) { ThrowMs = 400 };
                for (int k = 1; k < n; k++) Assert.LessOrEqual(scene.RestPos(scene.Order[k - 1]).X, scene.RestPos(scene.Order[k]).X);
                Assert.AreEqual(AttackDieFace.None, scene.ShownFace(0, 399, scratch), "던지기 전에는 안 보인다");
                for (int k = 0; k < n; k++) Assert.AreEqual(faces[k], scene.ShownFace(k, 400 + clip.LengthMs, scratch), $"{round}번 {k}번째");
                // 그림은 될 수 있는 만큼 바로 선다: 위 면 그림의 위쪽이 화면 위쪽 반(±45°) 안
                scene.Pose(5000, scratch, out var glitch);
                Assert.IsFalse(glitch.On);
                for (int i = 0; i < n; i++) Assert.Greater(DiceCube.Upright(scratch[i].Rot), 0.70f);
            }
        }

        [Test]
        public void M4_면바꾸기는제자리에서넘어가새면이위로온다()
        {
            var rng = new Pcg32(5);
            var scratch = new DiePose[DiceGeo.MaxDice];
            var cases = new[] { (R, S, 90f), (C, R, 90f), (S, R, 90f), (C, S, 180f), (R, AttackDieFace.None, 0f) };
            for (int round = 0; round < 100; round++)
            {
                var (from, to, _) = cases[round % cases.Length];
                var clip = Synth(4, rng);
                var faces = new[] { R, from, C, from };
                var scene = new DiceRollScene(clip, round % 2 == 0, faces, round);
                scene.AddFlip(3000, 1, to);
                scene.AddFlip(3000, 3, to);
                int die = scene.Order[1];
                scene.Pose(2999, scratch, out _);
                var before = scratch[die];
                Assert.AreEqual(0f, before.Dim);
                for (int k = 0; k < 4; k++) Assert.AreEqual(faces[k], scene.ShownFace(k, 2999, scratch));
                scene.Pose(3000 + DiceRollScene.FlipMs / 2, scratch, out _);
                if (to != AttackDieFace.None) Assert.AreEqual(before.Pos.Y + 0.6f, scratch[die].Pos.Y, 0.001f, "가운데에서 0.6칸 뜬다");
                else Assert.AreEqual(before.Pos.Y, scratch[die].Pos.Y, 0.001f);
                scene.Pose(3000 + DiceRollScene.FlipMs, scratch, out _);
                var after = scratch[die];
                Assert.AreEqual(1f, after.Dim, "회색");
                Assert.AreEqual(before.Pos.X, after.Pos.X, 1e-4f);
                Assert.AreEqual(before.Pos.Y, after.Pos.Y, 1e-4f);
                Assert.AreEqual(before.Pos.Z, after.Pos.Z, 1e-4f);
                var want = to == AttackDieFace.None ? from : to;
                Assert.AreEqual(want, scene.ShownFace(1, 9000, scratch));
                Assert.AreEqual(want, scene.ShownFace(3, 9000, scratch));
                Assert.AreEqual(R, scene.ShownFace(0, 9000, scratch), "다른 주사위는 그대로");
                Assert.AreEqual(C, scene.ShownFace(2, 9000, scratch));
                scene.Pose(9000, scratch, out _);
                Assert.AreEqual(0f, scratch[scene.Order[0]].Dim);
            }
        }

        [Test]
        public void M4_면바꾸기는될때는90도로넘어간다()
        {
            // 일반 → 심볼: 붙일 때 심볼 면을 옆에 두어 90°면 된다(멈춘 자세에서 넘긴 자세까지의 각)
            var rng = new Pcg32(21);
            var scratch = new DiePose[DiceGeo.MaxDice];
            for (int round = 0; round < 50; round++)
            {
                var scene = new DiceRollScene(Synth(2, rng), false, new[] { R, C }, round);
                scene.AddFlip(3000, 0, S);
                scene.AddFlip(3000, 1, S);
                scene.Pose(2999, scratch, out _);
                var a0 = scratch[scene.Order[0]].Rot;
                var b0 = scratch[scene.Order[1]].Rot;
                scene.Pose(4000, scratch, out _);
                Assert.AreEqual(90.0, Angle(a0, scratch[scene.Order[0]].Rot), 0.5);
                Assert.AreEqual(180.0, Angle(b0, scratch[scene.Order[1]].Rot), 0.5, "급소 → 심볼은 맞은편이라 180°");
            }
        }

        static double Angle(Q4 a, Q4 b)
        {
            double d = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W);
            return 2 * Math.Acos(Math.Min(1.0, d)) * 180 / Math.PI;
        }

        [Test]
        public void M9_재굴림글리치는제자리에서떠서되감기고몸이일그러진다_P38()
        {
            var rng = new Pcg32(38);
            var scratch = new DiePose[DiceGeo.MaxDice];
            for (int round = 0; round < 20; round++)
            {
                var clip = Synth(4, rng);
                var scene = new DiceRollScene(clip, round % 2 == 1, new[] { R, S, C, R }, round);
                scene.AddReroll(5000, 1, C);
                int die = scene.Order[1], other = scene.Order[0];
                var rest = scene.RestPos(die);
                scene.Pose(4999, scratch, out _);
                var restRot = scratch[die].Rot;
                Assert.AreEqual((0f, 0f, 0f), (scratch[die].Stretch.X, scratch[die].Stretch.Y, scratch[die].Stretch.Z), "글리치 전에는 그대로");

                // 들어오는 200ms(결정 190 — 옛 80의 2.5배): 떠오르기만 한다(자세 · 몸은 그대로)
                scene.Pose(5000 + 100, scratch, out _);
                Assert.AreEqual(rest.Y + DiceRollScene.GlitchLift * 0.5f, scratch[die].Pos.Y, 1e-4f);
                Assert.AreEqual(0f, scratch[die].Stretch.X);

                // 지직(200 ~ 1200): 제자리(가로 · 깊이)에서 떠 있고, 몸의 한 축이 늘고 나머지 둘이 준다 · 75ms마다 달라진다 · 다른 주사위는 그대로
                var seen = new HashSet<(float, float, float)>();
                bool turned = false;
                for (int u = 200; u < 1200; u += 10)
                {
                    scene.Pose(5000 + u, scratch, out var g);
                    var p = scratch[die];
                    Assert.IsTrue(g.On);
                    Assert.AreEqual(rest.Y + DiceRollScene.GlitchLift, p.Pos.Y, 1e-4f, "떠 있다");
                    Assert.That(Math.Abs(p.Pos.X - rest.X), Is.LessThanOrEqualTo(0.0401f), "제자리(결정 48 ②)");
                    Assert.That(Math.Abs(p.Pos.Z - rest.Z), Is.LessThanOrEqualTo(0.0301f));
                    float[] s = { p.Stretch.X, p.Stretch.Y, p.Stretch.Z };
                    Assert.AreEqual(1, s.Count(v => v > 0f), "한 축만 늘어난다");
                    Assert.AreEqual(2, s.Count(v => v < 0f), "나머지 둘은 준다");
                    Assert.That(s.Max(), Is.InRange(DiceRollScene.GlitchStretch / 4f - 1e-4f, DiceRollScene.GlitchStretch + 1e-4f));
                    Assert.AreEqual(-s.Max() / 2, s.Min(), 1e-5f);
                    seen.Add((p.Stretch.X, p.Stretch.Y, p.Stretch.Z));
                    if (Math.Abs(Q4Dot(p.Rot, restRot)) < 0.999f) turned = true;
                    Assert.AreEqual(0f, scratch[other].Stretch.X, "고르지 않은 주사위는 그대로");
                    scene.Pose(5000 + Math.Max(200, u / 75 * 75), scratch, out _);
                    Assert.AreEqual((p.Stretch.X, p.Stretch.Y, p.Stretch.Z), (scratch[die].Stretch.X, scratch[die].Stretch.Y, scratch[die].Stretch.Z), "75ms 칸 안에서는 같은 꼴");
                }
                Assert.That(seen.Count, Is.GreaterThanOrEqualTo(4), "몸의 꼴이 여러 번 바뀐다");
                Assert.IsTrue(turned, "멈춘 자세에서 벗어난다(되감기 · 면 깜빡임)");

                // 고정(1200): 제자리 · 제 꼴 · 새 면
                scene.Pose(5000 + 1200, scratch, out _);
                Assert.AreEqual(rest.Y, scratch[die].Pos.Y, 1e-4f, "내려앉았다");
                Assert.AreEqual((0f, 0f, 0f), (scratch[die].Stretch.X, scratch[die].Stretch.Y, scratch[die].Stretch.Z));
                Assert.AreEqual(C, scene.ShownFace(1, 5000 + 1200, scratch));
            }
        }

        static float Q4Dot(Q4 a, Q4 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        [Test]
        public void M4_재굴림은제자리글리치뒤새면으로고정된다()
        {
            var rng = new Pcg32(3);
            var scratch = new DiePose[DiceGeo.MaxDice];
            for (int round = 0; round < 60; round++)
            {
                var to = new[] { S, R, C }[round % 3];
                var clip = Synth(3, rng);
                var scene = new DiceRollScene(clip, round % 2 == 0, new[] { R, R, C }, round);
                if (round % 4 == 0) scene.AddFlip(2000, 2, S);   // 넘어간 주사위를 다시 굴려도 넘어간 자세에서 맞춘다
                scene.AddReroll(5000, 2, to);
                int die = scene.Order[2];
                var rest = scene.RestPos(die);

                scene.Pose(4999, scratch, out var g);
                Assert.IsFalse(g.On);
                // 멈춘 자리 = 그때 그려지는 자리(앞뒤로 뒤집은 재생도) — 글리치 가운데·고리·꼬리표가 여기에 놓인다
                var shown = scratch[die].Pos;
                Assert.AreEqual((shown.X, shown.Y, shown.Z), (rest.X, rest.Y, rest.Z));
                var startFace = round % 4 == 0 ? S : C;
                Assert.AreEqual(startFace, scene.ShownFace(2, 5000 + 199, scratch), "들어오는 200ms 동안은 면 그대로");
                scene.Pose(5000 + 199, scratch, out g);
                Assert.IsTrue(g.On);
                Assert.AreEqual(die, g.Die);
                Assert.AreEqual((shown.X, shown.Y, shown.Z), (g.Center.X, g.Center.Y, g.Center.Z), "글리치 가운데 = 멈춘 주사위 자리");
                Assert.AreEqual(0f, g.Ghost);

                scene.Pose(5000 + 750, scratch, out g);
                Assert.IsTrue(g.On);
                Assert.AreEqual(0.24f, g.Amt);
                Assert.AreEqual(1f, g.Clock);
                Assert.That(Math.Abs(scratch[die].Pos.X - rest.X), Is.LessThanOrEqualTo(0.0401f), "제자리(떨림 ±0.04칸)");
                Assert.That(Math.Abs(scratch[die].Pos.Z - rest.Z), Is.LessThanOrEqualTo(0.0301f));
                Assert.AreEqual(0f, scratch[die].Flash);

                scene.Pose(5000 + 1200, scratch, out g);
                Assert.IsTrue(g.On, "1500까지 고리가 남는다");
                Assert.AreEqual(1f, scratch[die].Flash, "고정 + 번쩍");
                Assert.AreEqual(rest.X, scratch[die].Pos.X, 1e-4f);
                Assert.AreEqual(to, scene.ShownFace(2, 5000 + 1200, scratch));
                scene.Pose(5000 + DiceRollScene.GlitchMs, scratch, out g);
                Assert.IsFalse(g.On);
                Assert.AreEqual(0f, scratch[die].Flash);
                Assert.AreEqual(to, scene.ShownFace(2, 9000, scratch));
                Assert.AreEqual(R, scene.ShownFace(0, 9000, scratch));
                Assert.AreEqual(R, scene.ShownFace(1, 9000, scratch));
                Assert.Greater(DiceCube.Upright(scratch[die].Rot), 0.70f);
            }
        }

        [Test]
        public void M4_같은시각은앞호출과무관하게같은자세다()
        {
            var rng = new Pcg32(8);
            var clip = Synth(5, rng, 40);
            DiceRollScene Make()
            {
                var s = new DiceRollScene(clip, true, new[] { R, S, C, R, R }, 4) { ThrowMs = 400 };
                s.AddFlip(1500, 1, R);
                s.AddReroll(2400, 3, C);
                return s;
            }
            var once = Make();
            var stepped = Make();
            var a = new DiePose[5];
            var b = new DiePose[5];
            foreach (int ms in new[] { 0, 399, 400, 533, 700, 1100, 1640, 1780, 2400, 2530, 2890, 3000, 9000 })
            {
                once.Pose(ms, a, out var ga);
                for (int t = 0; t <= ms; t += 16) stepped.Pose(t, b, out _);
                stepped.Pose(9999, b, out _);
                stepped.Pose(ms, b, out var gb);
                Assert.AreEqual(ga.On, gb.On);
                Assert.AreEqual(ga.Seed, gb.Seed);
                for (int i = 0; i < 5; i++)
                {
                    Assert.AreEqual(a[i].Shown, b[i].Shown);
                    Assert.AreEqual((a[i].Pos.X, a[i].Pos.Y, a[i].Pos.Z), (b[i].Pos.X, b[i].Pos.Y, b[i].Pos.Z));
                    Assert.AreEqual((a[i].Rot.X, a[i].Rot.Y, a[i].Rot.Z, a[i].Rot.W), (b[i].Rot.X, b[i].Rot.Y, b[i].Rot.Z, b[i].Rot.W));
                    Assert.AreEqual((a[i].Dim, a[i].Flash), (b[i].Dim, b[i].Flash));
                }
            }
        }

        static List<FxEntry> PlanAttack(AttackDieFace[] faces, params GameEvent[] extra)
        {
            var events = new List<GameEvent>
            {
                new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.BattleAction, Auto = AutoKind.Human },
                new DiceRolled { Seq = 1, Seat = 0, PokemonId = 2, Faces = faces },
            };
            foreach (var e in extra) { e.Seq = 1; e.Seat = 0; events.Add(e); }
            events.Add(new DamageBreakdown { Seq = 1, Seat = 0, TargetId = 1, Final = 3 });
            return new FxPlanner(0).Plan(events);
        }

        [Test]
        public void M4_굴림단계시간표는던짐400이고면바꾸기가멈춤과세기사이에낀다()
        {
            var rng = new Pcg32(2);
            var faces = new[] { R, R, C };
            var clip = Synth(3, rng);

            // 면 바꾸기 없음: 던짐 = 단계 시작 + 400, 세기 = + 1600 + 250, 세기 끝 = 굴림 단계 끝
            var plain = PlanAttack(faces)[0];
            var roll = plain.Step(560);
            var scene = DiceRollScene.Plan(plain, clip, false);
            Assert.AreEqual(roll.StartMs + 400, scene.ThrowMs);
            Assert.AreEqual(roll.StartMs + 400 + 1600 + 250, scene.CountMs);
            Assert.AreEqual(roll.EndMs, scene.CountMs + 130 * 3);

            // 졸음(상태이상 → 63행 단계)·아이템(57행 단계) 어느 쪽이든 굴림 바로 뒤 단계면 멈춤(위쪽 끝) 뒤에 낀다: + 450에 넘김, 세기는 730 뒤
            foreach (var kind in new[] { SourceKind.Status, SourceKind.Item })
            {
                var change = new DiceFacesChanged { Source = new SourceRef(kind, 1), Changes = new[] { new FaceChange { Index = 1, Before = R, After = S } }, SrcBefore = new[] { 0, 2, 1 }, SrcAfter = new[] { 1, 1, 1 } };
                var entry = PlanAttack(faces, change)[0];
                roll = entry.Step(560);
                var flipStep = entry.Steps[entry.Steps.IndexOf(roll) + 1];
                Assert.AreEqual(kind == SourceKind.Status ? 630 : 570, flipStep.Row);
                scene = DiceRollScene.Plan(entry, clip, false);
                int stop = roll.StartMs + 400 + 1600;
                var scratch = new DiePose[3];
                Assert.AreEqual(R, scene.ShownFace(1, stop + 449, scratch));
                scene.Pose(stop + 450 + 140, scratch, out _);
                Assert.Greater(scratch[scene.Order[1]].Pos.Y, 1.0f, "넘어가는 중(떠 있다)");
                Assert.AreEqual(S, scene.ShownFace(1, stop + 730, scratch));
                Assert.AreEqual(stop + 730 + 250, scene.CountMs);
                Assert.LessOrEqual(scene.CountMs + 130 * 3, flipStep.EndMs, "세기가 두 단계 길이 안에서 끝난다");
            }

            // 한 굴림에 면 바꾸기가 둘(졸린 공격자 일반 → 심볼 + 졸린 방어자 일반 → 급소 — 둘 다 굴린 뒤 창의 상태이상 효과):
            // 63행은 한 항목에 단계 하나(900)라 한 박자에 함께 넘기고, 57행은 이벤트마다 단계가 이어져 한 박자씩 넘긴다. 어느 쪽이든 세기는 면 바꾸기 단계 안에서 끝난다
            foreach (var kind in new[] { SourceKind.Status, SourceKind.Item })
            {
                var first = new DiceFacesChanged { Source = new SourceRef(kind, 1), Changes = new[] { new FaceChange { Index = 0, Before = R, After = S } }, SrcBefore = new[] { 0, 2, 1 }, SrcAfter = new[] { 1, 1, 1 } };
                var second = new DiceFacesChanged { Source = new SourceRef(kind, 2), Changes = new[] { new FaceChange { Index = 1, Before = R, After = C } }, SrcBefore = new[] { 1, 1, 1 }, SrcAfter = new[] { 1, 0, 2 } };
                var entry = PlanAttack(faces, first, second)[0];
                roll = entry.Step(560);
                int beats = kind == SourceKind.Status ? 1 : 2;
                var lastFlip = entry.Steps[entry.Steps.IndexOf(roll) + beats];
                Assert.AreEqual(kind == SourceKind.Status ? 630 : 570, lastFlip.Row);
                Assert.AreEqual(611, entry.Steps[entry.Steps.IndexOf(roll) + beats + 1].Row, "면 바꾸기 단계 뒤가 바로 피해");
                scene = DiceRollScene.Plan(entry, clip, false);
                int stop = roll.StartMs + 400 + 1600, secondAt = stop + 730 * (beats - 1) + 450;
                var scratch = new DiePose[3];
                Assert.AreEqual(R, scene.ShownFace(0, stop + 449, scratch));
                Assert.AreEqual(S, scene.ShownFace(0, stop + 730, scratch));
                Assert.AreEqual(R, scene.ShownFace(1, secondAt - 1, scratch));
                scene.Pose(secondAt + 140, scratch, out _);
                Assert.Greater(scratch[scene.Order[1]].Pos.Y, 1.0f, "둘째도 넘어가는 중");
                Assert.AreEqual(C, scene.ShownFace(1, secondAt + DiceRollScene.FlipMs, scratch));
                Assert.AreEqual(stop + 730 * beats + 250, scene.CountMs);
                Assert.LessOrEqual(scene.CountMs + 130 * 3, lastFlip.EndMs, $"{kind} 둘: 세기가 면 바꾸기 단계 안에서 끝난다(피해 단계와 겹치지 않는다)");
            }

            // 굴림 단계가 없는 항목
            Assert.IsNull(DiceRollScene.Plan(new FxEntry(), clip, false));
            // Host 조정 값을 따른다
            Tunables.Shared.Set(PlayTiming.DiceThrowMs.Key, 100);
            Assert.AreEqual(plain.Step(560).StartMs + 100, DiceRollScene.Plan(plain, clip, false).ThrowMs);
        }

        // ---------- 추적하는 녹화본 묶음(Assets/Sinnoh/Data/Dice/rolls.bytes — Sinnoh.Editor.DiceBake가 굽는다) ----------

        static DiceLibrary LoadLibrary() => DiceLibrary.Parse(File.ReadAllBytes(Path.Combine(TestData.RepoRoot(), "Assets/Sinnoh/Data/Dice/rolls.bytes")));

        [Test]
        public void M4_녹화본묶음은주사위수별벌수대로이고판안에서멈춘다()
        {
            var lib = LoadLibrary();
            var want = new[] { 0, 12, 24, 24, 24, 24, 12, 12, 12, 12 };
            Assert.AreEqual(want.Sum(), lib.Total);
            for (int n = 1; n <= 9; n++)
            {
                Assert.AreEqual(want[n], lib.Count(n), $"주사위 {n}개");
                for (int c = 0; c < lib.Count(n); c++)
                {
                    var clip = lib.Clip(n, c);
                    string name = $"{n}개 {c}번";
                    Assert.AreEqual(1f / 60f, clip.FrameSec, 1e-6f, name);
                    Assert.That(clip.SettleSec, Is.InRange(0.8f, 1.6f), name + " 길이");
                    Assert.LessOrEqual(clip.LengthMs, 1600 + 200, name + " 멈춘 뒤 0.15초 남짓까지만 적는다");
                    for (int i = 0; i < n; i++)
                    {
                        var p = clip.RestPos(i);
                        // 벽에서 0.5칸 안쪽(16비트로 적은 자리라 0.002 여유)
                        Assert.That(p.X, Is.InRange(0.498f, DiceGeo.Width - 0.498f), name + " 멈춘 가로 자리");
                        Assert.That(p.Z, Is.InRange(0.498f, DiceGeo.Depth - 0.498f), name + " 멈춘 깊이 자리");
                        Assert.That(p.Y, Is.InRange(0.49f, 0.56f), name + " 바닥에 멈춘다(겹쳐 얹히지 않음)");
                        Assert.Greater((clip.RestRot(i) * DiceCube.Normal[DiceCube.TopFace(clip.RestRot(i))]).Y, 0.999f, name + " 기울지 않고 멈춘다");
                        // 굴리는 동안: 왼쪽 벽 밖에서 들어오고, 나머지 벽은 넘지 않는다(주사위 가운데 기준, 모서리로 서면 벽에 0.5보다 가까워진다)
                        for (int f = 0; f < clip.Frames; f++)
                        {
                            if (!clip.Thrown[f * n + i]) continue;
                            var q = clip.Pos[f * n + i];
                            Assert.That(q.X, Is.InRange(-0.7f, DiceGeo.Width - 0.45f), name + " 가로");
                            Assert.That(q.Z, Is.InRange(0.45f, DiceGeo.Depth - 0.45f), name + " 깊이");
                            Assert.That(q.Y, Is.InRange(0.45f, 8f), name + " 높이");
                            // 높이 튀어도 판 그림 위 끝(3.6875칸)에서 0.7칸 아래까지만 — 앞뒤로 뒤집어도
                            Assert.LessOrEqual(Math.Max(q.Z, DiceGeo.Depth - q.Z) * (float)Math.Sin(DiceGeo.PitchDeg * Math.PI / 180) + q.Y * (float)Math.Cos(DiceGeo.PitchDeg * Math.PI / 180), 3.6875f - 0.7f + 0.002f, name + " 그림 위로 나가지 않는다");
                        }
                    }
                }
            }
        }

        [Test]
        public void M4_뒤집은재생은구르는내내원본의앞뒤거울이다()
        {
            // 앞뒤 뒤집기 = 판 깊이 방향 거울: 자리는 z → 깊이 − z, 정육면체는 면 법선 여섯이 z 거울(어느 면이 어디 붙었는지는 면 붙이기가 따로 정한다)
            var lib = LoadLibrary();
            var a = new DiePose[DiceGeo.MaxDice];
            var b = new DiePose[DiceGeo.MaxDice];
            int moving = 0;
            foreach (int n in new[] { 1, 4, 9 })
                for (int c = 0; c < lib.Count(n); c += 5)
                {
                    var clip = lib.Clip(n, c);
                    var faces = Faces(n, new Pcg32((ulong)(n * 100 + c)));
                    var plain = new DiceRollScene(clip, false, faces, c) { ThrowMs = 400 };
                    var flipped = new DiceRollScene(clip, true, faces, c) { ThrowMs = 400 };
                    foreach (int ms in new[] { 400, 480, 600, 750, 900, 1100, 1400, 400 + clip.LengthMs, 9000 })
                    {
                        plain.Pose(ms, a, out _);
                        flipped.Pose(ms, b, out _);
                        for (int die = 0; die < n; die++)
                        {
                            string name = $"{n}개 {c}번 주사위 {die} {ms}ms";
                            Assert.AreEqual(a[die].Shown, b[die].Shown, name);
                            if (!a[die].Shown) continue;
                            Assert.AreEqual(a[die].Pos.X, b[die].Pos.X, 1e-4f, name);
                            Assert.AreEqual(a[die].Pos.Y, b[die].Pos.Y, 1e-4f, name);
                            Assert.AreEqual(DiceGeo.Depth - a[die].Pos.Z, b[die].Pos.Z, 1e-4f, name + " 깊이 거울");
                            if (ms < 1100 && Math.Abs((a[die].Rot * DiceCube.Normal[DiceCube.TopFace(a[die].Rot)]).Y) < 0.95f) moving++;
                            foreach (var normal in DiceCube.Normal)
                            {
                                var want = a[die].Rot * normal;
                                float best = -2;
                                foreach (var other in DiceCube.Normal)
                                {
                                    var got = b[die].Rot * other;
                                    best = Math.Max(best, want.X * got.X + want.Y * got.Y - want.Z * got.Z);
                                }
                                Assert.Greater(best, 0.9995f, name + " 면 법선 거울");
                            }
                        }
                    }
                    for (int die = 0; die < n; die++)
                    {
                        Assert.AreEqual(b[die].Pos.X, flipped.RestPos(die).X, 1e-4f);
                        Assert.AreEqual(b[die].Pos.Z, flipped.RestPos(die).Z, 1e-4f, "멈춘 자리(RestPos) = 뒤집은 자리");
                    }
                }
            Assert.Greater(moving, 100, "기울어 구르는 자세를 견줬다");
        }

        [Test]
        public void M4_녹화본묶음의모든벌은뒤집어도엔진결과로멈춘다()
        {
            var lib = LoadLibrary();
            var rng = new Pcg32(2026);
            var scratch = new DiePose[DiceGeo.MaxDice];
            for (int n = 1; n <= 9; n++)
                for (int c = 0; c < lib.Count(n); c++)
                    foreach (bool mirror in new[] { false, true })
                    {
                        var clip = lib.Clip(n, c);
                        var faces = Faces(n, rng);
                        var scene = new DiceRollScene(clip, mirror, faces, c) { ThrowMs = 400 };
                        int k = rng.Next(n);
                        var to = faces[k] == S ? R : S;
                        scene.AddFlip(2450, k, to);
                        for (int i = 0; i < n; i++) Assert.AreEqual(faces[i], scene.ShownFace(i, 400 + clip.LengthMs, scratch), $"{n}개 {c}번 {i}번째");
                        Assert.AreEqual(to, scene.ShownFace(k, 2450 + DiceRollScene.FlipMs, scratch));
                        scene.Pose(9000, scratch, out _);
                        for (int i = 0; i < n; i++)
                        {
                            Assert.Greater(DiceCube.Upright(scratch[i].Rot), 0.70f);
                            Assert.That(scratch[i].Pos.Z, Is.InRange(0.498f, DiceGeo.Depth - 0.498f), "뒤집어도 판 안");
                        }
                    }
        }
    }
}
