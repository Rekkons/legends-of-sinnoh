using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Sinnoh.Client
{
    /// <summary>
    /// 확인·시험용 가상 마우스·키보드(plan §4 「마우스 조작 확인」): Input System에 장치 둘을 더하고 상태 이벤트만 넣는다 —
    /// 그 뒤는 실제 장치와 같은 길이다(UI Toolkit 실행 중 패널의 포인터 이벤트 · <c>Mouse.current</c> · <c>Keyboard.current</c>).
    /// 넣은 이벤트는 다음 프레임의 입력 갱신에서 읽히고 패널은 그 프레임에 받는다 — 넣은 뒤 두세 프레임을 보내고 본다.
    /// 좌표는 물리 px, 왼쪽 아래가 (0, 0). 게임 코드는 이것을 쓰지 않는다.
    /// </summary>
    public sealed class VirtualInput : IDisposable
    {
        public readonly Mouse Mouse;
        public readonly Keyboard Keys;

        /// <summary>마지막으로 넣은 마우스 자리.</summary>
        public Vector2 At { get; private set; }

        public VirtualInput()
        {
            Mouse = InputSystem.AddDevice<Mouse>("SinnohVirtualMouse");
            Keys = InputSystem.AddDevice<Keyboard>("SinnohVirtualKeyboard");
        }

        /// <summary>마우스 상태 하나: 자리 · 왼쪽 단추가 눌려 있나 · 이번 이벤트의 휠(Windows는 한 칸에 120) · 오른쪽 단추가 눌려 있나.</summary>
        public void Point(Vector2 px, bool down = false, float wheel = 0f, bool right = false)
        {
            At = px;
            var state = new MouseState { position = px, scroll = new Vector2(0f, wheel) };
            if (down) state = state.WithButton(MouseButton.Left);
            if (right) state = state.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(Mouse, state);
        }

        /// <summary>키보드 상태 하나: 그 키만 눌려 있다.</summary>
        public void Hold(Key key) => InputSystem.QueueStateEvent(Keys, new KeyboardState(key));

        /// <summary>키보드 상태 하나: 아무 키도 눌려 있지 않다.</summary>
        public void Release() => InputSystem.QueueStateEvent(Keys, new KeyboardState());

        public void Dispose()
        {
            if (Mouse.added) InputSystem.RemoveDevice(Mouse);
            if (Keys.added) InputSystem.RemoveDevice(Keys);
        }
    }
}
