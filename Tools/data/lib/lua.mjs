// 언어 모듈(ff1ce0·module_language_english)의 `vars = { 묶음 = { 키 = "값", … }, … }` 표만 읽는 작은 파서(03 §1-1).
// 주석(--)·이름·문자열·{ } = ,만 안다. 그 밖의 것(수, 함수, 3단 이상 표, 모르는 이스케이프)은 오류다.
const ESC = { n: '\n', t: '\t', '\\': '\\', '"': '"', "'": "'" };

function tokenize(src, file) {
  const toks = [];
  let i = 0, line = 1;
  const err = msg => { throw new Error(`${file}:${line} ${msg}`); };
  while (i < src.length) {
    const c = src[i];
    if (c === '\n') { line++; i++; continue; }
    if (/\s/.test(c)) { i++; continue; }
    if (c === '-' && src[i + 1] === '-') {
      if (src.startsWith('--[[', i)) err('긴 주석(--[[)은 읽지 않는다');
      while (i < src.length && src[i] !== '\n') i++;
      continue;
    }
    if (c === '"' || c === "'") {
      let s = '', j = i + 1;
      for (; ; j++) {
        if (j >= src.length || src[j] === '\n') err('문자열이 줄 안에서 닫히지 않는다');
        if (src[j] === c) break;
        if (src[j] === '\\') {
          const e = src[++j];
          if (!(e in ESC)) err(`모르는 이스케이프 \\${e}`);
          s += ESC[e];
        } else s += src[j];
      }
      toks.push({ t: 'str', v: s, line });
      i = j + 1;
      continue;
    }
    if (/[A-Za-z_]/.test(c)) {
      let j = i;
      while (j < src.length && /[A-Za-z0-9_]/.test(src[j])) j++;
      toks.push({ t: 'id', v: src.slice(i, j), line });
      i = j;
      continue;
    }
    if ('{}=,'.includes(c)) { toks.push({ t: c, line }); i++; continue; }
    err(`모르는 글자 ${JSON.stringify(c)}`);
  }
  return toks;
}

/** `vars = { … }` → { 묶음: { 키: 값 } }. 묶음·키 순서는 파일 순서. 겹치는 이름은 오류. */
export function parseLanguageModule(src, file) {
  const toks = tokenize(src, file);
  let p = 0;
  const err = (msg, tok = toks[p]) => { throw new Error(`${file}:${tok ? tok.line : 'EOF'} ${msg}`); };
  const take = (t, v) => {
    const tok = toks[p];
    if (!tok || tok.t !== t || (v !== undefined && tok.v !== v)) err(`${v ?? t}이 와야 한다`);
    p++;
    return tok;
  };
  const table = depth => {
    take('{');
    const out = {};
    while (toks[p] && toks[p].t !== '}') {
      const name = take('id').v;
      take('=');
      if (Object.prototype.hasOwnProperty.call(out, name)) err(`이름 ${name}이 겹친다`);
      if (toks[p] && toks[p].t === 'str') out[name] = take('str').v;
      else if (depth === 0 && toks[p] && toks[p].t === '{') out[name] = table(1);
      else err(depth === 0 ? '묶음 표가 와야 한다' : '문자열이 와야 한다');
      if (toks[p] && toks[p].t === ',') p++;
      else break;
    }
    take('}');
    return out;
  };
  take('id', 'vars');
  take('=');
  const vars = table(0);
  if (p !== toks.length) err('vars 표 뒤에 다른 것이 있다');
  for (const [g, v] of Object.entries(vars)) if (typeof v !== 'object') err(`묶음 ${g}이 표가 아니다`, null);
  return vars;
}
