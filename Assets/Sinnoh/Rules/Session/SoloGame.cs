using System;

namespace Sinnoh.Rules.Session
{
    /// <summary>
    /// 솔로 설정(04 S09, 02 §3-3)에서 고르는 값: 캠페인 · 트레이너 카드 · 울트라 온슬롯 · 울트라 버스트 난이도 · 갤럭시단 AI 세기.
    /// 「세부 규칙」(RuleOptions의 나머지)은 rulings 기본값 그대로다 — 고치는 화면은 아직 없다. 자리·조종·제한 시간은 솔로에 없다(조종은 AI 고정, 시계 없음 — 02 §1-3·§3-3).
    /// </summary>
    public sealed class SoloSettings
    {
        public Campaign Campaign = Campaign.HeartOfSinnoh;
        /// <summary>트레이너 카드 확장(결정 14 ④, A20-8). 이 형의 기본은 켬(시험 · 확인용 판이 그대로 쓴다) — 사람이 여는 새 판의 기본은 <see cref="SoloGame.NewGameTrainerCards"/>(끔, 결정 134).</summary>
        public bool TrainerCards = true;
        /// <summary>울트라 온슬롯(A21). 울트라 버스트에서만 듣는다.</summary>
        public bool UltraOnslaught;
        /// <summary>울트라 버스트 난이도(A14). 기본 신인(결정 29 ⑤).</summary>
        public UbDifficulty UbDifficulty = UbDifficulty.NewTrainer;
        /// <summary>갤럭시단 AI 세기(결정 26). 이 형의 기본은 원칙 AI(시험 · 확인용 판 · 튜토리얼이 그대로 쓴다) — 사람이 여는 새 판의 기본은 <see cref="SoloGame.NewGameAi"/>(「강함」, 결정 118 ②)이고 솔로 설정 · 방 만들기가 그 값으로 연다.</summary>
        public GalacticAiLevel GalacticAi = GalacticAiLevel.Principle;
        /// <summary>
        /// 내 좌석의 주인공(<see cref="Protagonists.Keys"/> 가운데 하나, 결정 52·53 ④·60): 보드의 주인공 도트 말과 배틀에서 볼을 던지는 뒷모습이 이 인물이다.
        /// 겉모양뿐이라 규칙·AI 설정에는 들어가지 않는다. 여럿이 하는 판에서는 로비가 좌석마다 받는다(M7) [가정].
        /// </summary>
        public string Protagonist = Protagonists.Default;
        /// <summary>플레이어끼리의 거래(결정 75 ⑤ — 판을 만들 때 켜고 끄며 그 판 내내 적용, rulings A29 ②). 기본 켬. 혼자 하는 판에는 거래 상대가 없다.</summary>
        public bool Trading = true;
        /// <summary>
        /// 사람이 조종하지 않는 쪽(UB · 네크로즈마 · 야생)의 기술 선택(A16 · 결정 122). 이 형의 기본은 원칙(시험 · 확인용 판이 그대로 쓴다 — 찾아 둔 판이 결정적이게) —
        /// 사람이 여는 새 판은 <see cref="SoloGame.NewGameNonPlayer"/>(강한 AI)로 연다.
        /// </summary>
        public NonPlayerMovePolicy NonPlayer = NonPlayerMovePolicy.Principle;
        /// <summary>
        /// 무르기(결정 149). 이 형의 기본은 끔(시험 · 확인용 판이 그대로 쓴다) — 사람이 여는 새 판은 <see cref="SoloGame.NewGameUndo"/>(내 턴 무르기, 결정 154 ①)로 연다.
        /// 규칙이 아니라 <see cref="ToOptions"/>에 넣지 않는다 — <see cref="SoloGame.Start"/>가 <see cref="SessionConfig.Undo"/>로 넘긴다.
        /// </summary>
        public UndoMode Undo;

        /// <summary>이 설정의 규칙 옵션: 고른 값만 넣고 나머지는 rulings 기본값(결정 14 ①). 울트라 온슬롯은 울트라 버스트일 때만 켠다.</summary>
        public RuleOptions ToOptions() => new RuleOptions
        {
            TrainerCards = TrainerCards,
            Trading = Trading,
            UltraOnslaught = Campaign == Campaign.UltraBurst && UltraOnslaught,
            UbDifficulty = UbDifficulty,
            NonPlayerMovePolicy = NonPlayer,
        };

