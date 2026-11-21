using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>화면 상수(Local 조정 값, 04 §8-5 공용 수치 표). 방장 멈춤에는 들지 않는다.</summary>
    public static class FxTune
    {
        /// <summary>입력이 열린 뒤 클릭 막기(P4): 연출이 걷혀 단추가 드러난 뒤 이만큼은 클릭을 받지 않는다.</summary>
        public static readonly Tunable ClickGuardMs = Tunables.Shared.Define("fx.clickGuardMs", 300, 0, 2000, TunableScope.Local);
        /// <summary>토스트 하나가 떠 있는 시간.</summary>
        public static readonly Tunable ToastMs = Tunables.Shared.Define("fx.toastMs", 4500, 500, 20000, TunableScope.Local);
        /// <summary>함께 떠 있는 토스트 수. 넘으면 가장 오래된 것이 내려간다.</summary>
        public static readonly Tunable ToastMax = Tunables.Shared.Define("fx.toastMax", 3, 1, 10, TunableScope.Local);
        /// <summary>S02 「연출 빠름」의 길이(보통의 %, 04 §8-1 P17 「×0.5」 [가정 — plan §5 #59]): 대기열 시계가 100 / 이 값 배로 흐른다.</summary>
        public static readonly Tunable FastPercent = Tunables.Shared.Define("fx.fastPercent", 50, 10, 100, TunableScope.Local);
    }

    /// <summary>
    /// S02 「연출」(04 §8-1 P14·P17 · §8-4): 내 화면의 재생 속도. 방장 멈춤(<c>PlayTiming</c>)은 바꾸지 않는다 — 대기열이 빨리 비어도 상대(AI)의 답은 방장 시각에 온다.
    /// 보통 = 그대로 · 빠름 = 대기열 시계가 <see cref="FxTune.FastPercent"/>만큼의 길이로 흐른다(클릭 막기·토스트는 실시각) ·
    /// 끄기 = 받은 항목을 다음 시간에 한 번에 다 보내고(소리 단서 없이 — 착지·화면 바꾸기는 그대로 닿는다) 갱신 때 대표 소리 하나만 낸다.
    /// </summary>
    public enum FxSpeed { Normal, Fast, Off }

    /// <summary>⑤ 토스트 하나: 실시각 <see cref="StartMs"/>(<see cref="FxSequencer.RealMs"/>)에 떠서 <c>FxTune.ToastMs</c> 뒤에 내려간다.</summary>
    public sealed class FxToast
    {
        public string Text;
        public long StartMs;
    }

    /// <summary>
    /// 연출 시퀀서(04 §8-2): 층 다섯을 한 시간축 아래에서 돌린다. 시각은 밖에서 넣는다(<see cref="Advance"/>에 흐른 ms) — 「도착 즉시 트는」 길이 없다.
    /// <list type="bullet">
    /// <item>대기열은 한 줄(FIFO)이고 건너뛰지도 줄이지도 않는다(P3). 항목 = <c>Seq</c> 하나, 길이는 <c>PlayTiming</c>이 센 값.</item>
    /// <item>항목 안에서 ① 차단 컷신 · ② 띠 · ③ 단계가 이벤트 순서대로 이어지고(①이 도는 동안 ②③은 기다린다), 보기 시간은 끝에 남는다. ④ 장식은 항목 시각을 따른다.</item>
    /// <item>단서(표시 값·소리)는 매번 (지난 시각, 지금] 구간을 훑어 낸다(04 §8-4) — 일시정지·건너뜀에도 시계를 따른다.</item>
    /// <item>「대기열이 빈다」 = 받은 이벤트를 다 재생함(<see cref="IsEmpty"/>, P4). <c>Seq</c>가 닫히기를 기다리지 않는다. 비면 표시 값이 보기와 같아진다(§8-3).</item>
    /// <item>일시정지: 지금 항목은 끝까지 재생하고 다음 항목을 시작하지 않는다.</item>
    /// <item>⑤ 토스트는 대기열과 따로 실시각(<see cref="RealMs"/>)으로 뜨고 내려간다 — 「빠름」·「끄기」가 대기열 시계를 당겨도 수명은 그대로다.</item>
    /// </list>
    /// </summary>
    public sealed class FxSequencer
    {
        const int HistoryMax = 8;

        readonly Queue<FxEntry> pending = new Queue<FxEntry>();
        readonly List<FxEntry> history = new List<FxEntry>();
        readonly List<FxToast> toasts = new List<FxToast>();
        readonly List<FxCue> swept = new List<FxCue>();
        long emptySinceMs = long.MinValue / 2;   // 아무것도 재생한 적이 없으면 입력은 처음부터 열려 있다

        public readonly FxShown Shown = new FxShown();

        /// <summary>시퀀서 시각(만든 뒤 흐른 ms). 「빠름」·「끄기」에서는 대기열이 남은 동안 실시각보다 빨리 흐른다.</summary>
        public long NowMs { get; private set; }

        /// <summary>실시각(만든 뒤 <see cref="Advance"/>로 받은 ms의 합 — 속도와 무관). 토스트가 쓴다.</summary>
        public long RealMs { get; private set; }

        /// <summary>지금 재생 중인 항목(없으면 null)과 그 항목 안 시각.</summary>
        public FxEntry Current { get; private set; }
        public int LocalMs { get; private set; }

        /// <summary>마지막으로 끝난 항목. 결과는 다음 항목이 시작될 때까지 화면에 남는다(P3).</summary>
        public FxEntry Last { get; private set; }

        /// <summary>
        /// <see cref="Last"/>가 끝난 뒤 흐른 시퀀서 시각(ms — 끝난 항목이 없으면 아주 큰 값). 항목 끝에 걸친 그림(착지 먼지)이 항목이 끝난 뒤에도 마저 돈다.
        /// 「끄기」로 한 번에 보낸 항목은 아주 큰 값이다(보이지 않고 지나간 연출의 꼬리를 그리지 않는다).
        /// </summary>
        public long SinceLastMs => lastEndMs < 0 ? long.MaxValue / 4 : NowMs - lastEndMs;
        long lastEndMs = -1;

        /// <summary>기다리는 항목 수(「다음 n」 딱지).</summary>
        public int Pending => pending.Count;

        /// <summary>아직 다 재생하지 않은 항목(재생 중인 것 → 기다리는 것, 받은 순서). 화면이 「아직 착지하지 않은 것」을 찾을 때 쓴다.</summary>
        public IEnumerable<FxEntry> Queued
        {
            get
            {
                if (Current != null) yield return Current;
                foreach (var e in pending) yield return e;
            }
        }

        /// <summary>일시정지(S22): 지금 항목은 끝까지, 다음 항목은 시작하지 않는다.</summary>
        public bool Paused;

        /// <summary>소리 단서를 내지 않는다(다시 보기·재접속). 표시 값 단서는 그대로 닿는다.</summary>
        public bool Muted;

        /// <summary>내 화면의 재생 속도(S02 「연출」 — <see cref="FxSpeed"/>). 기본 보통.</summary>
        public FxSpeed Speed;

        /// <summary>
        /// 따라잡을 연출 시각(ms — 플레이 시험 T4): 방 판에서 내 대기열이 방장 멈춤보다 늦게 끝날 만큼. 남은 동안 대기열 시계가 두 배로 흘러(보통 · 빠름 모두 — 끄기는 이미 한 번에 보낸다)
        /// 그만큼을 덜고, 대기열이 비면 0이 된다. 화면(<c>GameFlow</c>)이 갱신마다 다시 센다. 일시정지 · 건너뛰기와 무관하다.
        /// </summary>
        public long CatchUpMs
        {
            get => catchUp;
            set => catchUp = Math.Max(0, value);
        }

        long catchUp;

        /// <summary>지금 낸 소리 단서의 항목 Seq(<see cref="Sound"/> 안에서 읽는다 — 「끄기」의 대표 소리는 아직 재생하지 않은 그 항목의 것).</summary>
        public int CueSeq { get; private set; } = -1;

        /// <summary>대기열을 다 비우는 데 남은 연출 시각(ms): 재생 중인 항목의 나머지 + 기다리는 항목 길이(일시정지면 재생 중인 것만 — 다음 항목은 시작하지 않는다).</summary>
        public long LeftMs
        {
            get
            {
                long left = Current != null ? Current.LengthMs - LocalMs : 0;
                if (!Paused) foreach (var e in pending) left += e.LengthMs;
                return left;
            }
        }

        /// <summary>소리 단서가 닿았다. 재생은 받는 쪽이 한다.</summary>
        public event Action<FxCue> Sound;

        /// <summary>
        /// 지금 낸 소리 단서의 시퀀서 시각(<see cref="Sound"/> 안에서 읽는다): 그 항목이 시작한 시각 + 단서의 항목 안 시각(항목 길이로 자른다).
        /// 한 번에 크게 보내도 잘게 보낸 것과 같은 값이다 — 단서 시각 로그가 쓴다.
        /// </summary>
        public long CueMs { get; private set; }

        long entryStartMs;
        public event Action<FxEntry> Started, Finished;

        /// <summary>대기열이 비었다 = 받은 이벤트를 다 재생했다(P4).</summary>
        public bool IsEmpty => Current == null && pending.Count == 0;

        /// <summary>할 일이 없다(대기열·토스트가 없고 클릭 막기 시간도 지났다) — 실행기에서 빠져도 된다.</summary>
        public bool Idle => InputOpen && toasts.Count == 0;

        /// <summary>
        /// 일시정지로 서 있다: 재생 중인 항목도 토스트도 없고 기다리는 항목만 남았다 — 시간이 흘러도 바뀌는 것이 없어 실행기에서 빠져도 된다
        /// (바쁨으로 남으면 일시정지 내내 유휴 30fps로 내려가지 못한다, 04 §8-6). 일시정지를 풀면 <see cref="FxPlayer.SetPaused"/>가 다시 건다.
        /// </summary>
        public bool Stalled => Paused && Current == null && pending.Count > 0 && toasts.Count == 0;

        /// <summary>내 입력을 열어도 된다(P4): 대기열이 비었고, 빈 뒤 클릭 막기 시간이 지났다. 일시정지는 화면이 답 단추만 따로 흐린다.</summary>
        public bool InputOpen => IsEmpty && NowMs - emptySinceMs >= FxTune.ClickGuardMs.Value;

        public IReadOnlyList<FxToast> Toasts => toasts;

        /// <summary>
        /// 항목을 줄 끝에 넣는다(받은 순서 그대로). 항목이 바꿀 표시 값을 그 전 값으로 붙잡는다 — 보기가 먼저 바뀌어도 화면은 착지 때 바뀐다.
        /// 「끄기」(<see cref="Speed"/>)면 넣은 항목들(갱신 하나)의 대표 소리 하나 — 받은 순서로 첫 소리 단서 — 를 바로 낸다(04 §8-4 「끄기면 갱신 때 대표 소리 한 번」, 무음이면 없음).
        /// </summary>
        public void Enqueue(IEnumerable<FxEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            bool told = Speed != FxSpeed.Off || Muted;
            foreach (var e in entries)
            {
                if (e == null) continue;
                foreach (var cue in e.Cues)
                {
                    if (!cue.IsSound) Shown.Hold(cue.Key, cue.Before);
                    else if (!told)
                    {
                        told = true;
                        CueMs = NowMs;
                        CueSeq = e.Seq;
                        Sound?.Invoke(cue);
                    }
                }
                pending.Enqueue(e);
            }
        }

        /// <summary>
        /// 게임을 떠난다: 재생 중·기다리는 항목과 토스트를 모두 버리고 붙잡은 표시 값을 놓는다. 끝난 것으로 알리지 않는다(<see cref="Finished"/> 없음).
        /// 바로 유휴가 된다 — 실행기에 걸려 있던 손잡이는 다음 <see cref="Advance"/>에서 빠진다.
        /// </summary>
        public void Clear()
        {
            pending.Clear();
            history.Clear();
            toasts.Clear();
            Current = null;
            Last = null;
            LocalMs = 0;
            catchUp = 0;
            Shown.Clear();
            emptySinceMs = long.MinValue / 2;
        }

        /// <summary>토스트를 띄운다(⑤). 한도를 넘으면 가장 오래된 것이 내려간다.</summary>
        public void Toast(string text)
        {
            toasts.Add(new FxToast { Text = text, StartMs = RealMs });
            while (toasts.Count > FxTune.ToastMax.Value) toasts.RemoveAt(0);
        }

        /// <summary>Seq로 항목을 찾는다(재생 중 · 기다리는 것 · 최근에 끝난 것). 이어 받기(<c>Continues</c>·<c>ReactsTo</c>)가 앞 항목의 화면을 찾을 때 쓴다. 없으면 null.</summary>
        public FxEntry Find(int seq)
        {
            if (seq < 0) return null;
            if (Current != null && Current.Seq == seq) return Current;
            for (int i = history.Count - 1; i >= 0; i--)
                if (history[i].Seq == seq) return history[i];
            foreach (var e in pending)
                if (e.Seq == seq) return e;
            return null;
        }

        /// <summary>지금 항목에서 이 시각에 돌고 있는 단계·장식을 <paramref name="into"/>에 더한다(시작 ≤ 시각 &lt; 끝).</summary>
        public void Active(List<FxStep> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (Current == null) return;
            foreach (var s in Current.Steps)
                if (s.StartMs <= LocalMs && LocalMs < s.EndMs) into.Add(s);
            foreach (var s in Current.Decos)
                if (s.StartMs <= LocalMs && LocalMs < s.EndMs) into.Add(s);
        }

        /// <summary>
        /// 시간을 <paramref name="dtMs"/>만큼 보낸다. 항목이 끝나면 남은 시간으로 다음 항목을 이어 재생한다(길이 0인 항목은 그 자리에서 지나간다).
        /// 「빠름」이면 대기열이 남은 동안 연출 시각이 100 / <see cref="FxTune.FastPercent"/> 배로 흐르고, 비고 나면 실시각이다(클릭 막기·토스트는 그대로).
        /// 「끄기」면 남은 대기열을 소리 없이 한 번에 보낸 뒤 실시각을 보낸다(빠름 50%·끄기는 나눠 보내도 한 번에 보낸 것과 같다 — 다른 %는 올림만큼 어긋날 수 있다).
        /// </summary>
        public void Advance(int dtMs)
        {
            if (dtMs < 0) throw new ArgumentOutOfRangeException(nameof(dtMs));
            Play(dtMs);
            RealMs += dtMs;
            int toastMs = FxTune.ToastMs.Value;
            toasts.RemoveAll(t => RealMs - t.StartMs >= toastMs);
        }

        /// <summary>
        /// 재생 중인 항목을 그 항목 안 시각 <paramref name="localMs"/>까지 소리 없이 보낸다(건너뛰기 — 04 §8-4 「건너뛰기는 무음」, 표시 값은 내려앉는다).
        /// 지금 항목 안에서만 간다(항목 끝까지면 다음 항목이 시작한다). 지금 항목이 없거나 이미 지났으면 false. 진화 창의 스페이스 바(결정 84 L11)가 쓴다.
        /// </summary>
        public bool SkipTo(int localMs)
        {
            if (Current == null || localMs <= LocalMs) return false;
            int dt = Math.Min(localMs, Current.LengthMs) - LocalMs;
            if (dt <= 0) return false;
            bool was = Muted;
            Muted = true;
            try { Run(dt); }
            finally { Muted = was; }
            return true;
        }

        void Play(int dtMs)
        {
            if (catchUp > 0)
            {
                // 따라잡기: 이 시간만큼 더 보낸다(두 배) — 남은 대기열을 넘지 않는다. 다 보냈거나 비었으면 그친다
                long room = Math.Max(0, LeftMs - dtMs);
                int extra = (int)Math.Min(Math.Min(catchUp, dtMs), room);
                if (Speed != FxSpeed.Off && extra > 0)
                {
                    Run(extra);
                    catchUp -= extra;
                }
                if (room <= extra || Speed == FxSpeed.Off) catchUp = 0;
            }
            long left = Speed == FxSpeed.Normal ? 0 : LeftMs;
            if (left <= 0)
            {
                Run(dtMs);
                return;
            }
            if (Speed == FxSpeed.Off)
            {
                bool was = Muted;
                Muted = true;
                try { Run((int)Math.Min(left, int.MaxValue / 2)); }
                finally { Muted = was; }
                lastEndMs = -1;
                Run(dtMs);
                return;
            }
            int pct = FxTune.FastPercent.Value;
            long fast = Math.Min((long)dtMs * 100 / pct, left);
            Run((int)fast);
            int real = (int)((fast * pct + 99) / 100);   // 대기열에 쓴 실시각(올림)
            if (dtMs > real) Run(dtMs - real);
        }

        void Run(int dtMs)
        {
            long start = NowMs;
            int left = dtMs;
            while (true)
            {
                if (Current == null)
                {
                    if (Paused || pending.Count == 0) break;
                    Current = pending.Dequeue();
                    LocalMs = 0;
                    entryStartMs = start + (dtMs - left);
                    Started?.Invoke(Current);
                    Sweep(-1, 0);
                }
                int step = Math.Min(left, Current.LengthMs - LocalMs);
                if (step > 0)
                {
                    Sweep(LocalMs, LocalMs + step);
                    LocalMs += step;
                    left -= step;
                }
                if (LocalMs < Current.LengthMs) break;
                // 길이를 넘는 자리에 단 단서도 항목이 끝날 때는 닿는다(표시 값이 보기와 어긋난 채 남지 않게)
                Sweep(LocalMs, int.MaxValue);
                Last = Current;
                lastEndMs = start + (dtMs - left);
                history.Add(Current);
                if (history.Count > HistoryMax) history.RemoveAt(0);
                Current = null;
                Finished?.Invoke(Last);
                if (IsEmpty)
                {
                    emptySinceMs = start + (dtMs - left);
                    Shown.Clear();
                }
            }
            NowMs = start + dtMs;
        }

        void Sweep(int prevMs, int nowMs)
        {
            swept.Clear();
            FxCues.Sweep(Current.Cues, prevMs, nowMs, swept);
            foreach (var cue in swept)
            {
                if (!cue.IsSound) Shown.Land(cue.Key, cue.After);
                else if (!Muted)
                {
                    CueMs = entryStartMs + Math.Min(Math.Max(0, cue.AtMs), Current.LengthMs);
                    CueSeq = Current.Seq;
                    Sound?.Invoke(cue);
                }
            }
        }
    }

    /// <summary>
    /// 시퀀서를 트윈 실행기(화면에 하나, 04 §8-6)에 거는 손잡이. 대기열에 일이 생기면 실행기에 등록되고(바쁨 +1), 대기열·토스트가 다 비면 빠진다(유휴 때 매 프레임 일 0).
    /// 화면은 <c>show</c>에서 지금 항목·시각을 읽어 부품마다 <c>Seek(step.Local(seq.LocalMs))</c>를 부른다.
    /// </summary>
    public sealed class FxPlayer : ITweenDriver
    {
        public readonly FxSequencer Sequencer;
        readonly TweenRunner runner;
        readonly Action<FxSequencer> show;
        bool driving;

        public FxPlayer(TweenRunner runner, FxSequencer sequencer, Action<FxSequencer> show)
        {
            this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
            Sequencer = sequencer ?? throw new ArgumentNullException(nameof(sequencer));
            this.show = show;
        }

        public void Enqueue(IEnumerable<FxEntry> entries)
        {
            Sequencer.Enqueue(entries);
            Wake();
        }

        public void Toast(string text)
        {
            Sequencer.Toast(text);
            Wake();
        }

        /// <summary>일시정지를 켜고 끈다. 풀 때 기다리던 항목이 있으면 실행기에 다시 건다 — <c>Sequencer.Paused</c>를 직접 바꾸면 서 있던 대기열이 깨어나지 않는다.</summary>
        public void SetPaused(bool paused)
        {
            Sequencer.Paused = paused;
            Wake();
        }

        void Wake()
        {
            if (driving || Sequencer.Idle || Sequencer.Stalled) return;
            driving = true;
            runner.Drive(this);
        }

        bool ITweenDriver.Advance(int dtMs)
        {
            Sequencer.Advance(dtMs);
            show?.Invoke(Sequencer);
            driving = !Sequencer.Idle && !Sequencer.Stalled;
            return driving;
        }
    }
}
