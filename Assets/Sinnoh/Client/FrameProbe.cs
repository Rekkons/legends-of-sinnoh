using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sinnoh.Client
{
    /// <summary>
    /// Win64 프레임 시간(M4-2e, d3-3 §2-4·§4-3). 명령줄 <c>-sinnohFrameProbe &lt;폴더&gt;</c>로 띄운 플레이어에서만 켜진다 — 창 없는 <c>-batchmode</c>(그래픽 장치는 켬)로 띄운다.
    /// 보드 카메라를 1920×1080 RenderTexture에 그리며 vsync 끔·프레임 제한 없음. 깨어진 세계를 켠 울트라 버스트 판(칸 351·아이콘 173·피규어 33·말 6이 모두 보임, M4-2 검토 반영)에서
    /// 판(전체 줌·방향 0 / 지역 줌 천관산 정상 — 그림자 4캐스케이드가 보이는 칸 대부분에 걸리는 유일한 줌 / 근접 줌 천관산 정상)마다 예열 300 + 1,200프레임 동안
    /// FrameTimingManager(cpuFrameTime·cpuMainThreadFrameTime·cpuRenderThreadFrameTime·gpuFrameTime)와 unscaledDeltaTime을 모아(같은 프레임끼리 짝지음 — Align)
    /// &lt;폴더&gt;/frametime-win64.json(프레임별 값 + 요약), 판마다 끝 그림 win64-probe.png·win64-probe-region.png·win64-probe-close.png를 남기고 끝낸다.
    /// 종료 코드 0 = 그렸고(분홍 0·채움) Gen이 다 섰고 보인다(피규어 33·아이콘 173·칸 351·말 6) — 기준 ④ 합격 여부는 JSON <c>pass</c>(모든 판 p95 ≤ 16.7 ms, 가장 무거운 판 <c>heaviest</c>). 1 = 잰 값을 믿을 수 없다. 2 = 시간 초과.
    /// 카메라가 배치 모드에서 스스로 그리지 않으면 매 프레임 SingleCameraRequest로 한 번 그린다(<c>renderMode</c>에 적음).
    /// </summary>
    public sealed class FrameProbe : MonoBehaviour
    {
        public const string Arg = "-sinnohFrameProbe";
        public const int Width = 1920, Height = 1080, Warmup = 300, Frames = 1200;
        public const double ThresholdMs = 16.7;
        /// <summary>예열 중 이 프레임에서 카메라가 스스로 그렸는지 본다.</summary>
        const int ModeCheckFrame = 30;
        /// <summary>FrameTimingManager 지연(프레임)을 이만큼까지 찾는다. 잰 구간 앞 이만큼의 예열 프레임부터 타이밍을 읽어 첫 프레임들도 짝을 얻는다.</summary>
        public const int MaxLag = 8;
        const float TimeoutSeconds = 600f;
        // 그렸는지 문턱(d3-3 §2-4): 지역·근접 25%, 전체는 ①과 같은 15%(판 모양 때문에 전체 줌 채움이 21.7~23.8%, d3-3 §2-1 바꾼 까닭)
        const float MinCoverageFull = 0.15f, MinCoverageClose = 0.25f;

        /// <summary>정렬한 값의 가까운 순위 백분위(p = 0~1, 순위 = ⌈p·n⌉)·평균·최대.</summary>
        public sealed class Summary
        {
            public int Count;
            public double Mean, Median, P95, P99, Max;
        }

        public static Summary Summarize(IList<double> values)
        {
            var s = new Summary { Count = values.Count };
            if (values.Count == 0) return s;
            var v = new List<double>(values);
            v.Sort();
            double sum = 0;
            foreach (var x in v) sum += x;
            s.Mean = sum / v.Count;
            s.Median = Rank(v, 0.5);
            s.P95 = Rank(v, 0.95);
            s.P99 = Rank(v, 0.99);
            s.Max = v[v.Count - 1];
            return s;
        }

        static double Rank(List<double> sorted, double p) => sorted[Math.Max(0, Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1))];

        /// <summary>
        /// FrameTimingManager 표본을 같은 프레임의 unscaledDeltaTime과 짝짓는다(M4-2 검토 반영). GetLatestTimings는 몇 프레임 전 프레임의 값을 주므로
        /// 표본 i(잰 구간 dt 색인 cpuAt[i]인 프레임에서 읽음, 예열이면 음수)는 dt[cpuAt[i] − lag]와 같은 프레임이다. lag = 0~maxLag 중 |cpu − dt| 평균이 가장 작은 값
        /// (짝 수가 dt의 절반 이상인 것만). 돌려주는 것: dt 색인마다 짝지은 표본 번호(없으면 −1). 표본이 빠져도(같은 frameStartTimestamp) 프레임 번호로 짝지어 뒤 짝이 밀리지 않는다.
        /// </summary>
        public static int[] Align(IList<double> dt, IList<int> cpuAt, IList<double> cpu, int maxLag, out int lag, out double meanAbsDiff)
        {
            lag = 0;
            meanAbsDiff = -1;
            double best = double.MaxValue;
            for (int l = 0; l <= maxLag; l++)
            {
                double sum = 0;
                int n = 0;
                for (int i = 0; i < cpu.Count; i++)
                {
                    int j = cpuAt[i] - l;
                    if (j < 0 || j >= dt.Count) continue;
                    sum += Math.Abs(cpu[i] - dt[j]);
                    n++;
                }
                if (n == 0 || n * 2 < dt.Count) continue;
                if (sum / n < best) { best = sum / n; lag = l; }
            }
            var at = new int[dt.Count];
            for (int j = 0; j < at.Length; j++) at[j] = -1;
            if (best == double.MaxValue) return at;
            meanAbsDiff = best;
            for (int i = 0; i < cpu.Count; i++)
            {
                int j = cpuAt[i] - lag;
                if (j >= 0 && j < at.Length) at[j] = i;
            }
            return at;
        }

        /// <summary>프레임 시간(d3-3 §2-4): gpu를 쟀으면 짝지은 표본마다 max(cpu, gpu), 못 쟀으면 dt 프레임마다 max(cpu, dt)(짝 없는 프레임은 dt), 짝이 없으면 dt.</summary>
        public static List<double> FrameTimes(IList<double> dt, int[] at, IList<double> cpu, IList<double> gpu, out string rule)
        {
            var ms = new List<double>();
            bool paired = false, gpuMeasured = false;
            for (int j = 0; j < at.Length; j++)
                if (at[j] >= 0) { paired = true; if (gpu[at[j]] > 0) gpuMeasured = true; }
            if (gpuMeasured)
            {
                rule = "max(cpuFrameTime,gpuFrameTime)";
                for (int j = 0; j < at.Length; j++) if (at[j] >= 0) ms.Add(Math.Max(cpu[at[j]], gpu[at[j]]));
            }
            else if (paired)
            {
                rule = "max(cpuFrameTime,unscaledDeltaTime) same frame";
                for (int j = 0; j < at.Length; j++) ms.Add(at[j] >= 0 ? Math.Max(cpu[at[j]], dt[j]) : dt[j]);
            }
            else
            {
                rule = "unscaledDeltaTime";
                ms.AddRange(dt);
            }
            return ms;
        }

        sealed class Pass
        {
            public string Name, Png;
            public BoardCameraRig.Zoom Zoom;
            public Vector3 Target;
            public float MinCoverage;
            public readonly List<double> Cpu = new List<double>(), CpuMain = new List<double>(), CpuRender = new List<double>(), Gpu = new List<double>(), Dt = new List<double>();
            /// <summary>표본마다 읽은 프레임의 dt 색인(잰 구간 첫 프레임 = 0, 예열은 음수).</summary>
            public readonly List<int> CpuAt = new List<int>();
            public int Renders, Pink = -1;
            public float Coverage = -1f;
            /// <summary>잰 구간 처음(예열 끝) 시각, 잰 구간 + 끝 그림 읽기(GPU가 줄 선 일을 다 끝낼 때까지 기다림)까지 벽시계, 그 읽기만.</summary>
            public double MeasureStart, WallMs, SyncMs;
        }

        string outDir;
        BoardBoot boot;
        Camera cam;
        BoardCameraRig rig;
        RenderTexture rt;
        Texture2D tex;
        readonly List<Pass> passes = new List<Pass>();
        readonly FrameTiming[] timings = new FrameTiming[1];
        ulong lastStamp;
        int passIndex, frame;
        bool manual, done;
        string failure;
        float t0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == Arg)
                {
                    new GameObject("FrameProbe").AddComponent<FrameProbe>().outDir = args[i + 1];
                    return;
                }
        }

        void Start()
        {
            t0 = Time.realtimeSinceStartup;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            boot = FindAnyObjectByType<BoardBoot>();
            cam = Camera.main;
            rig = cam != null ? cam.GetComponent<BoardCameraRig>() : null;
            if (boot == null || rig == null || boot.Board == null) { Finish("씬에 BoardBoot·BoardCameraRig가 없거나 보드를 못 읽었다"); return; }
            Directory.CreateDirectory(outDir);
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            cam.targetTexture = rt;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            // 가장 무거운 판 = 울트라 버스트 판: 깨어진 세계 25칸·아이콘 11·네크로즈마까지 켠다(d3-3 §2-4, M4-2 검토 반영 — 전에는 숨긴 채 잼)
            boot.DistortionWorldVisible = true;

            boot.Board.TryGetIndex(new HexCoord(0, 0), out int summit);
            var look = boot.Looks[summit];
            var summitTop = new Vector3((float)look.X, (float)BoardLayout.TopY(boot.Board[summit]), (float)look.Z);
            passes.Add(new Pass { Name = "full", Png = "win64-probe.png", Zoom = BoardCameraRig.Zoom.Full, Target = Vector3.zero, MinCoverage = MinCoverageFull });
            passes.Add(new Pass { Name = "region", Png = "win64-probe-region.png", Zoom = BoardCameraRig.Zoom.Region, Target = summitTop, MinCoverage = MinCoverageClose });
            passes.Add(new Pass { Name = "close", Png = "win64-probe-close.png", Zoom = BoardCameraRig.Zoom.Close, Target = summitTop, MinCoverage = MinCoverageClose });
            BeginPass(0);
        }

        void OnDestroy() => RenderPipelineManager.endCameraRendering -= OnEndCamera;

        void OnEndCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c == cam && passIndex < passes.Count) passes[passIndex].Renders++;
        }

        void BeginPass(int i)
        {
            passIndex = i;
            frame = 0;
            rig.Set(passes[i].Zoom, 0, passes[i].Target);
        }

        void Update()
        {
            if (done || passes.Count == 0) return;
            if (Time.realtimeSinceStartup - t0 > TimeoutSeconds) { Finish("시간 초과 " + TimeoutSeconds + "초"); return; }
            var p = passes[passIndex];
            frame++;
            // 카메라가 배치 모드에서 스스로 그리지 않으면 이후 매 프레임 직접 그린다
            if (passIndex == 0 && frame == ModeCheckFrame && p.Renders < ModeCheckFrame / 2) manual = true;
            FrameTimingManager.CaptureFrameTimings();
            if (frame == Warmup) p.MeasureStart = Time.realtimeSinceStartupAsDouble;
            if (frame > Warmup) p.Dt.Add(Time.unscaledDeltaTime * 1000.0);
            // 타이밍은 몇 프레임 늦게 온다 — 예열 끝 MaxLag프레임부터 읽고, 읽은 프레임의 dt 색인을 같이 적어 Write에서 같은 프레임끼리 짝짓는다(Align)
            if (frame > Warmup - MaxLag && FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].frameStartTimestamp != lastStamp)
            {
                lastStamp = timings[0].frameStartTimestamp;
                p.CpuAt.Add(frame - Warmup - 1);
                p.Cpu.Add(timings[0].cpuFrameTime);
                p.CpuMain.Add(timings[0].cpuMainThreadFrameTime);
                p.CpuRender.Add(timings[0].cpuRenderThreadFrameTime);
                p.Gpu.Add(timings[0].gpuFrameTime);
            }
            if (frame >= Warmup + Frames)
            {
                EndPass(p);
                if (passIndex + 1 < passes.Count) BeginPass(passIndex + 1);
                else Finish(null);
            }
        }

        void LateUpdate()
        {
            if (manual && !done && rt != null)
            {
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, req);
            }
        }

        /// <summary>끝 그림: 지난 프레임이 그린 RT를 그대로 PNG로(채움), 분홍은 아이콘을 숨기고 한 번 더 그려 센다(d3-3 §2-2 (c)).</summary>
        void EndPass(Pass p)
        {
            Color32 bg = cam.backgroundColor;
            double s0 = Time.realtimeSinceStartupAsDouble;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            // 배치 모드는 화면에 내지 않아 gpuFrameTime을 못 잰다 — 끝 읽기가 GPU를 기다리므로 벽시계 ÷ 프레임이 GPU 평균 처리량의 위쪽 값이다
            double s1 = Time.realtimeSinceStartupAsDouble;
            p.SyncMs = (s1 - s0) * 1000.0;
            p.WallMs = (s1 - p.MeasureStart) * 1000.0;
            File.WriteAllBytes(Path.Combine(outDir, p.Png), tex.EncodeToPNG());
            OffscreenShot.Stats(tex, bg, out _, out p.Coverage);
            var icons = boot.Icons != null ? boot.Icons.Renderers : null;
            if (icons != null) foreach (var r in icons) r.enabled = false;
            OffscreenShot.Render(cam, rt, tex);
            if (icons != null) foreach (var r in icons) r.enabled = true;
            OffscreenShot.Stats(tex, bg, out p.Pink, out _);
        }

        void Finish(string why)
        {
            if (done) return;
            done = true;
            failure = why;
            int code = 1;
            try { code = Write(); }
            catch (Exception e) { Debug.LogException(e); }
            Debug.Log("[FrameProbe] 끝 — 종료 코드 " + code + (why != null ? " (" + why + ")" : ""));
            Application.Quit(code);
        }

        int Figures => boot != null && boot.Figures != null ? boot.Figures.Figures.Count : 0;
        int Icons => boot != null && boot.Icons != null ? boot.Icons.Icons.Count : 0;
        int Pieces => boot != null && boot.Pieces != null ? boot.Pieces.Pieces.Count : 0;

        // 화면에 켜진 수(읽어 들인 수와 따로): 칸은 메시 부분(본맵·깨어진 세계)이 켜졌는지, 아이콘·피규어는 계층에서 켜졌는지
        int VisibleCells()
        {
            if (boot == null || boot.View == null || boot.Board == null) return 0;
            int n = 0;
            for (int c = 0; c < boot.Board.Count; c++)
            {
                var r = boot.View.Renderer(boot.Board[c].DistortionWorld ? 1 : 0);
                if (r != null && r.gameObject.activeInHierarchy && r.enabled) n++;
            }
            return n;
        }

        int VisibleIcons()
        {
            if (boot == null || boot.Icons == null || !boot.Icons.gameObject.activeInHierarchy) return 0;
            int n = 0;
            foreach (var ic in boot.Icons.Icons) if (!boot.Board[ic.Cell].DistortionWorld || boot.DistortionWorldVisible) n++;
            return n;
        }

        int VisibleIconMeshes()
        {
            int n = 0;
            if (boot != null && boot.Icons != null) foreach (var r in boot.Icons.Renderers) if (r != null && r.gameObject.activeInHierarchy && r.enabled) n++;
            return n;
        }

        int VisibleFigures()
        {
            int n = 0;
            if (boot != null && boot.Figures != null) foreach (var f in boot.Figures.Figures) if (f.Root != null && f.Root.activeInHierarchy) n++;
            return n;
        }

        int Animating()
        {
            int n = 0;
            if (boot != null && boot.Figures != null)
                foreach (var f in boot.Figures.Figures)
                    if (f.Animation != null && f.Animation.isPlaying) n++;
            return n;
        }

        int Write()
        {
            var inv = CultureInfo.InvariantCulture;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            int visCells = VisibleCells(), visIcons = VisibleIcons(), visFigures = VisibleFigures();
            bool genOk = Figures == 33 && Icons == 173 && Pieces == 6 && visCells == 351 && visIcons == 173 && visFigures == 33;
            bool drawn = passes.Count > 0;
            bool pass = drawn && failure == null;
            var sb = new StringBuilder("{\"schema\":1");
            sb.Append(",\"env\":{");
            Str(sb, "platform", Application.platform.ToString()); sb.Append(',');
            Str(sb, "unity", Application.unityVersion); sb.Append(',');
            Str(sb, "graphicsApi", SystemInfo.graphicsDeviceType.ToString()); sb.Append(',');
            Str(sb, "gpu", SystemInfo.graphicsDeviceName); sb.Append(',');
            Str(sb, "gpuVersion", SystemInfo.graphicsDeviceVersion); sb.Append(',');
            Str(sb, "cpu", SystemInfo.processorType); sb.Append(',');
            sb.Append("\"cpuThreads\":").Append(SystemInfo.processorCount).Append(',');
#if ENABLE_IL2CPP
            Str(sb, "scriptingBackend", "IL2CPP"); sb.Append(',');
#else
            Str(sb, "scriptingBackend", "Mono"); sb.Append(',');
#endif
            sb.Append("\"development\":").Append(Debug.isDebugBuild ? "true" : "false").Append(',');
            sb.Append("\"batchmode\":").Append(Application.isBatchMode ? "true" : "false").Append(',');
            Str(sb, "quality", QualitySettings.names[QualitySettings.GetQualityLevel()]); sb.Append(',');
            Str(sb, "pipeline", urp != null ? urp.name : "?"); sb.Append(',');
            sb.Append("\"msaa\":").Append(urp != null ? urp.msaaSampleCount : 0).Append(',');
            sb.Append("\"hdr\":").Append(urp != null && urp.supportsHDR ? "true" : "false").Append(',');
            sb.Append("\"renderScale\":").Append((urp != null ? urp.renderScale : 0f).ToString("0.###", inv)).Append(',');
            sb.Append("\"shadowDistance\":").Append((urp != null ? urp.shadowDistance : 0f).ToString("0.###", inv)).Append(',');
            sb.Append("\"srpBatcher\":").Append(urp != null && urp.useSRPBatcher ? "true" : "false").Append(',');
            sb.Append("\"frameTimingStats\":").Append(FrameTimingManager.IsFeatureEnabled() ? "true" : "false").Append(',');
            sb.Append("\"vSyncCount\":").Append(QualitySettings.vSyncCount).Append(',');
            sb.Append("\"targetFrameRate\":").Append(Application.targetFrameRate).Append(',');
            Str(sb, "rt", Width + "x" + Height + " ARGB32 sRGB depth24"); sb.Append(',');
            Str(sb, "renderingThreading", SystemInfo.renderingThreadingMode.ToString()); sb.Append(',');
            Str(sb, "renderMode", manual ? "submit" : "camera");
            sb.Append("},\"board\":{\"cells\":").Append(boot != null && boot.Board != null ? boot.Board.Count : 0)
              .Append(",\"figures\":").Append(Figures).Append(",\"figuresAnimating\":").Append(Animating())
              .Append(",\"icons\":").Append(Icons).Append(",\"pieces\":").Append(Pieces)
              .Append(",\"distortionWorldVisible\":").Append(boot != null && boot.DistortionWorldVisible ? "true" : "false")
              .Append(",\"visibleCells\":").Append(visCells).Append(",\"visibleIcons\":").Append(visIcons)
              .Append(",\"visibleIconMeshes\":").Append(VisibleIconMeshes()).Append(",\"visibleFigures\":").Append(visFigures)
              .Append(",\"genOk\":").Append(genOk ? "true" : "false").Append('}');
            sb.Append(",\"warmup\":").Append(Warmup).Append(",\"frames\":").Append(Frames)
              .Append(",\"thresholdMs\":").Append(ThresholdMs.ToString("0.0", inv));
            sb.Append(",\"passes\":[");
            string heaviest = null;
            double heaviestP95 = -1;
            for (int i = 0; i < passes.Count; i++)
            {
                var p = passes[i];
                // 같은 프레임끼리 짝지은 값(Align): 타이밍 표본은 지연 lag 프레임 뒤에 읽힌다. 예열 프레임 값은 짝이 없어 빠진다
                var at = Align(p.Dt, p.CpuAt, p.Cpu, MaxLag, out int lag, out double lagDiff);
                List<double> cpu = new List<double>(), cpuMain = new List<double>(), cpuRender = new List<double>(), gpu = new List<double>();
                int paired = 0;
                for (int j = 0; j < at.Length; j++)
                {
                    if (at[j] < 0) continue;
                    paired++;
                    cpu.Add(p.Cpu[at[j]]); cpuMain.Add(p.CpuMain[at[j]]); cpuRender.Add(p.CpuRender[at[j]]); gpu.Add(p.Gpu[at[j]]);
                }
                bool gpuMeasured = false;
                foreach (var g in gpu) if (g > 0) { gpuMeasured = true; break; }
                // 프레임 시간 = 프레임마다 max(cpu, gpu). gpu를 못 쟀으면 max(cpu, unscaledDeltaTime), FrameTimingManager가 아무것도 안 주면 unscaledDeltaTime (d3-3 §2-4)
                var frameMs = FrameTimes(p.Dt, at, p.Cpu, p.Gpu, out string rule);
                var fs = Summarize(frameMs);
                bool ok = p.Pink == 0 && p.Coverage >= p.MinCoverage && p.Renders >= Frames && frameMs.Count > Frames / 2;
                drawn &= ok;
                pass &= fs.Count > 0 && fs.P95 <= ThresholdMs;
                if (fs.P95 > heaviestP95) { heaviestP95 = fs.P95; heaviest = p.Name; }
                if (i > 0) sb.Append(',');
                sb.Append('{');
                Str(sb, "name", p.Name); sb.Append(',');
                Str(sb, "zoom", p.Zoom.ToString()); sb.Append(',');
                sb.Append("\"target\":[").Append(p.Target.x.ToString("0.###", inv)).Append(',').Append(p.Target.y.ToString("0.###", inv)).Append(',').Append(p.Target.z.ToString("0.###", inv)).Append("],");
                sb.Append("\"renders\":").Append(p.Renders).Append(",\"timings\":").Append(p.Cpu.Count).Append(",\"pairedFrames\":").Append(paired)
                  .Append(",\"timingLagFrames\":").Append(lag).Append(",\"timingLagMeanAbsDiffMs\":").Append(lagDiff.ToString("0.0000", inv))
                  .Append(",\"gpuMeasured\":").Append(gpuMeasured ? "true" : "false").Append(',');
                Str(sb, "frameRule", rule); sb.Append(',');
                Str(sb, "png", p.Png); sb.Append(',');
                sb.Append("\"pink\":").Append(p.Pink).Append(",\"coverage\":").Append(p.Coverage.ToString("0.0000", inv))
                  .Append(",\"minCoverage\":").Append(p.MinCoverage.ToString("0.00", inv)).Append(",\"valid\":").Append(ok ? "true" : "false")
                  .Append(",\"wallPerFrameMs\":").Append((p.WallMs / Frames).ToString("0.000", inv)).Append(",\"finishSyncMs\":").Append(p.SyncMs.ToString("0.000", inv));
                Sum(sb, "frame", fs);
                Sum(sb, "cpu", Summarize(cpu));
                Sum(sb, "cpuMain", Summarize(cpuMain));
                Sum(sb, "cpuRender", Summarize(cpuRender));
                Sum(sb, "gpu", Summarize(gpu));
                Sum(sb, "dt", Summarize(p.Dt));
                // 표본: 잰 프레임(dt 색인)마다 같은 프레임 값, 짝 없는 프레임은 -1
                sb.Append(",\"samples\":{");
                Arr(sb, "frame", frameMs); sb.Append(',');
                Arr(sb, "cpu", Paired(at, p.Cpu)); sb.Append(',');
                Arr(sb, "cpuMain", Paired(at, p.CpuMain)); sb.Append(',');
                Arr(sb, "cpuRender", Paired(at, p.CpuRender)); sb.Append(',');
                Arr(sb, "gpu", Paired(at, p.Gpu)); sb.Append(',');
                Arr(sb, "dt", p.Dt);
                sb.Append("}}");
            }
            sb.Append(']');
            drawn &= genOk && passes.Count == 3;
            pass &= drawn;
            sb.Append(",\"heaviest\":\"").Append(heaviest ?? "").Append("\",\"heaviestP95\":").Append(heaviestP95.ToString("0.000", inv));
            sb.Append(",\"valid\":").Append(drawn ? "true" : "false").Append(",\"pass\":").Append(pass ? "true" : "false");
            if (failure != null) { sb.Append(','); Str(sb, "failure", failure); }
            sb.Append('}');
            string path = Path.Combine(outDir, "frametime-win64.json");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            foreach (var p in passes)
            {
                var s = Summarize(p.Cpu);
                Debug.Log($"[FrameProbe] {p.Name}: 그림 {p.Renders}, 타이밍 {p.Cpu.Count}, cpu p95 {s.P95:0.00} ms, 분홍 {p.Pink}, 채움 {p.Coverage:0.0000}");
            }
            Debug.Log($"[FrameProbe] Gen 피규어 {Figures}(보임 {visFigures})·움직임 {Animating()}·아이콘 {Icons}(보임 {visIcons})·칸 보임 {visCells}·말 {Pieces}, 그리기 {(manual ? "submit" : "camera")}, 가장 무거운 판 {heaviest} p95 {heaviestP95:0.00} ms, valid {drawn}, pass {pass} → {path}");
            if (failure != null && failure.StartsWith("시간 초과", StringComparison.Ordinal)) return 2;
            return drawn ? 0 : 1;
        }

        static void Str(StringBuilder sb, string k, string v)
        {
            sb.Append('"').Append(k).Append("\":\"");
            foreach (char c in v ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            sb.Append('"');
        }

        static void Sum(StringBuilder sb, string k, Summary s)
        {
            var inv = CultureInfo.InvariantCulture;
            sb.Append(",\"").Append(k).Append("\":{\"n\":").Append(s.Count)
              .Append(",\"mean\":").Append(s.Mean.ToString("0.000", inv))
              .Append(",\"median\":").Append(s.Median.ToString("0.000", inv))
              .Append(",\"p95\":").Append(s.P95.ToString("0.000", inv))
              .Append(",\"p99\":").Append(s.P99.ToString("0.000", inv))
              .Append(",\"max\":").Append(s.Max.ToString("0.000", inv)).Append('}');
        }

        static List<double> Paired(int[] at, List<double> v)
        {
            var r = new List<double>(at.Length);
            foreach (int i in at) r.Add(i >= 0 ? v[i] : -1);
            return r;
        }

        static void Arr(StringBuilder sb, string k, List<double> v)
        {
            var inv = CultureInfo.InvariantCulture;
            sb.Append('"').Append(k).Append("\":[");
            for (int i = 0; i < v.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(v[i].ToString("0.###", inv));
            }
            sb.Append(']');
        }
    }
}
