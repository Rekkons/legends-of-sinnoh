using System.Collections.Generic;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sinnoh.Client
{
    /// <summary>
    /// 3D 보드 칸(M4-2b, d3-3 §2-1): BoardMeshBuilder 메시 둘을 자식으로 그린다. 깨어진 세계 25칸은 울트라 버스트에서만 보인다(04 §3-1 [가정]) — 기본 숨김.
    /// 재질은 Sinnoh/BoardHex 셰이더로 실행 중에 만들고, 디퓨즈는 Gen 키 `Board/hex_<디퓨즈>.png`로 읽는다(Gen 본체가 없으면 흰색 + 경고 한 번).
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        readonly List<Object> owned = new List<Object>();
        readonly MeshRenderer[] renderers = new MeshRenderer[BoardMeshBuilder.Parts];
        readonly MeshFilter[] filters = new MeshFilter[BoardMeshBuilder.Parts];

        /// <summary>칸 색인(board.json 순서) → 메시 안 자리.</summary>
        public CellSpan[] Spans { get; private set; }

        public MeshFilter Filter(int part) => filters[part];
        public MeshRenderer Renderer(int part) => renderers[part];

        public bool DistortionWorldVisible
        {
            get => renderers[1] != null && renderers[1].gameObject.activeSelf;
            set { if (renderers[1] != null) renderers[1].gameObject.SetActive(value); }
        }

        /// <summary>메시·재질을 (다시) 만든다. translucentWater = 바다·호수 틴트 알파를 쓰는 비교 그림용(d3-3 §2-1, 기본 불투명).</summary>
        public void Build(Board board, IReadOnlyList<CellLook> looks, Shader shader, bool translucentWater = false)
        {
            bool showDistortion = DistortionWorldVisible;
            Clear();
            var parts = BoardMeshBuilder.Build(board, looks, translucentWater, out var spans);
            Spans = spans;
            var materials = new Dictionary<HexGroup, Material>();
            for (int p = 0; p < parts.Length; p++)
            {
                var go = new GameObject(parts[p].Mesh.name) { hideFlags = gameObject.hideFlags };
                go.transform.SetParent(transform, false);
                owned.Add(go);
                owned.Add(parts[p].Mesh);
                filters[p] = go.AddComponent<MeshFilter>();
                filters[p].sharedMesh = parts[p].Mesh;
                renderers[p] = go.AddComponent<MeshRenderer>();
                var mats = new Material[parts[p].Groups.Length];
                for (int s = 0; s < mats.Length; s++)
                {
                    var g = parts[p].Groups[s];
                    if (!materials.TryGetValue(g, out var m)) materials[g] = m = MakeMaterial(shader, g);
                    mats[s] = m;
                }
                renderers[p].sharedMaterials = mats;
            }
            DistortionWorldVisible = showDistortion;
        }

        Material MakeMaterial(Shader shader, HexGroup g)
        {
            var m = new Material(shader) { name = "Hex " + g, hideFlags = HideFlags.DontSave };
            owned.Add(m);
            string diffuse = g == HexGroup.WaterTop ? nameof(TileDiffuse.Standard) : g <= HexGroup.Secret ? g.ToString() : null;
            if (diffuse != null) m.SetTexture("_BaseMap", DiffuseTexture(diffuse));
            if (g == HexGroup.WaterTop || g == HexGroup.WaterSides)
            {
                m.SetFloat("_UseAlpha", 1f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            return m;
        }

        bool warned;

        Texture DiffuseTexture(string diffuse)
        {
            string key = "Board/hex_" + diffuse + ".png";
            var tex = GenAssets.Load<Texture2D>(key);
            if (tex != null) return tex;
            if (!warned) Debug.LogWarning("[BoardView] Gen 없음(" + key + "): 칸 디퓨즈 없이 틴트만 그린다. rebuild-all 먼저(03 §4-3)");
            warned = true;
            return Texture2D.whiteTexture;
        }

        public void Clear()
        {
            foreach (var o in owned)
                if (o != null)
                {
                    if (Application.isPlaying) Destroy(o);
                    else DestroyImmediate(o);
                }
            owned.Clear();
        }

        void OnDestroy() => Clear();
    }
}
