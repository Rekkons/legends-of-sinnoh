// xlsx(sinnoh_cube) → pokemon·moves·trainer_cards 행(03 §1-2·§1-3). 영문 문구는 xlsx 원문(결정 34), 앞뒤 공백만 뺀다.
// 값 정규화: xlsx 소문자 snake_case → PascalCase enum 이름(00 §5-1). 런타임 로더는 엄격하게 읽는다.
import { readXlsx, sheetRows } from './xlsx.mjs';

// Sinnoh.Rules.PokemonType 이름(TypeChart, raw/lua/_global.lua 순서)
export const TYPES = ['Typeless', 'Normal', 'Fire', 'Water', 'Electric', 'Grass', 'Ice', 'Fighting', 'Poison', 'Ground',
  'Flying', 'Psychic', 'Bug', 'Rock', 'Ghost', 'Dragon', 'Dark', 'Steel', 'Fairy'];
// Sinnoh.Rules.Biome·Climate 중 포켓몬 카드에 붙는 값(Ruins·Noble·Space는 카드 태그에만 있고 칸에는 없다)
const BIOMES = ['Plains', 'Forest', 'Mountain', 'Ocean', 'Ruins'];
const CLIMATES = ['Cold', 'Temperate', 'Warm', 'Noble', 'Space'];
const TIERS = ['Starter', 'Weak', 'Moderate', 'Strong', 'Legendary', 'UltraBeast', 'UltraBurst', 'Grunt', 'Commander', 'Boss'];
const TRAINERS = ['FemaleGrunt', 'MaleGrunt', 'Jupiter', 'Mars', 'Saturn', 'Cyrus'];

const POKEMON_COLS = ['pokedex_number', 'pokedex_name', 'type_1', 'type_2', 'hp', 'atk', 'def', 'sp_atk', 'sp_def', 'spd',
  'classification', 'biome', 'climate', '_initiative', '_health', 'i_mod', 'h_mod', 'initiative', 'health', 'total',
  'move_1', 'move_2', 'move_3', 'move_4', 'evolve_into', 'evolve_cost', 'internal_name', 'description', 'move_name',
  'move_type', 'move_attack_strength', 'move_effect', 'is_starter', 'is_legendary', 'encounter_tier', 'trainer',
  'number_in_deck', 'uncapturable', 'state'];
const MOVE_COLS = ['move_name', 'move_type', 'move_power', 'move_attack_strength', 'move_effect', 'count', 'min_total', 'max_total', 'variance'];
const TRAINER_COLS = ['trainer_class', 'ability_1_name', 'ability_1_description', 'ability_2_name', 'ability_2_description'];

const pascal = s => s.split('_').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join('');

