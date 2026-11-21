using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// T18 ① 공중날기 목적지를 보드에서 고르기(<see cref="VillageScreen.Picking"/>): 마을 창이 오른쪽 기둥으로 비키고 보드 입력이 그 창으로 간다 —
    /// 마우스를 올린 칸 · 누른 칸은 <see cref="VillageScreen.HoverCell"/> · <see cref="VillageScreen.ClickCell"/>, 칠하기는 <see cref="BoardHud"/>의 이동 표시 자리에서 <see cref="VillageScreen.Mark"/>.
    /// 고르기를 시작할 때의 카메라(보는 점 · 줌 · 방향)를 쥐어 두고 [취소] · Esc · 창 닫기면 되돌린다(날아가면 되돌리지 않는다 — 카메라가 말을 따라간다).
    /// </summary>
    public sealed partial class GameFlow
    {
        /// <summary>카메라 자리 하나(보는 점 · 줌 · 방향) — 쥐어 두었다 되돌리는 값.</summary>
        public struct CameraLook
        {
            public bool Set;
            public BoardCameraRig.Zoom Zoom;
            public int Yaw;
            public Vector3 Target;
        }

        /// <summary>보드에서 공중날기 목적지를 고르는 중인 마을 창(없으면 null).</summary>
        public VillageScreen FlyPick => OnBoard && Stage.Current is VillageScreen v && v.Picking ? v : null;

        /// <summary>지금 카메라 자리.</summary>
        public CameraLook SaveLook() => rig == null ? default : new CameraLook { Set = true, Zoom = rig.CurrentZoom, Yaw = rig.Yaw, Target = rig.Target };

        /// <summary>쥐어 둔 카메라 자리로 되돌린다(연출이 돌고 있으면 두지 않는다 — 그 연출의 따라가기가 카메라를 쥐고 있다).</summary>
        public bool RestoreLook(CameraLook look)
        {
            if (!look.Set || rig == null || (Fx != null && !Fx.Sequencer.IsEmpty)) return false;
            rig.Set(look.Zoom, look.Yaw, look.Target);
            return true;
        }

        // 보드에서 고르기의 전체 줌 보는 점 — 그 자리 그대로면 프레임마다 오른쪽 기둥 창의 지금 자리에 다시 맞춘다(끌거나 칩으로 옮겼으면 두지 않는다)
        bool flyAimed;
        Vector3 flyAt;

        /// <summary>판 전체가 보이게 전체 줌으로(방향 그대로 — 보는 점 = <see cref="WideLook"/>, 오른쪽 기둥 창 왼쪽에 판이 오게).</summary>
        public bool WideFly()
        {
            if (rig == null || board == null || (Fx != null && !Fx.Sequencer.IsEmpty)) return false;
            flyAt = WideLook(FlyRight());
            flyAimed = true;
            rig.Set(BoardCameraRig.Zoom.Full, rig.Yaw, flyAt);
            return true;
        }

        // 마을 창(오른쪽 기둥)의 왼쪽 끝(물리 px) — 배치가 아직 없으면 0(화면 끝)
        double FlyRight()
        {
            var panel = Stage.Root != null ? Stage.Root.Q("board-village-panel") : null;
            if (panel == null || float.IsNaN(panel.worldBound.width) || panel.worldBound.width <= 0f || !panel.ClassListContains("sn-village-side")) return 0.0;
            return panel.worldBound.xMin * Stage.Grade.PanelScale;
        }

        /// <summary>
        /// 그 칸이 지금 화면에서 가리지 않고 보이나(보드에서 고르는 동안 — 오른쪽 기둥 창 왼쪽 · 위 띠 아래 · 아래 판 위). 보이면 목적지 칩에 올려도 카메라를 옮기지 않는다(판 전체를 본 채 강조만 — [가정 #676]).
        /// </summary>
        public bool CellClear(int cell)
        {
            if (rig == null || board == null || laidFor.x <= 0 || cell < 0 || cell >= board.Board.Count) return false;
            if (!BoardPick.Screen(board, rig.GetComponent<Camera>(), cell, laidFor.x, laidFor.y, out var px)) return false;
            double right = FlyRight();
            if (right <= 0.0) right = laidFor.x;
            float scale = Stage.Grade.PanelScale;
            var dock = Stage.Root != null ? Stage.Root.Q("board-hud-dock") : null;
            var status = Stage.Root != null ? Stage.Root.Q("board-hud-status") : null;
            float bottom = dock != null && dock.worldBound.height > 0f ? laidFor.y - dock.worldBound.yMin * scale : 0f;
            float top = status != null && status.worldBound.height > 0f ? laidFor.y - status.worldBound.yMax * scale : laidFor.y;
            return px.x >= 0f && px.x <= right && px.y >= bottom && px.y <= top;
        }

        void Reaim()
        {
            if (!flyAimed) return;
            if (FlyPick == null || rig == null || rig.CurrentZoom != BoardCameraRig.Zoom.Full || rig.Target != flyAt) { flyAimed = false; return; }
            var look = WideLook(FlyRight());
            if (look == flyAt) return;
            flyAt = look;
            rig.LookAt(look);
        }

        /// <summary>깨어진 세계의 칸이 보드에 보이나(울트라메가로폴리스를 흐리게 칠할지).</summary>
        public bool DistortionShown => board != null && board.DistortionWorldVisible;
    }
}
