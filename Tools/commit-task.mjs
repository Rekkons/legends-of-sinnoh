#!/usr/bin/env node
// 지정 파일만 LLM 판단 없이 커밋하는 스크립트 (isekai Tools/commit-task.mjs에서 옮김).
//
// 사용: node Tools/commit-task.mjs [--dry-run] <<'EOF'
//   {"files":["Assets/..."],"subject":"M1: ...","body":"...","trailer":"Co-Authored-By: ..."}
// EOF
//   node Tools/commit-task.mjs --self-test     (순수 함수 시험, git을 건드리지 않음)
//
// 규칙
//  - files: 파일 또는 폴더. 각각의 .meta도 git status에 있으면 같이 넣는다. 폴더는 그 아래 바뀐 파일 전부.
//  - gitignore된 경로는 빼고 경고만. 지운 파일도 반영.
//  - 워크트리 쪽 변경(?? 또는 Y≠' ': 수정·새 파일·워크트리에서 지움)만 git add -A. index에만 있는 변경
//    (git rm·git rm --cached 뒤 무시·git mv)은 add하지 않고 그대로 커밋한다(전에는 add가 「did not match」·「ignored」로 실패).
//    판단은 status 항목 단위: 같은 경로에 index 쪽 지움(D_, 이름 바꿈의 원래 경로)과 ??가 함께 있으면 ??는 add하지 않고
//    index 상태(지움·이름 바꿈) 그대로 커밋하며 「추적 안 함으로 남음」을 알린다(전에는 add -A가 rm --cached를 되돌리거나 지정 안 한 새 파일을 넣었다).
//  - 병합·cherry-pick·revert·rebase 진행 중이면 FAILED(git의 「partial commit during a merge」 거부와 같게 — 임시 index 커밋이 그 상태를 끝내 버린다).
//  - 지정 파일만 커밋 — 임시 index(HEAD + 지정 경로의 index 항목)로 git commit. 다른 staged 변경은 그대로 남는다.
//    pathspec 커밋(git commit -- 경로)은 쓰지 않는다: rm --cached 한 무시 파일을 워크트리에서 다시 넣는다.
//  - trailer를 생략하면 DEFAULT_TRAILER(body에 Co-Authored-By 줄이 이미 있으면 붙이지 않는다). 빈 문자열이면 트레일러 없이.
//    귀속 줄은 body가 아니라 trailer 칸으로 넘긴다(세션 모델과 기본값이 다를 수 있다).
//  - 커밋할 변경이 없으면 최근 50개에서 같은 subject를 찾아 그 해시를 돌려준다. 없으면 FAILED.
//  - index.lock 충돌은 1초 간격 5번 재시도. hook은 건너뛰지 않는다.
//  - 관문 영수증(결정 39 ①): 커밋할 트리에 문서(*.md·Docs/) 밖 경로가 있으면, 같은 지문(HEAD + 지정 파일, 문서 제외 — Tools/lib/tree.mjs)에
//    필요한 층(Tools/만 → tools — Gen을 만드는 Tools/assets·sources.json은 full, 그 밖 → full) 이상의 통과 영수증(Logs/gate/receipts.jsonl)이 있어야 한다. 없으면 add하기 전에
//    「FAILED: 관문 영수증 없음 — node Tools/gate.mjs --files … 뒤 다시」. 문서만 바뀐 커밋은 통과.
//    가지 층(결정 103 ③): 연결된 작업 트리(git worktree — 형제 작업 폴더)에서는 full 대신 가지 영수증(tier branch) 이상이면 받는다
//    (주 작업 트리 = 본 폴더는 그대로 full). 작업 폴더에서는 본 폴더의 영수증도 함께 읽는다(같은 지문의 full 영수증). 연결 저장소(키트) 커밋은 어디서나 full.
//    우회는 환경 변수 SINNOH_GATE_SKIP=<이유> — 커밋 메시지 트레일러에 「Gate-Skip: <이유>」를 붙인다.
//  - 연결된 패키지 저장소(결정 51 — Packages/manifest.json의 file: 의존 중 프로젝트 밖 폴더, 지금은 공용 UI 키트 com.rekkon.uikit = ../uikit):
//    · 프로젝트 커밋의 영수증은 연결 저장소의 지금 내용(미커밋 포함) 지문까지 같아야 한다 — 관문 뒤 키트를 고치면 거부.
//    · 입력에 "repo":"<패키지 이름>"을 주면 그 저장소에 커밋한다(files는 그 저장소 기준 경로, 이 스크립트는 프로젝트 폴더에서 부른다).
//      문서 밖 경로가 있으면 커밋할 트리(HEAD + 지정 파일)의 지문을 본 full 영수증이 프로젝트 Logs/gate에 있어야 한다. 관문은 그 저장소
//      작업 트리 전체를 보므로 문서 밖 변경은 한 커밋에 모두 넣는다(일부만 넣으면 지문이 달라 거부). 문서만이면 영수증 없이 통과 —
//      연결 저장소의 문서는 *.md뿐이다(isLinkedDocPath: 저장소 루트가 패키지 루트라 Docs/ 아래도 Unity가 들여오고 컴파일한다).
//    · 순서: node Tools/gate.mjs --files <프로젝트 쪽 파일…> → commit-task {"repo":…}(키트) → commit-task(프로젝트). 지문은 커밋해도 같아 영수증 하나로 둘 다.
//  - 마지막 줄: 성공이면 해시, 실패면 "FAILED: <이유>". 종료 코드 0/1. 모르는 선택은 사용법(stderr) + FAILED, 종료 코드 2.
import { spawnSync } from 'node:child_process'
import { readFileSync, writeFileSync, mkdtempSync, rmSync, copyFileSync, existsSync } from 'node:fs'
import { join, resolve } from 'node:path'
import {
  normPath, parseStatusZ, expandTargets, indexGone, worktreeSide, leftUntracked, indexEntries, git,
  fillIndex, buildTree, fingerprintFromLs, neededTier, isDocPath, findReceipt, shellQuote,
  allTargets, linkedRepos, linkedProblem, linkedWork, linkedKey, findLinkedReceipt, isLinkedDocPath,
  commitTier, isLinkedWorktree, readAllReceipts,
} from './lib/tree.mjs'
export { normPath, parseStatusZ, expandTargets, indexGone, worktreeSide, leftUntracked, indexEntries }

