using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「운명 네 장 효과」(S13, rulings A24, README 결정 75 B2): 디아루가 재굴림 · 펄기아 순간이동 · 기라티나 자해 후 공격력 · 아르세우스 와일드카드.
    /// 배틀은 실제 흐름(트레이너전, 해피너스 242 대 악식킹 799 — 좌석 0이 먼저)으로 돌고, 굴림은 그 면이 나오는 난수 시드로 정한다. 기술 = 베어가르기 slash(공격력 4, 크리티컬 +1).
    /// </summary>
    public class FateEffectTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const AttackDieFace S = AttackDieFace.StatusSymbol;
        const AttackDieFace R = AttackDieFace.RegularHit;
        const AttackDieFace C = AttackDieFace.CriticalHit;
        const AttackDieFace X = AttackDieFace.None;

        int Key(FateCard c) => FateRules.CardKey(ctx, c);

        SourceRef Src(FateCard c) => new SourceRef(SourceKind.Fate, Key(c));

        void Hand(GameState s, int seat, params FateCard[] cards)
        {
            s.Seats[seat].FateHand.Clear();
            foreach (var c in cards) s.Seats[seat].FateHand.Add(Key(c));
        }

        static ChoiceAnswer Play(FateCard card, int n, params int[] values)
        {
            var a = ChoiceAnswer.Of(AnswerKind.PlayFate, (int)card, n);
            a.Values.AddRange(values);
            return a;
        }

        /// <summary>처음 굴림이 정확히 그 면들로 나오는 난수 시드(그 뒤 굴림까지 이어서 맞출 수 있다).</summary>
        static ulong SeedFor(params AttackDieFace[] faces)
        {
            for (ulong seed = 0; seed < 4000000; seed++)
            {
                var rng = new Pcg32(seed);
                bool same = true;
                for (int i = 0; i < faces.Length && same; i++) same = rng.RollAttackDie() == faces[i];
                if (same) return seed;
            }
            Assert.Fail("그 굴림이 나오는 시드가 없다");
            return 0;
        }

        sealed class Arena
        {
            public GameState S;
            public EventLog Log = new EventLog();
            public int A, D;
        }

        /// <summary>트레이너전: 좌석 0의 해피너스 대 좌석 1의 악식킹(한 마리씩). 좌석 0의 기술 고르기가 열려 있다. 기술 칸 0 = slash, 피로 0.</summary>
        Arena Duel()
        {
            var t = new Arena { S = Battles.Game(ctx, 2) };
            t.A = Scene.Give(t.S, ctx, 0, "242:0");
            t.D = Scene.Give(t.S, ctx, 1, "799:0");
            Battles.Start(t.S, ctx, t.Log, BattleKind.Trainer, 0, 1, -1);
            if (t.S.Pending.Count == 1 && t.S.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(t.S, ctx, t.S.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "좌석 0이 먼저");
            t.S.Pokemon[t.A].Moves[0] = Scene.Move(ctx, "slash");
            return t;
        }

        /// <summary>좌석 0이 slash를 고른다(faces = 그 뒤 굴림들). 굴리기 전 창이 열리면 그 창의 답을 기다린다 — 아니면 굴린 뒤 창까지 간다.</summary>
        List<GameEvent> Attack(Arena t, params AttackDieFace[] faces)
        {
            if (faces.Length > 0) t.S.Rng = new Pcg32(SeedFor(faces));
            int from = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ChoiceKind.BattleAction, Battles.Move(Scene.Move(ctx, "slash")), t.Log);
            return t.Log.Events.GetRange(from, t.Log.Events.Count - from);
        }

        /// <summary>열린 운명 반응 선택(그 좌석의).</summary>
        static PendingChoice FateAsk(GameState s, int seat, Window w)
        {
            var c = s.Pending.Find(x => x.Kind == ChoiceKind.Reaction && x.Sub == ChoiceSub.Fate && x.Seat == seat && !x.Answered);
            Assert.IsNotNull(c, $"좌석 {seat}의 운명 창이 없다");
            Assert.AreEqual(w, c.Window);
            return c;
        }

        /// <summary>그 창의 운명 선택에 답한다. 다른 좌석의 같은 창 물음은 「안 씀」(답한 뒤 한 바퀴 더 묻는 것도 — G6).</summary>
        List<GameEvent> Answer(Arena t, int seat, Window w, ChoiceAnswer a)
        {
            int from = t.Log.Events.Count;
            void Others()
            {
                for (var c = Other(); c != null; c = Other()) Battles.Answer(t.S, ctx, c, ChoiceAnswer.Of(AnswerKind.Decline), t.Log);
            }
            PendingChoice Other() => t.S.Pending.Find(c => c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.Fate && c.Window == w && c.Seat != seat && !c.Answered);
            Others();
            Battles.Answer(t.S, ctx, FateAsk(t.S, seat, w), a, t.Log);
            Others();
            return t.Log.Events.GetRange(from, t.Log.Events.Count - from);
        }

        static List<T> All<T>(List<GameEvent> events) where T : GameEvent => events.FindAll(e => e is T).ConvertAll(e => (T)e);

        // ── 디아루가 ──

        [Test]
        public void S13_4_디아루가1장_고른주사위하나를다시굴린다_피로는그대로()
        {
            var t = Duel();
            Hand(t.S, 0, FateCard.Dialga);
            Attack(t, S, R, R, C, C);                      // 굴림 넷 → 다시 굴린 눈(다섯째) = 크리티컬
            Answer(t, 0, Window.BeforeRoll, ChoiceAnswer.Of(AnswerKind.Decline));
            var ask = FateAsk(t.S, 0, Window.AfterRoll);
            var legal = Scene.Legal(t.S, ctx, ask);
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind, "첫 답 = 안 씀(G9) — 기본값");
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind);
            Assert.AreEqual(1 + 4, legal.Count, "주사위 넷 가운데 하나씩");
            for (int i = 0; i < 4; i++) Assert.IsTrue(ChoiceRules.Contains(legal, Play(FateCard.Dialga, 1, i)));
            int fatigue = t.S.Pokemon[t.A].Fatigue;

            var after = Answer(t, 0, Window.AfterRoll, Play(FateCard.Dialga, 1, 0));
            var ch = All<DiceFacesChanged>(after).FindAll(e => e.Source.Same(Src(FateCard.Dialga)));
            Assert.AreEqual(1, ch.Count);
            Assert.AreEqual(1, ch[0].Changes.Length);
            Assert.AreEqual(0, ch[0].Changes[0].Index);
            Assert.AreEqual(S, ch[0].Changes[0].Before);
            Assert.AreEqual(C, ch[0].Changes[0].After);
            CollectionAssert.AreEqual(new[] { 1, 2, 1 }, ch[0].SrcBefore);
            CollectionAssert.AreEqual(new[] { 0, 2, 2 }, ch[0].SrcAfter);
            Assert.AreEqual(1, All<FatePlayed>(after).Count);
            Assert.AreEqual(fatigue, t.S.Pokemon[t.A].Fatigue, "재굴림은 피로를 올리지 않는다(결정 75 B2 ③)");
            Assert.AreEqual(0, All<FatigueChanged>(after).Count);
            var dmg = All<DamageBreakdown>(after)[0];
            Assert.AreEqual(2 + 2 * 2, dmg.Steps[0].Value, "피해는 다시 굴린 마지막 굴림으로(G14)");
            Assert.AreEqual(0, t.S.Seats[0].FateHand.Count);
        }

        [Test]
        public void S13_4_디아루가_지워진주사위도고르고_다시굴린눈에는면바꾸기가다시걸리지않는다()
        {
            var t = Duel();
            Hand(t.S, 0, FateCard.Dialga);
            var me = t.S.Pokemon[t.A];
            me.Status = StatusEffect.Paralysed;
            me.ParalysisPending = true;                    // 마비된 바로 다음 공격: 일반 타격을 모두 지운다(G14)
            Attack(t, R, R, S, C, R);                      // 다시 굴린 눈 = 일반 타격
            Answer(t, 0, Window.BeforeRoll, ChoiceAnswer.Of(AnswerKind.Decline));
            CollectionAssert.AreEqual(new[] { X, X, S, C }, t.S.Battle.Faces, "마비가 지웠다");
            var legal = Scene.Legal(t.S, ctx, FateAsk(t.S, 0, Window.AfterRoll));
            Assert.IsTrue(ChoiceRules.Contains(legal, Play(FateCard.Dialga, 1, 0)), "지운 주사위도 고른다(결정 75 B2 ②)");

            var after = Answer(t, 0, Window.AfterRoll, Play(FateCard.Dialga, 1, 1));
            var ch = All<DiceFacesChanged>(after);
            Assert.AreEqual(1, ch.Count, "다시 굴린 눈에 마비가 또 걸리지 않는다(결정 75 B2 ①)");
            Assert.AreEqual(X, ch[0].Changes[0].Before);
            Assert.AreEqual(R, ch[0].Changes[0].After);
            Assert.AreEqual(1 + 0 + 2, All<DamageBreakdown>(after)[0].Steps[0].Value, "다시 굴린 일반 타격이 남는다");
        }

        [Test]
        public void S13_4_디아루가3장_고른주사위를각1번_상대공격굴림에도()
        {
            var t = Duel();
            Hand(t.S, 1, FateCard.Dialga, FateCard.Dialga, FateCard.Dialga);
            Attack(t, C, R, S, C, S, S);                   // 상대(좌석 0)의 굴림 넷 → 다시 굴린 눈 둘 = 심볼
            Answer(t, 1, Window.BeforeRoll, ChoiceAnswer.Of(AnswerKind.Decline));
            var legal = Scene.Legal(t.S, ctx, FateAsk(t.S, 1, Window.AfterRoll));
            Assert.AreEqual(1 + 4 + 15, legal.Count, "안 씀 · 1장(주사위 하나) 넷 · 3장(비지 않은 묶음) 열다섯");
            Assert.IsTrue(ChoiceRules.Contains(legal, Play(FateCard.Dialga, 3, 0, 1, 2, 3)));
            Assert.IsFalse(ChoiceRules.Contains(legal, Play(FateCard.Dialga, 3, 3, 0)), "묶음은 오름차순 한 꼴");

            var after = Answer(t, 1, Window.AfterRoll, Play(FateCard.Dialga, 3, 0, 3));
            var ch = All<DiceFacesChanged>(after).Find(e => e.Source.Same(Src(FateCard.Dialga)));
            Assert.AreEqual(2, ch.Changes.Length, "고른 두 주사위만 한 번씩");
            Assert.AreEqual(0, ch.Changes[0].Index);
            Assert.AreEqual(3, ch.Changes[1].Index);
            Assert.AreEqual(S, ch.Changes[0].After);
            Assert.AreEqual(S, ch.Changes[1].After);
            Assert.IsTrue(All<FatePlayed>(after)[0].Play3);
            Assert.AreEqual(1, All<DamageBreakdown>(after)[0].Steps[0].Value, "상대의 크리티컬 둘이 심볼이 됐다");
        }

        /// <summary>좌석 0(손패 hand)이 약한 야생(포획률 3)에게 몬스터볼을 던진다 — 볼 굴림은 faces(그 뒤 굴림까지).</summary>
        GameState ThrowAt(EventLog log, FateCard[] hand, params AttackDieFace[] faces) => ThrowAt(log, hand, 0, faces);

        /// <summary>같은 자리에 매혹적인 스튜 stews개를 먹은 채로(만찬 것 + 만든 것처럼 기록 stews줄).</summary>
        GameState ThrowAt(EventLog log, FateCard[] hand, int stews, params AttackDieFace[] faces)
        {
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "390:0");
            s.Seats[0].Balls[(int)Ball.Regular] = 1;
            int wild = Scene.Spawn(s, ctx, "399:0", SeatId.Wild);
            Hand(s, 0, hand);
            for (int i = 0; i < stews; i++) s.Used.Add(new UsageMark { Seat = 0, Kind = UsageKind.AlluringStew, Scope = s.TurnId });
            Battles.Wild(s, ctx, log, 0, wild);
            s.Rng = new Pcg32(SeedFor(faces));
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), log);
            return s;
        }

        // 결정 76 ③ F2 나(§5 #83 ②): 카드 「주사위 1개를 다시 굴린다」 · 원작 커뮤니티 확인-06 · 2024-11 「된다」 — 볼을 굴린 뒤 굴린 뒤 창(AfterRoll)이 열린다
        [Test]
        public void S13_4_디아루가는포획굴림에도_볼을굴린뒤묻고다시굴린눈으로판정한다()
        {
            var log = new EventLog();
            var s = ThrowAt(log, new[] { FateCard.Dialga }, S, S, R, C);   // 합 1(포획률 3) → 첫 눈을 다시 굴리면 크리티컬 → 합 3
            Assert.AreEqual(0, log.OfType<CaptureAttempt>().Count, "답하기 전에는 판정하지 않는다");
            var ask = FateAsk(s, 0, Window.AfterRoll);
            var legal = Scene.Legal(s, ctx, ask);
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind, "첫 답 = 안 씀");
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind);
            Assert.AreEqual(1 + 3, legal.Count, "볼 주사위 셋 가운데 하나");
            CollectionAssert.AreEqual(new[] { S, S, R }, s.Battle.Faces, "굴린 볼 주사위는 배틀 굴림 자리에");
            Battles.Answer(s, ctx, ask, Play(FateCard.Dialga, 1, 0), log);
            var ch = log.OfType<DiceFacesChanged>().Find(e => e.Source.Same(Src(FateCard.Dialga)));
            Assert.IsNotNull(ch);
            Assert.AreEqual(C, ch.Changes[0].After);
            var cap = log.OfType<CaptureAttempt>()[0];
            Assert.IsTrue(log.Events.IndexOf(ch) < log.Events.IndexOf(cap), "다시 굴린 뒤 판정");
            CollectionAssert.AreEqual(new[] { C, S, R }, cap.Faces);
            Assert.AreEqual(3, cap.Sum);
            Assert.IsTrue(cap.Success);
            Assert.IsNull(s.Battle, "잡았다");
            Assert.AreEqual(0, s.Seats[0].FateHand.Count);

            // 안 쓰면 굴린 그대로 판정 · 손패가 없으면 묻지 않는다(창을 건너뛴다)
            var l2 = new EventLog();
            var t = ThrowAt(l2, new[] { FateCard.Dialga }, S, S, R);
            Battles.Answer(t, ctx, FateAsk(t, 0, Window.AfterRoll), ChoiceAnswer.Of(AnswerKind.Decline), l2);
            Assert.IsFalse(l2.OfType<CaptureAttempt>()[0].Success);
            CollectionAssert.AreEqual(new[] { S, S, R }, l2.OfType<CaptureAttempt>()[0].Faces);
            Assert.AreEqual(1, t.Seats[0].FateHand.Count);
            var l3 = new EventLog();
            ThrowAt(l3, new FateCard[0], S, S, R);
            Assert.AreEqual(1, l3.OfType<CaptureAttempt>().Count, "운명 손패가 없으면 바로 판정");
        }

        // 결정 76 ③ 순서(볼 굴림 → 디아루가 창 → 매혹적인 스튜 묻기 → 판정 [가정]) · 만찬 스튜 + 만든 스튜는 한 굴림에 하나(plan §5 #95 ③)
        [Test]
        public void S13_4_포획굴림은디아루가창뒤에매혹적인스튜를묻고_스튜둘이면한굴림에하나만쓴다()
        {
            var log = new EventLog();
            var s = ThrowAt(log, new[] { FateCard.Dialga }, 2, S, S, S, R);   // 합 0 → 첫 눈을 다시 굴리면 일반 → 합 1 → 스튜로 심볼 하나 → 합 2
            var stew = s.Pending.Find(c => c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.AlluringStew);
            Assert.IsNull(stew, "디아루가 창이 먼저 — 스튜는 아직 묻지 않는다");
            Battles.Answer(s, ctx, FateAsk(s, 0, Window.AfterRoll), Play(FateCard.Dialga, 1, 0), log);
            Assert.AreEqual(0, log.OfType<CaptureAttempt>().Count, "스튜를 답하기 전에는 판정하지 않는다");
            stew = s.Pending.Find(c => c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.AlluringStew);
            Assert.IsNotNull(stew, "다시 굴린 면에 심볼이 남아 스튜를 묻는다");
            Battles.Answer(s, ctx, stew, ChoiceAnswer.Of(AnswerKind.Accept), log);
            var cap = log.OfType<CaptureAttempt>()[0];
            CollectionAssert.AreEqual(new[] { R, S, S }, cap.Faces, "판정은 다시 굴린 면");
            Assert.IsTrue(cap.Allured);
            Assert.AreEqual(2, cap.Sum, "스튜 하나 — 심볼 하나만 일반 타격");
            Assert.IsFalse(cap.Success);
            Assert.IsTrue(CraftRules.AlluringReady(s, 0), "스튜 하나가 그 조우의 다른 굴림에 남는다");
            Assert.IsNull(s.Pending.Find(c => c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.AlluringStew), "같은 굴림에 다시 묻지 않는다");
            Assert.AreEqual(-1, s.Battle.CaptureBall, "포획 단계를 닫았다");
        }

        // ── 펄기아 ──

        /// <summary>높이와 상관없이 n걸음 안의 칸(지금 칸 빼고, 깨어진 세계 빼고).</summary>
        HashSet<int> Within(int from, int steps)
        {
            var r = new HashSet<int>();
            var seen = new Dictionary<int, int> { [from] = 0 };
            var q = new Queue<int>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                if (seen[c] >= steps) continue;
                for (int d = 0; d < Board.Directions; d++)
                {
                    int n = ctx.Board.Neighbor(c, d);
                    if (n == Board.NoCell || seen.ContainsKey(n)) continue;
                    seen[n] = seen[c] + 1;
                    q.Enqueue(n);
                    if (!ctx.Board[n].DistortionWorld) r.Add(n);
                }
            }
            return r;
        }

        static List<int> Cells(List<ChoiceAnswer> legal, int n)
        {
            var r = new List<int>();
            foreach (var a in legal)
                if (a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Palkia && a.Arg == n) r.Add(a.Values[0]);
            return r;
        }

        [Test]
        public void S13_4_펄기아1장_높이와상관없이3걸음안_이동력은그대로()
        {
            var s = Flow.Started(ctx, 2, 5);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            // 걸어서는 높이 때문에 3걸음 안에 못 가는 칸이 3걸음 안에 있는 자리
            int at = -1;
            for (int c = 0; c < ctx.Board.Count && at < 0; c++)
            {
                if (ctx.Board[c].DistortionWorld) continue;
                var walk = MoveRules.Reachable(ctx.Board, c, 3, s.Options.ElevationMode);
                foreach (int n in Within(c, 3))
                    if (walk[n] == MoveRules.Unreachable) { at = c; break; }
            }
            Assert.GreaterOrEqual(at, 0);
            st.Cell = at;
            Hand(s, seat, FateCard.Palkia);
            var cells = Cells(Flow.MapLegal(s, ctx), 1);
            CollectionAssert.AreEquivalent(Within(at, 3), cells, "높이와 상관없이 3걸음 안(p.19 「regardless of elevation」)");
            int far = -1;
            var reach = MoveRules.Reachable(ctx.Board, at, 3, s.Options.ElevationMode);
            foreach (int c in cells)
                if (reach[c] == MoveRules.Unreachable && MapActions.FaceDownSpotAt(s, c) < 0) { far = c; break; }
            Assert.GreaterOrEqual(far, 0);
            CollectionAssert.DoesNotContain(cells, at, "지금 칸은 아니다");
            int moves = st.MovesLeft;

            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, Play(FateCard.Palkia, 1, far), log);
            Assert.AreEqual(far, st.Cell);
            Assert.AreEqual(moves, st.MovesLeft, "이동력을 쓰지 않는다");
            var tp = Flow.Of<Teleported>(log);
            Assert.AreEqual(1, tp.Count);
            Assert.AreEqual(seat, tp[0].Seat);
            Assert.AreEqual(at, tp[0].From);
            Assert.AreEqual(far, tp[0].To);
            Assert.IsTrue(tp[0].Source.Same(Src(FateCard.Palkia)));
            Assert.AreEqual(0, Flow.Of<Moved>(log).Count, "걷지 않는다");
            Assert.AreEqual(seat, Flow.Only(s, ChoiceKind.MapAction).Seat, "턴은 이어진다");
            Assert.AreEqual(0, Cells(Flow.MapLegal(s, ctx), 1).Count);
        }

        [Test]
        public void S13_4_펄기아3장_맵어디든_닿은칸의숨겨진장소는묻는다()
        {
            var s = Flow.Started(ctx, 2, 5);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            Hand(s, seat, FateCard.Palkia, FateCard.Palkia, FateCard.Palkia);
            st.MovesLeft = 0;                              // 이동력이 0이어도 낸다 · 닿은 칸과 상호작용한다(G11 기본)
            var cells = Cells(Flow.MapLegal(s, ctx), 3);
            int world = 0;
            for (int c = 0; c < ctx.Board.Count; c++)
                if (ctx.Board[c].DistortionWorld) world++;
            Assert.AreEqual(ctx.Board.Count - 1 - world, cells.Count, "지금 칸과 깨어진 세계 말고 모든 칸(신오의 심장 캠페인 [가정])");
            int spot = s.Tokens.FindIndex(x => x.Kind == TokenKind.HiddenSpot && !x.FaceUp && !x.Destroyed && x.Cell != st.Cell);
            Assert.GreaterOrEqual(spot, 0);
            int to = s.Tokens[spot].Cell;
            CollectionAssert.Contains(cells, to);

            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, Play(FateCard.Palkia, 3, to), log);
            Assert.AreEqual(to, st.Cell);
            Assert.IsTrue(Flow.Of<FatePlayed>(log)[0].Play3);
            var ask = Flow.Only(s, ChoiceKind.HiddenSpot);
            Assert.AreEqual(spot, ask.Subject, "걸어서 닿았을 때처럼 공개할지 묻는다(A23·G12)");
            Assert.AreEqual(0, ask.Options.Count, "남은 길은 없다");
            Flow.Answer(s, ctx, ChoiceKind.HiddenSpot, ChoiceAnswer.Of(AnswerKind.Decline));
            Assert.AreEqual(seat, Flow.Only(s, ChoiceKind.MapAction).Seat);
        }

        // ── 기라티나 ──

        [Test]
        public void S13_4_기라티나1장_공격전2피해_이번기술공격력1더()
        {
            var t = Duel();
            Hand(t.S, 0, FateCard.Giratina);
            Hand(t.S, 1, FateCard.Giratina);
            Attack(t);
            var ask = FateAsk(t.S, 0, Window.BeforeRoll);
            var legal = Scene.Legal(t.S, ctx, ask);
            Assert.AreEqual(2, legal.Count);
            Assert.IsTrue(legal[1].SameAs(Play(FateCard.Giratina, 1)));
            Assert.AreEqual(1, Scene.Legal(t.S, ctx, FateAsk(t.S, 1, Window.BeforeRoll)).Count, "공격받는 쪽은 못 낸다(원작 커뮤니티 확인)");
            int hp = Battles.Hp(t.S, ctx, t.A);

            t.S.Rng = new Pcg32(SeedFor(R, R, R, R, R));
            var after = Answer(t, 0, Window.BeforeRoll, Play(FateCard.Giratina, 1));
            var hit = All<HpChanged>(after).Find(e => e.Source.Same(Src(FateCard.Giratina)));
            Assert.IsNotNull(hit, "낼 때 바로 2 피해");
            Assert.AreEqual(t.A, hit.PokemonId);
            Assert.AreEqual(hp, hit.Before);
            Assert.AreEqual(hp - 2, hit.After);
            Assert.Less(after.IndexOf(hit), after.IndexOf(All<DiceRolled>(after)[0]), "굴리기 전에");
            var roll = All<DiceRolled>(after)[0];
            Assert.AreEqual(4 + 1, roll.Power, "이번 기술 공격력 +1");
            Assert.AreEqual(5, roll.Faces.Length);
            Assert.IsTrue(System.Array.Exists(roll.PowerMods, m => m.Source.Same(Src(FateCard.Giratina)) && m.Delta == 1));
            Assert.AreEqual(hp - 2, Battles.Hp(t.S, ctx, t.A), "피로 피해 없음(피로 0)");

            Answer(t, 1, Window.AfterRoll, ChoiceAnswer.Of(AnswerKind.Decline));   // 상대의 굴린 뒤 창(낼 것이 없다)
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
            Assert.AreEqual(0, t.S.Battle.FatePower, "다음 배틀 턴에는 남지 않는다");
        }

        [Test]
        public void S13_4_기라티나3장_남은체력전부_절반만큼더_쓰러져도기술을쓰고회복은없다()
        {
            var t = Duel();
            Hand(t.S, 0, FateCard.Giratina, FateCard.Giratina, FateCard.Giratina);
            Battles.SetHp(t.S, ctx, t.A, 7);
            t.S.Pokemon[t.A].Moves[0] = Scene.Move(ctx, "spit_up");   // 「Heal 2 damage」(공격력 4)
            t.S.Rng = new Pcg32(SeedFor(R, R, R, R, R, R, R));
            int from = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ChoiceKind.BattleAction, Battles.Move(Scene.Move(ctx, "spit_up")), t.Log);
            var after = Answer(t, 0, Window.BeforeRoll, Play(FateCard.Giratina, 3));
            var hit = All<HpChanged>(after).Find(e => e.Source.Same(Src(FateCard.Giratina)));
            Assert.AreEqual(7, hit.Before);
            Assert.AreEqual(0, hit.After, "지금 체력만큼(p.19)");
            var roll = All<DiceRolled>(after)[0];
            Assert.AreEqual(4 + 3, roll.Power, "+⌊7/2⌋(원작 커뮤니티 확인)");
            Assert.AreEqual(1, All<MoveUsed>(after).Count, "쓰러져도 그 기술은 쓴다(p.19)");
            Assert.AreEqual(7, All<DamageBreakdown>(after)[0].Steps[0].Value);
            Assert.AreEqual(0, Battles.Hp(t.S, ctx, t.A), "그 공격의 회복을 받지 못한다(원작 커뮤니티 확인)");
            Assert.IsTrue(All<Fainted>(after).Exists(e => e.PokemonId == t.A), "기절 확인에서 쓰러진다");
        }

        // ── 아르세우스 ──

        [Test]
        public void S13_4_아르세우스는다른카드3장효과에대신하고_자기3장효과는울트라버스트몫()
        {
            var t = Duel();
            Hand(t.S, 0, FateCard.Giratina, FateCard.Arceus, FateCard.Arceus);
            Attack(t);
            var legal = Scene.Legal(t.S, ctx, FateAsk(t.S, 0, Window.BeforeRoll));
            Assert.IsTrue(ChoiceRules.Contains(legal, Play(FateCard.Giratina, 3)), "기라티나 1 + 아르세우스 2(p.19 Wildcard)");
            var after = Answer(t, 0, Window.BeforeRoll, Play(FateCard.Giratina, 3));
            var ev = All<FatePlayed>(after)[0];
            Assert.AreEqual((int)FateCard.Giratina, ev.WildcardAs);
            CollectionAssert.AreEquivalent(new[] { Key(FateCard.Giratina), Key(FateCard.Arceus), Key(FateCard.Arceus) }, ev.CardKeys);

            Assert.IsTrue(FateRules.Ready(ctx, FateCard.Arceus, true), "아르세우스의 축복 — 울트라 버스트에서만 낸다(UbNecrozmaTests S13_4_아르세우스3장은…)");
            Assert.IsFalse(FateRules.Ready(ctx, FateCard.Arceus, false), "1장 효과가 없다");
            var s = Flow.Started(ctx, 2, 5);
            Hand(s, s.ActiveSeat, FateCard.Arceus, FateCard.Arceus, FateCard.Arceus);
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.PlayFate));
            foreach (var c in new[] { FateCard.Dialga, FateCard.Palkia, FateCard.Giratina })
            {
                Assert.IsTrue(FateRules.Ready(ctx, c, false), c.ToString());
                Assert.IsTrue(FateRules.Ready(ctx, c, true), c.ToString());
            }
        }

        // ── 봇 ──

        [Test]
        public void M5_봇은이득이분명할때만디아루가를낸다()
        {
            var t = Duel();
            Hand(t.S, 0, FateCard.Dialga);
            Hand(t.S, 1, FateCard.Dialga);
            Attack(t, C, R, S, C);
            Answer(t, 0, Window.BeforeRoll, ChoiceAnswer.Of(AnswerKind.Decline));
            var bot = new Playout.Bot(1);
            var rng = new Pcg32(3);
            // 정책 ⑥ 보충(「봇 종막 대역 0」): 갤럭시단·신·배신자 배틀 밖(여기는 트레이너전)에서는 손패가 가득(5장)일 때만 낸다
            Assert.AreEqual(AnswerKind.Decline, bot.Answer(t.S, ctx, FateAsk(t.S, 0, Window.AfterRoll), rng).Kind, "트레이너전 · 손패 1장: 아껴 둔다");
            Hand(t.S, 0, FateCard.Dialga, FateCard.Palkia, FateCard.Palkia, FateCard.Palkia, FateCard.Palkia);
            Hand(t.S, 1, FateCard.Dialga, FateCard.Palkia, FateCard.Palkia, FateCard.Palkia, FateCard.Palkia);
            for (int i = 0; i < 20; i++)
            {
                Assert.IsTrue(bot.Answer(t.S, ctx, FateAsk(t.S, 0, Window.AfterRoll), rng).SameAs(Play(FateCard.Dialga, 1, 2)), "공격하는 쪽: 0인 눈(심볼)을 다시 굴린다");
                Assert.IsTrue(bot.Answer(t.S, ctx, FateAsk(t.S, 1, Window.AfterRoll), rng).SameAs(Play(FateCard.Dialga, 1, 0)), "받는 쪽: 크리티컬을 다시 굴린다");
            }
            t.S.Battle.Faces.Clear();
            t.S.Battle.Faces.AddRange(new[] { R, R, R, R });
            Assert.AreEqual(AnswerKind.Decline, bot.Answer(t.S, ctx, FateAsk(t.S, 0, Window.AfterRoll), rng).Kind, "이득이 분명하지 않으면 안 낸다");
            Assert.AreEqual(AnswerKind.Decline, bot.Answer(t.S, ctx, FateAsk(t.S, 1, Window.AfterRoll), rng).Kind);
        }
    }
}
