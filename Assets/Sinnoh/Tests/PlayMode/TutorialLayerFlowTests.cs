using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Rules.Tutorial;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M6.5 화면 ① 튜토리얼 층(07 §9-1): 목차가 연 본편 판(<see cref="TutorialMenu.Start"/> — 층이 붙는 한 길, 07 T60)을 보드 씬의 실제 흐름에 띄운다.
    /// 단계는 연출 대기열이 빈 뒤에 뜨고(내 입력 단계는 입력이 열린 뒤), 앵커(요소 이름 · 보드 칸)에 구멍을 내고, 구멍 밖 클릭은 패널 맨 위에서 삼켜 hint를 띄우며,
    /// 거르개가 막은 명령도 hint를 띄우고, 지정 행동을 받아 연출이 끝나면 다음 단계로 간다. 앵커를 2초 못 찾으면 가운데 모드 + 경고. 저장 폴더를 쓰지 않는다(튜토리얼 판은 저장하지 않는다).
    /// </summary>
    public sealed class TutorialLayerFlowTests
    {
        internal static IEnumerator Open()
        {
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                var boot = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
        }

        static TutorialScript Main(GameFlow flow) =>
            Sinnoh.Data.TutorialLoader.Load(File.ReadAllText(Path.Combine(Application.dataPath, "Sinnoh/Data/Json/tutorial/main.json")), flow.Ctx, "tutorial/main.json");

        // 조건이 설 때까지 세션·연출 시각을 보내며 프레임을 돈다(배치가 서야 앵커를 찾는다)
        internal static IEnumerator Until(GameFlow flow, Func<bool> done, string what, int max = 800, int ms = 100)
        {
            for (int i = 0; i < max && !done(); i++)
            {
                flow.Skip(ms);
                yield return null;
            }
            Assert.That(done(), Is.True, what);
        }

        // 지금 층(장이 바뀌면 새 층이다)이 그 단계를 띄울 때까지
        internal static IEnumerator Shown(GameFlow flow, string stepId) =>
            Until(flow, () => flow.Tutorial.Layer != null && flow.Tutorial.Layer.Shown && flow.Tutorial.Runner.Step.Id == stepId, stepId + "이 뜬다");

        // [다음] 단계를 [다음]으로 넘겨 그 단계까지
        internal static IEnumerator NextTo(GameFlow flow, string stepId)
        {
            for (int guard = 0; flow.Tutorial.Runner.Step.Id != stepId; guard++)
            {
                Assert.That(guard, Is.LessThan(40), stepId + "까지 [다음]");
                yield return Until(flow, () => flow.Tutorial.Layer.Shown, flow.Tutorial.Runner.Step.Id + "이 뜬다");
                Assert.That(flow.Tutorial.Runner.Step.Advance, Is.EqualTo(StepAdvance.Next), flow.Tutorial.Runner.Step.Id + "은 [다음] 단계");
                Assert.That(flow.Tutorial.Layer.Next(), Is.True, "[다음]");
            }
            yield return Shown(flow, stepId);
        }

        // 그 요소에 클릭을 보내고 그 요소가 받았나
        internal static bool Clicked(VisualElement target)
        {
            int got = 0;
            EventCallback<ClickEvent> seen = _ => got++;
            target.RegisterCallback(seen);
            using (var e = ClickEvent.GetPooled())
            {
                e.target = target;
                target.SendEvent(e);
            }
            target.UnregisterCallback(seen);
            return got > 0;
        }

        static void Grab(List<string> errors, LogType type, string msg)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
        }

        [UnityTest]
        public IEnumerator M65_튜토리얼층_대기열뒤에단계를띄우고_구멍밖클릭은삼켜hint_거절도hint_지정행동뒤다음단계()
        {
            var errors = new List<string>();
            Application.LogCallback grab = (msg, stack, type) => Grab(errors, type, msg);
            Application.logMessageReceived += grab;
            RenderTexture rt = null;
            GameFlow flow = null;
            try
            {
                yield return Open();
                var boot = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
                Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
                Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                flow = boot.Flow;
                var stage = boot.Root.Stage;
                var tut = flow.Tutorial;
                Assert.That(tut.Start("main0"), Is.True);
                var t = tut.Layer;
                Assert.That(t, Is.Not.Null, "목차가 연 판에 층이 붙는다");
                Assert.That(flow.SaveId, Is.Null, "튜토리얼 판은 저장하지 않는다");

                // ── S001 [다음](앵커 없음): 가운데 · 덮개 · 구멍 밖(어디든) 클릭은 삼킨다 ──
                yield return Shown(flow, "S001");
                Assert.That(t.ShownText, Is.EqualTo(stage.T("tutorial.main.S001")));
                Assert.That(t.DockAt, Is.EqualTo("center"), "앵커 없는 [다음] 단계는 가운데");
                Assert.That(t.FullBody, Is.True, "장 첫 말풍선 = 마박사 전신(결정 96 Q2 다)");
                Assert.That(t.Element.parent, Is.SameAs(stage.Root), "문서 루트에 붙는다");
                Assert.That(stage.Root.IndexOf(t.Element), Is.EqualTo(stage.Root.IndexOf(stage.Tips.Element) - 1), "툴팁 바로 아래 — 화면·연출 위");
                Assert.That(stage.Current, Is.InstanceOf<SetupVillageScreen>(), "아래에 셋업 마을 화면");
                int hints = t.Hints;
                Assert.That(Clicked(stage.Root.Q("setup-village-list")), Is.False, "[다음] 단계는 화면 클릭을 삼킨다");
                Assert.That(t.Hints, Is.GreaterThan(hints), "삼킨 클릭은 hint");
                Assert.That(Clicked(t.Element.Q("tut-layer-text")), Is.True, "말풍선은 늘 받는다");

                // ── S101 지정 행동(앵커 setup-village-item-canalave+setup-village-confirm — 07 T130): 구멍 둘 · 거절 hint · 받은 뒤 대기열이 빌 때까지 다음 단계를 띄우지 않는다 ──
                yield return NextTo(flow, "S101");
                Assert.That(tut.Layer, Is.SameAs(t), "같은 각본 안에서는 층이 그대로");
                // 한 번에 한 부분만(결정 99 ① · 07 T180): 처음에는 운하시티 줄만 비추고, 누르면 [확정]으로 옮긴다
                yield return Until(flow, () => t.Holes.Count == 1, "S101 · 구멍 하나(운하시티 줄)");
                var row = stage.Root.Q("setup-village-item-canalave");
                var confirm = stage.Root.Q("setup-village-confirm");
                Assert.That(t.Holes[0], Is.EqualTo(row.worldBound), "첫 구멍 = 첫 앵커 부분(운하시티 줄) 사각형");
                Assert.That(t.PartName, Is.EqualTo("setup-village-item-canalave"));
                Assert.That(Clicked(confirm), Is.False, "아직 차례가 아닌 부분([확정])은 삼킨다");
                Assert.That(Clicked(row), Is.True, "구멍 안 클릭은 받는다");
                yield return null;
                Assert.That(t.Holes.Count, Is.EqualTo(1), "누른 뒤에도 구멍은 하나");
                Assert.That(t.Holes[0], Is.EqualTo(confirm.worldBound), "누르면 다음 부분([확정])으로 옮긴다");
                Assert.That(t.Part, Is.EqualTo(1));
                Assert.That(Clicked(stage.Root.Q("setup-village-detail")), Is.False, "구멍 밖 클릭은 삼킨다");
                var village = (SetupVillageScreen)stage.Current;
                int canalave = flow.Ctx.Fixtures.Ids.IndexOf("village.canalave");
                int wrong = -1, right = -1;
                for (int i = 0; i < village.Villages.Count; i++)
                {
                    if (village.Villages[i] == canalave) right = i;
                    else if (wrong < 0) wrong = i;
                }
                Assert.That(right, Is.GreaterThanOrEqualTo(0), "운하시티가 고를 수 있다");
                int blocked = t.Runner.Blocked;
                village.Pick(wrong);
                village.Confirm();
                yield return null;
                flow.Skip(16);
                Assert.That(t.Runner.Blocked, Is.EqualTo(blocked + 1), "allow 밖 마을은 거르개가 막는다");
                Assert.That(t.Hinted, Is.True);
                Assert.That(t.ShownText, Is.EqualTo(stage.T("tutorial.main.S101.hint")), "거절 hint 글");
                yield return Until(flow, () => t.Part == 0, "거절되면 첫 부분부터 다시 비춘다(07 T180)");
                village = stage.Current as SetupVillageScreen;
                Assert.That(village, Is.Not.Null, "막힌 뒤에도 마을 화면");
                village.Pick(right);
                Assert.That(village.Confirm(), Is.True, "지정 행동");
                yield return null;
                Assert.That(t.Runner.Taken, Is.EqualTo(1), "받은 지정 행동");
                Assert.That(flow.Fx.Sequencer.InputOpen, Is.False, "고향 마을 연출이 남았다");
                Assert.That(t.Runner.Step.Id, Is.EqualTo("S101"), "연출 대기열이 빌 때까지 다음 단계로 가지 않는다");
                yield return Shown(flow, "S102");
                Assert.That(flow.Fx.Sequencer.InputOpen, Is.True, "다음 단계는 입력이 열린 뒤에 뜬다");

                // ── S104 스타팅·카드 → S301([다음] · board-hud-moves) → S302(cell:32 — 보드 칸 구멍 · 구멍 밖 칸 누르기 삼킴) ──
                yield return NextTo(flow, "S104");
                var pick = (SetupPickScreen)stage.Current;
                int hiker = flow.Ctx.Cards.TrainerCardIds.IndexOf("hiker"), piplup = flow.Ctx.Cards.PokemonIds.IndexOf("393:0");
                for (int i = 0; i < pick.Starters.Count; i++) if (pick.Starters[i] == piplup) pick.PickStarter(i);
                Assert.That(pick.Starter, Is.GreaterThanOrEqualTo(0), "팽도리를 고를 수 있다");
                for (int i = 0; i < pick.Cards.Count; i++) if (pick.Cards[i] == hiker) pick.PickCard(i);
                Assert.That(pick.Confirm(), Is.True);
                yield return Shown(flow, "S105");
                yield return NextTo(flow, "S301");
                yield return Until(flow, () => t.Holes.Count == 1, "S301 · board-hud-moves 구멍");
                Assert.That(t.Next(), Is.True);
                yield return Shown(flow, "S302");
                yield return Until(flow, () => t.Holes.Count == 1, "S302 · 칸 32 구멍");
                Assert.That(flow.CellRect(32, out var cell), Is.True);
                Assert.That(Vector2.Distance(t.Holes[0].center, cell.center), Is.LessThan(2f), "구멍 = 칸 32를 비춘 사각형");
                hints = t.Hints;
                Assert.That(t.PassAt(cell.center), Is.True, "구멍 안 칸 누르기는 받는다");
                Assert.That(t.PassAt(new Vector2(cell.center.x + cell.width * 4f, cell.center.y)), Is.False, "구멍 밖 칸 누르기는 삼킨다(GameFlow.ClickGate)");
                Assert.That(t.Hints, Is.EqualTo(hints + 1));
                Assert.That(flow.ClickGate, Is.Not.Null, "보드 칸 누르기를 층이 거른다");
                Assert.That(flow.Hud.Walk(32), Is.True, "지정 칸으로 걷기");
                yield return Shown(flow, "S303");
                Assert.That(t.Runner.Failures, Is.Empty, string.Join("\n", t.Runner.Failures));

                // ── 판을 떠나면(끝내기 창의 [타이틀로]) 층을 걷고 거르개도 뗀다 ──
                Assert.That(tut.OpenQuit(), Is.True);
                tut.ToTitle();
                yield return null;
                Assert.That(t.Element.parent, Is.Null, "층을 걷는다");
                Assert.That(flow.ClickGate, Is.Null, "거르개도 뗀다");
                Assert.That(tut.Layer, Is.Null);
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= grab;
                flow?.OpenTitle();
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            }
        }

        // 결정 107: 본편 5장 — S505 정비 판 끌기(포인터 누르기·움직임·떼기가 층을 지난다) · S506 창이 열린 채 [다음] 없음 · 앵커를 가리는 창은 [닫기]부터(안전장치)
        [UnityTest]
        public IEnumerator M65_튜토리얼층_정비끌기는층을지나고_창이열린채다음없음_가리는창은닫기부터()
        {
            var path = new TutorialClickPath();
            var errors = new List<string>();
            Application.LogCallback grab = (msg, stack, type) => Grab(errors, type, msg);
            Application.logMessageReceived += grab;
            try
            {
                yield return path.Boot();
                var flow = path.Flow;
                var tut = flow.Tutorial;
                Assert.That(tut.Start("main5"), Is.True, "5장 고르기");
                yield return Shown(flow, "S501");
                var t = tut.Layer;
                yield return path.Cell(t, 33, "S501 고르기");
                yield return path.Cell(t, 33, "S501 걷기");   // 같은 칸을 다시 누르면 걷는다(결정 70 ⑱)
                yield return Shown(flow, "S502");
                yield return path.Press("board-hud-act-village", "S502");
                yield return Shown(flow, "S503");
                yield return path.Press("board-village-heal", "S503");
                yield return Shown(flow, "S504");
                yield return path.Press("board-village-close", "S504");
                yield return path.Press("board-hud-maint", "S504");

                // ── S505: 비버니(파티 칸 1)를 끌어 맨 앞 칸에 놓는다 ──
                yield return Shown(flow, "S505");
                int seat = flow.Client.Seat;
                int piplup = flow.Client.View.Seats[seat].Party[0], bidoof = flow.Client.View.Seats[seat].Party[1];
                Assert.That(t.Guarding, Is.False, "앵커(정비 판)가 그 창 안이면 [닫기]로 돌리지 않는다");
                yield return path.Drag("board-party-mon-1", "board-party-col-0", "S505");
                yield return Shown(flow, "S506");
                Assert.That(flow.Client.View.Seats[seat].Party[0], Is.EqualTo(bidoof), "끌어 놓은 비버니가 맨 앞");
                Assert.That(flow.Client.View.Seats[seat].Party[1], Is.EqualTo(piplup));
                Assert.That(flow.Top, Is.InstanceOf<PartyScreen>(), "정비 판은 그대로 열려 있다");

                // ── S506([다음] · board-party-close+cell:33): 창이 열린 채로는 [다음]이 없고 눌러도 넘기지 않는다 ──
                yield return Until(flow, () => t.PartName == "board-party-close", "S506 · 정비 창 [닫기]를 비춘다");
                Assert.That(t.NextShown, Is.False, "창이 열린 채로는 [다음]이 없다(결정 107 ②)");
                Assert.That(t.Next(), Is.False, "창이 열린 채 [다음]은 단계를 넘기지 않는다");
                Assert.That(t.Runner.Step.Id, Is.EqualTo("S506"));
                yield return path.Press("board-party-close", "S506 창 닫기");
                yield return Until(flow, () => t.PartName == "cell:33" && t.NextShown, "S506 · 창을 닫으면 칸 33 + [다음]");

                // ── 안전장치: [다음] 단계에서 창을 다시 열면(단축키 T) 그 창의 [닫기]부터 · [다음] 없음 ──
                Assert.That(flow.OpenParty(), Is.True, "정비 판을 다시 연다");
                yield return Until(flow, () => t.Guarding, "S506 · 칸 33을 가리는 창 → [닫기]");
                Assert.That(t.PartName, Is.EqualTo("board-party-close"));
                Assert.That(t.Holes.Count, Is.EqualTo(1));
                Assert.That(t.Holes[0], Is.EqualTo(flow.Stage.Root.Q("board-party-close").worldBound), "구멍 = [닫기]");
                Assert.That(t.NextShown, Is.False);
                Assert.That(t.Next(), Is.False);
                yield return path.Press("board-party-close", "S506 다시 연 창 닫기");
                yield return Until(flow, () => !t.Guarding && t.NextShown, "닫으면 앵커로 돌아가고 [다음]이 선다");
                Assert.That(t.Next(), Is.True, "S506 [다음]");

                // ── S601(지정 행동 · board-hud-act-challenge): 단계 중에 연 창이 [도전]을 가리면 [닫기]부터 → 닫으면 [도전] ──
                yield return Shown(flow, "S601");
                t = tut.Layer;
                yield return Until(flow, () => t.PartName == "board-hud-act-challenge", "S601 · [도전]");
                Assert.That(flow.OpenParty(), Is.True);
                yield return Until(flow, () => t.Guarding && t.PartName == "board-party-close", "S601 · [도전]을 가리는 정비 판 → [닫기]");
                int hints = t.Hints;
                Assert.That(Clicked(flow.Stage.Root.Q("board-party-title")), Is.False, "[닫기] 밖 클릭은 삼킨다");
                Assert.That(t.Hints, Is.EqualTo(hints + 1));
                yield return path.Press("board-party-close", "S601 창 닫기");
                yield return Until(flow, () => !t.Guarding && t.PartName == "board-hud-act-challenge", "닫으면 [도전]으로 돌아간다");
                Assert.That(t.Runner.Step.Id, Is.EqualTo("S601"));
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= grab;
                path.Release();
            }
        }

        [UnityTest]
        public IEnumerator M65_튜토리얼층_앵커를2초못찾으면가운데모드와경고_입력단계는구멍이없으면막지않는다()
        {
            RenderTexture rt = null;
            GameFlow flow = null;
            try
            {
                yield return Open();
                var boot = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
                Assert.That(boot?.Root, Is.Not.Null);
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                flow = boot.Flow;
                var stage = boot.Root.Stage;
                var script = Main(flow);
                script.Steps.Find(x => x.Id == "S101").Anchor = "no-such-anchor";
                Assert.That(flow.Tutorial.Play(script), Is.True, "확인용 각본도 같은 길로 연다");
                yield return NextTo(flow, "S101");
                var t = flow.Tutorial.Layer;
                Assert.That(t.Center, Is.False, "2초 전에는 아직 찾는 중");
                Assert.That(t.Holes, Is.Empty);
                LogAssert.Expect(LogType.Warning, new Regex("no-such-anchor.*가운데 모드"));
                flow.Skip(TutorialLayer.LostMs + 100);
                yield return null;
                Assert.That(t.Center, Is.True, "2초 못 찾으면 가운데 모드");
                Assert.That(Clicked(stage.Root.Q("setup-village-list")), Is.True, "구멍 없는 입력 단계는 화면 클릭을 막지 않는다(엔진 거르개만)");
                Assert.That(t.Element.Q("tut-layer-next").style.display.value, Is.EqualTo(DisplayStyle.None), "지정 행동 단계에는 [다음]이 없다");
            }
            finally
            {
                flow?.OpenTitle();
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            }
        }
    }
}
