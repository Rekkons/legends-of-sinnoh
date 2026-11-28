using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 방 채팅 창(결정 140 ② — 로비 S07 · 방 판): 로비는 오른쪽 설정 판 오른쪽에 펼친 판(<see cref="LobbyScreen"/>), 방 판은 덮개 층의 접는 작은 창(<see cref="RoomLayer"/> — 왼쪽 아래, 자원 줄 위).
    /// 몸: 머리(「채팅」 + 방 판의 [접기]) · 기록(높이 고정 ScrollView — 휠 · 막대, 끝에 있으면 새 줄을 따라간다) · 글 칸(<see cref="Rekkon.UiKit"/> 밖 엔진 TextField, 200자) + [보내기].
    /// 줄 = 보낸 사람이 바뀔 때만 머리(작은 얼굴 상자 — 좌석 색 테두리 · 주인공 얼굴 + 이름) + 글. 이름 · 글은 받을 때 적어 둔 것(<see cref="RoomChatLine"/>), 서식 태그는 글자 그대로 보인다.
    /// 글 칸: 포커스 = <see cref="GameFlow.Typing"/>(카메라 · 단축키가 글쇠를 받지 않는다) · Enter = 보내기(다음 프레임 — 한글 조합이 칸에 들어간 뒤) · Esc = 포커스를 놓고 접는다.
    /// 접힌 창: [채팅] 단추 + 안 읽은 수 배지 + 남의 새 줄 미리 보기(<see cref="PreviewMs"/>). 방 설정에서 끄면 창이 사라진다. 기록 칸 · 글 칸은 한 번만 만든다(다시 만들면 포커스 · 조합이 끊긴다).
    /// 요소 이름: <c>chat-tab</c>(+ <c>chat-open</c> · <c>chat-unread</c> · <c>chat-preview</c>) · <c>chat-panel</c> · <c>chat-head</c> · <c>chat-fold</c> · <c>chat-list</c> · <c>chat-empty</c> · <c>chat-line-&lt;n&gt;</c>(+ <c>-who</c> · <c>-name</c> · <c>-text</c>) ·
    /// <c>chat-foot</c> · <c>chat-field</c> · <c>chat-send</c> · <c>chat-wait</c>.
    /// </summary>
    public sealed class ChatBox
    {
        /// <summary>접힌 창의 새 줄 미리 보기 시간(ms) [가정 plan §5 #755].</summary>
        public const long PreviewMs = 5000;
        /// <summary>연타 제한에 걸렸을 때 안내 줄을 보이는 시간(ms).</summary>
        const long WaitShowMs = 2000;

        readonly GameFlow flow;
        readonly RoomLive live;
        readonly bool foldable;
        UiStage stage;
        VisualElement root, tab, panel;
        Label unread, preview, empty, wait;
        ScrollView list;
        TextField field;
        int drawnSeen, readSeen, lastSeat = int.MinValue, refocusFrame = -10, escFrame = -10;
        readonly List<VisualElement> rows = new List<VisualElement>();
        long previewUntil = -1, waitUntil = -1;
        bool focused, stick = true;

        public ChatBox(GameFlow flow, RoomLive live, bool foldable)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.live = live ?? throw new ArgumentNullException(nameof(live));
            this.foldable = foldable;
        }

        public RoomLive Live => live;
        public VisualElement Element => root;
        public TextField Field => field;
        public ScrollView List => list;

        /// <summary>펼쳐져 있나(로비는 늘).</summary>
        public bool Open { get; private set; }

        /// <summary>창이 보이나(방 설정이 켰고 가리지 않았다).</summary>
        public bool Shown => root != null && root.style.display != DisplayStyle.None;

        /// <summary>글 칸에 포커스가 있나.</summary>
        public bool Focused => focused;

        /// <summary>안 읽은 줄 수(접힌 동안 받은 남의 줄 — 펼치면 0).</summary>
        public int Unread { get; private set; }

        /// <summary>미리 보기 글(접힌 창 — 없으면 빈 글).</summary>
        public string PreviewText => preview != null && preview.style.display != DisplayStyle.None ? preview.text : "";

        /// <summary>글 칸의 지금 글.</summary>
        public string Draft => field != null ? field.value : "";

        /// <summary>덮개(재접속)가 가리면 창을 숨긴다(방 판).</summary>
        public bool Hidden { get; set; }

        /// <summary>창을 만든다(open = 펼친 채 · draft = 글 칸에 남아 있던 글 — 언어를 바꿔 다시 만들 때).</summary>
        public VisualElement Build(UiStage stage, bool open, string draft = "")
        {
            this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
            root = new VisualElement { name = "chat", pickingMode = PickingMode.Ignore };
            root.AddToClassList("sn-chat");
            root.AddToClassList(foldable ? "sn-chat-game" : "sn-chat-lobby");
            if (foldable)
            {
                tab = Add(root, new VisualElement { pickingMode = PickingMode.Ignore }, "chat-tab", "sn-chat-tab");
                var openBtn = Add(tab, Kit.Button(stage.T("screen.room.chat.title"), KitButtonKind.Normal, () => Unfold()), "chat-open", null);
                stage.Tips.Hook(openBtn, stage.T("screen.room.chat.title"), stage.T("screen.room.chat.tip"), TipSide.Above, reading: true);
                unread = Add(tab, Kit.Badge("", KitBadgeKind.Gold), "chat-unread", "sn-chat-unread");
                unread.pickingMode = PickingMode.Ignore;
                preview = Add(tab, Kit.Text("", Kit.F2), "chat-preview", "sn-chat-preview");
                preview.pickingMode = PickingMode.Ignore;
                preview.enableRichText = false;
            }
            panel = Add(root, Kit.Panel(stage.Skin), "chat-panel", "sn-chat-panel");
            var head = Add(panel, new VisualElement(), "chat-head", "sn-chat-head");
            var title = Add(head, Kit.Text(stage.T("screen.room.chat.title"), Kit.FTitle), "chat-title", "sn-chat-title");
            if (!foldable) stage.Tips.Hook(title, stage.T("screen.room.chat.title"), stage.T("screen.room.chat.tip"), TipSide.Below, reading: true);
            if (foldable) Add(head, Kit.Button(stage.T("screen.common.fold"), KitButtonKind.Normal, () => Fold()), "chat-fold", null);
            list = Add(panel, new ScrollView(ScrollViewMode.Vertical), "chat-list", "sn-chat-list");
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.verticalScrollerVisibility = ScrollerVisibility.Auto;
            list.verticalScroller.valueChanged += v => stick = v >= list.verticalScroller.highValue - 2f;
            // 끝에 있었으면 새 줄을 따라간다 — 막대의 끝 값은 ScrollView가 같은 배치 알림에서 고치니 다음 틱에 한 번 더
            list.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!stick) return;
                ScrollToEnd();
                list.schedule.Execute(() => { if (stick) ScrollToEnd(); });
            });
            empty = Add(list, Kit.Wrapped(stage.T("screen.room.chat.empty"), Kit.F2), "chat-empty", "kit-muted");
            wait = Add(panel, Kit.Text(stage.T("screen.room.chat.wait"), Kit.F1), "chat-wait", "kit-muted");
            wait.style.display = DisplayStyle.None;
            var foot = Add(panel, new VisualElement(), "chat-foot", "sn-chat-foot");
            field = new TextField { maxLength = Sinnoh.Rules.Session.ChatRules.MaxLength, multiline = false, isDelayed = false, value = draft ?? "" };
            field.AddToClassList("sn-field");
            field.AddToClassList("sn-chat-field");
            Add(foot, field, "chat-field", null);
            field.RegisterCallback<FocusInEvent>(e => { focused = true; flow.Typing = true; });
            field.RegisterCallback<FocusOutEvent>(e => { focused = false; flow.Typing = false; });
            field.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            Add(foot, Kit.Button(stage.T("screen.room.chat.send"), KitButtonKind.Main, () => Submit()), "chat-send", null);
            drawnSeen = live.ChatSeen - live.Chat.Count;   // 기억하는 줄은 모두 그린다(안 읽은 수는 지금부터)
            readSeen = live.ChatSeen;
            Open = !foldable || open;
            Lay();
            Tick();
            return root;
        }

        // Enter = 보내기(한 프레임 뒤 — IME가 조합 중인 글자를 칸에 넣은 뒤), Esc = 포커스를 놓고 접기
        void OnKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n' || e.character == '\r')
            {
                refocusFrame = Time.frameCount;
                field.schedule.Execute(() => { Submit(); if (Time.frameCount - refocusFrame <= 2 && !focused) field.Focus(); });
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                escFrame = Time.frameCount;
                Escape();
            }
        }

        /// <summary>글 칸의 글을 보낸다([보내기] · Enter). 보냈으면 칸을 비우고 true. 끈 방 · 빈 글이면 false, 연타 제한이면 글을 두고 안내 줄.</summary>
        public bool Submit()
        {
            if (field == null) return false;
            string text = field.value;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!live.SendChat(text))
            {
                if (live.ChatOn && live.ChatWaitMs > 0)
                {
                    waitUntil = live.NowMs + WaitShowMs;
                    wait.style.display = DisplayStyle.Flex;
                }
                return false;
            }
            field.value = "";
            stick = true;
            return true;
        }

        /// <summary>글 칸에 글을 넣는다(확인 · 시험 — 사람이 친 것과 같은 값 바꾸기).</summary>
        public void SetText(string text)
        {
            if (field != null) field.value = text ?? "";
        }

        /// <summary>Esc(<see cref="GameFlow.Escape"/>가 먼저 묻는다): 글 칸에 포커스가 있었으면 놓고(방 판은 접는다) true. 아니면 false — 다른 창의 Esc로 간다.</summary>
        public bool Escape()
        {
            if (!focused && Time.frameCount != escFrame) return false;   // 같은 프레임에 글 칸이 먼저 Esc를 받아 포커스를 놓았어도 그 Esc는 채팅 몫
            Blur();
            if (foldable) Fold();
            return true;
        }

        /// <summary>펼친다(방 판 — 안 읽은 수를 지우고 글 칸에 포커스).</summary>
        public void Unfold()
        {
            if (Open) return;
            Open = true;
            stick = true;
            Lay();
            Tick();
            field.Focus();
        }

        /// <summary>접는다(방 판).</summary>
        public void Fold()
        {
            if (!foldable || !Open) return;
            Blur();
            Open = false;
            Lay();
        }

        void Blur()
        {
            if (field != null && focused) field.Blur();
            focused = false;
            flow.Typing = false;
        }

        /// <summary>창이 없어질 때(로비를 닫음 · 층을 걷음): 글 치기 표시를 끈다.</summary>
        public void Close()
        {
            if (focused) flow.Typing = false;
            focused = false;
        }

        void Lay()
        {
            panel.style.display = Open ? DisplayStyle.Flex : DisplayStyle.None;
            if (tab != null) tab.style.display = Open ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>매 프레임(로비 Sync · 덮개 층 Tick): 켜짐 · 새 줄 · 안 읽은 수 · 미리 보기 · 안내 줄.</summary>
        public void Tick()
        {
            if (root == null) return;
            bool on = live.ChatOn && !Hidden;
            root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on && focused) Blur();
            long now = live.NowMs;
            if (waitUntil >= 0 && now >= waitUntil)
            {
                waitUntil = -1;
                wait.style.display = DisplayStyle.None;
            }
            int fresh = live.ChatSeen - drawnSeen;
            if (fresh > 0)
            {
                var lines = live.Chat;
                for (int i = Math.Max(0, lines.Count - fresh); i < lines.Count; i++) Line(lines[i], live.ChatSeen - lines.Count + i);
                drawnSeen = live.ChatSeen;
                while (rows.Count > RoomLive.KeepChat)
                {
                    rows[0].RemoveFromHierarchy();
                    rows.RemoveAt(0);
                }
                empty.style.display = rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (stick) ScrollToEnd();
            }
            empty.style.display = rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (Open) readSeen = live.ChatSeen;
            else if (readSeen < live.ChatSeen)
            {
                // 접힌 동안 받은 남의 줄: 안 읽은 수 + 마지막 줄 미리 보기(내 줄은 세지 않는다)
                var lines = live.Chat;
                RoomChatLine last = null;
                for (int i = Math.Max(0, lines.Count - (live.ChatSeen - readSeen)); i < lines.Count; i++)
                    if (lines[i].Seat != live.Seat)
                    {
                        Unread++;
                        last = lines[i];
                    }
                readSeen = live.ChatSeen;
                if (last != null)
                {
                    preview.text = NameOf(last) + ": " + last.Text;
                    previewUntil = now + PreviewMs;
                }
            }
            if (Open) Unread = 0;
            if (unread != null)
            {
                unread.text = Unread > 99 ? "99+" : Unread.ToString(System.Globalization.CultureInfo.InvariantCulture);
                unread.style.display = !Open && Unread > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                bool show = !Open && previewUntil >= 0 && now < previewUntil;
                if (!show) previewUntil = -1;
                preview.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>기록을 끝 줄로 굴린다(새 줄 · 펼칠 때 — 끝에 있었으면 따라간다).</summary>
        public void ScrollToEnd()
        {
            if (list == null) return;
            list.verticalScroller.value = list.verticalScroller.highValue;
            stick = true;
        }

        string NameOf(RoomChatLine l) => !string.IsNullOrEmpty(l.Name) ? l.Name : stage.T("screen.board.seat").Replace("{n}", (l.Seat + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));

        // 줄 하나: 보낸 사람이 앞 줄과 다르면 머리(얼굴 상자 + 이름), 그 아래 글(서식 태그를 풀지 않는다)
        void Line(RoomChatLine l, int serial)
        {
            string n = serial.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var row = Add(list, new VisualElement(), "chat-line-" + n, "sn-chat-line");
            row.userData = l;
            if (l.Seat != lastSeat || rows.Count == 0)
            {
                var who = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "chat-line-" + n + "-who", "sn-chat-who");
                var face = FaceBox.Tag();
                FaceBox.Paint(face, Math.Max(0, l.Seat), l.Color, l.Protagonist);
                who.Add(face);
                var name = Add(who, Kit.Text(NameOf(l), Kit.F2), "chat-line-" + n + "-name", "sn-chat-name");
                name.enableRichText = false;
            }
            lastSeat = l.Seat;
            var text = Add(row, Kit.Wrapped(l.Text, Kit.F2), "chat-line-" + n + "-text", "sn-chat-text");
            text.enableRichText = false;
            rows.Add(row);
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
