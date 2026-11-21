// 효과(01 §7-1, M3-f): Tools/data/effects/moves.json(손으로 옮긴 기술 효과) → effects.json 행 + 미구현 보고서(effects-todo.tsv).
// 손 파일: { schema: 1, note, moves: { <기술 키>: { en: <xlsx 영문 문구 그대로>, effect: [절…] } } }
//   절 = { window, when?, ops: [op…], limit?, onNoTarget?, handler? }, when = { symbols?, targetStatus?, targetType?, targetHeldItem?, sameMoveAsLast? },
//   op = { op, who?, status?, face?, value?, scope?, role?, hold? }
//   M5 효과 기반(01 §7-6): limit = Turn·BattleTurn·Battle·UntilSwitch(한도) · onNoTarget = SkipOp(기본 Skip은 적지 않는다) · handler = C# 처리기 이름(있으면 ops는 빼거나 []),
//   op Hold = { who: Target|User, scope: NextAttacked|NextAttack|UntilSwitch|BattleEnd, role: Attacker|Defender(UntilSwitch·BattleEnd만), hold: FaceDamage|ExtraDamage, face?, value },
//   op Ask = {}(효과 주인에게 「쓸까」 — 안 쓰면 그 절의 나머지 op를 건너뛴다).
//   M5 지속 보정 갈래(rulings G19): hold에 걸 것(HOLDS — 칸은 HOLD_FIELDS) · op ExtendMods = { who: User }(eternal_glade) · when.sameMoveAsLast: false(「If this move was not used last turn」).
//   M5 「전설·알파·폼」(rulings A26): 실행 op ChangeForm = { who: User, value: 그 종의 상태 }(「Change to X Form if possible」) · Hold RemoveFaces의 value 0 = 모두(kings_shield).
//   M5 나머지 op 갈래(rulings G20): 실행 op FatigueTo(value 0~5) · InflictBoth(status·status2) · InflictRolled · Damage(value) · FaintBelow(value) · ConsumeApricorn · ReflectTaken · CarryDamage · SwitchOut,
//     상시 op FatigueDamage(BeforeDamage) · 표지 IgnorePrism(BeforeDamage) · FromParty(BeforeRoll), hold Retaliate(face·value, 공격받을 때), who TargetOther·TargetOthers(Inflict·Damage만),
//     Heal의 face(면 하나마다), when.partyType · when.knowsType, 처리기 절(type_learnable·type_any·belch = BeforeRoll · photon_geyser = BeforeDamage).
//   M5 핸들러 갈래(rulings G21): 표지 LockDice(BeforeRoll, sacred_sword) · 처리기 절 — 프레임(foul_play·mimic·metronome·kinesis·future_sight = BeforeRoll · drain_half·second_move·encore = BeforeDamage)과
//     셈 처리기(Sinnoh.Rules EffectHandlers.Static — 상시 절: 공격력 party_song·party_surf·revenge·taken_last·flail·rollout·eruption·electro_ball·seismic_toss·heavy_slam = BeforeRoll · future_sight_miss = BeforeDamage).
//     굴림과 얽힌 것(ROLL_HOLDS)만 한 번짜리 범위로 걸 수 있고 UntilSwitch·BattleEnd면 role을 적는다. 그 밖(턴 끝 회복·면역·금지)은 UntilSwitch·BattleEnd로만, role 없이.
// 출력 행(절 하나 = 한 행, 기술 표 순서 → 절 순서) = 모든 칸을 적은 정규형. Sinnoh.Data EffectDbLoader가 같은 규칙으로 엄격하게 읽고 같은 바이트로 쓴다.
// 영문 문구(정본, A20-9)가 xlsx와 다르면 오류 — 문구가 바뀌면 효과를 다시 본다. 문구 일부만 옮길 수 있는 기술은 넣지 않는다(rulings G14).
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { TYPES } from './cube.mjs';

