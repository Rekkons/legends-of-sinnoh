// 커밋 전에 돌리는 정적 검사. 컴파일·시험을 기다리지 않고 미커밋 변경을 1초 안에 본다(isekai Tools/precheck.mjs의 틀).
//   node Tools/precheck.mjs                 git 미커밋 변경 파일(수정·추가·추적 안 됨)
//   node Tools/precheck.mjs <파일…>          그 파일만(프로젝트 루트 기준 또는 절대 경로, 저장소 밖·없는 파일은 경고하고 건너뜀)
//   node Tools/precheck.mjs --self-test     해석 함수 자기 시험
// 「파일:줄 [규칙] 내용」을 찍는다. 오류가 하나라도 있으면 종료 코드 1, 경고만이면 0, git status 실패는 2.
// 규칙(대상은 Assets/**/*.cs 코드 — 주석·문자열 밖):
//   (a) engine   Rules/·Ai/·Data/·Fx/·Tests/EditMode/에 UnityEngine·UnityEditor — dotnet test가 같은 소스를 엔진 없이 컴파일한다
//   (b) input    옛 입력 UnityEngine.Input·Input.GetKey 등 — activeInputHandler 1(새 Input System만)이라 컴파일은 되고 실행 중 예외
//   (c) testname Tests/** 시험([Test]·[UnityTest]·[TestCase…]) 이름이 <근거ID>_<내용>(Docs/plan.md §2)
//       ignore   Tests/** [Ignore]·TestCase Ignore= 이유가 근거 ID로 시작
//   (d) cs9      record 선언·init 접근자 — IsExternalInit이 없어 Unity에서 안 된다(net8.0인 dotnet test는 통과해 버린다)
//   (e) determinism Rules/·Ai/·Data/에 부동소수(float·double·리터럴)·Random·시계(DateTime·Stopwatch·TickCount)·Guid·프로세스마다 다른 해시
//                (HashCode·GetHashCode 호출) — 같은 시드 + 같은 명령 → 같은 상태(01 §0, plan §1). Dictionary 순회·정적 가변 필드는 못 잡는다
// 규칙(대상은 Gen/ 밖 .unity·.prefab·.asset·.mat — 인자가 없으면 미커밋 변경만이 아니라 추적 파일 전부 + 새 파일):
//   (f) ref      Assets/Sinnoh/Gen/ 안 파일의 GUID(디스크의 Gen/**/*.meta, git 밖 Figures/ 포함)를 가리킴 — 추적하는 씬·프리팹·에셋은
//                Gen/을 직렬화로 참조하지 않는다. 본체가 git 밖이라 새 작업 트리에서 깨진다. 화면 코드가 키로 읽는다(03 §0·§7 「참조」)
// 공용 UI 키트(결정 51 — 별도 저장소 ../uikit, Packages/manifest.json의 com.rekkon.uikit = file:../../uikit)의 *.cs도 본다. 줄 머리에 「uikit:」.
//   인자가 없으면 키트 저장소의 미커밋 변경, 인자로는 키트 안 파일의 경로(절대 또는 프로젝트 루트 기준 ../uikit/…).
//   (a) engine은 Runtime/Core/·Tests/Core/(dotnet test가 엔진 없이 컴파일), (b)·(d)는 전부, (c)는 Tests/. (e)는 걸지 않는다(트윈·이징은 부동소수를 쓴다).
//   키트 저장소가 그 자리에 없으면 경고만 하고 건너뛴다(관문은 종료 2로 막는다).
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { linkedRepos, linkedProblem } from './lib/tree.mjs';