/** 03 §1-2: 영문 이름 → snake_case 키. é→e(발음 구별 기호 삭제), 따옴표 삭제, &→and. */
export function snakeKey(name) {
  return name.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase()
    .replace(/['"’]/g, '').replace(/&/g, ' and ').replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
}

// 데이터 문구에 넣지 않는 글자: 제어 문자와 U+0085·U+2028·U+2029(Newtonsoft와 JSON.stringify의 이스케이프가 갈려 왕복 바이트가 달라진다)
export const BAD_TEXT = new RegExp(`[\x00-\x1f${String.fromCharCode(0x85, 0x2028, 0x2029)}]`);

export function buildCube(xlsxPath, errors) {
  const sheets = readXlsx(xlsxPath);
  const ctx = (sheet, row) => `xlsx ${sheet}:${row._row}`;
  const text = (at, s) => { if (BAD_TEXT.test(s)) errors.push(`${at} 제어 문자: ${JSON.stringify(s)}`); return s.trim(); };
  const int = (at, s, what) => (/^-?\d+$/.test(s) ? Number(s) : (errors.push(`${at} ${what} 정수가 아님: "${s}"`), 0));
  const intOrQ = (at, s, what) => (s === '?' ? null : int(at, s, what)); // 「?」 = 카드에 ?로 인쇄(효과가 정함)
  const bit = (at, s, what) => (s === '1' ? true : s === '0' ? false : (errors.push(`${at} ${what} 0/1이 아님: "${s}"`), false));
  const oneOf = (at, v, list, what) => (list.includes(v) ? v : (errors.push(`${at} 모르는 ${what}: "${v}"`), v));
  const type = (at, s) => oneOf(at, pascal(s), TYPES, '타입');

  // ── moves ──
  const moves = sheetRows(sheets, 'moves', MOVE_COLS).map(r => {
    const at = ctx('moves', r);
    return {
      key: snakeKey(r.move_name),
      name: text(at, r.move_name),
      type: type(at, r.move_type),
      power: intOrQ(at, r.move_power, 'move_power'),
      attackStrength: intOrQ(at, r.move_attack_strength, 'move_attack_strength'),
      maxTotal: int(at, r.max_total, 'max_total'),
      en: text(at, r.move_effect),
    };
  });
  const moveByName = new Map(moves.map(m => [m.name, m]));

  // ── pokemon ──
  const raw = sheetRows(sheets, 'pokemon', POKEMON_COLS);
  // 03 §1-2 키 <dex>:<state>. xlsx 접미사(479-f)는 버린다. 갤럭시단 카드(trainer 열)는 같은 종의 야생 카드와 다른 정의라
  // @<trainer>를 붙인다(198:0@female_grunt). 그래도 겹치면(동쪽 바다 422-e·423-e는 state가 서쪽과 같은 0) 접미사를 남긴다.
  const parts = raw.map(r => {
    const m = /^(\d+)(?:-([a-z]+))?$/.exec(r.pokedex_number);
    if (!m) errors.push(`${ctx('pokemon', r)} pokedex_number 형식: "${r.pokedex_number}"`);
    return { dex: m ? Number(m[1]) : 0, suffix: m && m[2] ? m[2] : null };
  });
  const baseKey = (r, i, withSuffix) =>
    `${parts[i].dex}${withSuffix && parts[i].suffix ? '-' + parts[i].suffix : ''}:${r.state}${r.trainer ? '@' + r.trainer : ''}`;
  const seen = new Map();
  raw.forEach((r, i) => { const k = baseKey(r, i, false); seen.set(k, (seen.get(k) || 0) + 1); });
  const keys = raw.map((r, i) => baseKey(r, i, seen.get(baseKey(r, i, false)) > 1));

  // 진화 대상: evolve_into의 이름(여럿이면 /)은 야생 카드 행(state 0·1 = 덱에 들어가는 카드, 2 이상은 그 카드의 다른 면)의 internal_name
  const evolveTarget = new Map();
  raw.forEach((r, i) => {
    if (r.trainer || !(r.state === '0' || r.state === '1')) return;
    if (evolveTarget.has(r.internal_name)) evolveTarget.set(r.internal_name, null); // 모호
    else evolveTarget.set(r.internal_name, keys[i]);
  });

  const pokemon = raw.map((r, i) => {
    const at = ctx('pokemon', r);
    const sig = moveByName.get(r.move_name);
    if (!sig) errors.push(`${at} 시그니처 기술 "${r.move_name}"이 moves 시트에 없다`);
    else {
      // 시그니처 기술의 정본은 moves 시트(원작 카드 뒷면도 moves 시트로 그린다, card_generator generate_moves.py)
      if (pascal(r.move_type) !== sig.type) errors.push(`${at} ${r.move_name} 타입 ${r.move_type} ≠ moves 시트 ${sig.type}`);
      if (intOrQ(at, r.move_attack_strength, 'move_attack_strength') !== sig.attackStrength)
        errors.push(`${at} ${r.move_name} 공격력 ${r.move_attack_strength} ≠ moves 시트 ${sig.attackStrength}`);
      // pokemon 행 move_effect가 빈 11행(끝 공백이 있는 moves 시트 문구 6종)은 moves 시트 문구를 쓴다
      const effect = r.move_effect.trim();
      if (effect !== '' && effect !== sig.en) errors.push(`${at} ${r.move_name} 효과 문구가 moves 시트와 다르다`);
    }
    const evolveInto = r.evolve_into ? r.evolve_into.split('/').map(n => {
      const k = evolveTarget.get(n);
      if (!k) errors.push(`${at} 진화 대상 "${n}"이 ${k === null ? '여러 카드 행과 맞는다' : '야생 카드 행에 없다'}`);
      return k || n;
    }) : [];
    if (!!r.evolve_into !== !!r.evolve_cost) errors.push(`${at} evolve_into와 evolve_cost 중 하나만 있다`);
    const state = int(at, r.state, 'state');
    return {
      key: keys[i],
      dex: parts[i].dex,
      state,
      name: text(at, r.internal_name),
      types: [r.type_1, r.type_2].filter(Boolean).map(t => type(at, t)),
      health: int(at, r.health, 'health'),
      initiative: int(at, r.initiative, 'initiative'),
      biome: r.biome ? oneOf(at, r.biome, BIOMES, 'biome') : null,
      climate: r.climate ? oneOf(at, r.climate, CLIMATES, 'climate') : null,
      learnTypes: [r.move_1, r.move_2, r.move_3, r.move_4].filter(Boolean).map(t => type(at, t)),
      evolveInto,
      evolveCost: r.evolve_cost ? int(at, r.evolve_cost, 'evolve_cost') : null,
      signature: sig ? sig.key : snakeKey(r.move_name),
      tier: oneOf(at, pascal(r.encounter_tier), TIERS, 'encounter_tier'),
      trainer: r.trainer ? oneOf(at, pascal(r.trainer), TRAINERS, 'trainer') : null,
      copies: int(at, r.number_in_deck, 'number_in_deck'),
      uncapturable: bit(at, r.uncapturable, 'uncapturable'),
      legendary: bit(at, r.is_legendary, 'is_legendary'),
      starter: bit(at, r.is_starter, 'is_starter'),
    };
  });

  // ── trainer_cards ──  능력 1 = 파랑(실버에서 해금, 카드 왼쪽), 능력 2 = 빨강(골드에서 해금, 오른쪽). trainer-cards.md §1, generate_trainer_cards.py
  const trainerCards = sheetRows(sheets, 'trainer_cards', TRAINER_COLS).map(r => {
    const at = ctx('trainer_cards', r);
    return {
      key: snakeKey(r.trainer_class),
      class: text(at, r.trainer_class),
      abilities: [
        { slot: 1, color: 'Blue', name: text(at, r.ability_1_name), en: text(at, r.ability_1_description) },
        { slot: 2, color: 'Red', name: text(at, r.ability_2_name), en: text(at, r.ability_2_description) },
      ],
    };
  });

  // 종 영문 이름(pokedex_name, 결정 34 영어 = xlsx 원문): 도감 번호 → 이름. 행마다 같아야 한다(strings.en species.<dex>)
  const species = new Map();
  raw.forEach((r, i) => {
    const d = parts[i].dex, name = text(ctx('pokemon', r), r.pokedex_name);
    if (species.has(d) && species.get(d) !== name) errors.push(`${ctx('pokemon', r)} 도감 ${d}의 pokedex_name이 "${species.get(d)}"와 다르다: "${name}"`);
    species.set(d, name);
  });

  return { pokemon, moves, trainerCards, species };
}

/** 03 §1-4 검증(pokemon·moves·trainer_cards). 틀리면 errors에 더한다. */
export function validateCube({ pokemon, moves, trainerCards }, errors) {
  const expect = (what, got, want) => { if (got !== want) errors.push(`${what}: ${got}(기대 ${want})`); };
  const sum = (rows, f) => rows.reduce((a, r) => a + f(r), 0);
  const dupes = keys => keys.filter((k, i) => keys.indexOf(k) !== i);

  expect('pokemon 행', pokemon.length, 390);
  expect('pokemon 장수 합', sum(pokemon, r => r.copies), 633);
  const species = new Set(pokemon.map(r => r.dex));
  expect('pokemon 종(도감 번호)', species.size, 322);
  expect('pokemon #650 이상 종', [...species].filter(d => d >= 650).length, 39);
  const tierWant = { Strong: 234, Moderate: 168, Weak: 85, Legendary: 84, Starter: 18, Commander: 15, UltraBeast: 9, Boss: 8, Grunt: 8, UltraBurst: 4 };
  for (const [tier, want] of Object.entries(tierWant)) expect(`등급 ${tier} 장수`, sum(pokemon.filter(r => r.tier === tier), r => r.copies), want);
  expect('moves 행', moves.length, 236);
  expect('trainer_cards 행', trainerCards.length, 51);
  expect('trainer_cards 능력', sum(trainerCards, c => c.abilities.length), 102);

  // 참조 무결성
  for (const [what, keys] of [['pokemon', pokemon.map(r => r.key)], ['moves', moves.map(r => r.key)], ['trainer_cards', trainerCards.map(r => r.key)]]) {
    const d = dupes(keys);
    if (d.length) errors.push(`${what} 키 중복: ${[...new Set(d)].join(', ')}`);
    for (const k of keys) if (!k) errors.push(`${what} 빈 키`);
  }
  const pk = new Set(pokemon.map(r => r.key)), mk = new Set(moves.map(r => r.key));
  for (const r of pokemon) {
    for (const k of r.evolveInto) if (!pk.has(k)) errors.push(`pokemon ${r.key} 진화 대상 ${k}이 없다`);
    if (!mk.has(r.signature)) errors.push(`pokemon ${r.key} 시그니처 ${r.signature}이 moves에 없다`);
    if (r.types.length < 1 || r.types.length > 2) errors.push(`pokemon ${r.key} 타입 ${r.types.length}개`);
    for (const t of [...r.types, ...r.learnTypes]) if (!TYPES.includes(t)) errors.push(`pokemon ${r.key} 타입 ${t}이 TypeChart에 없다`);
    if ((r.trainer !== null) !== ['Grunt', 'Commander', 'Boss'].includes(r.tier)) errors.push(`pokemon ${r.key} 갤럭시단 등급 ${r.tier}과 trainer ${r.trainer}가 맞지 않다`);
    if (r.copies < 1) errors.push(`pokemon ${r.key} 장수 ${r.copies}`);
  }
  for (const m of moves) if (!TYPES.includes(m.type)) errors.push(`moves ${m.key} 타입 ${m.type}이 TypeChart에 없다`);
}
