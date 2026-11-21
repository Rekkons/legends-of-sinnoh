using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    // 무르기 · 되돌리기(결정 149 · 154, Docs/Design/undo.md). 이 파일은 조각 0(계약) — 형과 시그니처만 있고 판정 몸통은 조각 A가 채운다.

    /// <summary>무르기 설정(결정 149): 끔 · 내 턴 무르기(운이 드러나기 전까지, 남의 행동을 넘지 못함) · 자유(내 턴 시작까지 + 방장 되돌리기). 규칙이 아니라 RuleOptions 밖이다(상태 해시에 들지 않는다).</summary>
    public enum UndoMode { Off, Turn, Free }

    /// <summary>무르기를 못 하는 까닭(화면 툴팁 키 = <see cref="UndoRules.BlockKey"/>). None = 무를 수 있다.</summary>
    public enum UndoBlock { None, Off, Setup, NotYourTurn, TurnStart, Luck, Others, Floor, Paused }

    /// <summary>
    /// 좌석에 보내는 「지금 무를 수 있나」(<see cref="GameUpdate.Undo"/> — 좌석마다). StepTo = 한 수 무르면 갈 CommandCount(−1 = 못 함, 까닭은 Block).
    /// HostList = 「되돌리기」 목록을 열 수 있나(자유 + 솔로 플레이어 · 방장). Epoch = 세션이 되돌린 횟수(손님이 명령에 실어 낡은 명령을 거른다).
    /// </summary>
    public sealed class UndoOffer
    {
        public UndoMode Mode;
        public int StepTo = -1;
        public UndoBlock Block;
        public bool HostList;
        public int Epoch;
    }

    /// <summary>되돌림 종류: Step = 개인 무르기(한 쉬는 때만큼) · Host = 방장(솔로 플레이어)의 지난 턴으로 되돌리기.</summary>
    public enum RewindKind { Step, Host }

    /// <summary>
    /// 되돌림 알림(<see cref="GameUpdate.Rewind"/> — 되돌린 직후 한 번, 모든 좌석). By = 되돌린 좌석, To = 되돌린 CommandCount, Round · TurnSeat = 되돌린 자리의 라운드 · 차례 좌석,
    /// NextSeq = 되돌린 상태의 다음 이벤트 Seq(화면 기록이 이것 이상인 줄 = 지운 미래를 뺀다), Epoch = 되돌린 뒤의 <see cref="UndoOffer.Epoch"/>.
    /// </summary>
    public sealed class RewindNote
    {
        public RewindKind Kind;
        public int By;
        public int To;
        public int Round;
        public int TurnSeat;
        public int NextSeq;
        public int Epoch;
    }

    /// <summary>방장 되돌리기 목록 한 줄(턴 시작 자리). To = 되돌릴 CommandCount, Current = 지금 턴의 시작. Deeds = 그 턴에 한 일(공개 이벤트에서 — 관전자 거름).</summary>
    public sealed class UndoPoint
    {
        public int To;
        public int Round;
        public int TurnId;
        public int Seat;
        public bool Current;
        public List<UndoDeed> Deeds = new List<UndoDeed>();
    }

    /// <summary>되돌리기 목록 줄의 「한 일」 종류(화면 키 screen.menu.rewind.deed.&lt;소문자&gt;).</summary>
    public enum UndoDeedKind { Moved, Battle, Caught, Fled, Evolved, Crafted, Stamp, Offer, Trade, Fly }

    /// <summary>한 일 하나. Def = 포켓몬 표 색인(배틀 · 포획 · 진화의 상대 · 결과, 없으면 −1), Arg = 종류마다의 덧값(없으면 −1).</summary>
    public sealed class UndoDeed
    {
        public UndoDeedKind Kind;
        public int Def = -1;
        public int Arg = -1;
    }

    /// <summary>
    /// 무르기 책의 한 줄(세션 메모리 — 저장하지 않는다): 받아들인 명령 하나와 그 뒤 상태의 작은 칸. 책은 바닥 CommandCount부터 빈틈없이 이어진다.
    /// </summary>
    public sealed class UndoLine
    {
        /// <summary>다시 적용할 명령(Seq · Seat · Command).</summary>
        public SaveLogLine Line;
        /// <summary>이 명령이 숨은 정보를 드러냈다(<see cref="UndoRules.Revealed"/>).</summary>
        public bool Luck;
        /// <summary>이 줄 뒤가 쉬는 때(<see cref="SaveRest.IsRest"/>).</summary>
        public bool RestAfter;
        /// <summary>이 줄의 이벤트에 TurnStarted가 있다.</summary>
        public bool TurnStarted;
        /// <summary>이 줄 끝에서 턴이 끝난 채다(마지막 TurnEnded 뒤에 TurnStarted가 없다 — 라운드 끝 UB 건너뛰기 투표 · 게임 끝).</summary>
        public bool TurnEnded;
        /// <summary>사람 조종자가 가상 좌석 값으로 낸 답(AI 답이 아니다) — 「내 턴 무르기」는 남이 한 일로 친다(결정 154 ③).</summary>
        public bool HumanVirtual;
        /// <summary>AI 답(ByAi) · 시간 초과(Timeout) 줄이고 BeforeHash가 그 앞 상태 해시다 — 되돌린 뒤 같은 자리 · 같은 상태에 오면 묻지 않고 이 줄을 다시 낸다.</summary>
        public bool Replayable;
        public ulong BeforeHash;
        /// <summary>줄 뒤 상태.</summary>
        public int TurnId, Round, ActiveSeat;
        public Phase Phase;
        /// <summary>줄 뒤 공개 기록(관전자 거름) 길이.</summary>
        public int PublicLog;
        /// <summary>공개 이벤트에서 뽑은 한 일(없으면 null).</summary>
        public List<UndoDeed> Deeds;
        /// <summary>Deeds 가운데 이 줄의 (마지막) TurnStarted 앞에서 난 것의 수 — 앞 턴의 몫(턴을 끝내는 명령의 배틀 · 포획 같은 것). TurnStarted가 없으면 0.</summary>
        public int DeedsBeforeTurn;
    }

    /// <summary>명령 하나를 적용하기 전에 떠 둔 숨은 몫(상태 전체가 아니라 비교에 필요한 값의 사본): Rng · 덱마다 카드(정렬) · 뒷면 숨겨진 장소 · 좌석마다 운명 손패 · 트레이너 카드 후보(정렬).</summary>
    public sealed class LuckProbe
    {
        public ulong RngState, RngIncrement;
        public int[][] Decks;
        /// <summary>뒷면 숨겨진 장소 토큰: Tokens 색인 · Def · Cell(토큰은 Tokens에 덧붙기만 한다 — 색인이 바뀌지 않는다).</summary>
        public int[] SpotIndex, SpotDef, SpotCell;
        public int[][] Fate, Offer;
        /// <summary>적용 전 다음 선택 Id — 이보다 크거나 같은 Id가 새로 열린 선택이다.</summary>
        public int NextChoiceId;
    }

    /// <summary>무르기 판정(순수 static — 세션 · 화면 · 시험이 같이 쓴다).</summary>
    public static class UndoRules
    {
        /// <summary>명령 하나를 적용하기 전 숨은 몫(Rng · 덱 · 뒷면 숨겨진 장소 · 좌석 손패)을 뜬다.</summary>
        public static LuckProbe Probe(GameState before)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            var p = new LuckProbe { RngState = before.Rng.State, RngIncrement = before.Rng.Increment, Decks = new int[before.Decks.Length][], NextChoiceId = before.NextChoiceId };
            for (int d = 0; d < before.Decks.Length; d++) p.Decks[d] = Sorted(before.Decks[d]);
            int n = 0;
            foreach (var t in before.Tokens)
                if (FaceDown(t)) n++;
            p.SpotIndex = new int[n];
            p.SpotDef = new int[n];
            p.SpotCell = new int[n];
            n = 0;
            for (int i = 0; i < before.Tokens.Count; i++)
            {
                var t = before.Tokens[i];
                if (!FaceDown(t)) continue;
                p.SpotIndex[n] = i;
                p.SpotDef[n] = t.Def;
                p.SpotCell[n] = t.Cell;
                n++;
            }
            p.Fate = new int[before.Seats.Length][];
            p.Offer = new int[before.Seats.Length][];
            for (int i = 0; i < before.Seats.Length; i++)
            {
                p.Fate[i] = Sorted(before.Seats[i].FateHand);
                p.Offer[i] = Sorted(before.Seats[i].TrainerCardOffer);
            }
            return p;
        }

        /// <summary>
        /// 그 명령이 운을 드러냈나(undo.md §1-2): Rng가 바뀜 · 덱에서 카드가 빠짐 · 뒷면 숨겨진 장소가 바뀜 · 남(seat가 아닌 좌석)의 숨은 카드가 빠짐 · 비밀 공개 이벤트.
        /// 새 숨은 몫을 규칙에 더하면 <see cref="Engine.ViewFor"/>와 이것을 함께 고친다.
        /// </summary>
        public static bool Revealed(LuckProbe before, GameState after, int seat, IReadOnlyList<GameEvent> events)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));
            // ① 주사위 · 동전 · 섞기 · 무작위 고르기 — 규칙의 모든 우연은 IRng 하나를 지난다
            if (after.Rng.State != before.RngState || after.Rng.Increment != before.RngIncrement) return true;
            // ② 덱에서 카드가 빠졌다(넣기만 한 것은 새 정보가 아니다)
            for (int d = 0; d < before.Decks.Length; d++)
                if (!Within(before.Decks[d], after.Decks[d])) return true;
            // ③ 뒷면 숨겨진 장소가 앞면이 됨 · 없어짐 · 정체가 바뀜
            for (int k = 0; k < before.SpotIndex.Length; k++)
            {
                var t = after.Tokens[before.SpotIndex[k]];
                if (!FaceDown(t) || t.Def != before.SpotDef[k] || t.Cell != before.SpotCell[k]) return true;
            }
            // ④ 남의 운명 손패 · 트레이너 카드 후보에서 카드가 빠짐
            for (int i = 0; i < before.Fate.Length; i++)
            {
                if (i == seat) continue;
                if (!Within(before.Fate[i], after.Seats[i].FateHand) || !Within(before.Offer[i], after.Seats[i].TrainerCardOffer)) return true;
            }
            // ⑤ 남의 비밀 답 · 트레이너 카드 · 추종자 공개
            if (events != null)
                foreach (var e in events)
                    if (e is SecretRevealed || e is TrainerCardsRevealed || e is FollowerTally) return true;
            // ⑥ 후보를 고르는 좌석만 보는 선택이 새로 열렸다(ChoiceRules.HidesFromOthers) — 후보가 숨은 몫에서 올 수 있다:
            //    dragon_tamer B의 약한 덱 드래곤 · 동등한 교환의 조우 덱 카드(덱에서 빼지 않고 내용을 보인다) · 거래 응답의 남의 운명 카드(반증 시험이 찾았다)
            if (NewPrivateChoice(after.Pending, before.NextChoiceId) || NewPrivateChoice(after.Held, before.NextChoiceId)) return true;
            return false;
        }

        static bool NewPrivateChoice(List<PendingChoice> list, int nextId)
        {
            foreach (var c in list)
                if (c.Id >= nextId && !c.Answered && ChoiceRules.HidesFromOthers(c)) return true;
            return false;
        }

        static bool FaceDown(TokenState t) => t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed;

        static int[] Sorted(List<int> list)
        {
            var a = list.ToArray();
            Array.Sort(a);
            return a;
        }

        /// <summary>정렬된 before의 다중집합이 after 안에 다 있나.</summary>
        static bool Within(int[] before, List<int> after)
        {
            if (before.Length == 0) return true;
            if (after.Count < before.Length) return false;
            var a = Sorted(after);
            int j = 0;
            foreach (int x in before)
            {
                while (j < a.Length && a[j] < x) j++;
                if (j == a.Length || a[j] != x) return false;
                j++;
            }
            return true;
        }

        /// <summary>
        /// 받아들인 명령 하나의 책 줄(undo.md §1-3): before = 적용 전에 뜬 숨은 몫, after = 적용 뒤 호스트 원본, hostEvents = 그 명령의 이벤트(걸러지기 전).
        /// PublicLog = publicBefore + 그 이벤트 가운데 관전자에게 가는 것의 수. 한 일은 관전자에게 거른 이벤트에서 뽑는다.
        /// </summary>
        public static UndoLine LineOf(SaveLogLine line, LuckProbe before, GameState after, IReadOnlyList<GameEvent> hostEvents, int publicBefore)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));
            var u = new UndoLine
            {
                Line = line, Luck = Revealed(before, after, line.Seat, hostEvents), RestAfter = SaveRest.IsRest(after),
                TurnId = after.TurnId, Round = after.Round, ActiveSeat = after.ActiveSeat, Phase = after.Phase, PublicLog = publicBefore,
                HumanVirtual = line.Seat <= SeatId.Galactic && line.Command != null && line.Command.Kind == CommandKind.Answer && !line.Command.ByAi,
                Replayable = IsReplayable(line),
            };
            if (hostEvents == null) return u;
            UndoDeed battle = null;
            foreach (var host in hostEvents)
            {
                if (host is TurnEnded) u.TurnEnded = true;
                var e = Engine.FilterFor(host, SeatId.Observer);
                if (e == null) continue;
                u.PublicLog++;
                UndoDeed d = null;
                switch (e)
                {
                    case TurnStarted _:
                        u.TurnStarted = true;
                        u.TurnEnded = false;
                        u.DeedsBeforeTurn = u.Deeds?.Count ?? 0;
                        break;
                    case Moved _:
                        d = new UndoDeed { Kind = UndoDeedKind.Moved };
                        break;
                    case BattleStarted _:
                        d = battle = new UndoDeed { Kind = UndoDeedKind.Battle };
                        break;
                    case SentIn si when battle != null && battle.Def < 0 && si.Side == 1:
                        battle.Def = si.Species; // 배틀 상대(첫 출전)
                        break;
                    case CaptureAttempt ca when ca.Success:
                        d = new UndoDeed { Kind = UndoDeedKind.Caught, Def = ca.TargetId >= 0 && ca.TargetId < after.Pokemon.Count ? after.Pokemon[ca.TargetId].Def : -1 };
                        break;
                    case Fled _:
                        d = new UndoDeed { Kind = UndoDeedKind.Fled };
                        break;
                    case Evolved ev:
                        d = new UndoDeed { Kind = UndoDeedKind.Evolved, Def = ev.To };
                        break;
                    case Crafted cr:
                        d = new UndoDeed { Kind = UndoDeedKind.Crafted, Arg = cr.ItemKey };
                        break;
                    case StampGained sg:
                        d = new UndoDeed { Kind = UndoDeedKind.Stamp, Arg = sg.StampKey };
                        break;
                    case ShrineOffered so:
                        d = new UndoDeed { Kind = UndoDeedKind.Offer, Arg = so.ShrineKey };
                        break;
                    case TradeDone _:
                        d = new UndoDeed { Kind = UndoDeedKind.Trade };
                        break;
                    case Flew _:
                        d = new UndoDeed { Kind = UndoDeedKind.Fly };
                        break;
                }
                if (d != null) (u.Deeds ??= new List<UndoDeed>()).Add(d);
            }
            return u;
        }

        /// <summary>
        /// 되돌린 뒤 다시 낼 줄인가: AI 답(ByAi) · 시간 초과(Timeout — 늦은 AI 답을 기본값으로 넘긴 것 포함). 강한 AI는 시간 마감으로 끝나면 같은 입력에도 답이 달라질 수 있어
        /// 같은 자리 · 같은 상태(BeforeHash)로 돌아오면 묻지 않고 그 줄을 낸다(undo.md §1-6). 세션은 AI가 답할 선택에서만 찾으므로 사람 좌석의 줄은 쓰이지 않는다.
        /// </summary>
        public static bool IsReplayable(SaveLogLine line) =>
            line?.Command != null && ((line.Command.Kind == CommandKind.Answer && line.Command.ByAi) || line.Command.Kind == CommandKind.Timeout);

        /// <summary>그 자리(CommandCount)가 쉬는 때인가 — 바닥이면 baseRest, 아니면 그 앞 줄의 RestAfter.</summary>
        static bool RestAt(IReadOnlyList<UndoLine> book, int baseCount, bool baseRest, int pos) => pos == baseCount ? baseRest : book[pos - baseCount - 1].RestAfter;

        /// <summary>
        /// 지금 턴의 시작 자리(TurnStarted가 낀 마지막 줄부터 처음 쉬는 때가 된 줄 뒤). 책에 TurnStarted가 없으면 −1(턴이 바닥 앞에서 시작했다),
        /// 시작했지만 아직 쉬는 때에 닿지 않았으면 int.MaxValue.
        /// </summary>
        static int TurnStartPos(IReadOnlyList<UndoLine> book, int baseCount)
        {
            int t = book.Count - 1;
            while (t >= 0 && !book[t].TurnStarted) t--;
            if (t < 0) return -1;
            for (int j = t; j < book.Count; j++)
                if (book[j].RestAfter) return baseCount + j + 1;
            return int.MaxValue;
        }

        /// <summary>개인 「한 수 무르기」의 목표 CommandCount(못 하면 −1, why = 까닭). now = 호스트 원본, book · baseCount · baseRest = 세션의 책과 바닥.</summary>
        public static int StepTarget(IReadOnlyList<UndoLine> book, int baseCount, bool baseRest, GameState now, int seat, UndoMode mode, out UndoBlock why)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));
            if (now == null) throw new ArgumentNullException(nameof(now));
            why = UndoBlock.Off;
            if (mode == UndoMode.Off) return -1;
            why = UndoBlock.Setup;
            if (now.Phase == Phase.Setup) return -1;
            why = UndoBlock.NotYourTurn;
            if (seat < 0 || now.ActiveSeat != seat) return -1;
            if (book.Count != now.CommandCount - baseCount) throw new InvalidOperationException($"무르기 책 {book.Count}줄 ≠ CommandCount {now.CommandCount} − 바닥 {baseCount}");
            // 턴을 끝냈는데 다음 턴이 아직 시작하지 않았다(라운드 끝 UB 건너뛰기 투표 · 게임 끝 — ActiveSeat가 끝낸 좌석에 남는다): 끝낸 턴은 무르지 못한다
            for (int i = book.Count - 1; i >= 0; i--)
            {
                if (book[i].TurnEnded) return -1;
                if (book[i].TurnStarted) break;
            }
            // 지금보다 앞의 가장 늦은 쉬는 때(지금이 배틀 · 조우 한가운데면 그 앞 보드)
            int to = now.CommandCount - 1;
            while (to >= baseCount && !RestAt(book, baseCount, baseRest, to)) to--;
            int start = TurnStartPos(book, baseCount);
            if (start >= 0 && to < start)
            {
                why = UndoBlock.TurnStart;
                return -1;
            }
            if (to < baseCount)
            {
                why = UndoBlock.Floor;
                return -1;
            }
            if (mode == UndoMode.Turn)
                for (int i = to - baseCount; i < book.Count; i++)
                {
                    var u = book[i];
                    if (u.Luck)
                    {
                        why = UndoBlock.Luck;
                        return -1;
                    }
                    if (u.HumanVirtual || (u.Line.Seat >= 0 && u.Line.Seat != seat)) // 시스템(Timeout · HostOp)과 AI가 낸 가상 좌석 답은 넘는다 — 사람 조종자의 답은 남의 것
                    {
                        why = UndoBlock.Others;
                        return -1;
                    }
                }
            why = UndoBlock.None;
            return to;
        }

        /// <summary>방장 되돌리기 목록(자유일 때만, 새것부터): 책 안의 턴 시작 자리마다 한 줄 + 지금 턴(Current). 지금 상태 그대로인 자리 · 아직 닿지 않은 자리는 넣지 않는다.</summary>
        public static List<UndoPoint> Points(IReadOnlyList<UndoLine> book, int baseCount, GameState now)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));
            if (now == null) throw new ArgumentNullException(nameof(now));
            var starts = new List<int>();
            for (int i = 0; i < book.Count; i++)
                if (book[i].TurnStarted) starts.Add(i);
            var list = new List<UndoPoint>();
            for (int k = starts.Count - 1; k >= 0; k--)
            {
                int t = starts[k], end = k + 1 < starts.Count ? starts[k + 1] : book.Count;
                int j = t;
                while (j < end && !book[j].RestAfter) j++;
                if (j == end) continue; // 다음 턴이 오기 전에 쉬는 때가 없었다(또는 아직 닿지 않았다)
                int to = baseCount + j + 1;
                if (to >= now.CommandCount) continue;
                var at = book[j];
                var p = new UndoPoint { To = to, Round = at.Round, TurnId = at.TurnId, Seat = at.ActiveSeat, Current = k == starts.Count - 1 && at.TurnId == now.TurnId };
                for (int i = t; i <= end && i < book.Count; i++)
                {
                    var deeds = book[i].Deeds;
                    if (deeds == null) continue;
                    int from = i == t ? book[i].DeedsBeforeTurn : 0;
                    int until = i == end ? book[i].DeedsBeforeTurn : deeds.Count;
                    for (int q = from; q < until; q++)
                    {
                        var d = deeds[q];
                        if (d.Kind == UndoDeedKind.Moved && p.Deeds.Count > 0 && p.Deeds[p.Deeds.Count - 1].Kind == UndoDeedKind.Moved) continue; // 이어진 걸음은 한 줄
                        p.Deeds.Add(d);
                    }
                }
                list.Add(p);
            }
            return list;
        }

        /// <summary>못 하는 까닭의 화면 툴팁 키(screen.board.undo.block.&lt;snake_case&gt; — 예 TurnStart → turn_start). None이면 null.</summary>
        public static string BlockKey(UndoBlock block) => block == UndoBlock.None ? null : "screen.board.undo.block." + Snake(block.ToString());

        /// <summary>한 일 종류의 화면 키(screen.menu.rewind.deed.&lt;snake_case&gt;).</summary>
        public static string DeedKey(UndoDeedKind kind) => "screen.menu.rewind.deed." + Snake(kind.ToString());

        static string Snake(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }
    }
}
