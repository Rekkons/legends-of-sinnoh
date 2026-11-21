// 손으로 쓰는 JSON(Tools/data/authored·effects)의 겹친 키 찾기. JSON.parse는 같은 객체 안의 같은 키를 조용히 뒤의 것으로 덮는다
// (screens.json의 `screen.battle.guide.pick`이 두 번 정의돼 「기술을 선택하십시오.」가 덮였던 것 — M5 「맵 결과·배틀 안 연출(합침)」).
// 원문을 훑어 같은 객체 안에서 두 번 나온 키를 낸다: [{ key, line, first }] (line·first = 1부터 센 줄 번호, 둘째·첫째 자리).
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';

export function duplicateKeys(text) {
  const found = [];
  const stack = [];   // 객체면 Map(키 → 줄), 배열이면 null
  let line = 1;
  let expectKey = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === '\n') line++;
    else if (c === '{') { stack.push(new Map()); expectKey = true; }
    else if (c === '[') { stack.push(null); expectKey = false; }
    else if (c === '}' || c === ']') { stack.pop(); expectKey = false; }
    else if (c === ',') expectKey = stack.length > 0 && stack[stack.length - 1] !== null;
    else if (c === '"') {
      let j = i + 1;
      while (j < text.length && text[j] !== '"') j += text[j] === '\\' ? 2 : 1;
      if (expectKey) {
        let key;
        try { key = JSON.parse(text.slice(i, j + 1)); } catch { key = text.slice(i + 1, j); }
        const seen = stack[stack.length - 1];
        if (seen.has(key)) found.push({ key, line, first: seen.get(key) });
        else seen.set(key, line);
        expectKey = false;
      }
      i = j;
    }
  }
  return found;
}

/** 폴더(하위 포함)·파일들의 .json을 훑어 겹친 키마다 errors에 한 줄. rels = 저장소 기준 경로(폴더나 파일). */
export function checkDuplicateKeys(root, rels, errors) {
  const walk = rel => {
    const abs = path.join(root, rel);
    if (!rel.endsWith('.json')) return readdirSync(abs, { withFileTypes: true }).flatMap(d => (d.isDirectory() || d.name.endsWith('.json') ? walk(rel + '/' + d.name) : []));
    return [rel];
  };
  let files = 0;
  for (const rel of rels.flatMap(walk)) {
    files++;
    for (const d of duplicateKeys(readFileSync(path.join(root, rel), 'utf8'))) errors.push(`${rel}:${d.line}: 겹친 키 ${JSON.stringify(d.key)}(첫 정의 ${d.first}줄) — 뒤의 것이 앞의 것을 덮는다`);
  }
  return files;
}

/** 자기 시험: 실패한 줄 목록(비면 통과). */
export function selfTestDuplicateKeys() {
  const bad = [];
  const eq = (name, got, want) => { if (JSON.stringify(got) !== JSON.stringify(want)) bad.push(`${name}: ${JSON.stringify(got)} ≠ ${JSON.stringify(want)}`); };
  eq('겹침 없음', duplicateKeys('{"a":1,"b":{"a":2},"c":[{"a":1},{"a":2}]}'), []);
  eq('같은 객체의 같은 키', duplicateKeys('{\n"a":1,\n"b":2,\n"a":3}'), [{ key: 'a', line: 4, first: 2 }]);
  eq('안쪽 객체', duplicateKeys('{"s":{"k":{"ko":"x"},\n"k":{"ko":"y"}}}'), [{ key: 'k', line: 2, first: 1 }]);
  eq('값 문자열은 키가 아니다', duplicateKeys('{"a":"a","b":"a","c":["a","a"],"d":"{\\"a\\":1,\\"a\\":2}"}'), []);
  eq('이스케이프한 키', duplicateKeys('{"a\\"b":1,"a\\"b":2,"\\u0061":3,"a":4}').map(d => d.key), ['a"b', 'a']);
  eq('배열 안 객체 뒤의 키', duplicateKeys('{"a":[{"x":1}],"x":2,"a":3}').map(d => d.key), ['a']);
  return bad;
}
