using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>프레임 한 걸음의 결과(01 §7-2): 다음 op로 / 선택을 열고 멈춤 / 끝(프레임을 뺀다).</summary>
    public enum EffectStepResult { Continue, Wait, Done }

    /// <summary>
    /// 효과 처리기(01 §7-2·§7-3, M3-f). 창에서 <see cref="Trigger"/>가 그 창의 자동 효과를 GameState.Stack에 프레임으로 쌓고, <see cref="Run"/>이 맨 위 프레임을
    /// op 하나씩 실행한다. 프레임은 상태 안의 데이터라 어느 순간이든 복제·저장된다(01 §2-3). 선택이 필요한 op는 PendingChoice를 열고 Wait로 멈춘다
    /// (M5 효과 기반: op Ask · 처리기의 <see cref="Wait"/> — 답은 Locals에 넣고 Run이 이어 간다). 처리기 절(EffectDef.Handler)은 op 대신 등록부의 C#을 부른다.
    /// 한도(Limit)·대상 없음(OnNoTarget, G3)은 프레임을 처음 돌 때 본다(rulings G16). 상시 효과(공격력·피해 보정)는 프레임이 아니라 <see cref="StrengthMods"/>·
    /// <see cref="DamageMods"/>가 계산할 때 훑는다(01 §7-1). 배틀 창만 쓴다: AfterRoll(면 바꾸기) · BeforeDamage(기술 효과·혼란) · BattleTurnEnd(독).
    /// 순서는 rulings G6: 공격 쪽(상태이상 → 기술 절, 데이터 순) → 받는 쪽(상태이상). 굴린 뒤 면 바꾸기만 졸림 먼저(<see cref="OrderFaceChanges"/>, 원작 커뮤니티 확인).
    /// 같은 창의 반응 창은 자동 효과가 다 돈 뒤에 연다.
    /// </summary>
    public static class EffectRules
    {
        /// <summary>창 w의 자동 효과를 Stack에 쌓는다. 먼저 처리할 것이 맨 위(끝)다. 기절한 포켓몬의 상태이상은 훑지 않는다.</summary>
        public static void Trigger(GameState s, Ctx ctx, Window w)
        {
            var b = s.Battle;
            if (b == null || b.ActiveSide < 0) return;
            int k = b.ActiveSide;
            int att = b.Sides[k].Active;
            int def = b.Sides[1 - k].Active;
            if (w == Window.AfterRoll && DiceLocked(s, ctx))
            {
                // sacred_sword(rulings G21): 굴린 면을 바꾸거나 지우는 효과가 없다 — 마비의 「바로 다음 공격」도 이 공격에 쓴 것이다
                if (att >= 0) s.Pokemon[att].ParalysisPending = false;
                return;
            }
            var frames = new List<EffectFrame>(4);
            AddStatus(s, ctx, frames, w, EffectRole.Attacker, att, def, b.Sides[k].Owner);
            if (att >= 0)
                foreach (int id in ctx.Effects.OfMove(BattleRules.EffectMove(s, b.Move))) // kinesis가 복사한 · mimic이 부른 기술의 효과(G21)
                {
                    var e = ctx.Effects.Effects[id];
                    if (e.Window == w && !e.IsStatic) frames.Add(Frame(e, b.Sides[k].Owner, att, def));
                }
            AddItem(s, ctx, frames, w, EffectRole.Attacker, att, def, b.Sides[k].Owner);
            AddMods(s, ctx, frames, w, EffectRole.Attacker, att, def, b.Sides[k].Owner);
            AddStatus(s, ctx, frames, w, EffectRole.Defender, def, att, b.Sides[1 - k].Owner);
            AddItem(s, ctx, frames, w, EffectRole.Defender, def, att, b.Sides[1 - k].Owner);
            AddMods(s, ctx, frames, w, EffectRole.Defender, def, att, b.Sides[1 - k].Owner);
            if (w == Window.AfterRoll) OrderFaceChanges(s, ctx, frames);
            for (int i = frames.Count - 1; i >= 0; i--) s.Stack.Add(frames[i]);
        }

        /// <summary>졸림 둘이 서로 상쇄한 면 바꾸기 프레임(Locals[0]). 실행하면 아무 면도 바꾸지 않고 0 결과를 낸다(F10).</summary>
        public const int FaceChangeCancelled = 1;

        /// <summary>
        /// 굴린 뒤 면 바꾸기(AfterRoll)의 상태이상 효과 순서(rulings G14 · 원작 커뮤니티 확인). frames = 공격하는 쪽 → 받는 쪽 순서로 쌓인 그 창의 프레임(앞이 먼저 돈다).
        /// 기본(자동) 순서: 졸림이 먼저(「Drowsy is intended to trigger first」 — 공격하는 쪽 졸림 → 받는 쪽 졸림), 그다음 마비가 남은 일반 타격을 지운다
        /// (마비된 공격자 대 졸린 대상: 「I'd let one of the regular hits convert to a critical hit before removing the rest」).
        /// 둘 다 졸림이고 지금 굴림의 일반 타격이 1개뿐이면 서로 상쇄해 그대로 둔다(「Keep it as a Regular Hit」), 2개 이상이면 둘 다 처리한다(「as many effects as possible」).
        /// 결정 72(효과가 충돌하면 공격자가 순서를 고른다): 이 순서는 기본값이고, 순서에 따라 결과가 갈리면 배틀 단계가 <see cref="AskOrder"/>로 공격자에게 묻는다.
        /// </summary>
        public static void OrderFaceChanges(GameState s, Ctx ctx, List<EffectFrame> frames)
        {
            var drowsy = new List<EffectFrame>(2);
            var rest = new List<EffectFrame>(2);
            foreach (var f in frames)
            {
                var e = ctx.Effects.Effects[f.EffectId];
                if (e.IsStatus && e.Source.Id == (int)StatusEffect.Drowsy) drowsy.Add(f);
                else rest.Add(f);
            }
            if (drowsy.Count == 2 && AttackDice.Count(s.Battle.Faces, AttackDieFace.RegularHit) == 1)
                foreach (var f in drowsy) f.Locals[0] = FaceChangeCancelled;
            frames.Clear();
            frames.AddRange(drowsy);
            frames.AddRange(rest);
        }

        /// <summary>면 바꾸기 op 하나(마비 RemoveFaces · 졸림 ReplaceFace)를 faces에 적용한다 — 실행(Exec)과 순서 미리 셈(<see cref="Preview"/>)이 같이 쓴다. changes가 있으면 바꾼 자리를 적는다.</summary>
        static void ChangeFaces(List<AttackDieFace> faces, EffectOpDef o, bool cancelled, List<FaceChange> changes)
        {
            int left = o.Op == EffectOp.RemoveFaces ? (o.Value > 0 ? o.Value : int.MaxValue) : cancelled ? 0 : o.Value;
            var to = o.Op == EffectOp.RemoveFaces ? AttackDieFace.None : o.To;
            for (int i = 0; i < faces.Count && left > 0; i++)
            {
                if (faces[i] != o.Face) continue;
                changes?.Add(new FaceChange { Index = i, Before = faces[i], After = to });
                faces[i] = to;
                left--;
            }
        }

        // ── 결정 72: 면 바꾸기 순서를 공격자가 고른다 ──

        /// <summary>
        /// 순서 하나의 결과(결정 72 선택 창이 보이는 값): 그 순서대로 면을 바꾼 마지막 굴림, 그 굴림의 피해(기술 효과·상성·화상까지 — BattleFlow 피해 계산 그대로), 받는 포켓몬이 쓰러지나, 공격하는 포켓몬이 자기 피해로 쓰러지나.
        /// Order = 적용 순서의 효과 Id(앞이 먼저).
        /// </summary>
        public sealed class OrderOutcome
        {
            public List<int> Order = new List<int>();
            public List<AttackDieFace> Faces = new List<AttackDieFace>();
            public int Damage;
            public bool Faints;
            /// <summary>공격하는 포켓몬이 자기 피해(기술 반동 · 혼란)로 쓰러지나 — 크리마다 반동인 기술은 순서에 따라 갈린다.</summary>
            public bool AttackerFaints;

            /// <summary>결과가 같다: 피해 · 기절(받는 쪽 · 공격하는 쪽)이 같다(결정 73 ② — 주사위 눈(면 종류별 개수·자리)과 반동 양만 다르면 같은 결과다, G14).</summary>
            public bool SameAs(OrderOutcome o) => Damage == o.Damage && Faints == o.Faints && AttackerFaints == o.AttackerFaints;
        }

        /// <summary>Stack 맨 위의 아직 돌지 않은 굴린 뒤 면 바꾸기 프레임(AfterRoll), 적용 순서(맨 위가 먼저).</summary>
        static List<EffectFrame> FaceFrames(GameState s, Ctx ctx)
        {
            var r = new List<EffectFrame>(3);
            for (int i = s.Stack.Count - 1; i >= 0; i--)
            {
                var f = s.Stack[i];
                if (f.StepIndex != 0 || ctx.Effects.Effects[f.EffectId].Window != Window.AfterRoll) break;
                r.Add(f);
            }
            return r;
        }

        /// <summary>그 순서(효과 Id, 앞이 먼저)로 지금 굴림의 면을 바꾸면 어떻게 되나. 상태를 바꾸지 않는다 — 보기 사본에서도 된다(굴림·상태이상은 공개 정보).</summary>
        public static OrderOutcome Preview(GameState s, Ctx ctx, IReadOnlyList<int> order)
        {
            var b = s.Battle;
            var frames = FaceFrames(s, ctx);
            var r = new OrderOutcome();
            r.Faces.AddRange(b.Faces);
            foreach (int id in order)
            {
                var f = frames.Find(x => x.EffectId == id);
                if (f == null) throw new ArgumentException($"효과 {id}: 지금 쌓인 면 바꾸기가 아니다", nameof(order));
                r.Order.Add(id);
                if (EffectDb.IsModEffect(id))
                {
                    // 지속 보정의 면 지우기(「remove 1 regular hit from the roll」): 그 포켓몬에 걸린 것을 한꺼번에(Step과 같다)
                    ModRules.RemoveFaces(s, f.User, id == EffectDb.ModFacesAttacker ? EffectRole.Attacker : EffectRole.Defender, r.Faces, null);
                    continue;
                }
                var e = ctx.Effects.Effects[id];
                // 조건은 그 프레임이 도는 때의 굴림으로 본다(Step과 같다)
                if (e.When != null && !Holds(s, ctx, e.When, f.User >= 0 ? s.Pokemon[f.User] : null, f.Target >= 0 ? s.Pokemon[f.Target] : null,
                        AttackDice.Count(r.Faces, AttackDieFace.StatusSymbol), b.Move)) continue;
                foreach (var o in e.Ops)
                    if (o.Op == EffectOp.RemoveFaces || o.Op == EffectOp.ReplaceFace) ChangeFaces(r.Faces, o, f.Locals[0] == FaceChangeCancelled, null);
            }
            int k = b.ActiveSide;
            int def = b.Sides[1 - k].Active;
            int att = b.Sides[k].Active;
            var calc = BattleFlow.Damage(s, ctx, b, r.Faces, null);
            r.Damage = calc.Final;
            // 버퍼에 이미 든 피해·회복(굴릴 때의 피로 3 등)까지 적용한 체력과 견준다 — 이 공격의 피해·반동도 그 뒤에 버퍼로 든다
            r.Faints = def >= 0 && r.Damage >= HpAfterBuffer(s, ctx, b, def);
            r.AttackerFaints = att >= 0 && Recoil(s, ctx, att, def, r.Faces) + calc.Reflect >= HpAfterBuffer(s, ctx, b, att); // 배리어가 되돌린 크리티컬도 순서에 따라 갈린다
            return r;
        }

        /// <summary>그 굴림(faces)으로 공격하는 포켓몬이 받을 자기 피해(BeforeDamage 창의 SelfDamage — 혼란 · 기술 반동, Exec와 같은 셈). 회복은 보지 않는다(반동과 함께 쓰는 기술이 없다).</summary>
        static int Recoil(GameState s, Ctx ctx, int att, int def, List<AttackDieFace> faces)
        {
            var b = s.Battle;
            var u = s.Pokemon[att];
            var t = def >= 0 ? s.Pokemon[def] : null;
            int symbols = AttackDice.Count(faces, AttackDieFace.StatusSymbol);
            int n = 0;
            void Add(EffectDef e)
            {
                if (e.Window != Window.BeforeDamage || e.IsStatic || (e.When != null && !Holds(s, ctx, e.When, u, t, symbols, b.Move))) return;
                foreach (var o in e.Ops)
                    if (o.Op == EffectOp.SelfDamage) n += o.Face == AttackDieFace.None ? o.Value : o.Value * AttackDice.Count(faces, o.Face);
            }
            var statuses = new List<StatusEffect>(2);
            StatusRules.Statuses(u, statuses);
            foreach (var st in statuses)
                for (int id = 0; id < EffectDb.BuiltInCount; id++)
                {
                    var e = ctx.Effects.Effects[id];
                    if (e.IsStatus && e.Role == EffectRole.Attacker && e.Source.Id == (int)st) Add(e);
                }
            foreach (int id in ctx.Effects.OfMove(BattleRules.EffectMove(s, b.Move))) Add(ctx.Effects.Effects[id]);
            return n + HeldItems.BlastDamage(ctx, t); // 폭발수정: 첫 공격이면 공격자도 1(S10)
        }

        /// <summary>
        /// 결정 72 선택 창의 합법 답(ChoiceSub.EffectOrder): 쌓인 면 바꾸기의 순열마다 결과를 셈해 서로 다른 결과(피해·기절 — 받는 쪽·공격하는 쪽, <see cref="OrderOutcome.SameAs"/>)마다 순서 하나(PickMany(효과 Id, 앞이 먼저)).
        /// 첫 답 = 기본 순서(<see cref="OrderFaceChanges"/> — 원작 커뮤니티 확인)의 결과, 나머지는 순열 순서(원래 순서 기준 사전 순)로 처음 나온 순서. 프레임이 둘 미만이면 기본 순서 하나.
        /// </summary>
        public static void OrderOptions(GameState s, Ctx ctx, List<ChoiceAnswer> into)
        {
            into.Clear();
            var ids = new List<int>();
            foreach (var f in FaceFrames(s, ctx)) ids.Add(f.EffectId);
            if (ids.Count == 0) return;
            var seen = new List<OrderOutcome>();
            Permute(ids, 0, perm =>
            {
                var r = Preview(s, ctx, perm);
                if (seen.Exists(x => x.SameAs(r))) return;
                seen.Add(r);
                into.Add(ChoiceAnswer.PickMany(perm));
            });
        }

        static void Permute(List<int> a, int at, Action<List<int>> f)
        {
            if (at >= a.Count - 1)
            {
                f(new List<int>(a));
                return;
            }
            for (int i = at; i < a.Count; i++)
            {
                (a[at], a[i]) = (a[i], a[at]);
                Permute(a, at + 1, f);
                (a[at], a[i]) = (a[i], a[at]);
            }
        }

        /// <summary>
        /// 공격자에게 가장 유리한 답(사람이 고르지 않는 쪽 — 야생·갤럭시단·UB·봇, 결정 72 [가정]): 받는 포켓몬이 쓰러지는 것 → 공격하는 포켓몬이 쓰러지지 않는 것 → 피해가 큰 것 → 합법 답 순서(기본 순서가 앞).
        /// </summary>
        public static ChoiceAnswer BestOrder(GameState s, Ctx ctx, List<ChoiceAnswer> legal)
        {
            ChoiceAnswer best = null;
            OrderOutcome bestR = null;
            foreach (var a in legal)
            {
                var r = Preview(s, ctx, a.Values);
                if (bestR == null || (r.Faints && !bestR.Faints)
                    || (r.Faints == bestR.Faints && ((!r.AttackerFaints && bestR.AttackerFaints) || (r.AttackerFaints == bestR.AttackerFaints && r.Damage > bestR.Damage))))
                {
                    best = a;
                    bestR = r;
                }
            }
            return best;
        }

        /// <summary>
        /// 굴린 뒤 면 바꾸기를 쌓은 다음(배틀 단계 Roll): 순서에 따라 결과가 갈리면 공격자에게 묻는다(결정 72 — 원작 커뮤니티 확인).
        /// 좌석이 공격하면 선택(Reaction/EffectOrder, 공격하는 쪽 응답자)을 열고, 가상 쪽(야생·갤럭시단·UB)은 묻지 않고 <see cref="BestOrder"/>로 늘어놓는다.
        /// 결과(피해·기절 — 공격하는 쪽의 반동 기절 포함)가 하나뿐이면 묻지 않는다 — 눈 개수·반동 양만 다르면 기본 순서 그대로(결정 73 ②). 옛 판(<see cref="RuleOptions.FixedEffectOrder"/>)은 묻지 않고 기본 순서 그대로다.
        /// </summary>
        internal static void AskOrder(GameState s, Ctx ctx, Emitter fx)
        {
            var b = s.Battle;
            if (b == null || b.ActiveSide < 0 || s.Options.FixedEffectOrder) return;
            var legal = new List<ChoiceAnswer>();
            OrderOptions(s, ctx, legal);
            if (legal.Count < 2) return;
            var side = b.Sides[b.ActiveSide];
            if (side.Responder >= 0 && side.Responder < s.SeatCount)
            {
                ChoiceRules.Open(s, ctx, fx, new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.EffectOrder, Seat = side.Responder, Subject = side.Active });
                return;
            }
            ApplyOrder(s, ctx, BestOrder(s, ctx, legal));
        }

        /// <summary>순서 답(효과 Id, 앞이 먼저)대로 쌓인 면 바꾸기 프레임을 다시 늘어놓는다(맨 위 = 먼저). 이어서 진행 루프가 돈다.</summary>
        public static void ApplyOrder(GameState s, Ctx ctx, ChoiceAnswer a)
        {
            var frames = FaceFrames(s, ctx);
            if (a.Values.Count != frames.Count) throw new InvalidOperationException($"순서 답 {a}: 쌓인 면 바꾸기 {frames.Count}개와 수가 다르다");
            s.Stack.RemoveRange(s.Stack.Count - frames.Count, frames.Count);
            for (int i = a.Values.Count - 1; i >= 0; i--) s.Stack.Add(frames.Find(f => f.EffectId == a.Values[i]));
        }

        static void AddStatus(GameState s, Ctx ctx, List<EffectFrame> frames, Window w, EffectRole role, int user, int target, int owner)
        {
            if (user < 0) return;
            var p = s.Pokemon[user];
            if (p.Status == StatusEffect.None || PokemonRules.IsFainted(ctx, p)) return;
            // 동시에 건 상태 둘(S7_6)이면 첫 칸 → 둘째 칸 순서로 둘 다
            var statuses = new List<StatusEffect>(2);
            StatusRules.Statuses(p, statuses);
            foreach (var st in statuses)
            {
                if (st == StatusEffect.Drowsy && TrainerRules.Dreaming(s, ctx, p)) continue; // fairy_tale_girl R: 졸림의 효과가 「턴 끝 1 회복」으로 바뀐다(rulings A35)
                for (int id = 0; id < EffectDb.BuiltInCount; id++)
                {
                    var e = ctx.Effects.Effects[id];
                    if (e.IsStatus && e.Window == w && e.Role == role && e.Source.Id == (int)st) frames.Add(Frame(e, owner, user, target));
                }
            }
        }

        /// <summary>지닌물건의 굴린 뒤 면 바꾸기(행운의 알 = 공격하는 쪽, 진화의 휘석 = 받는 쪽 — HeldItems.FaceEffect). 프레임의 출처가 그 아이템이다. 순서는 그 쪽의 상태이상·기술 절 뒤(G6).</summary>
        static void AddItem(GameState s, Ctx ctx, List<EffectFrame> frames, Window w, EffectRole role, int user, int target, int owner)
        {
            if (user < 0) return;
            var p = s.Pokemon[user];
            int id = HeldItems.FaceEffect(ctx, p, role);
            if (id < 0 || ctx.Effects.Effects[id].Window != w || PokemonRules.IsFainted(ctx, p)) return;
            var f = Frame(ctx.Effects.Effects[id], owner, user, target);
            f.Source = new SourceRef(SourceKind.Item, HeldItems.ItemOf(ctx, p, role == EffectRole.Attacker ? HeldItems.LuckyEgg : HeldItems.Eviolite));
            frames.Add(f);
        }

        /// <summary>
        /// 지속 보정의 프레임(M5 지속 보정 갈래, rulings G19): 굴린 뒤 면 지우기(그 자리의 보정이 있을 때 — 공격하는 쪽·받는 쪽 하나씩) · 공격받을 때 공격자에게 상태이상(받는 쪽).
        /// 순서는 그 쪽의 상태이상·기술 절·지닌물건 뒤(G6). 프레임의 출처 = 건 기술(여럿이면 먼저 건 것 — 화면의 순서 줄이 이름으로 쓴다, <see cref="OrderSource"/>).
        /// </summary>
        static void AddMods(GameState s, Ctx ctx, List<EffectFrame> frames, Window w, EffectRole role, int user, int target, int owner)
        {
            if (user < 0 || PokemonRules.IsFainted(ctx, s.Pokemon[user])) return;
            int id = -1;
            var src = SourceRef.None;
            if (w == Window.AfterRoll)
            {
                if (ModRules.RemovesFaces(s, user, role, out src)) id = role == EffectRole.Attacker ? EffectDb.ModFacesAttacker : EffectDb.ModFacesDefender;
            }
            else if (w == Window.BeforeDamage && role == EffectRole.Defender)
            {
                var held = new List<PokemonMod>(1);
                ModRules.AttackedStatuses(s, user, held);
                if (held.Count == 0) ModRules.Retaliations(s, user, held);
                if (held.Count > 0)
                {
                    id = EffectDb.ModAttacked;
                    src = held[0].Source;
                }
            }
            if (id < 0) return;
            var f = Frame(ctx.Effects.Effects[id], owner, user, target);
            f.Source = src;
            frames.Add(f);
        }

        /// <summary>결정 72 순서 줄의 그 효과 이름에 쓸 출처: 상태이상·기술 절은 정의의 출처, 지닌물건·지속 보정은 쌓인 프레임의 출처(그 아이템 · 건 기술).</summary>
        public static SourceRef OrderSource(GameState s, Ctx ctx, int effectId)
        {
            var e = ctx.Effects.Effects[effectId];
            if (e.Source.Kind != SourceKind.Item && !EffectDb.IsModEffect(effectId)) return e.Source;
            foreach (var f in s.Stack)
                if (f.EffectId == effectId) return f.Source;
            return e.Source;
        }

        /// <summary>지속 보정 프레임 한 걸음(늘 한 걸음에 끝난다): 그 포켓몬에 걸린 보정을 건 순서대로 한다. 이벤트의 출처 = 보정마다 건 기술.</summary>
        static void StepMods(GameState s, Ctx ctx, Emitter fx, EffectDef e, EffectFrame f)
        {
            var b = s.Battle;
            if (e.Id == EffectDb.ModAttacked)
            {
                // 「When the user is attacked, inflict the Frozen status on the attacker」(frigid_ice): 프레임의 User = 공격받은 포켓몬, Target = 공격자
                if (f.Target < 0) return;
                var held = new List<PokemonMod>(1);
                ModRules.AttackedStatuses(s, f.User, held);
                foreach (var m in held) Inflict(s, ctx, fx, f.Target, (StatusEffect)m.Value, m.Source);
                // shell_trap 「the attacker takes 5 damage if any critical hits are rolled」: 마지막 굴림에 그 면이 있으면 공격자에게(그 턴 피해 버퍼, rulings G20)
                ModRules.Retaliations(s, f.User, held);
                foreach (var m in held)
                    if (AttackDice.Count(b.Faces, m.Face) > 0) BattleFlow.Buffer(b, f.Target, -m.Value, m.Source);
                return;
            }
            ModRules.RemoveFaces(s, f.User, e.Role, b.Faces, (m, changes, before) =>
            {
                if (fx.On) fx.Add(new DiceFacesChanged { Source = m.Source, Changes = changes.ToArray(), SrcBefore = before, SrcAfter = AttackDice.Src(b.Faces) });
            });
        }

        /// <summary>상태이상을 건다(면역·이미 걸림은 Result — 0 결과도 이벤트). 별 방패: 첫 공격을 받는 턴에는 걸리는 상태이상을 막는다(S10).</summary>
        static void Inflict(GameState s, Ctx ctx, Emitter fx, int pokemon, StatusEffect status, SourceRef src)
        {
            // hex_maniac(rulings A34): 이 op에 물어 둔 답 — 상태 주사위로 바꾼 상태 · 이미 걸린 상태가 있어도 더 건다
            var b = s.Battle;
            bool hex = b != null && b.HexAsked;
            if (hex && b.HexAs > 0) status = (StatusEffect)b.HexAs;
            var r = HeldItems.Shielded(ctx, s.Pokemon[pokemon]) ? StatusResult.Immune
                : hex && b.HexExtra ? StatusRules.InflictExtra(ctx, s.Pokemon[pokemon], status) : StatusRules.Inflict(ctx, s.Pokemon[pokemon], status);
            if (fx.On) fx.Add(new StatusApplied { Source = src, PokemonId = pokemon, Status = status, Result = r });
        }

        /// <summary>상태이상 하나를 건다(처리기용 — op Inflict와 같다).</summary>
        internal static void InflictOne(GameState s, Ctx ctx, Emitter fx, int pokemon, StatusEffect status, SourceRef src) => Inflict(s, ctx, fx, pokemon, status, src);

        /// <summary>둘을 동시에 건다(StatusRules.InflictBoth — 별 방패면 둘 다 막는다). 결과마다 StatusApplied.</summary>
        internal static void InflictBoth(GameState s, Ctx ctx, Emitter fx, int pokemon, StatusEffect a, StatusEffect b, SourceRef src)
        {
            StatusResult ra, rb;
            if (HeldItems.Shielded(ctx, s.Pokemon[pokemon])) ra = rb = StatusResult.Immune;
            else if (s.Battle != null && s.Battle.HexAsked && s.Battle.HexExtra)
            {
                // hex_maniac R: 이미 걸린 상태가 있어도 둘을 더 건다(rulings A34)
                ra = StatusRules.InflictExtra(ctx, s.Pokemon[pokemon], a);
                rb = StatusRules.InflictExtra(ctx, s.Pokemon[pokemon], b);
            }
            else StatusRules.InflictBoth(ctx, s.Pokemon[pokemon], a, b, out ra, out rb);
            if (!fx.On) return;
            fx.Add(new StatusApplied { Source = src, PokemonId = pokemon, Status = a, Result = ra });
            fx.Add(new StatusApplied { Source = src, PokemonId = pokemon, Status = b, Result = rb });
        }

        // ── 나머지 op 갈래(rulings G20): 효과가 여는 고르기 · 버퍼 셈 ──

        /// <summary>그 효과 주인 쪽의 응답자(배틀 쪽 Responder). 배틀 밖이면 −1.</summary>
        internal static int Responder(GameState s, int owner)
        {
            var b = s.Battle;
            if (b == null) return -1;
            foreach (var side in b.Sides)
                if (side.Owner == owner) return side.Responder;
            return -1;
        }

        /// <summary>
        /// 효과가 여는 고르기(Reaction/EffectPick — 01 §7-2 Wait): options에서 하나(Pick(값)), mayDecline이면 「안 함」(Decline)이 첫 답이다. 좌석이 아니면(가상 쪽) 묻지 않고 virtualPick을 쓴다.
        /// 답이 와 있으면 answer에 넣고 Continue, 물으면 Wait, 가상 쪽이 안 고르면(virtualPick &lt; 0) answer = −1.
        /// </summary>
        internal static EffectStepResult Pick(GameState s, Ctx ctx, Emitter fx, EffectFrame f, int seat, List<int> options, bool mayDecline, SourceRef src, int virtualPick, out int answer)
        {
            answer = -1;
            if (HasAnswer(f))
            {
                if ((AnswerKind)f.Locals[EffectFrame.LocalAnswerKind] == AnswerKind.Pick) answer = f.Locals[EffectFrame.LocalAnswerTarget];
                return EffectStepResult.Continue;
            }
            if (seat < 0 || seat >= s.SeatCount)
            {
                answer = virtualPick;
                return EffectStepResult.Continue;
            }
            var c = new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.EffectPick, Seat = seat, Source = src, Subject = f.User, Count = mayDecline ? 0 : 1 };
            c.Options.AddRange(options);
            return Wait(s, ctx, fx, f, c);
        }

        /// <summary>
        /// 대상이 TargetOther(「another Pokémon in the target party」)면 후보 가운데 하나만 남긴다: 하나뿐이면 그것, 둘 이상이면 효과 주인이 고른다(가상 쪽 = 후보 첫째 [가정]).
        /// 그 밖의 대상은 Targets 그대로. who를 채우고 Continue, 물으면 Wait.
        /// </summary>
        static EffectStepResult PickOther(GameState s, Ctx ctx, Emitter fx, EffectFrame f, EffectOpDef o, SourceRef src, List<int> who)
        {
            Targets(s, ctx, o.Who, f, who);
            if (o.Who != EffectWho.TargetOther || who.Count <= 1) return EffectStepResult.Continue;
            var r = Pick(s, ctx, fx, f, Responder(s, f.Owner), who, false, src, who[0], out int p);
            if (r != EffectStepResult.Continue) return r;
            if (p < 0 || !who.Contains(p)) p = who[0];
            who.Clear();
            who.Add(p);
            return EffectStepResult.Continue;
        }

        /// <summary>버퍼(그 턴 피해·회복)를 다 적용하면 그 포켓몬의 체력이 얼마가 되나(ApplyBuffer와 같은 셈 — 독이면 회복 0). clamp = false면 0 아래로도 센다(넘친 피해).</summary>
        internal static int HpAfterBuffer(GameState s, Ctx ctx, BattleState b, int pokemon, bool clamp = true)
        {
            var pk = s.Pokemon[pokemon];
            int max = PokemonRules.MaxHp(ctx, pk);
            int hp = PokemonRules.Hp(ctx, pk);
            var buf = b.DamageBuffer;
            for (int i = 0; i < buf.Count; i += 4)
            {
                if (buf[i] != pokemon) continue;
                int d = buf[i + 1];
                if (d > 0 && StatusRules.BlocksHealing(pk)) d = 0;
                if (d > 0) d += TrainerRules.HealBonus(s, ctx, pk);
                hp = Math.Min(max, hp + d);
                if (clamp) hp = Math.Max(0, hp);
            }
            return hp;
        }

        /// <summary>버퍼에 든 그 포켓몬의 피해 합(회복은 빼지 않는다 — 「damage taken」).</summary>
        static int Taken(BattleState b, int pokemon)
        {
            int n = 0;
            var buf = b.DamageBuffer;
            for (int i = 0; i < buf.Count; i += 4)
                if (buf[i] == pokemon && buf[i + 1] < 0) n -= buf[i + 1];
            return n;
        }

        /// <summary>
        /// bug_bite 「Consume an Apricorn from the bag of target. The target decides which one.」: 대상 주인이 좌석이고 규토리가 있으면 그 좌석이 색을 고른다(한 색뿐이면 묻지 않는다).
        /// 가방이 없는 쪽(야생·갤럭시단 — 원작 커뮤니티 확인)은 아무 일도 없다. ApricornSpent(Source = 그 기술).
        /// </summary>
        static EffectStepResult ConsumeApricorn(GameState s, Ctx ctx, Emitter fx, EffectFrame f, SourceRef src)
        {
            if (f.Target < 0) return EffectStepResult.Continue;
            int owner = s.Pokemon[f.Target].Owner;
            if (owner < 0) return EffectStepResult.Continue;
            var colors = new List<int>(SeatState.ApricornColors);
            for (int c = 0; c < SeatState.ApricornColors; c++)
                if (s.Seats[owner].Apricorns[c] > 0) colors.Add(c);
            if (colors.Count == 0) return EffectStepResult.Continue;
            int color = colors[0];
            if (colors.Count > 1)
            {
                var r = Pick(s, ctx, fx, f, Responder(s, owner), colors, false, src, colors[0], out color);
                if (r != EffectStepResult.Continue) return r;
                if (!colors.Contains(color)) color = colors[0];
            }
            SpendApricorn(s, fx, owner, color, src);
            return EffectStepResult.Continue;
        }

        /// <summary>좌석의 규토리 1개를 쓴다(ApricornSpent).</summary>
        internal static void SpendApricorn(GameState s, Emitter fx, int seat, int color, SourceRef src)
        {
            int before = s.Seats[seat].Apricorns[color];
            s.Seats[seat].Apricorns[color]--;
            if (fx.On)
                fx.Add(new ApricornSpent
                {
                    Seat = seat, Source = src,
                    Changes = new[] { new ResChange { Seat = seat, Res = ResKind.Apricorn, Key = color, Before = before, After = before - 1 } },
                });
        }

        /// <summary>그 기술이 프리즘 아머를 무시한다(multi_attack, 표지 op IgnorePrism). 프리즘 아머(울트라 버스트 캠페인, M6)가 피해 감소 전에 본다.</summary>
        public static bool IgnoresPrism(Ctx ctx, int move) => HasMarker(ctx, move, EffectOp.IgnorePrism);

        /// <summary>그 기술을 파티(벤치)에서 쓸 수 있다(lunge·acrobatics, 표지 op FromParty).</summary>
        public static bool UsableFromParty(Ctx ctx, int move) => HasMarker(ctx, move, EffectOp.FromParty);

        /// <summary>
        /// 지금 공격의 굴린 주사위가 고정이다(sacred_sword 「The dice rolled by this attack cannot be replaced or removed by any effect」, 표지 op LockDice — 복사·부른 기술 포함, rulings G21):
        /// 굴린 뒤 면 바꾸기(마비·졸림·지닌물건·지우기 보정)·매혹적인 스튜·디아루가 재굴림(원작 커뮤니티 확인)이 없다. 면 더하기(「add」)는 막지 않는다 [가정].
        /// </summary>
        public static bool DiceLocked(GameState s, Ctx ctx)
        {
            var b = s.Battle;
            return b != null && b.Move != -1 && HasMarker(ctx, BattleRules.EffectMove(s, b.Move), EffectOp.LockDice);
        }

        static bool HasMarker(Ctx ctx, int move, EffectOp marker)
        {
            foreach (int id in ctx.Effects.OfMove(move))
                foreach (var o in ctx.Effects.Effects[id].Ops)
                    if (o.Op == marker) return true;
            return false;
        }

        static EffectFrame Frame(EffectDef e, int owner, int user, int target) =>
            new EffectFrame { EffectId = e.Id, StepIndex = 0, Source = e.Source, Owner = owner, User = user, Target = target };

        /// <summary>Stack이 빌 때까지(또는 선택이 열릴 때까지) 맨 위 프레임을 한 걸음씩 돈다. 비었으면 true.</summary>
        public static bool Run(GameState s, Ctx ctx, Emitter fx)
        {
            for (int guard = 0; s.Stack.Count > 0; guard++)
            {
                if (s.Pending.Count > 0) return false;
                if (guard > 10000) throw new InvalidOperationException("효과 처리가 멈추지 않는다");
                var f = s.Stack[s.Stack.Count - 1];
                var r = Step(s, ctx, fx, f);
                if (r == EffectStepResult.Wait) return false;
                if (r == EffectStepResult.Done) s.Stack.RemoveAt(s.Stack.LastIndexOf(f));
            }
            return true;
        }

        /// <summary>프레임 한 걸음(01 §7-2): 처음(StepIndex 0)이면 조건을 보고, 맞으면 op 하나를 실행한다.</summary>
        static EffectStepResult Step(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            if (s.Battle == null) return EffectStepResult.Done; // 지금 효과는 모두 배틀 효과다. 배틀이 끝났으면 버린다
            var e = ctx.Effects.Effects[f.EffectId];
            // 처음 한 번(조건 → 한도 → 대상 없음). 선택을 기다렸다 돌아온 프레임은 다시 보지 않는다
            if (f.StepIndex == 0 && f.Locals[EffectFrame.LocalWait] == WaitNone)
            {
                if (e.When != null && !Holds(s, ctx, e.When, f.User, f.Target)) return EffectStepResult.Done;
                if (Spent(s, e, f.Owner, f.User)) return EffectStepResult.Done;
                if (e.OnNoTarget == EffectNoTarget.Skip && MissesTarget(s, ctx, e, f)) return EffectStepResult.Done; // G3: 효과 없이 넘어간다(한도도 쓰지 않는다)
            }
            if (EffectDb.IsModEffect(e.Id))
            {
                StepMods(s, ctx, fx, e, f);
                return EffectStepResult.Done;
            }
            if (e.Handler != null)
            {
                var hr = ctx.Effects.Handlers[e.Handler](s, ctx, fx, f);
                // StepIndex가 처리기의 자리표다(0 = 처음). 0인 채 Continue면 처음 검사(조건·한도)를 또 지나게 된다
                if (hr == EffectStepResult.Continue && f.StepIndex == 0) throw new InvalidOperationException($"처리기 {e.Handler}: Continue로 돌려줄 때는 StepIndex를 올린다");
                if (hr == EffectStepResult.Continue) f.Locals[EffectFrame.LocalWait] = WaitNone;
                return hr;
            }
            var o = e.Ops[f.StepIndex];
            bool inflicts = o.Op == EffectOp.Inflict || o.Op == EffectOp.InflictBoth || o.Op == EffectOp.InflictRolled;
            if (inflicts && f.Locals[EffectFrame.LocalWait] == WaitNone)
            {
                // hex_maniac B·R(rulings A34): 상태를 걸기 전에 묻는다 — 물음 프레임을 위에 쌓고, 끝나면 이 op가 다시 돈다
                var who = new List<int>(6);
                Targets(s, ctx, o.Who, f, who);
                if (TrainerRules.Hexing(s, ctx, o, f, who)) return EffectStepResult.Continue;
            }
            var r = Exec(s, ctx, fx, e, o, f);
            if (inflicts && r != EffectStepResult.Wait) TrainerRules.HexDone(s.Battle);
            if (r != EffectStepResult.Continue) return r;
            if (o.Op != EffectOp.Ask) Spend(s, ctx, f); // 한도는 실제로 한 일이 있을 때 쓴다(「안 씀」으로 답한 Ask는 쓰지 않는다)
            f.StepIndex++;
            f.Locals[EffectFrame.LocalWait] = WaitNone;
            return f.StepIndex >= e.Ops.Count ? EffectStepResult.Done : EffectStepResult.Continue;
        }

        // ── 선택을 여는 op·처리기(01 §7-2 Wait) ──

        /// <summary>EffectFrame.Locals[LocalWait] 값: 선택 없음 · 선택을 열고 기다림 · 답을 받음.</summary>
        public const int WaitNone = 0, WaitAsked = 1, WaitAnswered = 2;

        /// <summary>
        /// 프레임이 선택을 열고 멈춘다(op Ask · 처리기). 부르는 쪽이 PendingChoice의 Kind·Sub·Seat·Options 등을 채운다(ChoiceRules.Open과 같다 — Legal·Default는 그 Kind·Sub의 것).
        /// 답이 오면 <see cref="Answered"/>가 Locals[LocalAnswerKind]·[LocalAnswerTarget]에 넣고, Run이 같은 op·처리기를 다시 부른다(<see cref="HasAnswer"/>).
        /// </summary>
        public static EffectStepResult Wait(GameState s, Ctx ctx, Emitter fx, EffectFrame f, PendingChoice c)
        {
            if (f.Locals[EffectFrame.LocalWait] == WaitAsked) throw new InvalidOperationException($"효과 {f.EffectId}: 이미 선택을 기다린다");
            f.Locals[EffectFrame.LocalWait] = WaitAsked;
            ChoiceRules.Open(s, ctx, fx, c);
            return EffectStepResult.Wait;
        }

        /// <summary>맨 위 프레임이 선택의 답을 기다린다.</summary>
        public static bool Waiting(GameState s) => s.Stack.Count > 0 && s.Stack[s.Stack.Count - 1].Locals[EffectFrame.LocalWait] == WaitAsked;

        /// <summary>기다리던 맨 위 프레임에 답을 넣는다(Engine의 답 적용). 이어 도는 것은 Engine.Advance.</summary>
        internal static void Answered(GameState s, ChoiceAnswer a)
        {
            if (!Waiting(s)) throw new InvalidOperationException("답을 기다리는 효과 프레임이 없다");
            var f = s.Stack[s.Stack.Count - 1];
            f.Locals[EffectFrame.LocalWait] = WaitAnswered;
            f.Locals[EffectFrame.LocalAnswerKind] = (int)a.Kind;
            f.Locals[EffectFrame.LocalAnswerTarget] = a.Target;
        }

        /// <summary>프레임이 답을 받았다(처리기가 본다). 답 = (AnswerKind)Locals[LocalAnswerKind], Locals[LocalAnswerTarget].</summary>
        public static bool HasAnswer(EffectFrame f) => f.Locals[EffectFrame.LocalWait] == WaitAnswered;

        // ── 한도(01 §7-1 Limit, GameState.Used) · 대상 없음(G3) ──

        /// <summary>그 효과의 한도를 이미 썼다(owner = 효과 주인, user = 효과를 가진 포켓몬). 한도 없는 효과는 늘 false.</summary>
        public static bool Spent(GameState s, EffectDef e, int owner, int user)
        {
            if (e.Limit == EffectLimit.None) return false;
            foreach (var u in s.Used)
            {
                if (u.Kind != UsageKind.Effect || u.Key != e.Id || u.Seat != owner) continue;
                switch (e.Limit)
                {
                    case EffectLimit.Turn: if (u.Scope == s.TurnId) return true; break;
                    case EffectLimit.BattleTurn: if (s.Battle != null && u.Scope == s.Battle.Turn) return true; break;
                    case EffectLimit.UntilSwitch: if (u.Subject == user) return true; break;
                    default: return true; // Battle: 배틀이 끝날 때 지운다
                }
            }
            return false;
        }

        /// <summary>그 프레임의 효과가 한도를 쓴다(이미 썼으면 그대로). op 절은 처리기가 알아서 부르고, 처리기 절은 처리기가 실제로 일을 한 때 부른다.</summary>
        public static void Spend(GameState s, Ctx ctx, EffectFrame f)
        {
            var e = ctx.Effects.Effects[f.EffectId];
            if (e.Limit == EffectLimit.None || Spent(s, e, f.Owner, f.User)) return;
            s.Used.Add(new UsageMark
            {
                Seat = f.Owner, Kind = UsageKind.Effect, Key = e.Id,
                Scope = e.Limit == EffectLimit.Turn ? s.TurnId : e.Limit == EffectLimit.BattleTurn && s.Battle != null ? s.Battle.Turn : 0,
                Subject = e.Limit == EffectLimit.UntilSwitch ? f.User : -1,
            });
        }

        /// <summary>그 한도의 효과 기록을 지운다: Turn = 지난 맵 턴의 것만(턴 시작) · 그 밖 = 전부(배틀 끝).</summary>
        internal static void ClearLimits(GameState s, Ctx ctx, EffectLimit limit)
        {
            for (int i = s.Used.Count - 1; i >= 0; i--)
            {
                var u = s.Used[i];
                if (u.Kind != UsageKind.Effect || ctx.Effects.Effects[u.Key].Limit != limit) continue;
                if (limit == EffectLimit.Turn && u.Scope == s.TurnId) continue;
                s.Used.RemoveAt(i);
            }
        }

        /// <summary>그 포켓몬이 나갔다: 「나갈 때까지 한 번」 기록을 지운다(Subject는 그 한도만 쓴다).</summary>
        internal static void ClearLimitsOf(GameState s, int pokemon)
        {
            for (int i = s.Used.Count - 1; i >= 0; i--)
                if (s.Used[i].Kind == UsageKind.Effect && s.Used[i].Subject == pokemon) s.Used.RemoveAt(i);
        }

        /// <summary>대상 포켓몬을 쓰는 op 가운데 대상이 하나도 없는 것이 있다(G3).</summary>
        static bool MissesTarget(GameState s, Ctx ctx, EffectDef e, EffectFrame f)
        {
            var who = new List<int>(6);
            foreach (var o in e.Ops)
            {
                if (!o.NeedsWho) continue;
                Targets(s, ctx, o.Who, f, who);
                if (who.Count == 0) return true;
            }
            return false;
        }

        static EffectStepResult Exec(GameState s, Ctx ctx, Emitter fx, EffectDef e, EffectOpDef o, EffectFrame f)
        {
            var b = s.Battle;
            var src = e.Source.Kind == SourceKind.Item ? f.Source : e.Source; // 지닌물건 효과의 아이템 색인은 프레임이 싣는다
            var who = new List<int>(6);
            switch (o.Op)
            {
                case EffectOp.Inflict:
                {
                    var pick = PickOther(s, ctx, fx, f, o, src, who);
                    if (pick != EffectStepResult.Continue) return pick;
                    foreach (int p in who) Inflict(s, ctx, fx, p, o.Status, src);
                    break;
                }
                case EffectOp.Heal:
                {
                    // 회복도 그 턴 피해 버퍼로 동시에(p.13). 독이면 적용하는 순간 0이 된다(BattleFlow.ApplyBuffer, G14). Face가 있으면 그 면 하나마다(stinging_punch)
                    Targets(s, ctx, o.Who, f, who);
                    int n = o.Face == AttackDieFace.None ? o.Value : o.Value * AttackDice.Count(b.Faces, o.Face);
                    if (n > 0) foreach (int p in who) BattleFlow.Buffer(b, p, n, src);
                    break;
                }
                case EffectOp.CureStatus:
                {
                    Targets(s, ctx, o.Who, f, who);
                    var entries = new List<HealEntry>();
                    foreach (int p in who)
                    {
                        var pk = s.Pokemon[p];
                        if (pk.Status == StatusEffect.None || (o.Status != StatusEffect.None && !StatusRules.Has(pk, o.Status))) continue;
                        var before = pk.Status;
                        if (o.Status == StatusEffect.None) StatusRules.Clear(pk);
                        else StatusRules.Remove(pk, o.Status);
                        int hp = PokemonRules.Hp(ctx, pk);
                        entries.Add(new HealEntry { Id = p, HpBefore = hp, HpAfter = hp, StatusBefore = before });
                    }
                    // 상태이상만 푼 회복: Healed(체력 그대로). 푼 것이 없어도 낸다(0 결과, 01 §3-6 ⑥)
                    if (fx.On && who.Count > 0) fx.Add(new Healed { Seat = s.Pokemon[who[0]].Owner, Source = src, Pokemon = entries.ToArray() });
                    break;
                }
                case EffectOp.Fatigue:
                    Targets(s, ctx, o.Who, f, who);
                    foreach (int p in who)
                    {
                        var pk = s.Pokemon[p];
                        int before = pk.Fatigue;
                        pk.Fatigue = Math.Min(FatigueRules.Max, pk.Fatigue + o.Value);
                        if (fx.On) fx.Add(new FatigueChanged { Source = src, PokemonId = p, Before = before, After = pk.Fatigue, Cause = FatigueCause.Effect });
                    }
                    break;
                case EffectOp.SelfDamage:
                {
                    int n = o.Face == AttackDieFace.None ? o.Value : o.Value * AttackDice.Count(b.Faces, o.Face);
                    if (n > 0 && f.User >= 0) BattleFlow.Buffer(b, f.User, -n, src);
                    break;
                }
                case EffectOp.RemoveFaces:
                case EffectOp.ReplaceFace:
                {
                    var faces = b.Faces;
                    var before = AttackDice.Src(faces);
                    var changes = new List<FaceChange>();
                    ChangeFaces(faces, o, f.Locals[0] == FaceChangeCancelled, changes);
                    // 마비 대기(G14)는 다음 공격 한 번에 쓰고 지운다 — 지울 일반 타격이 없었어도
                    if (e.When != null && e.When.UserParalysisPending && f.User >= 0) s.Pokemon[f.User].ParalysisPending = false;
                    if (fx.On) fx.Add(new DiceFacesChanged { Source = src, Changes = changes.ToArray(), SrcBefore = before, SrcAfter = AttackDice.Src(faces) });
                    break;
                }
                case EffectOp.Hold:
                    Targets(s, ctx, o.Who, f, who);
                    // 걸 것이 상태이상(공격받을 때 공격자에게)이면 보정의 Value = 그 상태
                    foreach (int p in who) ModRules.Add(s, p, o.Scope, o.Role, o.Held, o.Face, o.Held == EffectOp.Inflict ? (int)o.Status : o.Value, src);
                    break;
                case EffectOp.ExtendMods:
                    Targets(s, ctx, o.Who, f, who);
                    foreach (int p in who) ModRules.Extend(s.Pokemon[p]);
                    break;
                case EffectOp.FatigueTo:
                    Targets(s, ctx, o.Who, f, who);
                    foreach (int p in who)
                    {
                        var pk = s.Pokemon[p];
                        int before = pk.Fatigue;
                        pk.Fatigue = o.Value;
                        if (fx.On) fx.Add(new FatigueChanged { Source = src, PokemonId = p, Before = before, After = pk.Fatigue, Cause = FatigueCause.Effect });
                    }
                    break;
                case EffectOp.InflictBoth:
                    Targets(s, ctx, o.Who, f, who);
                    foreach (int p in who) InflictBoth(s, ctx, fx, p, o.Status, o.Status2, src);
                    break;
                case EffectOp.InflictRolled:
                    // 상태이상 주사위(여섯 면 = 여섯 상태): 대상이 있을 때만 굴린다(엔진 난수 — 결정적)
                    Targets(s, ctx, o.Who, f, who);
                    foreach (int p in who) Inflict(s, ctx, fx, p, StatusRules.RollDie(s.Rng), src);
                    break;
                case EffectOp.Damage:
                {
                    var pick = PickOther(s, ctx, fx, f, o, src, who);
                    if (pick != EffectStepResult.Continue) return pick;
                    foreach (int p in who)
                    {
                        int n = TrainerRules.SideDamage(s, ctx, p, o.Value); // 받는 쪽 트레이너 카드의 감소·문턱·상한(「from attacks」, rulings A32)
                        if (n > 0) BattleFlow.Buffer(b, p, -n, src);
                    }
                    break;
                }
                case EffectOp.FaintBelow:
                    // 「if they have 2 or less health remaining after this attack」: 이 턴 버퍼까지 더한 체력이 1~Value면 남은 만큼 더 깎는다(동시에 적용 — 기절은 기절 확인이)
                    Targets(s, ctx, o.Who, f, who);
                    foreach (int p in who)
                    {
                        int hp = HpAfterBuffer(s, ctx, b, p);
                        if (hp > 0 && hp <= o.Value) BattleFlow.Buffer(b, p, -hp, src);
                    }
                    break;
                case EffectOp.ConsumeApricorn:
                    return ConsumeApricorn(s, ctx, fx, f, src);
                case EffectOp.ReflectTaken:
                {
                    // 「Deal any damage taken by the user during this attack to the target as well」: 지금까지 버퍼에 든 그 포켓몬의 피해(피로·혼란 — 원작 커뮤니티 확인,
                    // 남은 체력과 상관없이 받은 양 — 원작 커뮤니티 확인). 턴 끝의 응징은 「during this attack」이 아니다(원작 커뮤니티 확인)
                    Targets(s, ctx, o.Who, f, who);
                    int taken = Taken(b, f.User);
                    if (taken > 0) foreach (int p in who) BattleFlow.Buffer(b, p, -taken, src);
                    break;
                }
                case EffectOp.CarryDamage:
                    Targets(s, ctx, o.Who, f, who);
                    foreach (int p in who)
                    {
                        int left = HpAfterBuffer(s, ctx, b, p, false);
                        int k2 = BattleFlow.SideOfAny(s, b, p);
                        if (left >= 0 || k2 < 0) continue;
                        b.Sides[k2].Carry += -left;
                        b.Sides[k2].CarrySource = src;
                    }
                    break;
                case EffectOp.ChangeForm:
                    // 「Change to X Form if possible」(rulings A26): 그 종의 그 상태 폼으로 — 없으면(다른 종이 배운 기술) · 금지(imprison)면 아무 일도 없다
                    Targets(s, ctx, o.Who, f, who);
                    foreach (int p in who) FormRules.Change(s, ctx, fx, p, FormRules.MoveForm(ctx, s.Pokemon[p], e.Source.Id, o.Value), src);
                    break;
                case EffectOp.SwitchOut:
                    // 교체는 배틀 턴 끝 — 턴 끝 효과(독·응징) 뒤(원작 커뮤니티 확인), BattleFlow.ReplaceStep이 연다
                    if (f.User >= 0) b.SwitchAfter = f.User;
                    break;
                case EffectOp.Ask:
                {
                    if (HasAnswer(f)) return (AnswerKind)f.Locals[EffectFrame.LocalAnswerKind] == AnswerKind.Accept ? EffectStepResult.Continue : EffectStepResult.Done;
                    // 가상 쪽(야생·갤럭시단·UB)은 묻지 않고 쓴다(G16 [가정] — 결정 72의 가상 쪽과 같은 결)
                    int k = b.Sides[0].Owner == f.Owner ? 0 : 1;
                    int responder = b.Sides[k].Responder;
                    if (responder < 0 || responder >= s.SeatCount) return EffectStepResult.Continue;
                    return Wait(s, ctx, fx, f, new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.EffectOption, Seat = responder, Source = src, Subject = f.User });
                }
                default:
                    throw new InvalidOperationException($"효과 {e.Id}: {o.Op}는 상시 op라 프레임으로 실행하지 않는다");
            }
            return EffectStepResult.Continue;
        }

        /// <summary>op 대상(EffectWho). Party = 효과 주인의 파티에서 기절하지 않은 것 전부(좌석 = 파티 순서, 가상 = 그 쪽 명단 — 기술 효과로는 기절한 포켓몬을 회복하지 못한다:
        /// 원작 커뮤니티 확인, rulings G20).</summary>
        static void Targets(GameState s, Ctx ctx, EffectWho who, EffectFrame f, List<int> into)
        {
            into.Clear();
            switch (who)
            {
                case EffectWho.Target:
                    if (f.Target >= 0) into.Add(f.Target);
                    break;
                case EffectWho.User:
                    if (f.User >= 0) into.Add(f.User);
                    break;
                case EffectWho.Party:
                {
                    if (f.User < 0) break;
                    int owner = s.Pokemon[f.User].Owner;
                    if (owner >= 0)
                    {
                        foreach (int p in s.Seats[owner].Party)
                            if (p >= 0 && !PokemonRules.IsFainted(ctx, s.Pokemon[p])) into.Add(p);
                    }
                    else if (s.Battle != null)
                        foreach (var side in s.Battle.Sides)
                            if (side.Owner == owner)
                                foreach (int p in side.Roster)
                                    if (!PokemonRules.IsFainted(ctx, s.Pokemon[p])) into.Add(p);
                    break;
                }
                case EffectWho.TargetOther:
                case EffectWho.TargetOthers:
                    // 대상 파티의 다른 포켓몬(기절하지 않은 것, 좌석 = 파티 순서 · 가상 = 명단 순서 — rulings G20). TargetOther는 하나를 PickOther가 고른다
                    if (f.Target >= 0 && s.Battle != null) PartyOf(s, ctx, f.Target, into, f.Target);
                    break;
                default:
                    throw new InvalidOperationException($"op 대상 {who}");
            }
        }

        /// <summary>그 포켓몬과 같은 쪽의 기절하지 않은 포켓몬(좌석 = 파티 순서, 가상 = 배틀 명단). except는 뺀다.</summary>
        internal static void PartyOf(GameState s, Ctx ctx, int pokemon, List<int> into, int except)
        {
            into.Clear();
            int owner = s.Pokemon[pokemon].Owner;
            var ids = new List<int>(6);
            if (owner >= 0) foreach (int p in s.Seats[owner].Party) { if (p >= 0) ids.Add(p); }
            else if (s.Battle != null)
                foreach (var side in s.Battle.Sides)
                    if (side.Owner == owner && side.Roster.Contains(pokemon)) ids.AddRange(side.Roster);
            foreach (int p in ids)
                if (p != except && !PokemonRules.IsFainted(ctx, s.Pokemon[p])) into.Add(p);
        }

        /// <summary>조건(EffectCondition)이 맞나. 대상 상태는 지금 상태 — 부르는 자리가 효과 전이다(G14).</summary>
        static bool Holds(GameState s, Ctx ctx, EffectCondition c, int user, int target)
        {
            var b = s.Battle;
            int symbols = b != null ? AttackDice.Count(b.Faces, AttackDieFace.StatusSymbol) : 0;
            return Holds(s, ctx, c, user >= 0 ? s.Pokemon[user] : null, target >= 0 ? s.Pokemon[target] : null, symbols, b != null ? b.Move : -1);
        }

        /// <summary>
        /// 상태를 아는 조건 판정: <see cref="Holds(Ctx, EffectCondition, PokemonState, PokemonState, int, int)"/> + 「파티에 다른 그 타입 포켓몬」(PartyType — 파티는 상태에서 읽는다).
        /// </summary>
        public static bool Holds(GameState s, Ctx ctx, EffectCondition c, PokemonState u, PokemonState t, int symbols, int move)
        {
            if (!Holds(ctx, c, u, t, symbols, move)) return false;
            if (c.PartyType != PokemonType.Typeless && (u == null || !PartyHasType(s, ctx, u, c.PartyType))) return false;
            return true;
        }

        /// <summary>attack_order 「If there is another non-fainted Bug-type Pokémon in your party」: 효과를 가진 포켓몬 말고 그 파티(좌석 = 파티, 가상 = 배틀 명단)의 기절하지 않은 그 타입.</summary>
        static bool PartyHasType(GameState s, Ctx ctx, PokemonState u, PokemonType type)
        {
            int self = s.Pokemon.IndexOf(u);
            if (self < 0) return false;
            var party = new List<int>(6);
            PartyOf(s, ctx, self, party, self);
            foreach (int p in party)
                if (HasType(ctx, s.Pokemon[p], type)) return true;
            return false;
        }

        /// <summary>wild_charge 「If the user knows another Electric-type move」: 그 포켓몬의 기술(시그니처 · 배운 칸 · 플랜 B) 가운데 이 기술 하나를 뺀 나머지에 그 타입이 있다.</summary>
        static bool KnowsOtherType(Ctx ctx, PokemonState u, int move, PokemonType type)
        {
            var moves = new List<int>(4);
            PokemonRules.MoveOptions(ctx, u, moves);
            bool skipped = false;
            foreach (int m in moves)
            {
                if (m == move && !skipped)
                {
                    skipped = true;
                    continue;
                }
                if (BattleRules.MoveType(ctx, m) == type) return true;
            }
            return false;
        }

        /// <summary>
        /// 조건 판정 본체. symbols = 마지막 굴림의 상태이상 심볼 수, move = 지금 쓰는 기술(「직전 턴과 같은 기술」). 원칙 AI(PrincipleRules, G15)가
        /// 굴리기 전 값을 셀 때도 쓴다 — 그때 symbols는 (s, r, c) 묶음의 심볼 수다.
        /// </summary>
        public static bool Holds(Ctx ctx, EffectCondition c, PokemonState u, PokemonState t, int symbols, int move)
        {
            if (c.MinSymbols > 0 && symbols < c.MinSymbols) return false;
            if (c.TargetAnyStatus && (t == null || t.Status == StatusEffect.None)) return false;
            if (c.TargetStatus != StatusEffect.None && (t == null || !StatusRules.Has(t, c.TargetStatus))) return false;
            if (c.TargetType != PokemonType.Typeless && (t == null || !HasType(ctx, t, c.TargetType))) return false;
            if (c.TargetHeldItem && (t == null || (t.HeldItem < 0 && t.HeldItem2 < 0))) return false;
            if (c.UserParalysisPending && (u == null || !u.ParalysisPending || !StatusRules.Has(u, StatusEffect.Paralysed))) return false;
            // 「used last turn」: 직전 배틀 턴에 둘째 기술을 썼으면 그 턴의 두 기술(LastMove2 — RuleOptions.SecondMoveLastTurn, rulings G21)
            bool usedLast = u != null && move != -1 && (u.LastMove == move || u.LastMove2 == move);
            if (c.SameMoveAsLast && !usedLast) return false;
            if (c.NotSameMoveAsLast && usedLast) return false;
            if (c.KnowsType != PokemonType.Typeless && (u == null || !KnowsOtherType(ctx, u, move, c.KnowsType))) return false;
            return true;
        }

        static bool HasType(Ctx ctx, PokemonState p, PokemonType type)
        {
            foreach (var t in ctx.Cards.Pokemon[p.Def].Types)
                if (t == type) return true;
            return false;
        }

        // ── 상시 효과(훑기, 01 §7-1) ──

        /// <summary>
        /// 기술 공격력 보정(BeforeRoll 절의 StrengthTo): 조건이 맞으면 「공격력 = n」을 Mod{출처 = 그 기술, 증감 = n − 기술 공격력}으로 맨 앞에 넣는다.
        /// 다른 보정(아이템·빙결 대상)은 그 뒤에 더해진다(G14).
        /// </summary>
        public static void StrengthMods(GameState s, Ctx ctx, int move, int attacker, int defender, List<Mod> into)
        {
            int at = 0;
            bool set = false; // 기술 효과의 「공격력 = n」이 들었다
            int baseStrength = BattleRules.MoveStrength(ctx, BattleRules.StrengthMove(s, move)); // mimic·metronome = 부른 기술의 공격력(G21)
            foreach (int id in ctx.Effects.OfMove(BattleRules.EffectMove(s, move)))
            {
                var e = ctx.Effects.Effects[id];
                if (e.Window != Window.BeforeRoll || !e.IsStatic) continue;
                // 「직전 턴과 같은 기술」은 묻는 기술(move)로 본다 — 굴릴 때는 b.Move와 같고, 화면이 고르기 전의 기술 줄을 셀 때는 아직 b.Move가 아니다
                if (e.When != null && !Holds(s, ctx, e.When, attacker >= 0 ? s.Pokemon[attacker] : null, defender >= 0 ? s.Pokemon[defender] : null,
                        s.Battle != null ? AttackDice.Count(s.Battle.Faces, AttackDieFace.StatusSymbol) : 0, move)) continue;
                if (e.Handler != null)
                {
                    // 공격력 셈 처리기(「?」·동적 공격력, rulings G21): 「= n」은 맨 앞(StrengthTo와 같다), 「+n」은 0이 아닐 때 그 뒤
                    var h = EffectHandlers.Static[e.Handler];
                    int v = h.Value(s, ctx, move, attacker, defender, null);
                    if (h.Sets)
                    {
                        into.Insert(at++, new Mod { Source = e.Source, Delta = v - baseStrength });
                        set = true;
                    }
                    else if (v != 0) into.Insert(at++, new Mod { Source = e.Source, Delta = v });
                    continue;
                }
                foreach (var o in e.Ops)
                    if (o.Op == EffectOp.StrengthTo)
                    {
                        into.Insert(at++, new Mod { Source = e.Source, Delta = o.Value - baseStrength });
                        set = true;
                    }
            }
            // 굴리기 전에 효과가 정한 공격력(belch 「consume 1 Apricorn to increase attack strength to 7」 · foul_play — BattleState.StrengthTo, 이번 공격만)
            var b = s.Battle;
            // 기술 효과의 「= n」이 들었으면 그 값이 공격력이다 — 둘 다 넣으면 기본값이 두 번 빠진다(ginkgo_merchant R이 바꾼 기본값 위에서도 「= n」, 감사 unclear 5 [가정])
            if (!set && b != null && b.Move == move && b.StrengthTo > 0 && b.ActiveSide >= 0 && b.Sides[b.ActiveSide].Active == attacker)
                into.Insert(at, new Mod { Source = new SourceRef(SourceKind.Move, move), Delta = b.StrengthTo - baseStrength });
        }

        /// <summary>
        /// 피해 보정(BeforeDamage 절의 FaceDamage·ExtraDamage·Double, 01 §6-4 ②·④). 효과 피해는 op마다 Mod{출처 = 그 기술, 증감}(0은 뺀다)로 넣고,
        /// 2배면 true. faces = 면 바꾸기가 끝난 마지막 굴림(G14).
        /// </summary>
        public static bool DamageMods(GameState s, Ctx ctx, int move, int attacker, int defender, IReadOnlyList<AttackDieFace> faces, List<Mod> into)
        {
            bool doubled = false;
            int symbols = AttackDice.Count(faces, AttackDieFace.StatusSymbol);   // 그 굴림의 심볼(실제 피해 = 마지막 굴림, 결정 72 미리 셈 = 그 순서의 굴림)
            foreach (int id in ctx.Effects.OfMove(BattleRules.EffectMove(s, move)))
            {
                var e = ctx.Effects.Effects[id];
                if (e.Window != Window.BeforeDamage || !e.IsStatic) continue;
                if (e.When != null && !Holds(s, ctx, e.When, attacker >= 0 ? s.Pokemon[attacker] : null, defender >= 0 ? s.Pokemon[defender] : null, symbols, move)) continue;
                if (e.Handler != null)
                {
                    // 효과 피해 셈 처리기(future_sight 「If incorrect, deal -3 damage」, rulings G21) — 그 굴림(faces)으로 센다(결정 72 미리 셈도 같다)
                    int v = EffectHandlers.Static[e.Handler].Value(s, ctx, move, attacker, defender, faces);
                    if (v != 0) into.Add(new Mod { Source = e.Source, Delta = v });
                    continue;
                }
                foreach (var o in e.Ops)
                {
                    int d;
                    switch (o.Op)
                    {
                        case EffectOp.FaceDamage: d = o.Value * AttackDice.Count(faces, o.Face); break;
                        case EffectOp.ExtraDamage: d = o.Value; break;
                        case EffectOp.FatigueDamage: d = attacker >= 0 ? s.Pokemon[attacker].Fatigue : 0; break; // calm_mind: 굴림으로 오른 피로까지(지금 값)
                        case EffectOp.Double: doubled = true; continue;
                        default: continue;
                    }
                    if (d != 0) into.Add(new Mod { Source = e.Source, Delta = d });
                }
            }
            ModRules.DamageMods(s, ModRules.Scanned(s, ctx, attacker), ModRules.Scanned(s, ctx, defender), faces, into); // 걸려 있는 지속 보정(M5, G16) — 체력 0인 포켓몬의 것은 훑지 않는다(rulings A24 ③)
            TrainerRules.DamageMods(s, ctx, move, attacker, faces, into); // 트레이너 카드(beauty — rulings A32)
            return doubled;
        }
    }
}
