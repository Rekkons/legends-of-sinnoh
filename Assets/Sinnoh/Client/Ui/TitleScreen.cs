using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S01 타이틀(04 §1, 목업 r3 <c>Logs/UiMock/r3/title-1080.png</c> — 결정 47 ③): 왼쪽 위 글자 로고, 왼쪽 메뉴(고른 줄 = ▶ + 노란 바탕), 왼쪽 아래 설명 창(두 줄), 맨 아래 띠.
    /// 뒤의 보드 전경은 이 화면이 그리지 않는다 — 같은 씬의 3D 보드가 패널 아래에 보인다. 자리·크기는 <c>Styles~/Sinnoh.uss</c>의 <c>sn-title-*</c>.
    /// 아직 없는 화면으로 가는 줄은 흐리게 두고 설명 둘째 줄에 준비 중이라고 적는다 [가정] — 고를 수는 있고(설명이 보인다) 눌러도 아무 일이 없다.
    /// 문구는 문자열 표의 <c>screen.title.*</c>(원본 <c>Tools/data/authored/screens.json</c>, 지침 <c>Docs/Design/ui-text-guide.md</c> — 안내는 합쇼체).
    /// 「혼자 하기」(솔로 설정 S09)·「설정」(S02)은 갈 곳을 받았을 때만 켜진다 — 화면 흐름(<see cref="GameFlow"/>)이 넣는다.
    /// 「이어하기」(저장 목록 S04, 04 §9-12)는 저장이 있을 때만 켜진다: 줄 아래에 가장 최근 판의 요약 한 줄, 저장이 없으면 회색 + 「저장한 게임 없음」이고 설명 둘째 줄에 까닭(「저장한 게임이 없습니다.」)을 적는다.
    /// 「튜토리얼」(M6.5, 07 §2-2 [가정 T3·T40]): 「혼자 하기」 바로 아래 줄(<c>title-item-tutorial</c>). 본편 마지막 장을 끝까지 보기 전까지 「처음이라면!」 딱지(<c>title-tutorial-first</c>). 웹 손님판에는 없다(웹 손님 입구는 M7).
    /// </summary>
    public sealed class TitleScreen : IUiScreen, IOverBoard
    {
        public sealed class Item
        {
            /// <summary>줄 이름. 글은 문자열 표의 <c>screen.title.&lt;Id&gt;</c>(+ <c>.desc1</c>·<c>.desc2</c>).</summary>
            public readonly string Id;

            /// <summary>웹 손님판에서도 보인다 — 웹은 참가 전용이라 「참가하기」·「튜토리얼」(결정 112 Q18)·「도감」·「설정」만(02 §8 · plan M7 「웹 손님」 [가정 M7-62]: 방 만들기 · 이어하기 · 혼자 하기 · 크레딧 · 종료를 숨긴다).</summary>
            public readonly bool OnWeb;

            public Item(string id, bool onWeb = true)
            {
                Id = id;
                OnWeb = onWeb;
            }
        }

        public const string Continue = "continue", Host = "host", Join = "join", Solo = "solo", Tutorial = "tutorial", Dex = "dex", Rulebook = "rulebook", Settings = "settings", Credits = "credits", Quit = "quit";

        /// <summary>영문 원제·판(두 언어 화면에 같은 글이라 문자열 표에 두지 않는다).</summary>
        public const string LogoEn = "Pokémon: Legends of Sinnoh · v1.4a";

        /// <summary>메뉴 줄(04 §1 순서). 요소 이름은 <c>title-item-&lt;Id&gt;</c>.</summary>
        public static readonly Item[] Items =
        {
            new Item(Continue, onWeb: false), new Item(Host, onWeb: false), new Item(Join), new Item(Solo, onWeb: false), new Item(Tutorial), new Item(Dex), new Item(Rulebook), new Item(Settings), new Item(Credits, onWeb: false), new Item(Quit, onWeb: false),
        };

        /// <summary>메뉴 판 오른쪽 변(720 토큰 px — <c>Sinnoh.uss</c> <c>sn-title-menu</c>의 left 60 + width 230)과, 보드를 거기서 띄우는 틈 [가정].</summary>
        public const float MenuRight = 290f, BoardGap = BoardBackdrop.Gap;

        public float PanelRight => MenuRight;

        /// <summary>타이틀 뒤 보드를 오른쪽으로 미는 폭(물리 px, <see cref="BoardBackdrop.ShiftPx"/>).</summary>
        public static float BoardShiftPx(int width, int height) => BoardBackdrop.ShiftPx(width, height, MenuRight);

        /// <summary>타이틀 뒤 보드 카메라가 보는 점(전체 줌·정면, <see cref="BoardBackdrop.Target"/>).</summary>
        public static Vector3 BoardTarget(int width, int height) => BoardBackdrop.Target(width, height, MenuRight);

        readonly Action quit, solo, settings, saves, tutorial, host, join, dex;
        readonly Action rulebook, credits;   // 「룰 설명서」 S43 · 「크레딧」 S03(M9 — GameFlow.OpenTitleRulebook · OpenCredits). null이면 그 줄은 아직 없는 화면이다
        readonly Action<bool> notice;   // 첫 실행 플레이 기록 알림의 답(결정 126 ④ — GameFlow.AnswerPlayNotice: false = [확인] · true = [설정 열기]). null이면 알림이 없다
        UpdateView view;   // 새 판 알림(결정 148 ① · 161 — GameFlow.ShowRelease · GameFlow.Update.cs). null이면 알림이 없다
        VisualElement page, noticeBox, updateBox, updateBlock, updateFill;
        Label updateAmount;
        bool noticeAnswered;
        readonly string saveLine;
        readonly bool web, firstTime;
        readonly string startPick;
        readonly List<Item> shown = new List<Item>();
        readonly List<VisualElement> rows = new List<VisualElement>();
        UiStage stage;
        Label line1, line2;

        /// <param name="quit">「종료」가 하는 일. 기본은 <c>Application.Quit</c>.</param>
        /// <param name="web">웹 손님판 메뉴(방 만들기·종료 숨김). 기본은 지금 플랫폼.</param>
        /// <param name="pick">처음 고른 줄. 기본은 「혼자 하기」(화면 흐름은 저장이 있으면 「이어하기」를 넣는다 [가정]).</param>
        /// <param name="solo">「혼자 하기」가 하는 일(솔로 설정을 연다). null이면 그 줄은 아직 없는 화면이다.</param>
        /// <param name="settings">「설정」이 하는 일(설정 화면을 연다). null이면 그 줄은 아직 없는 화면이다.</param>
        /// <param name="saves">「이어하기」가 하는 일(저장 목록을 연다). null이면 저장한 게임이 없다(회색).</param>
        /// <param name="saveLine">「이어하기」 아래에 적을 저장 요약(가장 최근 판). null이면 「저장한 게임 없음」.</param>
        /// <param name="tutorial">「튜토리얼」이 하는 일(<see cref="TutorialMenu.Open"/>). null이면 그 줄은 아직 없는 화면이다.</param>
        /// <param name="firstTime">「처음이라면!」 딱지를 붙이나(본편 마지막 장을 끝까지 보기 전 — <see cref="TutorialMenu.FirstTime"/>).</param>
        /// <param name="host">「방 만들기」가 하는 일(S05 — <see cref="RoomFlow.OpenCreate"/>, M7 화면 ①). null이면 그 줄은 아직 없는 화면이다.</param>
        /// <param name="join">「참가하기」가 하는 일(S06 — <see cref="RoomFlow.OpenJoin"/>). null이면 그 줄은 아직 없는 화면이다.</param>
        /// <param name="dex">「포켓몬 도감」이 하는 일(S42 — <see cref="GameFlow.OpenTitleDex"/>, 결정 105 ③ · 웹 손님판에도 있다). null이면 그 줄은 아직 없는 화면이다.</param>
        public TitleScreen(Action quit = null, bool? web = null, string pick = Solo, Action solo = null, Action settings = null, Action saves = null, string saveLine = null,
            Action tutorial = null, bool firstTime = false, Action host = null, Action join = null, Action dex = null, Action rulebook = null, Action credits = null, Action<bool> notice = null,
            string update = null, Action download = null, UpdateView updates = null)
        {
            this.notice = notice;
            view = updates ?? (update != null ? new UpdateView { Version = update, Download = download } : null);
            this.rulebook = rulebook;
            this.credits = credits;
            this.host = host;
            this.dex = dex;
            this.join = join;
            this.tutorial = tutorial;
            this.firstTime = firstTime;
            this.saves = saves;
            this.saveLine = saveLine;
            this.quit = quit ?? Application.Quit;
            this.web = web ?? Application.platform == RuntimePlatform.WebGLPlayer;
            this.solo = solo;
            this.settings = settings;
            startPick = pick;
        }

        public string Name => "title";

        /// <summary>보이는 메뉴 줄(웹 손님판은 둘이 빠진다).</summary>
        public IReadOnlyList<Item> Shown => shown;

        /// <summary>고른 줄(<see cref="Shown"/> 색인).</summary>
        public int Picked { get; private set; } = -1;

        /// <summary>그 줄이 갈 화면(또는 할 일)이 있나. 지금은 「종료」와, 갈 곳을 받은 「혼자 하기」·「튜토리얼」·「설정」·「이어하기」(저장이 있을 때)·「방 만들기」·「참가하기」(M7).</summary>
        public bool Ready(Item item) => item.Id == Quit || (item.Id == Solo && solo != null) || (item.Id == Tutorial && tutorial != null) || (item.Id == Settings && settings != null) || (item.Id == Continue && saves != null)
            || (item.Id == Host && host != null) || (item.Id == Join && join != null) || (item.Id == Dex && dex != null)
            || (item.Id == Rulebook && rulebook != null) || (item.Id == Credits && credits != null);

        /// <summary>「처음이라면!」 딱지가 붙었나.</summary>
        public bool FirstTimeShown { get; private set; }

        /// <summary>「이어하기」 아래에 적힌 저장 요약 줄.</summary>
        public string SaveLine { get; private set; }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            shown.Clear();
            rows.Clear();
            Picked = -1;
            FirstTimeShown = false;
            page = new VisualElement();

            var logo = Add(page, new VisualElement(), "title-logo", "sn-title-logo");
            Add(logo, Kit.Text(stage.T("screen.title.logo_sub"), Kit.F2), null, "sn-title-logo-sub");
            Add(logo, Kit.Text(stage.T("screen.title.logo_main"), Kit.F3), null, "sn-title-logo-main");
            Add(logo, Kit.Text(LogoEn, Kit.F1), null, "sn-title-logo-en");

            var menu = Add(page, Kit.Panel(stage.Skin), "title-menu", "sn-title-menu");
            foreach (var item in Items)
            {
                if (web && !item.OnWeb) continue;
                int index = shown.Count;
                shown.Add(item);
                var row = Add(menu, new VisualElement(), "title-item-" + item.Id, "sn-title-item");
                Add(row, Kit.Text("▶", Kit.F2), null, "sn-title-cursor");
                var text = Add(row, new VisualElement(), null, Ready(item) ? null : "sn-off");
                text.Add(Kit.Text(stage.T("screen.title." + item.Id), Kit.F3));
                if (item.Id == Continue)
                {
                    // 저장 요약 한 줄(04 §1): 가장 최근 판 — 없으면 「저장한 게임 없음」
                    row.AddToClassList("sn-title-item-two");
                    SaveLine = saveLine ?? stage.T("screen.title.no_save");
                    Add(text, Kit.Text(SaveLine, Kit.F1), "title-save-line", "kit-muted");
                }
                if (item.Id == Tutorial && firstTime && Ready(item))
                {
                    Add(row, Kit.Badge(stage.T("screen.tutorial.menu.first"), KitBadgeKind.Gold, Kit.F2), "title-tutorial-first", "sn-title-first");
                    FirstTimeShown = true;
                }
                row.RegisterCallback<PointerEnterEvent>(e => Pick(index));
                stage.OnTap(row, SoundKeys.Decide, e => Activate(index));
                rows.Add(row);
            }

            var desc = Add(page, Kit.Panel(stage.Skin), "title-desc", "sn-title-desc");
            line1 = Add(desc, Kit.Text("", Kit.F2), null, null);
            line2 = Add(desc, Kit.Text("", Kit.F2), null, "kit-mt2");

            var foot = Add(page, new VisualElement(), "title-foot", "sn-title-foot");
            foot.Add(Kit.Text(stage.T("screen.title.foot_left"), Kit.F1));
            var right = Add(foot, new VisualElement(), null, "sn-title-foot-right");
            right.Add(Kit.Text(stage.T("screen.title.foot_right"), Kit.F1));
            Add(right, Kit.Text(VersionText, Kit.F1), "title-version", "sn-title-version");

            int start = shown.FindIndex(i => i.Id == startPick);
            Pick(start >= 0 ? start : 0);
            updateBox = view?.Version != null && !web ? Update() : null;
            noticeBox = notice != null && !noticeAnswered ? Notice(page) : null;
            return page;
        }

        public void Close()
        {
            noticeBox = null;
            updateBox = null;
            updateBlock = null;
            updateFill = null;
            updateAmount = null;
            page = null;
        }

        /// <summary>아래 띠 오른쪽 끝의 판 번호(결정 148 — 정본은 프로젝트 설정 bundleVersion = <see cref="Application.version"/>, 버그 신고 · 판 정보 파일과 같은 값).</summary>
        public static string VersionText => "v" + Application.version;

        /// <summary>새 판 알림(<c>title-update</c>)이 떠 있나.</summary>
        public bool UpdateShown => updateBox != null && updateBox.parent != null;

        /// <summary>새 판 알림을 띄운다(이미 그린 타이틀 — 판 정보를 늦게 받았을 때). 웹 손님판은 띄우지 않는다. 첫 실행 알림 창이 떠 있으면 그 아래에.</summary>
        public void ShowUpdate(string version, Action download) => ShowUpdate(version == null ? null : new UpdateView { Version = version, Download = download });

        /// <summary>알림 상자를 그 단계로 다시 그린다(결정 161 — 흐름이 단계가 바뀔 때마다 부른다). 받는 중에는 메뉴를 막는 덮개가 상자 아래에 깔린다.</summary>
        public void ShowUpdate(UpdateView v)
        {
            view = v;
            if (page == null || web) return;
            updateBlock?.RemoveFromHierarchy();
            updateBox?.RemoveFromHierarchy();
            updateBlock = null;
            updateBox = v?.Version != null ? Update() : null;
            if (updateBox != null && NoticeShown)
            {
                if (updateBlock != null) page.Insert(page.IndexOf(noticeBox), updateBlock);
                page.Insert(page.IndexOf(noticeBox), updateBox);
            }
        }

        /// <summary>받는 중 진행 막대 · 「12.3 / 28.8 MB」만 고친다(상자를 다시 짓지 않는다).</summary>
        public void Progress(long got, long total)
        {
            if (view == null) return;
            view.Got = got;
            view.Total = total;
            if (updateFill == null) return;
            updateFill.style.width = Length.Percent(total > 0 ? Mathf.Clamp01((float)got / total) * 100f : 0f);
            updateAmount.text = Amount(got, total);
        }

        static string Amount(long got, long total) => GameFlow.Mb(got) + " / " + GameFlow.Mb(total) + " MB";

        /// <summary>업데이트가 메뉴를 막고 있나(견주기 · 받기 · 다시 시작 — 모달).</summary>
        public bool UpdateBusy => view != null && view.Busy && UpdateShown;

        /// <summary>알림의 단계(알림이 없으면 null).</summary>
        public UpdateStep? UpdateStepShown => UpdateShown ? view.Step : (UpdateStep?)null;

        /// <summary>알림의 첫 단추: [업데이트](자동 업데이트가 되면) 또는 [받기]. 알림이 없거나 받는 중이면 false.</summary>
        public bool PressUpdate()
        {
            if (!UpdateShown || NoticeShown || view.Busy || view.Step == UpdateStep.Failed) return false;
            var act = view.Start ?? view.Download;
            if (act == null) return false;
            act();
            return true;
        }

        /// <summary>[취소](견주기 · 받는 중).</summary>
        public bool PressCancel()
        {
            if (!UpdateShown || (view.Step != UpdateStep.Scan && view.Step != UpdateStep.Fetch) || view.Cancel == null) return false;
            view.Cancel();
            return true;
        }

        /// <summary>실패 알림의 [다시 시도](retry = true) · [받기].</summary>
        public bool PressFailed(bool retry)
        {
            if (!UpdateShown || NoticeShown || view.Step != UpdateStep.Failed) return false;
            var act = retry ? view.Retry : view.Download;
            if (act == null) return false;
            act();
            return true;
        }

        // 새 판 알림(결정 148 ① · 161 — 아래 띠 바로 위 오른쪽 작은 판 [가정]): 단계마다
        //   알림 「새 버전 x.y.z」 + [업데이트](바뀐 파일만 받고 다시 시작) 또는 [받기](GitHub) · 견주기 「확인 중」 + [취소] · 받기 「받는 중 12.3 / 28.8 MB」 + 막대 + [취소]
        //   · 「다시 시작합니다」 · 실패 「업데이트하지 못했습니다」 + 까닭 한 줄(공간 · 한도) + [다시 시도] · [받기]. 받는 동안은 덮개가 메뉴를 막는다
        VisualElement Update()
        {
            var v = view;
            updateFill = null;
            updateAmount = null;
            if (v.Busy) updateBlock = Add(page, new VisualElement(), "title-update-block", "kit-scrim");
            var box = Add(page, Kit.Panel(stage.Skin), "title-update", "sn-title-update");
            switch (v.Step)
            {
                case UpdateStep.Scan:
                    Add(box, Kit.Text(stage.T("screen.title.update.scan"), Kit.F2), "title-update-text", "sn-title-update-text");
                    Add(box, Kit.Button(stage.T("screen.title.update.cancel"), KitButtonKind.Normal, () => PressCancel()), "title-update-cancel", null);
                    break;
                case UpdateStep.Fetch:
                    box.AddToClassList("sn-title-update-wide");
                    var head = Add(box, new VisualElement(), null, "sn-title-update-row");
                    Add(head, Kit.Text(stage.T("screen.title.update.fetch"), Kit.F2), "title-update-text", "sn-title-update-text");
                    updateAmount = Add(head, Kit.Text(Amount(v.Got, v.Total), Kit.F2), "title-update-amount", "sn-title-update-amount");
                    var row = Add(box, new VisualElement(), null, "sn-title-update-row");
                    var bar = Add(row, new VisualElement(), "title-update-bar", "sn-title-update-bar");
                    updateFill = Add(bar, new VisualElement(), "title-update-fill", "sn-title-update-fill");
                    Add(row, Kit.Button(stage.T("screen.title.update.cancel"), KitButtonKind.Normal, () => PressCancel()), "title-update-cancel", null);
                    Progress(v.Got, v.Total);
                    break;
                case UpdateStep.Ready:
                case UpdateStep.Restart:
                    Add(box, Kit.Text(stage.T("screen.title.update.restart"), Kit.F2), "title-update-text", null);
                    break;
                case UpdateStep.Failed:
                    box.AddToClassList("sn-title-update-wide");
                    Add(box, Kit.Text(stage.T("screen.title.update.fail"), Kit.F2), "title-update-text", null);
                    if (v.FailLine != null) Add(box, Kit.Wrapped(v.FailLine, Kit.F2), "title-update-why", "sn-title-update-why");
                    var buttons = Add(box, new VisualElement(), null, "sn-title-update-buttons");
                    if (v.Retry != null) Add(buttons, Kit.Button(stage.T("screen.title.update.retry"), KitButtonKind.Main, () => PressFailed(true)), "title-update-retry", null);
                    GetButton(buttons, v.Retry == null ? KitButtonKind.Main : KitButtonKind.Normal, () => PressFailed(false));
                    break;
                default:
                    Add(box, Kit.Text(stage.T("screen.title.update").Replace("{v}", v.Version), Kit.F2), "title-update-text", "sn-title-update-text");
                    if (v.Start != null)
                    {
                        var go = Add(box, Kit.Button(stage.T("screen.title.update.do"), KitButtonKind.Main, () => PressUpdate()), "title-update-do", null);
                        stage.Tips.Hook(go, stage.T("screen.title.update.do"), stage.T("screen.title.update.do.tip"), TipSide.Above, reading: true);
                    }
                    else GetButton(box, KitButtonKind.Main, () => PressUpdate());
                    break;
            }
            return box;
        }

        // [받기](마우스 오버 = GitHub 받기 페이지)
        void GetButton(VisualElement parent, KitButtonKind kind, Action press)
        {
            var get = Add(parent, Kit.Button(stage.T("screen.title.update.get"), kind, press), "title-update-get", null);
            stage.Tips.Hook(get, stage.T("screen.title.update.get"), stage.T("screen.title.update.tip"), TipSide.Above, reading: true);
        }

        /// <summary>첫 실행의 플레이 기록 알림 창(<c>title-notice</c>)이 떠 있나.</summary>
        public bool NoticeShown => noticeBox != null && noticeBox.parent != null;

        // 첫 실행 플레이 기록 알림(결정 126 ④ — 타이틀 위 가운데 창 · 뒤를 덮는다 [가정 plan §5 #651]): 무엇 · 왜 · 끄는 곳 세 줄 + 마우스 오버(개인 정보 처리 요약), [확인] · [설정 열기]
        VisualElement Notice(VisualElement page)
        {
            var box = Add(page, new VisualElement(), "title-notice", "sn-notice");
            Add(box, new VisualElement(), null, "kit-scrim");
            var panel = Add(box, Kit.Panel(stage.Skin), "title-notice-panel", "sn-notice-panel");
            Add(panel, Kit.Text(stage.T("screen.title.notice.title"), Kit.FTitle), "title-notice-head", "kit-title");
            var body = Add(panel, new VisualElement(), "title-notice-body", null);
            foreach (string line in new[] { "what", "why", "off" }) Add(body, Kit.Wrapped(stage.T("screen.title.notice." + line), Kit.F2), "title-notice-" + line, "sn-notice-line");
            var more = Add(panel, Kit.Text(stage.T("screen.title.notice.more"), Kit.F2), "title-notice-more", "kit-muted");
            stage.Tips.Hook(more, stage.T("screen.title.notice.title"), stage.T("screen.privacy.tip"), TipSide.Below, reading: true);
            var row = Add(panel, new VisualElement(), null, "sn-foot-row");
            Add(row, Kit.Button(stage.T("screen.title.notice.ok"), KitButtonKind.Main, () => AnswerNotice(false)), "title-notice-ok", null);
            Add(row, Kit.Button(stage.T("screen.title.notice.settings"), KitButtonKind.Normal, () => AnswerNotice(true)), "title-notice-settings", null);
            return box;
        }

        /// <summary>알림의 [확인](false) · [설정 열기](true): 창을 걷고 흐름에 답한다. 알림이 없으면 false.</summary>
        public bool AnswerNotice(bool openSettings)
        {
            if (!NoticeShown) return false;
            noticeBox.RemoveFromHierarchy();
            noticeAnswered = true;
            notice(openSettings);
            return true;
        }

        /// <summary>줄을 고른다(마우스를 올렸다): ▶ + 노란 바탕, 설명 창에 그 줄의 설명.</summary>
        public void Pick(int index)
        {
            if (index < 0 || index >= rows.Count || index == Picked) return;
            if (Picked >= 0) rows[Picked].RemoveFromClassList("sn-title-item-picked");
            Picked = index;
            rows[index].AddToClassList("sn-title-item-picked");
            var item = shown[index];
            line1.text = stage.T("screen.title." + item.Id + ".desc1");
            // 못 고르는 까닭: 「이어하기」는 저장이 없어서, 나머지는 아직 없는 화면이라서
            line2.text = Ready(item) ? stage.TOrNull("screen.title." + item.Id + ".desc2") ?? "" : stage.T(item.Id == Continue ? "screen.title.continue.none" : "screen.common.not_ready");
        }

        /// <summary>줄을 누른다. 아직 없는 화면이면 아무 일도 없다(false).</summary>
        public bool Activate(int index)
        {
            if (index < 0 || index >= shown.Count || NoticeShown || UpdateBusy) return false;   // 알림 창이 떠 있는 동안 · 업데이트를 받는 동안은 메뉴를 누르지 않는다(창 · 덮개가 덮고 있다)
            Pick(index);
            var item = shown[index];
            if (!Ready(item)) return false;
            if (item.Id == Quit) quit();
            else if (item.Id == Solo) solo();
            else if (item.Id == Continue) saves();
            else if (item.Id == Tutorial) tutorial();
            else if (item.Id == Host) host();
            else if (item.Id == Join) join();
            else if (item.Id == Dex) dex();
            else if (item.Id == Rulebook) rulebook();
            else if (item.Id == Credits) credits();
            else settings();
            return true;
        }

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
