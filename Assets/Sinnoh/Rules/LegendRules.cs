using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 전설 포켓몬(룰북 p.6 「Legendary Pokémon act differently to regular Pokémon in two ways」 · p.4 · p.17, rulings A26, M5 「전설·알파·폼」).
    /// - 전설 특수기술: 알파 스트라이크(기술, <see cref="BattleRules.AlphaStrike"/>)와 비스트 부스트(특성). 배운 기술이 덮는다 — 첫 기술이 알파 스트라이크, 둘째가 비스트 부스트를 덮고
    ///   잊으면 거꾸로 돌아온다(p.6 「Alpha Strike is always overwritten before Beast Boost」 · 「When captured, you can add/remove moves to overwrite/reveal these tactics」) — 그래서 배운 칸 수로 센다.
    ///   가진 포켓몬 = PokemonState.HasLegendaryMove(전설·우두머리로 나온 야생 — BattleRules.LegendMovesEligible, 잡아도 남는다).
    /// - 알파 스트라이크: 배울 수 있는 타입 하나로 친다(원작 커뮤니티 확인 — 답이 타입을 싣는다), 공격력 4,
    ///   그 포켓몬이 직전 턴에 썼으면 2(포켓몬마다 — 원작 커뮤니티 확인). 고른 타입이 이 공격의 타입이다 —
    ///   상성 · 신오의 심장 +1 · 지닌 유물 +1이 모두 고른 타입으로 본다(원작 커뮤니티 확인).
    /// - 비스트 부스트: 전설과 배틀하던 상대 출전 포켓몬이 쓰러질 때마다 전설의 피로 −3, 그 쪽의 다음 공격 공격력 +1(쌓인다 — 울트라 온슬롯 3 「add to its effect」와 같은 결 [가정 plan §5 #86 ③]).
    ///   그 전설이 나가면 사라진다(원작 커뮤니티 확인 — BattleSide.BeastBoost, LeaveField가 지운다).
    /// - 전설 장소(p.4 「Artifact Legendary Pokémon: Can only be encountered at their home location if you are holding the matching Artifact」): 맞는 유물을 가진 좌석이 그 칸에서 찾아보기 →
    ///   전설 덱의 그 카드와 BattleKind.Legendary 배틀. 지거나 도망·기권이면 카드는 덱으로(다시 도전할 수 있다 — 원작 커뮤니티 확인), 쓰러뜨리면 버림 더미.
    /// </summary>
    public static class LegendRules
    {
        /// <summary>비스트 부스트: 피로 −3, 다음 공격 공격력 +1(p.6).</summary>
        public const int BeastBoostFatigue = 3;
        public const int BeastBoostPower = 1;

        static int Learned(PokemonState p)
        {
            int n = 0;
            foreach (int m in p.Moves)
                if (m >= 0) n++;
            return n + BreachRules.Attached(p); // 균열 카드도 기술처럼 붙어 덮는다(rulings A52 ①)
        }

        /// <summary>알파 스트라이크를 쓸 수 있나: 전설 특수기술을 가졌고 배운 기술이 없다(첫 기술이 덮는다).</summary>
        public static bool HasAlphaStrike(PokemonState p) => p.HasLegendaryMove && Learned(p) == 0;

        /// <summary>비스트 부스트가 있나: 전설 특수기술을 가졌고 배운 기술이 하나 이하다(둘째 기술이 덮는다).</summary>
        public static bool HasBeastBoost(PokemonState p) => p.HasLegendaryMove && Learned(p) <= 1;

        /// <summary>알파 스트라이크로 고를 수 있는 타입(그 포켓몬 카드의 배울 수 있는 타입, 카드 순서 · 같은 타입은 한 번).</summary>
        public static void AlphaTypes(Ctx ctx, PokemonState p, List<int> into)
        {
            into.Clear();
            foreach (var t in ctx.Cards.Pokemon[p.Def].LearnTypes)
                if (!into.Contains((int)t)) into.Add((int)t);
        }

        /// <summary>
        /// 공격력 보정(BattleRules.RollPower가 부른다): 알파 스트라이크를 그 포켓몬이 직전 턴에 썼으면 「공격력 2」(맨 앞 — G14 「공격력이 n이 된다」) ·
        /// 그 쪽에 쌓인 비스트 부스트(출전 중인 포켓몬의 공격에). 출처 = 규칙(AlphaStrike · BeastBoost).
        /// </summary>
        public static void PowerMods(GameState s, Ctx ctx, int move, int attacker, List<Mod> into)
        {
            if (attacker < 0) return;
            // 울트라 온슬롯 5의 둘째 알파 스트라이크도 「이어서 두 번」이라 2(rulings A56 ⑥)
            if (move == BattleRules.AlphaStrike && (s.Pokemon[attacker].LastMove == BattleRules.AlphaStrike || UbOnslaught.Repeats(s, attacker)))
                into.Insert(0, new Mod { Source = SourceRef.Rule(RuleSource.AlphaStrike), Delta = BattleRules.AlphaStrikeRepeatStrength - BattleRules.AlphaStrikeStrength });
            var b = s.Battle;
            if (b == null) return;
            foreach (var side in b.Sides)
                if (side.Active == attacker && side.BeastBoost > 0)
                    into.Add(new Mod { Source = SourceRef.Rule(RuleSource.BeastBoost), Delta = side.BeastBoost });
        }

        /// <summary>
        /// 비스트 부스트(p.6 「Triggers whenever the Pokémon battling the Legendary Pokémon faints」): k 쪽 출전 포켓몬이 쓰러졌다 — 상대 쪽 출전 포켓몬이 비스트 부스트를 가졌고
        /// 쓰러지지 않았으면 피로 −3(0까지, FatigueChanged Cause = BeastBoost)과 다음 공격 +1. 함께 쓰러진 전설은 받지 않는다 [가정 plan §5 #86 ③].
        /// </summary>
        internal static void Fainted(GameState s, Ctx ctx, Emitter fx, BattleState b, int k)
        {
            var other = b.Sides[1 - k];
            int legend = other.Active;
            if (legend < 0) return;
            var p = s.Pokemon[legend];
            if (!HasBeastBoost(p) || PokemonRules.IsFainted(ctx, p)) return;
            int before = p.Fatigue;
            // expert R(rulings A35): 비스트 부스트 대신 피로 0 · 다음 공격 +2 · 회복 3(원작 커뮤니티 확인)
            bool expert = TrainerRules.Protector(s, ctx, other.Owner);
            p.Fatigue = expert ? 0 : System.Math.Max(0, p.Fatigue - BeastBoostFatigue);
            other.BeastBoost += expert ? TrainerRules.ProtectorPower : BeastBoostPower;
            if (expert) TrainerRules.Protected(s, fx, b, other.Owner, legend);
            if (fx.On) fx.Add(new FatigueChanged { Source = SourceRef.Rule(RuleSource.BeastBoost), PokemonId = legend, Before = before, After = p.Fatigue, Cause = FatigueCause.BeastBoost });
        }

        // ── 전설 장소 ──

        /// <summary>그 좌석의 칸이 전설 장소이고 맞는 유물을 가졌고(RelicRules.OpensSite) 그 전설 카드가 전설 덱에 있으면 그 고정물. 아니면 −1.</summary>
        public static int SiteAt(GameState s, Ctx ctx, int seat)
        {
            int cell = s.Seats[seat].Cell;
            if (cell < 0) return -1;
            var fixtures = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
            {
                if (fixtures[f].Kind != FixtureKind.LegendarySite || fixtures[f].Cell != cell) continue;
                return RelicRules.OpensSite(s, ctx, seat, f) && SiteCard(s, ctx, f) >= 0 ? f : -1;
            }
            return -1;
        }

        /// <summary>
        /// 그 전설 장소의 피규어가 보드에 서나(사용자 결정 123 · 128 — 보기 값, 규칙 상태와 무관): 등장 조건 유물이 없는 장소(호수 동굴 셋 · 신오신전 — 울트라 버스트 판에서만 만난다, rulings A43 ④ · A55 ①)는
        /// 울트라 버스트 판이고 그 전설이 아직 전설 덱에 있을 때(만날 수 있는 동안 — 잡거나 쓰러뜨리면 사라지고, 지거나 도망치면 덱으로 돌아와 그대로) · 신오의 심장 판에서는 서지 않는다(결정 128),
        /// 있으면 그 전설이 아직 전설 덱에 있고(잡히거나 쓰러지지 않음 — p.18) 탈락하지 않은 어느 좌석이든 그 장소의 유물을 가졌을 때(<see cref="RelicRules.OpensSite"/>).
        /// </summary>
        public static bool SiteFigureShown(GameState s, Ctx ctx, int fixture)
        {
            var f = ctx.Fixtures.Fixtures[fixture];
            if (f.Kind != FixtureKind.LegendarySite) return true;
            if (f.Artifact.Count == 0) return s.Campaign == Campaign.UltraBurst && SiteCard(s, ctx, fixture) >= 0;
            if (SiteCard(s, ctx, fixture) < 0) return false;
            for (int seat = 0; seat < s.SeatCount; seat++)
                if (!s.Seats[seat].Eliminated && RelicRules.OpensSite(s, ctx, seat, fixture)) return true;
            return false;
        }

        /// <summary>그 전설 장소의 전설 가운데 전설 덱에 있는 첫 카드(고정물 데이터 순서). 없으면 −1(잡혔거나 버림 더미).</summary>
        public static int SiteCard(GameState s, Ctx ctx, int fixture)
        {
            var deck = s.Decks[(int)DeckId.Legendary];
            foreach (int card in ctx.Fixtures.Fixtures[fixture].Legendary)
                if (deck.Contains(card)) return card;
            return -1;
        }

        /// <summary>
        /// 고정물 찾아보기(MapAction Search, Arg = 고정물 — 그 칸): 전설 장소 = 전설 덱의 그 카드와 BattleKind.Legendary · 전설 사당 = 그 칸의 전설 토큰(사당 전설 이벤트)과
        /// BattleKind.Legendary 또는 시작의 사당의 신 토큰과 BattleKind.God · 왜곡 게이트 = 무리 배틀(rulings A28). 배틀 칸 = 그 칸.
        /// </summary>
        internal static void SearchSite(GameState s, Ctx ctx, Emitter fx, int seat, int fixture)
        {
            var f = ctx.Fixtures.Fixtures[fixture];
            if (f.Kind == FixtureKind.Gate)
            {
                SearchGate(s, ctx, fx, seat, fixture);
                return;
            }
            if (f.Kind == FixtureKind.LegendaryShrine)
            {
                var t = s.Tokens[TokenAt(s, f.Cell)];
                s.Decks[(int)DeckId.Legendary].Remove(t.Def);
                if (t.Kind == TokenKind.God) BattleFlow.StartGod(s, ctx, fx, seat, t.Def, f.Cell);
                else BattleFlow.StartLegendary(s, ctx, fx, seat, t.Def, f.Cell);
                return;
            }
            int card = SiteCard(s, ctx, fixture);
            s.Decks[(int)DeckId.Legendary].Remove(card);
            if (s.Ub != null && UbTrial.LakeOf(ctx, card) >= 0) BattleFlow.StartTrial(s, ctx, fx, seat, card, f.Cell); // 호수 시련(rulings A55)
            else BattleFlow.StartLegendary(s, ctx, fx, seat, card, f.Cell);
        }

        /// <summary>
        /// 그 좌석이 지금 칸에서 찾아보기로 싸울 고정물(MapActions.Legal의 Search Arg): 전설 장소(<see cref="SiteAt"/>) · 전설 토큰·신 토큰이 있는 전설 사당 · 왜곡 게이트. 없으면 −1.
        /// 상호작용·쓸 포켓몬은 MapActions가 본다.
        /// </summary>
        public static int FightAt(GameState s, Ctx ctx, int seat)
        {
            int site = SiteAt(s, ctx, seat);
            if (site >= 0) return site;
            site = UbTrial.SiteAt(s, ctx, seat); // 울트라 버스트: 호수 동굴의 수호신 · 신오신전의 실버디(p.26, rulings A55)
            if (site >= 0) return site;
            int cell = s.Seats[seat].Cell;
            if (cell < 0) return -1;
            var fixtures = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
            {
                if (fixtures[f].Cell != cell) continue;
                if (fixtures[f].Kind == FixtureKind.LegendaryShrine && TokenAt(s, cell) >= 0) return f;
                if (fixtures[f].Kind == FixtureKind.Gate && !MapActions.Used(s, seat, UsageKind.EmptySearch, cell)) return f;
            }
            return -1;
        }

        /// <summary>그 칸의 전설 토큰·신 토큰 가운데 그 카드가 전설 덱에 있는 첫 것(토큰 목록 순서). 없으면 −1.</summary>
        public static int TokenAt(GameState s, int cell)
        {
            if (cell < 0) return -1;
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if ((t.Kind == TokenKind.Legend || t.Kind == TokenKind.God) && t.Cell == cell && !t.Destroyed && s.Decks[(int)DeckId.Legendary].Contains(t.Def)) return i;
            }
            return -1;
        }

        /// <summary>
        /// 전설·신을 쓰러뜨리거나 잡았다(p.18 「removed from the game once it is defeated or captured」): 그 카드(기본 폼)의 전설 토큰·신 토큰을 맵에서 뺀다(TokenRemoved).
        /// 전설 장소의 전설처럼 토큰이 없으면 아무것도 하지 않는다. 신 토큰이었으면 true.
        /// </summary>
        internal static bool RemoveToken(GameState s, Emitter fx, int seat, int card)
        {
            foreach (var t in s.Tokens)
            {
                if ((t.Kind != TokenKind.Legend && t.Kind != TokenKind.God) || t.Def != card || t.Cell < 0 || t.Destroyed) continue;
                int cell = t.Cell;
                t.Cell = -1;
                if (fx.On) fx.Add(new TokenRemoved { Seat = seat, Cell = cell, TokenKind = t.Kind });
                return t.Kind == TokenKind.God;
            }
            return false;
        }

        // ── 신(rulings A28 ①) ──

        static readonly string[] GodSpecies = { "483", "484", "487" };
        static readonly SinnohGod[] Gods = { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina };

        /// <summary>신오의 신 카드면 그 신(디아루가·펄기아·기라티나 — 어느 폼이든), 아니면 None.</summary>
        public static SinnohGod GodOf(Ctx ctx, int def)
        {
            if (def < 0 || def >= ctx.Cards.Pokemon.Count) return SinnohGod.None;
            string key = ctx.Cards.Pokemon[def].Key;
            int at = key.IndexOf(':');
            int i = System.Array.IndexOf(GodSpecies, at < 0 ? key : key.Substring(0, at));
            return i < 0 || key.IndexOf('@') >= 0 ? SinnohGod.None : Gods[i];
        }

        /// <summary>
        /// 천상의 오라(p.18 「if you do not posses their matching Celestial Artifact」): 신 배틀이고 도전한 좌석이 그 신의 천상의 유물을 가지지 않았다(가방이면 된다 — RelicRules.WardsAura).
        /// 오라면 포획 7(두 폼 모두)이고 첫 폼이 쓰러지면 둘째 폼으로.
        /// </summary>
        public static bool AuraOn(GameState s, Ctx ctx, BattleState b)
        {
            if (b == null || b.Kind != BattleKind.God || b.Sides[0].Owner < 0 || b.Sides[1].Roster.Count == 0) return false;
            var god = GodOf(ctx, s.Pokemon[b.Sides[1].Roster[0]].Def);
            return god != SinnohGod.None && !RelicRules.WardsAura(s, ctx, b.Sides[0].Owner, god);
        }

        /// <summary>오라 아래 그 신의 둘째 폼 카드(지금 폼 1이고 폼 2 카드가 있으면), 아니면 −1 — 둘째 폼은 한 번뿐이다.</summary>
        internal static int SecondForm(GameState s, Ctx ctx, BattleState b, int pokemon)
        {
            if (!AuraOn(s, ctx, b) || pokemon < 0 || b.Sides[1].Roster.IndexOf(pokemon) < 0) return -1;
            var d = ctx.Cards.Pokemon[s.Pokemon[pokemon].Def];
            if (d.State != FormRules.DefaultState) return -1;
            string key = d.Key;
            return ctx.Cards.PokemonIds.IndexOf(key.Substring(0, key.IndexOf(':')) + ":2");
        }

        /// <summary>
        /// 첫 폼이 쓰러졌다(BattleFlow.ReportFaint — 기절을 알리고 나간 뒤): 오라면 둘째 폼 카드가 되어 완전 회복한다(FormChanged, Source = Rule:CelestialAura — 배틀 전 폼을 적어 배틀이 끝나면 폼 1로).
        /// 쓰러지지 않은 포켓몬처럼 다음 포켓몬 자리에서 다시 나오고 순서를 다시 정한다(원작 커뮤니티 확인). 바꿨으면 true.
        /// </summary>
        internal static bool Ascend(GameState s, Ctx ctx, Emitter fx, BattleState b, int pokemon)
        {
            int to = SecondForm(s, ctx, b, pokemon);
            if (to < 0) return false;
            var p = s.Pokemon[pokemon];
            if (p.FormBeforeBattle < 0) p.FormBeforeBattle = p.Def;
            int before = p.Def;
            p.Def = to;
            p.Damage = 0;
            StatusRules.Clear(p);
            if (fx.On) fx.Add(new FormChanged { Source = SourceRef.Rule(RuleSource.CelestialAura), PokemonId = pokemon, Before = before, After = to, Temporary = true });
            return true;
        }

        // ── 무리 배틀(rulings A28 ② · A18) ──

        /// <summary>왜곡 게이트 이름(키 「gate.이름_main」·「gate.이름_distortion」) → 나오는 포켓몬의 지형(p.12).</summary>
        static readonly (string name, Biome biome)[] GateBiomes =
        {
            ("snowdrift", Biome.Plains), ("lilly", Biome.Mountain), ("westbreeze", Biome.Ocean), ("deepmarsh", Biome.Forest),
        };

        /// <summary>그 왜곡 게이트의 지형. 게이트가 아니면 None.</summary>
        public static Biome GateBiome(Ctx ctx, int fixture)
        {
            var f = ctx.Fixtures.Fixtures[fixture];
            if (f.Kind != FixtureKind.Gate) return Biome.None;
            foreach (var g in GateBiomes)
                if (f.Key.StartsWith("gate." + g.name + "_", System.StringComparison.Ordinal)) return g.biome;
            return Biome.None;
        }

        /// <summary>무리 배틀의 등급 순서(p.12 「a Weak Pokémon, a Moderate Pokémon and a Strong Pokémon. The Pokémon enter the battle in this same order」).</summary>
        public static readonly EncounterTier[] SwarmTiers = { EncounterTier.Weak, EncounterTier.Moderate, EncounterTier.Strong };

        /// <summary>
        /// 왜곡 게이트 찾아보기: 약한 · 적당한 · 강한 덱을 하나씩 섞고 맨 위부터 그 게이트 지형의 첫 카드(기후는 보지 않는다 — p.12가 지형만 적는다).
        /// 맞는 카드가 없으면 그 덱의 아무 카드, 덱이 비었으면 그 등급은 빠진다 [가정 plan §5 #89 ②]. 한 장도 없으면 빈 찾아보기(턴이 이어진다).
        /// </summary>
        static void SearchGate(GameState s, Ctx ctx, Emitter fx, int seat, int fixture)
        {
            var cards = DrawSwarm(s, ctx, fixture);
            int cell = ctx.Fixtures.Fixtures[fixture].Cell;
            if (cards.Count == 0)
            {
                MapActions.Mark(s, fx, seat, UsageKind.EmptySearch, cell, false);
                MapActions.Continue(s, ctx, fx, seat);
                return;
            }
            BattleFlow.StartSwarm(s, ctx, fx, seat, cards, cell);
        }

        /// <summary>왜곡 게이트에서 뽑는 무리(약한 · 적당한 · 강한 한 장씩 — 덱에서 뺀다). SearchGate와 scientist B(배틀 없는 찾아보기)가 같이 쓴다.</summary>
        internal static List<int> DrawSwarm(GameState s, Ctx ctx, int fixture)
        {
            var biome = GateBiome(ctx, fixture);
            var cards = new List<int>(SwarmTiers.Length);
            foreach (var tier in SwarmTiers)
            {
                var pile = s.Decks[(int)MapActions.DeckOf(tier)];
                Piles.Shuffle(pile, s.Rng);
                int at = pile.Count - 1;
                for (int i = pile.Count - 1; i >= 0; i--)
                    if (ctx.Cards.Pokemon[pile[i]].Biome == biome)
                    {
                        at = i;
                        break;
                    }
                if (at < 0) continue;
                cards.Add(pile[at]);
                pile.RemoveAt(at);
            }
            return cards;
        }
    }
}
