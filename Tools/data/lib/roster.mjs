// 로스터(Research/roster.csv, 포켓몬 322종: ko_name,en_name,dex,gen,role,copies). 10-30 raw/_tools/job-e4eed7cf/roster2.js + dex.js를 옮겼다(plan M2).
// 원천: 한국어판 카드 Lua(sources cards, 633장) + authored/dex-ko.json(한국어 종 이름 → [영문, 도감 번호], dex.js 표).
// roster2.js는 세이브 최상위 덱 번호(T1471~T1479)로 역할을 정했다. 여기서는 그 덱의 역할 태그로 정한다(바탕 카드는 역할 태그가 하나다).
// 출력 바이트는 roster2.js와 같다(UTF-8 BOM, LF, 도감 번호 순). build.mjs가 Research/roster.csv에 쓰고 --check가 비교한다.

const ROLE_TAGS = {
  'Galactic Boss': 'galactic_boss', 'Galactic Commander': 'galactic_commander', 'Galactic Grunt': 'galactic_grunt',
  'Legendary Encounter Card': 'legendary', 'Ultra Beast Encounter Card': 'ultra_beast', 'Ultra Burst Encounter Card': 'ultra_burst',
  'Moderate Encounter Card': 'wild_moderate', 'Evolution Card': 'evolution', 'Weak Encounter Card': 'wild_weak',
  'Strong Encounter Card': 'wild_strong', 'Starter Card': 'starter',
};
const ORDER = ['starter', 'wild_weak', 'wild_moderate', 'wild_strong', 'legendary', 'ultra_beast', 'ultra_burst', 'galactic_grunt', 'galactic_commander', 'galactic_boss', 'evolution'];
const EXTRA = { 디아루가: 'sinnoh_god', 펄기아: 'sinnoh_god', 기라티나: 'sinnoh_god', 엠라이트: 'lake_guardian', 유크시: 'lake_guardian', 아그놈: 'lake_guardian' };
const gen = d => (d <= 151 ? 1 : d <= 251 ? 2 : d <= 386 ? 3 : d <= 493 ? 4 : d <= 649 ? 5 : d <= 721 ? 6 : d <= 809 ? 7 : d <= 905 ? 8 : 9);

/**
 * cards: 카드 Lua 배열(raw/_tools/cards.json), dexKo: authored/dex-ko.json 내용.
 * → { rows: [{ko, en, dex, gen, role, copies}](도감 번호 순), csv: roster.csv 내용 }
 */
export function buildRoster(cards, dexKo, errors) {
  const file = 'Tools/data/authored/dex-ko.json';
  if (dexKo.schema !== 1 || Object.keys(dexKo).sort().join() !== 'note,schema,species') errors.push(`${file}: 형식(schema 1, 필드 note·schema·species)`);
  const dex = dexKo.species || {};
  for (const [ko, v] of Object.entries(dex))
    if (!Array.isArray(v) || v.length !== 2 || typeof v[0] !== 'string' || !Number.isInteger(v[1])) errors.push(`${file} ${ko}: [영문, 도감 번호]가 아니다`);

  const sp = new Map();
  cards.forEach((c, i) => {
    const s = sp.get(c.pokedex_name) || { n: c.pokedex_name, roles: new Set(), copies: 0, noble: false };
    sp.set(c.pokedex_name, s);
    if (c.inState) { if (/수호신 폼/.test(c.nick)) s.noble = true; return; }
    const roles = c.tags.filter(t => t in ROLE_TAGS);
    if (roles.length !== 1) { errors.push(`cards.json[${i}] ${c.nick}: 역할 태그 ${roles.length}개`); return; }
    s.roles.add(ROLE_TAGS[roles[0]]);
    s.copies++;
  });

  const rows = [];
  for (const s of sp.values()) {
    const d = dex[s.n];
    if (!d) { errors.push(`${file}에 종 ${s.n}이 없다`); continue; }
    const roles = ORDER.filter(r => s.roles.has(r));
    if (EXTRA[s.n]) roles.splice(1, 0, EXTRA[s.n]);
    if (s.noble) roles.push('noble_form');
    rows.push({ ko: s.n, en: d[0], dex: d[1], gen: gen(d[1]), role: roles.join(';'), copies: s.copies });
  }
  for (const ko of Object.keys(dex)) if (!sp.has(ko)) errors.push(`${file} ${ko}: 카드에 없는 종`);
  rows.sort((a, b) => a.dex - b.dex);
  for (let i = 1; i < rows.length; i++) if (rows[i].dex === rows[i - 1].dex) errors.push(`roster 도감 번호 겹침: ${rows[i - 1].ko}·${rows[i].ko}(${rows[i].dex})`);

  const esc = v => (/[,"]/.test(String(v)) ? '"' + String(v).replace(/"/g, '""') + '"' : String(v));
  const csv = '﻿ko_name,en_name,dex,gen,role,copies\n' + rows.map(r => [r.ko, r.en, r.dex, r.gen, r.role, r.copies].map(esc).join(',')).join('\n') + '\n';
  return { rows, csv };
}
