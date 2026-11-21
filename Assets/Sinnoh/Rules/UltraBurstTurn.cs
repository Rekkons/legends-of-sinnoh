using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>울트라 버스트가 끝난 결과(UbState.Result). Won = 네크로즈마 3페이즈 격파(M6 「네크로즈마·탈락·축복」), Lost = 균열 70% 이상(R8) · 모두 탈락.</summary>
    public enum UbResult { None, Won, Lost }

    /// <summary>
    /// 울트라 버스트의 UB 턴(룰북 p.23·p.25, 01 §8-2, automation R4~R8 · B4, rulings A51 — M6 「UB 턴」). Lua `raw/lua/d66d964ab6.lua` move_ultra_beasts(:611-667)를 1:1로 옮겼다.
    /// 라운드 끝마다 TurnFlow.NextTurn이 <see cref="RoundEnd"/>를 부른다(자동 Seq UbTurn).
    /// ① 총 이동 = 4 + ⌊균열/10⌋(턴 시작 값, :612). ② UB 토큰을 섞어(:621) 한 바퀴씩: UB마다 이웃 여섯 칸을 섞은 순서로(:468) 점수 매겨(:492-503) 처음 나온 최고점 칸을 고른다
    ///   (:505-515 · get_max_key_and_value :215-223 — 엄밀히 큰 것만 바꾼다 = 동점은 섞은 순서 첫째). 고른 칸이 야생 칸일 때만 옮기고 방문 표시를 남기고 센다(:625-632).
    ///   고른 칸의 것은 옮기지 않아도 처리한다(:634 — R5). 한도에 닿거나(:637) 한 바퀴에 아무도 못 옮기면(:640) 멈춘다.
    /// ③ 네크로즈마가 남은 나무·사당 가운데 무작위 ⌈인원/2⌉개를 파괴한다(:563-596, R6). ④ 방문 표시를 지운다 — UB가 서 있는 칸의 표시는 남는다(:446-464).
    /// ⑤ 균열 = round(100 × 파괴 / (사당 + 나무))(:669-674, R7). 턴 시작보다 10% 구간을 넘었고 70% 미만이면 좌석마다 균열 카드(:598-609, B4). ⑥ 70% 이상이면 패배(:652, R8).
    /// 점수표(:411-423 target_priorities — 칸 위의 것이 가진 태그마다 더한다, 세이브 태그로 확인 rulings A51 ②):
    ///   야생 칸 +1000 · 숨겨진 장소·유물 토큰 +10 · 사당 +10 · 나무·산호 +10 · 파괴된 것 −20 · 마을 −100 · 전설(전설 장소 · 전설·신 토큰 · 네크로즈마 토큰) −100 · 트레이너 −100 ·
    ///   다른 UB −200(UB 토큰은 Ultra Beast와 Legendary Encounter Card 태그를 둘 다 가졌다) · 이번 턴 방문 −200 · 맵 밖 = 0.
    /// 높이는 보지 않는다(원작의 탐색은 위아래 5.5 유닛 — 보드 전체 높이).
    /// </summary>
    public static class UbTurn
    {
        public const int WildScore = 1000;
        public const int LureScore = 10;
        public const int ClaimedScore = -20;
        public const int AvoidScore = -100;
        public const int VisitedScore = -200;

        /// <summary>라운드 끝(새 라운드를 열기 전). 자동 Seq(UbTurn)를 열고 UB 턴을 돈다. 게임이 끝났으면 true(다음 라운드를 열지 않는다).</summary>
        public static bool RoundEnd(GameState s, Ctx ctx, Emitter fx)
        {
            if (UbBlessing.AskSkip(s, ctx, fx)) return true; // 축복이 남았으면 「UB 턴을 건너뛰겠습니까?」(사용자 결정 84 U2 — 답이 AfterAsk로 잇는다)
            return Turn(s, ctx, fx);
        }

        /// <summary>UB 턴 하나(또는 축복으로 건너뛰기). 게임이 끝났으면 true.</summary>
        static bool Turn(GameState s, Ctx ctx, Emitter fx)
        {
            fx.Begin(SeatId.System, ActionKind.UbTurn, AutoKind.Auto);
            if (UbBlessing.Skip(s, fx)) return false; // 아르세우스의 축복으로 건너뛴 UB 턴: 이동·네크로즈마의 파괴·균열 카드 딜이 모두 없다(rulings A54 ⑤)
            Run(s, ctx, fx, s.Rng);
            return s.Phase == Phase.GameOver;
        }

        /// <summary>라운드 끝 건너뛰기 물음(UbBlessing.AnsweredSkip · 동의 묶음 셈)이 끝났다: UB 턴을 돌리고 게임이 이어지면 새 라운드를 연다.</summary>
        internal static void AfterAsk(GameState s, Ctx ctx, Emitter fx)
        {
            if (!Turn(s, ctx, fx)) TurnFlow.StartRound(s, ctx, fx);
        }

        /// <summary>나무·산호·사당 토큰인가(균열의 대상 — Lua APRICORN_TREE·SHRINE 태그. 왜곡된 나무·전설 사당은 아니다).</summary>
        public static bool IsTarget(TokenKind k) => k == TokenKind.ApricornTree || k == TokenKind.ApricornReef || k == TokenKind.Shrine;

        /// <summary>지금 균열 강도(%, R7): 보드의 나무·산호·사당 토큰 가운데 파괴된 비율. UbState.Destroyed도 맞춘다.</summary>
        public static int Breach(GameState s)
        {
            int targets = 0, destroyed = 0;
            foreach (var t in s.Tokens)
            {
                if (!IsTarget(t.Kind) || t.Cell < 0) continue;
                targets++;
                if (t.Destroyed) destroyed++;
            }
            if (s.Ub != null) s.Ub.Destroyed = destroyed;
            return targets == 0 ? 0 : UltraBurstRules.BreachStrength(destroyed, targets);
        }

        /// <summary>UB 턴 전체(클래스 설명 ①~⑥). 우연은 모두 rng를 거친다(게임은 GameState.Rng, 시험은 ScriptedRng).</summary>
        public static void Run(GameState s, Ctx ctx, Emitter fx, IRng rng)
        {
            var ub = s.Ub;
            int before = Breach(s);
            ub.BreachAtTurnStart = before;
            int limit = UltraBurstRules.TotalMoves(before);
            if (fx.On) fx.Add(new UbTurnStarted { Seat = SeatId.System, Round = s.Round, TotalMoves = limit, Breach = before });

            var tokens = new List<int>();
            for (int i = 0; i < s.Tokens.Count; i++)
                if (s.Tokens[i].Kind == TokenKind.UltraBeast && s.Tokens[i].Cell >= 0) tokens.Add(i);
            var order = new List<int>(tokens.Count);
            int total = 0;
            while (total < limit)
            {
                int previous = total;
                order.Clear();
                order.AddRange(tokens);
                Piles.Shuffle(order, rng);
                foreach (int token in order)
                {
                    int chosen = Choose(s, ctx, token, rng);
                    if (Step(s, ctx, fx, token, chosen, total, rng)) total++;
                    if (total >= limit) break;
                }
                if (total == previous) break; // 한 바퀴에 아무도 못 옮겼다
            }

            Strike(s, ctx, fx, rng);
            ResetVisited(s);
            int after = Breach(s);
            DealBreachCards(s, ctx, fx, before, after);
            if (UltraBurstRules.IsLost(after)) Lose(s, fx);
        }

        /// <summary>
        /// 그 칸의 점수(클래스 설명의 점수표). 맵 밖(Board.NoCell)은 0. 탈락한 좌석의 말은 세지 않는다 [가정 A51 ②].
        /// </summary>
        public static int Score(GameState s, Ctx ctx, int cell)
        {
            if (cell == Board.NoCell) return 0;
            var c = ctx.Board[cell];
            int p = 0;
            if (c.Wild)
            {
                p += WildScore;
                if (s.Ub != null && s.Ub.Visited.Contains(cell)) p += VisitedScore;
            }
            foreach (var f in ctx.Fixtures.Fixtures)
            {
                if (f.Cell != cell) continue;
                if (f.Kind == FixtureKind.LegendarySite) p += AvoidScore;
                else if (f.Kind == FixtureKind.Village && !c.DistortionWorld) p += AvoidScore; // 울트라메가로폴리스 토큰에는 Village Token 태그가 없다
            }
            foreach (var t in s.Tokens)
            {
                if (t.Cell != cell) continue;
                switch (t.Kind)
                {
                    case TokenKind.ApricornTree:
                    case TokenKind.ApricornReef:
                    case TokenKind.Shrine:
                        p += LureScore;
                        if (t.Destroyed) p += ClaimedScore;
                        break;
                    case TokenKind.HiddenSpot:
                        p += LureScore;
                        break;
                    case TokenKind.Legend:
                    case TokenKind.God:
                    case TokenKind.Necrozma:
                        p += AvoidScore;
                        break;
                    case TokenKind.UltraBeast:
                        p += AvoidScore + AvoidScore; // Ultra Beast + Legendary Encounter Card
                        break;
                }
            }
            foreach (var seat in s.Seats)
                if (seat.Cell == cell && !seat.Eliminated) p += AvoidScore;
            return p;
        }

        /// <summary>
        /// 그 UB 토큰이 고르는 칸: 이웃 여섯 방향을 섞은 순서로 점수 매겨 처음 나온 최고점(엄밀히 큰 것만 바꾼다). 맵 밖이 뽑히면 Board.NoCell.
        /// 섞기 = Piles.Shuffle(방향 0~5, rng) — rng.Next 5번.
        /// </summary>
        public static int Choose(GameState s, Ctx ctx, int token, IRng rng)
        {
            int at = s.Tokens[token].Cell;
            var dirs = new List<int>(Board.Directions);
            for (int d = 0; d < Board.Directions; d++) dirs.Add(d);
            Piles.Shuffle(dirs, rng);
            int chosen = Board.NoCell;
            int best = int.MinValue;
            foreach (int d in dirs)
            {
                int n = ctx.Board.Neighbor(at, d);
                int p = Score(s, ctx, n);
                if (p <= best) continue;
                best = p;
                chosen = n;
            }
            return chosen;
        }

        /// <summary>
        /// 고른 칸을 처리한다: 야생 칸이면 UB를 옮기고 방문 표시(UbMoved, StepIdx = 이번 턴 몇 번째 이동) — 옮겼으면 true. 그 칸의 것은 옮기지 않아도 처리한다(<see cref="Interact"/>).
        /// </summary>
        public static bool Step(GameState s, Ctx ctx, Emitter fx, int token, int chosen, int stepIdx, IRng rng)
        {
            if (chosen == Board.NoCell) return false;
            var t = s.Tokens[token];
            bool moved = false;
            if (ctx.Board[chosen].Wild)
            {
                int from = t.Cell;
                t.Cell = chosen;
                if (!s.Ub.Visited.Contains(chosen)) s.Ub.Visited.Add(chosen);
                if (fx.On) fx.Add(new UbMoved { Seat = SeatId.UltraBeast, UbId = t.Def, From = from, To = chosen, StepIdx = stepIdx });
                moved = true;
            }
            Interact(s, ctx, fx, chosen, t.Def, rng);
            return moved;
        }

        /// <summary>
        /// 그 칸의 것 처리(R5, Lua handle_object_interactions :545-561): 숨겨진 장소·유물 토큰 → 깨어진 세계의 빈 야생 칸(없으면 버림, 뒷면·앞면은 그대로) ·
        /// 나무·산호·사당 → 파괴(조우·공물 불가, 균열 +). byUb = 밟은 UB(포켓몬 표 색인), 네크로즈마의 원격 파괴는 −1.
        /// </summary>
        public static void Interact(GameState s, Ctx ctx, Emitter fx, int cell, int byUb, IRng rng)
        {
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if (t.Cell != cell) continue;
                if (t.Kind == TokenKind.HiddenSpot)
                {
                    int to = FreeDistortionCell(s, ctx, rng);
                    t.Cell = to;
                    if (fx.On) fx.Add(new TokenMoved { Seat = SeatId.UltraBeast, TokenKind = t.Kind, From = cell, To = to });
                }
                else if (IsTarget(t.Kind) && !t.Destroyed) Destroy(s, fx, t, byUb);
            }
        }

        static void Destroy(GameState s, Emitter fx, TokenState t, int byUb)
        {
            int before = Breach(s);
            t.Destroyed = true;
            int after = Breach(s);
            if (!fx.On) return;
            fx.Add(new CellDestroyed { Seat = SeatId.UltraBeast, Cell = t.Cell, TokenKind = t.Kind, ByUbId = byUb });
            if (after != before) fx.Add(new BreachChanged { Seat = SeatId.System, Before = before, After = after });
        }

        /// <summary>
        /// 숨겨진 장소를 보낼 깨어진 세계의 빈 야생 칸(Lua get_safe_wild_area :535-543 — 깨어진 세계 야생 칸을 섞어 처음 안전한 칸). 없으면 −1(버림).
        /// 빈 칸 = 고정물(왜곡된 나무·게이트)이 없고 · 네크로즈마 자리(가운데 — 그 자리의 World Token)가 아니고 · 말이 없고 · 숨겨진 장소·유물 토큰이 없는 칸(is_wild_area_safe :517-533).
        /// </summary>
        public static int FreeDistortionCell(GameState s, Ctx ctx, IRng rng)
        {
            var board = ctx.Board;
            var cells = new List<int>();
            for (int c = 0; c < board.Count; c++)
                if (board[c].Wild && board[c].DistortionWorld) cells.Add(c);
            Piles.Shuffle(cells, rng);
            int centre = UltraBurstSetup.NecrozmaCell(ctx);
            foreach (int c in cells)
            {
                if (c == centre) continue;
                bool free = true;
                foreach (var f in ctx.Fixtures.Fixtures)
                    if (f.Cell == c && f.Kind != FixtureKind.Taxi) free = false;
                foreach (var t in s.Tokens)
                    if (t.Cell == c && t.Kind == TokenKind.HiddenSpot) free = false;
                foreach (var seat in s.Seats)
                    if (seat.Cell == c && !seat.Eliminated) free = false;
                if (free) return c;
            }
            return -1;
        }

        /// <summary>
        /// 네크로즈마의 원격 파괴(R6, Lua ultra_necrozma_attacks :563-596): 나무·산호·사당 토큰을 모두 섞어 아직 파괴되지 않은 것부터 ⌈인원/2⌉개. 남은 것이 모자라면 남은 만큼.
        /// 인원 = 좌석 수(Lua는 앉은 사람 수 — 탈락 좌석도 센다 [가정 A51 ④]). 말이 서 있는 칸도 친다(원작 커뮤니티 확인).
        /// </summary>
        public static void Strike(GameState s, Ctx ctx, Emitter fx, IRng rng)
        {
            var targets = new List<int>();
            for (int i = 0; i < s.Tokens.Count; i++)
                if (IsTarget(s.Tokens[i].Kind) && s.Tokens[i].Cell >= 0) targets.Add(i);
            Piles.Shuffle(targets, rng);
            int count = UltraBurstRules.NecrozmaDestroyCount(s.SeatCount);
            var hit = new List<int>();
            foreach (int i in targets)
            {
                if (hit.Count >= count) break;
                if (!s.Tokens[i].Destroyed) hit.Add(i);
            }
            if (fx.On)
            {
                var cells = new int[hit.Count];
                for (int k = 0; k < cells.Length; k++) cells[k] = s.Tokens[hit[k]].Cell;
                fx.Add(new NecrozmaStrike { Seat = SeatId.UltraBeast, Cells = cells, Count = count });
            }
            foreach (int i in hit) Destroy(s, fx, s.Tokens[i], -1);
        }

        /// <summary>방문 표시 지우기(Lua reset_already_visited_tags :446-464): UB가 서 있는 칸의 표시만 남긴다.</summary>
        public static void ResetVisited(GameState s)
        {
            var v = s.Ub.Visited;
            for (int i = v.Count - 1; i >= 0; i--)
            {
                bool stands = false;
                foreach (var t in s.Tokens)
                    if (t.Kind == TokenKind.UltraBeast && t.Cell == v[i]) stands = true;
                if (!stands) v.RemoveAt(i);
            }
        }

        /// <summary>
        /// 균열 카드 딜(R8 · B4, Lua handle_breach_cards :598-609): 턴 시작보다 넘은 10% 구간 수만큼(옵션 OncePerTurn이면 1장) 좌석마다 왜곡 균열 덱 맨 위에서 — 70% 이상이면 주지 않는다.
        /// 받은 카드는 좌석의 손(UbState.BreachHands)에 둔다 — 붙이기는 M6 「균열 카드 36장」. 덱이 떨어지면 버림 더미를 섞어 잇고, 그래도 없으면 남은 만큼만.
        /// 좌석 순서 = 좌석 번호. 탈락 좌석도 받는다(Lua deal(1)은 앉은 사람 모두 [가정 A51 ⑦]).
        /// </summary>
        public static void DealBreachCards(GameState s, Ctx ctx, Emitter fx, int before, int after)
        {
            int per = UltraBurstRules.BreachCardsPerSeat(before, after, s.Options.BreachCardsPerBracket);
            if (per <= 0) return;
            if (fx.On) fx.Add(new BreachBracket { Seat = SeatId.System, Before = before, After = after, Brackets = after / 10 - before / 10 });
            var deck = s.Decks[(int)DeckId.DistortionBreach];
            var discard = s.Discards[(int)DeckId.DistortionBreach];
            var deals = new BreachDeal[s.SeatCount];
            for (int seat = 0; seat < s.SeatCount; seat++)
            {
                var cards = new List<int>();
                for (int k = 0; k < per; k++)
                {
                    if (deck.Count == 0 && discard.Count > 0)
                    {
                        deck.AddRange(discard);
                        discard.Clear();
                        Piles.Shuffle(deck, s.Rng);
                        if (fx.On) fx.Add(new PileShuffled { Seat = SeatId.System, Pile = DeckId.DistortionBreach });
                    }
                    if (deck.Count == 0) break;
                    int card = deck[deck.Count - 1];
                    deck.RemoveAt(deck.Count - 1);
                    s.Ub.BreachHands.Add(seat);
                    s.Ub.BreachHands.Add(card);
                    cards.Add(card);
                }
                deals[seat] = new BreachDeal { Seat = seat, CardKeys = cards.ToArray() };
            }
            if (fx.On) fx.Add(new BreachCardsDealt { Seat = SeatId.System, PerSeat = deals });
        }

        /// <summary>그 좌석이 손에 든 균열 카드(카드 색인, 받은 순서).</summary>
        public static List<int> BreachHand(GameState s, int seat)
        {
            var r = new List<int>();
            if (s.Ub == null) return r;
            for (int i = 0; i + 1 < s.Ub.BreachHands.Count; i += 2)
                if (s.Ub.BreachHands[i] == seat) r.Add(s.Ub.BreachHands[i + 1]);
            return r;
        }

        /// <summary>균열 70% 이상(R8 — 원작 커뮤니티 확인): 모두 패배. 이긴 좌석 없이 게임이 끝난다.</summary>
        static void Lose(GameState s, Emitter fx)
        {
            s.Phase = Phase.GameOver;
            s.Ub.Result = UbResult.Lost;
            if (fx.On) fx.Add(new GameEnded { Seat = SeatId.System, Reason = GameEndReason.BreachLost, Winners = new int[0] });
        }
    }
}
