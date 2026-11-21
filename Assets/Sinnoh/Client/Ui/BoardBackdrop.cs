using Rekkon.UiKit;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 보드 위에 얹히는 화면(타이틀·솔로 설정·고향 고르기): 왼쪽에 판을 두고 뒤의 3D 보드 전경을 보인다.
    /// 화면 흐름(<see cref="GameFlow"/>)이 보드 왼쪽 끝을 <see cref="PanelRight"/> 오른쪽으로 민다(<see cref="BoardBackdrop"/>).
    /// </summary>
    public interface IOverBoard
    {
        /// <summary>왼쪽 판의 오른쪽 변(720 토큰 px — <c>Sinnoh.uss</c>의 그 판 left + width와 같아야 한다, 시험이 견준다).</summary>
        float PanelRight { get; }
    }

    /// <summary>
    /// 화면 뒤 보드 전경의 자리(04 §9-3): 보드는 화면 가운데에 높이 기준으로 그려지고 판은 왼쪽 변에 붙으므로, 판이 넓거나 창이 좁으면 보드가 판 밑으로 들어간다 —
    /// 보드 왼쪽 끝이 판 오른쪽 변 + 틈에 오도록 카메라가 보는 점을 옮긴다(보드 오른쪽 끝이 창 안에 남는 데까지만).
    /// </summary>
    public static class BoardBackdrop
    {
        /// <summary>판과 보드 사이 틈(720 토큰 px) [가정].</summary>
        public const float Gap = 12f;

        /// <summary>전체 줌·정면·보는 점이 원점일 때 보드가 화면 가운데에서 왼쪽·오른쪽으로 뻗는 폭(높이 1080 기준 px — <c>Logs/BoardShots/full-yaw0.png</c>에서 잰 x 427~1470).</summary>
        public const float BoardLeft = 533f, BoardRight = 510f;

        /// <summary>보드를 오른쪽으로 미는 폭(물리 px). 4:3보다 좁은 창이나 넓은 판은 다 피하지 못한다 [가정 — 지원 범위 밖].</summary>
        public static float ShiftPx(int width, int height, float panelRight)
        {
            var grade = UiGrade.For(width, height);
            float unit = grade.TokenHeight / 720f * grade.PanelScale;
            float k = height / BoardCameraRig.ReferenceHeight;
            float need = (panelRight + Gap) * unit - (width * 0.5f - BoardLeft * k);
            float room = width * 0.5f - BoardRight * k - Gap * unit;
            return Mathf.Clamp(need, 0f, Mathf.Max(0f, room));
        }

        /// <summary>전경 카메라가 보는 점(전체 줌·정면): 원점에서 <see cref="ShiftPx"/>만큼 그림이 오른쪽으로 가게 왼쪽으로 옮긴 점.</summary>
        public static Vector3 Target(int width, int height, float panelRight) =>
            Vector3.left * (ShiftPx(width, height, panelRight) * BoardCameraRig.HexWidth * BoardCameraRig.ReferenceHeight / (BoardCameraRig.CellPixels(BoardCameraRig.Zoom.Full) * height));
    }
}
