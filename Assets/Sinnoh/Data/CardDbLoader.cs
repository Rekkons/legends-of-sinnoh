using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Sinnoh.Rules;

namespace Sinnoh.Data
{
    /// <summary>
    /// pokemon·moves·trainer_cards.json(Tools/data/build.mjs가 xlsx에서 만듦, 03 §1-3) → CardDb, 그리고 그 반대(Write*).
    /// 경로는 받지 않는다(01 §11). 문자열 키는 IdTable로 조밀한 int 색인이 된다(진화 대상·시그니처도 색인으로 바뀐다).
    /// 엄격하다(TableJson.Parse): 빠진 필드·모르는 필드·같은 필드 두 번·형이 다른 값·모르는 enum 값·키 형식·없는 키 참조·중복 키는 모두
    /// FormatException(어느 파일인지 붙음).
    /// Write*는 build.mjs와 같은 바이트를 쓴다(행 하나 = 한 줄, 필드 순서 고정) — 읽고 다시 쓰면 같은 파일이다(plan M2 조건 3).
    /// </summary>
    public static class CardDbLoader
    {
        public static CardDb Load(Stream pokemon, Stream moves, Stream trainerCards) =>
            Load(TableJson.ReadAll(pokemon, nameof(pokemon)), TableJson.ReadAll(moves, nameof(moves)), TableJson.ReadAll(trainerCards, nameof(trainerCards)));

        public static CardDb Load(string pokemonJson, string movesJson, string trainerCardsJson)
        {
            var moveRows = TableJson.Parse<MoveRow>(movesJson, "moves.json");
            var pokemonRows = TableJson.Parse<PokemonRow>(pokemonJson, "pokemon.json");
            var trainerRows = TableJson.Parse<TrainerCardRow>(trainerCardsJson, "trainer_cards.json");

            var moves = TableJson.Wrap("moves.json", () => ToMoves(moveRows));
            var moveIds = TableJson.Wrap("moves.json", () => new IdTable("moves", KeysOf(moveRows, r => r.Key)));
            var pokemon = TableJson.Wrap("pokemon.json", () => ToPokemon(pokemonRows, moveIds));
            var trainerCards = TableJson.Wrap("trainer_cards.json", () => ToTrainerCards(trainerRows));
            return TableJson.Wrap("CardDb", () => new CardDb(pokemon, moves, trainerCards));
        }

        // ── 읽기 ──

