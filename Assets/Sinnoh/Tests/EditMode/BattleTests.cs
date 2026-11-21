using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M3-e 배틀 상태 기계(01 §6, rulings G5·G13·B5): 출전·우선권 토큰·순서, 공격(주사위·피로·상성·피해 내역), 기절·교체·다음 포켓몬,
    /// 야생(도망·포획·기권·보상), 트레이너 도전(G7), 1막 조무래기 배틀(A1), 은하의 응징(A13·G5), 갤럭시단 교체 금지(A16).
    /// 포켓몬: 모부기 387(풀, 체력 6, 우선도 4, 잎날가르기 3) · 불꽃숭이 390(불꽃, 4, 6, 불꽃세례 2) · 팽도리 393(물, 5, 5) · 찌르꼬 396(노말·비행, 4, 6) ·
    /// 비버니 399(노말, 5, 4, 몸통박치기 2) · 조무래기 주뱃 41(독·비행, 우선도 5, 독엄니 2) · 조무래기 동미러 436(에스퍼·강철, 체력 9, 우선도 2).
    /// </summary>
    public class BattleTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        int Give(GameState s, int seat, string key) => Scene.Give(s, ctx, seat, key);

        int Wild(GameState s, string key) => Scene.Spawn(s, ctx, key, SeatId.Wild);

        int Grunt(GameState s, string key) => Scene.Spawn(s, ctx, key, SeatId.Galactic);

        int Mv(string key) => Scene.Move(ctx, key);

        static T Last<T>(EventLog log) where T : GameEvent
        {
            var l = log.OfType<T>();
            return l.Count > 0 ? l[l.Count - 1] : null;
        }

        // ── 출전·순서 ──

        [Test]
        public void S7_1_도전자가먼저내고상대가낸뒤도전자가토큰()
        {
            var s = Battles.Game(ctx, 2);
            int cell = Flow.StepInto(s, ctx, s.Seats[0].Cell); // 마을 밖
            s.Seats[0].Cell = cell;
            s.Seats[1].Cell = cell;
            s.Seats[0].MovesLeft = 3;
            Give(s, 0, "387:0");
            int a2 = Give(s, 0, "399:0");
            Give(s, 1, "393:0");
            int b2 = Give(s, 1, "396:0");
            var log = new EventLog();
            var map = Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = 0 });
            Assert.IsTrue(Scene.Legal(s, ctx, map).Exists(x => x.Kind == AnswerKind.Challenge && x.Target == 1), "같은 칸의 트레이너(G7)");
            Battles.Answer(s, ctx, map, ChoiceAnswer.Of(AnswerKind.Challenge, 1), log);

            Assert.IsFalse(log.OfType<ChallengeIssued>()[0].CanRefuse, "마을 밖 도전은 거절할 수 없다(p.3)");
            Assert.IsTrue(log.OfType<ChallengeAnswered>()[0].Accept);
            var send0 = Battles.Only(s, ChoiceKind.SendIn);
            Assert.AreEqual(0, send0.Seat, "도전자가 먼저 낸다(S7_1)");
            Assert.AreEqual(0, log.OfType<InitiativeTokenAt>().Count);
            Battles.Answer(s, ctx, send0, ChoiceAnswer.Pick(a2), log);
            var send1 = Battles.Only(s, ChoiceKind.SendIn);
            Assert.AreEqual(1, send1.Seat, "상대는 도전자가 낸 포켓몬을 보고 낸다");
            Battles.Answer(s, ctx, send1, ChoiceAnswer.Pick(b2), log);

            var sent = log.OfType<SentIn>();
            Assert.AreEqual(0, sent[0].Side);
            Assert.AreEqual(a2, sent[0].PokemonId);
            Assert.AreEqual(1, sent[1].Side);
            var token = log.OfType<InitiativeTokenAt>();
            Assert.AreEqual(1, token.Count);
            Assert.AreEqual(0, token[0].Side, "상대가 낸 뒤 도전자가 우선권 토큰");
            Assert.IsTrue(s.Battle.Sides[0].InitiativeToken);
            var order = log.OfType<OrderDecided>()[0];
            Assert.AreEqual(1, order.First, "찌르꼬 6 > 비버니 4");
            Assert.AreEqual(1, Battles.Only(s, ChoiceKind.BattleAction).Seat);
        }

        [Test]
        public void S7_1_우선도가같으면토큰보유자가고른다()
        {
            foreach (bool use in new[] { true, false })
            {
                var s = Battles.Game(ctx, 2);
                Give(s, 0, "387:0");
                var log = new EventLog();
                Battles.Wild(s, ctx, log, 0, Wild(s, "399:0"));
                var c = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.InitiativeToken);
                Assert.AreEqual(0, c.Seat, "토큰 보유자(도전자)가 고른다");
                Assert.AreEqual(AnswerKind.Decline, c.Default.Kind);
                var shown = log.OfType<OrderDecided>()[0];
                Assert.AreEqual(-1, shown.First, "묻기 전에 비교를 보인다");
                Assert.AreEqual(4, shown.Sides[0].Final);
                Assert.AreEqual(4, shown.Sides[1].Final);
                Battles.Answer(s, ctx, c, ChoiceAnswer.Of(use ? AnswerKind.Accept : AnswerKind.Decline), log);
                var order = log.OfType<OrderDecided>()[1];
                Assert.AreEqual(use, order.TokenUsed);
                Assert.AreEqual(use ? 0 : 1, order.First, use ? "쓰면 선턴" : "안 쓰면 토큰이 없는 쪽이 선턴");
                Assert.AreEqual(use ? 1 : 0, order.TokenTo, use ? "토큰은 상대에게" : "토큰은 그대로");
                Assert.AreEqual(ActionKind.Reaction, log.OfType<ActionStarted>()[1].Kind);
            }
        }

        [Test]
        public void B5_보정후우선도가같으면토큰을묻는다()
        {
            // 모부기 4 + 민첩의 띠 2 = 6 대 야생 불꽃숭이 6 → 동률(InitiativeTie 기본 = 보정 후)
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "387:0");
            int sash = Scene.Item(ctx, "item.swiftness_sash");
            s.Pokemon[mine].HeldItem = sash;
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, Wild(s, "390:0"));
            Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.InitiativeToken);
            var order = log.OfType<OrderDecided>()[0];
            Assert.AreEqual(4, order.Sides[0].Base);
            Assert.AreEqual(1, order.Sides[0].Mods.Length);
            Assert.IsTrue(order.Sides[0].Mods[0].Source.Same(new SourceRef(SourceKind.Item, sash)), "출처 = 민첩의 띠(01 §3-6 ④)");
            Assert.AreEqual(2, order.Sides[0].Mods[0].Delta);
            Assert.AreEqual(6, order.Sides[0].Final);
            Assert.AreEqual(6, order.Sides[1].Final);

            // 마비 −3: 보정 후면 불꽃숭이 3 < 모부기 4
            var t = Battles.Game(ctx, 2);
            Give(t, 0, "387:0");
            int foe = Wild(t, "390:0");
            t.Pokemon[foe].Status = StatusEffect.Paralysed;
            var tlog = new EventLog();
            Battles.Wild(t, ctx, tlog, 0, foe);
            var o2 = tlog.OfType<OrderDecided>()[0];
            Assert.AreEqual(3, o2.Sides[1].Final);
            Assert.AreEqual(0, o2.First);
        }

        [Test]
        public void R11_기본우선도면보정을무시하고비교()
        {
            var opt = new RuleOptions { InitiativeTie = InitiativeTie.BasePriority };
            var s = Battles.Game(ctx, 2, 7, opt);
            int mine = Give(s, 0, "387:0");
            s.Pokemon[mine].HeldItem = Scene.Item(ctx, "item.swiftness_sash");
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, Wild(s, "390:0"));
            Assert.AreEqual(0, s.Pending.FindAll(c => c.Sub == ChoiceSub.InitiativeToken).Count, "기본값 4 ≠ 6이라 토큰을 묻지 않는다(Lua R11)");
            var order = log.OfType<OrderDecided>()[0];
            Assert.AreEqual(0, order.Sides[0].Mods.Length, "보정을 훑지 않는다");
            Assert.AreEqual(4, order.Sides[0].Final);
            Assert.AreEqual(1, order.First);

            var t = Battles.Game(ctx, 2, 7, opt);
            Give(t, 0, "387:0");
            int foe = Wild(t, "390:0");
            t.Pokemon[foe].Status = StatusEffect.Paralysed;
            var tlog = new EventLog();
            Battles.Wild(t, ctx, tlog, 0, foe);
            Assert.AreEqual(1, tlog.OfType<OrderDecided>()[0].First, "마비도 무시: 6 > 4");
        }

        // ── 공격·기절·교체 ──

        /// <summary>트레이너전: 좌석 0 모부기(민첩의 띠 → 6, 피로·체력은 인자) + 뒤 포켓몬, 좌석 1 팽도리(체력 1) + 뒤 포켓몬. 좌석 0이 잎날가르기를 쓸 때까지.</summary>
        GameState Exchange(EventLog log, int attackerHp, int fatigue, string[] bench0, string[] bench1, out int a, out int b)
        {
            var s = Battles.Game(ctx, 2);
            a = Give(s, 0, "387:0");
            foreach (var k in bench0) Give(s, 0, k);
            b = Give(s, 1, "393:0");
            foreach (var k in bench1) Give(s, 1, k);
            s.Pokemon[a].HeldItem = Scene.Item(ctx, "item.swiftness_sash");
            s.Pokemon[a].Fatigue = fatigue;
            Battles.SetHp(s, ctx, a, attackerHp);
            Battles.SetHp(s, ctx, b, 1);
            BattleFlow.StartTrainer(s, ctx, Scene.Fx(s, log, 0, ActionKind.Challenge), 0, 1);
            if (bench0.Length > 0) Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(a), log);
            if (bench1.Length > 0) Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(b), log);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("razor_leaf")), log);
            return s;
        }

        [Test]
        public void S7_3_피해내역은주사위합과상성과화상()
        {
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "390:0");
            s.Pokemon[mine].Status = StatusEffect.Burned;
            int foe = Wild(s, "387:0");
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, foe);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);

            var sel = log.OfType<MoveSelected>()[0];
            Assert.AreEqual(Mv("ember"), sel.MoveKey);
            Assert.AreEqual(2, sel.Power);
            var dice = log.OfType<DiceRolled>()[0];
            Assert.AreEqual(2, dice.Faces.Length, "공격력 2 = 주사위 2개, 면 순서 그대로(01 §6-3)");
            Assert.AreEqual((int)dice.Faces[0] + (int)dice.Faces[1], dice.Sum);
            Assert.AreEqual(0, dice.FatigueBefore);
            Assert.AreEqual(1, dice.FatigueAfter);
            var used = log.OfType<MoveUsed>()[0];
            Assert.AreEqual(MoveCategory.Special, used.Category);
            Assert.AreEqual(foe, used.TargetId);
            var tm = log.OfType<TypeMatchup>()[0];
            Assert.AreEqual(2, tm.Delta, "불꽃 → 풀 +2");
            CollectionAssert.AreEqual(new[] { PokemonType.Grass }, tm.DefTypes);
            var br = log.OfType<DamageBreakdown>()[0];
            Assert.AreEqual(3, br.Steps.Length);
            Assert.AreEqual(DamageStepKind.Dice, br.Steps[0].Kind);
            Assert.AreEqual(dice.Sum, br.Steps[0].Value);
            Assert.AreEqual(DamageStepKind.Effectiveness, br.Steps[1].Kind);
            Assert.AreEqual(2, br.Steps[1].Value);
            Assert.AreEqual(DamageStepKind.Reduction, br.Steps[2].Kind);
            Assert.AreEqual(-2, br.Steps[2].Value);
            Assert.IsTrue(br.Steps[2].Source.Same(new SourceRef(SourceKind.Status, (int)StatusEffect.Burned)), "화상 −2(S7_6)");
            Assert.AreEqual(dice.Sum, br.Final, "주사위 + 상성 2 − 화상 2");
            var hp = log.OfType<HpChanged>()[0];
            Assert.AreEqual(foe, hp.PokemonId);
            Assert.AreEqual(6, hp.Before);
            Assert.AreEqual(6 - br.Final, hp.After);
            Assert.AreEqual(3, log.OfType<WindowSkipped>().FindAll(w => w.Seq == used.Seq).Count, "BeforeRoll·AfterRoll·BeforeDamage 창은 쓸 것이 없어 건너뜀");
        }

        [Test]
        public void G13_피로피해는공격피해와동시()
        {
            // 피로 5에서 굴리면 3 피해(A10) — 공격 피해와 같은 Index로 동시에 적용해 둘 다 쓰러진다
            var log = new EventLog();
            var s = Exchange(log, 3, FatigueRules.Max, new[] { "399:0", "396:0" }, new[] { "399:0", "390:0" }, out int a, out int b);
            var hits = log.OfType<HpChanged>();
            Assert.AreEqual(2, hits.Count);
            var attack = hits.Find(h => h.PokemonId == b);
            var tired = hits.Find(h => h.PokemonId == a);
            Assert.AreEqual(SourceKind.Move, attack.Source.Kind);
            Assert.IsTrue(tired.Source.Same(SourceRef.Rule(RuleSource.Fatigue)));
            Assert.AreEqual(3, tired.Before - tired.After);
            Assert.AreEqual(attack.Index, tired.Index, "동시 적용(01 §3-6 ⑪, p.13)");
            var fc = log.OfType<FatigueChanged>()[0];
            Assert.AreEqual(5, fc.Before);
            Assert.AreEqual(5, fc.After, "변화 없음도 낸다(01 §3-6 ⑥)");
            Assert.IsTrue(PokemonRules.IsFainted(ctx, s.Pokemon[a]));
            Assert.IsTrue(PokemonRules.IsFainted(ctx, s.Pokemon[b]));
        }

        [Test]
        public void S7_1_함께기절하면토큰보유자가먼저낸다()
        {
            var log = new EventLog();
            var s = Exchange(log, 3, FatigueRules.Max, new[] { "399:0", "396:0" }, new[] { "399:0", "390:0" }, out int a, out int b);
            var faints = log.OfType<Fainted>();
            Assert.AreEqual(2, faints.Count);
            Assert.AreEqual(1, faints[0].Side, "공격받은 쪽 → 공격한 쪽(G13)");
            Assert.AreEqual(0, faints[1].Side);
            Assert.AreEqual(0, s.Pending.FindAll(c => c.Kind == ChoiceKind.SwitchWindow).Count, "둘 다 기절이면 교체 창 없음");
            Assert.AreEqual(0, Battles.Only(s, ChoiceKind.NextPokemon).Seat, "토큰 보유자(도전자)가 먼저 낸다(p.13)");
            Battles.Answer(s, ctx, ChoiceKind.NextPokemon, Battles.Only(s, ChoiceKind.NextPokemon).Default.Clone(), log);
            Assert.AreEqual(1, Battles.Only(s, ChoiceKind.NextPokemon).Seat);
        }

        [Test]
        public void S7_5_상대기절뒤교체하면순서다시정함_피로0()
        {
            var log = new EventLog();
            var s = Exchange(log, 6, 0, new[] { "399:0" }, new[] { "396:0", "390:0" }, out int a, out int b);
            int a2 = s.Seats[0].Party[1];
            Assert.AreEqual(1, s.Pokemon[a].Fatigue);
            var sw = Battles.Only(s, ChoiceKind.SwitchWindow);
            Assert.AreEqual(0, sw.Seat, "상대가 다음 포켓몬을 내기 전에 교체(p.8)");
            Assert.AreEqual(AnswerKind.Decline, sw.Default.Kind, "G9 기본값 = 교체 안 함");
            int orders = log.OfType<OrderDecided>().Count;
            log.Clear();
            Battles.Answer(s, ctx, sw, ChoiceAnswer.Pick(a2), log);
            var outEv = log.OfType<SwitchedOut>()[0];
            Assert.AreEqual(a, outEv.PokemonId);
            Assert.AreEqual(SwitchReason.Switch, outEv.Reason);
            var fc = log.OfType<FatigueChanged>()[0];
            Assert.AreEqual(1, fc.Before);
            Assert.AreEqual(0, fc.After, "교체하면 피로 0(S7_7)");
            Assert.AreEqual(0, s.Pokemon[a].Fatigue);
            Assert.AreEqual(a2, log.OfType<SentIn>()[0].PokemonId);
            Assert.AreEqual(0, log.OfType<OrderDecided>().Count, "다음 포켓몬이 나오기 전");
            Battles.Answer(s, ctx, ChoiceKind.NextPokemon, ChoiceAnswer.Pick(s.Seats[1].Party[1]), log);
            var order = log.OfType<OrderDecided>();
            Assert.AreEqual(1, order.Count, "교체 뒤 순서를 다시 정한다(S7_5)");
            Assert.AreEqual(1, order[0].First, "찌르꼬 6 > 비버니 4");
            Assert.Greater(orders, 0);
        }

        [Test]
        public void R12_포획굴림도피로를센다_끄면안셈()
        {
            foreach (bool on in new[] { true, false })
            {
                var s = Battles.Game(ctx, 2, 7, new RuleOptions { CaptureAddsFatigue = on });
                int mine = Give(s, 0, "390:0");
                s.Seats[0].Balls[(int)Ball.Regular] = 1;
                var log = new EventLog();
                Battles.Wild(s, ctx, log, 0, Wild(s, "399:0"));
                Battles.Answer(s, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), log);
                var fc = log.OfType<FatigueChanged>().Find(f => f.Cause == FatigueCause.Capture);
                if (on)
                {
                    Assert.IsNotNull(fc, "포획 굴림도 피로 +1(R12, A10)");
                    Assert.AreEqual(0, fc.Before);
                    Assert.AreEqual(1, fc.After);
                }
                else Assert.IsNull(fc, "끄면 포획 굴림은 피로를 세지 않는다");
                // 배틀이 끝났으면 액티브 존이 비어 0(R12)
                if (s.Battle == null) Assert.AreEqual(0, s.Pokemon[mine].Fatigue);
                else Assert.AreEqual(on ? 1 : 0, s.Pokemon[mine].Fatigue);
            }
        }

        // ── 야생: 포획·도망·기권·보상 ──

        GameState CaptureScene(ulong seed, EventLog log, string target, Ball ball, int partyExtra, StatusEffect status, out int mine, out int wild)
        {
            var s = Battles.Game(ctx, 2);
            mine = Give(s, 0, "390:0");
            for (int i = 0; i < partyExtra; i++) Give(s, 0, "399:0");
            s.Seats[0].Balls[(int)ball] = 1;
            wild = Wild(s, target);
            s.Pokemon[wild].Status = status;
            Battles.Wild(s, ctx, log, 0, wild);
            if (partyExtra > 0) Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(mine), log);
            s.Rng = new Pcg32(seed);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)ball), log);
            return s;
        }

        [Test]
        public void A7_포획성공이면파티로_실패면볼버리고계속()
        {
            ulong ok = Battles.Seed(sd => CaptureScene(sd, new EventLog(), "399:0", Ball.Regular, 0, StatusEffect.None, out _, out _).Battle == null);
            var log = new EventLog();
            var s = CaptureScene(ok, log, "399:0", Ball.Regular, 0, StatusEffect.None, out int mine, out int wild);
            var cap = log.OfType<CaptureAttempt>()[0];
            Assert.IsTrue(cap.Success);
            Assert.AreEqual(3, cap.Faces.Length, "몬스터볼 = 타입없음 주사위 3(A7)");
            Assert.AreEqual(3, cap.CatchRate, "약한 3+(S5_2)");
            Assert.GreaterOrEqual(cap.Sum, 3, "눈 합 ≥ 포획률");
            Assert.AreEqual(3, cap.Shakes);
            var gained = log.OfType<PokemonGained>()[0];
            Assert.AreEqual(wild, gained.PokemonId);
            Assert.AreEqual(1, gained.PartySlot);
            Assert.AreEqual((int)Ball.Regular, gained.BallKey);
            Assert.AreEqual(0, s.Pokemon[wild].Owner);
            Assert.AreEqual((int)Ball.Regular, s.Pokemon[wild].Ball);
            Assert.AreEqual(wild, s.Seats[0].Party[1]);
            Assert.AreEqual(0, s.Seats[0].Balls[(int)Ball.Regular]);
            Assert.AreEqual(1, cap.Changes.Length, "던진 볼은 이벤트가 싣는다(01 §3-6 ③) — 볼 칩이 던지는 순간 줄어든다");
            Assert.AreEqual(ResKind.Ball, cap.Changes[0].Res);
            Assert.AreEqual((int)Ball.Regular, cap.Changes[0].Key);
            Assert.AreEqual(0, cap.Changes[0].Seat);
            Assert.AreEqual(1, cap.Changes[0].Before);
            Assert.AreEqual(0, cap.Changes[0].After);
            var end = log.OfType<BattleEnded>()[0];
            Assert.AreEqual(BattleResult.Captured, end.Result);
            Assert.AreEqual(0, end.Winner);
            var jp = System.Array.Find(end.Changes, c => c.Res == ResKind.Jp);
            Assert.AreEqual(1, jp.After - jp.Before, "포획 = 약한 JP 1, 쓰러뜨린 보너스 없음(p.9)");
            Assert.AreEqual(TurnEndReason.Battle, log.OfType<TurnEnded>()[0].Reason, "배틀 뒤 턴 끝(p.3)");

            ulong miss = Battles.Seed(sd => CaptureScene(sd, new EventLog(), "399:0", Ball.Regular, 0, StatusEffect.None, out _, out _).Battle != null);
            var mlog = new EventLog();
            var t = CaptureScene(miss, mlog, "399:0", Ball.Regular, 0, StatusEffect.None, out _, out int foe);
            var mcap = mlog.OfType<CaptureAttempt>()[0];
            Assert.IsFalse(mcap.Success);
            Assert.AreEqual(System.Math.Max(0, 3 - (3 - mcap.Sum)), mcap.Shakes, "흔들림 = max(0, 3 − (포획률 − 합))(01 §6-5)");
            Assert.AreEqual(0, t.Seats[0].Balls[(int)Ball.Regular], "실패한 볼은 버린다");
            Assert.AreEqual(SeatId.Wild, t.Pokemon[foe].Owner);
            Assert.AreEqual(0, mlog.OfType<PokemonGained>().Count);
            Assert.AreEqual(0, mlog.OfType<BattleEnded>().Count, "배틀 계속");
        }

        [Test]
        public void A7_상태이상이면심볼하나를일반으로_포획()
        {
            bool HasSymbol(EventLog l) => System.Array.IndexOf(l.OfType<CaptureAttempt>()[0].Faces, AttackDieFace.StatusSymbol) >= 0;
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                CaptureScene(sd, l, "387:0", Ball.Regular, 0, StatusEffect.Paralysed, out _, out _);
                return HasSymbol(l);
            });
            var log = new EventLog();
            CaptureScene(seed, log, "387:0", Ball.Regular, 0, StatusEffect.Paralysed, out _, out _);
            var cap = log.OfType<CaptureAttempt>()[0];
            int raw = 0;
            foreach (var f in cap.Faces) raw += (int)f;
            Assert.IsTrue(cap.StatusConverted);
            Assert.AreEqual(raw + 1, cap.Sum, "심볼 1개 → 일반 1개(A7). Faces는 바꾸기 전 면");

            var plain = new EventLog();
            CaptureScene(seed, plain, "387:0", Ball.Regular, 0, StatusEffect.None, out _, out _);
            var pc = plain.OfType<CaptureAttempt>()[0];
            Assert.IsFalse(pc.StatusConverted);
            Assert.AreEqual(raw, pc.Sum);
        }

        /// <summary>CaptureScene과 같되 던지기 전에 야생의 피로·직전 기술을 놓는다(이번 배틀에서 굴린 야생).</summary>
        GameState CaptureTired(ulong seed, EventLog log, int partyExtra, out int wild)
        {
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "390:0");
            for (int i = 0; i < partyExtra; i++) Give(s, 0, "399:0");
            s.Seats[0].Balls[(int)Ball.Keystone] = 1;
            wild = Wild(s, "399:0");
            Battles.Wild(s, ctx, log, 0, wild);
            if (partyExtra > 0) Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(mine), log);
            s.Pokemon[wild].Fatigue = 4;
            s.Pokemon[wild].LastMove = Mv("tackle");
            s.Rng = new Pcg32(seed);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Keystone), log);
            return s;
        }

        [Test]
        public void R12_포획한포켓몬은피로0_직전기술없음()
        {
            // 잡힌 포켓몬도 액티브 존을 떠난다(R12, G14, rulings G13). 빈 칸이 있을 때와 파티 가득(아직 파티 밖)일 때 모두
            foreach (int extra in new[] { 0, 2 })
            {
                ulong ok = Battles.Seed(sd => CaptureTiredOk(sd, extra));
                var log = new EventLog();
                var s = CaptureTired(ok, log, extra, out int wild);
                Assert.IsTrue(log.OfType<CaptureAttempt>()[0].Success);
                Assert.AreEqual(0, s.Pokemon[wild].Fatigue, $"파티 {1 + extra}: 잡힌 포켓몬 피로 0");
                Assert.AreEqual(-1, s.Pokemon[wild].LastMove, "직전 기술 없음 — 다음 배틀 첫 턴에 혼란 3 피해를 받지 않는다");
                var fc = log.OfType<FatigueChanged>().Find(f => f.PokemonId == wild);
                Assert.IsNotNull(fc, "화면 피로 칸이 0으로(01 §3-6)");
                Assert.AreEqual(4, fc.Before);
                Assert.AreEqual(0, fc.After);
                if (extra > 0)
                {
                    var c = Battles.Only(s, ChoiceKind.Reward, ChoiceSub.PartyFull);
                    Battles.Answer(s, ctx, c, ChoiceAnswer.Pick(s.Seats[0].Party[1]), log);
                    Assert.AreEqual(wild, s.Seats[0].Party[2]);
                    Assert.AreEqual(0, s.Pokemon[wild].Fatigue);
                    Assert.AreEqual(-1, s.Pokemon[wild].LastMove);
                }
            }
        }

        bool CaptureTiredOk(ulong seed, int extra)
        {
            var l = new EventLog();
            CaptureTired(seed, l, extra, out _);
            var cap = l.OfType<CaptureAttempt>();
            return cap.Count > 0 && cap[0].Success;
        }

        [Test]
        public void S9_2_기술은시그니처포함3개()
        {
            // p.9 「Pokémon can only have 3 moves in total, but the signature move cannot be changed」 = 배운 기술 2칸(파티 패널 칸마다 기술 카드 자리 2)
            Assert.AreEqual(2, PokemonState.MoveSlots);
            var s = Scene.Game(ctx, 2);
            int p = Scene.Give(s, ctx, 0, "387:0");
            Assert.AreEqual(2, s.Pokemon[p].Moves.Length);
            var c = Scene.Open(s, ctx, null, new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.TeachMove, Seat = 0, Subject = Mv("stun_spore"), Window = Window.BattleEnd });
            var legal = Scene.Legal(s, ctx, c);
            Assert.AreEqual(3, legal.Count, "안 가르침 + 칸 0·1");
            Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.Pick && a.Arg >= 2), "칸 2는 없다");
            s.Pokemon[p].Moves[0] = Mv("stun_spore");
            s.Pokemon[p].Moves[1] = Mv("tackle");
            var moves = new List<int>();
            PokemonRules.MoveOptions(ctx, s.Pokemon[p], moves);
            Assert.AreEqual(3, moves.Count, "시그니처 + 배운 기술 2 = 3개");
            s.Seats[0].StampMoves.Add(Mv("razor_leaf"));
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.Anytime(new Maintenance { Kind = MaintKind.SetMove, Pokemon = p, Slot = 2, Value = Mv("razor_leaf") }), null), RejectReason.Illegal, "정비도 칸 2는 없다");
        }

        [Test]
        public void S9_2_타입없음기술은누구에게나가르침()
        {
            // p.9 「Moves that are Typeless can be taught to any Pokémon.」 — 배울 수 있는 타입 목록에 타입없음이 있는 포켓몬은 데이터에 없다
            int mimic = Mv("mimic");
            Assert.AreEqual(PokemonType.Typeless, ctx.Cards.Moves[mimic].Type);
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "387:0");
            Assert.IsTrue(ChoiceRules.CanLearn(ctx, s.Pokemon[mine], PokemonType.Typeless));
            Assert.IsFalse(ChoiceRules.CanLearn(ctx, s.Pokemon[mine], PokemonType.Fire), "다른 타입은 그대로 제한");
            // 꼬지모(흉내내기)를 잎날가르기(+2)로 쓰러뜨리면 기술 가르치기가 열린다(검토 재현: 전에는 열리지 않고 턴이 끝났다)
            int wild = Wild(s, "438:0");
            Battles.SetHp(s, ctx, wild, 1);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("razor_leaf")), log);
            Assert.AreEqual(BattleResult.Win, log.OfType<BattleEnded>()[0].Result);
            var teach = Battles.Only(s, ChoiceKind.Reward, ChoiceSub.TeachMove);
            Assert.AreEqual(mimic, teach.Subject);
            Battles.Answer(s, ctx, teach, ChoiceAnswer.Pick(mine, 0), log);
            Assert.AreEqual(mimic, s.Pokemon[mine].Moves[0]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void S9_2_폼마다시그니처가다른야생을쓰러뜨리면어느폼의것이든하나를가르친다(bool declineFirst)
        {
            // p.6 「If you defeat a wild Pokémon with different signature moves on its forms, you can teach any of the signature moves」 —
            // 체리꼬(421) 흐린 폼 gather_sunlight · 맑은 폼 solar_beam(둘 다 풀 — 모부기가 배운다). 불꽃숭이의 불꽃세례로 쓰러뜨린다
            var s = Battles.Game(ctx, 2);
            int chimchar = Give(s, 0, "390:0");
            int turtwig = Give(s, 0, "387:0");
            int wild = Wild(s, "421:1");
            Battles.SetHp(s, ctx, wild, 1);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild);
            for (int guard = 0; guard < 4 && s.Pending.Count == 1 && s.Pending[0].Kind != ChoiceKind.BattleAction; guard++)
                Battles.Answer(s, ctx, s.Pending[0], s.Pending[0].Kind == ChoiceKind.SendIn ? ChoiceAnswer.Pick(chimchar) : ChoiceAnswer.Of(AnswerKind.Accept), log);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);
            Assert.AreEqual(BattleResult.Win, log.OfType<BattleEnded>()[0].Result);
            var teach = Battles.Only(s, ChoiceKind.Reward, ChoiceSub.TeachMove);
            Assert.AreEqual(Mv("gather_sunlight"), teach.Subject, "쓰러뜨린 폼의 것 먼저");
            if (declineFirst)
            {
                Battles.Answer(s, ctx, teach, ChoiceAnswer.Of(AnswerKind.Decline), log);
                teach = Battles.Only(s, ChoiceKind.Reward, ChoiceSub.TeachMove);
                Assert.AreEqual(Mv("solar_beam"), teach.Subject, "안 가르치면 다른 폼의 것");
            }
            Battles.Answer(s, ctx, teach, ChoiceAnswer.Pick(turtwig, 1), log);
            Assert.AreEqual(Mv(declineFirst ? "solar_beam" : "gather_sunlight"), s.Pokemon[turtwig].Moves[1]);
            Assert.IsFalse(s.Pending.Exists(c => c.Kind == ChoiceKind.Reward), "하나를 가르치면 같은 포켓몬의 다른 폼 시그니처는 묻지 않는다");
        }

        [Test]
        public void G13_파티가득이면새포켓몬을놓아주는것이기본()
        {
            ulong ok = Battles.Seed(sd => CaptureScene(sd, new EventLog(), "399:0", Ball.Keystone, 2, StatusEffect.None, out _, out _).Battle == null);
            foreach (bool keepNew in new[] { false, true })
            {
                var log = new EventLog();
                var s = CaptureScene(ok, log, "399:0", Ball.Keystone, 2, StatusEffect.None, out int mine, out int wild);
                Assert.AreEqual(0, log.OfType<PokemonGained>().Count, "빈 칸이 없다(A3 3칸)");
                Assert.AreEqual(1, log.OfType<BattleEnded>().Count);
                var c = Battles.Only(s, ChoiceKind.Reward, ChoiceSub.PartyFull);
                Assert.AreEqual(Window.BattleEnd, c.Window);
                Assert.AreEqual(wild, c.Default.Target, "기본값 = 새로 잡은 포켓몬을 놓아줌");
                Assert.AreEqual(4, Scene.Legal(s, ctx, c).Count, "새 포켓몬 + 파티 3");
                int def = s.Pokemon[wild].Def;
                int old = s.Seats[0].Party[1];
                if (!keepNew)
                {
                    Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), log));
                    Assert.AreEqual(wild, log.OfType<Released>()[0].PokemonId);
                    Assert.AreEqual(-1, PokemonRules.PartyIndexOf(s.Seats[0], wild));
                    Assert.AreEqual(-1, s.Pokemon[wild].Def, "인스턴스를 비운다");
                    Assert.AreEqual(def, s.Discards[(int)DeckId.Weak][s.Discards[(int)DeckId.Weak].Count - 1], "카드는 버림 더미로(G10)");
                    Assert.AreEqual(0, log.OfType<PokemonGained>().Count);
                }
                else
                {
                    Battles.Answer(s, ctx, c, ChoiceAnswer.Pick(old), log);
                    Assert.AreEqual(old, log.OfType<Released>()[0].PokemonId);
                    var g = log.OfType<PokemonGained>()[0];
                    Assert.AreEqual(wild, g.PokemonId);
                    Assert.AreEqual(2, g.PartySlot, "놓아준 뒤 빈 칸(뒤를 당겨 맨 뒤)");
                }
                Assert.AreEqual(1, log.OfType<TurnEnded>().Count, "고른 뒤 턴 끝");
            }
        }

        [Test]
        public void S7_8_약한야생은하나를쓰러뜨리면도망_규토리2()
        {
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "387:0");
            Give(s, 0, "399:0");
            Battles.SetHp(s, ctx, mine, 1);
            int token = Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak);
            var color = (int)ctx.Fixtures.Fixtures[s.Tokens[token].Def].Apricorns[0];
            int wild = Wild(s, "390:0");
            int def = s.Pokemon[wild].Def;
            int deckBefore = s.Decks[(int)DeckId.Weak].Count;
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild, token);
            Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(mine), log);

            Assert.AreEqual(mine, log.OfType<Fainted>()[0].PokemonId, "불꽃숭이가 먼저(6 > 4), 불꽃세례 +2");
            var fled = log.OfType<Fled>()[0];
            Assert.AreEqual(1, fled.Side);
            Assert.AreEqual(1, fled.Changes.Length);
            Assert.AreEqual(color, fled.Changes[0].Key);
            Assert.AreEqual(2, fled.Changes[0].After - fled.Changes[0].Before, "약한 2개(A8)");
            var end = log.OfType<BattleEnded>()[0];
            Assert.AreEqual(BattleResult.Fled, end.Result);
            Assert.AreEqual(-1, end.Winner);
            Assert.AreEqual(0, end.Changes.Length, "같은 이득을 두 번 세지 않는다(04 §8-2)");
            Assert.AreEqual(deckBefore + 1, s.Decks[(int)DeckId.Weak].Count, "기절하지 않은 야생은 덱으로(S7_8)");
            Assert.AreEqual(def, s.Decks[(int)DeckId.Weak][deckBefore]);
            Assert.IsNull(s.Battle);
            Assert.AreEqual(0, log.OfType<Whiteout>().Count, "비버니가 남았다");
        }

        [Test]
        public void A8_섞인산호에서도망치면색을고른다()
        {
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "387:0");
            Give(s, 0, "399:0");
            Battles.SetHp(s, ctx, mine, 1);
            int token = Flow.Tree(s, ctx, TokenKind.ApricornReef, EncounterTier.Weak); // 빨강·파랑·노랑
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, Wild(s, "390:0"), token);
            Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(mine), log);
            var c = Battles.Only(s, ChoiceKind.Reward, ChoiceSub.CoralColor);
            Assert.AreEqual(2, c.Count, "약한 2개");
            Assert.AreEqual(3, Scene.Legal(s, ctx, c).Count, "3색 중 2색");
            Assert.AreEqual(0, log.OfType<Fled>().Count, "색을 고른 뒤 도망");
            var pick = ChoiceAnswer.PickMany(new List<int> { (int)ApricornColor.Red, (int)ApricornColor.Yellow });
            Battles.Answer(s, ctx, c, pick, log);
            var fled = log.OfType<Fled>()[0];
            Assert.AreEqual(2, fled.Changes.Length);
            Assert.AreEqual((int)ApricornColor.Red, fled.Changes[0].Key);
            Assert.AreEqual((int)ApricornColor.Yellow, fled.Changes[1].Key);
            Assert.AreEqual(1, s.Seats[0].Apricorns[(int)ApricornColor.Red]);
            Assert.AreEqual(1, s.Seats[0].Apricorns[(int)ApricornColor.Yellow]);
        }

        [Test]
        public void G13_야생도함께쓰러지면도망이아니라승리()
        {
            // 모부기(띠, 체력 3, 피로 5)가 체력 1 팽도리를 쓰러뜨리고 피로로 같이 쓰러진다 → 승리, JP 1 + 2, 기술 가르치기
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "387:0");
            int bench = Give(s, 0, "399:0");
            s.Pokemon[mine].HeldItem = Scene.Item(ctx, "item.swiftness_sash");
            s.Pokemon[mine].Fatigue = FatigueRules.Max;
            Battles.SetHp(s, ctx, mine, 3);
            int wild = Wild(s, "393:0");
            Battles.SetHp(s, ctx, wild, 1);
            int def = s.Pokemon[wild].Def;
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild);
            Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(mine), log);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("razor_leaf")), log);

            Assert.AreEqual(2, log.OfType<Fainted>().Count);
            Assert.AreEqual(0, log.OfType<Fled>().Count);
            var end = log.OfType<BattleEnded>()[0];
            Assert.AreEqual(BattleResult.Win, end.Result);
            Assert.AreEqual(0, end.Winner);
            var jp = System.Array.Find(end.Changes, c => c.Res == ResKind.Jp);
            Assert.AreEqual(3, jp.After - jp.Before, "약한 1 + 쓰러뜨림 2(p.9)");
            Assert.AreEqual(def, s.Discards[(int)DeckId.Weak][s.Discards[(int)DeckId.Weak].Count - 1], "기절한 야생은 버림");
            Assert.AreEqual(0, log.OfType<Whiteout>().Count);
            // 거품광선(물)을 배울 수 있는 비버니가 있어 묻는다(p.9)
            var teach = Battles.Only(s, ChoiceKind.Reward, ChoiceSub.TeachMove);
            Assert.AreEqual(Mv("bubble_beam"), teach.Subject);
            Assert.AreEqual(AnswerKind.Decline, teach.Default.Kind);
            Battles.Answer(s, ctx, teach, ChoiceAnswer.Pick(bench, 0), log);
            var taught = log.OfType<MoveTaught>()[0];
            Assert.AreEqual(bench, taught.PokemonId);
            Assert.AreEqual(Mv("bubble_beam"), s.Pokemon[bench].Moves[0]);
            Assert.AreEqual(1, log.OfType<TurnEnded>().Count);
        }

        [Test]
        public void S7_8_기권은이번배틀에서기절한뒤부터_야생은덱으로()
        {
            // 배틀 전에 기절해 있던 포켓몬은 세지 않는다(rulings G13 — 앞 배틀에서 다친 좌석이 반드시 하는 배틀을 대가 없이 빠져나가지 않게)
            var s = Battles.Game(ctx, 2);
            Give(s, 0, "390:0");
            int down = Give(s, 0, "399:0");
            Battles.SetHp(s, ctx, down, 0);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, Wild(s, "399:0"));
            var c = Battles.Only(s, ChoiceKind.BattleAction);
            Assert.IsFalse(Scene.Legal(s, ctx, c).Exists(x => x.Kind == AnswerKind.Forfeit), "배틀 전에 기절한 포켓몬만 있으면 기권 없음");

            // 이번 배틀에서 기절한 뒤: 우두머리 불꽃숭이(우선도 6, 모두 쓰러뜨려야 도망 A17)가 체력 1 모부기를 쓰러뜨리고 비버니가 나온다
            var t = Battles.Game(ctx, 2);
            int first = Give(t, 0, "387:0");
            int second = Give(t, 0, "399:0");
            Battles.SetHp(t, ctx, first, 1);
            int foe = Wild(t, "390:0");
            t.Pokemon[foe].Alpha = true;
            int def = t.Pokemon[foe].Def;
            int before = t.Decks[(int)DeckId.Weak].Count;
            var tlog = new EventLog();
            Battles.Wild(t, ctx, tlog, 0, foe);
            Battles.Answer(t, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(first), tlog);
            Assert.AreEqual(first, tlog.OfType<Fainted>()[0].PokemonId);
            Assert.IsTrue(t.Battle.Sides[0].FaintedThisBattle);
            Assert.AreEqual(second, t.Battle.Sides[0].Active, "비버니가 나왔다");
            var tc = Battles.Only(t, ChoiceKind.BattleAction);
            Assert.AreEqual(AnswerKind.Forfeit, Scene.Legal(t, ctx, tc)[Scene.Legal(t, ctx, tc).Count - 1].Kind, "이번 배틀에서 기절했으니 기권할 수 있다");
            Battles.Answer(t, ctx, tc, ChoiceAnswer.Of(AnswerKind.Forfeit), tlog);
            Assert.AreEqual(0, tlog.OfType<Forfeited>()[0].Seat);
            var end = tlog.OfType<BattleEnded>()[0];
            Assert.AreEqual(BattleResult.Forfeited, end.Result);
            Assert.AreEqual(1, end.Winner);
            Assert.AreEqual(0, end.Changes.Length, "기권은 보상 없음(G13)");
            Assert.AreEqual(def, t.Decks[(int)DeckId.Weak][before], "야생은 덱으로");
            Assert.AreEqual(0, tlog.OfType<Whiteout>().Count, "비버니가 남았다");
        }

        [Test]
        public void S7_8_마지막포켓몬을쓰러뜨린약한야생도도망_규토리2()
        {
            // 스타터 1마리뿐인 좌석(초반 기본)이 쓰러져도 약한 야생은 도망치고 규토리 2개를 남긴다(p.8·p.9, rulings G13). 그 뒤 고향 마을로
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "387:0");
            Battles.SetHp(s, ctx, mine, 1);
            int token = Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak);
            var color = (int)ctx.Fixtures.Fixtures[s.Tokens[token].Def].Apricorns[0];
            int wild = Wild(s, "390:0");
            int def = s.Pokemon[wild].Def;
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild, token);

            Assert.AreEqual(mine, log.OfType<Fainted>()[0].PokemonId);
            var fled = log.OfType<Fled>();
            Assert.AreEqual(1, fled.Count, "마지막 포켓몬이어도 도망");
            Assert.AreEqual(2, fled[0].Changes[0].After - fled[0].Changes[0].Before, "약한 2개(A8)");
            var end = log.OfType<BattleEnded>()[0];
            Assert.AreEqual(BattleResult.Fled, end.Result);
            Assert.AreEqual(-1, end.Winner);
            Assert.AreEqual(2, s.Seats[0].Apricorns[color], "받은 규토리는 귀환 뒤에도 남는다");
            Assert.AreEqual(1, log.OfType<Whiteout>().Count, "쓸 포켓몬이 바닥나 고향 마을로(S7_9)");
            Assert.AreEqual(def, s.Decks[(int)DeckId.Weak][s.Decks[(int)DeckId.Weak].Count - 1], "기절하지 않은 야생은 덱으로");
        }

        [Test]
        public void S7_8_우두머리는모두쓰러뜨리면도망_빙결이면야생승리()
        {
            foreach (bool frozen in new[] { false, true })
            {
                var s = Battles.Game(ctx, 2);
                int mine = Give(s, 0, "387:0");
                Battles.SetHp(s, ctx, mine, 1);
                int token = Flow.Tree(s, ctx, TokenKind.ApricornTree, EncounterTier.Weak);
                int wild = Wild(s, "390:0");
                s.Pokemon[wild].Alpha = true;
                if (frozen) s.Pokemon[wild].Status = StatusEffect.Frozen;
                var log = new EventLog();
                Battles.Wild(s, ctx, log, 0, wild, token);
                var end = log.OfType<BattleEnded>()[0];
                if (!frozen)
                {
                    Assert.AreEqual(BattleResult.Fled, end.Result, "우두머리는 내 포켓몬을 모두 쓰러뜨리면 도망(p.8, A17)");
                    var fled = log.OfType<Fled>()[0];
                    Assert.AreEqual(2, fled.Changes[0].After - fled.Changes[0].Before, "규토리는 등급대로(약한 2)");
                }
                else
                {
                    Assert.AreEqual(BattleResult.Win, end.Result, "빙결은 도망칠 수 없다(p.9, G14)");
                    Assert.AreEqual(1, end.Winner);
                    Assert.AreEqual(0, log.OfType<Fled>().Count);
                    Assert.AreEqual(0, end.Changes.Length, "보상 없음");
                }
                Assert.AreEqual(1, log.OfType<Whiteout>().Count);
            }
        }

        [Test]
        public void S5_2_포획률과보상은등급별_A17_A18()
        {
            CollectionAssert.AreEqual(new[] { 3, 4, 5, 6 }, new[]
            {
                BattleRules.CatchRate(EncounterTier.Weak, false), BattleRules.CatchRate(EncounterTier.Moderate, false),
                BattleRules.CatchRate(EncounterTier.Strong, false), BattleRules.CatchRate(EncounterTier.Legendary, false),
            });
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, new[]
            {
                BattleRules.RewardJp(EncounterTier.Weak, false, false), BattleRules.RewardJp(EncounterTier.Moderate, false, false),
                BattleRules.RewardJp(EncounterTier.Strong, false, false), BattleRules.RewardJp(EncounterTier.Legendary, false, false),
            });
            Assert.AreEqual(5, BattleRules.RewardJp(EncounterTier.Strong, false, true), "쓰러뜨리면 +2");
            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, new[]
            {
                BattleRules.FleeApricorns(EncounterTier.Weak), BattleRules.FleeApricorns(EncounterTier.Moderate), BattleRules.FleeApricorns(EncounterTier.Strong),
            });
            // A17 우두머리: 포획 4, 기절 4 + 2, 포획률 6, 내 포켓몬을 모두 쓰러뜨려야 끝
            Assert.AreEqual(4, BattleRules.RewardJp(EncounterTier.Strong, true, false));
            Assert.AreEqual(6, BattleRules.RewardJp(EncounterTier.Strong, true, true));
            Assert.AreEqual(6, BattleRules.CatchRate(EncounterTier.Strong, true));
            Assert.IsFalse(BattleRules.WildFleesAfterFirstKo(BattleKind.Wild, EncounterTier.Strong, true));
            Assert.IsTrue(BattleRules.WildFleesAfterFirstKo(BattleKind.Wild, EncounterTier.Strong, false));
            // A18 무리 배틀: 7 하나, 끝까지
            Assert.AreEqual(7, BattleRules.SwarmRewardJp);
            Assert.IsFalse(BattleRules.WildFleesAfterFirstKo(BattleKind.Swarm, EncounterTier.Weak, false));
            Assert.IsFalse(BattleRules.WildFleesAfterFirstKo(BattleKind.Legendary, EncounterTier.Legendary, false));
        }

        [Test]
        public void R13_전설특수기술은전설UB우두머리만()
        {
            var s = Battles.Game(ctx, 2);
            bool E(string key, bool alpha = false)
            {
                int id = Scene.Spawn(s, ctx, key, SeatId.Wild);
                s.Pokemon[id].Alpha = alpha;
                return BattleRules.LegendaryMovesEligible(ctx, s.Pokemon[id]);
            }
            Assert.IsTrue(E("389:2"), "전설");
            Assert.IsTrue(E("793:0"), "울트라 비스트");
            Assert.IsTrue(E("800:1"), "울트라 버스트");
            Assert.IsFalse(E("399:0"), "약한");
            Assert.IsTrue(E("399:0", true), "우두머리 토큰 소지");
            Give(s, 0, "387:0");
            var log = new EventLog();
            BattleFlow.StartWild(s, ctx, Scene.Fx(s, log, 0, ActionKind.EncounterPick), 0, Scene.Def(ctx, "399:0"), s.Seats[0].Cell);
            Assert.IsFalse(s.Pokemon[s.Battle.Sides[1].Roster[0]].HasLegendaryMove, "찾아보기 야생은 전설 특수기술이 없다");
        }

        // ── 트레이너 도전 ──

        [Test]
        public void G13_거절당한상대에게는그턴에다시도전못함()
        {
            var s = Battles.Game(ctx, 2);
            Give(s, 0, "387:0");
            Give(s, 1, "393:0");
            var log = new EventLog();
            var map = Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = 0 });
            Assert.IsFalse(Scene.Legal(s, ctx, map).Exists(x => x.Kind == AnswerKind.Challenge), "다른 칸이면 도전 없음(G7)");
            s.Pending.Clear();

            s.Seats[0].Cell = s.Seats[1].Cell; // 좌석 1의 고향 마을
            map = Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = 0 });
            Battles.Answer(s, ctx, map, ChoiceAnswer.Of(AnswerKind.Challenge, 1), log);
            var issued = log.OfType<ChallengeIssued>()[0];
            Assert.IsTrue(issued.CanRefuse, "마을 안이면 거절할 수 있다(p.3)");
            var respond = Battles.Only(s, ChoiceKind.Respond, ChoiceSub.Challenge);
            Assert.AreEqual(1, respond.Seat);
            Assert.AreEqual(AnswerKind.Decline, respond.Default.Kind);
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(respond.Id), log));
            var ans = log.OfType<ChallengeAnswered>()[0];
            Assert.IsFalse(ans.Accept);
            Assert.IsTrue(ans.ByTimeout);
            Assert.AreEqual(issued.Seq, Last<ActionStarted>(log).Continues, "도전 → 응답(01 §3-6 ①-2)");
            Assert.IsNull(s.Battle);
            var again = Battles.Only(s, ChoiceKind.MapAction);
            Assert.AreEqual(0, again.Seat, "거절당하면 턴이 이어진다");
            Assert.IsFalse(Scene.Legal(s, ctx, again).Exists(x => x.Kind == AnswerKind.Challenge && x.Target == 1), "그 턴에 다시 도전 못함(G13)");

            // 보호 중인 좌석에는 도전 못함(p.9)
            var t = Battles.Game(ctx, 2);
            Give(t, 0, "387:0");
            Give(t, 1, "393:0");
            t.Seats[0].Cell = t.Seats[1].Cell;
            t.Seats[1].Protected = true;
            var tm = Scene.Open(t, ctx, null, new PendingChoice { Kind = ChoiceKind.MapAction, Seat = 0 });
            Assert.IsFalse(Scene.Legal(t, ctx, tm).Exists(x => x.Kind == AnswerKind.Challenge));
        }

        [Test]
        public void S7_9_트레이너전이기면둘다보호_진좌석은고향으로()
        {
            foreach (bool draw in new[] { false, true })
            {
                var s = Battles.Game(ctx, 2);
                int a = Give(s, 0, "387:0");
                int b = Give(s, 1, "393:0");
                s.Pokemon[a].HeldItem = Scene.Item(ctx, "item.swiftness_sash");
                if (draw)
                {
                    s.Pokemon[a].Fatigue = FatigueRules.Max;
                    Battles.SetHp(s, ctx, a, 3);
                }
                Battles.SetHp(s, ctx, b, 1);
                int away = Flow.StepInto(s, ctx, s.Seats[0].Cell);
                s.Seats[0].Cell = away;
                s.Seats[1].Cell = away;
                var log = new EventLog();
                BattleFlow.StartTrainer(s, ctx, Scene.Fx(s, log, 0, ActionKind.Challenge), 0, 1);
                Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("razor_leaf")), log);

                var end = log.OfType<BattleEnded>()[0];
                Assert.AreEqual(draw ? BattleResult.Draw : BattleResult.Win, end.Result);
                Assert.AreEqual(draw ? -1 : 0, end.Winner, "둘 다 없으면 무승부(p.8)");
                CollectionAssert.AreEqual(new[] { 0, 1 }, log.OfType<ProtectionSet>()[0].Seats);
                Assert.IsTrue(s.Seats[0].Protected, "두 좌석 모두 도전 보호(p.9)");
                Assert.IsTrue(log.OfType<ProtectionCleared>().Exists(x => x.Seat == 1), "좌석 1은 바로 자기 턴이 와서 풀린다");
                var outs = log.OfType<Whiteout>();
                Assert.AreEqual(draw ? 2 : 1, outs.Count, "쓸 포켓몬이 바닥난 좌석은 고향 마을로(S7_9)");
                var lost = outs.Find(w => w.Seat == 1);
                Assert.AreEqual(ctx.Fixtures.Fixtures[s.Seats[1].HomeVillage].Cell, lost.ToCell);
                Assert.IsTrue(log.OfType<RespawnPending>().Exists(x => x.Seat == 1));
                Assert.IsTrue(log.OfType<RespawnCleared>().Exists(x => x.Seat == 1), "다음 내 턴 시작에 회복(S7_9)");
                Assert.AreEqual(TurnEndReason.Battle, log.OfType<TurnEnded>()[0].Reason);
                Assert.AreEqual(1, s.ActiveSeat, "다음 좌석의 턴");
            }
        }

        // ── 1막 조무래기 배틀(A1) ──

        /// <summary>실제 흐름(2인)에서 차례 좌석이 자기 마을의 조무래기에게 도전한다. prepare로 조무래기·내 포켓몬을 맞춘 뒤 기본 답으로 끝까지.</summary>
        GameState GruntBattle(ulong seed, EventLog log, System.Action<GameState, List<int>> prepare, out int seat)
        {
            var s = Flow.Started(ctx, 2, 41);
            seat = s.ActiveSeat;
            s.Rng = new Pcg32(seed);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic), log);
            prepare(s, s.Battle.Sides[1].Roster);
            Battles.Drive(s, ctx, log);
            return s;
        }

        [Test]
        public void A1_조무래기2마리를모두쓰러뜨리면해방()
        {
            void Prep(GameState s, List<int> roster)
            {
                Battles.SetHp(s, ctx, roster[1], 0);
                Battles.SetHp(s, ctx, roster[0], 1);
            }
            ulong seed = Battles.Seed(sd => GruntBattle(sd, new EventLog(), Prep, out int st).Seats[st].ActProgress == 1);
            var pre = Flow.Started(ctx, 2, 41);
            Assert.IsTrue(Flow.MapLegal(pre, ctx).Exists(x => x.Kind == AnswerKind.Challenge && x.Target == SeatId.Galactic), "자기 마을의 조무래기(A1)");

            var log = new EventLog();
            var s = GruntBattle(seed, log, Prep, out int seat);
            int home = ctx.Fixtures.Fixtures[s.Seats[seat].HomeVillage].Cell;
            Assert.AreEqual(DeckId.GalacticGrunt, log.OfType<PileShuffled>()[0].Pile, "조무래기 덱을 섞고");
            var gb = log.OfType<GalacticBattle>()[0];
            Assert.AreEqual(GalacticLeader.Grunt, gb.Leader);
            Assert.AreEqual(2, gb.LineupCount, "2장 앞면(p.21)");
            Assert.AreEqual(2, gb.Lineup.Length);
            Assert.AreEqual(BattleKind.Galactic, log.OfType<BattleStarted>()[0].Kind);
            Assert.AreEqual(SeatId.Galactic, log.OfType<ChoiceRequested>().Find(c => c.Kind == ChoiceKind.SendIn).Seat, "조종자가 첫 포켓몬을 고른다(p.20)");
            var goal = log.OfType<ActGoalMet>()[0];
            Assert.AreEqual(seat, goal.Seat);
            Assert.AreEqual(1, goal.Act);
            Assert.IsTrue(goal.First);
            Assert.AreEqual(home, log.OfType<TokenRemoved>()[0].Cell);
            Assert.IsFalse(SetupFlow.GruntAt(s, home), "조무래기 토큰 제거");
            Assert.AreEqual(seat, s.Hos.FirstSeat[1]);
            Assert.AreEqual(8, s.Decks[(int)DeckId.GalacticGrunt].Count, "조무래기 카드는 덱으로(G13)");
            var acts = log.OfType<ActRevealed>();
            Assert.AreEqual(1, acts.Count, "처음 해방한 좌석이 2막을 공개");
            Assert.AreEqual(seat, acts[0].FirstSeat);
            Assert.AreEqual(2, s.Hos.Act);
            Assert.Less(log.Events.IndexOf(log.OfType<TurnEnded>()[0]), log.Events.IndexOf(acts[0]), "턴 끝 → 막 공개 → 다음 턴");

            // 다음 내 턴부터 이동력 +2(A1)
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);
            Assert.AreEqual(seat, s.ActiveSeat);
            var allowance = Last<MoveAllowanceSet>(log);
            Assert.AreEqual(5, allowance.Total);
            Assert.AreEqual(SourceKind.Act, allowance.Mods[0].Source.Kind);
            Assert.IsFalse(Flow.MapLegal(s, ctx).Exists(x => x.Kind == AnswerKind.Challenge && x.Target == SeatId.Galactic), "해방했으면 다시 도전 없음");
        }

        // M9 다듬기 P18(04 §8-8 94행 1막): 조무래기 토큰 빼기는 941행 단계로 서고(방장·화면 두 표 · 1500 — §8-5 「공개 없는 queue 항목」), 배틀 끝 항목에서 떼어 결과 창 [확인] 뒤의 새 항목이 된다
        [Test]
        public void M9_1막해방의조무래기토큰은결과창뒤의새항목에서날아가고이동력칩이뜬다()
        {
            void Prep(GameState s, List<int> roster)
            {
                Battles.SetHp(s, ctx, roster[1], 0);
                Battles.SetHp(s, ctx, roster[0], 1);
            }
            ulong seed = Battles.Seed(sd => GruntBattle(sd, new EventLog(), Prep, out int st).Seats[st].ActProgress == 1);
            var log = new EventLog();
            var state = GruntBattle(seed, log, Prep, out int seat);
            var gone = log.OfType<TokenRemoved>()[0];
            Assert.AreEqual(TokenKind.Grunt, gone.TokenKind);
            Assert.AreEqual(Sinnoh.Fx.LiberateBeats.Row, Sinnoh.Rules.Session.PlayTiming.RowOf(gone));
            Assert.AreEqual(Sinnoh.Fx.LiberateBeats.Row, Sinnoh.Fx.FxRows.Of(gone));
            Assert.AreEqual(1500, Sinnoh.Rules.Session.PlayTiming.RowMs(gone, Sinnoh.Fx.LiberateBeats.Row));
            Assert.IsTrue(Sinnoh.Fx.FxBuilt.Drawn(Sinnoh.Fx.LiberateBeats.Row), "화면이 그리는 행");

            var entries = new Sinnoh.Fx.FxPlanner(seat, 2).Plan(log.Events);
            var before = entries.Find(x => Sinnoh.Fx.LiberateBeats.StepOf(x) != null);
            Assert.IsNotNull(before, "941행 단계");
            Assert.IsTrue(before.Events.Exists(e => e is BattleEnded), "엔진은 배틀 끝과 한 행동으로 낸다");
            int length = before.LengthMs, at = entries.IndexOf(before);
            Assert.AreEqual(1, Sinnoh.Fx.GodBeats.SplitOff(entries, Sinnoh.Fx.LiberateBeats.StepOf));
            var tail = entries[at + 1];
            Assert.IsNull(Sinnoh.Fx.LiberateBeats.StepOf(before), "배틀 끝 항목에는 없다");
            Assert.IsNotNull(before.Step(940), "막 목표 줄(결과 창)은 배틀 끝 항목에 남는다");
            var step = Sinnoh.Fx.LiberateBeats.StepOf(tail);
            Assert.IsNotNull(step);
            Assert.AreEqual(0, step.StartMs);
            Assert.AreSame(gone, Sinnoh.Fx.LiberateBeats.Removed(step));
            Assert.AreEqual(before.Seq, tail.Seq, "같은 행동");
            Assert.AreEqual(length, before.LengthMs + tail.LengthMs, "길이의 합은 그대로(P5)");
            Assert.AreEqual(1500, tail.LengthMs);

            // 박자: 움찔 → 날아오름 → 사라짐 · 칩이 튀어나와 머물다 자리로 날아가 흐려진다
            Assert.AreEqual(0f, Sinnoh.Fx.LiberateBeats.Lift(0));
            Assert.AreEqual(1f, Sinnoh.Fx.LiberateBeats.Squash(Sinnoh.Fx.LiberateBeats.LeadMs - 1), "결과 창을 걷는 줄무늬 동안은 그대로");
            Assert.Less(Sinnoh.Fx.LiberateBeats.Squash(Sinnoh.Fx.LiberateBeats.LiftAtMs - 1), 0.75f, "움찔");
            Assert.That(Sinnoh.Fx.LiberateBeats.Lift(Sinnoh.Fx.LiberateBeats.LiftAtMs + Sinnoh.Fx.LiberateBeats.LiftMs / 2), Is.InRange(0.1f, 0.9f), "날아오르는 중");
            Assert.AreEqual(-1f, Sinnoh.Fx.LiberateBeats.Lift(Sinnoh.Fx.LiberateBeats.GoneAt), "사라짐");
            Sinnoh.Fx.LiberateBeats.Chip(Sinnoh.Fx.LiberateBeats.ChipAtMs - 1, 1500, out float show, out float scale, out float fly);
            Assert.AreEqual(0f, show, "토큰이 뜬 뒤에 칩");
            Sinnoh.Fx.LiberateBeats.Chip(Sinnoh.Fx.LiberateBeats.ChipAtMs + 50, 1500, out show, out scale, out fly);
            Assert.Greater(show, 0f);
            Assert.Greater(scale, 1f, "튀어나온다");
            Assert.AreEqual(0f, fly);
            Sinnoh.Fx.LiberateBeats.Chip(1100, 1500, out show, out scale, out fly);
            Assert.AreEqual(1f, show);
            Assert.AreEqual(1f, scale);
            Assert.AreEqual(0f, fly, "마을 위에 머묾");
            Sinnoh.Fx.LiberateBeats.Chip(1450, 1500, out show, out scale, out fly);
            Assert.AreEqual(1f, fly, "그 좌석의 자리에 닿았다");
            Assert.Less(show, 1f, "흐려진다");
            Sinnoh.Fx.LiberateBeats.Chip(1500, 1500, out show, out scale, out fly);
            Assert.AreEqual(0f, show);
            Assert.AreEqual(1, state.Seats[seat].ActProgress);

            // 소리: 날아오를 때 flee · 칩이 튀어나올 때 shine(소리 표의 키 — 새 SE 없음)
            Sinnoh.Fx.SoundCues.Add(tail, true);
            Assert.IsTrue(tail.Cues.Exists(c => c.Sound == Sinnoh.Fx.SoundKeys.Flee && c.AtMs == Sinnoh.Fx.LiberateBeats.LiftAtMs));
            Assert.IsTrue(tail.Cues.Exists(c => c.Sound == Sinnoh.Fx.SoundKeys.Shine && c.AtMs == Sinnoh.Fx.LiberateBeats.ChipAtMs));
        }

        [Test]
        public void A1_마지막에함께쓰러지면해방아님()
        {
            void Prep(GameState s, List<int> roster)
            {
                Battles.SetHp(s, ctx, roster[1], 0);
                Battles.SetHp(s, ctx, roster[0], 1);
                int mine = s.Battle.Sides[0].Active;
                s.Pokemon[mine].Fatigue = FatigueRules.Max;
                Battles.SetHp(s, ctx, mine, System.Math.Min(3, Battles.Hp(s, ctx, mine)));
            }
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                GruntBattle(sd, l, Prep, out _);
                var e = l.OfType<BattleEnded>();
                return e.Count == 1 && e[0].Result == BattleResult.Draw;
            });
            var log = new EventLog();
            var s = GruntBattle(seed, log, Prep, out int seat);
            var end = log.OfType<BattleEnded>()[0];
            Assert.AreEqual(-1, end.Winner, "아직 싸울 수 있는 상태가 아니다(A1)");
            Assert.AreEqual(0, s.Seats[seat].ActProgress);
            Assert.AreEqual(0, log.OfType<ActGoalMet>().Count);
            Assert.IsTrue(SetupFlow.GruntAt(s, ctx.Fixtures.Fixtures[s.Seats[seat].HomeVillage].Cell));
            Assert.AreEqual(seat, log.OfType<Whiteout>()[0].Seat);
        }

        [Test]
        public void M3_좌석1에서6_조무래기배틀이끝까지()
        {
            for (int n = 1; n <= SeatId.MaxSeats; n++)
            {
                var s = Flow.Started(ctx, n, (ulong)(60 + n));
                int seat = s.ActiveSeat;
                var log = new EventLog();
                Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic), log);
                Battles.Drive(s, ctx, log);
                Assert.IsNull(s.Battle, $"{n}인");
                Assert.AreEqual(1, log.OfType<BattleEnded>().Count, $"{n}인");
                Assert.AreEqual(Phase.Turn, s.Phase);
                Assert.AreEqual(ChoiceKind.MapAction, Battles.Only(s, ChoiceKind.MapAction).Kind);
                Assert.AreEqual((seat + 1) % n, s.ActiveSeat, $"{n}인: 다음 좌석의 턴");
            }
        }

        // ── 갤럭시단: 플랜 B·은하의 응징·교체 금지 ──

        /// <summary>갤럭시단 배틀: 좌석 0 = mine(먼저 낼 포켓몬), 조무래기 명단 grunts(첫째가 먼저 나온다).</summary>
        GameState Galactic(EventLog log, RuleOptions opt, string mineKey, int mineHp, string[] bench, string[] grunts, out int mine, out int[] g)
        {
            var s = Battles.Game(ctx, 2, 7, opt);
            mine = Give(s, 0, mineKey);
            foreach (var k in bench) Give(s, 0, k);
            if (mineHp > 0) Battles.SetHp(s, ctx, mine, mineHp);
            g = new int[grunts.Length];
            for (int i = 0; i < g.Length; i++) g[i] = Grunt(s, grunts[i]);
            Battles.Start(s, ctx, log, BattleKind.Galactic, 0, SeatId.Galactic, -1, g);
            if (bench.Length > 0) Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(mine), log);
            if (g.Length > 1) Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(g[0]), log);
            return s;
        }

        [Test]
        public void A13_플랜B는응징1_기절은2()
        {
            // 모부기(4)가 먼저, 동미러(2)는 체력 9라 남는다 → 조무래기가 플랜 B
            var log = new EventLog();
            var s = Galactic(log, null, "387:0", 0, new string[0], new[] { "436:0@male_grunt", "41:0@female_grunt" }, out int mine, out var g);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("razor_leaf")), log);
            var c = Battles.Only(s, ChoiceKind.BattleAction);
            Assert.AreEqual(SeatId.Galactic, c.Seat);
            var legal = Scene.Legal(s, ctx, c);
            Assert.AreEqual(BattleRules.PlanB, legal[legal.Count - 1].Target, "플랜 B는 늘 고를 수 있다(전술 보드)");
            log.Clear();
            Battles.Answer(s, ctx, c, Battles.Move(BattleRules.PlanB), log);
            var sel = log.OfType<MoveSelected>()[0];
            Assert.AreEqual(BattleRules.PlanB, sel.MoveKey);
            Assert.AreEqual(PokemonType.Typeless, sel.Type);
            Assert.AreEqual(3, sel.Power);
            var st = log.OfType<RetributionStored>()[0];
            Assert.AreEqual(RetributionCause.PlanB, st.Cause);
            Assert.AreEqual(0, st.Before);
            Assert.AreEqual(1, st.After, "플랜 B마다 +1(A13)");
            Assert.AreEqual(1, s.Battle.Sides[1].Retribution);

            // 기절마다 2: 불꽃숭이 불꽃세례(+2)가 체력 1 동미러를 쓰러뜨린다
            var log2 = new EventLog();
            var t = Galactic(log2, null, "390:0", 0, new string[0], new[] { "436:0@male_grunt", "436:0@male_grunt" }, out _, out var g2);
            Battles.SetHp(t, ctx, g2[0], 1);
            Battles.Answer(t, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log2);
            var faint = log2.OfType<RetributionStored>()[0];
            Assert.AreEqual(RetributionCause.Fainted, faint.Cause);
            Assert.AreEqual(2, faint.After - faint.Before);
        }

        [Test]
        public void G5_피해전이면응징과공격피해동시적용()
        {
            // 옵션 BeforeDamage(2026-11-14 전의 기본 — 기본은 턴 끝, 원작 커뮤니티 확인)
            var log = new EventLog();
            var s = Galactic(log, BeforeDamage(), "390:0", 0, new string[0], new[] { "436:0@male_grunt", "41:0@female_grunt" }, out int mine, out var g);
            s.Battle.Sides[1].Retribution = 2;
            log.Clear();
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);
            int attack = log.OfType<ActionStarted>()[0].Seq;
            var c = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.Retribution);
            Assert.AreEqual(Window.BeforeDamage, c.Window, "굴림 뒤·피해 전(G5)");
            Assert.AreEqual(mine, c.Subject);
            Assert.AreEqual(0, log.OfType<HpChanged>().Count, "아직 피해 전");
            Battles.Answer(s, ctx, c, ChoiceAnswer.Of(AnswerKind.Accept), log);
            var starts = log.OfType<ActionStarted>();
            Assert.AreEqual(attack, starts[1].ReactsTo, "반응 Seq의 ReactsTo = 공격");
            Assert.AreEqual(attack, starts[2].Continues, "나머지 공격 Seq의 Continues = 공격");
            var hits = log.OfType<HpChanged>();
            Assert.AreEqual(g[0], hits[0].PokemonId);
            Assert.AreEqual(mine, hits[1].PokemonId);
            Assert.IsTrue(hits[1].Source.Same(SourceRef.Rule(RuleSource.GalacticRetribution)));
            Assert.AreEqual(2, hits[1].Before - hits[1].After, "고정 피해 2(상성·감소 없음)");
            Assert.AreEqual(hits[0].Index, hits[1].Index, "공격 피해와 동시(p.13)");
            Assert.AreEqual(starts[2].Seq, hits[0].Seq);
        }

        [Test]
        public void G5_피해전이면이번기절의2는다음공격에()
        {
            var log = new EventLog();
            var s = Galactic(log, BeforeDamage(), "390:0", 0, new string[0], new[] { "436:0@male_grunt", "436:0@male_grunt" }, out int mine, out var g);
            Battles.SetHp(s, ctx, g[0], 1);
            log.Clear();
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);
            Assert.AreEqual(0, log.OfType<RetributionMoved>().Count);
            Assert.IsTrue(log.OfType<WindowSkipped>().Exists(w => w.Window == Window.BeforeDamage), "저장 0이라 이번 공격에는 묻지 않는다");
            Assert.AreEqual(2, s.Battle.Sides[1].Retribution, "이번 기절의 2는 기절 확인에서 저장");
            Assert.AreEqual(g[1], s.Battle.Sides[1].Active, "다음 조무래기(하나뿐이라 묻지 않음)");
            // 다음 공격에 옮길 수 있다
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);
            var c = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.Retribution);
            Assert.AreEqual(mine, c.Subject);
        }

        static RuleOptions BeforeDamage() => new RuleOptions { GalacticRetributionTiming = GalacticRetributionTiming.BeforeDamage };

        [Test]
        public void G5_턴끝이기본_공격피해뒤에묻고옮긴피해는독과함께()
        {
            // 원작 커뮤니티 확인,
            // 원작 커뮤니티 확인
            Assert.AreEqual(GalacticRetributionTiming.TurnEnd, new RuleOptions().GalacticRetributionTiming);
            var log = new EventLog();
            var s = Galactic(log, null, "390:0", 0, new string[0], new[] { "436:0@male_grunt", "41:0@female_grunt" }, out int mine, out var g);
            StatusRules.Inflict(ctx, s.Pokemon[mine], StatusEffect.Poisoned);
            s.Battle.Sides[1].Retribution = 2;
            log.Clear();
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);
            var c = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.Retribution);
            Assert.AreEqual(Window.BattleTurnEnd, c.Window, "공격한 쪽 턴 끝");
            Assert.AreEqual(mine, c.Subject);
            Assert.AreEqual(0, log.OfType<RetributionMoved>().Count, "피해 전에는 묻지 않는다");
            var hit = log.OfType<HpChanged>();
            Assert.AreEqual(1, hit.Count, "공격 피해는 이미 들어갔다");
            Assert.AreEqual(g[0], hit[0].PokemonId);
            int hp = Battles.Hp(s, ctx, mine);
            Battles.Answer(s, ctx, c, ChoiceAnswer.Of(AnswerKind.Accept), log);
            var moved = log.OfType<HpChanged>().FindAll(h => h.PokemonId == mine && h.Source.Same(SourceRef.Rule(RuleSource.GalacticRetribution)));
            Assert.AreEqual(1, moved.Count);
            Assert.AreEqual(2, moved[0].Before - moved[0].After, "고정 피해 2");
            Assert.AreNotEqual(hit[0].Index, moved[0].Index, "공격 피해와 따로(턴 끝)");
            var tick = log.OfType<StatusTicked>().Find(t => t.PokemonId == mine);
            Assert.IsNotNull(tick, "독도 같은 턴 끝에");
            Assert.AreEqual(moved[0].Index, tick.Index, "턴 끝 피해끼리 동시(p.13)");
            Assert.AreEqual(System.Math.Max(0, hp - 4), Battles.Hp(s, ctx, mine));
        }

        [Test]
        public void G5_턴끝이면마지막갤럭시단포켓몬을쓰러뜨린공격에는터지지않음()
        {
            // 원작 커뮤니티 확인
            // (원작 커뮤니티 확인). 2023 답(무승부 = 트레이너 패배, 원작 커뮤니티 확인)과 엇갈려 [가정] — 2023 쪽은 AfterFaint 옵션
            var log = new EventLog();
            var s = Galactic(log, null, "390:0", 0, new string[0], new[] { "436:0@male_grunt" }, out int mine, out var g);
            Battles.SetHp(s, ctx, g[0], 1);
            s.Battle.Sides[1].Retribution = 4;
            int hp = Battles.Hp(s, ctx, mine);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);
            Assert.AreEqual(0, s.Pending.FindAll(x => x.Sub == ChoiceSub.Retribution).Count, "배틀이 끝나 묻지 않는다");
            Assert.AreEqual(0, log.OfType<RetributionMoved>().Count);
            Assert.AreEqual(BattleResult.Win, log.OfType<BattleEnded>()[0].Result, "무승부가 아니라 승리");
            Assert.AreEqual(hp, Battles.Hp(s, ctx, mine));
        }

        [Test]
        public void G5_기절뒤면이번기절의2도옮김()
        {
            var opt = new RuleOptions { GalacticRetributionTiming = GalacticRetributionTiming.AfterFaint };
            var log = new EventLog();
            var s = Galactic(log, opt, "390:0", 0, new string[0], new[] { "436:0@male_grunt", "436:0@male_grunt" }, out int mine, out var g);
            Battles.SetHp(s, ctx, g[0], 1);
            log.Clear();
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);
            var c = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.Retribution);
            Assert.AreEqual(Window.AfterDamage, c.Window);
            Assert.AreEqual(1, log.OfType<Fainted>().Count, "기절 확인 뒤에 묻는다");
            int hp = Battles.Hp(s, ctx, mine);
            Battles.Answer(s, ctx, c, ChoiceAnswer.Of(AnswerKind.Accept), log);
            Assert.AreEqual(2, log.OfType<RetributionMoved>()[0].Amount, "이번 기절의 2도 옮긴다");
            Assert.AreEqual(hp - 2, Battles.Hp(s, ctx, mine), "옮긴 피해는 따로 적용");
            var hits = log.OfType<HpChanged>();
            Assert.AreNotEqual(hits[0].Seq, hits[hits.Count - 1].Seq);
        }

        [Test]
        public void A16_갤럭시단은쓰러뜨린직후교체못함_끄면함()
        {
            foreach (bool locked in new[] { true, false })
            {
                var log = new EventLog();
                var s = Galactic(log, new RuleOptions { GalacticNoSwitchAfterKo = locked }, "387:0", 1, new[] { "399:0" },
                    new[] { "41:0@female_grunt", "436:0@male_grunt" }, out int mine, out var g);
                // 주뱃(5)이 먼저, 독엄니(+2)로 체력 1 모부기를 쓰러뜨린다
                Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("poison_fang")), log);
                Assert.AreEqual(mine, log.OfType<Fainted>()[0].PokemonId);
                Assert.AreEqual(g[0], log.OfType<Fainted>()[0].ById);
                if (locked)
                {
                    var lockEv = log.OfType<SwitchLocked>();
                    Assert.AreEqual(1, lockEv.Count);
                    Assert.AreEqual(1, lockEv[0].Side);
                    Assert.AreEqual(SwitchLockReason.GalacticNoSwitchAfterKo, lockEv[0].Reason);
                    Assert.AreEqual(0, s.Pending.FindAll(c => c.Kind == ChoiceKind.SwitchWindow).Count);
                }
                else
                {
                    Assert.AreEqual(0, log.OfType<SwitchLocked>().Count);
                    Assert.AreEqual(SeatId.Galactic, Battles.Only(s, ChoiceKind.SwitchWindow).Seat, "끄면 룰북의 「더 어려운 배틀」(S7_10)");
                }
            }
        }

        [Test]
        public void M3_연출Seq경계()
        {
            // ① 찾아보기 → 조우 고르기: 둘째 Seq의 Continues = 첫 Seq
            var f = Flow.Started(ctx, 2, 22);
            var tree = f.Tokens[Flow.Tree(f, ctx, TokenKind.ApricornTree, EncounterTier.Moderate)];
            f.Seats[f.ActiveSeat].Cell = tree.Cell;
            var flog = new EventLog();
            Flow.Answer(f, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.Search), flog);
            int search = flog.OfType<ActionStarted>()[0].Seq;
            var pick = Flow.Only(f, ChoiceKind.EncounterPick);
            flog.Clear();
            Flow.Answer(f, ctx, ChoiceKind.EncounterPick, pick.Default, flog);
            Assert.AreEqual(search, flog.OfType<ActionStarted>()[0].Continues);
            Assert.AreEqual(flog.OfType<ActionStarted>()[0].Seq, flog.OfType<BattleStarted>()[0].Seq, "배틀 시작은 고른 행동 안");

            // ② 응징 옮기기가 낀 공격: 반응 Seq의 ReactsTo = 공격, 나머지 공격 Seq의 Continues = 공격(피해 전 옵션 — 기절 연쇄 ③과 한 공격에서 보려고)
            var log = new EventLog();
            var s = Galactic(log, BeforeDamage(), "390:0", 0, new string[0], new[] { "436:0@male_grunt", "436:0@male_grunt" }, out int mine, out var g);
            s.Battle.Sides[1].Retribution = 2;
            Battles.SetHp(s, ctx, g[0], 1);
            log.Clear();
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("ember")), log);
            int attack = log.OfType<ActionStarted>()[0].Seq;
            Battles.Answer(s, ctx, Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.Retribution), ChoiceAnswer.Of(AnswerKind.Accept), log);
            var starts = log.OfType<ActionStarted>();
            Assert.AreEqual(ActionKind.Reaction, starts[1].Kind);
            Assert.AreEqual(attack, starts[1].ReactsTo);
            Assert.AreEqual(-1, starts[1].Continues);
            Assert.AreEqual(ActionKind.BattleAction, starts[2].Kind);
            Assert.AreEqual(attack, starts[2].Continues);

            // ③ 기절 연쇄: 같은 Seq + ParentSource = 기술
            var faint = log.OfType<Fainted>()[0];
            Assert.AreEqual(starts[2].Seq, faint.Seq);
            Assert.IsTrue(faint.ParentSource.Same(new SourceRef(SourceKind.Move, Mv("ember"))));
            var stored = log.OfType<RetributionStored>().Find(r => r.Cause == RetributionCause.Fainted);
            Assert.AreEqual(faint.Seq, stored.Seq);
            Assert.IsTrue(stored.ParentSource.Same(faint.ParentSource), "기절 → 응징 저장(01 §3-6 ⑦)");
            Assert.AreEqual(mine, log.OfType<RetributionMoved>()[0].ToPokemonId);
        }
    }
}
