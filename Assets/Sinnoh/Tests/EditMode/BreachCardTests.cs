using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 왜곡 균열 카드(룰북 p.24, rulings A15 · A52 — M6 「균열 카드 36장」): 여섯 종 효과(영문 카드 문구) · 붙이기(동시 선택 · 기술 칸을 막음 · 손에 들기) · 파티를 떠나지 못함 · UB 격파 뒤 버리기·옮기기.
    /// 효과 장면은 신오의 심장 장면 판에 카드를 손으로 붙여(PokemonState.Breach) 본다 — 효과는 캠페인을 보지 않는다.
    /// </summary>
    public class BreachCardTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        int Give(GameState s, int seat, string key) => Scene.Give(s, ctx, seat, key);
        int Wild(GameState s, string key) => Scene.Spawn(s, ctx, key, SeatId.Wild);
        int Mv(string key) => Scene.Move(ctx, key);
        int Card(BreachKind kind) => BreachRules.CardOf(ctx, kind);

        void Stick(GameState s, int pokemon, BreachKind kind, int slot = 0) => s.Pokemon[pokemon].Breach[slot] = Card(kind);

        [Test]
        public void A15_카드표의여섯종류가규칙의여섯종류다()
        {
            var seen = new List<int>();
            foreach (BreachKind kind in System.Enum.GetValues(typeof(BreachKind)))
            {
                if (kind == BreachKind.None) continue;
                int card = Card(kind);
                Assert.GreaterOrEqual(card, 0, kind.ToString());
                Assert.AreEqual(kind, BreachRules.KindOf(ctx, card));
                seen.Add(card);
            }
            int total = 0;
            foreach (var e in ctx.Deck(DeckId.DistortionBreach).Cards)
            {
                CollectionAssert.Contains(seen, e.Card);
                total += e.Copies;
            }
            Assert.AreEqual(36, total);
        }

        [Test]
        public void A15_무모한강습_유리한상성이면공격과피격모두2더()
        {
            // 「When the user attacks or is attacked, positive type effectiveness damage is increased by 2.」
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "390:0");
            Stick(s, mine, BreachKind.RecklessAssault);
            int foe = Wild(s, "387:0");
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, foe);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);
            var tm = log.OfType<TypeMatchup>()[0];
            Assert.AreEqual(4, tm.Delta, "불꽃 → 풀 +2, 카드 +2");
            var br = log.OfType<DamageBreakdown>()[0];
            // 결정 194: 계산 줄의 상성 칸은 표 상성(출처 없음) +2와 카드 몫 +2(그 균열 카드)로 나뉜다 — 합은 그대로 4
            var matchups = System.Array.FindAll(br.Steps, x => x.Kind == DamageStepKind.Effectiveness);
            Assert.AreEqual(2, matchups.Length, "표 상성 · 무모한 돌격");
            Assert.AreEqual(2, matchups[0].Value);
            Assert.AreEqual(SourceRef.None, matchups[0].Source, "표 상성은 출처 없음");
            Assert.AreEqual(BreachRules.RecklessBonus, matchups[1].Value);
            Assert.AreEqual(new SourceRef(SourceKind.BreachCard, Card(BreachKind.RecklessAssault)), matchups[1].Source, "카드 몫의 출처 = 무모한 돌격");
            Assert.AreEqual(log.OfType<DiceRolled>()[0].Sum + 4, br.Final);

            // 받는 쪽에 붙어 있어도 같다: 야생 불꽃숭이(우선도 6)가 내 모부기(4)를 먼저 친다
            var s2 = Battles.Game(ctx, 2);
            int grass = Give(s2, 0, "387:0");
            Stick(s2, grass, BreachKind.RecklessAssault, 1);
            int fire = Wild(s2, "390:0");
            var log2 = new EventLog();
            Battles.Wild(s2, ctx, log2, 0, fire);
            var hit = log2.OfType<TypeMatchup>()[0];
            Assert.AreEqual(fire, hit.AttackerId);
            Assert.AreEqual(4, hit.Delta);

            // 유리하지 않은 상성에는 붙지 않는다(물 → 풀)
            var s3 = Battles.Game(ctx, 2);
            int water = Give(s3, 0, "393:0");
            Stick(s3, water, BreachKind.RecklessAssault);
            int turtwig = Wild(s3, "387:0");
            var log3 = new EventLog();
            Battles.Wild(s3, ctx, log3, 0, turtwig);
            Battles.Answer(s3, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("bubble_beam")), log3);
            Assert.AreEqual(TypeChart.Bonus(PokemonType.Water, PokemonType.Grass, PokemonType.Typeless), log3.OfType<TypeMatchup>()[0].Delta);
            Assert.Less(log3.OfType<TypeMatchup>()[0].Delta, 0);
        }

        [Test]
        public void A15_신속한공격_늘먼저가고공격력은3까지()
        {
            // 「The user always goes first but reduces its moves with greater than 3 total attack strength to 3 attack strength.」
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "387:0"); // 우선도 4
            Stick(s, mine, BreachKind.RapidStrikes);
            int foe = Wild(s, "390:0");     // 우선도 6
            s.Pokemon[foe].Status = StatusEffect.Frozen; // 빙결 대상 공격력 +2 → 잎날가르기 3 + 2 = 5
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, foe);
            var order = log.OfType<OrderDecided>()[0];
            Assert.AreEqual(0, order.First, "우선도가 낮아도 먼저");
            Assert.Less(order.Sides[0].Final, order.Sides[1].Final);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("razor_leaf")), log);
            var dice = log.OfType<DiceRolled>()[0];
            Assert.AreEqual(3, dice.Power);
            Assert.AreEqual(3, dice.Faces.Length);
            var last = dice.PowerMods[dice.PowerMods.Length - 1];
            Assert.AreEqual(-2, last.Delta);
            Assert.IsTrue(last.Source.Same(new SourceRef(SourceKind.BreachCard, Card(BreachKind.RapidStrikes))));

            // 3 이하는 그대로 · 둘 다 가졌으면 보통 순서(우선도)
            var s2 = Battles.Game(ctx, 2);
            int a = Give(s2, 0, "387:0");
            int b = Give(s2, 1, "390:0");
            Stick(s2, a, BreachKind.RapidStrikes);
            Stick(s2, b, BreachKind.RapidStrikes);
            var log2 = new EventLog();
            BattleFlow.StartTrainer(s2, ctx, Scene.Fx(s2, log2, 0, ActionKind.Challenge), 0, 1);
            Assert.AreEqual(1, log2.OfType<OrderDecided>()[0].First);
            Battles.Answer(s2, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log2);
            Assert.AreEqual(2, log2.OfType<DiceRolled>()[0].Power);
        }

        [Test]
        public void A15_중력의이끌림_우선도3감소_체력3증가()
        {
            // 「Reduce the initiative of the user by 3 but increase the base health of the user by 3.」
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "390:0"); // 체력 4 · 우선도 6
            Assert.AreEqual(4, PokemonRules.MaxHp(ctx, s.Pokemon[mine]));
            Stick(s, mine, BreachKind.GravitationalDrag);
            Assert.AreEqual(7, PokemonRules.MaxHp(ctx, s.Pokemon[mine]));
            Assert.AreEqual(7, PokemonRules.Hp(ctx, s.Pokemon[mine]));
            int foe = Wild(s, "387:0");     // 우선도 4
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, foe);
            var order = log.OfType<OrderDecided>()[0];
            Assert.AreEqual(3, order.Sides[0].Final);
            Assert.AreEqual(1, order.First, "6 − 3 = 3 < 4");
            Assert.AreEqual(7, log.OfType<SentIn>().Find(e => e.PokemonId == mine).MaxHp);
            // 두 장이면 겹친다 [가정 A52 ⑤]
            Stick(s, mine, BreachKind.GravitationalDrag, 1);
            Assert.AreEqual(10, PokemonRules.MaxHp(ctx, s.Pokemon[mine]));
        }

        [Test]
        public void A15_날렵한뼈대_공격하면1피해를입고교체할수있다()
        {
            // 「When the user attacks, it takes 1 damage, but may switch out afterwards. Does not affect the current turn order.」
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "390:0");
            int bench = Give(s, 0, "393:0");
            Stick(s, mine, BreachKind.NimbleFrame);
            int foe = Wild(s, "399:0"); // 체력 5 — 불꽃세례(2)로는 쓰러지지 않는다
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, foe);
            Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(mine), log);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);
            var self = log.OfType<HpChanged>().Find(e => e.PokemonId == mine);
            Assert.IsNotNull(self, "자기 피해");
            Assert.AreEqual(4, self.Before);
            Assert.AreEqual(3, self.After);
            Assert.IsTrue(self.Source.Same(new SourceRef(SourceKind.BreachCard, Card(BreachKind.NimbleFrame))));
            Assert.AreEqual(self.Index, log.OfType<HpChanged>().Find(e => e.PokemonId == foe).Index, "공격 피해와 함께 적용");
            // 턴 끝 뒤 교체 창(안 함이 첫 답) — 바꿔도 순서는 그대로라 다음은 야생의 차례
            var window = Battles.Only(s, ChoiceKind.SwitchWindow);
            Assert.AreEqual(0, window.Seat);
            Assert.AreEqual(AnswerKind.Decline, window.Default.Kind);
            int orders = log.OfType<OrderDecided>().Count;
            Battles.Answer(s, ctx, window, ChoiceAnswer.Pick(bench), log);
            Assert.AreEqual(bench, s.Battle.Sides[0].Active);
            Assert.AreEqual(orders, log.OfType<OrderDecided>().Count, "순서를 다시 정하지 않는다");
            Assert.IsTrue(log.OfType<MoveUsed>().Exists(e => e.PokemonId == foe && e.TargetId == bench), "야생이 이어서 친다");
        }

        [Test]
        public void A15_크로마틱유니온_기술이타입없음이되고공격력1증가()
        {
            // 「The moves of the user are now Typeless, but their attack strengths are increased by 1.」 — 한국어 카드는 「위력 1 증가」로 옮겼다(rulings A15)
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "390:0");
            Stick(s, mine, BreachKind.ChromaticUnion);
            int foe = Wild(s, "387:0");
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, foe);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);
            Assert.AreEqual(PokemonType.Typeless, log.OfType<MoveSelected>()[0].Type);
            var dice = log.OfType<DiceRolled>()[0];
            Assert.AreEqual(3, dice.Power, "불꽃세례 2 + 1");
            Assert.AreEqual(PokemonType.Typeless, dice.Type);
            var tm = log.OfType<TypeMatchup>()[0];
            Assert.IsTrue(tm.Typeless);
            Assert.AreEqual(0, tm.Delta, "불꽃 → 풀 +2가 없다");
        }

        [Test]
        public void A15_상태과부하_나올때상태주사위를굴려양쪽에건다()
        {
            // 「When the user switches in, roll a status dice and inflict the result on both the user and the target.」
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "390:0");
            Stick(s, mine, BreachKind.StatusOverload);
            int foe = Wild(s, "399:0");
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, foe);
            var applied = log.OfType<StatusApplied>();
            Assert.AreEqual(2, applied.Count, "첫 출전: 상대가 나온 뒤에 한 번");
            Assert.AreEqual(mine, applied[0].PokemonId);
            Assert.AreEqual(foe, applied[1].PokemonId);
            Assert.AreEqual(applied[0].Status, applied[1].Status, "주사위 하나의 결과를 둘 다에게");
            Assert.AreNotEqual(StatusEffect.None, applied[0].Status);
            foreach (var e in applied)
            {
                Assert.IsTrue(e.Source.Same(new SourceRef(SourceKind.BreachCard, Card(BreachKind.StatusOverload))));
                Assert.AreEqual(e.Result == StatusResult.Applied, s.Pokemon[e.PokemonId].Status == e.Status);
            }
            Assert.Greater(applied[1].Index, log.OfType<SentIn>().Find(e => e.PokemonId == foe).Index, "상대가 나온 뒤");
            Assert.IsFalse(s.Battle.Sides[0].Entered);
            // 카드가 없는 포켓몬이 나올 때는 굴리지 않는다
            var s2 = Battles.Game(ctx, 2);
            Give(s2, 0, "390:0");
            var log2 = new EventLog();
            Battles.Wild(s2, ctx, log2, 0, Wild(s2, "399:0"));
            Assert.AreEqual(0, log2.OfType<StatusApplied>().Count);
        }

        // ── 붙이기 ──

        /// <summary>나무·산호·사당 토큰 n개를 파괴해 둔다(균열 = n/131).</summary>
        static void Destroy(GameState s, int n)
        {
            foreach (var t in s.Tokens)
            {
                if (n == 0) break;
                if (!UbTurn.IsTarget(t.Kind) || t.Cell < 0 || t.Destroyed) continue;
                t.Destroyed = true;
                n--;
            }
            Assert.AreEqual(0, n);
        }

        static List<PendingChoice> AttachGroup(GameState s) =>
            s.Pending.FindAll(c => c.Kind == ChoiceKind.Simultaneous && c.Sub == ChoiceSub.BreachAttach);

        [Test]
        public void S17_5_균열카드는모든좌석이한꺼번에붙인다()
        {
            // p.24 「trainers will be dealt a random Distortion Breach card that they must attach to a Pokémon in their party」
            var log = new EventLog();
            var s = UbScenes.Started(ctx, 2, 3, log);
            int cards = Playout.CardCount(s);
            Destroy(s, 12); // 9% → 네크로즈마 1개 → 10%
            log.Clear();
            UbScenes.EndRound(s, ctx, log);
            Assert.AreEqual(1, Flow.Of<BreachCardsDealt>(log).Count);
            var group = AttachGroup(s);
            Assert.AreEqual(2, group.Count, "좌석마다 하나");
            Assert.AreEqual(group.Count, s.Pending.Count, "붙이기가 끝나야 맵 행동이 열린다");
            Assert.AreEqual(group[0].GroupId, group[1].GroupId);
            Assert.IsFalse(group[0].Secret);
            for (int seat = 0; seat < 2; seat++)
            {
                var c = group[seat];
                Assert.AreEqual(seat, c.Seat);
                Assert.AreEqual(UbTurn.BreachHand(s, seat)[0], c.Options[0]);
                var legal = Scene.Legal(s, ctx, c);
                int starter = s.Seats[seat].Party[0];
                Assert.AreEqual(2, legal.Count, "스타터 한 마리 × 기술 칸 둘 — 「안 함」은 없다(강제)");
                Assert.IsTrue(legal[0].SameAs(ChoiceAnswer.Pick(starter, 0)));
                Assert.IsTrue(legal[1].SameAs(ChoiceAnswer.Pick(starter, 1)));
                Assert.IsTrue(c.Default.SameAs(legal[0]));
            }
            // 한 좌석만 답하면 기다린다
            int card0 = group[0].Options[0], card1 = group[1].Options[0];
            int p0 = s.Seats[0].Party[0], p1 = s.Seats[1].Party[0];
            log.Clear();
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(group[0].Id, ChoiceAnswer.Pick(p0, 1)), log));
            Assert.AreEqual(0, Flow.Of<BreachCardAttached>(log).Count);
            Assert.AreEqual(-1, s.Pokemon[p0].Breach[1]);
            Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(group[1].Id, ChoiceAnswer.Pick(p1, 0)), log));
            var attached = Flow.Of<BreachCardAttached>(log);
            Assert.AreEqual(2, attached.Count);
            Assert.AreEqual(0, attached[0].Seat);
            Assert.AreEqual(card0, attached[0].CardKey);
            Assert.AreEqual(1, attached[0].Slot);
            Assert.AreEqual(-1, attached[0].ReplacedKey);
            Assert.AreEqual(card0, s.Pokemon[p0].Breach[1]);
            Assert.AreEqual(card1, s.Pokemon[p1].Breach[0]);
            Assert.AreEqual(0, s.Ub.BreachHands.Count);
            Assert.AreEqual(cards, Playout.CardCount(s), "카드 총수 보존(덱 36장)");
            Assert.AreEqual(s.ActiveSeat, Flow.Only(s, ChoiceKind.MapAction).Seat, "붙인 뒤 그 턴의 맵 행동");
            Assert.AreEqual(2, s.Round);
        }

        [Test]
        public void S17_5_빈칸이없으면기술을버리고붙이고_모두균열카드면손에든다()
        {
            // 원작 커뮤니티 확인 [가정 A52 ②]
            // p.24 「If it is not possible to attach the card when it is dealt, keep the card in your hand and attach it as soon as possible」
            var s = UbScenes.Started(ctx, 1, 3);
            int p = s.Seats[0].Party[0];
            var pk = s.Pokemon[p];
            pk.Moves[0] = Mv("tackle");
            pk.Moves[1] = Mv("ember");
            var deck = s.Decks[(int)DeckId.DistortionBreach];
            void Deal()
            {
                s.Ub.BreachHands.Add(0);
                s.Ub.BreachHands.Add(deck[deck.Count - 1]);
                deck.RemoveAt(deck.Count - 1);
            }
            for (int i = 0; i < 3; i++) Deal();
            int cards = Playout.CardCount(s);
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log); // 혼자: 라운드 끝 → 건너뛰기 물음 → UB 턴 → 다음 턴 시작
            UbScenes.NoSkip(s, ctx, log);
            var c = Battles.Only(s, ChoiceKind.Simultaneous, ChoiceSub.BreachAttach);
            Assert.AreEqual(2, Scene.Legal(s, ctx, c).Count, "가르친 기술이 있는 칸도 고를 수 있다");
            int first = c.Options[0];
            Battles.Answer(s, ctx, c, ChoiceAnswer.Pick(p, 0), log);
            var a = Flow.Of<BreachCardAttached>(log)[0];
            Assert.AreEqual(Mv("tackle"), a.ReplacedKey, "그 칸의 기술을 버린다");
            Assert.AreEqual(-1, pk.Moves[0]);
            Assert.AreEqual(first, pk.Breach[0]);
            // 남은 손 카드로 바로 다시 묻는다 — 균열 카드가 붙은 칸은 고를 수 없다
            c = Battles.Only(s, ChoiceKind.Simultaneous, ChoiceSub.BreachAttach);
            var legal = Scene.Legal(s, ctx, c);
            Assert.AreEqual(1, legal.Count);
            Assert.IsTrue(legal[0].SameAs(ChoiceAnswer.Pick(p, 1)));
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), log));
            Assert.AreEqual(-1, pk.Moves[1]);
            Assert.AreEqual(2, BreachRules.Attached(pk));
            // 셋째 카드는 붙일 칸이 없어 손에 남고 턴이 이어진다
            Assert.IsFalse(BreachRules.CanAttach(s, 0));
            Assert.AreEqual(1, UbTurn.BreachHand(s, 0).Count);
            Flow.Only(s, ChoiceKind.MapAction);
            Assert.AreEqual(cards, Playout.CardCount(s));
            // 붙일 칸이 생기면(새 포켓몬) 다음 턴 시작에 붙인다
            int fresh = Give(s, 0, "399:0");
            s.Pokemon[fresh].Moves[1] = Mv("tackle");
            cards = Playout.CardCount(s); // 새 포켓몬 카드 한 장
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
            UbScenes.NoSkip(s, ctx, log); // 라운드 끝 건너뛰기 물음(결정 84 U2)
            c = Battles.Only(s, ChoiceKind.Simultaneous, ChoiceSub.BreachAttach);
            Assert.IsTrue(c.Default.SameAs(ChoiceAnswer.Pick(fresh, 0)), "시간 초과 기본 답 = 빈 칸 먼저(가르친 기술을 버리지 않는다)");
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), log));
            Assert.AreEqual(0, s.Ub.BreachHands.Count);
            Assert.AreEqual(Mv("tackle"), s.Pokemon[fresh].Moves[1]);
            Assert.AreEqual(cards, Playout.CardCount(s));
        }

        [Test]
        public void A52_균열카드는기술칸을막는다()
        {
            // 원작 커뮤니티 확인
            var s = UbScenes.Started(ctx, 1, 3);
            int seat = 0;
            int p = s.Seats[seat].Party[0];
            var pk = s.Pokemon[p];
            pk.Breach[0] = Card(BreachKind.NimbleFrame);
            int typeless = -1;
            for (int m = 0; m < ctx.Cards.Moves.Count && typeless < 0; m++)
                if (ctx.Cards.Moves[m].Type == PokemonType.Typeless) typeless = m;
            Assert.GreaterOrEqual(typeless, 0);
            var teach = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.TeachMove, Seat = seat, Subject = typeless, Window = Window.BattleEnd };
            var legal = Scene.Legal(s, ctx, teach);
            Assert.AreEqual(2, legal.Count, "안 가르침 + 남은 칸 하나");
            Assert.IsTrue(legal[1].SameAs(ChoiceAnswer.Pick(p, 1)));
            // 정비로도 그 칸은 못 쓴다
            s.Seats[seat].StampMoves.Add(typeless);
            Scene.Reject(Engine.Submit(s, ctx, seat, Command.Anytime(new Maintenance { Kind = MaintKind.SetMove, Pokemon = p, Slot = 0, Value = typeless }), null), RejectReason.Illegal);
            Scene.Accept(Engine.Submit(s, ctx, seat, Command.Anytime(new Maintenance { Kind = MaintKind.SetMove, Pokemon = p, Slot = 1, Value = typeless }), null));
            // 전설 특수기술: 첫 카드가 알파 스트라이크를, 둘째가 비스트 부스트를 덮는다(원작 커뮤니티 확인)
            var legend = new PokemonState { Def = pk.Def, HasLegendaryMove = true };
            Assert.IsTrue(LegendRules.HasAlphaStrike(legend));
            legend.Breach[1] = Card(BreachKind.RapidStrikes);
            Assert.IsFalse(LegendRules.HasAlphaStrike(legend));
            Assert.IsTrue(LegendRules.HasBeastBoost(legend));
            legend.Breach[0] = Card(BreachKind.RapidStrikes);
            Assert.IsFalse(LegendRules.HasBeastBoost(legend));
        }

        [Test]
        public void S17_5_균열카드가붙은포켓몬은파티를떠나지못한다()
        {
            // p.24 「Any action that leads to a Distortion Breach card leaving your party is not allowed.」
            var s = UbScenes.Started(ctx, 1, 3);
            int a = s.Seats[0].Party[0];
            int b = Give(s, 0, "399:0");
            s.Pokemon[a].Breach[0] = Card(BreachKind.ChromaticUnion);
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.Anytime(new Maintenance { Kind = MaintKind.Release, Pokemon = a, Value = s.Pokemon[a].Def }), null), RejectReason.Illegal);
            // 파티 가득 놓아주기에도 나오지 않는다
            int caught = Wild(s, "396:0");
            var full = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.PartyFull, Seat = 0, Subject = caught, Window = Window.BattleEnd };
            var legal = Scene.Legal(s, ctx, full);
            Assert.AreEqual(2, legal.Count);
            Assert.IsFalse(legal.Exists(x => x.Target == a));
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.Anytime(new Maintenance { Kind = MaintKind.Release, Pokemon = b, Value = s.Pokemon[b].Def }), null));
        }

        [Test]
        public void S17_5_UB를쓰러뜨리면모두한장을버리거나자기파티안에서옮긴다()
        {
            // p.24 「when an Ultra Beast is defeated, all trainers may discard or move 1 Distortion Breach card」 · 원작 커뮤니티 확인
            var s = UbScenes.Started(ctx, 3, 3);
            s.Pending.Clear(); // 장면: 배틀이 막 끝난 자리(맵 행동은 닫혔다)
            int active = s.ActiveSeat;
            int a0 = s.Seats[0].Party[0], a1 = s.Seats[1].Party[0];
            int b0 = Give(s, 0, "399:0");
            s.Pokemon[b0].Moves[0] = Mv("tackle");
            var deck = s.Decks[(int)DeckId.DistortionBreach];
            int Draw()
            {
                int card = deck[deck.Count - 1];
                deck.RemoveAt(deck.Count - 1);
                return card;
            }
            int c0 = Draw(), c1 = Draw();
            s.Pokemon[a0].Breach[1] = c0;
            s.Pokemon[a1].Breach[0] = c1;
            int cards = Playout.CardCount(s);
            var log = new EventLog();
            Assert.IsTrue(BreachRules.OpenRelease(s, ctx, Scene.Fx(s, log, active, ActionKind.BattleAction), active, new List<int>()));
            var group = AttachGroup(s);
            Assert.AreEqual(2, group.Count, "균열 카드가 붙은 좌석만(좌석 2는 없다)");
            var legal0 = Scene.Legal(s, ctx, group[0]);
            Assert.AreEqual(AnswerKind.Decline, legal0[0].Kind, "「may」 — 안 함이 첫 답·기본");
            Assert.IsTrue(group[0].Default.SameAs(legal0[0]));
            Assert.AreEqual(4, legal0.Count, "안 함 · 버리기 · 다른 포켓몬의 두 칸으로 옮기기");
            Assert.IsTrue(legal0[1].SameAs(ChoiceAnswer.Pick(a0, 1)));
            var legal1 = Scene.Legal(s, ctx, group[1]);
            Assert.AreEqual(2, legal1.Count, "파티에 한 마리뿐이면 버리기만 — 남의 파티로는 못 옮긴다");
            // 좌석 0: 가르친 기술이 있는 칸으로 옮긴다(그 기술은 버린다) · 좌석 1: 버린다
            var move = ChoiceAnswer.Pick(a0, 1);
            move.Values.Add(b0);
            move.Values.Add(0);
            Assert.IsTrue(legal0.Exists(x => x.SameAs(move)));
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(group[0].Id, move), log));
            Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(group[1].Id, ChoiceAnswer.Pick(a1, 0)), log));
            var moved = Flow.Of<BreachCardMoved>(log)[0];
            Assert.AreEqual(c0, moved.CardKey);
            Assert.AreEqual(Mv("tackle"), moved.ReplacedKey);
            Assert.AreEqual(-1, s.Pokemon[a0].Breach[1]);
            Assert.AreEqual(c0, s.Pokemon[b0].Breach[0]);
            Assert.AreEqual(-1, s.Pokemon[b0].Moves[0]);
            var gone = Flow.Of<BreachCardDiscarded>(log)[0];
            Assert.AreEqual(1, gone.Seat);
            Assert.AreEqual(c1, gone.CardKey);
            Assert.IsFalse(BreachRules.Any(s.Pokemon[a1]));
            CollectionAssert.Contains(s.Discards[(int)DeckId.DistortionBreach], c1);
            Assert.AreEqual(cards, Playout.CardCount(s), "카드 총수 보존");
            // 배틀 뒤처럼 턴이 끝나고 다음 좌석의 턴이 열린다
            Assert.AreEqual(1, Flow.Of<TurnEnded>(log).Count);
            Assert.AreNotEqual(active, Flow.Only(s, ChoiceKind.MapAction).Seat);
            // 붙은 카드가 아무에게도 없으면 묻지 않는다
            var none = UbScenes.Started(ctx, 2, 3);
            Assert.IsFalse(BreachRules.OpenRelease(none, ctx, Scene.Fx(none, null, 0, ActionKind.BattleAction), 0, new List<int>()));
        }

        [Test]
        public void S17_5_UB를쓰러뜨리면손에든카드와보관한카드도버릴수있다_옮기기는붙은카드만_결정84()
        {
            // 사용자 결정 84 U10 나(「discard or move 1 Distortion Breach card」은 붙은 카드를 가리지 않는다) · U11(보관한 카드도 버리기 대상)
            var s = UbScenes.Started(ctx, 2, 3);
            s.Pending.Clear();
            int active = s.ActiveSeat;
            var deck = s.Decks[(int)DeckId.DistortionBreach];
            int Draw()
            {
                int card = deck[deck.Count - 1];
                deck.RemoveAt(deck.Count - 1);
                return card;
            }
            int hand = Draw(), stored = Draw();
            s.Ub.BreachHands.AddRange(new[] { 0, hand });
            s.Ub.BreachStored.AddRange(new[] { 1, stored });
            int cards = Playout.CardCount(s);
            var log = new EventLog();
            Assert.IsTrue(BreachRules.OpenRelease(s, ctx, Scene.Fx(s, log, active, ActionKind.BattleAction), active, new List<int>()), "붙은 카드가 없어도 손·보관 카드가 있으면 묻는다");
            var group = AttachGroup(s);
            Assert.AreEqual(2, group.Count);
            var l0 = Scene.Legal(s, ctx, group[0]);
            CollectionAssert.AreEqual(new[] { AnswerKind.Decline, AnswerKind.Pick }, l0.ConvertAll(a => a.Kind), "안 함 · 손의 카드 버리기(옮기기 없음)");
            Assert.IsTrue(l0[1].SameAs(ChoiceAnswer.Pick(BreachRules.HandTarget, hand)));
            Assert.AreEqual(0, l0[1].Values.Count);
            var l1 = Scene.Legal(s, ctx, group[1]);
            Assert.IsTrue(l1[1].SameAs(ChoiceAnswer.Pick(BreachRules.HandTarget, stored)), "보관한 카드도");
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(group[0].Id, l0[1]), log));
            Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(group[1].Id, l1[1]), log));
            Assert.AreEqual(0, UbTurn.BreachHand(s, 0).Count);
            Assert.AreEqual(0, BreachRules.StoredCount(s, 1));
            var gone = Flow.Of<BreachCardDiscarded>(log);
            Assert.AreEqual(2, gone.Count);
            Assert.AreEqual(-1, gone[0].PokemonId);
            Assert.AreEqual(hand, gone[0].CardKey);
            CollectionAssert.IsSubsetOf(new[] { hand, stored }, s.Discards[(int)DeckId.DistortionBreach]);
            Assert.AreEqual(cards, Playout.CardCount(s), "카드 총수 보존");
        }
    }
}
