using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 「규칙 판정」 화면(결정 213 — <see cref="RulingsScreen"/>): 타이틀 → 화면 → 묶음 · 절 · 찾기 · 출처 거르기 · 없음 줄 · Esc, 보드 → 게임 메뉴 [규칙 판정] → [닫기].
    /// 보기만 하는 화면이라 판에 답을 내지 않는다. 화면 요소 · 화면 함수로만 누른다(창 없는 에디터는 패널에 포인터를 넣지 못한다). Gen 겉모양이 있어야 돈다.
    /// </summary>
    public sealed class RulingsFlowTests
    {
        const int W = 1920, H = 1080;
        const string Joiner = "⁠";

        ScreenBoot boot;
        GameFlow flow;
        UiStage stage;
        RenderTexture rt;
        readonly List<string> errors = new List<string>();

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
        }

        IEnumerator Boot()
        {
            errors.Clear();
            Application.logMessageReceived += Grab;
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            flow = boot.Flow;
            stage = boot.Root.Stage;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
        }

        [TearDown]
        public void Shut()
        {
            Application.logMessageReceived -= Grab;
            var b = Object.FindAnyObjectByType<ScreenBoot>();
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                rt = null;
            }
        }

        int TitleRow(string id)
        {
            var title = (TitleScreen)stage.Current;
            for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == id) return i;
            return -1;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        // 목록에 선 줄의 물음(낱말 이음표 U+2060은 뺀다)
        List<string> RowQuestions() => stage.Root.Q<ListView>("rulings-list").Query<Label>("rulings-item-q").ToList()
            .Where(l => l.worldBound.height > 0).Select(l => l.text.Replace(Joiner, "")).ToList();

        [UnityTest]
        public IEnumerator M10_결정213_규칙판정을타이틀에서열어묶음절찾기출처로거르고Esc로닫는다()
        {
            yield return Boot();
            var names = boot.BookJson.Where(t => t != null).Select(t => t.name).ToList();
            CollectionAssert.IsSubsetOf(new[] { "rulings.ko", "rulings.en" }, names, "씬이 판정 표 두 언어를 가리킨다");
            var table = flow.RulingsOf("ko");
            Assert.That(table, Is.Not.Null);
            Assert.That(flow.RulingsOf("en"), Is.Not.Null);

            int row = TitleRow(TitleScreen.Rulings);
            Assert.That(row, Is.GreaterThanOrEqualTo(0), "타이틀에 「규칙 판정」 줄");
            Assert.That(((TitleScreen)stage.Current).Activate(row), Is.True);
            yield return Frames(6);
            var r = stage.Current as RulingsScreen;
            Assert.That(r, Is.Not.Null, "화면이 열렸다");
            Assert.That(r.InGame, Is.False);
            Assert.That(stage.Root.Q<Button>("rulings-close").text, Is.EqualTo(stage.T("screen.common.back")));
            Assert.That(r.Shown.Count, Is.EqualTo(table.Items.Count), "처음 = 전체 · 출처 모두 켬");
            Assert.That(stage.Root.Q("rulings-group-all").ClassListContains("sn-book-row-picked"), Is.True);
            Assert.That(stage.Root.Q<Label>("rulings-count").text, Is.EqualTo(stage.T("screen.rulings.count").Replace("{n}", table.Items.Count.ToString())));
            var list = stage.Root.Q<ListView>("rulings-list");
            var made = list.Query(className: "rulings-item").ToList();
            Assert.That(made.Count, Is.GreaterThan(0).And.LessThan(80), "보이는 줄만 만든다(전체 " + table.Items.Count + ")");
            Assert.That(RowQuestions(), Does.Contain(table.Items[0].Q), "첫 물음이 보인다");
            Assert.That(list.Q<ScrollView>().verticalScroller.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "엔진 스크롤 막대 없음");
            var field = stage.Root.Q<TextField>("rulings-find");

            // 묶음 → 그 묶음의 절이 펴진다 → 절 하나
            var g = table.Groups[1];
            Assert.That(r.Pick(g.Key), Is.True);
            yield return Frames(6);
            Assert.That(r.Shown.Count, Is.EqualTo(g.Count));
            Assert.That(stage.Root.Q("rulings-section-" + g.Sections[0].Key), Is.Not.Null, "고른 묶음의 절");
            Assert.That(stage.Root.Q("rulings-section-" + table.Groups[0].Sections[0].Key), Is.Null, "다른 묶음은 접혔다");
            Assert.That(r.Pick(g.Key, g.Sections[1].Key), Is.True);
            yield return Frames(6);
            Assert.That(r.Shown.Count, Is.EqualTo(g.Sections[1].Count));
            Assert.That(r.Shown.All(x => x.Section == g.Sections[1].Key), Is.True);
            Assert.That(stage.Root.Q("rulings-section-" + g.Sections[1].Key).ClassListContains("sn-book-row-picked"), Is.True);
            Assert.That(RowQuestions(), Does.Contain(r.Shown[0].Q));
            Assert.That(r.Pick("nope"), Is.False);
            Assert.That(r.Pick(g.Key, table.Groups[0].Sections[0].Key), Is.False, "다른 묶음의 절");
            Assert.That(r.Pick(null), Is.True);

            // 찾기: 글 칸은 그대로(다시 만들지 않는다) · 목록 · 수만 바뀐다
            string[] words = table.Items[0].Q.Split(' ');
            string query = words[0].ToUpperInvariant() + "  " + words[words.Length - 1];
            field.value = query;
            yield return Frames(6);
            Assert.That(stage.Root.Q<TextField>("rulings-find"), Is.SameAs(field), "글 칸을 다시 만들지 않는다");
            Assert.That(r.Query, Is.EqualTo(query));
            Assert.That(r.Shown.Count, Is.GreaterThan(0).And.LessThan(table.Items.Count));
            Assert.That(r.Shown[0], Is.SameAs(table.Items[0]));
            CollectionAssert.AreEqual(table.Filter(query).Select(x => x.Id).ToList(), r.Shown.Select(x => x.Id).ToList());
            Assert.That(RowQuestions(), Does.Contain(table.Items[0].Q));
            Assert.That(stage.Root.Q<Label>("rulings-count").text, Is.EqualTo(stage.T("screen.rulings.count").Replace("{n}", r.Shown.Count.ToString())));

            // 없는 말 → 「없음」 줄
            r.Find("zzzz qqqq");
            yield return Frames(3);
            Assert.That(field.value, Is.EqualTo("zzzz qqqq"));
            Assert.That(r.Shown.Count, Is.EqualTo(0));
            Assert.That(stage.Root.Q("rulings-none").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(stage.Root.Q<Label>("rulings-none").text.Replace(Joiner, ""), Is.EqualTo(stage.T("screen.rulings.none")));
            r.Find("");
            yield return Frames(3);
            Assert.That(stage.Root.Q("rulings-none").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            // 출처 거르기: 켜고 끄기 — 꼬리표와 마우스 오버
            var house = stage.Root.Q<Button>("rulings-source-house");
            Assert.That(house.text, Is.EqualTo(stage.T("screen.rulings.source.house")));
            r.Toggle(RulingSource.House);
            r.Toggle(RulingSource.Rulebook);
            yield return Frames(6);
            Assert.That(house.ClassListContains("sn-rule-src-off"), Is.True);
            Assert.That(stage.Root.Q("rulings-source-creator").ClassListContains("sn-rule-src-off"), Is.False);
            Assert.That(r.Shown.Count, Is.EqualTo(table.Items.Count(x => x.Source == RulingSource.Creator)));
            var badges = list.Query<Label>("rulings-item-source").ToList().Where(b => b.worldBound.height > 0).ToList();
            Assert.That(badges.Count, Is.GreaterThan(0));
            Assert.That(badges.All(b => b.text == stage.T("screen.rulings.source.creator")), Is.True, "켠 출처의 꼬리표만");
            stage.Tips.Show(badges[0]);
            Assert.That(stage.Tips.Target, Is.SameAs(badges[0]), "꼬리표의 마우스 오버");
            stage.Tips.Hide();
            r.Toggle(RulingSource.House);
            r.Toggle(RulingSource.Rulebook);
            Assert.That(r.SourceMask, Is.EqualTo(Rulings.AllSources));

            // Esc → 타이틀의 그 줄
            Assert.That(flow.Escape(), Is.True);
            yield return null;
            Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
            Assert.That(((TitleScreen)stage.Current).Picked, Is.EqualTo(TitleRow(TitleScreen.Rulings)));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_결정213_게임메뉴에서규칙판정을열고닫는다()
        {
            yield return Boot();
            Assert.That(((TitleScreen)stage.Current).Activate(TitleRow(TitleScreen.Solo)), Is.True);
            yield return null;
            FlowPilot.Start(flow, SoloTrail.Defeat);
            yield return null;
            for (int i = 0; i < 200 && !flow.InputOpen; i++)
            {
                flow.Skip(100);
                yield return null;
            }
            Assert.That(flow.InputOpen, Is.True);
            int commands = flow.Session.CommandCount;

            flow.OpenMenu();
            yield return null;
            var button = stage.Root.Q<Button>("board-menu-rulings");
            Assert.That(button, Is.Not.Null, "메뉴의 [규칙 판정]");
            Assert.That(button.text, Is.EqualTo(stage.T("screen.board.menu.rulings")));
            Assert.That(flow.OpenMenuRulings(), Is.True);
            yield return Frames(6);
            var r = stage.Current as RulingsScreen;
            Assert.That(r, Is.Not.Null);
            Assert.That(r.InGame, Is.True);
            Assert.That(flow.MenuUp, Is.False, "메뉴는 닫혔다");
            Assert.That(stage.Root.Q<Button>("rulings-close").text, Is.EqualTo(stage.T("screen.rulebook.close")));
            Assert.That(flow.InputOpen, Is.False, "창이 떠 있는 동안 보드 입력은 꺼진다");
            Assert.That(flow.OpenGameRulings(), Is.True, "이미 떠 있으면 그대로");
            Assert.That(stage.Current, Is.SameAs(r));
            Assert.That(RowQuestions().Count, Is.GreaterThan(0));
            Assert.That(GameFlow.KeptOnRewind(r), Is.True, "판과 무관한 보기 창");
            Assert.That(flow.Escape(), Is.True);
            yield return null;
            Assert.That(stage.Current, Is.Null);
            Assert.That(flow.InputOpen, Is.True);
            Assert.That(flow.OpenGameRulings(), Is.True);
            yield return null;
            flow.CloseRulings();
            yield return null;
            Assert.That(stage.Current, Is.Null);
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "보기만 한다 — 답을 내지 않는다");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
