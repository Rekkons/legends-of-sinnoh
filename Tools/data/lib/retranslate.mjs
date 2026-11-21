// 다시 옮긴 한국어(사용자 결정 163 — 3JA2 · 로씨온 번역을 바탕으로 한 글을 영문 원본에서 새로 옮긴 우리 번역으로 바꾼다). lib/strings.mjs가 부른다.
//   손 파일 authored/ko/<묶음>.json { schema: 1, note, strings: { "<문자열 키>": { en: 영문 원본(빌드가 지금 원천과 대조), ko: 우리 번역 } } }
//     파일 이름 = 일감 묶음(<nn>-<이름>.json). 한 키는 한 파일에만. 트레이너 카드 능력은 <키>.text를 옮기면 짧은 문구(.short)도 새 번역을 따른다 —
//     전체 문구가 48자를 넘으면 <키>.short를 함께 쓴다(옛 trainer-short-ko.json은 그 능력에서 쓰지 않는다).
//   범위(scope) = 3JA2 · 로씨온 글이 들어오던 키(아래 FAMILIES) − 공식 이름(종 · 공식 기술 · 원작 자산 직업 · 타입 · 종 이름과 같은 카드 이름 · authored/retranslate-keep.json).
//     지명(region.*) · 사당(shrine.*) · 갤럭시단 · 화면(screen.*) · 튜토리얼(tutorial.*)은 이미 우리 글이거나 공식 이름이라 범위 밖이다.
//   검사: 손 파일 키가 범위 안 · en = 지금 영문 원본(다르면 원본이 바뀐 것) · 용어 규칙(authored/terms-ko.json)에 걸리지 않음(새 번역은 바꾸지 않고 오류) ·
//     빈 값 · 앞뒤 공백 없음 · 범위 키가 모두 옮겨짐(빠진 키는 빌드 오류 — 옛 원천으로 떨어지지 않는다).
//   보고서 reports/retranslate-ko.tsv = 범위 키마다 묶음 · 상태(new · old) · 표시 · en · 지금 ko.
// 룰북 한국어(authored/rulebook/ko/<장>.json)는 이미 손 파일 꼴이라 여기가 아니다 — 그 파일을 고쳐 쓴다(lib/rulebook.mjs가 영어와 구조를 대조한다).
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import path from 'node:path';

export const RETRANSLATE_DIR = 'Tools/data/authored/ko';
export const KEEP_FILE = 'Tools/data/authored/retranslate-keep.json';
const SHORT_MAX = 48;

const readJson = p => JSON.parse(readFileSync(p, 'utf8'));

/** authored/ko/*.json → Map(키 → { en, ko, file }) */
export function readRetranslation(root, errors) {
  const out = new Map();
  const dir = path.join(root, RETRANSLATE_DIR);
  if (!existsSync(dir)) return out;
  for (const name of readdirSync(dir).filter(f => f.endsWith('.json')).sort()) {
    const file = `${RETRANSLATE_DIR}/${name}`;
    let j;
    try { j = readJson(path.join(dir, name)); } catch (e) { errors.push(`${file}: ${e.message}`); continue; }
    if (j.schema !== 1 || Object.keys(j).sort().join() !== 'note,schema,strings' || !j.strings || typeof j.strings !== 'object') { errors.push(`${file}: 형식(schema 1, 필드 note·schema·strings)`); continue; }
    for (const [key, e] of Object.entries(j.strings)) {
      const at = `${file} ${key}`;
      if (!e || Object.keys(e).sort().join() !== 'en,ko' || typeof e.en !== 'string' || typeof e.ko !== 'string') { errors.push(`${at}: 값 {en, ko}`); continue; }
      if (e.ko === '' || e.ko !== e.ko.trim() || /\s{2,}|\n/.test(e.ko)) errors.push(`${at}: ko 빈 값 · 앞뒤 공백 · 겹 띄어쓰기 · 줄바꿈`);
      if (out.has(key)) errors.push(`${at}: ${out.get(key).file}에도 있다`);
      out.set(key, { en: e.en, ko: e.ko, file });
    }
  }
  return out;
}

/** authored/retranslate-keep.json { schema, note, keep: { 키: 까닭 } } → Map */
export function readKeep(root, errors) {
  const j = readJson(path.join(root, KEEP_FILE));
  if (j.schema !== 1 || Object.keys(j).sort().join() !== 'keep,note,schema') errors.push(`${KEEP_FILE}: 형식(schema 1, 필드 keep·note·schema)`);
  for (const [k, v] of Object.entries(j.keep || {})) if (typeof v !== 'string' || !v) errors.push(`${KEEP_FILE} ${k}: 까닭이 없다`);
  return new Map(Object.entries(j.keep || {}));
}

