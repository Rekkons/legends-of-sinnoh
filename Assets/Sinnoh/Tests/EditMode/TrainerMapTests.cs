using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「맵 상호작용」(rulings A37 — 능력 7): gardener B · kindler B · psychic B · ruin_maniac B · scientist B · scuba_diver B · veteran B.
    /// </summary>
    public class TrainerMapTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static ChoiceAnswer A(AnswerKind k, int target = -1, int arg = -1) => ChoiceAnswer.Of(k, target, arg);

        void Card(GameState s, int seat, string key, int tier = TrainerRules.Silver)
        {
            s.Seats[seat].TrainerCard = ctx.Cards.TrainerCardIds.IndexOf(key);
            s.Seats[seat].TrainerCardTier = tier;
            TrainerRules.Refresh(s, ctx);
        }

        /// <summary>시작한 판의 차례 좌석에 카드를 준다. 숨겨진 장소는 모두 맵에서 치운다(시험이 필요한 것만 다시 놓는다).</summary>
        GameState Table(string card, out int seat, EventLog log, int tier = TrainerRules.Silver)
        {
            var s = Flow.Started(ctx, 2, 5, log);
            seat = s.ActiveSeat;
            if (card != null) Card(s, seat, card, tier);
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.HiddenSpot) t.Cell = -1;
            return s;
        }

        static void Stand(GameState s, int seat, int cell, int moves)
        {
            s.Seats[seat].Cell = cell;
            s.Seats[seat].MovesLeft = moves;
        }

        /// <summary>고정물·토큰이 없는 칸(깨어진 세계 밖).</summary>
        int EmptyCell(GameState s, System.Func<int, bool> ok)
        {
            for (int c = 0; c < ctx.Board.Count; c++)
            {
                if (ctx.Board[c].DistortionWorld || !ctx.Board[c].Wild || !ok(c)) continue;
                bool used = false;
                foreach (var f in ctx.Fixtures.Fixtures)
                    if (f.Cell == c) used = true;
                foreach (var t in s.Tokens)
                    if (t.Cell == c) used = true;
                if (!used) return c;
            }
            Assert.Fail("그런 칸이 없다");
            return -1;
        }

        /// <summary>숨겨진 장소 토큰 하나를 그 정체로 그 칸에 놓는다(뒷면). 토큰 색인.</summary>
        int Spot(GameState s, string key, int cell, bool faceUp = false)
        {
            int def = ctx.Items.Ids.IndexOf(key);
            Assert.GreaterOrEqual(def, 0, key);
            int i = s.Tokens.FindIndex(t => t.Kind == TokenKind.HiddenSpot && t.Def == def);
            Assert.GreaterOrEqual(i, 0, key);
            s.Tokens[i].Cell = cell;
            s.Tokens[i].FaceUp = faceUp;
            s.Tokens[i].Destroyed = false;
            return i;
        }

        bool HasFixture(int cell)
        {
            foreach (var f in ctx.Fixtures.Fixtures)
                if (f.Cell == cell) return true;
            return false;
        }

        static bool UsesAbility(List<ChoiceAnswer> legal) => legal.Exists(a => a.Kind == AnswerKind.UseAbility);

        // ── gardener B ──

        [Test]
        public void A20_6_gardener_파랑_선나무에서규토리1_턴당한번()
        {
            // 「Once per turn, you may gain 1 Apricorn from an Apricorn Tree you are standing on.」
            var log = new EventLog();
            var s = Table("gardener", out int seat, log);
            var tree = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak)];
            int color = (int)ctx.Fixtures.Fixtures[tree.Def].Apricorns[0];
            Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)), "나무가 아닌 칸");
            Stand(s, seat, tree.Cell, 0);
            var picks = Flow.MapLegal(s, ctx).FindAll(a => a.Kind == AnswerKind.UseAbility);
            Assert.AreEqual(1, picks.Count, "한 색 나무");
            Assert.AreEqual(color, picks[0].Arg);
            int before = s.Seats[seat].Apricorns[color];
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, picks[0], log);
            Assert.AreEqual(before + 1, s.Seats[seat].Apricorns[color]);
            Assert.AreEqual(1, Flow.Of<AbilityUsed>(log).Count);
            Assert.AreEqual(seat, s.ActiveSeat, "턴은 이어진다");
            Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)), "턴당 한 번");
            // 다음 내 턴에 다시
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);
            Assert.AreEqual(seat, s.ActiveSeat);
            Assert.IsTrue(UsesAbility(Flow.MapLegal(s, ctx)));
            // 산호는 나무가 아니다 · 브론즈에는 없다
            Stand(s, seat, s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornReef, EncounterTier.Weak)].Cell, 0);
            Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)));
            Stand(s, seat, tree.Cell, 0);
            Card(s, seat, "gardener", TrainerRules.Bronze);
            Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)));
        }

        // ── kindler B ──

        [Test]
        public void A20_6_kindler_파랑_맵의나무하나를없애고장작3()
        {
            // 「Once per turn, you may remove any Apricorn Tree from the map to add 3 Burned status effects to your bag as Firewood.」
            var log = new EventLog();
            var s = Table("kindler", out int seat, log);
            int trees = s.Tokens.FindAll(t => t.Kind == TokenKind.ApricornTree && t.Cell >= 0).Count;
            var burns = Flow.MapLegal(s, ctx).FindAll(a => a.Kind == AnswerKind.UseAbility);
            Assert.AreEqual(trees, burns.Count, "맵의 아무 나무(서 있는 칸이 아니어도)");
            foreach (var a in burns) Assert.AreEqual(TokenKind.ApricornTree, s.Tokens[a.Target].Kind, "산호·왜곡된 나무는 아니다");
            var tree = s.Tokens[burns[burns.Count - 1].Target];
            int cell = tree.Cell;
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, burns[burns.Count - 1], log);
            Assert.AreEqual(-1, tree.Cell);
            Assert.IsFalse(tree.Destroyed, "UB가 파괴한 것으로 세지 않는다");
            Assert.AreEqual(-1, MapActions.EncounterTokenAt(s, cell), "그 칸에서 더 찾아볼 수 없다");
            Assert.AreEqual(TrainerRules.FirewoodPerTree, s.Seats[seat].Firewood);
            var removed = Flow.Of<TokenRemoved>(log);
            Assert.AreEqual(1, removed.Count);
            Assert.AreEqual(cell, removed[0].Cell);
            Assert.AreEqual(TokenKind.ApricornTree, removed[0].TokenKind);
            Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)), "턴당 한 번");
        }

        // ── psychic B ──

        [Test]
        public void A20_6_psychic_파랑_두칸떨어진나무를찾아본다_말은그대로()
        {
            // 「Once per turn, you may interact with something from up to 2 tiles away.」 — 원작 커뮤니티 확인
            var log = new EventLog();
            var s = Table(null, out int seat, log);
            int tree = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak)].Cell;
            var around = TrainerRules.ReachCells(ctx, tree);
            Assert.Greater(around.Count, 6, "두 칸 안");
            int home = around.Find(c => !MoveRules.Adjacent(ctx.Board, c, tree));
            Stand(s, seat, home, 3);
            var search = TrainerRules.ReachAnswer(tree, A(AnswerKind.Search));
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.SameAs(search)), "카드가 없으면 닿지 않는다");
            Card(s, seat, "psychic");
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsTrue(legal.Exists(a => a.SameAs(search)));
            foreach (var a in legal)
                if (a.Kind == AnswerKind.UseAbility) CollectionAssert.Contains(TrainerRules.ReachCells(ctx, home), a.Target, "두 칸 안의 칸만");
            Assert.AreEqual(home, s.Seats[seat].Cell, "합법 답을 세어도 말은 그대로");
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, search, log);
            Assert.AreEqual(home, s.Seats[seat].Cell, "말은 움직이지 않는다");
            Assert.AreEqual(-1, s.Seats[seat].ReachFrom);
            Assert.AreEqual(tree, Flow.Of<Searched>(log)[0].Cell);
            Assert.AreEqual(1, Flow.Of<AbilityUsed>(log).Count);
            Assert.AreEqual(1, s.Pending.Count);
            if (s.Pending[0].Kind == ChoiceKind.EncounterPick)
            {
                Assert.AreEqual(tree, s.Pending[0].Subject, "배틀 칸 = 그 나무의 칸");
                Flow.Answer(s, ctx, ChoiceKind.EncounterPick, s.Pending[0].Default.Clone(), log);
                Assert.IsNotNull(s.Battle);
                Assert.AreEqual(tree, s.Battle.Cell);
                Assert.AreEqual(home, s.Seats[seat].Cell);
            }
            else Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)), "턴당 한 번");
        }

        [Test]
        public void A20_6_psychic_파랑_떨어진숨겨진장소를뒤집는다_우두머리면배틀()
        {
            // 원작 커뮤니티 확인(2023-07-03)
            var log = new EventLog();
            var s = Table("psychic", out int seat, log);
            int home = s.Seats[seat].Cell;
            int far = TrainerRules.ReachCells(ctx, home).Find(c => ctx.Board[c].Wild && !ctx.Board[c].DistortionWorld
                && MapActions.EncounterTokenAt(s, c) < 0 && !HasFixture(c));
            int stash = Spot(s, "hidden_spot.red_apricorn_stash", far);
            var flip = TrainerRules.ReachAnswer(far, A(AnswerKind.Accept, stash));
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.SameAs(flip)));
            int red = s.Seats[seat].Apricorns[(int)ApricornColor.Red];
            Flow.Answer(s, ctx, ChoiceKind.MapAction, flip, log);
            Assert.AreEqual(red + 3, s.Seats[seat].Apricorns[(int)ApricornColor.Red]);
            Assert.AreEqual(-1, s.Tokens[stash].Cell);
            Assert.AreEqual(home, s.Seats[seat].Cell);
            Assert.AreEqual(seat, s.ActiveSeat);
            Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)), "턴당 한 번");
            // 우두머리: 뒤집으면 싸워야 한다
            var s2 = Table("psychic", out int seat2, new EventLog());
            int alpha = Spot(s2, "item.alpha_pokemon", far);
            Flow.Answer(s2, ctx, ChoiceKind.MapAction, TrainerRules.ReachAnswer(far, A(AnswerKind.Accept, alpha)));
            Assert.IsNotNull(s2.Battle, "강제 배틀");
            Assert.AreEqual(far, s2.Battle.Cell);
            Assert.AreEqual(home, s2.Seats[seat2].Cell);
        }

        [Test]
        public void A20_6_psychic_파랑_떨어진사당의공물은안쪽답의JP7까지싣는다_합법답이겹치지않는다()
        {
            // M6 화면 교차 검토 확인 B: ReachAnswer가 안쪽 답의 Values를 버려 「JP 7을 더 낸 공물」이 보통 공물과 같은 답이 됐다(합법 답 두 번 · psychic으로는 3장 공물을 못 냄)
            var log = new EventLog();
            var s = Table("psychic", out int seat, log);
            int shrine = s.Tokens.FindIndex(t => t.Kind == TokenKind.Shrine && t.Cell >= 0 && !t.Destroyed);
            int cell = s.Tokens[shrine].Cell;
            int home = TrainerRules.ReachCells(ctx, cell).Find(c => c != cell && !MoveRules.Adjacent(ctx.Board, c, cell) && !ctx.Board[c].DistortionWorld && ctx.Board[c].Wild);
            Assert.GreaterOrEqual(home, 0, "시험 준비: 사당에서 두 칸");
            Stand(s, seat, home, 3);
            var st = s.Seats[seat];
            st.Jp = 10;
            st.Apricorns[(int)ApricornColor.Red] = 1;
            var legal = Flow.MapLegal(s, ctx);
            for (int i = 0; i < legal.Count; i++)
                for (int k = i + 1; k < legal.Count; k++)
                    Assert.IsFalse(legal[i].SameAs(legal[k]), "합법 답이 겹친다: " + legal[i]);
            var plain = legal.Find(a => a.Kind == AnswerKind.UseAbility && a.Target == cell && a.Arg == (int)AnswerKind.Offer && a.Values.Count == 2 && a.Values[1] == (int)ApricornColor.Red);
            var extra = legal.Find(a => a.Kind == AnswerKind.UseAbility && a.Target == cell && a.Arg == (int)AnswerKind.Offer && a.Values.Count == 3 && a.Values[1] == (int)ApricornColor.Red);
            Assert.IsNotNull(plain, "보통 공물");
            Assert.IsNotNull(extra, "JP 7을 더 낸 공물");
            Assert.AreEqual(ShrineRules.ExtraCost, extra.Values[2]);
            Assert.IsTrue(TrainerRules.ReachInner(extra).SameAs(TrainerRules.ReachInner(TrainerRules.ReachAnswer(cell, TrainerRules.ReachInner(extra)))), "안쪽 답이 오간다");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, extra, log);
            Assert.AreEqual(10 - ShrineRules.ExtraCost, st.Jp, "JP 7을 냈다");
            Assert.AreEqual(home, st.Cell, "말은 그대로");
        }

        [Test]
        public void A20_6_psychic_파랑_떨어진사당의날씨바꾸기_떨어진게이트를건너면건너편에선다_T33()
        {
            // T33(2026-11-19 플레이 시험) 염력으로 빠진 칸 행동 훑기 — 원작 커뮤니티 확인:
            // 사당 칸의 날씨 바꾸기(JP 3)는 빠져 있었고, 게이트 건너기는 답은 있었지만 적용 뒤 제 칸으로 되돌아갔다(공중날기처럼 건너편에 선다 — rulings A43 ⑭)
            var log = new EventLog();
            var s = Table("psychic", out int seat, log);
            s.Weather = 0;
            int shrine = s.Tokens.FindIndex(t => t.Kind == TokenKind.Shrine && t.Cell >= 0 && !t.Destroyed);
            int cell = s.Tokens[shrine].Cell;
            int home = TrainerRules.ReachCells(ctx, cell).Find(c => c != cell && !MoveRules.Adjacent(ctx.Board, c, cell) && !ctx.Board[c].DistortionWorld && ctx.Board[c].Wild && ShrineRules.ShrineAt(s, c) < 0);
            Assert.GreaterOrEqual(home, 0, "시험 준비: 사당에서 두 칸");
            Stand(s, seat, home, 3);
            var st = s.Seats[seat];
            st.Jp = 5;
            var weather = TrainerRules.ReachAnswer(cell, A(AnswerKind.UseAbility, TrainerRules.WeatherTarget, 2));
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.SameAs(weather)), "떨어진 사당의 날씨 바꾸기");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, weather, log);
            Assert.AreEqual(2, s.Weather);
            Assert.AreEqual(5 - TrainerRules.WeatherCost, st.Jp, "JP 3");
            Assert.AreEqual(home, st.Cell, "말은 그대로");
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.UseAbility && a.Values.Count >= 2), "턴당 한 번");

            var ulog = new EventLog();
            var u = UbScenes.Started(ctx, 2, 5, ulog);
            int us = u.ActiveSeat;
            Card(u, us, "psychic");
            foreach (var t in u.Tokens)
                if (t.Kind == TokenKind.HiddenSpot) t.Destroyed = true;
            var fixtures = ctx.Fixtures.Fixtures;
            int gate = -1;
            for (int f = 0; f < fixtures.Count && gate < 0; f++)
                if (fixtures[f].Kind == FixtureKind.Gate && !ctx.Board[fixtures[f].Cell].DistortionWorld) gate = f;
            Assert.GreaterOrEqual(gate, 0, "시험 준비: 깨어진 세계 밖 게이트");
            int gcell = fixtures[gate].Cell, pair = fixtures[gate].Pair, far = fixtures[pair].Cell;
            int from = TrainerRules.ReachCells(ctx, gcell).Find(c => c != gcell && !MoveRules.Adjacent(ctx.Board, c, gcell) && !ctx.Board[c].DistortionWorld && UltraBurstSetup.GateAt(ctx, c) < 0);
            Assert.GreaterOrEqual(from, 0, "시험 준비: 게이트에서 두 칸");
            Stand(u, us, from, 0);
            var cross = TrainerRules.ReachAnswer(gcell, A(AnswerKind.Move, far, pair));
            Assert.IsTrue(Flow.MapLegal(u, ctx).Exists(a => a.SameAs(cross)), "떨어진 게이트 건너기");
            ulog.Clear();
            Flow.Answer(u, ctx, ChoiceKind.MapAction, cross, ulog);
            Assert.AreEqual(far, u.Seats[us].Cell, "건너편에 선다 — 제 칸으로 되돌아가지 않는다");
            Assert.AreEqual(-1, u.Seats[us].ReachFrom);
            Assert.AreEqual(1, Flow.Of<GateTravel>(ulog).Count);
        }

        // ── ruin_maniac B ──

        [Test]
        public void A20_6_ruin_maniac_파랑_특별한숨겨진장소공개_유물이면가서붙인다()
        {
            // 「Once per turn, you may reveal 1 Special Hidden Spot anywhere on the map. If it is an Artifact, you may instantly move to it and attach it to a Pokémon in your party.」
            var log = new EventLog();
            var s = Table("ruin_maniac", out int seat, log);
            int a1 = EmptyCell(s, c => true);
            int relic = s.Tokens.FindIndex(t => t.Kind == TokenKind.HiddenSpot && RelicRules.IsRelic(ctx, t.Def));
            s.Tokens[relic].Cell = a1;
            int a2 = EmptyCell(s, c => true);
            int stash = Spot(s, "hidden_spot.blue_apricorn_stash", a2);
            var legal = Flow.MapLegal(s, ctx).FindAll(a => a.Kind == AnswerKind.UseAbility);
            Assert.AreEqual(1, legal.Count, "특별한 숨겨진 장소만(보통 숨겨진 장소는 아니다)");
            Assert.AreEqual(relic, legal[0].Target);
            Assert.IsTrue(s.Tokens[relic].Special);
            Assert.IsFalse(s.Tokens[stash].Special);
            // 보기 사본에서도 같은 답(정체는 가려도 금색 ?은 보인다)
            var view = Engine.ViewFor(s, seat);
            Assert.AreEqual(-1, view.Tokens[relic].Def);
            Assert.IsTrue(Scene.Legal(view, ctx, view.Pending[0]).Exists(a => a.SameAs(legal[0])));
            int item = s.Tokens[relic].Def;
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, legal[0], log);
            Assert.IsTrue(s.Tokens[relic].FaceUp);
            Assert.AreEqual(1, Flow.Of<HiddenSpotRevealed>(log).Count);
            var ask = Flow.Only(s, ChoiceKind.Reaction);
            Assert.AreEqual(ChoiceSub.EffectPick, ask.Sub);
            Assert.AreEqual(SourceKind.TrainerCard, ask.Source.Kind);
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind, "may — 기본 답은 안 감");
            int mon = s.Seats[seat].Party[0];
            Flow.Answer(s, ctx, ChoiceKind.Reaction, ChoiceAnswer.Pick(mon), log);
            Assert.AreEqual(a1, s.Seats[seat].Cell, "그 칸으로 곧바로");
            Assert.AreEqual(item, s.Pokemon[mon].HeldItem, "그 포켓몬이 지닌다");
            Assert.AreEqual(-1, s.Tokens[relic].Cell);
            CollectionAssert.DoesNotContain(s.Seats[seat].Bag, item);
            Assert.AreEqual(seat, s.ActiveSeat);
            Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)), "턴당 한 번");
        }

        [Test]
        public void A20_6_ruin_maniac_파랑_가지않은유물과우두머리는앞면으로남는다()
        {
            // 원작 커뮤니티 확인
            var log = new EventLog();
            var s = Table("ruin_maniac", out int seat, log);
            int home = s.Seats[seat].Cell;
            int cell = EmptyCell(s, c => true);
            int alpha = Spot(s, "item.alpha_pokemon", cell);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.UseAbility, alpha), log);
            Assert.IsNull(s.Battle, "강제 배틀이 아니다");
            Assert.IsTrue(s.Tokens[alpha].FaceUp);
            Assert.AreEqual(cell, s.Tokens[alpha].Cell);
            Assert.AreEqual(alpha, HiddenSpotRules.FaceUpAlphaAt(s, ctx, cell));
            Flow.Only(s, ChoiceKind.MapAction);
            // 유물을 공개하고 가지 않으면 앞면으로 남고, 그 칸에 온 좌석이 줍는다
            var s2 = Table("ruin_maniac", out int seat2, new EventLog());
            int relic = s2.Tokens.FindIndex(t => t.Kind == TokenKind.HiddenSpot && RelicRules.IsRelic(ctx, t.Def));
            s2.Tokens[relic].Cell = cell;
            Flow.Answer(s2, ctx, ChoiceKind.MapAction, A(AnswerKind.UseAbility, relic));
            Flow.Answer(s2, ctx, ChoiceKind.Reaction, A(AnswerKind.Decline));
            Assert.AreEqual(home, s2.Seats[seat2].Cell);
            Assert.IsTrue(s2.Tokens[relic].FaceUp);
            Assert.AreEqual(cell, s2.Tokens[relic].Cell);
            Assert.AreEqual(relic, MapActions.FaceDownSpotAt(s2, cell), "그 칸에 서면 멈춰 묻는다");
            int near = Flow.StepInto(s2, ctx, cell);
            if (near < 0) return;
            Stand(s2, seat2, near, 2);
            Flow.Answer(s2, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(new List<int> { near, cell }));
            Flow.Answer(s2, ctx, ChoiceKind.HiddenSpot, A(AnswerKind.Accept));
            CollectionAssert.Contains(s2.Seats[seat2].Bag, s2.Tokens[relic].Def);
            Assert.AreEqual(-1, s2.Tokens[relic].Cell);
        }

        // ── scientist B ──

        [Test]
        public void A20_6_scientist_파랑_배틀없이찾아보고기술을가르친다_턴은이어진다()
        {
            // 「Once per turn, search at an Apricorn Tree, Apricorn Reef or Distortion Gate without starting a battle. You may now directly teach all the moves searched to any Pokémon in your party that can learn them.」
            var log = new EventLog();
            var s = Table("scientist", out int seat, log);
            Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)), "나무·산호·게이트가 아닌 칸");
            int tree = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak)].Cell;
            Stand(s, seat, tree, 3);
            int weak = s.Decks[(int)DeckId.Weak].Count;
            Assert.IsTrue(Flow.MapLegal(s, ctx).Exists(a => a.SameAs(A(AnswerKind.UseAbility))));
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.UseAbility), log);
            Assert.IsNull(s.Battle, "배틀이 아니다");
            Assert.AreEqual(weak, s.Decks[(int)DeckId.Weak].Count, "본 카드는 덱으로(원작 커뮤니티 확인)");
            var drawn = Flow.Of<EncounterDrawn>(log);
            Assert.AreEqual(1, drawn.Count);
            Assert.AreEqual(0, Flow.Of<BattleStarted>(log).Count);
            int taught = 0;
            for (int guard = 0; guard < 12 && s.Pending[0].Kind == ChoiceKind.Reward; guard++)
            {
                var c = Flow.Only(s, ChoiceKind.Reward);
                Assert.AreEqual(ChoiceSub.TeachMove, c.Sub);
                Assert.AreEqual(SourceKind.TrainerCard, c.Source.Kind);
                Assert.AreEqual(AnswerKind.Decline, c.Default.Kind, "may — 기본 답은 안 가르침");
                var pick = Scene.Legal(s, ctx, c).Find(a => a.Kind == AnswerKind.Pick);
                int move = c.Subject;
                Flow.Answer(s, ctx, ChoiceKind.Reward, pick, log);
                Assert.AreEqual(move, s.Pokemon[pick.Target].Moves[pick.Arg]);
                taught++;
            }
            Assert.AreEqual(taught, Flow.Of<MoveTaught>(log).Count);
            Assert.AreEqual(seat, s.ActiveSeat, "턴이 끝나지 않는다(원작 커뮤니티 확인)");
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsFalse(UsesAbility(legal), "턴당 한 번");
            Assert.IsTrue(legal.Exists(a => a.Kind == AnswerKind.Search), "같은 턴에 보통 찾아보기도 된다(원작 커뮤니티 확인)");
            Assert.AreEqual(0, Flow.Of<TurnEnded>(log).Count);
        }

        [Test]
        public void A20_6_scientist_파랑_왜곡게이트에서는무리셋을본다()
        {
            var log = new EventLog();
            var s = Table("scientist", out int seat, log);
            int gate = -1;
            foreach (var f in ctx.Fixtures.Fixtures)
                if (f.Kind == FixtureKind.Gate && !ctx.Board[f.Cell].DistortionWorld && gate < 0) gate = f.Cell;
            Stand(s, seat, gate, 3);
            int total = s.Decks[(int)DeckId.Weak].Count + s.Decks[(int)DeckId.Moderate].Count + s.Decks[(int)DeckId.Strong].Count;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.UseAbility), log);
            Assert.AreEqual(LegendRules.SwarmTiers.Length, Flow.Of<EncounterDrawn>(log)[0].Cards.Length);
            Assert.IsNull(s.Battle);
            Assert.AreEqual(total, s.Decks[(int)DeckId.Weak].Count + s.Decks[(int)DeckId.Moderate].Count + s.Decks[(int)DeckId.Strong].Count);
            while (s.Pending[0].Kind == ChoiceKind.Reward) Flow.Answer(s, ctx, ChoiceKind.Reward, s.Pending[0].Default.Clone(), log);
            Assert.AreEqual(0, Flow.Of<MoveTaught>(log).Count, "기본 답 = 안 가르침");
            Flow.Only(s, ChoiceKind.MapAction);
        }

        // ── scuba_diver B ──

        [Test]
        public void A20_6_scuba_diver_파랑_바다칸에서버림더미한장을그칸에_상호작용은선택()
        {
            // 「Once per turn whilst on an Ocean tile, shuffle the Hidden Spot discard pile and then draw the top Hidden Spot. Place the drawn Hidden Spot on the tile you are on, you may then interact with it as usual.」
            var log = new EventLog();
            var s = Table("scuba_diver", out int seat, log);
            int sea = EmptyCell(s, c => ctx.Board[c].Biome == Biome.Ocean);
            Stand(s, seat, sea, 2);
            Assert.AreEqual(0, TrainerRules.SunkenPile(s, ctx).Count);
            Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)), "버림 더미가 비었다");
            int stash = Spot(s, "hidden_spot.yellow_apricorn_stash", -1, true); // 공개된 뒤 버려진 규토리 더미
            int relic = s.Tokens.FindIndex(t => t.Kind == TokenKind.HiddenSpot && RelicRules.IsRelic(ctx, t.Def));
            s.Tokens[relic].FaceUp = true; // 주운 유물 토큰은 버림 더미가 아니다
            CollectionAssert.AreEqual(new[] { stash }, TrainerRules.SunkenPile(s, ctx));
            // 버려진 우두머리 토큰도 버림 더미다 — 포켓몬에 붙어 있는 것은 아니다
            int alpha = Spot(s, "item.alpha_pokemon", -1, true);
            Assert.AreEqual(2, TrainerRules.SunkenPile(s, ctx).Count);
            s.Pokemon[s.Seats[seat].Party[0]].Alpha = true;
            CollectionAssert.AreEqual(new[] { stash }, TrainerRules.SunkenPile(s, ctx));
            s.Pokemon[s.Seats[seat].Party[0]].Alpha = false;
            s.Tokens[alpha].FaceUp = false;
            Stand(s, seat, EmptyCell(s, c => ctx.Board[c].Biome == Biome.Plains), 2);
            Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)), "바다 칸이 아니다");
            Stand(s, seat, sea, 2);
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.UseAbility), log);
            Assert.AreEqual(sea, s.Tokens[stash].Cell);
            Assert.IsFalse(s.Tokens[stash].FaceUp);
            Assert.AreEqual(1, Flow.Of<TokensPlaced>(log).Count);
            var ask = Flow.Only(s, ChoiceKind.HiddenSpot);
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind, "상호작용은 선택(원작 커뮤니티 확인)");
            int yellow = s.Seats[seat].Apricorns[(int)ApricornColor.Yellow];
            Flow.Answer(s, ctx, ChoiceKind.HiddenSpot, A(AnswerKind.Accept), log);
            Assert.AreEqual(yellow + 3, s.Seats[seat].Apricorns[(int)ApricornColor.Yellow]);
            Assert.IsFalse(UsesAbility(Flow.MapLegal(s, ctx)), "턴당 한 번(버림 더미에 다시 들어가도)");
        }

        // ── veteran B ──

        [Test]
        public void A20_6_veteran_파랑_규토리더미는같은색2개더_이동1더()
        {
            // 「When you find an Apricorn Stash, gain 2 extra Apricorns of the same type and move 1 additional tile this turn.」
            foreach (string card in new[] { "veteran", null })
            {
                var log = new EventLog();
                var s = Table(card, out int seat, log);
                int cell = -1, near = -1;
                for (int c = 0; c < ctx.Board.Count && near < 0; c++)
                {
                    if (ctx.Board[c].DistortionWorld || !ctx.Board[c].Wild || HasFixture(c) || MapActions.EncounterTokenAt(s, c) >= 0) continue;
                    near = Flow.StepInto(s, ctx, c);
                    cell = c;
                }
                Spot(s, "hidden_spot.black_apricorn_stash", cell);
                Stand(s, seat, near, 2);
                int black = s.Seats[seat].Apricorns[(int)ApricornColor.Black];
                Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.MoveAlong(new List<int> { near, cell }), log);
                Flow.Answer(s, ctx, ChoiceKind.HiddenSpot, A(AnswerKind.Accept), log);
                bool vet = card != null;
                Assert.AreEqual(black + 1 + (vet ? TrainerRules.StashExtra : 0), s.Seats[seat].Apricorns[(int)ApricornColor.Black], "검정 더미 1 (+2)");
                Assert.AreEqual(1 + (vet ? TrainerRules.StashMoves : 0), s.Seats[seat].MovesLeft, "한 걸음 쓰고 (+1)");
                Assert.AreEqual(vet ? 1 : 0, Flow.Of<AbilityUsed>(log).Count);
            }
        }

        // ── 특별한 숨겨진 장소 ──

        [Test]
        public void M6_특별한숨겨진장소는우두머리유물토큰17개()
        {
            var s = Flow.Started(ctx, 2, 5, new EventLog());
            int special = 0;
            foreach (var t in s.Tokens)
            {
                if (t.Kind != TokenKind.HiddenSpot) continue;
                Assert.AreEqual(ctx.Items.Items[t.Def].Kind != ItemKind.HiddenSpot, t.Special);
                if (t.Special) special++;
            }
            Assert.AreEqual(17, special, "우두머리 6 · 유물 8 · 천상의 유물 3(rulings G12)");
        }
    }
}
