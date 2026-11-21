// 손으로 옮긴 카드·아이템 문구(03 §1-3·§1-4 「이미지 전용 카드」, plan M5 「카드·아이템 데이터」).
//   authored/cards/<deck>.json 7개  { schema: 1, note, deck, cards: { <카드 키>: { name{en, ko}, en, ko | trades, starters?, src{en, ko}, verified, review, note } } }
//     en = 영문 카드 그림을 보고 옮긴 본문(효과의 정본, rules 규칙 「판정 근거」 — 손 파일에서는 줄을 \n으로 잇고 표에는 줄 배열로 쓴다), ko = 화면 문자열(한국어판 문구 + 지침 용어 — 효과 근거로 쓰지 않는다),
//     trades = 은행상회 거래 두 줄(글이 없는 카드 — en·ko 대신), starters = 고향 마을 카드 그림의 스타터(fixtures villageStarters와 맞춰 본다),
//     src.en = 영문 그림(원작 GitHub 경로#시트 칸)·룰북 행, src.ko = 전사본 위치, verified = 영문 그림과 대조한 날짜.
//   authored/items.json            { schema: 1, note, items: { <아이템 키>: { kind?, name?, en, ko, recipe, src, review, note } } }
//     kind·name이 있는 줄 = 세이브에 실물이 없는 새 행(Stew·Stamp·TrainerItem — 트레이너 카드 전용 아이템 11, rulings G1), 없는 줄 = 세이브에서 뽑은 행에 en·recipe를 붙인다.
// 빌드가 멈추는 조건(03 §1-4): 고유 앞면 54가 모두 verified, src 빈칸 0, 레시피 14(볼 4·스튜 4·배틀 아이템 6), 스튜 4, 스탬프 7, 트레이너 카드 전용 아이템 11.
import { readFileSync } from 'node:fs';
import path from 'node:path';

export const CARD_DECKS = ['act', 'home_village', 'shrine_event', 'change_of_fate', 'ginkgo_trade', 'distortion_breach', 'ultra_onslaught'];
const APRICORNS = ['Red', 'Blue', 'Yellow', 'Pink', 'Green', 'White', 'Black'];
const DATE = /^20\d\d-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
const DIR = 'Tools/data/authored';

const text = v => typeof v === 'string' && v !== '' && v === v.trim();
const lines = v => typeof v === 'string' && v.split('\n').every(text);
const pair = (v, a, b) => v && typeof v === 'object' && Object.keys(v).sort().join() === [a, b].sort().join() && text(v[a]) && text(v[b]);
const colors = v => Array.isArray(v) && v.length > 0 && v.every(c => APRICORNS.includes(c));

function readFile(root, file, keys, errors) {
  const j = JSON.parse(readFileSync(path.join(root, DIR, file), 'utf8'));
  const want = ['note', 'schema', ...keys].sort().join();
  if (j.schema !== 1 || Object.keys(j).sort().join() !== want) errors.push(`${DIR}/${file}: 형식(schema 1, 필드 ${want})`);
  return j;
}

