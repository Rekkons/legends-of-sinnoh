using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>연출 층 다섯(04 §8-2). 모두 한 시간축(<see cref="FxSequencer"/>) 아래에서 돈다.</summary>
    public enum FxLayer
    {
        /// <summary>① 차단 컷신(until-end): 범위 안 좌석은 끝까지.</summary>
        Block = 1,
        /// <summary>② 띠: 턴 결과 요약·넘김 띠·내 차례 컷신. ①이 돌면 기다린다.</summary>
        Band = 2,
        /// <summary>③ 대기열 항목의 단계(보드·배틀 액션).</summary>
        Item = 3,
        /// <summary>④ 장식(칩·맥박·숫자): 부모 항목의 시각을 따른다. 항목 길이에 들지 않는다.</summary>
        Deco = 4,
        /// <summary>⑤ 토스트·기록 줄.</summary>
        Toast = 5,
    }

    /// <summary>항목 안 단계 하나: 04 §8-8 행(× 10 + 단계) 하나가 항목 시간축에서 차지하는 구간. 부품은 <c>Seek(항목 시각 − StartMs)</c>로 그린다.</summary>
    public sealed class FxStep
    {
        public int Row;
        public FxLayer Layer;
        public int StartMs;
        public int Ms;
        /// <summary>이 단계에 닿은 이벤트(같은 행에 여럿이면 순서대로). 장식은 비어 있을 수 있다.</summary>
        public readonly List<GameEvent> Events = new List<GameEvent>();
        /// <summary>장식을 붙인 쪽이 넣는 꼬리표(어느 부품인지).</summary>
        public object Tag;

        public int EndMs => StartMs + Ms;

        /// <summary>항목 시각 → 이 단계 안 시각(0 ~ Ms로 잘라 붙인다). 지난 단계는 끝 모양, 아직 안 온 단계는 처음 모양이 된다.</summary>
        public int Local(int entryMs) => Math.Max(0, Math.Min(Ms, entryMs - StartMs));
    }

    public enum ShownKind { Hp, Fatigue, Res }

    /// <summary>표시 값 하나의 이름(04 §8-3): 포켓몬의 HP·피로 칸(Id = 포켓몬 인스턴스), 좌석 자원(Id = 좌석, Res, Key = 아이템·유물 키).</summary>
    public readonly struct ShownKey : IEquatable<ShownKey>
    {
        public readonly ShownKind Kind;
        public readonly int Id;
        public readonly ResKind Res;
        public readonly int Key;

        ShownKey(ShownKind kind, int id, ResKind res, int key)
        {
            Kind = kind;
            Id = id;
            Res = res;
            Key = key;
        }

        public static ShownKey Hp(int pokemonId) => new ShownKey(ShownKind.Hp, pokemonId, 0, 0);
        public static ShownKey Fatigue(int pokemonId) => new ShownKey(ShownKind.Fatigue, pokemonId, 0, 0);
        public static ShownKey Resource(int seat, ResKind res, int key) => new ShownKey(ShownKind.Res, seat, res, key);

        /// <summary>그 좌석의 운명 손패 장수(카드 키 −1 — 카드마다의 자원 값과 섞이지 않는다).</summary>
        public static ShownKey FateHand(int seat) => Resource(seat, ResKind.Fate, -1);

        public bool Equals(ShownKey o) => Kind == o.Kind && Id == o.Id && Res == o.Res && Key == o.Key;
        public override bool Equals(object obj) => obj is ShownKey k && Equals(k);
        public override int GetHashCode() => (((int)Kind * 31 + Id) * 31 + (int)Res) * 31 + Key;
        public override string ToString() => Kind == ShownKind.Res ? $"Res({Id}, {Res}, {Key})" : $"{Kind}({Id})";
    }

    /// <summary>
    /// 연출 단서: 항목 시각 <see cref="AtMs"/>에 닿는 일 하나. 표시 값(<see cref="Sound"/> == null) = 그 값이 Before → After로 바뀌는 착지 순간(04 §8-3),
    /// 소리(<see cref="Sound"/> != null) = 그 키의 소리를 내는 키 프레임(04 §8-4).
    /// </summary>
    public struct FxCue
    {
        public int AtMs;
        public string Sound;
        /// <summary>소리 단서를 단 행(04 §8-8 행 × 10 + 단계 — 단서 시각 로그용, 행 밖에서 단 것은 0).</summary>
        public int Row;
        public ShownKey Key;
        public int Before, After;

        public bool IsSound => Sound != null;
    }

    /// <summary>
    /// 대기열 항목 하나 = 원인 행동 하나(<c>Seq</c>, 04 §8-1 P3). 시간축: 단계가 이벤트 순서대로 이어지고(차단 컷신 포함), 끝에 보기 시간이 남으면 멈춘 화면으로 채운다.
    /// 길이 <see cref="LengthMs"/> = <see cref="QueueMs"/> + <see cref="BlockMs"/> — 방장과 같은 함수(<c>PlayTiming.Items</c>)가 센 값이다.
    /// <see cref="StepsMs"/>·<see cref="BlockStepsMs"/>는 화면이 따로 분류해 놓은 단계의 합이고, 방장 값과 같아야 한다(시험 M4_연출길이방장화면같음).
    /// </summary>
    public sealed class FxEntry
    {
        public int Seq;
        public int Seat;
        public ActionKind Kind;
        public AutoKind Auto;
        /// <summary>이어 받는 앞 행동의 Seq(선택을 사이에 둔 같은 행동의 뒷부분). 없으면 −1. 앞 항목의 마지막 화면에서 시작한다(04 §8-2).</summary>
        public int Continues = -1;
        /// <summary>반응한 행동의 Seq. 없으면 −1. 그 항목의 화면 위에 얹는다.</summary>
        public int ReactsTo = -1;

        /// <summary>이 항목의 이벤트(행이 없는 것 포함, 받은 순서).</summary>
        public readonly List<GameEvent> Events = new List<GameEvent>();
        /// <summary>①②③ 단계(시작 시각 순, 서로 겹치지 않는다).</summary>
        public readonly List<FxStep> Steps = new List<FxStep>();
        /// <summary>④ 장식(부모 항목의 시각을 따른다, 길이에 들지 않는다).</summary>
        public readonly List<FxStep> Decos = new List<FxStep>();
        /// <summary>단서(시각 순, 같은 시각은 넣은 순).</summary>
        public readonly List<FxCue> Cues = new List<FxCue>();

        /// <summary>방장과 같은 함수가 센 대기열 길이(max(단계 합, 보기 시간))와 차단 컷신 길이.</summary>
        public int QueueMs, BlockMs;

        /// <summary>이벤트가 화면에 드러나는 시각의 표(<see cref="FxReveal.At"/>가 처음 물을 때 채운다 — 단계를 고치면 null로 비운다).</summary>
        internal Dictionary<GameEvent, int> Reveal;

        public int LengthMs => QueueMs + BlockMs;

        /// <summary>화면이 분류한 ②③ 단계의 합.</summary>
        public int StepsMs
        {
            get
            {
                int ms = 0;
                foreach (var s in Steps) if (s.Layer != FxLayer.Block) ms += s.Ms;
                return ms;
            }
        }

        /// <summary>화면이 분류한 ① 단계의 합.</summary>
        public int BlockStepsMs
        {
            get
            {
                int ms = 0;
                foreach (var s in Steps) if (s.Layer == FxLayer.Block) ms += s.Ms;
                return ms;
            }
        }

        /// <summary>그 행의 단계(없으면 null).</summary>
        public FxStep Step(int row)
        {
            foreach (var s in Steps) if (s.Row == row) return s;
            return null;
        }

        /// <summary>장식을 붙인다(④): 항목 시각 startMs부터 ms 동안. 길이는 바뀌지 않는다.</summary>
        public FxStep AddDeco(int startMs, int ms, object tag)
        {
            var d = new FxStep { Layer = FxLayer.Deco, StartMs = startMs, Ms = ms, Tag = tag };
            Decos.Add(d);
            return d;
        }

        /// <summary>소리 단서를 단다(04 §8-4: 이벤트를 받을 때가 아니라 연출 시간표의 키 프레임에).</summary>
        public void AddSound(int atMs, string sound, int row = 0)
        {
            if (sound == null) throw new ArgumentNullException(nameof(sound));
            AddCue(new FxCue { AtMs = atMs, Sound = sound, Row = row });
        }

        /// <summary>
        /// 표시 값 단서를 단다: atMs에 key가 before → after로 바뀐다. 같은 키의 단서는 단 순서(= 이벤트 순서)대로 닿는다 — 먼저 단 단서보다 이른 시각이면
        /// 그 단서의 시각으로 늦춘다(동시 피해 묶음에서 독 피해 뒤에 온 공격 피해처럼 단계 순서와 이벤트 순서가 어긋날 때 마지막 값이 틀어지지 않게).
        /// </summary>
        public void AddShown(int atMs, ShownKey key, int before, int after)
        {
            for (int i = Cues.Count - 1; i >= 0; i--)
            {
                if (Cues[i].IsSound || !Cues[i].Key.Equals(key)) continue;
                if (Cues[i].AtMs > atMs) atMs = Cues[i].AtMs;
                break;
            }
            AddCue(new FxCue { AtMs = atMs, Key = key, Before = before, After = after });
        }

        void AddCue(FxCue cue)
        {
            if (cue.AtMs < 0) cue.AtMs = 0;
            int i = Cues.Count;
            while (i > 0 && Cues[i - 1].AtMs > cue.AtMs) i--;   // 같은 시각은 넣은 순
            Cues.Insert(i, cue);
        }
    }

    public static class FxCues
    {
        /// <summary>
        /// 구간 훑기(04 §8-4): 시각 순 단서에서 (prevMs, nowMs] 안의 것을 순서대로 <paramref name="into"/>에 더한다. 매 프레임 「지난 시각 → 지금」으로 부른다 —
        /// 시작 때 한 번 예약하지 않으므로 시계가 멈추거나 건너뛰어도 단서가 그 시계를 따른다. 항목을 시작할 때는 prevMs = −1(시각 0의 단서 포함).
        /// </summary>
        public static void Sweep(IReadOnlyList<FxCue> cues, int prevMs, int nowMs, List<FxCue> into)
        {
            if (cues == null) throw new ArgumentNullException(nameof(cues));
            if (into == null) throw new ArgumentNullException(nameof(into));
            for (int i = 0; i < cues.Count; i++)
            {
                int at = cues[i].AtMs;
                if (at > nowMs) break;
                if (at > prevMs) into.Add(cues[i]);
            }
        }
    }
}
