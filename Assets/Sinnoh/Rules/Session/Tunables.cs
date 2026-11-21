using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>Host = 방장 멈춤에 드는 값(판 시작 때 방장 값으로 손님을 맞춘다, 02 §3-2·§9) / Local = 내 화면에서만 쓰는 값.</summary>
    public enum TunableScope { Host, Local }

    /// <summary>조정 값 하나: 키·기본·최소·최대·범위. 값은 정수다(ms·개수).</summary>
    public sealed class Tunable
    {
        public readonly string Key;
        public readonly int Default, Min, Max;
        public readonly TunableScope Scope;

        /// <summary>지금 값. <see cref="Tunables.Set"/>으로만 바꾼다(최소·최대로 잘린다).</summary>
        public int Value { get; internal set; }

        internal Tunable(string key, int def, int min, int max, TunableScope scope)
        {
            Key = key;
            Default = def;
            Min = min;
            Max = max;
            Scope = scope;
            Value = def;
        }
    }

    /// <summary>전선 위의 조정 값 하나(키 · 값) — <see cref="RoomState.Tune"/>.</summary>
    public sealed class TuneValue
    {
        public string Key;
        public int Value;
    }

    /// <summary>
    /// 연출 조정 값 등록부(02 §9 Tunables 축소판, 04 §8-5): 수치는 두 곳에만 둔다 — <see cref="PlayTiming"/>(Host)과 화면 상수(Local, Sinnoh.Fx).
    /// 쓰는 쪽이 <see cref="Define"/>이 돌려준 <see cref="Tunable"/>을 들고 <see cref="Tunable.Value"/>를 읽는다. 조정 창·<c>tuning.json</c>은
    /// <see cref="Entries"/>·<see cref="Set"/>·<see cref="Apply"/> 위에 얹는다(아직 없다 — 화면 단계).
    /// Host 값을 바꾸면 방장·손님이 같은 값이어야 길이가 맞는다: 방장이 <see cref="Snapshot"/>(Host)을 보내고 손님이 <see cref="Apply"/>한다(M7 `RoomSettings`).
    /// 값을 바꾼 시험은 끝에 <see cref="Reset"/>한다(정적 등록부 <see cref="Shared"/>는 프로세스에 하나다).
    /// </summary>
    public sealed class Tunables
    {
        /// <summary>게임이 쓰는 등록부 하나. PlayTiming(Host)과 화면(Local)이 여기에 등록한다.</summary>
        public static readonly Tunables Shared = new Tunables();

        readonly List<Tunable> entries = new List<Tunable>();
        readonly Dictionary<string, Tunable> byKey = new Dictionary<string, Tunable>(StringComparer.Ordinal);

        /// <summary>등록한 순서 그대로.</summary>
        public IReadOnlyList<Tunable> Entries => entries;

        public Tunable Define(string key, int def, int min, int max, TunableScope scope)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("키가 비었다", nameof(key));
            if (min > max || def < min || def > max) throw new ArgumentOutOfRangeException(nameof(def), $"{key}: 기본 {def}가 [{min}, {max}] 밖");
            if (byKey.ContainsKey(key)) throw new ArgumentException("같은 키를 두 번 등록: " + key, nameof(key));
            var t = new Tunable(key, def, min, max, scope);
            entries.Add(t);
            byKey.Add(key, t);
            return t;
        }

        /// <summary>없으면 null.</summary>
        public Tunable Find(string key) => key != null && byKey.TryGetValue(key, out var t) ? t : null;

        /// <summary>값을 넣는다(최소·최대로 자른다). 모르는 키면 false.</summary>
        public bool Set(string key, int value)
        {
            var t = Find(key);
            if (t == null) return false;
            t.Value = Math.Max(t.Min, Math.Min(t.Max, value));
            return true;
        }

        /// <summary>모두 기본값으로.</summary>
        public void Reset()
        {
            foreach (var t in entries) t.Value = t.Default;
        }

        /// <summary>그 범위의 (키, 지금 값) 목록(등록 순). 방장이 손님에게 보내는 Host 값, 파일로 쓰는 값.</summary>
        public List<KeyValuePair<string, int>> Snapshot(TunableScope scope)
        {
            var list = new List<KeyValuePair<string, int>>();
            foreach (var t in entries)
                if (t.Scope == scope) list.Add(new KeyValuePair<string, int>(t.Key, t.Value));
            return list;
        }

        /// <summary>기본값과 다른 Host 값만(등록 순) — 방장이 판 시작 때 방 상태(<see cref="RoomState.Tune"/>)에 실어 손님에 맞춘다(02 §3-2 [가정 M7-73]). 손대지 않았으면 빈 목록.</summary>
        public List<TuneValue> HostChanges()
        {
            var list = new List<TuneValue>();
            foreach (var t in entries)
                if (t.Scope == TunableScope.Host && t.Value != t.Default) list.Add(new TuneValue { Key = t.Key, Value = t.Value });
            return list;
        }

        /// <summary>손님: Host 값을 방장 것에 맞춘다 — 모두 기본값으로 돌린 뒤 받은 것만 넣는다(Local 값은 그대로). 모르는 키·Local 키는 넣지 않고 그 수를 돌려준다.</summary>
        public int MatchHost(IEnumerable<TuneValue> values)
        {
            foreach (var t in entries)
                if (t.Scope == TunableScope.Host) t.Value = t.Default;
            int skipped = 0;
            if (values == null) return 0;
            foreach (var v in values)
            {
                var t = v != null ? Find(v.Key) : null;
                if (t == null || t.Scope != TunableScope.Host) skipped++;
                else Set(v.Key, v.Value);
            }
            return skipped;
        }

        /// <summary>(키, 값) 목록을 넣는다. <paramref name="scope"/> 밖 키와 모르는 키는 넣지 않고 그 수를 돌려준다(손님이 받은 Host 값에 Local 키가 섞여도 내 화면 값을 덮지 않는다).</summary>
        public int Apply(IEnumerable<KeyValuePair<string, int>> values, TunableScope scope)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            int skipped = 0;
            foreach (var kv in values)
            {
                var t = Find(kv.Key);
                if (t == null || t.Scope != scope) skipped++;
                else Set(kv.Key, kv.Value);
            }
            return skipped;
        }
    }
}
