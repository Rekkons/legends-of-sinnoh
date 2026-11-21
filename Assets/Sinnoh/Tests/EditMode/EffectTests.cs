using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M3-f 효과 처리기 뼈대(01 §7-2·§7-3, rulings G14): EffectDef 해석기·Stack/EffectFrame·시점 창, 처음 op 어휘(상태이상 걸기·풀기, 공격력·효과 피해·2배,
    /// 회복·반동·피로), 상태이상 기본 효과(S7_6), effects.json 로더, 효과 없는 기술은 피해만(미구현 목록).
    /// 포켓몬: 모부기 387(풀, 체력 6, 우선도 4) · 불꽃숭이 390(불꽃, 4, 6, 불꽃세례) · 비버니 399(노말, 5, 4, 몸통박치기 = 미구현) · 실쿤 266(벌레, 8, 2, 독침 = 미구현) ·
    /// 찌르꼬 396(노말·비행, 4, 6) · 팽도리 393(물, 5, 5) · 프로스트 로토무 479:3(전기·얼음).
    /// </summary>
    public class EffectTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        const AttackDieFace S = AttackDieFace.StatusSymbol;
        const AttackDieFace R = AttackDieFace.RegularHit;
        const AttackDieFace C = AttackDieFace.CriticalHit;
        const AttackDieFace X = AttackDieFace.None;

        int Give(GameState s, int seat, string key) => Scene.Give(s, ctx, seat, key);

        int Mv(string key) => Scene.Move(ctx, key);

        static SourceRef St(StatusEffect x) => new SourceRef(SourceKind.Status, (int)x);

        SourceRef MoveSrc(string key) => new SourceRef(SourceKind.Move, Mv(key));

        /// <summary>손으로 놓은 트레이너전(좌석 0 공격, 좌석 1 받음)에서 창 w의 자동 효과만 돈다. 반환 = 그 상태(로그는 log).</summary>
        GameState RunWindow(EventLog log, string attacker, string defender, int move, AttackDieFace[] faces, Window w, Action<GameState, int, int> prep, out int a, out int d)
        {
            var s = Scene.Game(ctx, 2);
            a = Give(s, 0, attacker);
            d = Give(s, 1, defender);
            var b = Scene.Battle(s, BattleKind.Trainer, 0, 1);
            b.Move = move;
            b.Faces.AddRange(faces);
            prep?.Invoke(s, a, d);
            EffectRules.Trigger(s, ctx, w);
            Assert.IsTrue(EffectRules.Run(s, ctx, Scene.Fx(s, log, 0, ActionKind.BattleAction)), "선택을 여는 op는 아직 없다");
            Assert.AreEqual(0, s.Stack.Count);
            return s;
        }

        /// <summary>
        /// 좌석 0의 attacker(배운 기술 칸 0 = move, null이면 시그니처)가 야생 defender에게 그 기술을 한 번 쓴다. 좌석 0이 먼저여야 한다(빠르거나 동률이면 토큰을 쓴다).
        /// seed = 그 공격 주사위의 난수. 반환 = 그 공격 행동(Seq)의 이벤트.
        /// </summary>
        List<GameEvent> Attack(string attacker, string move, string defender, ulong seed, Action<GameState, int, int> prep, out GameState state, out int a, out int d, EventLog all = null)
        {
            var s = Battles.Game(ctx, 2);
            a = Give(s, 0, attacker);
            d = Scene.Spawn(s, ctx, defender, SeatId.Wild);
            int m = move != null ? Mv(move) : ctx.Cards.Pokemon[s.Pokemon[a].Def].Signature;
            if (move != null) s.Pokemon[a].Moves[0] = m;
            prep?.Invoke(s, a, d);
            var log = all ?? new EventLog();
            Battles.Wild(s, ctx, log, 0, d);
            if (s.Pending.Count == 1 && s.Pending[0].Sub == ChoiceSub.InitiativeToken)
                Battles.Answer(s, ctx, s.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), log);
            s.Rng = new Pcg32(seed);
            int from = log.Events.Count;
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(m), log);
            state = s;
            int seq = log.Events[from].Seq;
            return log.Events.FindAll(e => e.Seq == seq);
        }

        List<GameEvent> Attack(string attacker, string move, string defender, ulong seed, Action<GameState, int, int> prep = null) =>
            Attack(attacker, move, defender, seed, prep, out _, out _, out _);

        static T One<T>(List<GameEvent> events) where T : GameEvent
        {
            var l = events.FindAll(e => e is T);
            Assert.AreEqual(1, l.Count, $"{typeof(T).Name}가 하나가 아니다");
            return (T)l[0];
        }

        static int Count<T>(List<GameEvent> events) where T : GameEvent => events.FindAll(e => e is T).Count;

        static HpChanged HpOf(List<GameEvent> events, int pokemon) => (HpChanged)events.Find(e => e is HpChanged h && h.PokemonId == pokemon);

        static int Crits(List<GameEvent> events) => AttackDice.Count(One<DiceRolled>(events).Faces, AttackDieFace.CriticalHit);

        // ── 상태이상 걸기 ──

        [Test]
        public void S7_6_면역타입과이미걸린상태()
        {
            var s = Scene.Game(ctx, 2);
            var rotom = s.Pokemon[Give(s, 0, "479:3")]; // 전기·얼음
            var turtwig = s.Pokemon[Give(s, 0, "387:0")];
            Assert.AreEqual(StatusResult.Immune, StatusRules.Check(ctx, rotom, StatusEffect.Paralysed), "전기는 마비 면역");
            Assert.AreEqual(StatusResult.Immune, StatusRules.Check(ctx, rotom, StatusEffect.Frozen), "얼음은 빙결 면역");
            Assert.AreEqual(StatusResult.Applied, StatusRules.Inflict(ctx, turtwig, StatusEffect.Paralysed));
            Assert.IsTrue(turtwig.ParalysisPending, "마비되면 다음 공격 표시(G14)");
            Assert.AreEqual(StatusResult.Already, StatusRules.Inflict(ctx, turtwig, StatusEffect.Burned), "이미 상태이상이면 더 안 걸린다(p.9)");
            Assert.AreEqual(StatusEffect.Paralysed, turtwig.Status);
            rotom.Status = StatusEffect.Burned;
            Assert.AreEqual(StatusResult.Immune, StatusRules.Check(ctx, rotom, StatusEffect.Paralysed), "면역이면서 이미 걸림 → Immune(G14)");

            // 실제 기술: 불꽃숭이 불꽃세례(Inflict Burned) → 비버니 걸림 / 불꽃숭이 면역 / 이미 마비인 비버니
            var hit = Attack("390:0", null, "399:0", 1, null, out var t, out _, out int bidoof);
            var applied = One<StatusApplied>(hit);
            Assert.AreEqual(StatusResult.Applied, applied.Result);
            Assert.AreEqual(StatusEffect.Burned, applied.Status);
            Assert.AreEqual(bidoof, applied.PokemonId);
            Assert.IsTrue(applied.Source.Same(MoveSrc("ember")), "출처 = 그 기술");
            Assert.AreEqual(StatusEffect.Burned, t.Pokemon[bidoof].Status, "불꽃세례 2개(최대 4)로는 비버니(5)가 안 쓰러진다");
            Assert.AreEqual(StatusResult.Immune, One<StatusApplied>(Attack("390:0", null, "390:0", 1)).Result, "불꽃은 화상 면역");
            var already = Attack("390:0", null, "399:0", 1, (st, a, d) => st.Pokemon[d].Status = StatusEffect.Paralysed, out var u, out _, out int b2);
            Assert.AreEqual(StatusResult.Already, One<StatusApplied>(already).Result);
            Assert.AreEqual(StatusEffect.Paralysed, u.Pokemon[b2].Status);
        }

        [Test]
        public void G14_이미걸린상태조건은효과전상태로본다()
        {
            // 매지컬플레임(불꽃 3): 「Inflict Burned. If the target is already Burned, double damage」 → 실쿤(벌레, 불꽃 +2)
            const ulong seed = 3;
            var burned = Attack("390:0", "mystical_fire", "266:0", seed, (s, a, d) => s.Pokemon[d].Status = StatusEffect.Burned);
            var fresh = Attack("390:0", "mystical_fire", "266:0", seed);
            int sum = One<DiceRolled>(fresh).Sum;
            Assert.AreEqual(sum, One<DiceRolled>(burned).Sum, "같은 시드 = 같은 주사위");

            var bb = One<DamageBreakdown>(burned);
            var dbl = Array.Find(bb.Steps, x => x.Kind == DamageStepKind.Double);
            Assert.IsNotNull(dbl, "이미 화상이면 2배");
            Assert.AreEqual(sum + 2, dbl.Value, "2배 칩 = 2배로 더한 만큼(주사위 + 상성)");
            Assert.IsTrue(dbl.Source.Same(MoveSrc("mystical_fire")));
            Assert.AreEqual(2 * (sum + 2), bb.Final);
            Assert.AreEqual(StatusResult.Already, One<StatusApplied>(burned).Result);

            var fb = One<DamageBreakdown>(fresh);
            Assert.IsNull(Array.Find(fb.Steps, x => x.Kind == DamageStepKind.Double), "이번 공격이 건 화상으로는 2배가 아니다(G14)");
            Assert.AreEqual(sum + 2, fb.Final);
            Assert.AreEqual(StatusResult.Applied, One<StatusApplied>(fresh).Result);
            Assert.Less(fresh.FindIndex(e => e is DamageBreakdown), fresh.FindIndex(e => e is StatusApplied), "피해 계산이 효과보다 먼저");
            Assert.Less(fresh.FindIndex(e => e is StatusApplied), fresh.FindIndex(e => e is HpChanged), "피해는 효과 뒤에 준다(p.8)");
        }

        [Test]
        public void G14_공격력을n으로올리면다른보정을더한다()
        {
            int sash = Scene.Item(ctx, "item.swiftness_sash");
            // 저리가루(풀 1): 「If the target is already Paralysed, attack strength is increased to 3」. 민첩의 띠 −1은 그 뒤에 더한다
            var para = Attack("387:0", "stun_spore", "266:0", 1, (s, a, d) =>
            {
                s.Pokemon[a].HeldItem = sash;
                s.Pokemon[d].Status = StatusEffect.Paralysed;
            });
            var roll = One<DiceRolled>(para);
            Assert.AreEqual(2, roll.Power, "3 − 1");
            Assert.AreEqual(2, roll.PowerMods.Length);
            Assert.IsTrue(roll.PowerMods[0].Source.Same(MoveSrc("stun_spore")), "공격력 = n은 맨 앞");
            Assert.AreEqual(2, roll.PowerMods[0].Delta, "1 → 3");
            Assert.IsTrue(roll.PowerMods[1].Source.Same(new SourceRef(SourceKind.Item, sash)));
            Assert.AreEqual(StatusResult.Already, One<StatusApplied>(para).Result);

            var all = new EventLog();
            var plain = Attack("387:0", "stun_spore", "266:0", 1, (s, a, d) => s.Pokemon[a].HeldItem = sash, out var t, out _, out int silcoon, all);
            var r2 = One<DiceRolled>(plain);
            Assert.AreEqual(1, r2.Power, "1 − 1 = 0 → 최소 1(S7_3)");
            Assert.AreEqual(1, r2.PowerMods.Length);
            Assert.AreEqual(StatusResult.Applied, One<StatusApplied>(plain).Result);
            // 이어진 실쿤의 공격이 마비 「다음 공격」이었다(일반 타격을 지움)
            Assert.AreEqual(StatusEffect.Paralysed, t.Pokemon[silcoon].Status);
            Assert.IsFalse(t.Pokemon[silcoon].ParalysisPending);
            Assert.IsTrue(all.OfType<DiceFacesChanged>().Exists(x => x.Source.Same(St(StatusEffect.Paralysed))));

            // 「If this move was used last turn, attack strength is decreased to 2」(파괴광선 6): 직전 자기 턴과 같은 기술일 때만
            var s2 = Scene.Game(ctx, 2);
            int att = Give(s2, 0, "387:0");
            int def = Give(s2, 1, "399:0");
            var b = Scene.Battle(s2, BattleKind.Trainer, 0, 1);
            b.Move = Mv("hyper_beam");
            var mods = new List<Mod>();
            EffectRules.StrengthMods(s2, ctx, b.Move, att, def, mods);
            Assert.AreEqual(0, mods.Count, "처음 쓰면 그대로");
            s2.Pokemon[att].LastMove = b.Move;
            EffectRules.StrengthMods(s2, ctx, b.Move, att, def, mods);
            Assert.AreEqual(1, mods.Count);
            Assert.AreEqual(2 - 6, mods[0].Delta);
        }

        [Test]
        public void G14_기술줄의위력은굴릴주사위수와같다()
        {
            // 화면의 기술 줄 「위력」(04 §10-2) = BattleRules.RollPower = 굴림이 쓰는 값: 기술 효과의 「공격력 = n」까지 넣는다. 고르기 전(b.Move가 아직 그 기술이 아니다)에도 같다
            var s = Scene.Game(ctx, 2);
            int att = Give(s, 0, "387:0");
            int def = Give(s, 1, "266:0");
            var b = Scene.Battle(s, BattleKind.Trainer, 0, 1);
            int spore = Mv("stun_spore"), beam = Mv("hyper_beam");
            var mods = new List<Mod>();
            b.Move = -1;
            Assert.AreEqual(1, BattleRules.RollPower(s, ctx, spore, att, def, mods), "저리가루: 카드 값 1");
            Assert.AreEqual(0, mods.Count);
            s.Pokemon[def].Status = StatusEffect.Paralysed;
            Assert.AreEqual(3, BattleRules.RollPower(s, ctx, spore, att, def, mods), "대상이 이미 마비면 3 — 굴림판의 주사위 수");
            Assert.AreEqual(1, mods.Count);
            // 굴림이 같은 값을 쓴다
            var roll = One<DiceRolled>(Attack("387:0", "stun_spore", "266:0", 1, (g, a, d) => g.Pokemon[d].Status = StatusEffect.Paralysed));
            Assert.AreEqual(3, roll.Power);
            Assert.AreEqual(3, roll.Faces.Length);

            // 「지난 턴에 쓴 기술이면 2」: 다른 기술을 쓰던 중(b.Move = 저리가루)에 다음 턴의 줄을 세도 묻는 기술로 본다
            b.Move = spore;
            Assert.AreEqual(6, BattleRules.RollPower(s, ctx, beam, att, def, mods), "파괴광선: 처음이면 6");
            s.Pokemon[att].LastMove = beam;
            Assert.AreEqual(2, BattleRules.RollPower(s, ctx, beam, att, def, mods), "지난 턴에 쓴 파괴광선은 2");
            Assert.AreEqual(3, BattleRules.RollPower(s, ctx, spore, att, def, mods), "다른 기술 줄은 그대로");
            Assert.AreEqual(BattleRules.PlanBStrength, BattleRules.RollPower(s, ctx, BattleRules.PlanB, att, def, mods), "플랜 B");
            Assert.AreEqual(2, BattleRules.RollPower(s, ctx, beam, att, -1, mods), "상대가 아직 없어도 센다(지난 턴 조건은 내 포켓몬만 본다)");
        }

        [Test]
        public void S7_3_효과피해는op마다피해내역단계()
        {
            // 잎날가르기(풀 3): 크리티컬 하나마다 +1 → 실쿤(벌레, 풀 −2)
            ulong seed = Battles.Seed(sd => Crits(Attack("387:0", null, "266:0", sd)) >= 1);
            var leaf = Attack("387:0", null, "266:0", seed);
            int crits = Crits(leaf);
            var br = One<DamageBreakdown>(leaf);
            Assert.AreEqual(DamageStepKind.Dice, br.Steps[0].Kind);
            Assert.AreEqual(DamageStepKind.Effect, br.Steps[1].Kind, "② 효과 피해는 주사위 다음");
            Assert.AreEqual(crits, br.Steps[1].Value);
            Assert.IsTrue(br.Steps[1].Source.Same(MoveSrc("razor_leaf")));
            Assert.AreEqual(DamageStepKind.Effectiveness, br.Steps[2].Kind);
            Assert.AreEqual(Math.Max(0, One<DiceRolled>(leaf).Sum + crits - 2), br.Final);

            // 떨어뜨리기(바위 2): 비행 타입에게 2 더 → 찌르꼬(노말·비행). 불꽃숭이와 우선도 6 동률이라 토큰을 쓴다
            var smack = Attack("390:0", "smack_down", "396:0", 1);
            var sb = One<DamageBreakdown>(smack);
            var extra = Array.Find(sb.Steps, x => x.Kind == DamageStepKind.Effect);
            Assert.IsNotNull(extra);
            Assert.AreEqual(2, extra.Value);
            Assert.AreEqual(One<DiceRolled>(smack).Sum + 2 + 2, sb.Final, "주사위 + 효과 2 + 상성 2(바위 → 비행)");
            var none = One<DamageBreakdown>(Attack("390:0", "smack_down", "399:0", 1));
            Assert.IsNull(Array.Find(none.Steps, x => x.Kind == DamageStepKind.Effect), "비행이 아니면 효과 칩이 없다");
        }

        [Test]
        public void G14_반동과회복은공격피해와동시()
        {
            // 돌진(노말 4): 크리티컬 +2, 크리티컬마다 사용자 1 피해 → 실쿤
            ulong seed = Battles.Seed(sd => Crits(Attack("390:0", "take_down", "266:0", sd)) >= 1);
            var hit = Attack("390:0", "take_down", "266:0", seed, null, out _, out int chim, out int silcoon);
            int crits = Crits(hit);
            var self = HpOf(hit, chim);
            Assert.IsNotNull(self, "반동");
            Assert.IsTrue(self.Source.Same(MoveSrc("take_down")));
            Assert.AreEqual(Math.Max(0, self.Before - crits), self.After);
            Assert.AreEqual(HpOf(hit, silcoon).Index, self.Index, "공격 피해와 같은 묶음(p.13)");
            Assert.AreEqual(2 * crits, Array.Find(One<DamageBreakdown>(hit).Steps, x => x.Kind == DamageStepKind.Effect).Value);

            // 반동으로 쓰러지면 상대가 쓰러뜨린 것이 아니다(ById −1)
            var ko = Attack("390:0", "take_down", "266:0", seed, (s, a, d) => Battles.SetHp(s, ctx, a, 1), out _, out int chim2, out _);
            var fainted = (Fainted)ko.Find(e => e is Fainted f && f.PokemonId == chim2);
            Assert.IsNotNull(fainted);
            Assert.AreEqual(-1, fainted.ById);

            // 드레인키스(페어리 2): 회복 1 + 졸림 → 회복도 같은 묶음
            var kiss = Attack("387:0", "draining_kiss", "266:0", 1, (s, a, d) => Battles.SetHp(s, ctx, a, 3), out var t, out int turtwig, out int target);
            var heal = HpOf(kiss, turtwig);
            Assert.AreEqual(3, heal.Before);
            Assert.AreEqual(4, heal.After);
            Assert.IsTrue(heal.Source.Same(MoveSrc("draining_kiss")));
            Assert.AreEqual(HpOf(kiss, target).Index, heal.Index);
            Assert.AreEqual(StatusEffect.Drowsy, t.Pokemon[target].Status);
        }

        // ── 상태이상 효과 ──

        [Test]
        public void S7_6_마비는다음공격한번만일반타격을지움()
        {
            var log = new EventLog();
            var s = RunWindow(log, "387:0", "399:0", Mv("razor_leaf"), new[] { R, C, R, S }, Window.AfterRoll,
                (st, a, d) => StatusRules.Inflict(ctx, st.Pokemon[a], StatusEffect.Paralysed), out int att, out _);
            var ch = log.OfType<DiceFacesChanged>();
            Assert.AreEqual(1, ch.Count);
            Assert.IsTrue(ch[0].Source.Same(St(StatusEffect.Paralysed)));
            Assert.AreEqual(2, ch[0].Changes.Length);
            Assert.AreEqual(0, ch[0].Changes[0].Index);
            Assert.AreEqual(X, ch[0].Changes[0].After, "지운 주사위는 None");
            Assert.AreEqual(2, ch[0].Changes[1].Index);
            CollectionAssert.AreEqual(new[] { 1, 2, 1 }, ch[0].SrcBefore);
            CollectionAssert.AreEqual(new[] { 1, 0, 1 }, ch[0].SrcAfter);
            CollectionAssert.AreEqual(new[] { X, C, X, S }, s.Battle.Faces);
            Assert.AreEqual(2, AttackDice.Sum(s.Battle.Faces));
            Assert.IsFalse(s.Pokemon[att].ParalysisPending, "다음 공격 한 번에 쓴다");
            Assert.AreEqual(StatusEffect.Paralysed, s.Pokemon[att].Status, "마비는 남는다(우선도 −3)");

            // 그다음 공격은 그대로
            s.Battle.Faces.Clear();
            s.Battle.Faces.AddRange(new[] { R, R });
            var log2 = new EventLog();
            EffectRules.Trigger(s, ctx, Window.AfterRoll);
            Assert.IsTrue(EffectRules.Run(s, ctx, Scene.Fx(s, log2, 0, ActionKind.BattleAction)));
            Assert.AreEqual(0, log2.OfType<DiceFacesChanged>().Count);
            CollectionAssert.AreEqual(new[] { R, R }, s.Battle.Faces);

            // 포획 굴림은 공격이 아니라 일반 타격을 지우지 않는다(원작 커뮤니티 확인).
            // 옛 [가정]은 표시가 다음 공격까지 남았다 → 원작 커뮤니티 확인).
            // 모부기(우선도 4)가 실쿤(2)보다 먼저 나온 뒤 마비된다(이번 턴이 마비된 바로 다음 자기 턴)
            var c = Battles.Game(ctx, 2);
            int mine = Give(c, 0, "387:0");
            c.Seats[0].Balls[(int)Ball.Regular] = 1;
            var clog = new EventLog();
            Battles.Wild(c, ctx, clog, 0, Scene.Spawn(c, ctx, "266:0", SeatId.Wild));
            StatusRules.Inflict(ctx, c.Pokemon[mine], StatusEffect.Paralysed);
            Assert.IsTrue(c.Pokemon[mine].ParalysisPending);
            Battles.Answer(c, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), clog);
            Assert.AreEqual(1, clog.OfType<CaptureAttempt>().Count);
            Assert.IsFalse(c.Pokemon[mine].ParalysisPending, "볼을 던진 턴에 사라진다(원작 커뮤니티 확인)");
            Assert.AreEqual(0, clog.OfType<DiceFacesChanged>().Count);
        }

        /// <summary>모부기(우선도 4, 잎날가르기)가 실쿤(체력 8, 우선도 2)과 야생 배틀을 시작하고, 첫 내 턴이 열린 뒤 마비된다. 볼 1개. move·wild = 배운 칸 0 · 야생(모부기보다 느려야 한다).</summary>
        GameState ParalysedOnMyTurn(EventLog log, out int mine, string move = "razor_leaf", string wild = "266:0")
        {
            var s = Battles.Game(ctx, 2);
            mine = Give(s, 0, "387:0");
            s.Pokemon[mine].Moves[0] = Mv(move);
            s.Seats[0].Balls[(int)Ball.Regular] = 1;
            Battles.Wild(s, ctx, log, 0, Scene.Spawn(s, ctx, wild, SeatId.Wild));
            Assert.AreEqual(0, Battles.Only(s, ChoiceKind.BattleAction).Seat, "모부기가 먼저");
            StatusRules.Inflict(ctx, s.Pokemon[mine], StatusEffect.Paralysed);
            return s;
        }

        /// <summary>이번 행동(마지막 ActionStarted부터)에서 마비가 낸 면 바꾸기.</summary>
        static List<DiceFacesChanged> ParalysisFaces(EventLog log, int from) =>
            log.Events.GetRange(from, log.Events.Count - from).FindAll(e => e is DiceFacesChanged d && d.Source.Same(St(StatusEffect.Paralysed))).ConvertAll(e => (DiceFacesChanged)e);

        [Test]
        public void G14_마비는바로다음자기턴에공격할때만일반타격을지움_볼을던진턴이면사라짐()
        {
            // 원작 커뮤니티 확인
            // ① 바로 다음 자기 턴에 공격하면 지운다
            var log = new EventLog();
            var s = ParalysedOnMyTurn(log, out int mine);
            ulong hit = Battles.Seed(sd =>
            {
                var l = new EventLog();
                var t = ParalysedOnMyTurn(l, out _);
                t.Rng = new Pcg32(sd);
                int f = l.Events.Count;
                Battles.Answer(t, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("razor_leaf")), l);
                var roll = l.Events.GetRange(f, l.Events.Count - f).Find(e => e is DiceRolled) as DiceRolled;
                return roll != null && Array.IndexOf(roll.Faces, R) >= 0;
            });
            s.Rng = new Pcg32(hit);
            int from = log.Events.Count;
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("razor_leaf")), log);
            var removed = ParalysisFaces(log, from);
            Assert.AreEqual(1, removed.Count);
            Assert.That(removed[0].Changes.Length, Is.GreaterThan(0), "일반 타격을 지운다");
            Assert.IsFalse(s.Pokemon[mine].ParalysisPending);

            // ② 그 턴에 볼을 던졌으면(실패) 다음 내 턴의 공격은 그대로 — 우선도 −3만 남는다
            ulong miss = Battles.Seed(sd =>
            {
                var l = new EventLog();
                var t = ParalysedOnMyTurn(l, out _);
                t.Rng = new Pcg32(sd);
                Battles.Answer(t, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), l);
                return t.Battle != null && !l.OfType<CaptureAttempt>()[0].Success;
            });
            var blog = new EventLog();
            var b = ParalysedOnMyTurn(blog, out int m2);
            b.Rng = new Pcg32(miss);
            Battles.Answer(b, ctx, ChoiceKind.BattleAction, ChoiceAnswer.Of(AnswerKind.ThrowBall, (int)Ball.Regular), blog);
            Assert.IsFalse(b.Pokemon[m2].ParalysisPending, "볼을 던진 턴에 사라진다");
            Assert.AreEqual(StatusEffect.Paralysed, b.Pokemon[m2].Status, "마비는 남는다");
            // 실쿤의 턴(기술 하나뿐 — 묻지 않음)이 지나고 다시 내 턴
            Assert.AreEqual(0, Battles.Only(b, ChoiceKind.BattleAction).Seat);
            int bf = blog.Events.Count;
            Battles.Answer(b, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("razor_leaf")), blog);
            Assert.AreEqual(1, blog.Events.GetRange(bf, blog.Events.Count - bf).FindAll(e => e is DiceRolled r && r.PokemonId == m2).Count, "공격했다");
            Assert.AreEqual(0, ParalysisFaces(blog, bf).Count, "다음 공격은 일반 타격을 지우지 않는다");
        }

        [Test]
        public void G14_배틀밖이나벤치에서걸린마비는나와서공격해도일반타격을지우지않음()
        {
            // 원작 커뮤니티 확인,
            // 원작 커뮤니티 확인. 옛 [가정]은 다른 배틀로 넘어가도 남았다.
            // 마비 불꽃숭이(우선도 6 − 3 = 3)가 실쿤(2)보다 먼저 나온다 — 출전할 때 대기가 지워진다
            var log = new EventLog();
            var hit = Attack("390:0", "ember", "266:0", 1, (s, a, d) =>
            {
                StatusRules.Inflict(ctx, s.Pokemon[a], StatusEffect.Paralysed);
                Assert.IsTrue(s.Pokemon[a].ParalysisPending);
            }, out var t, out int chim, out _, log);
            Assert.IsFalse(t.Pokemon[chim].ParalysisPending, "출전할 때 지운다");
            Assert.AreEqual(1, Count<DiceRolled>(hit));
            Assert.AreEqual(0, Count<DiceFacesChanged>(hit), "일반 타격을 지우지 않는다");
            Assert.AreEqual(StatusEffect.Paralysed, t.Pokemon[chim].Status, "우선도 −3은 남는다");
        }

        [Test]
        public void G14_둘다졸림이고일반타격이1개면그대로_2개이상이면둘다바꿈()
        {
            // 원작 커뮤니티 확인.
            // 옛 [가정]은 공격하는 쪽 것을 먼저(일반 → 심볼)였다
            void BothDrowsy(GameState st, int a, int d)
            {
                StatusRules.Inflict(ctx, st.Pokemon[a], StatusEffect.Drowsy);
                StatusRules.Inflict(ctx, st.Pokemon[d], StatusEffect.Drowsy);
            }
            var log = new EventLog();
            var s = RunWindow(log, "387:0", "399:0", Mv("razor_leaf"), new[] { C, R, S }, Window.AfterRoll, BothDrowsy, out _, out _);
            var ch = log.OfType<DiceFacesChanged>();
            Assert.AreEqual(2, ch.Count, "둘 다 0 결과를 낸다(01 §3-6 ⑥)");
            Assert.AreEqual(0, ch[0].Changes.Length);
            Assert.AreEqual(0, ch[1].Changes.Length);
            CollectionAssert.AreEqual(new[] { C, R, S }, s.Battle.Faces, "상쇄 — 일반 타격 그대로");

            // 일반 타격 2개면 「가능한 많은 효과를 처리」: 하나는 심볼, 하나는 크리티컬(원작 커뮤니티 확인)
            var log2 = new EventLog();
            var t = RunWindow(log2, "387:0", "399:0", Mv("razor_leaf"), new[] { R, R }, Window.AfterRoll, BothDrowsy, out _, out _);
            CollectionAssert.AreEqual(new[] { S, C }, t.Battle.Faces);
        }

        [Test]
        public void G14_마비된공격자대졸린대상은졸림먼저_크리하나를남기고나머지를지움()
        {
            // 원작 커뮤니티 확인,
            // 원작 커뮤니티 확인. 기본(자동) 순서 — 공격자가 고르는 창은 결정 72(EffectRules.OrderFaceChanges 자리)
            var log = new EventLog();
            var s = RunWindow(log, "387:0", "399:0", Mv("razor_leaf"), new[] { R, R, C }, Window.AfterRoll, (st, a, d) =>
            {
                StatusRules.Inflict(ctx, st.Pokemon[a], StatusEffect.Paralysed);
                StatusRules.Inflict(ctx, st.Pokemon[d], StatusEffect.Drowsy);
            }, out _, out _);
            var ch = log.OfType<DiceFacesChanged>();
            Assert.AreEqual(2, ch.Count);
            Assert.IsTrue(ch[0].Source.Same(St(StatusEffect.Drowsy)), "졸림 먼저");
            Assert.AreEqual(C, ch[0].Changes[0].After);
            Assert.IsTrue(ch[1].Source.Same(St(StatusEffect.Paralysed)));
            CollectionAssert.AreEqual(new[] { C, X, C }, s.Battle.Faces);
            Assert.AreEqual(4, AttackDice.Sum(s.Battle.Faces));
        }

        // ── 결정 72: 효과가 충돌하면 공격자가 순서를 고른다 ──

        /// <summary>
        /// 마비된 바로 다음 내 턴(<see cref="ParalysedOnMyTurn"/>)에 졸린 실쿤을 잎날가르기로 친다. seed = 그 공격 주사위. fixedOrder = 옛 판(<see cref="RuleOptions.FixedEffectOrder"/>).
        /// move·wildKey = 다른 기술·야생(<see cref="ParalysedOnMyTurn"/>).
        /// </summary>
        GameState ParalysedVsDrowsy(EventLog log, ulong seed, out int mine, out int wild, bool fixedOrder = false, string move = "razor_leaf", string wildKey = "266:0", int hp = 0, int fatigue = -1)
        {
            var s = ParalysedOnMyTurn(log, out mine, move, wildKey);
            if (hp > 0) s.Pokemon[mine].Damage = PokemonRules.MaxHp(ctx, s.Pokemon[mine]) - hp;
            if (fatigue >= 0) s.Pokemon[mine].Fatigue = fatigue;
            wild = s.Battle.Sides[1].Active;
            StatusRules.Inflict(ctx, s.Pokemon[wild], StatusEffect.Drowsy);
            s.Options.FixedEffectOrder = fixedOrder;
            s.Rng = new Pcg32(seed);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Mv(move)), log);
            return s;
        }

        static bool RolledRegular(EventLog l, int pokemon) => l.OfType<DiceRolled>().Exists(r => r.PokemonId == pokemon && Array.IndexOf(r.Faces, R) >= 0);

        static int OrderAsks(EventLog l) => l.OfType<ChoiceRequested>().FindAll(r => r.Sub == ChoiceSub.EffectOrder).Count;

        [Test]
        public void G14_결정72_마비된공격자대졸린대상은공격자가순서를고른다_일부러덜줄수있다()
        {
            // 결정 72 · 원작 커뮤니티 확인
            // (원작 커뮤니티 확인). 기본 순서는 졸림 먼저(F10 원작 커뮤니티 확인).
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                ParalysedVsDrowsy(l, sd, out int m, out _);
                return RolledRegular(l, m);
            });
            var log = new EventLog();
            var s = ParalysedVsDrowsy(log, seed, out int mine, out int wild);
            var c = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.EffectOrder);
            Assert.AreEqual(0, c.Seat, "공격하는 좌석에게 묻는다");
            Assert.AreEqual(mine, c.Subject);
            Assert.AreEqual(1, OrderAsks(log));
            Assert.AreEqual(0, log.OfType<DiceFacesChanged>().Count, "답하기 전에는 면을 바꾸지 않는다");
            var rolled = log.OfType<DiceRolled>().FindLast(r => r.PokemonId == mine).Faces;

            var legal = new List<ChoiceAnswer>();
            Engine.Legal(s, ctx, c, legal);
            Assert.AreEqual(2, legal.Count, "결과가 다른 순서 둘");
            const int para = 0, dDef = 2;   // 효과 Id(상태이상 기본 효과): 0 = 마비(공격할 때), 2 = 졸림(공격받을 때)
            CollectionAssert.AreEqual(new[] { dDef, para }, legal[0].Values, "첫 답 = 기본 순서(졸림 먼저)");
            CollectionAssert.AreEqual(new[] { para, dDef }, legal[1].Values);
            Assert.IsTrue(c.Default.SameAs(legal[0]), "시간 초과면 기본 순서");

            var keep = EffectRules.Preview(s, ctx, legal[0].Values);
            var spare = EffectRules.Preview(s, ctx, legal[1].Values);
            Assert.AreEqual(0, AttackDice.Count(keep.Faces, R));
            Assert.AreEqual(0, AttackDice.Count(spare.Faces, R));
            Assert.AreEqual(AttackDice.Count(rolled, C) + 1, AttackDice.Count(keep.Faces, C), "졸림 먼저: 일반 하나가 크리로 남는다");
            Assert.AreEqual(AttackDice.Count(rolled, C), AttackDice.Count(spare.Faces, C), "마비 먼저: 일반을 다 지워 졸림이 바꿀 것이 없다");
            Assert.That(keep.Damage, Is.GreaterThan(spare.Damage), "데미지가 달라서 묻는다(결정 73 — 잎날가르기의 크리 하나는 3이라 실쿤(벌레 −2)에도 늘 갈린다)");

            // 기본 순서로 답하면 크리가 남는다(사본)
            var t = s.Clone();
            var tlog = new EventLog();
            Battles.Answer(t, ctx, t.Pending[0], legal[0], tlog);
            var tch = tlog.OfType<DiceFacesChanged>();
            Assert.IsTrue(tch[0].Source.Same(St(StatusEffect.Drowsy)));
            Assert.AreEqual(keep.Damage, tlog.OfType<DamageBreakdown>().Find(d => d.TargetId == wild).Final, "미리 셈한 피해 그대로");

            // 일부러 덜 주는 순서(마비 먼저)를 고른다
            int from = log.Events.Count;
            Battles.Answer(s, ctx, c, legal[1], log);
            var after = log.Events.GetRange(from, log.Events.Count - from);
            after = after.FindAll(e => e.Seq == after[0].Seq);   // 답한 행동만(이어지는 야생의 턴은 새 Seq)
            var ch = after.FindAll(e => e is DiceFacesChanged).ConvertAll(e => (DiceFacesChanged)e);
            Assert.AreEqual(2, ch.Count);
            Assert.IsTrue(ch[0].Source.Same(St(StatusEffect.Paralysed)), "고른 순서대로: 마비 먼저");
            Assert.IsTrue(ch[1].Source.Same(St(StatusEffect.Drowsy)));
            Assert.AreEqual(0, ch[1].Changes.Length, "바꿀 일반 타격이 없다(0 결과)");
            Assert.AreEqual(spare.Damage, ((DamageBreakdown)after.Find(e => e is DamageBreakdown d && d.TargetId == wild)).Final);
            Assert.IsFalse(s.Pokemon[mine].ParalysisPending);
        }

        [Test]
        public void G14_결정72_순서로결과가같으면묻지않는다()
        {
            // 마비 대 졸림이어도 일반 타격이 없으면 같다
            ulong none = Battles.Seed(sd =>
            {
                var l = new EventLog();
                ParalysedVsDrowsy(l, sd, out int m, out _);
                return l.OfType<DiceRolled>().Exists(r => r.PokemonId == m) && !RolledRegular(l, m);
            });
            var log = new EventLog();
            ParalysedVsDrowsy(log, none, out _, out _);
            Assert.AreEqual(0, OrderAsks(log), "일반 타격이 없으면 순서가 결과를 바꾸지 않는다");

            // 둘 다 졸림이고 일반 타격 2개 이상: 하나는 심볼, 하나는 크리 — 어느 쪽이 먼저든 개수가 같다(F10). 1개면 상쇄(순서와 무관)
            void BothDrowsy(GameState g, int a, int d)
            {
                StatusRules.Inflict(ctx, g.Pokemon[a], StatusEffect.Drowsy);
                StatusRules.Inflict(ctx, g.Pokemon[d], StatusEffect.Drowsy);
            }
            ulong two = Battles.Seed(sd => AttackDice.Count(One<DiceRolled>(Attack("387:0", "razor_leaf", "266:0", sd, BothDrowsy)).Faces, R) >= 2);
            var dlog = new EventLog();
            var hit = Attack("387:0", "razor_leaf", "266:0", two, BothDrowsy, out _, out _, out _, dlog);
            Assert.AreEqual(0, OrderAsks(dlog));
            Assert.AreEqual(2, Count<DiceFacesChanged>(hit), "묻지 않고 그대로 처리한다");
        }

        [Test]
        public void G14_결정73_눈개수만다르고데미지와쓰러짐이같으면묻지않고기본순서()
        {
            // 결정 73 ②(B5 ① 나 「데미지나 기절이 다를 때만 묻는다(눈 개수만 다르면 묻지 않음)」). 마비된 모부기가 졸린 해골몽(고스트, 체력 7, 우선도 3)을
            // 몸통박치기(노말, 효과 피해 없음)로 친다 — 노말 → 고스트 −4라 졸림 먼저(크리 하나가 남음)와 마비 먼저(일반을 다 지움)의 눈 개수는 달라도 둘 다 데미지 0
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                ParalysedVsDrowsy(l, sd, out int m, out _, move: "tackle", wildKey: "355:0");
                return RolledRegular(l, m);
            });
            var log = new EventLog();
            var s = ParalysedVsDrowsy(log, seed, out int mine, out int wild, move: "tackle", wildKey: "355:0");
            Assert.AreEqual(0, OrderAsks(log), "눈 개수만 다르면 묻지 않는다");

            var roll = log.OfType<DiceRolled>().FindLast(r => r.PokemonId == mine);
            var rolled = roll.Faces;
            var spare = new List<AttackDieFace>(rolled);
            spare.RemoveAll(f => f == R);                                    // 마비 먼저: 일반을 다 지워 졸림이 바꿀 것이 없다
            var mineAct = log.Events.FindAll(e => e.Seq == roll.Seq);         // 내 공격 행동만(이어지는 야생의 턴은 새 Seq)
            var ch = mineAct.FindAll(e => e is DiceFacesChanged).ConvertAll(e => (DiceFacesChanged)e);
            Assert.AreEqual(2, ch.Count);
            Assert.IsTrue(ch[0].Source.Same(St(StatusEffect.Drowsy)), "기본 순서(졸림 먼저)");
            Assert.AreEqual(C, ch[0].Changes[0].After);
            Assert.IsTrue(ch[1].Source.Same(St(StatusEffect.Paralysed)));
            Assert.AreNotEqual(AttackDice.Count(rolled, C) + 1, AttackDice.Count(spare, C), "두 순서의 눈 개수는 다르다(크리 하나)");

            var hit = (DamageBreakdown)mineAct.Find(e => e is DamageBreakdown d && d.TargetId == wild);
            Assert.AreEqual(0, hit.Final, "졸림 먼저의 데미지");
            int spareDamage = DamageRules.Calculate(new DamageInput { DiceSum = AttackDice.Sum(spare), MoveType = PokemonType.Normal, DefenderType1 = PokemonType.Ghost },
                s.Options.DamageCapStage);
            Assert.AreEqual(0, spareDamage, "마비 먼저의 데미지도 같다");
            Assert.IsFalse(PokemonRules.IsFainted(ctx, s.Pokemon[wild]), "둘 다 쓰러지지 않는다");
        }

        [Test]
        public void G14_결정73_결과가같다는데미지와쓰러짐만본다_쓰러짐만달라도다르다()
        {
            // 결정 73 ②: 「결과가 같다」 = 피해(최종 데미지)와 받는 포켓몬 기절이 같다. 면 종류별 개수는 보지 않는다(결정 72 [가정] plan §5 #69 ① 「개수가 다르면 묻는다」를 바꿈).
            // 지금 엔진은 기절을 피해 ≥ 체력으로 세어 피해가 같고 기절만 다른 순서가 생기지 않는다 — 비교 자체를 단정한다(기절이 피해 밖의 것으로 갈리는 효과가 생겨도 묻는다)
            EffectRules.OrderOutcome O(int damage, bool faints, params AttackDieFace[] faces)
            {
                var o = new EffectRules.OrderOutcome { Damage = damage, Faints = faints };
                o.Faces.AddRange(faces);
                return o;
            }
            Assert.IsTrue(O(0, false, C, X).SameAs(O(0, false, X, X)), "눈 개수만 다르면 같다");
            Assert.IsTrue(O(3, true, C, S).SameAs(O(3, true, R, R, R)));
            Assert.IsFalse(O(3, false, C, R).SameAs(O(2, false, C, X)), "데미지가 다르면 다르다");
            Assert.IsFalse(O(4, true, C, C).SameAs(O(4, false, C, C)), "쓰러짐만 달라도 다르다");
        }

        [Test]
        public void G14_결정73_반동으로공격자만한쪽순서에서쓰러지면묻는다()
        {
            // 결정 73 ② — 누구의 기절인지 좁히지 않았다: 공격자가 반동(크리마다 1, 돌진)으로 한쪽 순서에서만 쓰러져도 묻는다(검토 반영).
            // 마비된 모부기(체력 1)가 졸린 해골몽(고스트 −4)을 돌진으로 친다. 크리 0개·일반 1개 이상: 졸림 먼저 = 크리 1(반동 1로 쓰러짐), 마비 먼저 = 크리 0(반동 없음) — 받는 쪽 데미지는 둘 다 0
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                ParalysedVsDrowsy(l, sd, out int m, out _, move: "take_down", wildKey: "355:0", hp: 1);
                return l.OfType<DiceRolled>().Exists(r => r.PokemonId == m && Array.IndexOf(r.Faces, R) >= 0 && Array.IndexOf(r.Faces, C) < 0);
            });
            var log = new EventLog();
            var s = ParalysedVsDrowsy(log, seed, out int mine, out _, move: "take_down", wildKey: "355:0", hp: 1);
            Assert.AreEqual(1, OrderAsks(log), "공격자가 한쪽 순서에서만 쓰러지면 묻는다");
            var c = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.EffectOrder);
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(s, ctx, c, legal);
            Assert.AreEqual(2, legal.Count);
            var keep = EffectRules.Preview(s, ctx, legal[0].Values);
            var spare = EffectRules.Preview(s, ctx, legal[1].Values);
            Assert.AreEqual(keep.Damage, spare.Damage, "받는 쪽 데미지는 같다");
            Assert.AreEqual(0, keep.Damage);
            Assert.IsFalse(keep.Faints || spare.Faints);
            Assert.IsTrue(keep.AttackerFaints, "졸림 먼저(기본): 크리 하나의 반동으로 쓰러진다");
            Assert.IsFalse(spare.AttackerFaints, "마비 먼저: 반동이 없다");
            Assert.AreSame(legal[1], EffectRules.BestOrder(s, ctx, legal), "가상 쪽도 받는 쪽 결과가 같으면 공격자가 사는 순서");

            // 기본 순서면 반동으로 쓰러지고, 마비 먼저를 고르면 산다(답한 행동만 — 이어지는 야생의 턴은 새 Seq)
            bool FaintsAfter(GameState g, ChoiceAnswer a)
            {
                var l = new EventLog();
                Battles.Answer(g, ctx, g.Pending[0], a, l);
                return l.Events.FindAll(e => e.Seq == l.Events[0].Seq).Exists(e => e is Fainted f && f.PokemonId == mine);
            }
            Assert.IsTrue(FaintsAfter(s.Clone(), legal[0]), "미리 셈대로: 기본 순서면 쓰러진다");
            Assert.IsFalse(FaintsAfter(s, legal[1]), "미리 셈대로: 마비 먼저면 산다");
        }

        [Test]
        public void G14_결정73_반동기절은이미버퍼에든피로피해까지센다()
        {
            // M5 규칙 끝 검토 반영: 굴릴 때 버퍼에 든 피로 3 피해(피로 상태 5)는 아직 체력에 들지 않았다 — 미리 셈은 버퍼를 다 적용한 체력(HpAfterBuffer)과 견준다.
            // 체력 4 · 피로 5인 모부기: 졸림 먼저 = 크리 1(반동 1 + 피로 3 = 4로 쓰러짐), 마비 먼저 = 반동 0(피로 3만 — 산다)
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                ParalysedVsDrowsy(l, sd, out int m, out _, move: "take_down", wildKey: "355:0", hp: 4, fatigue: FatigueRules.Max);
                return l.OfType<DiceRolled>().Exists(r => r.PokemonId == m && Array.IndexOf(r.Faces, R) >= 0 && Array.IndexOf(r.Faces, C) < 0);
            });
            var log = new EventLog();
            var s = ParalysedVsDrowsy(log, seed, out _, out _, move: "take_down", wildKey: "355:0", hp: 4, fatigue: FatigueRules.Max);
            Assert.AreEqual(1, OrderAsks(log), "피로 피해와 반동을 합쳐 한쪽 순서에서만 쓰러지면 묻는다");
            var c = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.EffectOrder);
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(s, ctx, c, legal);
            Assert.IsTrue(EffectRules.Preview(s, ctx, legal[0].Values).AttackerFaints, "졸림 먼저: 반동 1 + 피로 3");
            Assert.IsFalse(EffectRules.Preview(s, ctx, legal[1].Values).AttackerFaints, "마비 먼저: 피로 3만");
        }

        [Test]
        public void G14_결정72_가상쪽공격자는묻지않고자기에게유리한순서()
        {
            // 결정 72 [가정]: 사람이 고르지 않는 쪽(야생·갤럭시단·UB)은 공격자에게 가장 유리한 결과(쓰러뜨림 → 피해)를 스스로 고른다.
            // 실쿤 둘(우선도 2 동률): 토큰을 쓰지 않아 야생이 먼저, 그 전에 야생은 마비(대기)·내 실쿤은 졸림
            GameState Prep(EventLog l, ulong sd)
            {
                var g = Battles.Game(ctx, 2);
                int mine = Give(g, 0, "266:0");
                Battles.Wild(g, ctx, l, 0, Scene.Spawn(g, ctx, "266:0", SeatId.Wild));
                var token = Battles.Only(g, ChoiceKind.Reaction, ChoiceSub.InitiativeToken);
                int foe = g.Battle.Sides[1].Active;
                StatusRules.Inflict(ctx, g.Pokemon[foe], StatusEffect.Paralysed);
                StatusRules.Inflict(ctx, g.Pokemon[mine], StatusEffect.Drowsy);
                g.Rng = new Pcg32(sd);
                Battles.Answer(g, ctx, token, ChoiceAnswer.Of(AnswerKind.Decline), l);
                return g;
            }
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                var g = Prep(l, sd);
                return RolledRegular(l, g.Battle != null ? g.Battle.Sides[1].Active : -1);
            });
            var log = new EventLog();
            var s = Prep(log, seed);
            Assert.AreEqual(0, OrderAsks(log), "가상 쪽에는 묻지 않는다");
            var ch = log.OfType<DiceFacesChanged>();
            Assert.AreEqual(2, ch.Count);
            Assert.IsTrue(ch[0].Source.Same(St(StatusEffect.Drowsy)), "크리를 남기는 순서(공격자에게 유리)");
            Assert.AreEqual(1, ch[0].Changes.Length);
            Assert.AreEqual(C, ch[0].Changes[0].After);
            Assert.IsTrue(ch[1].Source.Same(St(StatusEffect.Paralysed)));
            Assert.IsNotNull(s.Battle, "배틀은 이어진다");
        }

        [Test]
        public void G14_결정72_봇은유리한순서_옛판은묻지않고기본순서_답은복제해도같다()
        {
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                ParalysedVsDrowsy(l, sd, out int m, out _);
                return RolledRegular(l, m);
            });
            var s = ParalysedVsDrowsy(new EventLog(), seed, out _, out _);
            var c = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.EffectOrder);
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(s, ctx, c, legal);

            // 봇(좌석 AI): 보기 사본으로 같은 답을 셈한다 — 크리를 남기는 기본 순서
            var view = Engine.ViewFor(s, 0);
            var viewLegal = new List<ChoiceAnswer>();
            Engine.Legal(view, ctx, view.Pending[0], viewLegal);
            Assert.AreEqual(legal.Count, viewLegal.Count, "보기에서도 같은 합법 답(굴림·상태이상은 공개)");
            var bot = new Sinnoh.Ai.PrincipleAgent().Decide(new Sinnoh.Rules.Session.AgentInput { View = view, Choice = view.Pending[0], Ctx = ctx }, new Sinnoh.Rules.Session.AgentBudget());
            Assert.IsTrue(bot.Answer.SameAs(legal[0]), $"봇의 답 {bot.Answer}");

            // 선택이 열린 채 복제·저장 왕복해도 같은 답에서 같은 상태(명령 줄로 다시 적용된다)
            var json = SaveStore.Deserialize<GameState>(SaveStore.Serialize(s, false));
            var copy = s.Clone();
            Battles.Answer(s, ctx, c, legal[1], new EventLog());
            Battles.Answer(copy, ctx, copy.Pending[0], legal[1], new EventLog());
            Battles.Answer(json, ctx, json.Pending[0], legal[1], new EventLog());
            Assert.AreEqual(StateHash.Of(s), StateHash.Of(copy));
            Assert.AreEqual(StateHash.Of(s), StateHash.Of(json));

            // 옛 판(규칙 판 4 전 저장을 이어 받은 판): 묻지 않고 기본 순서(졸림 먼저) — 옛 로그에 이 선택이 없다
            var olog = new EventLog();
            ParalysedVsDrowsy(olog, seed, out _, out _, fixedOrder: true);
            Assert.AreEqual(0, OrderAsks(olog));
            var och = olog.OfType<DiceFacesChanged>();
            Assert.IsTrue(och[0].Source.Same(St(StatusEffect.Drowsy)));
            Assert.IsTrue(och[1].Source.Same(St(StatusEffect.Paralysed)));
        }

        [Test]
        public void G14_결정72_유리한순서는기본순서가아니어도쓰러뜨림과피해로고른다_봇도같다()
        {
            // 결정 72 [가정](plan §5 #69 ③): 가상 쪽·봇은 공격자에게 가장 유리한 순서(쓰러뜨림 → 피해). 지금 엔진에서 결과가 갈리는 조합(마비된 공격자 대 졸린 대상)은
            // 기본 순서(졸림 먼저 — 원작 커뮤니티 확인)가 늘 유리해서, 합법 답 순서를 뒤집어 기본 순서와 다른 답이 이기는 판을 만든다(검토 반영 — 되돌리면 깨진다)
            ulong seed = Battles.Seed(sd =>
            {
                var l = new EventLog();
                ParalysedVsDrowsy(l, sd, out int m, out _);
                return RolledRegular(l, m);
            });
            var s = ParalysedVsDrowsy(new EventLog(), seed, out _, out int wild);
            var c = Battles.Only(s, ChoiceKind.Reaction, ChoiceSub.EffectOrder);
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(s, ctx, c, legal);
            Assert.AreEqual(2, legal.Count);
            var keep = EffectRules.Preview(s, ctx, legal[0].Values);   // 졸림 먼저: 크리 하나가 남는다
            var spare = EffectRules.Preview(s, ctx, legal[1].Values);  // 마비 먼저: 일반을 다 지운다
            Assert.That(keep.Damage, Is.GreaterThan(spare.Damage), "이 굴림에서는 졸림 먼저가 피해가 크다");

            // 피해: 합법 답 순서와 무관하게 큰 쪽
            var flipped = new List<ChoiceAnswer> { legal[1], legal[0] };
            Assert.IsTrue(EffectRules.BestOrder(s, ctx, flipped).SameAs(legal[0]), "뒤집은 목록에서도 피해가 큰 순서");
            Assert.IsTrue(EffectRules.BestOrder(s, ctx, legal).SameAs(legal[0]));

            // 쓰러뜨림: 졸림 먼저 피해만큼 체력이면 그 순서만 쓰러뜨린다 — 뒤집어도 그 순서
            int hp = PokemonRules.Hp(ctx, s.Pokemon[wild]);
            if (keep.Damage < hp)
            {
                s.Pokemon[wild].Damage += hp - keep.Damage;
                Assert.IsTrue(EffectRules.Preview(s, ctx, legal[0].Values).Faints);
                Assert.IsFalse(EffectRules.Preview(s, ctx, legal[1].Values).Faints);
                Assert.IsTrue(EffectRules.BestOrder(s, ctx, flipped).SameAs(legal[0]), "쓰러뜨리는 순서가 먼저");
                s.Pokemon[wild].Damage -= hp - keep.Damage;
            }

            // 봇: 기본 순서가 덜 유리한 판(보기 사본의 쌓인 순서를 뒤집어 첫 합법 답·시간 초과 답 = 마비 먼저)에서도 유리한 순서를 고른다 — Default로 빠지면 깨진다
            var view = Engine.ViewFor(s, 0);
            var top = view.Stack.Count - 1;
            (view.Stack[top], view.Stack[top - 1]) = (view.Stack[top - 1], view.Stack[top]);
            var viewLegal = new List<ChoiceAnswer>();
            Engine.Legal(view, ctx, view.Pending[0], viewLegal);
            Assert.IsTrue(viewLegal[0].SameAs(legal[1]), "뒤집은 보기: 첫 합법 답 = 마비 먼저");
            view.Pending[0].Default = viewLegal[0];
            var bot = new Sinnoh.Ai.PrincipleAgent().Decide(new Sinnoh.Rules.Session.AgentInput { View = view, Choice = view.Pending[0], Ctx = ctx }, new Sinnoh.Rules.Session.AgentBudget());
            Assert.IsTrue(bot.Answer.SameAs(legal[0]), $"봇의 답 {bot.Answer} — 피해가 큰 졸림 먼저");
        }

        [Test]
        public void G14_배틀이끝난턴에는독피해가없다()
        {
            // p.9 독 「ignore when the battle ends」 · 원작 커뮤니티 확인.
            // 독 불꽃숭이(체력 3)가 체력 1 실쿤을 불꽃세례로 쓰러뜨린다 — 턴 끝 2 피해가 없다(전에는 들어가 배틀 뒤에도 남았다)
            void Prep(GameState g, int a, int d)
            {
                Battles.SetHp(g, ctx, a, 3);
                StatusRules.Inflict(ctx, g.Pokemon[a], StatusEffect.Poisoned);
                Battles.SetHp(g, ctx, d, 1);
            }
            ulong seed = Battles.Seed(sd => Attack("390:0", "ember", "266:0", sd, Prep).Exists(e => e is Fainted));
            var hit = Attack("390:0", "ember", "266:0", seed, Prep, out var t, out int chim, out _);
            Assert.AreEqual(1, Count<BattleEnded>(hit));
            Assert.AreEqual(0, Count<StatusTicked>(hit), "배틀이 끝나면 독 없음");
            Assert.AreEqual(3, Battles.Hp(t, ctx, chim));

            // 배틀이 이어지면 그대로 2 피해(S7_6)
            var go = Attack("390:0", "ember", "266:0", seed, (g, a, d) =>
            {
                Battles.SetHp(g, ctx, a, 3);
                StatusRules.Inflict(ctx, g.Pokemon[a], StatusEffect.Poisoned);
            }, out _, out int chim2, out _);
            Assert.AreEqual(0, Count<BattleEnded>(go));
            var tick = One<StatusTicked>(go);
            Assert.AreEqual(chim2, tick.PokemonId);
            Assert.AreEqual(-2, tick.Effect);
            Assert.AreEqual(1, tick.HpAfter);
        }

        [Test]
        public void S7_6_졸림은공격할때와공격받을때면을바꿈()
        {
            var log = new EventLog();
            var s = RunWindow(log, "387:0", "399:0", Mv("razor_leaf"), new[] { R, C, R }, Window.AfterRoll, (st, a, d) =>
            {
                StatusRules.Inflict(ctx, st.Pokemon[a], StatusEffect.Drowsy);
                StatusRules.Inflict(ctx, st.Pokemon[d], StatusEffect.Drowsy);
            }, out _, out _);
            var ch = log.OfType<DiceFacesChanged>();
            Assert.AreEqual(2, ch.Count, "공격하는 쪽 → 받는 쪽(G14)");
            Assert.AreEqual(1, ch[0].Changes.Length);
            Assert.AreEqual(0, ch[0].Changes[0].Index, "왼쪽부터 첫 일반 타격");
            Assert.AreEqual(S, ch[0].Changes[0].After, "공격할 때: 일반 → 상태이상 심볼");
            Assert.AreEqual(2, ch[1].Changes[0].Index);
            Assert.AreEqual(C, ch[1].Changes[0].After, "공격받을 때: 상대 일반 → 크리티컬");
            CollectionAssert.AreEqual(new[] { S, C, C }, s.Battle.Faces);

            // 바꿀 일반 타격이 없으면 그대로, 0 결과는 낸다
            var log2 = new EventLog();
            var t = RunWindow(log2, "387:0", "399:0", Mv("razor_leaf"), new[] { C, S }, Window.AfterRoll,
                (st, a, d) => StatusRules.Inflict(ctx, st.Pokemon[a], StatusEffect.Drowsy), out _, out _);
            var none = log2.OfType<DiceFacesChanged>();
            Assert.AreEqual(1, none.Count);
            Assert.AreEqual(0, none[0].Changes.Length);
            CollectionAssert.AreEqual(none[0].SrcBefore, none[0].SrcAfter);
            CollectionAssert.AreEqual(new[] { C, S }, t.Battle.Faces);
        }

        [Test]
        public void S7_6_혼란은같은기술을이어쓰면3피해()
        {
            // 혼란 모부기(우선도 4) 대 실쿤(8, 우선도 2): 잎날가르기 −2로는 실쿤이 첫 공격에 안 쓰러지고, 독침 1개(+2)로는 모부기(6)가 안 쓰러진다
            var s = Battles.Game(ctx, 2);
            int mine = Give(s, 0, "387:0");
            StatusRules.Inflict(ctx, s.Pokemon[mine], StatusEffect.Confused);
            int wild = Scene.Spawn(s, ctx, "266:0", SeatId.Wild);
            var log = new EventLog();
            Battles.Wild(s, ctx, log, 0, wild);
            int leaf = Mv("razor_leaf");
            int first = log.Events.Count;
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(leaf), log);
            var one = log.Events.FindAll(e => e.Seq == log.Events[first].Seq);
            Assert.AreEqual(0, Count<StatusTicked>(one), "직전 기술이 없다");
            Assert.AreEqual(leaf, s.Pokemon[mine].LastMove, "턴 끝에 직전 기술을 적는다");
            int second = log.Events.Count;
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(leaf), log);
            var two = log.Events.FindAll(e => e.Seq == log.Events[second].Seq);
            var tick = One<StatusTicked>(two);
            Assert.AreEqual(mine, tick.PokemonId);
            Assert.AreEqual(StatusEffect.Confused, tick.Status);
            Assert.IsTrue(tick.Source.Same(St(StatusEffect.Confused)));
            Assert.AreEqual(-Math.Min(3, tick.HpBefore), tick.Effect);
            Assert.AreEqual(tick.HpBefore + tick.Effect, tick.HpAfter);
            Assert.AreEqual(HpOf(two, wild).Index, tick.Index, "공격 피해와 동시(G14)");
            Assert.IsNull(HpOf(two, mine), "상태이상 피해는 StatusTicked만 싣는다");
        }

        [Test]
        public void S7_6_독은자기턴끝에2피해_회복못함()
        {
            // 독 모부기(체력 5)가 드레인키스: 회복 1은 0이 되고, 자기 턴 끝에 2 피해
            var log = new EventLog();
            var hit = Attack("387:0", "draining_kiss", "266:0", 1, (s, a, d) =>
            {
                Battles.SetHp(s, ctx, a, 5);
                StatusRules.Inflict(ctx, s.Pokemon[a], StatusEffect.Poisoned);
            }, out var t, out int mine, out int wild, log);
            var heal = HpOf(hit, mine);
            Assert.AreEqual(5, heal.Before);
            Assert.AreEqual(5, heal.After, "독이면 회복 못 함(p.9) — 0 결과");
            var tick = One<StatusTicked>(hit);
            Assert.AreEqual(mine, tick.PokemonId);
            Assert.AreEqual(StatusEffect.Poisoned, tick.Status);
            Assert.AreEqual(-2, tick.Effect);
            Assert.AreEqual(5, tick.HpBefore);
            Assert.AreEqual(3, tick.HpAfter);
            Assert.Greater(hit.IndexOf(tick), hit.IndexOf(heal), "턴 끝 효과는 공격 피해 뒤에 따로");
            Assert.AreNotEqual(heal.Index, tick.Index);
            // 실쿤의 턴 끝에는 모부기 독이 돌지 않는다(자기 턴만)
            int ticks = log.OfType<StatusTicked>().FindAll(x => x.PokemonId == mine).Count;
            Assert.AreEqual(1, ticks);
            Assert.AreEqual(ChoiceKind.BattleAction, Battles.Only(t, ChoiceKind.BattleAction).Kind);
        }

        /// <summary>빙결 야생 불꽃숭이(우선도 6)가 체력 1 모부기를 불꽃세례(+2)로 쓰러뜨린다. 내 파티에 비버니가 남는다.</summary>
        GameState FrozenWild(EventLog log, out int turtwig, out int bidoof)
        {
            var s = Battles.Game(ctx, 2);
            turtwig = Give(s, 0, "387:0");
            bidoof = Give(s, 0, "399:0");
            Battles.SetHp(s, ctx, turtwig, 1);
            int wild = Scene.Spawn(s, ctx, "390:0", SeatId.Wild);
            s.Pokemon[wild].Status = StatusEffect.Frozen;
            Battles.Wild(s, ctx, log, 0, wild);
            Battles.Answer(s, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(turtwig), log);
            return s;
        }

        [Test]
        public void S7_6_빙결은교체창없고야생도망없음()
        {
            // 야생: 약한~강한은 내 포켓몬을 쓰러뜨리면 도망치지만(S7_8) 빙결이면 못 한다
            var log = new EventLog();
            var s = FrozenWild(log, out int turtwig, out int bidoof);
            Assert.AreEqual(1, log.OfType<Fainted>().Count);
            Assert.AreEqual(0, log.OfType<Fled>().Count, "빙결된 야생은 도망치지 않는다");
            Assert.IsNotNull(s.Battle);
            Assert.AreEqual(bidoof, s.Battle.Sides[0].Active, "다음 포켓몬이 나온다");
            Assert.AreEqual(0, log.OfType<SwitchLocked>().Count, "야생은 바꿀 포켓몬이 없어 잠김을 보이지 않는다");

            // 트레이너전: 빙결된 모부기(민첩의 띠로 우선도 6)가 팽도리(체력 1)를 쓰러뜨려도 교체 창이 없다
            var t = Battles.Game(ctx, 2);
            int a = Give(t, 0, "387:0");
            Give(t, 0, "399:0");
            int p = Give(t, 1, "393:0");
            Give(t, 1, "396:0");
            t.Pokemon[a].HeldItem = Scene.Item(ctx, "item.swiftness_sash");
            t.Pokemon[a].Status = StatusEffect.Frozen;
            Battles.SetHp(t, ctx, p, 1);
            var tlog = new EventLog();
            BattleFlow.StartTrainer(t, ctx, Scene.Fx(t, tlog, 0, ActionKind.Challenge), 0, 1);
            Battles.Answer(t, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(a), tlog);
            Battles.Answer(t, ctx, ChoiceKind.SendIn, ChoiceAnswer.Pick(p), tlog);
            Battles.Answer(t, ctx, ChoiceKind.BattleAction, Battles.Move(Mv("razor_leaf")), tlog);
            var locked = tlog.OfType<SwitchLocked>();
            Assert.AreEqual(1, locked.Count);
            Assert.AreEqual(SwitchLockReason.Frozen, locked[0].Reason);
            Assert.AreEqual(0, locked[0].Side);
            Assert.IsFalse(t.Pending.Exists(c => c.Kind == ChoiceKind.SwitchWindow), "교체 창을 열지 않는다");
            Assert.AreEqual(a, t.Battle.Sides[0].Active);
        }

        [Test]
        public void S7_6_기절하면상태이상이풀림()
        {
            var log = new EventLog();
            var s = FrozenWild(log, out int turtwig, out _);
            var applied = log.OfType<StatusApplied>().Find(x => x.PokemonId == turtwig);
            Assert.IsNotNull(applied);
            Assert.AreEqual(StatusResult.Applied, applied.Result, "불꽃세례가 먼저 화상을 건다");
            Assert.IsTrue(PokemonRules.IsFainted(ctx, s.Pokemon[turtwig]));
            Assert.AreEqual(StatusEffect.None, s.Pokemon[turtwig].Status, "기절하면 풀린다(p.9)");
            Assert.IsFalse(s.Pokemon[turtwig].ParalysisPending);
        }

        // ── 처리기 ──

        [Test]
        public void G14_효과스택은상태안에있어복제해도같다()
        {
            var s = Scene.Game(ctx, 2);
            int a = Give(s, 0, "387:0");
            int d = Give(s, 1, "399:0");
            var b = Scene.Battle(s, BattleKind.Trainer, 0, 1);
            b.Move = Mv("razor_leaf");
            b.Faces.AddRange(new[] { R, R, C });
            StatusRules.Inflict(ctx, s.Pokemon[a], StatusEffect.Drowsy);
            StatusRules.Inflict(ctx, s.Pokemon[d], StatusEffect.Drowsy);
            EffectRules.Trigger(s, ctx, Window.AfterRoll);
            Assert.AreEqual(2, s.Stack.Count, "공격 쪽 졸림 + 받는 쪽 졸림");
            var top = s.Stack[s.Stack.Count - 1];
            Assert.AreEqual(1, top.EffectId, "먼저 할 것(공격 쪽)이 맨 위");
            Assert.AreEqual(a, top.User);
            Assert.AreEqual(d, top.Target);
            Assert.AreEqual(0, top.StepIndex);
            Assert.IsTrue(top.Source.Same(St(StatusEffect.Drowsy)));
            Assert.AreEqual(2, s.Stack[0].EffectId);

            var copy = s.Clone();
            Assert.AreEqual(StateHash.Of(s), StateHash.Of(copy));
            var l1 = new EventLog();
            var l2 = new EventLog();
            EffectRules.Run(s, ctx, Scene.Fx(s, l1, 0, ActionKind.BattleAction));
            EffectRules.Run(copy, ctx, Scene.Fx(copy, l2, 0, ActionKind.BattleAction));
            Assert.AreEqual(StateHash.Of(s), StateHash.Of(copy));
            Assert.AreEqual(EventDump.Of(l1.Events, SeatId.HostViewer), EventDump.Of(l2.Events, SeatId.HostViewer));
            Assert.AreEqual(0, s.Stack.Count);

            // 진행 루프(Engine.Advance)도 남은 프레임을 돈다: 데이터 효과 Id는 기본 효과 뒤
            Assert.AreEqual(EffectDb.BuiltInCount, ctx.Effects.OfMove(ctx.Cards.MoveIds.IndexOf("struggle_bug"))[0], "기술 표 첫 효과 기술");
            foreach (var e in ctx.Effects.DataEffects()) Assert.AreEqual(SourceKind.Move, e.Source.Kind);
        }

        [Test]
        public void M3_효과없는기술은피해만주고미구현목록에오름()
        {
            int barrier = Mv("barrier");
            // 완료 조건 1의 마지막 하나였다: 결정 76 B3 답(① 가 · ② 가)으로 넣었다(rulings G20 ⑷ — 셈은 OpEffectTests.G20_barrier_*). 이름은 그대로 둔다(미구현 목록이 비었음을 여기서 지킨다)
            Assert.IsTrue(ctx.Effects.Implemented(barrier), "배리어 「all critical hits damage the attacker instead」 — 결정 76 B3");
            // 비버니(우선도 4) 배리어 → 실쿤: 쓰는 공격 자체는 효과 이벤트 없이 피해만(보정은 다음에 공격받을 때 쓰인다)
            var hit = Attack("399:0", "barrier", "266:0", 1);
            var effectTypes = new[] { typeof(StatusApplied), typeof(StatusTicked), typeof(DiceFacesChanged), typeof(Healed) };
            foreach (var e in hit) CollectionAssert.DoesNotContain(effectTypes, e.GetType(), e.GetType().Name);
            var br = One<DamageBreakdown>(hit);
            Assert.AreEqual(2, br.Steps.Length, "주사위 + 상성만");
            Assert.AreEqual(1, Count<HpChanged>(hit));

            // 미구현 목록 = 효과가 없는 기술(build가 Tools/data/reports/effects-todo.tsv로도 쓴다)
            var todo = new List<string>();
            foreach (var m in ctx.Cards.Moves)
                if (!ctx.Effects.Implemented(ctx.Cards.MoveIds.IndexOf(m.Key))) todo.Add(m.Key);
            CollectionAssert.DoesNotContain(todo, "quick_attack", "둘째 기술은 핸들러 갈래가 옮겼다(rulings G21)");
            CollectionAssert.DoesNotContain(todo, "tackle", "몸통박치기 「When the target next attacks …」는 M5 지속 보정 갈래가 옮겼다");
            CollectionAssert.DoesNotContain(todo, "infatuate", "동시에 2개 거는 기술은 M5 나머지 op 갈래가 상태 칸 둘(PokemonState.Status2)로 옮겼다(rulings G20)");
            CollectionAssert.DoesNotContain(todo, "ember");
            Assert.AreEqual(0, todo.Count, "미구현 0(완료 조건 1) — " + string.Join(",", todo));
            TestContext.WriteLine($"효과 미구현 {todo.Count}/{ctx.Cards.Moves.Count}종: {string.Join(", ", todo)}");
        }

        // ── 데이터 ──

        [Test]
        public void M3_효과표왕복같은바이트()
        {
            var db = TestData.LoadCardDb();
            string text = TestData.ReadText("effects.json");
            var effects = EffectDbLoader.Load(text, db);
            Assert.AreEqual(text, EffectDbLoader.Write(effects, db));
            Assert.AreEqual(db.Moves.Count, effects.MoveCount);
            Assert.AreEqual(EffectDb.BuiltInCount + text.Split('\n').Length - 3, effects.Effects.Count, "행 하나 = 효과 절 하나");
        }

        [Test]
        public void M3_효과표는모르는op와칸을거부()
        {
            var db = TestData.LoadCardDb();
            string text = TestData.ReadText("effects.json");
            void Rejected(string from, string to) =>
                Assert.Throws<FormatException>(() => EffectDbLoader.Load(Replace(text, from, to), db), $"{from} → {to}");
            Rejected("\"op\":\"Inflict\"", "\"op\":\"Explode\"");
            Rejected("\"op\":\"Inflict\"", "\"op\":\"RemoveFaces\"");
            Rejected("\"window\":\"BeforeDamage\"", "\"window\":\"AfterRoll\"");
            Rejected("\"key\":\"move.x_scissor\"", "\"key\":\"move.no_such_move\"");
            Rejected("\"key\":\"move.x_scissor\"", "\"key\":\"card.x_scissor\"");
            Rejected("\"face\":\"CriticalHit\",\"value\":1,", "\"face\":\"CriticalHit\",\"value\":null,");
            Rejected("\"face\":\"CriticalHit\",\"value\":1,", "\"face\":\"None\",\"value\":1,");
            Rejected("\"who\":\"Target\",\"status\":\"Paralysed\"", "\"who\":\"Target\",\"status\":null");
            Rejected("\"who\":\"Target\",\"status\":\"Paralysed\"", "\"who\":\"Party\",\"status\":\"Paralysed\"");
            Rejected("\"symbols\":1,", "\"symbols\":0,");
            Rejected("\"targetType\":\"Flying\"", "\"targetType\":\"Typeless\"");
            Rejected("\"op\":\"FaceDamage\",", "\"extra\":1,\"op\":\"FaceDamage\",");
            Rejected("{\"symbols\":1,\"targetStatus\":null,\"targetType\":null,\"targetHeldItem\":null,\"sameMoveAsLast\":null,\"partyType\":null,\"knowsType\":null}",
                "{\"symbols\":null,\"targetStatus\":null,\"targetType\":null,\"targetHeldItem\":null,\"sameMoveAsLast\":null,\"partyType\":null,\"knowsType\":null}");
            // 행 순서: 기술 표 순서가 아니면 거부(정규형 하나)
            var lines = new List<string>(text.Split('\n'));
            string a = lines[1].TrimEnd(','), b = lines[2].TrimEnd(',');
            lines[1] = b + ",";
            lines[2] = a + ",";
            Assert.Throws<FormatException>(() => EffectDbLoader.Load(string.Join("\n", lines), db), "행 순서");
        }

        static string Replace(string s, string from, string to)
        {
            int i = s.IndexOf(from, StringComparison.Ordinal);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), $"시험 준비: \"{from}\"가 없다");
            return s.Substring(0, i) + to + s.Substring(i + from.Length);
        }
    }
}
