using System.Collections.Generic;
using Sinnoh.Data;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 울트라 버스트 판의 UB · 네크로즈마 피규어 발밑 붉은 바닥 고리(결정 189 ①): 피규어가 서 있는 땅의 점(칸 윗면 — 칸 사이를 옮기는 동안은 두 칸 사이)에 눕힌 원 고리.
    /// 어디에 둘지는 화면 흐름(<c>GameFlow.SyncFigures</c>)이 피규어를 세울 때 정한다 — 여기는 그리기만 한다. 찾기(결정 189 ②) 동안 그 고리는 밝은 색 · 조금 큰 고리로 번쩍인다.
    /// 이동 표시(<see cref="BoardMarks"/>)와 같은 셰이더(빛 없음 · 알파 섞기 · 깊이는 견주기만)에 흰 그림 + 색 — 그림이 없어 거르기 결이 없다. 이동 표시 위 · 말 아래 층.
    /// </summary>
    public sealed class BeastRings : MonoBehaviour
    {
        /// <summary>칸 윗면 위로 띄우는 높이 — 이동 표시(<see cref="BoardMarks.Lift"/>)보다 위 [가정].</summary>
        public const float Lift = 0.04f;
        /// <summary>그리기 순서: 이동 표시(3001) 뒤, 말(3010) 앞.</summary>
        public const int Queue = 3002;
        /// <summary>고리의 바깥 · 안 반지름(칸 외접 반지름 <see cref="BoardLayout.HexRadius"/>에 대한 비율 — 넓은 UB(1.8)의 발도 안에 든다) · 번쩍일 때 늘리는 배율 [가정 — 캡처로 맞춤].</summary>
        public const float Outer = 0.82f, Inner = 0.68f, LitGrow = 1.2f, LitInner = 0.55f;
        /// <summary>원을 나누는 변 수.</summary>
        public const int Sides = 32;

        public static readonly Color RingColor = new Color(0.93f, 0.12f, 0.12f, 0.9f);
        /// <summary>번쩍일 때: 밝은 노랑 · 두꺼운 고리(안 반지름 <see cref="LitInner"/>) [가정 — 캡처로 맞춤].</summary>
        public static readonly Color LitColor = new Color(1f, 0.92f, 0.25f, 1f);

        readonly List<Vector3> points = new List<Vector3>(), next = new List<Vector3>();
        readonly List<bool> lit = new List<bool>(), nextLit = new List<bool>();
        readonly Material[] materials = new Material[2];
        Mesh mesh;
        MeshRenderer meshRenderer;

        /// <summary>지금 그린 고리의 자리(판 지역 좌표 — 땅의 점) · 번쩍이는 고리인가(확인용).</summary>
        public IReadOnlyList<Vector3> Points => points;
        public IReadOnlyList<bool> Lit => lit;
        public bool Visible => meshRenderer != null && meshRenderer.enabled;

        public void Init(Shader shader)
        {
            mesh = new Mesh { name = "BeastRings", hideFlags = HideFlags.DontSave };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            var colors = new[] { RingColor, LitColor };
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = new Material(shader) { name = "BeastRings " + i, hideFlags = HideFlags.DontSave, renderQueue = Queue };
                materials[i].SetTexture("_BaseMap", Texture2D.whiteTexture);
                materials[i].SetColor("_BaseColor", colors[i]);
            }
            meshRenderer.sharedMaterials = materials;
            meshRenderer.enabled = false;
        }

        /// <summary>이번에 그릴 고리를 모으기 시작한다(<see cref="Add"/> → <see cref="Commit"/>).</summary>
        public void Begin()
        {
            next.Clear();
            nextLit.Clear();
        }

        /// <summary>땅의 점(칸 윗면 높이) 하나에 고리를 둔다. lit = 찾기로 번쩍이는 중.</summary>
        public void Add(Vector3 ground, bool on)
        {
            next.Add(ground);
            nextLit.Add(on);
        }

        /// <summary>모은 고리로 다시 그린다(바뀌지 않았으면 그대로).</summary>
        public void Commit()
        {
            if (mesh == null) return;
            bool same = next.Count == points.Count;
            for (int i = 0; same && i < next.Count; i++) same = next[i] == points[i] && nextLit[i] == lit[i];
            if (same) return;
            points.Clear();
            points.AddRange(next);
            lit.Clear();
            lit.AddRange(nextLit);
            var v = new List<Vector3>();
            var tris = new[] { new List<int>(), new List<int>() };
            float r = (float)BoardLayout.HexRadius;
            for (int i = 0; i < points.Count; i++)
            {
                float grow = lit[i] ? LitGrow : 1f;
                Ring(v, tris[lit[i] ? 1 : 0], points[i] + Vector3.up * Lift, r * Outer * grow, r * (lit[i] ? LitInner : Inner) * grow);
            }
            mesh.Clear();
            mesh.subMeshCount = tris.Length;
            mesh.SetVertices(v);
            var uv = new List<Vector2>(v.Count);
            for (int i = 0; i < v.Count; i++) uv.Add(new Vector2(0.5f, 0.5f));
            mesh.SetUVs(0, uv);
            for (int i = 0; i < tris.Length; i++) mesh.SetTriangles(tris[i], i, false);
            mesh.RecalculateBounds();
            meshRenderer.enabled = v.Count > 0;
        }

        // 눕힌 원 고리 — 위에서 보아 시계 방향(BoardMarks.Ring과 같은 감는 방향 — 윗면이 보이는 쪽)
        static void Ring(List<Vector3> v, List<int> tris, Vector3 center, float outer, float inner)
        {
            int b = v.Count;
            for (int k = 0; k < Sides; k++)
            {
                float a = -k * 2f * Mathf.PI / Sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(center + dir * outer);
                v.Add(center + dir * inner);
            }
            for (int k = 0; k < Sides; k++)
            {
                int o0 = b + 2 * k, i0 = o0 + 1, o1 = b + 2 * ((k + 1) % Sides), i1 = o1 + 1;
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
            points.Clear();
            lit.Clear();
        }

        static void DestroyNow(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
