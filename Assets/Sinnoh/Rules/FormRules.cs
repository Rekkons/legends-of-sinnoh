using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 폼 바꾸기(룰북 p.6 「Certain Pokémon have multiple forms that are accessible by changing the state of their cards」, rulings A26, M5 「전설·알파·폼」). 폼 = 같은 종의 다른 카드 상태
    /// (포켓몬 표 키 「종:상태」) — 바꾸면 PokemonState.Def가 그 카드가 된다(타입·우선도·시그니처·도트가 따라간다). 받은 피해는 그대로다(원작 커뮤니티 확인).
    /// - 기술로(배틀 중 — op ChangeForm · 처리기 hangry_wheel·adaptive_cloak): 피로·순서는 그대로(p.6 「Neither battle fatigue or the current turn order is affected」), 배틀이 끝나면 배틀 전 폼으로
    ///   (「When the battle ends, the Pokémon returns to its default form (State 1)」 — 배틀 중에 바뀔 수 있는 종은 유물 폼이 없어 배틀 전 폼 = 기본 폼). 그 폼이 없는 포켓몬이 배운 기술이면
    ///   바꾸지 않는다(「If a form changing move has been taught to a Pokémon without the form it specifies, ignore the form change」) — 「그 폼」 = 그 기술을 시그니처로 가진 종의 그 상태.
    ///   폼 바꾸기 금지(imprison — ModRules.NoFormChange)면 바꾸지 않는다.
    /// - 유물로(배틀 밖 — 정비 MaintKind.ChangeForm): 「You may change to the form specified by the item whilst outside of battle」 · 「If the Pokémon is no longer holding the item, it returns to its default form」.
    ///   그라시데아 꽃 = 쉐이미, 비밀키 = 로토무(세이브 토큰 「Allows Shaymin/Rotom to change form whilst held」), 천상의 유물 = 디아루가·펄기아·기라티나(p.17 「Legendary Forms — Certain Artifacts allow
    ///   their corresponding Legendary Pokémon to access their alternate forms when held」 · 원작 커뮤니티 확인).
    /// </summary>
    public static class FormRules
    {
        /// <summary>기본 폼(p.6 「State 1」).</summary>
        public const int DefaultState = 1;

        static readonly string[] ItemKeys = { "item.gracidea_flower", "item.secret_key", "item.adamant_artifact", "item.lustrous_artifact", "item.griseous_artifact" };
        static readonly string[] ItemSpecies = { "492", "479", "483", "484", "487" };

        /// <summary>카드 키의 종 부분(「681:2」 → 「681」, 「422-e:0」 → 「422-e」).</summary>
        static string Species(Ctx ctx, int def)
        {
            string key = ctx.Cards.Pokemon[def].Key;
            int at = key.IndexOf(':');
            return at < 0 ? key : key.Substring(0, at);
        }

        /// <summary>그 종의 그 상태 카드(포켓몬 표 색인). 없으면 −1.</summary>
        static int Card(Ctx ctx, string species, int state) => ctx.Cards.PokemonIds.IndexOf(species + ":" + state.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>
        /// 기술이 정한 폼(「Change to X Form if possible」): 그 포켓몬의 종에 이 기술을 시그니처로 가진 폼이 있으면 그 종의 state 카드, 아니면 −1(그 폼이 없는 포켓몬 — 바꾸지 않는다).
        /// </summary>
        public static int MoveForm(Ctx ctx, PokemonState p, int move, int state)
        {
            if (p.Def < 0 || !OwnsMove(ctx, p, move)) return -1;
            return Card(ctx, Species(ctx, p.Def), state);
        }

        /// <summary>그 포켓몬의 종(어느 폼이든)이 이 기술을 시그니처로 가진다.</summary>
        static bool OwnsMove(Ctx ctx, PokemonState p, int move)
        {
            string species = Species(ctx, p.Def);
            var cards = ctx.Cards.Pokemon;
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].Signature == move && Species(ctx, i) == species && cards[i].Key.IndexOf('@') < 0) return true;
            return false;
        }

        /// <summary>
        /// 그 카드의 종이 폼마다 가진 시그니처(그 카드의 것 먼저, 나머지는 표 순서 · 같은 기술은 한 번) — 쓰러뜨린 포켓몬이 가르칠 수 있는 기술(p.6 「If you defeat a wild Pokémon with
        /// different signature moves on its forms, you can teach any of the signature moves」 · 신도 — 원작 커뮤니티 확인, 천상의 유물이 있어도).
        /// </summary>
        public static void Signatures(Ctx ctx, int def, List<int> into)
        {
            into.Clear();
            var cards = ctx.Cards.Pokemon;
            if (cards[def].Signature >= 0) into.Add(cards[def].Signature);
            string species = Species(ctx, def);
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].Signature >= 0 && !into.Contains(cards[i].Signature) && cards[i].Key.IndexOf('@') < 0 && Species(ctx, i) == species) into.Add(cards[i].Signature);
        }

        /// <summary>그 기술이 고를 수 있게 하는 폼들(adaptive_cloak 「change Cloak Form」 — 그 기술을 시그니처로 가진 그 종의 폼, 표 순서). 그 폼이 없는 포켓몬이면 빈 목록.</summary>
        public static void MoveForms(Ctx ctx, PokemonState p, int move, List<int> into)
        {
            into.Clear();
            if (p.Def < 0 || !OwnsMove(ctx, p, move)) return;
            string species = Species(ctx, p.Def);
            var cards = ctx.Cards.Pokemon;
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].Signature == move && Species(ctx, i) == species && cards[i].Key.IndexOf('@') < 0) into.Add(i);
        }

        /// <summary>
        /// 폼을 바꾼다(to = 포켓몬 표 색인): 같은 폼이거나 폼 바꾸기 금지(imprison)면 false. 배틀 중이면 배틀 전 폼을 적어 둔다(배틀 끝에 돌아간다). FormChanged(Source = 바꾼 출처).
        /// </summary>
        public static bool Change(GameState s, Ctx ctx, Emitter fx, int pokemon, int to, SourceRef src)
        {
            var p = s.Pokemon[pokemon];
            if (to < 0 || to == p.Def || ModRules.NoFormChange(p)) return false;
            bool battle = s.Battle != null;
            if (battle && p.FormBeforeBattle < 0) p.FormBeforeBattle = p.Def;
            int before = p.Def;
            p.Def = to;
            TrainerRules.Refresh(s, ctx); // 타입이 바뀌면 트레이너 카드의 최대 체력 보정도 바뀐다(rulings A32)
            if (fx.On) fx.Add(new FormChanged { Source = src, PokemonId = pokemon, Before = before, After = to, Temporary = battle });
            return true;
        }

        /// <summary>배틀이 끝났다: 두 쪽(좌석 = 파티, 가상 = 명단)의 포켓몬 가운데 배틀 중 폼이 바뀐 것을 배틀 전 폼으로(카드를 덱·버림 더미로 보내기 전에, BattleFlow.Conclude).</summary>
        internal static void BattleEnded(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            var seen = new List<int>(12);
            foreach (var side in b.Sides)
            {
                seen.AddRange(side.Roster);
                if (side.Owner >= 0) seen.AddRange(s.Seats[side.Owner].Party);
            }
            foreach (int id in seen)
            {
                if (id < 0) continue;
                var p = s.Pokemon[id];
                if (p.FormBeforeBattle < 0) continue;
                int before = p.Def;
                p.Def = p.FormBeforeBattle;
                p.FormBeforeBattle = -1;
                if (fx.On && before != p.Def) fx.Add(new FormChanged { Source = SourceRef.None, PokemonId = id, Before = before, After = p.Def, Temporary = false });
            }
        }

        // ── 유물 폼(배틀 밖) ──

        /// <summary>그 포켓몬의 종이 유물 폼을 가진 종이면 그 유물 키의 자리, 아니면 −1.</summary>
        static int ItemSpeciesOf(Ctx ctx, PokemonState p) => p.Def < 0 ? -1 : System.Array.IndexOf(ItemSpecies, Species(ctx, p.Def));

        /// <summary>
        /// 정비 폼 바꾸기(MaintKind.ChangeForm)로 갈 수 있는 폼인가: 유물 폼을 가진 종이고 같은 종의 카드이고, 기본 폼이거나(언제든 돌아간다) 그 유물을 지녔다.
        /// </summary>
        public static bool ItemFormAllowed(Ctx ctx, PokemonState p, int to)
        {
            if (StampRules.NobleAllowed(ctx, p, to, out bool noble)) return noble; // 수호신의 서(스탬프 보상 7, rulings A30)
            int i = ItemSpeciesOf(ctx, p);
            if (i < 0 || to < 0 || to >= ctx.Cards.Pokemon.Count || Species(ctx, to) != ItemSpecies[i] || ctx.Cards.Pokemon[to].Key.IndexOf('@') >= 0) return false;
            if (ctx.Cards.Pokemon[to].State == DefaultState) return true;
            return HeldItems.Holds(ctx, p, ItemKeys[i]);
        }

        /// <summary>
        /// 지닌물건이 바뀌었다(정비 지니게 하기 · 승리 보상으로 빼앗김): 유물 폼인데 그 유물을 더 지니지 않으면 기본 폼으로(「returns to its default form (State 1)」).
        /// </summary>
        internal static void HeldChanged(GameState s, Ctx ctx, Emitter fx, int pokemon)
        {
            if (pokemon < 0 || StampRules.NobleHeldChanged(s, ctx, fx, pokemon)) return;
            var p = s.Pokemon[pokemon];
            int i = ItemSpeciesOf(ctx, p);
            if (i < 0 || ctx.Cards.Pokemon[p.Def].State == DefaultState || ItemFormAllowed(ctx, p, p.Def)) return;
            int home = Card(ctx, ItemSpecies[i], DefaultState);
            if (home < 0) return;
            int before = p.Def;
            p.Def = home;
            if (fx.On) fx.Add(new FormChanged { Source = SourceRef.None, PokemonId = pokemon, Before = before, After = home, Temporary = false });
        }
    }
}
