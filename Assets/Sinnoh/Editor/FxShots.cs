using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Rekkon.UiKit;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules.Session;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Sinnoh.Editor
{
    // 연출 캡처(M4-3 「연출 층 뼈대」, 04 §8-7): 이벤트 대본 → 화면 대기열 → Seek(ms) → RenderTexture → PNG. 플레이 없이 편집 모드에서 돈다(UiShots와 같은 창 없는 패널).
    //   메뉴 Sinnoh/Capture/Fx(모든 장면),
    //   배치: Unity.exe -batchmode -projectPath . -executeMethod Sinnoh.Editor.FxShots.Run -logFile <log> [-fxScene <이름[,이름…]|all>] [-fxOut <폴더>]
    //   (-nographics 없이. 배치는 끝나면 종료 코드 0/1로 나간다)
    //   움직임 보기: … -fxScene <이름> -fxFrames <간격 ms> [-fxUntil <ms>] → <폴더>/frames-<장면>/f<번호 4자리>.png(장면 하나를 한 번 만들어 그 간격으로 보내며 찍는다 — GIF 재료)
    // 장면: turn-band(혼자 하기 · 주운 것 없음 — 띠 · 컷신) · turn-band-gain(혼자 하기 · 주운 것 있음 — 요약부터) · turn-band-2p · turn-band-720(턴 넘김 견본 Sinnoh.Client.FxTurnSample — 보드 HUD와 같은 부품 TurnBand) ·
    //   battle-attack(배틀 화면 — 실제 판에서 기술을 누른 뒤: 굴림 → 세기 → 「○○의 △△!」 → 맞음 → 계산 줄 → 체력 착지) · battle-flip(졸음·마비의 면 바꾸기) · battle-faint(기절 — 도트가 내려간다) ·
    //   battle-fled(야생이 도망친다 — 옆으로 빠진다) · battle-switch(교체 — 불러들이고 내보낸다) · battle-attack-720(같은 공격을 720 벌로) — 이 넷은 추적하는 게임 데이터(Data/Json)로 실제 게임을 그 장면까지 간 뒤 연출 시각만 보낸다(04 §8-9 「배틀 연출」. 보드 그림은 없다 — 씬을 열지 않는다),
    //   encounter-flip([찾아보기]를 누른 뒤 — 「!」 → 조우 창 → 카드 뒤집기) · battle-enter(카드를 고른 뒤 — 고른 카드 → 줄무늬 전환 → 야생 · 내 포켓몬 등장) ·
    //   p2-flip-strong(+ -720 — 강한 나무: 강한 카드가 늦게 떠올라 느리게 뒤집히고 쿵) · p2-hover(마우스 오버 들림) · p2-sweep(고른 뒤 나머지가 덱으로 쓸려 감 — M9 다듬기 2) ·
    //   capture-success · capture-fail-2 · capture-fail-1 · capture-fail-0 · capture-success-720(볼을 던진 뒤 — 뒷모습 · 볼 · 포획 굴림 · 흔들림 · 판정 · 결말, 성공은 결과 창의 줄까지. 찾아 둔 판 SoloTrail.Catch·Miss2·Miss1·Miss0, 04 §4-10),
    //   setup-village(마을 [확정] 뒤 — 좌석 띠) · setup-card(고르는 화면이 열린 뒤 — 카드 3장이 들어와 뒤집힌다) · setup-starter(고르는 화면의 [확정] 뒤 — 볼로 · 뒷면으로 → 볼이 열림 → 카드 공개) + 각 -720(04 §8-8 1~3행),
    //   token-place · token-glow · token-pass + 각 -720(우선권 토큰 — 인트로 끝에 놓임 · 동률을 묻는 동안 빛남 · [사용] 뒤 돌며 상대 쪽으로, 04 §8-8 53·54행 · 결정 73 ⑦, Defeat 판의 첫 야생 배틀),
    //   sample(연출 층 뼈대 견본) · dice2 · dice4 · dice6 · dice-flip · dice-glitch · dice-all(주사위 굴림 견본 Sinnoh.Client.FxDiceSample — 1080 등급, 굴림판 실제 크기와 3배) · dice4-720(같은 견본을 720 등급으로).
    // 출력 Logs/FxShots/(git 밖 — Temp/는 배치가 끝나면 지워진다): <장면>-<ms 5자리>.png, fx-shots.json(그림마다 시각·색 수·나눠 보낸 것과 다른 픽셀·앞 시각과 다른 픽셀).
    // 판정(로그 [FxShots] 줄): 그림이 비지 않음, 「Layout update failed to stabilize」 로그 0,
    //   그 시각으로 한 번에 보낸 그림 = 16ms씩 나눠 보낸 그림(같은 시각이면 같은 그림 — Seek가 앞 호출·실시각에 기대지 않는다), 앞 시각의 그림과는 다름(움직인다).
    // 장면을 더하려면 Scenes에 이름·찍을 시각·만드는 함수를 한 줄 넣는다. 만드는 함수는 화면 뿌리에 장면을 짓고 「처음부터 ms가 지난 모습으로 보내기」를 돌려준다.
    // 찍은 그림은 직접 열어 본다 — 찍어 본 행만 「된다」고 한다(CLAUDE.md).
    public static class FxShots
    {
        public const string OutDir = "Logs/FxShots";
        /// <summary>나눠 보낼 때의 걸음(60fps 한 프레임 어림).</summary>
        public const int FrameMs = 16;

        /// <summary>연출 캡처는 720 토큰 × 1 한 등급으로 찍는다(겉모양 등급 넷은 UiShots가 본다).</summary>
        public static readonly UiGrade Grade = new UiGrade(720, 1);
        /// <summary>주사위 굴림 견본은 1080 토큰으로 찍는다(주사위 48px — spikes/dice-roll §5의 기준 등급).</summary>
        public static readonly UiGrade DiceGrade = new UiGrade(1080, 1);

        public sealed class Scene
        {
            public string Name;
            public int[] TimesMs;
            public Func<VisualElement, KitSkin, UiGrade, Action<int>> Build;
            /// <summary>찍는 등급(없으면 <see cref="FxShots.Grade"/>).</summary>
            public UiGrade? Grade;
        }

        /// <summary>
        /// 배틀 연출 장면의 찍는 시각(기술을 누른 때부터 ms — 기본 조정 값).
        /// 공격(불꽃숭이의 불꽃세례 → 동미러 · 주사위 2개 · 효과가 굉장함 · 화상): 굴림 0~2510(굴림판이 들어옴 150~350 · 던짐 400 · 멈춤 위쪽 끝 2000 · 세기 2250·2380 —
        /// 결정 143 ⑦ 준비 동작: 0부터 공격한 도트가 당겨 떨고 DS에 「○○의 △△!」) → 맞는 순간 2510~2810(돌진 150 · 제자리) → 상성 2810~4410(깜빡임 600 · 흔들림 360 · 문장 3710) →
        /// 계산 단계 4410~4910(계산 줄 칸은 맞는 순간 2510부터 — 결정 171, 피해 숫자 4660) → 상태 4910~6110 → 체력 6110~6610(착지 6610).
        /// 면 바꾸기·기절(찾아 둔 판 Coral — 내 공격 5910 뒤 야생의 공격): 굴림 5910~ · 넘김 · 세기 · … · 체력 10850~11350 · 기절 11350~(내려감 600 → 「쓰러졌다!」).
        /// 결정 203(계산 단계 1050 → 500 · 체력 800 → 500) 전 시각: 공격 { …, 3900, 4500, 4600, 4800, 5900, 7000, 7500 } · 깎이는 중/착지 7000/7500 · 넘김 { 8800, 9100, 9350, 9600, 9900 } · 기절 { 13300, 13600, 14050 }.
        /// 옛 시각(결정 143 전 — 기술 단계 1400: 당김 · 돌진 · 제자리 + 문장 읽기)은 기술 단계 뒤가 1100씩 늦었다: 공격 { 250, 700, 2100, 2330, 2500, 2700, 4000, 5000, 5600, 5700, 5900, 7000, 8100, 8600 } ·
        /// 깎이는 중/착지 8100/8600 · 넘김 { 9900, 10200, 10450, 10700, 11000 } · 기절 { 15500, 15800, 16250 }(야생의 기술 단계까지 둘 — 2200).
        /// </summary>
        public static readonly int[] AttackShotsMs = { 250, 700, 2100, 2330, 2500, 2600, 2900, 3900, 4700, 5350, 6360, 6700 };
        /// <summary>그 가운데 체력이 깎이는 중(숫자는 아직 앞 값)과 착지한 뒤.</summary>
        public const int AttackDrainMs = 6360, AttackLandedMs = 6700;
        public static readonly int[] FlipShotsMs = { 7950, 8250, 8500, 8750, 9050 };
        public static readonly int[] FaintShotsMs = { 11600, 11900, 12350 };
        /// <summary>도망(67행 — 받을 규토리 색을 고른 뒤가 0): 옆으로 빠지는 중 · 빠진 뒤 「도망쳤다!」. 교체(66행 — 교체 창에서 대기 포켓몬을 누른 뒤가 0): 불러들임 · 절반(1100) 뒤 나옴.</summary>
        public static readonly int[] FledShotsMs = { 150, 400, 700 }, SwitchShotsMs = { 300, 1500 };
        /// <summary>
        /// 들어가고 나오는 연출(04 §8-8 20·21·22·53·68·70행)의 찍는 시각 — 그 답을 누른 뒤의 연출 시각(ms).
        /// 찾아보기: 「!」 · 맥박 → 조우 창(뒷면) → 뒤집히는 중 → 다 뒤집힘. 들어가기: 고른 카드 → 줄무늬 닫힘 → 열림 → 빈 무대 → 야생 등장 → 내 포켓몬.
        /// 포획(성공 3960 · 실패 3340·3100·3100 — 결정 54 ①): 뒷모습 들어옴 → 볼 날아감 → 빨려 듦 → 튕김 → 흔들림 → 판정 → 결말 대사 → (성공) 결과 창의 줄.
        /// </summary>
        public static readonly int[] SearchShotsMs = { 150, 400, 1500, 1850, 2180, 2430, 2700, 3200 };
        public static readonly int[] EnterShotsMs = { 300, 1300, 1500, 1700, 2000, 2500, 3000, 3200, 3700, 4500, 5500 };
        /// <summary>
        /// 강한 나무 찾아보기(M9 다듬기 2 — [찾아보기]를 누른 뒤): 공개 단계 1750 + 강한 카드 600부터 떠오름(300) · 떠오른 채 뒤집힘(2500~2900, 가운데 2700) · 떨어짐(2900~3060) ·
        /// 닿아 눌림(3060) → 편다(3300). 뒷면 → 떠오르며 뒤집히는 중 → 공중 앞면 → 떨어지는 중 → 쿵 눌림 → 되튐 → 착지.
        /// </summary>
        public static readonly int[] StrongShotsMs = { 2300, 2550, 2780, 2980, 3065, 3150, 3400 };
        /// <summary>
        /// 배틀 시작의 첫 출전(53행 — 결정 99 ②, 인트로 단계 안 시각): 뒷모습 들어옴 → 섬 → 던지기 → 손을 떠남(<see cref="IntroReleaseMs"/>) → 볼 날아감 → 열림(1750) → 포켓몬 커짐 · 뒷모습 물러남 → 체력 상자.
        /// 던지기(팔 듦 — 손을 떠나기까지 <see cref="EnterBeats.ReleaseMs"/>로 늘인 프레임) · 손을 떠남 · 날아감은 결정 126 ③(던지기 1.5배 — ReleaseMs 260 → 390 · IntroFlyMs 240 → 360)을 따라
        /// 열리는 때에서 거꾸로 센다(옛 1300 · 1400 · 1510 · 1600 · 1700 — 던지기 시작 1250).
        /// </summary>
        public static readonly int[] IntroThrowShotsMs = { 350, 500, 800, IntroThrowFromMs + EnterBeats.ReleaseMs / 5, IntroThrowFromMs + EnterBeats.ReleaseMs * 3 / 5, IntroReleaseMs,
            IntroReleaseMs + EnterBeats.IntroFlyMs * 3 / 8, IntroReleaseMs + EnterBeats.IntroFlyMs * 4 / 5, EnterBeats.IntroSecondMs + 10, EnterBeats.IntroSecondMs + 100, EnterBeats.IntroSecondMs + 210, 2300 };
        const int IntroThrowFromMs = EnterBeats.IntroSecondMs - IntroStage.ThrowLeadMs, IntroReleaseMs = EnterBeats.IntroSecondMs - EnterBeats.IntroFlyMs;

        /// <summary>
        /// 포획(68행 — 볼을 던진 단계 시작이 0)의 찍는 시각은 박자에서 센다(결정 126 ③ — 던지기 1.5배로 옛 시각이 머무는 그림에 떨어졌다, M10 감사 ②):
        /// 뒷모습 들어옴 0~<see cref="EnterBeats.TrainerInMs"/> · 볼 날아감 <see cref="EnterBeats.ReleaseMs"/>~<see cref="EnterBeats.FlyEndMs"/> · 빨려 듦 ~<see cref="EnterBeats.SuckEndMs"/> ·
        /// 떨어져 두 번 튐 ~<see cref="EnterBeats.BounceEndMs"/> · 흔들림 <see cref="CaptureShakeStartMs"/>~판정(<c>PlayTiming.CaptureVerdictMs</c> — 세기는 합계가 판정 100 전에 서도록 거꾸로) ·
        /// 결말 대사 · 단계 끝(<c>PlayTiming.CaptureMs</c>) 뒤 69 · 70행. 옛 시각(결정 126 전 — 판정 3060 · 끝 3960): 성공 { 120, 300, 450, 800, 950, 1250, 1500, 1800, 2500, 3000, 3100, 3450, 3900, 4600, 5400, 5800, 6500, 9300 } ·
        /// 실패 2 { 300, 800, 1300, 1900, 2500, 2640, 2700, 2900, 3250, 3600 } · 실패 1 { 300, 1300, 1800, 2100, 2300, 2400, 2500, 2700, 3050, 3400 } · 실패 0 { …, 1800, 2200, … }.
        /// </summary>
        public static readonly int[] ThrowShotsMs = { EnterBeats.ReleaseMs / 4, EnterBeats.ReleaseMs / 2, EnterBeats.ReleaseMs, EnterBeats.ReleaseMs + 70, EnterBeats.ReleaseMs + 190, EnterBeats.SuckEndMs + 250 };
        public static readonly int[] CatchShotsMs =
        {
            EnterBeats.TrainerInMs * 2 / 3, EnterBeats.ReleaseMs + 60, CapFlyMs, CapSuckMs, EnterBeats.SuckEndMs - 75, CapFallMs, CapBounce2Ms,
            CaptureShakeStartMs(3) + 150, CaptureShakeStartMs(3) + 850, CaptureVerdictMs(3) - 60, CaptureVerdictMs(3) + 40, CaptureVerdictMs(3) + 390,
            CaptureEndMs(true, 3) - 60, CaptureEndMs(true, 3) + 640, CaptureEndMs(true, 3) + 1440, CaptureEndMs(true, 3) + 1840, CaptureEndMs(true, 3) + 2540, CaptureEndMs(true, 3) + 5340,
        };
        public static readonly int[] Miss2ShotsMs =
        {
            EnterBeats.ReleaseMs + 60, CapSuckMs, CapBounce1Ms, CaptureShakeStartMs(2) + 250, CaptureVerdictMs(2) - 90,
            CaptureVerdictMs(2) + 50, CaptureVerdictMs(2) + 110, CaptureVerdictMs(2) + 310, CaptureEndMs(false, 2) - 90, CaptureEndMs(false, 2) + 260,
        };
        public static readonly int[] Miss1ShotsMs =
        {
            EnterBeats.ReleaseMs + 60, CapBounce1Ms, CaptureShakeStartMs(1) - 80, CaptureShakeStartMs(1) + 220, CaptureVerdictMs(1) - 50,
            CaptureVerdictMs(1) + 50, CaptureVerdictMs(1) + 150, CaptureVerdictMs(1) + 350, CaptureEndMs(false, 1) - 50, CaptureEndMs(false, 1) + 300,
        };
        public static readonly int[] Miss0ShotsMs =
        {
            EnterBeats.ReleaseMs + 60, CapBounce1Ms, CapBounce2Ms, Math.Max(EnterBeats.BounceEndMs, CaptureVerdictMs(0) - 150), CaptureVerdictMs(0) - 50,
            CaptureVerdictMs(0) + 50, CaptureVerdictMs(0) + 150, CaptureVerdictMs(0) + 350, CaptureEndMs(false, 0) - 50, CaptureEndMs(false, 0) + 300,
        };
        /// <summary>포획 연출 720 벌: 볼이 날아가는 중 · 판정(별) · 결과 창의 줄.</summary>
        public static readonly int[] Catch720ShotsMs = { EnterBeats.ReleaseMs + 60, CaptureVerdictMs(3) + 40, CaptureEndMs(true, 3) + 1840 };

        /// <summary>포획 박자의 닻(단계 안 시각): 판정 · 흔들림 시작 · 단계 끝.</summary>
        public static int CaptureVerdictMs(int shakes) => PlayTiming.CaptureVerdictMs(shakes);
        public static int CaptureShakeStartMs(int shakes) => CaptureVerdictMs(shakes) - PlayTiming.CaptureShakeMs.Value * shakes;
        public static int CaptureEndMs(bool success, int shakes) => PlayTiming.CaptureMs(success, shakes);
        // 볼이 날아가는 가운데 · 빨려 드는 가운데 · 떨어지는 중(떨어짐 = 튐 구간의 절반) · 첫 튐의 처음 · 둘째 튐의 가운데(CaptureStage.Pose — 절반 · 3할 · 2할)
        const int CapBounceSpan = EnterBeats.BounceEndMs - EnterBeats.SuckEndMs;
        const int CapFlyMs = (EnterBeats.ReleaseMs + EnterBeats.FlyEndMs) / 2, CapSuckMs = (EnterBeats.FlyEndMs + EnterBeats.SuckEndMs) / 2;
        const int CapFallMs = EnterBeats.SuckEndMs + CapBounceSpan * 9 / 20, CapBounce1Ms = EnterBeats.SuckEndMs + CapBounceSpan / 2 + CapBounceSpan * 3 / 50, CapBounce2Ms = EnterBeats.BounceEndMs - CapBounceSpan / 10;

        /// <summary>
        /// 남은 M4 연출 행(실제 진행 판 Defeat — plan §4 「남은 M4 연출 행」)의 찍는 시각.
        /// [전체 회복](17행 — 마을 창에서 누른 뒤가 0, 2800): 파티 칸 반짝임(한 마리 — 0~320) → 머묾 → 징글 끝음 2800에 체력 착지.
        /// 귀환(71행 — 결과 창 [확인] 뒤가 0): 역전환 0~300과 함께 어두워짐 0~700 → 다 어두움 · 1000에 말이 고향에 → 걷힘 1300~2000 → 넘김 띠 2000~2500 · 내 턴 컷신 ~3000 → 귀환 회복(8행) 3000~5800.
        /// AI 자막(73행 — 그 단계 시작이 0, 1000): 들어옴 0~150 → 머묾 → 걷힘 850~1000 → 조무래기의 굴림.
        /// </summary>
        public static readonly int[] HealShotsMs = { 100, 1500, 2900 };
        public static readonly int[] WhiteoutShotsMs = { 200, 500, 1100, 1700, 2100, 3100, 5900 };
        public static readonly int[] AiLineShotsMs = { 60, 200, 900, 1100 };

        /// <summary>
        /// Seek 확인(plan §3 M4-3 조건 ③)에서 장면이 없던 배틀 행 — 그 단계의 시작이 0(실제 진행 판 Defeat · 볼을 쥐여 준 판 Catch·Miss0).
        /// 선턴(54행 1200): 먼저 움직이는 쪽 체력 상자가 1px 내려갔다 올라옴(150마다 — 0~600) → 머묾. 차례 시작의 상태 이상(63행 900). 전투 피로 피해(64행의 피해 쪽 900 — 파티를 피로 상태로 손본 Defeat 판 <see cref="Tired"/>).
        /// 갤럭시단 등장(72행 3900 — [도전]의 전환 뒤): 조무래기가 미끄러져 들어옴 → 섬 → 명단 볼이 떨어짐 → 끝까지 서 있음(결정 143 ⑤ — 첫 출전을 고르는 동안 물러나지 않는다.
        /// 옛 { 100, 600, 3600, 3850 }의 3600 · 3850은 물러나는 중이었는데 이제 같은 그림 — 물러남은 상대 첫 출전 인트로의 처음 intro-foe-ball). 막 목표(94행 3000 — 조무래기를 이긴 결과 창의 줄).
        /// </summary>
        public static readonly int[] OrderShotsMs = { 100, 200, 400, 700 };
        public static readonly int[] TickShotsMs = { 100, 500, 1000 };
        public static readonly int[] FatigueShotsMs = { 100, 400, 800, 1000 };
        public static readonly int[] GalacticShotsMs = { 100, 600, LineupBeats.LeadMs + LineupBeats.DropMs / 2, 3850 };
        public static readonly int[] GoalShotsMs = { 300, 1500, 3100 };

        /// <summary>라인업 공개(72행 — 등장 단계 시작이 0): 트레이너가 섰다 600 · 볼 둘 1000 · 넷째 볼이 떨어지는 중 1350. 2막 결과 창(94행 940이 0): 줄 없음 300 · 조각 800 · 파티 칸 1300 · 2막 목표 1800.</summary>
        public static readonly int[] LineupShotsMs = { 600, 1000, 1350 };
        public static readonly int[] ShardShotsMs = { 300, 800, 1300, 1800 };

        // 태홍의 명단 공개(72행 — 막 길 Heart)
        // 갤럭시단 포켓몬이 나오는 인트로 단계(53행 — M9 다듬기 P05의 상대 트레이너 볼)
        static bool GalacticIntro(FxStep step) => step.Row == FxRows.Intro && step.Events.Any(e => e is Sinnoh.Rules.SentIn si && si.Seat == Sinnoh.Rules.SeatId.Galactic);

        static bool CyrusLineup(FxStep step) => step.Row == EnterRows.Galactic && LineupBeats.Lineup(step) is Sinnoh.Rules.GalacticBattle g && g.Leader == Sinnoh.Rules.GalacticLeader.Cyrus;

        /// <summary>
        /// 우선권 토큰(결정 73 ⑦, 04 §8-8 53·54행 — Defeat 판의 첫 야생 배틀은 스피드 동률이다): 놓기 = 인트로 단계 시작이 0(놓이기 전 2500 · 떨어지는 중 2700 · 닿음 2800 · 튀어 오름 2850 · 제자리 3100),
        /// 넘기기 = [사용]을 누른 뒤 선턴 단계 시작이 0(도는 중 50~450 · 상대 상자 옆에 닿고 선턴 상자 흔들림 650), 빛남 = 묻는 동안의 늘 도는 시각(200마다 빛·반짝이 자리가 바뀐다).
        /// </summary>
        public static readonly int[] TokenPlaceShotsMs = { 2500, 2700, 2800, 2850, 3100 };
        public static readonly int[] TokenPassShotsMs = { 50, 150, 300, 450, 650 };
        public static readonly int[] TokenGlowShotsMs = { 0, 200, 400, 600 };

        /// <summary>
        /// 도트 이펙트(결정 69, pixelfx — <c>SheetBeats</c>)의 찍는 시각: 이펙트가 시작한 바로 뒤의 프레임들.
        /// 기술 맞음(battle-attack의 체력 6110~ — 프레임 60ms × 4) · 기절(내 포켓몬 = Coral 11350~ — 결정 203 전에는 6660~ · 13350~, 결정 143 ⑦ 전에는 7760~ · 15550~ — 도트가 내려가는 600의 끝 150 전부터 먼지 45ms × 6, 메시지 창 바로 위로 올려 찍는다 · 상대 = Defeat의 첫 기절 단계 시작이 0 — 450부터 발판 위) ·
        /// 출전(교체 창에서 누른 뒤 — 나오는 때부터) ·
        /// 포획(볼이 열리는 <see cref="EnterBeats.FlyEndMs"/>부터 빛 · 성공 판정 뒤에는 원래 별 셋만(결정 70 ⑬ ④) / 실패 1번 흔들림 판정부터 다시 빛 — 결정 126 ③ 전에는 600 · 3060 · 2350) ·
        /// 회복(마을 [전체 회복] 뒤 — 칸 반짝임과 함께 70ms × 7) · 스타팅(고르는 화면 [확정] 뒤 — 공개 2700 + 660에 볼이 열린다).
        /// </summary>
        // 결정 143 ⑦: 기술 단계가 1400 → 300이라 맞음 불꽃은 1100, 야생의 공격 뒤 기절 먼지는 2200 앞당겨졌다(옛 { 7780, 7850, 7910, 7960 } · { 15900, 16020, 16110, 16200 })
        public static readonly int[] FxHitShotsMs = { 6130, 6200, 6260, 6310 };
        public static readonly int[] FxFaintShotsMs = { 12000, 12120, 12210, 12300 };
        public static readonly int[] FxFaintFoeShotsMs = { 300, 480, 560, 650 };
        public static readonly int[] FxCatchShotsMs = { EnterBeats.FlyEndMs + 10, EnterBeats.FlyEndMs + 70, EnterBeats.FlyEndMs + 180, EnterBeats.FlyEndMs + 300, CaptureVerdictMs(3) + 80 };
        public static readonly int[] FxMissShotsMs = { EnterBeats.FlyEndMs + 10, CaptureVerdictMs(1) + 10, CaptureVerdictMs(1) + 70, CaptureVerdictMs(1) + 170, CaptureVerdictMs(1) + 270 };
        public static readonly int[] FxHealShotsMs = { 40, 180, 330, 460 };
        public static readonly int[] FxStarterShotsMs = { 3370, 3420, 3480, 3580, 3680 };

        /// <summary>
        /// 막 카드(04 §8-8 4행 — 셋업을 끝낸 뒤가 0): 서막 카드 0~3600(어두워짐 0~300 · 떨어짐 300~750 · 글자 810·880 · 흔들림 1020~1280 · 목표 문장 1150~ · 착지 3200 · 걷힘 ~3600) →
        /// 조무래기 놓기 3600~5400(보드 — 이 캡처에는 보이지 않는다) → 1막 카드 5400~9000(보상 줄 포함).
        /// </summary>
        public static readonly int[] ActShotsMs = { 150, 450, 600, 760, 830, 900, 1000, 1150, 1600, 3300, 3500, 6300, 7200, 8850 };

        /// <summary>
        /// 셋업 연출(04 §8-8 1~3행 — 기본 값)의 찍는 시각.
        /// 고향 마을(마을 [확정] 뒤가 0 — 1800): 좌석 띠가 들어옴 0~150 → 회색으로 머묾 → 말이 내려앉는 1116에 마을 색으로 칠해지며 한 번 눌렸다 펴짐(~1296).
        /// 카드 받기(고르는 화면이 열린 때가 0 — 800): 뒷면 3장이 60씩 어긋나 들어옴(각 400) → 닿은 카드부터 앞면으로 뒤집힘(각 240) → 800 뒤 클릭 막기 300.
        /// 고른 뒤(고르는 화면의 [확정] 뒤가 0): 스타팅 포켓몬 고른 뒤 0~1200(물러남 ~420 · 볼로 들어감 360~840 · 흔들림 ~1200) → 트레이너 카드 고른 뒤 1500~2700(뒷면으로 1860~2340) →
        /// 스타팅 포켓몬 공개 2700~4900(흔들림 ~3030 · 튐 ~3360 · 열림 3360 · 도트 ~3624 · 머묾 · 볼로 4416~4592 · 떨어짐 ~4900) →
        /// 트레이너 카드 공개 4900~7400(들림 ~5100 · 뒤집힘 ~5500 · 금속광 ~6000 · 머묾 · 내려앉음 6900~7400) → 화면이 닫히고 보드 HUD.
        /// 결정 104 ③ 뒤: 내가 고른 트레이너 카드는 앞면 그대로라(뒷면으로 · 다시 뒤집힘이 없다) 2150~2700 · 5250~5500은 머무는 그림 — 그 사이 시각(2500 · 5450)은 찍지 않는다.
        /// </summary>
        public static readonly int[] VillageShotsMs = { 60, 400, 1150, 1500 };
        public static readonly int[] DealShotsMs = { 100, 300, 520, 700, 1200 };
        public static readonly int[] PickedShotsMs = { 200, 600, 900, 1150, 1900, 2150, 2900, 3250, 3400, 3550, 4000, 4500, 4750, 5000, 5250, 5700, 6500, 7100, 7300 };

        public static readonly Scene[] Scenes = BuildScenes();

        static Scene[] BuildScenes()
        {
            var scenes = new List<Scene> { new Scene { Name = "sample", TimesMs = FxSample.ShotsMs, Build = (root, skin, grade) => new FxSample(root, skin, grade).Seek } };
            foreach (var v in FxDiceSample.Variants)
            {
                var variant = v;
                scenes.Add(new Scene { Name = v.Name, TimesMs = v.ShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => Dice(root, skin, grade, variant).Seek });
            }
            // 720 토큰(격자 412×124 · 1px 도트 · 주사위 32px)도 한 장면
            scenes.Add(new Scene { Name = "dice4-720", TimesMs = new[] { 700, 2100 }, Build = (root, skin, grade) => Dice(root, skin, grade, FxDiceSample.Find("dice4")).Seek });
            // 턴 넘김(04 §8-8 101행, 결정 57): 요약 → 넘김 띠 → 내 턴 컷신. 혼자 하기(띠 = 라운드 — 주운 것이 없으면 요약 없이 1초, 있으면 요약부터)와 두 좌석(띠 = 「A ▶ B」, 2초), 1080 등급(큰 글자 48·72px)
            scenes.Add(new Scene { Name = "turn-band", TimesMs = FxTurnSample.SoloShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => new FxTurnSample(root, skin, grade, UiShots.Strings(), false, false).Seek });
            scenes.Add(new Scene { Name = "turn-band-gain", TimesMs = FxTurnSample.SoloGainShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => new FxTurnSample(root, skin, grade, UiShots.Strings(), false).Seek });
            scenes.Add(new Scene { Name = "turn-band-2p", TimesMs = FxTurnSample.ShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => new FxTurnSample(root, skin, grade, UiShots.Strings(), true).Seek });
            scenes.Add(new Scene { Name = "turn-band-720", TimesMs = new[] { 500, 1100, 1700 }, Build = (root, skin, grade) => new FxTurnSample(root, skin, grade, UiShots.Strings(), true).Seek });
            // 막 카드(04 §8-8 4행, S14): 실제 판의 셋업을 끝낸 뒤 — 서막 카드 → (조무래기 놓기) → 1막 카드
            scenes.Add(new Scene { Name = "act-card", TimesMs = ActShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => Opening(root, skin, grade) });
            scenes.Add(new Scene { Name = "act-card-720", TimesMs = new[] { 900, 1600, 7200 }, Build = (root, skin, grade) => Opening(root, skin, grade) });
            // 셋업 연출(04 §8-8 1~3행, 결정 66 ③ — 보드 그림은 없다: 말이 떨어지는 것은 UiShots setup-village-*가 찍는다)
            scenes.Add(new Scene { Name = "setup-village", TimesMs = VillageShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => SetupScene(root, skin, grade, 0) });
            scenes.Add(new Scene { Name = "setup-card", TimesMs = DealShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => SetupScene(root, skin, grade, 1) });
            scenes.Add(new Scene { Name = "setup-starter", TimesMs = PickedShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => SetupScene(root, skin, grade, 2) });
            scenes.Add(new Scene { Name = "setup-village-720", TimesMs = new[] { 400, 1300 }, Build = (root, skin, grade) => SetupScene(root, skin, grade, 0) });
            scenes.Add(new Scene { Name = "setup-card-720", TimesMs = new[] { 300, 520, 1200 }, Build = (root, skin, grade) => SetupScene(root, skin, grade, 1) });
            scenes.Add(new Scene { Name = "setup-starter-720", TimesMs = new[] { 900, 2400, 3550, 4000, 5700, 7100 }, Build = (root, skin, grade) => SetupScene(root, skin, grade, 2) });
            // 배틀 연출(04 §8-8 56·57·59·60·61·62·65행): 실제 판에서 기술을 누른 뒤의 화면 대기열을 그 시각으로 보낸다
            scenes.Add(new Scene { Name = "battle-attack", TimesMs = AttackShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => BattleAttack(root, skin, grade) });
            scenes.Add(new Scene { Name = "battle-attack-720", TimesMs = new[] { 1200, 4700, 6700 }, Build = (root, skin, grade) => BattleAttack(root, skin, grade) });
            scenes.Add(new Scene { Name = "battle-flip", TimesMs = FlipShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => BattleTrail(root, skin, grade, SoloTrail.Coral) });
            scenes.Add(new Scene { Name = "battle-faint", TimesMs = FaintShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => BattleTrail(root, skin, grade, SoloTrail.Coral) });
            // 도망(67행): 같은 판에서 내 포켓몬이 쓰러진 뒤 받을 규토리 색을 고르면 야생이 옆으로 빠진다. 교체(66행): 볼을 쥐여 준 판의 조무래기전 교체 창에서 대기 포켓몬을 누른다
            scenes.Add(new Scene
            {
                Name = "battle-fled", TimesMs = FledShotsMs, Grade = DiceGrade,
                Build = (root, skin, grade) => BattleTrail(root, skin, grade, SoloTrail.Coral, b => b.AskingReward && b.Ready),
            });
            scenes.Add(new Scene
            {
                Name = "battle-switch", TimesMs = SwitchShotsMs, Grade = DiceGrade,
                Build = (root, skin, grade) => BattleTrail(root, skin, grade, SoloTrail.Catch, b => b.Open && b.CanStay, b => b.PressBench(0)),
            });
            // 들어가고 나오는 연출(04 §8-8 20·21·22·53·68·70행): 찾아 둔 판을 화면의 함수로 그 답까지 간 뒤 연출 시각만 보낸다
            scenes.Add(new Scene { Name = "encounter-flip", TimesMs = SearchShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Catch, f => f.InputOpen && f.Hud != null && f.Hud.Shows(BoardHud.Search)) });
            scenes.Add(new Scene { Name = "battle-enter", TimesMs = EnterShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Catch, f => f.Stage.Current is EncounterScreen e && e.Open) });
            // M9 다듬기 2(21행 — 가정 #415~#417): 강한 나무(1장)의 늦게·무겁게 · 마우스 오버 들림 · 고른 뒤 덱으로 쓸려 감
            scenes.Add(new Scene { Name = "p2-flip-strong", TimesMs = StrongShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.One, SearchOn) });
            scenes.Add(new Scene { Name = "p2-flip-strong-720", TimesMs = new[] { 2550, 3065, 3400 }, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.One, SearchOn) });
            scenes.Add(new Scene { Name = "p2-hover", TimesMs = new[] { 0, 40, 80, 130 }, Grade = DiceGrade, Build = (root, skin, grade) => EncounterHover(root, skin, grade, 1) });
            scenes.Add(new Scene { Name = "p2-sweep", TimesMs = new[] { 50, 120, 250, 400, 560 }, Grade = DiceGrade, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Catch, EncounterOpen) });
            scenes.Add(new Scene { Name = "capture-success", TimesMs = CatchShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Catch, CanThrow) });
            scenes.Add(new Scene { Name = "capture-fail-2", TimesMs = Miss2ShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Miss2, CanThrow) });
            scenes.Add(new Scene { Name = "capture-fail-1", TimesMs = Miss1ShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Miss1, CanThrow) });
            scenes.Add(new Scene { Name = "capture-fail-0", TimesMs = Miss0ShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Miss0, CanThrow) });
            scenes.Add(new Scene { Name = "capture-success-720", TimesMs = Catch720ShotsMs, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Catch, CanThrow) });
            // 남은 M4 연출 행(04 §8-8 8·17·71·73행): 실제 진행 판(Defeat)을 화면의 함수로 그 모습까지 간 뒤 연출 시각만
            scenes.Add(new Scene { Name = "board-heal", TimesMs = HealShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => DefeatTrail(root, skin, grade, HealNext, true) });
            scenes.Add(new Scene { Name = "board-heal-720", TimesMs = new[] { 100, 2900 }, Build = (root, skin, grade) => DefeatTrail(root, skin, grade, HealNext, true) });
            scenes.Add(new Scene { Name = "board-whiteout", TimesMs = WhiteoutShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => DefeatTrail(root, skin, grade, WhiteoutResult, true) });
            scenes.Add(new Scene { Name = "board-whiteout-720", TimesMs = new[] { 1100, 3100 }, Build = (root, skin, grade) => DefeatTrail(root, skin, grade, WhiteoutResult, true) });
            scenes.Add(new Scene { Name = "battle-ai", TimesMs = AiLineShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => AiLineScene(root, skin, grade) });
            scenes.Add(new Scene { Name = "battle-ai-720", TimesMs = new[] { 200, 900 }, Build = (root, skin, grade) => AiLineScene(root, skin, grade) });
            // Seek 확인에서 장면이 없던 배틀 행(04 §8-8 54·63·64·72·94행): 찾아 둔 판에서 그 행의 단계가 화면 대기열에 들어오면 그 시작까지 건너뛰고 연출 시각만
            scenes.Add(new Scene { Name = "battle-order", TimesMs = OrderShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, OrderStep) });
            scenes.Add(new Scene { Name = "battle-order-720", TimesMs = new[] { 100, 700 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, OrderStep) });
            scenes.Add(new Scene { Name = "battle-tick", TimesMs = TickShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, st => st.Row == 630) });
            scenes.Add(new Scene { Name = "battle-fatigue", TimesMs = FatigueShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, st => st.Row == 642, prepare: Tired) });
            scenes.Add(new Scene { Name = "battle-galactic", TimesMs = GalacticShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, st => st.Row == EnterRows.Galactic) });
            scenes.Add(new Scene { Name = "battle-galactic-720", TimesMs = new[] { 100, 600 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, st => st.Row == EnterRows.Galactic) });
            // 배틀 시작의 첫 출전(결정 99 ② — 원작 Pt: 주인공 뒷모습이 서 있다가 볼을 던져 첫 포켓몬을 내보낸다): 야생 배틀(Catch 판) · 갤럭시단 배틀(Defeat 판은 첫 배틀이 야생이라 Coral 판의 조무래기전)
            scenes.Add(new Scene { Name = "intro-throw", TimesMs = IntroThrowShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Catch, st => st.Row == FxRows.Intro) });
            scenes.Add(new Scene { Name = "intro-throw-720", TimesMs = new[] { 800, IntroThrowShotsMs[6], IntroThrowShotsMs[8], IntroThrowShotsMs[9] }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Catch, st => st.Row == FxRows.Intro) });
            // 여자 조무래기(결정 70 ⑩ — 남·여는 조무래기가 선 칸으로 고른다: Defeat 판 = 남, Coral 판 = 여 — EnterStagingTests가 지킨다)
            // 상대 트레이너의 볼(53행 — M9 다듬기 P05): 갤럭시단이 물러난 뒤 인트로에서 볼이 화면 밖에서 날아와 열린다
            scenes.Add(new Scene { Name = "intro-foe-ball", TimesMs = new[] { 40, 150, 280, 320, 400, 520, 900 }, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, GalacticIntro) });
            scenes.Add(new Scene { Name = "intro-foe-ball-720", TimesMs = new[] { 40, 150, 280, 320, 400, 520, 900 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, GalacticIntro) });
            scenes.Add(new Scene { Name = "battle-galactic-f", TimesMs = new[] { 100, 600 }, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Coral, st => st.Row == EnterRows.Galactic) });
            scenes.Add(new Scene { Name = "battle-galactic-f-720", TimesMs = new[] { 100, 600 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Coral, st => st.Row == EnterRows.Galactic) });
            scenes.Add(new Scene { Name = "battle-goal", TimesMs = GoalShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Catch, st => st.Row == 940) });
            // 막·종막 화면(M5 — 72·94행): 라인업 볼(간부 4 · 태홍 6)이 하나씩 놓임 · 2막 결과 창의 조각 · 파티 칸 · 2막 목표 줄 — 막 길(SoloTrail.Lake·Heart)
            scenes.Add(new Scene { Name = "row-72-lineup", TimesMs = LineupShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Lake, st => st.Row == EnterRows.Galactic) });
            scenes.Add(new Scene { Name = "row-72-lineup-720", TimesMs = new[] { 600, 1350 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Lake, st => st.Row == EnterRows.Galactic) });
            scenes.Add(new Scene { Name = "row-72-cyrus", TimesMs = new[] { 1000, 1900 }, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Heart, CyrusLineup) });
            scenes.Add(new Scene { Name = "row-72-cyrus-720", TimesMs = new[] { 1000, 1900 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Heart, CyrusLineup) });
            // 운명의 변화(04 §8-8 42~45행 — M5 「운명·응답 화면」): 운명 길 SoloTrail.Fate·FateGiratina의 그 단계 시작이 0ms. 7행(떨어진 별)은 배틀 결과 창 [확인] 뒤에 재생된다 — RowScene의 afterBattle. 14행(순간이동)은 보드(3D 말·칸 고리) 쪽 연출이라 이 캡처(PC 층·DS만 그린다)에는 그릴 것이 없다 — UiShots row-14-*(보드를 함께 찍는다)
            foreach (var (name, trail, row, times) in new (string, SoloTrail, int, int[])[]
            {
                ("row-42-tear", SoloTrail.Fate, FateRows.Discard, new[] { 100, 300, 500 }),
                ("row-45-pass", SoloTrail.Fate, FateRows.Play, new[] { 90, 300, 520 }),
                ("row-45-pass3", SoloTrail.Fate, FateRows.Play, new[] { 300, 700, 1100 }),
                ("row-43-reroll", SoloTrail.Fate, FateRows.Reroll, new[] { 500, 1300, 2250, 3125 }),   // 결정 190: 옛 200 · 520 · 900 · 1250의 2.5배
                ("row-44-sacrifice", SoloTrail.FateGiratina, FateRows.Sacrifice, new[] { 300, 800, 1300 }),
                ("row-07-star", SoloTrail.Fate, FateRows.Star, new[] { 150, 300, 520, 700 }),   // 날아옴(0~450) · 뒤집힘(~600) · 장수가 톡(~780)
            })
            {
                bool three = name == "row-45-pass3";
                Func<FxStep, bool> match = st => st.Row == row && (row != FateRows.Play || FateRows.First<Sinnoh.Rules.FatePlayed>(st) != null && FateRows.First<Sinnoh.Rules.FatePlayed>(st).Play3 == three);
                scenes.Add(new Scene { Name = name, TimesMs = times, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, trail, match, row == FateRows.Star) });
                scenes.Add(new Scene { Name = name + "-720", TimesMs = times, Build = (root, skin, grade) => RowScene(root, skin, grade, trail, match, row == FateRows.Star) });
            }
            // P39 공간 일그러짐(M9 다듬기 8 — 마감 감사 A7이 웹에서 못 본 순간): 기라티나 운명 카드의 그림자(44행)가 덮는 동안 DS 화면이 줄마다 밀린다 — 시각마다 그려진 진폭 = 박자(RippleBeats.Ds)
            scenes.Add(new Scene { Name = "p39-shadow", TimesMs = P39ShadowShotsMs, Build = (root, skin, grade) => WarpSeen(RowScene(root, skin, grade, SoloTrail.FateGiratina, st => st.Row == FateRows.Sacrifice), P39ShadowShotsMs) });
            // 사당·균열(04 §8-8 31·32·34행 — M5 「사당·균열 화면」): 사당 길 SoloTrail.Shrine의 사당 판이 그리는 단계(그 단계 시작이 0ms). 보드 쪽(33행 전설 · 34행 강림)은 UiShots row-33-*·row-34-*
            foreach (var (name, row, times) in new (string, int, int[])[]
            {
                ("row-31-offer", ShrineRows.Offer, new[] { 350, 800, 1500, 1800, 2300 }),
                ("row-32-bless", ShrineRows.Blessing, new[] { 150, 400, 900 }),
                ("row-34-tally", ShrineRows.Tally, new[] { 150, 400, 900, 1350 }),
            })
            {
                scenes.Add(new Scene { Name = name, TimesMs = times, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Shrine, st => st.Row == row) });
                scenes.Add(new Scene { Name = name + "-720", TimesMs = times, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Shrine, st => st.Row == row) });
            }
            // 여행 스탬프(04 §8-8 47행 — M5 「스탬프·도전·거래 화면」): 볼을 쥐여 준 판(Catch)의 첫 스탬프 단계(그 단계 시작이 0ms) — 배틀 화면 위의 덮개(무대 맨 위 층).
            // 도전 VS(51행)·거래(52행)는 좌석 여럿 판이라 UiShots row-51-*·row-52-*
            scenes.Add(new Scene { Name = "row-47-stamp", TimesMs = new[] { 150, 300, 600, 1000, 1300, 1800 }, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Catch, st => st.Row == DealRows.Stamp) });
            // 트레이너 카드 해금(04 §8-8 48행 — M6 「트레이너 카드 해금 연출」, 결정 84 M5 가): 찾아 둔 판 SoloTrail.Two(적당한 나무)에 전설 다섯을 쥐여 이기게 한 판 — 배틀 안의 해금은 결과 창 [확인] 뒤 보드에서(afterBattle)
            scenes.Add(new Scene { Name = "row-48-unlock", TimesMs = new[] { 100, 500, 1000, 1450, 1600, 1750, 2050, 2600 }, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Two, st => st.Row == UnlockBeats.Row, afterBattle: true, prepare: Strong) });
            scenes.Add(new Scene { Name = "row-48-unlock-720", TimesMs = new[] { 100, 500, 1000, 1450, 1600, 1750, 2050, 2600 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Two, st => st.Row == UnlockBeats.Row, afterBattle: true, prepare: Strong) });
            scenes.Add(new Scene { Name = "row-47-stamp-720", TimesMs = new[] { 150, 300, 600, 1000, 1300, 1800 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Catch, st => st.Row == DealRows.Stamp) });
            scenes.Add(new Scene { Name = "row-94-shard", TimesMs = ShardShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Lake, st => st.Row == 940) });
            scenes.Add(new Scene { Name = "row-94-shard-720", TimesMs = new[] { 300, 1800 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Lake, st => st.Row == 940) });
            // 우선권 토큰(결정 73 ⑦): 놓기(53행) · 묻는 동안 빛남 · 쓰면 돌며 상대 쪽으로(54행) — 1080과 720
            scenes.Add(new Scene { Name = "token-place", TimesMs = TokenPlaceShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, TokenPlaced) });
            scenes.Add(new Scene { Name = "token-place-720", TimesMs = new[] { 2700, 3100 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, TokenPlaced) });
            scenes.Add(new Scene { Name = "token-glow", TimesMs = TokenGlowShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => TokenGlow(root, skin, grade) });
            scenes.Add(new Scene { Name = "token-glow-720", TimesMs = new[] { 0, 400 }, Build = (root, skin, grade) => TokenGlow(root, skin, grade) });
            scenes.Add(new Scene { Name = "token-pass", TimesMs = TokenPassShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, TokenPassed) });
            scenes.Add(new Scene { Name = "token-pass-720", TimesMs = new[] { 150, 650 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, TokenPassed) });
            // 도트 이펙트(결정 69, pixelfx): 자리마다 이펙트가 도는 순간 — 1080과 720
            scenes.Add(new Scene { Name = "pixelfx-hit", TimesMs = FxHitShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => BattleAttack(root, skin, grade) });
            scenes.Add(new Scene { Name = "pixelfx-hit-720", TimesMs = new[] { 6130, 6200 }, Build = (root, skin, grade) => BattleAttack(root, skin, grade) });
            scenes.Add(new Scene { Name = "pixelfx-faint", TimesMs = FxFaintShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => BattleTrail(root, skin, grade, SoloTrail.Coral) });
            scenes.Add(new Scene { Name = "pixelfx-faint-720", TimesMs = new[] { 12120, 12210 }, Build = (root, skin, grade) => BattleTrail(root, skin, grade, SoloTrail.Coral) });
            scenes.Add(new Scene { Name = "pixelfx-faint-foe", TimesMs = FxFaintFoeShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, st => st.Row == 650) });
            scenes.Add(new Scene { Name = "pixelfx-faint-foe-720", TimesMs = new[] { 480, 560 }, Build = (root, skin, grade) => RowScene(root, skin, grade, SoloTrail.Defeat, st => st.Row == 650) });
            scenes.Add(new Scene { Name = "pixelfx-catch", TimesMs = FxCatchShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Catch, CanThrow) });
            scenes.Add(new Scene { Name = "pixelfx-catch-720", TimesMs = new[] { FxCatchShotsMs[1], FxCatchShotsMs[4] }, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Catch, CanThrow) });
            scenes.Add(new Scene { Name = "pixelfx-miss", TimesMs = FxMissShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Miss1, CanThrow) });
            scenes.Add(new Scene { Name = "pixelfx-heal", TimesMs = FxHealShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => DefeatTrail(root, skin, grade, HealNext, true) });
            scenes.Add(new Scene { Name = "pixelfx-heal-720", TimesMs = new[] { 40, 180 }, Build = (root, skin, grade) => DefeatTrail(root, skin, grade, HealNext, true) });
            scenes.Add(new Scene { Name = "pixelfx-starter", TimesMs = FxStarterShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => SetupScene(root, skin, grade, 2) });
            scenes.Add(new Scene { Name = "pixelfx-starter-720", TimesMs = new[] { 3420, 3480 }, Build = (root, skin, grade) => SetupScene(root, skin, grade, 2) });
            // M5 「제작·은행·진화 화면」(04 §8-8 18·36·37·39행): 손본 판(SoloTrail.Work)의 첫 턴에 창에서 누른 뒤가 0ms — 그 창이 연출을 그린다(보드 그림은 없다)
            scenes.Add(new Scene { Name = "craft-make", TimesMs = CraftShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => WorkScene(root, skin, grade, f => Make(f, "item.choice_band")) });
            scenes.Add(new Scene { Name = "craft-make-720", TimesMs = new[] { CraftShotsMs[2], CraftShotsMs[4] }, Build = (root, skin, grade) => WorkScene(root, skin, grade, f => Make(f, "item.choice_band")) });
            scenes.Add(new Scene { Name = "craft-stew", TimesMs = StewShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => WorkScene(root, skin, grade, f => Make(f, Sinnoh.Rules.CraftRules.ZestyStew)) });
            scenes.Add(new Scene { Name = "bank-trade", TimesMs = BankShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => WorkScene(root, skin, grade, Trade) });
            scenes.Add(new Scene { Name = "bank-trade-720", TimesMs = new[] { 1200, 2420 }, Build = (root, skin, grade) => WorkScene(root, skin, grade, Trade) });
            scenes.Add(new Scene { Name = "evolve", TimesMs = EvolveShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => WorkScene(root, skin, grade, Evolve) });
            scenes.Add(new Scene { Name = "evolve-720", TimesMs = new[] { 2200, 4000 }, Build = (root, skin, grade) => WorkScene(root, skin, grade, Evolve) });
            // 고른 주인공의 뒷모습 던지기(결정 53 ④·55 ①·60): 같은 판을 주인공만 바꿔 — 던지기 전 · 팔을 든 때 · 손을 떠나는 260 · 그 뒤 · 다 던진 뒤. 장면 이름 = capture-<주인공 키>
            foreach (string key in Protagonists.Keys)
            {
                string who = key;
                scenes.Add(new Scene { Name = "capture-" + who, TimesMs = ThrowShotsMs, Grade = DiceGrade, Build = (root, skin, grade) => FlowTrail(root, skin, grade, SoloTrail.Catch, CanThrow, who) });
            }
            return scenes.ToArray();
        }

        /// <summary>포획 장면(시험·캡처가 함께 쓴다): 찾아 둔 판의 야생 배틀에서 첫 볼을 던진 뒤. shakes = 그 볼의 흔들림 수(3 = 성공 · 2·1·0 = 실패). protagonist = 고른 주인공(없으면 기본).</summary>
        public static Action<int> CaptureScene(VisualElement root, KitSkin skin, UiGrade grade, int shakes, string protagonist = null) =>
            FlowTrail(root, skin, grade, shakes >= 3 ? SoloTrail.Catch : shakes == 2 ? SoloTrail.Miss2 : shakes == 1 ? SoloTrail.Miss1 : SoloTrail.Miss0, CanThrow, protagonist);

        // ── M5 「제작·은행·진화 화면」 ──

        /// <summary>
        /// 찍는 시각(누른 뒤가 0ms). 제작(36행 — 결단의 머리띠, 재료 여섯, 1000 — T11): 빛남 100 · 빨려 듦 200·340 · 펑 480 · 커짐 570 · 「+1」 떠오름 710 · 끝 뒤 1100.
        /// 스튜(37행 — 자극적인 스튜, 제작 1000 뒤): 첫 튐 1100 · 둘째 튐 1300 · 꼬리표 1600·2400.
        /// 은행상회(18행): 빛남 150 · 낸 것 450 · 받은 것 1200·1700 · 순증감 1950 · 뒤집기 2300(옛 카드)·2420(새 카드).
        /// 진화(39행 — 4800, 결정 84 L11 실루엣 8번): 「…오잉!?」 200 · 하얘짐 800 · 실루엣 1400(전)·2000(뒤)·2200(전)·2400(뒤)·2550(전)·2700(뒤)·2800(전)·2950(뒤) · 흰 빛 3200·3400 · 걷힘 4000 · 진화 뒤 4700.
        /// </summary>
        // 제작 · 스튜는 박자에서 센다(플레이 시험 T11 — 제작 1500 → 1000이라 옛 { 100, 300, 500, 780, 900, 1100, 1450 } · { 1600, 1800, 2100, 2900 }의 끝이 머무는 그림에 떨어졌다, M10 감사 ②):
        // 빛 차오름 100 · 첫 재료 빨려 듦 · 마지막(여섯째) 재료 빨려 듦 · 펑 · 커졌다 돌아오는 중 · 「+1」 떠오름 · 빛 걷힘 · 단계 끝 뒤(개수 ×1) / 스튜 단계(제작 끝부터 1500): 첫 튐 · 둘째 튐 · 꼬리표가 톡 뜨는 중 · 다 뜬 꼬리표
        public static int[] CraftShotsMs => new[]
        {
            100, CraftBeats.SuckFromMs + 100, CraftBeats.SuckFromMs + CraftBeats.SuckEveryMs * 5 + CraftBeats.SuckMs / 2, CraftBeats.FlashAt + 20, CraftBeats.FlashAt + CraftBeats.PopMs / 2,
            CraftBeats.FlashAt + CraftBeats.FlashMs + 100, PlayTiming.CraftMs + 100,
        };   // 속성: 장면 표(Scenes)가 위에서 먼저 만들어진다
        public static int[] StewShotsMs => new[] { PlayTiming.CraftMs + 100, PlayTiming.CraftMs + StewBeats.FirstMs + StewBeats.SecondMs / 5, PlayTiming.CraftMs + StewBeats.BadgeAt + 120, PlayTiming.CraftMs + 1400 };
        public static int[] BankShotsMs => new[] { 150, 450, 1200, 1700, 1950, 2300, 2420 };
        public static int[] EvolveShotsMs => new[] { 200, 800, 1400, 2000, 2200, 2400, 2550, 2700, 2800, 2950, 3200, 3400, 4000, 4700 };   // 4800 — 실루엣 8번(결정 84 L11): 오가는 때 1914·2160·2349·2508·2648·2775·2891 사이마다 한 장

        /// <summary>손본 판(<see cref="SoloTrail.Work"/>)의 첫 턴(입력이 열린 고향 마을)까지 간 뒤 act(창을 열고 답 하나)를 누르고 「그 뒤 ms가 지난 모습으로 보내기」를 돌려준다.</summary>
        public static Action<int> WorkScene(VisualElement root, KitSkin skin, UiGrade grade, Func<GameFlow, bool> act)
        {
            var flow = Flow(root, skin, grade);
            var pilot = FlowPilot.Start(flow, SoloTrail.Work);
            if (!FlowPilot.Run(flow, pilot, () => flow.InputOpen && flow.Hud != null && flow.Hud.Shows(BoardHud.Craft), ms => UiShots.Pass(flow, ms), UiShots.BattleStepMs, UiShots.BattleStepMax * 10))
                throw new InvalidOperationException("work: 첫 턴까지 가지 못했다");
            if (!act(flow)) throw new InvalidOperationException("work: 창의 답이 거절됐다");
            return SeekFrom(flow);
        }

        static bool Make(GameFlow f, string item) => f.Hud.Press(BoardHud.Craft) && f.Stage.Current is CraftScreen c && c.PressMake(c.ItemOf(item));

        static bool Trade(GameFlow f) => f.Hud.Press(BoardHud.Village) && f.Stage.Current is VillageScreen v && v.Bank != null
            && v.Bank.Press(v.Bank.CanTrade(Sinnoh.Rules.BankRules.Top) ? Sinnoh.Rules.BankRules.Top : Sinnoh.Rules.BankRules.Bottom);

        static bool Evolve(GameFlow f) => f.Hud.Press(BoardHud.Evolve) && f.Stage.Current is EvolveScreen e && e.PressEvolve(e.Listed[1]);   // 스타팅 포켓몬(파티 둘째)

        // ── 남은 M4 연출 행 ──

        /// <summary>실제 진행 판(Defeat)을 길잡이가 화면의 함수로 지나 <paramref name="until"/>의 모습까지 간 뒤, press면 길잡이의 다음 걸음 하나(마을 [전체 회복] · 결과 창 [확인])를 누르고 연출 시각만 보낸다.</summary>
        static Action<int> DefeatTrail(VisualElement root, KitSkin skin, UiGrade grade, Func<GameFlow, SoloPilot, bool> until, bool press)
        {
            var flow = Flow(root, skin, grade);
            var pilot = FlowPilot.Start(flow, SoloTrail.Defeat);
            if (!FlowPilot.Run(flow, pilot, () => until(flow, pilot), ms => UiShots.Pass(flow, ms), UiShots.BattleStepMs, UiShots.BattleStepMax * 10)) throw new InvalidOperationException("defeat: 그 장면까지 가지 못했다");
            if (press && !FlowPilot.Step(flow, pilot, true)) throw new InvalidOperationException("defeat: 다음 걸음이 거절됐다");
            return SeekFrom(flow);
        }

        // 길잡이의 다음 답이 [전체 회복]이다(야생 배틀을 치른 뒤 고향에서 — 다친 포켓몬이 있다)
        internal static bool HealNext(GameFlow flow, SoloPilot pilot)
        {
            var client = flow.Client;
            var choice = client != null ? client.FirstChoice() : null;
            if (!flow.InputOpen || choice == null || choice.Kind != Sinnoh.Rules.ChoiceKind.MapAction) return false;
            var answer = pilot.Decide(flow.Ctx, client.View, choice, client.Legal(choice));
            return answer != null && answer.Kind == Sinnoh.Rules.AnswerKind.VillageHeal;
        }

        // 조무래기에게 져서 귀환하는 배틀의 결과 창이 [확인]을 기다린다
        internal static bool WhiteoutResult(GameFlow flow, SoloPilot pilot) =>
            flow.Stage.Current is BattleScreen b && b.ShowsResult && !b.AskingReward && flow.Battle != null && flow.Battle.Whiteout;

        /// <summary>AI 자막 장면: 조무래기의 기술 선택(사유가 있는 AI 답)이 화면 대기열에 들어오면 그 단계 시작까지 건너뛰고 연출 시각만 보낸다.</summary>
        static Action<int> AiLineScene(VisualElement root, KitSkin skin, UiGrade grade)
        {
            var flow = Flow(root, skin, grade);
            var pilot = FlowPilot.Start(flow, SoloTrail.Defeat);
            // 세션 시각을 먼저 보내고, AI 답이 들어왔으면 연출 시각은 보내지 않는다(그 단계를 지나쳐 버리지 않게)
            void Pass(int ms)
            {
                flow.Tick(ms);
                if (!AiQueued(flow)) flow.Stage.Tweens.Advance(ms);
            }
            if (!FlowPilot.Run(flow, pilot, () => AiQueued(flow), Pass, UiShots.BattleStepMs, UiShots.BattleStepMax * 10)) throw new InvalidOperationException("defeat: AI 자막까지 가지 못했다");
            flow.Skip(flow.MsUntil(st => st.Row == AiLineBeats.Row && AiLineBeats.Answer(st) != null));
            return SeekFrom(flow);
        }

        /// <summary>
        /// 전투 피로 피해(64행의 피해 쪽 642)의 손본 판: 셋업 뒤 좌석 0의 파티를 모두 피로 상태(<see cref="Sinnoh.Rules.FatigueRules.Max"/>)로 — 첫 배틀의 첫 굴림이 피로 피해를 낸다
        /// (실제 진행으로는 다섯 번 연속 굴린 긴 배틀에만 나와 찾아 둔 판이 규칙 변경마다 낡았다). 피로는 나갈 때만 0이 된다.
        /// </summary>
        static void Strong(Sinnoh.Rules.GameState s) => UiShots.GiveTeam(LoadCtx(), s);   // 전설 다섯을 쥐여 준다(해금 48행 — 적당한 야생을 이기게)

        static void Tired(Sinnoh.Rules.GameState s)
        {
            foreach (int id in s.Seats[0].Party)
                if (id >= 0) s.Pokemon[id].Fatigue = Sinnoh.Rules.FatigueRules.Max;
        }

        /// <summary>
        /// 행 장면: 찾아 둔 판을 길잡이가 화면의 함수로 지나다가 <paramref name="match"/>인 단계가 화면 대기열에 들어오면 그 단계 시작까지 건너뛰고 연출 시각만 보낸다(그 단계의 시작이 0ms).
        /// <paramref name="prepare"/>가 있으면 셋업 뒤 그 판을 손봐 다시 연다(<see cref="GameFlow.Rebase"/>).
        /// </summary>
        public static Action<int> RowScene(VisualElement root, KitSkin skin, UiGrade grade, SoloTrail trail, Func<FxStep, bool> match, bool afterBattle = false, Action<Sinnoh.Rules.GameState> prepare = null)
        {
            var flow = Flow(root, skin, grade);
            var pilot = FlowPilot.Start(flow, trail);
            if (prepare != null) flow.Rebase(prepare);
            // 세션 시각을 먼저 보내고, 그 단계가 들어왔으면 연출 시각은 보내지 않는다(그 단계를 지나쳐 버리지 않게)
            void Pass(int ms)
            {
                flow.Tick(ms);
                if (!StepQueued(flow, match)) flow.Stage.Tweens.Advance(ms);
            }
            if (!FlowPilot.Run(flow, pilot, () => StepQueued(flow, match), Pass, UiShots.BattleStepMs, UiShots.BattleStepMax * 10))
            {
                var choice = flow.Client.FirstChoice();
                throw new InvalidOperationException(trail.Name + ": 그 행까지 가지 못했다(멈춘 곳: 선택 " + (choice != null ? choice.Kind + "/" + choice.Sub : "없음") + " · 화면 " + (flow.Top != null ? flow.Top.Name : "보드") + " · 입력 " + flow.InputOpen + " · 보드 " + flow.BoardOpen + " · 대기열 " + (flow.Fx != null ? flow.Fx.Sequencer.Queued.Count() : -1) + " · 길잡이 " + (choice != null ? pilot.Decide(flow.Ctx, flow.Client.View, choice, flow.Client.Legal(choice)) + "" : "-") + ")");
            }
            // 배틀로 턴이 끝난 뒤의 행(떨어진 별 7행): 그 단계는 대기열에 들어와도 배틀 결과 창 [확인] 뒤에 재생된다 — 결과 창이 서면 [확인]을 누른다(누르지 않으면 그 창에서 그림이 멈춘다)
            if (afterBattle && flow.Stage.Current is BattleScreen battle)
            {
                for (int i = 0; i < UiShots.BattleStepMax * 10 && !battle.ShowsResult; i++) UiShots.Pass(flow, UiShots.BattleStepMs);
                // 배틀 뒤 물음(격파 뒤 기술 가르치기 — 해금 48행 장면)은 마지막 답(가르치지 않기)으로 넘기고 [확인]이 설 때까지
                for (int i = 0; i < 20 && battle.AskingReward; i++)
                {
                    battle.PressReward(battle.Rewards.Count - 1);
                    UiShots.Pass(flow, UiShots.BattleStepMs);
                }
                for (int i = 0; i < 40 && !(flow.BattleOver && battle.Ready && !battle.AskingReward); i++) UiShots.Pass(flow, UiShots.BattleStepMs);
                if (!battle.Confirm()) throw new InvalidOperationException(trail.Name + ": 결과 창 [확인]이 거절됐다");
            }
            flow.Skip(flow.MsUntil(match));
            return SeekFrom(flow);
        }

        /// <summary>P39 기라티나 그림자(44행 희생 단계 시작이 0): 덮어 오는 중 · 다 덮음 · 걷히기 전(그림자가 있는 동안만 — 진폭이 0이 아니다).</summary>
        public static int[] P39ShadowShotsMs => new[] { 300, 800, 1100 };   // 속성: 장면 표(Scenes)가 위에서 먼저 만들어진다

        /// <summary>
        /// P39(M9 다듬기 8): 그 순간(<paramref name="at"/>의 시각)으로 보낸 뒤 배틀 화면의 DS가 그 박자의 진폭(<see cref="RippleBeats.Ds"/>)으로 일그러져 그려졌는지 단정한다 — 0.5 px 미만이거나 박자와 다르면 실패
        /// (셰이더 <see cref="DsStage.WarpShaderName"/>가 없으면 그려진 진폭이 0이다).
        /// </summary>
        public static Action<int> WarpSeen(Action<int> seek, int[] at) => ms =>
        {
            seek(ms);
            if (Array.IndexOf(at, ms) < 0) return;   // 나눠 보내는 확인(16ms 걸음)의 사이 시각은 보지 않는다
            var flow = BattleFlow;
            var screen = flow != null ? flow.Stage.Current as BattleScreen : null;
            if (screen == null || screen.Ds == null) throw new InvalidOperationException("p39: 배틀 화면(DS)이 없다(" + ms + "ms)");
            var seq = flow.Fx.Sequencer;
            float want = RippleBeats.Ds(seq.Current, seq.LocalMs);
            if (want < 0.5f) throw new InvalidOperationException("p39: 그림자가 덮지 않는 시각이다(" + ms + "ms · 박자 " + want + " px) — 찍는 시각을 고친다");
            if (Math.Abs(screen.Ds.WarpDrawn - want) > 0.01f || screen.Painted == null || screen.Painted.WarpPx != screen.Ds.WarpDrawn)
                throw new InvalidOperationException("p39: DS가 박자대로 일그러지지 않았다(그림 " + screen.Ds.WarpDrawn + " px · 박자 " + want + " px · " + ms + "ms)");
        };

        // 선턴 단계(54행 가운데 먼저 움직일 쪽이 정해진 것 — 우선권 토큰을 묻기 전의 비교(First −1)는 그릴 것이 없다)
        static bool OrderStep(FxStep step)
        {
            if (step.Row != 540) return false;
            foreach (var e in step.Events) if (e is Sinnoh.Rules.OrderDecided o && o.First >= 0) return true;
            return false;
        }

        // 우선권 토큰이 놓이는 인트로 단계(53행) · 토큰을 써서 넘기는 선턴 단계(54행)
        public static bool TokenPlaced(FxStep step)
        {
            if (step.Row != FxRows.Intro) return false;
            foreach (var e in step.Events) if (e is Sinnoh.Rules.InitiativeTokenAt) return true;
            return false;
        }

        public static bool TokenPassed(FxStep step)
        {
            if (step.Row != 540) return false;
            foreach (var e in step.Events) if (e is Sinnoh.Rules.OrderDecided o && o.TokenUsed) return true;
            return false;
        }

        /// <summary>우선권 토큰을 묻는 모습(Defeat 판의 첫 야생 배틀)까지 간 뒤 늘 도는 시각만 보낸다 — [사용]은 누르지 않는다(대기열은 비어 있다).</summary>
        static Action<int> TokenGlow(VisualElement root, KitSkin skin, UiGrade grade)
        {
            var flow = Flow(root, skin, grade);
            var pilot = FlowPilot.Start(flow, SoloTrail.Defeat);
            bool Asking() => flow.Stage.Current is BattleScreen b && flow.Battle != null && b.Open && b.AskingToken;
            if (!FlowPilot.Run(flow, pilot, Asking, ms => UiShots.Pass(flow, ms), UiShots.BattleStepMs, UiShots.BattleStepMax * 10)) throw new InvalidOperationException("defeat: 우선권 토큰을 묻는 데까지 가지 못했다");
            return SeekFrom(flow);
        }

        // 그 단계가 화면 대기열에 있고 아직 시작하지 않았다
        static bool StepQueued(GameFlow flow, Func<FxStep, bool> match)
        {
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            if (seq == null) return false;
            foreach (var entry in seq.Queued)
                foreach (var step in entry.Steps)
                    if (match(step) && (!ReferenceEquals(entry, seq.Current) || seq.LocalMs <= step.StartMs)) return true;
            return false;
        }

        // 지금 재생 중인 항목에서 이 시각에 걸린 단계의 행(04 §8-8 — fx-shots.json의 rows: Seek 확인 표가 읽는다). 화면 흐름이 없는 장면 · 대기열이 빈 때는 빈 목록
        static List<int> RowsNow()
        {
            var rows = new List<int>();
            var seq = BattleFlow != null && BattleFlow.Fx != null ? BattleFlow.Fx.Sequencer : null;
            if (seq == null || seq.Current == null) return rows;
            foreach (var step in seq.Current.Steps)
                if (step.StartMs <= seq.LocalMs && seq.LocalMs <= step.EndMs && !rows.Contains(step.Row)) rows.Add(step.Row);
            return rows;
        }

        /// <summary>마지막 <see cref="Shoot"/>이 찍은 순간에 재생 중이던 단계의 행.</summary>
        public static List<int> LastRows { get; private set; } = new List<int>();

        internal static bool AiQueued(GameFlow flow)
        {
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            if (seq == null) return false;
            foreach (var entry in seq.Queued)
            {
                var step = AiLineBeats.StepOf(entry);
                if (step != null && (!ReferenceEquals(entry, seq.Current) || seq.LocalMs <= step.StartMs)) return true;
            }
            return false;
        }

        static bool CanThrow(GameFlow flow) => flow.Stage.Current is BattleScreen b && flow.Battle != null && b.Open && b.BagOn;

        /// <summary>
        /// 찾아 둔 판을 길잡이가 화면의 함수로 지나 <paramref name="until"/>의 모습까지 간 뒤, 길잡이의 다음 답 하나(찾아보기 · 카드 고르기 · 볼 던지기)를 누르고 「그 뒤 ms가 지난 모습으로 보내기」를 돌려준다.
        /// 연출 시각만 보낸다(세션 시각은 그대로 — 상대(AI)의 답이 끼어들지 않는다).
        /// </summary>
        public static Action<int> FlowTrail(VisualElement root, KitSkin skin, UiGrade grade, SoloTrail trail, Func<GameFlow, bool> until, string protagonist = null, FxSpeed speed = FxSpeed.Normal)
        {
            var flow = Flow(root, skin, grade);
            flow.FxSpeed = speed;   // S02 「연출」(흐름의 음량 설정 — 메모리만, 사용자 설정에 쓰지 않는다)
            var pilot = FlowPilot.Start(flow, trail, protagonist);
            if (!FlowPilot.Run(flow, pilot, () => until(flow), ms => UiShots.Pass(flow, ms), UiShots.BattleStepMs, UiShots.BattleStepMax * 10)) throw new InvalidOperationException(trail.Name + ": 그 장면까지 가지 못했다");
            if (!FlowPilot.Step(flow, pilot, false)) throw new InvalidOperationException(trail.Name + ": 답이 거절됐다");
            return SeekFrom(flow);
        }

        /// <summary>[찾아보기]가 켜져 있다(길잡이의 다음 답 = 찾아보기).</summary>
        public static bool SearchOn(GameFlow flow) => flow.InputOpen && flow.Hud != null && flow.Hud.Shows(BoardHud.Search);

        /// <summary>조우 창에서 카드를 고를 수 있다(길잡이의 다음 답 = 카드).</summary>
        public static bool EncounterOpen(GameFlow flow) => flow.Stage.Current is EncounterScreen e && e.Open;

        /// <summary>
        /// 마우스 오버 들림(M9 다듬기 2): 약한 나무 판(<see cref="SoloTrail.Catch"/>)의 조우 창이 고를 수 있게 된 뒤 <paramref name="card"/>번(0부터)에 마우스를 올린 때가 0ms.
        /// 창 없는 패널에는 포인터를 넣지 못해 화면 함수(<see cref="EncounterScreen.Hover"/>)를 직접 부른다.
        /// </summary>
        public static Action<int> EncounterHover(VisualElement root, KitSkin skin, UiGrade grade, int card, FxSpeed speed = FxSpeed.Normal)
        {
            var flow = Flow(root, skin, grade);
            flow.FxSpeed = speed;
            var pilot = FlowPilot.Start(flow, SoloTrail.Catch);
            if (!FlowPilot.Run(flow, pilot, () => EncounterOpen(flow), ms => UiShots.Pass(flow, ms), UiShots.BattleStepMs, UiShots.BattleStepMax * 10)) throw new InvalidOperationException("catch: 조우 창까지 가지 못했다");
            ((EncounterScreen)flow.Stage.Current).Hover(card, true);
            return SeekFrom(flow);
        }

        // ── 배틀 연출 장면 ──

        sealed class Closer : IDisposable
        {
            readonly Action close;
            public Closer(Action close) { this.close = close; }
            public void Dispose() => close();
        }

        /// <summary>마지막으로 만든 배틀 장면의 화면 흐름(시험이 화면 값을 읽는다). 장면을 치우면 null.</summary>
        public static GameFlow BattleFlow { get; private set; }

        const string JsonDir = "Assets/Sinnoh/Data/Json/";
        static Sinnoh.Rules.Ctx gameData;

        /// <summary>엔진 정적 데이터(추적하는 Data/Json — 보드 씬의 <c>BoardBoot</c>가 가리키는 것과 같은 파일). 씬을 열지 않고 읽는다.</summary>
        public static Sinnoh.Rules.Ctx LoadCtx()
        {
            if (gameData != null) return gameData;
            string Text(string name)
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonDir + name + ".json");
                if (asset == null) throw new FileNotFoundException("게임 데이터 없음: " + JsonDir + name + ".json");
                return asset.text;
            }
            var board = Sinnoh.Data.BoardLoader.Load(Text("board"), out _);
            var cards = Sinnoh.Data.CardDbLoader.Load(Text("pokemon"), Text("moves"), Text("trainer_cards"));
            var items = Sinnoh.Data.ItemDbLoader.Load(Text("items"));
            var fixtures = Sinnoh.Data.FixtureDbLoader.Load(Text("fixtures"), board, cards, items);
            return gameData = new Sinnoh.Rules.Ctx(board, cards, Sinnoh.Data.DeckDbLoader.Load(Text("decks"), Text("cards"), cards), fixtures, items, Sinnoh.Data.EffectDbLoader.Load(Text("effects"), cards));
        }

        // 실행 중과 같은 화면 흐름(보드 그림 없이 — 보드·카메라는 null). 시드는 고정이다
        static GameFlow Flow(VisualElement root, KitSkin skin, UiGrade grade)
        {
            var stage = new UiStage(skin, UiShots.Strings());
            stage.Bind(root, grade);
            var flow = new GameFlow(stage, LoadCtx(), null, null, () => UiShots.Seed, () => { }, web: false,
                window: new FixedWindow(new Vector2Int(3840, 2160), new Vector2Int(grade.Width, grade.Height)), dice: LoadDiceLibrary());
            BattleFlow = flow;
            // 소리 단서(04 §8-4): 한 번에 보낸 것과 나눠 보낸 것의 소리 목록을 견준다(시퀀서 시각 · 행 · 키)
            flow.Sound += c => { if (!c.Immediate) shotSounds.Add(c.CueMs + " " + c.Row + " " + c.Key); };
            made.Add(new Closer(() =>
            {
                stage.Shut();
                stage.ShutHud();
                BattleFlow = null;
            }));
            return flow;
        }

        // 기술을 누른 뒤 연출 시각만 보낸다(세션 시각은 그대로 — 상대(AI)의 답이 끼어들지 않는다)
        static Action<int> SeekFrom(GameFlow flow)
        {
            int now = 0;
            return ms =>
            {
                if (ms < now) throw new ArgumentOutOfRangeException(nameof(ms), ms, "앞으로만 간다 — 되돌리려면 새로 만든다");
                flow.Stage.Tweens.Advance(ms - now);
                now = ms;
            };
        }

        /// <summary>
        /// 셋업 연출 장면(고정 시드 판 — UiShots와 같은 길): part 0 = 마을 [확정]을 누른 뒤가 0ms, 1 = 고르는 화면이 열린 때(카드가 들어오기 시작)가 0ms, 2 = 고르는 화면의 [확정]을 누른 뒤가 0ms.
        /// </summary>
        public static Action<int> SetupScene(VisualElement root, KitSkin skin, UiGrade grade, int part)
        {
            var flow = Flow(root, skin, grade);
            flow.StartSolo(new SoloSettings(), UiShots.Seed, UiShots.AiSeed);
            UiShots.PickVillage(flow);
            if (!((SetupVillageScreen)flow.Stage.Current).Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
            if (part == 0) return SeekFrom(flow);
            flow.Stage.Tweens.Advance(flow.MsUntil(step => step.Row == SetupRows.Deal));
            if (part == 1) return SeekFrom(flow);
            flow.Stage.Tweens.Advance(flow.QueueLeftMs);
            var pick = (SetupPickScreen)flow.Stage.Current;
            pick.PickStarter(0);
            pick.PickCard(1);
            if (!pick.Confirm()) throw new InvalidOperationException("셋업 확정이 거절됐다");
            return SeekFrom(flow);
        }

        /// <summary>고정 시드 판(UiShots와 같은 길): 셋업을 끝내고 그 연출(고른 뒤·공개 · 숨겨진 장소 배치)을 지난다. 그 뒤가 0ms다(게임 시작의 막 카드 · 조무래기 놓기가 여기서부터 돈다).</summary>
        public static Action<int> Opening(VisualElement root, KitSkin skin, UiGrade grade)
        {
            var flow = Flow(root, skin, grade);
            flow.StartSolo(new SoloSettings(), UiShots.Seed, UiShots.AiSeed);
            UiShots.PickVillage(flow);
            if (!((SetupVillageScreen)flow.Stage.Current).Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
            flow.Stage.Tweens.Advance(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
            var pick = (SetupPickScreen)flow.Stage.Current;
            pick.PickStarter(0);
            pick.PickCard(1);
            if (!pick.Confirm()) throw new InvalidOperationException("셋업 확정이 거절됐다");
            flow.Stage.Tweens.Advance(flow.MsUntil(step => step.Row == ActBeats.Row));
            return SeekFrom(flow);
        }

        /// <summary>고정 시드 판(UiShots와 같은 길): 셋업 → 고향 마을에서 [도전] → 내 차례에 첫 기술을 누른다. 그 뒤가 0ms다.</summary>
        public static Action<int> BattleAttack(VisualElement root, KitSkin skin, UiGrade grade)
        {
            var flow = Flow(root, skin, grade);
            flow.StartSolo(new SoloSettings(), UiShots.Seed, UiShots.AiSeed);
            UiShots.PickVillage(flow);
            if (!((SetupVillageScreen)flow.Stage.Current).Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
            flow.Stage.Tweens.Advance(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
            var pick = (SetupPickScreen)flow.Stage.Current;
            pick.PickStarter(0);
            pick.PickCard(1);
            if (!pick.Confirm()) throw new InvalidOperationException("셋업 확정이 거절됐다");
            flow.Stage.Tweens.Advance(flow.OpeningMs);   // 게임 시작의 막 카드 · 조무래기 놓기를 지나
            flow.Stage.Tweens.Advance(UiShots.BoardReadyMs);
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다");
            if (!UiShots.MyBattleTurn(flow)) throw new InvalidOperationException("내 차례가 오기 전에 배틀이 끝났다");
            if (!((BattleScreen)flow.Stage.Current).PressMove(0)) throw new InvalidOperationException("기술 답이 거절됐다");
            return SeekFrom(flow);
        }

        /// <summary>
        /// 찾아 둔 판(<see cref="SoloTrail"/>)을 길잡이가 화면으로 지나 배틀 화면이 <paramref name="until"/>(기본 = 첫 배틀의 내 기술 차례)이 될 때까지 가고,
        /// 거기서 <paramref name="press"/>(기본 = 길잡이의 답 — 결과 창의 [확인]은 누르지 않는다)를 누른다. 그 뒤가 0ms다.
        /// </summary>
        public static Action<int> BattleTrail(VisualElement root, KitSkin skin, UiGrade grade, SoloTrail trail, Func<BattleScreen, bool> until = null, Func<BattleScreen, bool> press = null)
        {
            var flow = Flow(root, skin, grade);
            var pilot = FlowPilot.Start(flow, trail);
            bool There() => flow.Stage.Current is BattleScreen b && flow.Battle != null && (until != null ? until(b) : b.Open && b.Moves.Count > 0);
            if (!FlowPilot.Run(flow, pilot, There, ms => UiShots.Pass(flow, ms), UiShots.BattleStepMs, UiShots.BattleStepMax * 10)) throw new InvalidOperationException(trail.Name + ": 그 장면까지 가지 못했다");
            bool pressed = press != null ? press((BattleScreen)flow.Stage.Current) : FlowPilot.Step(flow, pilot, false);
            if (!pressed) throw new InvalidOperationException(trail.Name + ": 답이 거절됐다");
            return SeekFrom(flow);
        }

        // 장면이 만든 것 가운데 치울 것(RT를 든 굴림판) — 한 장을 찍고 나면 버린다
        static readonly List<IDisposable> made = new List<IDisposable>();

        // 지금 찍는 장면의 화면 흐름이 낸 소리 단서(「시퀀서 시각 행 키」 — Shoot이 비우고 모은다)
        static readonly List<string> shotSounds = new List<string>();

        /// <summary>마지막 <see cref="Shoot"/>이 만든 장면의 소리 단서(화면 흐름이 없는 장면은 빈 목록).</summary>
        public static List<string> LastSounds { get; private set; } = new List<string>();

        /// <summary>추적하는 녹화본 묶음(<see cref="DiceBake.OutPath"/>).</summary>
        public static DiceLibrary LoadDiceLibrary()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(DiceBake.OutPath);
            if (asset == null) throw new FileNotFoundException("녹화본 묶음 없음: " + DiceBake.OutPath);
            return DiceLibrary.Parse(asset.bytes);
        }

        public static FxDiceSample Dice(VisualElement root, KitSkin skin, UiGrade grade, FxDiceSample.Variant variant)
        {
            var sample = new FxDiceSample(root, skin, grade, LoadDiceLibrary(), variant);
            made.Add(sample);
            return sample;
        }

        /// <summary>장면이 만든 것(굴림판 RT · 배틀 장면의 화면)을 치운다 — 장면을 직접 지은 시험이 끝에 부른다.</summary>
        public static void Release() => Cleanup();

        static void Cleanup()
        {
            foreach (var d in made) d.Dispose();
            made.Clear();
        }

        [MenuItem("Sinnoh/Capture/Fx")]
        static void Menu() => Capture("all", OutDir);

        public static void Run()
        {
            bool ok = false;
            try
            {
                UiShots.Lang = Arg("-uiLang", "ko");   // 화면 글의 언어(M9 영어 넘침 전수 — UiShots와 같은 인자, 기본 ko)
                int every = int.Parse(Arg("-fxFrames", "0"));
                ok = every > 0 ? Frames(Arg("-fxScene", ""), Arg("-fxOut", OutDir), every, int.Parse(Arg("-fxUntil", "0"))) : Capture(Arg("-fxScene", "all"), Arg("-fxOut", OutDir));
            }
            catch (Exception e) { Debug.LogException(e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        static bool Capture(string scene, string outDir)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogError("[FxShots] 그래픽 장치 없음(-nographics): 찍지 않는다");
                return false;
            }
            var scenes = new List<Scene>();
            foreach (var s in Scenes) if (scene == "all" || Array.IndexOf(scene.Split(','), s.Name) >= 0) scenes.Add(s);
            if (scenes.Count == 0)
            {
                var names = new List<string>();
                foreach (var s in Scenes) names.Add(s.Name);
                Debug.LogError($"[FxShots] 모르는 장면: -fxScene {scene} — 장면: all, {string.Join(", ", names)}");
                return false;
            }

            Directory.CreateDirectory(outDir);
            var fails = new List<string>();
            var layoutLogs = new List<string>();
            var json = new StringBuilder("{\"images\":[");
            int images = 0;
            foreach (var s in scenes)
            {
                Color32[] prev = null;
                // 장면 하나의 예외는 그 장면만 실패로 적고 남은 장면을 마저 찍는다(종료 코드는 하나라도 실패면 1)
                try
                {
                    foreach (int ms in s.TimesMs)
                    {
                        string file = s.Name + "-" + ms.ToString("00000");
                        var sw = Stopwatch.StartNew();
                        var px = Shoot(s.Name, ms, 0, layoutLogs);
                        var sounds = LastSounds;
                        string rows = string.Join(",", LastRows);
                        long took = sw.ElapsedMilliseconds;
                        if (px == null) { Debug.LogError("[FxShots] Gen 없음(글꼴·창틀): rebuild-all 먼저"); return false; }
                        var grade = s.Grade ?? Grade;
                        WritePng(Path.Combine(outDir, file + ".png"), px, grade);
                        int colors = Colors(px);
                        if (colors < 2) fails.Add($"{file} 빈 그림(색 {colors})");

                        string steppedPath = Path.Combine(outDir, file + "-stepped.png");
                        File.Delete(steppedPath);
                        var stepped = Shoot(s.Name, ms, FrameMs, layoutLogs);
                        int stepDiff = Diff(px, stepped);
                        bool soundsSame = sounds.SequenceEqual(LastSounds);
                        if (!soundsSame) fails.Add($"{file} 한 번에 보낸 것과 {FrameMs}ms씩 보낸 것의 소리 단서가 다르다({sounds.Count} / {LastSounds.Count})");
                        if (stepDiff > 0)
                        {
                            WritePng(steppedPath, stepped, grade);
                            fails.Add($"{file} 한 번에 보낸 것과 {FrameMs}ms씩 보낸 것이 다른 픽셀 {stepDiff}");
                        }
                        int moved = prev == null ? -1 : Diff(prev, px);
                        if (moved == 0) fails.Add($"{file} 앞 시각의 그림과 같다(움직이지 않았다)");
                        prev = px;

                        if (images++ > 0) json.Append(',');
                        json.Append("{\"name\":\"").Append(file).Append("\",\"scene\":\"").Append(s.Name).Append("\",\"ms\":").Append(ms)
                            .Append(",\"width\":").Append(grade.Width).Append(",\"height\":").Append(grade.Height).Append(",\"colors\":").Append(colors)
                            .Append(",\"steppedDiff\":").Append(stepDiff).Append(",\"rows\":[").Append(rows).Append("],\"sounds\":").Append(sounds.Count).Append(",\"soundsSame\":").Append(soundsSame ? "true" : "false").Append(",\"movedFromPrev\":").Append(moved < 0 ? "null" : moved.ToString())
                            .Append(",\"tookMs\":").Append(took).Append('}');
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    fails.Add($"{s.Name} 예외: {e.GetType().Name}: {e.Message}");
                }
            }
            foreach (string log in layoutLogs) fails.Add("배치 흔들림: " + log);

            json.Append("],\"layoutLogs\":").Append(layoutLogs.Count).Append(",\"ok\":").Append(fails.Count == 0 ? "true" : "false").Append(",\"fails\":[");
            for (int i = 0; i < fails.Count; i++) json.Append(i > 0 ? "," : "").Append('"').Append(fails[i].Replace("\\", "/").Replace("\"", "'")).Append('"');
            json.Append("]}\n");
            File.WriteAllText(Path.Combine(outDir, "fx-shots.json"), json.ToString(), new UTF8Encoding(false));

            if (fails.Count == 0) Debug.Log($"[FxShots] 통과: 그림 {images}장, 나눠 보낸 것과 다른 픽셀 0 → {outDir}/fx-shots.json");
            else Debug.LogError($"[FxShots] 실패 {fails.Count}: {string.Join("; ", fails)}");
            return fails.Count == 0;
        }

        /// <summary>장면 하나를 한 번 만들어 0부터 <paramref name="untilMs"/>(0이면 마지막 찍는 시각 + 400)까지 <paramref name="everyMs"/> 간격으로 보내며 찍는다.</summary>
        static bool Frames(string scene, string outDir, int everyMs, int untilMs)
        {
            Scene found = null;
            foreach (var s in Scenes) if (s.Name == scene) found = s;
            if (found == null) { Debug.LogError("[FxShots] -fxFrames에는 장면 하나를 준다: -fxScene <이름>"); return false; }
            var skin = UiSkin.Load();
            if (skin == null) { Debug.LogError("[FxShots] Gen 없음(글꼴·창틀): rebuild-all 먼저"); return false; }
            var grade = found.Grade ?? Grade;
            if (untilMs <= 0) untilMs = found.TimesMs[found.TimesMs.Length - 1] + 400;
            string dir = Path.Combine(outDir, "frames-" + scene);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            int count = 0;
            try
            {
                using (var panel = new UiShotPanel(grade.Width, grade.Height, grade.PanelScale))
                {
                    var seek = found.Build(panel.Root, skin, grade);
                    panel.Tick(3);
                    for (int t = 0; t <= untilMs; t += everyMs)
                    {
                        seek(t);
                        panel.Tick(3);
                        WritePng(Path.Combine(dir, "f" + (count++).ToString("0000") + ".png"), panel.Read(), grade);
                    }
                }
            }
            finally
            {
                Cleanup();
                UiSkin.Release(skin);
            }
            Debug.Log($"[FxShots] 통과: {scene} {count}장({everyMs}ms 간격, {untilMs}ms까지) → {dir}");
            return true;
        }

        /// <summary>
        /// 장면을 처음부터 새로 만들어 <paramref name="ms"/>가 지난 모습으로 보내고 그 픽셀을 돌려준다(아래 줄부터). <paramref name="stepMs"/>가 0이면 한 번에,
        /// 아니면 그 걸음으로 나눠 보낸다. Gen 본체(글꼴·창틀)가 없으면 null.
        /// </summary>
        public static Color32[] Shoot(string scene, int ms, int stepMs, List<string> layoutLogs)
        {
            Scene found = null;
            foreach (var s in Scenes) if (s.Name == scene) found = s;
            if (found == null) throw new ArgumentException("모르는 장면: " + scene, nameof(scene));
            var skin = UiSkin.Load();
            if (skin == null) return null;
            Application.LogCallback watch = (message, stack, type) => { if (message.Contains("Layout update")) layoutLogs.Add(message); };
            shotSounds.Clear();
            Application.logMessageReceived += watch;
            try
            {
                var grade = found.Grade ?? Grade;
                using (var panel = new UiShotPanel(grade.Width, grade.Height, grade.PanelScale))
                {
                    var seek = found.Build(panel.Root, skin, grade);
                    panel.Tick(3);   // 자리가 잡힌 뒤에 보낸다(크기·자리는 만들 때 한 번)
                    if (stepMs <= 0) seek(ms);
                    else
                        for (int t = 0; t < ms;)
                        {
                            t = Math.Min(ms, t + stepMs);
                            seek(t);
                        }
                    panel.Tick(6);
                    LastSounds = new List<string>(shotSounds);
                    LastRows = RowsNow();
                    return panel.Read();
                }
            }
            finally
            {
                Application.logMessageReceived -= watch;
                Cleanup();
                UiSkin.Release(skin);
            }
        }

        static int Diff(Color32[] a, Color32[] b)
        {
            int diff = 0;
            for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) diff++;
            return diff;
        }

        static int Colors(Color32[] px)
        {
            var seen = new HashSet<int>();
            foreach (var p in px) seen.Add(p.r << 24 | p.g << 16 | p.b << 8 | p.a);
            return seen.Count;
        }

        static void WritePng(string path, Color32[] px, UiGrade grade)
        {
            var tex = new Texture2D(grade.Width, grade.Height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            tex.SetPixels32(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
