using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「제작·사당·스탬프·진화·파티」(rulings A39 — 능력 13): ace_trainer B · chef B · picnicker R · poke_fan R · expert B · collector B·R · interviewers R · lady B ·
    /// pokemon_breeder B·R · pokemon_ranger B·R.
    /// </summary>
    public class TrainerCraftTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const string Bidoof = "399:0", Bibarel = "400:0", Starly = "396:0", Staravia = "397:0", Garchomp = "445:0";

        SourceRef Card(GameState s, int seat, string key, int tier = TrainerRules.Gold)
        {
            int card = ctx.Cards.TrainerCardIds.IndexOf(key);
            s.Seats[seat].TrainerCard = card;
            s.Seats[seat].TrainerCardTier = tier;
            TrainerRules.Refresh(s, ctx);
            return new SourceRef(SourceKind.TrainerCard, card);
        }

        int Item(string key) => Scene.Item(ctx, key);

        static void Fill(SeatState st, int n)
        {
            for (int i = 0; i < st.Apricorns.Length; i++) st.Apricorns[i] = n;
        }

        static SubmitResult Maint(GameState s, Ctx ctx, int seat, Maintenance m, EventLog log = null) => MaintenanceRules.Submit(s, ctx, seat, m, log ?? new EventLog());

        // ── ace_trainer B ──

        [Test]
        public void A20_6_에이스트레이너_조우당한번다시찾아보기_배틀아이템은색마다하나()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 22, log);
            int seat = s.ActiveSeat;
            Card(s, seat, "ace_trainer", TrainerRules.Silver);
            s.Seats[seat].Cell = s.Tokens[Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Moderate)].Cell;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Search), log);
            var pick = Flow.Only(s, ChoiceKind.EncounterPick);
            var legal = Scene.Legal(s, ctx, pick);
            Assert.IsTrue(legal.Exists(a => a.Kind == AnswerKind.UseAbility), "다시 찾아보기");
            Assert.AreEqual(AnswerKind.Pick, pick.Default.Kind, "기본 답은 카드 고르기");
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.EncounterPick, ChoiceAnswer.Of(AnswerKind.UseAbility), log);
            Assert.AreEqual(1, log.OfType<CardsReturned>().Count, "본 카드는 덱으로");
            Assert.AreEqual(1, log.OfType<Searched>().Count, "다시 찾아본다");
            var again = Flow.Only(s, ChoiceKind.EncounterPick);
            Assert.IsFalse(Scene.Legal(s, ctx, again).Exists(a => a.Kind == AnswerKind.UseAbility), "조우당 한 번");

            // 배틀 아이템: 레시피의 색마다 하나
            var b = Battles.Game(ctx, 2);
            int band = Item("item.choice_band");
            var recipe = ctx.Items.Items[band].Recipe;
            var distinct = new List<ApricornColor>();
            foreach (var c in recipe)
                if (!distinct.Contains(c)) distinct.Add(c);
            Assert.Less(distinct.Count, recipe.Count, "같은 색이 겹친 레시피");
            Fill(b.Seats[0], 0);
            foreach (var c in distinct) b.Seats[0].Apricorns[(int)c]++;
            Assert.IsFalse(CraftRules.CanCraft(b, ctx, 0, band));
            Card(b, 0, "ace_trainer", TrainerRules.Silver);
            Assert.IsTrue(CraftRules.CanCraft(b, ctx, 0, band));
            CraftRules.Apply(b, ctx, Scene.Fx(b, null, 0), 0, band);
            Assert.AreEqual(0, b.Seats[0].Apricorns[(int)distinct[0]], "색마다 하나만 낸다");
            CollectionAssert.Contains(b.Seats[0].Bag, band);
            int stew = Item("item.restoring_stew");
            Fill(b.Seats[0], 0);
            foreach (var c in ctx.Items.Items[stew].Recipe) b.Seats[0].Apricorns[(int)c]++;
            Assert.AreSame(ctx.Items.Items[stew].Recipe, TrainerRules.Recipe(b, ctx, 0, ctx.Items.Items[stew]), "스튜·볼은 그대로");
        }

        // ── chef B ──

        [Test]
        public void A20_6_요리사_같은단계규토리를바꾼다()
        {
            var s = Battles.Game(ctx, 2);
            Fill(s.Seats[0], 0);
            s.Seats[0].Apricorns[(int)ApricornColor.Red] = 1;
            var m = new Maintenance { Kind = MaintKind.Convert, Slot = (int)ApricornColor.Red, Value = (int)ApricornColor.Blue };
            Scene.Reject(Maint(s, ctx, 0, m), RejectReason.Illegal, "카드 없음");
            Card(s, 0, "chef", TrainerRules.Silver);
            Assert.AreEqual(MapActions.ApricornGrade(ApricornColor.Red), MapActions.ApricornGrade(ApricornColor.Blue));
            var log = new EventLog();
            Scene.Accept(Maint(s, ctx, 0, m, log));
            Assert.AreEqual(0, s.Seats[0].Apricorns[(int)ApricornColor.Red]);
            Assert.AreEqual(1, s.Seats[0].Apricorns[(int)ApricornColor.Blue]);
            Assert.AreEqual(1, log.OfType<ApricornsGained>().Count);
            Scene.Reject(Maint(s, ctx, 0, new Maintenance { Kind = MaintKind.Convert, Slot = (int)ApricornColor.Blue, Value = (int)ApricornColor.Black }), RejectReason.Illegal, "다른 단계");
            Scene.Reject(Maint(s, ctx, 0, m), RejectReason.Illegal, "빨강이 없다");
        }

        // ── picnicker R · poke_fan R ──

        [Test]
        public void A20_6_피크닉걸빨강_스튜를만들면다른스튜효과공짜()
        {
            var s = Battles.Game(ctx, 2);
            int restoring = Item("item.restoring_stew"), zesty = Item("item.zesty_stew");
            Fill(s.Seats[0], 3);
            Card(s, 0, "picnicker");
            var legal = new List<ChoiceAnswer>();
            CraftRules.Legal(s, ctx, 0, legal);
            Assert.IsTrue(ChoiceRules.Contains(legal, ChoiceAnswer.Of(AnswerKind.Craft, restoring)), "공짜 몫 없이도");
            Assert.IsTrue(ChoiceRules.Contains(legal, ChoiceAnswer.Of(AnswerKind.Craft, restoring, zesty)));
            Assert.IsFalse(ChoiceRules.Contains(legal, ChoiceAnswer.Of(AnswerKind.Craft, restoring, restoring)), "다른 스튜");
            int moves = s.Seats[0].MovesLeft;
            var log = new EventLog();
            CraftRules.Apply(s, ctx, Scene.Fx(s, log, 0), 0, restoring, zesty);
            var eaten = log.OfType<StewEaten>();
            Assert.AreEqual(2, eaten.Count);
            Assert.AreEqual(zesty, eaten[1].ItemKey);
            Assert.AreEqual(moves + CraftRules.ZestyMoves, s.Seats[0].MovesLeft, "자극적인 스튜의 효과");
            Assert.IsTrue(CraftRules.CanCraft(s, ctx, 0, zesty), "공짜로 받은 스튜는 만든 것이 아니다");
        }

        [Test]
        public void A20_6_포켓몬팬빨강_배틀아이템을만들면하나더()
        {
            var s = Battles.Game(ctx, 2);
            int band = Item("item.choice_band"), sash = Item("item.swiftness_sash");
            Fill(s.Seats[0], 5);
            var legal = new List<ChoiceAnswer>();
            CraftRules.Legal(s, ctx, 0, legal);
            Assert.IsFalse(ChoiceRules.Contains(legal, ChoiceAnswer.Of(AnswerKind.Craft, band, sash)), "카드 없음");
            Card(s, 0, "poke_fan");
            legal.Clear();
            CraftRules.Legal(s, ctx, 0, legal);
            Assert.IsTrue(ChoiceRules.Contains(legal, ChoiceAnswer.Of(AnswerKind.Craft, band, sash)));
            Assert.IsTrue(ChoiceRules.Contains(legal, ChoiceAnswer.Of(AnswerKind.Craft, band, band)), "같은 것도 하나 더");
            var log = new EventLog();
            CraftRules.Apply(s, ctx, Scene.Fx(s, log, 0), 0, band, sash);
            CollectionAssert.AreEquivalent(new[] { band, sash }, s.Seats[0].Bag);
            Assert.AreEqual(2, System.Array.FindAll(log.OfType<Crafted>()[0].Changes, x => x.Res == ResKind.Item).Length, "제작 이벤트에 둘 다");
        }

        // ── expert B ──

        [Test]
        public void A20_6_전문가_사당카드1장더뽑고쓰는수는그대로()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 12, log);
            int seat = s.ActiveSeat;
            Card(s, seat, "expert", TrainerRules.Silver);
            int shrine = -1;
            for (int f = 0; f < ctx.Fixtures.Fixtures.Count && shrine < 0; f++)
                if (ctx.Fixtures.Fixtures[f].Kind == FixtureKind.Shrine) shrine = f;
            s.Seats[seat].Cell = ctx.Fixtures.Fixtures[shrine].Cell;
            s.Seats[seat].Apricorns[(int)ApricornColor.Pink] = 1;
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Offer, shrine, (int)ApricornColor.Pink), log);
            Assert.AreEqual(2, log.OfType<ShrineCardsDrawn>()[0].CardKeys.Length, "1장 + 1장");
            Assert.IsTrue(s.Shrine == null || s.Shrine.Picks <= 1, "쓰는 수는 1장 그대로");
        }

        // ── collector B·R ──

        [Test]
        public void A20_6_수집가_스탬프보상두번()
        {
            var s = Battles.Game(ctx, 2);
            Fill(s.Seats[0], 0);
            Card(s, 0, "collector", TrainerRules.Silver);
            StampRules.Gain(s, ctx, Scene.Fx(s, null, 0), 0, StampRules.TrainerWin);
            Assert.AreEqual(2, s.Seats[0].Apricorns[(int)ApricornColor.Red], "보상 1(빨강·파랑·노랑) 두 번");
            Assert.AreEqual(2, s.Seats[0].Apricorns[(int)ApricornColor.Yellow]);
        }

        GameState Pool(CollectorDiscards mode, out int a, out int b)
        {
            var s = Battles.Game(ctx, 2, 7, new RuleOptions { CollectorDiscards = mode });
            Card(s, 0, "collector");
            Scene.Give(s, ctx, 0, Garchomp);
            a = Scene.Give(s, ctx, 0, Bidoof);
            b = Scene.Give(s, ctx, 0, Starly);
            return s;
        }

        [Test]
        public void A20_6_수집가빨강_풀의카드를포켓몬으로_놓아준포켓몬은풀로_턴에한장()
        {
            var s = Pool(CollectorDiscards.OncePerTurn, out int a, out int b);
            int bidoof = s.Pokemon[a].Def;
            var st = s.Seats[0];
            Scene.Accept(Maint(s, ctx, 0, new Maintenance { Kind = MaintKind.Release, Pokemon = a, Value = bidoof }));
            CollectionAssert.Contains(st.StampCards, bidoof, "놓아준 포켓몬은 카드째 풀로");
            CollectionAssert.Contains(st.StampMoves, ctx.Cards.Pokemon[bidoof].Signature);
            Assert.IsFalse(s.Discards[(int)DeckId.Weak].Contains(bidoof), "버림 더미로 가지 않는다");
            int starly = s.Pokemon[b].Def;
            Scene.Accept(Maint(s, ctx, 0, new Maintenance { Kind = MaintKind.Release, Pokemon = b, Value = starly }));
            Assert.IsFalse(st.StampCards.Contains(starly), "턴에 한 장(기본)");

            var log = new EventLog();
            Scene.Accept(Maint(s, ctx, 0, new Maintenance { Kind = MaintKind.Adopt, Value = bidoof }, log));
            Assert.AreEqual(1, log.OfType<PokemonGained>().Count, "풀의 카드를 포켓몬으로 파티에");
            Assert.IsFalse(st.StampCards.Contains(bidoof));
            Assert.IsFalse(st.StampMoves.Contains(ctx.Cards.Pokemon[bidoof].Signature), "그 기술은 풀에서 빠진다");
            Assert.AreEqual(2, PokemonRules.PartyCount(st));
        }

        [Test]
        public void A20_6_수집가빨강_옵션_문구대로는매번_없음은안함()
        {
            var w = Pool(CollectorDiscards.AsWritten, out int a, out int b);
            Scene.Accept(Maint(w, ctx, 0, new Maintenance { Kind = MaintKind.Release, Pokemon = a, Value = w.Pokemon[a].Def }));
            int sd = w.Pokemon[b].Def;
            Scene.Accept(Maint(w, ctx, 0, new Maintenance { Kind = MaintKind.Release, Pokemon = b, Value = sd }));
            Assert.AreEqual(4, w.Seats[0].StampCards.Count, "문구대로: 둘 다");
            var off = Pool(CollectorDiscards.Off, out int c, out _);
            int cd = off.Pokemon[c].Def;
            Scene.Accept(Maint(off, ctx, 0, new Maintenance { Kind = MaintKind.Release, Pokemon = c, Value = cd }));
            Assert.AreEqual(0, off.Seats[0].StampCards.Count, "없음");
            Assert.IsTrue(off.Discards[(int)DeckId.Weak].Contains(cd));
        }

        // ── interviewers R ──

        [Test]
        public void A20_6_인터뷰어빨강_배운기술을파티안에서맞바꾼다_배울수있어야()
        {
            var s = Battles.Game(ctx, 2);
            int a = Scene.Give(s, ctx, 0, Bidoof), b = Scene.Give(s, ctx, 0, Starly);
            int punch = -1, steel = -1;
            for (int m = 0; m < ctx.Cards.Moves.Count; m++)
            {
                var t = ctx.Cards.Moves[m].Type;
                if (punch < 0 && t == PokemonType.Fighting) punch = m;
                if (steel < 0 && t == PokemonType.Steel) steel = m;
            }
            s.Pokemon[a].Moves[0] = punch;
            s.Pokemon[b].Moves[1] = steel;
            var swap = new Maintenance { Kind = MaintKind.PassMove, Pokemon = a, Slot = 0, Value = b, Values = new List<int> { 1 } };
            Scene.Reject(Maint(s, ctx, 0, swap), RejectReason.Illegal, "카드 없음");
            Card(s, 0, "interviewers");
            Scene.Reject(Maint(s, ctx, 0, swap), RejectReason.Illegal, "비버니는 강철을 못 배운다");
            var give = new Maintenance { Kind = MaintKind.PassMove, Pokemon = a, Slot = 0, Value = b, Values = new List<int> { 0 } };
            var log = new EventLog();
            Scene.Accept(Maint(s, ctx, 0, give, log));
            Assert.AreEqual(-1, s.Pokemon[a].Moves[0]);
            Assert.AreEqual(punch, s.Pokemon[b].Moves[0], "격투 기술을 찌르꼬에게");
            Assert.AreEqual(1, log.OfType<MoveTaught>().Count);
            s.ActiveSeat = 1;
            Scene.Reject(Maint(s, ctx, 0, new Maintenance { Kind = MaintKind.PassMove, Pokemon = b, Slot = 0, Value = a, Values = new List<int> { 0 } }), RejectReason.Timing, "내 턴에만");
        }

        [Test]
        public void A52_균열카드가막은칸은_인터뷰어맞바꾸기와브리더알기술에서빠진다()
        {
            // 감사 G4-2 · G4-3(11-20): 「카드가 있는 칸은 가르치기의 합법 답에서 빠진다」(rulings A52 ①) — PassMove · EggAnswers가 Blocked를 보지 않았다
            var s = Battles.Game(ctx, 2);
            int a = Scene.Give(s, ctx, 0, Bidoof), b = Scene.Give(s, ctx, 0, Starly);
            int normal = -1;
            for (int m = 0; m < ctx.Cards.Moves.Count && normal < 0; m++)
                if (ctx.Cards.Moves[m].Type == PokemonType.Normal) normal = m;
            Card(s, 0, "interviewers");
            s.Pokemon[a].Moves[0] = -1;
            s.Pokemon[a].Breach[0] = 0; // 균열 카드가 칸 0을 막았다(Moves = −1)
            s.Pokemon[b].Moves[1] = normal;
            Scene.Reject(Maint(s, ctx, 0, new Maintenance { Kind = MaintKind.PassMove, Pokemon = a, Slot = 0, Value = b, Values = new List<int> { 1 } }), RejectReason.Illegal, "막힌 칸으로 받기");
            Scene.Reject(Maint(s, ctx, 0, new Maintenance { Kind = MaintKind.PassMove, Pokemon = b, Slot = 1, Value = a, Values = new List<int> { 0 } }), RejectReason.Illegal, "막힌 칸으로 주기");
            Assert.AreEqual(-1, s.Pokemon[a].Moves[0]);
            Assert.AreEqual(normal, s.Pokemon[b].Moves[1]);

            Card(s, 0, "pokemon_breeder", TrainerRules.Silver);
            s.Seats[0].Jp = 20;
            var legal = new List<ChoiceAnswer>();
            EvolveRules.Legal(s, ctx, 0, legal);
            var egg = legal.FindAll(x => x.Kind == AnswerKind.Evolve && x.Target == a && x.Arg == TrainerRules.EggArg);
            Assert.AreEqual(1, egg.Count, "막힌 칸 0은 빠지고 칸 1만");
            Assert.AreEqual(1, egg[0].Values[1]);
        }

        // ── lady B ──

        [Test]
        public void A20_6_아가씨_해금되면JP7_진화비용2덜()
        {
            var s = Battles.Game(ctx, 2);
            int p = Scene.Give(s, ctx, 0, Bidoof);
            int cost = EvolveRules.Cost(s, ctx, 0, p);
            s.Seats[0].TrainerCard = ctx.Cards.TrainerCardIds.IndexOf("lady");
            s.Seats[0].TrainerCardTier = TrainerRules.Bronze;
            int jp = s.Seats[0].Jp;
            int moderate = Scene.Spawn(s, ctx, Bibarel, SeatId.Wild);
            s.Pokemon[moderate].Owner = 0;
            var log = new EventLog();
            TrainerRules.Obtained(s, ctx, Scene.Fx(s, log, 0), 0, moderate, ObtainCause.CardEffect);
            Assert.AreEqual(TrainerRules.Silver, s.Seats[0].TrainerCardTier);
            Assert.AreEqual(jp + TrainerRules.HeritageJp, s.Seats[0].Jp, "해금될 때 JP 7");
            Assert.AreEqual(cost - TrainerRules.HeritageDiscount, EvolveRules.Cost(s, ctx, 0, p));
        }

        // ── pokemon_breeder B·R ──

        [Test]
        public void A20_6_포켓몬브리더_진화대신알기술_더는진화못함_빨강은체력우선도5()
        {
            var s = Battles.Game(ctx, 2);
            int p = Scene.Give(s, ctx, 0, Bidoof);
            s.Seats[0].Jp = 20;
            var legal = new List<ChoiceAnswer>();
            EvolveRules.Legal(s, ctx, 0, legal);
            Assert.IsFalse(legal.Exists(a => a.Arg == TrainerRules.EggArg), "카드 없음");
            Card(s, 0, "pokemon_breeder", TrainerRules.Silver);
            legal.Clear();
            EvolveRules.Legal(s, ctx, 0, legal);
            var egg = legal.FindAll(a => a.Kind == AnswerKind.Evolve && a.Arg == TrainerRules.EggArg);
            Assert.AreEqual(PokemonState.MoveSlots, egg.Count, "진화형 하나 × 칸 둘");
            Assert.AreEqual(Scene.Def(ctx, Bibarel), egg[0].Values[0]);
            int hp = PokemonRules.MaxHp(ctx, s.Pokemon[p]);
            TrainerRules.Hatch(s, ctx, Scene.Fx(s, null, 0), 0, egg[0]);
            Assert.AreEqual(Scene.Def(ctx, Bidoof), s.Pokemon[p].Def, "진화하지 않는다");
            Assert.AreEqual(ctx.Cards.Pokemon[Scene.Def(ctx, Bibarel)].Signature, s.Pokemon[p].Moves[0], "진화형의 기술");
            Assert.AreEqual(20 - EvolveRules.Cost(s, ctx, 0, p), s.Seats[0].Jp, "진화 비용을 낸다");
            Assert.IsTrue(s.Pokemon[p].EggMove);
            Assert.IsFalse(EvolveRules.CanEvolve(s, ctx, 0, p), "더는 진화 못 함");
            Assert.AreEqual(hp, PokemonRules.MaxHp(ctx, s.Pokemon[p]), "실버: 빨강 없음");
            Card(s, 0, "pokemon_breeder");
            Assert.AreEqual(hp + TrainerRules.PerfectIvs, PokemonRules.MaxHp(ctx, s.Pokemon[p]));
            var mods = new List<Mod>();
            TrainerRules.PriorityMods(s, ctx, p, mods);
            Assert.AreEqual(TrainerRules.PerfectIvs, mods[0].Delta);
            s.Pokemon[p].Moves[0] = -1;
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(hp + TrainerRules.PerfectIvs, PokemonRules.MaxHp(ctx, s.Pokemon[p]), "알 기술을 버려도 남는다(원작 커뮤니티 확인)");
        }

        // ── pokemon_ranger B·R ──

        [Test]
        public void A20_6_포켓몬레인저_잠긴칸에보관_배틀에는못나옴()
        {
            var s = Battles.Game(ctx, 2);
            var st = s.Seats[0];
            for (int i = 0; i < st.OpenSlots; i++) Scene.Give(s, ctx, 0, Bidoof);
            int extra = Scene.Spawn(s, ctx, Starly, 0);
            Assert.AreEqual(-1, PokemonRules.AddToParty(st, extra), "카드 없음: 잠긴 칸에 못 둔다");
            Card(s, 0, "pokemon_ranger", TrainerRules.Silver);
            int slot = PokemonRules.AddToParty(st, extra);
            Assert.AreEqual(st.OpenSlots, slot, "잠긴 첫 칸");
            Assert.IsFalse(PokemonRules.Fights(st, slot));
            var log = new EventLog();
            Scene.Give(s, ctx, 1, Garchomp);
            BattleFlow.StartTrainer(s, ctx, Scene.Fx(s, log, 0, ActionKind.Challenge), 0, 1);
            CollectionAssert.DoesNotContain(s.Battle.Sides[0].Roster, extra, "보관한 포켓몬은 배틀에 못 나온다");
            Assert.AreEqual(st.OpenSlots, s.Battle.Sides[0].Roster.Count);
        }

        [Test]
        public void A20_6_포켓몬레인저빨강_보통포켓몬은연칸마다체력우선도1()
        {
            var s = Battles.Game(ctx, 2);
            int p = Scene.Give(s, ctx, 0, Bibarel);
            int w = Scene.Give(s, ctx, 0, Bidoof);
            int hp = PokemonRules.MaxHp(ctx, s.Pokemon[p]);
            s.Seats[0].OpenSlots = CampaignRules.OpenPartySlots(s.Campaign, 0) + 2;
            Card(s, 0, "pokemon_ranger");
            Assert.AreEqual(hp + 2, PokemonRules.MaxHp(ctx, s.Pokemon[p]), "조각으로 연 칸 둘");
            var mods = new List<Mod>();
            TrainerRules.PriorityMods(s, ctx, p, mods);
            Assert.AreEqual(2, mods[0].Delta);
            mods.Clear();
            TrainerRules.PriorityMods(s, ctx, w, mods);
            Assert.AreEqual(0, mods.Count, "보통만");
            s.Seats[0].OpenSlots = CampaignRules.OpenPartySlots(s.Campaign, 0);
            TrainerRules.Refresh(s, ctx);
            Assert.AreEqual(hp, PokemonRules.MaxHp(ctx, s.Pokemon[p]), "처음부터 열린 칸은 세지 않는다");
        }

        [Test]
        public void A20_7_레인저빨강_UB판은보통포켓몬에체력우선도3을바로()
        {
            // 「In the Ultra Burst campaign, you get the maximum of +3 health and +3 initiative immediately」
            var s = UbScenes.Bare(ctx, 1);
            int p = Scene.Give(s, ctx, 0, Bibarel);
            int w = Scene.Give(s, ctx, 0, Bidoof);
            int hp = PokemonRules.MaxHp(ctx, s.Pokemon[p]);
            Card(s, 0, "pokemon_ranger");
            Assert.AreEqual(hp + 3, PokemonRules.MaxHp(ctx, s.Pokemon[p]), "연 칸 없이도 +3");
            var mods = new List<Mod>();
            TrainerRules.PriorityMods(s, ctx, p, mods);
            Assert.AreEqual(3, mods[0].Delta);
            mods.Clear();
            TrainerRules.PriorityMods(s, ctx, w, mods);
            Assert.AreEqual(0, mods.Count, "보통만");
        }

        [Test]
        public void A20_7_레인저파랑_보관한균열카드는내턴정비로꺼내붙인다_결정87()
        {
            // 사용자 결정 87 R4(「보관한거 맘대로 붙일수는 없는거야? 내가볼땐 붙일수 있을거 같은데?」) — 막는 문구·제작자 답이 없다(rulings A44 ③): 정비 AttachBreach
            var s = UbScenes.Started(ctx, 2, 3);
            UbScenes.Attach(s, ctx);
            int me = s.ActiveSeat, other = 1 - me;
            Card(s, me, "pokemon_ranger", TrainerRules.Silver);
            int mon = Scene.Give(s, ctx, me, Bidoof), theirs = Scene.Give(s, ctx, other, Bidoof);
            var deck = s.Decks[(int)DeckId.DistortionBreach];
            int Draw()
            {
                int card = deck[deck.Count - 1];
                deck.RemoveAt(deck.Count - 1);
                return card;
            }
            int c0 = Draw(), c1 = Draw();
            s.Ub.BreachStored.AddRange(new[] { me, c0, me, c1 });
            s.Pokemon[mon].Moves[1] = 0; // 가르친 기술이 있는 칸 — 붙이면 버린다
            int cards = Playout.CardCount(s);
            Maintenance At(int pokemon, int slot, int card) => new Maintenance { Kind = MaintKind.AttachBreach, Pokemon = pokemon, Slot = slot, Value = card };
            SubmitResult Do(int seat, Maintenance m, EventLog l = null) => Engine.Submit(s, ctx, seat, Command.Anytime(m), l);
            Scene.Reject(Do(other, At(theirs, 0, c0)), RejectReason.Illegal, "보관하지 않은 좌석");
            Scene.Reject(Do(me, At(theirs, 0, c0)), RejectReason.Illegal, "남의 포켓몬");
            Scene.Reject(Do(me, At(mon, 2, c0)), RejectReason.Illegal, "없는 칸");
            var log = new EventLog();
            Scene.Accept(Do(me, At(mon, 0, c0), log));
            Assert.AreEqual(c0, s.Pokemon[mon].Breach[0]);
            Assert.AreEqual(1, BreachRules.StoredCount(s, me));
            var put = Flow.Of<BreachCardAttached>(log)[0];
            Assert.AreEqual((me, mon, 0, c0, -1), (put.Seat, put.PokemonId, put.Slot, put.CardKey, put.ReplacedKey));
            Scene.Reject(Do(me, At(mon, 0, c1)), RejectReason.Illegal, "균열 카드가 있는 칸");
            Assert.AreEqual(me, Flow.Only(s, ChoiceKind.MapAction).Seat, "맵 행동은 그대로 열려 있다");
            Scene.Accept(Do(me, At(mon, 1, c1), log));
            Assert.AreEqual(0, Flow.Of<BreachCardAttached>(log)[1].ReplacedKey, "가르친 기술은 버린다(붙이기 규칙 그대로)");
            Assert.AreEqual(-1, s.Pokemon[mon].Moves[1]);
            Assert.AreEqual(0, BreachRules.StoredCount(s, me));
            Assert.AreEqual(cards, Playout.CardCount(s), "카드 총수 보존");
            // 내 턴이 아니면 못 한다
            int c2 = Draw();
            s.Ub.BreachStored.AddRange(new[] { other, c2 });
            Scene.Reject(Do(other, At(theirs, 0, c2)), RejectReason.Timing, "내 턴에만");
        }

        [Test]
        public void A20_7_레인저파랑_UB판은받을때보관할지묻는다_빈칸마다2장_넘치면붙인다_결정84()
        {
            // 「In the Ultra Burst campaign, you may store up to 2 Distortion Breach cards in each empty slot」 — 「may」: 붙이기 물음에서 보관(첫 답·시간 초과)을 고른다(사용자 결정 84 U11 나, rulings A42)
            var s = UbScenes.Started(ctx, 1, 3);
            var st = s.Seats[0];
            Card(s, 0, "pokemon_ranger", TrainerRules.Silver);
            while (PokemonRules.PartyCount(st) < st.Party.Length - 1) Scene.Give(s, ctx, 0, Bidoof);
            Assert.AreEqual(TrainerRules.RangerBreachPerSlot, TrainerRules.BreachStorage(s, ctx, 0), "빈 칸 하나 × 2");
            var deck = s.Decks[(int)DeckId.DistortionBreach];
            void Deal()
            {
                s.Ub.BreachHands.Add(0);
                s.Ub.BreachHands.Add(deck[deck.Count - 1]);
                deck.RemoveAt(deck.Count - 1);
            }
            Deal();
            Deal();
            var log = new EventLog();
            void NextTurn()
            {
                Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log); // 혼자: 라운드 끝 → (건너뛰기 물음) → UB 턴 → 다음 턴 시작
                UbScenes.NoSkip(s, ctx, log);
            }
            NextTurn();
            for (int n = 0; n < 2; n++)
            {
                var ask = Battles.Only(s, ChoiceKind.Simultaneous, ChoiceSub.BreachAttach);
                Assert.AreEqual(AnswerKind.Decline, Scene.Legal(s, ctx, ask)[0].Kind, "보관할 자리가 있다 — 보관이 첫 답");
                Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind, "보관이 기본 답");
                Assert.IsTrue(Scene.Legal(s, ctx, ask).Exists(a => a.Kind == AnswerKind.Pick), "붙일 수도 있다");
                Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(ask.Id), log));
            }
            Assert.AreEqual(0, UbTurn.BreachHand(s, 0).Count);
            Assert.AreEqual(2, BreachRules.StoredCount(s, 0), "두 장을 보관");
            Assert.AreEqual(0, Flow.Of<BreachCardAttached>(log).Count);
            Flow.Only(s, ChoiceKind.MapAction);
            // 셋째 장은 보관할 자리를 넘는다 → 「보관」 없이 붙인다 · 보관한 카드는 다시 묻지 않는다
            Deal();
            NextTurn();
            var attach = Battles.Only(s, ChoiceKind.Simultaneous, ChoiceSub.BreachAttach);
            Assert.IsFalse(Scene.Legal(s, ctx, attach).Exists(a => a.Kind == AnswerKind.Decline), "보관할 자리가 없으면 붙여야 한다");
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(attach.Id), log));
            Assert.AreEqual(1, Flow.Of<BreachCardAttached>(log).Count);
            Assert.AreEqual(2, BreachRules.StoredCount(s, 0));
            Flow.Only(s, ChoiceKind.MapAction);
            // 빈 칸에 포켓몬이 들어오면 보관할 자리가 없어져 보관한 카드가 손으로 돌아와 붙여야 한다
            Scene.Give(s, ctx, 0, Bidoof);
            NextTurn();
            Assert.AreEqual(0, BreachRules.StoredCount(s, 0));
            var forced = Battles.Only(s, ChoiceKind.Simultaneous, ChoiceSub.BreachAttach);
            Assert.IsFalse(Scene.Legal(s, ctx, forced).Exists(a => a.Kind == AnswerKind.Decline), "돌아온 카드는 붙여야 한다");
            // 카드가 없는 좌석(신오의 심장 · 다른 카드)은 보관하지 않는다
            Card(s, 0, "hiker", TrainerRules.Gold);
            Assert.AreEqual(0, TrainerRules.BreachStorage(s, ctx, 0));
            Assert.AreEqual(0, TrainerRules.BreachStorage(Battles.Game(ctx, 2), ctx, 0));
        }
    }
}
