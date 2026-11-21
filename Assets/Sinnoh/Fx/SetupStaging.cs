using System;
using Rekkon.UiKit;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 셋업 연출의 행(04 §8-8 1~3행 — 결정 66 ③): 트레이너 카드 받기(10) · 고른 뒤(11) · 공개(12) · 고향 마을 고르기(20) · 스타팅 포켓몬 고르기(30).
    /// 길이는 방장과 같은 값(<c>PlayTiming.RowMs</c>)이고, 여기는 그 단계가 대기열의 어디쯤인지 묻는 것들이다 — 화면(고르는 화면 · 마을 띠 · 보드)이 이것으로만 모습을 정한다.
    /// </summary>
    public static class SetupRows
    {
        public const int Deal = 10, Chosen = 11, Reveal = 12, Village = 20, Starter = 30;

        /// <summary><see cref="At"/>의 답: 그 단계가 대기열에 있지만 아직 시작하지 않았다 · 대기열에 없다(이미 지났거나 아직 받지 않았다).</summary>
        public const int Waiting = int.MinValue, Gone = int.MaxValue;

        public static bool Built(int row) => row == Deal || row == Chosen || row == Reveal || row == Village || row == Starter;

        /// <summary>그 항목의 셋업 단계가 끝나는 항목 시각(셋업 단계가 없으면 −1).</summary>
        public static int EndOf(FxEntry entry)
        {
            int end = -1;
            if (entry != null)
                foreach (var s in entry.Steps)
                    if (Built(s.Row)) end = Math.Max(end, s.EndMs);
            return end;
        }

        /// <summary>대기열에 아직 다 보이지 않은 셋업 단계가 있나 — 있는 동안 고르는 화면이 남고 보드 HUD는 뜨지 않는다.</summary>
        public static bool Pending(FxSequencer seq)
        {
            if (seq == null) return false;
            foreach (var entry in seq.Queued)
            {
                int end = EndOf(entry);
                if (end >= 0 && (!ReferenceEquals(entry, seq.Current) || seq.LocalMs < end)) return true;
            }
            return false;
        }

        /// <summary>그 행의 단계가 대기열에 있고 아직 끝나지 않았나.</summary>
        public static bool Pending(FxSequencer seq, int row)
        {
            int at = At(seq, row, null, out var step);
            return at == Waiting || (at != Gone && at < step.Ms);
        }

        /// <summary>
        /// 그 행의 단계 안 시각: 재생 중이면 0 ~ 길이(끝났지만 그 항목이 아직 재생 중이면 길이), 기다리는 중이면 <see cref="Waiting"/>, 대기열에 없으면 <see cref="Gone"/>.
        /// <paramref name="tag"/>가 있으면 그 꼬리표(<see cref="FxStep.Tag"/>)가 달린 단계만 친다(「고른 뒤」 11행 — 무엇을 고른 답인가).
        /// </summary>
        public static int At(FxSequencer seq, int row, object tag, out FxStep step)
        {
            step = null;
            if (seq == null) return Gone;
            foreach (var entry in seq.Queued)
                foreach (var s in entry.Steps)
                {
                    if (s.Row != row || (tag != null && !tag.Equals(s.Tag))) continue;
                    step = s;
                    if (!ReferenceEquals(entry, seq.Current) || seq.LocalMs < s.StartMs) return Waiting;
                    return Math.Min(s.Ms, seq.LocalMs - s.StartMs);
                }
            return Gone;
        }

        /// <summary><see cref="At"/>의 답을 박자 함수에 넣을 시각으로: 기다리는 중 = −1(처음 모습), 대기열에 없음 = <paramref name="done"/>이면 길이(끝 모습) · 아니면 −1.</summary>
        public static int Ms(int at, FxStep step, bool done, int fallbackTotal, out int total)
        {
            total = step != null ? step.Ms : fallbackTotal;
            if (at == Waiting) return -1;
            if (at == Gone) return done ? total : -1;
            return at;
        }
    }

    /// <summary>받은 카드 한 장의 모습(<see cref="DealBeats.Card"/>).</summary>
    public struct DealPose
    {
        /// <summary>덱 쪽으로 물러나 있는 만큼(1 = 화면 밖 ~ 0 = 제자리).</summary>
        public float Slide;
        /// <summary>앞면이 보이나 · 가로로 늘인 만큼(뒤집히는 동안 1 → 0 → 1).</summary>
        public bool Face;
        public float ScaleX;
        /// <summary>나타났나(자기 차례가 오기 전에는 보이지 않는다).</summary>
        public bool Shown;
    }

    /// <summary>
    /// 트레이너 카드 3장 받기(04 §8-8 1행 「들어옴」 — 800): 뒷면 3장이 덱 쪽에서 부채꼴로 어긋나게 미끄러져 들어오고, 자리에 닿으면 내 화면에서 앞면으로 뒤집힌다.
    /// 구간은 길이의 비율이다(§8-5 규칙 9 — 800이면 어긋남 60 · 미끄러짐 400 · 뒤집기 240).
    /// </summary>
    public static class DealBeats
    {
        public const int EveryPermille = 75, SlidePermille = 500, FlipPermille = 300;

        /// <param name="ms">단계 안 시각(음수 = 아직 — 보이지 않는다, 길이 이상 = 다 받았다).</param>
        public static DealPose Card(int index, int ms, int total)
        {
            var p = new DealPose { Face = true, ScaleX = 1f, Shown = true };
            if (ms < 0) return new DealPose { Slide = 1f, ScaleX = 1f };
            if (total <= 0 || ms >= total) return p;
            int at = total * EveryPermille / 1000 * Math.Max(0, index), slide = total * SlidePermille / 1000, flip = total * FlipPermille / 1000, half = flip / 2;
            p.Shown = ms >= at;
            p.Slide = 1f - Tween.At(ms, at, slide, 0f, 1f, Ease.OutQuad);
            int t = ms - at - slide;
            if (t <= 0) p.Face = false;
            else if (t < flip)
            {
                p.Face = t >= half;
                p.ScaleX = t < half ? 1f - Tween.Progress(t, 0, half) : Tween.Progress(t, half, flip - half);
            }
            return p;
        }
    }

    /// <summary>고른 뒤의 모습(<see cref="ChosenBeats.At"/>).</summary>
    public struct ChosenPose
    {
        /// <summary>고르지 않은 것이 덱으로 쓸려 간 만큼(0 ~ 1).</summary>
        public float Away;
        /// <summary>고른 것이 가려진 만큼(0 ~ 1): 카드는 뒷면으로 뒤집히고(0.5에서 그림이 바뀐다) 스타팅 포켓몬은 볼로 들어간다 — 모두 고를 때까지 비공개다.</summary>
        public float Hide;
        /// <summary>가려진 뒤의 좌우 흔들림(−1 ~ 1).</summary>
        public float Wobble;
    }

    /// <summary>
    /// 고른 뒤(04 §8-8 1행 「고른 뒤」 · 3행 예비 — 11행 1200): 고르지 않은 것이 덱으로 쓸려 가고, 고른 것은 가려진 채(카드 = 뒷면 · 포켓몬 = 볼) 한 번 흔들리며 공개를 기다린다.
    /// </summary>
    public static class ChosenBeats
    {
        public const int AwayPermille = 350, HideFromPermille = 300, HideToPermille = 700;

        /// <param name="ms">단계 안 시각(음수 = 고르기 전 모습, 길이 이상 = 끝 모습).</param>
        public static ChosenPose At(int ms, int total)
        {
            var p = new ChosenPose();
            if (ms < 0) return p;
            if (total <= 0 || ms >= total) { p.Away = p.Hide = 1f; return p; }
            int from = total * HideFromPermille / 1000, to = total * HideToPermille / 1000;
            p.Away = Tween.At(ms, 0, total * AwayPermille / 1000, 0f, 1f, Ease.OutQuad);
            p.Hide = Tween.At(ms, from, to - from, 0f, 1f, Ease.InOutQuad);
            if (ms >= to)
            {
                float t = (ms - to) / (float)(total - to);
                p.Wobble = (float)(Math.Sin(t * Math.PI * 2.0) * (1.0 - t));
            }
            return p;
        }

        /// <summary>가려지는 뒤집기의 가로 늘이기(1 → 0 → 1)와 앞면이 보이나.</summary>
        public static float FlipScale(float hide, out bool face)
        {
            face = hide < 0.5f;
            return Math.Abs(1f - 2f * Math.Max(0f, Math.Min(1f, hide)));
        }
    }

    /// <summary>공개되는 카드의 모습(<see cref="RevealBeats.At"/>).</summary>
    public struct RevealPose
    {
        /// <summary>들린 만큼(0 ~ 1).</summary>
        public float Lift;
        public bool Face;
        public float ScaleX;
        /// <summary>테두리 금속광이 카드를 가로지른 만큼(−1 = 없음, 0 ~ 1).</summary>
        public float Gloss;
        /// <summary>내 자리로 내려앉은 만큼(0 ~ 1 — 줄어들며 사라진다).</summary>
        public float Land;
    }

    /// <summary>
    /// 트레이너 카드 공개(04 §8-8 1행 「전원 공개」 — 12행 2500): 가려 둔 카드가 들리며 앞면으로 가로 뒤집히고, 금속광이 한 번 지나간 뒤 머물다 내 자리로 내려앉는다.
    /// </summary>
    public static class RevealBeats
    {
        public const int LiftPermille = 80, FlipToPermille = 240, GlossToPermille = 440, LandFromPermille = 800;

        /// <param name="ms">단계 안 시각(음수 = 뒷면으로 기다림, 길이 이상 = 내려앉았다).</param>
        public static RevealPose At(int ms, int total)
        {
            var p = new RevealPose { ScaleX = 1f, Gloss = -1f };
            if (ms < 0) return p;
            if (total <= 0 || ms >= total) { p.Face = true; p.Land = 1f; return p; }
            int lift = total * LiftPermille / 1000, flipTo = total * FlipToPermille / 1000, glossTo = total * GlossToPermille / 1000, land = total * LandFromPermille / 1000;
            p.Lift = Tween.At(ms, 0, lift, 0f, 1f, Ease.OutQuad);
            int half = (flipTo - lift) / 2, t = ms - lift;
            p.Face = t >= half;
            if (t > 0 && t < flipTo - lift) p.ScaleX = t < half ? 1f - Tween.Progress(t, 0, half) : Tween.Progress(t, half, flipTo - lift - half);
            if (ms >= flipTo && ms < glossTo) p.Gloss = Tween.Progress(ms, flipTo, glossTo - flipTo);
            p.Land = Tween.At(ms, land, total - land, 0f, 1f, Ease.InQuad);
            return p;
        }
    }

    /// <summary>스타팅 포켓몬 공개의 모습(<see cref="StarterBeats.At"/>).</summary>
    public struct StarterPose
    {
        /// <summary>닫힌 볼이 보이나 · 좌우 흔들림(−1 ~ 1) · 튀어 오른 높이(0 ~ 1) · 아래로 떨어지며 사라진 만큼(0 ~ 1).</summary>
        public bool Ball;
        public float Wobble, Hop, Drop;
        /// <summary>열린 볼이 보이는 만큼(0 ~ 1) · 흰 빛(1 → 0).</summary>
        public float Open, Flash;
        /// <summary>도트의 크기(0 ~ 1).</summary>
        public float Mon;
    }

    /// <summary>
    /// 스타팅 포켓몬 고르기(04 §8-8 3행 — 30행 2200): 볼이 좌우로 흔들리다 튀어 오르며 열리고, 흰 빛이 걷히며 도트가 나타나 머문 뒤, 볼로 줄어 아래(내 파티 쪽)로 떨어진다.
    /// </summary>
    public static class StarterBeats
    {
        public const int WobblePermille = 150, OpenPermille = 300, FlashToPermille = 500, MonToPermille = 420, BackPermille = 780, BallPermille = 860;

        /// <param name="ms">단계 안 시각(음수 = 볼이 기다린다, 길이 이상 = 파티로 들어갔다).</param>
        public static StarterPose At(int ms, int total)
        {
            var p = new StarterPose { Ball = true };
            if (ms < 0) return p;
            if (total <= 0 || ms >= total) { p.Drop = 1f; return p; }
            int wobble = total * WobblePermille / 1000, open = total * OpenPermille / 1000, flashTo = total * FlashToPermille / 1000, monTo = total * MonToPermille / 1000;
            int back = total * BackPermille / 1000, ball = total * BallPermille / 1000;
            if (ms < wobble) p.Wobble = (float)Math.Sin(ms / (float)wobble * Math.PI * 4.0);
            else if (ms < open) p.Hop = (float)Math.Sin((ms - wobble) / (float)(open - wobble) * Math.PI);
            if (ms >= open && ms < ball)
            {
                p.Ball = false;
                p.Open = 1f - Tween.Progress(ms, monTo, flashTo - monTo);
                p.Flash = 1f - Tween.Progress(ms, open, flashTo - open);
                p.Mon = ms < back ? Tween.At(ms, open, monTo - open, 0f, 1f, Ease.OutQuad) : 1f - Tween.Progress(ms, back, ball - back);
            }
            if (ms >= ball) p.Drop = Tween.At(ms, ball, total - ball, 0f, 1f, Ease.InQuad);
            return p;
        }
    }

    /// <summary>고향 마을 고르기의 한 순간(<see cref="VillageBeats.At"/>).</summary>
    public struct VillagePose
    {
        /// <summary>카메라가 마을로 다가간 만큼(0 ~ 1).</summary>
        public float Glide;
        /// <summary>마을 칸 테두리의 맥박이 켜져 있나.</summary>
        public bool Pulse;
        /// <summary>말이 떨어진 만큼(−1 = 아직 안 나타남, 0 = 위 ~ 1 = 내려앉음) · 내려앉은 뒤 한 번 튀는 높이(0 ~ 1).</summary>
        public float Fall, Hop;
        /// <summary>내려앉았다 — 좌석 칩이 회색에서 자리 색으로 바뀌는 때.</summary>
        public bool Landed;
    }

    /// <summary>
    /// 고향 마을 고르기(04 §8-8 2행 — 20행 1800): 카메라가 고른 마을로 미끄러지고 마을 칸 테두리가 두 번 맥박 친 뒤, 말이 위에서 떨어져 내려앉아 한 번 튄다.
    /// 내려앉을 때 좌석 칩이 자리 색으로 칠해진다. 구간은 길이의 비율이다.
    /// </summary>
    public static class VillageBeats
    {
        public const int Row = SetupRows.Village;
        public const int GlidePermille = 250, PulseFromPermille = 100, PulseEveryPermille = 150, Pulses = 2, FallFromPermille = 400, LandPermille = 620, HopToPermille = 740;

        /// <summary>그 항목의 고향 마을 단계(없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Picked(step) != null ? step : null;
        }

        /// <summary>그 단계가 실은 고향 마을 고르기 이벤트(없으면 null).</summary>
        public static VillagePicked Picked(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is VillagePicked v) return v;
            return null;
        }

        /// <summary>말이 나타나는(떨어지기 시작하는) 단계 안 시각.</summary>
        public static int FallAt(int total) => total * FallFromPermille / 1000;

        /// <param name="ms">단계 안 시각.</param>
        public static VillagePose At(int ms, int total)
        {
            var p = new VillagePose { Fall = -1f };
            if (ms < 0) return p;
            if (total <= 0 || ms >= total) { p.Glide = 1f; p.Fall = 1f; p.Landed = true; return p; }
            int fall = FallAt(total), land = total * LandPermille / 1000, hopTo = total * HopToPermille / 1000;
            int pulseFrom = total * PulseFromPermille / 1000, every = Math.Max(1, total * PulseEveryPermille / 1000);
            p.Glide = Tween.At(ms, 0, total * GlidePermille / 1000, 0f, 1f, Ease.InOutQuad);
            p.Pulse = ms >= pulseFrom && ms < pulseFrom + every * Pulses && (ms - pulseFrom) % every < every / 2;
            if (ms >= fall) p.Fall = Tween.At(ms, fall, land - fall, 0f, 1f, Ease.InQuad);
            p.Landed = ms >= land;
            if (ms >= land && ms < hopTo) p.Hop = (float)Math.Sin((ms - land) / (float)(hopTo - land) * Math.PI);
            return p;
        }
    }

    /// <summary>
    /// 게임 시작의 숨겨진 장소 배치(04 §8-8 5행의 뒷부분 — 50행, 마을 없는 TokensPlaced 1500): 카메라가 전체 줌으로 물러나고 「?」 뒷면 토큰이 칸 색인 순서로 톡톡 떨어진다
    /// (놓은 순서가 정체를 흘리지 않게 — B2. 이벤트의 칸 목록이 이미 칸 색인 순서다). 토큰 사이 간격은 남은 시간을 토큰 수로 나눈 값이다.
    /// </summary>
    public static class SpotDropBeats
    {
        public const int Row = 50;
        public const int LeadMs = 300, FallMs = 280, TailMs = 120, SquashMs = 120;

        /// <summary>그 항목의 숨겨진 장소 배치 단계(없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Spots(step) != null ? step : null;
        }

        /// <summary>그 단계가 실은 숨겨진 장소 배치 이벤트(없으면 null).</summary>
        public static TokensPlaced Spots(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is TokensPlaced tp && tp.Kind == TokenKind.HiddenSpot && tp.Cells != null && tp.Cells.Length > 0) return tp;
            return null;
        }

        /// <summary>카메라가 전체 줌으로 물러난 만큼(0 ~ 1 — 처음 <see cref="LeadMs"/> 동안).</summary>
        public static float Wide(int ms) => Tween.At(ms, 0, LeadMs, 0f, 1f, Ease.InOutQuad);

        /// <summary><paramref name="index"/>번째 토큰이 떨어지기 시작하는 단계 안 시각.</summary>
        public static int DropAt(int total, int index, int count)
        {
            int span = Math.Max(0, total - LeadMs - FallMs - TailMs);
            return LeadMs + (count > 1 ? (int)((long)span * Math.Max(0, Math.Min(index, count - 1)) / (count - 1)) : 0);
        }

        /// <summary>그 토큰이 떨어진 만큼: −1 = 아직 안 나타남, 0 = 위 ~ 1 = 내려앉음.</summary>
        public static float Fall(int ms, int total, int index, int count)
        {
            int at = DropAt(total, index, count);
            if (ms < at) return -1f;
            return Tween.At(ms, at, FallMs, 0f, 1f, Ease.InQuad);
        }

        /// <summary>내려앉은 뒤의 눌림(1 = 제 모양, 내려앉는 순간 가장 납작 → 돌아옴).</summary>
        public static float Squash(int ms, int total, int index, int count)
        {
            int at = DropAt(total, index, count) + FallMs;
            if (ms < at || ms >= at + SquashMs) return 1f;
            return Tween.At(ms, at, SquashMs, 0.7f, 1f, Ease.OutQuad);
        }
    }
}
