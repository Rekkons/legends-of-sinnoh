using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sinnoh.Editor
{
    /// <summary>
    /// TTS 피규어 번들 → URP 프리팹(03 §2-5 파이프라인 2·4·5, d3-1 §6-3). 메뉴 <c>Sinnoh/Assets/Import Figures</c>, 배치 <c>-executeMethod Sinnoh.Editor.FigureImport.Run -quit</c>.
    /// <list type="number">
    /// <item>목록 <see cref="ListPath"/>(37행 = 33종 + 폼 4) → 번들 <c>AssetSources/tts/bundles/&lt;url sha1&gt;.unity3d</c>(받기 = <c>node Tools/assets/fetch.mjs figures</c>).
    /// 환경 변수 SINNOH_CACHE_ROOT, 없으면 작업 트리 설정 UserSettings/SinnohCacheRoot.txt(rebuild-all --cache-root가 씀)의 뿌리 아래 AssetSources를 본다(03 §7 빈 worktree — 에디터 메뉴도 같은 뿌리).</item>
    /// <item>그래픽 장치가 있어야 한다(텍스처를 GPU Blit·ReadPixels로 뽑는다). -nographics(Null 장치)면 빈 회색 PNG가 오류 없이 나오므로 시작에서 오류로 멈춘다.</item>
    /// <item>LoadFromFile → CollectDependencies → 텍스처는 RenderTexture로 PNG(원본 DXT1·RGB = 알파 없는 PNG → 들여오기 규칙이 DXT1, 아니면 DXT5) → 메시 복사 .asset →
    /// 재질: Standard = URP StandardUpgrader, Legacy Particles/Additive = URP Particles/Unlit 더하기(_BaseColor = 2×_TintColor), 번들 안 Particles/Standard Surface = URP ParticleUpgrader(→ Particles/Lit),
    /// 그 밖 셰이더는 URP Lit으로 옮기고 보고(figures.json shaders에 「other」).</item>
    /// <item>클립: 레거시는 30fps 다시 표본 + 키 줄이기, Mecanim은 표본해 레거시로 다시 굽기(<see cref="FigureClips"/>).</item>
    /// <item>TTSAssetBundleEffects(효과표 → figures.json)·Animator·숨은 Cube를 떼고, 번들 루트(3DS 출처 배율 0.01)를 배율 1인 새 루트 &lt;dex&gt;_&lt;state&gt; 밑에 둔다
    /// (루트 배율을 곱해 둔다 — 화면 코드는 새 루트에 fitScale만 준다).</item>
    /// <item>출력 Gen/Figures/&lt;dex&gt;_&lt;state&gt;/ + Gen/Figures/figures.json {key, fitScale, clips{Idle…TakeDamage → clip·length}, size, bytes, bundle, src…}.</item>
    /// <item>크기 기록(03 §2-5 「목표 하나 3 MB 이하」): bundle = 그 피규어만 담은 AssetBundle(LZ4, 지금 빌드 대상, URP 셰이더는 공용 번들로 뺌) 크기 = 나눠 받기 크기(03 §5),
    /// bytes = Gen/ 폴더의 텍스트 YAML 합(에디터 전용 곡선 m_EditorCurves·m_EulerEditorCurves가 대부분이라 받기 크기가 아니다, git 밖) [가정 M4-1e].</item>
    /// </list>
    /// 번들 sha256·들여오기 판이 figures.json과 같고 프리팹이 있으면 건너뛴다(두 번째 실행 바뀐 파일 0).
    /// fitScale [가정, 결정 22 Q3 · d3-1 FigureScaleFit]: TTS 배율 크기의 max(폭, 높이) d를 목록 전체 최소·최대 d의 로그 눈금으로 0.75~1.8에 옮긴 값을 배율 1 크기로 나눈 것(바닥 0.6 → 0.75: 결정 70 ⑦ — 쉐이미·유크시 같은 작은 종이 전체 줌에서 알아보이게).
    /// <para>바이트 결정성(03 §7, 들여오기 판 3): 새 작업 트리에서 들여도 파일·.meta가 바이트까지 같게 —
    /// GUID = 경로 해시(<see cref="StableGuid"/>: 프리팹·폴더·json은 .meta를 미리 쓰고, CreateAsset은 미리 쓴 .meta를 무시하므로 메시·재질·클립·PNG는 임시 폴더에서 Unity가 쓴 파일·.meta를 GUID만 바꿔 옮긴다 <see cref="SaveNative{T}"/>·<see cref="SavePng"/>),
    /// 프리팹 로컬 fileID = 계층 경로 해시(<see cref="CanonPrefab"/>), 재질 URP AssetVersion 부속 fileID 고정, GameObject 옛 칸 m_NavMeshLayer = 0,
    /// 클립·텍스처는 이름 순(공통 채널을 구울 값이 클립 순서에 따라 달라지던 것), 재질은 대상 셰이더가 선언하지 않은 저장 속성을 지운다.</para>
    /// </summary>
    public static class FigureImport
    {
        public const string OutRoot = "Assets/Sinnoh/Gen/Figures";
        public const string JsonPath = OutRoot + "/figures.json";
        public const string ListPath = "Tools/assets/tables/figure_list.json";
        /// <summary>들여오기 판. 출력이 바뀌는 고침을 하면 올린다(모두 다시 들인다).</summary>
        // 2: 메시 파일 이름 <이름>.asset(「.mesh.asset」이면 새로 만들 때 Unity가 메시 이름을 「….mesh」로 바꿔 덮어쓴 것과 달랐다, M4-1e 빈 worktree)
        // 3: 바이트 결정성(고정 GUID·fileID, m_NavMeshLayer 0, 클립 이름 순, 재질 안 쓰는 속성 지움 — M4-1 남은 일, 03 §7)
        // 4: 클립 사이 공통 채널은 모든 클립에 있을 때만 굽는다(없는 클립이 있는 채널을 구워 처음 Idle 자세가 번들과 달랐다 — 481 Hips 15°, M4-2c)
        public const int ImporterVersion = 4;
        public const float FitMin = 0.75f, FitMax = 1.8f;
        /// <summary>피규어 하나 받기 크기 목표 3 MB = 십진 3,000,000바이트 [가정 M4-1 남은 일, 03 §2-5](전에는 3 MiB = 3,145,728).</summary>
        public const long TargetBytes = 3_000_000;
        /// <summary>메시·재질·클립·PNG를 Unity 길로 만들었다 지우는 임시 폴더(<see cref="SaveNative{T}"/>·<see cref="SavePng"/>, 끝에 지운다).</summary>
        const string TmpDir = OutRoot + "/_tmp";

        /// <summary>TTS 효과 이름(Looping 4 + Trigger 4) → figures.json 키.</summary>
        public static readonly string[] Effects = { "Idle", "Walk", "Run", "Faint", "Physical Attack", "Special Attack", "Status Attack", "Take Damage" };
        public static string EffectKey(string effect) => effect.Replace(" ", "");

        public sealed class Result
        {
            public int Imported, Skipped;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Notes = new List<string>();
            public bool Ok => Errors.Count == 0;
        }

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        [MenuItem("Sinnoh/Assets/Import Figures")]
        public static void Menu() => ImportAll(false);

        [MenuItem("Sinnoh/Assets/Import Figures (Force)")]
        public static void MenuForce() => ImportAll(true);

        /// <summary>배치 입구. 실패하면 종료 코드 1.</summary>
        public static void Run()
        {
            var r = ImportAll(Environment.GetCommandLineArgs().Contains("-figuresForce"));
            if (Application.isBatchMode && !r.Ok) EditorApplication.Exit(1);
        }

        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>작업 트리 설정 파일(git 밖, Tools/sources.mjs CACHE_ROOT_FILE과 같다).</summary>
        public const string CacheRootFile = "UserSettings/SinnohCacheRoot.txt";

        /// <summary>git 밖 원천(AssetSources)의 뿌리: SINNOH_CACHE_ROOT, 없으면 <see cref="CacheRootFile"/>, 없으면 프로젝트.</summary>
        public static string CacheRoot
        {
            get
            {
                var c = Environment.GetEnvironmentVariable("SINNOH_CACHE_ROOT");
                if (!string.IsNullOrEmpty(c)) return Path.GetFullPath(c);
                string f = Path.Combine(ProjectRoot, CacheRootFile);
                string t = File.Exists(f) ? File.ReadAllText(f, Utf8).Trim() : "";
                return t.Length > 0 ? Path.GetFullPath(t) : ProjectRoot;
            }
        }

        public static string BundlePath(string url) => Path.Combine(CacheRoot, "AssetSources/tts/bundles", Hex(SHA1.Create().ComputeHash(Utf8.GetBytes(url))) + ".unity3d");
        public static string Key(int dex, int state) => dex + ":" + state;
        public static string Dir(int dex, int state) => OutRoot + "/" + dex + "_" + state;
        public static string PrefabPath(int dex, int state) => Dir(dex, state) + "/" + dex + "_" + state + ".prefab";

        public static Result ImportAll(bool force)
        {
            var res = new Result();
            var t0 = DateTime.Now;
            // 텍스처를 GPU로 뽑는다(ToPng). Null 장치(-nographics)에서는 회색 한 색 PNG가 오류 없이 나오고 다음 실행이 건너뛰어 그대로 남는다
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                res.Errors.Add("그래픽 장치 없음(-nographics): FigureImport는 텍스처를 GPU로 뽑으므로 -nographics 없이 돌린다(03 §2-5)");
                return Finish(res, t0);
            }
            JArray rows;
            try { rows = (JArray)Parse(File.ReadAllText(Path.Combine(ProjectRoot, ListPath), Utf8))["rows"]; }
            catch (Exception e) { res.Errors.Add(ListPath + " 읽기 실패: " + e.Message); return Finish(res, t0); }

            // Gen/Figures가 고정 GUID가 아니면(들여오기 판 2까지 = 무작위) 통째로 지우고 모두 다시 들인다(figures.json도 지워진다)
            if (AssetDatabase.IsValidFolder(OutRoot) && AssetDatabase.AssetPathToGUID(OutRoot) != StableGuid(OutRoot)) res.Notes.Add(OutRoot + " GUID가 고정 값이 아니라 다시 만듦");
            EnsureStableFolder(OutRoot);

            var old = new Dictionary<string, JObject>();
            string jsonFull = Path.Combine(ProjectRoot, JsonPath);
            if (File.Exists(jsonFull))
            {
                try
                {
                    var j = Parse(File.ReadAllText(jsonFull, Utf8));
                    if ((int?)j["importer"] == ImporterVersion)
                        foreach (JObject r in (JArray)j["rows"]) old[(string)r["key"]] = r;
                }
                catch (Exception e) { res.Notes.Add("figures.json 다시 만듦: " + e.Message); }
            }

            var outRows = new List<JObject>();
            var keepDirs = new HashSet<string>();
            try
            {
                int i = 0;
                foreach (JObject row in rows)
                {
                    int dex = (int)row["dex"], state = (int)row["state"];
                    string key = Key(dex, state), url = (string)row["url"];
                    keepDirs.Add(Dir(dex, state));
                    EditorUtility.DisplayProgressBar("Import Figures", key + " " + row["name"], (float)i++ / rows.Count);
                    string bundle = BundlePath(url);
                    if (!File.Exists(bundle)) { res.Errors.Add($"{key}: 번들 없음 {bundle} — node Tools/assets/fetch.mjs figures 먼저"); continue; }
                    string sha = Hex(SHA256.Create().ComputeHash(File.ReadAllBytes(bundle)));
                    if (!force && old.TryGetValue(key, out var prev) && (string)prev["src"] == sha
                        && File.Exists(Path.Combine(ProjectRoot, PrefabPath(dex, state))))
                    {
                        outRows.Add(prev); res.Skipped++; continue;
                    }
                    try
                    {
                        var r = ImportOne(row, bundle, sha, res);
                        if (r != null) { outRows.Add(r); res.Imported++; }
                    }
                    catch (Exception e) { res.Errors.Add($"{key}: {e.GetType().Name}: {e.Message} @ {FirstLine(e.StackTrace)}"); }
                    finally { AssetBundle.UnloadAllAssetBundles(true); }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            if (AssetDatabase.IsValidFolder(TmpDir)) AssetDatabase.DeleteAsset(TmpDir);
            // 목록에 없는 폴더는 지운다
            foreach (var d in AssetDatabase.GetSubFolders(OutRoot))
                if (!keepDirs.Contains(d)) { AssetDatabase.DeleteAsset(d); res.Notes.Add("지움 " + d); }

            // 크기: 새로 들인 것(또는 아직 안 잰 것)만 AssetBundle로 묶어 잰다
            var measure = outRows.Where(r => r["bundle"] == null || (long)r["bundle"] <= 0).ToList();
            if (measure.Count > 0 && res.Ok) MeasureBundles(measure, res);
            if (outRows.Count == rows.Count) WriteJson(outRows, res);
            else res.Errors.Add($"figures.json 안 씀: {outRows.Count}/{rows.Count}행");
            AssetDatabase.SaveAssets();
            return Finish(res, t0);
        }

        /// <summary>
        /// 피규어 하나의 실제 받기 크기(03 §5 나눠 받기): 지금 빌드 대상으로 피규어마다 AssetBundle(LZ4)을 Temp/FigureBundles에 만들어 파일 크기를 잰다.
        /// URP 셰이더는 공용 번들 fig_shaders 하나로 빼서(피규어마다 겹치지 않게) 따로 적는다. Gen/ YAML 크기(bytes)는 에디터 전용 곡선이 섞여 받기 크기가 아니다.
        /// </summary>
        static void MeasureBundles(List<JObject> rows, Result res)
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            string outDir = Path.Combine(ProjectRoot, "Temp/FigureBundles");
            Directory.CreateDirectory(outDir);
            var shaders = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { OutRoot }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                var sp = m && m.shader ? AssetDatabase.GetAssetPath(m.shader) : null;
                if (!string.IsNullOrEmpty(sp)) shaders.Add(sp);
            }
            string Name(JObject r) => "fig_" + ((string)r["key"]).Replace(':', '_');
            var builds = rows.Select(r => new AssetBundleBuild { assetBundleName = Name(r), assetNames = new[] { OutRoot + "/" + (string)r["prefab"] } }).ToList();
            builds.Add(new AssetBundleBuild { assetBundleName = "fig_shaders", assetNames = shaders.ToArray() });
            var t0 = DateTime.Now;
            var manifest = BuildPipeline.BuildAssetBundles(outDir, builds.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression, target);
            if (manifest == null) { res.Errors.Add("AssetBundle 만들기 실패(크기 못 잼)"); return; }
            foreach (var r in rows)
            {
                var f = new FileInfo(Path.Combine(outDir, Name(r)));
                r["bundle"] = f.Exists ? f.Length : -1;
                if (f.Length > TargetBytes) res.Notes.Add($"{r["key"]}: 번들 {f.Length:N0}바이트 — 목표 {TargetBytes:N0}바이트(3 MB) 넘음");
            }
            var sh = new FileInfo(Path.Combine(outDir, "fig_shaders"));
            res.Notes.Add($"번들 크기 잼: {rows.Count}개 + 셰이더 공용 {(sh.Exists ? sh.Length : 0):N0}바이트({string.Join(", ", shaders.Select(Path.GetFileNameWithoutExtension))}), 대상 {target}, {(DateTime.Now - t0).TotalSeconds:0.0}초");
        }

        static Result Finish(Result res, DateTime t0)
        {
            foreach (var n in res.Notes) Debug.Log("[FigureImport] " + n);
            foreach (var e in res.Errors) Debug.LogError("[FigureImport] " + e);
            string line = $"[FigureImport] 들임 {res.Imported}, 건너뜀 {res.Skipped}, 오류 {res.Errors.Count} ({(DateTime.Now - t0).TotalSeconds:0.0}초)";
            if (res.Ok) Debug.Log(line); else Debug.LogError(line);
            return res;
        }

        // ---------- 하나 ----------
        static JObject ImportOne(JObject row, string bundlePath, string sha, Result res)
        {
            int dex = (int)row["dex"], state = (int)row["state"];
            string key = Key(dex, state), dir = Dir(dex, state), name = dex + "_" + state;
            var bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null) { res.Errors.Add($"{key}: LoadFromFile 실패 {bundlePath}"); return null; }
            GameObject inst = null, copy = null, wrap = null;
            var temps = new List<Object>();
            try
            {
                var top = bundle.LoadAllAssets();
                var all = EditorUtility.CollectDependencies(top).Where(o => o != null).Distinct().ToArray();
                var prefab = all.OfType<GameObject>().Where(g => g.transform.parent == null)
                    .OrderByDescending(g => g.GetComponentsInChildren<Transform>(true).Length).FirstOrDefault();
                if (prefab == null) { res.Errors.Add(key + ": 루트 GameObject 없음"); return null; }
                inst = Object.Instantiate(prefab);
                inst.name = prefab.name;
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;   // 배율(3DS 출처 0.01)은 그대로 둔다
                float rootScale = inst.transform.localScale.x;

                // 효과표(대역 클래스) → 효과 이름 → (레거시 클립 이름, Mecanim 상태 이름)
                var fx = inst.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(m => m != null && m.GetType().Name == "TTSAssetBundleEffects");
                if (fx == null) { res.Errors.Add(key + ": TTSAssetBundleEffects 없음(대역 클래스가 안 붙음 — Assets/TTSCompat 확인)"); return null; }
                var effects = ReadEffects(fx);

                // 숨은 Cube: 렌더러 없이 MeshFilter만 있는 메시(TTS 받침 000Collider의 Cube, BoxCollider는 남긴다)와 꺼진 「Cube」 오브젝트
                int removedCube = 0;
                foreach (var t in inst.GetComponentsInChildren<Transform>(true).Where(t => t != inst.transform && t.name == "Cube").ToList())
                {
                    var mr = t.GetComponent<Renderer>();
                    if (mr == null || !mr.enabled || !t.gameObject.activeSelf) { Object.DestroyImmediate(t.gameObject); removedCube++; }
                }
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true).Where(m => m.GetComponent<Renderer>() == null).ToList()) { Object.DestroyImmediate(mf); removedCube++; }

                EnsureStableFolder(dir);
                var produced = new HashSet<string>();

                // 텍스처 → PNG(이름 순: 같은 이름이 있으면 _2를 붙이는 차례가 번들 읽기 순서에 묶이지 않게)
                var texMap = new Dictionary<Texture, Texture2D>();
                var texNames = new Names();
                foreach (var t in all.OfType<Texture2D>().OrderBy(t => t.name, StringComparer.Ordinal).ThenBy(t => t.width).ThenBy(t => t.height).ThenBy(t => (int)t.format))
                {
                    string p = dir + "/" + texNames.Next(San(t.name)) + ".png";
                    var tex = SavePng(ToPng(t), p);
                    if (tex == null) { res.Errors.Add($"{key}: 텍스처 들여오기 실패 {p}"); return null; }
                    texMap[t] = tex; produced.Add(p);
                }

                // 메시
                var meshMap = new Dictionary<Mesh, Mesh>();
                var meshNames = new Names();
                int tris = 0;
                foreach (var m in Meshes(inst))
                {
                    var c = Object.Instantiate(m); c.name = m.name; temps.Add(c);
                    string p = dir + "/" + meshNames.Next(San(m.name)) + ".asset";
                    meshMap[m] = SaveNative(c, p); produced.Add(p);
                    for (int s = 0; s < m.subMeshCount; s++) tris += (int)(m.GetIndexCount(s) / 3);
                }

                // 재질
                var matMap = new Dictionary<Material, Material>();
                var matNames = new Names();
                var shaderCount = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    foreach (var src in r.sharedMaterials)
                    {
                        if (src == null || matMap.ContainsKey(src)) continue;
                        string how;
                        var dst = ToUrp(src, out how); temps.Add(dst);
                        foreach (var pn in dst.GetTexturePropertyNames())
                        {
                            var tx = dst.GetTexture(pn);
                            if (tx == null) continue;
                            if (texMap.TryGetValue(tx, out var nt)) dst.SetTexture(pn, nt);
                            else { dst.SetTexture(pn, null); res.Notes.Add($"{key}: 재질 {src.name} {pn} 끊음({tx.name}, 번들 밖 텍스처)"); }
                        }
                        StripUnusedProperties(dst);
                        string sk = (src.shader ? src.shader.name : "null") + "→" + how;
                        shaderCount[sk] = shaderCount.TryGetValue(sk, out var nn) ? nn + 1 : 1;
                        if (how == "other") res.Notes.Add($"{key}: 모르는 셰이더 {src.shader?.name}(재질 {src.name}) → URP Lit으로 옮김");
                        string p = dir + "/" + matNames.Next(San(src.name)) + ".mat";
                        matMap[src] = SaveNative(dst, p); produced.Add(p);
                    }

                // 클립
                var anim = inst.GetComponentInChildren<Animation>(true);
                var animator = inst.GetComponentInChildren<Animator>(true);
                var pathRoot = anim ? anim.transform : animator ? animator.transform : inst.transform;
                var clipMap = new Dictionary<AnimationClip, AnimationClip>();
                var clipNames = new Names();
                var cst = new FigureClips.Stats();
                var datas = new List<FigureClips.ClipData>();
                // 이름 순: DropShared가 공통 채널을 구울 값을 첫 클립에서 가져오므로 번들 읽기 순서가 바뀌면 프리팹 자세가 허용 오차 안에서 달라졌다(491_0 위치 0.064, 480_0 쿼터니언 2.6e-3)
                foreach (var c in all.OfType<AnimationClip>().OrderBy(c => c.name, StringComparer.Ordinal).ThenBy(c => c.length))
                {
                    bool legacyCurves = c.legacy && AnimationUtility.GetCurveBindings(c).Length > 0;
                    datas.Add(legacyCurves ? FigureClips.ReadLegacy(c, pathRoot, cst) : FigureClips.ReadMecanim(c, animator ? animator.gameObject : inst, pathRoot, cst));
                }
                var baked = FigureClips.DropShared(datas, cst);
                foreach (var d in datas)
                {
                    var nc = FigureClips.Build(d, cst);
                    temps.Add(nc);
                    string p = dir + "/" + clipNames.Next(San(d.Src.name)) + ".anim";
                    clipMap[d.Src] = SaveNative(nc, p); produced.Add(p);
                }
                foreach (var n in cst.Notes) res.Notes.Add(key + ": " + n);

                // 효과 → 클립
                var clipsJson = new JObject();
                foreach (var e in Effects)
                {
                    if (!effects.TryGetValue(e, out var want)) { res.Errors.Add($"{key}: 효과 {e} 없음"); return null; }
                    var src = all.OfType<AnimationClip>().FirstOrDefault(c => c.name == want);
                    if (src == null || !clipMap.TryGetValue(src, out var dstClip)) { res.Errors.Add($"{key}: 효과 {e}의 클립 {want} 없음"); return null; }
                    clipsJson[EffectKey(e)] = new JObject { ["clip"] = dstClip.name, ["length"] = R4(dstClip.length) };
                }

                // 크기(배율 1 새 루트 기준 = 번들 인스턴스 월드): Idle 첫 자세
                var idle = all.OfType<AnimationClip>().First(c => c.name == effects["Idle"]);
                Sample(idle, anim, animator, inst, 0f);
                var b = WorldBounds(inst);

                // 프리팹: 사본의 참조를 바꿔 끼우고, 효과표·Animator·숨은 Cube를 떼고, 배율 1 새 루트 밑에 둔다
                copy = Object.Instantiate(inst); copy.name = inst.name;
                string pathRel = AnimationUtility.CalculateTransformPath(pathRoot, inst.transform);
                FigureClips.ApplyBaked(baked, pathRel.Length == 0 ? copy.transform : copy.transform.Find(pathRel));
                foreach (var s in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (s.sharedMesh && meshMap.TryGetValue(s.sharedMesh, out var nm)) s.sharedMesh = nm;
                    s.updateWhenOffscreen = false;
                }
                foreach (var f in copy.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh && meshMap.TryGetValue(f.sharedMesh, out var nm)) f.sharedMesh = nm;
                foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++) if (mats[k] && matMap.TryGetValue(mats[k], out var nm)) mats[k] = nm;
                    r.sharedMaterials = mats;
                }
                foreach (var m in copy.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null && m.GetType().Name == "TTSAssetBundleEffects").ToList()) Object.DestroyImmediate(m);
                var animOnCopy = copy.GetComponentInChildren<Animation>(true);
                if (animOnCopy == null)
                {
                    // Mecanim뿐인 번들: 경로 기준(Animator가 있던 곳)에 레거시 Animation을 붙인다
                    var host = pathRel.Length == 0 ? copy.transform : copy.transform.Find(pathRel);
                    animOnCopy = host.gameObject.AddComponent<Animation>();
                }
                var newClips = clipMap.Values.OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
                AnimationUtility.SetAnimationClips(animOnCopy, newClips);
                var idleNew = clipMap[idle];
                animOnCopy.clip = idleNew;
                animOnCopy.playAutomatically = true;
                foreach (var a in copy.GetComponentsInChildren<Animator>(true).ToList()) Object.DestroyImmediate(a);
                foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb == null) { res.Errors.Add(key + ": 스크립트가 없는 컴포넌트가 남음"); return null; }

                wrap = new GameObject(name);
                copy.transform.SetParent(wrap.transform, false);
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                ZeroNavMeshLayer(wrap);
                string prefabPath = PrefabPath(dex, state);
                PrepareStable(prefabPath, PrefabMeta);   // SaveAsPrefabAsset은 미리 쓴 .meta의 GUID를 쓴다(C2 확인)
                PrefabUtility.SaveAsPrefabAsset(wrap, prefabPath, out bool ok);
                if (!ok) { res.Errors.Add(key + ": 프리팹 저장 실패 " + prefabPath); return null; }
                string prefabFull = Path.Combine(ProjectRoot, prefabPath), saved = File.ReadAllText(prefabFull, Utf8), canon = CanonPrefab(saved, prefabPath);
                if (canon != saved) { File.WriteAllText(prefabFull, canon, Utf8); AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate); }
                produced.Add(prefabPath);
                AssetDatabase.SaveAssets();
                foreach (var p in produced.Append(dir))
                    if (AssetDatabase.AssetPathToGUID(p) != StableGuid(p)) { res.Errors.Add($"{key}: GUID가 고정 값이 아님 {p}"); return null; }

                // 이번에 만들지 않은 파일은 지운다
                foreach (var g in AssetDatabase.FindAssets("", new[] { dir }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    if (!AssetDatabase.IsValidFolder(p) && !produced.Contains(p) && Path.GetDirectoryName(p).Replace('\\', '/') == dir) AssetDatabase.DeleteAsset(p);
                }

                long bytes = Directory.GetFiles(Path.Combine(ProjectRoot, dir)).Where(f => !f.EndsWith(".meta")).Sum(f => new FileInfo(f).Length);
                var outRow = new JObject
                {
                    ["key"] = key,
                    ["name"] = row["name"],
                    ["group"] = row["group"],
                    ["required"] = row["required"],
                    ["prefab"] = prefabPath.Substring(OutRoot.Length + 1),
                    ["ttsScale"] = row["ttsScale"],
                    ["fitScale"] = 0,
                    ["size"] = new JArray(R4(b.size.x), R4(b.size.y), R4(b.size.z)),
                    ["bottom"] = R4(b.min.y),
                    ["rootScale"] = R4(rootScale),
                    ["tris"] = tris,
                    ["meshes"] = meshMap.Count,
                    ["textures"] = texMap.Count,
                    ["materials"] = matMap.Count,
                    ["shaders"] = new JArray(shaderCount.Select(kv => kv.Value > 1 ? kv.Key + "×" + kv.Value : kv.Key)),
                    ["keys"] = new JArray(cst.KeysIn, cst.KeysOut),
                    ["baked"] = cst.Baked,
                    ["err"] = new JArray(R4(cst.MaxRotErr), R4(cst.MaxPosErr)),
                    ["cube"] = removedCube,
                    ["bytes"] = bytes,
                    ["bundle"] = -1,
                    ["src"] = sha,
                    ["clips"] = clipsJson,
                };
                return outRow;
            }
            finally
            {
                if (wrap) Object.DestroyImmediate(wrap);
                else if (copy) Object.DestroyImmediate(copy);
                if (inst) Object.DestroyImmediate(inst);
                foreach (var t in temps) if (t != null && !EditorUtility.IsPersistent(t)) Object.DestroyImmediate(t);
                bundle.Unload(true);
            }
        }

        static Dictionary<string, string> ReadEffects(MonoBehaviour fx)
        {
            var d = new Dictionary<string, string>();
            var j = Parse(JsonUtility.ToJson(fx));
            foreach (var list in new[] { "LoopingEffects", "TriggerEffects" })
                foreach (var e in (j[list] as JArray) ?? new JArray())
                {
                    string n = (string)e["Name"];
                    string a = (string)e["Animation"]?["AnimationName"], s = (string)e["Animator"]?["StateName"];
                    if (!string.IsNullOrEmpty(n)) d[n] = !string.IsNullOrEmpty(a) ? a : s;
                }
            return d;
        }

        static IEnumerable<Mesh> Meshes(GameObject go) =>
            go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.sharedMesh)
              .Concat(go.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh))
              .Where(m => m != null).Distinct();

        /// <summary>읽기 불가 텍스처도 RenderTexture로 복사해 PNG로(d3-1 §5-3 ⑥). 원본에 알파가 없으면(DXT1·RGB) RGB PNG.</summary>
        static byte[] ToPng(Texture2D t)
        {
            bool alpha = !(t.format == TextureFormat.DXT1 || t.format == TextureFormat.DXT1Crunched || t.format == TextureFormat.RGB24 || t.format == TextureFormat.RGB565 || t.format == TextureFormat.BC4 || t.format == TextureFormat.BC5);
            var rw = t.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, rw);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                var c = new Texture2D(t.width, t.height, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false, rw == RenderTextureReadWrite.Linear);
                c.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
                c.Apply();
                var png = c.EncodeToPNG();
                Object.DestroyImmediate(c);
                return png;
            }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
        }

        /// <summary>번들 재질 → URP 재질(새 객체). how = standard | additive | particle | other.</summary>
        static Material ToUrp(Material src, out string how)
        {
            string sn = src.shader ? src.shader.name : "";
            if (sn == "Standard" || sn == "Standard (Specular setup)")
            {
                var m = new Material(src) { name = src.name };
                new StandardUpgrader(sn).Upgrade(m, MaterialUpgrader.UpgradeFlags.None);
                how = "standard";
                return m;
            }
            var main = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : SavedTexture(src, "_MainTex");
            if (sn.IndexOf("Particles/Additive", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // 내장 Legacy Particles/Additive: 2 × _TintColor × 텍스처, 더하기 섞기 → URP Particles/Unlit 더하기(d3-1 §5-3 ③)
                var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = src.name };
                if (main)
                {
                    m.SetTexture("_BaseMap", main);
                    m.SetTextureScale("_BaseMap", src.GetTextureScale("_MainTex"));
                    m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_MainTex"));
                }
                var tint = src.HasProperty("_TintColor") ? src.GetColor("_TintColor") : new Color(0.5f, 0.5f, 0.5f, 0.5f);
                m.SetColor("_BaseColor", new Color(Mathf.Min(1f, tint.r * 2f), Mathf.Min(1f, tint.g * 2f), Mathf.Min(1f, tint.b * 2f), Mathf.Min(1f, tint.a * 2f)));
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
                BaseShaderGUI.SetupMaterialBlendMode(m);
                how = "additive";
                return m;
            }
            if (sn.StartsWith("Particles/Standard", StringComparison.Ordinal))
            {
                // 번들 안 Particles/Standard Surface(2019 컴파일본, Unity 6에서 못 씀) → URP 공식 ParticleUpgrader(Surface → Particles/Lit, Unlit → Particles/Unlit) [가정 M4-1a]
                var m = new Material(src) { name = src.name };
                new ParticleUpgrader(sn).Upgrade(m, MaterialUpgrader.UpgradeFlags.None);
                if (main && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") == null) m.SetTexture("_BaseMap", main);
                how = "particle";
                return m;
            }
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = src.name };
            if (main) lit.SetTexture("_BaseMap", main);
            lit.SetColor("_BaseColor", src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white);
            how = "other";
            return lit;
        }

        /// <summary>셰이더가 속성을 모를 때(번들 안 셰이더) 재질에 저장된 텍스처를 직접 읽는다.</summary>
        static Texture SavedTexture(Material m, string prop)
        {
            var so = new SerializedObject(m);
            var envs = so.FindProperty("m_SavedProperties.m_TexEnvs");
            if (envs == null) return null;
            for (int i = 0; i < envs.arraySize; i++)
            {
                var e = envs.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first")?.stringValue == prop) return e.FindPropertyRelative("second.m_Texture")?.objectReferenceValue as Texture;
            }
            return null;
        }

        static void Sample(AnimationClip clip, Animation anim, Animator animator, GameObject inst, float t)
        {
            if (clip.legacy && anim)
            {
                var st = anim[clip.name];
                if (st == null) { anim.AddClip(clip, clip.name); st = anim[clip.name]; }
                anim.Stop(); st.enabled = true; st.weight = 1f; st.time = t; anim.Sample(); st.enabled = false;
                return;
            }
            clip.SampleAnimation(animator ? animator.gameObject : inst, t);
        }

        /// <summary>스킨 메시를 지금 자세로 구워 잰 월드 경계(d3-1 FigureSpike.WorldBounds).</summary>
        public static Bounds WorldBounds(GameObject go)
        {
            bool any = false; var bb = new Bounds();
            void Add(Vector3 w) { if (!any) { bb = new Bounds(w, Vector3.zero); any = true; } else bb.Encapsulate(w); }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.sharedMesh || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;
                var baked = new Mesh();
                try
                {
                    smr.BakeMesh(baked, true);
                    foreach (var v in baked.vertices) Add(smr.transform.TransformPoint(v));
                }
                finally { Object.DestroyImmediate(baked); }
            }
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (!mf.sharedMesh || !mr || !mr.enabled || !mf.gameObject.activeInHierarchy) continue;
                foreach (var v in mf.sharedMesh.vertices) Add(mf.transform.TransformPoint(v));
            }
            return any ? bb : new Bounds(go.transform.position, Vector3.zero);
        }

        // ---------- figures.json ----------
        static void WriteJson(List<JObject> rows, Result res)
        {
            // fitScale: TTS 배율 크기 d = max(폭, 높이) × ttsScale을 로그 눈금으로 FitMin~FitMax에 옮기고 배율 1 크기로 나눈다
            double D(JObject r) => Math.Max((double)r["size"][0], (double)r["size"][1]);
            double Tts(JObject r) => (double)r["ttsScale"];
            double lo = rows.Min(r => Math.Log(D(r) * Tts(r))), hi = rows.Max(r => Math.Log(D(r) * Tts(r)));
            foreach (var r in rows)
            {
                double d0 = D(r), ln = Math.Log(d0 * Tts(r));
                double target = hi - lo < 1e-9 ? (FitMin + FitMax) / 2 : FitMin + (FitMax - FitMin) * (ln - lo) / (hi - lo);
                r["fitScale"] = Math.Round(target / Math.Max(d0, 1e-6), 4);
            }
            var sb = new StringBuilder();
            sb.Append("{\"schema\":1,\"importer\":").Append(ImporterVersion).Append(",\"fit\":[").Append(Num(FitMin)).Append(',').Append(Num(FitMax)).Append("],\"rows\":[\n");
            for (int i = 0; i < rows.Count; i++)
            {
                sb.Append(rows[i].ToString(Formatting.None));
                sb.Append(i < rows.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("]}\n");
            string text = sb.ToString();
            string full = Path.Combine(ProjectRoot, JsonPath);
            PrepareStable(JsonPath, TextMeta);
            if (File.Exists(full) && File.ReadAllText(full, Utf8) == text) return;
            File.WriteAllText(full, text, Utf8);
            AssetDatabase.ImportAsset(JsonPath, ImportAssetOptions.ForceSynchronousImport);
            res.Notes.Add($"figures.json 씀({rows.Count}행)");
        }

        // ---------- 바이트 결정성(03 §7) ----------
        // Unity가 이 PC에서 새로 쓰는 .meta와 같은 꼴(CRLF). 미리 쓴 .meta는 들일 때 GUID가 그대로 쓰인다(프리팹·폴더·json에 쓴다, C2 확인)
        const string FolderMeta = "folderAsset: yes\r\nDefaultImporter:\r\n  externalObjects: {}\r\n  userData: \r\n  assetBundleName: \r\n  assetBundleVariant: \r\n";
        const string PrefabMeta = "PrefabImporter:\r\n  externalObjects: {}\r\n  userData: \r\n  assetBundleName: \r\n  assetBundleVariant: \r\n";
        const string TextMeta = "TextScriptImporter:\r\n  externalObjects: {}\r\n  userData: \r\n  assetBundleName: \r\n  assetBundleVariant: \r\n";
        static readonly Regex GuidLine = new Regex(@"^guid: ([0-9a-f]{32})(?=\r?\n)", RegexOptions.Multiline);

        /// <summary>피규어 출력의 GUID = 프로젝트 상대 경로의 MD5. 새 작업 트리에서 들여도 같은 GUID라 .meta와 재질·프리팹 안 GUID 참조가 바이트까지 같다.</summary>
        public static string StableGuid(string assetPath)
        {
            using (var md5 = MD5.Create()) return Hex(md5.ComputeHash(Utf8.GetBytes("sinnoh-figure:" + assetPath)));
        }

        static long Hash64(string s)
        {
            using (var md5 = MD5.Create())
            {
                long v = (long)(BitConverter.ToUInt64(md5.ComputeHash(Utf8.GetBytes(s)), 0) & 0x7FFFFFFFFFFFFFFFUL);
                return v < 100000000000L ? v + 100000000000L : v;   // 작은 값(classID×100000 꼴 주 객체 fileID)과 겹치지 않게
            }
        }

        static string Full(string assetPath) => Path.Combine(ProjectRoot, assetPath);
        static string MetaGuid(string metaFull) => File.Exists(metaFull) ? GuidLine.Match(File.ReadAllText(metaFull, Utf8)).Groups[1].Value : "";
        static bool SameBytes(byte[] a, byte[] b) => ((ReadOnlySpan<byte>)a).SequenceEqual(b);

        /// <summary>path를 고정 GUID로 만들 준비: 다른 GUID로 있으면 지우고, .meta가 없거나 GUID가 다르면 metaBody로 미리 쓴다.</summary>
        static void PrepareStable(string path, string metaBody)
        {
            string want = StableGuid(path), meta = Full(path) + ".meta";
            string have = AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets);
            if (have.Length > 0 && have != want) AssetDatabase.DeleteAsset(path);
            if (MetaGuid(meta) != want) File.WriteAllText(meta, "fileFormatVersion: 2\r\nguid: " + want + "\r\n" + metaBody, Utf8);
        }

        /// <summary>고정 GUID 폴더. 다른 GUID로 있으면(들여오기 판 2까지 = 무작위) 안의 것과 함께 지우고 다시 만든다.</summary>
        static void EnsureStableFolder(string path)
        {
            string want = StableGuid(path);
            if (AssetDatabase.IsValidFolder(path))
            {
                if (AssetDatabase.AssetPathToGUID(path) == want) return;
                AssetDatabase.DeleteAsset(path);
            }
            Directory.CreateDirectory(Full(path));
            File.WriteAllText(Full(path) + ".meta", "fileFormatVersion: 2\r\nguid: " + want + "\r\n" + FolderMeta, Utf8);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (!AssetDatabase.IsValidFolder(path)) throw new InvalidOperationException("폴더 들이기 실패 " + path);
        }

        /// <summary>
        /// 메시·재질·클립을 고정 GUID로 저장한다. CreateAsset은 미리 쓴 .meta를 무시하고 새 GUID를 주므로(C2 확인) 임시 폴더에 만들어 Unity가 쓴 텍스트를 읽고 지운 뒤,
        /// 부속 fileID를 고정해 최종 경로에 .meta(GUID만 바꿈)와 함께 쓰고 들인다. 재질은 늘 「새로 만든 재질」 길(URP MaterialPostprocessor의 InitializeLatest + AssetVersion)로 간다
        /// — 전에는 덮어쓰기(CopySerialized)와 새로 만들기가 _MainTex·_Color 같은 옛 칸이 달랐다. 에셋 이름 = 파일 이름(클립은 이 이름으로 재생한다).
        /// </summary>
        static T SaveNative<T>(T obj, string path) where T : Object
        {
            obj.name = Path.GetFileNameWithoutExtension(path);
            string tmp = TmpPath(path);
            AssetDatabase.CreateAsset(obj, tmp);
            if (obj is Material) { EditorUtility.SetDirty(obj); AssetDatabase.SaveAssetIfDirty(obj); }   // URP가 들일 때 붙인 AssetVersion 부속까지 파일에
            string text = File.ReadAllText(Full(tmp), Utf8), meta = File.ReadAllText(Full(tmp) + ".meta", Utf8);
            AssetDatabase.DeleteAsset(tmp);
            if (obj is Material && !text.Contains("UnityEditor.Rendering.Universal.AssetVersion")) throw new InvalidOperationException(path + ": URP AssetVersion 부속이 저장되지 않음");
            WriteStable(path, Utf8.GetBytes(CanonSubAssets(text, path)), meta);
            var saved = AssetDatabase.LoadAssetAtPath<T>(path);
            if (saved == null) throw new InvalidOperationException(path + ": 저장한 에셋을 읽지 못함");
            return saved;
        }

        /// <summary>
        /// PNG를 고정 GUID로 들인다. GUID만 적은 .meta를 미리 쓰면 TextureImporter가 새 파일 기본값이 아니라 클래스 기본값으로 시작해 applyGammaDecoding·cookieLightType이 달랐다(C2 확인)
        /// → 임시 폴더에서 .meta 없이 들여 Unity가 쓴 .meta(들여오기 규칙 GenImport 적용)를 가져와 GUID만 바꾼다(메타 없이 들이던 전과 같은 설정).
        /// </summary>
        static Texture2D SavePng(byte[] png, string path)
        {
            string tmp = TmpPath(path);
            File.WriteAllBytes(Full(tmp), png);
            AssetDatabase.ImportAsset(tmp, ImportAssetOptions.ForceSynchronousImport);
            string meta = File.ReadAllText(Full(tmp) + ".meta", Utf8);
            AssetDatabase.DeleteAsset(tmp);
            WriteStable(path, png, meta);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static string TmpPath(string path)
        {
            if (!AssetDatabase.IsValidFolder(TmpDir)) AssetDatabase.CreateFolder(OutRoot, Path.GetFileName(TmpDir));
            string tmp = TmpDir + "/" + Path.GetFileName(path);
            if (AssetDatabase.LoadMainAssetAtPath(tmp) != null) AssetDatabase.DeleteAsset(tmp);
            return tmp;
        }

        /// <summary>최종 경로에 본체와 .meta(임시 경로에서 Unity가 쓴 것, GUID만 경로 해시로)를 쓰고 들인다. 다른 GUID로 있으면 먼저 지운다. 바이트가 같으면 다시 쓰지 않는다.</summary>
        static void WriteStable(string path, byte[] bytes, string tmpMeta)
        {
            if (!GuidLine.IsMatch(tmpMeta)) throw new InvalidOperationException(path + ": 임시 .meta에 guid 줄 없음");
            string full = Full(path), want = StableGuid(path), meta = GuidLine.Replace(tmpMeta, "guid: " + want, 1);
            string have = AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets);
            if (have.Length > 0 && have != want) AssetDatabase.DeleteAsset(path);
            if (!File.Exists(full) || !SameBytes(File.ReadAllBytes(full), bytes)) File.WriteAllBytes(full, bytes);
            if (!File.Exists(full + ".meta") || File.ReadAllText(full + ".meta", Utf8) != meta) File.WriteAllText(full + ".meta", meta, Utf8);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        sealed class Doc { public int Cls; public long Id; public string Tail, Body; }
        static readonly Regex DocHead = new Regex(@"^--- !u!(\d+) &(-?\d+)([^\n]*)\n", RegexOptions.Multiline);
        static readonly Regex LocalRef = new Regex(@"\{fileID: (-?\d+)\}");
        static readonly Regex NameRe = new Regex(@"^  m_Name: ?(.*)$", RegexOptions.Multiline);
        static readonly Regex CompRe = new Regex(@"^  - component: \{fileID: (-?\d+)\}$", RegexOptions.Multiline);
        static readonly Regex GoRe = new Regex(@"^  m_GameObject: \{fileID: (-?\d+)\}$", RegexOptions.Multiline);
        static readonly Regex FatherRe = new Regex(@"^  m_Father: \{fileID: (-?\d+)\}$", RegexOptions.Multiline);
        static readonly Regex ChildrenRe = new Regex(@"^  m_Children:\n((?:  - \{fileID: -?\d+\}\n)*)", RegexOptions.Multiline);
        static readonly Regex NavRe = new Regex(@"^  m_NavMeshLayer: ([0-9]+)$", RegexOptions.Multiline);

        static long L(string s) => long.Parse(s, CultureInfo.InvariantCulture);

        /// <summary>Unity 텍스트 직렬화를 문서로 나눈다(줄 끝은 LF로 바꿔 다루고 <see cref="JoinDocs"/>가 되돌린다).</summary>
        static (string head, List<Doc> docs) SplitDocs(string text)
        {
            var ms = DocHead.Matches(text);
            if (ms.Count == 0) throw new InvalidOperationException("YAML 문서 없음");
            var docs = new List<Doc>();
            for (int i = 0; i < ms.Count; i++)
            {
                int s = ms[i].Index + ms[i].Length, e = i + 1 < ms.Count ? ms[i + 1].Index : text.Length;
                docs.Add(new Doc { Cls = int.Parse(ms[i].Groups[1].Value, CultureInfo.InvariantCulture), Id = L(ms[i].Groups[2].Value), Tail = ms[i].Groups[3].Value, Body = text.Substring(s, e - s) });
            }
            return (text.Substring(0, ms[0].Index), docs);
        }

        static string JoinDocs(string head, IEnumerable<Doc> docs, Dictionary<long, long> map, string path, bool crlf)
        {
            var sb = new StringBuilder(head);
            foreach (var d in docs)
            {
                sb.Append("--- !u!").Append(d.Cls).Append(" &").Append(map[d.Id]).Append(d.Tail).Append('\n');
                sb.Append(LocalRef.Replace(d.Body, m =>
                {
                    long id = L(m.Groups[1].Value);
                    if (id == 0) return m.Value;
                    if (map.TryGetValue(id, out var n)) return "{fileID: " + n + "}";
                    throw new InvalidOperationException($"{path}: 파일 안에 없는 로컬 참조 {id}");
                }));
            }
            string s = sb.ToString();
            return crlf ? s.Replace("\n", "\r\n") : s;
        }

        /// <summary>메시·재질·클립: 주 객체(fileID = classID×100000) 밖 부속(재질의 URP AssetVersion)의 무작위 fileID를 경로 해시로 바꾸고 fileID 순(Unity 순서)으로 둔다.</summary>
        static string CanonSubAssets(string text, string path)
        {
            if (text.IndexOf("\n--- !u!", StringComparison.Ordinal) == text.LastIndexOf("\n--- !u!", StringComparison.Ordinal)) return text;   // 문서 하나(메시·클립)
            bool crlf = text.Contains("\r\n");
            var (head, docs) = SplitDocs(crlf ? text.Replace("\r\n", "\n") : text);
            var map = new Dictionary<long, long>();
            foreach (var d in docs)
            {
                if (d.Id == d.Cls * 100000L) { map.Add(d.Id, d.Id); continue; }
                if (docs.Count(x => x.Cls == d.Cls && x.Id != x.Cls * 100000L) > 1) throw new InvalidOperationException($"{path}: 같은 종류 부속이 둘 이상(classID {d.Cls})");
                map.Add(d.Id, Hash64(path + "|sub|" + d.Cls));
            }
            return JoinDocs(head, docs.OrderBy(d => map[d.Id]), map, path, crlf);
        }

        /// <summary>
        /// 프리팹 로컬 fileID를 계층 경로(형제 순번 + 이름)·컴포넌트 순번의 해시로 바꾸고, 문서를 Unity 순서(GameObject를 fileID 오름차순, 각 뒤에 컴포넌트를 m_Component 순)로 둔다.
        /// SaveAsPrefabAsset은 새로 만들 때마다 무작위 fileID를 준다. 초기화 안 된 m_NavMeshLayer가 남아 있으면 오류(<see cref="ZeroNavMeshLayer"/>가 0으로 둔다).
        /// </summary>
        public static string CanonPrefab(string text, string path)
        {
            bool crlf = text.Contains("\r\n");
            var (head, docs) = SplitDocs(crlf ? text.Replace("\r\n", "\n") : text);
            var byId = new Dictionary<long, Doc>();
            foreach (var d in docs) byId.Add(d.Id, d);
            var names = new Dictionary<long, string>();
            var comps = new Dictionary<long, List<long>>();
            var goOf = new Dictionary<long, long>();
            var children = new Dictionary<long, List<long>>();
            var roots = new List<long>();
            foreach (var d in docs)
            {
                if (d.Cls == 1)
                {
                    names[d.Id] = NameRe.Match(d.Body).Groups[1].Value;
                    comps[d.Id] = CompRe.Matches(d.Body).Cast<Match>().Select(m => L(m.Groups[1].Value)).ToList();
                    var nav = NavRe.Match(d.Body);
                    if (nav.Success && nav.Groups[1].Value != "0") throw new InvalidOperationException($"{path}: m_NavMeshLayer {nav.Groups[1].Value}({names[d.Id]})");
                }
                else if (d.Cls == 4 || d.Cls == 224)
                {
                    goOf[d.Id] = L(GoRe.Match(d.Body).Groups[1].Value);
                    var cm = ChildrenRe.Match(d.Body);
                    children[d.Id] = cm.Success ? LocalRef.Matches(cm.Groups[1].Value).Cast<Match>().Select(m => L(m.Groups[1].Value)).ToList() : new List<long>();
                    if (L(FatherRe.Match(d.Body).Groups[1].Value) == 0) roots.Add(d.Id);
                }
            }
            if (roots.Count != 1) throw new InvalidOperationException($"{path}: 뿌리 Transform {roots.Count}개");
            var map = new Dictionary<long, long>();
            var order = new List<long>();
            void Visit(long tr, string p)
            {
                long go = goOf[tr];
                map.Add(go, Hash64(path + "|go|" + p));
                var cl = comps[go];
                for (int k = 0; k < cl.Count; k++) map.Add(cl[k], Hash64(path + "|c|" + p + "|" + k + "|" + byId[cl[k]].Cls));
                order.Add(go);
                var ch = children[tr];
                for (int c = 0; c < ch.Count; c++) Visit(ch[c], p + "/" + c + ":" + names[goOf[ch[c]]]);
            }
            Visit(roots[0], "0:" + names[goOf[roots[0]]]);
            if (map.Count != docs.Count) throw new InvalidOperationException($"{path}: 계층 밖 문서 {docs.Count - map.Count}개");
            if (map.Values.Distinct().Count() != map.Count) throw new InvalidOperationException($"{path}: fileID 해시 겹침");
            var sorted = order.OrderBy(g => map[g]).SelectMany(g => new[] { byId[g] }.Concat(comps[g].Select(c => byId[c])));
            return JoinDocs(head, sorted, map, path, crlf);
        }

        /// <summary>GameObject의 옛 칸 m_NavMeshLayer: 번들(2019) 객체에서 초기화되지 않은 값(4294967295·1701602643 …)이 따라와 실행마다 달랐다 → 0(새 GameObject 기본값).</summary>
        static void ZeroNavMeshLayer(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var so = new SerializedObject(t.gameObject);
                var p = so.FindProperty("m_NavMeshLayer");
                if (p != null && p.longValue != 0) { p.longValue = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
        }

        /// <summary>대상 셰이더가 선언하지 않은 저장 속성(Standard·번들 PSS에서 따라온 _Mode·_UVSec·unity_Lightmaps 등)을 지운다. 셰이더가 읽지 않으니 렌더는 같다.</summary>
        static void StripUnusedProperties(Material m)
        {
            var so = new SerializedObject(m);
            bool changed = false;
            foreach (var list in new[] { "m_SavedProperties.m_TexEnvs", "m_SavedProperties.m_Ints", "m_SavedProperties.m_Floats", "m_SavedProperties.m_Colors" })
            {
                var arr = so.FindProperty(list);
                if (arr == null || !arr.isArray) continue;
                for (int i = arr.arraySize - 1; i >= 0; i--)
                {
                    var first = arr.GetArrayElementAtIndex(i).FindPropertyRelative("first");
                    if (first != null && m.shader.FindPropertyIndex(first.stringValue) < 0) { arr.DeleteArrayElementAtIndex(i); changed = true; }
                }
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- 도움 ----------

        sealed class Names
        {
            readonly Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public string Next(string n)
            {
                if (string.IsNullOrEmpty(n)) n = "_";
                if (!seen.TryGetValue(n, out int k)) { seen[n] = 1; return n; }
                seen[n] = k + 1;
                return n + "_" + (k + 1);
            }
        }

        static JObject Parse(string json) => JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double });
        static double R4(float x) => Math.Round(x, 4);
        static string Num(float x) => x.ToString("0.####", CultureInfo.InvariantCulture);
        static string San(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace(' ', '_'); }
        static string Hex(byte[] b) { var sb = new StringBuilder(b.Length * 2); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
        static string FirstLine(string s) => string.IsNullOrEmpty(s) ? "" : s.Split('\n')[0].Trim();
    }
}
