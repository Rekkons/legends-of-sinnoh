using System;
using System.Collections.Generic;
using System.Threading;

namespace Sinnoh.Rules.Session
{
    /// <summary>
    /// 호스트 작업 스레드 AI 러너(02 §1-2): 받은 물음을 받은 순서대로 작업 스레드 하나에서 <see cref="IAgent.Decide"/>로 셈하고 답을 큐에 넣는다.
    /// 세션(메인 스레드)이 Tick마다 <see cref="TryTake"/>로 꺼내 넣는다 — 세션 진행은 메인 스레드 한 곳뿐이고 답은 물음의 사본(보기·공개 기록·시드)으로만 정해져서,
    /// 러너가 있든 없든 같은 시드면 같은 판이다(<see cref="LocalSession"/>이 순서를 지킨다).
    /// 시계(clock, ms 단조 — 엔진 밖에서 준다)가 있으면 Budget.Now·Deadline(= 셈을 시작한 때 + ThinkMs)을 채운다. 없으면 시간 예산이 없다(시험 — MaxSimulations만).
    /// 갖는 쪽(Net의 NetRunner · 시험)이 방을 닫을 때 <see cref="Dispose"/>한다 — 셈하던 물음은 버린다(방이 닫혔다).
    /// </summary>
    public sealed class AgentWorker : IAgentRunner, IDisposable
    {
        /// <summary><see cref="WaitIdle"/>이 한 번에 기다리는 조각(ms).</summary>
        const int WaitSliceMs = 20;

        readonly object gate = new object();
        readonly Queue<AgentRequest> asks = new Queue<AgentRequest>();
        readonly Queue<AgentReply> replies = new Queue<AgentReply>();
        readonly Func<long> clock;
        readonly Thread thread;
        bool busy, stop;
        int decided;

        public AgentWorker(Func<long> clock = null, string name = "sinnoh-ai")
        {
            this.clock = clock;
            thread = new Thread(Loop) { IsBackground = true, Name = name };
            thread.Start();
        }

        /// <summary>셈한 물음 수(던진 것 포함).</summary>
        public int Decided
        {
            get
            {
                lock (gate) return decided;
            }
        }

        public void Post(AgentRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            lock (gate)
            {
                if (stop) throw new ObjectDisposedException(nameof(AgentWorker));
                asks.Enqueue(request);
                Monitor.PulseAll(gate);
            }
        }

        public bool TryTake(out AgentReply reply)
        {
            lock (gate)
            {
                if (replies.Count == 0)
                {
                    reply = null;
                    return false;
                }
                reply = replies.Dequeue();
                return true;
            }
        }

        /// <summary>받은 물음을 다 셈할 때까지 기다린다(답은 큐에 남는다 — 꺼내는 것은 세션). 시험처럼 시각을 손으로 옮기는 드라이버가 쓴다. 다 되면 true, 시간이 지나면 false.</summary>
        public bool WaitIdle(int timeoutMs = 10000)
        {
            lock (gate)
            {
                for (int waited = 0; asks.Count > 0 || busy; waited += WaitSliceMs)
                {
                    if (waited >= timeoutMs) return false;
                    Monitor.Wait(gate, WaitSliceMs);
                }
                return true;
            }
        }

        /// <summary>작업 스레드를 끝낸다(셈하던 물음은 끝나기를 기다리고 남은 물음은 버린다). 그 뒤 Post는 ObjectDisposedException.</summary>
        public void Dispose()
        {
            lock (gate)
            {
                if (stop) return;
                stop = true;
                asks.Clear();
                Monitor.PulseAll(gate);
            }
            thread.Join();
        }

        void Loop()
        {
            while (true)
            {
                AgentRequest r;
                lock (gate)
                {
                    while (asks.Count == 0 && !stop) Monitor.Wait(gate);
                    if (stop) return;
                    r = asks.Dequeue();
                    busy = true;
                }
                var reply = new AgentReply { Serial = r.Serial, ChoiceId = r.ChoiceId };
                try
                {
                    if (clock != null)
                    {
                        r.Budget.Now = clock;
                        r.Budget.Deadline = clock() + Math.Max(0, r.ThinkMs);
                    }
                    reply.Decision = r.Agent.Decide(r.Input, r.Budget);
                    if (reply.Decision?.Answer == null) reply.Error = new InvalidOperationException($"AI가 선택 {r.ChoiceId}에 답하지 않았다");
                }
                catch (Exception e)
                {
                    reply.Decision = null;
                    reply.Error = e;
                }
                lock (gate)
                {
                    replies.Enqueue(reply);
                    decided++;
                    busy = false;
                    Monitor.PulseAll(gate);
                }
            }
        }
    }
}
