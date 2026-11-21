using System;
using Rekkon.UiKit;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 운명의 변화 연출 행(04 §8-8 7·8·14·42~45행, M5 「운명·응답 화면」): 떨어진 별 운명(70 — 7행) · 받기(420 — 42행, 귀환 8행의 「운명 1장」·배신자 반환·보상도 이 박자) ·
    /// 6장째 버리기(421) · 카드 내기(450 — 카드 지나가기, 3장이면 세 장이 하나로 합쳐짐 45행) · 펄기아 순간이동(140 — 14행) · 디아루가 재굴림(430 — 43행) · 기라티나 희생(440 — 44행).
    /// 길이는 방장과 같은 <c>PlayTiming</c>이고 여기 값은 그 안에서 화면이 나누는 박자다 [가정 — plan §5 #98].
    /// </summary>
    public static class FateRows
    {
        public const int Star = 70, Draw = 420, Discard = 421, Play = 450, Teleport = 140, Reroll = 430, Sacrifice = 440;

        public static bool Built(int row) => row == Star || row == Draw || row == Discard || row == Play || row == Teleport || row == Reroll || row == Sacrifice;

        /// <summary>배틀 화면이 그리는 운명 행(나머지는 보드 HUD — 카드 내기 450은 둘 다).</summary>
        public static bool InBattle(int row) => row == Reroll || row == Sacrifice;

        /// <summary>그 항목의 운명 받기 단계(떨어진 별 70 · 받기 420 · 귀환 회복 80에 실린 운명 1장 — 없으면 null).</summary>
        public static FxStep DealStep(FxEntry entry)
        {
            if (entry == null) return null;
            var s = entry.Step(Star) ?? entry.Step(Draw);
            if (s != null) return s;
            var heal = entry.Step(80);
            return First<FateDealt>(heal) != null ? heal : null;
        }

        /// <summary>받기 단계 안에서 카드가 날아오기 시작하는 시각: 70·420은 처음부터, 귀환 회복(80)은 끝의 <see cref="FateDealBeats.FlyMs"/> + 뒤집기 앞(8행 「착지: 운명 1장이 손으로 날아옴」).</summary>
        public static int DealFrom(FxStep step) => step == null ? 0 : step.Row == 80 ? Math.Max(0, step.Ms - FateDealBeats.FlyMs - FateDealBeats.FlipMs - FateDealBeats.PopMs) : 0;

        public static T First<T>(FxStep step) where T : GameEvent
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is T t) return t;
            return null;
        }
    }

    /// <summary>카드 한 장의 한 순간: 출발점 → 도착점 진행(0~1) · 보이기 · 앞면이 보이는 정도(0 = 뒷면 · 1 = 앞면, 뒤집기는 가로 배율) · 크기 배수 · 찢김(0~1).</summary>
    public struct FateCardPose
    {
        public float T, Show, Face, Scale, Tear, Lift;
    }

    /// <summary>
    /// 7·42행 받기: 카드 뒷면이 출발점(떨어진 별 = 위 띠 · 그 밖 = 화면 가운데 위)에서 내 [운명] 단추로 호를 그리며 날아오고(좌석마다 <see cref="SeatGapMs"/> 어긋남),
    /// 닿는 순간 내 화면에서만 앞면으로 뒤집히고 단추의 장수 배지가 톡 튄다. 남 좌석은 뒷면 그대로 그 좌석 칩으로.
    /// </summary>
    public static class FateDealBeats
    {
        public const int SeatGapMs = 150, FlyMs = 450, FlipMs = 150, PopMs = 180;

        /// <summary>i번째 좌석(PerSeat 순서) 카드의 모습. 단계 밖이면 Show 0.</summary>
        public static FateCardPose At(int ms, int total, int i, bool mine)
        {
            var p = new FateCardPose { Scale = 1f };
            int start = SeatGapMs * Math.Max(0, i), fly = Math.Max(1, Math.Min(FlyMs, total - start - FlipMs));
            if (ms < start || ms >= total) return p;
            p.T = Tween.Apply(Ease.OutQuad, Tween.Progress(ms, start, fly));
            p.Show = 1f;
            p.Lift = (float)Math.Sin(Math.PI * p.T);
            p.Face = mine ? Tween.Progress(ms, start + fly, FlipMs) : 0f;
            p.Scale = 1f + 0.25f * p.Lift;
            return p;
        }

        /// <summary>그 좌석의 장수 배지가 튀는 배수(카드가 닿은 뒤 <see cref="PopMs"/> 동안 1.4 → 1).</summary>
        public static float Pop(int ms, int total, int i)
        {
            int start = SeatGapMs * Math.Max(0, i), land = start + Math.Max(1, Math.Min(FlyMs, total - start - FlipMs));
            return ms < land ? 1f : Tween.At(ms, land, PopMs, 1.4f, 1f, Ease.OutQuad);
        }
    }

    /// <summary>42행 6장째 버리기: 버린 카드가 [운명] 단추 위로 떠올라 부르르 떨고(150~350) 가운데부터 찢기듯 가로로 줄며 사라진다(350~600).</summary>
    public static class FateDiscardBeats
    {
        public const int RiseMs = 150, ShakeMs = 200, TearMs = 250;

        public static FateCardPose At(int ms, int total)
        {
            var p = new FateCardPose { Scale = 1f, Face = 1f };
            if (ms < 0 || ms >= total) return p;
            p.Show = 1f;
            p.Lift = Tween.Apply(Ease.OutQuad, Tween.Progress(ms, 0, RiseMs));
            p.T = ms >= RiseMs && ms < RiseMs + ShakeMs ? (float)Math.Sin(ms * 0.25) : 0f;   // 떨림(가로 px 비)
            p.Tear = Tween.Apply(Ease.InQuad, Tween.Progress(ms, RiseMs + ShakeMs, Math.Max(1, total - RiseMs - ShakeMs)));
            return p;
        }
    }

    /// <summary>
    /// 45행 카드 내기(카드 지나가기 P11): 낸 카드가 오른쪽에서 화면 가운데로 크게 튀어나와 머물고 왼쪽으로 빠진다. 3장이면 세 장이 겹쳐 돌며 하나로 합쳐지고(금빛 둘레 — 아르세우스가 섞이면 금빛이 더 짙다)
    /// 그 뒤 같은 모습으로 빠진다. 효과 연출(14·43·44행)은 이 단계 뒤에 이어진다.
    /// </summary>
    public static class FatePassBeats
    {
        public const int InMs = 180, OutMs = 160, MergeFromMs = 150, MergeMs = 600;

        /// <summary>한 순간: X = 가운데에서의 가로 위치(−1 왼쪽 밖 ~ 0 가운데 ~ 1 오른쪽 밖) · Spread = 세 장이 벌어진 정도(1 → 0 합쳐짐) · Glow = 금빛 둘레 · Spin = 돈 각(라디안).</summary>
        public struct Pose
        {
            public float X, Show, Spread, Glow, Spin;
        }

        public static Pose At(int ms, int total, bool play3)
        {
            var p = new Pose();
            if (total <= 0 || ms < 0 || ms >= total) return p;
            p.Show = 1f;
            if (ms < InMs) p.X = 1f - Tween.Apply(Ease.OutQuad, Tween.Progress(ms, 0, InMs));
            else if (ms >= total - OutMs) p.X = -Tween.Apply(Ease.InQuad, Tween.Progress(ms, total - OutMs, OutMs));
            if (play3)
            {
                float m = Tween.Progress(ms, MergeFromMs, MergeMs);
                p.Spread = 1f - Tween.Apply(Ease.InQuad, m);
                p.Spin = (float)(Math.PI * 2 * Tween.Apply(Ease.OutQuad, m));
                p.Glow = m >= 1f ? 1f - Tween.Progress(ms, MergeFromMs + MergeMs, Math.Max(1, total - OutMs - MergeFromMs - MergeMs)) * 0.5f : m * 0.6f;
            }
            return p;
        }
    }

    /// <summary>
    /// 14행 펄기아 순간이동: 출발 칸에서 말이 비틀리며 떠올라 공간 틈으로 사라지고(분홍 고리) → 비어 있는 틈 → 도착 칸 위에서 공간이 갈라지며 말이 떨어져 내려앉는다(분홍 고리 · 착지 먼지).
    /// </summary>
    public static class TeleportBeats
    {
        /// <summary>사라짐 끝 · 나타남 시작(길이의 천분율).</summary>
        public const int GonePermille = 400, BackPermille = 550;
        public const float RiseHeight = 0.8f, DropHeight = 1.6f;

        public struct Pose
        {
            /// <summary>0 = 출발 칸에 있음 · 1 = 도착 칸에 있음. Hidden = 틈 속(말을 그리지 않는다).</summary>
            public bool AtDest, Hidden;
            /// <summary>칸 위로 뜬 높이(보드 유닛).</summary>
            public float Height;
            /// <summary>출발 · 도착 칸의 분홍 고리(0~1 — 커지며 옅어짐).</summary>
            public float RingFrom, RingTo;
        }

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(FateRows.Teleport) : null;

        public static Teleported Event(FxStep step) => FateRows.First<Teleported>(step);

        public static Pose At(int ms, int total)
        {
            var p = new Pose();
            if (total <= 0) return p;
            if (ms >= total) return new Pose { AtDest = true };
            int gone = total * GonePermille / 1000, back = total * BackPermille / 1000;
            if (ms < gone)
            {
                float t = Tween.Progress(ms, 0, gone);
                p.Height = RiseHeight * Tween.Apply(Ease.InQuad, t);
                p.RingFrom = t;
                p.Hidden = t >= 0.95f;
                return p;
            }
            if (ms < back)
            {
                p.Hidden = true;
                p.RingFrom = 1f;
                return p;
            }
            p.AtDest = true;
            float d = Tween.Progress(ms, back, Math.Max(1, total - back));
            p.Height = DropHeight * (1f - Tween.Apply(Ease.OutQuad, Math.Min(1f, d * 1.6f)));
            p.RingTo = d;
            return p;
        }
    }

    /// <summary>
    /// 43행 디아루가 재굴림: 고른 주사위가 제자리에서 글리치(파란 깜빡임 + 면이 마구 바뀜) → 새 면으로 탁 고정 + 번쩍 → 다시 세기. 3장이면 고른 주사위를 하나씩 차례로(주사위당 <c>PlayTiming.RerollPerMs</c>).
    /// 굴림판은 다시 구르지 않는다 — 멈춘 주사위를 그 자리에 다시 세우고 고른 주사위만 입체로 글리치한다(M9 다듬기 P38 — <c>DiceRollScene.AddReroll</c>, 단계의 Tag). 쥐어 둔 굴림이 없으면 독의 주사위 줄(납작한 칩)만 [가정 — plan §5 #98 · #501].
    /// </summary>
    public static class RerollBeats
    {
        public const int InMs = 200, LockMs = 1200, FlashMs = 225, FlickerMs = 150;   // 결정 190: 옛 80 · 480 · 90 · 60의 2.5배(PlayTiming.RerollPerMs · DiceRollScene 글리치와 같이)

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(FateRows.Reroll) : null;

        public static DiceFacesChanged Event(FxStep step) => FateRows.First<DiceFacesChanged>(step);

        /// <summary>k번째로 다시 굴리는 주사위: 0 = 아직 · 글리치 중(Glitch &gt; 0, Shown = 지금 깜빡이는 면 번호 0~2) · 고정(Locked, Flash = 번쩍).</summary>
        public struct Pose
        {
            public float Glitch, Flash;
            public bool Locked;
            public int Flicker;
        }

        public static Pose At(int ms, int k, int per)
        {
            var p = new Pose();
            int start = per * Math.Max(0, k);
            if (ms < start) return p;
            int u = ms - start;
            if (u < LockMs)
            {
                p.Glitch = Math.Min(1f, u / (float)InMs);
                p.Flicker = (u / FlickerMs) % 3;
                return p;
            }
            p.Locked = true;
            p.Flash = Math.Max(0f, 1f - (u - LockMs) / (float)FlashMs);
            return p;
        }
    }

    /// <summary>44행 기라티나 희생: 카드 뒤 검은 그림자가 내 포켓몬을 감싸고(0~40%) → 체력이 빠짐(40~75%, 표시 값은 단계 끝에 착지) → 위력 칩 「+n」이 붉게 박힘(75~100%).</summary>
    public static class SacrificeBeats
    {
        public const int CoverPermille = 400, DrainPermille = 750;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(FateRows.Sacrifice) : null;

        public struct Pose
        {
            public float Shadow, Chip;
        }

        public static Pose At(int ms, int total)
        {
            var p = new Pose();
            if (total <= 0 || ms < 0 || ms >= total) return p;
            int cover = total * CoverPermille / 1000, drain = total * DrainPermille / 1000;
            p.Shadow = ms < cover ? Tween.Progress(ms, 0, cover) : 1f - Tween.Progress(ms, drain, total - drain);
            p.Chip = ms < drain ? 0f : Tween.At(ms, drain, 200, 1.6f, 1f, Ease.OutQuad);
            return p;
        }
    }

    /// <summary>
    /// 98행 남의 차례 응답 프롬프트(S41 — 결정 75 A3 가: 아래 판 「행동」 칸 자리): 대기열이 빈 뒤 판이 아래에서 미끄러져 올라오고(<see cref="InMs"/>), 시계가 있으면 남은 시간 막대가 줄고,
    /// 시간이 다 되면 기본값 답이 저절로 눌린 뒤 「시간 초과 · 사용 안 함」이 <see cref="TimeoutMs"/> 동안 남는다. 대기열 항목이 아니다(창 — none).
    /// </summary>
    public static class PromptBeats
    {
        public const int InMs = 200, TimeoutMs = 1500, RisePx = 24;

        /// <summary>올라온 정도(0~1).</summary>
        public static float Rise(long sinceMs) => sinceMs < 0 ? 0f : Tween.Apply(Ease.OutQuad, Tween.Progress((int)Math.Min(sinceMs, int.MaxValue), 0, InMs));
    }
}
