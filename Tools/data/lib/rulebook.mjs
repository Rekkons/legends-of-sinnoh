// 룰 설명서 글(M9 「설명서 글 ①②」, 03 §10 · README 결정 35·111, plan §5 #306):
//   authored/rulebook/chapters.json { schema, note, pages, chapters: [장 키…], expansions: [ { chapter, source, pages } ] } — 장 순서(기본 룰북 장 → 확장 장)와 원본 쪽 수.
//     기본 룰북 장의 원천은 'rulebook'(pages = 룰북 쪽 수), 확장 장(설명서 글 ②)은 저마다 원천 id와 그 원천의 쪽 수 — 절 pages는 그 원천의 1~쪽 수, 빠짐 없이.
//   authored/rulebook/<lang>/<장 키>.json { schema, note, chapter, title, sections: [ { key, title, pages, blocks } ] }
//   authored/rulebook/<lang>-terms.json { schema, note, terms: [ { re, to, why } ] } — 바꾼 용어(그 언어 글에 re가 남으면 오류)
//   → Data/Json/rulebook.<lang>.json(절 하나 = 한 줄 — 장 순서 · 절 순서 그대로, 행에 원천 source) · manifest rulebook.<lang>
// 블록(손 파일 → 출력 kind): {p} 문단 · {h} 작은 제목 · {list} / {ol} 목록(항목 = 글 또는 {text, items}) · {table: {head, rows, icons?}} 표(icons = 줄마다 첫 칸 앞 그림 키 — 결정 143 ③) ·
//   {figure, caption} 그림 자리(키만 — 자르기는 「설명서 그림」 묶음) · {q, a} 묻고 답 · {digital} 「디지털판」 꼬리표(결정 111 ④) ·
//   {cards: [문자열 키…]} 게임 표 참조(설명서 글 ②, plan §5 #315) — 글을 설명서에 다시 쓰지 않고 strings.<lang>의 키를 가리킨다(화면이 표에서 그린다):
//   trainer_card.<카드>(이름 · trainer_ability.<카드>.1·2 능력) · ultra_onslaught.<n>(.text.*) · item.<아이템>(이름 · .text · items.json recipe).
//   카드 묶음 CARD_FAMILIES(트레이너 카드 51 · 울트라 온슬롯 · 레시피 있는 아이템)는 설명서 어딘가에서 빠짐 없이 가리켜야 한다.
// 글 안 링크: [[절 키|보이는 글]] — 화면에는 원본처럼 「[보이는 글]」(예: [[crafting.balls|제작 참조]] → [제작 참조]).
// 검사: 장·절 키 문법·유일 · 장 목록 ↔ 파일 짝 · 링크 대상 있음 ·
//   그림 키 = <절 키>/<이름> 유일 · 원천마다 원본 쪽 빠짐 없음 · 바꾼 용어가 남지 않음 · 글 앞뒤·겹 띄어쓰기 없음 · 카드 키가 그 언어 strings에 있음 · 카드 묶음 빠짐 없음 ·
//   두 언어(완료 조건 2): 절 키 순서 · 절마다 원천 · 쪽 · 그림 키 · 카드 키가 같다.
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import path from 'node:path';

export const RULEBOOK_DIR = 'Tools/data/authored/rulebook';
export const RULEBOOK_LANGS = ['ko', 'en'];
export const BASE_SOURCE = 'rulebook';

const CHAPTER_KEY = /^[a-z]+(?:_[a-z]+)*$/;
const SECTION_KEY = /^([a-z]+(?:_[a-z]+)*)\.[a-z0-9]+(?:_[a-z0-9]+)*$/;
const FIGURE_KEY = /^([a-z]+(?:_[a-z]+)*\.[a-z0-9]+(?:_[a-z0-9]+)*)\/[a-z0-9]+(?:_[a-z0-9]+)*$/;
const LINK = /\[\[([^\]|]+)\|([^\]]+)\]\]/g;
const SOURCE_ID = /^[a-z]+(?:_[a-z]+)*$/;
const STRING_KEY = /^[a-z0-9_]+(?:\.[a-z0-9_]+)+$/;
const SECTION_FIELDS = ['blocks', 'key', 'pages', 'title'];

