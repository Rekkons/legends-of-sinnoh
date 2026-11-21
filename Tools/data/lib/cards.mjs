// 한국어판 포켓몬 카드 Lua(raw/_tools/cards.json 633장) → xlsx pokemon 키(03 §1-2)에 붙이기.
// 종은 로스터(lib/roster.mjs, Research/roster.csv)의 한국어 이름 → 도감 번호, 폼은 state(카드 States 순서), 갤럭시단 카드는 주인(닉네임 꼬리),
// 동쪽·서쪽 바다는 internal_name으로 가른다. 붙이지 못한 카드·카드가 없는 키는 오류, 수치가 다른 것은 불일치 목록으로 돌려준다.

const GALACTIC_TAGS = { 'Galactic Boss': 'Boss', 'Galactic Commander': 'Commander', 'Galactic Grunt': 'Grunt' };

/**
 * cards: cards.json 배열, pokemon·moves: cube 행, roster: buildRoster 행([{ko, en, dex, …}]), ownerKo: {Cyrus|Mars|Jupiter|Saturn: 원작 자산 한국어 이름}.
 * → { byKey: Map<키, 카드[]>, mismatches: [{key, field, xlsx, cards, count}], gruntSuffix }
 */
export function matchCards(cards, pokemon, moves, roster, ownerKo, errors) {
  const dexOfKo = new Map();
  for (const r of roster) {
    if (dexOfKo.has(r.ko)) errors.push(`roster 한국어 이름 겹침: ${r.ko}`);
    dexOfKo.set(r.ko, r.dex);
  }
  const moveByKey = new Map(moves.map(m => [m.key, m]));
  const ownerOfKo = new Map(Object.entries(ownerKo).map(([en, ko]) => [ko, en]));
  const byKey = new Map(pokemon.map(r => [r.key, []]));
  const gruntSuffixes = new Set();

  let base = null, stateNo = 0;
  cards.forEach((c, i) => {
    const at = `cards.json[${i}] ${c.nick}`;
    // States: 바탕 카드(state 1, 상태가 없으면 0) 바로 뒤에 상태 2, 3, …이 키 순서로 온다(raw/_tools/cards.js walk)
    let state;
    if (!c.inState) {
      if (base && stateNo !== base.states) errors.push(`${at}: 앞 카드 ${base.nick}의 상태 ${base.states}개 중 ${stateNo}개만 있다`);
      base = c; stateNo = 0;
      state = c.states > 0 ? 1 : 0;
    } else {
      if (!base || ++stateNo > base.states) { errors.push(`${at}: 바탕 카드 없는 상태`); return; }
      state = stateNo + 1;
    }
    const dex = dexOfKo.get(c.pokedex_name);
    if (dex === undefined) { errors.push(`${at}: roster에 없는 종 ${c.pokedex_name}`); return; }

    const tier = c.tags.map(t => GALACTIC_TAGS[t]).find(Boolean);
    let owners = [null];
    if (tier) {
      const m = /^(.*) \((.+)\)$/.exec(c.nick);
      const tail = m ? m[2] : '';
      if (tier === 'Grunt') { owners = ['FemaleGrunt', 'MaleGrunt']; gruntSuffixes.add(tail); }
      else {
        const who = ownerOfKo.get(tier === 'Commander' ? tail.replace(/^간부 /, '') : tail);
        if (!who || (tier === 'Commander') !== tail.startsWith('간부 ')) { errors.push(`${at}: 갤럭시단 주인 꼬리 "${tail}"를 모른다`); return; }
        owners = [who];
      }
    }
    let cand = pokemon.filter(r => r.dex === dex && r.state === state && owners.includes(r.trainer));
    if (cand.length > 1) {
      const east = /^East Ocean /.test(c.internal_name);
      cand = cand.filter(r => /^\d+-e:/.test(r.key) === east);
    }
    if (cand.length !== 1) { errors.push(`${at}: xlsx 행 ${cand.length}개와 맞는다(dex ${dex}, state ${state}, 주인 ${owners})`); return; }
    byKey.get(cand[0].key).push(c);
  });
  if (base && stateNo !== base.states) errors.push(`마지막 카드 ${base.nick}의 상태 수가 맞지 않다`);

  // 수치 대조(plan M2 조건 5): 키마다 카드 값의 종류를 xlsx 값과 비교한다
  const mismatches = [];
  const tierName = t => t.split('_').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join('');
  for (const r of pokemon) {
    const list = byKey.get(r.key);
    if (list.length === 0) { errors.push(`pokemon ${r.key}: 붙은 카드가 없다`); continue; }
    const sig = moveByKey.get(r.signature);
    const fields = [
      ['copies', String(r.copies), () => String(list.length)],
      ['health', String(r.health), c => String(c.health)],
      ['initiative', String(r.initiative), c => String(c.initiative)],
      ['attackStrength', sig.attackStrength === null ? '?' : String(sig.attackStrength), c => c.move_attack_strength],
      ['moveType', sig.type, c => c.move_type],
      ['types', r.types.join('/'), c => c.types.join('/')],
      ['learnTypes', r.learnTypes.join('/'), c => c.moves.join('/')],
      ['evolveCost', r.evolveCost === null ? '' : String(r.evolveCost), c => (c.evolve_cost === null ? '' : String(c.evolve_cost))],
      ['tier', r.tier, c => tierName(c.encounter_tier)],
    ];
    for (const [field, want, get] of fields) {
      const seen = new Map();
      for (const c of field === 'copies' ? [null] : list) { const v = get(c); seen.set(v, (seen.get(v) || 0) + 1); }
      for (const [v, n] of seen) if (v !== want) mismatches.push({ key: r.key, field, xlsx: want, cards: v, count: field === 'copies' ? list.length : n });
    }
  }
  return { byKey, mismatches, gruntSuffixes: [...gruntSuffixes] };
}

