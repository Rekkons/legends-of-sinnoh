// commit-task.mjs·gate.mjs 공용: 「HEAD + 지정 파일」 트리 펼치기, 관문 지문, 영수증(결정 39 ①, 하네스 재검토 보고 §4).
// 두 스크립트가 같은 함수로 트리를 만들어야 지문이 같다 — 관문이 시험한 트리와 커밋할 트리를 지문으로 맞춘다.
//  - 목록 펼치기: 파일 + .meta(git status에 있으면), 폴더는 그 아래 바뀐 파일 전부, 이름 바꿈은 양쪽 경로(commit-task에서 옮김).
//  - 지문 = 임시 index(HEAD + 지정 경로의 워크트리 내용)의 `git ls-files -s`에서 문서 경로(isDocPath)를 뺀 sha256.
//  - 영수증 = Logs/gate/receipts.jsonl(누적, 통과만)·receipt.json(마지막). Logs/는 git 밖이고 배치 종료 때 지워지는 Temp/와 달리 남는다.
//  - 연결된 로컬 패키지 저장소(결정 51 — Packages/manifest.json의 file: 의존 중 프로젝트 밖 폴더, 지금은 공용 UI 키트 ../uikit):
//    Unity·dotnet은 그 저장소의 작업 트리를 컴파일하므로 영수증에 그 지문도 적는다(receipt.linked[] = { name, fingerprint, tree, head }).
//    지문 = 그 저장소 작업 트리 전체(HEAD + 미커밋 변경 모두)에 같은 식 — 커밋해도 값이 같아 「관문 → 키트 커밋 → 프로젝트 커밋」에서 영수증이 그대로 유효하다.
//    다만 연결 저장소의 문서 경로는 *.md뿐이다(isLinkedDocPath) — 저장소 루트가 패키지 루트라 Docs/ 같은 폴더도 Unity가 들여오고 컴파일한다.
//    프로젝트 커밋은 (프로젝트 지문, 연결 지문들)이 모두 같은 영수증이, 연결 저장소 커밋은 그 지문을 본 full 영수증이 있어야 한다.
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { readFileSync, existsSync, mkdtempSync, rmSync, copyFileSync, mkdirSync, writeFileSync, appendFileSync, realpathSync } from 'node:fs'
import { join, resolve, relative, isAbsolute, basename } from 'node:path'

// ---------- 순수 함수 ----------

/** 경로 정규화: 역슬래시→/, 앞 ./ 제거, 끝 / 제거, 저장소 루트 절대경로면 상대로. */
export function normPath(p, root) {
  let s = String(p ?? '').trim().replace(/\\/g, '/')
  if (root) {
    const r = String(root).replace(/\\/g, '/').replace(/\/+$/, '')
    if (s.toLowerCase().startsWith(r.toLowerCase() + '/')) s = s.slice(r.length + 1)
  }
  s = s.replace(/^(\.\/)+/, '').replace(/\/{2,}/g, '/').replace(/\/+$/, '')
  return s
}

/** git status --porcelain -z 출력 → [{xy, path, orig?}]. 이름 바꿈(R/C)은 뒤따르는 원래 경로를 orig로. */
export function parseStatusZ(out) {
  const parts = String(out).split('\0')
  const res = []
  for (let i = 0; i < parts.length; i++) {
    const e = parts[i]
    if (e.length < 4) continue
    const xy = e.slice(0, 2), path = e.slice(3)
    const it = { xy, path }
    if (xy[0] === 'R' || xy[0] === 'C' || xy[1] === 'R' || xy[1] === 'C') it.orig = parts[++i]
    res.push(it)
  }
  return res
}

/** 요청 경로들을 status 항목에 대응시킨다. 반환 { paths:[...정렬], hit:Set(요청 중 하나라도 맞은 것) } */
export function expandTargets(files, status) {
  const paths = new Set()
  const hit = new Set()
  const take = (it) => { paths.add(it.path); if (it.orig) paths.add(it.orig) }
  for (const f of files) {
    for (const cand of [f, f + '.meta']) {
      for (const it of status) {
        if (it.path === cand || it.path.startsWith(cand + '/') || it.orig === cand || (it.orig && it.orig.startsWith(cand + '/'))) {
          take(it); hit.add(f)
        }
      }
    }
  }
  return { paths: [...paths].sort(), hit }
}

/** 모든 변경(지정 목록이 없을 때): status의 경로와 이름 바꿈 원래 경로. */
export function allTargets(status) {
  return [...new Set(status.flatMap(it => it.orig ? [it.path, it.orig] : [it.path]))].sort()
}

