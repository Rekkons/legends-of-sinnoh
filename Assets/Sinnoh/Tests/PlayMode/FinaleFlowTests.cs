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
    /// 사람 혼자 하는 판이 종막까지 화면으로 간다(M5 「막·종막 화면」 — 04 S14·S54·S62·S64, §8-8 4·72·94·95·97행): 타이틀 → 혼자 하기 → 셋업(화면) →
    /// 막 길 <see cref="SoloTrail.Heart"/>(셋업 뒤 3막 · 조각 셋 · 시작의 사당으로 손본 판 — 2막 이후를 실제 진행으로 가면 판 하나가 수백 턴이다) →
    /// [공물](심장 회복 연출) → 신오신전까지 걸어 [도전] → 태홍의 명단 볼 6개 → 배틀 → 결과 창 → 종막 카드 → 종막 선택 창 [영웅] → 다른 카드가 타 버림 → 결과 창(S64) → [타이틀로].
    /// 답은 길잡이가 고르고 <see cref="FlowPilot"/>이 화면 요소로 누른다(dotnet <c>SoloPilotTests.M5_막길_…</c>가 같은 판을 화면 없이 돈다). 받을 화면이 없는 선택은 0번.
    /// </summary>
    public sealed class FinaleFlowTests
    {
        [UnityTest]
        public IEnumerator M5_사람혼자하는판이공물과태홍을거쳐종막선택과결과창까지화면으로간다()
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
                var pilot = FlowPilot.Start(flow, SoloTrail.Heart);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");
                Assert.That(flow.Client.View.Hos.Act, Is.EqualTo(3), "막 길: 3막");
                string notReady = stage.T("screen.board.guide.not_ready");

                bool offerSeen = false, heartSeen = false, finaleCard = false, asked = false, burned = false, resultSeen = false;
                int balls = 0, mons = 0, endingArt = 0;
                ResultScreen result = null;
                float t0 = Time.realtimeSinceStartup;
                for (int guard = 0; guard < 40000 && Time.realtimeSinceStartup - t0 < 240f; guard++)
                {
                    // 연출은 250ms씩 — 한 번에 1초씩 보내면 짧은 연출(심장 2500 · 볼 200 간격)의 가운데를 건너뛴다
                    flow.Skip(250);
                    yield return null;
                    var guide = stage.Root.Q<Label>("board-hud-guide");
                    if (guide != null) Assert.That(guide.text, Is.Not.EqualTo(notReady), "HUD에 준비 중 안내가 떴다");
                    if (flow.Hud != null && flow.Hud.HeartShown) heartSeen = true;
                    if (stage.Root.Q("board-hud-act-" + BoardHud.Offer) != null) offerSeen = true;
                    if (flow.Hud != null && stage.Root.Q("act-card") != null)
                    {
                        var card = flow.Fx.Sequencer.Current != null ? Sinnoh.Fx.ActBeats.StepOf(flow.Fx.Sequencer.Current) : null;
                        if (card != null && card.Tag is ActCardInfo info && info.Act == 4 && info.FirstSeat == 0) finaleCard = true;
                    }
                    if (stage.Current is BattleScreen battle) balls = Mathf.Max(balls, battle.LineupShown);
                    if (stage.Current is EndingScreen ending)
                    {
                        if (ending.Asking) { asked = true; endingArt = Mathf.Max(endingArt, ending.ArtShown); }
                        if (ending.Burn > 0f && ending.Chosen == 0) burned = true;
                    }
                    if (stage.Current is ResultScreen r)
                    {
                        resultSeen = true;
                        mons = Mathf.Max(mons, r.MonsShown);
                        if (r.Ready) { result = r; break; }
                        continue;
                    }
                    FlowPilot.Step(flow, pilot);
                }

                Assert.That(offerSeen, Is.True, "[공물] 단추(시작의 사당 · 3막)");
                Assert.That(events.Exists(e => e is HeartRestored), Is.True, "심장 회복");
                Assert.That(heartSeen, Is.True, "심장 회복 연출(94행 — 조각 셋이 심장으로)을 봤다");
                Assert.That(balls, Is.EqualTo(HosFlow.BossLineup), "태홍의 명단 볼 6개가 놓였다(72행 S54)");
                Assert.That(finaleCard, Is.True, "종막 카드(4행 — 처음 달성한 좌석)");
                Assert.That(asked, Is.True, "종막 선택 창(S62)");
                Assert.That(endingArt, Is.EqualTo(2), "두 카드에 원작 종막 문양(결정 80 L1 나)");
                Assert.That(burned, Is.True, "[영웅] 뒤 배신자 카드가 타 들어갔다(95행)");
                Assert.That(resultSeen && result != null, Is.True, "결과 창(S64)이 떠서 [타이틀로]가 켜졌다");
                Assert.That(result.Reason, Is.EqualTo(GameEndReason.Hero));
                Assert.That(result.Winner, Is.EqualTo(0));
                int party = 0;
                foreach (int id in flow.Client.View.Seats[0].Party) if (id >= 0) party++;
                Assert.That(mons, Is.EqualTo(party), "파티 포켓몬이 하나씩 다 나왔다(97행)");
                Assert.That(result.StatsShown, Is.True);
                Assert.That(flow.Client.View.Phase, Is.EqualTo(Phase.GameOver));
                Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택이 없었다");
                Assert.That(result.PressTitle(), Is.True);
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<TitleScreen>(), "[타이틀로]");
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
