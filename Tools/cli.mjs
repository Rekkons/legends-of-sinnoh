// 명령줄 선택 거르기(오래 걸리거나 파일을 쓰는 Tools/ 스크립트 공용, plan §4 M4-1 남은 일). 일을 시작하기 전에 부른다.
//   --help·-h            사용법(stdout) → 종료 코드 0
//   모르는 선택·값 없는 선택(빈 값, 「-」로 시작하는 값 포함)·받지 않는 위치 인자 → 이유 + 사용법(stderr) → 종료 코드 2
// options는 node:util parseArgs 형식({ only: { type: 'string' }, check: { type: 'boolean' } }). 반환 { values, positionals }.
import { parseArgs } from 'node:util';

export function cli(usage, options = {}, { positionals = false, args = process.argv.slice(2) } = {}) {
  let r;
  try {
    r = parseArgs({ args, options: { ...options, help: { type: 'boolean', short: 'h' } }, allowPositionals: positionals, strict: true });
  } catch (e) { usageError(usage, e.message); }
  if (r.values.help) { console.log(usage); process.exit(0); }
  for (const [k, v] of Object.entries(r.values)) if (v === '' || (Array.isArray(v) && v.includes(''))) usageError(usage, `--${k}에 값이 없다`);
  return r;
}

export function usageError(usage, why) {
  console.error(`${why}\n${usage}`);
  process.exit(2);
}
