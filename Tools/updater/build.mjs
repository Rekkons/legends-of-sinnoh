#!/usr/bin/env node
// 업데이트 도우미 SinnohUpdater.exe 빌드 · 자기 시험(자동 업데이트 2단계 — 바뀐 파일만 받아 바꿔 끼우기. 도우미가 하는 일은 SinnohUpdater.cs 머리).
// 빌드: Unity 에디터에 든 Roslyn(<에디터>/Data/DotNetSdk/dotnet.exe + sdk/<판>/Roslyn/bincore/csc.dll — 관문이 쓰는 dotnet과 같은 곳)으로
//   .NET Framework 4.x 창 없는 exe(-target:winexe · x64)를 만든다. 참조는 에디터에 든 Mono의 .NET Framework 4.8 참조 어셈블리
//   (MonoBleedingEdge/lib/mono/4.8-api — 이 PC의 Windows 판과 무관). 소스 = SinnohUpdater.cs + Assets/Sinnoh/Data/Ed25519.cs(링크 — 두 벌 두지 않는다)
//   + 빌드가 임시 폴더에 쓰는 Key.g.cs(서명 확인 공개 키 — 기본 ReleaseFeed.cs PublicKey). -deterministic · -pathmap이라 같은 소스 · 키 · 에디터 판이면 같은 바이트
//   (도우미를 고치지 않은 판은 게임이 다시 받지 않는다). 아이콘 Tools/release/installer/sinnoh.ico가 없으면(공개 사본) 아이콘 없이 만든다(바이트가 달라진다).
// export buildUpdater({ out, publicKey }) → { file, sha256 }: out = 출력 폴더(file = <out>/SinnohUpdater.exe), publicKey = base64 32바이트.
//
// 사용: node Tools/updater/build.mjs [--out <폴더>] [--public-key <base64>]
//       node Tools/updater/build.mjs --self-test   (관문 연기 검사가 부른다 — 임시 폴더에서 가짜 게임으로 바꿔 끼우기 · 되돌림을 실제로 돌린다. 네트워크 없음)
// 종료 코드: 0 통과 / 1 실패(컴파일 · 시험) / 2 사용법 · 환경(에디터 · Roslyn 없음)
import { spawn, spawnSync } from 'node:child_process'
import crypto from 'node:crypto'
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, rmdirSync, statSync, writeFileSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { cli } from '../cli.mjs'
import { loadSources } from '../sources.mjs'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const ROOT = path.resolve(HERE, '../..')
export const UPDATER = 'SinnohUpdater.exe'
const SOURCES = [path.join(HERE, 'SinnohUpdater.cs'), path.join(ROOT, 'Assets/Sinnoh/Data/Ed25519.cs')]
const FAKE_GAME = path.join(HERE, 'FakeGame.cs')
const MANIFEST = path.join(HERE, 'app.manifest')
const ICON = path.join(ROOT, 'Tools/release/installer/sinnoh.ico')
const FEED_CS = path.join(ROOT, 'Assets/Sinnoh/Data/ReleaseFeed.cs')
const REFS = ['mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Numerics.dll', 'System.Web.Extensions.dll']
const DEFAULT_EDITOR = 'D:/Unity/Editors/6000.6.3f1/Editor'
// RFC 8032 §7.1 시험 1의 키(pack.mjs keyFromSeed와 같은 것) — 자기 시험 · --test-key 포장만. 실제 키로 시험 견본을 만들지 않는다
export const TEST_SEED = '9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60'
export const TEST_PUBLIC = Buffer.from('d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a', 'hex').toString('base64')

