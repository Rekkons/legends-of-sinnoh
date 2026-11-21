using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Sinnoh.Data
{
    /// <summary>
    /// 튜토리얼 목차의 장 하나(M6.5 「화면 ② 목차·입구」, 07 §2-1·§9-3). 본편 장 = 각본 main의 장 번호 하나, 확장 장 = 각본 하나 통째(<see cref="Chapter"/> −1).
    /// </summary>
    public sealed class TutorialChapter
    {
        /// <summary>장 id(`main0`~ · `e01`~) — 기록(<see cref="TutorialRecord"/>)의 키 끝.</summary>
        public string Id;
        /// <summary>각본 id(`Data/Json/tutorial/&lt;id&gt;.json`).</summary>
        public string Script;
        /// <summary>각본 안의 장 번호(단계의 ch). 확장 장은 −1(각본 통째).</summary>
        public int Chapter;
        /// <summary>확장 장 칸의 그림(Gen 키). 본편 장은 null.</summary>
        public string Icon;
        /// <summary>장 이름의 문자열 키(`screen.tutorial.menu.ch.&lt;id&gt;`).</summary>
        public string Name;
        /// <summary>그 장의 단계 수(<see cref="Then"/> 각본의 단계를 더한 값). 0 = 아직 각본이 없다(목차는 「준비 중」).</summary>
        public int Steps;
        /// <summary>
        /// 본편 장이 <see cref="Script"/>의 그 장 단계 뒤에 차례로 이어 여는 각본(07 T54 — 막 맛보기 7장 = preview2 → preview3). 각본마다 판을 그 각본의 start로 새로 연다. 대개 빈 목록.
        /// </summary>
        public IReadOnlyList<string> Then = Array.Empty<string>();
        /// <summary>
        /// 그 장과 관련된 룰 설명서 절 키 1~4개(<c>Sinnoh.Data.Rulebook</c> 절 키 — 앞 키가 먼저 열 절, M9 「튜토리얼 잇기」 [가정 07 T212]).
        /// 장 끝의 「설명서에서 더 보기」(설명서 화면 S43을 합친 뒤 메인이 잇는다)가 쓴다.
        /// </summary>
        public IReadOnlyList<string> Rulebook = Array.Empty<string>();

        public bool IsMain => Chapter >= 0;
        public bool Ready => Steps > 0;

        /// <summary>그 각본의 단계가 이 장에 드나(확장 장은 모든 단계).</summary>
        public bool Holds(int stepChapter) => Chapter < 0 || stepChapter == Chapter;

        /// <summary>이 장에서 그 각본 다음에 이어 열 각본(<see cref="Then"/> — <see cref="Script"/>이면 첫 각본). 없으면 null(이 장의 끝).</summary>
        public string After(string script)
        {
            if (script == Script) return Then.Count > 0 ? Then[0] : null;
            for (int i = 0; i + 1 < Then.Count; i++)
                if (Then[i] == script) return Then[i + 1];
            return null;
        }

        public override string ToString() => Id;
    }

    /// <summary>
    /// 목차의 장 머리 표(`Data/Json/tutorial_chapters.json` — build.mjs가 `Tools/data/authored/tutorial-chapters.json`과 각본에서 만든다). 엄격하다:
    /// 빠진·모르는 필드 · 형이 다른 값 · id 겹침 · id 문법(`main&lt;n&gt;` · `e&lt;nn&gt;`) · 본편 장의 각본이 main이 아님 · 이름 키가 `screen.tutorial.menu.ch.&lt;id&gt;`가 아님 ·
    /// 이어 여는 각본(`then`)이 확장 장에 있거나 · main·`e&lt;nn&gt;`·각본 id 문법 밖이거나 · 두 번 나옴 · 설명서 절 키(`rulebook`)가 1~4개 장.절 꼴이 아님 = FormatException. 순서는 표 그대로(본편 장 → 확장 장).
    /// </summary>
    public static class TutorialChapters
    {
        public const string MainScript = "main";
        static readonly Regex MainId = new Regex(@"^main[0-9]+$", RegexOptions.CultureInvariant), ExtraId = new Regex(@"^e[0-9]{2}$", RegexOptions.CultureInvariant),
            ScriptId = new Regex(@"^[a-z0-9]+$", RegexOptions.CultureInvariant);

        /// <summary>룰 설명서 절 키 문법(<c>장.절</c> — <c>Sinnoh.Data.Rulebook</c>의 절 키와 같은 꼴).</summary>
        public static readonly Regex RulebookKey = new Regex(@"^[a-z]+(?:_[a-z]+)*\.[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant);

        [JsonObject(ItemRequired = Required.Always)]
        sealed class Row
        {
            public string Id, Script, Name;
            public int Ch, Steps;
            [JsonProperty(Required = Required.AllowNull)] public string Icon;
            public List<string> Then, Rulebook;
        }

        public static List<TutorialChapter> Load(string json, string file = "tutorial_chapters.json")
        {
            var rows = TableJson.Parse<Row>(json, file);
            var list = new List<TutorialChapter>(rows.Count);
            var ids = new HashSet<string>();
            var thens = new HashSet<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                string at = $"{file} 행 {i} {r.Id}";
                bool main = r.Ch >= 0;
                if (!ids.Add(r.Id)) throw new FormatException(at + ": id가 겹친다");
                if (main ? r.Script != MainScript || r.Id != MainScript + r.Ch || r.Icon != null : r.Ch != -1 || !ExtraId.IsMatch(r.Id) || r.Script != r.Id)
                    throw new FormatException(at + ": 본편 장은 main<ch>·각본 main·그림 없음, 확장 장은 e<nn>·각본 = id·ch −1");
                if (main && !MainId.IsMatch(r.Id)) throw new FormatException(at + ": id 문법");
                if (r.Name != "screen.tutorial.menu.ch." + r.Id) throw new FormatException(at + ": 이름 키 " + r.Name);
                if (r.Steps < 0) throw new FormatException(at + ": steps " + r.Steps);
                if (r.Then == null || (!main && r.Then.Count > 0)) throw new FormatException(at + ": then은 배열이고 본편 장만 쓴다");
                foreach (string t in r.Then)
                    if (t == null || !ScriptId.IsMatch(t) || t == MainScript || ExtraId.IsMatch(t) || !thens.Add(t)) throw new FormatException(at + ": then 각본 " + t);
                if (r.Rulebook == null || r.Rulebook.Count < 1 || r.Rulebook.Count > 4 || r.Rulebook.Exists(k => k == null || !RulebookKey.IsMatch(k)) || new HashSet<string>(r.Rulebook).Count != r.Rulebook.Count)
                    throw new FormatException(at + ": rulebook은 설명서 절 키 1~4개(장.절 꼴, 겹침 없음)");
                list.Add(new TutorialChapter { Id = r.Id, Script = r.Script, Chapter = r.Ch, Icon = r.Icon, Name = r.Name, Steps = r.Steps, Then = r.Then.ToArray(), Rulebook = r.Rulebook.ToArray() });
            }
            return list;
        }

        /// <summary>그 각본의 그 단계 장 번호가 드는 장(이어 여는 각본 <see cref="TutorialChapter.Then"/>이면 그 장 — 단계 장 번호를 보지 않는다). 없으면 null.</summary>
        public static TutorialChapter Of(IReadOnlyList<TutorialChapter> chapters, string script, int stepChapter)
        {
            foreach (var c in chapters)
                if ((c.Script == script && c.Holds(stepChapter)) || Contains(c.Then, script)) return c;
            return null;
        }

        static bool Contains(IReadOnlyList<string> list, string s)
        {
            foreach (string x in list)
                if (x == s) return true;
            return false;
        }

        public static TutorialChapter Find(IReadOnlyList<TutorialChapter> chapters, string id)
        {
            foreach (var c in chapters)
                if (c.Id == id) return c;
            return null;
        }

        /// <summary>본편의 마지막 장(타이틀 「처음이라면!」 딱지가 이 장을 끝까지 볼 때까지 붙는다 — 07 T3).</summary>
        public static TutorialChapter LastMain(IReadOnlyList<TutorialChapter> chapters)
        {
            TutorialChapter last = null;
            foreach (var c in chapters)
                if (c.IsMain) last = c;
            return last;
        }

        /// <summary>같은 각본에서 그 장 다음의 고를 수 있는 장(단계가 있는 것). 없으면 null(각본의 끝).</summary>
        public static TutorialChapter Next(IReadOnlyList<TutorialChapter> chapters, TutorialChapter from)
        {
            if (from == null || !from.IsMain) return null;
            foreach (var c in chapters)
                if (c.Script == from.Script && c.Chapter > from.Chapter && c.Ready) return c;
            return null;
        }
    }
}
