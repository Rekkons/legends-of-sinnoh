using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M6 「UB 시작」: 타이틀 → 혼자 하기(S09 「캠페인」 울트라 버스트 · 울트라 온슬롯 · 베테랑) → 셋업(S12·S13) → 보드(깨어진 세계 · UB 9 · 네크로즈마 피규어가 토큰 칸에 · 파티 n/6) →
    /// [턴 종료] 둘(라운드 끝마다 UB 턴 — 기록 줄) → 타이틀 → 이어 받기 → 같은 보드(토큰 칸 · 부서진 칸). 저장은 임시 폴더. 네크로즈마 페이즈 폼(손본 판)도 본다.
    /// </summary>
    public sealed class UbStartFlowTests
    {
        const long Clock = 1794000000000;

        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        // UB·네크로즈마 피규어가 보기의 토큰 칸에 섰나(대기열이 빈 때) — 선 수
        static int Standing(GameFlow flow, BoardBoot board)
        {
            int n = 0;
            foreach (var f in board.Figures.Figures)
            {
                if (f.Group != GameFlow.BeastFigures && f.Group != GameFlow.NecrozmaFigures) continue;
                Assert.That(flow.Ctx.Cards.PokemonIds.TryIndexOf(f.Key, out int def), Is.True);
                var at = UbBoard.Of(flow.Client.View.Tokens, def, null, null, 0);
                Assert.That(f.Root.activeSelf, Is.EqualTo(at.Shown), f.Key);
                if (!at.Shown) continue;
                n++;
                Assert.That((f.Root.transform.localPosition - board.CellTop(at.From)).sqrMagnitude, Is.LessThan(1e-4f), f.Key + " 토큰 칸");
            }
            return n;
        }

        [UnityTest]
        public IEnumerator M6_타이틀에서울트라버스트혼자하기로보드까지가고UB턴뒤저장을이어받으면같은보드다_에러로그0()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-ub-test-" + System.Guid.NewGuid().ToString("N"));
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
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                boot.UseSaves(root, () => Clock);
                var flow = boot.Flow;
                var board = Object.FindAnyObjectByType<BoardBoot>();
                Assert.That(board.DistortionWorldVisible, Is.False, "타이틀 뒤(시험 판)에는 깨어진 세계가 숨어 있다");

                // ── S09: 울트라 버스트 · 울트라 온슬롯 사용 · 베테랑 ──
                flow.OpenSolo();
                yield return null;
                var solo = (SoloScreen)stage.Current;
                Assert.That(solo.Set(SoloScreen.Campaign, "ub"), Is.True, "울트라 버스트를 고를 수 있다");
                Assert.That(solo.Set(SoloScreen.Onslaught, "on"), Is.True);
                Assert.That(solo.Set(SoloScreen.GalacticAi, "basic"), Is.True, "이 판은 원칙 AI로(기본값은 「강함」 — 결정 118 ②)");
                Assert.That(solo.Set(SoloScreen.UbDifficulty, "veteran"), Is.True);
                Assert.That(solo.Set(SoloScreen.TrainerCards, "on"), Is.True, "트레이너 카드를 쓰는 판(기본값은 끔 — 결정 134)");
                Assert.That(stage.Root.Q("solo-opt-" + SoloScreen.Campaign + "-ub").ClassListContains("sn-opt-picked"));
                Assert.That(stage.Root.Q("solo-row-" + SoloScreen.Onslaught).ClassListContains("sn-off"), Is.False);
                flow.StartSolo(solo.Settings, 20261117, 99);
                yield return null;

                // ── S12 고향 마을: 울트라메가로폴리스도 줄에 있다 · 셋업부터 깨어진 세계가 보인다 ──
                var village = (SetupVillageScreen)stage.Current;
                Assert.That(flow.Client.View.Campaign, Is.EqualTo(Campaign.UltraBurst));
                Assert.That(stage.Root.Q("setup-village-item-ultra_megalopolis"), Is.Not.Null, "울트라 버스트에서는 울트라메가로폴리스도 고향 마을(G12)");
                Assert.That(board.DistortionWorldVisible, Is.True);
                village.Pick(0);
                Assert.That(village.Confirm(), Is.True);
                flow.Skip(flow.QueueLeftMs);
                yield return null;
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(0);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                yield return null;
                flow.Skip(flow.OpeningMs);
                yield return Until(() => flow.InputOpen && flow.Hud != null, 20f);
                Assert.That(flow.InputOpen, Is.True, "첫 내 턴의 입력이 열린다");

                // ── 보드: 셋업 값(p.23) · UB 9 + 네크로즈마 피규어 · 파티 n/6 ──
                var view = flow.Client.View;
                Assert.That(view.Hos, Is.Null);
                Assert.That(view.Seats[0].OpenSlots, Is.EqualTo(6));
                Assert.That(view.Ub.Blessings, Is.EqualTo(1), "베테랑 = 축복 1");
                Assert.That(view.Options.UltraOnslaught, Is.True);
                foreach (var c in new[] { ApricornColor.Red, ApricornColor.Blue, ApricornColor.Yellow })
                    Assert.That(view.Seats[0].Apricorns[(int)c], Is.GreaterThanOrEqualTo(UltraBurstSetup.StartingApricorns), c.ToString());
                Assert.That(Standing(flow, board), Is.EqualTo(UbState.Beasts + 1));
                var partyHead = stage.Root.Q("board-hud-party").Q<Label>(className: "kit-title");
                Assert.That(partyHead.text, Does.EndWith("/6"), "파티 머리 = n/6");
                Assert.That(flow.SaveId, Is.EqualTo(GameFlow.SoloSlot), "울트라 버스트 판도 자동 저장 한 칸에");

                // ── 위 띠 프리즘 칩의 마우스 오버: 개수 줄 + 효과 문구(결정 193) ──
                var prismChip = stage.Root.Q(BoardUb.Prism);
                Assert.That(prismChip, Is.Not.Null, "프리즘 칩");
                stage.Tips.Show(prismChip);
                var prismTip = stage.Tips.Element.Query<Label>().ToList().ConvertAll(l => l.text.Replace("⁠", ""));
                Assert.That(prismTip, Does.Contain(stage.T("screen.board.ub.prism.title")), "툴팁 머리 = 프리즘 아머");
                Assert.That(prismTip.Exists(t => t.Contains(stage.T("screen.board.ub.prism.tip.effect").Replace("⁠", ""))), Is.True, "툴팁 본문에 효과 문구: " + string.Join(" | ", prismTip));
                stage.Tips.Hide();

                // ── 위 띠 축복 칩(S59 진입점 — 결정 84 M4 가): 누르면 축복 판 · Esc로 닫힘 ──
                var blessChip = stage.Root.Q(BoardUb.Bless);
                Assert.That(blessChip, Is.Not.Null, "축복 칩");
                using (var click = ClickEvent.GetPooled())
                {
                    click.target = blessChip;
                    blessChip.SendEvent(click);
                }
                yield return null;
                Assert.That(flow.Top is BlessScreen panel && panel.IsPanel, Is.True, "축복 칩을 누르면 축복 판이 열린다");
                flow.Escape();
                yield return null;
                Assert.That(flow.Top, Is.Null, "Esc로 축복 판이 닫힌다");

                // ── [턴 종료] 둘: 라운드 끝마다 UB 턴(기록 줄) ──
                bool asked = false;
                for (int t = 0; t < 2; t++)
                {
                    Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True);
                    yield return null;
                    for (int i = 0; i < 200 && !(flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null); i++)
                    {
                        // 라운드 끝 「UB 턴을 건너뛰겠습니까?」(결정 84 U2 — 축복 1이 남았다): 저절로 열린 축복 물음 창(S59 — M6 「UB 화면 묶음 2」)의 [건너뛰지 않음]. 응답 프롬프트로는 오지 않는다
                        Assert.That(flow.Hud.Prompting, Is.False, "건너뛰기 물음은 응답 프롬프트가 아니다");
                        if (flow.BoardOpen && flow.Top is BlessScreen ask && !ask.IsPanel) { asked = true; Assert.That(ask.PressAsk(false), Is.True, "건너뛰지 않음"); }
                        flow.Skip(500);
                        yield return null;
                    }
                    Assert.That(flow.InputOpen, Is.True, "UB 턴 뒤 내 턴이 온다");
                }
                Assert.That(asked, Is.True, "라운드 끝에 건너뛰기 물음 창이 열렸다");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택이 없다");
                var keys = new HashSet<string>();
                var shownLines = new List<BoardLogLine>();
                flow.Log.Shown(flow.Fx.Sequencer, shownLines);
                foreach (var line in shownLines) keys.Add(line.Line.Key);
                Assert.That(keys, Does.Contain(BoardLog.K + "ub.turn"));
                Assert.That(keys, Does.Contain(BoardLog.K + "ub.move"));
                Assert.That(Standing(flow, board), Is.EqualTo(UbState.Beasts + 1), "UB 턴 뒤에도 피규어는 토큰 칸에");
                flow.OpenLog();
                yield return null;
                Assert.That(stage.Root.Query<Label>().ToList().Exists(l => l.text.Contains("이동했다") && !l.text.Contains("(울트라 비스트)")), "기록 창에 UB 이동 줄(꼬리 없는 이름)");

                // ── 저장 → 타이틀 → 이어 받기: 같은 보드 ──
                var before = flow.Session.HostCopy();
                ulong hash = flow.Session.Hash;
                flow.OpenTitle();
                yield return null;
                Assert.That(board.DistortionWorldVisible, Is.False);
                Assert.That(((TitleScreen)stage.Current).SaveLine, Does.StartWith("울트라 버스트"), "이어하기 줄의 요약");
                Assert.That(flow.Resume(GameFlow.SoloSlot), Is.True, flow.ResumeError);
                yield return null;
                yield return Until(() => flow.Hud != null, 5f);
                Assert.That(flow.Session.Hash, Is.EqualTo(hash), "이어 받은 판 = 끊은 판");
                Assert.That(board.DistortionWorldVisible, Is.True);
                Assert.That(Standing(flow, board), Is.EqualTo(UbState.Beasts + 1));
                var after = flow.Client.View;
                for (int i = 0; i < before.Tokens.Count; i++)
                {
                    Assert.That(after.Tokens[i].Cell, Is.EqualTo(before.Tokens[i].Cell), "토큰 " + i);
                    Assert.That(after.Tokens[i].Destroyed, Is.EqualTo(before.Tokens[i].Destroyed), "파괴 " + i);
                }
                Assert.That(flow.Settings.Campaign, Is.EqualTo(Campaign.UltraBurst), "이어 받은 판의 설정");

                // ── 네크로즈마 페이즈 폼: 2페이즈 문(새벽의 날개) → 3페이즈(울트라) — 판의 1페이즈 피규어 자리에 그 폼이 선다 ──
                Assert.That(flow.NecrozmaShown(), Is.EqualTo(UbNecrozma.Phase1));
                flow.Rebase(st => { st.Ub.NecrozmaPhase = 2; st.Ub.SunMoon = UbNecrozma.Moon; });
                yield return null;
                Assert.That(flow.NecrozmaShown(), Is.EqualTo(UbNecrozma.DawnWings));
                flow.Rebase(st => st.Ub.NecrozmaPhase = 3);
                yield return null;
                Assert.That(flow.NecrozmaShown(), Is.EqualTo(UbNecrozma.Ultra));
                Assert.That(board.Figures.Figures.Count, Is.EqualTo(33), "판에 서는 피규어 수는 그대로(폼은 따로)");
                flow.OpenTitle();
                yield return null;
                foreach (var f in board.Figures.Forms) Assert.That(f.Root.activeSelf, Is.False, "게임 밖에서는 폼을 숨긴다");

                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
