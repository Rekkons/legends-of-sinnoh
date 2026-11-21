using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 웹 손님판 글 입력(02 §8 · isekai Client/UI/WebTextInput을 옮김 — isekai 00 §7 #149 G9 「한글 입력이 안 된다」). WebGL에서 UI Toolkit TextField는 IME 조합(한글)을 받지 못한다(Unity WebGL 제약).
    /// 그래서 WebGL 플레이어에서는 붙인 TextField에 포커스가 가면 같은 자리에 HTML &lt;input&gt;을 겹쳐 띄워(Client/Web/SinnohWeb.jslib) 입력을 받는다.
    /// - 입력 중: 매 프레임 HTML 글을 칸에 넣는다(최대 길이로 잘라서, 바뀔 때만 — 칸의 값 바뀜 알림이 그대로 돈다). 칸 자리가 바뀌면(창 크기 등) HTML 칸도 옮긴다.
    /// - Enter = 확정(onCommit → onEnter), 포커스 아웃·게임 화면 누름 = 확정(onCommit), Esc = 취소(연 때 값으로 되돌림). 칸이 화면에서 빠지거나 보이지 않게 되면(폭·높이 0, 조상 display: none) 그대로 닫는다.
    /// - 열린 동안 UI Toolkit 칸은 포커스를 놓고, Unity 키 입력 잡기(WebGLInput.captureAllKeyboardInput)를 끈다. 닫으면 되돌린다.
    /// Windows·에디터에서는 <see cref="Attach"/>가 아무것도 하지 않는다(지금 그대로). 값 반영 규칙은 <see cref="Edit"/>(시험이 직접 돌린다).
    /// </summary>
    public static class WebTextInput
    {
        /// <summary>HTML 입력 상태(jslib SinnohTextInput_State와 같다).</summary>
        public const int Closed = -1, Editing = 0, Enter = 1, Commit = 2, Cancel = 3;

        /// <summary>HTML 입력을 겹칠 수 있나: WebGL 플레이어에서만.</summary>
        public static bool Supported =>
#if UNITY_WEBGL && !UNITY_EDITOR
            true;
#else
            false;
#endif

        static Edit current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            poller = null;
            keyboardReleased = false;
        }

        static IVisualElementScheduledItem poller;
        static Rect placed;
        static bool keyboardReleased, savedCapture;

        /// <summary>지금 HTML 입력을 띄운 칸(없으면 null).</summary>
        public static Edit Current => current;

        /// <summary>
        /// 칸에 붙인다: WebGL 플레이어면 포커스가 갈 때 HTML 입력을 겹쳐 띄운다. onEnter = Enter 확정 뒤(칸 KeyDown Enter와 같은 일), onCommit = 확정 뒤(Enter·포커스 아웃).
        /// 붙였으면 true. 에디터·Windows는 아무것도 하지 않고 false.
        /// </summary>
        public static bool Attach(TextField field, Action onEnter = null, Action onCommit = null)
        {
            if (field == null || !Supported) return false;
            field.RegisterCallback<FocusInEvent>(_ => Open(field, onEnter, onCommit));
            return true;
        }

        /// <summary>최대 길이(0 이하 = 제한 없음)로 자른다. 서로게이트 쌍은 가르지 않는다. null은 "".</summary>
        public static string Clip(string text, int maxLength)
        {
            text = text ?? "";
            if (maxLength <= 0 || text.Length <= maxLength) return text;
            int n = maxLength;
            if (char.IsHighSurrogate(text[n - 1])) n--;
            return text.Substring(0, n);
        }

        /// <summary>칸 값을 글로 바꾼다(최대 길이로 잘라서). 바뀌었으면 true(같으면 알림 없이 그대로).</summary>
        public static bool Apply(TextField field, string text)
        {
            var v = Clip(text, field.maxLength);
            if (field.value == v) return false;
            field.value = v;
            return true;
        }

        /// <summary>패널 좌표의 상자를 패널 뿌리(= 캔버스) 기준 비율(0~1)로. 뿌리 크기가 없으면 Rect.zero.</summary>
        public static Rect Normalize(Rect box, Rect root)
        {
            if (root.width <= 0 || root.height <= 0) return Rect.zero;
            return new Rect((box.x - root.x) / root.width, (box.y - root.y) / root.height, box.width / root.width, box.height / root.height);
        }

        /// <summary>
        /// HTML 입력 하나의 값 반영 규칙. 매 프레임 <see cref="Step"/>에 HTML 상태와 글을 넣는다.
        /// 입력 중 = 글을 칸에, Enter·확정 = 글을 칸에 넣고 onCommit(Enter면 이어서 onEnter), 취소 = 연 때 값으로, 닫힘(칸이 사라짐) = 그대로.
        /// </summary>
        public sealed class Edit
        {
            public readonly TextField Field;
            /// <summary>열 때 칸 값(Esc 취소가 되돌릴 값).</summary>
            public readonly string Original;
            readonly Action onEnter, onCommit;

            public bool Done { get; private set; }

            public Edit(TextField field, Action onEnter = null, Action onCommit = null)
            {
                Field = field;
                Original = field.value ?? "";
                this.onEnter = onEnter;
                this.onCommit = onCommit;
            }

            /// <summary>HTML 상태·글 하나를 넣는다. 끝났으면 true(그 뒤 Step은 아무것도 하지 않는다).</summary>
            public bool Step(int state, string text)
            {
                if (Done) return true;
                switch (state)
                {
                    case Editing:
                        Apply(Field, text);
                        return false;
                    case Enter:
                    case Commit:
                        Done = true;
                        Apply(Field, text);
                        onCommit?.Invoke();
                        if (state == Enter) onEnter?.Invoke();
                        return true;
                    case Cancel:
                        Done = true;
                        Apply(Field, Original);
                        return true;
                    default:
                        Done = true;
                        return true;
                }
            }
        }

        // ── WebGL 플레이어에서만 도는 부분 ──

        static VisualElement Box(TextField f) => f.Q(className: TextField.inputUssClassName) ?? f;

        static Rect Where(TextField f) => Normalize(Box(f).worldBound, f.panel.visualTree.worldBound);

        /// <summary>입력 상자가 화면에 보이지 않나: 폭·높이가 0 이하이거나 NaN(레이아웃이 아직 없음).</summary>
        public static bool BoxHidden(Rect box) =>
            float.IsNaN(box.width) || float.IsNaN(box.height) || box.width <= 0 || box.height <= 0;

        /// <summary>
        /// 칸이 보이지 않나: 입력 상자가 <see cref="BoxHidden"/>이거나 칸·조상 중 하나가 display: none(탭·창을 접어 감춘 경우 — 패널에는 아직 붙어 있다).
        /// 보이지 않는 칸 자리에 HTML 입력이 떠 있으면 안 되므로 Poll이 닫는다.
        /// </summary>
        public static bool Hidden(TextField f)
        {
            if (BoxHidden(Box(f).worldBound)) return true;
            for (VisualElement v = f; v != null; v = v.parent)
                if (v.resolvedStyle.display == DisplayStyle.None || (v.style.display.keyword == StyleKeyword.Undefined && v.style.display.value == DisplayStyle.None))
                    return true;
            return false;
        }

        static void Open(TextField field, Action onEnter, Action onCommit)
        {
            if (current != null && current.Field == field) return;
            var root = field.panel?.visualTree;
            if (root == null) return;
            if (current != null) Finish(Commit);   // 다른 칸이 열려 있었으면 그 칸을 먼저 확정

            current = new Edit(field, onEnter, onCommit);
            placed = Where(field);
            Native.Open(field.value ?? "", field.maxLength, field.textEdition.placeholder ?? "", placed);
            if (!keyboardReleased)
            {
                savedCapture = Native.KeyboardCaptured;
                Native.SetKeyboardCapture(false);   // 키를 HTML 입력이 받게
                keyboardReleased = true;
            }
            field.schedule.Execute(() => field.Blur());   // UI Toolkit 칸은 포커스를 놓는다(다시 누르면 또 열린다)
            poller = root.schedule.Execute(Poll).Every(16);
        }

        static void Poll()
        {
            var e = current;
            if (e == null) { End(); return; }
            if (e.Field.panel == null || Hidden(e.Field)) { Finish(Closed); return; }   // 화면이 바뀌어 칸이 빠졌거나, 패널에는 남았어도 감춰져 보이지 않는다
            var r = Where(e.Field);
            if (r != placed)
            {
                placed = r;
                Native.Place(r);
            }
            int state = Native.State();
            if (state == Editing) e.Step(Editing, Native.Text());
            else Finish(state);
        }

        // 마지막 글을 읽고 HTML 입력을 닫은 뒤 값·콜백을 처리한다(콜백이 다른 칸을 열어도 되게)
        static void Finish(int state)
        {
            var e = current;
            if (e == null) return;
            var text = Native.Text();
            End();
            e.Step(state, text);
        }

        static void End()
        {
            current = null;
            poller?.Pause();
            poller = null;
            Native.Close();
            if (keyboardReleased)
            {
                Native.SetKeyboardCapture(savedCapture);
                keyboardReleased = false;
            }
        }

        static class Native
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            [DllImport("__Internal")] static extern void SinnohTextInput_Open(string value, int maxLength, string placeholder, float x, float y, float w, float h);
            [DllImport("__Internal")] static extern void SinnohTextInput_Place(float x, float y, float w, float h);
            [DllImport("__Internal")] static extern int SinnohTextInput_State();
            [DllImport("__Internal")] static extern int SinnohTextInput_Text(byte[] buffer, int size);
            [DllImport("__Internal")] static extern void SinnohTextInput_Close();

            static byte[] buffer = new byte[256];

            public static void Open(string value, int maxLength, string placeholder, Rect r) =>
                SinnohTextInput_Open(value, maxLength, placeholder, r.x, r.y, r.width, r.height);
            public static void Place(Rect r) => SinnohTextInput_Place(r.x, r.y, r.width, r.height);
            public static int State() => SinnohTextInput_State();
            public static string Text()
            {
                int n = SinnohTextInput_Text(buffer, buffer.Length);
                if (n >= buffer.Length)
                {
                    buffer = new byte[n + 1];
                    n = SinnohTextInput_Text(buffer, buffer.Length);
                }
                return Encoding.UTF8.GetString(buffer, 0, n);
            }
            public static void Close() => SinnohTextInput_Close();
#if SINNOH_COMPILE_CHECK   // PlayerCompileCheck.RunWebGL: 활성 대상이 아닌 WebGL 스크립트 컴파일은 UnityEngine.WebGLModule을 참조하지 않아 WebGLInput이 거짓 에러다(실제 WebGL 빌드는 아래 줄)
            public static bool KeyboardCaptured => false;
            public static void SetKeyboardCapture(bool on) { }
#else
            public static bool KeyboardCaptured => WebGLInput.captureAllKeyboardInput;
            public static void SetKeyboardCapture(bool on) => WebGLInput.captureAllKeyboardInput = on;
#endif
#else
            public static void Open(string value, int maxLength, string placeholder, Rect r) { }
            public static void Place(Rect r) { }
            public static int State() => Closed;
            public static string Text() => "";
            public static void Close() { }
            public static bool KeyboardCaptured => true;
            public static void SetKeyboardCapture(bool on) { }
#endif
        }
    }
}