/** → Map<카드 키, 항목>(덱 순서 → 파일 안 순서) */
export function readCards(root, errors) {
  const out = new Map();
  for (const deck of CARD_DECKS) {
    const file = `cards/${deck}.json`;
    const j = readFile(root, file, ['deck', 'cards'], errors);
    if (j.deck !== deck) errors.push(`${DIR}/${file}: deck "${j.deck}"`);
    for (const [key, c] of Object.entries(j.cards || {})) {
      const at = `${DIR}/${file} ${key}`;
      const traded = deck === 'ginkgo_trade';
      const want = ['name', 'note', 'review', 'src', 'verified', ...(traded ? ['trades'] : ['en', 'ko']), ...(deck === 'home_village' ? ['starters'] : [])].sort().join();
      if (Object.keys(c).sort().join() !== want) { errors.push(`${at}: 필드는 ${want}`); continue; }
      if (!key.startsWith(deck + '.')) errors.push(`${at}: 키 접두는 ${deck}.`);
      if (out.has(key)) errors.push(`${at}: 겹침`);
      if (!pair(c.name, 'en', 'ko')) errors.push(`${at}: name{en, ko}`);
      if (!pair(c.src, 'en', 'ko')) errors.push(`${at}: src{en, ko} 빈칸`);
      if (typeof c.verified !== 'string' || !DATE.test(c.verified)) errors.push(`${at}: verified는 영문 그림과 대조한 날짜(YYYY-MM-DD)`);
      if (typeof c.review !== 'boolean' || typeof c.note !== 'string' || (c.review && c.note === '')) errors.push(`${at}: review(참이면 note에 까닭)`);
      if (traded) {
        const ok = Array.isArray(c.trades) && c.trades.length === 2 && c.trades.every(t => t && Object.keys(t).sort().join() === 'get,give' && colors(t.give) && colors(t.get));
        if (!ok) errors.push(`${at}: trades = [{give, get}, {give, get}](규토리 색)`);
      } else if (!lines(c.en) || !lines(c.ko) || c.en.split('\n').length !== c.ko.split('\n').length) errors.push(`${at}: en·ko 본문(줄 = \\n, 두 언어 줄 수가 같다)`);
      out.set(key, { deck, ...c });
    }
  }
  return out;
}

/**
 * rows: buildDecks가 세이브에서 뽑은 카드 행 [{key, deck, copies}], villageStarters: authored/fixtures.json(village.<마을> → 스타터 둘).
 * → 행 [{key, deck, copies, en, trades, src{ko, en}, verified}](순서 그대로)
 */
export function mergeCards(rows, cards, villageStarters, errors) {
  for (const key of cards.keys()) if (!rows.some(r => r.key === key)) errors.push(`${DIR}/cards: ${key}는 세이브 덱에 없는 앞면`);
  const out = rows.map(r => {
    const c = cards.get(r.key);
    if (!c) { errors.push(`cards ${r.key}: ${DIR}/cards/에 전사가 없다(verified·src 빈칸)`); return r; }
    if (r.deck !== `deck.${c.deck}`) errors.push(`cards ${r.key}: 덱 ${r.deck} ≠ ${c.deck}`);
    if (c.deck === 'home_village') {
      const want = villageStarters[`village.${r.key.slice('home_village.'.length)}`];
      if (!want || JSON.stringify(want) !== JSON.stringify(c.starters)) errors.push(`cards ${r.key}: starters ${JSON.stringify(c.starters)} ≠ villageStarters ${JSON.stringify(want)}`);
    }
    return { key: r.key, deck: r.deck, copies: r.copies, en: c.en === undefined ? null : c.en.split('\n'), trades: c.trades ?? null, src: { ko: c.src.ko, en: c.src.en }, verified: c.verified };
  });
  // 03 §1-4 이미지 전용 카드: 고유 앞면 54가 모두 verified, src 빈칸 0
  const verified = out.filter(r => r.verified && r.src && r.src.ko && r.src.en).length;
  if (verified !== 54) errors.push(`이미지 전용 카드 verified ${verified}(기대 54, src 빈칸 0)`);
  return out;
}

/** 카드 문자열: <카드 키> = 이름, <카드 키>.text.<n> = 본문 n째 줄(1부터 — 문자열 표는 줄바꿈을 받지 않는다. 은행상회는 이름만). 검토 대상 카드는 { reviewOf: 줄 키들, label, src, note } 한 줄로 알린다(검토 목록 한 행) */
export function cardStrings(cards) {
  const out = [];
  for (const [key, c] of cards) {
    out.push({ key, en: c.name.en, ko: c.name.ko });
    if (c.en === undefined) continue;
    const en = c.en.split('\n'), ko = c.ko.split('\n');
    en.forEach((line, i) => out.push({ key: `${key}.text.${i + 1}`, en: line, ko: ko[i] }));
    if (c.review) out.push({ reviewOf: en.map((_, i) => `${key}.text.${i + 1}`), label: `${key}.text`, src: `transcribed:${c.src.ko}`, note: c.note });
  }
  return out;
}

const ITEM_KIND_ORDER = ['Stew', 'Stamp', 'TrainerItem'];

