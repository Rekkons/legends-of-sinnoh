using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 울트라 온슬롯(실험 선택 규칙, rulings A21-1·A21-2 · A56 — M6 「울트라 온슬롯」). 실제 울트라 버스트 판(옵션 켬)에서 좌석을 UB 토큰 칸에 세우고 찾아보기로 배틀을 연다.
    /// 뽑을 카드는 덱 맨 위에 순서대로 놓아 정하고(<see cref="Stack"/>), 주사위가 갈리는 장면은 게임 난수를 다시 심어 원하는 결과가 나오는 시드를 고른다(결정적).
    /// 누가 먼저 움직이든 같은 장면이 되게, 손볼 것이 있는 장면은 첫 카드를 해가 없는 것(3)으로 두고 내 차례에 손본 뒤 둘째 카드를 본다(<see cref="Second"/>).
    /// </summary>
    public class UbOnslaughtTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const string Foe = "797:0";  // 철화구야: 체력 21 · 시그니처 lift_off(강철)
        const string Mine = "799:0"; // 악식킹: 체력 23 · 시그니처 chomp(악)

        int Mv(string key) => Scene.Move(ctx, key);

        sealed class Run
        {
            public GameState S;
            public EventLog Log = new EventLog();
            public int Seat, Tank, Token, Ub;
            /// <summary>손본 뒤(둘째 카드부터)의 이벤트가 시작하는 자리.</summary>
            public int Mark;

            public List<T> After<T>() where T : GameEvent
            {
                var r = new List<T>();
                for (int i = Mark; i < Log.Events.Count; i++)
                    if (Log.Events[i] is T t) r.Add(t);
                return r;
            }
        }

        /// <summary>울트라 버스트 판(혼자 · 온슬롯 on) — 차례 좌석의 파티를 악식킹 한 마리로 바꾸고(keepStarter면 스타터 뒤에 둔다) 철화구야 토큰 칸에 세운다.</summary>
        Run Game(bool on = true, bool keepStarter = false, ulong seed = 3)
        {
            var r = new Run();
            var s = r.S = UbScenes.Started(ctx, 1, seed, null, new RuleOptions { UltraOnslaught = on });
            r.Seat = s.ActiveSeat;
            int starter = s.Seats[r.Seat].Party[0];
            r.Tank = Scene.Give(s, ctx, r.Seat, Mine);
            if (!keepStarter) MaintenanceRules.Release(s, ctx, Scene.Fx(s, null, r.Seat, ActionKind.Release), r.Seat, starter);
            int def = Scene.Def(ctx, Foe);
            r.Token = s.Tokens.FindIndex(t => t.Kind == TokenKind.UltraBeast && t.Def == def && t.Cell >= 0);
            Assert.GreaterOrEqual(r.Token, 0, "UB 토큰");
            s.Seats[r.Seat].Cell = s.Tokens[r.Token].Cell;
            return r;
        }

        /// <summary>온슬롯 덱 맨 위에 그 번호의 카드를 그 순서로 뽑히게 놓는다(끝 = 맨 위).</summary>
        void Stack(GameState s, params int[] numbers)
        {
            var deck = s.Decks[(int)DeckId.UltraOnslaught];
            Assert.AreEqual(UbOnslaught.Cards, deck.Count, "셋업 뒤 온슬롯 덱 6장");
            for (int i = numbers.Length - 1; i >= 0; i--)
            {
                int card = UbOnslaught.CardOf(ctx, numbers[i]);
                Assert.IsTrue(deck.Remove(card), $"온슬롯 카드 {numbers[i]}");
                deck.Add(card);
            }
        }

        void Fight(Run r)
        {
            var search = ChoiceAnswer.Of(AnswerKind.Search, r.Token);
            Flow.Answer(r.S, ctx, ChoiceKind.MapAction, search, r.Log);
            if (r.S.Battle.Sides[0].Active < 0) Battles.Answer(r.S, ctx, ChoiceKind.SendIn, Scene.Legal(r.S, ctx, Battles.Only(r.S, ChoiceKind.SendIn))[0], r.Log);
            r.Ub = r.S.Battle.Sides[1].Roster[0];
        }

        bool MyTurn(Run r) => r.S.Pending.Exists(c => !c.Answered && c.Kind == ChoiceKind.BattleAction && c.Seat == r.Seat);

        static int Drawn(Run r) => r.Log.OfType<OnslaughtDrawn>().Count;

        /// <summary>stop이 참이거나 배틀이 끝날 때까지 답한다: 내 배틀 행동은 출전 포켓몬의 첫 기술(시그니처), 그 밖(UB · 창 · 교체)은 기본 답.</summary>
        void Drive(Run r, Func<bool> stop)
        {
            var s = r.S;
            for (int i = 0; i < 600; i++)
            {
                if (s.Battle == null || stop()) return;
                var c = s.Pending.Find(x => !x.Answered);
                Assert.IsNotNull(c, "열린 선택 없이 멈췄다");
                var a = c.Default.Clone();
                if (c.Kind == ChoiceKind.BattleAction && c.Seat == r.Seat) a = Scene.Legal(s, ctx, c).Find(x => x.Kind == AnswerKind.UseMove && x.Arg < 0).Clone();
                Battles.Answer(s, ctx, c, a, r.Log);
            }
            Assert.Fail("끝나지 않는다");
        }

        /// <summary>첫 카드 = first를 뽑은 UB 턴이 지난 내 차례에서 멈춘다(누가 먼저 움직이든).</summary>
        Run First(ulong seed, int first, Action<Run> setup = null, bool keepStarter = false, params int[] then)
        {
            var r = Game(true, keepStarter);
            var order = new List<int> { first };
            order.AddRange(then);
            Stack(r.S, order.ToArray());
            setup?.Invoke(r);
            r.S.Rng.CopyFrom(new Pcg32(seed));
            Fight(r);
            Drive(r, () => Drawn(r) >= 1 && MyTurn(r));
            return r;
        }

        /// <summary>첫 카드 3(해 없음) 뒤 내 차례에 prepare로 손보고, 둘째 카드 = card를 뽑은 UB 턴이 지난 내 차례(또는 배틀 끝)까지.</summary>
        Run Second(ulong seed, int card, Action<Run> prepare)
        {
            var r = First(seed, card == UbOnslaught.Boost ? UbOnslaught.Thorns : UbOnslaught.Boost, null, false, card);
            if (r.S.Battle == null) return r;
            prepare?.Invoke(r);
            r.Mark = r.Log.Events.Count;
            Drive(r, () => Drawn(r) >= 2 && MyTurn(r));
            return r;
        }

        T Seeded<T>(Func<ulong, T> run, Predicate<T> ok) where T : class
        {
            for (ulong seed = 0; seed < 300; seed++)
            {
                var r = run(seed);
                if (ok(r)) return r;
            }
            Assert.Fail("원하는 주사위 결과가 나오는 시드가 없다");
            return null;
        }

        [Test]
        public void A21_1_카드1_공격하지않고4회복_상태이상을모두회복()
        {
            // 「Do not attack this turn. Heal 4 damage and all status effects.」 — 상태이상을 먼저 풀어 독이 회복을 막지 않는다(rulings A56 ②)
            var r = Second(1, UbOnslaught.Rest, x =>
            {
                x.S.Pokemon[x.Ub].Damage = 5;
                x.S.Pokemon[x.Ub].Status = StatusEffect.Poisoned;
            });
            var s = r.S;
            Assert.IsNotNull(s.Battle, "배틀이 이어진다");
            var drawn = r.After<OnslaughtDrawn>();
            Assert.AreEqual(1, drawn.Count);
            Assert.AreEqual(UbOnslaught.Rest, drawn[0].Effect);
            Assert.AreEqual(UbOnslaught.CardOf(ctx, UbOnslaught.Rest), drawn[0].CardKey);
            Assert.AreEqual(SourceKind.Onslaught, drawn[0].Source.Kind);
            int at = r.Log.Events.IndexOf(drawn[0]);
            for (int i = at; i < r.Log.Events.Count; i++)
            {
                Assert.IsFalse(r.Log.Events[i] is DiceRolled d && d.PokemonId == r.Ub, "그 턴에 UB는 굴리지 않는다");
                Assert.IsFalse(r.Log.Events[i] is StatusTicked t && t.PokemonId == r.Ub, "독은 이미 풀렸다");
            }
            var healed = r.After<Healed>().Find(e => e.Source.Kind == SourceKind.Onslaught);
            Assert.AreEqual(StatusEffect.Poisoned, healed.Pokemon[0].StatusBefore);
            Assert.AreEqual(StatusEffect.None, s.Pokemon[r.Ub].Status);
            var hp = r.After<HpChanged>().Find(e => e.PokemonId == r.Ub && e.Source.Kind == SourceKind.Onslaught);
            Assert.AreEqual(UbOnslaught.RestHeal, hp.After - hp.Before, "4 회복");
            Assert.AreEqual(-1, s.Pokemon[r.Ub].LastMove, "직전 기술 없음");
        }

        [Test]
        public void A21_1_카드2_상태심볼은모두크리티컬_졸림이만든심볼도()
        {
            // 「Replace all status symbols with critical hits.」 · 졸림(일반 타격 1 → 심볼)이 먼저 돌고 그 심볼까지 크리티컬(원작 커뮤니티 확인)
            var r = Seeded(seed => Second(seed, UbOnslaught.Crits, x => x.S.Pokemon[x.Ub].Status = StatusEffect.Drowsy), x =>
            {
                if (x.S.Battle == null) return false;
                var roll = x.After<DiceRolled>().Find(e => e.PokemonId == x.Ub);
                return roll != null && Array.IndexOf(roll.Faces, AttackDieFace.RegularHit) >= 0 && Array.IndexOf(roll.Faces, AttackDieFace.StatusSymbol) >= 0;
            });
            var rolled = r.After<DiceRolled>().Find(e => e.PokemonId == r.Ub);
            int symbols = AttackDice.Count(rolled.Faces, AttackDieFace.StatusSymbol), crits = AttackDice.Count(rolled.Faces, AttackDieFace.CriticalHit), hits = AttackDice.Count(rolled.Faces, AttackDieFace.RegularHit);
            var changes = r.After<DiceFacesChanged>();
            int drowsy = changes.FindIndex(e => e.Source.Kind == SourceKind.Status && e.Source.Id == (int)StatusEffect.Drowsy);
            int card = changes.FindIndex(e => e.Source.Kind == SourceKind.Onslaught);
            Assert.GreaterOrEqual(drowsy, 0, "졸림의 면 바꾸기");
            Assert.Greater(card, drowsy, "카드 2는 다른 면 바꾸기 뒤");
            Assert.AreEqual(UbOnslaught.CardOf(ctx, UbOnslaught.Crits), changes[card].Source.Id);
            Assert.AreEqual(symbols + 1, changes[card].Changes.Length, "굴린 심볼 + 졸림이 만든 심볼");
            CollectionAssert.AreEqual(new[] { 0, hits - 1, crits + symbols + 1 }, changes[card].SrcAfter);
            var dmg = r.After<DamageBreakdown>().Find(e => e.TargetId == r.Tank);
            Assert.AreEqual((hits - 1) + 2 * (crits + symbols + 1), Array.Find(dmg.Steps, x => x.Kind == DamageStepKind.Dice).Value, "피해의 주사위 합은 바뀐 면으로");
        }

        [Test]
        public void A21_1_카드3_비스트부스트즉시_이미있으면더한다()
        {
            // 「Immediately trigger the effects of Beast Boost. If Beast Boost is already active, add to its effect.」 — 피로 −3 · 다음 공격 +1(쌓인다)
            var r = Second(1, UbOnslaught.Boost, x =>
            {
                x.S.Pokemon[x.Ub].Fatigue = 4;
                x.S.Battle.Sides[1].BeastBoost = 1; // 이미 켜져 있다
            });
            Assert.IsNotNull(r.S.Battle, "배틀이 이어진다");
            Assert.AreEqual(UbOnslaught.Boost, r.After<OnslaughtDrawn>()[0].Effect);
            var fatigue = r.After<FatigueChanged>().Find(e => e.PokemonId == r.Ub && e.Cause == FatigueCause.BeastBoost);
            Assert.AreEqual(SourceKind.Onslaught, fatigue.Source.Kind);
            Assert.AreEqual(4, fatigue.Before);
            Assert.AreEqual(4 - LegendRules.BeastBoostFatigue, fatigue.After);
            var roll = r.After<DiceRolled>().Find(e => e.PokemonId == r.Ub);
            var boost = Array.Find(roll.PowerMods, m => m.Source.Same(SourceRef.Rule(RuleSource.BeastBoost)));
            Assert.AreEqual(2, boost.Delta, "있던 1 + 카드의 1");
            Assert.AreEqual(0, r.S.Battle.Sides[1].BeastBoost, "그 공격에 쓰고 사라진다");
        }

        [Test]
        public void A21_1_카드4_다음에공격받으면일반타격만큼공격자도()
        {
            // 「When next attacked, all regular hits damage the attacker as well.」 — 다음 공격 한 번
            Run Scene4(ulong seed)
            {
                var x = First(seed, UbOnslaught.Thorns);
                if (x.S.Battle == null) return x;
                x.Mark = x.Log.Events.Count;
                Drive(x, () => x.After<DamageBreakdown>().Exists(e => e.TargetId == x.Ub) && MyTurn(x));
                return x;
            }
            var r = Seeded(Scene4, x =>
            {
                if (x.S.Battle == null) return false;
                var roll = x.After<DiceRolled>().Find(e => e.PokemonId == x.Tank);
                return roll != null && Array.IndexOf(roll.Faces, AttackDieFace.RegularHit) >= 0 && !x.After<DiceFacesChanged>().Exists(e => true);
            });
            var s = r.S;
            var mine = r.After<DiceRolled>().Find(e => e.PokemonId == r.Tank);
            int hits = AttackDice.Count(mine.Faces, AttackDieFace.RegularHit);
            var back = r.After<HpChanged>().FindAll(e => e.PokemonId == r.Tank && e.Source.Kind == SourceKind.Onslaught);
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual(hits, back[0].Before - back[0].After, "일반 타격 하나마다 1");
            Assert.AreEqual(UbOnslaught.CardOf(ctx, UbOnslaught.Thorns), back[0].Source.Id);
            Assert.IsFalse(s.Battle.OnslaughtThorns, "한 번 쓰면 끝");
            // 그다음 공격에는 돌아오지 않는다
            int attacks = r.Log.OfType<DamageBreakdown>().FindAll(e => e.TargetId == r.Ub).Count;
            Drive(r, () => r.Log.OfType<DamageBreakdown>().FindAll(e => e.TargetId == r.Ub).Count > attacks && MyTurn(r));
            Assert.AreEqual(1, r.Log.OfType<OnslaughtDrawn>().FindAll(e => e.Effect == UbOnslaught.Thorns).Count, "카드 4는 버림 더미에 있다");
            Assert.AreEqual(1, r.Log.OfType<HpChanged>().FindAll(e => e.PokemonId == r.Tank && e.Source.Kind == SourceKind.Onslaught && e.Source.Id == back[0].Source.Id).Count);
        }

        [Test]
        public void A21_1_카드5_알파스트라이크두번_둘째는공격력2_쓰러지면다음포켓몬에게()
        {
            // 「Use Alpha Strike twice this turn.」 · 원작 커뮤니티 확인
            var r = First(1, UbOnslaught.TwinStrike);
            int at = r.Log.Events.FindIndex(e => e is OnslaughtDrawn);
            var picks = new List<MoveSelected>();
            var rolls = new List<DiceRolled>();
            for (int i = at; i < r.Log.Events.Count; i++)
            {
                if (r.Log.Events[i] is MoveSelected m && m.Side == 1) picks.Add(m);
                if (r.Log.Events[i] is DiceRolled d && d.PokemonId == r.Ub) rolls.Add(d);
            }
            Assert.AreEqual(2, picks.Count, "그 턴에 두 번");
            Assert.AreEqual(BattleRules.AlphaStrike, picks[0].MoveKey);
            Assert.AreEqual(BattleRules.AlphaStrike, picks[1].MoveKey);
            Assert.AreEqual(2, rolls.Count);
            Assert.AreEqual(BattleRules.AlphaStrikeStrength, rolls[0].Power);
            Assert.AreEqual(BattleRules.AlphaStrikeRepeatStrength, rolls[1].Power, "둘째는 2");

            // 첫 공격으로 쓰러지면 둘째는 다음 포켓몬에게(UB에게 유리하게 — rulings A56 ⑥)
            var k = Seeded(seed => First(seed, UbOnslaught.TwinStrike, x => Battles.SetHp(x.S, ctx, x.S.Seats[x.Seat].Party[0], 1), true), x =>
            {
                int starter = x.S.Seats[x.Seat].Party[0];
                var hits = x.Log.OfType<DamageBreakdown>().FindAll(e => e.TargetId == starter || e.TargetId == x.Tank);
                return x.Log.OfType<Fainted>().Exists(e => e.PokemonId == starter) && hits.Count >= 2 && hits[0].TargetId == starter && hits[0].Final >= 1;
            });
            int first = k.S.Seats[k.Seat].Party[0];
            var targets = k.Log.OfType<DamageBreakdown>().FindAll(e => e.TargetId == first || e.TargetId == k.Tank);
            Assert.AreEqual(first, targets[0].TargetId);
            Assert.AreEqual(k.Tank, targets[1].TargetId, "둘째 알파 스트라이크는 다음 포켓몬에게");
            var ubRolls = k.Log.OfType<DiceRolled>().FindAll(e => e.PokemonId == k.Ub);
            Assert.AreEqual(BattleRules.AlphaStrikeRepeatStrength + LegendRules.BeastBoostPower, ubRolls[1].Power, "둘째 2 + 방금 쓰러뜨려 켜진 비스트 부스트 1");
        }

        [Test]
        public void A21_1_카드6_균열카드가붙은상대에게2배()
        {
            // 「Deal double damage to the opposing Pokémon if they have any Distortion Breach cards attached.」
            Run Hit(bool breach) => First(1, UbOnslaught.Rend, x =>
            {
                if (breach) x.S.Pokemon[x.Tank].Breach[1] = BreachRules.CardOf(ctx, BreachKind.NimbleFrame);
            });
            var r = Hit(true);
            int at = r.Log.Events.FindIndex(e => e is OnslaughtDrawn);
            var dmg = (DamageBreakdown)r.Log.Events.Find(e => e is DamageBreakdown d && d.TargetId == r.Tank && r.Log.Events.IndexOf(e) > at);
            Assert.IsTrue(Array.Exists(dmg.Steps, x => x.Kind == DamageStepKind.Double), "2배");
            int once = 0;
            foreach (var st in dmg.Steps)
                if (st.Kind == DamageStepKind.Dice || st.Kind == DamageStepKind.Effect || st.Kind == DamageStepKind.Effectiveness) once += st.Value;
            Assert.AreEqual(Math.Max(0, 2 * once), dmg.Final);
            // 균열 카드가 없으면 그대로
            var plain = Hit(false);
            int pat = plain.Log.Events.FindIndex(e => e is OnslaughtDrawn);
            var pd = (DamageBreakdown)plain.Log.Events.Find(e => e is DamageBreakdown d && d.TargetId == plain.Tank && plain.Log.Events.IndexOf(e) > pat);
            Assert.IsFalse(Array.Exists(pd.Steps, x => x.Kind == DamageStepKind.Double));
        }

        [Test]
        public void A21_1_덱이떨어지면버림더미를섞고_배틀사이에도버림더미가남는다()
        {
            // 「Discard any played cards. If the deck is out of cards, shuffle the discard pile back into the deck.」 · 배틀 사이 유지(A21-1 [추정])
            var r = Game();
            var s = r.S;
            var deck = s.Decks[(int)DeckId.UltraOnslaught];
            var discard = s.Discards[(int)DeckId.UltraOnslaught];
            Stack(s, UbOnslaught.Boost);
            // 한 장만 남긴다(나머지는 이미 쓴 것으로)
            while (deck.Count > 1)
            {
                discard.Add(deck[0]);
                deck.RemoveAt(0);
            }
            Fight(r);
            Drive(r, () => Drawn(r) >= 2 && MyTurn(r));
            Assert.AreEqual(UbOnslaught.Cards, deck.Count + discard.Count, "여섯 장이 덱과 버림 더미에");
            if (Drawn(r) >= 2)
            {
                Assert.IsTrue(r.Log.OfType<PileShuffled>().Exists(e => e.Pile == DeckId.UltraOnslaught), "둘째 장을 뽑을 때 버림 더미를 섞었다");
                Assert.AreEqual(1, discard.Count, "섞은 여섯 장에서 한 장");
                Assert.AreEqual(5, deck.Count);
            }
            Drive(r, () => false);
            Assert.IsNull(s.Battle);
            Assert.Greater(discard.Count, 0, "배틀이 끝나도 버림 더미는 그대로");
            Assert.AreEqual(UbOnslaught.Cards, deck.Count + discard.Count);
            Assert.GreaterOrEqual(Drawn(r), 1);
        }

        [Test]
        public void A21_2_끄면안뽑는다()
        {
            // 실험 선택 규칙 — 기본 끔(A21-2)
            Assert.IsFalse(new RuleOptions().UltraOnslaught);
            var r = Game(false);
            Fight(r);
            Drive(r, () => false);
            Assert.IsNull(r.S.Battle);
            Assert.AreEqual(0, Drawn(r));
            Assert.AreEqual(UbOnslaught.Cards, r.S.Decks[(int)DeckId.UltraOnslaught].Count);
            Assert.AreEqual(0, r.S.Discards[(int)DeckId.UltraOnslaught].Count);
        }

        [Test]
        public void A21_2_신오의심장에서는무시_UB와네크로즈마배틀에만()
        {
            // 「THE ULTRA BURST CAMPAIGN」 카드 뒷면 · 원작 커뮤니티 확인
            var s = Battles.Game(ctx, 1, 7, new RuleOptions { UltraOnslaught = true });
            Assert.IsNull(s.Ub);
            Scene.Give(s, ctx, 0, Mine);
            int ub = Scene.Spawn(s, ctx, Foe, SeatId.UltraBeast);
            var log = new EventLog();
            var b = Battles.Start(s, ctx, log, BattleKind.UltraBeast, 0, SeatId.UltraBeast, -1, ub);
            Assert.IsFalse(UbOnslaught.On(s, b));
            for (int i = 0; i < 12 && s.Battle != null && log.OfType<DiceRolled>().FindAll(e => e.PokemonId == ub).Count < 2; i++)
            {
                var c = s.Pending.Find(x => !x.Answered);
                Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            }
            Assert.AreEqual(0, log.OfType<OnslaughtDrawn>().Count);
            Assert.AreEqual(UbOnslaught.Cards, s.Decks[(int)DeckId.UltraOnslaught].Count);
            // 울트라 버스트 판이어도 UB·네크로즈마 배틀에만
            var u = UbScenes.Started(ctx, 1, 3, null, new RuleOptions { UltraOnslaught = true });
            Assert.IsTrue(UbOnslaught.On(u, new BattleState { Kind = BattleKind.UltraBeast }));
            Assert.IsTrue(UbOnslaught.On(u, new BattleState { Kind = BattleKind.Necrozma }));
            Assert.IsFalse(UbOnslaught.On(u, new BattleState { Kind = BattleKind.Trial }));
            Assert.IsFalse(UbOnslaught.On(u, new BattleState { Kind = BattleKind.Legendary }));
            Assert.IsFalse(UbOnslaught.On(u, new BattleState { Kind = BattleKind.Wild }));
        }
    }
}
