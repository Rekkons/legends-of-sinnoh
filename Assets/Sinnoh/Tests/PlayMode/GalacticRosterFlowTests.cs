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
    /// 결정 201 ①(2026-11-22): 간부 · 태홍 배틀의 앞면으로 공개된 명단 — 결정 206 ②부터 상대 기둥 머리 줄의 [상대 엔트리 n] → 엔트리 판(<c>battle-entry-n</c>): 카드 수 = 명단 수,
    /// 아직 안 나온 것도 그 포켓몬 카드, 쓰러진 카드는 흐림 · 꼬리표 「기절」. 명단을 고르는 동안(Lineup — 보기에 아직 배틀 명단이 없다)에는 단추가 없다.
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
                        Assert.That(screen == null || (screen.RosterCount == 0 && stage.Root.Q(BattleScreen.EntryFoeButton) == null), Is.True, "명단을 고르기 전에 [상대 엔트리]가 섰다");
                    }
                    if (screen != null && screen.Open && screen.RosterCount > 0 && view.Battle != null)
                    {
                        var roster = view.Battle.Sides[1 - flow.Battle.MySide].Roster;
                        Assert.That(screen.RosterCount, Is.EqualTo(roster.Count), "명단 칸 수 = 명단 수");
                        rows = roster.Count;
                        // 결정 206 ②: 명단 줄 대신 [상대 엔트리 n] → 엔트리 판(카드마다 꼬리표 · 쓰러진 카드 흐림)
                        Assert.That(stage.Root.Q(BattleScreen.EntryFoeButton), Is.Not.Null, "[상대 엔트리] 단추");
                        if (!screen.EntryOpen) Assert.That(screen.OpenEntry(false, false), Is.True, "상대 엔트리 판");
                        Assert.That(screen.EntryShown.Count, Is.EqualTo(roster.Count), "엔트리 카드 수 = 명단 수");
                        int dim = 0;
                        for (int n = 1; n <= roster.Count; n++)
                        {
                            var slot = stage.Root.Q(BattleScreen.EntryPanel + "-" + n);
                            Assert.That(slot, Is.Not.Null, "엔트리 카드 " + n);
                            if (slot.ClassListContains(BattleScreen.RosterDoneClass)) dim++;
                        }
                        Assert.That(dim, Is.EqualTo(screen.RosterDone), "흐린 칸 수 = 쓰러진 수");
                        if (screen.RosterActive >= 0)
                        {
                            Assert.That(stage.Root.Q(BattleScreen.EntryPanel + "-" + (screen.RosterActive + 1)).ClassListContains(BattleScreen.RosterDoneClass), Is.False, "나와 있는 카드는 흐리지 않다");
                            Assert.That(screen.EntryTag(screen.RosterActive + 1), Is.EqualTo("screen.battle.entry.active"), "나와 있는 카드 꼬리표 = 배틀 중");
                        }
                        if (!hovered && screen.Moves.Count > 0)
                        {
                            // 판 기하(한 번 — 판은 이미 열려 있다): 화면 안 · 기둥 오른쪽 · 기록 기둥 가운데에서 끊기지 않는다(검토 지적 5 · 23)
                            yield return null;
                            var panel = stage.Root.Q(BattleScreen.EntryPanel).worldBound;
                            var root = stage.Root.worldBound;
                            Assert.That(panel.xMin >= root.xMin && panel.xMax <= root.xMax && panel.yMin >= root.yMin && panel.yMax <= root.yMax, Is.True, "판이 화면 안: " + panel);
                            Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(stage.Root.Q("battle-info").worldBound.xMax - 0.5f), "판은 기둥 오른쪽");
                            var log = stage.Root.Q("battle-log").worldBound;
                            Assert.That(panel.xMax <= log.xMin + 0.5f || panel.xMax >= log.xMax - 0.5f, Is.True, "판 오른쪽 끝이 기록 기둥 가운데에 걸리지 않는다: " + panel + " · " + log);
                            Assert.That(stage.Root.Q<Button>("battle-entry-close"), Is.Not.Null, "[닫기]");
                            // 카드마다: 그 명단 칸의 포켓몬 · 판 안 · 「배틀 중」 테는 나와 있는 카드에만
                            for (int n = 1; n <= roster.Count; n++)
                            {
                                Assert.That(screen.HoverRoster(n), Is.True, "엔트리 카드 " + n);
                                var col = stage.Root.Q(BattleScreen.EntryPanel + "-" + n);
                                Assert.That(view.Pokemon[screen.EntryShown[n - 1]].Def, Is.EqualTo(view.Pokemon[roster[n - 1]].Def), "엔트리 카드 " + n + " = 그 포켓몬(아직 안 나온 것도)");
                                Assert.That(panel.Contains(col.worldBound.center), Is.True, "엔트리 카드 " + n + "는 판 안");
                                Assert.That(col.ClassListContains(BattleScreen.EntryNowClass), Is.EqualTo(n - 1 == screen.RosterActive), "「배틀 중」 테는 나와 있는 카드에만 " + n);
                            }
                            hovered = true;
                        }
                        if (screen.RosterDone >= 1 && hovered)
                        {
                            int down = -1;
                            for (int n = 1; n <= screen.RosterCount && down < 0; n++)
                                if (stage.Root.Q(BattleScreen.EntryPanel + "-" + n).ClassListContains(BattleScreen.RosterDoneClass)) down = n;
                            Assert.That(down, Is.GreaterThan(0), "흐린(쓰러진) 카드");
                            Assert.That(screen.HoverRoster(down), Is.True, "쓰러진 카드도 판에");
                            Assert.That(screen.EntryTag(down), Is.EqualTo("screen.info.fainted"), "쓰러진 카드 꼬리표 = 기절");
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
