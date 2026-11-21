using System.Collections.Generic;

namespace Sinnoh.Rules
{
    // ── 이벤트(01 §3-7 「캠페인」 34행) ──

    /// <summary>
    /// 천상의 균열 집계(추종자 공개 — SecretRevealed — 바로 뒤): Seat = 카드를 뽑은 좌석, Source = 그 카드(ShrineEvent), Counts = 신마다 추종자 수[SinnohGod](0 = 추종자 없음),
    /// Tied = 가장 많은 신(게임에 남은 신만 — 빠진 신은 세지 않는다, 신 순서 · 추종자가 0이면 넣지 않는다), TieBreakSeat = 동률이라 고를 좌석(아니면 −1). Tied가 비면 토큰을 놓지 않는다.
    /// </summary>
    public sealed class FollowerTally : GameEvent
    {
        public int[] Counts;
        public int[] Tied;
        public int TieBreakSeat = U;
    }

    /// <summary>
    /// 신 강림(룰북 p.18 「place the God of Sinnoh token … on the Origin Shrine (… replaces any God of Sinnoh token already there)」): Seat = 뽑은 좌석, Source = 그 카드,
    /// God = 놓은 신(SinnohGod), CardKey = 그 신의 폼 1 카드(포켓몬 표 색인 — 토큰 Def), Cell = 시작의 사당 칸, ReplacedGod = 치운 신(없으면 None — 치운 토큰은 앞선 TokenRemoved),
    /// Stayed = 같은 신이 이미 있어 그대로 둠.
    /// </summary>
    public sealed class GodDescended : GameEvent
    {
        public int God = U;
        public int CardKey = U;
        public int Cell = U;
        public int ReplacedGod = U;
        public bool Stayed;
    }

    /// <summary>
    /// 천상의 균열(룰북 p.18 · p.19, 카드 shrine_event.celestial_fracture, rulings A31 · U11, M5 「천상의 균열」). 사당 카드 처리(ShrineRules — 모든 좌석이 운명 1장)에 이어:
    /// ① 추종자 비밀 선택 — 모든 좌석에게 한꺼번에 묻는다(Simultaneous/Follower, Secret, GroupId 하나, Source = 그 카드, Subject = 뽑은 좌석). 다 답할 때까지 남에게는 답이 가려지고(보기 사본·SecretAnswered),
    ///    그동안 남의 추종자도 뒷면이다(<see cref="HideFollowers"/> — 「flip your token face-down and then change the state」). 답 = Pick(신) · 아직 추종자가 아니면 Decline(안 고름)이 첫 답.
    ///    한 번 따르면 「안 고름」으로 못 돌아간다. 기본(시간 초과) = 지금 추종자(처음이면 안 고름).
    /// ② 공개(SecretRevealed) — 고른 신이 그 좌석의 추종자가 되고(SeatState.Follower = 공개된 값), 계속 보인다.
    /// ③ 집계 — 게임에 남은 신(폼 1 카드가 전설 덱에 있다 — 잡히거나 쓰러져 빠진 신은 세지 않는다)마다 추종자 수(FollowerTally). 가장 많은 신이 둘 이상이면 뽑은 좌석이 고른다(Reward/FractureTie, Options = 그 신들).
    ///    아무도(남은 신을) 따르지 않았으면(모두 0) 아무 신도 내려오지 않는다 — 시작의 사당은 그대로다(결정 76 ① R3 나 · 있던 신 토큰은 그대로 둔다 [가정]).
    /// ④ 강림 — 그 신 토큰(TokenKind.God, Def = 폼 1 카드)을 시작의 사당에 둔다. 다른 신 토큰이 있으면 치운다(TokenRemoved — 그 신은 다음 균열에 다시 올 수 있다). 남은 신이 없으면 놓지 않는다.
    ///    다 처리하면 사당 처리를 잇는다(ShrineRules.Resume).
    /// 신 토큰이 시작의 사당에 있는 동안: 그 신의 추종자는 턴마다 이동력 +1(<see cref="MoveBonus"/>, p.18 「All of their Followers gain 1 extra movement per turn」) ·
    /// 시작의 사당에 공물을 못 바친다(HosFlow.GodAtOrigin — 3막 심장 회복 포함) · 그 칸 찾아보기로 신 배틀(LegendRules.SearchSite, rulings A28 ①).
    /// </summary>
    public static class FractureRules
    {
        /// <summary>시작의 사당 신 토큰의 추종자가 받는 이동력(p.18).</summary>
        public const int FollowerMoves = 1;

