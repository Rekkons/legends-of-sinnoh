using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 유물(Artifact 8 · 천상의 유물 CelestialArtifact 3 = 11종, 룰북 p.17 · 세이브 토큰 설명, rules-summary §14, M5 「숨겨진 장소·유물·트레이너전 보상」).
    /// 유물은 숨겨진 장소에서 얻어 가방(SeatState.Bag)에 들고, 지닌물건처럼 포켓몬에게 지닌다(정비 HoldItem — 배틀 밖 언제든 넣고 뺀다, 원작 커뮤니티 확인).
    /// - 공격력: 「Increase the attack strength of X and Y type moves by 1」 — **지닌 포켓몬만**(원작 커뮤니티 확인,
    ///   원작 커뮤니티 확인). <see cref="PowerMods"/>(BattleRules.RollPower가 부른다).
    /// - 전설 열쇠: 전설 장소의 전설은 그 유물을 가진 좌석만 찾아보기로 만난다(p.4 「if you are holding the matching Artifact」 · p.17, 잃지 않는다). 「가진」 = 가방 또는 파티가 지님 [가정 plan §5 #85 ①] —
    ///   <see cref="OpensSite"/>. 배틀 시작 경로(BattleKind.Legendary)는 「전설·알파·폼」 묶음이 이것을 읽는다.
    /// - 천상의 유물: 「Prevents the effects of Celestial Aura when Dialga/Palkia/Giratina is encountered」 — 지니지 않아도 가지면 된다(원작 커뮤니티 확인).
    ///   <see cref="WardsAura"/>(신 배틀은 「신·무리·우두머리」가 읽는다). 폼 바꾸기(그라시데아·비밀키·천상의 유물)는 「전설·알파·폼」.
    /// - 트레이너전 승리 보상(p.9 「You may take 1 Artifact of your choice from them」): 진 좌석의 가방 또는 포켓몬이 지닌 유물 — <see cref="Owned"/>·<see cref="Take"/>
    ///   (지닌 것도 가져간다 — 원작 커뮤니티 확인).
    /// </summary>
    public static class RelicRules
    {
        /// <summary>유물 하나가 올리는 공격력(p.17 「by 1」).</summary>
        public const int Power = 1;

        // p.17(Rulebook-en-master.txt 439–466행)·세이브 토큰 설명: 유물 → 공격력 +1 타입 둘
        static readonly string[] TypeKeys = { "item.seafarer_egg", "item.odd_keystone", "item.magma_stone", "item.secret_key", "item.gracidea_flower", "item.rockice_core", "item.icesteel_core", "item.steelrock_core" };
        static readonly PokemonType[][] TypePairs =
        {
            new[] { PokemonType.Psychic, PokemonType.Water },
            new[] { PokemonType.Dark, PokemonType.Ghost },
            new[] { PokemonType.Fire, PokemonType.Ground },
            new[] { PokemonType.Ghost, PokemonType.Electric },
            new[] { PokemonType.Grass, PokemonType.Flying },
            new[] { PokemonType.Rock, PokemonType.Ice },
            new[] { PokemonType.Ice, PokemonType.Steel },
            new[] { PokemonType.Steel, PokemonType.Rock },
        };

        // 천상의 유물 → 신(p.17 「Adamant Artifact … when Dialga is encountered」)
        static readonly string[] AuraKeys = { "item.adamant_artifact", "item.lustrous_artifact", "item.griseous_artifact" };
        static readonly SinnohGod[] AuraGods = { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina };

        static readonly PokemonType[] NoTypes = new PokemonType[0];

        /// <summary>유물(Artifact·CelestialArtifact)인가. 우두머리 토큰·배틀 아이템·신오의 심장은 아니다(원작 커뮤니티 확인).</summary>
        public static bool IsRelic(Ctx ctx, int item) =>
            item >= 0 && item < ctx.Items.Items.Count && (ctx.Items.Items[item].Kind == ItemKind.Artifact || ctx.Items.Items[item].Kind == ItemKind.CelestialArtifact);

        /// <summary>그 유물이 공격력을 올리는 타입 둘(유물이 아니거나 천상의 유물이면 빈 배열).</summary>
        public static IReadOnlyList<PokemonType> TypesOf(Ctx ctx, int item)
        {
            if (item < 0 || item >= ctx.Items.Items.Count) return NoTypes;
            int i = Array.IndexOf(TypeKeys, ctx.Items.Items[item].Key);
            return i < 0 ? NoTypes : TypePairs[i];
        }

        /// <summary>그 천상의 유물이 막는 신. 아니면 None.</summary>
        public static SinnohGod GodOf(Ctx ctx, int item)
        {
            if (item < 0 || item >= ctx.Items.Items.Count) return SinnohGod.None;
            int i = Array.IndexOf(AuraKeys, ctx.Items.Items[item].Key);
            return i < 0 ? SinnohGod.None : AuraGods[i];
        }

        /// <summary>지닌 유물의 공격력 +1(이번 공격의 타입 — 효과가 고른 타입 포함, <see cref="BattleRules.AttackType"/>). 출처 = SourceKind.Relic(유물 아이템 색인).</summary>
        public static void PowerMods(GameState s, Ctx ctx, int move, int attacker, List<Mod> into)
        {
            var a = s.Pokemon[attacker];
            if (move == BattleRules.PlanB) return;
            var type = BattleRules.AttackType(s, ctx, move);
            foreach (int relic in new[] { a.HeldItem, a.HeldItem2 }) // 지닌물건 두 칸(backpacker R — 서로 다른 유물 둘이면 하나씩, rulings A39)
            {
                if (!IsRelic(ctx, relic)) continue;
                foreach (var t in TypesOf(ctx, relic))
                    if (t == type)
                    {
                        into.Add(new Mod { Source = new SourceRef(SourceKind.Relic, relic), Delta = Power });
                        TrainerRules.RelicMod(s, ctx, attacker, into); // ruin_maniac R: 유물 보정 +1 더(rulings A32)
                        break;
                    }
            }
        }

        /// <summary>그 좌석이 가진 유물(가방 → 파티 순서로 지닌 것, 아이템 색인).</summary>
        public static List<int> Owned(GameState s, Ctx ctx, int seat)
        {
            var r = new List<int>();
            var st = s.Seats[seat];
            foreach (int item in st.Bag)
                if (IsRelic(ctx, item)) r.Add(item);
            foreach (int p in st.Party)
            {
                if (p >= 0 && IsRelic(ctx, s.Pokemon[p].HeldItem)) r.Add(s.Pokemon[p].HeldItem);
                if (p >= 0 && IsRelic(ctx, s.Pokemon[p].HeldItem2)) r.Add(s.Pokemon[p].HeldItem2);
            }
            return r;
        }

        /// <summary>그 좌석이 그 아이템을 가졌나(가방 또는 파티가 지님).</summary>
        public static bool Owns(GameState s, Ctx ctx, int seat, int item) => Owned(s, ctx, seat).Contains(item);

        /// <summary>전설 장소(고정물)의 전설을 만날 열쇠를 가졌나: 그 장소의 유물 가운데 하나를 가졌다. 유물 조건이 없는 고정물이면 false.</summary>
        public static bool OpensSite(GameState s, Ctx ctx, int seat, int fixture)
        {
            var f = ctx.Fixtures.Fixtures[fixture];
            if (f.Kind != FixtureKind.LegendarySite) return false;
            foreach (int item in f.Artifact)
                if (Owns(s, ctx, seat, item)) return true;
            return false;
        }

        /// <summary>그 신의 천상의 오라를 막는 천상의 유물을 가졌나(지니지 않아도 된다).</summary>
        public static bool WardsAura(GameState s, Ctx ctx, int seat, SinnohGod god)
        {
            if (god == SinnohGod.None) return false;
            foreach (int item in Owned(s, ctx, seat))
                if (GodOf(ctx, item) == god) return true;
            return false;
        }

        /// <summary>
        /// 유물을 진 좌석에서 이긴 좌석의 가방으로(가방에 있으면 가방에서, 아니면 지닌 포켓몬에게서 — 파티 순서 첫 포켓몬). 지녔던 포켓몬을 돌려준다(가방에서면 −1 —
        /// 유물 폼이면 부르는 쪽이 FormRules.HeldChanged로 기본 폼으로 돌린다). 없으면 InvalidOperationException.
        /// </summary>
        public static int Take(GameState s, Ctx ctx, int from, int to, int item)
        {
            if (!IsRelic(ctx, item)) throw new InvalidOperationException($"아이템 {item}은 유물이 아니다");
            var st = s.Seats[from];
            int holder = -1;
            if (!st.Bag.Remove(item))
            {
                foreach (int p in st.Party)
                    if (p >= 0 && PokemonRules.HoldsItem(s.Pokemon[p], item)) { holder = p; break; }
                if (holder < 0) throw new InvalidOperationException($"좌석 {from}에게 유물 {item}이 없다");
                PokemonRules.DropItem(s.Pokemon[holder], item);
            }
            s.Seats[to].Bag.Add(item);
            return holder;
        }
    }
}
