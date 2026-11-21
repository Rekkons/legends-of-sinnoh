using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M9 플레이 시험 T4 · T8 · T16(결정 120 · 126): 방장 시각 판(<see cref="GameFlow.HostPaced"/> — 방 판과 같은 길을 좌석 둘 확인 판으로)에서 남의 야생 배틀을 볼 때
    /// 화면 대기열이 방장 멈춤과 같은 때에 끝난다 — ① 접은 동안에도 흐른다 ② 열고 접기를 되풀이해도 끝나는 시각이 같다 ③ 열어 둔 채 결과 창까지 가도 늦지 않고, 결과 창은 내가 [확인]을 누르지 않아도 닫힌다.
    /// 시간은 실제 프레임과 같은 길(<see cref="GameFlow.Skip"/> = 세션 Tick + 무대 트윈 실행기)로 보내고, 남 좌석은 방장 멈춤이 끝난 뒤에만 답한다(방의 AI와 같은 박자).
    /// </summary>
    public sealed class HostPacedFlowTests
    {
        const int W = 1280, H = 720;
        /// <summary>끝 시각 허용(ms): 클릭 막기 300 + 프레임 한 번(100) + 여유.</summary>
        const long EndSlackMs = 600;
        /// <summary>방장보다 늦어도 되는 한도(ms) — RoomSyncFlowTests의 LateLimitMs와 같다.</summary>
        const long LateLimitMs = 1200;

        ScreenBoot boot;
        GameFlow flow;
        UiStage stage;
        RenderTexture rt;
        readonly List<string> errors = new List<string>();

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
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

        IEnumerator Open()
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
            flow.StartSeats(new SoloSettings(), 2, 20261116, 7);
            flow.HostPaced = true;
            flow.WatchWildBattles = true;
            for (int i = 0; i < 6; i++) yield return null;
            // 내 턴까지(남 좌석이 먼저면 턴 종료) — 그 뒤 내 턴을 넘긴다
            for (int i = 0; i < 600 && !(flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null); i++)
            {
                if (flow.Client.View.ActiveSeat != flow.Client.Seat && flow.Fx.Sequencer.InputOpen) flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == AnswerKind.EndTurn);
                flow.Skip(100);
                yield return null;
            }
            Assert.That(flow.Client.View.ActiveSeat, Is.EqualTo(flow.Client.Seat), "내 턴");
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");
        }

        enum Watch { Fold, Toggle, Keep }

        sealed class Run
        {
            public bool SawBattle, Folded, DrewFolded, ReopenedFolded, SawResult, ResultClosedByItself, Paused, Done;
            public int Toggles;
            public long DoneAt, HostEnd;
        }

        // 남 좌석(1)이 찾아보기 → 야생 배틀 → 턴 종료까지(방장 멈춤이 끝난 뒤에만 답한다), 내 다음 턴의 입력이 열릴 때까지 프레임을 보낸다
        IEnumerator Play(Watch watch, Run run)
        {
            int me = flow.Client.Seat, other = 1 - me;
            bool ruleBattle = false, ruleDone = false;
            long nextToggle = -1;
            for (int guard = 0; guard < 6000 && !run.Done; guard++)
            {
                flow.Skip(100);
                yield return null;
                var view = flow.Client.View;
                if (view.Battle != null) ruleBattle = true;
                else if (ruleBattle) ruleDone = true;
                if (flow.Fx.Sequencer.Paused) run.Paused = true;
                if (flow.WildAside)
                {
                    run.SawBattle = true;
                    var cur = flow.Fx.Sequencer.Current;
                    if (flow.WatchingWild && stage.Current is BattleScreen)
                    {
                        if (watch == Watch.Fold && !flow.BattleOver)
                        {
                            Assert.That(flow.CloseWildWatch(), Is.True, "[보드로] = 접기");
                            run.Folded = true;
                        }
                        else if (watch == Watch.Toggle && flow.NowMs >= nextToggle && !flow.BattleOver)
                        {
                            Assert.That(flow.CloseWildWatch(), Is.True, "접기");
                            run.Toggles++;
                            nextToggle = flow.NowMs + 1500;
                        }
                        if ((stage.Current as BattleScreen)?.ResultShown == true) run.SawResult = true;  // 결과 창(70행 — 줄이 내려앉는 동안 · [확인]을 기다리는 동안)
                    }
                    else if (flow.WildFolded)
                    {
                        if (stage.Current is BattleScreen) run.ReopenedFolded = true;
                        if (cur != null && cur.Steps.Exists(s => s.Events.Exists(e => e is DiceRolled || e is HpChanged))) run.DrewFolded = true;
                        if (watch == Watch.Toggle && flow.NowMs >= nextToggle)
                        {
                            Assert.That(flow.OpenWildWatch(), Is.True, "[보기]");
                            run.Toggles++;
                            nextToggle = flow.NowMs + 1500;
                        }
                    }
                }
                else if (run.SawResult && !run.ResultClosedByItself && !(stage.Current is BattleScreen)) run.ResultClosedByItself = true;
                // 내 다음 턴: 입력이 열렸다 — 끝 시각을 적는다
                if (run.SawBattle && ruleDone && flow.Battle == null && view.ActiveSeat == me && flow.Client.Choice(ChoiceKind.MapAction) != null && flow.Fx.Sequencer.IsEmpty)
                {
                    run.DoneAt = flow.NowMs;
                    run.HostEnd = flow.Client.AnimEndMs;
                    run.Done = true;
                    break;
                }
                if (flow.NowMs < flow.Client.AnimEndMs) continue;   // 방장 멈춤 동안은 아무도 답하지 않는다(방의 AI와 같은 박자)
                var mine = flow.Client.FirstChoice();
                if (mine != null && view.ActiveSeat != me) flow.Answer(mine, mine.Default.Clone());   // 남의 턴에 내게 온 물음(반응 등)은 기본 답
                else if (mine != null && mine.Kind == ChoiceKind.MapAction && !ruleBattle)
                {
                    var end = flow.Client.Legal(mine).Find(a => a.Kind == AnswerKind.EndTurn);
                    if (end != null) flow.Answer(mine, end);   // 좌석 2가 배틀 없이 턴을 넘겼다 — 내 턴도 넘긴다
                }
                if (view.ActiveSeat != other) continue;
                if (ruleDone)
                {
                    if (!flow.AnswerSeat(other, a => a.Kind == AnswerKind.EndTurn) && !flow.AnswerSeat(other, a => a.Kind == AnswerKind.Decline)) flow.AnswerSeat(other);
                    continue;
                }
                if (flow.AnswerSeat(other, a => a.Kind == AnswerKind.Search)) continue;
                if (flow.AnswerSeat(other, a => a.Kind == AnswerKind.UseMove && a.Arg < 0)) continue;
                if (view.Battle == null && !view.Seats[other].Moved && flow.AnswerSeat(other, a => a.Kind == AnswerKind.Move)) continue;
                if (!flow.AnswerSeat(other, a => a.Kind == AnswerKind.Decline)) flow.AnswerSeat(other);
            }
            Assert.That(run.Done, Is.True, $"좌석 2의 야생 배틀 뒤 내 턴까지 가지 않았다 — 배틀 봄 {run.SawBattle} · 화면 {stage.Current?.Name ?? "보드"} · 턴 {flow.Client.View.ActiveSeat + 1}");
        }

        void Ends(Run run, string what)
        {
            long late = run.DoneAt - run.HostEnd;
            TestContext.Out.WriteLine($"[T4 {what}] 내 입력 열림 {run.DoneAt} · 방장 멈춤 끝 {run.HostEnd} · 차이 {late} ms · 따라잡기 차이 최소 {flow.Drift.MinMs} · 최대 {flow.Drift.MaxMs} · 갱신 {flow.Drift.Count}");
            Assert.That(late, Is.LessThanOrEqualTo(EndSlackMs), what + ": 내 화면이 방장 멈춤보다 늦게 끝나지 않는다");
            Assert.That(flow.Drift.MinMs, Is.GreaterThanOrEqualTo(-LateLimitMs), what + ": 갱신마다 내 대기열이 방장 멈춤보다 크게 늦지 않았다");
            Assert.That(run.Paused, Is.False, what + ": 화면 대기열이 서지 않았다(결과 창 · 결정 126)");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest, NUnit.Framework.Timeout(300000)]
        public IEnumerator M9_방장시각판_남의야생배틀을접은동안에도대기열이흐르고_다시열지않으며_결과창없이제때끝난다_T4()
        {
            yield return Open();
            var run = new Run();
            yield return Play(Watch.Fold, run);
            Assert.That(run.Folded, Is.True, "창이 없어 저절로 열린 배틀을 접었다(결정 120)");
            Assert.That(run.DrewFolded, Is.True, "접은 동안에도 배틀 안(굴림 · 체력)이 대기열에서 흘렀다");
            Assert.That(run.ReopenedFolded, Is.False, "접은 배틀은 저절로 다시 열지 않는다");
            Assert.That(run.SawResult, Is.False, "접은 배틀은 결과 창 없이 끝난다");
            Ends(run, "접기");
        }

        [UnityTest, NUnit.Framework.Timeout(300000)]
        public IEnumerator M9_방장시각판_남의야생배틀을열고접기를되풀이해도_끝나는시각이방장과같다_T4()
        {
            yield return Open();
            var run = new Run();
            yield return Play(Watch.Toggle, run);
            Assert.That(run.Toggles, Is.GreaterThanOrEqualTo(4), "열고 접기를 여러 번 했다");
            Ends(run, "열고 접기");
        }

        [UnityTest, NUnit.Framework.Timeout(300000)]
        public IEnumerator M9_방장시각판_남의야생배틀을열어둔채끝나도늦지않고_결과창은내가확인을누르지않아도닫힌다_T4_T8()
        {
            yield return Open();
            var run = new Run();
            yield return Play(Watch.Keep, run);
            Assert.That(run.SawResult, Is.True, "열어 둔 배틀의 결과 창을 봤다");
            Assert.That(run.ResultClosedByItself, Is.True, "남의 결과 창은 그 뒤 연출이 시작되면 내가 [확인]을 누르지 않아도 닫힌다(T8)");
            Ends(run, "열어 둠");
        }
    }
}