// 스크립트 위치에서 루트를 잡는다(작업 트리를 따로 만들어도 그 트리를 본다)
const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
// Tools/dotnet-test가 엔진 없이 컴파일하는 곳. Data/도 처음부터 엔진 무관으로 만든다(plan §1)
// Fx/(연출 층의 순수 부분 — 화면 대기열·표시 상태)는 엔진만 금지한다. 결정성 검사는 걸지 않는다(규칙 상태를 만들지 않는다)
const ENGINE_FREE = ['Assets/Sinnoh/Rules/', 'Assets/Sinnoh/Ai/', 'Assets/Sinnoh/Data/', 'Assets/Sinnoh/Fx/', 'Assets/Sinnoh/Tests/EditMode/'];
const TESTS = 'Assets/Sinnoh/Tests/';
// 결정성 검사 대상(plan §1 금지 목록). 시험은 빼서 χ² 같은 계산에 double을 쓸 수 있다
const DETERMINISTIC = ['Assets/Sinnoh/Rules/', 'Assets/Sinnoh/Ai/', 'Assets/Sinnoh/Data/'];
// 검사 구역: 이 저장소(Assets/**/*.cs)와 공용 UI 키트 저장소(모든 *.cs). base = 그 저장소 루트, label = 출력 줄 머리
const OWN = { base: ROOT, label: '', code: 'Assets/', engineFree: ENGINE_FREE, deterministic: DETERMINISTIC, tests: TESTS };
const KIT_NAME = 'com.rekkon.uikit';
const kitArea = dir => ({ base: dir, label: 'uikit:', code: '', engineFree: ['Runtime/Core/', 'Tests/Core/'], deterministic: [], tests: 'Tests/' });
// 근거 ID(plan §2): rulings A1·A20_1·G4, automation R1·B1, rules-summary S7_3·S7_3_2, 근거 없는 구조 시험 M0
const ID = String.raw`(?:A\d+(?:_\d+)?|G\d+|R\d+|B\d+|S\d+_\d+(?:_\d+)?|M\d+)`;
const TEST_NAME = new RegExp(`^${ID}_[^_]`, 'u');
const IGNORE_REASON = new RegExp(`^${ID}(?![\\p{L}\\p{N}])`, 'u');

// ── C# 훑기: 주석·문자열을 같은 길이 공백으로 지운 코드와 리터럴 내용(isekai에서 그대로) ──
function scanCs(s) {
  const code = s.split('');
  const literals = [];
  const blank = (a, b) => { for (let k = a; k < b && k < s.length; k++) if (s[k] !== '\n') code[k] = ' '; };
  let i = 0;
  const n = s.length;
  while (i < n) {
    const c = s[i], d = i + 1 < n ? s[i + 1] : '';
    if (c === '/' && d === '/') { const e = s.indexOf('\n', i); const end = e < 0 ? n : e; blank(i, end); i = end; continue; }
    if (c === '/' && d === '*') { const e = s.indexOf('*/', i + 2); const end = e < 0 ? n : e + 2; blank(i, end); i = end; continue; }
    let verbatim = false, start = -1;
    if (c === '"') start = i + 1;
    else if (c === '@' && d === '"') { verbatim = true; start = i + 2; }
    else if (c === '$' && d === '"') start = i + 2;
    else if ((c === '$' && d === '@') || (c === '@' && d === '$')) { if (s[i + 2] === '"') { verbatim = true; start = i + 3; } }
    if (start >= 0) {
      let text = '', j = start;
      while (j < n) {
        const x = s[j];
        if (verbatim) { if (x === '"') { if (s[j + 1] === '"') { text += '"'; j += 2; continue; } break; } }
        else {
          if (x === '"' || x === '\n') break;
          if (x === '\\' && j + 1 < n) { text += s[j + 1]; j += 2; continue; }
        }
        text += x; j++;
      }
      literals.push({ text, at: start });
      blank(start, j);
      i = j + 1;
      continue;
    }
    if (c === "'") {
      let text = '', j = i + 1;
      while (j < n && s[j] !== "'" && s[j] !== '\n') {
        if (s[j] === '\\' && j + 1 < n) { text += s[j + 1]; j += 2; continue; }
        text += s[j++];
      }
      literals.push({ text, at: i });
      blank(i + 1, j);
      i = j + 1;
      continue;
    }
    i++;
  }
  return { code: code.join(''), literals };
}

