using System.Collections.Generic;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// worker B 「토목 기사」가 놓은 길(<see cref="GameState.Roads"/> — 판이 끝날 때까지 남고 모든 트레이너의 걷기 값을 바꾼다)을 칸 윗면에 칠한다(감사 G5-1 —
    /// rulings A36 원작 커뮤니티 확인). 어느 칸인지는 화면 흐름(GameFlow)이 보기에서 준다 — 보기는 공개 값이라 남의 화면 · 방 판 손님에게도 같다.
    /// 새 길 칸은 <see cref="GrowMs"/> 동안 작게 시작해 커진다(놓을 때 짧은 연출). 메시 하나, 칸이 바뀔 때·자랄 때만 다시 짠다. 이동 표시(<see cref="BoardMarks"/>)보다 아래.
    /// </summary>
    public sealed class BoardRoads : MonoBehaviour
    {
        /// <summary>칸 윗면 위로 띄우는 높이 — 고정물 아이콘(0.02)보다 아래 [가정 #661].</summary>
        public const float Lift = 0.015f;
        /// <summary>칠하는 육각형의 크기(칸 외접 반지름에 대한 비율) — 이동 표시(0.88)보다 작게 [가정 #661].</summary>
        public const float Inset = 0.62f;
        /// <summary>그리기 순서: 물 칸(3000) 뒤 · 이동 표시(3001) 앞.</summary>
        public const int Queue = 3000;
        /// <summary>새 길 칸이 다 커지는 시간 [가정 #661].</summary>
        public const int GrowMs = 360;
        /// <summary>길 색(흙길 — 칸 색과 섞여 보이게 반쯤 비친다) [가정 #661].</summary>
        public static readonly Color RoadColor = new Color(0.86f, 0.70f, 0.46f, 0.85f);

        readonly List<int> cells = new List<int>();
        readonly Dictionary<int, int> age = new Dictionary<int, int>();
        Board board;
        IReadOnlyList<CellLook> looks;
        Mesh mesh;
        MeshRenderer meshRenderer;
        Material material;

        /// <summary>지금 칠한 길 칸(놓인 순서).</summary>
        public IReadOnlyList<int> Cells => cells;

        /// <summary>아직 자라는 길 칸이 있나.</summary>
        public bool Growing
        {
            get
            {
                foreach (var kv in age) if (kv.Value < GrowMs) return true;
                return false;
            }
        }

        public void Init(Board board, IReadOnlyList<CellLook> looks, Shader shader)
        {
            this.board = board;
            this.looks = looks;
            mesh = new Mesh { name = "Roads", hideFlags = HideFlags.DontSave };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            material = new Material(shader) { name = "Roads", hideFlags = HideFlags.DontSave, renderQueue = Queue };
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            material.SetColor("_BaseColor", RoadColor);
            meshRenderer.sharedMaterial = material;
            meshRenderer.enabled = false;
        }

        /// <summary>
        /// 길 칸을 맞춘다: roads = 보기의 길(없으면 비운다). grow = 새 칸을 더해도 되나(false면 새 칸은 기다린다 — 말이 걷는 연출 중) · dtMs = 지난 시간(자라는 칸).
        /// 판이 바뀌어 사라진 칸은 바로 지운다. 처음 보는 판(지금 칠한 것이 없다)의 길은 자라지 않고 바로 선다.
        /// </summary>
        public void Show(IReadOnlyList<int> roads, bool grow, int dtMs)
        {
            if (mesh == null) return;
            bool dirty = false;
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                if (roads != null && Contains(roads, cells[i])) continue;
                age.Remove(cells[i]);
                cells.RemoveAt(i);
                dirty = true;
            }
            bool fresh = cells.Count == 0;
            if (roads != null && grow)
                foreach (int c in roads)
                {
                    if (c < 0 || c >= board.Count || age.ContainsKey(c)) continue;
                    cells.Add(c);
                    age[c] = fresh ? GrowMs : 0;
                    dirty = true;
                }
            foreach (int c in cells)
            {
                if (age[c] >= GrowMs) continue;
                age[c] = Mathf.Min(GrowMs, age[c] + Mathf.Max(0, dtMs));
                dirty = true;
            }
            if (dirty) Build();
        }

        static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }

        void Build()
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            foreach (int c in cells)
            {
                float t = age[c] / (float)GrowMs;
                float scale = 1f - (1f - t) * (1f - t);   // 빨리 커지다 멈춘다
                if (scale > 0f) Hex(v, uv, tris, c, scale);
            }
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0, false);
            mesh.RecalculateBounds();
            meshRenderer.enabled = v.Count > 0;
        }

        // flat-top 육각형(꼭짓점이 ±x), 위에서 보아 시계 방향 — BoardMarks.Hex와 같은 꼴
        void Hex(List<Vector3> v, List<Vector2> uv, List<int> tris, int cell, float scale)
        {
            float r = (float)BoardLayout.HexRadius * Inset * scale;
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

        void OnDestroy()
        {
            if (material != null) DestroyNow(material);
            if (mesh != null) DestroyNow(mesh);
            mesh = null;
            cells.Clear();
            age.Clear();
        }

        static void DestroyNow(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