// 묶음(family): 키 → 3JA2 · 로씨온 원천. 순서대로 처음 맞는 것
const FAMILIES = [
  ['ui', /^ui\.(?!types\.)/, 'ff1ce0 언어 모듈(한국어판 Lua)'],
  ['pokemon_form', /^pokemon\./, '한국어판 카드 닉네임(폼 이름)'],
  ['move_text', /^move\.[^.]+\.text$/, '한국어판 카드 Lua move_effect'],
  ['move_name', /^move\.[^.]+$/, '한국어판 카드 Lua move_name(이 게임만의 기술)'],
  ['trainer_class', /^trainer_card\./, '한국어판 카드 직업(원작 자산에 없음 · 조무래기 꼴)'],
  ['trainer_ability_text', /^trainer_ability\.[^.]+\.[12]\.text$/, '한국어판 트레이너 카드 문구(Research/trainer-cards.md §3 KR)'],
  ['trainer_ability_name', /^trainer_ability\.[^.]+\.[12]$/, '한국어판 트레이너 카드 능력 이름'],
  ['fixture_name', /^(village|legendary_site|legendary_shrine|gate)\./, '한국어판 세이브 고정물 이름'],
  ['item_text', /^item\.[^.]+\.text$/, '한국어판 룰북 · 가방 설명(authored/items.json ko)'],
  ['item_name', /^(item|hidden_spot)\.[^.]+$/, '한국어판 세이브 아이템 이름 · authored/items.json name.ko'],
  ['stamp', /^stamp\./, '한국어판 룰북(authored/items.json)'],
  ['card_text', /^(act|home_village|shrine_event|change_of_fate|distortion_breach|ultra_onslaught)\.[^.]+\.text\.\d+$/, '한국어판 카드 전사(authored/cards ko)'],
  ['card_name', /^(act|home_village|shrine_event|change_of_fate|ginkgo_trade|distortion_breach|ultra_onslaught)\.[^.]+$/, '한국어판 카드 이름(authored/cards name.ko)'],
];
export const FAMILY_NAMES = FAMILIES.map(f => f[0]);

/**
 * 범위: Map(키 → { family, flag }). info = { en, ko(최종 — 다시 옮기기 전), speciesKo: Set(종 한국어 이름), officialMoves: Set(공식 기술 키),
 *   classStatus: Map(trainer_card 키 → 대조표 판정), authored: Map(strings-ko.json), keep }
 */
export function retranslateScope(info, errors) {
  const out = new Map();
  for (const k of info.keep.keys()) if (!(k in info.en)) errors.push(` ${k}: 빌드가 만드는 키가 아니다`);
  for (const key of Object.keys(info.en)) {
    const f = FAMILIES.find(([, re]) => re.test(key));
    if (!f || info.keep.has(key)) continue;
    const [family] = f;
    const flags = [];
    if (family === 'pokemon_form' && info.speciesKo.has(info.ko[key])) continue;   // 종 이름 그대로
    if (family === 'move_name' && info.officialMoves.has(key)) continue;          // 현행 공식 기술명
    if (family === 'trainer_class' && !['notInRoms', 'classPlusName'].includes(info.classStatus.get(key))) continue;   // 원작 자산 공식 직업
    if (family === 'card_name' && info.speciesKo.has(info.ko[key])) continue;     // 포켓몬 이름인 카드
    if (family === 'pokemon_form') flags.push('폼 이름 — 공식 폼 이름이 있으면 그것');
    if (family === 'trainer_class') flags.push(`Pt · HGSS · BW · B2W2 원작 자산에 같은 직업 없음(${info.classStatus.get(key)}) — 후속작 공식 직업명이 있으면 그것`);
    if (family === 'move_name') flags.push('이 게임만의 기술 — 공식 이름 없음');
    if (family === 'fixture_name' || family === 'item_name') flags.push('공식 이름이 있으면 그것');
    const a = info.authored.get(key);
    if (a) flags.push(a.src === 'translated' ? '이미 우리 번역(strings-ko.json)' : `strings-ko.json ${a.src}`);
    out.set(key, { family, flag: flags.join(' · ') });
  }
  return out;
}

/** 새 번역이 용어 규칙(terms-ko.json — 옛 글에는 바꿔 넣는 규칙)에 걸리는가 → 걸린 규칙 목록 */
export function termHits(text, terms) {
  return terms.filter(t => text.replace(t.re, t.to) !== text);
}

/** 짧은 문구(해금 카드 능력 칸): 새 전체 문구를 옮긴 능력이면 새 .short ?? 새 전체 문구(48자 이하) — 아니면 null(옛 길) */
export function newShort(re, abilityKey, errors) {
  const text = re.get(`${abilityKey}.text`), short = re.get(`${abilityKey}.short`);
  if (!text) {
    if (short) errors.push(`${short.file} ${abilityKey}.short: 전체 문구(${abilityKey}.text)를 옮기지 않았는데 짧은 문구만 있다`);
    return null;
  }
  const s = short ? short.ko : text.ko;
  if (s.length > SHORT_MAX) errors.push(`${(short || text).file} ${abilityKey}: ${short ? '짧은 문구' : '전체 문구'} ${s.length}자 — 48자를 넘으면 ${abilityKey}.short를 쓴다`);
  return s;
}

