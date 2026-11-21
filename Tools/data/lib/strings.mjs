// strings.ko·strings.en(03 §1-3·§10) + 한국어 보고서. 키 = 묶음 접두 + 데이터 키(03 §1-2):
//   ui.<묶음>.<키>        ff1ce0(한국어) / 원작 module_language_english(영어, 결정 34) — 10묶음
//   species.<dex>         종 이름: 카드 Lua pokedex_name / xlsx pokedex_name
//   pokemon.<키>          카드 이름: 카드 닉네임(갤럭시단 주인 꼬리 뺌) / xlsx internal_name(바탕 면과 같은 폼 면은 원작 영문 세이브 카드 이름)
//   move.<키>, move.<키>.text   기술 이름·효과: 카드 Lua move_name·move_effect / xlsx moves. 이름은 현행 공식 한국어 기술명으로 바꾼다(결정 43 ⑩·44 ②, lib/move-names.mjs)
//   trainer_card.<키>     직업: 원작 자산 공식 이름(결정 33, 카드명 대조표) / xlsx trainer_class
//   trainer_ability.<키>.<1|2>, …<1|2>.text   트레이너 카드 능력 이름·문구(1 = 파랑, 2 = 빨강): 한국어판 카드 문구(Research/trainer-cards.md §3 KR 줄) / xlsx trainer_cards
//   trainer_ability.<키>.<1|2>.short   해금 카드에 싣는 짧은 문구(48자 이하 — 사용자 결정 87 V11 라): authored/trainer-short-ko.json, 없으면 전체 문구(48자 이하일 때만) / authored/trainer-short-en.json, 없으면 전체 영문(48자 이하일 때만)
//                         (접두가 trainer_card.가 아닌 것은 그 묶음이 DS 화면 글자 — fonts.mjs DS_KEYS — 라서다. 능력 문구는 PC 층에만 나온다)
//   tutorial.<각본>.<단계>[.hint]   마박사 대사(M6.5): authored/tutorial-ko.json {ko, en}(lib/tutorial.mjs) — screen.*처럼 용어 규칙·검토 목록을 거치지 않는다. 말투는 해라체(07 §3-2)
//   screen.<화면>.<키>    우리 화면(PC 층) 문구: authored/screens.json {ko, en} — ui-text-guide.md를 따라 직접 쓴 말이라 용어 규칙·검토 목록(review-ko.tsv)을 거치지 않는다.
//                         영어는 M9 언어 ①에서 검토했다(ui-text-guide §6 — 원작 영문 용어, 짧게)
//   galactic.<주인>       갤럭시단 인물: 원작 자산 공식 이름 / 원작 영문
//   region.<snake>        보드 지명: authored(새로 옮김) / map_cells region
//   village.·legendary_site.·legendary_shrine.·shrine.·gate.<snake>   고정물 이름(키 = fixtures 키): 한국어판 세이브 / 원작 영문 세이브
//                         (사당 13곳은 한국어판에서도 영어로 남아 authored가 한국어를 준다)
//   item.·hidden_spot.<snake>   아이템 이름(키 = items 키): 한국어판 세이브 / 원작 영문 세이브
//   item.<snake>.text · stamp.<n>[.text] · <카드 키>[.text.<n>]   볼·스튜·배틀 아이템 효과, 스탬프 조건, 이미지 전용 카드 이름·본문: authored/items.json · authored/cards/(lib/authored.mjs, M5)
// 한국어 값(authored 제외)에는 korean.md §9 권장 용어(authored/terms-ko.json)를 적용한다(결정 14 ⑤).
// 다시 옮김(결정 163): 위 한국어 가운데 3JA2 · 로씨온 글은 authored/ko/<묶음>.json(영문에서 새로 옮긴 우리 번역)이 덮는다 — 범위 · 꼴 · 검사는 lib/retranslate.mjs.
//   범위 안에서 아직 옮기지 않은 키는 빌드 오류다(옛 원천으로 떨어지지 않는다).
import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { srcPath } from '../../sources.mjs';
import { parseLanguageModule } from './lua.mjs';
import { matchCards, countCauses } from './cards.mjs';
import { buildRoster } from './roster.mjs';
import { walkPokemonCards } from './save.mjs';
import { snakeKey, BAD_TEXT } from './cube.mjs';
import { officialMoveNames } from './move-names.mjs';
import { readRetranslation, readKeep, retranslateScope, checkRetranslation, newShort } from './retranslate.mjs';