const isStr = v => typeof v === 'string' && v.length > 0;

/** 보이는 글 — 링크는 원본처럼 「[보이는 글]」 */
export const plain = s => s.replace(LINK, '[$2]');

function readJson(root, rel, errors) {
  try { return JSON.parse(readFileSync(path.join(root, rel), 'utf8')); }
  catch (e) { errors.push(`${rel}: ${e.message}`); return null; }
}

function checkText(s, at, errors) {
  if (!isStr(s)) { errors.push(`${at}: 빈 글`); return; }
  if (s !== s.trim() || /\s{2,}|\n/.test(s)) errors.push(`${at}: 앞뒤 공백 · 겹 띄어쓰기 · 줄바꿈`);
  if (/\[\[(?![^\]|]+\|[^\]]+\]\])/.test(s)) errors.push(`${at}: 링크 문법([[절 키|글]])`);
}

/** 손 블록 → 출력 블록. texts에 보이는 글을 모은다(검사용) */
function block(b, at, texts, links, figures, cards, errors) {
  if (b === null || typeof b !== 'object' || Array.isArray(b)) { errors.push(`${at}: 객체가 아니다`); return null; }
  const keys = Object.keys(b).sort().join();
  const text = (s, where) => { checkText(s, where, errors); if (isStr(s)) { texts.push(s); for (const m of s.matchAll(LINK)) links.push({ to: m[1], at: where }); } return s; };
  const item = (it, where) => {
    if (typeof it === 'string') return { text: text(it, where), items: [] };
    if (it && typeof it === 'object' && Object.keys(it).sort().join() === 'items,text' && Array.isArray(it.items) && it.items.length)
      return { text: text(it.text, where), items: it.items.map((x, j) => { if (typeof x !== 'string') errors.push(`${where}.${j}: 둘째 단계 항목은 글만`); return { text: text(x, `${where}.${j}`), items: [] }; }) };
    errors.push(`${where}: 목록 항목(글 또는 {text, items})`); return { text: '', items: [] };
  };
  switch (keys) {
    case 'p': return { kind: 'p', text: text(b.p, at) };
    case 'h': return { kind: 'h', text: text(b.h, at) };
    case 'digital': return { kind: 'digital', text: text(b.digital, at) };
    case 'a,q': return { kind: 'qa', q: text(b.q, at + ' q'), text: text(b.a, at + ' a') };
    case 'list': case 'ol': {
      const list = b.list ?? b.ol;
      if (!Array.isArray(list) || !list.length) { errors.push(`${at}: 빈 목록`); return null; }
      return { kind: keys, items: list.map((it, j) => item(it, `${at}.${j}`)) };
    }
    case 'table': {
      const t = b.table;
      const fields = t ? Object.keys(t).sort().join() : '';
      if (!t || (fields !== 'head,rows' && fields !== 'head,icons,rows') || !Array.isArray(t.head) || !Array.isArray(t.rows) || !t.rows.length) { errors.push(`${at}: 표 {head, rows, icons?}`); return null; }
      t.head.forEach((c, j) => text(c, `${at} 머리 ${j}`));
      t.rows.forEach((r, i) => { if (!Array.isArray(r) || r.length !== t.head.length) errors.push(`${at} 행 ${i}: 칸 수 ${t.head.length}`); else r.forEach((c, j) => text(c, `${at} 행 ${i}.${j}`)); });
      if (!t.icons) return { kind: 'table', head: t.head, rows: t.rows };
      // 줄마다 첫 칸 글 앞 아이콘(결정 143 ③) — 그림 키(figure 블록과 같은 문법 · 같은 그림 표)
      if (!Array.isArray(t.icons) || t.icons.length !== t.rows.length) { errors.push(`${at}: icons = 줄마다 그림 키 하나(${t.rows.length})`); return null; }
      t.icons.forEach((k, i) => {
        if (!FIGURE_KEY.test(k ?? '')) errors.push(`${at} 아이콘 ${i}: 그림 키 문법 <절 키>/<이름> — ${k}`);
        figures.push({ key: k, at });
      });
      return { kind: 'table', head: t.head, rows: t.rows, icons: t.icons };
    }
    case 'caption,figure': {
      if (!FIGURE_KEY.test(b.figure ?? '')) errors.push(`${at}: 그림 키 문법 <절 키>/<이름> — ${b.figure}`);
      figures.push({ key: b.figure, at });
      return { kind: 'figure', figure: b.figure, text: text(b.caption, at + ' caption') };
    }
    case 'cards': {
      if (!Array.isArray(b.cards) || !b.cards.length || b.cards.some(k => typeof k !== 'string' || !STRING_KEY.test(k))) { errors.push(`${at}: cards = 문자열 키 배열`); return null; }
      if (new Set(b.cards).size !== b.cards.length) errors.push(`${at}: 카드 키가 겹친다`);
      for (const k of b.cards) cards.push({ key: k, at });
      return { kind: 'cards', keys: b.cards };
    }
    default: errors.push(`${at}: 모르는 블록(${keys})`); return null;
  }
}

