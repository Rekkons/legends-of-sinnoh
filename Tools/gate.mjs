#!/usr/bin/env node
// 관문 v1(결정 39 ①, 하네스 재검토 보고 §4): 커밋 전 검증을 싼 것부터 고정 순서로 돌리고, 통과하면 영수증(Logs/gate/)을 남긴다.
// commit-task.mjs는 같은 지문(HEAD + 지정 파일, 문서 제외 — Tools/lib/tree.mjs)의 통과 영수증이 없으면 커밋하지 않는다.
//
// 사용: node Tools/gate.mjs [--fast | --branch | --full] [--playmode "<필터>"] [--editorassets "<필터>"] [--files <경로…>] [--since <sha>] [--allow-test-removal "<이유>"] [--json]
//       node Tools/gate.mjs --verify-head [--hook] [--json]   HEAD에 맞는 통과 영수증이 있나(Unity 없음, 약 1초)
//       node Tools/gate.mjs --self-test
// 층(기본은 바뀐 경로로 고른다 — commit-task가 요구하는 층과 같은 함수)
//   md     문서(*.md·Docs/)만 → precheck
//   fast   (--fast) precheck → dotnet test → 조건부 --check(Tools/data·Data/Json·Research/*.csv → data, Tools/assets → assets,
//          그 밖에 카드 굽기 입력(Data/Json의 pokemon·moves·strings.ko·strings.en · Tools/data/lib/cube.mjs) → assets --only cardfaces)
//          + 연결 저장소의 생성기 --self-test → --check(늘 — 키트 토큰 생성기의 자기 시험, USS 두 벌이 원본에서 다시 만든 것과 같은가, LINKED_CHECK)
//          + 그 생성기로 만든 이 프로젝트의 화면 USS --check(늘 — Client/Ui/Styles~/Sinnoh.uss → Resources/SinnohUi 두 벌, LINKED_PROJECT_CHECK)
//   tools  Tools/만(Gen을 만드는 Tools/assets·sources.json 빼고) → fast + 진입점 연기 검사(빈 임시 cwd에서 --help 5초·있으면 --self-test, 저장소 git status 그대로)
//   branch 가지 층(결정 103 ③ — 형제 작업 폴더의 가벼운 확인): fast(+Tools/가 바뀌었으면 연기 검사) + 고른 Unity 시험만.
//          연결된 작업 트리(git worktree add로 만든 형제 폴더 — git-dir ≠ git-common-dir)에서는 「그 밖」의 기본 층이 full 대신 이것이다
//          (commit-task·--verify-head도 거기서는 가지 영수증으로 받는다 — 전체 관문은 main에 합칠 때 본 폴더에서 한 번). 본 폴더에서는 --branch로만.
//          --playmode "<필터>" · --editorassets "<필터>": 그 묶음이 건드린 시험 클래스(이름 조각, 「;」·「,」·공백으로 여럿, all = 그 어셈블리 전부)를
//          Unity 배치로 돈다(Sinnoh.Tests.PlayMode / Sinnoh.Tests.EditorAssets, 필터마다 배치 하나). 둘 다 없으면 Unity를 띄우지 않는다.
//          실제 UGS·바깥 프로세스가 필요한 [Explicit] 시험(NetOnlineTests·WebGuestLiveTests·M7_Online_·M9_Online_, Category Online)은 필터로 짚어도 돌지 않는다
//          (이름을 짚으면 사용법 오류, 조각이 걸려도 정규식·-testCategory "!Online"으로 빼고, 그래도 돌았으면 실패). 맞는 시험이 0개면 실패.
//          고른 시험의 수는 영수증 selected에만 적는다(counts에 넣지 않는다 — 래칫은 dotnet 몫으로 돈다). 새 사본 dotnet·전체 EditMode/PlayMode·플레이어 컴파일은 없다.
//          작업 폴더에서는 본 폴더의 영수증도 읽는다(readAllReceipts — 래칫 기준·키트 HEAD 지문·verify-head의 아래 커밋). 키트 지문을 본 full 영수증이
//          없어도 작업 폴더에서는 full로 올리지 않고 경고만 낸다(키트 커밋은 어디서나 full 영수증).
//   full   그 밖(또는 --full) → fast(+Tools/가 바뀌었으면 연기 검사) + 새 사본 dotnet(git archive) [+ --since면 되돌림 검사(경고)]
//          + Unity EditMode + PlayMode + 플레이어 컴파일(배치, 창 없음). 대조: dotnet 수 = Sinnoh.Tests.EditMode 수 + 키트 Rekkon.UiKit.Core.Tests 수
// 연결된 패키지 저장소(결정 51 — manifest.json의 file: 의존 중 프로젝트 밖 폴더, 지금은 공용 UI 키트 ../uikit — Tools/lib/tree.mjs):
//   영수증에 그 저장소 작업 트리 전체의 지문(receipt.linked[], 문서 제외, 커밋해도 같은 값)을 함께 적는다. 그 지문을 본 full 영수증이 아직 없으면
//   (키트 코드가 바뀌었거나 plain git으로 커밋됐다) --fast가 아닌 한 full로 올린다 — Unity가 키트를 컴파일·시험한다. 키트 문서만 바뀌면 지문이 같아 영향 없음.
//   연결 저장소의 문서는 *.md뿐이다(프로젝트는 *.md·Docs/) — 저장소 루트가 패키지 루트라 Docs/ 같은 폴더도 Unity가 들여오고 컴파일한다.
//   새 사본 dotnet은 키트도 git archive로 프로젝트 사본 옆 같은 상대 자리에 뜬다. 실행 중 키트가 바뀌면 실패 — Unity 단계 중 새로 생긴 키트 .meta만
//   받아들여 지문에 넣는다(패키지 폴더도 .meta가 필요하다 — 키트 저장소에 함께 커밋한다). 저장소가 그 자리에 없거나 자체 git 저장소가 아니면 종료 2.
//   커밋은 키트 → 프로젝트 순서: commit-task 입력에 "repo":"<패키지 이름>"을 주면 그 저장소에 커밋한다(같은 영수증).
// 래칫: 어셈블리별 시험 수가 커밋된 상태보다 줄면 실패(--allow-test-removal "<이유>"면 경고로 두고 영수증에 이유). 기준 = HEAD 트리 지문의
//   통과 영수증 중 그 어셈블리 수가 있는 것. 없으면(HEAD가 Tools만 바꾼 커밋 — tools 영수증에는 dotnet 수뿐) 첫 부모를 따라 내려가 그 수가 있는
//   가장 가까운 커밋 지문의 영수증, 그것도 없으면 head가 HEAD인 가장 이른 영수증. 키트 내용에 따라 수가 달라지는 어셈블리(dotnet·키트 시험)는
//   영수증의 연결 저장소 지문이 그 저장소 HEAD나 조상의 것이어야 하고(가까운 것 먼저 — plain git 키트 커밋 뒤에도 그 아래 커밋의 수와 견준다),
//   프로젝트 어셈블리는 연결 저장소 지문을 가리지 않는다. 더 최근 영수증(버린 --fast 등)보다 준 것은 경고만 — 기준이 아예 없으면 성공 줄 아래에 찍는다.
// --files: commit-task와 같은 목록(파일·폴더, .meta 함께). 목록 밖에 문서 아닌 미커밋 변경이 있으면 종료 2. 없으면 모든 변경.
// 실행 중 변경: 끝에 목록 지문을 다시 내고(목록 안), 시작 때 뜬 status(+ 크기·시각)와 견준다(목록 밖) — 목록 밖이 바뀌었으면 실패.
//   예외는 Unity 단계 안에서 바뀐 에셋류·Unity 설정(재직렬화, 경고)과 Unity가 만든 목록 안 새 .meta(지문에 넣음)뿐이다.
// --since <sha>: 경고 — 목록 밖 경로(--files일 때), 지운 시험 파일, 새 [Ignore], <sha> 영수증보다 준 시험 수, (full) 되돌림 검사.
// --verify-head: 문서만 바뀐 커밋은 건너뛰고(지문이 부모와 같다) 그 아래 첫 코드 커밋의 층으로 영수증을 찾는다. 영수증이 full보다 낮으면
//   (작업 폴더에서는 branch보다 낮으면 — 거기서는 필요한 층의 꼭대기가 branch다. 본 폴더는 가지 영수증만 있는 코드 커밋을 MISSING으로 본다)
//   그 아래에서 더 높은 층이 필요한 커밋도 영수증(또는 Gate-Skip)이 있어야 한다 — plain git 코드 커밋을 문서·Tools 커밋으로 가리지 못하게.
//   연결 저장소도 본다: 맨 위 코드 커밋의 영수증은 연결 저장소 HEAD 지문까지 같아야 하고, 연결 저장소에 문서 밖 미커밋 변경이 없어야 하며,
//   그 HEAD 지문을 본 full 영수증(또는 그 지문을 만든 코드 커밋의 Gate-Skip — 위에 얹힌 문서 커밋의 Gate-Skip 줄은 치지 않는다)이 있어야 한다
//   — plain git 키트 커밋·관문 뒤 고친 키트를 잡는다.
//   (--hook은 앞의 둘을 견주지 않는다 — 작업 중·두 커밋 사이에는 막지 않고, 어느 저장소든 HEAD에 영수증 없는 커밋이 있을 때만 막는다.)
// --verify-head --hook: SubagentStop 훅용(stdin 훅 JSON). 영수증이 없으면 stderr + 종료 2로 막고, 같은 에이전트를 2번 막은 뒤에는
//   보내 주며 Logs/status.txt에 UNVERIFIED를 남긴다(plan §5 설정 예시). 환경 오류(키트 저장소 없음·manifest.json 깨짐)로 판정하지 못했을 때도
//   같은 규칙이다(이유는 stderr로 — 「gate verify-head ENV · …」). 불릴 때마다(막지 않을 때도) Logs/gate/hook-calls.jsonl에
//   한 줄(시각·agent_id·agent_type·ok·종료 코드) — 훅이 어느 에이전트(워크플로 포함)에 걸리는지 보는 기록.
// 자기 우선순위와 자식은 BELOW_NORMAL. 단계마다 시간 제한(넘으면 taskkill /T로 프로세스 트리 종료).
// 출력: 성공은 한 줄, 실패만 자세히(10건까지). 실행마다 Logs/status.txt에 한 줄. --json이면 마지막 줄에 JSON.
// 종료 코드: 0 통과 / 1 검사 실패 / 2 사용법·환경 / 3 Unity 바쁨(같은 프로젝트 에디터·배치·잠금) / 4 영수증 없음(--verify-head)
// v2로 미룸(plan §4 「관문 v1」): --milestone <sha>(사후 대조·무작위 순서 재실행·문서 시험 수 대조), --exec <Method>.
import { spawn, spawnSync } from 'node:child_process'
import { existsSync, readFileSync, writeFileSync, appendFileSync, mkdirSync, rmSync, readdirSync, statSync, openSync, closeSync, mkdtempSync, copyFileSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { cli, usageError } from './cli.mjs'
import { loadSources } from './sources.mjs'
import {
  normPath, allTargets, buildTree, fingerprintOfRev, readStatus, git, gitOk, isDocPath, neededTier, tierRank,
  readReceipts, writeReceipt, findReceipt, GATE_DIR,
  linkedRepos, linkedProblem, linkedWork, linkedHead, linkedSkip, findLinkedReceipt, isLinkedDocPath,
  commitTier, isLinkedWorktree, mainWorktreeOf, readAllReceipts,
} from './lib/tree.mjs'

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const GATE = path.join(ROOT, GATE_DIR)
const BELOW = os.constants.priority.PRIORITY_BELOW_NORMAL
const MIN = 60_000
const TIMEOUT = { precheck: 2 * MIN, dotnet: 20 * MIN, check: 15 * MIN, bake: 40 * MIN, help: 5000, selfTest: 5 * MIN, unity: 35 * MIN, git: 2 * MIN }
const ASM_DOTNET = 'dotnet'
const ASM_EDIT = 'Sinnoh.Tests.EditMode'
// 연결 저장소의 시험 중 dotnet csproj가 같은 소스로 함께 도는 Unity 어셈블리(패키지 이름 → 어셈블리). dotnet 수 = ASM_EDIT + 이것들의 합
const LINKED_DOTNET_ASM = { 'com.rekkon.uikit': 'Rekkon.UiKit.Core.Tests' }
// 연결 저장소의 생성물 검사(패키지 이름 → [이름, 그 저장소 기준 스크립트]). `node <스크립트> --self-test` → `--check` 순서로 부른다:
// 키트 토큰 생성기의 자기 시험(반올림·알파·팔레트 규칙 — 생성기를 잘못 고치고 다시 만들면 --check는 스스로 맞아 통과한다),
// 토큰 USS 두 벌(Kit720·Kit1080)이 원본(Styles~/Kit.uss)에서 다시 만든 것과 같은가
const LINKED_CHECK = { 'com.rekkon.uikit': ['uss', 'Tools~/build-uss.mjs'] }
// 연결 저장소의 생성기로 만드는 이 프로젝트의 생성물(패키지 이름 → [이름, 그 저장소 기준 스크립트, 원본, 출력 폴더] — 뒤 둘은 프로젝트 기준).
// `node <스크립트> --check <원본> <출력 폴더>`를 늘 부른다: 화면 USS 두 벌(Sinnoh720·Sinnoh1080)이 원본(Client/Ui/Styles~/Sinnoh.uss)에서 다시 만든 것과 같은가
const LINKED_PROJECT_CHECK = { 'com.rekkon.uikit': ['ui-uss', 'Tools~/build-uss.mjs', 'Assets/Sinnoh/Client/Ui/Styles~/Sinnoh.uss', 'Assets/Sinnoh/Client/Ui/Resources/SinnohUi'] }
const MAX_FAIL = 10
const TEST_ID = String.raw`(?:A\d+(?:_\d+)?|G\d+|R\d+|B\d+|S\d+_\d+(?:_\d+)?|M\d+)_[\p{L}\p{N}_]*`   // plan §2(precheck와 같음)

class Exit extends Error { constructor(code, msg) { super(msg); this.exitCode = code } }

// ---------- 순수 함수 ----------

const ENT = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" }
const unent = s => String(s).replace(/&(#x[0-9a-f]+|#\d+|\w+);/gi, (m, e) =>
  e[0] === '#' ? String.fromCodePoint(e[1].toLowerCase() === 'x' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10)) : (ENT[e] ?? m))

/** XML 태그 안 속성 → Map(순서에 기대지 않는다). */
export function xmlAttrs(s) {
  const m = new Map()
  for (const a of String(s).matchAll(/([\w:.-]+)\s*=\s*(?:"([^"]*)"|'([^']*)')/g)) m.set(a[1], unent(a[2] ?? a[3]))
  return m
}
const tagRe = name => new RegExp(`<${name}(?=[\\s/>])((?:[^>"']|"[^"]*"|'[^']*')*?)(/?)>`, 'g')
function elemText(body, name) {
  const m = String(body).match(new RegExp(`<${name}>(?:\\s*<!\\[CDATA\\[([\\s\\S]*?)\\]\\]>\\s*|([\\s\\S]*?))</${name}>`))
  return m ? (m[1] ?? unent(m[2] ?? '')) : ''
}
const num = v => Number(v ?? 0) || 0

/** NUnit 3 결과 XML(Unity·dotnet 같은 형식) 하나만 읽는 곳. { run, assemblies:[{name,total,passed,failed,skipped,inconclusive}], cases:[…] } */
export function parseNUnit(xml) {
  const runM = tagRe('test-run').exec(xml)
  const ra = runM ? xmlAttrs(runM[1]) : new Map()
  const counts = a => ({ total: num(a.get('total')), passed: num(a.get('passed')), failed: num(a.get('failed')), skipped: num(a.get('skipped')), inconclusive: num(a.get('inconclusive')) })
  const assemblies = []
  for (const m of xml.matchAll(tagRe('test-suite'))) {
    const a = xmlAttrs(m[1])
    if (a.get('type') === 'Assembly') assemblies.push({ name: String(a.get('name') ?? '').replace(/\.dll$/i, ''), ...counts(a) })
  }
  const cases = []
  for (const m of xml.matchAll(tagRe('test-case'))) {
    const a = xmlAttrs(m[1])
    let body = ''
    if (!m[2]) { const s = m.index + m[0].length, e = xml.indexOf('</test-case>', s); body = e < 0 ? '' : xml.slice(s, e) }
    cases.push({
      name: a.get('name') ?? '', fullname: a.get('fullname') ?? '', methodname: a.get('methodname') ?? '', classname: a.get('classname') ?? '',
      result: a.get('result') ?? '', label: a.get('label') ?? '', message: elemText(body, 'message'), stack: elemText(body, 'stack-trace'),
    })
  }
  return { run: { result: ra.get('result') ?? '', ...counts(ra) }, assemblies, cases }
}

const oneLine = s => String(s ?? '').trim().replace(/\s*\r?\n\s*/g, ' / ')

/** 실패한 시험 → 「클래스.이름: 메시지 (파일:줄)」. */
export function caseFailures(nu) {
  return nu.cases.filter(c => c.result === 'Failed').map(c => {
    const loc = c.stack.match(/([^\s\\/:]+\.cs):(?:line )?(\d+)/)
    const cls = c.classname.split('.').pop()
    return `${cls ? cls + '.' : ''}${c.name}: ${oneLine(c.message).slice(0, 240) || c.label || '실패'}${loc ? ` (${loc[1]}:${loc[2]})` : ''}`
  })
}

/** 빌드 로그의 C# 컴파일 오류 → 「경로:줄 CS####: 내용」(같은 것 한 번, 앞 5개). */
export function compileErrors(text, root = ROOT) {
  const out = []
  const r = String(root).replace(/\\/g, '/').toLowerCase() + '/'
  for (const m of String(text).matchAll(/([^\s"]+?\.cs)\((\d+),\d+\): error (CS\d+): ([^\r\n]*?)(?:\s+\[[^\]]*\])?\r?$/gm)) {
    let f = m[1].replace(/\\/g, '/')
    if (f.toLowerCase().startsWith(r)) f = f.slice(r.length)
    const s = `${f}:${m[2]} ${m[3]}: ${m[4].trim()}`
    if (!out.includes(s)) out.push(s)
    if (out.length >= 5) break
  }
  return out
}

/** Unity 명령줄의 -projectPath 값(따옴표·대소문자 무관). */
export function projectPathOf(cmdline) {
  const m = String(cmdline ?? '').match(/"?-projectpath"?\s+(?:"([^"]+)"|(\S+))/i)
  return m ? (m[1] ?? m[2]) : null
}
export const normDir = p => path.resolve(String(p)).replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase()

/** 연결 저장소(키트) 내용에 따라 시험 수가 달라지는 어셈블리: dotnet(키트 Core 시험을 함께 돈다)과 키트 시험 어셈블리. 나머지는 프로젝트 트리만으로 정해진다. */
const linkedAsm = asm => asm === ASM_DOTNET || Object.values(LINKED_DOTNET_ASM).includes(asm)

/** 래칫: 어셈블리마다 커밋된 상태의 수와 견준다. 기준 영수증(그 어셈블리 수가 있는 통과 영수증)을 찾는 순서:
 *  ① 지문이 fpHead(HEAD 트리)인 것 ② older(첫 부모를 따라 내려간 조상 커밋 지문, 가까운 것부터)의 것 — HEAD가 Tools만 바꾼 커밋이면
 *  tools 영수증에 Unity 어셈블리 수가 없다 ③ head가 지금 HEAD인 가장 이른 것.
 *  linked({ 패키지 이름: [연결 저장소 HEAD 지문, 조상 지문…] })를 주면 linkedAsm 어셈블리의 ①②는 영수증의 연결 지문이 모두 그 줄 안에 있어야 하고
 *  HEAD에 가까운 것을 먼저 고른다(버린 키트 실험 실행을 기준으로 삼지 않고, plain git 키트 커밋 뒤에는 그 아래 커밋 때의 수와 견준다).
 *  그 밖의 어셈블리는 연결 지문을 보지 않는다(키트가 무엇이든 프로젝트 트리가 같으면 수가 같다). 같은 조건이면 가장 최근 영수증.
 *  기준보다 줄면 drops(실패). 기준보다는 안 줄었는데 더 최근 영수증(버린 --fast 등)보다 줄면 soft(경고), 기준이 아예 없으면 soft에 noBase.
 *  { drops:[…], skipUps:[…], soft:[…] } */
export function ratchet(counts, receipts, { fpHead = null, older = [], head = null, linked } = {}) {
  const drops = [], skipUps = [], soft = []
  const fps = [fpHead, ...older].filter(Boolean)
  // 영수증이 본 연결 저장소 지문이 HEAD에서 몇 커밋 아래 것인가(합). HEAD나 조상의 것이 아니면 -1
  const depth = r => {
    let n = 0
    for (const [name, chain] of Object.entries(linked)) {
      const i = chain.indexOf((r.linked ?? []).find(l => l.name === name)?.fingerprint)
      if (i < 0) return -1
      n += i
    }
    return n
  }
  for (const [asm, c] of Object.entries(counts)) {
    const has = r => r?.ok && r.counts?.[asm]
    const keyed = linked && linkedAsm(asm)
    let base = null, latest = null
    for (let i = receipts.length - 1; i >= 0 && !latest; i--) if (has(receipts[i])) latest = receipts[i]
    for (const fp of fps) {
      let best = -1
      for (let i = receipts.length - 1; i >= 0 && best !== 0; i--) {
        const r = receipts[i]
        if (!has(r) || r.fingerprint !== fp) continue
        const d = keyed ? depth(r) : 0
        if (d >= 0 && (!base || d < best)) { base = r; best = d }
      }
      if (base) break
    }
    if (!base && head) base = receipts.find(r => has(r) && r.head === head) ?? null
    const row = (r, list, more) => list.push({ asm, from: r.counts[asm].total, to: c.total, fp: r.fingerprint, at: r.at, ...more })
    if (base && c.total < base.counts[asm].total) row(base, drops)
    else if (latest && latest !== base && c.total < latest.counts[asm].total) row(latest, soft, base ? {} : { noBase: true })
    const ref = base ?? latest
    if (ref && c.skipped > ref.counts[asm].skipped) skipUps.push({ asm, from: ref.counts[asm].skipped, to: c.skipped })
  }
  return { drops, skipUps, soft }
}

/** 첫 부모를 따라 내려간 조상 커밋들의 지문(가까운 것부터, rev 자신과 같은 지문·겹치는 지문은 뺀다 — 문서만 바뀐 커밋). 래칫의 기준 찾기용:
 *  enough(지문)이 참이면 거기서 멈춘다(그 지문까지 넣는다). rev 자신의 지문(fp0)이 이미 enough면 내려가지 않는다. */
export function olderFingerprints(root, rev, fp0, enough, isDoc = isDocPath, maxDepth = 30) {
  const out = []
  if (!rev || enough(fp0)) return out
  const revs = git(root, ['rev-list', '--first-parent', `--max-count=${maxDepth + 1}`, rev]).out.split('\n').filter(Boolean).slice(1)
  for (const c of revs) {
    const fp = fingerprintOfRev(root, c, isDoc)
    if (fp === fp0 || out.includes(fp)) continue
    out.push(fp)
    if (enough(fp)) break
  }
  return out
}

/** 대조(full): dotnet 수 = Unity에서 같은 소스를 도는 어셈블리들의 합(ASM_EDIT + 연결 저장소의 LINKED_DOTNET_ASM). 어긋나면 이유, 맞으면 null.
 *  Unity 결과에 그 어셈블리가 없으면(manifest testables에서 빠짐 등) 어긋난 것으로 본다. */
export function mirrorCompare(counts, linkedNames) {
  const asms = [ASM_EDIT, ...linkedNames.map(n => LINKED_DOTNET_ASM[n]).filter(Boolean)]
  const d = counts[ASM_DOTNET]?.total
  const us = asms.map(a => counts[a]?.total)
  if (us.every(n => n != null) && d === us.reduce((a, b) => a + b, 0)) return null
  return `dotnet ${d} ≠ Unity ${asms.map((a, i) => `${a} ${us[i] ?? '없음'}`).join(' + ')}`
}

/** git diff -U0 출력 → 새로 넣은 [Ignore]·Ignore= 줄 [{file, text}]과 더한 시험 이름(Set). */
export function scanTestDiff(diff) {
  let file = ''
  const ignores = [], names = new Set()
  const nameRe = new RegExp(`(${TEST_ID})\\s*\\(`, 'gu')
  for (const line of String(diff).split('\n')) {
    if (line.startsWith('+++ ')) { file = line.slice(4).replace(/^b\//, '').trim(); continue }
    if (!line.startsWith('+') || line.startsWith('+++')) continue
    const t = line.slice(1)
    if (/^\s*\/\//.test(t)) continue
    if (/\[\s*(?:[\w.]+(?:\([^)]*\))?\s*,\s*)*Ignore\b|\bIgnore\s*=\s*"/.test(t)) ignores.push({ file, text: t.trim().slice(0, 160) })
    for (const m of t.matchAll(nameRe)) names.add(m[1])
  }
  return { ignores, names }
}

/** 가지 층이 고른 Unity 시험(--playmode·--editorassets): 종류 → [어셈블리, -testPlatform]. */
export const SELECT = { playmode: ['Sinnoh.Tests.PlayMode', 'PlayMode'], editorassets: ['Sinnoh.Tests.EditorAssets', 'EditMode'] }
/** 관문에서 돌리지 않는 [Explicit] 시험(실제 UGS·바깥 프로세스·인터넷, Category Online): 필터로 짚어도 뺀다. */
export const ONLINE_NAMES = ['NetOnlineTests', 'WebGuestLiveTests', 'M7_Online_', 'M9_Online_']
const ONLINE_RE = new RegExp(ONLINE_NAMES.join('|'), 'i')

/** 필터 글 → Unity -testFilter 정규식(전체 이름에 대고 본다 — UTF는 -testFilter를 「;」로 나눠 정규식 FullNameFilter로 쓴다).
 *  이름 조각(글자·숫자·_·.)을 「;」·「,」·공백으로 여럿, all이면 전부(regex null). 온라인 시험은 부정 앞보기로 늘 뺀다. 잘못이면 { error }. */
export function unityFilter(text, kind = 'playmode') {
  const names = [...new Set(String(text ?? '').split(/[;,\s]+/).filter(Boolean))]
  if (!names.length) return { error: '필터가 비었다' }
  const bad = names.filter(n => !/^[\p{L}\p{N}_.]+$/u.test(n))
  if (bad.length) return { error: `필터에는 시험 클래스·이름 조각(글자·숫자·_·.)만: ${bad.join(' ')}` }
  const online = names.filter(n => ONLINE_RE.test(n))
  if (online.length) return { error: `실제 UGS·바깥 프로세스가 필요한 [Explicit] 시험은 관문에서 돌리지 않는다: ${online.join(' ')}` }
  const all = names.some(n => n.toLowerCase() === 'all')
  const esc = n => n.replace(/\./g, '\\.')
  // 이름 조각은 그 어셈블리의 이름 공간(= 어셈블리 이름) 뒤에서만 찾는다 — 위 묶음(이름 공간·dll 경로)이 조각에 걸려 전부 도는 일이 없게.
  // all은 -testFilter 없이 어셈블리만 짚는다(정규식에 걸린 [Explicit] 시험은 「짚은」 것이 돼 돈다 — 필터가 없으면 돌지 않는다)
  return { names: all ? ['all'] : names, regex: all ? null : `^(?!.*(?:${ONLINE_NAMES.join('|')}))${esc(SELECT[kind][0])}\\..*(?:${names.map(esc).join('|')})` }
}

const fmtDur = ms => { const s = Math.round(ms / 1000); return s >= 60 ? `${Math.floor(s / 60)}m${String(s % 60).padStart(2, '0')}s` : `${s}s` }

// ---------- 프로세스 ----------

function killTree(pid) { if (pid) spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' }) }

/** 비동기 실행: BELOW_NORMAL, 시간 제한이면 프로세스 트리 종료. { code, out, timedOut, error } */
function run(cmd, args, { cwd = ROOT, timeout, env, quiet } = {}) {
  return new Promise(res => {
    let out = '', timedOut = false, done = false, p, timer, grace
    const finish = r => { if (!done) { done = true; clearTimeout(timer); clearTimeout(grace); res(r) } }
    try {
      p = spawn(cmd, args, { cwd, env: { ...process.env, ...env }, windowsHide: true, stdio: quiet ? 'ignore' : ['ignore', 'pipe', 'pipe'] })
    } catch (e) { return res({ code: null, out: '', error: e }) }
    try { os.setPriority(p.pid, BELOW) } catch { /* 이미 끝났거나 물려받음 */ }
    const add = d => { out += d; if (out.length > 4e6) out = out.slice(-2e6) }
    p.stdout?.on('data', add); p.stderr?.on('data', add)
    if (timeout) timer = setTimeout(() => { timedOut = true; killTree(p.pid) }, timeout)
    p.on('error', e => finish({ code: null, out, error: e }))
    // 손자 프로세스가 파이프를 물고 있어도 끝나게: exit 뒤 3초 안에 close가 안 오면 그대로 끝낸다
    p.on('exit', code => { grace = setTimeout(() => finish({ code, out, timedOut }), 3000) })
    p.on('close', code => finish({ code, out, timedOut }))
  })
}

const tailLines = (s, n = 8) => String(s).split(/\r?\n/).map(l => l.trimEnd()).filter(Boolean).slice(-n)

// ---------- Unity 잠금 ----------

function unityProcesses() {
  const ps = spawnSync('powershell', ['-NoProfile', '-NonInteractive', '-Command',
    "Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | Select-Object ProcessId,CommandLine | ConvertTo-Json -Compress"],
  { encoding: 'utf8', windowsHide: true, timeout: 30000 })
  if (ps.status !== 0) return { error: oneLine(ps.stderr || ps.error) }
  const txt = String(ps.stdout).trim()
  if (!txt) return { pids: [] }
  let arr; try { arr = JSON.parse(txt) } catch { return { error: 'Unity 프로세스 목록 해석 실패' } }
  if (!Array.isArray(arr)) arr = [arr]
  const me = normDir(ROOT)
  return { pids: arr.filter(p => { const pp = projectPathOf(p.CommandLine); return pp && normDir(pp) === me }).map(p => p.ProcessId) }
}

function lockfileBusy() {
  const f = path.join(ROOT, 'Temp/UnityLockfile')
  if (!existsSync(f)) return false
  try { closeSync(openSync(f, 'r+')); return false } catch (e) { return ['EBUSY', 'EPERM', 'EACCES'].includes(e.code) }
}

function alive(pid) { try { process.kill(pid, 0); return true } catch (e) { return e.code === 'EPERM' } }
const OWN_LOCK = path.join(GATE, 'unity.lock')

/** 바쁘면 이유, 아니면 null(경고는 warn에). */
function unityBusy(warn) {
  const pr = unityProcesses()
  if (pr.error) warn(`Unity 프로세스 확인 실패(잠금 파일만 봄): ${pr.error}`)
  else if (pr.pids.length) return `같은 프로젝트 Unity 실행 중(PID ${pr.pids.join(',')}) — 에디터를 닫거나 끝나기를 기다린다`
  if (lockfileBusy()) return 'Temp/UnityLockfile 잠김(이 프로젝트 에디터가 열려 있다)'
  if (existsSync(OWN_LOCK)) {
    try { const j = JSON.parse(readFileSync(OWN_LOCK, 'utf8')); if (j.pid !== process.pid && alive(j.pid)) return `다른 관문이 Unity 사용 중(PID ${j.pid}, ${j.start})` } catch { /* 깨진 잠금은 무시 */ }
  }
  return null
}

/** 시간 초과로 죽인 뒤: 같은 프로젝트 Unity가 없을 때만 Unity 잠금 파일을 지운다. */
function clearStaleUnityLock() {
  const pr = unityProcesses()
  if (!pr.error && !pr.pids.length) rmSync(path.join(ROOT, 'Temp/UnityLockfile'), { force: true })
}

// ---------- 단계 ----------

function editorDir() {
  try { return loadSources(ROOT).unityEditor } catch { return 'D:/Unity/Editors/6000.6.3f1/Editor' }
}
const DOTNET_ENV = { DOTNET_CLI_UI_LANGUAGE: 'en', VSLANG: '1033', DOTNET_NOLOGO: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1', MSBUILDDISABLENODEREUSE: '1' }

async function precheckStep() {
  const r = await run(process.execPath, [path.join(ROOT, 'Tools/precheck.mjs')], { timeout: TIMEOUT.precheck })
  if (r.error) throw new Exit(2, `precheck 실행 실패: ${r.error.message}`)
  if (r.code === 0 && !r.timedOut) return { ok: true, label: 'precheck' }
  const errs = String(r.out).split(/\r?\n/).filter(l => /\[[a-z]+\]/.test(l) && !/\] 경고 /.test(l))
  return { ok: false, label: 'precheck', failures: errs.length ? errs : tailLines(r.out), log: 'node Tools/precheck.mjs' }
}

/** dotnet test → NUnit XML. { ok, nu, failures, total, skipped } */
async function dotnetRun(cwd, outDir) {
  rmSync(outDir, { recursive: true, force: true })
  const exe = path.join(editorDir(), 'Data/DotNetSdk/dotnet.exe')
  if (!existsSync(exe)) throw new Exit(2, `dotnet 없음: ${exe}`)
  const r = await run(exe, ['test', 'Tools/dotnet-test/SinnohCore.Tests.csproj', '--nologo', '--', `NUnit.TestOutputXml=${outDir}`],
    { cwd, timeout: TIMEOUT.dotnet, env: DOTNET_ENV })
  if (r.error) throw new Exit(2, `dotnet 실행 실패: ${r.error.message}`)
  const xml = path.join(outDir, 'SinnohCore.Tests.xml')
  if (r.timedOut) return { ok: false, failures: [`시간 초과(${TIMEOUT.dotnet / MIN}분) — 프로세스 트리 종료`] }
  if (!existsSync(xml)) { const ce = compileErrors(r.out, cwd); return { ok: false, compile: true, failures: ce.length ? ce : tailLines(r.out) } }
  const nu = parseNUnit(readFileSync(xml, 'utf8'))
  const failures = caseFailures(nu)
  const ok = r.code === 0 && nu.run.failed === 0 && nu.run.total > 0
  if (!ok && !failures.length) failures.push(nu.run.total ? `dotnet 종료 코드 ${r.code}` : '돈 시험이 0개다')
  return { ok, nu, failures, total: nu.run.total, skipped: nu.run.skipped + nu.run.inconclusive }
}

async function dotnetStep(ctx) {
  const r = await dotnetRun(ROOT, path.join(GATE, 'dotnet'))
  if (r.nu) ctx.counts[ASM_DOTNET] = { total: r.nu.run.total, passed: r.nu.run.passed, failed: r.nu.run.failed, skipped: r.nu.run.skipped + r.nu.run.inconclusive }
  return { ok: r.ok, label: `dotnet${r.nu ? ' ' + r.total : ''}`, failures: r.failures, log: `${GATE_DIR}/dotnet/SinnohCore.Tests.xml` }
}

/** 돌릴 검사 목록 [[이름, 스크립트 경로, 차례로 부를 인자들]]: 프로젝트 것(`--check`)은 바뀐 경로로 고르고, 연결 저장소의 생성물 검사(LINKED_CHECK —
 *  생성기 `--self-test` 뒤 `--check`)는 늘 넣는다(그 저장소는 작업 트리 전체가 대상이고 검사가 싸다 — 생성기는 연기 검사(프로젝트 Tools/만 훑는다)에
 *  들지 않아 자기 시험을 여기서 부른다). 프로젝트 스크립트는 루트 기준, 연결 저장소 스크립트는 절대 경로. */
// 카드 굽기(Tools/assets/lib/card-faces.mjs loadData — 결정 206)가 읽는 프로젝트 파일: 이것만 바뀌어도 cardfaces 묶음 --check(약 12분)를 돈다
// (Tools/assets/가 바뀌어 전체 --check가 돌면 그 안에 들어 있어 따로 넣지 않는다)
const CARDFACES_IN = ['Assets/Sinnoh/Data/Json/pokemon.json', 'Assets/Sinnoh/Data/Json/moves.json', 'Assets/Sinnoh/Data/Json/strings.ko.json',
  'Assets/Sinnoh/Data/Json/strings.en.json', 'Tools/data/lib/cube.mjs']
export function checksFor(scope, links) {
  const which = []
  if (scope.some(p => p.startsWith('Tools/data/') || p.startsWith('Assets/Sinnoh/Data/Json/') || /^Research\/[^/]+\.csv$/.test(p))) which.push(['data', 'Tools/data/build.mjs', ['--check']])
  if (scope.some(p => p.startsWith('Tools/assets/'))) which.push(['assets', 'Tools/assets/build.mjs', ['--check']])
  else if (scope.some(p => CARDFACES_IN.includes(p))) which.push(['cardfaces', 'Tools/assets/build.mjs', [['--check', '--only', 'cardfaces']]])
  for (const l of links) {
    const c = LINKED_CHECK[l.name]
    if (c) which.push([`${l.label}-${c[0]}`, `${l.dir}/${c[1]}`, ['--self-test', '--check']])
    const pc = LINKED_PROJECT_CHECK[l.name]
    if (pc) which.push([pc[0], `${l.dir}/${pc[1]}`, [['--check', pc[2], pc[3]]]])
  }
  return which
}

async function checkStep(ctx) {
  const which = checksFor(ctx.scope, ctx.links)
  if (!which.length) return null
  for (const [name, script, args] of which) for (const arg of args) {
    // 카드 굽기를 다시 해 보는 검사(assets 전체 · cardfaces)는 혼자 약 12분, 다른 작업과 겹치면 15분을 넘는다(11-23 관문 시간 초과) — 굽기 몫 한도
    const r = await run(process.execPath, [path.resolve(ROOT, script), ...[].concat(arg)], { timeout: name === 'assets' || name === 'cardfaces' ? TIMEOUT.bake : TIMEOUT.check })
    if (r.error) throw new Exit(2, `${script} 실행 실패: ${r.error.message}`)
    if (r.code !== 0 || r.timedOut) return { ok: false, label: `check:${name}`, failures: r.timedOut ? ['시간 초과'] : tailLines(r.out), log: `node ${script} ${[].concat(arg).join(' ')}` }
  }
  return { ok: true, label: which.map(w => w[0]).join('+') }
}

/** 진입점: Tools/와 그 바로 아래 폴더의 .mjs 중 명령줄을 읽는 것(lib/·cli.mjs 같은 모듈 제외). */
export function entryPoints(root = ROOT) {
  const out = []
  const look = (dir, deeper) => {
    for (const e of readdirSync(path.join(root, dir), { withFileTypes: true })) {
      const rel = `${dir}/${e.name}`
      if (e.isDirectory()) { if (deeper && e.name !== 'lib') look(rel, false); continue }
      if (!e.name.endsWith('.mjs') || e.name === 'cli.mjs') continue
      const src = readFileSync(path.join(root, rel), 'utf8')
      if (/process\.argv|\bcli\(/.test(src)) out.push({ rel, selfTest: src.includes("'--self-test'") })
    }
  }
  look('Tools', true)
  return out.sort((a, b) => a.rel.localeCompare(b.rel))
}

async function smokeStep() {
  const before = gitOk(ROOT, ['status', '--porcelain', '-z', '-uall'])
  const cwd = mkdtempSync(path.join(os.tmpdir(), 'sinnoh-gate-smoke-'))
  const failures = []
  let n = 0
  try {
    for (const ep of entryPoints()) {
      for (const [arg, timeout] of [['--help', TIMEOUT.help], ...(ep.selfTest ? [['--self-test', TIMEOUT.selfTest]] : [])]) {
        n++
        const r = await run(process.execPath, [path.join(ROOT, ep.rel), arg], { cwd, timeout })
        if (r.error || r.code !== 0 || r.timedOut) failures.push(`${ep.rel} ${arg}: ${r.timedOut ? `시간 초과 ${timeout / 1000}초` : `종료 ${r.code}`} — ${oneLine(tailLines(r.out, 2).join(' / ')).slice(0, 200)}`)
      }
    }
  } finally { rmSync(cwd, { recursive: true, force: true }) }
  if (gitOk(ROOT, ['status', '--porcelain', '-z', '-uall']) !== before) failures.push('연기 검사가 저장소 git status를 바꿨다')
  return { ok: !failures.length, label: `smoke ${n}`, failures }
}

/** 새 사본을 뜰 자리: 프로젝트는 base/_project, 연결 저장소는 프로젝트에서 본 상대 자리 그대로(../uikit → base/uikit). base 밖이면 null. */
export function freshPlace(base, root, dir) {
  const project = path.join(base, '_project')
  const to = path.resolve(project, path.relative(root, dir))
  return to === project ? project : to.startsWith(base + path.sep) && !to.startsWith(project + path.sep) ? to : null
}

/** 새 사본(git archive) dotnet + (--since) 되돌림 검사(경고). 연결 저장소도 지금 내용(ctx.links[].tree)을 같은 방식으로 옆에 뜬다. */
async function freshStep(ctx) {
  const base = mkdtempSync(path.join(os.tmpdir(), 'sinnoh-gate-fresh-'))
  try {
    const dir = freshPlace(base, ROOT, ROOT)
    const tarExe = existsSync('C:/Windows/System32/tar.exe') ? 'C:/Windows/System32/tar.exe' : 'tar'
    const copy = (root, tree, to) => {
      const tar = path.join(base, '_copy.tar')
      const a = git(root, ['archive', '--format=tar', '-o', tar, tree])
      if (a.code !== 0) throw new Exit(2, `git archive 실패(${root}): ${oneLine(a.err)}`)
      mkdirSync(to, { recursive: true })
      const x = spawnSync(tarExe, ['-xf', tar, '-C', to], { windowsHide: true, encoding: 'utf8', timeout: TIMEOUT.git })
      rmSync(tar, { force: true })
      if (x.status !== 0) throw new Exit(2, `사본 풀기 실패(${root}): ${oneLine(x.stderr || x.error)}`)
    }
    copy(ROOT, ctx.tree, dir)
    for (const l of ctx.links) {
      const to = freshPlace(base, ROOT, l.dir)
      if (!to) throw new Exit(2, `연결 저장소 ${l.label}의 새 사본을 뜰 수 없다 — 프로젝트 옆 폴더(../<이름>)여야 한다: ${l.dir}`)
      copy(l.dir, l.tree, to)
    }
    const r = await dotnetRun(dir, path.join(GATE, 'fresh-dotnet'))
    if (r.nu) {
      ctx.fresh = { total: r.total, skipped: r.skipped }
      const d = ctx.counts[ASM_DOTNET]
      if (d && d.total !== r.total) ctx.warn(`새 사본 dotnet 수 ${r.total} ≠ 작업 트리 ${d.total}`)
    }
    if (r.ok && ctx.since) await revertCheck(ctx, dir)
    return { ok: r.ok, label: `fresh${r.nu ? ` ${r.total}(skip ${r.skipped})` : ''}`, failures: r.failures, log: `${GATE_DIR}/fresh-dotnet/SinnohCore.Tests.xml` }
  } finally { rmSync(base, { recursive: true, force: true, maxRetries: 3 }) }
}

/** 시험 밖 바뀐 파일을 --since 판으로 되돌리고 dotnet을 다시 돈다. 그래도 통과하는 새 시험은 「변경에 기대지 않는 시험」 경고. */
async function revertCheck(ctx, dir) {
  const changed = ctx.sinceDiff.filter(d => !d.path.startsWith('Assets/Sinnoh/Tests/') && !isDocPath(d.path))
  const names = scanTestDiff(git(ROOT, ['diff', '-U0', '--no-renames', ctx.since, ctx.tree, '--', 'Assets/Sinnoh/Tests/EditMode']).out).names
  if (!changed.length || !names.size) { ctx.revert = changed.length ? '해당 없음(새 시험 없음)' : '해당 없음(시험 밖 변경 없음)'; return }
  for (const d of changed) {
    const f = path.join(dir, d.path)
    if (d.status === 'A') { rmSync(f, { force: true }); continue }
    const b = spawnSync('git', ['show', `${ctx.since}:${d.path}`], { cwd: ROOT, maxBuffer: 256 * 1024 * 1024, windowsHide: true })
    if (b.status === 0) { mkdirSync(path.dirname(f), { recursive: true }); writeFileSync(f, b.stdout) }
  }
  const r = await dotnetRun(dir, path.join(GATE, 'revert-dotnet'))
  if (!r.nu) { ctx.revert = '해당 없음(되돌리면 컴파일 안 됨)'; return }
  const still = r.nu.cases.filter(c => c.result === 'Passed' && names.has(c.methodname || c.name.replace(/\(.*$/, '')))
  ctx.revert = `새 시험 ${names.size}개 중 되돌려도 통과 ${still.length}`
  for (const c of still) ctx.warn(`변경 없이도 통과하는 새 시험(되돌림 검사): ${c.classname.split('.').pop()}.${c.name}`)
}

async function unityRun(name, args, { xml } = {}) {
  const exe = path.join(editorDir(), 'Unity.exe')
  if (!existsSync(exe)) throw new Exit(2, `Unity 없음: ${exe}`)
  const log = path.join(GATE, `${name}.log`)
  rmSync(log, { force: true })
  if (xml) rmSync(xml, { force: true })
  const r = await run(exe, ['-batchmode', '-projectPath', ROOT, ...args, '-logFile', log], { timeout: TIMEOUT.unity, quiet: true })
  if (r.error) throw new Exit(2, `Unity 실행 실패: ${r.error.message}`)
  const text = existsSync(log) ? readFileSync(log, 'utf8') : ''
  if (r.timedOut) { clearStaleUnityLock(); return { ok: false, text, failures: [`시간 초과(${TIMEOUT.unity / MIN}분) — 프로세스 트리 종료`] } }
  if (/another Unity instance is running with this project open|Multiple Unity instances cannot open the same project/i.test(text)) throw new Exit(3, '같은 프로젝트를 다른 Unity가 열고 있다')
  return { ok: true, code: r.code, text }
}

/** sel = { kind, names, regex }를 주면(가지 층) 그 어셈블리에서 필터에 맞는 시험만 돈다: 수는 ctx.selected에 적고 ctx.counts에는 넣지 않는다(래칫·대조 밖). */
async function unityTestStep(ctx, platform, sel) {
  const name = sel ? `select-${sel.kind}` : platform.toLowerCase()
  const xml = path.join(GATE, `${name}.xml`)
  const pick = sel ? ['-assemblyNames', SELECT[sel.kind][0], ...(sel.regex ? ['-testFilter', sel.regex] : []), '-testCategory', '!Online'] : []
  const r = await unityRun(name, ['-runTests', '-testPlatform', platform, ...pick, '-testResults', xml], { xml })
  const log = `${GATE_DIR}/${name}.log`
  if (!r.ok) return { ok: false, label: name, failures: r.failures, log }
  if (!existsSync(xml)) {
    const ce = compileErrors(r.text)
    return { ok: false, label: name, failures: ce.length ? ce : [`결과 XML 없음(Unity 종료 코드 ${r.code})`, ...tailLines(r.text, 5)], log }
  }
  const nu = parseNUnit(readFileSync(xml, 'utf8'))
  const failures = caseFailures(nu)
  if (sel) {
    const ran = nu.cases.filter(c => c.result !== 'Skipped')
    ctx.selected[sel.kind] = { filter: sel.names.join(';'), ran: ran.length, passed: nu.run.passed, failed: nu.run.failed, skipped: nu.cases.length - ran.length }
    for (const c of ran) if (ONLINE_RE.test(c.fullname || `${c.classname}.${c.name}`)) failures.push(`관문 밖 [Explicit] 온라인 시험이 돌았다: ${c.classname.split('.').pop()}.${c.name}`)
    const okSel = nu.run.failed === 0 && ran.length > 0 && !failures.length
    if (!okSel && !failures.length) failures.push(ran.length ? `실패 ${nu.run.failed}(시험 밖 오류 — 로그 확인)` : `필터에 맞는 시험이 0개다(--${sel.kind} "${sel.names.join(';')}" — ${SELECT[sel.kind][0]}의 클래스·시험 이름 조각을 준다)`)
    return { ok: okSel, label: `${sel.kind} ${ran.length}`, failures, log: `${GATE_DIR}/${name}.xml` }
  }
  for (const a of nu.assemblies) ctx.counts[a.name] = { total: a.total, passed: a.passed, failed: a.failed, skipped: a.skipped + a.inconclusive }
  const ok = nu.run.failed === 0 && nu.run.total > 0
  if (!ok && !failures.length) failures.push(nu.run.total ? `실패 ${nu.run.failed}(시험 밖 오류 — 로그 확인)` : '돈 시험이 0개다')
  return { ok, label: `${name} ${nu.assemblies.map(a => a.total).join('+')}`, failures, log: `${GATE_DIR}/${name}.xml` }
}

async function playerStep() {
  const r = await unityRun('player', ['-executeMethod', 'Sinnoh.Editor.PlayerCompileCheck.Run', '-quit'])
  const log = `${GATE_DIR}/player.log`
  if (!r.ok) return { ok: false, label: 'player', failures: r.failures, log }
  if (r.text.includes('[PlayerCompileCheck] Win64 · WebGL 플레이어 스크립트 컴파일 통과')) return { ok: true, label: 'player' }
  const ce = compileErrors(r.text)
  const lines = r.text.split(/\r?\n/).filter(l => l.includes('[PlayerCompileCheck]'))
  return { ok: false, label: 'player', failures: ce.length ? ce : lines.length ? lines : [`[PlayerCompileCheck] 줄 없음(Unity 종료 코드 ${r.code})`, ...tailLines(r.text, 5)], log }
}

// unityTouched(Unity 단계 앞뒤)·실행 중 목록 밖 변경(시작·끝): git status(+ 크기·시각)를 견준다
function snapshot(root = ROOT, status = readStatus(root)) {
  const m = new Map()
  for (const it of status) {
    let k = it.xy
    try { const s = statSync(path.join(root, it.path)); k += `:${s.size}:${s.mtimeMs}` } catch { /* 지운 파일 */ }
    m.set(it.path, k)
  }
  return m
}
function touchedBetween(a, b) {
  const out = new Set()
  for (const [p, k] of b) if (a.get(p) !== k) out.add(p)
  for (const p of a.keys()) if (!b.has(p)) out.add(p)
  return [...out].sort()
}

/** Unity가 배치에서 다시 쓸 수 있는 파일(재직렬화): 에셋류(.asset·.prefab·.unity·.meta)와 Unity가 관리하는 설정. */
export const unityRewritable = p => /\.(asset|prefab|unity|meta)$/i.test(p) || p.startsWith('ProjectSettings/') || p === 'Packages/packages-lock.json'

/** 실행 중 목록 밖에서 바뀐 경로(검토 반영): Unity 단계 안에서 바뀐 재직렬화 대상만 경고(warnOnly), 나머지(.cs·.mjs·.asmdef·.json·새 파일,
 *  Unity 단계 밖에서 바뀐 에셋)는 실패(fail) — 시험은 그 변경이 든 작업 트리로 돌았는데 영수증·커밋은 목록만 담는다. 문서는 뺀다. */
export function outsideDrift(changed, inList, unityTouched) {
  const byUnity = new Set(unityTouched)
  const warnOnly = [], fail = []
  for (const p of changed) {
    if (isDocPath(p) || inList.has(p)) continue
    ;(byUnity.has(p) && unityRewritable(p) ? warnOnly : fail).push(p)
  }
  return { warnOnly, fail }
}

/** 실행 중 지문이 바뀌었을 때(문서 제외): Unity 단계 중 새로 생긴 목록 안 .meta만 받아들인다(보고서 §4).
 *  고친·지운·새 파일, 바뀐 기존 .meta, Unity 단계 밖에서 생긴 .meta는 other — 시험하지 않은 내용이라 다시 돌린다.
 *  isDoc = 그 저장소의 문서 규칙(연결 저장소는 isLinkedDocPath). */
function driftOf(tree0, tree1, unityTouched, root = ROOT, isDoc = isDocPath) {
  const byUnity = new Set(unityTouched)
  const absorbed = [], other = []
  for (const d of diffNameStatus(tree0, tree1, root)) {
    if (isDoc(d.path)) continue
    if (d.status === 'A' && d.path.endsWith('.meta') && byUnity.has(d.path)) absorbed.push(d.path)
    else other.push(d.path)
  }
  return { absorbed, other }
}

// ---------- 출력·기록 ----------

function appendStatus(line) {
  try { mkdirSync(path.join(ROOT, 'Logs'), { recursive: true }); appendFileSync(path.join(ROOT, 'Logs/status.txt'), `${new Date().toLocaleString('sv-SE')} ${line}\n`, 'utf8') } catch { /* 기록 실패는 무시 */ }
}

function diffNameStatus(a, b, root = ROOT) {
  const parts = gitOk(root, ['diff', '--name-status', '--no-renames', '-z', a, b]).split('\0').filter(Boolean)
  const res = []
  for (let i = 0; i + 1 < parts.length; i += 2) res.push({ status: parts[i][0], path: parts[i + 1] })
  return res
}

/** 연결 저장소 목록. 그 자리에 없거나 자체 git 저장소가 아니면 종료 2(다른 PC·새 사본 — 키트 없이는 Unity·dotnet이 컴파일하지 못한다). */
function linkedOrExit() {
  let list
  try { list = linkedRepos(ROOT) } catch (e) { throw new Exit(2, `Packages/manifest.json 읽기 실패: ${e.message}`) }
  for (const l of list) { const p = linkedProblem(l); if (p) throw new Exit(2, p) }
  return list
}

// ---------- 관문 실행 ----------

/** 돌 층: --full · --fast · --branch가 먼저, 그다음 고른 Unity 시험(--playmode·--editorassets)이 있으면 branch.
 *  없으면 본 폴더는 바뀐 경로로(need) — 연결 저장소 지문을 본 full 영수증이 없으면(unproven) full.
 *  연결된 작업 트리(worktree)는 need가 full일 때 branch(결정 103 ③), 그 밖(문서만·Tools만)은 그대로 — unproven이어도 올리지 않는다. */
export function pickTier(o, { worktree, unproven, need, selects }) {
  if (o.full) return 'full'
  if (o.fast) return 'fast'
  if (o.branch || selects) return 'branch'
  if (worktree) return need === 'full' ? 'branch' : need
  return unproven ? 'full' : need
}

async function gate(o, files) {
  const t0 = Date.now()
  const warnings = []
  const warn = w => warnings.push(w)
  let status0
  try { status0 = readStatus(ROOT) } catch (e) { throw new Exit(2, e.message) }
  const snapRun0 = snapshot(ROOT, status0)   // 끝에서 견준다: 실행 중 목록 밖 변경(지문보다 먼저 떠서 그 사이 바뀐 것도 잡는다)
  const plan0 = buildTree(ROOT, files, { status: status0 })
  const fp8 = plan0.fingerprint.slice(0, 8)
  // 연결 저장소: 지금 내용(미커밋 포함)의 지문. 그 지문을 본 full 영수증이 없으면(unproven) Unity가 봐야 한다
  const links = linkedOrExit().map(l => ({ ...linkedWork(l), unityTouched: [] }))
  const worktree = isLinkedWorktree(ROOT)   // 형제 작업 폴더(결정 103 ③): 기본 층이 full 대신 branch, 본 폴더 영수증도 읽는다
  const receipts0 = readAllReceipts(ROOT)
  const unproven = new Set(links.filter(l => !findLinkedReceipt(receipts0, l.name, l.fingerprint)).map(l => l.name))

  let since = null, sinceDiff = []
  if (o.since) {
    const r = git(ROOT, ['rev-parse', '--verify', '-q', `${o.since}^{commit}`])
    if (r.code !== 0) throw new Exit(2, `--since 커밋을 찾지 못함: ${o.since}`)
    since = r.out.trim()
    sinceDiff = diffNameStatus(since, plan0.tree)
  }
  const scope = [...new Set([...plan0.paths, ...sinceDiff.map(d => d.path)])].sort()
  const selects = Object.keys(SELECT).filter(k => o[k]).map(k => ({ kind: k, ...unityFilter(o[k], k) }))
  const tier = pickTier(o, { worktree, unproven: unproven.size > 0, need: neededTier(scope), selects: selects.length > 0 })
  if (tier === 'branch' && unproven.size) warn(`연결 저장소 지문을 본 full 영수증이 없다(${links.filter(l => unproven.has(l.name)).map(l => `${l.label} ${l.fingerprint.slice(0, 8)}`).join(', ')}) — 가지 층은 Unity로 보지 않는다. 그 저장소 커밋에는 --full 영수증이 있어야 한다`)

  // 0. 준비: 목록 밖 변경, Unity 바쁨
  if (files) {
    const inList = new Set(plan0.paths)
    const outside = allTargets(status0).filter(p => !inList.has(p) && !isDocPath(p))
    if (outside.length) throw new Exit(2, `지정 밖 변경 ${outside.length}개(문서 제외): ${outside.slice(0, 5).join(', ')}${outside.length > 5 ? ' …' : ''} — 목록에 넣거나 치운 뒤 다시`)
    for (const f of files) if (!plan0.hit.has(f)) warn(`바뀐 내용 없음: ${f}`)
  }
  if (tier === 'full' || selects.length) { const b = unityBusy(warn); if (b) throw new Exit(3, b) }

  // --since 경고(싼 것): 목록 밖 경로, 지운 시험, 새 [Ignore]
  if (since) {
    if (files) {
      const covered = p => files.some(f => p === f || p === f + '.meta' || p.startsWith(f + '/') || p.startsWith(f + '.meta'))
      const out = sinceDiff.map(d => d.path).filter(p => !isDocPath(p) && !covered(p))
      if (out.length) warn(`목록 밖 변경(${since.slice(0, 8)}부터) ${out.length}개: ${out.slice(0, 5).join(', ')}${out.length > 5 ? ' …' : ''}`)
    }
    for (const d of sinceDiff) if (d.status === 'D' && d.path.startsWith('Assets/Sinnoh/Tests/') && d.path.endsWith('.cs')) warn(`지운 시험 파일: ${d.path}`)
    for (const ig of scanTestDiff(git(ROOT, ['diff', '-U0', '--no-renames', since, plan0.tree, '--', 'Assets/Sinnoh/Tests']).out).ignores) warn(`새 [Ignore]: ${ig.file}: ${ig.text}`)
  }

  const ctx = { tree: plan0.tree, links, scope, since, sinceDiff, counts: {}, selected: {}, warn }
  const plan = [['precheck', precheckStep]]
  if (tier !== 'md') plan.push(['dotnet', dotnetStep], ['check', checkStep])
  if (tier === 'tools' || ((tier === 'full' || tier === 'branch') && scope.some(p => p.startsWith('Tools/')))) plan.push(['smoke', smokeStep])
  if (tier === 'full') plan.push(['fresh', freshStep], ['unity', null])
  else if (selects.length) plan.push(['unity', null])   // 가지 층: 고른 시험만(필터마다 배치 하나)

  const steps = []
  let failed = null, unityTouched = []
  const doStep = async (name, fn) => {
    const s0 = Date.now()
    const r = await fn(ctx)
    if (!r) return true
    steps.push({ name, ok: r.ok, label: r.label, sec: Math.round((Date.now() - s0) / 100) / 10, failures: r.failures ?? [], log: r.log })
    if (!r.ok) failed = steps[steps.length - 1]
    return r.ok
  }
  for (const [name, fn] of plan) {
    if (name !== 'unity') { if (!(await doStep(name, fn))) break; continue }
    // Unity 단계: 바로 앞에서 다시 바쁨 확인, 자체 잠금, 앞뒤 status로 unityTouched
    const b = unityBusy(warn)
    if (b) throw new Exit(3, b)
    mkdirSync(GATE, { recursive: true })
    writeFileSync(OWN_LOCK, JSON.stringify({ pid: process.pid, start: new Date().toISOString() }), 'utf8')
    const snap0 = snapshot()
    const linkSnap0 = links.map(l => snapshot(l.dir))
    try {
      if (tier !== 'full') { for (const sel of selects) if (!(await doStep(`select-${sel.kind}`, c => unityTestStep(c, SELECT[sel.kind][1], sel)))) break }
      else if (await doStep('editmode', c => unityTestStep(c, 'EditMode')) && await doStep('playmode', c => unityTestStep(c, 'PlayMode'))) await doStep('player', playerStep)
    } finally {
      rmSync(OWN_LOCK, { force: true })
      unityTouched = touchedBetween(snap0, snapshot()).filter(p => !isDocPath(p))   // Unity는 문서를 쓰지 않는다(그 사이 손으로 고친 문서는 뺀다)
      links.forEach((l, i) => { l.unityTouched = touchedBetween(linkSnap0[i], snapshot(l.dir)).filter(p => !isLinkedDocPath(p)) })
    }
    break
  }

  // 대조·래칫·지문(모든 단계가 통과했을 때)
  const extra = []
  const loud = new Set()   // 성공해도 화면에 찍는 경고
  if (!failed) {
    if (tier === 'full') {
      const m = mirrorCompare(ctx.counts, links.map(l => l.name))
      if (m) extra.push(m)
    }
    const receipts = readAllReceipts(ROOT)
    const headNow = git(ROOT, ['rev-parse', '-q', '--verify', 'HEAD']).out.trim() || null
    // 기준 찾기: 연결 저장소마다 [HEAD 지문, 조상 지문…](full 영수증이 본 지문에서 멈춤), 프로젝트는 HEAD 지문 + 조상 지문(full 영수증이 있는 지문에서 멈춤)
    const linkedChains = {}
    for (const l of links) {
      const h = linkedHead(l)
      linkedChains[l.name] = [h.fingerprint, ...olderFingerprints(l.dir, h.head, h.fingerprint, fp => !!findLinkedReceipt(receipts, l.name, fp), isLinkedDocPath)]
    }
    const fpHead = headNow ? fingerprintOfRev(ROOT, headNow) : null
    const linkedSeen = r => links.every(l => (r.linked ?? []).some(x => x.name === l.name && linkedChains[l.name].includes(x.fingerprint)))
    const older = olderFingerprints(ROOT, headNow, fpHead, fp => !!findReceipt(receipts, fp, 'full', linkedSeen))
    const { drops, skipUps, soft } = ratchet(ctx.counts, receipts, { fpHead, older, head: headNow, linked: linkedChains })
    for (const x of drops) {
      const s = `시험 수 줄어듦: ${x.asm} ${x.from} → ${x.to}(커밋된 상태의 통과 영수증 ${x.fp.slice(0, 8)} ${x.at})`
      if (o['allow-test-removal']) warn(`${s} — 허용: ${o['allow-test-removal']}`)
      else extra.push(`${s} — 일부러면 --allow-test-removal "<이유>"`)
    }
    for (const x of soft) {
      if (!x.noBase) { warn(`직전 통과 영수증보다 시험 수 줄어듦(커밋된 상태 기준은 아님 — 버린 실행이면 무시): ${x.asm} ${x.from} → ${x.to}(${x.fp.slice(0, 8)} ${x.at})`); continue }
      // 견줄 커밋된 상태의 영수증을 찾지 못했다 — 막지는 못하지만 숨기지 않는다(성공 줄 아래에 찍는다)
      const w = `시험 수 줄어듦 — 견줄 커밋된 상태의 영수증이 없어 경고만: ${x.asm} ${x.from} → ${x.to}(직전 통과 영수증 ${x.fp.slice(0, 8)} ${x.at}). 일부러 지운 것이 아니면 되살린다`
      warn(w); loud.add(w)
    }
    for (const x of skipUps) warn(`건너뜀 늘어남: ${x.asm} ${x.from} → ${x.to}`)
    if (since) {
      const fpSince = fingerprintOfRev(ROOT, since)
      const base = [...receipts].reverse().find(r => r.ok && r.fingerprint === fpSince)
      if (base) for (const [asm, c] of Object.entries(ctx.counts)) if (base.counts?.[asm] && c.total < base.counts[asm].total) warn(`${since.slice(0, 8)}보다 시험 수 줄어듦: ${asm} ${base.counts[asm].total} → ${c.total}`)
    }
  }
  // 실행 중 파일이 바뀌었나: 끝에 같은 목록으로 다시 지문. Unity 단계 중 새로 생긴 목록 안 .meta만이면 받아들인다(driftOf)
  let plan1 = plan0, links1 = links
  if (!failed && !extra.length) {
    plan1 = buildTree(ROOT, plan0.targets)
    const drift = (t0, t1, touched, root, tag, isDoc) => {
      const { absorbed, other } = driftOf(t0, t1, touched, root, isDoc)
      if (other.length) extra.push(`실행 중 바뀐 파일${tag} ${other.length}개: ${other.slice(0, 5).join(', ')}${other.length > 5 ? ' …' : ''} — 다시 돌린다`)
      else if (absorbed.length) {
        const w = `Unity가 만든 새 .meta ${absorbed.length}개를 지문에 넣음${tag}: ${absorbed.slice(0, 5).join(', ')}${absorbed.length > 5 ? ' …' : ''}`
        warn(w); loud.add(w)
      }
    }
    if (plan1.fingerprint !== plan0.fingerprint) drift(plan0.tree, plan1.tree, unityTouched, ROOT, '')
    // 연결 저장소는 작업 트리 전체가 지문이라 「목록 밖」이 없다 — 실행 중 바뀌면 같은 규칙(Unity 단계 중 새 .meta만 받아들임)
    links1 = links.map(l => ({ ...linkedWork(l), unityTouched: l.unityTouched }))
    links.forEach((l, i) => { if (links1[i].fingerprint !== l.fingerprint) drift(l.tree, links1[i].tree, l.unityTouched, l.dir, `(${l.label})`, isLinkedDocPath) })
    // 목록 밖: 시작 snapshot과 견준다. 목록 밖 코드·새 파일이 실행 중 바뀌었으면 시험한 트리 ≠ 커밋할 트리라 실패
    const { warnOnly, fail: drifted } = outsideDrift(touchedBetween(snapRun0, snapshot()), new Set(plan1.paths), unityTouched)
    const few = a => `${a.length}개: ${a.slice(0, 5).join(', ')}${a.length > 5 ? ' …' : ''}`
    if (drifted.length) extra.push(`실행 중 목록 밖 파일이 바뀜 ${few(drifted)} — 시험은 이 변경이 든 트리로 돌았지만 영수증·커밋은 목록만 담는다. 편집을 끝내고(목록에 넣거나 되돌려) 다시 돌린다`)
    if (warnOnly.length) warn(`Unity 단계 중 목록 밖 에셋·설정이 바뀜(재직렬화로 보고 지문에 안 넣음 — 커밋 전에 되돌릴지 본다) ${few(warnOnly)}`)
  }
  if (extra.length && !failed) { steps.push({ name: 'compare', ok: false, label: 'compare', sec: 0, failures: extra }); failed = steps[steps.length - 1] }

  const ok = !failed
  const dur = fmtDur(Date.now() - t0)
  const head = git(ROOT, ['rev-parse', '-q', '--verify', 'HEAD']).out.trim() || null
  const fp = plan1.fingerprint
  const marks = steps.map(s => `${s.ok ? '✓' : '✗'}${s.label}`).join(' ')
  // 연결 저장소 지문: (새) = 이 지문을 본 full 영수증이 아직 없었다(그래서 full로 올렸다)
  const linkMarks = links1.map(l => ` ${l.label}:${l.fingerprint.slice(0, 8)}${unproven.has(l.name) ? '(새)' : ''}`).join('')
  console.log(`gate ${tier} ${fp.slice(0, 8)}${linkMarks} ${marks} ${dur} ${ok ? 'PASS' : 'FAIL'}${ok && warnings.length ? ` · 경고 ${warnings.length}(${GATE_DIR}/receipt.json)` : ''}`)
  if (!ok) {
    const fl = failed.failures.length ? failed.failures : ['(자세한 내용 없음)']
    for (const f of fl.slice(0, MAX_FAIL)) console.log(`  ✗ ${f}`)
    const again = ['node Tools/gate.mjs', ...process.argv.slice(2)].join(' ')
    console.log(`  ${fl.length > MAX_FAIL ? `…외 ${fl.length - MAX_FAIL}건 · ` : ''}${failed.log ? `로그 ${failed.log} · ` : ''}다음: 고친 뒤 ${again}`)
  } else for (const w of warnings) if (since || o.json || loud.has(w)) console.log(`  ! ${w}`)
  if (!ok && warnings.length) for (const w of warnings.slice(0, 5)) console.log(`  ! ${w}`)

  const receipt = {
    ok, at: new Date().toISOString(), fingerprint: fp, tree: plan1.tree, head, tier, worktree, files, paths: plan1.paths.length,
    selected: selects.length ? ctx.selected : null,
    linked: links1.map(({ name, fingerprint, tree, head, unityTouched }) => ({ name, fingerprint, tree, head, unityTouched })),
    steps: steps.map(({ name, ok, label, sec }) => ({ name, ok, label, sec })), counts: ctx.counts, fresh: ctx.fresh ?? null,
    revert: ctx.revert ?? null, since, warnings, unityTouched, unity: tier === 'full' || selects.length ? path.basename(path.dirname(editorDir())) : null,
    allowTestRemoval: o['allow-test-removal'] ?? null, sec: Math.round((Date.now() - t0) / 1000),
  }
  if (ok) writeReceipt(ROOT, receipt)
  appendStatus(`gate ${tier} ${fp.slice(0, 8)} ${ok ? 'PASS' : `FAIL(${failed.name})`} ${dur}`)
  if (o.json) console.log(JSON.stringify({ ok, tier, fingerprint: fp, tree: plan1.tree, steps: receipt.steps, failures: failed?.failures ?? [], warnings, unityTouched, linked: receipt.linked }))
  return ok ? 0 : 1
}

// ---------- --verify-head ----------

/** HEAD 판정(--verify-head·자기 시험). 첫 부모를 따라 내려가며 문서만 바뀐 커밋은 건너뛴다(지문이 부모와 같다).
 *  문서 밖이 바뀐 커밋은 덮인 층(위 커밋 영수증의 층)보다 높은 층이 필요할 때만 그 커밋 지문의 영수증(또는 Gate-Skip)을 찾는다.
 *  full 영수증이나 Gate-Skip을 만나면 끝, 영수증이 없으면 MISSING(at = 막힌 커밋).
 *  연결 저장소(links = linkedHead 목록): 맨 위 코드 커밋의 영수증은 연결 저장소 HEAD 지문까지 같아야 한다(아래 커밋은 그때의 키트를 알 수 없어 보지 않는다).
 *  그 뒤 연결 저장소마다 문서 밖 미커밋 변경이 없고, HEAD 지문을 본 full 영수증이 있어야 한다(linked = 걸린 저장소 이름).
 *  그 영수증이 없는 연결 저장소 HEAD(문서만 바뀐 커밋은 지나 내려간 코드 커밋 — linkedSkip)가 Gate-Skip 커밋이면 그 저장소는 두 검사 모두 견주지 않고 이유만 적는다.
 *  hook(SubagentStop 훅): 저장소마다 HEAD에 영수증 없는 커밋이 있는지만 본다 — 연결 저장소의 미커밋 변경과 「영수증의 연결 지문 = 연결 저장소 HEAD」 짝은
 *  견주지 않는다(작업 중이거나 키트 커밋과 프로젝트 커밋 사이인 세션의 하위 에이전트가 끝날 때마다 막지 않게. 프로젝트의 미커밋 변경도 막지 않는다).
 *  worktree(연결된 작업 트리 — 형제 작업 폴더, 결정 103 ③): 코드 커밋에 필요한 층이 full 대신 branch이고(commitTier), branch 이상 영수증을 만나면 끝.
 *  본 폴더에서는 가지 영수증만 있는 코드 커밋이 MISSING이다(층을 견준다). 작업 폴더의 기본 영수증은 본 폴더 것 + 자기 것(readAllReceipts).
 *  { ok, head, need, fp, rec, skip, at, why, linked } */
export function headVerdict(root, { worktree = isLinkedWorktree(root), receipts = worktree ? readAllReceipts(root) : readReceipts(root), maxDepth = 200, links = linkedRepos(root).map(linkedHead), hook = false } = {}) {
  const top = worktree ? 'branch' : 'full'   // 필요한 층의 꼭대기: 작업 폴더는 가지 영수증으로 받는다(commitTier)
  const head = git(root, ['rev-parse', '-q', '--verify', 'HEAD']).out.trim()
  if (!head) return null
  const s8 = c => c.slice(0, 8)
  // 연결 저장소마다: HEAD 지문을 본 full 영수증이 있나(proven), 없으면 그 HEAD의 Gate-Skip 이유(skips)
  const proven = new Set(links.filter(l => findLinkedReceipt(receipts, l.name, l.fingerprint)).map(l => l.name))
  const skips = new Map(links.filter(l => !proven.has(l.name)).map(l => [l.name, linkedSkip(l)]).filter(([, why]) => why))
  const linksMatch = r => links.every(l => skips.has(l.name) || (r.linked ?? []).some(x => x.name === l.name && x.fingerprint === l.fingerprint))
  let c = head, first = null, covered = null
  const done = (ok, why, at = null) => ({ ok, head, need: first?.need ?? 'md', fp: first?.fp ?? fingerprintOfRev(root, head), rec: first?.rec ?? null, skip: first?.skip ?? null, at, why, linked: null })
  const walk = () => {
    for (let i = 0; c && i < maxDepth; i++) {
      const parent = git(root, ['rev-parse', '-q', '--verify', `${c}^`]).out.trim()
      const changed = (parent ? gitOk(root, ['diff', '--name-only', '--no-renames', '-z', parent, c]) : gitOk(root, ['ls-tree', '-r', '--name-only', '-z', c])).split('\0').filter(Boolean)
      const need = commitTier(changed, worktree)
      if (need !== 'md' && (!covered || tierRank(need) > tierRank(covered))) {
        const fp = fingerprintOfRev(root, c)
        const skip = (gitOk(root, ['log', '-1', '--format=%B', c]).match(/^Gate-Skip:\s*(.+)$/m) || [])[1] ?? null
        const mine = !first
        const rec = findReceipt(receipts, fp, need, mine && !hook ? linksMatch : undefined)
        if (mine) first = { c, need, fp, skip, rec }
        const where = mine ? (c === head ? '' : ` · ${s8(c)} 기준(위는 문서만)`) : ` · 아래 커밋 ${s8(c)}(필요 ${need})`
        if (!rec && !skip) {
          // 프로젝트 지문의 영수증은 있는데 연결 저장소 지문이 다르다 = 관문 뒤 키트가 바뀌었거나(plain git 커밋 포함) 키트를 아직 커밋하지 않았다
          const other = mine && links.length && findReceipt(receipts, fp, need)
          return done(false, other ? `영수증(${other.tier} ${other.at})의 연결 저장소 지문이 지금 HEAD와 다르다(${links.map(l => `${l.label} ${s8(l.fingerprint)}`).join(', ')})${where}` : `영수증 없음(필요 ${need})${where}`, c)
        }
        if (!rec) return done(true, `${mine ? '' : `영수증 ${first.rec.tier} ${first.rec.at} · `}Gate-Skip: ${skip}${where}`)
        if (!covered || tierRank(rec.tier) > tierRank(covered)) covered = rec.tier
        if (tierRank(covered) >= tierRank(top)) return done(true, `영수증 ${first.rec.tier} ${first.rec.at}${mine ? where : ` · 아래 ${s8(c)} 영수증 ${rec.tier}`}`)
      }
      c = parent
    }
    if (!first) return done(true, '문서만')
    return done(true, `영수증 ${first.rec.tier} ${first.rec.at}${first.c === head ? '' : ` · ${s8(first.c)} 기준(위는 문서만)`}`)
  }
  const v = walk()
  if (!v.ok) return v
  for (const l of links) {
    if (l.dirty.length && !hook) return { ...v, ok: false, linked: l.name, why: `${l.label} 미커밋 변경 ${l.dirty.length}개(문서 제외): ${l.dirty.slice(0, 3).join(', ')}${l.dirty.length > 3 ? ' …' : ''}` }
    if (proven.has(l.name)) continue
    if (!skips.has(l.name)) return { ...v, ok: false, linked: l.name, why: `${l.label} HEAD ${l.head ? s8(l.head) : '(커밋 없음)'}의 지문 ${s8(l.fingerprint)}을 본 full 영수증 없음` }
    v.why += ` · ${l.label} Gate-Skip: ${skips.get(l.name)}`
  }
  return v
}

/** 훅 호출 기록 한 줄(hook-calls.jsonl): 시각·agent_id·agent_type·ok(판정, 판정 전에 끝났으면 null)·exit(훅 종료 코드, 2 = 막음). */
export function hookCallLine(input, ok, exit, at = new Date().toISOString()) {
  return JSON.stringify({ at, agent_id: input?.agent_id ?? null, agent_type: input?.agent_type ?? null, session_id: input?.session_id ?? null, ok, exit })
}

function verifyHead(o) {
  // 훅이면 stdin(훅 JSON)을 먼저 읽고, 어떻게 끝나든(환경 오류 포함) 호출을 기록한다 — 워크플로 에이전트에도 훅이 걸리는지 보는 기록
  let input = null
  if (o.hook) { input = {}; try { input = JSON.parse(readFileSync(0, 'utf8') || '{}') } catch { /* 훅 JSON이 없으면 빈 것 */ } }
  let ok = null, exit = 2
  try {
    const links = linkedOrExit().map(linkedHead)
    const v = headVerdict(ROOT, { links, hook: !!o.hook })
    if (!v) throw new Exit(2, 'HEAD가 없다')
    ok = v.ok
    const { head, need, fp, rec, skip, why } = v
    const dirty = allTargets(readStatus(ROOT)).filter(p => !isDocPath(p))
    const linkDirty = v.linked ? '' : links.filter(l => l.dirty.length).map(l => ` · ${l.label} 미커밋 변경 ${l.dirty.length}개(문서 제외)`).join('')
    const line = `gate verify-head ${head.slice(0, 8)} ${ok ? 'OK' : 'MISSING'} · ${why}${dirty.length ? ` · 미커밋 변경 ${dirty.length}개(문서 제외)` : ''}${linkDirty}`
    const hint = v.linked
      ? `연결 저장소(${v.linked})가 영수증과 맞지 않는다 — 미커밋 변경은 관문 뒤 node Tools/commit-task.mjs 입력에 "repo":"${v.linked}"를 넣어 커밋하고, 이미 커밋했으면(plain git 등) 깨끗한 작업 트리에서 node Tools/gate.mjs --full`
      : `HEAD에 맞는 통과 영수증이 없다${isLinkedWorktree(ROOT) ? '(작업 폴더 — 가지 층 영수증이면 된다)' : ''} — 관문(node Tools/gate.mjs --files …)을 돌리고 node Tools/commit-task.mjs로 커밋한다(이미 커밋했으면 깨끗한 작업 트리에서 node Tools/gate.mjs --since ${v.at ? v.at.slice(0, 8) : 'HEAD'}~1 — 층은 그 뒤 바뀐 경로로 고른다)`
    if (o.hook) return exit = hookVerdict(input, ok, line, hint)
    console.log(line)
    if (!ok) console.log(`  ${hint}`)
    appendStatus(`verify-head ${head.slice(0, 8)} ${ok ? 'OK' : 'MISSING'}`)
    if (o.json) console.log(JSON.stringify({ ok, head, need, fingerprint: fp, receipt: rec ? { tier: rec.tier, at: rec.at } : null, skip: skip ?? null, missingAt: v.at, dirty: dirty.length,
      linked: links.map(l => ({ name: l.name, head: l.head, fingerprint: l.fingerprint, dirty: l.dirty.length })), linkedMissing: v.linked }))
    return exit = ok ? 0 : 4
  } catch (e) {
    // 훅: 판정 전에 끝난 오류(키트 저장소 없음·manifest.json 깨짐 등)도 이유를 stderr로 돌려주고 같은 횟수 제한을 탄다(단계 검토 —
    // 전에는 메시지가 stdout으로만 나가 에이전트가 이유 없이, 2번 막은 뒤 보내는 규칙도 타지 않고 계속 막혔다)
    if (!o.hook) throw e
    return exit = hookVerdict(input, false, `gate verify-head ENV · ${oneLine(e?.message ?? e)}`, '환경 오류로 HEAD를 판정하지 못했다 — 위 이유를 고친 뒤 다시(이 작업에서 고칠 수 없는 것이면 그대로 보고한다)')
  } finally {
    if (o.hook) try { mkdirSync(GATE, { recursive: true }); appendFileSync(path.join(GATE, 'hook-calls.jsonl'), hookCallLine(input, ok, exit) + '\n', 'utf8') } catch { /* 기록 실패는 무시 */ }
  }
}

/** SubagentStop 훅: 영수증 없으면(또는 환경 오류로 판정하지 못했으면) stderr + 종료 2(막음). 같은 에이전트를 2번 막았으면 보내 주고 UNVERIFIED를 남긴다. */
function hookVerdict(input, ok, line, hint) {
  const key = String(input.agent_id ?? input.session_id ?? 'unknown')
  const f = path.join(GATE, 'hook-blocks.json')
  let blocks = {}
  try { blocks = JSON.parse(readFileSync(f, 'utf8')) } catch { /* 처음 */ }
  const save = () => { mkdirSync(GATE, { recursive: true }); writeFileSync(f, JSON.stringify(blocks), 'utf8') }
  if (ok) { if (blocks[key]) { delete blocks[key]; save() } return 0 }
  blocks[key] = (blocks[key] ?? 0) + 1
  save()
  if (blocks[key] > 2) { appendStatus(`UNVERIFIED ${line} (에이전트 ${key}, 2번 막은 뒤 보냄)`); return 0 }
  appendStatus(`verify-head 막음 ${blocks[key]}/2 (에이전트 ${key})`)
  console.error(`${line}\n${hint}`)
  return 2
}

// ---------- 자기 시험 ----------

async function selfTest() {
  let fail = 0
  const eq = (name, got, want) => {
    const a = JSON.stringify(got), b = JSON.stringify(want)
    if (a !== b) { fail++; console.log(`FAIL ${name}\n  got  ${a}\n  want ${b}`) }
  }
  const xml = `<?xml version="1.0"?>
<test-run total="4" result="Failed" failed="1" passed="2" skipped="1" inconclusive="0" id="2">
 <test-suite name="Sinnoh.Tests.EditMode.dll" total="3" passed="2" type="Assembly" failed="1" skipped="0" inconclusive="0">
  <test-case name="A1_가" classname="Sinnoh.Tests.EditMode.FooTests" methodname="A1_가" result="Passed" />
  <test-case result="Failed" label="Error" name="M4_&quot;나&quot;&gt;" classname="Sinnoh.Tests.EditMode.BarTests" methodname="M4_나">
   <failure><message><![CDATA[  Expected: 0
  But was:  3
]]></message><stack-trace><![CDATA[at NUnit.Framework.Assert.That()
at Sinnoh.Tests.EditMode.BarTests.M4_나 () [0x00001] in <PROJECT>\\Assets\\Sinnoh\\Tests\\EditMode\\BarTests.cs:88 ]]></stack-trace></failure>
  </test-case>
  <test-case name="A2_다" methodname="A2_다" classname="X.Y" result="Passed"/>
 </test-suite>
 <test-suite type="Assembly" name='Sinnoh.Tests.EditorAssets.dll' total="1" passed="0" failed="0" skipped="1" inconclusive="0" />
</test-run>`
  const nu = parseNUnit(xml)
  eq('nunit run', nu.run, { result: 'Failed', total: 4, passed: 2, failed: 1, skipped: 1, inconclusive: 0 })
  eq('nunit 어셈블리(속성 순서 무관)', nu.assemblies.map(a => [a.name, a.total, a.failed, a.skipped]), [['Sinnoh.Tests.EditMode', 3, 1, 0], ['Sinnoh.Tests.EditorAssets', 1, 0, 1]])
  eq('nunit 사례', nu.cases.map(c => [c.name, c.result]), [['A1_가', 'Passed'], ['M4_"나">', 'Failed'], ['A2_다', 'Passed']])
  eq('nunit 실패 줄', caseFailures(nu), ['BarTests.M4_"나">: Expected: 0 / But was:  3 (BarTests.cs:88)'])
  eq('dotnet 스택 줄', caseFailures({ cases: [{ result: 'Failed', name: 'A1_x', classname: 'N.C', message: 'm', stack: '   at N.C.A1_x() in D:\\a\\b\\C.cs:line 12' }] }), ['C.A1_x: m (C.cs:12)'])
  eq('컴파일 오류', compileErrors('D:\\r\\Assets\\A.cs(12,5): error CS1002: ; expected [D:\\r\\Tools\\x.csproj]\nAssets/B.cs(3,1): error CS0103: 없음\nD:\\r\\Assets\\A.cs(12,5): error CS1002: ; expected [D:\\r\\Tools\\x.csproj]\nwarning CS0168', 'D:\\r'),
    ['Assets/A.cs:12 CS1002: ; expected', 'Assets/B.cs:3 CS0103: 없음'])
  eq('projectPath', [projectPathOf('Unity.exe -projectpath <WORK>\\uni\\isekai -useHub'), projectPathOf('"Unity.exe" "-batchMode" "-projectPath" "<PROJECT>" "-logFile" x'), projectPathOf('Unity.exe -batchmode')],
    ['<WORK>\\uni\\isekai', '<PROJECT>', null])
  eq('normDir', normDir('<PROJECT>\\') === normDir('<PROJECT>'), true)
  // --check 목록: 프로젝트 것은 바뀐 경로로, 연결 저장소(키트)의 생성 USS 검사는 늘
  const kitLink = [{ name: 'com.rekkon.uikit', dir: 'D:/x/uikit', label: 'uikit' }]
  const uiUss = ['ui-uss', 'D:/x/uikit/Tools~/build-uss.mjs', [['--check', 'Assets/Sinnoh/Client/Ui/Styles~/Sinnoh.uss', 'Assets/Sinnoh/Client/Ui/Resources/SinnohUi']]]
  eq('check 목록 — 키트 USS와 화면 USS는 늘(생성기 자기 시험 → --check, 화면 USS --check)', checksFor(['Assets/Sinnoh/Client/A.cs'], kitLink), [['uikit-uss', 'D:/x/uikit/Tools~/build-uss.mjs', ['--self-test', '--check']], uiUss])
  eq('check 목록 — 프로젝트 것은 --check만', checksFor(['Tools/data/build.mjs'], []), [['data', 'Tools/data/build.mjs', ['--check']]])
  eq('check 목록 — 프로젝트 것 + 키트', checksFor(['Tools/data/build.mjs', 'Tools/assets/build.mjs'], kitLink).map(w => w[0]), ['data', 'assets', 'uikit-uss', 'ui-uss'])
  const cf = ['cardfaces', 'Tools/assets/build.mjs', [['--check', '--only', 'cardfaces']]]
  eq('check 목록 — 카드 굽기 입력(문자열 · 포켓몬 · 기술 표 · cube)이면 cardfaces --check', [checksFor(['Assets/Sinnoh/Data/Json/strings.ko.json'], []), checksFor(['Tools/data/lib/cube.mjs'], [])],
    [[['data', 'Tools/data/build.mjs', ['--check']], cf], [['data', 'Tools/data/build.mjs', ['--check']], cf]])
  eq('check 목록 — 카드 굽기 입력이 아닌 데이터는 data만', checksFor(['Assets/Sinnoh/Data/Json/items.json', 'Tools/data/build.mjs'], []), [['data', 'Tools/data/build.mjs', ['--check']]])
  eq('check 목록 — Tools/assets/면 전체 assets만(cardfaces 겹치지 않게)', checksFor(['Tools/assets/lib/card-faces.mjs', 'Assets/Sinnoh/Data/Json/moves.json'], []).map(w => w[0]), ['data', 'assets'])
  eq('check 목록 — 연결 저장소 없음·모르는 패키지', [checksFor(['Docs/a.cs'], []), checksFor([], [{ name: 'com.other', dir: 'D:/y', label: 'y' }])], [[], []])
  const rcs = [{ ok: true, fingerprint: 'a', at: 't1', counts: { dotnet: { total: 258, skipped: 0 }, X: { total: 10, skipped: 0 } } },
    { ok: false, fingerprint: 'b', counts: { dotnet: { total: 300, skipped: 0 } } },
    { ok: true, fingerprint: 'c', at: 't3', counts: { dotnet: { total: 259, skipped: 1 } } }]
  eq('래칫 줄어듦·건너뜀(HEAD 지문 영수증 기준)', ratchet({ dotnet: { total: 257, skipped: 2 }, X: { total: 10, skipped: 0 }, New: { total: 1, skipped: 0 } }, rcs, { fpHead: 'c' }),
    { drops: [{ asm: 'dotnet', from: 259, to: 257, fp: 'c', at: 't3' }], skipUps: [{ asm: 'dotnet', from: 1, to: 2 }], soft: [] })
  eq('래칫 그대로', ratchet({ dotnet: { total: 259, skipped: 1 } }, rcs, { fpHead: 'c' }), { drops: [], skipUps: [], soft: [] })
  // 검토 반영: 버린 안쪽 고리(--fast) 영수증이 기준을 올리지 않는다 — HEAD와 같은 트리로 돌아오면 경고만
  const rcsFast = [...rcs, { ok: true, fingerprint: 'd', at: 't4', tier: 'fast', counts: { dotnet: { total: 262, skipped: 1 } } }]
  eq('래칫 버린 --fast 영수증은 경고만', ratchet({ dotnet: { total: 259, skipped: 1 } }, rcsFast, { fpHead: 'c' }),
    { drops: [], skipUps: [], soft: [{ asm: 'dotnet', from: 262, to: 259, fp: 'd', at: 't4' }] })
  const rcsHead = [{ ok: true, fingerprint: 'e', head: 'H', at: 't5', counts: { dotnet: { total: 260, skipped: 0 } } }, { ok: true, fingerprint: 'f', head: 'H', at: 't6', counts: { dotnet: { total: 263, skipped: 0 } } }]
  eq('래칫 HEAD 지문 영수증이 없으면 head가 같은 가장 이른 영수증', ratchet({ dotnet: { total: 259, skipped: 0 } }, rcsHead, { fpHead: 'zz', head: 'H' }),
    { drops: [{ asm: 'dotnet', from: 260, to: 259, fp: 'e', at: 't5' }], skipUps: [], soft: [] })
  eq('래칫 기준이 없으면 경고만(noBase — 성공 줄 아래에 찍는다)', ratchet({ dotnet: { total: 257, skipped: 1 } }, rcs, {}), { drops: [], skipUps: [], soft: [{ asm: 'dotnet', from: 259, to: 257, fp: 'c', at: 't3', noBase: true }] })
  // 끝 검토: HEAD가 Tools만 바꾼 커밋(tools 영수증 = dotnet 수뿐)이면 Unity 어셈블리의 기준이 없어 줄어도 경고뿐이었다 → 조상 커밋 지문의 영수증에서 찾는다
  const U = 'Sinnoh.Tests.EditorAssets'
  const rcT = [{ ok: true, fingerprint: 'F0', tier: 'full', at: 't0', counts: { dotnet: { total: 250, skipped: 0 }, [U]: { total: 8, skipped: 0 } } },
    { ok: true, fingerprint: 'F1', tier: 'full', at: 't1', counts: { dotnet: { total: 261, skipped: 0 }, [U]: { total: 10, skipped: 0 } } },
    { ok: true, fingerprint: 'T2', tier: 'tools', at: 't2', counts: { dotnet: { total: 262, skipped: 0 } } }]
  eq('래칫 HEAD 영수증에 그 어셈블리 수가 없으면 가장 가까운 조상 커밋의 영수증', ratchet({ dotnet: { total: 262, skipped: 0 }, [U]: { total: 3, skipped: 0 } }, rcT, { fpHead: 'T2', older: ['F1', 'F0'] }),
    { drops: [{ asm: U, from: 10, to: 3, fp: 'F1', at: 't1' }], skipUps: [], soft: [] })
  eq('래칫 조상 지문을 주지 않으면 예전처럼 경고만(noBase)', ratchet({ [U]: { total: 3, skipped: 0 } }, rcT, { fpHead: 'T2' }).soft, [{ asm: U, from: 10, to: 3, fp: 'F1', at: 't1', noBase: true }])
  eq('래칫 조상보다 HEAD 지문 영수증이 먼저(커밋에서 일부러 줄인 수가 기준)', ratchet({ dotnet: { total: 262, skipped: 0 } }, [...rcT, { ok: true, fingerprint: 'F0', tier: 'full', at: 't9', counts: { dotnet: { total: 270, skipped: 0 } } }], { fpHead: 'T2', older: ['F1', 'F0'] }).drops, [])
  const diff = '+++ b/Assets/Sinnoh/Tests/EditMode/FooTests.cs\n+        [Test, Ignore("A20_4 미정의")]\n+        public void A20_4_새시험() {}\n+        // [Ignore("x")]\n'
    + '+        [TestCase(1, Ignore = "R7 대기")]\n+        public int M4_값(int a) => Helper(a);\n-        [Ignore("옛것")]\n+++ b/Assets/Sinnoh/Tests/EditMode/B.cs\n+   [Ignore]\n'
  const sd = scanTestDiff(diff)
  eq('새 Ignore', sd.ignores.map(i => i.file.split('/').pop()), ['FooTests.cs', 'FooTests.cs', 'B.cs'])
  eq('새 시험 이름', [...sd.names], ['A20_4_새시험', 'M4_값'])
  const eps = entryPoints().map(e => e.rel)
  eq('진입점', ['Tools/gate.mjs', 'Tools/commit-task.mjs', 'Tools/precheck.mjs', 'Tools/data/build.mjs'].every(p => eps.includes(p)) && !eps.some(p => /cli\.mjs|sources\.mjs|\/lib\//.test(p)), true)
  eq('fmtDur', [fmtDur(42_000), fmtDur(221_000)], ['42s', '3m41s'])

  // 명령줄: 모르는 선택·같이 못 쓰는 선택은 2, --help는 0
  const me = fileURLToPath(import.meta.url)
  const code = args => spawnSync(process.execPath, [me, ...args], { encoding: 'utf8', windowsHide: true }).status
  eq('모르는 선택 2', code(['--bogus']), 2)
  eq('--fast --full 2', code(['--fast', '--full']), 2)
  eq('위치 인자만 2', code(['a.cs']), 2)
  eq('--help 0', code(['--help']), 0)

  // 시간 제한: 프로세스 트리(손자까지) 종료
  const t = await run(process.execPath, ['-e', "const c=require('child_process').spawn(process.execPath,['-e','setTimeout(()=>{},60000)'],{stdio:'ignore'});console.log('CHILD '+c.pid);setTimeout(()=>{},60000)"], { timeout: 1500 })
  const grand = Number((String(t.out).match(/CHILD (\d+)/) || [])[1])
  await new Promise(r => setTimeout(r, 300))
  eq('시간 초과 → 트리 종료', [t.timedOut, grand > 0, grand > 0 && alive(grand)], [true, true, false])

  // 지문: 관문(buildTree)과 commit-task가 같은 트리를 본다 — 버리는 저장소에서 영수증 → 커밋, 다른 트리 거부, 문서만 통과, Gate-Skip 트레일러
  const repo = mkdtempSync(path.join(os.tmpdir(), 'sinnoh-gate-selftest-'))
  try {
    const g = (...a) => gitOk(repo, a)
    g('init', '-q'); g('config', 'user.name', 'gate-self-test'); g('config', 'user.email', 'gate@self.test'); g('config', 'core.autocrlf', 'false')
    const w = (f, s) => { mkdirSync(path.dirname(path.join(repo, f)), { recursive: true }); writeFileSync(path.join(repo, f), s) }
    w('a.cs', 'a1'); w('doc.md', 'd1'); w('dir/x.txt', 'x1'); w('.gitignore', 'Logs/\n')
    g('add', '-A'); g('commit', '-q', '-m', 'init')
    w('a.cs', 'a2'); w('c.cs', 'c1'); w('c.cs.meta', 'm1'); rmSync(path.join(repo, 'dir/x.txt')); w('doc.md', 'd2')
    const files = ['a.cs', 'c.cs', 'dir', 'doc.md']
    const p1 = buildTree(repo, files)
    eq('펼친 경로', p1.paths, ['a.cs', 'c.cs', 'c.cs.meta', 'dir/x.txt', 'doc.md'])
    w('doc.md', 'd3')
    eq('문서는 지문 밖', buildTree(repo, files).fingerprint, p1.fingerprint)
    eq('실제 index 그대로', gitOk(repo, ['diff', '--cached', '--name-only']), '')
    const ct = (input, env = {}) => spawnSync(process.execPath, [path.join(ROOT, 'Tools/commit-task.mjs')], { cwd: repo, input: JSON.stringify(input), encoding: 'utf8', windowsHide: true, env: { ...process.env, SINNOH_GATE_SKIP: '', ...env } })
    const last = r => String(r.stdout).trim().split('\n').pop()
    let r = ct({ files, subject: 'S1' })
    eq('영수증 없으면 거부', [r.status, /^FAILED: 관문 영수증 없음/.test(last(r))], [1, true])
    writeReceipt(repo, { ok: true, at: 'now', fingerprint: p1.fingerprint, tier: 'tools' })
    r = ct({ files, subject: 'S1' })
    eq('tools 영수증은 full 변경에 모자람', r.status, 1)
    writeReceipt(repo, { ok: true, at: 'now', fingerprint: p1.fingerprint, tier: 'full' })
    w('a.cs', 'a3')
    r = ct({ files, subject: 'S1' })
    eq('영수증 뒤 바뀐 트리 거부', [r.status, /관문 영수증 없음/.test(last(r))], [1, true])
    w('a.cs', 'a2')
    r = ct({ files, subject: 'S1' })
    eq('같은 지문이면 커밋', [r.status, /^[0-9a-f]{8}$/.test(last(r))], [0, true])
    eq('커밋한 트리 지문 = 영수증', fingerprintOfRev(repo, 'HEAD'), p1.fingerprint)
    const hv = () => headVerdict(repo)
    eq('verify-head 영수증 full', [hv().ok, hv().need, hv().at], [true, 'full', null])
    w('doc.md', 'd4')
    r = ct({ files: ['doc.md'], subject: 'S2' })
    eq('문서만은 영수증 없이', r.status, 0)
    eq('verify-head 문서만 커밋 → 아래 코드 커밋의 영수증', [hv().ok, /기준\(위는 문서만\)/.test(hv().why)], [true, true])
    w('a.cs', 'a5')
    r = ct({ files: ['a.cs'], subject: 'S3' }, { SINNOH_GATE_SKIP: '자기 시험' })
    eq('Gate-Skip 트레일러', [r.status, gitOk(repo, ['log', '-1', '--format=%B']).includes('Gate-Skip: 자기 시험\nCo-Authored-By:')], [0, true])
    eq('verify-head Gate-Skip', [hv().ok, hv().skip], [true, '자기 시험'])

    // 실행 중 바뀐 파일(T1 단계 검토): Unity 단계 중 새로 생긴 목록 안 .meta만 받아들이고, 고친 .cs·새 파일·바뀐 기존 .meta·창 밖 .meta는 다시
    w('e.cs', 'e1'); w('e.cs.meta', 'em1'); g('add', '-A'); g('commit', '-q', '-m', 'e')
    const eSha = g('rev-parse', 'HEAD').trim()
    eq('verify-head plain git 코드 커밋 → MISSING', [hv().ok, hv().at], [false, eSha])
    w('e.cs', 'e2'); w('n.cs', 'n1'); w('f/y.cs', 'y1'); w('o.cs', 'o1')
    const df = ['e.cs', 'n.cs', 'f']
    const q0 = buildTree(repo, df), q0o = buildTree(repo, ['o.cs'])
    w('o.cs.meta', 'om1')   // Unity 단계 앞(dotnet 등)에 생긴 .meta
    const s0 = snapshot(repo)
    const drift = (q, list) => driftOf(q.tree, buildTree(repo, list).tree, touchedBetween(s0, snapshot(repo)), repo)
    w('n.cs.meta', 'nm1')   // Unity가 만든 새 .meta
    eq('창 안 새 .meta만 → 받아들임', drift(q0, df), { absorbed: ['n.cs.meta'], other: [] })
    w('e.cs', 'e3-다른 세션')
    eq('창 안에서 고친 목록 안 .cs → 다시', drift(q0, df), { absorbed: ['n.cs.meta'], other: ['e.cs'] })
    w('f/z.cs', 'z1'); w('e.cs.meta', 'em2-재직렬화')
    eq('폴더 아래 새 파일·바뀐 기존 .meta → 다시', drift(q0, df).other, ['e.cs', 'e.cs.meta', 'f/z.cs'])
    eq('창 밖에서 생긴 .meta → 다시', drift(q0o, ['o.cs']), { absorbed: [], other: ['o.cs.meta'] })

    // 실행 중 목록 밖 변경(검토 반영): 시작 snapshot 뒤 목록 밖 .cs·새 파일·깨끗하던 추적 파일 → 실패, Unity 단계의 목록 밖 에셋·설정 → 경고, 문서 → 무시
    const r0 = snapshot(repo)
    w('o.cs', 'o2-다른 세션'); w('g.cs', 'g1'); w('a.cs', 'a6'); w('x.asset', 'x1'); w('ProjectSettings/p.asset', 'p1'); w('doc.md', 'd9')
    const moved = touchedBetween(r0, snapshot(repo))
    eq('목록 밖 .cs·새 파일·추적 파일 → 실패, Unity 단계 에셋·설정 → 경고', outsideDrift(moved, new Set(buildTree(repo, df).paths), ['x.asset', 'ProjectSettings/p.asset', 'g.cs']),
      { warnOnly: ['ProjectSettings/p.asset', 'x.asset'], fail: ['a.cs', 'g.cs', 'o.cs'] })
    eq('Unity 단계 밖에서 바뀐 에셋 → 실패', outsideDrift(moved, new Set(), []).fail.includes('x.asset'), true)

    // verify-head(검토 반영): plain git 코드 커밋을 문서만·Tools 커밋으로 가리지 못한다
    g('add', 'doc.md'); g('commit', '-q', '-m', 'doc')
    eq('verify-head 문서 커밋이 아래 plain git 커밋을 가리지 못함', [hv().ok, hv().at], [false, eSha])
    w('Tools/t.mjs', 't1'); g('add', 'Tools/t.mjs'); g('commit', '-q', '-m', 'tools')
    writeReceipt(repo, { ok: true, at: 'now', fingerprint: fingerprintOfRev(repo, 'HEAD'), tier: 'tools' })
    eq('verify-head tools 영수증이 아래 full 커밋을 덮지 못함', [hv().ok, hv().at], [false, eSha])
    writeReceipt(repo, { ok: true, at: 'now', fingerprint: fingerprintOfRev(repo, eSha), tier: 'full' })
    eq('verify-head 아래 커밋 영수증이 생기면 통과', [hv().ok, hv().need], [true, 'tools'])

    // 끝 검토: 래칫이 기준을 찾으러 내려가는 조상 지문. 이력(위부터) = tools · doc · e(plain) · S3 · S2(문서만) · S1 · init
    const fpOf = rev => fingerprintOfRev(repo, rev), fpT = fpOf('HEAD')
    const chain = [fpOf(eSha), fpOf(`${eSha}~1`), fpOf(`${eSha}~3`), fpOf(`${eSha}~4`)]
    eq('조상 지문: 가까운 것부터, 문서만 바뀐 커밋(지문이 같다)은 한 번', [olderFingerprints(repo, 'HEAD', fpT, () => false), new Set([fpT, ...chain]).size], [chain, 5])
    eq('조상 지문: enough에서 멈춤(그 지문까지)·rev가 이미 enough면 내려가지 않음', [olderFingerprints(repo, 'HEAD', fpT, fp => fp === chain[1]), olderFingerprints(repo, 'HEAD', fpT, () => true)], [chain.slice(0, 2), []])
    eq('조상 지문: rev와 같은 지문은 빼고·깊이 제한', [olderFingerprints(repo, 'HEAD~1', chain[0], () => false), olderFingerprints(repo, 'HEAD', fpT, () => false, isDocPath, 2)], [chain.slice(1), chain.slice(0, 1)])
  } finally { rmSync(repo, { recursive: true, force: true }) }

  // ── 연결된 패키지 저장소(결정 51): base/proj(Packages/manifest.json의 file:../../kit) + base/kit ──
  const cE = { dotnet: { total: 262 }, 'Sinnoh.Tests.EditMode': { total: 261 }, 'Rekkon.UiKit.Core.Tests': { total: 1 }, 'Sinnoh.Tests.EditorAssets': { total: 10 } }
  eq('대조: dotnet = EditMode + 키트 Core 시험', [mirrorCompare(cE, ['com.rekkon.uikit']), mirrorCompare({ dotnet: { total: 261 }, 'Sinnoh.Tests.EditMode': { total: 261 } }, [])], [null, null])
  eq('대조: 키트 시험이 Unity에서 안 돌면(testables 빠짐) 어긋남', mirrorCompare({ dotnet: { total: 262 }, 'Sinnoh.Tests.EditMode': { total: 261 } }, ['com.rekkon.uikit']),
    'dotnet 262 ≠ Unity Sinnoh.Tests.EditMode 261 + Rekkon.UiKit.Core.Tests 없음')
  eq('대조: 수가 다름', mirrorCompare({ ...cE, dotnet: { total: 263 } }, ['com.rekkon.uikit']), 'dotnet 263 ≠ Unity Sinnoh.Tests.EditMode 261 + Rekkon.UiKit.Core.Tests 1')
  const fb = path.resolve('/t/base'), fr = path.resolve('/w/uni/sinnoh')
  eq('새 사본 자리', [freshPlace(fb, fr, fr), freshPlace(fb, fr, path.resolve('/w/uni/uikit')), freshPlace(fb, fr, path.resolve('/w/uikit')), freshPlace(fb, fr, path.resolve('/w/uni/sinnoh/Packages/x'))],
    [path.join(fb, '_project'), path.join(fb, 'uikit'), null, null])
  eq('훅 호출 기록 줄', [hookCallLine({ agent_id: 'a1', agent_type: 'workflow', session_id: 's1', extra: 1 }, false, 2, 'T'), hookCallLine({}, null, 2, 'T')],
    ['{"at":"T","agent_id":"a1","agent_type":"workflow","session_id":"s1","ok":false,"exit":2}', '{"at":"T","agent_id":null,"agent_type":null,"session_id":null,"ok":null,"exit":2}'])
  const rk = [{ ok: true, fingerprint: 'S', tier: 'full', at: 't1', linked: [{ name: 'k', fingerprint: 'K1' }], counts: { dotnet: { total: 262, skipped: 0 } } },
    { ok: true, fingerprint: 'S', tier: 'full', at: 't2', linked: [{ name: 'k', fingerprint: 'Kx' }], counts: { dotnet: { total: 265, skipped: 0 } } }]
  eq('래칫 기준은 연결 저장소 HEAD 지문까지 같은 영수증(버린 키트 실험은 경고만)', ratchet({ dotnet: { total: 262, skipped: 0 } }, rk, { fpHead: 'S', linked: { k: ['K1'] } }),
    { drops: [], skipUps: [], soft: [{ asm: 'dotnet', from: 265, to: 262, fp: 'S', at: 't2' }] })
  eq('래칫 키트 시험이 줄면 실패', ratchet({ dotnet: { total: 261, skipped: 0 } }, rk, { fpHead: 'S', linked: { k: ['K1'] } }).drops.map(d => [d.from, d.to]), [[262, 261]])
  // 끝 검토: plain git 키트 커밋 뒤(키트 HEAD 지문 Kz = 본 영수증 없음)에는 기준이 모두 없어져 프로젝트 어셈블리까지 경고뿐이었다
  const KA = LINKED_DOTNET_ASM['com.rekkon.uikit'], PA = 'Sinnoh.Tests.PlayMode'
  const rz = [{ ok: true, fingerprint: 'S', tier: 'full', at: 't1', linked: [{ name: 'k', fingerprint: 'K1' }], counts: { dotnet: { total: 294, skipped: 0 }, [KA]: { total: 33, skipped: 0 }, [PA]: { total: 12, skipped: 0 } } },
    { ok: true, fingerprint: 'S', tier: 'full', at: 't2', linked: [{ name: 'k', fingerprint: 'Kx' }], counts: { dotnet: { total: 300, skipped: 0 }, [KA]: { total: 39, skipped: 0 }, [PA]: { total: 12, skipped: 0 } } }]
  const cut = { dotnet: { total: 281, skipped: 0 }, [KA]: { total: 20, skipped: 0 }, [PA]: { total: 10, skipped: 0 } }
  eq('래칫 모르는 키트 HEAD: 프로젝트 어셈블리는 연결 지문을 가리지 않고, 키트 쪽은 키트 조상 지문의 영수증과 견준다', ratchet(cut, rz, { fpHead: 'S', linked: { k: ['Kz', 'K1'] } }).drops.map(d => [d.asm, d.from, d.to, d.at]),
    [['dotnet', 294, 281, 't1'], [KA, 33, 20, 't1'], [PA, 12, 10, 't2']])
  eq('래칫 모르는 키트 HEAD·조상 영수증도 없음: 키트 쪽은 noBase 경고, 프로젝트 어셈블리는 실패', (r => [r.drops.map(d => d.asm), r.soft.map(x => [x.asm, x.noBase])])(ratchet(cut, rz, { fpHead: 'S', linked: { k: ['Kz'] } })),
    [[PA], [['dotnet', true], [KA, true]]])
  eq('래칫 키트 조상보다 키트 HEAD 지문 영수증이 먼저', ratchet({ dotnet: { total: 295, skipped: 0 } }, rz, { fpHead: 'S', linked: { k: ['Kx', 'K1'] } }).drops.map(d => [d.from, d.to]), [[300, 295]])
  eq('래칫 연결 지문이 없는 옛 영수증은 키트 쪽 기준이 못 된다', ratchet({ dotnet: { total: 250, skipped: 0 }, [PA]: { total: 11, skipped: 0 } }, [{ ok: true, fingerprint: 'S', at: 't0', counts: { dotnet: { total: 258, skipped: 0 }, [PA]: { total: 12, skipped: 0 } } }], { fpHead: 'S', linked: { k: ['K1'] } }),
    { drops: [{ asm: PA, from: 12, to: 11, fp: 'S', at: 't0' }], skipUps: [], soft: [{ asm: 'dotnet', from: 258, to: 250, fp: 'S', at: 't0', noBase: true }] })

  const base = mkdtempSync(path.join(os.tmpdir(), 'sinnoh-gate-selftest-kit-'))
  try {
    const proj = path.join(base, 'proj'), kit = path.join(base, 'kit')
    const init = dir => { mkdirSync(dir, { recursive: true }); for (const a of [['init', '-q'], ['config', 'user.name', 'gate-self-test'], ['config', 'user.email', 'gate@self.test'], ['config', 'core.autocrlf', 'false']]) gitOk(dir, a) }
    const w = (root, f, s) => { mkdirSync(path.dirname(path.join(root, f)), { recursive: true }); writeFileSync(path.join(root, f), s) }
    init(base)   // 상위 폴더도 git 저장소(<WORK>/uni처럼) — 자체 저장소가 아닌 키트 폴더를 상위 저장소로 착각하지 않아야 한다
    init(proj)
    w(proj, 'Packages/manifest.json', JSON.stringify({ dependencies: { 'com.test.kit': 'file:../../kit', 'com.test.inside': 'file:local', 'com.unity.x': '1.0.0' } }))
    w(proj, 'a.cs', 'a1'); w(proj, '.gitignore', 'Logs/\n')
    gitOk(proj, ['add', '-A']); gitOk(proj, ['commit', '-q', '-m', 'init'])
    const found = linkedRepos(proj), L = found[0]
    eq('연결 저장소 = 프로젝트 밖 file: 의존만', [found.length, L.name, L.label, normDir(L.dir) === normDir(kit)], [1, 'com.test.kit', 'kit', true])
    eq('연결 저장소 폴더 없음', /^연결된 패키지 저장소 없음: com\.test\.kit → /.test(linkedProblem(L)), true)
    const ct = (input, env = {}) => spawnSync(process.execPath, [path.join(ROOT, 'Tools/commit-task.mjs')], { cwd: proj, input: JSON.stringify(input), encoding: 'utf8', windowsHide: true, env: { ...process.env, SINNOH_GATE_SKIP: '', ...env } })
    const last = r => String(r.stdout).trim().split('\n').pop()
    const hash = r => /^[0-9a-f]{8}$/.test(last(r))
    w(proj, 'a.cs', 'a2')
    let r = ct({ files: ['a.cs'], subject: 'P0' })
    eq('연결 저장소가 없으면 프로젝트 코드 커밋 거부', [r.status, /^FAILED: 연결된 패키지 저장소 없음/.test(last(r))], [1, true])
    w(proj, 'a.cs', 'a1')
    w(kit, 'package.json', '{}')
    eq('자체 git 저장소가 아닌 폴더(상위 저장소에 얹힘)', /자체 git 저장소가 아니다/.test(linkedProblem(L)), true)
    init(kit); w(kit, 'Core/K.cs', 'k1'); w(kit, 'README.md', 'r1')
    eq('연결 저장소 정상', linkedProblem(L), null)
    const k0 = linkedWork(L)
    eq('커밋 없는 키트: 전부 미커밋(문서 제외)', [k0.head, k0.dirty], [null, ['Core/K.cs', 'package.json']])
    const S0 = fingerprintOfRev(proj, 'HEAD')
    const rc = (fp, k, tier = 'full') => writeReceipt(proj, { ok: true, at: 'now', fingerprint: fp, tier, linked: [{ name: L.name, fingerprint: k }] })
    const K = 'com.test.kit', all = ['Core', 'package.json', 'README.md']

    // 키트만 바꾼 변경: 영수증 없이는 거부 → 관문(full) 뒤 키트 커밋 → 지문은 커밋해도 그대로
    r = ct({ repo: K, files: all, subject: 'K1' })
    eq('키트: 영수증 없으면 거부', [r.status, /^FAILED: 관문 영수증 없음\(필요: full, kit 지문 /.test(last(r))], [1, true])
    eq('키트: 모르는 repo', [ct({ repo: 'com.nope', files: all, subject: 'K1' }).status, /repo를 찾지 못함: com\.nope/.test(last(ct({ repo: 'com.nope', files: all, subject: 'K1' })))], [1, true])
    rc(S0, k0.fingerprint, 'tools')
    eq('키트: tools 영수증은 모자람(Unity가 키트를 보지 않았다)', ct({ repo: K, files: all, subject: 'K1' }).status, 1)
    rc(S0, k0.fingerprint)
    r = ct({ repo: K, files: ['Core', 'README.md'], subject: 'K1' })
    eq('키트: 문서 밖 변경을 일부만 넣으면 거부(빠진 경로 안내)', [r.status, /kit의 목록 밖 변경 1개: package\.json/.test(last(r))], [1, true])
    r = ct({ repo: K, files: all, subject: 'K1' })
    eq('키트: 영수증이 있으면 커밋', [r.status, hash(r)], [0, true])
    eq('키트: 커밋해도 지문 그대로', [linkedWork(L).fingerprint === k0.fingerprint, linkedHead(L).fingerprint === k0.fingerprint, linkedHead(L).dirty], [true, true, []])
    eq('키트: 커밋은 키트 저장소에(프로젝트는 그대로)', [gitOk(kit, ['log', '-1', '--format=%s']).trim(), gitOk(proj, ['log', '-1', '--format=%s']).trim()], ['K1', 'init'])

    // 프로젝트 커밋: 영수증의 연결 지문이 키트의 지금 내용과 같아야 한다
    w(proj, 'a.cs', 'a2')
    const S1 = buildTree(proj, ['a.cs']).fingerprint
    writeReceipt(proj, { ok: true, at: 'now', fingerprint: S1, tier: 'full' })
    eq('프로젝트: 연결 지문이 없는 옛 영수증은 안 침', ct({ files: ['a.cs'], subject: 'P1' }).status, 1)
    rc(S1, k0.fingerprint)
    w(kit, 'Core/K.cs', 'k2')
    r = ct({ files: ['a.cs'], subject: 'P1' })
    eq('프로젝트: 관문 뒤 키트를 고치면 거부', [r.status, /^FAILED: 관문 영수증 없음\(필요: full, 지문 [0-9a-f]{12}, 연결 com\.test\.kit=[0-9a-f]{8}\)/.test(last(r))], [1, true])
    eq('키트: 관문 뒤 고친 키트도 거부', ct({ repo: K, files: ['Core'], subject: 'K2' }).status, 1)
    w(kit, 'Core/K.cs', 'k1'); w(kit, 'README.md', 'r2')
    r = ct({ files: ['a.cs'], subject: 'P1' })
    eq('프로젝트: 키트가 영수증과 같으면 커밋(키트 문서는 지문 밖)', [r.status, hash(r)], [0, true])
    eq('키트: 문서만은 영수증 없이', ct({ repo: K, files: ['README.md'], subject: 'K-doc' }).status, 0)
    const hv = () => headVerdict(proj)
    eq('verify-head: 프로젝트·키트 모두 영수증대로', [hv().ok, hv().linked], [true, null])

    // 단계 검토: 키트의 문서는 *.md뿐 — 저장소 루트가 패키지 루트라 Docs/ 아래도 Unity가 들여오고 asmdef가 있으면 컴파일한다
    eq('연결 저장소 문서 경로는 *.md만', ['README.md', 'Docs/guide.md', 'Docs/Extra.cs', 'Docs/Extra.asmdef', 'Docs/mock.html', 'Runtime/Core/A.cs'].map(isLinkedDocPath), [true, true, false, false, false, false])
    const kDocs = ['Docs/Extra.asmdef', 'Docs/Extra.cs']
    w(kit, kDocs[0], '{}'); w(kit, kDocs[1], 'x1'); w(kit, 'Docs/guide.md', 'g1')
    eq('키트: Docs/ 아래 코드는 문서가 아니다(지문·미커밋 변경에 든다)', [linkedWork(L).fingerprint === k0.fingerprint, linkedWork(L).dirty, linkedHead(L).dirty], [false, kDocs, kDocs])
    r = ct({ repo: K, files: ['Docs'], subject: 'K-docs' })
    eq('키트: Docs/ 아래 코드는 영수증 없이 커밋되지 않는다', [r.status, /^FAILED: 관문 영수증 없음\(필요: full, kit 지문 /.test(last(r))], [1, true])
    eq('verify-head: 키트 Docs/ 미커밋 코드 → MISSING', [hv().ok, hv().linked], [false, K])
    for (const p of kDocs) rmSync(path.join(kit, p))
    eq('키트: Docs/ 아래 *.md는 문서(지문 밖, 영수증 없이 커밋)', [linkedWork(L).fingerprint === k0.fingerprint, ct({ repo: K, files: ['Docs'], subject: 'K-guide' }).status, hv().ok], [true, 0, true])

    // 키트만 바꾼 변경 → 프로젝트 HEAD는 그대로, 키트 HEAD 지문이 새 영수증과 맞아야 한다
    w(kit, 'Core/K.cs', 'k3'); w(kit, 'Core/K.cs.meta', 'm1')
    eq('verify-head: 키트 미커밋 코드 변경 → MISSING', [hv().ok, hv().linked, /^kit 미커밋 변경 2개/.test(hv().why)], [false, K, true])
    eq('verify-head(훅): 키트 미커밋 변경으로는 막지 않는다', headVerdict(proj, { hook: true }).ok, true)
    rc(S1, linkedWork(L).fingerprint)
    eq('키트만 바꾼 변경: 관문 뒤 커밋', ct({ repo: K, files: ['Core'], subject: 'K3' }).status, 0)
    eq('verify-head: 키트만 바꾼 커밋 뒤 OK', hv().ok, true)
    // 커밋 사이(관문 → 키트 커밋 → 프로젝트 커밋): 엄격한 판정은 MISSING, 훅은 막지 않는다(각 저장소 HEAD에는 영수증이 있다)
    w(proj, 'a.cs', 'a3'); w(kit, 'Core/K.cs', 'k3b')
    rc(buildTree(proj, ['a.cs']).fingerprint, linkedWork(L).fingerprint)
    eq('커밋 사이: 키트 먼저 커밋', ct({ repo: K, files: ['Core'], subject: 'K3b' }).status, 0)
    eq('verify-head: 키트만 먼저 커밋된 사이 — 엄격 MISSING, 훅은 통과', [hv().ok, headVerdict(proj, { hook: true }).ok], [false, true])
    eq('커밋 사이: 프로젝트 커밋', ct({ files: ['a.cs'], subject: 'P3' }).status, 0)
    eq('verify-head: 두 커밋 뒤 OK', hv().ok, true)
    w(kit, 'README.md', 'r3')
    eq('verify-head: 키트 미커밋 문서만이면 통과', hv().ok, true)
    gitOk(kit, ['add', '-A']); gitOk(kit, ['commit', '-q', '-m', 'doc'])
    w(kit, 'Core/K.cs', 'k4'); gitOk(kit, ['add', '-A']); gitOk(kit, ['commit', '-q', '-m', 'plain'])
    eq('verify-head: plain git 키트 코드 커밋 → MISSING', [hv().ok, /연결 저장소 지문이 지금 HEAD와 다르다\(kit [0-9a-f]{8}\)/.test(hv().why), hv().at === gitOk(proj, ['rev-parse', 'HEAD']).trim()], [false, true, true])
    eq('verify-head(훅): plain git 키트 코드 커밋은 막는다', [headVerdict(proj, { hook: true }).ok, headVerdict(proj, { hook: true }).linked], [false, K])
    // 관문은 이런 키트(지문을 본 full 영수증 없음)를 보면 full로 올린다. 그래도 낮은 층 영수증이 생겼다면 verify-head가 잡는다
    const kPlain = linkedHead(L).fingerprint
    w(proj, 'Tools/t.mjs', 't1')
    rc(buildTree(proj, ['Tools']).fingerprint, kPlain, 'tools')
    eq('프로젝트: Tools 커밋(tools 영수증, 연결 지문 같음)', ct({ files: ['Tools'], subject: 'P2' }).status, 0)
    eq('verify-head: tools 영수증이 full 영수증 없는 키트 HEAD를 가리지 못함', [hv().ok, hv().linked, /^kit HEAD [0-9a-f]{8}의 지문 [0-9a-f]{8}을 본 full 영수증 없음/.test(hv().why)], [false, K, true])
    rc(fingerprintOfRev(proj, 'HEAD'), kPlain)
    eq('verify-head: 그 키트 지문의 full 영수증이 생기면 통과', hv().ok, true)
    w(kit, 'Core/K.cs', 'k5')
    r = ct({ repo: K, files: ['Core'], subject: 'K5' }, { SINNOH_GATE_SKIP: '자기 시험' })
    eq('키트 Gate-Skip 커밋', [r.status, gitOk(kit, ['log', '-1', '--format=%B']).includes('Gate-Skip: 자기 시험\nCo-Authored-By:')], [0, true])
    w(kit, 'README.md', 'r4'); gitOk(kit, ['add', '-A']); gitOk(kit, ['commit', '-q', '-m', 'doc2'])
    eq('verify-head: 키트 Gate-Skip(위에 문서만 커밋)', [hv().ok, / · kit Gate-Skip: 자기 시험$/.test(hv().why)], [true, true])
    // 단계 검토: Gate-Skip은 지문을 바꾼 코드 커밋의 것만 읽는다 — plain git 코드 커밋 위에 얹은 문서 커밋의 본문 줄로 가리지 못한다(프로젝트 쪽과 같게)
    w(kit, 'Core/K.cs', 'k5b'); gitOk(kit, ['add', '-A']); gitOk(kit, ['commit', '-q', '-m', 'plain2'])
    const hvh = () => headVerdict(proj, { hook: true })
    eq('verify-head: Gate-Skip 커밋 위 plain git 키트 코드 커밋 → MISSING(훅도 막음)', [hv().ok, hvh().ok, hvh().linked], [false, false, K])
    w(kit, 'README.md', 'r5')
    r = ct({ repo: K, files: ['README.md'], subject: 'K-doc2', body: 'Gate-Skip: 문서 정리' })
    eq('verify-head: 문서 커밋 본문의 Gate-Skip 줄은 아래 plain git 코드 커밋을 가리지 못함', [r.status, hv().ok, hvh().ok, hvh().linked, /^kit HEAD [0-9a-f]{8}의 지문 [0-9a-f]{8}을 본 full 영수증 없음$/.test(hvh().why)], [0, false, false, K, true])
    const kB = linkedHead(L).fingerprint
    w(kit, 'Docs/Extra.cs', 'x1'); gitOk(kit, ['add', '-A']); gitOk(kit, ['commit', '-q', '-m', 'plain3'])
    eq('키트: 커밋된 Docs/ 코드도 HEAD 지문에 든다', linkedHead(L).fingerprint === kB, false)

    // 실행 중 키트 변경: Unity 단계 중 새로 생긴 .meta만 받아들이고(패키지 폴더도 .meta가 필요하다), 고친 .cs·창 밖 .meta는 다시
    const q0 = linkedWork(L)
    w(kit, 'Core/Early.cs', 'e1')
    const q1 = linkedWork(L)
    w(kit, 'Core/Early.cs.meta', 'em')   // Unity 단계 앞에 생긴 .meta
    const s0 = snapshot(kit)
    w(kit, 'package.json.meta', 'pm'); w(kit, 'Core.meta', 'cm'); w(kit, 'README.md.meta', 'rm')
    const kd = q => driftOf(q.tree, linkedWork(L).tree, touchedBetween(s0, snapshot(kit)), kit, isLinkedDocPath)
    eq('키트: Unity 단계 중 새 .meta만 → 받아들임', [kd(q1).absorbed.length, kd(q1).other], [3, ['Core/Early.cs.meta']])
    gitOk(kit, ['add', '-A']); gitOk(kit, ['commit', '-q', '-m', 'meta'])
    const q2 = linkedWork(L), s1 = snapshot(kit)
    w(kit, 'Core/K.cs', 'k6-실행 중'); w(kit, 'Core/New.cs.meta', 'nm'); w(kit, 'README.md', 'r6'); w(kit, 'Docs/Extra.cs', 'x2-실행 중')
    eq('키트: 실행 중 고친 .cs → 다시(문서 *.md는 무시, Docs/ 아래 코드는 본다)', driftOf(q2.tree, linkedWork(L).tree, touchedBetween(s1, snapshot(kit)), kit, isLinkedDocPath), { absorbed: ['Core/New.cs.meta'], other: ['Core/K.cs', 'Docs/Extra.cs'] })
    eq('키트: 지문은 작업 트리 전체(미커밋 포함)', [q0.fingerprint === q1.fingerprint, linkedWork(L).fingerprint === q2.fingerprint], [false, false])
  } finally { rmSync(base, { recursive: true, force: true }) }

  // ── 가지 층(결정 103 ③): 형제 작업 폴더(연결된 작업 트리)는 가벼운 확인으로 커밋, 본 폴더는 그대로 full ──
  eq('층 순서: fast < tools < branch < full', ['md', 'fast', 'tools', 'branch', 'full'].map(tierRank), [0, 1, 2, 3, 4])
  const pt = (o, worktree, need, more = {}) => pickTier(o, { worktree, unproven: false, need, selects: false, ...more })
  eq('돌 층: 본 폴더는 그대로', [pt({}, false, 'full'), pt({}, false, 'tools'), pt({}, false, 'md'), pt({}, false, 'tools', { unproven: true }), pt({ fast: true }, false, 'full'), pt({ branch: true }, false, 'full'), pt({}, false, 'full', { selects: true })],
    ['full', 'tools', 'md', 'full', 'fast', 'branch', 'branch'])
  eq('돌 층: 작업 폴더는 full 대신 branch(문서만·Tools만은 그대로, 키트가 새 지문이어도 올리지 않음, --full은 full)', [pt({}, true, 'full'), pt({}, true, 'tools'), pt({}, true, 'md'), pt({}, true, 'full', { unproven: true }), pt({ full: true }, true, 'full'), pt({ fast: true }, true, 'full'), pt({}, true, 'tools', { selects: true })],
    ['branch', 'tools', 'md', 'branch', 'full', 'fast', 'branch'])
  const uf = unityFilter('RoomFlowTests; M7_Ping,RoomFlowTests')
  eq('필터: 이름 조각 여럿 → 그 어셈블리 이름 공간 뒤에서만 찾는 정규식(「;」 없음)', [uf.names, uf.regex], [['RoomFlowTests', 'M7_Ping'], '^(?!.*(?:NetOnlineTests|WebGuestLiveTests|M7_Online_|M9_Online_))Sinnoh\\.Tests\\.PlayMode\\..*(?:RoomFlowTests|M7_Ping)'])
  const ure = new RegExp(unityFilter('Net;Room').regex), inPlay = n => 'Sinnoh.Tests.PlayMode.' + n
  eq('필터: 조각이 온라인 시험에 걸려도 뺀다 · 이름 공간·dll 경로(위 묶음)는 안 걸린다', [inPlay('NetLoopTests.M7_가'), inPlay('RoomFlowTests.M5_나'), inPlay('NetOnlineTests.M7_Online_익명'), inPlay('NetOnlineTests.M7_다'), inPlay('WebGuestLiveTests.M7_Online_웹'), 'Sinnoh.Tests.PlayMode', 'D:/x/RoomNet/Library/ScriptAssemblies/Sinnoh.Tests.PlayMode.dll', 'Sinnoh.Tests.EditMode.NetTests.A1'].map(n => ure.test(n)),
    [true, true, false, false, false, false, false, false])
  eq('필터: all = 정규식 없이 어셈블리만 · EditorAssets는 자기 이름 공간', [unityFilter('ALL', 'editorassets'), unityFilter('GenTests', 'editorassets').regex.includes('Sinnoh\\.Tests\\.EditorAssets\\.')], [{ names: ['all'], regex: null }, true])
  eq('필터: 온라인 시험 이름·정규식 글자·빈 것은 오류', ['NetOnlineTests', 'RoomFlowTests;m7_online_x', 'WebGuestLiveTests', 'a|b', '.*', ' ; '].map(t => !!unityFilter(t).error), [true, true, true, true, true, true])
  eq('명령줄: 가지 층 선택', [code(['--branch', '--full']), code(['--branch', '--fast']), code(['--playmode', 'NetOnlineTests']), code(['--playmode', 'X', '--full']), code(['--editorassets', 'a(b']), code(['--verify-head', '--branch'])], [2, 2, 2, 2, 2, 2])
  const wb = mkdtempSync(path.join(os.tmpdir(), 'sinnoh-gate-selftest-wt-'))
  try {
    const m = path.join(wb, 'main'), wt = path.join(wb, 'wt')
    mkdirSync(m)
    for (const a of [['init', '-q'], ['config', 'user.name', 'gate-self-test'], ['config', 'user.email', 'gate@self.test'], ['config', 'core.autocrlf', 'false']]) gitOk(m, a)
    const w = (root, f, s) => { mkdirSync(path.dirname(path.join(root, f)), { recursive: true }); writeFileSync(path.join(root, f), s) }
    w(m, 'a.cs', 'a1'); w(m, 'Tools/t.mjs', 't1'); w(m, '.gitignore', 'Logs/\n')
    gitOk(m, ['add', '-A']); gitOk(m, ['commit', '-q', '-m', 'init'])
    gitOk(m, ['worktree', 'add', '-q', '-b', 'wt', wt])
    eq('작업 트리 판별: 주 작업 트리 · 연결된 작업 트리 · git 밖', [isLinkedWorktree(m), isLinkedWorktree(wt), normDir(mainWorktreeOf(wt) ?? '.') === normDir(m), isLinkedWorktree(wb)], [false, true, true, false])
    const ct = (cwd, input) => spawnSync(process.execPath, [path.join(ROOT, 'Tools/commit-task.mjs')], { cwd, input: JSON.stringify(input), encoding: 'utf8', windowsHide: true, env: { ...process.env, SINNOH_GATE_SKIP: '' } })
    const last = r => String(r.stdout).trim().split('\n').pop()
    const rc = (root, fp, tier) => writeReceipt(root, { ok: true, at: 'now', fingerprint: fp, tier })
    w(wt, 'a.cs', 'a2'); w(m, 'a.cs', 'a2')   // 같은 변경을 두 폴더에(가지에서 고치고 본 폴더에 패치로 합치는 꼴) — 지문이 같다
    const fp = buildTree(wt, ['a.cs']).fingerprint
    eq('두 폴더의 같은 변경은 지문이 같다', buildTree(m, ['a.cs']).fingerprint, fp)
    let r = ct(wt, { files: ['a.cs'], subject: 'W1' })
    eq('작업 폴더: 영수증 없으면 거부(필요 branch)', [r.status, /^FAILED: 관문 영수증 없음\(필요: branch,/.test(last(r))], [1, true])
    rc(wt, fp, 'tools')
    eq('작업 폴더: tools 영수증은 모자람', ct(wt, { files: ['a.cs'], subject: 'W1' }).status, 1)
    rc(wt, fp, 'branch')
    r = ct(m, { files: ['a.cs'], subject: 'M1' })
    eq('본 폴더: 작업 폴더의 영수증은 읽지 않는다', [r.status, /^FAILED: 관문 영수증 없음\(필요: full,/.test(last(r))], [1, true])
    r = ct(wt, { files: ['a.cs'], subject: 'W1' })
    eq('작업 폴더: 가지 영수증으로 커밋', [r.status, /^[0-9a-f]{8}$/.test(last(r))], [0, true])
    eq('verify-head 작업 폴더: 가지 영수증으로 OK', (v => [v.ok, v.need, v.rec?.tier])(headVerdict(wt)), [true, 'branch', 'branch'])
    eq('verify-head 같은 커밋·같은 영수증을 본 폴더 규칙으로 보면 MISSING(가지 영수증을 full로 치지 않는다)', (v => [v.ok, v.need, /영수증 없음\(필요 full\)/.test(v.why)])(headVerdict(wt, { worktree: false, receipts: readReceipts(wt) })), [false, 'full', true])
    rc(m, fp, 'branch')   // 같은 가지 영수증이 본 폴더에 있어도(옮겨 왔거나 --branch로 돌았다)
    r = ct(m, { files: ['a.cs'], subject: 'M1' })
    eq('본 폴더: 가지 영수증으로는 거부(무엇이 모자란지 짚는다)', [r.status, /^FAILED: 관문 영수증 없음\(필요: full, 지문 [0-9a-f]{12}\) — 같은 지문의 branch 영수증\(now\)은 있지만 full에 모자란다/.test(last(r))], [1, true])
    gitOk(m, ['add', '-A']); gitOk(m, ['commit', '-q', '-m', 'plain'])
    eq('verify-head 본 폴더: 가지 영수증만 있는 코드 커밋 → MISSING', (v => [v.ok, v.need])(headVerdict(m)), [false, 'full'])
    rc(m, fp, 'full')
    eq('verify-head 본 폴더: full 영수증이 생기면 OK', headVerdict(m).ok, true)
    // 작업 폴더는 본 폴더의 영수증도 읽는다(같은 지문의 full 영수증 · 갈라져 나온 main 커밋의 영수증)
    w(wt, 'a.cs', 'a3')
    rc(m, buildTree(wt, ['a.cs']).fingerprint, 'full')
    eq('작업 폴더: 본 폴더의 full 영수증도 읽는다', [readAllReceipts(wt).length, readReceipts(wt).length, readAllReceipts(m).length, ct(wt, { files: ['a.cs'], subject: 'W2' }).status], [5, 2, 3, 0])
    // Tools만 바꾼 커밋은 어디서나 tools 층: 그 아래 코드 커밋은 작업 폴더에서 branch 이상 영수증이 있어야 한다
    w(wt, 'Tools/t.mjs', 't2')
    rc(wt, buildTree(wt, ['Tools']).fingerprint, 'tools')
    eq('작업 폴더: Tools만은 tools 영수증', ct(wt, { files: ['Tools'], subject: 'W3' }).status, 0)
    eq('verify-head 작업 폴더: tools 영수증 + 아래 커밋의 (본 폴더) full 영수증', (v => [v.ok, v.need])(headVerdict(wt)), [true, 'tools'])
    w(wt, 'b.cs', 'b1'); gitOk(wt, ['add', '-A']); gitOk(wt, ['commit', '-q', '-m', 'plain-wt'])
    eq('verify-head 작업 폴더: plain git 코드 커밋 → MISSING(필요 branch)', (v => [v.ok, v.need, /영수증 없음\(필요 branch\)/.test(v.why)])(headVerdict(wt)), [false, 'branch', true])
  } finally { rmSync(wb, { recursive: true, force: true }) }

  // 단계 검토: 훅의 환경 오류(키트 저장소 없음·manifest.json 깨짐)도 이유를 stderr로 돌려주고 2번 막은 뒤 보낸다 — 스크립트 사본을 임시 프로젝트에서 돈다
  const hb = mkdtempSync(path.join(os.tmpdir(), 'sinnoh-gate-selftest-hook-'))
  try {
    const hp = path.join(hb, 'proj')
    for (const f of ['gate.mjs', 'cli.mjs', 'sources.mjs', 'lib/tree.mjs']) {
      mkdirSync(path.dirname(path.join(hp, 'Tools', f)), { recursive: true })
      copyFileSync(path.join(ROOT, 'Tools', f), path.join(hp, 'Tools', f))
    }
    mkdirSync(path.join(hp, 'Packages'))
    const hook = id => { const r = spawnSync(process.execPath, [path.join(hp, 'Tools/gate.mjs'), '--verify-head', '--hook'], { input: JSON.stringify({ agent_id: id }), encoding: 'utf8', windowsHide: true }); return [r.status, r.stderr] }
    writeFileSync(path.join(hp, 'Packages/manifest.json'), JSON.stringify({ dependencies: { 'com.test.kit': 'file:../../nokit' } }))
    const h1 = [hook('e1'), hook('e1'), hook('e1')]
    eq('훅: 키트 저장소 없음 → 이유를 stderr로, 2번 막고 3번째는 보냄', [h1.map(x => x[0]), h1.slice(0, 2).every(x => /^gate verify-head ENV · 연결된 패키지 저장소 없음: com\.test\.kit /.test(x[1]))], [[2, 2, 0], true])
    writeFileSync(path.join(hp, 'Packages/manifest.json'), '{ "dependencies": { ')
    const h2 = [hook('e2'), hook('e2'), hook('e2')]
    eq('훅: manifest.json 깨짐 → 같은 규칙', [h2.map(x => x[0]), /^gate verify-head ENV · Packages\/manifest\.json 읽기 실패/.test(h2[0][1])], [[2, 2, 0], true])
    const read = f => { try { return readFileSync(path.join(hp, f), 'utf8') } catch { return '' } }
    eq('훅: 환경 오류로 보낸 것은 UNVERIFIED로 남는다', (read('Logs/status.txt').match(/UNVERIFIED gate verify-head ENV · /g) ?? []).length, 2)
    eq('훅: 환경 오류도 호출 기록에 남는다(판정 없음 = ok null)', read('Logs/gate/hook-calls.jsonl').trim().split('\n').filter(Boolean).map(l => { const j = JSON.parse(l); return `${j.agent_id} ${j.ok} ${j.exit}` }),
      ['e1 null 2', 'e1 null 2', 'e1 null 0', 'e2 null 2', 'e2 null 2', 'e2 null 0'])
  } finally { rmSync(hb, { recursive: true, force: true }) }

  console.log(fail ? `gate self-test: ${fail}건 실패` : 'gate self-test: OK')
  return fail ? 1 : 0
}

// ---------- 진입 ----------

const USAGE = `사용: node Tools/gate.mjs [--fast | --branch | --full] [--playmode "<필터>"] [--editorassets "<필터>"] [--files <경로…>] [--since <sha>] [--allow-test-removal "<이유>"] [--json]
      node Tools/gate.mjs --verify-head [--hook] [--json]
      node Tools/gate.mjs --self-test
  층: 기본은 바뀐 경로로(문서만 → precheck, Tools/만(Tools/assets·sources.json 빼고) → fast + 진입점 연기 검사, 그 밖 → full)
  가지 층(branch, 결정 103): 형제 작업 폴더(git worktree)에서는 「그 밖」의 기본이 full 대신 가지 층 = fast(+ Tools/가 바뀌었으면 연기 검사) + 고른 Unity 시험.
    --playmode "RoomFlowTests;M7_Ping" · --editorassets "<필터>": 건드린 시험 클래스·이름 조각만 Unity 배치로(all = 그 어셈블리 전부). 없으면 Unity를 띄우지 않는다.
    실제 UGS 시험(NetOnlineTests·WebGuestLiveTests·M7_Online_·M9_Online_)은 필터에 걸려도 돌지 않는다. 작업 폴더의 commit-task·--verify-head는 가지 영수증으로 받고,
    본 폴더(main)는 그대로 full 영수증 — 합칠 때 본 폴더에서 full 한 번. 본 폴더에서 가지 층은 --branch, 작업 폴더에서 전체는 --full
  연결된 패키지 저장소(공용 UI 키트 ../uikit): 작업 트리 지문을 영수증에 함께 적는다. 그 지문을 본 full 영수증이 없으면(키트 코드가 바뀜) full.
    커밋은 키트 → 프로젝트 순서(commit-task 입력에 "repo":"com.rekkon.uikit"). 저장소가 그 자리에 없으면 종료 2
  종료 코드: 0 통과 · 1 검사 실패 · 2 사용법·환경 · 3 Unity 바쁨 · 4 영수증 없음(--verify-head)`

async function main() {
  const { values: o, positionals } = cli(USAGE, {
    fast: { type: 'boolean' }, full: { type: 'boolean' }, branch: { type: 'boolean' }, playmode: { type: 'string' }, editorassets: { type: 'string' }, files: { type: 'string', multiple: true }, since: { type: 'string' },
    'verify-head': { type: 'boolean' }, hook: { type: 'boolean' }, 'allow-test-removal': { type: 'string' },
    json: { type: 'boolean' }, 'self-test': { type: 'boolean' },
  }, { positionals: true })
  if (o.fast && o.full) usageError(USAGE, '--fast와 --full은 같이 쓰지 않는다')
  if (o.branch && (o.fast || o.full)) usageError(USAGE, '--branch는 --fast·--full과 같이 쓰지 않는다')
  for (const k of Object.keys(SELECT)) if (o[k]) {
    if (o.fast || o.full) usageError(USAGE, `--${k}는 가지 층에서만(--fast·--full과 같이 쓰지 않는다 — full은 전부 돈다)`)
    const f = unityFilter(o[k], k)
    if (f.error) usageError(USAGE, `--${k}: ${f.error}`)
  }
  if (positionals.length && !o.files) usageError(USAGE, `위치 인자는 --files 뒤에만: ${positionals.join(' ')}`)
  if (o.hook && !o['verify-head']) usageError(USAGE, '--hook은 --verify-head와 함께')
  if (o['verify-head'] && (o.fast || o.full || o.branch || o.playmode || o.editorassets || o.files || o.since || o['allow-test-removal'])) usageError(USAGE, '--verify-head는 다른 층·목록 선택과 같이 쓰지 않는다')
  if (o['self-test']) return selfTest()
  try { os.setPriority(0, BELOW) } catch { /* 권한 없으면 그대로 */ }
  try {
    if (o['verify-head']) return verifyHead(o)
    const files = o.files ? [...new Set([...o.files, ...positionals].map(f => normPath(f, ROOT)).filter(Boolean))] : null
    if (files && !files.length) usageError(USAGE, '--files가 비었다')
    return await gate(o, files)
  } catch (e) {
    if (!(e instanceof Exit)) throw e
    const tag = { 2: 'ENV', 3: 'BUSY', 4: 'MISSING' }[e.exitCode] ?? 'FAIL'
    console.log(`gate ${tag}: ${e.message}`)
    appendStatus(`gate ${tag} ${oneLine(e.message).slice(0, 160)}`)
    if (o.json) console.log(JSON.stringify({ ok: false, exit: e.exitCode, error: e.message }))
    return e.exitCode
  }
}

if (path.resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) {
  main().then(c => process.exit(c ?? 0), e => { console.log(`gate ENV: ${e?.stack ?? e}`); process.exit(2) })
}
