using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// d3-3 §2-3 ③ 말(M4-2d): 보드 씬에 말 6(trainer_cards 앞 6 직업 그림, 고향 마을 6칸)이 서고, 화면 공간에 정수배 k로 그려진다 — 크기 안 E(결정 47 ④·61 ②):
    /// 1080에서 전체 줌 = 얼굴 조각 30×30 ×1, 지역 = 그림 전체 80×80 ×1, 근접 ×2. 사각형이 정수 픽셀에 서고 원본(조각)을 k배 최근접으로 키운 것과 픽셀까지 같다(MAE 0),
    /// 카메라를 픽셀보다 조금 옮겨도 같다(떨림 없음).
    /// 한 칸 6명은 칸 안 6자리로 벌린다. 사람이 볼 그림은 배치 캡처(BoardShots → Logs/BoardShots/pieces-*.png·pieces6-*.png).
    /// Gen 본체가 없으면 건너뛴다(03 §7).
    /// </summary>
    public sealed class PieceRenderTests
    {
        const int W = 1920, H = 1080;
        BoardBoot boot;

        [UnitySetUp]
        public IEnumerator LoadBoard()
        {
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            yield return null;
            boot = Object.FindAnyObjectByType<BoardBoot>();
            Assert.That(boot, Is.Not.Null, "BoardBoot 없음");
            Assert.That(boot.Pieces, Is.Not.Null, "BoardPieces 없음");
            Style(PieceStyle.Job);   // 아래 시험은 직업 그림 양식을 본다(화면 흐름의 기본은 주인공 도트 — 그 양식은 M4_주인공도트말6…)
        }

        // 말 그림 양식을 바꾼다: 화면 흐름이 있으면 그 설정으로(흐름이 화면을 바꿀 때 제 설정으로 되돌리지 않게), 없으면 보드에 바로
        void Style(PieceStyle style)
        {
            var screens = Object.FindAnyObjectByType<ScreenBoot>();
            if (screens != null && screens.Flow != null) screens.Flow.PieceStyle = style;
            else boot.PieceStyle = style;
        }

        [UnityTest]
        public IEnumerator M4_주인공도트말6은화면정수배1_3_5이고보는쪽의프레임을그린다_결정47()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            Style(PieceStyle.Dot);
            var pcs = boot.Pieces;
            if (pcs.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저 — 말 확인 건너뜀(03 §7)");

            // 6개: 좌석 i = 주인공 i번째(시험 판), 필드 도트 시트의 32×32 한 칸, 서 있으면 앞모습
            Assert.AreEqual(6, pcs.Pieces.Count, "말 6");
            for (int i = 0; i < 6; i++)
            {
                var p = pcs.Pieces[i];
                Assert.AreEqual(PieceStyle.Dot, p.Style);
                Assert.AreEqual(Sinnoh.Rules.Session.Protagonists.Keys[i], p.Protagonist, "주인공 = 고를 수 있는 주인공의 앞 6");
                StringAssert.StartsWith("Field/", p.TextureKey);
                Assert.AreEqual(32, p.Width); Assert.AreEqual(32, p.Height);
                Assert.AreEqual(FilterMode.Point, p.Texture.filterMode, "Point(03 §2-0 들여오기)");
                Assert.AreEqual(Sinnoh.Fx.Facing.South, p.Facing);
                Assert.AreEqual(p.Cycles[(int)Sinnoh.Fx.Facing.South][0], p.Frame, "서 있으면 앞모습의 선 프레임");
                Assert.AreEqual(boot.HomeCells[i], p.Spot.Cell, "고향 칸");
            }
            CollectionAssert.AreEqual(new[] { 1, 3, 5 }, new[] { 1, 2, 3 }.Select(step => BoardPieces.Scale(BoardPieces.KFor(PieceStyle.Dot, step), 1080)).ToArray(), "크기 안 B1: 1080에서 ×1·×3·×5");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, new[] { 1, 2, 3 }.Select(step => BoardPieces.Scale(BoardPieces.KFor(PieceStyle.Dot, step), 720)).ToArray(), "720이면 ×1·×2·×3 [계산]");
            CollectionAssert.AreEqual(new[] { 1, 1, 2 }, new[] { 1, 2, 3 }.Select(step => BoardPieces.KFor(PieceStyle.Job, step)).ToArray(), "직업 그림은 크기 안 E: 얼굴 조각 ×1 · ×1 · ×2");

            // 좌석 0·1은 걷는 그림으로(서쪽을 보고 발을 내민 그림 · 북쪽을 본 선 모습) — 그 프레임이 그려지는지 본다
            Assert.IsTrue(pcs.Pose(0, Sinnoh.Fx.Facing.West, 1));
            Assert.IsTrue(pcs.Pose(1, Sinnoh.Fx.Facing.North, 0));
            Assert.AreEqual(pcs.Pieces[0].Cycles[(int)Sinnoh.Fx.Facing.West][1], pcs.Pieces[0].Frame);
            Assert.AreNotEqual(pcs.Pieces[0].Cycles[(int)Sinnoh.Fx.Facing.South][0], pcs.Pieces[0].Frame);

            // 원본 = 시트(아래가 0행)에서 지금 프레임의 32줄
            var src = pcs.Pieces.Select(p =>
            {
                var sheet = PieceShot.LoadSource(p.TextureKey, out int sw, out int sh);
                return sheet.Skip((sh - (p.Frame + 1) * p.Height) * sw).Take(p.Width * p.Height).ToArray();
            }).ToList();
            var pins = pcs.Pieces.Select(p => p.PinTexture.GetPixels32()).ToList();
            var rig = Camera.main.GetComponent<BoardCameraRig>();
            var cam = rig.GetComponent<Camera>();
            boot.View.gameObject.SetActive(false);
            boot.Figures.gameObject.SetActive(false);
            boot.Icons.gameObject.SetActive(false);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            int edges = 0;
            try
            {
                foreach (BoardCameraRig.Zoom zoom in System.Enum.GetValues(typeof(BoardCameraRig.Zoom)))
                {
                    int zk = BoardPieces.ZoomK(zoom), checkedPieces = 0;
                    rig.Set(zoom, 0, zoom == BoardCameraRig.Zoom.Full ? Vector3.zero : pcs.Pieces[1].Root.transform.position);
                    OffscreenShot.Render(cam, rt, tex);
                    OffscreenShot.Render(cam, rt, tex);
                    var shot = tex.GetPixels32();
                    cam.targetTexture = rt;
                    for (int i = 0; i < 6; i++)
                    {
                        var p = pcs.Pieces[i];
                        var rect = BoardPieces.BodyRect(p, cam, zk, out int k, out var raw);
                        string at = $"{zoom} 좌석 {i + 1} {p.Protagonist}";
                        Assert.AreEqual(2 * zk - 1, k, at + " k = 1·3·5");
                        Assert.AreEqual(32 * k, rect.width, at); Assert.AreEqual(32 * k, rect.height, at);
                        bool inView = rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= W && rect.yMax <= H;
                        if (zoom == BoardCameraRig.Zoom.Full) Assert.IsTrue(inView, at + " 전체 줌에서 6명 모두 화면 안");
                        if (!inView) continue;
                        bool edge = Mathf.Abs(raw.x - Mathf.Round(raw.x)) < 1e-3f || Mathf.Abs(raw.y - Mathf.Round(raw.y)) < 1e-3f;
                        PieceShot.Compare(shot, W, H, src[i], p.Width, p.Height, rect, k, out int n, out int bad, out long diff, out int off);
                        if (bad > 0 && edge) edges++;
                        else Assert.AreEqual(0, bad, $"{at} 프레임 {p.Frame}의 k배와 다른 픽셀 {bad}/{n}, MAE {diff / (3f * Mathf.Max(1, n)):F3}, 사각형 {rect}");
                        Assert.AreEqual(0, off, at);
                        Assert.Greater(n, 0, at);
                        var pin = BoardPieces.PinRect(p, cam, zk);
                        Assert.AreEqual(rect.y + (p.Height - p.Head + 1) * k, pin.y, at + " 핀은 머리 줄 위 1텍셀");
                        PieceShot.Compare(shot, W, H, pins[i], PieceArt.PinSize, PieceArt.PinSize, pin, k, out int pn, out int pbad, out _, out _);
                        if (!(pbad > 0 && edge)) Assert.AreEqual(0, pbad, $"{at} 좌석 번호 핀이 PieceArt k배와 다른 픽셀 {pbad}/{pn}");
                        checkedPieces++;
                    }
                    cam.targetTexture = null;
                    Assert.GreaterOrEqual(checkedPieces, zoom == BoardCameraRig.Zoom.Full ? 6 : 1, zoom + ": 화면 안 말을 줌마다 본다");
                }
            }
            finally
            {
                cam.targetTexture = null;
                boot.View.gameObject.SetActive(true);
                boot.Figures.gameObject.SetActive(true);
                boot.Icons.gameObject.SetActive(true);
                Object.Destroy(rt);
                Object.Destroy(tex);
            }
            Assert.LessOrEqual(edges, 1, "발 점 내림이 갈린 경우는 드물어야 한다");

            // 말 그림을 직업 그림으로 바꾸면 같은 좌석·같은 칸에 직업 그림으로 다시 선다. 직업이 없는 좌석(트레이너 카드를 끈 판)은 그 설정에서도 주인공 도트다
            Style(PieceStyle.Job);
            Assert.AreEqual(6, pcs.Pieces.Count);
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(PieceStyle.Job, pcs.Pieces[i].Style);
                Assert.AreEqual(boot.HomeCells[i], pcs.Pieces[i].Spot.Cell);
            }
            Assert.AreEqual(PieceStyle.Dot, BoardPieces.StyleOf(new BoardPieces.SeatLook(null, "village.stark", "bw_001_hilda"), PieceStyle.Job), "직업이 없으면 주인공 도트로 떨어진다");
            Assert.AreEqual(PieceStyle.Job, BoardPieces.StyleOf(new BoardPieces.SeatLook("artist", "village.stark", "bw_001_hilda"), PieceStyle.Job));
            Assert.AreEqual(PieceStyle.Dot, BoardPieces.StyleOf(new BoardPieces.SeatLook("artist", "village.stark", "bw_001_hilda"), PieceStyle.Dot));
            Assert.AreEqual(PieceStyle.Job, BoardPieces.StyleOf(new BoardPieces.SeatLook("artist", "village.stark"), PieceStyle.Dot), "주인공이 없으면 직업 그림");
            Assert.IsNull(BoardPieces.StyleOf(new BoardPieces.SeatLook(null, "village.stark"), PieceStyle.Dot), "둘 다 없으면 말 없음");
            yield return null;
        }

        [UnityTest]
        public IEnumerator M4_직업그림말6은전체줌얼굴조각이고지역1배근접2배다_결정61()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var pcs = boot.Pieces;
            if (pcs.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저 — 말 확인 건너뜀(03 §7)");

            // (a) 6개: 좌석 i = trainer_cards i번째 직업 · fixtures i번째 마을(시험 판 [가정 M4-2d]), 고향 칸 가운데
            Assert.AreEqual(6, pcs.Pieces.Count, "말 6");
            var homes = PieceLayout.DemoHomes(boot.Fixtures, 6);
            for (int i = 0; i < 6; i++)
            {
                var p = pcs.Pieces[i];
                Assert.AreEqual(i, p.Seat);
                Assert.AreEqual(boot.Cards.TrainerCards[i].Key, p.Trainer, "직업 = trainer_cards 앞 6");
                Assert.AreEqual("Trainers/Front/" + p.Trainer + ".png", p.TextureKey);
                Assert.AreEqual(boot.Fixtures.Fixtures[homes[i]].Key, p.Village);
                Assert.AreEqual(SeatColors.ForVillage(p.Village), p.Color, "좌석 색 B = 고향 마을 색");
                Assert.AreNotEqual(SeatColors.Unassigned, p.Color);
                Assert.AreEqual(boot.HomeCells[i], p.Spot.Cell, "고향 칸");
                Assert.AreEqual(80, p.Width); Assert.AreEqual(80, p.Height);
                Assert.AreEqual(FilterMode.Point, p.Texture.filterMode, "Point(03 §2-0 들여오기)");
                Assert.AreEqual("Sinnoh/BoardPiece", p.Body.shader.name);
                Assert.IsTrue(p.Body.shader.isSupported, "셰이더 지원");
            }
            CollectionAssert.AllItemsAreUnique(pcs.Pieces.Select(p => p.Color), "좌석 색이 겹치지 않는다");
            // 크기 안 E의 등급별 배수(04 §9-3 표): 720 얼굴 1 · 1 · 1 / 1080 얼굴 1 · 1 · 2 / 1440 얼굴 1 · 1 · 3 / 2160 얼굴 2 · 2 · 4
            int[] Ks(int height) => new[] { 1, 2, 3 }.Select(step => BoardPieces.Scale(BoardPieces.KFor(PieceStyle.Job, step), height)).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, Ks(720));
            CollectionAssert.AreEqual(new[] { 1, 1, 2 }, Ks(1080));
            CollectionAssert.AreEqual(new[] { 1, 1, 3 }, Ks(1440));
            CollectionAssert.AreEqual(new[] { 2, 2, 4 }, Ks(2160));
            foreach (var p in pcs.Pieces)
            {
                Assert.AreEqual(new Vector2Int(30, 30), p.Face.size, p.Trainer + " 얼굴 조각 30×30");
                Assert.IsTrue(BoardPieces.FaceAt(p, 1) && !BoardPieces.FaceAt(p, 2) && !BoardPieces.FaceAt(p, 3), "얼굴 조각은 전체 줌에서만");
                var whole = BoardPieces.ShapeAt(p, 2);
                Assert.AreEqual((80, 80, p.Foot), (whole.Width, whole.Height, whole.Foot));
                Assert.Less(BoardPieces.PinTop(p, 1), BoardPieces.PinTop(p, 2), "전체 줌의 핀은 얼굴 조각 위로 내려온다");
            }

            var src = pcs.Pieces.Select(p => PieceShot.LoadSource(p.TextureKey, out _, out _)).ToList();
            var pins = pcs.Pieces.Select(p => p.PinTexture.GetPixels32()).ToList();
            var rig = Camera.main.GetComponent<BoardCameraRig>();
            var cam = rig.GetComponent<Camera>();
            // 말만 그린다(판·피규어·아이콘 숨김) — 가림 없이 모든 불투명 픽셀을 본다. 가림이 섞인 그림은 BoardShots 기록
            boot.View.gameObject.SetActive(false);
            boot.Figures.gameObject.SetActive(false);
            boot.Icons.gameObject.SetActive(false);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var report = new List<string>();
            try
            {
                // 카메라를 픽셀보다 조금씩 옮겨도(0 · 0.37 · 0.71픽셀 남짓) 그림은 정수 픽셀만 옮겨 다닌다 — 떨림 없음
                var nudges = new[] { Vector3.zero, new Vector3(0.0137f, 0f, 0.0091f), new Vector3(-0.0263f, 0f, 0.0177f) };
                foreach (BoardCameraRig.Zoom zoom in System.Enum.GetValues(typeof(BoardCameraRig.Zoom)))
                {
                    int zk = BoardPieces.ZoomK(zoom), checkedPieces = 0;
                    var focus = zoom == BoardCameraRig.Zoom.Full ? Vector3.zero : pcs.Pieces[1].Root.transform.position;
                    foreach (var nudge in nudges)
                    {
                        rig.Set(zoom, 0, focus + nudge * (zoom == BoardCameraRig.Zoom.Full ? 3f : 1f));
                        OffscreenShot.Render(cam, rt, tex);
                        OffscreenShot.Render(cam, rt, tex);
                        var shot = tex.GetPixels32();
                        cam.targetTexture = rt;
                        for (int i = 0; i < 6; i++)
                        {
                            var p = pcs.Pieces[i];
                            var rect = BoardPieces.BodyRect(p, cam, zk, out int k, out var raw);
                            string at = $"{zoom} 좌석 {i + 1} {p.Trainer} 밀기 {nudge}";
                            var sh = BoardPieces.ShapeAt(p, zk);
                            Assert.AreEqual(zk == 3 ? 2 : 1, k, at + " k = 1·1·2");
                            int side = zk == 1 ? 30 : 80;
                            Assert.AreEqual(side * k, rect.width, at + " (b) 폭"); Assert.AreEqual(side * k, rect.height, at + " (b) 높이");
                            bool inView = rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= W && rect.yMax <= H;
                            if (zoom == BoardCameraRig.Zoom.Full) Assert.IsTrue(inView, at + " 전체 줌에서 6명 모두 화면 안");
                            if (!inView) continue;
                            // 발 점이 정수에 아주 가까우면 GPU·CPU 내림이 갈릴 수 있다 — 그때는 한 픽셀 옆도 본다
                            bool edge = Mathf.Abs(raw.x - Mathf.Round(raw.x)) < 1e-3f || Mathf.Abs(raw.y - Mathf.Round(raw.y)) < 1e-3f;
                            PieceShot.Compare(shot, W, H, BoardPieces.Cut(p, src[i], zk), sh.Width, sh.Height, rect, k, out int n, out int bad, out long diff, out int off);
                            if (bad > 0 && edge) report.Add(at + $" 발 점 {raw} 정수 가까움 → 엇갈림 {bad}");
                            else Assert.AreEqual(0, bad, $"{at} (c) 원본 k배와 다른 픽셀 {bad}/{n}, MAE {diff / (3f * Mathf.Max(1, n)):F3}, 사각형 {rect}");
                            Assert.AreEqual(0, off, at);
                            Assert.Greater(n, 0, at);
                            var pin = BoardPieces.PinRect(p, cam, zk);
                            if (zk == 1) Assert.AreEqual(rect.y + 31 * k, pin.y, at + " 핀은 얼굴 조각 위 1텍셀");
                            PieceShot.Compare(shot, W, H, pins[i], PieceArt.PinSize, PieceArt.PinSize, pin, k, out int pn, out int pbad, out _, out _);
                            if (!(pbad > 0 && edge)) Assert.AreEqual(0, pbad, $"{at} 좌석 번호 핀이 PieceArt k배와 다른 픽셀 {pbad}/{pn}");
                            checkedPieces++;
                        }
                        cam.targetTexture = null;
                    }
                    Assert.GreaterOrEqual(checkedPieces, zoom == BoardCameraRig.Zoom.Full ? 18 : 3, zoom + ": 화면 안 말을 줌마다 본다");
                }
            }
            finally
            {
                cam.targetTexture = null;
                boot.View.gameObject.SetActive(true);
                boot.Figures.gameObject.SetActive(true);
                boot.Icons.gameObject.SetActive(true);
                Object.Destroy(rt);
                Object.Destroy(tex);
            }
            if (report.Count > 0) Debug.Log("[M4_직업그림말6] " + string.Join("; ", report));
            Assert.LessOrEqual(report.Count, 1, "발 점 내림이 갈린 경우는 드물어야 한다");
            yield return null;
        }

        [UnityTest]
        public IEnumerator M4_말6한칸은6자리로벌린다()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var pcs = boot.Pieces;
            if (pcs.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저 — 말 확인 건너뜀(03 §7)");
            int home = boot.HomeCells[0];
            var look = boot.Looks[home];
            pcs.Stacking = false;   // 칸 안 6자리로 모두 세운 모습(M4-2 기준) — 실제 판은 하나만 세우고 묶는다(결정 66 ①, StackFlowTests)
            pcs.Arrange(Enumerable.Repeat(home, 6).ToList());
            var src = pcs.Pieces.Select(p => PieceShot.LoadSource(p.TextureKey, out _, out _)).ToList();
            var rig = Camera.main.GetComponent<BoardCameraRig>();
            var cam = rig.GetComponent<Camera>();
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            // 말끼리 가림만 본다(판·피규어·아이콘 숨김). 판에 가린 모습은 BoardShots pieces6-*.png
            boot.View.gameObject.SetActive(false);
            boot.Figures.gameObject.SetActive(false);
            boot.Icons.gameObject.SetActive(false);
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    var p = pcs.Pieces[i];
                    Assert.AreEqual(home, p.Spot.Cell);
                    Assert.AreEqual(i, p.Spot.Slot, "6명 → 자리 0~5");
                    FigureLayout.SlotOffset(i, out decimal dx, out decimal dz);
                    var pos = p.Root.transform.position;
                    Assert.AreEqual((float)(look.X + dx), pos.x, 1e-4f); Assert.AreEqual((float)(look.Z + dz), pos.z, 1e-4f);
                    Assert.AreEqual((float)BoardLayout.TopY(boot.Board[home]), pos.y, 1e-4f, "칸 윗면");
                }
                foreach (BoardCameraRig.Zoom zoom in System.Enum.GetValues(typeof(BoardCameraRig.Zoom)))
                {
                    rig.Set(zoom, 0, zoom == BoardCameraRig.Zoom.Full ? Vector3.zero : new Vector3((float)look.X, (float)BoardLayout.TopY(boot.Board[home]), (float)look.Z));
                    OffscreenShot.Render(cam, rt, tex);
                    OffscreenShot.Render(cam, rt, tex);
                    var shot = tex.GetPixels32();
                    cam.targetTexture = rt;
                    int zk = BoardPieces.ZoomK(zoom);
                    var feet = new HashSet<Vector2Int>();
                    int front = -1;
                    float frontDepth = float.MaxValue;
                    var visible = new float[6];
                    var bad = new int[6];
                    for (int i = 0; i < 6; i++)
                    {
                        var p = pcs.Pieces[i];
                        var rect = BoardPieces.BodyRect(p, cam, zk, out int k, out _);
                        var sh = BoardPieces.ShapeAt(p, zk);
                        feet.Add(new Vector2Int(rect.x + sh.Foot.x * k, rect.y + (sh.Height - 1 - sh.Foot.y) * k));
                        PieceShot.Compare(shot, W, H, BoardPieces.Cut(p, src[i], zk), sh.Width, sh.Height, rect, k, out int n, out bad[i], out _, out _);
                        visible[i] = n == 0 ? 0f : 1f - (float)bad[i] / n;
                        // 셰이더 깊이와 같이: 발 점에서 카메라 쪽으로 좌석별 당김(BiasFor)만큼 옮긴 점의 시야 깊이(같은 줄 자리는 번호가 작은 좌석이 앞)
                        var foot = p.Root.transform.position;
                        var near = foot + (cam.transform.position - foot).normalized * BoardPieces.BiasFor(p.Seat);
                        float depth = Vector3.Dot(near - cam.transform.position, cam.transform.forward);
                        if (depth < frontDepth) { frontDepth = depth; front = i; }
                    }
                    cam.targetTexture = null;
                    string vis = string.Join(" ", visible.Select((v, i) => $"{i + 1}:{v:P0}"));
                    Assert.AreEqual(6, feet.Count, zoom + " 발 픽셀이 6곳으로 갈린다");
                    Assert.AreEqual(0, bad[front], $"{zoom} 맨 앞 말(좌석 {front + 1})은 가림 없이 원본 k배 — 보이는 비율 {vis}");
                    if (zoom != BoardCameraRig.Zoom.Full)
                        for (int i = 0; i < 6; i++) Assert.Greater(visible[i], 0f, $"{zoom} 좌석 {i + 1}이 조금이라도 보인다 — {vis}");
                }
            }
            finally
            {
                cam.targetTexture = null;
                boot.View.gameObject.SetActive(true);
                boot.Figures.gameObject.SetActive(true);
                boot.Icons.gameObject.SetActive(true);
                pcs.Stacking = true;
                pcs.Arrange(boot.HomeCells);
                Object.Destroy(rt);
                Object.Destroy(tex);
            }
            yield return null;
        }

        /// <summary>
        /// 말 그림은 세운 스탠디처럼 위로 갈수록 카메라에 가까운 깊이를 쓴다(M4-2 검토 반영, d3-3 §2-3). 전에는 그림 전체가 발 + 0.5 한 깊이라
        /// 바로 뒤가 2층 넘게 높은 칸에서 머리·상체가 뒤 칸에 가렸다(0,-2 방향 0 지역 줌 몸의 37% 남짓). 판만 켜고(피규어·아이콘·다른 말 숨김) 말 하나를 세워
        /// 몸이 보이는 비율 ≥ 0.95를 본다 — 고른 자리는 바로 앞 칸이 말을 가리지 않는다(광선 모형으로 실제 가림 0).
        /// </summary>
        [UnityTest]
        public IEnumerator M4_말몸은뒤의높은칸에가리지않는다()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var pcs = boot.Pieces;
            if (pcs.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저 — 말 확인 건너뜀(03 §7)");
            // (칸, 줌, 방향): 천관산 정상 바로 앞(L3, 뒤 L6) 지역·전체, 서쪽 L2(뒤 L5) 방향 60°, 북쪽 L2 방향 180°
            var cases = new[]
            {
                (new Sinnoh.Rules.HexCoord(0, -2), BoardCameraRig.Zoom.Region, 0),
                (new Sinnoh.Rules.HexCoord(0, -2), BoardCameraRig.Zoom.Full, 0),
                (new Sinnoh.Rules.HexCoord(-2, 0), BoardCameraRig.Zoom.Region, 1),
                (new Sinnoh.Rules.HexCoord(-1, 3), BoardCameraRig.Zoom.Region, 3),
            };
            var p = pcs.Pieces[0];
            var src = PieceShot.LoadSource(p.TextureKey, out _, out _);
            var rig = Camera.main.GetComponent<BoardCameraRig>();
            var cam = rig.GetComponent<Camera>();
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            boot.Figures.gameObject.SetActive(false);
            boot.Icons.gameObject.SetActive(false);
            var report = new List<string>();
            try
            {
                foreach (var (coord, zoom, yaw) in cases)
                {
                    Assert.IsTrue(boot.Board.TryGetIndex(coord, out int cell), coord + " 칸");
                    pcs.Arrange(new[] { cell, -1, -1, -1, -1, -1 });
                    Assert.AreEqual(cell, p.Spot.Cell);
                    rig.Set(zoom, yaw, p.Root.transform.position);
                    OffscreenShot.Render(cam, rt, tex);
                    OffscreenShot.Render(cam, rt, tex);
                    var shot = tex.GetPixels32();
                    cam.targetTexture = rt;
                    int zk = BoardPieces.ZoomK(zoom);
                    var rect = BoardPieces.BodyRect(p, cam, zk, out int k, out _);
                    cam.targetTexture = null;
                    var sh = BoardPieces.ShapeAt(p, zk);
                    PieceShot.Compare(shot, W, H, BoardPieces.Cut(p, src, zk), sh.Width, sh.Height, rect, k, out int n, out int hidden, out _, out int off);
                    float visible = n == 0 ? 0f : 1f - (float)hidden / n;
                    string at = $"{coord} {zoom} 방향 {yaw * 60}°";
                    report.Add($"{at} {visible:F3}");
                    Assert.AreEqual(0, off, at + " 화면 안");
                    Assert.Greater(n, 0, at);
                    Assert.GreaterOrEqual(visible, 0.95f, $"{at}: 몸이 보이는 비율 {visible:F3}(가린 픽셀 {hidden}/{n}) — 뒤 칸이 말을 덮는다");
                }
            }
            finally
            {
                cam.targetTexture = null;
                boot.Figures.gameObject.SetActive(true);
                boot.Icons.gameObject.SetActive(true);
                pcs.Arrange(boot.HomeCells);
                Object.Destroy(rt);
                Object.Destroy(tex);
            }
            Debug.Log("[M4_말몸은뒤의높은칸에가리지않는다] " + string.Join("; ", report));
            yield return null;
        }
    }
}