/** 한 언어 → { rows, figures, terms, cards }. chapters = [{ chapter, source, pages }] */
export function buildRulebookLang(root, lang, chapters, strings, errors) {
  const dir = `${RULEBOOK_DIR}/${lang}`;
  const files = existsSync(path.join(root, dir)) ? readdirSync(path.join(root, dir)).filter(f => f.endsWith('.json')).sort() : [];
  const want = chapters.map(c => c.chapter + '.json').sort();
  if (files.join() !== want.join()) errors.push(`${dir}: 장 파일이 chapters.json과 다르다 — 있음 [${files}] · 필요 [${want}]`);
  const rows = [], links = [], figures = [], cards = [], seenPages = new Map(), allTexts = [];
  for (const { chapter, source, pages } of chapters) {
    if (!seenPages.has(source)) seenPages.set(source, { pages, seen: new Set() });
    const seen = seenPages.get(source).seen;
    const rel = `${dir}/${chapter}.json`;
    if (!existsSync(path.join(root, rel))) continue;
    const j = readJson(root, rel, errors);
    if (!j) continue;
    if (j.schema !== 1 || Object.keys(j).sort().join() !== 'chapter,note,schema,sections,title') errors.push(`${rel}: 형식(schema 1, 필드 chapter·note·schema·sections·title)`);
    if (j.chapter !== chapter) errors.push(`${rel}: chapter ${j.chapter} ≠ 파일 이름`);
    checkText(j.title, `${rel} title`, errors);
    if (!Array.isArray(j.sections) || !j.sections.length) { errors.push(`${rel}: 절이 없다`); continue; }
    j.sections.forEach((s, i) => {
      const at = `${rel} 절 ${i} ${s?.key}`;
      const extra = Object.keys(s || {}).filter(k => !SECTION_FIELDS.includes(k));
      if (extra.length || !s.key || !s.title || !s.pages || !s.blocks) errors.push(`${at}: 필드(key·title·pages·blocks)${extra.length ? ' — 모르는 칸 ' + extra : ''}`);
      const m = SECTION_KEY.exec(s.key ?? '');
      if (!m || m[1] !== chapter) errors.push(`${at}: 절 키 문법 ${chapter}.<이름>`);
      if (rows.some(r => r.key === s.key)) errors.push(`${at}: 절 키가 겹친다`);
      checkText(s.title, at + ' title', errors);
      if (!Array.isArray(s.pages) || !s.pages.length || s.pages.some(p => !Number.isInteger(p) || p < 1 || p > pages)) errors.push(`${at}: pages = 1~${pages} 정수 배열(원천 ${source})`);
      else s.pages.forEach(p => seen.add(p));
      const texts = [s.title];
      const blocks = (Array.isArray(s.blocks) ? s.blocks : []).map((b, k) => block(b, `${at} 블록 ${k}`, texts, links, figures, cards, errors)).filter(Boolean);
      if (!blocks.length) errors.push(`${at}: 블록이 없다`);
      allTexts.push(...texts.map(t => ({ t: plain(t), at })));
      rows.push({ key: s.key, chapter, chapterTitle: j.title, title: s.title, source, pages: s.pages, blocks });
    });
  }
  for (const l of links) if (!rows.some(r => r.key === l.to)) errors.push(`${l.at}: 링크 대상 ${l.to}가 없다`);
  const figureKeys = new Set();
  for (const f of figures) {
    if (figureKeys.has(f.key)) errors.push(`${f.at}: 그림 키 ${f.key}가 겹친다`);
    figureKeys.add(f.key);
    const m = FIGURE_KEY.exec(f.key ?? '');
    if (m && !f.at.includes(` ${m[1]} `)) errors.push(`${f.at}: 그림 키 ${f.key}의 앞부분이 그 절 키가 아니다`);
  }
  for (const [source, { pages, seen }] of seenPages) {
    const missing = [];
    for (let p = 1; p <= pages; p++) if (!seen.has(p)) missing.push(p);
    if (missing.length) errors.push(`${dir}: 원천 ${source} 원본 쪽 빠짐 ${missing}`);
  }
  for (const c of cards) if (!(c.key in strings)) errors.push(`${c.at}: 카드 키 ${c.key}가 strings.${lang}에 없다`);
  const termsRel = `${RULEBOOK_DIR}/${lang}-terms.json`;
  const terms = existsSync(path.join(root, termsRel)) ? readJson(root, termsRel, errors) : null;
  if (terms) {
    if (terms.schema !== 1 || Object.keys(terms).sort().join() !== 'note,schema,terms' || !Array.isArray(terms.terms)) errors.push(`${termsRel}: 형식(schema 1, 필드 note·schema·terms)`);
    for (const [i, t] of (terms.terms ?? []).entries()) {
      if (!t || Object.keys(t).sort().join() !== 're,to,why' || !isStr(t.re) || !isStr(t.to) || !isStr(t.why)) { errors.push(`${termsRel} 행 ${i}: 필드 re·to·why`); continue; }
      let re;
      try { re = new RegExp(t.re, 'u'); } catch (e) { errors.push(`${termsRel} 행 ${i}: re ${e.message}`); continue; }
      for (const { t: s, at } of allTexts) if (re.test(s)) errors.push(`${at}: 바꾼 용어 /${t.re}/ → 「${t.to}」가 남아 있다 — 「${s.slice(0, 60)}」`);
    }
  }
  return { rows, figures: [...figureKeys].sort(), terms: terms?.terms?.length ?? 0, cards: new Set(cards.map(c => c.key)) };
}

