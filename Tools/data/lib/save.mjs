// 한국어판 세이브(raw/sinnoh-ko-1.4a.json)와 원작 영문 세이브(raw/orig-save, 같은 v1.4a)에서 보드·고정물·덱·이미지 카드·아이템을 뽑는다
// (03 §1-1·§1-3, plan M2). 영문 이름은 원작 세이브 Nickname(GUID로 맞춘다, 결정 34), 한국어 이름은 한국어판 세이브 Nickname.
//   buildCells     헥스 프리즘 634개 → map_cells.csv(10-30 cells_csv.js·flat.js를 그대로 옮김) + 칸 덮개 틴트·디퓨즈
//   buildItems     숨겨진 장소 35·유물 토큰 17·소지 아이템·볼(뼈대: key·kind·copies)
//   buildFixtures  맵 고정물 174 → 가장 가까운 칸
//   buildDecks     덱 17(749장 + 대체 상태 179) + 이미지 전용 카드 뼈대(고유 앞면 54)
import { readFileSync } from 'node:fs';
import { snakeKey } from './cube.mjs';

export const readSave = file => JSON.parse(readFileSync(file, 'utf8'));

const has = (o, tag) => (o.Tags || []).includes(tag);
const tsv = (head, rows) => [head, ...rows].map(r => r.join('\t')).join('\n') + '\n';
const expect = (errors, what, got, want) => { if (got !== want) errors.push(`${what}: ${got}(기대 ${want})`); };
const countBy = (rows, f) => rows.reduce((m, r) => { const k = f(r); m[k] = (m[k] || 0) + 1; return m; }, {});
const sum = (rows, f) => rows.reduce((a, r) => a + f(r), 0);

/** 최상위 GUID → 원작 영문 세이브 오브젝트. 두 세이브 모두 최상위 GUID가 겹치지 않아야 한다. */
export function englishIndex(save, saveEn, errors) {
  for (const [what, s] of [['한국어판', save], ['영문', saveEn]]) {
    const seen = new Set();
    for (const o of s.ObjectStates) { if (seen.has(o.GUID)) errors.push(`${what} 세이브 최상위 GUID 겹침: ${o.GUID}`); seen.add(o.GUID); }
  }
  return new Map(saveEn.ObjectStates.map(o => [o.GUID, o]));
}

// ── 보드 칸 ──

const HEX_MESH = 'PMjCsZFs'; // 헥스 프리즘 OBJ(pastebin) — 칸 조각 634개가 같이 쓴다(inventory §1.1)
const CELL_BIOMES = ['Plains', 'Forest', 'Mountain', 'Ocean'];
const CELL_CLIMATES = ['Cold', 'Temperate', 'Warm'];
export const CSV_COLS = ['col', 'row', 'x', 'z', 'level', 'biome', 'climate', 'wild_area', 'region', 'distortion_world', 'pieces', 'top_scale_y'];
// 덮개 디퓨즈: Steam CDN ugc 번호 → 원작 GitHub assets/bases/tile_textures 이름. 숲 3종은 그 텍스처를 쓰는 칸의 지형·기후로 가렸고
// (빌드가 다시 확인한다), Secret은 남은 하나(깨어진 세계 -10,24 한 칸, 파일 secret.png) [추정]
export const DIFFUSE = {
  '1696153270885445957': 'Standard',
  '1822281577238457338': 'ForestCold',
  '1696153071110716419': 'ForestTemperate',
  '1822281577238474124': 'ForestWarm',
  '1826776884169179226': 'Secret',
};
export const DIFFUSES = Object.values(DIFFUSE);

/**
 * 10-30 job-e4eed7cf/flat.js + cells_csv.js를 그대로 옮겼다(map_cells.csv를 세이브에서 다시 만든다, plan M2).
 * flat.js가 위치를 소수 2자리·크기를 3자리로 반올림한 뒤 계산했으므로 같은 반올림을 한다(바이트가 같아야 한다).
 * → { csv: map_cells.csv 내용, looks: Map<"col,row", {tint, diffuse, pillars}> } (덮개 = 가장 높은 조각의 틴트 "#rrggbbaa"·디퓨즈 이름,
 *   pillars = 덮개 밑 기둥 조각 틴트 아래 → 위)
 */
