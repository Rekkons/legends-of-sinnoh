using System;
using System.Collections.Generic;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 고정물 평면 아이콘(M4-2c, d3-3 §2-2·04 §3-1 「멀리서는 룰북 p.2 같은 평면 아이콘」): fixtures.json 174 중 택시를 뺀 173칸 윗면에
    /// Gen `Board/Icons/` 토큰 그림(Gen `Board/board.json` 키 → front)을 눕혀 그린다. 원작 TTS 토큰도 칸 위에 눕는다(Custom_Token, StandUp false [확인 save]).
    /// 크기 Size·높이 Lift [가정 M4-2c], 그림 위쪽 = +z(방향 0 카메라에서 바로 선다). 가까이서 3D 소품으로 바꾸는 것과 거르개는 M4-3 이후.
    /// 그림마다 메시 하나로 묶는다(본맵·깨어진 세계 따로) → 그리기 호출 = 쓰인 그림 수. 깨어진 세계 칸 아이콘은 칸과 함께 숨는다.
    /// 숨겨진 장소·유물 52는 고정물이 아니라 셋업이 야생 칸에 섞어 놓는 토큰이라(R3) 여기서 그리지 않는다 — 게임 상태로 그리는 것은 M4-3.
    /// 게임 상태가 바꾸는 아이콘(M6 「UB 보드 화면」 — 04 §8-8 83행): UB가 밟아 부순 나무·산호·사당은 어둡게(<see cref="Shade"/> — 그 아이콘만 묶음 메시에서 빼서 따로 그린다),
    /// 맵에서 빠진 나무(kindler B)는 그리지 않는다. 바뀐 아이콘이 없으면 처음 그대로다.
    /// </summary>
    public sealed class FixtureIcons : MonoBehaviour
    {
        public const string TableKey = "Board/board.json";
        /// <summary>아이콘 한 변(월드 유닛) — 칸 안접 지름 1.905의 84% [가정 M4-2c].</summary>
        public const float Size = 1.6f;
        /// <summary>칸 윗면 위로 띄우는 높이(깊이 겹침 방지) [가정 M4-2c].</summary>
        public const float Lift = 0.02f;

        [Serializable] sealed class Row { public string key; public string front; }
        [Serializable] sealed class Table { public Row[] rows; }

        public readonly struct Icon
        {
            public readonly string FixtureKey;
            public readonly int Cell;
            /// <summary>Gen 키(`Board/Icons/…png`).</summary>
            public readonly string Texture;

            public Icon(string fixtureKey, int cell, string texture)
            {
                FixtureKey = fixtureKey;
                Cell = cell;
                Texture = texture;
            }
        }

        readonly List<Icon> icons = new List<Icon>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        Transform mainRoot, distortionRoot;

        /// <summary>파괴된 고정물 아이콘의 색(그림에 곱한다 — 시들어 어두워진 모습) [가정 plan §5 #156].</summary>
        public static readonly Color DestroyedTint = new Color(0.3f, 0.26f, 0.36f, 1f);

        // 그림·부분 묶음 하나(메시 하나 = 그리기 호출 하나)와 그 아이콘들(icons 색인)
        sealed class Group
        {
            public Mesh Mesh;
            public readonly List<int> Members = new List<int>();
            public Transform Parent;
            public Texture2D Texture;
        }

        // 어둡게 따로 그리는 아이콘 하나
        sealed class Solo
        {
            public GameObject Go;
            public Material Material;
            public float Dark = -1f;
        }

        readonly List<Group> groups = new List<Group>();
        readonly Dictionary<string, int> byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<int, Solo> solos = new Dictionary<int, Solo>();
        readonly HashSet<int> hidden = new HashSet<int>();
        int[] groupOf = new int[0];
        Board board;
        IReadOnlyList<CellLook> looks;
        Shader shader;

        public IReadOnlyList<Icon> Icons => icons;
        public IReadOnlyList<MeshRenderer> Renderers => renderers;
        public bool GenMissing { get; private set; }

        public void Build(Board board, IReadOnlyList<CellLook> looks, FixtureDb fixtures, Shader shader)
        {
            Clear();
            this.board = board;
            this.looks = looks;
            this.shader = shader;
            mainRoot = Child("Main");
            distortionRoot = Child("Distortion");
            var json = GenAssets.Load<TextAsset>(TableKey);
            if (json == null) { Missing(TableKey); return; }
            var rows = JsonUtility.FromJson<Table>(json.text).rows;
            var front = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in rows) front[r.key] = r.front;

            // 그림·부분(본맵 0 / 깨어진 세계 1)별 칸 목록. 순서는 fixtures 순서 그대로(결정적).
            var groupKeys = new List<string>();
            var groupCells = new List<List<int>>();
            foreach (var f in fixtures.Fixtures)
            {
                if (f.Kind == FixtureKind.Taxi) continue; // 찌르호크 피규어가 선다(BoardFigures)
                if (!front.TryGetValue(f.Key, out var file)) throw new InvalidOperationException($"[FixtureIcons] {TableKey}에 {f.Key}가 없다");
                string tex = "Board/" + file;
                byKey[f.Key] = icons.Count;
                icons.Add(new Icon(f.Key, f.Cell, tex));
                string g = (board[f.Cell].DistortionWorld ? "1|" : "0|") + tex;
                int k = groupKeys.IndexOf(g);
                if (k < 0) { groupKeys.Add(g); groupCells.Add(new List<int>()); groups.Add(new Group()); k = groupKeys.Count - 1; }
                groupCells[k].Add(f.Cell);
                groups[k].Members.Add(icons.Count - 1);
            }
            groupOf = new int[icons.Count];
            for (int k = 0; k < groups.Count; k++)
                foreach (int i in groups[k].Members) groupOf[i] = k;
            for (int k = 0; k < groupKeys.Count; k++)
            {
                string tex = groupKeys[k].Substring(2);
                var texture = GenAssets.Load<Texture2D>(tex);
                if (texture == null) { Missing(tex); continue; }
                var go = new GameObject(tex) { hideFlags = gameObject.hideFlags };
                go.transform.SetParent(groupKeys[k][0] == '1' ? distortionRoot : mainRoot, false);
                var mesh = Quads(board, looks, groupCells[k], tex);
                owned.Add(mesh);
                groups[k].Mesh = mesh;
                groups[k].Parent = go.transform.parent;
                groups[k].Texture = texture;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var m = new Material(shader) { name = "Icon " + tex, hideFlags = HideFlags.DontSave };
                m.SetTexture("_BaseMap", texture);
                owned.Add(m);
                mr.sharedMaterial = m;
                renderers.Add(mr);
            }
        }

        static Mesh Quads(Board board, IReadOnlyList<CellLook> looks, List<int> cells, string name)
        {
            var mesh = new Mesh { name = "Icons " + name, hideFlags = HideFlags.DontSave };
            Fill(mesh, board, looks, cells);
            return mesh;
        }

        static void Fill(Mesh mesh, Board board, IReadOnlyList<CellLook> looks, List<int> cells)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var n = new List<Vector3>();
            var tris = new List<int>();
            const float h = Size * 0.5f;
            foreach (int c in cells)
            {
                var p = new Vector3((float)looks[c].X, (float)BoardLayout.TopY(board[c]) + Lift, (float)looks[c].Z);
                int b = v.Count;
                v.Add(p + new Vector3(-h, 0, -h)); uv.Add(new Vector2(0, 0));
                v.Add(p + new Vector3(-h, 0, h)); uv.Add(new Vector2(0, 1));
                v.Add(p + new Vector3(h, 0, h)); uv.Add(new Vector2(1, 1));
                v.Add(p + new Vector3(h, 0, -h)); uv.Add(new Vector2(1, 0));
                for (int i = 0; i < 4; i++) n.Add(Vector3.up);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetNormals(n);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>그 고정물(키)의 아이콘을 어둡게(dark 0 = 그대로 · 1 = <see cref="DestroyedTint"/>) 하거나 숨긴다(off). 아이콘이 없는 고정물이면 아무것도 하지 않는다.</summary>
        public void Shade(string fixtureKey, float dark, bool off)
        {
            if (fixtureKey == null || !byKey.TryGetValue(fixtureKey, out int i) || i >= groupOf.Length) return;
            dark = off ? 0f : Mathf.Clamp01(dark);
            bool wasSolo = solos.ContainsKey(i), wasHidden = hidden.Contains(i), solo = dark > 0f;
            if (solo != wasSolo || off != wasHidden)
            {
                if (off) hidden.Add(i);
                else hidden.Remove(i);
                if (solo && !wasSolo) solos[i] = MakeSolo(i);
                else if (!solo && wasSolo)
                {
                    DestroySolo(solos[i]);
                    solos.Remove(i);
                }
                Refill(groupOf[i]);
            }
            if (solo && solos.TryGetValue(i, out var s) && s.Dark != dark)
            {
                s.Dark = dark;
                s.Material.SetColor("_BaseColor", Color.Lerp(Color.white, DestroyedTint, dark));
            }
        }

        /// <summary>모든 아이콘을 처음 모습으로(게임 밖 · 다른 판).</summary>
        public void ClearShades()
        {
            if (solos.Count == 0 && hidden.Count == 0) return;
            var touched = new HashSet<int>();
            foreach (var kv in solos)
            {
                DestroySolo(kv.Value);
                touched.Add(groupOf[kv.Key]);
            }
            foreach (int i in hidden) touched.Add(groupOf[i]);
            solos.Clear();
            hidden.Clear();
            foreach (int g in touched) Refill(g);
        }

        /// <summary>확인용: 그 고정물 아이콘의 지금 모습(어두운 정도 · 숨김 off). 아이콘이 없으면 0 · false.</summary>
        public float DarkOf(string fixtureKey, out bool off)
        {
            off = false;
            if (fixtureKey == null || !byKey.TryGetValue(fixtureKey, out int i)) return 0f;
            off = hidden.Contains(i);
            return solos.TryGetValue(i, out var s) ? Mathf.Max(0f, s.Dark) : 0f;
        }

        // 묶음 메시를 다시 채운다 — 따로 그리거나 숨긴 아이콘은 뺀다
        void Refill(int g)
        {
            var group = groups[g];
            if (group.Mesh == null) return;
            var cells = new List<int>();
            foreach (int i in group.Members)
                if (!hidden.Contains(i) && !solos.ContainsKey(i)) cells.Add(icons[i].Cell);
            Fill(group.Mesh, board, looks, cells);
        }

        Solo MakeSolo(int i)
        {
            var group = groups[groupOf[i]];
            var go = new GameObject("Shaded " + icons[i].FixtureKey) { hideFlags = gameObject.hideFlags };
            go.transform.SetParent(group.Parent != null ? group.Parent : mainRoot, false);
            var mesh = Quads(board, looks, new List<int> { icons[i].Cell }, icons[i].Texture);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var m = new Material(shader) { name = "Shaded " + icons[i].Texture, hideFlags = HideFlags.DontSave };
            if (group.Texture != null) m.SetTexture("_BaseMap", group.Texture);
            mr.sharedMaterial = m;
            return new Solo { Go = go, Material = m };
        }

        void DestroySolo(Solo s)
        {
            if (s == null) return;
            if (s.Go != null)
            {
                var mf = s.Go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) DestroyNow(mf.sharedMesh);
                DestroyNow(s.Go);
            }
            if (s.Material != null) DestroyNow(s.Material);
            s.Go = null;
            s.Material = null;
        }

        public void SetDistortionVisible(bool visible)
        {
            if (distortionRoot != null) distortionRoot.gameObject.SetActive(visible);
        }

        bool warned;

        void Missing(string key)
        {
            GenMissing = true;
            if (!warned) Debug.LogWarning("[FixtureIcons] Gen 없음(" + key + "): 고정물 아이콘을 그리지 않는다. rebuild-all 먼저(03 §4-3)");
            warned = true;
        }

        Transform Child(string name)
        {
            var go = new GameObject(name) { hideFlags = gameObject.hideFlags };
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        // 메시·재질은 DontSave(= DontUnloadUnusedAsset)라 씬을 내려도 남는다 — 지워질 때 직접 지운다(BoardView와 같이)
        void OnDestroy() => Clear();

        public void Clear()
        {
            icons.Clear();
            renderers.Clear();
            GenMissing = false;
            foreach (var s in solos.Values) DestroySolo(s);
            solos.Clear();
            hidden.Clear();
            groups.Clear();
            byKey.Clear();
            groupOf = new int[0];
            foreach (var o in owned) if (o != null) DestroyNow(o);
            owned.Clear();
            if (mainRoot != null) DestroyNow(mainRoot.gameObject);
            if (distortionRoot != null) DestroyNow(distortionRoot.gameObject);
            mainRoot = distortionRoot = null;
        }

        static void DestroyNow(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
