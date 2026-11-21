// 마우스 조작 확인(plan §4 「마우스 조작 확인」, 04 §9-9): Win64 플레이어를 -sinnohMouseProbe로 띄워 가상 마우스·키보드로 흐름을 끝까지 몬다(Sinnoh.Client.MouseProbe).
//   node Tools/window-probe.mjs [--out <폴더>] [--sizes 1280x720,1920x1080,1600x900] [--exe <Sinnoh.exe>] [--timeout <초>] [--json]
//       실제 창: 숨은 데스크톱(Win32 CreateDesktop — 사용자 화면에 보이지 않는 별도 데스크톱)에 창 모드로 띄운다. 백버퍼를 찍고 창 크기를 차례로 바꾼다(Screen.SetResolution).
//       이어서 타이틀 「설정」의 「화면 크기」를 가상 마우스로 눌러 창 크기·전체 화면을 바꾼다(결정 59 — 창 크기는 지정 해상도만). 여기서 더 보는 것 둘:
//       창 스타일에 크기 조절 테두리(WS_THICKFRAME)가 없다(테두리를 끌 수 없다) · 엔진이 저장한 창 크기(레지스트리 Screenmanager 값)가 마지막에 고른 값이다(다음 실행에도 남는다).
//   node Tools/window-probe.mjs --batch [--out <폴더>] …      창 없는 -batchmode 플레이어(같은 흐름 — 창 겹침·창 크기 바꾸기만 빠진다. 숨은 데스크톱을 쓰지 않는다)
//   node Tools/window-probe.mjs --canary                      숨은 데스크톱만 확인: 무해한 창(PowerShell WinForms 빈 창 4초)을 띄워 사용자 데스크톱에 창이 생기지 않는지 본다
// 실제 창 모드는 늘 --canary를 먼저 돌리고, 그것이 통과해야 플레이어를 띄운다. 사용자 데스크톱에는 창을 띄우지 않는다(대체 경로 없음).
// 판정(Tools/lib/hidden-desktop.cs가 250ms마다 잰다): 띄운 프로세스 무리의 창이 사용자 데스크톱에 0 · 포그라운드 창의 주인이 그 무리였던 적 0 · 입력 데스크톱 그대로 · 끝난 뒤 남은 프로세스 0 · 데스크톱 닫힘.
//   포그라운드 창이 전후로 같은지도 적지만(foregroundSame) 사용자가 그 사이 창을 바꾸면 달라지므로 판정에는 쓰지 않는다. 사용자 창의 제목은 읽지 않는다.
// 플레이어는 한 번에 하나만(이미 떠 있으면 종료 2). 플레이어가 고쳐 쓰는 창 크기·전체 화면 값(레지스트리 HKCU\Software\<회사>\<제품>)은 앞뒤로 그대로 되돌린다.
// 출력: <폴더>/mouse-probe.json · mouse-*.png · player.log(플레이어가 쓴다) + <폴더>/window-probe.json(데스크톱 판정). 기본 폴더 Logs/UiShots/mouse-probe/<window|batch>.
// 종료 코드: 0 통과 · 1 확인 실패(플레이어의 fails, 또는 사용자 데스크톱에 영향) · 2 사용법·환경(Windows 아님·빌드 없음·플레이어가 이미 떠 있음).
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync, rmSync, mkdtempSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import path from 'node:path';
import { cli, usageError } from './cli.mjs';
import { clearMade, recordMade, foreignNote } from './lib/made.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const USAGE = `node Tools/window-probe.mjs [--batch | --canary] [--out <폴더>] [--sizes <폭>x<높이>[,…]] [--exe <Sinnoh.exe>] [--timeout <초>] [--json]
  기본: 숨은 데스크톱의 실제 창에서 마우스 조작 확인(먼저 --canary) · --batch: 창 없는 -batchmode · --canary: 숨은 데스크톱이 사용자 화면에 창을 내지 않는지만
  종료 코드: 0 통과 · 1 확인 실패 · 2 사용법·환경`;
const { values } = cli(USAGE, {
  batch: { type: 'boolean' }, canary: { type: 'boolean' }, json: { type: 'boolean' },
  out: { type: 'string' }, sizes: { type: 'string' }, exe: { type: 'string' }, timeout: { type: 'string' },
});
if (values.batch && values.canary) usageError(USAGE, '--batch와 --canary는 함께 쓰지 못한다');
const sizes = (values.sizes ?? '1280x720,1920x1080,1600x900').split(',');
if (!sizes.every(s => /^[1-9]\d{2,4}x[1-9]\d{2,4}$/.test(s))) usageError(USAGE, `--sizes는 <폭>x<높이>[,…]: ${values.sizes}`);
const timeoutSec = Number(values.timeout ?? 360);
if (!(timeoutSec > 0)) usageError(USAGE, '--timeout은 양수(초)');
if (process.platform !== 'win32') { console.error('window-probe: Windows에서만 돈다'); process.exit(2); }

const EXE = path.resolve(ROOT, values.exe ?? 'Builds/Win64/Sinnoh.exe');
const OUT = path.resolve(ROOT, values.out ?? `Logs/UiShots/mouse-probe/${values.batch ? 'batch' : 'window'}`);
const HELPER = path.join(ROOT, 'Tools/lib/hidden-desktop.cs');

/** 숨은 데스크톱에서 명령 하나를 돌리고 판정 JSON을 받는다(PowerShell Add-Type P/Invoke — 스크립트 파일을 돌리지 않아 실행 정책과 무관하다). */
function onHiddenDesktop(commandLine, seconds) {
  const ps = `[Console]::OutputEncoding = [Text.Encoding]::UTF8; Add-Type -TypeDefinition (Get-Content -Raw -LiteralPath $env:SINNOH_HD_CS); [HiddenDesk]::Run($env:SINNOH_HD_DESKTOP, $env:SINNOH_HD_CMD, $env:SINNOH_HD_CWD, [int]$env:SINNOH_HD_TIMEOUT_MS, 250)`;
  const r = spawnSync('powershell', ['-NoProfile', '-NonInteractive', '-Command', ps], {
    encoding: 'utf8', windowsHide: true, timeout: (seconds + 60) * 1000,
    env: { ...process.env, SINNOH_HD_CS: HELPER, SINNOH_HD_DESKTOP: `sinnoh-probe-${process.pid}`, SINNOH_HD_CMD: commandLine, SINNOH_HD_CWD: os.tmpdir(), SINNOH_HD_TIMEOUT_MS: String(seconds * 1000) },
  });
  const line = (r.stdout ?? '').trim().split(/\r?\n/).pop() ?? '';
  try { return JSON.parse(line); } catch { return { ok: false, error: `판정을 읽지 못했다: ${(r.stderr || r.stdout || String(r.error ?? '')).trim().slice(0, 400)}` }; }
}

/** 사용자 화면에 영향이 없었나: 우리 창이 사용자 데스크톱에 0 · 포그라운드 주인이 우리였던 적 0 · 입력 데스크톱 그대로 · 남은 프로세스 0 · 데스크톱 닫힘. */
function untouched(d) {
  const why = [];
  if (!d.ok) why.push(d.error ?? '숨은 데스크톱 실행 실패');
  else {
    if (d.callerDesktop === d.desktop) why.push('숨은 데스크톱이 호출한 데스크톱과 같다');
    if (d.oursOnUserDesktopMax !== 0) why.push(`사용자 데스크톱에 띄운 프로세스의 창 ${d.oursOnUserDesktopMax}`);
    if (d.foregroundOursSamples !== 0) why.push(`포그라운드 창의 주인이 띄운 프로세스였던 표본 ${d.foregroundOursSamples}`);
    if (d.inputDesktopAfter !== d.inputDesktopBefore) why.push(`입력 데스크톱 ${d.inputDesktopBefore} → ${d.inputDesktopAfter}`);
    if (d.remainingProcesses !== 0) why.push(`남은 프로세스 ${d.remainingProcesses}`);
    if (!d.desktopClosed) why.push('데스크톱을 닫지 못했다');
  }
  return why;
}

function canary() {
  const cmd = `powershell.exe -NoProfile -NonInteractive -Command "Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.Form; $f.Text = 'sinnoh-probe-canary'; $f.Show(); [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Seconds 4"`;
  const d = onHiddenDesktop(cmd, 30);
  const why = untouched(d);
  if (d.ok && d.hiddenVisibleWindowsMax < 1) why.push('숨은 데스크톱에 창이 뜨지 않았다(무해한 창)');
  if (d.ok && d.timedOut) why.push('무해한 창이 끝나지 않았다');
  return { d, why };
}

// 플레이어의 창 크기·전체 화면 값(PlayerPrefs와 같은 레지스트리 키)을 앞뒤로 그대로 둔다
function registryKey() {
  const settings = readFileSync(path.join(ROOT, 'ProjectSettings/ProjectSettings.asset'), 'utf8');
  const pick = name => (settings.match(new RegExp(`^\\s*${name}:\\s*(.+?)\\s*$`, 'm')) ?? [])[1];
  return `HKCU\\Software\\${pick('companyName')}\\${pick('productName')}`;
}
/** 엔진이 저장한 창 크기·전체 화면 값(「Screenmanager …_h해시」 DWORD). 없으면 null. */
function savedScreen(key) {
  const out = spawnSync('reg', ['query', key], { encoding: 'utf8', windowsHide: true }).stdout ?? '';
  const num = name => { const m = out.match(new RegExp(`Screenmanager ${name}_h\\d+\\s+REG_DWORD\\s+0x([0-9a-f]+)`, 'i')); return m ? parseInt(m[1], 16) : null; };
  return { width: num('Resolution Width'), height: num('Resolution Height'), fullscreenMode: num('Fullscreen mode') };
}
function saveRegistry(key, dir) {
  const file = path.join(dir, 'prefs.reg');
  const had = spawnSync('reg', ['export', key, file, '/y'], { windowsHide: true }).status === 0;
  return () => {
    spawnSync('reg', ['delete', key, '/f'], { windowsHide: true });
    if (had) spawnSync('reg', ['import', file], { windowsHide: true });
  };
}

function playerRunning() {
  const r = spawnSync('tasklist', ['/FI', `IMAGENAME eq ${path.basename(EXE)}`, '/FO', 'CSV', '/NH'], { encoding: 'utf8', windowsHide: true });
  return (r.stdout ?? '').toLowerCase().includes(path.basename(EXE).toLowerCase());
}

if (values.canary) {
  const { d, why } = canary();
  if (values.json) console.log(JSON.stringify(d));
  console.log(why.length ? `window-probe canary FAIL · ${why.join(' · ')}` : `window-probe canary PASS · 숨은 데스크톱 창 ${d.hiddenVisibleWindowsMax} · 사용자 데스크톱 창 0 · 포그라운드 뺏음 0 · 남은 프로세스 0`);
  process.exit(why.length ? 1 : 0);
}

if (!existsSync(EXE)) { console.error(`window-probe: 플레이어가 없다 — ${EXE} (Sinnoh.Editor.BoardBuild.Win64)`); process.exit(2); }
if (playerRunning()) { console.error(`window-probe: ${path.basename(EXE)}가 이미 떠 있다 — 한 번에 하나만 띄운다`); process.exit(2); }

let desktop = null, why = [];
if (!values.batch) {
  const c = canary();
  if (c.why.length) { console.log(`window-probe FAIL · 숨은 데스크톱 확인(무해한 창)이 통과하지 못해 플레이어를 띄우지 않았다 · ${c.why.join(' · ')}`); process.exit(1); }
}
// 앞 실행이 만든 파일 + 늘 쓰는 결과 이름만 지운다(--out에 남의 폴더를 줘도 모르는 파일은 남는다 — Tools/lib/made.mjs)
const prior = clearMade(OUT, { own: /^(mouse-probe\.json|window-probe\.json|player\.log|mouse-[^/]*\.png)$/ });
if (prior.foreign.length) console.warn(foreignNote('window-probe', OUT, prior.foreign));
mkdirSync(OUT, { recursive: true });
const tmp = mkdtempSync(path.join(os.tmpdir(), 'sinnoh-window-probe-'));
const restore = saveRegistry(registryKey(), tmp);
// 저장 폴더는 임시 폴더(끝나면 지운다) — 확인은 사용자의 저장 폴더에 쓰지 않는다. 주면 저장 화면 단계(이어하기 · 저장 목록 · 삭제)를 지난다
const SAVES = path.join(tmp, 'saves');
const probeArgs = ['-sinnohMouseProbe', OUT, '-logFile', path.join(OUT, 'player.log')];
let exitCode = null, timedOut = false, saved = null;
const t0 = Date.now();
try {
  if (values.batch) {
    const r = spawnSync(EXE, ['-batchmode', ...probeArgs, '-sinnohSaves', SAVES], { windowsHide: true, timeout: timeoutSec * 1000 });
    exitCode = r.status;
    timedOut = r.error?.code === 'ETIMEDOUT';
  } else {
    const [w, h] = sizes[0].split('x');
    const q = s => `"${s}"`;
    const cmd = [q(EXE), '-screen-fullscreen', '0', '-screen-width', w, '-screen-height', h, probeArgs[0], q(probeArgs[1]), '-sinnohMouseSizes', sizes.join(','), probeArgs[2], q(probeArgs[3]), '-sinnohSaves', q(SAVES)].join(' ');
    desktop = onHiddenDesktop(cmd, timeoutSec);
    why = untouched(desktop);
    exitCode = desktop.exitCode ?? null;
    timedOut = !!desktop.timedOut;
    saved = savedScreen(registryKey());
  }
} finally {
  restore();
  rmSync(tmp, { recursive: true, force: true });
}
if (playerRunning()) why.push('플레이어 프로세스가 남아 있다');

let probe = null;
try { probe = JSON.parse(readFileSync(path.join(OUT, 'mouse-probe.json'), 'utf8')); } catch { /* 플레이어가 결과를 쓰지 못했다 */ }
if (timedOut) why.push(`시간 초과 ${timeoutSec}초`);
if (!probe) why.push(`mouse-probe.json이 없다(종료 코드 ${exitCode})`);
else {
  if (!probe.ok) why.push(...probe.fails);
  if (exitCode !== 0) why.push(`플레이어 종료 코드 ${exitCode}`);
  if (!values.batch && probe.mode !== 'window') why.push(`창 모드로 뜨지 않았다(${probe.mode})`);
  // 엔진이 저장한 값 = 설정 화면에서 마지막에 고른 창 크기(창 모드 = 3) — 다음 실행이 이 값으로 뜬다
  if (!values.batch && probe.settingsLast && `${saved?.width}x${saved?.height}/${saved?.fullscreenMode}` !== `${probe.settingsLast}/3`)
    why.push(`엔진이 저장한 창 크기가 설정에서 마지막에 고른 값이 아니다(저장 ${saved?.width}x${saved?.height} 모드 ${saved?.fullscreenMode} ≠ ${probe.settingsLast} 창 모드)`);
}
if (desktop?.ok) {
  // 창 테두리를 끌 수 없다(결정 59): 띄운 창 가운데 제목 줄이 있는 창을 봤고(WS_CAPTION), 어느 창에도 크기 조절 테두리(WS_THICKFRAME)가 없다
  const styles = (desktop.hiddenWindowStyles ?? []).map(s => parseInt(s, 16));
  if (!styles.some(s => (s & 0xc00000) === 0xc00000)) why.push(`제목 줄이 있는 창을 보지 못했다(창 스타일 ${(desktop.hiddenWindowStyles ?? []).join(' ')})`);
  if (styles.some(s => s & 0x40000)) why.push(`창 스타일에 크기 조절 테두리(WS_THICKFRAME)가 있다(${desktop.hiddenWindowStyles.join(' ')}) — resizableWindow가 켜져 있다`);
}
const report = { ok: why.length === 0, mode: values.batch ? 'batchmode' : 'window', sizes: values.batch ? null : sizes, saved, exitCode, seconds: Math.round((Date.now() - t0) / 100) / 10, desktop, fails: why, out: OUT };
writeFileSync(path.join(OUT, 'window-probe.json'), JSON.stringify(report, null, 1) + '\n');
recordMade(OUT, prior.foreign);
if (values.json) console.log(JSON.stringify(report));
const steps = probe?.steps?.length ?? 0;
const desk = desktop?.ok ? ` · 창 ${(desktop.hiddenClientSizes ?? []).join('→')} · 창 스타일 ${(desktop.hiddenWindowStyles ?? []).join(' ')} · 저장 ${saved?.width}x${saved?.height} · 사용자 데스크톱 창 ${desktop.oursOnUserDesktopMax} · 포그라운드 뺏음 ${desktop.foregroundOursSamples} · 남은 프로세스 ${desktop.remainingProcesses}` : '';
console.log(`window-probe ${report.mode} ${report.ok ? 'PASS' : 'FAIL'} · 단계 ${steps}${desk} · ${report.seconds}초 → ${OUT}${why.length ? `\n  ${why.join('\n  ')}` : ''}`);
process.exit(report.ok ? 0 : 1);
