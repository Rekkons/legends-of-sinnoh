using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>본편 해 보기 피드백(사용자 결정 101)의 화면 흐름: 막 안내 창이 막 카드 뒤에 저절로 뜨고 [확인]까지 머문다 · 막 배지로 다시 본다 · Esc.</summary>
    public class PlayFeedbackFlowTests
    {
        [UnityTest]
        public IEnumerator M65_결정101_막안내창은막카드뒤에뜨고확인까지머물며막배지로다시본다()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            ScreenBoot boot = null;
            try
            {
                yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    boot = Object.FindAnyObjectByType<ScreenBoot>();
                    if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
                }
                Assert.That(boot?.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                boot.PointerFromMouse = false;
                Assert.That(boot.Flow.ActNotice, Is.False, "에디터(플레이 시험)에서는 막 안내 창이 저절로 뜨지 않는다 — 길잡이가 창에 막히지 않게");
                boot.ActNotice = true;   // 사람이 하는 판처럼
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                var flow = boot.Flow;
                var stage = boot.Root.Stage;

                // 혼자 하는 판을 열고 셋업을 지난다(고향 마을 · 스타팅 포켓몬 · 트레이너 카드)
                flow.StartSolo(new SoloSettings(), 179, 99);
                var village = (SetupVillageScreen)stage.Current;
                village.Pick(0);
                Assert.That(village.Confirm(), Is.True, "고향 마을 확정");
                yield return Until(flow, () => stage.Current is SetupPickScreen p && p.Starters.Count > 0, "스타팅 포켓몬 · 트레이너 카드 고르기");
                var pick = (SetupPickScreen)stage.Current;
                // 카드가 들어오는 셋업 연출이 끝나야 고를 수 있다
                bool sent = false;
                yield return Until(flow, () =>
                {
                    pick = stage.Current as SetupPickScreen;
                    if (sent || pick == null) return sent;
                    if (!pick.CanConfirm)
                    {
                        pick.PickStarter(1);
                        pick.PickCard(0);
                    }
                    return sent = pick.CanConfirm && pick.Confirm();
                }, "셋업 확정");

                // 서막 카드 → 1막 카드 → 내 턴 컷신이 다 지나 보드 입력이 열리면 막 안내 창이 선다(1막 — 방금 바뀐 막)
                bool card = false;
                yield return Until(flow, () =>
                {
                    if (flow.Hud != null && flow.Hud.ActCard.Active)
                    {
                        card = true;
                        Assert.That(stage.Current is ActScreen, Is.False, "막 카드 연출이 도는 동안에는 막 안내 창이 없다");
                    }
                    return stage.Current is ActScreen;
                }, "막 안내 창");
                Assert.That(card, Is.True, "막 카드 연출(4행)은 그대로 돈다");
                var act = (ActScreen)stage.Current;
                Assert.That(act.Act, Is.EqualTo(1), "1막");
                Assert.That(act.Changed, Is.True, "막이 방금 바뀌어 뜬 것");
                Assert.That(act.Got, Is.EqualTo(-1), "아직 이룬 목표가 없다");
                Assert.That(act.Sections, Is.EqualTo(new[] { "goal", "gain", "change" }), "목표 / 달성하면 얻는 것 / 이번 막에서 달라지는 것");
                Assert.That(stage.Root.Q<Label>("board-act-name").text, Is.EqualTo(stage.T("screen.board.act.1")));
                Assert.That(stage.Root.Q("board-act-new"), Is.Not.Null, "「막이 바뀌었습니다」 배지");
                Assert.That(stage.Root.Q("board-act-goal").Query<Label>().ToList()[1].text.Replace("⁠", ""), Is.EqualTo(stage.T("screen.act.goal.1")));
                Assert.That(stage.Root.Q("board-act-gain").Query<Label>().ToList().Count, Is.EqualTo(3), "얻는 것 두 줄(이동력 +2 · 조무래기 토큰)");
                Assert.That(flow.InputOpen, Is.False, "창이 떠 있는 동안 보드 입력은 닫힌다");

                // [확인]을 누를 때까지 머문다: 시간을 보내도 그대로
                for (int i = 0; i < 20; i++)
                {
                    flow.Skip(500);
                    yield return null;
                }
                Assert.That(stage.Current, Is.SameAs(act), "[확인]을 누를 때까지 머문다");
                var ok = stage.Root.Q<Button>("board-act-ok");
                Assert.That(ok, Is.Not.Null);
                Assert.That(TutorialLayerFlowTests.Clicked(ok), Is.True);
                flow.CloseAct();   // 단추의 동작(창 없는 PlayMode는 Clickable을 직접 부르지 못한다)
                yield return null;
                Assert.That(stage.Current, Is.Null, "[확인] 뒤 보드");
                yield return Until(flow, () => flow.InputOpen, "보드 입력");

                // 다시 뜨지 않는다(알림은 한 번) · 막 배지를 누르면 다시 본다(바뀜 배지 없이)
                for (int i = 0; i < 6; i++)
                {
                    flow.Skip(200);
                    yield return null;
                }
                Assert.That(stage.Current, Is.Null, "같은 막 안내가 다시 저절로 뜨지 않는다");
                var badge = stage.Root.Q("board-hud-act");
                Assert.That(badge, Is.Not.Null);
                Assert.That(TutorialLayerFlowTests.Clicked(badge), Is.True, "막 배지는 누를 수 있다");
                yield return null;
                var again = stage.Current as ActScreen;
                Assert.That(again, Is.Not.Null, "막 배지를 누르면 막 안내 창");
                Assert.That(again.Act, Is.EqualTo(1));
                Assert.That(again.Changed, Is.False);
                Assert.That(stage.Root.Q("board-act-new"), Is.Null);
                Assert.That(flow.Escape(), Is.True, "Esc = [확인]");
                Assert.That(stage.Current, Is.Null);
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }

        static IEnumerator Until(GameFlow flow, System.Func<bool> done, string what)
        {
            for (int i = 0; i < 1200 && !done(); i++)
            {
                flow.Skip(100);
                flow.Stage.Tweens.Advance(100);   // 셋업 연출 · 막 카드는 화면 시계로 돈다(배치 프레임은 실제 시간이 거의 흐르지 않는다)
                yield return null;
            }
            Assert.That(done(), Is.True, "시간 초과: " + what);
        }
    }
}
