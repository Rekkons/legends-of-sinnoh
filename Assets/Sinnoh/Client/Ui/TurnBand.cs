using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>턴 넘김 한 번에 띄울 것(04 §8-8 101행): 누구의 턴이 끝났고 누구의 턴이 오나, 몇 라운드인가, 끝난 턴의 결과 줄. 화면 흐름이 이벤트에서 모아 단계의 꼬리표(<see cref="FxStep.Tag"/>)로 붙인다.</summary>
    public sealed class TurnBandInfo
    {
        /// <summary>턴이 끝난 좌석(게임 첫 턴이면 −1)과 턴이 오는 좌석.</summary>
        public int From = -1, To;
        public int Round;
        /// <summary>끝난 턴의 라운드 — 넘김 띠가 끝날 때까지 HUD 라운드 배지에 남는 값(<see cref="From"/>이 있을 때만 쓴다).</summary>
        public int FromRound;
        /// <summary>좌석 색(고향 마을 색).</summary>
        public Color32 FromColor, ToColor;
        public string FromName, ToName;
        /// <summary>두 좌석의 주인공(키 — <c>GameFlow.Protagonist</c>): 얼굴 상자에 그 얼굴을 그린다(결정 54 ④).</summary>
        public string FromFace, ToFace;
        /// <summary>끝난 턴의 결과 줄(얻은 것 — 최대 셋). 비어 있으면 「변화 없음」.</summary>
        public readonly List<string> Lines = new List<string>();
    }

    /// <summary>
    /// 턴 시작 단계(04 §8-8 6행 — 여럿 판만)에 띄울 것: 턴인 좌석(이름 · 색 · 주인공) · 내 턴인가 · 받은 이동력 · 라운드의 첫 턴이면 그 라운드(102행 라운드 띠 — 아니면 0).
    /// 화면 흐름이 이벤트에서 모아 그 단계의 꼬리표로 붙인다.
    /// </summary>
    public sealed class TurnStartInfo
    {
        public int Seat, Moves, Round;
        public bool Mine;
        public Color32 Color;
        public string Name, Face;
    }

    /// <summary>
    /// 턴 넘김 연출 부품(04 §8-8 101행, 목업 r5-fx 안 A — 결정 53 ①·54 ③·55 ②): 턴 결과 요약(1010) → 넘김 띠(1011) → 내 턴 컷신(1012)이 차례로 이어진다.
    /// 길이는 대기열 항목의 단계가 정하고(<c>PlayTiming</c> — 결정 57: 여럿 800 · 600 · 600, 한 좌석 판 500 · 500이고 요약은 변화가 있을 때만 800), 여기는 단계 안 시각으로 모습만 정한다 —
    /// 옮기기·늘이기·흐리기뿐(04 §8-6). 박자는 단계 길이의 비율이다(04 §8-5 「시간표는 0~1 비율」 — 길이를 바꿔도 머묾이 남는다):
    /// 요약 = 처음 1/5 동안 아래 10px에서 올라와 머묾(띠가 들어올 때 걷힘) · 띠 = 들어옴 1/4 · 머묾 1/2 · 나감 1/4 · 컷신 = 세로로 열림 1/6 · 머묾 2/3 · 끝 1/6 동안 걷힘(착지).
    /// 600이면 띠 150 · 300 · 150, 컷신 100 · 400 · 100 / 500이면 띠 125 · 250 · 125, 컷신 83 · 334 · 83 / 요약 800이면 오름 160.
    /// 큰 글자는 본문 글자를 정수배로 늘린 것이다(1080에서 48·72px — 12/24px 규칙의 예외는 넘김 띠·내 턴 컷신뿐, 결정 54 ③).
    /// 한 좌석뿐인 판(혼자 하기)은 「A ▶ B」 대신 라운드를 적는다 [가정].
    /// 여럿 판의 턴 시작(6행 — <see cref="TurnStartBeats"/>, 결정 70 ① A1 나): 남의 턴이면 위쪽에 배너 <c>turn-start-banner</c>(얼굴 상자 + 「○○의 턴」)가 내려왔다 올라가고,
    /// 라운드의 첫 턴이면 라운드 띠 <c>turn-round-strip</c>(102행 「n라운드」)가 넘김 띠보다 위쪽을 왼쪽에서 오른쪽으로 지나간다.
    /// 요소 이름: <c>turn-band</c>·<c>turn-band-summary</c>·<c>turn-band-strip</c>·<c>turn-band-cut</c>, 얼굴 상자 <c>turn-band-face-from</c>·<c>turn-band-face-to</c>·<c>turn-band-face-cut</c>.
    /// </summary>
    public sealed class TurnBand
    {
        /// <summary>단계 길이를 나누는 수: 띠 들어옴·나감 = 길이 / 4, 컷신 열림·걷힘 = 길이 / 6, 요약 오름 = 길이 / 5.</summary>
        public const int SlidePart = 4, OpenPart = 6, LandPart = 6, RisePart = 5;
        public const int RisePx = 10;

        readonly UiStage stage;
        readonly VisualElement summary, summaryLines, strip, stripFill, cut, cutFill, stripFaceFrom, stripFaceTo, cutFace;
        readonly Label summaryTitle;
        readonly BigText stripText, cutText, roundText;
        readonly VisualElement banner, bannerFill, bannerFace, roundStrip, roundFill;
        readonly Label bannerText;
        TurnBandInfo shown;
        TurnStartInfo shownStart;

        /// <summary>턴 시작 배너(6행) · 라운드 띠(102행)가 지금 보이나 — 시험·캡처가 본다.</summary>
        public bool BannerShown { get; private set; }
        public bool RoundShown { get; private set; }

        /// <summary>
        /// 정수배로 늘린 큰 글자 한 줄. 늘이기(scale)는 배치 크기를 바꾸지 않아 옆 요소와 겹친다 — 그래서 같은 글의 보이지 않는 자리 채움을 배수만큼 나란히 두어
        /// 늘어난 폭을 배치에 잡아 두고, 보이는 글자는 그 가운데에 얹어 늘린다(글 길이를 코드로 재지 않는다).
        /// </summary>
        sealed class BigText
        {
            public readonly VisualElement Element = new VisualElement { pickingMode = PickingMode.Ignore };
            readonly List<Label> pads = new List<Label>();
            readonly Label label;

            public BigText(int times, string cls)
            {
                Element.AddToClassList("sn-big");
                for (int i = 0; i < times; i++)
                {
                    var pad = Kit.Text("", Kit.F2);
                    pad.AddToClassList("sn-big-pad");
                    pad.pickingMode = PickingMode.Ignore;
                    pads.Add(pad);
                    Element.Add(pad);
                }
                label = Kit.Text("", Kit.F2);
                label.AddToClassList("sn-big-" + times.ToString(System.Globalization.CultureInfo.InvariantCulture));
                label.AddToClassList(cls);
                label.pickingMode = PickingMode.Ignore;
                Element.Add(label);
            }

            public void Set(string text, Color32 color)
            {
                foreach (var pad in pads) pad.text = text;
                label.text = text;
                label.style.color = new StyleColor((Color)color);
            }

            /// <summary>글자색은 USS가 정한다(UB 페이즈 띠 — <c>sn-round-ub</c>).</summary>
            public void Set(string text)
            {
                foreach (var pad in pads) pad.text = text;
                label.text = text;
                label.style.color = StyleKeyword.Null;
            }
        }

        /// <summary>102행 UB 페이즈 띠(가운데 가로 띠 「울트라 비스트의 턴」 — UB 턴 시작 821 단계 처음)가 지금 보이나 — 시험·캡처가 본다.</summary>
        public bool UbPhaseShown { get; private set; }
        bool ubPainted;

        public readonly VisualElement Element;

        /// <summary>지금 그리고 있는 단계의 행(없으면 0) — 시험·캡처가 본다.</summary>
        public int ActiveRow { get; private set; }

        public TurnBand(UiStage stage)
        {
            this.stage = stage;
            Element = new VisualElement { name = "turn-band", pickingMode = PickingMode.Ignore };
            Element.AddToClassList("sn-band-layer");

            strip = Add(Element, "turn-band-strip", "sn-band-strip");
            stripFill = Add(strip, null, "sn-band-fill");
            var stripRow = Add(strip, null, "sn-band-row");
            stripFaceFrom = Face(stripRow, "turn-band-face-from");
            stripText = new BigText(2, "sn-band-text");
            stripRow.Add(stripText.Element);
            stripFaceTo = Face(stripRow, "turn-band-face-to");

            cut = Add(Element, "turn-band-cut", "sn-band-strip");
            cutFill = Add(cut, null, "sn-band-fill");
            var cutRow = Add(cut, null, "sn-band-row");
            cutFace = Face(cutRow, "turn-band-face-cut");
            cutText = new BigText(3, "sn-band-text-big");
            cutRow.Add(cutText.Element);

            summary = Kit.Panel(stage.Skin);
            summary.name = "turn-band-summary";
            summary.AddToClassList("sn-band-summary");
            summaryTitle = Kit.Text("", Kit.FTitle);
            summaryTitle.AddToClassList("kit-title");
            summary.Add(summaryTitle);
            summaryLines = Add(summary, null, null);
            Element.Add(summary);

            // 턴 시작 배너(6행 — 여럿 판의 남의 턴): 위 띠 아래 가운데. 라운드 띠(102행): 넘김 띠보다 위
            // 배너는 내용 폭(얼굴 상자가 왼쪽 끝에 붙는다)이고, 화면 폭의 줄이 가운데에 세운다 — 자리는 줄이, 연출(옮기기·흐리기)은 배너가 받는다
            banner = Add(Add(Element, "turn-start-banner-row", "sn-turn-banner-row"), "turn-start-banner", "sn-turn-banner");
            bannerFill = Add(banner, null, "sn-band-fill");
            bannerFace = FaceBox.Make();
            bannerFace.name = "turn-start-face";
            banner.Add(bannerFace);
            bannerText = Kit.Text("", Kit.F2);
            bannerText.AddToClassList("sn-turn-banner-text");
            banner.Add(bannerText);
            roundStrip = Add(Element, "turn-round-strip", "sn-round-strip");
            roundFill = Add(roundStrip, null, "sn-band-fill");
            var roundRow = Add(roundStrip, null, "sn-band-row");
            roundText = new BigText(2, "sn-band-text");
            roundRow.Add(roundText.Element);

            foreach (var e in new[] { summary, strip, cut, banner, roundStrip })
            {
                e.pickingMode = PickingMode.Ignore;
                FxUi.Fade(e, 0f);
            }
            Unpick(Element);
        }

        // 넘김 띠는 마우스를 받지 않는다 — 요소마다 따로 꺼야 한다(부모가 Ignore여도 자식 글자는 잡히고, 흐려서 안 보여도(opacity 0) 잡힌다).
        // 하나라도 남으면 보이지 않는 요약 판이 보드 한가운데의 칸 고르기·휠을 막는다(GameFlow.OverUi)
        static void Unpick(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var child in e.Children()) Unpick(child);
        }

        static VisualElement Add(VisualElement parent, string name, string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            if (name != null) e.name = name;
            if (cls != null) e.AddToClassList(cls);
            parent.Add(e);
            return e;
        }

        // 큰 얼굴 상자(FaceBox — 주인공 얼굴 + 좌석 색 테두리 + 번호 배지, 결정 54 ④). 만든 뒤 생성자 끝의 Unpick이 훑는다
        static VisualElement Face(VisualElement parent, string name)
        {
            var face = FaceBox.Make(big: true);
            face.name = name;
            parent.Add(face);
            return face;
        }

        void Fill(TurnBandInfo info)
        {
            if (ReferenceEquals(info, shown)) return;
            shown = info;
            bool handoff = info.From >= 0 && info.From != info.To;
            summaryTitle.text = stage.T("screen.board.summary").Replace("{name}", info.FromName ?? "");
            summaryLines.Clear();
            if (info.Lines.Count == 0) summaryLines.Add(Muted(stage.T("screen.board.summary.none")));
            foreach (string line in info.Lines) summaryLines.Add(Kit.Text(line, Kit.F2));
            Unpick(summaryLines);
            string round = stage.T("screen.board.round").Replace("{n}", info.Round.ToString(System.Globalization.CultureInfo.InvariantCulture));
            stripText.Set(handoff ? info.FromName + " ▶ " + info.ToName : round, SeatColors.Ink(info.ToColor));
            stripFill.style.backgroundColor = new StyleColor((Color)info.ToColor);
            FaceBox.Paint(stripFaceFrom, handoff ? info.From : info.To, handoff ? info.FromColor : info.ToColor, handoff ? info.FromFace : info.ToFace);
            FaceBox.Paint(stripFaceTo, info.To, info.ToColor, info.ToFace);
            stripFaceTo.style.display = handoff ? DisplayStyle.Flex : DisplayStyle.None;
            cutText.Set(stage.T("screen.board.my_turn"), SeatColors.Ink(info.ToColor));
            cutFill.style.backgroundColor = new StyleColor((Color)info.ToColor);
            FaceBox.Paint(cutFace, info.To, info.ToColor, info.ToFace);
        }

        static Label Muted(string text)
        {
            var l = Kit.Text(text, Kit.F2);
            l.AddToClassList("kit-muted");
            return l;
        }

        // 턴 시작(6행)의 배너와 라운드 띠(102행): 그 단계 안 시각으로만 정한다. 내 턴에는 배너가 없다(101행 컷신이 겸한다)
        void ShowStart(FxEntry entry, int localMs)
        {
            var step = TurnStartBeats.StepOf(entry);
            var info = step != null ? step.Tag as TurnStartInfo : null;
            int t = step != null ? localMs - step.StartMs : -1;
            BannerShown = RoundShown = false;
            float a = 0f, y = -1f, x = -100f;
            if (info != null && t >= 0 && t < step.Ms)
            {
                if (!ReferenceEquals(info, shownStart))
                {
                    shownStart = info;
                    bannerText.text = stage.T("screen.board.turn_start").Replace("{name}", info.Name ?? "");
                    bannerText.style.color = new StyleColor((Color)SeatColors.Ink(info.Color));
                    bannerFill.style.backgroundColor = new StyleColor((Color)info.Color);
                    roundFill.style.backgroundColor = new StyleColor((Color)info.Color);
                    FaceBox.Paint(bannerFace, info.Seat, info.Color, info.Face);
                    roundText.Set(stage.T("screen.board.round").Replace("{n}", info.Round.ToString(System.Globalization.CultureInfo.InvariantCulture)), SeatColors.Ink(info.Color));
                }
                if (!info.Mine)
                {
                    var pose = TurnStartBeats.Banner(t, step.Ms);
                    a = pose.Shown;
                    y = pose.Slide;
                }
                if (info.Round > 0)
                {
                    var round = TurnStartBeats.Round(t);
                    RoundShown = round.Shown;
                    x = round.X;
                }
                if (ubPainted)
                {
                    ubPainted = false;
                    roundStrip.RemoveFromClassList("sn-round-ub");
                    roundText.Set(stage.T("screen.board.round").Replace("{n}", info.Round.ToString(System.Globalization.CultureInfo.InvariantCulture)), SeatColors.Ink(info.Color));
                    roundFill.style.backgroundColor = new StyleColor((Color)info.Color);
                }
            }
            // 102행 UB 페이즈 띠: 라운드 끝 UB 턴의 시작(821)에 같은 띠가 보라로 「울트라 비스트의 턴」(M6 「UB 화면 묶음 1」)
            UbPhaseShown = false;
            if (!RoundShown)
            {
                var ub = UbTurnBeats.At(entry, localMs);
                if (ub.Phase)
                {
                    if (!ubPainted)
                    {
                        ubPainted = true;
                        shownStart = null;
                        roundStrip.AddToClassList("sn-round-ub");
                        roundFill.style.backgroundColor = StyleKeyword.Null;
                        roundText.Set(stage.T("screen.board.ub.turn"));
                    }
                    UbPhaseShown = true;
                    x = ub.PhaseX;
                }
            }
            BannerShown = a > 0f;
            FxUi.Fade(banner, a);
            banner.style.translate = new Translate(0f, Length.Percent(Mathf.Round(y * 200f)));
            FxUi.Fade(roundStrip, RoundShown || UbPhaseShown ? 1f : 0f);
            roundStrip.style.translate = new Translate(Length.Percent(Mathf.Round(x)), 0f);
        }

        /// <summary>대기열의 지금 항목·시각으로 그린다. 넘김 단계가 없는 항목이면 모두 걷는다.</summary>
        public void Show(FxSequencer seq) => Show(seq.Current, seq.LocalMs);

        public void Show(FxEntry entry, int localMs)
        {
            var sum = entry?.Step(FxRows.TurnSummary);
            var band = entry?.Step(FxRows.Handoff);
            var mine = entry?.Step(FxRows.MyTurn);
            var info = (sum ?? band ?? mine)?.Tag as TurnBandInfo;
            ActiveRow = 0;
            ShowStart(entry, localMs);
            if (info == null)
            {
                FxUi.Fade(summary, 0f);
                FxUi.Fade(strip, 0f);
                FxUi.Fade(cut, 0f);
                return;
            }
            Fill(info);

            // 요약: 올라와 머물고, 띠가 들어오는 동안 걷힌다
            float sumA = 0f, sumY = RisePx;
            if (sum != null && localMs >= sum.StartMs)
            {
                int t = localMs - sum.StartMs, rise = sum.Ms / RisePart, gone = band != null ? band.Ms / SlidePart : rise;
                sumA = Tween.At(t, 0, rise, 0f, 1f, Ease.OutQuad) * (1f - Tween.Progress(t, sum.Ms, gone));
                sumY = Tween.At(t, 0, rise, RisePx, 0f, Ease.OutQuad);
                if (t < sum.Ms) ActiveRow = FxRows.TurnSummary;
            }
            FxUi.Fade(summary, sumA);
            FxUi.Move(summary, 0f, sumY);

            // 띠: 왼쪽에서 닦아 들어와 머물다 오른쪽으로 나간다
            float bandA = 0f, bandX = -100f;
            if (band != null && localMs >= band.StartMs && localMs < band.EndMs)
            {
                int t = localMs - band.StartMs, slide = band.Ms / SlidePart;
                bandA = 1f;
                bandX = t < band.Ms - slide
                    ? Tween.At(t, 0, slide, -100f, 0f, Ease.OutQuad)
                    : Tween.At(t, band.Ms - slide, slide, 0f, 100f, Ease.InQuad);
                ActiveRow = FxRows.Handoff;
            }
            FxUi.Fade(strip, bandA);
            strip.style.translate = new Translate(Length.Percent(Mathf.Round(bandX)), 0f);

            // 내 턴 컷신: 세로로 열리고, 끝에서 걷힌다(착지 — 그 뒤 입력이 열린다)
            float cutA = 0f, cutY = 0f;
            if (mine != null && localMs >= mine.StartMs && localMs < mine.EndMs)
            {
                int t = localMs - mine.StartMs, land = mine.Ms / LandPart;
                cutY = Tween.At(t, 0, mine.Ms / OpenPart, 0f, 1f, Ease.OutQuad);
                cutA = 1f - Tween.Progress(t, mine.Ms - land, land);
                ActiveRow = FxRows.MyTurn;
            }
            FxUi.Fade(cut, cutA);
            FxUi.Scale(cut, 1f, cutY);
        }
    }
}
