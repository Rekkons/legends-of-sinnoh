using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>턴 시작 배너의 한 순간(<see cref="TurnStartBeats.Banner"/>): 보이는 만큼(0 ~ 1)과 세로 자리(−1 = 위 밖 ~ 0 = 제자리).</summary>
    public struct BannerPose
    {
        public float Shown, Slide;
    }

    /// <summary>라운드 띠의 한 순간(<see cref="TurnStartBeats.Round"/>): 보이나 · 가로 자리(−100 = 왼쪽 밖 ~ 0 = 제자리 ~ 100 = 오른쪽 밖, 폭의 %).</summary>
    public struct RoundPose
    {
        public bool Shown;
        public float X;
    }

    /// <summary>
    /// 턴 시작(04 §8-8 6행 — 배너 · 카메라 · 이동력, 결정 70 ① A1 나)의 시간표. 길이는 방장과 같은 값(<c>PlayTiming.RowMs</c> 60행 — 1400)이고 여기는 그 안의 박자다:
    /// 남의 턴이면 위쪽에 배너(얼굴 상자 + 「○○의 턴」)가 미끄러져 내려와 머물다 올라가고 카메라가 그 말로 간다(내 턴은 101행 컷신이 배너를 겸해 배너·카메라 없음 [가정]) →
    /// 내 턴이면 「남은 이동력」이 단계 처음부터 하나씩(<see cref="ChipEveryMs"/>) 차오른다(내 턴에 그리는 것은 이것뿐이라 컷신이 끝나자마자 시작한다).
    /// 라운드의 첫 턴이면 같은 단계의 처음 <see cref="RoundMs"/> 동안 라운드 띠(102행 — 대기열 길이를 더하지 않는다)가 가운데를 지나간다.
    /// 한 좌석 판에는 이 단계가 없다(<see cref="FxBuilt.MultiOnly"/> — 넘김 띠가 라운드를 적고 컷신이 턴을 알린다, 결정 57).
    /// </summary>
    public static class TurnStartBeats
    {
        public const int Row = 60;

        /// <summary>배너: 들어옴 = 길이 / 8 · 나가기 시작 = 길이의 5/8 · 나감 = 길이 / 8.</summary>
        public const int SlidePart = 8, OutFromEighths = 5;
        /// <summary>이동력이 하나 차오르는 간격(ms).</summary>
        public const int ChipEveryMs = 100;
        /// <summary>라운드 띠 길이(04 §8-5 공용 수치 「라운드 띠 900」) — 들어옴·나감은 그 1/4씩.</summary>
        public const int RoundMs = 900, RoundSlidePart = 4;

        /// <summary>그 항목의 턴 시작 단계(없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Started(step) != null ? step : null;
        }

        /// <summary>그 단계가 실은 턴 시작 이벤트(없으면 null).</summary>
        public static TurnStarted Started(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is TurnStarted t) return t;
            return null;
        }

        /// <summary>그 항목에서 그 좌석이 받은 이동력(이벤트가 없으면 −1).</summary>
        public static int Allowance(FxEntry entry, int seat)
        {
            if (entry == null) return -1;
            foreach (var e in entry.Events)
                if (e is MoveAllowanceSet m && m.Seat == seat) return m.Total;
            return -1;
        }

        public static BannerPose Banner(int ms, int total)
        {
            var p = new BannerPose { Slide = -1f };
            if (total <= 0 || ms < 0 || ms >= total) return p;
            int slide = total / SlidePart, outAt = total * OutFromEighths / 8;
            if (ms >= outAt + slide) return p;
            p.Shown = 1f;
            p.Slide = ms < outAt ? Tween.At(ms, 0, slide, -1f, 0f, Ease.OutQuad) : Tween.At(ms, outAt, slide, 0f, -1f, Ease.InQuad);
            return p;
        }

        /// <summary>그 시각에 켜진 이동력 수(0 ~ n): 단계가 시작할 때 첫 칸, 그 뒤 100ms마다 하나(칸이 많으면 단계 끝에 다 켜지게 줄인다). 단계 전에는 0, 단계가 끝나면 n.</summary>
        public static int Lit(int ms, int total, int n)
        {
            if (n <= 0) return 0;
            if (total <= 0 || ms >= total) return n;
            if (ms < 0) return 0;
            int every = Math.Max(1, Math.Min(ChipEveryMs, total / n));
            return Math.Min(n, ms / every + 1);
        }

        public static RoundPose Round(int ms)
        {
            var p = new RoundPose { X = -100f };
            if (ms < 0 || ms >= RoundMs) return p;
            int slide = RoundMs / RoundSlidePart;
            p.Shown = true;
            p.X = ms < RoundMs - slide ? Tween.At(ms, 0, slide, -100f, 0f, Ease.OutQuad) : Tween.At(ms, RoundMs - slide, slide, 0f, 100f, Ease.InQuad);
            return p;
        }
    }

    /// <summary>방패 배지의 한 순간(<see cref="ShieldBeats.At"/>): 밝아졌나 · 금이 갔나 · 떨어진 만큼(0 ~ 1 — 아래로 떨어지며 흐려진다).</summary>
    public struct ShieldPose
    {
        public float Fall;
        public bool Lit, Cracked;
    }

    /// <summary>
    /// 도전 보호 해제(04 §8-8 9행 — 턴 시작 항목 안 단계)의 시간표. 길이는 방장과 같은 값(90행 — 500):
    /// 그 좌석 칩의 보호 배지가 밝아짐(처음부터 — 픽셀 글자를 늘리지 않고 색으로 알린다) → 금이 감(<see cref="CrackPermille"/> — 밝은 판이 꺼지고 글이 빨개진다) → 아래로 떨어지며 사라짐(<see cref="FallPermille"/>부터 끝까지).
    /// </summary>
    public static class ShieldBeats
    {
        public const int Row = 90;
        public const int CrackPermille = 500, FallPermille = 600;

        /// <summary>그 항목의 보호 해제 단계(없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Seat(step) >= 0 ? step : null;
        }

        /// <summary>보호가 풀린 좌석(없으면 −1).</summary>
        public static int Seat(FxStep step)
        {
            if (step == null) return -1;
            foreach (var e in step.Events)
                if (e is ProtectionCleared c) return c.Seat;
            return -1;
        }

        public static ShieldPose At(int ms, int total)
        {
            var p = new ShieldPose();
            if (total <= 0 || ms >= total) { p.Cracked = true; p.Fall = 1f; return p; }
            if (ms <= 0) return p;
            int crack = total * CrackPermille / 1000, fall = total * FallPermille / 1000;
            p.Cracked = ms >= crack;
            p.Lit = !p.Cracked;
            p.Fall = Tween.At(ms, fall, total - fall, 0f, 1f, Ease.InQuad);
            return p;
        }

        /// <summary>그 좌석의 보호 해제가 아직 다 보이지 않았나(대기열에 남았다 — 보기는 이미 풀렸어도 배지를 남겨 둔다).</summary>
        public static bool Pending(FxSequencer seq, int seat)
        {
            if (seq == null) return false;
            foreach (var entry in seq.Queued)
            {
                var step = StepOf(entry);
                if (step == null || Seat(step) != seat) continue;
                if (!ReferenceEquals(entry, seq.Current) || seq.LocalMs < step.EndMs) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 같은 칸의 말을 만남(04 §8-8 13행 — 대기열 밖 행: 걷기 항목 끝에 붙고 길이를 더하지 않는다). 먼저 있던 말이 옆자리로 비키는 대신 묶음 표지로 묶이고(결정 66 ①),
    /// 마지막 걸음이 닿는 때부터 <see cref="Ms"/> 동안 걸어 들어온 말 머리 위에 「!」가 뜬다. 그 칸에 다른 말이 있는지는 화면이 보기에서 센다.
    /// </summary>
    public static class MeetBeats
    {
        public const int Ms = 400;

        /// <summary>그 항목의 마지막 걸음 단계가 닿은 뒤 지난 시간(항목 시각 기준). 걸음이 없거나 아직 닿지 않았으면 −1.</summary>
        public static int Since(FxEntry entry, int entryMs, out Moved moved)
        {
            moved = null;
            if (entry == null) return -1;
            FxStep walk = null;
            foreach (var s in entry.Steps)
                if (s.Row == 110 && s.Events.Count > 0 && s.Events[0] is Moved) walk = s;
            if (walk == null) return -1;
            moved = (Moved)walk.Events[0];
            int landed = FxWalk.LandedAt(moved);
            if (landed < 0 || moved.Path == null || moved.Path.Length < 2) return -1;
            int since = entryMs - (walk.StartMs + landed);
            return since < 0 ? -1 : since;
        }
    }

    /// <summary>규토리 줍기의 한 순간(<see cref="ApricornBeats.At"/>).</summary>
    public struct ApricornPose
    {
        /// <summary>말이 나무 쪽으로 툭 내민 만큼(0 ~ 1 ~ 0) · 흔들림(−1 ~ 1 — 두 번).</summary>
        public float Nudge, Shake;
        /// <summary>줍는 것이 모여 사라진 만큼(0 ~ 1 — 떨어진 규토리가 말 쪽으로 모이며 흐려진다) · 「규토리 +n」 글이 보이는 만큼 · 떠오른 만큼(0 ~ 1).</summary>
        public float Gather, Label, Rise;
    }

    /// <summary>
    /// 규토리 줍기(04 §8-8 19행 — 빈 찾아보기 뒤 나무·산호의 규토리를 줍고 턴이 끝난다)의 시간표. 길이는 방장과 같은 값(190행 — 2400):
    /// 말이 툭 내밈(길이 / 8) → 두 번 흔들림(3/8까지) → 규토리가 하나씩 떨어져 튕김(<see cref="DropEveryMs"/> 어긋남) → 길이의 3/4부터 말 쪽으로 모이며 「규토리 +n」이 떠오름(착지 = 단계 끝).
    /// 나무·산호 소품과 가방 칩이 아직 없어 떨어지는 규토리는 그 칸 위에, 글은 말 머리 위에 그린다 [가정].
    /// </summary>
    public static class ApricornBeats
    {
        public const int Row = 190;
        public const int NudgePart = 8, DropFromEighths = 3, GatherFromEighths = 6;
        public const int DropEveryMs = 150, FallMs = 260, BounceMs = 180;

        /// <summary>그 항목의 규토리 줍기 단계(없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Count(step) > 0 ? step : null;
        }

        /// <summary>그 단계에서 주운 규토리 수(색마다 이벤트 하나 — 합).</summary>
        public static int Count(FxStep step)
        {
            int n = 0;
            if (step != null)
                foreach (var e in step.Events)
                    if (e is ApricornsTaken a && a.Count > 0) n += a.Count;
            return n;
        }

        /// <summary>주운 규토리의 색(떨어지는 순서 — 이벤트 순서대로 개수만큼).</summary>
        public static List<int> Colors(FxStep step)
        {
            var colors = new List<int>();
            if (step != null)
                foreach (var e in step.Events)
                    if (e is ApricornsTaken a)
                        for (int i = 0; i < a.Count; i++) colors.Add(a.Color);
            return colors;
        }

        /// <summary>줍는 좌석과 칸(없으면 −1).</summary>
        public static int Seat(FxStep step, out int cell)
        {
            cell = -1;
            if (step != null)
                foreach (var e in step.Events)
                    if (e is ApricornsTaken a) { cell = a.Cell; return a.Seat; }
            return -1;
        }

        /// <summary>
        /// 확인용(시험·캡처): 규토리 줍기가 열리는 판을 세운다 — 조우 덱 셋(약한·적당한·강한)을 비우고 그 좌석을 규토리가 달린 나무 칸에 세운다. 거기서 [찾아보기]를 하면 조우가 없어 [규토리 줍기]가 열린다
        /// (엔진의 길 그대로 — 실제 판에서는 그 칸의 지형·기후에 맞는 카드가 덱에 없을 때만 일어난다: 봇 판 60개 × 300명령에서 0번). 세운 칸을 돌려준다(나무가 없으면 −1).
        /// </summary>
        public static int Stage(Ctx ctx, GameState state, int seat)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (state == null) throw new ArgumentNullException(nameof(state));
            foreach (var deck in new[] { DeckId.Weak, DeckId.Moderate, DeckId.Strong }) state.Decks[(int)deck].Clear();
            foreach (var t in state.Tokens)
            {
                if (t.Kind != TokenKind.ApricornTree || t.Destroyed || t.Cell < 0 || ctx.Fixtures.Fixtures[t.Def].Apricorns.Count == 0) continue;
                state.Seats[seat].Cell = t.Cell;
                return t.Cell;
            }
            return -1;
        }

        /// <summary>i번째 규토리가 떨어지기 시작하는 단계 안 시각.</summary>
        public static int DropAt(int total, int i) => total * DropFromEighths / 8 + DropEveryMs * Math.Max(0, i);

        /// <summary>i번째 규토리의 높이(−1 = 아직 없음, 1 = 위에서 떨어지기 시작 ~ 0 = 바닥. 닿은 뒤 한 번 낮게 튄다).</summary>
        public static float Height(int ms, int total, int i)
        {
            int at = DropAt(total, i);
            if (ms < at) return -1f;
            int t = ms - at;
            if (t < FallMs) return Tween.At(t, 0, FallMs, 1f, 0f, Ease.InQuad);
            if (t < FallMs + BounceMs) return 0.25f * (float)Math.Sin(Math.PI * (t - FallMs) / BounceMs);
            return 0f;
        }

        public static ApricornPose At(int ms, int total)
        {
            var p = new ApricornPose();
            if (total <= 0 || ms <= 0) return p;
            if (ms >= total) { p.Gather = 1f; return p; }
            int nudge = total / NudgePart, drop = total * DropFromEighths / 8, gather = total * GatherFromEighths / 8;
            if (ms < nudge) p.Nudge = (float)Math.Sin(Math.PI * ms / nudge);
            else if (ms < drop) p.Shake = (float)Math.Sin(Math.PI * 4.0 * (ms - nudge) / (drop - nudge));
            p.Gather = Tween.At(ms, gather, (total - gather) / 2, 0f, 1f, Ease.InQuad);
            p.Label = ms < gather ? 0f : 1f - Tween.Progress(ms, total - (total - gather) / 4, (total - gather) / 4);
            p.Rise = Tween.At(ms, gather, total - gather, 0f, 1f, Ease.OutQuad);
            return p;
        }
    }

    /// <summary>
    /// 동시·비공개 선택 공개(04 §8-8 99행 — 셋업의 스타팅 포켓몬·트레이너 카드 묶음)의 시간표. 길이는 방장과 같은 값(990행 — 2000):
    /// 고르는 동안 좌석 칩마다 「고르는 중」/「선택 완료」만 보이다가, 모두 답하면 같은 박자에 좌석마다 <see cref="EveryMs"/>씩 어긋나게 한꺼번에 뒤집혀 고른 것이 드러난다.
    /// 한 좌석 판에는 이 단계가 없다(<see cref="FxBuilt.MultiOnly"/> — 셋업 1·3행 공개가 같은 순간을 그린다).
    /// </summary>
    public static class RevealAllBeats
    {
        public const int Row = 990;
        public const int LeadMs = 200, EveryMs = 60, FlipMs = 300;

        /// <summary>그 항목의 동시 공개 단계(없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Revealed(step) != null ? step : null;
        }

        public static SecretRevealed Revealed(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is SecretRevealed r && r.Window == Window.None) return r;
            return null;
        }

        /// <summary>i번째 좌석 칩이 뒤집히기 시작하는 단계 안 시각.</summary>
        public static int FlipAt(int i) => LeadMs + EveryMs * Math.Max(0, i);

        /// <summary>i번째 칩의 가로 늘이기(1 → 0 → 1)와 앞면인가(가운데에서 바뀐다). 단계가 아직 오지 않았으면(ms &lt; 0) 뒷면 그대로.</summary>
        public static float Flip(int ms, int i, out bool face)
        {
            int t = ms - FlipAt(i);
            face = t >= FlipMs / 2;
            if (t <= 0 || t >= FlipMs) return 1f;
            return Math.Abs(1f - 2f * t / FlipMs);
        }
    }
}
