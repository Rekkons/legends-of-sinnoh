using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Sinnoh.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.EditorAssets
{
    /// <summary>
    /// plan M4-1 완료 조건 ②(피규어)·03 §7 「피규어」 행: FigureImport가 만든 Gen/Figures를 엔진으로 본다(엔진 시험이라 Tests/EditMode가 아니라 이 asmdef).
    /// Gen/Figures가 없으면(rebuild-all 5단계 전) 건너뛴다. 렌더는 그래픽이 있는 배치(-nographics 없이)나 에디터에서 돈다.
    /// </summary>
    public class FigureAssetTests
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static JObject Json(string full) => JsonConvert.DeserializeObject<JObject>(File.ReadAllText(full, Utf8), new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });

        static List<JObject> ListRows() => ((JArray)Json(Path.Combine(Root, FigureImport.ListPath))["rows"]).Cast<JObject>().ToList();

        static Dictionary<string, JObject> FigureRows()
        {
            string p = Path.Combine(Root, FigureImport.JsonPath);
            if (!File.Exists(p)) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저 (" + FigureImport.JsonPath + ")");
            return ((JArray)Json(p)["rows"]).Cast<JObject>().ToDictionary(r => (string)r["key"]);
        }

        [Test]
        public void M4_피규어목록마다프리팹클립8과표()
        {
            var list = ListRows();
            var rows = FigureRows();
            Assert.AreEqual(37, list.Count, "figure_list = 33종 + 폼 4");
            Assert.AreEqual(21, list.Count(r => (bool)r["required"]), "필수 21");
            Assert.AreEqual(list.Count, rows.Count, "figures.json 행 수");
            foreach (var r in list)
            {
                int dex = (int)r["dex"], state = (int)r["state"];
                string key = FigureImport.Key(dex, state);
                Assert.IsTrue(rows.TryGetValue(key, out var f), key + ": figures.json에 없음");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FigureImport.PrefabPath(dex, state));
                Assert.IsNotNull(prefab, key + ": 프리팹 없음");
                Assert.AreEqual(Vector3.one, prefab.transform.localScale, key + ": 새 루트 배율 1(3DS 루트 0.01은 자식에)");
                Assert.AreEqual(1, prefab.transform.childCount, key + ": 새 루트 밑에 번들 루트 하나");
                Assert.AreEqual(0.01f, prefab.transform.GetChild(0).localScale.x, 1e-6f, key + ": 번들 루트 배율 0.01(3DS 출처)");
                var anim = prefab.GetComponentInChildren<Animation>(true);
                Assert.IsNotNull(anim, key + ": 레거시 Animation 없음");
                Assert.IsNull(prefab.GetComponentInChildren<Animator>(true), key + ": Animator가 남음");
                Assert.IsFalse(prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(m => m == null || m.GetType().Name == "TTSAssetBundleEffects"), key + ": 효과표·없는 스크립트가 남음");
                var clips = AnimationUtility.GetAnimationClips(anim.gameObject);
                Assert.AreEqual(8, clips.Length, key + ": 클립 8");
                var clipsJson = (JObject)f["clips"];
                Assert.AreEqual(8, clipsJson.Count, key + ": 효과 8");
                foreach (var e in FigureImport.Effects)
                {
                    var c = (JObject)clipsJson[FigureImport.EffectKey(e)];
                    Assert.IsNotNull(c, key + ": 효과 " + e);
                    var clip = clips.FirstOrDefault(x => x.name == (string)c["clip"]);
                    Assert.IsNotNull(clip, key + ": " + e + " 클립 " + c["clip"]);
                    Assert.IsTrue(clip.legacy, key + ": 레거시 클립");
                    Assert.AreEqual((double)c["length"], clip.length, 0.001, key + ": " + e + " 길이");
                    Assert.Greater(clip.length, 0.1f, key + ": " + e + " 길이");
                }
                Assert.Greater((double)f["fitScale"], 0, key + ": fitScale");
                double d = Math.Max((double)f["size"][0], (double)f["size"][1]) * (double)f["fitScale"];
                Assert.That(d, Is.InRange(FigureImport.FitMin - 0.01, FigureImport.FitMax + 0.01), key + ": 맞춘 크기(최소 0.75~1.8)");
                Assert.Greater((long)f["bytes"], 0, key + ": Gen 크기 기록");
                Assert.That((long)f["bundle"], Is.InRange(1L, FigureImport.TargetBytes), key + ": 번들 크기 기록(바이트), 목표 3 MB = 3,000,000바이트 이하");
            }
        }

        [Test]
        public void M4_피규어재질은URP셰이더만()
        {
            FigureRows();
            var mats = AssetDatabase.FindAssets("t:Material", new[] { FigureImport.OutRoot }).Select(g => AssetDatabase.GUIDToAssetPath(g)).ToList();
            Assert.Greater(mats.Count, 100, "재질 수(03 §2-5: 번들 131)");
            var bad = new List<string>();
            foreach (var p in mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null || m.shader == null || !m.shader.name.StartsWith("Universal Render Pipeline/") || !m.shader.isSupported) bad.Add(p + " " + (m && m.shader ? m.shader.name : "null"));
            }
            Assert.IsEmpty(bad, "URP가 아닌 셰이더: " + string.Join(", ", bad.Take(10)));
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { FigureImport.OutRoot }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        Assert.IsTrue(m != null && AssetDatabase.GetAssetPath(m).StartsWith(FigureImport.OutRoot), go.name + "/" + r.name + ": 재질이 Gen/Figures 밖이거나 없음");
                foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    Assert.IsTrue(s.sharedMesh != null && AssetDatabase.GetAssetPath(s.sharedMesh).StartsWith(FigureImport.OutRoot), go.name + "/" + s.name + ": 메시가 Gen/Figures 밖이거나 없음");
            }
        }

        /// <summary>
        /// 03 §7 바이트 결정성(들여오기 판 3): Gen/Figures의 모든 .meta GUID = 경로 해시(새 작업 트리에서도 같다), 프리팹은 이미 정규형(fileID = 계층 해시·Unity 순서, m_NavMeshLayer 0),
        /// 재질은 대상 셰이더가 선언한 저장 속성만. 같은 입력이면 실행마다 바이트가 같은지는 빈 사본 다시 들이기로 본다(plan §4).
        /// </summary>
        [Test]
        public void M4_피규어GUID와fileID는고정()
        {
            FigureRows();
            string outFull = Path.Combine(Root, FigureImport.OutRoot);
            var metas = Directory.GetFiles(outFull, "*.meta", SearchOption.AllDirectories).Append(outFull + ".meta").ToList();
            int bodies = Directory.GetFiles(outFull, "*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".meta")), folders = Directory.GetDirectories(outFull, "*", SearchOption.AllDirectories).Length;
            Assert.AreEqual(bodies + folders + 1, metas.Count, ".meta 수 = 본체 + 폴더 + Figures");
            var bad = new List<string>();
            foreach (var meta in metas)
            {
                string asset = Path.GetRelativePath(Root, meta.Substring(0, meta.Length - 5)).Replace('\\', '/');
                string g = Regex.Match(File.ReadAllText(meta, Utf8), "^guid: ([0-9a-f]{32})", RegexOptions.Multiline).Groups[1].Value;
                if (g != FigureImport.StableGuid(asset)) bad.Add(asset);
            }
            Assert.IsEmpty(bad, "GUID가 경로 해시가 아님: " + string.Join(", ", bad.Take(10)));
            foreach (var p in Directory.GetFiles(outFull, "*.prefab", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(Root, p).Replace('\\', '/'), text = File.ReadAllText(p, Utf8);
                Assert.AreEqual(text, FigureImport.CanonPrefab(text, rel), rel + ": 프리팹이 정규형(fileID 해시·순서)이 아님");
            }
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { FigureImport.OutRoot }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                var so = new SerializedObject(m);
                foreach (var list in new[] { "m_TexEnvs", "m_Ints", "m_Floats", "m_Colors" })
                {
                    var arr = so.FindProperty("m_SavedProperties." + list);
                    for (int i = 0; i < arr.arraySize; i++)
                    {
                        string n = arr.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue;
                        Assert.GreaterOrEqual(m.shader.FindPropertyIndex(n), 0, m.name + ": 셰이더 " + m.shader.name + "가 선언하지 않은 속성 " + n);
                    }
                }
            }
        }

        /// <summary>
        /// 들여오기 판 4(M4-2c 발견, d3-3 §6 ②): 프리팹을 기본 클립(Idle) 0초로 샘플한 뼈 자세가 원본 번들 인스턴스의 같은 자세와 같다(회전 1°·위치·배율 0.001 안).
        /// 판 3은 어느 클립에 없는 채널까지 프리팹에 구워 처음 Idle 자세가 달랐다(481 Hips y 15°, 480 16.7°, 484 더듬이 5~10° → figures.json 크기와 3~18% 어긋남).
        /// 원본 번들(AssetSources, git 밖)이 없으면 건너뛴다.
        /// </summary>
        [Test]
        public void M4_피규어처음Idle자세는번들과같다()
        {
            var rows = FigureRows();
            var bad = new List<string>();
            int seen = 0;
            foreach (var r in ListRows())
            {
                int dex = (int)r["dex"], state = (int)r["state"];
                string bundle = FigureImport.BundlePath((string)r["url"]);
                if (!File.Exists(bundle)) continue;
                var ab = AssetBundle.LoadFromFile(bundle);
                Assert.IsNotNull(ab, bundle);
                GameObject src = null, inst = null, pi = null;
                try
                {
                    src = ab.LoadAllAssets<GameObject>().First(o => o.transform.parent == null);
                    inst = Object.Instantiate(src);
                    pi = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FigureImport.PrefabPath(dex, state)));
                    var a = inst.GetComponentInChildren<Animation>(true);
                    var b = pi.GetComponentInChildren<Animation>(true);
                    SampleDefault(a);
                    SampleDefault(b);
                    foreach (var t in a.GetComponentsInChildren<Transform>(true))
                    {
                        string path = AnimationUtility.CalculateTransformPath(t, a.transform);
                        var u = path.Length == 0 ? b.transform : b.transform.Find(path);
                        if (u == null) { bad.Add(dex + ":" + state + " 뼈 없음 " + path); continue; }
                        float ang = Quaternion.Angle(t.localRotation, u.localRotation);
                        if (ang > 1f || (t.localPosition - u.localPosition).magnitude > 1e-3f || (t.localScale - u.localScale).magnitude > 1e-3f)
                            bad.Add(dex + ":" + state + " " + path + " 회전 " + ang.ToString("F1") + "°");
                    }
                    seen++;
                }
                finally
                {
                    if (inst) Object.DestroyImmediate(inst);
                    if (pi) Object.DestroyImmediate(pi);
                    ab.Unload(true);
                }
            }
            if (seen == 0) Assert.Ignore("M4 원본 번들 없음: fetch.mjs figures 먼저 (AssetSources/tts/bundles)");
            Assert.AreEqual(rows.Count, seen, "번들을 본 피규어 수");
            Assert.IsEmpty(bad, string.Join("; ", bad.Take(10)));
        }

        static void SampleDefault(Animation anim)
        {
            var st = anim[anim.clip.name];
            anim.Stop();
            st.enabled = true; st.weight = 1f; st.time = 0f;
            anim.Sample();
            st.enabled = false;
        }

        /// <summary>d3-1 방식: URP로 3/4 시점 렌더 → 분홍(r&gt;200, g&lt;60, b&gt;200) 픽셀 0, 모델이 보인다(배경과 다른 픽셀 ≥ 1%). 그림은 Logs/FigureShots에 남긴다(배치가 끝나면 Temp/는 지워진다, Logs/는 git 밖).</summary>
        [Test]
        public void M4_피규어렌더분홍픽셀0()
        {
            var rows = FigureRows();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            const int W = 256;
            var bg = new Color32(92, 102, 117, 255);
            string shots = Path.Combine(Root, "Logs/FigureShots");
            Directory.CreateDirectory(shots);
            var made = new List<Object>();
            var ambientMode = RenderSettings.ambientMode; var ambient = RenderSettings.ambientLight;
            var fails = new List<string>();
            try
            {
                var lgo = new GameObject("FigureTestLight") { hideFlags = HideFlags.DontSave }; made.Add(lgo);
                var light = lgo.AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.6f; light.shadows = LightShadows.None;
                lgo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
                var cgo = new GameObject("FigureTestCamera") { hideFlags = HideFlags.DontSave }; made.Add(cgo);
                var cam = cgo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = bg; cam.fieldOfView = 30f;
                var data = cgo.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = false;
                var rt = new RenderTexture(W, W, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); made.Add(rt);
                var tex = new Texture2D(W, W, TextureFormat.RGBA32, false); made.Add(tex);
                foreach (var key in rows.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    var parts = key.Split(':');
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FigureImport.PrefabPath(int.Parse(parts[0]), int.Parse(parts[1])));
                    Assert.IsNotNull(prefab, key);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.hideFlags = HideFlags.DontSave;
                    try
                    {
                        foreach (var sk in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { sk.updateWhenOffscreen = true; sk.forceMatrixRecalculationPerRender = true; }
                        var b = FigureImport.WorldBounds(inst);
                        float radius = Mathf.Max(b.extents.magnitude, 1e-3f) * 1.08f;
                        float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                        cam.transform.position = b.center + Quaternion.Euler(18f, 35f, 0f) * Vector3.forward * dist;
                        cam.transform.LookAt(b.center);
                        cam.nearClipPlane = Mathf.Max(0.001f, dist - radius * 2f); cam.farClipPlane = dist + radius * 2f;
                        Render(cam, rt, tex);   // 예열: 첫 렌더는 재질 상수가 섞일 수 있다(d3-1 §5-3 ⑨)
                        Render(cam, rt, tex);
                        int pink = 0, fg = 0;
                        foreach (var p in tex.GetPixels32())
                        {
                            if (Math.Abs(p.r - bg.r) + Math.Abs(p.g - bg.g) + Math.Abs(p.b - bg.b) > 12) fg++;
                            if (p.r > 200 && p.g < 60 && p.b > 200) pink++;
                        }
                        File.WriteAllBytes(Path.Combine(shots, key.Replace(':', '_') + ".png"), tex.EncodeToPNG());
                        if (pink > 0 || fg < W * W / 100) fails.Add($"{key} 분홍 {pink} 모델 {fg}px");
                    }
                    finally { Object.DestroyImmediate(inst); }
                }
            }
            finally
            {
                RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
                foreach (var o in made) if (o) Object.DestroyImmediate(o);
            }
            Assert.IsEmpty(fails, string.Join("; ", fails));
        }

        static void Render(Camera cam, RenderTexture rt, Texture2D tex)
        {
            cam.targetTexture = rt;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
        }
    }
}
