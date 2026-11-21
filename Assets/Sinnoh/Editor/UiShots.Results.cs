using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M5 「맵 결과·배틀 안 연출」의 UI 캡처 장면(04 §8-8 23·24·26~30·46·74~81행과 570·641 `Seek` — 결정 74): 행마다 그 단계 안의 순간들.
    /// 판은 혼자 판(<c>GameFlow.StartSeats</c> 1)을 손봐 연다 — 숨겨진 장소의 정체 · 전설 장소·게이트·시작의 사당에 선 좌석 · 전설 특수기술을 가진 포켓몬.
    /// 글자 넘침은 장면마다 단정한다(<c>WorkUi.Overflows</c> — 결과 꼬리표 · 배틀 기둥·독·결과 창).
    /// </summary>
    public static partial class UiShots
    {
        static UiShots()
        {
            foreach (var kv in ResultScreens) OverBoard.Add(kv.Key);   // 보드 위에 얹히는 화면(배틀 화면도 위 띠 아래로 보드가 비친다 — 다른 row-* 장면과 같다)
            foreach (var kv in CardScreens) OverBoard.Add(kv.Key);   // M6 트레이너 카드 장면(UiShots.Cards.cs)
            foreach (var kv in AssetScreens) OverBoard.Add(kv.Key);    // M6 그림 에셋(UiShots.Assets.cs) — 정비 판 · 카드 확대 · 트레이너 카드 판(보드 위 창)
            foreach (var kv in UbScreens) OverBoard.Add(kv.Key);       // M6 「UB 보드 화면」(UiShots.Ub.cs) — 울트라 버스트 판의 보드
            foreach (var kv in UbStartScreens) OverBoard.Add(kv.Key);  // M6 「UB 시작」(UiShots.UbStart.cs) — 솔로 설정 · 셋업 · 보드 · 기록 · 네크로즈마 폼(모두 보드 위)
            foreach (var kv in UbHudScreens) OverBoard.Add(kv.Key);    // M6 「UB 화면 묶음 1」(UiShots.UbHud.cs) — 균열 칩 · UB 턴 띠 · 84~86행(보드 위)
            foreach (var kv in UbBattleScreens) OverBoard.Add(kv.Key); // M6 「UB 배틀 화면 묶음」(UiShots.UbBattle.cs) — 87~92행 · 울트라 버스트 결과 창(보드 위)
            foreach (var kv in FillScreens) OverBoard.Add(kv.Key);     // M6 「연출 목록 채우기」(UiShots.Fill.cs) — 83행 토큰 날기 · 921 · 93행(보드 위)
            foreach (var kv in Ub2Screens) OverBoard.Add(kv.Key);      // M6 「UB 화면 묶음 2」(UiShots.Ub2.cs) — 균열 카드 창 · 축복 · 탈락(모두 보드 위)
            foreach (var kv in Answers87Screens) OverBoard.Add(kv.Key); // 결정 87 화면 몫(UiShots.Answers87.cs)
            foreach (var kv in PlayFeedbackScreens) OverBoard.Add(kv.Key); // 결정 101 화면 다섯(UiShots.PlayFeedback.cs)
            foreach (var kv in PlayFeedback2Screens) OverBoard.Add(kv.Key); // 결정 104·105·106 해 보기 피드백 2(UiShots.PlayFeedback2.cs)
            foreach (var kv in DexScreens) OverBoard.Add(kv.Key);   // 포켓몬 도감 S42(UiShots.Dex.cs — 결정 105 ③)
            foreach (var kv in EnIconScreens) OverBoard.Add(kv.Key);   // M9 영문 아이콘(UiShots.EnIcons.cs — 결정 115) — 도감 · 배틀 화면(보드 위)
            foreach (var kv in RulebookScreens) OverBoard.Add(kv.Key);   // 룰 설명서 S43 · 크레딧 S03(UiShots.Rulebook.cs — M9)
            foreach (var kv in UnlockScreens) OverBoard.Add(kv.Key);   // M6 「트레이너 카드 해금 연출」(UiShots.Unlock.cs) — 결과 창 · 보드 위 카드 뒤집기 · 꼬리표 · 작은 카드 · 날씨 배지
            foreach (var kv in TutorialMenuScreens) OverBoard.Add(kv.Key);   // M6.5 「화면 ② 목차·입구」(UiShots.TutorialMenu.cs) — 타이틀 · 목차 · 튜토리얼 판의 창(모두 보드 위)
            foreach (var kv in RoomScreens) OverBoard.Add(kv.Key);   // M7 「화면 ①」(UiShots.Room.cs) — 방 만들기 · 참가하기 · 로비 · 방 설정(모두 보드 위)
            foreach (var kv in PeekScreens) OverBoard.Add(kv.Key);   // M9 다듬기 P09 「남의 고르기 보기」(UiShots.Peek.cs)
            foreach (var kv in Room2Screens) OverBoard.Add(kv.Key);  // M7 「화면 ②」(UiShots.Room2.cs) — 대기실 · 재접속 덮개 · 응답 시계 · 투표 · 게임 메뉴의 방 줄(모두 보드 위)
            foreach (var kv in M8Screens) OverBoard.Add(kv.Key);     // M8 「연결」(UiShots.M8.cs) — 솔로 · 방 설정의 「강함」 · 강한 AI 자막(모두 보드 위)
            foreach (var kv in Polish6Screens) OverBoard.Add(kv.Key);   // M9 다듬기 6 「사당 · 전설 연출」(UiShots.Polish6.cs — 모두 보드 위)
            foreach (var kv in Polish5Screens) OverBoard.Add(kv.Key);   // M9 다듬기 5(UiShots.Polish5.cs) — P18 · P19(모두 보드 위)
            foreach (var kv in Polish6CheckScreens) OverBoard.Add(kv.Key);   // M9 다듬기 4 · 6 확인(UiShots.Polish6Check.cs — 모두 보드 위)
            foreach (var kv in Polish7Screens) OverBoard.Add(kv.Key);   // M9 다듬기 7(UiShots.Polish7.cs) — P22 · P23 · P31(모두 보드 위)
            foreach (var kv in PlayTestAScreens) OverBoard.Add(kv.Key);   // M9 플레이 시험 「창 · 입력 · 정보 표시」(UiShots.PlayTestA.cs — 모두 보드 위)
            foreach (var kv in Polish8Screens) OverBoard.Add(kv.Key); // M9 다듬기 8(UiShots.Polish8.cs) — P14 조우 전환 무늬 · P39 공간 일그러짐(보드 위)
            foreach (var kv in ConsentScreens) OverBoard.Add(kv.Key);   // 플레이 기록 동의(UiShots.Consent.cs — 결정 126 ④, 모두 보드 위)
            foreach (var kv in BugScreens) OverBoard.Add(kv.Key);       // 버그 신고(UiShots.Bug.cs — 결정 132 ①, 모두 보드 위)
            foreach (var kv in ChatScreens) OverBoard.Add(kv.Key);
            foreach (var kv in DiscardScreens) OverBoard.Add(kv.Key);   // 버림 더미 보기 창(UiShots.Discards.cs — 결정 196, 보드 위)      // 방 채팅(UiShots.Chat.cs — 결정 140 ②, 모두 보드 위)
            foreach (var kv in Tempo143Screens) OverBoard.Add(kv.Key);  // 결정 143 ⑤ 선출 중 DS · ⑦ 준비 동작(UiShots.Tempo143.cs — 배틀 화면, 보드 위)
            foreach (var kv in AuditMemoScreens) OverBoard.Add(kv.Key); // M8 · M9 마감 감사 2차 메모(UiShots.AuditMemo.cs — 턴 배지 · 시간 초과 뒤 창, 보드 위)
            foreach (var kv in AuditGapScreens) OverBoard.Add(kv.Key);  // M8 · M9 마감 감사가 못 본 장면(UiShots.AuditGaps.cs — 결정 123 · 128 · 121, 모두 보드 위)
            foreach (var kv in UndoScreens) OverBoard.Add(kv.Key);      // 무르기 · 되돌리기(UiShots.Undo.cs — 결정 149 · 154, 모두 보드 위)
            foreach (var kv in RotateScreens) OverBoard.Add(kv.Key);    // 보드 돌리기 단추 · 배틀 단축키(UiShots.Rotate.cs — 결정 164, 모두 보드 위)
            foreach (var kv in BattleUiScreens) OverBoard.Add(kv.Key);  // 배틀 화면 묶음(UiShots.BattleUi.cs — 결정 169 · 170 · 171, 배틀 화면 · 보드 위)
            foreach (var kv in FlyPathScreens) OverBoard.Add(kv.Key);   // 공중날기 컷인 · 길 육각형(UiShots.FlyPath.cs — 결정 167 · 168, 모두 보드 위)
            foreach (var kv in UbHoverScreens) OverBoard.Add(kv.Key);   // UB 이름 · 시련 마우스 오버(UiShots.UbHover.cs — 결정 179, 모두 보드 위)
            foreach (var kv in BeastBoostScreens) OverBoard.Add(kv.Key);   // 비스트 부스트 빛 알갱이(UiShots.BeastBoost.cs — 결정 184, 배틀 화면 · 보드 위)
            foreach (var kv in RecipeGradeScreens) OverBoard.Add(kv.Key);   // 조합표 · 등급 색(UiShots.RecipeGrade.cs — 결정 191 · 192, 보드 · 배틀 화면 위)
            foreach (var kv in GodAgainScreens) OverBoard.Add(kv.Key);   // 신 격파 「한 번 더!」는 추가 턴이 올 때만(UiShots.GodAgain.cs — 결정 198, 보드 위)
            foreach (var kv in SwarmAlphaScreens) OverBoard.Add(kv.Key);   // 무리 줄 카드 · 알파 타입 ±0(UiShots.SwarmAlpha.cs — 결정 199, 배틀 화면)
        }

        /// <summary><see cref="Screens"/> + 이 묶음의 장면(이름으로 찾는 곳은 이 목록을 쓴다).</summary>
        public static IEnumerable<KeyValuePair<string, Action<UiShot>>> AllScreens
        {
            get
            {
                foreach (var s in Screens) yield return s;
                foreach (var s in ResultScreens) yield return s;
                foreach (var s in CardScreens) yield return s;
                foreach (var s in AssetScreens) yield return s;
                foreach (var s in UbScreens) yield return s;
                foreach (var s in UbStartScreens) yield return s;
                foreach (var s in UbHudScreens) yield return s;
                foreach (var s in UbBattleScreens) yield return s;
                foreach (var s in FillScreens) yield return s;
                foreach (var s in Ub2Screens) yield return s;
                foreach (var s in UnlockScreens) yield return s;
                foreach (var s in Answers87Screens) yield return s;
                foreach (var s in TutorialScreens) yield return s;
                foreach (var s in TutorialMenuScreens) yield return s;
                foreach (var s in RoomScreens) yield return s;
                foreach (var s in Room2Screens) yield return s;
                foreach (var s in PeekScreens) yield return s;
                foreach (var s in PlayFeedbackScreens) yield return s;
                foreach (var s in PlayFeedback2Screens) yield return s;
                foreach (var s in DexScreens) yield return s;
                foreach (var s in RulebookScreens) yield return s;
                foreach (var s in Polish8Screens) yield return s;
                foreach (var s in M8Screens) yield return s;
                foreach (var s in EnIconScreens) yield return s;
                foreach (var s in Polish6Screens) yield return s;
                foreach (var s in Polish5Screens) yield return s;
                foreach (var s in Polish6CheckScreens) yield return s;
                foreach (var s in Polish7Screens) yield return s;
                foreach (var s in PlayTestAScreens) yield return s;
                foreach (var s in ConsentScreens) yield return s;
                foreach (var s in BugScreens) yield return s;
                foreach (var s in ChatScreens) yield return s;
                foreach (var s in DiscardScreens) yield return s;
                foreach (var s in Tempo143Screens) yield return s;
                foreach (var s in AuditMemoScreens) yield return s;
                foreach (var s in AuditGapScreens) yield return s;
                foreach (var s in UndoScreens) yield return s;
                foreach (var s in RotateScreens) yield return s;
                foreach (var s in BattleUiScreens) yield return s;
                foreach (var s in FlyPathScreens) yield return s;
                foreach (var s in UbHoverScreens) yield return s;
                foreach (var s in BeastBoostScreens) yield return s;
                foreach (var s in RecipeGradeScreens) yield return s;
                foreach (var s in GodAgainScreens) yield return s;
                foreach (var s in SwarmAlphaScreens) yield return s;
            }
        }

        static KeyValuePair<string, Action<UiShot>> Battle(string name, Action<UiShot> build) => Screen(name, FitBattle(build));

        public static readonly KeyValuePair<string, Action<UiShot>>[] ResultScreens =
        {
            // 26행 규토리 더미: 부풂 → 터져 튀는 중 → 말 쪽으로 모이며 「빨강 규토리 +3」
            Screen("row-26-swell", FitBoard(s => SpotScene(s, "stash", 180))),
            Screen("row-26-burst", FitBoard(s => SpotScene(s, "stash", 480))),
            Screen("row-26-gain", FitBoard(s => SpotScene(s, "stash", 1150))),
            // 27행 야영지: 접혔다 펴지는 중 → 「야영지 · 회복 · 은행상회」
            Screen("row-27-unfold", FitBoard(s => SpotScene(s, "camp", 330))),
            Screen("row-27-camp", FitBoard(s => SpotScene(s, "camp", 750))),
            // 28행 떨어진 별: 떠오름 → 위 띠로 나는 중 → 박힘 「떨어진 별 n개」 → 4개째의 띠 빛 「매 턴 운명의 변화 1장」
            Screen("row-28-rise", FitBoard(s => SpotScene(s, "star", 280))),
            Screen("row-28-fly", FitBoard(s => SpotScene(s, "star", 650))),
            Screen("row-28-land", FitBoard(s => SpotScene(s, "star", 1250))),
            Screen("row-28-fourth", FitBoard(s => SpotScene(s, "star", 2200, state => state.FallenStars = 3))),
            // 29행 우두머리: 붉은 섬광 + 땅울림 → 「우두머리 포켓몬! · 체력 +4」
            Screen("row-29-flash", FitBoard(s => SpotScene(s, "alpha", 520))),
            Screen("row-29-hp", FitBoard(s => SpotScene(s, "alpha", 1500))),
            Screen("row-29-alpha", FitBoard(s => SpotScene(s, "alpha", 2100))),
            // 30행 유물: 빛기둥 → 호를 그려 내 판으로 → 이름과 위력이 오르는 타입
            Screen("row-30-pillar", FitBoard(s => SpotScene(s, "relic", 250))),
            Screen("row-30-fly", FitBoard(s => SpotScene(s, "relic", 700))),
            Screen("row-30-relic", FitBoard(s => SpotScene(s, "relic", 1400))),
            // 23행 전설 장소 찾아보기: 유물이 칸으로 날아가는 중 → 공명 + 땅울림(그 뒤 조우 전환)
            Screen("row-23-relic", FitBoard(s => LegendSearch(s, 300))),
            Screen("row-23-quake", FitBoard(s => LegendSearch(s, 620))),
            // 77행 우두머리 배틀: 인트로의 붉은 기운 → 내 차례(기둥의 「우두머리 · 체력 +4」)
            Battle("row-77-aura", s => AlphaBattle(s, true)),
            Battle("row-77-alpha", s => AlphaBattle(s, false)),
            // 24·81행 무리(S55): 명단이 약한 것부터 서는 중 → 내 차례 ●○○ → 첫 마리를 쓰러뜨린 뒤 → 결과 창(JP 7)
            Battle("row-24-line", s => SwarmBattle(s, 0)),
            Battle("row-24-swarm", s => SwarmBattle(s, 1)),
            Battle("row-81-dot", s => SwarmBattle(s, 2)),
            Battle("row-81-won", s => SwarmBattle(s, 3)),
            // 76행 알파 스트라이크: 기술 줄 → 타입 줄 → 고른 타입 색 오라 · 비스트 부스트(641 `Seek`): 붉은 오라
            Battle("row-76-move", s => AlphaStrike(s, 0)),
            Battle("row-76-types", s => AlphaStrike(s, 1)),
            Battle("row-76-aura", s => AlphaStrike(s, 2)),
            Battle("row-76-boost", s => AlphaStrike(s, 3)),
            // M5 마감 답 반영 나-1(결정 80 N1 가): 대기 포켓몬의 기술 줄([대기 포켓몬 기술] → 줄 → 나오며 쓴다 · 넷이면 셋씩 두 쪽) · 부른 기술 알림 · DS 둘째 상태 배지(내 쪽 · 상대 쪽)
            Battle("battle-partymove", s => LeftBattle(s, 0, false)),
            Battle("battle-partymove-open", s => LeftBattle(s, 1, false)),
            Battle("battle-partymove-many", s => LeftBattle(s, 1, true)),
            Battle("battle-partymove-next", s => LeftBattle(s, 5, true)),
            Battle("battle-partymove-used", s => LeftBattle(s, 2, false)),
            Battle("battle-called", s => LeftBattle(s, 3, false)),
            Battle("battle-status2", s => LeftBattle(s, 4, false)),
            // 유물 폼체인지(정비 판): [폼체인지] 줄 → 물음 줄(폼이 여럿이면 [다른 폼]) → 바뀐 모습
            Screen("board-party-form", Fit(s => LeftParty(s, 0))),
            Screen("board-party-form-ask", Fit(s => LeftParty(s, 1))),
            Screen("board-party-form-done", Fit(s => LeftParty(s, 2))),
            // 74행 은하의 응징: 저장(구슬 → 배지) → 옮기기(배지 → 내 포켓몬)
            Battle("row-74-stored", s => Retribution(s, ResultRows.Stored, 250)),
            Battle("row-74-badge", s => Retribution(s, ResultRows.Stored, 450)),
            Battle("row-74-moved", s => Retribution(s, ResultRows.Moved, -55)),   // 구슬이 배지에서 상대 포켓몬으로 날아가는 한가운데(모이기 25% · 닿기 70% — 600ms는 닿은 뒤였다)
            // 75행 교체 금지: 자물쇠 칩
            Battle("row-75-lock", s => Locked(s)),
            // 결정 80 L16 나: DS 체력 상자 옆의 작은 그림 — 응징 구슬 + 쌓인 수(상대 상자 위) · 마우스 오버(교체 불가 자물쇠는 row-75-lock이 단정한다)
            Battle("battle-ds-retribution", s => DsRetribution(s, false)),
            Battle("battle-ds-retribution-tip", s => DsRetribution(s, true)),
            // 79행 폼 바꾸기: 흰 빛 속에서 번갈아 → 새 폼
            Battle("row-79-white", s => FormMove(s, 400)),
            Battle("row-79-form", s => FormMove(s, 900)),
            // 팬 폼 오라(결정 74 ③ — 수호신 폼의 금빛 오라, 기본 도트 + 효과)
            Battle("row-79-fan", s => FanAura(s)),
            // 78행 신 둘째 폼: 빛기둥 → 둘째 폼(체력이 찬다)
            Battle("row-78-aura", s => GodBattle(s, -1, 0)),   // 결정 80 L15 나: 천상의 오라가 걸린 신의 첫 내 차례 — 도트 둘레에 옅은 금빛 오라가 남는다
            Battle("row-78-pillar", s => GodBattle(s, ResultRows.GodPhase, 1700)),
            Battle("row-78-second", s => GodBattle(s, ResultRows.GodPhase, 2500)),
            // 80행 신 격파: 결과 창 [확인] 뒤 보드에서 「한 번 더!」
            Screen("row-80-again", FitBoard(s => GodBattle(s, ResultRows.GodGone, 700))),
            // 46행 승리 보상: 고르기(유물 · 운명 2장) → 결과 창의 줄
            Battle("row-46-ask", s => Spoils(s, -1)),
            Battle("row-46-relic", s => Spoils(s, 0)),
            Battle("row-46-fate", s => Spoils(s, 1)),
            // 570 면 바꾸기 `Seek`(결정 74 — 기술 효과의 면 바꾸기): 물기를 맞은 상대를 다시 칠 때 일반 타격 하나가 더해지는 중
            Battle("row-57-flip", s => MoveRow(s, "bite", st => st.Row == 570, 200)),
            // 641 피로 칸 `Seek`(결정 74 — 기술 효과의 피로): 모래지옥이 상대의 전투 피로를 2 올리는 중
            Battle("row-64-fatigue", s => MoveRow(s, "sand_tomb", st => st.Row == 641 && st.Events.Exists(e => e is FatigueChanged f && f.Cause == FatigueCause.Effect), 150)),
            // ── 여럿 판 것(M5 마감 답 반영 마 — 결정 80 N2 나: 좌석 여럿 견본 판) ──
            Screen("board-stamp-seat", Fit(s => SeatStamp(s))),                                      // 남 좌석 정비 판의 「여행 스탬프 n/7」 → 그 좌석의 스탬프 판(제목에 좌석 이름)
            Screen("board-party-stamps", s => SeatStamp(s, true)),                                   // 그 앞: 남 좌석 정비 판의 「여행 스탬프 n/7」에 마우스를 올린 툴팁
            Screen("board-hud-did", s => OtherDid(s, AnswerKind.Craft, WorkRows.Craft, 600)),          // 남 좌석의 제작: 그 좌석 칩 아래 「제작」 꼬리표
            Screen("board-hud-did-offer", s => OtherDid(s, AnswerKind.Offer, ShrineRows.Offer, 900)),  // 남 좌석의 공물: 「공물」 꼬리표(사당 판은 열리지 않는다)
            Screen("watch-craft", s => Watched(s, AnswerKind.Craft, WorkRows.Craft, 520, typeof(CraftScreen))),          // 남의 제작 = 제작 창과 같은 보기 창(결정 120 ① · T6) — 펑 뒤 「○○ 제작!」
            Screen("watch-craft-stew", s => Watched(s, AnswerKind.Craft, WorkRows.Craft, 520, typeof(CraftScreen), stew: true)),   // 스튜는 붉은 꼬리표로 뚜렷이(T6)
            Screen("watch-offer", s => Watched(s, AnswerKind.Offer, ShrineRows.Offer, 900, typeof(ShrineScreen))),       // 남의 공물 = 사당 판과 같은 보기 판(T9)
            Screen("watch-shrine-pick", s => WatchedPick(s)),                                                             // 남의 사당 카드 고르기(공개) — 단추 없이 「○○이 고르는 중」
            Screen("watch-encounter", s => WatchedEncounter(s)),
            Screen("board-road-mine", s => Road(s, true)),     // worker B 도로 켬: 내 HUD 「행동」 머리 줄 칩 + 내 좌석 칩 배지 + 기록 줄(한 칸 걸은 뒤)
            Screen("board-road-other", s => Road(s, false)),   // 남(좌석 2)의 도로 켬: 그 좌석 칩 배지(+ 툴팁)
            Screen("watch-maint", s => WatchedMaint(s)),          // 남(좌석 2)의 정비(지닌물건 지니기): 보기 창 없이 그 좌석 칩 꼬리표 「정비」 · 기록 줄만(결정 177)
            Screen("board-hud-did-stamp", s => OtherStamp(s)),    // 6인 판 남(좌석 2)의 스탬프: 도장 덮개 없이 그 좌석 칩 꼬리표 「스탬프 1/7」(결정 178 — 칩 폭 안)
            Screen("watch-shrine-gift", s => WatchedGift(s)),     // 남(좌석 2)의 사당 뜻밖의 선물 → 고른 규토리: 그 좌석의 판 보기(제목 「사당 보상」 · 가방의 규토리 수 — 결정 120)
            Screen("board-reach-other", s => Reach(s)),         // 남(좌석 2)이 염력(psychic B)으로 고른 떨어진 칸: 능력 사용 50행 300ms에 그 칸 위 좌석 색 표시
            // ── 남 좌석 창 · 덮개 머리의 「누구」(결정 120 — #707): 그 좌석 얼굴 상자(좌석 색 · 번호 · 주인공) + 「○○의 …」 ──
            Screen("owner-ability", s => Reach(s, AbilityBeats.BounceAt + AbilityBeats.BounceMs / 2)),   // 남(좌석 2)의 능력 사용 작은 카드: 얼굴 상자 + 「○○의 염력」
            Screen("watch-bank", s => Watched(s, AnswerKind.Ginkgo, WorkRows.Bank, 600, typeof(VillageScreen))),       // 남의 은행상회 보기 창
            Screen("watch-evolve", s => Watched(s, AnswerKind.Evolve, WorkRows.Evolve, 600, typeof(EvolveScreen))),    // 남의 진화 보기 창
            Screen("board-party-other", Fit(s => SeatOpened(s, false))),   // 내 턴에 연 좌석 2의 판(S39)
            Screen("board-trainer-other", Fit(s => SeatOpened(s, true))),  // 내 턴에 연 좌석 2의 트레이너 카드 판(S40)
            Screen("setup-village-spec", s => VillageSpec(s, false)),   // 고향 마을 고르기의 스타팅 스펙(T3) — 첫 마을을 고른 모습
            Screen("setup-village-watch", s => VillageSpec(s, true)),
            Screen("owner-long-village", s => VillageSpec(s, true, LongNick)),   // 좌석 2 닉네임 12자(방 판): 안내 줄 「○○이 고향 마을을 고르는 중…」이 꺾여도 판 안
            Screen("owner-long-shrine", s => WatchedPick(s, LongNick)),          // 좌석 2 닉네임 12자: 좁은 사당 보기 판의 머리 「○○의 사당」
            Screen("room-peek-village", s => PeekVillage(s)),   // 방 판: 좌석 2가 셋째 마을을 가리킨다 — 보기 화면의 그 줄 · 점에 좌석 색(결정 120 · Protocol 10)
            Battle("room-peek-move", s => PeekMove(s)),         // 방 판: 남의 배틀에서 좌석 2가 마지막 기술을 가리킨다 — 보기 전용 기술 줄에 좌석 색 테두리
            Battle("battle-alpha-sendin", s => AlphaSendIn(s)),   // 우두머리 조우 · 첫 출전을 고르는 동안 상대 기둥에 그 포켓몬(T14 — 룰북 p.16 찾아보기로 공개된 카드)   // 좌석 2가 고르는 동안의 보기 화면(T2) — 둘째 마을을 내 화면에서 훑는 모습                                                          // 남의 조우 고르기 = 조우 창과 같은 카드(T7)
            Screen("board-trade-more", Fit(s => TradeMore(s))),                                      // 거래 창 여행 점수 [+5점]: 상대 14점 가운데 10점을 올렸다
            Battle("battle-trade-pick", s => BattleTradePick(s)),                                    // 남의 배틀을 보는 동안 거래 [수락] 뒤: 배틀 화면 위의 줄 카드 고르기(펄기아를 골랐다)
            Battle("battle-spectate-dock", s => SpectateDock(s)),                                    // 남의 배틀(좌석 2 ↔ 조무래기)을 보는 독: [정비]만 — [가방] · [기권] 없음(M7 · [가정 #400])
        };

        // 좌석 둘 판: 좌석 2가 자기 고향 마을의 조무래기와 싸우고, 좌석 2가 기술을 고를 차례에 내 화면의 독을 본다
        static void SpectateDock(UiShot s)
        {
            var flow = Seats(s, 2, st =>
            {
                var ctx = s.Made.Ctx;
                var two = st.Seats[1];
                two.OpenSlots = two.Party.Length;
                int starter = two.Party[0];
                for (int i = 0; i < two.Party.Length; i++) two.Party[i] = -1;
                foreach (string key in new[] { "483:2", "484:2" }) PokemonRules.AddToParty(two, PokemonRules.Spawn(st, ctx.Cards.PokemonIds.IndexOf(key), 1, (int)DeckId.Legendary));
                if (starter >= 0) PokemonRules.AddToParty(two, starter);
            });
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != 1; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.BoardOpen && f.Hud != null && f.Client.View.ActiveSeat == 1);
            if (!flow.AnswerSeat(1, a => a.Kind == AnswerKind.Challenge && a.Target != 0)) throw new InvalidOperationException("좌석 2의 조무래기 도전이 거절됐다");
            BattleScreen screen = null;
            for (int i = 0; i < 600; i++)
            {
                Pass(flow, 100);
                screen = flow.Stage.Current as BattleScreen;
                // 첫 출전(Pick)은 대신 답하고, 그 밖의 선택(기술 고르기)이 열리면 멈춘다
                if (screen != null && screen.Open && !flow.AnswerSeat(1, a => a.Kind == AnswerKind.Pick)) break;
            }
            if (screen == null || !screen.Open || flow.Battle == null || flow.Battle.Mine) throw new InvalidOperationException("남의 배틀을 보는 배틀 화면이 아니다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            if (root.Q("battle-side") == null || root.Q("battle-maint") == null) throw new InvalidOperationException("관전 독에 [정비]가 없다");
            if (root.Q("battle-bag") != null || root.Q("battle-forfeit") != null) throw new InvalidOperationException("관전 독에 [가방] · [기권]이 있다");
            if (!screen.WatchMoves || root.Q("battle-move-1") == null) throw new InvalidOperationException("남의 배틀의 기술 줄(보기 전용 — T12)이 없다");
        }

        // 좌석 둘 판(남 좌석 = 스탬프 셋)에서 남 좌석의 정비 판을 열고, tip이면 「여행 스탬프 n/7」의 툴팁, 아니면 눌러 그 좌석의 스탬프 판
        static void SeatStamp(UiShot s, bool tip = false)
        {
            var flow = MyTurn(s, 2, st =>
            {
                st.Seats[1].Stamps[StampRules.TrainerWin] = true;
                st.Seats[1].Stamps[StampRules.HeldItems] = true;
                st.Seats[1].Stamps[StampRules.AllApricorns] = true;
            });
            if (!flow.OpenParty(1) || flow.Stage.Root.Q("board-party-stamps") == null) throw new InvalidOperationException("남 좌석 정비 판의 「여행 스탬프 n/7」이 없다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (tip)
            {
                flow.Stage.Tips.Show(flow.Stage.Root.Q("board-party-stamps"));
                return;
            }
            if (!flow.OpenSeatStamp(1) || !(flow.Stage.Current is StampScreen sheet) || sheet.Seat != 1 || sheet.Count != 3) throw new InvalidOperationException("남 좌석의 스탬프 판이 열리지 않았다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            OwnerCheck(flow, flow.Stage.Root.Q(FaceBox.OwnerName), 1, "남 좌석 스탬프 판");
        }

        // 좌석 둘 판의 남 좌석 턴: 그 좌석이 kind(제작 · 공물)를 한 뒤 그 행 단계의 ms — 그 좌석 칩 아래 꼬리표(글이 꼬리표 안에 들고, 사당 판·창은 열리지 않는다)
        // 좌석 둘 판: 좌석 2의 그 행동을 내 화면의 보기 창(GameFlow.Watch — 결정 120 ①)으로 찍는다. 보기 창 · 단추 없음 · 글 넘침을 단정
        static void Watched(UiShot s, AnswerKind kind, int row, int ms, Type screen, bool stew = false)
        {
            var flow = Seats(s, 2, st =>
            {
                for (int seat = 0; seat < 2; seat++)
                {
                    for (int c = 0; c < st.Seats[seat].Apricorns.Length; c++) st.Seats[seat].Apricorns[c] = 3;
                    st.Seats[seat].Jp = 20;
                }
                if (kind == AnswerKind.Offer) SoloTrail.PrepareShrine(s.Made.Ctx, st, 1, SoloTrail.ShrineTop);
            });
            ToSeat2(s, flow);
            var items = flow.Ctx.Items.Items;
            if (!flow.AnswerSeat(1, a => a.Kind == kind && (kind != AnswerKind.Craft || (items[a.Target].Kind == ItemKind.Stew) == stew))) throw new InvalidOperationException("좌석 2의 " + kind + "이 거절됐다");
            ShrineInto(s, row, ms);
            WatchCheck(s, flow, screen);
        }

        // 좌석 둘 판(둘 다 worker 골드 · 여행 점수 20): 그 좌석이 도로를 켜고(UseAbility — 대상 없음) 한 칸 걷는다. 배지 · 칩 · 기록 줄을 단정
        static void Road(UiShot s, bool mine)
        {
            var ctx = s.Board.Ctx;
            int worker = ctx.Cards.TrainerCardIds.IndexOf("worker");
            Action<GameState> prepare = st =>
            {
                for (int seat = 0; seat < 2; seat++)
                {
                    st.Seats[seat].TrainerCard = worker;
                    st.Seats[seat].TrainerCardTier = TrainerRules.Gold;
                    st.Seats[seat].Jp = 20;
                }
            };
            GameFlow flow;
            int who = mine ? 0 : 1;
            if (mine) flow = MyTurn(s, 2, prepare);
            else
            {
                flow = Seats(s, 2, prepare);
                ToSeat2(s, flow);
            }
            bool Do(Func<ChoiceAnswer, bool> pick)
            {
                if (!mine) return flow.AnswerSeat(1, pick);
                var c = flow.Client.Choice(ChoiceKind.MapAction);
                if (c == null) return false;
                foreach (var a in flow.Client.Legal(c))
                    if (pick(a)) return flow.Answer(c, a).Accepted;
                return false;
            }
            if (!Do(a => a.Kind == AnswerKind.UseAbility && a.Target < 0)) throw new InvalidOperationException("도로 켜기(worker B)가 거절됐다");
            DealSettle(s, flow, f => f.Fx.Sequencer.IsEmpty && f.Client.View.Seats[who].Building);
            if (mine && !Do(a => a.Kind == AnswerKind.Move && ctx.Board[a.Target].Biome != Biome.Ocean)) throw new InvalidOperationException("한 칸 걷기가 거절됐다");
            DealSettle(s, flow, f => f.Fx.Sequencer.IsEmpty && (!mine || f.BoardOpen));
            flow.Hud.Refresh();
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            if (!flow.Hud.RoadShown(who)) throw new InvalidOperationException("좌석 칩에 도로 배지가 없다");
            if (flow.Hud.RoadShown(1 - who)) throw new InvalidOperationException("도로를 켜지 않은 좌석에 배지가 섰다");
            if (mine && root.Q(BoardHud.CardState + "-road") == null) throw new InvalidOperationException("내 HUD에 도로 칩이 없다");
            bool logged = false;
            foreach (var l in flow.Log.Lines) if (l.Line != null && l.Line.Key == BoardLog.K + "road" && l.Seat == 0) logged = true;
            if (mine && !logged) throw new InvalidOperationException("도로 기록 줄이 없다");
            var bad = WorkUi.Overflows(root.Q(BoardHud.RoadName(who)));
            if (bad.Count > 0) throw new InvalidOperationException("도로 배지 글이 넘친다: " + string.Join(", ", bad));
            if (!mine) flow.Stage.Tips.Show(root.Q(BoardHud.RoadName(who)));
        }

        // 좌석 둘 판(좌석 2 = psychic 골드 · 규토리 · 여행 점수): 좌석 2의 턴에 염력으로 떨어진 칸의 답 하나를 내고 50행 단계 300ms — 표시가 그 칸에 섰나 단정
        static void Reach(UiShot s, int ms = 300)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 2, st =>
            {
                var two = st.Seats[1];
                two.TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("psychic");
                two.TrainerCardTier = TrainerRules.Gold;
                two.Jp = 20;
                for (int c = 0; c < two.Apricorns.Length; c++) two.Apricorns[c] = 3;
            });
            ToSeat2(s, flow);
            int cell = -1;
            if (!flow.AnswerSeat(1, a => { if (a.Kind != AnswerKind.UseAbility || a.Target < 0) return false; cell = a.Target; return true; })) throw new InvalidOperationException("좌석 2의 염력 답이 없다");
            ShrineInto(s, CardRows.Ability, ms);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (flow.Hud.ReachShown != cell) throw new InvalidOperationException("염력 칸 표시가 " + cell + "이 아니다: " + flow.Hud.ReachShown);
            if (ms > 300)   // 능력 사용의 작은 카드도 남 좌석 것은 얼굴 상자 + 「○○의 능력」(#707)
            {
                if (!flow.Cards.AbilityShown) throw new InvalidOperationException("남 좌석의 능력 사용에 작은 카드가 뜨지 않았다");
                CardOwner(flow, "card-fx-mini-owner", 1);
                Apart(flow.Stage.Root, "card-fx-mini", "board-hud-log");
            }
        }

        // 내 턴에 남 좌석(좌석 2)의 판 · 트레이너 카드 판을 연다 — 머리에 그 좌석 얼굴 상자 + 「○○의 …」(#707)
        static void SeatOpened(UiShot s, bool trainer)
        {
            var flow = MyTurn(s, 2, null);
            if (!(trainer ? flow.OpenTrainer(1) : flow.OpenParty(1))) throw new InvalidOperationException("좌석 2의 " + (trainer ? "트레이너 카드 판" : "판") + "이 열리지 않았다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            OwnerCheck(flow, flow.Stage.Root.Q(FaceBox.OwnerName), 1, flow.Stage.Current.Name);
        }

        // 좌석 둘 판(좌석 2 가방에 구애머리띠): 좌석 2의 턴에 첫 포켓몬에게 지니게 한 정비 단계 600ms — 보기 창은 없고(결정 177) 좌석 2 칩 꼬리표 「정비」 · 기록 줄만 단정
        static void WatchedMaint(UiShot s)
        {
            var ctx = s.Board.Ctx;
            int band = ctx.Items.Ids.IndexOf("item.choice_band");
            var flow = Seats(s, 2, st => st.Seats[1].Bag.Add(band));
            ToSeat2(s, flow);
            int mon = flow.Client.View.Seats[1].Party[0];
            if (!flow.MaintainSeat(1, new Maintenance { Kind = MaintKind.HoldItem, Pokemon = mon, Value = band })) throw new InvalidOperationException("좌석 2의 지니게 하기가 거절됐다");
            ShrineInto(s, WorkRows.Maint, 600);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (flow.Watch != null || flow.WatchShown || flow.Stage.Root.Q("board-watch") != null) throw new InvalidOperationException("남의 정비에 보기 창이 떴다: " + (flow.Watch != null ? flow.Watch.Name : "board-watch"));
            if (flow.Stage.Current != null) throw new InvalidOperationException("남의 정비에 창이 열렸다: " + flow.Stage.Current.Name);
            var tag = flow.Stage.Root.Q<Label>(BoardHud.DidName(1));
            if (tag == null || flow.Hud.DidSeat != 1 || flow.Hud.DidKey != "maint") throw new InvalidOperationException("좌석 2 칩의 「정비」 꼬리표가 없다: " + flow.Hud.DidKey);
            if (tag.text != flow.Stage.T("screen.board.did.maint")) throw new InvalidOperationException("꼬리표 글이 아니다: " + tag.text);
        }

        // 6인 판(좌석 2만 규토리 일곱 색): 좌석 2의 [턴 종료] 끝에 받은 스탬프 단계 600ms — 도장 덮개 · 보기 창 없이 좌석 2 칩 꼬리표 「스탬프 n/7」, 칩 폭 안 · 글 안 잘림 단정
        static void OtherStamp(UiShot s)
        {
            var flow = Seats(s, 6, st =>
            {
                for (int c = 0; c < st.Seats[1].Apricorns.Length; c++) st.Seats[1].Apricorns[c] = Math.Max(1, st.Seats[1].Apricorns[c]);
            });
            for (int k = 0; k < 12 && flow.Client.View.ActiveSeat != 1; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.BoardOpen && f.Hud != null && f.Client.View.ActiveSeat == 1);
            EndActive(flow);
            ShrineInto(s, DealRows.Stamp, 600);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var step = DealRows.Playing(flow.Fx.Sequencer, DealRows.Stamp, out _);
            var e = StampBeats.Event(step);
            if (e == null || e.Seat != 1) throw new InvalidOperationException("좌석 2의 스탬프 단계가 아니다");
            if (flow.Deals != null && flow.Deals.StampShown) throw new InvalidOperationException("남의 스탬프에 도장 덮개가 떴다");
            if (flow.Watch != null || flow.Stage.Current != null) throw new InvalidOperationException("남의 스탬프에 창이 열렸다");
            var tag = flow.Stage.Root.Q<Label>(BoardHud.DidName(1));
            string want = flow.Stage.T("screen.board.did.stamp").Replace("{n}", e.CountAfter.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (tag == null || flow.Hud.DidSeat != 1 || flow.Hud.DidKey != "stamp") throw new InvalidOperationException("좌석 2 칩의 스탬프 꼬리표가 없다: " + flow.Hud.DidKey);
            if (tag.text != want) throw new InvalidOperationException("꼬리표 글이 아니다: " + tag.text + " ≠ " + want);
            var need = tag.MeasureTextSize(tag.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            if (need.x > tag.contentRect.width + 1f) throw new InvalidOperationException("꼬리표 글이 잘린다: 칸 " + tag.contentRect.width + " < 글 " + need.x);
            var chip = flow.Stage.Root.Q("board-hud-seat-2").worldBound;
            if (tag.worldBound.xMax > chip.xMax + 0.5f || tag.worldBound.xMin < chip.xMin - 0.5f) throw new InvalidOperationException("꼬리표가 칩 폭 밖이다: " + tag.worldBound + " · 칩 " + chip);
            UnityEngine.Debug.Log($"[UiShots] board-hud-did-stamp {s.Grade.Height}: 「{tag.text}」 칸 {tag.contentRect.width} · 칩 {chip.width}");
        }

        // 좌석 2가 사당에서 뜻밖의 선물(덱 맨 위)을 골라 규토리를 받은 뒤 380행 600ms — 보기 창 · 제목 · 꼬리표 「축복」 단정
        static void WatchedGift(UiShot s)
        {
            var flow = Seats(s, 2, st =>
            {
                for (int seat = 0; seat < 2; seat++)
                {
                    for (int c = 0; c < st.Seats[seat].Apricorns.Length; c++) st.Seats[seat].Apricorns[c] = 3;
                    st.Seats[seat].Jp = 20;
                }
                SoloTrail.PrepareShrine(s.Made.Ctx, st, 1, SoloTrail.ShrineTop);
            });
            ToSeat2(s, flow);
            if (!flow.AnswerSeat(1, a => a.Kind == AnswerKind.Offer && a.Arg == (int)ApricornColor.Pink && a.Values.Count > 0)) throw new InvalidOperationException("좌석 2의 공물이 거절됐다");
            ShrineSettle(s, flow, f => f.Fx.Sequencer.IsEmpty && PeekRules.Open(f.Client.View, 1, PeekSpot.Shrine) != null);
            var pick = PeekRules.Open(flow.Client.View, 1, PeekSpot.Shrine);
            if (!flow.AnswerSeat(1, a => a.Kind == AnswerKind.Pick && a.Target == pick.Options[0])) throw new InvalidOperationException("좌석 2의 사당 카드 고르기가 거절됐다");
            for (int i = 0; i < 200 && !flow.AnswerSeat(1, a => a.Kind == AnswerKind.Pick && a.Values.Count > 0); i++) Pass(flow, 250);
            ShrineInto(s, WorkRows.Maint, 600);
            WatchCheck(s, flow, typeof(PartyScreen));
            string title = flow.Stage.Root.Q("board-watch").Q<Label>("board-party-title").text;
            if (title != KoJosa.Fix(flow.Stage.T("screen.party.title.gift").Replace("{name}", flow.SeatName(1)))) throw new InvalidOperationException("사당 보상 보기 제목이 아니다: " + title);
        }

        static void ToSeat2(UiShot s, GameFlow flow)
        {
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != 1; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.BoardOpen && f.Hud != null && f.Client.View.ActiveSeat == 1);
        }

        static void WatchCheck(UiShot s, GameFlow flow, Type screen)
        {
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var page = flow.Stage.Root.Q("board-watch");
            if (!flow.WatchShown || flow.Watch == null || flow.Watch.GetType() != screen || flow.Watch.Watched != 1 || page == null)
                throw new InvalidOperationException("좌석 2의 보기 창이 아니다: " + (flow.Watch != null ? flow.Watch.Name + " " + flow.Watch.Watched : "없음"));
            if (flow.Stage.Current != null) throw new InvalidOperationException("보기 창이 무대의 화면을 차지했다: " + flow.Stage.Current.Name);
            if (page.Query<Button>().ToList().Exists(b => b.enabledSelf && b.name != null && !b.name.EndsWith("-close", StringComparison.Ordinal) && !b.name.EndsWith("-fold", StringComparison.Ordinal)))
                throw new InvalidOperationException("보기 창에 누를 단추가 있다");
            var bad = WorkUi.Overflows(page);
            if (bad.Count > 0) throw new InvalidOperationException("보기 창 글이 넘친다: " + string.Join(", ", bad));
            string skip = flow.Stage.T("screen.evolve.skip");   // 남의 연출은 건너뛸 수 없다 — 내 조작 안내 줄이 없어야 한다
            if (page.Query<Label>().ToList().Exists(l => l.resolvedStyle.display != DisplayStyle.None && l.text == skip)) throw new InvalidOperationException("보기 창에 「" + skip + "」 줄이 있다");
            OwnerCheck(flow, page.Q(FaceBox.OwnerName), 1, "보기 창 " + flow.Watch.Name);   // 머리에 그 좌석 얼굴 상자 + 「○○의 …」(#707)
        }

        // 좌석 2가 사당 카드를 고르는 중(공물 3장 — 공개): 연출이 다 돈 뒤의 보기 판
        static void WatchedPick(UiShot s, string nick = null)
        {
            var flow = Seats(s, 2, st =>
            {
                for (int seat = 0; seat < 2; seat++)
                {
                    for (int c = 0; c < st.Seats[seat].Apricorns.Length; c++) st.Seats[seat].Apricorns[c] = 3;
                    st.Seats[seat].Jp = 20;
                }
                SoloTrail.PrepareShrine(s.Made.Ctx, st, 1, SoloTrail.ShrineTop);
            });
            ToSeat2(s, flow);
            if (nick != null) Nick2(flow, nick);
            // 분홍 + 여행 점수 7 = 3장 고르기(사당 길과 같은 공물)
            if (!flow.AnswerSeat(1, a => a.Kind == AnswerKind.Offer && a.Arg == (int)ApricornColor.Pink && a.Values.Count > 0)) throw new InvalidOperationException("좌석 2의 공물이 거절됐다");
            ShrineSettle(s, flow, f => f.Fx.Sequencer.IsEmpty && PeekRules.Open(f.Client.View, 1, PeekSpot.Shrine) != null && f.Watch is ShrineScreen sh && sh.Shown == ShrineScreen.Mode.Cards);
            WatchCheck(s, flow, typeof(ShrineScreen));
            if (flow.Stage.Root.Q("board-watch").Q("board-shrine-watch") == null) throw new InvalidOperationException("「○○이 고르는 중」이 없다");
        }

        // 좌석 2를 조우 토큰 칸에 세우고 찾아보기 — 카드가 다 뒤집힌 보기 창
        static void WatchedEncounter(UiShot s)
        {
            var flow = Seats(s, 2, st => PeekSpotFor(s, st, 1));
            ToSeat2(s, flow);
            if (!flow.AnswerSeat(1, a => a.Kind == AnswerKind.Search)) throw new InvalidOperationException("좌석 2의 찾아보기가 거절됐다");
            ShrineSettle(s, flow, f => f.Fx.Sequencer.IsEmpty && f.Watch is EncounterScreen);
            WatchCheck(s, flow, typeof(EncounterScreen));
            var page = flow.Stage.Root.Q("board-watch");
            if (page.Q("encounter-card-1") == null || page.Q("encounter-pick-1") != null) throw new InvalidOperationException("카드가 없거나 [배틀!]이 있다");
        }

        // 좌석 둘 판을 셋업부터 화면으로: watch면 내가 확정한 뒤 좌석 2의 고르기를 보는 화면, 아니면 내 고르기 — 마을을 하나 골라 스펙을 띄운다
        // 고향 마을 보기 화면(좌석 2가 고르는 중) + 가짜 방이 받은 좌석 2의 가리킴(셋째 마을)
        static void PeekVillage(UiShot s)
        {
            VillageSpec(s, true);
            var flow = s.Made;
            var village = (SetupVillageScreen)flow.Stage.Current;
            var live = FakeGame(flow, true);
            flow.Room.ShowGame(live);
            live.TakePeek(new PeekMsg { From = 1, Spot = PeekSpot.Village, Index = village.Villages[2] });
            Pass(flow, 0);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            village.PlacePins();
            if (village.PeekShown != 2 || !village.Marked(2)) throw new InvalidOperationException("좌석 2가 가리키는 마을이 칠해지지 않았다: " + village.PeekShown);
            if (flow.Stage.Root.Q(className: SetupVillageScreen.PeekClass) == null) throw new InvalidOperationException("가리키는 마을 줄 테두리가 없다");
        }

        // 남의 배틀 관전 독(SpectateDock) + 가짜 방이 받은 좌석 2의 가리킴(마지막 기술)
        static void PeekMove(UiShot s)
        {
            SpectateDock(s);
            var flow = s.Made;
            var screen = (BattleScreen)flow.Stage.Current;
            var moves = screen.WatchedMoves;
            if (moves.Count == 0) throw new InvalidOperationException("보기 전용 기술 줄이 없다");
            var live = FakeGame(flow, true);
            flow.Room.ShowGame(live);
            live.TakePeek(new PeekMsg { From = 1, Spot = PeekSpot.Move, Index = moves[moves.Count - 1] });
            Pass(flow, 0);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (screen.PeekMove != moves[moves.Count - 1] || flow.Stage.Root.Q(className: BattleScreen.PeekClass) == null) throw new InvalidOperationException("좌석 2가 가리키는 기술이 칠해지지 않았다: " + screen.PeekMove);
        }

        static void VillageSpec(UiShot s, bool watch, string nick = null)
        {
            var flow = s.Flow();
            flow.StartSeats(new SoloSettings(), 2, Seed, AiSeed, liveSetup: true);
            if (nick != null) Nick2(flow, nick);
            flow.Layout(s.Width, s.Height);
            for (int guard = 0; !(flow.Stage.Current is SetupVillageScreen); guard++)
            {
                if (guard > 40) throw new InvalidOperationException("고향 마을 고르기가 열리지 않았다");
                Pass(flow, 250);
            }
            var village = (SetupVillageScreen)flow.Stage.Current;
            if (watch)
            {
                village.Pick(0);
                if (!village.Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
                for (int guard = 0; !(flow.Stage.Current is SetupVillageScreen v && v.Watched == 1); guard++)
                {
                    if (guard > 80) throw new InvalidOperationException("좌석 2의 고르기를 보는 화면이 열리지 않았다");
                    Pass(flow, 250);
                }
                village = (SetupVillageScreen)flow.Stage.Current;
            }
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            village.Pick(watch ? 1 : 0);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var bad = WorkUi.Overflows(flow.Stage.Root.Q("setup-village-col"));
            if (bad.Count > 0) throw new InvalidOperationException("고향 마을 판 글이 넘친다: " + string.Join(", ", bad));
            if (flow.Stage.Root.Q("setup-village-starter-1-move") == null) throw new InvalidOperationException("스타팅 스펙이 없다");
            if (watch) OwnerCheck(flow, flow.Stage.Root.Q(FaceBox.OwnerName), 1, "고향 마을 보기 화면");   // 안내 줄 앞 그 좌석 얼굴 상자(#707)
        }

        // 우두머리 숨겨진 장소를 공개 — 파티 둘이라 첫 출전을 묻는 동안의 배틀 화면
        static void AlphaSendIn(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = SpotScene(s, "alpha", 0, st =>
            {
                var me = st.Seats[0];
                me.OpenSlots = me.Party.Length;
                PokemonRules.AddToParty(me, PokemonRules.Spawn(st, ctx.Cards.PokemonIds.IndexOf("421:1"), 1, (int)DeckId.Legendary));
            });
            BattleScreen screen = null;
            for (int i = 0; i < 400; i++)
            {
                Pass(flow, 100);
                screen = flow.Stage.Current as BattleScreen;
                if (screen != null && screen.Open && flow.Client.Choice(ChoiceKind.SendIn) != null) break;
            }
            if (screen == null || flow.Client.Choice(ChoiceKind.SendIn) == null) throw new InvalidOperationException("첫 출전을 묻는 우두머리 배틀 화면이 아니다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (!screen.FoePreviewed || flow.Stage.Root.Q("battle-foe-moves") == null) throw new InvalidOperationException("첫 출전 전에 상대(우두머리)가 기둥에 보이지 않는다");
        }

        static void OtherDid(UiShot s, AnswerKind kind, int row, int ms)
        {
            var flow = Seats(s, 2, st =>
            {
                for (int seat = 0; seat < 2; seat++)
                {
                    for (int c = 0; c < st.Seats[seat].Apricorns.Length; c++) st.Seats[seat].Apricorns[c] = 3;
                    st.Seats[seat].Jp = 20;
                }
                if (kind == AnswerKind.Offer) SoloTrail.PrepareShrine(s.Made.Ctx, st, 1, SoloTrail.ShrineTop);
            });
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != 1; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.BoardOpen && f.Hud != null && f.Client.View.ActiveSeat == 1);
            if (!flow.AnswerSeat(1, a => a.Kind == kind)) throw new InvalidOperationException("좌석 2의 " + kind + "이 거절됐다");
            ShrineInto(s, row, ms);
            var tag = flow.Stage.Root.Q<Label>(BoardHud.DidName(1));
            if (tag == null || flow.Hud.DidSeat != 1) throw new InvalidOperationException("좌석 2 칩의 꼬리표가 없다");
            if (flow.Stage.Current != null) throw new InvalidOperationException("남 좌석의 " + kind + "에 창이 열렸다: " + flow.Stage.Current.Name);   // 보기 창(결정 120 ①)은 무대의 화면이 아니다 — 꼬리표와 함께 뜬다
            var need = tag.MeasureTextSize(tag.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            if (need.x > tag.contentRect.width + 1f) throw new InvalidOperationException("꼬리표 글이 잘린다: 칸 " + tag.contentRect.width + " < 글 " + need.x);
            var chip = flow.Stage.Root.Q("board-hud-seat-2").worldBound;
            if (tag.worldBound.xMax > chip.xMax + 0.5f || tag.worldBound.xMin < chip.xMin - 0.5f) throw new InvalidOperationException("꼬리표가 칩 폭 밖이다: " + tag.worldBound + " · 칩 " + chip);
            var bad = WorkUi.Overflows(flow.Stage.Root.Q("board-hud-seats"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침(좌석 칩) " + bad.Count + "곳: " + string.Join(" / ", bad));
        }

        // 거래 창(좌석 둘 · 같은 칸): 상대의 여행 점수 14 가운데 [+5점] 두 번 = 10점
        static void TradeMore(UiShot s)
        {
            var flow = MyTurn(s, 2, st =>
            {
                TradeKit(s.Made.Ctx, st, false);
                st.Seats[1].Jp = 14;
            });
            if (!flow.Hud.Press(BoardHud.Trade) || !(flow.Stage.Current is TradeScreen trade)) throw new InvalidOperationException("[거래]가 거래 창을 열지 않았다");
            if (!trade.More(1) || !trade.More(1) || trade.Put(1, TradeKind.Jp, -1) != 10) throw new InvalidOperationException("[+5점]이 5점씩 오르지 않았다");
            if (flow.Stage.Root.Q("board-trade-ask-jp-more") == null) throw new InvalidOperationException("[+5점] 단추가 없다");
            s.Panel.Tick(3);
        }

        // 좌석 셋(나 · 좌석 3 = 같은 신의 추종자): 좌석 2의 턴에 좌석 3이 파랑 규토리 1 ↔ 내 운명 카드 1장을 제안하고, 응답이 열린 채 좌석 2가 자기 고향 마을의 조무래기와 싸운다 —
        // 그 배틀을 보는 내 배틀 독의 [수락] → 배틀 화면 위의 줄 카드 고르기(펄기아를 골랐다)
        static void BattleTradePick(UiShot s)
        {
            int palkia = -1;
            var flow = Seats(s, 3, st =>
            {
                var ctx = s.Made.Ctx;
                st.Seats[0].Follower = SinnohGod.Dialga;
                st.Seats[2].Follower = SinnohGod.Dialga;
                st.Seats[2].Apricorns[(int)ApricornColor.Blue] = 2;
                var deck = st.Decks[(int)DeckId.ChangeOfFate];
                palkia = FateRules.CardKey(ctx, FateCard.Palkia);
                foreach (int key in new[] { FateRules.CardKey(ctx, FateCard.Dialga), palkia })
                    if (deck.Remove(key)) st.Seats[0].FateHand.Add(key);
                // 좌석 2: 조무래기의 첫 공격에 쓰러지지 않게
                var two = st.Seats[1];
                two.OpenSlots = two.Party.Length;
                int starter = two.Party[0];
                for (int i = 0; i < two.Party.Length; i++) two.Party[i] = -1;
                foreach (string key in new[] { "483:2", "484:2" }) PokemonRules.AddToParty(two, PokemonRules.Spawn(st, ctx.Cards.PokemonIds.IndexOf(key), 1, (int)DeckId.Legendary));
                if (starter >= 0) PokemonRules.AddToParty(two, starter);
            });
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != 1; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.BoardOpen && f.Hud != null && f.Client.View.ActiveSeat == 1);
            var offer = ChoiceAnswer.Of(AnswerKind.ProposeTrade, flow.Client.Seat);
            offer.Values.AddRange(new[] { 0, (int)TradeKind.Apricorn, (int)ApricornColor.Blue, 1, 1, (int)TradeKind.Fate, -1, 1 });
            if (!flow.ProposeSeat(2, offer)) throw new InvalidOperationException("좌석 3의 거래 제안이 거절됐다");
            Pass(flow, BoardReadyMs * 4);
            if (!flow.AnswerSeat(1, a => a.Kind == AnswerKind.Challenge && a.Target != 0 && a.Target != 2)) throw new InvalidOperationException("좌석 2의 조무래기 도전이 거절됐다");
            BattleScreen screen = null;
            for (int i = 0; i < 600; i++)
            {
                Pass(flow, 100);
                screen = flow.Stage.Current as BattleScreen;
                if (screen != null && screen.Open && screen.AskingRespond) break;
                if (flow.Fx != null && flow.Fx.Sequencer.InputOpen && !flow.AnswerSeat(1, a => a.Kind == AnswerKind.UseMove && a.Arg < 0) && !flow.AnswerSeat(1, a => a.Kind == AnswerKind.Decline)) flow.AnswerSeat(1);
            }
            if (screen == null || !screen.AskingRespond || !screen.PressRespond(true) || !(screen.Over is TradePickScreen cards)) throw new InvalidOperationException("배틀 독의 [수락]이 줄 카드 고르는 창을 열지 않았다");
            if (!cards.Pick(flow.Client.View.Seats[flow.Client.Seat].FateHand.IndexOf(palkia))) throw new InvalidOperationException("펄기아를 고르지 못했다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var bad = WorkUi.Overflows(flow.Stage.Root.Q("board-trade-pick"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침(줄 카드 고르기) " + bad.Count + "곳: " + string.Join(" / ", bad));
        }

        // 글자 자리 단언(보드 위 결과 층): 꼬리표가 판 밖으로 나가거나 글이 칩 밖으로 넘치면 실패
        static Action<UiShot> FitBoard(Action<UiShot> build) => s =>
        {
            build(s);
            s.Panel.Tick(3);
            var flow = s.Made;
            var fx = flow.Results;
            var bad = fx != null ? WorkUi.Overflows(fx.Element) : new List<string>();
            if (fx != null)
                foreach (var name in new[] { "board-result-label", "board-result-banner" })
                {
                    var e = fx.Element.Q(name);
                    if (e == null || e.resolvedStyle.opacity <= 0f) continue;
                    var box = e.worldBound;
                    var root = flow.Stage.Root.worldBound;
                    if (box.xMin < root.xMin || box.xMax > root.xMax || box.yMin < root.yMin || box.yMax > root.yMax) bad.Add(name + ": 화면 밖으로 나간다");
                    e.Query<TextElement>().ForEach(l =>
                    {
                        if (string.IsNullOrEmpty(l.text) || l.resolvedStyle.display == DisplayStyle.None) return;
                        var need = l.MeasureTextSize(l.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                        if (need.x > l.contentRect.width + 1f) bad.Add(name + "「" + l.text + "」: 글이 잘린다");
                    });
                }
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + bad.Count + "곳: " + string.Join(" / ", bad));
        };

        // 글자 자리 단언(배틀 화면의 이 묶음 몫): 기둥의 덧칩 · 무리 줄 · 타입 줄 · 보상 고르기 · 결과 창의 줄
        static Action<UiShot> FitBattle(Action<UiShot> build) => s =>
        {
            build(s);
            s.Panel.Tick(3);
            var root = s.Made.Stage.Root;
            var bad = new List<string>();
            // 톡 커지는 중인 칩·점(scale)은 제 크기로 돌려 놓고 잰 뒤 그 순간의 크기로 되돌린다(늘어난 크기는 배치가 아니다)
            var scaled = new List<KeyValuePair<VisualElement, StyleScale>>();
            foreach (var name in new[] { "battle-me-extra", "battle-foe-extra", "battle-swarm", "battle-swarm-slots" })
            {
                var line = root.Q(name);
                if (line == null) continue;
                foreach (var child in line.Children())
                {
                    scaled.Add(new KeyValuePair<VisualElement, StyleScale>(child, child.style.scale));
                    child.style.scale = StyleKeyword.Null;
                }
            }
            if (scaled.Count > 0) s.Panel.Tick(3);
            foreach (var name in new[] { "battle-me-extra", "battle-foe-extra", "battle-swarm", "battle-swarm-slots", "battle-reward", "battle-result-lines" })
                bad.AddRange(WorkUi.Overflows(root.Q(name)));
            foreach (var kv in scaled) kv.Key.style.scale = kv.Value;
            var screen = s.Made.Stage.Current as BattleScreen;
            if (screen != null && screen.AlphaOpen) bad.AddRange(WorkUi.Overflows(root.Q("battle-moves")));
            // 기둥의 덧줄이 기둥 판 아래로 밀려나지 않았나
            var info = root.Q("battle-info");
            foreach (var name in new[] { "battle-me-extra", "battle-foe-extra", "battle-swarm-slots" })
            {
                var e = root.Q(name);
                if (e != null && info != null && e.worldBound.yMax > info.worldBound.yMax + 1f) bad.Add(name + ": 기둥 아래로 넘친다");
            }
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + bad.Count + "곳: " + string.Join(" / ", bad));
        };

        // ── 보드 ──

        // 결과 kind의 숨겨진 장소를 공개하고 그 결과 단계 안의 ms로 보낸다(extra = 판을 더 손본다)
        static GameFlow SpotScene(UiShot s, string kind, int ms, Action<GameState> extra = null)
        {
            var ctx = s.Board.Ctx;
            bool Want(int def)
            {
                var item = ctx.Items.Items[def];
                switch (kind)
                {
                    case "stash": return item.Key == "hidden_spot.red_apricorn_stash";
                    case "star": return item.Key == HiddenSpotRules.FallenStar;
                    case "relic": return item.Key == "item.gracidea_flower";
                    case "alpha": return item.Kind == ItemKind.AlphaPokemon;
                    default: return item.Key == HiddenSpotRules.Campsite;
                }
            }
            var flow = Seats(s, 1, state =>
            {
                int me = state.Seats[0].Cell;
                var from = MoveRules.Reachable(ctx.Board, me, 400, state.Options.ElevationMode);
                TokenState goal = null, swap = null;
                foreach (var t in state.Tokens)
                    if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0 && from[t.Cell] > 0 && from[t.Cell] != MoveRules.Unreachable && (goal == null || from[t.Cell] < from[goal.Cell])) goal = t;
                if (goal == null) throw new InvalidOperationException("닿을 수 있는 숨겨진 장소가 없다");
                if (!Want(goal.Def))
                {
                    foreach (var t in state.Tokens)
                        if (t != goal && t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && Want(t.Def)) { swap = t; break; }
                    if (swap == null) throw new InvalidOperationException($"뒷면 {kind} 토큰이 없다");
                    (goal.Def, swap.Def) = (swap.Def, goal.Def);
                }
                extra?.Invoke(state);
            });
            WalkToSpot(flow);
            if (!flow.Hud.Press(BoardHud.Reveal)) throw new InvalidOperationException("[공개]가 거절됐다");
            var step = Queued(flow, st => ResultRows.Spot(st.Row));
            if (step == null) throw new InvalidOperationException(kind + ": 결과 단계(26~30행)가 대기열에 없다");
            Laid(s, flow, step, ms);
            var fx = flow.Results;
            var told = ResultRows.EventOf<SpotResolved>(step);
            if ((told.Result == SpotResult.Star || told.Result == SpotResult.Relic) && fx != null && fx.FlyShown > 0 && !fx.TargetFound)
                throw new InvalidOperationException(kind + ": 날아갈 HUD 자리(위 띠 · 내 판)를 읽지 못했다");
            if (fx == null) throw new InvalidOperationException("결과 층이 없다");
            if (ms >= SpotResultBeats.LabelAt(told.Result) + SpotResultBeats.LabelInMs && fx.LabelShown == null) throw new InvalidOperationException(kind + ": 결과 꼬리표가 보이지 않는다");
            return flow;
        }

        // HUD 요소의 자리를 읽는 연출: 그 순간 1ms 앞에서 배치를 세운 뒤 마저 보낸다(RULE-CAPTURE — 답 직후 다시 지은 HUD는 아직 배치가 없다)
        static void Laid(UiShot s, GameFlow flow, FxStep step, int ms)
        {
            Into(s, flow, step, Math.Max(0, ms - 1));
            s.Panel.Tick(3);
            flow.Stage.Tweens.Advance(ms > 0 ? 1 : 0);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
        }

        // 그 좌석에 강한 파티(전설 다섯)를 쥐여 준다 — 스타팅 포켓몬은 맨 뒤
        internal static void GiveTeam(Ctx ctx, GameState state, params string[] lead)
        {
            var st = state.Seats[0];
            st.OpenSlots = st.Party.Length;
            int starter = st.Party[0];
            for (int i = 0; i < st.Party.Length; i++) st.Party[i] = -1;
            foreach (string key in lead.Length > 0 ? lead : SoloTrail.Team)
                PokemonRules.AddToParty(st, PokemonRules.Spawn(state, ctx.Cards.PokemonIds.IndexOf(key), 0, (int)DeckId.Legendary));
            if (starter >= 0 && PokemonRules.PartyCount(st) < st.OpenSlots) PokemonRules.AddToParty(st, starter);
        }

        static int FixtureCell(Ctx ctx, string key)
        {
            int f = ctx.Fixtures.Ids.IndexOf(key);
            if (f < 0) throw new InvalidOperationException("고정물 " + key + "가 없다");
            return ctx.Fixtures.Fixtures[f].Cell;
        }

        // 23행: 비밀키를 가진 좌석이 그랜드 샤토에서 [찾아보기] → 조우 전환 단계의 앞 ms
        static GameFlow LegendSearch(UiShot s, int ms)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 1, state =>
            {
                GiveTeam(ctx, state);
                state.Seats[0].Cell = FixtureCell(ctx, "legendary_site.grand_chateau");
                state.Seats[0].Bag.Add(ctx.Items.Ids.IndexOf("item.secret_key"));
            });
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException("[찾아보기]가 거절됐다(전설 장소 · 비밀키)");
            var step = Queued(flow, st => st.Row == EnterRows.Wipe && ResultRows.EventOf<BattleStarted>(st) != null);
            if (step == null) throw new InvalidOperationException("전설 배틀의 조우 전환 단계가 대기열에 없다");
            Laid(s, flow, step, ms);
            if (flow.Results == null || flow.Results.GlowShown <= 0f && flow.Results.FlyShown == 0) throw new InvalidOperationException("전설 장소의 유물 공명이 보이지 않는다");
            return flow;
        }

        // ── 배틀 ──

        // 내 차례까지 보낸 뒤 until이 참이 될 때까지 첫 기술을 누르며 돈다(알파 스트라이크 줄이면 첫 타입)
        const int NoAlpha = -2;

        static BattleScreen Fight(GameFlow flow, Func<BattleScreen, bool> until, int move = -1)
        {
            for (int i = 0; i < BattleStepMax * 10; i++)
            {
                var screen = flow.Stage.Current as BattleScreen;
                if (screen == null)
                {
                    if (flow.Battle == null) throw new InvalidOperationException("배틀 화면이 열리지 않았다");
                    Pass(flow, BattleStepMs);
                    continue;
                }
                if (until(screen)) return screen;
                if (screen.ShowsResult) throw new InvalidOperationException("그 모습이 되기 전에 배틀이 끝났다(이겼나 " + flow.Battle.Won + ")");
                if (!screen.Open) { Pass(flow, BattleStepMs); continue; }
                if (screen.AskingToken) screen.PressToken(true);
                else if (screen.AskingOrder) screen.PressOrder(0);
                else if (screen.AskingFate) screen.PressFatePass();
                else if (screen.AskingAsk) screen.PressAsk(false);
                else if (screen.AskingPick) { if (!screen.PressPick(-1)) screen.PressPick(0); }
                else if (screen.Bench.Count > 0) { if (!screen.PressStay()) screen.PressBench(0); }
                else if (screen.Moves.Count > 0)
                {
                    // move ≥ 0 = 그 기술(나와 있는 포켓몬이 가졌을 때) · NoAlpha = 알파 스트라이크가 아닌 첫 줄 · 그 밖 = 첫 줄
                    int row = -1;
                    for (int k = 0; k < screen.Moves.Count && row < 0; k++)
                        if (move >= 0 ? screen.Moves[k].Target == move : move != NoAlpha || screen.Moves[k].Target != BattleRules.AlphaStrike) row = k;
                    if (row < 0) row = 0;
                    if (screen.Moves[row].Target == BattleRules.AlphaStrike) { screen.OpenAlpha(); screen.PressAlpha(0); }
                    else screen.PressMove(row);
                }
                else { Pass(flow, BattleStepMs); continue; }
                if (until(screen)) return screen;
                Pass(flow, BattleStepMs);
            }
            throw new InvalidOperationException("그 모습까지 가지 못했다");
        }

        // 대기열에 그 행의 단계가 올 때까지 싸운 뒤 그 단계 안의 ms로
        static GameFlow FightInto(UiShot s, GameFlow flow, Func<FxStep, bool> match, int ms, int move = -1)
        {
            FxStep step = null;
            Fight(flow, screen => (step = Queued(flow, match)) != null, move);
            Laid(s, flow, step, ms < 0 ? step.Ms * -ms / 100 : ms);   // 음수 = 그 단계 길이의 백분율(단계 길이가 판마다 다른 연출의 한가운데). 배치를 세운 뒤 마지막 1ms(기둥의 칩·DS 자리를 읽는 연출)
            return flow;
        }

        // 내가 기술을 고를 차례(입력이 열렸다)
        static bool MyTurn(BattleScreen b) => b.Open && b.Moves.Count > 0 && !b.AskingToken && !b.AskingOrder && !b.AskingFate && !b.AskingAsk && !b.AskingPick && b.Bench.Count == 0;

        // 야생이 나오는 인트로 단계(첫 출전을 고른 뒤에 온다 — 파티가 여럿이면 엔진이 먼저 묻는다)
        static bool WildIntro(FxStep st) => st.Row == FxRows.Intro && st.Events.Exists(e => e is SentIn si && si.Side == 1);

        // 77행: 우두머리 토큰을 공개해 배틀로 — intro면 인트로에서 야생이 선 직후, 아니면 내 차례
        static GameFlow AlphaBattle(UiShot s, bool intro)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 1, state =>
            {
                GiveTeam(ctx, state);
                int me = state.Seats[0].Cell;
                var from = MoveRules.Reachable(ctx.Board, me, 400, state.Options.ElevationMode);
                TokenState goal = null, swap = null;
                foreach (var t in state.Tokens)
                    if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0 && from[t.Cell] > 0 && from[t.Cell] != MoveRules.Unreachable && (goal == null || from[t.Cell] < from[goal.Cell])) goal = t;
                foreach (var t in state.Tokens)
                    if (t != goal && t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && ctx.Items.Items[t.Def].Kind == ItemKind.AlphaPokemon) { swap = t; break; }
                if (goal == null || swap == null) throw new InvalidOperationException("우두머리 토큰을 놓지 못했다");
                if (ctx.Items.Items[goal.Def].Kind != ItemKind.AlphaPokemon) (goal.Def, swap.Def) = (swap.Def, goal.Def);
            });
            WalkToSpot(flow);
            if (!flow.Hud.Press(BoardHud.Reveal)) throw new InvalidOperationException("[공개]가 거절됐다");
            if (intro)
            {
                FightInto(s, flow, WildIntro, EnterBeats.IntroLeadMs + EnterBeats.IntroSlideMs + 450);
                var screen = flow.Stage.Current as BattleScreen;
                if (screen == null || screen.EnemyAura != AuraKind.Alpha) throw new InvalidOperationException("우두머리의 붉은 기운이 찍히지 않았다(" + (screen != null ? screen.EnemyAura.ToString() : "화면 없음") + ")");
                return flow;
            }
            var mine = Fight(flow, MyTurn);
            if (!mine.AlphaChipShown) throw new InvalidOperationException("우두머리 칩이 없다");
            return flow;
        }

        // 24·81행: 깊은늪 게이트에서 [찾아보기] → 무리 배틀. stage 0 명단이 서는 중 · 1 내 차례 · 2 첫 마리를 끝낸 뒤 · 3 결과 창
        static GameFlow SwarmBattle(UiShot s, int stage)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 1, state =>
            {
                GiveTeam(ctx, state);
                state.Seats[0].Cell = FixtureCell(ctx, "gate.deepmarsh_main");
            });
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException("[찾아보기]가 거절됐다(왜곡 게이트)");
            if (stage == 0)
            {
                FightInto(s, flow, WildIntro, SwarmBeats.LineFromMs + SwarmBeats.LineEveryMs + 100);
                return flow;
            }
            var screen = Fight(flow, b => stage == 1 ? MyTurn(b) : stage == 2 ? b.SwarmDone >= 1 && MyTurn(b) : b.ShowsResult);
            if (screen.SwarmCount != 3) throw new InvalidOperationException("무리 명단이 셋이 아니다(" + screen.SwarmCount + ")");
            if (stage == 3 && !flow.Battle.Won) throw new InvalidOperationException("무리 배틀을 이기지 못했다");
            return flow;
        }

        // 조무래기 배틀(고향 마을 [도전])을 연다 — 맨 앞 포켓몬 lead(전설 특수기술 legend), 뒤에 bench
        static GameFlow Grunts(UiShot s, string lead, bool legend, int leadHp, params string[] bench)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 1, state =>
            {
                var keys = new List<string> { lead };
                keys.AddRange(bench);
                GiveTeam(ctx, state, keys.ToArray());
                var first = state.Pokemon[state.Seats[0].Party[0]];
                first.HasLegendaryMove = legend;
                if (leadHp > 0) first.Damage = Math.Max(0, PokemonRules.MaxHp(ctx, first) - leadHp);
            });
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
            return flow;
        }

        // 76행: 전설 특수기술을 가진 포켓몬 — stage 0 기술 줄의 「알파 스트라이크」 · 1 타입 줄 · 2 고른 타입 색 오라 · 3 비스트 부스트(641)
        static GameFlow AlphaStrike(UiShot s, int stage)
        {
            if (stage == 3)
            {
                // 비스트 부스트: 전설 특수기술을 가진 디아루가가 화염방사(배운 기술 하나 — 알파 스트라이크는 덮이고 비스트 부스트는 남는다)로 조무래기를 쓰러뜨린다
                var ctx = s.Board.Ctx;
                int fire = ctx.Cards.MoveIds.IndexOf("flamethrower");
                var boost = Seats(s, 1, state =>
                {
                    GiveTeam(ctx, state, "483:2", "484:2");
                    var first = state.Pokemon[state.Seats[0].Party[0]];
                    first.HasLegendaryMove = true;
                    first.Moves[0] = fire;
                });
                if (!boost.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
                FightInto(s, boost, st => st.Row == 641 && st.Events.Exists(e => e is FatigueChanged f && f.Cause == FatigueCause.BeastBoost), 300, fire);
                var fought = (BattleScreen)boost.Stage.Current;
                if (fought.PlayerAura != AuraKind.BeastBoost) throw new InvalidOperationException("비스트 부스트의 오라가 찍히지 않았다(" + fought.PlayerAura + ")");
                return boost;
            }
            var flow = Grunts(s, "483:2", true, 0);   // 전설 특수기술을 가진 디아루가
            var screen = Fight(flow, MyTurn);
            int row = -1;
            for (int i = 0; i < screen.Moves.Count; i++) if (screen.Moves[i].Target == BattleRules.AlphaStrike) row = i;
            if (row < 0) throw new InvalidOperationException("기술 줄에 알파 스트라이크가 없다");
            if (stage == 0) return flow;
            if (!screen.PressMove(row) || !screen.AlphaOpen || screen.AlphaTypes.Count == 0) throw new InvalidOperationException("타입 줄이 열리지 않았다");
            if (stage == 1) return flow;
            if (!screen.PressAlpha(0)) throw new InvalidOperationException("타입 줄이 거절됐다");
            var used = Queued(flow, st => st.Row == 590 && st.Events.Exists(e => e is MoveUsed m && m.MoveKey == BattleRules.AlphaStrike));
            if (used == null) throw new InvalidOperationException("알파 스트라이크의 기술 단계가 대기열에 없다");
            Into(s, flow, used, 300);
            if (screen.PlayerAura != AuraKind.AlphaStrike) throw new InvalidOperationException("알파 스트라이크의 오라가 찍히지 않았다(" + screen.PlayerAura + ")");
            return flow;
        }

        // ── M5 마감 답 반영 나-1 ──

        // 손본 판(고향 마을): 디아루가(금강옥을 지님 · 손가락흔들기 · 불꽃채찍 · 독 + 화상) · 찌르꼬(애크러뱃) · 로토무(비밀키를 지님) · 스타팅 포켓몬.
        // many = 대기 포켓몬의 기술이 넷 — 두 쪽(찌르꼬 애크러뱃·덤벼들기 · 로토무 덤벼들기·애크러뱃)
        internal static GameFlow LeftBoard(UiShot s, bool many)
        {
            var ctx = s.Board.Ctx;
            return Seats(s, 1, state => LeftTeam(ctx, state, many));
        }

        internal static void LeftTeam(Ctx ctx, GameState state, bool many)
        {
            GiveTeam(ctx, state, "483:1", "396:0", "479:1");
            var st = state.Seats[0];
            int acrobatics = ctx.Cards.MoveIds.IndexOf("acrobatics"), lunge = ctx.Cards.MoveIds.IndexOf("lunge");
            var lead = state.Pokemon[st.Party[0]];
            lead.HeldItem = ctx.Items.Ids.IndexOf("item.adamant_artifact");
            lead.Moves[0] = ctx.Cards.MoveIds.IndexOf("metronome");
            lead.Moves[1] = ctx.Cards.MoveIds.IndexOf("fire_lash");
            lead.Status = StatusEffect.Poisoned;
            lead.Status2 = StatusEffect.Burned;
            state.Pokemon[st.Party[1]].Moves[0] = acrobatics;
            state.Pokemon[st.Party[2]].HeldItem = ctx.Items.Ids.IndexOf("item.secret_key");
            if (!many) return;
            state.Pokemon[st.Party[1]].Moves[1] = lunge;
            state.Pokemon[st.Party[2]].Moves[0] = lunge;
            state.Pokemon[st.Party[2]].Moves[1] = acrobatics;
        }

        // 조무래기에게 도전한 내 차례 — stage 0 차례 칸의 [대기 포켓몬 기술] · 1 그 줄을 연 모습 · 2 누른 뒤(대기 포켓몬이 나와 그 기술을 쓴다) ·
        // 3 손가락흔들기(「○○의 손가락흔들기! / △△이(가) 나왔다!」) · 4 불꽃채찍 뒤의 내 차례(상대 상자에도 상태 둘)
        static GameFlow LeftBattle(UiShot s, int stage, bool many)
        {
            var flow = LeftBoard(s, many);
            var ctx = flow.Ctx;
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
            var screen = Fight(flow, MyTurn);
            if (screen.Painted == null || screen.Painted.Player == null || screen.Painted.Player.Status2 != StatusEffect.Burned) throw new InvalidOperationException("내 체력 상자에 둘째 상태 배지가 없다");
            if (!screen.PartyMovesOn || flow.Stage.Root.Q("battle-partymoves") == null) throw new InvalidOperationException("[대기 포켓몬 기술]이 없다");
            if (stage == 0) return flow;
            if (stage == 3 || stage == 4)
            {
                int move = ctx.Cards.MoveIds.IndexOf(stage == 3 ? "metronome" : "fire_lash"), row = -1;
                for (int i = 0; i < screen.Moves.Count; i++) if (screen.Moves[i].Target == move) row = i;
                if (row < 0 || !screen.PressMove(row)) throw new InvalidOperationException("기술 줄이 거절됐다");
                if (stage == 4)
                {
                    Fight(flow, b => MyTurn(b) && b.Ready);
                    if (screen.Painted.Enemy == null || screen.Painted.Enemy.Status2 == StatusEffect.None) throw new InvalidOperationException("상대 체력 상자에 둘째 상태 배지가 없다");
                    return flow;
                }
                var cast = Queued(flow, st => st.Row == 590 && st.Events.Exists(e => e is MoveUsed m && m.MoveKey == move));
                if (cast == null) throw new InvalidOperationException("손가락흔들기의 기술 단계가 대기열에 없다");
                Into(s, flow, cast, 300);
                if (!flow.Battle.Lines.Exists(l => l.Key == BattleFeed.K + "move.called")) throw new InvalidOperationException("부른 기술 줄이 없다");
                if (screen.Message.IndexOf('\n') < 0) throw new InvalidOperationException("DS 메시지가 두 줄이 아니다(" + screen.Message + ")");
                return flow;
            }
            if (!screen.OpenPartyMoves() || !screen.PartyOpen || screen.PartyMoves.Count != (many ? 4 : 1)) throw new InvalidOperationException("대기 포켓몬의 기술 줄이 열리지 않았다(" + screen.PartyMoves.Count + ")");
            if (many && (screen.PartyPages != 2 || flow.Stage.Root.Q("battle-partymove-next") == null || flow.Stage.Root.Q("battle-partymove-4") != null)) throw new InvalidOperationException("넷이면 두 쪽이다(첫 쪽 셋 + [다음 1/2])");
            s.Panel.Tick(3);
            var bad = WorkUi.Overflows(flow.Stage.Root.Q("battle-dock"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + bad.Count + "곳: " + string.Join(" / ", bad));
            if (stage == 1) return flow;
            if (stage == 5)
            {
                if (!screen.NextPartyMoves() || screen.PartyPage != 1 || flow.Stage.Root.Q("battle-partymove-4") == null) throw new InvalidOperationException("[다음]이 둘째 쪽을 열지 않았다");
                s.Panel.Tick(3);
                bad = WorkUi.Overflows(flow.Stage.Root.Q("battle-dock"));
                if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + bad.Count + "곳: " + string.Join(" / ", bad));
                return flow;
            }
            var answer = screen.PartyMoves[0];
            if (!screen.PressPartyMove(0)) throw new InvalidOperationException("대기 포켓몬의 기술 줄이 거절됐다");
            var used = Queued(flow, st => st.Row == 590 && st.Events.Exists(e => e is MoveUsed m && m.MoveKey == answer.Target && m.PokemonId == answer.Arg));
            if (used == null) throw new InvalidOperationException("대기 포켓몬의 기술 단계가 대기열에 없다");
            Into(s, flow, used, 300);
            return flow;
        }

        // 정비 판 — stage 0 [폼체인지] 줄(디아루가 · 로토무) · 1 로토무의 물음 줄([다른 폼]을 한 번 넘긴 모습) · 2 디아루가를 오리진폼으로 바꾼 뒤
        static GameFlow LeftParty(UiShot s, int stage)
        {
            var flow = LeftBoard(s, false);
            if (!flow.OpenParty()) throw new InvalidOperationException("정비 판이 열리지 않았다");
            var party = (PartyScreen)flow.Stage.Current;
            var root = flow.Stage.Root;
            if (root.Q("board-party-form-0") == null || root.Q("board-party-form-2") == null || root.Q("board-party-form-1") != null) throw new InvalidOperationException("[폼체인지] 줄은 유물을 지닌 디아루가·로토무에만 선다");
            if (stage == 0) return flow;
            if (stage == 1)
            {
                if (!party.PressForm(2) || !party.Asking || root.Q("board-party-ask-next") == null || !party.NextForm()) throw new InvalidOperationException("로토무의 폼체인지 물음이 뜨지 않았다");
                return flow;
            }
            int id = flow.Client.View.Seats[0].Party[0];
            if (!party.PressForm(0) || !party.Confirm()) throw new InvalidOperationException("디아루가의 폼체인지가 거절됐다");
            if (flow.Ctx.Cards.Pokemon[flow.Client.View.Pokemon[id].Def].Key != "483:2") throw new InvalidOperationException("오리진폼이 되지 않았다");
            Pass(flow, 400);
            return flow;
        }

        // 74행: 조무래기를 쓰러뜨려 응징이 쌓이고(741) 다음 공격에 옮겨진다(742)
        static GameFlow Retribution(UiShot s, int row, int ms)
        {
            var flow = Grunts(s, "483:2", false, 0, "484:2", "487:2");
            FightInto(s, flow, st => st.Row == row && (row != ResultRows.Moved || st.Events.Exists(e => e is RetributionMoved m && m.Amount > 0)), ms);
            var screen = (BattleScreen)flow.Stage.Current;
            if (row == ResultRows.Stored && ms >= 400 && screen.RetributionShown <= 0) throw new InvalidOperationException("응징 배지가 없다");
            if ((row == ResultRows.Stored && ms < 300 || row == ResultRows.Moved) && screen.OrbShown <= 0f)
                throw new InvalidOperationException("응징 구슬이 보이지 않는다(" + screen.OrbShown + " · " + screen.OrbAt + " · 단계 " + (flow.Fx.Sequencer.Current != null ? flow.Fx.Sequencer.LocalMs.ToString() : "없음") + ")");
            return flow;
        }

        // 75행: 체력 1인 내 포켓몬이 조무래기에게 쓰러진다 — 갤럭시단은 교체하지 못한다
        static GameFlow Locked(UiShot s)
        {
            var flow = Grunts(s, "129:0", false, 1, "483:2", "484:2");
            FightInto(s, flow, st => st.Row == ResultRows.Lock, 150);
            var screen = (BattleScreen)flow.Stage.Current;
            if (screen.LockShown(1) == null) throw new InvalidOperationException("상대 쪽 교체 불가 칩이 없다");
            // 결정 80 L16 나: DS 상대 체력 상자 옆의 자물쇠 + 그 자리의 마우스 오버 칸
            s.Panel.Tick(3);
            if (screen.Painted == null || screen.Painted.Enemy == null || !screen.Painted.Enemy.Locked) throw new InvalidOperationException("DS 상대 체력 상자 옆에 자물쇠가 찍히지 않았다");
            if (flow.Stage.Root.Q("battle-ds-foe-lock") == null) throw new InvalidOperationException("DS 자물쇠의 마우스 오버 칸이 없다");
            return flow;
        }

        // 결정 80 L16 나: 조무래기를 쓰러뜨려 응징이 쌓인 뒤 다음 포켓몬이 나와 있는 순간 — DS 상대 체력 상자 위의 구슬 + 수
        static GameFlow DsRetribution(UiShot s, bool tip)
        {
            var flow = Grunts(s, "483:2", false, 0, "484:2", "487:2");
            FightInto(s, flow, st => st.Row == ResultRows.Stored, 450);
            var screen = Fight(flow, b => b.Ready && b.RetributionShown > 0 && b.Painted != null && b.Painted.Enemy != null && b.Painted.Enemy.BoxShown && b.Painted.Enemy.Retribution > 0);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var hit = flow.Stage.Root.Q("battle-ds-retribution");
            if (hit == null) throw new InvalidOperationException("DS 응징 구슬의 마우스 오버 칸이 없다");
            var box = flow.Stage.Root.Q("battle-ds").worldBound;
            if (!(hit.worldBound.width > 0f) || !box.Contains(hit.worldBound.min) || !box.Contains(hit.worldBound.max)) throw new InvalidOperationException("DS 응징 구슬의 마우스 오버 칸이 DS 밖이다: " + hit.worldBound + " · DS " + box);
            if (tip) flow.Stage.Tips.Show(hit);
            return flow;
        }

        // 79행: 체리꼬가 시그니처(gather_sunlight)로 폼을 바꾼다
        static GameFlow FormMove(UiShot s, int ms)
        {
            var flow = Grunts(s, "421:1", false, 0, "483:2");
            FightInto(s, flow, st => st.Row == ResultRows.Form, ms);
            var screen = (BattleScreen)flow.Stage.Current;
            if (!screen.FormShown) throw new InvalidOperationException("폼 바꾸기가 그려지지 않았다");
            return flow;
        }

        // 팬 폼(수호신 폼 토대부기 389:2)이 나와 있는 내 차례 — DS의 내 도트 둘레에 금빛 오라
        static GameFlow FanAura(UiShot s)
        {
            var flow = Grunts(s, "389:2", false, 0);
            var screen = Fight(flow, MyTurn);
            if (screen.PlayerAura != AuraKind.Fan) throw new InvalidOperationException("팬 폼 오라가 찍히지 않았다(" + screen.PlayerAura + ")");
            // 결정 80 L15 나: 두꺼운 오라 + 도트의 옅은 색조
            if (screen.Painted.Player.AuraPx < AuraBeats.StayPx || screen.Painted.Player.Tint.a == 0) throw new InvalidOperationException("팬 폼 오라가 얇거나 색조가 없다(" + screen.Painted.Player.AuraPx + "px · 색조 " + screen.Painted.Player.Tint.a + ")");
            return flow;
        }

        // 78·80행: 시작의 사당의 신 토큰(기라티나)과 싸운다 — 780 = 첫 폼이 쓰러진 뒤의 둘째 폼 단계, 800 = 이긴 뒤 결과 창 [확인] 다음의 「한 번 더!」
        static GameFlow GodBattle(UiShot s, int row, int ms)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 1, state =>
            {
                GiveTeam(ctx, state);
                int cell = HosFlow.OriginShrineCell(ctx);
                state.Seats[0].Cell = cell;
                state.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = ctx.Cards.PokemonIds.IndexOf("487:1"), Cell = cell, FaceUp = true });
            });
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException("[찾아보기]가 거절됐다(시작의 사당의 신 토큰)");
            if (row < 0)
            {
                var first = Fight(flow, MyTurn);
                if (first.EnemyAura != AuraKind.God || first.Painted.Enemy.AuraPx < AuraBeats.StayPx) throw new InvalidOperationException("신의 오라가 찍히지 않았다(" + first.EnemyAura + " · " + first.Painted.Enemy.AuraPx + "px)");
                return flow;
            }
            if (row == ResultRows.GodPhase)
            {
                FightInto(s, flow, st => st.Row == ResultRows.GodPhase, ms);
                var screen = (BattleScreen)flow.Stage.Current;
                if (!screen.FormShown) throw new InvalidOperationException("둘째 폼이 그려지지 않았다");
                return flow;
            }
            var result = Fight(flow, b => b.ShowsResult);
            if (!flow.Battle.Won) throw new InvalidOperationException("신을 이기지 못했다");
            for (int i = 0; i < 20 && result.AskingReward; i++)
            {
                // 가르치기 · 놓아주기는 마지막 답(가르치지 않기 · 첫 답)
                var asked = result.Rewards;
                result.PressReward(asked.Count - 1);
                Pass(flow, BattleStepMs);
            }
            for (int i = 0; i < 40 && !flow.BattleOver; i++) Pass(flow, BattleStepMs);
            var step = Queued(flow, st => st.Row == ResultRows.GodGone);
            if (step == null) throw new InvalidOperationException("신 격파 단계(800)가 대기열에 없다");
            if (!result.Confirm()) throw new InvalidOperationException("결과 창 [확인]이 거절됐다");
            Into(s, flow, step, ms);
            if (ms >= GodBeats.BannerAtMs + 100 && (flow.Results == null || !flow.Results.BannerShown)) throw new InvalidOperationException("「한 번 더!」가 보이지 않는다");
            return flow;
        }

        // 46행: 좌석 둘 판 — 좌석 1이 좌석 2(체력 1 · 가방에 비밀키 · 운명 3장)를 트레이너 배틀로 이긴다. pick −1 = 보상 고르기 · 0 = 유물 줄 · 1 = 운명 줄
        static GameFlow Spoils(UiShot s, int pick)
        {
            var flow = s.Flow();
            var ctx = flow.Ctx;
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings(), 2, Seed, AiSeed, state =>
            {
                int home = state.Seats[0].Cell, cell = -1;
                for (int dir = 0; dir < 6 && cell < 0; dir++)
                {
                    int n = ctx.Board.Neighbor(home, dir);
                    if (n >= 0 && !BattleRules.InVillage(ctx, n)) cell = n;
                }
                if (cell < 0) throw new InvalidOperationException("고향 마을 옆에 마을 밖 칸이 없다");
                foreach (var seat in state.Seats) seat.Cell = cell;
                GiveTeam(ctx, state, "483:2");
                var foe = state.Pokemon[state.Seats[1].Party[0]];
                foe.Damage = PokemonRules.MaxHp(ctx, foe) - 1;
                state.Seats[1].Bag.Add(ctx.Items.Ids.IndexOf("item.secret_key"));
                var deck = state.Decks[(int)DeckId.ChangeOfFate];
                for (int i = 0; i < 3 && deck.Count > 0; i++)
                {
                    state.Seats[1].FateHand.Add(deck[deck.Count - 1]);
                    deck.RemoveAt(deck.Count - 1);
                }
            });
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            for (int i = 0; i < BattleStepMax; i++)
            {
                var screen = flow.Stage.Current as BattleScreen;
                if (screen != null && screen.AskingReward && flow.Client.Choice(ChoiceKind.Reward) != null && flow.Client.Choice(ChoiceKind.Reward).Sub == ChoiceSub.TrainerReward && screen.Ready)
                {
                    if (screen.Rewards.Count < 2) throw new InvalidOperationException("보상 답이 둘(유물 · 운명)이 아니다");
                    if (pick < 0) return flow;
                    if (!screen.PressReward(pick == 0 ? 0 : screen.Rewards.Count - 1)) throw new InvalidOperationException("보상 단추가 거절됐다");
                    var step = Queued(flow, st => st.Row == ResultRows.Spoils);
                    if (step == null) throw new InvalidOperationException("승리 보상 단계(461)가 대기열에 없다");
                    Into(s, flow, step, 600);
                    if (flow.Stage.Root.Q("battle-result-spoils") == null) throw new InvalidOperationException("결과 창에 보상 줄이 없다");
                    return flow;
                }
                if (screen != null && screen.ShowsResult && !screen.AskingReward && flow.Client.Choice(ChoiceKind.Reward) == null && flow.BattleOver)
                    throw new InvalidOperationException("보상을 묻기 전에 배틀이 끝났다(이기지 못했나 — " + (flow.Battle != null && flow.Battle.Won) + ")");
                var mine = flow.Client.FirstChoice();
                if (mine != null && mine.Kind == ChoiceKind.MapAction && flow.InputOpen)
                {
                    var challenge = flow.Client.Legal(mine).Find(a => a.Kind == AnswerKind.Challenge && a.Target >= 0);
                    if (challenge == null) throw new InvalidOperationException("좌석 2에게 도전할 수 없다");
                    flow.Answer(mine, challenge);
                }
                else if (screen != null && screen.Open && screen.AskingToken) screen.PressToken(true);
                else if (screen != null && screen.Open && screen.AskingFate) screen.PressFatePass();
                else if (screen != null && screen.Open && screen.AskingAsk) screen.PressAsk(false);
                else if (screen != null && screen.Open && screen.Bench.Count > 0) { if (!screen.PressStay()) screen.PressBench(0); }
                else if (screen != null && screen.Open && screen.Moves.Count > 0) screen.PressMove(0);
                else flow.AnswerSeat(1, a => a.Kind != AnswerKind.Forfeit && a.Kind != AnswerKind.PlayFate);
                Pass(flow, BattleStepMs);
            }
            throw new InvalidOperationException("승리 보상을 묻는 데까지 가지 못했다");
        }

        // 570·641 `Seek`: 그 기술을 배운 디아루가로 조무래기를 치며 그 행의 단계가 올 때까지 — 그 단계 안의 ms
        static GameFlow MoveRow(UiShot s, string moveKey, Func<FxStep, bool> match, int ms)
        {
            var ctx = s.Board.Ctx;
            int move = ctx.Cards.MoveIds.IndexOf(moveKey);
            if (move < 0) throw new InvalidOperationException("기술 " + moveKey + "가 없다");
            var flow = Seats(s, 1, state =>
            {
                GiveTeam(ctx, state, "483:2", "484:2");
                state.Pokemon[state.Seats[0].Party[0]].Moves[0] = move;
            });
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
            FightInto(s, flow, match, ms, move);
            return flow;
        }
    }
}