/** index에서 지워진 경로: D_ 항목과 이름 바꿈(R_)의 원래 경로. 같은 경로의 ??는 add하면 그 지움·이름 바꿈이 되돌려진다. */
export function indexGone(status) {
  const gone = new Set()
  for (const it of status) {
    if (it.xy[0] === 'D') gone.add(it.path)
    if (it.orig && it.xy[0] === 'R') gone.add(it.orig)
  }
  return gone
}

/** 워크트리 쪽 변경이 있는 경로(?? 또는 Y≠' '). 이것만 git add -A 한다. 이름 바꿈의 원래 경로는 index 쪽 변경이라 들지 않는다.
 *  항목 단위로 본다: index에서 지워진 경로(indexGone)의 ??는 넣지 않는다(leftUntracked). */
export function worktreeSide(status) {
  const gone = indexGone(status)
  return new Set(status.filter(it => (it.xy === '??' && !gone.has(it.path)) || (it.xy !== '??' && it.xy[1] !== ' ')).map(it => it.path))
}

/** index 쪽 지움·이름 바꿈과 같은 경로에 있는 ?? — add하지 않고 추적 안 함으로 남긴다. */
export function leftUntracked(status) {
  const gone = indexGone(status)
  return new Set(status.filter(it => it.xy === '??' && gone.has(it.path)).map(it => it.path))
}

/** git ls-files -s -z 출력에서 paths의 index 항목 줄(update-index --index-info에 그대로)과 index에 없는 경로(gone). */
export function indexEntries(lsOut, paths) {
  const want = new Set(paths)
  const entries = String(lsOut).split('\0').filter(e => e.includes('\t') && want.has(e.slice(e.indexOf('\t') + 1)))
  const present = new Set(entries.map(e => e.slice(e.indexOf('\t') + 1)))
  return { entries, gone: paths.filter(p => !present.has(p)) }
}

/** 문서 경로: 지문에서 빼고, 이것만 바뀐 커밋은 영수증 없이 통과. *.md + Docs/(목업 HTML 등 — 코드·빌드·시험이 읽지 않는다) [가정 T1]. */
export function isDocPath(p) {
  return /\.md$/i.test(p) || p.startsWith('Docs/')
}

/** 연결 저장소(키트)의 문서 경로: *.md만. 프로젝트의 Docs/는 Assets/ 밖이라 Unity가 읽지 않지만, 연결 저장소는 루트가 곧 패키지 루트라
 *  Docs/ 아래도 Unity가 들여오고 asmdef가 있으면 컴파일한다(단계 검토 — isDocPath를 그대로 써서 키트 Docs/ 아래 코드가 영수증 없이 커밋됐다). */
export function isLinkedDocPath(p) {
  return /\.md$/i.test(p)
}

/** 관문 층. 뒤로 갈수록 넓다. md = precheck만, fast = + dotnet·조건부 --check, tools = fast + 진입점 연기 검사,
 *  branch(가지 층, 결정 103 ③) = fast(+ Tools/가 바뀌었으면 연기 검사) + 고른 PlayMode·EditorAssets 시험, full = 전부. */
export const TIERS = ['md', 'fast', 'tools', 'branch', 'full']
export const tierRank = t => TIERS.indexOf(t)

/** Gen 출력을 바꾸는 도구(에셋 빌드·원천 표): Gen 본체는 git 밖이라 커밋 경로에 안 보여도 Unity EditorAssets 시험(Gen 수·들여오기 설정)이 봐야 한다. */
export const genTool = p => p.startsWith('Tools/assets/') || p === 'Tools/sources.json'

/** 바뀐 경로로 필요한 층: 문서만 → md, 문서 밖이 모두 Tools/(genTool 빼고) → tools, 그 밖(C#·씬·에셋·Packages·ProjectSettings·Data/Json·Tools/assets…) → full. */
export function neededTier(paths) {
  const code = paths.filter(p => !isDocPath(p))
  if (!code.length) return 'md'
  return code.every(p => p.startsWith('Tools/') && !genTool(p)) ? 'tools' : 'full'
}

/** 커밋에 필요한 층(commit-task·--verify-head): 주 작업 트리는 neededTier 그대로, 연결된 작업 트리(형제 작업 폴더 — isLinkedWorktree)는
 *  full 대신 branch(결정 103 ③ — 전체 관문은 main에 합칠 때 본 폴더에서 한 번). 문서만·Tools만 규칙은 어디서나 같다. */
