using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Sinnoh.Editor
{
    // 보드 캡처(M4-2b, d3-3 §2-1·§4-1): 보드 씬을 열고(플레이 없이) 칸 메시를 만들어 1920×1080 RenderTexture로 찍는다. 창을 띄우지 않는다.
    //   메뉴 Sinnoh/Capture/Board, 배치: Unity.exe -batchmode -projectPath . -executeMethod Sinnoh.Editor.BoardShots.Run -logFile <log>
    //   (-nographics 없이 — 그래픽 장치가 있어야 그린다. 배치는 끝나면 종료 코드 0/1로 나간다)
    // 출력 Logs/BoardShots/(git 밖, Temp/는 배치가 끝나면 지워진다):
    //   <full|region|close>-yaw<각>.png 18장(줌 3 × 방향 6, 지역·근접은 천관산 정상 칸 윗면을 본다),
    //   full-yaw0-alpha.png·region-yaw0-alpha.png(바다·호수 틴트 알파 + 판 밑 바닥판 — 기본 그림과 견줌),
    //   full-yaw0-ub.png·distortion-yaw0.png(깨어진 세계를 켬), board-shots.json(그림마다 분홍·채움, 줌별 칸 폭, 방향별 가린 칸, 메시 수).
    //   M4-2c(d3-3 §2-2): 위 그림 모두에 피규어 33(Idle 0초 자세)·고정물 아이콘 173이 함께 선다(깨어진 세계 것은 칸과 함께 숨음).
    //   figure-<dex>_<state>.png 33장(근접 줌·방향 0, 피규어 발밑을 봄, 깨어진 세계 켬) + board-shots.json figures(맞춘 크기·넘침·이웃 칸 가림)·figureOccluded·icons.
    //   M4-2d(d3-3 §2-3): 위 그림 모두에 말 6(고향 마을 6칸, 고리·핀)이 함께 선다. pieces-<줌>.png 3장(고향 6칸, 지역·근접은 장막시티 = 택시 피규어 칸),
    //   pieces6-<줌>.png 3장(6명이 운하시티 한 칸) + board-shots.json pieces(줌별 k·말 사각형·몸 높이 ÷ 칸 폭·원본 k배와 MAE·보이는 비율).
    //   M4-2 검토 반영: pieces-summit-region.png(천관산 정상 바로 앞 `0,-2`에 말 하나, 피규어 숨김) + pieces.summitFront(몸이 보이는 비율 ≥ 0.95 — 뒤의 높은 칸이 덮지 않는다).
    // 판정(로그 [BoardShots] 줄): 모든 그림 분홍 0, 전체 줌 채움 ≥ 15%, 칸 폭 51/130/245 px ± 5%, 피규어 33·아이콘 173, 맞춘 크기 0.75~1.8,
    //   말 6, 줌별 k(직업 그림 1·1·2 — 전체 줌은 얼굴 조각, 주인공 도트 1·3·5), 말만 그린 그림에서 화면 안 말 몸·핀이 원본 k배와 MAE 0.
    public static class BoardShots
    {
        public const string ScenePath = "Assets/Sinnoh/Scenes/Board.unity";
        public const string OutDir = "Logs/BoardShots";
        const int W = 1920, H = 1080;
        // 전체 줌 채움 문턱(빈 화면·카메라 오류 거르기). d3-3 §2-1 (c)의 25%는 어림이었다 — 판 모양이 울퉁불퉁해 잰 값 21.7~23.8% → 15% [가정 M4-2b]
        public const float MinFullCoverage = 0.15f;
        // 바다·호수 반투명 비교 그림의 판 밑 바닥판 색 [가정 M4-2b: 원작 탁자 그림(TableURL)은 받지 않았다 — 가운데 회색]
        static readonly Color32 PlateColor = new Color32(128, 128, 128, 255);

        [MenuItem("Sinnoh/Capture/Board")]
        static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Capture();
        }

        public static void Run()
        {
            bool ok = false;
            try { ok = Capture(); }
            catch (Exception e) { Debug.LogException(e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Capture()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogError("[BoardShots] 그래픽 장치 없음(-nographics): 찍지 않는다");
                return false;
            }
            Directory.CreateDirectory(OutDir);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var boot = Object.FindAnyObjectByType<BoardBoot>();
            var cam = Camera.main;
            var rig = cam != null ? cam.GetComponent<BoardCameraRig>() : null;
            if (boot == null || rig == null) { Debug.LogError("[BoardShots] 씬에 BoardBoot·BoardCameraRig가 없다"); return false; }
            boot.Build();
            // 자세를 바꿔 다시 그릴 때 스키닝을 다시 계산한다(d3-1 §5-3 ⑨)
            foreach (var f in boot.Figures.Figures)
                foreach (var s in f.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.forceMatrixRecalculationPerRender = true;

            var made = new List<Object>();
            var json = new StringBuilder();
            var imgs = new StringBuilder(); // images 배열(피규어 근접 그림도 여기에 — 마지막에 앞에 붙인다)
            var fails = new List<string>();
            int images = 0;
            try
            {
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.DontSave }; made.Add(rt);
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave }; made.Add(tex);
                Color32 bg = cam.backgroundColor;

                boot.Board.TryGetIndex(new HexCoord(0, 0), out int summit);
                var summitTop = CellTop(boot, summit);

                void Shot(string name, BoardCameraRig.Zoom zoom, int yaw, Vector3 target)
                {
                    rig.Set(zoom, yaw, target);
                    OffscreenShot.Render(cam, rt, tex); // 예열: 첫 렌더는 재질 상수가 섞일 수 있다(d3-1 §5-3 ⑨)
                    OffscreenShot.Render(cam, rt, tex);
                    OffscreenShot.Stats(tex, bg, out int withIcons, out float coverage);
                    File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
                    // 분홍 정의 범위(r>200·g<60·b>200)는 고정물 아이콘 그림에도 있다(Sinnome 마을 토큰 8,013px [확인]) → 분홍이 나오면 아이콘을 숨기고
                    // 한 번 더 그려 칸·피규어만 센다(d3-3 §2-2 (c) 바꾼 까닭). 아이콘 재질은 Figures()가 셰이더로 본다
                    int pink = withIcons;
                    if (withIcons > 0 && boot.Icons != null)
                    {
                        boot.Icons.gameObject.SetActive(false);
                        OffscreenShot.Render(cam, rt, tex);
                        OffscreenShot.Stats(tex, bg, out pink, out _);
                        boot.Icons.gameObject.SetActive(true);
                    }
                    if (images++ > 0) imgs.Append(',');
                    imgs.Append("{\"name\":\"").Append(name).Append("\",\"pink\":").Append(pink).Append(",\"pinkWithIcons\":").Append(withIcons)
                        .Append(",\"coverage\":").Append(F(coverage)).Append('}');
                    if (pink > 0) fails.Add($"{name} 분홍 {pink}");
                    if (zoom == BoardCameraRig.Zoom.Full && coverage < MinFullCoverage) fails.Add($"{name} 채움 {coverage:P1} < {MinFullCoverage:P0}");
                }

                foreach (BoardCameraRig.Zoom zoom in Enum.GetValues(typeof(BoardCameraRig.Zoom)))
                    for (int yaw = 0; yaw < 6; yaw++)
                        Shot($"{ZoomName(zoom)}-yaw{yaw * 60}", zoom, yaw, zoom == BoardCameraRig.Zoom.Full ? Vector3.zero : summitTop);

                // 바다·호수 반투명 + 판 밑 바닥판(d3-3 §2-1: 알파 무시 그림과 견줘 고른다)
                boot.Build(translucentWater: true);
                var plate = Plate(made);
                Shot("full-yaw0-alpha", BoardCameraRig.Zoom.Full, 0, Vector3.zero);
                Shot("region-yaw0-alpha", BoardCameraRig.Zoom.Region, 0, summitTop);
                Object.DestroyImmediate(plate);
                boot.Build();

                // 깨어진 세계(울트라 버스트에서만 보임, 04 §3-1 [가정])
                boot.DistortionWorldVisible = true;
                Shot("full-yaw0-ub", BoardCameraRig.Zoom.Full, 0, Vector3.zero);
                Shot("distortion-yaw0", BoardCameraRig.Zoom.Region, 0, DistortionCenter(boot));
                boot.DistortionWorldVisible = false;

                // 줌별 화면 가운데 칸 폭(d3-3 §2-1 (d)): 바라보는 점에 놓은 헥스 가로 대각선(2.2)의 투영 길이
                json.Append(",\"cellPx\":{");
                foreach (BoardCameraRig.Zoom zoom in Enum.GetValues(typeof(BoardCameraRig.Zoom)))
                {
                    rig.Set(zoom, 0, zoom == BoardCameraRig.Zoom.Full ? Vector3.zero : summitTop);
                    float px = CellWidthPx(cam, rt, rig.Target);
                    float want = BoardCameraRig.CellPixels(zoom);
                    if (zoom != BoardCameraRig.Zoom.Full) json.Append(',');
                    json.Append('"').Append(ZoomName(zoom)).Append("\":").Append(F(px));
                    if (Mathf.Abs(px - want) > want * 0.05f) fails.Add($"{ZoomName(zoom)} 칸 폭 {px:F1}px, 기대 {want} ± 5%");
                }
                json.Append('}');

                // 가린 칸(기록, 문턱 아님 — d3-3 §2-1): 카메라 → 칸 윗면 가운데 광선이 다른 칸에 먼저 맞는 본맵 칸(화면 안에 있는 것만)
                json.Append(",\"occluded\":{");
                var col = boot.View.Filter(0).gameObject.AddComponent<MeshCollider>();
                col.sharedMesh = boot.View.Filter(0).sharedMesh;
                Physics.SyncTransforms();
                var zooms = new[] { BoardCameraRig.Zoom.Full, BoardCameraRig.Zoom.Region };
                for (int zi = 0; zi < zooms.Length; zi++)
                {
                    if (zi > 0) json.Append(',');
                    json.Append('"').Append(ZoomName(zooms[zi])).Append("\":[");
                    for (int yaw = 0; yaw < 6; yaw++)
                    {
                        rig.Set(zooms[zi], yaw, zooms[zi] == BoardCameraRig.Zoom.Full ? Vector3.zero : summitTop);
                        Occlusion(boot, col, cam, rt, out int seen, out int hidden);
                        if (yaw > 0) json.Append(',');
                        json.Append("{\"yaw\":").Append(yaw * 60).Append(",\"inView\":").Append(seen).Append(",\"occluded\":").Append(hidden).Append('}');
                    }
                    json.Append(']');
                }
                json.Append('}');

                Figures(boot, cam, rt, rig, col, json, fails, Shot);
                Pieces(boot, cam, rt, tex, rig, json, fails, Shot);
                Object.DestroyImmediate(col);

                // 메시(그리기 호출은 패스마다 서브메시 수)
                json.Append(",\"mesh\":[");
                for (int p = 0; p < BoardMeshBuilder.Parts; p++)
                {
                    var m = boot.View.Filter(p).sharedMesh;
                    int tris = 0;
                    for (int s = 0; s < m.subMeshCount; s++) tris += (int)m.GetIndexCount(s) / 3;
                    if (p > 0) json.Append(',');
                    json.Append("{\"name\":\"").Append(m.name).Append("\",\"vertices\":").Append(m.vertexCount)
                        .Append(",\"triangles\":").Append(tris).Append(",\"submeshes\":").Append(m.subMeshCount).Append('}');
                }
                json.Append("]");
                json.Append(",\"ok\":").Append(fails.Count == 0 ? "true" : "false").Append(",\"fails\":[");
                for (int i = 0; i < fails.Count; i++) json.Append(i > 0 ? "," : "").Append('"').Append(fails[i].Replace("\"", "'")).Append('"');
                json.Append("]}\n");
                File.WriteAllText(Path.Combine(OutDir, "board-shots.json"), "{\"images\":[" + imgs + "]" + json, new UTF8Encoding(false));
            }
            finally
            {
                foreach (var o in made) if (o) Object.DestroyImmediate(o);
                // 메뉴로 부른 뒤 열린 씬에 저장 안 하는 보드가 남지 않게(편집 모드에서는 OnDestroy가 불리지 않는다)
                boot.Teardown();
            }

            if (fails.Count == 0) Debug.Log($"[BoardShots] 통과: 그림 {images}장 분홍 0 → {OutDir}/board-shots.json");
            else Debug.LogError($"[BoardShots] 실패 {fails.Count}: {string.Join("; ", fails)}");
            return fails.Count == 0;
        }

        // 칸 안접원 지름(변 사이) = 2 × 1.1 × cos 30°
        const float InnerDiameter = 1.9053f;

        // 피규어(M4-2c, d3-3 §2-2): 33개 수·맞춘 크기(Idle 0초, 구운 스킨 경계) 0.75~1.8·figures.json과 2% 안, 근접 한 장씩(분홍 0),
        // 기록(문턱 아님 — 사용자 U14·「피규어 크기」·「가림」): 가로 경계가 칸 안접원을 넘는 양, 지역 줌·방향 0에서 이 피규어 경계 상자에 가려지는 다른 칸 수, 피규어가 칸에 가려지는 방향 수,
        // 전체·지역 줌 6방향에서 피규어에 가려지는 칸 수(칸 메시에 먼저 가린 것은 뺌), 아이콘 수·그리기 호출.
        static void Figures(BoardBoot boot, Camera cam, RenderTexture rt, BoardCameraRig rig, Collider board, StringBuilder json, List<string> fails,
            Action<string, BoardCameraRig.Zoom, int, Vector3> shot)
        {
            var figs = boot.Figures;
            if (figs.GenMissing || figs.Figures.Count != 33) fails.Add($"피규어 {figs.Figures.Count} ≠ 33(Gen 없음 {figs.GenMissing})");
            if (boot.Icons.GenMissing || boot.Icons.Icons.Count != 173) fails.Add($"고정물 아이콘 {boot.Icons.Icons.Count} ≠ 173(Gen 없음 {boot.Icons.GenMissing})");
            foreach (var mr in boot.Icons.Renderers)
                if (mr.sharedMaterial == null || mr.sharedMaterial.shader == null || mr.sharedMaterial.shader.name != "Sinnoh/BoardIcon" || !mr.sharedMaterial.shader.isSupported
                    || mr.sharedMaterial.GetTexture("_BaseMap") == null) fails.Add("아이콘 재질 " + mr.name);
            boot.DistortionWorldVisible = true;
            var bounds = new List<Bounds>();
            json.Append(",\"figures\":[");
            for (int i = 0; i < figs.Figures.Count; i++)
            {
                var f = figs.Figures[i];
                BoardFigures.Pose(f, 0f);
                var b = BoardFigures.BakedBounds(f.Root);
                bounds.Add(b);
                float fitted = Mathf.Max(b.size.x, b.size.y);
                float want = Mathf.Max(f.FittedSize.x, f.FittedSize.y);
                float over = Mathf.Max(b.size.x, b.size.z) - InnerDiameter;
                if (fitted < FigureImport.FitMin - 0.01f || fitted > FigureImport.FitMax + 0.01f) fails.Add($"{f.Key} 맞춘 크기 {fitted:F3} ∉ [{FigureImport.FitMin}, {FigureImport.FitMax}]");
                if (Mathf.Abs(fitted - want) > want * 0.02f) fails.Add($"{f.Key} 맞춘 크기 {fitted:F3} ≠ figures.json {want:F3} ± 2%");
                var pos = f.Root.transform.position;
                rig.Set(BoardCameraRig.Zoom.Region, 0, pos);
                int covers = CoveredBy(boot, cam, rt, b, f.Spot.Cell);
                // 이 피규어가 칸 메시에 가려지는 방향 수(지역 줌, 피규어를 봄, 경계 상자 가운데로 쏜 광선 — 본맵 칸만)
                int hiddenYaws = 0;
                for (int yaw = 0; yaw < 6; yaw++)
                {
                    rig.Set(BoardCameraRig.Zoom.Region, yaw, pos);
                    var o = cam.transform.position;
                    var d = b.center - o;
                    if (board.Raycast(new Ray(o, d.normalized), out _, d.magnitude - 0.01f)) hiddenYaws++;
                }
                shot("figure-" + f.Key.Replace(':', '_'), BoardCameraRig.Zoom.Close, 0, pos);
                if (i > 0) json.Append(',');
                json.Append("{\"key\":\"").Append(f.Key).Append("\",\"group\":\"").Append(f.Group)
                    .Append("\",\"cell\":\"").Append(boot.Board[f.Spot.Cell].Coord.ToString()).Append("\",\"slot\":").Append(f.Spot.Slot)
                    .Append(",\"fitScale\":").Append(F(f.FitScale)).Append(",\"fitted\":").Append(F(fitted))
                    .Append(",\"size\":[").Append(F(b.size.x)).Append(',').Append(F(b.size.y)).Append(',').Append(F(b.size.z))
                    .Append("],\"overInner\":").Append(F(Mathf.Max(0f, over))).Append(",\"coversRegion\":").Append(covers).Append(",\"hiddenYaws\":").Append(hiddenYaws).Append('}');
            }
            json.Append(']');
            json.Append(",\"offBoard\":[");
            for (int i = 0; i < figs.OffBoard.Count; i++) json.Append(i > 0 ? "," : "").Append('"').Append(figs.OffBoard[i]).Append('"');
            json.Append(']');

            // 피규어에 가려진 칸(전체 줌 판 가운데, 지역 줌 천관산 정상, 방향 6)
            boot.Board.TryGetIndex(new HexCoord(0, 0), out int summit);
            var summitTop = CellTop(boot, summit);
            json.Append(",\"figureOccluded\":{");
            var zooms = new[] { BoardCameraRig.Zoom.Full, BoardCameraRig.Zoom.Region };
            for (int zi = 0; zi < zooms.Length; zi++)
            {
                json.Append(zi > 0 ? "," : "").Append('"').Append(ZoomName(zooms[zi])).Append("\":[");
                for (int yaw = 0; yaw < 6; yaw++)
                {
                    rig.Set(zooms[zi], yaw, zooms[zi] == BoardCameraRig.Zoom.Full ? Vector3.zero : summitTop);
                    int hidden = 0;
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    var origin = cam.transform.position;
                    for (int c = 0; c < boot.Board.Count; c++)
                    {
                        if (boot.Board[c].DistortionWorld) continue;
                        var top = CellTop(boot, c) + Vector3.up * 0.001f;
                        var v = cam.WorldToViewportPoint(top);
                        if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue;
                        var d = top - origin;
                        var ray = new Ray(origin, d.normalized);
                        if (board.Raycast(ray, out _, d.magnitude - 0.01f)) continue;
                        for (int k = 0; k < bounds.Count; k++)
                            if (figs.Figures[k].Spot.Cell != c && bounds[k].IntersectRay(ray, out float t) && t < d.magnitude - 0.01f) { hidden++; break; }
                    }
                    cam.targetTexture = prev;
                    json.Append(yaw > 0 ? "," : "").Append(hidden);
                }
                json.Append(']');
            }
            json.Append('}');
            json.Append(",\"icons\":").Append(boot.Icons.Icons.Count).Append(",\"iconDraws\":").Append(boot.Icons.Renderers.Count);
            boot.DistortionWorldVisible = false;
        }

        // 말(M4-2d, d3-3 §2-3): A = 고향 마을 6칸(시험 판 처음 자리), B = 6명이 운하시티 한 칸. 줌마다 한 장씩 찍고(사람이 볼 그림),
        // 판·피규어·아이콘을 숨겨 말만 그린 그림으로 (b) 사각형(그 줌의 조각 — 직업 그림은 전체 줌 얼굴 조각 30k, 그 밖 80k)·정수 픽셀 (c) 원본 k배 최근접과 MAE 0을 센다(화면 안 말만). 핀도 PieceArt 그림 k배와 견준다.
        // 기록(문턱 아님, 사용자): 몸 높이 ÷ 칸 폭, 다 그린 그림에서 말 몸이 보이는 비율(칸·피규어·다른 말에 가림).
        static void Pieces(BoardBoot boot, Camera cam, RenderTexture rt, Texture2D tex, BoardCameraRig rig, StringBuilder json, List<string> fails,
            Action<string, BoardCameraRig.Zoom, int, Vector3> shot)
        {
            var pcs = boot.Pieces;
            if (pcs == null || pcs.GenMissing || pcs.Pieces.Count != 6) { fails.Add($"말 {(pcs == null ? 0 : pcs.Pieces.Count)} ≠ 6"); return; }
            var src = new List<Color32[]>();
            var pins = new List<Color32[]>();
            var heights = new List<int>();
            // 말마다 원본 그림(아래가 0행): 직업 그림은 그림 전체, 주인공 도트는 걷기 시트에서 지금 프레임의 한 칸
            bool Sources()
            {
                src.Clear();
                pins.Clear();
                heights.Clear();
                foreach (var p in pcs.Pieces)
                {
                    var px = PieceShot.LoadSource(p.TextureKey, out int w, out int h);
                    if (px == null || w != p.Width || h % p.Height != 0 || (p.Frame + 1) * p.Height > h) { fails.Add("말 원본 " + p.TextureKey); return false; }
                    if (h != p.Height)
                    {
                        var cell = new Color32[p.Width * p.Height];
                        Array.Copy(px, (h - (p.Frame + 1) * p.Height) * w, cell, 0, cell.Length);
                        px = cell;
                    }
                    src.Add(px);
                    PieceShot.Opaque(px, p.Width, p.Height, out int bh);
                    heights.Add(bh);
                    pins.Add(p.PinTexture.GetPixels32());
                }
                return true;
            }
            if (!Sources()) return;
            int home0 = boot.HomeCells[0], home1 = boot.HomeCells[1];
            json.Append(",\"pieces\":{\"seats\":[");
            for (int i = 0; i < pcs.Pieces.Count; i++)
            {
                var p = pcs.Pieces[i];
                json.Append(i > 0 ? "," : "").Append("{\"seat\":").Append(p.Seat + 1).Append(",\"trainer\":\"").Append(p.Trainer).Append("\",\"village\":\"").Append(p.Village)
                    .Append("\",\"color\":\"#").Append(ColorUtility.ToHtmlStringRGB(p.Color)).Append("\",\"home\":\"").Append(boot.Board[boot.HomeCells[i]].Coord.ToString())
                    .Append("\",\"foot\":[").Append(p.Foot.x).Append(',').Append(p.Foot.y).Append("],\"bodyHeight\":").Append(heights[i]).Append('}');
            }
            json.Append(']');

            void Layout(string prefix, int focus)
            {
                json.Append(",\"").Append(prefix).Append("\":[");
                bool first = true;
                foreach (BoardCameraRig.Zoom zoom in Enum.GetValues(typeof(BoardCameraRig.Zoom)))
                {
                    var target = zoom == BoardCameraRig.Zoom.Full ? Vector3.zero : CellTop(boot, focus);
                    string name = prefix + "-" + ZoomName(zoom);
                    shot(name, zoom, 0, target);
                    var full = tex.GetPixels32();
                    // 말만: 판·피규어·아이콘을 숨기고 다시 그린다(고리·핀·다른 말은 그대로)
                    boot.View.gameObject.SetActive(false);
                    boot.Figures.gameObject.SetActive(false);
                    boot.Icons.gameObject.SetActive(false);
                    OffscreenShot.Render(cam, rt, tex);
                    OffscreenShot.Render(cam, rt, tex);
                    var only = tex.GetPixels32();
                    boot.View.gameObject.SetActive(true);
                    boot.Figures.gameObject.SetActive(true);
                    boot.Icons.gameObject.SetActive(true);

                    int zk = BoardPieces.ZoomK(zoom);
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    json.Append(first ? "" : ",").Append("{\"zoom\":\"").Append(ZoomName(zoom)).Append("\",\"cellPx\":").Append(F(BoardCameraRig.CellPixels(zoom))).Append(",\"pieces\":[");
                    first = false;
                    for (int i = 0; i < pcs.Pieces.Count; i++)
                    {
                        var p = pcs.Pieces[i];
                        var rect = BoardPieces.BodyRect(p, cam, zk, out int k, out var raw);
                        var pin = BoardPieces.PinRect(p, cam, zk);
                        bool inView = rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= rt.width && rect.yMax <= rt.height;
                        var sh = BoardPieces.ShapeAt(p, zk);
                        var cut = BoardPieces.Cut(p, src[i], zk);
                        PieceShot.Opaque(cut, sh.Width, sh.Height, out int bodyHeight);
                        PieceShot.Compare(only, rt.width, rt.height, cut, sh.Width, sh.Height, rect, k, out int n, out int bad, out long diff, out _);
                        PieceShot.Compare(only, rt.width, rt.height, pins[i], PieceArt.PinSize, PieceArt.PinSize, pin, k, out int pn, out int pbad, out _, out _);
                        PieceShot.Compare(full, rt.width, rt.height, cut, sh.Width, sh.Height, rect, k, out int fn, out int hidden, out _, out _);
                        // B(한 칸 6명)는 서로 가리므로 말만 그림의 MAE도 기록만 한다. A는 화면 안 말마다 0이어야 한다
                        if (k != BoardPieces.KFor(p.Style, zk)) fails.Add($"{name} 좌석 {p.Seat + 1} k {k} ≠ {BoardPieces.KFor(p.Style, zk)}");
                        if (!prefix.StartsWith("pieces6", StringComparison.Ordinal) && inView && (bad > 0 || pbad > 0)) fails.Add($"{name} 좌석 {p.Seat + 1} 원본 k배와 다름: 몸 {bad}/{n}, 핀 {pbad}/{pn}");
                        if (zoom == BoardCameraRig.Zoom.Full && !inView) fails.Add($"{name} 좌석 {p.Seat + 1} 전체 줌 화면 밖");
                        json.Append(i > 0 ? "," : "").Append("{\"seat\":").Append(p.Seat + 1).Append(",\"slot\":").Append(p.Spot.Slot).Append(",\"k\":").Append(k)
                            .Append(",\"rect\":[").Append(rect.x).Append(',').Append(rect.y).Append(',').Append(rect.width).Append(',').Append(rect.height)
                            .Append("],\"footRaw\":[").Append(F(raw.x)).Append(',').Append(F(raw.y)).Append("],\"inView\":").Append(inView ? "true" : "false")
                            .Append(",\"pixels\":").Append(n).Append(",\"mismatched\":").Append(bad).Append(",\"mae\":").Append(F(n == 0 ? 0f : diff / (3f * n)))
                            .Append(",\"pinMismatched\":").Append(pbad).Append(",\"visible\":").Append(F(fn == 0 ? 0f : 1f - (float)hidden / fn))
                            .Append(",\"heightOverCell\":").Append(F(bodyHeight * k / BoardCameraRig.CellPixels(zoom))).Append(",\"face\":").Append(BoardPieces.FaceAt(p, zk) ? "true" : "false").Append('}');
                    }
                    cam.targetTexture = prev;
                    json.Append("]}");
                }
                json.Append(']');
            }

            Layout("pieces", home1);
            pcs.Stacking = false;   // 칸 안 6자리로 모두 세운 모습(M4-2 기준 그림) — 실제 판은 하나만 세우고 묶는다(결정 66 ①, UiShots board-stack-*)
            pcs.Arrange(new[] { home0, home0, home0, home0, home0, home0 });
            Layout("pieces6", home0);
            pcs.Stacking = true;

            // C(M4-2 검토 반영): 천관산 정상 바로 앞 `0,-2`(L3, 뒤 L6)에 좌석 1 말 하나, 지역 줌·방향 0 → pieces-summit-region.png.
            // 그림 깊이가 세운 평면이라 머리·상체가 뒤 칸에 가리지 않는다(전에는 몸의 약 37%가 가렸다). 이 칸의 악식킹(시험 판 UB)은 숨기고 찍는다
            if (boot.Board.TryGetIndex(new HexCoord(0, -2), out int summitFront))
            {
                pcs.Arrange(new[] { summitFront, -1, -1, -1, -1, -1 });
                boot.Figures.gameObject.SetActive(false);
                shot("pieces-summit-region", BoardCameraRig.Zoom.Region, 0, CellTop(boot, summitFront));
                boot.Figures.gameObject.SetActive(true);
                var full = tex.GetPixels32();
                var p0 = pcs.Pieces[0];
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                var rect = BoardPieces.BodyRect(p0, cam, BoardPieces.ZoomK(BoardCameraRig.Zoom.Region), out int k, out _);
                cam.targetTexture = prev;
                PieceShot.Compare(full, rt.width, rt.height, src[0], p0.Width, p0.Height, rect, k, out int fn, out int hidden, out _, out _);
                float visible = fn == 0 ? 0f : 1f - (float)hidden / fn;
                json.Append(",\"summitFront\":{\"cell\":\"0,-2\",\"zoom\":\"region\",\"yaw\":0,\"figuresHidden\":true,\"visible\":").Append(F(visible)).Append('}');
                if (visible < 0.95f) fails.Add($"pieces-summit-region 좌석 1 몸이 보이는 비율 {visible:F3} < 0.95(뒤 칸이 덮음)");
            }
            pcs.Arrange(boot.HomeCells);

            // 주인공 도트 양식(04 §3-1 양식 ①, 크기 안 B1 ×1·×3·×5): 같은 자리 둘을 한 번 더 — pieces-dot-<줌>.png · pieces6-dot-<줌>.png. 끝나면 직업 그림으로 되돌린다
            boot.PieceStyle = PieceStyle.Dot;
            if (pcs.GenMissing || pcs.Pieces.Count != 6) fails.Add($"주인공 도트 말 {pcs.Pieces.Count} ≠ 6");
            else if (Sources())
            {
                Layout("pieces-dot", home1);
                pcs.Stacking = false;
                pcs.Arrange(new[] { home0, home0, home0, home0, home0, home0 });
                Layout("pieces6-dot", home0);
                pcs.Stacking = true;
            }
            boot.PieceStyle = PieceStyle.Job;
            json.Append('}');
        }

        // 지금 카메라에서 칸 윗면 가운데로 쏜 광선이 경계 상자 b에 먼저 맞는 다른 본맵 칸 수(화면 안만)
        static int CoveredBy(BoardBoot boot, Camera cam, RenderTexture rt, Bounds b, int own)
        {
            int n = 0;
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            var origin = cam.transform.position;
            for (int c = 0; c < boot.Board.Count; c++)
            {
                if (c == own || boot.Board[c].DistortionWorld) continue;
                var top = CellTop(boot, c) + Vector3.up * 0.001f;
                var v = cam.WorldToViewportPoint(top);
                if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue;
                var d = top - origin;
                if (b.IntersectRay(new Ray(origin, d.normalized), out float t) && t < d.magnitude - 0.01f) n++;
            }
            cam.targetTexture = prev;
            return n;
        }

        static string ZoomName(BoardCameraRig.Zoom z) => z == BoardCameraRig.Zoom.Full ? "full" : z == BoardCameraRig.Zoom.Region ? "region" : "close";

        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        static Vector3 CellTop(BoardBoot boot, int cell)
        {
            var look = boot.Looks[cell];
            return new Vector3((float)look.X, (float)Sinnoh.Data.BoardLayout.TopY(boot.Board[cell]), (float)look.Z);
        }

        static Vector3 DistortionCenter(BoardBoot boot)
        {
            var sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < boot.Board.Count; i++)
                if (boot.Board[i].DistortionWorld) { sum += CellTop(boot, i); n++; }
            return sum / n;
        }

        static GameObject Plate(List<Object> made)
        {
            var go = new GameObject("BoardShots Plate") { hideFlags = HideFlags.DontSave };
            var mesh = new Mesh { name = "Plate", hideFlags = HideFlags.DontSave }; made.Add(mesh);
            const float S = 30f, Y = -0.05f;
            mesh.SetVertices(new List<Vector3> { new Vector3(-S, Y, -S), new Vector3(-S, Y, S), new Vector3(S, Y, S), new Vector3(S, Y, -S) });
            mesh.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetColors(new List<Color32> { PlateColor, PlateColor, PlateColor, PlateColor });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            // 옆면 재질(텍스처 없음, 불투명)을 빌려 쓴다
            var sides = Object.FindAnyObjectByType<BoardBoot>().View.Renderer(0).sharedMaterials;
            go.AddComponent<MeshRenderer>().sharedMaterial = Array.Find(sides, m => m.name == "Hex " + HexGroup.Sides);
            return go;
        }

        static float CellWidthPx(Camera cam, RenderTexture rt, Vector3 target)
        {
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            var right = cam.transform.right * (float)Sinnoh.Data.BoardLayout.HexRadius;
            float px = Vector3.Distance(cam.WorldToScreenPoint(target - right), cam.WorldToScreenPoint(target + right));
            cam.targetTexture = prev;
            return px;
        }

        static void Occlusion(BoardBoot boot, Collider board, Camera cam, RenderTexture rt, out int seen, out int hidden)
        {
            seen = hidden = 0;
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            var origin = cam.transform.position;
            for (int i = 0; i < boot.Board.Count; i++)
            {
                if (boot.Board[i].DistortionWorld) continue;
                var top = CellTop(boot, i) + Vector3.up * 0.001f;
                var v = cam.WorldToViewportPoint(top);
                if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) continue;
                seen++;
                var d = top - origin;
                if (board.Raycast(new Ray(origin, d.normalized), out _, d.magnitude - 0.01f)) hidden++; // 칸 메시만(피규어 받침 BoxCollider는 아래 figureOccluded)
            }
            cam.targetTexture = prev;
        }
    }
}