const WINDOWS_STATIC = { StrengthTo: 'BeforeRoll', FaceDamage: 'BeforeDamage', ExtraDamage: 'BeforeDamage', Double: 'BeforeDamage',
  FatigueDamage: 'BeforeDamage', IgnorePrism: 'BeforeDamage', FromParty: 'BeforeRoll', LockDice: 'BeforeRoll' };
const ACTION_OPS = ['Inflict', 'Heal', 'CureStatus', 'Fatigue', 'SelfDamage', 'Hold', 'Ask', 'ExtendMods',
  'FatigueTo', 'InflictBoth', 'InflictRolled', 'Damage', 'FaintBelow', 'ConsumeApricorn', 'ReflectTaken', 'CarryDamage', 'SwitchOut', 'ChangeForm'];
const HANDLER_WINDOWS = ['BeforeRoll', 'BeforeDamage'];
const SCOPES = ['NextAttacked', 'NextAttack', 'UntilSwitch', 'BattleEnd'];
const ROLES = ['Attacker', 'Defender'];
// 걸 것마다 쓰는 칸(Sinnoh.Rules ModRules.CanHold·HoldUsesFace·HoldUsesValue·OnRoll과 같다). signed = 음수 값도 된다
const HOLD_FIELDS = {
  FaceDamage: { face: true, value: true, roll: true, signed: true },
  ExtraDamage: { value: true, roll: true, signed: true },
  FaceDamageOrCapture: { face: true, value: true, roll: true, signed: true },
  AddFaces: { face: true, value: true, roll: true },
  RemoveFaces: { face: true, value: true, roll: true },
  Inflict: { status: true, roll: true, defender: true },
  DamageCap: { value: true, roll: true, defender: true },
  TurnEndHeal: { value: true },
  StatusImmune: {},
  NoEscape: {},
  NoFormChange: {},
  NoMoveSwitch: {},
  Retaliate: { face: true, value: true, roll: true, defender: true },
  ReflectCrits: { roll: true, defender: true },
};
const HOLDS = Object.keys(HOLD_FIELDS);
const LIMITS = ['Turn', 'BattleTurn', 'Battle', 'UntilSwitch'];
const STATUSES = ['Confused', 'Paralysed', 'Poisoned', 'Burned', 'Frozen', 'Drowsy'];
const FACES = ['StatusSymbol', 'RegularHit', 'CriticalHit'];
const WHO = ['Target', 'User', 'Party', 'TargetOther', 'TargetOthers'];
// op마다 칸: need = 있어야 함, may = 있어도 됨(없으면 null), 나머지는 없어야 함
const OP_FIELDS = {
  StrengthTo: { need: ['value'], may: [] },
  FaceDamage: { need: ['face', 'value'], may: [] },
  ExtraDamage: { need: ['value'], may: [] },
  Double: { need: [], may: [] },
  Inflict: { need: ['who', 'status'], may: [] },
  Heal: { need: ['who', 'value'], may: ['face'] },
  CureStatus: { need: ['who'], may: ['status'] },
  Fatigue: { need: ['who', 'value'], may: [] },
  SelfDamage: { need: ['value'], may: ['face'] },
  Hold: { need: ['who', 'scope', 'hold'], may: ['face', 'role', 'value', 'status'] },
  Ask: { need: [], may: [] },
  ExtendMods: { need: ['who'], may: [] },
  FatigueTo: { need: ['who', 'value'], may: [] },
  FatigueDamage: { need: [], may: [] },
  IgnorePrism: { need: [], may: [] },
  FromParty: { need: [], may: [] },
  LockDice: { need: [], may: [] },
  InflictBoth: { need: ['who', 'status', 'status2'], may: [] },
  InflictRolled: { need: ['who'], may: [] },
  Damage: { need: ['who', 'value'], may: [] },
  FaintBelow: { need: ['who', 'value'], may: [] },
  ConsumeApricorn: { need: [], may: [] },
  ReflectTaken: { need: ['who'], may: [] },
  CarryDamage: { need: ['who'], may: [] },
  SwitchOut: { need: [], may: [] },
  ChangeForm: { need: ['who', 'value'], may: [] },
};
const OP_KEYS = ['op', 'who', 'status', 'face', 'value', 'scope', 'role', 'hold', 'status2'];
const WHEN_KEYS = ['symbols', 'targetStatus', 'targetType', 'targetHeldItem', 'sameMoveAsLast', 'partyType', 'knowsType'];

