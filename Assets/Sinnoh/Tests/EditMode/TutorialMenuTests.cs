using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Tutorial;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M6.5 「화면 ② 목차·입구」의 엔진 없는 부분(07 §2): 튜토리얼 기록(`tutorial.last`·`tutorial.seen.&lt;장&gt;` — 임시 폴더) · 목차 표시(끝까지 본 장만 완료, 결정 90 Q7 ①) ·
    /// 장 머리 표(`tutorial_chapters.json`) · 빨리감기(앞 장 명령을 보내면 그 장 시작 국면 — 국면 스냅샷 없음, 07 §2-2).
    /// </summary>
    public class TutorialMenuTests
    {
        static Ctx ctx;

        [OneTimeSetUp]
        public void Load() => ctx = TestData.LoadCtx();

        static string TempFolder() => Path.Combine(Path.GetTempPath(), "sinnoh-tutorial-record-" + Guid.NewGuid().ToString("N"));

        [Test]
        public void M65_튜토리얼기록_임시폴더에쓰고다시읽는다_모르는줄은버리고_폴더가없으면메모리만()
        {
            string dir = TempFolder();
            try
            {
                var r = TutorialRecord.Open(dir);
                Assert.IsFalse(r.Any, "처음에는 기록이 없다");
                Assert.IsFalse(File.Exists(r.PathOf), "읽기만으로는 파일을 만들지 않는다");
                r.Enter("main0");
                r.MarkSeen("main0");
                r.Enter("main3");
                r.MarkSeen("e01");
                Assert.IsNull(r.Error);
                Assert.AreEqual("tutorial.last=main3\ntutorial.seen.e01=1\ntutorial.seen.main0=1\n", File.ReadAllText(r.PathOf), "한 줄에 키=값, 키 순서 고정");

                var again = TutorialRecord.Open(dir);
                Assert.IsTrue(again.Any);
                Assert.AreEqual("main3", again.Last);
                CollectionAssert.AreEqual(new[] { "e01", "main0" }, again.SeenChapters.ToArray());

                // 남이 고친 파일: 모르는 줄·빈 값·값이 1이 아닌 seen은 버리고, 아는 줄은 읽는다(게임이 서지 않게)
                File.WriteAllText(r.PathOf, "garbage\r\ntutorial.seen.main1=1\r\ntutorial.seen.main2=0\r\ntutorial.last=\r\n=x\r\nother.key=3\r\n");
                var odd = TutorialRecord.Open(dir);
                Assert.IsNull(odd.Last);
                CollectionAssert.AreEqual(new[] { "main1" }, odd.SeenChapters.ToArray());

                Assert.Throws<ArgumentException>(() => odd.Enter("a=b"), "키=값을 깨는 장 id");
                Assert.Throws<ArgumentException>(() => odd.MarkSeen(""));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            var mem = TutorialRecord.Memory();
            mem.Enter("main0");
            Assert.IsTrue(mem.Any);
            Assert.IsNull(mem.PathOf, "메모리 기록은 파일이 없다(에디터·캡처·확인용 플레이어)");
            Assert.IsFalse(TutorialRecord.Open(null).Any);
        }

        [Test]
        public void M65_목차표시_끝까지본장만완료_마지막으로들어간장은이어서_딱지는본편마지막장을볼때까지()
        {
            var chapters = TutorialChapters.Load(TestData.ReadText("tutorial_chapters.json"));
            var last = TutorialChapters.LastMain(chapters);
            Assert.AreEqual("main7", last.Id, "본편 마지막 장 = 막 맛보기");
            var r = TutorialRecord.Memory();
            Assert.IsTrue(r.FirstTime(last));
            Assert.AreEqual(TutorialMark.None, r.MarkOf("main0"));
            r.Enter("main0");
            Assert.AreEqual(TutorialMark.Resume, r.MarkOf("main0"), "들어갔다 나온 장 = [이어서]");
            r.MarkSeen("main0");
            r.Enter("main1");
            Assert.AreEqual(TutorialMark.Seen, r.MarkOf("main0"));
            Assert.AreEqual(TutorialMark.Resume, r.MarkOf("main1"));
            // 건너뛰기는 MarkSeen을 부르지 않는다 — 다음 장에 들어간 것만 남는다(건너뛴 장은 완료가 아니다)
            r.Enter("main3");
            Assert.AreEqual(TutorialMark.None, r.MarkOf("main1"), "건너뛴 장은 완료도 이어서도 아니다");
            Assert.AreEqual(TutorialMark.Resume, r.MarkOf("main3"));
            r.MarkSeen("main3");
            Assert.AreEqual(TutorialMark.Seen, r.MarkOf("main3"), "끝까지 본 장은 마지막 장이어도 완료");
            Assert.IsTrue(r.FirstTime(last), "본편 마지막 장을 보기 전까지 딱지");
            r.MarkSeen("main7");
            Assert.IsFalse(r.FirstTime(last));
        }

        /// <summary>튜토리얼 판의 좌석 인물(결정 96 Q5 나): 좌석 0 = 내 주인공(모르는 키면 기본값) · 1 = 용식(Pt 라이벌 — 고를 수 없는 손님 인물) · 2 = 빛나(Pt 여자 주인공).</summary>
        [Test]
        public void M65_튜토리얼판좌석인물_내주인공_용식_빛나()
        {
            var P = typeof(Sinnoh.Rules.Session.Protagonists);
            CollectionAssert.AreEqual(new[] { "hgss_001_lyra", "pt_rival", "pt_player_female" }, Sinnoh.Rules.Session.Protagonists.ForTutorial("hgss_001_lyra", 3), P.Name);
            CollectionAssert.AreEqual(new[] { Sinnoh.Rules.Session.Protagonists.Default, Sinnoh.Rules.Session.Protagonists.Rival }, Sinnoh.Rules.Session.Protagonists.ForTutorial("no_such", 2));
            Assert.IsFalse(Sinnoh.Rules.Session.Protagonists.Has(Sinnoh.Rules.Session.Protagonists.Rival), "용식은 고르는 화면에 없다");
            Assert.IsTrue(Sinnoh.Rules.Session.Protagonists.Has(Sinnoh.Rules.Session.Protagonists.Dawn));
            CollectionAssert.DoesNotContain(Sinnoh.Rules.Session.Protagonists.Keys, Sinnoh.Rules.Session.Protagonists.Guests[0]);
            foreach (string who in Sinnoh.Rules.Session.Protagonists.ForTutorial(null, 6)) Assert.IsNotNull(who);
        }

        [Test]
        public void M65_장머리표_빌드한표의단계수가각본과같고_로더는엄격하다()
        {
            string json = TestData.ReadText("tutorial_chapters.json");
            var chapters = TutorialChapters.Load(json);
            Assert.AreEqual(8 + 15, chapters.Count, "본편 8장(0~7) + 확장 15장(07 §4-2 · §5-2)");
            var main = TutorialLoader.Load(TestData.ReadText("tutorial/main.json"), ctx, "tutorial/main.json");
            foreach (var c in chapters.Where(c => c.IsMain))
            {
                int then = c.Then.Sum(id => TutorialLoader.Load(TestData.ReadText($"tutorial/{id}.json"), ctx, id + ".json").Steps.Count(x => x.Text != null));
                Assert.AreEqual(main.Steps.Count(s => s.Chapter == c.Chapter) + then, c.Steps, c.Id + ": 그 장의 main 단계 + 이어 여는 각본의 단계(말없는 판 차리기 단계는 세지 않는다 — 07 T160)");
            }
            foreach (var s in main.Steps) Assert.IsNotNull(TutorialChapters.Of(chapters, "main", s.Chapter), s.Id + ": 본편 단계의 장이 목차에 있다");
            // 막 맛보기 7장 = 각본 셋(07 T54): main의 7장 4단계 → preview2 5단계(결정 96 Q9 나 — 새턴 배틀 demo를 없앴다) → preview3 6단계
            var seven = TutorialChapters.Find(chapters, "main7");
            CollectionAssert.AreEqual(new[] { "preview2", "preview3" }, seven.Then.ToArray());
            Assert.AreEqual(4 + 5 + 6, seven.Steps);
            Assert.AreEqual("preview2", seven.After("main"));
            Assert.AreEqual("preview3", seven.After("preview2"));
            Assert.IsNull(seven.After("preview3"), "마지막 각본 뒤 = 장의 끝");
            Assert.IsNull(TutorialChapters.Find(chapters, "main3").After("main"), "이어 여는 각본이 없는 장");
            Assert.AreSame(seven, TutorialChapters.Of(chapters, "preview2", 7), "이어 여는 각본의 단계는 그 장");
            Assert.AreSame(seven, TutorialChapters.Of(chapters, "preview3", 7));
            foreach (string id in Directory.GetFiles(TestData.JsonPath("tutorial"), "*.json").Select(Path.GetFileNameWithoutExtension))
                Assert.IsTrue(id == "main" || chapters.Any(c => c.Id == id || c.Then.Contains(id)), id + ": 각본은 목차의 장이나 이어 여는 각본");
            for (int i = 1; i <= 15; i++)
            {
                var e = chapters[8 + i - 1];
                Assert.AreEqual("e" + i.ToString("00"), e.Id);
                Assert.AreEqual(File.Exists(TestData.JsonPath("tutorial/" + e.Id + ".json")), e.Ready, e.Id + ": 각본이 있어야 고를 수 있다");
                StringAssert.EndsWith(".png", e.Icon, e.Id);
            }
            Assert.AreEqual("main2", TutorialChapters.Next(chapters, TutorialChapters.Find(chapters, "main1")).Id, "다음 장은 단계가 있는 장");
            Assert.IsNull(TutorialChapters.Next(chapters, TutorialChapters.Find(chapters, "main7")), "본편의 끝 장");
            var ko = TestData.LoadStrings("ko");
            foreach (var c in chapters) Assert.IsTrue(ko.Contains(c.Name), c.Name);

            string row0 = "{\"id\":\"main0\",\"script\":\"main\",\"ch\":0,\"icon\":null,\"name\":\"screen.tutorial.menu.ch.main0\",\"steps\":1,\"then\":[],\"rulebook\":[\"overview.goal\"]}";
            string row1 = "{\"id\":\"main1\",\"script\":\"main\",\"ch\":1,\"icon\":null,\"name\":\"screen.tutorial.menu.ch.main1\",\"steps\":1,\"then\":[\"preview2\"],\"rulebook\":[\"overview.setup\"]}";
            string ext = "{\"id\":\"e01\",\"script\":\"e01\",\"ch\":-1,\"icon\":\"a.png\",\"name\":\"screen.tutorial.menu.ch.e01\",\"steps\":1,\"then\":[],\"rulebook\":[\"shrines.fate\"]}";
            string T(params string[] rows) => "{\"schema\":1,\"rows\":[" + string.Join(",", rows) + "]}";
            Assert.AreEqual(3, TutorialChapters.Load(T(row0, row1, ext)).Count);
            foreach (var bad in new[]
            {
                T(row0.Replace(",\"then\":[]", "")),                                // then이 빠졌다
                T(row0.Replace("\"then\":[]", "\"then\":[\"preview2\"]"), row1),   // 이어 여는 각본이 두 번
                T(row1.Replace("preview2", "main")),                                // 본편 각본을 이어 연다
                T(row1.Replace("preview2", "e01")),                                 // 확장 장 각본을 이어 연다
                T(ext.Replace("\"then\":[]", "\"then\":[\"preview2\"]")),          // 확장 장은 then을 쓰지 않는다
                T(row0, row0),                                                     // id 겹침
                T(row0.Replace("\"steps\":1", "\"steps\":1,\"x\":0")),             // 모르는 필드
                T(row0.Replace(",\"steps\":1", "")),                               // 빠진 필드
                T(row0.Replace("\"script\":\"main\"", "\"script\":\"e01\"")),      // 본편 장인데 각본이 main이 아님
                T(row0.Replace("ch.main0", "ch.main1")),                           // 이름 키가 id와 다름
                T(row0.Replace("\"steps\":1", "\"steps\":\"1\"")),                 // 형
                T(row0.Replace("\"id\":\"main0\"", "\"id\":\"e01\"")),             // 확장 장 id에 본편 장 번호
                T(row0.Replace(",\"rulebook\":[\"overview.goal\"]", "")),          // 설명서 절 키가 빠졌다(M9)
                T(row0.Replace("[\"overview.goal\"]", "[]")),                       // 설명서 절 키 0개
                T(row0.Replace("[\"overview.goal\"]", "[\"Overview Goal\"]")),      // 절 키 문법
                T(row0.Replace("[\"overview.goal\"]", "[\"overview.goal\",\"overview.goal\"]")),   // 겹침
            })
                Assert.Throws<FormatException>(() => TutorialChapters.Load(bad), bad);
        }

        /// <summary>
        /// M9 「튜토리얼 잇기」(07 결정 38 · T212): 장마다 관련 룰 설명서 절 키가 있다 — 장 끝의 「설명서에서 더 보기」가 연다(화면은 설명서 S43을 합친 뒤).
        /// 설명서 표(<c>rulebook.ko.json</c> · <c>rulebook.en.json</c>)가 이 트리에 있으면 모든 키가 두 언어 설명서에 있어야 한다(설명서 묶음이 없는 가지에서는 문법만).
        /// </summary>
        [Test]
        public void M9_튜토리얼_장마다설명서절키가있고_설명서에있다()
        {
            var chapters = TutorialChapters.Load(TestData.ReadText("tutorial_chapters.json"));
            Assert.AreEqual(8 + 15, chapters.Count);
            foreach (var c in chapters)
            {
                Assert.That(c.Rulebook.Count, Is.InRange(1, 4), c.Id);
                foreach (string k in c.Rulebook) StringAssert.IsMatch(TutorialChapters.RulebookKey.ToString(), k, c.Id);
            }
            int checkedLangs = 0;
            foreach (string lang in new[] { "ko", "en" })
            {
                string file = TestData.JsonPath("rulebook." + lang + ".json");
                if (!File.Exists(file)) continue;
                checkedLangs++;
                // 절 행의 머리 「"key":"<장.절>","chapter"」(설명서 로더를 이 가지가 모른다 — 글로 읽는다)
                var keys = new System.Collections.Generic.HashSet<string>(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "\"key\"\\s*:\\s*\"([^\"]+)\"\\s*,\\s*\"chapter\"")
                    .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value));
                Assert.Greater(keys.Count, 50, $"rulebook.{lang}.json 절 행을 읽지 못했다");
                foreach (var c in chapters)
                    foreach (string k in c.Rulebook) Assert.IsTrue(keys.Contains(k), $"{c.Id}: 설명서 절 {k}가 rulebook.{lang}.json에 없다");
            }
            TestContext.WriteLine($"[M9 튜토리얼 설명서 절 키] 장 {chapters.Count} · 키 {chapters.Sum(c => c.Rulebook.Count)} · 대조한 설명서 언어 {checkedLangs}");
        }

        /// <summary>
        /// M9 「튜토리얼 → 설명서 더 보기」(07 T212 · T220): 튜토리얼 23장의 절 키 61개가 두 언어 설명서(로더 <see cref="Rulebook.Load"/>)에 모두 있고 제목이 있다 —
        /// 끝 창의 절 단추 글 · 목차 [?]가 여는 절. 설명서 표가 없으면 실패한다(위 시험과 달리 건너뛰지 않는다).
        /// </summary>
        [Test]
        public void M9_튜토리얼설명서_절키61개가두언어설명서에모두있다()
        {
            var chapters = TutorialChapters.Load(TestData.ReadText("tutorial_chapters.json"));
            Assert.AreEqual(23, chapters.Count);
            Assert.AreEqual(61, chapters.Sum(c => c.Rulebook.Count), "장 → 절 키 수(07 T212)");
            foreach (string lang in new[] { "ko", "en" })
            {
                string file = "rulebook." + lang + ".json";
                var book = Rulebook.Load(TestData.ReadText(file), file);
                foreach (var c in chapters)
                    foreach (string k in c.Rulebook)
                    {
                        var sec = book.Find(k);
                        Assert.IsNotNull(sec, $"{c.Id}: 설명서 절 {k}가 {file}에 없다");
                        Assert.IsFalse(string.IsNullOrWhiteSpace(sec.Title), $"{file} {k}: 제목이 비었다");
                        Assert.IsFalse(string.IsNullOrWhiteSpace(sec.ChapterTitle), $"{file} {k}: 장 제목이 비었다");
                    }
            }
            TestContext.WriteLine($"[M9 튜토리얼 설명서] 장 {chapters.Count} · 키 {chapters.Sum(c => c.Rulebook.Count)} · 다른 절 {chapters.SelectMany(c => c.Rulebook).Distinct().Count()} · 언어 2");
        }

        [Test]
        public void M65_빨리감기_앞장명령을보내면그장시작국면과같고_시계를다시맞춰도끝까지같다()
        {
            var script = TutorialLoader.Load(TestData.ReadText("tutorial/main.json"), ctx, "tutorial/main.json");
            TutorialRunner New() => new TutorialRunner(ctx, script, new PrincipleAgent(), TutorialDataTests.TrailAfter);

            // 한 단계씩(화면 없이 자동 진행과 같은 길) 3장 첫 단계까지
            var step = New();
            step.Begin();
            while (step.Step.Chapter < 3) step.Play();
            // 장 고르기: 3장보다 앞이면 보낸다
            var ff = New();
            ff.PlayWhile(s => s.Chapter < 3);
            Assert.AreEqual(step.Index, ff.Index);
            Assert.AreEqual(3, ff.Step.Chapter);
            Assert.AreEqual(step.Session.Hash, ff.Session.Hash, "빨리감기 = 그 장 시작 국면");
            Assert.AreEqual(step.Session.CommandCount, ff.Session.CommandCount);
            // [이 장 건너뛰기]: 2장 안에서 그 장 끝까지 — 다음 장(3장) 시작 국면
            var skip = New();
            skip.PlayWhile(s => s.Chapter < 2);
            skip.PlayWhile(s => s.Chapter == 2);
            Assert.AreEqual(ff.Index, skip.Index);
            Assert.AreEqual(ff.Session.Hash, skip.Session.Hash);

            // 화면에 넘기기 전 시계 다시 맞추기: 판·단계·대본은 그대로, 세션 시각은 0부터
            ulong hash = ff.Session.Hash;
            int index = ff.Index;
            Assert.Greater(ff.Session.AnimEndMs, 0, "빨리감기가 시계를 앞으로 보냈다");
            ff.Reclock(0);
            Assert.AreEqual(hash, ff.Session.Hash);
            Assert.AreEqual(index, ff.Index);
            Assert.AreEqual(0, ff.Session.AnimEndMs);

            step.PlayToEnd();
            ff.PlayToEnd();
            Assert.IsTrue(ff.Finished);
            CollectionAssert.IsEmpty(step.Failures);
            CollectionAssert.IsEmpty(ff.Failures, "다시 맞춘 판도 단계마다 expect = 엔진 값");
            Assert.AreEqual(0, ff.Blocked);
            Assert.AreEqual(step.Session.Hash, ff.Session.Hash, "끝 국면도 같다");
            Assert.Throws<InvalidOperationException>(() => New().Reclock(0), "시작 전에는 다시 맞출 판이 없다");
        }

        /// <summary>
        /// 화면에 넘기기(07 T60·T61 — 목차가 연 판에 층이 붙는 한 길): 빨리감기한 진행기를 화면 손님·화면 시계로 넘기면 그 시각에 판을 새로 열어 화면의 손님을 같이 쓰고,
        /// 그 뒤 진행기는 화면 시계 밑으로 세션을 돌리지 않으며(답 시각 ≥ 화면 시계) 세션을 앞으로 돌리면 화면 시계를 그만큼 당긴다. 막 앞 판 각본(preview2)은 다시 열 때도 같은 손님 길로 간다.
        /// 판(상태 해시)·단계 대조는 넘기지 않은 진행기와 같다.
        /// </summary>
        [Test]
        public void M65_화면에넘기기_화면손님을같이쓰고_진행기시계는화면시계밑으로가지않으며_앞서면당긴다()
        {
            var script = TutorialLoader.Load(TestData.ReadText("tutorial/main.json"), ctx, "tutorial/main.json");
            var plain = new TutorialRunner(ctx, script, new PrincipleAgent(), TutorialDataTests.TrailAfter);
            plain.PlayWhile(s => s.Chapter < 3);
            var run = new TutorialRunner(ctx, script, new PrincipleAgent(), TutorialDataTests.TrailAfter);
            run.PlayWhile(s => s.Chapter < 3);
            long screen = 5000;
            var attachedAt = new System.Collections.Generic.List<long>();
            Sinnoh.Rules.Session.SeatClient mine = null;
            run.Hand((session, ms) =>
            {
                attachedAt.Add(ms);
                return mine = new Sinnoh.Rules.Session.SeatClient(session.Link, ctx, TutorialRunner.Me);
            }, () => screen, ms => screen = ms, 5000);
            Assert.AreSame(mine, run.Client, "화면의 손님을 같이 쓴다");
            CollectionAssert.AreEqual(new[] { 5000L }, attachedAt, "넘긴 시각에 판을 새로 연다");
            Assert.AreEqual(plain.Session.Hash, run.Session.Hash, "판은 그대로");
            Assert.Throws<InvalidOperationException>(() => run.Hand((s, ms) => mine, () => screen, ms => { }), "두 번 넘기지 않는다");

            screen = 900000;   // 화면 시계가 한참 앞서 있다(연출을 보는 동안)
            run.Play();        // S301 [다음]
            run.Play();        // S302 걷기 — 답 시각은 화면 시계 밑으로 가지 않는다
            Assert.GreaterOrEqual(run.Session.AnimEndMs, 900000L, "진행기 답이 화면 시계보다 앞선 시각에 났다");
            while (!run.Finished && run.Step.Chapter < 5) run.Play();   // 3장 배틀 · 4장 포획(가상 응답자의 답을 진행기가 돌린다)
            Assert.Greater(screen, 900000L, "진행기가 세션을 앞으로 돌리면 화면 시계를 당긴다");
            run.PlayToEnd();
            plain.PlayToEnd();
            CollectionAssert.IsEmpty(run.Failures, string.Join("\n", run.Failures));
            Assert.AreEqual(plain.Session.Hash, run.Session.Hash, "끝 국면도 넘기지 않은 진행기와 같다");

            // 막 앞 판 각본: 판 차리기 단계 뒤 손보기로 다시 연 판도 화면 손님으로(넘긴 뒤 다시 연 시각 = 진행기 시계 ≥ 화면 시계)
            var p2 = TutorialLoader.Load(TestData.ReadText("tutorial/preview2.json"), ctx, "preview2.json");
            var pre = new TutorialRunner(ctx, p2, new PrincipleAgent(), TutorialDataTests.TrailAfter);
            pre.PlayWhile(s => !pre.Rebased);
            Assert.IsTrue(pre.Rebased, "판 차리기 demo 둘 뒤에 막 앞 판 손보기가 걸린다(07 T62)");
            Assert.AreEqual("S711", pre.Step.Id, "손보기 뒤 첫 단계(옮긴 소개 말풍선)부터 화면에 보인다(07 T160)");
            attachedAt.Clear();
            screen = 0;
            pre.Hand((session, ms) =>
            {
                attachedAt.Add(ms);
                return mine = new Sinnoh.Rules.Session.SeatClient(session.Link, ctx, TutorialRunner.Me);
            }, () => screen, ms => screen = ms);
            pre.PlayToEnd();
            CollectionAssert.IsEmpty(pre.Failures, string.Join("\n", pre.Failures));
            Assert.AreEqual(1, attachedAt.Count, "손보기는 넘기기 전에 걸려 다시 열지 않는다");
            Assert.AreSame(mine, pre.Client);
            Assert.AreEqual(3, pre.Session.HostCopy().Hos.Act, "3막 앞 판(막 길 heart)");
        }
    }
}