        /// <summary>AI·조종 설정: 조종은 AI 고정(02 §3-3). aiSeed는 게임 시드와 무관한 값이어야 한다(01 §4·§9-1).</summary>
        public AiSettings ToAi(ulong aiSeed) => new AiSettings { GalacticAiLevel = GalacticAi, Seed = aiSeed };

        public SoloSettings Clone() => (SoloSettings)MemberwiseClone();

        /// <summary>세이브 머리에서 솔로 설정을 되살린다(이어 받기 — 설정 화면을 다시 열면 그 판의 값이 보인다). 주인공이 없거나 모르는 키면 기본.</summary>
        public static SoloSettings FromHead(SaveHead head)
        {
            if (head == null) throw new ArgumentNullException(nameof(head));
            string who = head.Protagonists != null && head.Protagonists.Count > SoloGame.Seat ? head.Protagonists[SoloGame.Seat] : null;
            return new SoloSettings
            {
                Campaign = head.Campaign, TrainerCards = head.Options.TrainerCards, UltraOnslaught = head.Options.UltraOnslaught, UbDifficulty = head.Options.UbDifficulty,
                GalacticAi = head.Ai != null ? head.Ai.GalacticAiLevel : GalacticAiLevel.Principle, Protagonist = Protagonists.Has(who) ? who : Protagonists.Default, Trading = head.Options.Trading,
                NonPlayer = head.Options.NonPlayerMovePolicy, Undo = head.Undo,
            };
        }
    }

    /// <summary>
    /// 고를 수 있는 주인공 8명(Pt·HGSS·BW·B2W2 남·여, 04 §3-1). 키 = 공식 뒷모습 시트 이름(Gen <c>Trainers/Back/official/&lt;키&gt;.png</c>)이고,
    /// 필드 도트 표(Gen <c>Field/field.json</c>)의 행도 같은 키로 찾는다. 순서 = 고르는 화면에 놓는 순서.
    /// </summary>
    public static class Protagonists
    {
        public static readonly string[] Keys =
        {
            "pt_player_male", "pt_player_female", "hgss_000_ethan", "hgss_001_lyra", "bw_000_hilbert", "bw_001_hilda", "b2w2_000_nate", "b2w2_001_rosa",
        };

        /// <summary>고르지 않았을 때의 주인공: Pt 남자 주인공 [가정].</summary>
        public const string Default = "pt_player_male";

        public static bool Has(string key) => Array.IndexOf(Keys, key) >= 0;

        /// <summary>방 좌석의 주인공(M9 다듬기 P06): 고를 수 있는 키면 그대로, 아니면(고르지 않음 · 모르는 키) 자리 번호로 목록을 돈다 — 고르지 않은 좌석끼리 서로 다르게 [가정 M7-94].</summary>
        public static string ForSeat(string key, int seat) => Has(key) ? key : Keys[((seat % Keys.Length) + Keys.Length) % Keys.Length];

        /// <summary>Pt 라이벌(용식) — 고를 수 없는 손님 인물(결정 96 Q5 나): 튜토리얼 여럿 판의 봇 좌석 1 「용식」의 얼굴 상자·보드 말.</summary>
        public const string Rival = "pt_rival";

        /// <summary>Pt 여자 주인공(빛나) — 튜토리얼 여럿 판의 봇 좌석 2 「빛나」(결정 96 Q5 나). 고를 수 있는 주인공이기도 하다.</summary>
        public const string Dawn = "pt_player_female";

        /// <summary>
        /// 손님 인물(고르는 화면에 없다): 얼굴 상자 그림(Gen <c>Trainers/trainers.json</c> heroes 행)과 필드 도트 행(<c>Field/field.json</c>)만 주인공처럼 있다 —
        /// 볼 던지기 순서는 없어 내 좌석에는 쓰지 않는다. 순서 = <c>Tools/assets/lib/trainers.mjs</c>의 GUESTS.
        /// </summary>
        public static readonly string[] Guests = { Rival };

        /// <summary>튜토리얼 판의 좌석 인물(결정 96 Q5 나): 좌석 0 = 내 주인공 · 1 = 용식(Pt 라이벌) · 2 = 빛나(Pt 여자 주인공) · 그 뒤는 주인공 목록을 돈다.</summary>
        public static string[] ForTutorial(string mine, int seats)
        {
            var who = new string[seats];
            for (int i = 0; i < seats; i++) who[i] = i == 0 ? (Has(mine) ? mine : Default) : i == 1 ? Rival : i == 2 ? Dawn : Keys[i % Keys.Length];
            return who;
        }
    }

