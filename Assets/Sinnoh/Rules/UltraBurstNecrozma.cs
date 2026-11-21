using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 네크로즈마 · 탈락(룰북 p.25, 01 §8-2, rulings A54 — M6 「네크로즈마·탈락·축복」).
    /// - 배틀: 네크로즈마 토큰 칸(깨어진 세계 가운데)에서 찾아보기(MapAction Search, Target = 그 토큰 — p.25 「click "Search" whilst standing on one of their tokens」) → BattleKind.Necrozma.
    ///   지금 페이즈의 카드가 덱 밖의 새 인스턴스로 나온다(p.25 「fully restored between battle attempts and phases」). UB 배틀과 같은 꼴 — 포획 불가 · 전설 특수기술 · p.25 원칙 AI ·
    ///   프리즘 아머는 울트라 버스트 쪽 개수(처음 9 = −4, UbBattle.Prism).
    /// - 페이즈(p.25 「Necrozma has 3 phases … When a phase is successfully defeated, the battle ends and Necrozma proceeds to its next phase permanently」): 1 = 800:1(체력 24) →
    ///   2 = 썬/문 동전으로 800:2 황혼의 갈기 · 800:3 새벽의 날개(체력 30 — 「Use the Sun/Moon coin to determine which form to battle」) → 3 = 800:4 울트라(체력 36).
    ///   동전은 2페이즈에 처음 도전할 때 한 번 던지고 그 판 내내 같은 폼이다 [가정 A54 ③].
    /// - 1·2페이즈 격파는 「쓰러뜨림」이 아니다(원작 커뮤니티 확인): JP·기술 가르치기·스탬프가 없고,
    ///   균열 카드 버리기·옮기기도 없다(원작 커뮤니티 확인). 배틀이 끝나고 턴이 끝난다.
    /// - 3페이즈 격파 = 모두 승리(p.23 「Your only objective is to defeat Necrozma before the breach reaches its critical threshold」 · 원작 커뮤니티 확인
    ///   원작 커뮤니티 확인): GameEnded(NecrozmaDefeated, Winners = 모든 좌석 — 탈락한 좌석도 모둠의 승리다).
    /// - 탈락(p.25 「If a trainer is defeated by Necrozma, they are eliminated from the game」): 네크로즈마 배틀을 이기지 못하고 끝낸 좌석(쓸 포켓몬이 바닥남 · 기권 [가정 A54 ④]).
    ///   턴이 없고(TurnFlow), 도전·거래·균열 카드 붙이기의 대상이 아니다. 모두 탈락했는데 축복이 없으면 모두 패배(AllEliminated), 축복이 있으면 하나를 써서 부활한다(UbBlessing).
    /// </summary>
    public static class UbNecrozma
    {
        public const int Phases = 3;
        public const int Sun = 0, Moon = 1;

        /// <summary>페이즈 카드 키: 1 · 2(썬) · 2(문) · 3.</summary>
        public const string Phase1 = "800:1", DuskMane = "800:2", DawnWings = "800:3", Ultra = "800:4";

        /// <summary>그 칸의 네크로즈마 토큰(토큰 색인). 없으면 −1.</summary>
        public static int TokenAt(GameState s, int cell)
        {
            if (s.Ub == null || cell < 0) return -1;
            for (int i = 0; i < s.Tokens.Count; i++)
                if (s.Tokens[i].Kind == TokenKind.Necrozma && s.Tokens[i].Cell == cell) return i;
            return -1;
        }

        /// <summary>그 토큰이 보드 위의 네크로즈마 토큰인가(MapAction Search의 Target 갈래).</summary>
        public static bool IsToken(GameState s, int token) =>
            s.Ub != null && token >= 0 && token < s.Tokens.Count && s.Tokens[token].Kind == TokenKind.Necrozma && s.Tokens[token].Cell >= 0;

        /// <summary>그 페이즈(1~3)에 싸울 카드(포켓몬 표 색인). 2페이즈는 동전(Sun·Moon)이 정한다.</summary>
        public static int Card(Ctx ctx, int phase, int sunMoon)
        {
            string key = phase <= 1 ? Phase1 : phase >= Phases ? Ultra : sunMoon == Moon ? DawnWings : DuskMane;
            return ctx.Cards.PokemonIds.IndexOf(key);
        }

        /// <summary>네크로즈마 토큰 찾아보기: 2페이즈에 처음 도전하면 썬/문 동전을 던지고(CoinFlip — 게임 난수 Next(2)) 지금 페이즈의 카드와 배틀을 연다.</summary>
        internal static void Search(GameState s, Ctx ctx, Emitter fx, int seat, int token)
        {
            var ub = s.Ub;
            if (ub.NecrozmaPhase == 2 && ub.SunMoon < 0)
            {
                ub.SunMoon = s.Rng.Next(2);
                if (fx.On) fx.Add(new CoinFlip { Seat = SeatId.System, Result = ub.SunMoon });
            }
            BattleFlow.StartNecrozma(s, ctx, fx, seat, s.Tokens[token].Cell, Card(ctx, ub.NecrozmaPhase, ub.SunMoon));
        }

        /// <summary>
        /// 네크로즈마 배틀이 끝났다(BattleFlow.Conclude — 배틀을 치운 뒤). 이겼으면 다음 페이즈(3페이즈면 모두 승리), 아니면 그 좌석이 탈락한다.
        /// 게임이 끝났거나 부활할 좌석을 묻는 선택을 열었으면 true(부르는 쪽은 턴 끝을 잇지 않는다).
        /// </summary>
        internal static bool Concluded(GameState s, Ctx ctx, Emitter fx, int seat, bool won)
        {
            var ub = s.Ub;
            if (!won) return Eliminate(s, ctx, fx, seat);
            if (ub.NecrozmaPhase < Phases)
            {
                int phase = ub.NecrozmaPhase++;
                if (fx.On) fx.Add(new NecrozmaPhase { Seat = seat, Phase = phase, Next = ub.NecrozmaPhase });
                return false;
            }
            foreach (var t in s.Tokens)
            {
                if (t.Kind != TokenKind.Necrozma || t.Cell < 0) continue;
                int cell = t.Cell;
                t.Cell = -1;
                if (fx.On) fx.Add(new TokenRemoved { Seat = seat, Cell = cell, TokenKind = TokenKind.Necrozma });
            }
            s.Phase = Phase.GameOver;
            ub.Result = UbResult.Won;
            var winners = new int[s.SeatCount];
            for (int i = 0; i < winners.Length; i++) winners[i] = i;
            if (fx.On) fx.Add(new GameEnded { Seat = seat, Reason = GameEndReason.NecrozmaDefeated, Winners = winners });
            return true;
        }

        /// <summary>남은(탈락하지 않은) 좌석 수.</summary>
        public static int Remaining(GameState s)
        {
            int n = 0;
            for (int i = 0; i < s.SeatCount; i++)
                if (!s.Seats[i].Eliminated) n++;
            return n;
        }

        /// <summary>
        /// 그 좌석이 탈락한다(Eliminated). 남은 좌석이 있으면 false(턴이 넘어간다). 모두 탈락했으면: 축복이 없으면 모두 패배(true) ·
        /// 축복이 있으면 하나를 써서 부활한다 — 혼자 하는 판은 바로(false), 여럿이면 방금 탈락한 좌석이 부활할 좌석을 고른다(Reward/Revive — true) [가정 A54 ⑥].
        /// </summary>
        static bool Eliminate(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            s.Seats[seat].Eliminated = true;
            if (fx.On) fx.Add(new Eliminated { Seat = seat });
            if (Remaining(s) > 0) return false;
            if (s.Ub.Blessings <= 0)
            {
                s.Phase = Phase.GameOver;
                s.Ub.Result = UbResult.Lost;
                if (fx.On) fx.Add(new GameEnded { Seat = SeatId.System, Reason = GameEndReason.AllEliminated, Winners = new int[0] });
                return true;
            }
            if (s.SeatCount == 1)
            {
                UbBlessing.Spend(s, fx, seat, seat);
                UbBlessing.Revive(s, ctx, fx, seat);
                return false;
            }
            var c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.Revive, Seat = seat, Source = SourceRef.Rule(RuleSource.Respawn) };
            for (int i = 0; i < s.SeatCount; i++) c.Options.Add((seat + i) % s.SeatCount); // 자기부터 시계 방향(G9 ④ — 기본 답 = 자기)
            ChoiceRules.Open(s, ctx, fx, c);
            return true;
        }
    }

    /// <summary>
    /// 아르세우스의 축복(룰북 p.23 「Blessing of Arceus: Use to skip a movement turn of the Ultra Beasts or revive an eliminated trainer」 · 「how many Blessings of Arceus you as a group start with」,
    /// rulings A14 · A54 ⑤~⑦, 04 U12). 모둠 자원(UbState.Blessings — 신인 3 · 베테랑 1 · 마스터 0).
    /// - 쓰기 = 정비 명령 MaintKind.UseBlessing(Value = <see cref="SkipTurn"/> 또는 부활할 좌석): 어느 좌석이든(탈락한 좌석도) 언제든 낸다. 혼자 하는 판은 바로 쓴다.
    ///   여럿 판은 낸 좌석을 뺀 탈락하지 않은 좌석에게 한꺼번에 묻고(Respond/Blessing 묶음 — 탈락한 좌석은 묻지 않는다, 사용자 결정 87 R2 나) 하나라도 막으면 쓰지 않는다(사용자 결정 84 U1 나 — 04 U12 처음 설계).
    ///   허용이 기본 답(시간 초과 = 막지 않음). 물을 좌석이 없으면 바로 쓴다.
    ///   여럿 판에서 내는 때는 쉬는 때 — 차례 좌석의 맵 행동 하나만 열려 있을 때(남의 턴 거래 제안과 같다 [가정 §5 #160 ③]). 묻는 동안 그 맵 행동을 거두고(ChoiceReissued) 셈이 끝나면 다시 연다.
    /// - 라운드 끝(UbTurn.RoundEnd): 축복이 남았고 건너뛰기로 한 턴이 없으면 탈락하지 않은 모든 좌석에게 한꺼번에 「UB 턴을 건너뛰겠습니까?」를 묻는다(Respond/UbSkip 묶음 — 사용자 결정 84 U2 나, 원작은 라운드가 끝난 뒤
    ///   균열 강도 단추를 누르지 않는 것 — 룰북 627행). 다수결(사용자 결정 87 R3 다 + 메모 「다수결」): 「건너뛰기」가 과반이면 건너뛴다 — 동률은 건너뛰지 않는다 [가정 §5 #170 ①]. 다수결이 곧 동의라
    ///   Respond/Blessing 동의를 다시 거치지 않는다. 셈이 끝나면 UB 턴(또는 건너뛰기)을 잇는다.
    /// - UB 턴 건너뛰기: 쓰면 바로 1개가 줄고, 다가오는 라운드 끝의 UB 턴 하나가 통째로 없다(UbTurn.RoundEnd — 이동 · 네크로즈마의 파괴 · 균열 카드 딜. 원작은 버튼을 누르지 않는 것이라
    ///   그 함수 전체가 돌지 않는다). 이미 건너뛰기로 한 턴이 있으면 다시 못 쓴다(NoChange).
    /// - 부활: 탈락한 좌석 하나가 고향 마을에 서고 파티를 회복한다 — 운명의 변화 카드는 없다(사용자 결정 84 U3 ② 나 · 원작 커뮤니티 확인).
    ///   턴 순서는 좌석 번호 그대로 — 이번 라운드의 자기 차례가 이미 지났으면 다음 라운드부터.
    /// - 얻기: 실버디 격파·포획(M6 「호수 시련·실버디」) · 아르세우스 운명 카드 3장(p.19 — FateRules).
    /// </summary>
    public static class UbBlessing
    {
        /// <summary>Maintenance.Value: 다음 UB 턴 건너뛰기(그 밖 = 부활할 좌석).</summary>
        public const int SkipTurn = -1;

        public static Maintenance SkipCommand() => new Maintenance { Kind = MaintKind.UseBlessing, Value = SkipTurn };

        public static Maintenance ReviveCommand(int seat) => new Maintenance { Kind = MaintKind.UseBlessing, Value = seat };

        /// <summary>부활할 좌석을 묻는 선택 · 축복 동의 묶음 · 라운드 끝 건너뛰기 물음이 열려(미뤄져) 있나(그동안 축복 정비 명령은 받지 않는다 — 후보·값이 바뀐다).</summary>
        static bool Asking(GameState s)
        {
            foreach (var list in new[] { s.Pending, s.Held })
                foreach (var c in list)
                    if ((c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.Revive) || (c.Kind == ChoiceKind.Respond && (c.Sub == ChoiceSub.Blessing || c.Sub == ChoiceSub.UbSkip))) return true;
            return false;
        }

        /// <summary>축복 동의 묶음(Respond/Blessing)이 열려 있다(「상단 바로 바로 묻는다」, 결정 84 U1 메모).</summary>
        public static bool Voting(GameState s)
        {
            foreach (var c in s.Pending)
                if (c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Blessing) return true;
            return false;
        }

        /// <summary>정비 명령 검사(MaintenanceRules.Check): 울트라 버스트 판 · 값이 이미 그 상태면 NoChange(건너뛰기로 한 턴이 있다 · 그 좌석은 탈락하지 않았다) · 축복이 있어야 한다.</summary>
        public static RejectReason Check(GameState s, Maintenance m)
        {
            if (s.Ub == null) return RejectReason.Illegal;
            if (m.Value == SkipTurn)
            {
                if (s.Ub.SkipNext) return RejectReason.NoChange;
            }
            else if (m.Value < 0 || m.Value >= s.SeatCount) return RejectReason.BadCommand;
            else if (!s.Seats[m.Value].Eliminated) return RejectReason.NoChange;
            if (s.Ub.Blessings <= 0) return RejectReason.Illegal;
            if (Asking(s)) return RejectReason.Timing;
            return s.SeatCount == 1 || Quiet(s) ? RejectReason.None : RejectReason.Timing;
        }

        /// <summary>여럿 판의 쉬는 때: 턴 단계 · 배틀·효과·반응 창·사당 처리 밖 · 미뤄 둔 선택 없음 · 열린 선택이 차례 좌석의 맵 행동 하나뿐(TradeRules.OffTurnAllowed와 같은 꼴).</summary>
        static bool Quiet(GameState s) =>
            s.Phase == Phase.Turn && s.Battle == null && s.Stack.Count == 0 && s.Windows.Count == 0 && s.Shrine == null && s.Held.Count == 0
            && s.Pending.Count == 1 && s.Pending[0].Kind == ChoiceKind.MapAction && s.Pending[0].Seat == s.ActiveSeat && !s.Pending[0].Answered;

        internal static void Apply(GameState s, Ctx ctx, Emitter fx, int seat, Maintenance m)
        {
            if (Others(s, seat) > 0) Propose(s, ctx, fx, seat, m.Value); // 여럿 판: 다른 좌석에게 묻는다(결정 84 U1)
            else Use(s, ctx, fx, seat, m.Value);
        }

        /// <summary>동의를 물을 좌석 수: 낸 좌석을 뺀 탈락하지 않은 좌석(사용자 결정 87 R2 나).</summary>
        static int Others(GameState s, int seat)
        {
            int n = 0;
            for (int i = 0; i < s.SeatCount; i++)
                if (i != seat && !s.Seats[i].Eliminated) n++;
            return n;
        }

        /// <summary>축복 하나를 쓴다: 건너뛰기 표시 또는 부활.</summary>
        static void Use(GameState s, Ctx ctx, Emitter fx, int seat, int value)
        {
            Spend(s, fx, seat, value);
            if (value == SkipTurn) s.Ub.SkipNext = true;
            else Revive(s, ctx, fx, value);
        }

        /// <summary>낸 좌석을 뺀 탈락하지 않은 좌석에게 한꺼번에 묻는다(Respond/Blessing 묶음, 낸 좌석 다음부터 시계 방향 — 탈락한 좌석은 묻지 않는다, 사용자 결정 87 R2 나).</summary>
        static void Propose(GameState s, Ctx ctx, Emitter fx, int seat, int value)
        {
            if (s.Pending.Count == 1 && s.Pending[0].Kind == ChoiceKind.MapAction)
            {
                // 쉬는 때의 맵 행동을 거둔다(묻는 동안 차례 좌석이 다른 행동을 하지 않게 — 셈이 끝나면 다시 연다)
                var map = s.Pending[0];
                s.Pending.RemoveAt(0);
                if (fx.On) fx.Add(new ChoiceReissued { Seat = map.Seat, OldId = map.Id, NewId = s.NextChoiceId });
            }
            var group = new List<PendingChoice>();
            for (int i = 1; i < s.SeatCount; i++)
            {
                int to = (seat + i) % s.SeatCount;
                if (s.Seats[to].Eliminated) continue;
                var c = new PendingChoice { Kind = ChoiceKind.Respond, Sub = ChoiceSub.Blessing, Seat = to, Subject = seat };
                c.Options.Add(value);
                c.Options.Add(0); // 옛 「라운드 끝에서 나옴」 칸 — 라운드 끝 물음은 다수결이 됐다(사용자 결정 87 R3)
                group.Add(c);
            }
            ChoiceRules.OpenGroup(s, ctx, fx, group);
        }

        /// <summary>축복 동의 묶음인가(Engine.Answer — 묶음이 다 답했을 때).</summary>
        public static bool IsVote(List<PendingChoice> members) =>
            members.Count > 0 && members[0].Kind == ChoiceKind.Respond && members[0].Sub == ChoiceSub.Blessing;

        /// <summary>
        /// 동의 묶음을 셈한다: 하나라도 막았으면(Decline) 쓰지 않는다. 그사이 쓸 수 없게 됐으면(축복이 다 떨어짐 · 이미 건너뛰기로 함 · 그 좌석이 이미 되살아남) 아무 일 없다.
        /// 거둔 맵 행동을 다시 연다.
        /// </summary>
        internal static void Tally(GameState s, Ctx ctx, Emitter fx, List<PendingChoice> members)
        {
            var c = members[0];
            int value = c.Options[0];
            bool blocked = members.Exists(m => m.Answer.Kind == AnswerKind.Decline);
            bool usable = s.Ub.Blessings > 0 && (value == SkipTurn ? !s.Ub.SkipNext : s.Seats[value].Eliminated);
            if (!blocked && usable) Use(s, ctx, fx, c.Subject, value);
            if (s.Phase == Phase.Turn && s.ActiveSeat >= 0) TurnFlow.OpenMapAction(s, ctx, fx, s.ActiveSeat); // 거둔 맵 행동을 다시 연다
        }

        /// <summary>
        /// 라운드 끝(UbTurn.RoundEnd): 축복이 남았고 건너뛰기로 한 턴이 없으면 탈락하지 않은 모든 좌석에게 한꺼번에 묻는다(Respond/UbSkip 묶음 — 사용자 결정 87 R3 다, 좌석 번호 순서).
        /// 물었으면 true — 묶음의 셈(TallySkip)이 UB 턴을 잇는다.
        /// </summary>
        internal static bool AskSkip(GameState s, Ctx ctx, Emitter fx)
        {
            if (s.Ub.Blessings <= 0 || s.Ub.SkipNext || s.ActiveSeat < 0 || s.Phase != Phase.Turn) return false;
            var group = new List<PendingChoice>();
            for (int i = 0; i < s.SeatCount; i++)
                if (!s.Seats[i].Eliminated) group.Add(new PendingChoice { Kind = ChoiceKind.Respond, Sub = ChoiceSub.UbSkip, Seat = i, Subject = -1 });
            if (group.Count == 0) return false;
            ChoiceRules.OpenGroup(s, ctx, fx, group);
            return true;
        }

        /// <summary>
        /// 이미 답한 그 선택에 다시 답할 수 있나(플레이 시험 T29 「울트라 비스트 건너뛰기는 투표는 전부 선택전에는 바꿀수 있도록」): 「UB 턴을 건너뛰겠습니까?」 다수결 묶음이고
        /// 아직 답하지 않은 좌석이 남았을 때만 — 마지막 좌석이 답하는 순간 셈하므로 그 뒤에는 바꿀 수 없다. 시간 초과는 답하지 않은 좌석에만 온다.
        /// </summary>
        public static bool CanRevote(GameState s, PendingChoice c)
        {
            if (s == null || c == null || !c.Answered || c.Kind != ChoiceKind.Respond || c.Sub != ChoiceSub.UbSkip) return false;
            foreach (var m in s.Pending)
                if (m.GroupId == c.GroupId && !m.Answered) return true;
            return false;
        }

        /// <summary>「UB 턴을 건너뛰겠습니까?」 묶음인가(Engine.Answer — 묶음이 다 답했을 때).</summary>
        public static bool IsSkipVote(List<PendingChoice> members) =>
            members.Count > 0 && members[0].Kind == ChoiceKind.Respond && members[0].Sub == ChoiceSub.UbSkip;

        /// <summary>
        /// 「UB 턴을 건너뛰겠습니까?」의 다수결(사용자 결정 87 R3 다 + 메모 「다수결」): 「건너뛰기」(Accept)가 과반이면 축복 1개로 건너뛴다 — 동률은 건너뛰지 않는다 [가정 §5 #170 ①].
        /// 혼자 판은 그 한 사람의 답. 다수결이 곧 동의라 Respond/Blessing 동의는 거치지 않는다. 쓴 좌석 = 「건너뛰기」를 고른 첫 좌석. 끝나면 UB 턴을 잇는다.
        /// </summary>
        internal static void TallySkip(GameState s, Ctx ctx, Emitter fx, List<PendingChoice> members)
        {
            var yes = members.FindAll(m => m.Answer.Kind == AnswerKind.Accept);
            if (yes.Count * 2 > members.Count && s.Ub.Blessings > 0 && !s.Ub.SkipNext) Use(s, ctx, fx, yes[0].Seat, SkipTurn);
            UbTurn.AfterAsk(s, ctx, fx);
        }

        /// <summary>축복 1개를 쓴다(BlessingUsed — Target = SkipTurn 또는 부활할 좌석).</summary>
        internal static void Spend(GameState s, Emitter fx, int seat, int target)
        {
            s.Ub.Blessings--;
            if (fx.On) fx.Add(new BlessingUsed { Seat = seat, Target = target, Left = s.Ub.Blessings });
        }

        /// <summary>
        /// 탈락한 좌석이 부활한다: 고향 마을에 서고 곧바로 파티를 회복한다 — 운명의 변화 카드는 없다(사용자 결정 84 U3 ② 나 — 옛 [가정]은 귀환처럼 다음 턴 시작에 회복 + 운명 1장).
        /// 네크로즈마에게 바닥나 남아 있던 귀환 표시(RespawnPending)도 지운다.
        /// </summary>
        internal static void Revive(GameState s, Ctx ctx, Emitter fx, int target)
        {
            var st = s.Seats[target];
            st.Eliminated = false;
            int home = ctx.Fixtures.Fixtures[st.HomeVillage].Cell;
            st.Cell = home;
            if (fx.On) fx.Add(new Revived { Seat = target, Cell = home });
            TurnFlow.HealParty(s, ctx, fx, target, SourceRef.Rule(RuleSource.Respawn));
            if (!st.RespawnPending) return;
            st.RespawnPending = false;
            if (fx.On) fx.Add(new RespawnCleared { Seat = target });
        }

        /// <summary>모두 탈락한 뒤 부활할 좌석을 골랐다(Reward/Revive): 축복 1개 → 부활 → 멈춰 둔 턴 끝을 잇는다.</summary>
        internal static void ApplyRevive(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            Spend(s, fx, c.Seat, a.Target);
            Revive(s, ctx, fx, a.Target);
            BattleFlow.AfterRevive(s, ctx, fx);
        }

        /// <summary>라운드 끝(UbTurn.RoundEnd): 건너뛰기로 한 UB 턴이면 표시를 지우고 UbTurnSkipped를 낸다 — true면 UB 턴이 돌지 않는다.</summary>
        internal static bool Skip(GameState s, Emitter fx)
        {
            if (!s.Ub.SkipNext) return false;
            s.Ub.SkipNext = false;
            if (fx.On) fx.Add(new UbTurnSkipped { Seat = SeatId.System, Round = s.Round });
            return true;
        }

        /// <summary>축복 +1(BlessingGained). 울트라 버스트 판이 아니면 아무 일 없다.</summary>
        internal static void Gain(GameState s, Emitter fx, int seat, SourceRef source)
        {
            if (s.Ub == null) return;
            s.Ub.Blessings++;
            if (fx.On) fx.Add(new BlessingGained { Seat = seat, Source = source, Total = s.Ub.Blessings });
        }
    }
}
