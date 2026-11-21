using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Sinnoh.Rules;
using Sinnoh.Rules.Tutorial;

namespace Sinnoh.Data
{
    /// <summary>
    /// 각본 JSON(build.mjs가 `Tools/data/authored/tutorial/&lt;id&gt;.json`에서 만든 `Data/Json/tutorial/&lt;id&gt;.json`, 07 §7) → <see cref="TutorialScript"/>. 경로는 받지 않는다(01 §11).
    /// 엄격하다: 빠진·모르는 필드, 같은 필드 두 번, 형이 다른 값, 모르는 advance·선택·답·정비·주사위 면·덱·캠페인, expect 키 문법(<see cref="TutorialExpect.IsKey"/>),
    /// 단계 id 겹침, 대사 키가 `tutorial.&lt;각본&gt;.&lt;단계&gt;`가 아님, advance와 allow·auto·봇·화면 칸의 짝이 틀림 = FormatException.
    /// 답 표기의 target·value에 적은 키는 여기서 색인으로 바꾼다: `pokemon.&lt;키&gt;` · `trainer_card.&lt;키&gt;` · `move.&lt;키&gt;` · 그 밖은 고정물·아이템·카드 표의 키(한 표에만 있어야 한다).
    /// 대사의 내용(근거 · 숫자 대조 · 말투 · 길이 · 앵커 문법)은 여기서 보지 않는다 — 데이터 검사 시험(M65_데이터검사)이 본다.
    /// </summary>
    public static class TutorialLoader
    {
        public const int Schema = 1;
        /// <summary>각본 id·단계 id·막 앞 판 손보기 이름·화면 이름의 문법.</summary>
        public static readonly Regex ScriptId = new Regex(@"^[a-z0-9]+$", RegexOptions.CultureInvariant);
        public static readonly Regex StepId = new Regex(@"^[A-Za-z0-9]+$", RegexOptions.CultureInvariant);
        /// <summary>화면 요소 이름(앵커 · screen:&lt;이름&gt; · after) — `board-hud-act-end_turn`처럼 소문자·숫자를 `-`·`_`로 잇는다.</summary>
        public static readonly Regex Name = new Regex(@"^[a-z0-9]+(?:[-_][a-z0-9]+)*$", RegexOptions.CultureInvariant);

        static readonly string[] RootFields = { "schema", "id", "start", "bots", "steps" };
        static readonly string[] StartFields = { "campaign", "seats", "seed", "trainerCards", "tops", "spots", "after" };
        static readonly string[] StepFields = { "id", "ch", "text", "hint", "anchor", "advance", "screen", "botSeat", "botAnswers", "allow", "auto", "dice", "expect", "ref" };
        static readonly string[] PatternFields = { "choice", "kind", "target", "arg", "values", "maint", "party", "slot", "value" };

        public static TutorialScript Load(Stream stream, Ctx ctx, string file) => Load(TableJson.ReadAll(stream, nameof(stream)), ctx, file);

        /// <param name="file">오류에 붙일 이름(예: "tutorial/main.json").</param>
        public static TutorialScript Load(string json, Ctx ctx, string file)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            JObject root;
            try { root = TableJson.LoadToken(json ?? throw new ArgumentNullException(nameof(json)), file) as JObject; }
            catch (Newtonsoft.Json.JsonException e) { throw new FormatException($"{file}: {e.Message}", e); }
            if (root == null) throw new FormatException($"{file}: 객체가 아니다");
            Only(root, file, RootFields);
            if (Int(root, "schema", file) != Schema) throw new FormatException($"{file} schema 기대 {Schema}");
            var s = new TutorialScript { Id = Str(root, "id", file) };
            if (!ScriptId.IsMatch(s.Id)) throw new FormatException($"{file}: 각본 id \"{s.Id}\"");
            s.Start = ReadStart(Obj(root, "start", file), ctx, file + " start");
            s.Bots = ReadBots(root["bots"], s.Start.Seats, ctx, file + " bots");
            var ids = new HashSet<string>();
            var steps = Arr(root, "steps", file);
            if (steps.Count == 0) throw new FormatException($"{file}: 단계가 없다");
            for (int i = 0; i < steps.Count; i++)
            {
                string where = $"{file} steps[{i}]";
                var step = ReadStep(steps[i] as JObject ?? throw new FormatException($"{where}: 객체가 아니다"), s, ctx, where);
                if (!ids.Add(step.Id)) throw new FormatException($"{where}: 단계 id {step.Id}가 겹친다");
                s.Steps.Add(step);
            }
            return s;
        }

