using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>게임 단계(01 §3-5). 셋업 안의 순서(카드 → 마을 → 스타터·카드 → 서막)는 셋업 작업이 정한다.</summary>
    public enum Phase { Setup, Turn, Battle, UbPhase, GameOver }

    /// <summary>보드 토큰 종류(01 §2-2 Tokens). 숨겨진 장소·유물·우두머리 토큰은 모두 HiddenSpot이고 정체는 Def(아이템 색인)다.</summary>
    public enum TokenKind { ApricornTree, ApricornReef, DistortedTree, Shrine, LegendaryShrine, HiddenSpot, Legend, God, Grunt, UltraBeast, Necrozma }

    /// <summary>상태이상(00 §5-2 #37, S7_6). None = 없음.</summary>
    public enum StatusEffect { None, Confused, Paralysed, Poisoned, Burned, Frozen, Drowsy }

    /// <summary>
    /// 신오의 신(00 §5-2 #49). 추종자 값. None = 없음. Arceus = 아르세우스의 추종자 토큰(undivided_acolyte B 「counted as a Follower of all three Gods of Sinnoh at the same time」 —
    /// 추종자 값에만 쓴다: 세 신 모두를 따른다 <see cref="FractureRules.Follows"/>, rulings A40).
    /// </summary>
    public enum SinnohGod { None, Dialga, Palkia, Giratina, Arceus }

    /// <summary>조각(00 §5-2 #52). SeatState.Shards 색인.</summary>
    public enum Shard { Emotion, Knowledge, Willpower }

    /// <summary>
    /// 덱(decks.json 행, 순서도 같다). 덱 키 = "deck." + snake_case(이름)이고 Ctx가 만들 때 확인한다. GameState.Decks·Discards 색인.
    /// </summary>
    public enum DeckId
    {
        Starter, Weak, Moderate, Strong, Evolution, Legendary, GalacticGrunt, GalacticCommander, GalacticBoss,
        TrainerCard, Act, HomeVillage, ShrineEvent, ChangeOfFate, GinkgoTrade, DistortionBreach, UltraOnslaught,
    }

    /// <summary>
    /// 좌석 하나(01 §2-2 SeatState). 참조는 모두 int 색인이다: 칸 = Board, 마을 = 고정물, 카드 = 덱 종류의 표, 포켓몬 = GameState.Pokemon.
    /// </summary>
    public sealed class SeatState : IStatePart<SeatState>
    {
        public const int ApricornColors = 7;
        public const int BallKinds = 4;
        public const int StampCount = 7;
        public const int ShardCount = 3;

        /// <summary>고향 마을(고정물 색인). 고르기 전 −1.</summary>
        public int HomeVillage = -1;
        /// <summary>말이 선 칸. 놓기 전 −1.</summary>
        public int Cell = -1;
        /// <summary>규토리 개수[ApricornColor].</summary>
        public int[] Apricorns = new int[ApricornColors];
        public int Jp;
        /// <summary>볼 개수[Ball].</summary>
        public int[] Balls = new int[BallKinds];
        /// <summary>가방 아이템(아이템 색인) — 유물(RelicRules)도 여기 든다.</summary>
        public List<int> Bag = new List<int>();
        /// <summary>파티 칸 6개(포켓몬 인스턴스 색인, −1 = 빈칸).</summary>
        public int[] Party = { -1, -1, -1, -1, -1, -1 };
        /// <summary>열린 파티 칸 수(A3).</summary>
        public int OpenSlots;
        /// <summary>운명의 변화 손패(카드 색인). 남에게는 장수만(01 §4).</summary>
        public List<int> FateHand = new List<int>();
        /// <summary>추종자. 첫 공개 전·몰래 바꾸는 중에는 본인만 안다(01 §4).</summary>
        public SinnohGod Follower;
        /// <summary>가진 조각[Shard](A2).</summary>
        public bool[] Shards = new bool[ShardCount];
        /// <summary>신오의 심장을 회복했다(3막).</summary>
        public bool Heart;
        /// <summary>막 목표 진행: 달성한 막 목표 수(1 = 1막 마을 해방, 2 = 조각 3개, 3 = 태홍 격파). 막 공개(전체)는 HosState.</summary>
        public int ActProgress;
        /// <summary>받은 여행 스탬프[칸 순서: 윗줄 4 + 아랫줄 3](S15).</summary>
        public bool[] Stamps = new bool[StampCount];
        /// <summary>스탬프 보상으로 받은 기술 풀(기술 색인, 가르칠 수 있음).</summary>
        public List<int> StampMoves = new List<int>();
        /// <summary>스탬프 보상으로 뽑아 트레이너 곁에 둔 포켓몬 카드: 둘씩 [덱, 카드(포켓몬 표 색인)]. 조우 풀에서 빠진다(원작 커뮤니티 확인, StampRules).</summary>
        public List<int> StampCards = new List<int>();
        /// <summary>셋업에 받은 트레이너 카드 후보(트레이너 카드 색인, A20-1). 본인만 본다.</summary>
        public List<int> TrainerCardOffer = new List<int>();
        /// <summary>고른 트레이너 카드(트레이너 카드 색인). 없으면 −1.</summary>
        public int TrainerCard = -1;
        /// <summary>트레이너 카드 단계 1~3(A20-2). 카드가 없으면 0.</summary>
        public int TrainerCardTier;
        /// <summary>브론즈에서 강함 조건을 먼저 채웠다(UnlockOrder.RememberThenGold만 읽는다 — 보통을 채우는 순간 골드, A20-5). 등급이 오르면 지운다.</summary>
        public bool StrongRemembered;
        /// <summary>트레이너 카드 아이템(전용 아이템, G1).</summary>
        public List<int> CardItems = new List<int>();
        /// <summary>도전 보호(트레이너전 뒤 다음 내 턴까지).</summary>
        public bool Protected;
        /// <summary>귀환 대기(기절 귀환 뒤 다음 턴 회복).</summary>
        public bool RespawnPending;
        /// <summary>탈락(울트라 버스트, S17_6).</summary>
        public bool Eliminated;
        /// <summary>이번 턴 남은 이동력.</summary>
        public int MovesLeft;
        /// <summary>이번 턴 이동력 보너스(스튜 등 일시값).</summary>
        public int MoveBonus;
        /// <summary>이번 턴에 말이 자리를 옮겼다(걷기 · 날기 · 운명 카드의 순간이동 — essentia R 「if you have not moved anywhere yet this turn」, rulings A34). 턴 시작에 지운다.</summary>
        public bool Moved;
        /// <summary>kindler의 장작(「add 3 Burned status effects to your bag as Firewood」 — 불꽃 기술에 하나씩 쓴다, rulings A35).</summary>
        public int Firewood;
        /// <summary>sky_trainer B: 2층 이상 내려선 뒤 무료로 더 갈 수 있는 칸 수(0~4, rulings A36).</summary>
        public int Glide;
        /// <summary>바로 앞 걸음이 길 → 길이었다(이어진 길은 몇 칸이든 이동력 1 — worker B의 길, rulings A36).</summary>
        public bool RoadChain;
        /// <summary>이번 턴에 남은 무료 지형 칸(턴 시작에 2 — 무료로 한 칸 밟으면 1, 그 뒤 보통 걸음을 걸으면 0, rulings A36).</summary>
        public int FreeLeft;
        /// <summary>worker B: 이번 턴에 밟는 칸마다 길을 놓는다.</summary>
        public bool Building;
        /// <summary>worker B를 가진 좌석이 길을 켜기 전 이번 턴에 걸어서 밟은 칸(밟은 순서) — 켜면 이 칸들에도 길을 놓는다(사용자 결정 84 T11 ② 나). 턴 시작에 비운다.</summary>
        public List<int> Trod = new List<int>();
        /// <summary>닫는 중인 턴(0 = 아님, 1 + TurnEndReason — team_aqua B·team_magma B·essentia B가 턴 끝에 고를 것이 있다)과 미뤄 둔 턴 끝 뒤 할 일(BattleFlow.FinishClose의 after).</summary>
        public int Closing, ClosingAfter;
        /// <summary>psychic B로 떨어진 칸과 상호작용하는 동안의 진짜 칸(−1 = 아님). 그 답 하나를 적용하는 동안만 Cell이 닿은 칸이고, 맵 행동을 다시 열기 전에 되돌린다(TrainerRules.Unreach, rulings A37).</summary>
        public int ReachFrom = -1;
        /// <summary>backpacker B: 방금 끝난 배틀에 나왔다 — 배틀 뒤(BattleFlow.Close) 파티 한 마리를 완전 회복할 차례가 남았다(rulings A38).</summary>
        public bool TentDue;
        /// <summary>bug_catcher B: 이번 턴에 벌레 타입을 잡아 턴 끝에 추가 턴이 온다 · 그 추가 턴의 TurnId(−1 = 없음 — 그 턴에는 다시 안 된다, rulings A38).</summary>
        public bool FrenzyDue;
        public int FrenzyTurn = -1;
        /// <summary>interviewers B: 스탬프를 얻은 좌석(−1 = 없음)·그 스탬프·물을 자리(0 = 맵 행동 앞 · 1 = 배틀 뒤 턴 끝 앞) — 그 좌석으로 가서 도전할지 아직 묻지 않았거나 그 배틀 중이다(rulings A38).</summary>
        public int ScoopTarget = -1, ScoopStamp = -1, ScoopAt;
        /// <summary>interviewers B: 묻는 중일 때 더 얻은 스탬프(둘씩 [좌석, 스탬프], 얻은 차례) — 지금 물음이 끝나면 다음 쉬는 자리에서 차례로 묻는다(rulings A38 · G6, M9 실행 감사 K1).</summary>
        public List<int> ScoopNext = new List<int>();
        /// <summary>pokemon_ranger B: 잠긴 파티 칸에도 포켓몬을 둔다(배틀에는 못 나온다 — PokemonRules.AddToParty·Fights). TrainerRules.Refresh가 적는다(rulings A39).</summary>
        public bool Ranch;
        /// <summary>
        /// 「When unlocked」의 미뤄 둔 물음(둘씩 [종류 TrainerRules.Due*, 값]) — 배틀·다른 선택을 끊지 않게 다음 쉬는 자리(맵 행동 앞 · 배틀 뒤 턴 끝 앞 · 명령 끝)에서 묻는다(rulings A40).
        /// UnlockAt = 지금 묻는 물음을 연 자리(−1 = 아님 · 0 = 맵 행동 · 1 + after = 배틀 뒤 턴 끝).
        /// </summary>
        public List<int> UnlockDue = new List<int>();
        public int UnlockAt = -1;
        /// <summary>무녀 빨강의 체력·우선도 −값을 해금 때 고정했다(RuleOptions.AcolyteRedTemporary = FixedAtUnlock, rulings G2). −1 = 고정 안 함(그때그때 센다).</summary>
        public int AcolyteToll = -1;
        /// <summary>guitarist B의 앰프 주사위 값(0~5 — 배틀 사이에 남는다). −1 = 주사위 없음(rulings A41).</summary>
        public int Amp = -1;
        /// <summary>gentleman R의 프레셔스 볼: −1 = 손에(카드가 풀렸으면 던질 수 있다) · −2 = 배틀 중 놓아주어 잃음 · 0 이상 = 그 볼로 잡은 포켓몬(rulings A41).</summary>
        public int CherishIn = -1;
        /// <summary>tourist B의 랜드마크 투어 도장(유물 전설 장소 8곳 — TrainerRules.Landmarks 순서의 비트)과 전설 스냅(포켓몬 표 색인, rulings A41).</summary>
        public int Tour;
        public List<int> Snaps = new List<int>();

        public void CopyFrom(SeatState o)
        {
            HomeVillage = o.HomeVillage;
            Cell = o.Cell;
            Apricorns = StateParts.CopyArray(Apricorns, o.Apricorns);
            Jp = o.Jp;
            Balls = StateParts.CopyArray(Balls, o.Balls);
            StateParts.CopyInts(Bag, o.Bag);
            Party = StateParts.CopyArray(Party, o.Party);
            OpenSlots = o.OpenSlots;
            StateParts.CopyInts(FateHand, o.FateHand);
            Follower = o.Follower;
            Shards = StateParts.CopyArray(Shards, o.Shards);
            Heart = o.Heart;
            ActProgress = o.ActProgress;
            Stamps = StateParts.CopyArray(Stamps, o.Stamps);
            StateParts.CopyInts(StampMoves, o.StampMoves);
            StateParts.CopyInts(StampCards, o.StampCards);
            StateParts.CopyInts(TrainerCardOffer, o.TrainerCardOffer);
            TrainerCard = o.TrainerCard;
            TrainerCardTier = o.TrainerCardTier;
            StrongRemembered = o.StrongRemembered;
            StateParts.CopyInts(CardItems, o.CardItems);
            Protected = o.Protected;
            RespawnPending = o.RespawnPending;
            Eliminated = o.Eliminated;
            MovesLeft = o.MovesLeft;
            MoveBonus = o.MoveBonus;
            Moved = o.Moved;
            Firewood = o.Firewood;
            Glide = o.Glide;
            RoadChain = o.RoadChain;
            FreeLeft = o.FreeLeft;
            Building = o.Building;
            StateParts.CopyInts(Trod, o.Trod);
            Closing = o.Closing;
            ClosingAfter = o.ClosingAfter;
            ReachFrom = o.ReachFrom;
            TentDue = o.TentDue;
            FrenzyDue = o.FrenzyDue;
            FrenzyTurn = o.FrenzyTurn;
            ScoopTarget = o.ScoopTarget;
            ScoopStamp = o.ScoopStamp;
            ScoopAt = o.ScoopAt;
            StateParts.CopyInts(ScoopNext, o.ScoopNext);
            Ranch = o.Ranch;
            StateParts.CopyInts(UnlockDue, o.UnlockDue);
            UnlockAt = o.UnlockAt;
            AcolyteToll = o.AcolyteToll;
            Amp = o.Amp;
            CherishIn = o.CherishIn;
            Tour = o.Tour;
            StateParts.CopyInts(Snaps, o.Snaps);
        }

        public void Walk(IStateWriter w)
        {
            w.Int(HomeVillage);
            w.Int(Cell);
            StateParts.Ints(w, Apricorns);
            w.Int(Jp);
            StateParts.Ints(w, Balls);
            StateParts.Ints(w, Bag);
            StateParts.Ints(w, Party);
            w.Int(OpenSlots);
            StateParts.Ints(w, FateHand);
            w.Int((int)Follower);
            StateParts.Bools(w, Shards);
            StateParts.Bool(w, Heart);
            w.Int(ActProgress);
            StateParts.Bools(w, Stamps);
            StateParts.Ints(w, StampMoves);
            StateParts.Ints(w, StampCards);
            StateParts.Ints(w, TrainerCardOffer);
            w.Int(TrainerCard);
            w.Int(TrainerCardTier);
            StateParts.Bool(w, StrongRemembered);
            StateParts.Ints(w, CardItems);
            StateParts.Bool(w, Protected);
            StateParts.Bool(w, RespawnPending);
            StateParts.Bool(w, Eliminated);
            w.Int(MovesLeft);
            w.Int(MoveBonus);
            StateParts.Bool(w, Moved);
            w.Int(Firewood);
            w.Int(Glide);
            StateParts.Bool(w, RoadChain);
            w.Int(FreeLeft);
            StateParts.Bool(w, Building);
            StateParts.Ints(w, Trod);
            w.Int(Closing);
            w.Int(ClosingAfter);
            w.Int(ReachFrom);
            StateParts.Bool(w, TentDue);
            StateParts.Bool(w, FrenzyDue);
            w.Int(FrenzyTurn);
            w.Int(ScoopTarget);
            w.Int(ScoopStamp);
            w.Int(ScoopAt);
            StateParts.Ints(w, ScoopNext);
            StateParts.Bool(w, Ranch);
            StateParts.Ints(w, UnlockDue);
            w.Int(UnlockAt);
            w.Int(AcolyteToll);
            w.Int(Amp);
            w.Int(CherishIn);
            w.Int(Tour);
            StateParts.Ints(w, Snaps);
        }

        public void Read(IStateReader r)
        {
            HomeVillage = r.Int();
            Cell = r.Int();
            Apricorns = StateParts.ReadArray(r, Apricorns);
            Jp = r.Int();
            Balls = StateParts.ReadArray(r, Balls);
            StateParts.ReadInts(r, Bag);
            Party = StateParts.ReadArray(r, Party);
            OpenSlots = r.Int();
            StateParts.ReadInts(r, FateHand);
            Follower = (SinnohGod)r.Int();
            Shards = StateParts.ReadBools(r, Shards);
            Heart = StateParts.ReadBool(r);
            ActProgress = r.Int();
            Stamps = StateParts.ReadBools(r, Stamps);
            StateParts.ReadInts(r, StampMoves);
            StateParts.ReadInts(r, StampCards);
            StateParts.ReadInts(r, TrainerCardOffer);
            TrainerCard = r.Int();
            TrainerCardTier = r.Int();
            StrongRemembered = StateParts.ReadBool(r);
            StateParts.ReadInts(r, CardItems);
            Protected = StateParts.ReadBool(r);
            RespawnPending = StateParts.ReadBool(r);
            Eliminated = StateParts.ReadBool(r);
            MovesLeft = r.Int();
            MoveBonus = r.Int();
            Moved = StateParts.ReadBool(r);
            Firewood = r.Int();
            Glide = r.Int();
            RoadChain = StateParts.ReadBool(r);
            FreeLeft = r.Int();
            Building = StateParts.ReadBool(r);
            StateParts.ReadInts(r, Trod);
            Closing = r.Int();
            ClosingAfter = r.Int();
            ReachFrom = r.Int();
            TentDue = StateParts.ReadBool(r);
            FrenzyDue = StateParts.ReadBool(r);
            FrenzyTurn = r.Int();
            ScoopTarget = r.Int();
            ScoopStamp = r.Int();
            ScoopAt = r.Int();
            StateParts.ReadInts(r, ScoopNext);
            Ranch = StateParts.ReadBool(r);
            StateParts.ReadInts(r, UnlockDue);
            UnlockAt = r.Int();
            AcolyteToll = r.Int();
            Amp = r.Int();
            CherishIn = r.Int();
            Tour = r.Int();
            StateParts.ReadInts(r, Snaps);
        }
    }

    /// <summary>
    /// 뽑혀 나온 포켓몬 인스턴스(01 §2-2 PokemonState). GameState.Pokemon의 색인이 인스턴스 Id이고, Def = −1이면 빈 자리(다시 쓴다).
    /// </summary>
    public sealed class PokemonState : IStatePart<PokemonState>
    {
        /// <summary>배운 기술 칸 수. 기술은 시그니처 포함 3개다(p.9 「Pokémon can only have 3 moves in total, but the signature move cannot be changed」, 파티 패널 칸마다 기술 카드 자리 2).</summary>
        public const int MoveSlots = 2;

        /// <summary>포켓몬 표 색인(종·폼 = (dex, state)). −1 = 빈 자리.</summary>
        public int Def = -1;
        /// <summary>받은 피해(체력 = 최대 − 피해).</summary>
        public int Damage;
        public StatusEffect Status;
        /// <summary>둘째 상태이상(동시에 2개 거는 기술만 — S7_6 p.9, rulings G20). Status가 None이면 늘 None.</summary>
        public StatusEffect Status2;
        /// <summary>
        /// 마비된 뒤 아직 자기 배틀 턴을 맞지 않았다 — 바로 다음 자기 턴에 공격하면 그 굴림에서 일반 타격을 모두 지운다(S7_6 「Only on your immediate next attack」, rulings G14 · 원작 커뮤니티 확인).
        /// 그 턴에 볼을 던졌거나 교체·기절·배틀 끝으로 나갔으면 지운다. 출전할 때도 지운다(벤치·배틀 밖에서 걸린 마비는 우선도 −3만). 상태가 풀리면 함께 지운다.
        /// </summary>
        /// <summary>셋째부터의 상태(비트 = 1 &lt;&lt; StatusEffect) — hex_maniac R 「additional status effects」로만 생긴다(수 제한 없음, rulings A34). 칸은 Status → Status2 → 여기 순으로 찬다.</summary>
        public int StatusMore;
        public bool ParalysisPending;
        /// <summary>배운 기술 2칸(기술 색인, −1 = 빈칸). 시그니처와 합쳐 3개(MoveSlots).</summary>
        public int[] Moves = { -1, -1 };
        /// <summary>전설 특수기술(알파 스트라이크·비스트 부스트, R13)이 남아 있다(덮어쓰지 않았다).</summary>
        public bool HasLegendaryMove;
        /// <summary>소지 아이템(아이템 색인). 없으면 −1.</summary>
        public int HeldItem = -1;
        /// <summary>둘째 지닌물건(backpacker R 「hold and use up to two Held Items so long as they are not the same item」 — 유물도, rulings A39). 없으면 −1. 읽을 때는 PokemonRules.HoldsItem.</summary>
        public int HeldItem2 = -1;
        /// <summary>잡은 볼(Ball 값). 없으면 −1.</summary>
        public int Ball = -1;
        /// <summary>소유: 좌석 0~5 / SeatId.Wild·Galactic·UltraBeast.</summary>
        public int Owner = SeatId.Wild;
        /// <summary>우두머리 토큰(A17).</summary>
        public bool Alpha;
        /// <summary>트레이너 카드의 최대 체력 보정(gardener R −2 · hiker R +4 · youngster B +1, rulings A32) — PokemonRules.MaxHp가 더한다. TrainerRules.Refresh만 고친다.</summary>
        public int HealthMod;
        /// <summary>월척 주사위의 면(0~5 = Tiny·Average·Medium·Large·Huge·Gargantuan, 값 −1·0·1·2·4·6 — rulings G1). −1 = 주사위 없음(fisherman B가 산호에서 잡을 때 붙인다, rulings A38).</summary>
        public int CatchSize = -1;
        /// <summary>pokemon_breeder B로 알 기술을 배웠다(진화 대신) — 더는 진화하지 못하고 pokemon_breeder R의 체력·우선도 +5를 받는다. 그 기술을 잃어도 남는다(원작 커뮤니티 확인, rulings A39).</summary>
        public bool EggMove;
        /// <summary>무녀 빨강(diamond·pearl·platinum_acolyte R)이 전설 덱에서 찾아 파티에 넣은 신 — 「Temporarily decrease the health and initiative」가 이 포켓몬에 걸린다(rulings A40 · G2).</summary>
        public bool Chosen;
        /// <summary>
        /// 붙은 왜곡 균열 카드[배운 기술 칸](카드 색인, −1 = 없음). 균열 카드는 기술처럼 붙어 그 기술 칸을 막는다(p.24 「attached to the Pokémon like a move」 · 원작 커뮤니티 확인,
        /// rulings A52) — 카드가 있는 칸의 Moves는 늘 −1이다. 붙이고 떼는 곳은 BreachRules.
        /// </summary>
        public int[] Breach = { -1, -1 };
        // 배틀 임시값
        /// <summary>배틀 중 기술로 폼이 바뀌었으면 배틀 전 폼(포켓몬 표 색인) — 배틀이 끝나면 Def가 이것으로 돌아간다(FormRules, rulings A26). 바뀌지 않았으면 −1.</summary>
        public int FormBeforeBattle = -1;
        /// <summary>전투 피로 0~5(FatigueRules).</summary>
        public int Fatigue;
        /// <summary>직전에 쓴 기술(혼란, 알파 스트라이크 −2). 없으면 −1.</summary>
        public int LastMove = -1;
        /// <summary>직전 배틀 턴에 둘째 기술(quick_attack 등)을 썼으면 그 턴의 첫 기술(rulings G21 — RuleOptions.SecondMoveLastTurn = BothMoves일 때만 채운다. 기본 SecondOnly는 둘째 기술만이라 −1, 결정 76 ⑥). 없으면 −1. LastMove와 같이 지운다.</summary>
        public int LastMove2 = -1;
        /// <summary>걸린 지속 보정(M5 효과 기반, rulings G16 — 다음 공격받을 때·다음 공격할 때·교체까지·배틀 끝까지). 건 순서. 걸고 지우는 곳은 ModRules.</summary>
        public List<PokemonMod> Mods = new List<PokemonMod>();
        /// <summary>뽑아 온 덱(DeckId). 놓아주거나 기절해 버릴 때 그 덱의 버림 더미로 간다(rulings G10). 덱 밖이면 −1.</summary>
        public int Deck = -1;
        /// <summary>
        /// 이 포켓몬 카드 밑에 둔 옛 카드(진화하며 옛 기술을 가르쳤을 때, 룰북 p.5 — EvolveRules): 셋씩 [덱, 카드(포켓몬 표 색인), 그 카드로 배운 기술]. 그 기술을 잃거나 놓아주면 버림 더미로.
        /// </summary>
        public List<int> Under = new List<int>();

        public void CopyFrom(PokemonState o)
        {
            Def = o.Def;
            Damage = o.Damage;
            Status = o.Status;
            Status2 = o.Status2;
            StatusMore = o.StatusMore;
            ParalysisPending = o.ParalysisPending;
            Moves = StateParts.CopyArray(Moves, o.Moves);
            HasLegendaryMove = o.HasLegendaryMove;
            HeldItem = o.HeldItem;
            HeldItem2 = o.HeldItem2;
            Ball = o.Ball;
            Owner = o.Owner;
            Alpha = o.Alpha;
            HealthMod = o.HealthMod;
            CatchSize = o.CatchSize;
            EggMove = o.EggMove;
            Chosen = o.Chosen;
            Breach = StateParts.CopyArray(Breach, o.Breach);
            FormBeforeBattle = o.FormBeforeBattle;
            Fatigue = o.Fatigue;
            LastMove = o.LastMove;
            LastMove2 = o.LastMove2;
            StateParts.CopyList(Mods, o.Mods);
            Deck = o.Deck;
            StateParts.CopyInts(Under, o.Under);
        }

        public void Walk(IStateWriter w)
        {
            w.Int(Def);
            w.Int(Damage);
            w.Int((int)Status);
            w.Int((int)Status2);
            w.Int(StatusMore);
            StateParts.Bool(w, ParalysisPending);
            StateParts.Ints(w, Moves);
            StateParts.Bool(w, HasLegendaryMove);
            w.Int(HeldItem);
            w.Int(HeldItem2);
            w.Int(Ball);
            w.Int(Owner);
            StateParts.Bool(w, Alpha);
            w.Int(HealthMod);
            w.Int(CatchSize);
            StateParts.Bool(w, EggMove);
            StateParts.Bool(w, Chosen);
            StateParts.Ints(w, Breach);
            w.Int(FormBeforeBattle);
            w.Int(Fatigue);
            w.Int(LastMove);
            w.Int(LastMove2);
            StateParts.List(w, Mods);
            w.Int(Deck);
            StateParts.Ints(w, Under);
        }

        public void Read(IStateReader r)
        {
            Def = r.Int();
            Damage = r.Int();
            Status = (StatusEffect)r.Int();
            Status2 = (StatusEffect)r.Int();
            StatusMore = r.Int();
            ParalysisPending = StateParts.ReadBool(r);
            Moves = StateParts.ReadArray(r, Moves);
            HasLegendaryMove = StateParts.ReadBool(r);
            HeldItem = r.Int();
            HeldItem2 = r.Int();
            Ball = r.Int();
            Owner = r.Int();
            Alpha = StateParts.ReadBool(r);
            HealthMod = r.Int();
            CatchSize = r.Int();
            EggMove = StateParts.ReadBool(r);
            Chosen = StateParts.ReadBool(r);
            Breach = StateParts.ReadArray(r, Breach);
            FormBeforeBattle = r.Int();
            Fatigue = r.Int();
            LastMove = r.Int();
            LastMove2 = r.Int();
            StateParts.ReadList(r, Mods);
            Deck = r.Int();
            StateParts.ReadInts(r, Under);
        }
    }

    /// <summary>
    /// 보드 토큰 하나(01 §2-2 Tokens). 나무·산호·사당은 Def = 고정물 색인(색·등급은 FixtureDef), 숨겨진 장소는 Def = 아이템 색인(뒷면이면 남에게 숨김),
    /// 전설·신 토큰은 Def = 포켓몬 색인. Cell = −1이면 보드 밖(아직 안 놓음·버림).
    /// </summary>
    public sealed class TokenState : IStatePart<TokenState>
    {
        public TokenKind Kind;
        public int Def = -1;
        public int Cell = -1;
        public bool FaceUp;
        /// <summary>UB가 파괴했다(R5, 균열 R7).</summary>
        public bool Destroyed;
        /// <summary>특별한 숨겨진 장소(금색 ? — 우두머리·유물·천상의 유물 토큰, p.16 「Regular Hidden Spots and Special Hidden Spots」). 뒷면이어도 모두가 본다(ruin_maniac B가 고른다, rulings A37).</summary>
        public bool Special;
        /// <summary>앞면으로 맵에 남은 유물(ruin_maniac B가 공개만 하고 가지 않았다) — 그 칸에 온 좌석이 숨겨진 장소처럼 멈춰 줍는다(MapActions.FaceDownSpotAt).</summary>
        public bool Loose;

        public void CopyFrom(TokenState o)
        {
            Kind = o.Kind;
            Def = o.Def;
            Cell = o.Cell;
            FaceUp = o.FaceUp;
            Destroyed = o.Destroyed;
            Special = o.Special;
            Loose = o.Loose;
        }

        public void Walk(IStateWriter w)
        {
            w.Int((int)Kind);
            w.Int(Def);
            w.Int(Cell);
            StateParts.Bool(w, FaceUp);
            StateParts.Bool(w, Destroyed);
            StateParts.Bool(w, Special);
            StateParts.Bool(w, Loose);
        }

        public void Read(IStateReader r)
        {
            Kind = (TokenKind)r.Int();
            Def = r.Int();
            Cell = r.Int();
            FaceUp = StateParts.ReadBool(r);
            Destroyed = StateParts.ReadBool(r);
            Special = StateParts.ReadBool(r);
            Loose = StateParts.ReadBool(r);
        }
    }

    /// <summary>신오의 심장 캠페인 전체 상태(01 §2-2 HosState, §8-1). 좌석별 목표 달성은 SeatState.ActProgress.</summary>
    public sealed class HosState : IStatePart<HosState>
    {
        /// <summary>막 0 서막 · 1~3 · 4 종막.</summary>
        public const int Acts = 5;

        /// <summary>공개된 막(0~4). 아직 없으면 −1.</summary>
        public int Act = -1;
        /// <summary>막마다 그 목표를 처음 달성한 좌석[막]. 없으면 −1.</summary>
        public int[] FirstSeat = { -1, -1, -1, -1, -1 };
        /// <summary>배신자 좌석(종막, S16_3). 없으면 −1.</summary>
        public int Traitor = -1;
        /// <summary>배신자가 고른 도전 순서(좌석).</summary>
        public List<int> TraitorOrder = new List<int>();
        /// <summary>배신자 연전 진행(TraitorOrder 안 다음 색인).</summary>
        public int TraitorIndex;
        /// <summary>이긴 좌석(게임 끝, Phase.GameOver). 아직이면 −1.</summary>
        public int Winner = -1;
        /// <summary>배신자가 된 뒤 낸 운명의 변화 카드(카드 색인) — 한 명을 이길 때마다 버림 더미에서 손으로 돌아오고 비운다(p.22, FateRules.ReturnUsed).</summary>
        public List<int> TraitorFate = new List<int>();

        public void CopyFrom(HosState o)
        {
            Act = o.Act;
            FirstSeat = StateParts.CopyArray(FirstSeat, o.FirstSeat);
            Traitor = o.Traitor;
            StateParts.CopyInts(TraitorOrder, o.TraitorOrder);
            TraitorIndex = o.TraitorIndex;
            Winner = o.Winner;
            StateParts.CopyInts(TraitorFate, o.TraitorFate);
        }

        public void Walk(IStateWriter w)
        {
            w.Int(Act);
            StateParts.Ints(w, FirstSeat);
            w.Int(Traitor);
            StateParts.Ints(w, TraitorOrder);
            w.Int(TraitorIndex);
            w.Int(Winner);
            StateParts.Ints(w, TraitorFate);
        }

        public void Read(IStateReader r)
        {
            Act = r.Int();
            FirstSeat = StateParts.ReadArray(r, FirstSeat);
            Traitor = r.Int();
            StateParts.ReadInts(r, TraitorOrder);
            TraitorIndex = r.Int();
            Winner = r.Int();
            StateParts.ReadInts(r, TraitorFate);
        }
    }

    /// <summary>울트라 버스트 캠페인 전체 상태(01 §2-2 UbState, §8-2). 균열 강도는 Destroyed에서 R7로 계산한다.</summary>
    public sealed class UbState : IStatePart<UbState>
    {
        public const int PrismArmourTotal = 9;
        /// <summary>판의 울트라 비스트 수(UB 토큰 9 — p.23).</summary>
        public const int Beasts = 9;
        public const int Lakes = 3;

        /// <summary>파괴한 나무·사당 수(R7 분자).</summary>
        public int Destroyed;
        /// <summary>UB 턴 시작 때 균열 강도(%). 넘은 10% 구간을 센다(R8, B4).</summary>
        public int BreachAtTurnStart;
        /// <summary>
        /// 프리즘 아머(합 9, S17_4 · rulings A53): 울트라 버스트 쪽(네크로즈마를 지킨다 — 처음 9) · 울트라 비스트 쪽(UB 9마리를 지킨다 — 처음 0).
        /// UB를 쓰러뜨릴 때마다 1개가 울트라 버스트 쪽에서 울트라 비스트 쪽으로 간다(UbBattle.Defeated).
        /// </summary>
        public int PrismUltraBurst = PrismArmourTotal;
        public int PrismUltraBeast;
        /// <summary>쓰러뜨린 UB 수(0~9 — 무녀 빨강 G2 「못 쓰러뜨린 UB 3마리마다」가 읽는다).</summary>
        public int Defeated;
        /// <summary>아르세우스의 축복(A14).</summary>
        public int Blessings;
        /// <summary>네크로즈마 페이즈 1~3(S17_6).</summary>
        public int NecrozmaPhase = 1;
        /// <summary>2페이즈 동전: −1 아직, 0 Sun, 1 Moon.</summary>
        public int SunMoon = -1;
        /// <summary>호수 수호신을 잡거나 쓰러뜨렸다[호수](S17_7).</summary>
        public bool[] GuardiansDefeated = new bool[Lakes];
        /// <summary>UB가 들어간 야생 칸의 방문 표시(Lua ALREADY_VISITED, 이동 점수 −200). UB 턴 끝에 UB가 서 있는 칸의 표시만 남는다(UbTurn.ResetVisited, rulings A51 ⑤).</summary>
        public List<int> Visited = new List<int>();
        /// <summary>좌석이 손에 든(아직 붙이지 않은) 왜곡 균열 카드: 둘씩 [좌석, 카드(카드 색인)], 받은 순서(UbTurn.DealBreachCards, B4). 공개.</summary>
        public List<int> BreachHands = new List<int>();
        /// <summary>pokemon_ranger B가 빈 파티 칸에 보관한 균열 카드: 둘씩 [좌석, 카드], 보관한 순서(BreachRules — 받을 때 보관할지 묻는다, 사용자 결정 84 U11). 공개.</summary>
        public List<int> BreachStored = new List<int>();
        /// <summary>끝난 결과(게임 끝, Phase.GameOver). 아직이면 None.</summary>
        public UbResult Result;
        /// <summary>아르세우스의 축복으로 다음 UB 턴을 건너뛰기로 했다(UbBlessing — 라운드 끝에 쓰고 지운다, rulings A54 ⑤).</summary>
        public bool SkipNext;
        /// <summary>넘긴 호수 시련[호수 — Shard 순서: 감정·지식·의지](S17_7, rulings A55 — 판에 한 번, UbTrial). 넘긴 수호신은 다음 배틀부터 면역이 없다.</summary>
        public bool[] TrialsOvercome = new bool[Lakes];
        /// <summary>실버디로 아르세우스의 축복을 이미 받았다(판에 한 번 [가정 A55 ⑥]).</summary>
        public bool SilvallyBlessed;

        public void CopyFrom(UbState o)
        {
            Destroyed = o.Destroyed;
            BreachAtTurnStart = o.BreachAtTurnStart;
            PrismUltraBurst = o.PrismUltraBurst;
            PrismUltraBeast = o.PrismUltraBeast;
            Defeated = o.Defeated;
            Blessings = o.Blessings;
            NecrozmaPhase = o.NecrozmaPhase;
            SunMoon = o.SunMoon;
            GuardiansDefeated = StateParts.CopyArray(GuardiansDefeated, o.GuardiansDefeated);
            StateParts.CopyInts(Visited, o.Visited);
            StateParts.CopyInts(BreachHands, o.BreachHands);
            StateParts.CopyInts(BreachStored, o.BreachStored);
            Result = o.Result;
            SkipNext = o.SkipNext;
            TrialsOvercome = StateParts.CopyArray(TrialsOvercome, o.TrialsOvercome);
            SilvallyBlessed = o.SilvallyBlessed;
        }

        public void Walk(IStateWriter w)
        {
            w.Int(Destroyed);
            w.Int(BreachAtTurnStart);
            w.Int(PrismUltraBurst);
            w.Int(PrismUltraBeast);
            w.Int(Defeated);
            w.Int(Blessings);
            w.Int(NecrozmaPhase);
            w.Int(SunMoon);
            StateParts.Bools(w, GuardiansDefeated);
            StateParts.Ints(w, Visited);
            StateParts.Ints(w, BreachHands);
            StateParts.Ints(w, BreachStored);
            w.Int((int)Result);
            StateParts.Bool(w, SkipNext);
            StateParts.Bools(w, TrialsOvercome);
            StateParts.Bool(w, SilvallyBlessed);
        }

        public void Read(IStateReader r)
        {
            Destroyed = r.Int();
            BreachAtTurnStart = r.Int();
            PrismUltraBurst = r.Int();
            PrismUltraBeast = r.Int();
            Defeated = r.Int();
            Blessings = r.Int();
            NecrozmaPhase = r.Int();
            SunMoon = r.Int();
            GuardiansDefeated = StateParts.ReadBools(r, GuardiansDefeated);
            StateParts.ReadInts(r, Visited);
            StateParts.ReadInts(r, BreachHands);
            StateParts.ReadInts(r, BreachStored);
            Result = (UbResult)r.Int();
            SkipNext = StateParts.ReadBool(r);
            TrialsOvercome = StateParts.ReadBools(r, TrialsOvercome);
            SilvallyBlessed = StateParts.ReadBool(r);
        }
    }

    /// <summary>
    /// 동적 게임 상태(01 §2-2). 바뀌는 것만 두고 참조는 모두 int 색인이라 복제가 배열·목록 복사로 끝난다(§2-3 ② 가변 + 손으로 쓴 깊은 복제).
    /// 처리 중 위치(Pending·Stack·Battle.Step)도 여기 있어 어느 대기 지점에서든 복제·저장·보기를 만든다.
    /// 필드를 더하면 CopyFrom·Walk·Read에도 더한다 — 반사 시험 M3_복제는모든필드를복사 · M7_되읽기는모든필드가 빠진 곳을 잡는다.
    /// </summary>
    public sealed class GameState : IStatePart<GameState>
    {
        public static readonly int DeckCount = Enum.GetValues(typeof(DeckId)).Length;

        /// <summary>규칙 옵션(01 §5). 게임을 만들 때 고정.</summary>
        public RuleOptions Options = new RuleOptions();
        public Campaign Campaign;
        public Phase Phase;
        /// <summary>라운드(1부터, 셋업 중 0).</summary>
        public int Round;
        /// <summary>차례 좌석. 없으면 −1.</summary>
        public int ActiveSeat = -1;
        /// <summary>턴 번호(늘기만 한다, 사용 기록 Scope).</summary>
        public int TurnId;
        /// <summary>라운드 첫 좌석 = 마지막에 고향 마을을 고른 좌석(S2_5, rulings G12). 차례가 이 좌석으로 돌아오면 새 라운드. 정하기 전 −1.</summary>
        public int RoundStartSeat = -1;
        public SeatState[] Seats = new SeatState[0];
        /// <summary>보드 토큰 전부(자리 = 칸 색인).</summary>
        public List<TokenState> Tokens = new List<TokenState>();
        /// <summary>뽑을 더미[DeckId]. 끝 = 맨 위. 카드 값은 덱 종류의 표 색인(포켓몬·트레이너 카드·이미지 카드).</summary>
        public List<int>[] Decks = NewPiles();
        /// <summary>버림 더미[DeckId]. 끝 = 맨 위.</summary>
        public List<int>[] Discards = NewPiles();
        /// <summary>포켓몬 인스턴스 표(빈 자리 재사용).</summary>
        public List<PokemonState> Pokemon = new List<PokemonState>();
        /// <summary>신오의 심장이면 있음, 아니면 null.</summary>
        public HosState Hos;
        /// <summary>울트라 버스트면 있음, 아니면 null.</summary>
        public UbState Ub;
        /// <summary>진행 중인 배틀. 없으면 null(01 §6-1).</summary>
        public BattleState Battle;
        /// <summary>열린 선택(01 §3-3).</summary>
        public List<PendingChoice> Pending = new List<PendingChoice>();
        /// <summary>운명 손패 버리기(A12)가 끝날 때까지 미뤄 둔 선택 — 연 순서. 버리기가 끝나면 Pending으로 옮기며 ChoiceRequested를 낸다(ChoiceRules.Release). 보기에서는 비운다.</summary>
        public List<PendingChoice> Held = new List<PendingChoice>();
        /// <summary>모인 떨어진 별 수(p.16 — 4개마다 턴 시작에 운명 1장, FateRules.DealStars). 올리는 곳은 숨겨진 장소 공개(HiddenSpotRules).</summary>
        public int FallenStars;
        /// <summary>길이 놓인 칸(worker B 「Roads」 — 놓인 순서, rulings A36). 모든 좌석이 이어진 길을 이동력 1로 지난다.</summary>
        public List<int> Roads = new List<int>();
        /// <summary>parasol_lady B의 날씨 휠(TrainerRules.WeatherTypes 색인: 0 쾌청 · 1 눈 · 2 비 · 3 모래바람). −1 = 날씨 휠 없음(rulings A41).</summary>
        public int Weather = -1;
        /// <summary>처리 중인 사당 공물(ShrineRules — 뽑은 카드·단계·나눌 좌석). 없으면 null.</summary>
        public ShrineState Shrine;
        /// <summary>처리 중인 효과(01 §7-2). 끝 = 맨 위.</summary>
        public List<EffectFrame> Stack = new List<EffectFrame>();
        /// <summary>「턴당·조우당 1번」 사용 기록.</summary>
        public List<UsageMark> Used = new List<UsageMark>();
        /// <summary>열린 반응 창(01 §7-3, rulings G6). 끝 = 지금 창. 창 안에서 창이 열리면 쌓인다.</summary>
        public List<ReactionWindow> Windows = new List<ReactionWindow>();
        /// <summary>게임 난수(비밀, 01 §4). 셔플·뽑기·주사위가 모두 이것을 거친다.</summary>
        public Pcg32 Rng = new Pcg32(0);
        // 이벤트·로그 커서
        /// <summary>다음 행동 Seq(01 §3-6 ① ActionStarted).</summary>
        public int NextSeq;
        /// <summary>다음 PendingChoice.Id.</summary>
        public int NextChoiceId;
        /// <summary>다음 BattleState.Id.</summary>
        public int NextBattleId;
        /// <summary>적용한 명령 수(세이브 로그 위치, 01 §10).</summary>
        public int CommandCount;
        /// <summary>보기 주인: SeatId.HostViewer(원본) / 좌석 0~5 / SeatId.Observer. 해시에 넣지 않는다(01 §2-3).</summary>
        public int Viewer = SeatId.HostViewer;

        public int SeatCount => Seats.Length;

        static List<int>[] NewPiles()
        {
            var a = new List<int>[DeckCount];
            for (int i = 0; i < a.Length; i++) a[i] = new List<int>();
            return a;
        }

        public GameState Clone()
        {
            var c = new GameState();
            c.CopyFrom(this);
            return c;
        }

        public void CopyFrom(GameState o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            Options.CopyFrom(o.Options);
            Campaign = o.Campaign;
            Phase = o.Phase;
            Round = o.Round;
            ActiveSeat = o.ActiveSeat;
            TurnId = o.TurnId;
            RoundStartSeat = o.RoundStartSeat;
            if (Seats.Length != o.Seats.Length)
            {
                var seats = new SeatState[o.Seats.Length];
                for (int i = 0; i < seats.Length; i++) seats[i] = i < Seats.Length ? Seats[i] : new SeatState();
                Seats = seats;
            }
            for (int i = 0; i < Seats.Length; i++) Seats[i].CopyFrom(o.Seats[i]);
            StateParts.CopyList(Tokens, o.Tokens);
            CopyPiles(Decks, o.Decks);
            CopyPiles(Discards, o.Discards);
            StateParts.CopyList(Pokemon, o.Pokemon);
            Hos = StateParts.CopyNullable(Hos, o.Hos);
            Ub = StateParts.CopyNullable(Ub, o.Ub);
            Battle = StateParts.CopyNullable(Battle, o.Battle);
            StateParts.CopyList(Pending, o.Pending);
            StateParts.CopyList(Held, o.Held);
            FallenStars = o.FallenStars;
            StateParts.CopyInts(Roads, o.Roads);
            Weather = o.Weather;
            Shrine = StateParts.CopyNullable(Shrine, o.Shrine);
            StateParts.CopyList(Stack, o.Stack);
            StateParts.CopyList(Used, o.Used);
            StateParts.CopyList(Windows, o.Windows);
            Rng.CopyFrom(o.Rng);
            NextSeq = o.NextSeq;
            NextChoiceId = o.NextChoiceId;
            NextBattleId = o.NextBattleId;
            CommandCount = o.CommandCount;
            Viewer = o.Viewer;
        }

        static void CopyPiles(List<int>[] dst, List<int>[] src)
        {
            if (dst.Length != src.Length) throw new InvalidOperationException($"더미 수가 다르다({dst.Length} ≠ {src.Length})");
            for (int i = 0; i < dst.Length; i++) StateParts.CopyInts(dst[i], src[i]);
        }

        /// <summary>
        /// 정해진 필드 순서로 훑는다(StateHash). Viewer는 넣지 않는다. 순서를 바꾸면 해시가 바뀐다.
        /// Section = 스트림 구역의 경계(02 §4-4 델타가 구역마다 비교한다 — 해시에는 없다). 구역 수는 좌석 수·덱 수로만 정해진다. <see cref="Read"/>가 같은 자리에서 읽는다.
        /// </summary>
        public void Walk(IStateWriter w)
        {
            if (w == null) throw new ArgumentNullException(nameof(w));
            Options.Walk(w);
            w.Int((int)Campaign);
            w.Int((int)Phase);
            w.Int(Round);
            w.Int(ActiveSeat);
            w.Int(TurnId);
            w.Int(RoundStartSeat);
            w.Int(Seats.Length);
            for (int i = 0; i < Seats.Length; i++)
            {
                StateParts.Section(w);
                Seats[i].Walk(w);
            }
            StateParts.Section(w);
            StateParts.List(w, Tokens);
            WalkPiles(w, Decks);
            WalkPiles(w, Discards);
            StateParts.Section(w);
            StateParts.List(w, Pokemon);
            StateParts.Section(w);
            StateParts.Nullable(w, Hos);
            StateParts.Nullable(w, Ub);
            StateParts.Section(w);
            StateParts.Nullable(w, Battle);
            StateParts.Section(w);
            StateParts.List(w, Pending);
            StateParts.List(w, Held);
            StateParts.Section(w);
            w.Int(FallenStars);
            StateParts.Ints(w, Roads);
            w.Int(Weather);
            StateParts.Nullable(w, Shrine);
            StateParts.Section(w);
            StateParts.List(w, Stack);
            StateParts.Section(w);
            StateParts.List(w, Used);
            StateParts.Section(w);
            StateParts.List(w, Windows);
            StateParts.Section(w);
            w.ULong(Rng.State);
            w.ULong(Rng.Increment);
            w.Int(NextSeq);
            w.Int(NextChoiceId);
            w.Int(NextBattleId);
            w.Int(CommandCount);
        }

        static void WalkPiles(IStateWriter w, List<int>[] piles)
        {
            w.Int(piles.Length);
            for (int i = 0; i < piles.Length; i++)
            {
                StateParts.Section(w);
                StateParts.Ints(w, piles[i]);
            }
        }

        /// <summary>
        /// Walk가 쓴 값을 같은 순서로 읽어 덮어쓴다(02 §4-4 — 손님이 받은 스트림을 보기로 되돌린다). Viewer와 해시 밖 값(Options.FixedEffectOrder)은 읽지 않는다 — 부르는 쪽이 정한다.
        /// 더미 수가 이 빌드와 다르면 FormatException.
        /// </summary>
        public void Read(IStateReader r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            Options.Read(r);
            Campaign = (Campaign)r.Int();
            Phase = (Phase)r.Int();
            Round = r.Int();
            ActiveSeat = r.Int();
            TurnId = r.Int();
            RoundStartSeat = r.Int();
            int seats = r.Int(); // 좌석은 뒤 구역들이라 Count(이 구역 안의 길이)로 재지 않는다
            if (seats < 0 || seats > SeatId.MaxSeats) throw new FormatException($"좌석 수 {seats}");
            if (Seats.Length != seats)
            {
                var a = new SeatState[seats];
                for (int i = 0; i < a.Length; i++) a[i] = i < Seats.Length ? Seats[i] : new SeatState();
                Seats = a;
            }
            for (int i = 0; i < Seats.Length; i++)
            {
                StateParts.Section(r);
                Seats[i].Read(r);
            }
            StateParts.Section(r);
            StateParts.ReadList(r, Tokens);
            ReadPiles(r, Decks);
            ReadPiles(r, Discards);
            StateParts.Section(r);
            StateParts.ReadList(r, Pokemon);
            StateParts.Section(r);
            Hos = StateParts.ReadNullable(r, Hos);
            Ub = StateParts.ReadNullable(r, Ub);
            StateParts.Section(r);
            Battle = StateParts.ReadNullable(r, Battle);
            StateParts.Section(r);
            StateParts.ReadList(r, Pending);
            StateParts.ReadList(r, Held);
            StateParts.Section(r);
            FallenStars = r.Int();
            StateParts.ReadInts(r, Roads);
            Weather = r.Int();
            Shrine = StateParts.ReadNullable(r, Shrine);
            StateParts.Section(r);
            StateParts.ReadList(r, Stack);
            StateParts.Section(r);
            StateParts.ReadList(r, Used);
            StateParts.Section(r);
            StateParts.ReadList(r, Windows);
            StateParts.Section(r);
            Rng.State = r.ULong();
            Rng.Increment = r.ULong();
            NextSeq = r.Int();
            NextChoiceId = r.Int();
            NextBattleId = r.Int();
            CommandCount = r.Int();
        }

        static void ReadPiles(IStateReader r, List<int>[] piles)
        {
            int n = r.Int();
            if (n != piles.Length) throw new FormatException($"더미 수가 다르다({n} ≠ {piles.Length})");
            for (int i = 0; i < piles.Length; i++)
            {
                StateParts.Section(r);
                StateParts.ReadInts(r, piles[i]);
            }
        }
    }
}