// 부를 때마다 적지 않아도 이 저장소 트레일러가 빠지지 않게
export const DEFAULT_TRAILER = 'Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>'

// ---------- 순수 함수 ----------

/** stdin JSON 해석. 실패하면 { error }. "repo"(연결 저장소 패키지 이름)가 있으면 linked에서 찾아 repo로 돌려주고 files는 그 저장소 기준으로 정규화한다. */
export function parseInput(text, root, linked = []) {
  let j
  try { j = JSON.parse(text) } catch (e) { return { error: `입력 JSON 해석 실패: ${e.message}` } }
  if (!j || typeof j !== 'object') return { error: '입력이 객체가 아니다' }
  let repo = null
  if (j.repo != null) {
    repo = linked.find(l => l.name === String(j.repo).trim())
    if (!repo) return { error: `repo를 찾지 못함: ${j.repo} — Packages/manifest.json의 file: 패키지 이름(${linked.map(l => l.name).join(', ') || '없음'})` }
    root = repo.dir
  }
  const files = [...new Set((Array.isArray(j.files) ? j.files : []).map(f => normPath(f, root)).filter(Boolean))]
  if (!files.length) return { error: 'files가 비었다' }
  const subject = String(j.subject ?? '').split(/\r?\n/)[0].trim()
  if (!subject) return { error: 'subject가 비었다' }
  const body = String(j.body ?? '').replace(/\r\n/g, '\n').trim()
  // body에 귀속 줄이 이미 있으면 기본 트레일러를 또 붙이지 않는다(b706813에서 둘이 됐다)
  const trailer = String(j.trailer ?? (/^Co-Authored-By:/mi.test(body) ? '' : DEFAULT_TRAILER)).trim()
  return repo ? { files, subject, body, trailer, repo } : { files, subject, body, trailer }
}

/** 진행 중인 병합류 상태(rev-parse·git-path 결과로 판단). 없으면 null. */
export function inProgress(has) {
  for (const [what, name] of [['병합', 'MERGE_HEAD'], ['cherry-pick', 'CHERRY_PICK_HEAD'], ['revert', 'REVERT_HEAD'], ['rebase', 'rebase-merge'], ['rebase', 'rebase-apply']])
    if (has(name)) return `${what}(${name})`
  return null
}

export function buildMessage(subject, body, trailer) {
  const parts = [subject]
  if (body) parts.push(body)
  if (trailer) parts.push(trailer)
  return parts.join('\n\n') + '\n'
}

/** SINNOH_GATE_SKIP 이유를 트레일러 묶음 앞에 붙인다(한 줄로). 이유가 없으면 그대로. */
export function withGateSkip(trailer, reason) {
  const r = String(reason ?? '').replace(/\s+/g, ' ').trim()
  if (!r) return trailer
  return trailer ? `Gate-Skip: ${r}\n${trailer}` : `Gate-Skip: ${r}`
}

/** 관문 판정: 문서만 → { need:'md' }, 영수증 있음 → { need, receipt }, 우회 → { need, skip }, 없음 → { need, missing:true }.
 *  key(linkedKey — 연결 저장소의 지금 지문)를 주면 영수증의 연결 저장소 지문도 같아야 한다.
 *  worktree(연결된 작업 트리)면 full 대신 가지 층(branch) 영수증으로 받는다(commitTier, 결정 103 ③). */