        static readonly SinnohGod[] Gods = { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina };

        /// <summary>그 신의 폼 1 카드(시작의 사당 고정물의 전설 목록에서). 없으면 −1.</summary>
        public static int GodCard(Ctx ctx, SinnohGod god)
        {
            foreach (int card in ctx.Fixtures.Fixtures[HosFlow.OriginShrine(ctx)].Legendary)
                if (LegendRules.GodOf(ctx, card) == god) return card;
            return -1;
        }

        /// <summary>그 신이 아직 게임에 있나(폼 1 카드가 전설 덱에 있다 — 잡히거나 쓰러지면 빠진다, p.18 「removed from the game once it is defeated or captured」).</summary>
        public static bool InGame(GameState s, Ctx ctx, SinnohGod god)
        {
            int card = GodCard(ctx, god);
            return card >= 0 && s.Decks[(int)DeckId.Legendary].Contains(card);
        }

        /// <summary>시작의 사당에 있는 신 토큰(없으면 null).</summary>
        public static TokenState OriginToken(GameState s, Ctx ctx)
        {
            int cell = HosFlow.OriginShrineCell(ctx);
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.God && t.Cell == cell && !t.Destroyed) return t;
            return null;
        }

        /// <summary>시작의 사당에 있는 신(없으면 None).</summary>
        public static SinnohGod OriginGod(GameState s, Ctx ctx)
        {
            var t = OriginToken(s, ctx);
            return t == null ? SinnohGod.None : LegendRules.GodOf(ctx, t.Def);
        }

        // ── ① 비밀 선택 ──

        /// <summary>
        /// 추종자 비밀 선택을 모든 좌석에게 연다(좌석 번호 순 — 답 적용·공개 순서). 운명 6장째 버리기가 열려 있으면 묶음은 그 뒤로 미뤄진다(ChoiceRules.Hold).
        /// ShrineRules.FractureFollowers가 부른다 — 늘 true(선택을 열었다).
        /// </summary>
        public static bool Open(GameState s, Ctx ctx, Emitter fx, int drawer, int card)
        {
            var src = new SourceRef(SourceKind.ShrineEvent, card);
            var group = new List<PendingChoice>(s.SeatCount);
            for (int i = 0; i < s.SeatCount; i++)
                group.Add(new PendingChoice { Kind = ChoiceKind.Simultaneous, Sub = ChoiceSub.Follower, Seat = i, Secret = true, Source = src, Subject = drawer });
            ChoiceRules.OpenGroup(s, ctx, fx, group);
            return true;
        }

        /// <summary>추종자 비밀 선택이 열려(또는 미뤄져) 있나 — 그동안 남의 추종자 토큰은 뒷면이다.</summary>
        public static bool Choosing(GameState s)
        {
            foreach (var c in s.Pending)
                if (c.Kind == ChoiceKind.Simultaneous && c.Sub == ChoiceSub.Follower) return true;
            foreach (var c in s.Held)
                if (c.Kind == ChoiceKind.Simultaneous && c.Sub == ChoiceSub.Follower) return true;
            return false;
        }

