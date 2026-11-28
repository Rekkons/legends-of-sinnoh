using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// d3-3 §2-2 ② 피규어(M4-2c): 보드 씬에 피규어 33이 FigureLayout 자리·fitScale로 서고(맞춘 크기 0.75~1.8 — 결정 70 ⑦, figures.json과 2% 안),
    /// 근접 33장 분홍 0, Idle이 돈다. 고정물 아이콘 173이 칸 윗면에 Gen 아이콘으로 눕는다. 사람이 볼 그림은 배치 캡처(BoardShots → Logs/BoardShots/).
    /// Gen 본체가 없으면 Gen 부분을 건너뛴다(03 §7).
    /// </summary>
    public sealed class FigureRenderTests
    {
        BoardBoot boot;

        [UnitySetUp]
        public IEnumerator LoadBoard()
        {
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            yield return null;
            boot = Object.FindAnyObjectByType<BoardBoot>();
            Assert.That(boot, Is.Not.Null, "BoardBoot 없음");
            Assert.That(boot.Figures, Is.Not.Null, "BoardFigures 없음");
            Assert.That(boot.Icons, Is.Not.Null, "FixtureIcons 없음");
        }

        [UnityTest]
        public IEnumerator M4_피규어33맞춘크기와자리()
        {
            var figs = boot.Figures;
            if (figs.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저 — 피규어 확인 건너뜀(03 §7)");
            Assert.AreEqual(33, figs.Figures.Count, "판에 서는 피규어 33(d3-3 §2-2)");
            CollectionAssert.AreEquivalent(new[] { "800:2", "800:3", "800:4", "487:2" }, figs.OffBoard, "폼 모델 4는 판에 서지 않는다");
            Assert.AreEqual(33, figs.Figures.Select(f => f.Key).Distinct().Count());
            boot.DistortionWorldVisible = true;
            foreach (var f in figs.Figures)
            {
                string at = f.Key + " @" + boot.Board[f.Spot.Cell].Coord;
                var t = f.Root.transform;
                if (f.Group == GameFlow.TaxiFigures) { Assert.IsFalse(f.Root.activeInHierarchy, at + " 택시 찌르호크는 늘 서 있지 않다(결정 126 — 공중날기 연출 동안만)"); continue; }
                Assert.IsTrue(f.Root.activeInHierarchy, at + " 보임(깨어진 세계 켬)");
                Assert.AreEqual(f.Distortion, boot.Board[f.Spot.Cell].DistortionWorld, at);
                Assert.AreEqual(f.FitScale, t.localScale.x, 1e-5f, at + " 새 루트 배율 = fitScale");
                Assert.AreEqual(t.localScale.x, t.localScale.y, 1e-6f); Assert.AreEqual(t.localScale.x, t.localScale.z, 1e-6f);
                Assert.AreEqual(0.01f, t.GetChild(0).localScale.x, 1e-6f, at + " 번들 루트 0.01은 그대로");
                Assert.AreEqual((float)f.Spot.X, t.position.x, 0.001f, at + " x = 칸 중심 + 칸 안 자리");
                Assert.AreEqual((float)f.Spot.Y, t.position.y, 0.001f, at + " y = 칸 윗면");
                Assert.AreEqual((float)f.Spot.Z, t.position.z, 0.001f, at + " z");
                Assert.AreEqual((float)BoardLayout.TopY(boot.Board[f.Spot.Cell]), t.position.y, 0.001f, at);
                Assert.That(f.Animation, Is.Not.Null, at + " 레거시 Animation");
                Assert.IsTrue(f.Animation.IsPlaying(f.IdleClip), at + " Idle 재생 중");
                Assert.AreEqual(WrapMode.Loop, f.Animation[f.IdleClip].wrapMode, at + " Idle 반복");

                BoardFigures.Pose(f, 0f);
                var b = BoardFigures.BakedBounds(f.Root);
                float fitted = Mathf.Max(b.size.x, b.size.y), want = Mathf.Max(f.FittedSize.x, f.FittedSize.y);
                Assert.That(fitted, Is.InRange(0.75f - 0.01f, 1.8f + 0.01f), at + " 맞춘 크기 0.75~1.8");
                Assert.AreEqual(want, fitted, want * 0.02f, at + " 맞춘 크기 = figures.json ± 2%");
                BoardFigures.PlayIdle(f);
            }
            boot.DistortionWorldVisible = false;
            foreach (var f in figs.Figures)
                Assert.AreEqual(!f.Distortion && f.Group != GameFlow.TaxiFigures, f.Root.activeInHierarchy, f.Key + ": 깨어진 세계 피규어는 칸과 함께 숨는다(택시는 늘 숨음 — 결정 126)");
            yield return null;
        }

        [UnityTest]
        public IEnumerator M4_피규어근접33장분홍0과Idle재생()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var figs = boot.Figures;
            if (figs.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저 — 피규어 렌더 건너뜀(03 §7)");
            var rig = Camera.main.GetComponent<BoardCameraRig>();
            var cam = rig.GetComponent<Camera>();
            boot.DistortionWorldVisible = true;
            boot.Icons.gameObject.SetActive(false); // 아이콘 그림에 분홍 범위 색이 있다(Sinnome 마을) — 칸·피규어만 센다(d3-3 §2-2 (c))
            const int W = 480, H = 270;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var still = new List<string>();
            // (d)는 피규어마다 따로 본다: 찍는 동안 다른 피규어의 렌더러를 끈다(애니는 계속 돈다) — 근접 화면에 이웃 피규어(같은 칸 3신 등)가 함께 찍혀
            // 대상이 멈춰 있어도 이웃의 Idle 때문에 통과하던 것을 막는다(M4-2 검토 반영). 판·말은 움직이지 않는다
            var renderers = figs.Figures.ToDictionary(f => f.Key, f => f.Root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray());
            void Only(BoardFigures.Figure target)
            {
                foreach (var f in figs.Figures)
                    foreach (var r in renderers[f.Key]) r.enabled = target == null || f == target;
            }
            try
            {
                foreach (var f in figs.Figures)
                    foreach (var s in f.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.forceMatrixRecalculationPerRender = true;
                var before = new Dictionary<string, Color32[]>();
                foreach (var f in figs.Figures)
                {
                    Only(f);
                    rig.Set(BoardCameraRig.Zoom.Close, 0, f.Root.transform.position);
                    OffscreenShot.Render(cam, rt, tex);
                    OffscreenShot.Render(cam, rt, tex);
                    OffscreenShot.Stats(tex, cam.backgroundColor, out int pink, out _);
                    Assert.AreEqual(0, pink, f.Key + " 근접 분홍");
                    before[f.Key] = tex.GetPixels32();
                }
                // Idle이 10프레임 돈다. 배치 모드는 프레임이 아주 짧을 수 있어 게임 시간 0.3초도 기다린다
                float t0 = Time.time;
                for (int i = 0; i < 10 || Time.time - t0 < 0.3f; i++) yield return null;
                foreach (var f in figs.Figures)
                {
                    Only(f);
                    rig.Set(BoardCameraRig.Zoom.Close, 0, f.Root.transform.position);
                    OffscreenShot.Render(cam, rt, tex);
                    OffscreenShot.Render(cam, rt, tex);
                    var a = before[f.Key];
                    var b = tex.GetPixels32();
                    int changed = 0;
                    for (int p = 0; p < a.Length; p++)
                        if (Mathf.Abs(a[p].r - b[p].r) + Mathf.Abs(a[p].g - b[p].g) + Mathf.Abs(a[p].b - b[p].b) > 12) changed++;
                    if (changed == 0 && f.Group != GameFlow.TaxiFigures) still.Add(f.Key);   // 택시는 숨어 있다(결정 126)
                }
            }
            finally
            {
                Only(null);
                boot.Icons.gameObject.SetActive(true);
                Object.Destroy(rt);
                Object.Destroy(tex);
            }
            Assert.IsEmpty(still, "10프레임 사이 자세가 바뀌지 않은 피규어(Idle이 돌지 않음, 피규어마다 혼자 그림)");
        }

        [UnityTest]
        public IEnumerator M4_고정물아이콘173칸()
        {
            var icons = boot.Icons;
            if (icons.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저 — 고정물 아이콘 확인 건너뜀(03 §7)");
            var fixtures = boot.Fixtures.Fixtures;
            Assert.AreEqual(174, fixtures.Count, "fixtures.json 174(d3-3 §2-2 대조: 04 §3-1의 214)");
            Assert.AreEqual(173, icons.Icons.Count, "택시를 뺀 고정물마다 아이콘 하나");
            CollectionAssert.AreEqual(fixtures.Where(f => f.Kind != FixtureKind.Taxi).Select(f => f.Key), icons.Icons.Select(i => i.FixtureKey), "fixtures 순서");
            var textures = new HashSet<string>();
            foreach (var ic in icons.Icons)
            {
                Assert.AreEqual(fixtures.First(f => f.Key == ic.FixtureKey).Cell, ic.Cell, ic.FixtureKey);
                StringAssert.StartsWith("Board/Icons/", ic.Texture, ic.FixtureKey);
                Assert.That(GenAssets.Load<Texture2D>(ic.Texture), Is.Not.Null, ic.FixtureKey + " 그림 " + ic.Texture);
                textures.Add((boot.Board[ic.Cell].DistortionWorld ? "1|" : "0|") + ic.Texture);
            }
            Assert.AreEqual(textures.Count, icons.Renderers.Count, "그림·부분마다 메시 하나(그리기 호출 = 그림 수)");
            foreach (var mr in icons.Renderers)
            {
                var m = mr.sharedMaterial;
                Assert.That(m, Is.Not.Null, mr.name);
                Assert.AreEqual("Sinnoh/BoardIcon", m.shader.name, mr.name + " 셰이더(분홍 = 셰이더 없음을 여기서 본다 — 그림에 분홍 범위 색이 있어 렌더로는 못 센다)");
                Assert.IsTrue(m.shader.isSupported, mr.name + " 셰이더 지원");
                Assert.AreEqual(mr.name, AssetNameOf(m.GetTexture("_BaseMap")), "재질 그림 = 그 묶음 그림");
            }
            // 메시 정점 = 아이콘 수 × 4, 정점이 칸 윗면 + Lift 높이
            int quads = 0;
            foreach (var mr in icons.Renderers)
            {
                var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
                quads += mesh.vertexCount / 4;
                var v = mesh.vertices;
                for (int q = 0; q < v.Length; q += 4)
                {
                    var c = (v[q] + v[q + 2]) * 0.5f;
                    Assert.AreEqual(FixtureIcons.Size, v[q + 2].x - v[q].x, 1e-4f, "아이콘 크기");
                    Assert.IsTrue(icons.Icons.Any(ic => Mathf.Abs((float)boot.Looks[ic.Cell].X - c.x) < 0.001f && Mathf.Abs((float)boot.Looks[ic.Cell].Z - c.z) < 0.001f
                        && Mathf.Abs((float)BoardLayout.TopY(boot.Board[ic.Cell]) + FixtureIcons.Lift - c.y) < 0.001f), "아이콘 가운데가 고정물 칸 윗면 " + c);
                }
            }
            Assert.AreEqual(173, quads);
            yield return null;
        }

        static string AssetNameOf(Texture t) => t == null ? null : "Board/Icons/" + t.name + ".png";
    }
}
