// 「규칙 판정」 화면의 손 파일을 검토용 md 초안에서 다시 만든다(결정 213 — 꼴의 정본은 lib/rulings.mjs 머리 주석).
//   node Tools/data/rulings-import.mjs <폴더>   <폴더>/<묶음>-ko.md · <묶음>-en.md → Tools/data/authored/rulings/ko.json · en.json
//   node Tools/data/rulings-import.mjs --self-test
// md 꼴: `# 묶음 제목` / `## 절 제목` / `**N. 물음**` 다음 줄부터 답, 답 끝에 백틱 꼬리표 —
//   ko `[원작자 답변]` · `[규칙서 해석]` · `[이 게임에서 정함]`, en `[Creator's answer]` · `[Rulebook reading]` · `[Decided for this game]`.
//   `---` 줄부터는 읽지 않는다. 번호는 건너뛰어도 된다(id = r + 번호 3자리 — 지운 번호는 다시 쓰지 않는다). 절 키 = <묶음>.s<그 묶음 안 순서 2자리>.
// 영어 md가 하나도 없으면 en.json은 ko.json의 임시 사본이다(영어 md가 일부만 있으면 오류). 쓴 뒤에는 `node Tools/data/build.mjs`로 표를 다시 만든다.
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { RULINGS_DIR, RULINGS_GROUPS } from './lib/rulings.mjs';
import { cli, usageError } from '../cli.mjs';

const USAGE = '사용: node Tools/data/rulings-import.mjs <md 폴더> | --self-test';
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const TAGS = {
  ko: { '원작자 답변': 'creator', '규칙서 해석': 'rulebook', '이 게임에서 정함': 'house' },
  en: { "Creator's answer": 'creator', 'Rulebook reading': 'rulebook', 'Decided for this game': 'house' },
};

/** md 한 파일 → { title, sections: [ { key, title, items } ] }. 잘못된 곳은 errors에. */
export function parseGroup(md, group, lang, at, errors) {
  const out = { key: group, title: null, sections: [] };
  const lines = md.replace(/^﻿/, '').split(/\r?\n/);
  let item = null, section = null;
  const close = n => {
    if (!item) return;
    const text = item.lines.join('\n').trim();
    const m = /^([\s\S]*?)\s*`\[([^\]`]+)\]`$/.exec(text);
    const source = m && TAGS[lang][m[2]];
    if (!source) errors.push(`${at}:${item.line} ${item.id}: 답 끝에 꼬리표(${Object.keys(TAGS[lang]).map(t => `\`[${t}]\``).join(' · ')})가 없다`);
    else if (!m[1]) errors.push(`${at}:${item.line} ${item.id}: 답이 비었다`);
    else section.items.push({ id: item.id, q: item.q, a: m[1], source });
    item = null;
  };
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i], n = i + 1;
    if (/^---\s*$/.test(line)) break;
    let m;
    if ((m = /^# (.+)$/.exec(line))) {
      close();
      if (out.title) errors.push(`${at}:${n}: 묶음 제목이 둘이다`);
      out.title = m[1].trim();
    } else if ((m = /^## (.+)$/.exec(line))) {
      close();
      section = { key: `${group}.s${String(out.sections.length + 1).padStart(2, '0')}`, title: m[1].trim(), items: [] };
      out.sections.push(section);
    } else if ((m = /^\*\*(\d+)\. (.+)\*\*\s*$/.exec(line))) {
      close();
      if (!section) { errors.push(`${at}:${n}: 절(## ) 앞에 항목이 있다`); continue; }
      if (m[1].length > 3) errors.push(`${at}:${n}: 번호가 세 자리를 넘는다`);
      item = { id: 'r' + m[1].padStart(3, '0'), q: m[2].trim(), lines: [], line: n };
    } else if (item) item.lines.push(line);
    else if (line.trim()) errors.push(`${at}:${n}: 항목 밖의 글 「${line.slice(0, 30)}」`);
  }
  close();
  if (!out.title) errors.push(`${at}: 묶음 제목(# )이 없다`);
  for (const s of out.sections) if (!s.items.length) errors.push(`${at}: 절 「${s.title}」에 항목이 없다`);
  return out;
}

const fileText = groups => JSON.stringify({
  schema: 1,
  note: '「규칙 판정」 화면의 물음 · 답(결정 213). Tools/data/rulings-import.mjs가 검토용 md에서 만든다 — 손으로 고치면 다음 가져오기에 덮인다. 꼴 · 검사는 Tools/data/lib/rulings.mjs.',
  groups,
}, null, 2) + '\n';