export function gateVerdict(paths, fingerprint, receipts, skipReason, key, worktree = false) {
  const need = commitTier(paths, worktree)
  if (need === 'md') return { need }
  const receipt = findReceipt(receipts, fingerprint, need, key === undefined ? undefined : r => linkedKey(r.linked) === key)
  if (receipt) return { need, receipt }
  const skip = String(skipReason ?? '').trim()
  return skip ? { need, skip } : { need, missing: true }
}

/** 연결 저장소 커밋의 관문 판정(gateVerdict와 같은 모양): 문서(*.md)만 → md, 그 밖 → 그 저장소 지문(커밋할 트리)을 본 full 영수증. */
export function linkedVerdict(paths, name, fingerprint, receipts, skipReason) {
  if (paths.every(isLinkedDocPath)) return { need: 'md' }
  const receipt = findLinkedReceipt(receipts, name, fingerprint)
  if (receipt) return { need: 'full', receipt }
  const skip = String(skipReason ?? '').trim()
  return skip ? { need: 'full', skip } : { need: 'full', missing: true }
}

export function isLockError(text) {
  return /index\.lock|Unable to create '.*\.lock'|another git process|\.lock': File exists/i.test(String(text))
}

export function isNothingToCommit(text) {
  return /nothing (added )?to commit|no changes added to commit|nothing to commit/i.test(String(text))
}

/** git log %H\0%s 목록에서 subject가 같은 첫 커밋 해시. */
export function findBySubject(logText, subject) {
  for (const line of String(logText).split('\n')) {
    const k = line.indexOf('\0')
    if (k < 0) continue
    if (line.slice(k + 1).trim() === subject) return line.slice(0, k)
  }
  return null
}

// ---------- 자기 시험 ----------

