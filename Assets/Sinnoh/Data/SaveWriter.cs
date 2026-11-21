using System;
using System.Collections.Generic;
using System.Threading;
using Sinnoh.Rules.Session;

namespace Sinnoh.Data
{
    /// <summary>
    /// 방 저장의 쓰기 스레드(02 §7 「파일 쓰기는 호스트 작업 스레드에서 한다」): 세션이 부르는 쓰기를 받은 순서대로 작업 스레드 하나에서 <see cref="SaveStore"/>에 쓴다.
    /// 부르는 쪽 스레드에서 하는 것은 작은 직렬화뿐이다 — 머리(방이 뒤에 고친다)와 로그 줄. 스냅샷은 세션이 이미 사본(상태 Clone)으로 넘기므로 직렬화도 작업 스레드에서 한다.
    /// 실패는 던지지 않는다(부른 쪽은 이미 다음 명령으로 갔다): <see cref="Problems"/>에 적고 같은 일을 잠시 뒤 다시 한다 — 로그 줄 순서가 어긋나지 않게 뒤의 일은 기다린다 [가정 M7-27].
    /// 방이 판을 끝낼 때([로비로] · 방 닫기 · 앱 종료 — <see cref="HostConfig.Store"/>가 만든 저장은 방이 닫는다) <see cref="Dispose"/> = 밀린 쓰기를 비우고 스레드를 끝낸다.
    /// </summary>
    public sealed class SaveWriter : ISaveStore, IDisposable
    {
        /// <summary>실패한 쓰기를 다시 하기까지(ms).</summary>
        const int RetryMs = 200;
        /// <summary>Dispose가 밀린 쓰기를 기다리는 한도(ms) — 넘기면 남은 일을 버리고 Problems에 적는다.</summary>
        public const int DisposeWaitMs = 10000;
        const int WaitSliceMs = 20;

        readonly SaveStore store;
        readonly object gate = new object();
        readonly Queue<Action> jobs = new Queue<Action>();
        readonly List<string> problems = new List<string>();
        readonly Thread thread;
        bool stop, disposed;
        int written;

        public SaveWriter(string directory) : this(new SaveStore(directory)) { }

        public SaveWriter(SaveStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            thread = new Thread(Loop) { IsBackground = true, Name = "sinnoh-save" };
            thread.Start();
        }

        /// <summary>쓴 일 수.</summary>
        public int Written
        {
            get
            {
                lock (gate) return written;
            }
        }

        /// <summary>아직 쓰지 않은 일 수(쓰는 중 포함).</summary>
        public int Pending
        {
            get
            {
                lock (gate) return jobs.Count;
            }
        }

        /// <summary>지금까지의 쓰기 실패(다시 해서 됐어도 남는다)·버린 일. 화면·로그가 경고로 보인다.</summary>
        public List<string> Problems
        {
            get
            {
                lock (gate) return new List<string>(problems);
            }
        }

        public void WriteHead(SaveHead head)
        {
            if (head == null) throw new ArgumentNullException(nameof(head));
            string json = SaveStore.HeadJson(head);
            Enqueue(() => store.WriteHeadJson(json));
        }

        public void WriteSnapshot(SaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Enqueue(() => store.WriteSnapshot(snapshot));
        }

        public void AppendCommand(SaveLogLine line)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));
            string json = SaveStore.Serialize(line, false);
            Enqueue(() => store.AppendLineJson(json));
        }

        /// <summary>이어하기 때 한 번: 밀린 쓰기를 다 쓴 뒤 부른 스레드에서 바로 자른다(그동안 새 일은 부르는 쪽 한 스레드에서만 들어오므로 겹치지 않는다).</summary>
        public void TruncateLog(int commandCount)
        {
            if (!Flush(DisposeWaitMs)) throw new InvalidOperationException("밀린 저장 쓰기가 끝나지 않아 로그를 자르지 못했다");
            store.TruncateLog(commandCount);
        }

        /// <summary>밀린 쓰기를 다 쓸 때까지 기다린다. 다 되면 true, 시간이 지나면 false.</summary>
        public bool Flush(int timeoutMs)
        {
            lock (gate)
            {
                for (int waited = 0; jobs.Count > 0; waited += WaitSliceMs)
                {
                    if (waited >= timeoutMs) return false;
                    Monitor.Wait(gate, WaitSliceMs);
                }
                return true;
            }
        }

        /// <summary>밀린 쓰기를 비우고(<see cref="DisposeWaitMs"/>까지) 스레드를 끝낸다. 그 뒤의 쓰기는 ObjectDisposedException.</summary>
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
            }
            Flush(DisposeWaitMs);
            lock (gate)
            {
                stop = true;
                Monitor.PulseAll(gate);
            }
            thread.Join();
        }

        void Enqueue(Action job)
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(SaveWriter));
                jobs.Enqueue(job);
                Monitor.PulseAll(gate);
            }
        }

        void Loop()
        {
            while (true)
            {
                Action job;
                lock (gate)
                {
                    while (jobs.Count == 0 && !stop) Monitor.Wait(gate);
                    if (jobs.Count == 0) return;
                    job = jobs.Peek();
                }
                try
                {
                    job();
                    lock (gate)
                    {
                        jobs.Dequeue();
                        written++;
                        Monitor.PulseAll(gate);
                    }
                }
                catch (Exception e)
                {
                    lock (gate)
                    {
                        problems.Add($"저장 쓰기 실패(다시 씀): {e.Message}");
                        if (stop)
                        {
                            problems.Add($"방을 닫으며 쓰지 못한 일 {jobs.Count}개를 버렸다");
                            jobs.Clear();
                            Monitor.PulseAll(gate);
                            return;
                        }
                        Monitor.Wait(gate, RetryMs);
                    }
                }
            }
        }
    }
}