        static TutorialStart ReadStart(JObject o, Ctx ctx, string where)
        {
            Only(o, where, StartFields);
            var st = new TutorialStart
            {
                Campaign = TableJson.ParseEnum<Campaign>(Str(o, "campaign", where), where),
                Seats = Int(o, "seats", where),
                Seed = (ulong)Long(o, "seed", where),
            };
            if (st.Seats < 1 || st.Seats > 6) throw new FormatException($"{where}: seats {st.Seats}(1~6)");
            if (o["trainerCards"].Type != JTokenType.Null)
            {
                var per = Arr(o, "trainerCards", where);
                st.TrainerCards = new string[per.Count][];
                for (int i = 0; i < per.Count; i++) st.TrainerCards[i] = Strings(per[i], $"{where}.trainerCards[{i}]");
            }
            foreach (var t in Arr(o, "tops", where))
            {
                var to = t as JObject ?? throw new FormatException($"{where}.tops: 객체가 아니다");
                Only(to, where + ".tops", "deck", "keys");
                st.Tops.Add(new DeckTop { Deck = TableJson.ParseEnum<DeckId>(Str(to, "deck", where), where + ".tops"), Keys = Strings(to["keys"], where + ".tops.keys") });
            }
            foreach (var t in Arr(o, "spots", where))
            {
                var so = t as JObject ?? throw new FormatException($"{where}.spots: 객체가 아니다");
                Only(so, where + ".spots", "cell", "item");
                st.Spots.Add(new SpotFix { Cell = Int(so, "cell", where), Item = Str(so, "item", where) });
            }
            if (o["after"].Type != JTokenType.Null)
            {
                st.After = Str(o, "after", where);
                if (!Name.IsMatch(st.After)) throw new FormatException($"{where}: after \"{st.After}\"");
            }
            // 키·장수·칸은 판을 만들 때(TutorialDeal) 본다 — 로더가 먼저 같은 검사를 돈다(ArgumentException → FormatException)
            TableJson.Wrap(where, () => st.BuildDeal(ctx));
            return st;
        }

        static List<AnswerPattern>[] ReadBots(JToken t, int seats, Ctx ctx, string where)
        {
            if (t == null) throw new FormatException($"{where}: 없다");
            if (t.Type == JTokenType.Null) return null;
            var a = t as JArray ?? throw new FormatException($"{where}: 배열이어야 한다");
            if (a.Count != seats) throw new FormatException($"{where}: 길이 {a.Count} ≠ 인원 {seats}");
            var bots = new List<AnswerPattern>[seats];
            for (int i = 0; i < seats; i++)
            {
                if (i == TutorialRunner.Me)
                {
                    if (a[i].Type != JTokenType.Null) throw new FormatException($"{where}[0]: 좌석 0(나)은 null");
                    continue;
                }
                bots[i] = Patterns(a[i], ctx, $"{where}[{i}]");
                if (bots[i].Count == 0) throw new FormatException($"{where}[{i}]: 봇 큐가 비었다");
            }
            return bots;
        }

