using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 트레이너 카드 — 이동 11(M6 묶음 5, rulings A36): hiker B · picnicker B · roller_skater B · swimmer B · triathlete B(턴당 1번 지형 2칸 무료) · sky_trainer B(2층 이상 내려서기 + 활공 4칸) ·
    /// bird_keeper B(아무 칸에서 마을로 날기) · picnicker B 뒤 절반(야영지로 날기) · team_aqua B · team_magma B(턴 끝에 다른 바다·산 칸으로) · essentia B(배틀 뒤 남은 이동력) ·
    /// roller_skater B 뒤 절반(야생은 둘을 쓰러뜨려야 도망) · swimmer B 뒤 절반(물 우선도 +1 — PriorityMods) · worker B(JP 2로 길 놓기).
    /// 걷기의 값·조건은 <see cref="MoveRules.MoveTerms"/>(<see cref="Terms"/>), 합법 답·적용은 MapActions가 여기를 부른다.
    /// </summary>
    public static partial class TrainerRules
    {
        /// <summary>「Once per turn, you may move up to 2 … tiles in a row for free」.</summary>
        public const int FreeTiles = 2;
        /// <summary>Move 답의 Arg: 무료 지형 이동(걸음 칸마다 그 지형 · 이동력을 쓰지 않는다).</summary>
        public const int FreeArg = 1;
        /// <summary>worker B 「Costs 2 Journey Points」.</summary>
        public const int RoadJp = 2;
        /// <summary>SeatState.Closing = ClosingBase + 턴 끝 까닭(TurnEndReason). 0 = 닫는 중이 아니다.</summary>
        public const int ClosingBase = 1;

        /// <summary>그 좌석의 걷기 조건: 높이 규칙 · hiker B(높이 무시) · sky_trainer B(2층 이상 내려서기·활공) · 놓인 길(모든 좌석).</summary>
        public static MoveRules.MoveTerms Terms(GameState s, Ctx ctx, int seat)
        {
            var t = new MoveRules.MoveTerms { Mode = s.Options.ElevationMode, Roads = s.Roads };
            if (!Seated(s, seat)) return t;
            t.AnyElevation = Has(s, ctx, seat, "hiker", Blue);
            t.Descend = Has(s, ctx, seat, "sky_trainer", Blue);
            return t;
        }

        static bool StepOk(Board board, MoveRules.MoveTerms t, int from, int to)
        {
            int glide = 0;
            bool chain = false;
            return MoveRules.StepCost(board, t, from, to, ref glide, ref chain) >= 0;
        }

        // ── 턴당 1번 지형 2칸 무료 ──

        /// <summary>
        /// 그 좌석이 그 칸을 무료로 밟을 수 있나: hiker B 산 · picnicker B 숲 · roller_skater B 평지 · swimmer B 바다 · triathlete B 산이 아닌 칸(호수·유적 포함).
        /// 호수는 바다가 아니다(지형 태그 Ocean만 — 원작 커뮤니티 확인).
        /// </summary>
        public static bool FreeTile(GameState s, Ctx ctx, int seat, int cell)
        {
            if (!Seated(s, seat)) return false;
            var biome = ctx.Board[cell].Biome;
            if (Has(s, ctx, seat, "hiker", Blue)) return biome == Biome.Mountain;
            if (Has(s, ctx, seat, "picnicker", Blue)) return biome == Biome.Forest;
            if (Has(s, ctx, seat, "roller_skater", Blue)) return biome == Biome.Plains;
            if (Has(s, ctx, seat, "swimmer", Blue)) return biome == Biome.Ocean;
            if (Has(s, ctx, seat, "triathlete", Blue)) return biome != Biome.Mountain;
            return false;
        }

        static bool HasFreeMove(GameState s, Ctx ctx, int seat) =>
            Seated(s, seat) && (Has(s, ctx, seat, "hiker", Blue) || Has(s, ctx, seat, "picnicker", Blue) || Has(s, ctx, seat, "roller_skater", Blue)
                || Has(s, ctx, seat, "swimmer", Blue) || Has(s, ctx, seat, "triathlete", Blue));

        /// <summary>
        /// 무료 지형 이동으로 닿는 칸마다 걸음 수(못 가면 −1, 지금 칸 0): 남은 무료 칸(SeatState.FreeLeft — 턴 시작에 2, 무료로 한 칸 밟으면 1, 그 사이에 보통 걸음을 걸으면 0) 안에서
        /// 밟는 칸마다 그 지형이고 걸음이 높이 규칙을 지난다. parent가 있으면 앞 칸을 채운다.
        /// </summary>
        public static int[] FreeReach(GameState s, Ctx ctx, int seat, int[] parent = null)
        {
            var board = ctx.Board;
            var steps = new int[board.Count];
            for (int i = 0; i < steps.Length; i++) steps[i] = -1;
            if (!HasFreeMove(s, ctx, seat)) return steps;
            var st = s.Seats[seat];
            if (st.FreeLeft <= 0 || st.Cell < 0) return steps;
            var t = Terms(s, ctx, seat);
            var queue = new List<int> { st.Cell };
            steps[st.Cell] = 0;
            for (int head = 0; head < queue.Count; head++)
            {
                int c = queue[head];
                if (steps[c] >= st.FreeLeft) continue;
                for (int d = 0; d < Board.Directions; d++)
                {
                    int n = board.Neighbor(c, d);
                    if (n == Board.NoCell || steps[n] != -1 || !FreeTile(s, ctx, seat, n) || !StepOk(board, t, c, n)) continue;
                    steps[n] = steps[c] + 1;
                    if (parent != null) parent[n] = c;
                    queue.Add(n);
                }
            }
            return steps;
        }

        /// <summary>무료 지형 이동의 길(출발 칸 뺀 걸음 칸 — 가장 짧은 것). 못 가면 null.</summary>
        public static List<int> FreePath(GameState s, Ctx ctx, int seat, int dest)
        {
            var parent = new int[ctx.Board.Count];
            var steps = FreeReach(s, ctx, seat, parent);
            if (dest < 0 || dest >= steps.Length || steps[dest] <= 0) return null;
            var path = new List<int>(steps[dest]);
            for (int c = dest; c != s.Seats[seat].Cell; c = parent[c]) path.Add(c);
            path.Reverse();
            return path;
        }

        /// <summary>고른 길(걸음 칸)이 무료 지형 이동으로 합법인가: 남은 무료 칸 안 · 한 칸씩 이어지고 · 밟는 칸마다 그 지형.</summary>
        public static bool FreeWalkAllowed(GameState s, Ctx ctx, int seat, IReadOnlyList<int> steps)
        {
            if (steps == null || steps.Count == 0 || !HasFreeMove(s, ctx, seat)) return false;
            var st = s.Seats[seat];
            if (steps.Count > st.FreeLeft) return false;
            var t = Terms(s, ctx, seat);
            int at = st.Cell;
            foreach (int next in steps)
            {
                if (next < 0 || next >= ctx.Board.Count || !MoveRules.Adjacent(ctx.Board, at, next) || !FreeTile(s, ctx, seat, next) || !StepOk(ctx.Board, t, at, next)) return false;
                at = next;
            }
            return true;
        }

        /// <summary>
        /// 걷지 않고 칸을 옮겼다(공중날기 · 왜곡 게이트 · 펄기아 순간이동 · 턴 끝 다이빙 · ruin_maniac B): 무료 칸은 이어서만이라(「in a row」 — 원작 커뮤니티 확인, rulings A36)
        /// 이미 무료로 한 칸이라도 걸었으면 남은 무료 칸을 끊는다. 아직 쓰지 않았으면(2칸) 새 칸에서 쓸 수 있다 — 보통 걸음과 같은 꼴(MapActions.Step).
        /// </summary>
        internal static void Jumped(SeatState st)
        {
            if (st.FreeLeft < FreeTiles) st.FreeLeft = 0;
        }

        // ── 날기: bird_keeper B · picnicker B ──

        /// <summary>bird_keeper B 「You may fly to a village from any tile by discarding 1 Apricorn」.</summary>
        public static bool Whistles(GameState s, Ctx ctx, int seat) => Seated(s, seat) && Has(s, ctx, seat, "bird_keeper", Blue);

        /// <summary>picnicker B 「you may fly to Campsites from any tile by discarding 1 Apricorn」: 날아갈 수 있는 앞면 야영지의 칸(지금 칸 제외, 토큰 순서).</summary>
        public static void Campsites(GameState s, Ctx ctx, int seat, List<int> into)
        {
            into.Clear();
            if (!Seated(s, seat) || !Has(s, ctx, seat, "picnicker", Blue)) return;
            int camp = ctx.Items.Ids.IndexOf("hidden_spot.campsite");
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.HiddenSpot && t.FaceUp && !t.Destroyed && t.Def == camp && t.Cell >= 0 && t.Cell != s.Seats[seat].Cell && !into.Contains(t.Cell)) into.Add(t.Cell);
        }

        // ── 턴 끝: team_aqua B · team_magma B · essentia B ──

        /// <summary>
        /// team_aqua B 「If you are standing on an Ocean tile at the end of your turn, you may instantly move to any other Ocean tile」 · team_magma B(산): 지금 칸에서 턴을 끝내며 갈 수 있는 칸.
        /// 같은 세계(깨어진 세계 안팎)의 같은 지형 칸 모두 — 지금 칸은 뺀다.
        /// </summary>
        public static void DiveTargets(GameState s, Ctx ctx, int seat, List<int> into)
        {
            into.Clear();
            if (!Seated(s, seat)) return;
            var biome = Has(s, ctx, seat, "team_aqua", Blue) ? Biome.Ocean : Has(s, ctx, seat, "team_magma", Blue) ? Biome.Mountain : Biome.None;
            int cell = s.Seats[seat].Cell;
            if (biome == Biome.None || cell < 0 || ctx.Board[cell].Biome != biome) return;
            for (int c = 0; c < ctx.Board.Count; c++)
                if (c != cell && ctx.Board[c].Biome == biome && ctx.Board[c].DistortionWorld == ctx.Board[cell].DistortionWorld) into.Add(c);
        }

        /// <summary>턴을 끝내며 그 칸으로 옮긴다(EndTurn 답의 Target — team_aqua B · team_magma B).</summary>
        internal static void Dive(GameState s, Ctx ctx, Emitter fx, int seat, int to)
        {
            var st = s.Seats[seat];
            int from = st.Cell;
            st.Cell = to;
            st.Moved = true;
            st.Glide = 0;
            st.RoadChain = false;
            Jumped(st);
            Used(s, fx, seat, Blue);
            if (fx.On) fx.Add(new Flew { Seat = seat, From = from, To = to });
        }

        /// <summary>essentia B 「After a battle, you may use any movement you have remaining but cannot interact with anything else this turn」를 가진 좌석인가.</summary>
        public static bool PlansAhead(GameState s, Ctx ctx, int seat) => Seated(s, seat) && Has(s, ctx, seat, "essentia", Blue);

        /// <summary>닫는 중인 턴에서 걸을 수 있나(essentia B — 배틀로 끝난 턴만).</summary>
        public static bool ClosingWalk(GameState s, Ctx ctx, int seat) =>
            s.Seats[seat].Closing == ClosingBase + (int)TurnEndReason.Battle && PlansAhead(s, ctx, seat);

        /// <summary>
        /// 그 좌석의 턴이 끝나려 한다(배틀 뒤 · 규토리 줍기): 턴 끝에 고를 것이 있으면(team_aqua B·team_magma B의 칸 · essentia B의 남은 이동력) 「닫는 MapAction」을 열고 true —
        /// 합법 답은 턴 종료(그대로 · 옮길 칸)와 essentia B의 걷기뿐이고, 턴 종료 답이 미뤄 둔 턴 끝(BattleFlow.FinishClose)을 잇는다. 쓸 포켓몬이 바닥나 귀환한 좌석은 걷지 않는다 [가정].
        /// </summary>
        internal static bool TurnClosing(GameState s, Ctx ctx, Emitter fx, int seat, int after, TurnEndReason reason)
        {
            if (!Seated(s, seat)) return false;
            var st = s.Seats[seat];
            if (st.Closing != 0) return false;
            var dive = new List<int>();
            DiveTargets(s, ctx, seat, dive);
            bool walk = reason == TurnEndReason.Battle && st.MovesLeft > 0 && PlansAhead(s, ctx, seat) && BattleRules.HasUsable(s, ctx, seat);
            if (dive.Count == 0 && !walk) return false;
            st.Closing = ClosingBase + (int)reason;
            st.ClosingAfter = after;
            TurnFlow.OpenMapAction(s, ctx, fx, seat);
            return true;
        }

        // ── worker B: 길 ──

        /// <summary>worker B 「Costs 2 Journey Points: You may build Roads on any non-Ocean tiles you move onto this turn」를 지금 켤 수 있나(턴당 1번 · JP 2).</summary>
        public static bool CanBuild(GameState s, Ctx ctx, int seat) =>
            Seated(s, seat) && !s.Seats[seat].Building && s.Seats[seat].Jp >= RoadJp && Has(s, ctx, seat, "worker", Blue);

        /// <summary>
        /// 길 놓기를 켠다: 「tiles you move onto this turn」 = 그 턴에 밟은 칸 모두 — 켜기 전에 이미 밟은 칸(SeatState.Trod)에도 길을 놓는다(사용자 결정 84 T11 ② 나 — 옛 [가정]은 켠 뒤 칸만).
        /// 서 있던 칸(턴 시작 칸)은 밟은 칸이 아니다.
        /// </summary>
        internal static void Build(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            var st = s.Seats[seat];
            st.Jp -= RoadJp;
            st.Building = true;
            foreach (int cell in st.Trod) Paved(s, ctx, seat, cell);
            st.Trod.Clear();
            Used(s, fx, seat, Blue);
        }

        /// <summary>그 칸을 걸어서 밟았다(MapActions.Step): 길을 놓는 턴이면 바다가 아닌 칸에 길이 놓이고(칸을 표시한다 — 원작 커뮤니티 확인), 아직 켜지 않은 worker B 좌석이면 칸을 적어 둔다.</summary>
        internal static void Paved(GameState s, Ctx ctx, int seat, int cell)
        {
            var st = s.Seats[seat];
            if (!st.Building)
            {
                if (Seated(s, seat) && Has(s, ctx, seat, "worker", Blue) && !st.Trod.Contains(cell)) st.Trod.Add(cell);
                return;
            }
            if (Paves(s, ctx, seat, cell, s.Roads)) s.Roads.Add(cell);
        }

        /// <summary>그 좌석이 이 칸을 밟으면 길이 놓이나(길을 놓는 턴 · 바다 아님 · 아직 길 아님 — roads = 놓인 길, 미리 보기는 사본).</summary>
        internal static bool Paves(GameState s, Ctx ctx, int seat, int cell, List<int> roads) =>
            s.Seats[seat].Building && ctx.Board[cell].Biome != Biome.Ocean && !roads.Contains(cell);

        // ── roller_skater B: 야생의 도망 ──

        /// <summary>
        /// roller_skater B 「non-Legendary wild Pokémon can now only flee after defeating two of your Pokémon」: 내 포켓몬 하나를 쓰러뜨리면 도망치는 야생(약한~강한)이 지금 도망치나 —
        /// 그 좌석이면 이 배틀에서 내 포켓몬이 둘 쓰러진 뒤에만(fallen = 쓰러진 수). 모두 쓰러지면 평소대로 도망친다.
        /// </summary>
        public static bool WildFlees(GameState s, Ctx ctx, BattleState b, int fallen)
        {
            int seat = b.Sides[0].Owner;
            return !Seated(s, seat) || !Has(s, ctx, seat, "roller_skater", Blue) || fallen >= 2;
        }
    }
}
