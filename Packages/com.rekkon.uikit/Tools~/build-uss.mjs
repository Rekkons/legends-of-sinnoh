#!/usr/bin/env node
// 토큰 원본 USS(720 화면 기준 px) 하나에서 해상도 토큰 두 벌 <이름>720.uss · <이름>1080.uss 를 만든다.
// 등급 넷 = 토큰 두 벌 × PanelSettings 정수 배율 1·2(720 · 1080 · 1440 = 720 × 2 · 2160 = 1080 × 2) — 벌을 고르는 것은 UiGrade.
//
// 사용: node Tools~/build-uss.mjs [--check] [<원본.uss> <출력 폴더>]
//       node Tools~/build-uss.mjs --self-test
//   인자가 없으면 키트 자신: Styles~/Kit.uss → Runtime/UiKit/Resources/RekkonUiKit/. 쓰는 프로젝트의 화면 USS도 같은 문법으로 만들 수 있다.
//   --check  쓰지 않고, 디스크의 생성물이 원본과 맞는지만 본다(다르면 종료 1). 쓰는 프로젝트의 관문이 부른다.
// 종료 코드: 0 통과 · 1 원본 오류 또는 --check 불일치 · 2 사용법
//
// 원본 문법(USS + 넷)
//   Npx          720 벌은 그대로, 1080 벌은 ×1.5 반올림. 0px·1px은 두 벌 모두 그대로(1px 테두리)
//   Npx!         두 벌 모두 N px
//   T(a, b)      720 벌에 a, 1080 벌에 b(안에서도 위 px 규칙)
//   @palette { 이름: 색; }   원시 색. 색 값(#hex·rgb()·rgba()·scrim())은 여기에만 적는다 — 밖에 있으면 오류
//     scrim(r, g, b, a)      어둡게 덮는 반투명 색. a는 sRGB 화면에서 고른 값 → Linear 색 공간용 1 − (1 − a)^2.2 로 바꿔 적는다
//   $이름        팔레트 색. 의미 토큰 선언(--*: $이름)에만 쓴다 — 부품 규칙은 var(--*)로 받는다
// USS는 var()를 함수 안에 못 쓰고 등급별 값이 달라야 하는 곳이 많아, 변수 대신 파일을 두 벌로 낸다(벌마다 값이 글자로 박힌다).
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const KIT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const TIERS = [720, 1080]

class SourceError extends Error {}

/** scrim 알파: sRGB에서 고른 덮기 a → Linear 섞기에서 같은 어둡기. 소수 셋째 자리. */
export const scrimAlpha = a => Math.round((1 - Math.pow(1 - a, 2.2)) * 1000) / 1000

/** 720 기준 px → 그 벌의 px. 0·1은 그대로, 나머지는 1080 벌에서 ×1.5 반올림(음수는 크기를 반올림). */
export function scalePx(n, tier) {
  if (tier === 720 || Math.abs(n) < 2) return n
  return Math.sign(n) * Math.round(Math.abs(n) * 1.5)
}

