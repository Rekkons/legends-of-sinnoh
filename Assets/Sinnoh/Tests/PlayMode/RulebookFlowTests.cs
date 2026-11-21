using System;
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
    /// 룰 설명서 S43(결정 100 · 111 ① — 큰 창 B · 게임 중 옆 판 C)과 크레딧 S03(M9 「설명서 화면 S43 · 크레딧 S03」): 타이틀 → 큰 창 → 목차 · 링크 · 찾기 · 고침 배지 · Esc ·
    /// 보드 → 옆 판(칩 · 펼친 절 · 찾기) → [크게 보기] · 게임 메뉴 [룰 설명서] · 타이틀 → 크레딧. 보기만 하는 화면이라 판에 답을 내지 않는다.
    /// 화면 요소 · 화면 함수로만 누른다(창 없는 에디터는 패널에 포인터를 넣지 못한다). Gen 겉모양이 있어야 돈다.
    /// </summary>
    public sealed class RulebookFlowTests
    {
        const int W = 1920, H = 1080;

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
                boot = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
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
            var b = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
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

        // 그 요소 아래 보이는 글 전부(리치 태그 그대로 · 감기는 글의 낱말 이음표 U+2060은 뺀다)
        static string Texts(VisualElement root) => string.Join("\n", root.Query<TextElement>().ToList().Select(t => t.text.Replace("⁠", "")));

        [UnityTest]
        public IEnumerator M9_룰설명서를타이틀에서열어목차링크찾기고침배지를보고Esc로닫는다()
        {
            yield return Boot();
            // 플레이어는 씬이 가리키는 표만 읽는다(설명서 두 언어 · 크레딧)
            var names = boot.BookJson.Where(t => t != null).Select(t => t.name).ToList();
            CollectionAssert.IsSubsetOf(new[] { "rulebook.ko", "rulebook.en", "credits" }, names, "씬이 설명서 · 크레딧 표를 가리킨다");
            Assert.That(flow.BookLang, Is.EqualTo("ko"), "설명서 언어 = 화면 문자열 표의 언어");
            var book = flow.Book("ko");

            int row = TitleRow(TitleScreen.Rulebook);
            Assert.That(row, Is.GreaterThanOrEqualTo(0), "타이틀에 「룰 설명서」 줄");
            Assert.That(((TitleScreen)stage.Current).Activate(row), Is.True);
            yield return null;
            var b = stage.Current as RulebookScreen;
            Assert.That(b, Is.Not.Null, "큰 창이 열렸다");
            Assert.That(b.InGame, Is.False);
            Assert.That(stage.Root.Q<Button>("rulebook-close").text, Is.EqualTo(stage.T("screen.common.back")));
            Assert.That(b.Current, Is.SameAs(book.Sections[0]), "처음 = 첫 절");
            Assert.That(stage.Root.Q("rulebook-section-" + book.Sections[0].Key).ClassListContains("sn-book-row-picked"), Is.True, "목차에 보는 절");
            Assert.That(stage.Root.Q("rulebook-chapter-trainer_cards"), Is.Not.Null, "확장 장도 목차에");

            // 이전/다음: 첫 절에서 이전 없음 · 다음 = 표 순서 다음 절
            Assert.That(b.Step(-1), Is.False);
            Assert.That(stage.Root.Q<Button>("rulebook-prev").enabledSelf, Is.False);
            Assert.That(b.Step(1), Is.True);
            Assert.That(b.Current, Is.SameAs(book.Sections[1]));

            // 링크: 그 절 글의 「[유물 참조]」 → 유물 절(목차가 그 장을 펼친다)
            Assert.That(b.Show("pokemon.encounter"), Is.True);
            yield return null;
            CollectionAssert.Contains(b.View.Links, "hidden_spots.artifacts");
            StringAssert.Contains("<link=\"hidden_spots.artifacts\">", Texts(stage.Root.Q("rulebook-body")), "본문 링크 태그");
            Assert.That(stage.Root.Q("rulebook-digital"), Is.Not.Null, "디지털판 꼬리표");
            Assert.That(b.Show("hidden_spots.artifacts"), Is.True);
            yield return null;
            Assert.That(b.Open, Does.Contain("hidden_spots"));
            Assert.That(stage.Root.Q("rulebook-section-hidden_spots.artifacts").ClassListContains("sn-book-row-picked"), Is.True);
            Assert.That(stage.Root.Q<Label>("rulebook-heading").text, Is.EqualTo(book.Find("hidden_spots.artifacts").Title));
            // 장 접기 · 펴기
            b.Toggle("hidden_spots");
            Assert.That(stage.Root.Q("rulebook-section-hidden_spots.artifacts"), Is.Null, "접은 장의 절은 목차에 없다");
            b.Toggle("hidden_spots");
            Assert.That(stage.Root.Q("rulebook-section-hidden_spots.artifacts"), Is.Not.Null);

            // 찾기: 결과 = 절마다 n곳(Data의 찾기와 같다) · 결과를 누르면 그 절 · 본문 칠하기 · 비우면 목차로
            stage.Root.Q<TextField>("rulebook-find").value = "규토리";
            yield return null;
            yield return null;
            Assert.That(b.Query, Is.EqualTo("규토리"), "찾기 칸이 찾기를 건다");
            var want = book.Search("규토리", s => RulebookView.CardTexts(stage, s));
            Assert.That(b.Hits.Select(h => h.Section.Key + h.Count), Is.EqualTo(want.Select(h => h.Section.Key + h.Count)));
            Assert.That(stage.Root.Q<Label>("rulebook-count").text.Replace("⁠", ""), Does.Contain(want.Sum(h => h.Count).ToString()));
            Assert.That(stage.Root.Q("rulebook-chapter-overview"), Is.Null, "찾는 동안 목차 자리에 결과");
            foreach (var h in want) Assert.That(stage.Root.Q("rulebook-hit-" + h.Section.Key), Is.Not.Null, h.Section.Key);
            Assert.That(b.Show("pokemon.apricorns"), Is.True);
            yield return null;
            StringAssert.Contains("<mark=", Texts(stage.Root.Q("rulebook-body")), "본문에서 찾은 말을 칠한다");
            b.Find("");
            yield return null;
            Assert.That(b.Hits, Is.Empty);
            Assert.That(stage.Root.Q("rulebook-chapter-overview"), Is.Not.Null, "비우면 목차");

            // 결정 163: 한국어도 영문 원본에서 옮긴 글이라 「고침」 배지가 없다 — 옛 고침 자리(은행상회 A4)의 글은 그대로 보인다
            Assert.That(b.Show("overview.village"), Is.True);
            yield return null;
            StringAssert.Contains("하나 또는 둘 다", Texts(stage.Root.Q("rulebook-body")));
            Assert.That(stage.Root.Query<VisualElement>().Where(e => e.name != null && e.name.StartsWith("rulebook-fix-", StringComparison.Ordinal)).ToList(), Is.Empty, "고침 배지 없음");

            // cards 블록: 게임 표에서 그린다
            Assert.That(b.Show("recipes.balls"), Is.True);
            yield return null;
            Assert.That(stage.Root.Q("rulebook-card-item.poke_ball"), Is.Not.Null);
            StringAssert.Contains(stage.T("item.poke_ball.text"), Texts(stage.Root.Q("rulebook-card-item.poke_ball")).Replace("⁠", ""));

            // Esc → 타이틀의 그 줄 · 다시 열면 마지막 절
            Assert.That(flow.Escape(), Is.True);
            yield return null;
            Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
            Assert.That(((TitleScreen)stage.Current).Picked, Is.EqualTo(TitleRow(TitleScreen.Rulebook)));
            flow.OpenTitleRulebook();
            yield return null;
            Assert.That(((RulebookScreen)stage.Current).Current.Key, Is.EqualTo("recipes.balls"), "마지막으로 본 절");
            flow.CloseRulebook();
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M9_게임중옆판칩을보고크게보기와게임메뉴로큰창을연다()
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
            var button = stage.Root.Q<Button>("board-hud-book");
            Assert.That(button, Is.Not.Null, "위 띠의 [설명서]");
            Assert.That(button.text, Is.EqualTo(RulebookView.Help));

            // 옆 판: HUD 위(창이 아니다 — 보드 입력은 열려 있다) · 지금 칸(고향 마을) 칩 · 첫 칩의 절 펼침
            Assert.That(flow.ToggleBookSide(), Is.True);
            flow.Skip(16);
            yield return null;
            var side = flow.BookSide;
            var panel = stage.Root.Q("rulebook-side");
            Assert.That(panel, Is.Not.Null, "옆 판이 붙었다");
            Assert.That(panel.parent, Is.SameAs(stage.Root.Q(stage.Hud.Name)), "보드 HUD의 자식");
            Assert.That(stage.Current, Is.Null);
            Assert.That(flow.InputOpen, Is.True, "옆 판이 열려도 보드 입력은 열려 있다");
            Assert.That(side.Chips, Does.Contain("overview.village"), "고향 마을 칸 = 마을 절 칩");
            Assert.That(side.Chips.Count, Is.LessThanOrEqualTo(RulebookChips.Max));
            Assert.That(side.Picked, Is.EqualTo(side.Chips[0]));
            Assert.That(stage.Root.Q("rulebook-side-open"), Is.Not.Null, "펼친 절");
            Assert.That(stage.Root.Q("rulebook-chip-" + side.Chips[0]).ClassListContains("sn-opt-picked"), Is.True);
            // 다른 칩 · 찾기
            Assert.That(side.Pick(side.Chips[1]), Is.True);
            Assert.That(side.Picked, Is.EqualTo(side.Chips[1]));
            Assert.That(stage.Root.Q("rulebook-fold-" + side.Chips[1]).ClassListContains("sn-book-row-picked"), Is.True);
            side.Find("회복");
            yield return null;
            Assert.That(side.Hits.Count, Is.GreaterThan(0));
            Assert.That(stage.Root.Q("rulebook-fold-" + side.Hits[0].Section.Key), Is.Not.Null, "찾은 절 줄");
            string picked = side.Picked;
            // HUD가 다시 지어져도 다음 프레임에 다시 붙는다
            stage.Bind(stage.Root, stage.Grade);   // 패널이 뿌리를 다시 준 때와 같다(HUD · 창을 다시 짓는다)
            flow.Skip(16);
            yield return null;
            Assert.That(stage.Root.Q("rulebook-side"), Is.Not.Null, "새 HUD에 다시 붙었다");

            // [크게 보기] → 큰 창(펼친 절) · Esc → 보드(옆 판은 그대로)
            Assert.That(side.Big(), Is.True);
            yield return null;
            var b = stage.Current as RulebookScreen;
            Assert.That(b, Is.Not.Null);
            Assert.That(b.InGame, Is.True);
            Assert.That(b.Current.Key, Is.EqualTo(picked));
            Assert.That(stage.Root.Q<Button>("rulebook-close").text, Is.EqualTo(stage.T("screen.rulebook.close")));
            Assert.That(flow.InputOpen, Is.False, "큰 창이 떠 있는 동안 보드 입력은 꺼진다");
            Assert.That(flow.OpenGameRulebook(), Is.True, "이미 떠 있으면 그대로(결정 125 — 두 번 열지 않는다)");
            Assert.That(stage.Current, Is.SameAs(b));
            Assert.That(stage.Info, Is.Null);
            Assert.That(flow.Escape(), Is.True);
            yield return null;
            Assert.That(stage.Current, Is.Null);
            Assert.That(flow.InputOpen, Is.True);
            Assert.That(side.IsOpen, Is.True);

            // 게임 메뉴 [룰 설명서] → 메뉴를 닫고 큰 창 → [닫기] → 보드
            flow.OpenMenu();
            yield return null;
            Assert.That(stage.Root.Q<Button>("board-menu-rulebook"), Is.Not.Null, "메뉴의 [룰 설명서]");
            Assert.That(flow.OpenMenuRulebook(), Is.True);
            yield return null;
            Assert.That(stage.Current, Is.InstanceOf<RulebookScreen>());
            Assert.That(flow.MenuUp, Is.False, "메뉴는 닫혔다");
            flow.CloseRulebook();
            yield return null;
            Assert.That(stage.Current, Is.Null);

            // 옆 판 닫기 → 떨어진다 · 보기만 했다
            Assert.That(flow.ToggleBookSide(), Is.False);
            flow.Skip(16);
            yield return null;
            Assert.That(stage.Root.Q("rulebook-side"), Is.Null);
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "보기만 한다 — 답을 내지 않는다");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M9_크레딧을타이틀에서열어묶음을고르고Esc로닫는다()
        {
            yield return Boot();
            var table = flow.Credits();
            Assert.That(table, Is.Not.Null);
            int row = TitleRow(TitleScreen.Credits);
            Assert.That(row, Is.GreaterThanOrEqualTo(0));
            Assert.That(((TitleScreen)stage.Current).Ready(((TitleScreen)stage.Current).Shown[row]), Is.True, "「크레딧」 줄이 켜졌다");
            Assert.That(((TitleScreen)stage.Current).Activate(row), Is.True);
            yield return null;
            var c = stage.Current as CreditsScreen;
            Assert.That(c, Is.Not.Null);
            Assert.That(c.Picked.Id, Is.EqualTo("original"));
            Assert.That(stage.Root.Q<Label>("credits-notice").text.Replace("⁠", ""), Is.EqualTo(stage.T("screen.credits.notice")), "라이선스 문구 자리");
            foreach (string file in new[] { @"LICENSE(\.ko)?\.md", @"THIRD-PARTY-NOTICES(\.ko)?\.md" }) StringAssert.IsMatch(file, stage.Root.Q<Label>("credits-license").text.Replace("⁠", ""), "라이선스 전문이 있는 파일(plan §5 #534 — 한국어는 LICENSE.ko.md · THIRD-PARTY-NOTICES.ko.md, 결정 138 · 152)");
            foreach (var s in table.Sections) Assert.That(stage.Root.Q("credits-tab-" + s.Id), Is.Not.Null, s.Id);
            Assert.That(c.Pick("art"), Is.True);
            yield return null;
            var art = table.Sections.First(s => s.Id == "art");
            Assert.That(stage.Root.Q("credits-entry-" + (art.Entries.Count - 1)), Is.Not.Null, "한 사람(곳)마다 한 칸");
            int smogon = art.Entries.ToList().FindIndex(e => e.Who == "Smogon Sprite Project");
            string text = Texts(stage.Root.Q("credits-entry-" + smogon)).Replace("⁠", "");
            StringAssert.Contains(art.Entries[smogon].Role, text, "역할 한 줄");
            StringAssert.Contains("leParagon", text, "작가 이름 줄");
            Assert.That(c.Pick("nope"), Is.False);
            Assert.That(flow.Escape(), Is.True);
            yield return null;
            Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
            Assert.That(((TitleScreen)stage.Current).Picked, Is.EqualTo(TitleRow(TitleScreen.Credits)));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
