using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 열린 반응 창 하나(01 §7-3, rulings G6). GameState.Windows의 끝이 지금 창이다. 바퀴마다 쓸 수 있는 좌석에 묻고(한꺼번에 / 차례로),
    /// 누가 쓰면 한 바퀴 더, 아무도 안 쓰면 닫고 원래 행동을 잇는다(새 Seq, Continues = OpenerSeq).
    /// </summary>
    public sealed class ReactionWindow : IStatePart<ReactionWindow>
    {
        public Window Window;
        /// <summary>창을 연 행동(반응 답의 ReactsTo, 닫힌 뒤 잇는 Seq의 Continues).</summary>
        public int OpenerSeq = -1;
        public int OpenerSeat = SeatId.System;
        public ActionKind OpenerKind;
        public AutoKind OpenerAuto;
        /// <summary>바퀴 번호(1부터).</summary>
        public int Round;
        /// <summary>RuleOptions.ReactionAsk = Sequential(G6 다른 해석).</summary>
        public bool Sequential;
        /// <summary>이번 바퀴 묶음의 GroupId(차례로면 지금 열린 선택의 GroupId). 없으면 −1.</summary>
        public int Group = -1;
        /// <summary>이번 바퀴에 물을 좌석(가상 응답자 포함), G6 적용 순서.</summary>
        public List<int> Order = new List<int>();
        /// <summary>차례로: 다음에 물을 Order 자리.</summary>
        public int Cursor;
        /// <summary>이번 바퀴에 누가 반응을 썼다.</summary>
        public bool AnyUsed;
        /// <summary>배틀 단계(BattleFlow)가 연 창이다. 닫히면 이어진 Seq에서 배틀 진행을 다시 돈다(장면 시험이 직접 연 창은 잇지 않는다).</summary>
        public bool ResumeBattle;

        public void CopyFrom(ReactionWindow o)
        {
            Window = o.Window;
            OpenerSeq = o.OpenerSeq;
            OpenerSeat = o.OpenerSeat;
            OpenerKind = o.OpenerKind;
            OpenerAuto = o.OpenerAuto;
            Round = o.Round;
            Sequential = o.Sequential;
            Group = o.Group;
            StateParts.CopyInts(Order, o.Order);
            Cursor = o.Cursor;
            AnyUsed = o.AnyUsed;
            ResumeBattle = o.ResumeBattle;
        }

        public void Walk(IStateWriter w)
        {
            w.Int((int)Window);
            w.Int(OpenerSeq);
            w.Int(OpenerSeat);
            w.Int((int)OpenerKind);
            w.Int((int)OpenerAuto);
            w.Int(Round);
            StateParts.Bool(w, Sequential);
            w.Int(Group);
            StateParts.Ints(w, Order);
            w.Int(Cursor);
            StateParts.Bool(w, AnyUsed);
            StateParts.Bool(w, ResumeBattle);
        }

        public void Read(IStateReader r)
        {
            Window = (Window)r.Int();
            OpenerSeq = r.Int();
            OpenerSeat = r.Int();
            OpenerKind = (ActionKind)r.Int();
            OpenerAuto = (AutoKind)r.Int();
            Round = r.Int();
            Sequential = StateParts.ReadBool(r);
            Group = r.Int();
            StateParts.ReadInts(r, Order);
            Cursor = r.Int();
            AnyUsed = StateParts.ReadBool(r);
            ResumeBattle = StateParts.ReadBool(r);
        }
    }

    /// <summary>창에서 반응할 수 있는 좌석 하나(공개 정보로 정함, 01 §3-3).</summary>
    public struct ReactionCandidate
    {
        public int Seat;
        public ChoiceSub Sub;
        public bool HandGated;
        public SourceRef Source;
        public int Subject;
        /// <summary>고를 값(Reaction/EffectPick 갈래 — 있으면 「안 함」이 첫 답). 없으면 null.</summary>
        public List<int> Options;
    }

    /// <summary>
    /// 반응 창 진행(01 §3-3·§7-3, rulings G6). 배틀 단계(BattleFlow, M3-e)가 창 자리에서 <see cref="Open"/>을 부르고, false면 바로 잇는다.
    /// 반응 갈래: 은하의 응징(G5, 쓰면 피해 버퍼로) · 운명의 변화(HandGated, 카드 효과는 M5 — 지금 Legal은 「안 씀」뿐).
    /// 우선권 토큰은 창이 아니라 순서 단계가 직접 여는 선택이다(01 §6-2).
    /// </summary>
    public static class Reactions
    {
        /// <summary>은하의 응징을 묻는 창(G5): 기본 TurnEnd면 BattleTurnEnd(공격한 쪽 턴 끝, 원작 커뮤니티 확인), BeforeDamage면 BeforeDamage, AfterFaint면 AfterDamage(배틀 단계가 기절 확인 뒤에 연다).</summary>
        public static Window RetributionWindow(RuleOptions o)
        {
            switch (o.GalacticRetributionTiming)
            {
                case GalacticRetributionTiming.BeforeDamage: return Window.BeforeDamage;
                case GalacticRetributionTiming.AfterFaint: return Window.AfterDamage;
                default: return Window.BattleTurnEnd;
            }
        }

        public static bool IsBattleWindow(Window w) => w >= Window.BattleStart;

        /// <summary>
        /// 그 창에서 반응할 수 있는 좌석, G6 적용 순서: 배틀 창은 ① 배틀 턴을 가진 쪽(없으면 도전자 Side 0) → ② 다른 쪽 → ③ 배틀 밖 좌석
        /// (맵 차례 좌석부터 시계 방향), 맵 창은 맵 차례 좌석부터 시계 방향. 가상 응답자는 자기 배틀 쪽 자리에 선다. 좌석마다 하나.
        /// 손패 내용은 보지 않는다(운명은 장수로, 01 §3-3 HandGated).
        /// </summary>
        public static void Candidates(GameState s, Ctx ctx, Window w, List<ReactionCandidate> into)
        {
            into.Clear();
            var b = s.Battle;
            if (b != null && IsBattleWindow(w))
            {
                int first = b.ActiveSide >= 0 ? b.ActiveSide : 0;
                SideCandidates(s, ctx, w, first, into);
                SideCandidates(s, ctx, w, 1 - first, into);
                // ③ 배틀 밖 좌석의 반응(인터뷰어 파랑 등)은 트레이너 카드 작업(M6)이 더한다.
            }
            // 맵 창의 반응(사당·스탬프 끼어들기 등)은 M5·M6이 더한다.
        }

        static void SideCandidates(GameState s, Ctx ctx, Window w, int k, List<ReactionCandidate> into)
        {
            var b = s.Battle;
            var side = b.Sides[k];
            int responder = side.Responder != SeatId.System ? side.Responder : side.Owner;
            // 은하의 응징(G5): 공격받는 갤럭시단 쪽, 저장 ≥ 1, 공격한 포켓몬이 아직 배틀에 있음
            if (w == RetributionWindow(s.Options) && RetributionOpen(s, ctx, k, out int attacker))
            {
                into.Add(new ReactionCandidate { Seat = responder, Sub = ChoiceSub.Retribution, Source = SourceRef.Rule(RuleSource.GalacticRetribution), Subject = attacker });
                return;
            }
            if (w != Window.BeforeRoll && w != Window.AfterRoll || side.Owner < 0 || side.Owner >= s.SeatCount) return;
            // 운명의 변화(A11, 01 §3-3): 배틀 당사자 좌석이 운명 장수 ≥ 1이고 이번 배틀 차례에 아직 안 썼으면. 기라티나 = 굴리기 전, 디아루가 = 굴린 뒤.
            bool fate = s.Seats[side.Owner].FateHand.Count >= 1 && FateRules.CanUse(s, side.Owner)
                && (s.Options.FateOnOpponentBattleTurn || k == b.ActiveSide);
            // psychic R(사용자 결정 131): 굴린 뒤 창의 공격하는 좌석 반응 — 운명과 같은 선택에 답으로 들어간다(좌석마다 선택 하나, G6 「바퀴마다 하나씩」 · 공격당 한 번)
            bool psychic = w == Window.AfterRoll && k == b.ActiveSide && TrainerRules.PsychicOpen(s, ctx, side.Owner);
            // guitarist B·R 앰프(사용자 결정 134): psychic R과 같은 꼴의 공격하는 좌석 반응(카드는 좌석마다 하나라 psychic R과 함께 서지 않는다)
            psychic |= w == Window.AfterRoll && k == b.ActiveSide && TrainerRules.AmpOpen(s, ctx, side.Owner);
            if (fate || psychic)
            {
                // 그 좌석에 다른 공개 반응이 없으면 HandGated — psychic R · 앰프는 공개 반응이라 숨은 손패 창이 아니다(묻지 않기 · 최소 시간 없음). lady R의 완전 회복은 반응 창에서 묻지 않는다 — 내 배틀 행동에서만(사용자 결정 84 T6 다, rulings A35)
                into.Add(new ReactionCandidate
                {
                    Seat = side.Owner, Sub = ChoiceSub.Fate, HandGated = !psychic, Source = fate ? new SourceRef(SourceKind.Fate, -1) : TrainerRules.CardSource(s, side.Owner), Subject = -1,
                });
            }
        }

        static bool RetributionOpen(GameState s, Ctx ctx, int k, out int attacker)
        {
            attacker = -1;
            var b = s.Battle;
            if (b.Kind != BattleKind.Galactic || b.ActiveSide < 0 || k == b.ActiveSide) return false;
            var side = b.Sides[k];
            if (side.Owner != SeatId.Galactic || side.Retribution < 1) return false;
            attacker = b.Sides[b.ActiveSide].Active;
            return attacker >= 0 && !PokemonRules.IsFainted(ctx, s.Pokemon[attacker]);
        }

        /// <summary>
        /// 창을 연다. 쓸 수 있는 좌석이 없으면 WindowSkipped를 내고 false — 부르는 쪽이 바로 잇는다. 있으면 선택을 열고 true — 부르는 쪽은 멈춘다.
        /// 창이 닫히면 원래 행동이 새 Seq(Continues = 지금 Seq)로 이어진다.
        /// </summary>
        public static bool Open(GameState s, Ctx ctx, Emitter fx, Window w, bool resumeBattle = false)
        {
            if (fx.Seq < 0) throw new InvalidOperationException("반응 창은 행동 안에서 연다");
            var cands = new List<ReactionCandidate>();
            Candidates(s, ctx, w, cands);
            if (cands.Count == 0)
            {
                if (fx.On) fx.Add(new WindowSkipped { Window = w });
                return false;
            }
            var win = new ReactionWindow
            {
                Window = w, OpenerSeq = fx.Seq, OpenerSeat = fx.Seat, OpenerKind = fx.Kind, OpenerAuto = fx.Auto,
                Round = 1, Sequential = s.Options.ReactionAsk == ReactionAsk.Sequential, ResumeBattle = resumeBattle,
            };
            s.Windows.Add(win);
            AskRound(s, ctx, fx, win, cands);
            return true;
        }

        static void AskRound(GameState s, Ctx ctx, Emitter fx, ReactionWindow win, List<ReactionCandidate> cands)
        {
            win.Order.Clear();
            foreach (var c in cands) win.Order.Add(c.Seat);
            win.AnyUsed = false;
            if (win.Sequential || cands.Count == 1)
            {
                win.Cursor = 1;
                win.Group = ChoiceRules.Open(s, ctx, fx, Choice(win, cands[0], false), win.OpenerSeq).GroupId;
                return;
            }
            // 한꺼번에(G6): 한 묶음, 비공개(다 답한 뒤 적용 순서대로 공개)
            var group = new List<PendingChoice>(cands.Count);
            foreach (var c in cands) group.Add(Choice(win, c, true));
            ChoiceRules.OpenGroup(s, ctx, fx, group, win.OpenerSeq);
            win.Group = group[0].GroupId;
        }

        static PendingChoice Choice(ReactionWindow win, ReactionCandidate c, bool secret)
        {
            var p = new PendingChoice
            {
                Kind = ChoiceKind.Reaction, Seat = c.Seat, Sub = c.Sub, Window = win.Window, HandGated = c.HandGated, Source = c.Source,
                Subject = c.Subject, Secret = secret,
            };
            if (c.Options != null)
            {
                p.Count = 0; // 「안 함」이 첫 답
                p.Options.AddRange(c.Options);
            }
            return p;
        }

        /// <summary>묶음 Id가 지금 창의 바퀴면 그 창.</summary>
        public static ReactionWindow OfGroup(GameState s, int group)
        {
            if (s.Windows.Count == 0) return null;
            var w = s.Windows[s.Windows.Count - 1];
            return w.Group == group ? w : null;
        }

        /// <summary>한꺼번에 물은 바퀴를 모두 답함: 적용 순서대로 공개하고, 쓴 반응은 저마다 새 Seq(ReactsTo = 창을 연 행동)로 적용한다.</summary>
        internal static void ResolveGroup(GameState s, Ctx ctx, Emitter fx, ReactionWindow win, List<PendingChoice> members)
        {
            var ordered = new List<PendingChoice>(members.Count);
            foreach (int seat in win.Order)
                foreach (var m in members)
                    if (m.Seat == seat && !ordered.Contains(m)) ordered.Add(m);
            if (fx.On)
            {
                var answers = new SeatAnswer[ordered.Count];
                for (int i = 0; i < ordered.Count; i++) answers[i] = new SeatAnswer { Seat = ordered[i].Seat, Value = ordered[i].Answer.Clone() };
                fx.Add(new SecretRevealed { Group = win.Group, Answers = answers, Window = win.Window });
            }
            foreach (var m in ordered)
            {
                if (m.Answer.Kind == AnswerKind.Decline) continue;
                fx.Begin(m.Seat, ActionKind.Reaction, m.AnswerAuto, -1, win.OpenerSeq);
                if (Apply(s, ctx, fx, m.Seat, m.Sub, m.Answer)) win.AnyUsed = true;
            }
            EndRound(s, ctx, fx, win);
        }

        /// <summary>하나만 물은 바퀴(좌석 하나 또는 차례로)의 답. 지금 Seq가 그 답의 행동이다.</summary>
        internal static void Answered(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            var win = OfGroup(s, c.GroupId) ?? throw new InvalidOperationException($"선택 {c.Id}: 열린 반응 창이 없다");
            if (a.Kind != AnswerKind.Decline && Apply(s, ctx, fx, c.Seat, c.Sub, a)) win.AnyUsed = true;
            if (win.Sequential)
            {
                // 다음 좌석: 이번 바퀴 순서에서 아직 쓸 수 있는 좌석(앞 결과로 조건을 다시 본다)
                var cands = new List<ReactionCandidate>();
                Candidates(s, ctx, win.Window, cands);
                while (win.Cursor < win.Order.Count)
                {
                    int seat = win.Order[win.Cursor++];
                    foreach (var cand in cands)
                        if (cand.Seat == seat)
                        {
                            win.Group = ChoiceRules.Open(s, ctx, fx, Choice(win, cand, false), win.OpenerSeq).GroupId;
                            return;
                        }
                }
            }
            EndRound(s, ctx, fx, win);
        }

        static void EndRound(GameState s, Ctx ctx, Emitter fx, ReactionWindow win)
        {
            if (win.AnyUsed)
            {
                // 누가 썼으면 창을 다시 계산해 한 바퀴 더(앞 바퀴의 「안 씀」 좌석도 다시 묻는다, G6)
                var cands = new List<ReactionCandidate>();
                Candidates(s, ctx, win.Window, cands);
                if (cands.Count > 0)
                {
                    win.Round++;
                    AskRound(s, ctx, fx, win, cands);
                    return;
                }
            }
            s.Windows.Remove(win);
            // (c) 끼어든 반응이 끝나 원래 행동이 이어진다(01 §3-6 ①). 이어지는 배틀 단계가 이 Seq에 이벤트를 낸다.
            fx.Begin(win.OpenerSeat, win.OpenerKind, win.OpenerAuto, win.OpenerSeq);
            if (win.ResumeBattle) BattleFlow.Run(s, ctx, fx);
        }

        /// <summary>반응 하나 적용. 앞 반응 때문에 조건이 깨졌으면 효과 없이 0 결과 이벤트만 내고 자원도 쓰지 않는다(G6·G3). 썼으면 true.</summary>
        static bool Apply(GameState s, Ctx ctx, Emitter fx, int seat, ChoiceSub sub, ChoiceAnswer a)
        {
            switch (sub)
            {
                case ChoiceSub.Retribution:
                {
                    var b = s.Battle;
                    int k = ChoiceRules.BattleSideOf(s, seat);
                    int attacker = -1;
                    bool ok = k >= 0 && RetributionOpen(s, ctx, k, out attacker);
                    if (!ok) attacker = b.ActiveSide >= 0 ? b.Sides[b.ActiveSide].Active : -1;
                    int amount = ok ? b.Sides[k].Retribution : 0;
                    if (ok)
                    {
                        // G5: 고정 피해. 그 턴의 피해 버퍼에 넣어 공격 피해와 동시에 적용한다(적용은 배틀 단계, 기절 알림 ById = 갤럭시단 출전 포켓몬).
                        b.Sides[k].Retribution = 0;
                        BattleFlow.Buffer(b, attacker, -amount, SourceRef.Rule(RuleSource.GalacticRetribution));
                    }
                    if (fx.On) fx.Add(new RetributionMoved { Source = SourceRef.Rule(RuleSource.GalacticRetribution), FromSide = k, ToPokemonId = attacker, Amount = amount });
                    return ok;
                }
                case ChoiceSub.Fate:
                    if (a.Kind == AnswerKind.UseAbility && a.Target == TrainerRules.AmpTarget) return TrainerRules.ApplyAmp(s, ctx, fx, seat, a); // 앰프(결정 134 — 앞 반응으로 못 쓰게 됐으면 효과 없이)
                    if (a.Kind == AnswerKind.UseAbility) return TrainerRules.ApplyPsychic(s, ctx, fx, seat, a); // psychic R(결정 131 — 앞 반응으로 못 쓰게 됐으면 효과 없이)
                    return FateRules.ApplyReaction(s, ctx, fx, seat, a); // 운명 효과 하나(A11 — 못 내게 됐으면 효과 없이)
                default:
                    throw new InvalidOperationException($"반응 {sub}({a})의 적용이 아직 없다 — Legal이 내주지 않아야 한다");
            }
        }
    }
}
