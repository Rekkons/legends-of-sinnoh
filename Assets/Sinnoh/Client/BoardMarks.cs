using System.Collections.Generic;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 이동 표시(04 §3-4 S19, §8-8 10행): 갈 수 있는 칸 · 마우스를 올린 칸까지의 경로 · 그 칸을 칸 윗면 위에 눕힌 육각형으로 칠한다.
    /// 어느 칸을 칠할지는 화면(<see cref="BoardHud"/>)이 엔진의 합법 답으로 정한다 — 여기는 그리기만 한다.
    /// 고정물 아이콘과 같은 셰이더(빛 없음 · 알파 섞기 · 깊이는 견주기만)에 흰 그림 + 색을 쓴다. 물 칸(투명 대기열) 위에도 보이게 그 뒤에 그린다.
    /// 메시 하나(부분 여덟: 갈 수 있는 칸 / 경로 / 마우스를 올린 칸 / 고른 칸의 테두리 / 경유 칸의 테두리 / 길 위 숨겨진 장소 표지 / 흐린 칸 / 고른 길 칸의 파란 테두리)를 바뀔 때마다 다시 짠다(많아야 수십 칸).
    /// </summary>
    public sealed class BoardMarks : MonoBehaviour
    {
        /// <summary>칸 윗면 위로 띄우는 높이 — 고정물 아이콘(0.02)보다 위 [가정].</summary>
        public const float Lift = 0.03f;
        /// <summary>칠하는 육각형의 크기(칸 외접 반지름에 대한 비율) — 칸 사이에 틈이 보이게 [가정].</summary>
        public const float Inset = 0.88f;
        /// <summary>그리기 순서: 물 칸·고정물 아이콘(3000) 뒤, 말(3010) 앞.</summary>
        public const int Queue = 3001;

        // 색 [가정 — 목업 r3 board의 밝은 칸]: 갈 수 있는 칸은 옅은 흰색, 경로는 더 짙게, 고른 칸은 금색
        public static readonly Color ReachColor = new Color(1f, 1f, 1f, 0.24f);
        public static readonly Color PathColor = new Color(1f, 1f, 1f, 0.55f);
        public static readonly Color TargetColor = new Color(0.973f, 0.816f, 0.251f, 0.8f);
        /// <summary>
        /// 고른 칸(걷기 전 확인 — 결정 70 ⑱)의 테두리: [이동]을 누를 때까지 남으므로 금색 칠 대신 금색 테두리 + 갈 수 있는 칸과 같은 옅은 칠로 그린다 —
        /// 그 칸의 토큰(숨겨진 장소 「?」 등)이 가려지지 않게. 테두리 안쪽 반지름 = 칠하는 육각형의 <see cref="RingInner"/>배 [가정].
        /// </summary>
        public static readonly Color RingColor = new Color(0.973f, 0.816f, 0.251f, 1f);
        public const float RingInner = 0.8f;

        /// <summary>
        /// 결정 71 ② 경유 칸(고른 길이 거쳐 가는 칸)의 테두리 — 고른 칸(금색)과 가르도록 흰색 [가정 꼴]. 자리·굵기는 고른 칸 테두리와 같다.
        /// </summary>
        public static readonly Color ViaColor = new Color(1f, 1f, 1f, 0.95f);

        /// <summary>
        /// 결정 71 ① 길 위의 뒷면 숨겨진 장소(걷다가 멈춰 공개할지 묻는 칸)의 표지: 테두리 안쪽에 하늘색 고리 [가정 꼴] — 고른 칸·경유 칸 테두리와 겹쳐도 보인다.
        /// 고리 = 칠하는 육각형의 <see cref="SpotOuter"/>배 ~ <see cref="SpotInner"/>배.
        /// </summary>
        public static readonly Color SpotColor = new Color(0.39f, 0.85f, 1f, 1f);
        public const float SpotOuter = 0.72f, SpotInner = 0.54f;

        /// <summary>T18 ① 공중날기 목적지를 보드에서 고르는 동안 갈 수 없는 마을 · 야영지를 흐리게 덮는 칠 [가정 #675].</summary>
        public static readonly Color DimColor = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>
        /// 결정 168 고른 길(걷기 전 확인 — 붙든 칸까지)의 칸마다 파란 육각 테두리: 출발 칸 · 고른 칸(금색) · 경유 칸(흰색)을 뺀 사이 칸. 자리 · 굵기는 고른 칸 테두리와 같다 [가정 꼴 — 색은 캡처로 맞춤].
        /// 길 위 숨겨진 장소의 하늘색 고리(안쪽)와 가르도록 짙은 파랑.
        /// </summary>
        public static readonly Color TrailColor = new Color(0.22f, 0.45f, 1f, 1f);

        readonly List<int> reach = new List<int>(), path = new List<int>(), vias = new List<int>(), stops = new List<int>(), dims = new List<int>(), trail = new List<int>();
        readonly Material[] materials = new Material[8];
        Board board;
        IReadOnlyList<CellLook> looks;
        Mesh mesh;
        MeshRenderer meshRenderer;

        /// <summary>지금 칠한 갈 수 있는 칸.</summary>
        public IReadOnlyList<int> Reach => reach;
        /// <summary>지금 칠한 경로(출발 칸 뺌, 고른 칸 뺌).</summary>
        public IReadOnlyList<int> Path => path;
        /// <summary>지금 고른 칸(없으면 −1).</summary>
        public int Target { get; private set; } = -1;
        /// <summary>지금 흰 테두리로 그린 경유 칸(결정 71 ②).</summary>
        public IReadOnlyList<int> Vias => vias;
        /// <summary>지금 표지를 단 길 위의 뒷면 숨겨진 장소 칸(결정 71 ①).</summary>
        public IReadOnlyList<int> Stops => stops;
        /// <summary>지금 흐리게 덮은 칸(T18 ① 갈 수 없는 마을 · 야영지).</summary>
        public IReadOnlyList<int> Dims => dims;
        /// <summary>지금 파란 테두리로 그린 고른 길의 칸(결정 168 — 붙든 칸이 있을 때만).</summary>
        public IReadOnlyList<int> Trail => trail;
        /// <summary>고른 칸을 테두리로 그렸나(붙든 칸 — 걷기 전 확인). 마우스를 올린 칸이면 false(금색 칠).</summary>
        public bool Held { get; private set; }
        public bool Visible => meshRenderer != null && meshRenderer.enabled;

        public void Init(Board board, IReadOnlyList<CellLook> looks, Shader shader)
        {
            this.board = board;
            this.looks = looks;
            mesh = new Mesh { name = "Marks", hideFlags = HideFlags.DontSave };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            var colors = new[] { ReachColor, PathColor, TargetColor, RingColor, ViaColor, SpotColor, DimColor, TrailColor };
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = new Material(shader) { name = "Marks " + i, hideFlags = HideFlags.DontSave, renderQueue = Queue };
                materials[i].SetTexture("_BaseMap", Texture2D.whiteTexture);
                materials[i].SetColor("_BaseColor", colors[i]);
            }
            meshRenderer.sharedMaterials = materials;
            meshRenderer.enabled = false;
        }

        /// <summary>
        /// 갈 수 있는 칸을 칠한다. walked = 고른 칸까지의 경로(출발 칸부터 — 없으면 null), target = 고른 칸(없으면 −1),
        /// held = 그 칸을 눌러 붙들었다(걷기 전 확인): 금색 칠 대신 테두리로 그린다. via = 경유 칸(흰 테두리, 결정 71 ②), spots = 길 위에서 멈춰 물을 숨겨진 장소 칸(하늘색 고리, 결정 71 ①).
        /// 갈 수 있는 칸이 비어도 길이 있으면 그린다(숨겨진 장소 물음 동안의 남은 길). dim = 흐리게 덮을 칸(T18 ① — 갈 수 없는 마을).
        /// </summary>
        public void Show(IReadOnlyList<int> reachable, IReadOnlyList<int> walked, int target, bool held = false, IReadOnlyList<int> via = null, IReadOnlyList<int> spots = null, IReadOnlyList<int> dim = null)
        {
            if (mesh == null) return;
            held = held && target >= 0;
            bool same = target == Target && held == Held && reachable.Count == reach.Count && (walked == null ? 0 : Mathf.Max(0, walked.Count - 2)) == path.Count && meshRenderer.enabled
                && Same(vias, via) && Same(stops, spots) && Same(dims, dim);
            for (int i = 0; same && i < reach.Count; i++) same = reach[i] == reachable[i];
            for (int i = 0; same && i < path.Count; i++) same = path[i] == walked[i + 1];
            if (same) return;
            reach.Clear();
            reach.AddRange(reachable);
            path.Clear();
            if (walked != null)
                for (int i = 1; i < walked.Count - 1; i++) path.Add(walked[i]);
            vias.Clear();
            if (via != null) vias.AddRange(via);
            stops.Clear();
            if (spots != null) stops.AddRange(spots);
            dims.Clear();
            if (dim != null) dims.AddRange(dim);
            Target = target;
            Held = held;
            trail.Clear();
            if (held)
                foreach (int c in path)
                    if (c != target && !vias.Contains(c)) trail.Add(c);
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            foreach (int c in reach)
                if (c != target && !path.Contains(c)) Hex(v, uv, tris[0], c);
            foreach (int c in path) Hex(v, uv, tris[1], c);
            if (target >= 0 && held)
            {
                Hex(v, uv, tris[0], target);
                Ring(v, uv, tris[3], target, 1f, RingInner);
            }
            else if (target >= 0) Hex(v, uv, tris[2], target);
            foreach (int c in vias)
                if (c != target) Ring(v, uv, tris[4], c, 1f, RingInner);
            foreach (int c in trail) Ring(v, uv, tris[7], c, 1f, RingInner);
            foreach (int c in stops) Ring(v, uv, tris[5], c, SpotOuter, SpotInner);
            foreach (int c in dims) Hex(v, uv, tris[6], c);
            mesh.Clear();
            mesh.subMeshCount = tris.Length;
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            for (int i = 0; i < tris.Length; i++) mesh.SetTriangles(tris[i], i, false);
            mesh.RecalculateBounds();
            meshRenderer.enabled = v.Count > 0;
        }

        static bool Same(List<int> have, IReadOnlyList<int> want)
        {
            int n = want != null ? want.Count : 0;
            if (have.Count != n) return false;
            for (int i = 0; i < n; i++)
                if (have[i] != want[i]) return false;
            return true;
        }

        public void Hide()
        {
            if (meshRenderer == null || !meshRenderer.enabled && reach.Count == 0) return;
            reach.Clear();
            path.Clear();
            vias.Clear();
            stops.Clear();
            dims.Clear();
            trail.Clear();
            Target = -1;
            Held = false;
            mesh.Clear();
            meshRenderer.enabled = false;
        }

        // flat-top 육각형(꼭짓점이 ±x), 위에서 보아 시계 방향 — 윗면이 보이는 쪽
        void Hex(List<Vector3> v, List<Vector2> uv, List<int> tris, int cell)
        {
            float r = (float)BoardLayout.HexRadius * Inset;
            var center = new Vector3((float)looks[cell].X, (float)BoardLayout.TopY(board[cell]) + Lift, (float)looks[cell].Z);
            int b = v.Count;
            v.Add(center);
            uv.Add(new Vector2(0.5f, 0.5f));
            for (int k = 0; k < 6; k++)
            {
                float a = -k * Mathf.PI / 3f;
                v.Add(center + new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a)));
                uv.Add(new Vector2(0.5f, 0.5f));
            }
            for (int k = 0; k < 6; k++)
            {
                tris.Add(b);
                tris.Add(b + 1 + k);
                tris.Add(b + 1 + (k + 1) % 6);
            }
        }

        // 육각형 테두리(바깥 = 칠하는 육각형의 outer배, 안 = inner배) — Hex와 같은 감는 방향
        void Ring(List<Vector3> v, List<Vector2> uv, List<int> tris, int cell, float outer, float inner)
        {
            float r = (float)BoardLayout.HexRadius * Inset;
            var center = new Vector3((float)looks[cell].X, (float)BoardLayout.TopY(board[cell]) + Lift, (float)looks[cell].Z);
            int b = v.Count;
            for (int k = 0; k < 6; k++)
            {
                float a = -k * Mathf.PI / 3f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(center + dir * (r * outer));
                v.Add(center + dir * (r * inner));
                uv.Add(new Vector2(0.5f, 0.5f));
                uv.Add(new Vector2(0.5f, 0.5f));
            }
            for (int k = 0; k < 6; k++)
            {
                int o0 = b + 2 * k, i0 = o0 + 1, o1 = b + 2 * ((k + 1) % 6), i1 = o1 + 1;
                tris.Add(i0);
                tris.Add(o0);
                tris.Add(o1);
                tris.Add(i0);
                tris.Add(o1);
                tris.Add(i1);
            }
        }

        void OnDestroy() => Clear();

        public void Clear()
        {
            foreach (var m in materials) if (m != null) DestroyNow(m);
            if (mesh != null) DestroyNow(mesh);
            mesh = null;
            reach.Clear();
            path.Clear();
            vias.Clear();
            stops.Clear();
            dims.Clear();
            trail.Clear();
            Target = -1;
            Held = false;
        }

        static void DestroyNow(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }

    /// <summary>
    /// 화면 좌표 ↔ 칸(04 §3-4 S19): 카메라의 자리·방향·세로 시야와 그림 크기만으로 광선을 만들어 <see cref="HexPick"/>에 넘긴다.
    /// 카메라가 지금 무엇에 그리고 있는지(창 · RT)에 기대지 않는다 — 창 없는 캡처·시험도 같은 값이 나온다. 좌표는 물리 px, 왼쪽 아래가 (0, 0).
    /// </summary>
    public static class BoardPick
    {
        /// <summary>그 점 아래의 칸(없으면 −1). 깨어진 세계 칸은 보일 때만 고른다.</summary>
        public static int Cell(BoardBoot board, Camera cam, float x, float y, int width, int height)
        {
            if (board == null || cam == null || width <= 0 || height <= 0) return HexPick.NoCell;
            var dir = Ray(cam, x, y, width, height);
            var o = cam.transform.position;
            return HexPick.Cell(Prisms(board), (double)BoardLayout.HexRadius, o.x, o.y, o.z, dir.x, dir.y, dir.z, out _);
        }

        /// <summary>그 점을 지나는 광선의 방향(카메라 자리에서 — 길이는 맞추지 않는다).</summary>
        public static Vector3 Ray(Camera cam, float x, float y, int width, int height)
        {
            var t = cam.transform;
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return t.forward + t.right * ((2f * x / width - 1f) * tan * width / height) + t.up * ((2f * y / height - 1f) * tan);
        }

        /// <summary>
        /// 그 점 아래의 땅(끌어서 옮길 때 잡는 점, 결정 66 ②): 칸에 닿으면 닿은 곳, 아니면 높이 fallbackY인 수평면. 어디에도 닿지 않으면 false.
        /// </summary>
        public static bool Ground(BoardBoot board, Camera cam, float x, float y, int width, int height, float fallbackY, out Vector3 point)
        {
            point = default;
            if (board == null || cam == null || width <= 0 || height <= 0) return false;
            var dir = Ray(cam, x, y, width, height);
            var o = cam.transform.position;
            if (HexPick.Cell(Prisms(board), (double)BoardLayout.HexRadius, o.x, o.y, o.z, dir.x, dir.y, dir.z, out double d) >= 0)
            {
                point = o + dir * (float)d;
                return true;
            }
            return GroundAt(cam, x, y, width, height, fallbackY, out point);
        }

        /// <summary>그 점을 지나는 광선이 높이 planeY인 수평면과 만나는 점(<see cref="BoardDrag.Ground"/>).</summary>
        public static bool GroundAt(Camera cam, float x, float y, int width, int height, float planeY, out Vector3 point)
        {
            var dir = Ray(cam, x, y, width, height);
            var o = cam.transform.position;
            bool hit = BoardDrag.Ground(o.x, o.y, o.z, dir.x, dir.y, dir.z, planeY, out double gx, out double gz);
            point = new Vector3((float)gx, planeY, (float)gz);
            return hit;
        }

        static BoardBoot prismsOf;
        static Board prismsBoard;
        static HexPrism[] prisms;

        /// <summary>보드의 칸 프리즘(밑면 ~ 윗면 — <see cref="BoardLayout.Bands"/>와 같은 모양). 보드가 바뀌면 다시 만든다. 깨어진 세계 칸은 보일 때만 고른다.</summary>
        public static IReadOnlyList<HexPrism> Prisms(BoardBoot board)
        {
            if (prisms == null || !ReferenceEquals(prismsOf, board) || !ReferenceEquals(prismsBoard, board.Board))
            {
                prismsOf = board;
                prismsBoard = board.Board;
                prisms = new HexPrism[board.Board.Count];
                for (int c = 0; c < prisms.Length; c++)
                {
                    var bands = BoardLayout.Bands(board.Board[c], board.Looks[c]);
                    prisms[c] = new HexPrism { X = (double)board.Looks[c].X, Z = (double)board.Looks[c].Z, Bottom = (double)bands[0].Bottom, Top = (double)bands[bands.Length - 1].Top };
                }
            }
            bool distortion = board.DistortionWorldVisible;
            for (int c = 0; c < prisms.Length; c++) prisms[c].Hidden = board.Board[c].DistortionWorld && !distortion;
            return prisms;
        }

        // 도메인 리로드를 끈 Play에서 정적 캐시가 앞 판의 보드를 쥐고 있지 않게 비운다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            prismsOf = null;
            prismsBoard = null;
            prisms = null;
        }

        /// <summary>칸 윗면 가운데가 그려지는 점(같은 셈의 거꾸로). 카메라 뒤면 false.</summary>
        public static bool Screen(BoardBoot board, Camera cam, int cell, int width, int height, out Vector2 px) => ScreenAt(cam, board.CellTop(cell), width, height, out px);

        /// <summary>월드의 한 점이 그려지는 점(<see cref="Screen"/>과 같은 셈). 카메라 뒤면 false.</summary>
        public static bool ScreenAt(Camera cam, Vector3 world, int width, int height, out Vector2 px)
        {
            var t = cam.transform;
            var v = world - t.position;
            float z = Vector3.Dot(v, t.forward);
            px = default;
            if (z <= 0f) return false;
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            px = new Vector2((Vector3.Dot(v, t.right) / (z * tan * width / height) + 1f) * 0.5f * width, (Vector3.Dot(v, t.up) / (z * tan) + 1f) * 0.5f * height);
            return true;
        }
    }
}