    /// <summary>
    /// 솔로 게임 조립(02 §1-3): 좌석 하나 + <see cref="LocalSession"/>. 가상 응답자 AI는 조립 지점(Client·시험)이 꽂는다 — Rules는 Ai를 모른다(01 §1).
    /// 좌석은 하나다(결정 14 ②). 핫시트·AI 트레이너는 없다(04 U8).
    /// </summary>
    public static class SoloGame
    {
        public const int SeatCount = 1;
        /// <summary>사람의 좌석.</summary>
        public const int Seat = 0;

        /// <summary>
        /// 지금 시작할 수 있는 캠페인인가: 신오의 심장 · 울트라 버스트(M6 「UB 시작」 — 셋업·UB 턴은 규칙 묶음이 만들었고 솔로 설정에서 연 것은 화면 묶음이다.
        /// 균열 게이지 · UB 턴 띠 · 균열 카드 창은 아직 없다 — 목업 고르기 뒤).
        /// </summary>
        public static bool CampaignReady(Campaign campaign) => campaign == Campaign.HeartOfSinnoh || campaign == Campaign.UltraBurst;

        /// <summary>지금 고를 수 있는 AI 세기인가: 원칙 · 강함 둘 다(M8 「연결」 — 강한 AI는 조립 지점이 <see cref="VirtualAgents.Strong"/>에 꽂는다).</summary>
        /// <summary>사람이 여는 새 판(솔로 설정 · 방 만들기)의 갤럭시단 AI 세기 기본값 = 「강함」(결정 118 ② — 옛 [가정] M8-34를 고침). 이어하기는 저장된 세기 그대로.</summary>
        public const GalacticAiLevel NewGameAi = GalacticAiLevel.Strong;

        /// <summary>사람이 여는 새 솔로 판의 UB · 네크로즈마 · 야생 기술 선택 = 강한 AI(결정 122). 방은 방 설정의 규칙 옵션이 <see cref="RuleOptions"/> 기본값(강한 AI) 그대로다.</summary>
        public const NonPlayerMovePolicy NewGameNonPlayer = NonPlayerMovePolicy.Strong;
        /// <summary>사람이 여는 새 판(솔로 설정 · 방 만들기)의 트레이너 카드 기본값 = 끔(결정 134 — 「켬」은 테스트 중인 기능). 이어하기는 저장된 값 그대로.</summary>
        public const bool NewGameTrainerCards = false;
        /// <summary>사람이 여는 새 솔로 판의 무르기 기본값 = 내 턴 무르기(결정 154 ①). 새 방은 끔(<see cref="RoomSettings.Undo"/> 기본). 이어하기는 저장된 값 그대로.</summary>
        public const UndoMode NewGameUndo = UndoMode.Turn;

        public static bool AiReady(GalacticAiLevel level) => level == GalacticAiLevel.Principle || level == GalacticAiLevel.Strong;

        /// <summary>가상 응답자 AI 고르기(<see cref="VirtualAgents"/>)에 솔로 세기를 맞춘다 — 솔로는 조종이 AI 고정(사람 조종 없음). 다른 AI면 그대로.</summary>
        static void SyncAgents(IAgent virtualAgent, GalacticAiLevel level)
        {
            if (!(virtualAgent is VirtualAgents va)) return;
            va.GalacticLevel = level;
            va.GalacticHuman = false;
        }

