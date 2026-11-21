using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M8 「사람 기록 → 시험대」(결정 113): 저장 파일(머리 + 명령 기록 — <see cref="SaveStore"/> 꼴)을 읽기만 해서 갤럭시단 배틀 장면 · 그 배틀의 사람 답을 뽑고(<see cref="HumanRecord"/>),
    /// 시험대가 그 장면 모음을 봇 장면(정본)과 따로 집계하고, 판세 평가 무게 맞추기(<see cref="Collect"/>)에 사람 장면을 넣는다. 시험의 저장은 봇 판을 임시 폴더에 쓴 견본이다 —
    /// 사용자 저장 폴더는 쓰지도 읽지도 않는다. 사용자 판(그 복사본)은 관문 밖 <c>SINNOH_HUMAN_SAVES</c> 진입점(<see cref="M8_사람기록_시험대_바깥저장폴더"/>, Explicit)만 읽는다.
    /// </summary>
    public partial class StrongAiTests
    {
        const string HumanContent = "m8-human-test";

        /// <summary>사람 장면의 AI 씨 바탕(봇 장면 id = 판 × 1000 + 번호와 겹치지 않게).</summary>
        static ulong HumanSceneId(int i) => (1UL << 40) + (ulong)i;

        /// <summary>사람 장면 지문의 바탕(정본 장면 지문과 따로 센다 — 같은 장면이어도 지문이 다르다).</summary>
        const ulong HumanDigestBasis = 0x4855_4D41_4E00_0113UL;

        /// <summary>바깥 저장 모음(폴더 하나 = 저장 하나)을 읽은 결과: 저장 수 · 읽은 수 · 못 읽은 까닭별 수 · 뽑은 배틀 · 사람 답 수.</summary>
        public sealed class HumanSet
        {
            public readonly List<HumanBattle> Battles = new List<HumanBattle>();
            public int Saves, Read, HumanMoves;
            public readonly SortedDictionary<string, int> Skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);

            public int SkippedCount
            {
                get
                {
                    int n = 0;
                    foreach (var kv in Skipped) n += kv.Value;
                    return n;
                }
            }

            public void Skip(string why) => Skipped[why] = Skipped.TryGetValue(why, out int n) ? n + 1 : 1;

            public override string ToString()
            {
                var parts = new List<string>();
                foreach (var kv in Skipped) parts.Add($"{kv.Key} {kv.Value}");
                return $"저장 {Saves} · 읽음 {Read} · 못 읽음 {SkippedCount}{(parts.Count > 0 ? "(" + string.Join(" · ", parts) + ")" : "")} · 갤럭시단 배틀 {Battles.Count} · 사람 답 {HumanMoves}";
            }
        }

        /// <summary>
        /// 사람 기록 진입점(시험 쪽, 결정 113): root 아래 폴더마다 머리만 읽고(<see cref="SaveStore.Peek"/> — 깨짐 · 옛 형식은 까닭별로 세고 건너뜀) → 명령 기록 전체(<see cref="SaveStore.ReadLog"/>) →
        /// <see cref="HumanRecord.Read"/>(규칙 판 · 데이터 · 다시 적용 어긋남도 건너뜀). 파일은 읽기만 한다. contentHash = 지금 데이터 해시(null이면 데이터 표만 견줌).
        /// </summary>
        public static HumanSet ReadHumanSaves(Ctx ctx, string root, string contentHash = null)
        {
            var set = new HumanSet();
            var dirs = Directory.GetDirectories(root);
            Array.Sort(dirs, StringComparer.Ordinal);
            foreach (var dir in dirs)
            {
                set.Saves++;
                var entry = SaveStore.Peek(dir);
                if (!entry.Readable)
                {
                    set.Skip(entry.Problem.ToString());
                    continue;
                }
                Add(HumanRecord.Read(ctx, entry.Head, SaveStore.ReadLog(dir), contentHash));
            }
            // 걷어 온 플레이 기록 묶음(02 §12 — Tools/play-records.mjs가 받은 rec_*.gz): 머리 + 명령 줄을 풀어 같은 길로
            var files = Directory.GetFiles(root, PlayRecord.Prefix + "*" + PlayRecord.Extension);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
            {
                set.Saves++;
                PlayRecordFile rec;
                try
                {
                    rec = PlayRecord.Unpack(File.ReadAllBytes(file));
                }
                catch (InvalidDataException)
                {
                    set.Skip(nameof(SaveProblem.Broken));
                    continue;
                }
                Add(HumanRecord.Read(ctx, rec.Head, rec.Lines, contentHash));
            }
            return set;

            void Add(HumanGame game)
            {
                if (game.Skip != HumanSkip.None)
                {
                    set.Skip(game.Skip.ToString());
                    return;
                }
                set.Read++;
                set.HumanMoves += game.HumanMoves;
                set.Battles.AddRange(game.Battles);
            }
        }

        /// <summary>
        /// 봇 판 하나를 저장 꼴로 dir에 쓴다(사람 기록 견본 — 2인 신오의 심장, 시험대 실전 장면과 같은 판): 머리 + 처음 스냅샷 + 명령 기록(좌석 봇 답 = 사람 답 꼴 AnswerTo, 갤럭시단 = AI 답).
        /// 명령 기록은 한 번에 쓴다(SaveStore.AppendCommand는 줄마다 디스크까지 비운다).
        /// </summary>
        void WriteBotSave(string dir, ulong seed)
        {
            var store = new SaveStore(dir);
            store.WriteHead(new SaveHead
            {
                SaveId = Path.GetFileName(dir), ContentHash = HumanContent, IdTables = SaveRecord.IdTablesOf(ctx), Options = new RuleOptions { NonPlayerMovePolicy = NonPlayerMovePolicy.Principle },   // 봇 판의 기본 옵션과 같게(Playout — 결정 122)
                Campaign = Campaign.HeartOfSinnoh, SeatCount = 2, Seed = seed,
            });
            var log = new StringBuilder();
            Playout.Run(ctx, 2, seed, commands: (s, seat, cmd) =>
            {
                if (s.CommandCount == 0) store.WriteSnapshot(new SaveSnapshot { CommandCount = 0, State = s.Clone() });
                log.Append(SaveStore.Serialize(new SaveLogLine { Seq = s.CommandCount, Seat = seat, Command = cmd }, false)).Append('\n');
            });
            File.WriteAllText(Path.Combine(dir, SaveStore.LogFile), log.ToString(), new UTF8Encoding(false));
        }

        /// <summary>시험 쪽 임시 폴더(사용자 저장 폴더가 아니다 — 시스템 임시 폴더 아래 새 폴더).</summary>
        static string HumanTemp()
        {
            string d = Path.Combine(Path.GetTempPath(), "sinnoh-m8-human-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>폴더 아래 파일마다 이름 · 크기 · 고친 시각 · 내용 해시 — 읽기 전후로 견줘 「읽기만」을 본다.</summary>
        static string FolderPrint(string root)
        {
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var f in files)
            {
                ulong h = 14695981039346656037UL;
                foreach (byte b in File.ReadAllBytes(f)) h = unchecked((h ^ b) * 1099511628211UL);
                sb.Append(f.Substring(root.Length)).Append(' ').Append(new FileInfo(f).Length).Append(' ').Append(File.GetLastWriteTimeUtc(f).Ticks).Append(' ').Append(h.ToString("X16")).Append('\n');
            }
            return sb.ToString();
        }

        [Test]
        public void M8_사람기록_저장을읽어장면과사람답을뽑는다_봇판의실전장면과같다()
        {
            // 봇 판(시드 0)을 저장 꼴로 임시 폴더에 쓰고 읽기만 해서: 뽑은 장면 = 같은 판의 실전 장면(RealScenes)과 해시까지 같다 · 장면마다 명령을 다시 적용하면 그 배틀이 닫힌다 ·
            // 사람 답 = 도전한 좌석의 AnswerTo(AI 답 · 시간 초과 · 정비 아님) · 읽기 전후 폴더가 그대로(읽기만)
            string root = HumanTemp();
            try
            {
                WriteBotSave(Path.Combine(root, "bot-0"), 0);
                string before = FolderPrint(root);
                var set = ReadHumanSaves(ctx, root, HumanContent);
                Assert.AreEqual(before, FolderPrint(root), "읽기만 한다");
                Report($"M8 사람 기록(봇 판 견본): {set}");
                Assert.AreEqual(1, set.Read, set.ToString());
                var expected = RealScenes(ctx, 2, 0);
                Assert.AreEqual(expected.Count, set.Battles.Count, "실전 장면 수");
                Assert.Greater(expected.Count, 1);
                int ended = 0, human = 0;
                for (int i = 0; i < expected.Count; i++)
                {
                    var hb = set.Battles[i];
                    Assert.AreEqual(StateHash.Of(expected[i]), StateHash.Of(hb.Scene), $"장면 {i} = 실전 장면");
                    Assert.AreEqual(KindOf(expected[i]), KindOf(hb.Scene));
                    Assert.AreEqual(i, hb.Index);
                    var g = hb.Scene.Clone();
                    int battle = -1;
                    foreach (var line in hb.Lines)
                    {
                        Assert.IsTrue(Engine.Submit(g, ctx, line.Seat, line.Command, null).Accepted, $"장면 {i} 명령 {line.Seq}");
                        if (g.Battle != null && g.Battle.Kind == BattleKind.Galactic) battle = g.Battle.Id;
                    }
                    Assert.GreaterOrEqual(battle, 0, $"장면 {i}: 갤럭시단 배틀이 열렸다");
                    if (hb.Ended)
                    {
                        ended++;
                        Assert.IsTrue(g.Battle == null || g.Battle.Id != battle, $"장면 {i}: 명령을 다 적용하면 그 배틀이 닫혔다");
                        Assert.That(hb.Winner, Is.InRange(-1, 1));
                    }
                    foreach (int at in hb.Human)
                    {
                        var c = hb.Lines[at].Command;
                        Assert.AreEqual(hb.Seat, hb.Lines[at].Seat);
                        Assert.AreEqual(CommandKind.Answer, c.Kind);
                        Assert.IsFalse(c.ByAi);
                        human++;
                    }
                }
                Assert.AreEqual(set.HumanMoves, human);
                Assert.Greater(human, 0, "사람 답");
                Assert.GreaterOrEqual(ended, expected.Count - 1, "마지막 장면 말고는 배틀이 끝났다");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void M8_사람기록_옛저장꼴과어긋난저장은못읽음으로건너뛴다()
        {
            // 한 폴더에 저장 여섯: 성한 것 · 옛 형식(머리 판 30) · 깨진 머리 · 옛 규칙 판 · 데이터 표가 다름 · 명령 기록이 어긋남 → 읽음 1 · 까닭별 못 읽음 1씩. 데이터 해시가 다르면 성한 것도 못 읽음
            string root = HumanTemp();
            try
            {
                string good = Path.Combine(root, "a-good");
                WriteBotSave(good, 0);
                string headText = File.ReadAllText(Path.Combine(good, SaveStore.HeadFile));
                var head = SaveStore.Deserialize<SaveHead>(headText);
                void Copy(string name, string headJson, Func<string, string> log = null)
                {
                    string d = Path.Combine(root, name);
                    Directory.CreateDirectory(d);
                    foreach (var f in Directory.GetFiles(good)) File.Copy(f, Path.Combine(d, Path.GetFileName(f)));
                    File.WriteAllText(Path.Combine(d, SaveStore.HeadFile), headJson, new UTF8Encoding(false));
                    if (log != null)
                    {
                        string p = Path.Combine(d, SaveStore.LogFile);
                        File.WriteAllText(p, log(File.ReadAllText(p)), new UTF8Encoding(false));
                    }
                }
                Copy("b-old-format", Regex.Replace(headText, "\"FormatVersion\": \\d+", "\"FormatVersion\": " + (SaveRecord.CurrentFormat - 1)));
                Copy("c-broken", "{ \"FormatVersion\": " + SaveRecord.CurrentFormat + ", ");
                var rules = SaveStore.Deserialize<SaveHead>(headText);
                rules.RulesVersion = SaveRecord.CurrentRulesVersion - 1;
                Copy("d-old-rules", SaveStore.HeadJson(rules));
                var data = SaveStore.Deserialize<SaveHead>(headText);
                data.IdTables[0].Keys[0] += "-renamed";
                Copy("e-data", SaveStore.HeadJson(data));
                Copy("f-replay", headText, text =>
                {
                    var lines = SaveStore.ReadLog(good);
                    int at = lines.FindIndex(lines.Count / 2, l => l.Command.Kind == CommandKind.Answer);
                    lines[at].Command.ChoiceId += 100000; // 없는 선택에 낸 답 — 거절
                    var sb = new StringBuilder();
                    foreach (var l in lines) sb.Append(SaveStore.Serialize(l, false)).Append('\n');
                    return sb.ToString();
                });
                string before = FolderPrint(root);
                var set = ReadHumanSaves(ctx, root, HumanContent);
                Report($"M8 사람 기록 건너뜀: {set}");
                Assert.AreEqual(before, FolderPrint(root), "읽기만 한다");
                Assert.AreEqual(6, set.Saves);
                Assert.AreEqual(1, set.Read);
                Assert.AreEqual(5, set.SkippedCount);
                foreach (var why in new[] { nameof(SaveProblem.OldFormat), nameof(SaveProblem.Broken), nameof(HumanSkip.Rules), nameof(HumanSkip.Data), nameof(HumanSkip.Replay) })
                    Assert.AreEqual(1, set.Skipped.TryGetValue(why, out int n) ? n : 0, $"{why}: {set}");
                Assert.AreEqual(RealScenes(ctx, 2, 0).Count, set.Battles.Count, "성한 저장의 장면만");
                var other = ReadHumanSaves(ctx, root, "other-content");
                Assert.AreEqual(0, other.Read, "데이터 해시가 다르면 못 읽음");
                Assert.AreEqual(3, other.Skipped[nameof(HumanSkip.Data)], "성한 것 · 데이터 표 · 명령 기록 어긋남(해시를 먼저 본다)");
                Assert.AreEqual(HumanSkip.Format, HumanRecord.Read(ctx, new SaveHead { FormatVersion = SaveRecord.CurrentFormat - 1 }, new List<SaveLogLine>()).Skip, "머리 판만 다른 머리도(Read 직접)");
                Assert.AreEqual(HumanSkip.Data, HumanRecord.Read(ctx, head, new List<SaveLogLine>(), "other-content").Skip);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>사람 장면 시험대 결과: 장면 종류마다 갤 원칙 · 갤 강한(1단계) 대 좌석 봇, 그리고 사람이 실제로 낸 결과. 지문은 정본과 따로(<see cref="HumanDigestBasis"/>).</summary>
        sealed class HumanBench
        {
            public readonly BenchResult[] Basic = { new BenchResult(), new BenchResult(), new BenchResult() };
            public readonly BenchResult[] Strong = { new BenchResult(), new BenchResult(), new BenchResult() };
            public readonly BenchResult[] Real = { new BenchResult(), new BenchResult(), new BenchResult() };
            public ulong Digest = HumanDigestBasis;
            public int Scenes, Unfinished;
            public long Simulations;
        }

        /// <summary>
        /// 시험대가 바깥 장면 모음을 받는다(결정 113 — 정본 1,000과 섞지 않는다): 사람 배틀의 장면마다 같은 자리에서 갤 원칙 · 갤 강한 AI 1단계(굴리기 <see cref="BenchSims"/>)를
        /// 좌석 봇(Playout.Bot — 씨 = 장면 id)과 맞붙이고, 사람이 실제로 낸 결과(끝난 배틀만)를 따로 센다. 앞 max개.
        /// </summary>
        HumanBench RunHumanBench(IReadOnlyList<HumanBattle> battles, int max, Func<long> now = null)
        {
            var res = new HumanBench();
            var agent = new StrongAgent();
            for (int i = 0; i < battles.Count && i < max; i++)
            {
                var hb = battles[i];
                int kind = KindOf(hb.Scene);
                ulong id = HumanSceneId(i);
                res.Digest = unchecked((res.Digest ^ StateHash.Of(hb.Scene)) * 1099511628211UL);
                foreach (bool smart in new[] { false, true })
                {
                    var bot = new Playout.Bot(id);
                    var rng = new Pcg32(id, 0xB07);
                    RolloutPolicy player = (g, cx, c, legal) => bot.Answer(g, cx, c, rng);
                    if (smart) GalacticBench.Play(hb.Scene.Clone(), ctx, agent, player, res.Strong[kind], BenchSims, 1, id, now);
                    else GalacticBench.Play(hb.Scene.Clone(), ctx, new PrincipleAgent(), player, res.Basic[kind], 0, 1, id);
                }
                var real = res.Real[kind];
                if (!hb.Ended) res.Unfinished++;
                else
                {
                    real.Battles++;
                    if (hb.Winner == 1) real.GalacticWins++;
                    else if (hb.Winner == 0) real.PlayerWins++;
                    else real.Draws++;
                }
                res.Scenes++;
            }
            res.Simulations = agent.Simulations;
            return res;
        }

        void ReportHumanBench(string label, HumanBench res)
        {
            var basic = new BenchResult();
            var strong = new BenchResult();
            var real = new BenchResult();
            for (int k = 0; k < SceneKinds.Length; k++)
            {
                basic.Add(res.Basic[k]);
                strong.Add(res.Strong[k]);
                real.Add(res.Real[k]);
                if (res.Basic[k].Battles == 0) continue;
                Report($"M8 사람 장면 시험대 [{label}] {SceneKinds[k]} {res.Basic[k].Battles}: 원칙 {BenchResult.Percent(res.Basic[k].WinPermille)} → 강한 1단계 {BenchResult.Percent(res.Strong[k].WinPermille)} · 실제(사람) {res.Real[k].Battles}판 갤 승률 {BenchResult.Percent(res.Real[k].WinPermille)}");
            }
            Report($"M8 사람 장면 시험대 [{label}] 장면 {res.Scenes} · 지문 0x{res.Digest:X16}(정본과 따로) · 원칙 {BenchResult.Percent(basic.WinPermille)} → 강한 1단계 {BenchResult.Percent(strong.WinPermille)}({Points(strong.WinPermille - basic.WinPermille)}) · 실제(사람) 끝난 {real.Battles}판 갤 승률 {BenchResult.Percent(real.WinPermille)}(끝나기 전 저장 {res.Unfinished}) · 굴리기 {res.Simulations} · 결정당 최대 {strong.MaxThinkMs}ms");
        }

        /// <summary>시험대 · 맞추기에 넣는 사람 장면 수(관문 — 견본 저장 하나의 앞부분).</summary>
#if UNITY_EDITOR
        const int HumanBenchScenes = 2;
#else
        const int HumanBenchScenes = 4;
#endif

        [Test, Category("Slow")]
        public void M8_사람기록_시험대는바깥장면을봇장면과따로집계한다()
        {
            // 견본 저장(봇 판 시드 0)의 장면 앞 HumanBenchScenes개 → 사람 장면 시험대: 장면 수 · 멈춤 0 · 실제 결과 칸 · 지문이 같은 장면의 정본 지문과 다르다(따로 센다).
            // 정본 실전 장면 시험대(ForEachRealScene)는 이 모음을 읽지 않는다 — 바깥 장면은 이 길로만 들어온다
            string root = HumanTemp();
            try
            {
                WriteBotSave(Path.Combine(root, "bot-0"), 0);
                var set = ReadHumanSaves(ctx, root, HumanContent);
                var sw = Stopwatch.StartNew();
                var res = RunHumanBench(set.Battles, HumanBenchScenes, () => sw.ElapsedMilliseconds);
                ReportHumanBench("봇 판 견본", res);
                int n = Math.Min(HumanBenchScenes, set.Battles.Count);
                Assert.AreEqual(n, res.Scenes);
                int battles = 0, real = 0;
                for (int k = 0; k < SceneKinds.Length; k++)
                {
                    Assert.AreEqual(0, res.Basic[k].Stuck);
                    Assert.AreEqual(0, res.Strong[k].Stuck);
                    Assert.AreEqual(res.Basic[k].Battles, res.Strong[k].Battles);
                    battles += res.Basic[k].Battles;
                    real += res.Real[k].Battles;
                }
                Assert.AreEqual(n, battles);
                Assert.AreEqual(n, real + res.Unfinished, "실제 결과 = 끝난 배틀 + 끝나기 전 저장");
                Assert.Greater(res.Simulations, 0);
                ulong canon = 14695981039346656037UL;
                ForEachRealScene(ctx, n, (g, i, s) => canon = unchecked((canon ^ StateHash.Of(s)) * 1099511628211UL));
                Assert.AreNotEqual(canon, res.Digest, "사람 장면 지문은 정본 지문과 따로 센다");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test, Category("Slow")]
        public void M8_사람기록_무게맞추기에사람장면을넣고없으면그대로()
        {
            // 판세 평가 무게 맞추기(Collect): 사람 장면이 0(null · 빈 목록)이면 자료 · 지문이 지금 그대로 · 사람 장면만 넣으면 그 배틀의 갤럭시단 결정 자료와 사람 답 견줌이 쌓인다 ·
            // 봇 + 사람 = 둘을 더한 줄 수
            string root = HumanTemp();
            try
            {
                WriteBotSave(Path.Combine(root, "bot-0"), 0);
                var set = ReadHumanSaves(ctx, root, HumanContent);
                var humans = set.Battles.GetRange(0, Math.Min(2, set.Battles.Count));
                var plain = Collect(1, BattleEval.Default, 0);
                var none = Collect(1, BattleEval.Default, 0, null, new List<HumanBattle>());
                Assert.AreEqual(plain.Rows, none.Rows);
                Assert.AreEqual(plain.Yy, none.Yy);
                Assert.AreEqual(plain.Digest, none.Digest, "사람 장면이 0이면 지금 그대로");
                Assert.AreEqual(0, none.HumanScenes);
                var only = Collect(0, BattleEval.Default, 0, null, humans);
                var w = Solve(only, out int r2, out _);
                Report($"M8 사람 기록 맞추기(사람 장면 {only.HumanScenes}): 결정 {only.Decisions} · 줄 {only.Rows} · R² {r2}‰ · 사람 답 {only.HumanMoves} — 원칙 답과 같음 {only.HumanSamePrinciple} · 탐색 답(플 1단계)과 같음 {only.HumanSameSearch} · 무게 {Table(w)}");
                Assert.AreEqual(humans.Count, only.HumanScenes);
                Assert.AreEqual(0, only.Scenes);
                Assert.Greater(only.Rows, 0, "사람 배틀의 갤럭시단 결정 자료");
                Assert.Greater(only.HumanMoves, 0, "사람 답 견줌");
                Assert.LessOrEqual(only.HumanSameSearch, only.HumanMoves);
                Assert.AreEqual(0, only.Stuck);
                var both = Collect(1, BattleEval.Default, 0, null, humans);
                Assert.AreEqual(plain.Rows + only.Rows, both.Rows, "봇 + 사람 = 줄 수의 합");
                Assert.AreEqual(plain.Decisions + only.Decisions, both.Decisions);
                Assert.AreNotEqual(plain.Digest, both.Digest);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test, Explicit("관문 밖 — SINNOH_HUMAN_SAVES=<사용자 저장 폴더의 복사본>을 주고 dotnet --filter로 짚어 돈다")]
        public void M8_사람기록_시험대_바깥저장폴더()
        {
            // 사용자 판을 읽어 보는 진입점(결정 113): 복사본 폴더만 받는다(사용자 저장 폴더 그 자리 — AppData\LocalLow — 는 거절). 읽기 → 못 읽음 까닭별 수 → 사람 장면 시험대(전부) → 사람 장면만으로 무게 맞추기
            string root = Environment.GetEnvironmentVariable("SINNOH_HUMAN_SAVES");
            if (string.IsNullOrEmpty(root)) Assert.Ignore("SINNOH_HUMAN_SAVES 없음");
            Assert.IsTrue(Directory.Exists(root), root);
            Assert.IsFalse(Path.GetFullPath(root).IndexOf("LocalLow", StringComparison.OrdinalIgnoreCase) >= 0, "사용자 저장 폴더 그 자리가 아니라 복사본을 준다");
            string before = FolderPrint(root);
            var set = ReadHumanSaves(ctx, root, Environment.GetEnvironmentVariable("SINNOH_HUMAN_CONTENT"));
            Report($"M8 사람 기록(바깥 폴더): {set}");
            var sw = Stopwatch.StartNew();
            ReportHumanBench("바깥 폴더", RunHumanBench(set.Battles, int.MaxValue, () => sw.ElapsedMilliseconds));
            if (set.Battles.Count > 0)
            {
                var only = Collect(0, BattleEval.Default, 0, null, set.Battles);
                var w = Solve(only, out int r2, out int r2Half);
                Report($"M8 사람 기록 맞추기(바깥 폴더 — 사람 장면 {only.HumanScenes}): 결정 {only.Decisions} · 줄 {only.Rows} · R² {r2}‰(500 기준 {r2Half}‰) · 사람 답 {only.HumanMoves} — 원칙 답과 같음 {only.HumanSamePrinciple} · 탐색 답(플 1단계)과 같음 {only.HumanSameSearch}");
                Report($"M8 사람 기록 맞춘 무게: {Table(w)}");
            }
            Assert.AreEqual(before, FolderPrint(root), "읽기만 한다");
        }
    }
}
