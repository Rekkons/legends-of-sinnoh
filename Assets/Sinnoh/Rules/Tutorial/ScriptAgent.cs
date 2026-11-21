using System;
using System.Collections.Generic;
using Sinnoh.Rules.Session;

namespace Sinnoh.Rules.Tutorial
{
    /// <summary>
    /// 봇 좌석 각본 큐(07 §6-4): 묻는 선택마다 큐의 다음 표기로 합법 답을 고른다. 원칙 AI를 쓰지 않는다 — AI를 고쳐도 튜토리얼 판이 흔들리지 않게.
    /// 큐가 비었거나 표기에 맞는 합법 답이 없으면 InvalidOperationException(각본이 판과 어긋났다 — 시험이 먼저 잡는다).
    /// 풀어 준 수(<see cref="Released"/>)만큼만 답한다: 세션의 Hold가 <see cref="Waiting"/>을 보고 그 뒤의 답을 붙잡는다(봇이 대사보다 앞서지 않게).
    /// </summary>
    public sealed class ScriptAgent : IAgent
    {
        readonly Queue<AnswerPattern> queue;

        public readonly int Seat;

        /// <summary>낸 답 수.</summary>
        public int Consumed { get; private set; }

        /// <summary>낼 수 있는 답 수(누적). 진행기가 bot 단계에서 늘린다.</summary>
        public int Released;

        public ScriptAgent(int seat, IEnumerable<AnswerPattern> answers)
        {
            Seat = seat;
            queue = new Queue<AnswerPattern>(answers ?? Array.Empty<AnswerPattern>());
        }

        /// <summary>남은 각본 답 수.</summary>
        public int Left => queue.Count;

        /// <summary>풀어 준 답을 다 냈다 — 다음 답은 붙잡는다.</summary>
        public bool Waiting => Consumed >= Released;

        public AgentDecision Decide(AgentInput input, AgentBudget budget)
        {
            var c = input.Choice ?? throw new ArgumentException("선택이 없다", nameof(input));
            if (queue.Count == 0) throw new InvalidOperationException($"봇 좌석 {Seat}: 각본 큐가 비었는데 {c.Kind}/{c.Sub}를 묻는다");
            var p = queue.Dequeue();
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(input.View, input.Ctx, c, legal);
            var a = p.First(c, legal) ?? throw new InvalidOperationException($"봇 좌석 {Seat}: 각본 답 {p}이 {c.Kind}/{c.Sub}의 합법 답 {legal.Count}개에 없다");
            Consumed++;
            return new AgentDecision(a.Clone(), null);
        }
    }
}
