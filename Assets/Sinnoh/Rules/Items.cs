using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 아이템 종류(items.json kind, 03 §1-3). M2 뼈대는 세이브에 실물이 있는 것만 든다:
    /// 숨겨진 장소 토큰(HiddenSpot 35장)과 유물 토큰(AlphaPokemon·Artifact·CelestialArtifact 17장) — 셋업 때 야생 칸에 뒤집어 놓는 두 묶음 —,
    /// 소지 아이템(Held), 볼(Ball). M5 「카드·아이템 데이터」에서 스튜(Stew 4)·여행 스탬프(Stamp 7 — 스탬프 시트 칸, 키 stamp.&lt;n&gt;)와
    /// 영문 효과 문구·레시피를 더했다(Tools/data/authored/items.json). 배틀 아이템 = 레시피가 있는 Held 6개(종류를 따로 두지 않는다).
    /// M6 「해금 뼈대」: 트레이너 카드 전용 아이템(TrainerItem 11 — 원작 가방 save 22ec55, rulings G1 · A20-6. 드래곤의 정점·대타출동은 원작 태그가 지닌물건이지만
    /// 제작·지니게 하기 목록(Held)에 섞이지 않게 이 종류로 둔다 — 붙이는 길은 그 능력 묶음이 만든다).
    /// </summary>
    public enum ItemKind { HiddenSpot, AlphaPokemon, Artifact, CelestialArtifact, Held, Ball, Stew, Stamp, TrainerItem }

    /// <summary>아이템 정의 하나(items.json 행). 키는 "item.&lt;영문 snake&gt;", 숨겨진 장소는 "hidden_spot.&lt;영문 snake&gt;"(03 §1-2).</summary>
    public sealed class ItemDef
    {
        public readonly string Key;
        public readonly ItemKind Kind;
        /// <summary>세이브 속 개수(셋업 토큰은 묶음 안 장수). 무한 가방에서 꺼내 쓰는 것(제작 소지 아이템·볼)은 null.</summary>
        public readonly int? Copies;
        /// <summary>영문 효과 문구(룰북 — 효과의 정본, 볼·스튜·배틀 아이템)·스탬프 조건. 아직 옮기지 않은 행은 null.</summary>
        public readonly string En;
        /// <summary>제작 재료 규토리(레시피 그림 왼쪽부터). 제작하지 않는 것은 null.</summary>
        public readonly IReadOnlyList<ApricornColor> Recipe;

        public ItemDef(string key, ItemKind kind, int? copies, string en = null, IReadOnlyList<ApricornColor> recipe = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            if (copies.HasValue && copies.Value < 1) throw new ArgumentOutOfRangeException(nameof(copies), copies, $"{key}: 개수는 1 이상");
            if (!copies.HasValue && kind != ItemKind.Held && kind != ItemKind.Ball && kind != ItemKind.Stew && kind != ItemKind.Stamp && kind != ItemKind.TrainerItem) throw new ArgumentException($"{key}: 셋업 토큰({kind})은 개수가 있어야 한다", nameof(copies));
            if (en != null && en.Length == 0) throw new ArgumentException($"{key}: 빈 영문 문구(없으면 null)", nameof(en));
            if (recipe != null)
            {
                if (recipe.Count == 0) throw new ArgumentException($"{key}: 빈 레시피(없으면 null)", nameof(recipe));
                if (en == null) throw new ArgumentException($"{key}: 레시피가 있는데 영문 효과가 없다", nameof(en));
                var a = new ApricornColor[recipe.Count];
                for (int i = 0; i < a.Length; i++) a[i] = recipe[i];
                recipe = a;
            }
            Kind = kind;
            Copies = copies;
            En = en;
            Recipe = recipe;
        }
    }

    /// <summary>아이템 표(정적 데이터). 읽기는 Sinnoh.Data ItemDbLoader.</summary>
    public sealed class ItemDb
    {
        public readonly IReadOnlyList<ItemDef> Items;
        public readonly IdTable Ids;

        public ItemDb(IReadOnlyList<ItemDef> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var a = new ItemDef[items.Count];
            var keys = new string[items.Count];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = items[i] ?? throw new ArgumentException($"아이템 행 {i}이 null이다", nameof(items));
                keys[i] = a[i].Key;
            }
            Items = a;
            Ids = new IdTable("items", keys);
        }
    }
}
