using System;
using System.Collections.Generic;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 판 위 3D 피규어(M4-2c, d3-3 §2-2): Gen `Figures/figures.json` 행마다 FigureLayout 자리에 프리팹(Gen 키)을 세운다.
    /// 새 루트 배율 = fitScale(번들 루트 0.01은 프리팹 안에 그대로, 03 §2-5), 방향 = 기본 카메라(방향 0, −z 쪽)를 본다 [가정 M4-2c], 플레이 중에는 Idle 반복.
    /// 지금은 시험 판이라 판에 서는 33을 한꺼번에 세운다 — 실제 게임에서 언제 서는지는 d3-3 §2-2 표, 게임 상태로 고르는 것은 M4-3·M5·M6.
    /// 깨어진 세계 칸의 피규어(네크로즈마)는 칸과 함께 숨는다(SetDistortionVisible). Gen 본체가 없으면 경고 한 번하고 비운다.
    /// </summary>
    public sealed class BoardFigures : MonoBehaviour
    {
        public const string TableKey = "Figures/figures.json";
        /// <summary>피규어가 보는 방향(y 회전). 번들 모델은 +z를 본다 → 180°면 방향 0 카메라를 본다 [가정 M4-2c].</summary>
        public const float FacingYaw = 180f;

        [Serializable] sealed class Clip { public string clip; public float length; }
        [Serializable] sealed class Clips { public Clip Idle, Walk, PhysicalAttack, SpecialAttack; }
        [Serializable] sealed class Row { public string key; public string name; public string group; public string prefab; public float fitScale; public float[] size; public Clips clips; }
        [Serializable] sealed class Table { public Row[] rows; }

        public sealed class Figure
        {
            public string Key;
            public string Group;
            public FigureSpot Spot;
            public GameObject Root;
            public Animation Animation;
            public string IdleClip;
            /// <summary>움직이는 클립(UB 이동 Walk · 밟기 PhysicalAttack · 네크로즈마의 원격 파괴 SpecialAttack — 82·83행). 표에 없으면 null.</summary>
            public string WalkClip, PhysicalClip, SpecialClip;
            /// <summary>figures.json fitScale(새 루트 배율).</summary>
            public float FitScale;
            /// <summary>figures.json 배율 1 크기(폭·높이·깊이) × fitScale.</summary>
            public Vector3 FittedSize;
            public bool Distortion;
        }

        readonly List<Figure> figures = new List<Figure>();
        Transform mainRoot, distortionRoot;

        public IReadOnlyList<Figure> Figures => figures;

        /// <summary>figures.json 행 중 판에 서지 않는 것(폼 모델 4 등)의 키.</summary>
        public IReadOnlyList<string> OffBoard { get; private set; } = new string[0];

        /// <summary>Gen 본체가 없어 세우지 못했다.</summary>
        public bool GenMissing { get; private set; }

        public void Build(Board board, IReadOnlyList<CellLook> looks, FixtureDb fixtures, CardDb cards)
        {
            Clear();
            mainRoot = Child("Main");
            distortionRoot = Child("Distortion");
            var json = GenAssets.Load<TextAsset>(TableKey);
            if (json == null) { Missing(TableKey); return; }
            var table = JsonUtility.FromJson<Table>(json.text);
            var keys = new List<string>();
            var groups = new List<string>();
            foreach (var r in table.rows) { keys.Add(r.key); groups.Add(r.group); }
            var spots = FigureLayout.Place(board, looks, fixtures, cards, keys, groups);
            var off = new List<string>(keys);
            foreach (var spot in spots)
            {
                off.Remove(spot.Key);
                var row = Array.Find(table.rows, r => r.key == spot.Key);
                var prefab = GenAssets.Load<GameObject>("Figures/" + row.prefab);
                if (prefab == null) { Missing("Figures/" + row.prefab); continue; }
                bool dist = board[spot.Cell].DistortionWorld;
                var go = Instantiate(prefab, dist ? distortionRoot : mainRoot, false);
                go.name = row.key + " " + row.name;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = gameObject.hideFlags;
                go.transform.localPosition = new Vector3((float)spot.X, (float)spot.Y, (float)spot.Z);
                go.transform.localRotation = Quaternion.Euler(0f, FacingYaw, 0f);
                go.transform.localScale = Vector3.one * row.fitScale;
                var f = new Figure
                {
                    Key = row.key, Group = row.group, Spot = spot, Root = go, Animation = go.GetComponentInChildren<Animation>(true),
                    IdleClip = row.clips.Idle.clip, FitScale = row.fitScale, Distortion = dist,
                    WalkClip = row.clips.Walk?.clip, PhysicalClip = row.clips.PhysicalAttack?.clip, SpecialClip = row.clips.SpecialAttack?.clip,
                    FittedSize = new Vector3(row.size[0], row.size[1], row.size[2]) * row.fitScale,
                };
                figures.Add(f);
                if (Application.isPlaying) PlayIdle(f);
                else Pose(f, 0f);
            }
            OffBoard = off;
            formRows = table.rows;
        }

        Row[] formRows;
        readonly Dictionary<string, Figure> forms = new Dictionary<string, Figure>();

        /// <summary>
        /// 판에 서는 피규어(<paramref name="at"/> — 네크로즈마 1페이즈)와 같은 자리에 세울 다른 폼(판에 자리가 없는 행 — <see cref="OffBoard"/>의 네크로즈마 2·3페이즈 폼, M6 「UB 시작」).
        /// 처음 부를 때 만들어 숨겨 두고 그 뒤로는 같은 것을 준다(<see cref="Figures"/>에는 넣지 않는다 — 판에 서는 피규어 수는 그대로). 표에 없거나 Gen이 없으면 null.
        /// </summary>
        public Figure FormOf(Figure at, string key)
        {
            if (at == null || key == null || formRows == null) return null;
            if (key == at.Key) return at;
            if (forms.TryGetValue(key, out var made)) return made;
            var row = Array.Find(formRows, r => r.key == key);
            var prefab = row != null ? GenAssets.Load<GameObject>("Figures/" + row.prefab) : null;
            if (prefab == null) { forms[key] = null; return null; }
            var go = Instantiate(prefab, at.Root.transform.parent, false);
            go.name = row.key + " " + row.name;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = gameObject.hideFlags;
            go.transform.localPosition = at.Root.transform.localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, FacingYaw, 0f);
            go.transform.localScale = Vector3.one * row.fitScale;
            go.SetActive(false);
            var f = new Figure
            {
                Key = row.key, Group = row.group, Spot = at.Spot, Root = go, Animation = go.GetComponentInChildren<Animation>(true),
                IdleClip = row.clips.Idle.clip, FitScale = row.fitScale, Distortion = at.Distortion,
                WalkClip = row.clips.Walk?.clip, PhysicalClip = row.clips.PhysicalAttack?.clip, SpecialClip = row.clips.SpecialAttack?.clip,
                FittedSize = new Vector3(row.size[0], row.size[1], row.size[2]) * row.fitScale,
            };
            forms[key] = f;
            if (!Application.isPlaying) Pose(f, 0f);
            return f;
        }

        /// <summary>지금까지 만든 다른 폼(<see cref="FormOf"/>).</summary>
        public IEnumerable<Figure> Forms
        {
            get
            {
                foreach (var f in forms.Values) if (f != null) yield return f;
            }
        }

        /// <summary>내려앉는 피규어가 시작하는 높이(월드 유닛 — 33·34행).</summary>
        public const float DescendHeight = 6f;

        /// <summary>빛으로 흩어지는 피규어가 떠오르는 높이(P28 · P37) · 전설 장소의 피규어가 솟는 높이(P25 — 월드 유닛).</summary>
        public const float ScatterHeight = 1.2f, RiseHeight = 1.5f;

        /// <summary>이 폭(월드 유닛)보다 좁은 피규어는 좁은 만큼 더 높이 솟는다(폭 1마다 <see cref="RisePerWidth"/>) — <see cref="RiseTop"/>까지.</summary>
        public const float RiseWide = 1.4f, RisePerWidth = 3f, RiseTop = 3f;

        /// <summary>
        /// 전설 장소의 그 피규어가 솟는 높이(P25 — 월드 유닛): 폭이 넓은 것(히드런)은 <see cref="RiseHeight"/>만 솟아도 말 양옆으로 보이지만, 좁은 것(로토무 · 쉐이미 · 호수의 수호신)은
        /// 그 높이에서는 말과 말 머리 위의 유물 그림에 가린다 — 좁을수록 더 높이, 유물 그림 위까지 [가정 — plan §5 #510].
        /// </summary>
        public static float RiseOf(Figure f) => f == null ? RiseHeight : Mathf.Clamp(RiseHeight + (RiseWide - f.FittedSize.x) * RisePerWidth, RiseHeight, RiseTop);

        /// <summary>그 피규어가 서는 자리(이 판의 지역 좌표 — <see cref="PlaceAt"/>에 넣는 값).</summary>
        public static Vector3 Rest(Figure f) => new Vector3((float)f.Spot.X, (float)f.Spot.Y, (float)f.Spot.Z);

        /// <summary>
        /// 그 피규어를 보이거나 숨기고, 제자리에서 height만큼 띄우고 squash만큼 누른다(33행 전설 이벤트 · 34행 신 강림 — M5 「사당·균열 화면」).
        /// 게임 밖(시험 판)에서는 부르지 않는다 — 모두 선 채다.
        /// </summary>
        public static void Place(Figure f, bool shown, float height = 0f, float squash = 1f, float size = 1f)
        {
            if (f == null || f.Root == null) return;
            if (f.Root.activeSelf != shown) f.Root.SetActive(shown);
            if (!shown) return;
            f.Root.transform.localPosition = new Vector3((float)f.Spot.X, (float)f.Spot.Y + height, (float)f.Spot.Z);
            f.Root.transform.localScale = new Vector3(f.FitScale * size, f.FitScale * size * squash, f.FitScale * size);   // size = 빛으로 흩어지며 줄어드는 만큼(P28 · P37)
            if (Application.isPlaying && f.Animation != null && !f.Animation.isPlaying) PlayIdle(f);
        }

        /// <summary>
        /// 그 피규어를 자리(이 판의 지역 좌표 — <c>BoardBoot.CellTop</c>과 같은 공간)에 세운다(울트라 버스트의 UB · 네크로즈마 — 판의 토큰 칸, M6 「UB 보드 화면」).
        /// clip이 Idle이 아니면 그 클립의 clipMs 자세로 멈춰 세우고(이동 · 밟기 연출), Idle이면 플레이 중에는 반복 재생으로 돌아간다.
        /// </summary>
        public static void PlaceAt(Figure f, Vector3 local, Sinnoh.Fx.BeastClip clip = Sinnoh.Fx.BeastClip.Idle, int clipMs = 0, float squash = 1f)
        {
            if (f == null || f.Root == null) return;
            if (!f.Root.activeSelf) f.Root.SetActive(true);
            f.Root.transform.localPosition = local;
            f.Root.transform.localScale = new Vector3(f.FitScale, f.FitScale * squash, f.FitScale);
            string name = clip == Sinnoh.Fx.BeastClip.Walk ? f.WalkClip : clip == Sinnoh.Fx.BeastClip.PhysicalAttack ? f.PhysicalClip : clip == Sinnoh.Fx.BeastClip.SpecialAttack ? f.SpecialClip : null;
            if (!string.IsNullOrEmpty(name) && Sample(f, name, clipMs / 1000f)) return;
            if (Application.isPlaying && f.Animation != null && !f.Animation.isPlaying) PlayIdle(f);
        }

        // 그 클립의 t초(클립 길이로 되감는다) 자세로 멈춘다. 클립이 없으면 false
        static bool Sample(Figure f, string clip, float t)
        {
            if (f.Animation == null) return false;
            var st = f.Animation[clip];
            if (st == null) return false;
            f.Animation.Stop();
            st.enabled = true; st.weight = 1f; st.time = st.length > 0f ? t % st.length : 0f;
            f.Animation.Sample();
            st.enabled = false;
            return true;
        }

        /// <summary>Idle 반복 재생(플레이 중).</summary>
        public static void PlayIdle(Figure f)
        {
            if (f.Animation == null) return;
            var st = f.Animation[f.IdleClip];
            if (st == null) return;
            st.wrapMode = WrapMode.Loop;
            f.Animation.Play(f.IdleClip);
        }

        /// <summary>Idle t초 자세로 멈춘다(배치 캡처·크기 재기). 플레이 중이면 재생을 멈춘다 — 다시 돌리려면 PlayIdle.</summary>
        public static void Pose(Figure f, float t)
        {
            if (f.Animation == null) return;
            var st = f.Animation[f.IdleClip];
            if (st == null) return;
            f.Animation.Stop();
            st.enabled = true; st.weight = 1f; st.time = t;
            f.Animation.Sample();
            st.enabled = false;
        }

        public void SetDistortionVisible(bool visible)
        {
            if (distortionRoot != null) distortionRoot.gameObject.SetActive(visible);
        }

        /// <summary>스킨 메시를 지금 자세로 구워 잰 월드 경계(FigureImport.WorldBounds와 같은 방법 — 렌더러 경계는 스킨 자세를 늦게 따른다).</summary>
        public static Bounds BakedBounds(GameObject go)
        {
            bool any = false;
            var bb = new Bounds();
            void Add(Vector3 w) { if (!any) { bb = new Bounds(w, Vector3.zero); any = true; } else bb.Encapsulate(w); }
            var baked = new Mesh();
            try
            {
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (!smr.sharedMesh || !smr.enabled) continue;
                    smr.BakeMesh(baked, true);
                    foreach (var v in baked.vertices) Add(smr.transform.TransformPoint(v));
                }
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(false))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (!mf.sharedMesh || !mr || !mr.enabled) continue;
                    foreach (var v in mf.sharedMesh.vertices) Add(mf.transform.TransformPoint(v));
                }
            }
            finally { DestroyNow(baked); }
            return any ? bb : new Bounds(go.transform.position, Vector3.zero);
        }

        bool warned;

        // 웹 손님판의 나눠 받기 전(그 번들을 아직 받는 중 — M8 · M9 마감 감사 2차 참고 ③)이면 경고하지 않는다: 다 오면 BoardBoot가 다시 세우고(GenMissing), 그때도 없으면 경고한다
        void Missing(string key)
        {
            GenMissing = true;
            if (warned || GenAssets.Pending(key)) return;
            Debug.LogWarning("[BoardFigures] Gen 없음(" + key + "): 피규어를 세우지 않는다. rebuild-all 먼저(03 §4-3)");
            warned = true;
        }

        Transform Child(string name)
        {
            var go = new GameObject(name) { hideFlags = gameObject.hideFlags };
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        public void Clear()
        {
            figures.Clear();
            forms.Clear();
            formRows = null;
            OffBoard = new string[0];
            GenMissing = false;
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
