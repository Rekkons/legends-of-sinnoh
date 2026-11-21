using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 트레이너 카드 — 배틀 굴림·공격력·타입 12(M6 묶음 3, rulings A34): chef R · essentia R · ginkgo_merchant R · hex_maniac B·R · ninja_boy B·R · poke_maniac B · psychic R ·
    /// swimmer R · triathlete R · veteran R. 물음은 효과 프레임 하나(<see cref="EffectDb.TrainerAbility"/>, 처리기 <see cref="AbilityHandler"/>)가 연다 —
    /// 프레임의 Locals[0] = 능력 번호, Owner = 그 좌석, Source = 그 카드. 「may」는 「안 함」이 첫 답(기본 답), 강제 고르기(ninja_boy R)는 첫 합법 답(rulings A33).
    /// </summary>
    public static partial class TrainerRules
    {
        /// <summary>처리기 이름(EffectHandlers.BuiltIn).</summary>
        public const string AbilityHandler = "trainer_ability";

        /// <summary>프레임 Locals[0]의 아래 네 비트 = 능력 번호.</summary>
        public const int AbToxinDart = 1, AbSwimmer = 2, AbTriathlete = 3, AbGinkgo = 4, AbChef = 5, AbVeteran = 6, AbHex = 8;   // 7 = 옛 psychic R 물음(결정 131로 반응 창으로 옮김)

        /// <summary>BattleState.CardAsk 비트: 이번 공격에서 이미 지난 물음 자리(굴리기 전 · 굴림). 4 = 옛 굴린 뒤 물음(psychic R — 결정 131로 반응 창 <see cref="UsedPsychic"/>).</summary>
        public const int AskedBeforeRoll = 1, AskedRolled = 2;

        const int HexAskBlue = 256, HexAskRed = 512;

        internal static void Register(Dictionary<string, EffectHandler> into) => into[AbilityHandler] = Ability;

        static void Push(GameState s, int seat, int ability, int user, int target)
        {
            var f = new EffectFrame { EffectId = EffectDb.TrainerAbility, Source = CardSource(s, seat), Owner = seat, User = user, Target = target };
            f.Locals[0] = ability;
            s.Stack.Add(f);
        }

        static bool OneOfEach(SeatState st)
        {
            foreach (int n in st.Apricorns)
                if (n < 1) return false;
            return true;
        }

        static bool AnyApricorn(SeatState st)
        {
            foreach (int n in st.Apricorns)
                if (n > 0) return true;
            return false;
        }

        /// <summary>triathlete R 「knows moves of 3 different types」: 그 포켓몬의 기술(시그니처 + 배운 기술)의 타입이 서로 다른 셋 이상이면 그 타입들(타입없음도 한 타입 — 원작 커뮤니티 확인). 아니면 빈 목록.</summary>
        public static List<int> TripleTypes(Ctx ctx, PokemonState p)
        {
            var types = new List<int>(3);
            var moves = new List<int>(4);
            moves.Add(ctx.Cards.Pokemon[p.Def].Signature);
            foreach (int m in p.Moves)
                if (m >= 0) moves.Add(m);
            foreach (int m in moves)
            {
                int t = (int)ctx.Cards.Moves[m].Type;
                if (!types.Contains(t)) types.Add(t);
            }
            if (types.Count < 3) types.Clear();
            return types;
        }

        // ── 물음 자리(BattleFlow.Run이 부른다 — 프레임을 쌓았으면 true: 진행 루프가 Stack부터 돈 뒤 같은 단계로 돌아온다) ──

        /// <summary>굴리기 전(기술을 고른 뒤): swimmer R 타입 바꾸기 · triathlete R 타입 바꾸기 · ginkgo_merchant R 기본 공격력 · chef R 마스터워크 스튜.</summary>
        internal static bool BeforeRoll(GameState s, Ctx ctx, BattleState b)
        {
            if ((b.CardAsk & AskedBeforeRoll) != 0) return false;
            b.CardAsk |= AskedBeforeRoll;
            int k = b.ActiveSide;
            int seat = b.Sides[k].Owner;
            if (CardOf(s, seat) < 0 || b.Move == BattleRules.PlanB) return false;
            int att = b.Sides[k].Active, def = b.Sides[1 - k].Active;
            var type = BattleRules.AttackType(s, ctx, b.Move);
            int ability = 0;
            if (Has(s, ctx, seat, "swimmer", Red) && type == PokemonType.Water) ability = AbSwimmer;
            else if (Has(s, ctx, seat, "triathlete", Red) && TripleTypes(ctx, s.Pokemon[att]).Count >= 3) ability = AbTriathlete;
            else if (Has(s, ctx, seat, "ginkgo_merchant", Red) && AnyApricorn(s.Seats[seat])) ability = AbGinkgo;
            else if (Has(s, ctx, seat, "chef", Red) && !b.Sides[k].Masterwork && OneOfEach(s.Seats[seat])) ability = AbChef;
            if (ability == 0) return false;
            Push(s, seat, ability, att, def);
            return true;
        }

        /// <summary>굴린 바로 뒤(면 바꾸기 전): veteran R 「add up to 2 status symbols to roll」.</summary>
        internal static bool Rolled(GameState s, Ctx ctx, BattleState b)
        {
            int k = b.ActiveSide;
            int seat = b.Sides[k].Owner;
            if (CardOf(s, seat) < 0 || !Has(s, ctx, seat, "veteran", Red)) return false;
            var type = BattleRules.AttackType(s, ctx, b.Move);
            if (type != PokemonType.Ice && type != PokemonType.Dragon) return false;
            Push(s, seat, AbVeteran, b.Sides[k].Active, b.Sides[1 - k].Active);
            return true;
        }

        // ── psychic R: 굴린 뒤 반응 창의 반응(사용자 결정 131) ──

        /// <summary>BattleState.CardAsk 비트: 이번 공격에 psychic R을 썼다(공격당 한 번 — 「change the result of 1 dice」).</summary>
        public const int UsedPsychic = 32;

        /// <summary>
        /// psychic R 「When you attack with a Psychic-type or Ghost-type move, you may change the result of 1 dice rolled to any other value」을 지금 굴린 뒤 창에서 그 좌석이 쓸 수 있나(사용자 결정 131 —
        /// 따로 묻던 자리(PostRoll)를 굴린 뒤 반응 창(Window.AfterRoll)으로 옮겼다: 디아루가와 같은 창 · 같은 바퀴, G6 순서 · 「attacker decides」 F11).
        /// 공격하는 좌석 · 공격 굴림(포획 굴림 아님) · 이번 공격에 아직 안 씀 · 면을 못 바꾸는 공격(sacred_sword — DiceLocked)이 아님 · 공격 타입(효과·능력이 바꾼 뒤) 에스퍼·고스트. 모두 공개 값이다.
        /// </summary>
        public static bool PsychicOpen(GameState s, Ctx ctx, int seat)
        {
            var b = s.Battle;
            if (b == null || b.ActiveSide < 0 || b.Step == BattleStep.Capture || b.Move == -1 || b.Faces.Count == 0) return false;
            if (b.Sides[b.ActiveSide].Owner != seat || (b.CardAsk & UsedPsychic) != 0) return false;
            if (CardOf(s, seat) < 0 || !Has(s, ctx, seat, "psychic", Red) || EffectRules.DiceLocked(s, ctx)) return false;
            var type = BattleRules.AttackType(s, ctx, b.Move);
            return type == PokemonType.Psychic || type == PokemonType.Ghost;
        }

        /// <summary>
        /// 굴린 뒤 반응 창(Reaction/Fate 선택 — 좌석의 반응을 한 선택에 모은다, G6 「바퀴마다 하나씩」)에 더하는 psychic R 답: UseAbility(Target = 바꿀 면, Arg = 새 면).
        /// 같은 면의 주사위는 어느 것이든 결과가 같아 첫 주사위를 바꾼다. 면 순서(심볼 · 일반 · 크리티컬) × 새 면 순서.
        /// </summary>
        public static void PsychicLegal(GameState s, Ctx ctx, int seat, Window w, List<ChoiceAnswer> into)
        {
            if (w != Window.AfterRoll || !PsychicOpen(s, ctx, seat)) return;
            var faces = s.Battle.Faces;
            for (int from = 0; from < 3; from++)
            {
                if (!faces.Contains((AttackDieFace)from)) continue;
                for (int to = 0; to < 3; to++)
                    if (to != from) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility, from, to));
            }
        }

        /// <summary>반응 창의 psychic R 답 적용(Reactions.Apply). 앞 반응 때문에 못 쓰게 됐으면(그 면이 없어짐 등) 효과 없이 false — 한도도 쓰지 않는다(G6·G3).</summary>
        internal static bool ApplyPsychic(GameState s, Ctx ctx, Emitter fx, int seat, ChoiceAnswer a)
        {
            if (a.Kind != AnswerKind.UseAbility || !PsychicOpen(s, ctx, seat)) return false;
            var b = s.Battle;
            int from = a.Target, to = a.Arg;
            int at = from >= 0 && from < 3 && to >= 0 && to < 3 && to != from ? b.Faces.IndexOf((AttackDieFace)from) : -1;
            if (at < 0) return false;
            b.CardAsk |= UsedPsychic;
            var before = AttackDice.Src(b.Faces);
            b.Faces[at] = (AttackDieFace)to;
            ChangeFaces(s, fx, b, CardSource(s, seat), new[] { new FaceChange { Index = at, Before = (AttackDieFace)from, After = (AttackDieFace)to } }, before);
            Used(s, fx, seat, Red, b.Sides[b.ActiveSide].Active);
            return true;
        }

        /// <summary>
        /// 봇의 psychic R 답(원칙 AI · 상대 모델 — [가정 plan §5 #665]): 상태 심볼 하나 → 크리티컬, 없으면 일반 타격 하나 → 크리티컬. 그런 답이 없으면(다 크리티컬) null = 안 쓴다.
        /// </summary>
        public static ChoiceAnswer PsychicBest(List<ChoiceAnswer> legal)
        {
            foreach (var from in new[] { AttackDieFace.StatusSymbol, AttackDieFace.RegularHit })
                foreach (var a in legal)
                    if (a.Kind == AnswerKind.UseAbility && a.Target == (int)from && a.Arg == (int)AttackDieFace.CriticalHit) return a;
            return null;
        }

        // ── 우선권 토큰: ninja_boy B·R ──

        /// <summary>
        /// ninja_boy B 「When you have Initiative Advantage, you may use it to go first regardless of actual initiative values」: 토큰을 가진 쪽이 그 좌석이면 우선도가 달라도 쓸지 묻는다 —
        /// 느린 쪽일 때(쓰면 선턴), 또는 빨강까지 풀렸을 때(빠른 쪽이어도 쓰면 Toxin Dart — 「regardless of actual initiative values」 [가정, rulings A34]).
        /// </summary>
        internal static bool AsksToken(GameState s, Ctx ctx, BattleState b, int holder, bool holderFaster)
        {
            int seat = b.Sides[holder].Owner;
            if (CardOf(s, seat) < 0 || !Has(s, ctx, seat, "ninja_boy", Blue)) return false;
            return !holderFaster || Has(s, ctx, seat, "ninja_boy", Red);
        }

        /// <summary>우선권 토큰을 썼다(BattleFlow.ApplyToken): ninja_boy R이면 상대 포켓몬에게 독·혼란·졸림 중 하나를 건다(강제 — 원작 커뮤니티 확인) — 고르기 프레임을 쌓는다.</summary>
        internal static void TokenUsed(GameState s, Ctx ctx, BattleState b, int k)
        {
            int seat = b.Sides[k].Owner;
            if (CardOf(s, seat) < 0 || !Has(s, ctx, seat, "ninja_boy", Red) || b.Sides[1 - k].Active < 0) return;
            Push(s, seat, AbToxinDart, b.Sides[k].Active, b.Sides[1 - k].Active);
        }

        // ── 상시(이 묶음): essentia R · poke_maniac B ──

        /// <summary>
        /// essentia R 「When you start a battle against a wild Pokémon, if you have not moved anywhere yet this turn, your first attack against them deals double damage」:
        /// 내가 건 배틀(쪽 0)의 상대가 야생 쪽(SeatId.Wild)이고 이번 턴에 아직 움직이지 않았으면(SeatState.Moved) 그 배틀의 내 첫 공격 피해가 2배.
        /// </summary>
        public static bool Opportune(GameState s, Ctx ctx, BattleState b)
        {
            if (b.ActiveSide != 0 || b.Sides[0].FirstAttackDone || b.Sides[1].Owner != SeatId.Wild) return false;
            int seat = b.Sides[0].Owner;
            return CardOf(s, seat) >= 0 && Has(s, ctx, seat, "essentia", Red) && !s.Seats[seat].Moved;
        }

        /// <summary>
        /// poke_maniac B 「When attacking, you may use the signature move of the opposing Pokémon against them」: 그 쪽의 배틀 행동에 더할 기술(상대 출전 포켓몬의 시그니처). 없으면 −1.
        /// 내 포켓몬이 이미 아는 기술이면 더하지 않는다(own = 지금 후보).
        /// </summary>
        public static int DisguiseMove(GameState s, Ctx ctx, BattleState b, int k, List<int> own)
        {
            int seat = b.Sides[k].Owner;
            int foe = b.Sides[1 - k].Active;
            if (foe < 0 || CardOf(s, seat) < 0 || !Has(s, ctx, seat, "poke_maniac", Blue)) return -1;
            int move = ctx.Cards.Pokemon[s.Pokemon[foe].Def].Signature;
            return own.Contains(move) ? -1 : move;
        }

        /// <summary>k 쪽이 고른 기술이 포켓몬 분장(poke_maniac B)으로 쓰는 상대의 시그니처인가 — 맞으면 AbilityUsed.</summary>
        internal static void MoveChosen(GameState s, Ctx ctx, Emitter fx, BattleState b, int k, int move)
        {
            if (move < 0) return;
            var own = new List<int>(4);
            PokemonRules.MoveOptions(ctx, s.Pokemon[b.Sides[k].Active], own);
            if (DisguiseMove(s, ctx, b, k, own) == move) Used(s, fx, b.Sides[k].Owner, Blue, b.Sides[1 - k].Active);
        }

        // ── hex_maniac B·R: 상태를 거는 op 앞에서 묻는다 ──

        /// <summary>
        /// 효과 프레임 f가 상태를 거는 op(Inflict · InflictBoth · InflictRolled)를 막 실행하려 한다(EffectRules.Step). 그 효과의 주인이 hex_maniac이면 물음 프레임을 위에 쌓고 true —
        /// 물음이 끝나면 같은 op가 다시 돈다(BattleState.HexAsked). 파랑 「you may instead roll the status dice」는 상태 하나를 거는 op(Inflict)에만,
        /// 빨강 「additional status effects」는 대상에 이미 상태가 있을 때만 묻는다.
        /// </summary>
        internal static bool Hexing(GameState s, Ctx ctx, EffectOpDef o, EffectFrame f, List<int> targets)
        {
            var b = s.Battle;
            if (b == null || b.HexAsked || f.EffectId == EffectDb.TrainerAbility) return false;
            if (o.Op != EffectOp.Inflict && o.Op != EffectOp.InflictBoth && o.Op != EffectOp.InflictRolled) return false;
            int seat = f.Owner;
            if (CardOf(s, seat) < 0 || seat >= s.SeatCount) return false;
            bool blue = o.Op == EffectOp.Inflict && Has(s, ctx, seat, "hex_maniac", Blue);
            bool red = false;
            if (Has(s, ctx, seat, "hex_maniac", Red))
                foreach (int p in targets)
                    if (s.Pokemon[p].Status != StatusEffect.None) red = true;
            if (!blue && !red) return false;
            b.HexAsked = true;
            Push(s, seat, AbHex | ((int)o.Status << 4) | (blue ? HexAskBlue : 0) | (red ? HexAskRed : 0), f.User, targets.Count > 0 ? targets[0] : f.Target);
            return true;
        }

        /// <summary>상태를 거는 op 하나가 끝났다 — 그 op에 물어 둔 답을 지운다.</summary>
        internal static void HexDone(BattleState b)
        {
            if (b == null) return;
            b.HexAsked = false;
            b.HexAs = -1;
            b.HexExtra = false;
        }

        // ── 처리기 ──

        static EffectStepResult Option(GameState s, Ctx ctx, Emitter fx, EffectFrame f, int responder, int slot, out bool accepted)
        {
            accepted = false;
            if (EffectRules.HasAnswer(f))
            {
                accepted = (AnswerKind)f.Locals[EffectFrame.LocalAnswerKind] == AnswerKind.Accept;
                return EffectStepResult.Continue;
            }
            if (responder < 0 || responder >= s.SeatCount) return EffectStepResult.Continue;
            // Count = 능력 칸(1 파랑 · 2 빨강) — 화면이 어느 능력의 물음인지 가른다
            return EffectRules.Wait(s, ctx, fx, f, new PendingChoice { Kind = ChoiceKind.Reaction, Sub = ChoiceSub.EffectOption, Seat = responder, Source = f.Source, Subject = f.User, Count = slot });
        }

        static void ChangeFaces(GameState s, Emitter fx, BattleState b, SourceRef source, FaceChange[] changes, int[] before)
        {
            if (fx.On) fx.Add(new DiceFacesChanged { Source = source, Changes = changes, SrcBefore = before, SrcAfter = AttackDice.Src(b.Faces) });
        }

        /// <summary>ninja_boy R의 세 상태 — 대상에게 걸리는 것이 앞에 온다(첫 답 = 기본 답).</summary>
        static List<int> ToxinOptions(Ctx ctx, PokemonState target)
        {
            var all = new[] { StatusEffect.Poisoned, StatusEffect.Confused, StatusEffect.Drowsy };
            var options = new List<int>(3);
            foreach (var st in all)
                if (StatusRules.Check(ctx, target, st) == StatusResult.Applied) options.Add((int)st);
            foreach (var st in all)
                if (!options.Contains((int)st)) options.Add((int)st);
            return options;
        }

        static EffectStepResult Ability(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            var b = s.Battle;
            int seat = f.Owner;
            int responder = EffectRules.Responder(s, seat);
            int code = f.Locals[0];
            switch (code & 15)
            {
                case AbToxinDart:
                {
                    if (f.Target < 0) return EffectStepResult.Done;
                    var options = ToxinOptions(ctx, s.Pokemon[f.Target]);
                    var r = EffectRules.Pick(s, ctx, fx, f, responder, options, false, f.Source, options[0], out int status);
                    if (r != EffectStepResult.Continue) return r;
                    if (!options.Contains(status)) status = options[0];
                    Used(s, fx, seat, Red, f.Target);
                    EffectRules.InflictOne(s, ctx, fx, f.Target, (StatusEffect)status, f.Source);
                    return EffectStepResult.Done;
                }
                case AbSwimmer:
                case AbTriathlete:
                {
                    // 「you may change its type to either Bug-type or Flying-type」 · 「you may change the type of your attack to any of those 3 types」
                    int now = (int)BattleRules.AttackType(s, ctx, b.Move);
                    var options = (code & 15) == AbSwimmer ? new List<int> { (int)PokemonType.Bug, (int)PokemonType.Flying } : TripleTypes(ctx, s.Pokemon[f.User]);
                    options.Remove(now);
                    if (options.Count == 0) return EffectStepResult.Done;
                    var r = EffectRules.Pick(s, ctx, fx, f, responder, options, true, f.Source, -1, out int type);
                    if (r != EffectStepResult.Continue) return r;
                    if (!options.Contains(type)) return EffectStepResult.Done;
                    b.TypeTo = type;
                    Used(s, fx, seat, Red, f.User);
                    return EffectStepResult.Done;
                }
                case AbGinkgo:
                {
                    // 「you may discard 1 Apricorn to temporarily change the base attack strength of a move this turn to 4/5/6 based on the tier of the Apricorn used」
                    var options = new List<int>(SeatState.ApricornColors);
                    for (int c = 0; c < SeatState.ApricornColors; c++)
                        if (s.Seats[seat].Apricorns[c] > 0) options.Add(c);
                    if (options.Count == 0) return EffectStepResult.Done;
                    var r = EffectRules.Pick(s, ctx, fx, f, responder, options, true, f.Source, -1, out int color);
                    if (r != EffectStepResult.Continue) return r;
                    if (!options.Contains(color)) return EffectStepResult.Done;
                    EffectRules.SpendApricorn(s, fx, seat, color, f.Source);
                    b.StrengthTo = GinkgoBase + MapActions.ApricornGrade((ApricornColor)color);
                    Used(s, fx, seat, Red, f.User);
                    return EffectStepResult.Done;
                }
                case AbChef:
                {
                    // 「Costs 1 of every Apricorn: Your Pokémon gain +1 attack strength to their moves that are the same type as them. Lasts until the end of battle.」
                    int k = b.Sides[0].Owner == seat ? 0 : 1;
                    if (b.Sides[k].Masterwork || !OneOfEach(s.Seats[seat])) return EffectStepResult.Done;
                    var r = Option(s, ctx, fx, f, responder, SlotOf(Red), out bool yes);
                    if (r != EffectStepResult.Continue) return r;
                    if (!yes) return EffectStepResult.Done;
                    for (int c = 0; c < SeatState.ApricornColors; c++) EffectRules.SpendApricorn(s, fx, seat, c, f.Source);
                    b.Sides[k].Masterwork = true;
                    Used(s, fx, seat, Red);
                    return EffectStepResult.Done;
                }
                case AbVeteran:
                {
                    // 「you may add up to 2 status symbols to roll」
                    var r = EffectRules.Pick(s, ctx, fx, f, responder, new List<int> { 1, 2 }, true, f.Source, -1, out int n);
                    if (r != EffectStepResult.Continue) return r;
                    if (n != 1 && n != 2) return EffectStepResult.Done;
                    var before = AttackDice.Src(b.Faces);
                    var changes = new FaceChange[n];
                    for (int i = 0; i < n; i++)
                    {
                        changes[i] = new FaceChange { Index = b.Faces.Count, Before = AttackDieFace.None, After = AttackDieFace.StatusSymbol };
                        b.Faces.Add(AttackDieFace.StatusSymbol);
                    }
                    ChangeFaces(s, fx, b, f.Source, changes, before);
                    Used(s, fx, seat, Red, f.User);
                    return EffectStepResult.Done;
                }
                case AbHex:
                {
                    if (f.StepIndex == 0)
                    {
                        if ((code & HexAskBlue) != 0)
                        {
                            // 「When inflicting any status effects, you may instead roll the status dice to inflict the new status effect it lands on.」
                            var r = Option(s, ctx, fx, f, responder, SlotOf(Blue), out bool roll);
                            if (r != EffectStepResult.Continue) return r;
                            if (roll)
                            {
                                b.HexAs = (int)StatusRules.RollDie(s.Rng);
                                Used(s, fx, seat, Blue, f.Target);
                            }
                        }
                        if ((code & HexAskRed) == 0) return EffectStepResult.Done;
                        f.StepIndex = 1;
                        return EffectStepResult.Continue;
                    }
                    // 「You may inflict additional status effects onto a target so long as the target does not have that status effect already.」
                    var r2 = Option(s, ctx, fx, f, responder, SlotOf(Red), out bool extra);
                    if (r2 != EffectStepResult.Continue) return r2;
                    if (extra)
                    {
                        b.HexExtra = true;
                        Used(s, fx, seat, Red, f.Target);
                    }
                    return EffectStepResult.Done;
                }
                default:
                    return AbilityAfter(s, ctx, fx, f, seat, responder, code); // 묶음 4의 능력(TrainerRules.After.cs)
            }
        }

        /// <summary>ginkgo_merchant R: 기본 공격력 = 3 + 규토리 단계(1·2·3 → 4·5·6).</summary>
        public const int GinkgoBase = 3;
    }
}
