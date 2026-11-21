// 설계 문서·목업 보기 + 목업 선택 제출 서버(isekai Tools/serve.mjs·Work/serve-phone.mjs 틀). PC는 localhost, 폰은 Tailscale 주소로 연다. 목업은 /mockup/
//   node Tools/serve.mjs [폴더=Docs/Design] [포트=8781]
// 127.0.0.1과 Tailscale 주소(100.64.0.0/10)에만 연다 — 같은 LAN의 다른 기기에는 열지 않는다.
// 제출 받기(README 사용자 결정 21): /api/<이름> GET = 저장된 JSON, POST = JSON 저장(64KB까지) → Docs/Design/mockup/choices/<이름>.json(이전 것은 .prev.json)
import { createServer } from 'node:http';
import { readFile, writeFile, rename, copyFile, mkdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { networkInterfaces } from 'node:os';
import { join, normalize, extname } from 'node:path';
import { cli, usageError } from './cli.mjs';

// 모르는 선택(--help 등)이 폴더 이름으로 서버를 띄우지 않게 거른다(종료 코드 2), --help는 사용법만
const USAGE = '사용: node Tools/serve.mjs [폴더=Docs/Design] [포트=8781]';
const { positionals: args } = cli(USAGE, {}, { positionals: true });
if (args.length > 2) usageError(USAGE, `남는 인자: ${args.slice(2).join(' ')}`);
const ROOT = normalize(join(process.cwd(), args[0] || 'Docs/Design'));
const CHOICES = normalize(join(process.cwd(), 'Docs/Design/mockup/choices'));
const PORT = Number(args[1]) || 8781;
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8', '.png': 'image/png', '.gif': 'image/gif', '.jpg': 'image/jpeg', '.svg': 'image/svg+xml', '.md': 'text/plain; charset=utf-8' };
const tailscale = Object.values(networkInterfaces()).flat()
  .filter(a => a && a.family === 'IPv4' && /^100\.(6[4-9]|[7-9]\d|1[01]\d|12[0-7])\./.test(a.address)).map(a => a.address);
const stamp = () => new Date().toLocaleString('sv-SE');
const json = (res, code, obj) => { res.writeHead(code, { 'content-type': TYPES['.json'], 'cache-control': 'no-store' }); res.end(JSON.stringify(obj)); };

async function api(req, res, name) {
  const target = join(CHOICES, name + '.json');
  if (req.method === 'GET') {
    let buf;
    try { buf = await readFile(target); } catch { return json(res, 200, {}); }
    res.writeHead(200, { 'content-type': TYPES['.json'], 'cache-control': 'no-store' });
    return res.end(buf);
  }
  if (req.method !== 'POST') return json(res, 405, { ok: false });
  // 덩어리(Buffer)를 모아 끝에 한 번에 UTF-8로 푼다 — 덩어리마다 풀면 경계에 걸린 한글이 깨진다. 크기는 바이트로 센다
  const chunks = []; let size = 0;
  req.on('data', d => { size += d.length; if (size > 65536) req.destroy(); else chunks.push(d); });
  req.on('end', async () => {
    let data;
    try { data = JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch { return json(res, 400, { ok: false, error: 'bad json' }); }
    if (!data || typeof data !== 'object' || Array.isArray(data)) return json(res, 400, { ok: false, error: 'not an object' });
    data.savedAt = stamp();
    try {
      await mkdir(CHOICES, { recursive: true });
      if (existsSync(target)) await copyFile(target, target.replace(/\.json$/, '.prev.json'));
      await writeFile(target + '.tmp', JSON.stringify(data, null, 2));
      await rename(target + '.tmp', target);
      console.log('[submit]', stamp(), name, req.socket.remoteAddress);
      json(res, 200, { ok: true, savedAt: data.savedAt });
    } catch (e) { json(res, 500, { ok: false, error: e.code || String(e) }); }
  });
}

const handler = async (req, res) => {
  const pathname = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  const m = pathname.match(/^\/api\/([a-z0-9-]+)$/);
  if (m) return api(req, res, m[1]);
  let rel = pathname;
  if (rel.endsWith('/')) rel += 'index.html';
  const path = normalize(join(ROOT, rel));
  if (!path.startsWith(ROOT)) { res.writeHead(403); return res.end(); }
  try { const body = await readFile(path); res.writeHead(200, { 'content-type': TYPES[extname(path)] || 'application/octet-stream', 'cache-control': 'no-store' }); res.end(body); }
  catch { res.writeHead(404); res.end('not found'); }
};
for (const host of ['127.0.0.1', ...tailscale])
  createServer(handler).listen(PORT, host, () => console.log(`http://${host === '127.0.0.1' ? 'localhost' : host}:${PORT}/  (${ROOT})`));
