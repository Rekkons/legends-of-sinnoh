using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 울트라 버스트 배틀 행(04 §8-8 87~92행 가운데 배틀 쪽 — M6 「UB 배틀 화면 묶음」): 프리즘 아머가 막음 88(880) · 프리즘 아머가 울트라 비스트 쪽으로 옮겨 감 88(881) ·
    /// 울트라 온슬롯 카드 89(890 — 04 표의 89행) · 네크로즈마 페이즈 격파 90(900 — 보드, 결과 창 [확인] 뒤) · 썬/문 동전 90(901 — 보드, 조우 전환 앞) · 호수 시련 진행·넘김 92(920) · 수호신 격파 이동력 +2 92(921).
    /// 87행 UB 등장은 새 행이 아니다 — 조우 전환(22행)의 줄무늬 색 · 인트로 문구 · 기둥 머리 · 「포획 불가」 단추가 같은 박자 안에서 바뀐다.
    /// 길이는 방장 <c>PlayTiming</c>(04 시간 열 안에서 고름 [가정 plan §5 #159]), 여기 값은 그 단계 안의 박자다.
    /// </summary>
    public static class UbBattleRows
    {
        public const int Prism = 880, PrismMove = 881, Onslaught = 890, Phase = 900, Coin = 901, Trial = 920, Guardian = 921;
        /// <summary>93행 실버디 격파·포획 → 아르세우스의 축복 +1(930 — 보드, 결과 창 [확인] 뒤 · <see cref="SilvallyBeats"/> — M6 「연출 목록 채우기」 [가정 plan §5 #163]).</summary>
        public const int Silvally = 930;

        /// <summary>배틀 화면이 그리는 행.</summary>
        public static bool Battle(int row) => row == Prism || row == PrismMove || row == Onslaught || row == Trial || row == Guardian;

        /// <summary>보드(HUD의 <c>BoardUb</c>)가 그리는 행.</summary>
        public static bool Board(int row) => row == Phase || row == Coin || row == Silvally;

        public static bool Built(int row) => Battle(row) || Board(row);

        /// <summary>지금 재생 중인 그 행의 단계(같은 항목에 여럿이면 지금 시각이 든 것). 없으면 null.</summary>
        public static FxStep Playing(FxSequencer seq, int row)
        {
            var entry = seq != null ? seq.Current : null;
            if (entry == null) return null;
            foreach (var s in entry.Steps)
                if (s.Row == row && seq.LocalMs >= s.StartMs && seq.LocalMs < s.EndMs) return s;
            return null;
        }

        public static T First<T>(FxStep step) where T : GameEvent => UbRows.First<T>(step);

        /// <summary>
        /// 네크로즈마 페이즈 격파(900)는 배틀 끝과 한 행동으로 배틀 끝 뒤에 온다 — 결과 창 [확인] 뒤 보드에서 보이게 그 단계를 떼어 새 항목으로 세운다(귀환·게임 끝과 같은 떼기). 뗀 수.
        /// </summary>
        public static int SplitPhase(List<FxEntry> entries) => WhiteoutBeats.SplitOff(entries, e => e != null ? e.Step(Phase) : null, First<NecrozmaPhase>);

        /// <summary>실버디의 축복(930)도 배틀 끝과 한 행동으로 온다 — 결과 창 [확인] 뒤 보드에서(같은 떼기). 뗀 수.</summary>
        public static int SplitSilvally(List<FxEntry> entries) => WhiteoutBeats.SplitOff(entries, e => e != null ? e.Step(Silvally) : null, First<BlessingGained>);

        /// <summary>그 축복 얻기가 실버디 몫(93행)인가 — 아르세우스 운명 카드 3장의 축복(Source = Fate)은 단계가 없다(45행 카드 내기가 그린다).</summary>
        public static bool BySilvally(BlessingGained e) => e != null && e.Source.Kind == SourceKind.Rule && e.Source.Id == (int)RuleSource.Silvally;
    }

    /// <summary>
    /// 88행 프리즘 아머: 막음(880 — UB·네크로즈마의 체력 상자 앞에 프리즘 방패가 서고 「프리즘 −n」이 튄다) · 옮겨 감(881 — 「네크로즈마 a · 울트라 비스트 b」 판에서 조각 하나가 오른쪽으로 날아가 숫자가 바뀐다).
    /// </summary>
    public static class PrismBeats
    {
        public const int ShieldInMs = 120, ShieldOutMs = 180, PopAtMs = 80, PopMs = 160;
        public const int PanelInMs = 150, PanelOutMs = 150, FlyFromMs = 200, FlyMs = 550;

        /// <summary>막음의 한 순간: 방패가 보이는 만큼 · 칩이 보이는 만큼 · 칩 크기.</summary>
        public static void Block(int ms, int total, out float shield, out float chip, out float scale)
        {
            shield = chip = 0f;
            scale = 1f;
            if (total <= 0 || ms < 0 || ms >= total) return;
            shield = Math.Min(Tween.Progress(ms, 0, ShieldInMs), 1f - Tween.Progress(ms, total - ShieldOutMs, ShieldOutMs));
            chip = Math.Min(Tween.Progress(ms, PopAtMs, 60), 1f - Tween.Progress(ms, total - 100, 100));
            scale = Tween.At(ms, PopAtMs, PopMs, 1.5f, 1f, Ease.OutQuad);
        }

        /// <summary>옮겨 감의 한 순간: 판이 보이는 만큼 · 조각이 간 만큼(0 왼쪽 칩 ~ 1 오른쪽 칩, 날기 전 −1) · 숫자가 바뀌었나(조각이 닿은 뒤).</summary>
        public static void Move(int ms, int total, out float panel, out float fly, out bool arrived)
        {
            panel = 0f;
            fly = -1f;
            arrived = false;
            if (total <= 0 || ms < 0 || ms >= total) return;
            panel = Math.Min(Tween.Progress(ms, 0, PanelInMs), 1f - Tween.Progress(ms, total - PanelOutMs, PanelOutMs));
            if (ms >= FlyFromMs) fly = Tween.Apply(Ease.InOutQuad, Tween.Progress(ms, FlyFromMs, FlyMs));
            arrived = ms >= FlyFromMs + FlyMs;
        }
    }

    /// <summary>울트라 온슬롯 카드 한 순간(<see cref="OnslaughtBeats.At"/>).</summary>
    public struct OnslaughtPose
    {
        /// <summary>보이는 만큼 · 떠오른 정도(0 = 상자 옆 아래, 1 = 제자리) · 떨림 px · 가로 크기(뒤집힘) · 앞면인가 · 전체 크기 · 효과 빛(0 ~ 1) · 상자 옆으로 붙은 정도(0 ~ 1).</summary>
        public float Alpha, Rise, ScaleX, Scale, Glow, Dock;
        public int Shake;
        public bool Front;
    }

    /// <summary>
    /// 89행 울트라 온슬롯(890 — 04 표 「예비: 적 상자 옆에서 카드가 떨며 떠오름 → 움직임: 가로 뒤집힘 → 맞음: 효과가 들어가는 박자 → 착지: 카드가 효과 아이콘으로 줄어 상자 옆에 붙음」).
    /// 카드 그림이 Gen에 없어 글 카드(이름 + 효과 한 줄)다 [가정 plan §5 #159].
    /// </summary>
    public static class OnslaughtBeats
    {
        public const int RisePermille = 250, FlipPermille = 450, HitPermille = 750, ShakeEveryMs = 40;

        public static OnslaughtPose At(int ms, int total)
        {
            var p = new OnslaughtPose { ScaleX = 1f, Scale = 1f };
            if (total <= 0 || ms < 0 || ms >= total) return p;
            int rise = total * RisePermille / 1000, flip = total * FlipPermille / 1000, hit = total * HitPermille / 1000;
            p.Alpha = Math.Min(Tween.Progress(ms, 0, 80), 1f - Tween.Progress(ms, total - 80, 80));
            p.Rise = Tween.Apply(Ease.OutQuad, Tween.Progress(ms, 0, rise));
            p.Shake = ms < rise ? (ms / ShakeEveryMs % 2 == 0 ? 1 : -1) : 0;
            if (ms < rise) p.Front = false;
            else if (ms < flip)
            {
                float u = Tween.Progress(ms, rise, flip - rise);
                p.ScaleX = Math.Abs((float)Math.Cos(Math.PI * u));
                p.Front = u >= 0.5f;
            }
            else p.Front = true;
            if (ms >= flip && ms < hit) p.Glow = 1f - Tween.Progress(ms, flip, hit - flip);
            p.Dock = Tween.Apply(Ease.InQuad, Tween.Progress(ms, hit, total - hit));
            p.Scale = 1f - 0.6f * p.Dock;
            return p;
        }
    }

    /// <summary>
    /// 92행 호수 시련(920): 진행이 오르면 기둥의 시련 칩이 톡 커지고, 넘기면(Done) DS의 면역 막(옅은 하늘색 오라)이 하얗게 번쩍인 뒤 깨져 사라지고 「시련 통과!」가 튄다.
    /// 수호신 격파·포획(921): DS 위에 「모든 트레이너 이동력 +2」 판이 선다.
    /// </summary>
    public static class TrialBeats
    {
        public const int PopMs = 200, CrackAtPermille = 250, ShatterPermille = 600, PassAtPermille = 450, PassPopMs = 200;
        /// <summary>면역 막: 오라 px · 깨질 때 번쩍이는 px.</summary>
        public const int ShieldPx = 1, CrackPx = 3;

        /// <summary>칩의 크기(진행이 오른 단계의 처음).</summary>
        public static float Pop(int ms) => ms < 0 || ms >= PopMs ? 1f : Tween.At(ms, 0, PopMs, 1.35f, 1f, Ease.OutQuad);

        /// <summary>넘김의 한 순간: 막이 남은 만큼(1 = 그대로, 0 = 깨져 없음) · 번쩍(0 ~ 1) · 「시련 통과!」가 보이는 만큼 · 크기.</summary>
        public static void Pass(int ms, int total, out float shield, out float flash, out float banner, out float scale)
        {
            shield = 1f;
            flash = banner = 0f;
            scale = 1f;
            if (total <= 0 || ms < 0) return;
            int crack = total * CrackAtPermille / 1000, gone = total * ShatterPermille / 1000, pass = total * PassAtPermille / 1000;
            if (ms >= total) { shield = 0f; return; }
            flash = ms < crack ? Tween.Progress(ms, 0, crack) : 1f - Tween.Progress(ms, crack, gone - crack);
            shield = ms < crack ? 1f : 1f - Tween.Progress(ms, crack, gone - crack);
            banner = Math.Min(Tween.Progress(ms, pass, 80), 1f - Tween.Progress(ms, total - 150, 150));
            scale = Tween.At(ms, pass, PassPopMs, 1.4f, 1f, Ease.OutQuad);
        }

        /// <summary>이동력 +2 판의 한 순간: 보이는 만큼 · 「+2」 크기.</summary>
        public static void Guardian(int ms, int total, out float banner, out float scale)
        {
            banner = 0f;
            scale = 1f;
            if (total <= 0 || ms < 0 || ms >= total) return;
            banner = Math.Min(Tween.Progress(ms, 0, 200), 1f - Tween.Progress(ms, total - 250, 250));
            scale = Tween.At(ms, 300, 200, 1.5f, 1f, Ease.OutQuad);
        }
    }

    /// <summary>네크로즈마 페이즈 · 동전의 보드 한 순간(<see cref="PhaseBeats.At"/> · <see cref="CoinBeats.At"/>).</summary>
    public struct PhasePose
    {
        /// <summary>네크로즈마 둘레의 빛(0 ~ 1) · 빛 고리 크기 · 가운데 판이 보이는 만큼 · 체력 막대가 찬 만큼(0 ~ 1).</summary>
        public float Burst, Ring, Panel, Fill;
    }

    /// <summary>
    /// 90행 네크로즈마 페이즈 격파(900 — 결과 창 [확인] 뒤 보드): 깨어진 세계의 네크로즈마에서 빛이 터지고(카메라가 그 칸으로) → 가운데 판 「네크로즈마 n페이즈」 · 다음 폼 이름 ·
    /// 체력 막대가 0에서 다시 찬다(24 → 30 → 36 — 다음 도전은 완전 회복, p.25). 피규어는 그 단계가 끝나면 다음 폼으로 선다(<see cref="UbBoard.NecrozmaForm"/>).
    /// </summary>
    public static class PhaseBeats
    {
        public const int BurstMs = 800, PanelFromMs = 500, FillFromMs = 900, FillMs = 900;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(UbBattleRows.Phase) : null;

        public static NecrozmaPhase Event(FxStep step) => UbBattleRows.First<NecrozmaPhase>(step);

        public static PhasePose At(int ms, int total)
        {
            var p = new PhasePose();
            if (total <= 0 || ms < 0 || ms >= total) return p;
            p.Burst = ms < BurstMs ? 1f - Tween.Progress(ms, BurstMs / 2, BurstMs / 2) : 0f;
            p.Ring = 0.5f + 1.5f * Tween.Apply(Ease.OutQuad, Tween.Progress(ms, 0, BurstMs));
            p.Panel = Math.Min(Tween.Progress(ms, PanelFromMs, 150), 1f - Tween.Progress(ms, total - 250, 250));
            p.Fill = Tween.Apply(Ease.OutQuad, Tween.Progress(ms, FillFromMs, FillMs));
            return p;
        }
    }

    /// <summary>썬/문 동전 한 순간(<see cref="CoinBeats.At"/>).</summary>
    public struct CoinPose
    {
        /// <summary>보이는 만큼 · 떠오른 높이(0 ~ 1) · 가로 크기(도는 동전) · 크기(착지 눌림) · 결과 글이 보이는 만큼.</summary>
        public float Alpha, Lift, ScaleX, Scale, Label;
        /// <summary>지금 보이는 면(0 = 썬, 1 = 문 — 착지 뒤에는 결과).</summary>
        public int Face;
        public bool Landed;
    }

    /// <summary>
    /// 90행 썬/문 동전(901 — 2페이즈에 처음 도전할 때, 조우 전환 앞 보드): 화면 가운데 위로 동전이 돌며 솟았다 떨어지고 → 착지(눌림) → 「썬 — 황혼의 갈기」·「문 — 새벽의 날개」.
    /// </summary>
    public static class CoinBeats
    {
        public const int SpinMs = 1000, SpinEveryMs = 120, LabelAtMs = 1100;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(UbBattleRows.Coin) : null;

        public static CoinFlip Event(FxStep step) => UbBattleRows.First<CoinFlip>(step);

        public static CoinPose At(int ms, int total, int result)
        {
            var p = new CoinPose { ScaleX = 1f, Scale = 1f, Face = result };
            if (total <= 0 || ms < 0 || ms >= total) return p;
            p.Alpha = Math.Min(Tween.Progress(ms, 0, 100), 1f - Tween.Progress(ms, total - 150, 150));
            if (ms < SpinMs)
            {
                float u = (float)ms / SpinMs;
                p.Lift = 4f * u * (1f - u);
                double turn = Math.PI * ms / SpinEveryMs;
                p.ScaleX = Math.Max(0.08f, Math.Abs((float)Math.Cos(turn)));
                p.Face = (int)(ms / SpinEveryMs) % 2 == 0 ? 0 : 1;
                return p;
            }
            p.Landed = true;
            p.Scale = Tween.At(ms, SpinMs, 150, 0.8f, 1f, Ease.OutQuad);
            p.Label = Tween.Progress(ms, LabelAtMs, 120);
            return p;
        }
    }

    /// <summary>실버디의 축복 한 순간(<see cref="SilvallyBeats.At"/>).</summary>
    public struct SilvallyPose
    {
        /// <summary>신전 칸의 금빛(0 ~ 1) · 축복 아이콘이 날아간 정도(−1 = 아직 · 0 신전 칸 ~ 1 위 띠 축복 칩 · 2 = 닿아 사라짐) · 칩의 금빛 고리(0 ~ 1 — 커지며 옅어짐) · 칩 크기.</summary>
        public float Light, Fly, Ring, Pop;
        /// <summary>축복 칩의 숫자가 +1 됐나(아이콘이 닿은 뒤).</summary>
        public bool Arrived;
    }

    /// <summary>
    /// 93행 실버디 격파·포획 → 아르세우스의 축복 +1(930 — 결과 창 [확인] 뒤 보드, 04 표 「예비: 실버디가 빛으로 흩어짐 → 움직임: 축복 아이콘이 신전에서 위 띠 축복 칸으로 호를 그려 날아감 →
    /// 맞음: 칸에 박히며 금빛 고리 → 착지: 축복 수 +1」). 배틀 화면은 이미 닫혀 있어 「흩어짐」은 신전 칸(이긴 좌석이 선 칸)의 금빛으로 갈음한다 [가정 plan §5 #163].
    /// </summary>
    public static class SilvallyBeats
    {
        public const int LightMs = 700, FlyFromMs = 400, FlyMs = 600, RingMs = 400;   // 금빛은 결과 창을 걷는 줄무늬(EnterBeats.WipeBackMs 300) 뒤까지 남는다

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(UbBattleRows.Silvally) : null;

        public static BlessingGained Event(FxStep step) => UbBattleRows.First<BlessingGained>(step);

        public static SilvallyPose At(int ms, int total)
        {
            var p = new SilvallyPose { Fly = -1f, Pop = 1f };
            if (total <= 0 || ms < 0) return p;
            if (ms >= total) return new SilvallyPose { Fly = 2f, Pop = 1f, Arrived = true };
            p.Light = ms < LightMs ? 1f - Tween.Progress(ms, LightMs / 2, LightMs / 2) : 0f;
            int arrive = FlyFromMs + FlyMs;
            if (ms >= FlyFromMs) p.Fly = ms < arrive ? Tween.Apply(Ease.InOutQuad, Tween.Progress(ms, FlyFromMs, FlyMs)) : 2f;
            p.Arrived = ms >= arrive;
            if (p.Arrived)
            {
                float u = Tween.Progress(ms, arrive, RingMs);
                p.Ring = u < 1f ? 1f - u : 0f;
                p.Pop = Tween.At(ms, arrive, 200, 1.4f, 1f, Ease.OutQuad);
            }
            return p;
        }

        /// <summary>
        /// 위 띠 축복 칩이 지금 보일 수: 보기 값에서, 대기열(재생 중인 항목 포함)에 남은 첫 실버디 축복 단계의 아이콘이 닿기 전이면 그 얻기 전 값(Total − 1).
        /// </summary>
        public static int Shown(int blessings, IEnumerable<FxEntry> queued, FxEntry current, int localMs)
        {
            if (queued == null) return blessings;
            foreach (var entry in queued)
            {
                var step = StepOf(entry);
                var e = Event(step);
                if (e == null) continue;
                bool playing = ReferenceEquals(entry, current);
                if (playing && localMs - step.StartMs >= FlyFromMs + FlyMs) continue;
                return Math.Max(0, e.Total - 1);
            }
            return blessings;
        }
    }
}