/** 설명서가 빠짐 없이 가리켜야 하는 카드 묶음(plan §5 #315) — data = { strings: { en, ko }, items: 아이템 표 행, trainerCards: 트레이너 카드 표 행 } */
export function cardFamilies(data) {
  return {
    '트레이너 카드': data.trainerCards.map(r => `trainer_card.${r.key}`),
    '울트라 온슬롯': Object.keys(data.strings.en).filter(k => /^ultra_onslaught\.\d+$/.test(k)),
    '레시피': data.items.filter(r => r.recipe).map(r => r.key),
  };
}

/** 두 언어의 절 꼴 지문(완료 조건 2): 키 · 원천 · 쪽 · 그림 키 · 카드 키 */
function shape(r) {
  const figures = r.blocks.flatMap(b => b.kind === 'figure' ? [b.figure] : b.kind === 'table' ? b.icons ?? [] : []);
  const cards = r.blocks.filter(b => b.kind === 'cards').flatMap(b => b.keys);
  return JSON.stringify([r.key, r.source, r.pages, figures, cards]);
}

/** 모든 언어 → { files: { 'rulebook.<lang>.json': 내용 }, rows, summary } */
export function buildRulebook(root, table, data, errors) {
  const idx = readJson(root, `${RULEBOOK_DIR}/chapters.json`, errors);
  if (!idx) return { files: {}, rows: {}, summary: '' };
  if (idx.schema !== 1 || Object.keys(idx).sort().join() !== 'chapters,expansions,note,pages,schema' || !Array.isArray(idx.chapters) || !Array.isArray(idx.expansions) || !Number.isInteger(idx.pages))
    errors.push(`${RULEBOOK_DIR}/chapters.json: 형식(schema 1, 필드 chapters·expansions·note·pages·schema)`);
  const chapters = [...(idx.chapters ?? []).map(chapter => ({ chapter, source: BASE_SOURCE, pages: idx.pages })), ...(idx.expansions ?? [])];
  for (const [i, c] of chapters.entries())
    if (!c || Object.keys(c).sort().join() !== 'chapter,pages,source' || !CHAPTER_KEY.test(c.chapter ?? '') || !SOURCE_ID.test(c.source ?? '') || !Number.isInteger(c.pages) || c.pages < 1)
      errors.push(`${RULEBOOK_DIR}/chapters.json 장 ${i}: 장 키 문법 · { chapter, source, pages }`);
  if (new Set(chapters.map(c => c.chapter)).size !== chapters.length) errors.push(`${RULEBOOK_DIR}/chapters.json: 장 키가 겹친다`);
  const bySource = new Map();
  for (const c of chapters) if (bySource.has(c.source) && bySource.get(c.source) !== c.pages) errors.push(`${RULEBOOK_DIR}/chapters.json: 원천 ${c.source}의 쪽 수가 장마다 다르다`); else bySource.set(c.source, c.pages);
  const families = cardFamilies(data);
  const files = {}, rows = {}, summary = [], built = {};
  for (const lang of RULEBOOK_LANGS) {
    const r = buildRulebookLang(root, lang, chapters, data.strings[lang], errors);
    built[lang] = r;
    for (const [name, keys] of Object.entries(families)) {
      const missing = keys.filter(k => !r.cards.has(k));
      if (!keys.length || missing.length) errors.push(`설명서 ${lang}: 카드 묶음 「${name}」 빠짐 ${missing.length}/${keys.length} — ${missing.slice(0, 5)}`);
    }
    files[`rulebook.${lang}.json`] = table(r.rows);
    rows[lang] = r.rows.length;
    summary.push(`설명서 ${lang}: 절 ${r.rows.length} · 그림 자리 ${r.figures.length} · 카드 참조 ${r.cards.size} · 바꾼 용어 ${r.terms}`);
  }
  // 완료 조건 2: 두 언어의 절 키 · 원천 · 쪽 · 그림 키 · 카드 키가 같다(첫 언어 기준)
  const [first, ...others] = RULEBOOK_LANGS;
  for (const lang of others) {
    const a = built[first].rows, b = built[lang].rows;
    if (a.map(r => r.key).join() !== b.map(r => r.key).join()) errors.push(`설명서 ${first} ↔ ${lang}: 절 키 · 순서가 다르다`);
    else a.forEach((r, i) => { if (shape(r) !== shape(b[i])) errors.push(`설명서 ${first} ↔ ${lang} ${r.key}: 원천 · 쪽 · 그림 키 · 카드 키가 다르다 — ${shape(r)} ≠ ${shape(b[i])}`); });
  }
  return { files, rows, summary: summary.join('\n') };
}