const USAGE = `사용: node Tools/updater/build.mjs [--out <폴더>] [--public-key <base64>]
  업데이트 도우미 SinnohUpdater.exe를 만든다(Unity에 든 Roslyn · .NET Framework 4.x · 결정적 바이트).
  --out <폴더>          출력 폴더(기본: 임시 폴더 — 경로를 찍는다)
  --public-key <base64> 서명 확인 공개 키(기본: Assets/Sinnoh/Data/ReleaseFeed.cs PublicKey)
  --self-test           임시 폴더에서 가짜 게임으로 바꿔 끼우기 · 되돌림 · 거부를 돌린다(네트워크 없음)
  에디터: Tools/sources.json unityEditor → 환경 변수 SINNOH_UNITY_EDITOR → ${DEFAULT_EDITOR}
  종료 코드: 0 통과 / 1 실패 / 2 사용법 · 환경`

export class Exit extends Error { constructor(code, msg) { super(msg); this.code = code } }
const sha = data => crypto.createHash('sha256').update(data).digest('hex')

export function editorDir() {
  try { const e = loadSources(ROOT).unityEditor; if (e) return e } catch { /* 공개 사본에는 sources.json이 없다 */ }
  return process.env.SINNOH_UNITY_EDITOR || DEFAULT_EDITOR
}

export function feedPublicKey(file = FEED_CS) {
  const m = /const string PublicKey = "([^"]*)"/.exec(readFileSync(file, 'utf8'))
  if (!m) throw new Exit(2, 'ReleaseFeed.cs에 PublicKey 상수가 없다')
  return m[1]
}

/** 에디터의 Roslyn(가장 높은 sdk 판)과 .NET Framework 4.8 참조 폴더. 없으면 종료 2. */
export function toolchain(editor = editorDir()) {
  const data = path.join(editor, 'Data')
  const dotnet = path.join(data, 'DotNetSdk/dotnet.exe')
  const sdkDir = path.join(data, 'DotNetSdk/sdk')
  const sdks = existsSync(sdkDir) ? readdirSync(sdkDir).filter(v => existsSync(path.join(sdkDir, v, 'Roslyn/bincore/csc.dll'))) : []
  sdks.sort((a, b) => a.localeCompare(b, 'en', { numeric: true }))
  const refDir = path.join(data, 'MonoBleedingEdge/lib/mono/4.8-api')
  if (!existsSync(dotnet) || !sdks.length) throw new Exit(2, `에디터에 Roslyn이 없다: ${path.join(sdkDir, '<판>/Roslyn/bincore/csc.dll')}`)
  if (REFS.some(r => !existsSync(path.join(refDir, r)))) throw new Exit(2, `에디터에 .NET Framework 4.8 참조 어셈블리가 없다: ${refDir}`)
  return { dotnet, csc: path.join(sdkDir, sdks.at(-1), 'Roslyn/bincore/csc.dll'), refDir }
}

function run(exe, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(exe, args, { windowsHide: true, env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1', DOTNET_CLI_UI_LANGUAGE: 'en' } })
    const chunks = []
    child.stdout.on('data', d => chunks.push(d))
    child.stderr.on('data', d => chunks.push(d))
    child.on('error', reject)
    child.on('close', code => resolve({ code, out: Buffer.concat(chunks).toString('utf8') }))
  })
}

/** C# → .NET Framework winexe. pathmap = [[실제 폴더, 박을 이름]] (결정적 바이트가 폴더 자리에 기대지 않게). */
export async function compile({ out, sources, manifest = null, icon = null, pathmap = [], editor = editorDir() }) {
  const t = toolchain(editor)
  const map = [[ROOT, '/sinnoh'], ...pathmap].map(([k, v]) => `${path.resolve(k)}${path.sep}=${v}/`).join(',')
  const args = [t.csc, '-nologo', '-noconfig', '-nostdlib+', '-deterministic+', '-langversion:9', '-target:winexe', '-platform:x64', '-optimize+', '-debug-',
    '-utf8output', `-pathmap:${map}`, ...REFS.map(r => `-r:${path.join(t.refDir, r)}`),
    ...(manifest ? [`-win32manifest:${manifest}`] : []), ...(icon ? [`-win32icon:${icon}`] : []), `-out:${out}`, ...sources]
  const r = await run(t.dotnet, args)
  if (r.code !== 0 || !existsSync(out)) throw new Exit(1, `컴파일 실패(종료 ${r.code}): ${path.basename(out)}\n${r.out.split(/\r?\n/).filter(l => /error|오류/i.test(l)).slice(0, 12).join('\n') || r.out.slice(-1500)}`)
}

