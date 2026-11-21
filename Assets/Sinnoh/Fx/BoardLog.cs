using System;
using System.Collections.Generic;
using System.Globalization;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>기록 줄의 종류: 라운드 머리(「n라운드」) · 일반 줄.</summary>
    public enum BoardLogKind { Line, Round }

    /// <summary>
    /// 보드 기록 한 줄(04 S21, §8-1 P18): 문자열 표의 키 + 빈칸(<see cref="BattleLine"/>과 같은 꼴 — 여기에는 한국어가 없다).
    /// <c>{name}</c> 빈칸은 좌석 이름이라 화면이 채운다(<see cref="Seat"/> — 닉네임은 로비가 준다). <see cref="Seq"/> = 그 줄을 낸 행동:
    /// 화면은 그 행동의 연출이 끝난 뒤에 줄을 붙인다(<see cref="BoardLog.Shows"/> — 결과가 먼저 새지 않게).
    /// </summary>
    public sealed class BoardLogLine
    {
        public BoardLogKind Kind;
        public BattleLine Line;
        /// <summary>줄의 주인 좌석(줄 머리 색 · <c>{name}</c>). 없으면 −1.</summary>
        public int Seat = -1;
        public int Seq;
        /// <summary>그 줄이 속한 라운드(셋업 = 0).</summary>
        public int Round;

        /// <summary>문자열 표(<paramref name="t"/>)와 좌석 이름으로 글을 만든다. 조사는 들어간 이름에 맞춘다.</summary>
        public string Text(Func<string, string> t, Func<int, string> seatName)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            string s = Line.Text(k => k != null && k.StartsWith(BoardLog.ShortPrefix, StringComparison.Ordinal) ? BoardLog.ShortName(t(k.Substring(BoardLog.ShortPrefix.Length))) : t(k));
            if (Seat >= 0 && seatName != null && s.IndexOf("{name}", StringComparison.Ordinal) >= 0) s = KoJosa.Fix(s.Replace("{name}", seatName(Seat) ?? ""));
            return s;
        }
    }

    /// <summary>
    /// 보드에서 일어난 일의 기록(04 S21 — 내 좌석이 받은 갱신만으로 만든다, 비공개는 이벤트가 이미 가려서 온다): 셋업 · 막 · 턴 · 이동 · 찾아보기 · 줍기 · 숨겨진 장소 · 회복 · 배틀의 시작과 끝.
    /// 배틀 안의 줄(기술 · 주사위 · 데미지)은 배틀 기록(<see cref="BattleFeed"/>)의 것이라 여기 두지 않는다. 규칙을 세지 않는다 — 줄은 이벤트가 실은 값 그대로다.
    /// 줄이 없는 이벤트(창 · 섞기 · 사용 기록 …)는 조용히 지나간다. 엔진이 아직 내지 않는 이벤트(사당 · 제작 · 운명 — M5 이후)의 줄은 그 이벤트가 생길 때 더한다.
    /// </summary>
    public sealed class BoardLog
    {
        /// <summary>남기는 줄 수(넘으면 오래된 줄부터 버린다).</summary>
        public const int MaxLines = 400;

        public const string K = "screen.log.line.";

        static readonly string[] ApricornNames = { "red", "blue", "yellow", "pink", "green", "white", "black" };

        readonly Ctx ctx;
        readonly List<BoardLogLine> lines = new List<BoardLogLine>();
        readonly Dictionary<int, int> species = new Dictionary<int, int>();
        readonly HashSet<int> hidden = new HashSet<int>();
        BattleSideInfo[] sides;
        BattleKind battleKind;
        GalacticLeader leader = GalacticLeader.Grunt;   // 갤럭시단 배틀의 상대(명단 공개가 배틀 시작보다 먼저 온다)
        int round, acts;

        public BoardLog(Ctx ctx)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public IReadOnlyList<BoardLogLine> Lines => lines;

        /// <summary>규토리 색의 문자열 키(<c>screen.apricorn.&lt;색&gt;</c>).</summary>
        public static string ApricornKey(int color) => "screen.apricorn." + ApricornNames[Math.Max(0, Math.Min(ApricornNames.Length - 1, color))];

        /// <summary>막 이름(「서막」·「1막」…)의 문자열 키.</summary>
        public static string ActKey(int act) => "screen.board.act." + act.ToString(CultureInfo.InvariantCulture);

        /// <summary>막 카드 제목(「침입」·「여정」…)의 문자열 키.</summary>
        public static string ActTitleKey(int act) => "screen.act.title." + act.ToString(CultureInfo.InvariantCulture);

        /// <summary>갱신 하나의 이벤트를 줄로 옮긴다(받은 순서 그대로).</summary>
        public void Feed(IReadOnlyList<GameEvent> events)
        {
            if (events == null) return;
            foreach (var e in events) if (e != null) Take(e);
            if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
        }

        /// <summary>
        /// 이어 받은 판의 첫 줄(04 §9-12): 지나간 줄은 저장에 없으니 [가정] 그 라운드의 머리 줄과 「저장한 곳에서 이어서 시작했다」 한 줄로 시작한다.
        /// round = 보기의 라운드(셋업 가운데면 0), revealed = 이미 공개된 막 카드 수(뒤에 오는 막 줄의 번호가 이어지게).
        /// </summary>
        public void Resumed(int round, int revealed)
        {
            this.round = Math.Max(0, round);
            acts = Math.Max(0, revealed);
            var head = this.round > 0 ? new BattleLine { Key = "screen.log.round" }.WithValue("n", this.round) : new BattleLine { Key = "screen.log.setup" };
            lines.Add(new BoardLogLine { Kind = BoardLogKind.Round, Line = head, Seq = ResumedSeq, Round = this.round });
            lines.Add(new BoardLogLine { Kind = BoardLogKind.Line, Line = new BattleLine { Key = K + "resumed" }, Seat = -1, Seq = ResumedSeq, Round = this.round });
        }

        /// <summary>
        /// 무르기 · 되돌리기(결정 149 — undo.md §3-5): 지운 미래의 줄(Seq ≥ nextSeq — 그 Seq는 다시 쓰이므로 남기면 기록 줄 핑이 엉뚱한 줄을 가리킨다)을 빼고,
        /// 되돌린 자리의 라운드 머리(마지막 남은 줄이 그 라운드가 아니면) + 「○○은(는) 무르기를 했다」(host = 「○○은(는) n라운드로 되돌렸다」) 한 줄을 더한다.
        /// round · revealed는 <see cref="Resumed"/>와 같은 뜻(되돌린 보기의 라운드 · 공개된 막 수). 남은 배틀 맥락은 버린다.
        /// by &lt; 0 = 누가 했는지 모르는 되돌림(끊긴 동안 일어나 재접속 스냅샷으로 알아챘다 — undo.md [가정 U9]): 지운 미래의 줄만 빼고 무르기 줄은 더하지 않는다.
        /// </summary>
        public void Rewound(int nextSeq, int round, int revealed, int by, bool host)
        {
            lines.RemoveAll(l => l.Seq != ResumedSeq && l.Seq >= nextSeq);
            this.round = Math.Max(0, round);
            acts = Math.Max(0, revealed);
            sides = null;
            leader = GalacticLeader.Grunt;
            var last = lines.Count > 0 ? lines[lines.Count - 1] : null;
            if (last == null || last.Round != this.round)
            {
                var head = this.round > 0 ? new BattleLine { Key = "screen.log.round" }.WithValue("n", this.round) : new BattleLine { Key = "screen.log.setup" };
                lines.Add(new BoardLogLine { Kind = BoardLogKind.Round, Line = head, Seq = ResumedSeq, Round = this.round });
            }
            if (by < 0) return;
            var line = host ? new BattleLine { Key = K + "rewind" }.WithValue("round", this.round) : new BattleLine { Key = K + "undo" };
            lines.Add(new BoardLogLine { Kind = BoardLogKind.Line, Line = line, Seat = by, Seq = ResumedSeq, Round = this.round });
        }

        /// <summary>이벤트가 싣지 않은 여행 점수 변화(트레이너 카드 능력 등 — 화면이 보기의 차이로 센다, M6): 「○○의 여행 점수가 n점 늘었다/줄었다」. Seq는 그 갱신의 마지막 이벤트(연출이 끝날 때 붙는다).</summary>
        public void JpDrift(int seat, int delta, IReadOnlyList<GameEvent> events)
        {
            if (delta == 0 || events == null || events.Count == 0) return;
            var last = events[events.Count - 1];
            Add(last, seat, new BattleLine { Key = K + (delta > 0 ? "jp_up" : "jp_down") }.WithValue("n", Math.Abs(delta)));
            if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
        }

        /// <summary>이벤트가 없는 도로 놓기(worker B — 화면이 보기의 도로 칸 차이로 센다): 「○○은(는) 도로 n칸을 놓았다」. Seq는 그 갱신의 마지막 이벤트.</summary>
        public void RoadDrift(int seat, int cells, IReadOnlyList<GameEvent> events)
        {
            if (cells <= 0 || events == null || events.Count == 0) return;
            Add(events[events.Count - 1], seat, new BattleLine { Key = K + "road" }.WithValue("n", cells));
            if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
        }

        /// <summary>이어 받은 줄의 Seq(행동이 아니라 화면이 넣은 줄 — 대기열의 어느 항목과도 겹치지 않는다).</summary>
        public const int ResumedSeq = int.MinValue;

        void Take(GameEvent e)
        {
            switch (e)
            {
                case VillagePicked v:
                    if (v.VillageKey >= 0) Add(e, v.Seat, new BattleLine { Key = K + "village" }.With("village", ctx.Fixtures.Fixtures[v.VillageKey].Key));
                    break;
                case StarterPicked s:
                    species[s.PokemonId] = s.Species;
                    Add(e, s.Seat, new BattleLine { Key = K + "starter" }.With("mon", MonKey(s.Species)));
                    break;
                case TrainerCardsRevealed tr:
                    if (tr.Picks != null)
                        foreach (var p in tr.Picks)
                            if (p != null && p.CardKey >= 0) Add(e, p.Seat, new BattleLine { Key = K + "trainer" }.With("card", "trainer_card." + ctx.Cards.TrainerCards[p.CardKey].Key));
                    break;
                case ActRevealed _:
                {
                    int act = Math.Min(acts++, HosState.Acts - 1);
                    Add(e, -1, new BattleLine { Key = K + "act" }.With("act", ActKey(act)).With("title", ActTitleKey(act)));
                    break;
                }
                case TokensPlaced tp:
                    if (tp.Kind == TokenKind.Grunt) Add(e, -1, new BattleLine { Key = K + "grunts" });
                    break;
                case RoundStarted rs:
                    round = rs.Round;
                    lines.Add(new BoardLogLine { Kind = BoardLogKind.Round, Line = new BattleLine { Key = "screen.log.round", Event = e }.WithValue("n", rs.Round), Seq = e.Seq, Round = round });
                    break;
                case Moved m:
                    if (m.Path != null && m.Path.Length > 1) Add(e, m.Seat, new BattleLine { Key = K + "move" }.WithValue("n", m.Path.Length - 1));
                    break;
                case Searched _:
                    Add(e, e.Seat, new BattleLine { Key = K + "search" });
                    break;
                case EncounterPicked ep:
                    Add(e, -1, new BattleLine { Key = K + "encounter" }.With("mon", MonKey(ep.CardKey)));
                    break;
                case ApricornsTaken at:
                    if (at.Count > 0) Add(e, at.Seat, new BattleLine { Key = K + "take" }.With("color", ApricornKey(at.Color)).WithValue("n", at.Count));
                    break;
                case ApricornsGained ag:
                    Gains(e, ag.Changes);
                    break;
                case HiddenSpotRevealed hr:
                    if (hr.SpotKey >= 0 && hr.SpotKey < ctx.Items.Items.Count) Add(e, hr.Seat, new BattleLine { Key = K + "reveal" }.With("spot", ctx.Items.Items[hr.SpotKey].Key));
                    break;
                case HiddenSpotPassed _:
                    Add(e, e.Seat, new BattleLine { Key = K + "pass" });
                    break;
                // M5 「맵 결과·배틀 안 연출」: 숨겨진 장소 결과(규토리 더미는 받은 규토리 줄이 적는다) · 승리 보상 · 신 격파
                case SpotResolved sr:
                    if (sr.Result == SpotResult.Relic && sr.SpotKey >= 0 && sr.SpotKey < ctx.Items.Items.Count) Add(e, sr.Seat, new BattleLine { Key = K + "relic" }.With("item", ctx.Items.Items[sr.SpotKey].Key));
                    else if (sr.Result == SpotResult.Star) Add(e, sr.Seat, new BattleLine { Key = K + "star" }.WithValue("n", sr.Count));
                    else if (sr.Result == SpotResult.Alpha) Add(e, sr.Seat, new BattleLine { Key = K + "alpha" });
                    break;
                case SpoilsTaken st:
                {
                    int relic = SpoilsBeats.Relic(st, out int fate);
                    if (relic >= 0 && relic < ctx.Items.Items.Count) Add(e, st.Seat, new BattleLine { Key = K + "spoils.relic" }.With("item", ctx.Items.Items[relic].Key));
                    else if (fate > 0) Add(e, st.Seat, new BattleLine { Key = K + "spoils.fate" }.WithValue("n", fate));
                    break;
                }

                case Healed h:
                    if (h.Seat >= 0) Add(e, h.Seat, new BattleLine { Key = K + "heal" });
                    break;
                case Flew fl:
                    foreach (var f in ctx.Fixtures.Fixtures)
                        if (f.Kind == FixtureKind.Village && f.Cell == fl.To) { Add(e, fl.Seat, new BattleLine { Key = K + "fly" }.With("village", f.Key)); break; }
                    break;
                case BattleStarted bs:
                    sides = bs.Sides;
                    battleKind = bs.Kind;
                    if (bs.Kind == BattleKind.Galactic)
                        Add(e, SeatOfSide(0) >= 0 ? SeatOfSide(0) : SeatOfSide(1), new BattleLine { Key = K + "battle.galactic" + (leader == GalacticLeader.Commander ? ".commander" : leader == GalacticLeader.Cyrus ? ".cyrus" : "") });
                    else if (bs.Kind == BattleKind.UltraBeast || bs.Kind == BattleKind.Necrozma)
                        Add(e, SeatOfSide(0) >= 0 ? SeatOfSide(0) : SeatOfSide(1), new BattleLine { Key = K + (bs.Kind == BattleKind.Necrozma ? "battle.necrozma" : "battle.ub") });
                    leader = GalacticLeader.Grunt;
                    break;
                case PokemonGained pg:
                    species[pg.PokemonId] = pg.Species;
                    if (pg.Seat >= 0 && pg.BallKey >= 0) Add(e, pg.Seat, new BattleLine { Key = K + "caught" }.With("mon", MonKey(pg.Species)));
                    break;
                case Fled _:
                    Add(e, -1, new BattleLine { Key = K + "fled" });
                    Gains(e, ((Fled)e).Changes);
                    break;
                case BattleEnded be:
                    Ended(be);
                    break;
                case Whiteout w:
                    Add(e, w.Seat, new BattleLine { Key = K + "whiteout" });
                    break;
                case GalacticBattle gb:
                    leader = gb.Leader;
                    break;
                // 2막 이후·종막(M5 「막·종막 화면」)
                case ShardGained sg:
                    Add(e, sg.Seat, new BattleLine { Key = K + "shard" }.With("shard", "screen.shard." + ((int)sg.Shard).ToString(CultureInfo.InvariantCulture)));
                    break;
                case HeartRestored hrt:
                    Add(e, hrt.Seat, new BattleLine { Key = K + "heart" });
                    break;
                case EndingChosen ec:
                    Add(e, ec.Seat, new BattleLine { Key = K + (ec.Traitor ? "ending.traitor" : "ending.hero") });
                    break;
                case GameEnded ge:
                    // 균열 70% 패배(86행)는 이긴 좌석이 없다 — 「{name}의 승리」 대신 패배 줄(M6 「UB 화면 묶음 1」)
                    if (ge.Reason == GameEndReason.BreachLost) Add(e, -1, new BattleLine { Key = K + "ub.lost" });
                    else if (ge.Reason == GameEndReason.AllEliminated) Add(e, -1, new BattleLine { Key = K + "ub.eliminated_all" });   // 모두 탈락 · 네크로즈마 격파(모두의 승리 — M6 「UB 배틀 화면 묶음」)
                    else if (ge.Reason == GameEndReason.NecrozmaDefeated) Add(e, -1, new BattleLine { Key = K + "ub.won" });
                    else Add(e, ge.Winners != null && ge.Winners.Length > 0 ? ge.Winners[0] : -1, new BattleLine { Key = K + "end" });
                    break;
                case ActGoalMet g:
                    Add(e, g.Seat, new BattleLine { Key = K + "goal" }.WithValue("n", g.Act));
                    break;
                // 사당·균열(M5 「사당·균열 화면」)
                case ShrineOffered so:
                    if (so.ShrineKey >= 0 && so.ShrineKey < ctx.Fixtures.Fixtures.Count) Add(e, so.Seat, new BattleLine { Key = K + "offer" }.With("shrine", ctx.Fixtures.Fixtures[so.ShrineKey].Key));
                    break;
                case ShrineCardsDrawn sd:
                    if (sd.CardKeys != null)
                        foreach (int card in sd.CardKeys)
                            if (card >= 0 && card < ctx.Decks.CardIds.Count) Add(e, -1, new BattleLine { Key = K + "shrine_card" }.With("card", ctx.Decks.CardIds[card]));
                    break;
                case LegendEvent le:
                    if (le.Placed && le.ShrineKey >= 0) Add(e, -1, new BattleLine { Key = K + "legend" }.With("mon", MonKey(le.Species)).With("shrine", ctx.Fixtures.Fixtures[le.ShrineKey].Key));
                    break;
                case GodDescended gd:
                    if (!gd.Stayed && gd.God > 0) Add(e, -1, new BattleLine { Key = K + "descend" }.With("god", GodKey(gd.God)));
                    break;
                // 스탬프·도전·거래(M5 「스탬프·도전·거래 화면」) — 좌석 이름은 「플레이어 n」(닉네임은 로비 M7)
                case StampGained sg:
                {
                    // 어떤 조건의 스탬프인가(결정 101 ④ — 기록 줄 끝에 조건 문구)
                    int slot = 0;
                    for (int i = 0; i < SeatState.StampCount; i++) if (StampRules.ItemOf(ctx, i) == sg.StampKey) slot = i;
                    Add(e, sg.Seat, new BattleLine { Key = K + "stamp" }.WithValue("n", sg.CountAfter).With("cond", "screen.stamp.cond." + (slot + 1).ToString(CultureInfo.InvariantCulture)));
                    break;
                }
                case ChallengeIssued ci:
                    if (ci.To >= 0) Add(e, ci.From, new BattleLine { Key = K + "challenge" }.WithValue("to", ci.To + 1));
                    break;
                case ChallengeAnswered ca:
                    if (!ca.Accept) Add(e, ca.To, new BattleLine { Key = K + "challenge.declined" });
                    break;
                case TradeProposed tp2:
                    Add(e, tp2.From, new BattleLine { Key = K + "trade.offer" }.WithValue("to", tp2.To + 1));
                    break;
                case TradeAnswered ta:
                    if (!ta.Accept) Add(e, ta.To, new BattleLine { Key = K + "trade.declined" });
                    break;
                case TradeDone td:
                    Add(e, td.B, new BattleLine { Key = K + "trade.done" }.WithValue("to", td.A + 1));
                    break;
                case UsageMarked um when um.Kind == UsageKind.GodVictory:
                    // 신 격파 뒤 추가 턴(결정 198 — 엔진의 「한 턴 더」 표): 바로 앞의 그 좌석 신 격파 줄을 「한 번 더」 꼴로
                    for (int i = lines.Count - 1; i >= 0; i--)
                        if (lines[i].Seat == um.Seat && lines[i].Line != null && lines[i].Line.Key == K + "god.done") { lines[i].Line.Key = K + "god"; break; }
                    break;
                case TokenRemoved tr:
                    if (tr.TokenKind == TokenKind.Grunt) Freed(e, tr.Cell);
                    else if (tr.TokenKind == TokenKind.God) Add(e, tr.Seat, new BattleLine { Key = K + "god.done" });   // 신 격파(80행) — 추가 턴 표가 뒤따르면 「한 번 더」 줄로(결정 198)
                    break;
                case MoveTaught mt:
                    if (species.TryGetValue(mt.PokemonId, out int sp) && mt.MoveKey >= 0)
                        Add(e, -1, new BattleLine { Key = K + "taught" }.With("mon", MonKey(sp)).With("move", BattleFeed.MoveNameKey(ctx, mt.MoveKey)));
                    break;
                case MoveForgotten mf:
                    if (species.TryGetValue(mf.PokemonId, out int fsp) && mf.MoveKey >= 0)
                        Add(e, e.Seat >= 0 ? e.Seat : -1, new BattleLine { Key = K + "forgot" }.With("mon", MonKey(fsp)).With("move", BattleFeed.MoveNameKey(ctx, mf.MoveKey)));
                    break;
                case ItemHeld ih:
                {
                    // 지니게 함(PokemonId) · 가방으로 돌아옴(PokemonId −1 — 들고 있던 포켓몬은 FromPokemonId)
                    int holder = ih.PokemonId >= 0 ? ih.PokemonId : ih.FromPokemonId;
                    if (ih.ItemKey < 0 || ih.ItemKey >= ctx.Items.Items.Count || !species.TryGetValue(holder, out int hsp)) break;
                    Add(e, e.Seat >= 0 ? e.Seat : -1, new BattleLine { Key = K + (ih.PokemonId >= 0 ? "held" : "unheld") }.With("mon", MonKey(hsp)).With("item", ctx.Items.Items[ih.ItemKey].Key));
                    break;
                }
                case Released r:
                    if (r.Species >= 0) Add(e, r.Seat, new BattleLine { Key = K + "released" }.With("mon", MonKey(r.Species)));
                    break;
                // 트레이너 카드(M6 「트레이너 카드 해금 연출」 — 48·49·35행): 해금 · 카드 바꾸기 · 날씨 휠(능력 사용 50행은 결과 줄이 적는다)
                case CardUnlocked cu:
                    if (cu.TierAfter >= 2 && cu.TierAfter <= 3) Add(e, cu.Seat, new BattleLine { Key = K + "unlock" }.With("tier", "screen.trainer.tier." + cu.TierAfter.ToString(CultureInfo.InvariantCulture)));
                    break;
                case TrainerCardSwapped sw:
                    if (sw.ToCard >= 0 && sw.ToCard < ctx.Cards.TrainerCards.Count) Add(e, sw.Seat, new BattleLine { Key = K + "swap" }.With("card", "trainer_card." + ctx.Cards.TrainerCards[sw.ToCard].Key));
                    break;
                case WeatherSet ws:
                    if (ws.Weather >= 0) Add(e, ws.Seat, new BattleLine { Key = K + "weather" }.With("weather", "screen.card.weather." + ws.Weather.ToString(CultureInfo.InvariantCulture)));
                    break;
                // 울트라 버스트(M6 「UB 시작」): UB 이름은 폼 꼬리 「(울트라 비스트)」를 뺀다(ShortPrefix)
                case UbTurnStarted _:
                    Add(e, -1, new BattleLine { Key = K + "ub.turn" });
                    break;
                case UbMoved um:
                    UbMove(um);
                    break;
                case TokenMoved tm:
                    Add(e, -1, new BattleLine { Key = K + (tm.To >= 0 ? "ub.token" : "ub.token.lost") });
                    break;
                case CellDestroyed cd:
                    Add(e, -1, new BattleLine { Key = K + "ub.destroy" }.With("mon", ShortMon(cd.ByUbId >= 0 ? cd.ByUbId : Necrozma(1)))
                        .With("what", cd.TokenKind == TokenKind.ApricornReef ? K + "ub.reef" : cd.TokenKind == TokenKind.ApricornTree || cd.TokenKind == TokenKind.DistortedTree ? K + "ub.tree" : K + "ub.shrine"));
                    break;
                case BreachChanged bc:
                    Breach(bc);
                    break;
                case BreachCardsDealt bd:
                    if (bd.PerSeat != null)
                        foreach (var d in bd.PerSeat)
                            if (d != null && d.CardKeys != null)
                                foreach (int card in d.CardKeys) Add(e, d.Seat, new BattleLine { Key = K + "ub.card" }.With("card", CardKey(card)));
                    break;
                case BreachCardAttached ba:
                    if (species.TryGetValue(ba.PokemonId, out int bsp)) Add(e, e.Seat, new BattleLine { Key = K + "ub.attach" }.With("mon", MonKey(bsp)).With("card", CardKey(ba.CardKey)));
                    break;
                case BlessingUsed bu:
                    if (bu.Target < 0) Add(e, bu.Seat, new BattleLine { Key = K + "ub.blessing.skip" }.WithValue("n", bu.Left));
                    break;
                case Revived _:
                    Add(e, e.Seat, new BattleLine { Key = K + "ub.revived" });
                    break;
                case UbTurnSkipped _:
                    Add(e, -1, new BattleLine { Key = K + "ub.skipped" });
                    break;
                case BlessingGained bg:
                    Add(e, bg.Seat, new BattleLine { Key = K + "ub.blessing.gain" }.WithValue("n", bg.Total));
                    break;
                case Eliminated _:
                    Add(e, e.Seat, new BattleLine { Key = K + "ub.eliminated" });
                    break;
                case NecrozmaPhase np:
                    Add(e, -1, new BattleLine { Key = K + "ub.phase" }.WithValue("n", np.Next));
                    break;
                case CoinFlip cf:
                    Add(e, -1, new BattleLine { Key = K + "ub.coin" }.With("mon", MonKey(Necrozma(2, cf.Result))));
                    break;
                case OnslaughtDrawn od:
                    Add(e, -1, new BattleLine { Key = K + "ub.onslaught" }.With("card", CardKey(od.CardKey) + ".text.1"));
                    break;
            }
        }

        /// <summary>
        /// 빈칸 키의 머리: 이 키의 글은 폼 꼬리 「(…)」를 뺀다(UB 이름의 「(울트라 비스트)」 — <see cref="BoardLogLine.Text"/>가 푼다).
        /// </summary>
        public const string ShortPrefix = "short:";

        /// <summary>이름 끝의 폼 꼬리 「(…)」를 뺀다(<c>BoardHud.ShortName</c>과 같은 셈).</summary>
        public static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int at = name.IndexOf('(');
            return at > 0 ? name.Substring(0, at).TrimEnd() : name;
        }

        string ShortMon(int def) => ShortPrefix + MonKey(def);

        // 그 페이즈(·동전)의 네크로즈마 카드
        int Necrozma(int phase, int sunMoon = -1) => UbNecrozma.Card(ctx, phase, sunMoon);

        string CardKey(int card) => card >= 0 && card < ctx.Decks.CardIds.Count ? ctx.Decks.CardIds[card] : BattleFeed.K + "mon.unknown";

        // UB 한 칸 이동: 같은 행동 안에서 같은 UB가 잇달아 옮기면 한 줄(「○○이 n칸 이동했다」)로 모은다
        void UbMove(UbMoved m)
        {
            var last = lines.Count > 0 ? lines[lines.Count - 1] : null;
            if (last != null && last.Seq == m.Seq && last.Line.Key == K + "ub.move" && last.Line.Event is UbMoved prev && prev.UbId == m.UbId)
            {
                var n = last.Line.Args.Find(a => a.Name == "n");
                n.Value = (int.Parse(n.Value, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
                last.Line.Event = m;   // 모은 줄은 마지막 걸음의 단계가 시작될 때 보인다(UbLineIn — 첫 걸음에 끝 칸 수가 먼저 뜨지 않게, M6 화면 교차 검토 #5)
                return;
            }
            Add(m, -1, new BattleLine { Key = K + "ub.move" }.With("mon", ShortMon(m.UbId)).WithValue("n", 1));
        }

        // 균열 강도: 같은 행동 안의 바뀜은 한 줄(처음 값 → 마지막 값)
        void Breach(BreachChanged b)
        {
            if (b.After == b.Before) return;
            foreach (var line in lines)
                if (line.Seq == b.Seq && line.Line.Key == K + "ub.breach")
                {
                    line.Line.Args.Find(a => a.Name == "to").Value = b.After.ToString(CultureInfo.InvariantCulture);
                    return;
                }
            Add(b, -1, new BattleLine { Key = K + "ub.breach" }.WithValue("from", b.Before).WithValue("to", b.After));
        }

        // 배틀 끝: 이긴 쪽·진 쪽의 좌석마다 한 줄(포획·도망은 바로 앞 줄이 끝을 알린다), 이어서 이긴 좌석이 받은 것
        void Ended(BattleEnded be)
        {
            if (be.Result != BattleResult.Captured && be.Result != BattleResult.Fled)
            {
                if (be.Winner < 0) Add(be, -1, new BattleLine { Key = K + "battle.draw" });
                else
                    for (int side = 0; side < 2; side++)
                    {
                        int seat = SeatOfSide(side);
                        if (seat >= 0) Add(be, seat, new BattleLine { Key = K + (side == be.Winner ? "battle.win" : "battle.lose") });
                    }
            }
            Gains(be, be.Changes);
            sides = null;
        }

        void Gains(GameEvent e, ResChange[] changes)
        {
            if (changes == null) return;
            foreach (var c in changes)
            {
                if (c == null || c.After <= c.Before || c.Seat < 0) continue;
                if (c.Res == ResKind.Apricorn) Add(e, c.Seat, new BattleLine { Key = K + "gain.apricorn" }.With("color", ApricornKey(c.Key)).WithValue("n", c.After - c.Before));
                else if (c.Res == ResKind.Jp) Add(e, c.Seat, new BattleLine { Key = K + "gain.jp" }.WithValue("n", c.After - c.Before));
            }
        }

        void Freed(GameEvent e, int cell)
        {
            foreach (var f in ctx.Fixtures.Fixtures)
                if (f.Kind == FixtureKind.Village && f.Cell == cell)
                {
                    Add(e, -1, new BattleLine { Key = K + "freed" }.With("village", f.Key));
                    return;
                }
        }

        /// <summary>신오의 신 이름 키(운명 카드의 이름과 같다 — change_of_fate.dialga …).</summary>
        public static string GodKey(int god) => "change_of_fate." + ((SinnohGod)god).ToString().ToLowerInvariant();

        int SeatOfSide(int side) => sides != null && side < sides.Length && sides[side] != null && sides[side].Seat >= 0 ? sides[side].Seat : -1;

        string MonKey(int def) => def >= 0 && def < ctx.Cards.Pokemon.Count ? "pokemon." + ctx.Cards.Pokemon[def].Key : BattleFeed.K + "mon.unknown";

        void Add(GameEvent e, int seat, BattleLine line)
        {
            line.Event = e;
            // 첫 라운드가 시작되기 전의 줄(고향 마을 · 스타팅 포켓몬 · 서막)은 「게임 준비」 머리 아래에 선다
            if (lines.Count == 0 && round == 0) lines.Add(new BoardLogLine { Kind = BoardLogKind.Round, Line = new BattleLine { Key = "screen.log.setup", Event = e }, Seq = e.Seq, Round = 0 });
            lines.Add(new BoardLogLine { Kind = BoardLogKind.Line, Line = line, Seat = seat, Seq = e.Seq, Round = round });
        }

        /// <summary>
        /// 그 줄을 지금 보여도 되나: 그 줄을 낸 행동이 화면 대기열에서 아직 다 재생되지 않았으면(재생 중 · 기다리는 중) 숨긴다 —
        /// 줄은 항목이 끝날 때 붙는다(04 §8-1 P18). 대기열이 없으면(셋업 · 연출 없이 도는 흐름) 다 보인다.
        /// </summary>
        public bool Shows(BoardLogLine line, FxSequencer seq)
        {
            if (line == null) return false;
            if (seq == null) return true;
            hidden.Clear();
            foreach (var entry in seq.Queued) hidden.Add(entry.Seq);
            return !hidden.Contains(line.Seq);
        }

        /// <summary>지금 보이는 줄(받은 순서)을 <paramref name="into"/>에 담는다.</summary>
        public void Shown(FxSequencer seq, List<BoardLogLine> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            hidden.Clear();
            FxEntry ub = null;
            if (seq != null)
                foreach (var entry in seq.Queued)
                {
                    hidden.Add(entry.Seq);
                    if (ReferenceEquals(entry, seq.Current) && entry.Kind == ActionKind.UbTurn) ub = entry;
                }
            foreach (var line in lines)
                if (!hidden.Contains(line.Seq) || ub != null && line.Seq == ub.Seq && UbLineIn(ub, line, seq.LocalMs)) into.Add(line);
        }

        /// <summary>
        /// 재생 중인 UB 턴 항목의 줄은 한 줄씩 붙는다(결정 84 M2 가): 줄의 이벤트가 든 단계(UB 턴 시작 821 · 이동 820 · 파괴 830 · 카드 받기 850)가 시작되면 보인다.
        /// 밟힌 토큰의 줄은 그 토큰 날기 단계(831)가 시작될 때. 단계가 없는 이벤트의 줄(균열 강도 「n% → m%」)은 그 항목이 끝날 때 — 지금처럼.
        /// </summary>
        static bool UbLineIn(FxEntry entry, BoardLogLine line, int localMs)
        {
            var e = line.Line != null ? line.Line.Event : null;
            if (e == null) return false;
            foreach (var s in entry.Steps)
                if (s.Events.Contains(e)) return localMs >= s.StartMs;
            return false;
        }
    }
}