        static TutorialStep ReadStep(JObject o, TutorialScript s, Ctx ctx, string where)
        {
            Only(o, where, StepFields);
            var st = new TutorialStep { Id = Str(o, "id", where), Chapter = Int(o, "ch", where), Text = NullOrStr(o, "text", where) };
            if (!StepId.IsMatch(st.Id)) throw new FormatException($"{where}: 단계 id \"{st.Id}\"");
            where += " " + st.Id;
            if (st.Chapter < 0) throw new FormatException($"{where}: ch {st.Chapter}");
            string key = $"tutorial.{s.Id}.{st.Id}";
            if (st.Text != null && st.Text != key) throw new FormatException($"{where}: text \"{st.Text}\" — 대사 키는 {key}");
            st.Hint = NullOrStr(o, "hint", where);
            if (st.Hint != null && st.Hint != key + ".hint") throw new FormatException($"{where}: hint \"{st.Hint}\" — {key}.hint");
            st.Anchor = NullOrStr(o, "anchor", where);
            string adv = Str(o, "advance", where);
            switch (adv)
            {
                case "next": st.Advance = StepAdvance.Next; break;
                case "command": st.Advance = StepAdvance.Command; break;
                case "bot": st.Advance = StepAdvance.Bot; break;
                case "free": st.Advance = StepAdvance.Free; break;
                case "demo": st.Advance = StepAdvance.Demo; break;
                case "screen": st.Advance = StepAdvance.Screen; break;
                default: throw new FormatException($"{where}: 모르는 advance \"{adv}\"");
            }
            st.Screen = NullOrStr(o, "screen", where);
            if ((st.Advance == StepAdvance.Screen) != (st.Screen != null) || st.Screen != null && !Name.IsMatch(st.Screen))
                throw new FormatException($"{where}: screen 칸은 advance screen에만, 이름 문법 {Name}");
            st.BotSeat = Int(o, "botSeat", where);
            st.BotAnswers = Int(o, "botAnswers", where);
            if (st.Advance == StepAdvance.Bot ? st.BotSeat < 1 || st.BotSeat >= s.Start.Seats || s.Bots?[st.BotSeat] == null || st.BotAnswers < 1 : st.BotSeat != -1 || st.BotAnswers != 0)
                throw new FormatException($"{where}: bot 단계는 봇 좌석(1~인원−1, 큐 있음)과 답 수 1 이상, 그 밖은 −1·0");
            st.Allow = Patterns(o["allow"], ctx, where + " allow");
            st.Auto = Patterns(o["auto"], ctx, where + " auto");
            bool wantAllow = st.Advance == StepAdvance.Command, wantAuto = st.Advance == StepAdvance.Command || st.Advance == StepAdvance.Demo;
            if ((st.Allow.Count > 0) != wantAllow || (st.Auto.Count > 0) != wantAuto)
                throw new FormatException($"{where}: allow는 command에만 · auto는 command·demo에만(둘 다 비우지 않는다)");
            foreach (var f in Arr(o, "dice", where))
            {
                var face = TableJson.ParseEnum<AttackDieFace>(f.Type == JTokenType.String ? (string)f : null, where + " dice");
                if (face == AttackDieFace.None) throw new FormatException($"{where}: 주사위 면 None");
                st.Dice.Add(face);
            }
            foreach (var p in Obj(o, "expect", where).Properties())
            {
                if (!TutorialExpect.IsKey(p.Name)) throw new FormatException($"{where}: 모르는 expect 키 \"{p.Name}\"");
                if (p.Value.Type != JTokenType.Integer) throw new FormatException($"{where}: expect {p.Name}이 정수가 아니다");
                st.Expect.Add(new ExpectValue(p.Name, (int)p.Value));
            }
            st.Ref.AddRange(Strings(o["ref"], where + " ref"));
            // 말없는 단계(07 T160): 막 앞 판 각본의 판 차리기(demo·bot)만 — 화면이 빨리감기로 지나니 보일 것(hint·anchor·expect)이 없다
            if (st.Text == null && (s.Start.After == null || st.Advance != StepAdvance.Demo && st.Advance != StepAdvance.Bot || st.Hint != null || st.Anchor != null || st.Expect.Count > 0))
                throw new FormatException($"{where}: 말없는 단계(text null)는 막 앞 판 각본(start.after)의 demo·bot만, hint·anchor·expect 없이");
            return st;
        }

        static List<AnswerPattern> Patterns(JToken t, Ctx ctx, string where)
        {
            var a = t as JArray ?? throw new FormatException($"{where}: 배열이어야 한다");
            var list = new List<AnswerPattern>(a.Count);
            for (int i = 0; i < a.Count; i++) list.Add(Pattern(a[i] as JObject ?? throw new FormatException($"{where}[{i}]: 객체가 아니다"), ctx, $"{where}[{i}]"));
            return list;
        }

        /// <summary>답 표기 하나(07 §7): 적은 칸만 본다. maint가 있으면 정비 꼴(choice·kind·target·arg와 함께 쓰지 않는다), 없으면 party·slot·value를 쓰지 않는다.</summary>
        static AnswerPattern Pattern(JObject o, Ctx ctx, string where)
        {
            foreach (var p in o.Properties())
                if (Array.IndexOf(PatternFields, p.Name) < 0) throw new FormatException($"{where}: 모르는 필드 {p.Name}");
            var a = new AnswerPattern();
            if (o["maint"] != null)
            {
                a.IsMaint = true;
                a.Maint = TableJson.ParseEnum<MaintKind>(Str(o, "maint", where), where);
                if (o["choice"] != null || o["kind"] != null || o["target"] != null || o["arg"] != null) throw new FormatException($"{where}: 정비 꼴에는 choice·kind·target·arg를 쓰지 않는다");
                a.Party = OptInt(o, "party", where);
                a.Slot = OptInt(o, "slot", where);
                a.Value = OptRef(o, "value", ctx, where);
                if (a.Party.HasValue && (a.Party.Value < 0 || a.Party.Value > 5)) throw new FormatException($"{where}: party {a.Party.Value}(파티 칸 0~5)");
            }
            else
            {
                if (o["party"] != null || o["slot"] != null || o["value"] != null) throw new FormatException($"{where}: party·slot·value는 정비 꼴(maint)에만");
                if (o["choice"] != null) a.Choice = TableJson.ParseEnum<ChoiceKind>(Str(o, "choice", where), where);
                if (o["kind"] != null) a.Kind = TableJson.ParseEnum<AnswerKind>(Str(o, "kind", where), where);
                a.Target = OptRef(o, "target", ctx, where);
                a.Arg = OptInt(o, "arg", where);
            }
            if (o["values"] != null)
            {
                var v = o["values"] as JArray ?? throw new FormatException($"{where}: values는 정수 배열");
                a.Values = new int[v.Count];
                for (int i = 0; i < v.Count; i++) a.Values[i] = v[i].Type == JTokenType.Integer ? (int)v[i] : throw new FormatException($"{where}: values[{i}]이 정수가 아니다");
            }
            return a;
        }

