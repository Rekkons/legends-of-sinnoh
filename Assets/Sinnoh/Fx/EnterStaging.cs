using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Rekkon.UiKit;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 갤럭시단 조무래기의 앞모습 그림(결정 70 ⑩): 남·여를 조무래기마다 섞는다 — 그 조무래기가 선 칸(배틀이 벌어진 칸) 번호에서 셈한다.
    /// 규칙 상태가 아니라 보기 값이라 세이브·방장 화면과 무관하고, 같은 판의 같은 조무래기는 다시 도전해도 · 이어 해도 늘 같은 그림이다 [가정 — plan §5 #65].
    /// </summary>
    public static class FoeArt
    {
        /// <summary>Gen <c>Stage/Foe/</c>의 앞모습 둘(80×80, 발 (40, 78) — <c>Tools/assets/tables/ball_throws.json</c>의 foe 줄).</summary>
        public const string GruntMale = "Stage/Foe/galactic_grunt.png", GruntFemale = "Stage/Foe/galactic_grunt_f.png";

        /// <summary>그 칸의 조무래기가 여자 그림인가(칸 번호를 섞은 값의 한 비트 — 이웃한 칸끼리 번갈지 않게 섞는다). 칸을 모르면(음수) 남자.</summary>
        public static bool Female(int cell)
        {
            if (cell < 0) return false;
            uint h = unchecked((uint)cell * 2654435761u);
            return ((h >> 19) & 1u) == 1u;
        }

        public static string Grunt(int cell) => Female(cell) ? GruntFemale : GruntMale;

        /// <summary>간부 셋 · 보스 태홍의 앞모습(Pt 원작 자산 trfgra — 결정 80 L4 나, M6 그림 에셋). 같은 표의 foe 줄.</summary>
        public const string Mars = "Stage/Foe/commander_mars.png", Jupiter = "Stage/Foe/commander_jupiter.png", Saturn = "Stage/Foe/commander_saturn.png", Cyrus = "Stage/Foe/cyrus.png";

        /// <summary>갤럭시단 상대의 앞모습: 간부·태홍이면 그 인물(명단 카드의 <see cref="GalacticTrainer"/>), 조무래기·모름이면 칸으로 고른 조무래기(<see cref="Grunt"/>).</summary>
        public static string For(GalacticTrainer trainer, int cell)
        {
            switch (trainer)
            {
                case GalacticTrainer.Mars: return Mars;
                case GalacticTrainer.Jupiter: return Jupiter;
                case GalacticTrainer.Saturn: return Saturn;
                case GalacticTrainer.Cyrus: return Cyrus;
                default: return Grunt(cell);
            }
        }
    }

    /// <summary>
    /// 들어가고 나오는 연출의 화면 상수(ms — 04 §8-8 20·21·22·53·68·70·72행의 단계 안 박자). 단계 길이는 방장과 같은 <c>PlayTiming</c>이 세고,
    /// 여기 값은 그 단계 안에서 화면이 나누는 박자라 방장 멈춤에 들지 않는다. 포획(68행)의 값은 목업 r5-fx README의 시간표(결정 54 ①) 그대로이고, 나머지는 [가정 — 캡처로 맞췄다].
    /// </summary>
    public static class EnterBeats
    {
        /// <summary>찾아보기(20행): 「!」가 튀어나오는 시간 · 머무는 끝 · 칸 테두리 맥박 한 번의 길이(켜짐 = 앞 절반)와 횟수.</summary>
        public const int BangPopMs = 200, BangEndMs = 1300, PulseEveryMs = 500, Pulses = 3;
        /// <summary>조우 카드 공개(21행): 창이 뜨고 첫 장이 뒤집히기까지 · 장마다 어긋남 · 한 장이 뒤집히는 시간.</summary>
        public const int FlipLeadMs = 300, FlipEveryMs = 250, FlipMs = 250;
        /// <summary>고른 뒤(21행 「고른 뒤 600」): 고른 카드가 커지는 시간.</summary>
        public const int PickPopMs = 200;
        /// <summary>
        /// 강한 · 전설 카드(21행 「강한 카드는 늦게 · 무겁게」 — M9 다듬기 2, [가정 plan §5 #415]): 가벼운 카드가 다 뒤집힌 뒤(없으면 <see cref="FlipLeadMs"/> 뒤) 더 기다림 · 무거운 카드끼리 어긋남 ·
        /// 떠오름(예비) · 떠오른 채 뒤집히기 시작 · 느린 뒤집기 · 떨어짐(움직임) · 닿은 뒤 눌렸다 펴짐(맞음 · 착지). 닿는 때 = 뒤집기 시작 + 뒤집기 + 떨어짐.
        /// </summary>
        public const int HeavyLeadMs = 300, HeavyEveryMs = 400, HeavyRiseMs = 300, HeavyFlipFromMs = 150, HeavyFlipMs = 400, HeavyDropMs = 160, HeavySquashMs = 240;
        /// <summary>무거운 카드가 바닥에 닿는 때(떠오르기 시작부터).</summary>
        public const int HeavyLandMs = HeavyFlipFromMs + HeavyFlipMs + HeavyDropMs;
        /// <summary>고른 뒤 나머지 카드가 덱으로 쓸려 감(M9 다듬기 2): 카드마다 어긋남 · 뒷면으로 뒤집힘 · 아래로 미끄러지며 흐려짐.</summary>
        public const int SweepEveryMs = 60, SweepFlipMs = 160, SweepSlideMs = 240;
        /// <summary>마우스를 올린 카드가 들리는 · 내려앉는 시간(내 화면만 — 대기열 밖, 「빠름」이면 <see cref="FxTune.FastPercent"/>, 「끄기」면 들리지 않는다).</summary>
        public const int HoverMs = 120;
        /// <summary>조우 전환(22행) — 단계 길이의 천분율: 줄무늬가 닫히는 구간 · 화면이 바뀌는 때 · 열리는 구간. 닫히기 전은 카메라가 그 칸으로 다가가는 예비.</summary>
        public const int WipeCloseFrom = 300, WipeCloseTo = 500, WipeSwapAt = 500, WipeOpenFrom = 600, WipeOpenTo = 800;
        /// <summary>결과 [확인] 뒤 보드로 돌아오는 역전환: 줄무늬가 걷히는 시간(방장 행이 아니다 — 턴 넘김과 겹쳐 흐른다).</summary>
        public const int WipeBackMs = 300;
        /// <summary>인트로(53행): 첫 포켓몬이 나오는 때 · 둘이 함께 나올 때 둘째(내 포켓몬)의 때 · 미끄러져 들어오는 시간과 거리(DS px) · 체력 상자가 뒤따르는 때·시간·거리.</summary>
        public const int IntroLeadMs = 300, IntroSecondMs = 1750, IntroSlideMs = 400, IntroSlidePx = 96, IntroBoxLeadMs = 400, IntroBoxMs = 250, IntroBoxPx = 128;
        /// <summary>
        /// 인트로의 주인공 뒷모습 던지기(53행 — 결정 99 ②, 원작 Pt: 배틀 시작에 주인공 뒷모습이 서 있다가 볼을 던져 첫 포켓몬을 내보낸다) — 모두 「내 포켓몬이 드러나는 때」에서 거꾸로 잰다:
        /// 뒷모습이 들어오기 시작(드러나기 <see cref="IntroTrainerLeadMs"/> 전 — 둘이 함께 나오는 단계에서는 상대와 같은 때) · 볼이 손을 떠나 날아가는 시간 · 볼이 열리는 자리의 높이(내 발 위) ·
        /// 첫 출전을 따로 고른 항목(그 출전만 있는 인트로)에서 드러나는 때 · 손을 떠난 뒤 뒷모습이 물러나기 시작하기까지 · 볼이 열리며 포켓몬이 하얀 채 커지는 시간.
        /// </summary>
        public const int IntroTrainerLeadMs = 1450, IntroFlyMs = 360, IntroOpenAbovePx = 20, IntroPickMs = 1250, IntroLeaveLagMs = 100, IntroPopMs = 200;
        /// <summary>
        /// 인트로의 상대 트레이너 볼(53행 — M9 다듬기 P05): 원작 자산(Pt · HGSS · BW)에 상대 트레이너의 던지는 앞모습이 없어 트레이너는 화면 밖으로 물러나고 볼만 오른쪽 화면 밖에서 날아든다.
        /// 볼이 나는 시간(상대 포켓몬이 드러나는 때에서 거꾸로 — 둘이 함께 나오는 단계의 <see cref="IntroLeadMs"/>와 같아 단계 시작에 떠난다) · 볼이 나오는 자리(DS x — 화면 오른쪽 밖) · 그 높이(상대 발 위).
        /// </summary>
        public const int IntroFoeFlyMs = 300, IntroFoeFromX = 264, IntroFoeFromAbovePx = 56;
        /// <summary>갤럭시단 등장(72행): 조무래기가 들어오는 시간 · 물러나는 시간 · 거리(DS px).</summary>
        public const int FoeInMs = 500, FoeOutMs = 400, FoeSlidePx = 112;
        /// <summary>포획(68행, r5 README): 뒷모습 들어옴 · 볼이 손을 떠남 · 날아감 끝 · 빨려 듦 끝 · 튕김 끝 — 플레이 시험 T28 · 결정 126 ②로 옛 값(180 · 260 · 600 · 1000 · 1550)의 1.5배(인트로 던지기 IntroFlyMs도 240 → 360, 혼자 나오는 첫 출전 IntroPickMs 1100 → 1250).</summary>
        public const int TrainerInMs = 270, ReleaseMs = 390, FlyEndMs = 900, SuckEndMs = 1500, BounceEndMs = 2330;
        /// <summary>포획: 합계가 판정보다 앞서는 시간 · 굴림이 멈춘 뒤 세기까지의 틈 · 세기 간격의 아래·위.</summary>
        public const int SumLeadMs = 100, RollGapMs = 150, CountMinMs = 130, CountMaxMs = 250;
        /// <summary>포획: 판정 뒤 결말 대사까지(성공 · 실패) · 실패하면 뒷모습이 물러나는 시간(단계 끝에서) · 튀어나오는 시간 · 별이 하나씩 켜지는 간격.</summary>
        public const int SuccessLineMs = 350, FailLineMs = 250, LeaveMs = 250, BreakMs = 200, StarEveryMs = 100;
        /// <summary>뒷모습이 들어오고 물러나는 거리 · 볼 포물선의 높이 · 볼이 열리는 자리의 높이(상대 발 위) · 튕김 높이 둘 · 흔들림 폭(DS px).</summary>
        public const int TrainerSlidePx = 72, ArcPx = 28, OpenAbovePx = 44, RestAbovePx = 8, Bounce1Px = 12, Bounce2Px = 5, ShakePx = 2;
        /// <summary>결과(70행): 창이 뜨고 첫 줄이 내려앉기까지 · 줄마다 어긋남 · 한 줄이 내려앉는 시간과 거리(px).</summary>
        public const int ResultLeadMs = 500, ResultEveryMs = 500, ResultDropMs = 200, ResultDropPx = 8;
    }

    /// <summary>들어가고 나오는 연출의 행(04 §8-8) 가운데 그리는 것: 찾아보기 20 · 조우 카드 21 · 조우 전환 22 · 갤럭시단 등장 72. 배틀 화면 안의 것(53·68·69·70·94)은 <see cref="BattleRows"/>.</summary>
    public static class EnterRows
    {
        public const int Search = 200, Reveal = 211, Picked = 212, Wipe = 220, Galactic = 720;

        public static bool Built(int row) => row == Search || row == Reveal || row == Picked || row == Wipe || row == Galactic;

        /// <summary>그 단계에 그릴 것이 있나: 카드가 한 장도 안 나온 공개(빈 찾아보기)는 창이 없다.</summary>
        public static bool Draws(FxStep step)
        {
            if (step == null) return false;
            if (step.Row != Reveal) return true;
            foreach (var e in step.Events)
                if (e is EncounterDrawn d && d.Cards != null && d.Cards.Length > 0) return true;
            return false;
        }

        /// <summary>
        /// 대기열이 그 행의 단계에 닿았나(그 단계를 가진 항목이 재생 중이고 <paramref name="permille"/> 지점을 지났다). 그 행이 대기열에 없으면 이미 지난 것이다(true).
        /// 화면을 여는 때를 정한다 — 조우 창은 공개(21행) 시작에, 배틀 화면은 전환(22행)의 가운데에.
        /// </summary>
        public static bool Reached(FxSequencer seq, int row, int permille = 0)
        {
            if (seq == null) return true;
            foreach (var entry in seq.Queued)
            {
                var step = entry.Step(row);
                if (step == null) continue;
                return ReferenceEquals(entry, seq.Current) && seq.LocalMs >= step.StartMs + (int)((long)step.Ms * permille / 1000);
            }
            return true;
        }

        /// <summary>조우 전환(22행)의 줄무늬가 덮은 만큼(0 ~ 1): 닫히는 구간에 0 → 1, 화면이 바뀐 뒤 열리는 구간에 1 → 0.</summary>
        public static float WipeCover(FxStep step, int entryMs)
        {
            if (step == null || step.Ms <= 0 || entryMs < step.StartMs || entryMs >= step.EndMs) return 0f;
            int t = entryMs - step.StartMs;
            int At(int permille) => (int)((long)step.Ms * permille / 1000);
            if (t < At(EnterBeats.WipeOpenFrom)) return Tween.Progress(t, At(EnterBeats.WipeCloseFrom), At(EnterBeats.WipeCloseTo) - At(EnterBeats.WipeCloseFrom));
            return 1f - Tween.Progress(t, At(EnterBeats.WipeOpenFrom), At(EnterBeats.WipeOpenTo) - At(EnterBeats.WipeOpenFrom));
        }

        /// <summary>카드 공개(21행): i번째(0부터) 카드가 뒤집히기 시작하는 단계 안 시각.</summary>
        public static int FlipAt(int index) => EnterBeats.FlipLeadMs + Math.Max(0, index) * EnterBeats.FlipEveryMs;

        /// <summary>카드 한 장의 모습: 앞면이 보이나 · 가로로 늘인 만큼(뒤집히는 동안 1 → 0 → 1). stepMs = 21행 단계 안 시각(단계가 지났으면 큰 값).</summary>
        public static bool FlipFace(int index, int stepMs, out float scaleX)
        {
            int t = stepMs - FlipAt(index), half = EnterBeats.FlipMs / 2;
            if (t <= 0) { scaleX = 1f; return false; }
            if (t >= EnterBeats.FlipMs) { scaleX = 1f; return true; }
            scaleX = t < half ? 1f - Tween.Progress(t, 0, half) : Tween.Progress(t, half, EnterBeats.FlipMs - half);
            return t >= half;
        }

        /// <summary>찾아보기(20행): 그 단계 안 시각에 칸 테두리 맥박이 켜져 있나.</summary>
        public static bool PulseOn(int stepMs) =>
            stepMs >= 0 && stepMs < EnterBeats.PulseEveryMs * EnterBeats.Pulses && stepMs % EnterBeats.PulseEveryMs < EnterBeats.PulseEveryMs / 2;
    }

    /// <summary>조우 카드 한 장의 그 순간 모습(<see cref="EncounterCards"/>). 옮김 px는 화면이 곱한다.</summary>
    public struct EncounterCardPose
    {
        /// <summary>앞면이 보이나.</summary>
        public bool Face;
        /// <summary>늘이기(뒤집기 · 떠오를 때 커짐 · 닿을 때 눌림 · 쓸려 갈 때 줄어듦을 모두 곱한 것).</summary>
        public float ScaleX, ScaleY;
        /// <summary>위로 뜬 만큼(0 ~ 1 — 무거운 카드의 떠오름).</summary>
        public float Rise;
        /// <summary>덱으로 쓸려 간 만큼(0 ~ 1 — 아래로 옮김).</summary>
        public float Away;
        public float Opacity;

        public static EncounterCardPose Rest => new EncounterCardPose { Face = true, ScaleX = 1f, ScaleY = 1f, Opacity = 1f };
    }

    /// <summary>
    /// 조우 카드 연출(04 §8-8 21행 — M9 다듬기 2, 결정 74 · [가정 plan §5 #415~#417]): 공개 단계에서 가벼운 카드(약함 · 보통)는 왼쪽부터 250씩 뒤집히고,
    /// 무거운 카드(강함 · 전설)는 그 뒤에 떠올라 느리게 뒤집히고 쿵 떨어져 눌렸다 편다. 고른 뒤에는 나머지 카드가 뒷면으로 뒤집혀 아래(덱)로 쓸려 간다.
    /// 모두 대기열 시각(단계 안 ms)만 읽는다 — 「빠름」은 시계가, 「끄기」는 단계를 건너뛰어 끝 모습(장식 없음)이 된다. 마우스 오버 들림은 대기열 밖(<see cref="HoverMs"/>).
    /// </summary>
    public static class EncounterCards
    {
        /// <summary>늦게 · 무겁게 내려앉는 등급.</summary>
        public static bool Heavy(EncounterTier grade) => grade == EncounterTier.Strong || grade == EncounterTier.Legendary;

        /// <summary>닿을 때 눌리는 만큼(세로 줄어듦 · 가로 늘어남)과 떠올랐을 때 커지는 만큼.</summary>
        public const float SquashY = 0.07f, SquashX = 0.04f, RiseGrow = 0.04f, AwayShrink = 0.15f;

        /// <summary>공개 단계(EncounterDrawn)의 카드마다 무거운가 — 화면 줄과 같은 순서.</summary>
        public static bool[] HeavyOf(FxStep step)
        {
            if (step != null)
                foreach (var e in step.Events)
                    if (e is EncounterDrawn d && d.Cards != null)
                    {
                        var heavy = new bool[d.Cards.Length];
                        for (int i = 0; i < heavy.Length; i++) heavy[i] = d.Cards[i] != null && Heavy(d.Cards[i].Grade);
                        return heavy;
                    }
            return new bool[0];
        }

        /// <summary>i번째 카드가 움직이기 시작하는 공개 단계 안 시각: 가벼운 카드는 가벼운 것끼리 왼쪽부터, 무거운 카드는 가벼운 카드가 다 뒤집힌 뒤에.</summary>
        public static int StartAt(IReadOnlyList<bool> heavy, int index)
        {
            int light = 0, lightBefore = 0, heavyBefore = 0;
            for (int k = 0; k < heavy.Count; k++)
            {
                if (!heavy[k]) { light++; if (k < index) lightBefore++; }
                else if (k < index) heavyBefore++;
            }
            if (index < 0 || index >= heavy.Count || !heavy[index]) return EnterBeats.FlipLeadMs + lightBefore * EnterBeats.FlipEveryMs;
            int lightEnd = light == 0 ? EnterBeats.FlipLeadMs : EnterBeats.FlipLeadMs + (light - 1) * EnterBeats.FlipEveryMs + EnterBeats.FlipMs;
            return lightEnd + EnterBeats.HeavyLeadMs + heavyBefore * EnterBeats.HeavyEveryMs;
        }

        /// <summary>그 카드가 앞면으로 바뀌는(뒤집기 가운데) 공개 단계 안 시각 — 소리 「flip」.</summary>
        public static int FaceAt(IReadOnlyList<bool> heavy, int index) =>
            StartAt(heavy, index) + (index >= 0 && index < heavy.Count && heavy[index] ? EnterBeats.HeavyFlipFromMs + EnterBeats.HeavyFlipMs / 2 : EnterBeats.FlipMs / 2);

        /// <summary>무거운 카드가 바닥에 닿는 공개 단계 안 시각 — 소리 「thump」.</summary>
        public static int LandAt(IReadOnlyList<bool> heavy, int index) => StartAt(heavy, index) + EnterBeats.HeavyLandMs;

        /// <summary>공개 단계(21행) 안 시각의 모습. stepMs = 단계 안 시각(단계가 지났으면 큰 값).</summary>
        public static EncounterCardPose Reveal(IReadOnlyList<bool> heavy, int index, int stepMs)
        {
            var p = EncounterCardPose.Rest;
            int t = stepMs == int.MaxValue ? int.MaxValue : stepMs - StartAt(heavy, index);
            bool isHeavy = index >= 0 && index < heavy.Count && heavy[index];
            if (t <= 0) { p.Face = false; return p; }
            if (!isHeavy)
            {
                p.ScaleX = Flip(t, EnterBeats.FlipMs, out p.Face);
                return p;
            }
            int dropFrom = EnterBeats.HeavyFlipFromMs + EnterBeats.HeavyFlipMs;
            if (t < dropFrom) p.Rise = Tween.Apply(Ease.OutQuad, Tween.Progress(t, 0, EnterBeats.HeavyRiseMs));
            else if (t < EnterBeats.HeavyLandMs) p.Rise = 1f - Tween.Apply(Ease.InQuad, Tween.Progress(t, dropFrom, EnterBeats.HeavyDropMs));
            int ft = t - EnterBeats.HeavyFlipFromMs;
            float flip = ft <= 0 ? 1f : Flip(ft, EnterBeats.HeavyFlipMs, out p.Face);
            if (ft <= 0) p.Face = false;
            float grow = 1f + RiseGrow * p.Rise, s = Squash(t - EnterBeats.HeavyLandMs);
            p.ScaleX = flip * grow * (1f + SquashX * s);
            p.ScaleY = grow * (1f - SquashY * s);
            return p;
        }

        /// <summary>닿은 뒤 u ms의 눌림(1 = 가장 눌림, 음수 = 살짝 늘어남, 0 = 그대로): 한 번 눌렸다가 감쇠하며 편다.</summary>
        public static float Squash(int u)
        {
            if (u < 0 || u >= EnterBeats.HeavySquashMs) return 0f;
            double q = u / (double)EnterBeats.HeavySquashMs;
            return (float)((1.0 - q) * (1.0 - q) * Math.Cos(q * Math.PI * 2.0));
        }

        /// <summary>
        /// 고른 뒤 단계(21행 「고른 뒤 600」) 안 시각의 고르지 않은 카드 모습: 고르지 않은 것끼리 왼쪽부터 <see cref="EnterBeats.SweepEveryMs"/>씩 어긋나
        /// 뒷면으로 뒤집히고 → 아래로 미끄러지며 줄어들고 흐려진다. 고른 카드(<paramref name="picked"/>)는 그대로(<see cref="EncounterCardPose.Rest"/>).
        /// </summary>
        public static EncounterCardPose Sweep(int picked, int index, int stepMs)
        {
            var p = EncounterCardPose.Rest;
            if (index == picked || picked < 0) return p;
            int order = index < picked ? index : index - 1;
            int t = stepMs == int.MaxValue ? int.MaxValue : stepMs - order * EnterBeats.SweepEveryMs;
            if (t <= 0) return p;
            if (t < EnterBeats.SweepFlipMs)
            {
                p.ScaleX = Flip(t, EnterBeats.SweepFlipMs, out bool second);
                p.Face = !second;
                return p;
            }
            p.Face = false;
            p.Away = Tween.Apply(Ease.InQuad, Tween.Progress(t, EnterBeats.SweepFlipMs, EnterBeats.SweepSlideMs));
            p.ScaleX = p.ScaleY = 1f - AwayShrink * p.Away;
            p.Opacity = 1f - p.Away;
            return p;
        }

        /// <summary>마우스 오버 들림의 길이(그 속도에서): 보통 <see cref="EnterBeats.HoverMs"/> · 빠름 그 <see cref="FxTune.FastPercent"/> · 끄기 0(들리지 않는다 — 장식 없음).</summary>
        public static int HoverMs(FxSpeed speed) =>
            speed == FxSpeed.Off ? 0 : speed == FxSpeed.Fast ? Math.Max(1, EnterBeats.HoverMs * FxTune.FastPercent.Value / 100) : EnterBeats.HoverMs;

        /// <summary>들림이 바뀐 뒤 ms의 들린 만큼(from → to, 길이 <paramref name="lengthMs"/>, 0이면 바로 to).</summary>
        public static float Lift(float from, float to, int ms, int lengthMs) => Tween.At(ms, 0, lengthMs, from, to, Ease.OutQuad);

        // 가로 늘이기 1 → 0 → 1(가운데에서 면이 바뀐다)
        static float Flip(int t, int length, out bool second)
        {
            int half = length / 2;
            if (t >= length) { second = true; return 1f; }
            second = t >= half;
            return t < half ? 1f - Tween.Progress(t, 0, half) : Tween.Progress(t, half, length - half);
        }
    }

    /// <summary>조우 전환(22행) 줄무늬의 무늬 갈래 — 상대에 따라 다르다(M9 다듬기 P14, 결정 116).</summary>
    public enum WipeStyle { Wild, Legend, Trainer, Rift }

    /// <summary>
    /// 조우 전환(22행)의 상대별 무늬(M9 다듬기 P14 — 원작 Pt는 상대마다 전환이 다르다. 원작 자산의 field_encounteffect는 미변환이라 줄무늬 열 하나로 본뜬다 [가정 — plan §5 #500]):
    /// 야생 = 좌우에서 엇갈려 한꺼번에 닫히는 줄무늬(옛 그대로) · 전설 = 닫히기 앞의 빛 + 가운데 줄부터 번지는 마름모 빛 ·
    /// 트레이너(갤럭시단 · 도전 · 배신자) = 위에서 아래로 한 줄씩 차례로 내리닫는 블라인드 · 울트라 비스트 = 가운데에서 들쭉날쭉 찢어지는 보라 균열.
    /// 덮은 만큼(<see cref="EnterRows.WipeCover"/>)과 화면이 바뀌는 때(<see cref="EnterBeats.WipeSwapAt"/>)는 그대로다 — 방장 길이도 박자 구간도 바꾸지 않고 줄마다의 늦음만 다르다.
    /// 어느 갈래든 덮음 0이면 모든 줄이 0, 덮음 1이면 모든 줄이 1이다(화면이 바뀌는 때 가운데 필드는 다 덮여 있다).
    /// </summary>
    public static class WipeBeats
    {
        /// <summary>줄마다의 늦음이 덮음에서 차지하는 몫(천분율): 가장 늦은 줄은 덮음이 이만큼 찬 뒤에 자라기 시작한다.</summary>
        public const int LegendSpread = 500, TrainerSpread = 700, RiftSpread = 600;
        /// <summary>전설의 빛(단계 길이의 천분율): 켜지기 시작 · 가장 밝은 때 · 꺼지는 끝(닫히는 구간의 가운데) · 가장 밝을 때의 불투명도(백분율).</summary>
        public const int LightFrom = 200, LightPeak = 300, LightTo = 400, LightMax = 70;

        public static WipeStyle StyleOf(BattleKind kind)
        {
            switch (kind)
            {
                case BattleKind.Legendary: case BattleKind.God: case BattleKind.Trial: return WipeStyle.Legend;
                case BattleKind.Trainer: case BattleKind.Galactic: case BattleKind.Traitor: return WipeStyle.Trainer;
                case BattleKind.UltraBeast: case BattleKind.Necrozma: return WipeStyle.Rift;
                default: return WipeStyle.Wild;
            }
        }

        /// <summary>그 전환 단계가 연 배틀의 무늬(배틀을 연 이벤트가 없으면 야생).</summary>
        public static WipeStyle StyleOf(FxStep step)
        {
            if (step != null)
                foreach (var e in step.Events)
                    if (e is BattleStarted b) return StyleOf(b.Kind);
            return WipeStyle.Wild;
        }

        /// <summary>그 줄이 자라는 쪽(가로 고정점, 백분율): 0 = 왼쪽에서 · 100 = 오른쪽에서 · 50 = 가운데에서 양쪽으로.</summary>
        public static int Origin(WipeStyle style, int index)
        {
            switch (style)
            {
                case WipeStyle.Legend: case WipeStyle.Rift: return 50;
                case WipeStyle.Trainer: return 0;
                default: return index % 2 == 0 ? 0 : 100;
            }
        }

        /// <summary>그 줄의 늦음(0 = 가장 먼저 ~ 1 = 가장 늦게).</summary>
        public static float Delay(WipeStyle style, int index, int bars)
        {
            if (bars <= 1 || index < 0 || index >= bars) return 0f;
            switch (style)
            {
                case WipeStyle.Legend:
                {
                    // 가운데 두 줄이 먼저, 위아래 끝이 마지막(마름모)
                    float mid = (bars - 1) * 0.5f, far = Math.Abs(index - mid) - (bars % 2 == 0 ? 0.5f : 0f);
                    return far / Math.Max(1f, (float)Math.Floor(mid));
                }
                case WipeStyle.Trainer: return index / (float)(bars - 1);
                case WipeStyle.Rift: return (index * 7 + 4) % bars / (float)(bars - 1);   // 줄마다 들쭉날쭉(늘 같은 꼴)
                default: return 0f;
            }
        }

        /// <summary>그 줄을 가로로 늘인 만큼(0 ~ 1): 덮음에서 그 줄의 늦음을 뺀 나머지.</summary>
        public static float Bar(WipeStyle style, int index, int bars, float cover)
        {
            if (cover <= 0f) return 0f;
            if (cover >= 1f) return 1f;
            int spread = style == WipeStyle.Legend ? LegendSpread : style == WipeStyle.Trainer ? TrainerSpread : style == WipeStyle.Rift ? RiftSpread : 0;
            if (spread == 0) return cover;
            float s = spread / 1000f, v = (cover - Delay(style, index, bars) * s) / (1f - s);
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }

        /// <summary>전설의 빛(0 ~ 1 불투명도): 줄무늬가 닫히기 앞에 가운데 필드가 환해졌다가 줄무늬에 묻힌다. 다른 갈래는 0.</summary>
        public static float Light(WipeStyle style, FxStep step, int entryMs)
        {
            if (style != WipeStyle.Legend || step == null || step.Ms <= 0 || entryMs < step.StartMs || entryMs >= step.EndMs) return 0f;
            int t = entryMs - step.StartMs;
            int At(int permille) => (int)((long)step.Ms * permille / 1000);
            float up = Tween.Progress(t, At(LightFrom), At(LightPeak) - At(LightFrom)), down = 1f - Tween.Progress(t, At(LightPeak), At(LightTo) - At(LightPeak));
            return Math.Min(up, down) * LightMax / 100f;
        }
    }

    /// <summary>
    /// 주인공 뒷모습 「볼 던지기」 한 벌(Gen <c>Trainers/trainers.json</c>의 <c>sequence</c>가 있는 행, schema 2 — plan §4 「주인공 뒷모습 던지기」, 결정 53 ④·55 ①):
    /// 시트는 프레임 <see cref="Fw"/>×<see cref="Fh"/>를 <see cref="Cols"/>열 격자로, 순서는 [[프레임, 틱]] × <see cref="Fps"/>,
    /// <see cref="Release"/> = 볼이 손을 떠나는 틱(원작 자산), 손(<see cref="HandX"/>·<see cref="HandY"/>) = 그때 볼이 나오는 프레임 안 점(주인공마다 — 결정 61 ①),
    /// <see cref="Leave"/> = 그 손 자리를 잰 프레임이 보이는 마지막 틱(5세대 = release − 1: 든 손에 볼이 있는 마지막 프레임 · 4세대 = release: 팔을 뻗은 프레임 — 결정 62), <see cref="Still"/> = 서 있는 프레임, 원점(<see cref="OriginX"/>·<see cref="OriginY"/>) = 원작 자산이 세우는 자리(화면 고정점에 놓는 프레임 안 점).
    /// 재생은 순수 함수다: <see cref="Leave"/> 틱이 포획 시간표의 「볼이 손을 떠남」(결정 54 ① 260ms)에 오도록 그 앞을 줄이거나 늘리고, 그 뒤는 원래 빠르기(틱 = 1/fps초)로 돈다 —
    /// 볼이 나타나는 순간의 뒷모습은 손 자리를 잰 프레임이고, 한 틱 뒤에 다음 프레임으로 넘어간다.
    /// </summary>
    public sealed class BackThrow
    {
        public string File;
        public int Fw, Fh, Cols = 1, Frames, Fps = 60, Total, Release, Leave, Still, OriginX, OriginY, HandX, HandY;
        public int[] SeqFrame = new int[0], SeqTicks = new int[0];

        static readonly Regex Row = new Regex("\"file\":\"(Back/official/[^\"]+\\.png)\".*\"fw\":(\\d+),\"fh\":(\\d+),\"frames\":(\\d+),\"origin\":\\[(-?\\d+),(-?\\d+)\\].*\"cols\":(\\d+),\"fps\":(\\d+),\"total\":(\\d+),\"release\":(\\d+),\"still\":(\\d+).*\"hand\":\\[(\\d+),(\\d+)\\],\"leave\":(\\d+).*\"sequence\":\\[((?:\\[\\d+,\\d+\\],?)+)\\]");
        static readonly Regex Pair = new Regex("\\[(\\d+),(\\d+)\\]");

        /// <summary>표의 한 줄(행 하나 = 한 줄)을 읽는다. 던지기 순서가 없는 행이면 null.</summary>
        public static BackThrow Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            var m = Row.Match(line);
            if (!m.Success) return null;
            int N(int g) => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            var pairs = Pair.Matches(m.Groups[15].Value);
            var t = new BackThrow
            {
                File = m.Groups[1].Value, Fw = N(2), Fh = N(3), Frames = N(4), OriginX = N(5), OriginY = N(6), Cols = Math.Max(1, N(7)), Fps = Math.Max(1, N(8)), Total = N(9), Release = N(10), Still = N(11), HandX = N(12), HandY = N(13), Leave = N(14),
                SeqFrame = new int[pairs.Count], SeqTicks = new int[pairs.Count],
            };
            for (int i = 0; i < pairs.Count; i++)
            {
                t.SeqFrame[i] = int.Parse(pairs[i].Groups[1].Value, CultureInfo.InvariantCulture);
                t.SeqTicks[i] = int.Parse(pairs[i].Groups[2].Value, CultureInfo.InvariantCulture);
            }
            return t;
        }

        /// <summary>표 전체에서 던지기 순서가 있는 행을 모두 읽는다(표의 순서 그대로).</summary>
        public static List<BackThrow> ParseAll(string table)
        {
            var list = new List<BackThrow>();
            if (table == null) return list;
            foreach (var line in table.Split('\n'))
            {
                var t = Parse(line);
                if (t != null) list.Add(t);
            }
            return list;
        }

        /// <summary>표에서 그 주인공(키 = 시트 파일 이름, 「pt_player_male」)의 행을 찾는다. 없으면 null.</summary>
        public static BackThrow Find(string table, string key)
        {
            string file = "Back/official/" + key + ".png";
            foreach (var t in ParseAll(table)) if (t.File == file) return t;
            return null;
        }

        /// <summary>그 틱에 보이는 프레임: 순서를 틱만큼 훑는다. 순서가 끝난 뒤(<see cref="Total"/> 이상)는 서 있는 프레임, 0 앞은 첫 프레임.</summary>
        public int FrameAtTick(int tick)
        {
            if (SeqFrame.Length == 0 || tick >= Total) return Still;
            if (tick < 0) return SeqFrame[0];
            int at = 0;
            for (int i = 0; i < SeqFrame.Length; i++)
            {
                at += SeqTicks[i];
                if (tick < at) return SeqFrame[i];
            }
            return Still;
        }

        /// <summary>
        /// 던지기를 시작하고 ms 뒤의 틱: <see cref="Leave"/> 틱이 <paramref name="releaseMs"/>에 오도록 그 앞은 고르게 늘이거나 줄이고(틱 = ms × leave ÷ releaseMs),
        /// 그 뒤는 원래 빠르기(1000 ÷ fps ms에 한 틱).
        /// </summary>
        public int TickAt(int ms, int releaseMs)
        {
            if (ms <= 0) return 0;
            if (releaseMs > 0 && ms < releaseMs) return (int)((long)ms * Leave / releaseMs);
            return Leave + (int)((long)(ms - Math.Max(0, releaseMs)) * Fps / 1000);
        }

        /// <summary>던지기를 시작하고 ms 뒤의 프레임.</summary>
        public int FrameAt(int ms, int releaseMs) => FrameAtTick(TickAt(ms, releaseMs));

        /// <summary>던지기가 다 끝나는 때(ms) — 그 뒤는 서 있는 프레임이다.</summary>
        public int EndMs(int releaseMs) => Math.Max(0, releaseMs) + (int)Math.Ceiling(Math.Max(0, Total - Leave) * 1000.0 / Fps);

        /// <summary>볼이 손을 떠나는 화면 자리: 뒷모습 고정점(원점이 놓이는 화면 점)에서 원점을 빼고 손 자리를 더한다(결정 61 ①).</summary>
        public void HandAt(int anchorX, int anchorY, out int x, out int y)
        {
            x = anchorX - OriginX + HandX;
            y = anchorY - OriginY + HandY;
        }

        /// <summary>프레임의 시트 안 자리(왼쪽 위 px).</summary>
        public void Cell(int frame, out int x, out int y)
        {
            frame = Math.Max(0, Math.Min(Math.Max(0, Frames - 1), frame));
            x = frame % Cols * Fw;
            y = frame / Cols * Fh;
        }
    }

    /// <summary>포획 단계(68행)의 한 순간 — DS 화면에 그릴 것(DS px, 왼쪽 위가 (0, 0)).</summary>
    public struct CapturePose
    {
        /// <summary>주인공 뒷모습: 보이나 · 옆으로 옮긴 만큼(들어오고 물러남) · 던지기를 시작한 뒤의 시간(ms — 프레임은 <see cref="BackThrow.FrameAt"/>).</summary>
        public bool Trainer;
        public int TrainerDx, TrainerMs;
        /// <summary>볼: 보이나 · 가운데 자리 · 프레임(0~8 = 도는 프레임, −1 = 열린 볼) · 어두워졌나(성공 딸깍) · 둘레 별 수(0~3).</summary>
        public bool Ball;
        public int BallX, BallY, BallFrame;
        public bool BallDark;
        public int Stars;
        /// <summary>체력 상자 둘(결정 62): 그리지 않나 · 옆으로 물러난 만큼(DS px — 상대 것은 왼쪽, 내 것은 오른쪽으로. 인트로에서 들어온 쪽).</summary>
        public bool BoxHidden;
        public int BoxAway;
        /// <summary>대상 포켓몬: 보이나 · 하얗게 물든 만큼(0~1) · 크기(1 = 그대로, 0 = 볼 속).</summary>
        public bool Mon;
        public float MonWhite, MonScale;
    }

    /// <summary>
    /// 포획 연출(04 §8-8 68행, 결정 54 ①②, 목업 r5-fx): 볼 던지기와 포획 주사위 굴림이 단계 시작에 함께 시작하고, 흔들림이 끝나는 때(<c>PlayTiming.CaptureVerdictMs</c>) 판정이 난다.
    /// 값은 모두 단계 안 시각과 이벤트에서만 나온다 — 같은 시각이면 같은 모습이다. 플래시·화면 흔들림은 없다(결정 53 ②).
    /// 뒷모습이 화면에 있는 동안 체력 상자 둘은 없다(결정 62): 뒷모습이 들어오는 <see cref="EnterBeats.TrainerInMs"/> 동안 옆으로 물러나고, 실패해 뒷모습이 물러나는 <see cref="EnterBeats.LeaveMs"/> 동안 돌아온다. 성공하면 돌아오지 않는다.
    /// </summary>
    public static class CaptureStage
    {
        public const int Row = 680;

        public static FxStep StepOf(FxEntry entry)
        {
            var s = entry?.Step(Row);
            return s != null && s.Events.Count > 0 && s.Events[0] is CaptureAttempt ? s : null;
        }

        /// <summary>판정(성공·실패 칸 + 딸깍·튀어나옴)이 나는 단계 안 시각.</summary>
        public static int VerdictMs(CaptureAttempt c) => PlayTiming.CaptureVerdictMs(c.Shakes);

        /// <summary>결말 대사(「신난다!」·「안 돼!」)가 뜨는 단계 안 시각.</summary>
        public static int LineMs(CaptureAttempt c) => VerdictMs(c) + (c.Success ? EnterBeats.SuccessLineMs : EnterBeats.FailLineMs);

        /// <summary>흔들림이 시작하는 단계 안 시각: 판정에서 거꾸로 번당 <c>PlayTiming.CaptureShakeMs</c>(3·2번은 1650부터, 1번은 판정 바로 앞).</summary>
        public static int ShakeStartMs(CaptureAttempt c) => VerdictMs(c) - PlayTiming.CaptureShakeMs.Value * Math.Max(0, c.Shakes);

        /// <summary>결과 줄에 설 면(엔진 순서): 굴린 면에서, 상태 이상인 대상이면 첫 심볼 하나가 일반으로 바뀐 것(A7).</summary>
        public static List<AttackDieFace> FinalFaces(CaptureAttempt c)
        {
            var faces = new List<AttackDieFace>();
            if (c.Faces == null) return faces;
            faces.AddRange(c.Faces);
            int at = Converted(c);
            if (at >= 0) faces[at] = AttackDieFace.RegularHit;
            return faces;
        }

        /// <summary>면이 바뀐 주사위(엔진 순서, 없으면 −1).</summary>
        public static int Converted(CaptureAttempt c) => c.StatusConverted && c.Faces != null ? Array.IndexOf(c.Faces, AttackDieFace.StatusSymbol) : -1;

        /// <summary>
        /// 세기 간격(결정 54 ① — 뒤에서부터 맞춘다): 마지막 주사위(합계)가 판정 <see cref="EnterBeats.SumLeadMs"/> 전에 서도록,
        /// (판정 − 100 − (굴림이 멈춘 때 + 150)) ÷ (주사위 수 − 1)을 10ms 단위로 내려 130~250으로 자른 값.
        /// </summary>
        /// <param name="rollMs">굴림이 다 멈춘 때(단계 안 시각 — 녹화본 길이). 녹화본이 없으면 <c>PlayTiming.DiceRollMaxMs</c>.</param>
        public static int CountEveryMs(int verdictMs, int rollMs, int dice)
        {
            if (dice <= 1) return EnterBeats.CountMaxMs;
            int every = (verdictMs - EnterBeats.SumLeadMs - (rollMs + EnterBeats.RollGapMs)) / (dice - 1) / 10 * 10;
            return Math.Max(EnterBeats.CountMinMs, Math.Min(EnterBeats.CountMaxMs, every));
        }

        /// <summary>k번째(0부터) 주사위를 세는 단계 안 시각 — 마지막 것이 판정 100ms 전.</summary>
        public static int CountAtMs(int verdictMs, int everyMs, int dice, int k) => verdictMs - EnterBeats.SumLeadMs - (dice - 1 - k) * everyMs;

        /// <summary>그 시각까지 센 주사위 수.</summary>
        public static int Counted(int verdictMs, int everyMs, int dice, int stepMs)
        {
            int n = 0;
            for (int k = 0; k < dice; k++) if (stepMs >= CountAtMs(verdictMs, everyMs, dice, k)) n++;
            return n;
        }

        /// <summary>그 순간의 모습. t = 단계 안 시각(단계가 지난 뒤의 큰 값이면 끝난 모습 — 성공: 뒷모습과 닫힌 볼이 남고 체력 상자는 없다 · 실패: 아무것도 없고 체력 상자는 제자리).</summary>
        /// <param name="handX">볼이 손을 떠나는 자리.</param>
        /// <param name="footX">대상 포켓몬의 발 자리(발판 고정점).</param>
        public static CapturePose Pose(CaptureAttempt c, int t, int stepMs, int handX, int handY, int footX, int footY)
        {
            var p = new CapturePose { Mon = true, MonScale = 1f, BallFrame = 0 };
            if (c == null || t < 0) return p;
            int verdict = VerdictMs(c);

            // 뒷모습: 왼쪽에서 들어와 던지고, 성공하면 남고 실패하면 단계 끝에서 물러난다(결정 54 ②)
            p.Trainer = true;
            p.TrainerMs = t;
            p.TrainerDx = -(int)Math.Round(EnterBeats.TrainerSlidePx * (1f - Tween.Apply(Ease.OutQuad, Tween.Progress(t, 0, EnterBeats.TrainerInMs))));
            // 체력 상자 둘(결정 62): 뒷모습이 들어오는 동안 옆으로 물러나고, 그 뒤로는 그리지 않는다. 실패하면 뒷모습이 물러나는 동안 돌아온다(인트로와 같은 거리)
            if (t < EnterBeats.TrainerInMs) p.BoxAway = (int)Math.Round(EnterBeats.IntroBoxPx * Tween.Apply(Ease.InQuad, Tween.Progress(t, 0, EnterBeats.TrainerInMs)));
            else p.BoxHidden = true;
            if (!c.Success)
            {
                int leave = Math.Max(verdict, stepMs - EnterBeats.LeaveMs);
                if (t >= leave + EnterBeats.LeaveMs) p.Trainer = false;
                else if (t >= leave) p.TrainerDx = -(int)Math.Round(EnterBeats.TrainerSlidePx * Tween.Apply(Ease.InQuad, Tween.Progress(t, leave, EnterBeats.LeaveMs)));
                if (t >= leave)
                {
                    p.BoxHidden = false;
                    p.BoxAway = (int)Math.Round(EnterBeats.IntroBoxPx * (1f - Tween.Apply(Ease.OutQuad, Tween.Progress(t, leave, EnterBeats.LeaveMs))));
                }
            }

            int topY = footY - EnterBeats.OpenAbovePx, restY = footY - EnterBeats.RestAbovePx;
            if (t < EnterBeats.ReleaseMs) return p;
            p.Ball = true;
            p.BallX = footX;
            if (t < EnterBeats.FlyEndMs)
            {
                // 돌며 포물선
                float u = Tween.Progress(t, EnterBeats.ReleaseMs, EnterBeats.FlyEndMs - EnterBeats.ReleaseMs);
                p.BallX = (int)Math.Round(handX + (footX - handX) * u);
                p.BallY = (int)Math.Round(handY + (topY - handY) * u - EnterBeats.ArcPx * 4f * u * (1f - u));
                p.BallFrame = (t - EnterBeats.ReleaseMs) / 40 % 8;
                return p;
            }
            if (t < EnterBeats.SuckEndMs)
            {
                // 열리며 포켓몬이 하얗게 빨려 든다
                float u = Tween.Progress(t, EnterBeats.FlyEndMs, EnterBeats.SuckEndMs - EnterBeats.FlyEndMs);
                p.BallY = topY;
                p.BallFrame = -1;
                p.MonWhite = 1f;
                p.MonScale = 1f - Tween.Apply(Ease.InQuad, u);
                p.Mon = p.MonScale > 0.05f;
                return p;
            }
            p.Mon = false;
            p.MonScale = 0f;
            if (t < EnterBeats.BounceEndMs)
            {
                // 떨어져 두 번 튕김(절반 · 3할 · 2할)
                int span = EnterBeats.BounceEndMs - EnterBeats.SuckEndMs, fall = span / 2, b1 = span * 3 / 10, b2 = span - fall - b1, u = t - EnterBeats.SuckEndMs;
                if (u < fall) p.BallY = (int)Math.Round(topY + (restY - topY) * Tween.Apply(Ease.InQuad, u / (float)fall));
                else if (u < fall + b1) { float v = (u - fall) / (float)b1; p.BallY = restY - (int)Math.Round(EnterBeats.Bounce1Px * 4f * v * (1f - v)); p.BallFrame = v < 0.15f ? 8 : 0; }
                else { float v = (u - fall - b1) / (float)b2; p.BallY = restY - (int)Math.Round(EnterBeats.Bounce2Px * 4f * v * (1f - v)); }
                return p;
            }
            p.BallY = restY;
            if (t < verdict)
            {
                // 흔들림: 번마다 왼쪽 → 가운데 → 오른쪽 → 가운데
                int start = ShakeStartMs(c), per = Math.Max(1, PlayTiming.CaptureShakeMs.Value);
                if (t >= start)
                {
                    int q = (t - start) % per * 4 / per;
                    p.BallX = footX + (q == 0 ? -EnterBeats.ShakePx : q == 2 ? EnterBeats.ShakePx : 0);
                }
                return p;
            }
            if (c.Success)
            {
                // 딸깍: 볼이 어두워지고 별 셋이 하나씩
                p.BallDark = true;
                int since = t - verdict;
                p.Stars = since < EnterBeats.StarEveryMs * 5 ? Math.Min(3, since / EnterBeats.StarEveryMs + 1) : 0;
                return p;
            }
            // 튀어나옴: 볼이 열리고 포켓몬이 하얀 채 커지며 돌아온다
            {
                int since = t - verdict;
                p.Mon = true;
                if (since < EnterBeats.BreakMs)
                {
                    p.BallFrame = -1;
                    p.MonScale = Tween.Progress(since, 0, EnterBeats.BreakMs);
                    p.MonWhite = 1f - p.MonScale;
                    p.Mon = p.MonScale > 0.05f;
                }
                else
                {
                    p.Ball = false;
                    p.MonScale = 1f;
                }
                return p;
            }
        }
    }

    /// <summary>인트로의 첫 출전(53행)의 한 순간 — 주인공 뒷모습 · 볼 · 볼에서 나오는 포켓몬(DS px).</summary>
    public struct IntroPose
    {
        /// <summary>주인공 뒷모습: 보이나 · 옆으로 옮긴 만큼(들어오고 물러남) · 던지기를 시작한 뒤의 시간(ms — 음수면 서 있는 프레임).</summary>
        public bool Trainer;
        public int TrainerDx, ThrowMs;
        /// <summary>볼: 보이나 · 가운데 자리 · 프레임(0~7 = 도는 프레임, −1 = 열린 볼).</summary>
        public bool Ball;
        public int BallX, BallY, BallFrame;
        /// <summary>내 포켓몬: 하얗게 물든 만큼(0~1) · 크기(1 = 그대로 — 볼에서 커지며 나온다).</summary>
        public float MonWhite, MonScale;
    }

    /// <summary>
    /// 배틀 시작의 첫 출전(04 §8-8 53행 — 결정 99 ②): 원작 Pt는 배틀이 시작될 때 주인공 뒷모습이 발판과 함께 들어와 서 있다가 던지기 동작(NANR 순서 1 — <see cref="BackThrow"/>)으로 볼을 던지고,
    /// 볼이 열리며 첫 포켓몬이 나온 뒤 뒷모습은 왼쪽으로 물러난다(pret pokeplatinum <c>battle_display.c</c> Task_SetTrainerEncounter → Task_ThrowTrainerBall, 야생전도 같다).
    /// 배틀 중 교체·쓰러진 뒤의 다음 포켓몬에는 주인공이 나오지 않는다. 값은 「그 포켓몬이 드러난 뒤의 시간」만으로 정해진다 — 같은 시각이면 같은 모습.
    /// 박자(드러나는 때 = 0): −lead 뒷모습이 들어옴(<see cref="EnterBeats.IntroSlideMs"/>) → −(260 + 240) 던지기 시작 → −240 볼이 손을 떠남 → 0 볼이 열리고 포켓몬이 하얀 채 커짐(200) · 손을 떠난 100 뒤부터 뒷모습이 물러남(250).
    /// </summary>
    public static class IntroStage
    {
        /// <summary>던지기를 시작하는 때(드러나는 때에서 거꾸로).</summary>
        public const int ThrowLeadMs = EnterBeats.ReleaseMs + EnterBeats.IntroFlyMs;

        /// <summary>
        /// 그 항목의 인트로 단계에 든 출전이 「배틀 시작의 첫 출전」인가: 배틀을 연 항목(<see cref="BattleStarted"/>·<see cref="GalacticBattle"/>)이거나 첫 출전을 고른 항목(<see cref="ActionKind.SendIn"/>)이고,
        /// 좌석의 포켓몬이다(야생·울트라 비스트·갤럭시단은 주인공이 던지지 않는다). 쓰러진 뒤의 다음 포켓몬(<see cref="ActionKind.NextPokemon"/>)·교체는 아니다.
        /// </summary>
        public static bool Thrown(FxEntry entry, FxStep step, SentIn si)
        {
            if (entry == null || step == null || si == null || step.Row != FxRows.Intro || si.Seat < 0) return false;
            if (entry.Kind == ActionKind.SendIn) return true;
            foreach (var e in entry.Events)
                if (e is BattleStarted || e is GalacticBattle) return true;
            return false;
        }

        /// <summary>
        /// 그 출전이 「상대 트레이너가 던진 볼에서 나오는 첫 출전」인가(M9 다듬기 P05): 배틀을 연 항목이거나 첫 출전을 고른 항목의 인트로 단계이고, 갤럭시단이나 좌석(도전 · 트레이너전 · 배신자)의 포켓몬이다.
        /// 야생 · 울트라 비스트는 볼에서 나오지 않는다. 화면의 위쪽(상대 자리)에 그리는 쪽에만 쓴다.
        /// </summary>
        public static bool FoeThrown(FxEntry entry, FxStep step, SentIn si)
        {
            if (entry == null || step == null || si == null || step.Row != FxRows.Intro || !(si.Seat >= 0 || si.Seat == SeatId.Galactic)) return false;
            if (entry.Kind == ActionKind.SendIn) return true;
            foreach (var e in entry.Events)
                if (e is BattleStarted || e is GalacticBattle) return true;
            return false;
        }

        /// <summary>
        /// 상대 트레이너의 볼(M9 다듬기 P05)의 그 순간. u = 상대 포켓몬이 드러난 뒤의 시간, lead = 인트로 단계가 시작해서 드러나기까지(볼은 단계가 시작하기 전에는 없다).
        /// 볼이 오른쪽 화면 밖에서 포물선으로 날아와(<see cref="EnterBeats.IntroFoeFlyMs"/>) 상대 발 위에서 열리고, 포켓몬이 하얀 채 커진다 — 내 쪽 볼 열림(<see cref="Pose"/>)과 같은 꼴. 트레이너 그림은 없다.
        /// </summary>
        public static IntroPose FoePose(int u, int lead, int footX, int footY)
        {
            var p = new IntroPose { MonScale = 1f, ThrowMs = -1 };
            int fly = Math.Min(EnterBeats.IntroFoeFlyMs, Math.Max(0, lead));
            int fromY = footY - EnterBeats.IntroFoeFromAbovePx, topY = footY - EnterBeats.OpenAbovePx;
            if (u < 0)
            {
                p.MonScale = 0f;   // 아직 볼 속
                if (u < -fly) return p;
                float v = Tween.Progress(u, -fly, fly);
                p.Ball = true;
                p.BallX = (int)Math.Round(EnterBeats.IntroFoeFromX + (footX - EnterBeats.IntroFoeFromX) * v);
                p.BallY = (int)Math.Round(fromY + (topY - fromY) * v - EnterBeats.ArcPx * 4f * v * (1f - v));
                p.BallFrame = (u + fly) / 40 % 8;
            }
            else if (u < EnterBeats.IntroPopMs)
            {
                p.Ball = true;
                p.BallX = footX;
                p.BallY = topY;
                p.BallFrame = -1;
                p.MonScale = Math.Max(0.05f, Tween.Progress(u, 0, EnterBeats.IntroPopMs));
                p.MonWhite = 1f - Tween.Progress(u, 0, EnterBeats.IntroPopMs);
            }
            return p;
        }

        /// <summary>그 순간의 모습. u = 내 포켓몬이 드러난 뒤의 시간(음수 = 드러나기 전), lead = 인트로 단계가 시작해서 드러나기까지(뒷모습은 단계가 시작하기 전에는 없다).</summary>
        /// <param name="handX">볼이 손을 떠나는 자리.</param>
        /// <param name="footX">내 포켓몬의 발 자리(발판 고정점).</param>
        /// <param name="standing">뒷모습이 단계 앞부터 서 있었다(결정 143 ⑤ — 첫 출전을 고르는 동안 서 있던 주인공): 들어오지 않고 단계 처음부터 서 있다가 던진다.</param>
        public static IntroPose Pose(int u, int lead, int handX, int handY, int footX, int footY, bool standing = false)
        {
            var p = new IntroPose { MonScale = 1f, ThrowMs = -1 };
            int enter = standing ? -Math.Max(0, lead) : -Math.Min(Math.Max(0, lead), EnterBeats.IntroTrainerLeadMs);
            int release = -EnterBeats.IntroFlyMs, leave = release + EnterBeats.IntroLeaveLagMs;
            if (u >= enter && u < leave + EnterBeats.LeaveMs)
            {
                p.Trainer = true;
                p.ThrowMs = u + ThrowLeadMs;
                int slide = standing ? 0 : Math.Min(EnterBeats.IntroSlideMs, -ThrowLeadMs - enter);   // 던지기 전에 다 들어와 선다(틈이 없으면 처음부터 서 있다)
                if (slide > 0 && u < enter + slide) p.TrainerDx = -(int)Math.Round(EnterBeats.TrainerSlidePx * (1f - Tween.Apply(Ease.OutQuad, Tween.Progress(u, enter, slide))));
                else if (u >= leave) p.TrainerDx = -(int)Math.Round(EnterBeats.TrainerSlidePx * Tween.Apply(Ease.InQuad, Tween.Progress(u, leave, EnterBeats.LeaveMs)));
            }
            int topY = footY - EnterBeats.IntroOpenAbovePx;
            if (u >= release && u < 0)
            {
                float v = Tween.Progress(u, release, EnterBeats.IntroFlyMs);
                p.Ball = true;
                p.BallX = (int)Math.Round(handX + (footX - handX) * v);
                p.BallY = (int)Math.Round(handY + (topY - handY) * v - EnterBeats.ArcPx * 4f * v * (1f - v));
                p.BallFrame = (u - release) / 40 % 8;
            }
            else if (u >= 0 && u < EnterBeats.IntroPopMs)
            {
                p.Ball = true;
                p.BallX = footX;
                p.BallY = topY;
                p.BallFrame = -1;
                p.MonScale = Math.Max(0.05f, Tween.Progress(u, 0, EnterBeats.IntroPopMs));
                p.MonWhite = 1f - Tween.Progress(u, 0, EnterBeats.IntroPopMs);
            }
            return p;
        }
    }

    /// <summary>결과 창(70·94행)의 줄 박자: 받은 것이 하나씩 내려앉는다.</summary>
    public static class ResultStage
    {
        /// <summary>k번째(0부터) 줄이 내려앉기 시작하는 때(결과 단계 안 시각).</summary>
        public static int LineAtMs(int k) => EnterBeats.ResultLeadMs + Math.Max(0, k) * EnterBeats.ResultEveryMs;

        /// <summary>그 시각에 내려앉기 시작한 줄 수(전체 <paramref name="lines"/>줄 가운데).</summary>
        public static int Landed(int lines, int stepMs)
        {
            int n = 0;
            for (int k = 0; k < lines; k++) if (stepMs >= LineAtMs(k)) n++;
            return n;
        }
    }
}
