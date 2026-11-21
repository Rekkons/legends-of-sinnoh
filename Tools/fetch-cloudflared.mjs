// cloudflared 받기(결정 91 — 공식 winget `Cloudflare.cloudflared`만, 02 §2-2): node Tools/fetch-cloudflared.mjs [--keep]
//   1. winget download --id Cloudflare.cloudflared -e (winget이 설치 파일 SHA256을 확인한다) → 임시 폴더의 MSI
//   2. msiexec /a(관리 설치 = 파일만 풀기 — 설치 · 관리자 권한 · 창 없음 /qn) → cloudflared.exe
//   3. Authenticode 서명 주체가 「Cloudflare, Inc.」이고 Valid인지 본 뒤 Tools/bin/cloudflared.exe로(받다 끊기면 예전 것을 그대로 둔다)
//   Tools/bin은 git에 넣지 않는다(.gitignore). 개발 중 CloudflaredTunnel.FindExe가 이 경로를 찾고, Win64 빌드(BoardBuild)가 실행 파일 옆으로 복사한다.
//   라이선스 Apache-2.0(winget show의 License). --keep = 임시 폴더를 지우지 않는다. 종료 코드 0 성공 · 1 실패 · 2 사용법.
import { mkdirSync, mkdtempSync, copyFileSync, renameSync, readdirSync, rmSync, statSync, existsSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { cli } from './cli.mjs';

const USAGE = 'node Tools/fetch-cloudflared.mjs [--keep]';
const { values } = cli(USAGE, { keep: { type: 'boolean' } });
const BIN = join(dirname(fileURLToPath(import.meta.url)), 'bin');
const work = mkdtempSync(join(tmpdir(), 'sinnoh-cloudflared-'));
const run = (exe, args) => execFileSync(exe, args, { encoding: 'utf8', windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
const find = (dir, test) => {
  for (const f of readdirSync(dir)) {
    const p = join(dir, f);
    if (statSync(p).isDirectory()) { const r = find(p, test); if (r) return r; }
    else if (test(f)) return p;
  }
  return null;
};

try {
  run('winget', ['download', '--id', 'Cloudflare.cloudflared', '-e', '--accept-source-agreements', '--accept-package-agreements', '-d', join(work, 'dl')]);
  const msi = find(join(work, 'dl'), f => f.toLowerCase().endsWith('.msi'));
  if (!msi) throw new Error('winget이 MSI를 받지 않았다');
  run('msiexec', ['/a', msi, '/qn', `TARGETDIR=${join(work, 'x')}`]);
  const exe = find(join(work, 'x'), f => f.toLowerCase() === 'cloudflared.exe');
  if (!exe) throw new Error('MSI 안에 cloudflared.exe가 없다');
  const sig = run('powershell', ['-NoProfile', '-Command', `$s = Get-AuthenticodeSignature -LiteralPath '${exe.replace(/'/g, "''")}'; "$($s.Status)|$($s.SignerCertificate.Subject)"`]).trim();
  if (!/^Valid\|CN="Cloudflare, Inc\."/.test(sig)) throw new Error(`서명이 맞지 않다: ${sig}`);
  mkdirSync(BIN, { recursive: true });
  const out = join(BIN, 'cloudflared.exe');
  copyFileSync(exe, out + '.part');
  renameSync(out + '.part', out);
  const version = run(out, ['--version']).trim();
  console.log(`${out} ${(statSync(out).size / 1048576).toFixed(1)}MB  ${version}  (서명 Cloudflare, Inc. Valid · winget Cloudflare.cloudflared)`);
} catch (e) {
  console.error(`[fetch-cloudflared] 실패: ${e.message}`);
  process.exitCode = 1;
} finally {
  if (!values.keep && existsSync(work)) rmSync(work, { recursive: true, force: true });
}
