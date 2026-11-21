using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M5 「신·무리·우두머리」(rulings A28 · A18): 신 배틀(시작의 사당 신 토큰 찾아보기 · 천상의 오라 포획 7 · 둘째 폼 완전 회복 · 이기면 토큰이 빠지고 한 턴 더) ·
    /// 무리 배틀(왜곡 게이트 찾아보기 · 약한 → 적당한 → 강한 · 잡으면 다음 · 이기면 JP 7 하나 · 져도 쓰러뜨린 것의 기술) · 사당 전설 배틀 · 호수 동굴은 신오의 심장에서 닫힘.
    /// 배틀은 실제 흐름으로 돌고, 끝이 갈리는 곳은 매 선택 전에 체력을 맞춰 정한다(내 포켓몬 완전 회복 · 상대 체력 1).
    /// </summary>
    public class GodSwarmTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        int Card(string key)
        {
            int d = Scene.Def(ctx, key);
            Assert.GreaterOrEqual(d, 0, key);
            return d;
        }

        int Fixture(string key)
        {
            int f = ctx.Fixtures.Ids.IndexOf(key);
            Assert.GreaterOrEqual(f, 0, key);
            return f;
        }

        int Cell(string key) => ctx.Fixtures.Fixtures[Fixture(key)].Cell;

        /// <summary>신 배틀에 앞세우는 포켓몬: 미끄래곤(드래곤 기술 — 신의 드래곤 타입에 막히지 않는다, 체력 11).</summary>
        const string Goodra = "706:0";

        /// <summary>실제 판(2인)의 차례 좌석을 그 칸에 세운다. lead를 주면 그 포켓몬이 파티 맨 앞(먼저 나온다 — 스타팅은 뒤로). 파티 칸 6.</summary>
        GameState At(int cell, string lead = null)
        {
            var s = Flow.Started(ctx, 2, 12);
            var st = s.Seats[s.ActiveSeat];
            st.Cell = cell;
            st.OpenSlots = 6;
            if (lead != null)
            {
                int id = Scene.Give(s, ctx, s.ActiveSeat, lead);
                int at = PokemonRules.PartyIndexOf(st, id);
                st.Party[at] = st.Party[0];
                st.Party[0] = id;
            }
            return s;
        }

        /// <summary>시작의 사당에 그 신의 토큰(Def = 폼 1 카드 — 놓기는 「천상의 균열」).</summary>
        void God(GameState s, string key) =>
            s.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = Card(key), Cell = HosFlow.OriginShrineCell(ctx), FaceUp = true });

        /// <summary>
        /// 배틀(과 배틀 뒤 선택)이 끝날 때까지 돈다. 매 선택 전에 tune(s)를 부르고, pick이 null을 주면 Default로 답한다. 이벤트는 log에.
        /// </summary>
        void Drive(GameState s, EventLog log, Action<GameState> tune, Func<GameState, PendingChoice, ChoiceAnswer> pick = null, int limit = 600)
        {
            for (int i = 0; i < limit; i++)
            {
                var c = s.Pending.Find(x => !x.Answered && (s.Battle != null || x.Window == Window.BattleEnd));
                if (c == null) return;
                tune?.Invoke(s);
                Battles.Answer(s, ctx, c, pick?.Invoke(s, c) ?? c.Default.Clone(), log);
            }
            Assert.Fail("배틀이 끝나지 않는다");
        }

        /// <summary>그 좌석 파티를 모두 완전 회복(지지 않게).</summary>
        void Fresh(GameState s, int seat)
        {
            foreach (int p in s.Seats[seat].Party)
                if (p >= 0)
                {
                    s.Pokemon[p].Damage = 0;
                    s.Pokemon[p].Fatigue = 0;
                    StatusRules.Clear(s.Pokemon[p]);
                }
        }

        /// <summary>출전 중인 야생의 체력을 1로(다음 맞는 공격에 쓰러진다).</summary>
        void Weak(GameState s)
        {
            int w = s.Battle != null ? s.Battle.Sides[1].Active : -1;
            if (w >= 0) s.Pokemon[w].Damage = PokemonRules.MaxHp(ctx, s.Pokemon[w]) - 1;
        }

        static ChoiceAnswer Search(List<ChoiceAnswer> legal, int fixture) => legal.Find(a => a.Kind == AnswerKind.Search && a.Arg == fixture);

        // ── 신(rulings A28 ①) ──

        [Test]
        public void M5_배틀시작_God_시작의사당의신토큰칸에서찾아보기로신과싸운다()
        {
            var log = new EventLog();
            var s = At(HosFlow.OriginShrineCell(ctx));
            Assert.IsNull(Search(Flow.MapLegal(s, ctx), HosFlow.OriginShrine(ctx)), "신 토큰이 없으면 싸울 것이 없다");
            God(s, "483:1");
            int seat = s.ActiveSeat;
            var search = Search(Flow.MapLegal(s, ctx), HosFlow.OriginShrine(ctx));
            Assert.IsNotNull(search, "신 토큰 칸에서 찾아보기(강제가 아니다)");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, search, log);
            var started = Flow.Of<BattleStarted>(log);
            Assert.AreEqual(1, started.Count);
            Assert.AreEqual(BattleKind.God, started[0].Kind);
            Assert.AreEqual((int)BattleBgm.God, started[0].BgmKey);
            Assert.AreEqual(seat, started[0].Sides[0].Owner);
            Assert.AreEqual(Card("483:1"), Flow.Of<SentIn>(log).Find(e => e.Side == 1).Species, "첫 폼(State 1)");
            Assert.AreEqual(0, Flow.Of<InitiativeTokenAt>(log)[0].Side, "도전한 좌석이 우선권 토큰");
            if (s.Battle != null)
            {
                CollectionAssert.DoesNotContain(s.Decks[(int)DeckId.Legendary], Card("483:1"), "배틀하는 동안 카드는 덱 밖");
                Assert.IsTrue(s.Pokemon[s.Battle.Sides[1].Roster[0]].HasLegendaryMove, "전설 특수기술");
                Assert.IsTrue(LegendRules.AuraOn(s, ctx, s.Battle), "금강옥이 없으면 천상의 오라");
            }
        }

        [Test]
        public void A28_천상의오라면첫폼이쓰러질때둘째폼이완전회복해다시나오고_순서를다시정한다()
        {
            var log = new EventLog();
            var s = At(HosFlow.OriginShrineCell(ctx), Goodra);
            int seat = s.ActiveSeat;
            God(s, "487:1");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, Search(Flow.MapLegal(s, ctx), HosFlow.OriginShrine(ctx)), log);
            int god = s.Battle.Sides[1].Roster[0];
            Drive(s, log, x =>
            {
                Fresh(x, seat);
                Weak(x); // 둘째 폼도 나온 뒤(SentIn 다음)에 체력 1로 — 배틀이 끝나게
            });
            var changed = Flow.Of<FormChanged>(log).Find(e => e.PokemonId == god && e.Source.Same(SourceRef.Rule(RuleSource.CelestialAura)));
            Assert.IsNotNull(changed, "「after their first form is defeated, they proceed to their second form」");
            Assert.AreEqual(Card("487:1"), changed.Before);
            Assert.AreEqual(Card("487:2"), changed.After, "기라티나 오리진폼(State 2)");
            int at = log.Events.IndexOf(changed);
            var fainted = log.Events.FindLastIndex(at, e => e is Fainted f && f.PokemonId == god);
            Assert.GreaterOrEqual(fainted, 0, "첫 폼이 쓰러진 뒤");
            var again = log.Events.FindIndex(at, e => e is SentIn si && si.Side == 1);
            Assert.Greater(again, at, "다음 포켓몬처럼 다시 나온다");
            var sent = (SentIn)log.Events[again];
            Assert.AreEqual(Card("487:2"), sent.Species);
            Assert.AreEqual(sent.MaxHp, sent.Hp, "「fully restored」");
            Assert.AreEqual(ctx.Cards.Pokemon[Card("487:2")].Health, sent.MaxHp);
            Assert.Greater(log.Events.FindIndex(again, e => e is OrderDecided), again, "순서를 다시 정한다(원작 커뮤니티 확인)");
            Assert.AreEqual(1, Flow.Of<FormChanged>(log).FindAll(e => e.Source.Same(SourceRef.Rule(RuleSource.CelestialAura))).Count, "둘째 폼은 한 번");
        }

        [Test]
        public void A28_천상의유물이있으면둘째폼없이한폼_쓰러뜨리면토큰이빠지고한턴더()
        {
            var log = new EventLog();
            var s = At(HosFlow.OriginShrineCell(ctx), Goodra);
            int seat = s.ActiveSeat;
            s.Seats[seat].Bag.Add(Scene.Item(ctx, "item.adamant_artifact"));
            God(s, "483:1");
            int token = s.Tokens.Count - 1;
            int turn = s.TurnId;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, Search(Flow.MapLegal(s, ctx), HosFlow.OriginShrine(ctx)), log);
            Assert.IsFalse(LegendRules.AuraOn(s, ctx, s.Battle), "금강옥을 가방에 가지면 오라가 없다(가지기만 하면 된다)");
            Drive(s, log, x => { Fresh(x, seat); Weak(x); });
            Assert.AreEqual(0, Flow.Of<FormChanged>(log).FindAll(e => e.Source.Same(SourceRef.Rule(RuleSource.CelestialAura))).Count, "둘째 폼 없이(「without facing his second form」)");
            var end = Flow.Of<BattleEnded>(log)[0];
            Assert.AreEqual(BattleResult.Win, end.Result);
            Assert.AreEqual(0, end.Winner);
            Assert.AreEqual(-1, s.Tokens[token].Cell, "「A God of Sinnoh token is removed from the game once it is defeated」");
            Assert.IsTrue(Flow.Of<TokenRemoved>(log).Exists(e => e.TokenKind == TokenKind.God));
            CollectionAssert.Contains(s.Discards[(int)DeckId.Legendary], Card("483:1"), "쓰러뜨린 신은 버림 더미(폼 1 카드)");
            var extra = Flow.Of<TurnStarted>(log).Find(e => e.TurnId > turn);
            Assert.IsNotNull(extra);
            Assert.AreEqual(seat, extra.Seat, "「you may immediately take another turn」");
            Assert.IsTrue(extra.IsExtra);
            Assert.AreEqual(seat, s.ActiveSeat);
            Assert.AreEqual(seat, Flow.Only(s, ChoiceKind.MapAction).Seat);
            Assert.IsNull(Search(Flow.MapLegal(s, ctx), HosFlow.OriginShrine(ctx)), "토큰이 빠졌으니 다시 싸우지 못한다");

            // 추가 턴이 끝나면 순서는 다음 좌석부터
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
            var next = Flow.Of<TurnStarted>(log)[0];
            Assert.AreEqual((seat + 1) % s.SeatCount, next.Seat);
            Assert.IsFalse(next.IsExtra);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void A28_신을쓰러뜨리면두폼의시그니처가운데하나를가르친다_천상의유물이있어도(bool declineFirst)
        {
            // 원작 커뮤니티 확인(물음원작 커뮤니티 확인) —
            // 디아루가 폼 1 roar_of_time(드래곤) · 폼 2 temporal_guard(강철), 미끄래곤은 둘 다 배운다. 하나씩 묻고(안 가르치면 다음 폼의 것), 가르치면 나머지는 묻지 않는다
            var log = new EventLog();
            var s = At(HosFlow.OriginShrineCell(ctx), Goodra);
            int seat = s.ActiveSeat;
            int goodra = s.Seats[seat].Party[0];
            s.Seats[seat].Bag.Add(Scene.Item(ctx, "item.adamant_artifact"));
            God(s, "483:1");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, Search(Flow.MapLegal(s, ctx), HosFlow.OriginShrine(ctx)), log);
            var asked = new List<int>();
            Drive(s, log, x => { Fresh(x, seat); Weak(x); }, (x, c) =>
            {
                if (c.Kind != ChoiceKind.Reward || c.Sub != ChoiceSub.TeachMove) return null;
                asked.Add(c.Subject);
                return declineFirst && asked.Count == 1 ? ChoiceAnswer.Of(AnswerKind.Decline) : ChoiceAnswer.Pick(goodra, 0);
            });
            Assert.AreEqual(BattleResult.Win, Flow.Of<BattleEnded>(log)[0].Result);
            int roar = Scene.Move(ctx, "roar_of_time"), guard = Scene.Move(ctx, "temporal_guard");
            CollectionAssert.AreEqual(declineFirst ? new[] { roar, guard } : new[] { roar }, asked, "둘째 폼을 만나지 않았어도(금강옥) 그 폼의 시그니처까지");
            Assert.AreEqual(declineFirst ? guard : roar, s.Pokemon[goodra].Moves[0]);
        }

        [Test]
        public void A28_천상의오라면포획7_유물이있으면6_잡아도토큰이빠지고한턴더()
        {
            foreach (bool artifact in new[] { false, true })
            {
                var log = new EventLog();
                var s = At(HosFlow.OriginShrineCell(ctx), Goodra);
                int seat = s.ActiveSeat;
                if (artifact) s.Seats[seat].Bag.Add(Scene.Item(ctx, "item.lustrous_artifact"));
                s.Seats[seat].Balls[(int)Ball.Keystone] = 30;
                God(s, "484:1");
                int token = s.Tokens.Count - 1;
                Flow.Answer(s, ctx, ChoiceKind.MapAction, Search(Flow.MapLegal(s, ctx), HosFlow.OriginShrine(ctx)), log);
                Drive(s, log, x => Fresh(x, seat), (x, c) =>
                {
                    if (c.Kind != ChoiceKind.BattleAction || c.Seat != seat) return null;
                    return Scene.Legal(x, ctx, c).Find(a => a.Kind == AnswerKind.ThrowBall);
                });
                var tries = Flow.Of<CaptureAttempt>(log);
                Assert.Greater(tries.Count, 0, artifact ? "유물" : "오라");
                foreach (var t in tries) Assert.AreEqual(artifact ? 6 : 7, t.CatchRate, artifact ? "「6 if you do have it」" : "「They require a roll of 7 to capture」(첫 폼에서도)");
                var end = Flow.Of<BattleEnded>(log)[0];
                Assert.AreEqual(BattleResult.Captured, end.Result, "볼 30개 안에 잡힌다");
                Assert.AreEqual(-1, s.Tokens[token].Cell, "「removed from the game once it is … captured」");
                var caught = Flow.Of<PokemonGained>(log)[0];
                Assert.AreEqual(Card("484:1"), s.Pokemon[caught.PokemonId].Def, "잡힌 신은 기본 폼");
                Assert.IsTrue(s.Seats[seat].Stamps[StampRules.LegendaryCaught], "신 포획도 CaptureResolve 성공 길 — 스탬프 4(rulings A30 ②4)");
                Assert.IsTrue(Flow.Of<TurnStarted>(log).Exists(e => e.IsExtra && e.Seat == seat), "잡아도 이긴 것으로 친다 — 한 턴 더 [가정 plan §5 #89 ③]");
            }
        }

        [Test]
        public void A28_신에게지면토큰은남고카드는전설덱_한턴더는없다()
        {
            var log = new EventLog();
            var s = At(HosFlow.OriginShrineCell(ctx));
            int seat = s.ActiveSeat;
            God(s, "483:1");
            int token = s.Tokens.Count - 1;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, Search(Flow.MapLegal(s, ctx), HosFlow.OriginShrine(ctx)), log);
            int god = s.Battle.Sides[1].Roster[0];
            // 신은 지치지 않고 내 포켓몬은 체력 1
            Drive(s, log, x =>
            {
                x.Pokemon[god].Damage = 0;
                foreach (int p in x.Seats[seat].Party)
                    if (p >= 0 && !PokemonRules.IsFainted(ctx, x.Pokemon[p])) x.Pokemon[p].Damage = PokemonRules.MaxHp(ctx, x.Pokemon[p]) - 1;
            });
            var end = Flow.Of<BattleEnded>(log)[0];
            Assert.AreEqual(BattleResult.Fled, end.Result, "전설처럼 내 포켓몬을 모두 쓰러뜨린 뒤 도망");
            Assert.AreEqual(HosFlow.OriginShrineCell(ctx), s.Tokens[token].Cell, "지면 토큰은 남는다(원작 커뮤니티 확인)");
            CollectionAssert.Contains(s.Decks[(int)DeckId.Legendary], Card("483:1"), "카드는 전설 덱으로");
            Assert.IsFalse(Flow.Of<TurnStarted>(log).Exists(e => e.IsExtra));
            Assert.AreNotEqual(seat, s.ActiveSeat, "턴이 넘어간다");
        }

        // ── 무리 배틀(rulings A28 ② · A18) ──

        [TestCase("gate.deepmarsh_main", Biome.Forest)]
        [TestCase("gate.snowdrift_main", Biome.Plains)]
        [TestCase("gate.lilly_main", Biome.Mountain)]
        [TestCase("gate.westbreeze_main", Biome.Ocean)]
        public void M5_배틀시작_Swarm_왜곡게이트에서찾아보면그지형의약한적당한강한이순서로나온다(string gate, Biome biome)
        {
            var log = new EventLog();
            var s = At(Cell(gate), "242:0");
            int seat = s.ActiveSeat;
            Assert.AreEqual(biome, LegendRules.GateBiome(ctx, Fixture(gate)));
            var search = Search(Flow.MapLegal(s, ctx), Fixture(gate));
            Assert.IsNotNull(search, "「Search」 whilst you are at a Distortion Gate");
            int total = Playout.CardCount(s);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, search, log);
            if (s.Pending.Count == 1 && s.Pending[0].Kind == ChoiceKind.SendIn) Battles.Answer(s, ctx, s.Pending[0], s.Pending[0].Default.Clone(), log); // 파티 둘 — 첫 출전 고르기
            var started = Flow.Of<BattleStarted>(log);
            Assert.AreEqual(1, started.Count);
            Assert.AreEqual(BattleKind.Swarm, started[0].Kind);
            Assert.AreEqual(0, Flow.Of<EncounterDrawn>(log).Count, "조우 고르기 없이(「manually move their cards」)");
            Assert.AreEqual(0, Flow.Of<InitiativeTokenAt>(log)[0].Side, "도전한 좌석이 우선권 토큰");
            var b = s.Battle;
            Assert.IsNotNull(b, "첫 공격 전");
            Assert.AreEqual(3, b.Sides[1].Roster.Count);
            var tiers = new[] { EncounterTier.Weak, EncounterTier.Moderate, EncounterTier.Strong };
            for (int i = 0; i < 3; i++)
            {
                var wp = s.Pokemon[b.Sides[1].Roster[i]];
                var d = ctx.Cards.Pokemon[wp.Def];
                Assert.AreEqual((int)MapActions.DeckOf(tiers[i]), wp.Deck, "약한 → 적당한 → 강한 덱(약한 덱에는 스타팅 카드도 있다)");
                Assert.AreEqual(biome, d.Biome, "그 게이트의 지형");
            }
            Assert.AreEqual(b.Sides[1].Roster[0], b.Sides[1].Active, "약한 것이 먼저");
            Assert.AreEqual(total, Playout.CardCount(s), "카드 수 보존");
            Assert.AreEqual(seat, b.Sides[0].Owner);
        }

        /// <summary>무리 배틀을 그 카드로 바로 연다(좌석 0, 장면 판).</summary>
        GameState Swarm(EventLog log, string[] cards, params string[] party)
        {
            var s = Battles.Game(ctx, 2);
            s.Seats[0].OpenSlots = 6;
            foreach (var k in party) Scene.Give(s, ctx, 0, k);
            var ids = new List<int>();
            foreach (var k in cards)
            {
                int c = Card(k);
                ids.Add(c);
                s.Decks[(int)MapActions.DeckOf(MapActions.GradeOf(ctx, c))].Remove(c);
            }
            BattleFlow.StartSwarm(s, ctx, Scene.Fx(s, log, 0, ActionKind.Search), 0, ids, s.Seats[0].Cell);
            return s;
        }

        /// <summary>그 등급 덱에서 시그니처가 그 타입인 카드 하나(가르치기 시험용).</summary>
        string CardOf(EncounterTier tier, PokemonType type)
        {
            var cards = ctx.Cards.Pokemon;
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].Tier == tier && cards[i].Signature >= 0 && ctx.Cards.Moves[cards[i].Signature].Type == type && cards[i].Key.IndexOf('@') < 0 && !cards[i].Uncapturable) return cards[i].Key;
            Assert.Fail($"{tier} {type}");
            return null;
        }

        [Test]
        public void A18_무리는셋을모두쓰러뜨리면JP7하나_규토리없음_기술셋을차례로묻는다()
        {
            var log = new EventLog();
            // 해피너스(노말)는 노말 기술을 배운다
            var cards = new[] { CardOf(EncounterTier.Weak, PokemonType.Normal), CardOf(EncounterTier.Moderate, PokemonType.Normal), CardOf(EncounterTier.Strong, PokemonType.Normal) };
            var s = Swarm(log, cards, "242:0");
            Assert.IsTrue(BattleRules.HasUsable(s, ctx, 0));
            int jp = s.Seats[0].Jp;
            var asked = new List<int>();
            Drive(s, log, x => { Fresh(x, 0); Weak(x); }, (x, c) =>
            {
                if (c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.TeachMove) asked.Add(c.Subject);
                return null;
            });
            Assert.AreEqual(3, Flow.Of<Fainted>(log).FindAll(e => e.Side == 1).Count, "셋 모두 쓰러뜨림");
            var end = Flow.Of<BattleEnded>(log)[0];
            Assert.AreEqual(BattleResult.Win, end.Result);
            Assert.AreEqual(0, end.Winner);
            Assert.AreEqual(1, end.Changes.Length, "JP 하나 — 규토리 없음");
            Assert.AreEqual(ResKind.Jp, end.Changes[0].Res);
            Assert.AreEqual(BattleRules.SwarmRewardJp, end.Changes[0].After - end.Changes[0].Before, "「you gain 7 Journey Points as the total reward」");
            Assert.AreEqual(jp + 7, s.Seats[0].Jp);
            var sigs = new List<int>();
            foreach (var k in cards) sigs.Add(ctx.Cards.Pokemon[Card(k)].Signature);
            CollectionAssert.AreEqual(sigs, asked, "쓰러뜨린 셋의 기술을 명단 순서로 하나씩(원작 커뮤니티 확인)");
            foreach (var k in cards) CollectionAssert.Contains(s.Discards[(int)MapActions.DeckOf(MapActions.GradeOf(ctx, Card(k)))], Card(k), "쓰러진 카드는 버림 더미");
            int sent = Flow.Of<SentIn>(log).FindAll(e => e.Side == 1).Count;
            Assert.AreEqual(3, sent, "하나씩 나온다");
        }

        [Test]
        public void A18_무리에서하나를잡으면다음포켓몬이나오고_모두잡아도JP7()
        {
            var log = new EventLog();
            var cards = new[] { CardOf(EncounterTier.Weak, PokemonType.Normal), CardOf(EncounterTier.Moderate, PokemonType.Normal), CardOf(EncounterTier.Strong, PokemonType.Normal) };
            var s = Swarm(log, cards, "242:0");
            s.Seats[0].Balls[(int)Ball.Keystone] = 40;
            s.Seats[0].OpenSlots = 6;
            int jp = s.Seats[0].Jp;
            Drive(s, log, x => Fresh(x, 0), (x, c) =>
                c.Kind == ChoiceKind.BattleAction && c.Seat == 0 ? Scene.Legal(x, ctx, c).Find(a => a.Kind == AnswerKind.ThrowBall) : null);
            var gained = Flow.Of<PokemonGained>(log);
            Assert.AreEqual(3, gained.Count, "셋 모두 잡힌다(볼 40개)");
            for (int i = 0; i < 3; i++) Assert.AreEqual(Card(cards[i]), gained[i].Species, "잡으면 다음 포켓몬(「as if you had won」)");
            Assert.AreEqual(1, Flow.Of<BattleEnded>(log).Count);
            var end = Flow.Of<BattleEnded>(log)[0];
            Assert.AreEqual(BattleResult.Captured, end.Result);
            Assert.AreEqual(jp + 7, s.Seats[0].Jp, "잡아도 JP 7 하나(「Even if you catch all three … you always get seven」)");
        }

        [Test]
        public void A18_무리에서지면JP는없고쓰러뜨린것의기술은가르친다_나머지카드는덱으로()
        {
            var log = new EventLog();
            var cards = new[] { CardOf(EncounterTier.Weak, PokemonType.Normal), CardOf(EncounterTier.Moderate, PokemonType.Normal), CardOf(EncounterTier.Strong, PokemonType.Normal) };
            var s = Swarm(log, cards, "242:0");
            int jp = s.Seats[0].Jp;
            int first = s.Battle.Sides[1].Roster[0];
            var asked = new List<int>();
            // 약한 것만 쓰러뜨리고 그다음은 내가 진다
            Drive(s, log, x =>
            {
                if (x.Battle == null) return;
                if (x.Battle.Sides[1].Active == first) { Fresh(x, 0); Weak(x); }
                else
                {
                    int w = x.Battle.Sides[1].Active;
                    if (w >= 0) x.Pokemon[w].Damage = 0;
                    foreach (int p in x.Seats[0].Party)
                        if (p >= 0 && !PokemonRules.IsFainted(ctx, x.Pokemon[p])) x.Pokemon[p].Damage = PokemonRules.MaxHp(ctx, x.Pokemon[p]) - 1;
                }
            }, (x, c) =>
            {
                if (c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.TeachMove) asked.Add(c.Subject);
                return null;
            });
            var end = Flow.Of<BattleEnded>(log)[0];
            Assert.AreEqual(BattleResult.Fled, end.Result, "내 포켓몬을 모두 쓰러뜨린 뒤 도망(「only flee after defeating all your Pokémon」)");
            Assert.AreEqual(jp, s.Seats[0].Jp, "「You do not gain any Journey Points if you lose」");
            CollectionAssert.AreEqual(new[] { ctx.Cards.Pokemon[Card(cards[0])].Signature }, asked, "쓰러뜨린 것의 기술은 가르친다(원작 커뮤니티 확인)");
            CollectionAssert.Contains(s.Discards[(int)DeckId.Weak], Card(cards[0]));
            CollectionAssert.Contains(s.Decks[(int)DeckId.Moderate], Card(cards[1]), "쓰러지지 않은 카드는 덱으로");
            CollectionAssert.Contains(s.Decks[(int)DeckId.Strong], Card(cards[2]), "나오지 않은 카드도 덱으로");
            Assert.IsFalse(s.Seats[0].Stamps[StampRules.SwarmWin], "지면 스탬프 6은 없다(rulings A30 ②6)");
        }

        [Test]
        public void A30_무리배틀셋을모두이기면스탬프6_배틀결과뒤()
        {
            var log = new EventLog();
            var cards = new[] { CardOf(EncounterTier.Weak, PokemonType.Normal), CardOf(EncounterTier.Moderate, PokemonType.Normal), CardOf(EncounterTier.Strong, PokemonType.Normal) };
            var s = Swarm(log, cards, "242:0");
            Drive(s, log, x => { Fresh(x, 0); Weak(x); }, (x, c) => null);
            var end = Flow.Of<BattleEnded>(log)[0];
            Assert.AreEqual(BattleResult.Win, end.Result);
            Assert.IsTrue(s.Seats[0].Stamps[StampRules.SwarmWin], "「You have been victorious at a Swarm Battle」 — JP 7과 같은 조건");
            var g = log.OfType<StampGained>().FindAll(e => e.StampKey == StampRules.ItemOf(ctx, StampRules.SwarmWin));
            Assert.AreEqual(1, g.Count);
            Assert.AreEqual(0, g[0].Seat);
            Assert.Less(log.Events.IndexOf(end), log.Events.IndexOf(g[0]), "배틀 결과 뒤(스탬프 2와 같은 자리)");
        }

        [Test]
        public void A18_무리에서파티가가득이면잡은것마다놓아주기를배틀끝에하나씩묻는다()
        {
            var log = new EventLog();
            var cards = new[] { CardOf(EncounterTier.Weak, PokemonType.Normal), CardOf(EncounterTier.Moderate, PokemonType.Normal), CardOf(EncounterTier.Strong, PokemonType.Normal) };
            var s = Swarm(log, cards, "242:0");
            s.Seats[0].OpenSlots = 1;
            s.Seats[0].Balls[(int)Ball.Keystone] = 40;
            var asked = new List<int>();
            Drive(s, log, x => Fresh(x, 0), (x, c) =>
            {
                if (c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.PartyFull)
                {
                    asked.Add(c.Subject);
                    return ChoiceAnswer.Pick(c.Subject); // 새로 잡은 것을 놓아준다
                }
                return c.Kind == ChoiceKind.BattleAction && c.Seat == 0 ? Scene.Legal(x, ctx, c).Find(a => a.Kind == AnswerKind.ThrowBall) : null;
            });
            Assert.AreEqual(3, Flow.Of<CaptureAttempt>(log).FindAll(e => e.Success).Count);
            Assert.AreEqual(3, asked.Count, "잡은 셋마다 하나씩");
            Assert.AreEqual(3, Flow.Of<Released>(log).Count);
            Assert.AreEqual(Card("242:0"), s.Pokemon[s.Seats[0].Party[0]].Def, "가진 것은 그대로");
        }

        // ── 사당 전설 · 호수 동굴 ──

        [Test]
        public void A28_사당전설은그사당칸에서찾아보기로싸우고_쓰러뜨리면토큰이빠지고_지면남는다()
        {
            foreach (bool win in new[] { true, false })
            {
                var log = new EventLog();
                int shrine = Fixture("legendary_shrine.frostsong_shrine");
                var s = At(ctx.Fixtures.Fixtures[shrine].Cell, Goodra);
                int seat = s.ActiveSeat;
                Assert.IsNull(Search(Flow.MapLegal(s, ctx), shrine), "전설 토큰이 없으면 싸울 것이 없다");
                s.Tokens.Add(new TokenState { Kind = TokenKind.Legend, Def = Card("144:0"), Cell = ctx.Fixtures.Fixtures[shrine].Cell, FaceUp = true });
                int token = s.Tokens.Count - 1;
                var search = Search(Flow.MapLegal(s, ctx), shrine);
                Assert.IsNotNull(search, "그 칸에서 찾아보기(전설은 강제가 아니다)");
                Flow.Answer(s, ctx, ChoiceKind.MapAction, search, log);
                Assert.AreEqual(BattleKind.Legendary, Flow.Of<BattleStarted>(log)[0].Kind);
                int wild = s.Battle.Sides[1].Roster[0];
                Assert.AreEqual(Card("144:0"), s.Pokemon[wild].Def, "프리저");
                if (win) Drive(s, log, x => { Fresh(x, seat); Weak(x); });
                else Drive(s, log, x =>
                {
                    if (x.Battle != null) x.Pokemon[wild].Damage = 0;
                    foreach (int p in x.Seats[seat].Party)
                        if (p >= 0 && !PokemonRules.IsFainted(ctx, x.Pokemon[p])) x.Pokemon[p].Damage = PokemonRules.MaxHp(ctx, x.Pokemon[p]) - 1;
                });
                var end = Flow.Of<BattleEnded>(log)[0];
                if (win)
                {
                    Assert.AreEqual(BattleResult.Win, end.Result);
                    Assert.AreEqual(-1, s.Tokens[token].Cell, "「The token is removed from the game once the Legendary Pokémon is defeated or captured」");
                    Assert.IsFalse(Flow.Of<TurnStarted>(log).Exists(e => e.IsExtra), "한 턴 더는 신만");
                }
                else
                {
                    Assert.AreNotEqual(BattleResult.Win, end.Result);
                    Assert.AreEqual(ctx.Fixtures.Fixtures[shrine].Cell, s.Tokens[token].Cell, "지면 토큰은 남는다");
                    CollectionAssert.Contains(s.Decks[(int)DeckId.Legendary], Card("144:0"));
                }
            }
        }

        [TestCase("legendary_site.lake_verity_cavern")]
        [TestCase("legendary_site.lake_acuity_cavern")]
        [TestCase("legendary_site.lake_valor_cavern")]
        [TestCase("legendary_site.temple_of_sinnoh")]
        public void A28_호수동굴과신오신전의전설은신오의심장에서만나지못한다(string site)
        {
            var s = At(Cell(site));
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Search && a.Arg >= 0), "원작 커뮤니티 확인");
        }

        // ── 우두머리(A17 — 「숨겨진 장소·유물」에서 만듦) ──

        [Test]
        public void M5_배틀시작_Alpha_우두머리는도전자라우선권토큰을쥐고시작한다()
        {
            var log = new EventLog();
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "242:0");
            int card = s.Decks[(int)DeckId.Strong][0];
            s.Decks[(int)DeckId.Strong].RemoveAt(0);
            BattleFlow.StartAlpha(s, ctx, Scene.Fx(s, log, 0, ActionKind.Search), 0, card, s.Seats[0].Cell);
            Assert.AreEqual(1, Flow.Of<InitiativeTokenAt>(log)[0].Side, "「Since the Alpha Pokémon is the initiator, they start with Initiative Advantage」");
            var swarm = new EventLog();
            var t = Swarm(swarm, new[] { CardOf(EncounterTier.Weak, PokemonType.Normal), CardOf(EncounterTier.Moderate, PokemonType.Normal), CardOf(EncounterTier.Strong, PokemonType.Normal) }, "242:0");
            Assert.AreEqual(0, Flow.Of<InitiativeTokenAt>(swarm)[0].Side, "그 밖의 야생(무리)은 도전한 좌석(원작 커뮤니티 확인)");
        }
    }
}
