// 「규칙 판정」 화면의 물음 · 답(결정 213):
//   authored/rulings/<lang>.json { schema, note, groups: [ { key, title, sections: [ { key, title, items: [ { id, q, a, source } ] } ] } ] }
//     (Tools/data/rulings-import.mjs가 검토용 md에서 만든다)
//   → Data/Json/rulings.<lang>.json(항목 하나 = 한 줄 — 묶음 · 절 · 항목 순서 그대로: { id, group, groupTitle, section, sectionTitle, q, a, source }) · manifest rulings.<lang>
// 묶음 key는 RULINGS_GROUPS 다섯(그 순서), 절 키 = <묶음>.<이름>, id = r + 숫자 3자리(파일 안에서 유일), source = creator(원작자 답변) · rulebook(규칙서 해석) · house(이 게임에서 정함).
// 검사: 꼴 · 키 문법 · 유일 · 빈 글 · 글 앞뒤 공백 · 두 언어: 묶음 · 절 키 순서와 절마다 항목 id 순서 · 출처가 같다.
import { readFileSync } from 'node:fs';
import path from 'node:path';

export const RULINGS_DIR = 'Tools/data/authored/rulings';
export const RULINGS_LANGS = ['ko', 'en'];
export const RULINGS_GROUPS = ['basic', 'battle', 'campaign', 'trainer', 'ultra'];
export const RULINGS_SOURCES = ['creator', 'rulebook', 'house'];

const SECTION_KEY = /^([a-z]+)\.[a-z0-9]+(?:_[a-z0-9]+)*$/;
const ITEM_ID = /^r\d{3}$/;
const isStr = v => typeof v === 'string' && v.length > 0 && v === v.trim();
const fields = (o, want) => o && typeof o === 'object' && !Array.isArray(o) && Object.keys(o).sort().join() === want;

function rowsOf(root, lang, errors) {
  const rel = `${RULINGS_DIR}/${lang}.json`;
  let j;
  try { j = JSON.parse(readFileSync(path.join(root, rel), 'utf8')); }
  catch (e) { errors.push(`${rel}: ${e.message}`); return []; }
  if (!fields(j, 'groups,note,schema') || j.schema !== 1 || !Array.isArray(j.groups)) { errors.push(`${rel}: 형식(schema 1, 필드 groups·note·schema)`); return []; }
  if (j.groups.map(g => g && g.key).join() !== RULINGS_GROUPS.join()) errors.push(`${rel}: 묶음은 ${RULINGS_GROUPS.join(' · ')} 순서`);
  const rows = [], ids = new Set(), keys = new Set();
  for (const g of j.groups) {
    if (!fields(g, 'key,sections,title') || !isStr(g.title) || !Array.isArray(g.sections) || !g.sections.length) { errors.push(`${rel} ${g && g.key}: 묶음 형식(key · title · sections)`); continue; }
    for (const s of g.sections) {
      const at = `${rel} ${s && s.key}`;
      if (!fields(s, 'items,key,title') || !isStr(s.title) || !Array.isArray(s.items) || !s.items.length) { errors.push(`${at}: 절 형식(key · title · items)`); continue; }
      const m = SECTION_KEY.exec(s.key);
      if (!m || m[1] !== g.key) errors.push(`${at}: 절 키 문법 <묶음>.<이름>`);
      if (keys.has(s.key)) errors.push(`${at}: 절 키가 겹친다`);
      keys.add(s.key);
      for (const it of s.items) {
        const iat = `${at} ${it && it.id}`;
        if (!fields(it, 'a,id,q,source') || !isStr(it.q) || !isStr(it.a)) { errors.push(`${iat}: 항목 형식(id · q · a · source, 빈 글 · 앞뒤 공백 없음)`); continue; }
        if (!ITEM_ID.test(it.id)) errors.push(`${iat}: id는 r + 숫자 3자리`);
        if (ids.has(it.id)) errors.push(`${iat}: id가 겹친다`);
        ids.add(it.id);
        if (!RULINGS_SOURCES.includes(it.source)) errors.push(`${iat}: source는 ${RULINGS_SOURCES.join(' · ')}`);
        rows.push({ id: it.id, group: g.key, groupTitle: g.title, section: s.key, sectionTitle: s.title, q: it.q, a: it.a, source: it.source });
      }
    }
  }
  return rows;
}

/** → { files: { 'rulings.<lang>.json': 글 }, rows: { <lang>: 항목 수 }, summary } */
export function buildRulings(root, table, errors) {
  const by = {};
  for (const lang of RULINGS_LANGS) by[lang] = rowsOf(root, lang, errors);
  const [a, b] = RULINGS_LANGS;
  const shape = rows => rows.map(r => `${r.section}/${r.id}/${r.source}`);
  const sa = shape(by[a]), sb = shape(by[b]);
  if (sa.join() !== sb.join()) {
    const only = (x, y) => x.filter(k => !y.includes(k)).slice(0, 5).join(' ');
    errors.push(`${RULINGS_DIR}: ${a} · ${b}의 절 · 항목 id · 출처(또는 순서)가 다르다 — ${a}에만 [${only(sa, sb)}] · ${b}에만 [${only(sb, sa)}]`);
  }
  const files = {}, rows = {};
  for (const lang of RULINGS_LANGS) {
    files[`rulings.${lang}.json`] = table(by[lang]);
    rows[lang] = by[lang].length;
  }
  return { files, rows, summary: `규칙 판정 ${RULINGS_LANGS.map(l => `${l} ${rows[l]}항목`).join(' · ')}` };
}
