using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Sinnoh.Rules;

namespace Sinnoh.Data
{
    /// <summary>
    /// items.json(build.mjs가 세이브에서 뽑은 행 + authored/items.json의 영문 효과·레시피·스튜·스탬프, 03 §1-3) → ItemDb, 그리고 그 반대(Write). 경로는 받지 않는다(01 §11).
    /// 엄격하다: 빠진·모르는 필드, 모르는 kind, 키 형식(숨겨진 장소 hidden_spot., 스탬프 stamp., 나머지 item.), 빈 문구·빈 레시피·모르는 규토리 색, 중복 키는 FormatException.
    /// Write는 build.mjs와 같은 바이트를 쓴다(plan M2 조건 3).
    /// </summary>
    public static class ItemDbLoader
    {
        const string File = "items.json";

        public static ItemDb Load(Stream items) => Load(TableJson.ReadAll(items, nameof(items)));

        public static ItemDb Load(string itemsJson)
        {
            var rows = TableJson.Parse<ItemRow>(itemsJson, File);
            var defs = new ItemDef[rows.Count];
            for (int i = 0; i < defs.Length; i++)
            {
                var r = rows[i];
                var kind = TableJson.ParseEnum<ItemKind>(r.Kind, $"{File} 행 {i}");
                TableJson.CheckKey(r.Key, kind == ItemKind.HiddenSpot ? "hidden_spot" : kind == ItemKind.Stamp ? "stamp" : "item", File, i);
                ApricornColor[] recipe = null;
                if (r.Recipe != null)
                {
                    recipe = new ApricornColor[r.Recipe.Count];
                    for (int k = 0; k < recipe.Length; k++) recipe[k] = TableJson.ParseEnum<ApricornColor>(r.Recipe[k], $"{File} 행 {i}");
                }
                defs[i] = TableJson.Wrap(File, () => new ItemDef(r.Key, kind, r.Copies, r.En, recipe));
            }
            return TableJson.Wrap(File, () => new ItemDb(defs));
        }

        public static string Write(ItemDb db) => TableJson.WriteTable(db.Items, (w, it) =>
        {
            w.WriteStartObject();
            TableJson.Prop(w, "key", it.Key);
            TableJson.Prop(w, "kind", it.Kind.ToString());
            TableJson.Prop(w, "copies", it.Copies);
            TableJson.Prop(w, "en", it.En);
            w.WritePropertyName("recipe");
            if (it.Recipe == null) w.WriteNull();
            else
            {
                w.WriteStartArray();
                foreach (var c in it.Recipe) w.WriteValue(c.ToString());
                w.WriteEndArray();
            }
            w.WriteEndObject();
        });

#pragma warning disable 0649 // Newtonsoft가 반사로 채운다
        // 필드 이름은 build.mjs(lib/save.mjs buildItems + lib/authored.mjs mergeItems) 출력과 같다
        [JsonObject(ItemRequired = Required.Always)]
        sealed class ItemRow
        {
            public string Key;
            public string Kind;
            [JsonProperty(Required = Required.AllowNull)] public int? Copies;
            [JsonProperty(Required = Required.AllowNull)] public string En;
            [JsonProperty(Required = Required.AllowNull)] public List<string> Recipe;
        }
#pragma warning restore 0649
    }
}
