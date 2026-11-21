using System;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>보드의 공간 일그러짐 한 순간: 가운데(<see cref="Cell"/> — 칸, <see cref="RippleBeats.WholeWorld"/>면 깨어진 세계 전체) · 진폭(칸 반지름의 몫) · 닿는 거리(칸 수) · 위상(0 ~ 1).</summary>
    public struct RipplePose
    {
        public int Cell;
        public float Amp, Cells, Phase;
        public bool On => Amp > 0f;
    }

    /// <summary>
    /// 공간이 일그러지는 효과(M9 다듬기 P39 — 결정 116)의 순수 박자. 새 행 · 소리 줄을 만들지 않는다 — 이미 그리는 행 안의 한때이거나(830 · 341 · 440 · 22행) 대기열 밖의 잔물결이다.
    /// <list type="bullet">
    /// <item>보드(<see cref="Board"/> — 화면은 보드 셰이더의 꼭짓점을 물결로 민다): 깨어진 세계가 보이는 판은 그 둘레가 늘 잔잔히 일렁이고(<see cref="Ambient"/>),
    /// 칸이 부서지는 단계(83행 830)는 그 칸 둘레가 단계 길이 동안 솟았다 가라앉고, 신 강림(34행 341)은 첫 번개부터 착지 고리가 끝날 때까지 사당 둘레가 크게 일렁이다 잦아든다.</item>
    /// <item>DS 층(<see cref="Ds"/> — 화면은 다 그린 DS 그림을 줄마다 가로로 민다, px): 기라티나의 그림자(44행 440)가 덮인 만큼 · 울트라 비스트 · 네크로즈마 배틀의 전환(22행)에서 화면이 바뀐 때부터 잦아든다.</item>
    /// </list>
    /// 위상은 대기열 시계에서만 나온다(같은 시각이면 같은 그림 — 「빠름」이면 함께 빨라진다). 「끄기」에서는 화면이 부르지 않는다(장식 없음).
    /// </summary>
    public static class RippleBeats
    {
        public const int WholeWorld = -1;
        /// <summary>보드: 한 바퀴(ms) · 깨어진 세계의 잔물결 · 터질 때의 진폭(칸 반지름의 몫) · 터질 때 닿는 거리(칸) · 깨어진 세계 가장자리 밖으로 더 닿는 거리(칸) · 물결 길이(칸).</summary>
        public const int PeriodMs = 2400;
        public const float Ambient = 0.07f, Burst = 0.24f, BurstCells = 3.5f, WorldMarginCells = 1.5f, WaveCells = 3f;
        /// <summary>DS: 한 바퀴(ms) · 기라티나 그림자의 진폭(px) · 균열 전환의 진폭(px) · 물결 길이(줄) · 전환 단계가 끝난 뒤 잦아드는 꼬리(ms) · 그대로 두는 아래쪽 줄(메시지 창).</summary>
        public const int DsPeriodMs = 900, DsWaveRows = 32, RiftTailMs = 600, DsKeepRows = 48;
        public const float DsShadowPx = 3f, DsRiftPx = 5f;

        /// <summary>그 시계의 위상(0 ~ 1).</summary>
        public static float Phase(long nowMs, int periodMs) => periodMs <= 0 ? 0f : (nowMs % periodMs + periodMs) % periodMs / (float)periodMs;

        static T First<T>(FxStep step) where T : GameEvent
        {
            foreach (var e in step.Events) if (e is T t) return t;
            return null;
        }

        /// <summary>보드의 일그러짐. <paramref name="worldShown"/> = 깨어진 세계가 보이는 판인가(울트라 버스트).</summary>
        public static RipplePose Board(FxEntry cur, int localMs, long nowMs, bool worldShown)
        {
            var p = new RipplePose { Cell = WholeWorld, Phase = Phase(nowMs, PeriodMs) };
            if (cur != null)
                foreach (var step in cur.Steps)
                {
                    int t = localMs - step.StartMs;
                    if (t < 0 || t >= step.Ms) continue;
                    if (step.Row == UbRows.Destroy && First<CellDestroyed>(step) is CellDestroyed d && d.Cell >= 0)
                    {
                        // 칸이 부서진다: 단계 길이 동안 솟았다 가라앉는다
                        p.Cell = d.Cell;
                        p.Amp = Burst * (float)Math.Sin(Math.PI * t / step.Ms);
                        p.Cells = BurstCells;
                        return p;
                    }
                    if (step.Row == DescendBeats.Row && First<GodDescended>(step) is GodDescended g && g.Cell >= 0)
                    {
                        // 신 강림: 첫 번개(공간 균열)에 터져 착지 고리가 끝날 때까지 잦아든다
                        int from = DescendBeats.BoltAt, to = DescendBeats.LandAt + DescendBeats.RingMs;
                        if (t < from || t >= to) continue;
                        p.Cell = g.Cell;
                        p.Amp = Burst * Math.Min(1f, (t - from) / 120f) * (1f - (t - from) / (float)(to - from));
                        p.Cells = BurstCells;
                        return p;
                    }
                }
            if (!worldShown) return p;
            p.Amp = Ambient;
            p.Cells = WorldMarginCells;
            return p;
        }

        /// <summary>DS 화면을 줄마다 미는 진폭(px, 0 = 없음).</summary>
        public static float Ds(FxEntry cur, int localMs)
        {
            if (cur == null) return 0f;
            float px = 0f;
            var shadow = SacrificeBeats.StepOf(cur);
            if (shadow != null && localMs >= shadow.StartMs && localMs < shadow.EndMs)
                px = Math.Max(px, DsShadowPx * SacrificeBeats.At(localMs - shadow.StartMs, shadow.Ms).Shadow);
            var wipe = cur.Step(EnterRows.Wipe);
            if (wipe != null && WipeBeats.StyleOf(wipe) == WipeStyle.Rift)
            {
                int from = wipe.StartMs + (int)((long)wipe.Ms * EnterBeats.WipeSwapAt / 1000), to = wipe.EndMs + RiftTailMs;
                if (localMs >= from && localMs < to) px = Math.Max(px, DsRiftPx * (1f - (localMs - from) / (float)(to - from)));
            }
            return px;
        }
    }
}
