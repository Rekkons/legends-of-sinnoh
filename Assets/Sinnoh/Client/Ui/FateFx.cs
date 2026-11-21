using System;
using System.Collections.Generic;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 운명의 변화 카드 연출 층(M5 「운명·응답 화면」 — 보드 HUD와 배틀 화면이 하나씩 갖는다, 마우스를 받지 않는다):
    /// 받기(7·42행 — 뒷면이 날아와 내 손에서만 앞면으로) · 귀환 회복 끝의 운명 1장(8행) · 6장째 버리기(42행 — 떠올라 떨다 찢기듯 사라짐) · 카드 내기(45행 — 가운데로 튀어나와 지나감, 3장은 셋이 합쳐짐).
    /// 박자는 <c>Sinnoh.Fx</c> <see cref="FateDealBeats"/>·<see cref="FateDiscardBeats"/>·<see cref="FatePassBeats"/>, 시간은 대기열 시각만(옮기기·늘이기·흐리기 — FxUi).
    /// 요소 이름: <c>&lt;접두&gt;-fate-fx</c>·<c>-fate-fly-&lt;i&gt;</c>·<c>-fate-drop</c>·<c>-fate-pass-&lt;i&gt;</c>.
    /// </summary>
    public sealed class FateFx
    {
        public const int MaxFly = 6, PassCards = 3;

        /// <summary>3장이 벌어진 반지름 · 버린 카드가 떠오르는 높이 · 떨림 폭(720 벌 px).</summary>
        public const float SpreadPx = 46f, DropRisePx = 40f, ShakePx = 3f;

        readonly UiStage stage;
        readonly List<VisualElement> fly = new List<VisualElement>();
        readonly List<VisualElement> pass = new List<VisualElement>();
        readonly VisualElement drop, glow;
        Texture2D back;

        public readonly VisualElement Element;

        /// <summary>지금 보이는 날아오는 카드 수 · 버리는 카드 · 지나가는 카드(확인용).</summary>
        public int FlyShown { get; private set; }
        public bool DropShown { get; private set; }
        public bool PassShown { get; private set; }

        public FateFx(UiStage stage, string prefix)
        {
            this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
            Element = new VisualElement { name = prefix + "-fate-fx", pickingMode = PickingMode.Ignore };
            Element.AddToClassList("sn-fate-fx");
            back = FateCards.Back();
            for (int i = 0; i < MaxFly; i++)
            {
                var card = Img(prefix + "-fate-fly-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "sn-fate-fly");
                fly.Add(card);
            }
            drop = Img(prefix + "-fate-drop", "sn-fate-fly");
            glow = new VisualElement { name = prefix + "-fate-glow", pickingMode = PickingMode.Ignore };
            glow.AddToClassList("sn-fate-glow");
            Element.Add(glow);
            FxUi.Fade(glow, 0f);
            for (int i = 0; i < PassCards; i++) pass.Add(PassCard(prefix + "-fate-pass-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        Image Img(string name, string cls)
        {
            var img = new Image { name = name, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.AddToClassList(cls);
            Element.Add(img);
            FxUi.Fade(img, 0f);
            return img;
        }

        // 지나가는 카드: 우리 카드 꼴(그림 부분 + 이름 + 큰 글씨 — 원작 카드의 영어 글씨는 쓰지 않는다)
        VisualElement PassCard(string name)
        {
            var box = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            box.AddToClassList("sn-fate-pass");
            var art = new Image { scaleMode = ScaleMode.ScaleToFit, uv = FateCards.ArtUv, pickingMode = PickingMode.Ignore };
            art.AddToClassList("sn-fate-pass-art");
            box.Add(art);
            foreach (string cls in new[] { "sn-fate-pass-name", "sn-fate-pass-line" })
            {
                var label = Rekkon.UiKit.Kit.Text("", cls == "sn-fate-pass-line" ? Rekkon.UiKit.Kit.F3 : Rekkon.UiKit.Kit.F2);
                label.pickingMode = PickingMode.Ignore;
                label.AddToClassList(cls);
                box.Add(label);
            }
            Element.Add(box);
            FxUi.Fade(box, 0f);
            return box;
        }

        void PaintPass(VisualElement box, FateCard card)
        {
            box.Q<Image>().image = FateCards.Art(card);
            box.Q<Label>(className: "sn-fate-pass-name").text = stage.T(FateCards.NameKey(card));
            box.Q<Label>(className: "sn-fate-pass-line").text = stage.T(FateCards.NameKey(card) + ".text.1");
        }

        void Hide(VisualElement e) => FxUi.Fade(e, 0f);

        /// <summary>
        /// 지금 항목의 운명 단계를 그린다. mySeat = 내 좌석(앞면은 내 카드만), hand = 내 손(받은 카드가 닿는 점 — 층 좌표), seatAt(좌석) = 남 좌석이 받는 점, from = 받기가 시작하는 점(떨어진 별 · 그 밖).
        /// </summary>
        public void Show(FxSequencer seq, int mySeat, Ctx ctx, Vector2 hand, Func<int, Vector2?> seatAt, Vector2 starFrom, Vector2 drawFrom)
        {
            FlyShown = 0;
            DropShown = PassShown = false;
            var entry = seq != null ? seq.Current : null;
            int local = seq != null ? seq.LocalMs : 0;
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            float w = Element.layout.width, h = Element.layout.height;
            if (float.IsNaN(w) || w <= 0f) { w = 1280f; h = 720f; }

            // 받기(7·8·42행)
            var deal = FateRows.DealStep(entry);
            var dealt = FateRows.First<FateDealt>(deal);
            int n = 0;
            if (deal != null && dealt != null && dealt.PerSeat != null && local >= deal.StartMs)
            {
                int start = FateRows.DealFrom(deal), t = local - deal.StartMs - start, total = deal.Ms - start;
                var from = deal.Row == FateRows.Star ? starFrom : drawFrom;
                for (int i = 0; i < dealt.PerSeat.Length; i++)
                {
                    var d = dealt.PerSeat[i];
                    int cards = Math.Max(0, d.After - d.Before);
                    if (cards == 0) continue;
                    bool mine = d.Seat == mySeat;
                    Vector2 to = mine ? hand : seatAt(d.Seat) ?? hand;
                    for (int c = 0; c < cards && n < fly.Count; c++)
                    {
                        var pose = FateDealBeats.At(t, total, i + c, mine);
                        var card = (Image)fly[n++];
                        if (pose.Show <= 0f) { Hide(card); continue; }
                        int key = mine && d.CardKeys != null && c < d.CardKeys.Length ? d.CardKeys[c] : -1;
                        bool face = pose.Face >= 0.5f && key >= 0;
                        card.image = face ? FateCards.Art(FateRules.KindOf(ctx, key)) : back;
                        card.uv = face ? FateCards.ArtUv : new Rect(0f, 0f, 1f, 1f);
                        var at = Vector2.Lerp(from, to, pose.T) + new Vector2(0f, -pose.Lift * 40f * unit);
                        FxUi.Move(card, at.x, at.y);
                        float flip = Mathf.Abs(Mathf.Cos(pose.Face * Mathf.PI));
                        FxUi.Scale(card, pose.Scale * Mathf.Max(0.05f, flip), pose.Scale);
                        FxUi.Fade(card, 1f);
                        FlyShown++;
                    }
                }
            }
            for (int i = n; i < fly.Count; i++) Hide(fly[i]);

            // 6장째 버리기(42행)
            var gone = entry != null ? entry.Step(FateRows.Discard) : null;
            var discarded = FateRows.First<FateDiscarded>(gone);
            if (gone != null && discarded != null && local >= gone.StartMs && local < gone.EndMs)
            {
                var pose = FateDiscardBeats.At(local - gone.StartMs, gone.Ms);
                ((Image)drop).image = FateCards.Art(FateRules.KindOf(ctx, discarded.CardKey));
                ((Image)drop).uv = FateCards.ArtUv;
                bool mine = discarded.Seat == mySeat;
                Vector2 at = mine ? hand : seatAt(discarded.Seat) ?? hand;
                FxUi.Move(drop, at.x + pose.T * ShakePx * unit, at.y - pose.Lift * DropRisePx * unit);
                FxUi.Scale(drop, Mathf.Max(0.02f, 1f - pose.Tear), 1f);
                FxUi.Fade(drop, 1f - pose.Tear * 0.6f);
                DropShown = true;
            }
            else Hide(drop);

            // 카드 내기(45행 — 카드 지나가기 P11)
            var play = entry != null ? entry.Step(FateRows.Play) : null;
            var played = FateRows.First<FatePlayed>(play);
            if (play != null && played != null && local >= play.StartMs && local < play.EndMs)
            {
                var pose = FatePassBeats.At(local - play.StartMs, play.Ms, played.Play3);
                int shown = played.Play3 ? PassCards : 1;
                var centre = new Vector2(w * 0.5f, h * 0.42f);
                float off = pose.X * (w * 0.5f + 120f * unit);
                for (int i = 0; i < pass.Count; i++)
                {
                    var card = pass[i];
                    if (i >= shown) { Hide(card); continue; }
                    // 3장: 낸 카드 그대로(아르세우스가 섞이면 그 자리에 아르세우스 그림)
                    int key = played.Play3 && played.CardKeys != null && i < played.CardKeys.Length ? played.CardKeys[i] : -1;
                    PaintPass(card, key >= 0 ? FateRules.KindOf(ctx, key) : played.Card);
                    double a = pose.Spin + i * 2.0 * Math.PI / PassCards;
                    float r = played.Play3 ? pose.Spread * SpreadPx * unit : 0f;
                    FxUi.Move(card, centre.x + off + (float)Math.Cos(a) * r, centre.y + (float)Math.Sin(a) * r * 0.4f);
                    FxUi.Fade(card, 1f);
                }
                FxUi.Move(glow, centre.x + off, centre.y);
                FxUi.Fade(glow, pose.Glow * (played.WildcardAs >= 0 ? 1f : 0.7f));
                PassShown = true;
            }
            else
            {
                foreach (var c in pass) Hide(c);
                Hide(glow);
            }
        }

        /// <summary>그 요소의 가운데(이 층 좌표). 배치 전이면 null.</summary>
        public Vector2? Centre(VisualElement e)
        {
            if (e == null) return null;
            var box = e.worldBound;
            if (float.IsNaN(box.width) || box.width <= 0f) return null;
            return Element.WorldToLocal(box.center);
        }
    }
}
