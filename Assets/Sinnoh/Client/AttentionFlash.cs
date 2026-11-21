using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 창이 비활성일 때 내 차례 · 응답 요청 알림(04 §5 ① 「차례 알림」, isekai Client/Platform/AttentionFlash를 옮김 — S02 「차례 알림」으로 끈다 — 결정 112 Q17, 기본 켬 · <see cref="SoundPrefs.Alert"/>).
    /// - 언제: 방 판에서 내 좌석에 새 선택(PendingChoice Id)이 열렸을 때(<see cref="RoomFlow"/>.Tick이 <see cref="Changed"/>로 한 번씩 거른다). 솔로 · 튜토리얼은 부르지 않는다.
    /// - Windows 빌드: user32 FlashWindowEx(작업 표시줄 깜빡임, FLASHW_TIMERNOFG — 창이 앞으로 오면 Windows가 멈춘다). 창이 활성이면 하지 않는다. 포커스를 가져가지 않는다.
    /// - WebGL: Client/Web/SinnohWeb.jslib가 탭 제목을 「● 내 차례 — 원래 제목」과 번갈아 바꾼다(페이지에 포커스가 오면 멈춤).
    /// - 에디터: 아무것도 하지 않는다(<see cref="Sink"/>가 있으면 그것을 부른다 — 시험).
    /// </summary>
    public static class AttentionFlash
    {
        /// <summary>웹 탭 제목 앞에 붙는 글자(화면 글이 아니라 브라우저 탭 — 언어 고르기 전까지 한국어 고정).</summary>
        public const string TitlePrefix = "● 내 차례 — ";

        static int lastChoice = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lastChoice = -1;
            Sink = null;
        }

        /// <summary>시험용: 플랫폼 깜빡임 대신 부른다. null이면 플랫폼 깜빡임.</summary>
        public static Action Sink;

        /// <summary>새로 열린 내 선택인가(같은 Id는 한 번만). choiceId &lt; 0 = 열린 선택 없음(다음 선택을 새것으로 본다).</summary>
        public static bool Changed(int choiceId)
        {
            if (choiceId < 0) { lastChoice = -1; return false; }
            if (choiceId == lastChoice) return false;
            lastChoice = choiceId;
            return true;
        }

        /// <summary>지금 한 번 깜빡인다(창이 활성이면 하지 않는다). 깜빡였으면 true.</summary>
        public static bool Now()
        {
            if (Sink != null) { Sink(); return true; }
#if UNITY_EDITOR
            return false;
#elif UNITY_WEBGL
            SinnohAttentionStart(TitlePrefix);
            return true;
#elif UNITY_STANDALONE_WIN
            if (Application.isFocused) return false;
            FlashTaskbar();
            return true;
#else
            return false;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SinnohAttentionStart(string prefix);
#endif

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential)]
        struct FlashInfo
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        const uint FlashAll = 3, FlashTimerNoFg = 12;   // FLASHW_ALL | FLASHW_TIMERNOFG: 창이 앞으로 올 때까지 깜빡인다

        [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FlashInfo info);
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string windowName);

        static IntPtr hwnd;

        // 창 핸들은 창이 활성일 때 잡아 둔다(비활성일 때 GetActiveWindow는 0). 못 잡았으면 Unity 창 클래스·제품 이름으로 찾는다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            Remember(Application.isFocused);
            Application.focusChanged += Remember;
        }

        static void Remember(bool focused)
        {
            if (!focused) return;
            var h = GetActiveWindow();
            if (h != IntPtr.Zero) hwnd = h;
        }

        static void FlashTaskbar()
        {
            var h = hwnd != IntPtr.Zero ? hwnd : FindWindow("UnityWndClass", Application.productName);
            if (h == IntPtr.Zero) return;
            var info = new FlashInfo
            {
                cbSize = (uint)Marshal.SizeOf(typeof(FlashInfo)),
                hwnd = h,
                dwFlags = FlashAll | FlashTimerNoFg,
                uCount = uint.MaxValue,
                dwTimeout = 0,
            };
            FlashWindowEx(ref info);
        }
#endif
    }
}
