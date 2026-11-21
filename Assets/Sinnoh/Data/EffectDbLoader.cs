using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Sinnoh.Rules;

namespace Sinnoh.Data
{
    /// <summary>
    /// effects.json(Tools/data/build.mjs가 effects/moves.json에서 만듦, 01 §7-1, M3-f) → EffectDb, 그리고 그 반대(Write). 행 하나 = 효과 절 하나,
    /// Id = 상태이상 기본 효과 수 + 행 번호. 키 = "move.&lt;기술 키&gt;"(기술만, 트레이너 카드·아이템·카드 효과는 M5·M6).
    /// 엄격하다: 모르는 op·창·값, op가 쓰지 않는 칸, 빠진 칸, 빈 조건, 기술 표 순서가 아닌 행은 FormatException. 정규형 하나만 받아 읽고 다시 쓰면 같은 바이트다.
    /// M5 효과 기반(01 §7-6): 행 칸 limit(한도) · onNoTarget(SkipOp만 적는다 — 기본 Skip은 null) · handler(처리기 이름 — 있으면 ops는 빈 배열),
    /// op 칸 scope · role · hold(op Hold의 지속 보정). 처리기 이름은 EffectDb가 등록부(handlers, 기본 EffectHandlers.BuiltIn)에서 찾는다.
    /// M5 지속 보정 갈래(rulings G19): hold에 걸 것이 늘었다(ModRules.CanHold — 칸은 걸 것마다: face·value·status, role은 굴림과 얽힌 것을 교체까지·배틀 끝까지로 걸 때만),
    /// op ExtendMods(who User), 조건 sameMoveAsLast: false(「If this move was not used last turn」).
    /// M5 나머지 op 갈래(rulings G20): op FatigueTo·FatigueDamage·InflictBoth(op 칸 status2)·InflictRolled·Damage·FaintBelow·ConsumeApricorn·ReflectTaken·CarryDamage·SwitchOut·
    /// 표지 IgnorePrism·FromParty, hold Retaliate, who TargetOther·TargetOthers(Inflict·Damage만), Heal의 face(면 하나마다), 조건 partyType·knowsType, 처리기 절의 창 BeforeRoll.
    /// </summary>
    public static class EffectDbLoader
    {
        const string File = "effects.json";
        const string MovePrefix = "move";

        public static EffectDb Load(Stream effects, CardDb cards) => Load(TableJson.ReadAll(effects, nameof(effects)), cards);

        public static EffectDb Load(string json, CardDb cards, IReadOnlyDictionary<string, EffectHandler> handlers = null)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            var rows = TableJson.Parse<EffectRow>(json, File);
            var defs = new EffectDef[rows.Count];
            for (int i = 0; i < defs.Length; i++) defs[i] = ToDef(rows[i], i, cards);
            return TableJson.Wrap(File, () => new EffectDb(defs, cards.Moves.Count, handlers));
        }

        // ── 읽기 ──

        static EffectDef ToDef(EffectRow r, int i, CardDb cards)
        {
            string at = $"{File} 행 {i}";
            TableJson.CheckKey(r.Key, MovePrefix, File, i);
            int move = TableJson.Index(cards.MoveIds, r.Key.Substring(MovePrefix.Length + 1), at);
            var window = TableJson.ParseEnum<Window>(r.Window, at);
            var when = r.When == null ? null : ToCondition(r.When, at);
            if (r.Handler != null && r.Handler.Length == 0) throw new FormatException($"{at}: 빈 handler(없으면 null)");
            if ((r.Ops.Count == 0) != (r.Handler != null)) throw new FormatException($"{at}: ops가 비었다(handler가 있을 때만 비운다)");
            var ops = new EffectOpDef[r.Ops.Count];
            for (int k = 0; k < ops.Length; k++) ops[k] = ToOp(r.Ops[k] ?? throw new FormatException($"{at}: op {k}가 null이다"), $"{at} op {k}");
            var limit = EffectLimit.None;
            if (r.Limit != null)
            {
                limit = TableJson.ParseEnum<EffectLimit>(r.Limit, at);
                if (limit == EffectLimit.None) throw new FormatException($"{at}: limit None은 쓰지 않는다(없으면 null)");
            }
            var noTarget = EffectNoTarget.Skip;
            if (r.OnNoTarget != null)
            {
                noTarget = TableJson.ParseEnum<EffectNoTarget>(r.OnNoTarget, at);
                if (noTarget == EffectNoTarget.Skip) throw new FormatException($"{at}: onNoTarget Skip은 기본이라 쓰지 않는다(null)");
            }
            return TableJson.Wrap(File, () => new EffectDef(EffectDb.BuiltInCount + i, new SourceRef(SourceKind.Move, move), window, EffectRole.Attacker, when, ops,
                limit, noTarget, r.Handler));
        }

