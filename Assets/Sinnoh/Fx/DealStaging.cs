using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 스탬프·도전·거래 화면 묶음(M5)의 연출 행(04 §8-8 47·51·52행): 여행 스탬프(470) · 트레이너 도전 VS(510) · 거래(520 — 주고받기 TradeDone).
    /// 길이는 방장과 같은 <c>PlayTiming</c>(470 = 1500 + 보상 줄마다 250 · 510 = 2800 · 520 = 2500)이고 여기 값은 그 안에서 화면이 나누는 박자다 [가정 — plan §5 #125·#126].
    /// 셋 다 무대의 맨 위 층(<c>UiStage.Over</c> — 창·배틀 화면 위)의 덮개 <c>DealFx</c>가 그린다: 스탬프는 받은 좌석의 화면에만(남의 것은 그 좌석 칩 꼬리표 「스탬프 n/7」 · 기록 줄 — 결정 178, 배틀 끝 항목에 실려 오면 <see cref="SplitOthers"/>가 보드로 뗀다),
    /// 도전 VS와 거래는 모든 좌석에(거래의 운명 카드는 남에게 장수만 온다 — 이벤트가 이미 가려서 온다).
    /// 거래 제안·응답(TradeProposed · TradeAnswered)과 도전 응답은 창(98행 — 대기열 밖)과 기록 줄이다.
    /// </summary>
    public static class DealRows
    {
        public const int Stamp = 470, Vs = 510, Trade = 520;

        /// <summary>이 묶음이 그리는 행인가.</summary>
        public static bool Built(int row) => row == Stamp || row == Vs || row == Trade;

        /// <summary>지금 재생 중인 그 행의 단계와 단계 안 시각(없으면 null · −1). 스탬프는 한 항목에 여러 단계가 이어질 수 있어 시각으로 고른다.</summary>
        public static FxStep Playing(FxSequencer seq, int row, out int localMs)
        {
            localMs = -1;
            var cur = seq != null ? seq.Current : null;
            if (cur == null) return null;
            foreach (var s in cur.Steps)
            {
                if (s.Row != row || seq.LocalMs < s.StartMs || seq.LocalMs >= s.EndMs) continue;
                localMs = seq.LocalMs - s.StartMs;
                return s;
            }
            return null;
        }

        public static T First<T>(FxStep step) where T : GameEvent => WorkRows.EventOf<T>(step);

        /// <summary>
        /// 남 좌석(me 밖)의 스탬프 단계가 배틀 끝 항목에 실려 오면 바로 뒤의 새 항목으로 뗀다(결정 178 — 결과 창 [확인] 뒤 보드에서 그 좌석 칩 꼬리표로 보인다, 길이의 합은 그대로 — P5).
        /// 뒤의 것부터 떼므로 새 항목은 받은 차례대로 선다. 내 스탬프는 결과 창 위에 찍으므로 떼지 않는다. 뗀 수를 돌려준다.
        /// </summary>
        public static int SplitOthers(List<FxEntry> entries, int me)
        {
            int made = 0, n;
            do
            {
                n = GodBeats.SplitOff(entries, e => LastOther(e, me));
                made += n;
            } while (n > 0);
            return made;
        }

        static FxStep LastOther(FxEntry entry, int me)
        {
            if (entry == null) return null;
            for (int i = entry.Steps.Count - 1; i >= 0; i--)
            {
                var st = entry.Steps[i];
                if (st.Row == Stamp && StampBeats.Event(st) is StampGained g && g.Seat >= 0 && g.Seat != me) return st;
            }
            return null;
        }
    }

    /// <summary>여행 스탬프 한 순간(<see cref="StampBeats.At"/>).</summary>
    public struct StampPose
    {
        /// <summary>덮개가 보이는 정도(0 ~ 1 — 처음 들어오고 끝에 걷힌다).</summary>
        public float Show;
        /// <summary>도장이 아직 떨어질 높이(1 = 위 ~ 0 = 찍힘).</summary>
        public float Drop;
        /// <summary>도장의 배율(1.8 → 1 — 찍히며 줄어든다).</summary>
        public float Scale;
        /// <summary>잉크 번짐(0 ~ 1 — 찍힌 자리에서 퍼지며 옅어진다, 없으면 −1).</summary>
        public float Ink;
        /// <summary>한 프레임 흔들림(px, 720 벌 — 0이면 없음).</summary>
        public float Shake;
        /// <summary>도장이 찍혔나(그 칸이 채워진 모습 · 스탬프 수가 오른 뒤).</summary>
        public bool Stamped;
        /// <summary>스탬프 수 숫자의 배율(1 = 그대로 — 찍힌 뒤 톡 커졌다 돌아온다).</summary>
        public float Count;
    }

    /// <summary>
    /// 여행 스탬프(04 §8-8 47행, S37)의 박자: 예비 = 도장이 위에서 내려옴(<see cref="DropMs"/>) → 맞음 = 쾅(1.8배 → 1배 · 잉크 번짐 · 한 프레임 흔들림 — <see cref="SlamAt"/>) →
    /// 움직임 = 스탬프 수 숫자(<see cref="CountAt"/>) → 착지 = 보상이 차례로 내려앉음(<see cref="Reward"/> — 규토리 색마다 · 기술 카드 묶음 · 수호신의 서).
    /// −8° 회전은 그리지 않는다(UI 연출은 옮기기·늘이기·흐리기만) [가정].
    /// </summary>
    public static class StampBeats
    {
        public const int Row = DealRows.Stamp;
        public const int InMs = 120, DropMs = 260, SlamAt = DropMs, SlamMs = 120, ShakeMs = 50, InkMs = 400, CountAt = 420, CountMs = 200,
            RewardFromMs = 700, RewardEveryMs = 250, RewardDropMs = 200, OutMs = 200;
        public const float ShakePx = 3f;

        /// <summary>그 단계의 스탬프 이벤트(없으면 null).</summary>
        public static StampGained Event(FxStep step) => DealRows.First<StampGained>(step);

        /// <summary>보상 줄 수: 자원 변화(규토리 색 · 수호신의 서)마다 + 기술 카드 묶음 하나.</summary>
        public static int Rewards(StampGained e) => e == null ? 0 : (e.Changes != null ? e.Changes.Length : 0) + (e.MoveKeys != null && e.MoveKeys.Length > 0 ? 1 : 0);

        public static StampPose At(int ms, int total)
        {
            var p = new StampPose { Ink = -1f, Scale = 1.8f, Drop = 1f, Count = 1f };
            if (ms < 0) return p;
            p.Show = ms >= total ? 0f : ms < InMs ? Tween.At(ms, 0, InMs, 0f, 1f) : Tween.At(ms, total - OutMs, OutMs, 1f, 0f);
            p.Drop = Tween.At(ms, 0, DropMs, 1f, 0f, Ease.InQuad);
            p.Stamped = ms >= SlamAt;
            p.Scale = Tween.At(ms, SlamAt, SlamMs, 1.8f, 1f, Ease.OutQuad);
            if (ms >= SlamAt && ms < SlamAt + ShakeMs) p.Shake = ShakePx;
            if (ms >= SlamAt && ms < SlamAt + InkMs) p.Ink = Tween.At(ms, SlamAt, InkMs, 0f, 1f, Ease.OutQuad);
            if (ms >= CountAt && ms < CountAt + CountMs) p.Count = 1f + 0.4f * (float)Math.Sin(Math.PI * (ms - CountAt) / CountMs);
            return p;
        }

        /// <summary>i번째 보상 줄이 내려앉기 시작하는 단계 안 시각.</summary>
        public static int RewardAt(int i) => RewardFromMs + RewardEveryMs * Math.Max(0, i);

        /// <summary>i번째 보상 줄: 보이는 만큼(0 ~ 1), drop = 아직 떨어질 높이(1 = 위 ~ 0 = 제자리).</summary>
        public static float Reward(int ms, int i, out float drop)
        {
            int t = ms - RewardAt(i);
            drop = t < 0 ? 1f : Tween.At(t, 0, RewardDropMs, 1f, 0f, Ease.OutQuad);
            return t < 0 ? 0f : Tween.At(t, 0, 100, 0f, 1f);
        }
    }

    /// <summary>도전 VS 한 순간(<see cref="VsBeats.At"/>).</summary>
    public struct VsPose
    {
        /// <summary>띠가 보이는 정도(0 ~ 1).</summary>
        public float Show;
        /// <summary>「!」(0 ~ 1 — 예비).</summary>
        public float Bang;
        /// <summary>양쪽 얼굴이 아직 들어올 거리(1 = 화면 밖 ~ 0 = 제자리, 부딪힐 때 살짝 음수).</summary>
        public float Slide;
        /// <summary>가운데 VS의 배율(0 = 아직 없음 · 2 → 1로 박힌다).</summary>
        public float Vs;
        /// <summary>VS가 박힐 때의 흰 빛(0 ~ 1).</summary>
        public float Flash;
    }

    /// <summary>
    /// 트레이너 도전(04 §8-8 51행, S35 — 2800)의 박자: 예비 = 「!」(<see cref="BangMs"/>) → 움직임 = 양쪽 얼굴이 미끄러져 들어와 부딪힘(<see cref="SlideFromMs"/>) →
    /// 맞음 = 가운데 VS가 박힘(<see cref="VsAt"/>) → 착지 = 띠가 걷히고 응답 프롬프트(S41 — 대상만) 또는 배틀 전환(22행).
    /// </summary>
    public static class VsBeats
    {
        public const int Row = DealRows.Vs;
        public const int InMs = 150, BangMs = 400, SlideFromMs = 300, SlideMs = 500, BumpMs = 160, VsAt = 900, VsMs = 150, FlashMs = 160, OutMs = 300;

        public static ChallengeIssued Event(FxStep step) => DealRows.First<ChallengeIssued>(step);

        public static VsPose At(int ms, int total)
        {
            var p = new VsPose { Slide = 1f };
            if (ms < 0 || ms >= total) return p;
            p.Show = ms < InMs ? Tween.At(ms, 0, InMs, 0f, 1f) : Tween.At(ms, total - OutMs, OutMs, 1f, 0f);
            p.Bang = ms < BangMs ? (ms < 100 ? Tween.At(ms, 0, 100, 0f, 1f, Ease.OutQuad) : Tween.At(ms, BangMs - 120, 120, 1f, 0f)) : 0f;
            int s = ms - SlideFromMs;
            if (s >= SlideMs + BumpMs) p.Slide = 0f;
            else if (s >= SlideMs) p.Slide = -0.06f * (float)Math.Sin(Math.PI * (s - SlideMs) / BumpMs);
            else if (s >= 0) p.Slide = Tween.At(s, 0, SlideMs, 1f, 0f, Ease.InQuad);
            if (ms >= VsAt) p.Vs = Tween.At(ms, VsAt, VsMs, 2f, 1f, Ease.OutQuad);
            if (ms >= VsAt && ms < VsAt + FlashMs) p.Flash = Tween.At(ms, VsAt, FlashMs, 1f, 0f);
            return p;
        }
    }

    /// <summary>거래 한 순간(<see cref="TradeBeats.At"/>).</summary>
    public struct TradePose
    {
        /// <summary>띠가 보이는 정도(0 ~ 1).</summary>
        public float Show;
        /// <summary>올린 것이 건너간 만큼(0 = 준 좌석 곁 ~ 0.35 = 가운데에 모임 ~ 1 = 받은 좌석 곁).</summary>
        public float Cross;
        /// <summary>받는 쪽 칸의 빛(0 ~ 1).</summary>
        public float Glow;
    }

    /// <summary>
    /// 거래(04 §8-8 52행, S36 — 2500)의 박자: 예비 = 양쪽이 올린 것이 가운데로 모임(<see cref="GatherMs"/>) → 움직임 = 엇갈려 건너감(<see cref="CrossFromMs"/>) →
    /// 맞음 = 받는 쪽 칸이 빛남(<see cref="GlowAt"/>) → 착지 = 받은 수가 표시 값으로 내려앉는다(단계 끝). 원격이면 띠 머리에 「원격」 꼬리표(두 말 사이 빛줄기 대신) [가정].
    /// </summary>
    public static class TradeBeats
    {
        public const int Row = DealRows.Trade;
        public const int InMs = 150, GatherMs = 500, CrossFromMs = 700, CrossMs = 800, GlowAt = CrossFromMs + CrossMs, GlowMs = 400, OutMs = 300;
        public const float Mid = 0.35f;

        public static TradeDone Event(FxStep step) => DealRows.First<TradeDone>(step);

        public static TradePose At(int ms, int total)
        {
            var p = new TradePose();
            if (ms < 0 || ms >= total) return p;
            p.Show = ms < InMs ? Tween.At(ms, 0, InMs, 0f, 1f) : Tween.At(ms, total - OutMs, OutMs, 1f, 0f);
            p.Cross = ms < CrossFromMs ? Tween.At(ms, 0, GatherMs, 0f, Mid, Ease.OutQuad) : Tween.At(ms, CrossFromMs, CrossMs, Mid, 1f, Ease.InOutQuad);
            if (ms >= GlowAt && ms < GlowAt + GlowMs) p.Glow = (float)Math.Sin(Math.PI * (ms - GlowAt) / GlowMs);
            return p;
        }
    }
}
