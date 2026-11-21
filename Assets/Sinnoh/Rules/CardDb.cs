using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>조우 등급(xlsx pokemon.encounter_tier). Grunt·Commander·Boss는 갤럭시단 카드다.</summary>
    public enum EncounterTier { Starter, Weak, Moderate, Strong, Legendary, UltraBeast, UltraBurst, Grunt, Commander, Boss }

    /// <summary>갤럭시단 카드의 주인(xlsx pokemon.trainer). 야생 카드는 None.</summary>
    public enum GalacticTrainer { None, FemaleGrunt, MaleGrunt, Jupiter, Mars, Saturn, Cyrus }

    /// <summary>트레이너 카드 능력 색. 파랑 = 실버(상태 2)에서, 빨강 = 골드(상태 3)에서 해금(trainer-cards.md §1).</summary>
    public enum AbilityColor { Blue, Red }

    /// <summary>
    /// 포켓몬 카드 정의 하나(xlsx pokemon 행, 01 §11). 키는 "&lt;dex&gt;:&lt;state&gt;"이고, 갤럭시단 카드는 "@&lt;trainer&gt;",
    /// 동쪽 바다 폼처럼 state가 같은 카드는 xlsx 접미사("422-e:0")가 붙는다(03 §1-2). 판별은 키·필드로 하고 Name으로 하지 않는다(B2).
    /// </summary>
    public sealed class PokemonDef
    {
        public readonly string Key;
        public readonly int Dex;
        /// <summary>TTS 카드 상태. 0 = 상태 없는 카드, 1 = 여러 상태 카드의 첫 면, 2 이상 = 그 카드의 다른 면(폼).</summary>
        public readonly int State;
        /// <summary>영문 이름(xlsx internal_name). 화면 이름은 문자열 표가 정한다.</summary>
        public readonly string Name;
        /// <summary>타입 1~2개.</summary>
        public readonly IReadOnlyList<PokemonType> Types;
        public readonly int Health;
        public readonly int Initiative;
        public readonly Biome Biome;
        public readonly Climate Climate;
        /// <summary>배울 수 있는 기술 타입 0~4개(xlsx move_1~4).</summary>
        public readonly IReadOnlyList<PokemonType> LearnTypes;
        /// <summary>진화 대상 포켓몬 색인(R14: 여럿이면 분기). 없으면 빈 목록.</summary>
        public readonly IReadOnlyList<int> EvolveInto;
        /// <summary>진화 비용. 진화가 없으면 null.</summary>
        public readonly int? EvolveCost;
        /// <summary>시그니처 기술의 기술 색인.</summary>
        public readonly int Signature;
        public readonly EncounterTier Tier;
        public readonly GalacticTrainer Trainer;
        /// <summary>덱에 든 장수(xlsx number_in_deck). 상태 2 이상 행은 첫 면 카드와 같은 값이다.</summary>
        public readonly int Copies;
        public readonly bool Uncapturable;
        public readonly bool Legendary;
        public readonly bool Starter;

        public PokemonDef(string key, int dex, int state, string name, IReadOnlyList<PokemonType> types, int health, int initiative,
            Biome biome, Climate climate, IReadOnlyList<PokemonType> learnTypes, IReadOnlyList<int> evolveInto, int? evolveCost,
            int signature, EncounterTier tier, GalacticTrainer trainer, int copies, bool uncapturable, bool legendary, bool starter)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Types = types ?? throw new ArgumentNullException(nameof(types));
            LearnTypes = learnTypes ?? throw new ArgumentNullException(nameof(learnTypes));
            EvolveInto = evolveInto ?? throw new ArgumentNullException(nameof(evolveInto));
            if (types.Count < 1 || types.Count > 2) throw new ArgumentException($"{key}: 타입 {types.Count}개", nameof(types));
            if (learnTypes.Count > 4) throw new ArgumentException($"{key}: 배울 타입 {learnTypes.Count}개", nameof(learnTypes));
            if ((evolveInto.Count > 0) != evolveCost.HasValue) throw new ArgumentException($"{key}: 진화 대상과 비용 중 하나만 있다", nameof(evolveCost));
            if (copies < 1) throw new ArgumentOutOfRangeException(nameof(copies), copies, $"{key}: 장수는 1 이상");
            if (health < 1) throw new ArgumentOutOfRangeException(nameof(health), health, $"{key}: 체력은 1 이상");
            Dex = dex;
            State = state;
            Health = health;
            Initiative = initiative;
            Biome = biome;
            Climate = climate;
            EvolveCost = evolveCost;
            Signature = signature;
            Tier = tier;
            Trainer = trainer;
            Copies = copies;
            Uncapturable = uncapturable;
            Legendary = legendary;
            Starter = starter;
        }
    }

    /// <summary>기술 정의 하나(xlsx moves 행). 키 = 영문 이름 snake_case(03 §1-2).</summary>
    public sealed class MoveDef
    {
        public readonly string Key;
        /// <summary>영문 이름(xlsx move_name).</summary>
        public readonly string Name;
        public readonly PokemonType Type;
        /// <summary>원작 게임 위력(xlsx move_power). 카드에 「?」면 null.</summary>
        public readonly int? Power;
        /// <summary>공격력 = 공격 주사위 수. 카드에 「?」(효과가 정함)면 null.</summary>
        public readonly int? AttackStrength;
        /// <summary>xlsx max_total.</summary>
        public readonly int MaxTotal;
        /// <summary>영문 효과 문구(xlsx move_effect, 앞뒤 공백 뺌). 효과 정의(EffectDef)는 M5.</summary>
        public readonly string En;

        public MoveDef(string key, string name, PokemonType type, int? power, int? attackStrength, int maxTotal, string en)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            En = en ?? throw new ArgumentNullException(nameof(en));
            Type = type;
            Power = power;
            AttackStrength = attackStrength;
            MaxTotal = maxTotal;
        }
    }

    /// <summary>트레이너 카드 능력 하나. 키는 "&lt;카드 키&gt;.&lt;slot&gt;"(03 §1-2).</summary>
    public sealed class TrainerAbility
    {
        /// <summary>1 = 파랑, 2 = 빨강.</summary>
        public readonly int Slot;
        public readonly AbilityColor Color;
        public readonly string Name;
        /// <summary>영문 문구(xlsx ability_n_description). 정본이다(A20-9). 효과 정의는 M6.</summary>
        public readonly string En;

        public TrainerAbility(int slot, AbilityColor color, string name, string en)
        {
            if (slot != 1 && slot != 2) throw new ArgumentOutOfRangeException(nameof(slot), slot, "능력 칸은 1·2");
            if (color != (slot == 1 ? AbilityColor.Blue : AbilityColor.Red)) throw new ArgumentException($"능력 {slot}의 색이 {color}", nameof(color));
            Slot = slot;
            Color = color;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            En = en ?? throw new ArgumentNullException(nameof(en));
        }
    }

    /// <summary>트레이너 카드 정의 하나(xlsx trainer_cards 행). 키 = trainer_class snake_case.</summary>
    public sealed class TrainerCardDef
    {
        public readonly string Key;
        /// <summary>영문 직업 이름(xlsx trainer_class).</summary>
        public readonly string Class;
        /// <summary>능력 2개: [0] = 파랑(slot 1), [1] = 빨강(slot 2).</summary>
        public readonly IReadOnlyList<TrainerAbility> Abilities;

        public TrainerCardDef(string key, string cls, IReadOnlyList<TrainerAbility> abilities)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Class = cls ?? throw new ArgumentNullException(nameof(cls));
            Abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
            if (abilities.Count != 2 || abilities[0]?.Slot != 1 || abilities[1]?.Slot != 2)
                throw new ArgumentException($"{key}: 능력은 slot 1·2 순서로 2개", nameof(abilities));
        }
    }

    /// <summary>
    /// 정적 카드 데이터(01 §2-1). 표마다 정의 배열과 IdTable(문자열 키 ↔ 색인)을 같이 든다. 엔진은 색인만 쓴다.
    /// 읽기는 Sinnoh.Data CardDbLoader(생성 JSON → CardDb). 덱·카드·아이템·효과는 뒤 마일스톤에서 더한다.
    /// </summary>
    public sealed class CardDb
    {
        public readonly IReadOnlyList<PokemonDef> Pokemon;
        public readonly IReadOnlyList<MoveDef> Moves;
        public readonly IReadOnlyList<TrainerCardDef> TrainerCards;
        public readonly IdTable PokemonIds;
        public readonly IdTable MoveIds;
        public readonly IdTable TrainerCardIds;

        /// <summary>색인 참조(진화 대상·시그니처)가 범위 안인지, 키가 겹치지 않는지 확인한다.</summary>
        public CardDb(IReadOnlyList<PokemonDef> pokemon, IReadOnlyList<MoveDef> moves, IReadOnlyList<TrainerCardDef> trainerCards)
        {
            Pokemon = Copy(pokemon, nameof(pokemon));
            Moves = Copy(moves, nameof(moves));
            TrainerCards = Copy(trainerCards, nameof(trainerCards));
            PokemonIds = new IdTable("pokemon", Keys(Pokemon, p => p.Key));
            MoveIds = new IdTable("moves", Keys(Moves, m => m.Key));
            TrainerCardIds = new IdTable("trainer_cards", Keys(TrainerCards, c => c.Key));
            foreach (var p in Pokemon)
            {
                if ((uint)p.Signature >= (uint)Moves.Count) throw new ArgumentException($"{p.Key}: 시그니처 색인 {p.Signature}이 기술 표 밖");
                foreach (int e in p.EvolveInto)
                    if ((uint)e >= (uint)Pokemon.Count) throw new ArgumentException($"{p.Key}: 진화 대상 색인 {e}이 포켓몬 표 밖");
            }
        }

        static T[] Copy<T>(IReadOnlyList<T> rows, string what) where T : class
        {
            if (rows == null) throw new ArgumentNullException(what);
            var a = new T[rows.Count];
            for (int i = 0; i < a.Length; i++) a[i] = rows[i] ?? throw new ArgumentException($"{what} 행 {i}이 null이다", what);
            return a;
        }

        static string[] Keys<T>(IReadOnlyList<T> rows, Func<T, string> key)
        {
            var k = new string[rows.Count];
            for (int i = 0; i < k.Length; i++) k[i] = key(rows[i]);
            return k;
        }
    }
}