        /// <summary>보기 사본(Engine.ViewFor): 비밀 선택 동안 남의 추종자를 가린다(Unknown). 공개된 값은 그 밖에는 늘 보인다(U11).</summary>
        public static void HideFollowers(GameState s, GameState v, int viewer)
        {
            if (!Choosing(s)) return;
            for (int i = 0; i < v.Seats.Length; i++)
                if (i != viewer) v.Seats[i].Follower = (SinnohGod)Engine.Unknown;
        }

        /// <summary>추종자 묶음(Engine.Answer — 다 답한 뒤 SecretRevealed 다음에 한꺼번에 적용한다).</summary>
        public static bool IsGroup(List<PendingChoice> members)
        {
            foreach (var m in members)
                if (m.Kind != ChoiceKind.Simultaneous || m.Sub != ChoiceSub.Follower) return false;
            return members.Count > 0;
        }

        // ── ②③ 공개 · 집계 ──

        /// <summary>
        /// 묶음 적용: 고른 신을 추종자로(Decline = 그대로 — 아직 추종자가 아님). 천상의 균열 카드가 연 묶음이면 집계 → 강림(동률이면 뽑은 좌석에게 묻는다) → 사당 처리를 잇는다.
        /// </summary>
        public static void ApplyPicks(GameState s, Ctx ctx, Emitter fx, List<PendingChoice> members)
        {
            foreach (var m in members)
                if (m.Answer.Kind == AnswerKind.Pick) s.Seats[m.Seat].Follower = (SinnohGod)m.Answer.Target;
            var first = members[0];
            if (first.Source.Kind != SourceKind.ShrineEvent) return; // 균열 카드가 아닌 묶음(뼈대 시험)은 추종자만
            Tally(s, ctx, fx, first.Subject, first.Source);
        }

        /// <summary>신마다 추종자 수[SinnohGod](0 = 추종자 없음). 아르세우스의 추종자(undivided_acolyte B)는 세 신 모두에 하나씩 센다(「counted as a Follower of all three Gods of Sinnoh at the same time」, rulings A40).</summary>
        public static int[] Counts(GameState s)
        {
            var n = new int[Gods.Length + 1];
            foreach (var st in s.Seats)
            {
                if (st.Follower == SinnohGod.Arceus)
                {
                    foreach (var god in Gods) n[(int)god]++;
                    continue;
                }
                int g = (int)st.Follower;
                if (g >= 0 && g < n.Length) n[g]++;
            }
            return n;
        }

        /// <summary>그 추종자 값이 그 신을 따르나 — 같은 신이거나 아르세우스의 추종자(세 신 모두, rulings A40). None·신이 아닌 값은 false.</summary>
        public static bool Follows(SinnohGod follower, SinnohGod god) => god != SinnohGod.None && god != SinnohGod.Arceus && (follower == god || follower == SinnohGod.Arceus);

        /// <summary>가장 많은 신(게임에 남은 신만, 신 순서). 남은 신이 없거나 남은 신 모두 추종자가 0이면 빈 목록(결정 76 ① R3 나 — 아무 신도 내려오지 않는다).</summary>
        public static List<SinnohGod> Leaders(GameState s, Ctx ctx, int[] counts)
        {
            var r = new List<SinnohGod>(Gods.Length);
            int best = -1;
            foreach (var g in Gods)
            {
                if (!InGame(s, ctx, g)) continue; // 원작 커뮤니티 확인
                if (counts[(int)g] > best)
                {
                    best = counts[(int)g];
                    r.Clear();
                }
                if (counts[(int)g] == best) r.Add(g);
            }
            if (best <= 0) r.Clear(); // 아무도 따르지 않았다: 시작의 사당은 그대로(비어 있으면 비어 있다)
            return r;
        }

