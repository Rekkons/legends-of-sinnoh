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
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 결정 201 ①(2026-11-22): 간부 · 태홍 배틀의 상대 기둥에 앞면으로 공개된 명단 줄(<c>battle-roster-slot-n</c>)이 선다 — 칸 수 = 명단 수,
    /// 마우스 오버 = 그 포켓몬 카드(아직 안 나온 것도), 쓰러진 칸은 흐림. 명단을 고르는 동안(Lineup — 보기에 아직 배틀 명단이 없다)에는 줄이 없다.
    /// 막 길 <see cref="SoloTrail.Lake"/>(2막 · 조각 둘 — 입지호수의 새턴, 명단 2 + 2). UiShots <c>roster-*</c>가 같은 판을 찍는다.
    /// </summary>
    public sealed class GalacticRosterFlowTests
    {
        [UnityTest]
        public IEnumerator M10_결정201_간부전상대기둥에명단줄이서고마우스오버는그포켓몬카드()
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
                var pilot = FlowPilot.Start(flow, SoloTrail.Lake);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");

                bool hovered = false, fainted = false;
                int rows = 0;
                float t0 = Time.realtimeSinceStartup;
                for (int guard = 0; guard < 20000 && Time.realtimeSinceStartup - t0 < 180f && !fainted; guard++)
                {
                    flow.Skip(250);
                    yield return null;
                    var view = flow.Client.View;
                    var screen = stage.Current as BattleScreen;
                    if (view.Pending.Exists(p => p.Kind == ChoiceKind.Lineup && !p.Answered))
                    {
                        // 명단을 고르는 중(갤럭시단 길잡이가 곧 답해 프레임에 걸리지 않을 수 있다): 보기에 배틀 명단이 없고 화면도 줄을 세우지 않는다
                        Assert.That(screen == null || screen.RosterCount == 0, Is.True, "명단을 고르기 전에 명단 줄이 섰다");
                    }
                    if (screen != null && screen.Open && screen.RosterCount > 0 && view.Battle != null)
                    {
                        var roster = view.Battle.Sides[1 - flow.Battle.MySide].Roster;
                        Assert.That(screen.RosterCount, Is.EqualTo(roster.Count), "명단 칸 수 = 명단 수");
                        rows = roster.Count;
                        int dim = 0;
                        for (int n = 1; n <= roster.Count; n++)
                        {
                            var slot = stage.Root.Q("battle-roster-slot-" + n);
                            Assert.That(slot, Is.Not.Null, "명단 칸 " + n);
                            if (slot.ClassListContains(BattleScreen.RosterDoneClass)) dim++;
                        }
                        Assert.That(dim, Is.EqualTo(screen.RosterDone), "흐린 칸 수 = 쓰러진 수");
                        if (screen.RosterActive >= 0) Assert.That(stage.Root.Q("battle-roster-slot-" + (screen.RosterActive + 1)).ClassListContains(BattleScreen.RosterDoneClass), Is.False, "나와 있는 칸은 흐리지 않다");
                        if (!hovered && screen.Moves.Count > 0)
                        {
                            for (int n = 1; n <= roster.Count; n++)
                            {
                                Assert.That(screen.HoverRoster(n), Is.True, "명단 칸 " + n + "에 카드");
                                yield return null;
                                Assert.That(screen.MonTipShown, Is.EqualTo(view.Pokemon[roster[n - 1]].Def), "명단 칸 " + n + "의 카드 = 그 포켓몬(아직 안 나온 것도)");
                                var card = screen.MonTipElement.worldBound;
                                var root = stage.Root.worldBound;
                                Assert.That(card.xMin >= root.xMin && card.xMax <= root.xMax && card.yMin >= root.yMin && card.yMax <= root.yMax, Is.True, "카드가 화면 안: " + card);
                            }
                            hovered = true;
                        }
                        if (screen.RosterDone >= 1 && hovered)
                        {
                            Assert.That(screen.HoverRoster(1), Is.True, "쓰러진 칸에도 카드");
                            yield return null;
                            fainted = true;
                            continue;
                        }
                    }
                    if (screen != null && screen.ShowsResult) break;
                    FlowPilot.Step(flow, pilot);
                }
                Assert.That(events.Exists(e => e is GalacticBattle g && g.Leader == GalacticLeader.Commander && g.LineupCount == rows), Is.True, "명단 공개(GalacticBattle)와 같은 수");
                Assert.That(rows, Is.EqualTo(HosFlow.CommanderLineup(2)), "새턴의 명단 = 조각 둘 + 2");
                Assert.That(hovered, Is.True, "명단 칸마다 카드를 띄웠다");
                Assert.That(fainted, Is.True, "쓰러진 칸이 흐려졌다");
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var shut = Object.FindAnyObjectByType<ScreenBoot>();
                if (shut != null && shut.Root != null) shut.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }
    }
}
