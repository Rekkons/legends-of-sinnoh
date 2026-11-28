using System;
using System.Collections.Generic;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 화면 시작점(보드 씬 <c>Scenes/Board.unity</c>의 「Screens」): 플레이어를 켜면 화면 뿌리와 화면 흐름(<see cref="GameFlow"/>)을 만들고 타이틀(S01)을 연다 — 화면 뒤의 보드 전경은 같은 씬의 보드다(04 §1).
    /// 씬이 직렬화로 가리키는 것(추적 파일만 — Gen은 가리키지 않는다, 03 §0):
    /// 실행용 테마(플레이어에서는 실행 중 만든 PanelSettings의 테마가 null이다, 04 §9-6), 코드가 <c>Shader.Find</c>로 찾는 셰이더(주사위 넷 — 씬이 가리켜야 플레이어 빌드에 들어간다),
    /// 화면 문자열 표(<c>Data/Json/strings.ko.json</c> · <c>strings.en.json</c> — S02 「언어」, <see cref="GameFlow.Lang"/>), 주사위 녹화본 묶음(<c>Data/Dice/rolls.bytes</c> — 배틀 독의 굴림판이 재생한다). 게임 데이터(<see cref="Sinnoh.Rules.Ctx"/>)는 같은 씬의 <see cref="BoardBoot"/>에서 받는다.
    /// 프레임마다 흐름의 세션 시각을 흘리고(<see cref="GameFlow.Tick"/>) 창 크기에 맞춰 보드 전경을 놓고(<see cref="GameFlow.Layout"/>) 마우스 자리·누름을 넘긴다(<see cref="GameFlow.Pointer"/> — 보드의 칸 고르기).
    /// 프레임 측정(<see cref="FrameProbe"/>)으로 띄운 플레이어에서는 화면을 만들지 않는다(보드만 잰다).
    /// </summary>
    public sealed class ScreenBoot : MonoBehaviour
    {
        [SerializeField] ThemeStyleSheet theme;
        [SerializeField] Shader[] buildShaders;
        [SerializeField] TextAsset stringsJson;
        [SerializeField] TextAsset diceRolls;
        // 영어 화면 문자열 표(Data/Json/strings.en.json — S02 「언어」, M9 언어 ②). 처음 고를 때 읽는다
        [SerializeField] TextAsset stringsEnJson;
        // 튜토리얼(M6.5 「화면 ② 목차·입구」): 목차의 장 머리(Data/Json/tutorial_chapters.json)와 각본(Data/Json/tutorial/*.json) — 플레이어는 씬이 가리키는 것만 읽는다
        [SerializeField] TextAsset tutorialChapters;
        [SerializeField] TextAsset[] tutorialScripts;
        // 룰 설명서 S43 · 크레딧 S03(M9): Data/Json/rulebook.<언어>.json · credits.json — 플레이어는 씬이 가리키는 것만 읽는다(GameFlow.ReadBook)
        [SerializeField] TextAsset[] bookJson;

        /// <summary>만든 화면 뿌리. Gen 겉모양이 없거나 프레임 측정 중이면 null.</summary>
        public ScreenRoot Root { get; private set; }

        /// <summary>화면 흐름(타이틀 → 혼자 하기 → 게임 준비 → 보드). 화면 뿌리가 없으면 null.</summary>
        public GameFlow Flow { get; private set; }

        public ThemeStyleSheet Theme => theme;
        public IReadOnlyList<Shader> BuildShaders => buildShaders;
        public TextAsset StringsJson => stringsJson;
        public TextAsset StringsEnJson => stringsEnJson;
        public TextAsset DiceRolls => diceRolls;
        public TextAsset TutorialChaptersJson => tutorialChapters;
        public IReadOnlyList<TextAsset> TutorialScripts => tutorialScripts;
        public IReadOnlyList<TextAsset> BookJson => bookJson;

        /// <summary>소리 재생 층(결정 66 ④ — 프레임마다 흐름의 곡 상황·음량을 옮기고 소리 부름을 받는다). 화면 뿌리가 없으면 null.</summary>
        public SoundPlayer Sound { get; private set; }

        /// <summary>
        /// 음량 설정을 둘 곳: 플레이어는 엔진의 PlayerPrefs(<see cref="StoredSoundPrefs"/> — 창 크기와 같은 곳). 에디터(플레이 시험)와 확인용으로 띄운 플레이어(<see cref="ScreenProbe"/>·<see cref="MouseProbe"/>)는
        /// 실행하는 동안만 쥐는 값 — 시험·확인이 사용자의 음량을 바꾸지 않게(<see cref="DefaultSaves"/>와 같은 규칙).
        /// </summary>
        public static SoundPrefs DefaultSoundPrefs()
        {
            var args = Environment.GetCommandLineArgs();
            if (Application.isEditor || Array.IndexOf(args, ScreenProbe.Arg) >= 0 || Array.IndexOf(args, MouseProbe.Arg) >= 0 || Array.IndexOf(args, UpdateProbe.Arg) >= 0) return new SoundPrefs();
            return new StoredSoundPrefs();
        }

        /// <summary>
        /// 첫 화면의 언어(결정 146 ④ [가정]): 저장한 언어(S02 「언어」 — <see cref="SoundPrefs.Lang"/>)가 있으면 그것, 없으면 OS 언어 — 한국어면 <c>ko</c>, 그 밖은 <c>en</c>.
        /// 고른 값은 저장하지 않는다(설정에서 바꿀 때만 — <see cref="GameFlow.Lang"/>).
        /// </summary>
        public static string LangOf(SystemLanguage os, string saved) => SoundPrefs.ClampLang(saved) ?? (os == SystemLanguage.Korean ? "ko" : "en");

        /// <summary>마우스 자리·누름을 프레임마다 흐름에 넘기나. 창 없는 확인·시험은 끄고 <see cref="GameFlow.Pointer"/>를 직접 부른다(패널을 RT에 그리면 창의 마우스 좌표와 크기가 다르다).</summary>
        public bool PointerFromMouse { get; set; } = true;

        /// <summary>막 안내 창을 저절로 띄우나(null = 사람이 하는 플레이어에서만 — 에디터 · 확인용 프로브는 끔). 시험·캡처가 값을 박는다.</summary>
        public bool? ActNotice { get; set; }

        static readonly bool HumanPlayer = !Application.isEditor && Array.IndexOf(Environment.GetCommandLineArgs(), ScreenProbe.Arg) < 0 && Array.IndexOf(Environment.GetCommandLineArgs(), MouseProbe.Arg) < 0;

        /// <summary>게임 시드를 주는 곳(null = 흐름의 기본 — 시각과 Guid). 흐름을 만들기(<c>Start</c>) 전에 넣는다 — 플레이어 확인(<see cref="MouseProbe"/>)이 같은 판을 다시 보려고 쓴다.</summary>
        public Func<ulong> Entropy { get; set; }

        /// <summary>
        /// 저장 폴더(<see cref="GameFlow"/>의 saves). 흐름을 만들기(<c>Start</c>) 전에 넣는다. 넣지 않으면 <see cref="DefaultSaves"/> — 에디터(플레이 시험)와 확인용으로 띄운 플레이어는 저장하지 않는다.
        /// </summary>
        public string SaveFolder { get; set; }

        /// <summary>저장 폴더를 정하는 플레이어 인자: <c>-sinnohSaves &lt;폴더&gt;</c>(확인용 — 사용자의 저장 폴더 대신 그 폴더에 쓴다).</summary>
        public const string SavesArg = "-sinnohSaves";

        /// <summary>
        /// 기본 저장 폴더(02 §7): 플레이어는 <c>persistentDataPath/saves</c>. <see cref="SavesArg"/>가 있으면 그 폴더.
        /// 에디터(플레이 시험 — 에디터와 플레이어는 persistentDataPath가 같다)와 확인용으로 띄운 플레이어(<see cref="ScreenProbe"/>·<see cref="MouseProbe"/>)는 null(저장 안 함) —
        /// 시험·확인이 사용자의 저장 폴더에 쓰지 않게.
        /// </summary>
        public static string DefaultSaves()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, SavesArg);
            if (at >= 0 && at + 1 < args.Length) return args[at + 1];
            if (Application.isEditor || Array.IndexOf(args, ScreenProbe.Arg) >= 0 || Array.IndexOf(args, MouseProbe.Arg) >= 0) return null;
            return System.IO.Path.Combine(Application.persistentDataPath, "saves");
        }

        /// <summary>
        /// 확인용(플레이 시험): 화면 흐름을 그 저장 폴더에 쓰는 것으로 다시 만들고 타이틀을 연다(하던 게임은 떠난다). 에디터의 흐름은 기본이 저장 안 함이라
        /// 저장·이어 받기를 보는 시험이 임시 폴더를 넣는 데 쓴다. 게임 화면은 부르지 않는다.
        /// </summary>
        public void UseSaves(string folder, Func<long> clock = null)
        {
            if (Flow == null) throw new InvalidOperationException("화면 흐름이 아직 없다");
            Flow.OpenTitle();
            SaveFolder = folder;
            Flow = MakeFlow(folder, clock);
            Flow.OpenTitle();
        }

        /// <summary>
        /// 튜토리얼 기록(`tutorial.last`·`tutorial.seen.&lt;장&gt;` — 07 [가정 T4·T40])을 둘 폴더: 플레이어는 <c>persistentDataPath</c>(저장 폴더 <c>saves/</c> 밖 — 파일 <see cref="Sinnoh.Data.TutorialRecord.FileName"/>).
        /// 에디터(플레이 시험)와 확인용으로 띄운 플레이어(<see cref="ScreenProbe"/>·<see cref="MouseProbe"/> · <see cref="SavesArg"/>)는 null — 실행하는 동안만 쥔다(사용자 기록을 바꾸지 않게).
        /// </summary>
        public static string DefaultTutorialFolder()
        {
            var args = Environment.GetCommandLineArgs();
            if (Application.isEditor || Array.IndexOf(args, SavesArg) >= 0 || Array.IndexOf(args, ScreenProbe.Arg) >= 0 || Array.IndexOf(args, MouseProbe.Arg) >= 0) return null;
            return Application.persistentDataPath;
        }

        // 튜토리얼 데이터 읽기: 씬이 가리키는 TextAsset(Data/Json 아래 경로 → 글)
        string TutorialJson(string file)
        {
            if (file == TutorialMenu.ChaptersFile) return tutorialChapters != null ? tutorialChapters.text : null;
            const string dir = "tutorial/";
            if (tutorialScripts != null && file.StartsWith(dir, StringComparison.Ordinal) && file.EndsWith(".json", StringComparison.Ordinal))
            {
                string id = file.Substring(dir.Length, file.Length - dir.Length - ".json".Length);
                foreach (var t in tutorialScripts)
                    if (t != null && t.name == id) return t.text;
            }
            return null;
        }

        /// <summary>플레이 기록 올리기(02 §12)를 이 실행에만 알림 없이 켜는 플레이어 인자(저장하지 않는다). 설정 값은 <see cref="SoundPrefs.PlayUpload"/>(기본 켜짐 · 첫 실행 알림 뒤부터 — 결정 126 ④).</summary>
        public const string PlayUploadArg = "-sinnohPlayUpload";

        /// <summary>올리는 창구(플레이 기록 · 버그 신고)를 만드나: 플레이어만 — 에디터는 만들지 않는다(창구가 없으면 첫 실행 알림 · 설정 줄이 없고 버그 신고는 「PC판에서만」).</summary>
        public static bool MakesUploads(bool editor) => !editor;

        GameFlow MakeFlow(string saves, Func<long> clock)
        {
            var flow = new GameFlow(Root.Stage, madeBoard != null ? madeBoard.Ctx : null, madeBoard, madeRig, Entropy, dice: madeDice, saves: saves, clock: clock, sound: soundPrefs);
            // 플레이 기록 올리기: 플레이어만 — 에디터(플레이 시험)는 창구를 만들지 않는다. 설정이 꺼져 있거나 첫 실행 알림을 보기 전이면 UGS를 부르지 않는다
            if (MakesUploads(Application.isEditor))
            {
                var prefs = soundPrefs;
                var args = Environment.GetCommandLineArgs();
                if (Array.IndexOf(args, ScreenProbe.Arg) >= 0 || Array.IndexOf(args, MouseProbe.Arg) >= 0 || Array.IndexOf(args, UpdateProbe.Arg) >= 0)
                {
                    prefs.PlayNotice = true;   // 확인용 실행은 첫 실행 알림을 띄우지 않는다(실행하는 동안만 쥐는 값 — DefaultSoundPrefs)
                    flow.Uploads = PlayUploads.Quiet(() => false);   // 확인용 창구: 서버를 부르지 않는다(설정 줄 · 지우기만 지난다)
                }
                else
                {
                    bool forced = Array.IndexOf(args, PlayUploadArg) >= 0;
                    flow.Uploads = new PlayUploads(new Sinnoh.Net.UgsPlayUploader(), Application.version, () => forced || PlayUploads.Allowed(prefs));
                }
            }
            if (tutorialChapters != null) flow.Tutorial.ReadJson = TutorialJson;
            if (bookJson != null && bookJson.Length > 0) flow.ReadBook = file => Array.Find(bookJson, t => t != null && t.name + ".json" == file)?.text;
            flow.Tutorial.Record = Sinnoh.Data.TutorialRecord.Open(DefaultTutorialFolder());
            if (Sound != null)
            {
                var sound = Sound;
                flow.Sound += call => sound.Play(call);
                flow.CryForm = key => sound.Cries.Form(key);   // 폼 울음(결정 143 ④)
                sound.Use(flow.SoundPrefs);
            }
            return flow;
        }

        SoundPrefs soundPrefs;

        BoardBoot madeBoard;
        BoardCameraRig madeRig;
        Sinnoh.Fx.DiceLibrary madeDice;
        double carryMs;

        void Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), FrameProbe.Arg) >= 0) return;
            if (stringsJson == null)
            {
                Debug.LogWarning("[ScreenBoot] 씬이 화면 문자열 표(strings.ko.json)를 가리키지 않아 화면을 열지 않는다");
                return;
            }
            Root = ScreenRoot.Create(theme, StringTable.Load(stringsJson.text));
            if (Root == null)
            {
                Debug.LogWarning("[ScreenBoot] 겉모양(Gen 글꼴·창틀)을 읽지 못해 화면을 열지 않는다 — rebuild-all, 플레이어는 BoardBuild의 ui 번들");
                return;
            }
            Root.transform.SetParent(transform, false);
            // 언어(S02 「언어」 — M9 언어 ②): 한국어 표는 지금 읽은 것, 영어 표는 처음 고를 때 읽는다
            var ko = Root.Stage.Strings;
            StringTable en = null;
            Root.Stage.Tables = lang => lang == "ko" ? ko : lang == "en" && stringsEnJson != null ? en ??= StringTable.Load(stringsEnJson.text) : null;
            var cam = Camera.main;
            var rig = cam != null ? cam.GetComponent<BoardCameraRig>() : null;
            var board = FindAnyObjectByType<BoardBoot>();
            Sinnoh.Fx.DiceLibrary dice = null;
            if (diceRolls != null) dice = Sinnoh.Fx.DiceLibrary.Parse(diceRolls.bytes);
            else Debug.LogWarning("[ScreenBoot] 씬이 주사위 녹화본 묶음(rolls.bytes)을 가리키지 않아 굴림판에 주사위가 구르지 않는다");
            madeBoard = board;
            madeRig = rig;
            madeDice = dice;
            soundPrefs = DefaultSoundPrefs();
            // 첫 실행 언어(결정 146 ④ [가정]): 사람이 하는 플레이어만 — 에디터 · 확인용 프로브는 PC 언어와 무관하게 한국어 표로 시작한다(시험 · 캡처가 OS에 기대지 않게)
            if (HumanPlayer) Root.Stage.UseLang(LangOf(Application.systemLanguage, soundPrefs.Lang));
            Sound = SoundPlayer.Create(transform);
            Flow = MakeFlow(SaveFolder ?? DefaultSaves(), null);
            // 자동 업데이트(결정 161): 사람이 하는 PC 플레이어만 — 도우미가 바꾸는 중이면 바로 끝, 도우미 결과(result.json)를 읽어 정리 · 실패면 타이틀에 알림
            bool pc = HumanPlayer;
            var probe = pc ? UpdateProbe.Open(out bool refused) : null;
            if (pc && probe == null && UpdateProbe.On)
            {
                Application.Quit();
                return;
            }
            UpdateResult result = null;
            if (pc && !UpdateBoot(probe, out result)) return;
            Flow.OpenTitle();
            if (result != null && !(result.Ok && result.Version == Application.version)) Flow.UpdateFailedBefore(result);
            // 새 판 알림(결정 148 ①): 사람이 하는 PC 플레이어만(에디터 · 확인용 프로브는 안 함), 판 정보 주소가 정해졌을 때 한 번 — 실패는 조용히
            // 끝에서 끝 프로브(4-4)는 -sinnohFeed의 loopback 주소 + 시험 키로 읽는다
            string feed = probe != null ? UpdateProbe.Feed() ?? Sinnoh.Data.ReleaseFeed.Url : Sinnoh.Data.ReleaseFeed.Url;
            if (probe != null)
            {
                probe.Attach(Flow);
                if (UpdateProbe.Feed() != null)
                {
                    Flow.FeedKey = UpdateProbe.TestKey();
                    Flow.FeedLoopback = true;
                }
            }
            Flow.FeedUrl = feed;
            if (pc && !string.IsNullOrEmpty(feed)) StartCoroutine(CheckRelease(feed, probe));
        }

        // 판 정보 파일(latest.json)을 받아 흐름에 넘긴다 — 서명 · 판 견주기는 GameFlow.TakeRelease(ReleaseFeed). 받지 못하면 로그 한 줄
        System.Collections.IEnumerator CheckRelease(string url, UpdateProbe probe)
        {
            using (var req = UnityEngine.Networking.UnityWebRequest.Get(url))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Debug.Log("[Release] 판 정보를 받지 못함 — " + req.result);
                    probe?.Released(Flow, "get");
                    yield break;
                }
                Flow?.TakeRelease(req.downloadHandler.text);
                probe?.Released(Flow, Flow?.NewVersion == null ? "none" : null);
            }
        }

        // 켤 때 자동 업데이트 몫(결정 161 4-2 ① ②): 도우미가 이 설치를 바꾸는 중이면(뮤텍스) 로그 한 줄 + 바로 끝(false). 도우미 결과를 읽어 정리한다(UpdateHandoff.Tidy) — 프로브면 그것을 적고 끝
        bool UpdateBoot(UpdateProbe probe, out UpdateResult result)
        {
            result = null;
            string install = Flow.InstallDir;
            if (install == null) return true;
            if (HelperRunning(install))
            {
                Debug.Log("[Update] 업데이트 도우미가 이 설치를 바꾸는 중 — 끝낸다");
                Application.Quit();
                return false;
            }
            try
            {
                result = UpdateHandoff.ReadResult(install);
                UpdateHandoff.Tidy(install, Application.version, result);
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                Debug.Log("[Update] 정리 실패 — " + e.Message);
            }
            if (result != null) Debug.Log("[Update] 도우미 결과 " + result.Version + " ok=" + result.Ok + " " + result.Why + " (지금 " + Application.version + ")");
            if (probe != null && result != null)
            {
                probe.Note("result", result.Ok && result.Version == Application.version, result.Ok ? result.Version : result.Why);
                probe.Saves(Flow);
                Application.Quit();
                return false;
            }
            return true;
        }

        static bool HelperRunning(string install)
        {
            try
            {
                if (!System.Threading.Mutex.TryOpenExisting(UpdateHandoff.MutexName(install), out var m)) return false;
                m.Dispose();
                return true;
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is System.IO.IOException || e is NotSupportedException || e is ArgumentException)
            {
                return e is UnauthorizedAccessException;   // 있지만 열 권한이 없다 = 잡혀 있다
            }
        }

        void Update()
        {
            if (Flow == null) return;
            carryMs += Time.unscaledDeltaTime * 1000.0;
            int dt = (int)carryMs;
            carryMs -= dt;
            Flow.Tick(dt);
            Flow.Layout(Root.Size.x, Root.Size.y);
            // 소리: 곡 상황 → 교차 페이드, 음량·더킹을 음원에(소리 단서는 Tick 안에서 이미 왔다)
            Sound.Want(Flow.Music());
            Sound.Tick(dt);
            // 단축키 T: 정비 판(04 §3-5)
            var keys = Keyboard.current;
            if (keys != null && keys.tKey.wasPressedThisFrame && !Flow.Typing) Flow.ToggleParty();
            // 단축키 Y: 타입 상성 · 상태 이상 창(플레이 시험 T5 — 어느 화면에서든, 위 띠 [상성]이 빠진 좌석 다섯 이상의 1080 벌에서도)
            if (keys != null && keys.yKey.wasPressedThisFrame && !Flow.Typing) Flow.ToggleTypes();
            // 막 안내 창(결정 101 ②)이 저절로 뜨는 것은 사람이 하는 판만: 플레이어(에디터 플레이 시험 · 확인용 프로브가 아닌 것)에서 마우스로 할 때
            Flow.ActNotice = ActNotice ?? (PointerFromMouse && HumanPlayer);
            // Esc: 맨 위의 창을 닫는다(결정 80 N3 나 — 창의 [닫기]와 같은 길), 창이 없으면 걷기 전 확인의 고른 칸을 푼다(결정 70 ⑱)
            if (keys != null && keys.escapeKey.wasPressedThisFrame) Flow.Escape();
            // 스페이스 바: 진화 창의 진화 연출을 착지로 건너뛴다(결정 84 L11)
            if (keys != null && keys.spaceKey.wasPressedThisFrame && !Flow.Typing) Flow.SkipKey();
            // Ctrl+Z: 한 수 무르기(결정 149 — 보드 HUD [무르기]가 켜져 있고 창이 없을 때만)
            if (keys != null && keys.zKey.wasPressedThisFrame && (keys.leftCtrlKey.isPressed || keys.rightCtrlKey.isPressed) && !Flow.Typing) Flow.UndoKey();
            // 배틀 독의 단축키(결정 164 — 단추 글자와 같다): 숫자 1~3 = 기술 줄(효과 순서 줄 — 결정 72) · Q = [가방] · E = [기권]. 배틀 화면이 맨 위일 때만(GameFlow.BattleKey)
            if (keys != null)
            {
                if (keys.digit1Key.wasPressedThisFrame) Flow.BattleKey('1');
                else if (keys.digit2Key.wasPressedThisFrame) Flow.BattleKey('2');
                else if (keys.digit3Key.wasPressedThisFrame) Flow.BattleKey('3');
                if (keys.qKey.wasPressedThisFrame) Flow.BattleKey('Q');
                if (keys.eKey.wasPressedThisFrame) Flow.BattleKey('E');
            }
            var mouse = Mouse.current;
            if (PointerFromMouse && mouse != null)
            {
                var at = mouse.position.ReadValue();
                // Alt를 누른 채 칸을 클릭 = 핑 찍기(방 판 — 결정 97)
                Flow.PingKey = keys != null && (keys.leftAltKey.isPressed || keys.rightAltKey.isPressed);
                // 왼쪽: 끌면 보드 옮기기 · 끌지 않고 떼면 칸 누르기. 가운데·오른쪽: 끌기만(결정 66 ②). 한 프레임 안에 누르고 뗀 것도 놓치지 않는다
                Flow.Pointer(at.x, at.y, mouse.leftButton.isPressed || mouse.leftButton.wasPressedThisFrame, mouse.middleButton.isPressed || mouse.rightButton.isPressed);
            }
        }
    }
}