const lineAt = (s, idx) => { let l = 1; for (let k = 0; k < idx && k < s.length; k++) if (s[k] === '\n') l++; return l; };

// 줄마다 첫 일치 하나씩
function eachLine(code, re, f) {
  code.split('\n').forEach((l, i) => { const m = re.exec(l); if (m) f(m, i + 1); });
}

// 시험 메서드(시험 속성 다음 첫 메서드 선언) → [{name, line}]. 반환형은 가리지 않는다(TestCase ExpectedResult)
const TEST_ATTR = /^\s*\[(?:[^\]]*,)?\s*(?:NUnit\.Framework\.)?(?:UnityTest|TestCaseSource|TestCase|Test)(?:Attribute)?\b/;
const METHOD_DECL = /^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|internal|protected|static|async|virtual|override|new)\s+)*[\p{L}_][\p{L}\p{N}_.]*(?:<[^()]*>)?(?:\[\])?\??\s+([\p{L}\p{N}_]+)\s*(?:<[^()]*>)?\s*\(/u;
function testMethods(src) {
  const out = [];
  let pending = false;
  scanCs(src).code.split('\n').forEach((l, i) => {
    if (TEST_ATTR.test(l)) pending = true;
    const m = METHOD_DECL.exec(l);
    if (m) { if (pending) out.push({ name: m[1], line: i + 1 }); pending = false; }
  });
  return out;
}

// ── 검사 ──
const gitIn = (cwd, ...a) => spawnSync('git', a, { cwd, encoding: 'utf8', maxBuffer: 1 << 26 });
const git = (...a) => gitIn(ROOT, ...a);
const rel = f => path.relative(ROOT, path.resolve(ROOT, f)).replace(/\\/g, '/');
const read = f => readFileSync(path.join(ROOT, f), 'utf8').replace(/\r\n/g, '\n');

const ENGINE_USE = /\bUnity(?:Engine|Editor)\b/;
function checkEngine(file, src, out) {
  eachLine(scanCs(src).code, ENGINE_USE, (m, line) =>
    out.push({ file, line, rule: 'engine', msg: `엔진 참조 ${m[0]} — dotnet test가 이 소스를 엔진 없이 컴파일한다. 엔진을 쓰는 코드·시험은 다른 asmdef로` }));
}

const INPUT_USE = /\bUnityEngine\s*\.\s*Input\b(?!System)|(?<![\w.])Input\s*\.\s*(?:Get[A-Z]\w*|mouse\w*|any\w*|touch\w*|inputString|composition\w*|ime\w*|acceleration\w*|gyro|compass|location|multiTouchEnabled|simulateMouseWithTouches|backButtonLeavesApp|deviceOrientation|ResetInputAxes)\b/;
function checkInput(file, src, out) {
  eachLine(scanCs(src).code, INPUT_USE, (m, line) =>
    out.push({ file, line, rule: 'input', msg: `옛 입력 ${m[0].replace(/\s+/g, '')} — 새 Input System(UnityEngine.InputSystem)만` }));
}

// 선언 자리(줄 머리·; { } ] 뒤 + 한정자)의 record만. 변수 이름 record는 건드리지 않는다
const RECORD_DECL = /(?<=(?:^|[;{}\]])\s*(?:(?:public|private|internal|protected|static|sealed|abstract|partial|readonly|unsafe|new)\s+)*)record\s+(?:class\s+|struct\s+)?[\p{L}_][\p{L}\p{N}_]*\s*[(<{:;]/gmu;
// 접근자 목록 안의 init(; { } ] 또는 접근 한정자 뒤)
const INIT_ACCESSOR = /(?<=(?:[{;}\]]|\b(?:private|protected|internal))\s*)init\s*(?:;|\{|=>)/g;
function checkCs9(file, src, out) {
  const code = scanCs(src).code;
  for (const m of code.matchAll(RECORD_DECL))
    out.push({ file, line: lineAt(code, m.index), rule: 'cs9', msg: 'record 선언 — IsExternalInit이 없어 Unity에서 컴파일되지 않는다. class·struct + 생성자로' });
  for (const m of code.matchAll(INIT_ACCESSOR))
    out.push({ file, line: lineAt(code, m.index), rule: 'cs9', msg: 'init 접근자 — IsExternalInit이 없어 Unity에서 컴파일되지 않는다. 생성자 + get(또는 private set)으로' });
}

// [정규식, 이유]. 줄마다 규칙마다 첫 일치 하나. decimal(1.25m)은 십진 정확 연산이라 통과
const NONDETERMINISM = [
  [/\b(?:float|double)\b|\bSystem\s*\.\s*(?:Single|Double)\b|\bMathF\b/, '부동소수 — 규칙은 int로(level은 ×4처럼)'],
  [/(?<![\w.])(?:\d+\.\d+(?:[eE][+-]?\d+)?|\d+[eE][+-]?\d+)(?![\w.])|(?<![\w.])\d+(?:\.\d+)?[fFdD]\b/, '부동소수 리터럴 — int로(decimal은 m 접미사)'],
  [/(?<!\.\s*)\bRandom\b|\b(?:System|UnityEngine)\s*\.\s*Random\b/, '난수 — IRng(Pcg32)로'],
  [/\bDateTime(?:Offset)?\b|\bStopwatch\b|\bTickCount(?:64)?\b/, '시계 — 엔진은 시계를 읽지 않는다. 시간은 인자로'],
  [/\bGuid\b/, 'Guid — 실행마다 다르다'],
  [/\bHashCode\s*\.|\.\s*GetHashCode\s*\(/, '프로세스마다 다른 해시 — 직접 만든 FNV-1a로'],
];
function checkDeterminism(file, src, out) {
  scanCs(src).code.split('\n').forEach((l, i) => {
    for (const [re, why] of NONDETERMINISM) {
      const m = re.exec(l);
      if (m) out.push({ file, line: i + 1, rule: 'determinism', msg: `${m[0].replace(/\s+/g, '')} — ${why}` });
    }
  });
}

function checkTestNames(file, src, out) {
  for (const t of testMethods(src))
    if (!TEST_NAME.test(t.name))
      out.push({ file, line: t.line, rule: 'testname', msg: `시험 이름 「${t.name}」 — <근거ID>_<내용>(예: S3_1_이웃관계는대칭, A20_4_…, M0_…; Docs/plan.md §2)` });
}

// [Ignore("…")]·[Test, Ignore("…")]·[TestCase(…, Ignore = "…")]. 따옴표 뒤 위치로 리터럴을 찾는다
const IGNORE_USE = /(?<=[\[,]\s*)(?:NUnit\.Framework\.)?(?:IgnoreAttribute|IgnoreReason|Ignore)\b(?:\s*[(=]\s*[@$]{0,2}")?/g;
function checkIgnore(file, src, out) {
  const { code, literals } = scanCs(src);
  for (const m of code.matchAll(IGNORE_USE)) {
    const line = lineAt(code, m.index);
    const lit = m[0].endsWith('"') ? literals.find(l => l.at === m.index + m[0].length) : null;
    if (!lit) out.push({ file, line, rule: 'ignore', msg: 'Ignore 이유 문자열이 없다 — [Ignore("A20_4 미정의")]처럼 근거 ID로 시작' });
    else if (!IGNORE_REASON.test(lit.text.trim())) out.push({ file, line, rule: 'ignore', msg: `Ignore 이유 「${lit.text.slice(0, 60)}」 — 근거 ID로 시작(예: "A20_4 미정의")` });
  }
}

// (f) Gen GUID → 경로(.meta 뺀 것). 본체가 없어도 추적 .meta는 있고, Figures/는 .meta도 git 밖이라 들인 작업 트리에서만 잡힌다
const GEN = 'Assets/Sinnoh/Gen/';
const isRefTarget = f => /\.(?:unity|prefab|asset|mat)$/.test(f) && !f.startsWith(GEN);
function genGuids() {
  const out = new Map();
  const walk = dir => {
    let ents;
    try { ents = readdirSync(path.join(ROOT, dir), { withFileTypes: true }); } catch { return; }
    for (const e of ents) {
      const p = dir + e.name;
      if (e.isDirectory()) walk(p + '/');
      else if (e.name.endsWith('.meta')) {
        const m = /^guid:\s*([0-9a-f]{32})\s*$/m.exec(readFileSync(path.join(ROOT, p), 'utf8'));
        if (m) out.set(m[1], p.slice(0, -'.meta'.length));
      }
    }
  };
  walk(GEN);
  return out;
}

const GUID_REF = /\bguid:\s*([0-9a-f]{32})\b/g;
function checkRefs(file, src, guids, out) {
  src.split('\n').forEach((l, i) => {
    for (const m of l.matchAll(GUID_REF)) {
      const target = guids.get(m[1]);
      if (target) out.push({ file, line: i + 1, rule: 'ref', msg: `Gen GUID ${m[1]}(${target}) — 추적 씬·프리팹·에셋은 Gen/을 직렬화로 참조하지 않는다. 화면 코드가 키로 읽는다(03 §0·§7)` });
    }
  });
}

// 구역 안 파일에 거는 규칙 이름(순서대로)
function rulesFor(area, file) {
  const r = [];
  if (area.engineFree.some(p => file.startsWith(p))) r.push('engine');
  if (area.deterministic.some(p => file.startsWith(p))) r.push('determinism');
  r.push('input', 'cs9');
  if (file.startsWith(area.tests)) r.push('testname', 'ignore');
  return r;
}
const CHECKS = { engine: checkEngine, determinism: checkDeterminism, input: checkInput, cs9: checkCs9, testname: checkTestNames, ignore: checkIgnore };

function checkFile(file, out, area = OWN) {
  if (!file.endsWith('.cs') || !file.startsWith(area.code) || !existsSync(path.join(area.base, file))) return false;
  const src = readFileSync(path.join(area.base, file), 'utf8').replace(/\r\n/g, '\n');
  for (const rule of rulesFor(area, file)) CHECKS[rule](area.label + file, src, out);
  return true;
}

function changedFiles(base = ROOT) {
  // quotePath를 끄지 않으면 한글 경로가 "\352…"로 나온다
  const r = gitIn(base, '-c', 'core.quotePath=false', 'status', '--porcelain', '-uall');
  if (r.status !== 0) { console.error('git status 실패: ' + r.stderr); process.exit(2); }
  return r.stdout.split('\n').filter(Boolean)
    .filter(l => !/^(D.|.D)/.test(l))
    .map(l => l.slice(3).replace(/^.* -> /, '').replace(/^"(.*)"$/, '$1'));
}

function selfTest() {
  const eq = (a, b, what) => { if (JSON.stringify(a) !== JSON.stringify(b)) { console.error(`실패: ${what}\n  기대 ${JSON.stringify(b)}\n  실제 ${JSON.stringify(a)}`); process.exitCode = 1; } };
  const run = (f, src) => { const o = []; f('t.cs', src, o); return o.map(v => v.line); };
  const s = scanCs('var a = "x // y"; // "z"\n/* "w" */ char c = \'q\'; var b = @"a""b";');
  eq(s.literals.map(l => l.text), ['x // y', 'q', 'a"b'], '리터럴(주석 안은 뺌)');
  eq(scanCs('var s = $"{a} \\" x"; Input.GetKey(k);').code.includes('Input.GetKey'), true, '코드는 남김');

  eq(run(checkEngine, 'using System;\nusing UnityEngine;\n// UnityEngine\nvar s = "UnityEditor";\nUnityEditor.AssetDatabase.Refresh();\nvar MyUnityEngineX = 1;\n'), [2, 5], '엔진 참조(주석·문자열·다른 이름 뺌)');

  eq(run(checkInput, 'using UnityEngine.InputSystem;\nvar p = Mouse.current.position;\n'), [], 'InputSystem은 통과');
  eq(run(checkInput, '// UnityEngine.Input.GetKey\nvar s = "Input.GetKey";\nPlayerInput.GetKey(); a.Input.GetKey();\n'), [], '주석·문자열·다른 Input');
  eq(run(checkInput, 'if (Input.GetKeyDown(KeyCode.A)) {}\nvar m = UnityEngine.Input.mousePosition;\n'), [1, 2], '옛 입력 두 줄');

  eq(run(checkCs9, 'public record Foo(int A);\ninternal sealed record struct P\n{\n}\nclass C { public int X { get; init; } }\nclass D { int Y { get; private init { } } }\n'), [1, 2, 5, 6], 'record·init');
  eq(run(checkCs9, 'var record = 1; Use(record); record = 2;\nforeach (var record in rs) {}\nint init; init = 3; return init;\n// public record Foo(int A);\n'), [], '이름 record·init은 통과');

  for (const [n, ok] of [['S3_1_이웃은서로다른6칸', true], ['M0_홀수좌표는거부', true], ['A20_1_해금', true], ['A6_층은floor_level', true],
    ['R9_공격주사위면_0_1_1_1_1_2', true], ['B4_구간여러개', true], ['S7_3_불대문자예시_데미지6', true], ['S7_3_2_x', true], ['S7_7_5번뒤굴림마다3피해', true], ['G4_상한은맨마지막', true],
    ['HexNeighbors', false], ['QA10_x', false], ['D125_x', false], ['A_x', false], ['G_x', false], ['S7_x', false], ['M0__x', false], ['A1', false], ['Base_p15_x', false]])
    eq(TEST_NAME.test(n), ok, `시험 이름 ${n}`);
  eq(testMethods('[Test]\npublic void A1_a() {}\nstatic void Helper() {}\n// [Test]\nvoid NotTest() {}\n[Category("x"), Test]\n[Explicit]\npublic IEnumerator 한글() { }\n'
    + '[TestCase(1, 2,\n    3, ExpectedResult = 4)]\npublic int R9_합(int a, int b, int c) => a;\n[Test] public void 한줄() {}\n[TestFixture]\npublic class X { }\n')
    .map(t => [t.name, t.line]), [['A1_a', 2], ['한글', 8], ['R9_합', 11], ['한줄', 12]], '시험 메서드');
  eq(testMethods('[Test]\npublic void G<T>() {}\n[Test]\npublic void A2_x() {}\n').map(t => [t.name, t.line]), [['G', 2], ['A2_x', 4]], '제네릭 시험 메서드');

  eq(run(checkDeterminism, 'float a = 1;\ndouble b;\nvar c = 1.5;\nvar d = 2f;\nvar e = new System.Random(1);\nvar f = DateTime.Now;\nvar g = Guid.NewGuid();\n'
    + 'int h = s.GetHashCode();\nint i = HashCode.Combine(1, 2);\nvar j = 1e3;\nvar k = Random.Shared.Next();\nvar l = MathF.Sqrt(2);\nvar m = System.Double.Epsilon;\n'
    + 'var n = Stopwatch.StartNew();\nint o = Environment.TickCount;\nvar p = 3d + x;\nvar q = UnityEngine.Random.value;\n'), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17], '결정성 위반');
  eq(run(checkDeterminism, '// float double 1.5 Random DateTime.Now\nvar s = "1.5 double Random Guid";\ndecimal q = 1.25m * 4;\nvar t = 0x1F + 0x1e3 + 10L + 3u + 7UL;\n'
    + 'var u = x.Item1;\nvar r = a[1..9];\npublic override int GetHashCode() => (Col * 397) ^ Row;\nvar rng = new Pcg32(42, 54);\nvar mode = TieBreak.Random;\n'
    + 'int floatCount = 2, doubled = 3;\nvar fp = FloatParseHandling.Decimal;\nvar v = 0.ToString();\n'), [], '결정성 통과(주석·문자열·decimal·정수 리터럴·범위·override·다른 Random)');

  eq(run(checkIgnore, '[Test, Ignore("A20_4 미정의")]\n[Ignore("미정")]\n[Ignore]\nAssert.Ignore("x");\n[TestCase(1, Ignore = "나중")]\n[TestCase(1, Ignore = "R7 대기")]\n[Ignore(Reasons.X)]\n[Ignore(@"S7_3 확인")]\n'), [2, 3, 5, 7], 'Ignore 이유');
  for (const [r, ok] of [['A20_4 미정의', true], ['M1 구조', true], ['S7_3: 확인', true], ['A6_층차 판정', true], ['미정', false], ['A20x', false], ['QA10 x', false]])
    eq(IGNORE_REASON.test(r), ok, `Ignore 이유 ${r}`);

  const gen = new Map([['0123456789abcdef0123456789abcdef', GEN + 'Board/hex_Standard.png']]);
  const refs = src => { const o = []; checkRefs('t.unity', src, gen, o); return o.map(v => v.line); };
  eq(refs('  m_Texture: {fileID: 2800000, guid: 0123456789abcdef0123456789abcdef, type: 3}\n  m_Script: {fileID: 11500000, guid: 44eeaeb5cdf54af40b4ea3f2bd3af5aa, type: 3}\n'
    + '  a: {guid: 0123456789abcdef0123456789abcdef} b: {guid: 0123456789abcdef0123456789abcdef}\n  guid: 0123456789abcdef0123456789abcdef\n  x: 0123456789abcdef0123456789abcdef\n'), [1, 3, 3, 4], 'Gen GUID 참조(다른 GUID·guid: 없는 같은 글자는 통과)');
  for (const [f, ok] of [['Assets/Sinnoh/Scenes/Board.unity', true], ['Assets/x.prefab', true], ['Assets/Settings/PC_RPAsset.asset', true], ['ProjectSettings/EditorBuildSettings.asset', true],
    ['Assets/Sinnoh/Art/Authored/a.mat', true], ['Assets/Sinnoh/Gen/Figures/793_0/793_0.prefab', false], ['Assets/Sinnoh/Gen/x.mat', false], ['Assets/a.cs', false], ['Assets/a.unity.meta', false]])
    eq(isRefTarget(f), ok, `참조 검사 대상 ${f}`);
  const kit = kitArea('x');
  eq([rulesFor(OWN, 'Assets/Sinnoh/Rules/A.cs'), rulesFor(OWN, 'Assets/Sinnoh/Client/B.cs'), rulesFor(OWN, 'Assets/Sinnoh/Tests/EditMode/T.cs'), rulesFor(OWN, 'Assets/Sinnoh/Tests/PlayMode/P.cs'), rulesFor(OWN, 'Assets/Sinnoh/Fx/Q.cs')],
    [['engine', 'determinism', 'input', 'cs9'], ['input', 'cs9'], ['engine', 'input', 'cs9', 'testname', 'ignore'], ['input', 'cs9', 'testname', 'ignore'], ['engine', 'input', 'cs9']], '구역 규칙(이 저장소)');
  eq([rulesFor(kit, 'Runtime/Core/KoText.cs'), rulesFor(kit, 'Runtime/UiKit/Tip.cs'), rulesFor(kit, 'Tests/Core/KoTextTests.cs'), rulesFor(kit, 'Tests/Editor/TipTests.cs')],
    [['engine', 'input', 'cs9'], ['input', 'cs9'], ['engine', 'input', 'cs9', 'testname', 'ignore'], ['input', 'cs9', 'testname', 'ignore']], '구역 규칙(키트: Core는 엔진 금지, 결정성 검사 없음)');
  console.log(process.exitCode ? 'precheck 자기 시험 실패' : 'precheck 자기 시험 통과');
}

const argv = process.argv.slice(2);
if (argv.includes('--self-test')) selfTest();
else {
  const out = [];
  const files = [];
  // 공용 UI 키트 저장소(있을 때만). 그 자리에 없으면 경고하고 이 저장소만 본다
  let kit = null;
  const kitFiles = [];
  const kitRepo = linkedRepos(ROOT).find(l => l.name === KIT_NAME);
  if (kitRepo) {
    const problem = linkedProblem(kitRepo);
    if (problem) out.push({ file: 'uikit', line: 0, rule: 'skip', warn: true, msg: `${problem} — 키트 검사를 건너뛴다` });
    else kit = kitArea(kitRepo.dir);
  }
  const inKit = a => { if (!kit) return null; const k = path.relative(kit.base, path.resolve(ROOT, a)).replace(/\\/g, '/'); return k.startsWith('../') || path.isAbsolute(k) ? null : k; };
  if (argv.length) {
    for (const a of argv) {
      const f = rel(a), k = inKit(a);
      if (k !== null) { if (existsSync(path.join(kit.base, k))) kitFiles.push(k); else out.push({ file: kit.label + k, line: 0, rule: 'skip', warn: true, msg: '없는 파일' }); }
      else if (f.startsWith('../') || path.isAbsolute(f)) out.push({ file: a, line: 0, rule: 'skip', warn: true, msg: '저장소 밖 — 검사하지 않는다' });
      else if (!existsSync(path.join(ROOT, f))) out.push({ file: f, line: 0, rule: 'skip', warn: true, msg: '없는 파일' });
      else files.push(f);
    }
  } else { files.push(...changedFiles()); if (kit) kitFiles.push(...changedFiles(kit.base)); }
  const uniq = files.filter((f, i, a) => a.indexOf(f) === i);
  const kitUniq = kitFiles.filter((f, i, a) => a.indexOf(f) === i);
  let checked = 0;
  for (const f of uniq) if (checkFile(f, out)) checked++;
  for (const f of kitUniq) if (checkFile(f, out, kit)) checked++;
  // (f) 인자가 없으면 추적 파일 전부 + 새 파일(Gen/ 참조는 옛 파일에서도 새로 생길 수 있다 — Gen을 다시 만들면 GUID가 바뀐다)
  const refFiles = (argv.length ? uniq : [...git('-c', 'core.quotePath=false', 'ls-files').stdout.split('\n'), ...uniq])
    .filter((f, i, a) => f && isRefTarget(f) && a.indexOf(f) === i && existsSync(path.join(ROOT, f)));
  const guids = refFiles.length ? genGuids() : new Map();
  for (const f of refFiles) checkRefs(f, read(f), guids, out);
  out.sort((a, b) => a.file.localeCompare(b.file) || a.line - b.line);
  for (const v of out) console.log(`${v.file}${v.line ? ':' + v.line : ''} [${v.rule}]${v.warn ? ' 경고' : ''} ${v.msg}`);
  const errors = out.filter(v => !v.warn).length;
  console.log(`precheck: 검사 파일 ${checked}개(대상 ${uniq.length + kitUniq.length}개${kit ? `, 키트 ${kitUniq.length}` : ''}), 참조 검사 ${refFiles.length}개(Gen GUID ${guids.size}), 오류 ${errors}, 경고 ${out.length - errors}`);
  process.exit(errors ? 1 : 0);
}
