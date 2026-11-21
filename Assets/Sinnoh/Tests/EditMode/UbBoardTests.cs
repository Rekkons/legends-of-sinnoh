using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「UB 보드 화면」의 보드 셈(<c>UbStaging.cs</c> — 엔진 무관, dotnet에서도 돈다): 왜곡 게이트 이동 15 · UB 이동 82 · 밟은 칸의 파괴 83행의 방장·화면 행과 길이,
    /// 실제 울트라 버스트 판(4인 · 시드 20261117 — UB 목업과 같은 판)에서 UB·네크로즈마 피규어가 선 칸과 UB 턴 동안 옮겨 가는 칸, 파괴된 나무·산호·사당의 어두운 정도.
    /// </summary>
    public class UbBoardTests
    {
        const ulong UbSeed = 20261117;
        const int UbSeats = 4;

        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        [Test]
        public void M6_UB보드행_방장과화면이같은행과길이()
        {
            var events = new (GameEvent e, int row, int ms)[]
            {
                (new GateTravel { Seat = 0, FromGate = 0, ToGate = 1, FromCell = 1, ToCell = 2 }, UbRows.Gate, 1600),
                (new UbMoved { Seat = SeatId.UltraBeast, UbId = 0, From = 1, To = 2 }, UbRows.Move, 700),
                (new CellDestroyed { Seat = SeatId.UltraBeast, Cell = 1, TokenKind = TokenKind.ApricornTree, ByUbId = 0 }, UbRows.Destroy, 1500),
            };
            foreach (var (e, row, ms) in events)
            {
                Assert.AreEqual(row, PlayTiming.RowOf(e), e.GetType().Name + " 방장 행");
                Assert.AreEqual(row, FxRows.Of(e), e.GetType().Name + " 화면 행");
                Assert.AreEqual(ms, PlayTiming.RowMs(e, row), e.GetType().Name + " 길이");
                Assert.IsTrue(FxBuilt.Drawn(row), row + "행은 화면이 그린다");
                Assert.IsTrue(SoundCues.Covers(row), row + "행 소리 표");
            }
            Assert.IsTrue(FxRows.Chains(UbRows.Move) && FxRows.Chains(UbRows.Destroy), "UB 이동·파괴는 하나씩 단계가 선다");
            Assert.IsFalse(FxRows.Chains(UbRows.Gate));
            // 균열 바뀜은 앞의 파괴 단계가 그린다 — 단계 없이(UB 턴 시작 · 빛 모음 · 구간은 M6 「UB 화면 묶음 1」의 821·840·850 — UbScreenTests, 토큰 날기 831은 UbFillTests)
            foreach (var e in new GameEvent[] { new BreachChanged() })
            {
                Assert.AreEqual(0, PlayTiming.RowOf(e), e.GetType().Name);
                Assert.AreEqual(FxRows.None, FxRows.Of(e), e.GetType().Name);
            }
        }

        [Test]
        public void M6_게이트박자_가라앉아사라졌다가건너편에서솟는다()
        {
            int total = PlayTiming.RowMs(new GateTravel(), UbRows.Gate);
            var start = GateBeats.At(0, total);
            Assert.IsFalse(start.AtDest);
            Assert.IsFalse(start.Hidden);
            Assert.AreEqual(0f, start.Depth);
            Assert.AreEqual(0f, start.Cross, "카메라는 출발 게이트에서");
            int sink = total * GateBeats.SinkPermille / 1000, rise = total * GateBeats.RisePermille / 1000, land = total * GateBeats.LandPermille / 1000;
            Assert.Greater(GateBeats.At(sink / 2, total).Depth, 0f, "가라앉는 중");
            var gap = GateBeats.At((sink + rise) / 2, total);
            Assert.IsTrue(gap.Hidden, "틈 속에서는 말을 그리지 않는다");
            Assert.Greater(gap.Line, 0f, "두 게이트 사이에 점선");
            Assert.Greater(gap.Cross, 0f);
            Assert.Less(gap.Cross, 1f, "카메라가 건너가는 중");
            var up = GateBeats.At((rise + land) / 2, total);
            Assert.IsTrue(up.AtDest);
            Assert.IsFalse(up.Hidden);
            Assert.Greater(up.Depth, 0f, "솟아오르는 중");
            Assert.Less(up.Depth, 1f);
            Assert.AreEqual(1f, up.Cross);
            var ring = GateBeats.At((land + total) / 2, total);
            Assert.Greater(ring.RingTo, 0f, "착지 고리");
            Assert.AreEqual(0f, ring.Depth);
            var end = GateBeats.At(total, total);
            Assert.IsTrue(end.AtDest);
            Assert.AreEqual(0f, end.Depth);
            Assert.IsFalse(end.Hidden);
        }

        [Test]
        public void M6_UB판_피규어는UB토큰칸에서고네크로즈마는깨어진세계가운데()
        {
            var s = UbScenes.Started(ctx, UbSeats, UbSeed);
            var board = BoardLoader.Load(TestData.ReadText("board.json"), out var looks);
            int beasts = 0;
            foreach (var t in s.Tokens)
            {
                if (t.Kind != TokenKind.UltraBeast && t.Kind != TokenKind.Necrozma) continue;
                var at = UbBoard.Of(s.Tokens, t.Def, null, null, 0);
                Assert.IsTrue(at.Shown, ctx.Cards.Pokemon[t.Def].Key);
                Assert.AreEqual(t.Cell, at.From, ctx.Cards.Pokemon[t.Def].Key + " 토큰 칸");
                Assert.AreEqual(t.Cell, at.To);
                Assert.AreEqual(BeastClip.Idle, at.Clip);
                if (t.Kind == TokenKind.UltraBeast)
                {
                    beasts++;
                    Assert.IsTrue(board[t.Cell].Wild && !board[t.Cell].DistortionWorld, "UB는 본맵 야생 칸");
                }
                else Assert.AreEqual(FigureLayout.DistortionCenter(board, looks), t.Cell, "네크로즈마 피규어 = 깨어진 세계 가운데(M4-2 배치 자리와 같다)");
            }
            Assert.AreEqual(UbState.Beasts, beasts, "UB 9마리");
            // 신오의 심장 판에는 UB 토큰이 없다 — 피규어가 서지 않는다
            var hos = Flow.Started(ctx, 2, 5);
            Assert.IsFalse(UbBoard.Of(hos.Tokens, ctx.Cards.PokemonIds.IndexOf("793:0"), null, null, 0).Shown);
        }

        // 울트라 버스트 4인 판의 첫 라운드를 [턴 종료]로 넘긴 갱신 → 좌석 0의 화면 항목(그리는 단계만)
        List<FxEntry> UbTurn(out GameState s, out List<GameEvent> events)
        {
            s = UbScenes.Started(ctx, UbSeats, UbSeed);
            var planner = new FxPlanner(0, UbSeats);
            planner.From(s);
            var log = new EventLog();
            UbScenes.EndRound(s, ctx, log);
            events = new List<GameEvent>();
            foreach (var e in log.Events)
            {
                var f = Engine.FilterFor(e, 0);
                if (f != null) events.Add(f);
            }
            var entries = planner.Plan(events);
            FxBuilt.KeepSteps(entries, st => FxBuilt.Draws(st, UbSeats));
            return entries;
        }

        static FxEntry UbEntry(List<FxEntry> entries)
        {
            foreach (var e in entries)
                if (e.Kind == ActionKind.UbTurn) return e;
            Assert.Fail("UB 턴 항목이 없다");
            return null;
        }

        [Test]
        public void M6_UB턴_이동과파괴가이벤트마다단계로서고길이는방장과같다()
        {
            var entries = UbTurn(out var s, out var events);
            var ub = UbEntry(entries);
            int moves = 0, hits = 0;
            foreach (var e in events)
            {
                if (e.Seq != ub.Seq) continue;
                if (e is UbMoved) moves++;
                if (e is CellDestroyed) hits++;
            }
            Assert.Greater(moves, 0, "UB가 움직였다");
            Assert.Greater(hits, 0, "네크로즈마의 원격 파괴는 늘 있다");
            int moveSteps = 0, hitSteps = 0, sum = 0, other = 0;
            var walkSteps = new List<FxStep>();
            foreach (var st in ub.Steps)
            {
                if (st.Row == UbRows.Move) { moveSteps++; Assert.AreEqual(1, st.Events.Count); walkSteps.Add(st); }
                else if (st.Row == UbRows.Destroy) { hitSteps++; Assert.AreEqual(1, st.Events.Count); walkSteps.Add(st); }
                else other += st.Ms;   // UB 턴 시작 821 · 빛 모음 840 · 구간 850(M6 「UB 화면 묶음 1」)
                sum += st.Ms;
            }
            Assert.AreEqual(moves, moveSteps, "이동마다 단계 하나");
            Assert.AreEqual(hits, hitSteps, "파괴마다 단계 하나");
            Assert.AreEqual(moves * 700 + hits * 1500 + other, sum, "단계 길이 합(플레이 시험 T24)");
            Assert.AreEqual(System.Math.Max(sum, PlayTiming.PlainMs), ub.QueueMs, "항목 길이 = 방장 PlayTiming");
            // 단계는 이벤트 순서 그대로다: UB가 칸으로 옮긴 단계 → 그 칸의 파괴 단계(엔진 Step → Interact)
            int stepAt = 0;
            foreach (var e in events)
            {
                if (e.Seq != ub.Seq || !(e is UbMoved || e is CellDestroyed)) continue;
                Assert.AreSame(e, walkSteps[stepAt].Events[0], "단계 " + stepAt + "는 이벤트 순서대로");
                stepAt++;
            }
        }

        [Test]
        public void M6_UB턴_피규어는대기열의박자대로칸에서칸으로옮겨간다()
        {
            var entries = UbTurn(out var s, out _);
            var ub = UbEntry(entries);
            FxStep first = null;
            foreach (var st in ub.Steps)
                if (st.Row == UbRows.Move) { first = st; break; }
            Assert.IsNotNull(first);
            var m = UbRows.First<UbMoved>(first);
            var token = s.Tokens.Find(t => t.Kind == TokenKind.UltraBeast && t.Def == m.UbId);
            // 보기는 이미 끝 칸 — 대기열의 이동이 지나기 전에는 첫 이동의 출발 칸
            var before = UbBoard.Of(s.Tokens, m.UbId, entries, ub, first.StartMs - 1);
            Assert.AreEqual(m.From, before.From);
            Assert.AreEqual(m.From, before.To);
            Assert.AreEqual(BeastClip.Idle, before.Clip);
            var queuedOnly = UbBoard.Of(s.Tokens, m.UbId, entries, null, 0);
            Assert.AreEqual(m.From, queuedOnly.From, "아직 재생하지 않은 항목도 출발 칸");
            var mid = UbBoard.Of(s.Tokens, m.UbId, entries, ub, first.StartMs + first.Ms / 2);
            Assert.AreEqual(m.From, mid.From);
            Assert.AreEqual(m.To, mid.To);
            Assert.Greater(mid.T, 0f);
            Assert.Less(mid.T, 1f);
            Assert.Greater(mid.Hop, 0f, "칸 사이를 뛴다");
            Assert.AreEqual(BeastClip.Walk, mid.Clip);
            // 항목이 다 지나면 보기의 칸
            var after = UbBoard.Of(s.Tokens, m.UbId, entries, ub, ub.LengthMs);
            Assert.AreEqual(token.Cell, after.From);
            Assert.AreEqual(token.Cell, after.To);
            var done = UbBoard.Of(s.Tokens, m.UbId, new List<FxEntry>(), null, 0);
            Assert.AreEqual(token.Cell, done.From, "대기열이 비면 보기 그대로");
            // 그 UB의 이동을 차례로 따라가면 끝 칸에 닿는다(이동마다 앞 이동의 도착 칸에서 출발)
            int at = m.From;
            foreach (var st in ub.Steps)
            {
                if (!(UbRows.First<UbMoved>(st) is UbMoved step) || step.UbId != m.UbId) continue;
                Assert.AreEqual(at, UbBoard.Of(s.Tokens, m.UbId, entries, ub, st.StartMs).From, "이동 시작 = 앞 이동의 도착 칸");
                at = step.To;
            }
            Assert.AreEqual(token.Cell, at);
        }

        [Test]
        public void M6_UB턴_파괴된나무는그단계에서어두워지고빠진나무는숨는다()
        {
            var entries = UbTurn(out var s, out _);
            var ub = UbEntry(entries);
            FxStep hit = null;
            foreach (var st in ub.Steps)
                if (st.Row == UbRows.Destroy) { hit = st; break; }
            Assert.IsNotNull(hit);
            var d = UbRows.First<CellDestroyed>(hit);
            var tree = s.Tokens.Find(t => UbBoard.Shaded(t.Kind) && t.Cell == d.Cell && t.Kind == d.TokenKind);
            Assert.IsNotNull(tree);
            Assert.IsTrue(tree.Destroyed);
            Assert.AreEqual(0f, UbBoard.ShadeOf(tree, entries, ub, hit.StartMs - 1).Dark, "파괴 단계 전에는 그대로");
            float half = UbBoard.ShadeOf(tree, entries, ub, hit.StartMs + hit.Ms * 6 / 10).Dark;
            Assert.Greater(half, 0f);
            Assert.Less(half, 1f, "그 단계 동안 어두워진다");
            Assert.AreEqual(1f, UbBoard.ShadeOf(tree, entries, ub, hit.EndMs).Dark, "단계 끝 = 파괴된 모습");
            Assert.AreEqual(1f, UbBoard.ShadeOf(tree, null, null, 0).Dark, "이어 받은 판(대기열 없음)은 파괴된 모습");
            Assert.IsFalse(UbBoard.ShadeOf(tree, null, null, 0).Hidden);
            // 파괴되지 않은 나무 · 맵에서 빠진 나무(kindler B — 칸 −1) · 고정물이 아닌 토큰
            var whole = s.Tokens.Find(t => t.Kind == TokenKind.ApricornTree && !t.Destroyed && t.Cell >= 0);
            Assert.AreEqual(0f, UbBoard.ShadeOf(whole, entries, ub, 0).Dark);
            var gone = new TokenState { Kind = TokenKind.ApricornTree, Def = whole.Def, Cell = -1 };
            Assert.IsTrue(UbBoard.ShadeOf(gone, null, null, 0).Hidden, "kindler B가 없앤 나무는 그리지 않는다");
            Assert.IsFalse(UbBoard.Shaded(TokenKind.HiddenSpot));
            Assert.IsFalse(UbBoard.Shaded(TokenKind.UltraBeast));
            // 밟은 UB는 그 단계 동안 공격 클립, 네크로즈마의 원격 파괴는 네크로즈마의 특수 공격 클립
            int mid = hit.StartMs + hit.Ms / 2;
            if (d.ByUbId >= 0) Assert.AreEqual(BeastClip.PhysicalAttack, UbBoard.Of(s.Tokens, d.ByUbId, entries, ub, mid).Clip);
            else Assert.AreEqual(BeastClip.SpecialAttack, UbBoard.Of(s.Tokens, ctx.Cards.PokemonIds.IndexOf(UltraBurstSetup.NecrozmaKey), entries, ub, mid).Clip);
        }

        [Test]
        public void M6_게이트이동은게이트행하나로서고이동력을쓰지않는다()
        {
            var s = UbScenes.Started(ctx, 2, 5);
            int gate = UbScenes.Fixture(ctx, "gate.deepmarsh_main"), pair = UbScenes.Fixture(ctx, "gate.deepmarsh_distortion");
            s.Seats[s.ActiveSeat].Cell = ctx.Fixtures.Fixtures[gate].Cell;
            var planner = new FxPlanner(s.ActiveSeat, 2);
            planner.From(s);
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Move, ctx.Fixtures.Fixtures[pair].Cell, pair), log);
            var entries = planner.Plan(log.Events);
            FxBuilt.KeepSteps(entries, st => FxBuilt.Draws(st, 2));
            FxStep step = null;
            foreach (var e in entries) step = step ?? GateBeats.StepOf(e);
            Assert.IsNotNull(step, "게이트 행 단계");
            var g = GateBeats.Event(step);
            Assert.AreEqual(ctx.Fixtures.Fixtures[gate].Cell, g.FromCell);
            Assert.AreEqual(ctx.Fixtures.Fixtures[pair].Cell, g.ToCell);
            Assert.AreEqual(1600, step.Ms);
        }

        [Test]
        public void M6_네크로즈마피규어는페이즈폼으로바꿔서고대기열에남은페이즈바뀜은아직지나지않았다()
        {
            var ub = new UbState();
            Assert.AreEqual(UbNecrozma.Phase1, UbBoard.NecrozmaForm(ub, null));
            Assert.AreEqual(UbNecrozma.Phase1, UbBoard.NecrozmaForm(null, null), "울트라 버스트 판이 아니면 1페이즈(서지 않는다)");
            ub.NecrozmaPhase = 2;
            Assert.AreEqual(UbNecrozma.Phase1, UbBoard.NecrozmaForm(ub, null), "2페이즈여도 동전 전에는 1페이즈 모습 [가정 #UB-시작-③]");
            ub.SunMoon = UbNecrozma.Sun;
            Assert.AreEqual(UbNecrozma.DuskMane, UbBoard.NecrozmaForm(ub, null), "썬 = 황혼의 갈기");
            ub.SunMoon = UbNecrozma.Moon;
            Assert.AreEqual(UbNecrozma.DawnWings, UbBoard.NecrozmaForm(ub, null), "문 = 새벽의 날개");
            var coin = new FxEntry();
            coin.Events.Add(new CoinFlip { Result = UbNecrozma.Moon });
            Assert.AreEqual(UbNecrozma.Phase1, UbBoard.NecrozmaForm(ub, new[] { coin }), "동전이 아직 대기열에 있으면 그 전 모습");
            ub.NecrozmaPhase = 3;
            Assert.AreEqual(UbNecrozma.Ultra, UbBoard.NecrozmaForm(ub, null), "3페이즈 = 울트라네크로즈마");
            var phase = new FxEntry();
            phase.Events.Add(new NecrozmaPhase { Phase = 2, Next = 3 });
            Assert.AreEqual(UbNecrozma.DawnWings, UbBoard.NecrozmaForm(ub, new[] { phase }), "페이즈 바뀜이 대기열에 남았으면 전 페이즈(배틀 결과 창 뒤에 바뀐다)");
            Assert.AreEqual(UbNecrozma.Ultra, UbBoard.NecrozmaForm(ub, new List<FxEntry>()));
        }

        [Test]
        public void M6_무거운UB는착지때눌렸다펴고가벼운UB는눌리지않는다()
        {
            Assert.IsTrue(UbMoveBeats.Heavy("797:0"), "철화구야");
            Assert.IsTrue(UbMoveBeats.Heavy("805:0"), "차곡차곡");
            Assert.IsFalse(UbMoveBeats.Heavy("793:0"), "텅비드");
            const int total = 400;
            int land = total * UbMoveBeats.HeavyLandPermille / 1000;
            for (int ms = 0; ms <= total; ms += 10) Assert.AreEqual(1f, UbMoveBeats.At(ms, total).Squash, "가벼운 UB " + ms);
            var landed = UbMoveBeats.At(land, total, true);
            Assert.AreEqual(1f, landed.T, 1e-4f, "무거운 UB는 먼저 내려앉는다");
            Assert.AreEqual(0f, landed.Hop, 1e-4f);
            var mid = UbMoveBeats.At((land + total) / 2, total, true);
            Assert.AreEqual(UbMoveBeats.HeavySquash, mid.Squash, 0.01f, "가운데에서 가장 눌린다");
            Assert.AreEqual(1f, UbMoveBeats.At(total, total, true).Squash, 1e-4f, "단계 끝에는 편다");
            Assert.Greater(UbMoveBeats.At(land / 2, total, true).Hop, 0f, "내려앉기 전에는 뛴다");
        }
    }
}
