using System;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 행동 경계와 이벤트 머리를 채운다(01 §3-6 ①②⑪). Submit 한 번에 하나를 만들고 버린다(상태가 아니다).
    /// Seq는 sink 유무와 상관없이 늘린다 — GameState.NextSeq는 해시에 들어가서 AI 탐색(sink null)과 실제 진행이 같아야 한다.
    /// sink가 null이면 이벤트 객체를 만들지 않는다: 부르는 쪽이 <see cref="On"/>을 보고 new를 건너뛴다.
    /// </summary>
    public sealed class Emitter
    {
        public readonly GameState State;
        public readonly IEventSink Sink;

        /// <summary>지금 행동의 Seq. Begin 전 −1.</summary>
        public int Seq = -1;
        /// <summary>지금 행동의 행위자(이벤트 머리 Seat 기본값).</summary>
        public int Seat = SeatId.System;
        public ActionKind Kind;
        public AutoKind Auto;
        /// <summary>연쇄 원인(01 §3-6 ⑦). 이벤트 ParentSource 기본값. 행동을 시작하면 None.</summary>
        public SourceRef Parent = SourceRef.None;

        int index;
        bool sameIndex;
        int heldIndex;

        /// <summary>이벤트 없이 배틀 끝만 받는 곳(<see cref="IBattleEndWatch"/> — 그때 Sink는 null이다).</summary>
        public readonly IBattleEndWatch Watch;

        public Emitter(GameState state, IEventSink sink)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Watch = sink as IBattleEndWatch;
            Sink = Watch != null ? null : sink;
        }

        public bool On => Sink != null;

        /// <summary>
        /// 새 행동(01 §3-6 ①): Seq를 열고 ActionStarted를 낸다. 여는 때 [가정]: (a) Answer·Timeout·정비 적용 시작, (b) 자동 단계 시작,
        /// (c) 끼어든 반응이 끝나 원래 행동이 이어질 때(continues = 원래 Seq). 반응 답은 reactsTo = 창을 연 행동의 Seq.
        /// </summary>
        public void Begin(int seat, ActionKind kind, AutoKind auto, int continues = -1, int reactsTo = -1)
        {
            Seq = State.NextSeq++;
            index = 0;
            sameIndex = false;
            Seat = seat;
            Kind = kind;
            Auto = auto;
            Parent = SourceRef.None;
            if (On) Add(new ActionStarted { Seat = seat, Kind = kind, Auto = auto, Continues = continues, ReactsTo = reactsTo });
        }

        /// <summary>머리를 채워 낸다: Seq·Index, 비어 있으면 Seat = 행위자, Source = None, ParentSource = 지금 연쇄 원인.</summary>
        public void Add(GameEvent e)
        {
            if (Sink == null) return;
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (Seq < 0) throw new InvalidOperationException($"{e.GetType().Name}: 행동(Begin) 밖에서 낸 이벤트");
            e.Seq = Seq;
            e.Index = sameIndex ? heldIndex : index++;
            if (e.Seat == GameEvent.U) e.Seat = Seat;
            if (e.Source.IsUnset) e.Source = SourceRef.None;
            if (e.ParentSource.IsUnset) e.ParentSource = Parent;
            Sink.Add(e);
        }

        /// <summary>동시에 적용하는 이벤트(한 턴 피해 버퍼 등)를 같은 Index로 묶는다(01 §3-6 ⑪). EndSameIndex로 닫는다.</summary>
        public void BeginSameIndex()
        {
            heldIndex = index++;
            sameIndex = true;
        }

        public void EndSameIndex() => sameIndex = false;
    }
}
