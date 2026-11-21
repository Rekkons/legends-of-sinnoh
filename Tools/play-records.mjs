#!/usr/bin/env node
// 플레이 기록 걷기(02 §12): UGS Cloud Save 「Player Files」에 플레이어들이 올린 판 기록(rec_*)을 서비스 계정으로 내려받는다.
// --bugs: 게임 안 [버그 신고] 묶음(bug_* — 결정 132 ①, Sinnoh.Data.BugReport)만 받아 풀고 신고 하나 = 폴더 하나로 쓴다.
//
//   node Tools/play-records.mjs --out <저장소 밖 폴더> [--max-requests 900] [--dry-run]
//   node Tools/play-records.mjs --bugs --out <저장소 밖 폴더> [--max-requests 900] [--dry-run]
//   node Tools/play-records.mjs --self-test
//
// 받은 파일: <out>/<파일 키>__<플레이어 ID>.gz — 그 폴더를 SINNOH_HUMAN_SAVES로 주면 시험대(StrongAiTests.ReadHumanSaves)가 읽는다.
// --bugs: <out>/raw/<파일 키>__<플레이어 ID>.gz(받은 그대로 — 다시 돌리면 건너뛴다) + <out>/<파일 키>__<플레이어 ID>/
//   report.txt(설명 · 판 · 화면 · 어디서 · 로그 · 기록 요약) · info.json · Player.log(있으면) · record.jsonl(지금 판 기록 — 첫 줄 지운 머리, 있으면).
//
// 서비스 계정 키(git 밖 — 이 PC에만): 환경 변수 UGS_KEY_ID · UGS_SECRET_KEY · UGS_PROJECT_ID · UGS_ENVIRONMENT_ID,
//   또는 JSON 파일 { "keyId", "secretKey", "projectId", "environmentId" } — 경로는 SINNOH_UGS_KEY_FILE(기본 ~/.sinnoh/ugs-service-account.json).
//   역할: Cloud Save Viewer. 키가 없거나 자리표시 글(「여기에 …」 · <…> · your… 등)이 남아 있으면 안내만 하고 끝난다(종료 2).
//
// 관리 API(Unity 문서 oas-cloud-save-admin 1.0.0 + 2026-11-20 실제 호출로 확인 — 서비스 계정 역할 Cloud Save Viewer):
//   서버 https://services.api.unity.com · HTTP Basic(keyId:secretKey) · 초당 60 · 30분에 1,000요청(문서)
//   GET /cloud-save/v1/files/projects/{p}/environments/{e}/players → { results: [{ id, accessClasses: { default: { numFiles, totalSize } } }], links: { next } } · 쿼리 after · pageSize
//   GET …/players/{playerId}/items → { results: [{ key, size, created: { date }, modified: { date }, writeLock, contentType }], links: { next } }
//   GET …/players/{playerId}/items/{key} → { signedUrl(storage.googleapis.com), httpMethod: "GET", requiredHeaders: null } (파일 내용이 아니라 서명 URL — Basic 인증을 붙이지 않는다)
//   links.next = /cloud-save가 빠진 경로 「/v1/files/…?after=<마지막 키>」 · 마지막 쪽 null(nextUrl). 여러 플레이어 쪽 넘김은 플레이어 하나뿐이라 실제로는 보지 못했다(같은 꼴로 읽는다).
import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from 'node:fs'
import { gunzipSync, gzipSync } from 'node:zlib'
import { homedir } from 'node:os'
import { dirname, join, relative, resolve, isAbsolute } from 'node:path'
import { fileURLToPath } from 'node:url'

const HOST = 'https://services.api.unity.com'
const PREFIX = 'rec_'   // Sinnoh.Data.PlayRecord.Prefix
const BUG_PREFIX = 'bug_'   // Sinnoh.Data.BugReport.Prefix
const REPO = resolve(dirname(fileURLToPath(import.meta.url)), '..')

