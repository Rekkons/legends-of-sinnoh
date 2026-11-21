using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 상태 스트림(02 §1-1 · §4-4): GameState를 <see cref="GameState.Walk"/> 순서의 int 열로 펴고 되읽는다. 해시(StateHash)와 같은 훑기라
    /// <see cref="Hash"/>(스트림의 FNV-1a 64) = <see cref="StateHash.Of"/>(그 상태)다 — ulong은 낮은 int · 높은 int 둘로 펴서 같은 8바이트가 들어간다.
    /// 스트림은 구역(int[]) 열이다: 경계는 Walk의 <c>StateParts.Section</c> 자리(머리 · 좌석마다 · 토큰 · 덱·버림 더미마다 · 포켓몬 · 캠페인 · 배틀 · 선택 · 기타 · 효과 · 사용 기록 · 반응 창 · 꼬리).
    /// 구역 수는 좌석 수와 덱 수로만 정해져 판 동안 바뀌지 않는다 — 델타(<see cref="StreamPatch"/>)가 구역 번호로 가리킨다.
    /// 스트림에는 Viewer·해시 밖 값(Options.FixedEffectOrder)이 없다. 보기(ViewFor)에서 만들면 Rng도 0이다(01 §4).
    /// </summary>
    public static class StateStream
    {
        public static int[][] Of(GameState s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var w = new Writer();
            s.Walk(w);
            return w.Finish();
        }

        /// <summary>
        /// 스트림을 GameState로 되읽는다(Viewer = viewer). into가 있으면 그것을 덮어쓴다(목록 원소 객체를 다시 쓴다), 없으면 새 상태.
        /// 모양이 틀리면(구역 경계 · 길이 · 남은 값) FormatException — 그때 into는 반쯤 바뀌어 있다.
        /// </summary>
        public static GameState Read(int[][] sections, int viewer, GameState into = null)
        {
            if (sections == null) throw new ArgumentNullException(nameof(sections));
            var r = new Reader(sections);
            var s = into ?? new GameState();
            s.Read(r);
            r.End();
            s.Viewer = viewer;
            return s;
        }

        /// <summary>스트림의 FNV-1a 64 = 그 상태의 StateHash.</summary>
        public static ulong Hash(int[][] sections)
        {
            var h = new Fnv1a64();
            foreach (var sec in sections)
                for (int i = 0; i < sec.Length; i++) h.Int(sec[i]);
            return h.Value;
        }

        /// <summary>스트림의 int 수(구역 합).</summary>
        public static int Length(int[][] sections)
        {
            int n = 0;
            foreach (var sec in sections) n += sec.Length;
            return n;
        }

        sealed class Writer : IStateWriter, IStateSections
        {
            readonly List<int[]> done = new List<int[]>();
            readonly List<int> cur = new List<int>(512);

            public void Int(int value) => cur.Add(value);

            public void ULong(ulong value)
            {
                cur.Add(unchecked((int)(uint)value));
                cur.Add(unchecked((int)(uint)(value >> 32)));
            }

            public void Section()
            {
                done.Add(cur.ToArray());
                cur.Clear();
            }

            public int[][] Finish()
            {
                Section();
                return done.ToArray();
            }
        }

        sealed class Reader : IStateReader, IStateSections
        {
            readonly int[][] s;
            int sec, pos;

            public Reader(int[][] sections)
            {
                s = sections;
                if (s.Length == 0) throw new FormatException("빈 스트림");
                foreach (var x in s)
                    if (x == null) throw new FormatException("빈 구역(null)");
            }

            public int Int()
            {
                if (pos >= s[sec].Length) throw new FormatException($"구역 {sec}을 넘어 읽는다({pos}) — 되읽기와 훑기가 어긋났다");
                return s[sec][pos++];
            }

            public ulong ULong()
            {
                uint lo = unchecked((uint)Int());
                uint hi = unchecked((uint)Int());
                return lo | ((ulong)hi << 32);
            }

            public int Count()
            {
                int n = Int();
                if (n < 0 || n > s[sec].Length - pos) throw new FormatException($"구역 {sec}의 목록 길이 {n}이 틀렸다(남은 값 {s[sec].Length - pos})");
                return n;
            }

            public void Section()
            {
                if (pos != s[sec].Length) throw new FormatException($"구역 {sec}이 {pos}/{s[sec].Length}에서 끝났다 — 되읽기와 훑기가 어긋났다");
                if (sec + 1 >= s.Length) throw new FormatException($"구역이 모자란다({s.Length})");
                sec++;
                pos = 0;
            }

            public void End()
            {
                if (sec != s.Length - 1 || pos != s[sec].Length) throw new FormatException($"스트림이 남았다(구역 {sec}/{s.Length}, {pos})");
            }
        }
    }

    /// <summary>
    /// 스트림 델타(02 §4-4 Patch, 코덱 = 02 §11 #9 [가정 M7-14]): 구역마다 앞뒤 같은 곳을 떼고, 가운데 길이가 같으면 자리끼리 · 다르면 Myers 차이(최대 편집 <see cref="MaxEdits"/>, 넘으면 가운데를 통째로)로
    /// 바뀐 덩이를 찾는다. 짧은 같은 줄(<see cref="MergeRun"/> 이하)은 앞뒤 덩이에 합친다(덩이 머리가 4 int라).
    /// 모양(평평한 int 열): 덩이마다 [구역, 옛 자리, 지울 수, 넣을 수, 넣을 값…] — 구역 오름차순, 한 구역 안에서는 옛 자리 오름차순이고 겹치지 않는다.
    /// 숨은 정보: 차이는 두 보기 스트림만 보고 만든다 — 보기에 없는 값은 패치에도 없다.
    /// </summary>
    public static class StreamPatch
    {
        public const int MaxEdits = 128;
        public const int MergeRun = 4;

        /// <summary>from → to 패치. 구역 수가 다르면 ArgumentException(스냅샷을 보낸다).</summary>
        public static int[] Diff(int[][] from, int[][] to)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (from.Length != to.Length) throw new ArgumentException($"구역 수가 다르다({from.Length} ≠ {to.Length})");
            var patch = new List<int>();
            var runs = new List<int>();
            for (int i = 0; i < from.Length; i++) DiffSection(i, from[i], to[i], patch, runs);
            return patch.ToArray();
        }

        /// <summary>패치를 적용한 새 스트림(바뀌지 않은 구역 배열은 같이 쓴다 — 구역은 읽기만 한다). 모양이 틀리면 FormatException.</summary>
        public static int[][] Apply(int[][] from, int[] patch)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (patch == null) throw new ArgumentNullException(nameof(patch));
            var to = (int[][])from.Clone();
            var buf = new List<int>();
            int i = 0, lastSec = -1;
            while (i < patch.Length)
            {
                int sec = At(patch, i);
                if (sec < 0 || sec >= from.Length || sec <= lastSec) throw new FormatException($"패치 {i}: 구역 {sec}이 틀렸다");
                lastSec = sec;
                var old = from[sec];
                buf.Clear();
                int cursor = 0;
                while (i < patch.Length && patch[i] == sec)
                {
                    int at = At(patch, i + 1), remove = At(patch, i + 2), count = At(patch, i + 3);
                    if (at < cursor || remove < 0 || count < 0 || at + remove > old.Length || i + 4 + count > patch.Length)
                        throw new FormatException($"패치 {i}: 구역 {sec}의 덩이({at}, {remove}, {count})가 틀렸다");
                    for (int k = cursor; k < at; k++) buf.Add(old[k]);
                    for (int k = 0; k < count; k++) buf.Add(patch[i + 4 + k]);
                    cursor = at + remove;
                    i += 4 + count;
                }
                for (int k = cursor; k < old.Length; k++) buf.Add(old[k]);
                to[sec] = buf.ToArray();
            }
            return to;
        }

        static int At(int[] patch, int i)
        {
            if (i >= patch.Length) throw new FormatException("패치가 잘렸다");
            return patch[i];
        }

        static void DiffSection(int sec, int[] a, int[] b, List<int> patch, List<int> runs)
        {
            int la = a.Length, lb = b.Length, lim = Math.Min(la, lb);
            int p = 0;
            while (p < lim && a[p] == b[p]) p++;
            if (p == la && p == lb) return;
            int q = 0;
            while (q < lim - p && a[la - 1 - q] == b[lb - 1 - q]) q++;
            int n = la - p - q, m = lb - p - q;
            runs.Clear();
            if (n == m) Positional(a, b, p, n, runs);
            else if (!Myers(a, b, p, n, m, runs)) runs.Clear();
            int cx = 0, cy = 0;
            for (int r = 0; r < runs.Count; r += 3)
            {
                int x = runs[r], y = runs[r + 1], len = runs[r + 2];
                if (len <= MergeRun) continue;
                if (x > cx || y > cy) Hunk(sec, b, p, cx, x, cy, y, patch);
                cx = x + len;
                cy = y + len;
            }
            if (cx < n || cy < m) Hunk(sec, b, p, cx, n, cy, m, patch);
        }

        /// <summary>덩이 하나: 옛 [p+x0, p+x1)을 새 [p+y0, p+y1)로.</summary>
        static void Hunk(int sec, int[] b, int p, int x0, int x1, int y0, int y1, List<int> patch)
        {
            patch.Add(sec);
            patch.Add(p + x0);
            patch.Add(x1 - x0);
            patch.Add(y1 - y0);
            for (int k = y0; k < y1; k++) patch.Add(b[p + k]);
        }

        /// <summary>같은 길이: 자리끼리 같은 줄(x, y, 길이)을 모은다.</summary>
        static void Positional(int[] a, int[] b, int p, int n, List<int> runs)
        {
            int i = 0;
            while (i < n)
            {
                if (a[p + i] != b[p + i])
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < n && a[p + i] == b[p + i]) i++;
                runs.Add(start);
                runs.Add(start);
                runs.Add(i - start);
            }
        }

        /// <summary>
        /// Myers O((N+M)D) 차이(a[p..p+n) → b[p..p+m)): 같은 줄(x, y, 길이 — 가운데 좌표, 오름차순)을 runs에. 편집이 MaxEdits를 넘으면 false.
        /// </summary>
        static bool Myers(int[] a, int[] b, int p, int n, int m, List<int> runs)
        {
            int max = Math.Min(n + m, MaxEdits);
            int off = max + 1;
            var v = new int[2 * max + 3];
            for (int i = 0; i < v.Length; i++) v[i] = Off;
            var trace = new List<int[]>();
            int found = -1;
            for (int d = 0; d <= max && found < 0; d++)
            {
                trace.Add((int[])v.Clone());
                for (int k = -d; k <= d; k += 2)
                {
                    int x = Step(v, off, d, k, n, m, out _);
                    if (x == Off)
                    {
                        v[off + k] = Off;
                        continue;
                    }
                    int y = x - k;
                    while (x < n && y < m && a[p + x] == b[p + y])
                    {
                        x++;
                        y++;
                    }
                    v[off + k] = x;
                    if (x == n && y == m)
                    {
                        found = d;
                        break;
                    }
                }
            }
            if (found < 0) return false;
            var rev = new List<int>();
            int cx = n, cy = m;
            for (int d = found; d >= 0; d--)
            {
                int k = cx - cy;
                var vd = trace[d];
                int sx = Step(vd, off, d, k, n, m, out bool down);
                int sy = sx - k;
                if (cx > sx)
                {
                    rev.Add(cx - sx);
                    rev.Add(sy);
                    rev.Add(sx);
                }
                if (d == 0) break;
                int prevK = down ? k + 1 : k - 1;
                cx = vd[off + prevK];
                cy = cx - prevK;
            }
            for (int i = rev.Count - 3; i >= 0; i -= 3)
            {
                runs.Add(rev[i + 2]);
                runs.Add(rev[i + 1]);
                runs.Add(rev[i]);
            }
            return true;
        }

        /// <summary>닿지 못한 대각선(격자 밖으로 나간 길은 버린다).</summary>
        const int Off = int.MinValue / 4;

        /// <summary>
        /// d번째 편집으로 대각선 k에 들어선 자리(뱀 앞, x) — 앞 대각선 k+1에서 아래로(down, x 그대로) 또는 k−1에서 오른쪽으로(x+1). 격자 안의 둘 가운데 x가 큰 쪽, 같으면 아래.
        /// 앞으로 갈 때와 되짚을 때 같은 값(그 차례 시작의 v)으로 부른다. 둘 다 격자 밖이면 Off.
        /// </summary>
        static int Step(int[] v, int off, int d, int k, int n, int m, out bool down)
        {
            down = true;
            if (d == 0) return 0;
            int xr = Off, xd = Off;
            if (k > -d && v[off + k - 1] != Off)
            {
                int x = v[off + k - 1] + 1;
                if (x <= n && x - k >= 0 && x - k <= m) xr = x;
            }
            if (k < d && v[off + k + 1] != Off)
            {
                int x = v[off + k + 1];
                if (x <= n && x - k >= 0 && x - k <= m) xd = x;
            }
            if (xd != Off && xd >= xr) return xd;
            down = false;
            return xr;
        }
    }
}
