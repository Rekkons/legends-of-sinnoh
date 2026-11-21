using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 울트라 버스트의 탈락·부활 연출 행(04 §8-8 91행 — M6 「UB 화면 묶음 2」 S61): 네크로즈마에게 져서 탈락(<see cref="Eliminated"/>) · 아르세우스의 축복으로 부활(<see cref="Revived"/>).
    /// 둘 다 910 단계 하나(좌석마다) — 길이는 방장과 같은 <c>PlayTiming</c>(1500)이고 여기 값은 그 안에서 화면이 나누는 박자다 [가정 — plan §5 #161].
    /// 그리는 곳: 보드의 말(<c>BoardPieces.Grey</c> — 회색으로 바램 · 휘청) · 위 띠 좌석 칩(회색 + 「탈락」 꼬리표 · 금) · 부활의 금빛 고리(HUD 층).
    /// UB 턴 건너뛰기의 금빛 봉인 · 축복 +1(93행)은 위 띠 균열·축복 칩(UB 화면 묶음 1)에 얹는 몫이라 여기서는 단계가 없다(기록 줄만).
    /// </summary>
    public static class FallRows
    {
        public const int Row = 910;

        /// <summary>화면이 그리는 행.</summary>
        public static bool Built(int row) => row == Row;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(Row) : null;

        /// <summary>그 단계의 첫 탈락·부활 이벤트(없으면 null).</summary>
        public static GameEvent Event(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is Eliminated || e is Revived) return e;
            return null;
        }
    }

    /// <summary>
    /// 91행의 박자. 탈락 = 예비: 말이 한 번 휘청(앞 <see cref="SwayPermille"/>) → 움직임: 회색으로 바래며 반투명(~<see cref="FadePermille"/>) → 맞음: 좌석 칩에 금(<see cref="CrackPermille"/>)
    /// → 착지: 회색 말·칩이 상태로 남음. 부활 = 금빛 고리가 말 위에서 내려오고(~<see cref="RingPermille"/>) 말·칩이 색을 되찾음(~<see cref="ColourPermille"/>) — 말은 이미 고향 마을 칸에 서 있다(보기 값).
    /// </summary>
    public static class FallBeats
    {
        public const int SwayPermille = 200, FadePermille = 650, CrackPermille = 650, RingPermille = 550, ColourPermille = 850;
        /// <summary>휘청이는 폭(보드 유닛 — 옆으로).</summary>
        public const float SwayUnits = 0.18f;

        public struct Pose
        {
            /// <summary>회색 정도(0 = 제 색 · 1 = 탈락 상태).</summary>
            public float Grey;
            /// <summary>옆으로 휘청(−1~1 — <see cref="SwayUnits"/>배).</summary>
            public float Sway;
            /// <summary>칩의 금이 보이나.</summary>
            public bool Crack;
            /// <summary>부활의 금빛 고리: 내려온 정도(0 = 말 위 높은 곳 · 1 = 발밑)와 진하기(0 = 없음).</summary>
            public float Ring, RingAlpha;
        }

        /// <summary>탈락 단계의 ms째(total = 단계 길이).</summary>
        public static Pose Out(int ms, int total)
        {
            if (total <= 0 || ms >= total) return new Pose { Grey = 1f, Crack = true };
            int sway = total * SwayPermille / 1000, fade = total * FadePermille / 1000;
            var p = new Pose();
            if (ms < sway) p.Sway = (float)Math.Sin(Tween.Progress(ms, 0, sway) * Math.PI * 2);
            else p.Grey = Tween.Apply(Ease.OutQuad, Tween.Progress(ms, sway, fade - sway));
            p.Crack = ms >= total * CrackPermille / 1000;
            return p;
        }

        /// <summary>부활 단계의 ms째.</summary>
        public static Pose Back(int ms, int total)
        {
            if (total <= 0 || ms >= total) return new Pose();
            int ring = total * RingPermille / 1000, colour = total * ColourPermille / 1000;
            var p = new Pose { Grey = 1f, Crack = true };
            if (ms < ring)
            {
                float t = Tween.Progress(ms, 0, ring);
                p.Ring = Tween.Apply(Ease.OutQuad, t);
                p.RingAlpha = Math.Min(1f, t * 3f);
                return p;
            }
            float c = Tween.Progress(ms, ring, colour - ring);
            p.Ring = 1f;
            p.RingAlpha = 1f - Tween.Progress(ms, ring, total - ring);
            p.Grey = 1f - Tween.Apply(Ease.InOutQuad, c);
            p.Crack = c < 0.5f;
            return p;
        }

        /// <summary>
        /// 그 좌석이 지금 보이는 모습: 대기열에서 아직 끝나지 않은 그 좌석의 첫 91행 단계가 있으면 그 앞 상태(탈락 단계면 제 색 · 부활 단계면 회색)이거나 그 단계 안의 자세,
        /// 없으면 보기 값(<paramref name="eliminated"/>) 그대로. 보기는 이미 끝 값이라 남은 단계로 되돌려 센다(UbBoard.Of와 같은 꼴).
        /// </summary>
        public static Pose Shown(FxSequencer seq, int seat, bool eliminated)
        {
            if (seq != null)
                foreach (var entry in seq.Queued)
                    foreach (var s in entry.Steps)
                    {
                        if (s.Row != FallRows.Row) continue;
                        var e = FallRows.Event(s);
                        if (e == null || e.Seat != seat) continue;
                        bool current = ReferenceEquals(entry, seq.Current);
                        if (current && seq.LocalMs >= s.EndMs) continue;
                        int ms = current ? seq.LocalMs - s.StartMs : -1;
                        bool fall = e is Eliminated;
                        if (ms < 0) return fall ? new Pose() : new Pose { Grey = 1f, Crack = true };
                        return fall ? Out(ms, s.Ms) : Back(ms, s.Ms);
                    }
            return eliminated ? new Pose { Grey = 1f, Crack = true } : new Pose();
        }
    }
}
