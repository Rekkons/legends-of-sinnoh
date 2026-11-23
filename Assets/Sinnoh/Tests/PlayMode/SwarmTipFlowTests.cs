using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 결정 199 ①(2026-11-21): 배틀 화면 상대 기둥의 무리 카드(<c>battle-swarm-slot-n</c> — 결정 206 ②부터 엇겹친 카드 셋)에 마우스를 올리면 셋을 펼친 엔트리 판(<c>battle-entry-n</c>)이 뜬다 —
    /// 아직 안 나온 포켓몬도(무리 셋은 공개 정보, 룰북 p.12). 왜곡 게이트(디프마시)의 무리 배틀. UiShots <c>swarm-tip-*</c>가 같은 판을 찍는다.
    /// </summary>
    public sealed class SwarmTipFlowTests
    {
        const ulong Seed = 20261112, AiSeed = 7;

        [UnityTest]
        public IEnumerator M10_결정199_무리줄포켓몬에마우스를올리면카드가뜬다()
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
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var ctx = flow.Ctx;
                flow.StartSeats(new SoloSettings(), 1, Seed, AiSeed, state =>
                {
                    var st = state.Seats[0];
                    st.OpenSlots = st.Party.Length;
                    int starter = st.Party[0];
                    for (int i = 0; i < st.Party.Length; i++) st.Party[i] = -1;
                    foreach (string key in SoloTrail.Team) PokemonRules.AddToParty(st, PokemonRules.Spawn(state, ctx.Cards.PokemonIds.IndexOf(key), 0, (int)DeckId.Legendary));
                    if (starter >= 0 && PokemonRules.PartyCount(st) < st.OpenSlots) PokemonRules.AddToParty(st, starter);
                    st.Cell = ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf("gate.deepmarsh_main")].Cell;
                });
                yield return null;
                flow.Skip(flow.OpeningMs);
                for (int i = 0; i < 300 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty || !flow.InputOpen); i++) { flow.Skip(100); if (i % 10 == 0) yield return null; }
                Assert.That(flow.Hud.Press(BoardHud.Search), Is.True, "[찾아보기] — 왜곡 게이트");
                BattleScreen screen = null;
                for (int guard = 0; guard < 400; guard++)
                {
                    screen = stage.Current as BattleScreen;
                    if (screen != null && screen.Open)
                    {
                        if (screen.AskingToken) screen.PressToken(true);
                        else if (screen.AskingOrder) screen.PressOrder(0);
                        else if (screen.Bench.Count > 0) { if (!screen.PressStay()) screen.PressBench(0); }
                        else if (screen.Moves.Count > 0) break;
                    }
                    flow.Skip(250);
                    if (guard % 4 == 0) yield return null;
                }
                Assert.That(screen, Is.Not.Null, "배틀 화면");
                Assert.That(screen.SwarmCount, Is.EqualTo(3), "무리 셋");
                var view = flow.Client.View;
                var roster = view.Battle.Sides[1 - flow.Battle.MySide].Roster;
                // 판 기하(한 번 — 마우스 오버로 연 판): 화면 안 · 기둥 오른쪽 · 기록 기둥 가운데에서 끊기지 않는다(검토 지적 5 · 23)
                Assert.That(screen.HoverSwarm(1), Is.True, "무리 카드 1");
                yield return null;
                yield return null;
                Assert.That(screen.EntryOpen, Is.True, "엇겹친 카드 마우스 오버 = 셋을 펼친 엔트리 판(결정 206 ②)");
                Assert.That(screen.EntryShown.Count, Is.EqualTo(roster.Count), "판에 셋 모두");
                var panel = stage.Root.Q(BattleScreen.EntryPanel).worldBound;
                var root = stage.Root.worldBound;
                Assert.That(panel.xMin >= root.xMin && panel.xMax <= root.xMax && panel.yMin >= root.yMin && panel.yMax <= root.yMax, Is.True, "판이 화면 안: " + panel);
                Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(stage.Root.Q("battle-info").worldBound.xMax - 0.5f), "판은 기둥 오른쪽");
                var log = stage.Root.Q("battle-log").worldBound;
                Assert.That(panel.xMax <= log.xMin + 0.5f || panel.xMax >= log.xMax - 0.5f, Is.True, "판 오른쪽 끝이 기록 기둥 가운데에 걸리지 않는다: " + panel + " · " + log);
                int active = screen.ShownMon(1 - flow.Battle.MySide).Id;
                // 마우스 오버 대상마다: 그 엇겹친 칸 → 판의 그 카드(같은 포켓몬 · 판 안 · 나와 있는 것만 「배틀 중」 테)
                for (int n = 1; n <= roster.Count; n++)
                {
                    Assert.That(stage.Root.Q("battle-swarm-slot-" + n), Is.Not.Null, "엇겹친 무리 칸 " + n);
                    Assert.That(screen.HoverSwarm(n), Is.True, "무리 카드 " + n);
                    yield return null;
                    var col = stage.Root.Q(BattleScreen.EntryPanel + "-" + n);
                    Assert.That(col, Is.Not.Null, "판의 카드 " + n);
                    Assert.That(view.Pokemon[screen.EntryShown[n - 1]].Def, Is.EqualTo(view.Pokemon[roster[n - 1]].Def), "판의 카드 " + n + " = 그 포켓몬(아직 안 나온 것도)");
                    Assert.That(stage.Root.Q(BattleScreen.EntryPanel).worldBound.Contains(col.worldBound.center), Is.True, "카드 " + n + "는 판 안");
                    Assert.That(col.ClassListContains(BattleScreen.EntryNowClass), Is.EqualTo(roster[n - 1] == active), "「배틀 중」 테는 나와 있는 카드에만 " + n);
                }
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
