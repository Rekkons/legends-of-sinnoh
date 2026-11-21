using System;
using Rekkon.UiKit;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 사당·균열 화면 묶음(M5)의 연출 행(04 §8-8 31~34행): 공물과 카드 뽑기(310 — 31행) · 사당의 축복(320 — 32행) · 전설 이벤트(330 — 33행) ·
    /// 천상의 균열의 추종자 공개·집계(340)와 신 강림(341 — 34행). 길이는 방장과 같은 <c>PlayTiming</c>이고 여기 값은 그 안에서 화면이 나누는 박자다 [가정 — plan §5 #115].
    /// 310·320·340은 사당 판(오른쪽 기둥 — <c>ShrineScreen</c>)이 그린다: 판이 없는 좌석(남의 공물을 보기만 하는 좌석)의 것은 기록 줄만 남고 대기열에서 덜어 낸다.
    /// 330·341은 보드(피규어가 내려앉음 · 카메라)와 HUD(어둠 · 고리 · 규토리 비 · 추종자 반짝임)가 모든 좌석에 그린다.
    /// </summary>
    public static class ShrineRows
    {
        public const int Offer = 310, Blessing = 320, Legend = 330, Tally = 340, Descend = 341;

        /// <summary>이 묶음이 그리는 행인가.</summary>
        public static bool Built(int row) => row == Offer || row == Blessing || row == Legend || row == Tally || row == Descend;

        /// <summary>사당 판이 그리는 행인가(판이 떠 있을 때만 남긴다). 나머지(전설 · 강림)는 보드·HUD가 늘 그린다.</summary>
        public static bool InPanel(int row) => row == Offer || row == Blessing || row == Tally;

        /// <summary>전원 차단 컷신(04 33·34행 「until-end(전원)」).</summary>
        public static bool BlocksAll(int row) => row == Legend || row == Descend;

        /// <summary>지금 재생 중인 항목의 그 행 단계와 단계 안 시각(없으면 null · −1).</summary>
        public static FxStep Playing(FxSequencer seq, int row, out int localMs) => WorkRows.Playing(seq, row, out localMs);

        public static T First<T>(FxStep step) where T : GameEvent => WorkRows.EventOf<T>(step);

        /// <summary>대기열(재생 중인 것 포함)에 이 묶음의 판 행이 남았나 — 사당 판은 그동안 닫히지 않는다.</summary>
        public static bool PanelPending(FxSequencer seq)
        {
            if (seq == null) return false;
            foreach (var entry in seq.Queued)
                foreach (var s in entry.Steps)
                    if (InPanel(s.Row) && !(ReferenceEquals(entry, seq.Current) && seq.LocalMs >= s.EndMs)) return true;
            return false;
        }

        /// <summary>대기열에 남은(아직 끝나지 않은) 그 행의 단계 — 없으면 null.</summary>
        public static FxStep Pending(FxSequencer seq, int row)
        {
            if (seq == null) return null;
            foreach (var entry in seq.Queued)
            {
                var s = entry.Step(row);
                if (s != null && !(ReferenceEquals(entry, seq.Current) && seq.LocalMs >= s.EndMs)) return s;
            }
            return null;
        }
    }

    /// <summary>
    /// 공물과 카드 뽑기(04 §8-8 31행, S31 — 2500)의 박자: 예비 = 바친 규토리 칸이 빛남(<see cref="Glow"/>) → 움직임 = 규토리가 칸에서 위로 빨려 오름(<see cref="Fly"/>) ·
    /// 여행 점수 7을 더 냈으면 그동안 숫자가 7만큼 줄어든다(<see cref="JpRoll"/>) → 맞음 = 단계(1·2·3)만큼 높은 빛기둥(<see cref="Pillar"/>) →
    /// 착지 = 뽑은 카드가 한 장씩(<see cref="CardEveryMs"/> 어긋남) 내려와 뒤집힌다(<see cref="Card"/> — 가운데에서 앞면). 사당 판 안에서 그린다.
    /// </summary>
    public static class OfferBeats
    {
        public const int Row = ShrineRows.Offer;
        public const int GlowMs = 300, FlyFromMs = 250, FlyMs = 400, PillarAt = 650, PillarRiseMs = 250, PillarHoldMs = 500, PillarFadeMs = 250,
            CardFromMs = 1150, CardEveryMs = 250, CardDropMs = 250, CardFlipMs = 300;

        /// <summary>바친 규토리 칸의 빛(0 ~ 1).</summary>
        public static float Glow(int ms) => ms < 0 ? 0f : ms < 120 ? Tween.At(ms, 0, 120, 0f, 1f, Ease.OutQuad) : Tween.At(ms, 120, GlowMs + FlyMs, 1f, 0f);

        /// <summary>규토리가 빨려 오른 만큼(0 = 제자리 ~ 1 = 사라짐).</summary>
        public static float Fly(int ms) => Tween.At(ms, FlyFromMs, FlyMs, 0f, 1f, Ease.InQuad);

        /// <summary>빛기둥의 높이(0 ~ 1 — 화면이 단계 / 3을 곱한다)와 밝기(0 ~ 1).</summary>
        public static float Pillar(int ms, out float light)
        {
            light = 0f;
            if (ms < PillarAt) return 0f;
            int t = ms - PillarAt;
            light = t < PillarRiseMs + PillarHoldMs ? 1f : Tween.At(t, PillarRiseMs + PillarHoldMs, PillarFadeMs, 1f, 0f);
            return Tween.At(t, 0, PillarRiseMs, 0f, 1f, Ease.OutQuad);
        }

        /// <summary>빛기둥이 서는 단계 안 시각(소리).</summary>
        public const int PillarSoundAt = PillarAt;

        /// <summary>판 위 사당의 빛기둥(M9 다듬기 P36 — 사당 판의 것과 같은 박자 <see cref="Pillar"/>): 바친 단계(1 · 2 · 3)의 높이(가장 높은 것 = 1).</summary>
        public static float BoardHeight(int tier) => Math.Max(1, Math.Min(3, tier)) / 3f;

        /// <summary>i번째 카드가 뒤집히는 가운데(앞면이 보이기 시작하는) 단계 안 시각.</summary>
        public static int FlipAt(int i) => CardFromMs + CardEveryMs * Math.Max(0, i) + CardDropMs + CardFlipMs / 2;

        /// <summary>
        /// i번째 카드의 모습: show = 보이는 만큼 · drop = 아직 떨어질 높이(1 = 위 ~ 0 = 제자리) · 반환 = 가로 배율(뒤집기 1 → 0 → 1) · face = 앞면이 보이나.
        /// </summary>
        public static float Card(int ms, int i, out float show, out float drop, out bool face)
        {
            int t = ms - CardFromMs - CardEveryMs * Math.Max(0, i);
            show = t < 0 ? 0f : Tween.At(t, 0, 100, 0f, 1f);
            drop = t < 0 ? 1f : Tween.At(t, 0, CardDropMs, 1f, 0f, Ease.OutQuad);
            int f = t - CardDropMs;
            face = f >= CardFlipMs / 2;
            if (f < 0) return 1f;
            if (f >= CardFlipMs) return 1f;
            return Math.Abs(1f - 2f * f / CardFlipMs);
        }

        /// <summary>여행 점수 7을 더 냈을 때 그동안 줄어든 점수(0 ~ cost).</summary>
        public static int JpRoll(int ms, int cost) => (int)Math.Round(Tween.At(ms, FlyFromMs, PillarAt + PillarRiseMs - FlyFromMs, 0f, cost));
    }

    public enum BlessGift { None, Fate, Apricorn, Exchange, Wisdom, Banquet, Treasure, Tranquil }

    /// <summary>축복 그림 하나의 한 순간: 제자리에서 벗어난 만큼(그림 크기의 배수 — x 오른쪽 · y 아래가 +) · 가로 배율(뒤집기) · 크기 · 보이는 만큼.</summary>
    public struct GiftPose
    {
        public float X, Y, Wide, Size, Show;
    }

    /// <summary>
    /// 사당의 축복(04 §8-8 32행, S32 — 1500)의 박자: 카드가 한 번 빛나고(<see cref="Pulse"/>) 받은 것 꼬리표가 톡 뜬다(<see cref="Badge"/>·<see cref="BadgeScale"/> — 「운명의 변화 +2」·「여행 점수 +4」 …).
    /// 나눠 받은 좌석의 것이면(4인 이상) 사당 판에서 그 좌석 칩으로 빛이 간다(<see cref="Beam"/> — <see cref="ArriveAt"/>에 닿는다).
    /// 받은 수(JP · 볼)는 단계 끝에 표시 값으로 착지한다. 효과마다의 고유 착지(M9 다듬기 P35)는 꼬리표 아래 그림 줄이 그린다(<see cref="GiftOf"/>·<see cref="Gift"/> — 축복 일곱 가지마다 다른 움직임).
    /// </summary>
    public static class BlessBeats
    {
        public const int Row = ShrineRows.Blessing;
        public const int PulseMs = 300, BadgeAt = 300, BadgeMs = 160, BeamFromMs = 300, BeamMs = 450, ArriveMs = 300;

        public static float Pulse(int ms) => ms < 0 || ms >= PulseMs ? 0f : (float)Math.Sin(Math.PI * ms / PulseMs);

        public static float Badge(int ms) => ms < BadgeAt ? 0f : Tween.At(ms, BadgeAt, BadgeMs, 0f, 1f, Ease.OutQuad);

        public static float BadgeScale(int ms) => 0.6f + 0.4f * Badge(ms);

        /// <summary>빛줄기가 간 만큼(0 = 사당 판 ~ 1 = 좌석 칩). 닿은 뒤에는 1.</summary>
        public static float Beam(int ms) => Tween.At(ms, BeamFromMs, BeamMs, 0f, 1f, Ease.InOutQuad);

        /// <summary>빛줄기가 닿는 단계 안 시각.</summary>
        public const int ArriveAt = BeamFromMs + BeamMs;

        /// <summary>좌석 칩의 반짝임(0 ~ 1).</summary>
        public static float Arrive(int ms) => ms < ArriveAt || ms >= ArriveAt + ArriveMs ? 0f : Tween.At(ms, ArriveAt, ArriveMs, 1f, 0f, Ease.OutQuad);

        // ── 축복마다의 고유 착지(P35) ──

        /// <summary>그림이 나오기 시작하는 때(꼬리표와 함께) · 그림마다 어긋남 · 한 그림의 움직임 길이 · 단계 끝에서 걷히는 길이 · 그림 수 상한.</summary>
        public const int GiftFromMs = BadgeAt, GiftEveryMs = 110, GiftMs = 550, GiftOutMs = 200, MaxGifts = 3;

        /// <summary>그 사당 카드(카드 키)의 축복 갈래. 축복이 아니면(전설 이벤트 · 천상의 균열) None.</summary>
        public static BlessGift GiftOf(string cardKey)
        {
            switch (cardKey)
            {
                case ShrineRules.FatefulEncounter: return BlessGift.Fate;        // 운명의 변화 n장이 손으로 내려온다
                case ShrineRules.UnexpectedGift: return BlessGift.Apricorn;      // 규토리 n개가 튀어나온다
                case ShrineRules.EquivalentExchange: return BlessGift.Exchange;  // 볼이 두 번 뒤집힌다(다른 포켓몬으로)
                case ShrineRules.AncientWisdom: return BlessGift.Wisdom;         // 기술 카드가 옆에서 빠져나온다
                case ShrineRules.SplendidBanquet: return BlessGift.Banquet;      // 스튜 그릇 n개가 하나씩 톡
                case ShrineRules.HiddenTreasure: return BlessGift.Treasure;      // 볼 하나가 통통 굴러온다
                case ShrineRules.TranquilMoment: return BlessGift.Tranquil;      // 여행 점수가 굴러 오른다
                default: return BlessGift.None;
            }
        }

        /// <summary>그림 수: 운명 · 규토리 · 스튜 = 단계만큼(1 ~ 3) · 볼 · 기술 카드 = 1 · 여행 점수 = 0(숫자만).</summary>
        public static int Gifts(BlessGift gift, int tier)
        {
            switch (gift)
            {
                case BlessGift.Fate: case BlessGift.Apricorn: case BlessGift.Banquet: return Math.Max(1, Math.Min(MaxGifts, tier));
                case BlessGift.Exchange: case BlessGift.Wisdom: case BlessGift.Treasure: return 1;
                default: return 0;
            }
        }

        /// <summary>i번째 그림의 그 순간(ms = 단계 안 시각 · total = 단계 길이). 여행 점수는 숫자 꼬리표의 것(i = 0).</summary>
        public static GiftPose Gift(BlessGift gift, int ms, int total, int i)
        {
            var p = new GiftPose { Wide = 1f, Size = 1f };
            int t = ms - GiftFromMs - GiftEveryMs * Math.Max(0, i);
            if (gift == BlessGift.None || t < 0 || ms >= total) return p;
            float u = Tween.Progress(t, 0, GiftMs), land = Tween.Apply(Ease.OutQuad, u);
            p.Show = Math.Min(Tween.Progress(t, 0, 80), 1f - Tween.Progress(ms, total - GiftOutMs, GiftOutMs));
            switch (gift)
            {
                case BlessGift.Fate: p.Y = -1f * (1f - land); break;
                case BlessGift.Apricorn:
                    p.Y = -0.7f * (float)Math.Sin(u * Math.PI);
                    p.Size = 0.6f + 0.4f * land;
                    break;
                case BlessGift.Exchange: p.Wide = Math.Max(0.05f, (float)Math.Abs(Math.Cos(u * 2 * Math.PI))); break;
                case BlessGift.Wisdom: p.X = -1.5f * (1f - land); break;
                case BlessGift.Banquet: p.Size = u < 0.6f ? Tween.At(t, 0, GiftMs * 6 / 10, 0.2f, 1.3f, Ease.OutQuad) : Tween.At(t, GiftMs * 6 / 10, GiftMs * 4 / 10, 1.3f, 1f, Ease.InQuad); break;
                case BlessGift.Treasure:
                    p.X = 2.5f * (1f - land);
                    p.Y = -0.5f * (float)Math.Abs(Math.Sin(u * 3 * Math.PI)) * (1f - u);
                    break;
                case BlessGift.Tranquil: p.Size = t < GiftMs ? 1f : Tween.At(t, GiftMs, 150, 1.4f, 1f, Ease.OutQuad); break;   // 다 오르면 톡
            }
            return p;
        }

        /// <summary>평온한 순간: 그동안 굴러 오른 여행 점수(0 ~ amount).</summary>
        public static int Roll(int ms, int amount) => (int)Math.Round(Tween.At(ms, GiftFromMs, GiftMs, 0f, amount, Ease.OutQuad));
    }

    /// <summary>전설 이벤트·신 강림의 한 순간: 보드의 어둠 · 피규어가 아직 내려올 높이(1 = 하늘 ~ 0 = 내려앉음, 그 전에는 보이지 않는다) · 눌림 · 착지 고리(0 ~ 1 진행, 밖이면 −1) · 번개(화면을 덮는 흰 빛).</summary>
    public struct DescendPose
    {
        public float Dark, Fall, Squash, Ring, Flash;
        public bool Shown;
    }

    /// <summary>
    /// 전설 이벤트(04 §8-8 33행 — 3500)의 박자: 예비 = 카메라가 그 사당으로 가고 하늘이 어두워짐(<see cref="DarkMs"/>) → 움직임 = 전설 피규어가 내려앉음(<see cref="FallFromMs"/>) →
    /// 맞음 = 착지 고리(<see cref="LandAt"/>) → 착지 = 모든 좌석 칩 위로 그 색 규토리가 떨어지고(좌석마다 <see cref="RainEveryMs"/> 어긋남) 「+n」.
    /// 토큰을 놓지 않는 카드(그 전설이 게임에서 빠졌다)는 피규어·고리 없이 어둠과 규토리 비만. 달빛공터의 바꿔치기는 있던 피규어가 어두워지는 동안 빛으로 흩어지고
    /// (<see cref="ScatterFromMs"/>부터 <c>ScatterBeats</c> — M9 다듬기 P37) 새 전설이 내려온다. 울음은 M9.
    /// </summary>
    public static class LegendBeats
    {
        public const int Row = ShrineRows.Legend;
        public const int DarkMs = 500, FallFromMs = 500, FallMs = 900, LandAt = FallFromMs + FallMs, RingMs = 400, RainFromMs = 1700, RainEveryMs = 150, RainMs = 350, PlusMs = 500, LightAt = 2900;

        /// <summary>바꿔 치운 전설(달빛공터)의 피규어가 빛으로 흩어지기 시작하는 단계 안 시각.</summary>
        public const int ScatterFromMs = 100;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(Row) : null;

        public static LegendEvent Event(FxStep step) => ShrineRows.First<LegendEvent>(step);

        /// <summary>그 항목에서 모든 좌석이 얻은 규토리(전설 이벤트 카드의 ApricornsGained — 바친 좌석부터 시계 방향). 없으면 null.</summary>
        public static ApricornsGained Gains(FxEntry entry)
        {
            if (entry == null) return null;
            foreach (var e in entry.Events)
                if (e is ApricornsGained g && g.Source.Kind == SourceKind.ShrineEvent && g.Changes != null) return g;
            return null;
        }

        /// <summary>규토리 비를 맞는 좌석 수.</summary>
        public static int RainSeats(FxEntry entry)
        {
            var g = Gains(entry);
            return g != null ? g.Changes.Length : 0;
        }

        public static DescendPose At(int ms, int total, bool placed)
        {
            var p = new DescendPose { Ring = -1f, Squash = 1f };
            if (ms < 0 || ms >= total) return p;
            p.Dark = ms < DarkMs ? Tween.At(ms, 0, DarkMs, 0f, 1f) : ms < LightAt ? 1f : Tween.At(ms, LightAt, Math.Max(1, total - LightAt), 1f, 0f);
            if (!placed) return p;
            if (ms >= FallFromMs)
            {
                p.Shown = true;
                p.Fall = Tween.At(ms, FallFromMs, FallMs, 1f, 0f, Ease.InQuad);
                int land = ms - LandAt;
                if (land >= 0 && land < 200) p.Squash = land < 80 ? Tween.At(land, 0, 80, 1f, 0.8f) : Tween.At(land, 80, 120, 0.8f, 1f);
                if (land >= 0 && land < RingMs) p.Ring = land / (float)RingMs;
            }
            return p;
        }

        /// <summary>i번째 좌석(Changes 순서) 칩 위 규토리: 반환 = 아직 떨어질 높이(1 = 위 ~ 0 = 칩, 밖이면 −1) · plus = 「+n」이 보이는 만큼.</summary>
        public static float Rain(int ms, int i, out float plus)
        {
            int t = ms - RainFromMs - RainEveryMs * Math.Max(0, i);
            plus = t < RainMs ? 0f : t < RainMs + PlusMs ? 1f : Tween.At(t, RainMs + PlusMs, 200, 1f, 0f);
            if (t < 0 || t >= RainMs) return -1f;
            return Tween.At(t, 0, RainMs, 1f, 0f, Ease.InQuad);
        }

        /// <summary>i번째 좌석 칩에 규토리가 닿는 단계 안 시각(소리).</summary>
        public static int RainLandAt(int i) => RainFromMs + RainEveryMs * Math.Max(0, i) + RainMs;
    }

    /// <summary>
    /// 천상의 균열 공개·집계(04 §8-8 34행 앞 — 1500)의 박자(사당 판): 모든 좌석의 추종자 토큰이 좌석마다 <see cref="FlipEveryMs"/> 어긋나 한꺼번에 뒤집히고(<see cref="Flip"/> — 가운데에서 신 아이콘),
    /// 신마다 막대가 추종자 수만큼 차오른다(<see cref="Bar"/>). 가장 많은 신의 줄이 빛난다(<see cref="Lead"/>) — 동률이면 뽑은 좌석이 같은 판에서 이어 고른다(결정 75 A4 가).
    /// </summary>
    public static class TallyBeats
    {
        public const int Row = ShrineRows.Tally;
        public const int FlipEveryMs = 60, FlipMs = 300, BarFromMs = 600, BarMs = 500, LeadAt = 1150;

        public static FollowerTally Event(FxStep step) => ShrineRows.First<FollowerTally>(step);

        /// <summary>i번째 좌석 토큰의 가로 배율(1 → 0 → 1)과 앞면(고른 신)이 보이나.</summary>
        public static float Flip(int ms, int i, out bool up)
        {
            int t = ms - FlipEveryMs * Math.Max(0, i);
            up = t >= FlipMs / 2;
            if (t < 0 || t >= FlipMs) return 1f;
            return Math.Abs(1f - 2f * t / FlipMs);
        }

        /// <summary>토큰이 한꺼번에 뒤집히는 단계 안 시각(소리 — 첫 좌석의 가운데).</summary>
        public const int FlipSoundAt = FlipMs / 2;

        /// <summary>막대가 찬 만큼(0 ~ 1).</summary>
        public static float Bar(int ms) => Tween.At(ms, BarFromMs, BarMs, 0f, 1f, Ease.OutQuad);

        /// <summary>가장 많은 신의 줄이 빛나는 만큼(0 ~ 1).</summary>
        public static float Lead(int ms) => ms < LeadAt ? 0f : Tween.At(ms, LeadAt, 150, 0f, 1f);
    }

    /// <summary>
    /// 신 강림(04 §8-8 34행 뒤 — 4000)의 박자: 예비 = 카메라가 시작의 사당으로 가고 하늘이 어두워짐 → 번개 두 번(<see cref="BoltAt"/>·<see cref="Bolt2At"/> — 공간 균열은 흰 빛으로 [가정]) →
    /// 움직임 = 신 피규어가 내려앉음(있던 신은 어두운 동안 사라진다) → 맞음 = 착지 고리 → 착지 = 그 신 추종자의 좌석 칩 아이콘이 반짝이고 「이동력 +1」(<see cref="Spark"/>).
    /// 같은 신이 이미 있으면(Stayed) 내려오지 않고 번개 · 고리 · 반짝임만.
    /// </summary>
    public static class DescendBeats
    {
        public const int Row = ShrineRows.Descend;
        public const int DarkMs = 600, BoltAt = 900, Bolt2At = 1150, BoltMs = 180, FallFromMs = 1300, FallMs = 1100, LandAt = FallFromMs + FallMs, RingMs = 450, SparkAt = 2800, SparkMs = 900, LightAt = 3500;

        public static FxStep StepOf(FxEntry entry) => entry != null ? entry.Step(Row) : null;

        public static GodDescended Event(FxStep step) => ShrineRows.First<GodDescended>(step);

        public static DescendPose At(int ms, int total, bool stayed)
        {
            var p = new DescendPose { Ring = -1f, Squash = 1f };
            if (ms < 0 || ms >= total) return p;
            p.Dark = ms < DarkMs ? Tween.At(ms, 0, DarkMs, 0f, 1f) : ms < LightAt ? 1f : Tween.At(ms, LightAt, Math.Max(1, total - LightAt), 1f, 0f);
            p.Flash = Math.Max(BoltOf(ms, BoltAt), BoltOf(ms, Bolt2At));
            int land = ms - LandAt;
            if (land >= 0 && land < RingMs) p.Ring = land / (float)RingMs;
            if (stayed)
            {
                p.Shown = true;
                return p;
            }
            if (ms >= FallFromMs)
            {
                p.Shown = true;
                p.Fall = Tween.At(ms, FallFromMs, FallMs, 1f, 0f, Ease.InQuad);
                if (land >= 0 && land < 200) p.Squash = land < 80 ? Tween.At(land, 0, 80, 1f, 0.8f) : Tween.At(land, 80, 120, 0.8f, 1f);
            }
            return p;
        }

        static float BoltOf(int ms, int at) => ms < at || ms >= at + BoltMs ? 0f : Tween.At(ms, at, BoltMs, 0.85f, 0f, Ease.OutQuad);

        /// <summary>있던 신(치운 토큰)의 피규어가 아직 보이나: 어두워지는 동안만.</summary>
        public static bool OldShown(int ms) => ms < DarkMs;

        /// <summary>추종자 칩 아이콘의 반짝임: 반환 = 배율(1 ~ 1.5) · plus = 「이동력 +1」이 보이는 만큼.</summary>
        public static float Spark(int ms, out float plus)
        {
            plus = 0f;
            if (ms < SparkAt || ms >= SparkAt + SparkMs) return 1f;
            int t = ms - SparkAt;
            plus = t < SparkMs - 200 ? Tween.At(t, 0, 120, 0f, 1f) : Tween.At(t, SparkMs - 200, 200, 1f, 0f);
            return t < 200 ? Tween.At(t, 0, 200, 1f, 1.5f, Ease.OutQuad) : Tween.At(t, 200, 300, 1.5f, 1f, Ease.InOutQuad);
        }
    }
}
