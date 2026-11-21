using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M3-c: 명령 4종, ChoiceKind 뼈대(Clock·Default·Legal), 동시 선택, 행동 경계(ActionStarted·Seq·Continues·ReactsTo, 01 §3-6 ①).
    /// </summary>
    public class ChoiceTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        sealed class Case
        {
            public string Name;
            public Func<GameState> Make;
            public Func<GameState, List<PendingChoice>> Choices;
        }

        PendingChoice C(ChoiceKind kind, int seat, ChoiceSub sub = ChoiceSub.None) => new PendingChoice { Kind = kind, Seat = seat, Sub = sub };

        List<PendingChoice> One(PendingChoice c) => new List<PendingChoice> { c };

        GameState Wild2(GameState s)
        {
            Scene.Give(s, ctx, 0, "387:0");
            Scene.Give(s, ctx, 0, "390:0");
            Scene.Battle(s, BattleKind.Wild, 0, SeatId.Wild, Scene.Spawn(s, ctx, "399:0", SeatId.Wild));
            return s;
        }

        List<Case> Cases()
        {
            var cases = new List<Case>();
            void Add(string name, Func<GameState> make, Func<GameState, List<PendingChoice>> choices) =>
                cases.Add(new Case { Name = name, Make = make, Choices = choices });
            Func<GameState> g2 = () => Scene.Game(ctx, 2);

            Add("VillagePick", g2, s => One(C(ChoiceKind.VillagePick, 0)));
            Add("StarterPick", g2, s =>
            {
                var c = C(ChoiceKind.StarterPick, 0);
                c.Secret = true;
                foreach (var e in ctx.Deck(DeckId.Starter).Cards) c.Options.Add(e.Card);
                return One(c);
            });
            Add("TrainerCardPick", g2, s =>
            {
                s.Seats[1].TrainerCardOffer.AddRange(Scene.Top(s, DeckId.TrainerCard, 3));
                var c = C(ChoiceKind.TrainerCardPick, 1);
                c.Secret = true;
                return One(c);
            });
            Add("MapAction", g2, s => One(C(ChoiceKind.MapAction, 0)));
            Add("HiddenSpot", g2, s => One(C(ChoiceKind.HiddenSpot, 0)));
            Add("EncounterPick", g2, s =>
            {
                Scene.Give(s, ctx, 0, "387:0"); // 고르면 배틀이 시작된다(M3-e) — 쓸 포켓몬이 있어야 찾아볼 수 있다
                var c = C(ChoiceKind.EncounterPick, 0);
                c.Options.AddRange(Scene.Top(s, DeckId.Weak, 3));
                return One(c);
            });
            Add("Lineup", g2, s =>
            {
                Scene.Give(s, ctx, 0, "387:0"); // 답하면 간부 배틀이 시작된다(M5 「막 뼈대」) — 도전한 좌석(Subject)에 포켓몬이 있어야 한다
                var c = C(ChoiceKind.Lineup, SeatId.Galactic);
                c.Subject = 0;
                c.Options.AddRange(Scene.Top(s, DeckId.GalacticCommander, 5));
                c.Count = 3;
                return One(c);
            });
            Add("SendIn", () => Wild2(Scene.Game(ctx, 2)), s => One(C(ChoiceKind.SendIn, 0)));
            Add("NextPokemon", () => Wild2(Scene.Game(ctx, 2)), s => One(C(ChoiceKind.NextPokemon, 0)));
            Add("SwitchWindow", () => Wild2(Scene.Game(ctx, 2)), s => One(C(ChoiceKind.SwitchWindow, 0)));
            Add("BattleAction", () =>
            {
                var s = Wild2(Scene.Game(ctx, 2));
                s.Seats[0].Balls[(int)Ball.Regular] = 1;
                return s;
            }, s => One(C(ChoiceKind.BattleAction, 0)));
            Add("Reaction/InitiativeToken", () => Wild2(Scene.Game(ctx, 2)), s => One(C(ChoiceKind.Reaction, 0, ChoiceSub.InitiativeToken)));
            Add("Respond/Challenge", g2, s =>
            {
                var c = C(ChoiceKind.Respond, 1, ChoiceSub.Challenge);
                c.Subject = 0;
                return One(c);
            });
            Add("Respond/Trade", g2, s =>
            {
                var c = C(ChoiceKind.Respond, 1, ChoiceSub.Trade);
                c.Subject = 0;
                return One(c);
            });
            Add("Simultaneous/Follower", () => Scene.Game(ctx, 3), s =>
            {
                var l = new List<PendingChoice>();
                for (int i = 0; i < 3; i++)
                {
                    var c = C(ChoiceKind.Simultaneous, i, ChoiceSub.Follower);
                    c.Secret = true;
                    l.Add(c);
                }
                return l;
            });
            Add("Simultaneous/BreachAttach", g2, s =>
            {
                int a = Scene.Give(s, ctx, 0, "387:0");
                int b = Scene.Give(s, ctx, 0, "393:0");
                var c = C(ChoiceKind.Simultaneous, 0, ChoiceSub.BreachAttach);
                c.Options.Add(BreachRules.CardOf(ctx, BreachKind.NimbleFrame)); // 붙일 카드 — 답은 상태에서 읽는다((포켓몬, 칸) — a·b의 두 칸씩)
                return One(c);
            });
            Add("Discard", g2, s =>
            {
                s.Seats[0].FateHand.AddRange(Scene.Top(s, DeckId.ChangeOfFate, 6));
                return One(C(ChoiceKind.Discard, 0));
            });
            Add("Reward/TrainerReward", g2, s =>
            {
                s.Seats[1].Bag.Add(Scene.Item(ctx, "item.secret_key"));
                s.Seats[1].Bag.Add(Scene.Item(ctx, "item.magma_stone"));
                s.Seats[1].FateHand.AddRange(Scene.Top(s, DeckId.ChangeOfFate, 3));
                var c = C(ChoiceKind.Reward, 0, ChoiceSub.TrainerReward);
                c.Subject = 1;
                return One(c);
            });
            Add("Reward/TeachMove", g2, s =>
            {
                Scene.Give(s, ctx, 0, "387:0");
                Scene.Give(s, ctx, 0, "390:0");
                var c = C(ChoiceKind.Reward, 0, ChoiceSub.TeachMove);
                c.Subject = Scene.Move(ctx, "razor_leaf");
                return One(c);
            });
            Add("Reward/PartyFull", g2, s =>
            {
                for (int i = 0; i < 3; i++) Scene.Give(s, ctx, 0, "399:0");
                var c = C(ChoiceKind.Reward, 0, ChoiceSub.PartyFull);
                c.Subject = PokemonRules.Spawn(s, Scene.Def(ctx, "396:0"), 0, (int)DeckId.Weak);
                return One(c);
            });
            Add("Reward/EvolveBranch", g2, s =>
            {
                var c = C(ChoiceKind.Reward, 0, ChoiceSub.EvolveBranch);
                c.Options.AddRange(ctx.Cards.Pokemon[Scene.Def(ctx, "387:0")].EvolveInto);
                return One(c);
            });
            Add("Reward/EvolveTeach", g2, s =>
            {
                var c = C(ChoiceKind.Reward, 0, ChoiceSub.EvolveTeach);
                c.Subject = Scene.Move(ctx, "razor_leaf");
                c.Options.Add(Scene.Give(s, ctx, 0, "388:0"));
                return One(c);
            });
            Add("Reward/SharedBlessing", () => Scene.Game(ctx, 4), s => One(C(ChoiceKind.Reward, 2, ChoiceSub.SharedBlessing)));
            Add("Reward/CoralColor", g2, s =>
            {
                var c = C(ChoiceKind.Reward, 0, ChoiceSub.CoralColor);
                c.Options.AddRange(new[] { (int)ApricornColor.Green, (int)ApricornColor.Red, (int)ApricornColor.Pink });
                c.Count = 2;
                return One(c);
            });
            Add("Ending/EndingChoice", g2, s => One(C(ChoiceKind.Ending, 0, ChoiceSub.EndingChoice)));
            Add("Ending/TraitorOrder", () => Scene.Game(ctx, 4), s =>
            {
                // 답하면 배신자 연전이 시작된다(M5 「막 뼈대」) — 배신자와 상대마다 포켓몬이 있어야 한다
                s.Hos.Traitor = 1;
                for (int i = 0; i < 4; i++) Scene.Give(s, ctx, i, "387:0");
                var c = C(ChoiceKind.Ending, 1, ChoiceSub.TraitorOrder);
                c.Options.AddRange(new[] { 2, 3, 0 });
                return One(c);
            });
            return cases;
        }

        [Test]
        public void M3_선택종류마다Default합법()
        {
            var kinds = new HashSet<ChoiceKind>();
            var subs = new HashSet<ChoiceSub>();
            foreach (var k in Cases())
            {
                var s = k.Make();
                var log = new EventLog();
                var group = k.Choices(s);
                var fx = Scene.Fx(s, log);
                if (group.Count == 1) ChoiceRules.Open(s, ctx, fx, group[0]);
                else ChoiceRules.OpenGroup(s, ctx, fx, group);
                foreach (var c in group)
                {
                    Assert.AreEqual(ChoiceRules.ClockOf(c.Kind), c.Clock, k.Name);
                    var legal = Scene.Legal(s, ctx, c);
                    Assert.Greater(legal.Count, 0, k.Name);
                    Assert.IsTrue(ChoiceRules.Contains(legal, c.Default), $"{k.Name}: Default {c.Default} ∉ Legal");
                    kinds.Add(c.Kind);
                    if (c.Sub != ChoiceSub.None) subs.Add(c.Sub);
                }
                ulong before = StateHash.Of(s);
                foreach (var c in group) Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), log), k.Name);
                Assert.AreNotEqual(before, StateHash.Of(s), k.Name);
                foreach (var c in group) Assert.IsNull(ChoiceRules.Find(s, c.Id), $"{k.Name}: 닫히지 않았다");
                int timeouts = 0;
                foreach (var e in log.Events)
                    if ((e is ChoiceAnswered a && a.ByTimeout) || (e is SecretAnswered sa && sa.ByTimeout)) timeouts++;
                Assert.AreEqual(group.Count, timeouts, $"{k.Name}: 시간 초과 표시");
                foreach (var r in log.OfType<ActionStarted>().GetRange(1, group.Count)) Assert.AreEqual(AutoKind.Timeout, r.Auto, k.Name);
            }
            // 창이 여는 반응 둘(응징·운명)은 ReactionTests가 연다 — 여기서는 갈래 목록만 맞춘다
            subs.Add(ChoiceSub.Retribution);
            subs.Add(ChoiceSub.Fate);
            // 결정 72 면 바꾸기 순서는 배틀의 굴림 단계가 연다(쌓인 프레임에서 Legal을 셈) — EffectTests G14_결정72_…
            subs.Add(ChoiceSub.EffectOrder);
            // 효과가 여는 「쓸까」(op Ask)는 기다리는 효과 프레임이 있어야 한다 — EffectBaseTests M5_선택을여는op_…
            subs.Add(ChoiceSub.EffectOption);
            // 매혹적인 스튜를 쓸지는 야생 배틀의 내 굴림이 연다 — CraftBankEvolveTests M5_제작_매혹적인스튜는…
            subs.Add(ChoiceSub.AlluringStew);
            // 기술 효과가 여는 고르기(타입·상태·규토리 색·대상 파티의 다른 포켓몬)는 그 기술의 공격이 연다 — OpEffectTests M5_효과_*
            subs.Add(ChoiceSub.EffectPick);
            // 사당 카드의 고르기는 처리 중인 공물(GameState.Shrine)이 연다 — ShrineTests A19_·S12_ 가 열고 Default ∈ Legal을 본다
            foreach (var sub in new[] { ChoiceSub.ShrinePick, ChoiceSub.ShrineApricorns, ChoiceSub.ShrineStews, ChoiceSub.ShrineExchange, ChoiceSub.ShrineExchangeCard, ChoiceSub.ShrineMove }) subs.Add(sub);
            // 천상의 균열 동률은 공개된 추종자 집계가 연다 — FractureTests A31_3_ 가 열고 Default ∈ Legal을 본다
            subs.Add(ChoiceSub.FractureTie);
            // 부활할 좌석은 모두 탈락한 네크로즈마 배틀이 연다 — UbNecrozmaTests A14_모두탈락… 이 열고 Default ∈ Legal을 본다
            subs.Add(ChoiceSub.Revive);
            // 축복 동의 · 라운드 끝 건너뛰기 물음은 울트라 버스트 여럿 판·라운드 끝이 연다 — UbNecrozmaTests A14_여럿판축복은… · A14_라운드가끝나면… 이 열고 Default ∈ Legal을 본다(결정 84 U1·U2)
            subs.Add(ChoiceSub.Blessing);
            subs.Add(ChoiceSub.UbSkip);
            foreach (ChoiceKind k in Enum.GetValues(typeof(ChoiceKind))) Assert.IsTrue(kinds.Contains(k), $"{k} 뼈대 시험이 없다");
            foreach (ChoiceSub sub in Enum.GetValues(typeof(ChoiceSub)))
                if (sub != ChoiceSub.None) Assert.IsTrue(subs.Contains(sub), $"{sub} 뼈대 시험이 없다");
        }

        [Test]
        public void G9_Legal순서와기본값()
        {
            // 안 함이 맨 앞(G9 ①), 버리기 = 가장 나중에 받은 카드, 공유 축복 = 다음 좌석부터, 산호 색 = 색 표 순서로 앞 n개, 배신자 순서 = 다음 좌석부터
            var s = Scene.Game(ctx, 4);
            var log = new EventLog();
            s.Seats[0].FateHand.AddRange(new[] { 10, 11, 12 });
            var d = Scene.Open(s, ctx, log, C(ChoiceKind.Discard, 0));
            Assert.AreEqual(12, d.Default.Target);

            var sw = Scene.Legal(s, ctx, Scene.Open(s, ctx, log, C(ChoiceKind.HiddenSpot, 0)));
            Assert.AreEqual(AnswerKind.Decline, sw[0].Kind);

            var sb = Scene.Open(s, ctx, log, C(ChoiceKind.Reward, 2, ChoiceSub.SharedBlessing));
            CollectionAssert.AreEqual(new[] { 3, 0, 1 }, Targets(Scene.Legal(s, ctx, sb)));
            Assert.AreEqual(3, sb.Default.Target);

            var coral = C(ChoiceKind.Reward, 0, ChoiceSub.CoralColor);
            coral.Options.AddRange(new[] { (int)ApricornColor.Green, (int)ApricornColor.Red, (int)ApricornColor.Pink });
            coral.Count = 2;
            Scene.Open(s, ctx, log, coral);
            CollectionAssert.AreEqual(new[] { (int)ApricornColor.Red, (int)ApricornColor.Pink }, coral.Default.Values);

            var tr = C(ChoiceKind.Ending, 1, ChoiceSub.TraitorOrder);
            tr.Options.AddRange(new[] { 2, 3, 0 });
            Scene.Open(s, ctx, log, tr);
            Assert.AreEqual(6, Scene.Legal(s, ctx, tr).Count);
            CollectionAssert.AreEqual(new[] { 2, 3, 0 }, tr.Default.Values);

            var lineup = C(ChoiceKind.Lineup, SeatId.Galactic);
            lineup.Options.AddRange(new[] { 50, 51, 52, 53, 54 });
            lineup.Count = 2;
            Scene.Open(s, ctx, log, lineup);
            Assert.AreEqual(10, Scene.Legal(s, ctx, lineup).Count, "C(5,2)");
            CollectionAssert.AreEqual(new[] { 0, 1 }, lineup.Default.Values, "명단 데이터 순서로 앞 k마리(G9)");

            // 셋업 고르기 Default는 엔진 Rng로 무작위: 같은 시드면 같다
            var a = Scene.Game(ctx, 2, 99);
            var b = Scene.Game(ctx, 2, 99);
            var va = Scene.Open(a, ctx, null, C(ChoiceKind.VillagePick, 0));
            var vb = Scene.Open(b, ctx, null, C(ChoiceKind.VillagePick, 0));
            Assert.IsTrue(va.Default.SameAs(vb.Default));
            Assert.AreEqual(StateHash.Of(a), StateHash.Of(b));
        }

        static int[] Targets(List<ChoiceAnswer> l)
        {
            var r = new int[l.Count];
            for (int i = 0; i < r.Length; i++) r[i] = l[i].Target;
            return r;
        }

        [Test]
        public void M3_BattleAction은원칙기술이Default()
        {
            // p.25 원칙(01 §9-6): 시그니처 값이 최댓값 이상이면 시그니처. 모부기(풀) 대 빈 상대 → 잎날가르기
            var s = Scene.Game(ctx, 2);
            int t = Scene.Give(s, ctx, 0, "387:0");
            int foe = Scene.Spawn(s, ctx, "393:0", SeatId.Wild); // 물 → 풀 약점 +2
            Scene.Battle(s, BattleKind.Wild, 0, SeatId.Wild, foe);
            var c = Scene.Open(s, ctx, null, C(ChoiceKind.BattleAction, 0));
            Assert.AreEqual(AnswerKind.UseMove, c.Default.Kind);
            Assert.AreEqual(ctx.Cards.Pokemon[s.Pokemon[t].Def].Signature, c.Default.Target);
            PrincipleRules.Move(ctx, s.Pokemon[t], s.Pokemon[foe], out var why);
            Assert.AreEqual(AiRule.Signature, why.Rule);
            Assert.AreEqual(PokemonRules.AttackStrength(ctx, c.Default.Target), why.Power);
            Assert.AreEqual(2, why.Bonus, "물 상대에 풀 기술 +2");

            // 볼이 없으면 포획 답이 없고, 이번 배틀에서 기절한 포켓몬이 없으면 기권이 없다(01 §6-5, rulings G13)
            var legal = Scene.Legal(s, ctx, c);
            Assert.IsFalse(legal.Exists(x => x.Kind == AnswerKind.ThrowBall || x.Kind == AnswerKind.Forfeit));
            s.Seats[0].Balls[(int)Ball.Greater] = 2;
            int t2 = Scene.Give(s, ctx, 0, "390:0");
            s.Pokemon[t2].Damage = 99;
            Assert.IsFalse(Scene.Legal(s, ctx, c).Exists(x => x.Kind == AnswerKind.Forfeit), "배틀 밖에서 기절한 포켓몬은 세지 않는다");
            s.Battle.Sides[0].FaintedThisBattle = true;
            legal = Scene.Legal(s, ctx, c);
            Assert.IsTrue(legal.Exists(x => x.Kind == AnswerKind.ThrowBall && x.Target == (int)Ball.Greater));
            Assert.AreEqual(AnswerKind.Forfeit, legal[legal.Count - 1].Kind);
            s.Battle.Kind = BattleKind.Galactic;
            Assert.IsFalse(Scene.Legal(s, ctx, c).Exists(x => x.Kind == AnswerKind.ThrowBall), "갤럭시단 배틀은 포획 불가");
        }

        [Test]
        public void M3_명령4종과거절()
        {
            var s = Scene.Game(ctx, 2);
            var log = new EventLog();
            var c = Scene.Open(s, ctx, log, C(ChoiceKind.HiddenSpot, 0));
            ulong h = StateHash.Of(s);
            int n = log.Events.Count;
            Scene.Reject(Engine.Submit(s, ctx, 1, Command.AnswerTo(c.Id, ChoiceAnswer.Of(AnswerKind.Accept)), log), RejectReason.NotYourChoice, "남의 선택");
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.AnswerTo(c.Id + 100, ChoiceAnswer.Of(AnswerKind.Accept)), log), RejectReason.NoSuchChoice, "없는 Id");
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.AnswerTo(c.Id, ChoiceAnswer.Pick(3)), log), RejectReason.Illegal, "Legal 밖");
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.AnswerTo(c.Id, null), log), RejectReason.BadCommand, "빈 답");
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.TimeoutOf(c.Id), log), RejectReason.NotYourChoice, "Timeout은 세션만");
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.Host(HostOpKind.MarkAway, 1), log), RejectReason.NotYourChoice, "HostOp는 세션만");
            Scene.Reject(Engine.Submit(s, ctx, 0, null, log), RejectReason.BadCommand, "null");
            Assert.AreEqual(h, StateHash.Of(s), "거절은 상태를 바꾸지 않는다");
            Assert.AreEqual(n, log.Events.Count, "거절은 이벤트가 아니다(04 12행)");

            // HostOp: 상태는 그대로, 로그 위치(CommandCount)만 늘고 이벤트·Seq 없음
            int seq = s.NextSeq;
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.Host(HostOpKind.MarkAway, 1), log));
            Assert.AreEqual(1, s.CommandCount);
            Assert.AreEqual(seq, s.NextSeq);
            Assert.AreEqual(n, log.Events.Count);

            // Answer
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(c.Id, ChoiceAnswer.Of(AnswerKind.Accept)), log));
            Assert.AreEqual(2, s.CommandCount);
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.AnswerTo(c.Id, ChoiceAnswer.Of(AnswerKind.Accept)), log), RejectReason.NoSuchChoice, "두 번 답");

            // 끝난 게임
            var c2 = Scene.Open(s, ctx, log, C(ChoiceKind.HiddenSpot, 0));
            s.Phase = Phase.GameOver;
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.AnswerTo(c2.Id, ChoiceAnswer.Of(AnswerKind.Decline)), log), RejectReason.GameOver, "게임 끝");
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.Host(HostOpKind.Kick, 1), log), "끝나도 HostOp는 받는다");

            // 보기 사본에는 Submit하지 않는다
            Assert.Throws<InvalidOperationException>(() => Engine.Submit(Engine.ViewFor(s, 0), ctx, 0, Command.Host(HostOpKind.Kick, 1), log));
        }

        [Test]
        public void M3_동시선택은다답해야공개()
        {
            var s = Scene.Game(ctx, 3);
            var log = new EventLog();
            var group = new List<PendingChoice>();
            for (int i = 0; i < 3; i++)
            {
                var c = C(ChoiceKind.Simultaneous, i, ChoiceSub.Follower);
                c.Secret = true;
                group.Add(c);
            }
            ChoiceRules.OpenGroup(s, ctx, Scene.Fx(s, log), group);
            Assert.AreEqual(group[0].Id, group[2].GroupId);

            Scene.Accept(Engine.Submit(s, ctx, 2, Command.AnswerTo(group[2].Id, ChoiceAnswer.Pick((int)SinnohGod.Giratina)), log));
            Scene.Reject(Engine.Submit(s, ctx, 2, Command.AnswerTo(group[2].Id, ChoiceAnswer.Pick((int)SinnohGod.Dialga)), log), RejectReason.AlreadyAnswered, "이미 답함");
            Assert.AreEqual(3, s.Pending.Count, "묶음이 다 답할 때까지 열려 있다");
            Assert.AreEqual(1, log.OfType<SecretAnswered>().Count);
            Assert.AreEqual(0, log.OfType<SecretRevealed>().Count);
            Assert.AreEqual(0, log.OfType<ChoiceAnswered>().Count, "비공개 답은 ChoiceAnswered로 내지 않는다");

            // 남의 보기에서 받아 둔 답이 가려진다
            var v0 = Engine.ViewFor(s, 0);
            Assert.IsTrue(ChoiceRules.Find(v0, group[2].Id).Answered, "고름 ✔은 보인다");
            Assert.AreEqual(Engine.Unknown, ChoiceRules.Find(v0, group[2].Id).Answer.Target);
            Assert.AreEqual((int)SinnohGod.Giratina, ChoiceRules.Find(Engine.ViewFor(s, 2), group[2].Id).Answer.Target);

            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(group[0].Id, ChoiceAnswer.Pick((int)SinnohGod.Palkia)), log));
            Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(group[1].Id), log));
            Assert.AreEqual(0, s.Pending.Count);
            var rev = log.OfType<SecretRevealed>();
            Assert.AreEqual(1, rev.Count);
            Assert.AreEqual(group[0].GroupId, rev[0].Group);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, new[] { rev[0].Answers[0].Seat, rev[0].Answers[1].Seat, rev[0].Answers[2].Seat });
            Assert.AreEqual((int)SinnohGod.Palkia, rev[0].Answers[0].Value.Target);
            Assert.AreEqual((int)SinnohGod.Giratina, rev[0].Answers[2].Value.Target);
            var secret = log.OfType<SecretAnswered>();
            Assert.AreEqual(3, secret.Count);
            Assert.IsTrue(secret[2].ByTimeout);
        }

        [Test]
        public void M3_답의행동경계와이어짐()
        {
            var s = Scene.Game(ctx, 2);
            var log = new EventLog();
            var fx = Scene.Fx(s, log, 0, ActionKind.Search); // 찾아보기 행동
            int searchSeq = fx.Seq;
            var pick = C(ChoiceKind.EncounterPick, 0);
            pick.Options.AddRange(Scene.Top(s, DeckId.Weak, 3));
            ChoiceRules.Open(s, ctx, fx, pick);
            var map = Scene.Open(s, ctx, log, C(ChoiceKind.MapAction, 1));
            int mapFrom = map.FromSeq;
            log.Clear();

            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(pick.Id, ChoiceAnswer.Pick(pick.Options[1], 1)), log));
            var a = (ActionStarted)log.Events[0];
            Assert.AreEqual(ActionKind.EncounterPick, a.Kind);
            Assert.AreEqual(AutoKind.Human, a.Auto);
            Assert.AreEqual(searchSeq, a.Continues, "찾아보기 → 조우 고르기(01 §3-6 ①-2)");
            Assert.AreEqual(-1, a.ReactsTo);
            Assert.AreEqual(0, a.Index);
            var ans = (ChoiceAnswered)log.Events[1];
            Assert.AreEqual(a.Seq, ans.Seq);
            Assert.AreEqual(1, ans.Index);
            Assert.AreEqual(0, ans.Seat);

            log.Clear();
            Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(map.Id, ChoiceAnswer.Of(AnswerKind.EndTurn)), log));
            var b = (ActionStarted)log.Events[0];
            Assert.AreEqual(ActionKind.EndTurn, b.Kind, "MapAction이면 답 종류");
            Assert.AreEqual(-1, b.Continues, "MapAction은 잇지 않는다");
            Assert.Greater(b.Seq, a.Seq);
            Assert.AreNotEqual(mapFrom, b.Continues);

            // AI 답: Auto = Ai, ChoiceAnswered.ByAi·Reason(표시 전용 — 해시는 사람 답과 같다)
            var s1 = Scene.Game(ctx, 2);
            var s2 = Scene.Game(ctx, 2);
            var c1 = Scene.Open(s1, ctx, null, C(ChoiceKind.HiddenSpot, 0));
            var c2 = Scene.Open(s2, ctx, null, C(ChoiceKind.HiddenSpot, 0));
            var l1 = new EventLog();
            Scene.Accept(Engine.Submit(s1, ctx, 0, Command.AiAnswer(c1.Id, ChoiceAnswer.Of(AnswerKind.Accept), new AiReason(AiRule.BestValue, 3, 1)), l1));
            Scene.Accept(Engine.Submit(s2, ctx, 0, Command.AnswerTo(c2.Id, ChoiceAnswer.Of(AnswerKind.Accept)), null));
            Assert.AreEqual(StateHash.Of(s1), StateHash.Of(s2), "AI 사유는 상태에 들어가지 않는다(01 §3-6 ⑨)");
            Assert.AreEqual(AutoKind.Ai, ((ActionStarted)l1.Events[0]).Auto);
            var ai = l1.OfType<ChoiceAnswered>()[0];
            Assert.IsTrue(ai.ByAi);
            Assert.AreEqual(3, ai.Reason.Power);
        }

        [Test]
        public void M3_이벤트없이도같은상태()
        {
            // sink null(AI 탐색)이어도 Seq·선택 Id·상태가 같다(01 §3-1, Emitter)
            ulong Run(IEventSink sink)
            {
                var s = Scene.Game(ctx, 3, 5);
                var group = new List<PendingChoice>();
                for (int i = 0; i < 3; i++)
                {
                    var c = C(ChoiceKind.Simultaneous, i, ChoiceSub.Follower);
                    c.Secret = true;
                    group.Add(c);
                }
                ChoiceRules.OpenGroup(s, ctx, Scene.Fx(s, sink), group);
                foreach (var c in group) Scene.Accept(Engine.Submit(s, ctx, SeatId.System, Command.TimeoutOf(c.Id), sink));
                var hs = Scene.Open(s, ctx, sink, C(ChoiceKind.HiddenSpot, 1));
                Scene.Accept(Engine.Submit(s, ctx, 1, Command.AnswerTo(hs.Id, ChoiceAnswer.Of(AnswerKind.Accept)), sink));
                return StateHash.Of(s);
            }
            Assert.AreEqual(Run(new EventLog()), Run(null));
        }
    }
}
