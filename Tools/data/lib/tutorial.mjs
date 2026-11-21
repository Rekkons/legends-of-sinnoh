// 각본 튜토리얼 데이터(M6.5, Docs/Design/07-tutorial.md §7):
//   authored/tutorial/<id>.json(손 파일 — 적지 않은 칸은 기본값) → Data/Json/tutorial/<id>.json(모든 칸을 적은 꼴, 단계 하나 = 한 줄)
//   authored/tutorial-ko.json { schema, note, strings: { "tutorial.<id>.<단계>[.hint]": { ko, en } } } → strings.ko·en의 tutorial.* 키
// 대사 키는 각본에 적지 않는다 — 단계마다 tutorial.<id>.<단계>(꼭 있다)와 tutorial.<id>.<단계>.hint(있으면)를 붙인다(07 T22). 쓰이지 않는 대사 키는 오류.
// 말없는 단계(text null, 07 T160): 막 앞 판 각본(start.after)의 맨 앞 demo·bot 줄(판 차리기 — 화면이 빨리감기로 지난다)만 대사 없이 둔다(hint·anchor·expect도 없이).
// 여기서는 꼴만 본다(칸 이름 · 형 · advance 짝 · id 겹침 · 대사 키). 키 → 색인 · 판 손보기 검사는 Sinnoh.Data TutorialLoader,
// 대사 내용(근거 · <b>숫자</b> ↔ expect · 말투 · 길이 · 앵커 문법 · 봇 답 덮임)은 시험 M65_데이터검사가 본다.
// 마박사 대사는 해라체라(07 §3-2) screens.json의 합쇼체 시험(screen.*)에 걸리지 않게 접두를 따로 둔다. en은 영어 지원(M9) 전까지 임시 값이다 [가정 T21].
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import path from 'node:path';

export const TUTORIAL_DIR = 'Tools/data/authored/tutorial';
export const TUTORIAL_LINES = 'Tools/data/authored/tutorial-ko.json';

const ADVANCES = ['next', 'command', 'bot', 'free', 'demo', 'screen'];
const STEP_KEYS = ['id', 'ch', 'anchor', 'advance', 'screen', 'botSeat', 'botAnswers', 'allow', 'auto', 'dice', 'expect', 'ref'];
const START_KEYS = ['campaign', 'seats', 'seed', 'trainerCards', 'tops', 'spots', 'after'];
const PATTERN_KEYS = ['choice', 'kind', 'target', 'arg', 'values', 'maint', 'party', 'slot', 'value'];
const FACES = ['StatusSymbol', 'RegularHit', 'CriticalHit'];

const isInt = v => Number.isInteger(v);
const isStr = v => typeof v === 'string' && v.length > 0;

/** 대사 표 → Map(키 → {ko, en}) */
export function readTutorialLines(root, errors) {
  const file = TUTORIAL_LINES;
  const j = JSON.parse(readFileSync(path.join(root, file), 'utf8'));
  const out = new Map();
  if (j.schema !== 1 || Object.keys(j).sort().join() !== 'note,schema,strings') errors.push(`${file}: 형식(schema 1, 필드 note·schema·strings)`);
  for (const [key, e] of Object.entries(j.strings || {})) {
    if (!/^tutorial\.[a-z0-9]+\.[A-Za-z0-9]+(\.hint)?$/.test(key) || Object.keys(e).sort().join() !== 'en,ko' || !isStr(e.ko) || !isStr(e.en))
      errors.push(`${file} ${key}: 항목 형식(키 tutorial.<각본>.<단계>[.hint], 값 {ko, en})`);
    out.set(key, e);
  }
  return out;
}

