using System;
using Rekkon.UiKit;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 2막 이후·종막의 연출 행(04 §8-8 94·95·96·97행, M5 「막·종막 화면」): 심장 회복(94행 3막 — 보드) · 종막 선택 · 배신자 순서 공개 · 게임 끝과 결과.
    /// 94행 2막(조각·파티 칸)과 막 목표는 배틀 결과 창의 줄이다(940 — <see cref="BattleRows"/>). 길이는 방장과 같은 <c>PlayTiming</c>(943 2500 · 950 2800 · 960 1500 · 970 8000)이고,
    /// 여기 값은 그 안에서 화면이 나누는 박자다 [가정 — plan §5 #96].
    /// </summary>
    public static class FinaleRows
    {
        public const int Heart = 943, Ending = 950, Order = 960, Result = 970;

        public static bool Built(int row) => row == Heart || row == Ending || row == Order || row == Result;
    }

    /// <summary>심장 회복 한 순간(<see cref="HeartBeats.At"/>): 조각 셋의 원 돌기(각도 · 반지름 비 0~1 · 보이기) · 심장(보이기 · 크기 배수).</summary>
    public struct HeartPose
    {
        public float Angle, Radius, Shards, Heart, HeartScale;
    }

    /// <summary>
    /// 94행 3막(시작의 사당 공물): 조각 셋(감정 분홍 · 지식 노랑 · 의지 파랑)이 사당 위에서 원을 그리며 좁혀 들고, 한 점에 모이면 심장이 톡 커졌다 자리 잡고, 끝에 걷힌다.
    /// </summary>
    public static class HeartBeats
    {
        /// <summary>조각이 나타나는 시간 · 모이는 때(길이의 천분율) · 심장이 커졌다 줄어드는 시간 · 걷히는 시간.</summary>
        public const int InMs = 250, MergePermille = 560, PopMs = 300, OutMs = 300;
        /// <summary>한 바퀴 반(라디안) — 모일 때까지 도는 양.</summary>
        public const float Turns = 3f * (float)Math.PI;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(FinaleRows.Heart) : null;

        public static HeartPose At(int ms, int total)
        {
            var p = new HeartPose { HeartScale = 1f };
            if (total <= 0 || ms < 0 || ms >= total) return p;
            int merge = (int)((long)total * MergePermille / 1000);
            float gone = Tween.Progress(ms, total - OutMs, OutMs);
            float spin = Tween.Progress(ms, 0, merge);
            p.Angle = Turns * Tween.Apply(Ease.InQuad, spin);
            p.Radius = 1f - Tween.Apply(Ease.InQuad, spin);
            p.Shards = ms < merge ? Tween.Progress(ms, 0, InMs) : 0f;
            p.Heart = ms >= merge ? 1f - gone : 0f;
            p.HeartScale = ms >= merge ? Tween.At(ms, merge, PopMs, 1.8f, 1f, Ease.OutQuad) : 1f;
            return p;
        }
    }

    /// <summary>종막 선택 한 순간(<see cref="EndingBeats.At"/>): 두 카드가 들어온 정도 · 고르지 않은 카드가 타 들어간 정도(0~1) · 붉은 물듦(배신자) · 걷힘.</summary>
    public struct EndingPose
    {
        public float In, Burn, Red, Fade;
    }

    /// <summary>
    /// 95행 종막 선택: 두 카드(영웅 · 배신자)가 양쪽에서 들어와 놓이고(묻는 동안은 놓인 채) → 고르면 고르지 않은 카드가 가장자리부터 타 버리고 →
    /// 배신자면 화면이 붉게 물든다 → 걷힌다. 묻는 동안의 모습 = <see cref="Asking"/>.
    /// </summary>
    public static class EndingBeats
    {
        public const int BurnFromMs = 300, BurnMs = 1300, RedFromMs = 1500, RedMs = 700, OutMs = 400;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(FinaleRows.Ending) : null;

        /// <summary>그 단계의 고른 답(없으면 null).</summary>
        public static EndingChosen Chosen(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is EndingChosen c) return c;
            return null;
        }

        public static EndingPose Asking => new EndingPose { In = 1f };

        public static EndingPose At(int ms, int total, bool traitor)
        {
            var p = new EndingPose { In = 1f };
            if (total <= 0 || ms < 0) return p;
            if (ms >= total) return new EndingPose { In = 1f, Burn = 1f, Red = traitor ? 1f : 0f, Fade = 1f };
            p.Burn = Tween.Apply(Ease.InQuad, Tween.Progress(ms, BurnFromMs, BurnMs));
            p.Red = traitor ? Tween.Progress(ms, RedFromMs, RedMs) : 0f;
            p.Fade = Tween.Progress(ms, total - OutMs, OutMs);
            return p;
        }
    }

    /// <summary>
    /// 96행 배신자 순서 공개: 배신자가 고른 상대가 칩 줄로 하나씩 놓인다(앞 300 · 끝 200을 뺀 시간을 상대 수로 나눈 간격).
    /// 이어지는 전원 회복은 8·17행의 회복(80)이 그리고, 연전은 배틀 화면이 그린다.
    /// </summary>
    public static class OrderBeats
    {
        public const int LeadMs = 300, TailMs = 200, PopMs = 180;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(FinaleRows.Order) : null;

        public static TraitorOrderSet Order(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is TraitorOrderSet o) return o;
            return null;
        }

        /// <summary>n개 가운데 i번째 칩이 놓이기 시작하는 단계 안 시각.</summary>
        public static int ChipAt(int total, int i, int n) => n <= 0 ? LeadMs : LeadMs + (Math.Max(0, total - LeadMs - TailMs) * i) / n;

        /// <summary>그 칩의 보이기(0~1).</summary>
        public static float Chip(int ms, int total, int i, int n) => Tween.Progress(ms, ChipAt(total, i, n), PopMs);
    }

    /// <summary>게임 끝 한 순간(<see cref="ResultBeats.At"/>): 판이 어두워진 정도 · 제목 · 보인 포켓몬 수 · 통계.</summary>
    public struct ResultPose
    {
        public float Dim, Title, Stats;
        public int Mons;
        /// <summary>지금 막 나온 포켓몬의 커짐(1 = 제 크기).</summary>
        public float Pop;
    }

    /// <summary>
    /// 97행 게임 끝과 결과(S64): 승리 좌석의 말로 카메라가 다가가는 동안 판이 어두워지고 → 제목 → 파티 포켓몬이 하나씩(전당 등록 리듬) → 통계.
    /// 단계가 끝나면 결과 창의 단추가 켜진다.
    /// </summary>
    public static class ResultBeats
    {
        public const int DimMs = 900, TitleAtMs = 900, TitleMs = 400, MonsAtMs = 1600, MonEveryMs = 700, PopMs = 300, StatsGapMs = 400, StatsMs = 400;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(FinaleRows.Result) : null;

        public static GameEnded Ended(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is GameEnded g) return g;
            return null;
        }

        /// <summary>i번째 포켓몬이 나오는 때.</summary>
        public static int MonAt(int i) => MonsAtMs + MonEveryMs * i;

        /// <summary>통계가 뜨는 때(포켓몬 mons마리 뒤).</summary>
        public static int StatsAt(int mons) => MonAt(Math.Max(0, mons)) + StatsGapMs;

        /// <summary>다 보인 모습(단계 밖 — 다 재생됐거나 이어 받은 판).</summary>
        public static ResultPose Done(int mons) => new ResultPose { Dim = 1f, Title = 1f, Stats = 1f, Mons = mons, Pop = 1f };

        public static ResultPose At(int ms, int total, int mons)
        {
            if (total <= 0 || ms >= total) return Done(mons);
            var p = new ResultPose { Pop = 1f };
            if (ms < 0) return p;
            p.Dim = Tween.Progress(ms, 0, DimMs);
            p.Title = Tween.Progress(ms, TitleAtMs, TitleMs);
            int k = 0;
            while (k < mons && ms >= MonAt(k)) k++;
            p.Mons = k;
            if (k > 0) p.Pop = Tween.At(ms, MonAt(k - 1), PopMs, 1.4f, 1f, Ease.OutQuad);
            p.Stats = Tween.Progress(ms, StatsAt(mons), StatsMs);
            return p;
        }
    }

    /// <summary>
    /// 72행 갤럭시단 라인업 공개(S54 — 결정 74): 상대 트레이너가 들어온 뒤 명단의 볼이 하나씩 탁탁 놓인다(간부 = 조각 수 + 2 · 태홍 6). 갤럭시단 등장 단계(720) 안의 박자다.
    /// </summary>
    public static class LineupBeats
    {
        public const int LeadMs = 700, EveryMs = 200, DropMs = 120, DropPx = 6;

        /// <summary>그 단계의 명단 공개(없으면 null).</summary>
        public static GalacticBattle Lineup(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is GalacticBattle g && g.LineupCount > 0) return g;
            return null;
        }

        /// <summary>i번째 볼이 놓이기 시작하는 단계 안 시각.</summary>
        public static int BallAt(int i) => LeadMs + EveryMs * i;

        /// <summary>그 볼이 위에서 떨어지는 남은 거리 비(1 = 막 나타남 · 0 = 놓임). 아직이면 −1.</summary>
        public static float Drop(int ms, int i)
        {
            int at = BallAt(i);
            if (ms < at) return -1f;
            return 1f - Tween.Apply(Ease.OutQuad, Tween.Progress(ms, at, DropMs));
        }
    }
}