export function commitTier(paths, worktree = false) {
  const need = neededTier(paths)
  return worktree && need === 'full' ? 'branch' : need
}

/** 지문: ls-files -s -z 출력에서 문서 경로를 뺀 줄들의 sha256. isDoc = 그 저장소의 문서 규칙(연결 저장소는 isLinkedDocPath). */
export function fingerprintFromLs(lsOut, isDoc = isDocPath) {
  const lines = String(lsOut).split('\0').filter(e => e.includes('\t') && !isDoc(e.slice(e.indexOf('\t') + 1)))
  return createHash('sha256').update(lines.join('\n')).digest('hex')
}

/** 연결 저장소 지문 묶음의 견줌 열쇠(이름순). 연결 저장소가 없거나 linked가 없는 옛 영수증은 ''. */
export const linkedKey = list => (list ?? []).map(l => `${l.name}=${l.fingerprint}`).sort().join(',')

/** 같은 지문에 필요한 층 이상인 통과 영수증(가장 최근). receipts는 오래된 것부터. also(영수증 → 참·거짓)를 주면 그것도 맞아야 한다(연결 저장소 지문 견주기). */
export function findReceipt(receipts, fingerprint, tier, also) {
  for (let i = receipts.length - 1; i >= 0; i--) {
    const r = receipts[i]
    if (r && r.ok && r.fingerprint === fingerprint && tierRank(r.tier) >= tierRank(tier) && (!also || also(r))) return r
  }
  return null
}

/** 연결 저장소(name)의 이 지문을 본 full 통과 영수증(가장 최근) — Unity가 그 내용을 컴파일·시험했다는 증거. 프로젝트 지문은 가리지 않는다. */
export function findLinkedReceipt(receipts, name, fingerprint) {
  for (let i = receipts.length - 1; i >= 0; i--) {
    const r = receipts[i]
    if (r && r.ok && r.tier === 'full' && (r.linked ?? []).some(l => l.name === name && l.fingerprint === fingerprint)) return r
  }
  return null
}

/** 명령줄에 붙일 경로(공백·따옴표가 있으면 큰따옴표). */
export function shellQuote(p) {
  return /^[\w./@:+-]+$/u.test(p) ? p : `"${String(p).replace(/"/g, '\\"')}"`
}

// ---------- git ----------

export function git(cwd, args, input, extraEnv) {
  const r = spawnSync('git', args[0] === 'check-ignore' ? args : ['--literal-pathspecs', ...args], {
    cwd, input, encoding: 'utf8', maxBuffer: 256 * 1024 * 1024, windowsHide: true,
    env: { ...process.env, GIT_TERMINAL_PROMPT: '0', LC_ALL: 'C', ...extraEnv },
  })
  return { code: r.status ?? 1, out: r.stdout ?? '', err: (r.stderr ?? '') + (r.error ? String(r.error) : '') }
}

/** 실패하면 던진다(메시지는 git 출력 끝). */
export function gitOk(cwd, args, input, extraEnv) {
  const r = git(cwd, args, input, extraEnv)
  if (r.code !== 0) throw new Error(`git ${args[0]} 실패: ${String(r.err || r.out).trim().slice(-400)}`)
  return r.out
}

/** 연결된 작업 트리(git worktree add로 만든 형제 작업 폴더)의 주 작업 트리 경로. 주 작업 트리이거나 git 저장소가 아니면 null.
 *  판별: git-dir ≠ git-common-dir(연결된 작업 트리의 git-dir은 <주>/.git/worktrees/<이름>). */
export function mainWorktreeOf(root) {
  const r = git(root, ['rev-parse', '--path-format=absolute', '--git-dir', '--git-common-dir'])
  if (r.code !== 0) return null
  const [gd, common] = r.out.split(/\r?\n/).map(x => x.trim()).filter(Boolean)
  const n = p => resolve(p).replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase()
  if (!gd || !common || n(gd) === n(common)) return null
  return resolve(common, '..').replace(/\\/g, '/')
}
export const isLinkedWorktree = root => mainWorktreeOf(root) !== null

export function readStatus(root) {
  return parseStatusZ(gitOk(root, ['status', '--porcelain', '-z', '-uall']))
}