        static EffectCondition ToCondition(ConditionRow c, string at)
        {
            if (c.Symbols == null && c.TargetStatus == null && c.TargetType == null && c.TargetHeldItem == null && c.SameMoveAsLast == null && c.PartyType == null && c.KnowsType == null)
                throw new FormatException($"{at}: 빈 조건은 null로 쓴다");
            if (c.Symbols != null && c.Symbols < 1) throw new FormatException($"{at}: symbols {c.Symbols}(1 이상)");
            if (c.TargetHeldItem == false) throw new FormatException($"{at}: targetHeldItem은 true만(아니면 null)");
            bool any = c.TargetStatus == "Any";
            var status = c.TargetStatus == null || any ? StatusEffect.None : Status(c.TargetStatus, at);
            var type = PokemonType.Typeless;
            if (c.TargetType != null)
            {
                type = TableJson.ParseEnum<PokemonType>(c.TargetType, at);
                if (type == PokemonType.Typeless) throw new FormatException($"{at}: targetType은 타입없음이 아니다");
            }
            return new EffectCondition(c.Symbols ?? 0, any, status, type, c.TargetHeldItem == true, false, c.SameMoveAsLast == true, c.SameMoveAsLast == false,
                TypeOrNone(c.PartyType, at), TypeOrNone(c.KnowsType, at));
        }

        static PokemonType TypeOrNone(string value, string at)
        {
            if (value == null) return PokemonType.Typeless;
            var t = TableJson.ParseEnum<PokemonType>(value, at);
            if (t == PokemonType.Typeless) throw new FormatException($"{at}: 조건 타입은 타입없음이 아니다(없으면 null)");
            return t;
        }

        static StatusEffect Status(string value, string at)
        {
            var s = TableJson.ParseEnum<StatusEffect>(value, at);
            if (s == StatusEffect.None) throw new FormatException($"{at}: 상태 None은 쓰지 않는다(없으면 null)");
            return s;
        }

