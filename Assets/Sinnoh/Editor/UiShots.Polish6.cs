using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M9 다듬기 6 「사당 · 전설 연출」 확인 장면(결정 116 — P25 · P28 · P29 · P35 · P36 · P37): 전설 장소의 피규어가 솟음(23행) · 신 피규어가 빛으로 흩어짐(80행) ·
    /// 신의 첫 폼이 미끄러지다 멈춤(78행) · 달빛공터의 바꿔치기(33행 — 크레세리아가 선 달빛공터 사당에 「솟구치는 그림자」) · 판 위 사당의 공물 빛기둥(31행) ·
    /// 축복 일곱 가지의 고유 착지(32행). 그림 <c>Logs/Review/m9/polish6/&lt;P번호&gt;/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] Polish6Screens =
        {
            // P25 전설 장소(23행 — 마그마의 방 · 마그마스톤 · 히드런): 땅울림과 함께 피규어가 솟는 중 → 가장 높이 → 내려앉는 중(줄무늬가 덮기 시작)
            Screen("p6-25-before", FitBoard(s => SiteRise(s, LegendSiteBeats.RiseAtMs - 150))),
            Screen("p6-25-quake", FitBoard(s => SiteRise(s, LegendSiteBeats.RiseAtMs + 90))),
            Screen("p6-25-rise", FitBoard(s => SiteRise(s, LegendSiteBeats.RiseAtMs + LegendSiteBeats.RiseMs / 2))),
            Screen("p6-25-land", FitBoard(s => SiteRise(s, LegendSiteBeats.Ms - 60))),
            // P28 신 격파(80행): 선 채 빛남 → 떠오르며 줄어듦 + 빛 알갱이 → 피규어는 사라지고 알갱이와 「한 번 더!」
            Screen("p6-28-flash", FitBoard(s => GodScatter(s, 100))),
            Screen("p6-28-scatter", FitBoard(s => GodScatter(s, 450))),
            Screen("p6-28-motes", FitBoard(s => GodScatter(s, 780))),
            // P29 신의 둘째 폼(78행): 쓰러지듯 미끄러지는 중 → 멈칫 → 빛기둥이 서고 흰 빛
            Battle("p6-29-slide", s => GodSlump(s, 3200 * FormBeats.SlidePercent / 100 * 2 / 3)),
            Battle("p6-29-halt", s => GodSlump(s, 3200 * 20 / 100)),
            Battle("p6-29-pillar", s => GodSlump(s, 3200 * FormBeats.HoldPercent / 100 + 200)),
            // P37 달빛공터(33행): 크레세리아가 선 채 → 빛으로 흩어지는 중 → 알갱이만 남고 다크라이가 내려오는 중 → 착지
            Screen("p6-37-stand", s => GladeSwap(s, 60)),
            Screen("p6-37-scatter", s => GladeSwap(s, LegendBeats.ScatterFromMs + 420)),
            Screen("p6-37-motes", s => GladeSwap(s, LegendBeats.ScatterFromMs + ScatterBeats.Ms + 60)),
            Screen("p6-37-land", s => GladeSwap(s, LegendBeats.LandAt + 120)),
            // P36 공물(31행): 판 위 사당의 빛기둥 — 솟는 중(3단계) · 1 · 2 · 3단계가 다 선 때(높이 · 색이 다르다)
            Screen("p6-36-rise", s => OfferSky(s, ApricornColor.Black, OfferBeats.PillarAt + 90)),
            Screen("p6-36-tier1", s => OfferSky(s, ApricornColor.Red, OfferBeats.PillarAt + 350)),
            Screen("p6-36-tier2", s => OfferSky(s, ApricornColor.Pink, OfferBeats.PillarAt + 350)),
            Screen("p6-36-tier3", s => OfferSky(s, ApricornColor.Black, OfferBeats.PillarAt + 350)),
            // P35 사당의 축복(32행) 일곱 가지: 운명 카드가 내려옴 · 규토리가 튀어나옴 · 볼이 뒤집힘 · 기술 카드가 빠져나옴 · 스튜 그릇이 톡 · 볼이 굴러옴 · 여행 점수가 오름
            Screen("p6-35-fate", Fit(s => BlessGifts(s, ShrineRules.FatefulEncounter, BlessBeats.GiftFromMs + 260))),
            Screen("p6-35-apricorn", Fit(s => BlessGifts(s, ShrineRules.UnexpectedGift, BlessBeats.GiftFromMs + 260))),
            Screen("p6-35-exchange", Fit(s => BlessGifts(s, ShrineRules.EquivalentExchange, BlessBeats.GiftFromMs + 200))),
            Screen("p6-35-wisdom", Fit(s => BlessGifts(s, ShrineRules.AncientWisdom, BlessBeats.GiftFromMs + 200))),
            Screen("p6-35-banquet", Fit(s => BlessGifts(s, ShrineRules.SplendidBanquet, BlessBeats.GiftFromMs + 260))),
            Screen("p6-35-treasure", Fit(s => BlessGifts(s, ShrineRules.HiddenTreasure, BlessBeats.GiftFromMs + 200))),
            Screen("p6-35-tranquil", Fit(s => BlessGifts(s, ShrineRules.TranquilMoment, BlessBeats.GiftFromMs + 300))),
            Screen("p6-35-landed", Fit(s => BlessGifts(s, ShrineRules.FatefulEncounter, BlessBeats.GiftFromMs + BlessBeats.GiftEveryMs + BlessBeats.GiftMs + 50))),   // 다 내려앉은 때
        };

        // P36: 사당 길의 판에서 그 색 규토리를 바친 뒤 공물 단계(310)의 ms — 판 위 빛기둥의 단계 · 높이
        static void OfferSky(UiShot s, ApricornColor color, int ms)
        {
            OfferOpen(s, color);
            ShrineInto(s, ShrineRows.Offer, ms);
            OfferCheck(s, color, ms);
        }

        // [바치기]까지(공물 단계가 대기열에 섰다)
        static FxStep OfferOpen(UiShot s, ApricornColor color)
        {
            var shrine = ShrineOpen(s);
            if (!shrine.PickColor((int)color) || !shrine.PressOffer()) throw new InvalidOperationException("[바치기]가 거절됐다(" + color + ")");
            return Queued(s.Made, st => st.Row == ShrineRows.Offer) ?? throw new InvalidOperationException("공물 단계(310)가 대기열에 없다");
        }

        static void OfferCheck(UiShot s, ApricornColor color, int ms)
        {
            var fx = s.Made.Hud.ShrineFx;
            int tier = MapActions.ApricornGrade(color);
            float want = OfferBeats.Pillar(ms, out float light) * OfferBeats.BoardHeight(tier);
            if (light <= 0f)
            {
                if (fx.SkyShown != 0) throw new InvalidOperationException("빛기둥이 설 때가 아닌데 보인다(" + ms + "ms)");
                return;
            }
            if (fx.SkyShown != tier || Mathf.Abs(fx.SkyHeight - want) > 0.01f) throw new InvalidOperationException("판 위 빛기둥이 박자와 다르다: 단계 " + fx.SkyShown + " · 높이 " + fx.SkyHeight + " (" + tier + " · " + want + ")");
            var box = s.Made.Stage.Root.Q("board-hud-shrine-sky").worldBound;
            if (!(box.height > 4f) || box.yMin < 0f) throw new InvalidOperationException("빛기둥이 화면 밖이거나 서지 않았다: " + box);
        }

        // P35: 혼자 판 — 덱 맨 위를 그 축복 카드로 두고 분홍(2단계)을 바친 뒤 축복 단계(320)의 ms
        static void BlessGifts(UiShot s, string card, int ms)
        {
            BlessOpen(s, card);
            ShrineInto(s, ShrineRows.Blessing, ms);
            BlessCheck(s, card, ms);
        }

        // [바치기]까지(축복 단계가 대기열에 섰다)
        static FxStep BlessOpen(UiShot s, string card)
        {
            var flow = Seats(s, 1, st =>
            {
                SoloTrail.PrepareShrine(s.Made.Ctx, st, 0, card);
                ShrineDiscards(st);
            });
            ShrineSettle(s, flow, f => f.InputOpen && f.Hud != null && f.Hud.Shows(BoardHud.Offer));
            var shrine = ShrineOpen(s, flow);
            if (!shrine.PickColor((int)ApricornColor.Pink) || !shrine.PressOffer()) throw new InvalidOperationException("[바치기]가 거절됐다");
            return Queued(flow, st => st.Row == ShrineRows.Blessing) ?? throw new InvalidOperationException("축복 단계(320)가 대기열에 없다");
        }

        static void BlessCheck(UiShot s, string card, int ms)
        {
            var flow = s.Made;
            var shrine = flow.Stage.Current as ShrineScreen;
            var gift = BlessBeats.GiftOf(card);
            if (shrine == null || shrine.GiftKind != gift) throw new InvalidOperationException("축복 갈래가 다르다: " + (shrine != null ? shrine.GiftKind.ToString() : "판 없음") + " ≠ " + gift);
            int want = 0;
            for (int i = 0; i < BlessBeats.Gifts(gift, 2); i++)
                if (BlessBeats.Gift(gift, ms, 1500, i).Show > 0f) want++;
            if (shrine.GiftsShown != want) throw new InvalidOperationException("축복 그림 수가 박자와 다르다: " + shrine.GiftsShown + " ≠ " + want);
            bool text = gift == BlessGift.Tranquil && BlessBeats.Gift(gift, ms, 1500, 0).Show > 0f;
            if (text != (shrine.GiftText != null)) throw new InvalidOperationException("여행 점수 숫자: " + shrine.GiftText);
            // 고대의 지혜: 기술 줄 꼴 칸 안에 타입 그림(어느 기술일지는 아직 모른다 — 「???」)
            var first = flow.Stage.Root.Q("board-shrine-gift-0");
            var plate = first != null ? first.Q(className: "sn-type-icon") : null;
            if (gift == BlessGift.Wisdom ? want > 0 && (plate == null || plate.resolvedStyle.backgroundImage.texture == null) : plate != null) throw new InvalidOperationException("기술 칸의 타입 그림: " + (plate != null) + " (" + gift + ")");
            if (gift == BlessGift.Wisdom && want > 0 && first.resolvedStyle.opacity >= 1f && !first.worldBound.Contains(plate.worldBound.center)) throw new InvalidOperationException("타입 그림이 기술 칸 밖이다: " + plate.worldBound + " · 칸 " + first.worldBound);
            var row = flow.Stage.Root.Q("board-shrine-gifts").worldBound;
            var panel = flow.Stage.Root.Q(shrine.Name).worldBound;
            if (row.yMax > panel.yMax + 0.5f || row.xMin < panel.xMin - 0.5f) throw new InvalidOperationException("그림 줄이 사당 판 밖이다: " + row + " · 판 " + panel);
        }

        // P25: 그 유물을 가진 좌석이 그 전설 장소에서 [찾아보기] → 조우 전환 단계의 앞 ms. 그 칸의 피규어가 박자대로 솟았나
        // (기본 = 마그마의 방 히드런. 솟는 높이는 피규어마다 다르다 — 낮은 피규어일수록 더 높이 BoardFigures.RiseOf: 그랜드 샤토의 로토무 · 감사의 꽃밭의 쉐이미)
        static void SiteRise(UiShot s, int ms, string site = MagmaChamber, string item = "item.magma_stone")
        {
            var step = SiteOpen(s, site, item);
            Laid(s, s.Made, step, ms);
            SiteCheck(s, site, ms);
        }

        const string MagmaChamber = "legendary_site.magma_chamber";

        // [찾아보기]까지(전설 배틀의 조우 전환 단계가 대기열에 섰다)
        static FxStep SiteOpen(UiShot s, string site, string item)
        {
            var ctx = s.Board.Ctx;
            int cell = FixtureCell(ctx, site);
            var flow = Seats(s, 1, state =>
            {
                GiveTeam(ctx, state);
                state.Seats[0].Cell = cell;
                state.Seats[0].Bag.Add(ctx.Items.Ids.IndexOf(item));
            });
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException("[찾아보기]가 거절됐다(전설 장소 · " + item + ")");
            return Queued(flow, st => st.Row == EnterRows.Wipe && ResultRows.EventOf<BattleStarted>(st) != null) ?? throw new InvalidOperationException("전설 배틀의 조우 전환 단계가 대기열에 없다");
        }

        static void SiteCheck(UiShot s, string site, int ms)
        {
            var flow = s.Made;
            int cell = FixtureCell(s.Board.Ctx, site);
            LegendSiteBeats.Figure(ms, out float hop, out _, out _);
            float rise = flow.FigureRise(cell), top = flow.FigureRiseTop(cell);
            if (rise < 0f) throw new InvalidOperationException("그 전설 장소 칸에 선 피규어가 없다");
            if (top < BoardFigures.RiseHeight) throw new InvalidOperationException("솟는 높이가 기본보다 낮다: " + top);
            if (Mathf.Abs(rise - hop * top) > 0.02f) throw new InvalidOperationException("피규어가 박자대로 솟지 않았다: " + rise + " ≠ " + hop * top);
        }

        // P28: 신 격파 단계(800)의 ms — 피규어의 크기와 빛 알갱이가 박자대로인지
        static void GodScatter(UiShot s, int ms)
        {
            GodBattle(s, ResultRows.GodGone, ms);
            GodScatterCheck(s, ms);
        }

        static void GodScatterCheck(UiShot s, int ms)
        {
            var flow = s.Made;
            bool want = ScatterBeats.At(ms, out float size, out _);
            bool shown = flow.FigureShown("487:1", out float drawn);
            if (shown != want || (want && Mathf.Abs(drawn - size) > 0.02f)) throw new InvalidOperationException("신 피규어가 박자대로 흩어지지 않았다: 보임 " + shown + " · 크기 " + drawn + " (박자 " + want + " · " + size + ")");
            int motes = 0;
            for (int i = 0; i < ScatterBeats.Motes; i++)
                if (ScatterBeats.Mote(ms, i, out _, out _) > 0f) motes++;
            if (flow.Results.MotesShown != motes) throw new InvalidOperationException("빛 알갱이 수가 박자와 다르다: " + flow.Results.MotesShown + " ≠ " + motes);
        }

        // P29: 둘째 폼 단계(780)의 ms — 첫 폼이 선 채 미끄러지다 멈춘다
        static void GodSlump(UiShot s, int ms)
        {
            GodBattle(s, ResultRows.GodPhase, ms);
            GodSlumpCheck(s, ms);
        }

        static void GodSlumpCheck(UiShot s, int ms)
        {
            var screen = (BattleScreen)s.Made.Stage.Current;
            var pose = FormBeats.At(ms, 3200, true);
            var enemy = screen.Painted.Enemy;
            if (enemy == null || !enemy.Standing) throw new InvalidOperationException("신의 첫 폼이 서 있지 않다");
            if (Mathf.Abs(enemy.Sink - pose.Sink) > 0.02f) throw new InvalidOperationException("미끄러진 깊이가 박자와 다르다: " + enemy.Sink + " ≠ " + pose.Sink);
            if (enemy.Key != (pose.After ? "487:2" : "487:1")) throw new InvalidOperationException("그려진 폼이 박자와 다르다: " + enemy.Key + " (" + ms + "ms)");
        }

        const string Cresselia = "488:0", Darkrai = "491:0";

        // P37: 4인 판 — 크레세리아 토큰이 선 달빛공터 사당 · 덱 맨 위 「솟구치는 그림자」(다크라이가 크레세리아를 바꿔 치운다) → 전설 단계(330)의 ms
        static void GladeSwap(UiShot s, int ms)
        {
            GladeOpen(s);
            ShrineInto(s, ShrineRows.Legend, ms);
            GladeCheck(s, ms);
        }

        // [바치기]까지(전설 이벤트 단계가 대기열에 섰다)
        static FxStep GladeOpen(UiShot s)
        {
            GameFlow flow = Seats(s, 4, st =>
            {
                var ctx = s.Made.Ctx;
                SoloTrail.PrepareShrine(ctx, st, 0, "shrine_event.shadow_emerges");
                int def = ctx.Cards.PokemonIds.IndexOf(Cresselia), cell = FixtureCell(ctx, "legendary_shrine.moonglade_shrine");
                var token = st.Tokens.Find(t => t.Kind == TokenKind.Legend && t.Def == def);
                if (token == null) st.Tokens.Add(new TokenState { Kind = TokenKind.Legend, Def = def, Cell = cell, FaceUp = true });
                else token.Cell = cell;
            });
            for (int k = 0; k < 8 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            ShrineSettle(s, flow, f => f.InputOpen && f.Hud != null && f.Hud.Shows(BoardHud.Offer));
            if (!flow.FigureShown(Cresselia, out _)) throw new InvalidOperationException("달빛공터에 크레세리아 피규어가 서 있지 않다");
            var shrine = ShrineOpen(s, flow);
            if (!shrine.PickColor((int)ApricornColor.Pink) || !shrine.PressOffer()) throw new InvalidOperationException("[바치기]가 거절됐다");
            var step = Queued(flow, st => st.Row == ShrineRows.Legend) ?? throw new InvalidOperationException("전설 이벤트 단계(330)가 대기열에 없다");
            var e = LegendBeats.Event(step);
            if (e == null || e.ReplacedSpecies < 0 || !e.Placed) throw new InvalidOperationException("바꿔치기가 아니다(전설 이벤트 " + (e != null ? e.ReplacedSpecies + " · " + e.Placed : "없음") + ")");
            return step;
        }

        static void GladeCheck(UiShot s, int ms)
        {
            var flow = s.Made;
            bool want = ScatterBeats.At(ms - LegendBeats.ScatterFromMs, out float size, out _);
            bool shown = flow.FigureShown(Cresselia, out float drawn);
            if (shown != want || (want && Mathf.Abs(drawn - size) > 0.02f)) throw new InvalidOperationException("크레세리아가 박자대로 흩어지지 않았다: 보임 " + shown + " · 크기 " + drawn + " (박자 " + want + " · " + size + ")");
            int since = ms - LegendBeats.ScatterFromMs;
            bool motes = since >= 0 && since < ScatterBeats.MoteMs;
            if ((flow.Hud.ShrineFx.MotesShown > 0) != motes) throw new InvalidOperationException("빛 알갱이가 박자와 다르다: " + flow.Hud.ShrineFx.MotesShown + " (" + since + "ms)");
            if (flow.FigureShown(Darkrai, out _) != (ms >= LegendBeats.FallFromMs)) throw new InvalidOperationException("다크라이 피규어가 박자와 다르다");
        }
    }
}
