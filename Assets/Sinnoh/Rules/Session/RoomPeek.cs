using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>
    /// 남의 고르기 보기(M9 다듬기 P09 — 결정 112 Q16, 04 §8-1 P15, 02 §4-10): 공개 선택에서 고르는 좌석이 지금 가리키는 · 누른 것을 다른 좌석의 화면에 그 좌석 색으로 보인다.
    /// 게임 명령이 아니다 — 엔진 · 세이브 · 리플레이 · 시계와 무관한 방 층 메시지이고(소통 <see cref="SocialKind"/>과 같은 꼴), 방장은 기억하지 않는다(재접속 뒤 다시 보내지 않는다).
    /// 보이는 자리는 허용 목록 셋뿐이다: 조우 카드(<see cref="ChoiceKind.EncounterPick"/>) · 사당 카드 고르기(Reward / <see cref="ChoiceSub.ShrinePick"/>) · 은행상회 줄(내 턴의 맵 행동 — 덱 맨 위 거래 카드는 공개, rulings A4).
    /// 비공개 선택(운명 손패 · 버리기 · 트레이너 카드 · 스타팅 · 비밀 묶음 · 거래 내용)은 자리가 없어 보낼 수 없고, 방장도 넘기지 않는다(<see cref="PeekRules.Relay"/>).
    /// </summary>
    /// <remarks>Village · Move(Protocol 10 — 결정 120, 플레이 시험 T2 · T12): 고향 마을 고르기(<see cref="ChoiceKind.VillagePick"/> — 남은 마을은 모두에게 보인다)에서 가리키는 마을 · 배틀 기술 고르기(<see cref="ChoiceKind.BattleAction"/>)에서 가리키는 기술. 둘의 Index는 공개 표의 색인(마을 고정물 · 기술)이다.</remarks>
    public enum PeekSpot { None = 0, Encounter = 1, Shrine = 2, Bank = 3, Village = 4, Move = 5 }

    /// <summary>
    /// 손님 → 호스트: 내가 지금 가리키는 것. Index = 그 선택의 공개 후보(<see cref="PendingChoice.Options"/>) 안 자리(조우 · 사당) · 은행상회는 줄(0 위 · 1 아래 · 2 두 줄 모두), −1 = 아무것도 가리키지 않는다.
    /// Press = 눌렀다(고름). 카드 번호 · 이름 같은 내용은 싣지 않는다 — 받는 쪽이 자기 보기의 같은 선택에서 읽는다.
    /// </summary>
    public sealed class SendPeek : RoomMsg
    {
        public PeekSpot Spot;
        public int Index = -1;
        public bool Press;
    }

    /// <summary>호스트 → 보낸 좌석을 뺀 전원: 그 좌석이 가리키는 것. 상태가 아니다 — 늦게 들어온 손님은 받지 않는다. 방장이 허용된 칸만으로 새로 만든 것이다.</summary>
    public sealed class PeekMsg : ServerMsg
    {
        public int From;
        public PeekSpot Spot;
        public int Index = -1;
        public bool Press;
    }

    /// <summary>
    /// 보내는 빈도(04 P15 「초당 3번 미만」 · Relay 좌석당 0.3 KiB/s 안 — 한 통 90바이트 밑): 보내는 화면은 <see cref="GapMs"/>마다 한 번(마지막 모습만 — 사이의 것은 버린다),
    /// 방장은 좌석마다 <see cref="WindowMs"/> 안 <see cref="HostMax"/>통까지 받고 넘으면 조용히 버린다(전선 위에서 간격이 줄어도 화면이 보낸 것을 버리지 않게 한 통 여유). 시각은 부르는 쪽이 준다.
    /// </summary>
    public sealed class PeekRate
    {
        public const int GapMs = 350, WindowMs = 1000, HostMax = 4;
        /// <summary>은행상회 표시가 다음 소식 없이 남는 시간(ms) — 은행상회는 열린 선택으로 닫힘을 알 수 없다(맵 행동이 그대로 열려 있다).</summary>
        public const long BankShowMs = 4000;

        readonly Dictionary<int, List<long>> times = new Dictionary<int, List<long>>();

        /// <summary>방장: 지금 받아도 되면 적고 true.</summary>
        public bool TryTake(int seat, long nowMs)
        {
            if (!times.TryGetValue(seat, out var list)) times[seat] = list = new List<long>();
            list.RemoveAll(t => nowMs - t >= WindowMs || t > nowMs);
            if (list.Count >= HostMax) return false;
            list.Add(nowMs);
            return true;
        }

        public void Clear() => times.Clear();
    }

    public static class PeekRules
    {
        /// <summary>은행상회 줄 수(위 · 아래 · 두 줄 모두 — <see cref="BankRules"/>의 Top · Bottom · Both).</summary>
        public const int BankRows = 3;

        /// <summary>
        /// 그 좌석이 그 자리에서 고르고 있는 열린 공개 선택(보기 · 호스트 원본 어느 쪽에서나 같다). 없거나 남에게 가리는 선택이면 null.
        /// </summary>
        public static PendingChoice Open(GameState s, int seat, PeekSpot spot)
        {
            if (s == null || seat < 0) return null;
            foreach (var c in s.Pending)
            {
                if (c.Seat != seat || c.Answered || ChoiceRules.HidesFromOthers(c)) continue;
                switch (spot)
                {
                    case PeekSpot.Encounter:
                        if (c.Kind == ChoiceKind.EncounterPick) return c;
                        break;
                    case PeekSpot.Shrine:
                        if (c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.ShrinePick) return c;
                        break;
                    case PeekSpot.Bank:
                        if (c.Kind == ChoiceKind.MapAction) return c;
                        break;
                    case PeekSpot.Village:
                        if (c.Kind == ChoiceKind.VillagePick) return c;
                        break;
                    case PeekSpot.Move:
                        if (c.Kind == ChoiceKind.BattleAction) return c;
                        break;
                }
            }
            return null;
        }

        /// <summary>그 자리에서 가리킬 수 있는 칸 수(Index는 [0, n) 또는 −1).</summary>
        public static int Slots(PendingChoice c, PeekSpot spot) => c == null ? 0 : spot == PeekSpot.Bank ? BankRows : spot == PeekSpot.Village || spot == PeekSpot.Move ? KeySlots : c.Options.Count;

        /// <summary>마을 · 기술 자리의 Index 상한(공개 표의 색인 — 마을 고정물 · 기술 카드). 받는 화면이 자기 보기의 후보에서 찾고, 없으면 아무것도 칠하지 않는다.</summary>
        public const int KeySlots = 4096;

        /// <summary>
        /// 방장: 받은 것을 넘길 메시지로 새로 만든다(틀리면 null) — 보낸 좌석이 그 자리의 공개 선택을 지금 열고 있고 자리가 범위 안일 때만.
        /// 받은 객체를 그대로 넘기지 않는다(허용된 칸 넷만 옮긴다). 비공개 선택은 <see cref="Open"/>이 찾지 않으므로 넘어가지 않는다.
        /// </summary>
        public static PeekMsg Relay(SendPeek m, int from, GameState host)
        {
            if (m == null) return null;
            var c = Open(host, from, m.Spot);
            if (c == null || m.Index < -1 || m.Index >= Slots(c, m.Spot)) return null;
            return new PeekMsg { From = from, Spot = m.Spot, Index = m.Index, Press = m.Press && m.Index >= 0 };
        }
    }
}
