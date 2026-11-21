// Tools/sources.json 읽기 + git 밖 원천의 뿌리(03 §4-2·§4-3·§7).
// sources.json의 상대 경로는 저장소 뿌리 기준이다. 다만 git 밖 원천(Research/raw/…, AssetSources/…)은 환경 변수 SINNOH_CACHE_ROOT
// (rebuild-all --cache-root <본 작업 트리>)가 있으면 그 뿌리에서 찾는다 — 빈 worktree가 원천을 복사하지 않고 본 작업 트리 캐시를 읽는다.
// 변수가 없으면 작업 트리 설정 파일 UserSettings/SinnohCacheRoot.txt(git 밖, rebuild-all --cache-root가 씀)를 본다 — 그 작업 트리의 에디터(FigureImport)도 같은 파일을 읽는다.
// 추적하는 입력(Research/*.csv, Tools/…)은 늘 이 작업 트리에서 읽는다.
import { readFileSync, existsSync } from 'node:fs';
import path from 'node:path';

const CACHE = /^(Research\/raw|AssetSources)(\/|$)/;
/** 작업 트리 설정: git 밖 원천 뿌리(한 줄). rebuild-all --cache-root가 쓰고, 지우면 이 작업 트리 자신을 쓴다 */
export const CACHE_ROOT_FILE = 'UserSettings/SinnohCacheRoot.txt';

export function loadSources(root) {
  return JSON.parse(readFileSync(path.join(root, 'Tools/sources.json'), 'utf8'));
}

/** git 밖 원천의 뿌리: SINNOH_CACHE_ROOT, 없으면 UserSettings/SinnohCacheRoot.txt, 없으면 root */
export function cacheRoot(root) {
  const c = process.env.SINNOH_CACHE_ROOT;
  if (c) return path.resolve(c);
  const f = path.join(root, CACHE_ROOT_FILE);
  const t = existsSync(f) ? readFileSync(f, 'utf8').trim() : '';
  return t ? path.resolve(t) : root;
}

/** 저장소 기준 상대 경로(또는 절대 경로) → 실제 경로. git 밖 원천은 cacheRoot 아래. 뒤 인자는 path.join처럼 붙인다 */
export function srcPath(root, rel, ...more) {
  if (path.isAbsolute(rel)) return path.join(rel, ...more);
  return path.join(CACHE.test(rel.replace(/\\/g, '/')) ? cacheRoot(root) : root, rel, ...more);
}
