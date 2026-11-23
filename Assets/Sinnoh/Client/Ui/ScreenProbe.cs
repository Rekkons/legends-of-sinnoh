using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Rekkon.UiKit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 창 없는 플레이어에서 화면 뿌리를 확인한다(plan §4 「화면 뿌리·타이틀」). 명령줄 <c>-sinnohScreenProbe &lt;폴더&gt;</c>로 띄운 플레이어에서만 켜진다 —
    /// <see cref="FrameProbe"/>처럼 <c>-batchmode</c>(그래픽 장치는 켬)로 띄운다. 보는 것:
    /// 켜면 타이틀이 열리는가, 실행용 테마가 걸렸는가, 글꼴·창틀을 번들에서 읽었는가, 코드가 <c>Shader.Find</c>로 찾는 셰이더가 빌드에 들었는가, 유휴 프레임 제한,
    /// 그리고 크기 일곱(등급 넷 + 1600×900 + 3440×1440 + 16:10인 1920×1200)에서 등급이 다시 골라지고 패널이 그 크기를 덮는가, 보드가 메뉴 판 밑으로 들어가지 않는가(<see cref="TitleScreen.BoardShiftPx"/>).
    /// 창이 없어 화면에 내지 못하므로 크기마다 패널을 RT에 그리고(<see cref="ScreenRoot.SetTarget"/>) 같은 크기로 그린 보드 위에 겹쳐 PNG로 남긴다 —
    /// 실제 창에서 패널이 카메라 위에 겹쳐지는 것과 마우스·키보드 장치의 길은 여기서 보지 못한다(<see cref="MouseProbe"/>가 본다).
    /// 이어서 화면 흐름을 끝까지 간다(plan §4 「솔로 설정·셋업」): 혼자 하기(S09) → 고정 시드로 게임 시작 → 고향 마을(S12) → 스타팅 포켓몬·트레이너 카드(S12·S13) → 보드.
    /// 화면마다 번들에서 읽는 그림(마을 카드·정지 도트·직업 그림·체력 아이콘)이 실렸는지, 셋업 뒤 보드에 내 말 하나가 서고 보드 입력이 켜지는지 본다.
    /// 보드에서는 턴을 돈다(plan §4 「보드 화면」): 보드 HUD가 뜨고 내 턴 컷신 뒤 입력이 열리는가 → 갈 수 있는 칸을 화면 좌표로 눌러 걷는가(말이 도착 칸에) →
    /// 게임 메뉴가 열리고 닫히는가 → [턴 종료] 뒤 다음 라운드의 내 턴에 입력이 다시 열리는가.
    /// 출력: &lt;폴더&gt;/screen-probe.json, title-&lt;폭&gt;x&lt;높이&gt;.png, flow-&lt;화면&gt;.png. 종료 코드 0 = 모두 맞음 · 1 = 어긋남 · 2 = 시간 초과.
    /// </summary>
    public sealed class ScreenProbe : MonoBehaviour
    {
        /// <summary>첫 판(타이틀 → 보드)의 주인공: 기본값이 아닌 5세대 주인공 — 고른 주인공이 보드 말에 닿는지와 플레이어가 필드 도트를 번들에서 읽는지를 본다.</summary>
        public const string ProbeProtagonist = "bw_001_hilda";

        public const string Arg = "-sinnohScreenProbe";

        /// <summary><c>Shader.Find</c>로 찾는 셰이더(<see cref="DiceBoard"/> 넷 · <see cref="DsStage"/> 하나).</summary>
        public static readonly string[] FoundShaders = { "Sinnoh/DiceLit", "Sinnoh/DiceFlat", "Sinnoh/DiceGlitch", "Sinnoh/DiceDot", DsStage.ShaderName, DsStage.WarpShaderName };

        /// <summary>
        /// UI Toolkit 필터(USS <c>filter</c> — drop-shadow · blur = 카드 블럭 그림자 결정 206, grayscale 같은 색 효과 = ColorEffect)가 쓰는 기본 셰이더. 씬이 가리키지 않아 플레이어 빌드에서 빠지면 그 요소를 그릴 때마다 예외가 나 화면이 서지 않는다(11-23 0.2.2 감사) —
        /// <c>BoardBuild.EnsureFilterShaders</c>가 Always Included Shaders에 넣는다.
        /// </summary>
        public static readonly string[] FilterShaders = { "Hidden/UIR/GaussianBlur", "Hidden/UIR/DropShadowComposite", "Hidden/UIR/ColorEffect" };

        static readonly int[][] Sizes =
        {
            new[] { 1280, 720 }, new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 }, new[] { 1600, 900 }, new[] { 3440, 1440 }, new[] { 1920, 1200 },
        };

        const float TimeoutSeconds = 180f;

        string outDir;
        readonly List<string> errors = new List<string>();
        readonly List<string> fails = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == Arg)
                {
                    new GameObject("ScreenProbe").AddComponent<ScreenProbe>().outDir = args[i + 1];
                    return;
                }
        }

        void Awake() => Application.logMessageReceived += Grab;

        void OnDestroy() => Application.logMessageReceived -= Grab;

        void Grab(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + message);
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            float t0 = Time.realtimeSinceStartup;
            var sb = new StringBuilder("{\"schema\":1");
            ScreenBoot boot = null;
            // 켜면 타이틀이 열리는가: 화면 뿌리가 패널 뿌리를 받아 화면을 얹을 때까지 기다린다
            while (Time.realtimeSinceStartup - t0 < TimeoutSeconds)
            {
                boot = FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null && boot.Root.Stage.Root.Q("title") != null) break;
                yield return null;
            }
            var root = boot != null ? boot.Root : null;
            var title = root != null && root.Stage.Root != null ? root.Stage.Root.Q("title") : null;
            if (title == null)
            {
                fails.Add("타이틀이 열리지 않았다(ScreenBoot " + (boot != null) + ", 화면 뿌리 " + (root != null) + ")");
                Finish(sb, 2);
                yield break;
            }
            for (int i = 0; i < 5; i++) yield return null;
            sound = boot.Sound;
            NoteMusic("title", Sinnoh.Fx.MusicScene.Title);

            var stage = root.Stage;
            sb.Append(",\"startup\":{\"screen\":\"").Append(stage.Current != null ? stage.Current.Name : "").Append('"')
              .Append(",\"screenSize\":[").Append(Screen.width).Append(',').Append(Screen.height).Append(']')
              .Append(",\"grade\":").Append(stage.Grade.Height)
              .Append(",\"theme\":\"").Append(root.Theme != null ? root.Theme.name : "").Append('"')
              .Append(",\"font\":").Append(stage.Skin.Font != null ? "true" : "false")
              .Append(",\"busy\":").Append(stage.Tweens.Busy)
              .Append(",\"idle\":").Append(root.Idle ? "true" : "false")
              .Append(",\"targetFrameRate\":").Append(Application.targetFrameRate)
              .Append(",\"vSyncCount\":").Append(QualitySettings.vSyncCount).Append('}');
            if (stage.Current == null || stage.Current.Name != "title") fails.Add("열린 화면이 타이틀이 아니다");
            if (root.Theme == null) fails.Add("실행용 테마가 걸리지 않았다(null)");
            if (stage.Tweens.Busy != 0 || !root.Idle || Application.targetFrameRate != ScreenRoot.IdleFps) fails.Add("타이틀에서 유휴 프레임 제한이 아니다");

            sb.Append(",\"shaders\":{");
            var probed = FoundShaders.Concat(FilterShaders).ToArray();
            for (int i = 0; i < probed.Length; i++)
            {
                var s = Shader.Find(probed[i]);
                bool ok = s != null && s.isSupported;
                if (!ok) fails.Add("셰이더 없음: " + probed[i]);
                sb.Append(i > 0 ? "," : "").Append('"').Append(probed[i]).Append("\":").Append(ok ? "true" : "false");
            }
            sb.Append('}');

            var cam = Camera.main;
            Directory.CreateDirectory(outDir);
            sb.Append(",\"sizes\":[");
            for (int i = 0; i < Sizes.Length; i++)
            {
                int w = Sizes[i][0], h = Sizes[i][1];
                var want = UiGrade.For(w, h);
                var uiRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                uiRt.Create();
                root.SetTarget(uiRt);
                for (int f = 0; f < 6; f++) yield return null;

                string png = "title-" + w + "x" + h + ".png";
                Snap(cam, uiRt, png, out int covered, out int boardShown, out int boardLeft);

                var layout = stage.Root.layout;
                var menu = stage.Root.Q("title-menu").worldBound;
                var foot = stage.Root.Q("title-foot").worldBound;
                var label = stage.Root.Q("title-item-" + TitleScreen.Solo).Q<Label>(className: Kit.F3);
                float textWidth = label != null ? label.layout.width : 0f;
                bool gradeOk = stage.Grade.Equals(want);
                bool sizeOk = Mathf.Approximately(layout.width, (float)w / want.PanelScale) && Mathf.Approximately(layout.height, (float)h / want.PanelScale);
                bool footOk = Mathf.Approximately(foot.yMax, layout.height) && Mathf.Approximately(foot.width, layout.width);
                if (!gradeOk) fails.Add($"{w}x{h}: 등급 {stage.Grade} ≠ {want}");
                if (!sizeOk) fails.Add($"{w}x{h}: 뿌리 {layout.width}x{layout.height} ≠ 패널 {(float)w / want.PanelScale}x{(float)h / want.PanelScale}");
                if (!footOk) fails.Add($"{w}x{h}: 아래 띠가 패널 아래·폭에 붙지 않았다({foot})");
                if (covered == 0) fails.Add($"{w}x{h}: 패널이 아무것도 그리지 않았다");
                if (boardShown == 0) fails.Add($"{w}x{h}: 보드가 보이지 않는다");
                if (boardLeft < menu.xMax * want.PanelScale) fails.Add($"{w}x{h}: 보드 왼쪽 끝 {boardLeft} < 메뉴 판 오른쪽 변 {menu.xMax * want.PanelScale}");
                if (textWidth <= 0f) fails.Add($"{w}x{h}: 메뉴 글자 폭 0(글꼴)");
                var inv = CultureInfo.InvariantCulture;
                sb.Append(i > 0 ? "," : "").Append("{\"size\":[").Append(w).Append(',').Append(h).Append(']')
                  .Append(",\"grade\":").Append(stage.Grade.Height).Append(",\"tokens\":").Append(stage.Grade.TokenHeight).Append(",\"scale\":").Append(stage.Grade.PanelScale)
                  .Append(",\"root\":[").Append(layout.width.ToString("0.##", inv)).Append(',').Append(layout.height.ToString("0.##", inv)).Append(']')
                  .Append(",\"menu\":[").Append(menu.x.ToString("0.##", inv)).Append(',').Append(menu.y.ToString("0.##", inv)).Append(',').Append(menu.width.ToString("0.##", inv)).Append(',').Append(menu.height.ToString("0.##", inv)).Append(']')
                  .Append(",\"footBottom\":").Append(foot.yMax.ToString("0.##", inv))
                  .Append(",\"menuTextWidth\":").Append(textWidth.ToString("0.##", inv))
                  .Append(",\"uiPixels\":").Append(covered).Append(",\"boardPixels\":").Append(boardShown).Append(",\"boardLeft\":").Append(boardLeft)
                  .Append(",\"png\":\"").Append(png).Append("\"}");

                root.SetTarget(null);
                uiRt.Release();
                Destroy(uiRt);
                if (Time.realtimeSinceStartup - t0 > TimeoutSeconds) { fails.Add("시간 초과"); sb.Append(']'); Finish(sb, 2); yield break; }
            }
            sb.Append(']');

            // 화면 흐름: 혼자 하기 → 게임 준비 → 보드(1920×1080)
            var flow = boot.Flow;
            if (flow != null)
            {
                // 단서 시각 로그(plan §4 「연출 단서에 소리 달기」): 이 확인이 지나는 판들의 연출 소리 — 건너뛴 연출도 낸다(시각은 시퀀서가 센 값이라 건너뛰어도 같다)
                flow.SoundOnSkip = true;
                flow.Sound += LogCue;
                cueFlow = flow;
            }
            boot.PointerFromMouse = false;   // 보드의 칸은 여기서 화면 좌표로 넣는다(패널을 RT에 그려 창의 마우스 좌표와 크기가 다르다)
            var flowRt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            flowRt.Create();
            root.SetTarget(flowRt);
            sb.Append(",\"flow\":{\"ctx\":").Append(flow != null && flow.Ctx != null ? "true" : "false");
            if (flow == null || flow.Ctx == null) fails.Add("화면 흐름·게임 데이터가 없다(씬이 덱·효과 표를 가리키나)");
            else
            {
                var rig = cam.GetComponent<BoardCameraRig>();
                var boardBoot = FindAnyObjectByType<BoardBoot>();
                if (rig.InputEnabled) fails.Add("타이틀에서 보드 입력이 켜져 있다");
                if (!((TitleScreen)stage.Current).Ready(TitleScreen.Items[3])) fails.Add("타이틀 「혼자 하기」가 꺼져 있다");

                flow.OpenSolo();
                for (int f = 0; f < 6; f++) yield return null;
                Snap(cam, flowRt, "flow-solo.png", out _, out _, out int soloBoardLeft);
                var soloPanel = stage.Root.Q("solo-panel");
                if (soloPanel == null) fails.Add("솔로 설정이 열리지 않았다");
                else if (soloBoardLeft < soloPanel.worldBound.xMax * stage.Grade.PanelScale) fails.Add($"솔로 설정: 보드 왼쪽 끝 {soloBoardLeft} < 판 오른쪽 변 {soloPanel.worldBound.xMax}");
                if (rig.InputEnabled) fails.Add("솔로 설정에서 보드 입력이 켜져 있다");

                flow.StartSolo(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, 20261112, 7);
                for (int f = 0; f < 6; f++) yield return null;
                var village = stage.Current as SetupVillageScreen;
                bool villageArt = false, monArt = MonArt.Load() != null;
                if (village == null) fails.Add("고향 마을 고르기가 열리지 않았다");
                else
                {
                    village.Pick(0);
                    for (int f = 0; f < 4; f++) yield return null;
                    var art = stage.Root.Q("setup-village-art");
                    villageArt = art != null && art.resolvedStyle.backgroundImage.texture != null;
                    Snap(cam, flowRt, "flow-setup-village.png", out _, out _, out _);
                    if (!villageArt) fails.Add("마을 카드 그림을 읽지 못했다(cards 번들)");
                    if (!monArt) fails.Add("포켓몬 정지 도트를 읽지 못했다(sprites 번들)");
                    if (boardBoot.Pieces.Pieces.Count != 0) fails.Add("셋업 중인데 시험 판 말이 남아 있다");
                    if (!village.Confirm()) fails.Add("고향 마을 확정이 거절됐다");
                    // 고향 마을 연출(04 §8-8 2행, 결정 66 ③): 마을 화면이 걷히고 좌석 띠가 뜨며 카메라가 마을로 다가가고 말이 떨어져 내려앉는다 → 끝나면 고르는 화면에 카드가 들어온다(1행)
                    for (int f = 0; f < 3; f++) yield return null;
                    var drop = stage.Current as SetupDropScreen;
                    if (drop == null) fails.Add("고향 마을 연출의 좌석 띠가 뜨지 않았다");
                    yield return Wait(() => drop != null && drop.Coloured, 4f);
                    for (int f = 0; f < 3; f++) yield return null;
                    Snap(cam, flowRt, "flow-setup-drop.png", out _, out _, out _);
                    var dropped = boardBoot.Pieces.Of(0);
                    if (drop == null || !drop.Coloured || dropped == null || dropped.Root == null || !dropped.Root.activeSelf) fails.Add("고향 마을 연출: 말이 내려앉지 않았다");
                    if (rig.CurrentZoom != BoardCameraRig.Zoom.Region) fails.Add("고향 마을 연출: 카메라가 고른 마을로 다가가지 않았다");
                    yield return Wait(() => stage.Current is SetupPickScreen && flow.SetupOpen, 6f);
                }
                for (int f = 0; f < 6; f++) yield return null;
                var pick = stage.Current as SetupPickScreen;
                bool portrait = false, icon = false;
                if (pick == null) fails.Add("스타팅 포켓몬·트레이너 카드 고르기가 열리지 않았다");
                else
                {
                    pick.PickStarter(0);
                    pick.PickCard(0);
                    for (int f = 0; f < 4; f++) yield return null;
                    var face = stage.Root.Q(className: "sn-trainer-art");
                    portrait = face != null && face.resolvedStyle.backgroundImage.texture != null;
                    var pill = stage.Root.Q(className: "kit-pill-icon");
                    icon = pill != null && pill.resolvedStyle.backgroundImage.texture != null;
                    Snap(cam, flowRt, "flow-setup-pick.png", out _, out _, out _);
                    if (pick.Starters.Count != 2 || pick.Cards.Count != 3) fails.Add($"선택지: 스타팅 포켓몬 {pick.Starters.Count}·트레이너 카드 {pick.Cards.Count}");
                    if (!portrait) fails.Add("직업 그림을 읽지 못했다(trainers 번들)");
                    if (!icon) fails.Add("체력 아이콘을 읽지 못했다(cards 번들)");
                    if (!pick.Confirm()) fails.Add("셋업 확정이 거절됐다");
                    // 고른 뒤·공개(1·3행): 고르는 화면이 남아 볼이 열리고 도트가 나타난다(볼 그림 — stage 번들) → 숨겨진 장소 배치(5행): 전체 줌에서 토큰이 떨어진다
                    flow.Skip(flow.MsUntil(step => step.Row == Sinnoh.Fx.SetupRows.Starter, 600));
                    for (int f = 0; f < 3; f++) yield return null;
                    Snap(cam, flowRt, "flow-setup-starter.png", out _, out _, out _);
                    if (!(stage.Current is SetupPickScreen) || !pick.StarterSent || pick.MonShown < 1f) fails.Add("스타팅 포켓몬 공개: 고르는 화면에서 볼이 열려 도트가 나타나지 않았다");
                    var ballArt = stage.Root.Q(className: "sn-pick-ball-open");
                    if (ballArt == null || ballArt.resolvedStyle.backgroundImage.texture == null) fails.Add("볼 그림을 읽지 못했다(stage 번들)");
                    flow.Skip(flow.MsUntil(step => Sinnoh.Fx.SpotDropBeats.Spots(step) != null, 600));
                    for (int f = 0; f < 2; f++) yield return null;
                    Snap(cam, flowRt, "flow-spots-drop.png", out _, out _, out _);
                    if (boardBoot.Tokens == null || !boardBoot.Tokens.SpotsDropping) fails.Add("숨겨진 장소 배치: 토큰이 떨어지지 않는다");
                    if (rig.CurrentZoom != BoardCameraRig.Zoom.Full) fails.Add("숨겨진 장소 배치: 카메라가 전체 줌으로 물러나지 않았다");
                }
                for (int f = 0; f < 6; f++) yield return null;
                Snap(cam, flowRt, "flow-game-board.png", out int boardUi, out _, out _);
                // 게임 시작의 연출(04 §8-8 4·5행): 서막 카드가 플레이어에서 뜨나(글자가 다 박히고 목표 문장이 뜬 모습을 찍는다)
                {
                    var openingHud = flow.Hud;
                    yield return Wait(() => openingHud != null && openingHud.ActCard != null && openingHud.ActCard.Active && openingHud.ActCard.GoalShown, 6f);
                    int openingAct = openingHud != null && openingHud.ActCard != null ? openingHud.ActCard.ShownAct : -1;
                    for (int f = 0; f < 3; f++) yield return null;
                    Snap(cam, flowRt, "flow-act-card.png", out _, out _, out _);
                    if (openingAct != 0) fails.Add("게임 시작에 서막 카드가 뜨지 않았다(막 " + openingAct + ")");
                }
                int pieces = boardBoot.Pieces.Pieces.Count;
                if (!flow.OnBoard) fails.Add("셋업이 끝나지 않았다");
                if (stage.Current != null) fails.Add("셋업 뒤에도 화면이 열려 있다: " + stage.Current.Name);
                if (pieces != 1) fails.Add("보드의 말 " + pieces + "개 ≠ 좌석 1");
                // 말 = 고른 주인공의 필드 도트(trainers 번들의 Field/ — 플레이어가 표와 시트를 읽었다)
                else if (boardBoot.Pieces.Pieces[0].Style != PieceStyle.Dot || boardBoot.Pieces.Pieces[0].Protagonist != ProbeProtagonist || boardBoot.Pieces.Pieces[0].Texture == null)
                    fails.Add("보드의 말이 고른 주인공(" + ProbeProtagonist + ")의 필드 도트가 아니다");
                // 위 띠의 얼굴 상자 = 고른 주인공의 앞모습 얼굴(trainers 번들의 Trainers/Hero/ — 결정 54 ④)
                if (FaceBox.Shown(stage.Root.Q("board-hud-seat-1-face")) != ProbeProtagonist)
                    fails.Add("위 띠의 얼굴 상자가 고른 주인공(" + ProbeProtagonist + ")의 얼굴이 아니다(trainers 번들의 Trainers/Hero · trainers.json heroes)");
                if (!rig.InputEnabled) fails.Add("보드에서 보드 입력이 꺼져 있다");

                // 보드 화면: HUD → 이동 한 번 → 메뉴 → 턴 종료 → 다음 라운드
                var hud = flow.Hud;
                int reach = 0, walkedTo = -1, round = -1, movesAfter = -1, roundTwoReach = 0, spotRevealed = -1;
                bool spotFaced = false;
                bool menuOk = false;
                if (hud == null || stage.Root.Q("board-hud-dock") == null) fails.Add("보드 HUD가 뜨지 않았다");
                else
                {
                    yield return Wait(() => flow.InputOpen, 15f);
                    if (!flow.InputOpen) fails.Add("내 턴 컷신 뒤 입력이 열리지 않았다");
                    // 게임 시작에 전체 줌으로 물러났던 카메라는 첫 내 턴 컷신에서 지역 줌으로 내 말에 돌아온다
                    if (rig.CurrentZoom != BoardCameraRig.Zoom.Region) fails.Add("보드 카메라가 내 말을 보지 않는다");
                    for (int f = 0; f < 3; f++) yield return null;   // 패널 RT는 프레임 끝에 그려진다 — 다시 그린 HUD가 찍히게 기다린다
                    Snap(cam, flowRt, "flow-board-hud.png", out _, out _, out _);
                    // 보드 위에 늘 보이는 것: 토큰(board·cards 번들) · 기록 판 · 공개 정보 카드 · 기록 창
                    {
                        int homeTokenCell = flow.Client.View.Seats[flow.Client.Seat].Cell;
                        var boardTokens = boardBoot.Tokens;
                        if (boardTokens == null || boardTokens.GenMissing || boardTokens.GruntAt(homeTokenCell) == null || boardTokens.SpotRenderers.Count == 0)
                            fails.Add("보드 위 토큰(숨겨진 장소 · 조무래기)이 서지 않았다(board·cards 번들)");
                        if (hud.RecentLog.Count != BoardHud.RecentLines) fails.Add("기록 판의 줄 " + hud.RecentLog.Count + " ≠ " + BoardHud.RecentLines);
                        if (stage.Root.Q<Label>("board-hud-act") == null || stage.Root.Q<Label>("board-hud-act").text != stage.T("screen.board.act.1")) fails.Add("1막 카드 뒤에도 위 띠 막 배지가 1막이 아니다");
                        hud.ShowSeatInfo(flow.Client.Seat);
                        for (int f = 0; f < 3; f++) yield return null;
                        Snap(cam, flowRt, "flow-board-info.png", out _, out _, out _);
                        if (hud.Info.Subject == null || stage.Root.Q("info-card-mon-0") == null) fails.Add("좌석 칩의 공개 정보 카드가 뜨지 않았다");
                        hud.HideInfo();
                        flow.OpenLog();
                        for (int f = 0; f < 3; f++) yield return null;
                        Snap(cam, flowRt, "flow-board-log.png", out _, out _, out _);
                        var logProbe = stage.Current as LogScreen;
                        if (logProbe == null || logProbe.Total == 0) fails.Add("기록 창이 열리지 않았거나 비어 있다");
                        flow.CloseLog();
                        if (stage.Current != null || !flow.InputOpen) fails.Add("기록 창을 닫은 뒤 보드 입력이 열리지 않았다");
                        // 정비 판(S18) → 카드 확대(S38) → 닫기
                        if (!flow.OpenParty()) fails.Add("정비 판이 열리지 않았다");
                        for (int f = 0; f < 3; f++) yield return null;
                        Snap(cam, flowRt, "flow-board-party.png", out _, out _, out _);
                        var partyProbe = stage.Current as PartyScreen;
                        if (partyProbe == null || stage.Root.Q("board-party-mon-0") == null || stage.Root.Q("board-party-sig-0") == null) fails.Add("정비 판에 내 포켓몬 칸이 없다");
                        else if (!partyProbe.PressMon(0)) fails.Add("포켓몬 카드를 눌렀는데 카드 확대가 열리지 않았다");
                        for (int f = 0; f < 3; f++) yield return null;
                        Snap(cam, flowRt, "flow-mon-detail.png", out _, out _, out _);
                        if (!(stage.Current is MonScreen) || stage.Root.Q("mon-detail-move-0") == null) fails.Add("카드 확대에 고유 기술 줄이 없다");
                        flow.CloseMon();
                        flow.CloseParty();
                        if (stage.Current != null || !flow.InputOpen) fails.Add("정비 판을 닫은 뒤 보드 입력이 열리지 않았다");
                        // 트레이너 카드 판(S40): 직업 그림 · 능력 둘(브론즈 — 잠김)
                        if (!flow.OpenTrainer()) fails.Add("트레이너 카드 판이 열리지 않았다");
                        for (int f = 0; f < 3; f++) yield return null;
                        Snap(cam, flowRt, "flow-board-trainer.png", out _, out _, out _);
                        var art = stage.Root.Q("board-trainer-art");
                        if (!(stage.Current is TrainerScreen) || stage.Root.Q("board-trainer-ability-1") == null || stage.Root.Q("board-trainer-ability-2") == null) fails.Add("트레이너 카드 판에 능력 칸 둘이 없다");
                        else if (art == null || art.Q(className: "sn-trainer-art").resolvedStyle.backgroundImage.texture == null) fails.Add("트레이너 카드 판에 직업 그림이 없다(플레이어 번들)");
                        flow.CloseTrainer();
                        // 마을 창(S23): 고향 마을 — [전체 회복]은 켜져 있고 공중날기는 조무래기 점거로 꺼져 있다(누르지는 않는다)
                        if (!hud.Press(BoardHud.Village)) fails.Add("[마을]을 눌렀는데 마을 창이 열리지 않았다");
                        for (int f = 0; f < 3; f++) yield return null;
                        Snap(cam, flowRt, "flow-board-village.png", out _, out _, out _);
                        var villageProbe = stage.Current as VillageScreen;
                        if (villageProbe == null || !villageProbe.CanHeal) fails.Add("마을 창의 [전체 회복]이 켜져 있지 않다");
                        else if (villageProbe.CanFly || villageProbe.FlyWhy == null || stage.Root.Q("board-village-fly-why") == null) fails.Add("조무래기가 점거한 마을인데 공중날기가 까닭 없이 꺼져 있거나 켜져 있다");
                        else if (villageProbe.Dests.Count == 0 || stage.Root.Q("board-village-color-red") == null) fails.Add("마을 창에 목적지 줄·규토리 줄이 없다");
                        flow.CloseVillage();
                        if (stage.Current != null || !flow.InputOpen) fails.Add("마을 창을 닫은 뒤 보드 입력이 열리지 않았다");
                    }
                    reach = hud.Reach.Count;
                    if (reach == 0) fails.Add("갈 수 있는 칸이 칠해지지 않았다");
                    if (!boardBoot.Marks.Visible) fails.Add("이동 표시가 보이지 않는다");
                    if (!hud.Shows(BoardHud.EndTurn)) fails.Add("[턴 종료] 단추가 없다");
                    // 갈 수 있는 칸 가운데 화면에서 바로 고를 수 있는 첫 칸을 누른다
                    var client = flow.Client;
                    int from = client.View.Seats[client.Seat].Cell;
                    foreach (int cell in hud.Reach)
                    {
                        if (!BoardPick.Screen(boardBoot, cam, cell, 1920, 1080, out var px) || px.x < 0 || px.x >= 1920 || px.y < 0 || px.y >= 1080) continue;
                        // HUD에 가린 칸 = 가운데가 HUD 판의 사각형 안인 칸뿐이다. 그 밖인데 PC 층이 마우스를 받으면 건너뛰지 않고 실패로 친다(보이지 않는 요소가 보드를 막는 것)
                        if (BoardHud.UnderPanel(stage.Root, new Vector2(px.x / stage.Grade.PanelScale, (1080 - px.y) / stage.Grade.PanelScale))) continue;
                        if (flow.OverUi(px.x, px.y))
                        {
                            fails.Add("칸 " + cell + " (" + px.x + ", " + px.y + ")은 HUD 판 밖인데 PC 층이 마우스를 받는다");
                            continue;
                        }
                        if (BoardPick.Cell(boardBoot, cam, px.x, px.y, 1920, 1080) != cell) continue;   // 앞의 높은 칸에 가렸다
                        if (hud.StopsBefore(cell).Count > 0) continue;   // 결정 71: 길 가운데 뒷면 숨겨진 장소에서 멈추는 칸은 뺀다(아래가 걸은 뒤 도착 칸을 단정한다)
                        flow.Pointer(px.x, px.y, false);
                        if (boardBoot.Marks.Target != cell) fails.Add("마우스를 올린 칸이 칠해지지 않았다");
                        flow.Pointer(px.x, px.y, true);   // 고른다(걷기 전 확인 — 결정 70 ⑱)
                        if (flow.Hud.PickedCell != cell) fails.Add("칸을 눌렀는데 고른 칸이 되지 않았다");
                        flow.Pointer(px.x, px.y, true);   // 한 번 더: 걷는다
                        walkedTo = cell;
                        break;
                    }
                    if (walkedTo < 0) fails.Add("화면 좌표로 고를 수 있는 갈 수 있는 칸(길 가운데 숨겨진 장소 없음)이 없다");
                    else if (client.View.Seats[client.Seat].Cell != walkedTo) fails.Add("칸을 눌렀는데 이동하지 않았다");
                    for (int f = 0; f < 20; f++) yield return null;
                    Snap(cam, flowRt, "flow-board-walk.png", out _, out _, out _);
                    yield return Wait(() => flow.InputOpen, 15f);
                    if (!flow.InputOpen) fails.Add("걸은 뒤 입력이 열리지 않았다");
                    if (boardBoot.Pieces.Pieces.Count != 1 || boardBoot.Pieces.Pieces[0].Spot == null || boardBoot.Pieces.Pieces[0].Spot.Cell != walkedTo) fails.Add("말이 도착 칸에 서지 않았다");
                    if (walkedTo == from) fails.Add("말이 제자리다");
                    // 카메라 따라가기(결정 57 ①): 걸은 뒤 보는 점 = 도착 칸, 방향·줌 그대로
                    if (walkedTo >= 0 && Vector3.Distance(rig.Target, boardBoot.CellTop(walkedTo)) > 1e-3f) fails.Add("걸은 뒤 카메라가 도착 칸을 보지 않는다");
                    if (rig.CurrentZoom != BoardCameraRig.Zoom.Region || rig.Yaw != 0) fails.Add("카메라가 따라가며 방향·줌을 바꿨다");
                    // 숨겨진 장소에 멈췄으면 지나간다(그 선택의 합법 답)
                    if (client.FirstChoice() != null && client.FirstChoice().Kind == Sinnoh.Rules.ChoiceKind.HiddenSpot)
                    {
                        if (!hud.Press(BoardHud.Pass)) fails.Add("[지나가기]가 거절됐다");
                        yield return Wait(() => flow.InputOpen, 15f);
                    }
                    movesAfter = client.View.Seats[client.Seat].MovesLeft;

                    flow.OpenMenu();
                    for (int f = 0; f < 4; f++) yield return null;
                    Snap(cam, flowRt, "flow-board-menu.png", out _, out _, out _);
                    menuOk = stage.Current is BoardMenuScreen && stage.Root.Q("board-menu-quit") != null && !rig.InputEnabled && stage.Hud == hud;
                    if (!menuOk) fails.Add("게임 메뉴가 HUD 위에 열리지 않았다");
                    flow.CloseMenu();
                    for (int f = 0; f < 2; f++) yield return null;
                    if (stage.Current != null || !rig.InputEnabled) fails.Add("게임 메뉴를 닫아도 보드로 돌아오지 않았다");

                    if (!hud.Press(BoardHud.EndTurn)) fails.Add("[턴 종료]가 거절됐다");
                    for (int f = 0; f < 6; f++) yield return null;
                    // 넘김 띠가 다 들어와 머무는 때(띠 길이의 1/4 ~ 3/4 — 한 좌석 판은 500 가운데 125~375)에 찍는다 — 들어오는 첫 프레임에는 띠가 아직 화면 왼쪽 밖이다
                    yield return Wait(() =>
                    {
                        var seq = flow.Fx.Sequencer;
                        var band = seq.Current != null ? seq.Current.Step(Sinnoh.Fx.FxRows.Handoff) : null;
                        return band != null && seq.LocalMs >= band.StartMs + band.Ms / 4;
                    }, 10f);
                    bool bandShown = hud.Band.ActiveRow == Sinnoh.Fx.FxRows.Handoff;
                    for (int f = 0; f < 3; f++) yield return null;   // 패널 RT에 그려진 뒤
                    Snap(cam, flowRt, "flow-board-band.png", out _, out _, out _);
                    if (!bandShown) fails.Add("턴 종료 뒤 넘김 띠가 돌지 않았다");
                    yield return Wait(() => flow.InputOpen, 15f);
                    round = client.View.Round;
                    roundTwoReach = hud.Reach.Count;
                    for (int f = 0; f < 3; f++) yield return null;
                    Snap(cam, flowRt, "flow-board-round2.png", out _, out _, out _);
                    if (round != 2) fails.Add("턴 종료 뒤 라운드 " + round + " ≠ 2");
                    if (!flow.InputOpen) fails.Add("다음 라운드의 내 턴에 입력이 열리지 않았다");
                    if (client.View.ActiveSeat != client.Seat || client.Choice(Sinnoh.Rules.ChoiceKind.MapAction) == null) fails.Add("다음 라운드의 내 턴이 아니다");
                    if (roundTwoReach == 0) fails.Add("다음 라운드에 갈 수 있는 칸이 칠해지지 않았다");

                    // 숨겨진 장소 공개(25행 · rulings A25 — 규칙 끝 감사): 화면에서 고를 수 있는 칸 가운데 길에 다른 숨겨진 장소가 없는 뒷면 숨겨진 장소(엔진 원본에서 결과를 본다 —
                    // 맵에서 빠지는 규토리 더미 · 유물 · 떨어진 별을 야영지보다 먼저, 배틀이 열리는 우두머리는 뺀다)로 걸어 [공개] → 앞면으로 다 뒤집힌 때를 찍는다
                    int spotCell = -1, spotRank = -1;
                    Vector2 spotPx = default;
                    var host = flow.Session.HostCopy();
                    foreach (int cell in hud.Reach)
                    {
                        if (!BoardPick.Screen(boardBoot, cam, cell, 1920, 1080, out var px) || px.x < 0 || px.x >= 1920 || px.y < 0 || px.y >= 1080) continue;
                        if (BoardHud.UnderPanel(stage.Root, new Vector2(px.x / stage.Grade.PanelScale, (1080 - px.y) / stage.Grade.PanelScale)) || flow.OverUi(px.x, px.y)) continue;
                        if (BoardPick.Cell(boardBoot, cam, px.x, px.y, 1920, 1080) != cell || hud.StopsBefore(cell).Count > 0) continue;
                        foreach (var t in host.Tokens)
                        {
                            if (t.Kind != Sinnoh.Rules.TokenKind.HiddenSpot || t.FaceUp || t.Destroyed || t.Cell != cell) continue;
                            var item = flow.Ctx.Items.Items[t.Def];
                            int k = item.Kind == Sinnoh.Rules.ItemKind.AlphaPokemon ? -1 : item.Key == Sinnoh.Rules.HiddenSpotRules.Campsite ? 0 : 1;
                            if (k > spotRank) { spotRank = k; spotCell = cell; spotPx = px; }
                        }
                    }
                    if (spotCell < 0) fails.Add("둘째 라운드에 화면에서 고를 수 있는 칸에 공개할 뒷면 숨겨진 장소가 없다");
                    else
                    {
                        flow.Pointer(spotPx.x, spotPx.y, true);
                        flow.Pointer(spotPx.x, spotPx.y, true);
                        yield return Wait(() => flow.InputOpen && client.Choice(Sinnoh.Rules.ChoiceKind.HiddenSpot) != null, 15f);
                        if (client.View.Seats[client.Seat].Cell != spotCell || !hud.Press(BoardHud.Reveal)) fails.Add("숨겨진 장소 칸 " + spotCell + "에서 [공개]를 하지 못했다");
                        else
                        {
                            bool drawn = true;
                            yield return Wait(() =>
                            {
                                var step = Sinnoh.Fx.SpotBeats.StepOf(flow.Fx.Sequencer.Current);
                                if (step == null) return false;
                                drawn &= boardBoot.Tokens.RaisedDrawn;
                                return boardBoot.Tokens.RaisedFlip >= 1f;
                            }, 10f);
                            spotFaced = boardBoot.Tokens.RaisedDrawn && boardBoot.Tokens.RaisedFlip >= 1f && drawn;
                            for (int f = 0; f < 3; f++) yield return null;   // 패널 RT에 그려진 뒤
                            Snap(cam, flowRt, "flow-board-spot.png", out _, out _, out _);
                            if (!spotFaced) fails.Add("숨겨진 장소를 공개했는데 띄운 토큰이 그려지지 않았거나 앞면으로 다 뒤집히지 않았다(칸 " + spotCell + ")");
                            yield return Wait(() => flow.InputOpen, 15f);
                        }
                    }
                    spotRevealed = spotCell;
                }
                sb.Append(",\"hud\":").Append(hud != null ? "true" : "false").Append(",\"reach\":").Append(reach).Append(",\"walkedTo\":").Append(walkedTo)
                  .Append(",\"movesAfterWalk\":").Append(movesAfter).Append(",\"menu\":").Append(menuOk ? "true" : "false")
                  .Append(",\"round\":").Append(round).Append(",\"roundTwoReach\":").Append(roundTwoReach)
                  .Append(",\"spotRevealed\":").Append(spotRevealed).Append(",\"spotFaced\":").Append(spotFaced ? "true" : "false");
                sb.Append(",\"villageArt\":").Append(villageArt ? "true" : "false").Append(",\"monArt\":").Append(monArt ? "true" : "false")
                  .Append(",\"portrait\":").Append(portrait ? "true" : "false").Append(",\"icon\":").Append(icon ? "true" : "false")
                  .Append(",\"onBoard\":").Append(flow.OnBoard ? "true" : "false").Append(",\"pieces\":").Append(pieces)
                  .Append(",\"boardInput\":").Append(rig.InputEnabled ? "true" : "false").Append(",\"boardUiPixels\":").Append(boardUi)
                  .Append(",\"commands\":").Append(flow.Session != null ? flow.Session.CommandCount : -1);

                // 배틀 화면(04 §4-7): 새 판 → 고향 마을에서 [도전] → 기술을 눌러 끝까지 → 결과 창 → [확인] → 보드의 다음 턴.
                // DS 층의 무대·체력 상자·원작 자산 글꼴은 stage 번들에서, 셰이더는 씬의 빌드 셰이더 목록에서 온다 — 에디터 시험이 못 보는 곳이다
                bool battleOpened = false, battleDs = false, battleClosed = false, dsPlaced = false, rollShown = false, rollBoard = false, rollLanded = false, wipeSeen = false, gruntShown = false;
                int battleMoves = 0, dsColors = 0, battleLines = 0, rollFaces = 0;
                var rollFrames = new List<double>();
                string battleResult = "none";
                flow.StartSolo(new Sinnoh.Rules.Session.SoloSettings(), 20261112, 7);
                for (int f = 0; f < 6; f++) yield return null;
                if (stage.Current is SetupVillageScreen village2)
                {
                    village2.Pick(0);
                    village2.Confirm();
                    flow.Skip(flow.QueueLeftMs);   // 고향 마을 연출 → 카드가 들어옴
                }
                for (int f = 0; f < 6; f++) yield return null;
                if (stage.Current is SetupPickScreen pick2)
                {
                    pick2.PickStarter(0);
                    pick2.PickCard(0);
                    pick2.Confirm();
                    flow.Skip(flow.OpeningMs);   // 고른 뒤·공개 → 숨겨진 장소 배치 → 막 카드 · 조무래기 놓기
                }
                for (int f = 0; f < 6; f++) yield return null;
                yield return Wait(() => flow.InputOpen, 15f);
                if (flow.Hud == null || !flow.Hud.Press(BoardHud.Challenge)) fails.Add("[도전]이 거절됐다(고향 마을의 1막 조무래기 배틀)");
                else
                {
                    // 배틀 화면은 조우 전환(22행)의 가운데 — 가운데 필드의 줄무늬가 다 닫힌 때 — 에 열린다
                    float wt = Time.realtimeSinceStartup;
                    while (!(stage.Current is BattleScreen) && Time.realtimeSinceStartup - wt < 20f)
                    {
                        flow.Skip(250);
                        yield return null;
                        if (flow.WipeCover > 0f) wipeSeen = true;
                    }
                    var battle = stage.Current as BattleScreen;
                    battleOpened = battle != null;
                    if (!battleOpened) fails.Add("배틀 화면이 열리지 않았다");
                    else
                    {
                        if (!wipeSeen) fails.Add("조우 전환의 줄무늬가 보이지 않았다");
                        // 갤럭시단 등장(72행): 조무래기 앞모습(stage 번들의 Stage/Foe)이 DS의 상대 자리에 선다
                        var enterSeq = flow.Fx.Sequencer;
                        var gruntStep = enterSeq.Current != null ? enterSeq.Current.Step(Sinnoh.Fx.EnterRows.Galactic) : null;
                        if (gruntStep != null)
                        {
                            stage.Tweens.Advance(Math.Max(0, gruntStep.StartMs + 1000 - enterSeq.LocalMs));
                            for (int f = 0; f < 2; f++) yield return null;
                            gruntShown = battle.FoeTrainerShown && GenAssets.Load<Texture2D>(battle.FoeKey) != null;
                            Snap(cam, flowRt, "flow-battle-grunt.png", out _, out _, out _);
                        }
                        if (!gruntShown) fails.Add("갤럭시단 조무래기 앞모습이 보이지 않았다(stage 번들의 " + Sinnoh.Fx.FoeArt.GruntMale + " · " + Sinnoh.Fx.FoeArt.GruntFemale + ")");
                        // 상대(AI)의 답은 방장 멈춤(연출 길이) 뒤에 오고 화면도 그 연출을 다 돈다 — 세션 시각과 연출 시각을 함께 더 흘려 기다림을 줄인다
                        float bt = Time.realtimeSinceStartup;
                        while (!(battle.Open && battle.Moves.Count > 0) && !battle.ShowsResult && Time.realtimeSinceStartup - bt < 60f)
                        {
                            if (battle.Open && battle.AskingToken) battle.PressToken(true);
                            else if (battle.Open && battle.AskingOrder) battle.PressOrder(0);   // 결정 72 효과 순서: 기본 순서
                            else if (battle.Open && battle.AskingFate) battle.PressFatePass();   // 운명 창: 안 씀
                            else if (battle.Open && battle.AskingAsk) battle.PressAsk(false);
                            flow.Skip(1000);
                            yield return null;
                        }
                        for (int f = 0; f < 3; f++) yield return null;
                        battleDs = battle.Ds != null;
                        if (!battleDs) fails.Add("DS 층을 만들지 못했다(stage 번들의 무대·체력 상자·원작 자산 글꼴, 셰이더 " + DsStage.ShaderName + ")");
                        else
                        {
                            dsColors = ColorsOf(battle.Ds.Output);
                            if (dsColors < 32) fails.Add("DS 화면이 비었다(색 " + dsColors + ")");
                        }
                        var dsBox = stage.Root.Q("battle-ds").worldBound;
                        dsPlaced = dsBox.x == 576 && dsBox.y == 187 && dsBox.width == 768 && dsBox.height == 576;
                        if (!dsPlaced) fails.Add("DS 자리 " + dsBox + " ≠ (576, 187) 768×576(04 §4-3)");
                        if (stage.Root.Q("battle-move-1") == null && !battle.ShowsResult) fails.Add("기술 줄이 없다");
                        var foeArt = stage.Root.Q("battle-foe-art");   // 결정 206 ②: 기둥은 원작 카드 앞면(cardfaces 번들)
                        if (foeArt == null || foeArt.resolvedStyle.backgroundImage.texture == null) fails.Add("상대 카드 그림을 읽지 못했다(cardfaces 번들)");
                        Snap(cam, flowRt, "flow-battle.png", out _, out _, out _);
                        NoteMusic("battle", flow.Music());

                        // 배틀 연출(04 §8-9): 첫 기술을 누르고 그 공격의 연출을 실시각으로 본다 — 굴림판(주사위 녹화본 rolls.bytes · 주사위 셰이더 넷)이 플레이어에서 뜨는지,
                        // 굴림판이 떠 있는 동안의 프레임 시간(3D 주사위를 RT에 그리는 가장 무거운 배틀 연출 — 수직 동기·프레임 제한을 끄고 잰다), 체력이 착지 때 바뀌는지
                        if (battle.Open && battle.Moves.Count > 0)
                        {
                            int foeSide = 1 - flow.Battle.MySide, hpWas = battle.ShownMon(foeSide).Hp;
                            int vsyncWas = QualitySettings.vSyncCount, fpsWas = Application.targetFrameRate;
                            if (battle.PressMove(0))
                            {
                                battleMoves++;
                                QualitySettings.vSyncCount = 0;
                                Application.targetFrameRate = -1;
                                bool shot = false;
                                float rt0 = Time.realtimeSinceStartup;
                                double last = Time.realtimeSinceStartupAsDouble;
                                while (!battle.Ready && Time.realtimeSinceStartup - rt0 < 30f)
                                {
                                    yield return null;
                                    double now = Time.realtimeSinceStartupAsDouble;
                                    if (battle.Rolling)
                                    {
                                        rollShown = true;
                                        rollBoard |= battle.RollBoardOn;
                                        rollFaces = Math.Max(rollFaces, battle.RollFaces.Count);
                                        var step = Sinnoh.Fx.BattleRoll.StepOf(flow.Fx.Sequencer.Current);
                                        if (step != null && flow.Fx.Sequencer.LocalMs < step.EndMs) rollFrames.Add((now - last) * 1000.0);   // 굴림 단계(주사위가 구르고 세는 동안)의 프레임
                                        if (!shot && battle.RollFaces.Count > 0)
                                        {
                                            shot = true;
                                            Snap(cam, flowRt, "flow-battle-roll.png", out _, out _, out _);
                                            now = Time.realtimeSinceStartupAsDouble;   // 찍느라 든 시간은 재지 않는다
                                        }
                                    }
                                    if (battle.ShownMon(foeSide).Hp != hpWas) rollLanded = true;
                                    last = now;
                                }
                                QualitySettings.vSyncCount = vsyncWas;
                                Application.targetFrameRate = fpsWas;
                                if (!rollShown) fails.Add("기술을 눌렀는데 굴림판이 뜨지 않았다");
                                else if (!rollBoard) fails.Add("굴림판에 주사위 그림이 없다(씬의 rolls.bytes · 주사위 셰이더 넷)");
                                if (rollShown && rollFaces == 0) fails.Add("굴림판의 결과 줄에 면이 서지 않았다");
                                if (!battle.Ready) fails.Add("공격 연출이 30초 안에 끝나지 않았다");
                            }
                        }
                        bt = Time.realtimeSinceStartup;
                        while (!battle.ShowsResult && Time.realtimeSinceStartup - bt < 90f)
                        {
                            if (battle.Open && battle.AskingToken) battle.PressToken(true);
                            else if (battle.Open && battle.AskingOrder) battle.PressOrder(0);   // 결정 72 효과 순서: 기본 순서
                            else if (battle.Open && battle.AskingFate) battle.PressFatePass();   // 운명 창: 안 씀
                            else if (battle.Open && battle.AskingAsk) battle.PressAsk(false);
                            else if (battle.Open && battle.Moves.Count > 0 && battle.PressMove(0)) battleMoves++;
                            flow.Skip(1000);
                            yield return null;
                        }
                        for (int f = 0; f < 3; f++) yield return null;
                        Snap(cam, flowRt, "flow-battle-result.png", out _, out _, out _);
                        if (!battle.ShowsResult) fails.Add("배틀이 끝나지 않았다");
                        else
                        {
                            battleLines = flow.Battle.Lines.Count;
                            battleResult = flow.Battle.Drawn ? "draw" : flow.Battle.Won ? "win" : "lose";
                            if (stage.Root.Q("battle-result-ok") == null) fails.Add("결과 창에 [확인]이 없다");
                            battle.Confirm();
                            yield return Wait(() => flow.InputOpen, 15f);
                            battleClosed = stage.Current == null && flow.InputOpen && flow.Client.Choice(Sinnoh.Rules.ChoiceKind.MapAction) != null;
                            if (!battleClosed) fails.Add("결과를 확인한 뒤 보드의 다음 턴이 열리지 않았다");
                        }
                    }
                }
                sb.Append(",\"battle\":{\"opened\":").Append(battleOpened ? "true" : "false").Append(",\"ds\":").Append(battleDs ? "true" : "false")
                  .Append(",\"dsColors\":").Append(dsColors).Append(",\"dsPlaced\":").Append(dsPlaced ? "true" : "false").Append(",\"moves\":").Append(battleMoves)
                  .Append(",\"lines\":").Append(battleLines).Append(",\"result\":\"").Append(battleResult).Append("\",\"closed\":").Append(battleClosed ? "true" : "false").Append('}');
                {
                    // 굴림 단계의 프레임 시간(ms — 창 없는 플레이어, 제한 없음). 기준 16.7(60fps)은 적기만 한다(다른 일을 하는 PC에서는 흔들린다 — 판정에 넣지 않는다)
                    var roll = FrameProbe.Summarize(rollFrames);
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    sb.Append(",\"roll\":{\"shown\":").Append(rollShown ? "true" : "false").Append(",\"board\":").Append(rollBoard ? "true" : "false").Append(",\"faces\":").Append(rollFaces)
                      .Append(",\"hpLanded\":").Append(rollLanded ? "true" : "false").Append(",\"frames\":").Append(roll.Count)
                      .Append(",\"meanMs\":").Append(roll.Mean.ToString("0.000", inv)).Append(",\"p95Ms\":").Append(roll.P95.ToString("0.000", inv)).Append(",\"maxMs\":").Append(roll.Max.ToString("0.000", inv))
                      .Append(",\"under16_7\":").Append(roll.Count > 0 && roll.P95 <= FrameProbe.ThresholdMs ? "true" : "false").Append('}');
                    Debug.Log("[ScreenProbe] 굴림 단계 프레임 " + roll.Count + "개: 평균 " + roll.Mean.ToString("0.00", inv) + " ms · p95 " + roll.P95.ToString("0.00", inv) + " ms · 최대 " + roll.Max.ToString("0.00", inv) + " ms");
                }

                // 조우·포획(04 §4-1·§4-8): 볼을 쥐여 준 판(SoloTrail.Catch)을 길잡이가 화면으로 지난다 — 찾아보기 → 조우 창 → 야생 배틀 → [가방] → 볼 → 결과.
                // 조우 등급 그림·볼·규토리 그림은 cards 번들에서 온다 — 에디터 시험이 못 보는 곳이다
                int encCards = 0, encIcons = 0, ballRows = 0, throws = 0;
                bool encOpened = false, bagOpened = false, wildEnded = false, encClosed = false, captureBack = false, captureBall = false, captureRoll = false, summaryCaught = false;
                string wildResult = "none", backDrawn = "";
                // 포획 판도 고른 주인공(5세대 — 뒷모습 시트가 4세대와 크기·틱 순서가 다르다)으로: 고른 주인공 = 볼 던지는 뒷모습(결정 60)이 플레이어 번들에서 그려지는지 본다
                var pilot = FlowPilot.Start(flow, Sinnoh.Ai.SoloTrail.Catch, ProbeProtagonist);
                for (int f = 0; f < 6; f++) yield return null;
                float et = Time.realtimeSinceStartup;
                while (!(stage.Current is EncounterScreen es && es.Open) && Time.realtimeSinceStartup - et < 60f)
                {
                    if (!FlowPilot.Step(flow, pilot)) flow.Skip(1000);
                    yield return null;
                }
                var enc = stage.Current as EncounterScreen;
                encOpened = enc != null;
                if (!encOpened) fails.Add("조우 창이 열리지 않았다(찾아보기 — 받을 화면 없는 선택 " + flow.NotReadyShown + "번)");
                else
                {
                    for (int f = 0; f < 3; f++) yield return null;
                    encCards = enc.Cards.Count;
                    if (stage.Root.Q("encounter-card-" + encCards) == null) fails.Add("조우 카드 요소가 없다");
                    if (stage.Root.Q("encounter-card-1").Q(className: "sn-mon-canvas") == null) fails.Add("조우 카드의 도트를 읽지 못했다(sprites 번들)");
                    // 번들에서 읽은 그림: 등급 그림 + 보상 규토리 + 볼 넷
                    foreach (var gotIcon in stage.Root.Q("encounter-panel").Query(className: "sn-enc-grade").ToList())
                        if (gotIcon.resolvedStyle.backgroundImage.texture != null) encIcons++;
                    foreach (var gotIcon in stage.Root.Q("encounter-panel").Query(className: "sn-icon").ToList())
                        if (gotIcon.resolvedStyle.backgroundImage.texture != null) encIcons++;
                    int wantIcons = encCards + stage.Root.Q("encounter-panel").Query(className: "sn-icon").ToList().Count;
                    if (encIcons != wantIcons || encIcons < encCards + 4) fails.Add("조우 창의 등급·볼·규토리 그림 " + encIcons + "/" + wantIcons + "장만 읽었다(cards 번들)");
                    Snap(cam, flowRt, "flow-encounter.png", out _, out _, out _);
                    // 고른다 → 야생 배틀 → [가방]이 켜질 때까지
                    et = Time.realtimeSinceStartup;
                    while (!(stage.Current is BattleScreen bs && bs.BagOn) && Time.realtimeSinceStartup - et < 60f)
                    {
                        if (!FlowPilot.Step(flow, pilot)) flow.Skip(1000);
                        yield return null;
                    }
                    var wild = stage.Current as BattleScreen;
                    bagOpened = wild != null && wild.OpenBag();
                    if (!bagOpened) fails.Add("야생 배틀에서 [가방]이 열리지 않았다");
                    else
                    {
                        for (int f = 0; f < 3; f++) yield return null;
                        ballRows = wild.Balls.Count;
                        var ball = stage.Root.Q("battle-ball-1");
                        if (ball == null || ball.Q(className: "sn-icon").resolvedStyle.backgroundImage.texture == null) fails.Add("볼 줄의 그림을 읽지 못했다(cards 번들)");
                        Snap(cam, flowRt, "flow-battle-bag.png", out _, out _, out _);
                        et = Time.realtimeSinceStartup;
                        while (!(wild.ShowsResult && !wild.AskingReward) && Time.realtimeSinceStartup - et < 90f)
                        {
                            int before = flow.Session.CommandCount;
                            bool throwing = wild.BagOn;
                            if (!FlowPilot.Step(flow, pilot, false)) flow.Skip(1000);
                            else if (throwing && flow.Session.CommandCount > before)
                            {
                                throws++;
                                if (throws == 1)
                                {
                                    // 포획 연출(68행)의 한가운데 — 볼이 날아가는 때: 내 주인공 뒷모습(trainers 번들)과 볼(stage 번들)이 DS에, 포획 주사위가 독의 굴림판에
                                    yield return null;
                                    var throwSeq = flow.Fx.Sequencer;
                                    var throwStep = Sinnoh.Fx.CaptureStage.StepOf(throwSeq.Current);
                                    if (throwStep == null) fails.Add("포획 단계가 재생되지 않았다");
                                    else
                                    {
                                        stage.Tweens.Advance(Math.Max(0, throwStep.StartMs + 450 - throwSeq.LocalMs));
                                        for (int f = 0; f < 2; f++) yield return null;
                                        string ballItem = Sinnoh.Fx.BattleFeed.BallItem(((Sinnoh.Rules.CaptureAttempt)throwStep.Events[0]).BallKey);
                                        backDrawn = wild.Ds != null ? wild.Ds.BackDrawn ?? "" : "";
                                        captureBack = wild.Capture.Trainer && flow.Protagonist(flow.Client.Seat) == ProbeProtagonist && backDrawn == "Trainers/Back/official/" + ProbeProtagonist + ".png";
                                        captureBall = wild.Capture.Ball && GenAssets.Load<Texture2D>(DsStage.BallFolder + ballItem + ".png") != null && GenAssets.Load<Texture2D>(DsStage.BallFolder + ballItem + "_open.png") != null;
                                        captureRoll = wild.RollIsCapture && wild.RollBoardOn;
                                        if (!captureBack) fails.Add("포획: DS에 그려진 뒷모습이 고른 주인공(" + ProbeProtagonist + ")이 아니다(그려진 것 「" + backDrawn + "」 — trainers 번들의 Trainers/Back/official · trainers.json)");
                                        if (!captureBall) fails.Add("포획: 볼 그림을 읽지 못했다(stage 번들의 Stage/Ball)");
                                        if (!captureRoll) fails.Add("포획: 독의 굴림판에 포획 주사위가 없다");
                                        Snap(cam, flowRt, "flow-capture.png", out _, out _, out _);
                                    }
                                }
                            }
                            yield return null;
                        }
                        for (int f = 0; f < 3; f++) yield return null;
                        Snap(cam, flowRt, "flow-battle-captured.png", out _, out _, out _);
                        wildEnded = wild.ShowsResult;
                        if (!wildEnded) fails.Add("야생 배틀이 끝나지 않았다");
                        else
                        {
                            wildResult = flow.Battle.Result.ToString().ToLowerInvariant();
                            wild.Confirm();
                            // 잡은 턴의 결과 요약(101행): 역전환이 걷힌 뒤 요약 판에 잡은 포켓몬이 적혀 있다
                            flow.Skip(Sinnoh.Fx.EnterBeats.WipeBackMs + 100);
                            for (int f = 0; f < 2; f++) yield return null;
                            var summary = stage.Root.Q("turn-band-summary");
                            summaryCaught = flow.Hud != null && flow.Hud.Band.ActiveRow == Sinnoh.Fx.FxRows.TurnSummary && summary != null && summary.Query<Label>().ToList().Count >= 3;
                            if (!summaryCaught) fails.Add("잡은 턴의 결과 요약이 뜨지 않았다");
                            Snap(cam, flowRt, "flow-turn-summary.png", out _, out _, out _);
                            yield return Wait(() => flow.InputOpen, 15f);
                            encClosed = stage.Current == null && flow.InputOpen;
                            if (!encClosed) fails.Add("야생 배틀의 결과를 확인한 뒤 보드의 다음 턴이 열리지 않았다");
                        }
                    }
                }
                if (flow.NotReadyShown > 0) fails.Add("받을 화면 없는 선택이 " + flow.NotReadyShown + "번 열렸다(「이 선택 화면은 아직 준비 중입니다.」)");
                sb.Append(",\"encounter\":{\"opened\":").Append(encOpened ? "true" : "false").Append(",\"cards\":").Append(encCards).Append(",\"icons\":").Append(encIcons)
                  .Append(",\"bag\":").Append(bagOpened ? "true" : "false").Append(",\"ballRows\":").Append(ballRows).Append(",\"throws\":").Append(throws)
                  .Append(",\"result\":\"").Append(wildResult).Append("\",\"closed\":").Append(encClosed ? "true" : "false").Append(",\"notReady\":").Append(flow.NotReadyShown).Append('}');
                sb.Append(",\"enter\":{\"wipe\":").Append(wipeSeen ? "true" : "false").Append(",\"grunt\":").Append(gruntShown ? "true" : "false")
                  .Append(",\"captureBack\":").Append(captureBack ? "true" : "false").Append(",\"backDrawn\":\"").Append(backDrawn).Append('"').Append(",\"captureBall\":").Append(captureBall ? "true" : "false")
                  .Append(",\"captureRoll\":").Append(captureRoll ? "true" : "false").Append(",\"summaryCaught\":").Append(summaryCaught ? "true" : "false").Append('}');
            }
            sb.Append('}');

            // M6 새 번들 키(마감 감사): 트레이너 카드 전용 아이템 아이콘 11 · 왜곡 균열 카드 그림 6 · 간부·태홍 앞모습 4 · 해금 연출 카드 틀 13 · 금색 ? 뒷면 1 · 울트라 온슬롯 카드 그림 1(결정 87 V8·V24 나)이 플레이어 번들에서 읽히나
            if (flow != null && flow.Ctx != null)
            {
                var want = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var it in flow.Ctx.Items.Items)
                    if (it.Kind == Sinnoh.Rules.ItemKind.TrainerItem) want.Add(WorkUi.IconKey(it));
                const string breach = "distortion_breach.";
                for (int c = 0; c < flow.Ctx.Decks.CardIds.Count; c++)
                {
                    string id = flow.Ctx.Decks.CardIds[c];
                    if (id != null && id.StartsWith(breach, StringComparison.Ordinal)) want.Add(GenAssets.BreachArt + id.Substring(breach.Length) + ".png");
                }
                foreach (string k in new[] { Sinnoh.Fx.FoeArt.Mars, Sinnoh.Fx.FoeArt.Jupiter, Sinnoh.Fx.FoeArt.Saturn, Sinnoh.Fx.FoeArt.Cyrus }) want.Add(k);
                foreach (string tier in new[] { "bronze", "silver", "gold" })
                    foreach (string part in new[] { "_base", "_bar", "_back" }) want.Add(GenAssets.TrainerFrame + tier + part + ".png");
                foreach (string k in new[] { "locked_blue", "locked_red", "icon_moderate", "icon_strong" }) want.Add(GenAssets.TrainerFrame + k + ".png");
                want.Add(GenAssets.SpecialSpotBack);
                want.Add(GenAssets.OnslaughtArt);
                var missing = new List<string>();
                foreach (string k in want)
                    if (GenAssets.Load<Texture2D>(k) == null) missing.Add(k);
                sb.Append(",\"m6keys\":{\"want\":").Append(want.Count).Append(",\"loaded\":").Append(want.Count - missing.Count).Append(",\"missing\":[");
                for (int i = 0; i < missing.Count; i++) sb.Append(i > 0 ? "," : "").Append('"').Append(missing[i]).Append('"');
                sb.Append("]}");
                if (want.Count != 36) fails.Add("M6 새 번들 키 수가 36이 아니다(" + want.Count + ")");
                if (missing.Count > 0) fails.Add("M6 새 번들 키가 플레이어에서 읽히지 않는다: " + string.Join(" ", missing));
            }

            // 저장 → 이어 받기(plan §4 「저장 기반·자동 저장」): 저장 폴더를 받아 띄웠을 때만(`-sinnohSaves <폴더>` — 확인은 사용자의 저장 폴더에 쓰지 않는다).
            // 새 판을 처음부터 저장하고, 첫 턴에서 게임 메뉴(저장 상태 줄)로 타이틀에 나갔다가 「이어하기」 → 저장 목록(S04)의 [이어하기]로 같은 상태의 보드에 돌아오는지 본다.
            // 만든 저장은 목록의 [삭제]로 지운다(04 §9-12)
            if (flow != null && flow.Ctx != null && flow.SaveRoot != null)
            {
                flow.StartSolo(new Sinnoh.Rules.Session.SoloSettings { Protagonist = ProbeProtagonist }, 20261113, 7);
                yield return null;
                string saveId = flow.SaveId;
                var saveVillage = stage.Current as SetupVillageScreen;
                if (saveVillage != null)
                {
                    saveVillage.Pick(0);
                    saveVillage.Confirm();
                    flow.Skip(flow.QueueLeftMs);   // 고향 마을 연출 → 카드가 들어옴
                }
                yield return null;
                var savePick = stage.Current as SetupPickScreen;
                if (savePick != null)
                {
                    savePick.PickStarter(0);
                    savePick.PickCard(0);
                    savePick.Confirm();
                }
                yield return null;
                flow.Skip(flow.OpeningMs);
                yield return Wait(() => flow.InputOpen, 15f);
                bool saved = saveId != null && flow.SaveStatus == SaveStatus.Saved && flow.OnBoard, resumed = false, same = false;
                ulong savedHash = flow.Session != null ? flow.Session.Hash : 0;
                int savedCount = flow.Session != null ? flow.Session.CommandCount : -1, listed = 0, party = 0;
                if (!saved) fails.Add("새 판이 저장되지 않았다(저장 폴더 " + flow.SaveRoot + ", " + flow.SaveStatus + ", " + flow.SaveError + ")");
                // 게임 메뉴: 저장 상태 줄 → [타이틀로 나가기] → 저장된 판이라는 안내 → [나가기]
                bool menuSaved = false, titleOn = false, listShown = false, deleted = false;
                string headHash = null;
                flow.OpenMenu();
                yield return null;
                var saveMenu = stage.Current as BoardMenuScreen;
                if (saveMenu != null)
                {
                    menuSaved = stage.Root.Q("board-menu-save") != null && saveMenu.SaveText == stage.T("screen.board.menu.save.saved").Replace("{time}", GameFlow.SaveTime(flow.SavedAtMs, date: false))
                        && saveMenu.QuitNoteKey == "screen.board.menu.quit.saved";
                    saveMenu.AskQuit();
                    yield return null;
                    Snap(cam, flowRt, "flow-menu-saved.png", out _, out _, out _);
                    saveMenu.Quit();
                }
                else flow.OpenTitle();
                if (!menuSaved) fails.Add("게임 메뉴의 저장 상태 줄이 「자동 저장됨」이 아니다(" + (saveMenu != null ? saveMenu.SaveText : "메뉴 없음") + ")");
                yield return null;
                // 타이틀: 「이어하기」가 켜지고 골라져 있다 → 저장 목록
                var saveTitle = stage.Current as TitleScreen;
                int continueAt = saveTitle != null ? IndexOf(saveTitle, TitleScreen.Continue) : -1;
                titleOn = continueAt >= 0 && saveTitle.Ready(saveTitle.Shown[continueAt]) && saveTitle.Picked == continueAt && saveTitle.SaveLine != stage.T("screen.title.no_save");
                if (!titleOn) fails.Add("저장이 있는데 타이틀 「이어하기」가 켜지지 않았거나 골라져 있지 않다");
                for (int i = 0; i < 3; i++) yield return null;
                Snap(cam, flowRt, "flow-title-continue.png", out _, out _, out _);
                if (titleOn) saveTitle.Activate(continueAt);
                yield return null;
                var saveList = stage.Current as SaveListScreen;
                listShown = saveList != null && saveList.Entries.Count >= 1 && stage.Root.Q("saves-row-0") != null && stage.Root.Q("saves-resume-0") != null
                    && FaceBox.Shown(stage.Root.Q("saves-face-0")) == ProbeProtagonist && stage.Root.Q("saves-party-0").childCount >= 1;
                if (!listShown) fails.Add("저장 목록(S04)에 방금 판의 칸(얼굴 · 파티 · [이어하기])이 없다");
                for (int i = 0; i < 3; i++) yield return null;
                Snap(cam, flowRt, "flow-saves.png", out _, out _, out _);
                if (saved)
                {
                    var saves = flow.Saves();
                    listed = saves.Count;
                    var mine = saves.Find(e => e.SaveId == saveId);
                    if (mine == null || !GameFlow.CanResume(mine)) fails.Add("저장 목록에 방금 판이 없거나 읽을 수 없다(" + (mine != null ? mine.Detail : "없음") + ")");
                    else
                    {
                        party = mine.Head.Summary.Seats.Count > 0 ? mine.Head.Summary.Seats[0].Party.Count : 0;
                        if (party < 1 || mine.Head.Protagonists.Count != 1 || mine.Head.Protagonists[0] != ProbeProtagonist || mine.Head.Summary.SavedAtMs <= 0)
                            fails.Add("저장 머리의 요약(파티 " + party + " · 주인공 · 저장 시각 " + mine.Head.Summary.SavedAtMs + ")이 비었다");
                        // 머리의 데이터 해시 = 이 플레이어가 읽은 표의 해시(manifest.json의 contentHash와 같은 값이어야 한다 — 값은 아래 save.hash로 낸다)
                        headHash = mine.Head.ContentHash;
                        if (string.IsNullOrEmpty(flow.ContentHash) || headHash != flow.ContentHash) fails.Add("저장 머리의 데이터 해시(" + headHash + ")가 지금 데이터의 것(" + flow.ContentHash + ")과 다르다");
                        if (!(saveList != null ? saveList.Resume(saveId) : flow.Resume(saveId))) fails.Add("이어 받지 못했다: " + flow.ResumeError);
                        else
                        {
                            resumed = true;
                            yield return null;
                            same = flow.Session.Hash == savedHash && flow.Session.CommandCount == savedCount;
                            if (!same) fails.Add("이어 받은 상태가 나갈 때와 다르다(명령 " + flow.Session.CommandCount + " / " + savedCount + ")");
                            flow.Skip(1000);
                            yield return Wait(() => flow.InputOpen, 15f);
                            if (!flow.OnBoard || stage.Current != null || !flow.InputOpen || flow.Hud == null || flow.Protagonist(flow.Client.Seat) != ProbeProtagonist)
                                fails.Add("이어 받은 뒤 보드(HUD · 입력 · 주인공)가 열리지 않았다");
                            else if (flow.Hud.RecentLog.Count != 1 || flow.Hud.RecentLog[0] != stage.T("screen.log.line.resumed"))
                                fails.Add("이어 받은 보드의 기록 판이 「이어서 시작」 한 줄이 아니다(" + string.Join(" | ", flow.Hud.RecentLog) + ")");
                            for (int i = 0; i < 3; i++) yield return null;
                            Snap(cam, flowRt, "flow-resumed.png", out _, out _, out _);
                            flow.OpenTitle();
                            yield return null;
                        }
                    }
                }
                // 저장 목록의 [삭제] → 한 번 더 묻기 → [삭제]: 그 칸이 사라지고, 타이틀의 「이어하기」는 남은 저장이 있을 때만 켜져 있다(앞 단계의 판들도 이 폴더에 저장됐다)
                if (saveId != null)
                {
                    flow.OpenSaves();
                    yield return null;
                    var delList = stage.Current as SaveListScreen;
                    if (delList != null)
                    {
                        delList.AskDelete(saveId);
                        yield return null;
                        bool asked = delList.Asking == saveId && stage.Root.Q("saves-delete-yes") != null;
                        Snap(cam, flowRt, "flow-saves-ask.png", out _, out _, out _);
                        deleted = asked && delList.Delete();
                        foreach (var e in delList.Entries) if (e.SaveId == saveId) deleted = false;
                        if ((delList.Entries.Count == 0) != (stage.Root.Q("saves-empty") != null)) deleted = false;
                        delList.Back();
                        yield return null;
                        var after = stage.Current as TitleScreen;
                        int at2 = after != null ? IndexOf(after, TitleScreen.Continue) : -1;
                        if (at2 < 0 || after.Ready(after.Shown[at2]) != (flow.Saves().Count > 0)) deleted = false;
                    }
                    if (!deleted) fails.Add("저장 목록에서 확인이 만든 저장을 지우지 못했거나 타이틀 「이어하기」가 꺼지지 않았다: " + saveId);
                    if (!deleted) flow.DeleteSave(saveId);
                }
                sb.Append(",\"save\":{\"saved\":").Append(saved ? "true" : "false").Append(",\"menu\":").Append(menuSaved ? "true" : "false").Append(",\"title\":").Append(titleOn ? "true" : "false")
                  .Append(",\"list\":").Append(listShown ? "true" : "false").Append(",\"deleted\":").Append(deleted ? "true" : "false").Append(",\"listed\":").Append(listed).Append(",\"party\":").Append(party)
                  .Append(",\"resumed\":").Append(resumed ? "true" : "false").Append(",\"same\":").Append(same ? "true" : "false").Append(",\"left\":").Append(flow.Saves().Count)
                  .Append(",\"hash\":\"").Append(headHash ?? "").Append("\"}");
            }
            root.SetTarget(null);
            flowRt.Release();
            Destroy(flowRt);

            for (int i = 0; i < 3; i++) yield return null;
            Finish(sb, -1);
        }

        // RT에 그려진 색의 수(빈 그림인지 본다)
        static int ColorsOf(RenderTexture rt)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var colors = new HashSet<Color32>(tex.GetPixels32());
            Destroy(tex);
            return colors.Count;
        }

        // 조건이 될 때까지 프레임을 보낸다(실시각 상한) — 연출 길이만큼 기다린다
        static IEnumerator Wait(Func<bool> done, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        static int IndexOf(TitleScreen title, string id)
        {
            for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == id) return i;
            return -1;
        }

        /// <summary>패널 RT의 지금 그림을 같은 크기로 그린 보드 위에 겹쳐 PNG로 남긴다. covered = 패널이 그린 픽셀, boardShown = 패널 밖으로 보이는 보드 픽셀, boardLeft = 보드가 있는 가장 왼쪽 열.</summary>
        void Snap(Camera cam, RenderTexture uiRt, string png, out int covered, out int boardShown, out int boardLeft)
        {
            var tex = Compose(cam, uiRt, out covered, out boardShown, out boardLeft);
            File.WriteAllBytes(Path.Combine(outDir, png), tex.EncodeToPNG());
            Destroy(tex);
        }

        /// <summary>패널 RT의 지금 그림을 같은 크기로 그린 보드 위에 겹친 그림(받은 쪽이 지운다). <see cref="MouseProbe"/>도 창 없는 플레이어에서 이것으로 찍는다.</summary>
        public static Texture2D Compose(Camera cam, RenderTexture uiRt, out int covered, out int boardShown, out int boardLeft)
        {
            int w = uiRt.width, h = uiRt.height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            RenderTexture.active = uiRt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var ui = tex.GetPixels32();
            var boardRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            boardRt.Create();
            OffscreenShot.Render(cam, boardRt, tex);
            OffscreenShot.Render(cam, boardRt, tex);
            var px = tex.GetPixels32();
            covered = 0;
            boardShown = 0;
            boardLeft = w;
            Color32 bg = cam.backgroundColor;
            for (int p = 0; p < px.Length; p++)
            {
                var u = ui[p];
                bool board = Mathf.Abs(px[p].r - bg.r) + Mathf.Abs(px[p].g - bg.g) + Mathf.Abs(px[p].b - bg.b) > 12;
                if (board && p % w < boardLeft) boardLeft = p % w;
                if (u.a > 0) covered++;
                else if (board) boardShown++;
                int keep = 255 - u.a;
                px[p] = new Color32((byte)Mathf.Min(255, u.r + px[p].r * keep / 255), (byte)Mathf.Min(255, u.g + px[p].g * keep / 255), (byte)Mathf.Min(255, u.b + px[p].b * keep / 255), 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            boardRt.Release();
            Destroy(boardRt);
            return tex;
        }

        // 소리(결정 66 ④): 그때의 곡 상황과 곡 음원의 클립(번들 audio에서 읽혔나 — 소리가 나는지는 사람이 듣는다)
        SoundPlayer sound;

        // 단서 시각 로그: 판 번호(새 판 = 새 화면 대기열) · 시퀀서 시각 · 항목 · 행 · 항목 안 시각 · 키 · 클립(audio 번들에서 읽혔나) · 길이
        GameFlow cueFlow;
        object cueFx;
        int cueGame;
        readonly StringBuilder cueLog = new StringBuilder("game\tcueMs\tseq\trow\tatMs\tkey\tclip\tclipMs\tkind\n");
        readonly HashSet<string> cueKeys = new HashSet<string>(), cueMissing = new HashSet<string>();

        void LogCue(SoundCall c)
        {
            var fx = cueFlow != null ? cueFlow.Fx : null;
            if (!ReferenceEquals(fx, cueFx))
            {
                cueFx = fx;
                cueGame++;
            }
            var clip = sound != null ? sound.ClipFor(c.Key) : null;
            if (clip == null) cueMissing.Add(c.Key);
            if (!c.Immediate) cueKeys.Add(c.Key);
            cueLog.Append(cueGame).Append('\t').Append(c.CueMs).Append('\t').Append(c.Seq).Append('\t').Append(c.Row).Append('\t').Append(c.AtMs).Append('\t').Append(c.Key).Append('\t')
                  .Append(clip != null ? clip.name : "").Append('\t').Append(clip != null ? Mathf.RoundToInt(clip.length * 1000f) : 0).Append('\t').Append(c.Immediate ? "now" : "cue").Append('\n');
        }
        readonly List<string> musicNotes = new List<string>();

        void NoteMusic(string at, Sinnoh.Fx.MusicScene want)
        {
            string file = sound != null ? sound.Music.File(want) : null;
            string clip = sound != null && sound.MusicSource.clip != null ? sound.MusicSource.clip.name : "";
            bool ok = sound != null && sound.Scene == want && (file == null ? clip == "" : clip == Path.GetFileNameWithoutExtension(file));
            musicNotes.Add("{\"at\":\"" + at + "\",\"scene\":\"" + want + "\",\"clip\":\"" + clip + "\",\"ok\":" + (ok ? "true" : "false") + "}");
            if (!ok) fails.Add("곡: " + at + "에서 상황 " + (sound != null ? sound.Scene.ToString() : "재생 층 없음") + " · 클립 「" + clip + "」 ≠ " + want + " " + (file ?? "무음") + "(audio 번들)");
        }

        void Finish(StringBuilder sb, int code)
        {
            if (cueFlow != null)
            {
                cueFlow.SoundOnSkip = false;
                foreach (string key in new[] { Sinnoh.Fx.SoundKeys.Land, Sinnoh.Fx.SoundKeys.ActCard, Sinnoh.Fx.SoundKeys.MyTurn, Sinnoh.Fx.SoundKeys.Wipe, Sinnoh.Fx.SoundKeys.BallThrow, Sinnoh.Fx.SoundKeys.Capture })
                    if (!cueKeys.Contains(key)) fails.Add("소리 단서: 지나간 판에 「" + key + "」가 나지 않았다");
                // 찾아보기 「!」는 결정 143 ⑥부터 bang이 아니라 칸에 따라 search(나무) · search_reef(산호) — 남은 bang 행(VS · UB 파괴 · 시련)은 이 혼자 판이 지나지 않는다
                if (!cueKeys.Contains(Sinnoh.Fx.SoundKeys.Search) && !cueKeys.Contains(Sinnoh.Fx.SoundKeys.SearchReef))
                    fails.Add("소리 단서: 지나간 판에 찾아보기(「" + Sinnoh.Fx.SoundKeys.Search + "」 · 「" + Sinnoh.Fx.SoundKeys.SearchReef + "」)가 나지 않았다");
                foreach (string key in cueMissing) fails.Add("소리 단서: 「" + key + "」의 클립을 읽지 못했다(audio 번들)");
            }
            sb.Append(",\"cues\":{\"keys\":").Append(cueKeys.Count).Append(",\"games\":").Append(cueGame).Append(",\"missing\":").Append(cueMissing.Count).Append('}');
            sb.Append(",\"sound\":{\"musicRows\":").Append(sound != null ? sound.Music.Count : -1).Append(",\"seRows\":").Append(sound != null ? sound.Effects.Count : -1)
              .Append(",\"heard\":").Append(sound != null ? sound.Heard.Count : -1).Append(",\"music\":[").Append(string.Join(",", musicNotes)).Append("]}");
            foreach (string e in errors) fails.Add("에러 로그: " + e);
            if (code < 0) code = fails.Count == 0 ? 0 : 1;
            sb.Append(",\"errorLogs\":").Append(errors.Count).Append(",\"ok\":").Append(fails.Count == 0 ? "true" : "false").Append(",\"fails\":[");
            for (int i = 0; i < fails.Count; i++)
                sb.Append(i > 0 ? "," : "").Append('"').Append(fails[i].Replace("\\", "/").Replace("\"", "'").Replace("\n", " ").Replace("\r", " ")).Append('"');
            sb.Append("]}\n");
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "screen-probe.json"), sb.ToString(), new UTF8Encoding(false));
                if (sound != null)
                {
                    // 낸 소리 기록(재생 층 시각 · 종류 · 키 · 클립 · 음량 · 단서 자리) — 사람이 듣기 전의 근거
                    var lines = new StringBuilder("atMs\tkind\tkey\tclip\tvolume\tfrom\n");
                    foreach (var h in sound.Heard)
                        lines.Append(h.AtMs).Append('\t').Append(h.Kind).Append('\t').Append(h.Key).Append('\t').Append(h.Clip ?? "").Append('\t')
                             .Append(h.Volume.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t').Append(h.From).Append('\n');
                    File.WriteAllText(Path.Combine(outDir, "sound-heard.tsv"), lines.ToString(), new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outDir, "cue-log.tsv"), cueLog.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception e) { Debug.LogException(e); }
            Debug.Log("[ScreenProbe] 끝 — 종료 코드 " + code + (fails.Count > 0 ? " (" + string.Join("; ", fails) + ")" : "") + " → " + outDir);
            Application.Quit(code);
        }
    }
}
