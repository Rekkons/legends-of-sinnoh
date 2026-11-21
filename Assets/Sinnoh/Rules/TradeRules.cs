using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>거래에 넣는 것(rulings A29 ①, 사용자 결정 75 B4 가). 값 순서가 거래 내용의 정규 순서다.</summary>
    public enum TradeKind
    {
        /// <summary>규토리. Key = 색(ApricornColor), Count = 개수.</summary>
        Apricorn,
        /// <summary>여행 점수. Key = −1.</summary>
        Jp,
        /// <summary>볼. Key = 볼 종류(Ball), Count = 개수.</summary>
        Ball,
        /// <summary>가방의 아이템(배틀 아이템·유물 …). Key = 아이템 색인, Count = 개수.</summary>
        Item,
        /// <summary>파티 포켓몬 — 배운 기술·지닌물건·우두머리 토큰·밑에 둔 카드째로. Key = 포켓몬 인스턴스, Count = 1.</summary>
        Pokemon,
        /// <summary>운명의 변화 카드. 제안한 좌석이 주는 것은 Key = 카드, 상대가 주는 것은 Key = −1(장수만 — 상대가 받을 때 고른다).</summary>
        Fate,
    }

    // ── 이벤트(01 §3-7 「카드」 52행 · 「찾아보기·조우」 51행 — 행은 「스탬프·도전·거래 화면」 묶음이 정한다) ──

    /// <summary>거래 한 줄. 운명 카드의 Key는 당사자만(남에게는 −1 — 장수는 Count로 남는다).</summary>
    public sealed class TradeItem
    {
        public TradeKind Kind = (TradeKind)GameEvent.U;
        public int Key = GameEvent.U;
        public int Count = GameEvent.U;
    }

    /// <summary>
    /// 거래 제안(S35 「도전·거래 요청」). Seat = From = 제안한 좌석, To = 상대, Remote = 같은 칸이 아님(같은 신의 추종자, p.19), OffTurn = 남의 턴에 낸 제안(결정 75 B5 나),
    /// Give = 제안한 좌석이 주는 것, Ask = 상대에게 받는 것.
    /// </summary>
    public sealed class TradeProposed : GameEvent
    {
        public int From = U;
        public int To = U;
        public bool Remote;
        public bool OffTurn;
        public TradeItem[] Give;
        public TradeItem[] Ask;

        public override GameEvent ForViewer(int viewer)
        {
            if (viewer == From || viewer == To) return this;
            var c = Copy<TradeProposed>();
            c.Give = TradeRules.HideFate(Give);
            c.Ask = TradeRules.HideFate(Ask);
            return c;
        }
    }

    /// <summary>거래 응답. Seat = To = 답한 좌석, From = 제안한 좌석, Accept = 받아들임(거래는 뒤따르는 TradeDone), ByTimeout = 시간 초과(기본 = 거절).</summary>
    public sealed class TradeAnswered : GameEvent
    {
        public int To = U;
        public int From = U;
        public bool Accept;
        public bool ByTimeout;
    }

    /// <summary>
    /// 거래 끝(01 §3-7 TradeDone, 04 52행). A = 제안한 좌석, B = 상대, GiveA = A가 준 것, GiveB = B가 준 것(운명 카드는 당사자만 Key), Remote = 원격,
    /// Changes = 규토리·JP·볼·아이템·운명 장수(Key −1)의 좌석마다 전후. 포켓몬은 GiveA·GiveB의 Pokemon 줄(Key = 인스턴스 — 받은 좌석 파티 맨 뒤로).
    /// </summary>
    public sealed class TradeDone : GameEvent
    {
        public int A = U;
        public int B = U;
        public TradeItem[] GiveA;
        public TradeItem[] GiveB;
        public bool Remote;
        public ResChange[] Changes;

        public override GameEvent ForViewer(int viewer)
        {
            if (viewer == A || viewer == B) return this;
            var c = Copy<TradeDone>();
            c.GiveA = TradeRules.HideFate(GiveA);
            c.GiveB = TradeRules.HideFate(GiveB);
            return c;
        }
    }

    /// <summary>
    /// 트레이너끼리 거래 · 원격 도전(rulings A29 — p.3 「freely trade」 · p.19 추종자 · 사용자 결정 75 B4 가 · B5 나 · ⑤ 옵션, 원작 커뮤니티 확인).
    /// <para>
    /// 거래 내용은 답 하나(ProposeTrade: Target = 상대 좌석, Values = 넷씩 [주는 쪽(0 = 제안한 좌석, 1 = 상대), TradeKind, Key, Count], 정규 순서 = 주는 쪽 → 종류 → Key 오름차순, 같은 줄 없음).
    /// 거래 내용은 늘어놓을 수 없어 MapAction Legal에 넣지 않는다 — 고른 길(결정 71)처럼 ChoiceRules.IsLegal이 <see cref="OfferAllowed"/>로 받는다. 화면은 <see cref="Partners"/>로 거래 단추를 켠다.
    /// </para>
    /// <para>
    /// 내 턴: MapAction 답 ProposeTrade(같은 칸 — 상호작용할 수 있을 때(G11) · 같은 신의 추종자는 어디서나). 남의 턴: 명령 <see cref="CommandKind.Propose"/>(같은 신의 추종자만 — 결정 75 B5 나),
    /// 차례 좌석이 맵 행동을 고르는 동안만(<see cref="OffTurnAllowed"/>). 상대에게 Respond/Trade(Subject = 제안한 좌석, Options = [방식, 거래 내용…])를 열린 선택의 맨 앞(버리기 뒤)에 연다.
    /// 응답이 열려 있는 동안 새로 여는 선택은 미뤄 둔다(운명 손패 6장째와 같은 GameState.Held — ChoiceRules.Hold·Release). 답 = 거절(첫 답·기본·봇) / 받아들임(상대가 줄 운명 카드가 있으면 Values = 고른 카드).
    /// </para>
    /// 원격 도전(다른 신의 추종자): MapAction Challenge(Target = 상대, Arg = <see cref="RemoteArg"/>) — 내 턴에만, 늘 거절할 수 있다(원작 커뮤니티 확인). 응답·배틀은 같은 칸 도전 경로(BattleFlow.ApplyRespond)다.
    /// </summary>
    public static class TradeRules
    {
        /// <summary>Values 한 줄의 칸 수.</summary>
        public const int Stride = 4;
        /// <summary>MapAction Challenge의 Arg: 원격 도전(p.19).</summary>
        public const int RemoteArg = 1;

        /// <summary>Respond/Trade Options[0] 방식: 같은 칸(내 턴) · 원격(내 턴) · 원격(남의 턴) · psychic B 「염력」으로 2칸 떨어진 좌석과(내 턴 — 사용자 결정 84 T12 ② 나).</summary>
        public const int SameTile = 0, RemoteOnTurn = 1, RemoteOffTurn = 2, Telekinesis = 3;

        // ── 고리(천상의 균열 · 트레이너 카드가 이을 자리) ──

        /// <summary>
        /// 두 좌석이 같은 신의 추종자다(p.19 「They may trade remotely」). SeatState.Follower(공개된 추종자, None = 아직 아님)를 본다 — 천상의 균열의 비밀 선택 중 값은 선택의 답에만 있고 공개 뒤에 이 칸이 바뀐다(FractureRules.ApplyPicks, rulings A31 ②).
        /// 고리: 비취 무녀 「아르세우스의 추종자」(M6 — 세 신 모두의 추종자, 원작 커뮤니티 확인)는 여기와 <see cref="DifferentGod"/>에 함께 더한다.
        /// </summary>
        public static bool SameGod(GameState s, int a, int b)
        {
            var fa = s.Seats[a].Follower;
            var fb = s.Seats[b].Follower;
            if (fa == SinnohGod.None || fb == SinnohGod.None) return false;
            return fa == fb || fa == SinnohGod.Arceus || fb == SinnohGod.Arceus; // 아르세우스의 추종자는 어느 추종자와도(원작 커뮤니티 확인, rulings A40)
        }

        /// <summary>두 좌석이 서로 다른 신의 추종자다(p.19 「They may battle remotely (these remote battle requests can be denied)」). 고리는 <see cref="SameGod"/>와 같다.</summary>
        public static bool DifferentGod(GameState s, int a, int b)
        {
            var fa = s.Seats[a].Follower;
            var fb = s.Seats[b].Follower;
            return fa != SinnohGod.None && fb != SinnohGod.None && (fa != fb || fa == SinnohGod.Arceus);
        }

        // ── 거래 내용 ──

        /// <summary>거래 내용 답(정규 순서로 맞춘다). quads = 넷씩 [주는 쪽, TradeKind, Key, Count].</summary>
        public static ChoiceAnswer Offer(int partner, params int[] quads)
        {
            if (quads == null || quads.Length % Stride != 0) throw new ArgumentException("넷씩", nameof(quads));
            var rows = new List<int[]>();
            for (int i = 0; i < quads.Length; i += Stride) rows.Add(new[] { quads[i], quads[i + 1], quads[i + 2], quads[i + 3] });
            rows.Sort((x, y) => x[0] != y[0] ? x[0].CompareTo(y[0]) : x[1] != y[1] ? x[1].CompareTo(y[1]) : x[2].CompareTo(y[2]));
            var a = ChoiceAnswer.Of(AnswerKind.ProposeTrade, partner);
            foreach (var r in rows) a.Values.AddRange(r);
            return a;
        }

        /// <summary>
        /// 그 좌석이 지금 거래를 제안할 수 있는 상대(다음 좌석부터 시계 방향). 내 턴(내 MapAction이 열려 있음)이면 같은 칸·같은 신, 남의 턴이면 같은 신만(<see cref="OffTurnAllowed"/>).
        /// 화면의 거래 단추용 — 보기 사본에서도 같다(공개 정보만 본다).
        /// </summary>
        public static void Partners(GameState s, Ctx ctx, int seat, List<int> into)
        {
            into.Clear();
            if (!s.Options.Trading || seat < 0 || seat >= s.SeatCount) return;
            int mode;
            if (OwnMapAction(s, seat)) mode = RemoteOnTurn;
            else if (OffTurnAllowed(s, seat)) mode = RemoteOffTurn;
            else return;
            for (int i = 1; i < s.SeatCount; i++)
            {
                int t = (seat + i) % s.SeatCount;
                if (ModeOf(s, ctx, seat, t, mode) >= 0 && Parties(s, seat, t)) into.Add(t);
            }
        }

        /// <summary>
        /// 그 좌석이 지금 그 상대에게 거래를 제안하면 쓰는 방식(<see cref="Partners"/>와 같은 때 · 같은 순서 — 내 턴 / 남의 턴을 스스로 고른다), 닿지 않으면 −1.
        /// 화면이 psychic B 「염력」으로만 닿는 상대(<see cref="Telekinesis"/>)를 가린다(결정 84 T12 ②).
        /// </summary>
        public static int ModeWith(GameState s, Ctx ctx, int seat, int target)
        {
            if (!s.Options.Trading || seat < 0 || seat >= s.SeatCount) return -1;
            int want = OwnMapAction(s, seat) ? RemoteOnTurn : OffTurnAllowed(s, seat) ? RemoteOffTurn : -1;
            return want < 0 || !Parties(s, seat, target) ? -1 : ModeOf(s, ctx, seat, target, want);
        }

        /// <summary>내 턴에 내 MapAction이 열려 있다(답하지 않은 채).</summary>
        static bool OwnMapAction(GameState s, int seat)
        {
            if (s.ActiveSeat != seat) return false;
            foreach (var c in s.Pending)
                if (c.Kind == ChoiceKind.MapAction && c.Seat == seat) return true;
            return false;
        }

        /// <summary>
        /// 남의 턴에 거래를 제안할 수 있는 때(결정 75 B5 나 [가정 — 쉬는 때]): 판의 차례 좌석이 맵 행동을 고르는 중이고(배틀·효과·반응 창·사당 카드 처리 중이 아님),
        /// 열린 선택이 그 MapAction 하나뿐이고, 미뤄 둔 선택·열린 거래 응답이 없다. 차례 좌석은 이 길을 쓰지 않는다(MapAction으로 낸다).
        /// </summary>
        public static bool OffTurnAllowed(GameState s, int seat)
        {
            if (!s.Options.Trading || seat < 0 || seat >= s.SeatCount || seat == s.ActiveSeat || s.Seats[seat].Eliminated) return false;
            if (s.Phase != Phase.Turn || s.Battle != null || s.Stack.Count > 0 || s.Windows.Count > 0 || s.Shrine != null || s.Held.Count > 0) return false;
            return s.Pending.Count == 1 && s.Pending[0].Kind == ChoiceKind.MapAction && s.Pending[0].Seat == s.ActiveSeat;
        }

        /// <summary>
        /// 두 좌석이 이 방식으로 닿나: 같은 칸(내 턴 — 상호작용할 수 있을 때, G11·G7) 또는 같은 신의 추종자 · 내 턴이면 psychic B로 2칸 안의 좌석(그 턴에 능력을 아직 안 썼을 때 —
        /// 원작 커뮤니티 확인, 사용자 결정 84 T12 ② 나). 닿으면 방식(SameTile·RemoteOnTurn·RemoteOffTurn·Telekinesis), 아니면 −1.
        /// want = RemoteOnTurn이면 내 턴(같은 칸 → 같은 신 → 염력 순 — 능력을 쓰지 않는 길을 먼저), RemoteOffTurn이면 남의 턴(같은 신만).
        /// </summary>
        static int ModeOf(GameState s, Ctx ctx, int a, int b, int want)
        {
            if (a == b || b < 0 || b >= s.SeatCount || s.Seats[b].Eliminated) return -1;
            if (want != RemoteOffTurn && s.Seats[a].Cell >= 0 && s.Seats[a].Cell == s.Seats[b].Cell && MapActions.CanInteract(s, a)) return SameTile;
            if (SameGod(s, a, b)) return want;
            return want == RemoteOnTurn && MapActions.CanInteract(s, a) && TrainerRules.TelekinesisTo(s, ctx, a, b, true) ? Telekinesis : -1;
        }

        /// <summary>두 좌석 다 배틀에 들어가 있지 않다.</summary>
        static bool Parties(GameState s, int a, int b)
        {
            var bt = s.Battle;
            if (bt == null) return true;
            foreach (var side in bt.Sides)
                if (side.Owner == a || side.Owner == b) return false;
            return true;
        }

        /// <summary>MapAction의 거래 답이 합법인가(ChoiceRules.IsLegal): 내 턴, 닿는 상대, 맞는 거래 내용(<see cref="Valid"/>).</summary>
        public static bool OfferAllowed(GameState s, Ctx ctx, int seat, ChoiceAnswer a)
        {
            if (a == null || a.Kind != AnswerKind.ProposeTrade || a.Arg != -1 || !s.Options.Trading) return false;
            int mode = ModeOf(s, ctx, seat, a.Target, RemoteOnTurn);
            return mode >= 0 && Valid(s, ctx, seat, a.Target, a.Values, 0);
        }

        /// <summary>
        /// 그 좌석이 지금 이 거래를 제안할 수 있나(거래 창의 [제안] — 보기 사본에서도 같다): 내 턴이면 맵 행동 답(<see cref="OfferAllowed"/>), 남의 턴이면 명령 Propose가 받는 조건(<see cref="SubmitOffer"/>와 같은 검사).
        /// </summary>
        public static bool CanOffer(GameState s, Ctx ctx, int seat, ChoiceAnswer a)
        {
            if (a == null || a.Kind != AnswerKind.ProposeTrade || a.Arg != -1 || seat < 0 || seat >= s.SeatCount) return false;
            if (OwnMapAction(s, seat)) return OfferAllowed(s, ctx, seat, a);
            return OffTurnAllowed(s, seat) && ModeOf(s, ctx, seat, a.Target, RemoteOffTurn) >= 0 && Valid(s, ctx, seat, a.Target, a.Values, 0);
        }

        /// <summary>
        /// 거래 내용이 맞나(rulings A29 ②): 정규 순서 · 줄마다 주는 쪽이 그만큼 가짐 · 포켓몬은 그 파티에 있고 균열 카드가 붙지 않음(p.24 「Any action that leads to a Distortion Breach card leaving your party is not allowed」)
        /// · 거래 뒤 두 파티 모두 1마리 이상·열린 칸 이하 · 빈 거래 아님 · 두 좌석 다 배틀 밖. from = Values에서 거래 내용이 시작하는 자리.
        /// </summary>
        static bool Valid(GameState s, Ctx ctx, int a, int b, List<int> v, int from)
        {
            if (a < 0 || a >= s.SeatCount || b < 0 || b >= s.SeatCount || a == b || !Parties(s, a, b)) return false;
            int n = v.Count - from;
            if (n <= 0 || n % Stride != 0) return false;
            var seats = new[] { s.Seats[a], s.Seats[b] };
            var poke = new int[2]; // 주는 포켓몬 수
            int heart = ctx.Items.Ids.IndexOf(HosFlow.HeartItemKey);
            for (int i = from; i < v.Count; i += Stride)
            {
                int giver = v[i], kind = v[i + 1], key = v[i + 2], count = v[i + 3];
                if (giver < 0 || giver > 1 || kind < 0 || kind > (int)TradeKind.Fate || count < 1) return false;
                if (i > from && !Before(v, i - Stride, i)) return false;
                var st = seats[giver];
                switch ((TradeKind)kind)
                {
                    case TradeKind.Apricorn:
                        if (key < 0 || key >= SeatState.ApricornColors || st.Apricorns[key] < count) return false;
                        break;
                    case TradeKind.Jp:
                        if (key != -1 || st.Jp < count) return false;
                        break;
                    case TradeKind.Ball:
                        if (key < 0 || key >= SeatState.BallKinds || st.Balls[key] < count) return false;
                        break;
                    case TradeKind.Item:
                        if (key < 0 || key >= ctx.Items.Items.Count || CountOf(st.Bag, key) < count || key == heart) return false; // 신오의 심장은 넣지 않는다(rulings A29 ①)
                        if (ctx.Items.Items[key].Kind == ItemKind.TrainerItem) return false; // 트레이너 카드 전용 아이템(대타출동)도 넣지 않는다(rulings A29 ① · A41)
                        break;
                    case TradeKind.Pokemon:
                        // 심장을 지닌 포켓몬도 넣지 않는다 [가정 plan §5 #94 — 지닌물건은 배틀 밖에서 바꿀 수 있으니 먼저 뗀다(원작 커뮤니티 확인)]
                        if (count != 1 || PokemonRules.PartyIndexOf(st, key) < 0 || BreachRules.Any(s.Pokemon[key]) || (heart >= 0 && PokemonRules.HoldsItem(s.Pokemon[key], heart))) return false;
                        poke[giver]++;
                        break;
                    case TradeKind.Fate:
                        // 제안한 쪽(0)의 카드: 받는 좌석의 보기에서는 그 손패가 가려져 있다(Engine.Unknown) — 가린 칸은 그 카드로 센다(호스트 원본에는 가린 칸이 없다. 보기 = 호스트 Legal, ChoiceRules.Legal)
                        if (giver == 0 ? key < 0 || CountOf(st.FateHand, key) + CountOf(st.FateHand, Engine.Unknown) < count : key != -1 || st.FateHand.Count < count) return false;
                        break;
                }
            }
            for (int k = 0; k < 2; k++)
            {
                int after = PokemonRules.PartyCount(seats[k]) - poke[k] + poke[1 - k];
                if (after < 1 || after > seats[k].OpenSlots || after > seats[k].Party.Length) return false;
            }
            return true;
        }

        /// <summary>v[i..]가 v[j..]보다 정규 순서에서 앞선다(주는 쪽 → 종류 → Key, 같은 줄은 안 됨).</summary>
        static bool Before(List<int> v, int i, int j)
        {
            for (int k = 0; k < 3; k++)
                if (v[i + k] != v[j + k]) return v[i + k] < v[j + k];
            return false;
        }

        static int CountOf(List<int> list, int value)
        {
            int n = 0;
            foreach (int x in list)
                if (x == value) n++;
            return n;
        }

        /// <summary>상대(주는 쪽 1)가 줄 운명 카드 장수.</summary>
        static int FateAsked(List<int> v, int from)
        {
            for (int i = from; i < v.Count; i += Stride)
                if (v[i] == 1 && v[i + 1] == (int)TradeKind.Fate) return v[i + 3];
            return 0;
        }

        // ── 제안 ──

        /// <summary>내 턴 MapAction 답 ProposeTrade: 상대에게 응답을 연다(Continues = 이 Seq). 답이 오면 턴이 이어진다.</summary>
        public static void Propose(GameState s, Ctx ctx, Emitter fx, int seat, ChoiceAnswer a)
        {
            int mode = ModeOf(s, ctx, seat, a.Target, RemoteOnTurn);
            if (mode == Telekinesis) TrainerRules.Telekinesis(s, fx, seat, s.Seats[a.Target].Cell); // psychic B 「Once per turn」을 이 제안으로 쓴다(결정 84 T12 ②)
            Open(s, ctx, fx, seat, a, mode);
        }

        /// <summary>
        /// 남의 턴 제안(명령 <see cref="CommandKind.Propose"/>, Answer = ProposeTrade 거래 내용 — 결정 75 B5 나). 검사: 좌석 범위·답 모양(BadCommand) → 때(<see cref="OffTurnAllowed"/>, Timing)
        /// → 같은 신의 추종자·거래 내용(Illegal). 받으면 한 행동(ActionStarted ProposeTrade)으로 응답을 연다. 차례 좌석의 MapAction은 그대로 열려 있다.
        /// </summary>
        public static SubmitResult SubmitOffer(GameState s, Ctx ctx, int seat, Command cmd, IEventSink sink)
        {
            var a = cmd.Answer;
            if (seat < 0 || seat >= s.SeatCount || a == null || a.Kind != AnswerKind.ProposeTrade || a.Arg != -1) return SubmitResult.Rejected(RejectReason.BadCommand);
            if (!OffTurnAllowed(s, seat)) return SubmitResult.Rejected(RejectReason.Timing);
            if (ModeOf(s, ctx, seat, a.Target, RemoteOffTurn) < 0 || !Valid(s, ctx, seat, a.Target, a.Values, 0)) return SubmitResult.Rejected(RejectReason.Illegal);
            var fx = new Emitter(s, sink);
            fx.Begin(seat, ActionKind.ProposeTrade, cmd.ByAi ? AutoKind.Ai : AutoKind.Human);
            Open(s, ctx, fx, seat, a.Clone(), RemoteOffTurn);
            s.CommandCount++;
            return SubmitResult.Ok;
        }

        static void Open(GameState s, Ctx ctx, Emitter fx, int seat, ChoiceAnswer a, int mode)
        {
            if (fx.On)
                fx.Add(new TradeProposed
                {
                    Seat = seat, From = seat, To = a.Target, Remote = mode != SameTile, OffTurn = mode == RemoteOffTurn,
                    Give = Items(a.Values, 0, 0), Ask = Items(a.Values, 0, 1),
                });
            var c = new PendingChoice { Kind = ChoiceKind.Respond, Sub = ChoiceSub.Trade, Seat = a.Target, Subject = seat };
            c.Options.Add(mode);
            c.Options.AddRange(a.Values);
            ChoiceRules.Open(s, ctx, fx, c);
            // 응답이 먼저다(남의 턴 제안 — 차례 좌석의 MapAction 앞, 버리기 뒤): 「첫 선택」을 보는 화면·봇이 이것부터 답한다
            if (!s.Pending.Remove(c)) return; // 버리기 중이면 미뤄 뒀다(Held)
            int at = 0;
            while (at < s.Pending.Count && s.Pending[at].Kind == ChoiceKind.Discard) at++;
            s.Pending.Insert(at, c);
        }

        /// <summary>거래 응답이 열려 있다 — 그동안 새로 여는 선택은 미뤄 둔다(ChoiceRules.Hold·Release).</summary>
        public static bool Waiting(GameState s)
        {
            foreach (var c in s.Pending)
                if (c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Trade) return true;
            return false;
        }

        // ── 응답 ──

        /// <summary>
        /// Respond/Trade의 합법 답: 거절(첫 답 — 기본·봇·시간 초과, [가정]) → 거래가 아직 맞으면 받아들임. 상대가 줄 운명 카드가 있으면 받아들임은 고를 카드마다
        /// (Values = 카드 오름차순, 손패의 서로 다른 조합). 거래 내용이 지금 상태로 맞지 않으면(그사이 포켓몬을 놓아줌 등) 거절만.
        /// </summary>
        public static void RespondLegal(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> into)
        {
            into.Add(ChoiceAnswer.Of(AnswerKind.Decline));
            if (c.Options.Count < 1 || !s.Options.Trading || !StillReaches(s, ctx, c) || !Valid(s, ctx, c.Subject, c.Seat, c.Options, 1)) return;
            int ask = FateAsked(c.Options, 1);
            if (ask == 0)
            {
                into.Add(ChoiceAnswer.Of(AnswerKind.Accept));
                return;
            }
            var hand = s.Seats[c.Seat].FateHand;
            var pick = new List<int>(ask);
            Combos(hand, 0, ask, pick, into);
        }

        /// <summary>제안한 때의 방식으로 아직 닿나(같은 칸이었으면 같은 칸 · 염력이었으면 2칸 안 — 능력은 제안할 때 썼다 · 원격이었으면 같은 신).</summary>
        static bool StillReaches(GameState s, Ctx ctx, PendingChoice c)
        {
            int a = c.Subject, b = c.Seat;
            if (a < 0 || a >= s.SeatCount || s.Seats[a].Eliminated || s.Seats[b].Eliminated) return false;
            if (c.Options[0] == Telekinesis) return TrainerRules.TelekinesisTo(s, ctx, a, b, false);
            return c.Options[0] == SameTile ? s.Seats[a].Cell >= 0 && s.Seats[a].Cell == s.Seats[b].Cell : SameGod(s, a, b);
        }

        /// <summary>손패 자리 조합(사전 순)마다 카드 값을 오름차순으로 — 같은 값 목록은 한 번.</summary>
        static void Combos(List<int> hand, int start, int left, List<int> pick, List<ChoiceAnswer> into)
        {
            if (left == 0)
            {
                var vals = new List<int>(pick.Count);
                foreach (int i in pick) vals.Add(hand[i]);
                vals.Sort();
                var a = ChoiceAnswer.Of(AnswerKind.Accept);
                a.Values.AddRange(vals);
                if (!ChoiceRules.Contains(into, a)) into.Add(a);
                return;
            }
            for (int i = start; i <= hand.Count - left; i++)
            {
                pick.Add(i);
                Combos(hand, i + 1, left - 1, pick, into);
                pick.RemoveAt(pick.Count - 1);
            }
        }

        /// <summary>
        /// 거래 응답(Continues = 제안 Seq): TradeAnswered → 받아들이면 주고받기(TradeDone) → 미뤄 둔 선택을 열고, 열린 선택이 없으면 판을 잇는다(배틀이면 배틀 진행,
        /// 아니면 차례 좌석의 MapAction — 내 턴 제안이면 제안한 좌석의 턴이 이어진다) → 넘친 운명 손패(A12)를 묻는다.
        /// </summary>
        public static void ApplyRespond(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            bool accept = a.Kind == AnswerKind.Accept;
            if (fx.On) fx.Add(new TradeAnswered { Seat = c.Seat, To = c.Seat, From = c.Subject, Accept = accept, ByTimeout = fx.Auto == AutoKind.Timeout });
            if (accept) Exchange(s, ctx, fx, c, a);
            ChoiceRules.Release(s, ctx, fx);
            if (s.Pending.Count == 0)
            {
                if (s.Battle != null) BattleFlow.Run(s, ctx, fx);
                else if (s.Phase == Phase.Turn && s.ActiveSeat >= 0) TurnFlow.OpenMapAction(s, ctx, fx, s.ActiveSeat);
            }
            if (accept) FateRules.Overflow(s, ctx, fx);
        }

        /// <summary>주고받기(rulings A29 ③): 자원 → 포켓몬(둘 다 뺀 뒤 받은 파티 맨 뒤로, 주인 바꿈) → 운명 카드. 포켓몬에 붙은 것(기술·지닌물건·우두머리 토큰·밑에 둔 카드·피해·상태)은 그대로 간다.</summary>
        static void Exchange(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            var v = c.Options;
            int[] who = { c.Subject, c.Seat };
            var changes = new List<ResChange>();
            var give = new[] { new List<TradeItem>(), new List<TradeItem>() };
            var moving = new List<int>[] { new List<int>(), new List<int>() };
            for (int i = 1; i < v.Count; i += Stride)
            {
                int g = v[i], key = v[i + 2], n = v[i + 3];
                var kind = (TradeKind)v[i + 1];
                var from = s.Seats[who[g]];
                var to = s.Seats[who[1 - g]];
                switch (kind)
                {
                    case TradeKind.Apricorn:
                        Shift(changes, who[g], who[1 - g], ResKind.Apricorn, key, ref from.Apricorns[key], ref to.Apricorns[key], n);
                        break;
                    case TradeKind.Jp:
                        Shift(changes, who[g], who[1 - g], ResKind.Jp, -1, ref from.Jp, ref to.Jp, n);
                        break;
                    case TradeKind.Ball:
                        Shift(changes, who[g], who[1 - g], ResKind.Ball, key, ref from.Balls[key], ref to.Balls[key], n);
                        break;
                    case TradeKind.Item:
                    {
                        int fb = CountOf(from.Bag, key), tb = CountOf(to.Bag, key);
                        for (int k = 0; k < n; k++)
                        {
                            from.Bag.Remove(key);
                            to.Bag.Add(key);
                        }
                        changes.Add(new ResChange { Seat = who[g], Res = ResKind.Item, Key = key, Before = fb, After = fb - n });
                        changes.Add(new ResChange { Seat = who[1 - g], Res = ResKind.Item, Key = key, Before = tb, After = tb + n });
                        break;
                    }
                    case TradeKind.Pokemon:
                        moving[g].Add(key);
                        break;
                    case TradeKind.Fate:
                        break;
                }
                if (kind != TradeKind.Fate) give[g].Add(new TradeItem { Kind = kind, Key = key, Count = n });
            }
            for (int g = 0; g < 2; g++)
                foreach (int p in moving[g]) PokemonRules.RemoveFromParty(s.Seats[who[g]], p);
            for (int g = 0; g < 2; g++)
                foreach (int p in moving[g])
                {
                    s.Pokemon[p].Owner = who[1 - g];
                    PokemonRules.AddToParty(s.Seats[who[1 - g]], p);
                    TrainerRules.Unpocket(s, ctx, fx, p); // backpacker R의 둘째 지닌물건은 backpacker의 포켓몬만(자체 검토 — 원작 커뮤니티 확인)
                }
            // 운명 카드: 제안한 좌석 → 거래 내용의 카드, 상대 → 받아들임 답의 카드
            var cards = new[] { new List<int>(), new List<int>(a.Values) };
            for (int i = 1; i < v.Count; i += Stride)
                if (v[i] == 0 && v[i + 1] == (int)TradeKind.Fate)
                    for (int k = 0; k < v[i + 3]; k++) cards[0].Add(v[i + 2]);
            int[] before = { s.Seats[who[0]].FateHand.Count, s.Seats[who[1]].FateHand.Count };
            for (int g = 0; g < 2; g++)
                foreach (int card in cards[g])
                {
                    if (!s.Seats[who[g]].FateHand.Remove(card)) throw new InvalidOperationException($"좌석 {who[g]}: 손패에 없는 운명 카드 {card}");
                    s.Seats[who[1 - g]].FateHand.Add(card);
                    give[g].Add(new TradeItem { Kind = TradeKind.Fate, Key = card, Count = 1 });
                }
            if (cards[0].Count + cards[1].Count > 0)
                for (int g = 0; g < 2; g++)
                    changes.Add(new ResChange { Seat = who[g], Res = ResKind.Fate, Key = -1, Before = before[g], After = s.Seats[who[g]].FateHand.Count });
            if (fx.On)
                fx.Add(new TradeDone
                {
                    Seat = c.Seat, A = who[0], B = who[1], GiveA = give[0].ToArray(), GiveB = give[1].ToArray(), Remote = c.Options[0] != SameTile, Changes = changes.ToArray(),
                });
            // 트레이너 카드 해금: 거래로 받은 것은 UnlockTrade를 켰을 때만 「얻음」(결정 76 ⑦ T3 나, rulings A20-3) — 제안한 좌석이 받은 것부터
            for (int g = 1; g >= 0; g--)
                foreach (int p in moving[g]) TrainerRules.Obtained(s, ctx, fx, who[1 - g], p, ObtainCause.Trade);
        }

        static void Shift(List<ResChange> changes, int fromSeat, int toSeat, ResKind res, int key, ref int from, ref int to, int n)
        {
            changes.Add(new ResChange { Seat = fromSeat, Res = res, Key = key, Before = from, After = from - n });
            changes.Add(new ResChange { Seat = toSeat, Res = res, Key = key, Before = to, After = to + n });
            from -= n;
            to += n;
        }

        /// <summary>거래 내용 Values[from..]에서 그 쪽이 주는 줄들(이벤트용).</summary>
        static TradeItem[] Items(List<int> v, int from, int giver)
        {
            var r = new List<TradeItem>();
            for (int i = from; i + Stride <= v.Count; i += Stride)
                if (v[i] == giver) r.Add(new TradeItem { Kind = (TradeKind)v[i + 1], Key = v[i + 2], Count = v[i + 3] });
            return r.ToArray();
        }

        /// <summary>남의 보기: 운명 카드 줄의 Key를 가린다(장수는 남긴다).</summary>
        internal static TradeItem[] HideFate(TradeItem[] items)
        {
            if (items == null) return null;
            var r = new TradeItem[items.Length];
            for (int i = 0; i < items.Length; i++)
                r[i] = items[i].Kind == TradeKind.Fate ? new TradeItem { Kind = TradeKind.Fate, Key = -1, Count = items[i].Count } : items[i];
            return r;
        }

        // ── 원격 도전(p.19) ──

        /// <summary>
        /// MapAction의 원격 도전 답(Challenge Target = 상대, Arg = RemoteArg): 다른 신의 추종자 · 쓸 포켓몬이 둘 다 있음 · 상대가 보호 중 아님 · 이번 턴에 그 좌석에 도전하지 않음(G13).
        /// 같은 칸 도전이 되는 상대(같은 칸 + 상호작용)는 그쪽(거절 못 할 수도 있다)만 둔다. 이동력과 상관없다 [가정]. 내 턴에만(결정 75 B5 나 — 도전은 내 턴).
        /// </summary>
        public static void RemoteChallenges(GameState s, Ctx ctx, int seat, bool interact, List<ChoiceAnswer> into)
        {
            if (!BattleRules.HasUsable(s, ctx, seat)) return;
            for (int i = 1; i < s.SeatCount; i++)
            {
                int t = (seat + i) % s.SeatCount;
                var ts = s.Seats[t];
                if (!DifferentGod(s, seat, t) || ts.Protected || ts.Eliminated || MapActions.Used(s, seat, UsageKind.Challenge, t) || !BattleRules.HasUsable(s, ctx, t)) continue;
                if (interact && ts.Cell == s.Seats[seat].Cell) continue; // 같은 칸 도전(MapActions)
                into.Add(ChoiceAnswer.Of(AnswerKind.Challenge, t, RemoteArg));
            }
        }

        /// <summary>원격 도전: 그 턴 그 좌석 기록(G13) → ChallengeIssued(Cell −1, Remote, CanRefuse 늘 참 — F12) → 상대에게 Respond/Challenge. 답은 같은 칸 도전과 같은 BattleFlow.ApplyRespond.</summary>
        public static void RemoteChallenge(GameState s, Ctx ctx, Emitter fx, int seat, int target)
        {
            MapActions.Mark(s, fx, seat, UsageKind.Challenge, target, false);
            if (fx.On) fx.Add(new ChallengeIssued { Seat = seat, From = seat, To = target, Cell = -1, Remote = true, CanRefuse = true });
            ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.Respond, Sub = ChoiceSub.Challenge, Seat = target, Subject = seat });
        }
    }
}