const HANGUL = /[가-힣]/;
const SHORT_MAX = 48; // 해금 카드 능력 칸에 싣는 글자 수(CardFx.BodyMax와 같다)
const LONE_SURROGATE = /[\uD800-\uDFFF]/u; // u 모드에서는 짝 없는 대리 문자만 맞는다(JSON.stringify만 이스케이프한다)
const GALACTIC_EN = { Cyrus: 'Cyrus', Mars: 'Mars', Jupiter: 'Jupiter', Saturn: 'Saturn', FemaleGrunt: 'Team Galactic Grunt', MaleGrunt: 'Team Galactic Grunt' };

const readJson = p => JSON.parse(readFileSync(p, 'utf8'));

/** "<저장소.git>@<커밋>:<경로>" → 그 파일 내용(원작 GitHub 클론에서 git show로 읽는다) */
export function readGitSpec(root, spec) {
  const m = /^(.+\.git)@([0-9a-f]{40}):(.+)$/.exec(spec);
  if (!m) throw new Error(`git 원천 형식이 아니다: ${spec}`);
  const r = spawnSync('git', [`--git-dir=${srcPath(root, m[1])}`, 'show', `${m[2]}:${m[3]}`], { encoding: 'utf8', maxBuffer: 1 << 26 });
  if (r.status !== 0) throw new Error(`git show 실패: ${spec}: ${r.stderr}`);
  return r.stdout;
}

function readAuthored(root, file, errors) {
  const j = readJson(path.join(root, file));
  const out = new Map();
  if (j.schema !== 1) errors.push(`${file} schema ${j.schema}`);
  for (const [key, e] of Object.entries(j.strings || {})) {
    const keys = Object.keys(e).sort().join(',');
    if (keys !== 'en,note,review,src,text' || typeof e.en !== 'string' || typeof e.text !== 'string' || typeof e.review !== 'boolean'
      || typeof e.note !== 'string' || !/^(translated|rom:.+|transcribed:.+)$/.test(e.src)) errors.push(`${file} ${key}: 항목 형식`);
    out.set(key, e);
  }
  return out;
}

function readTerms(root, file, errors) {
  const j = readJson(path.join(root, file));
  if (j.schema !== 1) errors.push(`${file} schema ${j.schema}`);
  return (j.terms || []).map((t, i) => {
    if (Object.keys(t).sort().join(',') !== 're,ref,to') errors.push(`${file} terms[${i}] 형식`);
    return { re: new RegExp(t.re, 'g'), to: t.to, ref: t.ref };
  });
}

/** 트레이너 카드 직업 한국어(결정 33): 원작 자산 공식 이름이 있으면 그것, 없으면 카드 번역. 대조표 판정(status)을 따른다. */
function trainerClassKo(cc) {
  const strip = s => s.replace(/ /g, '');
  switch (cc.status) {
    case 'same': return { ko: cc.cardKo, rule: '같음' };
    case 'spacingOnly': return { ko: cc.officialKo.find(o => strip(o) === strip(cc.cardKo)), rule: '띄어쓰기만 다름 → 원작 자산' };
    case 'classPlusName': return { ko: cc.cardKo, rule: '원작 자산 직업 + 조무래기(배틀 표시와 같음) → 카드' };
    case 'notInRoms': return { ko: cc.cardKo, rule: '원작 자산에 없음 → 카드 번역' };
    case 'different': return { ko: cc.officialKo[0], rule: '다름 → 원작 자산' };
    default: return { ko: null, rule: `모르는 판정 ${cc.status}` };
  }
}

/** authored/screens.json: { schema: 1, note, strings: { "screen.…": { ko, en } } } → Map */
function readScreens(root, file, errors) {
  const j = readJson(path.join(root, file));
  const out = new Map();
  if (j.schema !== 1 || Object.keys(j).sort().join() !== 'note,schema,strings') errors.push(`${file}: 형식(schema 1, 필드 note·schema·strings)`);
  for (const [key, e] of Object.entries(j.strings || {})) {
    if (!/^screen\.[a-z0-9_]+(\.[a-z0-9_]+)+$/.test(key) || Object.keys(e).sort().join() !== 'en,ko' || typeof e.ko !== 'string' || typeof e.en !== 'string')
      errors.push(`${file} ${key}: 항목 형식(키 screen.<화면>.<키>, 값 {ko, en})`);
    out.set(key, e);
  }
  return out;
}

