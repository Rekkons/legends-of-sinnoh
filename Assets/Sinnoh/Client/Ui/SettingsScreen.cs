using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Data;
using Sinnoh.Fx;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S02 설정(04 §1·§2). 줄 둘: 「화면 크기」(결정 59 — 창 테두리 끌기 없이 정해 둔 해상도만): <see cref="WindowSizes"/> 가운데 모니터 안에 드는 것(모니터보다 작은 것) + 「전체 화면」.
    /// 누르면 바로 바뀌고(<see cref="WindowControl.Set"/>) 엔진이 그 값을 저장해 다음 실행에도 남는다.
    /// 「말 그림」(04 §3-1, 결정 52 [가정 — plan §5]): 주인공 도트 / 직업 그림 — 보는 사람의 표시 설정이라 누르면 뒤의 보드 말이 바로 바뀐다(<see cref="GameFlow.PieceStyle"/> — 음량과 같은 곳에 저장한다, 결정 70 ⑤).
    /// 「배경음악」·「효과음」(결정 66 ④ · 결정 70 ⑭ — 슬라이더 0(끔)~10, 오른쪽에 값): 누르거나 끌면 바로 그 음량이 되고(<see cref="GameFlow.SoundPrefs"/> — 플레이어는 창 크기와 같은 곳에 저장한다),
    /// 효과음을 바꾸면 그 음량으로 확정 소리를 한 번 낸다(끔이면 소리 없음).
    /// 게임 메뉴에서 연 설정(결정 70 ⑨ — <see cref="InGame"/>): 같은 화면을 보드 HUD 위에 열고 뒤를 어둡게 덮는다. [뒤로]는 게임 메뉴로 돌아간다(카메라는 건드리지 않는다).
    /// 「연출」(04 §8-1 P17, [가정 — plan §5 #59]): 보통 · 빠름 · 끄기(<see cref="GameFlow.FxSpeed"/> — 내 화면의 재생만, 음량과 같은 곳에 저장).
    /// 뒤의 보드 전경·판 자리는 솔로 설정과 같다(<c>sn-solo-*</c>), 값이 두 줄로 넘어가는 줄만 <c>sn-set-*</c>. 글은 문자열 표 <c>screen.settings.*</c>(크기 값 「1280×720」은 숫자뿐이라 표에 두지 않는다).
    /// 요소 이름: <c>settings</c>·<c>settings-panel</c>·<c>settings-row-window</c>·<c>settings-opt-window-&lt;폭&gt;x&lt;높이&gt;</c>·<c>settings-opt-window-full</c>·
    /// <c>settings-row-piece</c>·<c>settings-opt-piece-dot</c>·<c>settings-opt-piece-job</c>·<c>settings-row-bgm</c>·<c>settings-slider-bgm</c>·<c>settings-val-bgm</c>·<c>settings-row-se</c>·<c>settings-slider-se</c>·<c>settings-val-se</c>·
    /// <c>settings-row-fx</c>·<c>settings-opt-fx-&lt;normal|fast|off&gt;</c>·<c>settings-row-alert</c>·<c>settings-opt-alert-&lt;on|off&gt;</c>(결정 112 Q17 — 차례 알림)·<c>settings-row-lang</c>·<c>settings-opt-lang-&lt;ko|en&gt;</c>(M9 언어 ② — 맨 위 줄, 값 이름은 그 언어로)·<c>settings-row-upload</c>·<c>settings-opt-upload-&lt;on|off&gt;</c>·<c>settings-upload-delete</c>(+ 아래 줄 <c>settings-upload-note</c> · <c>-ask</c> · <c>-delete-yes</c> · <c>-delete-no</c> · <c>-result</c> — 결정 126 ④ 플레이 기록 보내기, 올리는 창구가 있는 PC 플레이어만)·<c>settings-back</c>·<c>settings-bug</c>(타이틀에서 연 설정만 — 버그 신고 창 <see cref="BugReportScreen"/>, 결정 132 ①).
    /// </summary>
    public sealed class SettingsScreen : IUiScreen, IOverBoard
    {
        public const string Full = "full";
        public const string PieceDot = "dot", PieceJob = "job";

        readonly Action back;
        readonly GameFlow flow;
        readonly Dictionary<PieceStyle, VisualElement> pieceOpts = new Dictionary<PieceStyle, VisualElement>();
        readonly WindowControl window;
        readonly List<(int W, int H)> sizes;
        readonly Dictionary<string, VisualElement> opts = new Dictionary<string, VisualElement>();
        readonly Dictionary<string, KitSlider> sliders = new Dictionary<string, KitSlider>();
        readonly Dictionary<string, Label> values = new Dictionary<string, Label>();
        string offText;
        readonly Dictionary<FxSpeed, VisualElement> fxOpts = new Dictionary<FxSpeed, VisualElement>();
        readonly Dictionary<bool, VisualElement> alertOpts = new Dictionary<bool, VisualElement>();
        readonly Dictionary<string, VisualElement> langOpts = new Dictionary<string, VisualElement>();
        readonly Dictionary<bool, VisualElement> uploadOpts = new Dictionary<bool, VisualElement>();
        VisualElement uploadNote;
        UiStage uploadStage;

        /// <summary>[내 기록 지우기]의 단계: 없음 · 묻는 중 · 지우는 중 · 지움(<see cref="Deleted"/>개) · 실패.</summary>
        public enum DeleteStep { None, Asking, Deleting, Done, Failed }

        /// <summary>[내 기록 지우기]의 지금 단계.</summary>
        public DeleteStep Deletion { get; private set; }

        /// <summary>마지막으로 지운 기록 수(<see cref="DeleteStep.Done"/>일 때).</summary>
        public int Deleted { get; private set; }

        /// <summary>플레이 기록 보내기(결정 126 ④ — 올리는 창구가 없는 화면이면 false).</summary>
        public bool Upload => flow != null && flow.Uploads != null && flow.SoundPrefs.PlayUpload;

        /// <summary>플레이 기록 보내기를 켜고 끈다(음량과 같은 곳에 저장 — 끄면 다음 판부터 올리지 않는다).</summary>
        public void PickUpload(bool on)
        {
            if (flow == null || flow.Uploads == null) return;
            flow.SoundPrefs.PlayUpload = on;
            Refresh();
        }

        /// <summary>[내 기록 지우기]: 판 아래 줄에 「모두 지우겠습니까?」 + [지우기] · [취소]. 지우는 중이면 false.</summary>
        public bool AskDelete()
        {
            if (flow == null || flow.Uploads == null || Deletion == DeleteStep.Deleting) return false;
            Deletion = DeleteStep.Asking;
            PaintNote();
            return true;
        }

        /// <summary>물음의 [취소].</summary>
        public void CancelDelete()
        {
            if (Deletion != DeleteStep.Asking) return;
            Deletion = DeleteStep.None;
            PaintNote();
        }

        /// <summary>물음의 [지우기]: 서버에서 내 기록을 모두 지우고(<see cref="PlayUploads.DeleteMine"/>) 결과 한 줄을 적는다. 지운 수 · 실패 −1(묻는 중이 아니면 −1, 부르지 않는다).</summary>
        public async System.Threading.Tasks.Task<int> DeleteRecords()
        {
            if (flow == null || flow.Uploads == null || Deletion != DeleteStep.Asking) return -1;
            Deletion = DeleteStep.Deleting;
            PaintNote();
            int n = await flow.Uploads.DeleteMine();
            Deleted = Math.Max(0, n);
            Deletion = n >= 0 ? DeleteStep.Done : DeleteStep.Failed;
            PaintNote();
            return n;
        }

        // 판 아래 지우기 줄: 물음 + 단추 · 지우는 중 · 결과(없음이면 비운다)
        void PaintNote()
        {
            if (uploadNote == null) return;
            uploadNote.Clear();
            var stage = uploadStage;
            if (Deletion == DeleteStep.Asking)
            {
                Add(uploadNote, Kit.Wrapped(stage.T("screen.settings.upload.delete.ask"), Kit.F2), "settings-upload-ask", null);
                var row = Add(uploadNote, new VisualElement(), null, "sn-foot-row");
                Add(row, Kit.Button(stage.T("screen.settings.upload.delete.yes"), KitButtonKind.End, () => { _ = DeleteRecords(); }), "settings-upload-delete-yes", null);
                Add(row, Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, CancelDelete), "settings-upload-delete-no", null);
            }
            else if (Deletion != DeleteStep.None)
            {
                string text = Deletion == DeleteStep.Deleting ? stage.T("screen.settings.upload.deleting")
                    : Deletion == DeleteStep.Failed ? stage.T("screen.settings.upload.delete.failed")
                    : KoJosa.Fix(stage.T("screen.settings.upload.deleted").Replace("{n}", Deleted.ToString(CultureInfo.InvariantCulture)));
                Add(uploadNote, Kit.Wrapped(text, Kit.F2), "settings-upload-result", Deletion == DeleteStep.Failed ? "sn-menu-warn" : "kit-muted");
            }
        }

        /// <param name="back">[뒤로]가 하는 일(타이틀로).</param>
        /// <param name="flow">말 그림 설정을 가진 흐름. null이면 「말 그림」 줄이 없다.</param>
        /// <param name="inGame">게임 메뉴에서 열었나(보드 HUD 위 — 뒤를 어둡게 덮는다).</param>
        public SettingsScreen(WindowControl window, Action back, GameFlow flow = null, bool inGame = false)
        {
            InGame = inGame;
            this.flow = flow;
            this.window = window ?? throw new ArgumentNullException(nameof(window));
            this.back = back ?? throw new ArgumentNullException(nameof(back));
            var monitor = window.Monitor;
            sizes = WindowSizes.Fit(monitor.x, monitor.y);
            var now = window.Size;
            Picked = window.Fullscreen ? Full : sizes.Contains((now.x, now.y)) ? Id(now.x, now.y) : null;
        }

        public string Name => "settings";

        /// <summary>게임 메뉴에서 연 설정인가(결정 70 ⑨).</summary>
        public bool InGame { get; }

        public float PanelRight => SoloScreen.ColumnRight + SoloScreen.EnWiden(flow);   // 게임 중(InGame)에는 화면 흐름이 카메라를 옮기지 않는다(GameFlow.LayCamera)

        /// <summary>고를 수 있는 창 크기(모니터 안에 드는 것).</summary>
        public IReadOnlyList<(int W, int H)> Sizes => sizes;

        /// <summary>고른 값: <see cref="Id"/>나 <see cref="Full"/>. 지금 창이 목록에 없는 크기면 null.</summary>
        public string Picked { get; private set; }

        public static string Id(int w, int h) => w.ToString(CultureInfo.InvariantCulture) + "x" + h.ToString(CultureInfo.InvariantCulture);

        /// <summary>고른 말 그림 양식(흐름 없이 연 화면이면 기본값).</summary>
        public PieceStyle Piece => flow != null ? flow.PieceStyle : PieceStyle.Dot;

        /// <summary>말 그림 양식을 고른다 — 뒤의 보드 말이 바로 바뀐다.</summary>
        public void PickPiece(PieceStyle style)
        {
            if (flow == null) return;
            flow.PieceStyle = style;
            Refresh();
        }

        /// <summary>배경음악 음량 단계(0 = 끔). 흐름 없이 연 화면이면 기본값.</summary>
        public int Bgm => flow != null ? flow.SoundPrefs.Bgm : SoundMix.DefaultBgmStep;

        /// <summary>효과음 음량 단계(0 = 끔).</summary>
        public int Se => flow != null ? flow.SoundPrefs.Se : SoundMix.DefaultSeStep;

        /// <summary>배경음악 음량을 고른다 — 곡이 바로 그 음량이 된다.</summary>
        public void PickBgm(int step)
        {
            if (flow == null) return;
            flow.SoundPrefs.Bgm = step;
            Refresh();
        }

        /// <summary>연출 속도(흐름 없이 연 화면이면 보통).</summary>
        public FxSpeed Fx => flow != null ? flow.FxSpeed : FxSpeed.Normal;

        /// <summary>연출 속도를 고른다(보통 · 빠름 · 끄기 — 내 화면의 재생만, 방장 멈춤은 그대로).</summary>
        public void PickFx(FxSpeed speed)
        {
            if (flow == null) return;
            flow.FxSpeed = speed;
            Refresh();
        }

        /// <summary>내 차례 알림(결정 112 Q17 — 흐름 없이 연 화면이면 켬).</summary>
        public bool Alert => flow == null || flow.SoundPrefs.Alert;

        /// <summary>차례 알림을 켜고 끈다(방 판에서 창이 비활성일 때의 작업 표시줄 깜빡임 · 웹 탭 제목 — 음량과 같은 곳에 저장).</summary>
        public void PickAlert(bool on)
        {
            if (flow == null) return;
            flow.SoundPrefs.Alert = on;
            Refresh();
        }

        /// <summary>고를 수 있는 화면 글 언어(S02 「언어」 — 칸 이름 <c>settings-opt-lang-&lt;언어&gt;</c>).</summary>
        public static readonly string[] Langs = { "ko", "en" };

        /// <summary>지금 화면 글의 언어(흐름 없이 연 화면이면 null).</summary>
        public string Lang => flow != null ? flow.Lang : null;

        /// <summary>화면 글의 언어를 고른다 — 이 화면 · 뒤의 보드 HUD가 그 언어로 다시 그려지고(<see cref="GameFlow.Lang"/>) 다음 실행에도 남는다. 방의 다른 좌석 화면은 그대로(결정 34).</summary>
        public void PickLang(string lang)
        {
            if (flow == null) return;
            flow.Lang = lang;
            Refresh();
        }

        /// <summary>연출 칸의 이름 마디(<c>settings-opt-fx-&lt;마디&gt;</c>·문자열 키 <c>screen.settings.fx.&lt;마디&gt;</c>).</summary>
        public static string FxId(FxSpeed speed) => speed == FxSpeed.Fast ? "fast" : speed == FxSpeed.Off ? "off" : "normal";

        /// <summary>효과음 음량을 고르고 그 음량으로 확정 소리를 한 번 낸다.</summary>
        public void PickSe(int step)
        {
            if (flow == null) return;
            flow.SoundPrefs.Se = step;
            flow.PlaySound(SoundKeys.Decide);
            Refresh();
        }

        public VisualElement Build(UiStage stage)
        {
            opts.Clear();
            pieceOpts.Clear();
            sliders.Clear();
            values.Clear();
            offText = stage.T("screen.settings.volume.off");
            fxOpts.Clear();
            alertOpts.Clear();
            var page = new VisualElement();
            if (InGame) Add(page, new VisualElement(), "settings-scrim", "kit-scrim");   // 뒤의 HUD·보드를 누르지 못하게 덮는다
            var col = Add(page, new VisualElement(), "settings-col", "sn-solo-col");
            Add(col, Kit.Text(stage.T("screen.settings.title"), Kit.FTitle), "settings-head", "sn-head");
            var panel = Add(col, Kit.Panel(stage.Skin), "settings-panel", "sn-solo-panel");
            // 언어(M9 언어 ② — 결정 111 ② · plan §5 #304): 값 이름은 그 언어의 표에서(「한국어」 · 「English」 — 어느 언어 화면에서도 같은 글)
            langOpts.Clear();
            if (flow != null && Array.TrueForAll(Langs, l => stage.TableOf(l) != null))
            {
                var langRow = Add(panel, new VisualElement(), "settings-row-lang", "sn-solo-row");
                var langLabel = Add(langRow, Kit.Text(stage.T("screen.settings.lang"), Kit.F2), null, "sn-solo-label");
                stage.Tips.Hook(langLabel, null, stage.T("screen.settings.lang.tip"), TipSide.Below, reading: true);
                var langBox = Add(langRow, new VisualElement(), null, "sn-solo-opts");
                foreach (string lang in Langs)
                {
                    string l = lang, shown = stage.TableOf(l)["screen.settings.lang.name"];
                    var opt = Add(langBox, new VisualElement(), "settings-opt-lang-" + l, "sn-opt");
                    opt.Add(Kit.Text(shown, Kit.F2));
                    stage.OnTap(opt, SoundKeys.Select, e => PickLang(l));
                    stage.Tips.Hook(opt, shown, stage.TableOf(l)["screen.settings.lang.pick"], TipSide.Below, reading: true);
                    langOpts[l] = opt;
                }
            }
            var row = Add(panel, new VisualElement(), "settings-row-window", "sn-set-row");
            Add(row, Kit.Text(stage.T("screen.settings.window"), Kit.F2), null, "sn-solo-label");
            var box = Add(row, new VisualElement(), null, "sn-set-opts");
            foreach (var s in sizes)
            {
                var size = s;
                AddOpt(stage, box, Id(s.W, s.H), s.W.ToString(CultureInfo.InvariantCulture) + "×" + s.H.ToString(CultureInfo.InvariantCulture), () => Pick(size.W, size.H));
            }
            AddOpt(stage, box, Full, stage.T("screen.settings.window.full"), PickFull);
            if (flow != null)
            {
                var pieceRow = Add(panel, new VisualElement(), "settings-row-piece", "sn-solo-row");
                Add(pieceRow, Kit.Text(stage.T("screen.settings.piece"), Kit.F2), null, "sn-solo-label");
                var pieceBox = Add(pieceRow, new VisualElement(), null, "sn-solo-opts");
                foreach (var style in new[] { PieceStyle.Dot, PieceStyle.Job })
                {
                    var s = style;
                    string id = s == PieceStyle.Dot ? PieceDot : PieceJob;
                    var opt = Add(pieceBox, new VisualElement(), "settings-opt-piece-" + id, "sn-opt");
                    opt.Add(Kit.Text(stage.T("screen.settings.piece." + id), Kit.F2));
                    stage.OnTap(opt, SoundKeys.Select, e => PickPiece(s));
                    stage.Tips.Hook(opt, stage.T("screen.settings.piece." + id), stage.T("screen.settings.piece." + id + ".tip"), TipSide.Below, reading: true);
                    pieceOpts[s] = opt;
                }
            }
            if (flow != null)
            {
                AddVolume(stage, panel, "bgm", PickBgm);
                AddVolume(stage, panel, "se", PickSe);
                var fxRow = Add(panel, new VisualElement(), "settings-row-fx", "sn-solo-row");
                Add(fxRow, Kit.Text(stage.T("screen.settings.fx"), Kit.F2), null, "sn-solo-label");
                var fxBox = Add(fxRow, new VisualElement(), null, "sn-solo-opts");
                foreach (var speed in new[] { FxSpeed.Normal, FxSpeed.Fast, FxSpeed.Off })
                {
                    var s = speed;
                    string id = FxId(s);
                    var opt = Add(fxBox, new VisualElement(), "settings-opt-fx-" + id, "sn-opt");
                    opt.Add(Kit.Text(stage.T("screen.settings.fx." + id), Kit.F2));
                    stage.OnTap(opt, SoundKeys.Select, e => PickFx(s));
                    stage.Tips.Hook(opt, stage.T("screen.settings.fx." + id), stage.T("screen.settings.fx." + id + ".tip"), TipSide.Below, reading: true);
                    fxOpts[s] = opt;
                }
                // 차례 알림(결정 112 Q17): 켬 · 끔
                var alertRow = Add(panel, new VisualElement(), "settings-row-alert", "sn-solo-row");
                var alertLabel = Add(alertRow, Kit.Text(stage.T("screen.settings.alert"), Kit.F2), null, "sn-solo-label");
                stage.Tips.Hook(alertLabel, null, stage.T("screen.settings.alert.tip"), TipSide.Below, reading: true);
                var alertBox = Add(alertRow, new VisualElement(), null, "sn-solo-opts");
                foreach (bool on in new[] { true, false })
                {
                    bool v = on;
                    string id = on ? "on" : "off";
                    var opt = Add(alertBox, new VisualElement(), "settings-opt-alert-" + id, "sn-opt");
                    opt.Add(Kit.Text(stage.T("screen.settings.alert." + id), Kit.F2));
                    stage.OnTap(opt, SoundKeys.Select, e => PickAlert(v));
                    alertOpts[v] = opt;
                }
            }
            // 플레이 기록 보내기(결정 126 ④ — 올리는 창구가 있는 PC 플레이어만): 켬 · 끔 + [내 기록 지우기](물음 → 서버에서 지움 → 결과 한 줄)
            uploadOpts.Clear();
            uploadNote = null;
            if (flow != null && flow.Uploads != null)
            {
                var upRow = Add(panel, new VisualElement(), "settings-row-upload", "sn-solo-row");
                var upLabel = Add(upRow, Kit.Text(stage.T("screen.settings.upload"), Kit.F2), null, "sn-solo-label");
                stage.Tips.Hook(upLabel, stage.T("screen.settings.upload"), stage.T("screen.settings.upload.tip"), TipSide.Below, reading: true);
                var upBox = Add(upRow, new VisualElement(), null, "sn-solo-opts");
                foreach (bool on in new[] { true, false })
                {
                    bool v = on;
                    var opt = Add(upBox, new VisualElement(), "settings-opt-upload-" + (on ? "on" : "off"), "sn-opt");
                    opt.Add(Kit.Text(stage.T("screen.settings.upload." + (on ? "on" : "off")), Kit.F2));
                    stage.OnTap(opt, SoundKeys.Select, e => PickUpload(v));
                    uploadOpts[v] = opt;
                }
                var del = Add(upBox, Kit.Button(stage.T("screen.settings.upload.delete"), KitButtonKind.Normal, () => AskDelete()), "settings-upload-delete", "sn-set-delete");
                stage.Tips.Hook(del, stage.T("screen.settings.upload.delete"), stage.T("screen.settings.upload.delete.tip"), TipSide.Below, reading: true);
                uploadNote = Add(panel, new VisualElement(), "settings-upload-note", "sn-set-note");
                uploadStage = stage;
                PaintNote();
            }
            var foot = Add(col, new VisualElement(), "settings-foot", "sn-foot-row");
            Add(foot, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, back), "settings-back", null);
            if (flow != null && !InGame)
            {
                // 버그 신고(M9 — 결정 132 ①): 타이틀 설정에서도 — 게임 중에는 게임 메뉴의 [버그 신고]. 보낼 창구가 없는 화면(웹 손님판 · 에디터)은 흐린 단추 + 「PC판에서만」 이유 툴팁
                bool ready = flow.BugReady;
                var bug = Add(foot, Kit.Button(stage.T(ready ? "screen.bug.button" : "screen.bug.button.pc"), KitButtonKind.Normal, () => flow.OpenTitleBug()), "settings-bug", null);
                if (!ready) bug.AddToClassList(UiStage.OffClass);
                stage.Tips.Hook(bug, null, stage.T(ready ? "screen.bug.button.tip" : "screen.bug.button.pc.tip"), TipSide.Below, reading: true);
            }
            Refresh();
            return page;
        }

        public void Close() { }

        /// <summary>[뒤로]: 타이틀로(게임 메뉴에서 열었으면 게임 메뉴로).</summary>
        public void Back() => back();

        /// <summary>창 모드로 그 크기를 고른다. 목록에 없는 크기면 아무 일도 없다(false).</summary>
        public bool Pick(int w, int h)
        {
            if (!sizes.Contains((w, h))) return false;
            window.Set(w, h, false);
            Picked = Id(w, h);
            Refresh();
            return true;
        }

        /// <summary>전체 화면을 고른다.</summary>
        public void PickFull()
        {
            var monitor = window.Monitor;
            window.Set(monitor.x, monitor.y, true);
            Picked = Full;
            Refresh();
        }

        // 음량 줄 하나: 슬라이더(0 = 끔 ~ 10) + 지금 값(「끔」 · 숫자 — 숫자뿐이라 문자열 표에 두지 않는다). 누르는 소리는 없다 — 효과음 줄은 고른 음량의 미리 듣기(확정 소리)가 난다
        void AddVolume(UiStage stage, VisualElement panel, string id, Action<int> pick)
        {
            var row = Add(panel, new VisualElement(), "settings-row-" + id, "sn-solo-row");
            Add(row, Kit.Text(stage.T("screen.settings." + id), Kit.F2), null, "sn-solo-label");
            sliders[id] = Add(row, new KitSlider(SoundMix.MaxStep, 0, pick), "settings-slider-" + id, null);
            values[id] = Add(row, Kit.Text("", Kit.F2), "settings-val-" + id, "sn-set-val");
        }

        /// <summary>그 음량 줄(<c>bgm</c>·<c>se</c>)의 슬라이더(확인용).</summary>
        public KitSlider Slider(string id) => sliders.TryGetValue(id, out var s) ? s : null;

        void AddOpt(UiStage stage, VisualElement box, string id, string text, Action pick)
        {
            var opt = Add(box, new VisualElement(), "settings-opt-window-" + id, "sn-opt");
            opt.AddToClassList("sn-set-opt");
            opt.Add(Kit.Text(text, Kit.F2));
            stage.OnTap(opt, SoundKeys.Select, e => pick());
            opts[id] = opt;
        }

        void Refresh()
        {
            foreach (var o in opts) o.Value.EnableInClassList("sn-opt-picked", o.Key == Picked);
            foreach (var o in pieceOpts) o.Value.EnableInClassList("sn-opt-picked", o.Key == Piece);
            foreach (var s in sliders)
            {
                int v = s.Key == "bgm" ? Bgm : Se;
                s.Value.Value = v;
                values[s.Key].text = v == 0 ? offText : v.ToString(CultureInfo.InvariantCulture);
            }
            foreach (var o in fxOpts) o.Value.EnableInClassList("sn-opt-picked", o.Key == Fx);
            foreach (var o in alertOpts) o.Value.EnableInClassList("sn-opt-picked", o.Key == Alert);
            foreach (var o in langOpts) o.Value.EnableInClassList("sn-opt-picked", o.Key == Lang);
            foreach (var o in uploadOpts) o.Value.EnableInClassList("sn-opt-picked", o.Key == Upload);
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