        /// <summary>정수 또는 표의 키(→ 색인).</summary>
        static int? OptRef(JObject o, string name, Ctx ctx, string where)
        {
            var t = o[name];
            if (t == null) return null;
            if (t.Type == JTokenType.Integer) return (int)t;
            if (t.Type != JTokenType.String) throw new FormatException($"{where}: {name}은 정수나 키");
            return Resolve(ctx, (string)t, $"{where} {name}");
        }

        /// <summary>
        /// 표의 키 → 색인: `pokemon.&lt;키&gt;`(포켓몬 표) · `trainer_card.&lt;키&gt;`(트레이너 카드 표) · `move.&lt;키&gt;`(기술 표) — 그 밖은 고정물(마을 등)·아이템·카드 표의 키를 그대로 찾는다(한 표에만 있어야 한다).
        /// </summary>
        public static int Resolve(Ctx ctx, string key, string where)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (key == null) throw new FormatException($"{where}: 키가 없다");
            if (Strip(key, "pokemon.", out string k)) return TableJson.Index(ctx.Cards.PokemonIds, k, where);
            if (Strip(key, "trainer_card.", out k)) return TableJson.Index(ctx.Cards.TrainerCardIds, k, where);
            if (Strip(key, "move.", out k)) return TableJson.Index(ctx.Cards.MoveIds, k, where);
            int found = -1, hits = 0;
            foreach (var table in new[] { ctx.Fixtures.Ids, ctx.Items.Ids, ctx.Decks.CardIds })
                if (table.TryIndexOf(key, out int id)) { found = id; hits++; }
            if (hits == 1) return found;
            throw new FormatException(hits == 0 ? $"{where}: 키 \"{key}\"가 어느 표에도 없다" : $"{where}: 키 \"{key}\"가 표 {hits}곳에 있다");
        }

        static bool Strip(string key, string prefix, out string rest)
        {
            rest = key.StartsWith(prefix, StringComparison.Ordinal) ? key.Substring(prefix.Length) : null;
            return rest != null;
        }

        // ── 엄격한 JObject 읽기 ──

        static void Only(JObject o, string where, params string[] names)
        {
            foreach (var n in names)
                if (o.Property(n) == null) throw new FormatException($"{where}: {n}이 없다");
            foreach (var p in o.Properties())
                if (Array.IndexOf(names, p.Name) < 0) throw new FormatException($"{where}: 모르는 필드 {p.Name}");
        }

        static JObject Obj(JObject o, string name, string where) => o[name] as JObject ?? throw new FormatException($"{where}: {name}이 객체가 아니다");

        static JArray Arr(JObject o, string name, string where) => o[name] as JArray ?? throw new FormatException($"{where}: {name}이 배열이 아니다");

        static string Str(JObject o, string name, string where) =>
            o[name]?.Type == JTokenType.String ? (string)o[name] : throw new FormatException($"{where}: {name}이 문자열이 아니다");

        static string NullOrStr(JObject o, string name, string where) => o[name]?.Type == JTokenType.Null ? null : Str(o, name, where);

        static int Int(JObject o, string name, string where) =>
            o[name]?.Type == JTokenType.Integer ? (int)o[name] : throw new FormatException($"{where}: {name}이 정수가 아니다");

        static long Long(JObject o, string name, string where)
        {
            long v = o[name]?.Type == JTokenType.Integer ? (long)o[name] : throw new FormatException($"{where}: {name}이 정수가 아니다");
            if (v < 0) throw new FormatException($"{where}: {name} {v} < 0");
            return v;
        }

        static int? OptInt(JObject o, string name, string where) => o[name] == null ? (int?)null : Int(o, name, where);

        static string[] Strings(JToken t, string where)
        {
            var a = t as JArray ?? throw new FormatException($"{where}: 문자열 배열이어야 한다");
            var r = new string[a.Count];
            for (int i = 0; i < a.Count; i++) r[i] = a[i].Type == JTokenType.String ? (string)a[i] : throw new FormatException($"{where}[{i}]이 문자열이 아니다");
            return r;
        }
    }
}
