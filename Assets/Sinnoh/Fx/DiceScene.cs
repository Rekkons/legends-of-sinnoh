using System;
using System.Collections.Generic;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 주사위 정육면체의 면(spikes/dice-roll §2 ③). 면 번호: 0 +X · 1 −X · 2 +Y · 3 −Y · 4 +Z · 5 −Z.
    /// 면 구성은 규칙의 공격 주사위(R9 — <c>AttackDice.FaceOf</c>: 심볼 1 · 일반 4 · 급소 1)와 같고, 배치는 급소(+Y)와 심볼(−Y)이 맞은편, 일반 타격 넷이 옆이다 [가정, 원작 TTS 모델 미확인].
    /// </summary>
    public static class DiceCube
    {
        public static readonly V3[] Normal = { V3.Right, -V3.Right, V3.Up, -V3.Up, V3.Forward, -V3.Forward };
        /// <summary>면 그림의 위쪽(그리는 쪽 메시의 V 방향과 같아야 한다).</summary>
        public static readonly V3[] GlyphUp = { V3.Up, V3.Up, V3.Forward, -V3.Forward, V3.Up, V3.Up };
        public static readonly AttackDieFace[] FaceAt =
        {
            AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.CriticalHit, AttackDieFace.StatusSymbol, AttackDieFace.RegularHit, AttackDieFace.RegularHit,
        };

        /// <summary>정육면체 회전 대칭 24개.</summary>
        public static readonly Q4[] Syms = BuildSyms();

        static Q4[] BuildSyms()
        {
            var gens = new[] { Q4.AngleAxis(90, V3.Right), Q4.AngleAxis(90, V3.Up) };
            var found = new List<Q4> { Q4.Identity };
            for (int i = 0; i < found.Count; i++)
                foreach (var g in gens)
                {
                    var q = (g * found[i]).Normalized;
                    bool seen = false;
                    foreach (var f in found) if (Q4.Same(f, q)) { seen = true; break; }
                    if (!seen) found.Add(q);
                }
            return found.ToArray();
        }

        /// <summary>그 자세에서 위를 보는 면 번호.</summary>
        public static int TopFace(Q4 rot)
        {
            int best = 0;
            float bd = -2;
            for (int k = 0; k < 6; k++)
            {
                float d = (rot * Normal[k]).Y;
                if (d > bd) { bd = d; best = k; }
            }
            return best;
        }

        /// <summary>위를 보는 면의 그림이 화면 위쪽(판 안쪽 +Z)으로 얼마나 서 있나(−1~1).</summary>
        public static float Upright(Q4 rot) => (rot * GlyphUp[TopFace(rot)]).Z;
    }

    /// <summary>주사위 하나의 그릴 자세(물리 주사위 번호 순).</summary>
    public struct DiePose
    {
        public bool Shown;
        public V3 Pos;
        public Q4 Rot;
        /// <summary>0 타입 색 → 1 회색(면 바꾸기로 바뀐 주사위).</summary>
        public float Dim;
        /// <summary>0 → 1 흰 번쩍(재굴림 고정).</summary>
        public float Flash;
        /// <summary>주사위 몸의 축마다 늘인 만큼(1에서 벗어난 값 — 0 = 그대로). 재굴림 글리치가 입체로 일그러뜨린다(M9 다듬기 P38).</summary>
        public V3 Stretch;
    }

    /// <summary>면 바꾸기 하나(굴림판의 꼬리표·고리가 따라갈 값): <see cref="AtMs"/>에 엔진의 <see cref="Index"/>번째 주사위가 넘어가기 시작한다.</summary>
    public struct DiceFlip
    {
        public int AtMs, Index;
        public AttackDieFace To;
    }

    /// <summary>글리치 재굴림이 도는 동안의 값(spikes/dice-roll §4, 길이는 칸 단위). <see cref="On"/>이 false면 글리치 없음.</summary>
    public struct DiceGlitch
    {
        public bool On;
        /// <summary>물리 주사위 번호.</summary>
        public int Die;
        public V3 Center;
        /// <summary>가로 띠 밀림 · 색 분리 · 잔상(칸), 파란 색조 · 시계 무늬 세기(0~1), 바늘 각(rad), 띠 무늬 씨앗, 시계 고리 반지름(칸).</summary>
        public float Amt, Split, Ghost, Tint, Clock, Angle, Seed, ClockR;
    }

    /// <summary>
    /// 굴림 한 번의 재생(04 §8-8 56·57·43행, spikes/dice-roll §2·§4): 녹화본 + 엔진 결과 → 시각마다 주사위 자세. <see cref="Pose"/>는 같은 ms면 같은 값이다(앞 호출·실시각에 기대지 않는다).
    /// <list type="bullet">
    /// <item>엔진의 k번째 주사위 = 멈췄을 때 왼쪽에서 k번째 주사위(<see cref="Order"/>). 면은 정육면체 대칭 24개 중 목표 면이 위로 오고 그림이 가장 바로 서는 것을 붙인다.</item>
    /// <item>면 바꾸기(57행): 그 주사위가 제자리에서 90° 넘어가며 0.6칸 떴다 내려오고 회색이 된다. 맞은편 면으로 바뀌면(급소 ↔ 심볼) 180° [가정]. 지워진 주사위(After = None)는 넘어가지 않고 회색만 된다 [가정].</item>
    /// <item>재굴림(43행): 제자리 글리치 600ms — 0~80 들어옴 · 80~480 지직(면 50ms · 자리 30ms마다) · 480 새 면으로 고정 + 번쩍 90 · 600까지 고리.
    /// 지직 동안 주사위는 입체로 일그러진다(M9 다듬기 P38 — 결정 116): 살짝 떠서(<see cref="GlitchLift"/>칸) 녹화본의 구르던 자세를 30ms씩 끊어 거꾸로 되감고(<see cref="RewindSec"/>초 분량),
    /// 30ms마다 몸의 한 축이 늘어나고 나머지가 줄어든다(<see cref="DiePose.Stretch"/>). 자리(가로·깊이)는 그대로 제자리다(결정 48 ② — 굴림판을 다시 구르지 않는다).</item>
    /// </list>
    /// 시각은 모두 항목 시각(ms)이다.
    /// </summary>
    public sealed class DiceRollScene
    {
        public const int FlipMs = 280, GlitchMs = 600;
        /// <summary>면 바꾸기 한 박자(730) 안에서 넘김이 시작하는 때: 꼬리표·고리 300 + 150.</summary>
        public const int FlipLeadMs = 450;
        const int GlitchInMs = 80, GlitchLockMs = 480, FlashMs = 90;
        /// <summary>재굴림 글리치의 입체 일그러짐(M9 다듬기 P38): 떠오르는 높이(칸) · 되감는 녹화본 분량(초 — 녹화본이 짧으면 그 0.8배) · 한 축이 늘어나는 가장 큰 몫.</summary>
        public const float GlitchLift = 0.45f, RewindSec = 1.1f, GlitchStretch = 0.35f;

        sealed class Op
        {
            public int AtMs;
            public bool Reroll;
            public AttackDieFace To;
            // Build가 채운다
            public V3 Axis;
            public float Angle;
            public Q4 Label;
        }

        public readonly DiceClip Clip;
        public readonly bool Mirror;
        public int Dice => Clip.Dice;
        /// <summary>던지는 때.</summary>
        public int ThrowMs;
        /// <summary>세기가 시작하는 때(굴림판 머리의 결과 줄·합계 — 화면 쪽이 쓴다). <see cref="Plan"/>이 채운다.</summary>
        public int CountMs;
        /// <summary>Order[k] = 엔진의 k번째 주사위인 물리 주사위 번호(멈췄을 때 왼쪽에서 k번째).</summary>
        public readonly int[] Order;
        /// <summary>건 면 바꾸기(건 순서). 굴림판 머리가 꼬리표를 붙일 때 읽는다(04 §8-8 57행 — 넘기기 150 전에 꼬리표).</summary>
        public readonly List<DiceFlip> Flips = new List<DiceFlip>();

        readonly AttackDieFace[] faces;
        readonly int salt;
        readonly List<Op>[] ops;
        readonly V3[] restPos;
        readonly Q4[] restRot;
        Q4[] label;
        bool built;

        /// <param name="faces">엔진이 정한 면(엔진 순서).</param>
        /// <param name="salt">같은 점수의 면 붙이기 가운데 하나를 고르는 값(겉모양만 달라진다).</param>
        public DiceRollScene(DiceClip clip, bool mirror, IReadOnlyList<AttackDieFace> faces, int salt = 0)
        {
            Clip = clip ?? throw new ArgumentNullException(nameof(clip));
            if (faces == null || faces.Count != clip.Dice) throw new ArgumentException($"주사위 수가 다르다(녹화본 {clip.Dice})", nameof(faces));
            Mirror = mirror;
            this.salt = salt;
            int n = clip.Dice;
            this.faces = new AttackDieFace[n];
            ops = new List<Op>[n];
            restPos = new V3[n];
            restRot = new Q4[n];
            Order = new int[n];
            for (int i = 0; i < n; i++)
            {
                this.faces[i] = faces[i];
                ops[i] = new List<Op>();
                restPos[i] = Flip(clip.RestPos(i));
                restRot[i] = Flip(clip.RestRot(i));
                Order[i] = i;
            }
            Array.Sort(Order, (a, b) => restPos[a].X != restPos[b].X ? restPos[a].X.CompareTo(restPos[b].X) : a.CompareTo(b));
        }

        // 앞뒤 뒤집기: 판 깊이 방향 거울(z → 깊이 − z). 정육면체는 거울 대칭이라 회전은 (−x, −y, z, w)가 된다
        V3 Flip(V3 p) => Mirror ? new V3(p.X, p.Y, DiceGeo.Depth - p.Z) : p;
        Q4 Flip(Q4 q) => Mirror ? new Q4(-q.X, -q.Y, q.Z, q.W) : q;

        /// <summary>멈춘 자리(물리 주사위 번호).</summary>
        public V3 RestPos(int die) => restPos[die];

        /// <summary>면 바꾸기: <paramref name="atMs"/>에 엔진의 <paramref name="index"/>번째 주사위가 넘어가기 시작한다(<see cref="FlipMs"/> 동안).</summary>
        public void AddFlip(int atMs, int index, AttackDieFace to)
        {
            Add(new Op { AtMs = atMs, To = to }, index);
            Flips.Add(new DiceFlip { AtMs = atMs, Index = index, To = to });
        }

        /// <summary>글리치 재굴림: <paramref name="atMs"/>부터 <see cref="GlitchMs"/> 동안, 새 면 <paramref name="to"/>.</summary>
        public void AddReroll(int atMs, int index, AttackDieFace to)
        {
            if (to == AttackDieFace.None) throw new ArgumentOutOfRangeException(nameof(to));
            Add(new Op { AtMs = atMs, Reroll = true, To = to }, index);
        }

        void Add(Op op, int index)
        {
            if (index < 0 || index >= Dice) throw new ArgumentOutOfRangeException(nameof(index));
            var list = ops[Order[index]];
            int i = list.Count;
            while (i > 0 && list[i - 1].AtMs > op.AtMs) i--;
            list.Insert(i, op);
            built = false;
        }

        void Build()
        {
            label = new Q4[Dice];
            for (int k = 0; k < Dice; k++)
            {
                int die = Order[k];
                var list = ops[die];
                var rest = restRot[die];
                var face = faces[k];
                Op next = list.Count > 0 && !list[0].Reroll ? list[0] : null;
                label[die] = Choose(rest, face, next, salt + k * 7);
                Q4 lab = label[die];
                for (int i = 0; i < list.Count; i++)
                {
                    var op = list[i];
                    next = i + 1 < list.Count && !list[i + 1].Reroll ? list[i + 1] : null;
                    if (op.Reroll)
                    {
                        lab = op.Label = Choose(rest, op.To, next, salt + k * 7 + (i + 1) * 13);
                        continue;
                    }
                    if (op.To == AttackDieFace.None) { op.Angle = 0; op.Axis = V3.Right; continue; }
                    // 붙인 면이 정해진 채로 넘길 축을 찾는다(처음 넘김은 Choose가 면과 함께 골랐다 — 같은 답이 나온다)
                    FindFlip(rest * lab, op.To, out op.Axis, out op.Angle, out _);
                    rest = Q4.AngleAxis(op.Angle, op.Axis) * rest;
                }
            }
            built = true;
        }

        /// <summary>멈춘 자세 <paramref name="rest"/>에서 <paramref name="target"/> 면이 위로 오는 붙이기 가운데, (넘김이 이어지면 넘긴 뒤의) 그림이 가장 바로 서는 것.</summary>
        static Q4 Choose(Q4 rest, AttackDieFace target, Op flip, int salt)
        {
            float best = -100;
            var ties = new List<Q4>();
            foreach (var s in DiceCube.Syms)
            {
                var vis = rest * s;
                if (DiceCube.FaceAt[DiceCube.TopFace(vis)] != target) continue;
                float score;
                if (flip == null || flip.To == AttackDieFace.None) score = DiceCube.Upright(vis);
                else
                {
                    FindFlip(vis, flip.To, out _, out float angle, out score);
                    score += 0.5f * DiceCube.Upright(vis);   // 넘기기 전 그림도 될 수 있으면 바로
                    if (angle < 135) score += 10;       // 90°로 되는 붙이기가 먼저
                }
                if (score > best + 1e-3f) { best = score; ties.Clear(); }
                if (score > best - 1e-3f) ties.Add(s);
            }
            return ties[(salt & 0x7fffffff) % ties.Count];
        }

        /// <summary>바닥에 놓인 자세 <paramref name="vis"/>를 수평축으로 90°(안 되면 180°) 넘겨 <paramref name="to"/> 면이 위로 오는 축. 넘긴 뒤 그림이 가장 바로 서는 것.</summary>
        static void FindFlip(Q4 vis, AttackDieFace to, out V3 axis, out float angle, out float score)
        {
            axis = V3.Right;
            angle = 0;
            score = -100;
            if (DiceCube.FaceAt[DiceCube.TopFace(vis)] == to) { score = DiceCube.Upright(vis); return; }
            foreach (float deg in new[] { 90f, 180f })
            {
                for (int k = 0; k < 6; k++)
                {
                    var a = vis * DiceCube.Normal[k];
                    if (Math.Abs(a.Y) > 0.5f) continue;
                    float m = (float)Math.Sqrt(a.X * a.X + a.Z * a.Z);
                    a = new V3(a.X / m, 0, a.Z / m);
                    var after = Q4.AngleAxis(deg, a) * vis;
                    if (DiceCube.FaceAt[DiceCube.TopFace(after)] != to) continue;
                    float s = DiceCube.Upright(after);
                    if (s > score + 1e-3f) { score = s; axis = a; angle = deg; }
                }
                if (angle > 0) return;
            }
        }

        /// <summary>그 시각에 엔진의 <paramref name="index"/>번째 주사위가 위로 보이는 면(아직 안 던졌으면 None). 글리치로 깜빡이는 동안은 그 순간의 면이다.</summary>
        public AttackDieFace ShownFace(int index, int ms, DiePose[] scratch)
        {
            Pose(ms, scratch, out _);
            var p = scratch[Order[index]];
            return p.Shown ? DiceCube.FaceAt[DiceCube.TopFace(p.Rot)] : AttackDieFace.None;
        }

        /// <summary>항목 시각 <paramref name="ms"/>의 자세를 <paramref name="into"/>(물리 주사위 번호 순, 길이 ≥ Dice)에 적는다.</summary>
        public void Pose(int ms, DiePose[] into, out DiceGlitch glitch)
        {
            if (!built) Build();
            glitch = default(DiceGlitch);
            float sec = (ms - ThrowMs) / 1000f;
            for (int die = 0; die < Dice; die++)
            {
                var p = default(DiePose);
                if (ms < ThrowMs || !Clip.Sample(die, sec, out var pos, out var rot)) { into[die] = p; continue; }
                pos = Flip(pos);
                rot = Flip(rot);
                var lab = label[die];
                var pre = Q4.Identity;
                foreach (var op in ops[die])
                {
                    int u = ms - op.AtMs;
                    if (u < 0) break;
                    if (!op.Reroll)
                    {
                        float q = Math.Min(1f, u / (float)FlipMs);
                        if (op.Angle > 0)
                        {
                            pre = Q4.AngleAxis(op.Angle * q * q * (3 - 2 * q), op.Axis) * pre;
                            pos = new V3(pos.X, pos.Y + 0.6f * 4 * q * (1 - q), pos.Z);
                        }
                        p.Dim = Math.Max(p.Dim, Math.Max(0f, Math.Min(1f, (q - 0.3f) / 0.4f)));
                        continue;
                    }
                    var rest = restPos[die];
                    if (u < GlitchLockMs)
                    {
                        float k01 = Math.Min(1f, u / (float)GlitchInMs);
                        int sd = u / 30;
                        if (u >= GlitchInMs)
                        {
                            lab = DiceCube.Syms[((u / 50 + 3) * 7919 + 13) % DiceCube.Syms.Length];
                            pos = new V3(rest.X + (sd * 37 % 5 - 2) * 0.02f, rest.Y + GlitchLift, rest.Z + (sd * 53 % 5 - 2) * 0.015f);
                            // 시간이 되감긴다: 멈춘 때에서 거꾸로 — 구르던 자세를 30ms씩 끊어 되짚는다(자리는 제자리, 자세만)
                            float len = Clip.LengthMs / 1000f, back = (sd * 30 - GlitchInMs) / (float)(GlitchLockMs - GlitchInMs);
                            if (Clip.Sample(die, Math.Max(0f, len - Math.Min(RewindSec, len * 0.8f) * Math.Max(0f, back)), out _, out var spun)) rot = Flip(spun);
                            // 몸이 일그러진다: 30ms마다 한 축이 늘고 나머지 둘이 그 절반만큼 준다
                            float grow = GlitchStretch * (1 + sd * 53 % 4) / 4f;
                            int axis = sd * 31 % 3;
                            p.Stretch = new V3(axis == 0 ? grow : -grow / 2, axis == 1 ? grow : -grow / 2, axis == 2 ? grow : -grow / 2);
                        }
                        else pos = new V3(pos.X, pos.Y + GlitchLift * k01, pos.Z);   // 들어오는 동안 떠오른다
                        glitch = new DiceGlitch
                        {
                            On = true, Die = die, Center = rest, Amt = u < GlitchInMs ? 0.06f * k01 : 0.24f, Split = 0.02f + 0.06f * k01, Tint = 0.3f * k01,
                            Ghost = u >= GlitchInMs ? 0.36f : 0f, Clock = k01, Angle = -u * 0.025f, Seed = sd, ClockR = 0.95f,
                        };
                    }
                    else
                    {
                        lab = op.Label;
                        p.Flash = Math.Max(0f, 1f - (u - GlitchLockMs) / (float)FlashMs);
                        if (u < GlitchMs)
                        {
                            float e = (u - GlitchLockMs) / (float)(GlitchMs - GlitchLockMs);
                            glitch = new DiceGlitch
                            {
                                On = true, Die = die, Center = rest, Split = 0.03f * (1 - e), Tint = 0.25f * (1 - e), Clock = 1 - e, Angle = -GlitchLockMs * 0.025f, ClockR = 0.95f + 0.6f * e,
                            };
                        }
                    }
                }
                p.Shown = true;
                p.Pos = pos;
                p.Rot = (pre * rot * lab).Normalized;
                into[die] = p;
            }
        }

        /// <summary>
        /// 대기열 항목의 굴림 단계(56행)로 재생을 짠다(없으면 null): 던짐 = 단계 시작 + <c>PlayTiming.DiceThrowMs</c>, 다 멈춤(위쪽 끝) = + <c>DiceRollMaxMs</c>.
        /// 굴림 단계 바로 뒤에 이어진 면 바꾸기 단계(57행, 상태이상이 원인이면 63행)는 멈춤과 세기 사이에 끼운다(04 §8-8 56행 「면 바꾸기가 있으면 57행이 여기 낀다」) —
        /// 단계 하나가 한 박자 <c>FaceFlipMs</c>(730)이고 +450에 넘긴다. 그래서 세기는 그만큼 늦게 시작하고(<see cref="CountMs"/>), 단계 길이의 합은 그대로다.
        /// 57행은 이벤트마다 단계가 이어져 한 박자씩 넘어가고, 63행은 한 항목에 단계 하나(900)라 그 안의 면 바꾸기(졸린 공격자 + 졸린 방어자처럼 둘일 수 있다)는 한 박자에 함께 넘긴다.
        /// 사이에 다른 단계가 끼어 있으면 그 면 바꾸기는 제 단계의 시각에 넘긴다 [가정].
        /// </summary>
        public static DiceRollScene Plan(FxEntry entry, DiceClip clip, bool mirror, int salt = 0)
        {
            var roll = entry?.Step(560);
            if (roll == null || roll.Events.Count == 0) return null;
            var scene = new DiceRollScene(clip, mirror, ((DiceRolled)roll.Events[0]).Faces, salt) { ThrowMs = roll.StartMs + PlayTiming.DiceThrowMs.Value };
            var flips = new List<DiceFlip>();
            scene.CountMs = Timeline(entry, flips);
            foreach (var f in flips)
                if (f.Index < scene.Dice) scene.AddFlip(f.AtMs, f.Index, f.To);
            return scene;
        }

        /// <summary>
        /// <see cref="Plan"/>의 시각만(녹화본 없이 — 소리 단서가 같은 시각을 쓴다, 04 §8-4): 면 바꾸기(넘기기 시작 시각 · 엔진 순서 · 새 면)를 <paramref name="flips"/>에 더하고 세기가 시작하는 항목 시각을 돌려준다.
        /// 굴림 단계가 없으면 −1.
        /// </summary>
        public static int Timeline(FxEntry entry, List<DiceFlip> flips)
        {
            if (flips == null) throw new ArgumentNullException(nameof(flips));
            var roll = entry?.Step(560);
            if (roll == null || roll.Events.Count == 0) return -1;
            int stop = roll.StartMs + PlayTiming.DiceThrowMs.Value + PlayTiming.DiceRollMaxMs.Value, cursor = stop, prevEnd = roll.EndMs;
            bool chain = true;
            foreach (var step in entry.Steps)
            {
                if (step == roll || step.StartMs < roll.EndMs) continue;
                bool any = false;
                int at = chain && step.StartMs == prevEnd ? cursor : step.StartMs;
                foreach (var e in step.Events)
                {
                    if (!(e is DiceFacesChanged d) || d.Changes == null || d.Changes.Length == 0) continue;
                    any = true;
                    foreach (var c in d.Changes)
                        if (c.Index >= 0) flips.Add(new DiceFlip { AtMs = at + FlipLeadMs, Index = c.Index, To = c.After });
                }
                if (any && chain && step.StartMs == prevEnd)
                {
                    cursor = at + PlayTiming.FaceFlipMs.Value;
                    prevEnd = step.EndMs;
                }
                else if (step.Ms > 0) chain = false;
            }
            return cursor + PlayTiming.DiceCountGapMs.Value;
        }
    }
}
