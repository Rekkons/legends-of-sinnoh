using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「제작·은행·진화」(규칙): 제작 레시피 14(S10 — 볼 4 · 스튜 4 · 지닌물건 6, 볼을 얻는 길) · 은행상회(rulings A4, S11) · 진화(S9_1, A9, Lua R14) · 결단의 머리띠.
    /// </summary>
    public class CraftBankEvolveTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static ChoiceAnswer A(AnswerKind k, int target = -1, int arg = -1) => ChoiceAnswer.Of(k, target, arg);

        int Item(string key) => Scene.Item(ctx, key);

        /// <summary>가방 규토리를 꼭 이것만으로.</summary>
        static void Bag(SeatState st, params ApricornColor[] colors)
        {
            for (int i = 0; i < st.Apricorns.Length; i++) st.Apricorns[i] = 0;
            foreach (var c in colors) st.Apricorns[(int)c]++;
        }

        static int Total(SeatState st)
        {
            int n = 0;
            foreach (int a in st.Apricorns) n += a;
            return n;
        }

        static void Craft(GameState s, Ctx ctx, int item, EventLog log = null) => Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Craft, item), log);

        /// <summary>모든 좌석이 턴을 넘겨 같은 좌석의 다음 턴으로.</summary>
        static void NextOwnTurn(GameState s, Ctx ctx)
        {
            for (int i = 0; i < s.SeatCount; i++) Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn));
        }

        // ── 제작 ──

        [Test]
        public void M5_제작_레시피14개는재료를내고얻는다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 31, log);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int balls = 0, stews = 0, held = 0;
            for (int i = 0; i < ctx.Items.Items.Count; i++)
            {
                var d = ctx.Items.Items[i];
                if (d.Recipe == null) continue;
                Bag(st);
                Assert.IsFalse(ChoiceRules.Contains(Flow.MapLegal(s, ctx), A(AnswerKind.Craft, i)), $"{d.Key}: 재료 없이");
                var recipe = new List<ApricornColor>(d.Recipe);
                Bag(st, recipe.GetRange(0, recipe.Count - 1).ToArray());
                Assert.IsFalse(ChoiceRules.Contains(Flow.MapLegal(s, ctx), A(AnswerKind.Craft, i)), $"{d.Key}: 재료 하나 모자람");
                Bag(st, recipe.ToArray());
                Assert.IsTrue(ChoiceRules.Contains(Flow.MapLegal(s, ctx), A(AnswerKind.Craft, i)), d.Key);
                int bagBefore = st.Bag.Count;
                log.Clear();
                Craft(s, ctx, i, log);
                Assert.AreEqual(0, Total(st), $"{d.Key}: 재료를 모두 냈다");
                Assert.AreEqual(ActionKind.Craft, Flow.Of<ActionStarted>(log)[0].Kind);
                var ev = Flow.Of<Crafted>(log)[0];
                Assert.AreEqual(i, ev.ItemKey);
                Assert.AreEqual(seat, ev.Seat);
                int paid = 0;
                foreach (var c in ev.Changes)
                    if (c.Res == ResKind.Apricorn) paid += c.Before - c.After;
                Assert.AreEqual(recipe.Count, paid, $"{d.Key}: Changes가 낸 규토리를 싣는다");
                switch (d.Kind)
                {
                    case ItemKind.Ball:
                    {
                        int ball = CraftRules.BallOf(ctx, i);
                        Assert.AreEqual(i, CraftRules.BallItem(ctx, (Ball)ball));
                        Assert.AreEqual(1, st.Balls[ball], d.Key);
                        Assert.IsTrue(System.Array.Exists(ev.Changes, c => c.Res == ResKind.Ball && c.Key == ball && c.Before == 0 && c.After == 1));
                        balls++;
                        break;
                    }
                    case ItemKind.Stew:
                        Assert.AreEqual(i, Flow.Of<StewEaten>(log)[0].ItemKey);
                        Assert.AreEqual(bagBefore, st.Bag.Count, "스튜는 바로 먹는다");
                        stews++;
                        break;
                    default:
                        Assert.AreEqual(ItemKind.Held, d.Kind);
                        Assert.AreEqual(bagBefore + 1, st.Bag.Count);
                        Assert.AreEqual(i, st.Bag[st.Bag.Count - 1], "지닌물건은 가방으로");
                        Assert.IsTrue(System.Array.Exists(ev.Changes, c => c.Res == ResKind.Item && c.Key == i && c.After == c.Before + 1));
                        held++;
                        break;
                }
                Assert.AreEqual(seat, Flow.Only(s, ChoiceKind.MapAction).Seat, "제작은 턴을 끝내지 않는다");
            }
            Assert.AreEqual(4, balls, "볼 4");
            Assert.AreEqual(4, stews, "스튜 4");
            Assert.AreEqual(6, held, "지닌물건 6");
        }

        [Test]
        public void M5_제작_몬스터볼은빨강1개_새판의빨강3개로볼3개()
        {
            // §5 #33 볼 얻는 길: 1막 공개의 빨강 규토리 3개(G12)로 첫 턴부터 몬스터볼을 만든다
            var s = Flow.Started(ctx, 2, 17);
            var st = s.Seats[s.ActiveSeat];
            int ball = Item("item.poke_ball");
            Assert.AreEqual(3, st.Apricorns[(int)ApricornColor.Red]);
            Assert.AreEqual(0, st.Balls[(int)Ball.Regular], "새 판은 볼 0");
            for (int i = 0; i < 3; i++) Craft(s, ctx, ball);
            Assert.AreEqual(3, st.Balls[(int)Ball.Regular]);
            Assert.AreEqual(0, st.Apricorns[(int)ApricornColor.Red]);
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Craft), "규토리가 없으면 만들 것이 없다");
            // 볼은 종류마다 몇 번이든(스튜만 턴당 1번)
            Bag(st, ApricornColor.Black, ApricornColor.Yellow, ApricornColor.Black, ApricornColor.Yellow);
            Craft(s, ctx, Item("item.ultimate_ball"));
            Craft(s, ctx, Item("item.ultimate_ball"));
            Assert.AreEqual(2, st.Balls[(int)Ball.Ultimate]);
        }

        [Test]
        public void M5_제작_스튜는종류마다턴당1번_다음내턴에다시()
        {
            var s = Flow.Started(ctx, 2, 19);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int restoring = Item(CraftRules.RestoringStew), zesty = Item(CraftRules.ZestyStew);
            Bag(st, ApricornColor.Yellow, ApricornColor.Blue, ApricornColor.Yellow, ApricornColor.Blue, ApricornColor.White, ApricornColor.Yellow);
            Craft(s, ctx, restoring);
            Assert.IsTrue(CraftRules.CanPay(st, ctx.Items.Items[restoring].Recipe), "재료는 남았다");
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsFalse(ChoiceRules.Contains(legal, A(AnswerKind.Craft, restoring)), "같은 스튜는 턴당 1번");
            Assert.IsTrue(ChoiceRules.Contains(legal, A(AnswerKind.Craft, zesty)), "다른 스튜는 된다");
            Craft(s, ctx, zesty);
            Assert.IsFalse(ChoiceRules.Contains(Flow.MapLegal(s, ctx), A(AnswerKind.Craft, zesty)));
            NextOwnTurn(s, ctx);
            Assert.AreEqual(seat, s.ActiveSeat);
            Assert.IsTrue(ChoiceRules.Contains(Flow.MapLegal(s, ctx), A(AnswerKind.Craft, restoring)), "다음 내 턴에는 다시");
        }

        [Test]
        public void M5_제작_회복스튜는파티전체를완전회복()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 23, log);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int other = Scene.Give(s, ctx, seat, "399:0");
            var a = s.Pokemon[st.Party[0]];
            var b = s.Pokemon[other];
            a.Damage = 2;
            a.Status = StatusEffect.Poisoned;
            b.Damage = PokemonRules.MaxHp(ctx, b);
            Flow.StepInto(s, ctx, st.Cell); // 마을 밖에서도
            st.Cell = Flow.StepInto(s, ctx, st.Cell);
            Bag(st, ApricornColor.Yellow, ApricornColor.Blue);
            int stew = Item(CraftRules.RestoringStew);
            log.Clear();
            Craft(s, ctx, stew, log);
            Assert.AreEqual(0, a.Damage);
            Assert.AreEqual(StatusEffect.None, a.Status);
            Assert.AreEqual(0, b.Damage, "기절한 포켓몬도");
            var healed = Flow.Of<Healed>(log)[0];
            Assert.AreEqual(SourceKind.Item, healed.Source.Kind);
            Assert.AreEqual(stew, healed.Source.Id);
            Assert.AreEqual(2, healed.Pokemon.Length);
        }

        [Test]
        public void M5_제작_자극적인스튜는이번턴이동력3_우두머리를막는다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 29, log);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int before = st.MovesLeft;
            Assert.IsFalse(CraftRules.AlphaWarded(s, ctx, seat));
            Bag(st, ApricornColor.White, ApricornColor.Yellow);
            log.Clear();
            Craft(s, ctx, Item(CraftRules.ZestyStew), log);
            Assert.AreEqual(before + 3, st.MovesLeft);
            var eaten = Flow.Of<StewEaten>(log)[0];
            Assert.AreEqual(3, eaten.Moves);
            Assert.AreEqual(before + 3, eaten.MovesLeft);
            Assert.IsTrue(CraftRules.AlphaWarded(s, ctx, seat), "이번 턴 우두머리가 배틀을 걸지 않는다");
            // 늘어난 이동력으로 더 멀리
            var steps = MoveRules.Reachable(ctx.Board, st.Cell, st.MovesLeft, s.Options.ElevationMode);
            int far = System.Array.FindIndex(steps, d => d == before + 1);
            if (far >= 0) Assert.IsTrue(ChoiceRules.Contains(Flow.MapLegal(s, ctx), A(AnswerKind.Move, far)));
            NextOwnTurn(s, ctx);
            Assert.AreEqual(before, st.MovesLeft, "다음 턴에는 원래 이동력");
            Assert.IsFalse(CraftRules.AlphaWarded(s, ctx, seat));
        }

        // 카드 문구(v1.4a) 「Discard only half of the Journey Points needed to evolve 1 Pokémon this turn」(「next·first」가 없다) · 원작 커뮤니티 확인
        [Test]
        public void S9_1_우러난스튜는이번턴진화한번만반값_내림_어느진화에쓸지고른다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 37, log);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int bidoof = Scene.Give(s, ctx, seat, "399:0");   // 진화 비용 5 → 반값 2
            int combee = Scene.Give(s, ctx, seat, "415:0");   // 진화 비용 5
            Assert.AreEqual(5, ctx.Cards.Pokemon[s.Pokemon[bidoof].Def].EvolveCost);
            st.Jp = 7;
            Assert.AreEqual(5, EvolveRules.Cost(s, ctx, seat, bidoof));
            Bag(st, ApricornColor.Pink, ApricornColor.White);
            Craft(s, ctx, Item(CraftRules.InfusedStew));
            Assert.IsTrue(CraftRules.InfusedReady(s, seat));
            Assert.AreEqual(2, EvolveRules.Cost(s, ctx, seat, bidoof, true), "절반, 내림");
            Assert.AreEqual(5, EvolveRules.Cost(s, ctx, seat, bidoof), "제값도 고를 수 있다");
            var legal = Flow.MapLegal(s, ctx);
            Assert.IsTrue(ChoiceRules.Contains(legal, A(AnswerKind.Evolve, bidoof)) && ChoiceRules.Contains(legal, A(AnswerKind.Evolve, bidoof, 1)), "진화마다 제값·반값 두 답");
            // 첫 진화는 제값으로 하고 반값을 아껴 둔다
            st.Jp = 12;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Evolve, combee));
            if (s.Pending[0].Kind == ChoiceKind.Reward) Flow.Answer(s, ctx, ChoiceKind.Reward, A(AnswerKind.Decline));
            Assert.AreEqual(7, st.Jp);
            Assert.IsTrue(CraftRules.InfusedReady(s, seat), "제값으로 진화하면 반값이 남는다");
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Evolve, bidoof, 1), log);
            var ev = Flow.Of<Evolved>(log)[0];
            Assert.IsTrue(ev.Halved);
            Assert.AreEqual(2, ev.JpCost);
            Assert.AreEqual(5, st.Jp);
            Assert.IsFalse(CraftRules.InfusedReady(s, seat), "한 마리에만");
            if (s.Pending[0].Kind == ChoiceKind.Reward) Flow.Answer(s, ctx, ChoiceKind.Reward, A(AnswerKind.Decline));
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(a => a.Kind == AnswerKind.Evolve && a.Arg == 1), "쓴 뒤에는 반값 답이 없다");

            // 쓰지 않은 반값은 턴이 끝나면 사라진다(「this turn」)
            var t = Flow.Started(ctx, 2, 37);
            int seat2 = t.ActiveSeat;
            Bag(t.Seats[seat2], ApricornColor.Pink, ApricornColor.White);
            Craft(t, ctx, Item(CraftRules.InfusedStew));
            NextOwnTurn(t, ctx);
            Assert.IsFalse(CraftRules.InfusedReady(t, seat2));
        }

        /// <summary>화려한 만찬이 먹인 스튜 하나 몫(ShrineRules — 만든 것과 같은 기록, 원작 커뮤니티 확인).</summary>
        static void BanquetStew(GameState s, int seat, UsageKind kind, int key) => s.Used.Add(new UsageMark { Seat = seat, Kind = kind, Key = key, Scope = s.TurnId });

        [Test]
        public void S9_1_우러난스튜가둘이면반값진화도두번()
        {
            // 만찬의 우러난 스튜 + 만든 우러난 스튜 = 두 마리 반값(원작 커뮤니티 확인)
            var s = Flow.Started(ctx, 2, 37);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int bidoof = Scene.Give(s, ctx, seat, "399:0");
            int combee = Scene.Give(s, ctx, seat, "415:0");
            Bag(st, ApricornColor.Pink, ApricornColor.White);
            BanquetStew(s, seat, UsageKind.InfusedStew, -1);
            Craft(s, ctx, Item(CraftRules.InfusedStew));
            st.Jp = 4;
            foreach (int p in new[] { bidoof, combee })
            {
                Assert.IsTrue(CraftRules.InfusedReady(s, seat));
                Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Evolve, p, 1));
                if (s.Pending[0].Kind == ChoiceKind.Reward) Flow.Answer(s, ctx, ChoiceKind.Reward, A(AnswerKind.Decline));
            }
            Assert.AreEqual(0, st.Jp, "반값 2 × 2");
            Assert.IsFalse(CraftRules.InfusedReady(s, seat), "둘 다 썼다");
        }

        /// <summary>매혹적인 스튜를 먹고 약한 야생(포획률 3)에게 몬스터볼을 던진다. stew = false면 스튜 없이.</summary>
        GameState AllureScene(ulong seed, EventLog log, bool stew, out int seat)
        {
            var s = Flow.Started(ctx, 2, 41);
            seat = s.ActiveSeat;
            var st = s.Seats[seat];
            if (stew)
            {
                Bag(st, ApricornColor.Green, ApricornColor.Red);
                Craft(s, ctx, Item(CraftRules.AlluringStew));
            }
            st.Balls[(int)Ball.Regular] = 1;
            s.Pending.Clear();
            int wild = Scene.Spawn(s, ctx, "399:0", SeatId.Wild);
            s.Rng = new Pcg32(seed);
            Battles.Wild(s, ctx, log, seat, wild);
            if (MyAction(s, log, seat)) Battles.Answer(s, ctx, ChoiceKind.BattleAction, A(AnswerKind.ThrowBall, (int)Ball.Regular), log);
            return s;
        }

        /// <summary>그 좌석의 BattleAction이 열릴 때까지 다른 선택(우선권 토큰 · 상대 차례)에 기본값으로 답한다. 배틀이 끝나면 false.</summary>
        bool MyAction(GameState s, EventLog log, int seat)
        {
            for (int i = 0; i < 30 && s.Battle != null && s.Pending.Count == 1; i++)
            {
                var c = s.Pending[0];
                if (c.Kind == ChoiceKind.BattleAction && c.Seat == seat) return true;
                Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            }
            return false;
        }

        /// <summary>포획 굴림(없으면 null — 던지기 전에 배틀이 끝났다).</summary>
        static CaptureAttempt Throw(EventLog log)
        {
            var list = log.OfType<CaptureAttempt>();
            return list.Count > 0 ? list[0] : null;
        }

        static PendingChoice AllureAsk(GameState s) => s.Pending.Find(c => c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.AlluringStew);

        // 카드 문구(v1.4a) 「Replace any 1 status symbol with 1 regular hit once during your next wild encounter」 · 원작 커뮤니티 확인
        // · 원작 커뮤니티 확인(플레이어가 고른다 — 묻는다)
        [Test]
        public void M5_제작_매혹적인스튜는포획굴림에심볼이있으면쓸지묻는다_기본은성공이될때만씀()
        {
            // 스튜 없이: 합 2(포획률 3)에 심볼이 있어 실패하는 굴림
            ulong near = Battles.Seed(sd =>
            {
                var l = new EventLog();
                AllureScene(sd, l, false, out _);
                var c = Throw(l);
                return c != null && !c.Success && c.Sum == 2 && System.Array.IndexOf(c.Faces, AttackDieFace.StatusSymbol) >= 0;
            });
            var log = new EventLog();
            var s = AllureScene(near, log, true, out int seat);
            Assert.IsNull(Throw(log), "쓸지 답하기 전에는 판정하지 않는다");
            var ask = AllureAsk(s);
            Assert.IsNotNull(ask, "굴림에 심볼이 있으면 묻는다");
            Assert.AreEqual(seat, ask.Seat);
            var legal = new List<ChoiceAnswer>();
            ChoiceRules.Legal(s, ctx, ask, legal);
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind, "첫 답 = 안 씀");
            Assert.IsTrue(ChoiceRules.Contains(legal, A(AnswerKind.Accept)));
            Assert.AreEqual(AnswerKind.Accept, ask.Default.Kind, "시간 초과·봇 = 성공이 되면 쓴다");
            Battles.Answer(s, ctx, ask, A(AnswerKind.Accept), log);
            var cap = Throw(log);
            Assert.IsTrue(cap.Allured, "심볼 하나 → 일반 타격");
            Assert.IsTrue(cap.Success);
            Assert.AreEqual(3, cap.Sum);
            Assert.AreEqual(2, AttackDice.Sum(cap.Faces), "Faces는 굴린 그대로");
            Assert.IsNull(s.Battle, "잡았다");
            Assert.IsFalse(CraftRules.AlluringReady(s, seat), "한 번 쓰면 끝");

            // 바꿔도 실패인 굴림(합 ≤ 1)에도 묻는다 — 기본은 안 씀, 안 쓰면 아껴 두고 그 조우가 끝나면 사라진다
            ulong far = Battles.Seed(sd =>
            {
                var l = new EventLog();
                AllureScene(sd, l, false, out _);
                var c = Throw(l);
                return c != null && c.Sum <= 1 && System.Array.IndexOf(c.Faces, AttackDieFace.StatusSymbol) >= 0;
            });
            log.Clear();
            s = AllureScene(far, log, true, out seat);
            ask = AllureAsk(s);
            Assert.AreEqual(AnswerKind.Decline, ask.Default.Kind);
            Battles.Answer(s, ctx, ask, ask.Default.Clone(), log);
            Assert.IsFalse(Throw(log).Allured);
            Assert.IsFalse(Throw(log).Success);
            Assert.IsTrue(CraftRules.AlluringReady(s, seat), "아껴 둔다");
            Battles.Drive(s, ctx, log);
            Assert.IsNull(s.Battle);
            Assert.IsFalse(CraftRules.AlluringReady(s, seat), "다음 야생 조우까지만");
        }

        /// <summary>매혹적인 스튜를 먹고 약한 야생에게 내 출전 포켓몬이 기본 기술로 공격한다(굴린 뒤까지). stew = false면 스튜 없이.</summary>
        GameState AllureAttack(ulong seed, EventLog log, bool stew, out int seat, bool banquet = false)
        {
            var s = Flow.Started(ctx, 2, 41);
            seat = s.ActiveSeat;
            if (stew)
            {
                Bag(s.Seats[seat], ApricornColor.Green, ApricornColor.Red);
                Craft(s, ctx, Item(CraftRules.AlluringStew));
                if (banquet) BanquetStew(s, seat, UsageKind.AlluringStew, Item(CraftRules.AlluringStew));
            }
            s.Pending.Clear();
            int wild = Scene.Spawn(s, ctx, "399:0", SeatId.Wild);
            s.Rng = new Pcg32(seed);
            Battles.Wild(s, ctx, log, seat, wild);
            if (MyAction(s, log, seat))
            {
                var c = Battles.Only(s, ChoiceKind.BattleAction);
                Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            }
            return s;
        }

        /// <summary>그 쪽(0 = 도전자)의 첫 공격 굴림(없으면 null).</summary>
        static DiceRolled MyRoll(EventLog log)
        {
            foreach (var e in log.Events)
                if (e is DiceRolled d && d.Side == 0 && d.Kind == DiceKind.Attack) return d;
            return null;
        }

        // 원작 커뮤니티 확인 공격 굴림·볼 굴림 「Yup, can be used for either」(v1.4a) · 원작 커뮤니티 확인
        // · 원작 커뮤니티 확인
        [Test]
        public void M5_제작_매혹적인스튜는그야생배틀의내공격굴림에도쓸지묻는다_쓰면심볼하나가일반타격_만든턴이끝나면사라진다()
        {
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                AllureAttack(sd, l, false, out _);
                var r = MyRoll(l);
                return r != null && System.Array.IndexOf(r.Faces, AttackDieFace.StatusSymbol) >= 0;
            });
            var log = new EventLog();
            var s = AllureAttack(seed, log, true, out int seat);
            var ask = AllureAsk(s);
            Assert.IsNotNull(ask, "내 공격 굴림에 심볼이 있으면 묻는다");
            Assert.AreEqual(seat, ask.Seat);
            int before = AttackDice.Sum(s.Battle.Faces);
            int from = log.Events.Count;
            Battles.Answer(s, ctx, ask, A(AnswerKind.Accept), log);
            var after = log.Events.GetRange(from, log.Events.Count - from);
            var stew = new SourceRef(SourceKind.Item, Item(CraftRules.AlluringStew));
            var changed = after.FindAll(e => e is DiceFacesChanged d && d.Source.Same(stew));
            Assert.AreEqual(1, changed.Count);
            var ch = ((DiceFacesChanged)changed[0]).Changes;
            Assert.AreEqual(1, ch.Length);
            Assert.AreEqual(AttackDieFace.StatusSymbol, ch[0].Before);
            Assert.AreEqual(AttackDieFace.RegularHit, ch[0].After);
            var dmg = after.Find(e => e is DamageBreakdown) as DamageBreakdown;
            Assert.AreEqual(before + 1, dmg.Steps[0].Value, "주사위 합 +1");
            Assert.IsFalse(CraftRules.AlluringReady(s, seat), "한 번 쓰면 끝");

            // 만든 턴이 끝나면 쓰지 않았어도 사라진다
            var t = Flow.Started(ctx, 2, 41);
            int seat2 = t.ActiveSeat;
            Bag(t.Seats[seat2], ApricornColor.Green, ApricornColor.Red);
            Craft(t, ctx, Item(CraftRules.AlluringStew));
            Assert.IsTrue(CraftRules.AlluringReady(t, seat2));
            NextOwnTurn(t, ctx);
            Assert.IsFalse(CraftRules.AlluringReady(t, seat2));

            // 트레이너전·갤럭시단전(야생이 아닌 배틀)에서는 묻지 않는다
            var u = Battles.Game(ctx, 2);
            Scene.Give(u, ctx, 0, "399:0");
            Scene.Give(u, ctx, 1, "399:0");
            u.Used.Add(new UsageMark { Seat = 0, Kind = UsageKind.AlluringStew, Scope = u.TurnId });
            Battles.Start(u, ctx, new EventLog(), BattleKind.Trainer, 0, 1, -1);
            Assert.IsFalse(CraftRules.AlluringOpen(u, 0, new[] { AttackDieFace.StatusSymbol }));
        }

        [Test]
        public void M5_제작_매혹적인스튜가둘이면하나를써도그조우에하나가남는다()
        {
            // 만찬의 매혹적인 스튜 + 만든 매혹적인 스튜(원작 커뮤니티 확인) — 한 굴림에 하나 [가정 plan §5 #95 ③], 그 조우가 끝나면 둘 다 사라진다
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                AllureAttack(sd, l, false, out _);
                var r = MyRoll(l);
                return r != null && System.Array.IndexOf(r.Faces, AttackDieFace.StatusSymbol) >= 0;
            });
            var log = new EventLog();
            var s = AllureAttack(seed, log, true, out int seat, banquet: true);
            var ask = AllureAsk(s);
            Assert.IsNotNull(ask);
            Battles.Answer(s, ctx, ask, A(AnswerKind.Accept), log);
            Assert.IsTrue(CraftRules.AlluringReady(s, seat), "하나가 남는다");
            Assert.IsNull(AllureAsk(s), "같은 굴림에 다시 묻지 않는다");
            Battles.Drive(s, ctx, log);
            Assert.IsNull(s.Battle);
            Assert.IsFalse(CraftRules.AlluringReady(s, seat), "다음 야생 조우까지만 — 남은 것도 사라진다");
        }

        // ── 은행상회 ──

        [Test]
        public void A4_은행상회는맨위카드의한줄또는두줄_거래뒤덱을섞고턴당1번()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 43, log);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            Assert.IsTrue(SetupFlow.GruntAt(s, st.Cell), "조무래기가 있는 고향에서도 거래는 된다(p.21)");
            int card = BankRules.Card(s);
            var trades = ctx.Decks.Cards[card].Trades;
            Assert.AreEqual(9, s.Decks[(int)DeckId.GinkgoTrade].Count);
            Bag(st);
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Ginkgo), "낼 규토리가 없다");

            var give = new List<ApricornColor>(trades[0].Give);
            Bag(st, give.ToArray());
            var legal = Flow.MapLegal(s, ctx).FindAll(a => a.Kind == AnswerKind.Ginkgo);
            Assert.AreEqual(1, legal.Count);
            Assert.AreEqual(BankRules.Top, legal[0].Target);

            give.AddRange(trades[1].Give);
            Bag(st, give.ToArray());
            legal = Flow.MapLegal(s, ctx).FindAll(a => a.Kind == AnswerKind.Ginkgo);
            CollectionAssert.AreEqual(new[] { BankRules.Top, BankRules.Bottom, BankRules.Both }, legal.ConvertAll(a => a.Target), "한 줄 또는 두 줄(각 1번)");

            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Ginkgo, BankRules.Both), log);
            var want = new int[SeatState.ApricornColors];
            foreach (var t in trades)
                foreach (var c in t.Get) want[(int)c]++;
            CollectionAssert.AreEqual(want, st.Apricorns, "왼쪽을 내고 오른쪽을 받는다");
            var traded = Flow.Of<GinkgoTraded>(log);
            Assert.AreEqual(2, traded.Count);
            Assert.AreEqual(card, traded[0].CardKey);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, traded.ConvertAll(e => e.Row));
            Assert.AreEqual(DeckId.GinkgoTrade, Flow.Of<PileShuffled>(log)[0].Pile, "거래 뒤 덱을 섞는다");
            Assert.AreEqual(UsageKind.Ginkgo, Flow.Of<UsageMarked>(log)[0].Kind);
            Assert.AreEqual(ActionKind.Ginkgo, Flow.Of<ActionStarted>(log)[0].Kind);
            Assert.AreEqual(9, s.Decks[(int)DeckId.GinkgoTrade].Count);
            Assert.AreEqual(seat, Flow.Only(s, ChoiceKind.MapAction).Seat, "턴은 이어진다");

            for (int i = 0; i < st.Apricorns.Length; i++) st.Apricorns[i] = 5;
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Ginkgo), "턴당 1번");
            NextOwnTurn(s, ctx);
            Assert.IsTrue(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Ginkgo), "다음 내 턴에는 다시");

            // 한 줄만
            var one = Flow.Started(ctx, 2, 43);
            var st1 = one.Seats[one.ActiveSeat];
            Bag(st1, give.ToArray());
            Flow.Answer(one, ctx, ChoiceKind.MapAction, A(AnswerKind.Ginkgo, BankRules.Bottom));
            var left = new int[SeatState.ApricornColors];
            foreach (var c in trades[0].Give) left[(int)c]++;
            foreach (var c in trades[1].Get) left[(int)c]++;
            CollectionAssert.AreEqual(left, st1.Apricorns, "아래 줄만: 위 줄 재료는 그대로");
        }

        [Test]
        public void A4_두줄은한줄에서받은규토리로이어서할수있다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 47, log);
            var st = s.Seats[s.ActiveSeat];
            // 4번 카드: 분홍 → 빨강·파랑·노랑 / 검정 → 분홍·초록·하양
            int card = ctx.Decks.CardIds.IndexOf("ginkgo_trade.4");
            var deck = s.Decks[(int)DeckId.GinkgoTrade];
            deck.Remove(card);
            deck.Add(card);
            Bag(st, ApricornColor.Black);
            var legal = Flow.MapLegal(s, ctx).FindAll(a => a.Kind == AnswerKind.Ginkgo);
            CollectionAssert.AreEqual(new[] { BankRules.Bottom, BankRules.Both }, legal.ConvertAll(a => a.Target));
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Ginkgo, BankRules.Both), log);
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 0, 1, 1, 0 }, st.Apricorns, "빨강·파랑·노랑·초록·하양 1개씩(분홍은 다시 냈다)");
            CollectionAssert.AreEqual(new[] { 1, 0 }, Flow.Of<GinkgoTraded>(log).ConvertAll(e => e.Row), "아래 줄 먼저");
        }

        [Test]
        public void A4_마을밖에서는안되고_앞면야영지에서는된다()
        {
            var s = Flow.Started(ctx, 2, 53);
            var st = s.Seats[s.ActiveSeat];
            for (int i = 0; i < st.Apricorns.Length; i++) st.Apricorns[i] = 3;
            Assert.IsTrue(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Ginkgo));
            int outside = Flow.StepInto(s, ctx, st.Cell);
            st.Cell = outside;
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Ginkgo), "마을 밖");
            Assert.IsTrue(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Craft), "제작은 어디서나");
            var token = s.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && ctx.Items.Items[t.Def].Key == "hidden_spot.campsite");
            token.Cell = outside;
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Ginkgo), "뒷면 야영지");
            token.FaceUp = true;
            Assert.IsTrue(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Ginkgo), "앞면 야영지(p.16)");
        }

        [Test]
        public void A4_보기에서도맨위거래카드가보이고합법답이같다()
        {
            var s = Flow.Started(ctx, 2, 59);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            for (int i = 0; i < st.Apricorns.Length; i++) st.Apricorns[i] = 3;
            st.Jp = 20;
            foreach (int viewer in new[] { seat, 1 - seat, SeatId.Observer })
            {
                var v = Engine.ViewFor(s, viewer);
                Assert.AreEqual(BankRules.Card(s), BankRules.Card(v), "맨 위 거래 카드는 공개");
                var pile = v.Decks[(int)DeckId.GinkgoTrade];
                for (int i = 0; i < pile.Count - 1; i++) Assert.AreEqual(Engine.Unknown, pile[i], "나머지는 가린다");
            }
            var view = Engine.ViewFor(s, seat);
            var mine = Scene.Legal(s, ctx, Flow.Only(s, ChoiceKind.MapAction));
            var seen = Scene.Legal(view, ctx, Flow.Only(view, ChoiceKind.MapAction));
            Assert.IsTrue(Flow.Has(mine, AnswerKind.Ginkgo));
            Assert.IsTrue(Flow.Has(mine, AnswerKind.Evolve));
            Assert.IsTrue(Flow.Has(mine, AnswerKind.Craft));
            Assert.AreEqual(mine.Count, seen.Count);
            for (int i = 0; i < mine.Count; i++) Assert.IsTrue(mine[i].SameAs(seen[i]), $"{mine[i]} ≠ {seen[i]}");
        }

        // ── 진화 ──

        [Test]
        public void R14_진화대상은모두진화덱에있다()
        {
            var evo = new HashSet<int>();
            foreach (var e in ctx.Deck(DeckId.Evolution).Cards) evo.Add(e.Card);
            int n = 0, branched = 0;
            foreach (var p in ctx.Cards.Pokemon)
            {
                foreach (int to in p.EvolveInto)
                {
                    Assert.IsTrue(evo.Contains(to), $"{p.Key} → {ctx.Cards.Pokemon[to].Key}");
                    n++;
                }
                if (p.EvolveInto.Count > 1) branched++;
            }
            Assert.Greater(n, 100);
            Assert.AreEqual(6, branched, "분기 진화 6종(이브이 등)");
        }

        [Test]
        public void A9_진화는JP를내고완전회복_지닌물건과배운기술은그대로()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 61, log);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int p = st.Party[0];
            var pk = s.Pokemon[p];
            int from = pk.Def;
            var def = ctx.Cards.Pokemon[from];
            int cost = def.EvolveCost.Value;
            int to = def.EvolveInto[0];
            int oldDeck = pk.Deck;
            st.Jp = cost - 1;
            Assert.IsFalse(Flow.Has(Flow.MapLegal(s, ctx), AnswerKind.Evolve), "JP가 모자라다");
            st.Jp = cost + 1;
            pk.Damage = PokemonRules.MaxHp(ctx, pk); // 기절한 포켓몬도 진화가 회복시킨다(A9 — 회복은 전제 조건이 아니라 첫 단계)
            pk.Status = StatusEffect.Burned;
            pk.HeldItem = Item("item.lucky_egg");
            pk.Moves[0] = Scene.Move(ctx, "tackle");
            pk.Ball = (int)Ball.Greater;
            int cards = Playout.CardCount(s);
            Assert.IsTrue(ChoiceRules.Contains(Flow.MapLegal(s, ctx), A(AnswerKind.Evolve, p)));
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Evolve, p), log);

            Assert.AreEqual(to, pk.Def);
            Assert.AreEqual(1, st.Jp);
            Assert.AreEqual(0, pk.Damage);
            Assert.AreEqual(StatusEffect.None, pk.Status);
            Assert.AreEqual(Item("item.lucky_egg"), pk.HeldItem, "지닌물건 유지");
            Assert.AreEqual(Scene.Move(ctx, "tackle"), pk.Moves[0], "배운 기술 유지");
            Assert.AreEqual((int)Ball.Greater, pk.Ball);
            Assert.AreEqual(p, st.Party[0], "같은 파티 칸");
            Assert.AreEqual((int)DeckId.Evolution, pk.Deck);
            Assert.IsFalse(s.Decks[(int)DeckId.Evolution].Contains(to), "진화 카드를 덱에서 꺼냈다(1장)");
            CollectionAssert.AreEqual(new[] { oldDeck, from, def.Signature }, pk.Under, "옛 기술을 가르칠지 묻는 동안 옛 카드는 새 카드 밑에(p.5)");
            CollectionAssert.DoesNotContain(s.Discards[oldDeck], from);
            Assert.AreEqual(cards, Playout.CardCount(s), "카드 총수 보존");

            Assert.AreEqual(ActionKind.Evolve, Flow.Of<ActionStarted>(log)[0].Kind);
            var ev = Flow.Of<Evolved>(log)[0];
            Assert.AreEqual(p, ev.PokemonId);
            Assert.AreEqual(from, ev.From);
            Assert.AreEqual(to, ev.To);
            Assert.AreEqual(cost, ev.JpCost);
            Assert.IsFalse(ev.Halved);
            Assert.AreEqual(cost + 1, ev.Changes[0].Before);
            Assert.AreEqual(1, ev.Changes[0].After);
            Assert.AreEqual(ResKind.Jp, ev.Changes[0].Res);
            Assert.AreEqual(0, ev.Pokemon[0].HpBefore);
            Assert.AreEqual(ctx.Cards.Pokemon[to].Health, ev.Pokemon[0].HpAfter);
            Assert.AreEqual(StatusEffect.Burned, ev.Pokemon[0].StatusBefore);

            // 옛 기술 가르치기를 묻고(기본 = 안 가르침), 끝나면 턴이 이어진다
            var teach = Flow.Only(s, ChoiceKind.Reward);
            Assert.AreEqual(ChoiceSub.EvolveTeach, teach.Sub);
            Assert.AreEqual(def.Signature, teach.Subject);
            Assert.AreEqual(AnswerKind.Decline, teach.Default.Kind);
            Flow.Answer(s, ctx, ChoiceKind.Reward, A(AnswerKind.Decline));
            Assert.AreEqual(-1, pk.Moves[1]);
            CollectionAssert.Contains(s.Discards[oldDeck], from, "안 가르쳤으면 옛 카드는 버림 더미로(p.5 「Otherwise discard the old card」)");
            Assert.AreEqual(0, pk.Under.Count);
            Assert.AreEqual(cards, Playout.CardCount(s));
            Assert.AreEqual(seat, Flow.Only(s, ChoiceKind.MapAction).Seat, "진화는 턴을 끝내지 않는다");
        }

        [Test]
        public void S9_1_진화하면옛포켓몬의기술을타입과상관없이가르칠수있다()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 67, log);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int p = Scene.Give(s, ctx, seat, "415:0"); // 세꿀버리 → 비퀸: 달콤한향기(노말)는 비퀸이 배울 수 없는 타입
            var pk = s.Pokemon[p];
            int old = ctx.Cards.Pokemon[pk.Def].Signature;
            st.Jp = 9;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Evolve, p), log);
            Assert.IsFalse(ChoiceRules.CanLearn(ctx, pk, ctx.Cards.Moves[old].Type), "배울 수 있는 타입이 아니다");
            var c = Flow.Only(s, ChoiceKind.Reward);
            var legal = Scene.Legal(s, ctx, c);
            Assert.AreEqual(3, legal.Count);
            Assert.AreEqual(AnswerKind.Decline, legal[0].Kind);
            Assert.IsTrue(legal[1].SameAs(ChoiceAnswer.Pick(p, 0)));
            Assert.IsTrue(legal[2].SameAs(ChoiceAnswer.Pick(p, 1)));
            pk.Moves[1] = Scene.Move(ctx, "tackle");
            log.Clear();
            Flow.Answer(s, ctx, ChoiceKind.Reward, ChoiceAnswer.Pick(p, 1), log);
            Assert.AreEqual(old, pk.Moves[1]);
            var taught = Flow.Of<MoveTaught>(log)[0];
            Assert.AreEqual(p, taught.PokemonId);
            Assert.AreEqual(1, taught.Slot);
            Assert.AreEqual(old, taught.MoveKey);
            Assert.AreEqual(Scene.Move(ctx, "tackle"), taught.ReplacedKey);
            Flow.Only(s, ChoiceKind.MapAction);
            // 룰북 p.5 「by flipping the old card over and placing it under the new card. Otherwise discard the old card.」: 가르쳤으면 옛 카드는 새 카드 밑에 남는다
            int oldCard = Scene.Def(ctx, "415:0");
            Assert.AreEqual(3, pk.Under.Count);
            Assert.AreEqual(oldCard, pk.Under[1]);
            int deck = pk.Under[0];
            CollectionAssert.DoesNotContain(s.Discards[deck], oldCard);
            // 그 기술을 바꾸면 밑의 옛 카드를 버린다
            Scene.Accept(Engine.Submit(s, ctx, seat, Command.Anytime(new Maintenance { Kind = MaintKind.SetMove, Pokemon = p, Slot = 1, Value = -1 }), null));
            Assert.AreEqual(0, pk.Under.Count);
            CollectionAssert.Contains(s.Discards[deck], oldCard);
        }

        [Test]
        public void R14_진화분기는고른다_기본은evolve_into첫째_JP는고른뒤에()
        {
            var log = new EventLog();
            var s = Flow.Started(ctx, 2, 71, log);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int eevee = Scene.Give(s, ctx, seat, "133:0");
            var pk = s.Pokemon[eevee];
            var branches = ctx.Cards.Pokemon[pk.Def].EvolveInto;
            Assert.AreEqual(8, branches.Count);
            st.Jp = 8;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Evolve, eevee), log);
            var c = Flow.Only(s, ChoiceKind.Reward);
            Assert.AreEqual(ChoiceSub.EvolveBranch, c.Sub);
            Assert.AreEqual(eevee, c.Subject);
            CollectionAssert.AreEqual(branches, c.Options);
            Assert.IsTrue(c.Default.SameAs(ChoiceAnswer.Pick(branches[0])), "기본 = evolve_into 첫째(G9)");
            Assert.AreEqual(8, st.Jp, "아직 내지 않았다");
            Assert.AreEqual(Scene.Def(ctx, "133:0"), pk.Def);
            Flow.Answer(s, ctx, ChoiceKind.Reward, ChoiceAnswer.Pick(branches[2]));
            Assert.AreEqual(branches[2], pk.Def);
            Assert.AreEqual(0, st.Jp);
            var deck = s.Decks[(int)DeckId.Evolution];
            Assert.IsFalse(deck.Contains(branches[2]));
            Assert.IsTrue(deck.Contains(branches[0]) && deck.Contains(branches[7]), "고르지 않은 분기는 덱에 남는다");

            // 다른 이브이도 이미 나간 분기를 고를 수 있다(카드를 복제 — 원작 커뮤니티 확인)
            int second = Scene.Give(s, ctx, seat, "133:0");
            var left = new List<int>();
            EvolveRules.Branches(s, ctx, second, left);
            Assert.AreEqual(8, left.Count);
            Assert.AreEqual(0, EvolveRules.Available(s, ctx, branches[2]));
        }

        [Test]
        public void R14_분기를묻는사이놓아주면진화하지않고JP도그대로()
        {
            var s = Flow.Started(ctx, 2, 73);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            int eevee = Scene.Give(s, ctx, seat, "133:0");
            st.Jp = 8;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Evolve, eevee));
            var c = Flow.Only(s, ChoiceKind.Reward);
            Scene.Accept(Engine.Submit(s, ctx, seat, Command.Anytime(new Maintenance { Kind = MaintKind.Release, Pokemon = eevee, Value = s.Pokemon[eevee].Def }), null));
            c = s.Pending.Find(x => x.Kind == ChoiceKind.Reward);
            Scene.Accept(Engine.Submit(s, ctx, seat, Command.AnswerTo(c.Id, c.Default.Clone()), null));
            Assert.AreEqual(8, st.Jp);
            Assert.AreEqual(seat, Flow.Only(s, ChoiceKind.MapAction).Seat);
        }

        [Test]
        // 원작 커뮤니티 확인 진화 카드가 이미 나가 덱에 없을 때 복제해도 되나 → 원작 커뮤니티 확인(복제가 필요한 특수한 경우)
        public void R14_진화카드가덱에없어도복제해진화한다_덱밖카드라버림더미로가지않는다()
        {
            var s = Flow.Started(ctx, 2, 79);
            int seat = s.ActiveSeat;
            var st = s.Seats[seat];
            st.OpenSlots = 6;
            int a = Scene.Give(s, ctx, seat, "415:0");
            int b = Scene.Give(s, ctx, seat, "415:0");
            int to = ctx.Cards.Pokemon[s.Pokemon[a].Def].EvolveInto[0];
            Assert.AreEqual(1, EvolveRules.Available(s, ctx, to), "비퀸 진화 카드 1장");
            st.Jp = 20;
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Evolve, a));
            Flow.Answer(s, ctx, ChoiceKind.Reward, A(AnswerKind.Decline));
            Assert.AreEqual(0, EvolveRules.Available(s, ctx, to));
            // 보기 사본에서도 같은 값(덱 내용이 아니라 공개 정보로 센다)
            Assert.AreEqual(0, EvolveRules.Available(Engine.ViewFor(s, 1 - seat), ctx, to));
            Assert.IsTrue(ChoiceRules.Contains(Flow.MapLegal(s, ctx), A(AnswerKind.Evolve, b)), "남은 진화 카드가 없어도 된다");
            int cards = Playout.CardCount(s);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Evolve, b));
            Flow.Answer(s, ctx, ChoiceKind.Reward, A(AnswerKind.Decline));
            Assert.AreEqual(to, s.Pokemon[b].Def);
            Assert.AreEqual(-1, s.Pokemon[b].Deck, "복제한 카드는 덱 밖");
            Assert.AreEqual(cards, Playout.CardCount(s), "카드 총수 보존(복제한 카드는 세지 않는다)");
            int discards = s.Discards[(int)DeckId.Evolution].Count;
            MaintenanceRules.Release(s, ctx, Scene.Fx(s, null, seat), seat, b);
            Assert.AreEqual(discards, s.Discards[(int)DeckId.Evolution].Count, "놓아줘도 버림 더미에 가지 않는다");
        }

        // ── 결단의 머리띠 ──

        GameState BandScene(ulong seed, EventLog log, bool band, out int mine, out int sig, out int other)
        {
            var s = Battles.Game(ctx, 2, seed);
            mine = Scene.Give(s, ctx, 0, "389:1");
            var pk = s.Pokemon[mine];
            sig = ctx.Cards.Pokemon[pk.Def].Signature;
            other = Scene.Move(ctx, "tackle");
            pk.Moves[0] = other;
            if (band) pk.HeldItem = Item(HeldItems.ChoiceBand);
            int wild = Scene.Spawn(s, ctx, "389:1", SeatId.Wild);
            Battles.Wild(s, ctx, log, 0, wild);
            return s;
        }

        [Test]
        public void M5_제작_결단의머리띠는공격력1더하고나갈때까지고른기술만()
        {
            var log = new EventLog();
            var plain = BandScene(7, log, false, out int m0, out int sig, out int other);
            Assert.IsTrue(MyAction(plain, log, 0));
            int basePower = BattleRules.RollPower(plain, ctx, other, m0, plain.Battle.Sides[1].Active, new List<Mod>());

            // 첫 공격 뒤에도 배틀이 이어지는 시드
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                var t = BandScene(sd, l, true, out _, out _, out int o);
                if (!MyAction(t, l, 0)) return false;
                Battles.Answer(t, ctx, ChoiceKind.BattleAction, Battles.Move(o), l);
                return MyAction(t, l, 0);
            }, 200);
            log.Clear();
            var s = BandScene(seed, log, true, out int mine, out sig, out other);
            Assert.IsTrue(MyAction(s, log, 0));
            var pk = s.Pokemon[mine];
            var mods = new List<Mod>();
            Assert.AreEqual(basePower + 1, BattleRules.RollPower(s, ctx, other, mine, s.Battle.Sides[1].Active, mods), "공격력 +1");
            Assert.IsTrue(mods.Exists(m => m.Source.Kind == SourceKind.Item && m.Source.Id == pk.HeldItem && m.Delta == 1));
            var first = Scene.Legal(s, ctx, Battles.Only(s, ChoiceKind.BattleAction)).FindAll(a => a.Kind == AnswerKind.UseMove);
            Assert.AreEqual(2, first.Count, "처음에는 아무 기술이나");
            Assert.AreEqual(-1, HeldItems.LockedMove(ctx, pk));

            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(other), log);
            Assert.AreEqual(other, HeldItems.LockedMove(ctx, pk));
            var rolled = log.OfType<DiceRolled>().Find(d => d.PokemonId == mine);
            Assert.AreEqual(basePower + 1, rolled.Power);
            Assert.IsTrue(MyAction(s, log, 0));
            var c = Battles.Only(s, ChoiceKind.BattleAction);
            var next = Scene.Legal(s, ctx, c).FindAll(a => a.Kind == AnswerKind.UseMove);
            Assert.AreEqual(1, next.Count, "나갈 때까지 그 기술만");
            Assert.AreEqual(other, next[0].Target);
            Assert.IsTrue(c.Default.SameAs(Battles.Move(other)), "시간 초과 기본값도 묶인 기술");

            // 배틀이 끝나면(나가면) 풀린다
            Battles.Drive(s, ctx, log);
            Assert.IsNull(s.Battle);
            Assert.AreEqual(-1, HeldItems.LockedMove(ctx, pk));
            Assert.AreEqual(0, pk.Mods.Count);
        }

        // ── 행운의 알 · 진화의 휘석 · 별 방패 · 폭발수정 ──

        /// <summary>야생 배틀 장면: 좌석 0의 포켓몬(지닌물건 item, 없으면 null) 대 야생.</summary>
        GameState ItemScene(ulong seed, EventLog log, string mineKey, string item, string wildKey, out int mine, out int wild)
        {
            var s = Battles.Game(ctx, 2, seed);
            mine = Scene.Give(s, ctx, 0, mineKey);
            if (item != null) s.Pokemon[mine].HeldItem = Item(item);
            wild = Scene.Spawn(s, ctx, wildKey, SeatId.Wild);
            Battles.Wild(s, ctx, log, 0, wild);
            return s;
        }

        /// <summary>좌석 0이 시그니처 기술을 한 번 쓴다(차례가 오지 않으면 false). 그 전에 야생의 체력을 가득 채워 둔다(장면 — 배틀이 이어지게).</summary>
        bool Attack(GameState s, EventLog log, int mine, int wild)
        {
            if (!MyAction(s, log, 0)) return false;
            s.Pokemon[wild].Damage = -100;
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(ctx.Cards.Pokemon[s.Pokemon[mine].Def].Signature), log);
            return true;
        }

        static int Count(AttackDieFace[] faces, AttackDieFace face) => System.Array.FindAll(faces, f => f == face).Length;

        static List<DiceFacesChanged> ItemChanges(EventLog log, int item) =>
            log.OfType<DiceFacesChanged>().FindAll(c => c.Source.Kind == SourceKind.Item && c.Source.Id == item);

        static DamageBreakdown Hit(EventLog log, int target, int nth = 0) => log.OfType<DamageBreakdown>().FindAll(d => d.TargetId == target)[nth];

        [Test]
        public void M5_제작_행운의알은공격할때심볼2개를크리티컬1개로_2개가안되면그대로()
        {
            int egg = Item(HeldItems.LuckyEgg);
            foreach (bool two in new[] { true, false })
            {
                ulong seed = Battles.Seed(sd =>
                {
                    var l = new EventLog();
                    var t = ItemScene(sd, l, "389:1", null, "389:1", out int m, out int w);
                    if (!Attack(t, l, m, w)) return false;
                    var d = l.OfType<DiceRolled>().Find(x => x.PokemonId == m);
                    return d != null && (two ? Count(d.Faces, AttackDieFace.StatusSymbol) >= 2 : Count(d.Faces, AttackDieFace.StatusSymbol) == 1);
                });
                var log = new EventLog();
                var s = ItemScene(seed, log, "389:1", HeldItems.LuckyEgg, "389:1", out int mine, out int wild);
                Assert.IsTrue(Attack(s, log, mine, wild));
                var rolled = log.OfType<DiceRolled>().Find(x => x.PokemonId == mine);
                var changes = ItemChanges(log, egg);
                int dice = System.Array.Find(Hit(log, wild).Steps, st => st.Kind == DamageStepKind.Dice).Value;
                if (!two)
                {
                    Assert.AreEqual(0, changes.Count, "심볼이 1개면 바꾸지 않는다(「if possible」)");
                    Assert.AreEqual(AttackDice.Sum(rolled.Faces), dice);
                    continue;
                }
                var all = new List<FaceChange>();
                foreach (var c in changes) all.AddRange(c.Changes);
                Assert.AreEqual(2, all.Count, "심볼 2개가 바뀐다");
                foreach (var c in all) Assert.AreEqual(AttackDieFace.StatusSymbol, c.Before);
                Assert.AreEqual(1, all.FindAll(c => c.After == AttackDieFace.CriticalHit).Count, "하나는 크리티컬");
                Assert.AreEqual(1, all.FindAll(c => c.After == AttackDieFace.None).Count, "하나는 사라진다");
                Assert.AreEqual(AttackDice.Sum(rolled.Faces) + 2, dice, "크리티컬 1개만큼 주사위 합이 는다");
            }
        }

        [Test]
        public void M5_제작_진화의휘석은받는일반타격2개를지운다_다자란강한포켓몬에게는안듣는다()
        {
            int stone = Item(HeldItems.Eviolite);
            Assert.IsTrue(HeldItems.EvioliteWorks(ctx, new PokemonState { Def = Scene.Def(ctx, "415:0") }), "약한 등급");
            Assert.IsTrue(HeldItems.EvioliteWorks(ctx, new PokemonState { Def = Scene.Def(ctx, "388:0") }), "진화가 남았다");
            Assert.IsFalse(HeldItems.EvioliteWorks(ctx, new PokemonState { Def = Scene.Def(ctx, "389:1") }), "강한 등급 · 다 진화");

            // 야생(토대부기, 주사위 5)이 일반 타격 2개 이상을 굴리는 판
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                var t = ItemScene(sd, l, "415:0", null, "389:1", out int m, out int w);
                Attack(t, l, m, w);
                MyAction(t, l, 0);
                var d = l.OfType<DiceRolled>().Find(x => x.PokemonId == w);
                return d != null && Count(d.Faces, AttackDieFace.RegularHit) >= 2;
            });
            var log = new EventLog();
            var s = ItemScene(seed, log, "415:0", HeldItems.Eviolite, "389:1", out int mine, out int wild);
            Attack(s, log, mine, wild);
            MyAction(s, log, 0);
            var rolled = log.OfType<DiceRolled>().Find(x => x.PokemonId == wild);
            var changes = ItemChanges(log, stone);
            Assert.GreaterOrEqual(changes.Count, 1, "공격받을 때마다(첫 굴림을 본다)");
            Assert.AreEqual(2, changes[0].Changes.Length, "일반 타격 2개");
            foreach (var c in changes[0].Changes)
            {
                Assert.AreEqual(AttackDieFace.RegularHit, c.Before);
                Assert.AreEqual(AttackDieFace.None, c.After);
            }
            int dice = System.Array.Find(Hit(log, mine).Steps, st => st.Kind == DamageStepKind.Dice).Value;
            Assert.AreEqual(AttackDice.Sum(rolled.Faces) - 2, dice);

            // 다 자란 강한 포켓몬이 들면 효과가 없다
            log.Clear();
            s = ItemScene(seed, log, "389:1", HeldItems.Eviolite, "389:1", out mine, out wild);
            Attack(s, log, mine, wild);
            MyAction(s, log, 0);
            Assert.AreEqual(0, ItemChanges(log, stone).Count);
        }

        [Test]
        public void M5_제작_별방패는첫공격에만2덜받고그턴상태이상을막는다()
        {
            int shield = Item(HeldItems.StarShield);
            var log = new EventLog();
            // 야생 코일의 전기쇼크는 늘 마비를 건다
            var s = ItemScene(11, log, "389:1", HeldItems.StarShield, "81:0", out int mine, out int wild);
            for (int i = 0; i < 4 && log.OfType<DamageBreakdown>().FindAll(d => d.TargetId == mine).Count < 2; i++)
            {
                Assert.IsTrue(Attack(s, log, mine, wild), "배틀이 이어진다");
                s.Pokemon[mine].Damage = 0;
            }
            MyAction(s, log, 0);
            var first = Hit(log, mine, 0);
            var cut = System.Array.FindAll(first.Steps, st => st.Kind == DamageStepKind.Reduction && st.Source.Kind == SourceKind.Item && st.Source.Id == shield);
            Assert.AreEqual(1, cut.Length);
            Assert.AreEqual(-2, cut[0].Value, "2 덜 받는다");
            int before = 0;
            foreach (var st in first.Steps)
                if (st.Kind != DamageStepKind.Reduction) before += st.Value;
            Assert.AreEqual(System.Math.Max(0, before - 2), first.Final);
            var second = Hit(log, mine, 1);
            Assert.IsFalse(System.Array.Exists(second.Steps, st => st.Kind == DamageStepKind.Reduction), "둘째 공격부터는 그대로");

            var statuses = log.OfType<StatusApplied>().FindAll(e => e.PokemonId == mine);
            Assert.AreEqual(StatusResult.Immune, statuses[0].Result, "첫 공격의 그 턴에 걸리는 상태이상을 막는다");
            Assert.AreEqual(StatusResult.Applied, statuses[1].Result, "그 뒤에는 걸린다");
        }

        /// <summary>트레이너전: 좌석 0(토대부기 + 비버니)이 좌석 1의 체력 1 세꿀버리(지닌물건 item) + 비버니를 친다. 좌석 0의 첫 공격까지.</summary>
        GameState BlastScene(ulong seed, EventLog log, string item, out int attacker, out int holder)
        {
            var s = Battles.Game(ctx, 2, seed);
            attacker = Scene.Give(s, ctx, 0, "389:1");
            Scene.Give(s, ctx, 0, "399:0");
            holder = Scene.Give(s, ctx, 1, "415:0");
            Scene.Give(s, ctx, 1, "399:0");
            if (item != null) s.Pokemon[holder].HeldItem = Item(item);
            Battles.SetHp(s, ctx, holder, 1);
            s.Pokemon[attacker].Moves[0] = Scene.Move(ctx, "tackle");
            Battles.Start(s, ctx, log, BattleKind.Trainer, 0, 1, -1);
            if (MyAction(s, log, 0)) Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Scene.Move(ctx, "tackle")), log);
            return s;
        }

        [Test]
        public void M5_제작_폭발수정은첫공격에공격자에게1피해_이번턴교체를막는다()
        {
            int crystal = Item(HeldItems.BlastCrystal);
            // 아이템 없이: 세꿀버리가 쓰러지고 좌석 0에 교체 창이 열리는 판
            ulong seed = Battles.Seed(sd =>
            {
                var t = BlastScene(sd, new EventLog(), null, out _, out _);
                return t.Battle != null && t.Pending.Count == 1 && t.Pending[0].Kind == ChoiceKind.SwitchWindow && t.Pending[0].Seat == 0;
            }, 200);
            var log = new EventLog();
            var s = BlastScene(seed, log, HeldItems.BlastCrystal, out int attacker, out int holder);
            var hurt = log.OfType<HpChanged>().Find(h => h.PokemonId == attacker && h.Source.Kind == SourceKind.Item);
            Assert.IsNotNull(hurt, "공격자가 피해를 받는다");
            Assert.AreEqual(crystal, hurt.Source.Id);
            Assert.AreEqual(1, hurt.Before - hurt.After);
            Assert.IsTrue(PokemonRules.IsFainted(ctx, s.Pokemon[holder]), "공격은 그대로 들어간다(동시 적용)");
            var locked = log.OfType<SwitchLocked>();
            Assert.AreEqual(1, locked.Count);
            Assert.AreEqual(SwitchLockReason.BlastCrystal, locked[0].Reason);
            Assert.AreEqual(0, locked[0].Side);
            Assert.IsFalse(s.Pending.Exists(c => c.Kind == ChoiceKind.SwitchWindow), "교체 창이 열리지 않는다");
            Assert.IsNotNull(s.Battle, "배틀은 이어진다");
        }

        // ── 결정성 ──

        ulong Script(ulong seed, GameState fork = null)
        {
            var s = fork ?? Flow.Started(ctx, 2, seed);
            var st = s.Seats[s.ActiveSeat];
            if (fork == null)
            {
                for (int i = 0; i < st.Apricorns.Length; i++) st.Apricorns[i] = 4;
                st.Jp = 10;
                Craft(s, ctx, Item("item.poke_ball"));
                Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Ginkgo, BankRules.Both));
            }
            Craft(s, ctx, Item(CraftRules.ZestyStew));
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Evolve, st.Party[0]));
            Flow.Answer(s, ctx, ChoiceKind.Reward, ChoiceAnswer.Pick(st.Party[0], 0));
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.EndTurn));
            return StateHash.Of(s);
        }

        [Test]
        public void M5_제작은행진화는같은명령이면같은해시_복제에서도()
        {
            Assert.AreEqual(Script(83), Script(83), "같은 시드 · 같은 명령 = 같은 상태");
            Assert.AreNotEqual(Script(83), Script(84));
            // 가운데(은행상회 거래 뒤 — 덱을 섞은 Rng가 상태에 있다)에서 복제해 이어 가도 같다
            var s = Flow.Started(ctx, 2, 83);
            var st = s.Seats[s.ActiveSeat];
            for (int i = 0; i < st.Apricorns.Length; i++) st.Apricorns[i] = 4;
            st.Jp = 10;
            Craft(s, ctx, Item("item.poke_ball"));
            Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Ginkgo, BankRules.Both));
            Assert.AreEqual(Script(83), Script(83, s.Clone()));
        }
    }
}