function selfTest() {
  let fail = 0
  const eq = (name, got, want) => {
    const a = JSON.stringify(got), b = JSON.stringify(want)
    if (a !== b) { fail++; console.log(`FAIL ${name}\n  got  ${a}\n  want ${b}`) }
  }
  eq('norm 역슬래시', normPath('Assets\\Foo\\Bar.cs'), 'Assets/Foo/Bar.cs')
  eq('norm ./와 끝/', normPath('./Assets/Foo/'), 'Assets/Foo')
  eq('norm 절대경로', normPath('<PROJECT>\\Assets\\A.cs', '<PROJECT>'), 'Assets/A.cs')
  eq('norm 대소문자 무시 루트', normPath('<PROJECT>/Assets/A.cs', '<PROJECT>'), 'Assets/A.cs')
  eq('norm 루트 밖', normPath('E:/x/A.cs', '<PROJECT>'), 'E:/x/A.cs')
  eq('norm 이중 슬래시', normPath('a//b'), 'a/b')

  eq('input 정상', parseInput('{"files":["a\\\\b.cs","a/b.cs"],"subject":"M1: x\\ny","body":"b","trailer":"T"}'),
    { files: ['a/b.cs'], subject: 'M1: x', body: 'b', trailer: 'T' })
  eq('input body·trailer 생략', parseInput('{"files":["a"],"subject":"s"}'), { files: ['a'], subject: 's', body: '', trailer: DEFAULT_TRAILER })
  eq('input trailer 빈 문자열', parseInput('{"files":["a"],"subject":"s","trailer":""}').trailer, '')
  eq('input body에 귀속 줄이 있으면 기본 트레일러 없음', parseInput('{"files":["a"],"subject":"s","body":"b\\n\\nCo-Authored-By: X <x@y>"}').trailer, '')
  eq('input JSON 깨짐', !!parseInput('{').error, true)
  eq('input files 빔', parseInput('{"files":[],"subject":"s"}').error, 'files가 비었다')
  eq('input subject 빔', parseInput('{"files":["a"],"subject":" "}').error, 'subject가 비었다')

  const st = parseStatusZ(' M a/x.cs\0?? a/x.cs.meta\0 D a/gone.cs\0R  n/new.cs\0o/old.cs\0?? dir/f1.txt\0?? dir/sub/f2.txt\0?? dir.meta\0 M other.cs\0')
  eq('status 개수', st.length, 8)
  eq('status 이름바꿈', st[3], { xy: 'R ', path: 'n/new.cs', orig: 'o/old.cs' })
  eq('status 삭제', st[2], { xy: ' D', path: 'a/gone.cs' })
  eq('expand 파일+meta', expandTargets(['a/x.cs'], st).paths, ['a/x.cs', 'a/x.cs.meta'])
  eq('expand 폴더', expandTargets(['dir'], st).paths, ['dir.meta', 'dir/f1.txt', 'dir/sub/f2.txt'])
  eq('expand 삭제', expandTargets(['a/gone.cs'], st).paths, ['a/gone.cs'])
  eq('expand 이름바꿈', expandTargets(['o/old.cs'], st).paths, ['n/new.cs', 'o/old.cs'])
  eq('expand 접두어 오탐 없음', expandTargets(['di'], st).paths, [])
  eq('expand hit', [...expandTargets(['a/x.cs', 'zzz'], st).hit], ['a/x.cs'])
  // git rm(D_)·rm --cached 뒤 무시(D_, 파일은 디스크에)·mv(R_)·staged 수정(M_)은 add하지 않는다. MM·AD·UU는 워크트리 쪽이 있다
  const st2 = parseStatusZ(' M m\0?? n\0 D wd\0D  sd\0R  rn\0ro\0M  sm\0MM mm\0AD ad\0UU uu\0RM rm2\0ro2\0')
  eq('worktreeSide', [...worktreeSide(st2)].sort(), ['ad', 'm', 'mm', 'n', 'rm2', 'uu', 'wd'])
  // 같은 경로에 index 쪽 지움 + ??: rm --cached 뒤 무시 안 함(D_ foo + ?? foo), mv 뒤 옛 경로에 새 파일(R_ b<-a + ?? a) → ??는 add하지 않는다
  const st3 = parseStatusZ('D  foo ?? foo R  b a ?? a D  d/t.txt ?? d/t.txt ?? fresh RM c2 c1 ?? c1 ')
  eq('worktreeSide 같은 경로 D+?? R+orig ??', [...worktreeSide(st3)].sort(), ['c2', 'fresh'])
  eq('leftUntracked', [...leftUntracked(st3)].sort(), ['a', 'c1', 'd/t.txt', 'foo'])
  eq('indexGone', [...indexGone(st3)].sort(), ['a', 'c1', 'd/t.txt', 'foo'])
  eq('inProgress 없음', inProgress(() => false), null)
  eq('inProgress 병합', inProgress(n => n === 'MERGE_HEAD'), '병합(MERGE_HEAD)')
  eq('inProgress cherry-pick', inProgress(n => n === 'CHERRY_PICK_HEAD'), 'cherry-pick(CHERRY_PICK_HEAD)')
  eq('inProgress rebase', inProgress(n => n === 'rebase-apply'), 'rebase(rebase-apply)')
  eq('indexEntries', indexEntries(['100644 aaa 0\tx', '100644 bbb 0\ty y', '100644 ccc 0\tz', ''].join('\0'), ['y y', 'x', 'gone']),
    { entries: ['100644 aaa 0\tx', '100644 bbb 0\ty y'], gone: ['gone'] })
  eq('msg 전체', buildMessage('S', 'B1\nB2', 'T'), 'S\n\nB1\nB2\n\nT\n')
  eq('msg 본문 없음', buildMessage('S', '', 'T'), 'S\n\nT\n')
  eq('msg 트레일러 없음', buildMessage('S', '', ''), 'S\n')
  eq('lock 감지', isLockError("fatal: Unable to create 'D:/x/.git/index.lock': File exists."), true)
  eq('lock 아님', isLockError('error: pathspec foo did not match'), false)
  eq('nothing 감지', isNothingToCommit('On branch main\nnothing to commit, working tree clean'), true)
  eq('nothing 감지2', isNothingToCommit('no changes added to commit'), true)
  eq('subject 검색', findBySubject('aaa\0M1: x\nbbb\0M1: y\n', 'M1: y'), 'bbb')
  eq('subject 없음', findBySubject('aaa\0M1: x\n', 'M1: z'), null)
  eq('gateSkip 붙임', withGateSkip('Co-Authored-By: X', ' 급한\n수정 '), 'Gate-Skip: 급한 수정\nCo-Authored-By: X')
  eq('gateSkip 빈 트레일러', withGateSkip('', 'r'), 'Gate-Skip: r')
  eq('gateSkip 이유 없음', withGateSkip('T', ''), 'T')
  eq('문서 경로', ['a.md', 'Docs/x.html', 'CLAUDE.md', 'Tools/a.mjs', 'Assets/a.cs', 'Research/x.csv'].map(isDocPath), [true, true, true, false, false, false])
  eq('필요 층', [neededTier(['a.md', 'Docs/m.html']), neededTier(['Tools/a.mjs', 'b.md']), neededTier(['Tools/a.mjs', 'Assets/x.cs']), neededTier(['.gitignore']), neededTier(['Tools/assets/lib/cards.mjs']), neededTier(['Tools/sources.json']), neededTier(['Tools/assets/README.md', 'Tools/data/build.mjs'])], ['md', 'tools', 'full', 'full', 'full', 'full', 'tools'])
  eq('지문 문서 제외', fingerprintFromLs('100644 aa 0\tA.cs\x00100644 bb 0\tB.md\x00') === fingerprintFromLs('100644 aa 0\tA.cs\x00100644 cc 0\tB.md\x00'), true)
  eq('지문 코드 반영', fingerprintFromLs('100644 aa 0\tA.cs\x00') === fingerprintFromLs('100644 ab 0\tA.cs\x00'), false)
  const rc = [{ ok: true, fingerprint: 'f1', tier: 'tools' }, { ok: false, fingerprint: 'f2', tier: 'full' }, { ok: true, fingerprint: 'f3', tier: 'full' }]
  eq('판정 문서만', gateVerdict(['a.md'], 'zz', [], ''), { need: 'md' })
  eq('판정 tools 영수증', gateVerdict(['Tools/a.mjs'], 'f1', rc, '').receipt === rc[0], true)
  eq('판정 full에 tools 영수증은 모자람', gateVerdict(['Assets/a.cs'], 'f1', rc, ''), { need: 'full', missing: true })
  eq('판정 실패 영수증은 안 침', gateVerdict(['Assets/a.cs'], 'f2', rc, ''), { need: 'full', missing: true })
  eq('판정 full 영수증은 tools도', gateVerdict(['Tools/a.mjs'], 'f3', rc, '').receipt === rc[2], true)
  eq('판정 우회', gateVerdict(['Assets/a.cs'], 'zz', rc, '급함'), { need: 'full', skip: '급함' })
  // 가지 층(결정 103 ③): 연결된 작업 트리는 branch 영수증으로, 주 작업 트리는 그대로 full
  const rb = [{ ok: true, fingerprint: 'b1', tier: 'branch' }, { ok: true, fingerprint: 'b2', tier: 'fast' }, { ok: true, fingerprint: 'b3', tier: 'tools' }]
  eq('필요 층(작업 폴더): full만 branch로', [commitTier(['Assets/a.cs'], true), commitTier(['Tools/a.mjs'], true), commitTier(['a.md'], true), commitTier(['Tools/assets/x.mjs'], true), commitTier(['Assets/a.cs'], false), commitTier(['Assets/a.cs'])], ['branch', 'tools', 'md', 'branch', 'full', 'full'])
  eq('판정 작업 폴더: branch 영수증으로 받는다', gateVerdict(['Assets/a.cs'], 'b1', rb, '', undefined, true).receipt === rb[0], true)
  eq('판정 본 폴더: branch 영수증은 full에 모자람', gateVerdict(['Assets/a.cs'], 'b1', rb, '', undefined, false), { need: 'full', missing: true })
  eq('판정 작업 폴더: fast·tools 영수증은 branch에 모자람', [gateVerdict(['Assets/a.cs'], 'b2', rb, '', undefined, true), gateVerdict(['Assets/a.cs'], 'b3', rb, '', undefined, true)], [{ need: 'branch', missing: true }, { need: 'branch', missing: true }])
  eq('판정 작업 폴더: full 영수증은 branch도 · branch 영수증은 tools도', [gateVerdict(['Assets/a.cs'], 'f3', rc, '', undefined, true).receipt === rc[2], gateVerdict(['Tools/a.mjs'], 'b1', rb, '', undefined, true).receipt === rb[0]], [true, true])
  // 연결된 패키지 저장소(결정 51)
  const kitL = [{ name: 'com.rekkon.uikit', dir: '<UIKIT>', label: 'uikit' }]
  eq('input repo', parseInput('{"repo":"com.rekkon.uikit","files":["<UIKIT>\\\\Runtime\\\\Core","README.md"],"subject":"s","trailer":"T"}', '<PROJECT>', kitL),
    { files: ['Runtime/Core', 'README.md'], subject: 's', body: '', trailer: 'T', repo: kitL[0] })
  eq('input 모르는 repo', /^repo를 찾지 못함: uikit — .*com\.rekkon\.uikit/.test(parseInput('{"repo":"uikit","files":["a"],"subject":"s"}', 'D:/x', kitL).error), true)
  eq('연결 열쇠', [linkedKey([{ name: 'b', fingerprint: '2' }, { name: 'a', fingerprint: '1' }]), linkedKey([]), linkedKey(undefined)], ['a=1,b=2', '', ''])
  const rl = [{ ok: true, fingerprint: 'f1', tier: 'full' }, { ok: true, fingerprint: 'f1', tier: 'full', linked: [{ name: 'k', fingerprint: 'K1' }] }, { ok: true, fingerprint: 'f9', tier: 'tools', linked: [{ name: 'k', fingerprint: 'K2' }] }]
  eq('판정 연결 지문까지 같은 영수증', gateVerdict(['Assets/a.cs'], 'f1', rl, '', 'k=K1').receipt === rl[1], true)
  eq('판정 연결 지문이 다르면 없음', gateVerdict(['Assets/a.cs'], 'f1', rl, '', 'k=K2'), { need: 'full', missing: true })
  eq('판정 연결 저장소가 없으면 linked 없는 영수증', gateVerdict(['Assets/a.cs'], 'f1', rl.slice(0, 1), '', '').receipt === rl[0], true)
  eq('판정 문서만은 연결 지문을 보지 않음', gateVerdict(['a.md'], 'zz', rl, '', 'k=K9'), { need: 'md' })
  eq('연결 저장소 판정 문서만(*.md)', linkedVerdict(['README.md', 'Docs/guide.md'], 'k', 'K9', rl, ''), { need: 'md' })
  eq('연결 저장소 판정 Docs/ 아래 *.md 밖은 문서가 아니다', [linkedVerdict(['Docs/Extra.cs'], 'k', 'K9', rl, ''), linkedVerdict(['README.md', 'Docs/x.html'], 'k', 'K9', rl, '')], [{ need: 'full', missing: true }, { need: 'full', missing: true }])
  eq('지문 연결 저장소는 Docs/ 아래 코드를 넣는다', [fingerprintFromLs('100644 aa 0\tDocs/E.cs\x00') === fingerprintFromLs(''), fingerprintFromLs('100644 aa 0\tDocs/E.cs\x00', isLinkedDocPath) === fingerprintFromLs('', isLinkedDocPath), fingerprintFromLs('100644 aa 0\tDocs/g.md\x00', isLinkedDocPath) === fingerprintFromLs('')], [true, false, true])
  eq('연결 저장소 판정 full 영수증', linkedVerdict(['Runtime/Core/A.cs', 'README.md'], 'k', 'K1', rl, '').receipt === rl[1], true)
  eq('연결 저장소 판정 tools 영수증은 안 침', linkedVerdict(['package.json'], 'k', 'K2', rl, ''), { need: 'full', missing: true })
  eq('연결 저장소 판정 우회', linkedVerdict(['package.json'], 'k', 'K3', rl, '급함'), { need: 'full', skip: '급함' })
  console.log(fail ? `self-test: ${fail}건 실패` : 'self-test: OK')
  process.exit(fail ? 1 : 0)
}

