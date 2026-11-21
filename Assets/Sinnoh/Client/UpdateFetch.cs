using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Sinnoh.Data;
using UnityEngine.Networking;

namespace Sinnoh.Client
{
    /// <summary>
    /// 자동 업데이트의 받기(결정 161 4-2 — <see cref="IUpdateFetch"/>, UnityWebRequest): 받을 것 하나 = 요청 하나, <c>stage/&lt;sha&gt;.part</c>에 쓴다.
    /// .part가 있으면 <c>Range: bytes=&lt;길이&gt;-</c> — 206이면 이어 쓰고 200이면 처음부터, 416이면 .part를 지우고 다시. 받은 바이트가 manifest 크기를 넘으면 끊는다.
    /// <see cref="StallMs"/> 동안 진행이 없으면 끊고 재시도. 취소 = Abort(.part는 남김). 다 받으면 다른 스레드에서 크기 · 지문을 확인해 <c>stage/&lt;sha&gt;</c>로(<see cref="StageStore.Commit"/>).
    /// 오류 응답의 몸은 .part에 쓰지 않는다(<see cref="DownloadHandlerScript"/> — 응답 코드를 보고 고른다, 몸 앞 4 KB만 쥐어 Cloudflare 1027을 가린다).
    /// 끝은 <see cref="Poll"/>(프레임마다)이 <see cref="UpdateQueue.Done"/>로 알린다.
    /// </summary>
    public sealed class UpdateFetch : IUpdateFetch
    {
        /// <summary>진행이 없으면 끊는 시간(ms) — 시험이 줄인다.</summary>
        public int StallMs = 30000;

        readonly string stage;
        readonly Func<long> clock;
        readonly List<Run> runs = new List<Run>();

        public UpdateFetch(string stage, Func<long> clock)
        {
            this.stage = stage;
            this.clock = clock;
        }

        sealed class Run
        {
            public UpdateJob Job;
            public UnityWebRequest Req;
            public Writer Out;
            public long Seen, SeenAt;
            public Task<bool> Check;
            public FetchEnd? End;
        }

        // 응답 코드를 보고 쓸 곳을 고르는 받기 손잡이: 206(시작 자리가 맞을 때) = 이어 쓰기, 200 = 처음부터, 그 밖 = 메모리 4 KB(몸은 버린다)
        sealed class Writer : DownloadHandlerScript
        {
            readonly string part;
            readonly long have, size;
            public UnityWebRequest Req;
            FileStream file;
            bool chosen;
            public long Written;
            public bool TooBig;
            public readonly MemoryStream Body = new MemoryStream();

            public Writer(string part, long have, long size) : base(new byte[64 * 1024])
            {
                this.part = part;
                this.have = have;
                this.size = size;
            }

            protected override bool ReceiveData(byte[] data, int length)
            {
                if (!chosen)
                {
                    chosen = true;
                    long code = Req.responseCode;
                    string range = Req.GetResponseHeader("Content-Range") ?? "";
                    if (code == 206 && have > 0 && range.StartsWith("bytes " + have + "-", StringComparison.Ordinal))
                    {
                        file = new FileStream(part, FileMode.Append, FileAccess.Write, FileShare.Read);
                        Written = have;
                    }
                    else if (code == 200 || (code == 206 && have == 0 && range.StartsWith("bytes 0-", StringComparison.Ordinal)))
                        file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.Read);
                }
                if (file == null)
                {
                    if (Body.Length < 4096) Body.Write(data, 0, Math.Min(length, 4096 - (int)Body.Length));
                    return true;
                }
                if (Written + length > size)
                {
                    TooBig = true;
                    return false;
                }
                file.Write(data, 0, length);
                Written += length;
                return true;
            }

            public bool Wrote => file != null;

            public void Shut()
            {
                if (file == null) return;
                file.Dispose();
                file = null;
            }
        }

