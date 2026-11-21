// 도구 출력 폴더 비우기 — 지우는 것은 앞 실행이 만든 파일뿐(M9 사고 11-20: rom.mjs가 rom/pt/vgmtrans/의 사용자 내보내기를 지운 뒤 같은 원칙으로).
//   const prior = clearMade(dir[, { own }])   일 시작 전: 목록(<dir>/_made.txt)에 적힌 파일 + own(이 도구가 늘 쓰는 이름의 정규식 — 낡은 결과를 새것으로 읽지 않게)만 지운다.
//                                             목록에 없는 파일은 남기고 prior.foreign에 돌려준다. 목록이 없으면 own 밖은 아무것도 지우지 않는다. 빈 폴더도 그대로 둔다.
//   recordMade(dir, prior.foreign)            일 끝: 지금 폴더의 파일 가운데 남의 파일을 뺀 것을 목록으로 쓴다(남의 파일과 같은 이름으로 덮어쓴 것도 빼 둔다 — 다음에도 지우지 않는다)
//   foreignNote(tag, dir, foreign)            남의 파일이 있으면 경고 한 줄(없으면 '')
// 자기 시험 selfTestMade()는 node Tools/assets/rom.mjs --self-test가 함께 돈다(lib/는 관문 연기 검사의 진입점이 아니다).
import { existsSync, readFileSync, writeFileSync, readdirSync, unlinkSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';

export const MADE = '_made.txt';

function walk(dir, base = dir) {
  if (!existsSync(dir)) return [];
  return readdirSync(dir, { withFileTypes: true }).flatMap(e => {
    const p = path.join(dir, e.name);
    return e.isDirectory() ? walk(p, base) : [path.relative(base, p).split(path.sep).join('/')];
  }).sort();
}

export function clearMade(dir, { own = null } = {}) {
  const list = path.join(dir, MADE);
  const prev = new Set(existsSync(list) ? readFileSync(list, 'utf8').split('\n').filter(Boolean) : []);
  let removed = 0;
  const foreign = [];
  for (const rel of walk(dir)) {
    if (rel === MADE) continue;
    if (prev.has(rel) || (own && own.test(rel))) { unlinkSync(path.join(dir, rel)); removed++; } else foreign.push(rel);
  }
  return { removed, foreign };
}

export function recordMade(dir, foreign = []) {
  const skip = new Set([...foreign, MADE]);
  mkdirSync(dir, { recursive: true });
  writeFileSync(path.join(dir, MADE), walk(dir).filter(r => !skip.has(r)).join('\n') + '\n');
}

export const foreignNote = (tag, dir, foreign) => foreign.length
  ? `[${tag}] 경고: 이 도구가 만들지 않은 파일 ${foreign.length}개를 그대로 둠(${dir} — 예: ${foreign.slice(0, 3).join(' · ')})` : '';

/** 자기 시험 → 틀린 것 목록(빈 목록 = 통과): 남의 파일은 남는다 · 앞 실행 파일만 사라진다 · 목록이 없으면 own 밖은 안 지운다 */
export function selfTestMade() {
  const base = mkdtempSync(path.join(tmpdir(), 'sinnoh-made-')), bad = [];
  const put = rel => { const p = path.join(base, rel); mkdirSync(path.dirname(p), { recursive: true }); writeFileSync(p, rel); };
  const has = rel => existsSync(path.join(base, rel));
  const eq = (what, a, b) => { if (JSON.stringify(a) !== JSON.stringify(b)) bad.push(`made ${what}: ${JSON.stringify(a)} ≠ ${JSON.stringify(b)}`); };
  try {
    put('user-note.txt'); put('old.json');
    const p0 = clearMade(base, { own: /^report\.json$/ });
    eq('목록 없음 — 지움 0', [p0.removed, p0.foreign], [0, ['old.json', 'user-note.txt']]);
    put('report.json'); put('a/1.wav');
    recordMade(base, p0.foreign);
    eq('목록 = 이번 것만', readFileSync(path.join(base, MADE), 'utf8'), 'a/1.wav\nreport.json\n');
    const p1 = clearMade(base, { own: /^report\.json$/ });
    eq('앞 실행 파일만 사라진다', [p1.removed, has('a/1.wav'), has('report.json')], [2, false, false]);
    eq('남의 파일은 남는다', [has('user-note.txt'), has('old.json'), p1.foreign.length], [true, true, 2]);
    put('report.json');
    eq('own 이름은 목록 없이도 지운다', [rmSync(path.join(base, MADE)), clearMade(base, { own: /^report\.json$/ }).removed, has('report.json'), has('user-note.txt')], [undefined, 1, false, true]);
  } catch (e) { bad.push('made 예외: ' + e.message); } finally { rmSync(base, { recursive: true, force: true }); }
  return bad;
}