/** → { rows, todo(tsv), implemented } */
export function buildEffects(root, file, moves, errors) {
  const j = JSON.parse(readFileSync(path.join(root, file), 'utf8'));
  if (j.schema !== 1 || Object.keys(j).sort().join() !== 'moves,note,schema') { errors.push(`${file}: 형식(schema 1, 필드 moves·note·schema)`); return { rows: [], todo: '' }; }
  const byKey = new Map(moves.map((m, i) => [m.key, i]));
  const rows = [];
  const entries = Object.entries(j.moves);
  for (const [key, spec] of entries) {
    const at = `${file} moves.${key}`;
    if (!byKey.has(key)) { errors.push(`${at}: moves에 없는 기술`); continue; }
    const m = moves[byKey.get(key)];
    if (Object.keys(spec).sort().join() !== 'effect,en') errors.push(`${at}: 필드는 en·effect`);
    if (spec.en !== m.en) errors.push(`${at}: 영문 문구가 xlsx와 다르다 — 문구가 바뀌면 효과를 다시 옮긴다`);
    if (!Array.isArray(spec.effect) || spec.effect.length === 0) { errors.push(`${at}: effect는 절 1개 이상`); continue; }
    spec.effect.forEach((c, ci) => {
      const row = clause(`${at}.effect[${ci}]`, key, c, errors);
      if (row) rows.push({ index: byKey.get(key), ci, row });
    });
  }
  rows.sort((a, b) => a.index - b.index || a.ci - b.ci);
  const has = new Set(rows.map(r => r.index));
  const todo = ['key\ttype\tattackStrength\ten', ...moves.filter((m, i) => !has.has(i)).map(m => `${m.key}\t${m.type}\t${m.attackStrength ?? '?'}\t${m.en}`)].join('\n') + '\n';
  return { rows: rows.map(r => r.row), todo, implemented: has.size };
}

function clause(at, key, c, errors) {
  const bad = Object.keys(c).filter(k => !['window', 'when', 'ops', 'limit', 'onNoTarget', 'handler'].includes(k));
  if (bad.length) errors.push(`${at}: 모르는 칸 ${bad.join(',')}`);
  const handler = c.handler ?? null;
  if (handler !== null && (typeof handler !== 'string' || handler === '')) errors.push(`${at}: handler는 처리기 이름`);
  if (c.limit != null && !LIMITS.includes(c.limit)) errors.push(`${at}: limit "${c.limit}"`);
  if (c.onNoTarget != null && c.onNoTarget !== 'SkipOp') errors.push(`${at}: onNoTarget은 SkipOp만(기본 Skip은 적지 않는다)`);
  const tail = { limit: c.limit ?? null, onNoTarget: c.onNoTarget ?? null, handler };
  if (handler !== null) {
    // 처리기 절: op 대신 C#(Sinnoh.Rules EffectHandlers — 이름이 등록부에 없으면 로더가 거부한다)
    if (c.ops !== undefined && !(Array.isArray(c.ops) && c.ops.length === 0)) errors.push(`${at}: handler가 있으면 ops를 쓰지 않는다`);
    if (tail.onNoTarget !== null) errors.push(`${at}: 처리기 절은 onNoTarget을 쓰지 않는다`);
    if (!HANDLER_WINDOWS.includes(c.window)) errors.push(`${at}: 처리기 절의 창은 ${HANDLER_WINDOWS.join('·')}`);
    return { key: `move.${key}`, window: c.window, when: when(`${at}.when`, c.when, errors), ops: [], ...tail };
  }
  if (!Array.isArray(c.ops) || c.ops.length === 0) { errors.push(`${at}: ops가 없다`); return null; }
  const ops = c.ops.map((o, i) => op(`${at}.ops[${i}]`, o, errors));
  const statics = c.ops.filter(o => o.op in WINDOWS_STATIC).length;
  if (statics !== 0 && statics !== c.ops.length) errors.push(`${at}: 상시 op와 실행 op를 한 절에 섞지 않는다`);
  if (statics !== 0 && (tail.limit !== null || tail.onNoTarget !== null)) errors.push(`${at}: 상시 절은 limit·onNoTarget을 쓰지 않는다`);
  for (const o of c.ops) {
    const want = WINDOWS_STATIC[o.op] || 'BeforeDamage';
    if (c.window !== want) errors.push(`${at}: ${o.op}의 창은 ${want}(지금 ${c.window})`);
  }
  return { key: `move.${key}`, window: c.window, when: when(`${at}.when`, c.when, errors), ops, ...tail };
}

