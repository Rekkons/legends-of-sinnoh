using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 스탬프·도전·거래의 덮개(M5 「스탬프·도전·거래 화면」 — 04 §8-8 47·51·52행). 무대의 맨 위 층(<see cref="UiStage.Over"/> — 창·배틀 화면 위, 마우스를 받지 않는다)에 <see cref="GameFlow"/>가 하나 둔다:
    /// 여행 스탬프(가운데 작은 판 — 일곱 칸에 도장이 내려와 찍히고 수가 오르고 보상이 차례로 내려앉는다) · 도전 VS(가로 띠 — 양쪽 얼굴 상자가 미끄러져 들어와 부딪히고 VS가 박힌다) ·
    /// 거래(가로 띠 — 양쪽이 올린 것이 가운데로 모였다 엇갈려 건너가고 받는 쪽이 빛난다).
    /// 박자는 <c>Sinnoh.Fx</c> <see cref="StampBeats"/>·<see cref="VsBeats"/>·<see cref="TradeBeats"/>, 시간은 대기열 시각만(옮기기·늘이기·흐리기 — FxUi).
    /// 요소 이름: <c>deal-fx</c>·<c>deal-stamp</c>·<c>deal-stamp-slot-&lt;n&gt;</c>·<c>deal-stamp-count</c>·<c>deal-stamp-reward-&lt;i&gt;</c>·<c>deal-vs</c>·<c>deal-vs-face-a</c>·<c>deal-vs-face-b</c>·<c>deal-vs-mark</c>·
    /// <c>deal-trade</c>·<c>deal-trade-face-a</c>·<c>deal-trade-face-b</c>·<c>deal-trade-chip-a-&lt;i&gt;</c>·<c>deal-trade-chip-b-&lt;i&gt;</c>.
    /// </summary>
    public sealed class DealFx
    {
        /// <summary>한 번에 적는 보상 줄 · 한쪽이 올린 것의 줄 수(넘으면 마지막 줄이 「+n」).</summary>
        public const int MaxRewards = 4, MaxChips = 3;
        /// <summary>720 벌 px: 도장이 내려오는 높이 · 보상 줄이 내려앉는 높이 · 얼굴이 들어오는 거리 · VS 얼굴이 서는 자리(가운데에서) · 거래 얼굴이 서는 자리 · 올린 것이 놓이는 자리 · 줄 간격.</summary>
        public const float DropPx = 44f, RewardDropPx = 12f, SlidePx = 460f, VsSidePx = 125f, SidePx = 340f, ChipPx = 170f, ChipRowPx = 21f;
        /// <summary>두 언어에 같은 글리프(문자열 표에 두지 않는다).</summary>
        public const string Vs = "VS", Bang = "!";

        readonly UiStage stage;
        readonly GameFlow flow;
        readonly VisualElement stampBox, vsBand, tradeBand, vsFlash;
        readonly VisualElement[] slots = new VisualElement[SeatState.StampCount], seals = new VisualElement[SeatState.StampCount], inks = new VisualElement[SeatState.StampCount];
        readonly Label count, cond, vsMark, vsBang, tradeTitle;
        readonly List<Label> rewards = new List<Label>();
        readonly VisualElement[] vsSide = new VisualElement[2], vsFace = new VisualElement[2], tradeSide = new VisualElement[2], tradeFace = new VisualElement[2], tradeGlow = new VisualElement[2];
        readonly Label[] vsName = new Label[2], tradeName = new Label[2];
        readonly List<Label>[] chips = { new List<Label>(), new List<Label>() };

        public readonly VisualElement Element;

        /// <summary>지금 보이는 것(확인용): 스탬프 판 · 찍힌 칸(1~7, 없으면 0) · 보이는 보상 줄 수 · VS 띠 · VS가 박혔나 · 거래 띠 · 보이는 거래 줄 수 · 받는 쪽 빛.</summary>
        public bool StampShown { get; private set; }
        public int Stamped { get; private set; }
        public int RewardsShown { get; private set; }
        public bool VsShown { get; private set; }
        public bool VsMarked { get; private set; }
        public bool TradeShown { get; private set; }
        public int ChipsShown { get; private set; }
        public bool GlowShown { get; private set; }

        public DealFx(UiStage stage, GameFlow flow)
        {
            this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            var inv = CultureInfo.InvariantCulture;
            Element = new VisualElement { name = "deal-fx", pickingMode = PickingMode.Ignore };
            Element.AddToClassList("sn-deal-fx");

            // 여행 스탬프(47행)
            stampBox = Box(Element, "deal-stamp", "sn-deal-stamp");
            Text(stampBox, stage.T("screen.stamp.fx.title"), Kit.FTitle, null, "kit-title");
            // 원작 스탬프 시트 그림 위에 도장(M9 다듬기 P12 — 결정 80 L18 나): 칸 = 동그라미 자리(StampScreen.Spots), 받은 칸은 찍힌 도장 · 이번 칸은 도장이 내려와 찍힌다
            var row = Box(stampBox, "deal-stamp-sheet", "sn-deal-slots");
            StampScreen.Art(row, GenAssets.StampSheet);
            for (int i = 0; i < slots.Length; i++)
            {
                string n = (i + 1).ToString(inv);
                slots[i] = Box(row, "deal-stamp-slot-" + n, "sn-deal-slot");
                StampScreen.Place(slots[i], i);
                StampScreen.Art(Box(slots[i], null, "sn-deal-mark"), GenAssets.StampSeal);
                inks[i] = Box(slots[i], null, "sn-deal-ink");
                seals[i] = Box(slots[i], null, "sn-deal-seal");
                StampScreen.Art(seals[i], GenAssets.StampSeal);
            }
            count = Text(stampBox, "", Kit.F3, "deal-stamp-count", "kit-title");
            cond = Text(stampBox, "", Kit.F2, "deal-stamp-cond", "sn-deal-cond");   // 어떤 조건으로 찍었나(결정 101 ④)
            var list = Box(stampBox, null, "sn-deal-rewards");
            for (int i = 0; i < MaxRewards; i++)
            {
                var l = Kit.Badge("", KitBadgeKind.Ok, Kit.F2);
                l.name = "deal-stamp-reward-" + (i + 1).ToString(inv);
                l.pickingMode = PickingMode.Ignore;
                l.AddToClassList("sn-deal-reward");
                list.Add(l);
                rewards.Add(l);
            }

            // 도전 VS(51행)
            vsBand = Box(Element, "deal-vs", "sn-deal-band");
            vsFlash = Box(vsBand, null, "sn-deal-flash");
            Sides(vsBand, "deal-vs-face-", vsSide, vsFace, vsName, null);
            vsBang = Text(vsBand, Bang, Kit.F3, "deal-vs-bang", "sn-deal-bang");
            vsMark = Text(vsBand, Vs, Kit.F3, "deal-vs-mark", "sn-deal-vs");

            // 거래(52행)
            tradeBand = Box(Element, "deal-trade", "sn-deal-band");
            tradeBand.AddToClassList("sn-deal-band-trade");
            tradeTitle = Text(tradeBand, "", Kit.F2, "deal-trade-title", "sn-deal-title");
            Sides(tradeBand, "deal-trade-face-", tradeSide, tradeFace, tradeName, tradeGlow);
            for (int g = 0; g < 2; g++)
                for (int i = 0; i < MaxChips; i++)
                {
                    var l = Kit.Badge("", KitBadgeKind.Plain, Kit.F2);
                    l.name = "deal-trade-chip-" + (g == 0 ? "a-" : "b-") + (i + 1).ToString(inv);
                    l.pickingMode = PickingMode.Ignore;
                    l.AddToClassList("sn-deal-chip");
                    tradeBand.Add(l);
                    chips[g].Add(l);
                }
            Unpick();
        }

        // UI Toolkit은 부모가 PickingMode.Ignore여도, opacity 0이어도 자식을 잡는다 — 얼굴 상자의 조각까지 모두 마우스를 받지 않게(보이지 않는 덮개가 칸 고르기·휠을 막지 않게)
        void Unpick() => Element.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);

        static VisualElement Box(VisualElement parent, string name, string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            if (name != null) e.name = name;
            e.AddToClassList(cls);
            parent.Add(e);
            return e;
        }

        static Label Text(VisualElement parent, string text, string font, string name, string cls)
        {
            var l = Kit.Text(text, font);
            l.pickingMode = PickingMode.Ignore;
            if (name != null) l.name = name;
            if (cls != null) l.AddToClassList(cls);
            parent.Add(l);
            return l;
        }

        // 띠의 양쪽: 큰 얼굴 상자 + 좌석 이름(a = 왼쪽 · b = 오른쪽), 거래 띠는 얼굴 둘레의 빛도
        static void Sides(VisualElement band, string faceName, VisualElement[] side, VisualElement[] face, Label[] name, VisualElement[] glow)
        {
            for (int g = 0; g < 2; g++)
            {
                side[g] = Box(band, null, "sn-deal-side");
                face[g] = FaceBox.Make(true);
                face[g].name = faceName + (g == 0 ? "a" : "b");
                face[g].pickingMode = PickingMode.Ignore;
                side[g].Add(face[g]);
                if (glow != null) glow[g] = Box(face[g], null, "sn-deal-glow");
                name[g] = Text(side[g], "", Kit.F2, null, "sn-deal-name");
            }
        }

        float Unit => stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;

        /// <summary>그 스탬프 아이템(「stamp.n」)의 칸(0 ~ 6, 모르면 −1).</summary>
        public static int SlotOf(Ctx ctx, int stampKey)
        {
            for (int i = 0; i < SeatState.StampCount; i++)
                if (StampRules.ItemOf(ctx, i) == stampKey) return i;
            return -1;
        }

        /// <summary>거래 한 줄의 글(「파랑 규토리 ×2」·「여행 점수 ×3」·포켓몬 이름 · 운명 카드는 당사자에게만 이름, 남에게는 장수).</summary>
        public static string ItemText(UiStage stage, GameFlow flow, TradeKind kind, int key, int count)
        {
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            string n = " " + WorkUi.Times + count.ToString(inv);
            switch (kind)
            {
                case TradeKind.Apricorn: return stage.T(BoardLog.ApricornKey(key)) + " " + stage.T("screen.prompt.kind.apricorn") + n;
                case TradeKind.Jp: return stage.T("screen.prompt.kind.jp") + n;
                case TradeKind.Ball:
                {
                    int item = CraftRules.BallItem(ctx, (Ball)key);
                    return (item >= 0 ? stage.T(ctx.Items.Items[item].Key) : stage.T("screen.prompt.kind.ball")) + n;
                }
                case TradeKind.Item: return (key >= 0 && key < ctx.Items.Items.Count ? stage.T(ctx.Items.Items[key].Key) : stage.T("screen.prompt.kind.item")) + n;
                case TradeKind.Pokemon:
                {
                    var view = flow.Client != null ? flow.Client.View : null;
                    // 폼 꼬리 「(…)」는 뺀다(아래 판의 파티 칸과 같다 — 좁은 줄에서 잘리지 않게)
                    return view != null && key >= 0 && key < view.Pokemon.Count && view.Pokemon[key].Def >= 0 ? BoardHud.ShortName(stage.T("pokemon." + ctx.Cards.Pokemon[view.Pokemon[key].Def].Key)) : stage.T("screen.prompt.kind.pokemon");
                }
                default:
                    return key >= 0 ? stage.T(FateCards.NameKey(FateRules.KindOf(ctx, key))) : stage.T("screen.prompt.kind.fate") + n;
            }
        }

        /// <summary>그 순간을 그린다(대기열이 없거나 이 묶음의 단계가 아니면 모두 숨긴다).</summary>
        public void Show(FxSequencer seq)
        {
            ShowStamp(seq);
            ShowVs(seq);
            ShowTrade(seq);
        }

        void ShowStamp(FxSequencer seq)
        {
            var inv = CultureInfo.InvariantCulture;
            var step = DealRows.Playing(seq, DealRows.Stamp, out int ms);
            var e = StampBeats.Event(step);
            if (e != null && flow.Client != null && e.Seat != flow.Client.Seat && (flow.OwnWindowUp || !flow.HostPaced)) e = null;   // 남의 스탬프는 내 창을 덮지 않는다(결정 120) · 방장 시각 판이 아니면 그 좌석 칩 꼬리표만(결정 178)
            StampShown = false;
            Stamped = 0;
            RewardsShown = 0;
            if (e == null)
            {
                FxUi.Fade(stampBox, 0f);
                return;
            }
            float unit = Unit;
            var pose = StampBeats.At(ms, step.Ms);
            StampShown = pose.Show > 0f;
            FxUi.Fade(stampBox, pose.Show);
            FxUi.Move(stampBox, pose.Shake * unit, 0f);
            int slot = SlotOf(flow.Ctx, e.StampKey);
            var view = flow.Client != null ? flow.Client.View : null;
            var mine = view != null && e.Seat >= 0 && e.Seat < view.Seats.Length ? view.Seats[e.Seat].Stamps : null;
            for (int i = 0; i < slots.Length; i++)
            {
                bool now = i == slot;
                slots[i].EnableInClassList("sn-deal-slot-on", now ? pose.Stamped : mine != null && mine[i]);
                FxUi.Fade(seals[i], now && !pose.Stamped ? Mathf.Clamp01((1f - pose.Drop) * 4f) : 0f);
                FxUi.Move(seals[i], 0f, now ? -pose.Drop * DropPx * unit : 0f);
                FxUi.Scale(seals[i], now ? pose.Scale : 1f, now ? pose.Scale : 1f);
                FxUi.Scale(slots[i], now && pose.Stamped ? pose.Scale : 1f, now && pose.Stamped ? pose.Scale : 1f);
                bool ink = now && pose.Ink >= 0f;
                FxUi.Fade(inks[i], ink ? 1f - pose.Ink : 0f);
                FxUi.Scale(inks[i], ink ? 1f + 1.2f * pose.Ink : 1f, ink ? 1f + 1.2f * pose.Ink : 1f);
            }
            if (pose.Stamped) Stamped = slot + 1;
            cond.text = slot >= 0 ? stage.T("screen.stamp.fx.cond").Replace("{cond}", stage.T("screen.stamp.cond." + (slot + 1).ToString(inv))) : "";
            count.text = (pose.Stamped ? e.CountAfter : e.CountBefore).ToString(inv) + "/" + SeatState.StampCount.ToString(inv);
            FxUi.Scale(count, pose.Count, pose.Count);

            // 보상 줄: 자원 변화마다(규토리 색 · 수호신의 서) + 기술 카드 묶음 하나
            var lines = new List<string>();
            if (e.Changes != null)
                foreach (var c in e.Changes)
                {
                    if (c == null) continue;
                    string what = c.Res == ResKind.Apricorn ? stage.T(BoardLog.ApricornKey(c.Key)) + " " + stage.T("screen.prompt.kind.apricorn")
                        : c.Res == ResKind.Item && c.Key >= 0 && c.Key < flow.Ctx.Items.Items.Count ? stage.T(flow.Ctx.Items.Items[c.Key].Key) : stage.T("screen.prompt.kind.item");
                    lines.Add(what + " " + WorkUi.Signed(c.After - c.Before));
                }
            if (e.MoveKeys != null && e.MoveKeys.Length > 0) lines.Add(stage.T("screen.stamp.fx.moves").Replace("{n}", e.MoveKeys.Length.ToString(inv)));
            for (int i = 0; i < rewards.Count; i++)
            {
                if (i >= lines.Count)
                {
                    FxUi.Fade(rewards[i], 0f);
                    continue;
                }
                rewards[i].text = lines[i];
                float show = StampBeats.Reward(ms, i, out float drop);
                FxUi.Fade(rewards[i], show);
                FxUi.Move(rewards[i], 0f, -drop * RewardDropPx * unit);
                if (show > 0f) RewardsShown++;
            }
        }

        void Paint(VisualElement face, Label name, int seat)
        {
            FaceBox.Paint(face, seat, flow.SeatColor(seat), flow.Protagonist(seat));
            name.text = flow.SeatName(seat);
            face.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
        }

        void ShowVs(FxSequencer seq)
        {
            var step = DealRows.Playing(seq, DealRows.Vs, out int ms);
            var e = VsBeats.Event(step);
            VsShown = VsMarked = false;
            if (e == null || e.From < 0 || e.To < 0)
            {
                FxUi.Fade(vsBand, 0f);
                return;
            }
            float unit = Unit;
            var pose = VsBeats.At(ms, step.Ms);
            VsShown = pose.Show > 0f;
            FxUi.Fade(vsBand, pose.Show);
            Paint(vsFace[0], vsName[0], e.From);
            Paint(vsFace[1], vsName[1], e.To);
            float away = pose.Slide * SlidePx * unit;
            FxUi.Move(vsSide[0], -VsSidePx * unit - away, 0f);
            FxUi.Move(vsSide[1], VsSidePx * unit + away, 0f);
            FxUi.Fade(vsBang, pose.Bang);
            VsMarked = pose.Vs > 0f;
            FxUi.Fade(vsMark, VsMarked ? 1f : 0f);
            FxUi.Scale(vsMark, VsMarked ? pose.Vs : 1f, VsMarked ? pose.Vs : 1f);
            FxUi.Fade(vsFlash, pose.Flash * 0.6f);
        }

        void ShowTrade(FxSequencer seq)
        {
            var inv = CultureInfo.InvariantCulture;
            var step = DealRows.Playing(seq, DealRows.Trade, out int ms);
            var e = TradeBeats.Event(step);
            TradeShown = GlowShown = false;
            ChipsShown = 0;
            if (e == null || e.A < 0 || e.B < 0)
            {
                FxUi.Fade(tradeBand, 0f);
                return;
            }
            float unit = Unit;
            var pose = TradeBeats.At(ms, step.Ms);
            TradeShown = pose.Show > 0f;
            FxUi.Fade(tradeBand, pose.Show);
            tradeTitle.text = stage.T("screen.trade.fx.title") + (e.Remote ? " · " + stage.T("screen.trade.fx.remote") : "");
            Paint(tradeFace[0], tradeName[0], e.A);
            Paint(tradeFace[1], tradeName[1], e.B);
            FxUi.Move(tradeSide[0], -SidePx * unit, 0f);
            FxUi.Move(tradeSide[1], SidePx * unit, 0f);
            var gives = new[] { e.GiveA, e.GiveB };
            // 줄: 위쪽은 A가 준 것(왼쪽 → 오른쪽), 그 아래는 B가 준 것(오른쪽 → 왼쪽) — 엇갈려 건너가도 글이 겹치지 않는다
            int rowsA = Mathf.Min(MaxChips, e.GiveA != null ? e.GiveA.Length : 0);
            for (int g = 0; g < 2; g++)
            {
                // 받는 쪽 = 반대편: 받을 것이 있으면 그 얼굴 둘레가 빛난다
                bool gets = gives[1 - g] != null && gives[1 - g].Length > 0;
                FxUi.Fade(tradeGlow[g], gets ? pose.Glow : 0f);
                if (gets && pose.Glow > 0f) GlowShown = true;
                int n = gives[g] != null ? gives[g].Length : 0;
                float from = (g == 0 ? -1f : 1f) * ChipPx * unit;
                for (int i = 0; i < chips[g].Count; i++)
                {
                    var chip = chips[g][i];
                    if (i >= n)
                    {
                        FxUi.Fade(chip, 0f);
                        continue;
                    }
                    var it = gives[g][i];
                    chip.text = i == MaxChips - 1 && n > MaxChips ? WorkUi.Plus + (n - MaxChips + 1).ToString(inv) : ItemText(stage, flow, it.Kind, it.Key, it.Count);
                    FxUi.Move(chip, Mathf.Lerp(from, -from, pose.Cross), (i + (g == 1 ? rowsA : 0)) * ChipRowPx * unit);
                    FxUi.Fade(chip, 1f);
                    ChipsShown++;
                }
            }
        }
    }
}