const HELP = `플레이 기록 걷기 — UGS Cloud Save Player Files의 판 기록(rec_*)을 내려받는다(02 §12)
  node Tools/play-records.mjs --out <저장소 밖 폴더> [--max-requests 900] [--dry-run]
  node Tools/play-records.mjs --bugs --out <저장소 밖 폴더> [--max-requests 900] [--dry-run]   버그 신고(bug_*)만 — 신고마다 폴더(report.txt · Player.log · record.jsonl)
  node Tools/play-records.mjs --self-test
키: 환경 변수 UGS_KEY_ID · UGS_SECRET_KEY · UGS_PROJECT_ID · UGS_ENVIRONMENT_ID
    또는 JSON 파일(SINNOH_UGS_KEY_FILE, 기본 ~/.sinnoh/ugs-service-account.json): { "keyId", "secretKey", "projectId", "environmentId" }
받은 폴더는 SINNOH_HUMAN_SAVES로 시험대에 넣는다. 이미 받은 파일(같은 이름 · 같은 크기)은 건너뛴다.
종료 코드: 0 받음 · 1 요청 실패 · 2 사용법 · 키 없음`

const NO_KEY = `서비스 계정 키가 없다 — 아무것도 내려받지 않았다.
  1) Unity Cloud 대시보드 → Administration → Service Accounts에서 계정을 만들고 프로젝트 역할 「Cloud Save Viewer」를 준다
  2) 키(Key ID · Secret key)를 만들어 이 PC에만 둔다(git에 넣지 않는다):
     환경 변수 UGS_KEY_ID · UGS_SECRET_KEY · UGS_PROJECT_ID · UGS_ENVIRONMENT_ID
     또는 파일 ~/.sinnoh/ugs-service-account.json = { "keyId": "…", "secretKey": "…", "projectId": "…", "environmentId": "…" }
  3) node Tools/play-records.mjs --out <저장소 밖 폴더>   (버그 신고는 --bugs)
  파일이 있어도 「여기에 …」 같은 자리표시 글이 남은 칸이 있으면 없는 것으로 본다.`

/** 자리표시 글인가: 빈 값 · 보이는 ASCII 밖 글자(「여기에 Key ID」 · 빈칸) · <…> · your… · ... */
export const placeholder = v => {
  if (typeof v !== 'string') return true
  const t = v.trim()
  return !t || /[^\x21-\x7e]/.test(t) || /^<.*>$/.test(t) || /^your[-_ ]/i.test(t) || t.includes('...')
}

/** 키: 환경 변수가 먼저, 없으면 파일. 네 칸이 다 있고 자리표시 글이 아니어야 한다 — 아니면 null. */
export function loadKey(env, readFile) {
  let k = { keyId: env.UGS_KEY_ID, secretKey: env.UGS_SECRET_KEY, projectId: env.UGS_PROJECT_ID, environmentId: env.UGS_ENVIRONMENT_ID }
  if (!k.keyId || !k.secretKey) {
    const text = readFile(env.SINNOH_UGS_KEY_FILE || join(homedir(), '.sinnoh', 'ugs-service-account.json'))
    if (text == null) return null
    try { k = { ...JSON.parse(text), ...Object.fromEntries(Object.entries(k).filter(([, v]) => v)) } } catch { return null }
  }
  return ['keyId', 'secretKey', 'projectId', 'environmentId'].some(f => placeholder(k[f])) ? null : k
}

/** 버그 신고 묶음(gzip JSON { Info, Log, Record } — Sinnoh.Data.BugReport.Pack)을 푼다. 깨졌으면 던진다. */
export function unpackBug(bytes) {
  const body = JSON.parse(gunzipSync(bytes).toString('utf8'))
  if (!body || typeof body !== 'object' || !body.Info) throw new Error('버그 신고 묶음에 Info가 없다')
  return { info: body.Info, log: body.Log ?? null, record: body.Record ?? null }
}

const ROLE = { title: '타이틀', solo: '혼자 하기', tutorial: '튜토리얼', host: '방장', guest: '손님' }
const RECORD = { none: '없음(판 없음 · 손님 · 저장하지 않는 판)', busy: '못 읽음(쓰는 중이었다)', 'too-big': '뺌(묶음 상한)' }