/**
 * 손 파일 검사 + 상태 → { rows: 보고서 행, done, old, summary }. en = 최종 영어 표, ko = 최종 한국어 표(새 번역이 들어간 뒤).
 */
export function checkRetranslation(re, scope, en, ko, terms, strict, errors) {
  for (const [key, r] of re) {
    const at = `${r.file} ${key}`;
    const shortOf = /^(trainer_ability\.[^.]+\.[12])\.short$/.exec(key);
    if (!(key in en)) { errors.push(`${at}: 빌드가 만드는 키가 아니다`); continue; }
    if (!scope.has(key) && !(shortOf && scope.has(`${shortOf[1]}.text`))) errors.push(`${at}: 다시 옮길 범위 밖(공식 이름 · 이미 우리 글 — lib/retranslate.mjs)`);
    if (r.en !== en[key]) errors.push(`${at}: en 「${r.en}」이 지금 영문 원본 「${en[key]}」과 다르다(원본이 바뀌었으면 다시 옮긴다)`);
    for (const t of termHits(r.ko, terms)) errors.push(`${at}: 용어 규칙 ${t.ref} — /${t.re.source}/ → 「${t.to}」(새 번역은 바꿔 넣지 않는다, 그 말로 쓴다): 「${r.ko}」`);
  }
  const rows = [];
  let done = 0;
  const missing = [];
  for (const [key, s] of scope) {
    const isNew = re.has(key);
    if (isNew) done++; else missing.push(key);
    rows.push([key, s.family, isNew ? 'new' : 'old', s.flag, en[key], ko[key]]);
  }
  if (strict) for (const key of missing) errors.push(`다시 옮김: ${key}(${scope.get(key).family})를 아직 옮기지 않았다`);
  const byFamily = {};
  for (const [, s] of scope) byFamily[s.family] = (byFamily[s.family] || 0) + 1;
  const summary = `다시 옮김(결정 163): 범위 ${scope.size}키(${FAMILY_NAMES.filter(f => byFamily[f]).map(f => `${f} ${byFamily[f]}`).join(' · ')}) — 새 번역 ${done} · 옛 원천 ${missing.length}${strict ? '' : '(검사 끔)'} → reports/retranslate-ko.tsv`;
  return { rows, done, old: missing.length, summary };
}

/** 자기 시험(build.mjs --self-test) → 틀린 경우 설명 목록 */
export function selfTestRetranslate() {
  const bad = [];
  const terms = [{ re: /전투(?! 피로)/g, to: '배틀', ref: 't1' }, { re: /피해/g, to: '데미지', ref: 't2' }];
  const hits = s => termHits(s, terms).length;
  if (hits('배틀에서 데미지 2') !== 0) bad.push('용어: 맞는 글을 잡았다');
  if (hits('전투 피로 1') !== 0) bad.push('용어: 「전투 피로」(정한 말)를 잡았다');
  if (hits('전투에서 피해 2') !== 2) bad.push('용어: 옛 말 둘을 다 잡지 못했다');
  const re = new Map([['a', { en: 'A', ko: '가', file: 'f' }], ['b', { en: 'B2', ko: '나', file: 'f' }], ['z', { en: 'Z', ko: '자', file: 'f' }]]);
  const scope = new Map([['a', { family: 'ui', flag: '' }], ['b', { family: 'ui', flag: '' }], ['c', { family: 'ui', flag: '' }]]);
  const en = { a: 'A', b: 'B', c: 'C', z: 'Z' }, ko = { a: '가', b: '나', c: '다', z: '자' };
  const e1 = [];
  const r1 = checkRetranslation(re, scope, en, ko, terms, false, e1);
  if (e1.length !== 2) bad.push(`검사: 오류 ${e1.length}건(기대 2 — en 다름 · 범위 밖)`);
  if (r1.done !== 2 || r1.old !== 1) bad.push(`검사: 새 ${r1.done} · 옛 ${r1.old}(기대 2 · 1)`);
  const e2 = [];
  checkRetranslation(re, scope, en, ko, terms, true, e2);
  if (e2.length !== 3) bad.push(`검사(빠진 키): 오류 ${e2.length}건(기대 3 — 빠진 키 더함)`);
  const e3 = [];
  const long = 'ㄱ'.repeat(49);
  if (newShort(new Map([['t.1.text', { en: '', ko: long, file: 'f' }]]), 't.1', e3) !== long || e3.length !== 1) bad.push('짧은 문구: 48자 넘는 전체 문구를 잡지 못했다');
  const e4 = [];
  if (newShort(new Map([['t.1.text', { en: '', ko: long, file: 'f' }], ['t.1.short', { en: '', ko: '짧게', file: 'f' }]]), 't.1', e4) !== '짧게' || e4.length) bad.push('짧은 문구: .short를 쓰지 않았다');
  const e5 = [];
  if (newShort(new Map(), 't.1', e5) !== null || e5.length) bad.push('짧은 문구: 옮기지 않은 능력은 옛 길(null)');
  return bad;
}