// ---------- git 실행 ----------

function sleepMs(ms) { Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms) }

/** index.lock 충돌이면 1초 간격으로 최대 5번 다시. */
function gitRetry(cwd, args, input, extraEnv) {
  let r
  for (let i = 0; i <= 5; i++) {
    r = git(cwd, args, input, extraEnv)
    if (r.code === 0 || !isLockError(r.err + r.out)) return r
    if (i < 5) { console.log(`index.lock 충돌, 재시도 ${i + 1}/5`); sleepMs(1000) }
  }
  return r
}

const tail = (s, n = 400) => { const t = String(s).trim(); return t.length > n ? '…' + t.slice(-n) : t }
function fail(reason) { console.log(`FAILED: ${reason.replace(/\s*\n\s*/g, ' | ')}`); process.exit(1) }
function ok(hash) { console.log(hash); process.exit(0) }

const USAGE = `사용: node Tools/commit-task.mjs [--dry-run] <<'EOF'
  {"files":["경로 또는 폴더",...],"subject":"M4: ...","body":"...","trailer":"..."}
EOF
      node Tools/commit-task.mjs --self-test
  연결된 패키지 저장소(공용 UI 키트)에 커밋: 입력에 "repo":"com.rekkon.uikit"(files는 그 저장소 기준 경로) — 프로젝트 폴더에서 부른다`