        public void Start(UpdateJob job)
        {
            string part = StageStore.PartOf(stage, job.Sha256);
            long have = StageStore.PartLength(stage, job.Sha256);
            var run = new Run { Job = job, SeenAt = clock() };
            runs.Add(run);
            if (have > job.Size)
            {
                File.Delete(part);
                have = 0;
            }
            job.Got = have;
            if (have == job.Size)
            {
                run.Check = Verify(job);   // 다 받아 놓고 확인 전에 끊긴 조각
                return;
            }
            var writer = new Writer(part, have, job.Size);
            var req = new UnityWebRequest(job.Url, UnityWebRequest.kHttpVerbGET, writer, null) { disposeDownloadHandlerOnDispose = true };
            writer.Req = req;
            if (have > 0) req.SetRequestHeader("Range", "bytes=" + have + "-");
            run.Req = req;
            run.Out = writer;
            run.Seen = have;
            req.SendWebRequest();
        }

        public void Stop(UpdateJob job)
        {
            var run = runs.Find(r => r.Job == job);
            if (run == null) return;
            runs.Remove(run);
            Close(run, abort: true);
        }

        /// <summary>남은 요청을 모두 끊는다(.part는 남는다).</summary>
        public void StopAll()
        {
            foreach (var r in runs) Close(r, abort: true);
            runs.Clear();
        }

        Task<bool> Verify(UpdateJob job)
        {
            string dir = stage, sha = job.Sha256;
            long size = job.Size;
            return Task.Run(() => StageStore.Commit(dir, sha, size, out _));
        }

        /// <summary>프레임마다: 진행 · 끝을 차례에 알린다.</summary>
        public void Poll(UpdateQueue queue)
        {
            long now = clock();
            foreach (var r in runs.ToArray())
            {
                if (!runs.Contains(r)) continue;   // 앞 항목의 끝이 차례를 멈춰(404 · 한도 · 실패) 이것도 이미 멈췄다
                if (r.Check != null)
                {
                    if (!r.Check.IsCompleted) continue;
                    Finish(queue, r, r.Check.Status == TaskStatus.RanToCompletion && r.Check.Result ? FetchEnd.Ok : FetchEnd.Bad, now);
                    continue;
                }
                var req = r.Req;
                long got = r.Out.Wrote || r.Out.Written > 0 ? r.Out.Written : r.Job.Got;
                if (got != r.Seen)
                {
                    r.Seen = got;
                    r.SeenAt = now;
                    queue.Progress(r.Job, got);
                }
                if (!req.isDone)
                {
                    if (now - r.SeenAt > StallMs) Finish(queue, r, FetchEnd.Failed, now, abort: true);   // 진행 없음 — 끊고 재시도(.part는 남김)
                    continue;
                }
                long code = req.responseCode;
                var result = req.result;
                bool wrote = r.Out.Wrote;
                long written = r.Out.Written;
                string body = Encoding.UTF8.GetString(r.Out.Body.ToArray());
                bool tooBig = r.Out.TooBig;
                Close(r, abort: false);
                if (tooBig)
                {
                    TryDelete(StageStore.PartOf(stage, r.Job.Sha256));
                    Finish(queue, r, FetchEnd.Bad, now);
                }
                else if (wrote && result == UnityWebRequest.Result.Success && written == r.Job.Size)
                {
                    r.Req = null;
                    r.Check = Verify(r.Job);
                }
                else if (code == 404) Finish(queue, r, FetchEnd.NotFound, now);
                else if (code == 429 || code == 503 || body.Contains("1027")) Finish(queue, r, FetchEnd.Limited, now);
                else
                {
                    if (code == 416) TryDelete(StageStore.PartOf(stage, r.Job.Sha256));
                    UnityEngine.Debug.Log("[Update] 받기 실패 " + r.Job.Sha256.Substring(0, 12) + " — " + code + " " + result);
                    Finish(queue, r, FetchEnd.Failed, now);
                }
            }
        }

        void Finish(UpdateQueue queue, Run r, FetchEnd end, long now, bool abort = false)
        {
            runs.Remove(r);
            Close(r, abort);
            if (end == FetchEnd.Failed) queue.Progress(r.Job, StageStore.PartLength(stage, r.Job.Sha256));
            queue.Done(r.Job, end, now);
        }

        static void Close(Run r, bool abort)
        {
            if (r.Req == null) return;
            if (abort && !r.Req.isDone) r.Req.Abort();
            r.Out.Shut();
            r.Req.Dispose();
            r.Req = null;
        }

        static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (IOException) { }
        }
    }
}
