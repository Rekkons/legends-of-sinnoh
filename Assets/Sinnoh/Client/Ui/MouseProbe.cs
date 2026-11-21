using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Rekkon.UiKit;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 플레이어에서 마우스·키보드 길을 끝까지 확인한다(plan §4 「마우스 조작 확인」). 명령줄 <c>-sinnohMouseProbe &lt;폴더&gt;</c>로 띄운 플레이어에서만 켜진다.
    /// Input System에 가상 마우스·키보드 장치를 더하고(<c>InputSystem.AddDevice</c>) 상태 이벤트만 넣는다(<c>QueueStateEvent</c>) — 화면의 함수나 <see cref="GameFlow.Pointer"/>를 직접 부르지 않는다.
    /// 그래서 지나는 길은 사람이 마우스를 움직일 때와 같다: 장치 → UI Toolkit 실행 중 패널(단추·툴팁) / <c>Mouse.current</c> → <see cref="ScreenBoot"/> → 칸 고르기 / <see cref="BoardCameraRig"/>(Q·E·휠).
    /// 흐름: 타이틀 올리기·누르기 → 솔로 설정(툴팁) → 고향 마을 → 스타팅 포켓몬·트레이너 카드 → 보드에서 회색 단추 툴팁 → 칸 올리기·누르기(걷기) → HUD 위 휠(줌 그대로)·보드 위 휠(줌 바뀜) →
    /// E 회전 + 가까운 줌에서 다시 칸 누르기 → [턴 종료] → (창이 있으면) 창 크기 바꾸기 → [메뉴] → 메뉴 뒤 칸·Q는 듣지 않음 → 나가기 → 타이틀 →
    /// 설정(S02, 결정 59): 「화면 크기」의 값을 차례로 누르면 창 크기·등급이 따라오고 「전체 화면」도 된다(창이 있을 때만 — 창이 없으면 열고 닫기만) → [뒤로] → 타이틀.
    /// 띄우는 법 둘(<c>Tools/window-probe.mjs</c>): <c>-batchmode</c>(창 없음 — 그림은 패널을 잠깐 RT에 그려 보드 위에 겹친다. 마우스 좌표는 <c>Screen</c> 크기 기준) ·
    /// 창 모드(백버퍼를 그대로 찍고, 패널이 보드 위에 겹쳐 그려졌는지와 창 크기 → 등급을 본다. 사용자 화면에 창이 뜨지 않게 숨은 데스크톱에서 띄운다).
    /// <c>-sinnohMouseSizes 1280x720,1920x1080,…</c> = 창 모드에서 차례로 바꿔 볼 크기(<c>Screen.SetResolution</c>).
    /// 입력 설정(포커스 없을 때의 동작 등)은 바꾸지 않는다 — 두 방식 모두 <c>Application.isFocused</c>가 참이라 풀 것이 없었다(JSON <c>focused</c>). 확인 중 <c>runInBackground</c>만 켠다.
    /// 게임 시드는 고정이다(<see cref="ScreenBoot.Entropy"/>). 출력: &lt;폴더&gt;/mouse-probe.json(ok · fails · 단계별 값), mouse-&lt;단계&gt;.png(분홍 십자 = 그때의 마우스 자리).
    /// 종료 코드 0 = 모두 맞음 · 1 = 어긋남 · 2 = 시간 초과.
    /// </summary>
    public sealed class MouseProbe : MonoBehaviour
    {
        public const string Arg = "-sinnohMouseProbe", SizesArg = "-sinnohMouseSizes";

        /// <summary>솔로 설정에서 누를 주인공(기본값이 아닌 것). 조우·포획 판도 이 주인공으로 시작한다 — 고른 주인공 = 볼 던지는 뒷모습.</summary>
        public const string ProbeProtagonist = "b2w2_001_rosa";

        const float TimeoutSeconds = 300f;
        const ulong Seed = 20261112, AiSeed = 7;

        string outDir;
        readonly List<Vector2Int> sizes = new List<Vector2Int>();
        readonly List<string> errors = new List<string>();
        readonly List<string> fails = new List<string>();
        readonly List<string> ignored = new List<string>();
        readonly StringBuilder steps = new StringBuilder();
        readonly StringBuilder head = new StringBuilder();
        readonly CultureInfo inv = CultureInfo.InvariantCulture;

        VirtualInput input;
        ScreenBoot boot;
        ScreenRoot root;
        UiStage stage;
        GameFlow flow;
        Camera cam;
        BoardCameraRig rig;
        BoardBoot board;
        Vector2 at;
        bool windowed;
        float t0;
        int seedCalls;
        string lastStep = "";
        bool finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == Arg)
                {
                    var probe = new GameObject("MouseProbe").AddComponent<MouseProbe>();
                    probe.outDir = args[i + 1];
                    for (int k = 0; k + 1 < args.Length; k++)
                        if (args[k] == SizesArg)
                            foreach (string part in args[k + 1].Split(','))
                            {
                                string[] wh = part.Split('x');
                                if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h)) probe.sizes.Add(new Vector2Int(w, h));
                            }
                    // 화면 시작점이 흐름을 만들기(Start) 전에 고정 시드를 넣는다
                    var screens = FindAnyObjectByType<ScreenBoot>();
                    if (screens != null && screens.Flow == null) screens.Entropy = probe.NextSeed;
                    return;
                }
        }

        ulong NextSeed() => seedCalls++ == 0 ? Seed : AiSeed + (ulong)(seedCalls - 2);

        void Awake()
        {
            windowed = !Application.isBatchMode;
            Application.logMessageReceived += Grab;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= Grab;
            input?.Dispose();
        }

        void Grab(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            // 숨은 데스크톱(입력 데스크톱이 아니다)에서는 엔진이 시작할 때 OS 커서 자리를 얻지 못해 이 줄을 낸다 — 게임 탓이 아니라 세지 않고 적어만 둔다
            if (windowed && message.StartsWith("Failed to get first mouse position", StringComparison.Ordinal)) ignored.Add(message.Trim());
            else errors.Add(type + ": " + message);
        }

        // ── 가상 장치 ──

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        void Queue(Vector2 px, bool down, float wheel = 0f)
        {
            at = px;
            input.Point(px, down, wheel);
        }

        IEnumerator Move(Vector2 px)
        {
            Queue(px, false);
            yield return Frames(3);
        }

        IEnumerator Click(Vector2 px)
        {
            yield return Move(px);
            Queue(px, true);
            yield return Frames(2);
            Queue(px, false);
            yield return Frames(3);
        }

        // 왼쪽 단추를 누른 채 from에서 to로 끈다(단추는 누른 채로 둔다 — 부른 쪽이 Queue(at, false)로 뗀다)
        IEnumerator Drag(Vector2 from, Vector2 to, int steps = 4)
        {
            yield return Move(from);
            Queue(from, true);
            yield return Frames(2);
            for (int i = 1; i <= steps; i++)
            {
                Queue(Vector2.Lerp(from, to, i / (float)steps), true);
                yield return Frames(2);
            }
        }

        // 휠 한 칸(Windows는 한 칸에 120). 같은 자리에서 굴린다
        IEnumerator Wheel(float notches)
        {
            Queue(at, false, notches * 120f);
            yield return Frames(2);
            Queue(at, false);
            yield return Frames(2);
        }

        // 오른쪽 단추를 끌지 않고 눌렀다 뗀다(걷기 전 확인의 한 단계 물리기 — 결정 71 ②)
        IEnumerator RightClick(Vector2 px)
        {
            yield return Move(px);
            at = px;
            input.Point(px, false, 0f, true);
            yield return Frames(2);
            input.Point(px, false);
            yield return Frames(3);
        }

        IEnumerator Tap(Key key)
        {
            input.Hold(key);
            yield return Frames(2);
            input.Release();
            yield return Frames(2);
        }

        static IEnumerator Wait(Func<bool> done, float seconds)
        {
            float start = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - start < seconds) yield return null;
        }

        // ── 화면 좌표 ──

        /// <summary>요소 가운데의 마우스 좌표(물리 px, 왼쪽 아래가 (0, 0)).</summary>
        Vector2 Px(VisualElement e)
        {
            var c = e.worldBound.center;
            float s = stage.Grade.PanelScale;
            return new Vector2(c.x * s, root.Size.y - c.y * s);
        }

        // 요소의 가로 한 점(0 = 왼쪽 끝 · 1 = 오른쪽 끝)의 창 좌표 — 슬라이더의 홈. 1 = 오른쪽 경계 그 자리라 누르면 요소 밖으로 빠질 수 있다(누를 때는 PxIn)
        Vector2 PxAt(VisualElement e, float along)
        {
            var b = e.worldBound;
            float s = stage.Grade.PanelScale;
            return new Vector2((b.xMin + b.width * along) * s, root.Size.y - b.center.y * s);
        }

        /// <summary>패널 px로 요소 안쪽에 남기는 틈(슬라이더의 양 끝을 누를 때) — 앞 감사(m4-3-answers)에서 오른쪽 끝 바로 바깥을 눌러 실패했다.</summary>
        const float EdgeInset = 2f;

        // 요소 안의 가로 한 점(PxAt과 같되 양 끝에서 EdgeInset만큼 안쪽 — 0 = 안쪽 왼쪽 끝 · 1 = 안쪽 오른쪽 끝)
        Vector2 PxIn(VisualElement e, float along)
        {
            var b = e.worldBound;
            float s = stage.Grade.PanelScale;
            float x = Mathf.Clamp(b.xMin + b.width * along, b.xMin + EdgeInset, b.xMax - EdgeInset);
            return new Vector2(x * s, root.Size.y - b.center.y * s);
        }

        VisualElement Q(string name) => stage.Root.Q(name);

        /// <summary>요소가 있고 배치가 끝나 자리가 정해졌나(막 만든 요소는 다음 프레임까지 자리가 없다 — 그때 누르면 좌표가 NaN이다).</summary>
        static bool Laid(VisualElement e) => e != null && !float.IsNaN(e.worldBound.width) && e.worldBound.width > 0f;

        struct Spot
        {
            public int Cell;
            public Vector2 Px;
        }

        /// <summary>
        /// 갈 수 있는 칸 가운데 화면에서 바로 고를 수 있는 칸(가운데가 창 안 · HUD 판 밖 · 앞의 높은 칸에 가리지 않음)과 그 마우스 좌표.
        /// HUD 판 밖인데 PC 층이 마우스를 받으면 실패로 친다(조용히 건너뛰지 않는다). 내 셈(<see cref="BoardPick.Screen"/>)과 엔진 카메라(<c>WorldToScreenPoint</c>)의 차도 잰다.
        /// </summary>
        List<Spot> OpenCells(string when, out float maxOff)
        {
            var open = new List<Spot>();
            maxOff = 0f;
            int w = root.Size.x, h = root.Size.y;
            float scale = stage.Grade.PanelScale;
            foreach (int cell in flow.Hud.Reach)
            {
                if (!BoardPick.Screen(board, cam, cell, w, h, out var px) || px.x < 0 || px.x >= w || px.y < 0 || px.y >= h) continue;
                if (BoardHud.UnderPanel(stage.Root, new Vector2(px.x / scale, (h - px.y) / scale))) continue;
                if (flow.OverUi(px.x, px.y))
                {
                    Fail(when, "칸 " + cell + " (" + px.x.ToString("0.#", inv) + ", " + px.y.ToString("0.#", inv) + ")은 HUD 판 밖인데 PC 층이 마우스를 받는다");
                    continue;
                }
                if (BoardPick.Cell(board, cam, px.x, px.y, w, h) != cell) continue;   // 앞의 높은 칸에 가렸다
                var engine = cam.WorldToScreenPoint(board.CellTop(cell));
                maxOff = Mathf.Max(maxOff, Vector2.Distance(px, new Vector2(engine.x, engine.y)));
                open.Add(new Spot { Cell = cell, Px = px });
            }
            return open;
        }

        // ── 기록 ──

        void Fail(string step, string what) => fails.Add(step + ": " + what);

        bool Check(string step, bool ok, string what)
        {
            if (!ok) Fail(step, what);
            return ok;
        }

        static string Esc(string s) => (s ?? "").Replace("\\", "/").Replace("\"", "'").Replace("\n", " ").Replace("\r", " ");

        /// <summary>단계 한 줄(JSON 객체). kv = 이름, 값, 이름, 값 … (값은 bool·수·문자열).</summary>
        void Step(string name, params object[] kv)
        {
            lastStep = name;
            steps.Append(steps.Length > 0 ? "," : "").Append("{\"step\":\"").Append(name).Append('"');
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                steps.Append(",\"").Append(kv[i]).Append("\":");
                object v = kv[i + 1];
                if (v is bool b) steps.Append(b ? "true" : "false");
                else if (v is int n) steps.Append(n.ToString(inv));
                else if (v is float f) steps.Append(f.ToString("0.###", inv));
                else if (v is Vector2 p) steps.Append('[').Append(p.x.ToString("0.#", inv)).Append(',').Append(p.y.ToString("0.#", inv)).Append(']');
                else steps.Append('"').Append(Esc(v != null ? v.ToString() : "")).Append('"');
            }
            steps.Append('}');
        }

        bool TimedOut => Time.realtimeSinceStartup - t0 > TimeoutSeconds;

        // ── 그림 ──

        /// <summary>
        /// 지금 화면을 PNG로 남긴다(분홍 십자 = 마우스 자리). 창이 있으면 백버퍼 그대로, 없으면 패널을 잠깐 RT에 그려 보드 위에 겹친다(<see cref="ScreenProbe.Snap"/>).
        /// 창이 있을 때 돌려주는 값: uiOver = 보드만 그린 그림과 달라진 픽셀(패널이 덮은 곳), boardThrough = 보드만 그린 그림과 같은 보드 픽셀(패널 밑으로 보드가 비치는 곳). 창이 없으면 둘 다 −1.
        /// </summary>
        IEnumerator Snap(string name, Action<int, int> overlap = null)
        {
            int w = root.Size.x, h = root.Size.y;
            string path = Path.Combine(outDir, "mouse-" + name + ".png");
            Texture2D tex;
            int uiOver = -1, boardThrough = -1;
            if (windowed)
            {
                yield return new WaitForEndOfFrame();
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (overlap != null)
                {
                    var shot = tex.GetPixels32();
                    var boardRt = new RenderTexture(tex.width, tex.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    boardRt.Create();
                    var alone = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                    OffscreenShot.Render(cam, boardRt, alone);
                    OffscreenShot.Render(cam, boardRt, alone);
                    var only = alone.GetPixels32();
                    Color32 bg = cam.backgroundColor;
                    uiOver = boardThrough = 0;
                    for (int p = 0; p < shot.Length && p < only.Length; p++)
                    {
                        int diff = Mathf.Abs(shot[p].r - only[p].r) + Mathf.Abs(shot[p].g - only[p].g) + Mathf.Abs(shot[p].b - only[p].b);
                        bool isBoard = Mathf.Abs(only[p].r - bg.r) + Mathf.Abs(only[p].g - bg.g) + Mathf.Abs(only[p].b - bg.b) > 12;
                        if (diff > 36) uiOver++;
                        else if (isBoard && diff <= 12) boardThrough++;
                    }
                    boardRt.Release();
                    Destroy(boardRt);
                    Destroy(alone);
                }
            }
            else
            {
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                root.SetTarget(rt);
                yield return Frames(3);
                tex = ScreenProbe.Compose(cam, rt, out _, out _, out _);
                root.SetTarget(null);
                rt.Release();
                Destroy(rt);
                yield return Frames(2);
            }
            // 마우스 자리 십자
            int cx = Mathf.RoundToInt(at.x * tex.width / Mathf.Max(1, w)), cy = Mathf.RoundToInt(at.y * tex.height / Mathf.Max(1, h));
            var pink = new Color32(255, 0, 255, 255);
            for (int d = -9; d <= 9; d++)
                for (int t = -1; t <= 1; t++)
                {
                    if (Mathf.Abs(d) < 3) continue;   // 가운데는 비워 밑이 보이게
                    Dot(tex, cx + d, cy + t, pink);
                    Dot(tex, cx + t, cy + d, pink);
                }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            overlap?.Invoke(uiOver, boardThrough);
        }

        static void Dot(Texture2D tex, int x, int y, Color32 c)
        {
            if (x >= 0 && y >= 0 && x < tex.width && y < tex.height) tex.SetPixel(x, y, c);
        }

        // ── 흐름 ──

        IEnumerator Start()
        {
            t0 = Time.realtimeSinceStartup;
            return Guard(Run());
        }

        // 흐름이 어디서 멈춰 있어도(끝나지 않는 기다림) 시간이 지나면 그때까지의 단계를 적고 끝낸다
        void Update()
        {
            if (finished || !TimedOut) return;
            Fail("시간", "시간 초과 — 마지막으로 적은 단계 「" + lastStep + "」");
            Finish(2, head.ToString());
        }

        /// <summary>
        /// 흐름을 한 걸음씩 돌리며 예외를 받는다 — 요소 이름이 어긋나 예외가 나면 코루틴이 조용히 멈추는 대신 그때까지의 단계와 까닭을 적고 끝낸다(종료 코드 1).
        /// 안쪽 흐름(<c>yield return Move(…)</c> 등)도 여기서 직접 돌린다(엔진에 넘기면 그 안의 예외를 받지 못한다).
        /// </summary>
        IEnumerator Guard(IEnumerator run)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(run);
            while (stack.Count > 0 && !finished)
            {
                bool more;
                object current = null;
                try
                {
                    more = stack.Peek().MoveNext();
                    if (more) current = stack.Peek().Current;
                }
                catch (Exception e)
                {
                    Fail("예외", "마지막으로 적은 단계 「" + lastStep + "」 뒤에 흐름이 끊겼다 — " + e);
                    Finish(1, head.ToString());
                    yield break;
                }
                if (!more) stack.Pop();
                else if (current is IEnumerator inner) stack.Push(inner);
                else yield return current;
            }
        }

        IEnumerator Run()
        {
            windowed = !Application.isBatchMode;
            bool runInBackgroundWas = Application.runInBackground;
            Application.runInBackground = true;   // 포커스가 없어도 프레임이 돌게(확인 중에만)
            while (!TimedOut)
            {
                boot = FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null && boot.Root.Stage.Root.Q("title") != null) break;
                yield return null;
            }
            root = boot != null ? boot.Root : null;
            if (root == null || root.Stage.Root == null || root.Stage.Root.Q("title") == null)
            {
                Fail("시작", "타이틀이 열리지 않았다");
                Finish(2, "");
                yield break;
            }
            stage = root.Stage;
            flow = boot.Flow;
            cam = Camera.main;
            rig = cam.GetComponent<BoardCameraRig>();
            board = FindAnyObjectByType<BoardBoot>();
            Directory.CreateDirectory(outDir);
            yield return Frames(10);

            // 가상 장치. 입력 설정은 건드리지 않는다 — 포커스가 없으면 Input System·UI Toolkit이 입력을 버리므로 그때는 아래 첫 단계가 실패하고 까닭(focused)이 JSON에 남는다
            int nativeMice = 0;
            foreach (var d in InputSystem.devices) if (d is Mouse) nativeMice++;
            bool focused = Application.isFocused;
            input = new VirtualInput();
            yield return Frames(3);
            head.Append("\"mode\":\"").Append(windowed ? "window" : "batchmode").Append('"')
                .Append(",\"screen\":[").Append(Screen.width).Append(',').Append(Screen.height).Append(']')
                .Append(",\"fullScreenMode\":\"").Append(Screen.fullScreenMode).Append('"')
                .Append(",\"grade\":").Append(stage.Grade.Height)
                .Append(",\"seed\":").Append(Seed)
                .Append(",\"nativeMice\":").Append(nativeMice)
                .Append(",\"focused\":").Append(focused ? "true" : "false")
                .Append(",\"runInBackgroundSetting\":").Append(runInBackgroundWas ? "true" : "false")
                .Append(",\"backgroundBehavior\":\"").Append(InputSystem.settings.backgroundBehavior).Append('"');
            if (flow == null || flow.Ctx == null || rig == null || board == null)
            {
                Fail("시작", "화면 흐름·게임 데이터·보드가 없다");
                Finish(1, head.ToString());
                yield break;
            }
            if (windowed && sizes.Count > 0 && (Screen.width != sizes[0].x || Screen.height != sizes[0].y))
            {
                Screen.SetResolution(sizes[0].x, sizes[0].y, FullScreenMode.Windowed);
                yield return Wait(() => Screen.width == sizes[0].x && Screen.height == sizes[0].y && root.Size == sizes[0], 5f);
                yield return Frames(5);
            }

            // ── ① 타이틀: 올리면 그 줄이 골라지고(PointerEnter), 누르면 솔로 설정 ──
            var title = stage.Current as TitleScreen;
            int creditsIndex = -1;
            if (title != null) for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Credits) creditsIndex = i;
            int pickedWas = title != null ? title.Picked : -1;
            var creditsPx = Px(Q("title-item-" + TitleScreen.Credits));
            // 패널이 받은 움직임 수를 센다. 화면 가운데로 한 번 옮긴 뒤(장치를 더하고 처음 넣은 이벤트가 패널에 갔는지 따로 적는다) 줄 위로 옮긴다
            int panelMoves = 0;
            EventCallback<PointerMoveEvent> count = e => panelMoves++;
            stage.Root.RegisterCallback(count, TrickleDown.TrickleDown);
            yield return Move(new Vector2(root.Size.x * 0.5f, root.Size.y * 0.5f));
            int firstMoves = panelMoves;
            yield return Move(creditsPx);
            stage.Root.UnregisterCallback(count, TrickleDown.TrickleDown);
            bool hoverOk = title != null && title.Picked == creditsIndex && creditsIndex != pickedWas;
            bool current = Mouse.current == input.Mouse;
            Vector2 read = Mouse.current != null ? Mouse.current.position.ReadValue() : default;
            Step("title-hover", "ok", hoverOk, "at", creditsPx, "picked", title != null ? title.Picked : -1, "want", creditsIndex, "mouseCurrentIsProbe", current, "mousePosition", read, "panelMovesAfterFirstEvent", firstMoves, "panelMoves", panelMoves);
            Check("타이틀 올리기", hoverOk, "마우스를 올린 줄이 골라지지 않았다 — 실행 중 패널이 마우스 움직임을 받지 못한다(올린 줄 " + (title != null ? title.Picked : -1) + " ≠ " + creditsIndex + ", 패널이 받은 움직임 " + panelMoves + ", 포커스 " + focused + ")");
            Check("타이틀 올리기", current && Vector2.Distance(read, creditsPx) < 0.5f, "Mouse.current가 넣은 자리를 읽지 않는다(" + read + " ≠ " + creditsPx + ")");
            yield return Snap("01-title-hover");
            if (!hoverOk) { Finish(1, head.ToString()); yield break; }   // 패널이 입력을 받지 못하면 뒤는 볼 것이 없다

            // ── ①-2 방 만들기 · 참가하기(M7 화면 ①): 타이틀 → S05 → [직접 연결] → [뒤로] · 타이틀 → S06 → 빈 칸 [참가] → 주소 형식 안내 → [뒤로].
            //    방은 열지 않는다(방화벽 창 · UGS 없이) — 방 목록은 빈 가짜(실제 UGS Lobby를 부르지 않는다)
            flow.Room.Board = RoomFlow.NoRooms;
            yield return Click(Px(Q("title-item-" + TitleScreen.Host)));
            bool createOpen = stage.Current is RoomCreateScreen;
            yield return Frames(3);
            var direct = Q("room-create-opt-mode-direct");
            if (Laid(direct)) yield return Click(Px(direct));
            bool directPicked = createOpen && !flow.Room.Create.Internet && Q("room-create-opt-mode-direct") != null && Q("room-create-opt-mode-direct").ClassListContains("sn-opt-picked");
            Step("room-click-create", "ok", createOpen && directPicked, "at", at, "screen", stage.Current != null ? stage.Current.Name : "");
            Check("방 만들기", createOpen && directPicked, "「방 만들기」 → S05 → [직접 연결]을 눌러도 골라지지 않았다");
            yield return Snap("01b-room-create");
            var createBack = Q("room-create-back");
            if (Laid(createBack)) yield return Click(Px(createBack));
            flow.Room.Create.Internet = true;   // 기본으로 되돌린다(실행하는 동안만 남는 값)
            yield return Frames(3);
            yield return Click(Px(Q("title-item-" + TitleScreen.Join)));
            bool joinOpen = stage.Current is RoomJoinScreen;
            yield return Frames(3);
            var go = Q("room-join-go");
            if (Laid(go)) yield return Click(Px(go));
            yield return Frames(2);
            bool badAddress = joinOpen && flow.Room.FailCode == "NET_BAD_ADDRESS" && Q("room-join-fail") != null;
            Step("room-click-join", "ok", badAddress, "at", at, "fail", flow.Room.FailCode ?? "", "empty", Q("room-join-list-note") != null);
            Check("참가하기", badAddress, "「참가하기」 → S06 → 빈 칸 [참가]에 주소 형식 안내가 뜨지 않았다");
            yield return Snap("01c-room-join");
            var joinBack = Q("room-join-back");
            if (Laid(joinBack)) yield return Click(Px(joinBack));
            yield return Frames(3);
            bool titleBack = stage.Current is TitleScreen;
            Step("room-back-title", "ok", titleBack, "screen", stage.Current != null ? stage.Current.Name : "");
            if (!Check("방 화면 뒤로", titleBack, "[뒤로]가 타이틀로 돌아가지 않았다")) { Finish(1, head.ToString()); yield break; }

            // ── ①-3 재접속 덮개(M7 화면 ② S11): 가짜 방(망 없이 — 방장이 방을 닫음)의 덮개가 마우스를 받는다 → [타이틀로] → 타이틀
            var fakeRoom = new RoomLive(() => 0) { Room = new Sinnoh.Rules.Session.RoomState { Phase = Sinnoh.Rules.Session.RoomPhase.InGame } };
            fakeRoom.FakeFailed(Sinnoh.Rules.Session.NetCode.HostClosed, Sinnoh.Rules.Session.RoomPhase.InGame);
            flow.Room.ShowGame(fakeRoom);
            yield return Frames(3);
            bool coverUp = flow.RoomLayer != null && flow.RoomLayer.CoverUp && Q("room-reconnect-retry") != null;
            yield return Snap("01d-room-reconnect");
            var coverLeave = Q("room-reconnect-leave");
            if (Laid(coverLeave)) yield return Click(Px(coverLeave));
            yield return Frames(3);
            bool coverOk = coverUp && flow.Room.Live == null && stage.Current is TitleScreen && (flow.RoomLayer.Element == null || flow.RoomLayer.Element.parent == null);
            Step("room-reconnect-click", "ok", coverOk, "at", at, "coverUp", coverUp, "screen", stage.Current != null ? stage.Current.Name : "");
            if (!Check("재접속 덮개", coverOk, "가짜 방의 재접속 덮개 [타이틀로]를 눌러도 타이틀로 돌아가지 않았다")) { Finish(1, head.ToString()); yield break; }

            yield return Click(Px(Q("title-item-" + TitleScreen.Solo)));
            bool soloOpen = stage.Current is SoloScreen;
            Step("title-click-solo", "ok", soloOpen, "at", at, "screen", stage.Current != null ? stage.Current.Name : "");
            if (!Check("타이틀 누르기", soloOpen, "「혼자 하기」를 눌렀는데 솔로 설정이 열리지 않았다")) { Finish(1, head.ToString()); yield break; }
            yield return Frames(3);

            // ── ② 솔로 설정: 고를 수 없는 값에 올리면 툴팁, [게임 시작] ──
            var ub = Q("solo-opt-" + SoloScreen.Rules + "-custom");   // 세부 규칙 「직접 설정」 — 강한 AI는 M8 「연결」부터, 울트라 버스트는 M6 「UB 시작」부터 고를 수 있다(맨 끝 Ub 단계)
            yield return Move(Px(ub));
            var tipLabels = stage.Tips.Element.Query<Label>().ToList();
            bool tipOk = stage.Tips.Target == ub && stage.Tips.Element.resolvedStyle.display == DisplayStyle.Flex && stage.Tips.Element.resolvedStyle.visibility == Visibility.Visible;
            string tipBody = tipLabels.Count > 1 ? tipLabels[1].text.Replace("⁠", "") : "";
            Step("solo-tooltip", "ok", tipOk, "at", at, "body", tipBody);
            Check("솔로 설정 툴팁", tipOk, "값에 마우스를 올렸는데 툴팁이 뜨지 않았다");
            Check("솔로 설정 툴팁", tipBody == stage.T("screen.common.not_ready"), "툴팁 글 「" + tipBody + "」");
            yield return Snap("02-solo-tooltip");
            yield return Click(Px(ub));
            Check("솔로 설정", !ub.ClassListContains("sn-opt-picked"), "아직 고를 수 없는 값이 눌렸다");
            // 갤럭시단 AI: 처음 값이 「강함」(결정 118 ②) — 「기본」을 눌렀다가 「강함」을 누르면 골라지고, 다시 「기본」으로 둔다(이 확인 판은 원칙 AI로 끝까지 간다)
            var strongOpt = Q("solo-opt-" + SoloScreen.GalacticAi + "-strong");
            bool strongFirst = stage.Current is SoloScreen soloA && soloA.Settings.GalacticAi == Sinnoh.Rules.Session.GalacticAiLevel.Strong && strongOpt.ClassListContains("sn-opt-picked");
            Check("솔로 설정 갤럭시단 AI", strongFirst, "처음 값이 「강함」이 아니다");
            yield return Click(Px(Q("solo-opt-" + SoloScreen.GalacticAi + "-basic")));
            bool basicPicked = stage.Current is SoloScreen soloP && soloP.Settings.GalacticAi == Sinnoh.Rules.Session.GalacticAiLevel.Principle;
            yield return Click(Px(strongOpt));
            bool strongPicked = strongFirst && basicPicked && stage.Current is SoloScreen solo0 && solo0.Settings.GalacticAi == Sinnoh.Rules.Session.GalacticAiLevel.Strong && strongOpt.ClassListContains("sn-opt-picked");
            yield return Click(Px(Q("solo-opt-" + SoloScreen.GalacticAi + "-basic")));
            strongPicked &= stage.Current is SoloScreen soloB && soloB.Settings.GalacticAi == Sinnoh.Rules.Session.GalacticAiLevel.Principle;
            Step("solo-click-strong", "ok", strongPicked, "at", at);
            Check("솔로 설정 갤럭시단 AI", strongPicked, "「강함」 → 「기본」을 눌렀는데 골라지지 않았다");
            // 「주인공」 줄: 필드 도트 칸을 누르면 그 주인공이 골라진다(결정 52·60) — 보드의 말과 배틀의 뒷모습이 그 인물
            var protoOpt = Q("solo-opt-" + SoloScreen.Protagonist + "-" + ProbeProtagonist);
            yield return Click(Px(protoOpt));
            bool protoPicked = stage.Current is SoloScreen solo1 && solo1.Settings.Protagonist == ProbeProtagonist && protoOpt.ClassListContains("sn-opt-picked");
            Step("solo-click-protagonist", "ok", protoPicked, "at", at, "protagonist", stage.Current is SoloScreen solo2 ? solo2.Settings.Protagonist : "");
            Check("솔로 설정 주인공", protoPicked, "주인공 칸을 눌렀는데 골라지지 않았다");
            yield return Snap("02b-solo-protagonist");
            // 트레이너 카드: 처음 값은 「끔」(결정 134) — 「켬」에 올리면 「테스트 중인 기능입니다.」, 눌러 켠다(이 확인 판은 카드 고르기까지 간다)
            var cardsOn = Q("solo-opt-" + SoloScreen.TrainerCards + "-on");
            bool cardsOffFirst = stage.Current is SoloScreen soloC && !soloC.Settings.TrainerCards;
            yield return Move(Px(cardsOn));
            var cardTips = stage.Tips.Element.Query<Label>().ToList();
            bool cardTip = stage.Tips.Target == cardsOn && cardTips.Count > 1 && cardTips[1].text.Replace("⁠", "") == stage.T("screen.solo.trainer_cards.on.tip").Replace("⁠", "");
            yield return Click(Px(cardsOn));
            bool cardsPicked = stage.Current is SoloScreen soloD && soloD.Settings.TrainerCards;
            Step("solo-trainer-cards", "ok", cardsOffFirst && cardTip && cardsPicked, "at", at, "offFirst", cardsOffFirst, "tip", cardTip);
            Check("솔로 설정 트레이너 카드", cardsOffFirst && cardTip && cardsPicked, "처음 값이 「끔」이 아니거나 「켬」 툴팁 · 누르기가 맞지 않다");
            // 무르기(결정 149 · 154): 처음 값은 「내 턴만」 — 「자유」에 올리면 그 설명, 눌러 고른 뒤 「내 턴만」으로 되돌렸다가 다시 「자유」로 간다
            // (이 확인 판은 자유 — 보드 단계 undo-press · undo-rewind가 HUD [무르기]와 메뉴 「되돌리기」를 마우스로 지난다)
            var undoTurn = Q("solo-opt-" + SoloScreen.Undo + "-turn");
            var undoFree = Q("solo-opt-" + SoloScreen.Undo + "-free");
            bool undoFirst = stage.Current is SoloScreen soloU && soloU.Settings.Undo == Sinnoh.Rules.Session.UndoMode.Turn && undoTurn != null && undoTurn.ClassListContains("sn-opt-picked");
            yield return Move(Px(undoFree));
            var undoTips = stage.Tips.Element.Query<Label>().ToList();
            bool undoTip = stage.Tips.Target == undoFree && undoTips.Count > 1 && undoTips[1].text.Replace("⁠", "") == stage.T("screen.solo.undo.free.tip").Replace("⁠", "");
            yield return Click(Px(undoFree));
            bool undoPicked = stage.Current is SoloScreen soloF && soloF.Settings.Undo == Sinnoh.Rules.Session.UndoMode.Free && undoFree.ClassListContains("sn-opt-picked");
            yield return Click(Px(undoTurn));
            bool undoBack = stage.Current is SoloScreen soloT && soloT.Settings.Undo == Sinnoh.Rules.Session.UndoMode.Turn;
            yield return Click(Px(undoFree));
            bool undoFreeGame = stage.Current is SoloScreen soloG && soloG.Settings.Undo == Sinnoh.Rules.Session.UndoMode.Free;
            Step("solo-undo", "ok", undoFirst && undoTip && undoPicked && undoBack && undoFreeGame, "at", at, "first", undoFirst, "tip", undoTip, "free", undoPicked, "back", undoBack, "freeGame", undoFreeGame);
            Check("솔로 설정 무르기", undoFirst && undoTip && undoPicked && undoBack && undoFreeGame, "처음 값이 「내 턴만」이 아니거나 「자유」 툴팁 · 누르기가 맞지 않다");
            yield return Click(Px(Q("solo-start")));
            bool villageOpen = stage.Current is SetupVillageScreen;
            Step("solo-click-start", "ok", villageOpen, "at", at, "tipHidden", stage.Tips.Target == null);
            if (!Check("솔로 설정 [게임 시작]", villageOpen, "고향 마을 고르기가 열리지 않았다")) { Finish(1, head.ToString()); yield break; }
            yield return Frames(3);

            // ── ③ 고향 마을: 줄 누르기 → [확정] ──
            var village = (SetupVillageScreen)stage.Current;
            var confirmVillage = stage.Root.Q<Button>("setup-village-confirm");
            yield return Click(Px(confirmVillage));
            Check("고향 마을", stage.Current is SetupVillageScreen, "고르기 전인데 [확정]이 눌렸다");
            var villageRow = stage.Root.Q("setup-village-list").Q(className: "sn-setup-item");
            yield return Click(Px(villageRow));
            bool villagePicked = village.Picked == 0 && confirmVillage.enabledSelf;
            Step("village-click-row", "ok", villagePicked, "at", at, "picked", village.Picked);
            Check("고향 마을", villagePicked, "첫 줄을 눌렀는데 골라지지 않았다");
            yield return Snap("03-village-picked");
            yield return Click(Px(confirmVillage));
            // 고향 마을 연출(04 §8-8 2행, 결정 66 ③): 마을 화면이 걷히고 좌석 띠가 뜨며 말이 떨어져 내려앉는다(실시각 1.8초) → 고르는 화면에 카드가 들어온다(1행 0.8초)
            var drop = stage.Current as SetupDropScreen;
            yield return Wait(() => drop != null && drop.Coloured, 4f);
            yield return Frames(2);
            var dropped = board.Pieces != null && flow.Client != null ? board.Pieces.Of(flow.Client.Seat) : null;
            bool dropOk = drop != null && drop.Coloured && dropped != null && dropped.Root != null && dropped.Root.activeSelf && FaceBox.Shown(Q("setup-drop-face")) == ProbeProtagonist;
            Step("village-drop", "ok", dropOk, "at", at, "band", drop != null, "coloured", drop != null && drop.Coloured, "piece", dropped != null && dropped.Root != null && dropped.Root.activeSelf);
            Check("고향 마을 연출", dropOk, "마을 [확정] 뒤 좌석 띠가 뜨고 말이 내려앉지 않았다");
            yield return Snap("03b-village-drop");
            yield return Wait(() => stage.Current is SetupPickScreen, 4f);
            yield return Frames(2);   // 새로 열린 화면의 자리가 잡힌 뒤에 누른다
            // 카드가 들어오는 동안에는 칸을 눌러도 골라지지 않는다(입력은 연출이 끝나고 클릭 막기까지 지난 뒤에 열린다)
            if (stage.Current is SetupPickScreen early && !flow.SetupOpen)
            {
                yield return Click(Px(stage.Root.Q("setup-pick-starters").Q(className: "sn-pick-tile").Q(className: "sn-pick-name")));
                bool stillClosed = !flow.SetupOpen;
                Step("pick-click-during-deal", "ok", !stillClosed || early.Starter < 0, "at", at, "starter", early.Starter, "closed", stillClosed);
                if (stillClosed) Check("카드가 들어오는 중", early.Starter < 0, "연출 중인데 스타팅 포켓몬 칸이 골라졌다");
            }
            yield return Wait(() => flow.SetupOpen, 4f);
            yield return Frames(2);
            bool pickOpen = stage.Current is SetupPickScreen && flow.SetupOpen && ((SetupPickScreen)stage.Current).CardsFaceUp == ((SetupPickScreen)stage.Current).Cards.Count;
            Step("village-click-confirm", "ok", pickOpen, "at", at);
            if (!Check("고향 마을 [확정]", pickOpen, "스타팅 포켓몬·트레이너 카드 고르기가 열리지 않았다")) { Finish(1, head.ToString()); yield break; }
            yield return Frames(3);

            // ── ④ 스타팅 포켓몬 · 트레이너 카드: 고유 기술 툴팁, 칸 누르기 → [확정] ──
            var pick = (SetupPickScreen)stage.Current;
            var starterTile = stage.Root.Q("setup-pick-starters").Q(className: "sn-pick-tile");
            var moveBox = starterTile.Q(className: "sn-pick-move");
            yield return Move(Px(moveBox));
            bool moveTip = stage.Tips.Target == moveBox;
            Step("pick-move-tooltip", "ok", moveTip, "at", at);
            Check("고유 기술 툴팁", moveTip, "고유 기술에 마우스를 올렸는데 툴팁이 뜨지 않았다");
            yield return Snap("04-pick-tooltip");
            yield return Click(Px(starterTile.Q(className: "sn-pick-name")));
            var cardTile = stage.Root.Q("setup-pick-cards") != null ? stage.Root.Q("setup-pick-cards").Q(className: "sn-pick-tile") : null;
            if (cardTile != null) yield return Click(Px(cardTile.Q(className: "sn-pick-name")));
            bool picked = pick.Starter == 0 && (cardTile == null || pick.Card == 0) && pick.CanConfirm;
            Step("pick-click-tiles", "ok", picked, "starter", pick.Starter, "card", pick.Card);
            Check("스타팅 포켓몬·트레이너 카드", picked, "칸을 눌렀는데 골라지지 않았다(스타팅 포켓몬 " + pick.Starter + " · 카드 " + pick.Card + ")");
            yield return Snap("05-pick-picked");
            yield return Click(Px(Q("setup-pick-confirm")));
            yield return Frames(4);
            // 고른 뒤·공개(1·3행): 고르는 화면이 남아 볼이 열리고 카드가 뒤집힌다 → 숨겨진 장소 배치(5행). 긴 연출은 연출 시각으로 건너뛰며 한 순간씩 본다
            bool sent = stage.Current is SetupPickScreen && pick.StarterSent;
            flow.Skip(flow.MsUntil(step => step.Row == Sinnoh.Fx.SetupRows.Starter, 600));
            yield return Frames(3);
            bool shown = sent && stage.Current is SetupPickScreen && pick.MonShown >= 1f;
            // 도트 이펙트 시트(결정 69)가 플레이어 번들에서 읽히나 — 못 읽으면 조용히 안 그려져 다른 단계로는 드러나지 않는다
            bool sheet = SheetArt.Texture(SheetArt.Of("open")) != null && SheetArt.Texture(SheetArt.Of("dust")) != null;
            Step("pick-reveal", "ok", shown && sheet, "sent", sent, "mon", pick.MonShown, "sheet", sheet);
            Check("스타팅 포켓몬 공개", shown, "[확정] 뒤 고르는 화면에서 볼이 열려 도트가 나타나지 않았다");
            Check("도트 이펙트 시트", sheet, "이펙트 시트(open·dust)를 읽지 못했다 — 볼 빛·먼지가 그려지지 않는다");
            yield return Snap("05b-pick-reveal");
            flow.Skip(flow.MsUntil(step => Sinnoh.Fx.SpotDropBeats.Spots(step) != null, 600));
            yield return Frames(2);
            bool spots = board.Tokens != null && board.Tokens.SpotsDropping && stage.Current == null && flow.Hud != null;
            Step("spots-drop", "ok", spots, "visible", board.Tokens != null ? board.Tokens.SpotsVisible : -1);
            Check("숨겨진 장소 배치", spots, "셋업 연출 뒤 보드에서 숨겨진 장소 토큰이 떨어지지 않는다");
            yield return Snap("05c-spots-drop");
            flow.Skip(flow.MsUntil(step => step.Row == Sinnoh.Fx.ActBeats.Row));
            yield return Frames(4);
            bool onBoard = flow.OnBoard && stage.Current == null && flow.Hud != null;
            Step("pick-click-confirm", "ok", onBoard, "at", at, "commands", flow.Session != null ? flow.Session.CommandCount : -1);
            if (!Check("셋업 [확정]", onBoard, "보드로 넘어가지 않았다")) { Finish(1, head.ToString()); yield break; }
            {
                var mine = board.Pieces != null ? board.Pieces.Of(flow.Client.Seat) : null;
                bool dot = mine != null && mine.Style == PieceStyle.Dot && mine.Protagonist == ProbeProtagonist && flow.Protagonist(flow.Client.Seat) == ProbeProtagonist;
                Step("board-piece-protagonist", "ok", dot, "style", mine != null ? mine.Style.ToString() : "", "protagonist", mine != null ? mine.Protagonist ?? "" : "");
                Check("보드 말", dot, "보드의 말이 솔로 설정에서 고른 주인공의 도트가 아니다");
                string faceShown = FaceBox.Shown(Q("board-hud-seat-" + (flow.Client.Seat + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "-face"));
                Step("board-face-protagonist", "ok", faceShown == ProbeProtagonist, "protagonist", faceShown ?? "");
                Check("얼굴 상자", faceShown == ProbeProtagonist, "위 띠의 얼굴 상자가 솔로 설정에서 고른 주인공의 얼굴이 아니다");
            }

            // ── ⑤ 보드: 컷신 뒤 입력 열림 → 행동 단추([전체 회복])의 툴팁이 아래 판 밖(위)에 ──
            var hud = flow.Hud;
            var client = flow.Client;
            yield return Wait(() => flow.InputOpen, 15f);
            if (!Check("보드", flow.InputOpen, "내 턴 컷신 뒤 입력이 열리지 않았다")) { Finish(1, head.ToString()); yield break; }
            yield return Frames(3);
            int commands = flow.Session.CommandCount;
            // 첫 턴의 고향 마을에는 회색 단추가 없다([마을]·[도전]이 켜졌다). 켜진 단추의 툴팁을 본다(누르지는 않는다)
            var grey = Q("board-hud-act-" + BoardHud.Village);
            if (Check("행동 단추", grey != null && !grey.ClassListContains("sn-hud-off"), "고향 마을의 [마을]이 판에 없거나 회색이다"))
            {
                yield return Move(Px(grey));
                var labels = stage.Tips.Element.Query<Label>().ToList();
                string body = labels.Count > 1 ? labels[1].text.Replace("⁠", "") : "";
                bool greyTip = stage.Tips.Target == grey && labels.Count > 0 && labels[labels.Count - 1].text.Replace("⁠", "") == stage.T("screen.board.action." + BoardHud.Village + ".tip");
                body = labels.Count > 0 ? labels[labels.Count - 1].text.Replace("⁠", "") : "";
                // 단추 툴팁은 아래 판 밖(위)에 뜬다 — 판 안의 「행동 · 이번 턴 · 안내」 줄을 덮지 않는다
                yield return Frames(2);
                var tipBox = stage.Tips.Element.worldBound;
                float dockTop = Q("board-hud-dock").worldBound.yMin;
                bool tipClear = stage.Tips.Target == grey && tipBox.yMax <= dockTop + 0.5f && !tipBox.Overlaps(Q("board-hud-guide").worldBound) && !tipBox.Overlaps(Q("board-hud-used").worldBound);
                yield return Snap("06-board-button-tooltip");
                bool noAnswer = flow.Session.CommandCount == commands;
                Step("board-button-tooltip", "ok", greyTip && noAnswer && tipClear, "name", grey.name, "at", at, "tip", body, "commandsBefore", commands, "commandsAfter", flow.Session.CommandCount,
                    "tipBottom", tipBox.yMax, "dockTop", dockTop, "tipOutsideDock", tipClear);
                Check("행동 단추", tipClear, "단추 툴팁이 아래 판 안의 줄을 덮는다(툴팁 아래 변 " + tipBox.yMax.ToString("0.#", inv) + " · 판 위 변 " + dockTop.ToString("0.#", inv) + ")");
                Check("행동 단추", greyTip, "올렸는데 단추의 설명 툴팁이 뜨지 않았다(「" + body + "」)");
            }

            // ── ⑤-2 보드 위에 늘 보이는 것: 토큰 · 좌석 칩에 올리면 공개 정보 카드 · 기록 [펼치기] → [닫기] ──
            {
                int homeTokenCell = client.View.Seats[client.Seat].Cell;
                bool tokensOk = board.Tokens != null && !board.Tokens.GenMissing && board.Tokens.GruntAt(homeTokenCell) != null && board.Tokens.SpotRenderers.Count > 0;
                Step("board-tokens", "ok", tokensOk, "shown", board.Tokens != null ? board.Tokens.Shown.Count : -1, "grunt", board.Tokens != null && board.Tokens.GruntAt(homeTokenCell) != null);
                Check("보드 위 토큰", tokensOk, "숨겨진 장소·조무래기 토큰이 보드에 서지 않았다(board·cards 번들)");
                string seatNo = (client.Seat + 1).ToString(inv);
                var chip = Q("board-hud-seat-" + seatNo);
                yield return Move(Px(chip));
                yield return Frames(3);
                var infoCard = Q("info-card");
                bool infoShown = hud.Info.Subject == "seat-" + seatNo && infoCard != null && infoCard.resolvedStyle.display == DisplayStyle.Flex && infoCard.worldBound.yMin >= chip.worldBound.yMax - 0.5f && Q("info-card-mon-0") != null;
                yield return Snap("06b-board-info-card");
                Step("board-info-card", "ok", infoShown, "at", at, "subject", hud.Info.Subject ?? "", "top", infoCard != null ? infoCard.worldBound.yMin : -1f, "chipBottom", chip.worldBound.yMax);
                Check("공개 정보 카드", infoShown, "좌석 칩에 마우스를 올렸는데 공개 정보 카드가 칩 아래에 뜨지 않았다(대상 " + (hud.Info.Subject ?? "없음") + ")");
                var logOpen = Q("board-hud-log-open");
                yield return Move(Px(logOpen));
                yield return Frames(2);
                bool infoGone = hud.Info.Subject == null;
                Check("공개 정보 카드", infoGone, "좌석 칩에서 벗어났는데 공개 정보 카드가 남았다");
                yield return Click(Px(logOpen));
                yield return Frames(3);
                var logScreen = stage.Current as LogScreen;
                bool logShown = logScreen != null && Q("board-log-panel") != null && logScreen.Total > 0 && flow.Session.CommandCount == commands;
                yield return Snap("06c-board-log");
                if (logShown)
                {
                    yield return Click(Px(Q("board-log-close")));
                    yield return Frames(3);
                }
                bool logClosed = stage.Current == null && flow.InputOpen;
                Step("board-log", "ok", logShown && logClosed && infoGone, "at", at, "lines", logScreen != null ? logScreen.Total : -1, "recent", hud.RecentLog.Count, "infoGone", infoGone);
                Check("기록", logShown, "[펼치기]를 눌렀는데 기록 창이 열리지 않았다");
                Check("기록", logClosed, "[닫기]를 눌렀는데 기록 창이 닫히지 않았거나 보드 입력이 열리지 않았다");
            }

            // ── ⑤-3 정비: [정비] → 정비 판 → 포켓몬 카드 → 카드 확대 → [닫기] → 정비 판 → T로 닫기(실제 진행이라 기술 카드·지닌물건은 없다 — 집어 넣기는 플레이 시험 MaintFlowTests) ──
            {
                var maint = Q("board-hud-maint");
                bool maintOn = maint != null && maint.enabledSelf;
                if (maintOn)
                {
                    yield return Click(Px(maint));
                    yield return Frames(3);
                }
                var party = stage.Current as PartyScreen;
                bool partyShown = party != null && party.Mine && Q("board-party-mon-0") != null && Q("board-party-col-5") != null && !flow.InputOpen && flow.Session.CommandCount == commands;
                yield return Snap("06d-board-party");
                bool monShown = false, monClosed = false, keyClosed = false;
                if (partyShown)
                {
                    yield return Click(Px(Q("board-party-mon-0")));
                    yield return Frames(3);
                    monShown = stage.Current is MonScreen && Q("mon-detail-move-0") != null;
                    yield return Snap("06e-mon-detail");
                    if (monShown)
                    {
                        yield return Click(Px(Q("mon-detail-close")));
                        yield return Frames(3);
                    }
                    monClosed = stage.Current is PartyScreen;
                    yield return Tap(Key.T);
                    yield return Frames(3);
                    keyClosed = stage.Current == null && flow.InputOpen;
                }
                // [정비] 두 번 = 열고 닫힘(결정 176 — 판이 떠 있어도 아래 판의 단추는 가려지지 않는다)
                bool buttonToggled = false;
                if (keyClosed)
                {
                    yield return Click(Px(Q("board-hud-maint")));
                    yield return Frames(3);
                    bool reopened = stage.Current is PartyScreen;
                    yield return Click(Px(Q("board-hud-maint")));
                    yield return Frames(3);
                    buttonToggled = reopened && stage.Current == null && flow.InputOpen;
                }
                Step("board-party", "ok", partyShown && monShown && monClosed && keyClosed && buttonToggled, "at", at, "maintOn", maintOn, "party", partyShown, "mon", monShown, "back", monClosed, "keyT", keyClosed, "buttonTwice", buttonToggled, "commands", flow.Session.CommandCount);
                Check("정비", buttonToggled, "[정비]를 다시 눌렀는데 정비 판이 닫히지 않았다(결정 176)");
                Check("정비", partyShown, "[정비]를 눌렀는데 정비 판이 열리지 않았다(단추 켜짐 " + maintOn + ")");
                Check("정비", monShown && monClosed, "포켓몬 카드를 눌러 카드 확대를 열고 [닫기]로 정비 판에 돌아오지 못했다");
                Check("정비", keyClosed, "T로 정비 판이 닫히지 않았거나 보드 입력이 열리지 않았다");
                if (stage.Current != null) { flow.CloseMon(); flow.CloseParty(); }
            }

            // ── ⑤-4 트레이너 카드 판(S40): [트레이너 카드] → 판 → 창 밖(가운데 필드)을 눌러 닫기. 답은 나가지 않는다 ──
            {
                var cardButton = Q("board-hud-trainer");
                bool cardOn = cardButton != null && cardButton.enabledSelf;
                if (cardOn)
                {
                    yield return Click(Px(cardButton));
                    yield return Frames(3);
                }
                bool cardShown = stage.Current is TrainerScreen && Q("board-trainer-ability-1") != null && Q("board-trainer-ability-2") != null && !flow.InputOpen;
                yield return Snap("06f-board-trainer");
                bool cardClosed = false;
                if (cardShown)
                {
                    // 창 밖: 판의 아래 변과 아래 HUD 판 사이의 어둠
                    var panelBox = Q("board-trainer-panel").worldBound;
                    var dockBox = Q("board-hud-dock").worldBound;
                    float panelScale = stage.Grade.PanelScale;
                    yield return Click(new Vector2(panelBox.center.x * panelScale, root.Size.y - (panelBox.yMax + dockBox.yMin) * 0.5f * panelScale));
                    yield return Frames(3);
                    cardClosed = stage.Current == null && flow.InputOpen && flow.Session.CommandCount == commands;
                }
                Step("board-trainer", "ok", cardShown && cardClosed, "at", at, "buttonOn", cardOn, "shown", cardShown, "closedOutside", cardClosed, "commands", flow.Session.CommandCount);
                Check("트레이너 카드", cardShown, "[트레이너 카드]를 눌렀는데 판이 열리지 않았다(단추 켜짐 " + cardOn + ")");
                Check("트레이너 카드", cardClosed, "창 밖을 눌렀는데 판이 닫히지 않았거나 답이 나갔다");
                if (stage.Current != null) flow.CloseTrainer();
            }

            // ── ⑤-5 마을 창(S23): [마을] → 창 → [전체 회복](답 하나) → 단추가 꺼지고 까닭이 선다 → [닫기] ──
            {
                var villageButton = Q("board-hud-act-" + BoardHud.Village);
                if (villageButton != null)
                {
                    yield return Click(Px(villageButton));
                    yield return Frames(3);
                }
                var villageScreen = stage.Current as VillageScreen;
                bool villageShown = villageScreen != null && villageScreen.CanHeal && !villageScreen.CanFly && Q("board-village-fly-why") != null && flow.Session.CommandCount == commands;
                yield return Snap("06g-board-village");
                bool healed = false, villageClosed = false;
                if (villageShown)
                {
                    yield return Click(Px(Q("board-village-heal")));
                    yield return Frames(3);
                    yield return Wait(() => flow.BoardOpen, 5f);
                    yield return Frames(2);
                    healed = flow.Session.CommandCount == commands + 1 && !villageScreen.CanHeal && Q("board-village-heal-why") != null && stage.Current == villageScreen;
                    yield return Snap("06h-board-village-healed");
                    yield return Click(Px(Q("board-village-close")));
                    yield return Frames(3);
                    yield return Wait(() => flow.InputOpen, 5f);
                    villageClosed = stage.Current == null && flow.InputOpen;
                }
                Step("board-village", "ok", villageShown && healed && villageClosed, "at", at, "shown", villageShown, "healed", healed, "closed", villageClosed, "commandsBefore", commands, "commandsAfter", flow.Session.CommandCount);
                Check("마을 창", villageShown, "[마을]을 눌렀는데 마을 창이 열리지 않았거나 [전체 회복]이 꺼져 있다");
                Check("마을 창", healed, "[전체 회복]을 눌렀는데 답이 나가지 않았거나 단추가 그대로 켜져 있다");
                Check("마을 창", villageClosed, "[닫기]를 눌렀는데 마을 창이 닫히지 않았거나 보드 입력이 열리지 않았다");
                if (stage.Current != null) flow.CloseVillage();
                commands = flow.Session.CommandCount;   // 회복 답 하나가 나갔다 — 뒤 단계의 기준을 맞춘다
            }

            // ── ⑥ 칸 올리기 → 누르기(걷기): 기본 시점 ──
            int from = client.View.Seats[client.Seat].Cell;
            var dist = MoveRules.Reachable(flow.Ctx.Board, from, client.View.Seats[client.Seat].MovesLeft, client.View.Options.ElevationMode);
            var open = OpenCells("첫 턴", out float off0);
            Spot first = default;
            bool have = false;
            foreach (var s in open) if (!have && dist[s.Cell] == 1) { first = s; have = true; }
            if (!Check("칸 고르기", have, "한 걸음 떨어진 고를 수 있는 칸이 없다(갈 수 있는 칸 " + hud.Reach.Count + " · 고를 수 있는 칸 " + open.Count + ")")) { Finish(1, head.ToString()); yield break; }
            yield return Move(first.Px);
            bool hover = hud.HoverCell == first.Cell && board.Marks.Target == first.Cell && flow.Session.CommandCount == commands;
            yield return Snap("07-board-hover");
            Step("board-hover-cell", "ok", hover, "cell", first.Cell, "at", first.Px, "hoverCell", hud.HoverCell, "markTarget", board.Marks.Target, "reach", hud.Reach.Count, "open", open.Count, "pickVsEngineMaxPx", off0);
            Check("칸 올리기", hover, "마우스를 올린 칸 " + first.Cell + "이 칠해지지 않았다(올린 칸 " + hud.HoverCell + ")");
            Check("칸 좌표", off0 < 1.5f, "내 셈과 엔진 카메라의 칸 자리가 " + off0.ToString("0.##", inv) + "px 어긋난다");
            // 걷기 전 확인(결정 70 ⑱): 한 번 누르면 고른 칸(길 · 「이동력 n 사용」 · [이동]) — 걷지 않는다. Esc로 풀리고, 다시 골라 같은 칸을 한 번 더 누르면 걷는다
            yield return Click(first.Px);
            bool pickedOnce = hud.PickedCell == first.Cell && flow.Session.CommandCount == commands && Q(BoardHud.MoveGo) != null && board.Marks.Target == first.Cell;
            yield return Snap("07b-board-picked");
            yield return Tap(Key.Escape);
            bool unpicked = hud.PickedCell == -1 && flow.Session.CommandCount == commands && Q(BoardHud.MoveGo) == null;
            yield return Click(first.Px);
            bool pickedAgain = hud.PickedCell == first.Cell && flow.Session.CommandCount == commands;
            // 결정 73 ③: 단추 줄의 [취소]를 눌러도 풀린다(+ 「행동」 머리 줄의 오른쪽 클릭 안내) → 다시 골라 아래에서 걷는다
            yield return Frames(2);
            // 안내는 카드 칩(앰프 · 장작 · 도로)과 [무르기]가 함께 선 머리 줄에서는 빠진다(BoardHud — 무르기 D1 2차)
            bool hintRoom = Q(BoardHud.UndoButton) == null || Q(BoardHud.CardState + "-amp") == null && Q(BoardHud.CardState + "-firewood") == null && Q(BoardHud.CardState + "-road") == null;
            bool cancelShown = pickedAgain && Laid(Q(BoardHud.MoveCancel)) && (Laid(Q(BoardHud.MoveHint)) || !hintRoom && Q(BoardHud.MoveHint) == null);
            if (cancelShown) yield return Click(Px(Q(BoardHud.MoveCancel)));
            bool cancelled = cancelShown && hud.PickedCell == -1 && flow.Session.CommandCount == commands && Q(BoardHud.MoveCancel) == null;
            yield return Click(first.Px);
            pickedAgain = pickedAgain && hud.PickedCell == first.Cell && flow.Session.CommandCount == commands;
            Step("board-pick-cell", "ok", pickedOnce && unpicked && cancelled && pickedAgain, "cell", first.Cell, "at", first.Px, "picked", pickedOnce, "esc", unpicked, "cancelButton", cancelled, "again", pickedAgain, "commands", flow.Session.CommandCount);
            Check("칸 고르기", cancelled, "고른 뒤 [취소] 단추(와 오른쪽 클릭 안내)가 서지 않았거나 눌러도 고른 칸이 풀리지 않았다");
            Check("칸 고르기", pickedOnce, "칸 " + first.Cell + "을 한 번 눌렀는데 고른 칸이 되지 않았거나 바로 걸었다(고른 칸 " + hud.PickedCell + " · 명령 " + flow.Session.CommandCount + ")");
            Check("칸 고르기", unpicked, "Esc를 눌렀는데 고른 칸이 풀리지 않았다");
            Check("칸 고르기", pickedAgain, "풀고 다시 눌렀는데 고른 칸이 되지 않았다");
            yield return Click(first.Px);
            bool walked = client.View.Seats[client.Seat].Cell == first.Cell && flow.Session.CommandCount == commands + 1;
            Step("board-click-cell", "ok", walked, "cell", first.Cell, "seatCell", client.View.Seats[client.Seat].Cell, "commands", flow.Session.CommandCount);
            Check("칸 누르기", walked, "칸 " + first.Cell + "을 눌렀는데 이동 답이 하나 나가지 않았다(말 칸 " + client.View.Seats[client.Seat].Cell + " · 명령 " + flow.Session.CommandCount + ")");
            yield return Frames(12);
            yield return Snap("08-board-walking");
            yield return Wait(() => flow.InputOpen, 15f);
            Check("걷기", flow.InputOpen && board.Pieces.Pieces.Count == 1 && board.Pieces.Pieces[0].Spot != null && board.Pieces.Pieces[0].Spot.Cell == first.Cell, "말이 도착 칸에 서지 않았다");
            yield return PassHiddenSpot("첫 걸음 뒤");

            // ── ⑥-2 카메라 따라가기(결정 57 ①): 걸은 뒤 보는 점 = 도착 칸(말이 화면 가운데 · HUD 판 밖), 방향·줌 그대로 → 따라간 화면에서 다시 칸 올리기·누르기 ──
            {
                float camOff = Vector3.Distance(rig.Target, board.CellTop(first.Cell));
                bool onScreen = BoardPick.Screen(board, cam, first.Cell, root.Size.x, root.Size.y, out var herePx);
                bool centred = onScreen && Mathf.Abs(herePx.x - root.Size.x * 0.5f) < 2f && Mathf.Abs(herePx.y - root.Size.y * 0.5f) < 2f;
                bool clear = onScreen && !BoardHud.UnderPanel(stage.Root, new Vector2(herePx.x / stage.Grade.PanelScale, (root.Size.y - herePx.y) / stage.Grade.PanelScale)) && !flow.OverUi(herePx.x, herePx.y);
                bool sameView = rig.Yaw == 0 && rig.CurrentZoom == BoardCameraRig.Zoom.Region;
                bool followedCam = camOff < 1e-3f && centred && clear && sameView;
                Step("camera-follow", "ok", followedCam, "cell", first.Cell, "targetOff", camOff, "cellAt", herePx, "centred", centred, "outsideHud", clear, "yaw", rig.Yaw, "zoom", rig.CurrentZoom.ToString());
                Check("카메라 따라가기", camOff < 1e-3f, "걸은 뒤 카메라의 보는 점이 도착 칸이 아니다(" + camOff.ToString("0.###", inv) + " 떨어짐)");
                Check("카메라 따라가기", centred && clear, "걸은 뒤 말 칸이 화면 가운데(HUD 판 밖)에 없다(" + herePx.x.ToString("0.#", inv) + ", " + herePx.y.ToString("0.#", inv) + ")");
                Check("카메라 따라가기", sameView, "카메라가 따라가며 방향·줌을 바꿨다(방향 " + rig.Yaw + " · 줌 " + rig.CurrentZoom + ")");

                commands = flow.Session.CommandCount;
                int here = client.View.Seats[client.Seat].Cell;
                var distF = MoveRules.Reachable(flow.Ctx.Board, here, client.View.Seats[client.Seat].MovesLeft, client.View.Options.ElevationMode);
                var followed = OpenCells("따라간 뒤", out float offF);
                Spot near = default;
                bool haveNear = false;
                foreach (var s in followed) if (!haveNear && distF[s.Cell] == 1) { near = s; haveNear = true; }
                if (Check("따라간 뒤 칸 고르기", haveNear, "카메라가 따라간 뒤 한 걸음 떨어진 고를 수 있는 칸이 없다(갈 수 있는 칸 " + hud.Reach.Count + " · 고를 수 있는 칸 " + followed.Count + ")"))
                {
                    yield return Move(near.Px);
                    bool hoverF = hud.HoverCell == near.Cell && board.Marks.Target == near.Cell;
                    yield return Snap("08b-board-followed-hover");
                    yield return Click(near.Px);   // 고르고
                    yield return Click(near.Px);   // 한 번 더: 걷는다
                    bool walkedF = client.View.Seats[client.Seat].Cell == near.Cell && flow.Session.CommandCount == commands + 1;
                    yield return Wait(() => flow.InputOpen, 15f);
                    float camOff2 = Vector3.Distance(rig.Target, board.CellTop(near.Cell));
                    Step("board-followed-click-cell", "ok", hoverF && walkedF && camOff2 < 1e-3f, "cell", near.Cell, "at", near.Px, "hoverCell", hoverF ? near.Cell : hud.HoverCell,
                        "seatCell", client.View.Seats[client.Seat].Cell, "open", followed.Count, "pickVsEngineMaxPx", offF, "targetOff", camOff2);
                    Check("따라간 뒤 칸 올리기", hoverF, "카메라가 따라간 뒤 마우스를 올린 칸 " + near.Cell + "이 칠해지지 않았다");
                    Check("따라간 뒤 칸 누르기", walkedF, "카메라가 따라간 뒤 누른 칸 " + near.Cell + " ≠ 간 칸 " + client.View.Seats[client.Seat].Cell);
                    Check("따라간 뒤 칸 좌표", offF < 1.5f, "카메라가 따라간 뒤 내 셈과 엔진 카메라의 칸 자리가 " + offF.ToString("0.##", inv) + "px 어긋난다");
                    Check("카메라 따라가기", camOff2 < 1e-3f, "둘째 걸음 뒤 카메라의 보는 점이 도착 칸이 아니다");
                    yield return PassHiddenSpot("따라간 뒤 걸음 뒤");
                    yield return Snap("08c-board-followed-walked");
                }
            }

            // ── ⑥-3 마우스로 보드 끌기(결정 66 ②): 끈 만큼 땅이 커서를 따라오고 칸은 눌리지 않는다 → 옮겨진 화면에서 칸 올리기 → HUD 위에서 시작한 끌기는 무시 ──
            {
                commands = flow.Session.CommandCount;
                int seatCell = client.View.Seats[client.Seat].Cell;
                int w = root.Size.x, h = root.Size.y;
                var dragFrom = new Vector2(w * 0.5f, h * 0.5f);
                var dragTo = dragFrom + new Vector2(w * 0.12f, h * 0.08f);
                bool startClear = !flow.OverUi(dragFrom.x, dragFrom.y);
                var targetWas = rig.Target;
                int yawWas0 = rig.Yaw;
                var zoomWas0 = rig.CurrentZoom;
                bool grabbed = BoardPick.Ground(board, cam, dragFrom.x, dragFrom.y, w, h, rig.Target.y, out var grab);
                yield return Drag(dragFrom, dragTo);
                bool dragging = flow.Dragging;
                float moved = Vector3.Distance(rig.Target, targetWas);
                float groundOff = grabbed && BoardPick.GroundAt(cam, at.x, at.y, w, h, grab.y, out var under) ? Vector3.Distance(under, grab) : float.MaxValue;
                bool sameView0 = rig.Yaw == yawWas0 && rig.CurrentZoom == zoomWas0 && Mathf.Approximately(rig.Target.y, targetWas.y);
                bool noHover = hud.HoverCell < 0;
                yield return Snap("08d-board-dragging");
                Queue(at, false);
                yield return Frames(3);
                bool noClick = flow.Session.CommandCount == commands && client.View.Seats[client.Seat].Cell == seatCell && !flow.Dragging;
                bool dragOk = startClear && dragging && moved > 0.5f && groundOff < 0.02f && sameView0 && noClick && noHover;
                Step("board-drag", "ok", dragOk, "from", dragFrom, "to", dragTo, "dragging", dragging, "targetMoved", moved, "groundOff", groundOff, "sameView", sameView0, "noClick", noClick, "noHoverWhileDragging", noHover,
                    "commands", flow.Session.CommandCount);
                Check("보드 끌기", startClear, "화면 가운데가 PC 층 위라 끌기를 시작하지 못했다");
                Check("보드 끌기", dragging && moved > 0.5f, "보드 위에서 왼쪽 단추로 끌었는데 보는 점이 옮겨지지 않았다(" + moved.ToString("0.###", inv) + ")");
                Check("보드 끌기", groundOff < 0.02f, "잡은 땅이 커서를 따라오지 않았다(" + groundOff.ToString("0.###", inv) + " 떨어짐)");
                Check("보드 끌기", sameView0, "끌기가 방향·줌·보는 점 높이를 바꿨다");
                Check("보드 끌기", noClick, "끌고 뗐는데 칸이 눌렸다(명령 " + flow.Session.CommandCount + ")");
                Check("보드 끌기", noHover, "끄는 동안 칸이 칠해졌다(" + hud.HoverCell + ")");

                var draggedOpen = OpenCells("끈 뒤", out float offD);
                if (Check("끈 뒤 칸 고르기", draggedOpen.Count > 0, "보드를 끈 뒤 고를 수 있는 칸이 없다(갈 수 있는 칸 " + hud.Reach.Count + ")"))
                {
                    var spot = draggedOpen[0];
                    yield return Move(spot.Px);
                    bool hoverD = hud.HoverCell == spot.Cell && board.Marks.Target == spot.Cell && flow.Session.CommandCount == commands;
                    yield return Snap("08e-board-dragged-hover");
                    Step("board-dragged-hover-cell", "ok", hoverD && offD < 1.5f, "cell", spot.Cell, "at", spot.Px, "hoverCell", hud.HoverCell, "open", draggedOpen.Count, "pickVsEngineMaxPx", offD);
                    Check("끈 뒤 칸 올리기", hoverD, "보드를 끈 뒤 마우스를 올린 칸 " + spot.Cell + "이 칠해지지 않았다(올린 칸 " + hud.HoverCell + ")");
                    Check("끈 뒤 칸 좌표", offD < 1.5f, "보드를 끈 뒤 내 셈과 엔진 카메라의 칸 자리가 " + offD.ToString("0.##", inv) + "px 어긋난다");
                }

                // HUD 판 위에서 누르고 보드 위로 끌어도 옮겨지지 않는다
                var hudFrom = Px(Q("board-hud-party"));
                var heldTarget = rig.Target;
                yield return Drag(hudFrom, dragFrom);
                bool hudDead = !flow.Dragging && Vector3.Distance(rig.Target, heldTarget) < 1e-4f;
                Queue(at, false);
                yield return Frames(3);
                bool hudNoClick = flow.Session.CommandCount == commands && stage.Current == null;
                Step("board-drag-from-hud", "ok", hudDead && hudNoClick, "from", hudFrom, "to", dragFrom, "targetMoved", Vector3.Distance(rig.Target, heldTarget), "commands", flow.Session.CommandCount);
                Check("HUD 위에서 시작한 끌기", hudDead, "HUD 판 위에서 누르고 끌었는데 보드가 옮겨졌다");
                Check("HUD 위에서 시작한 끌기", hudNoClick, "HUD 판 위에서 누르고 보드 위에서 뗐는데 칸이 눌렸거나 창이 열렸다");
                if (stage.Current != null) { Finish(1, head.ToString()); yield break; }
            }

            // ── ⑦ 휠: HUD 위에서는 줌 그대로, 보드 위에서는 바뀐다 ──
            var dockPx = Px(Q("board-hud-party"));
            yield return Move(dockPx);
            var zoomWas = rig.CurrentZoom;
            bool wheelOff = !rig.WheelEnabled;
            yield return Wheel(1);
            yield return Wheel(-1);
            bool hudWheel = rig.CurrentZoom == zoomWas && wheelOff;
            Step("wheel-over-hud", "ok", hudWheel, "at", dockPx, "zoom", rig.CurrentZoom.ToString(), "wheelEnabled", rig.WheelEnabled, "overUi", flow.OverUi(dockPx.x, dockPx.y));
            Check("HUD 위 휠", hudWheel, "아래 판 위에서 굴린 휠이 보드 줌을 바꿨다(" + zoomWas + " → " + rig.CurrentZoom + ")");
            // 보드 위에서 HUD 위로 옮기며 같은 이벤트에 휠을 굴려도 새지 않는다(스크립트 실행 순서)
            var boardPx = new Vector2(root.Size.x * 0.5f, root.Size.y * 0.5f);
            if (flow.OverUi(boardPx.x, boardPx.y)) Fail("보드 위 휠", "화면 가운데가 PC 층 위다");
            yield return Move(boardPx);
            Queue(dockPx, false, 120f);
            yield return Frames(2);
            Queue(dockPx, false);
            yield return Frames(2);
            bool sameEvent = rig.CurrentZoom == zoomWas;
            Step("wheel-move-and-scroll", "ok", sameEvent, "zoom", rig.CurrentZoom.ToString());
            Check("HUD 위 휠", sameEvent, "보드에서 HUD 위로 옮기는 같은 이벤트의 휠이 보드로 샜다(" + zoomWas + " → " + rig.CurrentZoom + ")");
            if (!sameEvent) rig.Set(zoomWas, rig.Yaw, rig.Target);   // 뒤 단계가 같은 시점에서 보게

            yield return Move(boardPx);
            yield return Wheel(1);
            var zoomIn = rig.CurrentZoom;
            yield return Wheel(-1);
            var zoomBack = rig.CurrentZoom;
            yield return Wheel(-1);
            var zoomOut = rig.CurrentZoom;
            bool boardWheel = zoomIn == BoardCameraRig.Zoom.Close && zoomBack == BoardCameraRig.Zoom.Region && zoomOut == BoardCameraRig.Zoom.Full;
            yield return Snap("09-board-zoom-full");
            Step("wheel-over-board", "ok", boardWheel, "at", boardPx, "up", zoomIn.ToString(), "down", zoomBack.ToString(), "down2", zoomOut.ToString());
            Check("보드 위 휠", boardWheel, "보드 위 휠 한 칸에 줌이 한 단씩 바뀌지 않았다(위 " + zoomIn + " · 아래 " + zoomBack + " · 아래 " + zoomOut + ")");

            // ── ⑧ Q·E 회전 + 가까운 줌 → 다시 칸 누르기(고른 칸 = 누른 칸) ──
            yield return Wheel(1);
            yield return Wheel(1);
            yield return Tap(Key.E);
            int yawE = rig.Yaw;
            yield return Tap(Key.Q);
            yield return Tap(Key.Q);
            int yawQ = rig.Yaw;
            yield return Tap(Key.E);
            yield return Tap(Key.E);
            bool keysOk = yawE == 1 && yawQ == 5 && rig.Yaw == 1 && rig.CurrentZoom == BoardCameraRig.Zoom.Close;
            Step("keys-q-e", "ok", keysOk, "afterE", yawE, "afterQQ", yawQ, "afterEE", rig.Yaw, "zoom", rig.CurrentZoom.ToString());
            Check("Q·E", keysOk, "Q·E로 방향이 60°씩 돌지 않았다(E " + yawE + " · QQ " + yawQ + " · EE " + rig.Yaw + ", 줌 " + rig.CurrentZoom + ")");
            // 보드 돌리기 단추(결정 164): ↺ = Q · ↻ = E — 마우스로 누른다(같은 방향으로 돌아온다)
            {
                var rotLeft = Q(BoardHud.RotateLeft);
                var rotRight = Q(BoardHud.RotateRight);
                bool rotShown = flow.Hud.RotateShown && Laid(rotLeft) && Laid(rotRight);
                int rotYaw0 = rig.Yaw, rotAfterLeft = -1;
                commands = flow.Session.CommandCount;
                if (rotShown)
                {
                    yield return Click(Px(rotLeft));
                    yield return Frames(2);
                    rotAfterLeft = rig.Yaw;
                    yield return Snap("10a-board-rotate-left");
                    yield return Click(Px(rotRight));
                    yield return Frames(2);
                }
                bool rotOk = rotShown && rotAfterLeft == (rotYaw0 + 5) % 6 && rig.Yaw == rotYaw0 && rig.CurrentZoom == BoardCameraRig.Zoom.Close && flow.Session.CommandCount == commands;
                Step("rotate-click", "ok", rotOk, "shown", rotShown, "from", rotYaw0, "rotAfterLeft", rotAfterLeft, "afterRight", rig.Yaw, "commands", flow.Session.CommandCount - commands);
                Check("돌리기 단추", rotOk, "보드 돌리기 단추(↺ · ↻)를 눌렀는데 Q · E처럼 돌지 않았다(보임 " + rotShown + " · " + rotYaw0 + " → " + rotAfterLeft + " → " + rig.Yaw + ")");
            }
            commands = flow.Session.CommandCount;
            var turned = OpenCells("회전 뒤", out float off1);
            turned.RemoveAll(sp => hud.StopsBefore(sp.Cell).Count > 0);   // 결정 71: 길 가운데 뒷면 숨겨진 장소에서 멈추는 칸은 뺀다(아래가 걸은 뒤 도착 칸을 단정한다)
            if (Check("회전 뒤 칸 고르기", turned.Count > 0, "회전·가까운 줌에서 고를 수 있는 칸(길 가운데 숨겨진 장소 없음)이 없다(갈 수 있는 칸 " + hud.Reach.Count + ")"))
            {
                var second = turned[turned.Count - 1];
                yield return Move(second.Px);
                bool hover2 = hud.HoverCell == second.Cell && board.Marks.Target == second.Cell;
                yield return Snap("10-board-turned-hover");
                // 고른 뒤 아래 판의 [이동] 단추로 걷는다(같은 칸을 다시 누르는 것과 같은 답)
                yield return Click(second.Px);
                yield return Frames(2);
                bool goShown = hud.PickedCell == second.Cell && Laid(Q(BoardHud.MoveGo));
                Check("[이동] 단추", goShown, "칸을 골랐는데 [이동] 단추가 서지 않았다");
                if (goShown) yield return Click(Px(Q(BoardHud.MoveGo)));
                bool walked2 = client.View.Seats[client.Seat].Cell == second.Cell && flow.Session.CommandCount == commands + 1;
                Step("board-turned-click-cell", "ok", hover2 && walked2, "yaw", rig.Yaw, "zoom", rig.CurrentZoom.ToString(), "cell", second.Cell, "at", second.Px, "hoverCell", hover2 ? second.Cell : hud.HoverCell,
                    "seatCell", client.View.Seats[client.Seat].Cell, "open", turned.Count, "pickVsEngineMaxPx", off1);
                Check("회전 뒤 칸 올리기", hover2, "회전·줌 뒤 마우스를 올린 칸 " + second.Cell + "이 칠해지지 않았다");
                Check("회전 뒤 칸 누르기", walked2, "회전·줌 뒤 누른 칸 " + second.Cell + " ≠ 간 칸 " + client.View.Seats[client.Seat].Cell);
                Check("회전 뒤 칸 좌표", off1 < 1.5f, "회전·줌 뒤 내 셈과 엔진 카메라의 칸 자리가 " + off1.ToString("0.##", inv) + "px 어긋난다");
                yield return Wait(() => flow.InputOpen, 15f);
                yield return PassHiddenSpot("둘째 걸음 뒤");
                yield return Snap("11-board-turned-walked");
            }

            // ── ⑨ [턴 종료] → 넘김 띠 → 다음 라운드 ──
            if (!Check("턴 종료", flow.InputOpen && hud.Shows(BoardHud.EndTurn), "[턴 종료] 단추를 누를 수 없다")) { Finish(1, head.ToString()); yield break; }
            yield return Click(Px(Q("board-hud-act-" + BoardHud.EndTurn)));
            bool ended = client.View.Round == 2;
            // 넘김 박자(결정 57 ③): 한 좌석 판은 띠 500 + 컷신 500 = 1000이고, 주운 것이 없는 턴이라 요약 단계가 없다. 그 뒤 클릭 막기 300
            int bandMs = -1, cutMs = -1, turnMs = -1;
            bool summaryStep = false;
            foreach (var queued in flow.Fx.Sequencer.Queued)
            {
                var strip = queued.Step(Sinnoh.Fx.FxRows.Handoff);
                if (strip == null) continue;
                bandMs = strip.Ms;
                cutMs = queued.Step(Sinnoh.Fx.FxRows.MyTurn) != null ? queued.Step(Sinnoh.Fx.FxRows.MyTurn).Ms : -1;
                summaryStep = queued.Step(Sinnoh.Fx.FxRows.TurnSummary) != null;
                turnMs = queued.LengthMs;
            }
            long fxStart = flow.Fx.Sequencer.NowMs;
            yield return Wait(() =>
            {
                var seq = flow.Fx.Sequencer;
                var band = seq.Current != null ? seq.Current.Step(Sinnoh.Fx.FxRows.Handoff) : null;
                return band != null && seq.LocalMs >= band.StartMs + band.Ms * 3 / 8;   // 머묾(띠 길이의 1/4 ~ 3/4) 안
            }, 10f);
            bool bandShown = hud.Band.ActiveRow == Sinnoh.Fx.FxRows.Handoff;
            // 마우스는 누른 자리(꺼진 [턴 종료] 위)에 그대로다 — 넘김 동안 단추 툴팁이 다시 뜨지 않는다
            bool noTipWhileClosed = stage.Tips.Target == null && stage.Tips.Element.resolvedStyle.display == DisplayStyle.None;
            yield return Snap("12-board-band");
            yield return Wait(() => flow.InputOpen, 15f);
            long fxTook = flow.Fx.Sequencer.NowMs - fxStart;
            bool beat = bandMs == 500 && cutMs == 500 && turnMs == 1000 && !summaryStep;
            Step("end-turn", "ok", ended && bandShown && flow.InputOpen && beat && noTipWhileClosed, "at", at, "round", client.View.Round, "band", bandShown, "inputOpen", flow.InputOpen,
                "bandMs", bandMs, "cutMs", cutMs, "turnMs", turnMs, "summaryStep", summaryStep, "fxMsUntilInputOpen", (int)fxTook, "noTipWhileClosed", noTipWhileClosed);
            Check("턴 종료", ended, "[턴 종료]를 눌렀는데 라운드가 넘어가지 않았다(" + client.View.Round + ")");
            Check("턴 종료", bandShown, "넘김 띠가 돌지 않았다");
            Check("턴 넘김 박자", beat, "솔로의 턴 넘김이 띠 500 + 컷신 500 = 1000(요약 없음)이 아니다(띠 " + bandMs + " · 컷신 " + cutMs + " · 항목 " + turnMs + " · 요약 " + summaryStep + ")");
            Check("턴 종료 툴팁", noTipWhileClosed, "넘김 동안 꺼진 단추 위에 툴팁이 떴다");
            if (!Check("턴 종료", flow.InputOpen, "다음 라운드의 내 턴에 입력이 열리지 않았다")) { Finish(1, head.ToString()); yield break; }
            yield return Frames(3);

            // ── ⑨-2 걷는 길 고르기(결정 71): 칸 누르기 = 고른 칸 → 다른 칸 누르기 = 경유 칸(흰 테두리 · 길이 그 칸을 거친다) → 오른쪽 클릭 = 경유 칸 빼기 →
            // 다시 경유 칸 → 고른 칸을 다시 눌러 걷기 → 길 가운데(도착 칸 아님)의 뒷면 숨겨진 장소에서 멈추면(남은 길이 칠해져 있다) [지나가기] → 남은 길을 이어 걸어 고른 칸에 ──
            // 짝은 첫 멈춤이 길 가운데인 것을 먼저, 그다음 이동력이 남는 것. 그 라운드에 길 가운데 멈춤 짝이 없으면 [턴 종료]로 다음 라운드에서 다시 찾는다(최대 3라운드)
            {
                Spot dest71 = default, via71 = default;
                int best71 = -1, roundsTried = 0;
                bool mid71 = false;
                List<Spot> cells71 = null;
                for (int r = 0; r < 3; r++)
                {
                    var view71 = client.View;
                    var me71 = view71.Seats[client.Seat];
                    cells71 = OpenCells("걷는 길 고르기", out _);
                    best71 = -1;
                    roundsTried = r + 1;
                    foreach (var d in cells71)
                        foreach (var v in cells71)
                        {
                            if (d.Cell == v.Cell) continue;
                            var route = MoveRules.Route(flow.Ctx.Board, me71.Cell, new[] { v.Cell }, d.Cell, me71.MovesLeft, view71.Options.ElevationMode);
                            var shortest = MoveRules.Path(flow.Ctx.Board, me71.Cell, d.Cell, me71.MovesLeft, view71.Options.ElevationMode);
                            if (route == null || shortest == null || shortest.Contains(v.Cell)) continue;
                            var stopsR = MapActions.StopsOn(view71, flow.Ctx, client.Seat, route.GetRange(1, route.Count - 1));
                            bool mid = stopsR.Count > 0 && stopsR[0] != d.Cell;
                            int score = (mid ? 2 : 0) + (route.Count - 1 < me71.MovesLeft ? 1 : 0);
                            if (score > best71) { best71 = score; dest71 = d; via71 = v; mid71 = mid; }
                        }
                    if (mid71 || r == 2) break;
                    yield return NextRound("걷는 길 고르기 — 길 가운데 멈춤 짝 없음(라운드 " + client.View.Round + ")");
                    if (finished) yield break;
                }
                commands = flow.Session.CommandCount;
                Check("걷는 길 고르기", mid71, "3라운드 동안 화면에서 고를 수 있는 칸 가운데 첫 멈춤이 길 가운데인 경유 칸 짝이 없다(길 가운데 물음 → [지나가기] → 이어 걷기를 지나지 못했다)");
                if (Check("걷는 길 고르기", best71 >= 0, "화면에서 고를 수 있는 칸 가운데 경유 칸 짝이 없다(고를 수 있는 칸 " + cells71.Count + ")"))
                {
                    yield return Click(dest71.Px);
                    bool picked71 = hud.PickedCell == dest71.Cell && hud.Vias.Count == 0;
                    yield return Click(via71.Px);
                    bool via71Ok = hud.PickedCell == dest71.Cell && hud.Vias.Count == 1 && hud.Vias[0] == via71.Cell && new List<int>(board.Marks.Vias).Contains(via71.Cell)
                        && hud.PickedPath != null && hud.PickedPath.Contains(via71.Cell) && flow.Session.CommandCount == commands;
                    bool marks71 = new List<int>(board.Marks.Stops).Count == hud.PathStops.Count;
                    yield return Snap("11b-board-via");
                    yield return RightClick(via71.Px);
                    bool back71 = hud.PickedCell == dest71.Cell && hud.Vias.Count == 0;
                    yield return Click(via71.Px);
                    var walk71 = hud.PickedPath;
                    yield return Click(dest71.Px);
                    bool sent71 = flow.Session.CommandCount == commands + 1;
                    int stops71 = 0, midCell71 = -1;
                    bool midAsk71 = false;
                    string restAsk71 = "", restPainted71 = "";
                    for (int i = 0; i < 4; i++)
                    {
                        yield return Wait(() => flow.InputOpen, 15f);
                        var ask71 = client.Choice(ChoiceKind.HiddenSpot);
                        if (ask71 == null) break;
                        yield return Frames(2);
                        if (stops71 == 0)
                        {
                            // 첫 멈춤 = 길 가운데: 선 칸 ≠ 고른 칸, 물음의 남은 길이 고른 칸으로 끝나고, 보드에 남은 길이 칠해져 있다(BoardMarks.Path = 가운데 칸 · Target = 끝 칸)
                            midCell71 = client.View.Seats[client.Seat].Cell;
                            restAsk71 = string.Join("-", ask71.Options);
                            var painted71 = new List<int>(board.Marks.Path) { board.Marks.Target };
                            restPainted71 = string.Join("-", painted71);
                            midAsk71 = midCell71 != dest71.Cell && ask71.Options.Count > 0 && ask71.Options[ask71.Options.Count - 1] == dest71.Cell
                                && restPainted71 == restAsk71 && board.Marks.Visible;
                            yield return Snap("11c-board-spot-walk");
                        }
                        stops71++;
                        yield return Click(Px(Q("board-hud-act-" + BoardHud.Pass)));
                    }
                    yield return Wait(() => flow.InputOpen, 15f);
                    bool arrived71 = client.View.Seats[client.Seat].Cell == dest71.Cell && flow.Session.CommandCount == commands + 1 + stops71
                        && board.Pieces.Pieces[0].Spot != null && board.Pieces.Pieces[0].Spot.Cell == dest71.Cell;
                    yield return Frames(2);
                    yield return Snap("11d-board-spot-passed");
                    bool passedMid71 = midAsk71 && arrived71;
                    Step("board-via-walk", "ok", picked71 && via71Ok && marks71 && back71 && sent71 && arrived71 && passedMid71, "dest", dest71.Cell, "via", via71.Cell, "path", walk71 != null ? string.Join("-", walk71) : "",
                        "picked", picked71, "viaShown", via71Ok, "marks", marks71, "rightClickBack", back71, "sent", sent71, "spotStops", stops71, "arrived", arrived71, "commands", flow.Session.CommandCount,
                        "roundsTried", roundsTried, "round", client.View.Round, "midPair", mid71, "midStopCell", midCell71, "restAtAsk", restAsk71, "restPainted", restPainted71, "midAsk", midAsk71,
                        "passedMidAndWalkedOn", passedMid71, "movesLeft", client.View.Seats[client.Seat].MovesLeft);
                    Check("걷는 길 고르기", passedMid71, "길 가운데 물음 → [지나가기] → 남은 길 이어 걷기를 지나지 않았다(첫 멈춤 칸 " + midCell71 + " · 고른 칸 " + dest71.Cell + " · 남은 길 " + restAsk71 + " · 칠한 길 " + restPainted71 + ")");
                    Check("걷는 길 고르기", picked71, "칸 " + dest71.Cell + "을 눌렀는데 고른 칸이 되지 않았다");
                    Check("걷는 길 고르기", via71Ok, "고른 뒤 칸 " + via71.Cell + "을 눌렀는데 경유 칸이 되지 않았다(경유 칸 " + hud.Vias.Count + ")");
                    Check("걷는 길 고르기", marks71, "길 위 숨겨진 장소 표지 수가 엔진이 멈출 칸 수와 다르다");
                    Check("걷는 길 고르기", back71, "오른쪽 클릭으로 경유 칸이 빠지지 않았다");
                    Check("걷는 길 고르기", sent71, "고른 칸을 다시 눌렀는데 이동 답이 하나 나가지 않았다");
                    Check("걷는 길 고르기", arrived71, "길 위 숨겨진 장소를 지나간 뒤 고른 칸 " + dest71.Cell + "에 닿지 않았다(말 칸 " + client.View.Seats[client.Seat].Cell + " · 멈춤 " + stops71 + ")");
                }
                // 뒤 단계(창 크기 hover · 메뉴 뒤 칸)는 고를 수 있는 칸이 있어야 한다 — 이동력을 다 썼으면 다음 라운드로
                if (OpenCells("걷는 길 고르기 뒤", out _).Count == 0) yield return NextRound("걷는 길 고르기 뒤 — 고를 수 있는 칸 0");
                if (finished) yield break;
                commands = flow.Session.CommandCount;
            }

            // ── ⑨-3 숨겨진 장소 공개(25행 · rulings A25 — 규칙 끝 감사): 화면에서 고를 수 있는 칸 가운데 길에 다른 숨겨진 장소가 없는 뒷면 숨겨진 장소를 두 번 눌러 걷고 → [공개] →
            // 토큰이 떠서 뒤집히고 앞면으로 머문다 → 공개 연출이 끝나면 맵에서 빠진 결과(규토리 더미 · 유물 · 떨어진 별)는 보드에서 치워지고 야영지는 앞면으로 남는다.
            // 고정 판(찾아 둔 판 · 같은 시드)이라 결과는 늘 같다 — 엔진 원본에서 결과를 보고 규토리 더미 > 유물 > 떨어진 별 > 야영지 순으로 고른다(우두머리는 배틀이 열려 뺀다). 최대 3라운드 ──
            {
                Spot spot25 = default;
                int rank25 = -1, rounds25 = 0;
                string kind25 = "";
                for (int r = 0; r < 3 && rank25 < 0; r++)
                {
                    rounds25 = r + 1;
                    var host25 = flow.Session.HostCopy();
                    foreach (var o in OpenCells("숨겨진 장소 공개", out _))
                    {
                        if (hud.StopsBefore(o.Cell).Count > 0) continue;
                        foreach (var t in host25.Tokens)
                        {
                            if (t.Kind != TokenKind.HiddenSpot || t.FaceUp || t.Destroyed || t.Cell != o.Cell) continue;
                            var item = flow.Ctx.Items.Items[t.Def];
                            int k = HiddenSpotRules.Stash(flow.Ctx, t.Def, out _, out _) ? 3 : item.Kind == ItemKind.Artifact ? 2 : item.Key == HiddenSpotRules.FallenStar ? 1 : item.Key == HiddenSpotRules.Campsite ? 0 : -1;
                            if (k > rank25) { rank25 = k; spot25 = o; kind25 = item.Key; }
                        }
                    }
                    if (rank25 >= 0) break;
                    yield return NextRound("숨겨진 장소 공개 — 고를 수 있는 칸에 뒷면 숨겨진 장소 없음(라운드 " + client.View.Round + ")");
                    if (finished) yield break;
                }
                if (Check("숨겨진 장소 공개", rank25 >= 0, "3라운드 동안 화면에서 고를 수 있는 칸에 공개할 뒷면 숨겨진 장소가 없다"))
                {
                    commands = flow.Session.CommandCount;
                    yield return Click(spot25.Px);
                    yield return Click(spot25.Px);
                    yield return Wait(() => flow.InputOpen && client.Choice(ChoiceKind.HiddenSpot) != null, 15f);
                    bool asked25 = client.Choice(ChoiceKind.HiddenSpot) != null && client.View.Seats[client.Seat].Cell == spot25.Cell;
                    bool raised25 = board.Tokens != null && board.Tokens.RaisedDrawn && board.Tokens.RaisedCell == spot25.Cell;
                    yield return Frames(2);
                    if (asked25) yield return Click(Px(Q("board-hud-act-" + BoardHud.Reveal)));
                    bool sent25 = flow.Session.CommandCount == commands + 2;
                    bool gone25 = rank25 > 0;   // 야영지만 맵에 남는다
                    // 뒤집는 동안 내내 띄운 토큰이 그려지고(공개한 명령 안에서 맵에서 빠져도), 앞면으로 다 뒤집힌다
                    bool drawnAll25 = true, faced25 = false;
                    for (float t0w = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0w < 5f && !faced25;)
                    {
                        yield return null;
                        if (flow.Fx == null || Sinnoh.Fx.SpotBeats.StepOf(flow.Fx.Sequencer.Current) == null) continue;
                        drawnAll25 &= board.Tokens.RaisedDrawn;
                        faced25 = board.Tokens.RaisedFlip >= 1f;
                    }
                    bool front25 = board.Tokens.RaisedDrawn && board.Tokens.RaisedHasFront;
                    yield return Snap("11e-board-spot-revealed");
                    yield return Wait(() => flow.InputOpen, 15f);
                    yield return Frames(2);
                    bool onBoard25 = new List<BoardTokens.Token>(board.Tokens.Shown).Exists(x => x.Kind == TokenKind.HiddenSpot && x.Cell == spot25.Cell);
                    bool settled25 = gone25 ? !onBoard25 : onBoard25;
                    bool ok25 = asked25 && raised25 && sent25 && drawnAll25 && faced25 && (front25 || rank25 == 2) && settled25;   // 유물은 앞면 그림이 Gen에 아직 없다(뒷면 그대로 뒤집힌다)
                    Step("board-spot-reveal", "ok", ok25, "cell", spot25.Cell, "at", spot25.Px, "kind", kind25, "rounds", rounds25, "asked", asked25, "raised", raised25, "sent", sent25, "drawnWhileFlipping", drawnAll25,
                        "faced", faced25, "front", front25, "onBoardAfter", onBoard25, "commands", flow.Session.CommandCount);
                    Check("숨겨진 장소 공개", asked25, "칸 " + spot25.Cell + "을 두 번 눌렀는데 그 칸에서 공개를 묻지 않았다");
                    Check("숨겨진 장소 공개", drawnAll25 && faced25, "공개 연출 동안 띄운 토큰이 그려지지 않았거나 다 뒤집히지 않았다(" + kind25 + ")");
                    Check("숨겨진 장소 공개", settled25, gone25 ? "맵에서 빠진 결과인데 공개 연출 뒤에도 보드에 남았다(" + kind25 + ")" : "야영지인데 공개 뒤 보드에서 사라졌다");
                }
                if (OpenCells("숨겨진 장소 공개 뒤", out _).Count == 0) yield return NextRound("숨겨진 장소 공개 뒤 — 고를 수 있는 칸 0");
                if (finished) yield break;
                commands = flow.Session.CommandCount;
            }

            // ── ⑨-4 무르기 · 되돌리기(결정 149 · 154 — 이 판은 「자유」, 솔로 설정 단계 solo-undo): 길 · 도착 칸에 숨겨진 장소가 없는 칸을 두 번 눌러 걷고 → HUD [무르기]를 마우스로 →
            // 말 · 판이 걷기 전 그대로. 이어 [메뉴] → 「되돌리기」 → 지난 턴 줄(없으면 지금 턴)의 [되돌리기] → 확인 물음 [되돌리기] → 그 턴 처음. 되돌린 판은 HUD를 새로 세우니 hud를 다시 잡는다 ──
            {
                var host9 = flow.Session.HostCopy();
                Spot walk9 = default;
                bool found9 = false;
                foreach (var o in OpenCells("무르기", out _))
                {
                    if (hud.StopsBefore(o.Cell).Count > 0) continue;
                    if (host9.Tokens.Exists(t => t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell == o.Cell)) continue;
                    walk9 = o;
                    found9 = true;
                    break;
                }
                if (Check("무르기", found9, "화면에서 고를 수 있는 칸 가운데 길 · 도착 칸에 숨겨진 장소가 없는 칸이 없다"))
                {
                    int from9 = client.View.Seats[client.Seat].Cell;
                    commands = flow.Session.CommandCount;
                    ulong hash9 = flow.Session.Hash;
                    var hud9 = hud;
                    yield return Click(walk9.Px);
                    yield return Click(walk9.Px);
                    yield return Wait(() => flow.InputOpen, 15f);
                    yield return Frames(2);
                    bool walked9 = client.View.Seats[client.Seat].Cell == walk9.Cell && flow.Session.CommandCount == commands + 1;
                    var undo9 = Q(BoardHud.UndoButton) as Button;
                    bool lit9 = undo9 != null && undo9.enabledSelf && !undo9.ClassListContains(UiStage.OffClass) && flow.UndoOpen;
                    yield return Snap("11f-undo-walked");
                    if (lit9) yield return Click(Px(undo9));
                    yield return Wait(() => flow.InputOpen && flow.Session.CommandCount == commands, 15f);
                    yield return Frames(3);
                    hud = flow.Hud;
                    bool back9 = flow.Session.CommandCount == commands && flow.Session.Hash == hash9 && client.View.Seats[client.Seat].Cell == from9 && flow.Session.Undos == 1
                        && board.Pieces.Pieces[0].Spot != null && board.Pieces.Pieces[0].Spot.Cell == from9 && hud != null && hud != hud9;
                    yield return Snap("11g-undo-pressed");
                    Step("undo-press", "ok", walked9 && lit9 && back9, "cell", walk9.Cell, "at", walk9.Px, "from", from9, "walked", walked9, "lit", lit9, "back", back9,
                        "commands", flow.Session.CommandCount, "undos", flow.Session.Undos, "piece", board.Pieces.Pieces[0].Spot != null ? board.Pieces.Pieces[0].Spot.Cell : -1);
                    Check("무르기", walked9, "칸 " + walk9.Cell + "을 두 번 눌렀는데 걷지 않았다");
                    Check("무르기", lit9, "걸은 뒤 HUD [무르기]가 켜지지 않았다(목표 " + (flow.Undo != null ? flow.Undo.StepTo : -2) + ")");
                    if (!Check("무르기", back9, "[무르기]를 눌렀는데 말 · 판이 걷기 전으로 돌아가지 않았다(말 칸 " + client.View.Seats[client.Seat].Cell + " · 걷기 전 " + from9 + ")")) { Finish(1, head.ToString()); yield break; }
                }
                yield return Click(Px(Q("board-hud-menu")));
                yield return Frames(3);
                bool menu9 = stage.Current is BoardMenuScreen && Q("board-menu-rewind") != null;
                if (menu9) yield return Click(Px(Q("board-menu-rewind")));
                yield return Frames(3);
                var page9 = stage.Current as BoardMenuScreen;
                var points9 = page9 != null && page9.Page == BoardMenuScreen.RewindPage ? page9.RewindPoints : null;
                int pick9 = points9 != null && points9.Count > 1 ? 1 : 0;
                bool listed9 = points9 != null && points9.Count > 0 && points9[0].Current && Q("board-menu-rewind-row-0-now") != null && Q("board-menu-rewind-row-" + pick9 + "-go") != null;
                yield return Snap("11h-undo-rewind-page");
                if (listed9) yield return Click(Px(Q("board-menu-rewind-row-" + pick9 + "-go")));
                yield return Frames(3);
                var target9 = page9 != null ? page9.RewindAsking : null;
                bool asked9 = target9 != null && Q("board-menu-rewind-yes") != null;
                yield return Snap("11i-undo-rewind-ask");
                int epoch9 = flow.Session.Epoch;
                if (asked9) yield return Click(Px(Q("board-menu-rewind-yes")));
                yield return Wait(() => flow.InputOpen && !flow.MenuUp, 15f);
                yield return Frames(3);
                hud = flow.Hud;
                bool rewound9 = target9 != null && flow.Session.CommandCount == target9.To && client.View.Round == target9.Round && flow.Session.Epoch == epoch9 + 1 && !flow.MenuUp && hud != null;
                yield return Snap("11j-undo-rewound");
                Step("undo-rewind", "ok", menu9 && listed9 && asked9 && rewound9, "at", at, "menu", menu9, "points", points9 != null ? points9.Count : -1, "pick", pick9, "listed", listed9, "asked", asked9,
                    "to", target9 != null ? target9.To : -1, "round", client.View.Round, "commands", flow.Session.CommandCount, "rewound", rewound9);
                Check("되돌리기", menu9, "[메뉴]에 「되돌리기」가 없다(자유 판인데)");
                Check("되돌리기", listed9, "「되돌리기」를 눌렀는데 지난 턴 목록(지금 턴 꼬리표 · [되돌리기])이 서지 않았다");
                Check("되돌리기", asked9, "줄의 [되돌리기]를 눌렀는데 확인 물음이 뜨지 않았다");
                if (!Check("되돌리기", rewound9, "확인 물음의 [되돌리기]를 눌렀는데 그 턴 처음으로 돌아가지 않았다(명령 " + flow.Session.CommandCount + " · 목표 " + (target9 != null ? target9.To : -1) + ")")) { Finish(1, head.ToString()); yield break; }
                if (OpenCells("되돌리기 뒤", out _).Count == 0) yield return NextRound("되돌리기 뒤 — 고를 수 있는 칸 0");
                if (finished) yield break;
                commands = flow.Session.CommandCount;
            }

            // ── ⑩ 패널이 보드 위에 겹쳐 그려지는가(창) + 창 크기 → 등급(창) ──
            if (windowed)
            {
                yield return Move(new Vector2(root.Size.x * 0.5f, root.Size.y * 0.5f));
                int over = 0, through = 0;
                yield return Snap("13-window-" + root.Size.x + "x" + root.Size.y, (a, b) => { over = a; through = b; });
                int total = root.Size.x * root.Size.y;
                bool overlapOk = over > total / 50 && through > total / 20;
                Step("window-overlap", "ok", overlapOk, "size", new Vector2(root.Size.x, root.Size.y), "uiOverPixels", over, "boardThroughPixels", through, "pixels", total);
                Check("창 겹침", overlapOk, "백버퍼에서 패널이 덮은 곳(" + over + ")이나 패널 밑으로 비치는 보드(" + through + ")가 너무 적다");
                for (int i = 1; i < sizes.Count; i++)
                {
                    var size = sizes[i];
                    Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
                    yield return Wait(() => Screen.width == size.x && Screen.height == size.y && root.Size == size, 5f);
                    yield return Frames(6);
                    yield return CheckSize(size);
                    if (TimedOut) { Fail("시간", "시간 초과"); Finish(2, head.ToString()); yield break; }
                }
            }

            // ── ⑪ [메뉴] → 메뉴 뒤의 칸·Q는 듣지 않는다 → 나가기 → 타이틀 ──
            commands = flow.Session.CommandCount;
            var behind = OpenCells("메뉴 앞", out _);
            int reachBefore = hud.Reach.Count;
            yield return Click(Px(Q("board-hud-menu")));
            bool menuOpen = stage.Current is BoardMenuScreen;
            Step("menu-click-open", "ok", menuOpen, "at", at);
            if (!Check("메뉴", menuOpen, "[메뉴]를 눌렀는데 게임 메뉴가 열리지 않았다")) { Finish(1, head.ToString()); yield break; }
            yield return Frames(3);
            int yawWas = rig.Yaw;
            var zoomMenu = rig.CurrentZoom;
            bool haveBehind = Check("메뉴 뒤 칸", behind.Count > 0, "메뉴를 열기 전에 고를 수 있는 칸이 없어 메뉴 뒤 칸 누르기를 보지 못했다(갈 수 있는 칸 " + reachBefore + ")");
            var behindPx = haveBehind ? behind[0].Px : at;
            if (haveBehind) yield return Click(behindPx);
            yield return Tap(Key.Q);
            yield return Wheel(-1);
            bool blocked = flow.Session.CommandCount == commands && rig.Yaw == yawWas && rig.CurrentZoom == zoomMenu && stage.Current is BoardMenuScreen;
            Step("menu-blocks-board", "ok", blocked && haveBehind, "behind", behind.Count, "behindCell", haveBehind ? behind[0].Cell : -1, "at", behindPx, "commands", flow.Session.CommandCount, "yaw", rig.Yaw, "zoom", rig.CurrentZoom.ToString());
            Check("메뉴", blocked, "메뉴가 열려 있는데 뒤의 칸 누르기·Q·휠이 들었다");
            // 게임 메뉴의 [설정](결정 70 ⑨): 같은 설정 화면이 HUD 위에 열리고(카메라는 그대로) [뒤로]는 게임 메뉴로 돌아온다
            {
                var lookWas = rig.transform.position;
                yield return Click(Px(Q("board-menu-settings")));
                yield return Frames(3);
                bool inSettings = stage.Current is SettingsScreen set0 && set0.InGame && Q("settings-slider-bgm") != null && stage.Hud != null;
                yield return Snap("13b-menu-settings");
                bool camKept = rig.transform.position == lookWas && rig.CurrentZoom == zoomMenu && rig.Yaw == yawWas;
                if (inSettings) yield return Click(Px(Q("settings-back")));
                yield return Frames(3);
                bool backToMenu = stage.Current is BoardMenuScreen && flow.Session.CommandCount == commands;
                Step("menu-settings", "ok", inSettings && camKept && backToMenu, "at", at, "open", inSettings, "camKept", camKept, "back", backToMenu);
                Check("메뉴 설정", inSettings, "게임 메뉴의 [설정]을 눌렀는데 설정 화면이 HUD 위에 열리지 않았다");
                Check("메뉴 설정", camKept, "게임 중 설정을 열었는데 카메라가 움직였다");
                if (!Check("메뉴 설정", backToMenu, "설정의 [뒤로]를 눌렀는데 게임 메뉴로 돌아오지 않았다")) { Finish(1, head.ToString()); yield break; }
            }
            // 게임 메뉴의 [버그 신고](결정 132 ①): 신고 쪽이 열리고 빈 설명이면 [보내기]가 꺼져 있다 → [취소] = 메뉴로(보내지 않는다 — 확인용 창구 PlayUploads.Quiet)
            {
                yield return Click(Px(Q("board-menu-bug")));
                yield return Frames(3);
                var bugMenu = stage.Current as BoardMenuScreen;
                bool bugOpen = bugMenu != null && bugMenu.Bug != null && Q("bug-text") != null && Q("bug-send") is Button bugSend && !bugSend.enabledSelf;
                yield return Snap("13c-menu-bug");
                if (bugOpen) yield return Click(Px(Q("bug-cancel")));
                yield return Frames(3);
                bool bugBack = stage.Current is BoardMenuScreen m2 && m2.Page == BoardMenuScreen.MainPage && Q("board-menu-quit") != null && flow.Session.CommandCount == commands;
                Step("menu-bug", "ok", bugOpen && bugBack, "at", at, "open", bugOpen, "back", bugBack);
                if (!Check("메뉴 버그 신고", bugOpen && bugBack, "[버그 신고] → 신고 쪽(빈 설명 = [보내기] 꺼짐) → [취소] = 메뉴가 아니다")) { Finish(1, head.ToString()); yield break; }
            }
            ulong leftHash = flow.Session.Hash;
            int leftCount = flow.Session.CommandCount;
            yield return Click(Px(Q("board-menu-quit")));
            var menu = stage.Current as BoardMenuScreen;
            bool asking = menu != null && menu.Asking && Q("board-menu-quit-yes") != null;
            yield return Snap("14-menu-ask");
            Step("menu-click-quit", "ok", asking, "at", at);
            if (!Check("메뉴", asking, "[타이틀로 나가기]를 눌렀는데 묻는 창이 뜨지 않았다")) { Finish(1, head.ToString()); yield break; }
            yield return Click(Px(Q("board-menu-quit-yes")));
            yield return Frames(4);
            bool backToTitle = stage.Current is TitleScreen && flow.Session == null && stage.Hud == null;
            yield return Snap("15-title-again");
            Step("menu-click-quit-yes", "ok", backToTitle, "at", at, "screen", stage.Current != null ? stage.Current.Name : "");
            Check("나가기", backToTitle, "[나가기]를 눌렀는데 타이틀로 돌아오지 않았다");
            if (backToTitle && flow.SaveRoot != null) yield return Saves(leftHash, leftCount);
            if (finished) yield break;
            if (stage.Current is TitleScreen) yield return Dex();
            if (stage.Current is TitleScreen) yield return Rulebook();
            if (stage.Current is TitleScreen) yield return Settings();
            if (finished) yield break;
            yield return Battle();
            if (finished) yield break;
            yield return Encounter();
            if (finished) yield break;
            yield return MaintDrag();
            if (finished) yield break;
            yield return Work();   // M5 「제작·은행·진화 화면」: 제작 창 · 은행상회 · 진화 창
            if (finished) yield break;
            yield return Stacks();
            if (finished) yield break;
            yield return Finale();
            if (finished) yield break;
            yield return Fate();
            if (finished) yield break;
            yield return Shrine();   // M5 「사당·균열 화면」: 사당 판(공물 · 3장 고르기 · 추종자)
            if (finished) yield break;
            yield return Deal();     // M5 「스탬프·도전·거래 화면」: 스탬프 판 · 거래 창([제안] → 수락 → 주고받기)
            if (finished) yield break;
            yield return Left();     // M5 마감 답 반영 나-1: 정비 판의 [폼체인지] · 배틀 차례 칸의 [대기 포켓몬 기술]
            if (finished) yield break;
            yield return Card();     // M6 트레이너 카드 화면: 행동 단추 줄의 능력 단추 → 칸 고르기 → [취소]
            if (finished) yield break;
            yield return FlyMap();   // M9 T18 ①: [마을] → [지도에서 고르기] → 보드의 마을 칸 누르기 → [취소] → 카메라 되돌림
            if (finished) yield break;
            yield return Ub();       // M6 「UB 시작」: 타이틀 → 혼자 하기 → 울트라 버스트 → 셋업 → 보드
            if (finished) yield break;
            yield return Peek();     // M9 플레이 시험 T35 · 결정 125 · T21: 균열 카드 창 · 축복 물음 위의 게임 메뉴 · [상성] · [정비] · [도감] → 닫으면 물음이 남는다 · 접은 물음 아래 보드 끌기
            if (finished) yield break;
            yield return Tutorial(); // M6.5 마감: 타이틀 [튜토리얼] → 목차 → 장 고르기 → 말풍선 [다음] → [튜토리얼 끝내기] → [목차로] → [뒤로]
            if (finished) yield break;

            head.Append(",\"focusedAtEnd\":").Append(Application.isFocused ? "true" : "false");
            Application.runInBackground = runInBackgroundWas;
            Finish(-1, head.ToString());
        }

        /// <summary>지금 창 크기(size)에서 등급·패널 크기·아래 판 자리 · 칸 올리기 · [턴 종료] 툴팁을 보고 백버퍼를 찍는다.</summary>
        IEnumerator CheckSize(Vector2Int size)
        {
            string label = "창 크기 " + size.x + "x" + size.y;
            var hud = flow.Hud;
            var want = UiGrade.For(size.x, size.y);
            var layout = stage.Root.layout;
            var dock = Q("board-hud-dock").worldBound;
            bool sized = Screen.width == size.x && Screen.height == size.y && root.Size == size;
            bool graded = stage.Grade.Equals(want) && Mathf.Approximately(layout.width, (float)size.x / want.PanelScale) && Mathf.Approximately(layout.height, (float)size.y / want.PanelScale);
            bool docked = dock.yMax <= layout.height + 0.5f && dock.yMax > layout.height * 0.9f && dock.xMax <= layout.width + 0.5f;
            // 바뀐 크기에서도 마우스 자리가 맞는가: 칸 올리기 + 단추 올리기(툴팁)
            var resized = OpenCells(size.x + "x" + size.y, out float offR);
            bool hoverR = false;
            int cellR = -1;
            if (resized.Count > 0)
            {
                cellR = resized[0].Cell;
                yield return Move(resized[0].Px);
                hoverR = hud.HoverCell == cellR && board.Marks.Target == cellR;
            }
            var end = Q("board-hud-act-" + BoardHud.EndTurn);
            yield return Move(Px(end));
            yield return Frames(2);
            var tip = stage.Tips.Element.worldBound;
            bool tipR = stage.Tips.Target == end;
            bool tipInside = tipR && tip.xMin >= -0.5f && tip.yMin >= -0.5f && tip.xMax <= layout.width + 0.5f && tip.yMax <= dock.yMin + 0.5f;
            int over = 0, through = 0;
            yield return Snap("13-window-" + size.x + "x" + size.y, (a, b) => { over = a; through = b; });
            Step("window-resize", "ok", sized && graded && docked && hoverR && tipR && tipInside, "size", new Vector2(size.x, size.y), "screen", new Vector2(Screen.width, Screen.height), "grade", stage.Grade.Height,
                "scale", stage.Grade.PanelScale, "root", new Vector2(layout.width, layout.height), "dockBottom", dock.yMax, "dockRight", dock.xMax, "docked", docked,
                "hoverCell", cellR, "hoverOk", hoverR, "tipOk", tipR, "tipInsideAboveDock", tipInside, "pickVsEngineMaxPx", offR, "uiOverPixels", over, "boardThroughPixels", through);
            Check(label, sized, "창이 그 크기가 되지 않았다(" + Screen.width + "x" + Screen.height + ")");
            Check(label, graded, "등급·패널 크기가 따라오지 않았다(등급 " + stage.Grade + " ≠ " + want + ", 뿌리 " + layout.width + "x" + layout.height + ")");
            Check(label, docked, "아래 판이 창 아래에 붙지 않았다(" + dock + ")");
            Check(label, hoverR, "바뀐 크기에서 마우스를 올린 칸이 칠해지지 않았다(고를 수 있는 칸 " + resized.Count + ")");
            Check(label, tipR, "바뀐 크기에서 [턴 종료]에 올렸는데 툴팁이 뜨지 않았다");
            Check(label, tipInside, "바뀐 크기에서 [턴 종료] 툴팁이 창 밖이나 아래 판 안에 있다(" + stage.Tips.Element.worldBound + ")");
            Check(label, offR < 1.5f, "바뀐 크기에서 칸 자리가 " + offR.ToString("0.##", inv) + "px 어긋난다");
        }

        /// <summary>
        /// ⑪-3 포켓몬 도감(S42, 결정 105 ③): 타이틀 「포켓몬 도감」 → 덱 「진화」 → 판 → 같은 판(상세 쪽) → Esc(목록) → Esc → 타이틀.
        /// </summary>
        IEnumerator Dex()
        {
            yield return Click(Px(Q("title-item-" + TitleScreen.Dex)));
            var dex = stage.Current as DexScreen;
            Step("title-click-dex", "ok", dex != null, "at", at, "screen", stage.Current != null ? stage.Current.Name : "", "cards", dex != null ? dex.Shown.Count : -1);
            if (!Check("도감", dex != null, "「포켓몬 도감」을 눌렀는데 도감이 열리지 않았다")) yield break;
            yield return Frames(3);
            yield return Click(Px(Q("dex-deck-" + DexBook.Id(DexDeck.Evolution))));
            bool deck = dex.Deck == DexDeck.Evolution && dex.Shown.Count > DexScreen.Rows;
            yield return Frames(3);
            yield return Click(Px(Q("dex-plate-3")));
            bool picked = dex.Picked == 3 && dex.Viewed == dex.Shown[3].Def && !dex.Detail;
            yield return Frames(3);
            yield return Click(Px(Q("dex-plate-3")));   // 고른 판을 다시 누르면 상세 쪽
            bool detail = dex.Detail && Q("dex-detail") != null;
            yield return Snap("17a-dex");
            Step("dex-click-plate", "ok", deck && picked && detail, "at", at, "deck", dex.Deck.ToString(), "picked", dex.Picked, "detail", dex.Detail);
            Check("도감 조작", deck && picked && detail, "도감의 덱 · 판 · 고른 판(상세 쪽)을 눌렀는데 바뀌지 않았다");
            yield return Tap(Key.Escape);
            bool listed = stage.Current is DexScreen && !dex.Detail;
            yield return Tap(Key.Escape);
            bool back = listed && stage.Current is TitleScreen;
            Step("dex-key-esc", "ok", back, "listed", listed, "screen", stage.Current != null ? stage.Current.Name : "");
            Check("도감 닫기", back, "Esc를 눌렀는데 도감이 닫히지 않았다");
        }

        /// <summary>
        /// ⑪-4 룰 설명서(S43 B — 결정 111 ①) · 크레딧(S03): 타이틀 「룰 설명서」 → 목차의 장(접기) → 절 → [다음 절] → Esc → 타이틀 「크레딧」 → 묶음 「Smogon」 → [뒤로] → 타이틀.
        /// </summary>
        IEnumerator Rulebook()
        {
            yield return Click(Px(Q("title-item-" + TitleScreen.Rulebook)));
            var book = stage.Current as RulebookScreen;
            Step("title-click-rulebook", "ok", book != null, "at", at, "screen", stage.Current != null ? stage.Current.Name : "");
            if (!Check("룰 설명서", book != null, "「룰 설명서」를 눌렀는데 큰 창이 열리지 않았다")) yield break;
            yield return Frames(3);
            yield return Click(Px(Q("rulebook-chapter-pokemon")));   // 접힌 장을 편다
            bool opened = Q("rulebook-section-pokemon.capture") != null;
            yield return Frames(3);
            if (opened) yield return Click(Px(Q("rulebook-section-pokemon.capture")));
            bool shown = book.Current != null && book.Current.Key == "pokemon.capture";
            yield return Frames(3);
            yield return Click(Px(Q("rulebook-next")));
            bool next = book.Current != null && book.Current.Key != "pokemon.capture";
            yield return Snap("17b-rulebook");
            Step("rulebook-click-tree", "ok", opened && shown && next, "at", at, "opened", opened, "shown", shown, "section", book.Current != null ? book.Current.Key : "");
            Check("룰 설명서 조작", opened && shown && next, "목차의 장 · 절 · [다음 절]을 눌렀는데 바뀌지 않았다");
            yield return Tap(Key.Escape);
            bool back = stage.Current is TitleScreen;
            Step("rulebook-key-esc", "ok", back, "screen", stage.Current != null ? stage.Current.Name : "");
            if (!Check("룰 설명서 닫기", back, "Esc를 눌렀는데 큰 창이 닫히지 않았다")) yield break;
            yield return Click(Px(Q("title-item-" + TitleScreen.Credits)));
            var credits = stage.Current as CreditsScreen;
            if (!Check("크레딧", credits != null, "「크레딧」을 눌렀는데 크레딧이 열리지 않았다")) yield break;
            yield return Frames(3);
            yield return Click(Px(Q("credits-tab-art")));
            bool tab = credits.Picked.Id == "art" && Q("credits-entry-0") != null;
            yield return Snap("17c-credits");
            yield return Click(Px(Q("credits-back")));
            bool title = stage.Current is TitleScreen;
            Step("credits-click", "ok", tab && title, "at", at, "tab", credits.Picked.Id, "screen", stage.Current != null ? stage.Current.Name : "");
            Check("크레딧 조작", tab && title, "크레딧의 묶음 · [뒤로]를 눌렀는데 바뀌지 않았다");
        }

        /// <summary>
        /// ⑫ 설정(S02, 결정 59): 타이틀 「설정」 → 「화면 크기」의 값을 마우스로 누른다 → 창 크기·등급이 따라온다 → 「전체 화면」 → 다시 창 → [뒤로] → 타이틀.
        /// 누르는 차례: 지금 크기가 아닌 값들(목록 순서) → 전체 화면 → 띄울 때와 다른 창 크기 하나로 끝낸다(엔진이 저장한 값이 마지막에 고른 것인지 띄운 쪽이 본다 — JSON <c>settingsLast</c>).
        /// 창이 없으면(-batchmode) 창 크기를 바꿀 수 없어 열기 · 값이 판 안에 놓였는지 · [뒤로]만 본다.
        /// </summary>
        IEnumerator Settings()
        {
            yield return Click(Px(Q("title-item-" + TitleScreen.Settings)));
            var set = stage.Current as SettingsScreen;
            Step("title-click-settings", "ok", set != null, "at", at, "screen", stage.Current != null ? stage.Current.Name : "", "sizes", set != null ? set.Sizes.Count : -1, "picked", set != null ? set.Picked ?? "" : "",
                "monitor", new Vector2(Display.main.systemWidth, Display.main.systemHeight));
            if (!Check("설정", set != null, "「설정」을 눌렀는데 설정 화면이 열리지 않았다")) { Finish(1, head.ToString()); yield break; }
            yield return Frames(3);
            yield return SettingsLaid("설정 " + root.Size.x + "x" + root.Size.y, set);
            if (windowed)
            {
                var order = new List<string>();
                foreach (var s in set.Sizes) if (SettingsScreen.Id(s.W, s.H) != set.Picked) order.Add(SettingsScreen.Id(s.W, s.H));
                order.Add(SettingsScreen.Full);
                string last = null;
                foreach (var s in set.Sizes) if (last == null && (sizes.Count == 0 || s.W != sizes[0].x || s.H != sizes[0].y)) last = SettingsScreen.Id(s.W, s.H);
                if (Check("설정", last != null, "띄울 때와 다른 창 크기가 목록에 없다(목록 " + set.Sizes.Count + ")")) order.Add(last);
                foreach (string id in order)
                {
                    bool full = id == SettingsScreen.Full;
                    var want = new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);
                    if (!full)
                    {
                        string[] wh = id.Split('x');
                        want = new Vector2Int(int.Parse(wh[0], inv), int.Parse(wh[1], inv));
                    }
                    var wantMode = full ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                    string label = "설정 화면 크기 " + (full ? "전체 화면" : id);
                    yield return Click(Px(Q("settings-opt-window-" + id)));
                    yield return Wait(() => Screen.width == want.x && Screen.height == want.y && root.Size == want && Screen.fullScreenMode == wantMode, 5f);
                    yield return Frames(6);
                    var grade = UiGrade.For(want.x, want.y);
                    var layout = stage.Root.layout;
                    bool sized = Screen.width == want.x && Screen.height == want.y && root.Size == want;
                    bool mode = Screen.fullScreenMode == wantMode;
                    bool graded = stage.Grade.Equals(grade) && Mathf.Approximately(layout.width, (float)want.x / grade.PanelScale) && Mathf.Approximately(layout.height, (float)want.y / grade.PanelScale);
                    bool shown = set.Picked == id && Q("settings-opt-window-" + id).ClassListContains("sn-opt-picked") && Q("settings-row-window").Query(className: "sn-opt-picked").ToList().Count == 1;
                    yield return Snap("16-settings-" + id);
                    Step("settings-pick", "ok", sized && mode && graded && shown, "id", id, "at", at, "screen", new Vector2(Screen.width, Screen.height), "fullScreenMode", Screen.fullScreenMode.ToString(),
                        "grade", stage.Grade.Height, "scale", stage.Grade.PanelScale, "root", new Vector2(layout.width, layout.height), "picked", set.Picked ?? "");
                    Check(label, sized, "눌렀는데 창이 그 크기가 되지 않았다(" + Screen.width + "x" + Screen.height + " · 뿌리 " + root.Size + ")");
                    Check(label, mode, "창 모드·전체 화면이 맞지 않다(" + Screen.fullScreenMode + ")");
                    Check(label, graded, "등급·패널 크기가 따라오지 않았다(등급 " + stage.Grade + " ≠ " + grade + ", 뿌리 " + layout.width + "x" + layout.height + ")");
                    Check(label, shown, "누른 값이 고른 값으로 표시되지 않았다(" + set.Picked + ")");
                    yield return SettingsLaid(label, set);
                    if (TimedOut) { Fail("시간", "시간 초과"); Finish(2, head.ToString()); yield break; }
                }
                head.Append(",\"settingsLast\":\"").Append(set.Picked ?? "").Append('"');
            }
            // 「말 그림」 줄: 직업 그림을 누르면 뒤의 보드 말(시험 판 6)이 바로 직업 그림으로 바뀌고, 주인공 도트를 누르면 돌아온다
            {
                yield return Click(Px(Q("settings-opt-piece-" + SettingsScreen.PieceJob)));
                yield return Frames(2);
                bool job = flow.PieceStyle == PieceStyle.Job && board.Pieces.Pieces.Count == Sinnoh.Data.PieceLayout.MaxSeats && board.Pieces.Pieces[0].Style == PieceStyle.Job
                    && Q("settings-opt-piece-" + SettingsScreen.PieceJob).ClassListContains("sn-opt-picked") && !Q("settings-opt-piece-" + SettingsScreen.PieceDot).ClassListContains("sn-opt-picked");
                yield return Snap("16-settings-piece-job");
                yield return Click(Px(Q("settings-opt-piece-" + SettingsScreen.PieceDot)));
                yield return Frames(2);
                bool dot = flow.PieceStyle == PieceStyle.Dot && board.Pieces.Pieces.Count == Sinnoh.Data.PieceLayout.MaxSeats && board.Pieces.Pieces[0].Style == PieceStyle.Dot
                    && Q("settings-opt-piece-" + SettingsScreen.PieceDot).ClassListContains("sn-opt-picked");
                Step("settings-piece", "ok", job && dot, "at", at, "job", job, "dot", dot, "pieces", board.Pieces.Pieces.Count);
                Check("설정 말 그림", job, "「직업 그림」을 눌렀는데 보드 말이 직업 그림으로 바뀌지 않았다");
                Check("설정 말 그림", dot, "「주인공 도트」를 눌렀는데 보드 말이 주인공 도트로 돌아오지 않았다");
            }
            // 「배경음악」·「효과음」 줄(결정 66 ④ · 결정 70 ⑭ 슬라이더): 배경음악 홈의 안쪽 오른쪽 끝을 누름 → 가장 큰 단계 · 곡 음원 음량이 그 값 → 손잡이를 잡고 왼쪽 끝 밖까지 끎 → 끔 ·
            // 효과음 안쪽 왼쪽 끝 → 확정 소리가 음량 0 · 안쪽 오른쪽 끝 → 확정 소리가 그 음량 · 기본으로 되돌림. 누르는 자리는 홈 안쪽(PxIn — 앞 감사는 오른쪽 끝 바로 바깥을 눌러 실패했다).
            // 이 플레이어의 음량 설정은 실행하는 동안만이다(사용자 값을 건드리지 않는다 — ScreenBoot.DefaultSoundPrefs)
            {
                var sound = boot.Sound;
                int bgmWas = flow.SoundPrefs.Bgm, seWas = flow.SoundPrefs.Se;
                int max = Sinnoh.Fx.SoundMix.MaxStep;
                var bgmBar = Q("settings-slider-bgm");
                yield return Click(PxIn(bgmBar, 1f));
                yield return Frames(2);
                float want = Sinnoh.Fx.SoundMix.Music(max, 0) * (sound != null && !string.IsNullOrEmpty(sound.MusicFile) ? 1f : 0f);
                bool bgmMax = flow.SoundPrefs.Bgm == max && ((Label)Q("settings-val-bgm")).text == max.ToString()
                    && sound != null && Mathf.Abs(sound.MusicSource.volume - want) < 0.02f;
                // 손잡이(가운데)를 잡고 왼쪽 끝 밖까지 끈다(끄는 동안 슬라이더가 포인터를 잡는다) → 끔
                var bgmKnob = bgmBar.Q("kit-slider-knob");
                yield return Drag(bgmKnob != null && Laid(bgmKnob) ? Px(bgmKnob) : PxIn(bgmBar, 1f), PxAt(bgmBar, -0.2f), 6);
                Queue(at, false);
                yield return Frames(3);
                bool bgm = bgmMax && flow.SoundPrefs.Bgm == 0 && sound != null && sound.MusicSource.volume == 0f;
                int heardAt = sound != null ? sound.Heard.Count : 0;
                yield return Click(PxIn(Q("settings-slider-se"), 0f));
                yield return Frames(2);
                yield return Click(PxIn(Q("settings-slider-se"), 1f));
                yield return Frames(2);
                var decides = new List<SoundHeard>();
                if (sound != null) for (int i = heardAt; i < sound.Heard.Count; i++) if (sound.Heard[i].Key == Sinnoh.Fx.SoundKeys.Decide && sound.Heard[i].From == "now") decides.Add(sound.Heard[i]);
                var decideEntry = sound != null ? sound.Effects.Find(Sinnoh.Fx.SoundKeys.Decide) : null;
                bool se = flow.SoundPrefs.Se == max && decides.Count == 2 && decides[0].Volume == 0f
                    && decideEntry != null && Mathf.Approximately(decides[1].Volume, Sinnoh.Fx.SoundMix.Effect(max, decideEntry.Group, decideEntry.Db)) && decides[1].Clip != null;
                yield return Snap("16-settings-sound");
                Step("settings-sound", "ok", bgm && se, "at", at, "bgm", bgm, "bgmMax", bgmMax, "se", se, "musicFile", sound != null ? sound.MusicFile ?? "" : "", "musicVolume", sound != null ? sound.MusicSource.volume : -1f,
                    "decides", decides.Count, "decideClip", decides.Count > 1 ? decides[1].Clip ?? "" : "");
                Check("설정 음량", bgmMax, "「배경음악」 슬라이더의 오른쪽 끝을 눌렀는데 곡 음량이 가장 큰 단계가 되지 않았다(" + flow.SoundPrefs.Bgm + ")");
                Check("설정 음량", bgm, "「배경음악」 손잡이를 왼쪽 끝까지 끌었는데 끔이 되지 않았다");
                Check("설정 음량", se, "「효과음」 슬라이더의 왼쪽 끝·오른쪽 끝을 눌렀는데 확정 소리가 그 음량으로 나지 않았다(난 것 " + decides.Count + ")");
                flow.SoundPrefs.Bgm = bgmWas;
                flow.SoundPrefs.Se = seWas;
            }
            // 「연출」 줄(04 §8-1 P17): 빠름 → 끄기 → 처음 값. 고른 칸 하나만 칠해지고 흐름의 연출 속도가 그 값이 된다(실행하는 동안만 — 사용자 값을 건드리지 않는다)
            {
                var fxWas = flow.FxSpeed;
                int tapAt = boot.Sound != null ? boot.Sound.Heard.Count : 0;
                yield return Click(Px(Q("settings-opt-fx-" + SettingsScreen.FxId(Sinnoh.Fx.FxSpeed.Fast))));
                yield return Frames(2);
                // 즉시 소리(04 §8-4): 고르기 칸을 마우스로 누르면 고르기 소리(SELECT)가 「now」로 난다
                bool tapSelect = false;
                if (boot.Sound != null) for (int i = tapAt; i < boot.Sound.Heard.Count; i++) if (boot.Sound.Heard[i].Key == Sinnoh.Fx.SoundKeys.Select && boot.Sound.Heard[i].From == "now" && boot.Sound.Heard[i].Clip != null) tapSelect = true;
                Check("즉시 소리", tapSelect, "「연출」 칸을 눌렀는데 고르기 소리(select)가 나지 않았다");
                bool fast = flow.FxSpeed == Sinnoh.Fx.FxSpeed.Fast && Q("settings-opt-fx-fast").ClassListContains("sn-opt-picked") && Q("settings-row-fx").Query(className: "sn-opt-picked").ToList().Count == 1;
                yield return Click(Px(Q("settings-opt-fx-" + SettingsScreen.FxId(Sinnoh.Fx.FxSpeed.Off))));
                yield return Frames(2);
                bool off = flow.FxSpeed == Sinnoh.Fx.FxSpeed.Off && Q("settings-opt-fx-off").ClassListContains("sn-opt-picked") && Q("settings-row-fx").Query(className: "sn-opt-picked").ToList().Count == 1;
                yield return Snap("16-settings-fx");
                Step("settings-fx", "ok", fast && off, "at", at, "fast", fast, "off", off);
                Check("설정 연출", fast, "「빠름」을 눌렀는데 연출 속도가 빠름이 되지 않았다");
                Check("설정 연출", off, "「끄기」를 눌렀는데 연출 속도가 끄기가 되지 않았다");
                flow.FxSpeed = fxWas;
            }
            // 「언어」 줄(M9 언어 ②): English → 설정 화면이 영어로 다시 그려지고 고른 칸이 칠해진다 → 한국어로 되돌림(실행하는 동안만 — 사용자 값을 건드리지 않는다)
            if (Q("settings-row-lang") != null)
            {
                string langWas = flow.Lang;
                yield return Click(Px(Q("settings-opt-lang-en")));
                yield return Frames(2);
                var enTable = stage.TableOf("en");
                bool en = flow.Lang == "en" && enTable != null && Q("settings-head").Q<Label>().text == enTable["screen.settings.title"]
                    && Q("settings-opt-lang-en").ClassListContains("sn-opt-picked") && Q("settings-row-lang").Query(className: "sn-opt-picked").ToList().Count == 1;
                yield return Snap("16-settings-lang-en");
                yield return Click(Px(Q("settings-opt-lang-ko")));
                yield return Frames(2);
                var koTable = stage.TableOf("ko");
                bool ko = flow.Lang == "ko" && koTable != null && Q("settings-head").Q<Label>().text == koTable["screen.settings.title"] && Q("settings-opt-lang-ko").ClassListContains("sn-opt-picked");
                Step("settings-lang", "ok", en && ko, "at", at, "en", en, "ko", ko);
                Check("설정 언어", en, "「English」를 눌렀는데 설정 화면이 영어로 다시 그려지지 않았다");
                Check("설정 언어", ko, "「한국어」를 눌렀는데 한국어로 돌아오지 않았다");
                if (langWas != null && langWas != flow.Lang) flow.Lang = langWas;
            }
            // 「플레이 기록 보내기」 줄(결정 126 ④ — 확인용 실행의 창구는 서버를 부르지 않는다): 끔 → 켬, 고른 칸 하나만 칠해지고 설정 값이 따라간다(실행하는 동안만 쥐는 값)
            if (Q("settings-row-upload") != null)
            {
                bool upWas = flow.SoundPrefs.PlayUpload;
                yield return Click(Px(Q("settings-opt-upload-off")));
                yield return Frames(2);
                bool off = !flow.SoundPrefs.PlayUpload && Q("settings-opt-upload-off").ClassListContains("sn-opt-picked") && Q("settings-row-upload").Query(className: "sn-opt-picked").ToList().Count == 1;
                yield return Click(Px(Q("settings-opt-upload-on")));
                yield return Frames(2);
                bool on = flow.SoundPrefs.PlayUpload && Q("settings-opt-upload-on").ClassListContains("sn-opt-picked");
                Step("settings-upload", "ok", off && on, "at", at, "off", off, "on", on);
                Check("설정 플레이 기록", off, "「끔」을 눌렀는데 플레이 기록 보내기가 꺼지지 않았다");
                Check("설정 플레이 기록", on, "「켬」을 눌렀는데 다시 켜지지 않았다");
                flow.SoundPrefs.PlayUpload = upWas;
            }
            int backAt = boot.Sound != null ? boot.Sound.Heard.Count : 0;
            yield return Click(Px(Q("settings-back")));
            // 즉시 소리: [뒤로] 단추는 무음(결정 70 ⑯)
            bool backSilent = true;
            if (boot.Sound != null) for (int i = backAt; i < boot.Sound.Heard.Count; i++) if (boot.Sound.Heard[i].From == "now") backSilent = false;
            Check("즉시 소리", backSilent, "[뒤로] 단추를 눌렀는데 소리가 났다(닫기·뒤로는 무음)");
            var title = stage.Current as TitleScreen;
            // 타이틀은 「설정」 줄을 고른 채 열린다. 다만 [뒤로]를 누른 자리가 타이틀 목록 위면(설정 판의 줄 수에 따라) 그 자리의 줄에 마우스가 올라가 그 줄이 골라진다 — 둘 다 맞다
            var pickedItem = title != null ? Q("title-item-" + title.Shown[title.Picked].Id) : null;
            bool under = pickedItem != null && pickedItem.worldBound.Contains(new Vector2(at.x / stage.Grade.PanelScale, (root.Size.y - at.y) / stage.Grade.PanelScale));
            bool back = title != null && (title.Shown[title.Picked].Id == TitleScreen.Settings || under);
            yield return Snap("17-title-after-settings");
            Step("settings-click-back", "ok", back && backSilent, "at", at, "screen", stage.Current != null ? stage.Current.Name : "", "backSilent", backSilent);
            Check("설정 [뒤로]", back, "[뒤로]를 눌렀는데 타이틀로 돌아오지 않았거나 고른 줄이 「설정」도 마우스 아래의 줄도 아니다");
        }

        /// <summary>
        /// ⑪-2 저장 화면(04 §9-12 — 저장 폴더를 받아 띄웠을 때만, <c>-sinnohSaves</c>): 방금 나온 판이 저장돼 있다. 타이틀 「이어하기」(켜져 있다) → 저장 목록(S04) →
        /// [삭제] → 묻기 → [취소] → [이어하기] → 나갈 때와 같은 상태의 보드(기록 판 「이어서 시작」) → [메뉴](저장 상태 줄) → 나가기 → 「이어하기」 → [삭제] → [삭제] → 빈 목록 → [뒤로] → 「이어하기」가 꺼진 타이틀.
        /// </summary>
        IEnumerator Saves(ulong leftHash, int leftCount)
        {
            var title = stage.Current as TitleScreen;
            var row = Q("title-item-" + TitleScreen.Continue);
            bool on = title != null && row != null && title.Ready(title.Shown[0]) && title.Shown[0].Id == TitleScreen.Continue && title.SaveLine != stage.T("screen.title.no_save");
            yield return Click(Px(row));
            yield return Frames(3);
            var list = stage.Current as SaveListScreen;
            bool listed = list != null && list.Entries.Count == 1 && GameFlow.CanResume(list.Entries[0]) && Q("saves-resume-0") != null
                && FaceBox.Shown(Q("saves-face-0")) == ProbeProtagonist && Q("saves-party-0").childCount >= 1;
            yield return Snap("15a-saves");
            Step("title-click-continue", "ok", on && listed, "at", at, "continueOn", on, "screen", stage.Current != null ? stage.Current.Name : "", "saves", list != null ? list.Entries.Count : -1, "line", title != null ? title.SaveLine ?? "" : "");
            if (!Check("이어하기", on && listed, "저장한 판이 있는데 타이틀 「이어하기」가 꺼져 있거나 눌러도 저장 목록에 그 판의 칸(얼굴 · 파티 · [이어하기])이 없다")) { Finish(1, head.ToString()); yield break; }

            // [삭제] → 한 번 더 묻는다 → [취소]: 지워지지 않는다
            yield return Click(Px(Q("saves-delete-0")));
            yield return Frames(3);
            bool asked = list.Asking == list.Entries[0].SaveId && Q("saves-delete-yes") != null && Q("saves-resume-0") == null;
            yield return Snap("15b-saves-ask");
            yield return Click(Px(Q("saves-delete-no")));
            yield return Frames(3);
            bool kept = list.Asking == null && list.Entries.Count == 1 && flow.Saves().Count == 1 && Q("saves-resume-0") != null;
            Step("saves-delete-cancel", "ok", asked && kept, "at", at, "asked", asked, "kept", kept);
            Check("저장 삭제 묻기", asked && kept, "[삭제]가 한 번 더 묻지 않았거나 [취소]인데 저장이 지워졌다");

            // [이어하기]: 나갈 때와 같은 상태의 보드. 게임 시작 연출을 다시 틀지 않고 바로 입력이 열린다
            yield return Click(Px(Q("saves-resume-0")));
            yield return Frames(4);
            yield return Wait(() => flow.InputOpen, 10f);
            yield return Frames(3);
            bool same = flow.Session != null && flow.Session.Hash == leftHash && flow.Session.CommandCount == leftCount;
            var resumedHud = flow.Hud;
            bool shown = flow.OnBoard && stage.Current == null && flow.InputOpen && resumedHud != null && resumedHud.RecentLog.Count == 1 && resumedHud.RecentLog[0] == stage.T("screen.log.line.resumed")
                && flow.Protagonist(flow.Client.Seat) == ProbeProtagonist && board.Pieces.Of(flow.Client.Seat) != null;
            yield return Snap("15c-resumed");
            Step("saves-click-resume", "ok", same && shown, "at", at, "same", same, "commands", flow.Session != null ? flow.Session.CommandCount : -1, "left", leftCount, "rewound", flow.Session != null ? flow.Session.Rewound : -1,
                "inputOpen", flow.InputOpen, "log", resumedHud != null ? string.Join(" | ", resumedHud.RecentLog) : "");
            if (!Check("이어 받기", same && shown, "저장 목록의 [이어하기]를 눌렀는데 나갈 때와 같은 상태의 보드(HUD · 입력 · 기록 판 「이어서 시작」)가 열리지 않았다")) { Finish(1, head.ToString()); yield break; }

            // [메뉴]: 저장 상태 줄 → 나가기 → 타이틀
            yield return Click(Px(Q("board-hud-menu")));
            yield return Frames(3);
            var menu = stage.Current as BoardMenuScreen;
            var saveLine = Q("board-menu-save") as Label;
            bool status = menu != null && saveLine != null && saveLine.text == menu.SaveText && flow.SaveStatus == SaveStatus.Saved
                && menu.SaveText == stage.T("screen.board.menu.save.saved").Replace("{time}", GameFlow.SaveTime(flow.SavedAtMs, date: false));
            yield return Snap("15d-menu-saved");
            if (menu != null) yield return Click(Px(Q("board-menu-quit")));
            yield return Frames(2);
            bool note = menu != null && menu.Asking && menu.QuitNoteKey == "screen.board.menu.quit.saved";
            if (Q("board-menu-quit-yes") != null) yield return Click(Px(Q("board-menu-quit-yes")));
            yield return Frames(4);
            Step("menu-save-status", "ok", status && note && stage.Current is TitleScreen, "at", at, "text", menu != null ? menu.SaveText ?? "" : "", "status", flow.SaveStatus.ToString(), "note", note);
            if (!Check("저장 상태", status && note && stage.Current is TitleScreen, "게임 메뉴의 저장 상태 줄이 「자동 저장됨 · 시각」이 아니거나 나가기 안내가 저장된 판의 것이 아니다")) { Finish(1, head.ToString()); yield break; }

            // 「이어하기」 → [삭제] → [삭제]: 빈 목록 → [뒤로] → 「이어하기」가 꺼진다
            yield return Click(Px(Q("title-item-" + TitleScreen.Continue)));
            yield return Frames(3);
            list = stage.Current as SaveListScreen;
            if (list != null && Q("saves-delete-0") != null) yield return Click(Px(Q("saves-delete-0")));
            yield return Frames(3);
            if (Q("saves-delete-yes") != null) yield return Click(Px(Q("saves-delete-yes")));
            yield return Frames(3);
            bool gone = list != null && list.Entries.Count == 0 && Q("saves-empty") != null && flow.Saves().Count == 0;
            yield return Snap("15e-saves-empty");
            if (Q("saves-back") != null) yield return Click(Px(Q("saves-back")));
            yield return Frames(4);
            title = stage.Current as TitleScreen;
            bool off = title != null && !title.Ready(title.Shown[0]) && title.SaveLine == stage.T("screen.title.no_save");
            Step("saves-click-delete", "ok", gone && off, "at", at, "empty", gone, "continueOff", off, "screen", stage.Current != null ? stage.Current.Name : "");
            if (!Check("저장 삭제", gone && off, "저장 목록의 [삭제] → [삭제]로 저장이 지워지지 않았거나 [뒤로] 뒤 타이틀의 「이어하기」가 꺼지지 않았다")) { Finish(1, head.ToString()); yield break; }
        }

        // 설정 판이 창 안에 있고, 값들이 판 안에 놓였는가(두 줄로 넘어가도 판을 벗어나지 않는다)
        IEnumerator SettingsLaid(string label, SettingsScreen set)
        {
            yield return Frames(2);
            var layout = stage.Root.layout;
            var panel = Q("settings-panel").worldBound;
            var backBox = Q("settings-back").worldBound;
            bool inside = panel.xMin >= 0 && panel.yMin >= 0 && panel.xMax <= layout.width + 0.5f && backBox.yMax <= layout.height + 0.5f;
            int opts = 0;
            bool optsInside = true;
            foreach (var opt in stage.Root.Query(className: "sn-set-opt").ToList())
            {
                opts++;
                var box = opt.worldBound;
                if (box.xMin < panel.xMin - 0.5f || box.xMax > panel.xMax + 0.5f || box.yMin < panel.yMin - 0.5f || box.yMax > panel.yMax + 0.5f) optsInside = false;
            }
            Check(label, inside, "설정 판·[뒤로]가 창 밖으로 나갔다(판 " + panel + " · 뿌리 " + layout.width + "x" + layout.height + ")");
            Check(label, opts == set.Sizes.Count + 1 && optsInside, "화면 크기 값이 판 안에 다 놓이지 않았다(값 " + opts + " · 목록 " + set.Sizes.Count + " + 전체 화면)");
        }

        // 숨겨진 장소에 멈췄으면 [지나가기]를 마우스로 누른다
        /// <summary>
        /// ⑫ 배틀(04 §4-7): 새 판을 보드까지 보낸 뒤(셋업은 앞에서 마우스로 봤다 — 여기서는 화면 함수로) 마우스로 [도전] → 기술 줄에 올리기(툴팁) → 누르기(답 하나) →
        /// 배틀 끝까지 기술 줄 누르기 → 결과 창 [확인] → 보드의 다음 턴. 상대(AI)의 답은 방장 멈춤 뒤에 오므로 세션 시각을 더 흘린다.
        /// </summary>
        IEnumerator Battle()
        {
            flow.StartSolo(new Sinnoh.Rules.Session.SoloSettings(), Seed, AiSeed);
            yield return Frames(4);
            if (stage.Current is SetupVillageScreen village)
            {
                village.Pick(0);
                village.Confirm();
                flow.Skip(flow.QueueLeftMs);   // 고향 마을 연출 → 카드가 들어옴
            }
            yield return Frames(4);
            if (stage.Current is SetupPickScreen pick)
            {
                pick.PickStarter(0);
                pick.PickCard(0);
                pick.Confirm();
                flow.Skip(flow.OpeningMs);   // 고른 뒤·공개 → 숨겨진 장소 배치 → 막 카드 · 조무래기 놓기
            }
            yield return Frames(4);
            yield return Wait(() => flow.InputOpen, 15f);
            yield return Frames(3);   // 입력이 열리며 다시 그린 판이 자리를 잡은 뒤
            var button = Q("board-hud-act-" + BoardHud.Challenge);
            if (!Check("배틀", flow.InputOpen && button != null, "보드에 [도전] 단추가 없다")) { Finish(1, head.ToString()); yield break; }
            int commands = flow.Session.CommandCount;
            yield return Click(Px(button));
            bool challenged = flow.Session.CommandCount == commands + 1;
            // 조우 전환(04 §8-8 22행): 가운데 필드의 줄무늬가 닫히고 그 가운데에서 배틀 화면이 열린다 — 실시각으로 기다리며 줄무늬가 덮는 모습을 한 장 찍는다
            float enterT = Time.realtimeSinceStartup;
            bool wiped = false;
            while (!(stage.Current is BattleScreen) && Time.realtimeSinceStartup - enterT < 10f)
            {
                if (!wiped && flow.WipeCover > 0.3f)
                {
                    wiped = true;
                    yield return Snap("16-battle-enter-wipe");
                }
                yield return null;
            }
            var battle = stage.Current as BattleScreen;
            bool opened = battle != null && challenged && wiped;
            Step("battle-click-challenge", "ok", opened, "at", at, "commands", flow.Session.CommandCount, "wipe", wiped);
            if (!Check("배틀 [도전]", opened, "[도전]을 눌렀는데 배틀 화면이 열리지 않았다")) { Finish(1, head.ToString()); yield break; }

            // 내 차례가 올 때까지(우선권 토큰을 물으면 [사용]을 누른다)
            float bt = Time.realtimeSinceStartup;
            while (!(battle.Open && battle.Moves.Count > 0) && !battle.ShowsResult && Time.realtimeSinceStartup - bt < 60f)
            {
                if (battle.Open && battle.AskingToken && Laid(Q("battle-token-use"))) yield return Click(Px(Q("battle-token-use")));
                else if (battle.Open && battle.AskingOrder && Laid(Q("battle-order-1"))) yield return Click(Px(Q("battle-order-1")));   // 결정 72 효과 순서 줄: 첫 줄(기본 순서)
                else if (battle.Open && battle.AskingFate && Laid(Q("battle-fate-pass"))) yield return Click(Px(Q("battle-fate-pass")));   // 운명 창: [넘기기](안 씀)
                else if (battle.Open && (battle.AskingAsk || battle.AskingPick) && Laid(Q("battle-ask-no"))) yield return Click(Px(Q("battle-ask-no")));   // 「쓸까」·고르기: [넘기기]
                flow.Skip(1000);
                yield return null;
            }
            yield return Frames(3);
            var row = Q("battle-move-1");
            if (!Check("배틀 기술 줄", Laid(row) && battle.Open, "내 차례의 기술 줄이 없다")) { Finish(1, head.ToString()); yield break; }

            // 기둥의 [운명의 변화 n/5](결정 80 N3 나): 누르면 배틀 화면 위에 보기만 하는 손패 창 → Esc로 닫힌다 — 답은 나가지 않는다
            {
                int before = flow.Session.CommandCount;
                var fateButton = Q(BattleScreen.FateButton);
                bool fateOn = Laid(fateButton) && battle.FateOn;
                if (fateOn) yield return Click(Px(fateButton));
                yield return Frames(3);
                bool viewing = flow.Top is FateScreen && stage.Current == battle && Q("board-fate-use-1") == null;
                if (viewing) yield return Snap("16-battle-fate-view");
                yield return Tap(Key.Escape);
                yield return Frames(3);
                bool escaped = battle.Over == null && stage.Current == battle && flow.Session.CommandCount == before;
                Step("battle-fate-view", "ok", fateOn && viewing && escaped, "at", at, "button", fateOn, "viewing", viewing, "esc", escaped);
                Check("배틀 중 손패 보기", fateOn && viewing && escaped, "기둥의 [운명의 변화]로 보기만 하는 손패 창이 열리고 Esc로 닫혀야 한다(단추 " + fateOn + " · 창 " + viewing + " · Esc " + escaped + ")");
            }

            // 독의 [정비](04 §3-5): 누르면 배틀 화면 위에 정비 판(독을 덮지 않는다) → 어둠에 덮인 독의 기술 줄 자리를 눌러도 답이 나가지 않고 판만 닫힌다 → 다시 열어 [← 배틀로]
            var maint = Q("battle-maint");
            bool maintOn = battle.MaintOn && Laid(maint);
            commands = flow.Session.CommandCount;
            bool maintOpen = false, clear = false, blocked = false, maintBack = false;
            if (maintOn)
            {
                yield return Click(Px(maint));
                yield return Frames(3);
                var panel = Q("board-party-panel");
                maintOpen = battle.Over is PartyScreen && stage.Current == battle && Laid(panel);
                clear = maintOpen && panel.worldBound.yMax <= Q("battle-dock").worldBound.yMin + 0.5f;
                yield return Snap("16-battle-maint-party");
                if (maintOpen)
                {
                    yield return Click(Px(row));
                    yield return Frames(3);
                    blocked = battle.Over == null && flow.Session.CommandCount == commands;
                    yield return Wait(() => battle.MaintOn && Laid(Q("battle-maint")), 5f);
                    yield return Frames(3);
                    if (Laid(Q("battle-maint"))) yield return Click(Px(Q("battle-maint")));
                    yield return Frames(3);
                    if (battle.Over is PartyScreen && Laid(Q("board-party-close"))) yield return Click(Px(Q("board-party-close")));
                    yield return Frames(3);
                    yield return Wait(() => battle.Open && Laid(Q("battle-move-1")), 5f);
                    yield return Frames(3);
                    maintBack = battle.Over == null && stage.Current == battle && battle.Open && flow.Session.CommandCount == commands;
                }
                if (battle.Over != null) flow.CloseParty();
            }
            Step("battle-maint", "ok", maintOn && maintOpen && clear && blocked && maintBack, "at", at, "maintOn", maintOn, "party", maintOpen, "panelAboveDock", clear, "dockBlocked", blocked, "back", maintBack, "commands", flow.Session.CommandCount);
            Check("배틀 [정비]", maintOn && maintOpen, "독의 [정비]를 눌렀는데 배틀 화면 위에 정비 판이 열리지 않았다(단추 켜짐 " + maintOn + ")");
            Check("배틀 [정비]", !maintOpen || clear, "정비 판이 독을 덮는다");
            Check("배틀 [정비]", !maintOpen || blocked, "정비 판이 열린 동안 독의 기술 줄 자리를 눌렀는데 답이 나갔거나 판이 닫히지 않았다");
            Check("배틀 [정비]", !maintOpen || maintBack, "[← 배틀로]로 배틀 화면에 돌아오지 못했다");
            row = Q("battle-move-1");
            if (!Check("배틀 기술 줄", Laid(row) && battle.Open, "정비 판을 닫은 뒤 기술 줄이 없다")) { Finish(1, head.ToString()); yield break; }
            yield return Move(Px(row));
            yield return Frames(2);
            bool tip = stage.Tips.Target == row && stage.Tips.Element.worldBound.yMax <= Q("battle-dock").worldBound.yMin + 0.5f;
            yield return Snap("16-battle-move-tooltip");
            commands = flow.Session.CommandCount;
            int lines = flow.Battle.Lines.Count;
            var moveAt = Px(row);
            yield return Click(moveAt);
            bool answered = flow.Session.CommandCount > commands && flow.Battle != null && flow.Battle.Lines.Count > lines;
            Step("battle-click-move", "ok", tip && answered, "at", at, "tipAboveDock", tip, "commandsBefore", commands, "commandsAfter", flow.Session.CommandCount, "lines", flow.Battle != null ? flow.Battle.Lines.Count : -1);
            Check("배틀 기술 줄", tip, "기술 줄에 마우스를 올렸는데 툴팁이 줄 위(독 밖)에 뜨지 않았다");
            Check("배틀 기술 줄", answered, "기술 줄을 눌렀는데 답이 나가지 않았다");

            // 연출 중(04 §8-9 「배틀 연출」): 기술 줄 자리가 굴림판으로 바뀌고, 그 자리를 다시 눌러도 답이 나가지 않는다(연출이 끝날 때까지 입력 닫힘)
            float rollT = Time.realtimeSinceStartup;
            while (!battle.Rolling && !battle.ShowsResult && Time.realtimeSinceStartup - rollT < 5f) yield return null;
            // 굴림판이 다 들어오고(150~350) 주사위가 던져진 뒤(400~)의 모습을 찍는다 — 실시각으로 기다린다
            while (battle.Rolling && flow.Fx.Sequencer.LocalMs < 900 && Time.realtimeSinceStartup - rollT < 8f) yield return null;
            yield return Frames(3);
            commands = flow.Session.CommandCount;
            yield return Click(moveAt);
            bool held = battle.Rolling && !battle.Open && flow.Session.CommandCount == commands && Laid(Q("battle-roll-board")) && Q("battle-move-1") == null;
            yield return Snap("16-battle-roll");
            Step("battle-click-during-roll", "ok", held, "at", at, "rolling", battle.Rolling, "board", battle.RollBoardOn, "commands", flow.Session.CommandCount);
            Check("배틀 굴림판", held, "연출 중에 기술 줄 자리를 눌렀는데 답이 나갔거나 굴림판이 뜨지 않았다");

            // 끝까지: 내 차례마다 첫 기술 줄을 누른다 → 결과 창 [확인]
            int clicks = 1;
            bt = Time.realtimeSinceStartup;
            while (!battle.ShowsResult && Time.realtimeSinceStartup - bt < 120f && !TimedOut)
            {
                if (battle.Open && battle.AskingToken && Laid(Q("battle-token-use"))) yield return Click(Px(Q("battle-token-use")));
                else if (battle.Open && battle.AskingOrder && Laid(Q("battle-order-1"))) yield return Click(Px(Q("battle-order-1")));   // 결정 72 효과 순서 줄: 첫 줄(기본 순서)
                else if (battle.Open && battle.AskingFate && Laid(Q("battle-fate-pass"))) yield return Click(Px(Q("battle-fate-pass")));   // 운명 창: [넘기기](안 씀)
                else if (battle.Open && (battle.AskingAsk || battle.AskingPick) && Laid(Q("battle-ask-no"))) yield return Click(Px(Q("battle-ask-no")));   // 「쓸까」·고르기: [넘기기]
                else if (battle.Open && battle.Moves.Count > 0 && Laid(Q("battle-move-1")))
                {
                    yield return Click(Px(Q("battle-move-1")));
                    clicks++;
                }
                flow.Skip(1000);
                yield return null;
            }
            yield return Frames(3);
            yield return Snap("17-battle-result");
            var ok = Q("battle-result-ok");
            if (!Check("배틀 결과", battle.ShowsResult && Laid(ok), "배틀이 끝나지 않았거나 결과 창에 [확인]이 없다")) { Finish(1, head.ToString()); yield break; }
            string outcome = flow.Battle.Drawn ? "draw" : flow.Battle.Won ? "win" : "lose";
            commands = flow.Session.CommandCount;
            yield return Click(Px(ok));
            bool closed = stage.Current == null && flow.Battle == null && flow.Session.CommandCount == commands;
            yield return Wait(() => flow.InputOpen, 15f);
            bool next = flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null;
            yield return Frames(3);
            yield return Snap("18-board-after-battle");
            Step("battle-click-result-ok", "ok", closed && next, "at", at, "result", outcome, "moveClicks", clicks, "round", flow.Client.View.Round, "inputOpen", flow.InputOpen);
            Check("배틀 결과", closed, "[확인]을 눌렀는데 배틀 화면이 닫히지 않았다");
            Check("배틀 결과", next, "결과를 확인한 뒤 다음 턴의 입력이 열리지 않았다");
        }

        /// <summary>
        /// ⑬ 조우·포획(04 §4-1·§4-8): 볼을 쥐여 준 판(<see cref="Sinnoh.Ai.SoloTrail.Catch"/> — 볼을 얻는 길이 아직 없다)을 나무 칸까지 보낸 뒤(길잡이가 화면 함수로) 마우스로
        /// [찾아보기] → 조우 창의 기술 줄에 올리기(툴팁은 줄 아래) → [배틀!] → [가방] → 볼 줄 → (길잡이가 끝까지) → 결과 창 [확인].
        /// </summary>
        IEnumerator Encounter()
        {
            var pilot = FlowPilot.Start(flow, Sinnoh.Ai.SoloTrail.Catch, ProbeProtagonist);   // 고른 주인공 = 볼 던지는 뒷모습(결정 60)
            int attempts = 0;
            void Count(IReadOnlyList<GameEvent> events)
            {
                foreach (var e in events) if (e is CaptureAttempt) attempts++;
            }
            flow.EventsSeen += Count;
            yield return Frames(4);
            // 나무 칸까지: [찾아보기]가 판에 뜰 때까지 길잡이가 걷는다
            float bt = Time.realtimeSinceStartup;
            while (!(flow.InputOpen && flow.Hud != null && flow.Hud.Shows(BoardHud.Search)) && Time.realtimeSinceStartup - bt < 60f && !TimedOut)
            {
                if (!FlowPilot.Step(flow, pilot)) flow.Skip(1000);
                yield return null;
            }
            yield return Frames(3);
            var search = Q("board-hud-act-" + BoardHud.Search);
            if (!Check("찾아보기", flow.InputOpen && Laid(search) && !search.ClassListContains("sn-hud-off"), "나무 칸에서 [찾아보기]가 켜지지 않았다")) { flow.EventsSeen -= Count; Finish(1, head.ToString()); yield break; }
            int commands = flow.Session.CommandCount;
            yield return Click(Px(search));
            yield return Frames(3);
            // 찾아보기(20행 1750): 말 머리 위 「!」가 뜨고, 그 뒤 카드 공개(21행)가 시작할 때 조우 창이 열린다(카드는 뒷면에서 왼쪽부터 뒤집힌다)
            bool searched = flow.Session.CommandCount == commands + 1;
            bool bang = stage.Current == null && flow.Hud != null && flow.Hud.BangShown && Laid(Q("board-hud-bang"));
            yield return Snap("19-board-search-bang");
            yield return Wait(() => stage.Current is EncounterScreen, 10f);
            var enc = stage.Current as EncounterScreen;
            bool backs = enc != null && enc.FaceUp < enc.Cards.Count && !enc.Open;
            yield return Snap("19-encounter-backs");
            bool opened = enc != null && searched && enc.Cards.Count > 0 && bang && backs;
            Step("board-click-search", "ok", opened, "at", at, "commands", flow.Session.CommandCount, "cards", enc != null ? enc.Cards.Count : -1, "bang", bang, "backs", backs);
            if (!Check("찾아보기", opened, "[찾아보기]를 눌렀는데 조우 창이 열리지 않았다")) { flow.EventsSeen -= Count; Finish(1, head.ToString()); yield break; }

            // 조우 창: 기술 줄 툴팁(줄 아래) → [배틀!]
            yield return Wait(() => enc.Open, 10f);
            yield return Frames(3);
            var moveRow = Q("encounter-card-1-move");
            yield return Move(Px(moveRow));
            yield return Frames(2);
            bool tipBelow = stage.Tips.Target == moveRow && stage.Tips.Element.worldBound.yMin >= moveRow.worldBound.yMax - 0.5f;
            yield return Snap("19-encounter-move-tooltip");
            var pickButton = Q("encounter-pick-1");
            commands = flow.Session.CommandCount;
            yield return Click(Px(pickButton));
            yield return Frames(3);
            // 고른 카드가 커진 채 조우 창이 남고(21행 「고른 뒤」), 조우 전환(22행)의 가운데에서 배틀 화면으로 바뀐다
            bool chosen = enc.Picked == 0 && stage.Current == enc;
            yield return Wait(() => stage.Current is BattleScreen, 10f);
            var battle = stage.Current as BattleScreen;
            bool picked = battle != null && chosen && flow.Session.CommandCount == commands + 1 && flow.Battle != null && flow.Battle.Kind == BattleKind.Wild;
            Step("encounter-click-battle", "ok", tipBelow && picked, "at", at, "tipBelowRow", tipBelow, "commands", flow.Session.CommandCount);
            Check("조우 창", tipBelow, "기술 줄에 마우스를 올렸는데 툴팁이 줄 아래에 뜨지 않았다");
            if (!Check("조우 창", picked, "[배틀!]을 눌렀는데 야생 배틀 화면이 열리지 않았다")) { flow.EventsSeen -= Count; Finish(1, head.ToString()); yield break; }

            // 야생 배틀: [가방] → 볼 줄
            bt = Time.realtimeSinceStartup;
            while (!battle.BagOn && !battle.ShowsResult && Time.realtimeSinceStartup - bt < 60f && !TimedOut)
            {
                if (battle.Open && battle.AskingToken && Laid(Q("battle-token-use"))) yield return Click(Px(Q("battle-token-use")));
                else if (battle.Open && battle.AskingOrder && Laid(Q("battle-order-1"))) yield return Click(Px(Q("battle-order-1")));   // 결정 72 효과 순서 줄: 첫 줄(기본 순서)
                else if (battle.Open && battle.AskingFate && Laid(Q("battle-fate-pass"))) yield return Click(Px(Q("battle-fate-pass")));   // 운명 창: [넘기기](안 씀)
                else if (battle.Open && (battle.AskingAsk || battle.AskingPick) && Laid(Q("battle-ask-no"))) yield return Click(Px(Q("battle-ask-no")));   // 「쓸까」·고르기: [넘기기]
                flow.Skip(1000);
                yield return null;
            }
            yield return Frames(3);
            var bag = Q("battle-bag");
            if (!Check("가방", battle.BagOn && Laid(bag), "야생 배틀의 내 차례에 [가방]이 켜지지 않았다")) { flow.EventsSeen -= Count; Finish(1, head.ToString()); yield break; }
            // 단축키 Q = [가방](결정 164 — 단추의 글자): 볼 줄 → Q 다시 = 기술 줄. 배틀 화면 위에서 Q는 보드를 돌리지 않고 돌리기 단추도 숨는다
            {
                int qkYaw = rig.Yaw;
                commands = flow.Session.CommandCount;
                bool qkHidden = flow.Hud == null || !flow.Hud.RotateShown;
                yield return Tap(Key.Q);
                yield return Frames(2);
                bool qkOpen = battle.BagOpen && Laid(Q("battle-ball-1"));
                yield return Tap(Key.Q);
                yield return Frames(2);
                bool qkBack = !battle.BagOpen && Laid(Q("battle-move-1"));
                bool qkOk = qkOpen && qkBack && rig.Yaw == qkYaw && qkHidden && flow.Session.CommandCount == commands;
                Step("battle-key-bag", "ok", qkOk, "open", qkOpen, "back", qkBack, "yaw", rig.Yaw, "yawBefore", qkYaw, "qkHidden", qkHidden, "commands", flow.Session.CommandCount - commands);
                Check("가방 단축키", qkOk, "배틀 화면에서 Q가 [가방]을 열고 닫지 않았다(열림 " + qkOpen + " · 닫힘 " + qkBack + " · 방향 " + qkYaw + " → " + rig.Yaw + " · 돌리기 단추 숨김 " + qkHidden + ")");
                bag = Q("battle-bag");
            }
            commands = flow.Session.CommandCount;
            yield return Click(Px(bag));
            yield return Frames(3);
            var ball = Q("battle-ball-1");
            bool bagOpen = battle.BagOpen && Laid(ball) && flow.Session.CommandCount == commands && Q("battle-move-1") == null;
            yield return Snap("20-battle-bag");
            Step("battle-click-bag", "ok", bagOpen, "at", at, "balls", battle.Balls.Count, "commands", flow.Session.CommandCount);
            if (!Check("가방", bagOpen, "[가방]을 눌렀는데 볼 줄이 뜨지 않았다(또는 답이 나갔다)")) { flow.EventsSeen -= Count; Finish(1, head.ToString()); yield break; }
            int before = attempts;
            yield return Click(Px(ball));
            yield return Frames(3);
            // 포획 연출(68행): 내 주인공 뒷모습이 들어와 던지고 독에서 포획 주사위가 구른다 — 볼이 손을 떠난 뒤의 모습을 찍는다(실시각으로 기다린다)
            float throwT = Time.realtimeSinceStartup;
            while (!battle.Capture.Ball && !battle.ShowsResult && Time.realtimeSinceStartup - throwT < 5f) yield return null;
            yield return Frames(2);   // 볼이 손을 떠나는 순간 볼 재고 칩이 바뀌어 독을 다시 짓는다 — 자리가 잡힌 뒤에 본다
            string backDrawn = battle.Ds != null ? battle.Ds.BackDrawn ?? "" : "";
            bool backMine = flow.Protagonist(flow.Client.Seat) == ProbeProtagonist && backDrawn == "Trainers/Back/official/" + ProbeProtagonist + ".png";
            bool staged = battle.Capture.Trainer && battle.Capture.Ball && battle.RollIsCapture && Laid(Q("battle-roll-board"));
            yield return Snap("21-battle-capture");
            bool thrown = flow.Session.CommandCount > commands && attempts == before + 1 && flow.Battle != null && flow.Battle.LastCapture != null && staged;
            Step("battle-click-ball", "ok", thrown, "at", at, "commandsBefore", commands, "commandsAfter", flow.Session.CommandCount, "attempts", attempts,
                "success", flow.Battle != null && flow.Battle.LastCapture != null && flow.Battle.LastCapture.Success, "staged", staged,
                "trainer", battle.Capture.Trainer, "ball", battle.Capture.Ball, "captureRoll", battle.RollIsCapture, "board", Laid(Q("battle-roll-board")), "backDrawn", backDrawn, "backMine", backMine);
            Check("볼 줄", thrown, "볼 줄을 눌렀는데 포획 시도가 일어나지 않았다");
            Check("볼 줄", backMine, "DS에 그려진 뒷모습이 고른 주인공(" + ProbeProtagonist + ")이 아니다(그려진 것 「" + backDrawn + "」)");

            // 끝까지(길잡이가 화면 함수로) → 결과 창 [확인]을 마우스로
            bt = Time.realtimeSinceStartup;
            while (!(battle.ShowsResult && !battle.AskingReward) && Time.realtimeSinceStartup - bt < 120f && !TimedOut)
            {
                if (!FlowPilot.Step(flow, pilot, false)) flow.Skip(1000);
                yield return null;
            }
            flow.EventsSeen -= Count;
            yield return Frames(3);
            yield return Snap("22-battle-captured");
            var ok = Q("battle-result-ok");
            if (!Check("포획 결과", battle.ShowsResult && Laid(ok), "야생 배틀이 끝나지 않았거나 결과 창에 [확인]이 없다")) { Finish(1, head.ToString()); yield break; }
            string outcome = flow.Battle.Result.ToString().ToLowerInvariant();
            yield return Click(Px(ok));
            // 보드로 돌아와(역전환) 턴 결과 요약이 뜬다 — 잡은 턴이라 요약에 잡은 포켓몬 · 여행 점수 · 규토리가 적힌다(101행)
            yield return Wait(() => flow.Hud != null && flow.Hud.Band.ActiveRow == Sinnoh.Fx.FxRows.TurnSummary && flow.WipeCover <= 0f, 5f);
            bool summed = stage.Current == null && flow.Hud != null && flow.Hud.Band.ActiveRow == Sinnoh.Fx.FxRows.TurnSummary && Q("turn-band-summary").Query<Label>().ToList().Count >= 3;
            yield return Snap("23-turn-summary-caught");
            yield return Wait(() => flow.InputOpen, 15f);
            bool next = stage.Current == null && flow.InputOpen && flow.NotReadyShown == 0 && summed;
            Step("encounter-click-result-ok", "ok", next, "at", at, "result", outcome, "attempts", attempts, "notReady", flow.NotReadyShown, "party", PokemonRules.PartyCount(flow.Client.View.Seats[flow.Client.Seat]), "summary", summed);
            Check("포획 결과", next, "결과를 확인한 뒤 다음 턴의 입력이 열리지 않았다(받을 화면 없는 선택 " + flow.NotReadyShown + "번)");
        }

        /// <summary>
        /// 정비의 끌어 놓기(결정 70 ⑪ #42): 기술 카드·지닌물건을 쥐여 준 판(<see cref="Sinnoh.Ai.SoloTrail.Kit"/> — 얻는 길은 M5)에서 [정비]를 열고, 마우스로
        /// ① 기술 카드를 눌러 끌다(그 카드 그림이 따라온다 — 결정 73 ④) 빛나지 않는 곳에서 뗀다 → 제자리(명령 없음) ② 다시 끌어 빛나는 기술 칸에서 뗀다 → 가르치기 명령 하나
        /// ③ 문턱 안에서 눌렀다 떼면 끌기가 아니라 누르기(집기)다 — 두 번 누르기는 그대로.
        /// </summary>
        IEnumerator MaintDrag()
        {
            FlowPilot.Start(flow, Sinnoh.Ai.SoloTrail.Kit, ProbeProtagonist);
            yield return Frames(4);
            flow.Skip(flow.OpeningMs);
            yield return Wait(() => flow.CanOpenParty && Laid(Q("board-hud-maint")), 15f);
            var maint = Q("board-hud-maint");
            if (Laid(maint) && maint.enabledSelf)
            {
                yield return Click(Px(maint));
                yield return Frames(3);
            }
            var party = stage.Current as PartyScreen;
            bool open = party != null && party.Mine && Laid(Q("board-party-pool-1")) && Laid(Q("board-party-slot-0-1")) && Laid(Q("board-party-title"));
            if (!Check("정비 끌어 놓기", open, "쥐여 준 판에서 정비 판(기술 카드 풀)이 열리지 않았다")) { Step("party-drag", "ok", false, "at", at, "open", open); yield break; }
            var client = flow.Client;
            int starter = client.View.Seats[client.Seat].Party[0], commands = flow.Session.CommandCount;
            var ghost = Q("board-party-ghost");

            // ① 끌다 빛나지 않는 곳(판의 제목 줄)에서 뗀다 → 제자리
            yield return Drag(Px(Q("board-party-pool-1")), Px(Q("board-party-title")), 6);
            yield return Frames(2);
            bool dragging = party.Dragging && party.Held == PartyScreen.Hold.Move && ghost != null && ghost.resolvedStyle.display == DisplayStyle.Flex && Laid(ghost.Q(className: "sn-enc-move"));   // 결정 73 ④: 기술 카드 그림이 따라온다
            bool lit = Q("board-party-slot-0-1") != null && Q("board-party-slot-0-1").ClassListContains("sn-party-ok");
            yield return Snap("24-party-drag");
            Queue(at, false);
            yield return Frames(4);
            bool back = !party.Dragging && party.Held == PartyScreen.Hold.None && flow.Session.CommandCount == commands && client.View.Pokemon[starter].Moves[0] < 0
                && ghost != null && ghost.resolvedStyle.display == DisplayStyle.None;

            // ② 다시 끌어 빛나는 기술 칸에서 뗀다 → 가르치기
            yield return Drag(Px(Q("board-party-pool-1")), Px(Q("board-party-slot-0-1")), 6);
            yield return Frames(2);
            bool over = party.Dragging;
            yield return Snap("25-party-drag-over");
            Queue(at, false);
            yield return Frames(5);
            bool taught = !party.Dragging && party.Held == PartyScreen.Hold.None && flow.Session.CommandCount == commands + 1 && client.View.Pokemon[starter].Moves[0] >= 0;
            yield return Snap("26-party-dropped");

            // ③ 문턱 안에서 눌렀다 떼면 누르기(집기) — 두 번 누르기는 그대로다
            yield return Click(Px(Q("board-party-pool-1")));
            yield return Frames(3);
            bool picked = !party.Dragging && party.Held == PartyScreen.Hold.Move;
            yield return Click(Px(Q("board-party-pool-1")));
            yield return Frames(3);
            bool dropped = party.Held == PartyScreen.Hold.None;

            if (Laid(Q("board-party-close"))) yield return Click(Px(Q("board-party-close")));
            yield return Frames(3);
            bool closed = stage.Current == null;
            bool ok = dragging && lit && back && over && taught && picked && dropped && closed;
            Step("party-drag", "ok", ok, "at", at, "dragging", dragging, "lit", lit, "back", back, "over", over, "taught", taught, "clickPicks", picked, "clickDrops", dropped, "closed", closed, "commands", flow.Session.CommandCount - commands);
            Check("정비 끌어 놓기", ok, "끌어 놓기가 기대와 다르다(끄는 중 " + dragging + " · 빛남 " + lit + " · 제자리 " + back + " · 칸 위 " + over + " · 가르침 " + taught + " · 누르기 " + picked + "/" + dropped + " · 닫힘 " + closed + ")");
        }

        /// <summary>
        /// 제작·은행·진화(M5 — S26·S24·S27): 손본 판(<see cref="Sinnoh.Ai.SoloTrail.Work"/> — 규토리 일곱 색 셋씩 · 여행 점수 · 이브이)의 첫 턴에 마우스로
        /// [제작] → 몬스터볼 [제작](36행이 창에서 돈다) → [닫기] · [마을] → 은행상회 [거래](18행) → [닫기] · [진화] → 스타팅 포켓몬 [진화](39행 DS 연출) → 옛 기술이면 [가르치지 않기] → [닫기].
        /// </summary>
        IEnumerator Work()
        {
            FlowPilot.Start(flow, Sinnoh.Ai.SoloTrail.Work, ProbeProtagonist);
            yield return Frames(4);
            flow.Skip(flow.OpeningMs);
            yield return Wait(() => flow.InputOpen && Laid(Q("board-hud-act-" + BoardHud.Craft)), 15f);
            var client = flow.Client;
            int seat = client.Seat, commands = flow.Session.CommandCount;
            var st = client.View.Seats[seat];
            int balls = st.Balls[(int)Sinnoh.Rules.Ball.Regular];

            // ① 제작 창: [제작] → 몬스터볼 [제작] → 창이 그대로 · 볼 +1 → [닫기]
            var craftButton = Q("board-hud-act-" + BoardHud.Craft);
            if (Laid(craftButton)) yield return Click(Px(craftButton));
            yield return Frames(3);
            var craft = stage.Current as CraftScreen;
            bool craftOpen = craft != null && Laid(Q("board-craft-make-poke_ball"));
            yield return Snap("27-craft");
            bool made = false, craftClosed = false;
            if (craftOpen)
            {
                // 키보드 제출(UiStage.NoSubmit — M6 「화면 교차 검토 반영」 확인 A): [제작]에 포커스를 두고 Space · Enter → 단추가 눌리지 않는다.
                // 패널 맨 위(무대 뿌리보다 먼저)에서 제출 이벤트를 세어 키가 실제로 제출을 냈는지도 적는다.
                var make = Q("board-craft-make-poke_ball");
                int submits = 0;
                EventCallback<NavigationSubmitEvent> seen = e => submits++;
                var top = stage.Root.panel != null ? stage.Root.panel.visualTree : null;
                if (top != null) top.RegisterCallback(seen, TrickleDown.TrickleDown);
                make.Focus();
                yield return Frames(2);
                yield return Tap(Key.Space);
                yield return Tap(Key.Enter);
                yield return Frames(3);
                if (top != null) top.UnregisterCallback(seen, TrickleDown.TrickleDown);
                bool pressed = flow.Session.CommandCount != commands || flow.Client.View.Seats[seat].Balls[(int)Sinnoh.Rules.Ball.Regular] != balls;
                bool stayed = stage.Current == craft;
                make.Blur();
                Step("key-no-submit", "ok", !pressed && stayed, "submits", submits, "pressed", pressed, "stayed", stayed, "commands", flow.Session.CommandCount - commands);
                Check("키보드 제출 막기", !pressed && stayed, "Space · Enter가 포커스된 [제작]을 눌렀다(제출 이벤트 " + submits + ")");
                yield return Click(Px(Q("board-craft-make-poke_ball")));
                yield return Frames(3);
                bool drawn = craft.Playing >= 0 || flow.Fx == null || !flow.Fx.Sequencer.IsEmpty;
                yield return Wait(() => flow.BoardOpen, 8f);
                made = drawn && stage.Current == craft && flow.Client.View.Seats[seat].Balls[(int)Sinnoh.Rules.Ball.Regular] == balls + 1 && flow.Session.CommandCount == commands + 1;
                yield return Snap("28-craft-made");
                yield return Click(Px(Q("board-craft-close")));
                yield return Frames(3);
                craftClosed = stage.Current == null;
            }
            Step("work-craft", "ok", craftOpen && made && craftClosed, "at", at, "open", craftOpen, "made", made, "closed", craftClosed);
            Check("제작 창", craftOpen && made && craftClosed, "제작 창이 열리지 않았거나 [제작]이 볼을 만들지 않았다(열림 " + craftOpen + " · 만듦 " + made + " · 닫힘 " + craftClosed + ")");

            // ② 은행상회: [마을] → 첫 줄 [거래] → 카드가 섞이고 「이번 턴에 이미 사용했습니다.」 → [닫기]
            yield return Wait(() => flow.InputOpen, 5f);
            commands = flow.Session.CommandCount;
            var villageButton = Q("board-hud-act-" + BoardHud.Village);
            if (Laid(villageButton)) yield return Click(Px(villageButton));
            yield return Frames(3);
            var village = stage.Current as VillageScreen;
            var trade = Q("board-village-bank-trade-0") ?? Q("board-village-bank-trade-1");
            bool bankOpen = village != null && village.Bank != null && Laid(trade) && trade.enabledSelf;
            bool traded = false, bankClosed = false;
            if (bankOpen)
            {
                yield return Click(Px(trade));
                yield return Frames(3);
                yield return Wait(() => flow.BoardOpen, 8f);
                traded = flow.Session.CommandCount == commands + 1 && village.Bank.Why == "screen.village.used";
                yield return Snap("29-bank-traded");
                yield return Click(Px(Q("board-village-close")));
                yield return Frames(3);
                bankClosed = stage.Current == null;
            }
            Step("work-bank", "ok", bankOpen && traded && bankClosed, "at", at, "open", bankOpen, "traded", traded, "closed", bankClosed);
            Check("은행상회", bankOpen && traded && bankClosed, "은행상회 칸이 없거나 [거래]가 나가지 않았다(열림 " + bankOpen + " · 거래 " + traded + " · 닫힘 " + bankClosed + ")");

            // ③ 진화 창: [진화] → 스타팅 포켓몬(파티 둘째 — 첫 칸은 이브이) [진화] → DS 연출 → 옛 기술이면 [가르치지 않기] → [닫기]
            yield return Wait(() => flow.InputOpen, 5f);
            commands = flow.Session.CommandCount;
            var evolveButton = Q("board-hud-act-" + BoardHud.Evolve);
            if (Laid(evolveButton)) yield return Click(Px(evolveButton));
            yield return Frames(3);
            var evolve = stage.Current as EvolveScreen;
            int starter = client.View.Seats[seat].Party[1];
            int before = client.View.Pokemon[starter].Def;
            bool evolveOpen = evolve != null && Laid(Q("board-evolve-go-1"));
            bool shown = false, evolved = false, evolveClosed = false;
            if (evolveOpen)
            {
                yield return Click(Px(Q("board-evolve-go-1")));
                yield return Frames(3);
                shown = evolve.Now == EvolveScreen.Mode.Show && Laid(Q("board-evolve-ds"));
                yield return Snap("30-evolve");
                // 스페이스 바(결정 84 L11): 진화 연출(본인 PlayTiming.EvolveMs)을 착지로 건너뛴다 — 남은 꼬리(약 10%)만 돌고 연출이 끝난다
                float spaceAt = Time.realtimeSinceStartup;
                bool showingAtSpace = evolve.Now == EvolveScreen.Mode.Show;
                yield return Tap(Key.Space);
                yield return Wait(() => evolve.Now != EvolveScreen.Mode.Show, 12f);
                int afterSpaceMs = (int)((Time.realtimeSinceStartup - spaceAt) * 1000f);
                bool skipped = showingAtSpace && evolve.Now != EvolveScreen.Mode.Show && afterSpaceMs < Sinnoh.Rules.Session.PlayTiming.EvolveMs / 3;
                Step("evolve-key-space", "ok", skipped, "showing", showingAtSpace, "afterSpaceMs", afterSpaceMs, "fullMs", Sinnoh.Rules.Session.PlayTiming.EvolveMs);
                Check("진화 스페이스 건너뛰기", skipped, "스페이스 바가 진화 연출을 건너뛰지 않았다(눌렀을 때 연출 중 " + showingAtSpace + " · 끝나기까지 " + afterSpaceMs + "ms / 전체 " + Sinnoh.Rules.Session.PlayTiming.EvolveMs + "ms)");
                yield return Wait(() => flow.BoardOpen && evolve.Now != EvolveScreen.Mode.Show && (evolve.Now != EvolveScreen.Mode.Teach || Laid(Q("board-evolve-keep"))), 12f);
                if (evolve.Now == EvolveScreen.Mode.Teach && Laid(Q("board-evolve-keep"))) yield return Click(Px(Q("board-evolve-keep")));
                yield return Frames(3);
                yield return Wait(() => flow.BoardOpen && evolve.Now == EvolveScreen.Mode.Pick, 8f);
                evolved = flow.Client.View.Pokemon[starter].Def != before;
                yield return Snap("31-evolved");
                if (Laid(Q("board-evolve-close"))) yield return Click(Px(Q("board-evolve-close")));
                yield return Frames(3);
                evolveClosed = stage.Current == null;
            }
            Step("work-evolve", "ok", evolveOpen && shown && evolved && evolveClosed, "at", at, "open", evolveOpen, "ds", shown, "evolved", evolved, "closed", evolveClosed, "commands", flow.Session.CommandCount - commands);
            Check("진화 창", evolveOpen && shown && evolved && evolveClosed, "진화 창이 열리지 않았거나 진화가 끝나지 않았다(열림 " + evolveOpen + " · DS " + shown + " · 진화 " + evolved + " · 닫힘 " + evolveClosed + ")");
        }

        /// <summary>
        /// 한 칸에 말 여럿(결정 66 ①): 좌석 셋인 확인용 판(<see cref="GameFlow.StartSeats"/>)에서 말 셋을 한 칸에 모으고, 묶음 표지에 마우스를 올려
        /// 그 칸의 공개 정보 카드(좌석 셋)가 뜨는지, 표지 위에서 휠·칸 고르기가 그대로 듣는지 본다.
        /// </summary>
        /// <summary>
        /// 종막(M5 「막·종막 화면」 — S62·S63·S64): 종막 앞 판(<see cref="Sinnoh.Ai.SoloTrail.PrepareEnding"/>)에서 마우스로
        /// ① 좌석 셋 판 — 배신자 카드에 올려 툴팁 → 배신자 [선택] → 순서 고르기에서 좌석 3 · 좌석 2를 누르고 [확정] → 순서 칩이 놓인다(96행)
        /// ② 혼자 하는 판 — 영웅 [선택] → 결과 창(S64)이 끝까지 돌면 [타이틀로] → 타이틀.
        /// </summary>
        IEnumerator Finale()
        {
            var ctx = flow.Ctx;
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, 3, Seed, AiSeed, s => Sinnoh.Ai.SoloTrail.PrepareEnding(ctx, s));
            yield return Frames(6);
            yield return Wait(() => stage.Current is EndingScreen e && e.Asking && Laid(Q("ending-pick-1")) && Q("ending-pick-1").enabledSelf, 10f);
            var traitor = Q("ending-card-1");
            bool tip = false;
            if (Laid(traitor))
            {
                yield return Move(Px(traitor));
                yield return Frames(4);
                tip = stage.Tips.Target == traitor;
                yield return Snap("40-ending-tip");
                yield return Click(Px(Q("ending-pick-1")));
            }
            yield return Wait(() => stage.Current is EndingScreen e && e.Ordering && Laid(Q("ending-order-seat-2")), 15f);
            bool ordering = stage.Current is EndingScreen o && o.Ordering;
            Step("ending-click-traitor", "ok", tip && ordering, "at", at, "tip", tip, "ordering", ordering);
            if (!Check("종막 선택", ordering, "배신자 [선택]을 눌렀는데 순서 고르기가 열리지 않았다")) yield break;
            yield return Wait(() => flow.BoardOpen, 8f);   // 95행 연출(2800 ms) 동안에는 순서 칩이 눌리지 않는다
            yield return Frames(2);                        // 입력이 열리는 프레임에 순서 칸을 다시 그린다 — 새 칩이 자리 잡은 뒤에 누른다
            yield return Wait(() => flow.BoardOpen && Laid(Q("ending-order-seat-2")), 5f);
            yield return Click(Px(Q("ending-order-seat-2")));
            yield return Frames(3);
            yield return Click(Px(Q("ending-order-seat-1")));
            yield return Frames(3);
            yield return Snap("41-ending-order");
            int commands = flow.Session.CommandCount;
            yield return Click(Px(Q("ending-order-confirm")));
            yield return Wait(() => stage.Current is EndingScreen e && e.ChipsShown == 2, 10f);
            bool chips = stage.Current is EndingScreen c2 && c2.ChipsShown == 2;
            if (chips) yield return Snap("42-ending-chips");
            Step("ending-click-order", "ok", chips && flow.Session.CommandCount == commands + 1, "at", at, "chips", chips, "commands", flow.Session.CommandCount);
            Check("배신자 순서", chips, "순서를 고르고 [확정]을 눌렀는데 순서 칩이 놓이지 않았다");

            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, 1, Seed, AiSeed, s => Sinnoh.Ai.SoloTrail.PrepareEnding(ctx, s));
            yield return Frames(6);
            yield return Wait(() => stage.Current is EndingScreen e && e.Asking && Laid(Q("ending-pick-0")) && Q("ending-pick-0").enabledSelf, 10f);
            yield return Click(Px(Q("ending-pick-0")));
            yield return Frames(3);
            flow.Skip(2800 + 8000);   // 종막 선택(95행) · 결과(97행)의 전원 차단 연출
            yield return Wait(() => stage.Current is ResultScreen r && r.Ready && Laid(Q("result-title-button")), 15f);
            bool result = stage.Current is ResultScreen;
            if (result) yield return Snap("43-result");
            yield return Click(Px(Q("result-title-button")));
            yield return Frames(4);
            bool title = stage.Current is TitleScreen;
            Step("result-click-title", "ok", result && title, "at", at, "result", result, "title", title);
            Check("결과 창", result && title, "영웅 [선택] 뒤 결과 창의 [타이틀로]로 타이틀에 돌아오지 않았다");
        }

        /// <summary>
        /// 운명·응답(M5 「운명·응답 화면」 — S30 · S41): ① 운명 길(<see cref="Sinnoh.Ai.SoloTrail.Fate"/>) — 저절로 열린 6장째 버리기 창에서 새 카드를 누르고 [버리기] →
        /// HUD [운명의 변화] → 펄기아 카드 → [1장 사용] → 보드에 갈 칸이 칠해진다 → [취소](카드는 손에 남는다) ② 좌석 둘 판 — 좌석 2의 도전이 「행동」 칸 자리에 뜨고 [거절].
        /// </summary>
        IEnumerator Fate()
        {
            var pilot = FlowPilot.Start(flow, Sinnoh.Ai.SoloTrail.Fate, ProbeProtagonist);
            yield return Frames(6);
            yield return Wait(() => flow.Top is FateScreen f && f.Discarding && Laid(Q("board-fate-card-6")), 15f);
            bool asked = flow.Top is FateScreen;
            if (asked) yield return Snap("44-fate-discard");
            yield return Click(Px(Q("board-fate-card-6")));
            yield return Frames(3);
            yield return Click(Px(Q("board-fate-discard")));
            yield return Wait(() => !(flow.Top is FateScreen) && flow.InputOpen && Laid(Q(BoardHud.FateButton)), 15f);
            bool discarded = flow.Client.View.Seats[flow.Client.Seat].FateHand.Count == Sinnoh.Rules.FateRules.HandLimit;
            Step("fate-click-discard", "ok", asked && discarded, "at", at, "asked", asked, "hand", flow.Client.View.Seats[flow.Client.Seat].FateHand.Count);
            if (!Check("6장째 버리기", asked && discarded, "버리기 창에서 새 카드를 누르고 [버리기]를 눌렀는데 손패가 5장이 되지 않았다")) yield break;
            yield return Click(Px(Q(BoardHud.FateButton)));
            yield return Wait(() => flow.Top is FateScreen && Laid(Q("board-fate-card-1")), 10f);
            var fate = flow.Top as FateScreen;
            int palkia = -1;
            if (fate != null)
                for (int i = 0; i < fate.Hand.Count; i++)
                    if (Sinnoh.Rules.FateRules.KindOf(flow.Ctx, fate.Hand[i]) == Sinnoh.Rules.FateCard.Palkia) palkia = i;
            if (palkia >= 0) yield return Click(Px(Q("board-fate-card-" + (palkia + 1))));
            yield return Frames(3);
            yield return Snap("45-fate-hand");
            yield return Click(Px(Q("board-fate-use-1")));
            yield return Frames(4);
            bool warping = flow.Hud != null && flow.Hud.Warping && flow.Hud.Reach.Count > 0;
            if (warping) yield return Snap("46-fate-warp");
            yield return Click(Px(Q(BoardHud.WarpCancel)));
            yield return Frames(3);
            bool back = flow.Hud != null && !flow.Hud.Warping && flow.Client.View.Seats[flow.Client.Seat].FateHand.Count == Sinnoh.Rules.FateRules.HandLimit;
            Step("fate-click-warp", "ok", palkia >= 0 && warping && back, "at", at, "palkia", palkia, "warping", warping, "back", back);
            Check("펄기아 순간이동", warping && back, "[1장 사용](펄기아)으로 갈 칸이 칠해지고 [취소]로 돌아와야 한다");

            var clocks = new int[(int)Sinnoh.Rules.ClockKind.Respond + 1];
            clocks[(int)Sinnoh.Rules.ClockKind.Respond] = 30000;
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, 2, Seed, AiSeed, s => s.Seats[1].Cell = s.Seats[0].Cell, false, clocks);
            yield return Frames(6);
            flow.Skip(flow.OpeningMs);
            for (int k = 0; k < 3 && flow.Client.View.ActiveSeat != 1; k++)   // 좌석 2의 턴까지(첫 좌석은 판마다 다르다)
            {
                if (flow.Client.View.ActiveSeat == flow.Client.Seat)
                {
                    yield return Wait(() => flow.InputOpen && flow.Hud != null && flow.Hud.Shows(BoardHud.EndTurn), 15f);
                    flow.Hud.Press(BoardHud.EndTurn);
                }
                else flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == Sinnoh.Rules.AnswerKind.EndTurn);
                flow.Skip(3000);
            }
            bool challenged = flow.AnswerSeat(1, a => a.Kind == Sinnoh.Rules.AnswerKind.Challenge && a.Target == flow.Client.Seat);
            flow.Skip(1000);
            yield return Wait(() => flow.Hud != null && flow.Hud.Prompting && Laid(Q(BoardHud.PromptDecline)), 10f);
            bool prompt = flow.Hud != null && flow.Hud.Prompting;
            if (prompt) yield return Snap("47-prompt");
            yield return Click(Px(Q(BoardHud.PromptDecline)));
            yield return Frames(4);
            bool declined = flow.Client.Choice(Sinnoh.Rules.ChoiceKind.Respond) == null;
            Step("prompt-click-decline", "ok", challenged && prompt && declined, "at", at, "challenged", challenged, "prompt", prompt, "declined", declined);
            Check("응답 프롬프트", prompt && declined, "좌석 2의 도전이 「행동」 칸 자리에 뜨고 [거절]로 닫혀야 한다");
        }

        /// <summary>
        /// 사당·균열(M5 「사당·균열 화면」 — S31~S33): 사당 길(<see cref="Sinnoh.Ai.SoloTrail.Shrine"/>) — HUD [공물] → 사당 판에서 분홍 규토리 칩 · 「7점 더 바치기」 → [바치기] →
        /// 3장이 내려온 뒤 첫 카드(뜻밖의 선물)를 누르고 [확정] → 검정 규토리 칩 두 번 → [확정] → 남은 두 장 중 천상의 균열 → 펄기아 칸 → [확정] → 신 강림 뒤 판이 닫히고 좌석 칩에 추종자 아이콘.
        /// </summary>
        IEnumerator Shrine()
        {
            FlowPilot.Start(flow, Sinnoh.Ai.SoloTrail.Shrine, ProbeProtagonist);
            yield return Frames(6);
            yield return Wait(() => flow.InputOpen && flow.Hud != null && flow.Hud.Shows(BoardHud.Offer) && Laid(Q("board-hud-act-" + BoardHud.Offer)), 20f);
            yield return Click(Px(Q("board-hud-act-" + BoardHud.Offer)));
            yield return Wait(() => stage.Current is ShrineScreen && Laid(Q("board-shrine-color-pink")), 10f);
            bool opened = stage.Current is ShrineScreen;
            if (opened) yield return Snap("48-shrine-offer");
            yield return Click(Px(Q("board-shrine-color-pink")));
            yield return Frames(3);
            yield return Click(Px(Q("board-shrine-extra")));
            yield return Frames(3);
            var panel = stage.Current as ShrineScreen;
            bool picked = panel != null && panel.OfferColor == (int)Sinnoh.Rules.ApricornColor.Pink && panel.Extra;
            yield return Click(Px(Q("board-shrine-go")));
            yield return Wait(() => stage.Current is ShrineScreen s && s.Shown == ShrineScreen.Mode.Cards && s.Open && Laid(Q("board-shrine-card-1")), 20f);
            bool cards = stage.Current is ShrineScreen s3 && s3.Cards.Count == 3;
            if (cards) yield return Snap("49-shrine-cards");
            Step("shrine-click-offer", "ok", opened && picked && cards, "at", at, "opened", opened, "picked", picked, "cards", cards);
            if (!Check("사당 공물", opened && picked && cards, "[공물] → 분홍 · 7점 → [바치기]로 카드 3장이 내려와야 한다")) yield break;
            yield return Click(Px(Q("board-shrine-card-1")));
            yield return Frames(3);
            yield return Click(Px(Q("board-shrine-go")));
            yield return Wait(() => stage.Current is ShrineScreen s && s.Shown == ShrineScreen.Mode.Apricorns && s.Open && Laid(Q("board-shrine-color-black")), 20f);
            for (int i = 0; i < 2; i++)
            {
                yield return Click(Px(Q("board-shrine-color-black")));
                yield return Frames(3);
            }
            yield return Click(Px(Q("board-shrine-go")));
            yield return Wait(() => stage.Current is ShrineScreen s && s.Shown == ShrineScreen.Mode.Cards && s.Open && Laid(Q("board-shrine-card-1")), 20f);
            bool gift = flow.Client.View.Seats[flow.Client.Seat].Apricorns[(int)Sinnoh.Rules.ApricornColor.Black] == 3;
            Step("shrine-click-gift", "ok", gift, "at", at, "black", flow.Client.View.Seats[flow.Client.Seat].Apricorns[(int)Sinnoh.Rules.ApricornColor.Black]);
            Check("뜻밖의 선물", gift, "검정 규토리 칩을 두 번 누르고 [확정]으로 검정 2개를 받아야 한다");
            yield return Click(Px(Q("board-shrine-card-1")));   // 남은 두 장의 첫 장 = 천상의 균열
            yield return Frames(3);
            yield return Click(Px(Q("board-shrine-go")));
            yield return Wait(() => stage.Current is ShrineScreen s && s.Shown == ShrineScreen.Mode.Follower && s.Open && Laid(Q("board-shrine-god-palkia")), 20f);
            bool asked = stage.Current is ShrineScreen s4 && s4.Shown == ShrineScreen.Mode.Follower;
            if (asked) yield return Snap("50-shrine-follower");
            yield return Click(Px(Q("board-shrine-god-palkia")));
            yield return Frames(3);
            yield return Click(Px(Q("board-shrine-go")));
            yield return Wait(() => stage.Current == null && flow.InputOpen && flow.Hud != null && flow.Hud.FollowerShown(flow.Client.Seat), 30f);
            bool follower = flow.Client.View.Seats[flow.Client.Seat].Follower == Sinnoh.Rules.SinnohGod.Palkia;
            bool descended = Sinnoh.Rules.FractureRules.OriginGod(flow.Client.View, flow.Ctx) == Sinnoh.Rules.SinnohGod.Palkia;
            bool chip = flow.Hud != null && flow.Hud.FollowerShown(flow.Client.Seat);
            if (chip) yield return Snap("51-shrine-descended");
            Step("shrine-click-follower", "ok", asked && follower && descended && chip, "at", at, "asked", asked, "follower", follower, "descended", descended, "chip", chip);
            Check("천상의 균열", asked && follower && descended && chip, "펄기아 칸 → [확정]으로 펄기아가 강림하고 좌석 칩에 추종자 아이콘이 서야 한다");
        }

        /// <summary>
        /// 스탬프·거래(M5 「스탬프·도전·거래 화면」 — S36·S37): 좌석 둘 판(같은 칸 · 내 파랑 규토리 3 · 상대 여행 점수 4)의 내 턴 — HUD [스탬프 n/7] → 스탬프 판 → [닫기] →
        /// HUD [거래] → 거래 창에서 내 파랑 규토리 칩 · 상대의 여행 점수 칩 → [제안] → 좌석 2의 수락(확인용 답) → 거래 띠(52행) 뒤 주고받은 수.
        /// </summary>
        IEnumerator Deal()
        {
            int blue = (int)Sinnoh.Rules.ApricornColor.Blue;
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, 2, Seed, AiSeed, s =>
            {
                s.Seats[1].Cell = s.Seats[0].Cell;
                s.Seats[0].Apricorns[blue] = 3;
                s.Seats[1].Jp = 4;
                s.Seats[1].Stamps[Sinnoh.Rules.StampRules.TrainerWin] = true;   // 남 좌석의 스탬프 판(결정 80 N2 나)
            });
            yield return Frames(6);
            flow.Skip(flow.OpeningMs);
            for (int k = 0; k < 3 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)   // 내 턴까지(첫 좌석은 판마다 다르다)
            {
                flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == Sinnoh.Rules.AnswerKind.EndTurn);
                flow.Skip(3000);
            }
            yield return Wait(() => flow.InputOpen && flow.Hud != null && flow.Hud.Shows(BoardHud.Trade) && Laid(Q(BoardHud.StampButton)), 20f);
            yield return Click(Px(Q(BoardHud.StampButton)));
            yield return Wait(() => stage.Current is StampScreen && Laid(Q("board-stamp-close")), 10f);
            bool sheet = stage.Current is StampScreen;
            if (sheet) yield return Snap("52-stamp-sheet");
            yield return Click(Px(Q("board-stamp-close")));
            yield return Wait(() => stage.Current == null && flow.InputOpen && Laid(Q("board-hud-act-" + BoardHud.Trade)), 10f);
            bool closed = stage.Current == null;
            Step("stamp-click-sheet", "ok", sheet && closed, "at", at, "sheet", sheet, "closed", closed);
            if (!Check("스탬프 판", sheet && closed, "[스탬프 n/7]로 스탬프 판이 열리고 [닫기]로 닫혀야 한다")) yield break;
            // Esc(결정 80 N3 나): 다시 열어 Esc로 닫는다 — [닫기]와 같은 길
            yield return Click(Px(Q(BoardHud.StampButton)));
            yield return Wait(() => stage.Current is StampScreen && Laid(Q("board-stamp-close")), 10f);
            bool reopened = stage.Current is StampScreen;
            yield return Tap(Key.Escape);
            yield return Wait(() => stage.Current == null && flow.InputOpen && Laid(Q("board-hud-act-" + BoardHud.Trade)), 10f);
            bool escClosed = reopened && stage.Current == null;
            Step("stamp-key-esc", "ok", escClosed, "reopened", reopened, "closed", stage.Current == null);
            if (!Check("스탬프 판 Esc", escClosed, "스탬프 판이 Esc로 닫혀야 한다")) yield break;
            // 남 좌석의 스탬프 판(결정 80 N2 나): 좌석 칩 → 정비 판(S39)의 「여행 스탬프 n/7」 → 그 좌석의 스탬프 판 → Esc
            int other = 1 - flow.Client.Seat;
            yield return Click(Px(Q("board-hud-seat-" + (other + 1))));
            yield return Wait(() => stage.Current is PartyScreen && Laid(Q("board-party-stamps")), 10f);
            bool party = stage.Current is PartyScreen;
            yield return Click(Px(Q("board-party-stamps")));
            yield return Wait(() => stage.Current is StampScreen && Laid(Q("board-stamp-close")), 10f);
            bool theirs = stage.Current is StampScreen seatSheet && seatSheet.Seat == other && seatSheet.Count == 1;
            if (theirs) yield return Snap("52-stamp-seat");
            yield return Tap(Key.Escape);
            yield return Wait(() => stage.Current == null && flow.InputOpen && Laid(Q("board-hud-act-" + BoardHud.Trade)), 10f);
            bool seatClosed = stage.Current == null;
            Step("seat-stamp-click", "ok", party && theirs && seatClosed, "party", party, "theirs", theirs, "closed", seatClosed);
            if (!Check("남 좌석의 스탬프 판", party && theirs && seatClosed, "좌석 칩 → 정비 판의 「여행 스탬프 n/7」로 그 좌석의 스탬프 판이 열리고 Esc로 닫혀야 한다")) yield break;

            yield return Click(Px(Q("board-hud-act-" + BoardHud.Trade)));
            yield return Wait(() => stage.Current is TradeScreen && Laid(Q("board-trade-give-apricorn-" + blue)), 10f);
            bool opened = stage.Current is TradeScreen;
            yield return Click(Px(Q("board-trade-give-apricorn-" + blue)));
            yield return Frames(3);
            yield return Click(Px(Q("board-trade-ask-jp--1")));
            yield return Frames(3);
            var trade = stage.Current as TradeScreen;
            bool put = trade != null && trade.Put(0, Sinnoh.Rules.TradeKind.Apricorn, blue) == 1 && trade.Put(1, Sinnoh.Rules.TradeKind.Jp, -1) == 1;
            if (put) yield return Snap("53-trade");
            yield return Click(Px(Q("board-trade-propose")));
            yield return Wait(() => stage.Current == null && flow.TradeWaiting, 10f);
            bool proposed = flow.TradeWaiting;
            bool accepted = flow.AnswerSeat(1 - flow.Client.Seat, a => a.Kind == Sinnoh.Rules.AnswerKind.Accept);
            yield return Wait(() => flow.Deals != null && flow.Deals.TradeShown, 10f);
            bool band = flow.Deals != null && flow.Deals.TradeShown;
            if (band) yield return Snap("54-trade-done");
            yield return Wait(() => flow.InputOpen && !flow.TradeWaiting, 15f);
            var me = flow.Client.View.Seats[flow.Client.Seat];
            bool swapped = me.Apricorns[blue] == 2 && flow.Client.View.Seats[1 - flow.Client.Seat].Jp == 3;
            Step("trade-click-propose", "ok", opened && put && proposed && accepted && band && swapped, "at", at, "opened", opened, "put", put, "proposed", proposed, "accepted", accepted, "band", band, "swapped", swapped);
            Check("거래", opened && put && proposed && accepted && band && swapped, "[거래] → 칩 둘 → [제안] → 수락으로 파랑 규토리 1개와 여행 점수 1점을 주고받아야 한다");
        }

        /// <summary>
        /// 사람이 못 쓰던 규칙 둘(M5 마감 답 반영 나-1 — 결정 80 N1 가): 손본 판(금강옥을 지닌 디아루가 · 애크러뱃을 배운 찌르꼬 · 스타팅 포켓몬)에서
        /// HUD [정비] → 디아루가 칸의 [폼체인지] → 물음 줄의 [폼체인지] → 오리진폼 → [닫기] / [도전] → 첫 출전 디아루가 → 차례 칸의 [대기 포켓몬 기술] → 그 줄 → 찌르꼬가 나오며 애크러뱃.
        /// </summary>
        // kindler B(골드로 쥐여 준 판): [장작 모으기] → 나무 칸을 칠한 고르기 · 머리 「카드 — 능력」 → [취소]로 돌아온다
        IEnumerator Card()
        {
            var ctx = flow.Ctx;
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, 1, Seed, AiSeed, s =>
            {
                s.Seats[0].TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("kindler");
                s.Seats[0].TrainerCardTier = Sinnoh.Rules.TrainerRules.Gold;
            });
            yield return Frames(6);
            flow.Skip(flow.OpeningMs);
            string act = "board-hud-act-" + BoardHud.Ability;
            yield return Wait(() => flow.InputOpen && flow.Hud != null && Laid(Q(act)), 20f);
            bool button = Laid(Q(act));
            if (button) yield return Click(Px(Q(act)));
            yield return Wait(() => flow.Hud.AbilityPicking && Laid(Q(BoardHud.AbilityCancel)), 10f);
            bool picking = flow.Hud.AbilityPicking && flow.Hud.Reach.Count > 0 && Q(BoardHud.CardHead) != null;
            if (picking) yield return Snap("57-card-cells");
            if (picking) yield return Click(Px(Q(BoardHud.AbilityCancel)));
            yield return Wait(() => !flow.Hud.AbilityPicking && Laid(Q(act)), 10f);
            bool back = !flow.Hud.AbilityPicking;
            Step("card-click-ability", "ok", button && picking && back, "at", at, "button", button, "picking", picking, "back", back);
            Check("트레이너 카드 능력", button && picking && back, "[장작 모으기] → 나무 칸 고르기(머리 · [취소]) → [취소]로 행동 단추 줄이 돌아와야 한다");
            if (finished) yield break;

            // 정비 판의 카드 능력(묶음 2): black_belt R — [정비] → 능력 줄 [투혼] → 포켓몬 카드 → 물음 줄 [사용] → 화상
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, 1, Seed, AiSeed, s =>
            {
                s.Seats[0].TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("black_belt");
                s.Seats[0].TrainerCardTier = Sinnoh.Rules.TrainerRules.Gold;
            });
            yield return Frames(6);
            flow.Skip(flow.OpeningMs);
            yield return Wait(() => flow.InputOpen && flow.Hud != null && Laid(Q("board-hud-maint")), 20f);
            yield return Click(Px(Q("board-hud-maint")));
            string abil = "board-party-abil-afflict";
            yield return Wait(() => Laid(Q(abil)), 10f);
            bool shown = Laid(Q(abil));
            if (shown) yield return Click(Px(Q(abil)));
            yield return Wait(() => flow.Stage.Current is PartyScreen p && p.Picking != null, 10f);
            bool open = flow.Stage.Current is PartyScreen pk && pk.Picking == Sinnoh.Rules.MaintKind.Afflict;
            if (open) yield return Click(Px(Q("board-party-mon-0")));
            yield return Wait(() => Laid(Q("board-party-ask-yes")), 10f);
            bool asked = Laid(Q("board-party-ask-yes"));
            if (asked) yield return Snap("57-card-party");
            if (asked) yield return Click(Px(Q("board-party-ask-yes")));
            yield return Wait(() => FirstBurned(), 10f);
            bool burned = FirstBurned();
            if (Laid(Q("board-party-close"))) yield return Click(Px(Q("board-party-close")));
            Step("card-click-party", "ok", shown && open && asked && burned, "at", at, "shown", shown, "open", open, "asked", asked, "burned", burned);
            Check("정비 판 카드 능력", shown && open && asked && burned, "[정비] → [투혼] → 포켓몬 카드 → [사용]으로 화상이 걸려야 한다");
            if (finished) yield break;

            // 카드 바꾸기 창(결정 87 V6): youngster R — 한 칸 걸으면 다음 맵 행동 앞에서 부채꼴 창 → 카드를 눌러 꺼냄 → [이 카드로 교체] → 카드가 바뀐다
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, 1, Seed, AiSeed, s =>
            {
                var st = s.Seats[0];
                st.TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("youngster");
                st.TrainerCardTier = Sinnoh.Rules.TrainerRules.Gold;
                st.TrainerCardOffer.Clear();
                foreach (string k in new[] { "hiker", "swimmer", "chef" }) st.TrainerCardOffer.Add(ctx.Cards.TrainerCardIds.IndexOf(k));
                st.UnlockDue.Add(Sinnoh.Rules.TrainerRules.DueSwap);
                st.UnlockDue.Add(0);
            });
            yield return Frames(6);
            flow.Skip(flow.OpeningMs);
            yield return Wait(() => flow.InputOpen && flow.Hud != null, 20f);
            int step = -1;
            foreach (int c in flow.ActiveReach())
                if (flow.Hud.StopsBefore(c).Count == 0) { step = c; break; }
            bool walked = step >= 0 && flow.Hud.Walk(step);
            yield return Wait(() => flow.Stage.Current is CardSwapScreen w && flow.BoardOpen && w.Answers.Count > 0 && Laid(Q("board-card-swap-card-1")), 20f);
            bool fan = flow.Stage.Current is CardSwapScreen;
            int was = flow.Client.View.Seats[0].TrainerCard;
            if (fan) yield return Click(Px(Q("board-card-swap-card-1")));
            yield return Frames(3);
            bool pulled = flow.Stage.Current is CardSwapScreen sw && sw.Picked == 0;
            if (pulled) yield return Snap("57-card-swap");
            if (pulled) yield return Click(Px(Q(CardSwapScreen.Go)));
            yield return Wait(() => !(flow.Stage.Current is CardSwapScreen) && flow.InputOpen, 20f);
            bool swapped = !(flow.Stage.Current is CardSwapScreen) && flow.Client.View.Seats[0].TrainerCard != was;
            Step("card-click-swap", "ok", walked && fan && pulled && swapped, "at", at, "walked", walked, "fan", fan, "pulled", pulled, "swapped", swapped);
            Check("카드 바꾸기 창", walked && fan && pulled && swapped, "한 칸 걸은 뒤 부채꼴 창 → 카드 → [이 카드로 교체]로 트레이너 카드가 바뀌어야 한다");
        }

        /// <summary>마우스 확인 <see cref="FlyMap"/>의 단계 이름(차례대로 — 실제 실행은 묶음 끝 감사의 window-probe).</summary>
        public static readonly string[] FlyMapSteps = { "fly-map-open", "fly-map-click", "fly-map-cancel" };

        /// <summary>
        /// T18 ① 확인 판(<see cref="FlyMap"/> · PlayMode <c>PeekFlowTests</c>가 같은 판을 쓴다): 혼자 하는 판의 첫 턴은 고향 마을의 조무래기가 공중날기를 막으므로
        /// 내 말을 고향이 아닌 첫 마을로 옮기고 빨강 규토리 하나를 쥐여 준다 — 그 마을에서 [마을] → 공중날기가 열린다(고향 마을은 조무래기 점거로 흐리게).
        /// </summary>
        public static void StartFlyGame(GameFlow flow, ulong seed, ulong aiSeed)
        {
            var ctx = flow.Ctx;
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, 1, seed, aiSeed, s =>
            {
                var st = s.Seats[0];
                var fixtures = ctx.Fixtures.Fixtures;
                for (int f = 0; f < fixtures.Count; f++)
                    if (fixtures[f].Kind == Sinnoh.Rules.FixtureKind.Village && f != st.HomeVillage && !ctx.Board[fixtures[f].Cell].DistortionWorld) { st.Cell = fixtures[f].Cell; break; }
                st.Apricorns[(int)Sinnoh.Rules.ApricornColor.Red] = Math.Max(1, st.Apricorns[(int)Sinnoh.Rules.ApricornColor.Red]);
            });
        }

        /// <summary>
        /// T18 ① 공중날기 목적지를 보드에서 고르기(<see cref="StartFlyGame"/>): [마을] → 공중날기 칸 [지도에서 고르기] → 창이 오른쪽 기둥으로 비키고 갈 수 있는 마을이 칠해진다(<c>fly-map-open</c>) →
        /// 화면에 보이는 갈 수 있는 마을 칸을 실제 포인터로 누르면 그 목적지가 골라지고 답은 나가지 않는다(<c>fly-map-click</c>) → [취소] → 창 꼴로 · 카메라는 고르기 전 자리로(<c>fly-map-cancel</c>) → [닫기].
        /// </summary>
        IEnumerator FlyMap()
        {
            StartFlyGame(flow, Seed, AiSeed);
            yield return Frames(6);
            flow.Skip(flow.OpeningMs);
            string act = "board-hud-act-" + BoardHud.Village;
            yield return Wait(() => flow.InputOpen && flow.Hud != null && Laid(Q(act)), 20f);
            if (!Check("공중날기 보드에서 고르기", Laid(Q(act)), "옮겨 둔 마을에서 [마을] 단추가 서지 않았다")) yield break;
            yield return Click(Px(Q(act)));
            yield return Wait(() => stage.Current is VillageScreen && Laid(Q("board-village-map")), 10f);
            var village = stage.Current as VillageScreen;
            var map = Q("board-village-map");
            if (!Check("공중날기 보드에서 고르기", village != null && village.CanFly && Laid(map) && map.enabledSelf, "[마을] 뒤 마을 창의 [지도에서 고르기]가 켜져 있지 않다(공중날기가 막혔다)")) yield break;
            var look = flow.SaveLook();
            yield return Click(Px(map));
            yield return Wait(() => flow.FlyPick == village && Laid(Q("board-village-cancel")), 10f);
            yield return Frames(4);   // 창이 오른쪽 기둥에 자리를 잡고 카메라가 그 왼쪽으로 맞춘 뒤
            var panel = Q("board-village-panel");
            var scrim = Q("board-village-scrim");
            var lit = new List<int>();
            foreach (int f in village.Dests)
                if (village.CanGo(f) && !lit.Contains(flow.Ctx.Fixtures.Fixtures[f].Cell)) lit.Add(flow.Ctx.Fixtures.Fixtures[f].Cell);
            var marks = flow.BoardMarks;
            bool painted = marks != null && lit.Count > 0 && marks.Reach.Count == lit.Count && lit.TrueForAll(c => { foreach (int r in marks.Reach) if (r == c) return true; return false; }) && marks.Dims.Count > 0;
            bool opened = flow.FlyPick == village && panel != null && panel.ClassListContains("sn-village-side") && (scrim == null || scrim.resolvedStyle.display == DisplayStyle.None)
                && rig.CurrentZoom == BoardCameraRig.Zoom.Full && painted;
            Step(FlyMapSteps[0], "ok", opened, "at", at, "lit", lit.Count, "dims", marks != null ? marks.Dims.Count : -1, "zoom", rig.CurrentZoom.ToString());
            yield return Snap("58-fly-map");
            if (!Check("공중날기 보드에서 고르기", opened, "[지도에서 고르기] 뒤 창이 오른쪽 기둥 · 가림막 없음 · 전체 줌 · 칠한 칸 = 합법 Fly 답의 마을 · 흐린 마을이어야 한다")) yield break;

            // 화면에 보이는 갈 수 있는 마을 칸(창 위가 아니고 · 앞의 높은 칸에 가리지 않음)을 실제 포인터로 누른다
            int w = root.Size.x, h = root.Size.y, picked = -1;
            Vector2 cellPx = default;
            foreach (int f in village.Dests)
            {
                if (!village.CanGo(f)) continue;
                int cell = flow.Ctx.Fixtures.Fixtures[f].Cell;
                if (!BoardPick.Screen(board, cam, cell, w, h, out var px) || px.x < 0 || px.x >= w || px.y < 0 || px.y >= h || flow.OverUi(px.x, px.y)) continue;
                if (BoardPick.Cell(board, cam, px.x, px.y, w, h) != cell) continue;
                picked = f;
                cellPx = px;
                break;
            }
            if (!Check("공중날기 보드에서 고르기", picked >= 0, "보드에 보이는(창에 가리지 않은) 갈 수 있는 마을 칸이 없다")) yield break;
            int commands = flow.Session.CommandCount;
            yield return Move(cellPx);
            int pickedCell = flow.Ctx.Fixtures.Fixtures[picked].Cell;
            bool hovered = marks.Target == pickedCell;
            yield return Click(cellPx);
            yield return Frames(3);
            bool chose = village.Dest == picked && flow.Session.CommandCount == commands && Laid(Q("board-village-fly")) && Q("board-village-fly").enabledSelf;
            Step(FlyMapSteps[1], "ok", hovered && chose, "at", at, "cell", pickedCell, "hovered", hovered, "dest", village.Dest, "want", picked, "quiet", flow.Session.CommandCount == commands);
            yield return Snap("58-fly-map-click");
            if (!Check("공중날기 보드에서 고르기", hovered && chose, "보드의 마을 칸에 올리면 강조되고(" + hovered + ") 누르면 그 마을이 목적지 · 답 없음 · [출발]이 켜져야 한다(" + chose + ")")) yield break;

            yield return Click(Px(Q("board-village-cancel")));
            yield return Wait(() => !village.Picking && Laid(Q("board-village-close")), 10f);
            yield return Frames(3);
            bool back = stage.Current == village && !village.Picking && Q("board-village-heal") != null && marks.Dims.Count == 0
                && rig.CurrentZoom == look.Zoom && Vector3.Distance(rig.Target, look.Target) < 1e-3f && rig.Yaw == look.Yaw && village.Dest == picked;
            Step(FlyMapSteps[2], "ok", back, "at", at, "zoom", rig.CurrentZoom.ToString(), "wantZoom", look.Zoom.ToString(), "targetOff", Vector3.Distance(rig.Target, look.Target), "dest", village.Dest);
            Check("공중날기 보드에서 고르기", back, "[취소] 뒤 창 꼴로 돌아오고(칠하기를 걷고 · 목적지는 남고) 카메라가 고르기 전 자리로 돌아와야 한다");
            if (Laid(Q("board-village-close"))) yield return Click(Px(Q("board-village-close")));
            yield return Frames(3);
        }

        bool FirstBurned()
        {
            var v = flow.Client.View;
            return v.Pokemon[v.Seats[0].Party[0]].Status == Sinnoh.Rules.StatusEffect.Burned;
        }

        /// <summary>
        /// 울트라 버스트 판(M6 「UB 시작」): 타이틀 → [혼자 하기] → 「캠페인」 울트라 버스트(울트라 온슬롯 줄이 켜진다) → [게임 시작] → 고향 마을 첫 줄 [확정] →
        /// 스타팅 포켓몬·트레이너 카드 [확정] → 보드(깨어진 세계가 보이고 UB 9 · 네크로즈마 피규어가 토큰 칸에 섰다 · 파티 n/6).
        /// </summary>
        IEnumerator Ub()
        {
            flow.OpenTitle(TitleScreen.Solo);
            yield return Frames(3);
            yield return Click(Px(Q("title-item-" + TitleScreen.Solo)));
            yield return Frames(3);
            if (!Check("UB 솔로 설정", stage.Current is SoloScreen, "「혼자 하기」를 눌렀는데 솔로 설정이 열리지 않았다")) yield break;
            yield return Click(Px(Q("solo-opt-" + SoloScreen.Campaign + "-ub")));
            yield return Frames(2);
            yield return Click(Px(Q("solo-opt-" + SoloScreen.GalacticAi + "-basic")));   // 확인 판은 원칙 AI로(처음 값은 「강함」 — 결정 118 ②)
            yield return Click(Px(Q("solo-opt-" + SoloScreen.TrainerCards + "-on")));   // 트레이너 카드 고르기까지(처음 값은 「끔」 — 결정 134)
            var solo = (SoloScreen)stage.Current;
            bool ub = solo.Settings.Campaign == Campaign.UltraBurst && solo.RowEnabled(SoloScreen.Onslaught) && !Q("solo-row-" + SoloScreen.Onslaught).ClassListContains("sn-off");
            Step("ub-click-campaign", "ok", ub, "at", at, "campaign", solo.Settings.Campaign.ToString());
            yield return Snap("60-ub-solo");
            if (!Check("UB 솔로 설정", ub, "「울트라 버스트」를 눌렀는데 골라지지 않았거나 울트라 온슬롯 줄이 켜지지 않았다")) yield break;
            yield return Click(Px(Q("solo-start")));
            yield return Frames(3);
            if (!Check("UB 셋업", stage.Current is SetupVillageScreen, "[게임 시작] 뒤 고향 마을 고르기가 열리지 않았다")) yield break;
            yield return Click(Px(stage.Root.Q("setup-village-list").Q(className: "sn-setup-item")));
            yield return Click(Px(stage.Root.Q<Button>("setup-village-confirm")));
            yield return Wait(() => stage.Current is SetupPickScreen, 6f);
            yield return Wait(() => flow.SetupOpen, 6f);
            yield return Frames(3);
            if (!Check("UB 셋업", stage.Current is SetupPickScreen && flow.SetupOpen, "고향 마을 [확정] 뒤 고르는 화면이 열리지 않았다")) yield break;
            yield return Click(Px(stage.Root.Q("setup-pick-starters").Q(className: "sn-pick-tile").Q(className: "sn-pick-name")));
            var cards = stage.Root.Q("setup-pick-cards");
            if (cards != null) yield return Click(Px(cards.Q(className: "sn-pick-tile").Q(className: "sn-pick-name")));
            yield return Snap("61-ub-pick");
            yield return Click(Px(Q("setup-pick-confirm")));
            yield return Frames(4);
            flow.Skip(flow.OpeningMs);
            yield return Wait(() => flow.InputOpen && flow.Hud != null, 20f);
            yield return Frames(4);
            var view = flow.Client != null ? flow.Client.View : null;
            int standing = 0;
            if (board.Figures != null)
                foreach (var f in board.Figures.Figures)
                    if ((f.Group == GameFlow.BeastFigures || f.Group == GameFlow.NecrozmaFigures) && f.Root != null && f.Root.activeSelf) standing++;
            bool onBoard = view != null && view.Campaign == Campaign.UltraBurst && flow.InputOpen && board.DistortionWorldVisible && standing == Sinnoh.Rules.UbState.Beasts + 1 && view.Seats[0].OpenSlots == 6;
            Step("ub-board", "ok", onBoard, "campaign", view != null ? view.Campaign.ToString() : "", "distortion", board.DistortionWorldVisible, "figures", standing, "slots", view != null ? view.Seats[0].OpenSlots : -1);
            yield return Snap("62-ub-board");
            if (!Check("UB 보드", onBoard, "울트라 버스트 판의 보드가 서지 않았다(깨어진 세계 · UB 9 + 네크로즈마 피규어 " + standing + " · 파티 6칸)")) yield break;
            // S56 균열 칩(M6 「UB 화면 묶음 1」 — 결정 84 M1 가): 위 띠의 칩에 마우스를 올리면 균열 강도 툴팁(제목 「균열 강도 n%」)
            var gauge = Q(BoardUb.Gauge);
            yield return Move(Px(gauge));
            yield return Frames(3);
            var gaugeLabels = stage.Tips.Element.Query<Label>().ToList();
            string gaugeTitle = gaugeLabels.Count > 0 ? gaugeLabels[0].text.Replace("⁠", "") : "";
            bool gaugeTip = stage.Tips.Target == gauge && stage.Tips.Element.resolvedStyle.display == DisplayStyle.Flex
                && gaugeTitle == stage.T("screen.board.ub.breach.title").Replace("{n}", flow.Hud.Ub.ShownBreach.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("⁠", "");
            Step("ub-gauge-hover", "ok", gaugeTip, "at", at, "title", gaugeTitle);
            yield return Snap("63-ub-gauge");
            Check("UB 균열 칩", gaugeTip, "균열 칩에 마우스를 올렸는데 균열 강도 툴팁이 뜨지 않았다(" + gaugeTitle + ")");
            // M6 「UB 화면 묶음 2」: [턴 종료] → 라운드 끝 「UB 턴을 건너뛰겠습니까?」 창(결정 84 U2) → [건너뛰지 않음] → 창이 닫히고 UB 턴이 돈다
            yield return Click(Px(Q("board-hud-act-" + BoardHud.EndTurn)));
            yield return Wait(() => flow.Top is BlessScreen ask && !ask.IsPanel && flow.BoardOpen, 20f);
            yield return Frames(3);
            bool asked = flow.Top is BlessScreen shown && !shown.IsPanel;
            yield return Snap("64-ub-skip-ask");
            if (!Check("UB 건너뛰기 물음", asked, "라운드 끝에 건너뛰기 물음 창이 열리지 않았다")) yield break;
            // [접기] → 머리 줄만 남은 띠(답 단추가 없다) → [펼치기](결정 87 V19 ①)
            yield return Click(Px(Q("board-bless-fold")));
            yield return Frames(3);
            bool folded = flow.Top is BlessScreen fd && fd.Folded && Q("board-bless-no") == null;
            yield return Snap("64-ub-fold");
            if (folded) yield return Click(Px(Q("board-bless-fold")));
            yield return Frames(3);
            bool unfolded = flow.Top is BlessScreen uf && !uf.Folded && Laid(Q("board-bless-no"));
            Step("ub-fold-click", "ok", folded && unfolded, "at", at, "folded", folded, "unfolded", unfolded);
            if (!Check("UB 창 접기", folded && unfolded, "[접기]로 띠가 되고 [펼치기]로 답 단추가 돌아와야 한다")) yield break;
            yield return Click(Px(Q("board-bless-no")));
            yield return Wait(() => !(flow.Top is BlessScreen), 6f);
            Step("ub-skip-ask", "ok", asked && !(flow.Top is BlessScreen), "blessings", view.Ub != null ? flow.Client.View.Ub.Blessings : -1);
            Check("UB 건너뛰기 물음", !(flow.Top is BlessScreen), "[건너뛰지 않음] 뒤 창이 닫히지 않았다");
            // 위 띠 축복 칩을 누르면 축복 판(S59 — 결정 84 M4 가) → [닫기]
            yield return Wait(() => flow.CanOpenBless && flow.BoardOpen && flow.Client.Choice(ChoiceKind.MapAction) != null, 30f);
            yield return Click(Px(Q(BoardUb.Bless)));
            yield return Wait(() => flow.Top is BlessScreen panel && panel.IsPanel, 6f);
            bool chipOpened = flow.Top is BlessScreen opened && opened.IsPanel;
            yield return Frames(3);
            yield return Snap("65-ub-bless-chip");
            if (!Check("UB 축복 칩", chipOpened, "축복 칩을 눌렀는데 축복 판이 열리지 않았다")) yield break;
            yield return Click(Px(Q("board-bless-close")));
            yield return Wait(() => !(flow.Top is BlessScreen), 6f);
            Step("ub-bless-chip", "ok", chipOpened && !(flow.Top is BlessScreen), "at", at);
            Check("UB 축복 칩", !(flow.Top is BlessScreen), "축복 판의 [닫기] 뒤 판이 닫히지 않았다");
        }

        /// <summary>마우스 확인 <see cref="Peek"/>의 단계 이름(차례대로 — 실제 실행은 묶음 끝 감사의 window-probe).</summary>
        public static readonly string[] PeekSteps = { "peek-bless", "peek-bless-fold-drag", "peek-breach" };

        /// <summary>
        /// T35 · 결정 125 확인 판(<see cref="Peek"/> · PlayMode <c>PeekFlowTests</c>가 같은 판을 쓴다): 혼자 하는 울트라 버스트 판 + 내 손의 왜곡 균열 카드 한 장 —
        /// [턴 종료] 뒤 라운드 끝에 「UB 턴을 건너뛰겠습니까?」(축복 물음), UB 턴 뒤 내 턴이 시작되면 붙이기 창(균열 카드 창)이 열린다.
        /// </summary>
        public static void StartPeekGame(GameFlow flow, ulong seed, ulong aiSeed)
        {
            var ctx = flow.Ctx;
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Campaign = Campaign.UltraBurst, Protagonist = ProbeProtagonist }, 1, seed, aiSeed, st =>
            {
                st.Ub.BreachHands.Add(0);
                st.Ub.BreachHands.Add(BreachRules.CardOf(ctx, BreachKind.RecklessAssault));
            });
        }

        /// <summary>
        /// 물음 창 위의 메뉴 · 정보 창(M9 플레이 시험 T35 · 결정 125 · T21 — <see cref="StartPeekGame"/>): 축복 물음 · 균열 카드 창이 떠 있는 동안 위 띠 [메뉴] · [상성] · 아래 판 [정비] · 위 띠 [도감]을
        /// 실제 마우스로 누르면 정보 창 층에 열리고(정비 판은 보기만) Esc로 닫으면 물음 창이 그대로 · 답이 나가지 않는다(<c>peek-bless</c> · <c>peek-breach</c>) →
        /// 축복 물음 [접기] → 보드 가운데를 끌면 보는 점이 옮겨지고 칸은 눌리지 않는다 → [펼치기](<c>peek-bless-fold-drag</c>) → [건너뛰지 않음]. 균열 카드 창은 [붙이기]로 닫는다. 창이 열리는 차례는 묻지 않는다(오는 대로).
        /// </summary>
        IEnumerator Peek()
        {
            StartPeekGame(flow, Seed, AiSeed);
            yield return Frames(6);
            flow.Skip(flow.OpeningMs);
            bool breachDone = false, blessDone = false;
            float start = Time.realtimeSinceStartup;
            while ((!breachDone || !blessDone) && Time.realtimeSinceStartup - start < 90f && !finished)
            {
                if (!breachDone && flow.Top is BreachScreen breach && flow.BoardOpen && !breach.Folded && Laid(Q("board-breach-go")))
                {
                    yield return Frames(3);
                    yield return PeekOver("peek-breach", "균열 카드 창", breach, "82-peek-breach");
                    yield return Click(Px(Q("board-breach-go")));
                    yield return Wait(() => !(flow.Top is BreachScreen), 10f);
                    breachDone = true;
                }
                else if (!blessDone && flow.Top is BlessScreen ask && !ask.IsPanel && flow.BoardOpen && Laid(Q("board-bless-no")))
                {
                    yield return Frames(3);
                    yield return PeekOver("peek-bless", "축복 물음", ask, "80-peek-bless");
                    yield return FoldDrag(ask);
                    if (Laid(Q("board-bless-no"))) yield return Click(Px(Q("board-bless-no")));
                    yield return Wait(() => !(flow.Top is BlessScreen), 10f);
                    blessDone = true;
                }
                else if (flow.Top == null && flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null && flow.Hud != null && flow.Hud.Shows(BoardHud.EndTurn) && Laid(Q("board-hud-act-" + BoardHud.EndTurn)))
                    yield return Click(Px(Q("board-hud-act-" + BoardHud.EndTurn)));
                else
                {
                    flow.Skip(300);
                    yield return null;
                }
            }
            Check("물음 창 위 정보 창", breachDone, "균열 카드 창(붙이기)이 열리지 않았다");
            Check("물음 창 위 정보 창", blessDone, "라운드 끝 축복 물음(UB 턴 건너뛰기)이 열리지 않았다");
        }

        // 물음 창(under)이 떠 있는 동안 [메뉴] · [상성] · [버림 더미] · [정비] · [도감]을 차례로 누르고 Esc로 닫는다 — 열린 층 · 아래 창 그대로 · 답 없음
        IEnumerator PeekOver(string step, string label, object under, string snap)
        {
            int commands = flow.Session.CommandCount;
            bool Under() => ReferenceEquals(stage.Current, under);

            yield return Click(Px(Q("board-hud-menu")));
            yield return Wait(() => flow.MenuUp, 5f);
            bool menu = flow.MenuUp && stage.Info is BoardMenuScreen && Under();
            yield return Snap(snap + "-menu");
            yield return Tap(Key.Escape);
            yield return Wait(() => !flow.MenuUp, 5f);
            bool menuBack = !flow.MenuUp && stage.Info == null && Under();

            yield return Click(Px(Q("board-hud-types")));
            yield return Wait(() => stage.Info is TypeChartScreen, 5f);
            bool types = stage.Info is TypeChartScreen && Under();
            yield return Tap(Key.Escape);
            yield return Wait(() => stage.Info == null, 5f);
            bool typesBack = stage.Info == null && Under();

            // 버림 더미(결정 196): 상성 창과 같은 층
            yield return Click(Px(Q("board-hud-discards")));
            yield return Wait(() => stage.Info is DiscardScreen, 5f);
            bool discards = stage.Info is DiscardScreen && Under();
            yield return Tap(Key.Escape);
            yield return Wait(() => stage.Info == null, 5f);
            bool discardsBack = stage.Info == null && Under();

            yield return Click(Px(Q("board-hud-maint")));
            yield return Wait(() => stage.Info is PartyScreen, 5f);
            bool party = stage.Info is PartyScreen p && p.Lock == "screen.peek.lock.window" && Under();
            yield return Snap(snap + "-party");
            yield return Tap(Key.Escape);
            yield return Wait(() => stage.Info == null, 5f);
            bool partyBack = stage.Info == null && Under();

            yield return Click(Px(Q("board-hud-dex")));
            yield return Wait(() => stage.Info is DexScreen, 5f);
            bool dex = stage.Info is DexScreen && Under();
            yield return Tap(Key.Escape);
            yield return Wait(() => stage.Info == null, 5f);
            bool dexBack = stage.Info == null && Under();

            bool quiet = flow.Session.CommandCount == commands;
            bool ok = menu && menuBack && types && typesBack && discards && discardsBack && party && partyBack && dex && dexBack && quiet;
            Step(step, "ok", ok, "menu", menu, "menuBack", menuBack, "types", types, "typesBack", typesBack, "discards", discards, "discardsBack", discardsBack, "party", party, "partyBack", partyBack, "dex", dex, "dexBack", dexBack,
                "commandsBefore", commands, "commandsAfter", flow.Session.CommandCount, "screen", stage.Current != null ? stage.Current.Name : "");
            Check(label + " 위 정보 창", ok, "[메뉴] · [상성] · [정비](보기만) · [도감]이 정보 창 층에 열리고 Esc 뒤 " + label + "이 그대로여야 한다(메뉴 " + menu + "/" + menuBack + " · 상성 " + types + "/" + typesBack
                + " · 정비 " + party + "/" + partyBack + " · 도감 " + dex + "/" + dexBack + " · 답 없음 " + quiet + ")");
        }

        // 축복 물음 [접기] → 보드 가운데에서 끌기(보는 점이 옮겨지고 칸은 눌리지 않는다) → [펼치기]
        IEnumerator FoldDrag(BlessScreen ask)
        {
            yield return Click(Px(Q("board-bless-fold")));
            yield return Frames(3);
            bool folded = ask.Folded && flow.Folded;
            int w = root.Size.x, h = root.Size.y;
            var from = new Vector2(w * 0.5f, h * 0.5f);
            var to = from + new Vector2(w * 0.12f, h * 0.08f);
            bool clear = !flow.OverUi(from.x, from.y);
            var was = rig.Target;
            int commands = flow.Session.CommandCount;
            yield return Drag(from, to);
            float moved = Vector3.Distance(rig.Target, was);
            yield return Snap("81-peek-bless-fold-drag");
            Queue(at, false);
            yield return Frames(3);
            bool quiet = flow.Session.CommandCount == commands && ReferenceEquals(flow.Top, ask);
            if (folded) yield return Click(Px(Q("board-bless-fold")));
            yield return Frames(3);
            bool unfolded = !ask.Folded && !flow.Folded && Laid(Q("board-bless-no"));
            bool ok = folded && clear && moved > 0.5f && quiet && unfolded;
            Step("peek-bless-fold-drag", "ok", ok, "folded", folded, "startClear", clear, "targetMoved", moved, "quiet", quiet, "unfolded", unfolded, "from", from, "to", to);
            Check("접은 축복 물음 아래 끌기", ok, "[접기] 뒤 보드 가운데를 끌면 보는 점이 옮겨지고(" + moved.ToString("0.###", inv) + ") 칸 · 답이 나가지 않으며 [펼치기]로 답 단추가 돌아와야 한다(접힘 " + folded
                + " · 시작점이 판 밖 " + clear + " · 조용 " + quiet + " · 펼침 " + unfolded + ")");
        }

        /// <summary>
        /// 각본 튜토리얼(M6.5 마감 — 07 §2 · §9): 타이틀 [튜토리얼] → (기록이 없으면 본편 0장이 바로 열린다 — [튜토리얼 끝내기] → [목차로]) → 목차의 확장 장 E2 →
        /// 처음 뜬 말풍선 = 장 첫 말풍선(판 차리기 뒤로 옮김, 07 T160) → [다음] → 다음 말풍선 → [튜토리얼 끝내기] → 끝내기 창 [목차로] → 목차 [뒤로] → 타이틀.
        /// 확인용 플레이어의 튜토리얼 기록은 실행 동안만이다(<see cref="ScreenBoot.DefaultTutorialFolder"/> — 사용자 기록을 바꾸지 않는다).
        /// </summary>
        IEnumerator Tutorial()
        {
            var tut = flow.Tutorial;
            flow.OpenTitle(TitleScreen.Tutorial);
            yield return Frames(3);
            yield return Click(Px(Q("title-item-" + TitleScreen.Tutorial)));
            yield return Frames(4);
            bool firstTime = tut.Running;
            if (firstTime)
            {
                yield return Wait(() => tut.Layer != null && tut.Layer.Shown && Laid(Q("tut-layer-quit")), 15f);
                yield return Snap("70-tut-first-time");
                yield return Click(Px(Q("tut-layer-quit")));
                yield return Wait(() => Laid(Q("tut-quit-menu")), 6f);
                yield return Click(Px(Q("tut-quit-menu")));
                yield return Frames(4);
            }
            bool menu = stage.Current is TutorialMenuScreen && !tut.Running && Laid(Q("tut-menu-ch-e02"));
            Step("tut-click-title", "ok", menu, "at", at, "firstTime", firstTime, "screen", stage.Current != null ? stage.Current.Name : "");
            yield return Snap("71-tut-menu");
            if (!Check("튜토리얼 목차", menu, "타이틀 [튜토리얼] 뒤 목차가 열리지 않았다")) yield break;
            yield return Click(Px(Q("tut-menu-ch-e02")));
            yield return Wait(() => tut.Layer != null && tut.Layer.Shown && Laid(Q("tut-layer-next")), 20f);
            yield return Frames(3);
            var first = tut.Layer != null && tut.Layer.Shown ? tut.Runner.Step : null;
            string firstText = tut.Layer != null ? tut.Layer.ShownText ?? "" : "";
            bool opened = tut.Script != null && tut.Script.Id == "e02" && first != null && first.Id == "S001" && firstText == stage.T("tutorial.e02.S001");
            Step("tut-click-chapter", "ok", opened, "at", at, "script", tut.Script != null ? tut.Script.Id : "", "shown", first != null ? first.Id : "", "text", firstText);
            yield return Snap("72-tut-first-bubble");
            if (!Check("튜토리얼 장 고르기", opened, "목차에서 E2를 눌렀는데 장 첫 말풍선(S001)이 뜨지 않았다")) yield break;
            yield return Click(Px(Q("tut-layer-next")));
            yield return Wait(() => tut.Layer != null && tut.Layer.Shown && tut.Runner.Step != first, 10f);
            yield return Frames(3);
            var second = tut.Layer != null && tut.Layer.Shown ? tut.Runner.Step : null;
            bool advanced = second != null && second.Id == "S002";
            Step("tut-click-next", "ok", advanced, "at", at, "shown", second != null ? second.Id : "", "holes", tut.Layer != null ? tut.Layer.Holes.Count : -1);
            yield return Snap("73-tut-next");
            if (!Check("튜토리얼 [다음]", advanced, "말풍선 [다음]을 눌렀는데 다음 단계(S002)로 넘어가지 않았다")) yield break;
            yield return Click(Px(Q("tut-layer-quit")));
            yield return Wait(() => tut.Quitting && Laid(Q("tut-quit-menu")), 6f);
            bool asking = tut.Quitting && Laid(Q("tut-quit-menu"));
            Step("tut-click-quit", "ok", asking, "at", at);
            yield return Snap("74-tut-quit");
            if (!Check("튜토리얼 끝내기", asking, "[튜토리얼 끝내기]를 눌렀는데 끝내기 창이 뜨지 않았다")) yield break;
            yield return Click(Px(Q("tut-quit-menu")));
            yield return Frames(4);
            bool back = stage.Current is TutorialMenuScreen && !tut.Running && Laid(Q("tut-menu-back"));
            Step("tut-click-to-menu", "ok", back, "at", at, "running", tut.Running);
            if (!Check("튜토리얼 [목차로]", back, "끝내기 창 [목차로] 뒤 목차로 돌아오지 않았다(판이 남았거나 목차가 없다)")) yield break;
            yield return Click(Px(Q("tut-menu-back")));
            yield return Frames(4);
            bool title = stage.Current is TitleScreen;
            Step("tut-click-back", "ok", title, "at", at, "screen", stage.Current != null ? stage.Current.Name : "");
            Check("튜토리얼 목차 [뒤로]", title, "목차 [뒤로]를 눌렀는데 타이틀로 돌아오지 않았다");
        }

        IEnumerator Left()
        {
            var ctx = flow.Ctx;
            int origin = ctx.Cards.PokemonIds.IndexOf("483:2"), acrobatics = ctx.Cards.MoveIds.IndexOf("acrobatics");
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, 1, Seed, AiSeed, s =>
            {
                var st = s.Seats[0];
                int starter = st.Party[0];
                st.OpenSlots = st.Party.Length;
                st.Party[0] = Sinnoh.Rules.PokemonRules.Spawn(s, ctx.Cards.PokemonIds.IndexOf("483:1"), 0, (int)Sinnoh.Rules.DeckId.Legendary);
                st.Party[1] = Sinnoh.Rules.PokemonRules.Spawn(s, ctx.Cards.PokemonIds.IndexOf("396:0"), 0, (int)Sinnoh.Rules.DeckId.Legendary);
                st.Party[2] = starter;
                s.Pokemon[st.Party[0]].HeldItem = ctx.Items.Ids.IndexOf("item.adamant_artifact");
                s.Pokemon[st.Party[1]].Moves[0] = acrobatics;
            });
            yield return Frames(6);
            flow.Skip(flow.OpeningMs);
            yield return Wait(() => flow.InputOpen && flow.Hud != null && Laid(Q("board-hud-maint")), 20f);
            int lead = flow.Client.View.Seats[flow.Client.Seat].Party[0], bird = flow.Client.View.Seats[flow.Client.Seat].Party[1];
            yield return Click(Px(Q("board-hud-maint")));
            yield return Wait(() => stage.Current is PartyScreen && Laid(Q("board-party-form-0")), 10f);
            bool row = stage.Current is PartyScreen && Q("board-party-form-0") != null && Q("board-party-form-1") == null;
            if (row) yield return Click(Px(Q("board-party-form-0")));
            yield return Wait(() => Laid(Q("board-party-ask-yes")), 10f);
            bool asked = stage.Current is PartyScreen asking && asking.Asking;
            if (asked) yield return Snap("55-form-ask");
            if (asked) yield return Click(Px(Q("board-party-ask-yes")));
            yield return Wait(() => flow.Client.View.Pokemon[lead].Def == origin, 10f);
            bool changed = flow.Client.View.Pokemon[lead].Def == origin;
            yield return Frames(3);
            if (Laid(Q("board-party-close"))) yield return Click(Px(Q("board-party-close")));
            yield return Wait(() => stage.Current == null && flow.InputOpen && Laid(Q("board-hud-act-" + BoardHud.Challenge)), 10f);
            bool closed = stage.Current == null;
            Step("form-click-change", "ok", row && asked && changed && closed, "at", at, "row", row, "asked", asked, "changed", changed, "closed", closed);
            if (!Check("폼체인지", row && asked && changed && closed, "정비 판의 [폼체인지] → 물음 줄 [폼체인지]로 디아루가가 오리진폼이 되어야 한다")) yield break;

            yield return Click(Px(Q("board-hud-act-" + BoardHud.Challenge)));
            yield return Wait(() => stage.Current is BattleScreen, 15f);
            var battle = stage.Current as BattleScreen;
            float t0 = Time.realtimeSinceStartup;
            // 첫 출전(디아루가) · 우선권 토큰을 마우스로 넘기며 [대기 포켓몬 기술]이 설 때까지
            while (battle != null && Time.realtimeSinceStartup - t0 < 40f && !(battle.Open && battle.PartyMovesOn && Laid(Q("battle-partymoves"))))
            {
                if (battle.Open && battle.Bench.Count > 0 && Laid(Q("battle-bench-1"))) yield return Click(Px(Q("battle-bench-1")));
                else if (battle.Open && battle.AskingToken && Laid(Q("battle-token-use"))) yield return Click(Px(Q("battle-token-use")));
                yield return Frames(3);
            }
            bool button = battle != null && battle.Open && battle.PartyMovesOn && Laid(Q("battle-partymoves"));
            if (button) yield return Click(Px(Q("battle-partymoves")));
            yield return Wait(() => battle != null && battle.PartyOpen && Laid(Q("battle-partymove-1")), 10f);
            bool rows = battle != null && battle.PartyOpen && battle.PartyMoves.Count == 1;
            if (rows) yield return Snap("56-partymove");
            int commands = flow.Session.CommandCount;
            if (rows) yield return Click(Px(Q("battle-partymove-1")));
            yield return Wait(() => flow.Battle != null && flow.Battle.Events.Exists(e => e is Sinnoh.Rules.MoveUsed m && m.MoveKey == acrobatics), 10f);
            bool used = flow.Battle != null && flow.Battle.Events.Exists(e => e is Sinnoh.Rules.MoveUsed m && m.MoveKey == acrobatics && m.PokemonId == bird) && flow.Session.CommandCount > commands;
            Step("partymove-click-use", "ok", button && rows && used, "at", at, "button", button, "rows", rows, "used", used);
            Check("대기 포켓몬 기술", button && rows && used, "[대기 포켓몬 기술] → 그 줄을 누르면 찌르꼬가 나오며 애크러뱃을 써야 한다");
        }

        IEnumerator Stacks()
        {
            const int seats = 3;
            flow.StartSeats(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, seats, Seed, AiSeed, state =>
            {
                for (int i = 1; i < seats; i++) state.Seats[i].Cell = state.Seats[0].Cell;
            });
            yield return Frames(6);
            yield return Wait(() => flow.Fx != null && flow.Fx.Sequencer.IsEmpty, 10f);
            yield return Frames(4);
            var hud = flow.Hud;
            var pieces = flow.BoardPieces;
            int cell = flow.Client.View.Seats[0].Cell, shown = 0;
            if (pieces != null) foreach (var p in pieces.Pieces) if (p.Root != null && p.Root.activeSelf) shown++;
            Rect rect = default;
            bool badge = hud != null && hud.Stacks != null && hud.Stacks.Count == 1 && hud.Stacks.Total(cell) == seats && hud.Stacks.RectOf(cell, out rect);
            bool stood = pieces != null && pieces.Pieces.Count == seats && shown == 1 && pieces.StackAt(cell) != null && pieces.StackAt(cell).Stand == 0;
            if (!Check("묶음 표지", badge && stood, "말 셋을 한 칸에 모았는데 내 말 하나만 서고 표지 「×3」이 뜨지 않았다(선 말 " + shown + " · 표지 " + (hud != null && hud.Stacks != null ? hud.Stacks.Count : -1) + ")")) { Finish(1, head.ToString()); yield break; }
            float scale = stage.Grade.PanelScale;
            var px = new Vector2(rect.center.x * scale, root.Size.y - rect.center.y * scale);
            yield return Move(px);
            yield return Frames(3);
            bool card = hud.Info.Subject == "cell-" + cell && hud.Info.Element.Q("info-card-seat-" + seats) != null && hud.Info.Element.Q("info-card-seat-1") != null;
            bool free = !flow.OverUi(px.x, px.y) && rig.WheelEnabled;
            var zoomWas = rig.CurrentZoom;
            yield return Wheel(1f);
            bool wheel = rig.CurrentZoom != zoomWas;
            yield return Frames(3);
            bool follows = hud.Stacks.RectOf(cell, out var after) && after != rect;
            yield return Snap("24-stack-badge-hover");
            Step("stack-badge-hover", "ok", card && free && wheel && follows, "at", px, "cell", cell, "subject", hud.Info.Subject ?? "", "badge", rect.width, "wheel", wheel, "zoom", rig.CurrentZoom.ToString());
            Check("묶음 표지", card, "표지에 마우스를 올렸는데 그 칸의 좌석 셋이 든 공개 정보 카드가 뜨지 않았다(대상 " + (hud.Info.Subject ?? "없음") + ")");
            Check("묶음 표지", free && wheel, "표지 위에서 휠이 듣지 않는다(표지가 마우스를 막는다)");
            Check("묶음 표지", follows, "줌을 바꿨는데 표지가 말 머리 위로 따라오지 않았다");
        }

        /// <summary>[턴 종료]를 눌러 다음 라운드의 내 턴(입력 열림)까지 간다(솔로 — 넘김 박자는 ⑨가 본다).</summary>
        IEnumerator NextRound(string when)
        {
            var client = flow.Client;
            int round = client.View.Round;
            if (!Check("다음 라운드", flow.InputOpen && flow.Hud.Shows(BoardHud.EndTurn), when + ": [턴 종료] 단추를 누를 수 없다")) { Finish(1, head.ToString()); yield break; }
            yield return Click(Px(Q("board-hud-act-" + BoardHud.EndTurn)));
            yield return Wait(() => flow.InputOpen && client.View.Round == round + 1, 15f);
            yield return Frames(3);
            bool next = flow.InputOpen && client.View.Round == round + 1;
            Step("next-round", "ok", next, "when", when, "round", client.View.Round, "cell", client.View.Seats[client.Seat].Cell, "movesLeft", client.View.Seats[client.Seat].MovesLeft);
            if (!Check("다음 라운드", next, when + ": [턴 종료] 뒤 다음 라운드의 내 턴에 입력이 열리지 않았다(라운드 " + client.View.Round + ")")) Finish(1, head.ToString());
        }

        IEnumerator PassHiddenSpot(string when)
        {
            var choice = flow.Client.FirstChoice();
            if (choice == null || choice.Kind != ChoiceKind.HiddenSpot) yield break;
            int commands = flow.Session.CommandCount;
            yield return Frames(2);
            yield return Click(Px(Q("board-hud-act-" + BoardHud.Pass)));
            bool passed = flow.Session.CommandCount == commands + 1;
            Step("hidden-spot-pass", "ok", passed, "when", when, "at", at);
            Check("숨겨진 장소", passed, when + ": [지나가기]를 눌렀는데 답이 나가지 않았다");
            yield return Wait(() => flow.InputOpen, 15f);
            yield return Frames(2);
        }

        void Finish(int code, string head)
        {
            if (finished) return;
            finished = true;
            foreach (string e in errors) fails.Add("에러 로그: " + e);
            if (code < 0) code = fails.Count == 0 ? 0 : 1;
            var sb = new StringBuilder("{\"schema\":1");
            if (head.Length > 0) sb.Append(',').Append(head);
            sb.Append(",\"seconds\":").Append((Time.realtimeSinceStartup - t0).ToString("0.#", inv));
            sb.Append(",\"steps\":[").Append(steps).Append(']');
            sb.Append(",\"ignoredErrorLogs\":[");
            for (int i = 0; i < ignored.Count; i++) sb.Append(i > 0 ? "," : "").Append('"').Append(Esc(ignored[i])).Append('"');
            sb.Append(']');
            sb.Append(",\"errorLogs\":").Append(errors.Count).Append(",\"ok\":").Append(fails.Count == 0 ? "true" : "false").Append(",\"fails\":[");
            for (int i = 0; i < fails.Count; i++) sb.Append(i > 0 ? "," : "").Append('"').Append(Esc(fails[i])).Append('"');
            sb.Append("]}\n");
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "mouse-probe.json"), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogException(e); }
            Debug.Log("[MouseProbe] 끝 — 종료 코드 " + code + (fails.Count > 0 ? " (" + string.Join("; ", fails) + ")" : "") + " → " + outDir);
            Application.Quit(code);
        }
    }
}