        static EffectOpDef ToOp(OpRow o, string at)
        {
            var op = TableJson.ParseEnum<EffectOp>(o.Op, at);
            var who = EffectWho.None;
            if (o.Who != null)
            {
                who = TableJson.ParseEnum<EffectWho>(o.Who, at);
                if (who == EffectWho.None) throw new FormatException($"{at}: who None은 쓰지 않는다(없으면 null)");
            }
            var status = o.Status == null ? StatusEffect.None : Status(o.Status, at);
            var status2 = o.Status2 == null ? StatusEffect.None : Status(o.Status2, at);
            if ((op == EffectOp.InflictBoth) != (o.Status2 != null)) throw new FormatException($"{at}: status2는 InflictBoth만(InflictBoth엔 꼭)");
            if ((who == EffectWho.TargetOther || who == EffectWho.TargetOthers) && op != EffectOp.Inflict && op != EffectOp.Damage) throw new FormatException($"{at}: {who}는 Inflict·Damage만");
            var face = AttackDieFace.None;
            if (o.Face != null)
            {
                face = TableJson.ParseEnum<AttackDieFace>(o.Face, at);
                if (face == AttackDieFace.None) throw new FormatException($"{at}: face None은 쓰지 않는다(없으면 null)");
            }
            var scope = ModScope.None;
            if (o.Scope != null)
            {
                scope = TableJson.ParseEnum<ModScope>(o.Scope, at);
                if (scope == ModScope.None) throw new FormatException($"{at}: scope None은 쓰지 않는다(없으면 null)");
            }
            var role = o.Role == null ? EffectRole.Attacker : TableJson.ParseEnum<EffectRole>(o.Role, at);
            var held = o.Hold == null ? default(EffectOp) : TableJson.ParseEnum<EffectOp>(o.Hold, at);
            if (op != EffectOp.Hold && (o.Scope != null || o.Role != null || o.Hold != null)) throw new FormatException($"{at}: {op}는 scope·role·hold를 쓰지 않는다");
            // op마다 쓰는 칸(build.mjs lib/effects.mjs OP_FIELDS와 같다)
            bool needWho = false, needStatus = false, needFace = false, needValue = false, mayStatus = false, mayFace = false;
            switch (op)
            {
                case EffectOp.StrengthTo:
                    needValue = true;
                    if (o.Value < 1 || o.Value > AttackDice.MaxDice) throw new FormatException($"{at}: StrengthTo {o.Value}(1~9)");
                    break;
                case EffectOp.FaceDamage:
                    needFace = needValue = true;
                    if (o.Value == 0) throw new FormatException($"{at}: FaceDamage 0");
                    break;
                case EffectOp.ExtraDamage:
                    needValue = true;
                    if (o.Value == 0) throw new FormatException($"{at}: ExtraDamage 0");
                    break;
                case EffectOp.Double:
                    break;
                case EffectOp.Inflict:
                    needWho = needStatus = true;
                    if (who == EffectWho.Party) throw new FormatException($"{at}: Inflict는 Target·User만");
                    break;
                case EffectOp.Heal:
                    needWho = needValue = true;
                    mayFace = true;
                    break;
                case EffectOp.CureStatus:
                    needWho = true;
                    mayStatus = true;
                    break;
                case EffectOp.Fatigue:
                    needWho = needValue = true;
                    if (who == EffectWho.Party) throw new FormatException($"{at}: Fatigue는 Target·User만");
                    break;
                case EffectOp.SelfDamage:
                    needValue = true;
                    mayFace = true;
                    break;
                case EffectOp.Hold:
                {
                    // 지속 보정: who(Target·User) · scope · hold(걸 것) · role(굴림과 얽힌 것을 교체까지·배틀 끝까지로 걸 때만 — 한 번짜리는 범위가 정한다) · 걸 것의 face·value·status
                    if (o.Scope == null || o.Hold == null) throw new FormatException($"{at}: Hold에 scope·hold가 없다");
                    if (!ModRules.CanHold(held)) throw new FormatException($"{at}: Hold {held}는 지속 보정으로 걸 수 없다");
                    if (who == EffectWho.Party) throw new FormatException($"{at}: Hold는 Target·User만");
                    bool once = scope == ModScope.NextAttacked || scope == ModScope.NextAttack;
                    bool roll = ModRules.OnRoll(held);
                    if (!roll && once) throw new FormatException($"{at}: Hold {held}는 UntilSwitch·BattleEnd로만 건다");
                    if ((roll && !once) != (o.Role != null)) throw new FormatException($"{at}: Hold role은 굴림 보정을 UntilSwitch·BattleEnd로 걸 때만 적는다");
                    var when = scope == ModScope.NextAttacked ? EffectRole.Defender : scope == ModScope.NextAttack ? EffectRole.Attacker : role;
                    if ((held == EffectOp.Inflict || held == EffectOp.DamageCap || held == EffectOp.Retaliate || held == EffectOp.ReflectCrits) && when != EffectRole.Defender) throw new FormatException($"{at}: Hold {held}는 공격받을 때(Defender)만");
                    needWho = true;
                    needValue = ModRules.HoldUsesValue(held);
                    needFace = ModRules.HoldUsesFace(held);
                    needStatus = held == EffectOp.Inflict;
                    if (needValue && o.Value == 0 && held != EffectOp.RemoveFaces) throw new FormatException($"{at}: Hold value 0(RemoveFaces만 0 = 모두)");
                    if (needValue && o.Value < 0 && held != EffectOp.FaceDamage && held != EffectOp.ExtraDamage && held != EffectOp.FaceDamageOrCapture)
                        throw new FormatException($"{at}: Hold {held} value {o.Value}(1 이상)");
                    break;
                }
                case EffectOp.ExtendMods:
                    needWho = true;
                    if (who != EffectWho.User) throw new FormatException($"{at}: ExtendMods는 User만");
                    break;
                // ── M5 나머지 op 갈래(rulings G20) ──
                case EffectOp.FatigueTo:
                    needWho = true;
                    if (who == EffectWho.Party) throw new FormatException($"{at}: FatigueTo는 Target·User만");
                    if (o.Value == null || o.Value < 0 || o.Value > FatigueRules.Max) throw new FormatException($"{at}: FatigueTo {o.Value}(0~{FatigueRules.Max})");
                    break;
                case EffectOp.FatigueDamage:
                case EffectOp.IgnorePrism:
                case EffectOp.FromParty:
                case EffectOp.LockDice: // 핸들러 갈래(rulings G21): sacred_sword 표지
                case EffectOp.SwitchOut:
                case EffectOp.ConsumeApricorn:
                    break;
                case EffectOp.InflictBoth:
                    needWho = needStatus = true;
                    if (who == EffectWho.Party) throw new FormatException($"{at}: InflictBoth는 Target·User만");
                    if (status2 == status) throw new FormatException($"{at}: InflictBoth는 서로 다른 상태 둘");
                    break;
                case EffectOp.InflictRolled:
                case EffectOp.ReflectTaken:
                case EffectOp.CarryDamage:
                    needWho = true;
                    if (who != EffectWho.Target) throw new FormatException($"{at}: {op}는 Target만");
                    break;
                case EffectOp.Damage:
                case EffectOp.FaintBelow:
                    needWho = needValue = true;
                    break;
                case EffectOp.Ask:
                    break;
                case EffectOp.ChangeForm: // 「Change to X Form if possible」(rulings A26): who = User, value = 그 종의 상태
                    needWho = needValue = true;
                    if (who != EffectWho.User) throw new FormatException($"{at}: ChangeForm은 User만");
                    break;
                default:
                    throw new FormatException($"{at}: {op}는 데이터에서 쓰지 않는다(상태이상 기본 효과 전용)");
            }
            Field(at, op, "who", o.Who != null, needWho, false);
            Field(at, op, "status", o.Status != null, needStatus, mayStatus);
            Field(at, op, "face", o.Face != null, needFace, mayFace);
            Field(at, op, "value", o.Value != null, needValue || op == EffectOp.FatigueTo, false);
            if (needValue && op != EffectOp.FaceDamage && op != EffectOp.ExtraDamage && op != EffectOp.Hold && o.Value < 1) throw new FormatException($"{at}: {op} value {o.Value}(1 이상)");
            return new EffectOpDef(op, who, status, face, AttackDieFace.None, o.Value ?? 0, scope, role, held, status2);
        }