/** 사람이 읽는 요약(report.txt). */
export function bugReportText(key, playerId, { info, log, record }) {
  const when = Number.isFinite(info.SentAtMs) && info.SentAtMs > 0 ? new Date(info.SentAtMs).toISOString() : '?'
  const rec = record != null ? `${info.RecordLines}줄(record.jsonl)` : RECORD[info.RecordNote] ?? (info.RecordNote || '없음')
  return [
    `버그 신고 ${key}`,
    `플레이어 ${playerId} · 보낸 때 ${when}(UTC)`,
    '',
    '── 설명 ──',
    info.Description ?? '',
    '',
    '── 판 ──',
    `게임 ${info.GameVersion} · 규칙 판 ${info.RulesVersion} · 프로토콜 판 ${info.ProtocolVersion} · 언어 ${info.Lang} · 화면 ${info.Screen} · ${info.Platform}`,
    `어디서: ${ROLE[info.Role] ?? info.Role}`,
    `로그: ${log == null ? '없음' : `Player.log 끝 ${info.LogBytes} B(원래 ${info.LogSize} B)`}`,
    `지금 판 기록: ${rec}`,
    '',
  ].join('\n')
}

/** 신고 하나를 폴더로 쓴다(write(경로, 글)). 쓴 파일 이름 목록. */
export function writeBug(folder, key, playerId, bug, write) {
  const files = { 'report.txt': bugReportText(key, playerId, bug), 'info.json': JSON.stringify(bug.info, null, 2) + '\n' }
  if (bug.log != null) files['Player.log'] = bug.log
  if (bug.record != null) files['record.jsonl'] = bug.record
  for (const [name, text] of Object.entries(files)) write(join(folder, name), text)
  return Object.keys(files)
}

/**
 * 다음 쪽 주소. 실제 응답(2026-11-20): links.next = 「/v1/files/projects/…/items?after=<마지막 키>」 — 서비스 앞마디 /cloud-save가 빠진 경로라 HOST + /cloud-save를 붙인다
 * (HOST에 그대로 붙이면 404). 마지막 쪽은 null. 절대 주소 · /cloud-save로 시작하는 경로 · after 값만 온 꼴도 받는다. 없으면 null.
 */