        /// <summary>
        /// 새 솔로 게임을 시작한다(NewGame + Start — 셋업의 첫 선택이 열린 상태). seed = 게임 시드(엔진 Rng), aiSeed = AI 난수(게임 시드와 무관하게).
        /// 아직 없는 캠페인·AI 세기면 NotSupportedException. store가 null이면 저장하지 않는다.
        /// </summary>
        /// <param name="prepare">확인용(캡처·시험·플레이어 확인): 시작 상태(셋업의 첫 선택이 열린 호스트 원본의 사본)를 손본 판으로 시작한다 — 볼을 얻는 길(제작, M5)이 생기기 전에
        /// 포획 화면을 지나려고 볼을 쥐여 주는 데 쓴다. 게임 화면은 넘기지 않는다(null). 손본 판은 <see cref="LocalSession.FromState"/>로 시작해 머리의 시드가 0이다(저장하지 않는 판에서만 쓴다).</param>
        /// <param name="virtualAgent">가상 응답자 AI. <see cref="VirtualAgents"/>면 솔로 세기(settings.GalacticAi)를 맞춘다(M8 「연결」).</param>
        /// <param name="runner">AI 러너(<see cref="SessionConfig.Runner"/> — 「강함」 판의 작업 스레드, 갖는 쪽이 Dispose). null = 정해진 시각에 이 스레드에서 바로 묻는다.</param>
        public static LocalSession Start(Ctx ctx, SoloSettings settings, ulong seed, ulong aiSeed, IAgent virtualAgent, long nowMs,
            ISaveStore store = null, string saveId = null, string contentHash = null, Action<GameState> prepare = null, Func<long> savedAt = null, IAgentRunner runner = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (prepare != null && store != null) throw new ArgumentException("손본 판은 저장하지 않는다", nameof(prepare));
            if (!CampaignReady(settings.Campaign)) throw new NotSupportedException($"캠페인 {settings.Campaign}은 아직 시작할 수 없다(M6)");
            if (!AiReady(settings.GalacticAi)) throw new NotSupportedException($"갤럭시단 AI {settings.GalacticAi}은 아직 없다(M8)");
            SyncAgents(virtualAgent, settings.GalacticAi);
            var cfg = new SessionConfig
            {
                VirtualAgent = virtualAgent, SeatAgents = null, Store = store, SaveId = saveId, ContentHash = contentHash,
                Ai = settings.ToAi(aiSeed), ClockMs = null, SavedAt = savedAt, Protagonists = new[] { settings.Protagonist }, Runner = runner, Undo = settings.Undo,
            };
            var session = LocalSession.Start(ctx, settings.ToOptions(), SeatCount, seed, settings.Campaign, cfg, nowMs);
            if (prepare == null) return session;
            var state = session.HostCopy();
            prepare(state);
            return LocalSession.FromState(ctx, state, cfg, nowMs);
        }

        /// <summary>
        /// 솔로 세이브를 이어 받는다(02 §7): 스냅샷 + 그 뒤 명령을 다시 적용해 마지막 쉬는 때(<see cref="SaveRest"/>)의 상태로 돌아온다 — 난수(Rng)도 그때 그대로라 이어 한 판은 끊지 않은 판과 같다.
        /// 배틀·조우의 한가운데에서 끊긴 판은 그 앞의 쉬는 때로 돌아가고 그 뒤의 줄은 버린다(store가 있으면 로그에서도). AI 설정은 머리의 것을 쓴다 — 갤럭시단 세기도(<see cref="VirtualAgents"/>면 맞춘다).
        /// 솔로 세이브가 아니거나(좌석 수) 아직 없는 캠페인·AI 세기면 NotSupportedException. 데이터·형식 확인은 <see cref="LocalSession.Resume"/>. runner는 <see cref="Start"/>와 같다.
        /// </summary>
        public static LocalSession Resume(Ctx ctx, SaveRecord record, IAgent virtualAgent, long nowMs, ISaveStore store = null, string contentHash = null, Func<long> savedAt = null, IAgentRunner runner = null)
        {
            if (record?.Head == null) throw new ArgumentException("머리가 없는 세이브", nameof(record));
            var head = record.Head;
            if (head.SeatCount != SeatCount) throw new NotSupportedException($"솔로 세이브가 아니다(좌석 {head.SeatCount})");
            if (!CampaignReady(head.Campaign)) throw new NotSupportedException($"캠페인 {head.Campaign}은 아직 시작할 수 없다(M6)");
            var ai = head.Ai ?? new AiSettings();
            if (!AiReady(ai.GalacticAiLevel)) throw new NotSupportedException($"갤럭시단 AI {ai.GalacticAiLevel}은 아직 없다(M8)");
            SyncAgents(virtualAgent, ai.GalacticAiLevel);
            var cfg = new SessionConfig
            {
                VirtualAgent = virtualAgent, SeatAgents = null, Store = store, SaveId = head.SaveId, ContentHash = contentHash, Ai = ai, ClockMs = null, SavedAt = savedAt, Runner = runner, Undo = head.Undo,
            };
            return LocalSession.Resume(ctx, record, cfg, nowMs, toRest: true);
        }
    }
}
