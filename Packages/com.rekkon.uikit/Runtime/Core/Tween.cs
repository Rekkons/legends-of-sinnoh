using System;
using System.Collections.Generic;

namespace Rekkon.UiKit
{
    /// <summary>
    /// 연출 부품: 시각(ms, 부품이 시작한 때부터) → 겉모양. 같은 ms로 두 번 부르면 같은 결과여야 한다(실시각·앞 호출에 기대지 않는다) —
    /// 그래야 캡처가 아무 시각으로 바로 가서 찍는다. UI 부품은 transform·opacity만 바꾼다(크기·자리는 만들 때 한 번).
    /// </summary>
    public interface ISeekable
    {
        void Seek(int ms);
    }

    /// <summary>끝을 스스로 아는 연출(대기열 같은 것): 실행기가 흐른 시간을 넘겨주고, false를 돌려주면 끝난 것으로 보고 뺀다.</summary>
    public interface ITweenDriver
    {
        bool Advance(int dtMs);
    }

    public enum Ease { Linear, InQuad, OutQuad, InOutQuad, OutCubic }

    /// <summary>트윈 시간 계산(순수 식). 시간은 정수 ms, 진행은 0~1.</summary>
    public static class Tween
    {
        /// <summary>구간 [startMs, startMs + lengthMs] 안에서 ms의 진행(0~1, 밖은 잘라 붙인다). 길이 0 이하면 시작 뒤는 1.</summary>
        public static float Progress(int ms, int startMs, int lengthMs)
        {
            if (ms <= startMs) return lengthMs <= 0 && ms == startMs ? 1f : 0f;
            if (lengthMs <= 0 || ms >= startMs + lengthMs) return 1f;
            return (ms - startMs) / (float)lengthMs;
        }

        public static float Apply(Ease ease, float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            switch (ease)
            {
                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return 1f - (1f - t) * (1f - t);
                case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
                case Ease.OutCubic: return 1f - (1f - t) * (1f - t) * (1f - t);
                default: return t;
            }
        }

        /// <summary>from → to를 구간 [startMs, startMs + lengthMs]에서 이징으로 잇는다.</summary>
        public static float At(int ms, int startMs, int lengthMs, float from, float to, Ease ease = Ease.Linear) =>
            from + (to - from) * Apply(ease, Progress(ms, startMs, lengthMs));
    }

    /// <summary>
    /// 트윈 실행기. 화면에 하나만 두고 움직이는 것은 모두 여기에 등록한다 — <see cref="Busy"/> 하나로 「연출 중」을 센다(유휴 프레임 낮추기 판단).
    /// 시계는 밖에서 넣는다(<see cref="Advance"/>에 흐른 ms) — 실시각을 읽지 않는다. 등록된 것이 없으면 <see cref="Advance"/>는 아무 일도 하지 않는다.
    /// 세 가지: <see cref="Play"/>(길이가 정해진 부품, 끝나면 빠짐) · <see cref="Drive"/>(끝을 스스로 아는 것) · <see cref="Ambient"/>(늘 도는 장식 — 바쁨으로 세지 않는다).
    /// </summary>
    public sealed class TweenRunner
    {
        sealed class Slot
        {
            public int Id;
            public ISeekable Part;
            public ITweenDriver Driver;
            public int LengthMs;     // Play만. Ambient는 −1
            public long ElapsedMs;   // 등록 뒤 흐른 시간 − 지연
            public bool Dead;
        }

        readonly List<Slot> slots = new List<Slot>();
        int nextId = 1;
        bool advancing;

        /// <summary>끝이 있는 등록 수(<see cref="Play"/> + <see cref="Drive"/>). 0이면 유휴다. 늘 도는 장식은 세지 않는다.</summary>
        public int Busy { get; private set; }

        /// <summary>등록된 수(장식 포함).</summary>
        public int Count
        {
            get
            {
                int n = 0;
                foreach (var s in slots) if (!s.Dead) n++;
                return n;
            }
        }

        /// <summary>길이가 정해진 부품을 돌린다: 지연 뒤 0부터 <paramref name="lengthMs"/>까지 Seek하고, 끝에서 Seek(lengthMs)를 한 번 부른 뒤 뺀다. 등록할 때 Seek(0)을 부른다.</summary>
        public int Play(ISeekable part, int lengthMs, int delayMs = 0)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            if (lengthMs < 0) throw new ArgumentOutOfRangeException(nameof(lengthMs));
            if (delayMs < 0) throw new ArgumentOutOfRangeException(nameof(delayMs));
            part.Seek(0);
            Busy++;
            return Add(new Slot { Part = part, LengthMs = lengthMs, ElapsedMs = -delayMs });
        }

        /// <summary>끝을 스스로 아는 것을 돌린다. <see cref="ITweenDriver.Advance"/>가 false를 돌려줄 때까지 바쁨으로 센다.</summary>
        public int Drive(ITweenDriver driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            Busy++;
            return Add(new Slot { Driver = driver });
        }

        /// <summary>늘 도는 장식(숨쉬기 같은 것): <see cref="Stop"/>할 때까지 등록 뒤 흐른 ms로 Seek한다. 바쁨으로 세지 않는다.</summary>
        public int Ambient(ISeekable part)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            part.Seek(0);
            return Add(new Slot { Part = part, LengthMs = -1 });
        }

        int Add(Slot s)
        {
            s.Id = nextId++;
            slots.Add(s);
            return s.Id;
        }

        /// <summary>등록을 뺀다(끝 모양으로 보내지 않는다). 이미 빠졌으면 false.</summary>
        public bool Stop(int id)
        {
            foreach (var s in slots)
            {
                if (s.Id != id || s.Dead) continue;
                Kill(s);
                if (!advancing) slots.Remove(s);
                return true;
            }
            return false;
        }

        void Kill(Slot s)
        {
            s.Dead = true;
            if (s.Driver != null || s.LengthMs >= 0) Busy--;
        }

        /// <summary>시간을 <paramref name="dtMs"/>만큼 보낸다. 도는 중에 새로 등록된 것은 다음 호출부터 돈다.</summary>
        public void Advance(int dtMs)
        {
            if (dtMs < 0) throw new ArgumentOutOfRangeException(nameof(dtMs));
            if (slots.Count == 0) return;
            if (advancing) throw new InvalidOperationException("TweenRunner.Advance 안에서 다시 부를 수 없다");
            advancing = true;
            try
            {
                int n = slots.Count;
                for (int i = 0; i < n; i++)
                {
                    var s = slots[i];
                    if (s.Dead) continue;
                    if (s.Driver != null)
                    {
                        if (!s.Driver.Advance(dtMs) && !s.Dead) Kill(s);
                        continue;
                    }
                    s.ElapsedMs += dtMs;
                    if (s.ElapsedMs < 0) continue;                       // 아직 지연 중
                    if (s.LengthMs < 0) s.Part.Seek((int)Math.Min(s.ElapsedMs, int.MaxValue));
                    else if (s.ElapsedMs >= s.LengthMs)
                    {
                        s.Part.Seek(s.LengthMs);
                        if (!s.Dead) Kill(s);
                    }
                    else s.Part.Seek((int)s.ElapsedMs);
                }
            }
            finally
            {
                advancing = false;
                slots.RemoveAll(s => s.Dead);
            }
        }
    }
}
