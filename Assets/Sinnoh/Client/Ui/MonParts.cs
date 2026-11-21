using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 포켓몬·기술을 그리는 작은 부품(04 §10-2·§10-3): 기술 가르치기를 묻는 곳들(배틀 뒤 선택 — <see cref="BattleScreen"/>의 결과 창, 정비 판 — <see cref="PartyScreen"/>, 카드 확대 — <see cref="MonScreen"/>)이 같은 줄을 쓴다.
    /// 값은 카드 그대로다(위력 = 기술의 공격력 — 지닌물건 보정은 넣지 않는다, 배틀 기술 줄만 넣는다).
    /// </summary>
    public static class MonParts
    {
        /// <summary>기술 한 줄: 타입 아이콘 · (★) 이름 · 「위력」 · 값. 마우스를 올리면 효과 문구(<paramref name="tip"/> — null이면 걸지 않는다).</summary>
        public static VisualElement MoveRow(UiStage stage, Ctx ctx, int move, bool star = false, TipSide? tip = TipSide.Below, VisualElement clear = null)
        {
            var inv = CultureInfo.InvariantCulture;
            string key = BattleFeed.MoveNameKey(ctx, move);
            var row = new VisualElement();
            row.AddToClassList("sn-enc-move");
            TypeIcon(row, BattleRules.MoveType(ctx, move));
            var name = Kit.Text((star ? Star : "") + stage.T(key), Kit.F2);
            name.AddToClassList("sn-enc-move-name");
            row.Add(name);
            var power = Kit.Text(stage.T("screen.battle.power"), Kit.F2);
            power.AddToClassList("kit-muted");
            row.Add(power);
            var def = move >= 0 && move < ctx.Cards.Moves.Count ? ctx.Cards.Moves[move] : null;
            row.Add(Kit.Text(def != null && def.AttackStrength.HasValue ? def.AttackStrength.Value.ToString(inv) : "?", Kit.F2));
            if (tip.HasValue) stage.Tips.Hook(row, stage.T(key), stage.TOrNull(key + ".text"), tip.Value, reading: true, clear: clear);
            return row;
        }

        /// <summary>
        /// 왜곡 균열 카드가 막은 기술 칸 한 줄(rulings A52 ① — M6 그림 에셋): 카드 그림(Gen <see cref="GenAssets.BreachArt"/>) · 카드 이름. 마우스를 올리면 카드 효과와 「막힌 칸」 설명(<paramref name="tip"/> — null이면 걸지 않는다).
        /// </summary>
        public static VisualElement BreachRow(UiStage stage, Ctx ctx, int card, TipSide? tip = TipSide.Below)
        {
            string key = card >= 0 && card < ctx.Decks.CardIds.Count ? ctx.Decks.CardIds[card] : null;
            var row = new VisualElement();
            row.AddToClassList("sn-breach-slot");
            var art = new VisualElement();
            art.AddToClassList("sn-breach-art");
            const string head = "distortion_breach.";
            var tex = key != null && key.StartsWith(head, System.StringComparison.Ordinal) ? GenAssets.Load<Texture2D>(GenAssets.BreachArt + key.Substring(head.Length) + ".png") : null;
            if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
            row.Add(art);
            var name = Kit.Text(key != null ? stage.T(key) : "?", Kit.F2);
            name.AddToClassList("sn-enc-move-name");
            row.Add(name);
            if (tip.HasValue && key != null) stage.Tips.Hook(row, stage.T(key), BreachText(stage, key) + "\n" + stage.T("screen.party.slot.breach.tip"), tip.Value, reading: true);
            return row;
        }

        /// <summary>균열 카드의 효과 문구(카드 둘째 줄 — 첫째 줄 「반드시 붙인다」는 모든 카드가 같다).</summary>
        public static string BreachText(UiStage stage, string key) => stage.TOrNull(key + ".text.2") ?? "";

        /// <summary>고유 기술 표시(기호라 문자열 표에 두지 않는다 — 공개 정보 카드와 같다).</summary>
        public const string Star = "★";

        /// <summary>타입 아이콘 하나(그림 키 = <see cref="IconArt.Type(PokemonType)"/>). 그림이 없으면 자리만.</summary>
        public static VisualElement TypeIcon(VisualElement parent, PokemonType type)
        {
            var icon = new VisualElement();
            icon.AddToClassList("sn-type-icon");
            var tex = GenAssets.Load<Texture2D>(IconArt.Type(type));
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            parent.Add(icon);
            return icon;
        }

        /// <summary>
        /// 늘 걸려 있는 효과(패시브 — 플레이 시험 T27) 칩: 우두머리 토큰(<see cref="PokemonState.Alpha"/> — 체력 +4 · 전설 취급, rulings A17) · 비스트 부스트(배운 기술이 하나 이하인 전설 —
        /// <see cref="LegendRules.HasBeastBoost"/>, rulings A26). 마우스 오버 = 효과 글. small = 작은 글(정비 판 카드). alpha = 우두머리 칩도 붙인다(배틀 기둥은 등급 배지가 이미 「우두머리」). 붙인 칩 수.
        /// 은하의 응징 · 천상의 오라 · 프리즘 아머는 배틀 기둥의 칩이, 왜곡 균열 카드는 기술 칸이 보인다. side · clear = 마우스 오버 자리(HUD 아래 판의 파티 칸은 위 · 판을 덮지 않게).
        /// </summary>
        public static int Passives(UiStage stage, VisualElement parent, PokemonState pk, bool alpha = true, string name = null, bool small = false, TipSide side = TipSide.Below, VisualElement clear = null)
        {
            int n = 0;
            if (alpha && pk.Alpha) n += Passive(stage, parent, "screen.passive.alpha", stage.T("item.alpha_pokemon"), stage.TOrNull("item.alpha_pokemon.text"), KitBadgeKind.Red, name != null ? name + "-alpha" : null, small, side, clear);
            if (LegendRules.HasBeastBoost(pk)) n += Passive(stage, parent, "screen.passive.beast_boost", null, stage.T("screen.passive.beast_boost.tip"), KitBadgeKind.Gold, name != null ? name + "-beast" : null, small, side, clear);
            return n;
        }

        static int Passive(UiStage stage, VisualElement parent, string key, string title, string tip, KitBadgeKind kind, string name, bool small, TipSide side, VisualElement clear)
        {
            var chip = Kit.Badge(stage.T(key), kind, small ? Kit.F1 : Kit.F2);   // 정비 판 카드(좁은 칸)는 작은 글
            if (name != null) chip.name = name;
            chip.AddToClassList("sn-passive");
            parent.Add(chip);
            stage.Tips.Hook(chip, title ?? stage.T(key), tip, side, reading: true, clear: clear);
            return 1;
        }

        /// <summary>지닌물건 칸의 우두머리 토큰(T23 — 원작 토큰 그림 + 이름, 마우스 오버 = 효과 · 뗄 수 없음).</summary>
        public static void AlphaToken(UiStage stage, VisualElement slot)
        {
            WorkUi.Icon(slot, GenAssets.ItemTokens + "alpha_pokemon.png", "sn-icon");
            slot.Add(Kit.Text(stage.T("item.alpha_pokemon"), Kit.F2));
            stage.Tips.Hook(slot, stage.T("item.alpha_pokemon"), stage.TOrNull("item.alpha_pokemon.text"), TipSide.Below, reading: true);
        }

        /// <summary>상태 이상의 문자열 키(<c>screen.status.*</c>). 없음이면 null.</summary>
        public static string StatusKey(StatusEffect status)
        {
            switch (status)
            {
                case StatusEffect.None: return null;
                default: return "screen.status." + status.ToString().ToLowerInvariant();
            }
        }
    }
}
