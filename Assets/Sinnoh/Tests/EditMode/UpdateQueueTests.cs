using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>받기 차례 상태 기계(결정 161 4-1 — 순수): 받는 일은 가짜.</summary>
    public sealed class UpdateQueueTests
    {
        sealed class Fake : IUpdateFetch
        {
            public readonly List<UpdateJob> Started = new List<UpdateJob>(), Stopped = new List<UpdateJob>();
            public void Start(UpdateJob job) => Started.Add(job);
            public void Stop(UpdateJob job) => Stopped.Add(job);
        }

        static List<UpdateJob> Jobs(int n)
        {
            var l = new List<UpdateJob>();
            for (int i = 0; i < n; i++) l.Add(new UpdateJob(i.ToString("x64"), 100, "u" + i));
            return l;
        }

        [Test]
        public void M10_받기차례_동시4개_진행합_다받으면끝()
        {
            var fake = new Fake();
            var q = new UpdateQueue(Jobs(6), fake);
            q.Tick(0);
            Assert.That(fake.Started.Count, Is.EqualTo(UpdateQueue.Parallel));
            q.Tick(10);
            Assert.That(fake.Started.Count, Is.EqualTo(4), "동시 4개를 넘지 않는다");
            q.Progress(q.Jobs[0], 40);
            q.Progress(q.Jobs[1], 150);
            Assert.That((q.Total, q.Got), Is.EqualTo((600L, 140L)), "받은 바이트는 크기를 넘겨 세지 않는다");
            q.Done(q.Jobs[0], FetchEnd.Ok, 20);
            q.Tick(20);
            Assert.That(fake.Started.Count, Is.EqualTo(5), "하나가 끝나면 다음을");
            foreach (var j in q.Jobs) if (j.Running) q.Done(j, FetchEnd.Ok, 30);
            q.Tick(30);
            q.Done(q.Jobs[5], FetchEnd.Ok, 40);
            Assert.That(q.State, Is.EqualTo(QueueState.Done));
            Assert.That(q.Got, Is.EqualTo(600));
            Assert.That(new UpdateQueue(new UpdateJob[0], fake).State, Is.EqualTo(QueueState.Done), "받을 것이 없으면 바로 끝");
        }

        [Test]
        public void M10_받기차례_재시도는2_5_15초뒤_세번넘으면실패()
        {
            var fake = new Fake();
            var q = new UpdateQueue(Jobs(1), fake);
            var job = q.Jobs[0];
            long now = 0;
            int[] waits = { 2000, 5000, 15000 };
            for (int i = 0; i < 3; i++)
            {
                q.Tick(now);
                Assert.That(job.Running, Is.True, "시도 " + (i + 1));
                q.Done(job, i == 1 ? FetchEnd.Bad : FetchEnd.Failed, now);
                Assert.That(q.State, Is.EqualTo(QueueState.Running));
                q.Tick(now + waits[i] - 1);
                Assert.That(job.Running, Is.False, "때가 되기 전에는 다시 하지 않는다");
                now += waits[i];
            }
            q.Tick(now);
            q.Done(job, FetchEnd.Failed, now);
            Assert.That(q.State, Is.EqualTo(QueueState.Failed), "재시도 3번 뒤");
            Assert.That(fake.Started.Count, Is.EqualTo(4));
        }

        [Test]
        public void M10_받기차례_404는판정보다시_429는모두멈춤_취소()
        {
            var fake = new Fake();
            var q = new UpdateQueue(Jobs(5), fake);
            q.Tick(0);
            q.Done(q.Jobs[2], FetchEnd.NotFound, 1);
            Assert.That(q.State, Is.EqualTo(QueueState.Refresh));
            Assert.That(fake.Stopped.Count, Is.EqualTo(3), "나머지 받는 중인 것을 멈춘다");
            q.Tick(10);
            Assert.That(fake.Started.Count, Is.EqualTo(4), "멈춘 뒤에는 시작하지 않는다");

            fake = new Fake();
            q = new UpdateQueue(Jobs(5), fake);
            q.Tick(0);
            q.Done(q.Jobs[0], FetchEnd.Limited, 1);
            Assert.That((q.State, fake.Stopped.Count), Is.EqualTo((QueueState.Later, 3)), "한도에 걸리면 재시도 없이 모두 멈춤");

            fake = new Fake();
            q = new UpdateQueue(Jobs(5), fake);
            q.Tick(0);
            q.Cancel();
            Assert.That((q.State, fake.Stopped.Count), Is.EqualTo((QueueState.Cancelled, 4)));
            q.Done(q.Jobs[0], FetchEnd.Ok, 2);
            Assert.That(q.State, Is.EqualTo(QueueState.Cancelled), "멈춘 뒤 늦게 온 끝은 무시");
        }
    }
}
