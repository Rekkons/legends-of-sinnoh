using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>굴림판(세계 좌표, 주사위 한 변 = 1 — spikes/dice-roll §3·§5). 굽기·재생·그리기가 같이 쓴다. 등급이 달라도 판의 세계 크기는 같다(녹화본 공용).</summary>
    public static class DiceGeo
    {
        /// <summary>내려다보는 각(도).</summary>
        public const float PitchDeg = 68f;
        /// <summary>판 폭(x 0 ~ Width) = (206 − 3 − 3) ÷ 16.</summary>
        public const float Width = 12.5f;
        /// <summary>판 깊이(z 0 ~ Depth) = (62 − 3 − 18) ÷ (16 × sin 68°) ≈ 2.764.</summary>
        public static readonly float Depth = 41f / (16f * (float)Math.Sin(PitchDeg * Math.PI / 180.0));
        public const int MinDice = 1, MaxDice = 9;
    }

    /// <summary>
    /// 녹화본 한 벌(spikes/dice-roll §3 「녹화 형식」): 60 Hz로 적은 주사위 n개의 위치·회전. 재생은 위치 선형·회전 구면 보간, 실행 중 물리 없음.
    /// 굴림 머리 11바이트(주사위 수 1 · 프레임 수 2 · 간격 초 4 · 멈춤 시각 초 4) + 주사위·프레임당 10바이트(위치 16비트 × 3, 범위 −16~48 · x = 0이면 아직 안 던짐,
    /// 회전 32비트 = 가장 큰 성분 번호 2비트 + 나머지 10비트 × 3).
    /// </summary>
    public sealed class DiceClip
    {
        public const int HeadBytes = 11, DieFrameBytes = 10;

        public int Dice, Frames;
        public float FrameSec, SettleSec;
        /// <summary>[프레임 × Dice + 주사위]. 아직 안 던진 주사위는 <see cref="Thrown"/>이 false.</summary>
        public V3[] Pos;
        public Q4[] Rot;
        public bool[] Thrown;

        /// <summary>녹화본 길이(ms, 던진 때부터 마지막 프레임까지 — 마지막 프레임에서 모두 멈춰 있다).</summary>
        public int LengthMs => (int)Math.Ceiling((Frames - 1) * FrameSec * 1000f);

        public V3 RestPos(int die) => Pos[(Frames - 1) * Dice + die];
        public Q4 RestRot(int die) => Rot[(Frames - 1) * Dice + die];

        /// <summary>던진 뒤 <paramref name="sec"/>초의 자세. 아직 안 던졌으면 false.</summary>
        public bool Sample(int die, float sec, out V3 pos, out Q4 rot)
        {
            float f = sec / FrameSec;
            if (f < 0) f = 0;
            if (f > Frames - 1) f = Frames - 1;
            int a = (int)f, b = Math.Min(a + 1, Frames - 1);
            float u = f - a;
            int ia = a * Dice + die, ib = b * Dice + die;
            if (!Thrown[ia] || !Thrown[ib])
            {
                // 던지는 프레임 사이: 뒤쪽 절반에서 나타난다
                pos = Pos[ib];
                rot = Rot[ib];
                return Thrown[ib] && (Thrown[ia] || u >= 0.5f);
            }
            pos = V3.Lerp(Pos[ia], Pos[ib], u);
            rot = Q4.Slerp(Rot[ia], Rot[ib], u);
            return true;
        }

        /// <summary>한 벌을 읽는다. <paramref name="at"/>은 다음 벌의 자리로 옮겨진다.</summary>
        public static DiceClip Read(byte[] data, ref int at)
        {
            if (at + HeadBytes > data.Length) throw new FormatException("녹화본 머리가 잘렸다");
            var c = new DiceClip { Dice = data[at], Frames = data[at + 1] | data[at + 2] << 8, FrameSec = BitConverter.ToSingle(data, at + 3), SettleSec = BitConverter.ToSingle(data, at + 7) };
            at += HeadBytes;
            int m = c.Dice * c.Frames;
            if (c.Dice < DiceGeo.MinDice || c.Dice > DiceGeo.MaxDice || c.Frames < 1 || at + m * DieFrameBytes > data.Length) throw new FormatException($"녹화본이 깨졌다(주사위 {c.Dice}, 프레임 {c.Frames})");
            c.Pos = new V3[m];
            c.Rot = new Q4[m];
            c.Thrown = new bool[m];
            var q = new float[4];
            for (int i = 0; i < m; i++, at += DieFrameBytes)
            {
                int x = data[at] | data[at + 1] << 8, y = data[at + 2] | data[at + 3] << 8, z = data[at + 4] | data[at + 5] << 8;
                uint packed = (uint)(data[at + 6] | data[at + 7] << 8 | data[at + 8] << 16) | (uint)data[at + 9] << 24;
                c.Thrown[i] = x != 0;
                c.Pos[i] = new V3(x / 65535f * 64f - 16f, y / 65535f * 64f - 16f, z / 65535f * 64f - 16f);
                int big = (int)(packed >> 30);
                float sum = 0;
                for (int k = 3; k >= 0; k--)
                {
                    if (k == big) continue;
                    q[k] = ((packed & 1023) / 1023f - 0.5f) * 2f * 0.70711f;
                    packed >>= 10;
                    sum += q[k] * q[k];
                }
                q[big] = (float)Math.Sqrt(Math.Max(0f, 1f - sum));
                c.Rot[i] = new Q4(q[0], q[1], q[2], q[3]).Normalized;
            }
            return c;
        }

        /// <summary>한 벌을 적는다(굽기 도구·시험). 자세는 [프레임 × Dice + 주사위].</summary>
        public static void Write(List<byte> into, int dice, int frames, float frameSec, float settleSec, V3[] pos, Q4[] rot, bool[] thrown)
        {
            into.Add((byte)dice);
            into.Add((byte)frames);
            into.Add((byte)(frames >> 8));
            into.AddRange(BitConverter.GetBytes(frameSec));
            into.AddRange(BitConverter.GetBytes(settleSec));
            for (int i = 0; i < dice * frames; i++)
            {
                int x = thrown[i] ? Clamp(Pack16(pos[i].X), 1, 65535) : 0, y = Clamp(Pack16(pos[i].Y), 0, 65535), z = Clamp(Pack16(pos[i].Z), 0, 65535);
                into.Add((byte)x);
                into.Add((byte)(x >> 8));
                into.Add((byte)y);
                into.Add((byte)(y >> 8));
                into.Add((byte)z);
                into.Add((byte)(z >> 8));
                float[] c = { rot[i].X, rot[i].Y, rot[i].Z, rot[i].W };
                int big = 0;
                for (int k = 1; k < 4; k++) if (Math.Abs(c[k]) > Math.Abs(c[big])) big = k;
                float sgn = c[big] < 0 ? -1 : 1;
                uint packed = (uint)big;
                for (int k = 0; k < 4; k++)
                    if (k != big) packed = packed << 10 | (uint)Clamp((int)Math.Round((c[k] * sgn / 0.70711f * 0.5f + 0.5f) * 1023f), 0, 1023);
                into.Add((byte)packed);
                into.Add((byte)(packed >> 8));
                into.Add((byte)(packed >> 16));
                into.Add((byte)(packed >> 24));
            }
        }

        static int Pack16(float v) => (int)Math.Round((v + 16f) / 64f * 65535f);
        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>녹화본 묶음: 「SDR1」 + 벌 수(2바이트) + 벌들. 주사위 수별로 나눠 든다(묶음 안 순서 그대로).</summary>
    public sealed class DiceLibrary
    {
        readonly List<DiceClip>[] byDice = new List<DiceClip>[DiceGeo.MaxDice + 1];

        public int Total { get; private set; }

        public int Count(int dice) => dice >= DiceGeo.MinDice && dice <= DiceGeo.MaxDice ? byDice[dice].Count : 0;
        public DiceClip Clip(int dice, int index) => byDice[dice][index];
        public DiceClip Clip(DicePick pick) => byDice[pick.Dice][pick.Clip];

        public static DiceLibrary Parse(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < 6 || data[0] != 'S' || data[1] != 'D' || data[2] != 'R' || data[3] != '1') throw new FormatException("녹화본 묶음이 아니다(SDR1)");
            var lib = new DiceLibrary();
            for (int n = 0; n <= DiceGeo.MaxDice; n++) lib.byDice[n] = new List<DiceClip>();
            int count = data[4] | data[5] << 8, at = 6;
            for (int i = 0; i < count; i++)
            {
                var c = DiceClip.Read(data, ref at);
                lib.byDice[c.Dice].Add(c);
            }
            if (at != data.Length) throw new FormatException("녹화본 묶음 뒤에 남는 바이트가 있다");
            lib.Total = count;
            return lib;
        }

        public static byte[] Build(IReadOnlyList<byte[]> clips)
        {
            var all = new List<byte> { (byte)'S', (byte)'D', (byte)'R', (byte)'1', (byte)clips.Count, (byte)(clips.Count >> 8) };
            foreach (var c in clips) all.AddRange(c);
            return all.ToArray();
        }
    }

    /// <summary>고른 굴림: 주사위 수의 묶음에서 몇 번째 벌인지와 앞뒤 뒤집기. 방장이 이 값을 보내면 모두 같은 굴림을 본다(spikes/dice-roll §2 ④).</summary>
    public readonly struct DicePick
    {
        public readonly int Dice, Clip;
        public readonly bool Mirror;

        public DicePick(int dice, int clip, bool mirror)
        {
            Dice = dice;
            Clip = clip;
            Mirror = mirror;
        }
    }

    /// <summary>
    /// 녹화본 고르기(spikes/dice-roll §2 ②): 주사위 수마다 섞기 주머니 — 한 바퀴 동안 같은 벌이 다시 나오지 않고, 바퀴가 바뀌는 자리에서도 연달아 같은 벌이 안 나온다.
    /// 뽑을 때마다 앞뒤 뒤집기(판 깊이 방향 거울)를 반반으로 고른다. 난수는 넣어 준 <see cref="IRng"/>만 쓴다(규칙 난수와 따로 둔다 — 겉모양뿐이라 규칙에 영향 없음).
    /// </summary>
    public sealed class DiceBag
    {
        readonly IRng rng;
        readonly int[] counts = new int[DiceGeo.MaxDice + 1];
        readonly int[][] order = new int[DiceGeo.MaxDice + 1][];
        readonly int[] next = new int[DiceGeo.MaxDice + 1];

        /// <param name="counts">[주사위 수] = 그 묶음의 벌 수(색인 0은 안 쓴다).</param>
        public DiceBag(IRng rng, IReadOnlyList<int> counts)
        {
            this.rng = rng ?? throw new ArgumentNullException(nameof(rng));
            for (int n = DiceGeo.MinDice; n <= DiceGeo.MaxDice && n < counts.Count; n++) this.counts[n] = counts[n];
        }

        public DiceBag(IRng rng, DiceLibrary lib) : this(rng, Counts(lib)) { }

        static int[] Counts(DiceLibrary lib)
        {
            var c = new int[DiceGeo.MaxDice + 1];
            for (int n = DiceGeo.MinDice; n <= DiceGeo.MaxDice; n++) c[n] = lib.Count(n);
            return c;
        }

        public DicePick Next(int dice)
        {
            if (dice < DiceGeo.MinDice || dice > DiceGeo.MaxDice || counts[dice] == 0) throw new ArgumentOutOfRangeException(nameof(dice), dice, "그 주사위 수의 녹화본이 없다");
            int count = counts[dice];
            if (order[dice] == null || next[dice] >= count)
            {
                int last = order[dice] == null ? -1 : order[dice][count - 1];
                var o = new int[count];
                for (int i = 0; i < count; i++) o[i] = i;
                for (int i = count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    int t = o[i];
                    o[i] = o[j];
                    o[j] = t;
                }
                if (count > 1 && o[0] == last)
                {
                    // 바퀴가 바뀌는 자리: 앞 바퀴의 마지막 벌이 맨 앞에 오면 뒤의 아무 자리와 바꾼다
                    int j = 1 + rng.Next(count - 1);
                    o[0] = o[j];
                    o[j] = last;
                }
                order[dice] = o;
                next[dice] = 0;
            }
            int clip = order[dice][next[dice]++];
            return new DicePick(dice, clip, rng.Next(2) == 1);
        }
    }
}
