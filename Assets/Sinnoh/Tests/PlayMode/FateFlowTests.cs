using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 사람 혼자 하는 판이 운명의 변화를 받고·버리고·낸다(M5 「운명·응답 화면」 — 04 S30, §8-8 7·14·42·43·45행): 타이틀 → 혼자 하기 → 셋업(화면) →
    /// 운명 길 <see cref="SoloTrail.Fate"/>(셋업 뒤 운명 카드 6장 · 떨어진 별 4개로 손본 판) → 저절로 열린 손패 창에서 6장째 버리기 → [운명의 변화] → 펄기아 [1장 사용] → 보드에서 칸 →
    /// 순간이동 → 약한 나무 배틀 → 굴린 뒤 창(배틀 독의 운명 줄)에서 디아루가 3장 → 재굴림 → 다음 턴 시작의 떨어진 별 1장.
    /// 답은 길잡이가 고르고 <see cref="FlowPilot"/>이 화면 요소로 누른다(dotnet <c>SoloPilotTests.M5_운명길_…</c>가 같은 판을 화면 없이 돈다). 받을 화면이 없는 선택은 0번.
    /// </summary>
    public sealed class FateFlowTests
    {
        [UnityTest]
        public IEnumerator M5_사람혼자하는판이운명카드를버리고펄기아와디아루가3장을화면으로낸다()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
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
                var flow = boot.Flow;
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;

                var title = (TitleScreen)stage.Current;
                int soloIndex = -1;
                for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Solo) soloIndex = i;
                Assert.That(title.Activate(soloIndex), Is.True);
                yield return null;
                var events = new List<GameEvent>();
                flow.EventsSeen += events.AddRange;
                var pilot = FlowPilot.Start(flow, SoloTrail.Fate);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");
                Assert.That(flow.Client.View.Seats[0].FateHand.Count, Is.EqualTo(6), "운명 길: 6장으로 시작");
                string notReady = stage.T("screen.board.guide.not_ready");

                bool discardWindow = false, passSeen = false, fateDock = false, rerollSeen = false, rerollBoard = false, starFly = false;
                float t0 = Time.realtimeSinceStartup;
                for (int guard = 0; guard < 40000 && Time.realtimeSinceStartup - t0 < 240f; guard++)
                {
                    flow.Skip(150);
                    yield return null;
                    var guide = stage.Root.Q<Label>("board-hud-guide");
                    if (guide != null) Assert.That(guide.text, Is.Not.EqualTo(notReady), "HUD에 준비 중 안내가 떴다");
                    if (flow.Top is FateScreen f && f.Discarding) discardWindow = true;
                    if ((flow.Hud != null && flow.Hud.FateFx.PassShown) || (stage.Current is BattleScreen b0 && b0.FateFx.PassShown)) passSeen = true;
                    if (stage.Current is BattleScreen b && b.AskingFate) fateDock = true;
                    if (stage.Current is BattleScreen b2 && b2.RerollShown) rerollSeen = true;
                    if (stage.Current is BattleScreen b3 && b3.RerollBoardShown && stage.Root.Q("battle-reroll-board") != null && stage.Root.Q("battle-reroll-die-1") != null) rerollBoard = true;
                    if (flow.Hud != null && flow.Hud.FateFx.FlyShown > 0 && events.Exists(e => e is FateDealt d && d.Source.Kind == SourceKind.Rule && d.Source.Id == (int)RuleSource.FallenStar)) starFly = true;
                    if (starFly && flow.InputOpen) break;
                    FlowPilot.Step(flow, pilot);
                }

                Assert.That(discardWindow, Is.True, "6장째 버리기 창(S30)이 저절로 열렸다");
                Assert.That(events.Exists(e => e is FateDiscarded), Is.True, "버렸다(42행)");
                Assert.That(events.Exists(e => e is Teleported), Is.True, "펄기아 순간이동(14행)");
                Assert.That(passSeen, Is.True, "카드 지나가기(45행)를 봤다");
                Assert.That(fateDock, Is.True, "배틀 독의 운명 줄(굴린 뒤 창)");
                Assert.That(events.Exists(e => e is FatePlayed p && p.Card == FateCard.Dialga && p.Play3), Is.True, "디아루가 3장");
                Assert.That(rerollSeen, Is.True, "재굴림 주사위 줄(43행)을 봤다");
                Assert.That(rerollBoard, Is.True, "재굴림은 굴림판의 멈춘 주사위 위에서 입체로 글리치한다(M9 다듬기 P38) — 주사위 줄은 그 머리");
                Assert.That(starFly, Is.True, "떨어진 별 카드가 날아왔다(7행)");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택이 없었다");
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (rt != null) rt.Release();
            }
        }

        /// <summary>
        /// 결정 80 N3 나: Esc는 맨 위 창의 [닫기]와 같은 길로 닫는다(스탬프 판 · 손패 창 · 기록 창 — 버리는 중인 손패 창처럼 답해야 하는 창은 닫지 않는다),
        /// 배틀 중에는 기둥의 [운명의 변화 n/5]로 손패를 보기만 한다(답은 나가지 않는다 · Esc로 닫힌다 · 배틀 화면은 Esc를 받지 않는다).
        /// </summary>
        [UnityTest]
        public IEnumerator M5_Esc로창을닫고_배틀중에는운명손패를보기만한다()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }
            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
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
                var flow = boot.Flow;
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;

                Assert.That(flow.Escape(), Is.False, "타이틀에서는 Esc가 아무것도 하지 않는다");
                var title = (TitleScreen)stage.Current;
                int soloIndex = -1;
                for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Solo) soloIndex = i;
                Assert.That(title.Activate(soloIndex), Is.True);
                yield return null;
                var pilot = FlowPilot.Start(flow, SoloTrail.Fate);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");

                bool discardKept = false, boardEsc = false, battleView = false;
                float t0 = Time.realtimeSinceStartup;
                for (int guard = 0; guard < 40000 && Time.realtimeSinceStartup - t0 < 240f && !battleView; guard++)
                {
                    flow.Skip(150);
                    yield return null;
                    // 6장째 버리기: 답해야 하는 창 — Esc로 닫히지 않는다
                    if (!discardKept && flow.Top is FateScreen f && f.Discarding)
                    {
                        Assert.That(flow.Escape(), Is.False, "버리는 중인 손패 창은 Esc로 닫히지 않는다");
                        Assert.That(flow.Top, Is.SameAs(f));
                        discardKept = true;
                    }
                    // 보드(내 턴 · 위에 창 없음): 스탬프 판 · 손패 창 · 기록 창을 열고 Esc로 닫는다 — 답은 나가지 않는다
                    if (discardKept && !boardEsc && stage.Current == null && flow.InputOpen && flow.CanOpenFate && flow.Client.Choice(ChoiceKind.Discard) == null)
                    {
                        int commands = flow.Session.CommandCount;
                        Assert.That(flow.Escape(), Is.False, "창도 고른 칸도 없으면 아무 일도 없다");
                        Assert.That(flow.OpenStamp(), Is.True);
                        Assert.That(stage.Current, Is.InstanceOf<StampScreen>());
                        Assert.That(flow.Escape(), Is.True, "Esc = 스탬프 판 [닫기]");
                        Assert.That(stage.Current, Is.Null);
                        Assert.That(flow.OpenFate(), Is.True);
                        Assert.That(stage.Root.Q("board-fate-use-1"), Is.Not.Null, "보드의 손패 창에는 [1장 사용]이 있다");
                        Assert.That(flow.Escape(), Is.True, "Esc = 손패 창 [닫기]");
                        Assert.That(stage.Current, Is.Null);
                        flow.OpenLog();
                        Assert.That(stage.Current, Is.InstanceOf<LogScreen>());
                        Assert.That(flow.Escape(), Is.True, "Esc = 기록 창 [닫기]");
                        Assert.That(stage.Current, Is.Null);
                        Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "여닫기만 했다");
                        boardEsc = true;
                    }
                    // 배틀(굴린 뒤 운명 창이 열린 순간): 기둥의 [운명의 변화 n/5] → 보기만 하는 손패 창 → Esc
                    if (boardEsc && stage.Current is BattleScreen b && b.Open && b.AskingFate && b.Over == null)
                    {
                        for (int i = 0; i < 3; i++) yield return null;
                        int commands = flow.Session.CommandCount;
                        Assert.That(b.FateOn, Is.True, "배틀 기둥의 [운명의 변화]가 켜져 있다");
                        var button = stage.Root.Q<Button>(BattleScreen.FateButton);
                        Assert.That(button, Is.Not.Null);
                        Assert.That(button.text, Is.EqualTo("운명의 변화 " + flow.Client.View.Seats[0].FateHand.Count + "/5"));
                        Assert.That(flow.OpenFate(), Is.True, "배틀 화면 위에 손패 창");
                        Assert.That(stage.Current, Is.SameAs(b), "배틀 화면은 그대로");
                        Assert.That(flow.Top, Is.InstanceOf<FateScreen>());
                        var fate = (FateScreen)flow.Top;
                        Assert.That(fate.Hand.Count, Is.EqualTo(flow.Client.View.Seats[0].FateHand.Count));
                        Assert.That(stage.Root.Q("board-fate-use-1"), Is.Null, "보기만 — [사용] 단추가 없다");
                        Assert.That(stage.Root.Q<Label>("board-fate-line").text, Is.EqualTo("배틀 중에는 확인만 할 수 있습니다."));
                        Assert.That(fate.Pick(0), Is.True, "카드를 눌러 글은 읽을 수 있다");
                        Assert.That(fate.Use(1), Is.False, "배틀 중에는 여기서 내지 못한다");
                        Assert.That(flow.Escape(), Is.True, "Esc = 손패 창 [닫기]");
                        Assert.That(flow.Top, Is.SameAs(b), "배틀로 돌아왔다");
                        Assert.That(b.AskingFate, Is.True, "독의 운명 창은 그대로 묻고 있다");
                        Assert.That(flow.Escape(), Is.False, "배틀 화면은 Esc를 받지 않는다");
                        Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "보기만 했다 — 답은 나가지 않았다");
                        battleView = true;
                        break;
                    }
                    FlowPilot.Step(flow, pilot);
                }

                Assert.That(discardKept, Is.True, "버리기 창에서 Esc를 눌러 봤다");
                Assert.That(boardEsc, Is.True, "보드에서 창 셋을 Esc로 닫았다");
                Assert.That(battleView, Is.True, "배틀 중 손패를 훑어봤다");
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (rt != null) rt.Release();
            }
        }

        /// <summary>
        /// M9 다듬기 P23(결정 116): Esc가 게임 메뉴를 한 단계씩 닫고(나가기 물음 → 메뉴 → 보드 · 메뉴에서 연 설정 → 메뉴), 배틀 독의 가방 줄을 기술 줄로 닫는다 —
        /// 답은 나가지 않고, 줄이 닫힌 배틀 화면은 Esc를 받지 않는다.
        /// </summary>
        [UnityTest]
        public IEnumerator M9_Esc로게임메뉴와배틀가방줄을한단계씩닫는다()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }
            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
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
                var flow = boot.Flow;
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;

                var title = (TitleScreen)stage.Current;
                int soloIndex = -1;
                for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Solo) soloIndex = i;
                Assert.That(title.Activate(soloIndex), Is.True);
                yield return null;
                var pilot = FlowPilot.Start(flow, SoloTrail.Catch);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");

                bool menuEsc = false, bagEsc = false;
                float t0 = Time.realtimeSinceStartup;
                for (int guard = 0; guard < 40000 && Time.realtimeSinceStartup - t0 < 240f && !bagEsc; guard++)
                {
                    flow.Skip(150);
                    yield return null;
                    // 보드(위에 창 없음): 게임 메뉴 → 나가기 물음 → Esc(메뉴) → [설정] → Esc(메뉴) → Esc(보드)
                    if (!menuEsc && stage.Current == null && flow.InputOpen)
                    {
                        int commands = flow.Session.CommandCount;
                        flow.OpenMenu();
                        var menu = stage.Current as BoardMenuScreen;
                        Assert.That(menu, Is.Not.Null, "게임 메뉴가 열렸다");
                        menu.AskQuit();
                        Assert.That(menu.Asking, Is.True);
                        Assert.That(flow.Escape(), Is.True, "Esc = 나가기 물음의 [취소]");
                        Assert.That(stage.Current, Is.SameAs(menu), "메뉴는 그대로");
                        Assert.That(menu.Asking, Is.False);
                        menu.OpenSettings();
                        Assert.That(stage.Current, Is.InstanceOf<SettingsScreen>());
                        Assert.That(flow.Escape(), Is.True, "Esc = 설정 [뒤로]");
                        Assert.That(stage.Current, Is.InstanceOf<BoardMenuScreen>(), "설정에서 메뉴로");
                        Assert.That(flow.MenuUp, Is.True);
                        Assert.That(flow.Escape(), Is.True, "Esc = [게임으로 돌아가기]");
                        Assert.That(stage.Current, Is.Null, "보드로");
                        Assert.That(flow.MenuUp, Is.False);
                        Assert.That(flow.Escape(), Is.False, "창도 고른 칸도 없으면 아무 일도 없다");
                        Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "여닫기만 했다");
                        menuEsc = true;
                    }
                    // 배틀(내 차례 · 볼이 있다): [가방] → Esc = [뒤로]
                    if (menuEsc && stage.Current is BattleScreen b && b.Open && b.Over == null && b.BagOn)
                    {
                        int commands = flow.Session.CommandCount;
                        Assert.That(b.OpenBag(), Is.True);
                        Assert.That(stage.Root.Q("battle-bag-back"), Is.Not.Null, "볼 줄이 떠 있다");
                        Assert.That(flow.Escape(), Is.True, "Esc = 가방 줄 [뒤로]");
                        Assert.That(b.BagOpen, Is.False);
                        Assert.That(stage.Current, Is.SameAs(b), "배틀 화면은 그대로");
                        Assert.That(stage.Root.Q("battle-bag-back"), Is.Null, "기술 줄로 돌아왔다");
                        Assert.That(flow.Escape(), Is.False, "줄이 닫힌 배틀 화면은 Esc를 받지 않는다");
                        Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "답은 나가지 않았다");
                        bagEsc = true;
                        break;
                    }
                    FlowPilot.Step(flow, pilot);
                }

                Assert.That(menuEsc, Is.True, "게임 메뉴를 Esc로 닫았다");
                Assert.That(bagEsc, Is.True, "가방 줄을 Esc로 닫았다");
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (rt != null) rt.Release();
            }
        }
    }
}