/** 도우미를 만든다(K4). out = 출력 폴더. 반환 { file, sha256 }. */
export async function buildUpdater({ out, publicKey = feedPublicKey(), editor = editorDir() } = {}) {
  if (!out) throw new Exit(2, 'buildUpdater: out(출력 폴더)이 없다')
  if (Buffer.from(publicKey ?? '', 'base64').length !== 32 || Buffer.from(publicKey, 'base64').toString('base64') !== publicKey) throw new Exit(2, '공개 키가 base64 32바이트가 아니다')
  mkdirSync(out, { recursive: true })
  const file = path.join(path.resolve(out), UPDATER)
  const gen = mkdtempSync(path.join(os.tmpdir(), 'sinnoh-updater-gen-'))
  try {
    const keyCs = path.join(gen, 'Key.g.cs')
    writeFileSync(keyCs, `// 빌드가 만든다(Tools/updater/build.mjs) — 서명 확인 공개 키\nnamespace Sinnoh.Updater\n{\n    static partial class Key\n    {\n        public const string PublicKey = "${publicKey}";\n    }\n}\n`)
    await compile({ out: file, sources: [...SOURCES, keyCs], manifest: MANIFEST, icon: existsSync(ICON) ? ICON : null, pathmap: [[gen, '/gen']], editor })
  } finally { rmSync(gen, { recursive: true, force: true }) }
  return { file, sha256: sha(readFileSync(file)) }
}

// ── 경로 규칙(K5) 시험 표: [경로, 안전, 지워도 됨] — 게임 InstallPathTests와 같은 줄 ──
export const PATH_TABLE = [
  ['Sinnoh.exe', true, false], ['Sinnoh_Data/Managed/a.dll', true, true], ['WebGL/StreamingAssets/content/cries.gz', true, true], ['Sinnoh_Data/{brace}.bin', true, true], ['Sinnoh_Data/글꼴.txt', true, true],
  ['', false, false], ['/a', false, false], ['a//b', false, false], ['./a', false, false], ['a/../b', false, false], ['..', false, false], ['a\\b', false, false], ['C:/a', false, false],
  ['a:b', false, false], ['CON', false, false], ['Sinnoh_Data/nul.txt', false, false], ['a.', false, false], ['a /b', false, false], ['_update/x', false, false], ['_UPDATE/x', false, false], ['a?b', false, false],
  ['Sinnoh_Data/x', true, true], ['WebGL/a/b', true, true], ['CREDITS.md', true, false], ['unins000.exe', true, false], ['notes.txt', true, false], ['Sinnoh_Data', true, false],
  ['sinnoh_data/x', true, true], ['Sinnoh_Data/COM1.log', false, false], ['lpt9', false, false], ['a\tb', false, false], [`${'a'.repeat(99)}/${'a'.repeat(99)}/${'a'.repeat(59)}`, true, false], [`${'a'.repeat(99)}/${'a'.repeat(99)}/${'a'.repeat(60)}`, false, false],
]

// ── 자기 시험 ──
const pause = ms => new Promise(r => setTimeout(r, ms))
async function until(f, ms = 10000) {
  const t = Date.now()
  while (Date.now() - t < ms) { if (f()) return true; await pause(50) }
  return f()
}

