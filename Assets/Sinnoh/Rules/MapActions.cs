using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 맵 행동(01 §3-2 MapAction, M3-d): 이동(S3_1, A6, G7), 찾아보기·조우(R1·R2, S5_1), 규토리 줍기(p.27), 숨겨진 장소 멈춤·공개·지나가기(A23),
    /// 마을 행동 전체 회복(S3_3)·공중날기(A5), 도전(G7·G13, 1막 조무래기 A1 — 배틀은 BattleFlow, M3-e), 턴 끝. 칸 행동의 이동력 조건은 rulings G11, 세부는 G12.
    /// 나머지 답(사당 M5, 은행상회 M5, 제작·진화·운명 M5, 카드 능력 M6, 거래 M5)은 그 작업이 Legal과 Apply에 더한다.
    /// Legal 순서(G9): 턴 끝(안 함) → 이동(칸 색인) → 찾아보기 → 규토리 줍기 → 사당 공물(ShrineRules — 규토리 색 순서, 그다음 JP 7의 3장 뽑기) → 전체 회복 → 은행상회(위 줄 · 아래 줄 · 두 줄, BankRules)
    /// → 공중날기(마을 데이터 순서 × 규토리 색 순서) → 제작(아이템 데이터 순서, CraftRules) → 진화(파티 순서, EvolveRules) → 도전(갤럭시단 → 좌석은 다음부터 시계 방향).
    /// </summary>
    public static class MapActions
    {
        /// <summary>찾아보기에서 등급마다 뽑는 장수(p.4 약한 3 / 적당한 2 / 강한 1, Lua R1).</summary>
        public static int SearchCount(EncounterTier tier)
        {
            switch (tier)
            {
                case EncounterTier.Weak: return 3;
                case EncounterTier.Moderate: return 2;
                case EncounterTier.Strong: return 1;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, "나무·산호 등급");
            }
        }

        /// <summary>조우 등급 → 덱.</summary>
        public static DeckId DeckOf(EncounterTier tier)
        {
            switch (tier)
            {
                case EncounterTier.Weak: return DeckId.Weak;
                case EncounterTier.Moderate: return DeckId.Moderate;
                case EncounterTier.Strong: return DeckId.Strong;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, "찾아보기 덱이 아니다");
            }
        }

        /// <summary>찾아보기 덱에서 나온 카드의 등급(덱). 셋업 뒤 약한 덱에 든 스타터는 약한 등급이다(R15).</summary>
        public static EncounterTier GradeOf(Ctx ctx, int def)
        {
            var t = ctx.Cards.Pokemon[def].Tier;
            return t == EncounterTier.Starter ? EncounterTier.Weak : t;
        }

        /// <summary>칸에 선 맵 상호작용(찾아보기·규토리 줍기·숨겨진 장소)을 할 수 있나(rulings G11).</summary>
        public static bool CanInteract(GameState s, int seat) => s.Options.InteractAtZeroMovement || s.Seats[seat].MovesLeft > 0;

        /// <summary>칸의 조우 토큰(나무·산호·왜곡된 나무, 파괴 안 됨). 없으면 −1.</summary>
        public static int EncounterTokenAt(GameState s, int cell)
        {
            if (cell < 0) return -1;
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if (t.Cell != cell || t.Destroyed) continue;
                if (t.Kind == TokenKind.ApricornTree || t.Kind == TokenKind.ApricornReef || t.Kind == TokenKind.DistortedTree) return i;
            }
            return -1;
        }

        /// <summary>
        /// 칸에서 멈춰 물을 숨겨진 장소 토큰: 뒷면, 또는 공개만 되고 아직 줍지 않은 유물(ruin_maniac B가 공개하고 가지 않은 것 — 그 칸에 온 좌석이 줍는다 [가정, rulings A37]). 없으면 −1.
        /// </summary>
        public static int FaceDownSpotAt(GameState s, int cell)
        {
            if (cell < 0) return -1;
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if (t.Kind == TokenKind.HiddenSpot && t.Cell == cell && !t.Destroyed && (!t.FaceUp || t.Loose)) return i;
            }
            return -1;
        }

        /// <summary>마을 행동을 하는 곳과 회복 출처: 마을 / 울트라메가로폴리스(깨어진 세계의 마을) / 앞면 야영지. 아니면 false.</summary>
        public static bool VillagePlace(GameState s, Ctx ctx, int cell, out RuleSource source, out int village)
        {
            source = RuleSource.VillageHeal;
            village = -1;
            if (cell < 0) return false;
            var fixtures = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
            {
                if (fixtures[f].Kind != FixtureKind.Village || fixtures[f].Cell != cell) continue;
                village = f;
                source = ctx.Board[cell].DistortionWorld ? RuleSource.MegalopolisHeal : RuleSource.VillageHeal;
                return true;
            }
            int camp = ctx.Items.Ids.IndexOf("hidden_spot.campsite");
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.HiddenSpot && t.FaceUp && t.Cell == cell && t.Def == camp && !t.Destroyed)
                {
                    source = RuleSource.CampHeal;
                    return true;
                }
            return false;
        }

        internal static bool Used(GameState s, int seat, UsageKind kind, int key = -1)
        {
            foreach (var u in s.Used)
                if (u.Seat == seat && u.Kind == kind && u.Key == key && u.Scope == s.TurnId) return true;
            return false;
        }

        internal static void Mark(GameState s, Emitter fx, int seat, UsageKind kind, int key = -1, bool announce = true)
        {
            s.Used.Add(new UsageMark { Seat = seat, Kind = kind, Key = key, Scope = s.TurnId });
            if (announce && fx.On) fx.Add(new UsageMarked { Seat = seat, Kind = kind });
        }

        /// <summary>공중날기를 쓸 수 있는 마을(A5): 마을이고, 울트라메가로폴리스가 아니고(p.26), 조무래기가 없다.</summary>
        static bool FlyVillage(GameState s, Ctx ctx, int fixture)
        {
            var f = ctx.Fixtures.Fixtures[fixture];
            return f.Kind == FixtureKind.Village && !ctx.Board[f.Cell].DistortionWorld && !SetupFlow.GruntAt(s, f.Cell);
        }

        /// <summary>MapAction의 합법 답(01 §9-2). 순서는 클래스 설명.</summary>
        public static void Legal(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> into)
        {
            into.Add(ChoiceAnswer.Of(AnswerKind.EndTurn));
            if (c.Seat < 0 || c.Seat >= s.SeatCount) return;
            Fill(s, ctx, c, into, false);
        }

        /// <summary>
        /// 그 좌석이 지금 선 칸(SeatState.Cell)에서 할 수 있는 상호작용만(psychic B 「interact with something from up to 2 tiles away」 — TrainerRules.ReachAnswers가 칸을 바꿔 가며 부른다, rulings A37):
        /// 찾아보기 · 규토리 줍기 · 숨겨진 장소 공개(Accept, Target = 토큰) · 사당 공물(JP 7 더 포함) · 사당의 날씨 바꾸기 · 전체 회복 · 은행상회 · 마을 공중날기 · 왜곡 게이트 건너기 · 도전.
        /// 걷기·제작·진화·운명·자기 카드 능력은 없다(칸에 달린 일이 아니다 — rulings A43 ⑭ 표).
        /// </summary>
        internal static void Interactions(GameState s, Ctx ctx, int seat, List<ChoiceAnswer> into) =>
            Fill(s, ctx, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = seat }, into, true);

        static void Fill(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> into, bool remote)
        {
            var st = s.Seats[c.Seat];
            int cell = st.Cell;
            if (cell < 0) return;

            // 이동: 도착 칸(옛 꼴 — 최단 경로). 고른 길(결정 71, Move + Values)은 늘어놓지 않고 ChoiceRules.IsLegal이 WalkAllowed로 받는다
            // 걷기의 값·높이 조건은 좌석마다(트레이너 카드 hiker B·sky_trainer B · 놓인 길 — TrainerRules.Terms, rulings A36). 닫는 중인 턴(배틀 뒤 등)에는 essentia B만 걷는다
            bool closing = st.Closing != 0;
            if (!remote && (!closing || TrainerRules.ClosingWalk(s, ctx, c.Seat)) && (st.MovesLeft > 0 || st.Glide > 0 || st.RoadChain))
            {
                var steps = MoveRules.Reachable(ctx.Board, cell, st.MovesLeft, TrainerRules.Terms(s, ctx, c.Seat), st.Glide, st.RoadChain);
                for (int d = 0; d < steps.Length; d++)
                    if (d != cell && steps[d] != MoveRules.Unreachable) into.Add(ChoiceAnswer.Of(AnswerKind.Move, d));
            }
            var dive = new List<int>();
            if (closing && !remote)
            {
                // 닫는 중: 턴 종료(그대로 · team_aqua B·team_magma B의 칸)와 위의 걷기뿐 — 다른 상호작용은 없다
                TrainerRules.DiveTargets(s, ctx, c.Seat, dive);
                foreach (int d in dive) into.Add(ChoiceAnswer.Of(AnswerKind.EndTurn, d));
                return;
            }
            // 턴당 1번 지형 2칸 무료(hiker B 등): Move(도착 칸, Arg = FreeArg) — 이동력을 쓰지 않는다
            if (!remote)
            {
                var free = TrainerRules.FreeReach(s, ctx, c.Seat);
                for (int d = 0; d < free.Length; d++)
                    if (free[d] > 0) into.Add(ChoiceAnswer.Of(AnswerKind.Move, d, TrainerRules.FreeArg));
            }
            // 왜곡 게이트(울트라 버스트, p.26 — rulings A50 ④): 이동력 없이 건너편 게이트로. Target = 건너편 칸, Arg = 건너편 게이트(고정물)
            int across = UltraBurstSetup.GateAcross(s, ctx, c.Seat);
            if (across >= 0) into.Add(ChoiceAnswer.Of(AnswerKind.Move, ctx.Fixtures.Fixtures[across].Cell, across));

            bool interact = CanInteract(s, c.Seat);
            bool usable = BattleRules.HasUsable(s, ctx, c.Seat);
            if (remote)
            {
                // 떨어진 칸의 숨겨진 장소: 그 칸에 서면 묻는 공개를 답으로(Accept, Target = 토큰)
                int spot = interact ? FaceDownSpotAt(s, cell) : -1;
                if (spot >= 0) into.Add(ChoiceAnswer.Of(AnswerKind.Accept, spot));
            }
            int token = EncounterTokenAt(s, cell);
            if (token >= 0 && interact)
            {
                bool empty = Used(s, c.Seat, UsageKind.EmptySearch, cell);
                if (!empty && usable) into.Add(ChoiceAnswer.Of(AnswerKind.Search));
                // 조우가 없었던 나무·산호에서만 규토리 3개(p.27 FAQ, G12)
                if (empty && ctx.Fixtures.Fixtures[s.Tokens[token].Def].Apricorns.Count > 0) into.Add(ChoiceAnswer.Of(AnswerKind.TakeApricorns));
            }
            // 앞면 우두머리(자극적인 스튜로 피한 것): 그 칸에서 찾아보기로 싸운다(Target = 토큰) [가정 plan §5 #85 ③]
            int alpha = HiddenSpotRules.FaceUpAlphaAt(s, ctx, cell);
            if (alpha >= 0 && interact && usable) into.Add(ChoiceAnswer.Of(AnswerKind.Search, alpha));
            // UB 토큰(울트라 버스트, p.25 「click "Search" whilst standing on one of their tokens」): 그 칸에서 찾아보기로 싸운다(Target = 토큰, rulings A53)
            int ubToken = UbBattle.TokenAt(s, cell);
            if (ubToken >= 0 && interact && usable) into.Add(ChoiceAnswer.Of(AnswerKind.Search, ubToken));
            // 네크로즈마 토큰(깨어진 세계 가운데 — p.25): 같은 꼴(Target = 토큰, rulings A54 ①)
            int necrozma = UbNecrozma.TokenAt(s, cell);
            if (necrozma >= 0 && interact && usable) into.Add(ChoiceAnswer.Of(AnswerKind.Search, necrozma));
            // 고정물 찾아보기(Arg = 고정물): 전설 장소(맞는 유물을 가진 좌석, p.4 · rulings A26) · 전설 사당의 전설 토큰 · 시작의 사당의 신 토큰 · 왜곡 게이트의 무리(rulings A28)
            int site = interact && usable ? LegendRules.FightAt(s, ctx, c.Seat) : -1;
            if (site >= 0) into.Add(ChoiceAnswer.Of(AnswerKind.Search, -1, site));

            // 사당 공물(p.17, ShrineRules — 턴당 1번, 규토리 색마다 · JP 7을 더 낸 3장 뽑기). 3막 시작의 사당 공물(심장 회복, HosFlow)도 같은 답이다
            ShrineRules.Legal(s, ctx, c.Seat, interact, into);

            bool place = VillagePlace(s, ctx, cell, out _, out int village);
            bool flyHere = village >= 0 && FlyVillage(s, ctx, village);
            if (!remote && !Used(s, c.Seat, UsageKind.Fly))
            {
                var fixtures = ctx.Fixtures.Fixtures;
                // bird_keeper B(rulings A36): 아무 칸에서 조무래기 없는 마을로(이미 마을 공중날기가 되는 칸이면 아래 답과 같다)
                if (!flyHere && TrainerRules.Whistles(s, ctx, c.Seat))
                    for (int f = 0; f < fixtures.Count; f++)
                    {
                        if (fixtures[f].Cell == cell || !FlyVillage(s, ctx, f)) continue;
                        for (int color = 0; color < SeatState.ApricornColors; color++)
                            if (st.Apricorns[color] > 0) into.Add(ChoiceAnswer.Of(AnswerKind.Fly, f, color));
                    }
                // picnicker B: 아무 칸에서 앞면 야영지로(UseAbility, Target = 칸, Arg = 버릴 규토리 색)
                var camps = new List<int>();
                TrainerRules.Campsites(s, ctx, c.Seat, camps);
                foreach (int camp in camps)
                    for (int color = 0; color < SeatState.ApricornColors; color++)
                        if (st.Apricorns[color] > 0) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility, camp, color));
            }
            // worker B: JP 2로 이번 턴에 밟는 칸마다 길(UseAbility, Target 없음)
            if (!remote && TrainerRules.CanBuild(s, ctx, c.Seat)) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility));
            // 맵 상호작용 능력(gardener B · kindler B · psychic B · ruin_maniac B · scientist B · scuba_diver B — rulings A37): 턴당 1번, UseAbility(뜻은 그 좌석의 카드가 정한다)
            if (!remote) TrainerRules.MapAbilities(s, ctx, c.Seat, interact, into);
            // parasol_lady B 날씨 휠: 누구든 사당에서 JP 3으로 날씨를 바꾼다(UseAbility, Target = WeatherTarget, Arg = 새 날씨 — rulings A41). 사당 칸의 일이라 psychic B로도(rulings A43 ⑭)
            TrainerRules.WeatherAnswers(s, ctx, c.Seat, interact, into);
            if (place)
            {
                if (!Used(s, c.Seat, UsageKind.VillageHeal)) into.Add(ChoiceAnswer.Of(AnswerKind.VillageHeal));
                BankRules.Legal(s, ctx, c.Seat, into); // 은행상회(A4): 마을·야영지·울트라메가로폴리스, 턴당 1번
                if (village >= 0 && !Used(s, c.Seat, UsageKind.Fly) && FlyVillage(s, ctx, village))
                {
                    var fixtures = ctx.Fixtures.Fixtures;
                    for (int f = 0; f < fixtures.Count; f++)
                    {
                        if (f == village || !FlyVillage(s, ctx, f)) continue;
                        for (int color = 0; color < SeatState.ApricornColors; color++)
                            if (st.Apricorns[color] > 0) into.Add(ChoiceAnswer.Of(AnswerKind.Fly, f, color));
                    }
                }
            }

            if (!remote)
            {
                CraftRules.Legal(s, ctx, c.Seat, into);  // 제작(S10): 내 턴·배틀 밖 어디서나
                EvolveRules.Legal(s, ctx, c.Seat, into); // 진화(S9_1, A9)
                FateRules.Legal(s, ctx, c.Seat, into);   // 운명의 변화(S13, A11): 맵에서 내는 카드

                // team_aqua B·team_magma B(rulings A36): 턴을 끝내며 다른 바다·산 칸으로(EndTurn, Target = 칸)
                TrainerRules.DiveTargets(s, ctx, c.Seat, dive);
                foreach (int d in dive) into.Add(ChoiceAnswer.Of(AnswerKind.EndTurn, d));

                TradeRules.RemoteChallenges(s, ctx, c.Seat, interact, into);
            }
            // 위 RemoteChallenges = 원격 도전(다른 신의 추종자, p.19 · rulings A29) — 거래는 Legal에 늘어놓지 않는다(ChoiceRules.IsLegal)
            // 도전(rulings G7·G13): 내 칸의 다른 트레이너(보호 중 아님, 쓸 포켓몬 있음, 이번 턴에 아직 안 건 좌석). 1막 조무래기 배틀(A1)은 자기 고향 마을에서.
            if (!interact || !usable) return;
            if (s.Hos != null && s.Hos.Act >= 1 && st.ActProgress == 0 && st.HomeVillage >= 0
                && ctx.Fixtures.Fixtures[st.HomeVillage].Cell == cell && SetupFlow.GruntAt(s, cell))
                into.Add(ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic));
            // 2막 호수의 간부 · 3막 신오신전의 태홍(HosFlow — 고향 마을과 겹치는 칸이 없어 대상은 같은 갤럭시단 값 하나다)
            else if (HosFlow.LeaderAt(s, ctx, c.Seat) != GalacticTrainer.None) into.Add(ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic));
            for (int i = 1; i < s.SeatCount; i++)
            {
                int t = (c.Seat + i) % s.SeatCount;
                var ts = s.Seats[t];
                if (ts.Cell != cell || ts.Protected || ts.Eliminated || Used(s, c.Seat, UsageKind.Challenge, t) || !BattleRules.HasUsable(s, ctx, t)) continue;
                into.Add(ChoiceAnswer.Of(AnswerKind.Challenge, t));
            }
        }

        /// <summary>MapAction 답 적용. 답의 Seq(ActionStarted Kind = 답 종류) 안에서 이벤트를 내고, 턴이 이어지면 MapAction을 다시 연다.</summary>
        public static void Apply(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            switch (a.Kind)
            {
                case AnswerKind.EndTurn:
                {
                    var st = s.Seats[c.Seat];
                    if (a.Target >= 0) TrainerRules.Dive(s, ctx, fx, c.Seat, a.Target); // team_aqua B·team_magma B: 턴을 끝내며 옮긴다(rulings A36)
                    if (st.Closing != 0)
                    {
                        // 닫는 중이던 턴(배틀 뒤 · 규토리 줍기): 미뤄 둔 턴 끝을 잇는다
                        var reason = (TurnEndReason)(st.Closing - TrainerRules.ClosingBase);
                        st.Closing = 0;
                        BattleFlow.FinishClose(s, ctx, fx, c.Seat, st.ClosingAfter, reason);
                    }
                    else TurnFlow.EndTurn(s, ctx, fx, TurnEndReason.EndTurn);
                    return;
                }
                case AnswerKind.Move:
                    if (a.Values.Count > 0) Walk(s, ctx, fx, c.Seat, a.Values, a.Arg == TrainerRules.FreeArg);
                    else if (UltraBurstSetup.IsTravel(s, ctx, c.Seat, a)) UltraBurstSetup.Travel(s, ctx, fx, c.Seat, a.Arg); // 왜곡 게이트 이동(울트라 버스트)
                    else Move(s, ctx, fx, c.Seat, a.Target, a.Arg == TrainerRules.FreeArg);
                    return;
                case AnswerKind.UseAbility:
                    if (TrainerRules.ChangeWeather(s, ctx, fx, c, a)) return; // 날씨 바꾸기(rulings A41 — 카드와 상관없이 먼저)
                    if (TrainerRules.UseMap(s, ctx, fx, c, a)) return; // 맵 상호작용 능력(rulings A37)
                    if (a.Target >= 0)
                    {
                        FlyTo(s, ctx, fx, c.Seat, a.Target, a.Arg, true); // picnicker B: 야영지로 날기
                        return;
                    }
                    TrainerRules.Build(s, ctx, fx, c.Seat); // worker B: 길 놓기를 켠다(그 턴에 이미 밟은 칸도 — 결정 84 T11 ②)
                    Continue(s, ctx, fx, c.Seat);
                    return;
                case AnswerKind.Search:
                    if (a.Arg >= 0) LegendRules.SearchSite(s, ctx, fx, c.Seat, a.Arg);
                    else if (a.Target < 0) Search(s, ctx, fx, c.Seat);
                    else if (UbBattle.IsToken(s, a.Target)) BattleFlow.StartUltraBeast(s, ctx, fx, c.Seat, a.Target);
                    else if (UbNecrozma.IsToken(s, a.Target)) UbNecrozma.Search(s, ctx, fx, c.Seat, a.Target);
                    else if (!HiddenSpotRules.StartAlpha(s, ctx, fx, c.Seat, a.Target)) Continue(s, ctx, fx, c.Seat);
                    return;
                case AnswerKind.TakeApricorns:
                    TakeApricorns(s, ctx, fx, c.Seat);
                    return;
                case AnswerKind.VillageHeal:
                {
                    VillagePlace(s, ctx, s.Seats[c.Seat].Cell, out var source, out _);
                    TurnFlow.HealParty(s, ctx, fx, c.Seat, SourceRef.Rule(source));
                    Mark(s, fx, c.Seat, UsageKind.VillageHeal);
                    Continue(s, ctx, fx, c.Seat);
                    return;
                }
                case AnswerKind.Fly:
                {
                    // 마을 공중날기가 안 되는 칸에서의 날기 = bird_keeper B(rulings A36)
                    VillagePlace(s, ctx, s.Seats[c.Seat].Cell, out _, out int here);
                    FlyTo(s, ctx, fx, c.Seat, ctx.Fixtures.Fixtures[a.Target].Cell, a.Arg, !(here >= 0 && FlyVillage(s, ctx, here)));
                    return;
                }
                case AnswerKind.Offer:
                    ShrineRules.Offer(s, ctx, fx, c.Seat, a); // 카드를 다 처리하면 턴이 이어진다
                    return;
                case AnswerKind.Challenge:
                    if (a.Arg == TradeRules.RemoteArg) TradeRules.RemoteChallenge(s, ctx, fx, c.Seat, a.Target); // 원격 도전(p.19)
                    else BattleFlow.Challenge(s, ctx, fx, c.Seat, a.Target);
                    return;
                case AnswerKind.ProposeTrade:
                    TradeRules.Propose(s, ctx, fx, c.Seat, a); // 거래(p.3·p.19, rulings A29) — 답이 오면 턴이 이어진다
                    return;
                case AnswerKind.Ginkgo:
                    BankRules.Apply(s, ctx, fx, c.Seat, a.Target);
                    return;
                case AnswerKind.Craft:
                    CraftRules.Apply(s, ctx, fx, c.Seat, a.Target, a.Arg);
                    return;
                case AnswerKind.Evolve:
                    if (a.Arg == TrainerRules.EggArg) TrainerRules.Hatch(s, ctx, fx, c.Seat, a); // pokemon_breeder B: 진화 대신 알 기술(rulings A39)
                    else EvolveRules.Apply(s, ctx, fx, c.Seat, a.Target, a.Arg == 1); // Arg 1 = 우러난 스튜의 반값을 이 진화에
                    return;
                case AnswerKind.PlayFate:
                    FateRules.ApplyMap(s, ctx, fx, c.Seat, a);
                    if (s.Pending.Count == 0 && s.Phase == Phase.Turn && s.Battle == null) TurnFlow.OpenMapAction(s, ctx, fx, c.Seat); // 턴은 이어진다
                    return;
                default:
                    throw new InvalidOperationException($"MapAction {a.Kind}: 아직 적용하지 않는 답(Legal에 없어야 한다)");
            }
        }

        /// <summary>그 좌석의 턴이 이어지면 MapAction을 다시 연다(장면 시험의 차례 밖 선택은 열지 않는다).</summary>
        internal static void Continue(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            if (s.Phase == Phase.Turn && s.ActiveSeat == seat) TurnFlow.OpenMapAction(s, ctx, fx, seat);
        }

        /// <summary>
        /// 이동, 옛 꼴(도착 칸만 — 결정 71 전의 명령, 저장 호환): 최단 경로(MoveRules.Path)로 걷고 걸음 수만큼 이동력을 쓴다. 도착 칸에 뒷면 숨겨진 장소가 있고
        /// 상호작용할 수 있으면(G11) 공개할지 묻는다(HiddenSpot 선택, Continues = 이 Seq). 지나간 칸의 숨겨진 장소는 묻지 않는다 — 결정 71 전 G12 그대로라
        /// 옛 로그를 다시 적용해도 같은 판이 된다. 화면은 늘 고른 길(<see cref="Walk"/>)을 낸다. 길잡이(SoloPilot)는 이 꼴을 그대로 쓴다(지나는 칸은 지나가기와 같은 결과).
        /// </summary>
        static void Move(GameState s, Ctx ctx, Emitter fx, int seat, int dest, bool free = false)
        {
            var st = s.Seats[seat];
            List<int> path;
            if (free)
            {
                // 무료 지형 이동(트레이너 카드 — rulings A36): 그 지형 칸만 밟는 가장 짧은 길
                path = TrainerRules.FreePath(s, ctx, seat, dest);
                if (path != null) path.Insert(0, st.Cell);
            }
            else path = MoveRules.Path(ctx.Board, st.Cell, dest, st.MovesLeft, TrainerRules.Terms(s, ctx, seat), st.Glide, st.RoadChain);
            if (path == null) throw new InvalidOperationException($"좌석 {seat}: {st.Cell} → {dest} 경로가 없다(Legal 밖)");
            int before = st.MovesLeft;
            for (int i = 1; i < path.Count; i++) Step(s, ctx, seat, path[i], free);
            int spot = st.Closing == 0 && CanInteract(s, seat) ? FaceDownSpotAt(s, dest) : -1;
            if (fx.On) fx.Add(MovedOf(ctx, seat, path, before, st.MovesLeft, spot >= 0 ? dest : -1));
            if (spot >= 0) OpenSpot(s, ctx, fx, seat, spot, null);
            else Continue(s, ctx, fx, seat);
        }

        /// <summary>
        /// 한 걸음(rulings A36): 이동력을 그 걸음의 값만큼 쓰고(MoveRules.StepCost — 길 이어 가기·활공은 0, 무료 지형 이동 free는 0) 좌석의 활공·길 이어 가기·남은 무료 칸을 고친 뒤
        /// 그 칸에 선다. 길을 놓는 턴(worker B)이면 밟은 칸에 길이 놓인다.
        /// </summary>
        static void Step(GameState s, Ctx ctx, int seat, int to, bool free)
        {
            var st = s.Seats[seat];
            int glide = st.Glide;
            bool chain = st.RoadChain;
            int cost = StepCost(ctx, free ? null : TrainerRules.Terms(s, ctx, seat), st.Cell, to, ref glide, ref chain);
            if (cost < 0) throw new InvalidOperationException($"좌석 {seat}: {st.Cell} → {to} 걸음이 막혔다(Legal 밖)");
            st.MovesLeft -= cost;
            st.Glide = glide;
            st.RoadChain = chain;
            if (free) st.FreeLeft--;
            else if (st.FreeLeft < TrainerRules.FreeTiles) st.FreeLeft = 0; // 무료 칸은 이어서만(「in a row」 — 원작 커뮤니티 확인)
            st.Cell = to;
            st.Moved = true;
            TrainerRules.Paved(s, ctx, seat, to);
        }

        // 한 걸음의 이동력 값(걷기 Step과 미리 보기 StopsOn이 같이 쓴다): 무료 지형 이동(terms = null)은 0이고 활공·길 이어 가기를 끊는다, 아니면 MoveRules.StepCost
        static int StepCost(Ctx ctx, MoveRules.MoveTerms terms, int from, int to, ref int glide, ref bool chain)
        {
            if (terms == null)
            {
                glide = 0;
                chain = false;
                return 0;
            }
            return MoveRules.StepCost(ctx.Board, terms, from, to, ref glide, ref chain);
        }

        // 걸음 뒤 그 칸에서 멈춰 물을까(G11 — 걷기 Walk와 미리 보기 StopsOn이 같이 쓴다): 닫는 턴이 아니고 상호작용할 수 있고(남은 이동력 > 0 또는 옵션) 뒷면 숨겨진 장소가 있다
        static int StopSpot(GameState s, int seat, int cell, int movesLeft) =>
            s.Seats[seat].Closing == 0 && (s.Options.InteractAtZeroMovement || movesLeft > 0) ? FaceDownSpotAt(s, cell) : -1;

        static Moved MovedOf(Ctx ctx, int seat, List<int> path, int before, int after, int stopAt)
        {
            var levels = new int[path.Count];
            for (int i = 0; i < levels.Length; i++) levels[i] = ctx.Board[path[i]].LevelQuarters;
            return new Moved { Seat = seat, Path = path.ToArray(), Levels = levels, AllowanceBefore = before, AllowanceAfter = after, StopAt = stopAt };
        }

        /// <summary>남은 길이 무료 지형 이동이면 HiddenSpot 선택의 Count = FreeRest(지나가기 뒤 이어 걸을 때 쓴다).</summary>
        public const int FreeRest = 2;

        internal static void OpenSpot(GameState s, Ctx ctx, Emitter fx, int seat, int spot, List<int> rest, bool free = false)
        {
            var c = new PendingChoice { Kind = ChoiceKind.HiddenSpot, Seat = seat, Subject = spot, Source = new SourceRef(SourceKind.HiddenSpot, -1) };
            if (free) c.Count = FreeRest;
            if (rest != null) c.Options.AddRange(rest);
            ChoiceRules.Open(s, ctx, fx, c);
        }

        /// <summary>결정 71 고른 길(Move + Values)이 그 좌석에게 합법인가: 마지막 칸 = Target, Arg 없음, 지금 칸에서 한 칸씩 이어지고 남은 이동력 안(<see cref="MoveRules.PathAllows"/>).</summary>
        public static bool WalkAllowed(GameState s, Ctx ctx, int seat, ChoiceAnswer a)
        {
            if (seat < 0 || seat >= s.SeatCount || a.Values.Count == 0 || a.Values[a.Values.Count - 1] != a.Target) return false;
            var st = s.Seats[seat];
            if (st.Cell < 0) return false;
            if (a.Arg == TrainerRules.FreeArg) return TrainerRules.FreeWalkAllowed(s, ctx, seat, a.Values); // 무료 지형 이동(rulings A36)
            if (a.Arg != -1) return false;
            var terms = TrainerRules.Terms(s, ctx, seat);
            if (terms.Plain && st.Glide == 0) return MoveRules.PathAllows(ctx.Board, st.Cell, a.Values, st.MovesLeft, s.Options.ElevationMode);
            int cost = MoveRules.PathCost(ctx.Board, st.Cell, a.Values, terms, st.Glide, st.RoadChain);
            return cost >= 0 && cost <= st.MovesLeft;
        }

        /// <summary>
        /// 결정 71 길 위에서 멈출 칸(걸음 칸 steps 중 — 출발 칸 뺌): 뒷면 숨겨진 장소가 있고 그 칸에 닿은 뒤 상호작용할 수 있는(G11) 칸. 걷기(<see cref="Walk"/>)와
        /// 화면의 길 미리 보기 표지가 같은 셈을 쓴다 — 걸음 값(무료 지형 이동 free · 길 이어 가기 · 활공 · 이번 걸음에 놓는 길)과 멈춤 판정을 Walk와 같은 함수로 센다.
        /// 지나가기로 답한 칸은 지나간 뒤에도 그대로라 같은 길이면 같은 칸이 나온다.
        /// </summary>
        public static List<int> StopsOn(GameState s, Ctx ctx, int seat, IReadOnlyList<int> steps, bool free = false)
        {
            var stops = new List<int>();
            if (steps == null || seat < 0 || seat >= s.SeatCount) return stops;
            var st = s.Seats[seat];
            if (st.Cell < 0) return stops;
            MoveRules.MoveTerms terms = null;
            List<int> roads = null;
            if (!free)
            {
                terms = TrainerRules.Terms(s, ctx, seat);
                roads = new List<int>(s.Roads);   // 걷는 동안 놓을 길(worker B)은 사본에 — 상태는 그대로
                terms.Roads = roads;
            }
            int left = st.MovesLeft, glide = st.Glide, at = st.Cell;
            bool chain = st.RoadChain;
            for (int i = 0; i < steps.Count; i++)
            {
                int cost = StepCost(ctx, terms, at, steps[i], ref glide, ref chain);
                if (cost < 0) break;
                left -= cost;
                at = steps[i];
                if (roads != null && TrainerRules.Paves(s, ctx, seat, at, roads)) roads.Add(at);
                if (StopSpot(s, seat, at, left) >= 0) stops.Add(at);
            }
            return stops;
        }

        /// <summary>
        /// 이동, 결정 71 고른 길(원작 커뮤니티 확인 — 원작 커뮤니티 확인, rulings G12): 걸음 칸을 하나씩 밟으며 이동력을 1씩 쓰고,
        /// 뒷면 숨겨진 장소가 있고 상호작용할 수 있는(G11) 칸에 닿으면 거기서 멈춰 공개할지 묻는다(HiddenSpot 선택 — Options = 남은 걸음 칸).
        /// 지나가기면 남은 길을 이어 걷고(<see cref="ApplyHiddenSpot"/>), 공개하면 남은 길은 버린다(남은 이동력은 그대로 — 다시 고를 수 있다, [가정]).
        /// 걷는 연출은 멈춘 칸까지 Moved 하나다(StopAt = 멈춘 칸).
        /// </summary>
        static void Walk(GameState s, Ctx ctx, Emitter fx, int seat, IReadOnlyList<int> steps, bool free = false)
        {
            var st = s.Seats[seat];
            int before = st.MovesLeft;
            var path = new List<int> { st.Cell };
            for (int i = 0; i < steps.Count; i++)
            {
                Step(s, ctx, seat, steps[i], free);
                path.Add(st.Cell);
                int spot = StopSpot(s, seat, st.Cell, st.MovesLeft);
                if (spot < 0) continue;
                if (fx.On) fx.Add(MovedOf(ctx, seat, path, before, st.MovesLeft, st.Cell));
                var rest = new List<int>();
                for (int k = i + 1; k < steps.Count; k++) rest.Add(steps[k]);
                OpenSpot(s, ctx, fx, seat, spot, rest, free);
                return;
            }
            if (fx.On) fx.Add(MovedOf(ctx, seat, path, before, st.MovesLeft, -1));
            Continue(s, ctx, fx, seat);
        }

        /// <summary>
        /// 숨겨진 장소 답(A23): 공개(Accept)면 앞면으로 뒤집고 SpotKey를 싣고 결과 5종을 처리한다(HiddenSpotRules — 우두머리면 강제 배틀). 지나가기(Decline)면 그대로.
        /// 어느 쪽이든 턴은 이어진다(배틀만 턴을 끝낸다, p.27). 결정 71 고른 길 가운데서 멈췄으면(Options = 남은 걸음 칸): 지나가기는 남은 길을 이어 걷고,
        /// 공개는 남은 길을 버리고 그 칸에 선다 — 공개 결과(배틀이면 턴 끝)를 보고 남은 이동력으로 다시 고른다 [가정 — plan §5].
        /// </summary>
        public static void ApplyHiddenSpot(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            if (c.Subject >= 0)
            {
                var t = s.Tokens[c.Subject];
                if (a.Kind == AnswerKind.Accept)
                {
                    if (Reveal(s, ctx, fx, c.Seat, c.Subject)) return; // 우두머리 배틀(A17) — 배틀이 턴을 끝낸다
                }
                else if (fx.On) fx.Add(new HiddenSpotPassed { Seat = c.Seat, Cell = t.Cell });
            }
            if (a.Kind == AnswerKind.Decline && c.Options.Count > 0 && s.Phase == Phase.Turn && s.ActiveSeat == c.Seat)
            {
                Walk(s, ctx, fx, c.Seat, c.Options, c.Count == FreeRest);
                return;
            }
            Continue(s, ctx, fx, c.Seat);
        }

        /// <summary>토큰을 앞면으로 뒤집고(이미 앞면인 남은 유물은 그대로) 결과를 처리한다. 배틀을 시작했으면 true.</summary>
        internal static bool Reveal(GameState s, Ctx ctx, Emitter fx, int seat, int token)
        {
            var t = s.Tokens[token];
            if (!t.FaceUp)
            {
                t.FaceUp = true;
                if (fx.On) fx.Add(new HiddenSpotRevealed { Seat = seat, Cell = t.Cell, SpotKey = t.Def });
            }
            return HiddenSpotRules.Resolve(s, ctx, fx, seat, token);
        }

        /// <summary>
        /// 찾아보기(R1·R2, rulings G12): 덱을 섞고 맨 위부터 처음 맞는 카드를 뽑는다를 장수만큼(매번 섞음), 맞는 카드가 더 없으면 멈춘다.
        /// 나무·산호는 칸의 지형·기후가 둘 다 같은 카드, 왜곡된 나무는 아무 카드를 강한 → 적당한 → 약한 덱에서 1장씩.
        /// 나오면 조우 고르기(반드시 1장, S5_1, Continues = 이 Seq), 한 장도 없으면 턴이 이어지고 규토리 줍기가 열린다.
        /// </summary>
        static PileCount[] ReturnedPiles(int[] returns)
        {
            var list = new List<PileCount>();
            for (int k = 0; k < returns.Length; k++)
                if (returns[k] > 0) list.Add(new PileCount { Pile = DeckId.Weak + k, Count = returns[k] });
            return list.ToArray();
        }

        /// <summary>retried = ace_trainer B로 다시 찾아봤다(조우 고르기 Count 1 — 그 조우에서 다시는 안 된다).</summary>
        static void Search(GameState s, Ctx ctx, Emitter fx, int seat, bool retried = false)
        {
            int cell = s.Seats[seat].Cell;
            var token = s.Tokens[EncounterTokenAt(s, cell)];
            var c = ctx.Board[cell];
            if (fx.On) fx.Add(new Searched { Seat = seat, Cell = cell, Biome = c.Biome, Climate = c.Climate });
            var drawn = new List<int>();
            var grades = new List<EncounterTier>();
            if (token.Kind == TokenKind.DistortedTree)
            {
                // R2: 강한 → 적당한 → 약한 각 1장. 태그 없는 토큰이라 아무 카드(R1). 첫 장이 없으면 멈춘다(Lua i == 1 break)
                var tiers = new[] { EncounterTier.Strong, EncounterTier.Moderate, EncounterTier.Weak };
                for (int i = 0; i < tiers.Length; i++)
                {
                    int card = DrawMatching(s, ctx, DeckOf(tiers[i]), Biome.None, Climate.None, true);
                    if (card < 0)
                    {
                        if (i == 0) break;
                        continue;
                    }
                    drawn.Add(card);
                    grades.Add(tiers[i]);
                }
            }
            else
            {
                var tier = ctx.Fixtures.Fixtures[token.Def].Tier.Value;
                for (int i = 0; i < SearchCount(tier); i++)
                {
                    int card = DrawMatching(s, ctx, DeckOf(tier), c.Biome, c.Climate, false);
                    if (card < 0) break;
                    drawn.Add(card);
                    grades.Add(tier);
                }
            }
            if (fx.On)
            {
                var cards = new GradedCard[drawn.Count];
                for (int i = 0; i < cards.Length; i++) cards[i] = new GradedCard { Grade = grades[i], CardKey = drawn[i] };
                fx.Add(new EncounterDrawn { Cards = cards });
            }
            if (drawn.Count == 0)
            {
                Mark(s, fx, seat, UsageKind.EmptySearch, cell, false);
                Continue(s, ctx, fx, seat);
                return;
            }
            var pick = new PendingChoice { Kind = ChoiceKind.EncounterPick, Seat = seat, Subject = cell, Count = retried ? 1 : 0 };
            pick.Options.AddRange(drawn);
            ChoiceRules.Open(s, ctx, fx, pick);
        }

        /// <summary>R1: 덱을 섞고 맨 위(끝)부터 처음 맞는 카드를 빼 돌려준다. 없으면 −1. any = 태그 없는 토큰(왜곡된 나무).</summary>
        internal static int DrawMatching(GameState s, Ctx ctx, DeckId deck, Biome biome, Climate climate, bool any)
        {
            var pile = s.Decks[(int)deck];
            Piles.Shuffle(pile, s.Rng);
            for (int i = pile.Count - 1; i >= 0; i--)
            {
                var p = ctx.Cards.Pokemon[pile[i]];
                if (!any && (p.Biome != biome || p.Climate != climate)) continue;
                int card = pile[i];
                pile.RemoveAt(i);
                return card;
            }
            return -1;
        }

        /// <summary>
        /// 조우 고르기 답(S5_1, Continues = 찾아보기 Seq): 고른 카드를 알리고 나머지는 제 덱으로(다음 찾아보기가 섞는다). 그 포켓몬과 배틀한다(BattleFlow).
        /// </summary>
        public static void ApplyEncounterPick(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            if (a.Kind == AnswerKind.Craft)
            {
                // 찾아보기 뒤·배틀 전 제작(룰북 p.2): 만들고 같은 카드로 고르기를 다시 연다(Continues = 이 Seq) [가정 — 고르기 전까지. 고른 카드는 곧바로 배틀이 되니 「고른 뒤·배틀 전」과 결과가 같다]
                CraftRules.Make(s, ctx, fx, c.Seat, a.Target, a.Arg);
                var again = new PendingChoice { Kind = ChoiceKind.EncounterPick, Seat = c.Seat, Subject = c.Subject, Count = c.Count };
                again.Options.AddRange(c.Options);
                ChoiceRules.Open(s, ctx, fx, again);
                return;
            }
            int position = a.Arg;
            int card = a.Target;
            if (a.Kind == AnswerKind.UseAbility) position = -1; // ace_trainer B: 모두 덱으로 돌려보내고 다시 찾아본다(rulings A39)
            var returns = new int[3]; // 약한·적당한·강한
            for (int i = 0; i < c.Options.Count; i++)
            {
                if (i == position) continue;
                var g = GradeOf(ctx, c.Options[i]);
                s.Decks[(int)DeckOf(g)].Add(c.Options[i]);
                returns[(int)DeckOf(g) - (int)DeckId.Weak]++;
            }
            if (a.Kind == AnswerKind.UseAbility)
            {
                if (fx.On) fx.Add(new CardsReturned { Seat = c.Seat, Returns = ReturnedPiles(returns) });
                TrainerRules.Used(s, fx, c.Seat, AbilityColor.Blue);
                Search(s, ctx, fx, c.Seat, true);
                return;
            }
            if (fx.On)
            {
                fx.Add(new EncounterPicked { Seat = c.Seat, CardKey = card, Grade = GradeOf(ctx, card), Position = position });
                var list = new List<PileCount>();
                for (int k = 0; k < returns.Length; k++)
                    if (returns[k] > 0) list.Add(new PileCount { Pile = DeckId.Weak + k, Count = returns[k] });
                fx.Add(new CardsReturned { Returns = list.ToArray() });
            }
            BattleFlow.StartWild(s, ctx, fx, c.Seat, card, c.Subject);
        }

        /// <summary>규토리 줍기(p.27 FAQ): 조우가 없었던 나무·산호의 규토리 3개를 얻고 턴이 끝난다. 색마다 ApricornsTaken 하나(산호는 색이 섞인다).</summary>
        static void TakeApricorns(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            var st = s.Seats[seat];
            int cell = st.Cell;
            var fixture = ctx.Fixtures.Fixtures[s.Tokens[EncounterTokenAt(s, cell)].Def];
            var counts = new int[SeatState.ApricornColors];
            foreach (var color in fixture.Apricorns) counts[(int)color]++;
            for (int color = 0; color < counts.Length; color++)
            {
                if (counts[color] == 0) continue;
                int before = st.Apricorns[color];
                st.Apricorns[color] += counts[color];
                if (fx.On)
                    fx.Add(new ApricornsTaken { Seat = seat, Cell = cell, Color = color, Grade = ApricornGrade((ApricornColor)color), Count = counts[color], Before = before, After = st.Apricorns[color] });
            }
            if (TrainerRules.TurnClosing(s, ctx, fx, seat, 0, TurnEndReason.Apricorns)) return; // team_aqua B·team_magma B: 턴 끝에 옮길 칸을 고른다(rulings A36)
            TurnFlow.EndTurn(s, ctx, fx, TurnEndReason.Apricorns);
        }

        /// <summary>규토리 등급(p.4): 1 = 빨강·파랑·노랑, 2 = 분홍·초록·하양, 3 = 검정.</summary>
        public static int ApricornGrade(ApricornColor c) => c == ApricornColor.Black ? 3 : c >= ApricornColor.Pink ? 2 : 1;

        /// <summary>공중날기(A5): 아무 규토리 1개(색은 고른다)를 버리고 다른 마을로. 이동력은 쓰지 않는다(G12). 턴당 1번.</summary>
        static void FlyTo(GameState s, Ctx ctx, Emitter fx, int seat, int to, int color, bool ability)
        {
            var st = s.Seats[seat];
            int from = st.Cell;
            int before = st.Apricorns[color];
            st.Apricorns[color]--;
            st.Cell = to;
            st.ReachFrom = -1; // psychic B로 떨어진 마을에서 날았으면 닿은 칸이 아니라 도착 마을에 선다
            st.Moved = true;
            st.Glide = 0;
            st.RoadChain = false;
            TrainerRules.Jumped(st);
            if (ability) TrainerRules.Used(s, fx, seat, AbilityColor.Blue); // bird_keeper B · picnicker B(rulings A36)
            if (fx.On)
            {
                fx.Add(new ApricornSpent
                {
                    Seat = seat, Source = SourceRef.Rule(RuleSource.Fly),
                    Changes = new[] { new ResChange { Seat = seat, Res = ResKind.Apricorn, Key = color, Before = before, After = before - 1 } },
                });
                fx.Add(new Flew { Seat = seat, From = from, To = to });
            }
            Mark(s, fx, seat, UsageKind.Fly);
            Continue(s, ctx, fx, seat);
        }
    }
}
