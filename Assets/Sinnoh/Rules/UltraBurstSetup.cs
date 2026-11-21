using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 울트라 버스트 셋업·맵(룰북 p.23·p.26, 01 §8-2, rulings A50 — M6 「UB 셋업·맵」). 게임 시작(SetupFlow.StartGame)이 부른다.
    /// - 좌석마다 빨강·파랑·노랑 규토리 3씩(p.23 「you start with 3 Red, 3 Blue and 3 Yellow Apricorns and all 6 Pokémon slots unlocked」). 파티 6칸·축복은 Engine.NewGame.
    /// - UB 9마리(Lua raw/lua/2d936ce9b1.lua:556-562 _expansion_setup → setup_items :497-536): 토큰을 섞어 하나씩, 그 토큰의 지형·기후 태그가 둘 다 맞는 야생 칸
    ///   (_get_compatible_wild_areas :467-472) 가운데 깨어진 세계가 아니고 · 이미 쓴 지역이 아니고 · 고정물·토큰이 없는 칸(_check_wild_area_is_safe :474-494)을 섞어 처음 맞는 칸에.
    ///   숨겨진 장소·유물 배치(SetupFlow.PlaceHiddenSpots, R3)와 같은 함수이고 그보다 먼저 돈다 — UB가 선 칸에는 숨겨진 장소가 놓이지 않고, 지역 기록은 UB 뒤에 비운다(:562).
    ///   B3(맞지 않는 칸 하나에서 지역 기록을 비움)은 숨겨진 장소와 같게 고쳤다: 한 바퀴 다 봐도 없을 때만 비운다.
    /// - 네크로즈마 토큰: 깨어진 세계 가운데 칸(:560 distortion_world_base 자리 — 세이브 268a0e = 칸 (−10,18)).
    /// - 왜곡 게이트 이동(p.26 「Each Distortion Gate links to its exact counterpart and does not cost a tile move to use」): MapAction의 Move 답에 Arg = 건너편 게이트(고정물).
    /// </summary>
    public static class UltraBurstSetup
    {
        /// <summary>시작 규토리: 빨강·파랑·노랑 3씩(p.23).</summary>
        public const int StartingApricorns = 3;

        static readonly ApricornColor[] StartingColors = { ApricornColor.Red, ApricornColor.Blue, ApricornColor.Yellow };

        /// <summary>네크로즈마 1페이즈 카드 키(pokemon.json — 2·3페이즈는 800:2~800:4).</summary>
        public const string NecrozmaKey = "800:1";

        /// <summary>
        /// UB 서식지 = 원작 세이브 UB 토큰의 지형·기후 태그 [확인 save 8cccdb·252fa1·67ce82·e5b898·6dd62b·53b026·7b50f2·03bad9·9829fd]. pokemon.json에는 없다(UB 카드 climate = Space).
        /// 셋업의 시작 자리 후보와 보드 피규어 배치(Sinnoh.Data FigureLayout)가 이 표 하나를 읽는다.
        /// </summary>
        public static bool Habitat(string key, out Biome biome, out Climate climate)
        {
            switch (key)
            {
                case "793:0": biome = Biome.Ocean; climate = Climate.Cold; return true;        // 텅비드
                case "794:0": biome = Biome.Forest; climate = Climate.Warm; return true;       // 매시붕
                case "795:0": biome = Biome.Plains; climate = Climate.Warm; return true;       // 페로코체
                case "796:0": biome = Biome.Mountain; climate = Climate.Warm; return true;     // 전수목
                case "797:0": biome = Biome.Mountain; climate = Climate.Cold; return true;     // 철화구야
                case "798:0": biome = Biome.Forest; climate = Climate.Temperate; return true;  // 종이신도
                case "799:0": biome = Biome.Mountain; climate = Climate.Temperate; return true; // 악식킹
                case "805:0": biome = Biome.Plains; climate = Climate.Temperate; return true;  // 차곡차곡
                case "806:0": biome = Biome.Forest; climate = Climate.Cold; return true;       // 두파팡
                default: biome = Biome.None; climate = Climate.None; return false;
            }
        }

        /// <summary>UB 카드(포켓몬 표 색인, 데이터 순서): 등급 UltraBeast 9장.</summary>
        public static List<int> UltraBeasts(Ctx ctx)
        {
            var r = new List<int>();
            var cards = ctx.Cards.Pokemon;
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].Tier == EncounterTier.UltraBeast) r.Add(i);
            return r;
        }

        /// <summary>
        /// 깨어진 세계 가운데 칸(네크로즈마 자리): 깨어진 세계 칸들의 무게중심에 가장 가까운 칸. 칸 좌표는 (col, row)이고 월드 거리² ∝ 3·Δcol² + Δrow²
        /// (이웃 = (0, ±2)·(±1, ±1) — board.json x = 1.65·col, z = 0.953·row). 깨어진 세계가 없으면 −1.
        /// </summary>
        public static int NecrozmaCell(Ctx ctx)
        {
            var board = ctx.Board;
            long n = 0, sumCol = 0, sumRow = 0;
            for (int c = 0; c < board.Count; c++)
            {
                if (!board[c].DistortionWorld) continue;
                n++;
                sumCol += board[c].Coord.Col;
                sumRow += board[c].Coord.Row;
            }
            int best = -1;
            long bestD = long.MaxValue;
            for (int c = 0; c < board.Count; c++)
            {
                if (!board[c].DistortionWorld) continue;
                long dc = n * board[c].Coord.Col - sumCol, dr = n * board[c].Coord.Row - sumRow;
                long d = 3 * dc * dc + dr * dr;
                if (d >= bestD) continue;
                best = c;
                bestD = d;
            }
            return best;
        }

        /// <summary>게임 시작의 울트라 버스트 몫(남은 스타터를 약한 덱에 섞은 뒤, 숨겨진 장소 배치 앞 — Lua setup_game 순서). 지금 Seq(Setup) 안에서 이벤트를 낸다.</summary>
        public static void Begin(GameState s, Ctx ctx, Emitter fx)
        {
            var cells = PlaceUltraBeasts(s, ctx);
            if (fx.On) fx.Add(new TokensPlaced { Kind = TokenKind.UltraBeast, Cells = cells.ToArray(), Villages = new int[0] });

            int centre = NecrozmaCell(ctx);
            int necrozma = ctx.Cards.PokemonIds.IndexOf(NecrozmaKey);
            s.Tokens.Add(new TokenState { Kind = TokenKind.Necrozma, Def = necrozma, Cell = centre, FaceUp = true });
            if (fx.On) fx.Add(new TokensPlaced { Kind = TokenKind.Necrozma, Cells = new[] { centre }, Villages = new int[0] });

            var changes = new ResChange[s.SeatCount * StartingColors.Length];
            for (int i = 0; i < s.SeatCount; i++)
            {
                var ap = s.Seats[i].Apricorns;
                for (int k = 0; k < StartingColors.Length; k++)
                {
                    int color = (int)StartingColors[k];
                    int before = ap[color];
                    ap[color] += StartingApricorns;
                    changes[i * StartingColors.Length + k] = new ResChange { Seat = i, Res = ResKind.Apricorn, Key = color, Before = before, After = ap[color] };
                }
            }
            if (fx.On) fx.Add(new ApricornsGained { Seat = SeatId.System, Changes = changes });
        }

        /// <summary>
        /// UB 9마리의 시작 자리(클래스 설명). 토큰(TokenKind.UltraBeast, Def = 포켓몬 표 색인, 앞면)을 놓은 순서대로 더하고 그 칸들을 돌려준다(토큰 순서).
        /// 서식지가 맞는 빈 칸이 하나도 없는 UB는 놓지 않는다(원작도 그 토큰은 제자리에 남는다 — 실제 보드에서는 일어나지 않는다).
        /// </summary>
        public static List<int> PlaceUltraBeasts(GameState s, Ctx ctx)
        {
            var board = ctx.Board;
            var blocked = new bool[board.Count];
            foreach (var f in ctx.Fixtures.Fixtures)
                if (f.Kind != FixtureKind.Taxi) blocked[f.Cell] = true;
            foreach (var t in s.Tokens)
                if (t.Cell >= 0) blocked[t.Cell] = true;

            var order = UltraBeasts(ctx);
            Piles.Shuffle(order, s.Rng);
            var regions = new List<string>();
            var placed = new List<int>();
            var candidates = new List<int>();
            foreach (int def in order)
            {
                if (!Habitat(ctx.Cards.Pokemon[def].Key, out var biome, out var climate)) throw new InvalidOperationException($"{ctx.Cards.Pokemon[def].Key}: UB 서식지가 없다");
                candidates.Clear();
                for (int c = 0; c < board.Count; c++)
                    if (board[c].Wild && !board[c].DistortionWorld && board[c].Biome == biome && board[c].Climate == climate) candidates.Add(c);
                Piles.Shuffle(candidates, s.Rng);
                int cell = SetupFlow.FirstSafe(board, candidates, blocked, regions);
                if (cell < 0)
                {
                    regions.Clear();
                    cell = SetupFlow.FirstSafe(board, candidates, blocked, regions);
                }
                if (cell < 0) continue;
                s.Tokens.Add(new TokenState { Kind = TokenKind.UltraBeast, Def = def, Cell = cell, FaceUp = true });
                blocked[cell] = true;
                if (board[cell].Region != null) regions.Add(board[cell].Region);
                placed.Add(cell);
            }
            return placed;
        }

        // ── 왜곡 게이트 이동 ──

        /// <summary>그 칸의 왜곡 게이트(고정물 색인). 없으면 −1.</summary>
        public static int GateAt(Ctx ctx, int cell)
        {
            if (cell < 0) return -1;
            var fixtures = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
                if (fixtures[f].Kind == FixtureKind.Gate && fixtures[f].Cell == cell) return f;
            return -1;
        }

        /// <summary>
        /// 그 좌석이 지금 칸의 게이트로 건너갈 수 있으면 건너편 게이트(고정물 색인), 아니면 −1. 울트라 버스트에서만 열린다(p.26 「You can now travel to the Distortion World」 —
        /// 신오의 심장에서는 무리 배틀만, 원작 커뮤니티 확인).
        /// 이동력을 쓰지 않고(p.26) 남은 이동력이 0이어도 된다 · 턴당 횟수 제한 없음 [가정 A50 ④]. 무리 배틀을 하지 않고 지나도 된다(원작 커뮤니티 확인).
        /// </summary>
        public static int GateAcross(GameState s, Ctx ctx, int seat)
        {
            if (s.Campaign != Campaign.UltraBurst || seat < 0 || seat >= s.SeatCount) return -1;
            int gate = GateAt(ctx, s.Seats[seat].Cell);
            return gate < 0 ? -1 : ctx.Fixtures.Fixtures[gate].Pair;
        }

        /// <summary>
        /// 그 Move 답이 게이트 이동인가(Target = 건너편 칸 · Arg = 건너편 게이트). Move의 Arg는 트레이너 카드의 무료 지형 이동(<see cref="TrainerRules.FreeArg"/>)도 쓰므로
        /// Arg가 있는 것만으로 가르지 않고 지금 건너갈 게이트·칸과 맞는지 본다(M6 「울트라 버스트 규칙 합침」).
        /// </summary>
        public static bool IsTravel(GameState s, Ctx ctx, int seat, ChoiceAnswer a)
        {
            int across = GateAcross(s, ctx, seat);
            return across >= 0 && a.Arg == across && a.Target == ctx.Fixtures.Fixtures[across].Cell;
        }

        /// <summary>게이트 이동(Move 답, Target = 건너편 칸 · Arg = 건너편 게이트): 말을 옮기고 GateTravel을 낸다. 이동력은 그대로, 턴은 이어진다.</summary>
        internal static void Travel(GameState s, Ctx ctx, Emitter fx, int seat, int toGate)
        {
            var st = s.Seats[seat];
            int fromGate = GateAt(ctx, st.Cell);
            int from = st.Cell;
            int to = ctx.Fixtures.Fixtures[toGate].Cell;
            st.Cell = to;
            st.ReachFrom = -1; // psychic B로 떨어진 게이트를 건넜으면 제 칸으로 되돌리지 않고 건너편에 선다(공중날기와 같은 꼴 — rulings A43 ⑭)
            // 칸을 옮겼다(공중날기와 같은 꼴): essentia R 「haven't moved」(rulings A34 ③) · 활공·길·무료 칸은 이어지지 않는다(A36 「in a row」)
            st.Moved = true;
            st.Glide = 0;
            st.RoadChain = false;
            TrainerRules.Jumped(st);
            if (fx.On) fx.Add(new GateTravel { Seat = seat, FromGate = fromGate, ToGate = toGate, FromCell = from, ToCell = to });
            MapActions.Continue(s, ctx, fx, seat);
        }
    }
}