        static readonly Regex PokemonKey = new Regex(@"^([0-9]+)(-[a-z]+)?:([0-9]+)(@([a-z]+(?:_[a-z]+)*))?$", RegexOptions.CultureInvariant);
        static readonly Regex SnakeKey = new Regex(@"^[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant);

        static MoveDef[] ToMoves(List<MoveRow> rows)
        {
            var defs = new MoveDef[rows.Count];
            for (int i = 0; i < defs.Length; i++)
            {
                var r = rows[i];
                if (!SnakeKey.IsMatch(r.Key)) throw new FormatException($"moves.json 행 {i}: 키 \"{r.Key}\" 형식");
                defs[i] = new MoveDef(r.Key, r.Name, ParseEnum<PokemonType>(r.Type, r.Key, false), r.Power, r.AttackStrength, r.MaxTotal, r.En);
            }
            return defs;
        }

        static PokemonDef[] ToPokemon(List<PokemonRow> rows, IdTable moveIds)
        {
            // 진화 대상은 뒤 행을 가리킬 수 있어 키 표를 먼저 만든다
            var ids = new IdTable("pokemon", KeysOf(rows, r => r.Key));
            var defs = new PokemonDef[rows.Count];
            for (int i = 0; i < defs.Length; i++)
            {
                var r = rows[i];
                var trainer = ParseEnum<GalacticTrainer>(r.Trainer, r.Key, true);
                var m = PokemonKey.Match(r.Key);
                if (!m.Success || m.Groups[1].Value != TableJson.Num(r.Dex) || m.Groups[3].Value != TableJson.Num(r.State)
                    || (m.Groups[5].Success ? m.Groups[5].Value : null) != (trainer == GalacticTrainer.None ? null : TableJson.Snake(trainer.ToString())))
                    throw new FormatException($"pokemon.json 행 {i}: 키 \"{r.Key}\"가 dex {r.Dex}·state {r.State}·trainer {r.Trainer}와 맞지 않다");
                // 접미사는 같은 <dex>:<state>[@trainer] 키가 있어 겹칠 때만 남긴다(03 §1-2 [가정 M2-a], 동쪽 바다 422-e:0·423-e:0)
                if (m.Groups[2].Success && !ids.TryIndexOf(r.Key.Remove(m.Groups[2].Index, m.Groups[2].Length), out _))
                    throw new FormatException($"pokemon.json 행 {i}: 키 \"{r.Key}\"의 접미사 {m.Groups[2].Value}는 겹치는 키가 없으면 쓰지 않는다");

                var evolveInto = new int[r.EvolveInto.Count];
                for (int k = 0; k < evolveInto.Length; k++)
                    if (!ids.TryIndexOf(r.EvolveInto[k], out evolveInto[k])) throw new FormatException($"pokemon.json {r.Key}: 진화 대상 \"{r.EvolveInto[k]}\"가 없다");
                if (!moveIds.TryIndexOf(r.Signature, out int signature)) throw new FormatException($"pokemon.json {r.Key}: 시그니처 기술 \"{r.Signature}\"가 moves에 없다");

                defs[i] = new PokemonDef(r.Key, r.Dex, r.State, r.Name, ParseTypes(r.Types, r.Key), r.Health, r.Initiative,
                    ParseEnum<Biome>(r.Biome, r.Key, true), ParseEnum<Climate>(r.Climate, r.Key, true), ParseTypes(r.LearnTypes, r.Key),
                    evolveInto, r.EvolveCost, signature, ParseEnum<EncounterTier>(r.Tier, r.Key, false), trainer, r.Copies,
                    r.Uncapturable, r.Legendary, r.Starter);
            }
            return defs;
        }

        static TrainerCardDef[] ToTrainerCards(List<TrainerCardRow> rows)
        {
            var defs = new TrainerCardDef[rows.Count];
            for (int i = 0; i < defs.Length; i++)
            {
                var r = rows[i];
                if (!SnakeKey.IsMatch(r.Key)) throw new FormatException($"trainer_cards.json 행 {i}: 키 \"{r.Key}\" 형식");
                var abilities = new TrainerAbility[r.Abilities.Count];
                for (int k = 0; k < abilities.Length; k++)
                {
                    var a = r.Abilities[k] ?? throw new FormatException($"trainer_cards.json {r.Key}: 능력 {k}이 null이다");
                    abilities[k] = new TrainerAbility(a.Slot, ParseEnum<AbilityColor>(a.Color, r.Key, false), a.Name, a.En);
                }
                defs[i] = new TrainerCardDef(r.Key, r.Class, abilities);
            }
            return defs;
        }

        static PokemonType[] ParseTypes(List<string> values, string key)
        {
            var types = new PokemonType[values.Count];
            for (int i = 0; i < types.Length; i++) types[i] = ParseEnum<PokemonType>(values[i], key, false);
            return types;
        }

        /// <summary>enum 이름과 정확히 같아야 한다(숫자·쉼표 조합 거부). nullIsNone이면 null = 0번 값이고 그 이름 문자열은 거부한다.</summary>
        static T ParseEnum<T>(string value, string key, bool nullIsNone) where T : struct, Enum
        {
            if (value == null)
            {
                if (nullIsNone) return default;
                throw new FormatException($"{key}: {typeof(T).Name} 값이 null이다");
            }
            if (Enum.TryParse(value, false, out T parsed) && parsed.ToString() == value && !(nullIsNone && parsed.Equals(default(T))))
                return parsed;
            throw new FormatException($"{key}: 모르는 {typeof(T).Name} 값 \"{value}\"");
        }

        static string[] KeysOf<T>(List<T> rows, Func<T, string> key)
        {
            var keys = new string[rows.Count];
            for (int i = 0; i < keys.Length; i++) keys[i] = key(rows[i]);
            return keys;
        }

        // ── 쓰기(build.mjs와 같은 바이트) ──

        public static string WritePokemon(CardDb db) => TableJson.WriteTable(db.Pokemon, (w, p) =>
        {
            w.WriteStartObject();
            TableJson.Prop(w, "key", p.Key);
            TableJson.Prop(w, "dex", p.Dex);
            TableJson.Prop(w, "state", p.State);
            TableJson.Prop(w, "name", p.Name);
            w.WritePropertyName("types");
            WriteTypes(w, p.Types);
            TableJson.Prop(w, "health", p.Health);
            TableJson.Prop(w, "initiative", p.Initiative);
            TableJson.Prop(w, "biome", p.Biome == Biome.None ? null : p.Biome.ToString());
            TableJson.Prop(w, "climate", p.Climate == Climate.None ? null : p.Climate.ToString());
            w.WritePropertyName("learnTypes");
            WriteTypes(w, p.LearnTypes);
            w.WritePropertyName("evolveInto");
            w.WriteStartArray();
            foreach (int e in p.EvolveInto) w.WriteValue(db.PokemonIds[e]);
            w.WriteEndArray();
            TableJson.Prop(w, "evolveCost", p.EvolveCost);
            TableJson.Prop(w, "signature", db.MoveIds[p.Signature]);
            TableJson.Prop(w, "tier", p.Tier.ToString());
            TableJson.Prop(w, "trainer", p.Trainer == GalacticTrainer.None ? null : p.Trainer.ToString());
            TableJson.Prop(w, "copies", p.Copies);
            TableJson.Prop(w, "uncapturable", p.Uncapturable);
            TableJson.Prop(w, "legendary", p.Legendary);
            TableJson.Prop(w, "starter", p.Starter);
            w.WriteEndObject();
        });

        public static string WriteMoves(CardDb db) => TableJson.WriteTable(db.Moves, (w, m) =>
        {
            w.WriteStartObject();
            TableJson.Prop(w, "key", m.Key);
            TableJson.Prop(w, "name", m.Name);
            TableJson.Prop(w, "type", m.Type.ToString());
            TableJson.Prop(w, "power", m.Power);
            TableJson.Prop(w, "attackStrength", m.AttackStrength);
            TableJson.Prop(w, "maxTotal", m.MaxTotal);
            TableJson.Prop(w, "en", m.En);
            w.WriteEndObject();
        });

        public static string WriteTrainerCards(CardDb db) => TableJson.WriteTable(db.TrainerCards, (w, c) =>
        {
            w.WriteStartObject();
            TableJson.Prop(w, "key", c.Key);
            TableJson.Prop(w, "class", c.Class);
            w.WritePropertyName("abilities");
            w.WriteStartArray();
            foreach (var a in c.Abilities)
            {
                w.WriteStartObject();
                TableJson.Prop(w, "slot", a.Slot);
                TableJson.Prop(w, "color", a.Color.ToString());
                TableJson.Prop(w, "name", a.Name);
                TableJson.Prop(w, "en", a.En);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });

        static void WriteTypes(JsonWriter w, IReadOnlyList<PokemonType> types)
        {
            w.WriteStartArray();
            foreach (var t in types) w.WriteValue(t.ToString());
            w.WriteEndArray();
        }

#pragma warning disable 0649 // Newtonsoft가 반사로 채운다
        // 필드 이름은 build.mjs(lib/cube.mjs) 출력과 같다
        [JsonObject(ItemRequired = Required.Always)]
        sealed class PokemonRow
        {
            public string Key;
            public int Dex;
            public int State;
            public string Name;
            public List<string> Types;
            public int Health;
            public int Initiative;
            [JsonProperty(Required = Required.AllowNull)] public string Biome;
            [JsonProperty(Required = Required.AllowNull)] public string Climate;
            public List<string> LearnTypes;
            public List<string> EvolveInto;
            [JsonProperty(Required = Required.AllowNull)] public int? EvolveCost;
            public string Signature;
            public string Tier;
            [JsonProperty(Required = Required.AllowNull)] public string Trainer;
            public int Copies;
            public bool Uncapturable;
            public bool Legendary;
            public bool Starter;
        }

        [JsonObject(ItemRequired = Required.Always)]
        sealed class MoveRow
        {
            public string Key;
            public string Name;
            public string Type;
            [JsonProperty(Required = Required.AllowNull)] public int? Power;
            [JsonProperty(Required = Required.AllowNull)] public int? AttackStrength;
            public int MaxTotal;
            public string En;
        }

        [JsonObject(ItemRequired = Required.Always)]
        sealed class TrainerCardRow
        {
            public string Key;
            public string Class;
            public List<AbilityRow> Abilities;
        }

        [JsonObject(ItemRequired = Required.Always)]
        sealed class AbilityRow
        {
            public int Slot;
            public string Color;
            public string Name;
            public string En;
        }
#pragma warning restore 0649
    }
}