export function buildCells(save, errors) {
  const r2 = v => (v == null ? null : +v.toFixed(2)), r3 = v => (v == null ? null : +v.toFixed(3));
  const pieces = save.ObjectStates
    .filter(o => o.Name === 'Custom_Model' && o.CustomMesh && o.CustomMesh.MeshURL.includes(HEX_MESH))
    .map(o => {
      const t = o.Transform;
      return { o, pos: [r2(t.posX), r2(t.posY), r2(t.posZ)], scl: [r3(t.scaleX), r3(t.scaleY), r3(t.scaleZ)], tags: o.Tags || [], desc: o.Description || '' };
    });
  expect(errors, '헥스 프리즘 조각', pieces.length, 634);

  const cells = {};
  for (const p of pieces) { const k = Math.round(p.pos[0] / 1.65) + ',' + Math.round(p.pos[2] / 0.9525); (cells[k] = cells[k] || []).push(p); }
  const topY = p => p.pos[1] + 0.576737 * p.scl[1]; // 메시 높이 0.577 × scaleY
  const rows = [];
  for (const [k, v] of Object.entries(cells)) {
    const top = v.reduce((a, b) => (topY(a) > topY(b) ? a : b));
    const dw = v.some(p => p.tags.includes('Distortion World'));
    const y = topY(top) + (dw ? 5 : 0); // 깨어진 세계는 5 아래에 숨겨져 있다(Setup Panel이 올린다)
    const tags = v.flatMap(p => p.tags);
    const [col, row] = k.split(',').map(Number);
    rows.push({
      col, row, x: top.pos[0], z: top.pos[2], level: Math.round((y - 0.998) / 0.7798 * 4) / 4,
      biome: CELL_BIOMES.find(b => tags.includes(b)) || (tags.includes('World Token') ? '(lake/summit)' : ''),
      climate: CELL_CLIMATES.find(c => tags.includes(c)) || '',
      wild_area: tags.includes('Wild Area') ? 1 : 0,
      region: (v.find(p => p.desc) || {}).desc || '',
      distortion_world: dw ? 1 : 0, pieces: v.length, top_scale_y: top.scl[1], top,
    });
  }
  rows.sort((a, b) => a.distortion_world - b.distortion_world || a.col - b.col || a.row - b.row);
  const esc = v => (/[,"]/.test(String(v)) ? '"' + String(v).replace(/"/g, '""') + '"' : String(v));
  const csv = '﻿' + CSV_COLS.join(',') + '\n' + rows.map(r => CSV_COLS.map(c => esc(r[c])).join(',')).join('\n') + '\n';

  const looks = new Map();
  for (const r of rows) {
    const c = r.top.o.ColorDiffuse, at = `칸 ${r.col},${r.row}`;
    const byte = v => { const n = Math.round(v * 255); if (!(n >= 0 && n <= 255)) errors.push(`${at} 틴트 값 ${v}`); return n.toString(16).padStart(2, '0'); };
    const hex = o => '#' + [o.r, o.g, o.b, o.a === undefined ? 1 : o.a].map(byte).join('');
    const tint = hex(c);
    // 덮개 밑 기둥 조각(scaleY 2, 두께 0.78)의 틴트, 아래 → 위(M4-2b, d3-3 §2-1). 호수 덮개(scaleY 1.5) 밑 얇은 조각(0.1)만 빼고 다른 조각은 없어야 한다
    const below = cells[`${r.col},${r.row}`].filter(p => p !== r.top).sort((a, b) => topY(a) - topY(b));
    const pillars = [];
    for (const p of below) {
      if (p.scl[1] === 2) pillars.push(hex(p.o.ColorDiffuse));
      else if (!(p.scl[1] === 0.1 && r.top.scl[1] === 1.5 && p === below[0])) errors.push(`${at} 덮개 밑 조각 scaleY ${p.scl[1]}(기둥 2·호수 밑 0.1만)`);
    }
    const ugc = (/\/ugc\/(\d+)\//.exec(r.top.o.CustomMesh.DiffuseURL) || [])[1];
    const diffuse = DIFFUSE[ugc];
    if (!diffuse) errors.push(`${at} 모르는 디퓨즈 ${r.top.o.CustomMesh.DiffuseURL}`);
    else if (diffuse.startsWith('Forest') && diffuse !== 'Forest' + r.climate) errors.push(`${at} 디퓨즈 ${diffuse}인데 지형·기후 ${r.biome}/${r.climate}`);
    else if (!diffuse.startsWith('Forest') && r.biome === 'Forest') errors.push(`${at} 숲 칸인데 디퓨즈 ${diffuse}`);
    looks.set(`${r.col},${r.row}`, { tint, diffuse, pillars });
  }
  return { csv, looks };
}

// ── 아이템(뼈대) ──

const STRONG_TAG = 'Strong Encounter Card';

/**
 * 세이브에 실물이 있는 아이템만(뼈대, 03 §1-3 items). 효과·레시피·전투 아이템·스튜·트레이너 카드 아이템은 M5·M6.
 *   HiddenSpot        Hidden Token 35(셋업 때 야생 칸에 뒤집어 놓는다): 떨어진 별·야영지·규토리 더미 7색
 *   Artifact·CelestialArtifact·AlphaPokemon   Artifact Token 17(같은 방식): 설명 첫 줄 "Artifact"·"Celestial Artifact", 우두머리 = 강함 조우 태그
 *   Held              무한 가방 소지 아이템 6(제작) + 신오의 심장·수호신의 서 각 6
 *   Ball              무한 가방 볼 4
 * copies = 세이브 속 개수, 무한 가방이면 null. → { rows: [{key, kind, copies}], names: [{key, en, ko}] }
 */
export function buildItems(save, enBy, errors) {
  const found = new Map(); // key → {key, kind, copies, en, ko}
  const add = (kind, en, ko, copies, at) => {
    const prefix = kind === 'HiddenSpot' ? 'hidden_spot' : 'item';
    const key = `${prefix}.${snakeKey(en)}`;
    const cur = found.get(key);
    if (!cur) { found.set(key, { key, kind, copies, en, ko }); return; }
    if (cur.kind !== kind || cur.en !== en || cur.ko !== ko || cur.copies === null || copies === null) errors.push(`${at}: 아이템 ${key}가 다르게 두 번 나온다`);
    else cur.copies += copies;
  };
  save.ObjectStates.forEach((o, i) => {
    const at = `세이브 T${i} ${o.Nickname}`;
    const e = enBy.get(o.GUID);
    if (o.Name === 'Infinite_Bag') {
      const inner = (o.ContainedObjects || [])[0], innerEn = e && (e.ContainedObjects || [])[0];
      if (!inner) return;
      const kind = has(inner, 'Held Item') ? 'Held' : has(inner, 'Ball Token') ? 'Ball' : null;
      if (!kind) return;
      if (!innerEn) { errors.push(`${at}: 영문 세이브에 같은 가방이 없다`); return; }
      add(kind, innerEn.Nickname, inner.Nickname, null, at);
      return;
    }
    if (!e && (has(o, 'Hidden Token') || has(o, 'Artifact Token') || has(o, 'Held Item'))) { errors.push(`${at}: 영문 세이브에 없다`); return; }
    if (has(o, 'Hidden Token')) add('HiddenSpot', e.Nickname, o.Nickname, 1, at);
    else if (has(o, 'Artifact Token')) {
      const first = (e.Description || '').split('\n')[0].trim();
      const kind = has(o, STRONG_TAG) ? 'AlphaPokemon' : first === 'Celestial Artifact' ? 'CelestialArtifact' : first === 'Artifact' ? 'Artifact' : null;
      if (!kind) { errors.push(`${at}: 유물 토큰 종류를 모른다(설명 첫 줄 "${first}")`); return; }
      add(kind, e.Nickname, o.Nickname, 1, at);
    } else if (has(o, 'Held Item') && o.Name === 'Custom_Tile') add('Held', e.Nickname, o.Nickname, 1, at);
  });
  const KIND_ORDER = ['HiddenSpot', 'AlphaPokemon', 'Artifact', 'CelestialArtifact', 'Held', 'Ball'];
  const all = [...found.values()].sort((a, b) => KIND_ORDER.indexOf(a.kind) - KIND_ORDER.indexOf(b.kind) || (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));

  // 03 §1-4 셋업 토큰: 숨겨진 장소 35(별 8·야영지 6·더미 21), 유물 토큰 17(우두머리 6·유물 8·천상 3)
  const copiesOf = kind => sum(all.filter(r => r.kind === kind), r => r.copies);
  expect(errors, '숨겨진 장소 토큰', copiesOf('HiddenSpot'), 35);
  expect(errors, '떨어진 별', (found.get('hidden_spot.fallen_star') || {}).copies, 8);
  expect(errors, '야영지', (found.get('hidden_spot.campsite') || {}).copies, 6);
  const stashes = all.filter(r => /^hidden_spot\.[a-z]+_apricorn_stash$/.test(r.key));
  expect(errors, '규토리 더미 색 수', stashes.length, 7);
  for (const r of stashes) expect(errors, `${r.key} 장수`, r.copies, 3);
  expect(errors, '유물 토큰', copiesOf('AlphaPokemon') + copiesOf('Artifact') + copiesOf('CelestialArtifact'), 17);
  expect(errors, '우두머리 포켓몬 토큰', copiesOf('AlphaPokemon'), 6);
  expect(errors, '유물', copiesOf('Artifact'), 8);
  expect(errors, '천상의 유물', copiesOf('CelestialArtifact'), 3);
  return {
    rows: all.map(r => ({ key: r.key, kind: r.kind, copies: r.copies })),
    names: all.map(r => ({ key: r.key, en: r.en, ko: r.ko })),
  };
}

// ── 고정물 ──

const APRICORNS = ['Red', 'Blue', 'Yellow', 'Pink', 'Green', 'White', 'Black'];
// rulings A8: 산호가 지키는 규토리 3개 = 토큰 그림(약함 빨강·파랑·노랑, 보통 분홍·초록·하양, 강함 검정 3)
const REEF_APRICORNS = { Weak: ['Red', 'Blue', 'Yellow'], Moderate: ['Pink', 'Green', 'White'], Strong: ['Black', 'Black', 'Black'] };
const TIER_TAGS = { 'Weak Encounter Card': 'Weak', 'Moderate Encounter Card': 'Moderate', 'Strong Encounter Card': 'Strong' };
export const FIXTURE_KINDS = ['Village', 'ApricornTree', 'ApricornReef', 'DistortedTree', 'LegendarySite', 'LegendaryShrine', 'Shrine', 'Gate', 'Taxi'];
const BIRD_GUID = '527326'; // 원작 config.ttslua guids.BIRD: 공중날기 때 장막시티에서 날아오르는 찌르호크(평소 숨김)
const MAX_DISTANCE = 0.5; // 칸 중심에서 이만큼 안이어야 붙인다(헥스 안쪽 반지름 약 0.95)

export function fixtureKind(o, e) {
  if (o.Name === 'Custom_Assetbundle') return o.GUID === BIRD_GUID ? 'Taxi' : null;
  if (has(o, 'Village Token')) return 'Village';
  if (has(o, 'Apricorn Tree Token')) return e.Nickname === 'Apricorn Reef' ? 'ApricornReef' : 'ApricornTree';
  if (has(o, 'Distortion Gate')) return 'Gate';
  if (has(o, 'Distortion Encounter Card') && has(o, 'World Token')) return 'DistortedTree';
  if (has(o, 'Legendary Encounter Card') && o.Name === 'Custom_Token') return 'LegendarySite';
  if (has(o, 'Shrine Token')) return 'Shrine';
  // 태그가 World Token(+ Distortion World)뿐인 토큰: 전설 사당 7, 울트라메가로폴리스(깨어진 세계의 마을), 「The Ultra Burst」(아래 제외)
  if (o.Name === 'Custom_Token' && (o.Tags || []).every(t => t === 'World Token' || t === 'Distortion World')) {
    if (e.Nickname === 'Ultra Megalopolis') return 'Village';
    if (/ Shrine$/.test(e.Nickname)) return 'LegendaryShrine';
  }
  return null;
}

/**
 * board: board 행(col·row·x·z·distortion), pokemon: cube.pokemon, items: buildItems 결과,
 * authored: Tools/data/authored/fixtures.json(전설 사당 → 전설 영문 이름, 마을 → 고향 마을 카드의 스타터 두 마리, 세이브에 없는 것).
 * → { rows, names: [{key, en, ko}], report: fixtures-cells.tsv 내용 }
 */
export function buildFixtures(save, enBy, board, pokemon, items, authored, errors) {
  // 전설 영문 이름 → 전설 덱 카드 키(Legendary 등급·주인 없음·첫 면 state 0/1). 겹치면 오류
  const legendaryKey = (name, at) => {
    const c = pokemon.filter(p => p.tier === 'Legendary' && !p.trainer && p.state <= 1 && p.name === name);
    if (c.length !== 1) { errors.push(`${at}: 전설 "${name}"이 pokemon ${c.length}행과 맞는다`); return null; }
    return c[0].key;
  };
  const itemByEn = new Map(); // 영문 이름 → 아이템 키(유물)
  items.rows.forEach((r, i) => { if (r.kind === 'Artifact') itemByEn.set(items.names[i].en, r.key); });
  const shrineLegends = authored.legendaryShrines || {};
  const villageStarters = authored.villageStarters || {};
  // 스타터 영문 이름 → 스타터 덱 카드 키(Starter 등급·첫 면). 겹치면 오류
  const starterKey = (name, at) => {
    const c = pokemon.filter(p => p.tier === 'Starter' && p.state <= 1 && p.name === name);
    if (c.length !== 1) { errors.push(`${at}: 스타터 "${name}"이 pokemon ${c.length}행과 맞는다`); return null; }
    return c[0].key;
  };

  const found = [];
  save.ObjectStates.forEach((o, i) => {
    const e = enBy.get(o.GUID);
    const at = `세이브 T${i} ${o.Nickname || o.Name}`;
    if (!e) { if (has(o, 'World Token') || o.Name === 'Custom_Assetbundle') errors.push(`${at}: 영문 세이브에 없다`); return; }
    const kind = fixtureKind(o, e);
    if (!kind) return;
    const t = o.Transform;
    let cell = null, dist = Infinity;
    for (const c of board) { const d = Math.hypot(c.x - t.posX, c.z - t.posZ); if (d < dist) { dist = d; cell = c; } }
    if (dist > MAX_DISTANCE) errors.push(`${at}: 가장 가까운 칸 ${cell.key}까지 ${dist.toFixed(3)}(> ${MAX_DISTANCE})`);
    if (has(o, 'Distortion World') !== cell.distortion) errors.push(`${at}: 깨어진 세계 태그와 칸 ${cell.key}가 맞지 않다`);
    const tierTag = (o.Tags || []).map(x => TIER_TAGS[x]).filter(Boolean);
    found.push({ o, e, i, at, kind, cell, dist, tier: tierTag.length === 1 ? tierTag[0] : null, x: t.posX, z: t.posZ });
  });

  const cellIndex = new Map(board.map((c, i) => [c.key, i]));
  found.sort((a, b) => FIXTURE_KINDS.indexOf(a.kind) - FIXTURE_KINDS.indexOf(b.kind) || cellIndex.get(a.cell.key) - cellIndex.get(b.cell.key));
  const counters = {};
  const next = k => (counters[k] = (counters[k] || 0) + 1);
  const stripThe = s => s.replace(/^The /, '');
  for (const f of found) {
    const en = f.e.Nickname;
    f.apricorns = [];
    f.legendary = [];
    f.artifact = [];
    f.starters = [];
    switch (f.kind) {
      case 'Village': {
        f.key = `village.${snakeKey(en.replace(/ Village$/, ''))}`;
        const a = villageStarters[f.key];
        if (!a || a.length !== 2) { errors.push(`authored fixtures.json: ${f.key}의 스타터 두 마리가 없다`); break; }
        for (const n of a) { const k = starterKey(n, `authored ${f.key}`); if (k) f.starters.push(k); }
        break;
      }
      case 'ApricornTree': {
        const m = /^(\w+) Apricorn Tree$/.exec(en);
        if (!m || !APRICORNS.includes(m[1])) { errors.push(`${f.at}: 나무 색을 모른다 "${en}"`); f.key = `apricorn_tree.${snakeKey(en)}`; break; }
        f.apricorns = [m[1], m[1], m[1]];
        f.key = `apricorn_tree.${m[1].toLowerCase()}_${next(f.kind + m[1])}`;
        if (!f.tier) errors.push(`${f.at}: 조우 등급 태그가 하나가 아니다`);
        break;
      }
      case 'ApricornReef':
        if (!f.tier) { errors.push(`${f.at}: 조우 등급 태그가 하나가 아니다`); f.key = `apricorn_reef.${next(f.kind)}`; break; }
        f.apricorns = REEF_APRICORNS[f.tier].slice();
        f.key = `apricorn_reef.${f.tier.toLowerCase()}_${next(f.kind + f.tier)}`;
        break;
      case 'DistortedTree': f.key = `distorted_tree.${next(f.kind)}`; break;
      case 'LegendarySite': {
        f.key = `legendary_site.${snakeKey(stripThe(en))}`;
        const k = legendaryKey((f.e.GMNotes || '').trim(), f.at);
        if (k) f.legendary = [k];
        // 설명 "Requires: X" / "Requires either: X or Y Automata Core"(유물 토큰 이름은 "X Core")
        const req = /^Requires(?: either)?: (.+)$/m.exec(f.e.Description || '');
        if (req) {
          const text = req[1].trim();
          const names = /Automata Core$/.test(text) ? text.replace(/ Automata Core$/, '').split(' or ').map(s => `${s} Core`) : text.split(' or ');
          for (const n of names) {
            if (itemByEn.has(n)) f.artifact.push(itemByEn.get(n));
            else errors.push(`${f.at}: 요구 유물 "${n}"이 아이템에 없다`);
          }
        }
        break;
      }
      case 'LegendaryShrine': {
        f.key = `legendary_shrine.${snakeKey(en)}`;
        const a = shrineLegends[f.key];
        if (!a) { errors.push(`authored fixtures.json: ${f.key}의 전설이 없다`); break; }
        for (const n of a.legendary) { const k = legendaryKey(n, `authored ${f.key}`); if (k) f.legendary.push(k); }
        break;
      }
      case 'Shrine': f.key = `shrine.${snakeKey(en)}`; break;
      case 'Gate': f.key = `gate.${snakeKey(en.replace(/ Gate$/, ''))}_${has(f.o, 'Distortion World') ? 'distortion' : 'main'}`; break;
      case 'Taxi': f.key = 'taxi.bird'; break;
    }
  }
  for (const key of Object.keys(shrineLegends)) if (!found.some(f => f.key === key)) errors.push(`authored fixtures.json: 전설 사당 ${key}가 세이브에 없다`);
  for (const key of Object.keys(villageStarters)) if (!found.some(f => f.key === key && f.kind === 'Village')) errors.push(`authored fixtures.json: 마을 ${key}가 세이브에 없다`);
  // 스타터 18장(스타터 덱)이 마을 9곳에 한 번씩
  const starterKeys = found.flatMap(f => f.starters);
  expect(errors, '마을 스타터', new Set(starterKeys).size, 18);
  expect(errors, '스타터 카드', starterKeys.length, pokemon.filter(p => p.tier === 'Starter').length);
  // 게이트 짝: 같은 이름의 본맵 쪽(Ultra Beast Spawn)과 깨어진 세계 쪽
  for (const f of found) {
    f.pair = null;
    if (f.kind !== 'Gate') continue;
    const other = found.filter(g => g !== f && g.kind === 'Gate' && g.e.Nickname === f.e.Nickname);
    if (other.length !== 1 || has(other[0].o, 'Distortion World') === has(f.o, 'Distortion World')) errors.push(`${f.at}: 게이트 짝이 하나가 아니다`);
    else f.pair = other[0].key;
    if (has(f.o, 'Ultra Beast Spawn') === has(f.o, 'Distortion World')) errors.push(`${f.at}: 본맵 쪽 게이트만 Ultra Beast Spawn이어야 한다`);
  }

  // 03 §1-4: 마을 8+1, 나무·산호 118(약함 62·보통 44·강함 12), 왜곡된 나무 6, 전설 장소 12, 전설 사당 7, 사당 13, 게이트 8(4쌍), 택시 1 = 174
  const n = countBy(found, f => f.kind);
  expect(errors, '고정물', found.length, 174);
  expect(errors, '마을', n.Village, 9);
  expect(errors, '깨어진 세계 마을', found.filter(f => f.kind === 'Village' && f.cell.distortion).length, 1);
  const trees = found.filter(f => f.kind === 'ApricornTree' || f.kind === 'ApricornReef');
  expect(errors, '나무·산호', trees.length, 118);
  const byTier = countBy(trees, f => f.tier);
  expect(errors, '나무·산호 약함', byTier.Weak, 62);
  expect(errors, '나무·산호 보통', byTier.Moderate, 44);
  expect(errors, '나무·산호 강함', byTier.Strong, 12);
  expect(errors, '왜곡된 나무', n.DistortedTree, 6);
  expect(errors, '전설 장소', n.LegendarySite, 12);
  expect(errors, '전설 사당', n.LegendaryShrine, 7);
  expect(errors, '사당', n.Shrine, 13);
  expect(errors, '게이트', n.Gate, 8);
  expect(errors, '택시', n.Taxi, 1);
  const keys = found.map(f => f.key);
  const dup = keys.filter((k, i) => keys.indexOf(k) !== i);
  if (dup.length) errors.push(`고정물 키 겹침: ${[...new Set(dup)].join(', ')}`);

  const rows = found.map(f => ({
    key: f.key, kind: f.kind, cell: f.cell.key, tier: f.tier, apricorns: f.apricorns, legendary: f.legendary, artifact: f.artifact, starters: f.starters, pair: f.pair,
  }));
  // 이름이 하나씩 있는 고정물만 문자열 표에 넣는다(나무·산호·왜곡된 나무·택시는 이름이 종류뿐)
  const NAMED = ['Village', 'LegendarySite', 'LegendaryShrine', 'Shrine', 'Gate'];
  const names = found.filter(f => NAMED.includes(f.kind)).map(f => ({ key: f.key, en: f.e.Nickname, ko: f.o.Nickname }));
  // 고향 마을 카드용: 트레이너 토큰 GMNotes 색 → 마을 키(원작 토큰 이름 "<마을> Trainer")
  const villageByColor = new Map();
  save.ObjectStates.forEach((o, i) => {
    if (!has(o, 'Trainer Token')) return;
    const e = enBy.get(o.GUID);
    const key = e && `village.${snakeKey(e.Nickname.replace(/ Trainer$/, ''))}`;
    if (!key || !found.some(f => f.key === key) || villageByColor.has(o.GMNotes)) errors.push(`세이브 T${i} 트레이너 토큰 ${o.Nickname}: 마을·색(${o.GMNotes})을 붙이지 못했다`);
    else villageByColor.set(o.GMNotes, key);
  });
  expect(errors, '트레이너 토큰 색', villageByColor.size, 9);
  const report = tsv(['key', 'kind', 'cell', 'distance', 'x', 'z', 'cellX', 'cellZ', 'region', 'saveObject'],
    found.map(f => [f.key, f.kind, f.cell.key, f.dist.toFixed(3), f.x.toFixed(2), f.z.toFixed(2), f.cell.x, f.cell.z, f.cell.region || '', `T${f.i} ${f.o.GUID}`]));
  return { rows, names, report, villageByColor, maxDistance: Math.max(...found.map(f => f.dist)) };
}

// ── 덱·이미지 카드 ──

// 포켓몬 덱: 역할 태그 → 덱 키(inventory §1.4)
const POKEMON_DECKS = [
  ['Starter Card', 'starter'], ['Weak Encounter Card', 'weak'], ['Moderate Encounter Card', 'moderate'], ['Strong Encounter Card', 'strong'],
  ['Evolution Card', 'evolution'], ['Ultra Beast Encounter Card', 'legendary'],
  ['Galactic Grunt', 'galactic_grunt'], ['Galactic Commander', 'galactic_commander'], ['Galactic Boss', 'galactic_boss'],
];
// 이미지 전용 카드 덱: 태그 → 덱 키(울트라 온슬롯은 태그가 없어 영문 이름으로)
const CARD_DECKS = [['Act Card', 'act'], ['Village Card', 'home_village'], ['Shrine Card', 'shrine_event'], ['Change of Fate Card', 'change_of_fate'],
  ['Ginkgo Card', 'ginkgo_trade'], ['Breach Card', 'distortion_breach'], [null, 'ultra_onslaught']];
// 03 §1-4·inventory §1.4: 덱마다 장수(대체 상태)
const DECK_COUNTS = {
  starter: [18, 0], weak: [80, 0], moderate: [95, 4], strong: [98, 20], evolution: [202, 41], legendary: [32, 12],
  galactic_grunt: [8, 0], galactic_commander: [15, 0], galactic_boss: [8, 0], trainer_card: [51, 102],
  act: [5, 0], home_village: [9, 0], shrine_event: [35, 0], change_of_fate: [42, 0], ginkgo_trade: [9, 0], distortion_breach: [36, 0], ultra_onslaught: [6, 0],
};
const FACE_COUNTS = { act: 5, home_village: 9, shrine_event: 15, change_of_fate: 4, ginkgo_trade: 9, distortion_breach: 6, ultra_onslaught: 6 };

function deckKeyOf(o, e) {
  if (o.Name !== 'Deck') return null;
  if (has(o, 'Pokemon Card')) {
    const k = POKEMON_DECKS.filter(([tag]) => has(o, tag));
    return k.length === 1 ? { key: k[0][1], kind: 'Pokemon' } : { error: `포켓몬 덱 역할 태그 ${k.length}개` };
  }
  if (has(o, 'Trainer Card')) return { key: 'trainer_card', kind: 'TrainerCard' };
  if (has(o, 'Breach Card')) return { key: 'distortion_breach', kind: 'Card' }; // Village Card 태그도 붙어 있다
  for (const [tag, key] of CARD_DECKS) if (tag && has(o, tag)) return { key, kind: 'Card' };
  if (e && e.Nickname === 'Ultra Onslaught Cards') return { key: 'ultra_onslaught', kind: 'Card' };
  return { error: '모르는 덱' };
}

/** cards.js(10-30)와 같은 순서로 포켓몬 카드를 돈다: 최상위 순서, 그 안 ContainedObjects, 다음 States. → [{guid, nick, top, inState, lua}] */
export function walkPokemonCards(save) {
  const out = [];
  const walk = (o, top, inState) => {
    if ((o.Name === 'Card' || o.Name === 'CardCustom') && has(o, 'Pokemon Card')) out.push({ guid: o.GUID, nick: o.Nickname, top, inState, lua: o.LuaScript || '' });
    for (const c of o.ContainedObjects || []) walk(c, top, inState);
    if (o.States) for (const k in o.States) walk(o.States[k], top, true);
  };
  save.ObjectStates.forEach((o, i) => walk(o, i, false));
  return out;
}

/**
 * pokemon·trainerCards: cube 행, cards: 원천 카드 Lua(raw/_tools/cards.json), byKey: matchCards 결과(키 → 카드 Lua 객체들),
 * villageByColor: buildFixtures 결과(트레이너 색 → 마을 키, 고향 마을 카드용), authored: Tools/data/authored/card-faces.json.
 * → { decks: [{key, kind, cards, altStates}], cards: [{key, deck, copies}] }
 */
export function buildDecks(save, enBy, saveEn, pokemon, trainerCards, cards, byKey, villageByColor, authored, errors) {
  // 카드 Lua 객체 → pokemon 키(matchCards가 붙인 것)
  const keyOfCard = new Map();
  for (const [key, list] of byKey) for (const c of list) keyOfCard.set(c, key);
  const walked = walkPokemonCards(save);
  if (walked.length !== cards.length) errors.push(`세이브 포켓몬 카드 ${walked.length}장 ≠ cards.json ${cards.length}장`);
  walked.forEach((w, i) => {
    const c = cards[i];
    if (!c || c.guid !== w.guid || c.nick !== w.nick || c.inState !== w.inState) errors.push(`세이브 포켓몬 카드 ${i}번째(${w.nick})가 cards.json 순서와 다르다`);
  });
  if (errors.length) return null;
  const pokemonIndex = new Map(pokemon.map((p, i) => [p.key, i]));
  const trainerIndex = new Map(trainerCards.map((t, i) => [t.key, i]));

  const decks = [], cardRows = [];
  const tally = (list, key) => { const e = list.find(x => x.card === key); if (e) e.copies++; else list.push({ card: key, copies: 1 }); };
  const faces = authored.decks || {};
  save.ObjectStates.forEach((o, i) => {
    const e = enBy.get(o.GUID);
    const id = deckKeyOf(o, e);
    if (!id) return;
    const at = `세이브 T${i} 덱 ${o.Nickname || o.GUID}`;
    if (id.error) { errors.push(`${at}: ${id.error}`); return; }
    const deck = { key: `deck.${id.key}`, kind: id.kind, cards: [], altStates: [] };
    if (id.kind === 'Pokemon') {
      walked.forEach((w, n) => { if (w.top === i) tally(w.inState ? deck.altStates : deck.cards, keyOfCard.get(cards[n])); });
      const order = (a, b) => pokemonIndex.get(a.card) - pokemonIndex.get(b.card);
      deck.cards.sort(order);
      deck.altStates.sort(order);
    } else if (id.kind === 'TrainerCard') {
      // 시트 위치 0~50 = 영문 직업명 알파벳순 = xlsx 행 순서(trainer-cards.md §2). 원작 영문 덱(카드 이름 "<직업> (Bronze)")으로 확인한다
      const enDeck = saveEn.ObjectStates.find(x => x.Name === 'Deck' && has(x, 'Trainer Card'));
      const enClass = new Map((enDeck ? enDeck.ContainedObjects : []).map(c => [c.CardID % 100, c.Nickname.replace(/ \(Bronze\)$/, '')]));
      for (const c of o.ContainedObjects) {
        const t = trainerCards[c.CardID % 100];
        if (!t || enClass.get(c.CardID % 100) !== t.class) { errors.push(`${at}: 카드 ${c.CardID}의 직업이 영문 덱과 맞지 않다`); continue; }
        tally(deck.cards, t.key);
        // 다른 면(실버·골드 = 해금 단계)은 키가 따로 없다 → 직업 키에 면 수를 센다
        for (const k in c.States || {}) tally(deck.altStates, t.key);
      }
      const order = (a, b) => trainerIndex.get(a.card) - trainerIndex.get(b.card);
      deck.cards.sort(order);
      deck.altStates.sort(order);
    } else {
      const deckFaces = faces[id.key];
      if (!deckFaces) { errors.push(`authored card-faces.json: 덱 ${id.key}가 없다`); return; }
      const sheets = Object.keys(o.CustomDeck).map(Number).sort((a, b) => a - b); // 시트 순서 = CustomDeck 번호 순(한·영 같음)
      if (sheets.length !== deckFaces.sheets.length) errors.push(`${at}: 시트 ${sheets.length}장 ≠ authored ${deckFaces.sheets.length}장`);
      const faceKey = c => {
        const sheet = sheets.indexOf(Math.floor(c.CardID / 100)), face = c.CardID % 100;
        if (id.key === 'home_village') {
          // 고향 마을 카드: GMNotes 색 = 그 마을 트레이너 토큰 색
          const v = villageByColor.get(c.GMNotes);
          if (!v) errors.push(`${at}: 고향 마을 카드 색 ${c.GMNotes}의 마을을 모른다`);
          return v ? `home_village.${v.replace(/^village\./, '')}` : null;
        }
        const names = deckFaces.sheets[sheet];
        if (!names || face >= names.length) { errors.push(`${at}: 카드 ${c.CardID}의 앞면이 authored에 없다`); return null; }
        const name = names[face];
        return `${id.key}.${name === null ? String(face + 1) : snakeKey(name.replace(/^(A|The) /, ''))}`;
      };
      const ordered = [];
      for (const c of o.ContainedObjects) {
        const k = faceKey(c);
        if (!k) continue;
        tally(deck.cards, k);
        ordered.push([Math.floor(c.CardID / 100), c.CardID % 100, k]);
        if (c.States) errors.push(`${at}: 이미지 카드에 상태가 있다`);
      }
      // 카드 순서: 시트·앞면 순(셔플된 세이브 순서가 아니다)
      const rank = new Map(ordered.sort((a, b) => a[0] - b[0] || a[1] - b[1]).map((x, n) => [x[2], n]));
      deck.cards.sort((a, b) => rank.get(a.card) - rank.get(b.card));
      for (const x of deck.cards) cardRows.push({ key: x.card, deck: deck.key, copies: x.copies });
    }
    decks.push(deck);
  });
  for (const k of Object.keys(faces)) if (!decks.some(d => d.key === `deck.${k}`)) errors.push(`authored card-faces.json: 덱 ${k}가 세이브에 없다`);

  // 순서: 포켓몬 덱(POKEMON_DECKS 순) → 트레이너 카드 → 이미지 카드 덱(CARD_DECKS 순)
  const ORDER = [...POKEMON_DECKS.map(x => x[1]), 'trainer_card', ...CARD_DECKS.map(x => x[1])].map(k => `deck.${k}`);
  decks.sort((a, b) => ORDER.indexOf(a.key) - ORDER.indexOf(b.key));
  cardRows.sort((a, b) => ORDER.indexOf(a.deck) - ORDER.indexOf(b.deck)); // 덱 안 순서는 위에서 정했다(안정 정렬)

  // 03 §1-4: 덱 17·749장(+대체 179), 덱마다 장수, 이미지 카드 고유 앞면 54(장수 136 + 온슬롯 6)
  expect(errors, '덱', decks.length, 17);
  const copies = d => sum(d.cards, x => x.copies), alts = d => sum(d.altStates, x => x.copies);
  expect(errors, '덱 장수 합', sum(decks, copies), 749);
  expect(errors, '대체 상태 합', sum(decks, alts), 179);
  for (const d of decks) {
    const want = DECK_COUNTS[d.key.slice(5)];
    if (!want) { errors.push(`${d.key}: 기대 장수가 없다`); continue; }
    expect(errors, `${d.key} 장수`, copies(d), want[0]);
    expect(errors, `${d.key} 대체 상태`, alts(d), want[1]);
    if (d.kind === 'Card') expect(errors, `${d.key} 고유 앞면`, d.cards.length, FACE_COUNTS[d.key.slice(5)]);
  }
  expect(errors, '이미지 카드 고유 앞면', cardRows.length, 54);
  expect(errors, '이미지 카드 장수(온슬롯 뺌)', sum(cardRows.filter(r => r.deck !== 'deck.ultra_onslaught'), r => r.copies), 136);
  expect(errors, '울트라 온슬롯 장수', sum(cardRows.filter(r => r.deck === 'deck.ultra_onslaught'), r => r.copies), 6);
  // 참조: 포켓몬 덱 장수 합 = xlsx number_in_deck(첫 면 행), 대체 상태 면 합 = 그 면 행의 number_in_deck(첫 면과 같은 값)
  const seen = new Map();
  for (const d of decks.filter(x => x.kind === 'Pokemon')) for (const x of [...d.cards, ...d.altStates]) seen.set(x.card, (seen.get(x.card) || 0) + x.copies);
  for (const p of pokemon) if ((seen.get(p.key) || 0) !== p.copies) errors.push(`pokemon ${p.key}: 덱 장수 ${seen.get(p.key) || 0} ≠ copies ${p.copies}`);
  for (const d of decks.filter(x => x.kind === 'Pokemon')) {
    for (const x of d.cards) if (pokemon[pokemonIndex.get(x.card)].state > 1) errors.push(`${d.key}: 바탕 카드 ${x.card}가 첫 면이 아니다`);
    for (const x of d.altStates) {
      const p = pokemon[pokemonIndex.get(x.card)];
      if (p.state < 2) errors.push(`${d.key}: 대체 상태 ${x.card}가 첫 면이다`);
      if (!d.cards.some(b => { const q = pokemon[pokemonIndex.get(b.card)]; return q.dex === p.dex && q.trainer === p.trainer; })) errors.push(`${d.key}: 대체 상태 ${x.card}의 바탕 카드가 덱에 없다`);
    }
  }
  const trainerDeck = decks.find(d => d.key === 'deck.trainer_card');
  if (trainerDeck) expect(errors, '트레이너 카드 덱 직업', trainerDeck.cards.length, trainerCards.length);
  return { decks, cards: cardRows };
}
