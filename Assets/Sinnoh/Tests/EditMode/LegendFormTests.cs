using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M5 「전설·알파·폼」(rulings A26): 전설 장소 배틀 시작 · 알파 스트라이크(타입마다 답 · 공격력 4, 직전 턴 2 · 고른 타입으로 심장·유물) · 비스트 부스트 · 전설은 모두 쓰러뜨려야 도망 ·
    /// 폼을 바꾸는 기술 8장 + 배틀 끝 복귀 · 유물 폼(정비). 배틀은 실제 흐름(트레이너전·야생)으로 돌고 굴림은 그 면이 나오는 시드로 정한다.
    /// </summary>
    public class LegendFormTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const AttackDieFace R = AttackDieFace.RegularHit;

        /// <summary>폼을 바꾸는 기술 8장(effects-todo의 폼 몫).</summary>
        public static readonly string[] FormMoves = { "hangry_wheel", "electro_wheel", "chest_thump", "gather_sunlight", "zen_restoration", "kings_shield", "kings_blade", "adaptive_cloak" };

        int Mv(string key)
        {
            int m = Scene.Move(ctx, key);
            Assert.GreaterOrEqual(m, 0, key);
            return m;
        }

        int Card(string key)
        {
            int d = Scene.Def(ctx, key);
            Assert.GreaterOrEqual(d, 0, key);
            return d;
        }

        sealed class Arena
        {
            public GameState S;
            public EventLog Log = new EventLog();
            public int A, D;
        }

        /// <summary>트레이너전: 좌석 0(a) 대 좌석 1(d + dBench). 좌석 0의 기술 고르기가 열려 있다(좌석 0이 더 빠른 종만 쓴다).</summary>
        Arena Duel(string a, string d = "242:0", bool legend = false, params string[] dBench)
        {
            var t = new Arena { S = Battles.Game(ctx, 2) };
            t.A = Scene.Give(t.S, ctx, 0, a);
            t.S.Pokemon[t.A].HasLegendaryMove = legend;
            t.D = Scene.Give(t.S, ctx, 1, d);
            foreach (var k in dBench) Scene.Give(t.S, ctx, 1, k);
            Battles.Start(t.S, ctx, t.Log, BattleKind.Trainer, 0, 1, -1);
            for (int guard = 0; guard < 6 && t.S.Pending.Count == 1; guard++)
            {
                var c = t.S.Pending[0];
                if (c.Kind == ChoiceKind.SendIn) Battles.Answer(t.S, ctx, c, ChoiceAnswer.Pick(c.Seat == 0 ? t.A : t.D), t.Log);
                else if (c.Sub == ChoiceSub.InitiativeToken) Battles.Answer(t.S, ctx, c, ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
                else break;
            }
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "좌석 0의 차례");
            return t;
        }

        static ulong SeedFor(AttackDieFace[] faces)
        {
            for (ulong seed = 0; seed < 2000000; seed++)
            {
                var roll = AttackDice.Roll(new Pcg32(seed), faces.Length);
                bool same = true;
                for (int i = 0; i < roll.Length && same; i++) same = roll[i] == faces[i];
                if (same) return seed;
            }
            Assert.Fail("그 굴림이 나오는 시드가 없다");
            return 0;
        }

        static AttackDieFace[] All(AttackDieFace f, int n)
        {
            var r = new AttackDieFace[n];
            for (int i = 0; i < n; i++) r[i] = f;
            return r;
        }

        /// <summary>열린 기술 고르기에 답한다(faces를 주면 그 굴림). 반환 = 그 답의 이벤트.</summary>
        List<GameEvent> Answer(Arena t, ChoiceAnswer a, AttackDieFace[] faces = null)
        {
            if (faces != null) t.S.Rng = new Pcg32(SeedFor(faces));
            int from = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, t.S.Pending[0], a, t.Log);
            return t.Log.Events.GetRange(from, t.Log.Events.Count - from);
        }

        /// <summary>두 출전 포켓몬의 피해를 0으로(여러 턴을 도는 시험이 중간에 끝나지 않게).</summary>
        static void Heal(Arena t)
        {
            t.S.Pokemon[t.A].Damage = 0;
            t.S.Pokemon[t.D].Damage = 0;
        }

        static T One<T>(List<GameEvent> events) where T : GameEvent
        {
            var l = events.FindAll(e => e is T);
            Assert.AreEqual(1, l.Count, typeof(T).Name);
            return (T)l[0];
        }

        static List<T> Of<T>(List<GameEvent> events) where T : GameEvent
        {
            var r = new List<T>();
            foreach (var e in events)
                if (e is T x) r.Add(x);
            return r;
        }

        // ── 배틀 시작 ──

        [Test]
        public void A26_전설장소와신토큰의찾아보기는좌석보기의합법답에도있다()
        {
            // 좌석 손님은 보기 사본(Engine.ViewFor — 덱을 가린다)으로 Legal을 돈다: 「아직 게임에 있는 전설」은 공개 정보라 보기에서도 같은 답이어야 한다(M5 규칙 끝 검토 반영)
            List<ChoiceAnswer> ViewLegal(GameState g, int seat)
            {
                var v = Engine.ViewFor(g, seat);
                return Scene.Legal(v, ctx, v.Pending.Find(c => c.Kind == ChoiceKind.MapAction));
            }
            void Same(GameState g, int seat, string why)
            {
                var host = Flow.MapLegal(g, ctx);
                var view = ViewLegal(g, seat);
                Assert.IsTrue(host.Exists(a => a.Kind == AnswerKind.Search && a.Arg >= 0), why + ": 호스트에 찾아보기");
                Assert.AreEqual(host.Count, view.Count, why);
                for (int i = 0; i < host.Count; i++) Assert.IsTrue(host[i].SameAs(view[i]), $"{why}: 답 {i}");
            }
            // 전설 장소(유물을 가진 좌석)
            var s = Flow.Started(ctx, 2, 12);
            int seat = s.ActiveSeat;
            int site = -1;
            for (int f = 0; f < ctx.Fixtures.Fixtures.Count; f++)
                if (ctx.Fixtures.Fixtures[f].Key == "legendary_site.grand_chateau") site = f;
            s.Seats[seat].Cell = ctx.Fixtures.Fixtures[site].Cell;
            s.Seats[seat].Bag.Add(Scene.Item(ctx, "item.secret_key"));
            Same(s, seat, "전설 장소");
            // 시작의 사당의 신 토큰
            s = Flow.Started(ctx, 2, 12);
            seat = s.ActiveSeat;
            s.Seats[seat].Cell = HosFlow.OriginShrineCell(ctx);
            s.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = Card("483:1"), Cell = HosFlow.OriginShrineCell(ctx), FaceUp = true });
            Same(s, seat, "신 토큰");
        }

        [Test]
        public void M5_배틀시작_Legendary_유물을가진좌석만전설장소에서찾아보기로전설과싸운다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 12);
            int seat = s.ActiveSeat;
            int site = -1;
            for (int f = 0; f < ctx.Fixtures.Fixtures.Count; f++)
                if (ctx.Fixtures.Fixtures[f].Key == "legendary_site.grand_chateau") site = f;
            Assert.GreaterOrEqual(site, 0);
            int rotom = Card("479:1");
            CollectionAssert.Contains(s.Decks[(int)DeckId.Legendary], rotom, "전설 덱에 로토무");
            s.Seats[seat].Cell = ctx.Fixtures.Fixtures[site].Cell;
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Search), "비밀키가 없으면 만나지 못한다(p.4 「if you are holding the matching Artifact」)");
            s.Seats[seat].Bag.Add(Scene.Item(ctx, "item.secret_key"));
            var search = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.Search);
            Assert.IsNotNull(search, "유물을 가지면 그 칸에서 찾아보기");
            Assert.AreEqual(site, search.Arg);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, search, log);
            var started = Flow.Of<BattleStarted>(log);
            Assert.AreEqual(1, started.Count);
            Assert.AreEqual(BattleKind.Legendary, started[0].Kind);
            Assert.AreEqual((int)BattleBgm.Legendary, started[0].BgmKey);
            Assert.AreEqual(rotom, Flow.Of<SentIn>(log).Find(e => e.Side == 1).Species);
            Assert.IsFalse(s.Decks[(int)DeckId.Legendary].Contains(rotom) && s.Battle != null, "배틀하는 동안 카드는 덱 밖");
            if (s.Battle != null) Assert.IsTrue(s.Pokemon[s.Battle.Sides[1].Roster[0]].HasLegendaryMove, "전설 특수기술");
            else if (Flow.Of<BattleEnded>(log)[0].Winner != 0) CollectionAssert.Contains(s.Decks[(int)DeckId.Legendary], rotom, "지면 카드는 덱으로 — 다시 도전한다(원작 커뮤니티 확인)");
            Assert.AreEqual(RelicRules.Owns(s, ctx, seat, Scene.Item(ctx, "item.secret_key")), true, "유물은 잃지 않는다(p.17)");
        }

        [Test]
        public void M5_배틀시작_Alpha_우두머리는알파스트라이크를고른다()
        {
            var log = new EventLog();
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "129:0");
            int card = s.Decks[(int)DeckId.Strong][0];
            s.Decks[(int)DeckId.Strong].RemoveAt(0);
            BattleFlow.StartAlpha(s, ctx, Scene.Fx(s, log, 0, ActionKind.Search), 0, card, s.Seats[0].Cell);
            Assert.AreEqual(BattleKind.Alpha, s.Battle.Kind);
            int wild = s.Battle.Sides[1].Roster[0];
            var moves = new List<int>();
            PokemonRules.MoveOptions(ctx, s.Pokemon[wild], moves);
            CollectionAssert.Contains(moves, BattleRules.AlphaStrike, "우두머리는 전설로 다시 분류된다(A17) — 알파 스트라이크");
            Assert.IsTrue(LegendRules.HasBeastBoost(s.Pokemon[wild]));
        }

        // ── 알파 스트라이크 ──

        [Test]
        public void A26_알파스트라이크는배울수있는타입마다답이있고고른타입으로친다()
        {
            var t = Duel("479:1", legend: true);
            var c = Battles.Only(t.S, ChoiceKind.BattleAction);
            var legal = Scene.Legal(t.S, ctx, c);
            var types = new List<int>();
            foreach (var x in ctx.Cards.Pokemon[Card("479:1")].LearnTypes) types.Add((int)x);
            var alpha = legal.FindAll(a => a.Kind == AnswerKind.UseMove && a.Target == BattleRules.AlphaStrike);
            Assert.AreEqual(types.Count, alpha.Count, "「any learnable type」 — 타입마다 답");
            for (int i = 0; i < types.Count; i++) CollectionAssert.AreEqual(new[] { types[i] }, alpha[i].Values);
            int psychic = (int)PokemonType.Psychic;
            var ev = Answer(t, ChoiceRules.AlphaStrikeAnswer(psychic));
            Assert.AreEqual(PokemonType.Psychic, One<MoveSelected>(ev).Type);
            Assert.AreEqual(BattleRules.AlphaStrike, One<MoveSelected>(ev).MoveKey);
            var roll = One<DiceRolled>(ev);
            Assert.AreEqual(BattleRules.AlphaStrikeStrength, roll.Power, "공격력 4");
            Assert.AreEqual(PokemonType.Psychic, roll.Type);
            Assert.AreEqual(PokemonType.Psychic, One<TypeMatchup>(ev).Type, "상성도 고른 타입");
            Assert.AreEqual(BattleRules.AlphaStrike, t.S.Pokemon[t.A].LastMove);
        }

        [Test]
        public void A26_알파스트라이크는직전턴에썼으면공격력2()
        {
            var t = Duel("479:1", legend: true);
            Heal(t);
            Answer(t, ChoiceRules.AlphaStrikeAnswer((int)PokemonType.Electric));
            // 좌석 1(해피너스)의 차례
            var c1 = Battles.Only(t.S, ChoiceKind.BattleAction);
            Assert.AreEqual(1, c1.Seat);
            Heal(t);
            Answer(t, c1.Default.Clone());
            var mods = new List<Mod>();
            Assert.AreEqual(BattleRules.AlphaStrikeRepeatStrength, BattleRules.RollPower(t.S, ctx, BattleRules.AlphaStrike, t.A, t.D, mods), "「decreased to 2 if this move was used last turn」");
            Assert.IsTrue(mods.Exists(m => m.Source.Same(SourceRef.Rule(RuleSource.AlphaStrike))));
            Heal(t);
            var ev = Answer(t, ChoiceRules.AlphaStrikeAnswer((int)PokemonType.Ghost));
            Assert.AreEqual(2, One<DiceRolled>(ev).Power, "타입이 달라도 같은 기술");
            // 다른 기술을 쓰면 다음 알파 스트라이크는 다시 4
            Heal(t);
            Answer(t, Battles.Only(t.S, ChoiceKind.BattleAction).Default.Clone());
            Heal(t);
            ev = Answer(t, Battles.Move(ctx.Cards.Pokemon[Card("479:1")].Signature));
            Heal(t);
            Answer(t, Battles.Only(t.S, ChoiceKind.BattleAction).Default.Clone());
            Assert.AreEqual(4, BattleRules.RollPower(t.S, ctx, BattleRules.AlphaStrike, t.A, t.D, mods));
        }

        [TestCase("Ghost", 5)]
        [TestCase("Normal", 4)]
        public void A26_알파스트라이크의유물보정은고른타입으로본다(string type, int power)
        {
            var t = Duel("479:1", legend: true);
            t.S.Pokemon[t.A].HeldItem = Scene.Item(ctx, "item.secret_key"); // 고스트·전기 +1
            var ev = Answer(t, ChoiceRules.AlphaStrikeAnswer((int)Enum.Parse(typeof(PokemonType), type)));
            Assert.AreEqual(power, One<DiceRolled>(ev).Power, "원작 커뮤니티 확인");
        }

        [Test]
        public void A26_배운기술이알파스트라이크다음비스트부스트를덮는다()
        {
            var p = new PokemonState { Def = Card("479:1"), HasLegendaryMove = true };
            Assert.IsTrue(LegendRules.HasAlphaStrike(p));
            Assert.IsTrue(LegendRules.HasBeastBoost(p));
            p.Moves[1] = Mv("splash");
            Assert.IsFalse(LegendRules.HasAlphaStrike(p), "첫 기술은 알파 스트라이크를 덮는다(어느 칸이든)");
            Assert.IsTrue(LegendRules.HasBeastBoost(p));
            p.Moves[0] = p.Moves[1];
            Assert.IsFalse(LegendRules.HasBeastBoost(p), "둘째 기술은 비스트 부스트를 덮는다");
            p.Moves[0] = p.Moves[1] = -1;
            Assert.IsTrue(LegendRules.HasAlphaStrike(p), "잊으면 돌아온다(p.6 「add/remove moves to overwrite/reveal」)");
            var plain = new PokemonState { Def = Card("479:1") };
            Assert.IsFalse(LegendRules.HasAlphaStrike(plain), "전설 특수기술이 없는 포켓몬");
        }

        [Test]
        public void A26_원칙AI는알파스트라이크를가장좋은타입으로고른다()
        {
            var me = new PokemonState { Def = Card("479:1"), HasLegendaryMove = true };
            var foe = new PokemonState { Def = Card("242:0") };
            int best = PrincipleRules.AlphaType(ctx, me, foe);
            var types = ctx.Cards.Pokemon[foe.Def].Types;
            foreach (var x in ctx.Cards.Pokemon[me.Def].LearnTypes)
                Assert.LessOrEqual(TypeChart.Bonus(x, types[0], types.Count > 1 ? types[1] : PokemonType.Typeless), TypeChart.Bonus((PokemonType)best, types[0], types.Count > 1 ? types[1] : PokemonType.Typeless));
            var a = PrincipleRules.Answer(ctx, me, foe, out _);
            if (a.Target == BattleRules.AlphaStrike) CollectionAssert.AreEqual(new[] { best }, a.Values);
            me.LastMove = BattleRules.AlphaStrike;
            Assert.AreEqual(BattleRules.AlphaStrikeRepeatStrength, PrincipleRules.Strength(ctx, BattleRules.AlphaStrike, me, foe));
        }

        // ── 비스트 부스트 ──

        [Test]
        public void A26_비스트부스트는상대가쓰러지면피로3을빼고다음공격공격력1()
        {
            var t = Duel("479:1", "129:0", true, "242:0");
            Battles.SetHp(t.S, ctx, t.D, 1);
            t.S.Pokemon[t.A].Fatigue = 3;
            int sig = ctx.Cards.Pokemon[Card("479:1")].Signature;
            var ev = Answer(t, Battles.Move(sig), All(R, 4));
            var boost = Of<FatigueChanged>(ev).FindAll(f => f.Cause == FatigueCause.BeastBoost);
            Assert.AreEqual(1, boost.Count, "「Triggers whenever the Pokémon battling the Legendary Pokémon faints」");
            Assert.AreEqual(4, boost[0].Before, "굴림 피로 3 → 4");
            Assert.AreEqual(1, boost[0].After, "「battle fatigue is decreased by 3」");
            Assert.AreEqual(t.A, boost[0].PokemonId);
            Assert.AreEqual(1, t.S.Battle.Sides[0].BeastBoost);
            // 해피너스가 나온 뒤 — 로토무(우선도 12)가 먼저
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
            var mods = new List<Mod>();
            Assert.AreEqual(BattleRules.MoveStrength(ctx, sig) + 1, BattleRules.RollPower(t.S, ctx, sig, t.A, t.S.Battle.Sides[1].Active, mods), "「increasing the attack strength of the next attack it uses by 1」");
            Assert.IsTrue(mods.Exists(m => m.Source.Same(SourceRef.Rule(RuleSource.BeastBoost))));
            ev = Answer(t, Battles.Move(sig));
            Assert.AreEqual(BattleRules.MoveStrength(ctx, sig) + 1, One<DiceRolled>(ev).Power);
            Assert.AreEqual(0, t.S.Battle != null ? t.S.Battle.Sides[0].BeastBoost : 0, "다음 공격 하나에만");
        }

        [Test]
        public void A26_비스트부스트는배운기술둘이면없다()
        {
            var t = Duel("479:1", "129:0", true, "242:0");
            t.S.Pokemon[t.A].Moves[0] = Mv("splash");
            t.S.Pokemon[t.A].Moves[1] = ctx.Cards.Pokemon[Card("242:0")].Signature;
            Battles.SetHp(t.S, ctx, t.D, 1);
            var ev = Answer(t, Battles.Move(ctx.Cards.Pokemon[Card("479:1")].Signature), All(R, 4));
            Assert.AreEqual(0, Of<FatigueChanged>(ev).FindAll(f => f.Cause == FatigueCause.BeastBoost).Count);
        }

        // ── 도망 ──

        [Test]
        public void S7_8_전설은내포켓몬을모두쓰러뜨려야도망친다()
        {
            var log = new EventLog();
            var s = Battles.Game(ctx, 2);
            Scene.Give(s, ctx, 0, "242:0");
            Scene.Give(s, ctx, 0, "242:0");
            int wild = PokemonRules.Spawn(s, Card("487:1"), SeatId.Wild, (int)DeckId.Legendary);
            s.Pokemon[wild].HasLegendaryMove = true;
            Battles.Start(s, ctx, log, BattleKind.Legendary, 0, SeatId.Wild, -1, wild);
            Battles.Drive(s, ctx, log, (st, c) =>
            {
                st.Pokemon[wild].Damage = 0; // 기라티나는 쓰러지지 않고 상태이상도 없다 — 내 포켓몬만 쓰러진다(해피너스의 노말 기술은 고스트에게 들지 않는다)
                StatusRules.Clear(st.Pokemon[wild]);
                if (c.Kind == ChoiceKind.BattleAction && c.Seat == 0)
                {
                    var me = st.Battle.Sides[0].Active;
                    Battles.SetHp(st, ctx, me, 1);
                }
                return null;
            });
            var ko = log.OfType<Fainted>().FindAll(f => f.Side == 0);
            Assert.AreEqual(2, ko.Count, "첫 포켓몬이 쓰러져도 도망치지 않는다(p.6 「flee the battle after defeating all your Pokémon instead of defeating just one」)");
            Assert.AreEqual(BattleResult.Fled, log.OfType<BattleEnded>()[0].Result);
            CollectionAssert.Contains(s.Decks[(int)DeckId.Legendary], Card("487:1"), "도망친 전설은 덱으로");
        }

        // ── 폼 기술 ──

        [TestCase("electro_wheel", "877:1", "877:2")]
        [TestCase("chest_thump", "555:1", "555:2")]
        [TestCase("gather_sunlight", "421:1", "421:2")]
        [TestCase("zen_restoration", "555:2", "555:1")]
        [TestCase("kings_shield", "681:1", "681:2")]
        [TestCase("kings_blade", "681:2", "681:1")]
        public void M5_폼기술_폼을바꾸고배틀이끝나면배틀전폼으로(string move, string from, string to)
        {
            var t = Duel(from);
            Battles.SetHp(t.S, ctx, t.D, 1);
            int m = Mv(move);
            Assert.AreEqual(m, ctx.Cards.Pokemon[Card(from)].Signature, "시그니처");
            int power = BattleRules.RollPower(t.S, ctx, m, t.A, t.D, new List<Mod>());
            var ev = Answer(t, Battles.Move(m), All(R, power));
            var changed = Of<FormChanged>(ev);
            Assert.GreaterOrEqual(changed.Count, 2, "바꾸고 · 배틀 끝에 돌아온다");
            Assert.AreEqual(Card(from), changed[0].Before);
            Assert.AreEqual(Card(to), changed[0].After, "「Change to X Form if possible」");
            Assert.IsTrue(changed[0].Temporary);
            Assert.IsTrue(changed[0].Source.Same(new SourceRef(SourceKind.Move, m)));
            Assert.IsNotNull(Of<BattleEnded>(ev).Find(e => e.Winner == 0), "해피너스가 쓰러져 배틀이 끝났다");
            var back = changed[changed.Count - 1];
            Assert.AreEqual(Card(to), back.Before);
            Assert.AreEqual(Card(from), back.After, "「When the battle ends, the Pokémon returns to its default form」");
            Assert.IsFalse(back.Temporary);
            Assert.AreEqual(Card(from), t.S.Pokemon[t.A].Def);
            Assert.AreEqual(-1, t.S.Pokemon[t.A].FormBeforeBattle);
        }

        [Test]
        public void M5_폼기술_폼은배틀중에남고시그니처가따라간다_kings_shield는공격받을때일반타격을모두지운다()
        {
            var t = Duel("681:1", "799:0");
            var ev = Answer(t, Battles.Move(Mv("kings_shield")), All(R, 1));
            Assert.AreEqual(Card("681:2"), t.S.Pokemon[t.A].Def, "블레이드폼");
            var moves = new List<int>();
            PokemonRules.MoveOptions(ctx, t.S.Pokemon[t.A], moves);
            Assert.AreEqual(Mv("kings_blade"), moves[0], "시그니처는 그 폼의 것(킹블레이드)");
            Assert.AreEqual(0, Of<FatigueChanged>(ev).FindAll(f => f.Cause != FatigueCause.Roll).Count, "「Neither battle fatigue or the current turn order is affected」");
            // 악식킹이 일반 타격 5개로 공격 → 모두 지운다(「remove all regular hits from the roll」)
            var c = Battles.Only(t.S, ChoiceKind.BattleAction);
            Assert.AreEqual(1, c.Seat);
            int chomp = ctx.Cards.Pokemon[Card("799:0")].Signature;
            int n = BattleRules.RollPower(t.S, ctx, chomp, t.D, t.A, new List<Mod>());
            ev = Answer(t, Battles.Move(chomp), All(R, n));
            var removed = 0;
            foreach (var fc in Of<DiceFacesChanged>(ev))
                if (fc.Source.Same(new SourceRef(SourceKind.Move, Mv("kings_shield")))) removed += fc.Changes.Length;
            Assert.AreEqual(n, removed, "일반 타격을 모두");
        }

        [Test]
        public void M5_폼기술_그폼이없는포켓몬은바꾸지않고_폼바꾸기금지면바꾸지않는다()
        {
            // 해피너스가 배운 king's blade: 해피너스에게 실드폼이 없다(「ignore the form change」)
            var t = Duel("242:0", "799:0");
            t.S.Pokemon[t.A].Moves[0] = Mv("kings_blade");
            var ev = Answer(t, Battles.Move(Mv("kings_blade")));
            Assert.AreEqual(Card("242:0"), t.S.Pokemon[t.A].Def);
            Assert.AreEqual(0, Of<FormChanged>(ev).Count);
            // imprison(폼을 바꾸지 못한다)이 걸린 킬가르도
            var u = Duel("681:1", "799:0");
            ModRules.Add(u.S, u.A, ModScope.UntilSwitch, EffectRole.Attacker, EffectOp.NoFormChange, AttackDieFace.None, 0, new SourceRef(SourceKind.Move, Mv("imprison")));
            ev = Answer(u, Battles.Move(Mv("kings_shield")));
            Assert.AreEqual(Card("681:1"), u.S.Pokemon[u.A].Def, "「The target may no longer change form」");
            Assert.AreEqual(0, Of<FormChanged>(ev).Count);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void M5_폼기술_hangry_wheel_규토리를쓰면공격력7과배부른모양(bool consume)
        {
            var t = Duel("877:2");
            t.S.Seats[0].Apricorns[0] = 2;
            Answer(t, Battles.Move(Mv("hangry_wheel")));
            var pick = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            var ev = Answer(t, consume ? ChoiceAnswer.Pick(0) : ChoiceAnswer.Of(AnswerKind.Decline));
            Assert.AreEqual(consume ? Card("877:1") : Card("877:2"), t.S.Pokemon[t.A].Def, "「to change to Full Belly Form」 — 쓸 때만");
            Assert.AreEqual(consume ? 7 : 4, One<DiceRolled>(ev).Power);
            Assert.AreEqual(consume ? 1 : 2, t.S.Seats[0].Apricorns[0]);
        }

        [Test]
        public void M5_폼기술_adaptive_cloak_도롱을고르고그폼의타입으로친다()
        {
            var t = Duel("413:1");
            Answer(t, Battles.Move(Mv("adaptive_cloak")));
            var forms = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            CollectionAssert.AreEquivalent(new[] { Card("413:2"), Card("413:3") }, forms.Options, "다른 도롱 둘(「may」 — 안 바꿀 수도)");
            Assert.AreEqual(0, forms.Count, "안 함이 첫 답");
            Answer(t, ChoiceAnswer.Pick(Card("413:2")));
            Assert.AreEqual(Card("413:2"), t.S.Pokemon[t.A].Def, "모래땅도롱");
            var type = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            CollectionAssert.AreEquivalent(new[] { (int)PokemonType.Bug, (int)PokemonType.Ground }, type.Options, "「either base type of the user」 — 바꾼 폼의 타입");
            var ev = Answer(t, ChoiceAnswer.Pick((int)PokemonType.Ground));
            Assert.AreEqual(PokemonType.Ground, One<DiceRolled>(ev).Type, "그 공격에 든다(원작 커뮤니티 확인)");
        }

        [Test]
        public void M5_폼기술_8장은효과표에있다()
        {
            foreach (var key in FormMoves) Assert.IsTrue(ctx.Effects.Implemented(Mv(key)), key);
            var todo = new List<string>();
            foreach (var m in ctx.Cards.Moves)
                if (!ctx.Effects.Implemented(Mv(m.Key)) && m.En.Contains("Form")) todo.Add(m.Key);
            CollectionAssert.IsEmpty(todo, "문구에 폼 바꾸기가 있는 미구현 기술이 남지 않았다");
        }

        // ── 유물 폼 ──

        [Test]
        public void A26_유물폼은지닐때배틀밖에서바꾸고빼면기본폼으로()
        {
            var s = Flow.Started(ctx, 2, 12);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int mon = Scene.Give(s, ctx, seat, "492:1");
            int flower = Scene.Item(ctx, "item.gracidea_flower");
            st.Bag.Add(flower);
            var sky = new Maintenance { Kind = MaintKind.ChangeForm, Pokemon = mon, Value = Card("492:2") };
            Assert.AreEqual(RejectReason.Illegal, MaintenanceRules.Check(s, ctx, seat, sky), "그라시데아를 지녀야(「whilst held」)");
            Scene.Accept(MaintenanceRules.Submit(s, ctx, seat, new Maintenance { Kind = MaintKind.HoldItem, Pokemon = mon, Value = flower }, null));
            var log = new EventLog();
            Scene.Accept(MaintenanceRules.Submit(s, ctx, seat, sky, log));
            Assert.AreEqual(Card("492:2"), s.Pokemon[mon].Def, "스카이폼");
            Assert.AreEqual(1, log.OfType<FormChanged>().Count);
            Assert.AreEqual(RejectReason.Illegal, MaintenanceRules.Check(s, ctx, seat, new Maintenance { Kind = MaintKind.ChangeForm, Pokemon = mon, Value = Card("479:2") }), "다른 종의 폼은 안 된다");
            log.Clear();
            Scene.Accept(MaintenanceRules.Submit(s, ctx, seat, new Maintenance { Kind = MaintKind.HoldItem, Pokemon = mon, Value = -1 }, log));
            Assert.AreEqual(Card("492:1"), s.Pokemon[mon].Def, "「If the Pokémon is no longer holding the item, it returns to its default form」");
            Assert.AreEqual(1, log.OfType<FormChanged>().Count);
            // 로토무 + 비밀키: 다섯 폼 어느 것이든, 기본 폼으로는 언제든
            int rotom = Scene.Give(s, ctx, seat, "479:1");
            s.Pokemon[rotom].HeldItem = Scene.Item(ctx, "item.secret_key");
            Scene.Accept(MaintenanceRules.Submit(s, ctx, seat, new Maintenance { Kind = MaintKind.ChangeForm, Pokemon = rotom, Value = Card("479:4") }, null));
            Assert.AreEqual(Card("479:4"), s.Pokemon[rotom].Def);
            Scene.Accept(MaintenanceRules.Submit(s, ctx, seat, new Maintenance { Kind = MaintKind.ChangeForm, Pokemon = rotom, Value = Card("479:1") }, null));
            Assert.AreEqual(Card("479:1"), s.Pokemon[rotom].Def);
            Assert.AreEqual(MaintTiming.OutsideBattle, Maintenance.TimingOf(MaintKind.ChangeForm), "배틀 밖에서만(p.6 「whilst outside of battle」)");
        }
    }
}
