using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules.Session;
using Sinnoh.Rules.Tutorial;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 튜토리얼의 입구·목차·장 고르기·건너뛰기·끝내기(M6.5 「화면 ② 목차·입구」, 07 §2 · §9-3)와 튜토리얼 층(화면 ① <see cref="TutorialLayer"/>)을 잇는 한 곳(07 T60). 화면 흐름(<see cref="GameFlow.Tutorial"/>) 하나에 하나.
    /// <list type="bullet">
    /// <item>타이틀 [튜토리얼](<see cref="Open"/>): 기록이 없으면 본편 첫 장으로 바로, 있으면 목차(<see cref="TutorialMenuScreen"/>).</item>
    /// <item>각본 열기(장 고르기 <see cref="Start"/> · [이 장 건너뛰기] <see cref="SkipChapter"/> · 이어 여는 각본 · 확인용 <see cref="Play"/>가 모두 같은 길): 진행기를 새로 차려 앞 단계를 연출 없이 보내고
    /// (<see cref="TutorialRunner.PlayWhile"/> — 판이 결정적이라 그 국면이 된다, 국면 스냅샷 없음 · 막 앞 판 각본은 손보기가 걸릴 때까지의 판 차리기 단계도 보낸다 — 07 T62)
    /// 화면 흐름에 넘기고(<see cref="TutorialRunner.Hand"/> → <see cref="GameFlow.Adopt"/> — 진행기와 화면이 손님 하나를 같이 쓰고 시계를 맞춘다, 판은 저장하지 않는다 — 결정 90 Q6)
    /// 그 판에 층을 붙인다(<see cref="Layer"/> — 말풍선 [이 장 건너뛰기 ▶▶] → <see cref="SkipChapter"/> · [튜토리얼 끝내기] → <see cref="OpenQuit"/>).</item>
    /// <item>장 하나 = 각본 여럿(07 T54 — 본편 7장 = main의 7장 단계 → preview2 → preview3, <see cref="TutorialChapter.Then"/>): 각본이 끝나면 그 장의 다음 각본을 그 각본의 start로 새로 연다(<see cref="Tick"/>).</item>
    /// <item>[이 장 건너뛰기 ▶▶]: 같은 길로 그 장 끝까지 보낸다 — 다음 장 시작 국면(마지막 장이면 끝 창). 각본 여럿인 장은 마지막 각본의 끝. 건너뛴 장은 완료가 아니다(결정 90 Q7 ①).</item>
    /// <item>끝내기 창(<see cref="OpenQuit"/> — 말풍선 [튜토리얼 끝내기]와 게임 메뉴의 [튜토리얼 끝내기]가 같은 창): [타이틀로] · [목차로] · [계속하기].</item>
    /// <item>끝 창(장의 마지막 각본의 마지막 단계 뒤 — <see cref="Tick"/>이 본다): 본편 [목차로] · [타이틀로], 확장 장 [목차로](결정 90 Q7 ③ 나).</item>
    /// <item>기록(<see cref="Record"/> — `tutorial.last`·`tutorial.seen.&lt;장&gt;`, [가정 T4]): 장에 들어가면 last, 그 장의 마지막 단계(각본 여럿이면 마지막 각본의 것)를 화면에서 끝내면 seen.</item>
    /// </list>
    /// 끝내기 창·끝 창은 무대의 화면(<see cref="UiStage.Current"/> — 조우 창·배틀 화면·셋업일 수 있다)을 닫지 않고 그 위에 덮는 창이다(툴팁 층 바로 아래 — 튜토리얼 층·말풍선 위).
    /// 떠 있는 동안 화면 대기열은 서 있고 보드 입력·Esc 밖의 단축키는 받지 않는다(<see cref="GameFlow.InputOpen"/>·<see cref="GameFlow.MenuUp"/>). Esc = [계속하기].
    /// </summary>
    public sealed class TutorialMenu
    {
        public const string ChaptersFile = "tutorial_chapters.json";

        readonly GameFlow flow;
        Func<string, string> readJson;
        List<TutorialChapter> chapters;
        bool chaptersRead;
        readonly Dictionary<string, TutorialScript> scripts = new Dictionary<string, TutorialScript>();
        TutorialScript script;
        int tracked;
        bool ended;
        VisualElement dialog;
        RulebookScreen book;
        VisualElement bookElement;
        TutorialRecord record = TutorialRecord.Memory();

        public TutorialMenu(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
#if UNITY_EDITOR
            readJson = EditorJson;
#endif
        }

        /// <summary>
        /// 생성 데이터 읽기: `Data/Json` 아래 경로(<see cref="ChaptersFile"/> · `tutorial/&lt;각본&gt;.json`) → 글(없으면 null). 에디터는 에셋 데이터베이스에서 읽고,
        /// 플레이어는 보드 씬의 <see cref="ScreenBoot"/>가 씬이 가리키는 TextAsset으로 넣는다. 바꾸면 읽어 둔 장 목록·각본을 버린다.
        /// </summary>
        public Func<string, string> ReadJson
        {
            get => readJson;
            set
            {
                readJson = value;
                chapters = null;
                chaptersRead = false;
                scripts.Clear();
            }
        }

        /// <summary>튜토리얼 기록. 기본은 실행하는 동안만(에디터·캡처) — 플레이어는 <see cref="ScreenBoot"/>가 설정 쪽 파일을 넣고, 시험은 임시 폴더를 넣는다.</summary>
        public TutorialRecord Record
        {
            get => record;
            set => record = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>목차의 장(본편 → 확장 장). 데이터를 읽지 못하면 null(타이틀 [튜토리얼]이 꺼진다).</summary>
        public IReadOnlyList<TutorialChapter> Chapters
        {
            get
            {
                if (!chaptersRead)
                {
                    chaptersRead = true;
                    string json = readJson?.Invoke(ChaptersFile);
                    if (json != null)
                    {
                        try { chapters = TutorialChapters.Load(json, ChaptersFile); }
                        catch (FormatException e) { Debug.LogError("[Tutorial] " + e.Message); }
                    }
                }
                return chapters;
            }
        }

        /// <summary>타이틀 [튜토리얼]을 켤 수 있나(게임 데이터와 장 목록이 있다).</summary>
        public bool Available => flow.Ctx != null && Chapters != null && TutorialChapters.Find(Chapters, FirstChapter) != null;

        /// <summary>기록이 없을 때 바로 가는 장(본편 첫 장).</summary>
        public const string FirstChapter = "main0";

        /// <summary>타이틀 「처음이라면!」 딱지(07 T3): 본편 마지막 장을 끝까지 보기 전까지.</summary>
        public bool FirstTime => Chapters == null || record.FirstTime(TutorialChapters.LastMain(Chapters));

        /// <summary>지금 도는 튜토리얼의 진행기(없으면 null — 화면 흐름에 넘긴 것, <see cref="TutorialRunner.Hand"/>).</summary>
        public TutorialRunner Runner { get; private set; }

        /// <summary>지금 진행기에 붙은 튜토리얼 층(각본이 끝났거나 튜토리얼 밖이면 null).</summary>
        public TutorialLayer Layer { get; private set; }

        /// <summary>지금 도는 각본(없으면 null).</summary>
        public TutorialScript Script => Runner != null ? script : null;

        public bool Running => Runner != null;

        /// <summary>지금 단계가 든 장(끝났으면 마지막 단계의 장). 튜토리얼 밖이거나 장 머리 표에 없는 각본(확인용 <see cref="Play"/>)이면 null.</summary>
        public TutorialChapter Chapter => Runner == null ? null : ChapterAt(Math.Min(Runner.Index, script.Steps.Count - 1));

        /// <summary>끝내기 창이 떠 있나.</summary>
        public bool Quitting { get; private set; }

        /// <summary>끝 창이 떠 있나.</summary>
        public bool Ending { get; private set; }

        /// <summary>끝내기 창이나 끝 창이 떠 있다 — 화면 흐름이 보드 입력·대기열·창 열기를 세운다.</summary>
        public bool DialogUp => dialog != null;

        /// <summary>떠 있는 창의 요소(끝내기 창 <c>tut-quit</c> · 끝 창 <c>tut-end</c>, 없으면 null).</summary>
        public VisualElement Dialog => dialog;

        // ── 설명서에서 더 보기(M9 — 07 T212 · T220) ──

        /// <summary>끝 창 · 목차 위에 덮은 룰 설명서 큰 창(없으면 null).</summary>
        public RulebookScreen Book => book;

        /// <summary>룰 설명서 큰 창이 끝 창 · 목차 위에 떠 있나.</summary>
        public bool BookUp => book != null;

        /// <summary>그 장의 설명서 절(장 머리 표의 절 키 순서 — 지금 언어의 설명서에 있는 것만). 설명서 데이터가 없으면 빈 목록(단추를 달지 않는다).</summary>
        public IReadOnlyList<RulebookSection> BookOf(TutorialChapter ch)
        {
            var list = new List<RulebookSection>();
            var data = ch != null ? flow.Book(flow.BookLang) : null;
            if (data == null) return list;
            foreach (string key in ch.Rulebook)
            {
                var sec = data.Find(key);
                if (sec != null) list.Add(sec);
            }
            return list;
        }

        /// <summary>
        /// 「설명서에서 더 보기」: 끝 창이나 목차 위에 룰 설명서 큰 창(S43 B)을 그 절로 덮어 연다 — 밑의 끝 창 · 목차는 그대로 두므로 닫으면(<see cref="CloseBook"/> · [닫기] · Esc) 그 화면으로 돌아온다.
        /// 끝 창 · 목차가 아니거나 이미 떠 있거나 그 절이 설명서에 없으면 false.
        /// </summary>
        public bool OpenBook(string key)
        {
            if (book != null || key == null || !(Ending || flow.Stage.Current is TutorialMenuScreen)) return false;
            var data = flow.Book(flow.BookLang);
            var root = flow.Stage.Root;
            if (data == null || data.Find(key) == null || root == null) return false;
            flow.Stage.Tips?.Hide();
            book = new RulebookScreen(data, flow.Ctx, flow.BookLang, key, CloseBook, true);
            bookElement = book.Build(flow.Stage);
            bookElement.name = book.Name;
            bookElement.AddToClassList("sn-screen");
            root.Add(bookElement);
            var tips = flow.Stage.Tips;
            if (tips != null && tips.Element.parent == root) bookElement.PlaceBehind(tips.Element);   // 툴팁 층 바로 아래 — 끝 창 위
            return true;
        }

        /// <summary>덮어 연 룰 설명서를 닫는다(밑의 끝 창 · 목차가 다시 보인다).</summary>
        public void CloseBook()
        {
            if (book == null) return;
            flow.Stage.Tips?.Hide();
            bookElement.RemoveFromHierarchy();
            bookElement = null;
            var was = book;
            book = null;
            was.Close();
        }

        // ── 입구 ──

        /// <summary>타이틀 [튜토리얼](07 §2-2): 기록이 하나도 없으면 본편 첫 장으로 바로, 있으면 목차.</summary>
        public void Open()
        {
            if (record.Any) flow.OpenTutorialMenu();
            else Start(FirstChapter);
        }

        /// <summary>
        /// 그 장의 처음부터 시작한다(목차의 장 줄 · [이어서] — 판을 저장하지 않으므로 늘 장 시작부터). 고를 수 없는 장(단계 0)이거나 각본이 판과 어긋나면 false(목차 그대로).
        /// </summary>
        public bool Start(string chapterId)
        {
            var ch = Chapters != null ? TutorialChapters.Find(Chapters, chapterId) : null;
            if (ch == null || !ch.Ready) return false;
            return Launch(ch.Script, s => ch.IsMain && s.Chapter < ch.Chapter, null);
        }

        /// <summary>
        /// 확인용(시험·캡처): 장 머리 표 밖의 각본(손본 견본 — 봇 좌석 각본 · 앵커를 바꾼 각본)을 처음부터 같은 길로 연다. 장이 없으므로 건너뛰기·기록·이어 여는 각본은 없다.
        /// </summary>
        public bool Play(TutorialScript s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            return Launch(s, null, null);
        }

        /// <summary>
        /// [이 장 건너뛰기 ▶▶](07 §2-2): 지금 장의 끝까지 연출 없이 보낸다 — 판을 처음부터 다시 차려 그 장까지 명령을 보낸 국면(판이 결정적이라 화면에서 한 것과 같은 곳 —
        /// 「자유」 단계에서 각본 밖으로 둔 수는 버린다). 다음 장이 있으면 그 장 시작, 없으면(마지막 장·확장 장) 각본 끝 → 끝 창. 각본 여럿인 장(<see cref="TutorialChapter.Then"/>)은
        /// 그 장의 마지막 각본 끝(07 T64). 건너뛴 장은 완료가 아니다. 튜토리얼 밖이면 false.
        /// </summary>
        public bool SkipChapter()
        {
            if (Runner == null || Runner.Finished || DialogUp) return false;
            var cur = Chapter;
            if (cur == null) return false;
            if (cur.Then.Count > 0) return Launch(cur.Then[cur.Then.Count - 1], s => true, cur);
            return Launch(script.Id, s => cur.Holds(s.Chapter) || (cur.IsMain && s.Chapter < cur.Chapter), cur);
        }

        bool Launch(string scriptId, Func<TutorialStep, bool> forward, TutorialChapter skipped)
        {
            TutorialScript s;
            try { s = ScriptOf(scriptId); }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is FormatException)
            {
                Debug.LogError("[Tutorial] " + scriptId + (skipped != null ? " 건너뛰기 " + skipped.Id : "") + ": " + e.Message);
                return false;
            }
            return Launch(s, forward, skipped);
        }

        // 각본을 여는 한 길(07 T60): 진행기를 차려 forward인 동안 보내고(막 앞 판 각본은 손보기가 걸릴 때까지도 — T62) 화면 흐름에 넘긴 뒤 층을 붙인다
        bool Launch(TutorialScript s, Func<TutorialStep, bool> forward, TutorialChapter skipped)
        {
            TutorialRunner runner = null;
            try
            {
                runner = new TutorialRunner(flow.Ctx, s, new PrincipleAgent(), SoloTrail.AfterNamed);
                bool prelude = s.Start.After != null;
                runner.PlayWhile(st => (forward != null && forward(st)) || (prelude && !runner.Rebased));
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is FormatException)
            {
                Debug.LogError("[Tutorial] " + s.Id + (skipped != null ? " 건너뛰기 " + skipped.Id : "") + ": " + e.Message);
                return false;
            }
            CloseDialog();
            StopLayer();
            Runner = runner;
            script = s;
            tracked = runner.Index;
            ended = false;
            var at = ChapterAt(Math.Min(runner.Index, s.Steps.Count - 1));
            if (at != null && !runner.Finished) record.Enter(at.Id);
            ulong seed = s.Start.Seed;
            // 여럿 판의 봇 좌석은 이름에 맞는 Pt 인물(결정 96 Q5 나 — 좌석 1 용식 = 라이벌 · 좌석 2 빛나 = 여자 주인공): 좌석 칩 얼굴 · 보드 말 · 봇 턴 띠가 같은 인물
            var who = s.Start.Seats > 1 ? Protagonists.ForTutorial(flow.Settings.Protagonist, s.Start.Seats) : null;
            runner.Hand((session, ms) => flow.Adopt(session, seed, ms, who), () => flow.NowMs, flow.CatchUp);
            if (runner.Finished) ShowEnd();
            else
            {
                Layer = TutorialLayer.Open(flow, runner);
                Layer.SkipPressed += _ => SkipChapter();
                Layer.QuitPressed += () => OpenQuit();
            }
            return true;
        }

        TutorialScript ScriptOf(string id)
        {
            if (scripts.TryGetValue(id, out var s)) return s;
            string file = "tutorial/" + id + ".json";
            string json = readJson?.Invoke(file) ?? throw new InvalidOperationException("각본 " + file + "이 없다");
            s = TutorialLoader.Load(json, flow.Ctx, file);
            scripts[id] = s;
            return s;
        }

        TutorialChapter ChapterAt(int index) =>
            script == null || Chapters == null || index < 0 || index >= script.Steps.Count ? null : TutorialChapters.Of(Chapters, script.Id, script.Steps[index].Chapter);

        // 지금 각본이 끝나면 이어 열 각본(그 장의 Then — 없으면 null: 장·튜토리얼의 끝)
        string NextScript()
        {
            var ch = script != null && script.Steps.Count > 0 ? ChapterAt(script.Steps.Count - 1) : null;
            return ch?.After(script.Id);
        }

        // ── 프레임 ──

        /// <summary>
        /// 화면 흐름이 프레임마다 부른다: 사람이 판을 떠났으면(타이틀 · 혼자 하기 …) 튜토리얼을 놓고, 단계 번호가 장 경계를 넘었으면 지난 장을 완료로(seen) · 새 장을 last로 적고,
        /// 각본이 끝났으면 그 장의 다음 각본을 열거나(07 T54) 끝 창을 연다.
        /// </summary>
        public void Tick()
        {
            if (Runner == null) return;
            if (flow.Session != Runner.Session)
            {
                Drop();
                return;
            }
            int index = Runner.Index;
            if (index != tracked)
            {
                for (int i = tracked; i < index && i < script.Steps.Count; i++)
                {
                    var was = ChapterAt(i);
                    bool crossed = i + 1 < script.Steps.Count ? ChapterAt(i + 1) != was : NextScript() == null;   // 각본 끝 = 장 끝일 때만(이어 열 각본이 남았으면 아직 그 장)
                    if (was != null && crossed) record.MarkSeen(was.Id);
                }
                var now = ChapterAt(index);
                if (now != null) record.Enter(now.Id);
                tracked = index;
            }
            if (Runner.Finished && !ended)
            {
                ended = true;
                string next = NextScript();
                if (next == null || !Launch(next, null, null)) ShowEnd();
            }
        }

        void Drop()
        {
            CloseDialog();
            StopLayer();
            Runner = null;
            script = null;
            ended = false;
        }

        void StopLayer()
        {
            Layer?.Stop();
            Layer = null;
        }

        // ── 창 ──

        /// <summary>
        /// 끝내기 창(07 §2-2): 「튜토리얼을 끝내겠습니까?」 + [타이틀로] [목차로] [계속하기]. 말풍선의 [튜토리얼 끝내기]와 게임 메뉴의 [튜토리얼 끝내기]가 부른다
        /// (게임 메뉴는 닫고 그 자리에 연다). 튜토리얼 밖이거나 창이 이미 떠 있으면 false.
        /// </summary>
        public bool OpenQuit()
        {
            if (Runner == null || DialogUp) return false;
            if (flow.Stage.Current is BoardMenuScreen) flow.Stage.Shut();
            Quitting = true;
            Show(new TutorialQuitScreen(this).Build(flow.Stage));
            return true;
        }

        /// <summary>[계속하기] · Esc: 창을 닫고 튜토리얼로 돌아간다(화면 대기열을 다시 돌린다).</summary>
        public void Continue()
        {
            if (!Quitting) return;
            CloseDialog();
            flow.ResumeAfterDialog();
        }

        /// <summary>
        /// 끝내기 창 · 끝 창의 [타이틀로] 단추 글 키: 참가 화면 · 로비에서 들어온 튜토리얼(<see cref="RoomFlow.InTutorial"/> — 누르면 들어온 화면으로 돌아간다)이면 「돌아가기」(결정 112 Q14).
        /// </summary>
        public string TitleKey => flow.Room.InTutorial ? "screen.tutorial.menu.to_back" : "screen.tutorial.menu.to_title";

        /// <summary>[타이틀로]: 판을 버리고 타이틀로(「튜토리얼」 줄을 고른 채).</summary>
        public void ToTitle()
        {
            Drop();
            flow.OpenTitle(TitleScreen.Tutorial);
        }

        /// <summary>[목차로]: 판을 버리고 목차로.</summary>
        public void ToMenu()
        {
            Drop();
            flow.OpenTutorialMenu();
        }

        /// <summary>Esc(<see cref="GameFlow.Escape"/>): 덮어 연 룰 설명서면 닫고, 끝내기 창이면 [계속하기]. 끝 창은 받지 않는다(고르는 창).</summary>
        public bool Escape()
        {
            if (book != null)
            {
                CloseBook();
                return true;
            }
            if (!Quitting) return false;
            Continue();
            return true;
        }

        /// <summary>끝 창(본편 [목차로] · [타이틀로], 확장 장 [목차로]). 장의 마지막 각본이 끝났을 때 <see cref="Tick"/>·<see cref="SkipChapter"/>가 연다.</summary>
        void ShowEnd()
        {
            ended = true;
            CloseDialog();
            StopLayer();
            Ending = true;
            var ch = Chapter;
            Show(new TutorialEndScreen(this, ch == null || ch.IsMain, ch).Build(flow.Stage));
        }

        void Show(VisualElement element)
        {
            dialog = element;
            var root = flow.Stage.Root;
            if (root != null)
            {
                root.Add(element);
                var tips = flow.Stage.Tips;
                if (tips != null && tips.Element.parent == root) element.PlaceBehind(tips.Element);   // 툴팁 층 바로 아래 — 튜토리얼 층·말풍선·연출 덮개 위
            }
            flow.PauseForDialog();
        }

        void CloseDialog()
        {
            CloseBook();
            dialog?.RemoveFromHierarchy();
            dialog = null;
            Quitting = Ending = false;
        }

#if UNITY_EDITOR
        static string EditorJson(string file)
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Sinnoh/Data/Json/" + file);
            return asset != null ? asset.text : null;
        }
#endif
    }
}
