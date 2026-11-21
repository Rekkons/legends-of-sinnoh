using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M3-c 정비(언제든 행동, 01 §3-4, 결정 15): 절대형·NoChange(G8), 시점 등급·대상(G10), 정비가 선택지를 바꾸면 ChoiceReissued.
    /// </summary>
    public class MaintenanceTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static Command Party(params int[] order)
        {
            var m = new Maintenance { Kind = MaintKind.PartyOrder };
            m.Values.AddRange(order);
            return Command.Anytime(m);
        }

        static Command SetMove(int p, int slot, int move) => Command.Anytime(new Maintenance { Kind = MaintKind.SetMove, Pokemon = p, Slot = slot, Value = move });

        /// <summary>놓아주기 = 파티에 없음, 대상 확인 값 = 그 포켓몬의 종(rulings G8).</summary>
        static Command Release(GameState s, int p) => Command.Anytime(new Maintenance { Kind = MaintKind.Release, Pokemon = p, Value = s.Pokemon[p].Def });

        static Command Hold(int p, int item) => Command.Anytime(new Maintenance { Kind = MaintKind.HoldItem, Pokemon = p, Value = item });

        [Test]
        public void G8_같은정비명령두번이면두번째는NoChange()
        {
            var s = Scene.Game(ctx, 2);
            int a = Scene.Give(s, ctx, 0, "387:0");
            int b = Scene.Give(s, ctx, 0, "390:0");
            int c = Scene.Give(s, ctx, 0, "393:0");
            var log = new EventLog();
            Scene.Accept(Engine.Submit(s, ctx, 0, Party(c, a, b), log));
            CollectionAssert.AreEqual(new[] { c, a, b, -1, -1, -1 }, s.Seats[0].Party);
            var ev = log.OfType<PartyReordered>();
            Assert.AreEqual(1, ev.Count);
            CollectionAssert.AreEqual(new[] { a, b, c }, ev[0].Before);
            CollectionAssert.AreEqual(new[] { c, a, b }, ev[0].After);
            Assert.AreEqual(ActionKind.PartyOrder, log.OfType<ActionStarted>()[0].Kind);

            ulong h = StateHash.Of(s);
            int n = log.Events.Count;
            Scene.Reject(Engine.Submit(s, ctx, 0, Party(c, a, b), log), RejectReason.NoChange, "두 번째");
            Assert.AreEqual(h, StateHash.Of(s), "NoChange는 로그·Seq·이벤트가 없다(G8)");
            Assert.AreEqual(n, log.Events.Count);

            Scene.Reject(Engine.Submit(s, ctx, 0, Party(c, a), log), RejectReason.Illegal, "순열이 아님");
            Scene.Reject(Engine.Submit(s, ctx, 1, Party(c, a, b), log), RejectReason.Illegal, "남의 파티");
            Scene.Reject(Engine.Submit(s, ctx, 6, Party(c, a, b), log), RejectReason.BadCommand, "좌석 범위 밖");
            Scene.Reject(Engine.Submit(s, ctx, SeatId.System, Party(c, a, b), log), RejectReason.BadCommand, "세션은 정비하지 않는다");

            // 같은 값 기술 칸·아이템도 NoChange
            Scene.Reject(Engine.Submit(s, ctx, 0, SetMove(a, 0, -1), log), RejectReason.NoChange, "빈 칸 = 빈 칸");
            Scene.Reject(Engine.Submit(s, ctx, 0, Hold(a, -1), log), RejectReason.NoChange, "없음 = 없음");
        }

        GameState InBattle(out int mine, out int foe, RuleOptions opt = null)
        {
            var s = Scene.Game(ctx, 2, 7, opt);
            mine = Scene.Give(s, ctx, 0, "387:0");
            Scene.Give(s, ctx, 0, "390:0");
            foe = Scene.Give(s, ctx, 1, "393:0");
            Scene.Battle(s, BattleKind.Trainer, 0, 1);
            s.Seats[0].StampMoves.Add(Scene.Move(ctx, "stun_spore"));
            s.Seats[1].StampMoves.Add(Scene.Move(ctx, "bubble_beam"));
            return s;
        }

        [Test]
        public void G10_공격처리중에는가르치기막힘()
        {
            var s = InBattle(out int mine, out int foe);
            int spore = Scene.Move(ctx, "stun_spore");
            s.Battle.ActiveSide = 0;
            s.Battle.Step = BattleStep.Roll;
            Scene.Reject(Engine.Submit(s, ctx, 0, SetMove(mine, 0, spore), null), RejectReason.Timing, "내 공격 굴림 중");
            Scene.Reject(Engine.Submit(s, ctx, 0, Party(s.Seats[0].Party[1], mine), null), RejectReason.Timing, "Always 등급 전체");
            // 상대(공격받는 쪽)는 된다
            Scene.Accept(Engine.Submit(s, ctx, 1, SetMove(foe, 0, Scene.Move(ctx, "bubble_beam")), null), "공격받는 쪽");
            // 내 공격이 아닌 단계면 된다
            s.Battle.Step = BattleStep.TurnEnd;
            var log = new EventLog();
            Scene.Accept(Engine.Submit(s, ctx, 0, SetMove(mine, 0, spore), log));
            var t = log.OfType<MoveTaught>()[0];
            Assert.AreEqual(mine, t.PokemonId);
            Assert.AreEqual(0, t.Slot);
            Assert.AreEqual(spore, t.MoveKey);
            Assert.AreEqual(-1, t.ReplacedKey);
            Assert.AreEqual(0, s.Seats[0].StampMoves.Count, "풀에서 꺼냈다");
        }

        [Test]
        public void G10_내배틀차례시작에만이면그때만가르침()
        {
            var s = InBattle(out int mine, out _, new RuleOptions { TeachInBattle = TeachInBattle.OwnBattleTurnStartOnly });
            int spore = Scene.Move(ctx, "stun_spore");
            s.Battle.ActiveSide = 1;
            s.Battle.Step = BattleStep.SelectAction;
            Scene.Reject(Engine.Submit(s, ctx, 0, SetMove(mine, 1, spore), null), RejectReason.Timing, "상대 차례");
            s.Battle.ActiveSide = 0;
            s.Battle.Step = BattleStep.TurnEnd;
            Scene.Reject(Engine.Submit(s, ctx, 0, SetMove(mine, 1, spore), null), RejectReason.Timing, "내 차례지만 시작이 아님");
            Scene.Accept(Engine.Submit(s, ctx, 0, Party(s.Seats[0].Party[1], mine), null), "파티 순서는 이 옵션과 무관");
            s.Battle.Step = BattleStep.SelectAction;
            Scene.Accept(Engine.Submit(s, ctx, 0, SetMove(mine, 1, spore), null), "내 배틀 차례 시작");

            // 기본값(BlockDuringAttack)이면 상대 차례에도 된다
            var d = InBattle(out int m2, out _);
            d.Battle.ActiveSide = 1;
            d.Battle.Step = BattleStep.Roll;
            Scene.Accept(Engine.Submit(d, ctx, 0, SetMove(m2, 1, spore), null));
        }

        [Test]
        public void G10_스탬프기술타입제한_끄면아무타입()
        {
            int ember = Scene.Move(ctx, "ember"); // 불꽃: 모부기(풀·땅·악·노말)는 못 배움
            var s = Scene.Game(ctx, 2);
            int t = Scene.Give(s, ctx, 0, "387:0");
            s.Seats[0].StampMoves.Add(ember);
            Scene.Reject(Engine.Submit(s, ctx, 0, SetMove(t, 0, ember), null), RejectReason.Illegal, "타입 제한(기본 켬)");
            Scene.Reject(Engine.Submit(s, ctx, 0, SetMove(t, 0, Scene.Move(ctx, "stun_spore")), null), RejectReason.Illegal, "풀에 없는 기술");

            var o = Scene.Game(ctx, 2, 7, new RuleOptions { StampMoveTypeLimit = false });
            int t2 = Scene.Give(o, ctx, 0, "387:0");
            o.Seats[0].StampMoves.Add(ember);
            Scene.Accept(Engine.Submit(o, ctx, 0, SetMove(t2, 1, ember), null), "끄면 아무 타입");
            Assert.AreEqual(ember, o.Pokemon[t2].Moves[1]);

            // 버리기: 밀려난 기술은 풀로 돌아가지 않는다
            var log = new EventLog();
            Scene.Accept(Engine.Submit(o, ctx, 0, SetMove(t2, 1, -1), log));
            var f = log.OfType<MoveForgotten>()[0];
            Assert.AreEqual(ember, f.MoveKey);
            Assert.AreEqual(1, f.Slot);
            Assert.AreEqual(0, o.Seats[0].StampMoves.Count);
            Assert.AreEqual(-1, o.Pokemon[t2].Moves[1]);
        }

        [Test]
        public void G10_배틀중놓아주기막힘_마지막한마리불가()
        {
            var s = InBattle(out int mine, out _);
            Scene.Reject(Engine.Submit(s, ctx, 0, Release(s, mine), null), RejectReason.Timing, "배틀 참가 중(U15-4)");
            Scene.Reject(Engine.Submit(s, ctx, 0, Hold(mine, Scene.Item(ctx, "item.choice_band")), null), RejectReason.Timing, "아이템도 배틀 밖에서만");
            // 놓아주기는 남의 배틀 중에도 막힌다(사용자 결정 84 T10 ③ 나 — 옛 U15-3 「남의 배틀 중에는 된다」를 바꿈). 아이템은 그대로 된다
            var t = Scene.Game(ctx, 3);
            Scene.Give(t, ctx, 0, "387:0");
            Scene.Give(t, ctx, 1, "390:0");
            int x = Scene.Give(t, ctx, 2, "393:0");
            int y = Scene.Give(t, ctx, 2, "399:0");
            Scene.Battle(t, BattleKind.Trainer, 0, 1);
            Scene.Reject(Engine.Submit(t, ctx, 2, Release(t, x), null), RejectReason.Timing, "남의 배틀 중");
            t.Battle = null;
            Scene.Accept(Engine.Submit(t, ctx, 2, Release(t, x), null), "배틀이 없으면 된다");
            Scene.Reject(Engine.Submit(t, ctx, 2, Release(t, y), null), RejectReason.Illegal, "마지막 1마리(p.27)");
        }

        [Test]
        public void G10_놓아주면카드는버림더미_아이템은가방()
        {
            var s = Scene.Game(ctx, 2);
            int a = Scene.Give(s, ctx, 0, "387:0");
            int b = Scene.Give(s, ctx, 0, "390:0");
            int c = Scene.Give(s, ctx, 0, "393:0");
            int band = Scene.Item(ctx, "item.choice_band");
            s.Pokemon[b].HeldItem = band;
            s.Pokemon[b].Moves[1] = Scene.Move(ctx, "ember");
            int def = s.Pokemon[b].Def;
            int discards = s.Discards[(int)DeckId.Weak].Count;
            var log = new EventLog();
            Scene.Accept(Engine.Submit(s, ctx, 0, Release(s, b), log));
            CollectionAssert.AreEqual(new[] { a, c, -1, -1, -1, -1 }, s.Seats[0].Party, "뒤를 당긴다");
            Assert.AreEqual(discards + 1, s.Discards[(int)DeckId.Weak].Count);
            Assert.AreEqual(def, s.Discards[(int)DeckId.Weak][discards]);
            CollectionAssert.Contains(s.Seats[0].Bag, band);
            Assert.AreEqual(-1, s.Pokemon[b].Def, "인스턴스 자리를 비웠다");
            var held = log.OfType<ItemHeld>()[0];
            Assert.AreEqual(-1, held.PokemonId, "가방으로");
            Assert.AreEqual(b, held.FromPokemonId);
            var r = log.OfType<Released>()[0];
            Assert.AreEqual(b, r.PokemonId);
            Assert.AreEqual(def, r.Species);
            CollectionAssert.AreEqual(new[] { Scene.Move(ctx, "ember") }, r.MovesToDiscard);
            Assert.AreEqual(b, PokemonRules.Spawn(s, def, 0, (int)DeckId.Weak), "빈 자리를 다시 쓴다");
        }

        [Test]
        public void G8_놓아주기두번이면두번째는NoChange_Id를다시써도()
        {
            var s = Scene.Game(ctx, 2);
            int a = Scene.Give(s, ctx, 0, "387:0");
            int b = Scene.Give(s, ctx, 0, "390:0");
            Scene.Give(s, ctx, 0, "393:0");
            var cmd = Release(s, b);
            Scene.Accept(Engine.Submit(s, ctx, 0, cmd, null), "첫 번째");
            ulong h = StateHash.Of(s);
            Scene.Reject(Engine.Submit(s, ctx, 0, cmd, null), RejectReason.NoChange, "같은 명령 두 번째는 결과가 이미 같다(G8)");
            Assert.AreEqual(h, StateHash.Of(s));
            // 놓아준 Id를 다음 포켓몬이 다시 써도, 늦게 온 같은 명령은 그 포켓몬을 놓아주지 않는다(종이 다르다)
            int again = Scene.Give(s, ctx, 0, "399:0");
            Assert.AreEqual(b, again, "빈 자리를 다시 쓴다");
            Scene.Reject(Engine.Submit(s, ctx, 0, cmd, null), RejectReason.NoChange, "Id를 다시 쓴 뒤 늦게 온 명령");
            Assert.AreEqual(3, PokemonRules.PartyCount(s.Seats[0]));
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.Anytime(new Maintenance { Kind = MaintKind.Release, Pokemon = a }), null), RejectReason.BadCommand, "대상 확인 값(종)이 없다");
        }

        [Test]
        public void G10_조우고르기_도전응답_파티가득중에는놓아주기막힘()
        {
            // ① 조우 고르기: 파티 [기절, 성한] — 열린 동안 성한 마지막 포켓몬을 놓아주면 답한 뒤 배틀을 시작할 수 없다(검토 재현: SendIn 예외)
            var s = Scene.Game(ctx, 2);
            int down = Scene.Give(s, ctx, 0, "387:0");
            int ok = Scene.Give(s, ctx, 0, "390:0");
            s.Pokemon[down].Damage = 99;
            int o1 = Scene.Give(s, ctx, 1, "393:0");
            Scene.Give(s, ctx, 1, "399:0");
            var pick = new PendingChoice { Kind = ChoiceKind.EncounterPick, Seat = 0 };
            pick.Options.Add(Scene.Def(ctx, "399:0"));
            Scene.Open(s, ctx, null, pick);
            Scene.Reject(Engine.Submit(s, ctx, 0, Release(s, ok), null), RejectReason.Timing, "조우 고르기 중");
            Scene.Reject(Engine.Submit(s, ctx, 0, Release(s, down), null), RejectReason.Timing, "그 선택이 열린 동안은 다 막는다");
            Scene.Accept(Engine.Submit(s, ctx, 1, Release(s, o1), null), "다른 좌석은 된다");
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(pick.Id, pick.Default.Clone()), null), "고르면 배틀이 시작된다");
            Assert.IsNotNull(s.Battle);

            // ② 도전 응답(마을 안): 건 좌석(Subject)과 받은 좌석 모두 막는다
            var t = Scene.Game(ctx, 3);
            int[] first = new int[3];
            for (int seat = 0; seat < 3; seat++)
            {
                first[seat] = Scene.Give(t, ctx, seat, "387:0");
                Scene.Give(t, ctx, seat, "390:0");
            }
            Scene.Open(t, ctx, null, new PendingChoice { Kind = ChoiceKind.Respond, Sub = ChoiceSub.Challenge, Seat = 1, Subject = 0 });
            Scene.Reject(Engine.Submit(t, ctx, 0, Release(t, first[0]), null), RejectReason.Timing, "건 좌석");
            Scene.Reject(Engine.Submit(t, ctx, 1, Release(t, first[1]), null), RejectReason.Timing, "받은 좌석");
            Scene.Accept(Engine.Submit(t, ctx, 2, Release(t, first[2]), null), "도전과 무관한 좌석");

            // ③ 파티 가득(S51): 빈 칸이 생겨도 선택이 그대로라 두 마리를 잃거나 새 포켓몬을 버린다(검토 재현) → 막는다
            var u = Scene.Game(ctx, 2);
            int p0 = Scene.Give(u, ctx, 0, "387:0");
            Scene.Give(u, ctx, 0, "390:0");
            Scene.Give(u, ctx, 0, "393:0");
            int caught = Scene.Spawn(u, ctx, "399:0", 0);
            var full = Scene.Open(u, ctx, null, new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.PartyFull, Seat = 0, Subject = caught, Window = Window.BattleEnd });
            Scene.Reject(Engine.Submit(u, ctx, 0, Release(u, p0), null), RejectReason.Timing, "파티 가득 놓아주기 중");
            Scene.Accept(Engine.Submit(u, ctx, 0, Command.AnswerTo(full.Id, ChoiceAnswer.Pick(p0)), null), "S51에서 놓아준다");
            Assert.AreEqual(caught, u.Seats[0].Party[2]);
            Scene.Accept(Engine.Submit(u, ctx, 0, Release(u, caught), null), "답한 뒤에는 다시 정비로 놓아줄 수 있다");
        }

        [Test]
        public void M3_소지아이템옮기기()
        {
            var s = Scene.Game(ctx, 2);
            int a = Scene.Give(s, ctx, 0, "387:0");
            int b = Scene.Give(s, ctx, 0, "390:0");
            int band = Scene.Item(ctx, "item.choice_band");
            int egg = Scene.Item(ctx, "item.lucky_egg");
            s.Seats[0].Bag.Add(band);
            var log = new EventLog();
            Scene.Accept(Engine.Submit(s, ctx, 0, Hold(a, band), log), "가방 → a");
            Assert.AreEqual(band, s.Pokemon[a].HeldItem);
            Assert.AreEqual(0, s.Seats[0].Bag.Count);
            Scene.Accept(Engine.Submit(s, ctx, 0, Hold(b, band), log), "a → b");
            Assert.AreEqual(-1, s.Pokemon[a].HeldItem);
            Assert.AreEqual(band, s.Pokemon[b].HeldItem);
            var moved = log.OfType<ItemHeld>();
            Assert.AreEqual(a, moved[1].FromPokemonId);
            Assert.AreEqual(b, moved[1].PokemonId);
            Scene.Reject(Engine.Submit(s, ctx, 0, Hold(a, egg), log), RejectReason.Illegal, "가방에도 파티에도 없다");
            Scene.Reject(Engine.Submit(s, ctx, 0, Hold(a, Scene.Item(ctx, "item.secret_key")), log), RejectReason.Illegal, "소지 아이템이 아니다");
            s.Seats[0].Bag.Add(egg);
            Scene.Accept(Engine.Submit(s, ctx, 0, Hold(b, egg), log), "바꿔 들면 옛 아이템은 가방으로");
            CollectionAssert.AreEqual(new[] { band }, s.Seats[0].Bag);
            Scene.Accept(Engine.Submit(s, ctx, 0, Hold(b, -1), log), "b → 가방");
            CollectionAssert.AreEqual(new[] { band, egg }, s.Seats[0].Bag);
        }

        [Test]
        public void M3_정비가선택지를바꾸면다시엶()
        {
            // 기술 고르기 중에 새 기술을 배우면 BattleAction 선택이 새 Id로 다시 열린다(01 §3-4)
            var s = InBattle(out int mine, out _);
            var log = new EventLog();
            var c = Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.BattleAction, Seat = 0 });
            var other = Scene.Open(s, ctx, log, new PendingChoice { Kind = ChoiceKind.HiddenSpot, Seat = 1 });
            int old = c.Id;
            int otherId = other.Id;
            log.Clear();
            Scene.Accept(Engine.Submit(s, ctx, 0, SetMove(mine, 0, Scene.Move(ctx, "stun_spore")), log));
            Assert.AreNotEqual(old, c.Id);
            Assert.AreEqual(otherId, other.Id, "바뀌지 않은 선택은 그대로");
            Assert.IsNull(ChoiceRules.Find(s, old));
            Assert.AreSame(c, s.Pending[0], "자리를 지킨다");
            var re = log.OfType<ChoiceReissued>();
            Assert.AreEqual(1, re.Count);
            Assert.AreEqual(old, re[0].OldId);
            Assert.AreEqual(c.Id, re[0].NewId);
            var req = log.OfType<ChoiceRequested>();
            Assert.AreEqual(c.Id, req[0].Id);
            Assert.IsTrue(req[0].Legal.Exists(a => a.Kind == AnswerKind.UseMove && a.Target == Scene.Move(ctx, "stun_spore")), "새 기술이 Legal에");
            Scene.Reject(Engine.Submit(s, ctx, 0, Command.AnswerTo(old, ChoiceAnswer.Of(AnswerKind.UseMove, Scene.Move(ctx, "razor_leaf"))), null), RejectReason.NoSuchChoice, "옛 Id");
            Scene.Accept(Engine.Submit(s, ctx, 0, Command.AnswerTo(c.Id, ChoiceAnswer.Of(AnswerKind.UseMove, Scene.Move(ctx, "stun_spore"))), null));

            // 파티 순서를 바꾸면 SendIn Default(파티 순서 첫 포켓몬)가 바뀌어 다시 연다
            var t = Scene.Game(ctx, 2);
            int p0 = Scene.Give(t, ctx, 0, "387:0");
            int p1 = Scene.Give(t, ctx, 0, "390:0");
            int p2 = Scene.Give(t, ctx, 0, "393:0");
            Scene.Battle(t, BattleKind.Wild, 0, SeatId.Wild, Scene.Spawn(t, ctx, "399:0", SeatId.Wild));
            t.Battle.Sides[0].Active = -1;
            t.Pokemon[p0].Damage = 99;
            var send = Scene.Open(t, ctx, null, new PendingChoice { Kind = ChoiceKind.NextPokemon, Seat = 0 });
            Assert.AreEqual(p1, send.Default.Target);
            Scene.Accept(Engine.Submit(t, ctx, 0, Party(p2, p0, p1), null), "남의 배틀이 아닌 내 배틀이지만 Always 등급, 공격 처리 중 아님");
            Assert.AreEqual(p2, send.Default.Target);
        }
    }
}
