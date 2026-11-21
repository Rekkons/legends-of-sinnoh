// 공식 한국어 기술명(결정 43 ⑩·44 ②): 영문 기술 이름 → 현행 공식 한국어 이름. strings.mjs가 move.<키>에 쓰고 reports/move-names-ko.tsv를 만든다.
//   원천(결정 44 ② — 결정 43 ⑩의 「원작 자산 우선」을 바꿈):
//     1 공식 기술(번호 있음) — PokeAPI move_names.csv(고정 커밋 = Tools/assets/lib/fetch-lists.mjs PIN.pokeapiData)의 local_language_id 3(ko).
//     2 번호 없는 기술(이 게임만의 기술) — 3JA2 카드 이름 그대로.
//   기술 번호는 PokeAPI 표의 영어 이름(local_language_id 9)으로 찾는다(대소문자·기호 무시). 원작의 영국식 철자는 ALIAS로 잇는다.
//   원작 자산 이름은 대조용이다 — Pt pl_msg 0637(기술 1~467), B2W2 a/0/0/2 0403(468~559). HGSS 0743·BW 0203도 같은 글이다(2026-11-08 대조, 1~467은 Pt = B2W2를 빌드가 확인).
//   원작 자산 이름이 현행 이름과 다르면(9세대 개정: 메가폰 → 메가혼 등) 보고서 note에 「원작 자산(Pt) 「메가폰」」처럼 옛 이름을 남긴다.
import { readFileSync } from 'node:fs';
import { srcPath } from '../../sources.mjs';

const PT_LAST = 467, GEN5_LAST = 559, KO = '3', EN = '9';
// 원작 xlsx 이름(영국식 철자) → PokeAPI 영어 이름
const ALIAS = { Judgement: 'Judgment' };

// 이 게임만의 기술 가운데 한국어판 카드가 붙여 쓴 이름(「시간의수호」)은 띄어 쓴다(사용자 결정 87 W10 ① 나 — 꾸미는 말 + 이름 꼴 모두. 공식 기술명은 붙여 쓴 그대로)
const SPACED = {
  'Renegade Fury': '반골의 분노', 'Shocking Verity': '충격적인 진실', 'Thunder Song': '천둥의 노래', 'Blazing Valor': '타오르는 용맹', 'Fire Song': '불꽃의 노래',
  'Menacing Moonraze': '위협적인 문레이즈', 'Eternal Glade': '영원한 숲', 'Frigid Ice': '서늘한 얼음', 'Chilling Acuity': '날카로운 예지', 'Frost Song': '얼음의 노래',
  'Rampart Riposte': '성벽의 반격', 'Temporal Guard': '시간의 수호', 'Searing Sunraze': '이글거리는 선레이즈', 'Ripple in Space': '공간의 물결',
};

const norm = s => s.toLowerCase().replace(/[^a-z0-9]/g, '');

/** 따옴표 있는 칸만 다루는 단순 CSV(PokeAPI 표) → 행 배열 */
function parseCsv(text) {
  const rows = [];
  for (const line of text.replace(/^﻿/, '').split(/\r?\n/)) {
    if (!line) continue;
    const cells = [];
    for (let i = 0; i <= line.length;) {
      if (line[i] === '"') {
        let v = '', j = i + 1;
        for (; j < line.length; j++) {
          if (line[j] === '"' && line[j + 1] === '"') { v += '"'; j++; } else if (line[j] === '"') break; else v += line[j];
        }
        cells.push(v);
        i = j + 2;
      } else {
        const j = line.indexOf(',', i);
        cells.push(line.slice(i, j < 0 ? line.length : j));
        i = j < 0 ? line.length + 1 : j + 1;
      }
    }
    rows.push(cells);
  }
  return rows;
}

/**
 * src: Tools/sources.json data(moveNamesPt·moveNamesB2w2·moveNamesPokeapi), moves: xlsx 기술 행, ko: 지금 한국어 값(3JA2, 용어 통일 뒤).
 * → { names: Map(문자열 키 → 공식 이름), report: TSV 내용, summary: 한 줄 }
 */
