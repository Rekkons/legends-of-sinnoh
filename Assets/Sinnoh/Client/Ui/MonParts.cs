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
            if (tip.HasValue) MoveTip(stage, ctx, row, move, tip.Value, clear);
            return row;
        }

        /// <summary>전설 특수기술 칸 줄의 클래스(결정 195).</summary>
        public const string LegendSlotClass = "sn-legend-slot";

        /// <summary>전설 특수기술의 이름 키(없음이면 null — 결정 195).</summary>
        public static string TacticKey(LegendTactic t) =>
            t == LegendTactic.AlphaStrike ? "screen.battle.move.alpha_strike" : t == LegendTactic.BeastBoost ? "screen.passive.beast_boost" : null;

        /// <summary>전설 특수기술 칸의 마우스 오버 글: 효과 + 「이 칸을 채우면 ○○이(가) 사라집니다.」(결정 195 [가정]).</summary>
        public static string TacticTip(UiStage stage, LegendTactic t)
        {
            string key = TacticKey(t);
            if (key == null) return null;
            string effect = stage.TOrNull(t == LegendTactic.AlphaStrike ? key + ".text" : key + ".tip");
            string covers = KoJosa.Fix(stage.T("screen.legend.covers").Replace("{name}", stage.T(key)));
            return effect != null ? effect + "\n" + covers : covers;
        }

        /// <summary>
        /// 빈 배운 기술 칸을 남은 전설 특수기술이 차지했으면(<see cref="LegendRules.TacticAt"/>) 「빈 칸」 대신 그릴 줄(결정 195): 이름 · 알파 스트라이크면 배울 수 있는 타입 그림(결정 182) ·
        /// 마우스 오버 = 효과 + 채우면 사라진다(<paramref name="tip"/> — null이면 걸지 않는다). 차지하지 않은 칸이면 null.
        /// </summary>
        public static VisualElement TacticRow(UiStage stage, Ctx ctx, PokemonState pk, int slot, TipSide? tip = TipSide.Below, VisualElement clear = null, bool types = true)
        {
            var t = LegendRules.TacticAt(pk, slot);
            if (t == LegendTactic.None) return null;
            string key = TacticKey(t);
            var row = new VisualElement();
            row.AddToClassList("sn-enc-move");
            row.AddToClassList(LegendSlotClass);
            var name = Kit.Text(stage.T(key), Kit.F2);
            name.AddToClassList("sn-enc-move-name");
            row.Add(name);
            if (t == LegendTactic.AlphaStrike && types)   // 좁은 칸(정비 판 — 위에 「배우는 타입」 줄이 있다)은 이름만
            {
                var learn = new System.Collections.Generic.List<int>(4);
                LegendRules.AlphaTypes(ctx, pk, learn);
                foreach (int type in learn) TypeIcon(row, (PokemonType)type);
            }
            if (tip.HasValue) stage.Tips.Hook(row, stage.T(key), TacticTip(stage, t), tip.Value, reading: true, clear: clear);
            return row;
        }

        /// <summary>기술의 마우스 오버(기술 줄과 같은 글 — 이름 + 효과 문구)를 그 요소에 건다. 기술 이름만 적힌 단추(가르칠 칸의 지금 기술 등)도 이것으로 — 단추면 입력이 열렸을 때만 부른다.</summary>
        /// <param name="note">효과 문구 뒤에 한 줄 더 붙일 안내(그 칸을 누르면 일어나는 일 등 — 칸 하나에 툴팁 하나만 건다).</param>
        public static void MoveTip(UiStage stage, Ctx ctx, VisualElement target, int move, TipSide side = TipSide.Below, VisualElement clear = null, string note = null)
        {
            string key = BattleFeed.MoveNameKey(ctx, move);
            string text = stage.TOrNull(key + ".text");
            if (note != null) text = string.IsNullOrEmpty(text) ? note : text + "\n" + note;
            stage.Tips.Hook(target, stage.T(key), text, side, reading: true, clear: clear);
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

        /// <summary>
        /// 등급 상자(결정 192): 「약함 · 보통 · 강함 · 전설 · 우두머리」 배지에 등급 색을 입힌다(USS <c>sn-grade-&lt;등급&gt;</c> — 색 토큰 <c>--sn-grade-*</c>, 단계가 읽히게 파랑 → 초록 → 주황 → 금 · 우두머리 빨강).
        /// grade = 영문 등급 키(weak · moderate · strong · legendary · alpha — 문자열 키 <c>screen.encounter.grade.*</c>의 꼬리). 그 등급 글이 표에 없으면 null.
        /// </summary>
        public static Label GradeBadge(UiStage stage, string grade, string size = Kit.F1)
        {
            string text = grade != null ? stage.TOrNull("screen.encounter.grade." + grade) : null;
            if (text == null) return null;
            var badge = Kit.Badge(text, KitBadgeKind.Plain, size);
            badge.AddToClassList("sn-grade");
            badge.AddToClassList("sn-grade-" + grade);
            return badge;
        }

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

        /// <summary>
        /// 걸린 상태 이상(<see cref="StatusRules.Statuses"/> — 첫 칸 · 둘째 칸 · hex_maniac R의 더한 것)을 배틀과 같은 Pt 상태 그림(<see cref="IconArt.Status(StatusEffect)"/>)으로 나란히 붙인다(결정 180).
        /// 아이콘 이름 = <paramref name="name"/> + <c>-&lt;상태 소문자&gt;</c>, 마우스 오버 = 상태 이름 + 짧은 효과(<c>screen.status.*.tip</c>). 그림이 없으면 이름 배지. 붙인 수.
        /// </summary>
        public static int StatusIcons(UiStage stage, VisualElement parent, PokemonState pk, string name, TipSide side = TipSide.Below, VisualElement clear = null)
        {
            var list = new System.Collections.Generic.List<StatusEffect>(2);
            StatusRules.Statuses(pk, list);
            foreach (var s in list)
            {
                string key = StatusKey(s);
                var tex = GenAssets.Load<Texture2D>(IconArt.Status(s));
                VisualElement icon;
                if (tex != null)
                {
                    icon = new VisualElement();
                    icon.AddToClassList("sn-type-icon");
                    icon.style.backgroundImage = new StyleBackground(tex);
                }
                else icon = Kit.Badge(stage.T(key), KitBadgeKind.Warn, Kit.F1);
                icon.name = name + "-" + s.ToString().ToLowerInvariant();
                icon.AddToClassList("sn-status-icon");
                parent.Add(icon);
                stage.Tips.Hook(icon, stage.T(key), stage.TOrNull(key + ".tip"), side, reading: true, clear: clear);
            }
            return list.Count;
        }
    }
}