/** env(GIT_INDEX_FILE)의 index를 HEAD(없으면 빈 것)로 되돌리고 paths만 lsOut(다른 index의 ls-files -s -z)의 항목으로. 실패하면 이유 문자열. */
export function fillIndex(root, env, lsOut, paths) {
  const { entries, gone } = indexEntries(lsOut, paths)
  const hasHead = git(root, ['rev-parse', '--verify', '-q', 'HEAD']).code === 0
  const steps = [
    [hasHead ? ['read-tree', '--reset', 'HEAD'] : ['read-tree', '--empty']],
    ...(entries.length ? [[['update-index', '-z', '--index-info'], entries.join('\0') + '\0']] : []),
    ...(gone.length ? [[['update-index', '-z', '--force-remove', '--stdin'], gone.join('\0') + '\0']] : []),
  ]
  for (const [args, input] of steps) {
    const r = git(root, args, input, env)
    if (r.code !== 0) return `임시 index 만들기 실패(git ${args[0]}): ${String(r.err || r.out).trim().slice(-400)}`
  }
  return null
}

function withTempIndex(root, fn) {
  const gd = gitOk(root, ['rev-parse', '--absolute-git-dir']).trim()
  const real = resolve(root, gitOk(root, ['rev-parse', '--git-path', 'index']).trim())
  const dir = mkdtempSync(join(gd, 'gate-tree-'))
  try {
    const env = { GIT_INDEX_FILE: join(dir, 'index') }
    if (existsSync(real)) copyFileSync(real, env.GIT_INDEX_FILE)
    return fn(env)
  } finally { rmSync(dir, { recursive: true, force: true }) }
}

/** 커밋할 트리(HEAD + files의 워크트리 내용, files가 null이면 모든 변경)를 실제 index를 건드리지 않고 만든다.
 *  반환 { targets, paths, hit, toAdd, fingerprint, tree(트리 객체 id), status }. commit-task는 add 뒤 같은 fillIndex로 다시 만들어 지문을 견준다. */
export function buildTree(root, files, { status, isDoc = isDocPath } = {}) {
  status ??= readStatus(root)
  const targets = files ?? allTargets(status)
  const { paths, hit } = expandTargets(targets, status)
  const wt = worktreeSide(status)
  const toAdd = paths.filter(p => wt.has(p))
  return withTempIndex(root, env => {
    // 사본 index에 워크트리 쪽 변경을 넣고(실제 index는 그대로), 그 항목으로 HEAD + 지정 경로 index를 만든다
    if (toAdd.length) gitOk(root, ['add', '-A', '--pathspec-from-file=-', '--pathspec-file-nul'], toAdd.join('\0') + '\0', env)
    const ls = gitOk(root, ['ls-files', '-s', '-z'], undefined, env)
    const err = fillIndex(root, env, ls, paths)
    if (err) throw new Error(err)
    const fingerprint = fingerprintFromLs(gitOk(root, ['ls-files', '-s', '-z'], undefined, env), isDoc)
    const tree =gitOk(root, ['write-tree'], undefined, env).trim()
    return { targets, paths, hit, toAdd, fingerprint, tree, status }
  })
}

/** 커밋(rev)의 트리 지문 — --verify-head용. buildTree와 같은 ls-files 형식. */
export function fingerprintOfRev(root, rev, isDoc = isDocPath) {
  return withTempIndex(root, env => {
    gitOk(root, ['read-tree', rev], undefined, env)
    return fingerprintFromLs(gitOk(root, ['ls-files', '-s', '-z'], undefined, env), isDoc)
  })
}

// ---------- 연결된 로컬 패키지 저장소 ----------

/** Packages/manifest.json의 file: 의존 중 프로젝트 밖 폴더 → [{ name, spec, dir, label }]. manifest가 없으면 [](버리는 시험 저장소). */
export function linkedRepos(root) {
  const f = join(root, 'Packages/manifest.json')
  if (!existsSync(f)) return []
  const deps = JSON.parse(readFileSync(f, 'utf8').replace(/^﻿/, '')).dependencies ?? {}
  const out = []
  for (const [name, spec] of Object.entries(deps)) {
    if (typeof spec !== 'string' || !spec.startsWith('file:')) continue
    const dir = resolve(root, 'Packages', spec.slice(5))
    const rel = relative(root, dir)
    if (rel.startsWith('..') || isAbsolute(rel)) out.push({ name, spec, dir: dir.replace(/\\/g, '/'), label: basename(dir) })
  }
  return out
}