/**
 * 03 §1-3 보고서용 숫자: 고유 pokedex_name 324 ↔ 322, 진화 덱 이름 162 ↔ 진화 종 161이 어디서 갈리는지.
 * save: 한국어판 세이브(JSON), cards: cards.json. 숫자와 원인 목록을 돌려준다(보고서 글은 strings.mjs가 쓴다).
 */
export function countCauses(save, cards) {
  // korean.md §8.1의 324는 세이브 전체 LuaScript에서 `pokedex_name = "…"`을 정규식으로 센 값이다
  const objs = [];
  (function walk(list) {
    for (const o of list || []) {
      if ((o.LuaScript || '').includes('pokedex_name')) objs.push(o);
      walk(o.ContainedObjects);
      if (o.States) walk(Object.values(o.States));
    }
  })(save.ObjectStates);
  const captured = new Set();
  for (const o of objs) for (const m of o.LuaScript.matchAll(/pokedex_name = "([^"]*)"/g)) captured.add(m[1]);
  const isCard = o => /^\s*pokedex_name\s*=\s*"/m.test(o.LuaScript);
  const cardNames = new Set(objs.filter(isCard).map(o => /^\s*pokedex_name\s*=\s*"([^"]*)"/m.exec(o.LuaScript)[1]));
  const tools = objs.filter(o => !isCard(o)).map(o => o.Nickname || o.Name);

  // 진화 덱(태그 Evolution Card)의 바탕 카드: 닉네임 종류 vs 종 종류
  const evo = cards.filter(c => !c.inState && c.tags.includes('Evolution Card'));
  const nicksOf = new Map();
  for (const c of evo) { if (!nicksOf.has(c.pokedex_name)) nicksOf.set(c.pokedex_name, new Set()); nicksOf.get(c.pokedex_name).add(c.nick); }
  return {
    luaObjects: objs.length,
    cardObjects: objs.filter(isCard).length,
    tools,
    captured: captured.size,
    cardNames: cardNames.size,
    extraCaptures: [...captured].filter(n => !cardNames.has(n)),
    cardsJson: cards.length,
    cardsJsonNames: new Set(cards.map(c => c.pokedex_name)).size,
    evolutionCards: evo.length,
    evolutionNicks: new Set(evo.map(c => c.nick)).size,
    evolutionSpecies: nicksOf.size,
    evolutionMultiNick: [...nicksOf].filter(([, s]) => s.size > 1).map(([n, s]) => ({ species: n, nicks: [...s] })),
  };
}
