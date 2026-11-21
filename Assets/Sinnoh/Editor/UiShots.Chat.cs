using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 결정 140 ② 방 채팅(<see cref="ChatBox"/>) 확인 장면 — 망 없이 가짜 방(<see cref="RoomLive"/> 가짜)에 받은 줄을 넣는다(<see cref="RoomLive.TakeChat"/>):
    /// 로비(방장 · 손님 · 끈 방) · 방 판(접힘 · 새 글 표시 · 펼침 · 긴 기록 · 손님 · 끈 방 · 배틀 화면 위). 장면마다 창이 화면 안 · 글 넘침 없음 · 로비 판 · HUD 판과 겹치지 않음을 단정한다. 모두 보드 위 화면이다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] ChatScreens =
        {
            Screen("chat-lobby-host", s => ChatLobby(s, true, true)),        // 로비 방장: 방 설정 판 오른쪽에 펼친 채팅 판 · 줄 넷(보낸 사람이 바뀔 때만 얼굴 + 이름) · 설정 「채팅: 사용」
            Screen("chat-lobby-guest", s => ChatLobby(s, false, true)),      // 로비 손님(3번 자리): 같은 판 · 설정은 읽기만
            Screen("chat-lobby-off", s => ChatLobby(s, true, false)),        // 끈 방(기본): 채팅 판이 없다 · 설정 「사용 안 함」
            Screen("chat-game-fold", s => ChatGame(s, true, true, c =>       // 방 판: 왼쪽 아래 자원 줄 위의 접힌 [채팅](앞 줄은 읽음 — 새 글 표시 없음)
            {
                c.Unfold();
                c.Fold();
                c.Tick();
                if (c.Unread != 0 || c.PreviewText.Length > 0) throw new InvalidOperationException("읽은 뒤 새 글 표시: " + c.Unread + " / " + c.PreviewText);
            })),
            Screen("chat-game-new", s => ChatGame(s, true, true, c =>        // 읽은 뒤 접힌 창에 남의 새 줄 둘: 안 읽은 수 2 + 마지막 줄 미리 보기
            {
                c.Unfold();
                c.Fold();
                c.Live.TakeChat(new ChatMsg { From = 1, Text = "다음 턴에 사당 갈게요" });
                c.Live.TakeChat(new ChatMsg { From = 2, Text = "저는 마을에서 회복하고 바로 뒤따라가겠습니다. 길이 긴 줄은 미리 보기에서 잘린다" });
                c.Tick();
                if (c.Unread != 2 || !c.PreviewText.StartsWith("용식: ", StringComparison.Ordinal)) throw new InvalidOperationException("안 읽은 수 · 미리 보기: " + c.Unread + " / " + c.PreviewText);
            })),
            Screen("chat-game-open", s => ChatGame(s, true, true, c => c.Unfold())),   // 펼친 창: 머리 · 기록 · 글 칸 + [보내기]
            Screen("chat-game-long", s => ChatGame(s, true, true, c =>       // 긴 기록 60줄: 높이 고정 · 세로 막대 · 끝 줄이 보인다
            {
                for (int i = 0; i < 60; i++) c.Live.TakeChat(new ChatMsg { From = i % 3, Text = "긴 기록 " + (i + 1) + "번째 줄 — 넘치면 기록 칸 안에서 굴러간다" });
                c.Unfold();
            }, longList: true)),
            Screen("chat-game-guest", s => ChatGame(s, false, true, c => c.Unfold())),   // 손님 화면(좌석 2)
            Screen("chat-game-off", s => ChatGame(s, true, false, c => { })),            // 끈 방: 창이 없다
            Screen("chat-battle-fold", s => ChatBattle(s, false)),                      // 배틀 화면: 접힌 [채팅]이 기술 줄 · 독을 가리지 않는다
            Screen("chat-battle-open", s => ChatBattle(s, true)),                       // 배틀 화면 위에서 펼친 창(층이 문서 맨 위 — 배틀 · 물음 창 위에서도 열린다)
        };

        static void ChatBattle(UiShot s, bool open)
        {
            var flow = ToBattle(s);
            var live = ChatFake(flow, true, true);
            flow.Room.ShowGame(live);
            ChatLines(live);
            ChatSettle(s, flow);
            var chat = flow.RoomLayer.Chat ?? throw new InvalidOperationException("배틀 화면에 채팅 창이 없다");
            if (open) chat.Unfold();
            ChatSettle(s, flow);
            ChatFit(s, flow, open ? "chat-panel" : "chat-tab");
            if (!open) Apart(flow.Stage.Root, "chat-tab", "battle-dock");
        }

        // 좌석의 주인공(채팅 줄의 얼굴 — 방 상태의 값)
        static readonly string[] ChatHeroes = { "pt_player_male", "pt_player_female", "hgss_000_ethan" };

        static void ChatDress(RoomLive live, bool on)
        {
            live.Room.Settings.Chat = on;
            for (int i = 0; i < live.Room.Seats.Count && i < ChatHeroes.Length; i++) live.Room.Seats[i].Protagonist = ChatHeroes[i];
        }

        // 가짜 방 판 상태(좌석 셋 — 방장트레이너 · 빛나 · 용식)에 채팅을 켜고 끈 것
        static RoomLive ChatFake(GameFlow flow, bool host, bool on)
        {
            var live = FakeGame(flow, host);
            ChatDress(live, on);
            return live;
        }

        // 받은 줄 넷(방장 둘 이어서 · 빛나 · 용식 — 한글 · 영어 · 서식 태그 글자)
        static void ChatLines(RoomLive live)
        {
            live.TakeChat(new ChatMsg { From = 0, Text = "다들 준비됐나요?" });
            live.TakeChat(new ChatMsg { From = 0, Text = "이번 판은 신오의 심장, 제한 시간 느긋하게입니다." });
            live.TakeChat(new ChatMsg { From = 1, Text = "네! <b>준비</b> 완료" });
            live.TakeChat(new ChatMsg { From = 2, Text = "Ready when you are :)" });
        }

        static GameFlow ChatLobby(UiShot s, bool host, bool on)
        {
            var flow = s.Flow();
            flow.OpenTitle();
            var live = new RoomLive(() => RoomNow)
            {
                IsHost = host, Seat = host ? 0 : 2, JoinCode = host ? "Q7K2MZ" : null, Listed = host, RoomAtMs = RoomNow,
                Room = new RoomState
                {
                    Phase = RoomPhase.Lobby, RoomId = "room-shot", Settings = new RoomSettings { Seats = 4, Chat = on },
                    Seats = new List<RoomSeat>
                    {
                        new RoomSeat { Nickname = "방장트레이너", Host = true, Connected = true, Protagonist = "pt_player_male" },
                        new RoomSeat { Nickname = "빛나", Ready = true, Connected = true, Protagonist = "pt_player_female" },
                        new RoomSeat { Nickname = "용식", Connected = true, Protagonist = "hgss_000_ethan" },
                        null, null, null,
                    },
                },
            };
            ChatLines(live);
            flow.Room.ShowLobby(live);
            var lobby = flow.Stage.Current as LobbyScreen ?? throw new InvalidOperationException("로비가 열리지 않았다");
            lobby.Sync();
            s.Panel.Tick(3);
            RoomFit(s, flow, "lobby-col");
            RoomFit(s, flow, "lobby-set");
            if (flow.Stage.Root.Q("room-set-opt-chat-" + (on ? "on" : "off")) == null) throw new InvalidOperationException("방 설정 판의 채팅 줄");
            if (lobby.Chat == null) throw new InvalidOperationException("로비 채팅 판이 없다");
            if (lobby.Chat.Shown != on) throw new InvalidOperationException("채팅 판 보임 = 설정: " + lobby.Chat.Shown);
            if (!on) return flow;
            lobby.Chat.ScrollToEnd();
            s.Panel.Tick(3);
            ChatFit(s, flow, "chat");
            Apart(flow.Stage.Root, "lobby-set", "chat");
            Apart(flow.Stage.Root, "lobby-col", "chat");
            ChatRows(flow, 4, 3);
            return flow;
        }

        static GameFlow ChatGame(UiShot s, bool host, bool on, Action<ChatBox> act, bool longList = false)
        {
            var flow = RoomGame(s, l => ChatDress(l, on), host);
            var live = flow.Room.Live;
            ChatLines(live);
            ChatSettle(s, flow);
            var chat = flow.RoomLayer.Chat ?? throw new InvalidOperationException("방 판 채팅 창이 없다");
            if (chat.Shown != on) throw new InvalidOperationException("채팅 창 보임 = 설정: " + chat.Shown);
            if (!on) return flow;
            act(chat);
            ChatSettle(s, flow);
            chat.Tick();
            if (chat.Open)
            {
                ChatFit(s, flow, "chat-panel");
                ChatRows(flow, longList ? -1 : 4, -1);
                var list = chat.List;
                if (longList)
                {
                    if (list.verticalScroller.resolvedStyle.display == DisplayStyle.None || list.verticalScroller.highValue <= 0) throw new InvalidOperationException("긴 기록에 세로 막대가 없다");
                    var last = flow.Stage.Root.Q("chat-line-" + (live.ChatSeen - 1)).worldBound;
                    var view = list.contentViewport.worldBound;
                    if (last.yMax > view.yMax + 1f || last.yMin < view.yMin - 1f) throw new InvalidOperationException("끝 줄이 보이지 않는다: " + last + " / " + view);
                }
            }
            else ChatFit(s, flow, "chat-tab");
            // 왼쪽 아래 자리: 자원 줄 · 아래 판 · 기록 판과 겹치지 않는다
            string box = chat.Open ? "chat-panel" : "chat-tab";
            Apart(flow.Stage.Root, box, "board-hud-res");
            Apart(flow.Stage.Root, box, "board-hud-dock");
            Apart(flow.Stage.Root, box, "board-hud-log");
            return flow;
        }

        static void ChatSettle(UiShot s, GameFlow flow)
        {
            for (int i = 0; i < 2; i++)
            {
                Pass(flow, 0);
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
            }
        }

        // 창(이름)이 화면 안 · 글 넘침 없음
        static void ChatFit(UiShot s, GameFlow flow, string name)
        {
            s.Panel.Tick(3);
            var e = flow.Stage.Root.Q(name) ?? throw new InvalidOperationException("요소 없음: " + name);
            var bad = WorkUi.Overflows(e);
            if (bad.Count > 0) throw new InvalidOperationException(name + " 글 넘침: " + string.Join(" / ", bad));
            var root = flow.Stage.Root.worldBound;
            var at = e.worldBound;
            if (at.width < 1f || at.xMin < root.xMin - 1f || at.yMin < root.yMin - 1f || at.xMax > root.xMax + 1f || at.yMax > root.yMax + 1f)
                throw new InvalidOperationException(name + "이(가) 화면 밖이거나 없다: " + at + " / " + root);
        }

        // 그려진 줄 수(−1 = 세지 않음) · 머리(얼굴 + 이름) 수(−1 = 세지 않음) · 글 · 이름은 서식을 풀지 않는다
        static void ChatRows(GameFlow flow, int lines, int heads)
        {
            var list = flow.Stage.Root.Q("chat-list");
            int n = 0, h = 0;
            list.Query(className: "sn-chat-line").ForEach(e => n++);
            list.Query(className: "sn-chat-who").ForEach(e => h++);
            if (lines >= 0 && n != lines) throw new InvalidOperationException("채팅 줄 수 " + n + " ≠ " + lines);
            if (heads >= 0 && h != heads) throw new InvalidOperationException("채팅 머리 수 " + h + " ≠ " + heads);
            list.Query<Label>(className: "sn-chat-text").ForEach(l => { if (l.enableRichText) throw new InvalidOperationException("채팅 글이 서식 태그를 푼다"); });
        }
    }
}
