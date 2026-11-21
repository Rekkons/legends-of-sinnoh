using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6 「배틀 피해 뒤·기절·교체·상태」(rulings A35 — 능력 11): aroma_lady R · undivided_acolyte R · scuba_diver R · worker R · team_rocket B·R · expert R · lady R · kindler R ·
    /// black_belt R · fairy_tale_girl R.
    /// </summary>
    public class TrainerAfterTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const AttackDieFace S = AttackDieFace.StatusSymbol;
        const AttackDieFace R = AttackDieFace.RegularHit;
        const AttackDieFace C = AttackDieFace.CriticalHit;
        const string Garchomp = "445:0", Bidoof = "399:0", Turtwig = "387:0", Torterra = "389:1", Piplup = "393:0", Shieldon = "410:0", Bastiodon = "411:0",
            Monferno = "391:0", Lucario = "448:0", Riolu = "447:0", Togepi = "175:0", Empoleon = "395:2";

        sealed class Arena
        {
            public GameState S;
            public EventLog Log = new EventLog();
            public List<int> Mine = new List<int>(), Theirs = new List<int>();
            public SourceRef Card0 = SourceRef.None;
            public int From;
            public int A => Mine[0];
            public int D => Theirs[0];
        }

        int Mv(string key)
        {
            int m = Scene.Move(ctx, key);
            Assert.GreaterOrEqual(m, 0, key);
            return m;
        }

        SourceRef Card(GameState s, int seat, string key, int tier)
        {
            int card = ctx.Cards.TrainerCardIds.IndexOf(key);
            s.Seats[seat].TrainerCard = card;
            s.Seats[seat].TrainerCardTier = tier;
            TrainerRules.Refresh(s, ctx);
            return new SourceRef(SourceKind.TrainerCard, card);
        }

        /// <summary>트레이너전: 좌석 0(카드)의 mine 대 좌석 1의 theirs — 각자 첫 포켓몬이 나오고 좌석 0의 기술 고르기가 열려 있다. prep = 배틀 전에 손볼 것.</summary>
        Arena Field(string card, string[] mine, string[] theirs, int tier = TrainerRules.Gold, System.Action<Arena> prep = null)
        {
            var t = new Arena { S = Battles.Game(ctx, 2) };
            foreach (var k in mine) t.Mine.Add(Scene.Give(t.S, ctx, 0, k));
            foreach (var k in theirs) t.Theirs.Add(Scene.Give(t.S, ctx, 1, k));
            if (card != null) t.Card0 = Card(t.S, 0, card, tier);
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

        static ulong SeedFor(AttackDieFace[] faces)
        {
            for (ulong seed = 0; seed < 4000000; seed++)
            {
                var roll = AttackDice.Roll(new Pcg32(seed), faces.Length);
                bool same = true;
                for (int i = 0; i < roll.Length && same; i++) same = roll[i] == faces[i];
                if (same) return seed;
            }
            Assert.Fail("그 굴림이 나오는 시드가 없다");
            return 0;
        }

        void Next(Arena t, params AttackDieFace[] faces) => t.S.Rng = new Pcg32(SeedFor(faces));

        /// <summary>차례인 좌석이 그 기술(배운 기술 칸 0에 넣는다)을 쓴다.</summary>
        void Use(Arena t, string move, params AttackDieFace[] faces)
        {
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            int active = t.S.Battle.Sides[ch.Seat].Active;
            int m = Mv(move);
            if (ctx.Cards.Pokemon[t.S.Pokemon[active].Def].Signature != m) t.S.Pokemon[active].Moves[0] = m;
            if (faces.Length > 0) Next(t, faces);
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

        static T One<T>(List<GameEvent> events) where T : GameEvent
        {
            var l = All<T>(events);
            Assert.AreEqual(1, l.Count, typeof(T).Name);
            return l[0];
        }

        int Hp(Arena t, int p) => Battles.Hp(t.S, ctx, p);

        int Max(Arena t, int p) => PokemonRules.MaxHp(ctx, t.S.Pokemon[p]);

        // ── 피해 뒤 ──

        [Test]
        public void A20_6_aroma_lady_빨강_풀페어리기술로준피해의절반올림을회복_흡수기술과따로()
        {
            // 「When you attack with a Grass-type or Fairy-type move, the user heals for half the damage dealt, rounded up.」
            var t = Field("aroma_lady", new[] { Torterra }, new[] { Bastiodon });
            Battles.SetHp(t.S, ctx, t.A, 1);
            Use(t, "razor_leaf", R, R, R);
            int dealt = One<DamageBreakdown>(Events(t)).Final;
            Assert.Greater(dealt, 0);
            Assert.AreEqual(1 + (dealt + 1) / 2, Hp(t, t.A));
            Assert.AreEqual(1, All<AbilityUsed>(Events(t)).Count);
            // giga_drain(내림)과 겹친다(원작 커뮤니티 확인) — 합이 준 피해와 같다
            t = Field("aroma_lady", new[] { Torterra }, new[] { Bastiodon });
            Battles.SetHp(t.S, ctx, t.A, 1);
            Use(t, "giga_drain", R, R, R, R);
            dealt = One<DamageBreakdown>(Events(t)).Final;
            Assert.AreEqual(System.Math.Min(Max(t, t.A), 1 + dealt), Hp(t, t.A));
            // 풀·페어리 기술이 아니면 없다 · 실버에서는 없다
            t = Field("aroma_lady", new[] { Torterra }, new[] { Bastiodon });
            Battles.SetHp(t.S, ctx, t.A, 1);
            Use(t, "tackle", R, R);
            Assert.AreEqual(1, Hp(t, t.A));
            t = Field("aroma_lady", new[] { Torterra }, new[] { Bastiodon }, TrainerRules.Silver);
            Battles.SetHp(t.S, ctx, t.A, 1);
            Use(t, "razor_leaf", R, R, R);
            Assert.AreEqual(1, Hp(t, t.A));
        }

        [TestCase(2, true)]
        [TestCase(1, true)]
        [TestCase(3, false)]
        public void A20_6_undivided_acolyte_빨강_공격뒤상대체력이2이하면기절(int hp, bool faints)
        {
            // 「After attacking, the opposing Pokémon faints if they have 2 or less health remaining.」 — 피해 0인 공격 뒤에도 본다
            var t = Field("undivided_acolyte", new[] { Garchomp }, new[] { Bidoof, Turtwig }, TrainerRules.Gold, a => Battles.SetHp(a.S, ctx, a.D, hp));
            Use(t, "tackle", S, S);
            var ev = Events(t);
            Assert.AreEqual(0, One<DamageBreakdown>(ev).Final);
            Assert.AreEqual(faints ? 1 : 0, All<Fainted>(ev).Count);
            Assert.AreEqual(faints ? 1 : 0, All<AbilityUsed>(ev).Count);
            if (faints)
            {
                var hit = All<HpChanged>(ev).Find(x => x.Source.Equals(t.Card0));
                Assert.IsNotNull(hit, "기절의 출처 = 그 카드");
                Assert.AreEqual(0, hit.After);
            }
            else Assert.AreEqual(hp, Hp(t, t.D));
        }

        [Test]
        public void A20_6_scuba_diver_빨강_물기술뒤교체하면상태이상이풀리고_순서는그대로()
        {
            // 「After you attack with a Water-type move, the user may switch out and then heal all status effects.」
            var t = Field("scuba_diver", new[] { Piplup, Turtwig }, new[] { Bastiodon }, TrainerRules.Gold, a => a.S.Pokemon[a.Mine[0]].Status = StatusEffect.Poisoned);
            Use(t, "water_gun", R, R);
            var ask = Battles.Only(t.S, ChoiceKind.SwitchWindow);
            Assert.AreEqual(0, ask.Seat);
            Assert.AreEqual(t.Card0, ask.Source);
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind, "「may」 — 기본 답은 안 바꿈");
            Battles.Answer(t.S, ctx, ask, ChoiceAnswer.Pick(t.Mine[1]), t.Log);
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.A].Status, "나간 포켓몬의 상태이상이 풀린다");
            Assert.AreEqual(t.Mine[1], t.S.Battle.Sides[0].Active);
            Assert.AreEqual(1, All<AbilityUsed>(Events(t)).Count);
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "순서는 그대로 — 상대 차례");
            // 안 바꾸면 상태이상은 그대로
            t = Field("scuba_diver", new[] { Piplup, Turtwig }, new[] { Bastiodon }, TrainerRules.Gold, a => a.S.Pokemon[a.Mine[0]].Status = StatusEffect.Poisoned);
            Use(t, "water_gun", R, R);
            ask = Battles.Only(t.S, ChoiceKind.SwitchWindow);
            Battles.Answer(t.S, ctx, ask, ask.Default.Clone(), t.Log);
            Assert.AreEqual(StatusEffect.Poisoned, t.S.Pokemon[t.A].Status);
            // 물 기술이 아니면 교체 창이 없다
            t = Field("scuba_diver", new[] { Piplup, Turtwig }, new[] { Bastiodon });
            Use(t, "tackle", R, R);
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
        }

        [Test]
        public void A20_6_worker_빨강_강철이교체로나오면상대피로3과교체도망금지_처음출전은아니다()
        {
            // 「When your Steel-type Pokémon are switched in, increase the battle fatigue of the opposing Pokémon by 3 and prevent them from switching out or fleeing the battle.」
            var t = Field("worker", new[] { Piplup, Bastiodon }, new[] { Shieldon });
            Use(t, "flip_turn", R, R, R);
            var ask = Battles.Only(t.S, ChoiceKind.SwitchWindow);
            int before = t.S.Pokemon[t.D].Fatigue;
            Battles.Answer(t.S, ctx, ask, ChoiceAnswer.Pick(t.Mine[1]), t.Log);
            Assert.AreEqual(System.Math.Min(FatigueRules.Max, before + 3), t.S.Pokemon[t.D].Fatigue);
            Assert.IsTrue(ModRules.NoEscape(t.S.Pokemon[t.D]));
            var used = One<AbilityUsed>(Events(t));
            Assert.AreEqual(AbilityColor.Red, used.Ability);
            // 강철이 아닌 포켓몬이 나오면 없다
            t = Field("worker", new[] { Piplup, Turtwig }, new[] { Shieldon });
            Use(t, "flip_turn", R, R, R);
            Battles.Answer(t.S, ctx, Battles.Only(t.S, ChoiceKind.SwitchWindow), ChoiceAnswer.Pick(t.Mine[1]), t.Log);
            Assert.IsFalse(ModRules.NoEscape(t.S.Pokemon[t.D]));
            // 처음 내보낸 강철(「sends out」)에는 없다
            t = Field("worker", new[] { Bastiodon }, new[] { Shieldon });
            Assert.AreEqual(0, t.S.Pokemon[t.D].Fatigue);
            Assert.IsFalse(ModRules.NoEscape(t.S.Pokemon[t.D]));
        }

        // ── 기절 · 바닥남 ──

        /// <summary>team_rocket 장면: 좌석 0의 터트윅(체력 1) 하나 대 배스티돈. 좌석 0이 피해 없는 공격을 한 뒤 좌석 1이 쓰러뜨린다 — 그 답까지 간다.</summary>
        Arena Blast(int tier = TrainerRules.Gold)
        {
            var t = Field("team_rocket", new[] { Turtwig }, new[] { Bastiodon }, tier, a => Battles.SetHp(a.S, ctx, a.Mine[0], 1));
            Use(t, "tackle", S, S);
            Use(t, "tackle", R, R);
            return t;
        }

        [Test]
        public void A20_6_team_rocket_빨강_내포켓몬이기절하면상대에게2_바닥나면아무칸으로()
        {
            // 「When one of your Pokémon faints, deal 2 damage to the opposing Pokémon. If you are out of usable Pokémon, you may travel to any tile on the map and are automatically fully restored at the start of your next turn.」
            var t = Blast();
            var ev = Events(t);
            Assert.AreEqual(1, All<Fainted>(ev).Count);
            var hit = All<HpChanged>(ev).Find(x => x.Source.Equals(t.Card0));
            Assert.IsNotNull(hit, "상대에게 2");
            Assert.AreEqual(2, hit.Before - hit.After);
            Assert.AreEqual(t.D, hit.PokemonId);
            // 바닥났다: 갈 칸을 묻는다(안 감 = 고향 마을이 첫 답·기본 답)
            Assert.IsNotNull(t.S.Battle, "배틀을 닫기 전에 묻는다");
            var ask = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            Assert.AreEqual(0, ask.Seat);
            Assert.AreEqual(t.Card0, ask.Source);
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind);
            int here = t.S.Seats[0].Cell, far = -1;
            for (int c = ctx.Board.Count - 1; c >= 0 && far < 0; c--)
                if (!ctx.Board[c].DistortionWorld && c != here) far = c;
            var legal = Scene.Legal(t.S, ctx, ask);
            Assert.IsNotNull(legal.Find(a => a.Kind == AnswerKind.Pick && a.Target == here), "지금 칸도 고를 수 있다(원작 커뮤니티 확인)");
            Battles.Answer(t.S, ctx, ask, ChoiceAnswer.Pick(far), t.Log);
            Assert.IsNull(t.S.Battle);
            Assert.AreEqual(far, t.S.Seats[0].Cell);
            Assert.IsTrue(t.S.Seats[0].RespawnPending, "다음 내 턴 시작에 완전 회복");
            Assert.AreEqual(far, One<Whiteout>(Events(t)).ToCell);
        }

        [Test]
        public void A20_6_team_rocket_빨강_안가면고향마을로_실버에서는묻지않는다()
        {
            var t = Blast();
            t.S.Seats[0].Cell = t.S.Seats[1].Cell; // 고향 마을이 아닌 칸에서 졌다
            var ask = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectPick);
            Battles.Answer(t.S, ctx, ask, ask.Default.Clone(), t.Log);
            Assert.IsNull(t.S.Battle);
            Assert.AreEqual(ctx.Fixtures.Fixtures[t.S.Seats[0].HomeVillage].Cell, t.S.Seats[0].Cell);
            Assert.IsTrue(t.S.Seats[0].RespawnPending);
            t = Blast(TrainerRules.Silver);
            Assert.IsNull(t.S.Battle, "빨강이 없으면 바로 끝난다");
            Assert.AreEqual(Max(t, t.D), Hp(t, t.D), "2 피해도 없다");
        }

        [Test]
        public void A20_6_team_rocket_파랑_트레이너전에서상대포켓몬을굴림빼기2로잡는다_붙은것그대로_해금으로센다()
        {
            // 「During a battle with another trainer, you can attempt to capture their Pokémon but with -2 to your capture rolls. The captured Pokémon keeps everything it currently has attached.」
            int band = -1;
            for (int i = 0; i < ctx.Items.Items.Count && band < 0; i++)
                if (ctx.Items.Items[i].Kind == ItemKind.Held) band = i;
            var t = Field("team_rocket", new[] { Garchomp }, new[] { Torterra, Bidoof }, TrainerRules.Silver, a =>
            {
                a.S.Seats[0].Balls[SeatState.BallKinds - 1] = 1;
                a.S.Pokemon[a.Theirs[0]].HeldItem = band;
                a.S.Pokemon[a.Theirs[0]].Moves[1] = Mv("tackle");
            });
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            var ball = Scene.Legal(t.S, ctx, ch).Find(a => a.Kind == AnswerKind.ThrowBall);
            Assert.IsNotNull(ball, "트레이너전에서도 볼을 던질 수 있다");
            int n = CaptureRules.Dice((Ball)ball.Target);
            var faces = new AttackDieFace[n];
            for (int i = 0; i < n; i++) faces[i] = C;
            Next(t, faces);
            t.From = t.Log.Events.Count;
            int stolen = t.D;
            Battles.Answer(t.S, ctx, ch, ball, t.Log);
            var ev = Events(t);
            var attempt = One<CaptureAttempt>(ev);
            Assert.AreEqual(2 * n - 2, attempt.Sum, "굴림 −2");
            Assert.AreEqual(5, attempt.CatchRate, "강함 5");
            Assert.IsTrue(attempt.Success);
            Assert.AreEqual(0, t.S.Pokemon[stolen].Owner);
            Assert.GreaterOrEqual(PokemonRules.PartyIndexOf(t.S.Seats[0], stolen), 0);
            Assert.AreEqual(-1, PokemonRules.PartyIndexOf(t.S.Seats[1], stolen));
            Assert.AreEqual(1, PokemonRules.PartyCount(t.S.Seats[1]));
            Assert.AreEqual(band, t.S.Pokemon[stolen].HeldItem, "지닌물건이 따라온다");
            Assert.AreEqual(Mv("tackle"), t.S.Pokemon[stolen].Moves[1], "배운 기술이 따라온다");
            Assert.AreEqual(TrainerRules.Gold, t.S.Seats[0].TrainerCardTier, "강함 포켓몬을 얻었다 — 실버 → 골드(A20-3)");
            Assert.AreEqual(1, All<CardUnlocked>(ev).Count);
            Assert.IsTrue(t.S.Battle.Sides[1].FaintedThisBattle, "잡힌 쪽은 기권할 수 있다(원작 커뮤니티 확인)");
            // 쓰러뜨렸을 때처럼 교체 창 → 상대의 다음 포켓몬. 잡은 포켓몬은 그 배틀에 못 낸다(결정 84 T7 ③) — 교체할 포켓몬이 없으면 창도 없다
            Assert.IsNull(t.S.Pending.Find(c => c.Kind == ChoiceKind.SwitchWindow), "빼앗은 포켓몬은 교체 후보가 아니다");
            Assert.AreEqual(t.Theirs[1], t.S.Battle.Sides[1].Active, "배틀은 다음 포켓몬으로 이어진다");
        }

        [Test]
        public void A20_6_team_rocket_파랑_빼앗은포켓몬은그배틀에못내고_굴림에디아루가없고_둘째지닌물건은가방으로_결정84()
        {
            // 사용자 결정 84 T7 ① 메모 「디아루가도 못굴리게」 · ③ 나(그 배틀에는 못 낸다) · ④ 나(둘째 지닌물건은 거래처럼 빼앗은 좌석의 가방으로)
            int band = -1, scarf = -1;
            for (int i = 0; i < ctx.Items.Items.Count; i++)
                if (ctx.Items.Items[i].Kind == ItemKind.Held)
                {
                    if (band < 0) band = i;
                    else if (scarf < 0) scarf = i;
                }
            int dialga = FateRules.CardKey(ctx, FateCard.Dialga);
            var t = Field("team_rocket", new[] { Garchomp, Turtwig }, new[] { Torterra, Bidoof }, TrainerRules.Silver, a =>
            {
                a.S.Seats[0].Balls[SeatState.BallKinds - 1] = 1;
                a.S.Seats[0].FateHand.Add(dialga);
                a.S.Pokemon[a.Theirs[0]].HeldItem = band;
                a.S.Pokemon[a.Theirs[0]].HeldItem2 = scarf; // 상대 좌석이 backpacker R이었다고 친다
            });
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            var ball = Scene.Legal(t.S, ctx, ch).Find(a => a.Kind == AnswerKind.ThrowBall);
            int n = CaptureRules.Dice((Ball)ball.Target);
            var faces = new AttackDieFace[n];
            for (int i = 0; i < n; i++) faces[i] = C;
            Next(t, faces);
            t.From = t.Log.Events.Count;
            int stolen = t.D;
            Battles.Answer(t.S, ctx, ch, ball, t.Log);
            var ev = Events(t);
            Assert.AreEqual(0, All<ChoiceRequested>(ev).FindAll(e => e.Kind == ChoiceKind.Reaction).Count, "디아루가 재굴림 창이 열리지 않는다");
            Assert.IsTrue(One<CaptureAttempt>(ev).Success);
            Assert.AreEqual(band, t.S.Pokemon[stolen].HeldItem, "첫 지닌물건은 따라온다");
            Assert.AreEqual(-1, t.S.Pokemon[stolen].HeldItem2);
            CollectionAssert.Contains(t.S.Seats[0].Bag, scarf, "둘째 것은 빼앗은 좌석의 가방으로");
            // 교체 창에는 원래 포켓몬만
            var sw = Battles.Only(t.S, ChoiceKind.SwitchWindow);
            var picks = Scene.Legal(t.S, ctx, sw).FindAll(a => a.Kind == AnswerKind.Pick).ConvertAll(a => a.Target);
            CollectionAssert.AreEqual(new[] { t.Mine[1] }, picks, "빼앗은 포켓몬은 그 배틀에 못 낸다");
            Battles.Answer(t.S, ctx, sw, ChoiceAnswer.Of(AnswerKind.Decline), t.Log);
            // 원래 포켓몬이 모두 쓰러지면 빼앗은 포켓몬이 남아도 진다
            Battles.SetHp(t.S, ctx, t.Mine[1], 0);
            Battles.SetHp(t.S, ctx, t.A, 0);
            Assert.IsFalse(BattleRules.SideHasUsable(t.S, ctx, t.S.Battle.Sides[0]), "빼앗은 포켓몬은 쓸 포켓몬으로 세지 않는다");
            Assert.IsTrue(BattleRules.HasUsable(t.S, ctx, 0), "배틀 밖에서는 내 포켓몬이다");
        }

        [Test]
        public void A20_6_team_rocket_파랑_카드가없거나상대의마지막포켓몬이면볼이없다()
        {
            System.Action<Arena> balls = a => a.S.Seats[0].Balls[(int)Ball.Regular] = 2;
            var t = Field(null, new[] { Garchomp }, new[] { Torterra, Bidoof }, TrainerRules.Gold, balls);
            Assert.IsNull(Scene.Legal(t.S, ctx, Battles.Only(t.S, ChoiceKind.BattleAction)).Find(a => a.Kind == AnswerKind.ThrowBall));
            t = Field("team_rocket", new[] { Garchomp }, new[] { Torterra, Bidoof }, TrainerRules.Bronze, balls);
            Assert.IsNull(Scene.Legal(t.S, ctx, Battles.Only(t.S, ChoiceKind.BattleAction)).Find(a => a.Kind == AnswerKind.ThrowBall), "브론즈");
            t = Field("team_rocket", new[] { Garchomp }, new[] { Torterra }, TrainerRules.Gold, balls);
            Assert.IsNull(Scene.Legal(t.S, ctx, Battles.Only(t.S, ChoiceKind.BattleAction)).Find(a => a.Kind == AnswerKind.ThrowBall), "마지막 포켓몬은 못 잡는다");
            t = Field("team_rocket", new[] { Garchomp }, new[] { Torterra, Bidoof }, TrainerRules.Gold, balls);
            Assert.IsNotNull(Scene.Legal(t.S, ctx, Battles.Only(t.S, ChoiceKind.BattleAction)).Find(a => a.Kind == AnswerKind.ThrowBall));
        }

        [Test]
        public void A20_6_team_rocket_파랑_균열카드가붙은포켓몬은못잡는다()
        {
            // p.24 「Any action that leads to a Distortion Breach card leaving your party is not allowed」(rulings A52 ⑤) — 결정 84 묶음의 UB 승리 길 봇 판(6인 시드 49)이 찾았다: 빼앗은 포켓몬의 균열 카드가 놓아주기로 사라졌다
            var t = Field("team_rocket", new[] { Garchomp }, new[] { Torterra, Bidoof }, TrainerRules.Gold, a =>
            {
                a.S.Seats[0].Balls[(int)Ball.Regular] = 2;
                a.S.Pokemon[a.Theirs[0]].Breach[0] = 0;
            });
            Assert.IsNull(Scene.Legal(t.S, ctx, Battles.Only(t.S, ChoiceKind.BattleAction)).Find(a => a.Kind == AnswerKind.ThrowBall), "균열 카드가 붙은 상대");
        }

        [Test]
        public void A20_6_expert_빨강_비스트부스트대신회복3_피로0_다음공격2()
        {
            // 「When Beast Boost triggers, your Legendary Pokémon heal 3 damage, reset their battle fatigue to 0 and increase the attack strength of their next attack by 2.」
            System.Action<Arena> prep = a =>
            {
                a.S.Pokemon[a.Mine[0]].HasLegendaryMove = true;
                a.S.Pokemon[a.Mine[0]].Damage = 5;
                a.S.Pokemon[a.Mine[0]].Fatigue = 4;
                Battles.SetHp(a.S, ctx, a.Theirs[0], 1);
            };
            var t = Field("expert", new[] { Empoleon }, new[] { Bidoof, Turtwig }, TrainerRules.Gold, prep);
            Assert.IsTrue(LegendRules.HasBeastBoost(t.S.Pokemon[t.A]));
            Use(t, "water_gun", R, R);
            Assert.AreEqual(1, All<Fainted>(Events(t)).Count);
            Assert.AreEqual(2, t.S.Battle.Sides[0].BeastBoost, "+1 대신 +2(원작 커뮤니티 확인)");
            Assert.AreEqual(0, t.S.Pokemon[t.A].Fatigue);
            Assert.AreEqual(Max(t, t.A) - 5 + 3, Hp(t, t.A));
            Assert.AreEqual(1, All<AbilityUsed>(Events(t)).Count);
            // 카드가 없으면 보통 비스트 부스트(피로 −3 · +1)
            t = Field(null, new[] { Empoleon }, new[] { Bidoof, Turtwig }, TrainerRules.Gold, prep);
            Use(t, "water_gun", R, R);
            Assert.AreEqual(1, t.S.Battle.Sides[0].BeastBoost);
            Assert.AreEqual(5 - 3, t.S.Pokemon[t.A].Fatigue);
            Assert.AreEqual(Max(t, t.A) - 5, Hp(t, t.A));
        }

        [Test]
        public void A20_6_lady_빨강_배틀당한번_파티의포켓몬하나를완전회복_턴을쓰지않는다()
        {
            // 「Once per battle, heal all damage and status effects from a non-fainted Pokémon in your party.」
            System.Action<Arena> prep = a =>
            {
                a.S.Pokemon[a.Mine[0]].Damage = 4;
                a.S.Pokemon[a.Mine[0]].Status = StatusEffect.Poisoned;
                Battles.SetHp(a.S, ctx, a.Mine[1], 0);
            };
            var t = Field("lady", new[] { Garchomp, Turtwig, Piplup }, new[] { Bidoof }, TrainerRules.Gold, prep);
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            var potions = Scene.Legal(t.S, ctx, ch).FindAll(a => a.Kind == AnswerKind.UseAbility);
            Assert.AreEqual(1, potions.Count, "쓰러진 포켓몬·고칠 것이 없는 포켓몬은 없다");
            Assert.AreEqual(t.A, potions[0].Target, "나와 있는 포켓몬도 파티다(원작 커뮤니티 확인)");
            Assert.AreNotEqual(AnswerKind.UseAbility, ch.Default.Kind);
            int turn = t.S.Battle.Turn;
            t.From = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ch, potions[0], t.Log);
            Assert.AreEqual(Max(t, t.A), Hp(t, t.A));
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.A].Status);
            Assert.AreEqual(1, All<AbilityUsed>(Events(t)).Count);
            Assert.AreEqual(1, All<Healed>(Events(t)).Count);
            ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            Assert.AreEqual(0, ch.Seat, "턴을 쓰지 않는다 — 다시 내 배틀 행동");
            Assert.AreEqual(turn, t.S.Battle.Turn);
            t.S.Pokemon[t.A].Damage = 2;
            Assert.IsNull(Scene.Legal(t.S, ctx, ch).Find(a => a.Kind == AnswerKind.UseAbility), "배틀당 한 번");
            // 실버에서는 없다
            t = Field("lady", new[] { Garchomp }, new[] { Bidoof }, TrainerRules.Silver, a => a.S.Pokemon[a.Mine[0]].Damage = 4);
            Assert.IsNull(Scene.Legal(t.S, ctx, Battles.Only(t.S, ChoiceKind.BattleAction)).Find(a => a.Kind == AnswerKind.UseAbility));
        }

        [Test]
        public void A20_6_lady_빨강_반응창에서는묻지않는다_내배틀행동에서만_결정84()
        {
            // 사용자 결정 84 T6 다: 내 배틀 행동 고르기에서만(턴을 쓰지 않는다) — 자체 검토가 넓힌 반응 창(원작 커뮤니티 확인)은 거둔다
            var t = Field("lady", new[] { Garchomp }, new[] { Bidoof }, TrainerRules.Gold);
            Use(t, "water_gun", S, S);
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            Assert.AreEqual(1, ch.Seat, "상대 차례");
            t.S.Pokemon[t.A].Damage = 4;
            t.S.Pokemon[t.A].Status = StatusEffect.Poisoned;
            Use(t, "tackle", R, R);
            Assert.IsNull(t.S.Pending.Find(c => c.Kind == ChoiceKind.Reaction && c.Seat == 0), "상대 공격의 굴리기 전·굴린 뒤 창에서 묻지 않는다");
            Assert.AreEqual(0, All<AbilityUsed>(Events(t)).Count);
            Assert.IsFalse(t.S.Battle.Sides[0].Potion);
            ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            Assert.AreEqual(0, ch.Seat);
            var potion = Scene.Legal(t.S, ctx, ch).Find(a => a.Kind == AnswerKind.UseAbility && a.Target == t.A);
            Assert.IsNotNull(potion, "내 배틀 행동에서는 쓴다");
            int turn = t.S.Battle.Turn;
            Battles.Answer(t.S, ctx, ch, potion, t.Log);
            Assert.AreEqual(Max(t, t.A), Hp(t, t.A));
            Assert.AreEqual(StatusEffect.None, t.S.Pokemon[t.A].Status);
            Assert.AreEqual(0, Battles.Only(t.S, ChoiceKind.BattleAction).Seat, "턴을 쓰지 않는다");
            Assert.AreEqual(turn, t.S.Battle.Turn);
        }

        [Test]
        public void A20_6_kindler_빨강_불꽃기술에장작1로화상과2피해()
        {
            // 「When you attack with a Fire-type move, you may inflict the Burned status effect and deal 2 damage to the opposing Pokémon by using 1 Firewood from your bag.」
            var t = Field("kindler", new[] { Monferno }, new[] { Bastiodon }, TrainerRules.Gold, a => a.S.Seats[0].Firewood = 2);
            Use(t, "flame_wheel", R, R, R);
            var ask = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectOption);
            Assert.AreEqual(t.Card0, ask.Source);
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind, "「you may」 — 기본 답은 안 씀");
            Battles.Answer(t.S, ctx, ask, ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
            var ev = Events(t);
            int dealt = One<DamageBreakdown>(ev).Final;
            Assert.AreEqual(1, t.S.Seats[0].Firewood);
            Assert.IsTrue(StatusRules.Has(t.S.Pokemon[t.D], StatusEffect.Burned));
            Assert.AreEqual(Max(t, t.D) - dealt - 2, Hp(t, t.D));
            Assert.AreEqual(1, All<AbilityUsed>(ev).Count);
            // 안 쓰면 장작도 피해도 그대로
            t = Field("kindler", new[] { Monferno }, new[] { Bastiodon }, TrainerRules.Gold, a => a.S.Seats[0].Firewood = 2);
            Use(t, "flame_wheel", R, R, R);
            ask = Battles.Only(t.S, ChoiceKind.Reaction, ChoiceSub.EffectOption);
            Battles.Answer(t.S, ctx, ask, ask.Default.Clone(), t.Log);
            Assert.AreEqual(2, t.S.Seats[0].Firewood);
            Assert.AreEqual(Max(t, t.D) - One<DamageBreakdown>(Events(t)).Final, Hp(t, t.D));
            // 장작이 없거나 불꽃 기술이 아니면 묻지 않는다
            t = Field("kindler", new[] { Monferno }, new[] { Bastiodon });
            Use(t, "flame_wheel", R, R, R);
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
            t = Field("kindler", new[] { Monferno }, new[] { Bastiodon }, TrainerRules.Gold, a => a.S.Seats[0].Firewood = 2);
            Use(t, "tackle", R, R);
            Assert.AreEqual(1, Battles.Only(t.S, ChoiceKind.BattleAction).Seat);
        }

        // ── 화상·졸림의 효과 바꾸기 ──

        Maintenance Afflict(int pokemon, StatusEffect status) => new Maintenance { Kind = MaintKind.Afflict, Pokemon = pokemon, Value = (int)status };

        [Test]
        public void A20_6_black_belt_빨강_내턴배틀밖에서내포켓몬에게화상_불꽃타입은면역()
        {
            // 「During your turn whilst outside of battle, you may inflict the Burned status on your Pokémon.」
            var s = Battles.Game(ctx, 2);
            var log = new EventLog();
            int riolu = Scene.Give(s, ctx, 0, Riolu), monferno = Scene.Give(s, ctx, 0, Monferno), bidoof = Scene.Give(s, ctx, 0, Bidoof);
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, Afflict(riolu, StatusEffect.Burned), log), RejectReason.Illegal, "카드가 없다");
            var card = Card(s, 0, "black_belt", TrainerRules.Gold);
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, Afflict(riolu, StatusEffect.Drowsy), log), RejectReason.Illegal, "졸림은 fairy_tale_girl");
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, Afflict(monferno, StatusEffect.Burned), log), RejectReason.Illegal, "불꽃 타입은 화상 면역(원작 커뮤니티 확인)");
            s.ActiveSeat = 1;
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, Afflict(riolu, StatusEffect.Burned), log), RejectReason.Timing, "내 턴이 아니다");
            s.ActiveSeat = 0;
            Scene.Accept(MaintenanceRules.Submit(s, ctx, 0, Afflict(riolu, StatusEffect.Burned), log));
            Assert.AreEqual(StatusEffect.Burned, s.Pokemon[riolu].Status);
            Assert.AreEqual(1, All<AbilityUsed>(log.Events).Count);
            Assert.AreEqual(card, One<StatusApplied>(log.Events).Source);
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, Afflict(riolu, StatusEffect.Burned), log), RejectReason.NoChange);
            Scene.Accept(MaintenanceRules.Submit(s, ctx, 0, Afflict(bidoof, StatusEffect.Burned), log), "격투 타입이 아니어도 건다(효과는 보통 화상)");
            s.Seats[0].TrainerCardTier = TrainerRules.Silver;
            s.Pokemon[riolu].Status = StatusEffect.None;
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, Afflict(riolu, StatusEffect.Burned), log), RejectReason.Illegal, "실버");
        }

        [Test]
        public void A20_6_black_belt_빨강_격투타입의화상은총피해2증가()
        {
            // 「Replace the effect of Burned when applied to your Fighting-type Pokémon: Increase the total damage you deal by 2.」
            var t = Field("black_belt", new[] { Lucario }, new[] { Bidoof }, TrainerRules.Gold, a => a.S.Pokemon[a.Mine[0]].Status = StatusEffect.Burned);
            Use(t, "tackle", R, R);
            var ev = Events(t);
            var d = One<DamageBreakdown>(ev);
            Assert.AreEqual(2 + One<TypeMatchup>(ev).Delta + 2, d.Final);
            var steps = new List<DamageStep>(d.Steps);
            Assert.IsNotNull(steps.Find(x => x.Kind == DamageStepKind.Reduction && x.Value == 2 && x.Source.Equals(t.Card0)));
            Assert.IsNull(steps.Find(x => x.Source.Kind == SourceKind.Status), "화상의 −2는 없다");
            // 카드가 없으면 보통 화상(−2)
            t = Field(null, new[] { Lucario }, new[] { Bidoof }, TrainerRules.Gold, a => a.S.Pokemon[a.Mine[0]].Status = StatusEffect.Burned);
            Use(t, "tackle", R, R);
            Assert.AreEqual(0, One<DamageBreakdown>(Events(t)).Final);
        }

        [Test]
        public void A20_6_fairy_tale_girl_빨강_졸림걸기_페어리의졸림은면바꾸기대신턴끝회복1()
        {
            // 「During your turn whilst outside of battle, you may inflict the Drowsy status on your Pokémon. Replace the effect of Drowsy when applied to your Fairy-type Pokémon: At the end of your turns during battle, heal 1 damage.」
            var s = Battles.Game(ctx, 2);
            var log = new EventLog();
            int togepi = Scene.Give(s, ctx, 0, Togepi);
            Card(s, 0, "fairy_tale_girl", TrainerRules.Gold);
            Scene.Reject(MaintenanceRules.Submit(s, ctx, 0, Afflict(togepi, StatusEffect.Burned), log), RejectReason.Illegal, "화상은 black_belt");
            Scene.Accept(MaintenanceRules.Submit(s, ctx, 0, Afflict(togepi, StatusEffect.Drowsy), log));
            Assert.AreEqual(StatusEffect.Drowsy, s.Pokemon[togepi].Status);
            // 배틀: 졸림의 면 바꾸기(일반 타격 → 심볼)가 없고 턴 끝에 1(+ 파랑 Wishing Star 1) 회복
            var t = Field("fairy_tale_girl", new[] { Togepi }, new[] { Bastiodon }, TrainerRules.Gold, a =>
            {
                a.S.Pokemon[a.Mine[0]].Status = StatusEffect.Drowsy;
                a.S.Pokemon[a.Mine[0]].Damage = 4;
            });
            Use(t, "tackle", R, R);
            var ev = Events(t);
            Assert.IsNull(All<DiceFacesChanged>(ev).Find(x => x.Source.Kind == SourceKind.Status), "졸림의 면 바꾸기가 없다");
            Assert.AreEqual(Max(t, t.A) - 4 + 2, Hp(t, t.A));
            // 카드가 없으면 보통 졸림
            t = Field(null, new[] { Togepi }, new[] { Bastiodon }, TrainerRules.Gold, a =>
            {
                a.S.Pokemon[a.Mine[0]].Status = StatusEffect.Drowsy;
                a.S.Pokemon[a.Mine[0]].Damage = 4;
            });
            Use(t, "tackle", R, R);
            Assert.IsNotNull(All<DiceFacesChanged>(Events(t)).Find(x => x.Source.Kind == SourceKind.Status));
            Assert.AreEqual(Max(t, t.A) - 4, Hp(t, t.A));
        }
    }
}
