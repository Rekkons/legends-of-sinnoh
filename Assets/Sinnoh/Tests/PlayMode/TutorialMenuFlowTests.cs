using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M6.5 「화면 ② 목차·입구」(07 §2): 보드 씬의 화면 흐름으로 타이틀 [튜토리얼] → 기록이 없으면 본편 첫 장 · 있으면 목차 → 장 고르기(빨리감기) · 게임 메뉴의 [튜토리얼 끝내기] ·
    /// 끝내기 창(Esc = 계속하기 · [목차로] · [타이틀로]) · [이 장 건너뛰기](건너뛴 장은 완료 아님) → 본편 끝 창. 기록은 임시 폴더에 쓰고 지운다(에디터의 기본 기록은 실행하는 동안만).
    /// 단계는 목차가 연 판에 붙은 튜토리얼 층(<see cref="TutorialMenu.Layer"/>)의 [다음]으로 넘긴다(07 T60).
    /// </summary>
    public sealed class TutorialMenuFlowTests
    {
        static int Row(TitleScreen title, string id)
        {
            for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == id) return i;
            return -1;
        }

        [UnityTest]
        public IEnumerator M65_타이틀튜토리얼_기록이없으면본편첫장_있으면목차_장고르기_끝내기창_건너뛰기_끝창_에러로그0()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-tutorial-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
                ScreenBoot boot = null;
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    boot = Object.FindAnyObjectByType<ScreenBoot>();
                    if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
                }
                Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
                Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                Assert.That(boot.TutorialChaptersJson, Is.Not.Null, "씬이 튜토리얼 장 머리(tutorial_chapters.json)를 가리키지 않는다 — 플레이어에 튜토리얼이 없다");
                Assert.That(boot.TutorialScripts, Is.Not.Empty, "씬이 각본(tutorial/*.json)을 가리키지 않는다");
                foreach (string file in Directory.GetFiles(Path.Combine(Application.dataPath, "Sinnoh/Data/Json/tutorial"), "*.json"))
                {
                    string id = Path.GetFileNameWithoutExtension(file);
                    bool has = false;
                    foreach (var t in boot.TutorialScripts) has |= t != null && t.name == id;
                    Assert.That(has, Is.True, "씬이 각본 " + id + "을 가리키지 않는다(플레이어에 없다)");
                }
                Assert.That(ScreenBoot.DefaultTutorialFolder(), Is.Null, "에디터는 사용자의 튜토리얼 기록에 쓰지 않는다");
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                var flow = boot.Flow;
                var tut = flow.Tutorial;
                tut.Record = TutorialRecord.Open(root);
                Assert.That(tut.Available, Is.True, "씬의 장 머리·각본을 읽는다");

                // ── 기록이 없는 타이틀: 「튜토리얼」 줄 + 「처음이라면!」 → 본편 첫 장으로 바로 ──
                flow.OpenTitle();
                var title = (TitleScreen)stage.Current;
                int row = Row(title, TitleScreen.Tutorial);
                Assert.That(row, Is.EqualTo(Row(title, TitleScreen.Solo) + 1), "「혼자 하기」 바로 아래");
                Assert.That(title.Ready(title.Shown[row]), Is.True);
                Assert.That(title.FirstTimeShown, Is.True);
                Assert.That(stage.Root.Q("title-item-" + TitleScreen.Tutorial).Q("title-tutorial-first"), Is.Not.Null, "딱지는 그 줄 안");
                Assert.That(title.Activate(row), Is.True);
                yield return null;
                Assert.That(tut.Running, Is.True);
                Assert.That(tut.Chapter.Id, Is.EqualTo("main0"));
                Assert.That(flow.Session, Is.SameAs(tut.Runner.Session), "화면 흐름이 진행기의 판을 본다");
                Assert.That(flow.Session.Saving, Is.False, "튜토리얼 판은 저장하지 않는다(결정 90 Q6)");
                Assert.That(flow.SaveId, Is.Null);
                Assert.That(stage.Current, Is.InstanceOf<SetupVillageScreen>(), "0장은 게임 준비 앞 — 고향 마을 고르기 화면");
                Assert.That(File.ReadAllText(Path.Combine(root, TutorialRecord.FileName)), Is.EqualTo("tutorial.last=main0\n"), "들어간 장이 임시 폴더의 기록에");

                // 0장의 단계를 말풍선 [다음]으로 끝까지 — 장 경계를 넘으면 그 장은 완료, 새 장이 last
                yield return TutorialLayerFlowTests.NextTo(flow, "S101");
                Assert.That(tut.Record.Seen("main0"), Is.True, "끝까지 본 장은 완료");
                Assert.That(tut.Record.Last, Is.EqualTo("main1"));

                // ── 끝내기 창(말풍선이 부르는 것과 같은 함수): Esc = [계속하기] ──
                Assert.That(tut.OpenQuit(), Is.True);
                Assert.That(tut.DialogUp && tut.Quitting, Is.True);
                Assert.That(stage.Root.Q("tut-quit"), Is.Not.Null);
                Assert.That(flow.MenuUp && !flow.InputOpen, Is.True, "창이 떠 있는 동안 대기열·입력이 선다");
                Assert.That(flow.Escape(), Is.True);
                Assert.That(tut.DialogUp, Is.False);
                Assert.That(stage.Root.Q("tut-quit"), Is.Null);
                Assert.That(tut.Running, Is.True, "[계속하기]는 판을 버리지 않는다");
                // [목차로]: 판을 버리고 목차 — 0장 완료, 1장 이어서
                Assert.That(tut.OpenQuit(), Is.True);
                tut.ToMenu();
                yield return null;
                var menu = (TutorialMenuScreen)stage.Current;
                Assert.That(tut.Running, Is.False);
                Assert.That(flow.Session, Is.Null);
                Assert.That(menu.Cells.Count, Is.EqualTo(tut.Chapters.Count), "본편 장 줄 + 확장 장 칸");
                Assert.That(menu.Cells["main0"].Q(className: "kit-badge-ok"), Is.Not.Null, "완료");
                Assert.That(menu.Cells["main1"].Q(className: "kit-badge-gold"), Is.Not.Null, "이어서");
                Assert.That(menu.Cells["e01"].ClassListContains("sn-off"), Is.False, "각본이 있는 확장 장은 고를 수 있다");
                foreach (var ch in tut.Chapters)
                    if (!ch.Ready)
                    {
                        Assert.That(menu.Cells[ch.Id].ClassListContains("sn-off"), Is.True, ch.Id + ": 각본이 없는 장은 준비 중");
                        Assert.That(menu.Pick(ch.Id), Is.False, ch.Id);
                    }
                Assert.That(stage.Root.Q("tut-menu-face"), Is.Not.Null, "마박사 작은 얼굴");
                menu.Back();
                yield return null;

                // ── 기록이 있는 타이틀 → 목차 → 3장(앞 장은 연출 없이 보낸 판) ──
                title = (TitleScreen)stage.Current;
                Assert.That(title.Shown[title.Picked].Id, Is.EqualTo(TitleScreen.Tutorial), "[뒤로]는 「튜토리얼」 줄로");
                Assert.That(title.FirstTimeShown, Is.True, "본편 마지막 장을 보기 전에는 딱지가 그대로");
                Assert.That(title.Activate(Row(title, TitleScreen.Tutorial)), Is.True);
                yield return null;
                menu = (TutorialMenuScreen)stage.Current;
                Assert.That(menu.Pick("main3"), Is.True);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(tut.Chapter.Id, Is.EqualTo("main3"));
                Assert.That(flow.OnBoard, Is.True, "3장 시작 = 셋업이 끝난 보드");
                Assert.That(tut.Runner.Step.Id, Is.EqualTo("S301"));
                Assert.That(tut.Record.Last, Is.EqualTo("main3"));
                Assert.That(tut.Record.Seen("main1"), Is.False, "빨리감기로 지나간 장은 완료가 아니다");

                // ── 게임 메뉴: 저장 줄 없음(결정 96 Q8 — 「튜토리얼 판은 저장되지 않습니다.」 문구를 뺐다) + [튜토리얼 끝내기] = 같은 끝내기 창 → [타이틀로] ──
                for (int i = 0; i < 300 && !flow.InputOpen; i++) yield return null;
                flow.OpenMenu();
                var game = (BoardMenuScreen)stage.Current;
                Assert.That(game.SaveText, Is.Null, "튜토리얼 판의 게임 메뉴에는 저장 줄이 없다(결정 96 Q8)");
                Assert.That(stage.Root.Q("board-menu-save"), Is.Null);
                Assert.That(stage.Root.Q<Button>("board-menu-quit").text, Is.EqualTo("튜토리얼 끝내기"));
                game.AskQuit();
                Assert.That(tut.Quitting, Is.True, "게임 메뉴의 나가기도 끝내기 창");
                Assert.That(stage.Current, Is.Null, "게임 메뉴는 닫고 그 자리에");
                tut.ToTitle();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
                Assert.That(tut.Running, Is.False);

                // ── 마지막 장(7장 — 각본 셋)에서 [이 장 건너뛰기] → 마지막 각본 끝 → 본편 끝 창([목차로] · [타이틀로]) — 건너뛴 장은 완료가 아니다 ──
                title = (TitleScreen)stage.Current;
                title.Activate(Row(title, TitleScreen.Tutorial));
                yield return null;
                Assert.That(((TutorialMenuScreen)stage.Current).Pick("main7"), Is.True);
                yield return null;
                Assert.That(tut.Runner.Step.Id, Is.EqualTo("S701"), "7장은 main의 7장 단계부터");
                Assert.That(tut.SkipChapter(), Is.True);
                yield return null;
                Assert.That(tut.Ending && tut.Runner.Finished, Is.True);
                Assert.That(tut.Script.Id, Is.EqualTo("preview3"), "각본 여럿인 장의 건너뛰기 = 마지막 각본의 끝(07 T64)");
                Assert.That(tut.Layer, Is.Null, "끝 창에는 층이 없다");
                Assert.That(stage.Root.Q("tut-end-menu"), Is.Not.Null);
                Assert.That(stage.Root.Q("tut-end-title"), Is.Not.Null, "본편 끝은 [목차로] · [타이틀로]");
                Assert.That(flow.Escape(), Is.False, "끝 창은 Esc로 닫지 않는다");
                Assert.That(tut.Record.Seen("main7"), Is.False, "건너뛴 장은 완료가 아니다(결정 90 Q7 ①)");
                tut.ToMenu();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<TutorialMenuScreen>());
                Assert.That(File.ReadAllText(Path.Combine(root, TutorialRecord.FileName)), Is.EqualTo("tutorial.last=main7\ntutorial.seen.main0=1\n"));
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        // 보드 씬을 열고 1080 RenderTexture에 그린다(기록은 실행하는 동안만 — 사용자 기록에 쓰지 않는다)
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
            if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();
            if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
            foreach (var rt in rts)
            {
                rt.Release();
                Object.Destroy(rt);
            }
        }

        /// <summary>
        /// 층 ↔ 목차 잇기(07 T60): 타이틀 [튜토리얼](기록 없음) → 본편 0장에 층이 뜨고 [다음]으로 넘긴다 → 말풍선 [이 장 건너뛰기 ▶▶] = 목차의 건너뛰기(다음 장 시작 국면에 새 층 — 건너뛴 장은 완료가 아니다)
        /// → 말풍선 [튜토리얼 끝내기] = 끝내기 창(층이 창 단추를 거르지 않는다 · Esc = [계속하기]) → 1장에서 다시 건너뛰기 → 2장(셋업이 끝난 보드). 에러 로그 0.
        /// </summary>
        [UnityTest]
        public IEnumerator M65_층과목차_타이틀에서0장_다음_건너뛰기는다음장_끝내기는끝내기창_에러로그0()
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
                flow.OpenTitle();
                var title = (TitleScreen)stage.Current;
                Assert.That(title.Activate(Row(title, TitleScreen.Tutorial)), Is.True);
                yield return null;
                yield return TutorialLayerFlowTests.Shown(flow, "S001");
                var first = tut.Layer;
                Assert.That(first.Element.parent, Is.SameAs(stage.Root), "타이틀이 연 판에 층이 붙는다");
                Assert.That(flow.ClickGate, Is.Not.Null);
                Assert.That(first.Next(), Is.True, "[다음]");
                yield return TutorialLayerFlowTests.Shown(flow, "S002");

                // [이 장 건너뛰기 ▶▶] → TutorialMenu.SkipChapter: 1장 시작 국면(셋업 마을 고르기)에 새 층
                Assert.That(TutorialLayerFlowTests.Clicked(first.Element.Q("tut-layer-skip")), Is.True, "말풍선 단추는 늘 받는다");
                first.Skip();
                yield return null;
                Assert.That(tut.Chapter.Id, Is.EqualTo("main1"));
                Assert.That(tut.Runner.Step.Id, Is.EqualTo("S101"));
                Assert.That(tut.Layer, Is.Not.SameAs(first), "새 판에 새 층");
                Assert.That(first.Element.parent, Is.Null, "앞 층은 걷었다");
                yield return TutorialLayerFlowTests.Shown(flow, "S101");
                yield return TutorialLayerFlowTests.Until(flow, () => tut.Layer.Holes.Count == 1, "S101 구멍(운하시티 줄 — 한 번에 한 부분, 결정 99 ①)");
                Assert.That(stage.Current, Is.InstanceOf<SetupVillageScreen>());
                Assert.That(tut.Record.Seen("main0"), Is.False, "건너뛴 장은 완료가 아니다");
                Assert.That(tut.Record.Last, Is.EqualTo("main1"));

                // [튜토리얼 끝내기] → TutorialMenu.OpenQuit: 끝내기 창(층 위) — 창 단추는 층이 거르지 않는다, Esc = [계속하기]
                tut.Layer.Quit();
                Assert.That(tut.Quitting, Is.True, "말풍선 [튜토리얼 끝내기] = 끝내기 창");
                Assert.That(stage.Root.Q("tut-quit"), Is.Not.Null);
                Assert.That(stage.Root.IndexOf(stage.Root.Q("tut-quit")), Is.GreaterThan(stage.Root.IndexOf(tut.Layer.Element)), "창은 층 위");
                Assert.That(TutorialLayerFlowTests.Clicked(stage.Root.Q("tut-quit-continue")), Is.True, "끝내기 창 단추를 층이 삼키지 않는다");
                Assert.That(flow.Escape(), Is.True);
                Assert.That(tut.DialogUp, Is.False);
                yield return TutorialLayerFlowTests.Until(flow, () => tut.Layer.Shown, "창을 닫으면 단계가 그대로");
                Assert.That(tut.Runner.Step.Id, Is.EqualTo("S101"));

                // 1장 건너뛰기 → 2장(셋업이 끝난 보드 — 다음 장 시작 국면)
                tut.Layer.Skip();
                yield return null;
                yield return TutorialLayerFlowTests.Shown(flow, "S201");
                Assert.That(tut.Chapter.Id, Is.EqualTo("main2"));
                Assert.That(flow.OnBoard, Is.True);
                Assert.That(tut.Runner.Failures, Is.Empty, string.Join("\n", tut.Runner.Failures));
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                Release(rts);
            }
        }

        /// <summary>
        /// 7장 막 맛보기 = 각본 셋(07 T54·T56·T62): 목차의 7장 → main의 7장 단계(6장 끝 판) → 각본이 끝나면 preview2를 그 start로 새로 연다(판 차리기 demo 둘은 보이지 않고
        /// 막 앞 판 손보기 뒤 옮긴 소개 말풍선 S711부터 — 07 T160) → 2막 → 3막은 대사로만(결정 96 Q9 나 — 대신 두는 배틀 없음) → preview3(종막 선택 창) → 끝 창. 모두 화면에서 끝까지 본 장이라 완료. 대조 어긋남 · 에러 로그 0.
        /// </summary>
        [UnityTest]
        public IEnumerator M65_7장_각본셋을차례로끝까지_판차리기는보이지않고_끝창_에러로그0()
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
                Assert.That(tut.Start("main7"), Is.True);
                yield return TutorialLayerFlowTests.Shown(flow, "S701");
                var opened = new List<string>();
                var failures = new List<string>();
                Sinnoh.Rules.Tutorial.TutorialRunner last = null;
                for (int guard = 0; guard < 4000 && !tut.Ending; guard++)
                {
                    if (tut.Runner != last)
                    {
                        if (last != null) failures.AddRange(last.Failures);
                        last = tut.Runner;
                        opened.Add(tut.Script.Id + ":" + (last.Step != null ? last.Step.Id : "-"));
                        Assert.That(tut.Chapter.Id, Is.EqualTo("main7"), tut.Script.Id + "도 7장");
                    }
                    Assert.That(flow.BattleOver, Is.False, "막 맛보기에는 배틀이 없다(결정 96 Q9 나)");
                    var t = tut.Layer;
                    if (t != null && t.Shown)
                    {
                        var adv = tut.Runner.Step.Advance;
                        string id = tut.Runner.Step.Id;
                        Assert.That(adv, Is.EqualTo(Sinnoh.Rules.Tutorial.StepAdvance.Next), id + ": 막 맛보기는 보기만 — 뜨는 단계는 모두 [다음](결정 96 Q9 나)");
                        Assert.That(t.Next(), Is.True, id);
                    }
                    flow.Skip(250);
                    yield return null;
                }
                if (last != null) failures.AddRange(last.Failures);
                Assert.That(tut.Ending, Is.True, "끝 창이 열린다(마지막: " + tut.Script?.Id + " " + tut.Runner?.Step?.Id + ")");
                CollectionAssert.AreEqual(new[] { "main:S701", "preview2:S711", "preview3:S731" }, opened, "각본 셋을 차례로 · 막 앞 판 각본은 판 차리기 뒤 단계부터");
                Assert.That(stage.Root.Q("tut-end-title"), Is.Not.Null, "본편 끝 창");
                Assert.That(tut.Record.Seen("main7"), Is.True, "각본 셋을 화면에서 끝까지 본 장은 완료");
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
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