/**
 * Research/trainer-cards.md §3 → 영문 직업 → [{ name(영문 능력 이름), ko(한국어 이름), text(한국어 문구) } × 2(파랑 · 빨강 순)].
 * 줄 꼴: 「### <번호>. <한국어 직업> / <영문 직업>」 · 「- 파랑|빨강 **<한국어 이름>** / <영문 이름>」 · 「  - KR: <문구>」.
 */
function parseTrainerAbilities(md, file, errors) {
  const out = new Map();
  let cur = null, ability = null;
  for (const line of md.split(/\r?\n/)) {
    let m;
    if ((m = /^### \d+\. .+? \/ (.+)$/.exec(line))) { cur = []; ability = null; out.set(m[1].trim(), cur); }
    else if (/^#{1,3} /.test(line)) { cur = null; ability = null; }
    else if (cur && (m = /^- (파랑|빨강) \*\*(.+?)\*\* \/ (.+)$/.exec(line))) {
      if ((m[1] === '파랑') !== (cur.length === 0)) errors.push(`${file}: 능력 순서(파랑 → 빨강)가 아니다: ${line}`);
      ability = { name: m[3].trim(), ko: m[2].trim(), text: null };
      cur.push(ability);
    } else if (ability && (m = /^  - KR: (.+)$/.exec(line))) ability.text = m[1].trim();
  }
  return out;
}

const tsv = (head, rows) => [head, ...rows].map(r => r.join('\t')).join('\n') + '\n';

/**
 * src: Tools/sources.json data(경로는 저장소 기준), cube: buildCube 결과, board: board 행, save·saveEn: 한국어판·원작 영문 세이브,
 * extra: 세이브에서 뽑은 고정물·아이템 이름 [{key, en, ko}](lib/save.mjs) + 손으로 옮긴 카드·아이템 이름·문구 [{key, en, ko, review?, src?, note?}] · 여러 줄 카드의 검토 행 {reviewOf: 키들, label, src, note}(lib/authored.mjs).
 * → { ko: {키: 문장}, en: {키: 문장}, reports: {파일 이름: 내용}, cards: 카드 Lua 배열, byKey: pokemon 키 → 카드 Lua 객체들,
 *     rosterCsv: Research/roster.csv 내용(lib/roster.mjs), moveNamesSummary: 공식 기술명 한 줄(lib/move-names.mjs), retranslateSummary: 다시 옮김 한 줄 }
 */
export function buildStrings(root, src, cube, board, save, saveEn, extra, errors) {
  const P = p => path.join(root, p);
  const read = key => (key === 'langEn' ? readGitSpec(root, src[key]) : readFileSync(srcPath(root, src[key]), 'utf8'));

  const authored = readAuthored(root, 'Tools/data/authored/strings-ko.json', errors);
  const terms = readTerms(root, 'Tools/data/authored/terms-ko.json', errors);
  const retranslated = readRetranslation(root, errors);
  const keep = readKeep(root, errors);
  const cards = JSON.parse(read('cards'));
  const { rows: roster, csv: rosterCsv } = buildRoster(cards, readJson(P('Tools/data/authored/dex-ko.json')), errors);
  const romNames = JSON.parse(read('romNamesKo'));
  const classes = JSON.parse(read('cardClassesKo'));

  const en = {}, ko = {}; // ko: 용어 적용 전 원문
  const set = (key, enText, koText) => {
    if (key in en) errors.push(`문자열 키 겹침: ${key}`);
    en[key] = enText;
    ko[key] = koText;
  };

  // ── ui: 언어 모듈 10묶음 ──
  const uiKo = parseLanguageModule(read('langKo'), src.langKo);
  const uiEn = parseLanguageModule(read('langEn'), src.langEn);
  if (Object.keys(uiKo).join() !== Object.keys(uiEn).join()) errors.push(`ff1ce0 묶음 ${Object.keys(uiKo)} ≠ 영어 모듈 ${Object.keys(uiEn)}`);
  for (const g of Object.keys(uiEn)) {
    if (Object.keys(uiKo[g] || {}).join() !== Object.keys(uiEn[g]).join()) errors.push(`ui 묶음 ${g}의 키가 한·영 다르다`);
    for (const k of Object.keys(uiEn[g])) set(`ui.${g}.${k}`, uiEn[g][k], uiKo[g] ? uiKo[g][k] : undefined);
  }

  // ── 포켓몬·기술: 카드 Lua를 xlsx 키에 붙인다 ──
  const ownerKo = {};
  for (const who of ['Mars', 'Jupiter', 'Saturn']) {
    const t = romNames.namedTrainers.find(n => n.en === who && /COMMANDER/.test(n.class));
    if (!t) errors.push(`원작 자산 이름에 간부 ${who}가 없다`); else ownerKo[who] = t.ko;
  }
  ownerKo.Cyrus = romNames.main.Cyrus && romNames.main.Cyrus.ko;
  const { byKey, mismatches, gruntSuffixes } = matchCards(cards, cube.pokemon, cube.moves, roster, ownerKo, errors);
  if (errors.length) return null;

  for (const r of roster) if (cube.species.get(r.dex) !== r.en) errors.push(`roster ${r.dex} 영문 ${r.en} ≠ xlsx ${cube.species.get(r.dex)}`);
  if (roster.length !== cube.species.size) errors.push(`roster ${roster.length}종 ≠ xlsx ${cube.species.size}종`);
  const speciesKo = new Map(roster.map(r => [r.dex, r.ko]));
  for (const [dex, name] of cube.species) set(`species.${dex}`, name, speciesKo.get(dex));

  const one = (key, what, values) => {
    const u = [...new Set(values)];
    if (u.length !== 1) errors.push(`${key}: 카드마다 ${what}이 다르다: ${u.join(' / ')}`);
    return u[0];
  };
  const cardName = new Map(); // 키 → 카드 닉네임(갤럭시단 주인 꼬리 뺌)
  const enStateNicks = englishStateNicks(saveEn);
  for (const p of cube.pokemon)
    cardName.set(p.key, one(p.key, '닉네임', byKey.get(p.key).map(c => (p.trainer ? c.nick.replace(/ \([^()]+\)$/, '') : c.nick))));
  for (const p of cube.pokemon) {
    let name = cardName.get(p.key);
    // 닉네임에 폼 이름이 없는데 영문은 폼 이름인 행(갤럭시단 서쪽바다 트리토돈 등): 같은 영문 이름의 야생 카드 이름을 쓴다. 없으면 authored
    if (name === speciesKo.get(p.dex) && p.name !== cube.species.get(p.dex)) {
      const wild = cube.pokemon.find(q => !q.trainer && q.dex === p.dex && q.name === p.name && cardName.get(q.key) !== name);
      name = wild ? cardName.get(wild.key) : authored.has(`pokemon.${p.key}`) ? undefined : (errors.push(`pokemon.${p.key}: 한국어 폼 이름이 없다(${p.name})`), name);
    }
    set(`pokemon.${p.key}`, englishCardName(p, cube.pokemon, enStateNicks, errors), name);
  }
  const moveKo = new Map();
  for (const p of cube.pokemon) for (const c of byKey.get(p.key)) {
    if (!moveKo.has(p.signature)) moveKo.set(p.signature, []);
    moveKo.get(p.signature).push(c);
  }
  for (const m of cube.moves) {
    const list = moveKo.get(m.key);
    if (!list) { errors.push(`move.${m.key}: 이 기술을 쓰는 카드가 없다`); continue; }
    set(`move.${m.key}`, m.name, one(m.key, '기술 이름', list.map(c => c.move_name)));
    set(`move.${m.key}.text`, m.en, one(m.key, '기술 효과', list.map(c => c.move_effect)));
  }

  // ── 트레이너 카드 직업·갤럭시단 인물(결정 33) ──
  const nameRows = [];
  const classStatus = new Map();   // trainer_card 키 → 대조표 판정(다시 옮길 범위 — 원작 자산에 없는 직업)
  const ccByEn = new Map(classes.classes.map(c => [c.en, c]));
  if (classes.classes.length !== cube.trainerCards.length) errors.push(`직업 대조표 ${classes.classes.length}종 ≠ trainer_cards ${cube.trainerCards.length}`);
  for (const t of cube.trainerCards) {
    const cc = ccByEn.get(t.class);
    if (!cc) { errors.push(`직업 대조표에 ${t.class}가 없다`); continue; }
    const { ko: name, rule } = trainerClassKo(cc);
    if (!name) errors.push(`trainer_card.${t.key}: ${rule}`);
    set(`trainer_card.${t.key}`, t.class, name);
    classStatus.set(`trainer_card.${t.key}`, cc.status);
    nameRows.push([`trainer_card.${t.key}`, t.class, name, cc.cardKo, cc.officialKo.join(' / ') || '—', rule]);
  }
  if (gruntSuffixes.length !== 1) errors.push(`조무래기 카드 꼬리가 하나가 아니다: ${gruntSuffixes}`);
  for (const [who, enName] of Object.entries(GALACTIC_EN)) {
    const grunt = /Grunt$/.test(who);
    // 조무래기: 원작 자산은 직업 「갤럭시단」 + 이름 「조무래기」로 배틀에 「갤럭시단 조무래기」라고 나온다 → 카드 꼬리와 같다
    const name = grunt ? gruntSuffixes[0] : ownerKo[who];
    const key = `galactic.${snakeKey(who.replace(/([a-z])([A-Z])/g, '$1_$2'))}`;
    set(key, enName, name);
    nameRows.push([key, enName, name, grunt ? gruntSuffixes[0] : (who === 'Cyrus' ? name : `간부 ${name}`), grunt ? '갤럭시단 + 조무래기' : name, grunt ? '카드 = 원작 자산 배틀 표시' : '원작 자산(카드 꼬리와 같음)']);
  }

  // ── 트레이너 카드 능력 이름·문구(한국어 = 한국어판 카드 문구, 아래 용어 통일을 거친다) ──
  const abilitiesKo = parseTrainerAbilities(read('trainerCardsKo'), src.trainerCardsKo, errors);
  for (const t of cube.trainerCards) {
    const list = abilitiesKo.get(t.class);
    if (!list || list.length !== t.abilities.length) { errors.push(`${src.trainerCardsKo}: ${t.class}의 능력 ${list ? list.length : 0}개 ≠ ${t.abilities.length}`); continue; }
    for (const a of t.abilities) {
      const k = list[a.slot - 1];
      if (k.name !== a.name) errors.push(`${src.trainerCardsKo}: ${t.class} 능력 ${a.slot} 영문 이름 "${k.name}" ≠ xlsx "${a.name}"`);
      set(`trainer_ability.${t.key}.${a.slot}`, a.name, k.ko);
      set(`trainer_ability.${t.key}.${a.slot}.text`, a.en, k.text || undefined);
    }
  }

  // ── 보드 지명(영어 = map_cells region) ──
  for (const region of [...new Set(board.map(c => c.region).filter(Boolean))]) set(`region.${snakeKey(region)}`, region, undefined);

  // ── 고정물·아이템 이름(세이브) ──
  for (const x of extra) if (x.key && !x.direct) set(x.key, x.en, x.ko);

  // ── 용어 통일(authored 아닌 한국어 값) ──
  const termRows = [];
  for (const key of Object.keys(ko)) {
    if (authored.has(key) || ko[key] === undefined) continue;
    let v = ko[key];
    for (const t of terms) {
      const next = v.replace(t.re, t.to);
      if (next !== v) termRows.push([key, t.ref, v, next]);
      v = next;
    }
    ko[key] = v;
  }

  // ── 기술 이름: 현행 공식 한국어(결정 43 ⑩·44 ②). 용어 통일 뒤에 넣어 공식 이름은 용어 규칙이 바꾸지 않는다 ──
  const moveNames = officialMoveNames(root, src, cube.moves, ko, errors);
  if (!moveNames) return null;
  for (const [key, name] of moveNames.names) ko[key] = name;

  // ── authored(새로 옮긴 한국어) ──
  const reviewRows = [];
  for (const [key, a] of authored) {
    if (!(key in en)) { errors.push(`authored ${key}: 빌드가 만드는 키가 아니다`); continue; }
    if (en[key] !== a.en) errors.push(`authored ${key}: en "${a.en}"이 원천 "${en[key]}"과 다르다`);
    // 덮인 옛 한국어도 검토 목록에 남긴다. note = 바꾼 문구(검토 대상이면 그 줄을 가리킨다)
    if (ko[key] !== undefined && HANGUL.test(ko[key])) reviewRows.push(['(덮어씀) ' + key, a.en, ko[key], 'cards.json·ff1ce0', a.review ? `→ ${key} 줄` : `→ ${a.text}`]);
    // 새로 옮긴 문장은 사용자 검토 대상이다(03 「한국어 문장」: 새로 번역한 문장은 review: true)
    if (a.src === 'translated' && !a.review) errors.push(`authored ${key}: 새로 옮긴 문장(src translated)은 review: true여야 한다`);
    ko[key] = a.text;
    if (a.review) reviewRows.push([key, a.en, a.text, a.src, a.note]);
  }
  // 손으로 옮긴 카드·아이템 문구(lib/authored.mjs) 가운데 영문과 뜻이 달라 고친 것도 검토 목록에 오른다
  for (const x of extra) {
    if (x.review) reviewRows.push([x.key, x.en, ko[x.key], x.src, x.note]);
    if (x.reviewOf) reviewRows.push([x.label, x.reviewOf.map(k => en[k]).join(' ⏎ '), x.reviewOf.map(k => ko[k]).join(' ⏎ '), x.src, x.note]);
  }

  // ── 다시 옮김(결정 163, lib/retranslate.mjs): 범위는 옛 한국어로 가른다(종 이름과 같은 카드 이름 등). 짧은 문구(.short)는 아래 짧은 문구 칸에서 ──
  const scope = retranslateScope({ en, ko, speciesKo: new Set(speciesKo.values()), officialMoves: moveNames.official, classStatus, authored, keep }, errors);
  for (const [key, r] of retranslated) if (key in ko && !key.endsWith('.short')) ko[key] = r.ko;
  // 이 게임만의 기술 이름은 다시 옮긴 이름이 최종 — 기술 이름 보고서도 그 이름으로(원천 ours, ko_3ja2 칸은 대조용으로 남김)
  moveNames.report = moveNames.report.split('\n').map(line => {
    const f = line.split('\t'), r = retranslated.get(f[0]);
    if (!r || f[6] !== '3ja2') return line;
    f[4] = r.ko; f[5] = f[3] === r.ko ? 'same' : 'changed'; f[6] = 'ours'; f[7] = '이 게임만의 기술 · 영문에서 옮김(결정 163)';
    return f.join('\t');
  }).join('\n');

  // ── 트레이너 카드 능력의 짧은 문구(authored/trainer-short-ko.json — 사용자 결정 87 V11 라: 해금 카드의 능력 칸. 전체 문구가 정해진 뒤라 용어 규칙을 거치지 않는다) ──
  // 영어 짝은 trainer-short-en.json(M9 언어 ① — 전체 영문이 48자를 넘으면 적는다)
  const readShorts = file => {
    const j = readJson(P(file));
    if (j.schema !== 1) errors.push(`${file} schema ${j.schema}`);
    return { file, map: new Map(Object.entries(j.short || {})) };
  };
  const shortKo = readShorts('Tools/data/authored/trainer-short-ko.json'), shortEn = readShorts('Tools/data/authored/trainer-short-en.json');
  const pickShort = ({ file, map }, id, full) => {
    const s = map.has(id) ? map.get(id) : full;
    map.delete(id);
    if (typeof s !== 'string' || s === '') errors.push(`${file} ${id}: 빈 값`);
    else if (s.length > SHORT_MAX) errors.push(`${file} ${id}: ${s.length}자 — 짧은 문구는 ${SHORT_MAX}자 이하(전체 문구가 넘으면 짧은 문구를 적는다)`);
    return s;
  };
  for (const t of cube.trainerCards) for (const a of t.abilities) {
    const id = `${t.key}.${a.slot}`, key = `trainer_ability.${id}`;
    // 다시 옮긴 능력은 새 .short ?? 새 전체 문구(옛 trainer-short-ko.json은 쓰지 않는다)
    const shortNew = newShort(retranslated, key, errors);
    if (shortNew !== null) shortKo.map.delete(id);
    set(`${key}.short`, pickShort(shortEn, id, en[`${key}.text`]), shortNew ?? pickShort(shortKo, id, ko[`${key}.text`]));
  }
  for (const { file, map } of [shortKo, shortEn]) for (const id of map.keys()) errors.push(`${file} ${id}: 그런 능력이 없다`);

  // ── 우리 화면 문구(authored/screens.json — 용어 규칙·검토 목록 밖) ──
  for (const [key, e] of readScreens(root, 'Tools/data/authored/screens.json', errors)) set(key, e.en, e.ko);
  // ── 마박사 대사(extra의 direct — authored/tutorial-ko.json, lib/tutorial.mjs: 화면 문구처럼 용어 규칙·검토 목록 밖) ──
  for (const x of extra) if (x.direct) set(x.key, x.en, x.ko);

  // ── 다시 옮김 검사(범위 · en 대조 · 용어 규칙 · 빠진 키) ──
  const retranslate = checkRetranslation(retranslated, scope, en, ko, terms, true, errors);

  // ── 검증: 두 언어 키가 같고, 빈 값·제어 문자 없음, 한국어에 한글 ──
  for (const key of Object.keys(en)) {
    for (const [lang, v] of [['en', en[key]], ['ko', ko[key]]]) {
      if (typeof v !== 'string' || v === '') errors.push(`strings.${lang} ${key}: 값이 없다`);
      else if (BAD_TEXT.test(v) || LONE_SURROGATE.test(v)) errors.push(`strings.${lang} ${key}: 쓸 수 없는 글자 ${JSON.stringify(v)}`);
    }
    if (typeof ko[key] === 'string' && !HANGUL.test(ko[key])) errors.push(`strings.ko ${key}: 한글이 없다(옮기지 않음?) "${ko[key]}"`);
  }
  // ── 검증(M9 언어 ① — plan M9 완료 조건 3 앞부분): ko 키 ⊂ en 키 · en에 한글(자모 포함) 0 · 자리표시자({이름})와 <b> 짝이 두 언어에 같은 수 ──
  for (const err of checkLanguagePair(ko, en)) errors.push(err);

  // ── 보고서 ──
  const causes = countCauses(save, cards);
  const reports = {
    'review-ko.tsv': tsv(['key', 'en', 'ko', 'src', 'note'], reviewRows.sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0))),
    'names-ko.tsv': tsv(['key', 'en', 'ko', 'card', 'rom', 'rule'], nameRows),
    'terms-ko.tsv': tsv(['key', 'ref', 'before', 'after'], termRows),
    'move-names-ko.tsv': moveNames.report,
    'retranslate-ko.tsv': tsv(['key', 'family', 'state', 'flag', 'en', 'ko'], retranslate.rows),
    'xlsx-cards.md': xlsxCardsReport(cube, cards, mismatches, causes),
  };
  return { ko, en, reports, cards, byKey, rosterCsv, moveNamesSummary: moveNames.summary, retranslateSummary: retranslate.summary };
}

const HANGUL_ANY = /[ᄀ-ᇿ㄰-㆏ꥠ-꥿가-힯ힰ-퟿]/; // 한글 음절 · 자모(호환 · 확장 포함)
const HOLES = /\{[A-Za-z0-9_]+\}|<\/?b>/g; // 코드가 채우는 자리표시자 · 튜토리얼의 <b>숫자</b>
const holes = s => (s.match(HOLES) || []).sort().join(' ');

/** 두 언어 표 검사(M9 언어 ①): ko의 모든 키가 en에 있고, en 값에 한글이 없고, 자리표시자·<b> 짝 모음이 같다. → 오류 문장 목록 */
export function checkLanguagePair(ko, en) {
  const out = [];
  for (const key of Object.keys(ko)) {
    if (!(key in en)) { out.push(`strings.en ${key}: ko에 있는 키가 en에 없다`); continue; }
    const e = en[key], k = ko[key];
    if (typeof e !== 'string' || typeof k !== 'string') continue;
    if (HANGUL_ANY.test(e)) out.push(`strings.en ${key}: 한글이 남았다 "${e}"`);
    if (holes(e) !== holes(k)) out.push(`strings.en ${key}: 자리표시자가 ko와 다르다 — ko [${holes(k)}] · en [${holes(e)}]`);
  }
  return out;
}

/** checkLanguagePair 자기 시험(build.mjs --self-test) → 틀린 경우 설명 목록 */
export function selfTestLanguagePair() {
  const bad = [];
  const expect = (what, ko, en, n) => { const got = checkLanguagePair(ko, en).length; if (got !== n) bad.push(`언어 짝 ${what}: 오류 ${got}건(기대 ${n})`); };
  expect('같은 자리표시자', { a: '{mon}의 {move}!' }, { a: '{mon} used {move}!' }, 0);
  expect('<b> 짝', { a: '위력 <b>3</b>' }, { a: 'power <b>3</b>' }, 0);
  expect('en에 없는 키', { a: '가', b: '나' }, { a: 'A' }, 1);
  expect('en에 한글', { a: '가' }, { a: '가 A' }, 1);
  expect('en에 자모', { a: '가' }, { a: 'ㅋ' }, 1);
  expect('자리표시자 빠짐', { a: '{n}개' }, { a: 'some' }, 1);
  expect('자리표시자 이름 다름', { a: '{n}개' }, { a: '{m}' }, 1);
  expect('<b> 빠짐', { a: '<b>3</b>' }, { a: '3' }, 1);
  return bad;
}

/** 원작 영문 세이브의 폼 면(TTS States) 포켓몬 카드: internal_name → 카드 이름 집합(이름이 internal_name과 다른 것만). */
function englishStateNicks(saveEn) {
  const out = new Map();
  for (const w of walkPokemonCards(saveEn)) {
    if (!w.inState) continue;
    const m = /internal_name\s*=\s*"([^"]*)"/.exec(w.lua);
    if (!m || w.nick === m[1]) continue;
    if (!out.has(m[1])) out.set(m[1], new Set());
    out.get(m[1]).add(w.nick);
  }
  return out;
}

