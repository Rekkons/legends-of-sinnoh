using System;
using System.Threading.Tasks;
using Rekkon.UiKit;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 버그 신고 창의 몸(M9 — 결정 132 ①): 제목 · 「무엇이 잘못됐는지 적어 주십시오.」 · 설명 글 칸(필수 — 비어 있으면 [보내기]가 꺼진다, <see cref="BugReport.MaxDescription"/>자까지) ·
    /// 「지금 판 기록과 로그를 함께 보냅니다.」(마우스 오버 = 보내는 것 목록) · [보내기] [취소].
    /// [보내기] → 「보내는 중…」 → 「보냈습니다. 고맙습니다.」 + [닫기] / 「보내지 못했습니다. 인터넷 연결을 확인해 주십시오.」 + [다시 보내기] [닫기](같은 묶음을 다시 보낸다).
    /// 묶기(<see cref="BugReport.Pack"/> — 파일 읽기 · gzip)는 작업 스레드에서, 올리기는 <see cref="PlayUploads.SendBug"/>.
    /// 게임 메뉴의 쪽(<see cref="BoardMenuScreen.BugPage"/>)과 타이틀 설정에서 연 창(<see cref="BugReportScreen"/>)이 같이 쓴다.
    /// 글 칸은 높이가 고정이고 넘치면 글 칸 안에서 굴린다(결정 136 — 엔진 글 칸의 ScrollView: 휠 · 막대 끌기 · 치거나 화살표로 옮긴 커서를 따라감).
    /// 요소 이름: <c>bug-title</c> · <c>bug-prompt</c> · <c>bug-text</c> · <c>bug-count</c>(「n / 4,000」) · <c>bug-attach</c> · <c>bug-status</c> · <c>bug-foot</c> · <c>bug-send</c> · <c>bug-cancel</c> · <c>bug-retry</c> · <c>bug-close</c>.
    /// </summary>
    public sealed class BugReportPanel
    {
        /// <summary>창의 단계: 적는 중 · 보내는 중 · 보냄 · 실패.</summary>
        public enum Step { Edit, Sending, Sent, Failed }

        readonly GameFlow flow;
        readonly Action close;
        UiStage stage;
        TextField field;
        Label count;
        Label status;
        VisualElement foot;
        Button send;
        Step footFor;
        Func<BugReportPack> make;
        string text = "";

        public BugReportPanel(GameFlow flow, Action close)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.close = close ?? throw new ArgumentNullException(nameof(close));
        }

        /// <summary>지금 단계.</summary>
        public Step State { get; private set; }

        /// <summary>적은 설명.</summary>
        public string Text => text;

        /// <summary>마지막으로 묶은 것(묶기 전 · 묶다 실패하면 null — 시험이 내용을 본다).</summary>
        public BugReportPack Pack { get; private set; }

        /// <summary>묶기를 작업 스레드 대신 그 자리에서(캡처 — 배치 한 장면 안에서 끝나게. 게임은 쓰지 않는다).</summary>
        public bool Inline { get; set; }

        /// <summary>[보내기]가 켜져 있나: 적는 중 · 설명이 빈칸만이 아니다 · 보낼 창구가 있다(PC 플레이어).</summary>
        public bool CanSend => State == Step.Edit && BugReport.CanSend(text) && flow.BugReady;

        public void Build(UiStage stage, VisualElement box)
        {
            this.stage = stage;
            send = null;
            Add(box, Kit.Text(stage.T("screen.bug.title"), Kit.FTitle), "bug-title", "kit-title");
            Add(box, Kit.Wrapped(stage.T("screen.bug.prompt"), Kit.F2), "bug-prompt", "kit-muted");
            // 여러 줄 글 칸은 높이가 고정이고, 넘치는 글은 엔진 글 칸 안의 ScrollView가 굴린다(결정 136 — 휠 · 막대 끌기 · 커서를 따라감) [가정 plan §5 #687]
            field = new TextField { value = text, maxLength = BugReport.MaxDescription, multiline = true, isDelayed = false, verticalScrollerVisibility = ScrollerVisibility.Auto };
            field.AddToClassList("sn-field");
            field.AddToClassList("sn-bug-field");
            Add(box, field, "bug-text", null);
            field.RegisterValueChangedCallback(e =>
            {
                text = Clip(e.newValue);
                if (text != e.newValue) field.SetValueWithoutNotify(text);
                Paint();
            });
            field.RegisterCallback<FocusInEvent>(e => flow.Typing = true);   // 글쇠가 단축키(카메라 · 정비 T · 스페이스 바)로 새지 않게
            field.RegisterCallback<FocusOutEvent>(e => flow.Typing = false);
            count = Add(box, Kit.Text("", Kit.F1), "bug-count", "kit-muted");   // 「n / 4,000」 [가정 plan §5 #686]
            count.AddToClassList("sn-bug-count");
            var attach = Add(box, Kit.Wrapped(stage.T("screen.bug.attach"), Kit.F2), "bug-attach", "kit-muted");
            stage.Tips.Hook(attach, null, stage.T("screen.bug.attach.tip"), TipSide.Below, reading: true);   // 머리 없이 — 그 줄 글을 되풀이했고 영어 1080에서 판 밖으로 나갔다(plan §5 #705)
            status = Add(box, Kit.Wrapped("", Kit.F2), "bug-status", null);
            foot = Add(box, new VisualElement(), "bug-foot", "sn-foot-row");
            Paint();
        }

        /// <summary>창이 닫힐 때(글 칸이 포커스를 잃는 알림이 오지 않을 수 있다).</summary>
        public void Close() => flow.Typing = false;

        /// <summary>설명을 적는다(확인 · 시험 — 사람이 친 것과 같은 길).</summary>
        public void SetText(string value)
        {
            text = Clip(value);
            field?.SetValueWithoutNotify(text);
            Paint();
        }

        /// <summary>[보내기]: 묶어서 보낸다. 켜져 있지 않으면 아무 일도 없다(false로 끝나는 Task). 끝나면 보냄 · 실패 단계.</summary>
        public Task<bool> Send()
        {
            if (!CanSend) return Task.FromResult(false);
            var info = flow.BugInfo(text);
            string log = flow.BugLogPath, dir = flow.BugSaveDir;
            var secrets = flow.BugSecrets();
            make = () => BugReport.Pack(info, log, dir, secrets);
            Pack = null;
            return Go();
        }

        /// <summary>[다시 보내기](실패 뒤): 같은 묶음을 다시 보낸다(묶기부터 실패했으면 다시 묶는다).</summary>
        public Task<bool> Retry()
        {
            if (State != Step.Failed || make == null) return Task.FromResult(false);
            return Go();
        }

        async Task<bool> Go()
        {
            State = Step.Sending;
            Paint();
            bool ok = false;
            try
            {
                if (Pack == null) Pack = Inline ? make() : await Task.Run(make);
                ok = await flow.Uploads.SendBug(Pack.FileName, Pack.Bytes);
            }
            catch (Exception e)
            {
                Debug.Log("[BugReport] 묶지 못함: " + e.Message);
            }
            State = ok ? Step.Sent : Step.Failed;
            Paint();
            return ok;
        }

        /// <summary>상한을 넘는 글은 앞 <see cref="BugReport.MaxDescription"/>자만(붙여넣기 · 코드로 넣은 글 — 치는 글은 글 칸의 maxLength가 막는다) [가정 plan §5 #686].</summary>
        static string Clip(string value)
        {
            value = value ?? "";
            return value.Length > BugReport.MaxDescription ? value.Substring(0, BugReport.MaxDescription) : value;
        }

        void Paint()
        {
            if (stage == null) return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            count.text = stage.T("screen.bug.count").Replace("{n}", text.Length.ToString("N0", inv)).Replace("{max}", BugReport.MaxDescription.ToString("N0", inv));
            if (send != null && footFor == State)
            {
                send.SetEnabled(CanSend);   // 글만 바뀌었다 — 단추 줄은 그대로
                return;
            }
            footFor = State;
            send = null;
            field.SetEnabled(State == Step.Edit);
            status.text = State == Step.Sending ? stage.T("screen.bug.sending")
                : State == Step.Sent ? stage.T("screen.bug.sent")
                : State == Step.Failed ? stage.T("screen.bug.failed") : "";
            status.EnableInClassList("sn-menu-warn", State == Step.Failed);
            status.EnableInClassList("kit-muted", State != Step.Failed);
            status.style.display = status.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foot.Clear();
            if (State == Step.Sent || State == Step.Failed)
            {
                if (State == Step.Failed) Add(foot, Kit.Button(stage.T("screen.bug.retry"), KitButtonKind.Main, () => { _ = Retry(); }), "bug-retry", null);
                Add(foot, Kit.Button(stage.T("screen.bug.close"), KitButtonKind.Normal, close), "bug-close", null);
                return;
            }
            send = Add(foot, Kit.Button(stage.T("screen.bug.send"), KitButtonKind.Main, () => { _ = Send(); }), "bug-send", null);
            send.SetEnabled(CanSend);
            Add(foot, Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, close), "bug-cancel", null);
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
