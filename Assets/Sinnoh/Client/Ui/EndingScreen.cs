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
    /// S62 종막 선택 · S63 배신자 순서(04 §2, §8-8 95·96행 — M5 「막·종막 화면」): 태홍을 이긴 좌석에게 영웅·배신자를 묻는 창. HUD 위에 열린다(<see cref="GameFlow.FinaleStage"/>).
    /// <list type="bullet">
    /// <item>두 카드(영웅 · 배신자)가 나란히 놓이고, 카드의 [선택]으로 답한다(남이 고르는 판이면 「○○이(가) 고르는 중입니다.」만). 효과 설명은 카드의 마우스 오버.</item>
    /// <item>고르면(95행) 고르지 않은 카드가 가장자리부터 타 버리고, 배신자면 화면이 붉게 물든다(<see cref="EndingBeats"/>). 길이는 방장과 같은 2800(전원 차단).</item>
    /// <item>배신자의 상대가 둘 이상이면 순서를 묻는다(S63): 상대 좌석을 배틀할 순서대로 누르고 [확정]. 고른 순서는 칩 줄로 하나씩 놓인다(96행 — <see cref="OrderBeats"/>).</item>
    /// </list>
    /// 카드 그림은 원작 종막 카드 뒷면의 금빛 문양(<see cref="GenAssets.EndingArt"/> — 원작 앞면은 영웅·배신자 두 칸이 든 글뿐인 한 장이라, 결정 80 L1 나), 배신자 쪽은 붉게 물들인다. 요소 이름: <c>ending</c>·<c>ending-red</c>·<c>ending-title</c>·<c>ending-ask</c>·
    /// <c>ending-card-&lt;0|1&gt;</c>·<c>ending-art-&lt;0|1&gt;</c>·<c>ending-burn-&lt;0|1&gt;</c>·<c>ending-pick-&lt;0|1&gt;</c>·<c>ending-order</c>·<c>ending-order-seat-&lt;좌석&gt;</c>·<c>ending-order-reset</c>·
    /// <c>ending-order-confirm</c>·<c>ending-chips</c>·<c>ending-chip-&lt;i&gt;</c>.
    /// </summary>
    public sealed class EndingScreen : IUiScreen
    {
        /// <summary>타 들어가는 카드가 사라지기 시작하는 탄 정도.</summary>
        public const float BurnFadeFrom = 0.4f;

        readonly GameFlow flow;
        readonly List<int> picked = new List<int>();
        UiStage stage;
        VisualElement page, red, body, cards, order, chips;
        readonly VisualElement[] card = new VisualElement[2], burn = new VisualElement[2];
        int paintedVersion = -1, paintedMode = -1;

        public EndingScreen(GameFlow flow) => this.flow = flow ?? throw new ArgumentNullException(nameof(flow));

        public string Name => "ending";

        /// <summary>내가 영웅·배신자를 고를 차례다(S62).</summary>
        public bool Asking => Choice(ChoiceSub.EndingChoice) != null;

        /// <summary>내가 배신자의 순서를 고를 차례다(S63).</summary>
        public bool Ordering => Choice(ChoiceSub.TraitorOrder) != null;

        /// <summary>지금까지 고른 순서(좌석) — 확인용.</summary>
        public IReadOnlyList<int> Picked => picked;

        /// <summary>지금 보이는 모습(확인용): 고르지 않은 카드가 탄 정도 · 붉은 물듦 · 놓인 순서 칩 수 · 고른 길(−1 = 아직, 0 영웅, 1 배신자).</summary>
        public float Burn { get; private set; }
        public float Red { get; private set; }
        public int ChipsShown { get; private set; }
        public int Chosen { get; private set; } = -1;

        /// <summary>그림이 그려진 카드 수(원작 종막 문양 — 결정 80 L1 나) — 확인용.</summary>
        public int ArtShown { get; private set; }

        PendingChoice Choice(ChoiceSub sub)
        {
            var c = flow.Client != null ? flow.Client.Choice(ChoiceKind.Ending) : null;
            return c != null && c.Sub == sub ? c : null;
        }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            page = new VisualElement { pickingMode = PickingMode.Ignore };   // 위 띠의 [메뉴] 등 HUD 단추는 어둠 너머로도 눌린다(T35)
            Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "ending-scrim", "sn-act-scrim");
            red = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "ending-red", "sn-ending-red");
            FxUi.Fade(red, 0f);
            body = Add(page, new VisualElement(), "ending-body", "sn-ending-body");
            // 순서 칩 줄(96행)은 몸통 밖 — 고른 뒤 몸통이 걷혀도 칩은 남는다
            chips = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "ending-chips", "sn-ending-chips");
            paintedVersion = paintedMode = -1;
            picked.Clear();
            Refresh();
            if (flow.Fx != null) ShowFx(flow.Fx.Sequencer);
            return page;
        }

        public void Close()
        {
            stage = null;
            page = null;
        }

        // 0 = 고르기(나 · 남) · 1 = 순서 고르기 · 2 = 고른 뒤(연출)
        int Mode() => Ordering ? 1 : Asking || !Played() ? 0 : 2;

        // 95행이 이미 지나갔나(고른 답이 대기열에 있었다): 고른 뒤 모습으로 그린다
        bool Played() => Chosen >= 0;

        /// <summary>보기가 바뀌었다(입력이 열림 · 다음 물음): 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || body == null || flow.Client == null || flow.Client.View == null) return;
            int mode = Mode();
            if (mode == paintedMode && paintedVersion == flow.Client.Version && !openChanged()) return;
            paintedMode = mode;
            paintedVersion = flow.Client.Version;
            wasOpen = flow.BoardOpen;
            body.Clear();
            ArtShown = 0;
            var view = flow.Client.View;
            int chooser = view.Hos != null && view.Hos.Traitor >= 0 ? view.Hos.Traitor : view.Hos != null ? view.Hos.FirstSeat[3] : -1;
            Add(body, Kit.Badge(stage.T("screen.ending.title"), KitBadgeKind.Silver, Kit.F2), "ending-title", null);
            if (mode == 1) { PaintOrder(); return; }
            string ask = Asking ? stage.T("screen.ending.ask") : mode == 0 && chooser >= 0 ? stage.T("screen.ending.wait").Replace("{name}", flow.SeatName(chooser)) : "";
            Add(body, Kit.Text(KoJosa.Fix(ask), Kit.F2), "ending-ask", "sn-ending-ask");
            cards = Add(body, new VisualElement(), "ending-cards", "sn-ending-cards");
            for (int i = 0; i < 2; i++) PaintCard(i);
        }

        bool wasOpen;
        bool openChanged() => flow.BoardOpen != wasOpen;

        void PaintCard(int i)
        {
            string key = i == 0 ? "hero" : "traitor";
            var c = Add(cards, Kit.Panel(stage.Skin), "ending-card-" + i.ToString(CultureInfo.InvariantCulture), "sn-ending-card");
            card[i] = c;
            Add(c, new VisualElement(), null, i == 0 ? "sn-ending-band-hero" : "sn-ending-band-traitor");
            // 원작 종막 카드의 그림(뒷면 문양 — 원작 앞면은 글뿐인 한 장이다, 결정 80 L1 나): 배신자 쪽은 붉게 물들인다(USS)
            var tex = GenAssets.Load<Texture2D>(GenAssets.EndingArt);
            if (tex != null)
            {
                var art = Add(c, new VisualElement { pickingMode = PickingMode.Ignore }, "ending-art-" + i.ToString(CultureInfo.InvariantCulture), "sn-ending-art");
                if (i == 1) art.AddToClassList("sn-ending-art-traitor");
                art.style.backgroundImage = new StyleBackground(tex);
                ArtShown++;
            }
            var name = Add(c, Kit.Text(stage.T("screen.ending." + key), Kit.F3), null, "kit-title");
            name.AddToClassList("sn-ending-name");
            Add(c, Kit.Text(stage.T("screen.ending." + key + ".text"), Kit.F2), null, "sn-ending-text");
            stage.Tips.Hook(c, stage.T("screen.ending." + key), stage.T("screen.ending." + key + ".tip"), TipSide.Below, reading: true);
            if (Asking)
            {
                var pick = Add(c, Kit.Button(stage.T("screen.ending.pick"), i == 0 ? KitButtonKind.Main : KitButtonKind.End, () => Pick(i)), "ending-pick-" + i.ToString(CultureInfo.InvariantCulture), "sn-ending-pick");
                pick.SetEnabled(flow.BoardOpen);
            }
            burn[i] = Add(c, new VisualElement { pickingMode = PickingMode.Ignore }, "ending-burn-" + i.ToString(CultureInfo.InvariantCulture), "sn-ending-burn");
            FxUi.Fade(burn[i], 0f);
        }

        // S63: 상대 좌석을 배틀할 순서대로 누른다 — 누른 좌석에 「n번째」 배지, 다 고르면 [확정]
        void PaintOrder()
        {
            var c = Choice(ChoiceSub.TraitorOrder);
            Add(body, Kit.Text(stage.T("screen.ending.order.ask"), Kit.F2), "ending-ask", "sn-ending-ask");
            order = Add(body, Kit.Panel(stage.Skin), "ending-order", "sn-ending-order");
            Add(order, Kit.Text(stage.T("screen.ending.order.title"), Kit.FTitle), null, "kit-title");
            var row = Add(order, new VisualElement(), null, "sn-ending-seats");
            picked.RemoveAll(s => c == null || !c.Options.Contains(s));
            if (c != null)
                foreach (int seat in c.Options)
                {
                    int s = seat;
                    var chip = Add(row, new VisualElement(), "ending-order-seat-" + s.ToString(CultureInfo.InvariantCulture), "sn-ending-seat");
                    var face = FaceBox.Make();
                    chip.Add(face);
                    FaceBox.Paint(face, s, flow.SeatColor(s), flow.Protagonist(s));
                    Add(chip, Kit.Text(flow.SeatName(s), Kit.F2), null, "sn-hud-seat-name");
                    int at = picked.IndexOf(s);
                    if (at >= 0)
                    {
                        chip.AddToClassList("sn-ending-seat-picked");
                        Add(chip, Kit.Badge(stage.T("screen.ending.order.nth").Replace("{n}", (at + 1).ToString(CultureInfo.InvariantCulture)), KitBadgeKind.Gold, Kit.F2), null, null);
                    }
                    stage.OnTap(chip, SoundKeys.Decide, e => PickSeat(s), () => flow.BoardOpen && !picked.Contains(s));
                }
            var foot = Add(order, new VisualElement(), null, "sn-ending-foot");
            var reset = Add(foot, Kit.Button(stage.T("screen.ending.order.reset"), KitButtonKind.Normal, () => ResetOrder()), "ending-order-reset", null);
            reset.SetEnabled(flow.BoardOpen && picked.Count > 0);
            var ok = Add(foot, Kit.Button(stage.T("screen.ending.order.confirm"), KitButtonKind.Main, () => ConfirmOrder()), "ending-order-confirm", null);
            ok.SetEnabled(flow.BoardOpen && c != null && picked.Count == c.Options.Count);
        }

        /// <summary>카드의 [선택]: 0 = 영웅 · 1 = 배신자. 받아들여지면 true.</summary>
        public bool Pick(int i)
        {
            var c = Choice(ChoiceSub.EndingChoice);
            if (c == null || !flow.BoardOpen) return false;
            var answer = flow.Client.Legal(c).Find(a => a.Kind == AnswerKind.Pick && a.Target == i);
            if (answer == null) return false;
            stage?.Tips.Hide();
            return flow.Answer(c, answer).Accepted;
        }

        /// <summary>S63: 그 좌석을 다음 순서로 고른다(이미 골랐으면 false).</summary>
        public bool PickSeat(int seat)
        {
            var c = Choice(ChoiceSub.TraitorOrder);
            if (c == null || !flow.BoardOpen || !c.Options.Contains(seat) || picked.Contains(seat)) return false;
            picked.Add(seat);
            paintedVersion = -1;
            Refresh();
            return true;
        }

        public void ResetOrder()
        {
            picked.Clear();
            paintedVersion = -1;
            Refresh();
        }

        /// <summary>S63의 [확정]: 고른 순서를 낸다. 다 고르지 않았거나 거절되면 false.</summary>
        public bool ConfirmOrder()
        {
            var c = Choice(ChoiceSub.TraitorOrder);
            if (c == null || !flow.BoardOpen || picked.Count != c.Options.Count) return false;
            var answer = ChoiceAnswer.PickMany(new List<int>(picked));
            stage?.Tips.Hide();
            bool ok = flow.Answer(c, answer).Accepted;
            if (ok) picked.Clear();
            return ok;
        }

        /// <summary>대기열의 지금 시각으로 고른 뒤(95행)·순서 공개(96행)의 모습을 그린다.</summary>
        public void ShowFx(FxSequencer seq)
        {
            if (stage == null || seq == null) return;
            if (openChanged()) { paintedVersion = -1; Refresh(); }
            var cur = seq.Current;
            var step = EndingBeats.StepOf(cur);
            var chosen = EndingBeats.Chosen(step);
            if (chosen != null)
            {
                bool was = Chosen >= 0;
                Chosen = chosen.Traitor ? 1 : 0;
                if (!was) { paintedVersion = -1; Refresh(); }
                int t = seq.LocalMs - step.StartMs;
                Pose(t < 0 ? EndingBeats.Asking : EndingBeats.At(t, step.Ms, chosen.Traitor));
            }
            else if (Chosen >= 0) Pose(EndingBeats.At(int.MaxValue / 2, 1, Chosen == 1));
            else Pose(EndingBeats.Asking);
            ShowChips(seq);
        }

        void Pose(EndingPose p)
        {
            int other = Chosen == 0 ? 1 : 0;
            Burn = Chosen >= 0 ? p.Burn : 0f;
            Red = p.Red;
            for (int i = 0; i < 2; i++)
            {
                if (card[i] == null || card[i].parent == null) continue;
                bool burning = Chosen >= 0 && i == other;
                FxUi.Fade(burn[i], burning ? Mathf.Clamp01(p.Burn * 1.5f) : 0f);
                FxUi.Fade(card[i], burning ? 1f - Mathf.Clamp01((p.Burn - BurnFadeFrom) / (1f - BurnFadeFrom)) : 1f);
                card[i].EnableInClassList("sn-ending-chosen", Chosen == i);
            }
            FxUi.Fade(red, p.Red);   // 가장 짙은 색은 USS(sn-ending-red)
            // 순서를 고르는 동안·순서 공개 동안은 걷지 않는다(배신자의 물음이 이어진다)
            FxUi.Fade(body, Ordering ? 1f : 1f - p.Fade);
        }

        // 96행: 고른 순서가 칩 줄로 하나씩 놓인다(배신자의 얼굴 → 상대들)
        void ShowChips(FxSequencer seq)
        {
            ChipsShown = 0;
            if (chips == null) return;
            var step = OrderBeats.StepOf(seq.Current);
            var set = OrderBeats.Order(step);
            if (set == null || set.Seats == null || set.Seats.Length == 0)
            {
                if (chips.childCount > 0 && step == null && !Ordering) return;   // 지나간 줄은 그대로 둔다
                chips.Clear();
                return;
            }
            int n = set.Seats.Length, t = seq.LocalMs - step.StartMs;
            if (chips.childCount != n)
            {
                chips.Clear();
                for (int i = 0; i < n; i++)
                {
                    int s = set.Seats[i];
                    var chip = Add(chips, new VisualElement { pickingMode = PickingMode.Ignore }, "ending-chip-" + i.ToString(CultureInfo.InvariantCulture), "sn-ending-chip");
                    Add(chip, Kit.Badge(stage.T("screen.ending.order.nth").Replace("{n}", (i + 1).ToString(CultureInfo.InvariantCulture)), KitBadgeKind.Gold, Kit.F2), null, null);
                    var face = FaceBox.Make();
                    face.pickingMode = PickingMode.Ignore;
                    chip.Add(face);
                    FaceBox.Paint(face, s, flow.SeatColor(s), flow.Protagonist(s));
                    Add(chip, Kit.Text(flow.SeatName(s), Kit.F2), null, "sn-hud-seat-name");
                }
            }
            for (int i = 0; i < n; i++)
            {
                float a = t < 0 ? 0f : OrderBeats.Chip(t, step.Ms, i, n);
                FxUi.Fade(chips[i], a);
                FxUi.Move(chips[i], 0f, (1f - a) * -OrderDropPx);
                if (a > 0f) ChipsShown++;
            }
        }

        /// <summary>순서 칩이 떨어지는 거리(720 벌 px).</summary>
        public const float OrderDropPx = 10f;

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