        static void Field(string at, EffectOp op, string name, bool has, bool need, bool may)
        {
            if (need && !has) throw new FormatException($"{at}: {op}에 {name}가 없다");
            if (!need && !may && has) throw new FormatException($"{at}: {op}는 {name}를 쓰지 않는다");
        }

        // ── 쓰기(build.mjs와 같은 바이트) ──

        public static string Write(EffectDb db, CardDb cards)
        {
            var rows = new List<EffectDef>(db.DataEffects());
            return TableJson.WriteTable(rows, (w, e) =>
            {
                w.WriteStartObject();
                TableJson.Prop(w, "key", MovePrefix + "." + cards.MoveIds[e.Source.Id]);
                TableJson.Prop(w, "window", e.Window.ToString());
                w.WritePropertyName("when");
                if (e.When == null) w.WriteNull();
                else
                {
                    var c = e.When;
                    w.WriteStartObject();
                    TableJson.Prop(w, "symbols", c.MinSymbols > 0 ? c.MinSymbols : (int?)null);
                    TableJson.Prop(w, "targetStatus", c.TargetAnyStatus ? "Any" : c.TargetStatus != StatusEffect.None ? c.TargetStatus.ToString() : null);
                    TableJson.Prop(w, "targetType", c.TargetType != PokemonType.Typeless ? c.TargetType.ToString() : null);
                    TrueOrNull(w, "targetHeldItem", c.TargetHeldItem);
                    w.WritePropertyName("sameMoveAsLast");
                    if (c.SameMoveAsLast) w.WriteValue(true);
                    else if (c.NotSameMoveAsLast) w.WriteValue(false);
                    else w.WriteNull();
                    TableJson.Prop(w, "partyType", c.PartyType != PokemonType.Typeless ? c.PartyType.ToString() : null);
                    TableJson.Prop(w, "knowsType", c.KnowsType != PokemonType.Typeless ? c.KnowsType.ToString() : null);
                    w.WriteEndObject();
                }
                w.WritePropertyName("ops");
                w.WriteStartArray();
                foreach (var o in e.Ops)
                {
                    w.WriteStartObject();
                    TableJson.Prop(w, "op", o.Op.ToString());
                    TableJson.Prop(w, "who", o.Who != EffectWho.None ? o.Who.ToString() : null);
                    TableJson.Prop(w, "status", o.Status != StatusEffect.None ? o.Status.ToString() : null);
                    TableJson.Prop(w, "face", o.Face != AttackDieFace.None ? o.Face.ToString() : null);
                    bool hold = o.Op == EffectOp.Hold;
                    TableJson.Prop(w, "value", o.Op == EffectOp.Double || o.Op == EffectOp.Inflict || o.Op == EffectOp.CureStatus || o.Op == EffectOp.Ask || o.Op == EffectOp.ExtendMods
                        || NoValue(o.Op) || (hold && !ModRules.HoldUsesValue(o.Held)) ? (int?)null : o.Value);
                    TableJson.Prop(w, "scope", hold ? o.Scope.ToString() : null);
                    TableJson.Prop(w, "role", hold && ModRules.OnRoll(o.Held) && (o.Scope == ModScope.UntilSwitch || o.Scope == ModScope.BattleEnd) ? o.Role.ToString() : null);
                    TableJson.Prop(w, "hold", hold ? o.Held.ToString() : null);
                    TableJson.Prop(w, "status2", o.Status2 != StatusEffect.None ? o.Status2.ToString() : null);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                TableJson.Prop(w, "limit", e.Limit != EffectLimit.None ? e.Limit.ToString() : null);
                TableJson.Prop(w, "onNoTarget", e.OnNoTarget != EffectNoTarget.Skip ? e.OnNoTarget.ToString() : null);
                TableJson.Prop(w, "handler", e.Handler);
                w.WriteEndObject();
            });
        }

        /// <summary>값 칸이 없는 나머지 op 갈래 op(rulings G20).</summary>
        static bool NoValue(EffectOp op) => op == EffectOp.FatigueDamage || op == EffectOp.InflictBoth || op == EffectOp.InflictRolled || op == EffectOp.ConsumeApricorn
            || op == EffectOp.ReflectTaken || op == EffectOp.CarryDamage || op == EffectOp.SwitchOut || op == EffectOp.IgnorePrism || op == EffectOp.FromParty || op == EffectOp.LockDice;

        static void TrueOrNull(JsonWriter w, string name, bool value)
        {
            w.WritePropertyName(name);
            if (value) w.WriteValue(true);
            else w.WriteNull();
        }

#pragma warning disable 0649 // Newtonsoft가 반사로 채운다
        // 필드 이름은 build.mjs(lib/effects.mjs) 출력과 같다
        [JsonObject(ItemRequired = Required.Always)]
        sealed class EffectRow
        {
            public string Key;
            public string Window;
            [JsonProperty(Required = Required.AllowNull)] public ConditionRow When;
            public List<OpRow> Ops;
            [JsonProperty(Required = Required.AllowNull)] public string Limit;
            [JsonProperty(Required = Required.AllowNull)] public string OnNoTarget;
            [JsonProperty(Required = Required.AllowNull)] public string Handler;
        }

        [JsonObject(ItemRequired = Required.Always)]
        sealed class ConditionRow
        {
            [JsonProperty(Required = Required.AllowNull)] public int? Symbols;
            [JsonProperty(Required = Required.AllowNull)] public string TargetStatus;
            [JsonProperty(Required = Required.AllowNull)] public string TargetType;
            [JsonProperty(Required = Required.AllowNull)] public bool? TargetHeldItem;
            [JsonProperty(Required = Required.AllowNull)] public bool? SameMoveAsLast;
            [JsonProperty(Required = Required.AllowNull)] public string PartyType;
            [JsonProperty(Required = Required.AllowNull)] public string KnowsType;
        }

        [JsonObject(ItemRequired = Required.Always)]
        sealed class OpRow
        {
            public string Op;
            [JsonProperty(Required = Required.AllowNull)] public string Who;
            [JsonProperty(Required = Required.AllowNull)] public string Status;
            [JsonProperty(Required = Required.AllowNull)] public string Face;
            [JsonProperty(Required = Required.AllowNull)] public int? Value;
            [JsonProperty(Required = Required.AllowNull)] public string Scope;
            [JsonProperty(Required = Required.AllowNull)] public string Role;
            [JsonProperty(Required = Required.AllowNull)] public string Hold;
            [JsonProperty(Required = Required.AllowNull)] public string Status2;
        }
#pragma warning restore 0649
    }
}