export function nextUrl(listUrl, body) {
  const next = body && body.links && body.links.next
  if (!next) return null
  if (/^https?:\/\//.test(next)) return next
  if (next.startsWith('/')) return HOST + (next.startsWith('/cloud-save/') ? '' : '/cloud-save') + next
  const u = new URL(listUrl)
  u.searchParams.set('after', next)
  return u.toString()
}

/** 받은 파일 이름: <키>__<플레이어 ID>.gz(글자 · 숫자 · _ · - 밖의 글자는 _로). */
export const fileName = (key, playerId) => `${key}__${playerId}`.replace(/[^A-Za-z0-9_-]/g, '_') + '.gz'

/** out이 저장소 안이면 true(기록을 git 작업 트리에 두지 않는다). */
export function insideRepo(out, repo = REPO) {
  const rel = relative(repo, resolve(out))
  return rel === '' || (!rel.startsWith('..') && !isAbsolute(rel))
}

/**
 * 걷기: 파일 있는 플레이어 → 그 플레이어의 rec_* 파일 → 서명 URL → 내용. io = { fetch, have(name, size), write(name, bytes), log }.
 * 요청 수가 maxRequests에 닿으면 멈춘다(30분에 1,000요청 한도 — 다음에 이어 받는다: 이미 받은 파일은 건너뛴다).
 */
export async function collect(key, io, { maxRequests = 900, dryRun = false, prefix = PREFIX } = {}) {
  const auth = 'Basic ' + Buffer.from(`${key.keyId}:${key.secretKey}`).toString('base64')
  const base = `${HOST}/cloud-save/v1/files/projects/${key.projectId}/environments/${key.environmentId}/players`
  const stat = { requests: 0, players: 0, files: 0, got: 0, skipped: 0, stopped: false }
  const api = async url => {
    if (stat.requests >= maxRequests) { stat.stopped = true; return null }
    stat.requests++
    const r = await io.fetch(url, { headers: { Authorization: auth } })
    if (!r.ok) throw new Error(`${r.status} ${url.replace(HOST, '')}`)
    return r.json()
  }
  for (let url = `${base}?pageSize=100`; url;) {
    const page = await api(url)
    if (!page) break
    for (const p of page.results || []) {
      stat.players++
      for (let items = `${base}/${encodeURIComponent(p.id)}/items?pageSize=100`; items;) {
        const list = await api(items)
        if (!list) break
        for (const f of list.results || []) {
          if (!f.key || !f.key.startsWith(prefix)) continue
          stat.files++
          const name = fileName(f.key, p.id)
          if (io.have(name, f.size)) { stat.skipped++; continue }
          if (dryRun) { io.log(`받을 것: ${name} (${f.size} B)`); continue }
          const link = await api(`${base}/${encodeURIComponent(p.id)}/items/${encodeURIComponent(f.key)}`)
          if (!link) break
          const r = await io.fetch(link.signedUrl, { method: link.httpMethod || 'GET', headers: link.requiredHeaders || {} })
          if (!r.ok) throw new Error(`${r.status} 서명 URL(${name})`)
          io.write(name, Buffer.from(await r.arrayBuffer()), f.key, p.id)
          stat.got++
        }
        items = stat.stopped ? null : nextUrl(items, list)
      }
      if (stat.stopped) break
    }
    url = stat.stopped ? null : nextUrl(url, page)
  }
  return stat
}

async function selfTest() {
  let fail = 0
  const eq = (name, got, want) => { if (JSON.stringify(got) !== JSON.stringify(want)) { fail++; console.error(`FAIL ${name}\n  got  ${JSON.stringify(got)}\n  want ${JSON.stringify(want)}`) } }
  const full = { keyId: 'k', secretKey: 's', projectId: 'p', environmentId: 'e' }
  eq('키: 환경 변수', loadKey({ UGS_KEY_ID: 'k', UGS_SECRET_KEY: 's', UGS_PROJECT_ID: 'p', UGS_ENVIRONMENT_ID: 'e' }, () => null), full)
  eq('키: 파일', loadKey({ SINNOH_UGS_KEY_FILE: 'x.json' }, p => (p === 'x.json' ? JSON.stringify(full) : null)), full)
  eq('키: 없음 · 칸 모자람 · 깨진 파일', [loadKey({}, () => null), loadKey({ UGS_KEY_ID: 'k', UGS_SECRET_KEY: 's' }, () => null), loadKey({}, () => '{')], [null, null, null])
  eq('다음 쪽: 실제 꼴(/cloud-save 빠진 경로) · /cloud-save 경로 · 주소 · after 값 · null · 없음', [
    nextUrl('https://h/a', { links: { next: '/v1/files/projects/p/environments/e/players/A/items?after=bug_1' } }),
    nextUrl('https://h/a', { links: { next: '/cloud-save/v1/x?after=1' } }),
    nextUrl('https://h/a?pageSize=100', { links: { next: 'https://h/b' } }), nextUrl('https://h/a?pageSize=100', { links: { next: 'cur' } }), nextUrl('https://h/a', { links: { next: null } }), nextUrl('https://h/a', {})],
  [HOST + '/cloud-save/v1/files/projects/p/environments/e/players/A/items?after=bug_1', HOST + '/cloud-save/v1/x?after=1', 'https://h/b', 'https://h/a?pageSize=100&after=cur', null, null])
  eq('파일 이름', fileName('rec_00ab_f32_r39_v0-1', 'Pl.ayer/1'), 'rec_00ab_f32_r39_v0-1__Pl_ayer_1.gz')
  eq('저장소 안 · 밖', [insideRepo(join(REPO, 'Logs', 'x')), insideRepo(REPO), insideRepo(join(REPO, '..', 'elsewhere'))], [true, true, false])

  // 가짜 서버(2026-11-20 실제 응답 꼴): 플레이어 둘(두 쪽) · 파일 셋(rec 둘 + 다른 것 하나 — A는 두 쪽, 다음 쪽 = /cloud-save 빠진 경로) · 서명 URL
  const base = `${HOST}/cloud-save/v1/files/projects/p/environments/e/players`
  const acc = n => ({ accessClasses: { default: { numFiles: n, totalSize: n * 3 } } })
  const pages = {
    [`${base}?pageSize=100`]: { results: [{ id: 'A', ...acc(2) }], links: { next: 'cur2' } },
    [`${base}?pageSize=100&after=cur2`]: { results: [{ id: 'B', ...acc(1) }], links: { next: null } },
    [`${base}/A/items?pageSize=100`]: { results: [{ key: 'rec_1', size: 3 }], links: { next: '/v1/files/projects/p/environments/e/players/A/items?after=rec_1' } },
    [`${base}/A/items?after=rec_1`]: { results: [{ key: 'other', size: 9 }], links: { next: null } },
    [`${base}/B/items?pageSize=100`]: { results: [{ key: 'rec_2', size: 4 }], links: { next: null } },
    [`${base}/A/items/rec_1`]: { signedUrl: 'https://signed/1', httpMethod: 'GET', requiredHeaders: { 'x-h': '1' } },
    [`${base}/B/items/rec_2`]: { signedUrl: 'https://signed/2', httpMethod: 'GET', requiredHeaders: null },
  }
  const seen = []
  const fake = async (url, opt) => {
    seen.push([url.replace(base, ''), opt.headers.Authorization ? 'auth' : opt.headers['x-h'] || '-'])
    if (url.startsWith('https://signed/')) return { ok: true, arrayBuffer: async () => new TextEncoder().encode('bytes' + url.slice(-1)).buffer }
    return pages[url] ? { ok: true, json: async () => pages[url] } : { ok: false, status: 404 }
  }
  const wrote = {}
  const io = { fetch: fake, have: name => name === 'rec_2__B.gz', write: (n, b) => { wrote[n] = b.toString() }, log: () => {} }
  eq('걷기: 받은 수 · 건너뜀', await collect(full, io), { requests: 6, players: 2, files: 2, got: 1, skipped: 1, stopped: false })
  eq('걷기: 쓴 파일', wrote, { 'rec_1__A.gz': 'bytes1' })
  eq('걷기: 서명 URL에는 Basic 인증 없이 requiredHeaders만', seen.filter(s => s[0].startsWith('https://signed')), [['https://signed/1', '1']])
  eq('걷기: 요청 한도에서 멈춤', (await collect(full, { ...io, have: () => false, write: () => {} }, { maxRequests: 2 })).stopped, true)
  let threw = ''
  try { await collect(full, { ...io, fetch: async () => ({ ok: false, status: 401 }) }) } catch (e) { threw = e.message }
  eq('걷기: 실패 응답은 던진다', threw.startsWith('401 '), true)

  // 자리표시 키(~/.sinnoh 견본 그대로) = 없음
  eq('키: 자리표시 글', [
    loadKey({}, () => JSON.stringify({ ...full, keyId: '여기에 Key ID' })),
    loadKey({}, () => JSON.stringify({ ...full, environmentId: '<environment id>' })),
    loadKey({}, () => JSON.stringify({ ...full, secretKey: 'your-secret' })),
    loadKey({}, () => JSON.stringify({ ...full, projectId: '...' })),
  ], [null, null, null, null])
  eq('키: 진짜 같은 값은 받는다', loadKey({}, () => JSON.stringify({ keyId: '0c1d2e3f-4a5b', secretKey: 'Zx9_-+/=', projectId: 'p1', environmentId: 'e1' })) !== null, true)

  // --bugs: bug_ 파일만 받는다 · 묶음을 풀어 폴더 하나로
  const bugKey = 'bug_1796000000000_ab12cd34_v1'
  pages[`${base}/A/items?pageSize=100`].results.push({ key: bugKey, size: 5 })
  pages[`${base}/A/items/${bugKey}`] = { signedUrl: 'https://signed/9', httpMethod: 'GET' }
  const bugWrote = {}
  const bugStat = await collect(full, { ...io, have: () => false, write: (n, b, k, pid) => { bugWrote[n] = [k, pid] } }, { prefix: BUG_PREFIX })
  eq('걷기 --bugs: bug_만', [bugStat.files, bugStat.got, bugWrote], [1, 1, { [`${bugKey}__A.gz`]: [bugKey, 'A'] }])
  const info = { Report: 1, Id: 'ab12cd34', Description: '배틀에서 멈췄다\n두 줄째', GameVersion: '0.9.1', RulesVersion: 40, ProtocolVersion: 10, Lang: 'ko', Screen: '1280x720', Platform: 'WindowsPlayer', Role: 'host', SentAtMs: 1796000000000, LogSize: 900, LogBytes: 12, RecordNote: null, RecordLines: 2 }
  const bug = unpackBug(gzipSync(Buffer.from(JSON.stringify({ Info: info, Log: 'log line\n', Record: 'head\nl1\nl2\n' }))))
  const files = {}
  eq('풀기 · 폴더', writeBug('F', bugKey, 'A', bug, (p, t) => { files[p.replace(/\\/g, '/')] = t }), ['report.txt', 'info.json', 'Player.log', 'record.jsonl'])
  const report = files['F/report.txt']
  eq('report.txt: 설명 · 판 · 어디서 · 로그 · 기록 · 때', ['배틀에서 멈췄다\n두 줄째', '규칙 판 40', '어디서: 방장', 'Player.log 끝 12 B(원래 900 B)', '지금 판 기록: 2줄(record.jsonl)', '2026-'].map(w => report.includes(w)), [true, true, true, true, true, true])
  eq('Player.log · record.jsonl 그대로', [files['F/Player.log'], files['F/record.jsonl']], ['log line\n', 'head\nl1\nl2\n'])
  const bare = unpackBug(gzipSync(Buffer.from(JSON.stringify({ Info: { ...info, Role: 'guest', RecordNote: 'none', RecordLines: 0, LogSize: 0, LogBytes: 0 } }))))
  const bareText = bugReportText(bugKey, 'A', bare)
  eq('로그 · 기록 없는 신고', [writeBug('G', bugKey, 'A', bare, () => {}), bareText.includes('지금 판 기록: 없음(판 없음'), bareText.includes('로그: 없음'), bareText.includes('어디서: 손님')], [['report.txt', 'info.json'], true, true, true])
  threw = ''
  try { unpackBug(Buffer.from([1, 2, 3])) } catch { threw = 'threw' }
  eq('깨진 묶음은 던진다', threw, 'threw')
  console.log(fail ? `play-records self-test: ${fail}건 실패` : 'play-records self-test: OK')
  return fail ? 1 : 0
}

async function main(argv) {
  if (argv.includes('--help') || argv.includes('-h')) { console.log(HELP); return 0 }
  if (argv.includes('--self-test')) return selfTest()
  const opt = name => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : undefined }
  const bugs = argv.includes('--bugs')
  const out = opt('--out')
  const max = Number(opt('--max-requests') ?? 900)
  if (!out || !Number.isInteger(max) || max < 1) { console.error(HELP); return 2 }
  if (insideRepo(out)) { console.error(`--out은 저장소 밖 폴더여야 한다: ${resolve(out)}`); return 2 }
  const key = loadKey(process.env, p => (existsSync(p) ? readFileSync(p, 'utf8') : null))
  if (!key) { console.error(NO_KEY); return 2 }
  const what = bugs ? '버그 신고' : '플레이 기록'
  const rawDir = bugs ? join(out, 'raw') : out
  mkdirSync(rawDir, { recursive: true })
  let broken = 0
  const io = {
    fetch,
    have: (name, size) => existsSync(join(rawDir, name)) && statSync(join(rawDir, name)).size === size,
    write: (name, bytes, fileKey, playerId) => {
      writeFileSync(join(rawDir, name), bytes)
      if (!bugs) return
      const folder = join(out, name.replace(/\.gz$/, ''))
      try {
        const bug = unpackBug(bytes)
        mkdirSync(folder, { recursive: true })
        writeBug(folder, fileKey, playerId, bug, (p, text) => writeFileSync(p, text, 'utf8'))
        console.log(`신고: ${folder} — ${String(bug.info.Description ?? '').split('\n')[0].slice(0, 60)}`)
      } catch (e) {
        broken++
        console.error(`풀지 못한 신고: ${name} — ${e.message}`)
      }
    },
    log: s => console.log(s),
  }
  try {
    const s = await collect(key, io, { maxRequests: max, dryRun: argv.includes('--dry-run'), prefix: bugs ? BUG_PREFIX : PREFIX })
    console.log(`${what}: 플레이어 ${s.players} · 파일 ${s.files} · 받음 ${s.got}${broken ? ` (풀지 못함 ${broken})` : ''} · 이미 있음 ${s.skipped} · 요청 ${s.requests}${s.stopped ? ' — 요청 한도에서 멈춤(30분 뒤 다시 돌리면 이어 받는다)' : ''} → ${resolve(out)}`)
    return broken ? 1 : 0
  } catch (e) {
    console.error(`${what} 걷기 실패: ${e.message}`)
    return 1
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) process.exitCode = await main(process.argv.slice(2))