function when(at, w, errors) {
  if (w === undefined || w === null) return null;
  const bad = Object.keys(w).filter(k => !WHEN_KEYS.includes(k));
  if (bad.length) errors.push(`${at}: 모르는 칸 ${bad.join(',')}`);
  const r = Object.fromEntries(WHEN_KEYS.map(k => [k, w[k] ?? null]));
  if (WHEN_KEYS.every(k => r[k] === null)) errors.push(`${at}: 빈 조건은 쓰지 않는다`);
  if (r.symbols !== null && !(Number.isInteger(r.symbols) && r.symbols >= 1)) errors.push(`${at}: symbols는 1 이상 정수`);
  if (r.targetStatus !== null && r.targetStatus !== 'Any' && !STATUSES.includes(r.targetStatus)) errors.push(`${at}: targetStatus "${r.targetStatus}"`);
  if (r.targetType !== null && (!TYPES.includes(r.targetType) || r.targetType === 'Typeless')) errors.push(`${at}: targetType "${r.targetType}"`);
  if (r.targetHeldItem !== null && r.targetHeldItem !== true) errors.push(`${at}: targetHeldItem은 true만(없으면 뺀다)`);
  if (r.sameMoveAsLast !== null && typeof r.sameMoveAsLast !== 'boolean') errors.push(`${at}: sameMoveAsLast는 true(직전 턴과 같은 기술)·false(직전 턴에 쓰지 않음)`);
  for (const k of ['partyType', 'knowsType'])
    if (r[k] !== null && (!TYPES.includes(r[k]) || r[k] === 'Typeless')) errors.push(`${at}: ${k} "${r[k]}"`);
  return r;
}

