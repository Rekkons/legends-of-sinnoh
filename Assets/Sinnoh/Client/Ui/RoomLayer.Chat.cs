namespace Sinnoh.Client
{
    /// <summary>
    /// ⑦ 방 채팅(결정 140 ②): 방 판의 덮개 층 왼쪽 아래(자원 줄 위)의 접는 작은 창(<see cref="ChatBox"/>) — 접힌 창 = [채팅] + 안 읽은 수 + 남의 새 줄 미리 보기.
    /// 층이 문서 뿌리 맨 위라 배틀 화면 · 물음 창 위에서도 열리고(결정 125 · T35), 창 밖은 마우스를 받지 않는다. 방 설정에서 끄면 창이 사라지고, 재접속 덮개(S11)가 뜨면 숨는다.
    /// 언어를 바꿔 층을 다시 만들어도 펼침 · 글 칸의 글은 이어진다.
    /// </summary>
    public sealed partial class RoomLayer
    {
        ChatBox chat;
        bool chatOpen;
        string chatDraft = "";

        /// <summary>방 판의 채팅 창(방 판이 아니면 null).</summary>
        public ChatBox Chat => chat;

        // 층에서 걷는다(방 판을 떠남 · 언어 바꿈) — 펼침 · 쓰던 글은 쥐어 둔다
        void ChatOff()
        {
            if (chat == null) return;
            chatOpen = chat.Open;
            chatDraft = chat.Draft;
            chat.Close();
            chat.Element?.RemoveFromHierarchy();
            chat = null;
        }

        // 방 판을 떠났다 — 펼침 · 쓰던 글도 잊는다
        void ChatGone()
        {
            ChatOff();
            chatOpen = false;
            chatDraft = "";
        }

        void ChatTick(RoomLive live, bool covered)
        {
            if (chat != null && (chat.Live != live || chat.Element.parent != layer)) ChatOff();
            if (chat == null)
            {
                chat = new ChatBox(flow, live, foldable: true);
                var e = chat.Build(Stage, chatOpen, chatDraft);
                e.name = "room-chat";
                layer.Insert(layer.IndexOf(cover), e);   // 재접속 덮개 밑
            }
            chat.Hidden = covered;
            chat.Tick();
        }

        /// <summary>Esc(<see cref="GameFlow.Escape"/>가 먼저): 채팅 글 칸에 포커스가 있으면 놓고 접는다 — 그랬으면 true.</summary>
        public bool ChatEscape() => chat != null && chat.Escape();
    }
}
