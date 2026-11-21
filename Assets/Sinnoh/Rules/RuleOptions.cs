namespace Sinnoh.Rules
{
    /// <summary>
    /// 규칙 옵션(01 §5). 게임을 만들 때 정하고 끝까지 바꾸지 않는다. 세이브에 들어간다.
    /// 기본값은 rulings다(결정 14 ①). 「원작 커뮤니티 답 대기」는 원작 커뮤니티 확인 답을 기다리는 항목이다 —
    /// 답이 오면 rulings에 적고 기본값만 바꾼다(결정 28). 「원작 커뮤니티 확인」은 원작 커뮤니티 확인에서 답을 찾은 항목이다.
    /// 기본값을 바꿔도 옛 저장은 머리·스냅샷에 적힌 값을 그대로 쓴다(필드를 모두 쓰므로) — 새 판만 새 기본값이다(RULE-SAVE). AI·조종(GalacticController 등)은 규칙이 아니라 방 설정 AiSettings다.
    /// 전용 아이템(G1)과 효과 대상 없음(G3)은 옵션이 아니라 데이터다.
    /// </summary>
    public sealed class RuleOptions
    {
        /// <summary>A6 높이 판정. 기본 Floor. 원작 커뮤니티 답 대기.</summary>
        public ElevationMode ElevationMode = ElevationMode.Floor;

        /// <summary>포획 굴림도 전투 피로 +1(A10, R12). 기본 켬. 끄면 공격 굴림만 센다. 확정(원작 커뮤니티 확인 — 피로 5에서도 포획 굴림마다 3 피해).</summary>
        public bool CaptureAddsFatigue = true;

        /// <summary>운명의 변화 「턴당 효과 1개」의 단위(A11). 기본 맵 턴 1 + 배틀 턴마다 1, 따로 셈. 확정(원작 커뮤니티 확인).</summary>
        public FateLimitScope FateLimitScope = FateLimitScope.SeparateMapAndBattleTurns;

        /// <summary>상대 배틀 턴에도 운명의 변화를 쓴다(디아루가 재굴림을 상대 굴림에, A11·04 U15-5 [가정]). 기본 켬. 원작 커뮤니티 확인, 남의 배틀에는 못 쓴다.</summary>
        public bool FateOnOpponentBattleTurn = true;

        /// <summary>운명의 변화 손패 6장째 처리(A12). 기본 즉시 골라 버림. 원작 커뮤니티 답 대기.</summary>
        public FateHandOverflow FateHandOverflow = FateHandOverflow.DiscardImmediately;

        /// <summary>
        /// 트레이드로 받은 보통·강함 포켓몬이 트레이너 카드 해금 조건(「defeat or obtain」)에 든다(A20-3 · A29). 기본 끔 — 포획 · 카드 효과로 얻은 것만(사용자 결정 76 ⑦ T3 나).
        /// 켜면 문구대로 센다(둘이 같은 포켓몬을 주고받아 서로 여는 길이 생긴다). 해금 자체는 트레이너 카드(M6)가 만든다 — 거래 경로(TradeRules)는 이 값을 보고 세지 않는다.
        /// </summary>
        public bool UnlockTrade;

        /// <summary>다른 트레이너의 보통·강함 포켓몬을 쓰러뜨려도 해금 조건에 든다(A20-3). 기본 켬. 갤럭시단은 아이콘이 달라 늘 불인정. 원작 커뮤니티 확인(권고).</summary>
        public bool UnlockDefeatOtherTrainer = true;

        /// <summary>진화로 보통·강함이 된 것을 「얻음」으로 친다(A20-4). 기본 켬 — 원작 커뮤니티 확인(전에는 끔 [가정]).</summary>
        public bool UnlockByEvolution = true;

        /// <summary>순수 전설(노랑 아이콘)이 「강함」 해금 조건을 채운다(A20-4). 기본 끔 — 원작 커뮤니티 확인.</summary>
        public bool UnlockLegendaryAsStrong;

        /// <summary>우두머리(강함 카드 + 우두머리 토큰)가 「강함」 해금 조건을 채운다(A20-4). 기본 끔 — 원작 커뮤니티 확인(전에는 켬 [가정, 아이콘 기준]).</summary>
        public bool UnlockAlphaAsStrong;

        /// <summary>
        /// 브론즈에서 강함 조건을 먼저 채웠을 때(A20-5). 기본 순서대로·기억 안 함(SilverOnly) — 원작 커뮤니티 확인, 가장 늦은 원작 커뮤니티 확인
        /// [가정: 먼저 한 강함 달성을 기억하나는 Lev 답이 엇갈린다]. 전에는 바로 골드.
        /// </summary>
        public UnlockOrder UnlockOrder = UnlockOrder.SilverOnly;

        /// <summary>UB가 한 턴에 균열 10% 구간을 여러 개 넘을 때 균열 카드 수(B4 [가정]). 기본 넘은 구간마다.</summary>
        public BreachCardsPerBracket BreachCardsPerBracket = BreachCardsPerBracket.EachBracketCrossed;

        /// <summary>우선도 동률 비교 값(B5 [가정]). 기본 보정 후 우선도(마비 −3, 민첩의 띠 +2 등).</summary>
        public InitiativeTie InitiativeTie = InitiativeTie.ModifiedPriority;

        /// <summary>무녀 빨강 「Temporarily」(G2 [가정]). 기본 매번 다시 계산(조각을 얻고 UB를 쓰러뜨리는 만큼 회복).</summary>
        public AcolyteRedTemporary AcolyteRedTemporary = AcolyteRedTemporary.Recalculate;

        /// <summary>collector R 뒷부분 「when you discard any Pokémon or moves, add them to your stamp rewards move pool」(rulings A33 · A39 [가정 plan §5 #139 ②]). 기본 턴에 한 번.</summary>
        public CollectorDiscards CollectorDiscards = CollectorDiscards.OncePerTurn;

        /// <summary>
        /// 아르세우스의 추종자 토큰(undivided_acolyte B)이 아르세우스 3장 효과(울트라 버스트 판의 축복 +1)에서도 카드 2장 몫이다(rulings A33 · A40 — 원작 커뮤니티 확인 두 번, 카드 문구에는 없다 [가정 plan §5 #139 ④]).
        /// 기본 켬. 끄면 문구대로 — 세 신의 추종자일 뿐이라 아르세우스 카드는 세지 않는다.
        /// </summary>
        public bool ArceusFollowerBlessing = true;

        /// <summary>피해 상한(3·6)을 거는 자리(G4 [가정], 01 §6-4). 기본 맨 마지막.</summary>
        public DamageCapStage DamageCapStage = DamageCapStage.Last;

        /// <summary>
        /// UB·네크로즈마·야생(전설 · 우두머리 · 신 · 시련 · 무리)의 기술 선택(A16, 결정 26). 기본 강한 AI(결정 122 — 사람이 조종하지 않는 쪽 전부) ·
        /// p.25 원칙(원작 커뮤니티 확인)은 옵션. 확인용 판(솔로 설정 형의 기본 SoloSettings.NonPlayer)과 튜토리얼 · 봇 판(원칙 AI를 꽂는다)은 원칙.
        /// </summary>
        public NonPlayerMovePolicy NonPlayerMovePolicy = NonPlayerMovePolicy.Strong;

        /// <summary>갤럭시단은 내 포켓몬을 쓰러뜨린 직후 교체 못 함(A16, S7_10). 기본 켬. 끄면 룰북의 「더 어려운 배틀」.</summary>
        public bool GalacticNoSwitchAfterKo = true;

        /// <summary>트레이너 카드 확장(결정 14 ④, A20-8). 기본 켬.</summary>
        public bool TrainerCards = true;

        /// <summary>울트라 온슬롯 실험 규칙(A21, 울트라 버스트 전용). 기본 끔.</summary>
        public bool UltraOnslaught;

        /// <summary>울트라 버스트 난이도 = 아르세우스의 축복 수(A14: 3 / 1 / 0). 기본 New Trainer(결정 29 ⑤).</summary>
        public UbDifficulty UbDifficulty = UbDifficulty.NewTrainer;

        /// <summary>UB 이동(R4·R16). 기본 Classic(Lua R4 점수표). Personality(R16 개별 성격)는 M6에서 정한다.</summary>
        public UbMovement UbMovement = UbMovement.Classic;

        /// <summary>배틀 참가 중 기술 가르치기 시점(04 U15-1, rulings G10 [가정]). 기본 공격 처리 중만 막음.</summary>
        public TeachInBattle TeachInBattle = TeachInBattle.BlockDuringAttack;

        /// <summary>스탬프 보상 기술도 배울 수 있는 타입에만 가르친다(04 U15-2, rulings G10 [추정]). 기본 켬.</summary>
        public bool StampMoveTypeLimit = true;

        /// <summary>
        /// 은하의 응징을 옮기는 시점(rulings G5 [가정]). 기본 턴 끝(독처럼, 배틀이 끝났으면 없음 — 원작 커뮤니티 확인 2024 답, 결정 72). 전에는 피해 전(공격 피해와 동시 적용).
        /// 옛 저장은 적힌 BeforeDamage 그대로다.
        /// </summary>
        public GalacticRetributionTiming GalacticRetributionTiming = GalacticRetributionTiming.TurnEnd;

        /// <summary>같은 창에 여러 좌석이 반응할 때 묻는 방식(rulings G6 [가정]). 기본 한꺼번에. 바퀴 규칙과 적용 순서는 둘이 같다.</summary>
        public ReactionAsk ReactionAsk = ReactionAsk.Simultaneous;

        /// <summary>이동력이 0인 칸에서도 맵 상호작용(찾아보기·규토리 줍기·숨겨진 장소 공개)을 한다(rulings G11 [가정]). 기본 켬. 끄면 이동력 1 이상일 때만. 마을 행동은 옵션과 상관없다.</summary>
        public bool InteractAtZeroMovement = true;

        /// <summary>
        /// 둘째 기술(quick_attack 등 「The user may use a second move this turn」, rulings G21 [가정 — 규칙 끝 물을 것 ⑸]): 첫 공격으로 대상이 쓰러지면 다음에 나오는 포켓몬에게 둘째 기술을 쓴다.
        /// 기본 끔 — 쓰러지면 둘째 기술은 없다(그 효과를 쓰지 않았다). 원작 커뮤니티 확인 · 2023-08-10 · 1139090927110074414.
        /// </summary>
        public bool SecondMoveAfterKo;

        /// <summary>
        /// 둘째 기술을 묻는데 「안 씀」으로 답해도 「Effect may be used once until the user switches out」을 쓴 것으로 친다(rulings G21). 기본 켬 — 교체할 때까지 다시 없다(사용자 결정 76 ⑤ B1 ② 나).
        /// 끄면 안 쓴 것 — 다음에 그 기술을 쓸 때 다시 묻는다(원작 커뮤니티 확인).
        /// </summary>
        public bool SecondMoveDeclineSpends = true;

        /// <summary>
        /// 둘째 기술을 쓴 다음 배틀 턴의 「used last turn」(혼란 · power_whip · soft_boiled · disguise)이 어느 기술인가(rulings G21). 기본 둘째로 쓴 기술만(사용자 결정 76 ⑥ B1 ③ 나).
        /// BothMoves = 두 기술 모두(원작 커뮤니티 확인).
        /// </summary>
        public SecondMoveLastTurn SecondMoveLastTurn = SecondMoveLastTurn.SecondOnly;

        /// <summary>둘째 기술이 있는 배틀 턴의 턴 끝(독 · 턴 끝 회복 · 교체 기술 · 은하의 응징 턴 끝)이 몇 번인가(rulings G21 [가정]). 기본 두 공격 뒤 한 번(「on that turn you get to use a second move」 — 한 배틀 턴).</summary>
        public SecondMoveTurnEnd SecondMoveTurnEnd = SecondMoveTurnEnd.Once;

        /// <summary>트레이너끼리 거래(같은 칸 · 같은 신의 추종자 원격, rulings A29). 기본 켬. 끄면 그 판 내내 거래 답이 나오지 않는다 — 판을 만들 때 고른다(사용자 결정 75 ⑤). 원격 도전과는 상관없다.</summary>
        public bool Trading = true;

        /// <summary>
        /// 굴린 뒤 면 바꾸기의 순서를 묻지 않고 기본 순서(졸림 먼저)로 처리한다 — 결정 72 선택 창(EffectRules.AskOrder) 전의 판. 고를 수 있는 옵션이 아니다:
        /// 규칙 판이 4보다 낮은 저장을 이어 받으면 세션이 켠다(LocalSession — 옛 로그의 명령 줄에는 그 선택이 없어서, 다시 적용할 때 선택 Id가 어긋나지 않게).
        /// 저장에 넣지 않고([NonSerialized], 판 번호는 머리에 있다) 해시에도 넣지 않는다. 새 판은 늘 끔.
        /// </summary>
        [System.NonSerialized] public bool FixedEffectOrder;

        /// <summary>GameState 복제(01 §2-3). 옵션을 더하면 여기와 Walk에도 더한다(반사 시험 M3_복제는모든필드를복사).</summary>
        public void CopyFrom(RuleOptions o)
        {
            ElevationMode = o.ElevationMode;
            CaptureAddsFatigue = o.CaptureAddsFatigue;
            FateLimitScope = o.FateLimitScope;
            FateOnOpponentBattleTurn = o.FateOnOpponentBattleTurn;
            FateHandOverflow = o.FateHandOverflow;
            UnlockTrade = o.UnlockTrade;
            UnlockDefeatOtherTrainer = o.UnlockDefeatOtherTrainer;
            UnlockByEvolution = o.UnlockByEvolution;
            UnlockLegendaryAsStrong = o.UnlockLegendaryAsStrong;
            UnlockAlphaAsStrong = o.UnlockAlphaAsStrong;
            UnlockOrder = o.UnlockOrder;
            BreachCardsPerBracket = o.BreachCardsPerBracket;
            InitiativeTie = o.InitiativeTie;
            AcolyteRedTemporary = o.AcolyteRedTemporary;
            CollectorDiscards = o.CollectorDiscards;
            ArceusFollowerBlessing = o.ArceusFollowerBlessing;
            DamageCapStage = o.DamageCapStage;
            NonPlayerMovePolicy = o.NonPlayerMovePolicy;
            GalacticNoSwitchAfterKo = o.GalacticNoSwitchAfterKo;
            TrainerCards = o.TrainerCards;
            UltraOnslaught = o.UltraOnslaught;
            UbDifficulty = o.UbDifficulty;
            UbMovement = o.UbMovement;
            TeachInBattle = o.TeachInBattle;
            StampMoveTypeLimit = o.StampMoveTypeLimit;
            GalacticRetributionTiming = o.GalacticRetributionTiming;
            ReactionAsk = o.ReactionAsk;
            InteractAtZeroMovement = o.InteractAtZeroMovement;
            SecondMoveAfterKo = o.SecondMoveAfterKo;
            SecondMoveDeclineSpends = o.SecondMoveDeclineSpends;
            SecondMoveLastTurn = o.SecondMoveLastTurn;
            SecondMoveTurnEnd = o.SecondMoveTurnEnd;
            Trading = o.Trading;
            FixedEffectOrder = o.FixedEffectOrder;
        }

        /// <summary>StateHash 훑기(GameState.Walk 맨 앞).</summary>
        public void Walk(IStateWriter w)
        {
            w.Int((int)ElevationMode);
            w.Int(CaptureAddsFatigue ? 1 : 0);
            w.Int((int)FateLimitScope);
            w.Int(FateOnOpponentBattleTurn ? 1 : 0);
            w.Int((int)FateHandOverflow);
            w.Int(UnlockTrade ? 1 : 0);
            w.Int(UnlockDefeatOtherTrainer ? 1 : 0);
            w.Int(UnlockByEvolution ? 1 : 0);
            w.Int(UnlockLegendaryAsStrong ? 1 : 0);
            w.Int(UnlockAlphaAsStrong ? 1 : 0);
            w.Int((int)UnlockOrder);
            w.Int((int)BreachCardsPerBracket);
            w.Int((int)InitiativeTie);
            w.Int((int)AcolyteRedTemporary);
            w.Int((int)CollectorDiscards);
            w.Int(ArceusFollowerBlessing ? 1 : 0);
            w.Int((int)DamageCapStage);
            w.Int((int)NonPlayerMovePolicy);
            w.Int(GalacticNoSwitchAfterKo ? 1 : 0);
            w.Int(TrainerCards ? 1 : 0);
            w.Int(UltraOnslaught ? 1 : 0);
            w.Int((int)UbDifficulty);
            w.Int((int)UbMovement);
            w.Int((int)TeachInBattle);
            w.Int(StampMoveTypeLimit ? 1 : 0);
            w.Int((int)GalacticRetributionTiming);
            w.Int((int)ReactionAsk);
            w.Int(InteractAtZeroMovement ? 1 : 0);
            w.Int(SecondMoveAfterKo ? 1 : 0);
            w.Int(SecondMoveDeclineSpends ? 1 : 0);
            w.Int((int)SecondMoveLastTurn);
            w.Int((int)SecondMoveTurnEnd);
            w.Int(Trading ? 1 : 0);
        }

        /// <summary>Walk의 거울(02 §4-4 스트림 되읽기). FixedEffectOrder는 해시·스트림 밖이라 그대로 둔다.</summary>
        public void Read(IStateReader r)
        {
            ElevationMode = (ElevationMode)r.Int();
            CaptureAddsFatigue = r.Int() != 0;
            FateLimitScope = (FateLimitScope)r.Int();
            FateOnOpponentBattleTurn = r.Int() != 0;
            FateHandOverflow = (FateHandOverflow)r.Int();
            UnlockTrade = r.Int() != 0;
            UnlockDefeatOtherTrainer = r.Int() != 0;
            UnlockByEvolution = r.Int() != 0;
            UnlockLegendaryAsStrong = r.Int() != 0;
            UnlockAlphaAsStrong = r.Int() != 0;
            UnlockOrder = (UnlockOrder)r.Int();
            BreachCardsPerBracket = (BreachCardsPerBracket)r.Int();
            InitiativeTie = (InitiativeTie)r.Int();
            AcolyteRedTemporary = (AcolyteRedTemporary)r.Int();
            CollectorDiscards = (CollectorDiscards)r.Int();
            ArceusFollowerBlessing = r.Int() != 0;
            DamageCapStage = (DamageCapStage)r.Int();
            NonPlayerMovePolicy = (NonPlayerMovePolicy)r.Int();
            GalacticNoSwitchAfterKo = r.Int() != 0;
            TrainerCards = r.Int() != 0;
            UltraOnslaught = r.Int() != 0;
            UbDifficulty = (UbDifficulty)r.Int();
            UbMovement = (UbMovement)r.Int();
            TeachInBattle = (TeachInBattle)r.Int();
            StampMoveTypeLimit = r.Int() != 0;
            GalacticRetributionTiming = (GalacticRetributionTiming)r.Int();
            ReactionAsk = (ReactionAsk)r.Int();
            InteractAtZeroMovement = r.Int() != 0;
            SecondMoveAfterKo = r.Int() != 0;
            SecondMoveDeclineSpends = r.Int() != 0;
            SecondMoveLastTurn = (SecondMoveLastTurn)r.Int();
            SecondMoveTurnEnd = (SecondMoveTurnEnd)r.Int();
            Trading = r.Int() != 0;
        }
    }

    /// <summary>둘째 기술을 쓴 다음 턴의 「used last turn」(rulings G21).</summary>
    public enum SecondMoveLastTurn
    {
        /// <summary>그 턴에 쓴 두 기술 모두.</summary>
        BothMoves,
        /// <summary>둘째(마지막) 기술만.</summary>
        SecondOnly,
    }

    /// <summary>둘째 기술이 있는 배틀 턴의 턴 끝(rulings G21).</summary>
    public enum SecondMoveTurnEnd
    {
        /// <summary>두 공격 뒤 한 번 — 첫 공격 뒤에는 공격 끝(한 번짜리 보정 · 직전 기술)만.</summary>
        Once,
        /// <summary>공격마다(첫 공격도 턴 끝·기절 확인·교체까지 다 돈 뒤 둘째 기술).</summary>
        EachAttack,
    }

    /// <summary>A6 높이 판정 방식.</summary>
    public enum ElevationMode
    {
        /// <summary>층 = floor(level). 층 차 ≥ 2면 막힘(rulings A6 결론).</summary>
        Floor,
        /// <summary>|level 차| ≥ 2.0이면 막힘. 숲 1.25↔산 3, 호수 0.75↔산 2가 통과한다(A6 남은 해석).</summary>
        RawDiff,
    }

    /// <summary>운명의 변화 「턴당 효과 1개」의 단위(A11).</summary>
    public enum FateLimitScope
    {
        /// <summary>배틀 밖 내 턴 1개 + 배틀 턴(공격 1번 단위)마다 1개, 따로 센다.</summary>
        SeparateMapAndBattleTurns,
        /// <summary>배틀을 포함한 내 턴 전체에 1개(비공식 튜토리얼 해석).</summary>
        WholeTurn,
    }

    /// <summary>운명의 변화 손패 6장째(A12).</summary>
    public enum FateHandOverflow
    {
        /// <summary>바로 골라 버려 5장으로. 그 전에는 다른 행동을 하지 않는다.</summary>
        DiscardImmediately,
        /// <summary>턴 끝에 5장으로.</summary>
        DiscardAtTurnEnd,
    }

    /// <summary>트레이너 카드 해금 순서(A20-5).</summary>
    public enum UnlockOrder
    {
        /// <summary>브론즈에서 강함을 먼저 채우면 바로 골드(두 능력 해금). 옛 기본(보드의 두 문장을 따로 읽은 해석).</summary>
        StraightToGold,
        /// <summary>순서대로: 실버까지만 오르고, 골드는 실버가 된 뒤 강함을 새로 쓰러뜨리거나 얻어야 한다(먼저 한 강함 달성을 기억하지 않는다 — 원작 커뮤니티 확인 2024-01 답).</summary>
        SilverOnly,
        /// <summary>강함 달성을 기억해 두었다가 보통을 채우는 순간 골드.</summary>
        RememberThenGold,
    }

    /// <summary>균열 10% 구간을 여러 개 넘었을 때(B4).</summary>
    public enum BreachCardsPerBracket
    {
        /// <summary>넘은 구간 수만큼 균열 카드(룰북 p.24 「10% 증가마다」).</summary>
        EachBracketCrossed,
        /// <summary>턴에 1장(Lua 동작).</summary>
        OncePerTurn,
    }

    /// <summary>우선도 동률 비교 값(B5).</summary>
    public enum InitiativeTie
    {
        /// <summary>보정 후 우선도.</summary>
        ModifiedPriority,
        /// <summary>카드에 적힌 기본 우선도(Lua R11).</summary>
        BasePriority,
    }

    /// <summary>무녀 빨강 「Temporarily」(G2).</summary>
    public enum AcolyteRedTemporary
    {
        /// <summary>없는 조각 수와 못 쓰러뜨린 UB(3마리당 1)를 매번 다시 계산한다.</summary>
        Recalculate,
        /// <summary>해금 때 값으로 고정한다.</summary>
        FixedAtUnlock,
    }

    /// <summary>collector R 뒷부분(버린 포켓몬·기술이 스탬프 보상 풀로, rulings A33 — 원작 커뮤니티 확인).</summary>
    public enum CollectorDiscards
    {
        /// <summary>턴마다(누구의 턴이든) 한 장 — 제작자 권고(원작 커뮤니티 확인).</summary>
        OncePerTurn,
        /// <summary>문구대로 버릴 때마다.</summary>
        AsWritten,
        /// <summary>없음(원작 커뮤니티 확인).</summary>
        Off,
    }

    /// <summary>피해 상한을 거는 자리(G4, 01 §6-4).</summary>
    public enum DamageCapStage
    {
        /// <summary>⑦ 맨 마지막(감소·문턱 뒤).</summary>
        Last,
        /// <summary>⑤ 총 데미지 감소 전.</summary>
        BeforeReduction,
    }

    /// <summary>사람이 조종하지 않는 쪽의 기술 선택(A16).</summary>
    public enum NonPlayerMovePolicy
    {
        /// <summary>p.25 원칙(시그니처 기술 먼저, 아니면 공격력 + 상성 + 효과 피해 최대).</summary>
        Principle,
        /// <summary>강한 AI(결정 26).</summary>
        Strong,
    }

    /// <summary>울트라 버스트 난이도(A14). 이름은 룰북 p.23 원문.</summary>
    public enum UbDifficulty
    {
        /// <summary>아르세우스의 축복 3.</summary>
        NewTrainer,
        /// <summary>아르세우스의 축복 1.</summary>
        VeteranTrainer,
        /// <summary>아르세우스의 축복 0.</summary>
        MasterTrainer,
    }

    /// <summary>UB 이동 방식.</summary>
    public enum UbMovement
    {
        /// <summary>Lua 점수표(R4).</summary>
        Classic,
        /// <summary>피규어별 성격(R16, 원작에서 읽지 않던 값).</summary>
        Personality,
    }

    /// <summary>배틀 참가 중 기술 가르치기 시점(04 U15-1).</summary>
    public enum TeachInBattle
    {
        /// <summary>그 좌석 포켓몬의 공격이 처리 중(Battle.Step이 PreRoll~ApplyDamage)일 때만 막는다.</summary>
        BlockDuringAttack,
        /// <summary>내 배틀 차례 시작에만 된다.</summary>
        OwnBattleTurnStartOnly,
    }

    /// <summary>은하의 응징을 옮기는 시점(G5).</summary>
    public enum GalacticRetributionTiming
    {
        /// <summary>굴림 뒤·피해 전(BeforeDamage 창). 공격 피해와 동시에 적용하고, 이번 공격의 기절로 생긴 2는 다음 공격에 쓴다.</summary>
        BeforeDamage,
        /// <summary>피해 적용·기절 확인 뒤. 이번 기절의 2도 옮기고, 옮긴 피해는 따로 적용한 뒤 기절을 다시 본다(마지막 한 방에도 옮겨 무승부가 될 수 있다 — 원작 커뮤니티 확인 2023 답 쪽).</summary>
        AfterFaint,
        /// <summary>
        /// 공격받은 쪽이 아니라 공격한 쪽의 배틀 턴 끝(BattleTurnEnd 창, 독처럼 — 원작 커뮤니티 확인).
        /// 공격 피해를 적용한 뒤 묻고, 옮긴 피해는 턴 끝 피해(독)와 함께 적용한다. 이번 공격으로 배틀이 끝나면(마지막 갤럭시단 포켓몬을 쓰러뜨림 등) 묻지 않는다
        /// (「so if i KO the final Galactic pokemon, that damage trigger doesnt activate?」 → 「Yeah I'd allow that」). 이번 기절의 2는 기절 확인에서 저장돼 다음 공격에 쓴다.
        /// </summary>
        TurnEnd,
    }

    /// <summary>같은 창의 여러 좌석 반응을 묻는 방식(G6).</summary>
    public enum ReactionAsk
    {
        /// <summary>쓸 수 있는 좌석에 한꺼번에 묻고 정한 순서로 적용한다.</summary>
        Simultaneous,
        /// <summary>적용 순서대로 한 좌석씩 묻는다. 앞 좌석의 결과를 보고 답한다.</summary>
        Sequential,
    }
}
