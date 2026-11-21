using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「전용 아이템 능력」(rulings A41 — 능력 10 + 전용 아이템 7): dragon_tamer B·R · gentleman R · guitarist B·R · parasol_lady B·R · poke_maniac R · tourist B·R
    /// (드래곤의 정점 · 프레셔스 볼 · 앰프 주사위 · 날씨 휠·바닥 · 대타출동 · 랜드마크 투어).
    /// </summary>
    public class TrainerItemTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const AttackDieFace S = AttackDieFace.StatusSymbol;
        const AttackDieFace R = AttackDieFace.RegularHit;
        const string Garchomp = "445:0", Bidoof = "399:0", Moderate = "388:0", SlowModerate = "411:0", Gible = "443:0", Bagon = "371:0";

        int CardId(string key) => ctx.Cards.TrainerCardIds.IndexOf(key);
        int Item(string key) => Scene.Item(ctx, key);

        void Card(GameState s, int seat, string key, int tier)
        {
            s.Seats[seat].TrainerCard = CardId(key);
            s.Seats[seat].TrainerCardTier = tier;
            s.Decks[(int)DeckId.TrainerCard].Remove(CardId(key));
            TrainerRules.Refresh(s, ctx);
        }

        EventLog Unlock(GameState s, string key, int seat = 0)
        {
            var log = new EventLog();
            int p = Scene.Spawn(s, ctx, key, seat);
            TrainerRules.Obtained(s, ctx, Scene.Fx(s, log, seat, ActionKind.BattleAction), seat, p, ObtainCause.CardEffect);
            return log;
        }

        static bool Same(AttackDieFace[] a, AttackDieFace[] b)
        {
            for (int i = 0; i < b.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        static ulong SeedFor(AttackDieFace[] faces) => Battles.Seed(seed => Same(AttackDice.Roll(new Pcg32(seed), faces.Length), faces), 4000000);

        /// <summary>좌석 0이 1 체력 야생 key를 이긴다 — 배틀 중 해금되고 배틀 뒤 턴 끝 앞의 물음에서 멈춘다.</summary>
        void WinAgainst(GameState s, string key, EventLog log)
        {
            int mine = Scene.Give(s, ctx, 0, Garchomp);
            int wild = Scene.Spawn(s, ctx, key, SeatId.Wild);
            Battles.SetHp(s, ctx, wild, 1);
            var party = s.Seats[0].Party;
            for (int i = 1; i < party.Length; i++)
                if (party[i] == mine)
                {
                    party[i] = party[0];
                    party[0] = mine;
                }
            Battles.Wild(s, ctx, log, 0, wild);
            for (int guard = 0; guard < 8 && s.Battle != null; guard++)
            {
                var c = s.Pending.Find(x => !x.Answered);
                if (c.Kind == ChoiceKind.BattleAction)
                {
                    s.Rng = new Pcg32(SeedFor(new[] { R, R, R, R, R }));
                    Battles.Answer(s, ctx, c, Battles.Move(ctx.Cards.Pokemon[s.Pokemon[mine].Def].Signature), log);
                }
                else Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            }
            for (int guard = 0; guard < 8; guard++)
            {
                var c = s.Pending.Find(x => !x.Answered && x.Window == Window.BattleEnd);
                if (c == null) break;
                Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            }
            Assert.IsNull(s.Battle, "배틀이 끝났다");
        }

        PendingChoice CardAsk(GameState s) => s.Pending.Find(c => !c.Answered && c.Source.Kind == SourceKind.TrainerCard);

        // ── 트레이너전 장면(좌석 0이 먼저 공격) ──

        sealed class Arena
        {
            public GameState S;
            public EventLog Log = new EventLog();
            public List<int> Mine = new List<int>(), Theirs = new List<int>();
            public int From;
            public int A => Mine[0];
            public int D => Theirs[0];
        }

        Arena Field(string card, string[] mine, string[] theirs, int tier = TrainerRules.Gold, System.Action<Arena> prep = null)
        {
            var t = new Arena { S = Battles.Game(ctx, 2) };
            foreach (var k in mine) t.Mine.Add(Scene.Give(t.S, ctx, 0, k));
            foreach (var k in theirs) t.Theirs.Add(Scene.Give(t.S, ctx, 1, k));
            if (card != null) Card(t.S, 0, card, tier);
            prep?.Invoke(t);
            Battles.Start(t.S, ctx, t.Log, BattleKind.Trainer, 0, 1, -1);
            for (int guard = 0; guard < 4 && t.S.Pending.Count == 1 && t.S.Pending[0].Kind == ChoiceKind.SendIn; guard++)
            {
                var c = t.S.Pending[0];
                Battles.Answer(t.S, ctx, c, ChoiceAnswer.Pick(c.Seat == 0 ? t.A : t.D), t.Log);
            }
            if (t.S.Pending.Count == 1 && t.S.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(t.S, ctx, t.S.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "좌석 0이 먼저");
            return t;
        }

        void Use(Arena t, string move, params AttackDieFace[] faces)
        {
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            int active = t.S.Battle.Sides[ch.Seat].Active;
            int m = Scene.Move(ctx, move);
            Assert.GreaterOrEqual(m, 0, move);
            if (ctx.Cards.Pokemon[t.S.Pokemon[active].Def].Signature != m) t.S.Pokemon[active].Moves[0] = m;
            t.S.Rng = new Pcg32(SeedFor(faces));
            t.From = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ch, Battles.Move(m), t.Log);
        }

        List<GameEvent> Events(Arena t) => t.Log.Events.GetRange(t.From, t.Log.Events.Count - t.From);

        static List<T> All<T>(List<GameEvent> events) where T : GameEvent
        {
            var r = new List<T>();
            foreach (var e in events)
                if (e is T x) r.Add(x);
            return r;
        }

        PendingChoice Ask(Arena t) => t.S.Pending.Find(c => !c.Answered && c.Source.Kind == SourceKind.TrainerCard);

        static int EffectFrom(DamageBreakdown d, SourceRef src)
        {
            int n = 0;
            foreach (var st in d.Steps)
                if (st.Kind == DamageStepKind.Effect && st.Source.Equals(src)) n += st.Value;
            return n;
        }

        // ── dragon_tamer B·R · 드래곤의 정점 ──

        [Test]
        public void A20_6_드래곤조련사파랑_약한덱의드래곤을골라파티로_정점을지니고진화비용5더()
        {
            var s = Battles.Game(ctx, 2);
            Card(s, 0, "dragon_tamer", TrainerRules.Bronze);
            Scene.Give(s, ctx, 0, Bidoof);
            var log = new EventLog();
            WinAgainst(s, SlowModerate, log);
            Assert.AreEqual(TrainerRules.Silver, s.Seats[0].TrainerCardTier);
            var c = CardAsk(s);
            Assert.IsNotNull(c, "드래곤이 여럿이면 고른다(강제)");
            Assert.AreEqual(1, c.Count);
            Assert.GreaterOrEqual(c.Options.Count, 2);
            int gible = Scene.Def(ctx, Gible);
            CollectionAssert.Contains(c.Options, gible);
            CollectionAssert.Contains(c.Options, Scene.Def(ctx, Bagon));
            // 감사 G3-4(11-20): 후보는 약한 덱의 내용이다 — 남의 보기에는 가린다(DueSwap · 동등한 교환과 같은 꼴)
            Assert.IsTrue(ChoiceRules.HidesFromOthers(c));
            var other = Engine.ViewFor(s, 1).Pending.Find(x => x.Id == c.Id);
            Assert.IsFalse(other.Options.Contains(gible), "남의 보기에 약한 덱의 드래곤이 새지 않는다");
            int weak = s.Decks[(int)DeckId.Weak].Count;
            log.Clear();
            Battles.Answer(s, ctx, c, ChoiceAnswer.Pick(gible), log);
            var gained = log.OfType<PokemonGained>();
            Assert.AreEqual(1, gained.Count);
            int p = gained[0].PokemonId;
            Assert.AreEqual(gible, s.Pokemon[p].Def);
            Assert.AreEqual(weak - 1, s.Decks[(int)DeckId.Weak].Count, "약한 덱에서 꺼냈다");
            Assert.AreEqual(Item("item.apex_dragon"), s.Pokemon[p].HeldItem, "「receives the Apex Dragon held item」");
            int cost = ctx.Cards.Pokemon[gible].EvolveCost.Value;
            Assert.AreEqual(cost + TrainerRules.ApexEvolveCost, EvolveRules.Cost(s, ctx, 0, p), "「costs an extra 5 Journey Points to evolve at all stages」");
            Assert.AreEqual(1, log.OfType<TurnEnded>().Count);
        }

        [Test]
        public void G1_드래곤의정점_뗄수없고놓아주면사라진다()
        {
            var s = Battles.Game(ctx, 2);
            Card(s, 0, "dragon_tamer", TrainerRules.Silver);
            int p = Scene.Give(s, ctx, 0, Gible);
            Scene.Give(s, ctx, 0, Bidoof);
            int apex = Item("item.apex_dragon"), band = Item("item.choice_band");
            s.Pokemon[p].HeldItem = apex;
            s.Seats[0].Bag.Add(band);
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, new Maintenance { Kind = MaintKind.HoldItem, Pokemon = p, Value = -1 }, new EventLog()), RejectReason.Illegal, "내려놓기 불가");
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, new Maintenance { Kind = MaintKind.HoldItem, Pokemon = p, Value = band }, new EventLog()), RejectReason.Illegal, "바꾸기 불가");
            Assert.IsFalse(TrainerRules.Holdable(ctx, apex));
            MaintenanceRules.Release(s, ctx, Scene.Fx(s, null, 0), 0, p);
            CollectionAssert.DoesNotContain(s.Seats[0].Bag, apex, "가방으로 가지 않는다 [가정 §5 #147 ②]");
        }

        [Test]
        public void G3_드래곤조련사파랑_약한덱에드래곤이없으면넘어간다()
        {
            var s = Battles.Game(ctx, 2);
            Card(s, 0, "dragon_tamer", TrainerRules.Bronze);
            Scene.Give(s, ctx, 0, Bidoof);
            s.Decks[(int)DeckId.Weak].RemoveAll(d => new List<PokemonType>(ctx.Cards.Pokemon[d].Types).Contains(PokemonType.Dragon));
            var log = Unlock(s, Moderate);
            Assert.AreEqual(TrainerRules.Silver, s.Seats[0].TrainerCardTier);
            Assert.AreEqual(0, log.OfType<PokemonGained>().Count);
            Assert.IsEmpty(s.Seats[0].UnlockDue, "물을 것도 없다");
        }

        [Test]
        public void A20_6_드래곤조련사빨강_정점에체력6우선도2공격력1()
        {
            var s = Battles.Game(ctx, 2);
            Card(s, 0, "dragon_tamer", TrainerRules.Silver);
            int p = Scene.Give(s, ctx, 0, Gible);
            s.Pokemon[p].HeldItem = Item("item.apex_dragon");
            TrainerRules.Refresh(s, ctx);
            int hp = PokemonRules.MaxHp(ctx, s.Pokemon[p]);
            Assert.AreEqual(ctx.Cards.Pokemon[s.Pokemon[p].Def].Health, hp, "실버에는 없다(원작 커뮤니티 확인)");
            s.Seats[0].TrainerCardTier = TrainerRules.Gold;
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(hp + TrainerRules.ApexHealth, PokemonRules.MaxHp(ctx, s.Pokemon[p]));
            var mods = new List<Mod>();
            TrainerRules.PriorityMods(s, ctx, p, mods);
            Assert.AreEqual(TrainerRules.ApexInitiative, Sum(mods));
            mods.Clear();
            int foe = Scene.Give(s, ctx, 1, Bidoof);
            TrainerRules.PowerMods(s, ctx, ctx.Cards.Pokemon[s.Pokemon[p].Def].Signature, p, foe, mods);
            Assert.AreEqual(TrainerRules.ApexPower, Sum(mods));
            s.Pokemon[p].Owner = 1; // 남이 가져가면(Lev — 보너스는 드래곤 조련사 빨강이 준다)
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(hp, PokemonRules.MaxHp(ctx, s.Pokemon[p]));
        }

        static int Sum(List<Mod> mods)
        {
            int n = 0;
            foreach (var m in mods) n += m.Delta;
            return n;
        }

        // ── gentleman R · 프레셔스 볼 ──

        [Test]
        public void A20_6_신사빨강_프레셔스볼은야생을바로잡고_놓아주면돌아온다()
        {
            var s = Battles.Game(ctx, 2);
            Card(s, 0, "gentleman", TrainerRules.Silver);
            Scene.Give(s, ctx, 0, Garchomp);
            Unlock(s, Garchomp);
            Assert.AreEqual(TrainerRules.Gold, s.Seats[0].TrainerCardTier);
            CollectionAssert.Contains(s.Seats[0].CardItems, Item("item.cherish_ball"));
            Assert.IsTrue(TrainerRules.CherishReady(s, ctx, 0));
            int wild = Scene.Spawn(s, ctx, Moderate, SeatId.Wild);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild);
            var c = Battles.Only(s, ChoiceKind.BattleAction);
            var throwIt = ChoiceAnswer.Of(AnswerKind.UseAbility, TrainerRules.CherishTarget);
            Assert.IsTrue(ChoiceRules.Contains(Scene.Legal(s, ctx, c), throwIt));
            Battles.Answer(s, ctx, c, throwIt, log);
            Assert.AreEqual(1, log.OfType<PokemonGained>().FindAll(e => e.PokemonId == wild).Count, "「it automatically captures that Pokémon」");
            Assert.IsTrue(log.OfType<CaptureAttempt>()[0].Success);
            Assert.AreEqual(wild, s.Seats[0].CherishIn);
            Assert.IsFalse(TrainerRules.CherishReady(s, ctx, 0), "볼은 그 포켓몬에");
            Battles.Drive(s, ctx, log);
            s.Battle = null;
            MaintenanceRules.Release(s, ctx, Scene.Fx(s, null, 0), 0, wild);
            Assert.AreEqual(TrainerRules.CherishInHand, s.Seats[0].CherishIn, "「if you release the captured Pokémon, you get the Cherish Ball back」");
            Assert.IsTrue(TrainerRules.CherishReady(s, ctx, 0));
        }

        [Test]
        public void G1_프레셔스볼_배틀중에는놓아줄수없다_트레이너전에는못쓴다_결정84()
        {
            // 사용자 결정 84 T10 ③ 나 + 메모 원작 커뮤니티 확인 — 볼을 잃는 길이 없다
            var t = Field("gentleman", new[] { Garchomp, Bidoof }, new[] { Garchomp });
            t.S.Seats[0].CherishIn = t.Mine[1];
            Assert.IsFalse(ChoiceRules.Contains(Scene.Legal(t.S, ctx, Battles.Only(t.S, ChoiceKind.BattleAction)), ChoiceAnswer.Of(AnswerKind.UseAbility, TrainerRules.CherishTarget)), "야생만");
            var release = new Maintenance { Kind = MaintKind.Release, Pokemon = t.Mine[1], Value = t.S.Pokemon[t.Mine[1]].Def };
            Scene.Reject(Engine.Submit(t.S, ctx, 0, Command.Anytime(release), null), RejectReason.Timing, "배틀 중에는 놓아줄 수 없다");
            Assert.AreEqual(t.Mine[1], t.S.Seats[0].CherishIn, "볼은 그 포켓몬에 그대로");
            t.S.Seats[0].CherishIn = TrainerRules.CherishInHand;
            Assert.IsFalse(ChoiceRules.Contains(Scene.Legal(t.S, ctx, Battles.Only(t.S, ChoiceKind.BattleAction)), ChoiceAnswer.Of(AnswerKind.UseAbility, TrainerRules.CherishTarget)), "트레이너전에는 없다");
        }

        // ── guitarist B·R · 앰프 주사위 ──

        [Test]
        public void A20_6_기타리스트파랑_상태심볼마다앰프_쓰면피해에더하고0으로()
        {
            var s0 = Battles.Game(ctx, 2);
            Card(s0, 0, "guitarist", TrainerRules.Bronze);
            Scene.Give(s0, ctx, 0, Bidoof);
            Unlock(s0, Moderate);
            Assert.AreEqual(0, s0.Seats[0].Amp, "「you receive an Amplifier dice」");
            CollectionAssert.Contains(s0.Seats[0].CardItems, Item("item.amplifier"));

            var t = Field("guitarist", new[] { Garchomp }, new[] { Garchomp, Bidoof }, TrainerRules.Silver, a => a.S.Seats[0].Amp = 1);
            Use(t, "razor_shell", S, S, R);
            Assert.AreEqual(3, t.S.Seats[0].Amp, "심볼 둘 → 1 + 2");
            var ask = Ask(t);
            Assert.IsNotNull(ask, "쓸까(may)");
            Assert.AreEqual(ChoiceSub.Fate, ask.Sub, "결정 134: 굴린 뒤 반응 창의 반응");
            Assert.AreEqual(Window.AfterRoll, ask.Window);
            Assert.IsFalse(ask.HandGated, "공개 반응 — 숨은 손패 창이 아니다");
            var legal = Scene.Legal(t.S, ctx, ask);
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind, "「You may」 — 안 씀이 첫 답");
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind, "기본 답 = 안 씀");
            CollectionAssert.AreEqual(new[] { 1 }, AmpTimes(legal), "전기 기술이 아니면 두 배는 없다");
            Assert.IsTrue(Sinnoh.Ai.PrincipleAgent.Choose(t.S, ctx, ask, legal).Answer.SameAs(Amp(1)), "봇(원칙 AI): 쓴다 [가정 §5 #618]");
            Battles.Answer(t.S, ctx, ask, Amp(1), t.Log);
            Assert.IsNull(Ask(t), "공격당 한 번 — 쓴 뒤 바퀴에 다시 묻지 않는다");
            var d = All<DamageBreakdown>(Events(t))[0];
            Assert.AreEqual(3, EffectFrom(d, new SourceRef(SourceKind.TrainerCard, CardId("guitarist"))), "「add its value as damage」");
            Assert.AreEqual(0, t.S.Seats[0].Amp, "「reset the Amplifier dice」");
        }

        [Test]
        public void G1_앰프주사위_5를넘지않고포획굴림의심볼도센다()
        {
            var t = Field("guitarist", new[] { Garchomp }, new[] { Garchomp, Bidoof }, TrainerRules.Silver, a => a.S.Seats[0].Amp = 4);
            Use(t, "razor_shell", S, S, S);
            Assert.AreEqual(TrainerRules.AmpMax, t.S.Seats[0].Amp, "면 0~5");

            var s = Battles.Game(ctx, 2);
            Card(s, 0, "guitarist", TrainerRules.Silver);
            s.Seats[0].Amp = 0;
            Scene.Give(s, ctx, 0, Garchomp);
            s.Seats[0].Balls[(int)Ball.Regular] = 1;
            int wild = Scene.Spawn(s, ctx, "242:0", SeatId.Wild);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild);
            var c = Battles.Only(s, ChoiceKind.BattleAction);
            s.Rng = new Pcg32(SeedFor(new[] { S, R }));
            Battles.Answer(s, ctx, c, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), log);
            Assert.AreEqual(1, s.Seats[0].Amp, "포획 굴림의 상태 심볼도(원작 커뮤니티 확인)");
        }

        [Test]
        public void A20_6_기타리스트빨강_전기기술이면두배_그배틀에서다시못씀()
        {
            var t = Field("guitarist", new[] { Garchomp }, new[] { "242:0", Bidoof }, TrainerRules.Gold, a => a.S.Seats[0].Amp = 3);
            Use(t, "electro_wheel", R, R, R);
            var ask = Ask(t);
            var legal = Scene.Legal(t.S, ctx, ask);
            CollectionAssert.AreEqual(new[] { 1, 2 }, AmpTimes(legal), "전기 기술 — 두 배를 고를 수 있다");
            Assert.IsTrue(Sinnoh.Ai.PrincipleAgent.Choose(t.S, ctx, ask, legal).Answer.SameAs(Amp(2)), "봇(원칙 AI): 두 배가 있으면 두 배 [가정 §5 #618]");
            Battles.Answer(t.S, ctx, ask, Amp(2), t.Log);
            var d = All<DamageBreakdown>(Events(t))[0];
            Assert.AreEqual(6, EffectFrom(d, new SourceRef(SourceKind.TrainerCard, CardId("guitarist"))), "「double the value of your Amplifier dice」");
            Assert.IsTrue(t.S.Battle.Sides[0].AmpSpent);
            Assert.IsTrue(t.Log.OfType<AbilityUsed>().Exists(e => e.Ability == AbilityColor.Red));
            t.S.Seats[0].Amp = 5;
            for (int guard = 0; guard < 20; guard++)
            {
                var c = t.S.Pending.Find(x => !x.Answered);
                if (c == null || (c.Kind == ChoiceKind.BattleAction && c.Seat == 0)) break;
                Battles.Answer(t.S, ctx, c, c.Default.Clone(), t.Log);
            }
            Assert.IsNotNull(t.S.Battle, "배틀이 이어진다");
            Use(t, "razor_shell", S, R, R);
            Assert.IsNull(Ask(t), "「you can no longer use your Amplifier dice during this battle」");
            Assert.AreEqual(TrainerRules.AmpMax, t.S.Seats[0].Amp, "올리기는 그대로(5에서 멈춤)");
        }

        static ChoiceAnswer Amp(int times) => ChoiceAnswer.Of(AnswerKind.UseAbility, TrainerRules.AmpTarget, times);

        static List<int> AmpTimes(List<ChoiceAnswer> legal) => legal.FindAll(a => a.Kind == AnswerKind.UseAbility && a.Target == TrainerRules.AmpTarget).ConvertAll(a => a.Arg);

        [Test]
        public void G6_결정134_앰프를안쓰고받는쪽이디아루가를내면둘째바퀴에공격자가쓴다()
        {
            // 결정 134: 앰프는 디아루가와 같은 창 · 같은 바퀴(G6 공격하는 쪽 → 받는 쪽, 누가 쓰면 한 바퀴 더). 올리기는 창 앞의 굴림으로 센다 — 디아루가로 심볼이 사라져도 내리지 않는다
            var t = Field("guitarist", new[] { Garchomp }, new[] { Garchomp, Bidoof }, TrainerRules.Silver, a =>
            {
                a.S.Seats[0].Amp = 2;
                a.S.Seats[0].FateHand.Clear();
                a.S.Seats[1].FateHand.Clear();
                a.S.Seats[1].FateHand.Add(FateRules.CardKey(ctx, FateCard.Dialga));
            });
            Use(t, "razor_shell", S, R, R, AttackDieFace.CriticalHit);   // 굴림 셋 → 디아루가가 다시 굴린 눈 = 크리티컬
            for (var c = t.S.Pending.Find(x => x.Kind == ChoiceKind.Reaction && x.Window == Window.BeforeRoll && !x.Answered); c != null; c = t.S.Pending.Find(x => x.Kind == ChoiceKind.Reaction && x.Window == Window.BeforeRoll && !x.Answered))
                Battles.Answer(t.S, ctx, c, ChoiceAnswer.Of(AnswerKind.Decline), t.Log);   // 굴리기 전 창(받는 쪽 손패 — 숨은 손패 창)은 안 씀
            Assert.AreEqual(3, t.S.Seats[0].Amp, "심볼 하나 → 2 + 1(창 앞)");
            var mine = Ask(t);
            var theirs = t.S.Pending.Find(x => x.Kind == ChoiceKind.Reaction && x.Sub == ChoiceSub.Fate && x.Seat == 1 && !x.Answered);
            Assert.IsNotNull(mine, "공격하는 좌석의 앰프");
            Assert.IsNotNull(theirs, "받는 쪽의 디아루가 — 같은 창 · 같은 바퀴");
            Assert.AreEqual(mine.GroupId, theirs.GroupId, "한 묶음(G6 한꺼번에)");
            Battles.Answer(t.S, ctx, mine, ChoiceAnswer.Of(AnswerKind.Decline), t.Log);
            var dialga = ChoiceAnswer.Of(AnswerKind.PlayFate, (int)FateCard.Dialga, 1);
            dialga.Values.Add(0);
            Battles.Answer(t.S, ctx, theirs, dialga, t.Log);
            Assert.AreEqual(AttackDieFace.CriticalHit, t.S.Battle.Faces[0], "디아루가가 심볼을 다시 굴렸다");
            Assert.AreEqual(3, t.S.Seats[0].Amp, "심볼이 사라져도 앰프는 그대로");
            var again = Ask(t);
            Assert.IsNotNull(again, "둘째 바퀴: 공격자가 다시 받는다(한도 안)");
            Battles.Answer(t.S, ctx, again, Amp(1), t.Log);
            var ev = Events(t);
            int dice = ev.FindIndex(e => e is DiceFacesChanged x && x.Source.Kind == SourceKind.Fate);
            int used = ev.FindIndex(e => e is AbilityUsed);
            Assert.IsTrue(dice >= 0 && used > dice, "디아루가 → (다음 바퀴) 앰프");
            Assert.AreEqual(3, EffectFrom(All<DamageBreakdown>(ev)[0], new SourceRef(SourceKind.TrainerCard, CardId("guitarist"))), "앰프 3을 피해에");
            Assert.AreEqual(0, t.S.Seats[0].Amp);
            Assert.IsNull(Ask(t), "셋째 바퀴는 없다");
        }

        // ── parasol_lady B·R · 날씨 휠 ──

        [Test]
        public void A20_6_파라솔아가씨파랑_해금때날씨를고른다_날씨타입기술은휠을가진좌석만2더_결정84()
        {
            var s = Battles.Game(ctx, 2);
            Card(s, 0, "parasol_lady", TrainerRules.Bronze);
            Scene.Give(s, ctx, 0, Bidoof);
            Assert.AreEqual(-1, s.Weather, "날씨 휠 없음");
            var log = new EventLog();
            WinAgainst(s, SlowModerate, log);
            CollectionAssert.Contains(s.Seats[0].CardItems, Item("item.weather_wheel"));
            CollectionAssert.Contains(s.Seats[0].CardItems, Item("item.weather_wheel_base"));
            var c = CardAsk(s);
            Assert.IsNotNull(c, "「on a Weather of your choice」 — 강제 고르기");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, c.Options);
            Assert.AreEqual(1, c.Count);
            log.Clear();
            Battles.Answer(s, ctx, c, ChoiceAnswer.Pick(2), log);
            Assert.AreEqual(2, s.Weather, "비");
            var set = log.OfType<WeatherSet>();
            Assert.AreEqual(1, set.Count);
            Assert.AreEqual(0, set[0].Changes.Length, "첫 날씨는 무료");
            // 「All moves that match the Weather type gain +2 attack strength」 — 휠을 가진 좌석의 기술만(사용자 결정 84 T10 ② 나)
            int wild = Scene.Spawn(s, ctx, Bidoof, SeatId.Wild);
            int mine = s.Seats[0].Party[0];
            int theirs = Scene.Give(s, ctx, 1, Bidoof);
            var mods = new List<Mod>();
            TrainerRules.PowerMods(s, ctx, Scene.Move(ctx, "razor_shell"), mine, wild, mods);
            Assert.AreEqual(TrainerRules.WeatherPower, Sum(mods), "휠을 가진 좌석");
            mods.Clear();
            TrainerRules.PowerMods(s, ctx, Scene.Move(ctx, "razor_shell"), wild, mine, mods);
            Assert.AreEqual(0, Sum(mods), "야생 쪽 기술에는 없다");
            mods.Clear();
            TrainerRules.PowerMods(s, ctx, Scene.Move(ctx, "razor_shell"), theirs, mine, mods);
            Assert.AreEqual(0, Sum(mods), "다른 좌석의 기술에도 없다");
            mods.Clear();
            TrainerRules.PowerMods(s, ctx, Scene.Move(ctx, "electro_wheel"), mine, wild, mods);
            Assert.AreEqual(0, Sum(mods), "다른 타입은 없다");
            CollectionAssert.AreEqual(new[] { PokemonType.Fire, PokemonType.Ice, PokemonType.Water, PokemonType.Rock }, TrainerRules.WeatherTypes, "G1 날씨 휠 네 칸");
        }

        [Test]
        public void G1_날씨휠_누구든사당에서JP3으로바꾼다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 5, log);
            int seat = s.ActiveSeat;
            s.Weather = 0;
            int shrine = s.Tokens.FindIndex(x => x.Kind == TokenKind.Shrine && x.Cell >= 0 && !x.Destroyed);
            s.Seats[seat].Cell = s.Tokens[shrine].Cell;
            s.Seats[seat].Jp = 2;
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.UseAbility && a.Target == TrainerRules.WeatherTarget), "JP 3이 없다");
            s.Seats[seat].Jp = 3;
            legal = Flow.MapLegal(s, ctx);
            var change = ChoiceAnswer.Of(AnswerKind.UseAbility, TrainerRules.WeatherTarget, 3);
            Assert.IsTrue(ChoiceRules.Contains(legal, change), "카드가 없어도 — 「Any trainer」");
            Assert.IsFalse(ChoiceRules.Contains(legal, ChoiceAnswer.Of(AnswerKind.UseAbility, TrainerRules.WeatherTarget, 0)), "지금 날씨로는 아니다");
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, change, log);
            Assert.AreEqual(3, s.Weather, "모래바람");
            Assert.AreEqual(0, s.Seats[seat].Jp);
            Assert.AreEqual(1, log.OfType<WeatherSet>().Count);
            Assert.AreEqual(ChoiceKind.MapAction, Flow.Only(s, ChoiceKind.MapAction).Kind, "턴은 이어진다");
        }

        [Test]
        public void A20_6_파라솔아가씨빨강_날씨타입기술의피해2덜()
        {
            var s = Battles.Game(ctx, 2);
            Card(s, 0, "parasol_lady", TrainerRules.Gold);
            int mine = Scene.Give(s, ctx, 0, Bidoof);
            s.Weather = 2;
            var t = TrainerRules.TakenMods(s, ctx, mine, true, PokemonType.Water);
            Assert.AreEqual(TrainerRules.UmbrellaGuard, t.Reduction, "「take 2 less damage from moves that match the current Weather」");
            Assert.AreEqual(0, TrainerRules.TakenMods(s, ctx, mine, true, PokemonType.Fire).Reduction);
            s.Seats[0].TrainerCardTier = TrainerRules.Silver;
            Assert.AreEqual(0, TrainerRules.TakenMods(s, ctx, mine, true, PokemonType.Water).Reduction, "빨강은 골드부터");
        }

        // ── poke_maniac R · 대타출동 ──

        [Test]
        public void A20_6_포켓몬마니아빨강_대타출동을지닌포켓몬이피해직전에교체해들어온다()
        {
            var s0 = Battles.Game(ctx, 2);
            Card(s0, 0, "poke_maniac", TrainerRules.Silver);
            Scene.Give(s0, ctx, 0, Bidoof);
            Unlock(s0, Garchomp);
            int sub = Item("item.substitute");
            CollectionAssert.Contains(s0.Seats[0].Bag, sub, "「you receive the Substitute held item」");
            Assert.IsTrue(TrainerRules.Holdable(ctx, sub));
            Scene.Accept(MaintenanceRules.Submit(s0, ctx, 0, new Maintenance { Kind = MaintKind.HoldItem, Pokemon = s0.Seats[0].Party[0], Value = sub }, new EventLog()), "지니게 한다");

            var t = Field(null, new[] { Garchomp }, new[] { Bidoof, Garchomp }, TrainerRules.Gold, a =>
            {
                Card(a.S, 1, "poke_maniac", TrainerRules.Gold);
                a.S.Pokemon[a.Theirs[1]].HeldItem = sub;
            });
            t.S.Pokemon[t.D].Status = StatusEffect.Frozen; // 나와 있는 포켓몬이 얼음이어도(원작 커뮤니티 확인)
            Use(t, "razor_shell", R, R, R);
            var ask = Ask(t);
            Assert.IsNotNull(ask, "피해 직전에 묻는다");
            Assert.AreEqual(1, ask.Seat);
            Assert.AreEqual(ChoiceSub.EffectOption, ask.Sub);
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind, "may");
            Battles.Answer(t.S, ctx, ask, ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
            var ev = Events(t);
            Assert.AreEqual(t.Theirs[1], All<SentIn>(ev)[0].PokemonId, "교체해 들어온다");
            Assert.AreEqual(t.Theirs[1], All<DamageBreakdown>(ev)[0].TargetId, "피해는 대타출동이 받는다");
            Assert.AreEqual(t.Theirs[1], t.S.Battle.Sides[1].Active);
        }

        // ── tourist B·R · 랜드마크 투어 ──

        [Test]
        public void A20_6_관광객파랑_유물전설장소에서도장과스냅()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 5, log);
            int seat = s.ActiveSeat;
            Card(s, seat, "tourist", TrainerRules.Silver);
            var marks = TrainerRules.Landmarks(ctx);
            Assert.AreEqual(8, marks.Count, "G1 — 유물 전설 장소 8곳");
            int f = marks[0];
            s.Seats[seat].Cell = ctx.Fixtures.Fixtures[f].Cell;
            var stamp = ChoiceAnswer.Of(AnswerKind.UseAbility, f);
            Assert.IsTrue(ChoiceRules.Contains(Flow.MapLegal(s, ctx), stamp));
            int site = LegendRules.SiteCard(s, ctx, f);
            int deck = s.Decks[(int)DeckId.Legendary].Count;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, stamp, log);
            CollectionAssert.AreEqual(new[] { site }, s.Seats[seat].Snaps, "「make a clone of its card」");
            Assert.AreEqual(deck, s.Decks[(int)DeckId.Legendary].Count, "복사본 — 덱은 그대로");
            Assert.AreEqual(1, s.Seats[seat].Tour);
            Assert.IsFalse(ChoiceRules.Contains(Flow.MapLegal(s, ctx), stamp), "「Once at each」");
        }

        [Test]
        public void A20_6_관광객_빈시트부터8곳을차례로찍으면_JP30은다찍은때한번()
        {
            // 자체 검토: 봇 판에 tourist R 완성이 오지 않아 빈 시트부터 맵 행동만으로 8곳을 다 돈다(앞 시험은 일곱 곳을 손으로 채웠다)
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 5, log);
            int seat = s.ActiveSeat;
            Card(s, seat, "tourist", TrainerRules.Gold);
            var marks = TrainerRules.Landmarks(ctx);
            int jp = s.Seats[seat].Jp;
            int from = log.Events.Count;
            for (int i = 0; i < marks.Count; i++)
            {
                Assert.AreEqual(jp, s.Seats[seat].Jp, $"{i}곳: 다 찍기 전에는 JP 그대로");
                s.Seats[seat].Cell = ctx.Fixtures.Fixtures[marks[i]].Cell;
                var stamp = ChoiceAnswer.Of(AnswerKind.UseAbility, marks[i]);
                Assert.IsTrue(ChoiceRules.Contains(Flow.MapLegal(s, ctx), stamp), $"고정물 {marks[i]}에서 도장");
                Flow.Answer(s, ctx, ChoiceKind.MapAction, stamp, log);
            }
            Assert.AreEqual((1 << marks.Count) - 1, s.Seats[seat].Tour, "8곳 모두");
            Assert.AreEqual(jp + TrainerRules.TourJp, s.Seats[seat].Jp, "「if you complete your Landmark Tour sheet, gain 30 Journey Points」");
            var used = log.Events.GetRange(from, log.Events.Count - from).FindAll(e => e is AbilityUsed);
            Assert.AreEqual(marks.Count, used.FindAll(e => ((AbilityUsed)e).Ability == AbilityColor.Blue).Count, "도장마다 파랑");
            Assert.AreEqual(1, used.FindAll(e => ((AbilityUsed)e).Ability == AbilityColor.Red).Count, "완성 JP 30은 한 번");
            Assert.Greater(s.Seats[seat].Snaps.Count, 0, "전설 스냅");
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.UseAbility && a.Target >= 0), "다 찍은 시트에는 도장이 없다");
        }

        [Test]
        public void A20_6_관광객빨강_스냅을기술로가르치고_8곳을채우면JP30()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 5, log);
            int seat = s.ActiveSeat;
            Card(s, seat, "tourist", TrainerRules.Gold);
            var marks = TrainerRules.Landmarks(ctx);
            s.Seats[seat].Tour = (1 << 8) - 1 - 1; // 첫 곳만 남았다
            int f = marks[0];
            s.Seats[seat].Cell = ctx.Fixtures.Fixtures[f].Cell;
            int jp = s.Seats[seat].Jp;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.UseAbility, f), log);
            Assert.AreEqual(jp + TrainerRules.TourJp, s.Seats[seat].Jp, "「if you complete your Landmark Tour sheet, gain 30 Journey Points」");
            int snap = s.Seats[seat].Snaps[0];
            int move = ctx.Cards.Pokemon[snap].Signature;
            var type = ctx.Cards.Moves[move].Type;
            string key = null;
            for (int d = 0; d < ctx.Cards.Pokemon.Count && key == null; d++)
                if (ctx.Cards.Pokemon[d].Tier == EncounterTier.Weak && ChoiceRules.CanLearn(ctx, new PokemonState { Def = d }, type)) key = ctx.Cards.Pokemon[d].Key;
            Assert.IsNotNull(key, "그 타입을 배우는 약한 포켓몬");
            int learner = Scene.Give(s, ctx, seat, key);
            Scene.Accept(MaintenanceRules.Submit(s, ctx, seat, new Maintenance { Kind = MaintKind.SetMove, Pokemon = learner, Slot = 0, Value = move }, log), "「teach the Legendary Snaps … as moves」");
            Assert.AreEqual(move, s.Pokemon[learner].Moves[0]);
            Assert.IsEmpty(s.Seats[seat].Snaps, "가르친 스냅은 쓴다");
        }
    }
}
