using System;
using System.Collections.Generic;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 보드 위 토큰(04 §3-1 — 게임 상태가 놓는 것): 숨겨진 장소·유물 토큰(뒷면 「?」 / 공개된 앞면)과 서막의 갤럭시단 조무래기 토큰.
    /// 나무·산호·사당·전설 장소는 고정물이라 <see cref="FixtureIcons"/>·<see cref="BoardFigures"/>가 그린다 — 여기는 보기의 <c>Tokens</c> 가운데 고정물이 아닌 것만 받는다.
    /// <list type="bullet">
    /// <item>숨겨진 장소: 칸 윗면에 눕힌 둥근 토큰(Gen <c>Board/Icons/hidden_spot_back.png</c>, 고정물 아이콘보다 작게 — <see cref="SpotSize"/>). 원작 TTS 토큰도 칸 위에 눕는다.
    /// 뒷면은 정체와 무관하게 한 가지 그림이다(보기가 정체를 가려서 준다 — rulings G12·B2. 원작의 금색 유물 뒷면을 쓰지 않는다).
    /// 공개되면 앞면 그림(<c>Board/board.json</c>의 그 아이템 front)으로 바뀐다 — 표에 없는 것(유물·우두머리 — 결과는 M5)은 뒷면 그대로 둔다.</item>
    /// <item>조무래기: 마을 칸의 카메라 쪽 왼쪽 모서리 위에 카메라를 보고 선 작은 표지(짙은 원판 + 갤럭시단 「G」 — Gen <c>Cards/Generator/encounter_icons/galactic.png</c>) [가정].
    /// 그 칸에 선 말(화면에서 칸 가운데 위로 선다)과 마을 아이콘에 가리지 않게 말 옆에 띄우고, 카메라를 돌려도(Q·E) 같은 쪽에 바로 서게 방향을 따라 돈다(<see cref="Face"/>).
    /// 눕히면 그 칸에 선 말에 덮이고 「G」가 비스듬해 읽히지 않는다(검토 반영).
    /// 서막 연출(04 §8-8 5행)에서는 위에서 떨어져 내려앉는다(<see cref="Drop"/>).</item>
    /// </list>
    /// 그리는 것은 <see cref="Show"/>에 준 목록뿐이다(규칙을 보지 않는다). 셰이더는 고정물 아이콘과 같다(빛 없음 · 알파 · 깊이 견주기만).
    /// </summary>
    public sealed class BoardTokens : MonoBehaviour
    {
        public const string SpotBackKey = "Board/Icons/hidden_spot_back.png";

        /// <summary>뒷면 숨겨진 장소 토큰의 Key — 특별한 숨겨진 장소(금색 ?, TokenState.Special). 그림은 원작 TTS의 금색 토큰 <see cref="GenAssets.SpecialSpotBack"/>(결정 87 V8 나).</summary>
        public const string SpecialBack = "special";
        public const string GruntKey = "Cards/Generator/encounter_icons/galactic.png";

        /// <summary>숨겨진 장소 토큰 한 변(월드 유닛) — 고정물 아이콘 1.6보다 작다 [가정].</summary>
        public const float SpotSize = 1.25f;
        /// <summary>조무래기 표지: 원판 지름 · 「G」 한 변 · 칸 가운데에서 벗어난 자리(카메라 기준 왼쪽 · 앞 · 칸 윗면 위 높이 — 원판 가운데) [가정].</summary>
        public const float GruntDisc = 0.95f, GruntMark = 0.8f, GruntLeft = 1.0f, GruntNear = 0.1f, GruntUp = 0.95f;
        /// <summary>칸 윗면 위로 띄우는 높이: 숨겨진 장소는 고정물 아이콘과 같다.</summary>
        public const float SpotLift = 0.02f;
        /// <summary>서막 연출에서 조무래기 토큰이 떨어지기 시작하는 높이(월드 유닛).</summary>
        public const float DropHeight = 3f;

        /// <summary>조무래기 원판 색(갤럭시단 짙은 남색) [가정].</summary>
        public static readonly Color GruntDiscColor = new Color(0.10f, 0.13f, 0.20f, 0.92f);

        [Serializable] sealed class Row { public string key; public string front; }
        [Serializable] sealed class Table { public Row[] rows; }

        /// <summary>그릴 토큰 하나. <see cref="Key"/> = 공개된 숨겨진 장소의 아이템 키(뒷면·조무래기는 null).</summary>
        public readonly struct Token : IEquatable<Token>
        {
            public readonly TokenKind Kind;
            public readonly int Cell;
            public readonly bool FaceUp;
            public readonly string Key;

            public Token(TokenKind kind, int cell, bool faceUp, string key)
            {
                Kind = kind;
                Cell = cell;
                FaceUp = faceUp;
                Key = key;
            }

            public bool Equals(Token o) => Kind == o.Kind && Cell == o.Cell && FaceUp == o.FaceUp && Key == o.Key;
            public override bool Equals(object obj) => obj is Token t && Equals(t);
            public override int GetHashCode() => ((int)Kind * 397 + Cell) * 2 + (FaceUp ? 1 : 0);
        }

        readonly List<Token> shown = new List<Token>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly List<MeshRenderer> spotRenderers = new List<MeshRenderer>();
        // 숨겨진 장소 묶음 메시와 그 칸들(네모 하나 = 꼭짓점 넷, 칸 순서대로) — 게임 시작의 놓기 연출이 꼭짓점을 옮긴다
        sealed class SpotGroup { public Mesh Mesh; public List<int> Cells; }
        readonly List<SpotGroup> spotGroups = new List<SpotGroup>();
        readonly List<Vector3> spotVerts = new List<Vector3>();
        bool spotsDropping;
        readonly Dictionary<int, Transform> grunts = new Dictionary<int, Transform>();
        readonly Dictionary<string, string> fronts = new Dictionary<string, string>(StringComparer.Ordinal);
        Board board;
        IReadOnlyList<CellLook> looks;
        Shader shader;
        Transform spotRoot, gruntRoot;
        Mesh discMesh, markMesh;
        Material discMaterial, markMaterial;
        int yaw;
        bool tableRead;

        /// <summary>지금 그리는 토큰(준 순서).</summary>
        public IReadOnlyList<Token> Shown => shown;

        /// <summary>숨겨진 장소 토큰의 렌더러(그림마다 하나).</summary>
        public IReadOnlyList<MeshRenderer> SpotRenderers => spotRenderers;

        /// <summary>Gen 본체가 없어 그리지 못한 것이 있다.</summary>
        public bool GenMissing { get; private set; }

        /// <summary>조무래기 토큰이 선 칸들.</summary>
        public IEnumerable<int> GruntCells => grunts.Keys;

        public void Init(Board board, IReadOnlyList<CellLook> looks, Shader shader)
        {
            this.board = board;
            this.looks = looks;
            this.shader = shader;
        }

        /// <summary>그 칸의 조무래기 토큰(없으면 null) — 확인·시험용.</summary>
        public Transform GruntAt(int cell) => grunts.TryGetValue(cell, out var t) ? t : null;

        /// <summary>이 토큰들을 그린다(앞서 그린 것과 같으면 아무것도 하지 않는다). 칸 −1인 것은 건너뛴다.</summary>
        public void Show(IReadOnlyList<Token> tokens)
        {
            if (board == null) return;
            bool same = tokens.Count == shown.Count;
            for (int i = 0; same && i < shown.Count; i++) same = shown[i].Equals(tokens[i]);
            if (same) return;
            var next = new List<Token>(tokens);
            Wipe();
            shown.AddRange(next);
            Build();
        }

        /// <summary>
        /// 떠 있는 숨겨진 장소 토큰의 가장 낮은 높이(월드 유닛 — 멈춰 서서 묻는 동안, <c>SpotBeats</c>의 높이 1) [가정]. 칸 윗면 바로 위에 두면 그 칸에 선 말에 가려 보이지 않는다.
        /// 말은 화면 px로 그려 줌마다 월드 높이가 다르므로, 실제 높이는 화면이 말 머리(좌석 번호 핀) 위로 셈해 준다(<c>GameFlow.SpotHover</c>) — 이 값은 그 아래 한계다.
        /// 떠오른 토큰은 카메라를 보고 선다(<see cref="StandAt"/> 높이부터 다 선다 — 눕힌 채로는 핀 뒤에서 납작해 읽히지 않는다).
        /// </summary>
        public const float HoverHeight = 3.2f;

        /// <summary>떠오른 숨겨진 장소 토큰이 다 서는 높이(월드 유닛). 그 아래에서는 높이에 비례해 눕는다(0 = 칸에 눕힌 모습).</summary>
        public const float StandAt = 1.2f;

        /// <summary>지금 따로 띄운 숨겨진 장소 토큰의 칸(없으면 −1) · 그 높이(월드 유닛) · 뒤집힌 만큼(0 ~ 1).</summary>
        public int RaisedCell => focusCell;
        public float RaisedHeight { get; private set; }
        public float RaisedFlip { get; private set; }

        /// <summary>따로 띄운 토큰의 크기 배수(1 = 그대로 — 숨겨진 장소 결과 26·27행의 부풀기·펴지기, 0 = 안 보임).</summary>
        public float RaisedSwell
        {
            get => swell;
            set
            {
                if (Mathf.Approximately(swell, value)) return;
                swell = value;
                PoseFocus();
            }
        }

        float swell = 1f;

        /// <summary>
        /// 띄운 토큰이 실제로 그려졌나(그 칸에 숨겨진 장소 토큰이 있어 따로 띄운 물체가 섰다) · 앞면 그림이 있나 — 확인·캡처용(공개한 명령 안에서 빠진 토큰을 곧바로 지워
        /// 뒤집기가 안 보이던 회귀, 규칙 끝 감사). RaisedCell만으로는 모른다(토큰이 없는 칸도 적어 둔다).
        /// </summary>
        public bool RaisedDrawn => focus != null && focusCell >= 0;
        public bool RaisedHasFront => RaisedDrawn && focusFront != null;

        int focusCell = -1;
        Transform focus;
        Material focusMaterial;
        Texture2D focusBack, focusFront;

        /// <summary>
        /// 숨겨진 장소 토큰 하나를 따로 띄운다(04 §8-8 25행): 멈춰 서서 묻는 동안 떠 있고, 공개하면 가로로 뒤집힌다(가로 늘이기 — 가운데에서 앞면 그림으로 바뀐다).
        /// cell −1 = 내려놓는다(모든 토큰이 제자리). 그 칸에 숨겨진 장소 토큰이 없으면 아무것도 띄우지 않는다.
        /// </summary>
        /// <param name="height">칸 윗면 위 높이(월드 유닛).</param>
        /// <param name="flip">뒤집힌 만큼(0 = 뒷면 ~ 1 = 앞면). 앞면 그림은 그 토큰이 공개된 것일 때만 있다 — 없으면 뒷면 그대로 뒤집힌다.</param>
        public void Raise(int cell, float height = 0f, float flip = 0f)
        {
            if (board == null) return;
            if (cell != focusCell)
            {
                focusCell = cell;
                var next = new List<Token>(shown);
                Wipe();
                shown.AddRange(next);
                Build();
            }
            RaisedHeight = height;
            RaisedFlip = flip;
            PoseFocus();
        }

        // 따로 띄운 토큰의 자리·기울기·뒤집힘: 높이만큼 카메라 쪽으로 일어선다(방향·줌이 바뀌어도 다시 맞춘다 — Face)
        void PoseFocus()
        {
            if (focus == null || focusCell < 0) return;
            float height = Mathf.Max(0f, RaisedHeight), stand = Mathf.Clamp01(height / StandAt);
            focus.localPosition = CellTop(focusCell) + Vector3.up * (SpotLift + height);
            focus.localRotation = Quaternion.Slerp(Quaternion.identity, Upright(), stand);
            float wide = Mathf.Max(0.04f, Mathf.Abs(Mathf.Cos(Mathf.Clamp01(RaisedFlip) * Mathf.PI))), size = Mathf.Lerp(1f, zoomScale, stand);
            focus.localScale = new Vector3(wide * size * swell, 1f, size * swell);
            focusMaterial.SetTexture("_BaseMap", RaisedFlip >= 0.5f && focusFront != null ? focusFront : focusBack);
        }

        // 눕힌 네모(윗면이 +y)를 카메라를 보고 서게 하는 회전: 카메라 방향(yaw)으로 돌리고 내려다보는 각만큼 일으킨다
        Quaternion Upright() => Quaternion.Euler(0f, yaw * 60f, 0f) * Tilted;

        static readonly Quaternion Tilted = Quaternion.Euler(BoardCameraRig.Tilt - 90f, 0f, 0f);

        void Build()
        {
            if (shown.Count == 0) return;
            ReadTable();

            // 숨겨진 장소: 그림마다 메시 하나(뒷면 한 장 + 공개된 종류마다 한 장)
            var groups = new List<string>();
            var cells = new List<List<int>>();
            foreach (var t in shown)
            {
                if (t.Kind != TokenKind.HiddenSpot || t.Cell < 0 || t.Cell >= board.Count) continue;
                if (t.Cell == focusCell && Focus(t)) continue;   // 따로 띄운 토큰은 묶음 메시에서 뺀다
                string tex = SpotTexture(t);
                int k = groups.IndexOf(tex);
                if (k < 0) { groups.Add(tex); cells.Add(new List<int>()); k = groups.Count - 1; }
                cells[k].Add(t.Cell);
            }
            for (int k = 0; k < groups.Count; k++)
            {
                string texKey = groups[k];
                var texture = GenAssets.Load<Texture2D>(texKey);
                if (texture == null) { Missing(texKey); continue; }
                if (spotRoot == null) spotRoot = Child("Spots", transform);
                var go = new GameObject(groups[k]) { hideFlags = gameObject.hideFlags };
                go.transform.SetParent(spotRoot, false);
                var mesh = SpotQuads(cells[k], texKey);
                owned.Add(mesh);
                spotGroups.Add(new SpotGroup { Mesh = mesh, Cells = cells[k] });
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = Renderer(go);
                var m = new Material(shader) { name = "Token " + groups[k], hideFlags = HideFlags.DontSave };
                m.SetTexture("_BaseMap", texture);
                owned.Add(m);
                mr.sharedMaterial = m;
                spotRenderers.Add(mr);
            }

            // 조무래기: 칸마다 물체 하나(연출에서 따로 떨어진다)
            foreach (var t in shown)
            {
                if (t.Kind != TokenKind.Grunt || t.Cell < 0 || t.Cell >= board.Count || grunts.ContainsKey(t.Cell)) continue;
                if (!GruntParts()) break;
                if (gruntRoot == null) gruntRoot = Child("Grunts", transform);
                var at = Child("Grunt " + t.Cell, gruntRoot);
                at.localPosition = CellTop(t.Cell);
                var badge = Child("Badge", at);
                badge.localPosition = new Vector3(-GruntLeft, GruntUp, -GruntNear);
                badge.localRotation = Tilted;
                var disc = Child("Disc", badge).gameObject;
                disc.AddComponent<MeshFilter>().sharedMesh = discMesh;
                Renderer(disc).sharedMaterial = discMaterial;
                var mark = Child("Mark", badge).gameObject;
                mark.AddComponent<MeshFilter>().sharedMesh = markMesh;
                Renderer(mark).sharedMaterial = markMaterial;
                grunts[t.Cell] = at;
            }
            Face(yaw, zoomScale > 1f);
            SpotsVisible = 0;
            foreach (var g in spotGroups) SpotsVisible += g.Cells.Count;
        }

        /// <summary>
        /// 카메라 방향(60° 단위 걸음 0~5)과 줌: 조무래기 표지가 그 방향에서 본 왼쪽에 카메라를 보고 서게 돌리고, 전체 줌에서는 <see cref="FullZoomScale"/>배로 키운다 —
        /// 전체 줌의 칸 폭(720에서 34px)에서도 토큰이 읽히게 [가정]. 떠 있는 숨겨진 장소 토큰도 같은 방향·배수로 맞춘다.
        /// </summary>
        public void Face(int yawStep, bool fullZoom = false)
        {
            yaw = ((yawStep % 6) + 6) % 6;
            zoomScale = fullZoom ? FullZoomScale : 1f;
            var rot = Quaternion.Euler(0f, yaw * 60f, 0f);
            foreach (var kv in grunts)
            {
                if (kv.Value == null) continue;
                kv.Value.localRotation = rot;
                if (!dropping) kv.Value.localScale = Vector3.one * zoomScale;
            }
            PoseFocus();
        }

        /// <summary>전체 줌에서 조무래기 토큰을 키우는 배수.</summary>
        public const float FullZoomScale = 1.45f;

        float zoomScale = 1f;
        bool dropping;

        /// <summary>조무래기 토큰이 떨어지는 중인가(<see cref="Drop"/> ~ <see cref="Land"/>) — 확인·시험용.</summary>
        public bool Dropping => dropping;

        /// <summary>
        /// 서막 연출: 그 칸의 조무래기 토큰을 떨어지는 중의 모습으로 놓는다. fall −1 = 아직 안 보임, 0 = 위 ~ 1 = 내려앉음. squash = 내려앉은 뒤의 눌림(1 = 제 모양).
        /// </summary>
        public void Drop(int cell, float fall, float squash = 1f)
        {
            if (!grunts.TryGetValue(cell, out var at) || at == null) return;
            at.gameObject.SetActive(fall >= 0f);
            at.localPosition = CellTop(cell) + Vector3.up * (DropHeight * (1f - Mathf.Clamp01(fall)));
            float wide = (2f - Mathf.Clamp(squash, 0.5f, 1f)) * zoomScale;
            at.localScale = new Vector3(wide, zoomScale, wide);
            dropping = true;
        }

        /// <summary>모든 조무래기 토큰을 내려앉은 모습으로 되돌린다(연출이 끝났다 · 건너뛰었다).</summary>
        public void Land()
        {
            foreach (var kv in grunts)
            {
                if (kv.Value == null) continue;
                kv.Value.gameObject.SetActive(true);
                kv.Value.localPosition = CellTop(kv.Key);
                kv.Value.localScale = Vector3.one * zoomScale;
            }
            dropping = false;
        }

        /// <summary>숨겨진 장소 토큰이 떨어지는 중인가(<see cref="DropSpots"/> ~ <see cref="LandSpots"/>) — 확인·시험용.</summary>
        public bool SpotsDropping => spotsDropping;

        /// <summary>지금 보이는(떨어지기 시작한) 숨겨진 장소 토큰 수 — 놓기 연출 중이 아니면 모두.</summary>
        public int SpotsVisible { get; private set; }

        /// <summary>
        /// 게임 시작의 숨겨진 장소 배치(04 §8-8 5행): 눕힌 토큰들을 떨어지는 중의 모습으로 놓는다. fall(칸) −1 = 아직 안 보임, 0 = 위 ~ 1 = 내려앉음, squash(칸) = 내려앉은 뒤의 눌림(1 = 제 모양).
        /// 묶음 메시의 꼭짓점만 옮긴다(물체를 새로 만들지 않는다).
        /// </summary>
        public void DropSpots(Func<int, float> fall, Func<int, float> squash = null)
        {
            if (fall == null) throw new ArgumentNullException(nameof(fall));
            const float h = SpotSize * 0.5f;
            int visible = 0;
            foreach (var g in spotGroups)
            {
                if (g.Mesh == null) continue;
                spotVerts.Clear();
                foreach (int c in g.Cells)
                {
                    float f = fall(c);
                    var p = CellTop(c) + Vector3.up * (SpotLift + DropHeight * (1f - Mathf.Clamp01(f)));
                    // 아직 안 나타난 토큰은 네모를 한 점으로 접는다. 내려앉는 순간에는 옆으로 퍼진다
                    float r = f < 0f ? 0f : h * (2f - Mathf.Clamp(squash != null ? squash(c) : 1f, 0.5f, 1f));
                    if (f >= 0f) visible++;
                    spotVerts.Add(p + new Vector3(-r, 0, -r));
                    spotVerts.Add(p + new Vector3(-r, 0, r));
                    spotVerts.Add(p + new Vector3(r, 0, r));
                    spotVerts.Add(p + new Vector3(r, 0, -r));
                }
                g.Mesh.SetVertices(spotVerts);
                g.Mesh.RecalculateBounds();
            }
            SpotsVisible = visible;
            spotsDropping = true;
        }

        /// <summary>모든 숨겨진 장소 토큰을 내려앉은 모습으로 되돌린다(연출이 끝났다 · 건너뛰었다).</summary>
        public void LandSpots()
        {
            if (spotsDropping) DropSpots(c => 1f);
            spotsDropping = false;
        }

        /// <summary>깨어진 세계 칸의 토큰은 그 칸과 함께 숨는다(지금 규칙은 깨어진 세계에 숨겨진 장소를 놓지 않는다 — 자리만 맞춘다).</summary>
        public void SetDistortionVisible(bool visible)
        {
            foreach (var kv in grunts)
                if (kv.Value != null && board[kv.Key].DistortionWorld) kv.Value.gameObject.SetActive(visible);
        }

        // 따로 띄울 토큰의 물체(네모 한 장 — 뒷면 그림, 공개된 것이면 앞면 그림도 쥔다). Gen이 없으면 false(묶음 메시가 그대로 그린다)
        bool Focus(Token t)
        {
            if (focus != null) return true;
            focusBack = GenAssets.Load<Texture2D>(BackKey(t));
            if (focusBack == null) return false;
            string front = SpotTexture(t);
            focusFront = t.FaceUp && front != BackKey(t) ? GenAssets.Load<Texture2D>(front) : null;
            if (spotRoot == null) spotRoot = Child("Spots", transform);
            focus = Child("Spot focus", spotRoot);
            var mesh = Quad(Vector3.zero, SpotSize * 0.5f);
            owned.Add(mesh);
            focus.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            focusMaterial = new Material(shader) { name = "Token focus", hideFlags = HideFlags.DontSave };
            focusMaterial.SetTexture("_BaseMap", focusBack);
            owned.Add(focusMaterial);
            Renderer(focus.gameObject).sharedMaterial = focusMaterial;
            PoseFocus();
            return true;
        }

        string SpotTexture(Token t)
        {
            if (t.FaceUp && t.Key != null && fronts.TryGetValue(t.Key, out var front)) return "Board/" + front;
            // 유물·우두머리 토큰(보드 표에 없다 — 원작 TTS 토큰 그림은 카드 묶음의 아이템 토큰에 있다, M5 「맵 결과·배틀 안 연출」)
            if (t.FaceUp && GenAssets.SpotTokenKey(t.Key) is string token) return token;
            return BackKey(t);
        }

        // 뒷면 그림: 특별한 숨겨진 장소(뒷면의 금색 ? · 공개된 유물·우두머리 토큰의 뒤집히는 뒷면)는 금색 토큰
        static string BackKey(Token t) => (t.FaceUp ? GenAssets.SpotTokenKey(t.Key) != null : t.Key == SpecialBack) ? GenAssets.SpecialSpotBack : SpotBackKey;

        void ReadTable()
        {
            if (tableRead) return;
            tableRead = true;
            var json = GenAssets.Load<TextAsset>(FixtureIcons.TableKey);
            if (json == null) { Missing(FixtureIcons.TableKey); return; }
            foreach (var r in JsonUtility.FromJson<Table>(json.text).rows)
                if (r.key != null && r.front != null) fronts[r.key] = r.front;
        }

        bool GruntParts()
        {
            if (discMesh != null) return true;
            var texture = GenAssets.Load<Texture2D>(GruntKey);
            if (texture == null) { Missing(GruntKey); return false; }
            discMesh = Disc(Vector3.zero, GruntDisc * 0.5f);
            markMesh = Quad(Vector3.up * 0.01f, GruntMark * 0.5f);
            discMaterial = new Material(shader) { name = "Grunt disc", hideFlags = HideFlags.DontSave };
            discMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            discMaterial.SetColor("_BaseColor", GruntDiscColor);
            markMaterial = new Material(shader) { name = "Grunt mark", hideFlags = HideFlags.DontSave };
            markMaterial.SetTexture("_BaseMap", texture);
            markMaterial.renderQueue = discMaterial.renderQueue + 1;   // 원판 위에
            return true;
        }

        Vector3 CellTop(int cell) => new Vector3((float)looks[cell].X, (float)BoardLayout.TopY(board[cell]), (float)looks[cell].Z);

        Mesh SpotQuads(List<int> cells, string name)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            const float h = SpotSize * 0.5f;
            foreach (int c in cells)
            {
                var p = CellTop(c) + Vector3.up * SpotLift;
                int b = v.Count;
                v.Add(p + new Vector3(-h, 0, -h)); uv.Add(new Vector2(0, 0));
                v.Add(p + new Vector3(-h, 0, h)); uv.Add(new Vector2(0, 1));
                v.Add(p + new Vector3(h, 0, h)); uv.Add(new Vector2(1, 1));
                v.Add(p + new Vector3(h, 0, -h)); uv.Add(new Vector2(1, 0));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            var mesh = new Mesh { name = "Tokens " + name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Quad(Vector3 p, float h)
        {
            var mesh = new Mesh { name = "Grunt mark", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(new List<Vector3> { p + new Vector3(-h, 0, -h), p + new Vector3(-h, 0, h), p + new Vector3(h, 0, h), p + new Vector3(h, 0, -h) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Disc(Vector3 p, float r)
        {
            const int n = 24;
            var v = new List<Vector3> { p };
            var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var tris = new List<int>();
            for (int k = 0; k < n; k++)
            {
                float a = -k * Mathf.PI * 2f / n;   // 위에서 보아 시계 방향 — 윗면이 보이는 쪽
                v.Add(p + new Vector3(r * Mathf.Cos(a), 0f, r * Mathf.Sin(a)));
                uv.Add(new Vector2(0.5f, 0.5f));
            }
            for (int k = 0; k < n; k++)
            {
                tris.Add(0);
                tris.Add(1 + k);
                tris.Add(1 + (k + 1) % n);
            }
            var mesh = new Mesh { name = "Grunt disc", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static MeshRenderer Renderer(GameObject go)
        {
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        bool warned;

        void Missing(string key)
        {
            GenMissing = true;
            if (!warned) Debug.LogWarning("[BoardTokens] Gen 없음(" + key + "): 보드 위 토큰을 그리지 않는다. rebuild-all 먼저(03 §4-3)");
            warned = true;
        }

        Transform Child(string name, Transform parent)
        {
            var go = new GameObject(name) { hideFlags = gameObject.hideFlags };
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // 그린 것만 지운다(조무래기 부품 — 원판·「G」의 메시와 재질 — 은 남겨 다시 쓴다)
        void Wipe()
        {
            shown.Clear();
            spotRenderers.Clear();
            spotGroups.Clear();
            spotsDropping = false;
            SpotsVisible = 0;
            grunts.Clear();
            dropping = false;   // 떨어지던 토큰도 함께 지웠다 — 놓기 도중에 게임을 떠나도 다음 판에 남지 않는다
            foreach (var o in owned) if (o != null) DestroyNow(o);
            owned.Clear();
            if (spotRoot != null) DestroyNow(spotRoot.gameObject);
            if (gruntRoot != null) DestroyNow(gruntRoot.gameObject);
            spotRoot = gruntRoot = null;
            focus = null;
            focusMaterial = null;
            focusBack = focusFront = null;
        }

        void OnDestroy() => Clear();

        public void Clear()
        {
            Wipe();
            GenMissing = false;
            foreach (var o in new UnityEngine.Object[] { discMesh, markMesh, discMaterial, markMaterial }) if (o != null) DestroyNow(o);
            discMesh = markMesh = null;
            discMaterial = markMaterial = null;
        }

        static void DestroyNow(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