export function officialMoveNames(root, src, moves, ko, errors) {
  const readJson = key => JSON.parse(readFileSync(srcPath(root, src[key]), 'utf8'));
  const pt = readJson('moveNamesPt'), b2 = readJson('moveNamesB2w2');
  const csv = parseCsv(readFileSync(srcPath(root, src.moveNamesPokeapi), 'utf8'));
  if (csv[0].join() !== 'move_id,local_language_id,name') errors.push(`${src.moveNamesPokeapi}: 머리 ${csv[0].join()}`);
  const enById = new Map(), koById = new Map(), idByEn = new Map();
  for (const [id, lang, name] of csv.slice(1)) {
    if (lang === KO) koById.set(Number(id), name);
    if (lang !== EN) continue;
    enById.set(Number(id), name);
    const k = norm(name);
    idByEn.set(k, idByEn.has(k) ? null : Number(id)); // null = 같은 영어 이름이 둘(Z기술 물리·특수)
  }
  if (!Array.isArray(pt) || pt.length !== PT_LAST + 1) errors.push(`${src.moveNamesPt}: 기술 이름 ${PT_LAST + 1}줄이 아니다`);
  if (!Array.isArray(b2) || b2.length !== GEN5_LAST + 1) errors.push(`${src.moveNamesB2w2}: 기술 이름 ${GEN5_LAST + 1}줄이 아니다`);
  if (errors.length) return null;
  for (let i = 1; i <= PT_LAST; i++) if (pt[i] !== b2[i]) errors.push(`원작 자산 기술 ${i}: Pt 「${pt[i]}」 ≠ B2W2 「${b2[i]}」`);
  if (pt[1] !== koById.get(1)) errors.push(`원작 자산 기술 1 「${pt[1]}」 ≠ PokeAPI 「${koById.get(1)}」(기술 이름 표가 아니다?)`);

  const names = new Map(), rows = [], tally = { official: 0, romSame: 0, romDiff: 0, game: 0, changed: 0 };
  for (const m of moves) {
    const key = `move.${m.key}`, old = ko[key];
    const id = idByEn.get(norm(ALIAS[m.name] || m.name));
    if (id === null) { errors.push(`${key}: 영어 이름 ${m.name}이 PokeAPI에 둘`); continue; }
    let name, from, note = '';
    if (id === undefined) {
      name = old; from = '3ja2'; note = '이 게임만의 기술'; tally.game++;
      const spaced = SPACED[m.name];
      if (spaced) {
        if (spaced.replace(/ /g, '') !== old) errors.push(`${key}: 띄어 쓴 이름 「${spaced}」이 3JA2 「${old}」과 다르다`);
        name = spaced; note += ' · 띄어 씀(결정 87 W10 ①)';
        names.set(key, name);
      }
    }
    else {
      name = koById.get(id);
      if (!name) { errors.push(`${key}: 기술 ${id}(${enById.get(id)})의 한국어 이름이 PokeAPI에 없다`); continue; }
      from = `pokeapi:${id}`;
      tally.official++;
      if (ALIAS[m.name]) note = `원작 철자 ${m.name} = ${ALIAS[m.name]}`;
      const rom = id <= PT_LAST ? pt[id] : id <= GEN5_LAST ? b2[id] : undefined;
      if (rom === name) tally.romSame++;
      else if (rom !== undefined) { tally.romDiff++; note = [note, `원작 자산(${id <= PT_LAST ? 'Pt' : 'B2W2'}) 「${rom}」`].filter(Boolean).join(', '); }
      names.set(key, name);
    }
    if (name !== old) tally.changed++;
    rows.push([key, m.name, id === undefined ? '' : String(id), old, name, name === old ? 'same' : 'changed', from, note]);
  }
  const report = [['key', 'en', 'id', 'ko_3ja2', 'ko', 'diff', 'src', 'note'], ...rows].map(r => r.join('\t')).join('\n') + '\n';
  const summary = `기술 이름 ${moves.length}: 현행 공식(PokeAPI) ${tally.official}(원작 자산과 같음 ${tally.romSame}·다름 ${tally.romDiff}·원작 자산에 없음 ${tally.official - tally.romSame - tally.romDiff}) · 이 게임만 ${tally.game}(3JA2), 3JA2와 다름 ${tally.changed} → reports/move-names-ko.tsv`;
  return { names, report, summary };
}