function pattern(p, at, errors) {
  if (p === null || typeof p !== 'object' || Array.isArray(p)) { errors.push(`${at}: 객체가 아니다`); return {}; }
  for (const k of Object.keys(p)) if (!PATTERN_KEYS.includes(k)) errors.push(`${at}: 모르는 칸 ${k}`);
  for (const k of ['choice', 'kind', 'maint']) if (k in p && !isStr(p[k])) errors.push(`${at}: ${k}는 문자열`);
  for (const k of ['arg', 'party', 'slot']) if (k in p && !isInt(p[k])) errors.push(`${at}: ${k}는 정수`);
  for (const k of ['target', 'value']) if (k in p && !isInt(p[k]) && !isStr(p[k])) errors.push(`${at}: ${k}는 정수나 키`);
  if ('values' in p && !(Array.isArray(p.values) && p.values.every(isInt))) errors.push(`${at}: values는 정수 배열`);
  // 칸 순서 고정(PATTERN_KEYS 순)
  return Object.fromEntries(PATTERN_KEYS.filter(k => k in p).map(k => [k, p[k]]));
}

function patterns(list, at, errors) {
  if (list === undefined) return [];
  if (!Array.isArray(list)) { errors.push(`${at}: 배열이 아니다`); return []; }
  return list.map((p, i) => pattern(p, `${at}[${i}]`, errors));
}

/** 손 파일 하나 → 모든 칸을 적은 각본(키 순서 고정) */
function normalize(j, file, lines, used, errors) {
  const want = ['bots', 'id', 'note', 'schema', 'start', 'steps'];
  for (const k of Object.keys(j)) if (!want.includes(k)) errors.push(`${file}: 모르는 칸 ${k}`);
  if (j.schema !== 1) errors.push(`${file}: schema 1`);
  if (!isStr(j.note)) errors.push(`${file}: note가 없다`);
  const id = j.id;
  if (!/^[a-z0-9]+$/.test(id || '') || path.basename(file) !== `${id}.json`) errors.push(`${file}: id ${id} — 파일 이름은 <id>.json`);
  const s = j.start || {};
  for (const k of Object.keys(s)) if (!START_KEYS.includes(k)) errors.push(`${file} start: 모르는 칸 ${k}`);
  if (!isInt(s.seed) || s.seed < 0) errors.push(`${file} start: seed는 0 이상 정수`);
  const seats = s.seats ?? 1;
  if (!isInt(seats) || seats < 1 || seats > 6) errors.push(`${file} start: seats 1~6`);
  const start = {
    campaign: s.campaign ?? 'HeartOfSinnoh', seats, seed: s.seed,
    trainerCards: s.trainerCards ?? null, tops: s.tops ?? [], spots: s.spots ?? [], after: s.after ?? null,
  };
  // 봇 큐: 손 파일은 { "<좌석>": [표기…] }, 나오는 꼴은 길이 = 인원 배열(좌석 0 = null)
  let bots = null;
  if (j.bots !== undefined && j.bots !== null) {
    if (typeof j.bots !== 'object' || Array.isArray(j.bots)) errors.push(`${file} bots: { "<좌석>": [...] }`);
    else {
      bots = Array.from({ length: seats }, () => null);
      for (const [seat, q] of Object.entries(j.bots)) {
        const n = Number(seat);
        if (!/^[1-5]$/.test(seat) || n >= seats) { errors.push(`${file} bots: 좌석 ${seat}(1~인원−1)`); continue; }
        bots[n] = patterns(q, `${file} bots.${seat}`, errors);
      }
    }
  }
  const ids = new Set();
  let lead = true;   // 아직 대사 있는 단계가 나오지 않았다(말없는 단계를 둘 수 있는 자리)
  const steps = (Array.isArray(j.steps) ? j.steps : (errors.push(`${file}: steps가 배열이 아니다`), [])).map((st, i) => {
    const at = `${file} steps[${i}] ${st.id}`;
    for (const k of Object.keys(st)) if (!STEP_KEYS.includes(k)) errors.push(`${at}: 모르는 칸 ${k}`);
    if (!/^[A-Za-z0-9]+$/.test(st.id || '')) errors.push(`${at}: 단계 id`);
    else if (ids.has(st.id)) errors.push(`${at}: 단계 id 겹침`);
    ids.add(st.id);
    if (!isInt(st.ch) || st.ch < 0) errors.push(`${at}: ch는 0 이상 정수`);
    // advance: "next" · "command" · "bot" · "free" · "demo" · "screen:<이름>"
    const m = /^(\w+)(?::(.+))?$/.exec(st.advance || '');
    const advance = m ? m[1] : null, screen = m && m[2] !== undefined ? m[2] : null;
    if (!ADVANCES.includes(advance) || (advance === 'screen') !== (screen !== null)) errors.push(`${at}: advance ${st.advance}(${ADVANCES.join('·')} — screen은 screen:<이름>)`);
    const text = `tutorial.${id}.${st.id}`, hint = `${text}.hint`;
    const silent = !lines.has(text);
    if (!silent) { used.add(text); lead = false; }
    else if (!(start.after && lead && (advance === 'demo' || advance === 'bot'))) errors.push(`${at}: 대사 ${text}가 ${TUTORIAL_LINES}에 없다(말없는 단계는 막 앞 판 각본의 맨 앞 demo·bot 줄만 — 07 T160)`);
    else if (lines.has(hint) || st.anchor != null || Object.keys(st.expect ?? {}).length > 0) errors.push(`${at}: 말없는 단계에 hint·anchor·expect를 두지 않는다(07 T160)`);
    if (lines.has(hint)) used.add(hint);
    if ('dice' in st && !(Array.isArray(st.dice) && st.dice.every(f => FACES.includes(f)))) errors.push(`${at}: dice는 ${FACES.join('·')} 배열`);
    const expect = st.expect ?? {};
    if (typeof expect !== 'object' || Array.isArray(expect) || !Object.values(expect).every(isInt)) errors.push(`${at}: expect는 { 키: 정수 }`);
    if (!Array.isArray(st.ref) || !st.ref.every(isStr)) errors.push(`${at}: ref는 문자열 배열(근거 — rulings ID · 01 절)`);
    return {
      id: st.id, ch: st.ch, text: silent ? null : text, hint: lines.has(hint) ? hint : null, anchor: st.anchor ?? null, advance, screen,
      botSeat: st.botSeat ?? -1, botAnswers: st.botAnswers ?? 0,
      allow: patterns(st.allow, `${at} allow`, errors), auto: patterns(st.auto, `${at} auto`, errors),
      dice: st.dice ?? [], expect, ref: st.ref ?? [],
    };
  });
  return { schema: 1, id, start, bots, steps };
}

