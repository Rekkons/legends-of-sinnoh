// 시험 실행 메뉴(Sinnoh/Test/Run, Assets/Sinnoh/Editor/TestRunMenu.cs)의 결과를 기다려 요약만 찍는다(isekai Tools/test-wait.mjs에서 옮김).
//   1) Temp/test-request.json을 쓴다  예: {"filters":["A1_"],"assemblies":["Sinnoh.Tests.EditMode"]}  (빈 {}면 EditMode 전체)
//   2) MCP menu로 Sinnoh/Test/Run을 부른다
//   3) node Tools/test-wait.mjs [--timeout 1500]   (초, 기본 1500)
// 요청 파일의 수정 시각(ms)을 그 요청의 표로 삼아, Temp/test-summary.json(또는 거절 Temp/test-rejected.json)의 requestMs가
// 그 값이 될 때까지 기다린다. 결과는 요약 한 줄 + 실패 목록만.
// 종료 코드: 0 통과, 1 시험 실패(또는 돈 시험 0개), 2 busy·요청 오류·실행 오류·시간 초과.
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const REQUEST = path.join(ROOT, 'Temp/test-request.json')
const SUMMARY = path.join(ROOT, 'Temp/test-summary.json')
const REJECTED = path.join(ROOT, 'Temp/test-rejected.json')

const argv = process.argv.slice(2)
let timeoutSec = 1500
for (let i = 0; i < argv.length; i++) {
  if (argv[i] === '--help' || argv[i] === '-h') { console.log('쓰는 법: node Tools/test-wait.mjs [--timeout 초]'); process.exit(0) }
  else if (argv[i] === '--timeout' && i + 1 < argv.length) timeoutSec = Number(argv[++i])
  else { console.error(`모르는 인자 ${argv[i]} — 쓰는 법: node Tools/test-wait.mjs [--timeout 초]`); process.exit(2) }
}
if (!(timeoutSec > 0)) { console.error('--timeout은 양수(초)'); process.exit(2) }
if (!fs.existsSync(REQUEST)) { console.error(`요청 파일이 없다: ${REQUEST}`); process.exit(2) }

const want = Math.floor(fs.statSync(REQUEST).mtimeMs)
const same = ms => typeof ms === 'number' && Math.abs(ms - want) <= 1

function readJson(file) {
  try { return JSON.parse(fs.readFileSync(file, 'utf8')) } catch { return null }   // 없거나 쓰는 중이면 다음 차례에
}

const oneLine = s => String(s ?? '').replace(/\s*\r?\n\s*/g, ' / ').trim()

function report(s) {
  const where = `${s.head ?? '?'}${s.dirty ? '(+미커밋)' : ''}`
  console.log(`${s.mode} ${s.passed}/${s.total} 통과 · 실패 ${s.failed} · 건너뜀 ${s.skipped} · 미결 ${s.inconclusive} · ${s.durationSec}초 · ${where} · ${s.at}`)
  if (s.error) console.log(`오류: ${oneLine(s.error)}`)
  for (const t of s.failedTests || []) {
    console.log(`- ${t.name}`)
    if (t.message) console.log(`    ${oneLine(t.message)}`)
    for (const l of String(t.stack || '').split('\n').filter(Boolean)) console.log(`    ${l}`)
  }
  if (s.error) process.exit(2)
  if (s.failed > 0) process.exit(1)
  if (!s.total) { console.log('돈 시험이 0개다 — 필터를 확인한다'); process.exit(1) }
  process.exit(0)
}

const deadline = Date.now() + timeoutSec * 1000
for (;;) {
  const s = readJson(SUMMARY)
  if (s && same(s.requestMs)) report(s)
  const r = readJson(REJECTED)
  if (r && same(r.requestMs)) { console.log(`${r.status}: ${oneLine(r.message)} (${r.at})`); process.exit(2) }
  if (Date.now() > deadline) {
    console.log(`시간 초과 ${timeoutSec}초 — 요청(${new Date(want).toISOString()})의 결과가 Temp/test-summary.json에 오지 않았다(메뉴를 불렀나, 에디터가 멈췄나)`)
    process.exit(2)
  }
  await new Promise(res => setTimeout(res, 2000))
}
