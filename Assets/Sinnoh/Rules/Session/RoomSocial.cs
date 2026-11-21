using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>
    /// 소통(결정 102, 02 §4-8 — isekai 02 §3-7 「소통」을 옮김): 핑 · 감정 표현(정해진 말 일곱 줄을 좌석 얼굴 옆 말풍선으로) · 도장(남의 행동 기록 줄에 대단해 · 헉 · 아깝다).
    /// 게임 명령이 아니다 — 엔진 · 세이브 · 리플레이 · 시계와 무관한 방 층 메시지이고, 방장은 지난 소통을 기억하지 않는다(재접속 · 이어하기 뒤 다시 보내지 않는다).
    /// 혼자 하기 · 튜토리얼에는 없다. 화면 이름은 「감정 표현」(「여행 스탬프」와 겹치지 않게 — 식별자 Emote), 도장 = Mark.
    /// </summary>
    public enum SocialKind { Ping = 0, Emote = 1, Mark = 2 }

    /// <summary>종류마다의 코드 수 [0, n). 문구는 화면 몫(<c>screen.room.ping.kind.*</c> · <c>emote.line.*</c> · <c>mark.kind.*</c>) — Ping 0 여기 봐 · 1 ? · 2 좋아 · 3 조심, Emote 0~6, Mark 0 대단해 · 1 헉 · 2 아깝다.</summary>
    public static class SocialCodes
    {
        public const int Ping = 4, Emote = 7, Mark = 3;

        public static int Of(SocialKind kind) => kind == SocialKind.Ping ? Ping : kind == SocialKind.Emote ? Emote : kind == SocialKind.Mark ? Mark : 0;
    }

    /// <summary>
    /// 연타 제한 — 보내는 화면(여유 0)과 방장(여유 <see cref="HostSlackMs"/> — 전선 위에서 간격이 줄어도 화면이 보낸 것을 버리지 않게)이 같은 표를 쓴다:
    /// 핑 5초에 3번 · 감정 표현 2초에 1번 + 20초에 5번 · 도장 1초에 1번 + 10초에 5번. 좌석 · 종류마다 따로 센다. 시각은 부르는 쪽이 준다.
    /// </summary>
    public sealed class SocialRate
    {
        public const int HostSlackMs = 500;

        public struct Limit { public int GapMs, WindowMs, Max; }

        public static Limit Of(SocialKind kind)
        {
            switch (kind)
            {
                case SocialKind.Emote: return new Limit { GapMs = 2000, WindowMs = 20000, Max = 5 };
                case SocialKind.Mark: return new Limit { GapMs = 1000, WindowMs = 10000, Max = 5 };
                default: return new Limit { GapMs = 0, WindowMs = (int)PingRules.WindowMs, Max = PingRules.Burst };
            }
        }

        readonly int slack;
        readonly Dictionary<long, List<long>> times = new Dictionary<long, List<long>>();

        public SocialRate(int slackMs = 0) { slack = slackMs; }

        List<long> ListOf(int seat, SocialKind kind)
        {
            long key = seat * 16L + (int)kind;
            if (!times.TryGetValue(key, out var list)) times[key] = list = new List<long>();
            return list;
        }

        /// <summary>다시 보낼 수 있을 때까지 남은 ms(지금 되면 0). 적지 않는다.</summary>
        public long WaitMs(int seat, SocialKind kind, long nowMs)
        {
            var lim = Of(kind);
            long gap = Math.Max(0, lim.GapMs - slack), win = Math.Max(0, lim.WindowMs - slack);
            var list = ListOf(seat, kind);
            list.RemoveAll(t => nowMs - t >= win || t > nowMs);
            long wait = 0;
            if (gap > 0 && list.Count > 0) wait = Math.Max(wait, list[list.Count - 1] + gap - nowMs);
            if (list.Count >= lim.Max) wait = Math.Max(wait, list[0] + win - nowMs);
            return Math.Max(0, wait);
        }

        /// <summary>지금 보내도 되면 적고 true. 걸리면 적지 않고 false.</summary>
        public bool TryTake(int seat, SocialKind kind, long nowMs)
        {
            if (WaitMs(seat, kind, nowMs) > 0) return false;
            ListOf(seat, kind).Add(nowMs);
            return true;
        }

        public void Clear() => times.Clear();
    }

    /// <summary>
    /// 손님 → 호스트: 소통 하나(게임 중 · 결과 화면 · 일시정지 중에도 — 좌석이 있는 연결). 로비 · 틀린 코드 · 허용 목록 밖 대상 · 연타 제한에 걸린 것은 방이 조용히 버린다.
    /// Ping = Code + <see cref="Target"/> · Emote = Code만 · Mark = Code + <see cref="Seq"/>(행동 번호 — 기록 줄) + <see cref="Owner"/>(그 줄의 주인 좌석 — 자기 행동에는 못 찍는다).
    /// </summary>
    public sealed class SendSocial : RoomMsg
    {
        public SocialKind Kind;
        public int Code;
        public PingTarget Target;
        public int Seq = -1;
        public int Owner = -1;
    }

    /// <summary>호스트 → 전원(보낸 좌석 포함): 그 좌석의 소통. 상태가 아니다 — 늦게 들어온 손님은 받지 않는다. 대상은 방장이 허용된 칸으로 새로 만든 것이다.</summary>
    public sealed class SocialMsg : ServerMsg
    {
        public int From;
        public SocialKind Kind;
        public int Code;
        public PingTarget Target;
        public int Seq = -1;
        public int Owner = -1;
    }

    /// <summary>
    /// 상대별 숨김(결정 102 — isekai #190): 그 좌석의 반응(감정 표현 · 도장)과 핑을 따로 숨긴다. 받는 쪽 거르기일 뿐이다 — 상대에게 알리지 않고 통신 · 세이브는 그대로, 판이 끝나면 잊는다.
    /// </summary>
    public sealed class SocialMutes
    {
        readonly HashSet<int> reactions = new HashSet<int>(), pings = new HashSet<int>();

        public bool Hides(int seat, bool ping) => (ping ? pings : reactions).Contains(seat);

        public void Set(int seat, bool ping, bool hidden)
        {
            var set = ping ? pings : reactions;
            if (hidden) set.Add(seat);
            else set.Remove(seat);
        }

        /// <summary>받은 소통을 내 화면에 보이나: 내 것은 늘, 남의 것은 그 좌석의 그 갈래를 숨기지 않았을 때.</summary>
        public bool Shows(SocialMsg m, int me) => m != null && (m.From == me || !Hides(m.From, m.Kind == SocialKind.Ping));

        public void Clear()
        {
            reactions.Clear();
            pings.Clear();
        }
    }

    public static class SocialRules
    {
        /// <summary>
        /// 방장: 받은 소통을 넘길 메시지로 새로 만든다(틀리면 null). 받은 객체의 칸을 그대로 넘기지 않는다 — 종류에 맞는 칸만 옮긴다(숨은 정보가 실려 와도 새지 않는다).
        /// </summary>
        public static SocialMsg Relay(SendSocial m, int from, int seats, int cells, int nextSeq)
        {
            if (m == null || from < 0 || from >= seats || m.Code < 0 || m.Code >= SocialCodes.Of(m.Kind)) return null;
            var o = new SocialMsg { From = from, Kind = m.Kind, Code = m.Code };
            switch (m.Kind)
            {
                case SocialKind.Ping:
                    o.Target = PingSpots.Norm(m.Target, from, seats, cells, nextSeq);
                    return o.Target != null ? o : null;
                case SocialKind.Emote:
                    return o;
                case SocialKind.Mark:
                    if (m.Seq < 0 || m.Seq >= nextSeq || m.Owner < 0 || m.Owner >= seats || m.Owner == from) return null;
                    o.Seq = m.Seq;
                    o.Owner = m.Owner;
                    return o;
                default:
                    return null;
            }
        }
    }
}
