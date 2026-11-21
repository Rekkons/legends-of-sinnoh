using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>
    /// 시계가 보는 열린 선택 하나(방이 상태에서 만든다). Seat = 답할 사람 좌석(가상 응답자는 사람 조종자 좌석), Own = 자기 일이라 뱅크를 쓸 수 있다
    /// (자기 턴의 MapTurn · 자기 포켓몬의 BattleAction — 조종자는 아니다, 02 §5-5), Offline = 그 좌석이 끊김, Away = 자리 비움,
    /// NotBefore = 이 시각 전에는 시간 초과로 넘기지 않는다(숨은 손패 창의 최소 시간, 02 §5-7).
    /// </summary>
    public struct ClockChoice
    {
        public int Id;
        public int Seat;
        public ChoiceKind Kind;
        public ClockKind Clock;
        public int TurnId;
        public bool Own;
        public bool Offline;
        public bool Away;
        public long NotBefore;
    }

    /// <summary>좌석 하나의 시계 표시(02 §5-7 ClockState.Seats). Running = 지금 흐르는 시계(None = 멈춤), LeftMs = 그 기본 시간 남은 것.</summary>
    public sealed class SeatClock
    {
        public ClockKind Running;
        public int ChoiceId = -1;
        public long LeftMs;
        public long BankMs;
        public bool Away;
        public bool Connected = true;
    }

    /// <summary>
    /// 제한 시간(02 §5, 결정 25·30 ①). 시각은 밖에서 받는다(세션 시각 ms) — 스레드·시계를 모른다.
    /// 쓰는 법: 상태가 바뀔 때마다 <see cref="Sync"/>(열린 선택 목록) → 시간이 흐르면 <see cref="AdvanceTo"/> → <see cref="NextExpiry"/>가 지금 이하면 그 선택을 넘긴다.
    /// 단위(02 §5-3): MapTurn = (좌석, 턴) 한 묶음을 그 턴의 MapTurn 선택이 나눠 씀 · Setup = 마을은 한 번마다, 스타터 + 카드는 좌석마다 묶음 한 번 ·
    /// 그 밖은 결정 하나마다(Lineup은 <see cref="ClockSettings.LineupMs"/>). 다시 열린 선택(ChoiceReissued)은 <see cref="Rename"/>으로 남은 시간을 잇는다.
    /// 흐르는 시계(02 §5-3): 한 좌석에 하나만 — Reaction &gt; Respond·Setup &gt; BattleAction &gt; MapTurn. 누군가의 Reaction·Respond가 흐르면 남의 MapTurn·BattleAction이 멈추고,
    /// BattleAction이 흐르면 MapTurn이 멈춘다. 방장 멈춤(연출) 끝 전에는 아무 시계도 줄지 않는다(<see cref="Sync"/>의 runFrom).
    /// 뱅크(02 §5-5): 좌석마다, 시작값 + 라운드가 2 이상으로 오를 때마다 충전(상한), Own이고 끊기지 않은 좌석만 기본 시간을 다 쓴 뒤 저절로 쓴다.
    /// 끊긴 좌석(§6-1): 뱅크 없이 그 시계의 남은 기본 시간만 기다린다 — 시계 끔 방이면 선택마다 <see cref="ClockSettings.OfflineMs"/>. 자리 비움 좌석은 0초.
    /// </summary>
    public sealed class TurnClock
    {
        sealed class Unit
        {
            public long LeftMs;
        }

        sealed class Run
        {
            public int Id, Seat;
            public ClockKind Clock;
            public Unit Unit;
            public bool Bank;
            public long NotBefore;
        }

        readonly Dictionary<string, Unit> units = new Dictionary<string, Unit>();
        readonly List<Run> running = new List<Run>();
        readonly long[] bank;
        readonly HashSet<int> open = new HashSet<int>();
        ClockSettings settings;
        long last;
        long runFrom;
        int round;
        int turn;

        public TurnClock(ClockSettings settings, int seats, long nowMs)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (seats < 1 || seats > SeatId.MaxSeats) throw new ArgumentOutOfRangeException(nameof(seats));
            bank = new long[seats];
            for (int i = 0; i < seats; i++) bank[i] = settings.On ? settings.BankStartMs : 0;
            last = runFrom = nowMs;
        }

        /// <summary>지금 길이 표. 바꾸면 새로 생기는 단위부터 새 길이다(02 §3-2 「다음 선택부터」). 뱅크는 그대로 둔다.</summary>
        public ClockSettings Settings
        {
            get => settings;
            set => settings = value ?? throw new ArgumentNullException(nameof(value));
        }

        public long BankMs(int seat) => bank[seat];

        /// <summary>뱅크를 마지막으로 충전한 라운드(방 저장에 적는다).</summary>
        public int Round => round;

        /// <summary>이어하기(02 §5-7 「방이 닫힌 동안」): 저장된 뱅크와 충전한 라운드로 되살린다 — 같은 라운드를 다시 충전하지 않는다. 열린 선택은 기본 시간을 새로 받는다(단위는 비어 있다).</summary>
        public void Restore(IReadOnlyList<long> banks, int bankRound)
        {
            if (banks == null) throw new ArgumentNullException(nameof(banks));
            for (int i = 0; i < bank.Length && i < banks.Count; i++) bank[i] = Math.Max(0, banks[i]);
            round = Math.Max(round, bankRound);
        }

        /// <summary>라운드가 올랐으면(2 이상) 좌석마다 충전값을 더하고 상한에서 자른다(02 §5-5). 셋업(0) → 1라운드는 시작값 그대로 [가정 M7-3].</summary>
        public void OnRound(int r)
        {
            if (r <= round) return;
            for (int k = Math.Max(round, 1) + 1; k <= r; k++)
                if (settings.On)
                    for (int i = 0; i < bank.Length; i++) bank[i] = Math.Min(settings.BankMaxMs, bank[i] + settings.BankRoundMs);
            round = r;
        }

        /// <summary>last부터 nowMs까지 흐른 시간을 지금 흐르는 시계에서 뺀다(기본 시간 → 뱅크). runFrom 전은 세지 않는다. 거꾸로 가는 값은 무시한다.</summary>
        public void AdvanceTo(long nowMs)
        {
            if (nowMs <= last) return;
            long from = Math.Max(last, runFrom);
            last = nowMs;
            if (nowMs <= from) return;
            long elapsed = nowMs - from;
            foreach (var r in running)
            {
                long take = Math.Min(r.Unit.LeftMs, elapsed);
                r.Unit.LeftMs -= take;
                long rest = elapsed - take;
                if (rest > 0 && r.Bank) bank[r.Seat] = Math.Max(0, bank[r.Seat] - rest);
            }
        }

        /// <summary>다음에 시간이 다 되는 선택과 그 시각. 없으면 long.MaxValue(choiceId −1).</summary>
        public long NextExpiry(out int choiceId)
        {
            choiceId = -1;
            long best = long.MaxValue;
            long from = Math.Max(last, runFrom);
            foreach (var r in running)
            {
                long t = from + r.Unit.LeftMs + (r.Bank ? bank[r.Seat] : 0);
                if (t < r.NotBefore) t = r.NotBefore;
                if (t < best)
                {
                    best = t;
                    choiceId = r.Id;
                }
            }
            return best;
        }

        /// <summary>다시 열린 선택이 옛 Id의 남은 시간을 잇는다(02 §5-3 「시계는 선택 Id가 아니라 (좌석, 종류, 단위)에 붙는다」).</summary>
        public void Rename(int oldId, int newId)
        {
            foreach (var p in new[] { "C", "A", "O" })
                if (units.TryGetValue(p + oldId, out var u))
                {
                    units.Remove(p + oldId);
                    units[p + newId] = u;
                }
        }

        /// <summary>
        /// 열린 선택 목록으로 단위·흐르는 시계를 다시 정한다. 먼저 <see cref="AdvanceTo"/>(지금)를 불러 둔다. runFrom = 방장 멈춤 끝 — 그 전에는 줄지 않는다.
        /// turnId = 지금 턴(지난 턴의 MapTurn 묶음을 버린다).
        /// </summary>
        public void Sync(long runFromMs, int turnId, IReadOnlyList<ClockChoice> choices)
        {
            if (choices == null) throw new ArgumentNullException(nameof(choices));
            runFrom = runFromMs;
            turn = Math.Max(turn, turnId);
            open.Clear();
            foreach (var c in choices) open.Add(c.Id);
            Prune();

            // 좌석마다 가장 앞선 시계 하나(Reaction > Respond·Setup > BattleAction > MapTurn)
            var best = new Run[bank.Length];
            var rank = new int[bank.Length];
            foreach (var c in choices)
            {
                if (c.Seat < 0 || c.Seat >= bank.Length || c.Clock == ClockKind.None) continue;
                var r = Make(c);
                if (r == null) continue;
                int k = Rank(c.Clock);
                if (best[c.Seat] == null || k > rank[c.Seat])
                {
                    best[c.Seat] = r;
                    rank[c.Seat] = k;
                }
            }
            int top = 0;
            for (int i = 0; i < best.Length; i++)
                if (best[i] != null && rank[i] > top) top = rank[i];
            running.Clear();
            for (int i = 0; i < best.Length; i++)
            {
                if (best[i] == null) continue;
                // 남의 응답이 흐르면 MapTurn·BattleAction이 멈춘다 · 배틀 결정이 흐르면 MapTurn이 멈춘다(02 §5-3 오른쪽 열)
                if (top >= Rank(ClockKind.Respond) && rank[i] <= Rank(ClockKind.BattleAction)) continue;
                if (top == Rank(ClockKind.BattleAction) && rank[i] == Rank(ClockKind.MapTurn)) continue;
                running.Add(best[i]);
            }
        }

        static int Rank(ClockKind k)
        {
            switch (k)
            {
                case ClockKind.Reaction: return 4;
                case ClockKind.Respond:
                case ClockKind.Setup: return 3;
                case ClockKind.BattleAction: return 2;
                case ClockKind.MapTurn: return 1;
                default: return 0;
            }
        }

        /// <summary>그 선택의 단위(없으면 만든다)와 흐를 때의 모양. 시계가 걸리지 않는 선택(끔 방의 접속한 좌석)이면 null.</summary>
        Run Make(ClockChoice c)
        {
            if (c.Away) return new Run { Id = c.Id, Seat = c.Seat, Clock = c.Clock, Unit = Get("A" + c.Id, 0), NotBefore = c.NotBefore };
            if (!settings.On)
            {
                if (!c.Offline) return null;
                return new Run { Id = c.Id, Seat = c.Seat, Clock = c.Clock, Unit = Get("O" + c.Id, settings.OfflineMs), NotBefore = c.NotBefore };
            }
            Unit u;
            bool bankable = false;
            switch (c.Clock)
            {
                case ClockKind.MapTurn:
                    u = Get($"M{c.Seat}:{c.TurnId}", settings.MapTurnMs);
                    bankable = true;
                    break;
                case ClockKind.Setup:
                    u = c.Kind == ChoiceKind.VillagePick ? Get("C" + c.Id, settings.VillageMs) : Get("S" + c.Seat, settings.SetupPickMs);
                    break;
                case ClockKind.BattleAction:
                    u = Get("C" + c.Id, c.Kind == ChoiceKind.Lineup ? settings.LineupMs : settings.BattleActionMs);
                    bankable = c.Kind != ChoiceKind.Lineup;
                    break;
                case ClockKind.Reaction:
                    u = Get("C" + c.Id, settings.ReactionMs);
                    break;
                default:
                    u = Get("C" + c.Id, settings.RespondMs);
                    break;
            }
            return new Run { Id = c.Id, Seat = c.Seat, Clock = c.Clock, Unit = u, Bank = bankable && c.Own && !c.Offline, NotBefore = c.NotBefore };
        }

        Unit Get(string key, long initMs)
        {
            if (!units.TryGetValue(key, out var u))
            {
                u = new Unit { LeftMs = initMs };
                units[key] = u;
            }
            return u;
        }

        /// <summary>닫힌 선택의 단위와 지난 턴의 MapTurn 묶음을 버린다. 스타터 + 카드 묶음(S)은 판 내내 둔다(한 번뿐).</summary>
        void Prune()
        {
            var drop = new List<string>();
            foreach (var key in units.Keys)
            {
                char p = key[0];
                if (p == 'C' || p == 'A' || p == 'O')
                {
                    if (!open.Contains(int.Parse(key.Substring(1)))) drop.Add(key);
                }
                else if (p == 'M' && int.Parse(key.Substring(key.IndexOf(':') + 1)) < turn) drop.Add(key);
            }
            foreach (var k in drop) units.Remove(k);
        }

        /// <summary>좌석마다 지금 흐르는 시계(표시용 — 02 §5-7 ClockState.Seats). Away·Connected는 방이 채운다.</summary>
        public List<SeatClock> Seats()
        {
            var r = new List<SeatClock>(bank.Length);
            for (int i = 0; i < bank.Length; i++) r.Add(new SeatClock { BankMs = bank[i] });
            foreach (var run in running)
            {
                var s = r[run.Seat];
                s.Running = run.Clock;
                s.ChoiceId = run.Id;
                s.LeftMs = run.Unit.LeftMs;
            }
            return r;
        }
    }
}
