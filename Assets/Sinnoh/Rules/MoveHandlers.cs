using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 기술 효과 처리기(EffectHandlers.BuiltIn의 이름들, M5 「기술 효과 — 나머지 op 갈래」, rulings G20). op로 쓰면 칸이 늘기만 하는 고르기 넷이다:
    /// 공격 타입 고르기(BeforeRoll — <c>type_learnable</c> hidden_power·hidden_punch·multi_attack / <c>type_any</c> judgement) ·
    /// 규토리를 쓰고 공격력 7(BeforeRoll — <c>belch</c>) · 고른 상태 하나 또는 둘(BeforeDamage — <c>photon_geyser</c>).
    /// 굴리기 전(BeforeRoll) 절은 BattleFlow.Act가 기술을 고른 바로 뒤에 쌓는다 — 굴린 뒤의 반응·피해보다 먼저 정해진다.
    /// 핸들러 갈래(rulings G21): 프레임 처리기 여덟(foul_play · mimic · metronome · kinesis · future_sight · drain_half · second_move · encore)과 셈 처리기(<see cref="Statics"/> — 공격력 「?」·동적 공격력 · future_sight의 빗나감).
    /// 고르기는 <see cref="EffectRules.Pick"/>(Reaction/EffectPick)로 연다. 가상 쪽(야생·갤럭시단)은 묻지 않는다.
    /// </summary>
    public static class MoveHandlers
    {
        /// <summary>belch 「You may consume 1 Apricorn to increase attack strength to 7」의 공격력.</summary>
        public const int BelchStrength = 7;

        internal static void Register(Dictionary<string, EffectHandler> into)
        {
            into.Add("type_learnable", (s, ctx, fx, f) => ChooseType(s, ctx, fx, f, false));
            into.Add("type_any", (s, ctx, fx, f) => ChooseType(s, ctx, fx, f, true));
            into.Add("belch", Belch);
            into.Add("photon_geyser", PhotonGeyser);
            // M5 「전설·알파·폼」(rulings A26): 폼을 바꾸는 기술 셋
            into.Add("hangry_wheel", HangryWheel);
            into.Add("adaptive_cloak", AdaptiveCloak);
            into.Add("zen_restoration", ZenRestoration);
            // 핸들러 갈래(rulings G21): 굴리기 전 고르기(복사·섞기 · 맞히기) · 준 피해의 절반 회복 · 둘째 기술 · 마지막 기술
            into.Add("foul_play", FoulPlay);
            into.Add("mimic", Mimic);
            into.Add("metronome", Metronome);
            into.Add("kinesis", Kinesis);
            into.Add("future_sight", FutureSight);
            into.Add("drain_half", DrainHalf);
            into.Add("second_move", SecondMove);
            into.Add("encore", Encore);
        }

        /// <summary>셈 처리기(EffectHandlers.Static, rulings G21): 공격력 「?」·동적 공격력(BeforeRoll) · future_sight의 빗나감(BeforeDamage ②).</summary>
        internal static Dictionary<string, StaticHandler> Statics()
        {
            var d = new Dictionary<string, StaticHandler>(StringComparer.Ordinal);
            void Power(string name, bool sets, Func<GameState, Ctx, int, int, int, int> v) =>
                d.Add(name, new StaticHandler(Window.BeforeRoll, sets, (s, ctx, move, a, t, faces) => v(s, ctx, move, a, t)));
            Power("party_song", false, (s, ctx, move, a, t) => PartyKnowing(s, ctx, a, m => ctx.Cards.Moves[m].Key.EndsWith(SongSuffix, StringComparison.Ordinal)));
            Power("party_surf", false, (s, ctx, move, a, t) => PartyKnowing(s, ctx, a, m => ctx.Cards.Moves[m].Key == "surf"));
            Power("revenge", false, (s, ctx, move, a, t) => FaintedInParty(s, ctx, a));
            Power("taken_last", false, (s, ctx, move, a, t) => TakenLast(s, a));
            Power("flail", false, (s, ctx, move, a, t) => a < 0 ? 0 : s.Pokemon[a].Damage / 2);
            Power("rollout", false, (s, ctx, move, a, t) => UsedThisBattle(s, a, move));
            Power("eruption", true, (s, ctx, move, a, t) => a < 0 ? 0 : Math.Min(EruptionMax, PokemonRules.Hp(ctx, s.Pokemon[a])));
            Power("electro_ball", true, (s, ctx, move, a, t) => a < 0 ? 0 : Math.Min(ElectroBallMax, Initiative(s, ctx, a) / 2));
            Power("seismic_toss", true, (s, ctx, move, a, t) => t < 0 ? 0 : Math.Min(SeismicTossMax, PokemonRules.MaxHp(ctx, s.Pokemon[t]) / 2));
            Power("heavy_slam", true, (s, ctx, move, a, t) => a < 0 ? 0 : Math.Min(HeavySlamMax, PokemonRules.MaxHp(ctx, s.Pokemon[a]) / 2));
            d.Add("future_sight_miss", new StaticHandler(Window.BeforeDamage, false, (s, ctx, move, a, t, faces) =>
                s.Battle == null || s.Battle.Guess < 0 || faces == null || GuessOf(faces) == s.Battle.Guess ? 0 : FutureSightMiss));
            return d;
        }

        /// <summary>공격력 상한: eruption 7 · electro_ball 5 · seismic_toss 7 · heavy_slam 5(「Maximum n attack strength」 — 효과가 정한 값의 상한, 지닌물건 등은 그 뒤에 더한다 [가정 G21]).</summary>
        public const int EruptionMax = 7, ElectroBallMax = 5, SeismicTossMax = 7, HeavySlamMax = 5;
        /// <summary>foul_play 「Minimum 1 attack strength」.</summary>
        public const int FoulPlayMin = 1;
        /// <summary>future_sight 「If incorrect, deal -3 damage」.</summary>
        public const int FutureSightMiss = -3;
        /// <summary>「a Song move」 = 기술 키가 _song으로 끝나는 기술(thunder_song·fire_song·frost_song·perish_song — 원작 커뮤니티 확인).</summary>
        public const string SongSuffix = "_song";
        /// <summary>future_sight의 답: 크리티컬이 심볼보다 많다 · 적다 · 같다.</summary>
        public const int GuessMore = 0, GuessLess = 1, GuessEqual = 2;

        /// <summary>그 굴림의 맞는 답(크리티컬 대 상태이상 심볼).</summary>
        public static int GuessOf(IReadOnlyList<AttackDieFace> faces)
        {
            int c = AttackDice.Count(faces, AttackDieFace.CriticalHit), sym = AttackDice.Count(faces, AttackDieFace.StatusSymbol);
            return c > sym ? GuessMore : c < sym ? GuessLess : GuessEqual;
        }

        /// <summary>그 포켓몬 파티의 다른 기절하지 않은 포켓몬 가운데 그런 기술(시그니처·배운 칸)을 아는 것의 수(「For each other non-fainted Pokémon in your party with …」).</summary>
        static int PartyKnowing(GameState s, Ctx ctx, int user, Predicate<int> move)
        {
            if (user < 0) return 0;
            var party = new List<int>(6);
            EffectRules.PartyOf(s, ctx, user, party, user);
            var moves = new List<int>(4);
            int n = 0;
            foreach (int p in party)
            {
                PokemonRules.MoveOptions(ctx, s.Pokemon[p], moves);
                if (moves.Exists(m => m >= 0 && move(m))) n++;
            }
            return n;
        }

        /// <summary>revenge 「For each fainted Pokémon in your party」: 그 쪽(좌석 = 파티, 가상 = 배틀 명단 — 갤럭시단도 센다, 원작 커뮤니티 확인)의 기절한 포켓몬 수.</summary>
        static int FaintedInParty(GameState s, Ctx ctx, int user)
        {
            if (user < 0) return 0;
            var ids = new List<int>(6);
            int owner = s.Pokemon[user].Owner;
            if (owner >= 0) foreach (int p in s.Seats[owner].Party) { if (p >= 0) ids.Add(p); }
            else if (s.Battle != null)
                foreach (var side in s.Battle.Sides)
                    if (side.Owner == owner && side.Roster.Contains(user)) ids.AddRange(side.Roster);
            int n = 0;
            foreach (int p in ids)
                if (PokemonRules.IsFainted(ctx, s.Pokemon[p])) n++;
            return n;
        }

        /// <summary>destiny_bond·rampart_riposte 「damage taken last turn」: 바로 앞 배틀 턴에 그 포켓몬이 받은 피해(BattleState.TakenLast — 버퍼에 든 피해 그대로).</summary>
        static int TakenLast(GameState s, int user)
        {
            var b = s.Battle;
            if (b == null || user < 0) return 0;
            int n = 0;
            for (int i = 0; i + 1 < b.TakenLast.Count; i += 2)
                if (b.TakenLast[i] == user) n += b.TakenLast[i + 1];
            return n;
        }

        /// <summary>rollout·lift_off 「Each time this attack is used this battle」: 이 배틀에서 그 포켓몬이 그 기술을 쓴 수(이번 공격은 빼고 — 첫 사용 = 카드 공격력 [가정 G21]).</summary>
        static int UsedThisBattle(GameState s, int user, int move)
        {
            var b = s.Battle;
            if (b == null || user < 0) return 0;
            int n = 0;
            for (int i = 0; i + 1 < b.UsedMoves.Count; i += 2)
                if (b.UsedMoves[i] == user && b.UsedMoves[i + 1] == move) n++;
            return n;
        }

        /// <summary>electro_ball 「the current initiative of the user」: 카드 우선도 + 우선도 보정(마비 −3 · 민첩의 띠 +2 — 원작 커뮤니티 확인).</summary>
        static int Initiative(GameState s, Ctx ctx, int user)
        {
            var mods = new List<Mod>(2);
            BattleRules.PriorityMods(s, ctx, user, mods);
            int v = ctx.Cards.Pokemon[s.Pokemon[user].Def].Initiative;
            foreach (var m in mods) v += m.Delta;
            return Math.Max(0, v);
        }

        // ── 복사·섞기 · 맞히기 · 흡수(rulings G21) ──

        /// <summary>그 포켓몬의 기술 후보(시그니처·배운 칸) 가운데 플랜 B와 except(키)를 뺀 것. 갤럭시단의 플랜 B는 그 포켓몬의 기술이 아니다(원작 커뮤니티 확인).</summary>
        static List<int> MovesOf(Ctx ctx, PokemonState p, string except, List<int> into = null)
        {
            var r = into ?? new List<int>(4);
            var moves = new List<int>(4);
            PokemonRules.MoveOptions(ctx, p, moves);
            foreach (int m in moves)
                if (m >= 0 && (except == null || ctx.Cards.Moves[m].Key != except) && !r.Contains(m)) r.Add(m);
            return r;
        }

        /// <summary>
        /// 그 기술을 쓰는 쪽(owner)이 against에게 쓸 때의 기본 공격력(foul_play 「Copy the attack strength of a move the target knows」): 카드 공격력(원작 커뮤니티 확인),
        /// 「?」면 「공격력 = n」 셈 처리기로 그 쪽에서 본 값(원작 커뮤니티 확인), 셈이 없으면(mimic·metronome·foul_play) 0 — 「Minimum 1」이 된다(같은 답).
        /// </summary>
        public static int BaseStrength(GameState s, Ctx ctx, int move, int owner, int against)
        {
            var d = ctx.Cards.Moves[move];
            if (d.AttackStrength != null) return d.AttackStrength.Value;
            foreach (int id in ctx.Effects.OfMove(move))
            {
                var e = ctx.Effects.Effects[id];
                if (e.Window == Window.BeforeRoll && e.Handler != null && EffectHandlers.Static.TryGetValue(e.Handler, out var h) && h.Sets)
                    return h.Value(s, ctx, move, owner, against, null);
            }
            return 0;
        }

        /// <summary>
        /// foul_play 「Copy the attack strength of a move the target knows. Minimum 1 attack strength.」: 효과 주인이 대상의 기술 하나를 고르고(하나면 묻지 않는다), 이번 공격력 = max(1, 그 기술의 기본 공격력)
        /// (BattleState.StrengthTo — 「공격력 = n」, 지닌물건 등은 그 뒤 G14). 가상 쪽 [가정 G21] = 가장 큰 것.
        /// </summary>
        static EffectStepResult FoulPlay(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            if (f.User < 0 || f.Target < 0) return EffectStepResult.Done;
            var options = MovesOf(ctx, s.Pokemon[f.Target], null);
            if (options.Count == 0) return EffectStepResult.Done;
            int pick = options[0];
            if (options.Count > 1)
            {
                int best = options[0];
                foreach (int m in options)
                    if (BaseStrength(s, ctx, m, f.Target, f.User) > BaseStrength(s, ctx, best, f.Target, f.User)) best = m;
                var r = EffectRules.Pick(s, ctx, fx, f, EffectRules.Responder(s, f.Owner), options, false, f.Source, best, out pick);
                if (r != EffectStepResult.Continue) return r;
                if (!options.Contains(pick)) pick = options[0];
            }
            s.Battle.StrengthTo = Math.Max(FoulPlayMin, BaseStrength(s, ctx, pick, f.Target, f.User));
            EffectRules.Spend(s, ctx, f);
            return EffectStepResult.Done;
        }

        /// <summary>다른 기술을 부르거나 복사하는 기술(kinesis · mimic · metronome).</summary>
        static bool Redirects(Ctx ctx, int move)
        {
            string k = ctx.Cards.Moves[move].Key;
            return k == "kinesis" || k == "mimic" || k == "metronome";
        }

        /// <summary>
        /// 이미 부르거나 복사한 기술의 효과로 다시 고를 때(kinesis → mimic 등)는 부르거나 복사하는 기술을 빼고 고른다 — 한 번만 이어진다 [가정 G21: mimic이 kinesis를 고르고 kinesis가 mimic을 복사하는 식의 끝없는 고리를 막는다].
        /// </summary>
        static void Unnest(GameState s, Ctx ctx, List<int> options)
        {
            if (s.Battle.EffectsOf >= 0) options.RemoveAll(m => Redirects(ctx, m));
        }

        /// <summary>
        /// 이번 공격을 그 기술로 한다(mimic·metronome 「attack the target with this move」 — 공격력·타입·효과가 그 기술, 쓴 기술(직전 기술·혼란)은 그대로 mimic·metronome:
        /// 원작 커뮤니티 확인). 부른 기술의 굴리기 전 절(타입 고르기 등)을 이어 쌓는다.
        /// </summary>
        static EffectStepResult Call(GameState s, Ctx ctx, Emitter fx, EffectFrame f, int move)
        {
            var b = s.Battle;
            b.Called = move;
            b.EffectsOf = move;
            // 부른 기술을 알린다(화면의 「○○의 흉내내기로 △△이(가) 나왔다!」 — 같은 공격의 둘째 MoveSelected, 단계 없음)
            if (fx.On) fx.Add(new MoveSelected { Side = BattleFlow.SideOfAny(s, b, f.User), MoveKey = move, Type = BattleRules.AttackType(s, ctx, b.Move), Power = BattleRules.MoveStrength(ctx, move) });
            EffectRules.Spend(s, ctx, f);
            EffectRules.Trigger(s, ctx, Window.BeforeRoll);
            return EffectStepResult.Done;
        }

        /// <summary>
        /// mimic 「Select a move from the target and then attack the target with this move.」: 효과 주인이 대상의 기술(플랜 B·mimic 빼고) 하나를 고른다(하나면 묻지 않는다). 가상 쪽 [가정 G21] = 원칙 값이 가장 큰 것.
        /// 대상이 mimic만 알면 metronome처럼 한다(원작 커뮤니티 확인 — mimic 대 mimic).
        /// </summary>
        static EffectStepResult Mimic(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            if (f.User < 0 || f.Target < 0) return EffectStepResult.Done;
            var options = MovesOf(ctx, s.Pokemon[f.Target], "mimic");
            Unnest(s, ctx, options);
            if (options.Count == 0) return Metronome(s, ctx, fx, f);
            int pick = options[0];
            if (options.Count > 1)
            {
                int best = options[0];
                long bestV = long.MinValue;
                foreach (int m in options)
                {
                    long v = PrincipleRules.Value(ctx, m, s.Pokemon[f.User], s.Pokemon[f.Target]);
                    if (v > bestV)
                    {
                        best = m;
                        bestV = v;
                    }
                }
                var r = EffectRules.Pick(s, ctx, fx, f, EffectRules.Responder(s, f.Owner), options, false, f.Source, best, out pick);
                if (r != EffectStepResult.Continue) return r;
                if (!options.Contains(pick)) pick = options[0];
            }
            return Call(s, ctx, fx, f, pick);
        }

        /// <summary>
        /// metronome 「Shuffle the Evolution Pokémon deck and then attack the target with the top move of the deck.」: 진화 덱을 섞고(PileShuffled) 맨 위 카드의 시그니처 기술로 공격한다(카드는 덱에 그대로).
        /// 덱이 비었으면 부를 기술이 없다(공격력 「?」 = 1, 타입없음) [가정 G21].
        /// </summary>
        static EffectStepResult Metronome(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            var deck = s.Decks[(int)DeckId.Evolution];
            if (deck.Count == 0) return EffectStepResult.Done;
            Piles.Shuffle(deck, s.Rng);
            if (fx.On) fx.Add(new PileShuffled { Pile = DeckId.Evolution });
            int top = ctx.Cards.Pokemon[deck[deck.Count - 1]].Signature;
            if (s.Battle.EffectsOf >= 0 && Redirects(ctx, top)) return EffectStepResult.Done; // 이어 부르지 않는다(Unnest)
            return Call(s, ctx, fx, f, top);
        }

        /// <summary>
        /// kinesis 「Copy the effect of a move the user or target knows.」: 효과 주인이 자기·대상의 기술(플랜 B·kinesis 빼고, 자기 것 → 대상 것) 하나를 고르고, 이번 공격의 효과 = 그 기술의 효과(공격력·타입은 kinesis).
        /// 공격력을 정하는 효과도 복사한다(원작 커뮤니티 확인). 한도는 복사한 효과의 것을 쓴 포켓몬에게(원작 커뮤니티 확인
        /// 「It does count as the effect being used for you」), 겹치지 않는 효과는 복사해도 겹치지 않는다(원작 커뮤니티 확인). 가상 쪽 [가정 G21] = 효과가 있는 첫 기술.
        /// </summary>
        static EffectStepResult Kinesis(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            if (f.User < 0) return EffectStepResult.Done;
            var options = MovesOf(ctx, s.Pokemon[f.User], "kinesis");
            if (f.Target >= 0) MovesOf(ctx, s.Pokemon[f.Target], "kinesis", options);
            Unnest(s, ctx, options);
            if (options.Count == 0) return EffectStepResult.Done;
            int pick = options[0];
            if (options.Count > 1)
            {
                int first = options.FindIndex(m => ctx.Effects.Implemented(m));
                var r = EffectRules.Pick(s, ctx, fx, f, EffectRules.Responder(s, f.Owner), options, false, f.Source, options[Math.Max(0, first)], out pick);
                if (r != EffectStepResult.Continue) return r;
                if (!options.Contains(pick)) pick = options[0];
            }
            s.Battle.EffectsOf = pick;
            EffectRules.Spend(s, ctx, f);
            EffectRules.Trigger(s, ctx, Window.BeforeRoll);
            return EffectStepResult.Done;
        }

        /// <summary>
        /// future_sight 「Will there be more, less or an equal amount of critical hits rolled than status symbols? If incorrect, deal -3 damage.」: 굴리기 전에 효과 주인이 셋 중 하나를 고른다
        /// (Options = <see cref="GuessMore"/> · <see cref="GuessLess"/> · <see cref="GuessEqual"/>, BattleState.Guess). 틀리면 효과 피해 −3(셈 처리기 future_sight_miss — ②). 가상 쪽 [가정 G21] = 가장 잘 나오는 답.
        /// </summary>
        static EffectStepResult FutureSight(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            var b = s.Battle;
            var options = new List<int> { GuessMore, GuessLess, GuessEqual };
            var r = EffectRules.Pick(s, ctx, fx, f, EffectRules.Responder(s, f.Owner), options, false, f.Source, LikelyGuess(s, ctx, f), out int pick);
            if (r != EffectStepResult.Continue) return r;
            b.Guess = options.Contains(pick) ? pick : GuessMore;
            return EffectStepResult.Done;
        }

        static int LikelyGuess(GameState s, Ctx ctx, EffectFrame f)
        {
            var b = s.Battle;
            if (b == null || f.User < 0) return GuessEqual;
            int n = BattleRules.RollPower(s, ctx, b.Move, f.User, f.Target, new List<Mod>());
            var w = new long[3];
            foreach (var o in AttackDice.Outcomes(n))
                w[o.CriticalHits > o.StatusSymbols ? GuessMore : o.CriticalHits < o.StatusSymbols ? GuessLess : GuessEqual] += o.Weight;
            int best = GuessMore;
            for (int i = 1; i < 3; i++)
                if (w[i] > w[best]) best = i;
            return best;
        }

        /// <summary>
        /// giga_drain·power_whip 「Heal for half the damage dealt, rounded down.」: 이번 공격이 대상에게 준 피해(그 턴 버퍼의 공격 피해 — 상성·2배 뒤 최종, 대상의 남은 체력과 상관없이 [가정 G21 —
        /// 뜻이 같은 v1.4 문구의 원작 커뮤니티 확인와 같다])의 절반(내림)을 자신이 회복한다(그 턴 버퍼 — 독이면 0, G14).
        /// </summary>
        static EffectStepResult DrainHalf(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            var b = s.Battle;
            if (f.User < 0 || f.Target < 0) return EffectStepResult.Done;
            int dealt = 0;
            var buf = b.DamageBuffer;
            for (int i = 0; i < buf.Count; i += 4)
                if (buf[i] == f.Target && buf[i + 1] < 0 && buf[i + 2] == (int)SourceKind.Move && buf[i + 3] == b.Move) dealt -= buf[i + 1];
            if (dealt / 2 > 0) BattleFlow.Buffer(b, f.User, dealt / 2, f.Source);
            return EffectStepResult.Done;
        }

        // ── 둘째 기술 · 마지막 기술(rulings G21 — 배틀 진행은 BattleFlow.ExtraAttack) ──

        /// <summary>
        /// quick_attack 등 「Effect may be used once until the user switches out: The user may use a second move this turn.」(절 한도 UntilSwitch): 기술 효과 자리(StepIndex 0)에서는 둘째 기술을 쓸 수 있다고 적기만 한다
        /// (BattleState.SecondBy — 한도는 쓸 때 쓴다). 둘째 기술 · encore의 마지막 기술 중이면 주지 않는다(원작 커뮤니티 확인).
        /// 공격이 끝나면 BattleFlow가 같은 절의 프레임을 StepIndex 1로 다시 쌓아 고르기를 연다 — <see cref="PickExtra"/>.
        /// </summary>
        static EffectStepResult SecondMove(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            var b = s.Battle;
            if (f.StepIndex >= 1) return PickExtra(s, ctx, fx, f, false);
            if (f.User < 0 || b.InSecond || b.FinalSide >= 0 || b.SecondBy >= 0) return EffectStepResult.Done;
            b.SecondBy = f.User;
            b.SecondEffect = f.EffectId;
            b.SecondTarget = f.Target;
            return EffectStepResult.Done;
        }

        /// <summary>
        /// encore 「If the user faints when it is next attacked, the user may use a final move before fainting.」: 자신에게 「다음에 공격받을 때」 보정(FinalMove, Value = 이 절의 효과 Id)을 건다.
        /// 그 공격으로 체력이 0이 되면 BattleFlow가 같은 절의 프레임을 StepIndex 1로 쌓아 마지막 기술을 묻는다(<see cref="PickExtra"/>). 그 기술이 무엇을 하든 그 뒤 쓰러진다(원작 커뮤니티 확인).
        /// </summary>
        static EffectStepResult Encore(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            if (f.StepIndex >= 1) return PickExtra(s, ctx, fx, f, true);
            if (f.User < 0) return EffectStepResult.Done;
            ModRules.Add(s, f.User, ModScope.NextAttacked, EffectRole.Defender, EffectOp.FinalMove, AttackDieFace.None, f.EffectId, f.Source);
            return EffectStepResult.Done;
        }

        /// <summary>
        /// 추가 공격의 기술을 묻는다(Reaction/EffectPick — Options = 쓸 수 있는 기술(결단의 머리띠면 그 기술만), 「안 씀」이 첫 답 — 「may」). 가상 쪽 = 원칙 기술(G15).
        /// 고르면 BattleState.ExtraMove(진행은 BattleFlow), 둘째 기술은 그때 한도를 쓴다. 안 쓰면 둘째 기술은 없던 것이 된다(RuleOptions.SecondMoveDeclineSpends면 한도도 쓴다).
        /// 알파 스트라이크를 고르면(StepIndex 2) 이어서 배울 수 있는 타입을 묻는다(꼭 고른다 — 가상 쪽 = PrincipleRules.AlphaType) → BattleState.ExtraType(rulings A26 ①, M5 규칙 끝 검토 반영).
        /// </summary>
        static EffectStepResult PickExtra(GameState s, Ctx ctx, Emitter fx, EffectFrame f, bool final)
        {
            var b = s.Battle;
            var user = s.Pokemon[f.User];
            var types = new List<int>(4);
            if (f.StepIndex == 2)
            {
                LegendRules.AlphaTypes(ctx, user, types);
                int best = PrincipleRules.AlphaType(ctx, user, f.Target >= 0 ? s.Pokemon[f.Target] : null);
                var tr = EffectRules.Pick(s, ctx, fx, f, EffectRules.Responder(s, f.Owner), types, false, f.Source, best, out int type);
                if (tr != EffectStepResult.Continue) return tr;
                b.ExtraType = types.Contains(type) ? type : types[0];
                return EffectStepResult.Done;
            }
            var options = new List<int>(4);
            PokemonRules.MoveOptions(ctx, user, options);
            HeldItems.LockMoves(ctx, user, options);
            int principle = f.Target >= 0 ? PrincipleRules.Move(ctx, user, s.Pokemon[f.Target], out _) : options[0];
            var r = EffectRules.Pick(s, ctx, fx, f, EffectRules.Responder(s, f.Owner), options, true, f.Source, options.Contains(principle) ? principle : options[0], out int pick);
            if (r != EffectStepResult.Continue) return r;
            bool use = options.Contains(pick);
            if (!final)
            {
                b.SecondBy = -1;
                if (use || s.Options.SecondMoveDeclineSpends) EffectRules.Spend(s, ctx, f);
            }
            if (use)
            {
                b.ExtraMove = pick;
                b.ExtraFinal = final;
                b.ExtraType = -1;
                if (pick == BattleRules.AlphaStrike)
                {
                    LegendRules.AlphaTypes(ctx, user, types);
                    if (types.Count == 1) b.ExtraType = types[0];
                    else if (types.Count > 1)
                    {
                        f.StepIndex = 2;
                        return EffectStepResult.Continue;
                    }
                }
            }
            return EffectStepResult.Done;
        }

        /// <summary>
        /// hangry_wheel 「You may consume 1 Apricorn to increase attack strength to 7 and to change to Full Belly Form if possible.」: belch와 같이 규토리를 쓸지 묻고, 썼으면 공격력 7 + 배부른 모양(상태 1)으로.
        /// 폼이 없는 포켓몬이 배운 기술이어도 공격력 7은 된다(원작 커뮤니티 확인).
        /// </summary>
        static EffectStepResult HangryWheel(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            var r = Belch(s, ctx, fx, f);
            if (r == EffectStepResult.Done && f.User >= 0 && s.Battle.StrengthTo == BelchStrength)
                FormRules.Change(s, ctx, fx, f.User, FormRules.MoveForm(ctx, s.Pokemon[f.User], f.Source.Id, FormRules.DefaultState), f.Source);
            return r;
        }

        /// <summary>
        /// adaptive_cloak 「The user may change Cloak Form if possible. Now choose either base type of the user as the type of this attack.」(굴리기 전 — 그 공격에 든다, 원작 커뮤니티 확인):
        /// ① 이 기술을 시그니처로 가진 그 종의 다른 폼 가운데 하나로 바꿀지(「안 함」이 첫 답 — 「may」) ② 바꾼 뒤 그 포켓몬의 타입 둘 가운데 이 공격의 타입(BattleState.TypeTo).
        /// ②는 폼을 바꾸지 않아도 한다(원작 커뮤니티 확인). 가상 쪽 [가정 plan §5 #86 ⑤] = 받는 포켓몬에게 가장 좋은 타입을 주는 폼(지금 폼보다 나을 때만 바꾼다) · 그 타입.
        /// </summary>
        static EffectStepResult AdaptiveCloak(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            if (f.User < 0) return EffectStepResult.Done;
            var p = s.Pokemon[f.User];
            if (f.StepIndex == 0)
            {
                var forms = new List<int>(3);
                FormRules.MoveForms(ctx, p, f.Source.Id, forms);
                forms.Remove(p.Def);
                if (forms.Count > 0 && !ModRules.NoFormChange(p))
                {
                    var r = EffectRules.Pick(s, ctx, fx, f, EffectRules.Responder(s, f.Owner), forms, true, f.Source, BestCloak(s, ctx, f, p.Def, forms), out int to);
                    if (r != EffectStepResult.Continue) return r;
                    if (forms.Contains(to)) FormRules.Change(s, ctx, fx, f.User, to, f.Source);
                }
                f.StepIndex = 1;
                return EffectStepResult.Continue;
            }
            var types = new List<int>(2);
            foreach (var t in ctx.Cards.Pokemon[p.Def].Types)
                if (!types.Contains((int)t)) types.Add((int)t);
            int type = types[0];
            if (types.Count > 1)
            {
                var r = EffectRules.Pick(s, ctx, fx, f, EffectRules.Responder(s, f.Owner), types, false, f.Source, BestType(s, ctx, f, types), out type);
                if (r != EffectStepResult.Continue) return r;
                if (!types.Contains(type)) type = types[0];
            }
            s.Battle.TypeTo = type;
            EffectRules.Spend(s, ctx, f);
            return EffectStepResult.Done;
        }

        /// <summary>가상 쪽의 도롱 폼: 폼마다 그 타입 가운데 가장 좋은 상성 값, 지금 폼보다 큰 첫 폼(없으면 −1 = 안 바꿈).</summary>
        static int BestCloak(GameState s, Ctx ctx, EffectFrame f, int now, List<int> forms)
        {
            if (f.Target < 0) return -1;
            int best = -1, bestV = BestBonus(s, ctx, f, now);
            foreach (int form in forms)
            {
                int v = BestBonus(s, ctx, f, form);
                if (v > bestV)
                {
                    best = form;
                    bestV = v;
                }
            }
            return best;
        }

        static int BestBonus(GameState s, Ctx ctx, EffectFrame f, int def)
        {
            var types = new List<int>(2);
            foreach (var t in ctx.Cards.Pokemon[def].Types) types.Add((int)t);
            var target = ctx.Cards.Pokemon[s.Pokemon[f.Target].Def].Types;
            var t2 = target.Count > 1 ? target[1] : PokemonType.Typeless;
            int v = int.MinValue;
            foreach (int t in types) v = Math.Max(v, TypeChart.Bonus((PokemonType)t, target[0], t2));
            return v;
        }

        /// <summary>
        /// zen_restoration 「Inflict the Drowsy status on the user. Heal 6 damage if the user has just become Drowsy. Change to Blazing Form if possible.」(BeforeDamage):
        /// 자신에게 졸음 → 이번에 졸음이 됐으면 6 회복(그 턴 피해 버퍼) → 노말모드(상태 1)로.
        /// </summary>
        static EffectStepResult ZenRestoration(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            if (f.User < 0) return EffectStepResult.Done;
            var p = s.Pokemon[f.User];
            bool had = StatusRules.Has(p, StatusEffect.Drowsy);
            EffectRules.InflictOne(s, ctx, fx, f.User, StatusEffect.Drowsy, f.Source);
            if (!had && StatusRules.Has(p, StatusEffect.Drowsy)) BattleFlow.Buffer(s.Battle, f.User, ZenHeal, f.Source);
            FormRules.Change(s, ctx, fx, f.User, FormRules.MoveForm(ctx, p, f.Source.Id, FormRules.DefaultState), f.Source);
            return EffectStepResult.Done;
        }

        /// <summary>zen_restoration의 회복량.</summary>
        public const int ZenHeal = 6;

        /// <summary>
        /// 「Choose any learnable type of the user as the type of this attack」(any = false — 카드의 배울 수 있는 타입) · 「Choose any type, this attack becomes the chosen type」(any = true — 타입없음 포함 모든 타입).
        /// 고른 타입 = 이번 공격의 타입(BattleState.TypeTo — 상성·신오의 심장·주사위 표시, BattleRules.AttackType). 후보가 하나면 묻지 않는다.
        /// 가상 쪽 [가정 G20] = 받는 포켓몬에게 상성 값이 가장 큰 타입(같으면 후보 순서 — G15 원칙 AI의 「가장 큰 값」과 같은 결).
        /// </summary>
        static EffectStepResult ChooseType(GameState s, Ctx ctx, Emitter fx, EffectFrame f, bool any)
        {
            var b = s.Battle;
            if (f.User < 0) return EffectStepResult.Done;
            var options = new List<int>(19);
            if (any)
                foreach (PokemonType t in Enum.GetValues(typeof(PokemonType))) options.Add((int)t);
            else
                foreach (var t in ctx.Cards.Pokemon[s.Pokemon[f.User].Def].LearnTypes)
                    if (!options.Contains((int)t)) options.Add((int)t);
            if (options.Count == 0) return EffectStepResult.Done;
            int type = options[0];
            if (options.Count > 1)
            {
                var r = EffectRules.Pick(s, ctx, fx, f, EffectRules.Responder(s, f.Owner), options, false, f.Source, BestType(s, ctx, f, options), out type);
                if (r != EffectStepResult.Continue) return r;
                if (!options.Contains(type)) type = options[0];
            }
            b.TypeTo = type;
            EffectRules.Spend(s, ctx, f);
            return EffectStepResult.Done;
        }

        static int BestType(GameState s, Ctx ctx, EffectFrame f, List<int> options)
        {
            if (f.Target < 0) return options[0];
            var types = ctx.Cards.Pokemon[s.Pokemon[f.Target].Def].Types;
            var t2 = types.Count > 1 ? types[1] : PokemonType.Typeless;
            int best = options[0], bestV = int.MinValue;
            foreach (int t in options)
            {
                int v = TypeChart.Bonus((PokemonType)t, types[0], t2);
                if (v > bestV)
                {
                    best = t;
                    bestV = v;
                }
            }
            return best;
        }

        /// <summary>
        /// belch 「You may consume 1 Apricorn to increase attack strength to 7」: 굴리기 전에 효과 주인(좌석)에게 쓸 규토리 색을 묻는다(「안 씀」이 첫 답 — 「You may」).
        /// 쓰면 규토리 1개(ApricornSpent) + 이번 공격의 공격력 = 7(BattleState.StrengthTo — 「공격력 = n」, 다른 보정은 그 뒤에 더한다 G14).
        /// 가방이 없는 쪽(야생·갤럭시단)은 쓰지 못한다(원작 커뮤니티 확인).
        /// </summary>
        static EffectStepResult Belch(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            if (f.Owner < 0 || f.Owner >= s.SeatCount) return EffectStepResult.Done;
            var colors = new List<int>(SeatState.ApricornColors);
            for (int c = 0; c < SeatState.ApricornColors; c++)
                if (s.Seats[f.Owner].Apricorns[c] > 0) colors.Add(c);
            if (colors.Count == 0) return EffectStepResult.Done;
            var r = EffectRules.Pick(s, ctx, fx, f, EffectRules.Responder(s, f.Owner), colors, true, f.Source, -1, out int color);
            if (r != EffectStepResult.Continue) return r;
            if (color < 0 || !colors.Contains(color)) return EffectStepResult.Done; // 안 씀
            EffectRules.SpendApricorn(s, fx, f.Owner, color, f.Source);
            s.Battle.StrengthTo = BelchStrength;
            EffectRules.Spend(s, ctx, f);
            return EffectStepResult.Done;
        }

        /// <summary>
        /// photon_geyser 「Inflict any status effect on the target. If any status symbols are rolled: Inflict another status effect simultaneously.」: 효과 주인이 상태 하나를 고르고,
        /// 마지막 굴림에 심볼이 있으면 다른 상태 하나를 더 골라 둘을 동시에 건다(StatusRules.InflictBoth). 대상에게 상태이상이 이미 있으면 아무것도 걸리지 않아 묻지 않는다
        /// (원작 커뮤니티 확인).
        /// 고를 수 있는 상태는 여섯 모두(「any」를 좁히지 않는다 — 면역이어도 고를 수 있다). 가상 쪽 [가정 G20] = 걸리는 첫 상태(상태 표 순서), 둘째도 같은 셈.
        /// Locals[0] = 고른 첫 상태.
        /// </summary>
        static EffectStepResult PhotonGeyser(GameState s, Ctx ctx, Emitter fx, EffectFrame f)
        {
            var b = s.Battle;
            if (f.Target < 0) return EffectStepResult.Done;
            var tp = s.Pokemon[f.Target];
            int seat = EffectRules.Responder(s, f.Owner);
            if (f.StepIndex == 0)
            {
                if (tp.Status != StatusEffect.None) return EffectStepResult.Done;
                var options = AllStatuses(StatusEffect.None);
                var r = EffectRules.Pick(s, ctx, fx, f, seat, options, false, f.Source, FirstApplies(ctx, tp, options), out int first);
                if (r != EffectStepResult.Continue) return r;
                if (!options.Contains(first)) first = options[0];
                f.Locals[0] = first;
                f.StepIndex = 1;
                return EffectStepResult.Continue;
            }
            var a = (StatusEffect)f.Locals[0];
            if (AttackDice.Count(b.Faces, AttackDieFace.StatusSymbol) == 0)
            {
                EffectRules.InflictOne(s, ctx, fx, f.Target, a, f.Source);
                return EffectStepResult.Done;
            }
            var rest = AllStatuses(a);
            var r2 = EffectRules.Pick(s, ctx, fx, f, seat, rest, false, f.Source, FirstApplies(ctx, tp, rest), out int second);
            if (r2 != EffectStepResult.Continue) return r2;
            if (!rest.Contains(second)) second = rest[0];
            EffectRules.InflictBoth(s, ctx, fx, f.Target, a, (StatusEffect)second, f.Source);
            return EffectStepResult.Done;
        }

        static List<int> AllStatuses(StatusEffect except)
        {
            var r = new List<int>(6);
            for (int i = (int)StatusEffect.Confused; i <= (int)StatusEffect.Drowsy; i++)
                if (i != (int)except) r.Add(i);
            return r;
        }

        static int FirstApplies(Ctx ctx, PokemonState p, List<int> options)
        {
            foreach (int st in options)
                if (StatusRules.Check(ctx, p, (StatusEffect)st) == StatusResult.Applied) return st;
            return options[0];
        }
    }
}
