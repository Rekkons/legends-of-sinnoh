// WebGL 손님판 정적 서버(시험용 — isekai Tools/serve-web.mjs를 옮김, 02 §2-2): 호스트 게임의 WebGuestServer와 같은 응답 규칙.
//   node Tools/serve-web.mjs [--root <폴더=Builds/WebGL>] [--addr <주소=127.0.0.1 | tailscale | 0.0.0.0>] [--port <8080>]
//   기본은 이 PC에서만(127.0.0.1 — 방화벽 창이 뜨지 않는다). tailscale = Tailscale IP(100.64.0.0/10)에만, 0.0.0.0 = 모든 주소(방화벽이 물을 수 있다).
//   압축: *.gz(빌드 Build/ · 번들 StreamingAssets/content/) · gzip인 *.unityweb → Content-Encoding: gzip + 원래 확장자의 MIME(모르면 application/octet-stream).
//   파일에는 ETag(크기 + 수정 시각)·Last-Modified·Cache-Control: no-cache, 조건부 요청이 맞으면 304. GET·HEAD /alive(쿼리 무시) = 200 'ok' + no-store.
import { createServer } from 'node:http';
import { createReadStream } from 'node:fs';
import { stat, open } from 'node:fs/promises';
import { networkInterfaces } from 'node:os';
import { join, resolve, extname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { cli, usageError } from './cli.mjs';

const USAGE = 'node Tools/serve-web.mjs [--root <폴더=Builds/WebGL>] [--addr <127.0.0.1|tailscale|0.0.0.0>] [--port <8080>]';
const { values } = cli(USAGE, { root: { type: 'string' }, addr: { type: 'string' }, port: { type: 'string' } });
const REPO = resolve(fileURLToPath(new URL('..', import.meta.url)));
const ROOT = resolve(REPO, values.root ?? 'Builds/WebGL');
const PORT = Number(values.port ?? 8080);
if (!Number.isInteger(PORT) || PORT < 1 || PORT > 65535) usageError(USAGE, `포트가 틀렸다: ${values.port}`);
const ADDR = values.addr === 'tailscale' ? tailscaleIp() : values.addr ?? '127.0.0.1';
if (!ADDR) usageError(USAGE, 'Tailscale 주소(100.64.0.0/10)를 찾지 못했다');

const TYPES = {
  '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8', '.wasm': 'application/wasm', '.data': 'application/octet-stream',
  '.symbols.json': 'application/json; charset=utf-8', '.png': 'image/png', '.jpg': 'image/jpeg', '.ico': 'image/x-icon',
  '.svg': 'image/svg+xml', '.txt': 'text/plain; charset=utf-8', '.webmanifest': 'application/manifest+json',
};

function tailscaleIp() {
  for (const list of Object.values(networkInterfaces()))
    for (const a of list || []) {
      if (a.family !== 'IPv4' && a.family !== 4) continue;
      const [o1, o2] = a.address.split('.').map(Number);
      if (o1 === 100 && o2 >= 64 && o2 <= 127) return a.address;
    }
  return null;
}

async function describe(path) {
  let name = path, gzip = false;
  if (path.endsWith('.gz')) { name = path.slice(0, -3); gzip = true; }
  else if (path.endsWith('.unityweb')) { name = path.slice(0, -9); gzip = await isGzip(path); }
  const ext = name.endsWith('.symbols.json') ? '.symbols.json' : extname(name).toLowerCase();
  return { type: TYPES[ext] || 'application/octet-stream', gzip };
}

// WebFiles.ETagOf와 같은 식: "크기16진-수정시각(유닉스 ms)16진"
const etagOf = (size, mtimeMs) => `"${size.toString(16)}-${Math.floor(mtimeMs).toString(16)}"`;

// WebFiles.IsNotModified와 같다
function isNotModified(ifNoneMatch, ifModifiedSince, etag, mtimeMs) {
  if (ifNoneMatch) return ifNoneMatch.split(',').map(t => t.trim().replace(/^W\//, '')).some(t => t === '*' || t === etag);
  if (!ifModifiedSince) return false;
  const since = Date.parse(ifModifiedSince);
  return !Number.isNaN(since) && Math.floor(mtimeMs / 1000) * 1000 <= since;
}

async function isGzip(path) {
  const f = await open(path, 'r');
  try { const b = Buffer.alloc(2); const { bytesRead } = await f.read(b, 0, 2, 0); return bytesRead === 2 && b[0] === 0x1f && b[1] === 0x8b; }
  finally { await f.close(); }
}

createServer(async (req, res) => {
  if (req.method !== 'GET' && req.method !== 'HEAD') { res.writeHead(405); return res.end(); }
  if (req.url.split(/[?#]/)[0] === '/alive') {
    res.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8', 'Content-Length': 2, 'Cache-Control': 'no-store' });
    return res.end(req.method === 'HEAD' ? undefined : 'ok');
  }
  let path;
  try { path = join(ROOT, decodeURIComponent(req.url.split(/[?#]/)[0])); }
  catch { res.writeHead(400); return res.end(); }
  if (path !== ROOT && !path.startsWith(ROOT + sep)) { res.writeHead(403); return res.end(); }
  try {
    let st = await stat(path);
    if (st.isDirectory()) { path = join(path, 'index.html'); st = await stat(path); }
    const { type, gzip } = await describe(path);
    const etag = etagOf(st.size, st.mtimeMs);
    const cache = { 'ETag': etag, 'Last-Modified': st.mtime.toUTCString(), 'Cache-Control': 'no-cache' };
    if (isNotModified(req.headers['if-none-match'], req.headers['if-modified-since'], etag, st.mtimeMs)) {
      res.writeHead(304, cache);
      return res.end();
    }
    const headers = { 'Content-Type': type, 'Content-Length': st.size, ...cache };
    if (gzip) headers['Content-Encoding'] = 'gzip';
    res.writeHead(200, headers);
    if (req.method === 'HEAD') return res.end();
    createReadStream(path).pipe(res);
  } catch {
    res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
    res.end('not found');
  }
}).listen(PORT, ADDR, () => console.log(`http://${ADDR === '0.0.0.0' ? '127.0.0.1' : ADDR}:${PORT}/  (${ROOT}, 바인드 ${ADDR})`));