async function selfTest() {
  let fail = 0
  const eq = (name, got, want) => {
    const ok = JSON.stringify(got) === JSON.stringify(want)
    if (!ok) { fail++; console.log(`FAIL ${name}: ${JSON.stringify(got)} ≠ ${JSON.stringify(want)}`) }
  }
  const tmp = mkdtempSync(path.join(os.tmpdir(), 'sinnoh-updater-test-'))
  const live = new Set()
  const started = path.join(tmp, 'started.txt')
  process.env.FAKEGAME_LOG = started   // 가짜 게임이 켜질 때마다 인자를 적는 곳(설치 폴더 밖 — 도우미가 다시 켠 게임도 물려받는다)
  const starts = () => (existsSync(started) ? readFileSync(started, 'utf8').split('\n').filter(Boolean) : [])
  const spawnLive = (exe, args) => {
    const c = spawn(exe, args, { stdio: 'ignore', windowsHide: true })
    live.add(c)
    c.on('exit', () => live.delete(c))
    return c
  }
  const startFake = async (exe, args) => {
    const n = starts().length
    const c = spawnLive(exe, args)
    if (!(await until(() => starts().length > n))) throw new Error(`가짜 게임이 켜지지 않았다: ${path.basename(exe)}`)
    return c
  }
  const stop = async c => { if (c.exitCode === null) { c.kill(); await until(() => c.exitCode !== null, 5000) } }
  try {
    // 10. 결정적 빌드 · 크기
    const b1 = await buildUpdater({ out: path.join(tmp, 'b1'), publicKey: TEST_PUBLIC })
    const b2 = await buildUpdater({ out: path.join(tmp, 'b2'), publicKey: TEST_PUBLIC })
    eq('10 두 번 빌드한 도우미 바이트가 같다(결정적)', b1.sha256, b2.sha256)
    const real = await buildUpdater({ out: path.join(tmp, 'b3') })
    eq('기본 키(ReleaseFeed.cs)로 빌드 — 키가 바이트에 박힌다', real.sha256 !== b1.sha256, true)
    eq('도우미 크기 < 100 KB', statSync(b1.file).size < 100 * 1024, true)
    const fakeExe = path.join(tmp, 'bin', 'FakeGame.exe')
    mkdirSync(path.dirname(fakeExe), { recursive: true })
    await compile({ out: fakeExe, sources: [FAKE_GAME] })

    // K5 경로 표(도우미의 InstallPath를 직접)
    writeFileSync(path.join(tmp, 'paths.json'), JSON.stringify(PATH_TABLE.map(r => r[0])))
    const tp = spawnSync(b1.file, ['--test-paths', path.join(tmp, 'paths.json')], { encoding: 'utf8', windowsHide: true })
    const got = tp.status === 0 ? JSON.parse(tp.stdout) : []
    PATH_TABLE.forEach(([rel, safe, del], i) => eq(`K5 ${JSON.stringify(rel.length > 40 ? `${rel.length}자` : rel)}`, got[i], [safe, del]))

    // 가짜 설치: 옛 판 0.1.0 → 새 판 0.2.0
    const key = crypto.createPrivateKey({ key: Buffer.concat([Buffer.from('302e020100300506032b657004220420', 'hex'), Buffer.from(TEST_SEED, 'hex')]), format: 'der', type: 'pkcs8' })
    const fake = readFileSync(fakeExe), helper = readFileSync(b1.file)
    const OLD = {
      'Sinnoh.exe': fake, 'UnityPlayer.dll': 'old player', 'version.txt': '0.1.0\r\n', 'SinnohUpdater.exe': 'old helper', 'CREDITS.md': 'credits', 'notes.txt': 'user notes',
      'Sinnoh_Data/a.bin': 'old a', 'Sinnoh_Data/old.bin': 'only in old', 'Sinnoh_Data/sub/gone.bin': 'gone', 'WebGL/x/y.txt': 'web', 'WebGL/keep.bin': 'web old',
    }
    const NEW = {
      'Sinnoh.exe': fake, 'UnityPlayer.dll': 'new player', 'version.txt': '0.2.0\r\n', 'SinnohUpdater.exe': helper, 'CREDITS.md': 'credits',
      'Sinnoh_Data/a.bin': 'new a', 'Sinnoh_Data/c.bin': 'same twice', 'Sinnoh_Data/new/b.bin': 'same twice', 'Sinnoh_Data/{brace}.bin': 'brace', 'Sinnoh_Data/글꼴.txt': 'font', 'WebGL/keep.bin': 'web new',
    }
    const DELETE = ['Sinnoh_Data/old.bin', 'Sinnoh_Data/sub/gone.bin', 'WebGL/x/y.txt']
    const ARGS = ['-screen-fullscreen', '0', 'a b', 'q"x', 'dir with space\\', '']
    const entry = (rel, body) => ({ path: rel, size: Buffer.byteLength(body), sha256: sha(body) })
    const put = (root, rel, body) => { const f = path.join(root, rel); mkdirSync(path.dirname(f), { recursive: true }); writeFileSync(f, body) }
    const want = files => Object.fromEntries(Object.entries(files).map(([r, b]) => [r, sha(b)]).sort(([a], [b]) => (a < b ? -1 : 1)))
    const tree = root => {
      const out = {}
      const walk = rel => {
        for (const e of readdirSync(path.join(root, rel), { withFileTypes: true })) {
          const r = rel ? `${rel}/${e.name}` : e.name
          if (r === '_update') continue
          if (e.isDirectory()) walk(r)
          else out[r] = sha(readFileSync(path.join(root, r)))
        }
      }
      walk('')
      return Object.fromEntries(Object.entries(out).sort(([a], [b]) => (a < b ? -1 : 1)))
    }
    const BEFORE = want(OLD), AFTER = want({ ...NEW, 'notes.txt': OLD['notes.txt'] })
    let n = 0
    function setup({ version = '0.2.0', files = NEW, signKey = key, tamper, auto = true } = {}) {
      const inst = path.join(tmp, `inst${++n}`)
      for (const [rel, body] of Object.entries(OLD)) put(inst, rel, body)
      const upd = path.join(inst, '_update'), ver = path.join(upd, version)
      const manifest = JSON.stringify({ app: 'sinnoh', version, date: '2026-11-21', page: 'p', base: '', store: '', auto, packages: [], files: Object.entries(files).map(([r, b]) => entry(r, b)) })
      put(ver, 'latest.json', JSON.stringify({ format: 1, manifest: tamper ? tamper(manifest) : manifest, signature: crypto.sign(null, Buffer.from(manifest), signKey).toString('base64') }))
      const changed = Object.entries(files).filter(([r, b]) => !(r in OLD) || sha(OLD[r]) !== sha(b))
      for (const [, b] of changed) put(path.join(upd, 'stage'), sha(b), b)
      put(path.join(upd, 'run'), UPDATER, helper)
      const s = {
        inst, upd, ver, planFile: path.join(ver, 'plan.json'), helper: path.join(upd, 'run', UPDATER),
        plan: { format: 1, version, from: '0.1.0', pid: 0, exe: 'Sinnoh.exe', put: changed.map(([r, b]) => entry(r, b)), delete: DELETE, args: ARGS },
      }
      s.write = (change = {}) => writeFileSync(s.planFile, JSON.stringify({ ...s.plan, ...change }))
      s.write()
      return s
    }
    const helperRun = (s, extra = []) => spawnSync(s.helper, ['--install', s.inst, '--plan', s.planFile, ...extra], { stdio: 'ignore', windowsHide: true, timeout: 90000 }).status
    const result = s => (existsSync(path.join(s.upd, 'result.json')) ? JSON.parse(readFileSync(path.join(s.upd, 'result.json'), 'utf8')) : null)
    const has = (s, rel) => existsSync(path.join(s.inst, rel))
    // 끝나면 다시 켠 가짜 게임을 기다린다(다음 실행이 잠금을 기다리지 않게)
    const settle = async (before, restarted) => {
      if (restarted) eq('다시 켬', await until(() => starts().length > before), true)
      else { await pause(800); eq('다시 켜지 않음', starts().length, before) }
      await pause(150)
    }

    // 1 · 9. 바꿈 · 새 파일 · 지우기 · 같은 인자로 다시 켬 · run/에서 설치 폴더의 도우미 바꾸기
    {
      const s = setup()
      const game = await startFake(path.join(s.inst, 'Sinnoh.exe'), ['--sleep', '700'])
      s.write({ pid: game.pid })
      const before = starts().length
      eq('1 종료 코드 0', helperRun(s), 0)
      eq('1 파일 = 새 판 + 맨 위 사용자 파일(_update 밖에 아무것도 안 생김)', tree(s.inst), AFTER)
      eq('1 비게 된 폴더 지움 · Unity 폴더 자신은 남김', [has(s, 'Sinnoh_Data/sub'), has(s, 'WebGL/x'), has(s, 'WebGL')], [false, false, true])
      eq('1 result.json', result(s), { format: 1, version: '0.2.0', from: '0.1.0', ok: true, why: '' })
      eq('1 journal 닫힘', [existsSync(path.join(s.ver, 'journal.txt')), existsSync(path.join(s.ver, 'journal.done.txt'))], [false, true])
      await settle(before, true)
      eq('1 같은 인자로 다시 켬', JSON.parse(starts().at(-1)), ARGS)
      eq('9 run/에서 돌며 설치 폴더의 SinnohUpdater.exe를 바꿈', sha(readFileSync(path.join(s.inst, UPDATER))), b1.sha256)
      const log = readFileSync(path.join(s.upd, 'updater.log'), 'utf8')
      eq('1 로그(단계 · 절대 경로 없음)', [/plan ok 0\.1\.0 -> 0\.2\.0/.test(log), /applied/.test(log), /restarted game/.test(log), log.includes(tmp), log.toLowerCase().includes(os.homedir().toLowerCase())], [true, true, true, false, false])
    }

    // 2. 서명 틀림 · 다른 키 · 판이 같거나 낮음 · 손으로 설치하는 판 → 2, 파일 그대로
    for (const [name, opt, why] of [
      ['서명 틀림(서명 뒤 manifest를 고침)', { tamper: t => t.replace('"date":"2026-11-21"', '"date":"2026-11-22"') }, 'signature'],
      ['다른 키', { signKey: crypto.generateKeyPairSync('ed25519').privateKey }, 'signature'],
      ['같은 판', { version: '0.1.0' }, 'version'],
      ['낮은 판', { version: '0.0.9' }, 'version'],
      ['auto 거짓', { auto: false }, 'manual'],
    ]) {
      const s = setup(opt)
      const before = starts().length
      eq(`2 ${name} → 2`, helperRun(s), 2)
      eq(`2 ${name} — 파일 그대로`, tree(s.inst), BEFORE)
      eq(`2 ${name} — result`, [result(s)?.ok, result(s)?.why], [false, why])
      await settle(before, true)
    }

    // 3. 경로 규칙 거부(plan put에) · manifest에 없는 put · manifest에 있는 delete · 맨 위 delete · manifest 안의 나쁜 경로 · 대소문자 중복 → 2
    {
      const s = setup()
      const bad = PATH_TABLE.filter(r => !r[1]).map(r => r[0])
      const codes = []
      for (const rel of bad) {
        s.write({ put: [{ path: rel, size: 1, sha256: sha('x') }] })
        const before = starts().length
        codes.push(helperRun(s))
        await settle(before, true)
      }
      eq('3 K5 거부 줄마다 → 2', codes, bad.map(() => 2))
      const cases = [
        ['manifest에 없는 put', { put: [entry('Sinnoh_Data/zzz.bin', 'zzz')] }],
        ['크기가 다른 put', { put: [{ ...entry('Sinnoh_Data/a.bin', 'new a'), size: 99 }] }],
        ['manifest에 있는 delete', { delete: ['Sinnoh_Data/a.bin'] }],
        ['대소문자만 다른 manifest 경로 delete', { delete: ['sinnoh_data/A.BIN'] }],
        ['맨 위 delete', { delete: ['notes.txt'] }],
        ['Unity 폴더 자신 delete', { delete: ['WebGL'] }],
        ['다른 판 폴더의 plan', null],
      ]
      for (const [name, change] of cases) {
        const before = starts().length
        if (change) s.write(change)
        let code
        if (change) code = helperRun(s)
        else {
          const other = path.join(s.upd, '0.3.0', 'plan.json')
          put(path.dirname(other), 'plan.json', JSON.stringify(s.plan))
          code = spawnSync(s.helper, ['--install', s.inst, '--plan', other], { stdio: 'ignore', windowsHide: true }).status
        }
        eq(`3 ${name} → 2`, code, 2)
        await settle(before, true)
      }
      eq('3 파일 그대로', tree(s.inst), BEFORE)
      for (const [name, files] of [['manifest 안의 나쁜 경로', { ...NEW, 'a\\b': 'x' }], ['manifest 대소문자 중복', { ...NEW, 'Sinnoh_Data/A.bin': 'x' }]]) {
        const t = setup({ files })
        const before = starts().length
        eq(`3 ${name} → 2`, [helperRun(t), result(t)?.why], [2, 'path'])
        eq(`3 ${name} — 파일 그대로`, tree(t.inst), BEFORE)
        await settle(before, true)
      }
      // 인자 · 설치 폴더가 틀리면 아무것도 하지 않는다(로그 · 다시 켜기도 없음)
      const before = starts().length
      eq('인자 틀림 → 2', [spawnSync(s.helper, [], { windowsHide: true }).status, spawnSync(s.helper, ['--install', path.join(tmp, 'none'), '--plan', s.planFile], { windowsHide: true }).status, spawnSync(s.helper, ['--install', s.inst, '--plan', s.planFile, '--wait', 'x'], { windowsHide: true }).status], [2, 2, 2])
      await settle(before, false)
    }

    // 4. 대기 파일 지문 틀림(받은 뒤 바뀜 — 크기 같음) → 2
    {
      const s = setup()
      writeFileSync(path.join(s.upd, 'stage', sha('new a')), 'new b')
      const before = starts().length
      eq('4 대기 파일 지문 틀림 → 2', [helperRun(s), result(s)?.why], [2, 'stage'])
      eq('4 파일 그대로', tree(s.inst), BEFORE)
      await settle(before, true)
    }

    // 5. 가운데에서 실패(뒤쪽 대상 WebGL/keep.bin을 다른 프로세스가 독점으로 쥠 — 앞 대상들은 이미 바뀐 뒤) → 1, 모두 원래대로, 옛 게임 다시 켬
    {
      const s = setup()
      const holder = await startFake(fakeExe, ['--hold', path.join(s.inst, 'WebGL', 'keep.bin'), '--sleep', '30000'])
      const before = starts().length
      eq('5 가운데 실패 → 1', helperRun(s), 1)
      await stop(holder)
      eq('5 모든 파일 원래대로', tree(s.inst), BEFORE)
      eq('5 지운 폴더도 원래대로', has(s, 'Sinnoh_Data/sub/gone.bin'), true)
      eq('5 result', [result(s)?.ok, result(s)?.why], [false, 'apply'])
      eq('5 대기 파일이 제자리(다시 할 수 있게)', s.plan.put.every(p => existsSync(path.join(s.upd, 'stage', p.sha256))), true)
      eq('5 journal 닫힘(되돌림 끝)', [existsSync(path.join(s.ver, 'journal.txt')), existsSync(path.join(s.ver, 'journal.undone.txt'))], [false, true])
      await settle(before, true)
      eq('5 옛 게임을 같은 인자로 다시 켬', JSON.parse(starts().at(-1)), ARGS)
    }

    // 6. 게임이 안 끝남(--wait 2000) → 3, 그대로, 다시 켜지 않음
    {
      const s = setup()
      const game = await startFake(path.join(s.inst, 'Sinnoh.exe'), ['--sleep', '30000'])
      s.write({ pid: game.pid })
      const before = starts().length
      eq('6 게임이 안 끝남 → 3', helperRun(s, ['--wait', '2000']), 3)
      eq('6 파일 그대로', tree(s.inst), BEFORE)
      await settle(before, false)
      eq('6 result', [result(s)?.ok, result(s)?.why], [false, 'wait'])
      await stop(game)
    }

    // 7. 정션(mklink /J — 관리자 불필요)으로 설치 밖을 가리키는 폴더 → 2, 밖은 그대로
    {
      const s = setup({ files: { ...NEW, 'Sinnoh_Data/link/x.bin': 'via link' } })
      const outside = path.join(tmp, 'outside'), link = path.join(s.inst, 'Sinnoh_Data', 'link')
      mkdirSync(outside)
      const mk = spawnSync('cmd.exe', ['/d', '/c', 'mklink', '/J', link, outside], { encoding: 'utf8', windowsHide: true })
      eq('7 정션 만듦', mk.status, 0)
      const before = starts().length
      eq('7 정션 아래 put → 2', [helperRun(s), result(s)?.why], [2, 'path'])
      eq('7 설치 밖 그대로', readdirSync(outside), [])
      await settle(before, true)
      rmdirSync(link)
      eq('7 파일 그대로', tree(s.inst), BEFORE)
    }

    // 8. 반쯤 남은 journal(도우미를 가운데에서 죽인 꼴) → 다음 실행이 먼저 되돌림, 그다음 실행은 바꿈
    {
      const s = setup()
      const holder = await startFake(fakeExe, ['--hold', path.join(s.inst, 'WebGL', 'keep.bin'), '--sleep', '30000'])
      const before = starts().length
      const h = spawnLive(s.helper, ['--install', s.inst, '--plan', s.planFile])
      const journal = path.join(s.ver, 'journal.txt')
      eq('8 도우미가 뒤쪽 파일에서 막힘', await until(() => existsSync(journal) && readFileSync(journal, 'utf8').includes('B\tWebGL/keep.bin')), true)
      await stop(h)
      await stop(holder)
      eq('8 죽인 뒤 섞인 설치(a.bin은 새것)', [tree(s.inst)['Sinnoh_Data/a.bin'] === sha('new a'), existsSync(journal)], [true, true])
      eq('8 죽은 도우미는 다시 켜지 않음', starts().length, before)
      const vsha = sha(NEW['version.txt']), vfile = path.join(s.upd, 'stage', vsha)
      writeFileSync(vfile, '0.2.1\r\n')
      eq('8 다음 실행: 먼저 되돌린 뒤 대기 파일 틀림으로 거부 → 2', [helperRun(s), result(s)?.why], [2, 'stage'])
      eq('8 되돌림으로 모든 파일 원래대로', tree(s.inst), BEFORE)
      eq('8 journal 닫힘', [existsSync(journal), existsSync(path.join(s.ver, 'journal.undone.txt'))], [false, true])
      await settle(before, true)
      writeFileSync(vfile, NEW['version.txt'])
      const again = starts().length
      eq('8 그다음 실행은 바꿈 → 0', helperRun(s), 0)
      eq('8 파일 = 새 판', tree(s.inst), AFTER)
      await settle(again, true)
    }
  } catch (e) {
    fail++
    console.log(`FAIL 예외: ${e instanceof Exit ? e.message : e.stack}`)
  } finally {
    for (const c of live) c.kill()
    await until(() => live.size === 0, 5000)
    await pause(300)
    rmSync(tmp, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 })
  }
  console.log(fail ? `updater self-test: ${fail}건 실패` : 'updater self-test: OK')
  return fail ? 1 : 0
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const { values } = cli(USAGE, { out: { type: 'string' }, 'public-key': { type: 'string' }, 'self-test': { type: 'boolean' } })
  try {
    if (values['self-test']) process.exit(await selfTest())
    const out = values.out ?? mkdtempSync(path.join(os.tmpdir(), 'sinnoh-updater-'))
    const r = await buildUpdater({ out, ...(values['public-key'] ? { publicKey: values['public-key'] } : {}) })
    console.log(`${r.file}  ${statSync(r.file).size} B  sha256 ${r.sha256}`)
  } catch (e) {
    console.error(e.message)
    process.exit(e instanceof Exit ? e.code : 1)
  }
}