function main() {
  const argv = process.argv.slice(2)
  if (argv.includes('--help') || argv.includes('-h')) { console.log(USAGE); process.exit(0) }
  // 모르는 선택(예: --dryrun 오타)으로 진짜 커밋하지 않게: 사용법은 stderr, 마지막 줄은 FAILED, 종료 코드 2
  const unknown = argv.filter(a => a !== '--self-test' && a !== '--dry-run')
  if (unknown.length) { console.error(USAGE); console.log(`FAILED: 모르는 선택 ${unknown.join(' ')}`); process.exit(2) }
  if (argv.includes('--self-test')) return selfTest()
  const dry = argv.includes('--dry-run')

  const top = git(process.cwd(), ['rev-parse', '--show-toplevel'])
  if (top.code !== 0) fail(`git 저장소가 아니다: ${tail(top.err)}`)
  const proj = top.out.trim()   // 프로젝트 저장소: 영수증(Logs/gate)과 연결 저장소 목록(Packages/manifest.json)이 여기 있다
  let linked
  try { linked = linkedRepos(proj) } catch (e) { fail(`Packages/manifest.json 읽기 실패: ${e.message}`) }

  let text
  try { text = readFileSync(0, 'utf8') } catch (e) { fail(`stdin 읽기 실패: ${e.message}`) }
  const inp = parseInput(text, proj, linked)
  if (inp.error) fail(inp.error)
  const { files, subject, body, trailer, repo } = inp
  if (repo) { const p = linkedProblem(repo); if (p) fail(p) }
  const root = repo ? repo.dir : proj   // 커밋할 저장소
  const isDoc = repo ? isLinkedDocPath : isDocPath   // 그 저장소의 문서 규칙(지문에서 빠지는 경로)

  // 병합·cherry-pick·revert·rebase 중에는 지정 파일만 커밋하지 않는다(임시 index 커밋이 부모 둘 커밋을 만들고 그 상태를 끝내 버린다)
  const busy = inProgress(name => name.startsWith('rebase-')
    ? existsSync(resolve(root, git(root, ['rev-parse', '--git-path', name]).out.trim()))
    : git(root, ['rev-parse', '-q', '--verify', name]).code === 0)
  if (busy) fail(`${busy} 진행 중에는 지정 파일만 커밋할 수 없다 — 먼저 끝내거나 중단한다`)

  const st = git(root, ['status', '--porcelain', '-z', '-uall'])
  if (st.code !== 0) fail(`git status 실패: ${tail(st.err)}`)
  const status = parseStatusZ(st.out)
  const { paths, hit } = expandTargets(files, status)

  // 맞는 변경이 없는 요청 경로 중 gitignore된 것은 경고, 나머지는 안내
  for (const f of files) {
    if (hit.has(f)) continue
    const ig = git(root, ['check-ignore', '-q', '--', f])
    console.log(ig.code === 0 ? `경고: gitignore 경로라 뺌: ${f}` : `안내: 바뀐 내용 없음: ${f}`)
  }

  const wt = worktreeSide(status)
  const toAdd = paths.filter(p => wt.has(p))
  const kept = leftUntracked(status)
  for (const p of paths) if (kept.has(p)) console.log(`경고: 추적 안 함으로 남음(index 쪽 지움·이름 바꿈을 그대로 커밋): ${p}`)

  // 관문 영수증(결정 39 ①): 실제 index를 건드리기 전에, 관문과 같은 함수로 커밋할 트리의 지문을 낸다
  let plan, key, left = []
  try {
    plan = paths.length ? buildTree(root, files, { status, isDoc }) : null
    if (plan && repo) {
      // 관문은 연결 저장소 작업 트리 전체를 봤다 — 목록에서 빠진 문서 밖 변경이 있으면 지문이 달라 영수증이 맞지 않는다(안내용)
      const inList = new Set(paths)
      left = allTargets(status).filter(p => !inList.has(p) && !isDoc(p))
    } else if (plan && neededTier(paths) !== 'md') {
      for (const l of linked) { const p = linkedProblem(l); if (p) fail(p) }
      key = linkedKey(linked.map(linkedWork))
    }
  } catch (e) { fail(`커밋할 트리 지문 계산 실패: ${e.message}`) }
  const worktree = !repo && isLinkedWorktree(proj)   // 형제 작업 폴더: 가지 영수증으로 받는다(결정 103 ③)
  const receipts = readAllReceipts(proj)
  const gate = !plan ? { need: 'md' }
    : repo ? linkedVerdict(paths, repo.name, plan.fingerprint, receipts, process.env.SINNOH_GATE_SKIP)
    : gateVerdict(paths, plan.fingerprint, receipts, process.env.SINNOH_GATE_SKIP, key, worktree)
  const short = k => k.replace(/=([0-9a-f]{8})[0-9a-f]*/g, '=$1')
  const gateCmd = repo ? `node Tools/gate.mjs [--files <프로젝트 쪽 파일…>]${left.length ? `(${repo.label}의 목록 밖 변경 ${left.length}개: ${left.slice(0, 3).join(', ')}${left.length > 3 ? ' …' : ''} — 연결 저장소는 문서 밖 변경을 한 커밋에 모두 넣는다)` : ''}`
    : `node Tools/gate.mjs --files ${files.map(shellQuote).join(' ')}`
  const gateLine = gate.need === 'md' ? '문서만 — 영수증 필요 없음'
    : gate.receipt ? `영수증 있음(${gate.receipt.tier}, ${gate.receipt.at})`
    : gate.skip ? `Gate-Skip: ${gate.skip}` : `영수증 없음(필요: ${gate.need})`
  // 본 폴더인데 같은 지문의 가지 영수증만 있다(작업 폴더에서 돈 것을 옮겨 왔거나 --branch로 돌았다) — 무엇이 모자란지 짚는다
  const lower = gate.missing && !repo ? findReceipt(receipts, plan.fingerprint, 'fast') : null
  const lowerNote = lower ? ` — 같은 지문의 ${lower.tier} 영수증(${lower.at})은 있지만 ${gate.need}에 모자란다` : ''

  if (dry) {
    // 한 경로에 항목이 둘일 수 있다(예: D_ + ??) — 모두 보인다
    const byPath = new Map()
    for (const s of status) for (const q of [s.path, ...(s.orig ? [s.orig] : [])]) byPath.set(q, [...(byPath.get(q) ?? []), s.xy])
    for (const p of paths) console.log(`${wt.has(p) ? 'add' : 'index'} ${byPath.get(p).join('+')} ${p}`)
    console.log(`관문: ${gateLine}${plan ? ` · 지문 ${plan.fingerprint.slice(0, 12)}` : ''}${key ? ` · 연결 ${short(key)}` : ''}${repo ? ` · 저장소 ${repo.name}` : ''}`)
    console.log(`DRY-RUN: ${paths.length}개(add ${toAdd.length}, index 그대로 ${paths.length - toAdd.length}), subject="${subject}"`)
    process.exit(0)
  }

  if (!paths.length) return alreadyCommitted(root, subject, '커밋할 변경이 없다')
  if (gate.missing) fail(`관문 영수증 없음(필요: ${gate.need}, ${repo ? `${repo.label} ` : ''}지문 ${plan.fingerprint.slice(0, 12)}${key ? `, 연결 ${short(key)}` : ''})${lowerNote} — ${gateCmd} 뒤 다시`)
  if (gate.skip) console.log(`경고: 관문 건너뜀 — Gate-Skip: ${gate.skip}`)

  if (toAdd.length) {
    const add = gitRetry(root, ['add', '-A', '--pathspec-from-file=-', '--pathspec-file-nul'], toAdd.join('\0') + '\0')
    if (add.code !== 0) fail(`git add 실패: ${tail(add.err || add.out)}`)
  }

  // 임시 index = 실제 index 사본(stat 정보 보존)을 HEAD로 되돌린 뒤 지정 경로만 실제 index 항목으로. 실제 index는 add 말고 건드리지 않는다
  const gd = git(root, ['rev-parse', '--absolute-git-dir'])
  const ix = git(root, ['rev-parse', '--git-path', 'index'])
  const ls = git(root, ['ls-files', '-s', '-z'])
  if (gd.code || ix.code || ls.code) fail(`git 정보 읽기 실패: ${tail(gd.err + ix.err + ls.err)}`)
  const dir = mkdtempSync(join(gd.out.trim(), 'commit-task-'))
  const tmpEnv = { GIT_INDEX_FILE: join(dir, 'index') }
  const msgFile = join(dir, 'msg.txt')
  writeFileSync(msgFile, buildMessage(subject, body, gate.skip ? withGateSkip(trailer, gate.skip) : trailer), 'utf8')
  let c, err
  try {
    copyFileSync(resolve(root, ix.out.trim()), tmpEnv.GIT_INDEX_FILE)
    err = fillIndex(root, tmpEnv, ls.out, paths)
    // 영수증과 맞춘 트리인지 다시 본다(관문 함수와 add 뒤 실제 트리가 다르면 커밋하지 않는다)
    if (!err && gate.receipt) {
      const now = git(root, ['ls-files', '-s', '-z'], undefined, tmpEnv)
      if (now.code !== 0 || fingerprintFromLs(now.out, isDoc) !== plan.fingerprint) err = `지문 불일치 — 영수증 뒤 파일이 바뀌었다. ${gateCmd} 뒤 다시 [add한 ${toAdd.length}개는 staged로 남음]`
    }
    // 메시지 안의 # 줄은 보존(--cleanup=whitespace)
    if (!err) c = gitRetry(root, ['commit', '--cleanup=whitespace', '-F', msgFile], undefined, tmpEnv)
  } catch (e) { err = `임시 index 만들기 실패: ${e.message}` } finally { rmSync(dir, { recursive: true, force: true }) }
  if (err) fail(err)

  if (c.code !== 0) {
    if (isNothingToCommit(c.out + c.err)) return alreadyCommitted(root, subject, '커밋할 변경이 없다')
    fail(`git commit 실패(hook 포함): ${tail(c.err || c.out)} [add한 ${toAdd.length}개는 staged로 남음]`)
  }
  const h = git(root, ['rev-parse', '--short=8', 'HEAD'])
  if (h.code !== 0) fail(`커밋은 됐으나 해시 읽기 실패: ${tail(h.err)}`)
  ok(h.out.trim())
}

function alreadyCommitted(root, subject, why) {
  const lg = git(root, ['log', '-50', '--format=%H%x00%s'])
  const full = lg.code === 0 ? findBySubject(lg.out, subject) : null
  if (!full) fail(`${why}. 최근 50개에 같은 제목("${subject}")도 없다`)
  console.log(`이미 커밋됨: ${full.slice(0, 8)} ("${subject}")`)
  ok(full.slice(0, 8))
}

if (/commit-task.mjs$/.test(process.argv[1] ?? '')) main()