/**
 * 영어 카드 이름 = xlsx internal_name(결정 34). 폼 면(state ≥ 2)의 internal_name이 같은 종·주인의 바탕 면(state 1)과 같으면
 * (수호신 폼, 디아루가·펄기아 오리진폼) 원작 영문 세이브의 그 면 카드 이름을 쓴다(「Torterra (Noble Form)」).
 */
function englishCardName(p, pokemon, enStateNicks, errors) {
  if (p.state < 2 || !pokemon.some(q => q.dex === p.dex && q.trainer === p.trainer && q.state === 1 && q.name === p.name)) return p.name;
  const nicks = [...(enStateNicks.get(p.name) || [])].filter(n => n.startsWith(p.name + ' ('));
  if (nicks.length !== 1) errors.push(`pokemon.${p.key}: 바탕 면과 영문 이름(${p.name})이 같은데 영문 세이브 폼 카드 이름이 ${nicks.length}개: ${nicks.join(' / ')}`);
  return nicks[0] || p.name;
}

function xlsxCardsReport(cube, cards, mismatches, c) {
  const states = cards.filter(x => x.inState).length;
  const lines = [
    '# xlsx ↔ 한국어판 카드 Lua 대조',
    '',
    '`node Tools/data/build.mjs`가 쓴다(손으로 고치지 않는다). 03 §1-3 보고서, plan M2 조건 5.',
    '',
    '## 수치 불일치',
    '',
    `- 붙이기: \`cards.json\` ${cards.length}장(바탕 ${cards.length - states} + 상태 ${states}) → xlsx pokemon ${cube.pokemon.length}행. 종 = 로스터 한국어 이름 → 도감 번호, 폼 = 카드 States 순서(state), 갤럭시단 = 닉네임 꼬리(태홍·간부 마스·주피터·새턴, 조무래기는 도감 번호로 하나), 동쪽·서쪽 바다 = internal_name.`,
    '- 비교: 장수(number_in_deck = 붙은 카드 수), 체력(health), 우선도(initiative), 공격력(move_attack_strength, 「?」 포함), 기술 타입, 타입, 배울 타입(moves), 진화 비용, 조우 등급.',
    `- **불일치 ${mismatches.length}건.**`,
  ];
  if (mismatches.length) {
    lines.push('', '| 키 | 값 | xlsx | 카드 | 장수 |', '|---|---|---|---|---|');
    for (const m of mismatches) lines.push(`| ${m.key} | ${m.field} | ${m.xlsx} | ${m.cards} | ${m.count} |`);
  }
  lines.push(
    '',
    '## 고유 pokedex_name 324 ↔ 322',
    '',
    `- korean.md §8.1의 324는 세이브 전체에서 LuaScript에 \`pokedex_name\`이 들어 있는 오브젝트 ${c.luaObjects}개를 \`pokedex_name = "…"\` 정규식으로 센 값이다(다시 세어 ${c.captured}).`,
    `- 그중 카드는 ${c.cardObjects}개이고 고유 이름은 ${c.cardNames}개다. 나머지 ${c.tools.length}개는 카드가 아닌 도구다: ${c.tools.join(', ')}.`,
    `- 도구 스크립트 안의 코드 조각 ${c.extraCaptures.length}개가 이름처럼 잡혔다: ${c.extraCaptures.map(s => '`' + s + '`').join(', ')}. 그래서 ${c.cardNames} + ${c.extraCaptures.length} = ${c.captured}.`,
    `- \`cards.json\`(${c.cardsJson}장)의 고유 pokedex_name은 ${c.cardsJsonNames}, xlsx 종은 ${cube.species.size}이다. 실제 종 수는 ${cube.species.size}이 맞다.`,
    '',
    '## 진화 덱 이름 162 ↔ 로스터 진화 161',
    '',
    `- 진화 덱(태그 Evolution Card) 바탕 카드 ${c.evolutionCards}장의 닉네임은 ${c.evolutionNicks}종, 종(pokedex_name)은 ${c.evolutionSpecies}종이다. inventory §1.4의 「이름 162개」는 닉네임으로 센 값이다.`,
    `- 차이: 한 종이 닉네임 여럿인 카드 — ${c.evolutionMultiNick.map(m => `${m.species}(${m.nicks.join(' · ')})`).join(', ') || '없음'}.`,
    '',
  );
  return lines.join('\n');
}
