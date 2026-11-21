using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 연출 계약 코드 표(01 §3-6 시험, 결정 37): 04 §8-8 행 → 이벤트 타입 → 필수 필드. 01 §3-7에서 손으로 옮겼다(시험은 문서를 읽지 않는다).
    /// 키 = 행 번호, 갈래가 있으면 「행-갈래」(P1: 경우가 갈리면 결말을 따로). Expect = 그 행에서 정해지는 값(「Kind=Reaction」).
    /// 타입·필드를 바꾸면 01 §3-7과 04 §8-8 「이벤트」 열을 먼저 고치고 여기를 맞춘다. M5·M6에서 그 행까지 넓힌다.
    /// 2026-11-07 M3-d: 셋업·턴·이동·찾아보기·숨겨진 장소·마을 행동·전멸 귀환 장면(1~6·8·9·11·17·19~21·25·71·101·102행)과 1막 빨강 규토리(4-1막).
    /// 2026-11-07 M3-e: 배틀 장면(22·53~56·59~61·64~70·72·94행, 갈래 54-토큰·64-피로피해·67-기권·68-실패)과 1막 해방(94행 TokenRemoved).
    /// 2026-11-07 M3-f: 효과 처리기 장면(57·62·63행, 갈래 62-면역·62-이미·63-면).
    /// 2026-11-07 M3 검토: 58-묶음(반응 창 비공개 묶음의 Window), 68의 Changes(던진 볼), 1·99의 Window = None.
    /// </summary>
    static class FxContract
    {
        public sealed class Need
        {
            public string Key;
            public string Type;
            public string[] Fields;
            public string Expect;
        }

        /// <summary>기대값이 있는 줄. expect = 「필드=값」을 ;로 잇는다.</summary>
        static Need E(string key, string type, string expect, params string[] fields) => new Need { Key = key, Type = type, Fields = fields, Expect = expect };

        static Need N(string key, string type, params string[] fields) => new Need { Key = key, Type = type, Fields = fields };

        /// <summary>04 §8-8에서 M 열에 4가 있는 행(10·12·13은 이벤트가 아님) + M5 「막·종막 화면」의 95·96·97행 + 「운명·응답 화면」의 7·14·42~45·98행 + 「제작·은행·진화 화면」의 16·18·36·37·39행 + 「사당·균열 화면」의 31~34행 + 「스탬프·도전·거래 화면」의 47·51·52행.</summary>
        public static readonly int[] Rows =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 14, 16, 17, 18, 19, 20, 21, 22, 25, 31, 32, 33, 34, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45,
            53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 94, 95, 96, 97, 98, 99, 100, 101, 102,
            47, 51, 52,   // M5 「스탬프·도전·거래 화면」(M5 마감 검토 반영이 넣었다)
            23, 24, 26, 27, 28, 29, 30, 46, 74, 75, 76, 77, 78, 79, 80, 81,   // M5 「맵 결과·배틀 안 연출」
            15, 82, 83,   // M6 「UB 보드 화면」(왜곡 게이트 이동 · UB 이동 · UB가 밟은 칸의 파괴)
            84, 85, 86,   // M6 「UB 화면 묶음 1」(네크로즈마 원격 파괴 · 10% 구간과 카드 받기 · 70% 패배 — 82행 UB 턴 시작 · 83행 균열 바뀜도 더했다)
            88, 89, 90, 92,   // M6 「UB 배틀 화면 묶음」(프리즘 아머 · 울트라 온슬롯 · 네크로즈마 페이즈·동전 · 호수 시련)
            87, 93,   // M6 「연출 목록 채우기」(UB 등장 — 22행 BattleStarted의 Kind · 실버디 → 축복 +1 — 83행 토큰 날기 83-토큰도 더했다)
            91,   // M6 「UB 화면 묶음 2」 합침(탈락·부활 910)
            35, 48, 49, 50,   // M6 「트레이너 카드 해금 연출」(날씨 휠 · 해금 · 카드 바꾸기 · 능력 사용)
        };

        public static readonly Need[] Table =
        {
            N("1", "TrainerCardsDealt", "Seat", "Count", "CardKeys"),
            E("1", "SecretAnswered", "Window=None", "Group", "Seat", "Window"),
            N("1", "TrainerCardsRevealed", "Picks", "ReturnedCount"),
            N("2", "VillagePicked", "Seat", "VillageKey", "Cell", "SeatColor"),
            N("3", "StarterPicked", "Seat", "Species", "PokemonId", "PartySlot"),
            N("3", "CardsReturned", "Returns"),
            E("3", "PileShuffled", "Pile=Weak", "Pile"),
            N("4", "ActRevealed", "ActKey", "FirstSeat", "PrevActKey"),
            N("4-1막", "ApricornsGained", "Seat", "Source", "Changes"),
            E("5", "ActionStarted", "Auto=Auto", "Seq", "Seat", "Kind", "Auto"),
            E("5", "TokensPlaced", "Kind=Grunt", "Kind", "Cells", "Villages"),
            N("5", "FlyBlocked", "Villages"),
            N("6", "TurnStarted", "Seat", "TurnId", "Round"),
            E("6", "MoveAllowanceSet", "Base=3", "Seat", "Base", "Mods", "Total"),
            E("8", "Healed", "Source=Rule:Respawn", "Seat", "Source", "Pokemon"),
            N("8", "RespawnCleared", "Seat"),
            E("8", "FateDealt", "Source=Rule:Respawn", "Seat", "Source", "StarCount", "PerSeat"),
            // M5 「사당·균열 화면」(31~34행)
            N("31", "ShrineOffered", "Seat", "Source", "ShrineKey", "Tier", "Changes"),
            N("31", "ShrineCardsDrawn", "Seat", "Tier", "CardKeys"),
            N("32", "ShrineBlessing", "Seat", "Source", "CardKey", "From", "Tier", "Changes"),
            E("33", "LegendEvent", "Placed=True", "Seat", "Source", "ShrineKey", "Species", "Cell"),
            N("33", "ApricornsGained", "Seat", "Source", "Changes"),
            E("34", "SecretRevealed", "Window=None", "Group", "Answers", "Window"),
            N("34", "FollowerTally", "Seat", "Source", "Counts", "Tied"),
            N("34", "GodDescended", "Seat", "Source", "God", "CardKey", "Cell"),
            // M5 「운명·응답 화면」(7·14·42~45·98행)
            E("7", "FateDealt", "Source=Rule:FallenStar", "Seat", "Source", "StarCount", "PerSeat"),
            N("14", "Teleported", "Seat", "Source", "From", "To"),
            // M6 「UB 보드 화면」(15·82·83행)
            N("15", "GateTravel", "Seat", "FromGate", "ToGate", "FromCell", "ToCell"),
            N("82", "UbMoved", "Seat", "UbId", "From", "To"),
            N("83", "CellDestroyed", "Seat", "Cell", "TokenKind", "ByUbId"),
            // M6 「UB 화면 묶음 1」(82행 예비 · 83행 균열 % · 84~86행)
            N("82", "UbTurnStarted", "Seat", "Round", "TotalMoves", "Breach"),
            N("83", "BreachChanged", "Seat", "Before", "After"),
            N("84", "NecrozmaStrike", "Seat", "Cells", "Count"),
            N("85", "BreachBracket", "Seat", "Before", "After", "Brackets"),
            N("85", "BreachCardsDealt", "Seat", "PerSeat"),
            E("86", "GameEnded", "Reason=BreachLost", "Seat", "Reason", "Winners"),
            // M6 「UB 배틀 화면 묶음」(88~92행 — 87행 UB 등장은 22행 BattleStarted의 Kind)
            N("88", "PrismReduced", "Seat", "Side", "PokemonId", "Amount"),
            N("88-옮김", "PrismMoved", "Seat", "Before", "After"),
            N("89", "OnslaughtDrawn", "Seat", "Source", "CardKey", "Effect"),
            N("90", "NecrozmaPhase", "Seat", "Phase", "Next"),
            N("90-동전", "CoinFlip", "Seat", "Result"),
            N("92", "TrialProgress", "Seat", "Lake", "Count", "Goal"),
            N("92-격파", "GuardianDown", "Seat", "Lake", "Total"),
            // M6 「연출 목록 채우기」(83행 토큰 날기 · 87행 UB 등장 · 93행 실버디)
            N("83-토큰", "TokenMoved", "Seat", "TokenKind", "From", "To"),
            E("87", "BattleStarted", "Kind=UltraBeast", "BattleId", "Kind", "Sides", "Cell", "Biome", "Climate", "BgmKey"),
            E("93", "BlessingGained", "Source=Rule:Silvally", "Seat", "Source", "Total"),
            // M6 「UB 화면 묶음 2」(91행 탈락·부활 — 혼자 판은 네크로즈마에게 진 뒤 축복으로 바로 부활)
            N("91", "Eliminated", "Seat"),
            N("91", "Revived", "Seat", "Cell"),
            // M6 「트레이너 카드 해금 연출」(35·48·49·50행)
            N("35", "WeatherSet", "Seat", "Source", "Weather", "Before", "Changes"),
            E("48", "CardUnlocked", "TierAfter=2", "Seat", "Source", "CardKey", "TierBefore", "TierAfter", "CausePokemonId", "CauseGrade", "AbilitySlots"),
            E("49", "TrainerCardSwapped", "Tier=3", "Seat", "Source", "FromCard", "ToCard", "Tier"),
            N("50", "AbilityUsed", "Seat", "Source", "CardKey", "Ability", "Targets"),
            E("14", "FatePlayed", "Card=Palkia;Play3=False", "Seat", "Source", "Card", "CardKeys", "WildcardAs", "Before", "After"),
            N("42", "FateDealt", "Seat", "Source", "PerSeat"),
            N("42", "FateDiscarded", "Seat", "CardKey", "Before", "After"),
            E("43", "FatePlayed", "Card=Dialga", "Seat", "Source", "Card", "CardKeys", "Before", "After"),
            N("43", "DiceFacesChanged", "Seat", "Source", "Changes", "SrcBefore", "SrcAfter"),
            E("44", "FatePlayed", "Card=Giratina", "Seat", "Source", "Card", "CardKeys", "Before", "After"),
            N("44", "HpChanged", "PokemonId", "Source", "Before", "After", "Max"),
            E("45", "FatePlayed", "Play3=True", "Seat", "Source", "Card", "CardKeys", "WildcardAs", "FollowerCounted", "Before", "After"),
            E("98", "ChoiceRequested", "Kind=Reaction", "Id", "Kind", "Sub", "Seat", "Clock", "Window", "Legal"),
            E("98", "ChoiceAnswered", "ByTimeout=True", "Id", "Seat", "Answer"),
            N("9", "ProtectionCleared", "Seat"),
            N("11", "Moved", "Seat", "Path", "Levels", "AllowanceBefore", "AllowanceAfter", "StopAt"),
            N("16", "Flew", "Seat", "From", "To"),
            N("16", "ApricornSpent", "Seat", "Changes"),
            N("17", "Healed", "Seat", "Source", "Pokemon"),
            N("18", "GinkgoTraded", "Seat", "CardKey", "Row", "Changes"),
            E("18", "PileShuffled", "Pile=GinkgoTrade", "Pile"),
            E("17", "UsageMarked", "Kind=VillageHeal", "Seat", "Kind"),
            N("19", "ApricornsTaken", "Seat", "Cell", "Color", "Grade", "Count", "Before", "After"),
            E("19", "TurnEnded", "Reason=Apricorns", "Seat", "TurnId", "Reason"),
            N("20", "Searched", "Seat", "Cell", "Biome", "Climate"),
            N("20", "EncounterDrawn", "Cards"),
            N("21", "EncounterPicked", "Seat", "CardKey", "Grade", "Position"),
            N("21", "CardsReturned", "Returns"),
            N("22", "BattleStarted", "BattleId", "Kind", "Sides", "Cell", "Biome", "Climate", "BgmKey"),
            N("25", "HiddenSpotRevealed", "Seat", "Cell", "SpotKey"),
            N("25-지나감", "HiddenSpotPassed", "Seat", "Cell"),
            N("36", "Crafted", "Seat", "ItemKey", "Changes"),
            N("37", "StewEaten", "Seat", "Source", "ItemKey", "Moves", "MovesLeft"),
            N("38", "ItemHeld", "Seat", "PokemonId", "ItemKey", "FromPokemonId"),
            N("39", "Evolved", "Seat", "PokemonId", "From", "To", "JpCost", "Changes", "Pokemon"),
            N("38-폼", "FormChanged", "PokemonId", "Before", "After", "Source"),
            N("40", "MoveTaught", "Seat", "PokemonId", "Slot", "MoveKey", "ReplacedKey"),
            N("40-버림", "MoveForgotten", "Seat", "PokemonId", "Slot", "MoveKey"),
            N("41", "Released", "Seat", "PokemonId", "Species", "MovesToDiscard"),
            N("53", "SentIn", "Side", "PokemonId", "Species", "Hp", "MaxHp", "Status", "TrainerKey"),
            N("53", "InitiativeTokenAt", "Side"),
            N("54", "OrderDecided", "Sides", "First", "TokenTo"),
            E("54-토큰", "OrderDecided", "TokenUsed=True", "Sides", "First", "TokenTo"),
            E("55", "ChoiceRequested", "Kind=BattleAction", "Id", "Kind", "Seat", "GroupId", "Clock", "Window", "Count", "Legal"),
            N("55", "MoveSelected", "Side", "MoveKey", "Type", "Power"),
            E("56", "DiceRolled", "Kind=Attack", "Side", "PokemonId", "Kind", "Type", "Faces", "Sum", "Power", "PowerMods", "FatigueBefore", "FatigueAfter"),
            N("57", "DiceFacesChanged", "Source", "Changes", "SrcBefore", "SrcAfter"),
            E("58", "ChoiceRequested", "Kind=Reaction;HandGated=True", "Id", "Kind", "Sub", "Seat", "GroupId", "Clock", "Window", "Count", "Legal"),
            N("58", "ChoiceAnswered", "Id", "Seat", "Answer"),
            N("58-건너뜀", "WindowSkipped", "Window"),
            E("58-묶음", "SecretAnswered", "Window=AfterRoll", "Group", "Seat", "Window"),
            E("58-묶음", "SecretRevealed", "Window=AfterRoll", "Group", "Answers", "Window"),
            N("59", "MoveUsed", "Side", "PokemonId", "MoveKey", "Type", "Category", "TargetId"),
            N("60", "TypeMatchup", "AttackerId", "DefenderId", "Type", "DefTypes", "Delta"),
            N("61", "DamageBreakdown", "TargetId", "Steps", "Final"),
            N("61", "HpChanged", "PokemonId", "Source", "Before", "After", "Max"),
            E("62", "StatusApplied", "Result=Applied", "PokemonId", "Status", "Source", "Result"),
            E("62-면역", "StatusApplied", "Result=Immune", "PokemonId", "Status", "Source", "Result"),
            E("62-이미", "StatusApplied", "Result=Already", "PokemonId", "Status", "Source", "Result"),
            E("63", "StatusTicked", "Status=Poisoned", "PokemonId", "Status", "Effect", "HpBefore", "HpAfter"),
            E("63-면", "DiceFacesChanged", "Source=Status:2", "Source", "Changes", "SrcBefore", "SrcAfter"),
            N("64", "FatigueChanged", "PokemonId", "Before", "After", "Cause"),
            E("64-피로피해", "HpChanged", "Source=Rule:Fatigue", "PokemonId", "Source", "Before", "After", "Max"),
            N("65", "Fainted", "PokemonId", "Side", "ById", "PartyIndex"),
            N("66", "SwitchedOut", "Side", "PokemonId", "Reason"),
            N("66", "SentIn", "Side", "PokemonId", "Species", "Hp", "MaxHp", "Status"),
            E("66", "FatigueChanged", "After=0", "PokemonId", "Before", "After", "Cause"),
            N("67", "Fled", "Side", "Changes"),
            N("67-기권", "Forfeited", "Seat"),
            E("68", "CaptureAttempt", "Success=True", "Side", "BallKey", "TargetId", "Faces", "Sum", "CatchRate", "RateMods", "Shakes", "Changes"),
            E("68-실패", "CaptureAttempt", "Success=False", "Side", "BallKey", "TargetId", "Faces", "Sum", "CatchRate", "RateMods", "Shakes", "Changes"),
            N("69", "PokemonGained", "Seat", "PokemonId", "Species", "PartySlot", "BallKey", "Hp", "Status"),
            N("70", "BattleEnded", "BattleId", "Winner", "Result", "Changes"),
            N("71", "Whiteout", "Seat", "FromCell", "ToCell"),
            N("71", "RespawnPending", "Seat"),
            N("72", "GalacticBattle", "Seat", "Leader", "LineupCount", "Lineup"),
            E("73", "ChoiceAnswered", "ByAi=True", "Id", "Seat", "Answer", "Reason"),
            N("94", "ActGoalMet", "Seat", "Act", "Changes"),
            E("94", "TokenRemoved", "TokenKind=Grunt", "Cell", "TokenKind"),
            // M5 「막·종막 화면」: 2막 간부 · 3막 심장 · 태홍 · 종막(4·72·94~97행)
            E("4-종막", "ActRevealed", null, "ActKey", "FirstSeat", "PrevActKey"),
            E("72-간부", "GalacticBattle", "Leader=Commander", "Seat", "Leader", "LineupCount", "Lineup"),
            E("72-태홍", "GalacticBattle", "Leader=Cyrus", "Seat", "Leader", "LineupCount", "Lineup"),
            N("94-2막", "ShardGained", "Seat", "Shard", "Count", "Cell"),
            N("94-2막", "PartySlotUnlocked", "Seat", "Before", "After"),
            E("94-2막", "ActGoalMet", "Act=2", "Seat", "Act", "Changes"),
            N("94-3막", "HeartRestored", "Seat", "Source", "ItemKey", "Cell"),
            E("94-태홍", "ActGoalMet", "Act=3", "Seat", "Act", "Changes"),
            N("95", "EndingChosen", "Seat", "Traitor"),
            N("96", "TraitorOrderSet", "Seat", "Seats"),
            E("96", "Healed", "Source=Rule:TraitorReturn", "Seat", "Source", "Pokemon"),
            N("97", "GameEnded", "Seat", "Reason", "Winners"),
            // M5 「맵 결과·배틀 안 연출」: 전설 장소 23 · 무리 24·81 · 숨겨진 장소 결과 26~30 · 승리 보상 46 · 응징 74 · 교체 금지 75 · 알파 스트라이크·비스트 부스트 76 · 우두머리 77 · 신 둘째 폼 78 · 폼 바꾸기 79 · 신 격파 80
            E("23", "BattleStarted", "Kind=Legendary", "BattleId", "Kind", "Sides", "Cell", "Biome", "Climate", "BgmKey"),
            E("24", "BattleStarted", "Kind=Swarm", "BattleId", "Kind", "Sides", "Cell", "Biome", "Climate", "BgmKey"),
            E("26", "SpotResolved", "Result=Stash", "Seat", "Source", "Cell", "SpotKey", "Result", "Count"),
            N("26", "ApricornsGained", "Seat", "Source", "Changes"),
            E("26", "TokenRemoved", "TokenKind=HiddenSpot", "Seat", "Cell", "TokenKind"),
            E("27", "SpotResolved", "Result=Camp", "Seat", "Source", "Cell", "SpotKey", "Result", "Count"),
            E("28", "SpotResolved", "Result=Star", "Seat", "Source", "Cell", "SpotKey", "Result", "Count"),
            N("28", "FateDealt", "Seat", "Source", "PerSeat"),
            E("29", "SpotResolved", "Result=Alpha", "Seat", "Source", "Cell", "SpotKey", "Result", "Count"),
            E("29-피함", "SpotResolved", "Result=AlphaWarded", "Seat", "Source", "Cell", "SpotKey", "Result", "Count"),
            E("30", "SpotResolved", "Result=Relic", "Seat", "Source", "Cell", "SpotKey", "Result", "Count"),
            N("46", "SpoilsTaken", "Seat", "From", "Changes"),
            N("46", "ProtectionSet", "Seats"),
            N("46-운명", "SpoilsTaken", "Seat", "From", "Changes"),
            N("74", "RetributionStored", "Side", "Before", "After", "Cause"),
            N("74-옮김", "RetributionMoved", "FromSide", "ToPokemonId", "Amount"),
            E("75", "SwitchLocked", "Reason=GalacticNoSwitchAfterKo", "Seat", "Side", "Reason"),
            E("76", "MoveUsed", "MoveKey=-3", "Side", "PokemonId", "MoveKey", "Type", "Category", "TargetId"),
            E("76-부스트", "FatigueChanged", "Cause=BeastBoost", "PokemonId", "Before", "After", "Cause"),
            E("77", "BattleStarted", "Kind=Alpha", "BattleId", "Kind", "Sides", "Cell", "Biome", "Climate", "BgmKey"),
            N("77", "InitiativeTokenAt", "Side"),
            E("78", "FormChanged", "Source=Rule:CelestialAura;Temporary=True", "PokemonId", "Before", "After", "Source"),
            E("79", "FormChanged", "Temporary=True", "PokemonId", "Before", "After", "Source"),
            E("80", "TokenRemoved", "TokenKind=God", "Seat", "Cell", "TokenKind"),
            E("80", "TurnStarted", "IsExtra=True", "Seat", "TurnId", "Round"),
            E("81", "BattleEnded", "Result=Win", "BattleId", "Winner", "Result", "Changes"),
            // M5 「스탬프·도전·거래 화면」: 스탬프 47 · 도전 VS 51(응답은 기록 줄) · 거래 52
            N("47", "StampGained", "Seat", "Source", "StampKey", "CountBefore", "CountAfter", "Changes", "MoveKeys"),
            N("51", "ChallengeIssued", "Seat", "From", "To", "Cell"),
            E("51-응답", "ChallengeAnswered", "Accept=True", "Seat", "To"),
            N("52", "TradeDone", "Seat", "A", "B", "GiveA", "GiveB", "Changes"),
            E("99", "SecretAnswered", "Window=None", "Group", "Seat", "Window"),
            E("99", "SecretRevealed", "Window=None", "Group", "Answers", "Window"),
            N("100", "ActionStarted", "Seq", "Index", "Seat", "Kind", "Auto", "Continues", "ReactsTo"),
            N("101", "TurnEnded", "Seat", "TurnId", "Reason"),
            N("101", "TurnStarted", "Seat", "TurnId", "Round"),
            N("102", "RoundStarted", "Round"),
        };

        /// <summary>
        /// 아직 장면이 없는 키와 그 행동을 만드는 작업(plan §4). 장면을 만들면 여기서 빼고 FxContractTests.Scenes에 더한다.
        /// M3-f(효과 처리기) 뒤에는 M5 데이터가 필요한 38-폼만 남는다.
        /// </summary>
        public static readonly Dictionary<string, string> Pending = new Dictionary<string, string>
        {
            { "38-폼", "M5 아이템 폼 데이터" },
        };

        /// <summary>낼 M이 3인데 04 §8-8의 M4 행에 없는 타입(01 §3-7 「04 행」 열이 —이거나 M5·M6 행).</summary>
        public static readonly string[] NotInM4Rows =
        {
            "ChoiceReissued", "PartyReordered", "ProtectionSet",
            "PokemonExchanged", // 동등한 교환의 답(사당 판 S32 — 축복 단계 뒤, 기록 줄만)
            "TradeProposed", "TradeAnswered", // 거래 제안·응답 — 단계 없이 기록 줄과 응답 창만(52행은 TradeDone)
            "BreachCardAttached", "BreachCardDiscarded", "BreachCardMoved", // 균열 카드(85·88행 — 화면 묶음)
            "BlessingUsed", "UbTurnSkipped", // 축복 쓰기·건너뛰기(91행의 탈락·부활은 계약 키 91 — 이 둘은 기록 줄만)
        };
    }

    public class FxContractTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static Type EventType(string name) => typeof(GameEvent).Assembly.GetType("Sinnoh.Rules." + name);

        [Test]
        public void M3_연출목록표는04행과타입을덮음()
        {
            var keys = new HashSet<string>();
            var rows = new HashSet<int>();
            var used = new HashSet<string>();
            foreach (var n in FxContract.Table)
            {
                keys.Add(n.Key);
                rows.Add(int.Parse(n.Key.Split('-')[0]));
                used.Add(n.Type);
                var t = EventType(n.Type);
                Assert.IsNotNull(t, $"{n.Key}: 타입 {n.Type}이 없다");
                Assert.IsTrue(typeof(GameEvent).IsAssignableFrom(t), n.Type);
                foreach (var f in n.Fields) Assert.IsNotNull(t.GetField(f), $"{n.Key} {n.Type}.{f} 필드가 없다");
                if (n.Expect != null)
                    foreach (var e in n.Expect.Split(';'))
                    {
                        Assert.IsTrue(e.Contains("="), $"{n.Key} {n.Type} 기대값 「{e}」은 필드=값");
                        Assert.IsNotNull(t.GetField(e.Split('=')[0]), $"{n.Key} {n.Type} 기대값 필드 {e}");
                    }
            }
            foreach (int r in FxContract.Rows) Assert.IsTrue(rows.Contains(r), $"04 §8-8 {r}행의 계약이 없다");
            foreach (int r in rows) CollectionAssert.Contains(FxContract.Rows, r, $"{r}행은 M4 행이 아니다");
            foreach (var k in FxContract.Pending.Keys) Assert.IsTrue(keys.Contains(k), $"Pending {k}가 표에 없다");
            foreach (var k in keys) Assert.IsFalse(Scenes().ContainsKey(k) && FxContract.Pending.ContainsKey(k), $"{k}: 장면이 생겼으면 Pending에서 뺀다");
            foreach (var k in keys) Assert.IsTrue(Scenes().ContainsKey(k) || FxContract.Pending.ContainsKey(k), $"{k}: 장면도 Pending도 아니다");

            // 낼 M이 3인 이벤트 타입은 모두 표에 있거나 M4 행 밖 목록에 있다
            foreach (var t in typeof(GameEvent).Assembly.GetTypes())
            {
                if (!t.IsSubclassOf(typeof(GameEvent)) || t.IsAbstract) continue;
                Assert.IsTrue(used.Contains(t.Name) || Array.IndexOf(FxContract.NotInM4Rows, t.Name) >= 0, $"{t.Name}이 계약 표에 없다");
                // 이벤트 필드는 미설정 표지로 시작한다(01 §3-6 [가정]): int·enum = int.MinValue, 참조 = null
                var fresh = Activator.CreateInstance(t);
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.Name == "StepIdx" || f.FieldType == typeof(bool)) continue;
                    Assert.IsFalse(Filled(f.GetValue(fresh)), $"{t.Name}.{f.Name}이 미설정 표지로 시작하지 않는다");
                }
            }
            TestContext.WriteLine($"연출 계약: 키 {keys.Count}개 중 장면 {Scenes().Count}개, 남음 {FxContract.Pending.Count}개");
        }

        /// <summary>「채움」 = 미설정 표지(int.MinValue·null)가 아니다. 배열·목록·원소 객체는 안까지 본다. 0·−1은 값이다.</summary>
        static bool Filled(object v)
        {
            switch (v)
            {
                case null: return false;
                case int i: return i != GameEvent.U;
                case SourceRef r: return !r.IsUnset;
                case bool _: return true;
                case string _: return true;
                case ChoiceAnswer _: return true;
                case AiReason _: return true;
            }
            var t = v.GetType();
            if (t.IsEnum) return Convert.ToInt64(v) != GameEvent.U;
            if (v is IEnumerable list)
            {
                foreach (var x in list)
                    if (!Filled(x)) return false;
                return true;
            }
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (!Filled(f.GetValue(v))) return false;
            return true;
        }

        static bool Matches(GameEvent e, string expect)
        {
            if (expect == null) return true;
            foreach (var cond in expect.Split(';'))
            {
                var kv = cond.Split('=');
                var v = e.GetType().GetField(kv[0]).GetValue(e);
                string got = v is SourceRef r ? (r.Kind == SourceKind.Rule ? "Rule:" + (RuleSource)r.Id : r.Kind + ":" + r.Id) : Convert.ToString(v);
                if (got != kv[1]) return false;
            }
            return true;
        }

        // ── 장면. 이벤트 열(호스트 원본)을 돌려준다. 정비·반응·동시 선택은 M3-c, 셋업·턴·맵 행동은 M3-d(실제 흐름 Flow) ──

        Dictionary<string, Func<List<GameEvent>>> Scenes()
        {
            return new Dictionary<string, Func<List<GameEvent>>>
            {
                // 셋업 한 판(Engine.Start → 첫 MapAction): 트레이너 카드·마을·스타터·게임 시작·서막·1막·라운드·턴
                { "1", Setup }, { "2", Setup }, { "3", Setup }, { "4", Setup }, { "4-1막", Setup }, { "5", Setup }, { "6", Setup }, { "102", Setup },
                { "101", () => Map((s, log) => Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log)) },
                { "8", () => Map((s, log) =>
                    {
                        var next = s.Seats[(s.ActiveSeat + 1) % s.SeatCount];
                        next.RespawnPending = true;
                        s.Pokemon[next.Party[0]].Damage = 1;
                        Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
                    })
                },
                { "9", () => Map((s, log) =>
                    {
                        s.Seats[(s.ActiveSeat + 1) % s.SeatCount].Protected = true;
                        Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
                    })
                },
                { "11", () => Map((s, log) =>
                    {
                        var steps = MoveRules.Reachable(ctx.Board, s.Seats[s.ActiveSeat].Cell, 3, s.Options.ElevationMode);
                        int dest = Array.FindIndex(steps, x => x == 3);
                        Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Move, dest), log);
                    })
                },
                { "16", () => Map((s, log) =>
                    {
                        // 조무래기 없는 마을에 서서 규토리 하나로 날아간다(M5 「제작·은행·진화 화면」 — S25)
                        var st = s.Seats[s.ActiveSeat];
                        st.Apricorns[0] = 1;
                        foreach (var f in ctx.Fixtures.Fixtures)
                        {
                            if (f.Kind != FixtureKind.Village) continue;
                            st.Cell = f.Cell;
                            var fly = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.Fly);
                            if (fly == null) continue;
                            Flow.Answer(s, ctx, ChoiceKind.MapAction, fly, log);
                            return;
                        }
                        Assert.Fail("날아갈 수 있는 마을이 없다");
                    })
                },
                { "17", () => Map((s, log) => Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.VillageHeal), log)) },
                { "18", () => Map((s, log) =>
                    {
                        var st = s.Seats[s.ActiveSeat];
                        for (int c = 0; c < st.Apricorns.Length; c++) st.Apricorns[c] = 3;
                        Flow.Answer(s, ctx, ChoiceKind.MapAction, Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.Ginkgo), log);
                    })
                },
                { "36", () => Map((s, log) =>
                    {
                        s.Seats[s.ActiveSeat].Apricorns[(int)ApricornColor.Red] = 1;
                        Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Craft, Scene.Item(ctx, "item.poke_ball")), log);
                    })
                },
                { "37", () => Map((s, log) =>
                    {
                        var st = s.Seats[s.ActiveSeat];
                        st.Apricorns[(int)ApricornColor.White] = 1;
                        st.Apricorns[(int)ApricornColor.Yellow] = 1;
                        Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Craft, Scene.Item(ctx, CraftRules.ZestyStew)), log);
                    })
                },
                { "39", () => Map((s, log) =>
                    {
                        var st = s.Seats[s.ActiveSeat];
                        st.Jp = 10;
                        Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Evolve, st.Party[0]), log);
                    })
                },
                { "19", () => Map((s, log) =>
                    {
                        var tree = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak)];
                        s.Seats[s.ActiveSeat].Cell = tree.Cell;
                        Flow.Strip(s, ctx, DeckId.Weak, ctx.Board[tree.Cell].Biome, ctx.Board[tree.Cell].Climate, 0);
                        Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Search), null);
                        Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.TakeApricorns), log);
                    })
                },
                { "20", () => Map((s, log) =>
                    {
                        s.Seats[s.ActiveSeat].Cell = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak)].Cell;
                        Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Search), log);
                    })
                },
                { "21", () => Map((s, log) =>
                    {
                        s.Seats[s.ActiveSeat].Cell = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak)].Cell;
                        Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Search), null);
                        var pick = Flow.Only(s, ChoiceKind.EncounterPick);
                        Flow.Answer(s, ctx, ChoiceKind.EncounterPick, ChoiceAnswer.Pick(pick.Options[2], 2), log);
                    })
                },
                { "25", () => Spot(true) },
                // M5 「막·종막 화면」(4·72·94~97행)
                { "72-간부", () => LakeWin() }, { "94-2막", () => LakeWin() },
                { "94-3막", () => { var log = new EventLog(); ActScenes.Heart(ctx, log); return log.Events; } },
                { "72-태홍", () => TempleWin() }, { "94-태홍", () => TempleWin() }, { "4-종막", () => TempleWin() },
                { "95", () => Finale(false) }, { "97", () => Finale(false) }, { "96", () => Finale(true) },
                { "25-지나감", () => Spot(false) },
                // M5 「운명·응답 화면」(7·14·42~45·98행 — 98 = 반응 창 시간 초과의 기본값 답)
                { "31", () => Fate((c, log) => ShrineScenes.Tranquil(c, log)) }, { "32", () => Fate((c, log) => ShrineScenes.Tranquil(c, log)) },
                { "33", () => Fate((c, log) => ShrineScenes.Legend(c, log)) }, { "34", () => Fate((c, log) => ShrineScenes.Fracture(c, log)) },
                { "7", () => Fate(FateScenes.Star) }, { "14", () => Fate(FateScenes.Teleport) }, { "42", () => Fate(FateScenes.Discard) },
                { "43", () => Fate(FateScenes.Reroll) }, { "44", () => Fate(FateScenes.Sacrifice) }, { "45", () => Fate(FateScenes.Play3) },
                { "98", FateWindow },
                { "71", () =>
                    {
                        var s = Flow.Started(ctx, 2);
                        var log = new EventLog();
                        int b = (s.ActiveSeat + 1) % 2;
                        s.Seats[b].Cell = Flow.StepInto(s, ctx, s.Seats[b].Cell);
                        TurnFlow.Whiteout(s, ctx, Scene.Fx(s, log, b, ActionKind.BattleAction), b);
                        return log.Events;
                    }
                },
                { "38", () => Maint(s => new Maintenance { Kind = MaintKind.HoldItem, Pokemon = s.Seats[0].Party[0], Value = Scene.Item(ctx, "item.choice_band") }, s => s.Seats[0].Bag.Add(Scene.Item(ctx, "item.choice_band"))) },
                { "40", () => Maint(s => new Maintenance { Kind = MaintKind.SetMove, Pokemon = s.Seats[0].Party[0], Slot = 0, Value = Scene.Move(ctx, "stun_spore") }, s => s.Seats[0].StampMoves.Add(Scene.Move(ctx, "stun_spore"))) },
                { "40-버림", () => Maint(s => new Maintenance { Kind = MaintKind.SetMove, Pokemon = s.Seats[0].Party[0], Slot = 1, Value = -1 }, s => s.Pokemon[s.Seats[0].Party[0]].Moves[1] = Scene.Move(ctx, "stun_spore")) },
                { "41", () => Maint(s => new Maintenance { Kind = MaintKind.Release, Pokemon = s.Seats[0].Party[1], Value = s.Pokemon[s.Seats[0].Party[1]].Def }, s => s.Pokemon[s.Seats[0].Party[1]].Moves[0] = Scene.Move(ctx, "ember")) },
                { "58", FateWindow },
                { "58-묶음", () =>
                    {
                        // 두 좌석 모두 운명 1장인 트레이너전의 굴린 뒤 창: 한 묶음(비공개)으로 묻고 둘 다 안 씀 — 창의 일부라 연출 없음(Window = 창)
                        var s = Scene.Game(ctx, 2);
                        Scene.Give(s, ctx, 0, "387:0");
                        Scene.Give(s, ctx, 1, "390:0");
                        Scene.Battle(s, BattleKind.Trainer, 0, 1);
                        s.Seats[0].FateHand.AddRange(Scene.Top(s, DeckId.ChangeOfFate, 1));
                        s.Seats[1].FateHand.AddRange(Scene.Top(s, DeckId.ChangeOfFate, 2).GetRange(1, 1));
                        var log = new EventLog();
                        Reactions.Open(s, ctx, Scene.Fx(s, log, 0, ActionKind.BattleAction), Window.AfterRoll);
                        foreach (var c in new List<PendingChoice>(s.Pending))
                            Scene.Accept(Engine.Submit(s, ctx, c.Seat, Command.AnswerTo(c.Id, ChoiceAnswer.Of(AnswerKind.Decline)), log));
                        return log.Events;
                    }
                },
                { "58-건너뜀", () =>
                    {
                        var s = Scene.Game(ctx, 2);
                        Scene.Give(s, ctx, 0, "387:0");
                        Scene.Give(s, ctx, 1, "390:0");
                        Scene.Battle(s, BattleKind.Trainer, 0, 1);
                        var log = new EventLog();
                        Reactions.Open(s, ctx, Scene.Fx(s, log, 0, ActionKind.BattleAction), Window.BeforeRoll);
                        return log.Events;
                    }
                },
                { "73", () =>
                    {
                        var s = Scene.Game(ctx, 2);
                        int mine = Scene.Give(s, ctx, 0, "387:0");
                        int grunt = Scene.Spawn(s, ctx, "393:0", SeatId.Galactic);
                        var b = Scene.Battle(s, BattleKind.Galactic, 0, SeatId.Galactic, grunt);
                        b.ActiveSide = 1;
                        var log = new EventLog();
                        var c = Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.BattleAction, Seat = SeatId.Galactic });
                        int move = PrincipleRules.Move(ctx, s.Pokemon[grunt], s.Pokemon[mine], out var why);
                        Scene.Accept(Engine.Submit(s, ctx, SeatId.Galactic, Command.AiAnswer(c.Id, ChoiceAnswer.Of(AnswerKind.UseMove, move), why), log));
                        return log.Events;
                    }
                },
                // M5 「맵 결과·배틀 안 연출」(장면 ResultScenes)
                { "23", () => ResultScenes.LegendSite(ctx) },
                { "24", () => ResultScenes.Swarm(ctx) }, { "81", () => ResultScenes.Swarm(ctx) },
                { "26", () => ResultScenes.Spot(ctx, "hidden_spot.red_apricorn_stash") },
                { "27", () => ResultScenes.Spot(ctx, HiddenSpotRules.Campsite) },
                { "28", () => ResultScenes.Spot(ctx, HiddenSpotRules.FallenStar) },
                { "29", () => ResultScenes.Spot(ctx, "item.alpha_pokemon") }, { "77", () => ResultScenes.Spot(ctx, "item.alpha_pokemon") },
                { "29-피함", () => ResultScenes.Spot(ctx, "item.alpha_pokemon", WardAlpha) },
                { "30", () => ResultScenes.Spot(ctx, "item.gracidea_flower") },
                { "46", () => ResultScenes.Spoils(ctx, true, out _) }, { "46-운명", () => ResultScenes.Spoils(ctx, false, out _) },
                { "74", () => Grunts(true) }, { "74-옮김", () => ResultScenes.Retribution(ctx) },
                { "75", () => ResultScenes.Locked(ctx) },
                { "76", () => ResultScenes.AlphaStrike(ctx) }, { "76-부스트", () => ResultScenes.BeastBoost(ctx) },
                { "78", () => ResultScenes.God(ctx) }, { "80", () => ResultScenes.God(ctx) },
                { "79", () => ResultScenes.Form(ctx) },
                // M6 「UB 보드 화면」(15·82·83행 — 울트라 버스트 판)
                { "15", Gate }, { "82", UbTurn }, { "83", UbTurn }, { "84", UbTurn },
                { "85", () => UbBreached(9) }, { "86", () => UbBreached(69) },
                // M6 「UB 배틀 화면 묶음」(88~92행 — 혼자 하는 울트라 버스트 판에서 그 상대 칸에 서서 찾아보기, 배틀은 기본 답)
                { "88", () => UbFight(UbFoe.Beast, st => { st.Ub.PrismUltraBeast = 8; st.Ub.PrismUltraBurst = 1; }, false, l => l.Exists(e => e is PrismReduced)) },
                { "88-옮김", () => UbFight(UbFoe.Beast, st => { st.Ub.PrismUltraBeast = 0; st.Ub.PrismUltraBurst = UbState.PrismArmourTotal; }, true, l => l.Exists(e => e is PrismMoved)) },
                { "89", () => UbFight(UbFoe.Beast, null, false, l => l.Exists(e => e is OnslaughtDrawn), true) },
                { "90", () => UbFight(UbFoe.Necrozma, st => { st.Ub.PrismUltraBurst = 0; st.Ub.PrismUltraBeast = UbState.PrismArmourTotal; }, true, l => l.Exists(e => e is NecrozmaPhase)) },
                { "90-동전", () => UbFight(UbFoe.Necrozma, st => st.Ub.NecrozmaPhase = 2, false, l => l.Exists(e => e is CoinFlip)) },
                { "92", () => UbFight(UbFoe.Lake, null, false, l => l.Exists(e => e is TrialProgress)) },
                { "92-격파", () => UbFight(UbFoe.Lake, st => st.Ub.TrialsOvercome[(int)Shard.Willpower] = true, true, l => l.Exists(e => e is GuardianDown)) },
                // M6 「연출 목록 채우기」(83행 토큰 날기 · 87행 UB 등장 · 93행 실버디)
                { "83-토큰", SpotTrampled },
                { "87", () => UbFight(UbFoe.Beast, null, false, l => l.Exists(e => e is BattleStarted)) },
                { "93", () => UbFight(UbFoe.Silvally, null, true, l => l.Exists(e => e is BlessingGained)) },
                { "91", () => UbFight(UbFoe.Necrozma, st => { foreach (int p in st.Seats[st.ActiveSeat].Party) if (p >= 0) Battles.SetHp(st, ctx, p, 1); }, false, l => l.Exists(e => e is Revived)) },
                // M6 「트레이너 카드 해금 연출」(35·48·49·50행)
                { "35", WeatherWheel }, { "48", Unlock }, { "49", CardSwap }, { "50", AbilityUse },
                { "47", Stamps }, { "51", () => Challenge(false) }, { "51-응답", () => Challenge(true) }, { "52", Trade },
                { "99", Secret },
                { "100", Secret },
                // 배틀(M3-e). 트레이너전 한 판: 출전·순서(민첩의 띠)·기술·주사위·피로 피해·상성·피해·기절·교체 창·다음 포켓몬
                { "22", Exchange }, { "53", Exchange }, { "54", Exchange }, { "55", Exchange }, { "56", Exchange }, { "59", Exchange }, { "60", Exchange },
                { "61", Exchange }, { "64", Exchange }, { "64-피로피해", Exchange }, { "65", Exchange }, { "66", Exchange },
                { "54-토큰", TokenUsed },
                { "67", WildFlees },
                { "67-기권", Forfeit },
                { "68", () => Capture(true) }, { "69", () => Capture(true) }, { "70", () => Capture(true) },
                { "68-실패", () => Capture(false) },
                { "72", () => Grunts(false) },
                { "94", () => Grunts(true) },
                // 효과 처리기(M3-f): 상태이상 걸기·면역·이미 걸림, 졸림 면 바꾸기(57), 독 턴 끝(63), 마비 다음 공격(63-면)
                { "62", () => StatusAttack("399:0", null) },
                { "62-면역", () => StatusAttack("390:0", null) },
                { "62-이미", () => StatusAttack("399:0", StatusEffect.Paralysed) },
                { "57", () => StatusTurn(StatusEffect.None, StatusEffect.Drowsy) },
                { "63", () => StatusTurn(StatusEffect.Poisoned, StatusEffect.None) },
                { "63-면", () => StatusTurn(StatusEffect.Paralysed, StatusEffect.None) },
            };
        }

        /// <summary>62행: 불꽃숭이(우선도 6)가 야생에게 불꽃세례(Inflict Burned). 비버니 = 걸림, 불꽃숭이 = 면역, 이미 마비 = 이미 걸림.</summary>
        List<GameEvent> StatusAttack(string wildKey, StatusEffect? already)
        {
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "390:0");
            int wild = Scene.Spawn(s, ctx, wildKey, SeatId.Wild);
            if (already != null) s.Pokemon[wild].Status = already.Value;
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild);
            if (s.Pending.Count == 1 && s.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(s, ctx, s.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), log);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Scene.Move(ctx, "ember")), log);
            return log.Events;
        }

        /// <summary>57·63행: 모부기(상태 mine) 잎날가르기 → 야생 실쿤(상태 wild, 우선도 2). 공격 한 번과 그 턴 끝까지.</summary>
        List<GameEvent> StatusTurn(StatusEffect mine, StatusEffect wild)
        {
            var s = Battles.Game(ctx, 2);
            int a = Scene.Give(s, ctx, 0, "387:0");
            int w = Scene.Spawn(s, ctx, "266:0", SeatId.Wild);
            if (wild != StatusEffect.None) StatusRules.Inflict(ctx, s.Pokemon[w], wild);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, w);
            // 내 상태는 출전한 뒤 내 턴에 건다 — 마비 대기는 출전 때 지워지고 바로 다음 자기 턴 공격에만 남는다(rulings G14 · 원작 커뮤니티 확인)
            if (mine != StatusEffect.None) StatusRules.Inflict(ctx, s.Pokemon[a], mine);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Scene.Move(ctx, "razor_leaf")), log);
            return log.Events;
        }

        List<GameEvent> exchangeLog;

        /// <summary>
        /// 트레이너전(22·53~56·59~61·64~66행): 좌석 0 모부기(민첩의 띠 → 우선도 6, 피로 5, 체력 6) 대 좌석 1 팽도리(체력 1). 잎날가르기(+2)는 늘 쓰러뜨리고
        /// 피로 피해 3은 모부기를 남긴다 → 좌석 0 교체 창(비버니) → 좌석 1 다음 포켓몬 → 순서 다시.
        /// </summary>
        List<GameEvent> Exchange()
        {
            if (exchangeLog != null) return exchangeLog;
            var s = Battles.Game(ctx, 2);
            int a = Scene.Give(s, ctx, 0, "387:0");
            int a2 = Scene.Give(s, ctx, 0, "399:0");
            int b = Scene.Give(s, ctx, 1, "393:0");
            Scene.Give(s, ctx, 1, "396:0");
            Scene.Give(s, ctx, 1, "399:0");
            s.Pokemon[a].HeldItem = Scene.Item(ctx, "item.swiftness_sash");
            s.Pokemon[a].Fatigue = FatigueRules.Max;
            Battles.SetHp(s, ctx, b, 1);
            var log = new EventLog();
            BattleFlow.StartTrainer(s, ctx, Scene.Fx(s, log, 0, ActionKind.Challenge), 0, 1);
            Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(a), log);
            Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(b), log);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Scene.Move(ctx, "razor_leaf")), log);
            Battles.Answer(s, ctx, ChoiceKind.SwitchWindow, ChoiceAnswer.Pick(a2), log);
            var next = Battles.Only(s, ChoiceKind.NextPokemon);
            Battles.Answer(s, ctx, next, next.Default.Clone(), log);
            exchangeLog = log.Events;
            return exchangeLog;
        }

        /// <summary>54행 토큰 갈래: 모부기(4) 대 야생 비버니(4) 동률 → 토큰 보유자(도전자)가 쓴다.</summary>
        List<GameEvent> TokenUsed()
        {
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "387:0");
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, Scene.Spawn(s, ctx, "399:0", SeatId.Wild));
            Battles.Answer(s, ctx, Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.InitiativeToken), ChoiceAnswer.Of(AnswerKind.Accept), log);
            return log.Events;
        }

        /// <summary>67행: 약한 나무의 야생 불꽃숭이(우선도 6)가 체력 1 모부기를 불꽃세례(+2)로 쓰러뜨리고 도망(규토리 2, A8).</summary>
        List<GameEvent> WildFlees()
        {
            var s = Battles.Game(ctx, 2);
            int mine = Scene.Give(s, ctx, 0, "387:0");
            Scene.Give(s, ctx, 0, "399:0");
            Battles.SetHp(s, ctx, mine, 1);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, Scene.Spawn(s, ctx, "390:0", SeatId.Wild), Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak));
            Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(mine), log);
            return log.Events;
        }

        /// <summary>67행 기권 갈래: 이번 배틀에서 내 포켓몬이 기절했으면 기권할 수 있다(S7_8).</summary>
        List<GameEvent> Forfeit()
        {
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "390:0");
            Battles.SetHp(s, ctx, Scene.Give(s, ctx, 0, "399:0"), 0);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, Scene.Spawn(s, ctx, "399:0", SeatId.Wild));
            s.Battle.Sides[0].FaintedThisBattle = true; // 기권은 이번 배틀에서 기절한 뒤부터(rulings G13) — 기절 장면은 S7_8 시험이 흐름으로 본다
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.Forfeit), log);
            return log.Events;
        }

        /// <summary>68~70행: 불꽃숭이(먼저)가 몬스터볼로 야생 비버니(포획률 3)를 잡는다 / 놓친다. 결과가 갈리는 시드를 찾는다.</summary>
        List<GameEvent> Capture(bool success)
        {
            List<GameEvent> Try(ulong seed)
            {
                var s = Battles.Game(ctx, 2);
                Scene.Give(s, ctx, 0, "390:0");
                s.Seats[0].Balls[(int)Ball.Regular] = 1;
                var log = new EventLog();
                Battles.Wild(s, ctx, log, 0, Scene.Spawn(s, ctx, "399:0", SeatId.Wild));
                s.Rng = new Pcg32(seed);
                Battles.Answer(s, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), log);
                return log.Events;
            }
            ulong found = Battles.Seed(seed => Try(seed).Find(e => e is CaptureAttempt) is CaptureAttempt c && c.Success == success);
            return Try(found);
        }

        /// <summary>72·94행: 실제 흐름에서 자기 마을의 조무래기에게 도전. 해방 장면은 조무래기 하나를 쓰러뜨려 두고 남은 하나를 체력 1로, 이기는 시드를 찾는다.</summary>
        List<GameEvent> Grunts(bool win)
        {
            List<GameEvent> Try(ulong seed, out bool liberated)
            {
                var s = Flow.Started(ctx, 2, 34);
                int seat = s.ActiveSeat;
                var log = new EventLog();
                s.Rng = new Pcg32(seed);
                Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic), log);
                if (win)
                {
                    var roster = s.Battle.Sides[1].Roster;
                    Battles.SetHp(s, ctx, roster[1], 0);
                    Battles.SetHp(s, ctx, roster[0], 1);
                    Battles.Drive(s, ctx, log);
                }
                liberated = s.Seats[seat].ActProgress == 1;
                return log.Events;
            }
            if (!win) return Try(0, out _);
            ulong found = Battles.Seed(seed =>
            {
                Try(seed, out bool ok);
                return ok;
            });
            return Try(found, out _);
        }

        /// <summary>29행 「피함」: 자극적인 스튜를 먹은 턴이면 우두머리와 싸우지 않는다(A17).</summary>
        void WardAlpha(GameState s) =>
            s.Used.Add(new UsageMark { Seat = s.ActiveSeat, Kind = UsageKind.Stew, Key = Scene.Item(ctx, CraftRules.ZestyStew), Scope = s.TurnId });

        List<GameEvent> setupLog;

        /// <summary>셋업 한 판의 이벤트(1~6·102행이 같이 쓴다).</summary>
        List<GameEvent> Setup()
        {
            if (setupLog == null)
            {
                var log = new EventLog();
                Flow.Started(ctx, 3, 31, log);
                setupLog = log.Events;
            }
            return setupLog;
        }

        /// <summary>셋업이 끝난 2인 판에서 행동 하나(첫 MapAction부터)의 이벤트.</summary>
        List<GameEvent> Map(Action<GameState, EventLog> act)
        {
            var s = Flow.Started(ctx, 2, 32);
            var log = new EventLog();
            act(s, log);
            return log.Events;
        }

        /// <summary>뒷면 숨겨진 장소 옆 칸에서 그 칸으로 걸어 멈추고 공개 / 지나가기(25행).</summary>
        List<GameEvent> Spot(bool reveal)
        {
            var s = Flow.Started(ctx, 2, 33);
            var spot = s.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && Flow.StepInto(s, ctx, t.Cell) >= 0);
            s.Seats[s.ActiveSeat].Cell = Flow.StepInto(s, ctx, spot.Cell);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Move, spot.Cell), null);
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.HiddenSpot, ChoiceAnswer.Of(reveal ? AnswerKind.Accept : AnswerKind.Decline), log);
            return log.Events;
        }

        List<GameEvent> Maint(Func<GameState, Maintenance> make, Action<GameState> prepare)
        {
            var s = Scene.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "387:0");
            Scene.Give(s, ctx, 0, "390:0");
            prepare(s);
            var log = new EventLog();
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.Anytime(make(s)), log));
            return log.Events;
        }

        List<GameEvent> FateWindow()
        {
            var s = Scene.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "387:0");
            Scene.Give(s, ctx, 1, "390:0");
            Scene.Battle(s, BattleKind.Trainer, 0, 1);
            s.Seats[1].FateHand.AddRange(Scene.Top(s, DeckId.ChangeOfFate, 2));
            var log = new EventLog();
            Assert.IsTrue(Reactions.Open(s, ctx, Scene.Fx(s, log, 0, ActionKind.BattleAction), Window.AfterRoll));
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(s.Pending[0].Id), log));
            return log.Events;
        }

        /// <summary>15행: 울트라 버스트 2인 판에서 차례 좌석이 본맵의 왜곡 게이트(디프마시)에 서서 깨어진 세계의 짝 게이트로 건너간다.</summary>
        List<GameEvent> Gate()
        {
            var s = UbScenes.Started(ctx, 2, 5);
            int gate = UbScenes.Fixture(ctx, "gate.deepmarsh_main"), pair = UbScenes.Fixture(ctx, "gate.deepmarsh_distortion");
            s.Seats[s.ActiveSeat].Cell = ctx.Fixtures.Fixtures[gate].Cell;
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Move, ctx.Fixtures.Fixtures[pair].Cell, pair), log);
            return log.Events;
        }

        List<GameEvent> ubTurnLog;

        /// <summary>82·83행: 울트라 버스트 2인 판의 첫 라운드를 [턴 종료]로 넘긴다 — 라운드 끝의 UB 턴(이동 · 밟은 칸의 파괴 · 네크로즈마의 원격 파괴).</summary>
        List<GameEvent> UbTurn()
        {
            if (ubTurnLog != null) return ubTurnLog;
            var s = UbScenes.Started(ctx, 2, 5);
            var log = new EventLog();
            UbScenes.EndRound(s, ctx, log);
            ubTurnLog = log.Events;
            return ubTurnLog;
        }

        /// <summary>85·86행: 울트라 버스트 2인 판의 균열을 percent%까지 올려 둔 뒤 라운드를 넘긴다 — UB 턴이 10% 구간(9 → 10%+) · 70%(69 → 70%+)를 넘는다.</summary>
        List<GameEvent> UbBreached(int percent)
        {
            var s = UbScenes.Started(ctx, 2, 5);
            Assert.AreEqual(percent, UbScenes.Breach(s, percent), "균열을 맞추지 못했다");
            var log = new EventLog();
            UbScenes.EndRound(s, ctx, log);
            return log.Events;
        }

        /// <summary>83행 토큰 날기: 울트라 버스트 2인 판에서 본맵의 뒷면 숨겨진 장소 칸을 UB가 밟는다(UbTurn.Interact — 깨어진 세계의 빈 칸으로).</summary>
        List<GameEvent> SpotTrampled()
        {
            var s = UbScenes.Started(ctx, 2, 5);
            var spot = s.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && t.Cell >= 0 && !ctx.Board[t.Cell].DistortionWorld);
            Assert.IsNotNull(spot, "시험 준비: 본맵의 숨겨진 장소");
            var ub = s.Tokens.Find(t => t.Kind == TokenKind.UltraBeast && t.Cell >= 0);
            var log = new EventLog();
            Sinnoh.Rules.UbTurn.Interact(s, ctx, Scene.Fx(s, log, SeatId.UltraBeast, ActionKind.UbTurn), spot.Cell, ub.Def, s.Rng);
            return log.Events;
        }

        enum UbFoe { Beast, Necrozma, Lake, Silvally }

        /// <summary>
        /// 88~92행: 혼자 하는 울트라 버스트 판(UB 첫 토큰 · 네크로즈마 · 입지호수 동굴 — 아그놈, 의지의 시련)의 그 칸에서 찾아보기 → 배틀을 기본 답으로 몬다(weak = 상대 체력 1).
        /// 게임 난수를 0부터 다시 심어 done이 참인 첫 판의 이벤트.
        /// </summary>
        List<GameEvent> UbFight(UbFoe foe, Action<GameState> prepare, bool weak, Predicate<List<GameEvent>> done, bool onslaught = false)
        {
            for (ulong seed = 0; seed < 80; seed++)
            {
                var s = UbScenes.Started(ctx, 1, 3, null, onslaught ? new RuleOptions { UltraOnslaught = true } : null);
                prepare?.Invoke(s);
                int cell = foe == UbFoe.Lake ? ctx.Fixtures.Fixtures[UbScenes.Fixture(ctx, "legendary_site.lake_valor_cavern")].Cell
                    : foe == UbFoe.Silvally ? ctx.Fixtures.Fixtures[UbScenes.Fixture(ctx, "legendary_site.temple_of_sinnoh")].Cell
                    : s.Tokens.Find(t => t.Cell >= 0 && t.Kind == (foe == UbFoe.Beast ? TokenKind.UltraBeast : TokenKind.Necrozma)).Cell;
                s.Seats[s.ActiveSeat].Cell = cell;
                s.Rng.CopyFrom(new Pcg32(seed));
                var log = new EventLog();
                var search = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.Search);
                Assert.IsNotNull(search, foe + " 칸에서 찾아보기");
                Flow.Answer(s, ctx, ChoiceKind.MapAction, search, log);
                if (weak && s.Battle != null) Battles.SetHp(s, ctx, s.Battle.Sides[1].Roster[0], 1);
                for (int i = 0; i < 400 && !done(log.Events) && s.Battle != null; i++)
                {
                    var c = s.Pending.Find(x => !x.Answered);
                    if (c == null) break;
                    Battles.Answer(s, ctx, c, c.Default.Clone(), log);
                }
                if (done(log.Events)) return log.Events;
            }
            Assert.Fail(foe + ": 원하는 이벤트가 나오는 시드가 없다");
            return null;
        }

        // ── M6 「트레이너 카드 해금 연출」(35·48·49·50행) ──

        /// <summary>35행: 2인 판의 차례 좌석이 사당에서 여행 점수 3으로 날씨를 모래바람으로 바꾼다(카드가 없어도 — 「Any trainer」).</summary>
        List<GameEvent> WeatherWheel()
        {
            var s = Flow.Started(ctx, 2, 5);
            int seat = s.ActiveSeat;
            s.Weather = 0;
            int shrine = s.Tokens.FindIndex(x => x.Kind == TokenKind.Shrine && x.Cell >= 0 && !x.Destroyed);
            s.Seats[seat].Cell = s.Tokens[shrine].Cell;
            s.Seats[seat].Jp = TrainerRules.WeatherCost;
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.UseAbility, TrainerRules.WeatherTarget, 3), log);
            return log.Events;
        }

        /// <summary>48행: 브론즈 카드(카드 표 첫 장)를 가진 좌석 0이 체력 1인 야생 적당한 포켓몬(모부기 진화형)을 쓰러뜨려 실버가 된다.</summary>
        List<GameEvent> Unlock()
        {
            var s = Battles.Game(ctx, 2, 7);
            s.Seats[0].TrainerCard = 0;
            s.Seats[0].TrainerCardTier = TrainerRules.Bronze;
            Scene.Give(s, ctx, 0, "445:0");
            int wild = Scene.Spawn(s, ctx, "388:0", SeatId.Wild);
            Battles.SetHp(s, ctx, wild, 1);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild);
            Battles.Drive(s, ctx, log);
            return log.Events;
        }

        /// <summary>49행: 골드 반바지 꼬마(빨강 「When unlocked」로 뽑아 둔 후보 셋)가 턴을 넘기면 묻는 카드 바꾸기에서 첫 후보를 고른다(새 카드 = 골드).</summary>
        List<GameEvent> CardSwap()
        {
            var s = Flow.Started(ctx, 2, 32);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            st.TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("youngster");
            st.TrainerCardTier = TrainerRules.Gold;
            st.TrainerCardOffer.Clear();
            foreach (string k in new[] { "hiker", "swimmer", "chef" }) st.TrainerCardOffer.Add(ctx.Cards.TrainerCardIds.IndexOf(k));
            st.UnlockDue.Add(TrainerRules.DueSwap);
            st.UnlockDue.Add(0);
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
            var c = s.Pending.Find(p => p.Kind == ChoiceKind.Reaction && p.Sub == ChoiceSub.EffectPick && p.Seat == seat);
            Assert.IsNotNull(c, "시험 준비: 카드 바꾸기 물음");
            Scene.Accept(Engine.Submit(s, ctx, seat, Command.AnswerTo(c.Id, ChoiceAnswer.Pick(c.Options[0])), log), "카드 고르기");
            return log.Events;
        }

        /// <summary>50행: 골드 정원사(gardener)가 선 약한 나무에서 파랑 능력으로 규토리 1개를 받는다.</summary>
        List<GameEvent> AbilityUse()
        {
            var s = Flow.Started(ctx, 2, 32);
            int seat = s.ActiveSeat;
            s.Seats[seat].TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("gardener");
            s.Seats[seat].TrainerCardTier = TrainerRules.Gold;
            s.Seats[seat].Cell = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak)].Cell;
            var use = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.UseAbility);
            Assert.IsNotNull(use, "시험 준비: 나무 칸의 능력 답");
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, use, log);
            return log.Events;
        }

        List<GameEvent> Fate(Action<Ctx, EventLog> scene)
        {
            var log = new EventLog();
            scene(ctx, log);
            return log.Events;
        }

        /// <summary>조각 둘을 가진 좌석이 셋째 호수의 간부를 이긴다(2막 목표 달성 → 3막 공개).</summary>
        List<GameEvent> LakeWin()
        {
            var log = new EventLog();
            ActScenes.Lake(ctx, log, Shard.Willpower, 2);
            return log.Events;
        }

        /// <summary>태홍을 이긴다(3막 목표 → 종막 카드 공개 · 종막 선택).</summary>
        List<GameEvent> TempleWin()
        {
            var log = new EventLog();
            ActScenes.Ending(ctx, 3, log);
            return log.Events;
        }

        /// <summary>종막 선택(영웅이면 곧바로 게임 끝, 배신자면 모두 회복 → 순서 고르기 → 첫 연전).</summary>
        List<GameEvent> Finale(bool traitor)
        {
            var s = ActScenes.Ending(ctx, 3, new EventLog());
            var log = new EventLog();
            Battles.Answer(s, ctx, ChoiceKind.Ending, ChoiceAnswer.Pick(traitor ? 1 : 0), log);
            if (traitor) Battles.Answer(s, ctx, ChoiceKind.Ending, ChoiceAnswer.PickMany(new List<int> { 2, 1 }), log);
            return log.Events;
        }

        // ── M5 「스탬프·도전·거래 화면」(47·51·52행 — DealStagingTests와 같은 판) ──

        /// <summary>좌석 둘 판의 첫 턴, 두 좌석이 같은 칸.</summary>
        GameState SameTile()
        {
            var s = Flow.Started(ctx, 2, 32);
            s.Seats[1 - s.ActiveSeat].Cell = s.Seats[s.ActiveSeat].Cell;
            return s;
        }

        /// <summary>47행: 스탬프 하나(트레이너 배틀 승리 조건) — 첫 보상 규토리 세 색.</summary>
        List<GameEvent> Stamps()
        {
            var s = Flow.Started(ctx, 2, 32);
            var log = new EventLog();
            var fx = new Emitter(s, log);
            fx.Begin(s.ActiveSeat, ActionKind.ProposeTrade, AutoKind.Human);
            Assert.IsTrue(StampRules.Gain(s, ctx, fx, s.ActiveSeat, StampRules.TrainerWin));
            return log.Events;
        }

        /// <summary>51행: 같은 칸 도전. answered면 상대의 응답(수락)까지.</summary>
        List<GameEvent> Challenge(bool answered)
        {
            var s = SameTile();
            var log = new EventLog();
            int them = 1 - s.ActiveSeat;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.Challenge && a.Target == them), log);
            if (!answered) return log.Events;
            var respond = s.Pending.Find(c => c.Kind == ChoiceKind.Respond && c.Seat == them);
            Assert.IsNotNull(respond, "시험 준비: 도전 응답 창");
            Scene.Accept(Engine.Submit(s, ctx, them, Command.AnswerTo(respond.Id, ChoiceAnswer.Of(AnswerKind.Accept)), log), "수락");
            return log.Events;
        }

        /// <summary>52행: 내 턴의 거래 제안(파랑 규토리 2 ↔ 여행 점수 3)을 상대가 받아들인다.</summary>
        List<GameEvent> Trade()
        {
            var s = SameTile();
            var log = new EventLog();
            int me = s.ActiveSeat, them = 1 - me;
            s.Seats[me].Apricorns[(int)ApricornColor.Blue] = 2;
            s.Seats[them].Jp = 3;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, TradeRules.Offer(them, 0, (int)TradeKind.Apricorn, (int)ApricornColor.Blue, 2, 1, (int)TradeKind.Jp, -1, 3), log);
            var respond = s.Pending.Find(c => c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Trade);
            Scene.Accept(Engine.Submit(s, ctx, them, Command.AnswerTo(respond.Id, ChoiceAnswer.Of(AnswerKind.Accept)), log), "수락");
            return log.Events;
        }

        List<GameEvent> Secret()
        {
            var s = Scene.Game(ctx, 2);
            var log = new EventLog();
            var group = new List<PendingChoice>
            {
                new PendingChoice { Kind = ChoiceKind.Simultaneous, Sub = ChoiceSub.Follower, Seat = 0, Secret = true },
                new PendingChoice { Kind = ChoiceKind.Simultaneous, Sub = ChoiceSub.Follower, Seat = 1, Secret = true },
            };
            ChoiceRules.OpenGroup(s, ctx, Scene.Fx(s, log), group);
            Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(group[1].Id, ChoiceAnswer.Pick((int)SinnohGod.Palkia)), log));
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(group[0].Id, ChoiceAnswer.Pick((int)SinnohGod.Dialga)), log));
            return log.Events;
        }

        [Test]
        public void M3_연출목록액션마다이벤트값()
        {
            var scenes = Scenes();
            int checkedNeeds = 0;
            foreach (var kv in scenes)
            {
                var events = kv.Value();
                CheckHeaders(kv.Key, events);
                foreach (var n in FxContract.Table)
                {
                    if (n.Key != kv.Key) continue;
                    var t = EventType(n.Type);
                    GameEvent hit = null;
                    foreach (var e in events)
                        if (e.GetType() == t && Matches(e, n.Expect)) { hit = e; break; }
                    Assert.IsNotNull(hit, $"{n.Key}행: {n.Type}({n.Expect})가 나오지 않았다");
                    foreach (var f in n.Fields)
                        Assert.IsTrue(Filled(t.GetField(f).GetValue(hit)), $"{n.Key}행 {n.Type}.{f}가 채워지지 않았다");
                    checkedNeeds++;
                }
            }
            Assert.Greater(checkedNeeds, 0);
        }

        /// <summary>공통 머리(01 §3-6 ②⑪): Seq마다 ActionStarted가 먼저, Index는 0부터 늘고(동시 적용 묶음은 같은 Index), Seat·Source·ParentSource가 채워지고 Seq는 늘기만 한다.</summary>
        static void CheckHeaders(string key, List<GameEvent> events)
        {
            int seq = -1, index = -1;
            foreach (var e in events)
            {
                Assert.AreNotEqual(GameEvent.U, e.Seat, $"{key}: {e.GetType().Name}.Seat");
                Assert.IsFalse(e.Source.IsUnset, $"{key}: {e.GetType().Name}.Source");
                Assert.IsFalse(e.ParentSource.IsUnset, $"{key}: {e.GetType().Name}.ParentSource");
                if (e.Seq != seq)
                {
                    Assert.Greater(e.Seq, seq, $"{key}: Seq가 줄었다");
                    Assert.IsInstanceOf<ActionStarted>(e, $"{key}: Seq {e.Seq}의 첫 이벤트가 ActionStarted가 아니다");
                    Assert.AreEqual(0, e.Index);
                    seq = e.Seq;
                    index = 0;
                    continue;
                }
                // 동시에 적용하는 묶음(한 턴 피해 버퍼)은 같은 Index다(01 §3-6 ⑪). 그 밖에는 늘기만 한다
                if (e.Index == index) Assert.IsTrue(e is HpChanged || e is StatusTicked, $"{key}: {e.GetType().Name} Index가 겹친다(같은 Index는 동시 적용 묶음만)");
                else Assert.Greater(e.Index, index, $"{key}: {e.GetType().Name} Index 순서");
                index = e.Index;
            }
        }
    }
}