/** 나오는 꼴: 머리 한 줄 + 단계 하나 = 한 줄 */
function write(s) {
  const head = JSON.stringify({ schema: s.schema, id: s.id, start: s.start, bots: s.bots });
  return `${head.slice(0, -1)},"steps":[\n${s.steps.map(x => JSON.stringify(x)).join(',\n')}\n]}\n`;
}

/** → { files: { "<id>.json": 내용 }, strings: [{key, en, ko}], rows: { id: 단계 수 }, chapters: 장 목록 행 } */
export function buildTutorial(root, errors) {
  const lines = readTutorialLines(root, errors);
  const dir = path.join(root, TUTORIAL_DIR);
  const used = new Set();
  const files = {}, rows = {}, scripts = {};
  for (const name of (existsSync(dir) ? readdirSync(dir) : []).filter(n => n.endsWith('.json')).sort()) {
    const file = `${TUTORIAL_DIR}/${name}`;
    const s = normalize(JSON.parse(readFileSync(path.join(dir, name), 'utf8')), file, lines, used, errors);
    files[name] = write(s);
    rows[s.id] = s.steps.length;
    scripts[s.id] = s;
  }
  for (const key of lines.keys()) if (!used.has(key)) errors.push(`${TUTORIAL_LINES} ${key}: 어느 각본 단계에도 쓰이지 않는다`);
  return { files, rows, chapters: buildChapters(root, scripts, errors), strings: [...lines].map(([key, e]) => ({ key, en: e.en, ko: e.ko, direct: true })) };
}