function importDir(dir) {
  const errors = [], by = {};
  const has = lang => RULINGS_GROUPS.filter(g => existsSync(path.join(dir, `${g}-${lang}.md`)));
  const en = has('en');
  if (has('ko').length !== RULINGS_GROUPS.length) errors.push(`${dir}: 한국어 md(${RULINGS_GROUPS.map(g => g + '-ko.md').join(' · ')})가 모자란다`);
  if (en.length && en.length !== RULINGS_GROUPS.length) errors.push(`${dir}: 영어 md가 일부만 있다(${en.join(',')})`);
  if (errors.length) return { errors };
  for (const lang of en.length ? ['ko', 'en'] : ['ko'])
    by[lang] = RULINGS_GROUPS.map(g => {
      const file = path.join(dir, `${g}-${lang}.md`);
      return parseGroup(readFileSync(file, 'utf8'), g, lang, `${g}-${lang}.md`, errors);
    });
  return { errors, ko: by.ko, en: by.en ?? by.ko, copied: !by.en };
}

function selfTest() {
  const bad = [], eq = (what, got, want) => { if (JSON.stringify(got) !== JSON.stringify(want)) bad.push(`${what}: ${JSON.stringify(got)} ≠ ${JSON.stringify(want)}`); };
  let e = [];
  const g = parseGroup('# 묶음\n\n## 첫 절\n\n**1. 물음?**\n답 한 줄. `[규칙서 해석]`\n\n**3. 둘째**\n첫 줄\n둘째 줄 `[이 게임에서 정함]`\n\n## 둘째 절\n\n**12. 셋째**\n답 `[원작자 답변]`\n\n---\n\n# 부록\n', 'basic', 'ko', 't', e);
  eq('오류 없음', e, []);
  eq('묶음', [g.key, g.title, g.sections.map(s => s.key)], ['basic', '묶음', ['basic.s01', 'basic.s02']]);
  eq('항목', g.sections[0].items, [{ id: 'r001', q: '물음?', a: '답 한 줄.', source: 'rulebook' }, { id: 'r003', q: '둘째', a: '첫 줄\n둘째 줄', source: 'house' }]);
  eq('셋째', g.sections[1].items[0], { id: 'r012', q: '셋째', a: '답', source: 'creator' });
  e = [];
  eq('영어 꼬리표', parseGroup("# G\n## S\n**2. Q**\nA `[Creator's answer]`\n", 'ultra', 'en', 't', e).sections[0].items[0].source, 'creator');
  eq('영어 오류 없음', e, []);
  e = [];
  parseGroup('# G\n## S\n**1. Q**\n꼬리표 없음\n', 'basic', 'ko', 't', e);
  eq('꼬리표 없음 = 오류', e.some(x => x.includes('꼬리표')), true);
  e = [];
  parseGroup('# G\n## S\n**1. Q**\nA `[Rulebook reading]`\n', 'basic', 'ko', 't', e);
  eq('다른 언어 꼬리표 = 오류', e.some(x => x.includes('꼬리표')), true);
  for (const b of bad) console.error('실패: ' + b);
  console.log(bad.length ? `rulings-import self-test: ${bad.length}건 실패` : 'rulings-import self-test: OK');
  process.exit(bad.length ? 1 : 0);
}

if (process.argv.includes('--self-test')) selfTest();
const { positionals } = cli(USAGE, {}, { positionals: true });
if (positionals.length !== 1) usageError(USAGE, 'md 폴더 하나를 준다');
const r = importDir(path.resolve(positionals[0]));
if (r.errors.length) {
  for (const e of r.errors) console.error(e);
  console.error(`rulings-import: 오류 ${r.errors.length}건 — 쓰지 않았다`);
  process.exit(1);
}
const out = path.join(ROOT, RULINGS_DIR);
mkdirSync(out, { recursive: true });
writeFileSync(path.join(out, 'ko.json'), fileText(r.ko));
writeFileSync(path.join(out, 'en.json'), fileText(r.en));
const count = groups => groups.reduce((n, g) => n + g.sections.reduce((m, s) => m + s.items.length, 0), 0);
console.log(`rulings-import: ko ${count(r.ko)}항목 · en ${r.copied ? '영어 md 없음 — ko 사본(임시)' : count(r.en) + '항목'} → ${RULINGS_DIR}/ (이어서 node Tools/data/build.mjs)`);
