using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>막 카드 한 순간의 모습(<see cref="ActBeats.At"/>). 값은 모두 0 ~ 1 비율이거나 배수다 — 화면이 px·색으로 옮긴다.</summary>
    public struct ActPose
    {
        /// <summary>보드를 덮는 어둠(0 = 없음 ~ 1 = 다 어두움, sRGB에서 고른 값 — 화면이 Linear로 보정한다).</summary>
        public float Dim;
        /// <summary>카드가 보이는 만큼(0 ~ 1).</summary>
        public float Card;
        /// <summary>카드의 세로 자리(−1 = 화면 위 밖 ~ 0 = 제자리).</summary>
        public float Drop;
        /// <summary>카드의 가로 늘이기(0 = 옆모습 ~ 1 = 앞면) — 떨어지며 가로로 뒤집힌다.</summary>
        public float Flip;
        /// <summary>박힌 글자 수(박히는 중인 글자 포함).</summary>
        public int Letters;
        /// <summary>박히는 중인 글자의 배수(2 → 1). 다 박혔으면 1.</summary>
        public float Stamp;
        /// <summary>마지막 글자가 박힌 뒤의 감쇠 흔들림(−1 ~ 1 — 화면이 px로 옮긴다).</summary>
        public float Shake;
        /// <summary>목표 문장 · 보상 줄이 보이는 만큼(0 ~ 1).</summary>
        public float Goal, Reward;
        /// <summary>착지했다 — 위 띠의 막 배지가 새 막으로 바뀌는 때.</summary>
        public bool Landed;
    }

    /// <summary>
    /// 막 카드 공개(04 §8-8 4행, S14)의 시간표. 길이는 방장과 같은 값(<c>PlayTiming.RowMs</c> 40행 — 3600)이고 여기는 그 안의 박자다(§8-1 P11 예비 → 임팩트 → 여운):
    /// 보드가 어두워짐 → 카드가 위에서 떨어지며 가로로 뒤집힘 → 제목 글자가 하나씩 박힘(70ms 간격, 2배 → 1배) → 마지막 글자에 감쇠 흔들림 한 번 →
    /// 목표 문장 · 보상 줄 → 머묾 → 걷히며 착지(위 띠 막 배지가 바뀐다). 구간은 길이의 비율이고 글자 간격·흔들림만 ms다(§8-5 규칙 9).
    /// </summary>
    public static class ActBeats
    {
        public const int Row = 40;

        /// <summary>길이를 나누는 수: 어두워짐 = 길이 / 12 · 떨어짐 = 길이 / 8 · 걷힘 = 길이 / 9.</summary>
        public const int DimPart = 12, DropPart = 8, OutPart = 9;
        public const int LetterEveryMs = 70, StampMs = 140, ShakeMs = 260, GoalMs = 160, RewardGapMs = 220;

        /// <summary>글자가 박히기 시작하는 때(어두워짐 + 떨어짐 뒤 한 박자).</summary>
        public static int LettersAt(int total) => total / DimPart + total / DropPart + 60;

        /// <summary>마지막 글자가 다 박히는 때.</summary>
        public static int StampedAt(int total, int letters) => LettersAt(total) + LetterEveryMs * Math.Max(0, letters - 1) + StampMs;

        /// <summary>걷히기 시작하는 때 = 착지(위 띠 막 배지가 바뀐다).</summary>
        public static int LandAt(int total) => total - total / OutPart;

        /// <summary>그 항목의 막 카드 단계(없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(Row) : null;

        /// <summary>
        /// 위 띠의 막 배지에 보일 막: 보기는 공개와 함께 바로 다음 막이지만 화면은 그 막 카드가 착지할 때 바뀐다 — 아직 착지하지 않은 막 카드(재생 중 · 기다리는 중)마다 하나씩 앞 막이다.
        /// 서막 카드가 착지하기 전이면 −1(배지 없음).
        /// </summary>
        public static int ShownAct(int viewAct, FxSequencer seq)
        {
            if (seq == null) return viewAct;
            int act = viewAct;
            foreach (var entry in seq.Queued)
            {
                var step = StepOf(entry);
                if (step == null) continue;
                bool landed = ReferenceEquals(entry, seq.Current) && seq.LocalMs >= step.StartMs + LandAt(step.Ms);
                if (!landed) act--;
            }
            return Math.Max(-1, act);
        }

        /// <param name="ms">단계 안 시각.</param>
        /// <param name="total">단계 길이.</param>
        /// <param name="letters">제목 글자 수.</param>
        public static ActPose At(int ms, int total, int letters)
        {
            var p = new ActPose { Stamp = 1f };
            if (total <= 0 || ms < 0 || ms >= total) return p;
            int dim = total / DimPart, drop = total / DropPart, land = LandAt(total), outMs = total - land;
            float gone = Tween.Progress(ms, land, outMs);
            p.Landed = ms >= land;
            p.Dim = Tween.Progress(ms, 0, dim) * (1f - gone);
            p.Card = ms >= dim ? 1f - gone : 0f;
            p.Drop = Tween.At(ms, dim, drop, -1f, 0f, Ease.OutQuad);
            p.Flip = Tween.At(ms, dim, drop, 0f, 1f, Ease.OutQuad);
            int at = LettersAt(total);
            if (ms >= at && letters > 0)
            {
                int k = Math.Min(letters - 1, (ms - at) / LetterEveryMs);
                p.Letters = k + 1;
                p.Stamp = Tween.At(ms, at + k * LetterEveryMs, StampMs, 2f, 1f, Ease.OutQuad);
            }
            int stamped = StampedAt(total, letters);
            if (ms >= stamped && ms < stamped + ShakeMs)
            {
                float t = (ms - stamped) / (float)ShakeMs;
                p.Shake = (float)(Math.Sin(t * Math.PI * 4.0) * (1.0 - t));
            }
            p.Goal = Tween.Progress(ms, stamped + ShakeMs / 2, GoalMs);
            p.Reward = Tween.Progress(ms, stamped + ShakeMs / 2 + RewardGapMs, GoalMs);
            return p;
        }
    }

    /// <summary>
    /// 서막의 조무래기 토큰 놓기(04 §8-8 5행)의 시간표: 고향 마을마다 순서대로(150ms 어긋남) 토큰이 위에서 떨어져 내려앉는다.
    /// 게임 시작의 숨겨진 장소 배치(같은 행)는 화면이 그리지 않는다 — 토큰은 처음부터 놓여 있다 [가정].
    /// </summary>
    public static class TokenBeats
    {
        public const int Row = 50;
        public const int LeadMs = 300, EveryMs = 150, FallMs = 320, SquashMs = 140;

        /// <summary>그 항목의 조무래기 놓기 단계(없으면 null — 숨겨진 장소 배치 단계는 치지 않는다).</summary>
        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Grunts(step) != null ? step : null;
        }

        /// <summary>그 단계가 실은 조무래기 놓기 이벤트(없으면 null).</summary>
        public static TokensPlaced Grunts(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is TokensPlaced tp && tp.Kind == TokenKind.Grunt) return tp;
            return null;
        }

        /// <summary>
        /// <paramref name="index"/>번째 마을의 토큰이 떨어진 만큼: −1 = 아직 안 나타남, 0 = 위(떨어지기 시작) ~ 1 = 내려앉음.
        /// </summary>
        public static float Fall(int ms, int index)
        {
            int at = LeadMs + EveryMs * Math.Max(0, index);
            if (ms < at) return -1f;
            return Tween.At(ms, at, FallMs, 0f, 1f, Ease.InQuad);
        }

        /// <summary>내려앉은 뒤의 눌림(1 = 제 모양, 내려앉는 순간 가장 납작 → 돌아옴).</summary>
        public static float Squash(int ms, int index)
        {
            int at = LeadMs + EveryMs * Math.Max(0, index) + FallMs;
            if (ms < at || ms >= at + SquashMs) return 1f;
            return Tween.At(ms, at, SquashMs, 0.7f, 1f, Ease.OutQuad);
        }
    }

    /// <summary>
    /// 1막 목표 달성(04 §8-8 94행 1막 — 941, M9 다듬기 P18): 고향 마을의 조무래기 토큰이 움찔했다가 위로 날아가 사라지고, 그 자리에 「이동력 +2」 칩이 튀어나와 머물다 그 좌석의 자리
    /// (내 것 = 아래 판의 남은 이동력 배지 · 남 = 좌석 칩)로 날아가 붙는다. 길이는 방장과 같은 값(<c>PlayTiming.RowMs</c> 941 = 1500 — §8-5 「공개 없는 queue 항목」)이고 여기는 그 안의 박자다.
    /// 엔진은 토큰 빼기(<c>TokenRemoved</c> Grunt)를 배틀 끝과 한 행동으로 낸다 — <c>GodBeats.SplitOff(entries, StepOf)</c>가 그 단계를 결과 창 [확인] 뒤의 새 항목으로 옮긴다.
    /// </summary>
    public static class LiberateBeats
    {
        public const int Row = 941;
        /// <summary>
        /// 기다림(결과 창을 걷는 줄무늬 <see cref="EnterBeats.WipeBackMs"/> — 보드가 드러난 뒤에 움직인다) → 움찔(눌림) → 날아오름(<see cref="LiftAtMs"/>) → 사라짐(<see cref="GoneAt"/>) ·
        /// 칩이 튀어나옴(<see cref="ChipAtMs"/>) → 머묾 → 자리로 날아감 → 흐려짐.
        /// </summary>
        public const int LeadMs = EnterBeats.WipeBackMs, FlinchMs = 200, LiftAtMs = LeadMs + FlinchMs, LiftMs = 450, GoneAt = LiftAtMs + LiftMs, SquashBackMs = 120,
            ChipAtMs = 850, ChipPopMs = 200, ChipFlyAtMs = 1150, ChipFlyMs = 250, ChipOutMs = 100;

        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Removed(step) != null ? step : null;
        }

        /// <summary>그 단계가 실은 조무래기 토큰 빼기(없으면 null).</summary>
        public static TokenRemoved Removed(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is TokenRemoved t && t.TokenKind == TokenKind.Grunt) return t;
            return null;
        }

        /// <summary>토큰이 떠오른 만큼: 0 = 칸 위 ~ 1 = 다 날아감(그 뒤 −1 = 사라짐).</summary>
        public static float Lift(int ms)
        {
            if (ms >= GoneAt) return -1f;
            return Tween.At(ms, LiftAtMs, LiftMs, 0f, 1f, Ease.InQuad);
        }

        /// <summary>움찔하는 동안의 눌림(1 = 제 모양, 가장 납작 0.7 → 날아오르며 돌아옴).</summary>
        public static float Squash(int ms)
        {
            if (ms < LeadMs || ms >= LiftAtMs + SquashBackMs) return 1f;
            return ms < LiftAtMs ? Tween.At(ms, LeadMs, FlinchMs, 1f, 0.7f, Ease.OutQuad) : Tween.At(ms, LiftAtMs, SquashBackMs, 0.7f, 1f, Ease.OutQuad);
        }

        /// <summary>칩: 보이는 만큼(0 ~ 1) · 크기(튀어나올 때 1.3 → 1) · 자리로 간 만큼(0 = 마을 칸 위 ~ 1 = 그 좌석의 자리).</summary>
        public static void Chip(int ms, int total, out float show, out float scale, out float fly)
        {
            show = fly = 0f;
            scale = 1f;
            if (total <= 0 || ms < ChipAtMs || ms >= total) return;
            show = Math.Min(Tween.Progress(ms, ChipAtMs, 100), 1f - Tween.Progress(ms, total - ChipOutMs, ChipOutMs));
            scale = Tween.At(ms, ChipAtMs, ChipPopMs, 1.3f, 1f, Ease.OutQuad);
            fly = Tween.At(ms, ChipFlyAtMs, ChipFlyMs, 0f, 1f, Ease.InOutQuad);
        }
    }

    /// <summary>공중날기의 한 순간(<see cref="FlyBeats.At"/>): 땅 위 직선의 어디쯤인가(0 = 떠난 마을 ~ 1 = 도착 마을)와 떠 있는 높이(0 ~ 1 — 화면이 월드 유닛으로 옮긴다).</summary>
    public struct FlyPose
    {
        public float T, Height;
    }

    /// <summary>
    /// 공중날기(04 §8-8 16행, S25)의 시간표. 길이는 방장과 같은 값(<c>PlayTiming.RowMs</c> 160행)이고 여기는 그 안의 박자다:
    /// 비전기술 컷인(결정 167 — 앞 <see cref="CutinMs"/>, 박자는 <see cref="FlyCutinBeats"/> · 그동안 말은 떠난 마을에 그대로) →
    /// 택시(<see cref="TaxiMs"/>): 제자리에서 떠오름 → 도착 마을까지 호를 그리며 날아감 → 내려앉음(찌르호크는 화면이 말 머리 위에 붙인다 — 결정 126).
    /// </summary>
    public static class FlyBeats
    {
        public const int Row = 160;

        /// <summary>택시 길이를 나누는 수: 떠오름 = 택시 길이 / 8 · 내려앉음 = 택시 길이 / 8.</summary>
        public const int LiftPart = 8, LandPart = 8;

        /// <summary>앞의 컷인 길이(결정 167 — 방장 <c>PlayTiming.FlyCutinMs</c>).</summary>
        public const int CutinMs = PlayTiming.FlyCutinMs;

        /// <summary>그 단계 길이에서 컷인 몫: 컷인보다 길면 <see cref="CutinMs"/>, 아니면 0(컷인 없이 택시만).</summary>
        public static int CutinOf(int total) => total > CutinMs ? CutinMs : 0;

        /// <summary>그 단계 길이에서 택시 몫(= 길이 − 컷인).</summary>
        public static int TaxiMs(int total) => Math.Max(0, total - CutinOf(total));

        /// <summary>떠오른 높이(호의 가장 높은 곳 = 1에 대한 비율).</summary>
        public const float Hover = 0.35f;

        /// <summary>그 항목의 공중날기 단계(없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Flight(step) != null ? step : null;
        }

        /// <summary>그 단계가 실은 공중날기 이벤트(없으면 null).</summary>
        public static Flew Flight(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is Flew f) return f;
            return null;
        }

        /// <summary>그 시각의 말 모습: 컷인 동안은 떠난 마을 땅 위(<c>T = 0 · Height = 0</c>), 그 뒤는 택시 몫 안의 시각으로 <see cref="Taxi"/>.</summary>
        /// <param name="ms">단계 안 시각.</param>
        /// <param name="total">단계 길이.</param>
        public static FlyPose At(int ms, int total)
        {
            int cut = CutinOf(total);
            return Taxi(ms - cut, total - cut);
        }

        /// <summary>택시 몫 안의 한 순간(<paramref name="ms"/> = 택시가 시작한 뒤, <paramref name="total"/> = 택시 길이).</summary>
        public static FlyPose Taxi(int ms, int total)
        {
            var p = new FlyPose();
            if (total <= 0 || ms >= total) { p.T = 1f; return p; }
            if (ms <= 0) return p;
            int lift = total / LiftPart, land = total / LandPart, glide = total - lift - land;
            if (ms < lift)
            {
                p.Height = Tween.At(ms, 0, lift, 0f, Hover, Ease.OutQuad);
                return p;
            }
            if (ms >= lift + glide)
            {
                p.T = 1f;
                p.Height = Tween.At(ms, lift + glide, land, Hover, 0f, Ease.InQuad);
                return p;
            }
            p.T = Tween.At(ms, lift, glide, 0f, 1f, Ease.InOutQuad);
            p.Height = Hover + (1f - Hover) * (float)Math.Sin(p.T * Math.PI);
            return p;
        }
    }

    /// <summary>공중날기 컷인의 한 순간(<see cref="FlyCutinBeats.At"/>). 띠 크기는 비율, 탄 것의 자리는 화면 가로 비율, 날갯짓은 텍셀.</summary>
    public struct FlyCutinPose
    {
        /// <summary>컷인이 보이는 때인가(단계의 앞 <see cref="FlyBeats.CutinMs"/> 안).</summary>
        public bool Shown;
        /// <summary>띠의 폭(0 ~ 1 — 가운데에서 좌우로 펼쳐진 만큼) · 높이(0 ~ 1 — <see cref="FlyCutinBeats.BandTexels"/>에 대한 비율).</summary>
        public float Wide, Tall;
        /// <summary>탄 것(찌르호크 + 주인공)이 보이나 · 가로 자리(1 = 오른쪽 화면 밖 · 0 = 가운데 · −1 = 왼쪽 화면 밖).</summary>
        public bool Riders;
        public float X;
        /// <summary>날갯짓으로 오르내린 만큼(텍셀, 위 = +).</summary>
        public int Bob;
    }

    /// <summary>
    /// 공중날기 앞의 비전기술 컷인(사용자 결정 167 — Pt 본편 hm_cut_in: 가로 띠가 가운데 줄에서 좌우로 → 위아래로 펼쳐지고, 포켓몬이 오른쪽에서 미끄러져 들어와
    /// 가운데에서 울고 왼쪽으로 빠져나간 뒤 띠가 접힌다). 여기서는 찌르호크에 그 좌석 주인공이 탄 채로 지나간다 [가정 — 길이 1400 · 띠 = 원작 자산 field_cutin 그림].
    /// 시각은 16행 단계 안 시각 그대로(컷인이 단계 맨 앞이다). 울음 = <see cref="CryMs"/>, 휙 소리 = <see cref="SwooshMs"/>(소리 표 16행 「cry」·「cutin」).
    /// </summary>
    public static class FlyCutinBeats
    {
        /// <summary>띠 높이(원작 자산 화면의 96 ± 40줄) · 띠가 좌우로 펼쳐지는 동안의 두께(텍셀).</summary>
        public const int BandTexels = 80, LineTexels = 2;

        /// <summary>박자(단계 안 ms): 좌우로 펼침 끝 · 위아래로 펼침 끝 · 가운데 닿음(울음) · 떠나기 시작(휙) · 왼쪽으로 다 빠짐 · 접힘 끝(= <see cref="FlyBeats.CutinMs"/>).</summary>
        public const int WideMs = 100, OpenMs = 300, InMs = 700, HoldMs = 950, OutMs = 1200, CloseMs = FlyBeats.CutinMs;

        /// <summary>가운데에 머무는 동안 왼쪽으로 흘러가는 만큼(원작은 −2px/프레임으로 흐르며 운다).</summary>
        public const float DriftX = 0.06f;

        /// <summary>날갯짓 한 번(ms) · 오르내림 폭(텍셀).</summary>
        public const int BobMs = 300, BobTexels = 2;

        /// <summary>찌르호크의 전국도감 번호(울음 · 그림 — 원작 TTS 장막시티 택시).</summary>
        public const int TaxiDex = 398;

        public static int CryMs => InMs;
        public static int SwooshMs => HoldMs;

        /// <param name="ms">16행 단계 안 시각.</param>
        /// <param name="total">16행 단계 길이(컷인 몫이 없으면 늘 안 보인다).</param>
        public static FlyCutinPose At(int ms, int total)
        {
            var p = new FlyCutinPose();
            if (FlyBeats.CutinOf(total) <= 0 || ms < 0 || ms >= CloseMs) return p;
            p.Shown = true;
            float line = LineTexels / (float)BandTexels;
            if (ms < WideMs) { p.Wide = Tween.At(ms, 0, WideMs, 0f, 1f, Ease.OutQuad); p.Tall = line; }
            else if (ms < OpenMs) { p.Wide = 1f; p.Tall = Tween.At(ms, WideMs, OpenMs - WideMs, line, 1f, Ease.InQuad); }
            else if (ms < OutMs) { p.Wide = 1f; p.Tall = 1f; }
            else { p.Wide = 1f; p.Tall = Tween.At(ms, OutMs, CloseMs - OutMs, 1f, 0f, Ease.InQuad); }
            if (ms >= OpenMs && ms < OutMs)
            {
                p.Riders = true;
                if (ms < InMs) p.X = Tween.At(ms, OpenMs, InMs - OpenMs, 1f, 0f, Ease.OutCubic);
                else if (ms < HoldMs) p.X = Tween.At(ms, InMs, HoldMs - InMs, 0f, -DriftX, Ease.Linear);
                else p.X = Tween.At(ms, HoldMs, OutMs - HoldMs, -DriftX, -1f, Ease.InQuad);
                p.Bob = (int)Math.Round(BobTexels * Math.Sin(2 * Math.PI * (ms - OpenMs) / BobMs));
            }
            return p;
        }
    }

    /// <summary>숨겨진 장소 토큰의 한 순간(<see cref="SpotBeats.At"/>): 떠 있는 높이(1 = 멈춰 서서 묻는 동안의 높이)와 뒤집힌 만큼(0 = 뒷면 ~ 1 = 앞면, 0.5에서 그림이 바뀐다).</summary>
    public struct SpotPose
    {
        public float Lift, Flip;
    }

    /// <summary>
    /// 숨겨진 장소의 공개 · 지나가기(04 §8-8 25행, S34)의 시간표. 멈춰 서서 묻는 동안 토큰은 떠 있다(높이 1 — 정체와 무관하게 늘 같은 모양, B2).
    /// [공개]: 조금 더 떠오르며 가로로 뒤집히고(<see cref="FlipPermille"/>까지 — 가운데에서 앞면 그림으로 바뀐다) 앞면으로 머물다(<see cref="HoldPermille"/>까지) 내려앉는다. [지나가기]: 그대로 내려앉는다.
    /// 길이는 방장과 같은 값(250행 700)이고 정체와 무관하다.
    /// </summary>
    public static class SpotBeats
    {
        public const int Row = 250;

        /// <summary>뒤집기가 끝나는 때 · 앞면으로 머묾이 끝나는 때(길이의 천분율) · 지나가기에서 내려앉는 데 드는 몫.</summary>
        public const int FlipPermille = 550, HoldPermille = 850, PassPermille = 300;

        /// <summary>뒤집히는 동안 더 떠오르는 높이(묻는 동안의 높이 1에 더한다).</summary>
        public const float Peak = 0.2f;

        /// <summary>그 항목의 숨겨진 장소 단계(없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(Row) : null;

        /// <summary>그 단계가 실은 숨겨진 장소의 칸(공개 · 지나가기 — 없으면 −1)과 공개인가.</summary>
        public static int Cell(FxStep step, out bool reveal)
        {
            reveal = false;
            if (step == null) return -1;
            foreach (var e in step.Events)
            {
                if (e is HiddenSpotRevealed r) { reveal = true; return r.Cell; }
                if (e is HiddenSpotPassed p) return p.Cell;
            }
            return -1;
        }

        /// <param name="ms">단계 안 시각(0 이하 = 아직 묻는 모습).</param>
        /// <param name="total">단계 길이.</param>
        public static SpotPose At(int ms, int total, bool reveal)
        {
            var p = new SpotPose { Lift = 1f };
            if (total <= 0 || ms <= 0) return p;
            if (!reveal)
            {
                p.Lift = Tween.At(ms, 0, total * PassPermille / 1000, 1f, 0f, Ease.OutQuad);
                return p;
            }
            int flip = total * FlipPermille / 1000, hold = total * HoldPermille / 1000;
            p.Flip = Tween.At(ms, 0, flip, 0f, 1f, Ease.InOutQuad);
            p.Lift = ms < flip ? 1f + Peak * (float)Math.Sin(p.Flip * Math.PI) : Tween.At(ms, hold, total - hold, 1f, 0f, Ease.InQuad);
            return p;
        }
    }

    /// <summary>시간대(결정 70 ⑥ #2 ① 「낮 고정을 풀고 시간대를 넣는다」) — Pt 원작 자산에 그림이 있는 셋, 흐르는 순서대로.</summary>
    public enum DayTime { Day, Evening, Night }

    /// <summary>
    /// 시간대 셈(결정 70 ⑥): 1라운드 = 낮 → 저녁 → 밤 → 다시 낮 [가정 — plan §5 #65: 종류·순서는 가진 그림에 맞췄다(Pt의 아침은 낮 그림과 같다)].
    /// 규칙 상태가 아니라 보기 값이다 — 라운드 번호에서만 셈하므로 세이브·방장·손님 화면이 늘 같다. 배틀 무대의 배경·발판(<c>Gen/Stage/stage.json</c>의 <c>timed</c>)과 보드 HUD 위 띠의 시간대 배지가 읽는다.
    /// </summary>
    public static class DayTimes
    {
        public const int Count = 3;

        /// <summary>그 라운드의 시간대(라운드는 1부터 — 셋업·서막처럼 라운드가 없는 때는 낮).</summary>
        public static DayTime Of(int round) => round <= 1 ? DayTime.Day : (DayTime)((round - 1) % Count);

        /// <summary>시간대 그림의 이름 꼬리(낮 = 없음 — 지형 짝 표의 이름 그대로) · Gen 표의 이름(<c>stage.json</c>의 <c>times</c>).</summary>
        public static string Suffix(DayTime time) => time == DayTime.Evening ? "_evening" : time == DayTime.Night ? "_night" : "";
        public static string Name(DayTime time) => time == DayTime.Evening ? "evening" : time == DayTime.Night ? "night" : "day";

        /// <summary>화면 글 키(<c>screen.board.time.&lt;day|evening|night&gt;</c>).</summary>
        public static string TextKey(DayTime time) => "screen.board.time." + Name(time);

        /// <summary>낮 그림의 키(…<c>.png</c>) → 그 시간대 그림의 키. 시간대 그림이 따로 없는 것(<paramref name="timed"/> = false — 실내·동굴·깨어진 세계)과 낮은 그대로.</summary>
        public static string Art(string dayKey, DayTime time, bool timed)
        {
            const string ext = ".png";
            if (!timed || time == DayTime.Day || dayKey == null || !dayKey.EndsWith(ext, StringComparison.Ordinal)) return dayKey;
            return dayKey.Substring(0, dayKey.Length - ext.Length) + Suffix(time) + ext;
        }
    }

    /// <summary>
    /// 회복(04 §8-8 8행 귀환 회복 · 17행 [전체 회복])의 시간표. 길이는 방장과 같은 값(<c>PlayTiming.RowMs</c> 80행 — 회복 징글에 묶인 2800)이고 여기는 그 안의 박자다:
    /// 보드 HUD 아래 판의 파티 칸이 징글 박자로 하나씩 반짝이고(<see cref="FlashSpanPermille"/>까지 고르게) → 징글 끝음(단계 끝)에 체력이 한꺼번에 찬다(표시 값 단서 — 단계 끝 착지).
    /// Pt 회복대(볼 줄)·상태 아이콘이 떨어지는 것은 아직 없다 — HUD의 파티 칸이 볼 줄을 대신한다 [가정 — plan §5 #58].
    /// </summary>
    public static class HealBeats
    {
        public const int Row = 80;

        /// <summary>마지막 칸이 반짝이기 시작하는 때까지(길이의 천분율) · 칸 하나의 반짝임 길이(ms).</summary>
        public const int FlashSpanPermille = 600, FlashMs = 320;

        /// <summary>그 항목의 회복 단계(없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Healing(step) != null ? step : null;
        }

        /// <summary>그 단계가 실은 회복(포켓몬이 한 마리라도 있는 것 — 없으면 null).</summary>
        public static Healed Healing(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is Healed h && h.Pokemon != null && h.Pokemon.Length > 0) return h;
            return null;
        }

        /// <summary>회복하는 n마리 가운데 i번째(0부터 — 파티 순서)가 반짝이기 시작하는 단계 안 시각.</summary>
        public static int FlashAt(int total, int i, int n) => n <= 1 ? 0 : (int)((long)Math.Max(0, total) * FlashSpanPermille / 1000 * i / (n - 1));

        /// <summary>그 칸의 반짝임(0 = 없음 ~ 1 = 가장 밝음): 반짝이기 시작할 때 1에서 <see cref="FlashMs"/> 동안 0으로.</summary>
        public static float Flash(int ms, int total, int i, int n)
        {
            int at = FlashAt(total, i, n);
            if (ms < at || ms >= at + FlashMs) return 0f;
            return Tween.At(ms, at, FlashMs, 1f, 0f, Ease.OutQuad);
        }
    }

    /// <summary>귀환 한 순간의 모습(<see cref="WhiteoutBeats.At"/>): 보드를 덮는 어둠(0 ~ 1, sRGB에서 고른 값 — 화면이 Linear로 보정한다) · 말이 고향에 다시 섰나.</summary>
    public struct WhiteoutPose
    {
        public float Dark;
        public bool Home;
    }

    /// <summary>
    /// 전멸 뒤 고향으로 돌아감(04 §8-8 71행 — 암전)의 시간표. 길이는 방장과 같은 값(<c>PlayTiming.RowMs</c> 710행 — 2000)이고 여기는 그 안의 박자다:
    /// 보드가 어두워짐(<see cref="DarkPermille"/>까지) → 다 어두운 동안 말이 고향 마을에 다시 섬(<see cref="HomePermille"/>) → 어둠이 걷힘(<see cref="ClearPermille"/>부터 끝까지).
    /// 빛기둥 · 「귀환 대기」 배지는 아직 없다 — 다음 내 턴의 귀환 회복(8행)이 이어진다 [가정 — plan §5 #58].
    /// </summary>
    public static class WhiteoutBeats
    {
        public const int Row = 710;

        public const int DarkPermille = 350, HomePermille = 500, ClearPermille = 650;

        /// <summary>그 항목의 귀환 단계(없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Event(step) != null ? step : null;
        }

        /// <summary>그 단계가 실은 귀환 이벤트(없으면 null).</summary>
        public static Whiteout Event(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is Whiteout w) return w;
            return null;
        }

        /// <param name="ms">단계 안 시각.</param>
        /// <param name="total">단계 길이.</param>
        public static WhiteoutPose At(int ms, int total)
        {
            var p = new WhiteoutPose();
            if (total <= 0 || ms >= total) { p.Home = true; return p; }
            if (ms <= 0) return p;
            int dark = total * DarkPermille / 1000, home = total * HomePermille / 1000, clear = total * ClearPermille / 1000;
            p.Home = ms >= home;
            p.Dark = ms < dark ? Tween.At(ms, 0, dark, 0f, 1f, Ease.InQuad) : ms < clear ? 1f : Tween.At(ms, clear, total - clear, 1f, 0f, Ease.OutQuad);
            return p;
        }

        /// <summary>
        /// 배틀 끝 항목(<c>BattleEnded</c>가 든 항목)에 실린 귀환 단계를 떼어 바로 뒤의 새 항목으로 세운다(같은 Seq · 좌석 · 종류): 엔진은 배틀 끝과 귀환을 한 행동(Seq)으로 내는데,
        /// 화면은 그 항목이 다 재생되면 결과 창을 띄우고 [확인]까지 대기열을 세운다 — 떼지 않으면 암전이 배틀 화면 뒤에서 지나가 버린다.
        /// 뗀 단계 · 그 뒤 단계 · 귀환부터의 이벤트 · 그 시각 뒤의 단서가 새 항목으로 옮겨 가고, 두 항목 길이의 합은 그대로다(방장 멈춤과 같다 — P5). 뗀 항목 수를 돌려준다.
        /// </summary>
        public static int Split(List<FxEntry> entries) => SplitOff(entries, StepOf, Event);

        /// <summary>같은 떼기를 다른 행에도(게임 끝 97행 — 배신자 연전의 마지막 배틀이 끝난 항목에 실린다, M5 「막·종막 화면」): stepOf = 그 항목의 뗄 단계, eventOf = 그 단계의 첫 이벤트.</summary>
        public static int SplitOff(List<FxEntry> entries, Func<FxEntry, FxStep> stepOf, Func<FxStep, GameEvent> eventOf)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            int made = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var step = stepOf(entry);
                if (step == null || !Ends(entry)) continue;
                int from = entry.Steps.IndexOf(step), at = step.StartMs;
                if (from <= 0) continue;   // 귀환이 첫 단계면 이미 따로 선다
                var tail = new FxEntry { Seq = entry.Seq, Seat = entry.Seat, Kind = entry.Kind, Auto = entry.Auto, Continues = entry.Continues, ReactsTo = entry.ReactsTo };
                int tailMs = 0, tailBlock = 0;
                for (int k = from; k < entry.Steps.Count; k++)
                {
                    var s = entry.Steps[k];
                    s.StartMs -= at;
                    if (s.Layer == FxLayer.Block) tailBlock += s.Ms;   // 차단 컷신(게임 끝 97행)은 차단 길이로 옮긴다
                    else tailMs += s.Ms;
                    tail.Steps.Add(s);
                }
                entry.Steps.RemoveRange(from, entry.Steps.Count - from);
                int ev = entry.Events.IndexOf(eventOf(step));
                if (ev >= 0)
                {
                    tail.Events.AddRange(entry.Events.GetRange(ev, entry.Events.Count - ev));
                    entry.Events.RemoveRange(ev, entry.Events.Count - ev);
                }
                for (int k = entry.Cues.Count - 1; k >= 0; k--)
                {
                    var cue = entry.Cues[k];
                    if (cue.AtMs <= at) continue;
                    cue.AtMs -= at;
                    tail.Cues.Insert(0, cue);
                    entry.Cues.RemoveAt(k);
                }
                tail.QueueMs = tailMs;
                tail.BlockMs = tailBlock;
                entry.BlockMs = Math.Max(0, entry.BlockMs - tailBlock);
                entry.QueueMs = Math.Max(at - (entry.BlockMs), entry.QueueMs - tailMs);
                entry.Reveal = null;
                entries.Insert(i + 1, tail);
                made++;
                i++;
            }
            return made;
        }

        static bool Ends(FxEntry entry)
        {
            foreach (var e in entry.Events)
                if (e is BattleEnded) return true;
            return false;
        }
    }
}
