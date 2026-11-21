using System;
using System.Collections.Generic;

namespace Sinnoh.Data
{
    /// <summary>받기 하나가 끝난 꼴(<see cref="IUpdateFetch"/>가 알린다).</summary>
    public enum FetchEnd
    {
        /// <summary>받았고 크기 · 지문이 맞아 stage/&lt;sha&gt;에 놓였다.</summary>
        Ok,
        /// <summary>404 — 판 정보를 한 번 다시 읽는다.</summary>
        NotFound,
        /// <summary>요청 한도(429 · 503 · Cloudflare 1027) — 재시도하지 않고 모두 멈춘다.</summary>
        Limited,
        /// <summary>받았지만 크기 · 지문이 틀림(.part는 지웠다) — 재시도.</summary>
        Bad,
        /// <summary>끊김 · 시간 초과 · 그 밖(.part는 남김 — 이어받기) — 재시도.</summary>
        Failed,
    }

    /// <summary>받을 것 하나(지문 하나 또는 zip 하나).</summary>
    public sealed class UpdateJob
    {
        public readonly string Sha256, Url;
        public readonly long Size;
        /// <summary>실패한 횟수 · 다음에 해 볼 시각(ms) · 받은 바이트(.part 포함).</summary>
        public int Tries;
        public long Due, Got;
        public bool Running, Finished;

        public UpdateJob(string sha256, long size, string url)
        {
            Sha256 = sha256;
            Size = size;
            Url = url;
        }
    }

    /// <summary>받는 일 자체(Client — UnityWebRequest, 시험은 가짜). Start는 바로 돌아오고, 끝은 <see cref="UpdateQueue.Done"/>로 알린다.</summary>
    public interface IUpdateFetch
    {
        void Start(UpdateJob job);
        void Stop(UpdateJob job);
    }

    public enum QueueState { Running, Done, Later, Refresh, Failed, Cancelled }

    /// <summary>
    /// 받기 차례 상태 기계(결정 161 4-1 — 순수, 시각은 인자): 동시 <see cref="Parallel"/>개 · 지문마다 재시도 3번(<see cref="RetryMs"/> 뒤) · 404면 <see cref="QueueState.Refresh"/>(흐름이 판 정보를 한 번 다시 읽고 다시 계획) ·
    /// 한도(429/503/1027)면 모두 멈춤(<see cref="QueueState.Later"/>) · 진행 바이트 합 · 취소.
    /// </summary>
    public sealed class UpdateQueue
    {
        public const int Parallel = 4;

        /// <summary>재시도 간격(ms) — 길이가 재시도 수. 시험이 줄인다.</summary>
        public int[] RetryMs = { 2000, 5000, 15000 };

        readonly List<UpdateJob> jobs;
        readonly IUpdateFetch fetch;

        public QueueState State { get; private set; } = QueueState.Running;

        /// <summary>멈춘 까닭(마지막으로 실패한 지문 — 로그용).</summary>
        public string Why { get; private set; }

        public IReadOnlyList<UpdateJob> Jobs => jobs;

        public long Total { get; }

        public long Got
        {
            get
            {
                long n = 0;
                foreach (var j in jobs) n += Math.Min(j.Finished ? j.Size : j.Got, j.Size);
                return n;
            }
        }

        public UpdateQueue(IEnumerable<UpdateJob> jobs, IUpdateFetch fetch)
        {
            this.jobs = new List<UpdateJob>(jobs);
            this.fetch = fetch;
            foreach (var j in this.jobs) Total += j.Size;
            if (this.jobs.Count == 0) State = QueueState.Done;
        }

        /// <summary>때가 된 것을 동시 수만큼 시작한다.</summary>
        public void Tick(long now)
        {
            if (State != QueueState.Running) return;
            int running = 0;
            foreach (var j in jobs) if (j.Running) running++;
            foreach (var j in jobs)
            {
                if (running >= Parallel) break;
                if (j.Running || j.Finished || j.Due > now) continue;
                j.Running = true;
                running++;
                fetch.Start(j);
            }
        }

        public void Progress(UpdateJob job, long got) => job.Got = got;

        public void Done(UpdateJob job, FetchEnd end, long now)
        {
            if (!job.Running || State != QueueState.Running) return;
            job.Running = false;
            switch (end)
            {
                case FetchEnd.Ok:
                    job.Finished = true;
                    job.Got = job.Size;
                    if (jobs.TrueForAll(j => j.Finished)) State = QueueState.Done;
                    return;
                case FetchEnd.NotFound:
                    Halt(QueueState.Refresh, job);
                    return;
                case FetchEnd.Limited:
                    Halt(QueueState.Later, job);
                    return;
                default:
                    if (end == FetchEnd.Bad) job.Got = 0;
                    job.Tries++;
                    if (job.Tries > RetryMs.Length) Halt(QueueState.Failed, job);
                    else job.Due = now + RetryMs[job.Tries - 1];
                    return;
            }
        }

        /// <summary>모두 멈춘다(.part는 남는다 — 다음에 이어받는다).</summary>
        public void Cancel()
        {
            if (State == QueueState.Running) Halt(QueueState.Cancelled, null);
        }

        void Halt(QueueState state, UpdateJob why)
        {
            State = state;
            Why = why?.Sha256;
            foreach (var j in jobs)
                if (j.Running)
                {
                    j.Running = false;
                    fetch.Stop(j);
                }
        }
    }
}