/**
 * items: buildItems 결과({rows: [{key, kind, copies}], names}) → 행 [{key, kind, copies, en, recipe}] + 새 행(Stew·Stamp·TrainerItem, 종류 → 키 순으로 뒤에 붙인다 —
 * 세이브에서 뽑은 행의 색인은 그대로) + 문자열([{key, en, ko, …}]: 새 행 이름, <키>.text = 효과 문구).
 */
export function mergeItems(root, items, errors) {
  const j = readFile(root, 'items.json', ['items'], errors);
  const byKey = new Map(items.rows.map(r => [r.key, r]));
  const strings = [], added = [], extra = new Map();
  for (const [key, a] of Object.entries(j.items || {})) {
    const at = `${DIR}/items.json ${key}`;
    const isNew = 'kind' in a;
    const want = ['en', 'ko', 'note', 'recipe', 'review', 'src', ...(isNew ? ['kind', 'name'] : [])].sort().join();
    if (Object.keys(a).sort().join() !== want) { errors.push(`${at}: 필드는 ${want}`); continue; }
    if (!text(a.en) || !text(a.ko) || !text(a.src)) errors.push(`${at}: en·ko·src 빈칸`);
    if (a.recipe !== null && !colors(a.recipe)) errors.push(`${at}: recipe는 규토리 색 목록 또는 null`);
    if (typeof a.review !== 'boolean' || typeof a.note !== 'string' || (a.review && a.note === '')) errors.push(`${at}: review(참이면 note에 까닭)`);
    if (isNew) {
      if (!ITEM_KIND_ORDER.includes(a.kind)) errors.push(`${at}: 새 행의 kind는 ${ITEM_KIND_ORDER.join('·')}`);
      if (!pair(a.name, 'en', 'ko')) errors.push(`${at}: name{en, ko}`);
      if (!key.startsWith(a.kind === 'Stamp' ? 'stamp.' : 'item.')) errors.push(`${at}: 키 접두(스탬프 stamp., 나머지 item.)`);
      if (byKey.has(key) || added.some(r => r.key === key)) errors.push(`${at}: 겹침`);
      added.push({ key, kind: a.kind, copies: null, en: a.en, recipe: a.recipe });
      strings.push({ key, en: a.name?.en, ko: a.name?.ko });
    } else {
      if (!byKey.has(key)) { errors.push(`${at}: 세이브에서 뽑은 아이템이 아니다(새 행이면 kind·name)`); continue; }
      if (extra.has(key)) errors.push(`${at}: 겹침`);
      extra.set(key, a);
    }
    strings.push({ key: `${key}.text`, en: a.en, ko: a.ko, review: a.review, src: `transcribed:${a.src}`, note: a.note });
  }
  added.sort((a, b) => ITEM_KIND_ORDER.indexOf(a.kind) - ITEM_KIND_ORDER.indexOf(b.kind) || (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
  const rows = [...items.rows.map(r => ({ key: r.key, kind: r.kind, copies: r.copies, en: extra.get(r.key)?.en ?? null, recipe: extra.get(r.key)?.recipe ?? null })), ...added];

  // 03 §1-4: 레시피 14 = 볼 4 + 스튜 4 + 배틀 아이템 6(recipe가 있는 Held), 스탬프 7
  const count = (what, got, wantN) => { if (got !== wantN) errors.push(`${what}: ${got}(기대 ${wantN})`); };
  const withRecipe = kind => rows.filter(r => r.kind === kind && r.recipe).length;
  count('레시피', rows.filter(r => r.recipe).length, 14);
  count('볼 레시피', withRecipe('Ball'), 4);
  count('스튜', rows.filter(r => r.kind === 'Stew').length, 4);
  count('스튜 레시피', withRecipe('Stew'), 4);
  count('배틀 아이템(레시피 있는 Held)', withRecipe('Held'), 6);
  count('스탬프', rows.filter(r => r.kind === 'Stamp').length, 7);
  count('트레이너 카드 전용 아이템(rulings G1)', rows.filter(r => r.kind === 'TrainerItem').length, 11);
  for (const r of rows) if (r.recipe && !r.en) errors.push(`items ${r.key}: 레시피가 있는데 영문 효과가 없다`);
  return { rows, names: items.names, strings };
}
