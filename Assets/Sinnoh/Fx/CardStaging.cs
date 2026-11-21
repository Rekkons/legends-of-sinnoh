using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 트레이너 카드 연출 행(M6 「트레이너 카드 해금 연출」 — 04 §8-8 35·48·49·50행): 날씨 휠(350 — 위 띠 날씨 배지가 돌림판처럼 돈다) · 해금(480 — 결정 84 M5 가: 보드 위 카드 뒤집기) ·
    /// 카드 바꾸기(490 — 반바지 꼬마 빨강, 옛 카드가 뒤집혀 새 카드 골드) · 능력 사용(500 — 출처 카드가 작게 떠올라 그 능력 칸이 빛나고 통통).
    /// 길이는 방장과 같은 <c>PlayTiming</c>(해금 3200 · 두 박자 +800 · 바꾸기 2000 · 능력 600 · 날씨 1800 — 남 좌석도 같다, #708)이고 여기 값은 그 안의 박자다 [가정 #162 ①].
    /// 그리는 곳: 해금·바꾸기·능력(내 것)은 무대의 맨 위 층 덮개(<c>CardFx</c> — 스탬프 덮개와 같은 층), 남 좌석의 해금·바꾸기·능력은 그 좌석 칩의 꼬리표(<see cref="OtherActs"/> — 결정 80 마 꼴) + 내 창이 없으면 같은 덮개(결정 120 ①),
    /// 날씨는 모든 좌석의 위 띠 배지(<c>BoardHud</c>). 배틀 안에서 생긴 해금은 결과 창 [확인] 뒤 보드에서 보인다(<see cref="UnlockHold"/>).
    /// </summary>
    public static class CardRows
    {
        public const int Weather = 350, Unlock = 480, Swap = 490, Ability = 500;

        /// <summary>이 묶음이 그리는 행인가.</summary>
        public static bool Built(int row) => row == Weather || row == Unlock || row == Swap || row == Ability;

        /// <summary>지금 재생 중인 그 행의 단계와 단계 안 시각(없으면 null · −1).</summary>
        public static FxStep Playing(FxSequencer seq, int row, out int localMs) => DealRows.Playing(seq, row, out localMs);

        public static T First<T>(FxStep step) where T : GameEvent => WorkRows.EventOf<T>(step);

        /// <summary>
        /// 그 소리 단서가 남 좌석(≠ <paramref name="me"/>)의 해금 · 바꾸기(48 · 49행) 덮개 소리인가. 그 덮개는 내 창 · 메뉴가 떠 있으면 숨으므로(<c>GameFlow.ShowsOthers</c>)
        /// 그때는 재생 층이 이 소리를 내지 않는다(결정 120 ① · plan §5 #709 — 좌석 칩 꼬리표는 무음).
        /// </summary>
        public static bool OthersCover(FxEntry entry, FxCue cue, int me)
        {
            if (entry == null || !cue.IsSound || (cue.Row != Unlock && cue.Row != Swap)) return false;
            foreach (var s in entry.Steps)
            {
                if (s.Row != cue.Row || cue.AtMs < s.StartMs || cue.AtMs > s.EndMs) continue;
                var u = First<CardUnlocked>(s);
                var w = First<TrainerCardSwapped>(s);
                int seat = u != null ? u.Seat : w != null ? w.Seat : -1;
                return seat >= 0 && seat != me;
            }
            return false;
        }
    }

    /// <summary>해금·카드 바꾸기 한 순간(<see cref="UnlockBeats.At"/> · <see cref="SwapBeats.At"/>).</summary>
    public struct CardFlipPose
    {
        /// <summary>덮개(어둠 · 카드)가 보이는 정도(0 ~ 1).</summary>
        public float Show;
        /// <summary>원인 포켓몬의 등급 아이콘이 날아온 만큼(0 = 출발 ~ 1 = 아래 띠에 박힘, 없으면 −1) · 박힐 때의 배율.</summary>
        public float Icon, IconScale;
        /// <summary>깨질 자물쇠가 흔들리는 가로 거리(1배 px — 0이면 가만히).</summary>
        public float Shake;
        /// <summary>깨진 자물쇠 수(0 ~ 박자 수).</summary>
        public int Broken;
        /// <summary>마지막으로 깨진 자물쇠 조각이 떨어진 만큼(0 ~ 1, 없으면 −1).</summary>
        public float Fall;
        /// <summary>카드의 가로 배율(1 = 앞면 그대로 · 0 = 옆으로 선 순간 — 뒤집기).</summary>
        public float Flip;
        /// <summary>보이는 면이 바뀐 수(0 = 처음 면 · 1 = 한 번 뒤집힘 …).</summary>
        public int Face;
        /// <summary>금속광 띠가 카드 위를 지나간 자리(0 = 왼쪽 끝 ~ 1 = 오른쪽 끝, 없으면 −1).</summary>
        public float Sheen;
        /// <summary>착지: 새로 열린 능력 칸의 금 테두리 · 별 · 꼬리표가 켜졌나와 꼬리표 불투명도(0 ~ 1).</summary>
        public bool Lit;
        public float Tags;
    }

    /// <summary>
    /// 트레이너 카드 해금(04 §8-8 48행, S40 — 결정 84 M5 가)의 박자: 예비 = 보드가 어두워지고 지금 등급의 카드가 뜬다 → 원인 포켓몬의 등급 아이콘이 날아와 아래 띠에 박힘(<see cref="IconLandAt"/>) →
    /// 움직임 = 자물쇠가 흔들림 → 맞음 = 자물쇠가 깨져 떨어지며 카드가 뒤집혀 다음 등급(<see cref="BreakAt"/> — 바로 골드면 두 박자, 800 간격) → 금속광이 카드를 쓸고(<see cref="SheenAt"/>) →
    /// 착지 = 열린 능력 칸에 금 테두리 · 별 · 「실버 ★★☆」「「능력」 해금」 꼬리표(<see cref="LitAt"/>). 회전은 그리지 않는다(UI 연출은 옮기기·늘이기·흐리기만 — 뒤집기 = 가로 배율) [가정 #162 ①].
    /// </summary>
    public static class UnlockBeats
    {
        public const int Row = CardRows.Unlock;
        public const int InMs = 200, IconFromMs = 200, IconMs = 500, IconLandAt = IconFromMs + IconMs, StampMs = 120, ShakeLeadMs = 700, FirstBreakAt = 1400,
            FlipMs = 400, FallMs = 500, SheenLeadMs = 400, SheenMs = 500, TagsMs = 150, OutMs = 250;
        public const float ShakePx = 3f;

        /// <summary>깨지는 자물쇠 수(열린 능력 칸 수 — 브론즈 → 골드면 둘).</summary>
        public static int Breaks(CardUnlocked e) => e == null || e.AbilitySlots == null ? 1 : Math.Max(1, Math.Min(2, e.AbilitySlots.Length));

        /// <summary>k번째 자물쇠가 깨지는 단계 안 시각.</summary>
        public static int BreakAt(int k) => FirstBreakAt + PlayTiming.UnlockStepMs * Math.Max(0, k);

        public static int SheenAt(int breaks) => BreakAt(breaks - 1) + SheenLeadMs;

        public static int LitAt(int breaks) => SheenAt(breaks) + SheenMs;

        public static CardFlipPose At(int ms, int total, int breaks)
        {
            var p = new CardFlipPose { Icon = -1f, IconScale = 1f, Fall = -1f, Flip = 1f, Sheen = -1f };
            if (ms < 0 || ms >= total) return p;
            breaks = Math.Max(1, breaks);
            p.Show = ms < InMs ? Tween.At(ms, 0, InMs, 0f, 1f) : Tween.At(ms, total - OutMs, OutMs, 1f, 0f);
            if (ms >= IconFromMs)
            {
                p.Icon = Tween.At(ms, IconFromMs, IconMs, 0f, 1f, Ease.OutQuad);
                if (ms >= IconLandAt) p.IconScale = Tween.At(ms, IconLandAt, StampMs, 1.6f, 1f, Ease.OutQuad);
            }
            for (int k = 0; k < breaks; k++)
            {
                int at = BreakAt(k);
                if (ms >= at)
                {
                    p.Broken = k + 1;
                    p.Fall = Tween.At(ms, at, FallMs, 0f, 1f, Ease.InQuad);
                    // 뒤집기: 앞 절반 동안 옆으로 서고(1 → 0) 그 순간 면이 바뀌어 뒤 절반에 펴진다(0 → 1)
                    int t = ms - at;
                    if (t < FlipMs / 2) p.Flip = Tween.At(t, 0, FlipMs / 2, 1f, 0f, Ease.InQuad);
                    else if (t < FlipMs) p.Flip = Tween.At(t, FlipMs / 2, FlipMs / 2, 0f, 1f, Ease.OutQuad);
                    p.Face = t < FlipMs / 2 ? k : k + 1;
                }
                else if (p.Broken == k)
                {
                    // 흔들림: 아이콘이 박힌 뒤(두 번째 자물쇠는 첫 뒤집기가 끝난 뒤)부터 깨지기 직전으로 갈수록 세진다
                    int from = k == 0 ? at - ShakeLeadMs : BreakAt(k - 1) + FlipMs;
                    if (ms >= from) p.Shake = (float)Math.Sin(ms * 0.09) * ShakePx * Tween.At(ms, from, at - from, 0.4f, 1f);
                }
            }
            int sheen = SheenAt(breaks);
            if (ms >= sheen && ms < sheen + SheenMs) p.Sheen = Tween.At(ms, sheen, SheenMs, 0f, 1f, Ease.InOutQuad);
            int lit = LitAt(breaks);
            p.Lit = ms >= lit;
            if (p.Lit) p.Tags = Tween.At(ms, lit, TagsMs, 0f, 1f);
            return p;
        }
    }

    /// <summary>
    /// 반바지 꼬마 빨강 카드 바꾸기(04 §8-8 49행 — 2000)의 박자: 지금 카드(반바지 꼬마 골드)가 뜬다 → 뒤집혀(<see cref="FlipAt"/>) 고른 새 카드 골드 → 금속광 → 「골드 ★★★」 꼬리표.
    /// 해금 연출을 줄인 꼴(자물쇠·아이콘 없음). 다섯 장 부채꼴과 고르기는 보드 카드 물음 단추가 이미 그린다(M6 화면 2c) [가정 #162 ①].
    /// </summary>
    public static class SwapBeats
    {
        public const int Row = CardRows.Swap;
        public const int InMs = 200, FlipAt = 700, FlipMs = 400, SheenAt = FlipAt + FlipMs, SheenMs = 400, LitAt = SheenAt + SheenMs, TagsMs = 150, OutMs = 250;

        public static CardFlipPose At(int ms, int total)
        {
            var p = new CardFlipPose { Icon = -1f, IconScale = 1f, Fall = -1f, Flip = 1f, Sheen = -1f };
            if (ms < 0 || ms >= total) return p;
            p.Show = ms < InMs ? Tween.At(ms, 0, InMs, 0f, 1f) : Tween.At(ms, total - OutMs, OutMs, 1f, 0f);
            int t = ms - FlipAt;
            if (t >= 0 && t < FlipMs / 2) p.Flip = Tween.At(t, 0, FlipMs / 2, 1f, 0f, Ease.InQuad);
            else if (t >= FlipMs / 2 && t < FlipMs) p.Flip = Tween.At(t, FlipMs / 2, FlipMs / 2, 0f, 1f, Ease.OutQuad);
            p.Face = t >= FlipMs / 2 ? 1 : 0;
            if (ms >= SheenAt && ms < SheenAt + SheenMs) p.Sheen = Tween.At(ms, SheenAt, SheenMs, 0f, 1f, Ease.InOutQuad);
            p.Lit = ms >= LitAt;
            if (p.Lit) p.Tags = Tween.At(ms, LitAt, TagsMs, 0f, 1f);
            return p;
        }
    }

    /// <summary>능력 사용 한 순간(<see cref="AbilityBeats.At"/>).</summary>
    public struct AbilityPose
    {
        /// <summary>작은 카드가 보이는 정도(0 ~ 1).</summary>
        public float Show;
        /// <summary>떠오르는 높이(1배 px — 들어올 때 아래에서, 통통 튈 때 위로 6).</summary>
        public float Rise;
        /// <summary>쓴 능력 칸 테두리의 빛(0 ~ 1).</summary>
        public float Glow;
    }

    /// <summary>
    /// 트레이너 카드 능력 사용(04 §8-8 50행 — 600)의 박자: 예비 = 출처 카드가 작게 떠오르며(<see cref="InMs"/>) 쓴 능력 칸(파랑·빨강) 테두리가 빛남 →
    /// 움직임 = 카드가 통통(6px — <see cref="BounceAt"/>) → 착지 = 걷힌다. 효과 연출(이동 · 회복 · 주사위 …)은 그 결과 행이 그대로 그린다(04 50행 「규칙」).
    /// 전용 아이템의 따로 그린 효과(프레셔스 볼 딸깍 · 대타출동 슬로모션 · 다른 색 주사위)는 그리지 않는다 [가정 #162 ①].
    /// </summary>
    public static class AbilityBeats
    {
        public const int Row = CardRows.Ability;
        public const int InMs = 120, BounceAt = 150, BounceMs = 200, OutMs = 150;
        public const float InPx = 12f, BouncePx = 6f;

        /// <summary>
        /// psychic B(「interact with something from up to 2 tiles away」)로 고른 떨어진 칸(결정 120 — 남과 나의 염력 칸을 보드에 칠한다): 그 단계의 AbilityUsed가 psychic 카드의 파랑이고 Targets[0]이 칸이면 그 칸, 아니면 −1.
        /// </summary>
        public static int ReachCell(FxStep step, Ctx ctx)
        {
            var a = step != null && step.Row == Row ? CardRows.First<AbilityUsed>(step) : null;
            if (a == null || ctx == null || a.Ability != AbilityColor.Blue || a.Targets == null || a.Targets.Length == 0) return -1;
            var cards = ctx.Cards.TrainerCards;
            if (a.CardKey < 0 || a.CardKey >= cards.Count || cards[a.CardKey].Key != "psychic") return -1;
            int cell = a.Targets[0];
            return cell >= 0 && cell < ctx.Board.Count ? cell : -1;
        }

        public static AbilityPose At(int ms, int total)
        {
            var p = new AbilityPose();
            if (ms < 0 || ms >= total) return p;
            p.Show = ms < InMs ? Tween.At(ms, 0, InMs, 0f, 1f) : Tween.At(ms, total - OutMs, OutMs, 1f, 0f);
            if (ms < InMs) p.Rise = -Tween.At(ms, 0, InMs, InPx, 0f, Ease.OutQuad);
            else if (ms >= BounceAt && ms < BounceAt + BounceMs) p.Rise = BouncePx * (float)Math.Sin(Math.PI * (ms - BounceAt) / BounceMs);
            p.Glow = ms < InMs ? Tween.At(ms, 0, InMs, 0f, 1f) : 0.6f + 0.4f * (float)Math.Cos(Math.PI * 2 * (ms - InMs) / 300.0);
            return p;
        }
    }

    /// <summary>날씨 휠 한 순간(<see cref="WeatherBeats.At"/>).</summary>
    public struct WeatherPose
    {
        /// <summary>배지에 보이는 날씨(<c>TrainerRules.WeatherTypes</c> 색인 0 ~ 3).</summary>
        public int Shown;
        /// <summary>멈출 때 톡의 세기(1 = 없음 — 화면은 1보다 큰 동안 배지를 밝힌다).</summary>
        public float Scale;
        /// <summary>아직 도는 중인가.</summary>
        public bool Spinning;
    }

    /// <summary>
    /// 날씨 휠 놓기·바꾸기(04 §8-8 35행 — 1800)의 박자: 위 띠 날씨 배지가 돌림판처럼 날씨 넷을 돌다가(<see cref="SpinMs"/> — 점점 느려진다) 고른 날씨에 멈추며 톡(<see cref="PopMs"/> — 배지가 밝아진다, 픽셀 글자라 늘이지 않는다).
    /// 판 위를 휩쓰는 날씨 파티클 · 사당으로 빠져나가는 JP 3 · 배틀 무대의 날씨는 그리지 않는다(JP는 표시 값으로 단계 끝에 내려앉는다) [가정 #162 ①].
    /// </summary>
    public static class WeatherBeats
    {
        public const int Row = CardRows.Weather;
        public const int SpinMs = 1300, PopMs = 200, Turns = 2, Kinds = 4;
        /// <summary>멈출 때 톡 커지는 정도(위 띠 이웃 배지를 덮지 않게 작게).</summary>
        public const float PopScale = 0.12f;

        /// <summary>돌림판이 지나가는 칸 수(처음 날씨에서 고른 날씨까지 — 적어도 두 바퀴).</summary>
        public static int Steps(WeatherSet e)
        {
            int to = e != null ? Math.Max(0, e.Weather) : 0, from = e != null && e.Before >= 0 ? e.Before : 0;
            return Turns * Kinds + ((to - from) % Kinds + Kinds) % Kinds;
        }

        public static WeatherPose At(int ms, WeatherSet e)
        {
            int to = e != null ? Math.Max(0, e.Weather) : 0, from = e != null && e.Before >= 0 ? e.Before : 0;
            var p = new WeatherPose { Shown = to, Scale = 1f };
            if (ms < 0) return p;
            if (ms < SpinMs)
            {
                int steps = Steps(e);
                int done = (int)Math.Floor(Tween.At(ms, 0, SpinMs, 0f, steps, Ease.OutQuad) + 0.5f);   // 반올림 — 멈추기 직전에 고른 날씨에 닿는다
                p.Shown = (from + Math.Min(done, steps)) % Kinds;
                p.Spinning = true;
                return p;
            }
            if (ms < SpinMs + PopMs) p.Scale = 1f + PopScale * (float)Math.Sin(Math.PI * (ms - SpinMs) / PopMs);
            return p;
        }
    }

    /// <summary>
    /// 배틀 안에서 생긴 해금(48행)을 결과 창 [확인] 뒤 보드로 미룬다(결정 84 M5 가 — 「배틀 중에 해금되면 배틀이 끝난 뒤 보드에서」): 배틀이 열린 동안(BattleStarted ~ BattleEnded)의 항목에서
    /// 해금 단계를 떼어 모아 두고, 배틀 끝이 든 항목 바로 뒤에 새 항목(같은 Seq · 해금한 좌석)으로 세운다 — 화면은 배틀 끝 항목이 다 재생되면 결과 창 [확인]까지 대기열을 세우므로
    /// 새 항목은 [확인] 뒤에 돈다. 단계 길이는 그대로 옮긴다(방장 멈춤과 합이 같다 — 배틀이 여러 갱신에 걸치면 그 사이 갱신 길이가 바뀐다).
    /// 화면이 좌석마다 하나 들고 갱신마다 부른다(배틀은 갱신 여러 개에 걸친다).
    /// </summary>
    public sealed class UnlockHold
    {
        readonly List<FxStep> held = new List<FxStep>();

        /// <summary>지금 배틀이 열려 있나(갱신 사이에 이어진다).</summary>
        public bool InBattle { get; set; }

        /// <summary>모아 둔 해금 단계 수.</summary>
        public int Count => held.Count;

        public void Clear()
        {
            held.Clear();
            InBattle = false;
        }

        /// <summary>이어 받은 판(지나간 이벤트가 없다 — 보기에서 세운다): 배틀 도중에 저장한 판이면 배틀이 열린 채로 시작한다(그 배틀 안의 해금도 배틀 끝 뒤로).</summary>
        public void Resume(bool inBattle)
        {
            held.Clear();
            InBattle = inBattle;
        }

        /// <summary>한 갱신의 항목(덜어 내기 · 결과 창 뒤 떼기를 마친 뒤)에서 미루고 세운다. 세운 항목 수를 돌려준다.</summary>
        public int Defer(List<FxEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            int made = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                bool starts = false, ends = false;
                foreach (var e in entry.Events)
                {
                    if (e is BattleStarted) starts = true;
                    if (e is BattleEnded) ends = true;
                }
                if (InBattle || starts || ends)
                {
                    var cut = entry.Steps.FindAll(s => s.Row == UnlockBeats.Row);
                    if (cut.Count > 0)
                    {
                        FxBuilt.KeepSteps(entry, s => s.Row != UnlockBeats.Row);
                        foreach (var s in cut)
                            foreach (var e in s.Events) entry.Events.Remove(e);
                        held.AddRange(cut);
                    }
                }
                if (starts) InBattle = true;
                if (!ends) continue;
                InBattle = false;
                if (held.Count == 0) continue;
                entries.Insert(i + 1, Flush(entry));
                i++;
                made++;
            }
            return made;
        }

        FxEntry Flush(FxEntry after)
        {
            int seat = held[0].Events.Count > 0 ? held[0].Events[0].Seat : after.Seat;
            var entry = new FxEntry { Seq = after.Seq, Seat = seat, Kind = after.Kind, Auto = after.Auto, Continues = after.Continues, ReactsTo = after.ReactsTo };
            int t = 0;
            foreach (var s in held)
            {
                s.StartMs = t;
                t += s.Ms;
                entry.Steps.Add(s);
                entry.Events.AddRange(s.Events);
            }
            entry.QueueMs = t;
            held.Clear();
            return entry;
        }
    }
}