// 목차의 장 머리(M6.5 「화면 ② 목차·입구」, 07 §2-1·§9-3):
//   authored/tutorial-chapters.json { schema, note, main: [장 번호…], then: { "main<ch>": [각본 id…] }, extra: [{ id: "e01"…, icon: Gen 키 | null }], rulebook: { "<장 id>": [절 키…] } } → Data/Json/tutorial_chapters.json 행
//   { id, script, ch, icon, name, steps, then, rulebook }. 본편 장 = 각본 main의 ch(id "main<ch>"), 확장 장 = 각본 하나 통째(id = 각본 id, ch −1). 장 이름은 screens.json의 screen.tutorial.menu.ch.<id>.
//   then = 본편 장 하나가 그 장의 main 단계 뒤에 차례로 이어 여는 각본(막 맛보기 7장 = preview2 → preview3, 07 T54 — 각본마다 판을 새로 연다). 그 각본의 단계 ch는 모두 그 장 번호.
//   steps = 그 장의 단계 수(대사 있는 단계만 — 말없는 판 차리기 단계는 화면에 뜨지 않는다, 07 T160. then 각본의 단계를 더한다. 각본이 아직 없거나 그 장 단계가 없으면 0 — 목차가 「준비 중」으로 둔다).
//   본편 각본의 ch는 모두 main에 있어야 하고, main 밖 각본은 모두 then이나 extra에 한 번씩 있어야 한다.
//   rulebook = 장마다 관련 룰 설명서 절 키 1~4개({ "<장 id>": ["overview.goal", …] } — 모든 장이 한 줄씩, M9 「튜토리얼 잇기」 [가정 07 T212]). 절 키 문법은 Sinnoh.Data.Rulebook과 같다(장.절),
//   그 키가 설명서에 있는지는 시험 M9_튜토리얼_장마다설명서절키가(rulebook.ko.json이 있으면 — 설명서 화면 S43을 합친 뒤) 본다. 끝 말풍선 뒤 「설명서에서 더 보기」 단추가 이 값을 쓴다.
export const TUTORIAL_CHAPTERS = 'Tools/data/authored/tutorial-chapters.json';
const SCREENS = 'Tools/data/authored/screens.json';
const RULEBOOK_SECTION = /^[a-z]+(?:_[a-z]+)*\.[a-z0-9]+(?:_[a-z0-9]+)*$/;

const shown = steps => steps.filter(s => s.text !== null).length;

