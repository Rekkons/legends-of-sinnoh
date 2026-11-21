using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>갤럭시단 AI 세기(결정 26). Strong은 M8 — 그 전에는 원칙 AI가 돈다.</summary>
    public enum GalacticAiLevel { Strong, Principle }

    /// <summary>
    /// AI·조종 방 설정(01 §5 아래, 02 §3-2). 규칙이 아니라서 RuleOptions 밖에 있고, AI 답은 명령으로 기록되므로 재현에 영향이 없다.
    /// 조종자 = 가상 응답자 값이면 AI, 0~5면 그 좌석(사람 조종, M7). 솔로는 AI 고정(02 §3-3).
    /// </summary>
    public sealed class AiSettings
    {
        public int GalacticController = SeatId.Galactic;
        public int UbController = SeatId.UltraBeast;
        public GalacticAiLevel GalacticAiLevel = GalacticAiLevel.Strong;
        /// <summary>결정당 시뮬레이션 상한(0 = AI 기본).</summary>
        public int MaxSimulations;
        /// <summary>결정당 마감(결정 29 ③).</summary>
        public int DeadlineMs = 2000;
        /// <summary>AI 난수 시드(AgentBudget.Seed = 이것과 선택 Id를 섞은 값). 게임 시드와 무관한 값을 호스트 조립 지점이 정한다 — 게임 시드에서 만들면 AI가 되돌려 숨은 상태를 재현한다(01 §4·§9-1).</summary>
        public ulong Seed;
    }

    /// <summary>표 하나의 문자열 키 목록(01 §10 IdTable). 데이터가 바뀌면 색인을 다시 맞추는 데 쓴다.</summary>
    public sealed class SaveIdTable
    {
        public string Name;
        public List<string> Keys = new List<string>();
    }

    /// <summary>저장 목록(04 S04)에 보일 요약.</summary>
    public sealed class SaveSummary
    {
        public Campaign Campaign;
        public int Round;
        /// <summary>공개된 막(신오의 심장, 없으면 −1).</summary>
        public int Act = -1;
        public Phase Phase;
        public int CommandCount;
        public bool Finished;
        /// <summary>이 요약을 쓴 때(UTC, 1970년부터의 ms). 조립 지점이 준 시계(<see cref="SessionConfig.SavedAt"/>)에서 받는다 — 없으면 0.</summary>
        public long SavedAtMs;
        /// <summary>좌석마다의 요약(좌석 순서). 색인은 머리의 IdTables 기준이다.</summary>
        public List<SaveSeatSummary> Seats = new List<SaveSeatSummary>();
    }

    /// <summary>저장 목록에 보일 좌석 하나: 고향 마을 · 트레이너 카드 · 여행 점수 · 파티(포켓몬 표 색인, 파티 칸 순서 — 빈칸은 뺀다).</summary>
    public sealed class SaveSeatSummary
    {
        public int HomeVillage = -1;
        public int TrainerCard = -1;
        public int Jp;
        public List<int> Party = new List<int>();
    }

    /// <summary>
    /// head.json(02 §7): SaveRecord 머리 + 요약 + 방 몫(<see cref="Room"/>, 방 저장만). Seed·IdTables·RuleOptions는 게임 시작 때 정하고 바뀌지 않는다.
    /// </summary>
    public sealed class SaveHead
    {
        public int FormatVersion = SaveRecord.CurrentFormat;
        public int RulesVersion = SaveRecord.CurrentRulesVersion;
        public string SaveId;
        public string ContentHash;
        public List<SaveIdTable> IdTables = new List<SaveIdTable>();
        public RuleOptions Options = new RuleOptions();
        public AiSettings Ai = new AiSettings();
        public Campaign Campaign;
        public int SeatCount;
        public ulong Seed;
        /// <summary>좌석마다의 주인공 키(<see cref="Protagonists.Keys"/>, 좌석 순서). 겉모양뿐이라 규칙·해시에 들지 않는다. 모르면 비어 있다.</summary>
        public List<string> Protagonists = new List<string>();
        public SaveSummary Summary = new SaveSummary();
        /// <summary>이 판의 무르기 설정(결정 149 — <see cref="SessionConfig.Undo"/>, 새 게임 때 적고 바뀌지 않는다). 규칙 밖이라 해시 · 체크포인트에 들지 않는다. 솔로 이어하기는 이 값으로 연다.</summary>
        public UndoMode Undo;
        /// <summary>방 몫(02 §7 head.json 「방」 — M7 「세션 ③」). 솔로 저장은 null. 방(<see cref="HostSession"/>)이 머리를 쓸 때마다 채운다(<see cref="SessionConfig.BeforeHead"/>).</summary>
        public SaveRoom Room;
    }

    /// <summary>
    /// 방 저장의 방 몫(02 §7): 방 설정 · 좌석(토큰·닉네임·PC/웹·강퇴됨, 좌석 순서) · 금지 토큰(로비 내보내기·강퇴 — 이어하기에도 못 들어온다) · 좌석별 뱅크와 뱅크를 충전한 라운드.
    /// 토큰은 호스트 PC에만 있다(손님에게 보내지 않는다, 02 §6-1). 자리 비움은 적지 않는다 — 이어하면 모두 다시 들어오고, 안 온 좌석은 끊김 규칙대로 간다 [가정 M7-20].
    /// </summary>
    public sealed class SaveRoom
    {
        public RoomSettings Settings = new RoomSettings();
        public List<SaveRoomSeat> Seats = new List<SaveRoomSeat>();
        public List<string> Banned = new List<string>();
        public List<long> Bank = new List<long>();
        /// <summary>뱅크를 마지막으로 충전한 라운드(<see cref="TurnClock.Round"/>) — 이어하며 다시 충전하지 않게.</summary>
        public int BankRound;
    }

    public sealed class SaveRoomSeat
    {
        public string Token;
        public string Nickname;
        public Platform Platform;
        /// <summary>강퇴 투표로 나갔다 — 끝까지 기본값(0초)이고 다시 들어오지 못한다(02 §6-2).</summary>
        public bool Kicked;
    }

    /// <summary>턴 끝마다 상태 해시(01 §10 Checkpoints). 다시 적용할 때 어긋남을 찾는다.</summary>
    public sealed class SaveCheckpoint
    {
        public int CommandCount;
        public ulong Hash;
    }

    /// <summary>snap-&lt;CommandCount&gt;.json(02 §7): 호스트 원본 GameState(Rng 포함) + 그때까지 Checkpoints. 처리 중 위치도 상태 안이라 어느 명령 뒤에서든 찍는다.</summary>
    public sealed class SaveSnapshot
    {
        public int CommandCount;
        public GameState State;
        public List<SaveCheckpoint> Checkpoints = new List<SaveCheckpoint>();
    }

    /// <summary>log.jsonl 한 줄(02 §7): Seq = 그 명령을 적용하기 전 CommandCount(0부터). 사람·AI·Timeout·정비·HostOp 모두. 거절된 명령은 없다.</summary>
    public sealed class SaveLogLine
    {
        public int Seq;
        public int Seat;
        public Command Command;
    }

    /// <summary>
    /// 세이브 하나를 읽은 결과(01 §10): 머리 + 가장 새 성한 스냅샷 + 그 뒤 명령. 이어하기 = 스냅샷 + 명령 다시 적용(LocalSession.Resume).
    /// 파일 형식·쓰기는 Data SaveStore가 한다(Rules는 Newtonsoft를 모른다).
    /// </summary>
    public sealed class SaveRecord
    {
        /// <summary>
        /// 파일 형식 판. 2 = 머리에 좌석 주인공·요약의 저장 시각·좌석 요약(M4-3 저장 기반). 다른 판(옛것·새것)은 열지 않는다 — 옛 형식을 옮기는 길은 없다 [가정:
        /// 1은 저장을 켜기 전의 형식이라 사용자 파일이 없다]. 3 = M5 효과 기반(스냅샷의 포켓몬에 지속 보정 Mods · 사용 기록에 Subject —
        /// 판 2 저장은 「읽을 수 없는 저장」이다, 결정 73 ①). 4 = M5 「막 뼈대」(스냅샷의 HosState에 이긴 좌석 Winner — 판 3 저장도 「읽을 수 없는 저장」).
        /// 5 = M5 「제작·은행·진화」(칸은 그대로지만 스냅샷에 든 값의 뜻이 바뀌었다 — 코드 상수 효과가 5 → 7이 되어 사용 기록·지속 보정의 효과 Id가 2씩 밀리고,
        /// 사용 기록 종류(스튜 셋)·지속 보정 op(MoveLock·ItemUsed·SwitchLock)가 늘었다. 판 4 저장도 「읽을 수 없는 저장」).
        /// 6 = M5 「운명 딜·손패」(스냅샷에 미뤄 둔 선택 Held · 떨어진 별 수 FallenStars · HosState의 배신자가 낸 운명 카드 TraitorFate — 판 5 저장도 「읽을 수 없는 저장」).
        /// 7 = M5 「기술 효과 — 지속 보정 갈래」(칸은 그대로지만 코드 상수 효과가 7 → 10이 되어 사용 기록·지속 보정의 효과 Id가 3씩 밀리고, 지속 보정 op가 늘었다 — 판 6 저장도 「읽을 수 없는 저장」).
        /// 8 = M5 「원작 대조 고침」(스냅샷의 포켓몬에 밑에 둔 옛 카드 Under · 사용 기록 AlluringStew가 맵 턴 단위 — 판 7 저장도 「읽을 수 없는 저장」).
        /// 9 = M5 「운명 네 장 효과」(배틀 상태에 기라티나 공격력 FatePower — 판 8 저장도 「읽을 수 없는 저장」).
        /// 10 = M5 「기술 효과 — 나머지 op 갈래」(배틀 상태에 효과가 정한 타입·공격력 TypeTo·StrengthTo · 턴 끝 교체 SwitchAfter · 쪽마다 이월 피해 Carry·CarrySource — 판 9 저장도 「읽을 수 없는 저장」).
        /// 11 = M5 「숨겨진 장소·유물·트레이너전 보상」(좌석의 유물 칸 Relics를 뺐다 — 유물은 가방 Bag에 든다. 판 10 저장도 「읽을 수 없는 저장」).
        /// 12 = M5 「전설·알파·폼」(칸은 그대로지만 뜻이 바뀌었다 — 포켓몬의 배틀 폼 칸이 「배틀 전 폼」 FormBeforeBattle이 되고 Def가 지금 폼이다 · 폼 기술 8장이 효과 표에 들어 효과 Id가 밀린다. 판 11 저장도 「읽을 수 없는 저장」).
        /// 13 = M5 「기술 효과 — 핸들러 갈래」·「사당 공물·카드」(배틀 상태에 둘째 기술·마지막 기술·복사·맞히기 칸 · 포켓몬에 직전 배틀 턴의 첫 기술 LastMove2 · 처리 중인 사당 공물 Shrine · 사용 기록 종류 ShrineOffer·Banquet · 효과 Id가 밀린다. 판 12 저장도 「읽을 수 없는 저장」).
        /// 14 = M5 「스탬프」·「거래·원격 도전」(배틀 쪽에 이어진 포획 실패 FailedCaptures · 좌석에 스탬프 보상 카드 StampCards · 규칙 옵션에 거래 Trading — 판 13 저장도 「읽을 수 없는 저장」).
        /// 15 = M5 「규칙 끝 검토 반영」(배틀 상태에 추가 공격으로 고른 알파 스트라이크의 타입 ExtraType — 판 14 저장도 「읽을 수 없는 저장」).
        /// 16 = 결정 76 답 반영(배틀 상태에 디아루가 창을 기다리는 포획 굴림의 볼 CaptureBall · 배틀 단계 Capture — 판 15 저장도 「읽을 수 없는 저장」).
        /// 17 = M6 「해금 뼈대」(좌석에 먼저 채운 강함 StrongRemembered · 아이템 표에 전용 아이템 11행 — 판 16 저장도 「읽을 수 없는 저장」).
        /// 18 = M6 「상시 보정」(포켓몬에 트레이너 카드의 최대 체력 보정 HealthMod — 판 17 저장도 「읽을 수 없는 저장」).
        /// 19 = M6 「배틀 굴림·공격력·타입」(배틀 상태에 CardAsk·HexAsked·HexAs·HexExtra · 배틀 쪽에 Masterwork · 좌석에 Moved · 포켓몬에 StatusMore · 코드 상수 효과 11 = 데이터 효과 Id가 하나씩 밀린다 — 판 18 저장도 「읽을 수 없는 저장」).
        /// 21 = M6 「이동」(좌석에 Glide·RoadChain·FreeLeft·Building·Closing·ClosingAfter · 판에 길 칸 Roads · 배틀 쪽에 Fallen — 판 20 저장도 「읽을 수 없는 저장」).
        /// 20 = M6 「배틀 피해 뒤·기절·교체·상태」(배틀 상태에 ScubaBy·BlastAsked·BlastSeat·BlastTo·EndResult·EndWinner · 배틀 쪽에 Potion · 좌석에 Firewood · 배틀 단계 Concluding — 판 19 저장도 「읽을 수 없는 저장」).
        /// 22 = M6 「맵 상호작용」(좌석에 ReachFrom · 토큰에 Special·Loose · 사용 기록 종류 Ability가 턴 단위 — 판 21 저장도 「읽을 수 없는 저장」).
        /// 23 = M6 「보상·남의 행동 반응」(좌석에 TentDue·FrenzyDue·FrenzyTurn·ScoopTarget·ScoopStamp·ScoopAt · 포켓몬에 CatchSize — 판 22 저장도 「읽을 수 없는 저장」).
        /// 24 = M6 「제작·사당·스탬프·진화·파티」(좌석에 Ranch · 포켓몬에 EggMove · 규칙 옵션에 CollectorDiscards — 판 23 저장도 「읽을 수 없는 저장」).
        /// 25 = M6 「제작·사당·스탬프·진화·파티」 backpacker R(포켓몬에 둘째 지닌물건 HeldItem2 — 판 24 저장도 「읽을 수 없는 저장」).
        /// 26 = M6 「무녀·카드 바꾸기」(좌석에 UnlockDue·UnlockAt·AcolyteToll · 포켓몬에 Chosen · 규칙 옵션에 ArceusFollowerBlessing · 추종자 값 Arceus — 판 25 저장도 「읽을 수 없는 저장」).
        /// 27 = M6 「전용 아이템 능력」(판에 Weather · 좌석에 Amp·CherishIn·Tour·Snaps · 배틀 상태에 AmpDamage · 배틀 쪽에 AmpSpent — 판 26 저장도 「읽을 수 없는 저장」).
        /// 28 = M6 「울트라 버스트 규칙 합침」(가지 wt-s2가 따로 올린 17~22를 하나로: 울트라 버스트 상태에 방문 표시 Visited · 손의 균열 카드 BreachHands · 결과 Result · 프리즘 아머 PrismUltraBeast · 격파 수 Defeated ·
        /// 건너뛸 UB 턴 SkipNext · 넘긴 시련 TrialsOvercome · 실버디 축복 SilvallyBlessed · 포켓몬의 균열 카드 칸이 기술 칸마다 Breach[2](BreachCard 한 칸을 대신) · 배틀 쪽에 「나올 때」 표시 Entered ·
        /// 배틀 상태에 PrismUsed · TrialNow·TrialPrev·TrialTurn · Onslaught·OnslaughtThorns · 선택 갈래 Revive · 정비 명령 UseBlessing — 판 27 저장도 「읽을 수 없는 저장」).
        /// 29 = M6 「질문 페이지 답 — 규칙」(사용자 결정 84: 좌석에 worker B가 켜기 전 밟은 칸 Trod · 울트라 버스트 상태에 pokemon_ranger B가 보관한 균열 카드 BreachStored · 선택 갈래 Blessing(축복 동의)·UbSkip(라운드 끝 건너뛰기) — 판 28 저장도 「읽을 수 없는 저장」).
        /// 30 = M6 「마감 답 반영 — 규칙」(사용자 결정 87: 정비 명령 AttachBreach — pokemon_ranger B가 보관한 균열 카드 붙이기 — 판 29 저장도 「읽을 수 없는 저장」).
        /// 31 = M7 「세션 ③」(머리에 방 몫 Room — 방 설정·좌석 토큰·금지 토큰·뱅크, 솔로는 null — 판 30 저장도 「읽을 수 없는 저장」).
        /// 32 = M7 「결정 112 반영」(방 설정에 일시정지를 거는 사람 Pause — 판 31 저장도 「읽을 수 없는 저장」).
        /// 33 = M9 실행 감사 K1(좌석에 interviewers B의 다음 물음 ScoopNext — 판 32 저장도 「읽을 수 없는 저장」).
        /// 34 = M10 결정 149 무르기(머리에 Undo · 방 설정에 Undo — 판 33 저장도 「읽을 수 없는 저장」).
        /// </summary>
        public const int CurrentFormat = 34;
        /// <summary>
        /// 규칙 코드 판(이어하기 경고용, 02 §7 호환 표). 규칙 결과를 바꾸는 커밋이 올린다 [가정]. 2 = M3 검토 반영(배운 기술 2칸·도망·포획 피로·기권).
        /// 3 = 원작 커뮤니티 확인 판정 반영(결정 72 — 마비는 바로 다음 자기 턴 공격에만 · 둘 다 졸림 상쇄 · 배틀이 끝난 턴의 턴 끝 피해 없음 · 은하의 응징 턴 끝).
        /// 4 = 결정 72 선택 창(굴린 뒤 면 바꾸기의 순서로 결과가 갈리면 공격자에게 묻는다 — 명령 줄에 새 선택이 끼인다).
        /// 5 = 결정 73 ②(순서는 데미지·쓰러짐이 갈릴 때만 묻는다 — 눈 개수만 다르면 묻지 않아 판 4 로그의 선택이 빠질 수 있다. 판 4 저장을 옮기는 길은 두지 않는다 — 결정 73 ①).
        /// 6 = 결정 73 ② 검토 반영(공격하는 포켓몬이 반동으로 한쪽 순서에서만 쓰러져도 묻는다 — 판 5 로그에 없던 선택이 끼일 수 있다. 옮기는 길은 두지 않는다).
        /// 7 = M5 「막 뼈대」(2막 간부·3막 심장·태홍·종막 — 맵 행동의 합법 답에 호수·신전 도전과 시작의 사당 공물이 끼고 게임이 끝난다. 옮기는 길은 두지 않는다).
        /// 8 = M5 「제작·은행·진화」(맵 행동의 합법 답에 제작·은행상회·진화가 끼고, 지닌물건 다섯이 배틀 결과를 바꾸고, 효과 Id가 2씩 밀린다. 옮기는 길은 두지 않는다).
        /// 9 = M5 「운명 딜·손패」(귀환한 다음 턴 시작에 운명 1장 · 손패 6장째 버리기 선택이 끼고 그동안 다른 선택이 미뤄진다 · 맵 행동·운명 창의 합법 답에 운명 내기. 옮기는 길은 두지 않는다).
        /// 10 = M5 「기술 효과 — 지속 보정 갈래」(기술 55장의 지속 보정이 굴림·피해·턴 끝 회복·교체를 바꾸고 굴린 뒤 순서 선택에 지우기 보정이 낀다. 옮기는 길은 두지 않는다).
        /// 11 = M5 「원작 대조 고침」(매혹적인 스튜를 쓸지 묻는 선택이 끼고 · 조우 고르기·진화 답이 늘고(제작 · 반값) · 같은 보정은 겹치지 않고 · 운명 3장 효과에 그 신의 카드 1장. 옮기는 길은 두지 않는다).
        /// 12 = M5 「운명 네 장 효과」(디아루가 재굴림 · 펄기아 순간이동 · 기라티나 자해 — 맵 행동·운명 창의 합법 답에 고른 것을 실은 운명 내기가 끼고 굴림·체력이 바뀐다. 옮기는 길은 두지 않는다).
        /// 13 = M5 「기술 효과 — 나머지 op 갈래」(기술 45장 — 상태 둘·피로·즉사·타입 고르기·교체 기술·피해 이월·반사·벤치 기절이 배틀 결과를 바꾸고 효과 고르기 선택이 낀다. 옮기는 길은 두지 않는다).
        /// 14 = M5 「숨겨진 장소·유물·트레이너전 보상」(공개한 숨겨진 장소가 규토리·운명·유물·떨어진 별·우두머리 배틀을 내고 · 지닌 유물이 공격력을 올리고 · 트레이너전 승리 보상 선택이 끼고 · 맵 행동에 앞면 우두머리 찾아보기가 낀다. 옮기는 길은 두지 않는다).
        /// 15 = M5 「전설·알파·폼」(전설·우두머리의 알파 스트라이크 답이 배틀 행동에 끼고 · 비스트 부스트가 피로·공격력을 바꾸고 · 폼 기술 8장이 폼·굴림을 바꾸고 · 맵 행동에 전설 장소 찾아보기가 낀다. 옮기는 길은 두지 않는다).
        /// 16 = M5 「기술 효과 — 핸들러 갈래」·「사당 공물·카드」(기술 30장 — 공격력 셈·복사·섞기·둘째 기술·마지막 기술이 배틀 결과를 바꾸고 효과 고르기 선택이 끼고 · 맵 행동의 사당 공물이 카드 3장·보상 선택을 낸다. 옮기는 길은 두지 않는다).
        /// 17 = M5 「신·무리·우두머리」(맵 행동의 합법 답에 왜곡 게이트 · 사당 전설 · 신 찾아보기가 끼고 무리 배틀·신 배틀이 열리고 · 배틀 뒤 놓아주기·기술 가르치기가 이어 묻고 · 신을 이기면 한 턴 더. 옮기는 길은 두지 않는다).
        /// 18 = M5 「스탬프」·「거래·원격 도전」(스탬프 조건 7이 보상(규토리 · 기술 풀 · 수호신의 서)을 내고 · 맵 행동에 거래 제안·원격 도전이 끼고 남의 턴 제안 Propose · 거래 응답 선택이 다른 선택을 미룬다. 옮기는 길은 두지 않는다).
        /// 19 = M5 「천상의 균열」(균열 카드가 모든 좌석에 추종자 비밀 선택(안 고름 Decline 포함)·동률 고르기를 열고 · 시작의 사당에 신 토큰을 놓고 · 그 신의 추종자가 턴마다 이동력 +1. 옮기는 길은 두지 않는다).
        /// 20 = M5 「규칙 끝 검토 반영」(heal_bell이 기절한 파티 포켓몬을 되살리지 않고 · 지닌물건 기록이 교체로 옮겨 가지 않고 · 추가 공격의 알파 스트라이크가 타입을 묻고 · 체력 0인 공격자의 지속 보정·지닌물건 공격력을 훑지 않고 ·
        /// 순서 미리 셈이 버퍼의 피로 피해를 세고 · 사당 3장 뽑기가 방금 뽑은 카드를 섞지 않고 · 심장은 거래에 넣지 않는다. 옮기는 길은 두지 않는다).
        /// 21 = M5 「규칙 끝 감사 반영」(쓰러뜨린 포켓몬의 다른 폼 시그니처도 가르치기에 묻고(신 포함) · 빠진 신은 추종자로 못 고르고 · 화려한 만찬의 매혹적인·우러난 스튜가 만든 것과 따로 한 몫씩. 옮기는 길은 두지 않는다).
        /// 22 = 결정 76 답 반영(첫 균열에 아무도 따르지 않으면 신이 내려오지 않고 · 운명 3장 효과에 함께 낼 아르세우스 장수를 고르는 답이 끼고 · 포획 굴림 뒤에도 디아루가 창이 열리고 ·
        /// 스탬프 7의 아르세우스는 한 종 · 둘째 기술 「안 씀」도 쓴 것 · 다음 턴 직전 기술은 둘째 기술만 · 거래로 받은 포켓몬은 해금 「얻음」이 아님. 옮기는 길은 두지 않는다).
        /// 23 = M5 마감 검토 반영(barrier가 다음에 공격받을 때 크리티컬 몫을 공격한 포켓몬에게 되돌린다 — 결정 76 B3 · 천상의 균열로 바뀐 신은 「신 격파」 박자를 쓰지 않는다. 옮기는 길은 두지 않는다).
        /// 24 = M6 「해금 뼈대」(트레이너 카드 등급이 오른다 — 보통·강함을 쓰러뜨리거나 얻으면 실버·골드, rulings A20-2~5. 옮기는 길은 두지 않는다).
        /// 25 = M6 「상시 보정」(트레이너 카드 능력 18이 우선도·최대 체력·공격력·피해·회복·포획 굴림을 바꾼다 — rulings A32. 옮기는 길은 두지 않는다).
        /// 26 = M6 「배틀 굴림·공격력·타입」(트레이너 카드 능력 12가 배틀에 물음을 열고 순서·공격력·타입·굴림·상태를 바꾼다 — rulings A34. 옮기는 길은 두지 않는다).
        /// 28 = M6 「이동」(트레이너 카드 능력 11이 걷기의 값·높이·날기·턴 끝·야생의 도망을 바꾼다 — rulings A36. 옮기는 길은 두지 않는다).
        /// 27 = M6 「배틀 피해 뒤·기절·교체·상태」(트레이너 카드 능력 11이 피해 뒤 회복·기절·교체·포획·비스트 부스트·상태 효과를 바꾼다 — rulings A35. 옮기는 길은 두지 않는다).
        /// 29 = M6 「맵 상호작용」(트레이너 카드 능력 7이 맵 행동에 능력 답을 더하고 숨겨진 장소·찾아보기·규토리 더미를 바꾼다 — rulings A37. 옮기는 길은 두지 않는다).
        /// 30 = M6 「보상·남의 행동 반응」(트레이너 카드 능력 11이 포획 굴림·배틀 뒤 보상·가르치기·추가 턴을 바꾸고 배틀 뒤·맵 행동 앞에 회복·도전 물음을 연다 — rulings A38. 옮기는 길은 두지 않는다).
        /// 31 = M6 「제작·사당·스탬프·진화·파티」(트레이너 카드 능력 13이 제작 답·조우 고르기·진화 답에 고르기를 더하고 정비 명령 셋 · 사당 뽑기 · 스탬프 보상 · 파티 칸을 바꾼다 — rulings A39. 옮기는 길은 두지 않는다).
        /// 34 = M6 「전용 아이템 능력」(드래곤의 정점 · 프레셔스 볼 · 앰프 주사위 · 날씨 휠 · 대타출동 · 랜드마크 투어 — 배틀·맵 행동·정비의 합법 답과 피해가 바뀐다, rulings A41. 옮기는 길은 두지 않는다).
        /// 33 = M6 「무녀·카드 바꾸기」(무녀 파랑의 고정 추종자·2장 몫 · 무녀 빨강의 신 · 아르세우스의 추종자 · youngster R 카드 바꾸기 — 「When unlocked」 고르기가 쉬는 자리에 끼인다, rulings A40. 옮기는 길은 두지 않는다).
        /// 32 = M6 backpacker R(지닌물건 두 칸 — 정비 HoldItem Slot 1 · 두 칸의 배틀 아이템·유물이 모두 듣는다 · 스탬프 5는 지닌물건 수로 센다, rulings A39. 옮기는 길은 두지 않는다).
        /// 35 = M6 「트레이너 카드 자체 검토」(lady R 완전 회복을 배틀 반응 창 — 굴리기 전·굴린 뒤, 상대 공격 도중 포함 — 에서도 묻는다 · 거래로 backpacker R이 아닌 좌석에 간 포켓몬의 둘째 지닌물건은 그 좌석의 가방으로, rulings A35·A39. 옮기는 길은 두지 않는다).
        /// 36 = M6 「울트라 버스트 규칙 합침」(가지 wt-s2가 따로 올린 24~29를 하나로 — 울트라 버스트 판: 셋업이 UB 9·네크로즈마·규토리 3색을 놓고 왜곡 게이트 이동이 끼고 라운드 끝마다 UB 턴(파괴 · 균열 70% 패배) ·
        /// 턴 시작에 균열 카드 붙이기 동시 선택 · 붙은 카드가 기술 칸을 막고 순서·공격력·상성·체력·교체·상태를 바꾼다 · UB 토큰 찾아보기·UB 배틀·프리즘 아머 · 네크로즈마 페이즈 1~3·썬/문 동전·탈락·축복 정비 명령 ·
        /// 아르세우스 운명 3장 = 축복 +1 · 호수 시련·실버디 · 옵션 울트라 온슬롯. 신오의 심장 판은 트레이너 카드 규칙(35) 그대로 — rulings A50~A57. 옮기는 길은 두지 않는다).
        /// 37 = M6 「질문 페이지 답 — 규칙」(사용자 결정 84: lady R은 내 배틀 행동에서만 · team_rocket B 굴림에 디아루가 없음 · 빼앗은 포켓몬은 그 배틀에 못 냄 · 둘째 지닌물건은 가방으로 · 날씨 +2는 휠을 가진 좌석만 ·
        /// 배틀 중에는 놓아주기 없음(남의 배틀 포함) · worker B 길은 그 턴에 밟은 칸 모두 · psychic B로 거래 · interviewers B는 턴 밖에서도 · 축복은 다른 좌석의 동의 · 라운드 끝 건너뛰기 물음 ·
        /// 부활은 회복만 · UB 격파 뒤 손·보관 카드도 버림 · pokemon_ranger B 보관은 묻는다 — rulings A35~A42·A52·A54. 옮기는 길은 두지 않는다).
        /// 38 = M6 「화면 교차 검토 반영」(psychic B의 답이 안쪽 답의 Values까지 실어 JP 7 더 낸 공물이 따로 서고 · 길 위 멈출 칸을 걷기와 같은 걸음 값으로 센다 — 맵 행동의 합법 답이 바뀐다. 옮기는 길은 두지 않는다).
        /// 39 = M6 「마감 답 반영 — 규칙」(사용자 결정 87: 축복 동의는 탈락한 좌석에게 묻지 않는다 · 라운드 끝 「UB 턴을 건너뛸까」는 탈락하지 않은 모든 좌석의 다수결(동의 묶음을 거치지 않는다) ·
        /// pokemon_ranger B가 보관한 균열 카드를 정비 AttachBreach로 꺼내 붙인다 — rulings A44. 옮기는 길은 두지 않는다).
        /// 40 = M9 사용자 결정 131(psychic R을 굴린 뒤 따로 묻지 않고 굴린 뒤 반응 창의 반응으로 — 공격하는 좌석의 운명 반응 선택에 UseAbility 답이 끼고, 쓰지 않던 창이 열린다 — rulings G6·G14·A34. 옮기는 길은 두지 않는다).
        /// 41 = M9 사용자 결정 134 ①(guitarist B 앰프를 굴린 뒤 따로 묻지 않고 굴린 뒤 반응 창의 반응으로 — 공격하는 좌석의 운명 반응 선택에 UseAbility(AmpTarget) 답이 끼고, 쓰지 않던 창이 열린다 — rulings G14·A41. 옮기는 길은 두지 않는다).
        /// 판이 다른 저장은 스냅샷(쉬는 때 = 배틀 밖)에서 이어 가고 체크포인트를 맞대 보지 않는다 — 옵션은 머리·스냅샷에 적힌 값 그대로다(SaveStore.Load 경고).
        /// 판 4 전의 저장은 그 판 동안 순서를 묻지 않는다(<see cref="RuleOptions.FixedEffectOrder"/> — 옛 로그에 없는 선택이 끼면 선택 Id가 밀려 다시 적용이 어긋난다).
        /// </summary>
        public const int CurrentRulesVersion = 41;

        /// <summary>결정 72 선택 창이 생긴 규칙 판. 이보다 낮은 판의 저장은 <see cref="RuleOptions.FixedEffectOrder"/>로 이어 간다.</summary>
        public const int EffectOrderRulesVersion = 4;

        public SaveHead Head;
        public SaveSnapshot Snapshot;
        /// <summary>Seq가 Snapshot.CommandCount부터 빈틈없이 이어지는 줄.</summary>
        public List<SaveLogLine> Commands = new List<SaveLogLine>();
        /// <summary>읽으며 고친 것(잘린 마지막 줄, 깨진 스냅샷, 판 다름).</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>Ctx 표들의 키 목록(01 §10 IdTable).</summary>
        public static List<SaveIdTable> IdTablesOf(Ctx ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var r = new List<SaveIdTable>();
            void Add(string name, IdTable t)
            {
                var x = new SaveIdTable { Name = name };
                for (int i = 0; i < t.Count; i++) x.Keys.Add(t[i]);
                r.Add(x);
            }
            Add("pokemon", ctx.Cards.PokemonIds);
            Add("moves", ctx.Cards.MoveIds);
            Add("trainer_cards", ctx.Cards.TrainerCardIds);
            Add("decks", ctx.Decks.DeckIds);
            Add("cards", ctx.Decks.CardIds);
            Add("fixtures", ctx.Fixtures.Ids);
            Add("items", ctx.Items.Ids);
            return r;
        }

        /// <summary>두 IdTable 목록이 표마다 같은 키 목록인가. 같으면 null, 다르면 처음 다른 곳(표 이름·자리).</summary>
        public static string IdTablesDiff(List<SaveIdTable> saved, List<SaveIdTable> now)
        {
            if (saved == null || saved.Count == 0) return "세이브에 IdTables가 없다";
            if (saved.Count != now.Count) return $"표 수 {saved.Count} ≠ {now.Count}";
            for (int i = 0; i < now.Count; i++)
            {
                var a = saved[i];
                var b = now[i];
                if (a?.Name != b.Name) return $"{i}번째 표 {a?.Name} ≠ {b.Name}";
                if (a.Keys == null || a.Keys.Count != b.Keys.Count) return $"{b.Name} 키 수 {a.Keys?.Count} ≠ {b.Keys.Count}";
                for (int k = 0; k < b.Keys.Count; k++)
                    if (a.Keys[k] != b.Keys[k]) return $"{b.Name}[{k}] {a.Keys[k]} ≠ {b.Keys[k]}";
            }
            return null;
        }
    }

    /// <summary>
    /// 쉬는 때(이어 받는 자리, 02 §7): 배틀 밖이고, 열린 선택이 모두 보드에서 고르는 것(행동 MapAction · 숨겨진 장소 공개 HiddenSpot)이거나 셋업 고르기이며, 반쯤 답한 묶음이 없다. 게임이 끝난 상태도 쉬는 때다.
    /// 스냅샷은 쉬는 때에만 찍고, 솔로의 이어 받기는 로그의 마지막 쉬는 때로 돌아간다 — 배틀·조우·배틀 뒤 선택의 한가운데는 그 화면을 이벤트 없이 다시 세울 수 없다 [가정].
    /// </summary>
    public static class SaveRest
    {
        public static bool IsRest(GameState s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (s.Phase == Phase.GameOver) return true;
            if (s.Battle != null) return false;
            foreach (var c in s.Pending)
            {
                if (c.Answered) return false;
                if (c.Kind != ChoiceKind.MapAction && c.Kind != ChoiceKind.HiddenSpot && c.Kind != ChoiceKind.VillagePick && c.Kind != ChoiceKind.StarterPick && c.Kind != ChoiceKind.TrainerCardPick) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 세이브 쓰기(02 §1-1 ISaveStore, §7). Data SaveStore가 구현한다(경로는 그쪽 생성자 인자). 세션은 명령마다 줄을 덧붙이고, 게임 시작과 셋업 끝·턴 끝·배틀 끝 뒤의 첫 쉬는 때(<see cref="SaveRest"/>)에 스냅샷과 머리를 쓴다.
    /// 받은 객체는 세션이 다시 고치지 않는다 — 바로 직렬화한다.
    /// </summary>
    public interface ISaveStore
    {
        void WriteHead(SaveHead head);
        void WriteSnapshot(SaveSnapshot snapshot);
        void AppendCommand(SaveLogLine line);
        /// <summary>
        /// 로그를 Seq가 commandCount 이상인 줄 앞에서 자른다(이어 받은 자리 뒤 — 그 뒤에 덧붙이는 줄의 Seq가 이어지게). 그 앞에 깨진 줄이나 Seq가 앞줄보다 커지지 않는 줄이 있으면 거기서 자른다
        /// (읽을 때 그 뒤가 버려지는 줄이다 — 남겨 두면 새 줄이 읽히지 않는다).
        /// </summary>
        void TruncateLog(int commandCount);
    }
}
