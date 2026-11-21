using System;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 공중날기 앞의 비전기술 컷인 층(사용자 결정 167 — Pt 본편 hm_cut_in을 본뜸): 화면 가운데의 가로 띠(원작 자산 <c>data/field_cutin.narc</c> 띠 그림 — Gen <see cref="BandKey"/>)가
    /// 펼쳐지고, 찌르호크(BW 앞모습 도트)에 그 좌석 주인공(필드 도트 — 왼쪽을 보는 프레임)이 탄 채 오른쪽에서 들어와 가운데에서 울고 왼쪽으로 빠져나간 뒤 띠가 접힌다.
    /// 박자는 순수 셈 <see cref="FlyCutinBeats"/>(16행 단계의 앞 1400ms) — 여기는 그 모습을 옮기고 늘이기만 한다(연출은 옮기기·늘이기·흐리기만). 남 좌석이 날 때도 그 좌석의 주인공으로 그린다(결정 120).
    /// 도트는 정수배: 한 텍셀 = 화면 px <see cref="Texel"/>(화면 높이 / <see cref="TexelPer"/>, 1 이상). 보드 HUD 판 위에 얹고 마우스를 받지 않는다.
    /// 요소 이름: <c>board-fly-cutin</c> · <c>board-fly-cutin-band</c> · <c>board-fly-cutin-riders</c> · <c>board-fly-cutin-bird</c> · <c>board-fly-cutin-rider</c>.
    /// </summary>
    public sealed class FlyCutin
    {
        /// <summary>띠 그림(Gen — stage 묶음, 256×80).</summary>
        public const string BandKey = "Stage/Cutin/band.png";

        /// <summary>띠 그림의 크기(텍셀).</summary>
        public const int BandWidth = 256;

        /// <summary>한 텍셀의 화면 px = 화면 높이 / 이 값(내림, 1 이상) — 720 = 3배 · 1080 = 4배(띠가 화면 높이의 1/3쯤) [가정].</summary>
        public const int TexelPer = 240;

        /// <summary>주인공이 찌르호크 등에 걸치는 자리: 찌르호크 그림 위에서 아래로 들어간 텍셀 · 가운데에서 오른쪽으로 비킨 텍셀 [가정 — 캡처로 맞춤].</summary>
        public const int RideDown = 14, RideRight = 4;

        readonly GameFlow flow;
        readonly VisualElement band, riders;
        public readonly VisualElement Element;

        MonArt mons;
        bool monsTried;
        VisualElement bird, rider;
        string built, riderKey;
        Vector2 birdSize, riderSize;

        /// <summary>지금 컷인의 모습(안 보이면 Shown = false) · 탄 주인공 키(없으면 null) · 한 텍셀의 화면 px — 확인용.</summary>
        public FlyCutinPose Pose { get; private set; }
        public string RiderShown { get; private set; }
        public int Texel { get; private set; }
        public bool Shown => Pose.Shown;

        public FlyCutin(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Element = new VisualElement { name = "board-fly-cutin", pickingMode = PickingMode.Ignore };
            Element.AddToClassList("sn-fly-cutin");
            band = new VisualElement { name = "board-fly-cutin-band", pickingMode = PickingMode.Ignore };
            band.style.position = Position.Absolute;
            Element.Add(band);
            riders = new VisualElement { name = "board-fly-cutin-riders", pickingMode = PickingMode.Ignore };
            riders.style.position = Position.Absolute;
            Element.Add(riders);
            Element.style.display = DisplayStyle.None;
            var tex = GenAssets.Load<Texture2D>(BandKey);
            if (tex != null) band.style.backgroundImage = new StyleBackground(tex);
            band.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.NoRepeat);
            band.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            band.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
        }

        /// <summary>프레임마다(화면 대기열이 도는 동안): 지금 항목의 16행 단계 앞 컷인을 그린다. 없으면 숨긴다.</summary>
        public void Show(FxSequencer seq, VisualElement hudRoot)
        {
            if (hudRoot != null && Element.parent != hudRoot) hudRoot.Add(Element);
            var step = seq != null ? FlyBeats.StepOf(seq.Current) : null;
            var pose = step != null && seq.LocalMs >= step.StartMs ? FlyCutinBeats.At(step.Local(seq.LocalMs), step.Ms) : default(FlyCutinPose);
            if (!pose.Shown)
            {
                Pose = default(FlyCutinPose);
                RiderShown = null;
                Element.style.display = DisplayStyle.None;
                return;
            }
            Pose = pose;
            Element.style.display = DisplayStyle.Flex;
            var grade = flow.Stage.Grade;
            Texel = Mathf.Max(1, grade.TokenHeight * grade.PanelScale / TexelPer);
            float unit = Texel / (float)grade.PanelScale;   // 패널 단위 / 텍셀

            // 띠: 가운데 줄에서 좌우로 → 위아래로 펼쳐지고 접힌다(그림은 가운데에 고정 — 창이 그림을 잘라 보이듯). 자리는 층 크기의 백분율 — HUD가 막 다시 지어져 배치 전이어도 맞다
            float bh = Mathf.Max(1f, Mathf.Round(FlyCutinBeats.BandTexels * unit * pose.Tall));
            band.style.left = Length.Percent(50f - 50f * pose.Wide);
            band.style.width = Length.Percent(100f * pose.Wide);
            band.style.top = Length.Percent(50f);
            band.style.marginTop = -Mathf.Round(bh / 2f);
            band.style.height = bh;
            band.style.backgroundSize = new BackgroundSize(new Length(BandWidth * unit), new Length(FlyCutinBeats.BandTexels * unit));

            // 탄 것: 찌르호크 + 그 좌석 주인공
            var f = FlyBeats.Flight(step);
            Riders(f != null ? flow.Protagonist(f.Seat) : null, unit);
            RiderShown = pose.Riders && rider != null ? riderKey : null;
            riders.style.display = pose.Riders ? DisplayStyle.Flex : DisplayStyle.None;
            if (!pose.Riders) return;
            // 가로: 가운데(0)에서 화면 밖(±1 — 그림 폭만큼 더)까지 · 세로: 띠 가운데에 날갯짓만큼
            float rw = Mathf.Max(birdSize.x, riderSize.x);
            riders.style.left = Length.Percent(50f + 50f * pose.X);
            riders.style.marginLeft = Mathf.Round(-rw / 2f + pose.X * rw);
            riders.style.top = Length.Percent(50f);
            riders.style.marginTop = Mathf.Round(-pose.Bob * unit - (birdSize.y + riderSize.y - RideDown * unit) / 2f);
        }

        // 찌르호크 · 주인공 도트를 (주인공이 바뀌면 다시) 짓는다 — 크기는 텍셀 × unit
        void Riders(string protagonist, float unit)
        {
            if (!monsTried) { mons = MonArt.Load(); monsTried = true; }
            string key = (protagonist ?? "") + "@" + unit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (key == built) return;
            built = key;
            riders.Clear();
            bird = rider = null;
            riderKey = null;
            birdSize = riderSize = Vector2.zero;
            Texture2D page = null;
            var rect = default(RectInt);
            for (int state = 0; mons != null && page == null && state < 4; state++)   // 도트 표의 키는 카드의 「종:상태」 — 찌르호크 카드가 어느 상태든 그림은 같은 종의 앞모습
                if (!mons.TryPiece(FlyCutinBeats.TaxiDex + ":" + state, false, out page, out rect, out _)) page = null;
            if (page != null)
            {
                bird = Clip("board-fly-cutin-bird", page, rect.x, rect.y, rect.width, rect.height, unit);
                birdSize = new Vector2(rect.width * unit, rect.height * unit);
            }
            if (protagonist != null && BoardPieces.FieldSide(protagonist, out var sheet, out int frame, out int size))
            {
                rider = Clip("board-fly-cutin-rider", sheet, 0, frame * size, size, size, unit);
                riderSize = new Vector2(size * unit, size * unit);
                riderKey = protagonist;
            }
            float rw = Mathf.Max(birdSize.x, riderSize.x);
            riders.style.width = rw;
            riders.style.height = birdSize.y + riderSize.y;
            // 주인공이 먼저(뒤) — 찌르호크 등에 걸쳐 아래가 가려진다
            if (rider != null)
            {
                rider.style.left = Mathf.Round((rw - riderSize.x) / 2f + RideRight * unit);
                rider.style.top = 0f;
                riders.Add(rider);
            }
            if (bird != null)
            {
                bird.style.left = Mathf.Round((rw - birdSize.x) / 2f);
                bird.style.top = Mathf.Round(riderSize.y - RideDown * unit);
                riders.Add(bird);
            }
        }

        // 그림 한 장의 한 조각(텍셀 사각형 — y 아래로)을 unit배로 보이는 칸
        static VisualElement Clip(string name, Texture2D page, int x, int y, int cw, int ch, float unit)
        {
            var clip = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            clip.style.position = Position.Absolute;
            clip.style.width = cw * unit;
            clip.style.height = ch * unit;
            clip.style.overflow = Overflow.Hidden;
            var sheet = new VisualElement { pickingMode = PickingMode.Ignore };
            sheet.style.position = Position.Absolute;
            sheet.style.left = -x * unit;
            sheet.style.top = -y * unit;
            sheet.style.width = page.width * unit;
            sheet.style.height = page.height * unit;
            sheet.style.backgroundImage = new StyleBackground(page);
            clip.Add(sheet);
            return clip;
        }
    }
}
