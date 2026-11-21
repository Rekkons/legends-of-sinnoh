using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「숨겨진 장소·유물·트레이너전 보상」: 숨겨진 장소 결과 5종(rules-summary §14 · §5.4, 룰북 p.16, rulings A17·A23) · 유물 11종(p.17, RelicRules) ·
    /// 트레이너전 승리 보상(p.9, rules-summary §7.9). 장면은 실제 판(Flow.Started)의 뒷면 숨겨진 장소 하나를 원하는 정체로 바꾸고 이웃 칸에서 걸어 들어가 공개한다.
    /// </summary>
    public class HiddenSpotTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static ChoiceAnswer A(AnswerKind k, int target = -1) => ChoiceAnswer.Of(k, target);

        /// <summary>뒷면 숨겨진 장소 하나(이웃 칸에서 들어갈 수 있는 것)를 key로 바꾸고 차례 좌석을 이웃 칸에 세운다(이동력 3). 돌려주는 값 = 토큰 색인.</summary>
        int Place(GameState s, string key)
        {
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if (t.Kind != TokenKind.HiddenSpot || t.Cell < 0 || t.FaceUp) continue;
                int n = Flow.StepInto(s, ctx, t.Cell);
                if (n < 0) continue;
                t.Def = Scene.Item(ctx, key);
                var st = s.Seats[s.ActiveSeat];
                st.Cell = n;
                st.MovesLeft = 3;
                return i;
            }
            Assert.Fail("들어갈 수 있는 숨겨진 장소가 없다");
            return -1;
        }

        /// <summary>그 토큰 칸으로 걸어 들어가 공개한다(공개 답의 이벤트를 log에).</summary>
        void Reveal(GameState s, int token, EventLog log)
        {
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, s.Tokens[token].Cell));
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.HiddenSpot, A(AnswerKind.Accept), log);
        }

        // ── 결과 5종 ──

        [Test]
        public void S14_1_규토리더미는빨강파랑노랑3_분홍초록하양2_검정1_토큰은버리고턴은이어진다()
        {
            var want = new[] { 3, 3, 3, 2, 2, 2, 1 };
            for (int color = 0; color < SeatState.ApricornColors; color++)
            {
                var log = new EventLog();
                var s = Flow.Started(ctx, 2, 12);
                string key = "hidden_spot." + ((ApricornColor)color).ToString().ToLowerInvariant() + "_apricorn_stash";
                int token = Place(s, key);
                int cell = s.Tokens[token].Cell;
                var st = s.Seats[s.ActiveSeat];
                int before = st.Apricorns[color];
                Reveal(s, token, log);
                Assert.AreEqual(before + want[color], st.Apricorns[color], key);
                var gained = Flow.Of<ApricornsGained>(log)[0];
                Assert.AreEqual(new SourceRef(SourceKind.HiddenSpot, Scene.Item(ctx, key)), gained.Source);
                Assert.AreEqual(color, gained.Changes[0].Key);
                Assert.AreEqual(-1, s.Tokens[token].Cell, "「discard this token」 — 맵에서 빠진다");
                Assert.AreEqual(cell, Flow.Of<TokenRemoved>(log)[0].Cell);
                Assert.AreEqual(s.ActiveSeat, Flow.Only(s, ChoiceKind.MapAction).Seat, "턴은 이어진다(p.27)");
            }
        }

        [Test]
        public void S14_2_야영지는앞면으로남고회복과은행상회_공중날기는안된다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 12);
            int token = Place(s, HiddenSpotRules.Campsite);
            int cell = s.Tokens[token].Cell;
            s.Seats[s.ActiveSeat].Apricorns[(int)ApricornColor.Red] = 3;
            Reveal(s, token, log);
            Assert.AreEqual(cell, s.Tokens[token].Cell, "「Token remains where it is found」");
            Assert.IsTrue(s.Tokens[token].FaceUp);
            Assert.AreEqual(0, Flow.Of<TokenRemoved>(log).Count);
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsTrue(Flow.Has(legal, AnswerKind.VillageHeal), "파티 회복");
            Assert.IsFalse(Flow.Has(legal, AnswerKind.Fly), "「you cannot fly to or from them」");
            Assert.IsTrue(MapActions.VillagePlace(s, ctx, cell, out var source, out int village) && source == RuleSource.CampHeal && village < 0, "은행상회도 이 자리(BankRules — 마을·야영지)");
        }

        [Test]
        public void S14_3_떨어진별은운명2장을받고모인별이하나는다_토큰은별자리로()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 12);
            s.FallenStars = 3;
            int token = Place(s, HiddenSpotRules.FallenStar);
            var hand = s.Seats[s.ActiveSeat].FateHand;
            hand.Clear();
            Reveal(s, token, log);
            Assert.AreEqual(2, hand.Count, "「Draw 2 Change of Fate cards when revealed」");
            var dealt = Flow.Of<FateDealt>(log)[0];
            Assert.AreEqual(new SourceRef(SourceKind.HiddenSpot, Scene.Item(ctx, HiddenSpotRules.FallenStar)), dealt.Source);
            Assert.AreEqual(4, s.FallenStars, "빈 떨어진 별 자리에 놓는다");
            Assert.AreEqual(-1, s.Tokens[token].Cell);

            // 4개 모였으니 다음 좌석의 턴 시작에 1장(FateRules.DealStars)
            int next = (s.ActiveSeat + 1) % s.SeatCount;
            int had = s.Seats[next].FateHand.Count;
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn), log);
            Assert.AreEqual(next, s.ActiveSeat);
            Assert.AreEqual(had + 1, s.Seats[next].FateHand.Count, "「For every 4 Fallen Stars collected: All trainers draw 1 … at the start of their turns」");
        }

        [TestCase("item.seafarer_egg")]
        [TestCase("item.odd_keystone")]
        [TestCase("item.magma_stone")]
        [TestCase("item.secret_key")]
        [TestCase("item.gracidea_flower")]
        [TestCase("item.rockice_core")]
        [TestCase("item.icesteel_core")]
        [TestCase("item.steelrock_core")]
        [TestCase("item.adamant_artifact")]
        [TestCase("item.lustrous_artifact")]
        [TestCase("item.griseous_artifact")]
        public void S14_5_유물은가방으로_토큰은맵에서빠진다(string key)
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 12);
            int token = Place(s, key);
            var st = s.Seats[s.ActiveSeat];
            Reveal(s, token, log);
            int item = Scene.Item(ctx, key);
            CollectionAssert.Contains(st.Bag, item);
            CollectionAssert.AreEqual(new[] { item }, RelicRules.Owned(s, ctx, s.ActiveSeat));
            Assert.AreEqual(-1, s.Tokens[token].Cell);
            Assert.AreEqual(s.ActiveSeat, Flow.Only(s, ChoiceKind.MapAction).Seat);
        }

        [Test]
        public void A17_우두머리는공개하면강제배틀_강한카드에체력4_우선권토큰은우두머리_토큰은맵에서빠진다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 12);
            int token = Place(s, "item.alpha_pokemon");
            int cell = s.Tokens[token].Cell;
            int seat = s.ActiveSeat;
            Reveal(s, token, log);
            // 배틀은 우두머리가 이기며 끝날 수 있다(시작 파티 1마리) — 이벤트로 본다
            var started = Flow.Of<BattleStarted>(log);
            Assert.AreEqual(1, started.Count, "「When revealed, you are forced to battle an Alpha Pokémon」");
            Assert.AreEqual(BattleKind.Alpha, started[0].Kind);
            Assert.AreEqual(seat, started[0].Sides[0].Owner);
            var foe = Flow.Of<SentIn>(log).Find(e => e.Side == 1);
            var card = ctx.Cards.Pokemon[foe.Species];
            Assert.AreEqual(EncounterTier.Strong, card.Tier, "강한 덱에서 찾아보기");
            Assert.AreEqual(card.Health + 4, foe.MaxHp, "「increases the health of the user by 4」");
            Assert.AreEqual(1, Flow.Of<InitiativeTokenAt>(log)[0].Side, "우두머리가 건 배틀 — 우선권 토큰은 우두머리(동률에서만)");
            Assert.AreEqual(-1, s.Tokens[token].Cell, "토큰은 그 포켓몬에 붙는다");
            Assert.AreEqual(cell, Flow.Of<TokenRemoved>(log)[0].Cell);
            Assert.AreEqual(0, Flow.Of<HiddenSpotRevealed>(log).FindIndex(e => e.SpotKey == Scene.Item(ctx, "item.alpha_pokemon")), "공개 → 배틀");
            if (s.Battle != null)
            {
                var wild = s.Pokemon[s.Battle.Sides[1].Roster[0]];
                Assert.IsTrue(wild.Alpha);
                Assert.IsTrue(wild.HasLegendaryMove, "전설로 다시 분류된다(알파 스트라이크·비스트 부스트 — 효과는 「전설·알파·폼」)");
                Assert.AreEqual(-1, s.Battle.Token, "나무·산호가 아니라 지키는 규토리가 없다(A17)");
            }
            else
            {
                var end = Flow.Of<BattleEnded>(log)[0];
                Assert.AreNotEqual(BattleResult.Captured, end.Result);
                Assert.AreEqual(0, System.Array.FindAll(end.Changes, c => c.Res == ResKind.Apricorn).Length, "규토리는 없다(A17)");
                if (end.Winner != 0) CollectionAssert.Contains(s.Decks[(int)DeckId.Strong], foe.Species, "지면 카드는 강한 덱으로 — 토큰은 버려져 맵에 남지 않는다");
            }
        }

        [Test]
        public void A17_자극적인스튜를먹은턴은배틀없이앞면으로남고_그칸에서찾아보기로싸운다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 12);
            int token = Place(s, "item.alpha_pokemon");
            int cell = s.Tokens[token].Cell;
            int seat = s.ActiveSeat;
            s.Used.Add(new UsageMark { Seat = seat, Kind = UsageKind.Stew, Key = Scene.Item(ctx, CraftRules.ZestyStew), Scope = s.TurnId });
            Assert.IsTrue(CraftRules.AlphaWarded(s, ctx, seat));
            Reveal(s, token, log);
            Assert.IsNull(s.Battle, "「preventing Alpha Pokémon from battling you」");
            Assert.AreEqual(cell, s.Tokens[token].Cell, "원작 커뮤니티 확인");
            Assert.IsTrue(s.Tokens[token].FaceUp);
            var legal = Flow.MapLegal(s, ctx);
            var fight = legal.Find(a => a.Kind == AnswerKind.Search && a.Target == token);
            Assert.IsNotNull(fight, "앞면 우두머리는 강제가 아니고 찾아보기로 싸울 수 있다 [가정 plan §5 #85 ③]");
            Assert.AreEqual(token, HiddenSpotRules.FaceUpAlphaAt(s, ctx, cell));
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, fight, log);
            Assert.AreEqual(BattleKind.Alpha, Flow.Of<BattleStarted>(log)[0].Kind);
            Assert.AreEqual(-1, s.Tokens[token].Cell);
        }

        [Test]
        public void A17_우두머리는지닌물건을들지못한다_유물은지닌다()
        {
            var s = Flow.Started(ctx, 2, 12);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int mon = st.Party[0];
            int relic = Scene.Item(ctx, "item.magma_stone");
            st.Bag.Add(relic);
            var hold = new Maintenance { Kind = MaintKind.HoldItem, Pokemon = mon, Value = relic };
            Assert.AreEqual(RejectReason.None, MaintenanceRules.Check(s, ctx, seat, hold), "유물은 지닌물건처럼 지닌다(원작 커뮤니티 확인)");
            s.Pokemon[mon].Alpha = true;
            Assert.AreEqual(RejectReason.Illegal, MaintenanceRules.Check(s, ctx, seat, hold), "원작 커뮤니티 확인");
        }

        // ── 유물 11종(p.17) ──

        int MoveOf(PokemonType type)
        {
            for (int m = 0; m < ctx.Cards.Moves.Count; m++)
                if (ctx.Cards.Moves[m].Type == type && ctx.Cards.Moves[m].AttackStrength != null && ctx.Effects.OfMove(m).Count == 0) return m;
            for (int m = 0; m < ctx.Cards.Moves.Count; m++)
                if (ctx.Cards.Moves[m].Type == type && ctx.Cards.Moves[m].AttackStrength != null) return m;
            Assert.Fail($"{type} 기술이 없다");
            return -1;
        }

        int Power(GameState s, int move, int attacker) => BattleRules.RollPower(s, ctx, move, attacker, -1, new List<Mod>());

        [TestCase("item.seafarer_egg", PokemonType.Psychic, PokemonType.Water)]
        [TestCase("item.odd_keystone", PokemonType.Dark, PokemonType.Ghost)]
        [TestCase("item.magma_stone", PokemonType.Fire, PokemonType.Ground)]
        [TestCase("item.secret_key", PokemonType.Ghost, PokemonType.Electric)]
        [TestCase("item.gracidea_flower", PokemonType.Grass, PokemonType.Flying)]
        [TestCase("item.rockice_core", PokemonType.Rock, PokemonType.Ice)]
        [TestCase("item.icesteel_core", PokemonType.Ice, PokemonType.Steel)]
        [TestCase("item.steelrock_core", PokemonType.Steel, PokemonType.Rock)]
        public void S14_5_유물은지닌포켓몬의그두타입기술공격력1_가방에만있으면없다(string key, PokemonType a, PokemonType b)
        {
            var s = Scene.Game(ctx, 2);
            int mon = Scene.Give(s, ctx, 0, "387:0");
            int item = Scene.Item(ctx, key);
            CollectionAssert.AreEquivalent(new[] { a, b }, RelicRules.TypesOf(ctx, item));
            int other = MoveOf(PokemonType.Normal);
            foreach (var type in new[] { a, b })
            {
                int move = MoveOf(type);
                int plain = Power(s, move, mon);
                s.Seats[0].Bag.Add(item);
                Assert.AreEqual(plain, Power(s, move, mon), "가방에만 있으면 없다(원작 커뮤니티 확인)");
                s.Seats[0].Bag.Remove(item);
                s.Pokemon[mon].HeldItem = item;
                var mods = new List<Mod>();
                int held = BattleRules.RollPower(s, ctx, move, mon, -1, mods);
                Assert.AreEqual(System.Math.Min(9, plain + 1), held, $"{key}: {type} +1");
                Assert.IsTrue(mods.Exists(m => m.Source.Kind == SourceKind.Relic && m.Source.Id == item && m.Delta == RelicRules.Power), "출처 = 유물");
                int otherPlain = Power(s, other, mon);
                s.Pokemon[mon].HeldItem = -1;
                Assert.AreEqual(Power(s, other, mon), otherPlain, "다른 타입은 그대로");
            }
            Assert.AreEqual(SinnohGod.None, RelicRules.GodOf(ctx, item));
        }

        [TestCase("item.adamant_artifact", SinnohGod.Dialga)]
        [TestCase("item.lustrous_artifact", SinnohGod.Palkia)]
        [TestCase("item.griseous_artifact", SinnohGod.Giratina)]
        public void S14_5_천상의유물은가지기만하면그신의천상의오라를막는다_공격력은없다(string key, SinnohGod god)
        {
            var s = Scene.Game(ctx, 2);
            int mon = Scene.Give(s, ctx, 0, "387:0");
            int item = Scene.Item(ctx, key);
            Assert.AreEqual(0, RelicRules.TypesOf(ctx, item).Count);
            foreach (var g in new[] { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina }) Assert.IsFalse(RelicRules.WardsAura(s, ctx, 0, g));
            s.Seats[0].Bag.Add(item);
            foreach (var g in new[] { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina })
                Assert.AreEqual(g == god, RelicRules.WardsAura(s, ctx, 0, g), $"가방에 든 {key} → {g}(원작 커뮤니티 확인)");
            s.Seats[0].Bag.Remove(item);
            s.Pokemon[mon].HeldItem = item;
            Assert.IsTrue(RelicRules.WardsAura(s, ctx, 0, god), "지녀도 가진 것이다");
            int move = MoveOf(PokemonType.Grass);
            int with = Power(s, move, mon);
            s.Pokemon[mon].HeldItem = -1;
            Assert.AreEqual(Power(s, move, mon), with);
        }

        [Test]
        public void S14_5_전설장소는그유물을가지면열린다_가방이든지닌것이든()
        {
            var s = Scene.Game(ctx, 2);
            int mon = Scene.Give(s, ctx, 0, "387:0");
            int sites = 0;
            for (int f = 0; f < ctx.Fixtures.Fixtures.Count; f++)
            {
                var fx = ctx.Fixtures.Fixtures[f];
                if (fx.Kind != FixtureKind.LegendarySite || fx.Artifact.Count == 0) continue;
                sites++;
                Assert.IsFalse(RelicRules.OpensSite(s, ctx, 0, f), fx.Key);
                foreach (int item in fx.Artifact)
                {
                    Assert.IsTrue(RelicRules.IsRelic(ctx, item), fx.Key);
                    s.Seats[0].Bag.Add(item);
                    Assert.IsTrue(RelicRules.OpensSite(s, ctx, 0, f), $"{fx.Key}: 가방의 {ctx.Items.Items[item].Key}");
                    s.Seats[0].Bag.Remove(item);
                    s.Pokemon[mon].HeldItem = item;
                    Assert.IsTrue(RelicRules.OpensSite(s, ctx, 0, f), $"{fx.Key}: 지닌 {ctx.Items.Items[item].Key}");
                    s.Pokemon[mon].HeldItem = -1;
                }
            }
            Assert.Greater(sites, 0, "유물 조건이 있는 전설 장소");
        }

        [Test]
        public void S14_5_결정123_전설장소피규어는누가조건유물을가졌을때만서고_잡히면사라진다_조건없는장소는늘()
        {
            // 사용자 결정 123(T25 「전설의 포켓몬을 잡았는데도 피규어가 남아있네」): 유물 조건이 있는 전설 장소의 피규어 = 어느 좌석이든 그 유물을 가졌고 그 전설이 아직 전설 덱에 있을 때만
            var s = Scene.Game(ctx, 2);
            int mon = Scene.Give(s, ctx, 1, "387:0");
            int withRelic = 0, without = 0;
            for (int f = 0; f < ctx.Fixtures.Fixtures.Count; f++)
            {
                var fx = ctx.Fixtures.Fixtures[f];
                if (fx.Kind != FixtureKind.LegendarySite) continue;
                if (fx.Artifact.Count == 0)
                {
                    without++;
                    Assert.IsFalse(LegendRules.SiteFigureShown(s, ctx, f), $"{fx.Key}: 조건 유물이 없는 장소는 신오의 심장 판에서 서지 않는다(결정 128 — 울트라 버스트 판 쪽은 UbTrialTests)");
                    continue;
                }
                withRelic++;
                Assert.IsFalse(LegendRules.SiteFigureShown(s, ctx, f), $"{fx.Key}: 아무도 유물이 없다");
                s.Pokemon[mon].HeldItem = fx.Artifact[0];
                Assert.IsTrue(LegendRules.SiteFigureShown(s, ctx, f), $"{fx.Key}: 좌석 2의 포켓몬이 유물을 지녔다(어느 좌석이든)");
                int card = LegendRules.SiteCard(s, ctx, f);
                s.Decks[(int)DeckId.Legendary].Remove(card);
                Assert.IsFalse(LegendRules.SiteFigureShown(s, ctx, f), $"{fx.Key}: 잡히거나 쓰러져 전설 덱에서 빠지면 사라진다");
                s.Decks[(int)DeckId.Legendary].Add(card);
                s.Seats[1].Eliminated = true;
                Assert.IsFalse(LegendRules.SiteFigureShown(s, ctx, f), $"{fx.Key}: 탈락한 좌석의 유물은 세지 않는다");
                s.Seats[1].Eliminated = false;
                s.Pokemon[mon].HeldItem = -1;
            }
            Assert.AreEqual(8, withRelic, "유물 조건 장소 8(쉐이미 · 히드런 · 화강돌 · 마나피 · 로토무 · 레지 셋)");
            Assert.AreEqual(4, without, "조건 없는 장소 4(유크시 · 엠라이트 · 아그놈 · 실버디)");
        }

        // ── 트레이너전 승리 보상(p.9) ──

        sealed class Won
        {
            public GameState S;
            public EventLog Log;
            public int HeldRelic, BagRelic, Holder;
        }

        /// <summary>좌석 0이 좌석 1을 트레이너전으로 이긴 판(좌석 1: 가방에 유물 하나 · 포켓몬이 지닌 유물 하나 · 운명 3장). 보상 선택이 열려 있다.</summary>
        Won Win(ulong seed)
        {
            var w = new Won { S = Battles.Game(ctx, 2, seed), Log = new EventLog() };
            var s = w.S;
            s.Phase = Phase.Turn;
            s.ActiveSeat = 0;
            Scene.Give(s, ctx, 0, "242:0");
            w.Holder = Scene.Give(s, ctx, 1, "387:0");
            Battles.SetHp(s, ctx, w.Holder, 1);
            w.BagRelic = Scene.Item(ctx, "item.secret_key");
            w.HeldRelic = Scene.Item(ctx, "item.magma_stone");
            s.Seats[1].Bag.Add(w.BagRelic);
            s.Pokemon[w.Holder].HeldItem = w.HeldRelic;
            s.Seats[1].FateHand.AddRange(Scene.Top(s, DeckId.ChangeOfFate, 3));
            foreach (int c in s.Seats[1].FateHand) s.Decks[(int)DeckId.ChangeOfFate].Remove(c);
            Battles.Start(s, ctx, w.Log, BattleKind.Trainer, 0, 1, -1);
            for (int i = 0; i < 300 && s.Battle != null; i++)
            {
                var c = s.Pending.Find(x => !x.Answered);
                Battles.Answer(s, ctx, c, c.Default.Clone(), w.Log);
            }
            return w;
        }

        [Test]
        public void S7_9_트레이너전에이기면상대의유물하나_가방이든지닌것이든()
        {
            ulong seed = Battles.Seed(x => { var t = Win(x); return t.S.Battle == null && t.Log.OfType<BattleEnded>()[0].Winner == 0; });
            foreach (bool held in new[] { true, false })
            {
                var w = Win(seed);
                var s = w.S;
                var ask = s.Pending.Find(x => x.Kind == ChoiceKind.Reward && x.Sub == ChoiceSub.TrainerReward);
                Assert.IsNotNull(ask, "「After winning a battle against another trainer」 — 이긴 좌석에게 묻는다");
                Assert.AreEqual(0, ask.Seat);
                Assert.AreEqual(1, ask.Subject, "진 좌석");
                var legal = Scene.Legal(s, ctx, ask);
                var relics = new List<int> { w.BagRelic, w.HeldRelic };
                relics.Sort();
                Assert.AreEqual(3, legal.Count, "유물 둘 + 운명 2장: " + string.Join(" ", legal.ConvertAll(x => x.ToString())));
                Assert.AreEqual(relics[0], legal[0].Target);
                Assert.AreEqual(relics[1], legal[1].Target);
                Assert.AreEqual(AnswerKind.Accept, legal[2].Kind);
                Assert.AreEqual(2, legal[2].Target);
                int take = held ? w.HeldRelic : w.BagRelic;
                w.Log.Clear();
                Battles.Answer(s, ctx, ask, ChoiceAnswer.Pick(take), w.Log);
                CollectionAssert.Contains(s.Seats[0].Bag, take);
                CollectionAssert.DoesNotContain(RelicRules.Owned(s, ctx, 1), take);
                if (held) Assert.AreEqual(-1, s.Pokemon[w.Holder].HeldItem, "지닌 유물도 가져간다");
                var spoils = w.Log.OfType<SpoilsTaken>()[0];
                Assert.AreEqual(0, spoils.Seat);
                Assert.AreEqual(1, spoils.From);
                Assert.IsTrue(System.Array.Exists(spoils.Changes, c => c.Seat == 0 && c.Res == ResKind.Relic && c.Key == take && c.After == 1));
                Assert.IsFalse(System.Array.Exists(spoils.Changes, c => c.Res == ResKind.Fate), "유물 또는 운명 — 둘 중 하나");
                Assert.AreEqual(1, s.ActiveSeat, "배틀 뒤 턴이 끝난다");
            }
        }

        [Test]
        public void S7_9_트레이너전에이기면상대손패에서무작위로운명2장_결정적()
        {
            ulong seed = Battles.Seed(x => { var t = Win(x); return t.S.Battle == null && t.Log.OfType<BattleEnded>()[0].Winner == 0; });
            List<int> got = null;
            for (int run = 0; run < 2; run++)
            {
                var w = Win(seed);
                var s = w.S;
                var loserHand = new List<int>(s.Seats[1].FateHand);
                int mine = s.Seats[0].FateHand.Count;
                var ask = s.Pending.Find(x => x.Kind == ChoiceKind.Reward && x.Sub == ChoiceSub.TrainerReward);
                w.Log.Clear();
                Battles.Answer(s, ctx, ask, ChoiceAnswer.Of(AnswerKind.Accept, 2), w.Log);
                Assert.AreEqual(mine + 2, s.Seats[0].FateHand.Count);
                var moved = s.Seats[0].FateHand.GetRange(mine, 2);
                foreach (int c in moved) CollectionAssert.Contains(loserHand, c);
                Assert.AreEqual(2, RelicRules.Owned(s, ctx, 1).Count, "유물은 그대로");
                var spoils = w.Log.OfType<SpoilsTaken>()[0];
                Assert.IsTrue(System.Array.Exists(spoils.Changes, c => c.Seat == 1 && c.Res == ResKind.Fate && c.Before == 3 && c.After == 1), "「take up to 2 of their Change of Fate cards at random」(진 좌석은 귀환 턴 시작에 1장을 더 받는다)");
                foreach (int c in moved) CollectionAssert.DoesNotContain(s.Seats[1].FateHand.GetRange(0, 1), c);
                if (got == null) got = moved;
                else CollectionAssert.AreEqual(got, moved, "같은 시드면 같은 카드(게임 난수)");
            }
        }

        [Test]
        public void S7_9_진상대에게유물도운명도없으면묻지않는다()
        {
            var s = Scene.Game(ctx, 2);
            var c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.TrainerReward, Seat = 0, Subject = 1, Window = Window.BattleEnd };
            Assert.AreEqual(0, Scene.Legal(s, ctx, c).Count, "BattleFlow.Conclude는 합법 답이 없으면 열지 않는다");
        }
    }
}
