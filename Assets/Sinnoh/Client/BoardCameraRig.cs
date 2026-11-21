using UnityEngine;
using UnityEngine.InputSystem;

namespace Sinnoh.Client
{
    /// <summary>
    /// 보드 카메라 뼈대(M4-2b, 04 §3-2 안 ① + d3-3 §3): 원근 세로 시야 30° [가정 M4-2a], 기울기 55° 고정, 60° 단위 6방향, 줌 3단.
    /// 줌 = 화면 가운데 칸 폭(1080에서 51 / 130 / 245 px) → 바라보는 점까지 거리 d = 2.2 × 540 / (W × tan 15°) = 86.9 / 34.1 / 18.1.
    /// 입력(Input System): Q·E 방향, 휠 줌 — <see cref="InputEnabled"/>가 꺼져 있으면 듣지 않는다. 걷는 말 따라가기와 마우스로 끌어 옮기기는 화면 흐름이 <see cref="LookAt"/>으로 한다(결정 57 ① · 결정 66 ②).
    /// 자유 회전은 없다(결정 66 ②로 거둠). 평면 지도는 M4-3 이후.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)]   // 화면 시작점(ScreenBoot)이 이번 프레임의 마우스 자리로 WheelEnabled를 정한 뒤에 휠을 읽는다 — 보드에서 HUD 위로 옮기며 굴린 휠이 한 프레임 새지 않게
    public sealed class BoardCameraRig : MonoBehaviour
    {
        public enum Zoom { Full, Region, Close }

        public const float Tilt = 55f;
        public const float FieldOfView = 30f;
        public const float HexWidth = 2.2f;
        public const float ReferenceHeight = 1080f;

        [SerializeField] Vector3 target;
        [SerializeField, Range(0, 5)] int yaw;
        [SerializeField] Zoom zoom;

        public Vector3 Target => target;
        public int Yaw => yaw;
        public Zoom CurrentZoom => zoom;

        /// <summary>Q·E·휠을 듣는가. 화면(타이틀·설정·셋업)이 보드를 덮고 있을 때는 끈다 — 화면 흐름(<see cref="GameFlow"/>)이 켜고 끈다. 기본 켬(보드만 띄운 측정·캡처).</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>휠 줌을 듣는가(<see cref="InputEnabled"/>가 켜져 있을 때만 뜻이 있다). 마우스가 보드 HUD 위에 있으면 화면 흐름이 끈다 — 판 위에서 굴린 휠이 보드로 새지 않게.</summary>
        public bool WheelEnabled { get; set; } = true;

        /// <summary>줌별 화면 가운데 칸 폭(1080 기준 px, 04 §3-2).</summary>
        public static float CellPixels(Zoom z) => z == Zoom.Full ? 51f : z == Zoom.Region ? 130f : 245f;

        public static float Distance(Zoom z) => HexWidth * ReferenceHeight * 0.5f / (CellPixels(z) * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad));

        // 두 줌 사이를 미끄러지는 중의 거리(0 = 줌 단계의 거리 그대로)
        float glide;

        /// <summary>
        /// 두 줌 단계 사이를 미끄러진다(셋업 연출 — 04 §8-8 2·5행): 보는 점까지의 거리는 두 단계 사이의 t(0 ~ 1) 자리이고, 줌 단계(말의 정수배)는 가운데에서 바뀐다.
        /// t가 1 이상이면 그 줌 단계에 그대로 선다. 방향은 그대로다. 다음 <see cref="Set"/>(Q·E·휠 포함)이 미끄러짐을 끝낸다.
        /// </summary>
        public void Glide(Zoom from, Zoom to, float t, Vector3 lookAt)
        {
            t = Mathf.Clamp01(t);
            zoom = t < 0.5f ? from : to;
            glide = t >= 1f || from == to ? 0f : Mathf.Lerp(Distance(from), Distance(to), t);
            target = lookAt;
            Apply();
        }

        public void Set(Zoom z, int yawStep, Vector3 lookAt)
        {
            glide = 0f;
            zoom = z;
            yaw = ((yawStep % 6) + 6) % 6;
            target = lookAt;
            Apply();
        }

        /// <summary>보는 점만 옮긴다 — 방향·줌은 그대로(걷는 말 따라가기, 결정 57 ①).</summary>
        public void LookAt(Vector3 point)
        {
            if (point == target) return;
            target = point;
            Apply();
        }

        public void Apply()
        {
            GetComponent<Camera>().fieldOfView = FieldOfView;
            var rot = Quaternion.Euler(Tilt, yaw * 60f, 0f);
            transform.SetPositionAndRotation(target - rot * Vector3.forward * (glide > 0f ? glide : Distance(zoom)), rot);
            Shader.SetGlobalFloat(BoardPieces.ZoomKProperty, BoardPieces.ZoomK(zoom)); // 말 정수배(M4-2d, d3-3 §2-3)
        }

        void Awake() => Apply();

        /// <summary>Q · E 돌리기를 받나 — 글 칸에 글을 치는 동안 끈다(<c>GameFlow.Typing</c>, M9 다듬기 P22).</summary>
        public bool KeysEnabled { get; set; } = true;

        void Update()
        {
            if (!InputEnabled) return;
            int y = yaw, z = (int)zoom;
            var kb = Keyboard.current;
            if (kb != null && KeysEnabled)
            {
                if (kb.qKey.wasPressedThisFrame) y--;
                if (kb.eKey.wasPressedThisFrame) y++;
            }
            var mouse = Mouse.current;
            float scroll = mouse != null && WheelEnabled ? mouse.scroll.ReadValue().y : 0f;
            if (scroll > 0f) z = Mathf.Min(z + 1, (int)Zoom.Close);
            else if (scroll < 0f) z = Mathf.Max(z - 1, (int)Zoom.Full);
            if (y != yaw || z != (int)zoom) Set((Zoom)z, y, target);
        }
    }
}
