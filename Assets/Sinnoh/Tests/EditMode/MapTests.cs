using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>라운드·턴·이동·찾아보기·숨겨진 장소·마을 행동(01 §3-2·§3-5, R1·R2, A5, A23, rulings G11·G12, M3-d).</summary>
    public class MapTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static ChoiceAnswer A(AnswerKind k, int target = -1, int arg = -1) => ChoiceAnswer.Of(k, target, arg);

        static int Other(GameState s) => (s.ActiveSeat + 1) % s.SeatCount;

        /// <summary>차례 좌석을 토큰 칸에 세운다(장면).</summary>
        static SeatState Stand(GameState s, int cell, int moves)
        {
            var st = s.Seats[s.ActiveSeat];
            st.Cell = cell;
            st.MovesLeft = moves;
            return st;
        }

        [Test]
        public void S2_5_턴은시계방향_라운드는첫좌석에서()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 3, 21, log);
            int r = s.RoundStartSeat;
            log.Clear();
            for (int i = 0; i < 3; i++) Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);
            var turns = Flow.Of<TurnStarted>(log);
            CollectionAssert.AreEqual(new[] { (r + 1) % 3, (r + 2) % 3, r }, turns.ConvertAll(t => t.Seat));
            CollectionAssert.AreEqual(new[] { 1, 1, 2 }, turns.ConvertAll(t => t.Round));
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, turns.ConvertAll(t => t.TurnId));
            var rounds = Flow.Of<RoundStarted>(log);
            Assert.AreEqual(1, rounds.Count);
            Assert.AreEqual(2, rounds[0].Round);
            var ended = Flow.Of<TurnEnded>(log);
            CollectionAssert.AreEqual(new[] { r, (r + 1) % 3, (r + 2) % 3 }, ended.ConvertAll(t => t.Seat));
            foreach (var e in ended) Assert.AreEqual(TurnEndReason.EndTurn, e.Reason);
            foreach (var a in Flow.Of<ActionStarted>(log))
                if (a.Kind == ActionKind.TurnStart) Assert.AreEqual(AutoKind.Auto, a.Auto);
            // 차례 이벤트는 거르지 않는다(01 §3-6 ⑩)
            foreach (var t in turns) Assert.AreSame(t, Engine.FilterFor(t, (t.Seat + 1) % 3));
        }

        [Test]
        public void S3_1_이동은최단경로로걷고이동력을씀()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 3, log);
            var st = s.Seats[s.ActiveSeat];
            int start = st.Cell;
            var steps = MoveRules.Reachable(ctx.Board, start, 3, s.Options.ElevationMode);
            int dest = -1;
            for (int c = 0; c < steps.Length && dest < 0; c++)
                if (steps[c] == 2 && MapActions.FaceDownSpotAt(s, c) < 0) dest = c;
            Assert.GreaterOrEqual(dest, 0);
            Assert.IsTrue(ChoiceRules.Contains(Flow.MapLegal(s, ctx), A(AnswerKind.Move, dest)));
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, dest), log);
            var m = Flow.Of<Moved>(log)[0];
            Assert.AreEqual(3, m.Path.Length);
            Assert.AreEqual(start, m.Path[0]);
            Assert.AreEqual(dest, m.Path[2]);
            Assert.AreEqual(ctx.Board[m.Path[1]].LevelQuarters, m.Levels[1]);
            Assert.AreEqual(3, m.AllowanceBefore);
            Assert.AreEqual(1, m.AllowanceAfter);
            Assert.AreEqual(-1, m.StopAt);
            Assert.AreEqual(dest, st.Cell);
            Assert.AreEqual(1, st.MovesLeft);
            Assert.AreEqual(ActionKind.Move, Flow.Of<ActionStarted>(log)[0].Kind);
            // 이동은 나눌 수 있다(G7): 남은 1걸음으로 이웃 칸만
            var near = MoveRules.Reachable(ctx.Board, dest, 1, s.Options.ElevationMode);
            foreach (var a in Flow.MapLegal(s, ctx))
                if (a.Kind == AnswerKind.Move) Assert.AreEqual(1, near[a.Target]);
            Assert.AreEqual(s.ActiveSeat, Flow.Only(s, ChoiceKind.MapAction).Seat, "이동으로는 턴이 끝나지 않는다");
        }

        [Test]
        public void A1_1막해방하면매턴이동력2더()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 5, log);
            int next = Other(s);
            s.Seats[next].ActProgress = 1;
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);
            var m = Flow.Of<MoveAllowanceSet>(log)[0];
            Assert.AreEqual(3, m.Base);
            Assert.AreEqual(5, m.Total);
            Assert.AreEqual(1, m.Mods.Length);
            Assert.AreEqual(SourceKind.Act, m.Mods[0].Source.Kind);
            Assert.AreEqual(ctx.Decks.CardIds.IndexOf("act.journey"), m.Mods[0].Source.Id);
            Assert.AreEqual(2, m.Mods[0].Delta);
            Assert.AreEqual(5, s.Seats[next].MovesLeft);
        }

        [Test]
        public void G11_이동력0이어도찾아보기_끄면못함()
        {
            foreach (bool on in new[] { true, false })
            {
                var s = Flow.Started(ctx, 2, 6, opt: new RuleOptions { InteractAtZeroMovement = on });
                var tree = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak)];
                Stand(s, tree.Cell, 0);
                Assert.AreEqual(on, Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Search), $"옵션 {on}");
                Stand(s, tree.Cell, 1);
                Assert.IsTrue(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Search), "이동력이 남으면 늘 된다");
            }
        }

        /// <summary>뒷면 숨겨진 장소 하나와 거기로 한 걸음에 들어가는 이웃 칸.</summary>
        static void SpotAndNeighbor(GameState s, Ctx ctx, out int token, out int from)
        {
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if (t.Kind != TokenKind.HiddenSpot || t.Cell < 0) continue;
                int n = Flow.StepInto(s, ctx, t.Cell);
                if (n < 0) continue;
                token = i;
                from = n;
                return;
            }
            token = from = -1;
            Assert.Fail("들어갈 수 있는 숨겨진 장소가 없다");
        }

        [Test]
        public void G11_이동력0이어도숨겨진장소공개_끄면안물음()
        {
            foreach (bool on in new[] { true, false })
            {
                var log = new EventLog();
                var s = Flow.Started(ctx, 2, 6, log, new RuleOptions { InteractAtZeroMovement = on });
                SpotAndNeighbor(s, ctx, out int token, out int from);
                int cell = s.Tokens[token].Cell;
                Stand(s, from, 1);
                log.Clear();
                Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, cell), log);
                var m = Flow.Of<Moved>(log)[0];
                Assert.AreEqual(0, m.AllowanceAfter);
                Assert.AreEqual(on ? cell : -1, m.StopAt, $"옵션 {on}");
                Assert.AreEqual(on ? ChoiceKind.HiddenSpot : ChoiceKind.MapAction, s.Pending[0].Kind);
            }
        }

        [Test]
        public void G11_마을행동은이동력과상관없음()
        {
            var s = Flow.Started(ctx, 2, 6, opt: new RuleOptions { InteractAtZeroMovement = false });
            s.Seats[s.ActiveSeat].MovesLeft = 0;
            Assert.IsTrue(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.VillageHeal));
        }

        [Test]
        public void A23_숨겨진장소공개는선택_공개하면앞면()
        {
            foreach (bool reveal in new[] { true, false })
            {
                var log = new EventLog();
                var s = Flow.Started(ctx, 2, 12, log);
                SpotAndNeighbor(s, ctx, out int token, out int from);
                var t = s.Tokens[token];
                t.Def = ctx.Items.Ids.IndexOf(HiddenSpotRules.Campsite); // 공개 뒤에도 그 칸에 남고 판을 바꾸지 않는 야영지 — 결과 5종은 HiddenSpotTests
                Stand(s, from, 3);
                log.Clear();
                Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, t.Cell), log);
                int moveSeq = Flow.Of<Moved>(log)[0].Seq;
                var c = Flow.Only(s, ChoiceKind.HiddenSpot);
                Assert.AreEqual(AnswerKind.Decline, c.Default.Kind, "시간 초과 = 지나가기");
                Assert.AreEqual(Engine.Unknown, Engine.ViewFor(s, 0).Tokens[token].Def, "공개 전에는 아무도 모른다");
                log.Clear();
                Flow.Answer(s, ctx, ChoiceKind.HiddenSpot, A(reveal ? AnswerKind.Accept : AnswerKind.Decline), log);
                Assert.AreEqual(moveSeq, Flow.Of<ActionStarted>(log)[0].Continues, "멈춤 → 공개·지나가기(01 §3-6 ①-2)");
                Assert.AreEqual(reveal, t.FaceUp);
                if (reveal)
                {
                    var e = Flow.Of<HiddenSpotRevealed>(log)[0];
                    Assert.AreEqual(t.Def, e.SpotKey);
                    Assert.AreEqual(t.Cell, e.Cell);
                    Assert.AreEqual(t.Def, Engine.ViewFor(s, 1).Tokens[token].Def, "공개 뒤에는 모두 본다");
                }
                else
                {
                    Assert.AreEqual(t.Cell, Flow.Of<HiddenSpotPassed>(log)[0].Cell);
                    Assert.AreEqual(0, Flow.Of<HiddenSpotRevealed>(log).Count);
                }
                Assert.AreEqual(s.ActiveSeat, Flow.Only(s, ChoiceKind.MapAction).Seat, "공개·지나가기로는 턴이 끝나지 않는다(p.27)");
            }
        }

        [Test]
        public void R1_나무는지형기후가둘다맞는카드만()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 14, log);
            var tree = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak)];
            var cell = ctx.Board[tree.Cell];
            Stand(s, tree.Cell, 3);
            int before = s.Decks[(int)DeckId.Weak].Count;
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Search), log);
            var searched = Flow.Of<Searched>(log)[0];
            Assert.AreEqual(cell.Biome, searched.Biome);
            Assert.AreEqual(cell.Climate, searched.Climate);
            var drawn = Flow.Of<EncounterDrawn>(log)[0];
            Assert.AreEqual(3, drawn.Cards.Length, "약한 나무 3장(p.4)");
            foreach (var g in drawn.Cards)
            {
                var p = ctx.Cards.Pokemon[g.CardKey];
                Assert.AreEqual(cell.Biome, p.Biome);
                Assert.AreEqual(cell.Climate, p.Climate);
                Assert.AreEqual(EncounterTier.Weak, g.Grade);
            }
            Assert.AreEqual(before - 3, s.Decks[(int)DeckId.Weak].Count, "뽑은 카드는 덱 밖(고르기 줄)");
            var pick = Flow.Only(s, ChoiceKind.EncounterPick);
            Assert.AreEqual(3, Scene.Legal(s, ctx, pick).FindAll(a => a.Kind == AnswerKind.Pick).Count, "반드시 1장(S5_1) — 안 고르기 없음(나머지 답은 고르기 전 제작, 룰북 p.2)");
            Assert.IsFalse(Scene.Legal(s, ctx, pick).Exists(a => a.Kind == AnswerKind.Decline));
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.EncounterPick, ChoiceAnswer.Pick(pick.Options[1], 1), log);
            var picked = Flow.Of<EncounterPicked>(log)[0];
            Assert.AreEqual(drawn.Cards[1].CardKey, picked.CardKey);
            Assert.AreEqual(1, picked.Position);
            var ret = Flow.Of<CardsReturned>(log)[0];
            Assert.AreEqual(1, ret.Returns.Length);
            Assert.AreEqual(DeckId.Weak, ret.Returns[0].Pile);
            Assert.AreEqual(2, ret.Returns[0].Count);
        }

        [Test]
        public void R1_맞는카드가모자라면나온만큼()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 14, log);
            var tree = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak)];
            var cell = ctx.Board[tree.Cell];
            Flow.Strip(s, ctx, DeckId.Weak, cell.Biome, cell.Climate, 1);
            Stand(s, tree.Cell, 3);
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Search), log);
            Assert.AreEqual(1, Flow.Of<EncounterDrawn>(log)[0].Cards.Length);
            Assert.AreEqual(1, Flow.Only(s, ChoiceKind.EncounterPick).Options.Count);
        }

        [Test]
        public void R2_왜곡된나무는강한적당한약한순서로아무카드()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 15, log);
            var warped = s.Tokens.Find(t => t.Kind == TokenKind.DistortedTree);
            Stand(s, warped.Cell, 3);
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Search), log);
            var drawn = Flow.Of<EncounterDrawn>(log)[0];
            CollectionAssert.AreEqual(new[] { EncounterTier.Strong, EncounterTier.Moderate, EncounterTier.Weak }, System.Array.ConvertAll(drawn.Cards, g => g.Grade));
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.EncounterPick, ChoiceAnswer.Pick(drawn.Cards[0].CardKey, 0), log);
            var ret = Flow.Of<CardsReturned>(log)[0];
            CollectionAssert.AreEqual(new[] { DeckId.Weak, DeckId.Moderate }, System.Array.ConvertAll(ret.Returns, r => r.Pile), "남은 카드는 제 덱으로(덱 순서로 알림)");
        }

        [Test]
        public void G12_찾아보기빈손이면규토리줍기()
        {
            foreach (var kind in new[] { TokenKind.ApricornTree, TokenKind.ApricornReef })
            {
                var log = new EventLog();
                var s = Flow.Started(ctx, 2, 16, log);
                var tree = s.Tokens[Flow.Tree(s, ctx, kind, EncounterTier.Weak)];
                var cell = ctx.Board[tree.Cell];
                var st = Stand(s, tree.Cell, 3);
                Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.TakeApricorns), "찾아보기 전에는 줍지 않는다(p.27 FAQ)");
                Flow.Strip(s, ctx, DeckId.Weak, cell.Biome, cell.Climate, 0);
                log.Clear();
                Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Search), log);
                Assert.AreEqual(0, Flow.Of<EncounterDrawn>(log)[0].Cards.Length, "조우 없음도 낸다(01 §3-6 ⑥)");
                var legal = Flow.MapLegal(s, ctx);
                Assert.IsFalse(Flow.Has(legal, AnswerKind.Search), "같은 턴 같은 칸 다시 찾아보기 없음");
                Assert.IsTrue(Flow.Has(legal, AnswerKind.TakeApricorns));
                var before = (int[])st.Apricorns.Clone();
                int seat = s.ActiveSeat;
                log.Clear();
                Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.TakeApricorns), log);
                var taken = Flow.Of<ApricornsTaken>(log);
                var fixture = ctx.Fixtures.Fixtures[tree.Def];
                int total = 0;
                foreach (var e in taken)
                {
                    Assert.AreEqual(tree.Cell, e.Cell);
                    Assert.AreEqual(before[e.Color], e.Before);
                    Assert.AreEqual(before[e.Color] + e.Count, e.After);
                    Assert.AreEqual(e.After, s.Seats[seat].Apricorns[e.Color]);
                    Assert.AreEqual(MapActions.ApricornGrade((ApricornColor)e.Color), e.Grade);
                    total += e.Count;
                }
                Assert.AreEqual(3, total, "그 토큰의 규토리 3개(A8)");
                Assert.AreEqual(kind == TokenKind.ApricornTree ? 1 : 3, taken.Count, "나무는 한 색 3개, 약한 산호는 빨강·파랑·노랑");
                Assert.AreEqual((int)fixture.Apricorns[0], taken[0].Color);
                Assert.AreEqual(TurnEndReason.Apricorns, Flow.Of<TurnEnded>(log)[0].Reason, "턴이 끝난다");
                Assert.AreNotEqual(seat, s.ActiveSeat);
            }
        }

        [Test]
        public void A5_공중날기는아무규토리1개_조무래기마을은막힘()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 17, log);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Fly), "고향에 조무래기가 있으면 못 난다(서막)");
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.Grunt && t.Def == st.HomeVillage) t.Cell = -1; // 1막 해방을 흉내
            var fly = Flow.MapLegal(s, ctx).FindAll(a => a.Kind == AnswerKind.Fly);
            var dests = new HashSet<int>();
            foreach (var a in fly)
            {
                Assert.AreEqual((int)ApricornColor.Red, a.Arg, "가진 색만(빨강 3)");
                dests.Add(a.Target);
                var f = ctx.Fixtures.Fixtures[a.Target];
                Assert.AreEqual(FixtureKind.Village, f.Kind);
                Assert.IsFalse(ctx.Board[f.Cell].DistortionWorld, "울트라메가로폴리스로는 못 난다");
                Assert.IsFalse(SetupFlow.GruntAt(s, f.Cell), "조무래기가 있는 마을로는 못 난다");
            }
            Assert.AreEqual(8 - 2, dests.Count, "본맵 마을 8 − 내 고향 − 남의 고향(조무래기)");
            int dest = fly[0].Target;
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, fly[0], log);
            Assert.AreEqual(2, st.Apricorns[(int)ApricornColor.Red]);
            Assert.AreEqual(ctx.Fixtures.Fixtures[dest].Cell, st.Cell);
            Assert.AreEqual(3, st.MovesLeft, "공중날기는 이동력을 쓰지 않는다(G12)");
            var spent = Flow.Of<ApricornSpent>(log)[0];
            Assert.AreEqual(SourceKind.Rule, spent.Source.Kind);
            Assert.AreEqual((int)RuleSource.Fly, spent.Source.Id);
            Assert.AreEqual(3, spent.Changes[0].Before);
            Assert.AreEqual(2, spent.Changes[0].After);
            var flew = Flow.Of<Flew>(log)[0];
            Assert.AreEqual(ctx.Fixtures.Fixtures[st.HomeVillage].Cell, flew.From);
            Assert.AreEqual(st.Cell, flew.To);
            Assert.AreEqual(UsageKind.Fly, Flow.Of<UsageMarked>(log)[0].Kind);
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Fly), "턴당 1번");
        }

        [Test]
        public void G12_마을행동은통틀어턴당1번()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 18, log);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            var mon = s.Pokemon[st.Party[0]];
            mon.Damage = 2;
            mon.Status = StatusEffect.Burned;
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.VillageHeal), log);
            var healed = Flow.Of<Healed>(log)[0];
            Assert.AreEqual((int)RuleSource.VillageHeal, healed.Source.Id);
            Assert.AreEqual(1, healed.Pokemon.Length);
            Assert.AreEqual(PokemonRules.MaxHp(ctx, mon) - 2, healed.Pokemon[0].HpBefore);
            Assert.AreEqual(PokemonRules.MaxHp(ctx, mon), healed.Pokemon[0].HpAfter);
            Assert.AreEqual(StatusEffect.Burned, healed.Pokemon[0].StatusBefore);
            Assert.AreEqual(0, mon.Damage, "피해와 상태이상을 모두 없앤다(S3_3)");
            Assert.AreEqual(StatusEffect.None, mon.Status);
            Assert.AreEqual(UsageKind.VillageHeal, Flow.Of<UsageMarked>(log)[0].Kind);
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.VillageHeal), "턴당 1번");

            // 앞면 야영지도 같은 행동으로 센다
            int camp = Flow.StepInto(s, ctx, st.Cell);
            var token = s.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && ctx.Items.Items[t.Def].Key == "hidden_spot.campsite");
            token.Cell = camp;
            token.FaceUp = true;
            Stand(s, camp, 3);
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.VillageHeal), "마을에서 회복했으면 야영지에서도 안 된다");

            // 다음 내 턴에는 다시(야영지 = CampHeal)
            for (int i = 0; i < s.SeatCount; i++) Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn));
            Assert.AreEqual(seat, s.ActiveSeat);
            Assert.AreEqual(camp, st.Cell);
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.VillageHeal), log);
            Assert.AreEqual((int)RuleSource.CampHeal, Flow.Of<Healed>(log)[0].Source.Id);
        }

        [Test]
        public void S7_9_전멸하면고향으로_다음내턴시작에회복()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 19, log);
            int b = Other(s);
            var st = s.Seats[b];
            int away = Flow.StepInto(s, ctx, st.Cell);
            st.Cell = away;
            var mon = s.Pokemon[st.Party[0]];
            mon.Damage = PokemonRules.MaxHp(ctx, mon);
            log.Clear();
            TurnFlow.Whiteout(s, ctx, Scene.Fx(s, log, b, ActionKind.BattleAction), b);
            var w = Flow.Of<Whiteout>(log)[0];
            Assert.AreEqual(away, w.FromCell);
            Assert.AreEqual(ctx.Fixtures.Fixtures[st.HomeVillage].Cell, w.ToCell);
            Assert.AreEqual(w.ToCell, st.Cell, "즉시 고향 마을로");
            Assert.IsTrue(st.RespawnPending);
            Assert.AreEqual(b, Flow.Of<RespawnPending>(log)[0].Seat);

            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);
            var healed = Flow.Of<Healed>(log)[0];
            Assert.AreEqual(b, healed.Seat);
            Assert.AreEqual((int)RuleSource.Respawn, healed.Source.Id);
            Assert.AreEqual(0, healed.Pokemon[0].HpBefore);
            Assert.AreEqual(0, mon.Damage);
            Assert.IsFalse(st.RespawnPending);
            Assert.AreEqual(b, Flow.Of<RespawnCleared>(log)[0].Seat);
        }

        [Test]
        public void S7_9_도전보호는다음내턴시작에풀림()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 3, 20, log);
            int b = Other(s);
            s.Seats[b].Protected = true;
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);
            Assert.IsFalse(s.Seats[b].Protected);
            Assert.AreEqual(b, Flow.Of<ProtectionCleared>(log)[0].Seat);
            // 턴 시작 항목 안 단계(04 9행): TurnStarted 뒤, MoveAllowanceSet 앞
            int start = log.Events.FindIndex(e => e is TurnStarted);
            int cleared = log.Events.FindIndex(e => e is ProtectionCleared);
            int allowance = log.Events.FindIndex(e => e is MoveAllowanceSet);
            Assert.Less(start, cleared);
            Assert.Less(cleared, allowance);
            Assert.AreEqual(log.Events[start].Seq, log.Events[cleared].Seq);
        }

        [Test]
        public void G7_지나간칸의트레이너와는도전거래없음()
        {
            // 2걸음 이동의 가운데 칸에 상대를 세운다: 그 칸에 멈추면 도전할 수 있지만, 지나가 더 먼 칸에 멈추면 도전·거래가 없다(p.3 「same tile」, rulings G7)
            var s = Flow.Started(ctx, 2, 3);
            int me = s.ActiveSeat, other = Other(s);
            var steps = MoveRules.Reachable(ctx.Board, s.Seats[me].Cell, 3, s.Options.ElevationMode);
            int dest = -1, mid = -1;
            for (int c = 0; c < steps.Length && dest < 0; c++)
            {
                if (steps[c] != 2 || MapActions.FaceDownSpotAt(s, c) >= 0) continue;
                var probe = s.Clone();
                var plog = new EventLog();
                Flow.Answer(probe, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, c), plog);
                int m = Flow.Of<Moved>(plog)[0].Path[1];
                if (MapActions.FaceDownSpotAt(s, m) >= 0) continue;
                dest = c;
                mid = m;
            }
            Assert.GreaterOrEqual(dest, 0, "가운데 칸에 뒷면 숨겨진 장소가 없는 2걸음 이동");
            s.Seats[other].Cell = mid;

            var stop = s.Clone();
            Flow.Answer(stop, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, mid), null);
            Assert.IsTrue(Flow.MapLegal(stop, ctx).Exists(a => a.Kind == AnswerKind.Challenge && a.Target == other), "같은 칸에 멈추면 도전할 수 있다");

            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, dest), log);
            CollectionAssert.Contains(Flow.Of<Moved>(log)[0].Path, mid, "상대가 선 칸을 지나갔다");
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.Challenge && a.Target == other), "지나간 칸의 트레이너와는 도전 없음");
            Assert.IsFalse(Flow.Has(legal, AnswerKind.ProposeTrade), "거래도 없음(거래 자체는 M5)");
        }

        /// <summary>
        /// 결정 71 장면: 뒷면 숨겨진 장소 칸 spot을 가운데에 둔 두 걸음 길 from → spot → beyond(beyond에는 뒷면 숨겨진 장소가 없다, 높이 A6).
        /// </summary>
        static void SpotOnTheWay(GameState s, Ctx ctx, out int token, out int from, out int beyond)
        {
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if (t.Kind != TokenKind.HiddenSpot || t.Cell < 0 || t.FaceUp) continue;
                int n = Flow.StepInto(s, ctx, t.Cell);
                if (n < 0) continue;
                for (int d = 0; d < Board.Directions; d++)
                {
                    int b = ctx.Board.Neighbor(t.Cell, d);
                    if (b == Board.NoCell || b == n || MapActions.FaceDownSpotAt(s, b) >= 0 || !MoveRules.ElevationAllows(ctx.Board[t.Cell], ctx.Board[b], s.Options.ElevationMode)) continue;
                    token = i;
                    from = n;
                    beyond = b;
                    return;
                }
            }
            token = from = beyond = -1;
            Assert.Fail("가운데에 숨겨진 장소를 둔 두 걸음 길이 없다");
        }

        [Test]
        public void G12_결정71_F5_고른길위숨겨진장소에서멈추고묻는다_지나가면남은길을이어걷는다()
        {
            // 원작 커뮤니티 확인 — 지나가는 칸마다 묻는다(결정 71 ③)
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 12, log);
            SpotOnTheWay(s, ctx, out int token, out int from, out int beyond);
            int spot = s.Tokens[token].Cell;
            var st = Stand(s, from, 3);
            var go = ChoiceAnswer.MoveAlong(new List<int> { from, spot, beyond });
            Assert.AreEqual(beyond, go.Target);
            CollectionAssert.AreEqual(new[] { spot, beyond }, go.Values, "답에는 출발 칸을 빼고 싣는다");
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, go, log);
            var first = Flow.Of<Moved>(log);
            Assert.AreEqual(1, first.Count);
            CollectionAssert.AreEqual(new[] { from, spot }, first[0].Path, "멈춘 칸까지만 걷는다");
            Assert.AreEqual(spot, first[0].StopAt);
            Assert.AreEqual(3, first[0].AllowanceBefore);
            Assert.AreEqual(2, first[0].AllowanceAfter);
            Assert.AreEqual(spot, st.Cell);
            Assert.AreEqual(2, st.MovesLeft);
            var ask = Flow.Only(s, ChoiceKind.HiddenSpot);
            Assert.AreEqual(token, ask.Subject);
            CollectionAssert.AreEqual(new[] { beyond }, ask.Options, "남은 길을 물음에 싣는다");
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind, "시간 초과 = 지나가기(남은 길을 이어 걷는다)");
            int moveSeq = first[0].Seq;

            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.HiddenSpot, A(AnswerKind.Decline), log);
            Assert.AreEqual(moveSeq, Flow.Of<ActionStarted>(log)[0].Continues);
            Assert.AreEqual(spot, Flow.Of<HiddenSpotPassed>(log)[0].Cell);
            var rest = Flow.Of<Moved>(log);
            Assert.AreEqual(1, rest.Count, "지나가기 뒤 남은 길");
            CollectionAssert.AreEqual(new[] { spot, beyond }, rest[0].Path);
            Assert.AreEqual(2, rest[0].AllowanceBefore);
            Assert.AreEqual(1, rest[0].AllowanceAfter);
            Assert.AreEqual(-1, rest[0].StopAt);
            Assert.Less(log.Events.IndexOf(Flow.Of<HiddenSpotPassed>(log)[0]), log.Events.IndexOf(rest[0]), "지나가기 → 남은 길 순서");
            Assert.IsFalse(s.Tokens[token].FaceUp);
            Assert.AreEqual(beyond, st.Cell);
            Assert.AreEqual(1, st.MovesLeft);
            Assert.AreEqual(s.ActiveSeat, Flow.Only(s, ChoiceKind.MapAction).Seat, "이동을 나눠 쓴다(G7) — 남은 이동력 1");
        }

        [Test]
        public void G12_결정71_고른길에서공개하면남은길은버리고그칸에선다_남은이동력은그대로()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 12, log);
            SpotOnTheWay(s, ctx, out int token, out int from, out int beyond);
            int spot = s.Tokens[token].Cell;
            var st = Stand(s, from, 3);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(new List<int> { from, spot, beyond }), log);
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.HiddenSpot, A(AnswerKind.Accept), log);
            Assert.IsTrue(s.Tokens[token].FaceUp);
            Assert.AreEqual(1, Flow.Of<HiddenSpotRevealed>(log).Count);
            Assert.AreEqual(0, Flow.Of<Moved>(log).Count, "공개하면 남은 길은 버린다 [가정]");
            Assert.AreEqual(spot, st.Cell);
            Assert.AreEqual(2, st.MovesLeft, "남은 이동력은 쓰지 않았다");
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsTrue(ChoiceRules.Contains(legal, A(AnswerKind.Move, beyond)), "남은 이동력으로 다시 고를 수 있다");
        }

        [Test]
        public void G12_결정71_고른길은최단이아니어도이동력안이면합법_끊긴길넘친길은거절()
        {
            var s = Flow.Started(ctx, 2, 12);
            SpotOnTheWay(s, ctx, out int token, out int from, out int beyond);
            int spot = s.Tokens[token].Cell;
            Stand(s, from, 3);
            var map = Flow.Only(s, ChoiceKind.MapAction);
            bool Legal(params int[] steps)
            {
                var a = A(AnswerKind.Move, steps.Length > 0 ? steps[steps.Length - 1] : -1);
                a.Values.AddRange(steps);
                return ChoiceRules.IsLegal(s, ctx, map, a);
            }
            Assert.IsTrue(Legal(spot, beyond), "최단 길");
            Assert.IsTrue(Legal(spot, from, spot), "되돌아오는 길(3걸음 = 이동력 3) — 최단이 아니어도 된다");
            Assert.IsFalse(Legal(spot, from, spot, beyond), "4걸음은 이동력 3을 넘는다");
            Assert.IsFalse(Legal(beyond), "이웃이 아닌 칸으로 뛰지 않는다(한 칸씩 이어진 길만)");
            Assert.IsFalse(Legal(spot, from), "출발 칸으로 돌아오는 길은 이동이 아니다(도착 칸이 Legal 밖)");
            var wrong = A(AnswerKind.Move, beyond);
            wrong.Values.Add(spot);
            Assert.IsFalse(ChoiceRules.IsLegal(s, ctx, map, wrong), "마지막 칸 ≠ 도착 칸");
            var arg = ChoiceAnswer.MoveAlong(new List<int> { from, spot });
            arg.Arg = 0;
            Assert.IsFalse(ChoiceRules.IsLegal(s, ctx, map, arg), "Arg는 비운다");
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Values.Count > 0), "고른 길은 Legal에 늘어놓지 않는다(옛 꼴 목록 그대로)");
            Assert.IsFalse(Engine.Submit(s, ctx, map.Seat, Command.AnswerTo(map.Id, ChoiceAnswer.MoveAlong(new List<int> { from, spot, from, spot, beyond })), null).Accepted, "엔진도 거절한다");
        }

        [Test]
        public void G12_결정71_도착칸만주는옛명령은지나는칸을묻지않는다_옛저장호환()
        {
            // 옛 꼴 Move(도착 칸)는 결정 71 전 G12 그대로(최단 경로 · 도착 칸에서만 묻기) — 옛 로그를 다시 적용해도 같은 판이 된다. 같은 길을 고른 길로 내면 가운데에서 멈춘다
            var s = Flow.Started(ctx, 2, 12);
            int me = s.ActiveSeat;
            List<int> path = null;
            for (int i = 0; i < s.Tokens.Count && path == null; i++)
            {
                var t = s.Tokens[i];
                if (t.Kind != TokenKind.HiddenSpot || t.Cell < 0 || t.FaceUp) continue;
                int from = Flow.StepInto(s, ctx, t.Cell);
                if (from < 0) continue;
                var steps = MoveRules.Reachable(ctx.Board, from, 2, s.Options.ElevationMode);
                for (int d = 0; d < steps.Length && path == null; d++)
                {
                    if (steps[d] != 2 || MapActions.FaceDownSpotAt(s, d) >= 0) continue;
                    var p = MoveRules.Path(ctx.Board, from, d, 2, s.Options.ElevationMode);
                    if (p[1] == t.Cell) path = p;
                }
            }
            Assert.IsNotNull(path, "가운데 칸에 숨겨진 장소가 있는 최단 경로");
            var old = s.Clone();
            Stand(old, path[0], 3);
            var log = new EventLog();
            Flow.Answer(old, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, path[2]), log);
            CollectionAssert.AreEqual(path, Flow.Of<Moved>(log)[0].Path);
            Assert.AreEqual(-1, Flow.Of<Moved>(log)[0].StopAt);
            Assert.AreEqual(path[2], old.Seats[me].Cell);
            Flow.Only(old, ChoiceKind.MapAction);

            Stand(s, path[0], 3);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(path), null);
            Assert.AreEqual(path[1], s.Seats[me].Cell, "고른 길은 가운데 숨겨진 장소에서 멈춘다");
            Flow.Only(s, ChoiceKind.HiddenSpot);
        }

        [Test]
        public void G11_결정71_고른길의가운데칸은늘묻고_도착칸은이동력0이면옵션대로()
        {
            foreach (bool on in new[] { true, false })
            {
                var s = Flow.Started(ctx, 2, 12, opt: new RuleOptions { InteractAtZeroMovement = on });
                SpotOnTheWay(s, ctx, out int token, out int from, out int beyond);
                int spot = s.Tokens[token].Cell;
                var path = new List<int> { from, spot, beyond };
                Stand(s, from, 2);
                CollectionAssert.AreEqual(new[] { spot }, MapActions.StopsOn(s, ctx, s.ActiveSeat, path.GetRange(1, 2)), $"옵션 {on}: 가운데 칸은 이동력 1이 남는다");
                Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(path), null);
                CollectionAssert.AreEqual(new[] { beyond }, Flow.Only(s, ChoiceKind.HiddenSpot).Options);
                Assert.AreEqual(spot, s.Seats[s.ActiveSeat].Cell);

                var t = Flow.Started(ctx, 2, 12, opt: new RuleOptions { InteractAtZeroMovement = on });
                Stand(t, from, 1);
                var one = new List<int> { from, spot };
                Assert.AreEqual(on ? 1 : 0, MapActions.StopsOn(t, ctx, t.ActiveSeat, one.GetRange(1, 1)).Count, $"옵션 {on}: 도착 칸에서 이동력 0");
                Flow.Answer(t, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(one), null);
                Assert.AreEqual(on ? ChoiceKind.HiddenSpot : ChoiceKind.MapAction, t.Pending[0].Kind, $"옵션 {on}");
            }
        }

        [Test]
        public void M3_찾아보기뒤조우고르기는Continues()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 22, log);
            var tree = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Moderate)];
            Stand(s, tree.Cell, 3);
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Search), log);
            var search = Flow.Of<ActionStarted>(log)[0];
            Assert.AreEqual(ActionKind.Search, search.Kind);
            var pick = Flow.Only(s, ChoiceKind.EncounterPick);
            Assert.AreEqual(search.Seq, pick.FromSeq);
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.EncounterPick, pick.Default, log);
            var a = Flow.Of<ActionStarted>(log)[0];
            Assert.AreEqual(ActionKind.EncounterPick, a.Kind);
            Assert.AreEqual(search.Seq, a.Continues, "찾아보기 → 조우 고르기(01 §3-6 ①-2)");
            Assert.Greater(a.Seq, search.Seq);
        }
    }
}
