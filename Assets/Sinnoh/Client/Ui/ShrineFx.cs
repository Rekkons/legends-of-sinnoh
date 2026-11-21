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
    /// 사당·균열의 보드 쪽 연출 층(M5 「사당·균열 화면」 — 보드 HUD가 하나 갖는다, 마우스를 받지 않는다):
    /// 전설 이벤트(33행 — 하늘이 어두워짐(<see cref="DarkShown"/> — HUD의 어둠 판이 그린다) · 착지 고리 · 좌석 칩 위로 그 색 규토리 비와 「+n」) · 신 강림(34행 — 어둠 · 번개 · 착지 고리 · 그 신 추종자 칩의 「이동력 +1」) ·
    /// 나눠 받는 축복(32행 — 사당 판에서 그 좌석 칩으로 가는 빛). 피규어가 내려앉는 것과 카메라는 <see cref="GameFlow"/>가 보드에서 한다.
    /// 박자는 <c>Sinnoh.Fx</c> <see cref="LegendBeats"/>·<see cref="DescendBeats"/>·<see cref="BlessBeats"/>, 시간은 대기열 시각만(옮기기·늘이기·흐리기 — FxUi).
    /// 요소 이름: <c>board-hud-shrine-fx</c>·<c>-flash</c>·<c>-ring</c>·<c>-rain-&lt;i&gt;</c>·<c>-plus-&lt;i&gt;</c>·<c>-beam</c>·<c>-move-&lt;i&gt;</c>.
    /// </summary>
    public sealed class ShrineFx
    {
        /// <summary>규토리가 떨어지기 시작하는 높이 · 칩 가운데에서 내려앉는 자리 · 「+n」·「이동력 +1」 꼬리표가 칩 가운데에서 올라선 거리(720 벌 px — 꼬리표는 칩 위에 얹힌다).</summary>
        public const float RainFromPx = 60f, RainLandPx = 14f, TagUpPx = 9f;

        readonly UiStage stage;
        readonly GameFlow flow;
        readonly VisualElement flash, ring, beam;
        readonly List<VisualElement> rain = new List<VisualElement>();
        readonly List<Label> plus = new List<Label>();
        readonly List<Label> move = new List<Label>();

        public readonly VisualElement Element;

        /// <summary>지금 보이는 것(확인용): 어둠(0 ~ 1) · 착지 고리 · 떨어지는 규토리 수 · 「+n」 수 · 빛줄기 · 「이동력 +1」 수.</summary>
        public float DarkShown { get; private set; }
        public bool RingShown { get; private set; }
        public int RainShown { get; private set; }
        public int PlusShown { get; private set; }
        public bool BeamShown { get; private set; }
        public int MoveShown { get; private set; }

        public ShrineFx(UiStage stage, GameFlow flow)
        {
            this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            var inv = CultureInfo.InvariantCulture;
            Element = new VisualElement { name = "board-hud-shrine-fx", pickingMode = PickingMode.Ignore };
            Element.AddToClassList("sn-fate-fx");
            flash = Box("board-hud-shrine-flash", "sn-shrine-flash");
            ring = Box("board-hud-shrine-ring", "sn-shrine-ring");
            beam = Box("board-hud-shrine-beam", "sn-shrine-beam");
            motes = new ScatterMotes(Element, "board-hud-shrine-mote-");
            sky = Box("board-hud-shrine-sky", "sn-shrine-sky");
            for (int i = 0; i < SeatId.MaxSeats; i++)
            {
                rain.Add(Box("board-hud-shrine-rain-" + i.ToString(inv), "sn-hud-drop"));
                plus.Add(Tag("board-hud-shrine-plus-" + i.ToString(inv), KitBadgeKind.Ok));
                move.Add(Tag("board-hud-shrine-move-" + i.ToString(inv), KitBadgeKind.Gold));
            }
        }

        VisualElement Box(string name, string cls)
        {
            var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            Element.Add(e);
            FxUi.Fade(e, 0f);
            return e;
        }

        Label Tag(string name, KitBadgeKind kind)
        {
            var l = Kit.Badge("", kind, Kit.F2);
            l.name = name;
            l.pickingMode = PickingMode.Ignore;
            l.AddToClassList("sn-shrine-tag");
            Element.Add(l);
            FxUi.Fade(l, 0f);
            return l;
        }

        readonly ScatterMotes motes;
        readonly VisualElement sky;

        /// <summary>판 위 사당에 선 공물 빛기둥의 단계(1 · 2 · 3 — 31행 P36, 없으면 0) · 그 높이 배율(0 ~ 1 — 3단계가 다 섰을 때 1). 확인용.</summary>
        public int SkyShown { get; private set; }
        public float SkyHeight { get; private set; }

        /// <summary>빛으로 흩어지는 알갱이 수(33행 달빛공터의 바꿔치기 — P37, 확인용).</summary>
        public int MotesShown => motes.Shown;

        readonly Vector2?[] seatAt = new Vector2?[SeatId.MaxSeats];

        // 좌석 칩의 가운데: 칩은 HUD가 다시 그릴 때마다 새로 만들어져 그 프레임에는 배치 전이다 — 그때는 지난 자리를 쓴다
        Vector2? SeatCentre(Func<int, VisualElement> seatChip, int seat)
        {
            if (seat < 0 || seat >= seatAt.Length) return null;
            var at = Centre(seatChip(seat));
            if (at != null) seatAt[seat] = at;
            return seatAt[seat];
        }

        // 요소의 가운데(이 층의 좌표 — 배치 전이면 null)
        Vector2? Centre(VisualElement e)
        {
            if (e == null) return null;
            var box = e.worldBound;
            if (float.IsNaN(box.width) || box.width <= 0f) return null;
            return Element.WorldToLocal(box.center);
        }

        /// <summary>그 순간을 그린다. seatChip(좌석) = 그 좌석 칩 요소, panelCard = 사당 판의 카드 요소(빛줄기가 떠나는 곳 — 없으면 null).</summary>
        public void Show(FxSequencer seq, Func<int, VisualElement> seatChip, VisualElement panelCard)
        {
            var inv = CultureInfo.InvariantCulture;
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            var legendStep = seq != null ? ShrineRows.Playing(seq, ShrineRows.Legend, out int legendMs) : null;
            var descendStep = seq != null ? ShrineRows.Playing(seq, ShrineRows.Descend, out int descendMs) : null;
            var legend = LegendBeats.Event(legendStep);
            var god = DescendBeats.Event(descendStep);
            var pose = default(DescendPose);
            pose.Ring = -1f;
            int cell = -1;
            if (legend != null)
            {
                pose = LegendBeats.At(seq.LocalMs - legendStep.StartMs, legendStep.Ms, legend.Placed);
                cell = legend.Cell;
            }
            else if (god != null)
            {
                pose = DescendBeats.At(seq.LocalMs - descendStep.StartMs, descendStep.Ms, god.Stayed);
                cell = god.Cell;
            }
            DarkShown = pose.Dark;
            FxUi.Fade(flash, pose.Flash);
            var at = default(Vector2);
            RingShown = pose.Ring >= 0f && pose.Ring < 1f && flow.CellPoint(cell, out at);
            if (RingShown)
            {
                FxUi.Move(ring, at.x, at.y);
                float s = 0.4f + 1.4f * pose.Ring;
                FxUi.Scale(ring, s, s * 0.5f);
                FxUi.Fade(ring, 1f - pose.Ring);
            }
            else FxUi.Fade(ring, 0f);
            // 달빛공터의 바꿔치기(P37): 있던 전설이 빛으로 흩어진다 — 그 사당 위의 빛 알갱이(피규어는 GameFlow.SyncFigures가 줄인다)
            var gladeAt = default(Vector2);
            bool scatter = legend != null && legend.ReplacedSpecies >= 0 && flow.CellPoint(legend.Cell, out gladeAt);
            motes.Show(scatter ? seq.LocalMs - legendStep.StartMs - LegendBeats.ScatterFromMs : -1, gladeAt.x, gladeAt.y - ScatterMotes.AbovePx * unit, unit);

            // 규토리 비(33행): 좌석(Changes 순서)마다 어긋나 칩 위로 떨어지고 「+n」
            RainShown = PlusShown = 0;
            var gains = legend != null ? LegendBeats.Gains(seq.Current) : null;
            for (int i = 0; i < rain.Count; i++)
            {
                var c = gains != null && i < gains.Changes.Length ? gains.Changes[i] : null;
                var chip = c != null ? SeatCentre(seatChip, c.Seat) : null;
                if (c == null || chip == null)
                {
                    FxUi.Fade(rain[i], 0f);
                    FxUi.Fade(plus[i], 0f);
                    continue;
                }
                float h = LegendBeats.Rain(seq.LocalMs - legendStep.StartMs, i, out float p);
                if (h >= 0f)
                {
                    var tex = GenAssets.Load<Texture2D>(EncounterScreen.ApricornKey((ApricornColor)c.Key));
                    if (tex != null) rain[i].style.backgroundImage = new StyleBackground(tex);
                    FxUi.Move(rain[i], chip.Value.x, Mathf.Round(chip.Value.y + (RainLandPx - h * RainFromPx) * unit));
                    FxUi.Fade(rain[i], 1f);
                    RainShown++;
                }
                else FxUi.Fade(rain[i], 0f);
                plus[i].text = WorkUi.Signed(c.After - c.Before);
                FxUi.Move(plus[i], chip.Value.x, Mathf.Round(chip.Value.y - TagUpPx * unit));
                FxUi.Fade(plus[i], p);
                if (p > 0f) PlusShown++;
            }

            for (int i = 0; i < seatAt.Length; i++) SeatCentre(seatChip, i);   // 자리를 미리 익혀 둔다(칩이 다시 지어지는 프레임에 쓴다)
            // 신 강림의 착지(34행): 그 신 추종자의 좌석 칩 아래 「이동력 +1」
            MoveShown = 0;
            var view = flow.Client != null ? flow.Client.View : null;
            float sparkPlus = 0f;
            if (god != null) DescendBeats.Spark(seq.LocalMs - descendStep.StartMs, out sparkPlus);
            for (int i = 0; i < move.Count; i++)
            {
                var chip = god != null && view != null && i < view.Seats.Length && (int)view.Seats[i].Follower == god.God && sparkPlus > 0f ? SeatCentre(seatChip, i) : null;
                if (chip == null)
                {
                    FxUi.Fade(move[i], 0f);
                    continue;
                }
                move[i].text = stage.T("screen.shrine.move_bonus");
                FxUi.Move(move[i], chip.Value.x, Mathf.Round(chip.Value.y - TagUpPx * unit));
                FxUi.Fade(move[i], sparkPlus);
                MoveShown++;
            }

            // 나눠 받는 축복(32행 — 4인 이상): 사당 판에서 그 좌석 칩으로 가는 빛
            var blessStep = seq != null ? ShrineRows.Playing(seq, ShrineRows.Blessing, out int blessMs) : null;
            var bless = ShrineRows.First<ShrineBlessing>(blessStep);
            BeamShown = false;
            if (bless != null && bless.Seat != bless.From)
            {
                var from = Centre(panelCard);
                var to = SeatCentre(seatChip, bless.Seat);
                float t = BlessBeats.Beam(seq.LocalMs - blessStep.StartMs);
                if (from != null && to != null && t > 0f && t < 1f)
                {
                    var p = Vector2.Lerp(from.Value, to.Value, t);
                    FxUi.Move(beam, p.x, p.y);
                    FxUi.Fade(beam, 1f);
                    BeamShown = true;
                }
            }
            if (!BeamShown) FxUi.Fade(beam, 0f);

            // 공물(31행 — P36): 바친 좌석이 선 사당 칸 위로 빛기둥 — 단계(1 · 2 · 3)마다 높이와 색이 다르다(사당 판 안의 빛기둥과 같은 박자 · 남 좌석의 공물도 보인다)
            var offerStep = seq != null ? ShrineRows.Playing(seq, ShrineRows.Offer, out int offerMs) : null;
            var offer = ShrineRows.First<ShrineOffered>(offerStep);
            SkyShown = 0;
            SkyHeight = 0f;
            var skyAt = default(Vector2);
            float light = 0f;
            float rise = offer != null ? OfferBeats.Pillar(seq.LocalMs - offerStep.StartMs, out light) : 0f;
            if (light > 0f && view != null && offer.Seat >= 0 && offer.Seat < view.Seats.Length && flow.CellPoint(view.Seats[offer.Seat].Cell, out skyAt))
            {
                int tier = Mathf.Clamp(offer.Tier, 1, 3);
                for (int k = 1; k <= 3; k++) sky.EnableInClassList("sn-shrine-sky-" + k.ToString(inv), k == tier);
                SkyShown = tier;
                SkyHeight = rise * OfferBeats.BoardHeight(tier);
                FxUi.Move(sky, skyAt.x, skyAt.y);
                FxUi.Scale(sky, 1f, Mathf.Max(0.02f, SkyHeight));
                FxUi.Fade(sky, light * 0.85f);
            }
            else FxUi.Fade(sky, 0f);
        }
    }

    /// <summary>
    /// 빛으로 흩어지는 알갱이(M9 다듬기 P28 신 격파 · P37 달빛공터 — 박자 <see cref="ScatterBeats"/>): 판 위 피규어 자리에서 퍼지며 떠오르는 작은 빛 <see cref="ScatterBeats.Motes"/>개.
    /// 맵 결과 층(<see cref="ResultFx"/>)과 사당 층(<see cref="ShrineFx"/>)이 하나씩 갖는다(마우스 안 받음). 요소 이름: 준 머리 + 차례.
    /// </summary>
    public sealed class ScatterMotes
    {
        /// <summary>720 화면 px: 퍼지는 반지름 · 칸 윗면에서 알갱이 가운데까지의 높이.</summary>
        public const float SpreadPx = 44f, AbovePx = 26f;

        readonly List<VisualElement> motes = new List<VisualElement>();

        /// <summary>지금 보이는 알갱이 수(확인용).</summary>
        public int Shown { get; private set; }

        public ScatterMotes(VisualElement layer, string name)
        {
            for (int i = 0; i < ScatterBeats.Motes; i++)
            {
                var e = new VisualElement { name = name + i.ToString(CultureInfo.InvariantCulture), pickingMode = PickingMode.Ignore };
                e.AddToClassList("sn-mote");
                layer.Add(e);
                FxUi.Fade(e, 0f);
                motes.Add(e);
            }
        }

        /// <summary>흩어지기 시작한 뒤 ms(밖이면 모두 숨긴다) · 가운데(층의 좌표) · 등급 배율.</summary>
        public void Show(int ms, float x, float y, float unit)
        {
            Shown = 0;
            for (int i = 0; i < motes.Count; i++)
            {
                float show = ScatterBeats.Mote(ms, i, out float dx, out float dy);   // 시작 전(ms < 0) · 끝난 뒤는 0
                if (show <= 0f) { FxUi.Fade(motes[i], 0f); continue; }
                FxUi.Move(motes[i], x + dx * SpreadPx * unit, y - dy * SpreadPx * unit);
                FxUi.Fade(motes[i], show);
                Shown++;
            }
        }
    }
}
