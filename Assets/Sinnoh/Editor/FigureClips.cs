using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 피규어 클립 줄이기(03 §2-5 파이프라인 5, §8 #13). 번들의 레거시 60fps 클립을
    /// <list type="number">
    /// <item>30fps로 다시 표본한다(곡선 Evaluate, 쿼터니언은 정규화·부호 연속).</item>
    /// <item>모든 클립에 있고 모두 일정하고 값이 같은 Transform 채널은 클립에서 빼고 프리팹 자세에 굽는다(모든 클립이 늘 같은 값을 넣으므로 재생 결과가 같다. 어느 클립에 없는 채널은 굽지 않는다 — 판 4).</item>
    /// <item>남은 채널은 Hermite 키로 줄인다: 표본의 중앙 차분을 접선으로 두고, 앞 남긴 키에서 j까지 Hermite 구간이 사이 표본을 모두 허용 오차 안에 두면 늘리고 아니면 j-1을 남긴다.
    /// 줄인 키가 원본 곡선의 키보다 적지 않으면 원본 곡선을 그대로 쓴다(번들 클립은 이미 듬성듬성한 60fps 접선 곡선이다 — d3-1 §5-3 ⑧의 「프레임마다 키」는 아니었다).</item>
    /// </list>
    /// 허용 오차 [가정]: 회전 0.5°, 위치 0.001(프리팹 배율 1 기준 월드 단위 — 뼈 로컬 단위로는 부모 월드 배율로 나눈다), 배율·그 밖 값 0.001.
    /// 회전(m_LocalRotation 4성분)·위치·배율은 성분을 묶어 같은 키 시각으로 줄인다(레거시 런타임 곡선이 벡터·쿼터니언 곡선이라).
    /// Mecanim 클립(에디터 곡선 0, d3-1 §5-3 ④)은 인스턴스에서 30fps로 자세를 표본해 같은 길로 레거시 클립을 만든다.
    /// </summary>
    public static class FigureClips
    {
        public const float Fps = 30f;
        public const float RotTolDeg = 0.5f;
        public const float PosTolWorld = 0.001f;
        public const float ScaleTol = 0.001f;
        public const float FloatTol = 0.001f;

        public sealed class Stats
        {
            public int CurvesIn, CurvesOut, KeysIn, KeysOut, Dropped, Baked, KeptSource;
            public float MaxRotErr, MaxPosErr;
            public readonly List<string> Notes = new List<string>();
        }

        public enum Kind { Position, Rotation, Scale, Euler, Float }

        public sealed class Channel
        {
            public string Path;
            public Type Type;
            public Kind Kind;
            public string Prefix;         // m_LocalPosition 등. Float는 속성 이름 전체
            public string[] Comps;        // x y z (w), Float는 속성 이름 하나
            public float[][] V;           // [표본][성분]
            public float Tol;
            public AnimationCurve[] Src;  // 레거시 원본 곡선(줄인 키가 원본보다 많으면 원본을 쓴다)
            public string Id => Path + "|" + Type.FullName + "|" + Prefix;
            public string Property(int k) => Kind == Kind.Float ? Comps[k] : Prefix + "." + Comps[k];
        }

        public sealed class ClipData
        {
            public AnimationClip Src;
            public float[] Times;
            public readonly List<Channel> Channels = new List<Channel>();
        }

        /// <summary>프리팹 자세에 구운 채널(경로는 Animation 컴포넌트 기준).</summary>
        public sealed class BakedChannel
        {
            public string Path;
            public Kind Kind;
            public string[] Comps;
            public float[] Value;
        }

        /// <summary>표본 시각: 0, 1/30, …(길이 미만) + 길이.</summary>
        public static float[] SampleTimes(float length)
        {
            var t = new List<float>();
            for (int i = 0; ; i++)
            {
                float x = i / Fps;
                if (x >= length - 1e-5f) break;
                t.Add(x);
            }
            t.Add(Mathf.Max(0f, length));
            if (t.Count == 1) t.Add(t[0]);
            return t.ToArray();
        }

        // ---------- 읽기 ----------
        /// <summary>레거시 클립(에디터 곡선 있음)을 30fps로 표본한다. root = 경로 기준(Animation 컴포넌트가 붙은 것, 번들 인스턴스).</summary>
        public static ClipData ReadLegacy(AnimationClip src, Transform root, Stats st)
        {
            var d = new ClipData { Src = src, Times = SampleTimes(src.length) };
            var groups = new Dictionary<string, (EditorCurveBinding b, Kind kind, string prefix, List<(string comp, AnimationCurve c)> comps)>();
            var order = new List<string>();
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                var curve = AnimationUtility.GetEditorCurve(src, b);
                if (curve == null) continue;
                st.CurvesIn++; st.KeysIn += curve.length;
                Classify(b, out var kind, out var prefix, out var comp);
                string id = b.path + "|" + b.type.FullName + "|" + prefix;
                if (!groups.TryGetValue(id, out var g)) { g = (b, kind, prefix, new List<(string, AnimationCurve)>()); groups[id] = g; order.Add(id); }
                g.comps.Add((comp, curve));
            }
            var refs = AnimationUtility.GetObjectReferenceCurveBindings(src);
            if (refs.Length > 0) { st.Dropped += refs.Length; st.Notes.Add($"{src.name}: 오브젝트 참조 곡선 {refs.Length}개 뺌(번들 에셋을 가리킴)"); }
            foreach (var id in order)
            {
                var g = groups[id];
                var kind = g.kind;
                if (kind == Kind.Rotation && !new[] { "x", "y", "z", "w" }.All(c => g.comps.Any(x => x.comp == c))) kind = Kind.Float;
                var comps = g.comps.OrderBy(c => CompOrder(c.comp)).ToList();
                if (kind == Kind.Float)
                {
                    // 묶지 않은 수: 성분마다 채널 하나
                    foreach (var c in comps) d.Channels.Add(Make(g.b.path, g.b.type, Kind.Float, g.kind == Kind.Float ? c.comp : g.prefix + "." + c.comp, new[] { g.kind == Kind.Float ? c.comp : g.prefix + "." + c.comp }, new[] { c.c }, d.Times, root));
                    continue;
                }
                d.Channels.Add(Make(g.b.path, g.b.type, kind, g.prefix, comps.Select(c => c.comp).ToArray(), comps.Select(c => c.c).ToArray(), d.Times, root));
            }
            return d;
        }

        static Channel Make(string path, Type type, Kind kind, string prefix, string[] comps, AnimationCurve[] curves, float[] times, Transform root)
        {
            var v = new float[times.Length][];
            for (int i = 0; i < times.Length; i++)
            {
                v[i] = new float[curves.Length];
                for (int k = 0; k < curves.Length; k++) v[i][k] = curves[k].Evaluate(times[i]);
            }
            if (kind == Kind.Rotation) NormalizeQuats(v);
            return new Channel { Path = path, Type = type, Kind = kind, Prefix = prefix, Comps = comps, V = v, Tol = Tolerance(kind, root, path), Src = curves };
        }

        /// <summary>Mecanim 클립: 인스턴스(sampleRoot)에서 30fps로 자세를 표본한다. 경로 기준 pathRoot 아래 모든 뼈의 위치·회전·배율.</summary>
        public static ClipData ReadMecanim(AnimationClip src, GameObject sampleRoot, Transform pathRoot, Stats st)
        {
            var d = new ClipData { Src = src, Times = SampleTimes(src.length) };
            var ts = pathRoot.GetComponentsInChildren<Transform>(true);
            int n = d.Times.Length;
            var vals = new float[ts.Length, 3][][];
            for (int j = 0; j < ts.Length; j++) for (int g = 0; g < 3; g++) vals[j, g] = new float[n][];
            for (int i = 0; i < n; i++)
            {
                src.SampleAnimation(sampleRoot, d.Times[i]);
                for (int j = 0; j < ts.Length; j++)
                {
                    var p = ts[j].localPosition; var q = ts[j].localRotation; var s = ts[j].localScale;
                    vals[j, 0][i] = new[] { p.x, p.y, p.z };
                    vals[j, 1][i] = new[] { q.x, q.y, q.z, q.w };
                    vals[j, 2][i] = new[] { s.x, s.y, s.z };
                }
            }
            for (int j = 0; j < ts.Length; j++)
            {
                string path = AnimationUtility.CalculateTransformPath(ts[j], pathRoot);
                for (int g = 0; g < 3; g++)
                {
                    var kind = g == 0 ? Kind.Position : g == 1 ? Kind.Rotation : Kind.Scale;
                    var v = vals[j, g];
                    if (kind == Kind.Rotation) NormalizeQuats(v);
                    float tol = Tolerance(kind, pathRoot, path);
                    if (Constant(v, kind, tol)) continue;   // 움직이지 않는 뼈는 뺀다(자세는 프리팹에 있다)
                    d.Channels.Add(new Channel
                    {
                        Path = path, Type = typeof(Transform), Kind = kind,
                        Prefix = g == 0 ? "m_LocalPosition" : g == 1 ? "m_LocalRotation" : "m_LocalScale",
                        Comps = g == 1 ? new[] { "x", "y", "z", "w" } : new[] { "x", "y", "z" }, V = v, Tol = tol,
                    });
                }
            }
            st.Notes.Add($"{src.name}: Mecanim → 레거시 다시 구움(채널 {d.Channels.Count})");
            return d;
        }

        // ---------- 클립 사이 ----------
        /// <summary>모든 클립에 있고 모두 일정하고 같은 값인 Transform 채널을 빼고 구울 값으로 돌려준다. 클립마다 채널을 하나는 남긴다(길이 유지).</summary>
        public static List<BakedChannel> DropShared(List<ClipData> clips, Stats st)
        {
            var baked = new List<BakedChannel>();
            var ids = clips.SelectMany(c => c.Channels).Where(ch => ch.Kind != Kind.Float).Select(ch => ch.Id).Distinct().ToList();
            var drop = new HashSet<string>();
            foreach (var id in ids)
            {
                Channel first = null;
                bool ok = true;
                foreach (var c in clips)
                {
                    var ch = c.Channels.FirstOrDefault(x => x.Id == id);
                    // 이 채널이 없는 클립은 그 뼈를 건드리지 않는다(레거시 Animation은 앞 자세를 둔다). 그런 채널을 구우면 처음 Idle 자세가 바뀐다 —
                    // 481·480 Hips y 15°·16.7°, 484 더듬이 5~10°(M4-2c d3-3 §6 ②, 판 4) → 모든 클립에 있을 때만 굽는다
                    if (ch == null) { ok = false; break; }
                    if (!Constant(ch.V, ch.Kind, ch.Tol)) { ok = false; break; }
                    if (first == null) first = ch;
                    else if (first.Comps.Length != ch.Comps.Length || Error(first.V[0], ch.V[0], ch.Kind) > ch.Tol) { ok = false; break; }
                }
                if (!ok || first == null) continue;
                drop.Add(id);
                baked.Add(new BakedChannel { Path = first.Path, Kind = first.Kind, Comps = first.Comps, Value = (float[])first.V[0].Clone() });
            }
            foreach (var c in clips)
            {
                var keep = c.Channels.Where(ch => !drop.Contains(ch.Id)).ToList();
                if (keep.Count == 0 && c.Channels.Count > 0) keep.Add(c.Channels[0]);
                st.Baked += c.Channels.Count - keep.Count;
                c.Channels.Clear(); c.Channels.AddRange(keep);
            }
            return baked;
        }

        /// <summary>구운 채널을 사본 자세에 넣는다(pathRoot = 사본의 Animation 기준).</summary>
        public static void ApplyBaked(List<BakedChannel> baked, Transform pathRoot)
        {
            foreach (var b in baked)
            {
                var t = b.Path.Length == 0 ? pathRoot : pathRoot.Find(b.Path);
                if (t == null) continue;
                switch (b.Kind)
                {
                    case Kind.Position: t.localPosition = Set(t.localPosition, b); break;
                    case Kind.Scale: t.localScale = Set(t.localScale, b); break;
                    case Kind.Euler: t.localEulerAngles = Set(t.localEulerAngles, b); break;
                    case Kind.Rotation: t.localRotation = new Quaternion(b.Value[0], b.Value[1], b.Value[2], b.Value[3]); break;
                }
            }
        }

        static Vector3 Set(Vector3 v, BakedChannel b)
        {
            for (int k = 0; k < b.Comps.Length; k++)
            {
                if (b.Comps[k] == "x") v.x = b.Value[k];
                else if (b.Comps[k] == "y") v.y = b.Value[k];
                else if (b.Comps[k] == "z") v.z = b.Value[k];
            }
            return v;
        }

        // ---------- 만들기 ----------
        public static AnimationClip Build(ClipData d, Stats st)
        {
            var src = d.Src;
            var dst = new AnimationClip { name = src.name, legacy = true, frameRate = Fps, wrapMode = src.wrapMode };
            var outB = new List<EditorCurveBinding>();
            var outC = new List<AnimationCurve>();
            foreach (var ch in d.Channels)
            {
                var curves = Reduce(d.Times, ch, st);
                for (int k = 0; k < curves.Length; k++)
                {
                    outB.Add(EditorCurveBinding.FloatCurve(ch.Path, ch.Type, ch.Property(k)));
                    outC.Add(curves[k]);
                    st.CurvesOut++; st.KeysOut += curves[k].length;
                }
            }
            AnimationUtility.SetEditorCurves(dst, outB.ToArray(), outC.ToArray());
            if (src.legacy) AnimationUtility.SetAnimationEvents(dst, AnimationUtility.GetAnimationEvents(src));
            dst.EnsureQuaternionContinuity();
            return dst;
        }

        /// <summary>Hermite 키 줄이기. 접선 = 표본의 중앙 차분(끝은 한쪽), 일정하면 처음·끝 키 둘(기울기 0).</summary>
        static AnimationCurve[] Reduce(float[] t, Channel ch, Stats st)
        {
            var v = ch.V;
            int n = t.Length, m = ch.Comps.Length;
            var curves = new AnimationCurve[m];
            if (Constant(v, ch.Kind, ch.Tol))
            {
                for (int k = 0; k < m; k++) curves[k] = new AnimationCurve(new Keyframe(t[0], v[0][k], 0f, 0f), new Keyframe(t[n - 1], v[0][k], 0f, 0f));
                return curves;
            }
            var dv = new float[n][];
            for (int i = 0; i < n; i++)
            {
                dv[i] = new float[m];
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                float dt = Mathf.Max(t[b] - t[a], 1e-6f);
                for (int k = 0; k < m; k++) dv[i][k] = (v[b][k] - v[a][k]) / dt;
            }
            var keep = new List<int> { 0 };
            var tmp = new float[m];
            int s0 = 0;
            for (int j = 2; j < n; j++)
            {
                bool ok = true;
                for (int i = s0 + 1; i < j && ok; i++)
                {
                    Hermite(t, v, dv, s0, j, t[i], tmp);
                    if (Error(tmp, v[i], ch.Kind) > ch.Tol) ok = false;
                }
                if (!ok) { keep.Add(j - 1); s0 = j - 1; }
            }
            if (n > 1) keep.Add(n - 1);
            if (ch.Src != null && keep.Count >= ch.Src.Max(c => c.length))
            {
                // 원본(60fps 접선 Hermite)이 더 적은 키로 정확하다 → 원본 그대로
                for (int k = 0; k < m; k++) curves[k] = new AnimationCurve(ch.Src[k].keys);
                st.KeptSource++;
                return curves;
            }
            for (int k = 0; k < m; k++)
            {
                var keys = new Keyframe[keep.Count];
                for (int i = 0; i < keep.Count; i++) keys[i] = new Keyframe(t[keep[i]], v[keep[i]][k], dv[keep[i]][k], dv[keep[i]][k]);
                curves[k] = new AnimationCurve(keys);
            }
            // 확인: Unity 곡선으로 표본 시각을 다시 재 본다
            var e = new float[m];
            float worst = 0f;
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < m; k++) e[k] = curves[k].Evaluate(t[i]);
                worst = Mathf.Max(worst, Error(e, v[i], ch.Kind));
            }
            if (ch.Kind == Kind.Rotation) st.MaxRotErr = Mathf.Max(st.MaxRotErr, worst);
            else if (ch.Kind == Kind.Position) st.MaxPosErr = Mathf.Max(st.MaxPosErr, worst / Mathf.Max(ch.Tol, 1e-9f) * PosTolWorld);
            return curves;
        }

        static void Hermite(float[] t, float[][] v, float[][] dv, int a, int b, float x, float[] outV)
        {
            float dt = t[b] - t[a], s = (x - t[a]) / Mathf.Max(dt, 1e-9f), s2 = s * s, s3 = s2 * s;
            float h00 = 2 * s3 - 3 * s2 + 1, h10 = s3 - 2 * s2 + s, h01 = -2 * s3 + 3 * s2, h11 = s3 - s2;
            for (int k = 0; k < outV.Length; k++) outV[k] = h00 * v[a][k] + h10 * dt * dv[a][k] + h01 * v[b][k] + h11 * dt * dv[b][k];
        }

        // ---------- 도움 ----------
        static void Classify(EditorCurveBinding b, out Kind kind, out string prefix, out string comp)
        {
            string p = b.propertyName;
            int dot = p.LastIndexOf('.');
            string head = dot > 0 ? p.Substring(0, dot) : p, tail = dot > 0 ? p.Substring(dot + 1) : "";
            if (b.type == typeof(Transform) && tail.Length == 1)
            {
                switch (head)
                {
                    case "m_LocalPosition": kind = Kind.Position; prefix = head; comp = tail; return;
                    case "m_LocalRotation": kind = Kind.Rotation; prefix = head; comp = tail; return;
                    case "m_LocalScale": kind = Kind.Scale; prefix = head; comp = tail; return;
                    case "localEulerAnglesRaw":
                    case "localEulerAngles":
                    case "localEulerAnglesBaked": kind = Kind.Euler; prefix = head; comp = tail; return;
                }
            }
            kind = Kind.Float; prefix = p; comp = p;
        }

        static int CompOrder(string c)
        {
            switch (c) { case "x": return 0; case "y": return 1; case "z": return 2; case "w": return 3; default: return 4; }
        }

        static float Tolerance(Kind kind, Transform root, string path)
        {
            switch (kind)
            {
                case Kind.Rotation:
                case Kind.Euler: return RotTolDeg;
                case Kind.Scale: return ScaleTol;
                case Kind.Position:
                {
                    // 위치 곡선은 부모 공간 값 → 월드 허용 오차를 부모의 월드 배율로 나눈다(3DS 출처 루트 0.01 → 뼈 로컬 0.1)
                    var t = root ? (path.Length == 0 ? root : root.Find(path)) : null;
                    float s = t && t.parent ? Mathf.Abs(t.parent.lossyScale.x) : 1f;
                    return PosTolWorld / Mathf.Max(s, 1e-6f);
                }
                default: return FloatTol;
            }
        }

        static void NormalizeQuats(float[][] v)
        {
            for (int i = 0; i < v.Length; i++)
            {
                var q = v[i];
                float len = Mathf.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
                if (len < 1e-8f) { q[0] = q[1] = q[2] = 0f; q[3] = 1f; len = 1f; }
                for (int k = 0; k < 4; k++) q[k] /= len;
                if (i > 0)
                {
                    var p = v[i - 1];
                    if (p[0] * q[0] + p[1] * q[1] + p[2] * q[2] + p[3] * q[3] < 0f) for (int k = 0; k < 4; k++) q[k] = -q[k];
                }
            }
        }

        static bool Constant(float[][] v, Kind kind, float tol)
        {
            for (int i = 1; i < v.Length; i++) if (Error(v[0], v[i], kind) > tol) return false;
            return true;
        }

        /// <summary>두 값 사이 오차: 회전 = 각도(°), 위치 = 거리, 그 밖 = 성분 최대 차.</summary>
        static float Error(float[] a, float[] b, Kind kind)
        {
            switch (kind)
            {
                case Kind.Rotation:
                {
                    float la = Mathf.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2] + a[3] * a[3]);
                    float lb = Mathf.Sqrt(b[0] * b[0] + b[1] * b[1] + b[2] * b[2] + b[3] * b[3]);
                    float d = Mathf.Abs(a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3]) / Mathf.Max(la * lb, 1e-12f);
                    return 2f * Mathf.Acos(Mathf.Min(1f, d)) * Mathf.Rad2Deg;
                }
                case Kind.Position:
                {
                    float s = 0f;
                    for (int k = 0; k < a.Length; k++) { float x = a[k] - b[k]; s += x * x; }
                    return Mathf.Sqrt(s);
                }
                default:
                {
                    float mx = 0f;
                    for (int k = 0; k < a.Length; k++) mx = Mathf.Max(mx, Mathf.Abs(a[k] - b[k]));
                    return mx;
                }
            }
        }
    }
}
