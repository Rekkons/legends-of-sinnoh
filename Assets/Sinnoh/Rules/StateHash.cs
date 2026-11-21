using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// GameState를 정해진 필드 순서로 훑어 받는 쪽(01 §2-3). StateHash가 쓰고, 02 §1-1 StateStream(보기 델타)도 같은 훑기를 쓴다 [가정].
    /// 목록은 길이 다음 원소, null일 수 있는 객체는 있음 표시(0/1) 다음 내용, bool은 0/1, enum은 int로 받는다.
    /// </summary>
    public interface IStateWriter
    {
        void Int(int value);
        void ULong(ulong value);
    }

    /// <summary>
    /// FNV-1a 64비트(01 §0: string.GetHashCode·HashCode.Combine은 프로세스마다 다르다). int는 4바이트, ulong은 8바이트 리틀 엔디언으로 먹인다.
    /// </summary>
    public sealed class Fnv1a64 : IStateWriter
    {
        public const ulong OffsetBasis = 14695981039346656037UL;
        public const ulong Prime = 1099511628211UL;

        public ulong Value = OffsetBasis;

        public void Byte(byte b)
        {
            unchecked
            {
                Value ^= b;
                Value *= Prime;
            }
        }

        public void Int(int value)
        {
            uint u = unchecked((uint)value);
            for (int i = 0; i < 4; i++, u >>= 8) Byte((byte)u);
        }

        public void ULong(ulong value)
        {
            for (int i = 0; i < 8; i++, value >>= 8) Byte((byte)value);
        }
    }

    /// <summary>
    /// 상태 해시(01 §2-3): GameState.Walk 순서의 FNV-1a 64. 재현·리플레이(세이브 Checkpoints, 01 §10)·동기 확인에 쓴다.
    /// Viewer는 넣지 않는다. 훑는 순서를 바꾸면 해시가 바뀐다.
    /// </summary>
    public static class StateHash
    {
        public static ulong Of(GameState s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var h = new Fnv1a64();
            s.Walk(h);
            return h.Value;
        }
    }

    /// <summary>
    /// Walk가 쓴 순서 그대로 되읽는 쪽(02 §4-4 StateStream — 손님이 받은 스트림을 GameState로 되돌린다). Count는 목록 길이를 읽고 남은 값보다 크거나 음수면 FormatException.
    /// </summary>
    public interface IStateReader
    {
        int Int();
        ulong ULong();
        int Count();
    }

    /// <summary>스트림의 구역 표지(StateStream 쓰기·읽기만 — 해시는 모른다). GameState.Walk·Read가 같은 자리에서 부른다.</summary>
    internal interface IStateSections
    {
        void Section();
    }

    /// <summary>상태 조각 하나: 손으로 쓴 깊은 복제(01 §2-3 ②)와 해시 훑기·되읽기. 반사 시험(M3_복제는모든필드를복사 · M7_되읽기는모든필드)이 빠진 필드를 잡는다.</summary>
    internal interface IStatePart<T>
    {
        void CopyFrom(T other);
        void Walk(IStateWriter w);
        /// <summary>Walk가 쓴 값을 같은 순서로 읽어 이 객체를 덮어쓴다(목록 원소 객체는 다시 쓴다).</summary>
        void Read(IStateReader r);
    }

    /// <summary>CopyFrom·Walk가 같이 쓰는 목록·배열 도우미. 대상 목록의 원소 객체는 다시 쓴다(복제마다 할당을 줄인다).</summary>
    internal static class StateParts
    {
        public static void CopyInts(List<int> dst, List<int> src)
        {
            dst.Clear();
            dst.AddRange(src);
        }

        public static void CopyList<T>(List<T> dst, List<T> src) where T : class, IStatePart<T>, new()
        {
            int n = src.Count;
            if (dst.Count > n) dst.RemoveRange(n, dst.Count - n);
            for (int i = 0; i < n; i++)
            {
                if (i == dst.Count) dst.Add(new T());
                dst[i].CopyFrom(src[i]);
            }
        }

        /// <summary>길이가 다르면 새 배열. 같으면 dst에 덮어쓴다.</summary>
        public static int[] CopyArray(int[] dst, int[] src)
        {
            if (dst == null || dst.Length != src.Length) dst = new int[src.Length];
            Array.Copy(src, dst, src.Length);
            return dst;
        }

        public static bool[] CopyArray(bool[] dst, bool[] src)
        {
            if (dst == null || dst.Length != src.Length) dst = new bool[src.Length];
            Array.Copy(src, dst, src.Length);
            return dst;
        }

        /// <summary>src가 null이면 null, 아니면 dst를 다시 쓰거나 새로 만들어 복사한다.</summary>
        public static T CopyNullable<T>(T dst, T src) where T : class, IStatePart<T>, new()
        {
            if (src == null) return null;
            if (dst == null) dst = new T();
            dst.CopyFrom(src);
            return dst;
        }

        public static void Bool(IStateWriter w, bool v) => w.Int(v ? 1 : 0);

        public static void Ints(IStateWriter w, List<int> list)
        {
            w.Int(list.Count);
            for (int i = 0; i < list.Count; i++) w.Int(list[i]);
        }

        public static void Ints(IStateWriter w, int[] a)
        {
            w.Int(a.Length);
            for (int i = 0; i < a.Length; i++) w.Int(a[i]);
        }

        public static void Bools(IStateWriter w, bool[] a)
        {
            w.Int(a.Length);
            for (int i = 0; i < a.Length; i++) w.Int(a[i] ? 1 : 0);
        }

        public static void List<T>(IStateWriter w, List<T> list) where T : class, IStatePart<T>
        {
            w.Int(list.Count);
            for (int i = 0; i < list.Count; i++) list[i].Walk(w);
        }

        public static void Nullable<T>(IStateWriter w, T part) where T : class, IStatePart<T>
        {
            if (part == null)
            {
                w.Int(0);
                return;
            }
            w.Int(1);
            part.Walk(w);
        }

        /// <summary>스트림 구역의 경계(02 §4-4). 해시(Fnv1a64)에는 아무것도 넣지 않는다.</summary>
        public static void Section(IStateWriter w) => (w as IStateSections)?.Section();

        public static void Section(IStateReader r) => (r as IStateSections)?.Section();

        // ── 되읽기(Walk의 거울) ──

        public static bool ReadBool(IStateReader r) => r.Int() != 0;

        public static void ReadInts(IStateReader r, List<int> dst)
        {
            int n = r.Count();
            dst.Clear();
            for (int i = 0; i < n; i++) dst.Add(r.Int());
        }

        /// <summary>길이가 다르면 새 배열, 같으면 dst에 덮어쓴다.</summary>
        public static int[] ReadArray(IStateReader r, int[] dst)
        {
            int n = r.Count();
            if (dst == null || dst.Length != n) dst = new int[n];
            for (int i = 0; i < n; i++) dst[i] = r.Int();
            return dst;
        }

        public static bool[] ReadBools(IStateReader r, bool[] dst)
        {
            int n = r.Count();
            if (dst == null || dst.Length != n) dst = new bool[n];
            for (int i = 0; i < n; i++) dst[i] = r.Int() != 0;
            return dst;
        }

        public static void ReadList<T>(IStateReader r, List<T> dst) where T : class, IStatePart<T>, new()
        {
            int n = r.Count();
            if (dst.Count > n) dst.RemoveRange(n, dst.Count - n);
            for (int i = 0; i < n; i++)
            {
                if (i == dst.Count) dst.Add(new T());
                dst[i].Read(r);
            }
        }

        /// <summary>있음 표시가 0이면 null, 1이면 dst를 다시 쓰거나 새로 만들어 읽는다. 그 밖의 값은 FormatException.</summary>
        public static T ReadNullable<T>(IStateReader r, T dst) where T : class, IStatePart<T>, new()
        {
            int flag = r.Int();
            if (flag == 0) return null;
            if (flag != 1) throw new FormatException($"있음 표시가 0/1이 아니다({flag})");
            if (dst == null) dst = new T();
            dst.Read(r);
            return dst;
        }
    }
}
