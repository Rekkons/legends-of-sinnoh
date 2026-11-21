using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 창 크기·전체 화면을 읽고 바꾸는 곳(결정 59). 실행 중에는 엔진의 것 그대로다 — 바꾼 값은 엔진이 스스로 저장해 다음 실행에도 남는다(Windows는 레지스트리의 Screenmanager 값).
    /// 캡처·시험은 값을 고정한 것으로 바꿔 넣는다(<see cref="GameFlow"/>의 window).
    /// </summary>
    public class WindowControl
    {
        /// <summary>모니터 크기(물리 px).</summary>
        public virtual Vector2Int Monitor => new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);

        /// <summary>지금 창 안쪽 크기.</summary>
        public virtual Vector2Int Size => new Vector2Int(Screen.width, Screen.height);

        public virtual bool Fullscreen => Screen.fullScreenMode != FullScreenMode.Windowed;

        /// <summary>창 모드로 그 크기, 또는 전체 화면(전체 화면 창 — 크기는 모니터 크기를 준다).</summary>
        public virtual void Set(int width, int height, bool fullscreen) =>
            Screen.SetResolution(width, height, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
    }

    /// <summary>값을 고정한 창(캡처·시험): 엔진의 창을 건드리지 않고, 바꾸면 값만 바뀐다.</summary>
    public sealed class FixedWindow : WindowControl
    {
        readonly Vector2Int monitor;
        Vector2Int size;
        bool fullscreen;

        public FixedWindow(Vector2Int monitor, Vector2Int size, bool fullscreen = false)
        {
            this.monitor = monitor;
            this.size = size;
            this.fullscreen = fullscreen;
        }

        public override Vector2Int Monitor => monitor;
        public override Vector2Int Size => size;
        public override bool Fullscreen => fullscreen;

        public override void Set(int width, int height, bool fullscreen)
        {
            size = new Vector2Int(width, height);
            this.fullscreen = fullscreen;
        }
    }
}
