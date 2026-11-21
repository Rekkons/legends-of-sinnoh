using System;
using System.Collections.Generic;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sinnoh.Client
{
    /// <summary>말 그림의 양식(04 §3-1, 결정 52): 원작 주인공 필드 도트 / 트레이너 카드 직업 그림. 보는 사람의 표시 설정이다(S02 「말 그림」).</summary>
    public enum PieceStyle { Dot, Job }

    /// <summary>
    /// 플레이어 말 6(M4-2d, d3-3 §2-3 ③, 04 §3-1). 양식 둘(결정 52): 주인공 도트(Gen `Field/&lt;시트&gt;.png` — 32×32 걷기 시트의 프레임 하나, 줌 단계별 ×1·×3·×5 = 크기 안 B1) /
    /// 트레이너 카드 직업 앞모습(Gen `Trainers/Front/&lt;직업&gt;.png`, 80×80, 결정 23 — 크기 안 E, 결정 47 ④·61 ②: 전체 줌은 얼굴 조각 30×30 ×1, 지역 = 그림 전체 ×1, 근접 ×2.
    /// 얼굴 조각의 자리는 표 `trainers.json` fronts 행의 `face`)을 스탠디처럼 세운다. 줌 단계에 따라 그리는 모양은 <see cref="ShapeAt"/> 한 곳에서 정한다(셰이더는 같은 규칙을 꼭짓점에서 고른다).
    /// 어느 양식으로 세울지는 <see cref="Style"/>(보는 사람의 설정)와 좌석 값(<see cref="SeatLook"/>)으로 정한다(<see cref="StyleOf"/>): 직업 그림을 골랐어도 직업이 없는 좌석(트레이너 카드를 끈 판)은 주인공 도트다.
    /// 주인공 도트 말은 걷는 쪽을 보고 걷기 그림이 돈다(<see cref="Pose(int, Facing, int)"/> — 셈은 <c>Sinnoh.Fx.FieldWalk</c>), 서 있으면 앞모습이다.
    /// 그리기 [가정 M4-2a, 04 §3-1 안 ①의 「화면 공간 그리기」를 직업 그림에]: 발 점(칸 윗면 + 칸 안 자리, PieceLayout)만 3D로 투영해 정수 픽셀로 내리고,
    /// 그림은 화면 공간에 정수배 k(양식·줌 단계마다 — KFor·Scale)로 텍셀을 그대로 읽어 그린다(BoardPiece 셰이더, 원근 크기 변화·걸러짐·떨림 없음).
    /// 그림 깊이는 꼭짓점마다 「발에서 카메라 쪽으로 BodyPullFor만큼 당긴 점을 지나 카메라 쪽을 보고 선 평면」의 깊이라(M4-2 검토 반영) 세운 스탠디처럼
    /// 앞 칸·피규어는 가리고 뒤의 높은 칸(예: 천관산 정상 바로 앞 `0,-2`의 뒤)은 머리를 가리지 않는다. 고리·핀은 발 점에서 BiasFor만큼 당긴 한 깊이.
    /// 말 하나 = 메시 하나(발밑 고리 + 그림 + 종이 테두리 2텍셀 | 머리 위 좌석 번호 핀). 핀은 늘 위에 그린다(가려져도 좌석을 찾게) [가정 M4-2d].
    /// 한 칸에 말 여럿(결정 66 ①): 말은 하나만 세우고 나머지는 숨긴다(<see cref="Stacks"/> — 누구를 세우나·누가 묶였나는 <c>Sinnoh.Fx.PieceStacks</c>). 머리 위 묶음 표지는 PC 층이 그린다(<c>StackBadges</c>).
    /// 걷는 중인 말(<see cref="Move"/>)은 묶음에서 빠져 따로 보이고, 도착하면(<see cref="Land"/> · <see cref="Arrange"/>) 묶인다. <see cref="Stacking"/>을 끄면 옛 칸 안 6자리(PieceLayout)로 모두 세운다(M4-2 기준 캡처·시험).
    /// </summary>
    public sealed class BoardPieces : MonoBehaviour
    {
        public const string TableKey = "Trainers/trainers.json";
        /// <summary>주인공 필드 도트 표(행의 key = 주인공 키 — <c>Sinnoh.Rules.Session.Protagonists</c>).</summary>
        public const string FieldTableKey = "Field/field.json";
        /// <summary>줌 단계(1·2·3)를 셰이더에 넘기는 전역 값. BoardCameraRig.Apply가 바꾼다. 말 배수는 양식마다 여기서 나온다(<see cref="KFor"/>).</summary>
        public const string ZoomKProperty = "_SinnohPieceZoomK";
        /// <summary>고리(·핀) 깊이 = 발 점에서 카메라 쪽으로 이만큼(월드 유닛) [가정 M4-2d: 고리 아래쪽(전체 줌 약 0.33, 720 전체 약 0.5)이 제 칸 윗면에 안 묻힌다].</summary>
        public const float DepthBias = 0.5f;
        /// <summary>
        /// 그림 세운 평면 = 발 점에서 카메라 쪽으로 이만큼 당긴 점을 지난다 [가정 M4-2 검토 반영: DepthBias + 0.25 — 발 줄과 그 아래 종이 테두리 2텍셀(전체 줌 약 2.5px)이
        /// 제 고리보다 앞이고, 평면이 발보다 수평 0.43 앞이라 칸 안 자리(반지름 0.5)에 선 말의 뒤 칸 면(발에서 0.45 이상 뒤)은 늘 평면 뒤].
        /// </summary>
        public const float BodyPlanePull = 0.75f;
        /// <summary>같은 깊이 말(칸 안 같은 줄 자리)끼리 앞뒤를 정하는 좌석별 덧당김 [가정 M4-2d: 번호가 작은 좌석이 앞 — 내 좌석을 앞으로는 M4-3].</summary>
        public const float SeatBiasStep = 0.001f;
        /// <summary>그림 둘레 종이 테두리(흰 1 + 어두운 1 텍셀) [가정 M4-2d: 목업 r2 paper 필터 — 얼룩진 칸 위에서도 말 윤곽이 보이게].</summary>
        public const int OutlinePad = 2;
        /// <summary>기준 화면 높이(KFor의 배수는 이 높이의 값).</summary>
        public const int ReferenceHeight = 1080;
        /// <summary>그리기 순서: 고정물 아이콘(3000) 뒤 그림, 그 뒤 핀.</summary>
        public const int BodyQueue = 3010, PinQueue = 3020;

        [Serializable] sealed class Front { public string key; public string file; public int w; public int h; public int[] foot; public int[] face; }
        [Serializable] sealed class Table { public Front[] fronts; }
        [Serializable] sealed class FieldRow { public string key; public string file; public int fw; public int fh; public int frames; public int[] foot; public int head; public int[] n; public int[] s; public int[] w; public int[] e; }
        [Serializable] sealed class FieldTable { public FieldRow[] rows; }

        /// <summary>좌석 하나의 말 모양: 직업(trainer_cards 키, 없으면 null) · 고향 마을(fixtures 키) · 주인공(주인공 키, 없으면 null). 직업도 주인공도 없으면 말을 세우지 않는다.</summary>
        public readonly struct SeatLook
        {
            public readonly string Trainer;
            public readonly string Village;
            public readonly string Protagonist;

            public SeatLook(string trainer, string village, string protagonist = null)
            {
                Trainer = trainer;
                Village = village;
                Protagonist = protagonist;
            }
        }

        public sealed class Piece
        {
            /// <summary>좌석 색인(0~5). 번호 핀은 Seat + 1.</summary>
            public int Seat;
            /// <summary>이 말을 세운 양식.</summary>
            public PieceStyle Style;
            /// <summary>주인공 키(없으면 null).</summary>
            public string Protagonist;
            public string Trainer;
            public string Village;
            public Color32 Color;
            /// <summary>Gen 키(`Trainers/Front/…png` · `Field/…png`).</summary>
            public string TextureKey;
            public Texture2D Texture;
            /// <summary>그림 한 칸의 크기(직업 그림 = 그림 전체 80×80, 주인공 도트 = 프레임 하나 32×32).</summary>
            public int Width, Height;
            /// <summary>지금 그리는 프레임(시트의 위에서 몇째 칸 — 직업 그림은 늘 0)과 보는 쪽·걷기 자리.</summary>
            public int Frame;
            public Facing Facing = Facing.South;
            public int Cycle;
            /// <summary>보는 쪽(북·남·서·동 순)마다 걷기 한 바퀴의 프레임 번호(주인공 도트만).</summary>
            public int[][] Cycles;
            /// <summary>그림의 맨 위 불투명 줄(위가 0행) — 핀이 이 줄 위 1텍셀에 선다. 직업 그림은 0(그림 칸의 맨 위).</summary>
            public int Head;
            /// <summary>직업 그림의 얼굴 조각(그림 안 px, 위가 0행 — 표의 face). 전체 줌에서는 이 조각만 그린다(크기 안 E). 폭 0 = 없음(주인공 도트).</summary>
            public RectInt Face;
            /// <summary>발밑 고리의 반지름(텍셀).</summary>
            public int RingA, RingB;
            /// <summary>trainers.json foot: (불투명 상자 가운데 x, 가장 아래 불투명 줄 y), 위가 0행. 발 점 = 이 열의 왼쪽 가장자리 · 이 줄의 아래 가장자리.</summary>
            public Vector2Int Foot;
            /// <summary>지금 자리(판에 없으면 null).</summary>
            public PieceSpot Spot;
            public GameObject Root;
            public Material Body, Pin;
            public Texture2D Ring, PinTexture;
            public Mesh Mesh;
        }

        readonly List<Piece> pieces = new List<Piece>();
        readonly HashSet<int> moving = new HashSet<int>();
        List<PieceStack> stacks = new List<PieceStack>();
        int[] shownCells = new int[0];
        int viewer = -1, active = -1;

        /// <summary>한 칸에 말이 둘 이상이면 하나만 세우나(결정 66 ①). 끄면 칸 안 6자리로 모두 세운다 — 바꾼 뒤 <see cref="Arrange"/>.</summary>
        public bool Stacking { get; set; } = true;

        /// <summary>지금 묶음(칸 번호 순). 걷는 중인 말은 들어 있지 않다.</summary>
        public IReadOnlyList<PieceStack> Stacks => stacks;

        /// <summary>묶음이 바뀔 때마다 오른다(표지를 다시 만들 때를 안다).</summary>
        public int StacksVersion { get; private set; }

        /// <summary>그 칸의 묶음(없으면 null).</summary>
        public PieceStack StackAt(int cell) => PieceStacks.At(stacks, cell);

        /// <summary>그 좌석의 말이 묶음에 가려 서 있지 않나.</summary>
        public bool Tucked(int seat) => PieceStacks.Tucked(stacks, seat);

        /// <summary>세워 두는 말을 고르는 데 쓰는 값: 보는 좌석 · 지금 턴인 좌석(없으면 −1). 바뀌면 묶음을 다시 센다.</summary>
        public void SetStackView(int viewer, int active)
        {
            if (viewer == this.viewer && active == this.active) return;
            this.viewer = viewer;
            this.active = active;
            if (board != null) Restack();
        }
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        Board board;
        IReadOnlyList<CellLook> looks;
        IReadOnlyList<FigureSpot> figures;

        public IReadOnlyList<Piece> Pieces => pieces;
        public bool GenMissing { get; private set; }

        /// <summary>말 그림의 양식(보는 사람의 설정). 바꾼 뒤 <see cref="Build"/>로 다시 세워야 보인다. 기본은 직업 그림 — 화면 흐름(GameFlow)이 제 설정으로 바꾼다.</summary>
        public PieceStyle Style { get; set; } = PieceStyle.Job;

        /// <summary>줌 → 줌 단계(전체 1 · 지역 2 · 근접 3).</summary>
        public static int ZoomK(BoardCameraRig.Zoom z) => z == BoardCameraRig.Zoom.Full ? 1 : z == BoardCameraRig.Zoom.Region ? 2 : 3;

        /// <summary>양식 · 줌 단계(1·2·3) → 1080 기준 정수배: 직업 그림 1·1·2(크기 안 E — 전체 줌의 1은 얼굴 조각의 배수), 주인공 도트 1·3·5(크기 안 B1, 결정 47 ④). 셰이더의 max(1, 단계 × _ZoomMul + _ZoomAdd)와 같은 식.</summary>
        public static int KFor(PieceStyle style, int zoomStep) => style == PieceStyle.Dot ? 2 * zoomStep - 1 : Mathf.Max(1, zoomStep - 1);

        /// <summary>그 줌 단계에서 말 그림으로 그리는 조각: 그림 한 칸 안의 자리(X·Y, 위가 0행)·크기·발(조각 안)·머리 줄(조각 안).</summary>
        public readonly struct Shape
        {
            public readonly int X, Y, Width, Height, Head;
            public readonly Vector2Int Foot;

            public Shape(int x, int y, int width, int height, Vector2Int foot, int head)
            {
                X = x; Y = y; Width = width; Height = height; Foot = foot; Head = head;
            }
        }

        /// <summary>전체 줌(단계 1)의 직업 그림 말은 얼굴 조각만 그린다(크기 안 E). 셰이더의 고르기와 같은 규칙.</summary>
        public static bool FaceAt(Piece p, int zoomStep) => p.Face.width > 0 && zoomStep <= 1;

        /// <summary>
        /// 그 줌 단계에서 그리는 조각. 얼굴 조각이면 발 = 조각 아래 가운데(조각이 발 점 위에 바로 선다 — 고리 위의 흉상), 머리 줄 0. 아니면 그림 한 칸 전체.
        /// </summary>
        public static Shape ShapeAt(Piece p, int zoomStep) => FaceAt(p, zoomStep)
            ? new Shape(p.Face.x, p.Face.y, p.Face.width, p.Face.height, new Vector2Int(p.Face.width / 2, p.Face.height - 1), 0)
            : new Shape(0, 0, p.Width, p.Height, p.Foot, p.Head);

        /// <summary>그림 한 칸의 픽셀(아래가 0행, Width × Height)에서 그 줌 단계의 조각만 잘라 낸다(캡처 판정용 — 조각이 한 칸 전체면 그대로).</summary>
        public static Color32[] Cut(Piece p, Color32[] cell, int zoomStep)
        {
            if (!FaceAt(p, zoomStep)) return cell;
            var s = ShapeAt(p, zoomStep);
            var cut = new Color32[s.Width * s.Height];
            for (int y = 0; y < s.Height; y++)
                Array.Copy(cell, (p.Height - s.Y - s.Height + y) * p.Width + s.X, cut, y * s.Width, s.Width);
            return cut;
        }

        /// <summary>화면 높이에 맞춘 정수배 = max(1, round(zoomK × 높이 / 1080)) [가정 M4-2d: 720이면 1·1·2]. 셰이더와 같은 식.</summary>
        public static int Scale(int zoomK, int screenHeight) => Mathf.Max(1, Mathf.FloorToInt(zoomK * (float)screenHeight / ReferenceHeight + 0.5f));

        /// <summary>그 말이 그 줌 단계·화면 높이에서 그려지는 정수배.</summary>
        public static int Scale(Piece p, int zoomStep, int screenHeight) => Scale(KFor(p.Style, zoomStep), screenHeight);

        /// <summary>
        /// 좌석을 어느 양식으로 세우나: 설정이 직업 그림이고 직업이 있으면 직업 그림, 아니면 주인공이 있으면 주인공 도트(트레이너 카드를 끈 판은 직업이 없다),
        /// 주인공이 없으면 직업 그림. 둘 다 없으면 null(말 없음).
        /// </summary>
        public static PieceStyle? StyleOf(SeatLook look, PieceStyle style)
        {
            if (style == PieceStyle.Job && look.Trainer != null) return PieceStyle.Job;
            if (look.Protagonist != null) return PieceStyle.Dot;
            return look.Trainer != null ? PieceStyle.Job : (PieceStyle?)null;
        }

        /// <summary>좌석들의 겉모양(양식·직업·주인공·고향 마을)이 지금 선 말과 같은가 — 같으면 다시 세우지 않고 <see cref="Arrange"/>로 옮기기만 한다.</summary>
        public bool Same(IReadOnlyList<SeatLook> seats)
        {
            int with = 0;
            for (int s = 0; s < seats.Count; s++)
            {
                var style = StyleOf(seats[s], Style);
                if (style == null) continue;
                with++;
                var p = Of(s);
                if (p == null || p.Style != style.Value || p.Village != seats[s].Village) return false;
                // 주인공 도트 말도 직업을 쥔다(셋업에서 마을을 고른 뒤 트레이너 카드를 고르면 직업이 생긴다 — 양식을 바꿀 때 쓴다)
                if (p.Trainer != seats[s].Trainer || (style.Value == PieceStyle.Dot && p.Protagonist != seats[s].Protagonist)) return false;
            }
            return with == pieces.Count;
        }

        /// <summary>주인공(키)의 필드 도트: 시트 그림과 앞모습으로 선 프레임(시트의 위에서 몇째 칸 — 한 칸 size×size). 표나 행·그림이 없으면 false.</summary>
        public static bool FieldFront(string protagonist, out Texture2D sheet, out int frame, out int size)
        {
            sheet = null;
            frame = size = 0;
            var json = GenAssets.Load<TextAsset>(FieldTableKey);
            if (json == null) return false;
            var row = Array.Find(JsonUtility.FromJson<FieldTable>(json.text).rows, r => r.key == protagonist);
            if (row == null) return false;
            sheet = GenAssets.Load<Texture2D>("Field/" + row.file);
            frame = row.s[0];
            size = row.fw;
            return sheet != null;
        }

        /// <summary>주인공(키)의 필드 도트 중 왼쪽(서)을 보고 선 프레임(공중날기 컷인 — 결정 167). 표나 행·그림이 없으면 false.</summary>
        public static bool FieldSide(string protagonist, out Texture2D sheet, out int frame, out int size)
        {
            sheet = null;
            frame = size = 0;
            var json = GenAssets.Load<TextAsset>(FieldTableKey);
            if (json == null) return false;
            var row = Array.Find(JsonUtility.FromJson<FieldTable>(json.text).rows, r => r.key == protagonist);
            if (row == null || row.w == null || row.w.Length == 0) return false;
            sheet = GenAssets.Load<Texture2D>("Field/" + row.file);
            frame = row.w[0];
            size = row.fw;
            return sheet != null;
        }

        /// <summary>좌석 seat(0~5) 말의 깊이 당김 = DepthBias + (6 − seat) × SeatBiasStep.</summary>
        public static float BiasFor(int seat) => DepthBias + (PieceLayout.MaxSeats - seat) * SeatBiasStep;

        /// <summary>좌석 seat(0~5) 말 그림의 세운 평면 당김 = BodyPlanePull + (6 − seat) × SeatBiasStep(같은 줄 자리는 번호가 작은 좌석이 앞).</summary>
        public static float BodyPullFor(int seat) => BodyPlanePull + (PieceLayout.MaxSeats - seat) * SeatBiasStep;

        /// <summary>직업(trainer_cards 키)의 앞모습 그림 Gen 키(<c>Trainers/Front/…png</c>). 표나 행이 없으면 null.</summary>
        public static string FrontKey(string trainer)
        {
            var json = GenAssets.Load<TextAsset>(TableKey);
            if (json == null) return null;
            var row = Array.Find(JsonUtility.FromJson<Table>(json.text).fronts, r => r.key == trainer);
            return row != null ? "Trainers/" + row.file : null;
        }

        public void Build(Board board, IReadOnlyList<CellLook> looks, IReadOnlyList<FigureSpot> figures, IReadOnlyList<SeatLook> seats, IReadOnlyList<int> seatCells, Shader shader)
        {
            Clear();
            if (seats == null || seatCells == null || seats.Count != seatCells.Count) throw new ArgumentException("seats·seatCells 길이가 다르다");
            this.board = board;
            this.looks = looks;
            this.figures = figures;
            Front[] fronts = null;
            FieldRow[] fields = null;
            for (int s = 0; s < seats.Count; s++)
            {
                var style = StyleOf(seats[s], Style);
                if (style == null) continue;
                var p = new Piece { Seat = s, Style = style.Value, Trainer = seats[s].Trainer, Protagonist = seats[s].Protagonist, Village = seats[s].Village, Color = SeatColors.ForVillage(seats[s].Village) };
                if (p.Style == PieceStyle.Job)
                {
                    if (fronts == null)
                    {
                        var json = GenAssets.Load<TextAsset>(TableKey);
                        if (json == null) { Missing(TableKey); return; }
                        fronts = JsonUtility.FromJson<Table>(json.text).fronts;
                    }
                    var row = Array.Find(fronts, r => r.key == p.Trainer) ?? throw new InvalidOperationException($"[BoardPieces] {TableKey}에 {p.Trainer}가 없다");
                    p.TextureKey = "Trainers/" + row.file;
                    p.Width = row.w;
                    p.Height = row.h;
                    p.Foot = new Vector2Int(row.foot[0], row.foot[1]);
                    if (row.face == null || row.face.Length != 4) throw new InvalidOperationException($"[BoardPieces] {TableKey}의 {p.Trainer}에 얼굴 조각(face)이 없다 — node Tools/assets/build.mjs --only trainers");
                    p.Face = new RectInt(row.face[0], row.face[1], row.face[2], row.face[3]);
                    p.RingA = PieceArt.RingA;
                    p.RingB = PieceArt.RingB;
                }
                else
                {
                    if (fields == null)
                    {
                        var json = GenAssets.Load<TextAsset>(FieldTableKey);
                        if (json == null) { Missing(FieldTableKey); return; }
                        fields = JsonUtility.FromJson<FieldTable>(json.text).rows;
                    }
                    var row = Array.Find(fields, r => r.key == p.Protagonist) ?? throw new InvalidOperationException($"[BoardPieces] {FieldTableKey}에 {p.Protagonist}가 없다");
                    p.TextureKey = "Field/" + row.file;
                    p.Width = row.fw;
                    p.Height = row.fh;
                    p.Foot = new Vector2Int(row.foot[0], row.foot[1]);
                    p.Head = row.head;
                    p.Cycles = new[] { row.n, row.s, row.w, row.e };
                    p.Frame = p.Cycles[(int)Facing.South][0];
                    p.RingA = PieceArt.DotRingA;
                    p.RingB = PieceArt.DotRingB;
                }
                string key = p.TextureKey;
                var tex = GenAssets.Load<Texture2D>(key);
                if (tex == null) { Missing(key); continue; }
                p.Texture = tex;
                if (tex.width != p.Width || tex.height % p.Height != 0 || (p.Style == PieceStyle.Job && tex.height != p.Height))
                    throw new InvalidOperationException($"[BoardPieces] {key} {tex.width}×{tex.height} ≠ 표의 칸 {p.Width}×{p.Height}");
                string label = p.Style == PieceStyle.Job ? p.Trainer : p.Protagonist;
                p.Ring = Own(PieceArt.Ring(p.Color, p.RingA, p.RingB));
                p.PinTexture = Own(PieceArt.Pin(p.Color, s + 1));
                p.Body = Own(Mat(shader, "Piece " + (s + 1) + " " + label, BodyQueue, tex, CompareFunction.LessEqual, true, BiasFor(s), p.Style));
                p.Body.SetTexture("_RingTex", p.Ring);
                p.Body.SetFloat("_BodyPull", BodyPullFor(s));
                SetCell(p);
                p.Pin = Own(Mat(shader, "Pin " + (s + 1), PinQueue, p.PinTexture, CompareFunction.Always, false, BiasFor(s), p.Style));
                // 얼굴 조각(텍셀, 아래가 0행). 폭이 0이 아니면 셰이더가 전체 줌에서 조각 쪽 꼭짓점·칸을 고른다 — 핀 재질도 같은 값을 받아 같이 내려온다
                var face = p.Face.width > 0 ? new Vector4(p.Face.x, tex.height - p.Face.y - p.Face.height, p.Face.width, p.Face.height) : Vector4.zero;
                p.Body.SetVector("_FaceCell", face);
                p.Pin.SetVector("_FaceCell", face);
                p.Mesh = Own(BuildMesh(p));
                p.Root = new GameObject("Piece " + (s + 1) + " " + label) { hideFlags = gameObject.hideFlags };
                p.Root.transform.SetParent(transform, false);
                p.Root.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                var mr = p.Root.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { p.Body, p.Pin };
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                pieces.Add(p);
            }
            Arrange(seatCells);
        }

        /// <summary>
        /// 좌석마다 칸(−1 = 판에 없음)으로 말을 옮긴다. 한 칸에 둘 이상이면 하나만 세우고 나머지는 숨긴다(<see cref="Stacks"/>) — 자리는 모두 그 칸에 혼자 선 자리다.
        /// <see cref="Stacking"/>을 껐으면 한 칸 여럿을 칸 안 6자리(PieceLayout)로 모두 세운다. 주인공 도트 말은 앞모습으로 선다.
        /// </summary>
        public void Arrange(IReadOnlyList<int> seatCells)
        {
            if (board == null) return;
            shownCells = new int[seatCells.Count];
            for (int s = 0; s < shownCells.Length; s++) shownCells[s] = seatCells[s];
            moving.Clear();
            var spots = Stacking ? null : PieceLayout.Place(board, looks, seatCells, figures);
            foreach (var p in pieces)
            {
                Pose(p, Facing.South, 0);
                p.Spot = spots != null ? spots.Find(sp => sp.Seat == p.Seat) : Alone(p.Seat, p.Seat < shownCells.Length ? shownCells[p.Seat] : -1);
                if (p.Spot != null) p.Root.transform.localPosition = new Vector3((float)p.Spot.X, (float)p.Spot.Y, (float)p.Spot.Z);
            }
            Restack();
        }

        // 그 칸에 혼자 선 자리(칸 −1이면 null)
        PieceSpot Alone(int seat, int cell)
        {
            if (cell < 0) return null;
            var one = new int[seat + 1];
            for (int i = 0; i < seat; i++) one[i] = -1;
            one[seat] = cell;
            return PieceLayout.Place(board, looks, one, figures)[0];
        }

        // 묶음을 다시 세고 말을 보이거나 숨긴다: 판에 있고(걷는 중이거나 묶음에 가리지 않은) 말만 보인다
        void Restack()
        {
            stacks = Stacking ? PieceStacks.Group(shownCells, viewer, active, moving) : new List<PieceStack>();
            StacksVersion++;
            foreach (var p in pieces)
                if (p.Root != null) p.Root.SetActive(moving.Contains(p.Seat) || (p.Spot != null && !PieceStacks.Tucked(stacks, p.Seat)));
        }

        /// <summary>
        /// 걷던 말이 그 칸에 닿았다(걷기·공중날기의 끝): 그 칸에 다른 말이 있으면 묶인다(세워 두는 말이 아니면 숨는다). 이미 그 칸에 서 있으면 아무것도 하지 않는다.
        /// </summary>
        public void Land(int seat, int cell)
        {
            var p = Of(seat);
            if (p == null || board == null || seat >= shownCells.Length) return;
            if (!moving.Contains(seat) && shownCells[seat] == cell) return;
            moving.Remove(seat);
            shownCells[seat] = cell;
            p.Spot = Alone(seat, cell);
            if (p.Spot != null) p.Root.transform.localPosition = new Vector3((float)p.Spot.X, (float)p.Spot.Y, (float)p.Spot.Z);
            Restack();
        }

        /// <summary>좌석 seat의 말(없으면 null).</summary>
        public Piece Of(int seat) => pieces.Find(p => p.Seat == seat);

        static readonly int GreyId = Shader.PropertyToID("_Grey");

        /// <summary>탈락한 좌석의 말을 회색으로 바래게 한다(0 = 제 색 · 1 = 탈락 — 04 §8-8 91행, M6 「UB 화면 묶음 2」). 그림만 바래고 번호 핀·종이 테두리는 그대로다.</summary>
        public void Grey(int seat, float amount)
        {
            var p = Of(seat);
            if (p == null || p.Body == null) return;
            amount = Mathf.Clamp01(amount);
            if (!Mathf.Approximately(p.Body.GetFloat(GreyId), amount)) p.Body.SetFloat(GreyId, amount);
        }

        /// <summary>그 좌석 말의 지금 회색 정도(확인용 — 말이 없으면 0).</summary>
        public float GreyOf(int seat)
        {
            var p = Of(seat);
            return p != null && p.Body != null ? p.Body.GetFloat(GreyId) : 0f;
        }

        /// <summary>
        /// 걷는 중인 말을 발 점(보드 좌표)으로 옮긴다(04 §8-8 11행 — 칸 사이·튀는 높이). 자리(<see cref="Piece.Spot"/>)는 그대로 두고 그림만 옮긴다 —
        /// 걸음이 끝나면 <see cref="Land"/>·<see cref="Arrange"/>가 칸 안 자리에 다시 세운다. 묶여 있던 말은 묶음에서 빠져 따로 보인다. 그 좌석의 말이 없으면 false.
        /// </summary>
        public bool Move(int seat, Vector3 foot)
        {
            var p = Of(seat);
            if (p == null || p.Root == null) return false;
            if (moving.Add(seat)) Restack();
            p.Root.SetActive(true);
            p.Root.transform.localPosition = foot;
            return true;
        }

        /// <summary>주인공 도트 말이 보는 쪽과 걷기 자리(0~3 — <c>FieldWalk.Cycle</c>)를 바꾼다. 직업 그림 말은 그대로다. 그 좌석의 말이 없으면 false.</summary>
        public bool Pose(int seat, Facing facing, int cycle)
        {
            var p = Of(seat);
            if (p == null) return false;
            Pose(p, facing, cycle);
            return true;
        }

        static void Pose(Piece p, Facing facing, int cycle)
        {
            if (p.Cycles == null) return;
            p.Facing = facing;
            p.Cycle = ((cycle % FieldWalk.CycleLength) + FieldWalk.CycleLength) % FieldWalk.CycleLength;
            int frame = p.Cycles[(int)facing][p.Cycle];
            if (frame == p.Frame) return;
            p.Frame = frame;
            SetCell(p);
        }

        // 그림 한 칸(텍셀, 아래가 0행): 시트는 위에서부터 프레임을 쌓았다
        static void SetCell(Piece p)
        {
            if (p.Body != null) p.Body.SetVector("_Cell", new Vector4(0, p.Texture.height - (p.Frame + 1) * p.Height, p.Width, p.Height));
        }

        /// <summary>
        /// 셰이더와 같은 식으로 말 그림이 그려질 화면 사각형(픽셀, 아래가 0행 — Camera.WorldToScreenPoint·ReadPixels 순서)을 셈한다.
        /// cam은 그릴 대상(targetTexture)을 단 채로 부른다. zoomK = 줌 단계(<see cref="ZoomK"/>), k = 그 말의 정수배. footRaw = 발 점의 내리기 전 화면 좌표(정수에 아주 가까우면 GPU와 내림이 갈릴 수 있다).
        /// </summary>
        public static RectInt BodyRect(Piece p, Camera cam, int zoomK, out int k, out Vector2 footRaw)
        {
            var s = cam.WorldToScreenPoint(p.Root.transform.position);
            footRaw = new Vector2(s.x, s.y);
            k = Scale(p, zoomK, cam.pixelHeight);
            var sh = ShapeAt(p, zoomK);
            int fx = Mathf.FloorToInt(s.x), fy = Mathf.FloorToInt(s.y);
            return new RectInt(fx - sh.Foot.x * k, fy - (sh.Height - 1 - sh.Foot.y) * k, sh.Width * k, sh.Height * k);
        }

        /// <summary>좌석 번호 핀의 화면 사각형(BodyRect와 같은 셈): 그림의 머리 줄 위 1텍셀, 발 열 가운데.</summary>
        public static RectInt PinRect(Piece p, Camera cam, int zoomK)
        {
            var body = BodyRect(p, cam, zoomK, out int k, out _);
            var sh = ShapeAt(p, zoomK);
            int footX = body.x + sh.Foot.x * k;
            return new RectInt(footX - PieceArt.PinSize / 2 * k, body.y + body.height + (1 - sh.Head) * k, PieceArt.PinSize * k, PieceArt.PinSize * k);
        }

        /// <summary>그 줌 단계에서 발 점에서 핀 윗변까지의 높이(텍셀) — 머리 위에 띄우는 것(「!」)이 이 위에 선다.</summary>
        public static int PinTop(Piece p, int zoomStep)
        {
            var sh = ShapeAt(p, zoomStep);
            return sh.Foot.y - sh.Head + 2 + PieceArt.PinSize;
        }

        // 메시: 꼭짓점 위치 xy = 발 점에서 텍셀 단위 화면 오프셋(위가 +), uv = (텍셀 좌표 x, y, 부분 0 고리 · 1 그림 · 2 핀).
        // uv2 = 얼굴 조각으로 그릴 때(전체 줌의 직업 그림)의 (오프셋 x, y, 텍셀 x, y) — 조각이 없는 말은 위와 같은 값.
        // 셰이더가 오프셋 × k를 내린 발 픽셀에 더한다. 경계는 발 둘레로 크게(화면 공간 크기는 줌마다 달라 메시 꼭짓점으로 셀 수 없다).
        static Mesh BuildMesh(Piece p)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector3>();
            var alt = new List<Vector4>();
            var body = new List<int>();
            var pin = new List<int>();
            int a = p.RingA, b = p.RingB, pad = OutlinePad;
            Quad(v, uv, body, -a, -b, 2 * a, 2 * b, 0, 0, 0);
            Alt(alt, -a, -b, 2 * a, 2 * b, 0, 0);
            int bottom = p.Height - 1 - p.Foot.y; // 발 줄 아래로 남는 텍셀 줄 수
            Quad(v, uv, body, -p.Foot.x - pad, -bottom - pad, p.Width + 2 * pad, p.Height + 2 * pad, -pad, -pad, 1);
            int half = PieceArt.PinSize / 2, pinY = p.Height - bottom + 1 - p.Head;
            if (p.Face.width > 0)
            {
                var f = ShapeAt(p, 1);
                Alt(alt, -f.Foot.x - pad, -pad, f.Width + 2 * pad, f.Height + 2 * pad, -pad, -pad);
                Alt(alt, -half, f.Height + 1, PieceArt.PinSize, PieceArt.PinSize, 0, 0);
            }
            else
            {
                Alt(alt, -p.Foot.x - pad, -bottom - pad, p.Width + 2 * pad, p.Height + 2 * pad, -pad, -pad);
                Alt(alt, -half, pinY, PieceArt.PinSize, PieceArt.PinSize, 0, 0);
            }
            Quad(v, uv, pin, -half, pinY, PieceArt.PinSize, PieceArt.PinSize, 0, 0, 2);
            var mesh = new Mesh { name = "Piece " + (p.Seat + 1), hideFlags = HideFlags.DontSave, subMeshCount = 2 };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, alt);
            mesh.SetTriangles(body, 0, false);
            mesh.SetTriangles(pin, 1, false);
            mesh.bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(8f, 8f, 8f));
            return mesh;
        }

        static void Quad(List<Vector3> v, List<Vector3> uv, List<int> tris, int ox, int oy, int w, int h, int tx, int ty, int part)
        {
            int i = v.Count;
            v.Add(new Vector3(ox, oy, 0)); uv.Add(new Vector3(tx, ty, part));
            v.Add(new Vector3(ox, oy + h, 0)); uv.Add(new Vector3(tx, ty + h, part));
            v.Add(new Vector3(ox + w, oy + h, 0)); uv.Add(new Vector3(tx + w, ty + h, part));
            v.Add(new Vector3(ox + w, oy, 0)); uv.Add(new Vector3(tx + w, ty, part));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        // Quad와 같은 꼭짓점 순서
        static void Alt(List<Vector4> alt, int ox, int oy, int w, int h, int tx, int ty)
        {
            alt.Add(new Vector4(ox, oy, tx, ty));
            alt.Add(new Vector4(ox, oy + h, tx, ty + h));
            alt.Add(new Vector4(ox + w, oy + h, tx + w, ty + h));
            alt.Add(new Vector4(ox + w, oy, tx + w, ty));
        }

        static Material Mat(Shader shader, string name, int queue, Texture tex, CompareFunction ztest, bool zwrite, float bias, PieceStyle style)
        {
            var m = new Material(shader) { name = name, hideFlags = HideFlags.DontSave, renderQueue = queue };
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_ZTest", (float)ztest);
            m.SetFloat("_ZWrite", zwrite ? 1f : 0f);
            m.SetFloat("_DepthBias", bias);
            // 줌 단계 → 정수배(KFor와 같은 식, 셰이더가 max(1, …)): 직업 그림 단계 − 1(크기 안 E), 주인공 도트 2 × 단계 − 1
            m.SetFloat("_ZoomMul", style == PieceStyle.Dot ? 2f : 1f);
            m.SetFloat("_ZoomAdd", -1f);
            m.SetColor("_PaperColor", SeatColors.Paper);
            m.SetColor("_EdgeColor", SeatColors.Edge);
            return m;
        }

        T Own<T>(T o) where T : UnityEngine.Object
        {
            owned.Add(o);
            return o;
        }

        bool warned;

        void Missing(string key)
        {
            GenMissing = true;
            if (!warned) Debug.LogWarning("[BoardPieces] Gen 없음(" + key + "): 말을 세우지 않는다. rebuild-all 먼저(03 §4-3)");
            warned = true;
        }

        // 메시·재질·고리·핀 텍스처는 DontSave(= DontUnloadUnusedAsset)라 씬을 내려도 남는다 — 지워질 때 직접 지운다(BoardView와 같이)
        void OnDestroy() => Clear();

        public void Clear()
        {
            // 실행 중의 Destroy는 프레임 끝에야 지운다 — 같은 프레임에 다시 세우면(양식 바꾸기) 옛 말이 한 번 더 그려지지 않게 먼저 끈다
            foreach (var p in pieces)
                if (p.Root != null)
                {
                    p.Root.SetActive(false);
                    DestroyNow(p.Root);
                }
            pieces.Clear();
            foreach (var o in owned) if (o != null) DestroyNow(o);
            owned.Clear();
            GenMissing = false;
            board = null;
            moving.Clear();
            stacks = new List<PieceStack>();
            shownCells = new int[0];
            StacksVersion++;
        }

        static void DestroyNow(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