        static void Tally(GameState s, Ctx ctx, Emitter fx, int drawer, SourceRef src)
        {
            var counts = Counts(s);
            var tied = Leaders(s, ctx, counts);
            if (fx.On)
            {
                var t = new int[tied.Count];
                for (int i = 0; i < t.Length; i++) t[i] = (int)tied[i];
                fx.Add(new FollowerTally { Seat = drawer, Source = src, Counts = counts, Tied = t, TieBreakSeat = tied.Count > 1 ? drawer : -1 });
            }
            if (tied.Count > 1)
            {
                // 「you choose if tied」 — 뽑은 좌석이 같은 흐름에서 이어 고른다(결정 75 A4 가)
                var c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.FractureTie, Seat = drawer, Source = src };
                foreach (var g in tied) c.Options.Add((int)g);
                ChoiceRules.Open(s, ctx, fx, c);
                return;
            }
            if (tied.Count == 1) Descend(s, ctx, fx, drawer, tied[0], src);
            ShrineRules.Resume(s, ctx, fx);
        }

        /// <summary>동률 고르기의 기본(시간 초과): 뽑은 좌석의 추종자 → 이미 있는 신(바뀌지 않음) → 첫 신 [가정].</summary>
        public static ChoiceAnswer TieDefault(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> legal)
        {
            var mine = ChoiceAnswer.Pick((int)s.Seats[c.Seat].Follower);
            if (ChoiceRules.Contains(legal, mine)) return mine;
            var keep = ChoiceAnswer.Pick((int)OriginGod(s, ctx));
            return ChoiceRules.Contains(legal, keep) ? keep : legal[0];
        }

        /// <summary>동률 고르기의 답(Engine — Reward/FractureTie): 그 신이 강림하고 사당 처리를 잇는다.</summary>
        public static void ApplyTie(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            Descend(s, ctx, fx, c.Seat, (SinnohGod)a.Target, c.Source);
            ShrineRules.Resume(s, ctx, fx);
        }

        // ── ④ 강림 ──

        static void Descend(GameState s, Ctx ctx, Emitter fx, int seat, SinnohGod god, SourceRef src)
        {
            int cell = HosFlow.OriginShrineCell(ctx);
            int card = GodCard(ctx, god);
            var here = OriginToken(s, ctx);
            var replaced = SinnohGod.None;
            bool stayed = here != null && here.Def == card;
            if (!stayed)
            {
                if (here != null)
                {
                    replaced = LegendRules.GodOf(ctx, here.Def);
                    here.Cell = -1; // 「replaces any God of Sinnoh token already there」 — 그 신은 게임에 남는다
                    if (fx.On) fx.Add(new TokenRemoved { Seat = seat, Source = src, Cell = cell, TokenKind = TokenKind.God });
                }
                TokenState token = null;
                foreach (var t in s.Tokens)
                    if (t.Kind == TokenKind.God && t.Def == card) token = t;
                if (token == null) s.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = card, Cell = cell, FaceUp = true });
                else token.Cell = cell;
            }
            if (fx.On) fx.Add(new GodDescended { Seat = seat, Source = src, God = (int)god, CardKey = card, Cell = cell, ReplacedGod = (int)replaced, Stayed = stayed });
        }

        // ── 추종자 이동력 ──

        /// <summary>
        /// 턴 시작 이동력 보정(TurnFlow.StartTurn): 시작의 사당에 그 좌석이 따르는 신의 토큰이 있으면 +1(Mod Source = Rule:GodFollower). 더한 값을 돌려준다.
        /// 턴 시작에만 본다 — 턴 가운데 신이 내려오거나 빠져도 그 턴의 이동력은 그대로 [가정].
        /// </summary>
        public static int MoveBonus(GameState s, Ctx ctx, int seat, List<Mod> mods)
        {
            if (!Follows(s.Seats[seat].Follower, OriginGod(s, ctx))) return 0; // 아르세우스의 추종자는 어느 신이든(rulings A40)
            mods.Add(new Mod { Source = SourceRef.Rule(RuleSource.GodFollower), Delta = FollowerMoves });
            return FollowerMoves;
        }
    }
}
