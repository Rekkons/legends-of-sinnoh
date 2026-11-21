using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M9 「튜토리얼 → 설명서 더 보기」(07 T212 · T220): 목차의 완료한 장 [?]와 끝 창(본편 · 확장 장)의 절 단추가 룰 설명서 큰 창(S43 B)을 그 장의 절로 덮어 열고,
    /// 닫으면([닫기] · Esc) 밑의 목차 · 끝 창으로 돌아온다. 기록은 실행하는 동안만(사용자 기록에 쓰지 않는다).
    /// </summary>
    public sealed class TutorialBookFlowTests
    {
        static IEnumerator Boot(List<RenderTexture> rts)
        {
            yield return TutorialLayerFlowTests.Open();
            var boot = Object.FindAnyObjectByType<ScreenBoot>();
            Assert.That(boot?.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            boot.PointerFromMouse = false;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            rts.Add(rt);
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            boot.Flow.Tutorial.Record = TutorialRecord.Memory();
        }

        static void Release(List<RenderTexture> rts)
        {
            var boot = Object.FindAnyObjectByType<ScreenBoot>();
            if (boot != null && boot.Flow != null)
            {
                boot.Flow.OpenTitle();
                if (boot.Flow.Lang != "ko") boot.Flow.Lang = "ko";
            }
            if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
            foreach (var rt in rts)
            {
                rt.Release();
                Object.Destroy(rt);
            }
        }

        [UnityTest]
        public IEnumerator M9_목차의완료한장에서설명서를열고_닫으면목차_영어도같다_에러로그0()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            var rts = new List<RenderTexture>();
            try
            {
                yield return Boot(rts);
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var tut = flow.Tutorial;
                Assert.That(flow.Book("ko"), Is.Not.Null, "씬이 설명서(rulebook.ko.json)를 가리키지 않는다");
                Assert.That(flow.Book("en"), Is.Not.Null, "씬이 설명서(rulebook.en.json)를 가리키지 않는다");
                tut.Record.Enter("main0");
                tut.Record.MarkSeen("main0");
                tut.Record.MarkSeen("e04");
                tut.Record.Enter("main3");

                foreach (string lang in new[] { "ko", "en" })
                {
                    flow.Lang = lang;
                    Assert.That(flow.Lang, Is.EqualTo(lang));
                    flow.OpenTutorialMenu();
                    yield return null;
                    var menu = (TutorialMenuScreen)stage.Current;
                    // 완료한 장에만 [?]
                    Assert.That(stage.Root.Q<Button>("tut-menu-book-main0"), Is.Not.Null, lang + ": 완료한 본편 장의 단추");
                    Assert.That(stage.Root.Q<Button>("tut-menu-book-e04"), Is.Not.Null, lang + ": 완료한 확장 장의 단추");
                    Assert.That(stage.Root.Q("tut-menu-book-main3"), Is.Null, "이어서 장에는 없다");
                    Assert.That(stage.Root.Q("tut-menu-book-main1"), Is.Null, "안 본 장에는 없다");
                    Assert.That(menu.More("main1"), Is.False);
                    // 단추의 클릭은 줄 누르기(장 시작)로 번지지 않는다
                    var button = stage.Root.Q<Button>("tut-menu-book-main0");
                    using (var click = ClickEvent.GetPooled())
                    {
                        click.target = button;
                        button.SendEvent(click);
                    }
                    yield return null;
                    Assert.That(tut.Running, Is.False, "[?]를 눌러도 그 장이 시작되지 않는다");
                    Assert.That(stage.Current, Is.SameAs(menu));

                    // 본편 0장 → 첫 절 overview.goal
                    Assert.That(menu.More("main0"), Is.True);
                    yield return null;
                    Assert.That(tut.BookUp, Is.True);
                    Assert.That(tut.Book.Current.Key, Is.EqualTo("overview.goal"));
                    Assert.That(tut.Book.Lang, Is.EqualTo(lang), "설명서 언어 = 게임 언어");
                    Assert.That(stage.Current, Is.SameAs(menu), "목차는 밑에 그대로");
                    var book = stage.Root.Q("rulebook");
                    Assert.That(book, Is.Not.Null);
                    Assert.That(stage.Root.IndexOf(book), Is.GreaterThan(stage.Root.IndexOf(stage.Root.Q("tut-menu"))), "설명서가 목차 위");
                    Assert.That(stage.Root.Q<Label>("rulebook-heading").text, Is.EqualTo(flow.Book(lang).Find("overview.goal").Title));
                    Assert.That(menu.More("e04"), Is.False, "떠 있는 동안 또 열지 않는다");
                    // Esc = 닫기 → 목차
                    Assert.That(flow.Escape(), Is.True);
                    yield return null;
                    Assert.That(tut.BookUp, Is.False);
                    Assert.That(stage.Root.Q("rulebook"), Is.Null);
                    Assert.That(stage.Current, Is.SameAs(menu), "닫으면 목차");
                    Assert.That(stage.Root.Q("tut-menu-col"), Is.Not.Null);

                    // 확장 장 e04 → pokemon.evolution, [닫기](같은 함수)
                    Assert.That(menu.More("e04"), Is.True);
                    yield return null;
                    Assert.That(tut.Book.Current.Key, Is.EqualTo("pokemon.evolution"));
                    Assert.That(tut.Book.Show("crafting.stews"), Is.True, "그 장의 나머지 절은 설명서 안에서");
                    tut.CloseBook();
                    Assert.That(stage.Root.Q("rulebook"), Is.Null);
                    Assert.That(stage.Current, Is.SameAs(menu));

                    // 설명서를 띄운 채 목차를 떠나면 같이 닫힌다
                    Assert.That(menu.More("main0"), Is.True);
                    menu.Back();
                    yield return null;
                    Assert.That(tut.BookUp, Is.False);
                    Assert.That(stage.Root.Q("rulebook"), Is.Null);
                    Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
                }
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                Release(rts);
            }
        }

        [UnityTest]
        public IEnumerator M9_끝창에서설명서절을열고_닫으면끝창_본편과확장장_에러로그0()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            var rts = new List<RenderTexture>();
            try
            {
                yield return Boot(rts);
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var tut = flow.Tutorial;
                var data = flow.Book("ko");
                Assert.That(data, Is.Not.Null);

                foreach (var (chapter, keys, main) in new[]
                {
                    ("main7", new[] { "heart.shards", "heart.heart", "heart.choice" }, true),
                    ("e04", new[] { "pokemon.evolution", "crafting.stews" }, false),
                })
                {
                    Assert.That(tut.Start(chapter), Is.True, chapter);
                    yield return null;
                    Assert.That(tut.SkipChapter(), Is.True, chapter + " 건너뛰기");
                    yield return null;
                    Assert.That(tut.Ending, Is.True, chapter + " 끝 창");
                    Assert.That(stage.Root.Q("tut-end-title") != null, Is.EqualTo(main));
                    var box = stage.Root.Q("tut-end-book");
                    Assert.That(box, Is.Not.Null, chapter + ": 끝 창의 「설명서에서 더 보기」");
                    for (int i = 0; i < keys.Length; i++)
                        Assert.That(box.Q<Button>("tut-end-book-" + i)?.text, Is.EqualTo(data.Find(keys[i]).Title), chapter + " 절 단추 " + i);
                    Assert.That(box.Q("tut-end-book-" + keys.Length), Is.Null);

                    // 절마다: 열기 → 그 절 → Esc → 끝 창 그대로
                    foreach (string key in keys)
                    {
                        Assert.That(tut.OpenBook(key), Is.True, key);
                        yield return null;
                        Assert.That(tut.BookUp && tut.Ending && tut.DialogUp, Is.True);
                        Assert.That(tut.Book.Current.Key, Is.EqualTo(key));
                        var book = stage.Root.Q("rulebook");
                        Assert.That(stage.Root.IndexOf(book), Is.GreaterThan(stage.Root.IndexOf(stage.Root.Q("tut-end"))), "설명서가 끝 창 위");
                        Assert.That(flow.MenuUp && !flow.InputOpen, Is.True, "판은 서 있다");
                        Assert.That(flow.Escape(), Is.True, "Esc = 설명서 닫기");
                        yield return null;
                        Assert.That(tut.BookUp, Is.False);
                        Assert.That(stage.Root.Q("rulebook"), Is.Null);
                        Assert.That(tut.Ending, Is.True, "닫으면 끝 창");
                        Assert.That(stage.Root.Q("tut-end-panel"), Is.Not.Null);
                    }
                    Assert.That(flow.Escape(), Is.False, "끝 창은 Esc로 닫지 않는다");
                    Assert.That(tut.OpenBook("no.such"), Is.False);

                    // 설명서를 띄운 채 [목차로]: 같이 닫힌다
                    Assert.That(tut.OpenBook(keys[0]), Is.True);
                    tut.ToMenu();
                    yield return null;
                    Assert.That(tut.BookUp, Is.False);
                    Assert.That(stage.Root.Q("rulebook"), Is.Null);
                    Assert.That(stage.Current, Is.InstanceOf<TutorialMenuScreen>());
                }
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                Release(rts);
            }
        }
    }
}
