using System.Collections;
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
    /// d3-3 §2-1 ① 보드 칸(M4-2b): 보드 씬의 칸 메시가 board.json·BoardLayout과 같다(칸 351, 보이는 칸 326, 중심·높이·외접·띠·틴트·디퓨즈),
    /// 카메라 줌 3단 칸 폭, 18장(줌 3 × 방향 6) 분홍 0. 사람이 볼 그림은 배치 캡처(Sinnoh.Editor.BoardShots → Logs/BoardShots/).
    /// </summary>
    public sealed class BoardRenderTests
    {
        BoardBoot boot;

        [UnitySetUp]
        public IEnumerator LoadBoard()
        {
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            yield return null;
            boot = Object.FindAnyObjectByType<BoardBoot>();
            Assert.That(boot, Is.Not.Null, "BoardBoot 없음");
            Assert.That(boot.View, Is.Not.Null, "BoardView 없음");
        }

        [UnityTest]
        public IEnumerator M4_보드칸351과높이()
        {
            var view = boot.View;
            Assert.AreEqual(351, view.Spans.Length);
            Assert.IsTrue(view.Renderer(0).gameObject.activeInHierarchy, "본맵");
            Assert.IsFalse(view.Renderer(1).gameObject.activeInHierarchy, "깨어진 세계는 울트라 버스트에서만(04 §3-1 [가정])");
            var verts = new[] { view.Filter(0).sharedMesh.vertices, view.Filter(1).sharedMesh.vertices };
            int visible = 0;
            var used = new int[2];
            for (int i = 0; i < view.Spans.Length; i++)
            {
                var s = view.Spans[i];
                var cell = boot.Board[i];
                var look = boot.Looks[i];
                string at = cell.Coord.ToString();
                Assert.AreEqual(cell.DistortionWorld ? 1 : 0, s.Part, at);
                if (view.Renderer(s.Part).gameObject.activeInHierarchy) visible++;
                var v = verts[s.Part];
                var c = v[s.TopStart];
                Assert.AreEqual((float)look.X, c.x, 0.006f, at + " 중심 x");
                Assert.AreEqual((float)look.Z, c.z, 0.006f, at + " 중심 z");
                Assert.AreEqual((float)BoardLayout.TopY(cell), c.y, 0.001f, at + " 윗면 = level × 0.78");
                for (int k = 1; k <= 6; k++)
                {
                    var p = v[s.TopStart + k];
                    Assert.AreEqual(c.y, p.y, 1e-5f, at);
                    Assert.AreEqual(1.1f, new Vector2(p.x - c.x, p.z - c.z).magnitude, 0.001f, at + " 외접 반지름");
                }
                Assert.AreEqual(0f, v[s.TopStart + 1].z - c.z, 1e-4f, at + " flat-top: 첫 꼭짓점이 +x");
                var bands = BoardLayout.Bands(cell, look);
                Assert.AreEqual(bands.Length, s.Bands, at + " 띠 수");
                for (int b = 0; b < bands.Length; b++)
                    for (int f = 0; f < 6; f++)
                    {
                        int q = s.SideStart + b * BoardMeshBuilder.SideVertsPerBand + f * 4;
                        Assert.AreEqual((float)bands[b].Top, v[q].y, 0.001f, at + " 띠 위");
                        Assert.AreEqual((float)bands[b].Bottom, v[q + 2].y, 0.001f, at + " 띠 밑");
                    }
                used[s.Part] += BoardMeshBuilder.TopVerts + s.Bands * BoardMeshBuilder.SideVertsPerBand;
            }
            Assert.AreEqual(326, visible, "보이는 칸");
            Assert.AreEqual(verts[0].Length, used[0], "본맵 정점 = 칸 몫의 합");
            Assert.AreEqual(verts[1].Length, used[1], "깨어진 세계 정점 = 칸 몫의 합");
            yield return null;
        }

        [UnityTest]
        public IEnumerator M4_칸틴트와디퓨즈는boardjson()
        {
            var view = boot.View;
            bool genMissing = false;
            for (int part = 0; part < BoardMeshBuilder.Parts; part++)
            {
                var mesh = view.Filter(part).sharedMesh;
                var mats = view.Renderer(part).sharedMaterials;
                Assert.AreEqual(mesh.subMeshCount, mats.Length);
                // 정점 → 그 정점을 쓰는 서브메시의 묶음
                var group = new HexGroup?[mesh.vertexCount];
                var groups = new HexGroup[mesh.subMeshCount];
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    groups[s] = (HexGroup)System.Enum.Parse(typeof(HexGroup), mats[s].name.Substring("Hex ".Length));
                    foreach (int idx in mesh.GetTriangles(s)) group[idx] = groups[s];
                    if (groups[s] <= HexGroup.Secret)
                    {
                        var want = GenAssets.Load<Texture2D>("Board/hex_" + groups[s] + ".png");
                        if (want == null) genMissing = true;
                        else Assert.AreSame(want, mats[s].GetTexture("_BaseMap"), "디퓨즈 " + groups[s]);
                    }
                }
                CollectionAssert.AllItemsAreUnique(groups, "묶음마다 서브메시 하나");
                var colors = mesh.colors32;
                for (int i = 0; i < view.Spans.Length; i++)
                {
                    var s = view.Spans[i];
                    if (s.Part != part) continue;
                    var look = boot.Looks[i];
                    string at = boot.Board[i].Coord.ToString();
                    var tint = BoardMeshBuilder.ParseTint(look.Tint);
                    Assert.AreEqual(look.Tint, Hex(tint), at);
                    for (int k = 0; k < BoardMeshBuilder.TopVerts; k++)
                    {
                        Assert.AreEqual(look.Tint, Hex(colors[s.TopStart + k]), at + " 윗면 틴트");
                        Assert.AreEqual((HexGroup)(int)look.Diffuse, group[s.TopStart + k], at + " 윗면 디퓨즈");
                    }
                    var bands = BoardLayout.Bands(boot.Board[i], look);
                    for (int b = 0; b < bands.Length; b++)
                        for (int k = 0; k < BoardMeshBuilder.SideVertsPerBand; k++)
                        {
                            int q = s.SideStart + b * BoardMeshBuilder.SideVertsPerBand + k;
                            Assert.AreEqual(b < look.Pillars.Count ? look.Pillars[b] : look.Tint, Hex(colors[q]), at + " 띠 " + b + " 틴트");
                            Assert.AreEqual(HexGroup.Sides, group[q], at + " 옆면");
                        }
                }
            }
            yield return null;
            if (genMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저 — 디퓨즈 텍스처 확인만 건너뜀(틴트·묶음은 통과, 03 §7)");
        }

        [UnityTest]
        public IEnumerator M4_줌3단칸폭()
        {
            var rig = Camera.main.GetComponent<BoardCameraRig>();
            Assert.That(rig, Is.Not.Null, "Main Camera에 BoardCameraRig");
            var cam = rig.GetComponent<Camera>();
            var proj = Matrix4x4.Perspective(BoardCameraRig.FieldOfView, 16f / 9f, cam.nearClipPlane, cam.farClipPlane);
            Vector2 Px(Vector3 w)
            {
                var c = proj * (cam.worldToCameraMatrix * new Vector4(w.x, w.y, w.z, 1f));
                return new Vector2((c.x / c.w * 0.5f + 0.5f) * 1920f, (c.y / c.w * 0.5f + 0.5f) * 1080f);
            }
            var target = new Vector3(0f, 4.68f, 0f);
            foreach (BoardCameraRig.Zoom z in System.Enum.GetValues(typeof(BoardCameraRig.Zoom)))
                for (int yaw = 0; yaw < 6; yaw++)
                {
                    rig.Set(z, yaw, target);
                    Assert.AreEqual(BoardCameraRig.FieldOfView, cam.fieldOfView, 1e-4f);
                    Assert.AreEqual(55f, cam.transform.eulerAngles.x, 1e-3f);
                    Assert.AreEqual(0f, Mathf.DeltaAngle(yaw * 60f, cam.transform.eulerAngles.y), 1e-3f);
                    var right = cam.transform.right * 1.1f;
                    Vector2 a = Px(target - right), b = Px(target + right), mid = Px(target);
                    float want = BoardCameraRig.CellPixels(z);
                    Assert.AreEqual(want, Vector2.Distance(a, b), want * 0.05f, $"{z} 방향 {yaw * 60}° 칸 폭");
                    Assert.AreEqual(0f, a.y - b.y, 0.01f, "대각선이 화면 가로와 나란하다");
                    Assert.AreEqual(960f, mid.x, 0.01f); Assert.AreEqual(540f, mid.y, 0.01f);
                }
            // 1080에서 51/130/245 px → 거리 86.9/34.1/18.1(d3-3 §3)
            Assert.AreEqual(86.93f, BoardCameraRig.Distance(BoardCameraRig.Zoom.Full), 0.01f);
            Assert.AreEqual(34.10f, BoardCameraRig.Distance(BoardCameraRig.Zoom.Region), 0.01f);
            Assert.AreEqual(18.10f, BoardCameraRig.Distance(BoardCameraRig.Zoom.Close), 0.01f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator M4_보드렌더18장분홍0()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var rig = Camera.main.GetComponent<BoardCameraRig>();
            var cam = rig.GetComponent<Camera>();
            boot.Board.TryGetIndex(new HexCoord(0, 0), out int summit);
            var top = new Vector3((float)boot.Looks[summit].X, (float)BoardLayout.TopY(boot.Board[summit]), (float)boot.Looks[summit].Z);
            var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            // M4-2c: 고정물 아이콘 그림 자체에 분홍 정의 범위 색이 있어(Sinnome 마을) 칸·피규어만 센다. 아이콘 재질은 M4_고정물아이콘173칸이 본다(d3-3 §2-2 (c))
            if (boot.Icons != null) boot.Icons.gameObject.SetActive(false);
            try
            {
                foreach (BoardCameraRig.Zoom z in System.Enum.GetValues(typeof(BoardCameraRig.Zoom)))
                    for (int yaw = 0; yaw < 6; yaw++)
                    {
                        rig.Set(z, yaw, z == BoardCameraRig.Zoom.Full ? Vector3.zero : top);
                        OffscreenShot.Render(cam, rt, tex);
                        OffscreenShot.Render(cam, rt, tex);
                        OffscreenShot.Stats(tex, cam.backgroundColor, out int pink, out float coverage);
                        Assert.AreEqual(0, pink, $"{z} 방향 {yaw * 60}° 분홍");
                        if (z == BoardCameraRig.Zoom.Full) Assert.GreaterOrEqual(coverage, 0.15f, $"전체 방향 {yaw * 60}° 판이 화면의 15% 이상(d3-3 §2-1 (c), M4-2b에서 25% → 15%)");
                    }
            }
            finally
            {
                if (boot.Icons != null) boot.Icons.gameObject.SetActive(true);
                Object.Destroy(rt);
                Object.Destroy(tex);
            }
            yield return null;
        }

        static string Hex(Color32 c) => $"#{c.r:x2}{c.g:x2}{c.b:x2}{c.a:x2}";
    }
}
