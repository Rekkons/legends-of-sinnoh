using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Rekkon.UiKit.Tests
{
    public class TweenTests
    {
        /// <summary>받은 ms를 적어 두는 부품. 값 = 0 → 100을 OutQuad로(길이 400).</summary>
        sealed class Probe : ISeekable
        {
            public readonly List<int> Seeks = new List<int>();
            public float Value;
            public void Seek(int ms)
            {
                Seeks.Add(ms);
                Value = Tween.At(ms, 0, 400, 0f, 100f, Ease.OutQuad);
            }
        }

        sealed class Countdown : ITweenDriver
        {
            public int LeftMs;
            public bool Advance(int dtMs)
            {
                LeftMs -= dtMs;
                return LeftMs > 0;
            }
        }

        [Test]
        public void M4_트윈진행은구간밖에서잘린다()
        {
            Assert.AreEqual(0f, Tween.Progress(99, 100, 200));
            Assert.AreEqual(0f, Tween.Progress(100, 100, 200));
            Assert.AreEqual(0.5f, Tween.Progress(200, 100, 200));
            Assert.AreEqual(1f, Tween.Progress(300, 100, 200));
            Assert.AreEqual(1f, Tween.Progress(999, 100, 200));
            Assert.AreEqual(0f, Tween.Progress(99, 100, 0), "길이 0: 시작 전");
            Assert.AreEqual(1f, Tween.Progress(100, 100, 0), "길이 0: 시작하자마자 끝");
        }

        [Test]
        public void M4_이징은양끝이0과1이고단조롭다()
        {
            foreach (Ease ease in Enum.GetValues(typeof(Ease)))
            {
                Assert.AreEqual(0f, Tween.Apply(ease, 0f), ease.ToString());
                Assert.AreEqual(1f, Tween.Apply(ease, 1f), ease.ToString());
                Assert.AreEqual(0f, Tween.Apply(ease, -1f), ease + " 아래로 넘침");
                Assert.AreEqual(1f, Tween.Apply(ease, 2f), ease + " 위로 넘침");
                float prev = 0f;
                for (int i = 1; i <= 20; i++)
                {
                    float v = Tween.Apply(ease, i / 20f);
                    Assert.GreaterOrEqual(v, prev, ease + " " + i);
                    prev = v;
                }
            }
            Assert.AreEqual(0.25f, Tween.Apply(Ease.InQuad, 0.5f), 1e-6);
            Assert.AreEqual(0.75f, Tween.Apply(Ease.OutQuad, 0.5f), 1e-6);
            Assert.AreEqual(0.5f, Tween.Apply(Ease.InOutQuad, 0.5f), 1e-6);
            Assert.AreEqual(0.875f, Tween.Apply(Ease.OutCubic, 0.5f), 1e-6);
            Assert.AreEqual(15f, Tween.At(150, 100, 100, 10f, 20f), 1e-6);
        }

        [Test]
        public void M4_같은시각으로Seek하면같은값()
        {
            var a = new Probe();
            a.Seek(130);
            float first = a.Value;
            a.Seek(390);
            a.Seek(130);
            Assert.AreEqual(first, a.Value, "앞 호출에 기대지 않는다");
        }

        [Test]
        public void M4_실행기는나눠보내도한번에보내도같은자리에온다()
        {
            // 250ms까지: 한 번에 / 16ms씩 / 1ms씩 — 마지막 Seek의 ms와 값이 같다
            float[] values = new float[3];
            int[] steps = { 250, 16, 1 };
            for (int k = 0; k < steps.Length; k++)
            {
                var runner = new TweenRunner();
                var p = new Probe();
                runner.Play(p, 400);
                int t = 0;
                while (t < 250)
                {
                    int dt = Math.Min(steps[k], 250 - t);
                    runner.Advance(dt);
                    t += dt;
                }
                Assert.AreEqual(250, p.Seeks[p.Seeks.Count - 1], "걸음 " + steps[k]);
                values[k] = p.Value;
            }
            Assert.AreEqual(values[0], values[1]);
            Assert.AreEqual(values[0], values[2]);
        }

        [Test]
        public void M4_실행기바쁨은등록하면1끝나면0()
        {
            var runner = new TweenRunner();
            Assert.AreEqual(0, runner.Busy);
            var p = new Probe();
            runner.Play(p, 400, 100);
            Assert.AreEqual(1, runner.Busy);
            CollectionAssert.AreEqual(new[] { 0 }, p.Seeks, "등록할 때 처음 모양");
            runner.Advance(60);
            CollectionAssert.AreEqual(new[] { 0 }, p.Seeks, "지연 중에는 부르지 않는다");
            runner.Advance(140);
            Assert.AreEqual(100, p.Seeks[p.Seeks.Count - 1], "지연 100 뒤 100");
            runner.Advance(1000);
            Assert.AreEqual(400, p.Seeks[p.Seeks.Count - 1], "끝은 길이로 한 번");
            Assert.AreEqual(100f, p.Value);
            Assert.AreEqual(0, runner.Busy);
            Assert.AreEqual(0, runner.Count);
            int calls = p.Seeks.Count;
            runner.Advance(16);
            Assert.AreEqual(calls, p.Seeks.Count, "유휴 때 하는 일 0");
        }

        [Test]
        public void M4_실행기늘도는장식은바쁨0이고멈출때까지돈다()
        {
            var runner = new TweenRunner();
            var breath = new Probe();
            int id = runner.Ambient(breath);
            Assert.AreEqual(0, runner.Busy, "장식은 바쁨으로 세지 않는다");
            Assert.AreEqual(1, runner.Count);
            runner.Advance(5000);
            runner.Advance(5000);
            Assert.AreEqual(10000, breath.Seeks[breath.Seeks.Count - 1]);
            Assert.IsTrue(runner.Stop(id));
            Assert.IsFalse(runner.Stop(id), "이미 빠짐");
            Assert.AreEqual(0, runner.Count);
        }

        [Test]
        public void M4_실행기는끝을스스로아는것을끝날때까지바쁨으로센다()
        {
            var runner = new TweenRunner();
            var queue = new Countdown { LeftMs = 300 };
            runner.Drive(queue);
            var p = new Probe();
            int play = runner.Play(p, 1000);
            Assert.AreEqual(2, runner.Busy);
            runner.Advance(200);
            Assert.AreEqual(2, runner.Busy);
            runner.Advance(100);
            Assert.AreEqual(1, runner.Busy, "false를 돌려주면 빠진다");
            Assert.IsTrue(runner.Stop(play));
            Assert.AreEqual(0, runner.Busy, "멈추면 바쁨에서 빠진다");
            Assert.AreEqual(300, p.Seeks[p.Seeks.Count - 1], "멈출 때 끝 모양으로 보내지 않는다");
        }

        /// <summary>Seek 안에서 실행기에 다른 부품을 등록하고 자신을 멈추는 부품.</summary>
        sealed class Chain : ISeekable
        {
            public TweenRunner Runner;
            public Probe Next = new Probe();
            public int Id, Calls;
            public void Seek(int ms)
            {
                Calls++;
                if (ms < 50) return;
                Runner.Play(Next, 400);
                Runner.Stop(Id);
            }
        }

        [Test]
        public void M4_실행기는도는중에등록하고멈춰도된다()
        {
            var runner = new TweenRunner();
            var chain = new Chain { Runner = runner };
            chain.Id = runner.Play(chain, 1000);
            runner.Advance(60);
            Assert.AreEqual(1, runner.Busy, "자신은 빠지고 새 부품이 들어옴");
            CollectionAssert.AreEqual(new[] { 0 }, chain.Next.Seeks, "새로 등록된 것은 다음 호출부터 돈다");
            int calls = chain.Calls;
            runner.Advance(100);
            Assert.AreEqual(calls, chain.Calls);
            Assert.AreEqual(100, chain.Next.Seeks[chain.Next.Seeks.Count - 1]);
        }

        /// <summary>자기 Advance 안에서 자신을 멈추고 false(끝)를 돌려주는 드라이버.</summary>
        sealed class Quitter : ITweenDriver
        {
            public TweenRunner Runner;
            public int Id;
            public bool Stopped;
            public bool Advance(int dtMs)
            {
                Stopped = Runner.Stop(Id);
                return false;
            }
        }

        /// <summary>마지막 Seek(길이) 안에서 자신을 멈추는 부품.</summary>
        sealed class EndStopper : ISeekable
        {
            public TweenRunner Runner;
            public int Id, Length;
            public bool Stopped;
            public void Seek(int ms)
            {
                if (ms >= Length) Stopped = Runner.Stop(Id);
            }
        }

        [Test]
        public void M4_실행기는끝나는순간스스로멈춰도바쁨을한번만줄인다()
        {
            // 끝과 Stop이 같은 호출에서 겹쳐도 바쁨은 0이다(음수가 되면 「바쁨 0 = 유휴」 판정이 어긋난다)
            var runner = new TweenRunner();
            var quitter = new Quitter { Runner = runner };
            quitter.Id = runner.Drive(quitter);
            Assert.AreEqual(1, runner.Busy);
            runner.Advance(16);
            Assert.IsTrue(quitter.Stopped);
            Assert.AreEqual(0, runner.Busy, "드라이버: Stop + false");
            Assert.AreEqual(0, runner.Count);

            var end = new EndStopper { Runner = runner, Length = 100 };
            end.Id = runner.Play(end, 100);
            Assert.AreEqual(1, runner.Busy);
            runner.Advance(100);
            Assert.IsTrue(end.Stopped);
            Assert.AreEqual(0, runner.Busy, "부품: 마지막 Seek 안에서 Stop");
            Assert.AreEqual(0, runner.Count);

            runner.Play(new Probe(), 400);
            Assert.AreEqual(1, runner.Busy, "그 뒤 등록은 다시 1부터");
        }
    }
}
