using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 트레이너 카드 — 맵 상호작용 7(M6 묶음 6, rulings A37): gardener B(선 나무에서 규토리 1) · kindler B(나무 하나를 없애고 장작 3) · psychic B(2칸 떨어진 것과 상호작용) ·
    /// ruin_maniac B(특별한 숨겨진 장소 하나 공개 — 유물이면 가서 붙임) · scientist B(배틀 없는 찾아보기 + 찾은 포켓몬의 기술 가르치기) · scuba_diver B(바다 칸에서 숨겨진 장소 버림 더미 한 장) ·
    /// veteran B(규토리 더미 +2 · 이동 +1). 앞 여섯은 「Once per turn」 — 맵 행동의 UseAbility 답(뜻은 그 좌석의 카드가 정한다, <see cref="MapAbilities"/>·<see cref="UseMap"/>),
    /// 기록은 GameState.Used의 UsageKind.Ability(턴이 바뀌면 지운다). 배틀 밖 물음은 효과 프레임이 아니라(프레임은 배틀 안에서만 돈다) 출처가 카드인 선택을 열고 답은 <see cref="MapAnswered"/>가 받는다.
    /// </summary>
    public static partial class TrainerRules
    {
        /// <summary>kindler B 「add 3 Burned status effects to your bag as Firewood」.</summary>
        public const int FirewoodPerTree = 3;
        /// <summary>veteran B 「gain 2 extra Apricorns of the same type and move 1 additional tile this turn」.</summary>
        public const int StashExtra = 2, StashMoves = 1;
        /// <summary>psychic B 「from up to 2 tiles away」.</summary>
        public const int ReachTiles = 2;

        static bool MapUsed(GameState s, int seat) => MapActions.Used(s, seat, UsageKind.Ability, SlotOf(Blue));

        static void MapUse(GameState s, Emitter fx, int seat, params int[] targets)
        {
            MapActions.Mark(s, fx, seat, UsageKind.Ability, SlotOf(Blue), false);
            Used(s, fx, seat, Blue, targets);
        }

        /// <summary>그 칸의 규토리 나무 토큰(산호·왜곡된 나무는 「Apricorn Tree」가 아니다 [가정]). 없으면 −1.</summary>
        static int TreeAt(GameState s, int cell)
        {
            int t = MapActions.EncounterTokenAt(s, cell);
            return t >= 0 && s.Tokens[t].Kind == TokenKind.ApricornTree ? t : -1;
        }

        /// <summary>scientist B가 그 칸에서 찾아볼 곳: 나무·산호 토큰(Gate = −1) 또는 왜곡 게이트 고정물(token = −1). 없으면 false.</summary>
        static bool FieldSite(GameState s, Ctx ctx, int cell, out int token, out int gate)
        {
            gate = -1;
            token = MapActions.EncounterTokenAt(s, cell);
            if (token >= 0 && s.Tokens[token].Kind != TokenKind.DistortedTree) return true;
            token = -1;
            var fixtures = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
                if (fixtures[f].Kind == FixtureKind.Gate && fixtures[f].Cell == cell)
                {
                    gate = f;
                    return true;
                }
            return false;
        }

        /// <summary>
        /// 숨겨진 장소 버림 더미(scuba_diver B)의 토큰 색인: 공개된 뒤 맵을 떠난 규토리 더미, 그리고 버려진 우두머리 토큰(맵 밖 앞면 우두머리 토큰 수 − 지금 우두머리 토큰이 붙어 있는 포켓몬 수 —
        /// 원작 커뮤니티 확인). 유물(가방·지닌 것)·떨어진 별(별 자리에 놓인다)·야영지(맵에 남는다)는 버림 더미가 아니다.
        /// </summary>
        public static List<int> SunkenPile(GameState s, Ctx ctx)
        {
            var pile = new List<int>();
            int attached = 0;
            foreach (var p in s.Pokemon)
                if (p.Def >= 0 && p.Alpha) attached++;
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if (t.Kind != TokenKind.HiddenSpot || t.Cell >= 0 || !t.FaceUp || t.Destroyed || t.Def < 0) continue;
                if (HiddenSpotRules.Stash(ctx, t.Def, out _, out _)) pile.Add(i);
                else if (ctx.Items.Items[t.Def].Kind == ItemKind.AlphaPokemon)
                {
                    if (attached > 0) attached--;
                    else pile.Add(i);
                }
            }
            return pile;
        }

        /// <summary>psychic B가 닿는 칸: 지금 칸에서 이웃을 따라 2칸 안(높이는 보지 않는다 — 걷는 길이 아니라 맵의 칸 수 [가정]), 지금 칸 제외, 칸 색인 순서.</summary>
        public static List<int> ReachCells(Ctx ctx, int from)
        {
            var cells = new List<int>();
            if (from < 0) return cells;
            var ring = new List<int> { from };
            for (int step = 0; step < ReachTiles; step++)
            {
                var next = new List<int>();
                foreach (int c in ring)
                    for (int d = 0; d < Board.Directions; d++)
                    {
                        int n = ctx.Board.Neighbor(c, d);
                        if (n == Board.NoCell || n == from || cells.Contains(n)) continue;
                        cells.Add(n);
                        next.Add(n);
                    }
                ring = next;
            }
            cells.Sort();
            return cells;
        }

        /// <summary>
        /// psychic B로 다른 좌석과 거래할 수 있나(사용자 결정 84 T12 ② 나 — 원작 커뮤니티 확인 가 「마을에서 회복 · 공중날기 · 거래 등」을 물은 말에 붙었다):
        /// 그 좌석의 칸이 2칸 안(<see cref="ReachCells"/>)이고, unused면 이번 턴에 능력을 아직 안 썼다. 거래 내용은 늘어놓을 수 없어 <see cref="ReachAnswer"/>로 감싸지 않고 TradeRules의 방식 Telekinesis로 받는다.
        /// </summary>
        public static bool TelekinesisTo(GameState s, Ctx ctx, int seat, int target, bool unused)
        {
            if (!Seated(s, seat) || !Has(s, ctx, seat, "psychic", Blue) || (unused && MapUsed(s, seat))) return false;
            int cell = s.Seats[target].Cell;
            return cell >= 0 && ReachCells(ctx, s.Seats[seat].Cell).Contains(cell);
        }

        /// <summary>psychic B를 떨어진 좌석과의 거래 제안으로 썼다(턴당 1번 · AbilityUsed — Targets = 그 칸).</summary>
        internal static void Telekinesis(GameState s, Emitter fx, int seat, int cell) => MapUse(s, fx, seat, cell);

        /// <summary>psychic B의 답 하나: UseAbility(Target = 닿는 칸, Arg = 그 칸에서 할 답의 종류, Values = [그 답의 Target, Arg, 그 답의 Values…]) — 안쪽 Values(공물의 JP 7 등)도 싣는다(그래야 「JP 7 더 낸 공물」이 보통 공물과 다른 답이다).</summary>
        public static ChoiceAnswer ReachAnswer(int cell, ChoiceAnswer inner)
        {
            var a = ChoiceAnswer.Of(AnswerKind.UseAbility, cell, (int)inner.Kind);
            a.Values.Add(inner.Target);
            a.Values.Add(inner.Arg);
            a.Values.AddRange(inner.Values);
            return a;
        }

        /// <summary>psychic B 답의 안쪽 답(<see cref="ReachAnswer"/>의 거꾸로).</summary>
        public static ChoiceAnswer ReachInner(ChoiceAnswer a)
        {
            var inner = ChoiceAnswer.Of((AnswerKind)a.Arg, a.Values[0], a.Values[1]);
            for (int i = 2; i < a.Values.Count; i++) inner.Values.Add(a.Values[i]);
            return inner;
        }

        /// <summary>
        /// 맵 행동의 합법 답에 그 좌석 카드의 맵 상호작용 능력을 더한다(MapActions.Fill — 이번 턴에 아직 안 썼을 때만). interact = 칸 상호작용을 할 수 있다(G11).
        /// gardener B = UseAbility(−1, 색) · kindler B = UseAbility(나무 토큰) · psychic B = <see cref="ReachAnswer"/> · ruin_maniac B = UseAbility(뒷면 특별한 숨겨진 장소 토큰) ·
        /// scientist B · scuba_diver B = UseAbility().
        /// </summary>
        internal static void MapAbilities(GameState s, Ctx ctx, int seat, bool interact, List<ChoiceAnswer> into)
        {
            if (!Seated(s, seat)) return;
            if (Has(s, ctx, seat, "tourist", Blue))
            {
                TourAnswers(s, ctx, seat, into); // 「Once at each Artifact Legendary Location」 — 턴 제한 없음(rulings A41)
                return;
            }
            if (MapUsed(s, seat)) return;
            var st = s.Seats[seat];
            int cell = st.Cell;
            if (Has(s, ctx, seat, "gardener", Blue))
            {
                int tree = interact ? TreeAt(s, cell) : -1;
                if (tree < 0) return;
                var seen = new List<int>(1);
                foreach (var color in ctx.Fixtures.Fixtures[s.Tokens[tree].Def].Apricorns)
                {
                    if (seen.Contains((int)color)) continue;
                    seen.Add((int)color);
                    into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility, -1, (int)color));
                }
            }
            else if (Has(s, ctx, seat, "kindler", Blue))
            {
                for (int i = 0; i < s.Tokens.Count; i++)
                    if (s.Tokens[i].Kind == TokenKind.ApricornTree && s.Tokens[i].Cell >= 0 && !s.Tokens[i].Destroyed) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility, i));
            }
            else if (Has(s, ctx, seat, "psychic", Blue))
            {
                if (!interact) return;
                var inner = new List<ChoiceAnswer>();
                foreach (int c in ReachCells(ctx, cell))
                {
                    // 그 칸에 선 것처럼 세어 본다(원작 커뮤니티 확인) — 상태는 바로 되돌린다
                    inner.Clear();
                    st.Cell = c;
                    try
                    {
                        MapActions.Interactions(s, ctx, seat, inner);
                    }
                    finally
                    {
                        st.Cell = cell;
                    }
                    foreach (var a in inner) into.Add(ReachAnswer(c, a));
                }
            }
            else if (Has(s, ctx, seat, "ruin_maniac", Blue))
            {
                for (int i = 0; i < s.Tokens.Count; i++)
                {
                    var t = s.Tokens[i];
                    if (t.Kind == TokenKind.HiddenSpot && t.Special && !t.FaceUp && t.Cell >= 0 && !t.Destroyed) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility, i));
                }
            }
            else if (Has(s, ctx, seat, "scientist", Blue))
            {
                if (interact && FieldSite(s, ctx, cell, out _, out _)) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility));
            }
            else if (Has(s, ctx, seat, "scuba_diver", Blue))
            {
                if (ctx.Board[cell].Biome == Biome.Ocean && SunkenPile(s, ctx).Count > 0) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility));
            }
        }

        /// <summary>psychic B로 닿아 있던 좌석을 제 칸으로 되돌린다(맵 행동을 다시 열기 전 · 턴 끝 · 그 답을 다 적용한 뒤).</summary>
        internal static void Unreach(GameState s)
        {
            foreach (var st in s.Seats)
            {
                if (st.ReachFrom < 0) continue;
                st.Cell = st.ReachFrom;
                st.ReachFrom = -1;
            }
        }

        /// <summary>맵 행동의 UseAbility 답을 그 좌석 카드의 맵 상호작용 능력으로 적용한다. 그런 카드가 아니면 false(picnicker B · worker B — MapActions가 잇는다).</summary>
        internal static bool UseMap(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            int seat = c.Seat;
            if (!Seated(s, seat)) return false;
            var st = s.Seats[seat];
            if (Has(s, ctx, seat, "tourist", Blue))
            {
                Stamp(s, ctx, fx, seat, a.Target); // 랜드마크 투어 도장 · 전설 스냅(rulings A41)
                MapActions.Continue(s, ctx, fx, seat);
                return true;
            }
            if (Has(s, ctx, seat, "gardener", Blue))
            {
                // 「Once per turn, you may gain 1 Apricorn from an Apricorn Tree you are standing on.」
                int before = st.Apricorns[a.Arg];
                st.Apricorns[a.Arg]++;
                MapUse(s, fx, seat);
                if (fx.On)
                    fx.Add(new ApricornsGained
                    {
                        Seat = seat, Source = CardSource(s, seat),
                        Changes = new[] { new ResChange { Seat = seat, Res = ResKind.Apricorn, Key = a.Arg, Before = before, After = before + 1 } },
                    });
                MapActions.Continue(s, ctx, fx, seat);
                return true;
            }
            if (Has(s, ctx, seat, "kindler", Blue))
            {
                // 「Once per turn, you may remove any Apricorn Tree from the map to add 3 Burned status effects to your bag as Firewood.」
                var t = s.Tokens[a.Target];
                int cell = t.Cell;
                t.Cell = -1;
                st.Firewood += FirewoodPerTree;
                MapUse(s, fx, seat);
                if (fx.On) fx.Add(new TokenRemoved { Seat = seat, Source = CardSource(s, seat), Cell = cell, TokenKind = TokenKind.ApricornTree });
                MapActions.Continue(s, ctx, fx, seat);
                return true;
            }
            if (Has(s, ctx, seat, "psychic", Blue))
            {
                // 「Once per turn, you may interact with something from up to 2 tiles away.」: 그 칸에 선 것처럼 그 답을 적용한다 — 말은 움직이지 않는다
                var inner = ReachInner(a);
                MapUse(s, fx, seat, a.Target);
                st.ReachFrom = st.Cell;
                st.Cell = a.Target;
                if (inner.Kind == AnswerKind.Accept)
                {
                    // 떨어진 숨겨진 장소를 뒤집는다 — 우두머리면 싸워야 한다(원작 커뮤니티 확인)
                    if (!MapActions.Reveal(s, ctx, fx, seat, inner.Target)) MapActions.Continue(s, ctx, fx, seat);
                }
                else MapActions.Apply(s, ctx, fx, c, inner);
                Unreach(s);
                return true;
            }
            if (Has(s, ctx, seat, "ruin_maniac", Blue))
            {
                // 「Once per turn, you may reveal 1 Special Hidden Spot anywhere on the map. If it is an Artifact, you may instantly move to it and attach it to a Pokémon in your party.」
                var t = s.Tokens[a.Target];
                MapUse(s, fx, seat, t.Cell);
                t.FaceUp = true;
                if (fx.On) fx.Add(new HiddenSpotRevealed { Seat = seat, Source = CardSource(s, seat), Cell = t.Cell, SpotKey = t.Def });
                if (!RelicRules.IsRelic(ctx, t.Def))
                {
                    // 우두머리: 앞면으로 맵에 남는다 — 강제 배틀이 아니다(원작 커뮤니티 확인)
                    MapActions.Continue(s, ctx, fx, seat);
                    return true;
                }
                t.Loose = true; // 가지 않으면 앞면 유물로 남는다 — 그 칸에 온 좌석이 줍는다
                var holders = new List<int>(6);
                foreach (int p in st.Party)
                    if (p >= 0 && !s.Pokemon[p].Alpha) holders.Add(p); // 우두머리 토큰은 지닌물건 칸을 막는다(A17)
                if (holders.Count == 0)
                {
                    MapActions.Continue(s, ctx, fx, seat);
                    return true;
                }
                var ask = new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.EffectPick, Seat = seat, Source = CardSource(s, seat), Subject = a.Target, Count = 0 };
                ask.Options.AddRange(holders);
                ChoiceRules.Open(s, ctx, fx, ask);
                return true;
            }
            if (Has(s, ctx, seat, "scientist", Blue))
            {
                Observe(s, ctx, fx, seat);
                return true;
            }
            if (Has(s, ctx, seat, "scuba_diver", Blue))
            {
                // 「Once per turn whilst on an Ocean tile, shuffle the Hidden Spot discard pile and then draw the top Hidden Spot. Place the drawn Hidden Spot on the tile you are on, you may then interact with it as usual.」
                var pile = SunkenPile(s, ctx);
                int pick = s.Rng.Next(pile.Count);
                var t = s.Tokens[pile[0]];
                var drawn = s.Tokens[pile[pick]];
                // 섞어 뽑은 한 장: 버림 더미의 첫 토큰에 뽑힌 정체를 옮겨 놓는다(토큰 색인으로 뒷면 정체가 새지 않게)
                int def = t.Def;
                bool special = t.Special;
                t.Def = drawn.Def;
                t.Special = drawn.Special;
                drawn.Def = def;
                drawn.Special = special;
                t.Cell = st.Cell;
                t.FaceUp = false;
                MapUse(s, fx, seat, st.Cell);
                if (fx.On) fx.Add(new TokensPlaced { Seat = seat, Source = CardSource(s, seat), Kind = TokenKind.HiddenSpot, Cells = new[] { st.Cell }, Villages = new int[0] });
                // 뽑은 것과 상호작용은 선택(원작 커뮤니티 확인) — 걸어서 닿았을 때처럼 묻는다
                if (MapActions.CanInteract(s, seat)) MapActions.OpenSpot(s, ctx, fx, seat, pile[0], null);
                else MapActions.Continue(s, ctx, fx, seat);
                return true;
            }
            return false;
        }

        /// <summary>
        /// scientist B 「Once per turn, search at an Apricorn Tree, Apricorn Reef or Distortion Gate without starting a battle. You may now directly teach all the moves searched to any Pokémon in your party
        /// that can learn them.」: 보통 찾아보기처럼 뽑고(나무·산호 = 등급 장수 · 게이트 = 무리 셋) 카드는 덱으로 돌려보낸 뒤(원작 커뮤니티 확인) 찾은 포켓몬의 시그니처 기술을 하나씩 가르칠지 묻는다.
        /// 배틀이 아니다 — 보상·규토리 없음 · 턴이 끝나지 않음 · 빈 찾아보기 표시도 없다(원작 커뮤니티 확인).
        /// </summary>
        static void Observe(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            int cell = s.Seats[seat].Cell;
            FieldSite(s, ctx, cell, out int token, out int gate);
            var board = ctx.Board[cell];
            MapUse(s, fx, seat, cell);
            if (fx.On) fx.Add(new Searched { Seat = seat, Source = CardSource(s, seat), Cell = cell, Biome = board.Biome, Climate = board.Climate });
            List<int> drawn;
            if (gate >= 0) drawn = LegendRules.DrawSwarm(s, ctx, gate);
            else
            {
                drawn = new List<int>();
                var tier = ctx.Fixtures.Fixtures[s.Tokens[token].Def].Tier.Value;
                for (int i = 0; i < MapActions.SearchCount(tier); i++)
                {
                    int card = MapActions.DrawMatching(s, ctx, MapActions.DeckOf(tier), board.Biome, board.Climate, false);
                    if (card < 0) break;
                    drawn.Add(card);
                }
            }
            var moves = new List<int>();
            var returns = new int[3]; // 약한·적당한·강한
            var cards = new GradedCard[drawn.Count];
            for (int i = 0; i < drawn.Count; i++)
            {
                var grade = MapActions.GradeOf(ctx, drawn[i]);
                cards[i] = new GradedCard { Grade = grade, CardKey = drawn[i] };
                BattleFlow.AddTeach(ctx, drawn[i], moves);
                s.Decks[(int)MapActions.DeckOf(grade)].Add(drawn[i]);
                returns[(int)MapActions.DeckOf(grade) - (int)DeckId.Weak]++;
            }
            if (fx.On)
            {
                fx.Add(new EncounterDrawn { Seat = seat, Source = CardSource(s, seat), Cards = cards });
                var list = new List<PileCount>();
                for (int k = 0; k < returns.Length; k++)
                    if (returns[k] > 0) list.Add(new PileCount { Pile = DeckId.Weak + k, Count = returns[k] });
                if (list.Count > 0) fx.Add(new CardsReturned { Seat = seat, Returns = list.ToArray() });
            }
            if (!OpenTeach(s, ctx, fx, seat, moves)) MapActions.Continue(s, ctx, fx, seat);
        }

        /// <summary>남은 기술 가운데 가르칠 수 있는 첫 기술을 묻는다(Reward/TeachMove, 출처 = 카드 — Options = 그 뒤 기술, 음수 = 앞 기술과 같은 포켓몬의 다른 폼 시그니처). 열었으면 true.</summary>
        static bool OpenTeach(GameState s, Ctx ctx, Emitter fx, int seat, List<int> moves)
        {
            var legal = new List<ChoiceAnswer>();
            for (int i = 0; i < moves.Count; i++)
            {
                int move = moves[i] < 0 ? ~moves[i] : moves[i];
                var c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.TeachMove, Seat = seat, Subject = move, Source = CardSource(s, seat) };
                ChoiceRules.Legal(s, ctx, c, legal);
                if (legal.Count <= 1) continue;
                c.Options.AddRange(moves.GetRange(i + 1, moves.Count - i - 1));
                ChoiceRules.Open(s, ctx, fx, c);
                return true;
            }
            return false;
        }

        /// <summary>배틀 밖에서 트레이너 카드가 연 선택(출처 = 카드)의 답: scientist B의 기술 가르치기(Reward/TeachMove) · ruin_maniac B의 가서 붙이기(Reaction/EffectPick — Subject = 토큰).</summary>
        internal static void MapAnswered(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            if (UnlockAnswered(s, ctx, fx, c, a)) return; // 미뤄 둔 「When unlocked」 물음(rulings A40)
            if (RewardAnswered(s, ctx, fx, c, a)) return; // backpacker B · interviewers B(rulings A38)
            int seat = c.Seat;
            if (c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.TeachMove)
            {
                BattleFlow.Teach(s, ctx, fx, c, a);
                var rest = new List<int>(c.Options);
                if (a.Kind == AnswerKind.Pick)
                    while (rest.Count > 0 && rest[0] < 0) rest.RemoveAt(0); // 하나를 가르쳤으면 그 포켓몬의 다른 폼 시그니처는 묻지 않는다
                if (!OpenTeach(s, ctx, fx, seat, rest)) MapActions.Continue(s, ctx, fx, seat);
                return;
            }
            if (c.Sub == ChoiceSub.EffectPick && Seated(s, seat) && Has(s, ctx, seat, "ruin_maniac", Blue) && a.Kind == AnswerKind.Pick)
            {
                // 「you may instantly move to it and attach it to a Pokémon in your party」
                var st = s.Seats[seat];
                var t = s.Tokens[c.Subject];
                int from = st.Cell, item = t.Def;
                if (t.Cell >= 0 && from != t.Cell)
                {
                    st.Cell = t.Cell;
                    st.Moved = true;
                    st.Glide = 0;
                    st.RoadChain = false;
                    Jumped(st);
                    if (fx.On) fx.Add(new Flew { Seat = seat, Source = CardSource(s, seat), From = from, To = t.Cell });
                }
                HiddenSpotRules.Resolve(s, ctx, fx, seat, c.Subject); // 유물은 가방으로, 토큰은 맵에서 빠진다
                MaintenanceRules.Hold(s, ctx, fx, seat, a.Target, item);
                StampRules.Sweep(s, ctx, fx); // 지니게 하기로 채운 스탬프(rulings A30)
            }
            MapActions.Continue(s, ctx, fx, seat);
        }

        /// <summary>
        /// veteran B 「When you find an Apricorn Stash, gain 2 extra Apricorns of the same type and move 1 additional tile this turn」: 규토리 더미를 공개한 좌석이면 같은 색 +2, 이번 턴 이동력 +1
        /// (HiddenSpotRules.Resolve가 더미의 규토리를 준 바로 뒤에 부른다 — 강제).
        /// </summary>
        internal static void Stashed(GameState s, Ctx ctx, Emitter fx, int seat, ApricornColor color)
        {
            if (!Seated(s, seat) || !Has(s, ctx, seat, "veteran", Blue)) return;
            var st = s.Seats[seat];
            int before = st.Apricorns[(int)color];
            st.Apricorns[(int)color] += StashExtra;
            st.MovesLeft += StashMoves;
            st.MoveBonus += StashMoves;
            Used(s, fx, seat, Blue);
            if (fx.On)
                fx.Add(new ApricornsGained
                {
                    Seat = seat, Source = CardSource(s, seat),
                    Changes = new[] { new ResChange { Seat = seat, Res = ResKind.Apricorn, Key = (int)color, Before = before, After = before + StashExtra } },
                });
        }
    }
}