/** 연결 저장소를 쓸 수 없는 이유(폴더·package.json 없음, 자체 git 저장소가 아님 — 상위 폴더의 저장소에 얹힌 폴더 포함). 괜찮으면 null. */
export function linkedProblem(l) {
  const where = `${l.name} → ${l.dir}(Packages/manifest.json "${l.spec}")`
  if (!existsSync(join(l.dir, 'package.json'))) return `연결된 패키지 저장소 없음: ${where} — 그 자리에 저장소를 둔다`
  const top = git(l.dir, ['rev-parse', '--show-toplevel'])
  const real = p => { try { return realpathSync.native(p).toLowerCase() } catch { return resolve(p).toLowerCase() } }
  if (top.code !== 0 || real(top.out.trim()) !== real(l.dir)) return `연결된 패키지 폴더가 자체 git 저장소가 아니다: ${where}`
  return null
}

const headOf = dir => git(dir, ['rev-parse', '-q', '--verify', 'HEAD']).out.trim() || null

/** 연결 저장소의 지금 내용(HEAD + 미커밋 변경 전부) — 관문·프로젝트 커밋이 견주는 값. dirty = 문서 밖 미커밋 경로. */
export function linkedWork(l) {
  const t = buildTree(l.dir, null, { isDoc: isLinkedDocPath })
  return { name: l.name, dir: l.dir, label: l.label, fingerprint: t.fingerprint, tree: t.tree, head: headOf(l.dir), dirty: t.paths.filter(p => !isLinkedDocPath(p)) }
}

/** 연결 저장소의 HEAD 내용 — --verify-head가 견주는 값. 커밋이 없으면 빈 트리의 지문. */
export function linkedHead(l) {
  const head = headOf(l.dir)
  return { name: l.name, dir: l.dir, label: l.label, fingerprint: head ? fingerprintOfRev(l.dir, head, isLinkedDocPath) : fingerprintFromLs(''), head,
    dirty: allTargets(readStatus(l.dir)).filter(p => !isLinkedDocPath(p)) }
}

/** 연결 저장소 HEAD 지문을 만든 코드 커밋의 Gate-Skip 이유. 없으면 null. HEAD에서 부모와 지문이 같은 동안(문서만 바뀐 커밋) 내려가
 *  부모와 지문이 달라지는 커밋(또는 첫 커밋)의 메시지만 읽는다 — 위에 얹힌 문서 커밋의 Gate-Skip 줄은 인정하지 않는다
 *  (단계 검토: plain git 코드 커밋을 본문에 「Gate-Skip:」을 적은 문서 커밋으로 가리지 못하게 — 프로젝트 쪽 headVerdict와 같게). */
export function linkedSkip(l, maxDepth = 50) {
  let c = l.head
  for (let i = 0; c && i < maxDepth; i++) {
    const parent = git(l.dir, ['rev-parse', '-q', '--verify', `${c}^`]).out.trim()
    if (parent && fingerprintOfRev(l.dir, parent, isLinkedDocPath) === l.fingerprint) { c = parent; continue }
    return (gitOk(l.dir, ['log', '-1', '--format=%B', c]).match(/^Gate-Skip:\s*(.+)$/m) || [])[1] ?? null
  }
  return null
}

// ---------- 영수증 ----------

export const GATE_DIR = 'Logs/gate'

export function readReceipts(root) {
  const f = join(root, GATE_DIR, 'receipts.jsonl')
  if (!existsSync(f)) return []
  return readFileSync(f, 'utf8').split('\n').filter(Boolean).map(l => { try { return JSON.parse(l) } catch { return null } }).filter(Boolean)
}

/** 판정에 쓰는 영수증: 연결된 작업 트리에서는 주 작업 트리(본 폴더)의 영수증을 앞에 붙인다(읽기만) — 같은 지문의 full 영수증은 어느 폴더에서
 *  돌았든 같은 증거이고, 새로 만든 작업 폴더도 갈라져 나온 main 커밋의 영수증(verify-head의 아래 커밋 · 래칫 기준 · 키트 HEAD 지문)을 찾는다.
 *  반대 방향은 없다 — 본 폴더는 작업 폴더의 영수증(가지 층)을 읽지 않는다. */
export function readAllReceipts(root) {
  const main = mainWorktreeOf(root)
  return main ? [...readReceipts(main), ...readReceipts(root)] : readReceipts(root)
}

export function writeReceipt(root, receipt) {
  const d = join(root, GATE_DIR)
  mkdirSync(d, { recursive: true })
  appendFileSync(join(d, 'receipts.jsonl'), JSON.stringify(receipt) + '\n', 'utf8')
  writeFileSync(join(d, 'receipt.json'), JSON.stringify(receipt, null, 2) + '\n', 'utf8')
}
