using System.Collections.Generic;
using Sinnoh.Data;
using Sinnoh.Rules;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 보드 씬(`Scenes/Board.unity`) 시작점. Data/Json(추적 TextAsset)을 Sinnoh.Data로 읽고 칸 메시(BoardView, M4-2b)·피규어(BoardFigures)·고정물 아이콘(FixtureIcons, M4-2c)·
    /// 플레이어 말 6(BoardPieces, M4-2d)을 만든다(`Docs/Design/spikes/d3-3-board.md`). 처음 말은 시험 판(타이틀 뒤 전경): 좌석 i = trainer_cards i번째 직업 · fixtures i번째 마을에 선다 [가정 M4-2d].
    /// 게임이 시작되면 화면 흐름(GameFlow)이 <see cref="SetSeats"/>로 실제 좌석·말 자리로 다시 세우고, 타이틀로 돌아가면 <see cref="ShowDemoSeats"/>.
    /// 규칙 엔진이 쓰는 정적 데이터 묶음(<see cref="Ctx"/>)도 여기서 만든다 — 같은 보드·카드·고정물 표를 화면과 엔진이 함께 쓴다.
    /// Gen/ 에셋은 직렬화로 참조하지 않고 화면 코드가 키로 읽는다(03 §0, precheck 「ref」). 셰이더는 Art/Authored(추적)라 직렬화로 가리킨다.
    /// </summary>
    public sealed class BoardBoot : MonoBehaviour
    {
        [SerializeField] TextAsset boardJson;
        [SerializeField] Shader hexShader;
        [SerializeField] TextAsset pokemonJson;
        [SerializeField] TextAsset movesJson;
        [SerializeField] TextAsset trainerCardsJson;
        [SerializeField] TextAsset itemsJson;
        [SerializeField] TextAsset fixturesJson;
        [SerializeField] Shader iconShader;
        [SerializeField] Shader pieceShader;
        [SerializeField] TextAsset decksJson;
        [SerializeField] TextAsset cardsJson;
        [SerializeField] TextAsset effectsJson;

        /// <summary>엔진 보드(칸 색인 0~350).</summary>
        public Board Board { get; private set; }

        /// <summary>같은 칸 순서의 화면용 값(월드 좌표·틴트·디퓨즈·기둥 틴트).</summary>
        public IReadOnlyList<CellLook> Looks { get; private set; }

        public CardDb Cards { get; private set; }
        public FixtureDb Fixtures { get; private set; }
        public ItemDb Items { get; private set; }

        /// <summary>엔진 정적 데이터 묶음(01 §3-1). 처음 물을 때 덱·효과 표를 읽어 만든다. 씬이 그 표를 가리키지 않으면 null.</summary>
        public Ctx Ctx
        {
            get
            {
                if (ctx == null && Board != null && decksJson != null && cardsJson != null && effectsJson != null)
                    ctx = new Ctx(Board, Cards, DeckDbLoader.Load(decksJson.text, cardsJson.text, Cards), Fixtures, Items, EffectDbLoader.Load(effectsJson.text, Cards));
                return ctx;
            }
        }

        Ctx ctx;

        /// <summary>
        /// 엔진이 읽는 표 아홉의 묶음 해시(세이브 머리의 ContentHash, 01 §10): 씬이 가리키는 TextAsset의 바이트에서 센다 — <c>manifest.json</c>의 <c>contentHash</c>와 같은 식·같은 값이다
        /// (<see cref="ContentManifest.ComputeContentHash(IReadOnlyList{string}, IReadOnlyList{string})"/>, 표 이름 = manifest의 contentTables). manifest를 읽지 않고 실제로 읽은 표에서 세므로
        /// 값만 바뀐 표도 잡는다. 처음 물을 때 센다. 씬이 덱·효과 표를 가리키지 않으면 null.
        /// </summary>
        public string ContentHash
        {
            get
            {
                if (contentHash == null && boardJson != null && decksJson != null && cardsJson != null && effectsJson != null)
                {
                    var names = new[] { "board", "cards", "decks", "effects", "fixtures", "items", "moves", "pokemon", "trainer_cards" };
                    var tables = new[] { boardJson, cardsJson, decksJson, effectsJson, fixturesJson, itemsJson, movesJson, pokemonJson, trainerCardsJson };
                    var shas = new string[tables.Length];
                    for (int i = 0; i < tables.Length; i++) shas[i] = ContentManifest.Sha256Hex(tables[i].bytes);
                    contentHash = ContentManifest.ComputeContentHash(names, shas);
                }
                return contentHash;
            }
        }

        string contentHash;

        public BoardView View { get; private set; }
        public BoardFigures Figures { get; private set; }
        public FixtureIcons Icons { get; private set; }
        public BoardPieces Pieces { get; private set; }

        /// <summary>이동 표시(갈 수 있는 칸·경로·고른 칸, 04 S19). 처음 물을 때 만든다.</summary>
        public BoardMarks Marks
        {
            get
            {
                if (marks == null && View != null)
                {
                    marks = Child("Marks").AddComponent<BoardMarks>();
                    marks.Init(Board, Looks, iconShader);
                }
                return marks;
            }
        }

        BoardMarks marks;

        /// <summary>UB · 네크로즈마 피규어 발밑의 붉은 바닥 고리(결정 189). 처음 물을 때 만든다. 둘 자리는 화면 흐름(GameFlow)이 피규어를 세울 때 준다.</summary>
        public BeastRings Rings
        {
            get
            {
                if (rings == null && View != null)
                {
                    rings = Child("BeastRings").AddComponent<BeastRings>();
                    rings.Init(iconShader);
                }
                return rings;
            }
        }

        BeastRings rings;

        /// <summary>놓인 길(worker B — 감사 G5-1). 처음 물을 때 만든다. 칠할 칸은 화면 흐름(GameFlow)이 보기에서 준다.</summary>
        public BoardRoads Roads
        {
            get
            {
                if (roads == null && View != null)
                {
                    roads = Child("Roads").AddComponent<BoardRoads>();
                    roads.Init(Board, Looks, iconShader);
                }
                return roads;
            }
        }

        BoardRoads roads;

        /// <summary>보드 위 토큰(숨겨진 장소 · 조무래기 — 게임 상태가 놓는 것, 04 §3-1). 처음 물을 때 만든다. 그릴 것은 화면 흐름(GameFlow)이 보기에서 골라 준다.</summary>
        public BoardTokens Tokens
        {
            get
            {
                if (tokens == null && View != null)
                {
                    tokens = Child("Tokens").AddComponent<BoardTokens>();
                    tokens.Init(Board, Looks, iconShader);
                }
                return tokens;
            }
        }

        BoardTokens tokens;

        /// <summary>시험 판 좌석 6의 고향 마을 칸(PieceLayout.DemoHomes). 말이 처음 서는 곳.</summary>
        public IReadOnlyList<int> HomeCells { get; private set; }

        /// <summary>깨어진 세계 25칸과 그 위 피규어·아이콘(울트라 버스트에서만, 04 §3-1 [가정]). 기본 숨김.</summary>
        public bool DistortionWorldVisible
        {
            get => View != null && View.DistortionWorldVisible;
            set
            {
                if (View != null) View.DistortionWorldVisible = value;
                if (Figures != null) Figures.SetDistortionVisible(value);
                if (Icons != null) Icons.SetDistortionVisible(value);
            }
        }

        void Awake() => Build();

        /// <summary>
        /// 읽고 칸 메시를 (다시) 만든다. 피규어·아이콘은 처음 한 번만 만든다(칸 반투명 비교 그림은 칸만 다시 만든다).
        /// 에디터 배치 캡처(플레이 아님)도 부른다 — 그때 만든 것은 저장하지 않는다(DontSave), 끝나면 Teardown.
        /// </summary>
        public void Build(bool translucentWater = false)
        {
            Board = BoardLoader.Load(boardJson.text, out var looks);
            Looks = looks;
            Cards = CardDbLoader.Load(pokemonJson.text, movesJson.text, trainerCardsJson.text);
            Items = ItemDbLoader.Load(itemsJson.text);
            Fixtures = FixtureDbLoader.Load(fixturesJson.text, Board, Cards, Items);
            ctx = null;
            contentHash = null;
            bool distortion = DistortionWorldVisible;
            if (View == null) View = Child("BoardView").AddComponent<BoardView>();
            View.Build(Board, Looks, hexShader, translucentWater);
            if (Figures == null)
            {
                Figures = Child("Figures").AddComponent<BoardFigures>();
                Figures.Build(Board, Looks, Fixtures, Cards);
            }
            if (Icons == null)
            {
                Icons = Child("FixtureIcons").AddComponent<FixtureIcons>();
                Icons.Build(Board, Looks, Fixtures, iconShader);
            }
            if (Pieces == null)
            {
                Pieces = Child("Pieces").AddComponent<BoardPieces>();
                ShowDemoSeats();
            }
            DistortionWorldVisible = distortion;
        }

        /// <summary>시험 판 말 6(타이틀 뒤 전경): 좌석 i = trainer_cards i번째 직업 · fixtures i번째 마을 · i번째 주인공.</summary>
        public void ShowDemoSeats()
        {
            var homes = PieceLayout.DemoHomes(Fixtures, PieceLayout.MaxSeats);
            var seats = new List<BoardPieces.SeatLook>();
            var cells = new List<int>();
            for (int s = 0; s < homes.Count; s++)
            {
                var home = Fixtures.Fixtures[homes[s]];
                seats.Add(new BoardPieces.SeatLook(Cards.TrainerCards[s].Key, home.Key, Sinnoh.Rules.Session.Protagonists.Keys[s % Sinnoh.Rules.Session.Protagonists.Keys.Length]));
                cells.Add(home.Cell);
            }
            HomeCells = cells;
            SetSeats(seats, cells);
        }

        /// <summary>
        /// 말을 이 좌석들로 다시 세운다(게임 상태 — 좌석마다 직업 그림·주인공·고향 마을 색·서 있는 칸). 직업도 주인공도 없는 좌석과 칸 −1인 좌석은 말이 없다.
        /// </summary>
        public void SetSeats(IReadOnlyList<BoardPieces.SeatLook> seats, IReadOnlyList<int> cells)
        {
            if (Pieces == null) return;
            lastSeats = new List<BoardPieces.SeatLook>(seats);
            lastCells = new List<int>(cells);
            var spots = new List<FigureSpot>();
            foreach (var f in Figures.Figures) spots.Add(f.Spot);
            Pieces.Build(Board, Looks, spots, seats, cells, pieceShader);
        }

        List<BoardPieces.SeatLook> lastSeats;
        List<int> lastCells;

        /// <summary>말 그림의 양식(04 §3-1 — 보는 사람의 설정). 바꾸면 지금 좌석들을 그 양식으로 다시 세운다.</summary>
        public PieceStyle PieceStyle
        {
            get => Pieces != null ? Pieces.Style : PieceStyle.Job;
            set
            {
                if (Pieces == null || Pieces.Style == value) return;
                Pieces.Style = value;
                if (lastSeats != null) SetSeats(lastSeats, lastCells);
            }
        }

        /// <summary>좌석들의 겉모양(양식·직업·주인공·고향 마을)이 지금 선 말과 같은가 — 같으면 다시 세우지 않고 <see cref="BoardPieces.Arrange"/>로 옮기기만 한다.</summary>
        public bool SameSeats(IReadOnlyList<BoardPieces.SeatLook> seats) => Pieces != null && Pieces.Same(seats);

        /// <summary>칸의 윗면 가운데(월드 좌표) — 카메라가 볼 점.</summary>
        public Vector3 CellTop(int cell) => new Vector3((float)Looks[cell].X, (float)BoardLayout.TopY(Board[cell]), (float)Looks[cell].Z);

        /// <summary>만든 것(메시·재질·피규어)을 모두 지운다. 편집 모드에서는 OnDestroy가 불리지 않아 에디터 배치 캡처가 부른다.</summary>
        public void Teardown()
        {
            if (marks != null) { marks.Clear(); DestroyNow(marks.gameObject); }
            marks = null;
            if (rings != null) { rings.Clear(); DestroyNow(rings.gameObject); }
            rings = null;
            if (tokens != null) { tokens.Clear(); DestroyNow(tokens.gameObject); }
            tokens = null;
            if (Pieces != null) { Pieces.Clear(); DestroyNow(Pieces.gameObject); }
            if (Icons != null) { Icons.Clear(); DestroyNow(Icons.gameObject); }
            if (Figures != null) { Figures.Clear(); DestroyNow(Figures.gameObject); }
            if (View != null) { View.Clear(); DestroyNow(View.gameObject); }
            Pieces = null;
            Icons = null;
            Figures = null;
            View = null;
        }

        GameObject Child(string name)
        {
            var go = new GameObject(name) { hideFlags = Application.isPlaying ? HideFlags.None : HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            return go;
        }

        static void DestroyNow(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
