// xlsx 읽기(03 §1-1). npm 패키지 없이: Windows tar.exe(bsdtar)로 임시 폴더에 풀고 sharedStrings·sheetN.xml을 정규식으로 읽는다.
// Git Bash의 GNU tar는 zip을 못 연다. 그래서 C:/Windows/System32/tar.exe를 직접 부른다.
import { readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import path from 'node:path';

const TAR = 'C:/Windows/System32/tar.exe';

const ENTITIES = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" };
const unescapeXml = s => s.replace(/&(#x[0-9a-fA-F]+|#\d+|[a-z]+);/g, (m, e) =>
  e[0] === '#' ? String.fromCodePoint(e[1] === 'x' ? parseInt(e.slice(2), 16) : Number(e.slice(1)))
    : e in ENTITIES ? ENTITIES[e] : (() => { throw new Error(`모르는 XML 개체 ${m}`); })());

// <si> 하나 = <t> 하나 또는 서식 조각 <r><t>…</t></r> 여러 개. 발음 표기(<rPh>)는 뺀다
const textOf = si => [...si.replace(/<rPh\b[\s\S]*?<\/rPh>/g, '').matchAll(/<t(?:\s[^>]*)?>([\s\S]*?)<\/t>|<t(?:\s[^>]*)?\/>/g)]
  .map(m => unescapeXml(m[1] ?? '')).join('');

const colIndex = letters => [...letters].reduce((n, ch) => n * 26 + ch.charCodeAt(0) - 64, 0) - 1;

/** xlsx를 풀어 시트 이름 → 2차원 문자열 배열(빈 칸 = '')로 돌려준다. 수식 칸은 저장된 값(<v>)을 쓴다. */
export function readXlsx(file) {
  if (!existsSync(file)) throw new Error(`xlsx가 없다: ${file}`);
  const dir = mkdtempSync(path.join(tmpdir(), 'sinnoh-xlsx-'));
  try {
    const r = spawnSync(TAR, ['-xf', file, '-C', dir], { encoding: 'utf8' });
    if (r.status !== 0) throw new Error(`tar.exe 실패(${r.status}): ${r.stderr || r.error}`);
    const read = p => readFileSync(path.join(dir, p), 'utf8');

    const shared = existsSync(path.join(dir, 'xl/sharedStrings.xml'))
      ? [...read('xl/sharedStrings.xml').matchAll(/<si>([\s\S]*?)<\/si>/g)].map(m => textOf(m[1])) : [];
    const rels = Object.fromEntries([...read('xl/_rels/workbook.xml.rels').matchAll(/<Relationship\b([^>]*)\/>/g)]
      .map(m => [/\bId="([^"]+)"/.exec(m[1])[1], /\bTarget="([^"]+)"/.exec(m[1])[1]]));

    const sheets = {};
    for (const m of read('xl/workbook.xml').matchAll(/<sheet\b([^>]*)\/>/g)) {
      const name = unescapeXml(/\bname="([^"]+)"/.exec(m[1])[1]);
      const target = rels[/\br:id="([^"]+)"/.exec(m[1])[1]].replace(/^\/?(xl\/)?/, '');
      const grid = [];
      for (const row of read('xl/' + target).matchAll(/<row\b([^>]*?)(?:\/>|>([\s\S]*?)<\/row>)/g)) {
        const rowNo = Number(/\br="(\d+)"/.exec(row[1])[1]);
        const cells = (grid[rowNo - 1] = []);
        for (const c of (row[2] ?? '').matchAll(/<c\b([^>]*?)(?:\/>|>([\s\S]*?)<\/c>)/g)) {
          const ref = /\br="([A-Z]+)(\d+)"/.exec(c[1]);
          if (Number(ref[2]) !== rowNo) throw new Error(`${name} 칸 ${ref[0]}이 행 ${rowNo} 밖`);
          const type = (/\bt="([^"]+)"/.exec(c[1]) || [])[1] || 'n';
          const body = c[2] ?? '';
          const v = /<v>([\s\S]*?)<\/v>/.exec(body);
          let value;
          if (type === 's') value = v ? shared[Number(v[1])] : '';
          else if (type === 'inlineStr') value = textOf(body);
          else if (type === 'n' || type === 'str' || type === 'b') value = v ? unescapeXml(v[1]) : '';
          else throw new Error(`${name} ${ref[0]}: 모르는 칸 형식 t="${type}"`);
          cells[colIndex(ref[1])] = value;
        }
      }
      sheets[name] = Array.from(grid, cells => Array.from(cells ?? [], x => x ?? ''));
    }
    return sheets;
  } finally { rmSync(dir, { recursive: true, force: true }); }
}

/** 첫 행 = 열 이름. 열 이름이 want와 정확히 같지 않으면 오류. 완전히 빈 행은 뺀다. → [{열: 값}] + 행 번호(_row, 1부터) */
export function sheetRows(sheets, name, want) {
  const grid = sheets[name];
  if (!grid) throw new Error(`xlsx에 시트 ${name}이 없다`);
  const head = grid[0] ?? [];
  const cols = head.slice(0, Math.max(head.length, want.length));
  if (cols.join('\t') !== want.join('\t')) throw new Error(`시트 ${name} 열이 다르다:\n  실제 ${cols.join(',')}\n  기대 ${want.join(',')}`);
  const out = [];
  grid.slice(1).forEach((cells, i) => {
    if (!cells || cells.every(x => x === '')) return;
    if (cells.length > want.length && cells.slice(want.length).some(x => x !== '')) throw new Error(`시트 ${name} 행 ${i + 2}: 열 이름 없는 칸에 값`);
    const o = { _row: i + 2 };
    want.forEach((c, k) => { o[c] = cells[k] ?? ''; });
    out.push(o);
  });
  return out;
}
