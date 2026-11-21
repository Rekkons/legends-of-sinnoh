using System;
using System.Collections.Generic;
using System.Text;

namespace Sinnoh.Rules.Session
{
    /// <summary>
    /// 손님 → 호스트: 방 채팅 한 줄(결정 140 ② — 로비 · 게임 중, 방 설정 <see cref="RoomSettings.Chat"/>이 켜진 방만). 게임 명령이 아니다 — 엔진 · 세이브 · 리플레이 · 시계 · 자리 비움과 무관한 방 층 메시지이고,
    /// 방장은 지난 글을 기억하지 않는다(늦게 들어온 손님 · 재접속은 지난 글을 받지 않는다). 끈 방 · 빈 글 · 연타 제한에 걸린 것은 방이 조용히 버린다.
    /// </summary>
    public sealed class SendChat : RoomMsg
    {
        public string Text;
    }

    /// <summary>
    /// 호스트 → 전원(보낸 좌석 포함 — 화면은 받은 것만 그린다): 그 자리의 글. From = 보낼 때의 방 단계의 자리 번호(로비 · 빈 자리가 섞인 6칸 번호, 게임 중 · 대기실 = 좌석 번호) —
    /// 받는 쪽은 받을 때의 방 상태로 닉네임 · 주인공을 읽어 줄에 함께 적어 둔다(뒤에 번호가 바뀌어도 줄은 그대로). 글은 방장이 다듬은 것(<see cref="ChatRules.Clean"/>).
    /// </summary>
    public sealed class ChatMsg : ServerMsg
    {
        public int From;
        public string Text;
    }

    /// <summary>방 채팅 규칙(결정 140 ②): 한 줄 길이 · 다듬기 · 연타 제한 [가정 plan §5 #751 · #752].</summary>
    public static class ChatRules
    {
        /// <summary>한 줄 최대 글자 수(UTF-16 — 한글 한 자 = 1) [가정 #751].</summary>
        public const int MaxLength = 200;

        /// <summary>연타 제한(좌석마다): 1초에 1번 + 10초에 5번 — 도장과 같은 꼴 [가정 #752]. 방장은 <see cref="SocialRate.HostSlackMs"/> 여유를 둔다.</summary>
        public const int GapMs = 1000, WindowMs = 10000, Max = 5;

        /// <summary>
        /// 글 다듬기: 제어 글자(줄바꿈 · 탭 포함)는 빈칸 하나로, 앞뒤 빈칸을 빼고 <see cref="MaxLength"/>자에서 자른다(서로게이트 쌍은 가르지 않는다). 남는 글이 없으면 null.
        /// </summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var sb = new StringBuilder(Math.Min(text.Length, MaxLength + 8));
            foreach (char c in text) sb.Append(char.IsControl(c) ? ' ' : c);
            string s = sb.ToString().Trim();
            if (s.Length == 0) return null;
            if (s.Length > MaxLength)
            {
                int n = MaxLength;
                if (char.IsHighSurrogate(s[n - 1])) n--;
                s = s.Substring(0, n).TrimEnd();
            }
            return s.Length > 0 ? s : null;
        }

        /// <summary>방장: 받은 글을 넘길 메시지로 새로 만든다. 끈 방 · 빈 글이면 null(연타 제한은 부르는 쪽이 <see cref="ChatRate"/>로).</summary>
        public static ChatMsg Relay(SendChat m, int from, RoomSettings settings)
        {
            if (m == null || from < 0 || settings == null || !settings.Chat) return null;
            string text = Clean(m.Text);
            return text == null ? null : new ChatMsg { From = from, Text = text };
        }
    }

    /// <summary>채팅 연타 제한(<see cref="ChatRules.GapMs"/> · <see cref="ChatRules.WindowMs"/> · <see cref="ChatRules.Max"/>) — 보내는 화면(여유 0)과 방장(여유 0.5초)이 같은 표를 쓴다. 좌석마다 따로, 시각은 부르는 쪽이 준다.</summary>
    public sealed class ChatRate
    {
        readonly int slack;
        readonly Dictionary<int, List<long>> times = new Dictionary<int, List<long>>();

        public ChatRate(int slackMs = 0) { slack = slackMs; }

        List<long> ListOf(int seat)
        {
            if (!times.TryGetValue(seat, out var list)) times[seat] = list = new List<long>();
            return list;
        }

        /// <summary>다시 보낼 수 있을 때까지 남은 ms(지금 되면 0). 적지 않는다.</summary>
        public long WaitMs(int seat, long nowMs)
        {
            long gap = Math.Max(0, ChatRules.GapMs - slack), win = Math.Max(0, ChatRules.WindowMs - slack);
            var list = ListOf(seat);
            list.RemoveAll(t => nowMs - t >= win || t > nowMs);
            long wait = 0;
            if (gap > 0 && list.Count > 0) wait = Math.Max(wait, list[list.Count - 1] + gap - nowMs);
            if (list.Count >= ChatRules.Max) wait = Math.Max(wait, list[0] + win - nowMs);
            return Math.Max(0, wait);
        }

        /// <summary>지금 보내도 되면 적고 true. 걸리면 적지 않고 false.</summary>
        public bool TryTake(int seat, long nowMs)
        {
            if (WaitMs(seat, nowMs) > 0) return false;
            ListOf(seat).Add(nowMs);
            return true;
        }

        public void Clear() => times.Clear();
    }
}
