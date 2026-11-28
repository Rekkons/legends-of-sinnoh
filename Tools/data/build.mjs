// 게임 데이터 JSON 빌드(Docs/Design/03-data-assets.md §1). 입력 경로는 Tools/sources.json(data.*) 한 곳.
//   node Tools/data/build.mjs           세이브 → Research/map_cells.csv(lib/save.mjs) → board.json(+ 덮개 틴트·디퓨즈·기둥 틴트),
//                                       세이브 → fixtures·decks·cards(이미지 카드 뼈대)·items(뼈대).json(lib/save.mjs),
//                                       authored/cards/<deck>.json·items.json(손으로 옮긴 카드 문구·verified, 레시피·스튜·스탬프) → cards·items 행 + 문자열(lib/authored.mjs, M5),
//                                       xlsx → pokemon·moves·trainer_cards.json(lib/cube.mjs),
//                                       effects/moves.json(손으로 옮긴 기술 효과) → effects.json + reports/effects-todo.tsv(lib/effects.mjs, M3-f),
//                                       카드 Lua·언어 모듈·원작 자산 이름·세이브 → strings.ko·strings.en.json(lib/strings.mjs),
//                                       authored/ko/<묶음>.json(영문에서 다시 옮긴 한국어, 결정 163) → 3JA2 · 로씨온 글을 덮음 + reports/retranslate-ko.tsv(lib/retranslate.mjs),
//                                       PokeAPI move_names.csv(+ 대조용 원작 자산 기술 이름 Pt·B2W2) → 현행 공식 한국어 기술명 + reports/move-names-ko.tsv(lib/move-names.mjs, 결정 43 ⑩·44 ②),
//                                       카드 Lua + authored/dex-ko.json → Research/roster.csv(lib/roster.mjs, 종 이름 → 도감 번호),
//                                       authored/tutorial/<id>.json + tutorial-ko.json → Data/Json/tutorial/<id>.json + 문자열 tutorial.*(lib/tutorial.mjs, M6.5),
//                                       authored/tutorial-chapters.json(목차의 장 머리) → Data/Json/tutorial_chapters.json(lib/tutorial.mjs, M6.5),
//                                       authored/rulebook/(장 순서 · <lang>/<장>.json · <lang>-terms.json) → Data/Json/rulebook.<lang>.json(lib/rulebook.mjs, M9),
//                                       authored/rulings/<lang>.json(rulings-import.mjs가 만든다) → Data/Json/rulings.<lang>.json(lib/rulings.mjs, 결정 213),
//                                       Tools/assets/credits/*.json(CREDITS.md의 손 표) → Data/Json/credits.json(크레딧 화면 S03 — ../assets/lib/credits.mjs creditsTable, M9),
//                                       + manifest.json → Assets/Sinnoh/Data/Json, 보고서 → Tools/data/reports
//   node Tools/data/build.mjs --self-test  손 파일 겹친 키 검사(lib/dupkeys.mjs)·두 언어 짝 검사(lib/strings.mjs checkLanguagePair)의 자기 시험 — 입력을 읽지 않는다
//   node Tools/data/build.mjs --check   임시 폴더에 다시 만들어 Data/Json·reports·map_cells.csv·roster.csv와 바이트 비교, 다르면 종료 코드 1
// 출력: UTF-8(BOM 없음)·LF, 키 순서 고정, 행 하나 = 한 줄, { "schema": 1, "rows": [...] }(03 §1-1).
// 검증(03 §1-4)이 하나라도 틀리면 쓰지 않고 종료 코드 1. 내용이 같은 파일은 다시 쓰지 않는다(에디터 재임포트 방지).
// 입력(Research/)은 이 도구만 읽는다. 코드·시험은 생성된 JSON을 읽는다(CLAUDE.md).
import { readFileSync, writeFileSync, existsSync, mkdirSync, mkdtempSync, readdirSync, rmSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { buildCube, validateCube } from './lib/cube.mjs';
import { buildEffects } from './lib/effects.mjs';
import { buildStrings, readGitSpec, selfTestLanguagePair } from './lib/strings.mjs';
import { readSave, englishIndex, buildCells, buildItems, buildFixtures, buildDecks, DIFFUSES } from './lib/save.mjs';
import { readCards, mergeCards, cardStrings, mergeItems } from './lib/authored.mjs';
import { checkDuplicateKeys, selfTestDuplicateKeys } from './lib/dupkeys.mjs';
import { buildTutorial } from './lib/tutorial.mjs';
import { buildRulebook } from './lib/rulebook.mjs';
import { buildRulings } from './lib/rulings.mjs';
import { selfTestRetranslate } from './lib/retranslate.mjs';
import { creditsTable } from '../assets/lib/credits.mjs';
import { srcPath } from '../sources.mjs';
import { cli } from '../cli.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const OUT = path.join(ROOT, 'Assets/Sinnoh/Data/Json');
const REPORTS = path.join(ROOT, 'Tools/data/reports');
const AUTHORED = 'Tools/data/authored';
const EFFECTS = 'Tools/data/effects/moves.json';
const SOURCES = JSON.parse(readFileSync(path.join(ROOT, 'Tools/sources.json'), 'utf8')).data;
const CELLS_CSV = SOURCES.mapCells;
const ROSTER_CSV = SOURCES.roster;
const XLSX = SOURCES.xlsx;

const BIOMES = ['Plains', 'Forest', 'Mountain', 'Ocean'];
const CLIMATES = ['Cold', 'Temperate', 'Warm'];
// 엔진이 읽는 표(manifest.contentHash에 묶는다, 01 §10 세이브 ContentHash)
const ENGINE_TABLES = ['board', 'cards', 'decks', 'effects', 'fixtures', 'items', 'moves', 'pokemon', 'trainer_cards'];

const sha = (algo, buf) => createHash(algo).update(buf).digest('hex');

function fail(errors) {
  for (const e of errors) console.error('오류: ' + e);
  console.error(`build: 오류 ${errors.length}건 — 쓰지 않음`);
  process.exit(1);
}

// map_cells.csv(따옴표 없는 단순 CSV, UTF-8 BOM) 내용 → board 행. looks: 칸 키 → 덮개 {tint, diffuse}·기둥 틴트 pillars(세이브)
function buildBoard(csvText, looks, errors) {
  const text = csvText.replace(/^﻿/, '');
  const [head, ...lines] = text.split(/\r?\n/).filter(l => l.length);
  const cols = head.split(',');
  const want = ['col', 'row', 'x', 'z', 'level', 'biome', 'climate', 'wild_area', 'region', 'distortion_world', 'pieces', 'top_scale_y'];
  if (cols.join() !== want.join()) { errors.push(`${CELLS_CSV} 열이 다르다: ${cols.join()}`); return []; }

  const rows = [];
  lines.forEach((line, i) => {
    const f = line.split(',');
    const at = `${CELLS_CSV}:${i + 2}`;
    if (f.length !== cols.length || line.includes('"')) { errors.push(`${at} 열 수 ${f.length}(따옴표 안 씀)`); return; }
    const v = Object.fromEntries(cols.map((c, k) => [c, f[k]]));
    const int = s => (/^-?\d+$/.test(s) ? Number(s) : (errors.push(`${at} 정수가 아님: ${s}`), 0));
    const num = s => (/^-?\d+(\.\d+)?$/.test(s) ? Number(s) : (errors.push(`${at} 수가 아님: ${s}`), 0));
    const bit = s => (s === '1' ? true : s === '0' ? false : (errors.push(`${at} 0/1이 아님: ${s}`), false));
    const col = int(v.col), row = int(v.row), level = num(v.level);
    if (((col + row) & 1) !== 0) errors.push(`${at} col+row 홀수: ${col},${row}`);
    if (level * 4 !== Math.round(level * 4) || level < 0) errors.push(`${at} level이 0.25 단위 음 아닌 수가 아님: ${v.level}`);
    // "(lake/summit)"은 지형 태그 없이 World Token만 붙은 칸(호수 3·천관산 정상 3)이라 biome 없음으로 둔다
    if (v.biome && v.biome !== '(lake/summit)' && !BIOMES.includes(v.biome)) errors.push(`${at} 모르는 biome: ${v.biome}`);
    if (v.climate && !CLIMATES.includes(v.climate)) errors.push(`${at} 모르는 climate: ${v.climate}`);
    rows.push({
      key: `${col},${row}`,
      col,
      row,
      x: num(v.x),
      z: num(v.z),
      level,
      biome: BIOMES.includes(v.biome) ? v.biome : null,
      climate: v.climate || null,
      wild: bit(v.wild_area),
      region: v.region || null,
      distortion: bit(v.distortion_world),
      tint: (looks.get(`${col},${row}`) || {}).tint || (errors.push(`${at} 덮개 틴트가 없다`), null),
      diffuse: (looks.get(`${col},${row}`) || {}).diffuse || (errors.push(`${at} 덮개 디퓨즈가 없다`), null),
      pillars: (looks.get(`${col},${row}`) || {}).pillars || (errors.push(`${at} 기둥 틴트가 없다`), null),
    });
  });
  // 칸 색인 0~350 = 이 순서(본맵 먼저, 그다음 col·row)
  rows.sort((a, b) => a.distortion - b.distortion || a.col - b.col || a.row - b.row);

  // 03 §1-4: 칸 351 = 본맵 326 + 깨어진 세계 25, 야생 343, 중복 0
  const count = (p) => rows.filter(p).length;
  const expect = (what, got, want) => { if (got !== want) errors.push(`${what}: ${got}(기대 ${want})`); };
  expect('칸 수', rows.length, 351);
  expect('본맵 칸', count(r => !r.distortion), 326);
  expect('깨어진 세계 칸', count(r => r.distortion), 25);
  expect('야생 칸', count(r => r.wild), 343);
  expect('중복 칸', rows.length - new Set(rows.map(r => r.key)).size, 0);
  for (const d of DIFFUSES) if (!rows.some(r => r.diffuse === d)) errors.push(`디퓨즈 ${d}를 쓰는 칸이 없다`);
  return rows;
}

const table = rows => `{"schema":1,"rows":[\n${rows.map(r => JSON.stringify(r)).join(',\n')}\n]}\n`;
// 문자열 표: 평평한 {키: 문장}, 키는 서수(UTF-16) 순, 한 줄에 하나(03 §1-3). Sinnoh.Data StringTable.Write와 같은 바이트
const stringsFile = (lang, map) => {
  const keys = Object.keys(map).sort();
  return `{"schema":1,"lang":"${lang}","strings":{\n${keys.map(k => JSON.stringify(k) + ':' + JSON.stringify(map[k])).join(',\n')}\n}}\n`;
};

/** Tools/data/authored/<file>: { schema: 1, note, <keys…> } 꼴만 받는다 */
function readAuthored(file, keys, errors) {
  const j = JSON.parse(readFileSync(path.join(ROOT, AUTHORED, file), 'utf8'));
  const want = ['note', 'schema', ...keys].sort().join();
  if (j.schema !== 1 || Object.keys(j).sort().join() !== want) errors.push(`${AUTHORED}/${file}: 형식(schema 1, 필드 ${want})`);
  return j;
}

/** → [{ dir, files: { 파일 이름: 내용 }, only: 폴더 안 .json·.tsv·.md는 모두 빌드가 만든 것인가 }] */
function buildAll() {
  const errors = [];
  // 손 파일의 겹친 키(JSON.parse가 조용히 덮는다 — lib/dupkeys.mjs)
  checkDuplicateKeys(ROOT, [AUTHORED, EFFECTS], errors);
  if (errors.length) fail(errors);
  const save = readSave(srcPath(ROOT, SOURCES.save));
  const saveEn = readSave(srcPath(ROOT, SOURCES.saveEn));
  const enBy = englishIndex(save, saveEn, errors);
  const { csv, looks } = buildCells(save, errors);
  const board = buildBoard(csv, looks, errors);
  let cube = null;
  try { cube = buildCube(srcPath(ROOT, XLSX), errors); } catch (e) { errors.push(`${XLSX}: ${e.message}`); }
  if (cube) validateCube(cube, errors);
  if (errors.length) fail(errors);
  const effects = buildEffects(ROOT, EFFECTS, cube.moves, errors);
  if (errors.length) fail(errors);

  const items = buildItems(save, enBy, errors);
  const authoredFixtures = readAuthored('fixtures.json', ['legendaryShrines', 'villageStarters'], errors);
  const fixtures = buildFixtures(save, enBy, board, cube.pokemon, items, authoredFixtures, errors);
  const itemTable = mergeItems(ROOT, items, errors);
  const cardTexts = readCards(ROOT, errors);
  const tutorial = buildTutorial(ROOT, errors);
  if (errors.length) fail(errors);
  let strings = null;
  try { strings = buildStrings(ROOT, SOURCES, cube, board, save, saveEn, [...fixtures.names, ...items.names, ...itemTable.strings, ...cardStrings(cardTexts), ...tutorial.strings], errors); } catch (e) { errors.push(`strings: ${e.message}`); }
  if (errors.length) fail(errors);
  const decks = buildDecks(save, enBy, saveEn, cube.pokemon, cube.trainerCards, strings.cards, strings.byKey, fixtures.villageByColor,
    readAuthored('card-faces.json', ['decks'], errors), errors);
  if (errors.length) fail(errors);
  const cards = mergeCards(decks.cards, cardTexts, authoredFixtures.villageStarters, errors);
  if (errors.length) fail(errors);
  const rulebook = buildRulebook(ROOT, table, { strings, items: itemTable.rows, trainerCards: cube.trainerCards }, errors);
  if (errors.length) fail(errors);
  const rulings = buildRulings(ROOT, table, errors);
  if (errors.length) fail(errors);

  const rowsOf = {
    board, cards, decks: decks.decks, effects: effects.rows, fixtures: fixtures.rows, items: itemTable.rows,
    moves: cube.moves, pokemon: cube.pokemon, trainer_cards: cube.trainerCards,
  };
  const files = {}, tables = {};
  for (const name of Object.keys(rowsOf).sort()) {
    const file = name + '.json';
    files[file] = table(rowsOf[name]);
    tables[name] = { file, rows: rowsOf[name].length, sha256: sha('sha256', files[file]) };
  }
  // 문자열 표는 엔진이 읽지 않는다(01 §11) → contentTables 밖. rows = 키 수
  for (const lang of ['en', 'ko']) {
    const file = `strings.${lang}.json`;
    files[file] = stringsFile(lang, strings[lang]);
    tables[`strings.${lang}`] = { file, rows: Object.keys(strings[lang]).length, sha256: sha('sha256', files[file]) };
  }
  // 각본(엔진이 아니라 진행기·화면이 읽는다 — contentTables 밖). rows = 단계 수
  for (const name of Object.keys(tutorial.files)) {
    const id = name.replace(/.json$/, '');
    tables[`tutorial.${id}`] = { file: `tutorial/${name}`, rows: tutorial.rows[id], sha256: sha('sha256', tutorial.files[name]) };
  }
  // 목차의 장 머리(화면이 읽는다 — contentTables 밖). rows = 장 수
  files['tutorial_chapters.json'] = table(tutorial.chapters);
  tables.tutorial_chapters = { file: 'tutorial_chapters.json', rows: tutorial.chapters.length, sha256: sha('sha256', files['tutorial_chapters.json']) };
  // 룰 설명서(화면이 읽는다 — contentTables 밖). rows = 절 수
  for (const [file, text] of Object.entries(rulebook.files)) {
    const name = file.replace(/\.json$/, '');
    files[file] = text;
    tables[name] = { file, rows: rulebook.rows[name.slice('rulebook.'.length)], sha256: sha('sha256', text) };
  }
  // 「규칙 판정」 화면(화면이 읽는다 — contentTables 밖, 결정 213). rows = 항목 수
  for (const [file, text] of Object.entries(rulings.files)) {
    const name = file.replace(/.json$/, '');
    files[file] = text;
    tables[name] = { file, rows: rulings.rows[name.slice('rulings.'.length)], sha256: sha('sha256', text) };
  }
  // 크레딧 화면 S03(화면이 읽는다 — contentTables 밖, M9). 원천 = Tools/assets/credits/*.json(CREDITS.md와 같은 손 표 — 검사는 assets build의 credits 묶음). rows = 묶음 수
  const credits = creditsTable(ROOT);
  files['credits.json'] = table(credits);
  tables.credits = { file: 'credits.json', rows: credits.length, sha256: sha('sha256', files['credits.json']) };
  // contentHash(세이브 ContentHash, 01 §10) = contentTables(엔진이 읽는 표)의 "이름\tsha256\n"을 이름순으로 이은 것의 sha256.
  // Sinnoh.Data ContentManifest.ComputeContentHash가 같은 식이다
  const contentTables = ENGINE_TABLES.slice().sort();
  const contentHash = sha('sha256', contentTables.map(n => `${n}\t${tables[n].sha256}\n`).join(''));
  // 입력 sha1: 파일은 바이트, git 원천(<저장소.git>@<커밋>:<경로>)은 그 내용
  // map_cells.csv·roster.csv는 이 빌드가 다시 만든 내용으로 센다(디스크 파일은 아래에서 고쳐 쓴다)
  const generated = { [CELLS_CSV]: csv, [ROSTER_CSV]: strings.rosterCsv };
  const inputs = Object.fromEntries(Object.values(SOURCES).map(p => [p, sha('sha1', p in generated ? Buffer.from(generated[p], 'utf8')
    : p.includes('.git@') ? Buffer.from(readGitSpec(ROOT, p), 'utf8') : readFileSync(srcPath(ROOT, p)))]));
  files['manifest.json'] = JSON.stringify({ schema: 1, tables, contentTables, contentHash, inputs }, null, 2) + '\n';
  console.log(`고정물 ${fixtures.rows.length}개를 칸에 붙임(최대 거리 ${fixtures.maxDistance.toFixed(3)})`);
  console.log(strings.moveNamesSummary);
  console.log(strings.retranslateSummary);
  console.log(rulebook.summary);
  console.log(rulings.summary);
  console.log(`이미지 전용 카드 ${cards.length}면 verified ${cards.filter(r => r.verified).length}, 아이템 ${itemTable.rows.length}행(레시피 ${itemTable.rows.filter(r => r.recipe).length})`);
  console.log(`기술 효과 ${effects.implemented}/${cube.moves.length}종(절 ${effects.rows.length}), 미구현 ${cube.moves.length - effects.implemented}종 → reports/effects-todo.tsv`);
  return [
    { dir: OUT, files, only: true },
    { dir: path.join(OUT, 'tutorial'), files: tutorial.files, only: true },
    { dir: REPORTS, files: { ...strings.reports, 'fixtures-cells.tsv': fixtures.report, 'effects-todo.tsv': effects.todo }, only: true },
    ...Object.entries(generated).map(([p, text]) => ({ dir: path.dirname(path.join(ROOT, p)), files: { [path.basename(p)]: text }, only: false })),
  ];
}

function writeChanged(dir, files) {
  mkdirSync(dir, { recursive: true });
  let written = 0;
  for (const [name, text] of Object.entries(files)) {
    const p = path.join(dir, name);
    if (existsSync(p) && readFileSync(p, 'utf8') === text) continue;
    writeFileSync(p, text, 'utf8');
    written++;
  }
  return written;
}

// 모르는 선택(--chek 오타 등)으로 쓰지 않게 만들기 전에 거른다(종료 코드 2), --help는 사용법만
if (process.argv.includes('--self-test')) {
  const bad = [...selfTestDuplicateKeys(), ...selfTestLanguagePair(), ...selfTestRetranslate()];
  for (const b of bad) console.error('실패: ' + b);
  console.log(bad.length ? `build self-test: ${bad.length}건 실패` : 'build self-test: OK');
  process.exit(bad.length ? 1 : 0);
}
const { values: opts } = cli('사용: node Tools/data/build.mjs [--check] [--self-test]', { check: { type: 'boolean' } });
const outputs = buildAll();
const count = outputs.reduce((n, o) => n + Object.keys(o.files).length, 0);
if (opts.check) {
  // 03 §1-4: 임시 폴더에 만들어 Data/Json·reports·map_cells.csv와 바이트 비교. 빌드가 만들지 않는 .json(Data/Json)·.tsv·.md(reports)도 diff
  const diffs = [];
  for (const { dir, files, only } of outputs) {
    const rel = path.relative(ROOT, dir).split(path.sep).join('/');
    const tmp = mkdtempSync(path.join(tmpdir(), 'sinnoh-data-'));
    try {
      writeChanged(tmp, files);
      for (const name of Object.keys(files)) {
        const p = path.join(dir, name);
        if (!existsSync(p)) diffs.push(`${rel}/${name}: 없음`);
        else if (!readFileSync(p).equals(readFileSync(path.join(tmp, name)))) diffs.push(`${rel}/${name}: 다시 만든 내용과 다르다`);
      }
      if (only && existsSync(dir))
        for (const name of readdirSync(dir))
          if (/\.(json|tsv|md)$/.test(name) && !(name in files)) diffs.push(`${rel}/${name}: 빌드가 만들지 않는 파일`);
    } finally { rmSync(tmp, { recursive: true, force: true }); }
  }
  for (const d of diffs) console.log(d);
  console.log(`build --check: 파일 ${count}개, diff ${diffs.length}`);
  process.exitCode = diffs.length ? 1 : 0;
} else {
  const n = outputs.reduce((a, o) => a + writeChanged(o.dir, o.files), 0);
  console.log(`build: 파일 ${count}개, 새로 씀 ${n}`);
}