/** 팔레트 값 하나 → USS 색. */
export function paletteColor(v) {
  const s = v.trim()
  if (/^#(?:[0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$/i.test(s)) return s.toLowerCase()
  const m = s.match(/^(rgba?|scrim)\(([^()]*)\)$/)
  if (m) {
    const a = m[2].split(',').map(x => Number(x.trim()))
    const ok = a.every(Number.isFinite) && a.slice(0, 3).every(x => x >= 0 && x <= 255) && (a.length === 3 || (a[3] >= 0 && a[3] <= 1))
    if (ok && m[1] === 'scrim' && a.length === 4) return `rgba(${a[0]}, ${a[1]}, ${a[2]}, ${scrimAlpha(a[3])})`
    if (ok && m[1] === 'rgba' && a.length === 4) return `rgba(${a.join(', ')})`
    if (ok && m[1] === 'rgb' && a.length === 3) return `rgb(${a.join(', ')})`
  }
  throw new SourceError(`팔레트 색을 읽지 못함: ${s}`)
}

/** 선언 값 하나를 그 벌의 값으로: T(a, b) 고르기 → px 규칙. */
export function tierValue(value, tier) {
  let v = value.replace(/\bT\(([^()]*)\)/g, (m, inner) => {
    const parts = inner.split(',')
    if (parts.length !== 2) throw new SourceError(`T(a, b)는 값 둘: ${m}`)
    return parts[tier === 720 ? 0 : 1].trim()
  })
  if (/\bT\(/.test(v)) throw new SourceError(`T() 안에 괄호를 쓸 수 없다: ${value}`)
  return v.replace(/(-?\d+(?:\.\d+)?)px(!?)/g, (m, n, keep) => `${keep ? Number(n) : scalePx(Number(n), tier)}px`)
}

const COLOR_LITERAL = /#[0-9a-f]{3,8}\b|\b(?:rgba?|hsla?|scrim)\(/i

/** 원본 → { rules: [{ selector, decls: [[prop, value]] }], palette: Map }. 주석은 버린다. */
export function parseSource(text) {
  const src = text.replace(/\/\*[\s\S]*?\*\//g, '')
  const palette = new Map()
  const rules = []
  const used = new Set()
  const re = /([^{}]+)\{([^{}]*)\}/g
  let m, last = 0   // last = 마지막 규칙 끝(그 뒤에 남은 글자는 오류)
  while ((m = re.exec(src))) {
    last = re.lastIndex
    const selector = m[1].trim().replace(/\s+/g, ' ')
    const decls = m[2].split(';').map(d => d.trim()).filter(Boolean).map(d => {
      const i = d.indexOf(':')
      if (i < 1) throw new SourceError(`선언을 읽지 못함(${selector}): ${d}`)
      return [d.slice(0, i).trim(), d.slice(i + 1).trim().replace(/\s+/g, ' ')]
    })
    if (selector === '@palette') {
      for (const [name, value] of decls) {
        if (palette.has(name)) throw new SourceError(`팔레트 이름이 겹침: ${name}`)
        palette.set(name, paletteColor(value))
      }
      continue
    }
    if (selector.startsWith('@')) throw new SourceError(`모르는 @규칙: ${selector}`)
    const out = []
    for (const [prop, value] of decls) {
      if (COLOR_LITERAL.test(value)) throw new SourceError(`색 값은 @palette 안에만 적는다(${selector} { ${prop} }): ${value}`)
      const refs = [...value.matchAll(/\$([\w-]+)/g)].map(r => r[1])
      if (refs.length && !prop.startsWith('--')) throw new SourceError(`$이름은 의미 토큰 선언(--*)에만 쓴다(${selector} { ${prop} }): ${value}`)
      for (const r of refs) {
        if (!palette.has(r)) throw new SourceError(`팔레트에 없는 색(${selector} { ${prop} }): $${r} — @palette는 쓰는 곳보다 위에 둔다`)
        used.add(r)
      }
      out.push([prop, value.replace(/\$([\w-]+)/g, (x, r) => palette.get(r))])
    }
    rules.push({ selector, decls: out })
  }
  if (src.slice(last).trim()) throw new SourceError(`규칙 밖 글자: ${src.slice(last).trim().slice(0, 40)}`)
  const unused = [...palette.keys()].filter(k => !used.has(k))
  if (unused.length) throw new SourceError(`쓰지 않는 팔레트 색: ${unused.join(', ')}`)
  // var(--x)는 이 파일에서 선언한 토큰만(다른 원본의 토큰을 받는 화면 USS는 그 파일에 선언이 없으니 건너뛴다 — 선언이 하나도 없을 때)
  const declared = new Set(rules.flatMap(r => r.decls.filter(d => d[0].startsWith('--')).map(d => d[0])))
  if (declared.size) {
    for (const r of rules) for (const [prop, value] of r.decls) for (const v of value.matchAll(/var\((--[\w-]+)\)/g)) {
      if (!declared.has(v[1])) throw new SourceError(`선언하지 않은 토큰(${r.selector} { ${prop} }): ${v[1]}`)
    }
  }
  return { rules, palette }
}

/** 한 벌의 USS 글. */
export function buildTier(parsed, tier, sourceName) {
  const lines = [`/* 생성됨 — 손으로 고치지 않는다. 원본 ${sourceName}, 만드는 법 node Tools~/build-uss.mjs (키트 저장소). ${tier} 토큰 벌 */`]
  for (const r of parsed.rules) {
    lines.push(`${r.selector} {`)
    for (const [prop, value] of r.decls) lines.push(`    ${prop}: ${tierValue(value, tier)};`)
    lines.push('}')
  }
  return lines.join('\n') + '\n'
}

/** 원본 파일 → [{ file, text }] 두 벌. */
export function buildAll(srcFile, outDir) {
  const name = path.basename(srcFile, '.uss')
  const parsed = parseSource(readFileSync(srcFile, 'utf8'))
  return TIERS.map(tier => ({ file: path.join(outDir, `${name}${tier}.uss`), text: buildTier(parsed, tier, path.basename(srcFile)) }))
}

function selfTest() {
  const fails = []
  const eq = (name, got, want) => { if (JSON.stringify(got) !== JSON.stringify(want)) fails.push(`${name}: ${JSON.stringify(got)} ≠ ${JSON.stringify(want)}`) }
  const throws = (name, fn, part) => { try { fn(); fails.push(`${name}: 오류가 나야 한다`) } catch (e) { if (!(e instanceof SourceError) || !e.message.includes(part)) fails.push(`${name}: ${e.message}`) } }
  eq('px 720', tierValue('0 8px 3px 1px', 720), '0 8px 3px 1px')
  eq('px 1080', tierValue('0 8px 3px 1px', 1080), '0 12px 5px 1px')
  eq('px 음수', tierValue('-7px -1px', 1080), '-11px -1px')
  eq('px!', tierValue('12px! 9px', 1080), '12px 14px')
  eq('T 720', tierValue('T(12px!, 24px!)', 720), '12px')
  eq('T 1080', tierValue('T(12px!, 24px!)', 1080), '24px')
  eq('T 여러 값', tierValue('T(9px! 12px!, 16px! 20px!)', 1080), '16px 20px')
  eq('T 둘 + 토큰', tierValue('T(1px!, 2px!) T(1px!, 2px!) 0 var(--kit-x)', 1080), '2px 2px 0 var(--kit-x)')
  eq('T 안 px 규칙', tierValue('T(4px, 10px)', 1080), '15px')
  throws('T 값 셋', () => tierValue('T(1px, 2px, 3px)', 720), 'T(a, b)')
  eq('scrim 0.5', scrimAlpha(0.5), 0.782)
  eq('scrim 0·1', [scrimAlpha(0), scrimAlpha(1)], [0, 1])
  eq('팔레트 hex', paletteColor('#F8F8F8'), '#f8f8f8')
  eq('팔레트 scrim', paletteColor('scrim(16, 24, 32, 0.55)'), 'rgba(16, 24, 32, 0.827)')
  eq('팔레트 rgba', paletteColor('rgba(0,0,0,0)'), 'rgba(0, 0, 0, 0)')
  throws('팔레트 모르는 값', () => paletteColor('red'), '읽지 못함')
  const ok = '@palette { ink: #383838; dim: scrim(0, 0, 0, 0.5); }\n/* 주석 #fff */\n.r { --t: $ink; --s: $dim; }\n.a { color: var(--t); padding: 4px 1px; font-size: T(12px!, 24px!); }\n.b { background-color: var(--s); }'
  const p = parseSource(ok)
  eq('규칙 수', p.rules.length, 3)
  eq('720 벌', buildTier(p, 720, 'x.uss').split('\n').slice(1), ['.r {', '    --t: #383838;', '    --s: rgba(0, 0, 0, 0.782);', '}', '.a {', '    color: var(--t);', '    padding: 4px 1px;', '    font-size: 12px;', '}', '.b {', '    background-color: var(--s);', '}', ''])
  eq('1080 벌 한 줄', buildTier(p, 1080, 'x.uss').split('\n')[7], '    padding: 6px 1px;')
  throws('색 리터럴', () => parseSource('@palette { a: #fff; } .r { --t: $a; } .x { color: #fff; }'), '@palette 안에만')
  throws('rgba 리터럴', () => parseSource('.x { background-color: rgba(0, 0, 0, 0.5); }'), '@palette 안에만')
  throws('$를 부품에', () => parseSource('@palette { a: #fff; } .x { color: $a; }'), '의미 토큰 선언')
  throws('없는 팔레트', () => parseSource('@palette { a: #fff; } .r { --t: $a; --u: $b; }'), '팔레트에 없는 색')
  throws('안 쓰는 팔레트', () => parseSource('@palette { a: #fff; b: #000; } .r { --t: $a; }'), '쓰지 않는 팔레트')
  throws('겹친 팔레트', () => parseSource('@palette { a: #fff; a: #000; } .r { --t: $a; }'), '겹침')
  throws('없는 토큰', () => parseSource('@palette { a: #fff; } .r { --t: $a; } .x { color: var(--u); }'), '선언하지 않은 토큰')
  throws('규칙 밖 글자', () => parseSource('.x { color: var(--u); } 떠돌이'), '규칙 밖')
  eq('토큰 선언 없는 화면 USS', parseSource('.x { color: var(--kit-text); width: 10px; }').rules.length, 1)
  // 키트 자신의 원본이 읽히고 두 벌이 다르다
  const own = buildAll(path.join(KIT, 'Styles~/Kit.uss'), 'out')
  eq('키트 두 벌', own.map(o => path.basename(o.file)), ['Kit720.uss', 'Kit1080.uss'])
  if (own[0].text === own[1].text) fails.push('키트 두 벌이 같다')
  if (COLOR_LITERAL.test(own[0].text.replace(/^\s*--[\w-]+:.*$/gm, ''))) fails.push('생성물의 토큰 선언 밖에 색 값이 있다')
  if (fails.length) { for (const f of fails) console.error('FAIL ' + f); process.exit(1) }
  console.log('build-uss self-test OK')
}

function main(argv) {
  if (argv.includes('--help') || argv.includes('-h')) {
    console.log('사용: node Tools~/build-uss.mjs [--check] [<원본.uss> <출력 폴더>]\n      node Tools~/build-uss.mjs --self-test\n  인자가 없으면 Styles~/Kit.uss → Runtime/UiKit/Resources/RekkonUiKit/Kit720.uss·Kit1080.uss\n  --check: 쓰지 않고 생성물이 원본과 맞는지만 본다(다르면 종료 1)')
    return 0
  }
  if (argv.includes('--self-test')) { selfTest(); return 0 }
  const check = argv.includes('--check')
  const rest = argv.filter(a => a !== '--check')
  if (rest.some(a => a.startsWith('--')) || (rest.length !== 0 && rest.length !== 2)) { console.error('사용법 오류 — --help'); return 2 }
  const src = rest.length ? path.resolve(rest[0]) : path.join(KIT, 'Styles~/Kit.uss')
  const outDir = rest.length ? path.resolve(rest[1]) : path.join(KIT, 'Runtime/UiKit/Resources/RekkonUiKit')
  if (!existsSync(src)) { console.error(`원본 없음: ${src}`); return 2 }
  let outs
  try { outs = buildAll(src, outDir) } catch (e) {
    if (!(e instanceof SourceError)) throw e
    console.error(`build-uss: ${path.basename(src)}: ${e.message}`)
    return 1
  }
  const norm = s => s.replace(/\r\n/g, '\n')
  const stale = outs.filter(o => !existsSync(o.file) || norm(readFileSync(o.file, 'utf8')) !== o.text)
  if (check) {
    if (stale.length) { console.error(`build-uss --check: 생성물이 원본과 다르다 — ${stale.map(o => path.basename(o.file)).join(', ')} (node Tools~/build-uss.mjs 로 다시 만든다)`); return 1 }
    console.log(`build-uss --check OK ${outs.map(o => path.basename(o.file)).join(' ')}`)
    return 0
  }
  mkdirSync(outDir, { recursive: true })
  for (const o of stale) writeFileSync(o.file, o.text, 'utf8')
  console.log(`build-uss ${outs.map(o => path.basename(o.file)).join(' ')} — 바뀐 파일 ${stale.length}`)
  return 0
}

process.exitCode = main(process.argv.slice(2))