function buildChapters(root, scripts, errors) {
  const file = TUTORIAL_CHAPTERS;
  const j = JSON.parse(readFileSync(path.join(root, file), 'utf8'));
  if (j.schema !== 1 || Object.keys(j).sort().join() !== 'extra,main,note,rulebook,schema,then') { errors.push(`${file}: 형식(schema 1, 필드 extra·main·note·rulebook·schema·then)`); return []; }
  const screens = JSON.parse(readFileSync(path.join(root, SCREENS), 'utf8')).strings || {};
  const rulebookOf = j.rulebook !== null && typeof j.rulebook === 'object' && !Array.isArray(j.rulebook) ? j.rulebook : (errors.push(`${file} rulebook: { "<장 id>": [절 키…] }`), {});
  const out = [];
  const thens = new Set();
  const add = (id, script, ch, icon, steps, then = []) => {
    const name = `screen.tutorial.menu.ch.${id}`;
    if (!(name in screens)) errors.push(`${file} ${id}: 장 이름 ${name}가 ${SCREENS}에 없다`);
    const rulebook = rulebookOf[id];
    if (!Array.isArray(rulebook) || rulebook.length < 1 || rulebook.length > 4 || !rulebook.every(k => RULEBOOK_SECTION.test(k || '')) || new Set(rulebook).size !== rulebook.length)
      errors.push(`${file} rulebook.${id}: 설명서 절 키 1~4개(장.절 꼴, 겹침 없음)`);
    out.push({ id, script, ch, icon, name, steps, then, rulebook: Array.isArray(rulebook) ? rulebook : [] });
  };
  const main = scripts.main;
  if (!main) errors.push(`${file}: 본편 각본 main이 없다`);
  if (!Array.isArray(j.main) || !j.main.every(c => isInt(c) && c >= 0) || j.main.some((c, i) => i > 0 && c <= j.main[i - 1])) errors.push(`${file} main: 0 이상 정수, 오름차순·겹침 없음`);
  else {
    const thenOf = j.then !== null && typeof j.then === 'object' && !Array.isArray(j.then) ? j.then : (errors.push(`${file} then: { "main<ch>": [각본 id…] }`), {});
    for (const key of Object.keys(thenOf)) if (!j.main.some(ch => `main${ch}` === key)) errors.push(`${file} then: ${key}는 main 목록의 장이 아니다`);
    for (const ch of j.main) {
      const id = `main${ch}`, then = thenOf[id] ?? [];
      if (!Array.isArray(then) || !then.every(t => /^[a-z0-9]+$/.test(t || '') && t !== 'main' && !/^e[0-9]{2}$/.test(t))) { errors.push(`${file} then.${id}: 각본 id 배열(main·e<nn> 밖)`); continue; }
      let steps = main ? shown(main.steps.filter(s => s.ch === ch)) : 0;
      for (const t of then) {
        if (thens.has(t)) errors.push(`${file} then.${id}: 각본 ${t}가 두 번 나온다`);
        thens.add(t);
        const sc = scripts[t];
        if (!sc) { errors.push(`${file} then.${id}: 각본 ${t}가 없다`); continue; }
        for (const st of sc.steps) if (st.ch !== ch) errors.push(`${file} then.${id}: ${t}의 단계 ${st.id} 장 ${st.ch} ≠ ${ch}`);
        steps += shown(sc.steps);
      }
      add(id, 'main', ch, null, steps, then);
    }
    if (main) for (const s of main.steps) if (!j.main.includes(s.ch)) errors.push(`${file} main: 본편 단계 ${s.id}의 장 ${s.ch}가 목록에 없다`);
  }
  const ids = new Set();
  for (const [i, e] of (Array.isArray(j.extra) ? j.extra : (errors.push(`${file} extra: 배열이 아니다`), [])).entries()) {
    const at = `${file} extra[${i}]`;
    if (e === null || typeof e !== 'object' || Object.keys(e).sort().join() !== 'icon,id') { errors.push(`${at}: { id, icon }`); continue; }
    if (!/^e[0-9]{2}$/.test(e.id || '') || ids.has(e.id)) errors.push(`${at}: id ${e.id}(e01 꼴, 겹침 없음)`);
    if (e.icon !== null && !/^[A-Za-z0-9_]+(\/[A-Za-z0-9_]+)*\.png$/.test(e.icon || '')) errors.push(`${at}: icon은 Gen 키(…/<이름>.png)나 null`);
    ids.add(e.id);
    add(e.id, e.id, -1, e.icon, scripts[e.id] ? shown(scripts[e.id].steps) : 0);
  }
  for (const id of Object.keys(scripts)) if (id !== 'main' && !ids.has(id) && !thens.has(id)) errors.push(`${file}: 각본 ${id}가 then·extra에 없다(목차에 없는 각본)`);
  for (const id of Object.keys(rulebookOf)) if (!out.some(c => c.id === id)) errors.push(`${file} rulebook: ${id}는 목차에 없는 장이다`);
  return out;
}
