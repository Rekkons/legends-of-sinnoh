using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 울트라 버스트의 보드 쪽 연출 행(04 §8-8 15·82·83행 — M6 「UB 보드 화면」의 기본 몫): 왜곡 게이트 이동(150 — 15행) · UB 한 칸 이동(820 — 82행, 이동마다 단계 하나) ·
    /// UB가 밟은 칸의 파괴(830 — 83행, 파괴마다 단계 하나 — 네크로즈마의 원격 파괴(84행)도 칸마다 이 박자로 마무리한다).
    /// 길이는 방장과 같은 <c>PlayTiming</c>이고 여기 값은 그 안에서 화면이 나누는 박자다 [가정 — plan §5 #156].
    /// 균열 칩 · UB 턴 띠 · 「자동」 딱지(82행의 HUD 몫) · 84행 빛줄기 · 85행 구간 맥동·카드 받기 · 86행 패배 덮개는 M6 「UB 화면 묶음 1」(821·840·850·860 — 결정 84 M1 가 · M2 가, [가정] plan §5 #158).
    /// </summary>
    public static class UbRows
    {
        public const int Gate = 150, Move = 820, Destroy = 830;
        /// <summary>
        /// M6 「UB 화면 묶음 1」(결정 84 M1 가 · M2 가): 821 UB 턴 시작(82행의 예비 — 균열 칩이 눌리고 위 가운데에 「울트라 비스트의 턴」 띠, 102행 UB 페이즈 띠가 같은 단계 처음에 지나간다) ·
        /// 840 네크로즈마가 빛을 모음(84행 — 빛줄기는 칸마다 830 단계 처음에 내리꽂힌다) · 850 균열 10% 구간 넘음 + 왜곡 균열 카드 받기(85행 — 붙이기 S58은 다음 묶음) ·
        /// 860 균열 70% 패배(86행 — 전원 차단 덮개).
        /// </summary>
        public const int Start = 821, Strike = 840, Bracket = 850, Lost = 860;
        /// <summary>83행 UB가 밟은 칸의 숨겨진 장소·유물 토큰이 깨어진 세계로 날아감(831 — TokenMoved마다 단계 하나, <see cref="SpotFlyBeats"/> — M6 「연출 목록 채우기」 [가정 plan §5 #163]).</summary>
        public const int Fly = 831;

        /// <summary>화면(보드)이 그리는 UB 행.</summary>
        public static bool Built(int row) => row == Gate || row == Move || row == Destroy || row == Fly || row == Start || row == Strike || row == Bracket || row == Lost;

        public static T First<T>(FxStep step) where T : GameEvent
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is T t) return t;
            return null;
        }
    }

    /// <summary>
    /// 82행 UB 턴의 HUD 몫(S57 — 결정 84 M2 가): 821 단계 처음에 102행 UB 페이즈 띠(가운데 가로 띠 「울트라 비스트의 턴」 — 라운드 띠와 같은 박자 <see cref="TurnStartBeats.Round"/>)가
    /// 지나가고 위 띠의 균열 칩이 한 번 눌린다. 위 가운데의 작은 띠 「울트라 비스트의 턴 · 자동 · 이동 n/m」는 821 단계부터 그 항목 끝까지 남고, n은 UB가 칸에 내려앉을 때마다(820 단계 끝) 하나씩 는다.
    /// 움직이는 UB 머리 위에는 「이름 · 자동」 꼬리표. 입력은 대기열이 도는 동안 닫혀 있다(04 §8-1 P4).
    /// </summary>
    public static class UbTurnBeats
    {
        /// <summary>위 가운데 띠가 나타나는 때 · 다 나타난 때(821 단계 안 ms) — 페이즈 띠가 빠지는 동안 들어온다.</summary>
        public const int BandFromMs = 500, BandInMs = 300;
        /// <summary>항목 끝에서 띠가 걷히는 길이(ms).</summary>
        public const int BandOutMs = 250;
        /// <summary>균열 칩이 눌리는 박자(821 단계 안 ms): 가장 눌린 때 · 다 돌아온 때와 가장 눌린 배율.</summary>
        public const int PressAtMs = 120, PressEndMs = 420;
        public const float PressScale = 0.86f;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(UbRows.Start) : null;

        public static UbTurnStarted Started(FxStep step) => UbRows.First<UbTurnStarted>(step);

        public struct Pose
        {
            /// <summary>위 가운데 띠의 진하기(0 = 없음).</summary>
            public float Band;
            /// <summary>가운데 페이즈 띠(102행)가 떠 있나와 그 가로 자리(%, −100 → 0 → 100).</summary>
            public bool Phase;
            public float PhaseX;
            /// <summary>균열 칩의 배율(1 = 그대로).</summary>
            public float Press;
            /// <summary>내려앉은 이동 수 · 이번 UB 턴의 총 이동 칸 수(UbTurnStarted.TotalMoves).</summary>
            public int Landed, Total;
            /// <summary>지금 움직이는 UB(포켓몬 표 색인 — 이동 단계가 재생 중이 아니면 −1).</summary>
            public int Moving;
        }

        /// <summary>그 항목(재생 중) · 항목 안 시각의 모습. UB 턴 항목이 아니면 빈 모습(Band 0 · Press 1).</summary>
        public static Pose At(FxEntry entry, int localMs)
        {
            var p = new Pose { Press = 1f, PhaseX = -100f, Moving = -1 };
            var step = StepOf(entry);
            var started = Started(step);
            if (started == null || localMs < step.StartMs) return p;
            int t = localMs - step.StartMs, length = entry.LengthMs;
            p.Total = Math.Max(0, started.TotalMoves);
            if (t < TurnStartBeats.RoundMs)
            {
                var round = TurnStartBeats.Round(t);
                p.Phase = round.Shown;
                p.PhaseX = round.X;
            }
            if (t < PressEndMs) p.Press = t < PressAtMs ? Tween.At(t, 0, PressAtMs, 1f, PressScale, Ease.OutQuad) : Tween.At(t, PressAtMs, PressEndMs - PressAtMs, PressScale, 1f, Ease.OutQuad);
            float inA = Tween.Progress(t, BandFromMs, BandInMs);
            float outA = length > 0 ? 1f - Tween.Progress(localMs, Math.Max(0, length - BandOutMs), BandOutMs) : 0f;
            p.Band = localMs >= length ? 0f : Math.Min(inA, outA);
            foreach (var s in entry.Steps)
            {
                if (s.Row != UbRows.Move) continue;
                if (localMs >= s.EndMs) p.Landed++;
                else if (localMs >= s.StartMs && UbRows.First<UbMoved>(s) is UbMoved m) p.Moving = m.UbId;
            }
            return p;
        }
    }

    /// <summary>
    /// 84행 네크로즈마의 원격 파괴: 840 단계 동안 네크로즈마가 빛을 모으고(특수 공격 클립 · 카메라가 네크로즈마로 — 몸에 빛 고리가 커진다),
    /// 이어지는 네크로즈마의 파괴 단계(830 — 칸마다)마다 그 칸에 빛줄기가 위에서 내리꽂힌 뒤(<see cref="BeamPose"/> — 어두워지기 <see cref="DestroyBeats.DarkFromPermille"/> 앞) UB 파괴와 같은 마무리.
    /// </summary>
    public static class StrikeBeats
    {
        /// <summary>빛줄기가 내리꽂혀 칸에 닿는 때 · 흐려져 사라지는 때(830 단계 길이의 천분율).</summary>
        public const int BeamDownPermille = 150, BeamGonePermille = 420;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(UbRows.Strike) : null;

        public static NecrozmaStrike Event(FxStep step) => UbRows.First<NecrozmaStrike>(step);

        /// <summary>빛을 모으는 정도(0~1 — 840 단계 안 ms): 처음 70% 동안 차오르고 끝까지 머문다.</summary>
        public static float Glow(int ms, int total) => total <= 0 ? 0f : Tween.Apply(Ease.OutQuad, Tween.Progress(ms, 0, total * 7 / 10));

        public struct BeamPose
        {
            /// <summary>위에서 내려온 길이(0~1 — 1이면 칸에 닿았다)와 진하기.</summary>
            public float Reach, Alpha;
        }

        /// <summary>네크로즈마 파괴 단계(830 — ByUbId &lt; 0) 안 시각의 빛줄기.</summary>
        public static BeamPose Beam(int ms, int total)
        {
            if (total <= 0 || ms < 0) return default;
            int down = total * BeamDownPermille / 1000, gone = total * BeamGonePermille / 1000;
            if (ms >= gone) return default;
            if (ms < down) return new BeamPose { Reach = Tween.Apply(Ease.InQuad, Tween.Progress(ms, 0, down)), Alpha = 1f };
            return new BeamPose { Reach = 1f, Alpha = 1f - Tween.Progress(ms, down, gone - down) };
        }

        /// <summary>그 파괴 단계가 네크로즈마의 원격 파괴인가.</summary>
        public static bool ByNecrozma(FxStep step) => step != null && step.Row == UbRows.Destroy && UbRows.First<CellDestroyed>(step) is CellDestroyed d && d.ByUbId < 0;
    }

    /// <summary>
    /// 85행 균열 10% 구간 넘음과 왜곡 균열 카드 받기(850 — BreachBracket · BreachCardsDealt 한 단계): 화면 가장자리가 붉게 한 번 맥동하고(균열 칩도 붉게) →
    /// 카드가 위 띠의 균열 칩에서 좌석 칩마다 날아간다(좌석마다 <see cref="CardEveryMs"/> 어긋남). 붙이기(S58)는 다음 묶음이다.
    /// </summary>
    public static class BracketBeats
    {
        /// <summary>붉은 맥동: 가장 진한 때 · 다 걷힌 때(단계 안 ms).</summary>
        public const int PulsePeakMs = 200, PulseEndMs = 800;
        /// <summary>카드가 날기 시작하는 때 · 좌석마다 어긋남 · 한 장이 나는 길이(ms).</summary>
        public const int CardFromMs = 600, CardEveryMs = 150, CardFlyMs = 600;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(UbRows.Bracket) : null;

        public static BreachCardsDealt Dealt(FxStep step) => UbRows.First<BreachCardsDealt>(step);

        /// <summary>붉은 맥동의 진하기(0~1).</summary>
        public static float Pulse(int ms)
        {
            if (ms < 0 || ms >= PulseEndMs) return 0f;
            return ms < PulsePeakMs ? Tween.Apply(Ease.OutQuad, Tween.Progress(ms, 0, PulsePeakMs)) : 1f - Tween.Apply(Ease.InQuad, Tween.Progress(ms, PulsePeakMs, PulseEndMs - PulsePeakMs));
        }

        /// <summary>i번째 좌석(받은 순서)으로 가는 카드의 비율(−1 = 아직 · 0~1 = 나는 중 · 2 = 닿아 사라짐).</summary>
        public static float Card(int ms, int i)
        {
            int from = CardFromMs + CardEveryMs * i;
            if (ms < from) return -1f;
            if (ms >= from + CardFlyMs) return 2f;
            return Tween.Apply(Ease.InOutQuad, Tween.Progress(ms, from, CardFlyMs));
        }
    }

    /// <summary>
    /// 86행 균열 70% 패배(860 — 전원 차단 덮개, 결과 화면 앞): 판 전체가 깨어진 세계의 보라로 물들고 → 조명이 꺼지듯 어두워지고 → 「패배」 글이 떠오른다.
    /// 단계가 끝난 뒤(게임이 끝난 판)에는 다 보인 모습으로 남는다 — 울트라 버스트 결과 창(S64)은 다음 묶음이다.
    /// </summary>
    public static class LostBeats
    {
        public const int WashMs = 1500, DarkFromMs = 1500, DarkMs = 1500, TitleFromMs = 2800, TitleMs = 800;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(UbRows.Lost) : null;

        public struct Pose
        {
            /// <summary>보라 물듦 · 어둠 · 글(0~1).</summary>
            public float Wash, Dark, Title;
        }

        public static Pose Done => new Pose { Wash = 1f, Dark = 1f, Title = 1f };

        public static Pose At(int ms)
        {
            if (ms < 0) return default;
            return new Pose
            {
                Wash = Tween.Apply(Ease.OutQuad, Tween.Progress(ms, 0, WashMs)),
                Dark = Tween.Apply(Ease.InQuad, Tween.Progress(ms, DarkFromMs, DarkMs)),
                Title = Tween.Apply(Ease.OutQuad, Tween.Progress(ms, TitleFromMs, TitleMs)),
            };
        }
    }

    /// <summary>
    /// 위 띠의 균열 칩(S56 — 결정 84 M1 가)이 읽는 값(엔진 무관): 균열 강도 · 다음 10%까지 남은 파괴 수 · 다음 UB 턴 이동 수.
    /// 보기는 이미 끝 값이라 화면 대기열에 남은 균열 바뀜(BreachChanged — 그 앞의 파괴 단계 830에 붙는다)이 지나기 전에는 바뀌기 전 값이고,
    /// 그 파괴 단계 동안은 어두워지는 박자대로 숫자가 굴러 오른다(83행 「균열 % 숫자가 굴러 오름」).
    /// </summary>
    public static class UbGauge
    {
        /// <summary>균열 막대의 칸 수(0~70%를 10%씩 — 마지막 칸 = 패배 앞).</summary>
        public const int Segments = 7;

        /// <summary>판의 나무·산호·사당 수와 그 가운데 파괴된 수(<see cref="UbTurn.Breach"/>와 같은 셈 — 상태를 바꾸지 않는다).</summary>
        public static void Count(IReadOnlyList<TokenState> tokens, out int targets, out int destroyed)
        {
            targets = destroyed = 0;
            if (tokens == null) return;
            foreach (var t in tokens)
            {
                if (!UbTurn.IsTarget(t.Kind) || t.Cell < 0) continue;
                targets++;
                if (t.Destroyed) destroyed++;
            }
        }

        /// <summary>보기의 균열 강도(%).</summary>
        public static int Breach(IReadOnlyList<TokenState> tokens)
        {
            Count(tokens, out int targets, out int destroyed);
            return targets == 0 ? 0 : UltraBurstRules.BreachStrength(destroyed, targets);
        }

        /// <summary>다음 10% 구간까지 더 파괴돼야 하는 나무·산호·사당 수(남은 대상으로 닿지 못하면 남은 대상 수).</summary>
        public static int NextNeed(IReadOnlyList<TokenState> tokens)
        {
            Count(tokens, out int targets, out int destroyed);
            if (targets == 0) return 0;
            int next = (UltraBurstRules.BreachStrength(destroyed, targets) / 10 + 1) * 10;
            for (int d = destroyed; d <= targets; d++)
                if (UltraBurstRules.BreachStrength(d, targets) >= next) return d - destroyed;
            return targets - destroyed;
        }

        /// <summary>
        /// 지금 보일 균열 강도: 보기 값에서, 화면 대기열(재생 중인 항목 포함, 순서대로)에 남은 첫 균열 바뀜의 바뀌기 전 값으로 되돌린다.
        /// 재생 중인 그 파괴 단계 안이면 어두워지는 박자(<see cref="DestroyBeats.Dark"/>)대로 바뀌기 전 → 뒤로 굴러 오른다.
        /// </summary>
        public static int Shown(IReadOnlyList<TokenState> tokens, IEnumerable<FxEntry> queued, FxEntry current, int localMs)
        {
            int shown = Breach(tokens);
            if (queued == null) return shown;
            foreach (var entry in queued)
            {
                bool cur = ReferenceEquals(entry, current);
                FxStep at = null;
                int k = 0;
                foreach (var e in entry.Events)
                {
                    if (e is CellDestroyed)
                    {
                        at = NthDestroy(entry, k++);
                        continue;
                    }
                    if (!(e is BreachChanged b) || b.After == b.Before) continue;
                    // 그 바뀜이 붙은 파괴 단계(없으면 항목 끝)가 이미 지났으면 다음 것
                    int end = at != null ? at.EndMs : entry.LengthMs;
                    if (cur && localMs >= end) continue;
                    if (cur && at != null && localMs >= at.StartMs)
                        return b.Before + (int)Math.Floor((b.After - b.Before) * DestroyBeats.Dark(localMs - at.StartMs, at.Ms) + 1e-4);
                    return b.Before;
                }
            }
            return shown;
        }

        static FxStep NthDestroy(FxEntry entry, int n)
        {
            foreach (var s in entry.Steps)
                if (s.Row == UbRows.Destroy && n-- == 0) return s;
            return null;
        }

        /// <summary>균열 막대 i번째 칸(0~6)이 찬 비율(0~1).</summary>
        public static float Fill(int breach, int i) => Math.Max(0f, Math.Min(1f, (breach - i * 10) / 10f));
    }

    /// <summary>
    /// 15행 왜곡 게이트 이동: 말이 게이트 속으로 가라앉아 사라지고 → 두 게이트 사이에 보라 점선이 이어지는 동안 카메라가 건너편으로 → 짝 게이트에서 솟아올라 → 착지 고리.
    /// </summary>
    public static class GateBeats
    {
        /// <summary>가라앉음 끝 · 솟아오름 시작 · 솟아오름 끝(길이의 천분율). 그 사이는 틈 속(말을 그리지 않는다).</summary>
        public const int SinkPermille = 350, RisePermille = 600, LandPermille = 850;
        /// <summary>가라앉는 깊이(보드 유닛 — 칸 윗면 아래로).</summary>
        public const float SinkDepth = 1.4f;
        /// <summary>보라 점선의 점 수.</summary>
        public const int Dots = 12;

        public struct Pose
        {
            /// <summary>도착 게이트에 있나 · 틈 속인가(말을 그리지 않는다).</summary>
            public bool AtDest, Hidden;
            /// <summary>칸 윗면 아래로 내려간 비율(0 = 윗면 · 1 = <see cref="SinkDepth"/>).</summary>
            public float Depth;
            /// <summary>카메라가 출발 → 도착 게이트로 건너간 비율(0~1).</summary>
            public float Cross;
            /// <summary>보라 점선이 뻗어 나간 비율(0~1 — 출발 쪽부터)과 진하기(0~1).</summary>
            public float Line, LineAlpha;
            /// <summary>출발 · 도착 게이트의 보라 고리(0~1 — 커지며 옅어짐, 0 = 없음).</summary>
            public float RingFrom, RingTo;
        }

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(UbRows.Gate) : null;

        public static GateTravel Event(FxStep step) => UbRows.First<GateTravel>(step);

        public static Pose At(int ms, int total)
        {
            var p = new Pose();
            if (total <= 0) return new Pose { AtDest = true, Cross = 1f };
            if (ms >= total) return new Pose { AtDest = true, Cross = 1f };
            int sink = total * SinkPermille / 1000, rise = total * RisePermille / 1000, land = total * LandPermille / 1000;
            if (ms < sink)
            {
                float t = Tween.Progress(ms, 0, sink);
                p.Depth = Tween.Apply(Ease.InQuad, t);
                p.RingFrom = t;
                p.Hidden = t >= 0.98f;
                return p;
            }
            if (ms < rise)
            {
                float t = Tween.Progress(ms, sink, rise - sink);
                p.Hidden = true;
                p.Depth = 1f;
                p.Cross = Tween.Apply(Ease.InOutQuad, t);
                p.Line = Math.Min(1f, t * 2f);
                p.LineAlpha = t < 0.75f ? 1f : (1f - t) * 4f;
                return p;
            }
            p.AtDest = true;
            p.Cross = 1f;
            if (ms < land)
            {
                float t = Tween.Progress(ms, rise, land - rise);
                p.Depth = 1f - Tween.Apply(Ease.OutQuad, t);
                return p;
            }
            p.RingTo = Tween.Progress(ms, land, Math.Max(1, total - land));
            return p;
        }
    }

    /// <summary>82행 UB 한 칸 이동: 칸 사이를 짧게 뛰어 옮긴다(Walk 클립) — 착지가 단계 끝(보이는 값 = 그 칸에 선 UB).</summary>
    public static class UbMoveBeats
    {
        /// <summary>칸 사이 점프 높이(보드 유닛).</summary>
        public const float HopHeight = 0.45f;

        /// <summary>무거운 UB(04 82행 「무거운 UB는 착지 때 작은 흔들림」 — M6 「UB 시작」): 단계의 이 천분율에 내려앉고 남은 동안 피규어가 눌렸다 편다(화면은 흔들지 않는다 — 결정 53 ②).</summary>
        public const int HeavyLandPermille = 750;
        /// <summary>무거운 UB가 내려앉을 때 가장 눌린 높이 배율.</summary>
        public const float HeavySquash = 0.86f;

        /// <summary>
        /// 무거운 UB인가(피규어·카드 키): 원작 몸무게 300kg 넘는 넷 — 매시붕(333.6) · 철화구야(999.9) · 악식킹(888) · 차곡차곡(820) [가정 #UB-시작-⑤].
        /// </summary>
        public static bool Heavy(string key) => key == "794:0" || key == "797:0" || key == "799:0" || key == "805:0";

        public struct Pose
        {
            /// <summary>출발 → 도착 칸 사이의 비율(0~1)과 뜬 높이(보드 유닛).</summary>
            public float T, Hop;
            /// <summary>피규어 높이 배율(1 = 그대로 — 무거운 UB의 착지 눌림).</summary>
            public float Squash;
        }

        public static Pose At(int ms, int total, bool heavy = false)
        {
            if (!heavy)
            {
                float t = Tween.Progress(ms, 0, total);
                return new Pose { T = Tween.Apply(Ease.InOutQuad, t), Hop = HopHeight * 4f * t * (1f - t), Squash = 1f };
            }
            int land = total * HeavyLandPermille / 1000;
            if (ms < land)
            {
                float t = Tween.Progress(ms, 0, land);
                return new Pose { T = Tween.Apply(Ease.InOutQuad, t), Hop = HopHeight * 4f * t * (1f - t), Squash = 1f };
            }
            float u = Tween.Progress(ms, land, Math.Max(1, total - land));
            return new Pose { T = 1f, Hop = 0f, Squash = 1f - (1f - HeavySquash) * (float)Math.Sin(Math.PI * u) };
        }
    }

    /// <summary>83행 UB가 칸을 밟음: 공격 클립(밟은 UB · 네크로즈마의 원격 파괴는 특수 공격) → 나무·산호·사당이 시들며 영구히 어두워진다(착지 = 단계 끝에 다 어둡다).</summary>
    public static class DestroyBeats
    {
        /// <summary>어두워지기 시작 · 끝(길이의 천분율).</summary>
        public const int DarkFromPermille = 300, DarkToPermille = 900;

        /// <summary>그 단계 안 시각의 어두운 정도(0 = 그대로 · 1 = 다 어둡다).</summary>
        public static float Dark(int ms, int total)
        {
            if (total <= 0) return 1f;
            int from = total * DarkFromPermille / 1000, to = total * DarkToPermille / 1000;
            return Tween.Apply(Ease.OutQuad, Tween.Progress(ms, from, Math.Max(1, to - from)));
        }
    }

    /// <summary>
    /// 83행 토큰 날기(831 — 04 표 「숨겨진 장소·유물 토큰은 빨려 올라가 깨어진 세계 빈 칸으로 날아감(없으면 사라짐)」): 밟힌 칸에서 토큰이 떠오르며 서고(카메라를 본다) →
    /// 틈 속(그리지 않는다 — 카메라가 깨어진 세계로 건너간다) → 깨어진 세계의 빈 칸 위에서 내려앉는다. 갈 칸이 없으면(To &lt; 0) 떠오른 뒤 사라진다.
    /// 보기는 이미 도착 칸이라 대기열에 남은 단계가 다 지나기 전에는 출발 칸에 둔다(<see cref="Shown"/>).
    /// </summary>
    public static class SpotFlyBeats
    {
        /// <summary>떠오름 끝 · 내려앉기 시작(길이의 천분율). 그 사이는 틈 속.</summary>
        public const int RisePermille = 400, LandFromPermille = 550;

        public struct Pose
        {
            /// <summary>그 순간 토큰이 보이는 칸(−1 = 틈 속이거나 사라짐) · 칸 윗면 위로 뜬 정도(0 = 칸에 누움 · 1 = 가장 높이).</summary>
            public int Cell;
            public float Height;
            /// <summary>카메라가 출발 → 도착 칸으로 건너간 비율(0~1 — 갈 칸이 없으면 0).</summary>
            public float Cross;
        }

        public static TokenMoved Event(FxStep step) => step != null && step.Row == UbRows.Fly ? UbRows.First<TokenMoved>(step) : null;

        /// <summary>그 단계 안 시각의 모습(ms &lt; 0 = 시작 전 · ms ≥ total = 끝난 뒤).</summary>
        public static Pose At(TokenMoved m, int ms, int total)
        {
            var p = new Pose { Cell = m.From };
            if (ms < 0 || total <= 0) return p;
            if (ms >= total) return new Pose { Cell = m.To, Cross = m.To >= 0 ? 1f : 0f };
            int rise = total * RisePermille / 1000, land = total * LandFromPermille / 1000;
            if (m.To >= 0) p.Cross = Tween.Apply(Ease.InOutQuad, Tween.Progress(ms, rise, land - rise));
            if (ms < rise)
            {
                p.Height = Tween.Apply(Ease.OutQuad, Tween.Progress(ms, 0, rise));
                return p;
            }
            if (ms < land || m.To < 0) return new Pose { Cell = -1, Cross = p.Cross };
            p.Cell = m.To;
            p.Height = 1f - Tween.Apply(Ease.InQuad, Tween.Progress(ms, land, total - land));
            return p;
        }

        /// <summary>
        /// 대기열(재생 중인 항목 포함)에 남은 토큰 날기 단계마다 그 토큰의 지금 모습 — 시작 전이면 출발 칸 · 끝난 단계는 뺀다. 같은 토큰(도착 칸)이 여러 번이면 앞의 것.
        /// </summary>
        public static List<KeyValuePair<TokenMoved, Pose>> Pending(IEnumerable<FxEntry> queued, FxEntry current, int localMs)
        {
            var list = new List<KeyValuePair<TokenMoved, Pose>>();
            if (queued == null) return list;
            foreach (var entry in queued)
                foreach (var s in entry.Steps)
                {
                    var m = Event(s);
                    if (m == null) continue;
                    bool playing = ReferenceEquals(entry, current);
                    if (playing && localMs >= s.EndMs) continue;
                    if (m.To >= 0 && list.Exists(kv => kv.Key.To == m.To)) continue;
                    list.Add(new KeyValuePair<TokenMoved, Pose>(m, At(m, playing ? localMs - s.StartMs : -1, s.Ms)));
                }
            return list;
        }
    }

    /// <summary>보드 피규어가 그 순간 돌리는 클립(figures.json의 클립 이름과 같다).</summary>
    public enum BeastClip { Idle, Walk, PhysicalAttack, SpecialAttack }

    /// <summary>
    /// 울트라 버스트 판의 보드 셈(엔진 무관): UB·네크로즈마 피규어가 그 순간 선 칸 · 파괴된 나무·산호·사당의 어두운 정도. 보기(게임 상태)는 갱신과 함께 이미 끝 값이므로
    /// 화면 대기열에 남은 이동·파괴 단계가 다 지나기 전에는 그 단계의 출발 칸 · 어둡기 전 모습으로 되돌려 센다.
    /// </summary>
    public static class UbBoard
    {
        public struct Beast
        {
            /// <summary>보드에 서 있나(UB 토큰이 맵에 있다 — 쓰러뜨린 UB · 다른 판에서는 없다).</summary>
            public bool Shown;
            /// <summary>출발 · 도착 칸과 그 사이 비율 · 뜬 높이. 서 있으면 From == To · T = 0.</summary>
            public int From, To;
            public float T, Hop;
            /// <summary>그 순간의 클립과 그 클립 안 시각(ms).</summary>
            public BeastClip Clip;
            public int ClipMs;
            /// <summary>피규어 높이 배율(1 = 그대로 — 무거운 UB의 착지 눌림 <see cref="UbMoveBeats.HeavySquash"/>).</summary>
            public float Squash;
        }

        /// <summary>
        /// 그 피규어(포켓몬 표 색인 def — UB 토큰 · 네크로즈마 토큰의 Def)가 지금 선 자리. queued = 화면 대기열(재생 중인 항목 포함, 순서대로), current = 재생 중인 항목, localMs = 그 항목 안 시각.
        /// 대기열이 없으면(null) 보기 그대로. heavy = 무거운 UB(<see cref="UbMoveBeats.Heavy"/> — 착지 눌림).
        /// </summary>
        public static Beast Of(IReadOnlyList<TokenState> tokens, int def, IEnumerable<FxEntry> queued, FxEntry current, int localMs, bool heavy = false)
        {
            var b = new Beast { Squash = 1f };
            if (tokens == null) return b;
            TokenState token = null;
            foreach (var t in tokens)
                if ((t.Kind == TokenKind.UltraBeast || t.Kind == TokenKind.Necrozma) && t.Def == def) { token = t; break; }
            if (token == null || token.Cell < 0) return b;
            b.Shown = true;
            b.From = b.To = token.Cell;
            bool necrozma = token.Kind == TokenKind.Necrozma;
            if (queued == null) return b;
            bool placed = false;
            foreach (var entry in queued)
            {
                bool cur = ReferenceEquals(entry, current);
                foreach (var step in entry.Steps)
                {
                    if (step.Row == UbRows.Move && UbRows.First<UbMoved>(step) is UbMoved m && m.UbId == def && !necrozma)
                    {
                        if (cur && localMs >= step.EndMs) continue;
                        if (placed) continue;
                        placed = true;
                        b.From = b.To = m.From;
                        if (cur && localMs >= step.StartMs)
                        {
                            var pose = UbMoveBeats.At(localMs - step.StartMs, step.Ms, heavy);
                            b.To = m.To;
                            b.T = pose.T;
                            b.Hop = pose.Hop;
                            b.Squash = pose.Squash;
                            b.Clip = BeastClip.Walk;
                            b.ClipMs = localMs - step.StartMs;
                        }
                        continue;
                    }
                    // 84행 빛을 모음(840): 네크로즈마가 특수 공격 클립
                    if (step.Row == UbRows.Strike && necrozma && cur && localMs >= step.StartMs && localMs < step.EndMs && b.Clip == BeastClip.Idle)
                    {
                        b.Clip = BeastClip.SpecialAttack;
                        b.ClipMs = localMs - step.StartMs;
                        continue;
                    }
                    if (step.Row == UbRows.Destroy && cur && localMs >= step.StartMs && localMs < step.EndMs && UbRows.First<CellDestroyed>(step) is CellDestroyed d
                        && (necrozma ? d.ByUbId < 0 : d.ByUbId == def) && b.Clip == BeastClip.Idle)
                    {
                        b.Clip = necrozma ? BeastClip.SpecialAttack : BeastClip.PhysicalAttack;
                        b.ClipMs = localMs - step.StartMs;
                    }
                }
            }
            return b;
        }

        /// <summary>
        /// 네크로즈마 피규어가 지금 세울 폼(피규어·카드 키 — M6 「UB 시작」): 1페이즈 800:1 · 2페이즈는 썬/문 동전이 정한 800:2 황혼의 갈기 · 800:3 새벽의 날개
        /// (동전을 던지기 전 — 2페이즈에 처음 도전하기 전 — 은 1페이즈 모습 그대로 [가정 #UB-시작-③]) · 3페이즈 800:4 울트라네크로즈마.
        /// 보기는 이미 끝 값이라 화면 대기열에 남은(아직 다 재생되지 않은) 페이즈 바뀜 · 동전은 지나지 않은 것으로 센다 — 배틀 결과 창 뒤 보드에 돌아올 때 바뀐다.
        /// </summary>
        public static string NecrozmaForm(UbState ub, IEnumerable<FxEntry> queued)
        {
            if (ub == null) return UbNecrozma.Phase1;
            int phase = ub.NecrozmaPhase, coin = ub.SunMoon;
            if (queued != null)
                foreach (var entry in queued)
                    foreach (var e in entry.Events)
                        if (e is NecrozmaPhase p && p.Phase < phase) phase = p.Phase;
                        else if (e is CoinFlip) coin = -1;
            if (phase <= 1 || (phase == 2 && coin < 0)) return UbNecrozma.Phase1;
            if (phase >= UbNecrozma.Phases) return UbNecrozma.Ultra;
            return coin == UbNecrozma.Moon ? UbNecrozma.DawnWings : UbNecrozma.DuskMane;
        }

        /// <summary>파괴로 어두워지거나(<see cref="UbTurn.IsTarget"/> — 나무·산호·사당) 맵에서 빠지는(kindler B — 칸 −1) 고정물 토큰인가.</summary>
        public static bool Shaded(TokenKind kind) => UbTurn.IsTarget(kind);

        public struct Shade
        {
            /// <summary>맵에서 빠졌다(그리지 않는다).</summary>
            public bool Hidden;
            /// <summary>어두운 정도(0 = 그대로 · 1 = 파괴된 모습).</summary>
            public float Dark;
        }

        /// <summary>그 나무·산호·사당 토큰의 지금 모습: 맵에서 빠졌으면 숨김, 파괴됐으면 어둡게 — 화면 대기열에 남은 그 칸의 파괴 단계가 지나기 전에는 그대로 · 그 단계 동안은 박자대로.</summary>
        public static Shade ShadeOf(TokenState t, IEnumerable<FxEntry> queued, FxEntry current, int localMs)
        {
            var s = new Shade();
            if (t == null || !Shaded(t.Kind)) return s;
            if (t.Cell < 0) return new Shade { Hidden = true };
            if (!t.Destroyed) return s;
            s.Dark = 1f;
            if (queued == null) return s;
            foreach (var entry in queued)
            {
                bool cur = ReferenceEquals(entry, current);
                foreach (var step in entry.Steps)
                {
                    if (step.Row != UbRows.Destroy || !(UbRows.First<CellDestroyed>(step) is CellDestroyed d) || d.Cell != t.Cell || d.TokenKind != t.Kind) continue;
                    if (cur && localMs >= step.EndMs) continue;
                    s.Dark = cur && localMs >= step.StartMs ? DestroyBeats.Dark(localMs - step.StartMs, step.Ms) : 0f;
                    return s;
                }
            }
            return s;
        }
    }
}
