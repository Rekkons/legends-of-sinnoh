using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>
    /// M5 「맵 결과·배틀 안 연출」의 행(04 §8-8 — 행 × 10 + 단계): 숨겨진 장소 결과 26 규토리 더미 · 27 야영지 · 28 떨어진 별 · 29 우두머리 · 30 유물(<c>SpotResolved</c>) ·
    /// 46 트레이너 배틀 승리 보상(<c>SpoilsTaken</c> — 결과 창의 줄) · 74 은하의 응징(저장 741 · 옮기기 742) · 75 교체 금지 750 · 78 신 둘째 폼 780 · 79 배틀 중 폼 바꾸기 790 ·
    /// 80 신 격파 800(보드 — 결과 창 [확인] 뒤). 길이는 방장과 같은 값(<c>PlayTiming.RowMs</c>).
    /// 따로 행을 두지 않고 다른 행의 박자 안에서 그리는 것: 23 전설 장소(조우 전환 22행의 앞 — <see cref="LegendSiteBeats"/>) · 24·81 무리 진행 점(인트로 53 · 기절 65 — <see cref="SwarmBeats"/>) ·
    /// 76 알파 스트라이크(기술 59)·비스트 부스트(피로 641) · 77 우두머리 등장(인트로 53) — <see cref="AuraBeats"/>.
    /// 15행 왜곡 게이트 이동은 규칙이 아직 없다(룰북 p.21 — 울트라 버스트 캠페인의 깨어진 세계, M6).
    /// </summary>
    public static class ResultRows
    {
        public const int Stash = 260, Camp = 270, Star = 280, Alpha = 290, Relic = 300;
        public const int Spoils = 461, Stored = 741, Moved = 742, Lock = 750, GodPhase = 780, Form = 790, GodGone = 800;

        public static int Of(SpotResult r)
        {
            switch (r)
            {
                case SpotResult.Stash: return Stash;
                case SpotResult.Camp: return Camp;
                case SpotResult.Star: return Star;
                case SpotResult.Relic: return Relic;
                default: return Alpha;
            }
        }

        /// <summary>숨겨진 장소 결과 행인가(26~30).</summary>
        public static bool Spot(int row) => row == Stash || row == Camp || row == Star || row == Alpha || row == Relic;

        /// <summary>보드가 그리는 행(HUD 위 <c>ResultFx</c>).</summary>
        public static bool Board(int row) => Spot(row) || row == GodGone;

        /// <summary>배틀 화면이 그리는 행.</summary>
        public static bool Battle(int row) => row == Spoils || row == Stored || row == Moved || row == Lock || row == GodPhase || row == Form;

        public static bool Built(int row) => Board(row) || Battle(row);

        /// <summary>그 단계의 첫 T 이벤트(없으면 null).</summary>
        public static T EventOf<T>(FxStep step) where T : GameEvent
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is T t) return t;
            return null;
        }

        /// <summary>그 항목의 숨겨진 장소 결과 단계(없으면 null).</summary>
        public static FxStep SpotStep(FxEntry entry)
        {
            if (entry == null) return null;
            foreach (var s in entry.Steps)
                if (Spot(s.Row) && EventOf<SpotResolved>(s) != null) return s;
            return null;
        }

        /// <summary>그 항목에서 그 행의 단계가 지금 재생 중이면 그 단계(시각 = 항목 시각 − StartMs), 아니면 null.</summary>
        public static FxStep Playing(FxSequencer seq, int row)
        {
            var entry = seq != null ? seq.Current : null;
            var step = entry != null ? entry.Step(row) : null;
            return step != null && seq.LocalMs >= step.StartMs && seq.LocalMs < step.EndMs ? step : null;
        }
    }

    /// <summary>숨겨진 장소 결과의 한 순간(<see cref="SpotResultBeats.At"/>).</summary>
    public struct SpotResultPose
    {
        /// <summary>토큰 크기(1 = 그대로 — 부풀기 · 접혔다 펴지기).</summary>
        public float Swell;
        /// <summary>결과 꼬리표(이름 칩)가 보이는 만큼 · 떠오른 만큼(0 ~ 1).</summary>
        public float Label, Rise;
        /// <summary>날아가는 그림(규토리 · 별 · 유물)의 길(0 = 토큰 ~ 1 = 닿는 곳) · 보이는 만큼 · 포물선 높이(0 ~ 1).</summary>
        public float Fly, FlyShow, Arc;
        /// <summary>닿은 곳이 튀는 크기(1 = 그대로).</summary>
        public float Pop;
        /// <summary>토큰 둘레의 빛(우두머리 = 붉은 섬광 · 유물 = 빛기둥, 0 ~ 1) · 땅울림(px 배수, −1 ~ 1).</summary>
        public float Glow, Shake;
        /// <summary>굴러 오르는 숫자(우두머리 체력 +4 — 0 ~ 4) · 떨어진 별 4·8개째의 띠 빛(0 ~ 1).</summary>
        public int Count;
        public float Band;
    }

    /// <summary>
    /// 숨겨진 장소 결과(04 §8-8 26~30행)의 시간표 — 공개(25행)로 뒤집힌 앞면 토큰이 말 머리 위에 떠 있는 채로 시작한다.
    /// 26 규토리 더미: 부풀었다 터지며 규토리가 튀고 말 쪽으로 모인다 → 「○○ 규토리 +n」. 27 야영지: 접혔다 펴지며 내려앉고 먼지 → 「야영지」.
    /// 28 떨어진 별: 별이 떠올라 위 띠로 날아가 박힌다 → 「떨어진 별 n개」(4·8개째는 띠가 한 번 빛난다) — 운명 2장은 이어지는 받기(42행)가 그린다.
    /// 29 우두머리: 붉은 섬광과 땅울림 한 번 → 「우두머리 포켓몬!」 · 체력 +4가 굴러 오른다(스튜로 피했으면 「배틀 없음」만) → 조우 전환. 30 유물: 빛기둥 → 유물이 호를 그려 내 판으로 → 이름.
    /// </summary>
    public static class SpotResultBeats
    {
        /// <summary>토큰이 맵에서 사라지는 때(단계 안 ms): 규토리 더미는 터질 때 · 별·유물은 떠오를 때. 우두머리는 단계 끝까지(그 뒤 전환) · 야영지는 남는다.</summary>
        public const int PopMs = 250, LiftMs = 300, LabelInMs = 150, LabelOutMs = 200, FlyMs = 700, GatherMs = 450, EachMs = 110;

        /// <summary>우두머리: 섬광이 가장 밝은 때 · 체력이 굴러 오르는 구간.</summary>
        public const int FlashAtMs = 500, HpFromMs = 1100, HpMs = 600, HpBonus = 4;

        /// <summary>떨어진 별 4·8개째의 띠 빛 길이(행 길이에 더해진 몫 — <c>PlayTiming.RowMs</c> 280).</summary>
        public const int BandMs = 800;

        public static int GoneAt(SpotResult r, int total)
        {
            switch (r)
            {
                case SpotResult.Stash: return PopMs;
                case SpotResult.Star:
                case SpotResult.Relic: return LiftMs;
                case SpotResult.Alpha: return total;
                default: return int.MaxValue;   // 야영지 · 피한 우두머리는 앞면으로 남는다
            }
        }

        /// <summary>날아가는 그림 수: 규토리 더미 = 개수(여섯까지) · 별·유물 = 1 · 그 밖 0.</summary>
        public static int Flyers(SpotResolved e)
        {
            if (e == null) return 0;
            if (e.Result == SpotResult.Stash) return Math.Max(0, Math.Min(6, e.Count));
            return e.Result == SpotResult.Star || e.Result == SpotResult.Relic ? 1 : 0;
        }

        /// <summary>그림이 닿는 때(단계 안 ms — 표시 값과 「+n」이 이때 선다): 별·유물 = 떠오름 + 날기, 규토리 = 마지막 것이 모인 때.</summary>
        public static int LandAt(SpotResolved e) => e != null && e.Result == SpotResult.Stash ? PopMs + EachMs * Math.Max(0, Flyers(e) - 1) + 300 + GatherMs : LiftMs + FlyMs;

        /// <param name="ms">단계 안 시각.</param>
        /// <param name="index">날아가는 그림의 차례(0부터).</param>
        public static SpotResultPose At(SpotResolved e, int ms, int total, int index = 0)
        {
            var p = new SpotResultPose { Swell = 1f, Pop = 1f };
            if (e == null || total <= 0 || ms < 0 || ms >= total) return p;
            int band = e.Result == SpotResult.Star && e.Count > 0 && e.Count % 4 == 0 ? BandMs : 0;
            int body = total - band;
            p.Label = Math.Min(Tween.Progress(ms, LabelAt(e.Result), LabelInMs), 1f - Tween.Progress(ms, total - LabelOutMs, LabelOutMs));
            p.Rise = Tween.Apply(Ease.OutQuad, Tween.Progress(ms, LabelAt(e.Result), 400));
            switch (e.Result)
            {
                case SpotResult.Stash:
                {
                    p.Swell = ms < PopMs ? Tween.At(ms, 0, PopMs, 1f, 1.35f, Ease.OutQuad) : 0f;
                    int from = PopMs + EachMs * index;
                    p.FlyShow = ms >= from ? 1f - Tween.Progress(ms, from + 300 + GatherMs - 100, 100) : 0f;
                    p.Arc = (float)Math.Sin(Tween.Progress(ms, from, 300) * Math.PI);   // 튀어 올랐다 내려앉고
                    p.Fly = Tween.At(ms, from + 300, GatherMs, 0f, 1f, Ease.InQuad);    // 말 쪽으로 모인다
                    p.Pop = Tween.At(ms, LandAt(e), 150, 1.3f, 1f, Ease.OutQuad);
                    break;
                }
                case SpotResult.Camp:
                    p.Swell = ms < 150 ? Tween.At(ms, 0, 150, 1f, 0.7f, Ease.OutQuad) : ms < 400 ? Tween.At(ms, 150, 250, 0.7f, 1.2f, Ease.OutQuad) : Tween.At(ms, 400, 150, 1.2f, 1f, Ease.InQuad);
                    break;
                case SpotResult.Star:
                case SpotResult.Relic:
                    p.Swell = ms < LiftMs ? 1f : 0f;
                    p.Glow = e.Result == SpotResult.Relic ? Math.Min(Tween.Progress(ms, 0, 200), 1f - Tween.Progress(ms, LiftMs + FlyMs - 200, 200)) : 0f;
                    p.FlyShow = ms >= LiftMs / 2 && ms < body ? 1f - Tween.Progress(ms, LiftMs + FlyMs + 150, 150) : 0f;
                    p.Fly = Tween.At(ms, LiftMs, FlyMs, 0f, 1f, Ease.InOutQuad);
                    p.Arc = (float)Math.Sin(p.Fly * Math.PI);
                    p.Pop = Tween.At(ms, LiftMs + FlyMs, 200, 1.4f, 1f, Ease.OutQuad);
                    p.Band = band > 0 && ms >= body ? (float)Math.Sin(Tween.Progress(ms, body, band) * Math.PI) : 0f;
                    break;
                case SpotResult.Alpha:
                    p.Glow = ms < FlashAtMs ? Tween.At(ms, 150, FlashAtMs - 150, 0f, 1f, Ease.OutQuad) : Tween.At(ms, FlashAtMs, 600, 1f, 0.35f, Ease.OutQuad);
                    p.Shake = ms >= FlashAtMs && ms < FlashAtMs + 360 ? (float)(Math.Cos((ms - FlashAtMs) / 60.0 * Math.PI) * (1.0 - (ms - FlashAtMs) / 360.0)) : 0f;
                    p.Count = (int)Math.Round(Tween.At(ms, HpFromMs, HpMs, 0f, HpBonus, Ease.OutQuad));
                    break;
                case SpotResult.AlphaWarded:
                    break;
            }
            return p;
        }

        /// <summary>꼬리표가 뜨는 때: 결과가 읽히는 순간(터진 뒤 · 내려앉은 뒤 · 닿은 뒤 · 섬광 뒤).</summary>
        public static int LabelAt(SpotResult r)
        {
            switch (r)
            {
                case SpotResult.Stash: return PopMs;
                case SpotResult.Camp: return 400;
                case SpotResult.Alpha: return FlashAtMs + 200;
                case SpotResult.AlphaWarded: return 100;
                default: return LiftMs + FlyMs;
            }
        }
    }

    /// <summary>
    /// 신 격파(04 §8-8 80행): 시작의 사당 위에서 빛이 흩어지고 「한 번 더!」가 튀어나와 머문다(신 토큰이 보드에 서는 것은 「사당·균열 화면」 — 여기는 빛과 배너만).
    /// 엔진은 토큰 빼기(<c>TokenRemoved</c> God)를 배틀 끝과 한 행동으로, 배틀 끝보다 먼저 낸다 — <see cref="SplitOff"/>가 그 단계를 결과 창 [확인] 뒤의 새 항목으로 옮긴다.
    /// </summary>
    public static class GodBeats
    {
        public const int Row = ResultRows.GodGone;
        public const int BurstMs = 500, BannerAtMs = 400, BannerPopMs = 200, BannerOutMs = 250;

        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Removed(step) != null ? step : null;
        }

        public static TokenRemoved Removed(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is TokenRemoved t && t.TokenKind == TokenKind.God) return t;
            return null;
        }

        /// <summary>빛(0 ~ 1) · 배너가 보이는 만큼 · 배너 크기.</summary>
        public static void At(int ms, int total, out float burst, out float banner, out float scale)
        {
            burst = banner = 0f;
            scale = 1f;
            if (total <= 0 || ms < 0 || ms >= total) return;
            burst = 1f - Tween.Progress(ms, 0, BurstMs);
            banner = Math.Min(Tween.Progress(ms, BannerAtMs, 100), 1f - Tween.Progress(ms, total - BannerOutMs, BannerOutMs));
            scale = Tween.At(ms, BannerAtMs, BannerPopMs, 1.3f, 1f, Ease.OutQuad);
        }

        /// <summary>
        /// 대기열에 남은(아직 끝나지 않은) 신 격파 단계와 그 단계 안 시각(M9 다듬기 P28 — 신 피규어가 빛으로 흩어진다 <see cref="ScatterBeats"/>). 아직 시작 전이면 local = −1(피규어는 선 채), 없으면 null.
        /// </summary>
        public static FxStep Pending(FxSequencer seq, out int local)
        {
            local = -1;
            if (seq == null) return null;
            foreach (var entry in seq.Queued)
            {
                var step = StepOf(entry);
                bool cur = ReferenceEquals(entry, seq.Current);
                if (step == null || (cur && seq.LocalMs >= step.EndMs)) continue;
                local = cur && seq.LocalMs >= step.StartMs ? seq.LocalMs - step.StartMs : -1;
                return step;
            }
            return null;
        }

        /// <summary>
        /// 배틀 끝 항목에 실린 신 격파 단계를 떼어 바로 뒤의 새 항목으로 세운다(같은 Seq — 결과 창 [확인] 뒤 보드에서 보인다). 단계는 배틀 끝 단계보다 앞에 있으므로
        /// 그 단계만 빼고 뒤 단계·단서를 그만큼 당긴다. 두 항목 길이의 합은 그대로다(P5). 뗀 수를 돌려준다.
        /// </summary>
        public static int SplitOff(List<FxEntry> entries) => SplitOff(entries, StepOf);

        /// <summary>같은 떼기를 다른 행에도(1막 해방의 조무래기 토큰 941 — <see cref="LiberateBeats"/>): stepOf = 그 항목의 뗄 단계.</summary>
        public static int SplitOff(List<FxEntry> entries, Func<FxEntry, FxStep> stepOf)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (stepOf == null) throw new ArgumentNullException(nameof(stepOf));
            int made = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var step = stepOf(entry);
                if (step == null || entry.Steps.Count < 2) continue;
                bool ends = false;
                foreach (var e in entry.Events) if (e is BattleEnded) ends = true;
                if (!ends) continue;
                int at = step.StartMs, ms = step.Ms;
                entry.Steps.Remove(step);
                foreach (var s in entry.Steps) if (s.StartMs > at) s.StartMs -= ms;
                for (int k = 0; k < entry.Cues.Count; k++)
                {
                    var cue = entry.Cues[k];
                    if (cue.AtMs >= at + ms) cue.AtMs -= ms;
                    else if (cue.AtMs > at) cue.AtMs = at;
                    entry.Cues[k] = cue;
                }
                var tail = new FxEntry { Seq = entry.Seq, Seat = entry.Seat, Kind = entry.Kind, Auto = entry.Auto, Continues = entry.Continues, ReactsTo = entry.ReactsTo };
                step.StartMs = 0;
                tail.Steps.Add(step);
                tail.Events.AddRange(step.Events);
                foreach (var e in step.Events) entry.Events.Remove(e);
                tail.QueueMs = ms;
                entry.QueueMs = Math.Max(0, entry.QueueMs - ms);
                entry.Reveal = null;
                entries.Insert(i + 1, tail);
                made++;
                i++;
            }
            return made;
        }
    }

    /// <summary>
    /// 빛으로 흩어지는 피규어(M9 다듬기 — 04 §8-8 80행 신 격파 P28 · 33행 달빛공터의 바꿔치기 P37): 피규어가 잠깐 선 채 빛나다(<see cref="FlashMs"/>) 떠오르며 줄어들어 사라지고(<see cref="Ms"/>),
    /// 빛 알갱이 <see cref="Motes"/>개가 그 자리에서 퍼지며 떠오른다(<see cref="MoteMs"/>까지 — 피규어보다 조금 더 남는다). 이미 그리는 행 안의 한 순간이다(새 행 · 소리 줄 없음).
    /// </summary>
    public static class ScatterBeats
    {
        public const int Ms = 700, FlashMs = 150, MoteMs = 950, Motes = 12;

        /// <summary>피규어: 크기(1 → 0) · 떠오른 만큼(0 ~ 1). 아직 보이면 true(시작 전 ms &lt; 0 = 선 채).</summary>
        public static bool At(int ms, out float size, out float lift)
        {
            size = 1f;
            lift = 0f;
            if (ms < 0) return true;
            if (ms >= Ms) { size = 0f; lift = 1f; return false; }
            size = 1f - Tween.Apply(Ease.InQuad, Tween.Progress(ms, FlashMs, Ms - FlashMs));
            lift = Tween.Apply(Ease.OutQuad, Tween.Progress(ms, FlashMs, Ms - FlashMs));
            return true;
        }

        /// <summary>i번째 빛 알갱이: 가운데에서 벗어난 자리(x −1 ~ 1 · y 위쪽이 +) · 반환 = 보이는 만큼(0 ~ 1).</summary>
        public static float Mote(int ms, int i, out float x, out float y)
        {
            x = y = 0f;
            int from = i % 4 * 40;
            if (ms < from || ms >= MoteMs) return 0f;
            float t = Tween.Progress(ms, from, MoteMs - from);
            double angle = i * 2.39996;   // 황금각 — 고르게 퍼진다
            float reach = (0.45f + 0.55f * (i * 7 % 5) / 4f) * Tween.Apply(Ease.OutQuad, t);
            x = (float)Math.Cos(angle) * reach;
            y = 0.3f + (float)Math.Sin(angle) * reach * 0.4f + t * (0.5f + 0.4f * (i % 3) / 2f);
            return Math.Min(Tween.Progress(ms, from, 80), 1f - Tween.Progress(ms, MoteMs - 300, 300));
        }
    }

    /// <summary>
    /// 전설 장소 찾아보기(04 §8-8 23행): 조우 전환(22행 — 전설 2500)의 앞 <see cref="Ms"/> 동안 보드에서 — 내 판의 유물이 칸으로 날아가 공명하고(예비) 땅이 한 번 울린다(움직임) → 전환 줄무늬가 덮는다.
    /// 엔진은 찾아보기 연출 없이 바로 배틀을 연다(plan §5 #86 ⑦) — 따로 행을 두지 않고 22행 안에서 그린다. 땅이 울릴 때 그 칸의 피규어가 솟았다 내려앉으며 Special Attack 클립을 한다
    /// (<see cref="Figure"/> — M9 다듬기 P25, 클립은 줄무늬가 덮을 때까지 이어진다). 울음은 아직 없다 [가정].
    /// </summary>
    public static class LegendSiteBeats
    {
        public const int Ms = 1000, FlyMs = 500, QuakeAtMs = 550, QuakeMs = 360;

        /// <summary>그 항목이 전설 배틀을 연 조우 전환 단계(전설 · 신). 아니면 null.</summary>
        public static FxStep StepOf(FxEntry entry, out BattleStarted started)
        {
            started = null;
            var step = entry != null ? entry.Step(EnterRows.Wipe) : null;
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is BattleStarted b && (b.Kind == BattleKind.Legendary || b.Kind == BattleKind.God)) started = b;
            return started != null ? step : null;
        }

        /// <summary>유물이 날아간 만큼(0 = 내 판 ~ 1 = 칸) · 공명 빛(0 ~ 1) · 땅울림(−1 ~ 1).</summary>
        public static void At(int ms, out float fly, out float glow, out float quake)
        {
            fly = glow = quake = 0f;
            if (ms < 0 || ms >= Ms) return;
            fly = Tween.At(ms, 0, FlyMs, 0f, 1f, Ease.InOutQuad);
            glow = Math.Min(Tween.Progress(ms, FlyMs - 100, 150), 1f - Tween.Progress(ms, Ms - 150, 150));
            quake = ms >= QuakeAtMs && ms < QuakeAtMs + QuakeMs ? (float)(Math.Cos((ms - QuakeAtMs) / 60.0 * Math.PI) * (1.0 - (ms - QuakeAtMs) / (double)QuakeMs)) : 0f;
        }

        /// <summary>피규어가 솟는 때(땅울림과 함께) · 솟았다 내려앉는 길이.</summary>
        public const int RiseAtMs = QuakeAtMs, RiseMs = Ms - QuakeAtMs;

        /// <summary>
        /// 그 칸의 피규어(P25): 솟은 만큼(0 ~ 1 — 솟았다 <see cref="Ms"/>에 내려앉는다) · 위로 늘어난 배율 · Special Attack 클립의 시각. ms = 전환 단계 안 시각 — 솟기 전이면 false(가만히 선 채).
        /// </summary>
        public static bool Figure(int ms, out float hop, out float stretch, out int clipMs)
        {
            hop = 0f;
            stretch = 1f;
            clipMs = 0;
            if (ms < RiseAtMs) return false;
            clipMs = ms - RiseAtMs;
            hop = (float)Math.Sin(Tween.Progress(ms, RiseAtMs, RiseMs) * Math.PI);
            stretch = 1f + 0.15f * hop;
            return true;
        }
    }

    /// <summary>DS 층 도트 둘레의 오라 한 순간: 색 갈래 · 두께(px, 0 = 없음) · 물들이는 만큼(0 ~ 1).</summary>
    public struct AuraPose
    {
        public AuraKind Kind;
        public int Px;
        public float Tint;
        /// <summary>알파 스트라이크의 타입(색) — 다른 갈래는 −1.</summary>
        public int Type;
        public bool On => Px > 0 || Tint > 0f;
    }

    public enum AuraKind { None, AlphaStrike, BeastBoost, Alpha, Form, God, Fan }

    /// <summary>
    /// 도트 둘레 오라의 박자(04 §8-8 76·77·78·79행 — DS 층은 도트의 실루엣을 색으로 둘레에 찍는다, <c>DsMon.Aura</c>):
    /// 76 알파 스트라이크 = 기술 단계(59행)의 앞 <see cref="StrikeMs"/> 동안 고른 타입 색 오라가 크게 일어난다 · 비스트 부스트 = 피로 단계(641 — 1200) 동안 붉은 오라가 터졌다 가라앉는다.
    /// 77 우두머리 = 인트로(53행)에서 야생이 선 뒤 <see cref="AlphaMs"/> 동안 붉은 기운, 그 뒤로도 배틀 내내 남는다(우두머리임을 알린다 — 결정 80 L15 나: 2px + 옅은 색조).
    /// 79 폼 바꾸기 = 흰 빛이 감싸고 두 모습이 번갈아 → 새 폼. 78 신 둘째 폼 = 멈칫 → 빛 → 둘째 폼(체력 가득).
    /// </summary>
    public static class AuraBeats
    {
        public const int StrikeMs = 600, AlphaMs = 1000, BoostMs = 1200;

        /// <summary>오라의 맥박(두께 1 ~ 3px): 150ms마다.</summary>
        public static int Pulse(int ms, int max) => max <= 1 ? max : 1 + Math.Abs(ms / 150 % (2 * (max - 1)) - (max - 1));

        /// <summary>알파 스트라이크(59행 안): 단계 안 시각 → 오라.</summary>
        public static AuraPose Strike(int ms, int type)
        {
            if (ms < 0 || ms >= StrikeMs) return default(AuraPose);
            return new AuraPose { Kind = AuraKind.AlphaStrike, Type = type, Px = Pulse(ms, 3), Tint = 0.35f * (1f - Tween.Progress(ms, StrikeMs - 200, 200)) };
        }

        /// <summary>비스트 부스트(641행 — 1200): 붉은 오라가 터졌다 가라앉는다.</summary>
        public static AuraPose Boost(int ms, int total)
        {
            if (total <= 0 || ms < 0 || ms >= total) return default(AuraPose);
            return new AuraPose { Kind = AuraKind.BeastBoost, Type = -1, Px = ms < total / 2 ? 3 : ms < total * 3 / 4 ? 2 : 1, Tint = 0.4f * (1f - Tween.Progress(ms, 0, total)) };
        }

        /// <summary>우두머리(77행): 선 뒤 since ms — 처음 <see cref="AlphaMs"/>는 굵게 맥박, 그 뒤 <see cref="StayPx"/>px로 남는다.</summary>
        public static AuraPose Alpha(int since)
        {
            if (since < 0) return default(AuraPose);
            return new AuraPose { Kind = AuraKind.Alpha, Type = -1, Px = since < AlphaMs ? Pulse(since, 3) : StayPx, Tint = since < AlphaMs ? StayTint + (0.3f - StayTint) * (1f - Tween.Progress(since, 0, AlphaMs)) : StayTint };
        }

        /// <summary>남는 오라(우두머리 · 팬 폼 · 천상의 오라가 걸린 신)의 두께와 도트에 입히는 옅은 색조(결정 80 L15 나 — 720 벌에서도 보이게 2px + 색조).</summary>
        public const int StayPx = 2;
        public const float StayTint = 0.16f;

        /// <summary>천상의 오라(78행 — 신 배틀에서 오라가 걸려 있는 동안): 옅은 금빛 오라가 남는다.</summary>
        public static AuraPose God() => new AuraPose { Kind = AuraKind.God, Type = -1, Px = StayPx, Tint = StayTint };
    }

    /// <summary>폼이 바뀌는 한 순간(<see cref="FormBeats.At"/>).</summary>
    public struct FormPose
    {
        /// <summary>새 폼의 그림인가(아니면 옛 폼) · 하얗게 덮인 만큼 · 가라앉은 만큼(0 ~ 1) · 빛기둥(0 ~ 1) · 체력 막대가 찬 만큼(0 ~ 1, 둘째 폼).</summary>
        public bool After;
        public float White, Sink, Pillar, Fill;
    }

    /// <summary>
    /// 폼 바꾸기(04 §8-8 79행 — 790, 1000ms): 움찔(가라앉음) → 흰 빛이 감싸고 두 모습이 두세 번 번갈아 → 빛이 걷히며 새 폼.
    /// 신 둘째 폼(78행 — 780, 3200ms): 첫 폼이 쓰러지듯 아래로 미끄러지다(<see cref="SlumpSink"/>까지 — M9 다듬기 P29, 앞의 기절 단계는 이 포켓몬을 가라앉히지 않는다 <see cref="SlumpAhead"/>) 멈칫 → 빛기둥이 서고 흰 모습이 솟아오른다 → 둘째 폼으로 바뀌며 빛이 걷힌다 → 체력이 가득 찬다. 천상의 유물로 오라가 깨지는 것(포획 7 → 6)은 인트로 꼬리표가 맡는다.
    /// </summary>
    public static class FormBeats
    {
        public const int Row = ResultRows.Form, GodRow = ResultRows.GodPhase;
        public const int FlickerEveryMs = 110;

        public static FormChanged Changed(FxStep step) => ResultRows.EventOf<FormChanged>(step);

        /// <summary>신의 첫 폼이 미끄러져 멈추는 깊이(0 = 선 채 ~ 1 = 다 가라앉음) · 미끄러짐 · 멈칫이 끝나는 때(단계 길이의 백분율).</summary>
        public const float SlumpSink = 0.55f;
        public const int SlidePercent = 12, HoldPercent = 28;

        /// <summary>그 항목에 그 포켓몬의 신 둘째 폼 단계가 아직 시작 전으로 남았나 — 그 앞의 기절 단계는 가라앉히지 않고 세워 둔다(미끄러짐은 780이 그린다).</summary>
        public static bool SlumpAhead(FxEntry entry, int localMs, int pokemon)
        {
            if (entry == null) return false;
            foreach (var s in entry.Steps)
                if (s.Row == GodRow && localMs < s.StartMs && Changed(s) is FormChanged fc && fc.PokemonId == pokemon) return true;
            return false;
        }

        public static FormPose At(int ms, int total, bool god)
        {
            var p = new FormPose();
            if (total <= 0 || ms < 0) return p;
            if (ms >= total) { p.After = true; p.Fill = 1f; return p; }
            if (!god)
            {
                int white = total * 20 / 100, swap = total * 75 / 100;
                p.Sink = ms < white ? 0.08f * (float)Math.Sin(Tween.Progress(ms, 0, white) * Math.PI) : 0f;
                p.White = ms < white ? Tween.Progress(ms, white / 2, white / 2) : ms < swap ? 1f : 1f - Tween.Progress(ms, swap, total - swap);
                p.After = ms >= swap || (ms >= white && (ms - white) / FlickerEveryMs % 2 == 1);
                p.Fill = 1f;
                return p;
            }
            // 첫 폼이 쓰러지듯 미끄러지다 멈칫 → 빛기둥이 서고 흰 빛이 감싼다 → 흰 모습이 솟아오르고 → 둘째 폼으로 바뀌며 빛이 걷힌다 → 체력이 찬다
            int slide = total * SlidePercent / 100, hold = total * HoldPercent / 100, rise = total * 40 / 100, turn = total * 65 / 100, fill = total * 85 / 100;
            p.Sink = ms < slide ? SlumpSink * Tween.Apply(Ease.InQuad, Tween.Progress(ms, 0, slide)) : ms < rise ? SlumpSink : Tween.At(ms, rise, turn - rise, SlumpSink, 0f, Ease.OutQuad);
            p.Pillar = ms < hold ? 0f : Math.Min(Tween.Progress(ms, hold, 300), 1f - Tween.Progress(ms, fill, total - fill));
            p.White = ms < hold ? 0f : ms < turn ? Tween.Progress(ms, hold, 300) : 1f - Tween.Progress(ms, turn, fill - turn);
            p.After = ms >= turn;
            p.Fill = Tween.At(ms, turn, fill - turn, 0f, 1f, Ease.OutQuad);
            return p;
        }
    }

    /// <summary>
    /// 은하의 응징(04 §8-8 74행): 저장(741 — 600ms) = 어두운 구슬이 상대 기둥의 「응징 n」 배지로 빨려 들며 배지가 톡 커진다 · 옮기기(742 — 1200ms) = 배지가 빛나고 구슬이 공격한 포켓몬 쪽으로 날아가 터진다.
    /// 교체 금지(75행 — 750, 400ms): 자물쇠 칩이 톡 붙는다.
    /// </summary>
    public static class RetributionBeats
    {
        /// <summary>구슬이 가는 길(0 ~ 1) · 보이는 만큼 · 배지 크기.</summary>
        public static void Stored(int ms, int total, out float fly, out float show, out float pop)
        {
            fly = show = 0f;
            pop = 1f;
            if (total <= 0 || ms < 0 || ms >= total) return;
            int land = total * 60 / 100;
            fly = Tween.At(ms, 0, land, 0f, 1f, Ease.InQuad);
            show = ms < land ? 1f : 0f;
            pop = ms < land ? 1f : Tween.At(ms, land, total - land, 1.4f, 1f, Ease.OutQuad);
        }

        public static void Moved(int ms, int total, out float fly, out float show, out float burst)
        {
            fly = show = burst = 0f;
            if (total <= 0 || ms < 0 || ms >= total) return;
            int gather = total * 25 / 100, land = total * 70 / 100;
            fly = Tween.At(ms, gather, land - gather, 0f, 1f, Ease.InQuad);
            show = ms < land ? Tween.Progress(ms, 0, gather) : 0f;
            burst = ms >= land ? 1f - Tween.Progress(ms, land, total - land) : 0f;
        }

        /// <summary>자물쇠 칩의 크기(톡 — 1.5 → 1).</summary>
        public static float Lock(int ms, int total) => total <= 0 || ms < 0 ? 1f : Tween.At(ms, 0, Math.Min(total, 250), 1.5f, 1f, Ease.OutQuad);

        /// <summary>잠금 이유의 화면 글 키(<c>screen.battle.lock.*</c> — 다섯).</summary>
        public static string ReasonKey(SwitchLockReason reason) => "screen.battle.lock." + BattleFeed.LockName(reason);
    }

    /// <summary>
    /// 무리 배틀(04 §8-8 24·81행, S55): 진행 점 ●○○ — 명단(약한 · 적당한 · 강한) 가운데 끝난(쓰러뜨렸거나 잡은) 수만큼 채워진다. 점은 그 포켓몬이 쓰러진 단계(65행)·잡힌 단계(68행)가 끝날 때 톡 커지며 채워지고,
    /// 시작(24행)은 인트로(53행) 안에서 명단 세 칸이 약한 것부터 하나씩 선다(<see cref="LineAt"/>).
    /// </summary>
    public static class SwarmBeats
    {
        public const int LineEveryMs = 400, LineFromMs = 300, DotPopMs = 300;

        /// <summary>인트로 단계 안 시각에 서 있는 명단 칸 수(0 ~ count).</summary>
        public static int LineAt(int ms, int count) => ms < LineFromMs ? 0 : Math.Min(count, 1 + (ms - LineFromMs) / LineEveryMs);

        /// <summary>k번째 칸이 설 때의 크기(톡).</summary>
        public static float LinePop(int ms, int k) => Tween.At(ms, LineFromMs + LineEveryMs * k, 200, 1.3f, 1f, Ease.OutQuad);

        /// <summary>점이 채워진 뒤 since ms의 크기.</summary>
        public static float DotPop(int since) => since < 0 ? 1f : Tween.At(since, 0, DotPopMs, 1.6f, 1f, Ease.OutQuad);
    }

    /// <summary>
    /// 트레이너 배틀 승리 보상(04 §8-8 46행 — 461, 1500ms): 결과 창에 「○○을(를) 가져왔다!」·「운명의 변화 2장을 받았다」 줄이 내려앉는다(유물은 그림과 함께) —
    /// 운명 카드가 손으로 날아오는 것은 배틀 화면의 운명 층이 그린다(받기와 같은 그림). 보호 배지(ProtectionSet)는 좌석 칩의 꼬리표가 맡는다.
    /// </summary>
    public static class SpoilsBeats
    {
        public const int Row = ResultRows.Spoils, DropAtMs = 200;

        public static SpoilsTaken Taken(FxStep step) => ResultRows.EventOf<SpoilsTaken>(step);

        /// <summary>가져온 유물(아이템 색인 — 없으면 −1) · 받은 운명 장수.</summary>
        public static int Relic(SpoilsTaken e, out int fate)
        {
            fate = 0;
            int relic = -1;
            if (e == null || e.Changes == null) return -1;
            foreach (var c in e.Changes)
            {
                if (c.Seat != e.Seat) continue;
                if (c.Res == ResKind.Relic) relic = c.Key;
                else if (c.Res == ResKind.Fate) fate = Math.Max(0, c.After - c.Before);
            }
            return relic;
        }
    }
}