function op(at, o, errors) {
  const bad = Object.keys(o).filter(k => !OP_KEYS.includes(k));
  if (bad.length) errors.push(`${at}: 모르는 칸 ${bad.join(',')}`);
  const f = OP_FIELDS[o.op];
  if (!f) { errors.push(`${at}: 모르는 op "${o.op}"(어휘: ${Object.keys(OP_FIELDS).join(', ')})`); return null; }
  for (const k of ['who', 'status', 'face', 'value', 'scope', 'role', 'hold', 'status2']) {
    const has = o[k] !== undefined && o[k] !== null;
    if (f.need.includes(k) && !has) errors.push(`${at}: ${o.op}에 ${k}가 없다`);
    if (!f.need.includes(k) && !f.may.includes(k) && has) errors.push(`${at}: ${o.op}는 ${k}를 쓰지 않는다`);
  }
  if (o.who != null && !WHO.includes(o.who)) errors.push(`${at}: who "${o.who}"`);
  if ((o.op === 'Inflict' || o.op === 'Fatigue' || o.op === 'Hold' || o.op === 'FatigueTo' || o.op === 'InflictBoth') && o.who === 'Party') errors.push(`${at}: ${o.op}는 Target·User만`);
  if ((o.who === 'TargetOther' || o.who === 'TargetOthers') && o.op !== 'Inflict' && o.op !== 'Damage') errors.push(`${at}: ${o.who}는 Inflict·Damage만`);
  if (['InflictRolled', 'ReflectTaken', 'CarryDamage'].includes(o.op) && o.who !== 'Target') errors.push(`${at}: ${o.op}는 Target만`);
  if (o.op === 'InflictBoth' && o.status === o.status2) errors.push(`${at}: InflictBoth는 서로 다른 상태 둘`);
  if (o.status2 != null && !STATUSES.includes(o.status2)) errors.push(`${at}: status2 "${o.status2}"`);
  if (o.op === 'Hold') {
    if (o.scope != null && !SCOPES.includes(o.scope)) errors.push(`${at}: scope "${o.scope}"`);
    if (o.hold != null && !HOLDS.includes(o.hold)) errors.push(`${at}: hold "${o.hold}"(걸 수 있는 op: ${HOLDS.join(', ')})`);
    if (o.role != null && !ROLES.includes(o.role)) errors.push(`${at}: role "${o.role}"`);
    const once = o.scope === 'NextAttacked' || o.scope === 'NextAttack';
    const h = HOLD_FIELDS[o.hold] || {};
    if (!h.roll && once) errors.push(`${at}: Hold ${o.hold}는 UntilSwitch·BattleEnd로만 건다`);
    if ((!!h.roll && !once) !== (o.role != null)) errors.push(`${at}: Hold role은 굴림 보정을 UntilSwitch·BattleEnd로 걸 때만 적는다`);
    const role = o.scope === 'NextAttacked' ? 'Defender' : o.scope === 'NextAttack' ? 'Attacker' : o.role;
    if (h.defender && role !== 'Defender') errors.push(`${at}: Hold ${o.hold}는 공격받을 때(Defender)만`);
    for (const k of ['face', 'value', 'status']) if (!!h[k] !== (o[k] != null)) errors.push(`${at}: Hold ${o.hold}의 ${k} 칸이 맞지 않다`);
    if (o.value != null && o.value < 0 && !h.signed) errors.push(`${at}: Hold ${o.hold} value ${o.value}(1 이상)`);
  }
  if (o.op === 'ExtendMods' && o.who !== 'User') errors.push(`${at}: ExtendMods는 User만`);
  if (o.op === 'ChangeForm' && o.who !== 'User') errors.push(`${at}: ChangeForm은 User만`);
  if (o.status != null && !STATUSES.includes(o.status)) errors.push(`${at}: status "${o.status}"`);
  if (o.face != null && !FACES.includes(o.face)) errors.push(`${at}: face "${o.face}"`);
  if (o.value != null) {
    const ok = Number.isInteger(o.value) && (o.op === 'StrengthTo' ? o.value >= 1 && o.value <= 9
      : o.op === 'FatigueTo' ? o.value >= 0 && o.value <= 5
      : o.op === 'Hold' && o.hold === 'RemoveFaces' ? o.value >= 0
      : o.op === 'FaceDamage' || o.op === 'ExtraDamage' || o.op === 'Hold' ? o.value !== 0 : o.value >= 1);
    if (!ok) errors.push(`${at}: ${o.op} value ${o.value}`);
  }
  if (!ACTION_OPS.includes(o.op) && !(o.op in WINDOWS_STATIC)) errors.push(`${at}: ${o.op}는 데이터에서 쓰지 않는다`);
  return { op: o.op, who: o.who ?? null, status: o.status ?? null, face: o.face ?? null, value: o.value ?? null, scope: o.scope ?? null, role: o.role ?? null, hold: o.hold ?? null, status2: o.status2 ?? null };
}
