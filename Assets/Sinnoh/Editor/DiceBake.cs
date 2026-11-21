using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Sinnoh.Fx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Sinnoh.Editor
{
    // 주사위 굴림 녹화본 굽기(결정 48 ①·50, spikes/dice-roll §2·§3): PhysX로 주사위 n개를 굴림판에 던져 끝까지 굴리고, 걸러내기를 통과한 굴림만 60 Hz로 적어 묶음으로 만든다.
    //   메뉴 Sinnoh/Build/Bake Dice Rolls,
    //   배치: Unity.exe -batchmode -projectPath . -executeMethod Sinnoh.Editor.DiceBake.Run -logFile <log> [-diceCheck] [-diceOut <파일>]
    //   -diceCheck: 굽기만 하고 추적 파일과 바이트로 견준다(쓰지 않는다). 같으면 종료 0, 다르면 1.
    // 출력: Assets/Sinnoh/Data/Dice/rolls.bytes(추적 — 형식은 Sinnoh.Fx.DiceClip·DiceLibrary), Logs/DiceBake/bake.txt·rolls.json(git 밖: 벌마다 씨앗·분류·길이·충돌 수).
    // 묶음: 주사위 2~5개 24벌 · 1개와 6~9개 12벌 = 156벌. 잔잔한(강한 충돌 0회) 굴림 비율 2개 2/3 · 3개 1/2 · 4개 1/3 · 5개 이상 0.
    // 굴림마다 물리 장면을 새로 만든다(앞선 굴림의 PhysX 내부 상태가 남지 않게 → 같은 씨앗 = 같은 궤적). 값을 조금만 바꿔도 굴림 후반이 달라지므로, 값을 고치면 묶음은 새로 고른 것이 된다.
    // 실행 중(게임)에는 물리를 쓰지 않는다 — 이 도구만 쓴다. 전역 Physics 설정은 굽는 동안만 바꾸고 되돌린다.
    public static class DiceBake
    {
        public const string OutPath = "Assets/Sinnoh/Data/Dice/rolls.bytes";
        public const string LogDir = "Logs/DiceBake";

        // ---------- 확정 값(spikes/dice-roll §3) ----------
        const float Gravity = 70f, Dt = 1f / 240f, MaxT = 3f;
        const int Every = 4;                                    // 240 Hz 물리 → 60 Hz 기록
        // 재질(곱하기로 합친다): 주사위끼리 탄성 0.60 · 바닥 0.42 · 벽 0.49, 마찰 주사위끼리 0.25 · 바닥 0.35 · 벽 0.15
        const float DieBounce = 0.7746f, DieFric = 0.5f, FloorBounce = 0.5455f, FloorFric = 0.7f, WallBounce = 0.6294f, WallFric = 0.3f;
        const float RelGap = 0.10f, VxJit = 4f;
        const float LenMin = 0.8f, LenMax = 1.6f;
        const float StrongJ = 8f;                               // 강한 충돌: 주사위끼리 닿는 순간의 충격량
        const float CenterTol = 0.15f, SpreadMin = 0.15f;
        // 판 그림 위 끝(세계 단위로 본 그림 높이 = (93 − 4.5) ÷ 24 = (124 − 6) ÷ 32)에서 주사위 가운데가 0.7칸 안으로 올라오면 모서리가 그림 밖으로 잘린다 [가정 — 옮기며 더한 걸러내기]
        const float TopLimit = 3.6875f - 0.7f;

        sealed class Throw
        {
            public float YMin, YMax, VxMin, VxMax, VyMin, VyMax, Vz, WMin, WMax;
        }
        // 부딪힘(세게): 위에서 내리꽂듯 — 오른쪽 쏠림을 잡는다
        static readonly Throw Hard = new Throw { YMin = 1.4f, YMax = 2.4f, VxMin = 19f, VxMax = 37f, VyMin = -14f, VyMax = -6f, Vz = 7f, WMin = 16f, WMax = 40f };
        static readonly Throw Calm = new Throw { YMin = 0.9f, YMax = 1.6f, VxMin = 15f, VxMax = 30f, VyMin = -1f, VyMax = 2f, Vz = 5f, WMin = 10f, WMax = 28f };

        static int Count(int n) => n >= 2 && n <= 5 ? 24 : 12;
        static int CalmCount(int n) => n == 2 ? 16 : n == 3 ? 12 : n == 4 ? 8 : 0;

        sealed class Roll
        {
            public int N, Steps;
            public float Settle;
            public string Reject;
            public Vector3[] Pos;
            public Quaternion[] Rot;
            public bool[] Thrown;
            public int Hits, Strong;
            public float MaxJ;
            public Vector3 P(int step, int i) => Pos[step * N + i];
        }

        [MenuItem("Sinnoh/Build/Bake Dice Rolls")]
        static void Menu() => Bake(false, OutPath);

        public static void Run()
        {
            bool ok = false;
            try
            {
                var args = Environment.GetCommandLineArgs();
                int o = Array.IndexOf(args, "-diceOut");
                ok = Bake(Array.IndexOf(args, "-diceCheck") >= 0, o >= 0 && o + 1 < args.Length ? args[o + 1] : OutPath);
            }
            catch (Exception e) { Debug.LogException(e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Bake(bool check, string outPath)
        {
            var mode = Physics.simulationMode;
            var gravity = Physics.gravity;
            int solver = Physics.defaultSolverIterations, solverV = Physics.defaultSolverVelocityIterations;
            float bounce = Physics.bounceThreshold, sleep = Physics.sleepThreshold, offset = Physics.defaultContactOffset;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Physics.gravity = new Vector3(0, -Gravity, 0);
                Physics.defaultSolverIterations = 12;
                Physics.defaultSolverVelocityIterations = 4;
                Physics.bounceThreshold = 1.5f;
                Physics.sleepThreshold = 0.005f;
                Physics.defaultContactOffset = 0.01f;
                return BakeAll(check, outPath);
            }
            finally
            {
                Physics.simulationMode = mode;
                Physics.gravity = gravity;
                Physics.defaultSolverIterations = solver;
                Physics.defaultSolverVelocityIterations = solverV;
                Physics.bounceThreshold = bounce;
                Physics.sleepThreshold = sleep;
                Physics.defaultContactOffset = offset;
            }
        }

        static bool BakeAll(bool check, string outPath)
        {
            var total = Stopwatch.StartNew();
            Simulate(4, 999, Calm);   // 첫 굴림 전에 물리를 한 번 데운다
            var clips = new List<byte[]>();
            var report = new StringBuilder("n | 벌(부딪힘/잔잔) | 시도 | 물리 통과 | 걸러냄(물리/분류/쏠림) | ms | 크기 B(gzip)\n");
            var meta = new StringBuilder("{\"rows\":[\n");
            for (int n = DiceGeo.MinDice; n <= DiceGeo.MaxDice; n++)
            {
                var sw = Stopwatch.StartNew();
                int calm = CalmCount(n), hit = Count(n) - calm, tries = 0, physOk = 0, rejPhys = 0, rejClass = 0, rejCenter = 0, bytes = 0;
                var group = new List<byte>();
                foreach (bool wantHit in new[] { true, false })
                {
                    int want = wantHit ? hit : calm, got = 0;
                    for (int s = 0; got < want; s++)
                    {
                        if (s >= 20000) throw new Exception($"굽기 실패: 주사위 {n}개 {(wantHit ? "부딪힘" : "잔잔")} {got}/{want}");
                        int seed = 300000 * n + (wantHit ? 0 : 100000) + s;
                        var r = Simulate(n, seed, wantHit ? Hard : Calm);
                        tries++;
                        if (r.Reject != null) { rejPhys++; continue; }
                        physOk++;
                        // 부딪힘 굴림 = 강한 충돌 1회 이상 + (4개 이상이면) 충돌 2회 이상. 잔잔한 굴림 = 강한 충돌 0회. 1개는 부딪힐 상대가 없다
                        bool isHit = n == 1 || (r.Strong >= 1 && (n < 4 || r.Hits >= 2));
                        if (wantHit ? !isHit : r.Strong != 0) { rejClass++; continue; }
                        if (!Centered(r, out float mx, out float sx)) { rejCenter++; continue; }
                        var one = Encode(r);
                        clips.Add(one);
                        group.AddRange(one);
                        bytes += one.Length;
                        meta.Append(clips.Count > 1 ? ",\n" : "").Append("{\"n\":").Append(n).Append(",\"seed\":").Append(seed).Append(",\"cls\":\"").Append(wantHit ? "hit" : "calm")
                            .Append("\",\"settle\":").Append(F2(r.Settle)).Append(",\"total\":").Append(r.Hits).Append(",\"strong\":").Append(r.Strong).Append(",\"maxJ\":").Append(F2(r.MaxJ))
                            .Append(",\"mx\":").Append(F2(mx)).Append(",\"sx\":").Append(F2(sx)).Append(",\"bytes\":").Append(one.Length).Append('}');
                        got++;
                    }
                }
                report.Append($"{n} | {Count(n)} ({hit}/{calm}) | {tries} | {physOk} ({100 * physOk / tries}%) | {rejPhys}/{rejClass}/{rejCenter} | {sw.ElapsedMilliseconds} | {bytes} ({Gzip(group.ToArray())})\n");
            }
            meta.Append("\n]}\n");
            var lib = DiceLibrary.Build(clips);
            DiceLibrary.Parse(lib);   // 읽히는지
            report.Append($"합계 | {clips.Count}벌 | {total.ElapsedMilliseconds} ms | {lib.Length} B (gzip {Gzip(lib)} B) — 60 Hz, 주사위·프레임당 10바이트\n");
            foreach (string line in report.ToString().Split('\n')) if (line.Length > 0) Debug.Log("[DiceBake] " + line);

            Directory.CreateDirectory(LogDir);
            File.WriteAllText(Path.Combine(LogDir, check ? "bake-check.txt" : "bake.txt"), report.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(LogDir, check ? "rolls-check.json" : "rolls.json"), meta.ToString(), new UTF8Encoding(false));
            if (check)
            {
                var have = File.Exists(outPath) ? File.ReadAllBytes(outPath) : null;
                bool same = have != null && have.SequenceEqual(lib);
                if (same) Debug.Log($"[DiceBake] 통과: 다시 구운 묶음이 {outPath}와 같다({lib.Length} B)");
                else Debug.LogError($"[DiceBake] 실패: 다시 구운 묶음이 {outPath}와 다르다({(have == null ? "파일 없음" : have.Length + " B")} ≠ {lib.Length} B)");
                return same;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, lib);
            if (outPath.StartsWith("Assets/", StringComparison.Ordinal)) AssetDatabase.ImportAsset(outPath);
            Debug.Log($"[DiceBake] 통과: {clips.Count}벌 {lib.Length} B → {outPath}");
            return true;
        }

        static string F2(float v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        static long Gzip(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(raw, 0, raw.Length);
                return ms.Length;
            }
        }

        static byte[] Encode(Roll r)
        {
            int frames = (r.Steps - 1) / Every + 1, n = r.N;
            var pos = new V3[frames * n];
            var rot = new Q4[frames * n];
            var thrown = new bool[frames * n];
            for (int f = 0; f < frames; f++)
                for (int i = 0; i < n; i++)
                {
                    int src = Math.Min(f * Every, r.Steps - 1) * n + i;
                    pos[f * n + i] = new V3(r.Pos[src].x, r.Pos[src].y, r.Pos[src].z);
                    rot[f * n + i] = new Q4(r.Rot[src].x, r.Rot[src].y, r.Rot[src].z, r.Rot[src].w);
                    thrown[f * n + i] = r.Thrown[src];
                }
            var bytes = new List<byte>();
            DiceClip.Write(bytes, n, frames, Dt * Every, r.Settle, pos, rot, thrown);
            return bytes.ToArray();
        }

        // 멈춘 자리: 가로 평균이 판 가운데 ±15% 안 + 가로로 퍼짐(3개 이상: 표준편차 ≥ 판 폭의 15%, 2개: 간격 ≥ 20%, 1개: 가운데 ±25%)
        static bool Centered(Roll r, out float mean, out float sd)
        {
            int last = r.Steps - 1;
            float m = 0, v = 0, W = DiceGeo.Width;
            for (int i = 0; i < r.N; i++) m += r.P(last, i).x;
            m /= r.N;
            for (int i = 0; i < r.N; i++) { float d = r.P(last, i).x - m; v += d * d; }
            mean = m / W;
            sd = Mathf.Sqrt(v / r.N) / W;
            if (r.N == 1) return Mathf.Abs(mean - 0.5f) <= 0.25f;
            if (Mathf.Abs(mean - 0.5f) > CenterTol) return false;
            return r.N == 2 ? sd * 2 >= 0.2f : sd >= SpreadMin;
        }

        static PhysicsMaterial Mat(float bounce, float fric) => new PhysicsMaterial
        {
            bounciness = bounce, dynamicFriction = fric, staticFriction = fric * 1.15f, bounceCombine = PhysicsMaterialCombine.Multiply, frictionCombine = PhysicsMaterialCombine.Multiply,
        };

        static BoxCollider Box(GameObject root, string name, Vector3 center, Vector3 size, PhysicsMaterial mat)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root.transform, false);
            g.transform.position = center;
            var b = g.AddComponent<BoxCollider>();
            b.size = size;
            b.sharedMaterial = mat;
            return b;
        }

        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        /// <summary>왼쪽 벽 밖 0.6칸에서 두 줄로 차례로 던져 넣고(왼쪽 벽은 주사위가 0.55칸 들어온 뒤 그 주사위에게만 막힌다) 다 멈출 때까지 굴린다.</summary>
        static Roll Simulate(int n, int seed, Throw th)
        {
            var rng = new System.Random(seed);
            float W = DiceGeo.Width, D = DiceGeo.Depth;
            var root = new GameObject("DiceBakeWorld");
            var scene = EditorSceneManager.NewPreviewScene();
            var made = new List<Object>();
            try
            {
                SceneManager.MoveGameObjectToScene(root, scene);
                var ps = scene.GetPhysicsScene();
                if (!ps.IsValid() || ps == Physics.defaultPhysicsScene) throw new Exception("따로 떨어진 물리 장면이 아니다");
                var pf = Mat(FloorBounce, FloorFric);
                var pw = Mat(WallBounce, WallFric);
                var pd = Mat(DieBounce, DieFric);
                made.Add(pf);
                made.Add(pw);
                made.Add(pd);
                Box(root, "floor", new Vector3(W / 2, -1, D / 2), new Vector3(W + 60, 2, D + 60), pf);
                Box(root, "ceil", new Vector3(W / 2, 9, D / 2), new Vector3(W + 60, 2, D + 60), pw);
                Box(root, "right", new Vector3(W + 1, 4, D / 2), new Vector3(2, 12, D + 60), pw);
                Box(root, "back", new Vector3(W / 2, 4, D + 1), new Vector3(W + 60, 12, 2), pw);
                Box(root, "front", new Vector3(W / 2, 4, -1), new Vector3(W + 60, 12, 2), pw);
                var left = Box(root, "left", new Vector3(-1, 4, D / 2), new Vector3(2, 12, D + 4), pw);
                var go = new GameObject[n];
                var rb = new Rigidbody[n];
                var col = new BoxCollider[n];
                var index = new Dictionary<Collider, int>();
                for (int i = 0; i < n; i++)
                {
                    var d = new GameObject("die" + i);
                    d.transform.SetParent(root.transform, false);
                    col[i] = d.AddComponent<BoxCollider>();
                    col[i].size = Vector3.one;
                    col[i].sharedMaterial = pd;
                    col[i].providesContacts = true;
                    rb[i] = d.AddComponent<Rigidbody>();
                    rb[i].mass = 1f;
                    rb[i].angularDamping = 0.05f;
                    rb[i].linearDamping = 0f;
                    rb[i].maxAngularVelocity = 100f;
                    rb[i].collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    rb[i].interpolation = RigidbodyInterpolation.None;
                    go[i] = d;
                    index[col[i]] = i;
                }

                var roll = new Roll { N = n };
                Physics.ContactEventDelegate onContact = (sc, headers) =>
                {
                    for (int a = 0; a < headers.Length; a++)
                    {
                        var h = headers[a];
                        for (int b = 0; b < h.pairCount; b++)
                        {
                            ref readonly var pair = ref h.GetContactPair(b);
                            if (!pair.isCollisionEnter) continue;
                            if (!index.TryGetValue(pair.collider, out int i) || !index.TryGetValue(pair.otherCollider, out int j)) continue;
                            var pi = rb[i].position;
                            var pj = rb[j].position;
                            if (pi.x < 0.3f || pj.x < 0.3f || pi.x > W - 0.3f || pj.x > W - 0.3f) continue;   // 판 밖(들어오기 전)의 충돌은 안 보인다
                            float impulse = pair.impulseSum.magnitude;
                            roll.Hits++;
                            roll.MaxJ = Mathf.Max(roll.MaxJ, impulse);
                            if (impulse >= StrongJ) roll.Strong++;
                        }
                    }
                };

                var rel = new float[n];
                var p0 = new Vector3[n];
                var v0 = new Vector3[n];
                var w0 = new Vector3[n];
                var q0 = new Quaternion[n];
                for (int i = 0; i < n; i++)
                {
                    int lane = i % 2, k = i / 2;
                    float ord = n <= 1 ? 0.5f : (float)i / (n - 1);
                    float zLane = n == 1 ? D * 0.5f + R(rng, -0.4f, 0.4f) : D * (lane == 0 ? 0.28f : 0.72f) + R(rng, -0.18f, 0.18f);
                    rel[i] = k * RelGap + lane * RelGap * 0.5f + R(rng, 0, 0.02f);
                    p0[i] = new Vector3(-0.6f, R(rng, th.YMin, th.YMax), zLane);
                    float vz = R(rng, -th.Vz, th.Vz);
                    v0[i] = new Vector3(Mathf.Lerp(th.VxMax, th.VxMin, ord) + R(rng, -VxJit, VxJit), R(rng, th.VyMin, th.VyMax), vz);
                    // 축(고른 구면) × 회전 속도
                    float az = R(rng, -1, 1), at = R(rng, 0, Mathf.PI * 2), asq = Mathf.Sqrt(1 - az * az);
                    w0[i] = new Vector3(asq * Mathf.Cos(at), asq * Mathf.Sin(at), az) * R(rng, th.WMin, th.WMax);
                    // 처음 방향(고른 회전)
                    float u1 = R(rng, 0, 1), u2 = R(rng, 0, Mathf.PI * 2), u3 = R(rng, 0, Mathf.PI * 2), qa = Mathf.Sqrt(1 - u1), qb = Mathf.Sqrt(u1);
                    q0[i] = new Quaternion(qa * Mathf.Sin(u2), qa * Mathf.Cos(u2), qb * Mathf.Sin(u3), qb * Mathf.Cos(u3));
                    go[i].SetActive(false);
                }

                int maxSteps = Mathf.CeilToInt(MaxT / Dt), need = Mathf.CeilToInt(0.15f / Dt), still = 0, settleStep = -1, steps = 0;
                var pos = new List<Vector3>(maxSteps * n);
                var rot = new List<Quaternion>(maxSteps * n);
                var thrown = new List<bool>(maxSteps * n);
                var released = new bool[n];
                var gated = new bool[n];
                Physics.ContactEvent += onContact;
                try
                {
                    for (int step = 0; step < maxSteps; step++)
                    {
                        float t = step * Dt;
                        bool changed = false;
                        for (int i = 0; i < n; i++)
                        {
                            if (released[i] || t < rel[i]) continue;
                            go[i].transform.SetPositionAndRotation(p0[i], q0[i]);
                            go[i].SetActive(true);
                            Physics.IgnoreCollision(col[i], left, true);
                            rb[i].position = p0[i];
                            rb[i].rotation = q0[i];
                            rb[i].linearVelocity = v0[i];
                            rb[i].angularVelocity = w0[i];
                            released[i] = true;
                            changed = true;
                        }
                        if (changed) Physics.SyncTransforms();
                        for (int i = 0; i < n; i++)
                        {
                            pos.Add(released[i] ? rb[i].position : Vector3.zero);
                            rot.Add(released[i] ? rb[i].rotation : Quaternion.identity);
                            thrown.Add(released[i]);
                        }
                        steps++;
                        if (settleStep >= 0) break;
                        ps.Simulate(Dt);
                        bool all = true;
                        float vmax = 0, wmax = 0;
                        for (int i = 0; i < n; i++)
                        {
                            if (!released[i]) { all = false; continue; }
                            if (!gated[i])
                            {
                                if (rb[i].position.x > 0.55f) { Physics.IgnoreCollision(col[i], left, false); gated[i] = true; }
                                else all = false;
                            }
                            vmax = Mathf.Max(vmax, rb[i].linearVelocity.magnitude);
                            wmax = Mathf.Max(wmax, rb[i].angularVelocity.magnitude);
                        }
                        still = all && vmax < 0.06f && wmax < 0.12f ? still + 1 : 0;
                        if (still >= need) settleStep = step + 1 - still + 1;
                    }
                }
                finally { Physics.ContactEvent -= onContact; }

                roll.Steps = steps;
                roll.Pos = pos.ToArray();
                roll.Rot = rot.ToArray();
                roll.Thrown = thrown.ToArray();
                roll.Settle = settleStep < 0 ? -1 : settleStep * Dt;
                if (settleStep < 0) roll.Reject = "안 멈춤";
                else
                {
                    for (int i = 0; i < n && roll.Reject == null; i++)
                    {
                        var p = roll.P(steps - 1, i);
                        var up = Quaternion.Inverse(roll.Rot[(steps - 1) * n + i]) * Vector3.up;
                        float dot = Mathf.Max(Mathf.Abs(up.x), Mathf.Max(Mathf.Abs(up.y), Mathf.Abs(up.z)));
                        if (p.x < 0.5f || p.x > W - 0.5f || p.z < 0.5f || p.z > D - 0.5f || p.y < 0) roll.Reject = "판 밖";
                        else if (p.y > 0.56f) roll.Reject = "겹쳐 얹힘";
                        else if (dot < 0.9995f) roll.Reject = "기울어 멈춤";
                    }
                    // 높이 튄 주사위가 판 그림 위로 나가는 굴림(앞뒤 뒤집은 것까지)
                    float sinP = Mathf.Sin(DiceGeo.PitchDeg * Mathf.Deg2Rad), cosP = Mathf.Cos(DiceGeo.PitchDeg * Mathf.Deg2Rad);
                    for (int k = 0; k < roll.Pos.Length && roll.Reject == null; k++)
                        if (roll.Thrown[k] && Mathf.Max(roll.Pos[k].z, D - roll.Pos[k].z) * sinP + roll.Pos[k].y * cosP > TopLimit) roll.Reject = "그림 위로 나감";
                    if (roll.Reject == null && roll.Settle > LenMax) roll.Reject = "오래 구름";
                    if (roll.Reject == null && roll.Settle < LenMin) roll.Reject = "너무 짧음";
                }
                return roll;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var m in made) Object.DestroyImmediate(m);
            }
        }
    }
}
