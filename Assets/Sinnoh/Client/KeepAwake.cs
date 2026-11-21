using System.Runtime.InteropServices;
using Sinnoh.Net;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 방이 열려 있는 동안 PC 절전 막기(isekai Client/Platform/KeepAwake를 옮김 — isekai 02 §5-2).
    /// - 언제: 방장은 방을 연 뒤 닫을 때까지, 손님은 접속 시도부터 끊김(Failed)·나감(Closed)까지. 재접속 중에도 막는다(<see cref="Wanted"/>).
    /// - 호출: <see cref="RoomFlow"/>.Tick이 매 프레임 <see cref="Set"/>(Wanted(...)), 방을 버릴 때 Set(false). 앱이 끝날 때도 끈다.
    /// - Windows 독립 실행만: kernel32 SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED), 끝나면 ES_CONTINUOUS만으로 되돌린다.
    ///   화면 끄기(ES_DISPLAY_REQUIRED)는 막지 않는다. 에디터·WebGL은 OS에 알리지 않는다(<see cref="On"/>만 바뀐다).
    /// </summary>
    public static class KeepAwake
    {
        /// <summary>절전을 막을 상태인가: 방장 방이 열려 있거나(host), 손님 연결이 접속 중·접속됨·재접속 중.</summary>
        public static bool Wanted(bool host, NetStatus status) =>
            host || status == NetStatus.Connecting || status == NetStatus.Connected || status == NetStatus.Reconnecting;

        /// <summary>마지막으로 요청한 상태(true = 절전 막는 중).</summary>
        public static bool On { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => On = false;

        /// <summary>바뀔 때만 OS에 알린다. 매 프레임 불러도 된다.</summary>
        public static void Set(bool on)
        {
            if (on == On) return;
            On = on;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            SetThreadExecutionState(on ? EsContinuous | EsSystemRequired : EsContinuous);
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001;

        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() => Application.quitting += () => Set(false);
#endif
    }
}
