using System;
using Rekkon.UiKit;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 고향 마을 연출(04 §8-8 2행, 결정 66 ③) 동안의 화면: 마을 고르기 화면이 걷히고 보드가 드러난 위에 좌석 띠 하나만 뜬다 — 얼굴 상자 + 좌석 이름 + 고른 마을.
    /// 보드에서는 카메라가 그 마을로 다가가고 말이 떨어져 내려앉는다(<see cref="GameFlow"/>). 말이 내려앉는 때 얼굴 상자의 좌석 색이 회색에서 그 마을의 색으로 칠해지고 띠가 한 번 눌렸다 펴진다
    /// (<see cref="VillageBeats"/> — 「위 칩 줄의 그 좌석 칩이 회색에서 자리 색으로」: 좌석 칩 줄은 셋업 뒤의 HUD에 있어 여기서는 이 띠가 맡는다 [가정]).
    /// 모습은 화면 대기열의 시각에서만 나온다. 마우스를 받지 않고, 단계가 끝나면 화면 흐름이 다음 화면(스타팅 포켓몬·트레이너 카드 고르기)으로 바꾼다.
    /// 요소 이름: <c>setup-drop</c>·<c>setup-drop-band</c>·<c>setup-drop-face</c>·<c>setup-drop-name</c>·<c>setup-drop-village</c>.
    /// </summary>
    public sealed class SetupDropScreen : IUiScreen
    {
        /// <summary>띠가 들어오는 시간(ms) · 내려앉을 때 눌렸다 펴지는 시간(ms)과 배수(1.15 → 1).</summary>
        public const int EnterMs = 150, PopMs = 180;
        public const float PopScale = 1.15f;

        readonly GameFlow flow;
        UiStage stage;
        VisualElement band, face;
        Label name, village;

        public SetupDropScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "setup-drop";

        /// <summary>지금 띠에 그린 좌석(없으면 −1)과 그 좌석 색이 칠해졌나(말이 내려앉았다) — 확인·시험이 본다.</summary>
        public int Seat { get; private set; } = -1;
        public bool Coloured { get; private set; }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };
            band = Kit.Panel(stage.Skin);
            band.name = "setup-drop-band";
            band.AddToClassList("sn-setup-drop");
            page.Add(band);
            face = FaceBox.Make(true);
            face.name = "setup-drop-face";
            band.Add(face);
            name = Kit.Text("", Kit.F2);
            name.name = "setup-drop-name";
            name.AddToClassList("sn-setup-drop-name");
            band.Add(name);
            village = Kit.Text("", Kit.F2);
            village.name = "setup-drop-village";
            village.AddToClassList("sn-setup-drop-village");
            village.AddToClassList("kit-title");
            band.Add(village);
            Unpick(page);
            FxUi.OriginLeft(band);
            Animate();
            return page;
        }

        public void Close() { }

        static void Unpick(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var child in e.Children()) Unpick(child);
        }

        /// <summary>그 순간의 모습으로 그린다(화면 대기열이 프레임마다 · 화면을 맞출 때 부른다).</summary>
        public void Animate()
        {
            if (stage == null || band == null) return;
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            int at = SetupRows.At(seq, SetupRows.Village, null, out var step);
            var picked = VillageBeats.Picked(step);
            if (picked == null)
            {
                FxUi.Fade(band, 0f);
                Seat = -1;
                return;
            }
            int ms = at == SetupRows.Waiting ? 0 : at;
            var pose = VillageBeats.At(ms, step.Ms);
            var fixtures = flow.Ctx.Fixtures.Fixtures;
            string key = picked.VillageKey >= 0 && picked.VillageKey < fixtures.Count ? fixtures[picked.VillageKey].Key : null;
            Seat = picked.Seat;
            Coloured = pose.Landed;
            FaceBox.Paint(face, picked.Seat, pose.Landed && key != null ? SeatColors.ForVillage(key) : SeatColors.Unassigned, flow.Protagonist(picked.Seat));
            name.text = flow.SeatName(picked.Seat);
            village.text = key != null ? stage.T(key) : "";
            Unpick(band);
            // 들어옴: 왼쪽에서 미끄러져 들어온다. 내려앉음: 한 번 눌렸다 펴진다(1.15 → 1)
            float enter = Tween.At(ms, 0, EnterMs, 0f, 1f, Ease.OutQuad);
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            FxUi.Fade(band, 1f);
            FxUi.Move(band, -(1f - enter) * 400f * unit, 0f);
            int land = step.Ms * VillageBeats.LandPermille / 1000;
            float pop = ms >= land ? Tween.At(ms, land, PopMs, PopScale, 1f, Ease.OutQuad) : 1f;
            FxUi.Scale(band, pop, pop);
        }
    }
}
