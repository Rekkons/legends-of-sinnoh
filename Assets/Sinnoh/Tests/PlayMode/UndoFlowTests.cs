using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 무르기 · 되돌리기 화면(결정 149 · 154 — Docs/Design/undo.md §3 · §5, 조각 D1): 혼자 하기 설정의 「무르기」 줄 · 보드 HUD [무르기] · Ctrl+Z 길 · 게임 메뉴 「무르기」 · 「되돌리기」 쪽 · Esc.
    /// 앞의 둘은 화면이 받은 제안을 그대로 읽는지(가짜 제안 <see cref="GameFlow.FakeUndo"/> — 줄 수 · 쪽 넘김 같은 겉모양), 나머지는 규칙 핵(조각 A)이 보낸 실제 제안으로 실제로 무른다:
    /// 보드가 다시 서고 기록 줄 · 저장도 되돌린 판 · 운이 드러나면 흐린 단추 · 지난 턴으로 · 장치의 Ctrl+Z · Esc · 배틀 중 메뉴로 무르면 배틀 앞 보드 · 같은 행동을 다시 하면 같은 결과.
    /// </summary>
    public sealed class UndoFlowTests
    {
        readonly List<string> errors = new List<string>();
        RenderTexture rt;

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
        }

        [TearDown]
        public void Shut()
        {
            Application.logMessageReceived -= Grab;
            var b = Object.FindAnyObjectByType<ScreenBoot>();
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null) rt.Release();
            rt = null;
        }

        IEnumerator Boot(System.Action<ScreenBoot> got)
        {
            errors.Clear();
            Application.logMessageReceived += Grab;
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            ScreenBoot boot = null;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null);
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            got(boot);
        }

        // 혼자 판을 보드까지(셋업은 화면의 함수로 — 고정 시드, 원칙 AI)
        static IEnumerator ToBoard(GameFlow flow, UndoMode undo)
        {
            flow.StartSolo(new SoloSettings { Undo = undo }, 11, 99);
            yield return null;
            var village = (SetupVillageScreen)flow.Stage.Current;
            village.Pick(0);
            Assert.That(village.Confirm(), Is.True);
            flow.Skip(flow.QueueLeftMs);
            yield return null;
            var pick = (SetupPickScreen)flow.Stage.Current;
            pick.PickStarter(0);
            pick.PickCard(0);
            Assert.That(pick.Confirm(), Is.True);
            flow.Skip(flow.OpeningMs);
            yield return Open(flow);
        }

        // 보드 입력이 열릴 때까지(연출 · 클릭 막기)
        static IEnumerator Open(GameFlow flow)
        {
            for (int i = 0; i < 200 && !(flow.InputOpen && flow.Hud != null); i++)
            {
                flow.Skip(250);
                yield return null;
            }
            Assert.That(flow.InputOpen && flow.Hud != null, Is.True, "보드 입력이 열리지 않았다");
        }

        // 걸어도 운이 드러나지 않는 칸: 길 가운데 · 도착 칸에 뒷면 숨겨진 장소가 없다(엔진이 멈춰 묻지 않는다)
        static int CleanCell(GameFlow flow, int not = -1, bool mustFind = true)
        {
            var me = flow.Client.View.Seats[flow.Client.Seat];
            var host = flow.Session.HostCopy();
            var view = flow.Client.View;
            var steps = MoveRules.Reachable(flow.Ctx.Board, me.Cell, me.MovesLeft, view.Options.ElevationMode);
            foreach (int c in flow.Hud.Reach.OrderBy(c => steps[c]).ThenBy(c => c))   // 가까운 칸부터(이동력을 남겨 한 번 더 걷는다)
            {
                if (c == me.Cell || c == not || flow.Hud.StopsBefore(c).Count > 0) continue;
                if (host.Tokens.Any(t => t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell == c)) continue;
                return c;
            }
            if (mustFind) Assert.Fail("걸을 칸이 없다(갈 수 있는 칸 " + flow.Hud.Reach.Count + ")");
            return -1;
        }

        // 길에 다른 숨겨진 장소가 없는 뒷면 숨겨진 장소로 걸어가 [공개] → 보드 입력이 다시 열릴 때까지. 그 라운드에 없으면 턴을 넘긴다(최대 3라운드).
        // 고르는 순서는 MouseProbe board-spot-reveal · UiShots.UndoReveal과 같다(규토리 더미 > 유물 > 떨어진 별 > 야영지 — 우두머리는 배틀이 열려 뺀다)
        static IEnumerator Reveal(GameFlow flow)
        {
            for (int r = 0; r < 3; r++)
            {
                int cell = -1, rank = -1;
                var host = flow.Session.HostCopy();
                foreach (int c in flow.Hud.Reach)
                {
                    if (flow.Hud.StopsBefore(c).Count > 0) continue;
                    foreach (var t in host.Tokens)
                    {
                        if (t.Kind != TokenKind.HiddenSpot || t.FaceUp || t.Destroyed || t.Cell != c) continue;
                        var item = flow.Ctx.Items.Items[t.Def];
                        int k = HiddenSpotRules.Stash(flow.Ctx, t.Def, out _, out _) ? 3 : item.Kind == ItemKind.Artifact ? 2 : item.Key == HiddenSpotRules.FallenStar ? 1 : item.Key == HiddenSpotRules.Campsite ? 0 : -1;
                        if (k > rank) { rank = k; cell = c; }
                    }
                }
                if (cell >= 0)
                {
                    Assert.That(flow.Hud.Walk(cell), Is.True, "숨겨진 장소 칸 " + cell + "으로 걷기");
                    yield return null;
                    for (int i = 0; i < 200 && !(flow.InputOpen && flow.Client.Choice(ChoiceKind.HiddenSpot) != null); i++)
                    {
                        flow.Skip(250);
                        yield return null;
                    }
                    Assert.That(flow.Client.Choice(ChoiceKind.HiddenSpot), Is.Not.Null, "칸 " + cell + "에서 공개를 묻는다");
                    Assert.That(flow.Hud.Press(BoardHud.Reveal), Is.True, "[공개]");
                    yield return null;
                    for (int i = 0; i < 200 && !(flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null); i++)
                    {
                        flow.Skip(250);
                        yield return null;
                    }
                    Assert.That(flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null, Is.True, "공개 뒤 보드 입력");
                    yield break;
                }
                int round = flow.Client.View.Round;
                Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "턴 종료");
                yield return null;
                for (int i = 0; i < 200 && !(flow.InputOpen && flow.Client.View.Round == round + 1); i++)
                {
                    flow.Skip(250);
                    yield return null;
                }
            }
            Assert.Fail("3라운드 동안 공개할 뒷면 숨겨진 장소가 갈 수 있는 칸에 없다");
        }

        static IEnumerator WalkTo(GameFlow flow, int cell)
        {
            Assert.That(flow.Hud.Walk(cell), Is.True, "칸 " + cell + "으로 걷기");
            yield return null;
            yield return Open(flow);
            Assert.That(flow.Client.View.Seats[flow.Client.Seat].Cell, Is.EqualTo(cell), "걸은 칸");
        }

        static int PieceCell(BoardBoot board) => board.Pieces.Pieces[0].Spot != null ? board.Pieces.Pieces[0].Spot.Cell : -1;

        // 떠 있는 툴팁의 본문(머리 · 본문 Label 가운데 마지막)
        static string TipOf(UiStage stage, VisualElement target)
        {
            stage.Tips.Show(target);
            if (stage.Tips.Target != target) return null;
            var labels = stage.Tips.Element.Query<Label>().ToList();
            return labels.Count > 0 ? labels[labels.Count - 1].text.Replace("⁠", "") : null;
        }

        static Sinnoh.Fx.BoardLogLine LastLine(GameFlow flow) => flow.Log.Lines.Count > 0 ? flow.Log.Lines[flow.Log.Lines.Count - 1] : null;

        [UnityTest]
        public IEnumerator M10_무르기_솔로_혼자하기설정의무르기줄()
        {
            ScreenBoot boot = null;
            yield return Boot(b => boot = b);
            var flow = boot.Flow;
            var stage = boot.Root.Stage;
            // 사람이 여는 새 판의 처음 값 = 내 턴만(결정 154 ①)
            Assert.That(flow.Settings.Undo, Is.EqualTo(SoloGame.NewGameUndo));
            Assert.That(SoloGame.NewGameUndo, Is.EqualTo(UndoMode.Turn));
            flow.OpenSolo();
            yield return null;
            var solo = (SoloScreen)stage.Current;
            var root = stage.Root;
            Assert.That(root.Q("solo-row-" + SoloScreen.Undo), Is.Not.Null, "「무르기」 줄");
            Assert.That(root.Q("solo-opt-" + SoloScreen.Undo + "-turn").ClassListContains("sn-opt-picked"), Is.True, "「내 턴만」이 골라져 있다");
            foreach (string v in new[] { "off", "turn", "free" })
            {
                Assert.That(solo.Set(SoloScreen.Undo, v), Is.True, v);
                Assert.That(root.Q("solo-opt-" + SoloScreen.Undo + "-" + v).ClassListContains("sn-opt-picked"), Is.True, v + " 골라짐");
            }
            Assert.That(solo.Settings.Undo, Is.EqualTo(UndoMode.Free));
            // 고른 값이 판의 세션 설정으로 간다(머리 SaveHead.Undo — 규칙 해시 밖)
            solo.Start();
            yield return null;
            Assert.That(flow.Session.UndoMode, Is.EqualTo(UndoMode.Free));
            Assert.That(flow.Session.Head.Undo, Is.EqualTo(UndoMode.Free));
            Assert.That(flow.Settings.Undo, Is.EqualTo(UndoMode.Free), "다음 판의 설정 화면은 지난 값으로 연다");
            flow.OpenTitle();
            yield return null;
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_무르기_솔로_단추와메뉴는받은제안을그대로읽고_되돌리기쪽은Esc로한단계씩()
        {
            ScreenBoot boot = null;
            yield return Boot(b => boot = b);
            var flow = boot.Flow;
            var stage = boot.Root.Stage;
            var pilot = FlowPilot.Start(flow, SoloTrail.Defeat);
            yield return null;
            flow.Skip(flow.OpeningMs);
            for (int i = 0; i < 200 && !(flow.InputOpen && flow.Hud != null); i++)
            {
                flow.Skip(250);
                yield return null;
            }
            Assert.That(flow.InputOpen, Is.True, "보드 입력이 열리지 않았다");
            var root = stage.Root;
            int commands = flow.Session.CommandCount;

            // 무르기를 끈 판: 단추 · 메뉴 줄이 아예 없다
            Assert.That(flow.Session.UndoMode, Is.EqualTo(UndoMode.Off));
            Assert.That(flow.UndoShown, Is.False);
            Assert.That(root.Q(BoardHud.UndoButton), Is.Null);

            // 못 무름(운이 드러남): 흐린 단추 + 까닭 툴팁, 누르기 · Ctrl+Z는 아무 일도 없다
            flow.FakeUndo(new UndoOffer { Mode = UndoMode.Turn, StepTo = -1, Block = UndoBlock.Luck });
            yield return null;
            var undo = root.Q<Button>(BoardHud.UndoButton);
            Assert.That(undo, Is.Not.Null, "[무르기]");
            Assert.That(undo.ClassListContains(UiStage.OffClass), Is.True, "흐린 단추");
            Assert.That(undo.parent.IndexOf(root.Q(BoardHud.StampButton)), Is.EqualTo(undo.parent.IndexOf(undo) + 1), "[스탬프] 바로 왼쪽");
            Assert.That(flow.UndoBlockText, Is.EqualTo(stage.T("screen.board.undo.block.luck")));
            Assert.That(flow.UndoOpen, Is.False);
            Assert.That(flow.PressUndo(), Is.False);
            Assert.That(flow.UndoKey(), Is.False);

            // 무를 수 있음: 켜진 단추(가짜 목표는 세션이 받지 않는다 — 판은 그대로)
            flow.FakeUndo(new UndoOffer { Mode = UndoMode.Turn, StepTo = 0 });
            yield return null;
            undo = root.Q<Button>(BoardHud.UndoButton);
            Assert.That(undo.ClassListContains(UiStage.OffClass), Is.False);
            Assert.That(undo.enabledSelf, Is.True);
            Assert.That(flow.UndoOpen, Is.True);
            flow.PressUndo();
            yield return null;
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "세션이 받지 않은 무르기는 판을 바꾸지 않는다");
            // 내 턴만: 메뉴에 「무르기」만(「되돌리기」는 자유 + 목록을 열 수 있을 때)
            flow.OpenMenu();
            yield return null;
            Assert.That(root.Q("board-menu-undo"), Is.Not.Null);
            Assert.That(root.Q("board-menu-rewind"), Is.Null);
            flow.Escape();
            yield return null;
            Assert.That(flow.MenuUp, Is.False);

            // 자유 + 목록: 「되돌리기」 쪽 — 10줄씩, 지금 턴 꼬리표 · 얼굴 상자, 확인 물음 → Esc 한 단계씩
            var points = new List<UndoPoint>();
            for (int i = 0; i < 13; i++) points.Add(new UndoPoint { To = 90 - i, Round = 13 - i, Seat = 0, Current = i == 0, Deeds = { new UndoDeed { Kind = UndoDeedKind.Moved } } });
            flow.FakeUndo(new UndoOffer { Mode = UndoMode.Free, StepTo = 0, HostList = true }, points);
            flow.OpenMenu();
            yield return null;
            var menu = (BoardMenuScreen)stage.Current;
            Assert.That(root.Q("board-menu-rewind"), Is.Not.Null);
            Assert.That(menu.OpenRewind(), Is.True);
            yield return null;
            Assert.That(menu.Page, Is.EqualTo(BoardMenuScreen.RewindPage));
            Assert.That(root.Query(className: "sn-menu-rewind-row").ToList().Count, Is.EqualTo(BoardMenuScreen.RewindRows));
            Assert.That(root.Q("board-menu-rewind-row-0-now"), Is.Not.Null, "지금 턴 꼬리표");
            Assert.That(root.Q("board-menu-rewind-row-1-now"), Is.Null);
            Assert.That(FaceBox.Shown(root.Q("board-menu-rewind-row-0-face")), Is.EqualTo(flow.Protagonist(0)));
            Assert.That(menu.TurnRewindPage(1), Is.True);
            yield return null;
            Assert.That(root.Query(className: "sn-menu-rewind-row").ToList().Count, Is.EqualTo(3), "둘째 쪽");
            Assert.That(menu.TurnRewindPage(1), Is.False, "마지막 쪽");
            Assert.That(menu.AskRewind(12), Is.True);
            yield return null;
            Assert.That(root.Q("board-menu-rewind-ask"), Is.Not.Null);
            flow.Escape();   // 물음 → 목록
            yield return null;
            Assert.That(menu.RewindAsking, Is.Null);
            Assert.That(menu.Page, Is.EqualTo(BoardMenuScreen.RewindPage));
            flow.Escape();   // 목록 → 메뉴
            yield return null;
            Assert.That(menu.Page, Is.EqualTo(BoardMenuScreen.MainPage));
            Assert.That(root.Q("board-menu-undo"), Is.Not.Null);
            flow.Escape();   // 메뉴 → 보드
            yield return null;
            Assert.That(flow.MenuUp, Is.False);
            // 확정은 메뉴를 닫고 세션에 보낸다(가짜 목표는 받지 않는다 — 판은 그대로)
            flow.OpenMenu();
            yield return null;
            menu = (BoardMenuScreen)stage.Current;
            Assert.That(menu.OpenRewind(), Is.True);
            Assert.That(menu.AskRewind(0), Is.True);
            menu.ConfirmRewind();
            yield return null;
            Assert.That(flow.MenuUp, Is.False, "확정하면 메뉴가 닫힌다");
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands));

            // 받은 제안이 없어지면 단추도 없다
            flow.FakeUndo(null);
            yield return null;
            Assert.That(root.Q(BoardHud.UndoButton), Is.Null);
            // 못 무르는 까닭 · 한 일의 글은 모두 표에 있다(화면이 키를 손으로 만들지 않는다 — UndoRules.BlockKey · DeedKey)
            foreach (UndoBlock b in System.Enum.GetValues(typeof(UndoBlock)))
                if (b != UndoBlock.None) Assert.That(stage.TOrNull(UndoRules.BlockKey(b)), Is.Not.Null, b.ToString());
            foreach (UndoDeedKind k in System.Enum.GetValues(typeof(UndoDeedKind))) Assert.That(stage.TOrNull(UndoRules.DeedKey(k)), Is.Not.Null, k.ToString());
            flow.OpenTitle();
            yield return null;
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_무르기_솔로_단추로한수무르면보드가다시서고기록줄()
        {
            ScreenBoot boot = null;
            yield return Boot(b => boot = b);
            string saves = Path.Combine(Application.temporaryCachePath, "sinnoh-undo-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                boot.UseSaves(saves, () => 1795000000000L);   // 사용자의 저장 폴더가 아니라 임시 폴더(RULE-SAVE)
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var root = stage.Root;
                var board = Object.FindAnyObjectByType<BoardBoot>();
                yield return ToBoard(flow, UndoMode.Turn);
                string id = flow.SaveId;
                Assert.That(id, Is.Not.Null, "저장하는 판");

                // 턴 처음: 흐린 단추 + 까닭 「이번 턴의 처음입니다」
                Assert.That(flow.Undo, Is.Not.Null, "무르기를 켠 판은 제안이 온다");
                Assert.That((flow.Undo.StepTo, flow.Undo.Block), Is.EqualTo((-1, UndoBlock.TurnStart)));
                var undo = root.Q<Button>(BoardHud.UndoButton);
                Assert.That(undo, Is.Not.Null);
                Assert.That(undo.ClassListContains(UiStage.OffClass), Is.True, "턴 처음에는 흐린 단추");
                Assert.That(TipOf(stage, undo), Is.EqualTo(stage.T("screen.board.undo.block.turn_start").Replace("⁠", "")));
                stage.Tips.Hide();

                // 한 칸 걷고 → [무르기] → 말이 제자리 · 판 · 저장이 걷기 전
                int start = flow.Client.View.Seats[0].Cell, before = flow.Session.CommandCount;
                ulong startHash = flow.Session.Hash;
                int cell = CleanCell(flow);
                yield return WalkTo(flow, cell);
                ulong walkedHash = flow.Session.Hash;
                int walked = flow.Session.CommandCount;
                Assert.That(flow.Undo.StepTo, Is.EqualTo(before), "한 수 = 걷기 전 쉬는 때");
                undo = root.Q<Button>(BoardHud.UndoButton);
                Assert.That(undo.ClassListContains(UiStage.OffClass) || !undo.enabledSelf, Is.False, "켜진 단추");
                var oldHud = flow.Hud;
                Assert.That(flow.UndoOpen, Is.True);
                Assert.That(flow.PressUndo(), Is.True, "[무르기](단추가 부르는 함수 — 창 없는 에디터는 패널에 포인터를 넣지 못한다, 마우스 길은 MouseProbe undo-press)");
                yield return null;
                yield return Open(flow);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(before), "판이 걷기 전으로");
                Assert.That(flow.Session.Hash, Is.EqualTo(startHash), "걷기 전과 같은 상태(Rng까지)");
                Assert.That(flow.Session.Undos, Is.EqualTo(1));
                Assert.That(flow.Client.View.Seats[0].Cell, Is.EqualTo(start), "보기의 말 칸");
                Assert.That(PieceCell(board), Is.EqualTo(start), "보드의 말이 제자리에 다시 섰다");
                Assert.That(flow.Hud, Is.Not.Null.And.Not.SameAs(oldHud), "HUD를 새로 세웠다");
                Assert.That(flow.Hud.Reach, Does.Contain(cell), "다시 갈 수 있는 칸");
                Assert.That(flow.Fx.Sequencer.Queued.Count(), Is.EqualTo(0), "되돌린 뒤 남은 연출이 없다");
                Assert.That((flow.Undo.StepTo, flow.Undo.Block), Is.EqualTo((-1, UndoBlock.TurnStart)), "다시 턴 처음");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));
                // 기록: 걷기 줄은 빠지고 「○○은(는) 무르기를 했다」
                var last = LastLine(flow);
                Assert.That(last.Line.Key, Is.EqualTo(Sinnoh.Fx.BoardLog.K + "undo"));
                Assert.That(last.Seat, Is.EqualTo(0));
                Assert.That(flow.Log.Lines.Any(l => l.Seq != Sinnoh.Fx.BoardLog.ResumedSeq && l.Seq >= flow.Session.NextSeq), Is.False, "지운 미래의 줄이 없다");
                flow.OpenLog();
                yield return null;
                var logTexts = root.Query<Label>().ToList().Select(l => l.text.Replace("⁠", "")).ToList();
                Assert.That(logTexts.Any(t => t.Contains("무르기를 했다")), Is.True, "기록 창에 무르기 줄: " + string.Join(" / ", logTexts));
                flow.Escape();
                yield return null;
                yield return Open(flow);

                // 같은 행동을 다시 하면 같은 결과
                yield return WalkTo(flow, cell);
                Assert.That((flow.Session.CommandCount, flow.Session.Hash), Is.EqualTo((walked, walkedHash)), "같은 걷기 = 같은 상태");

                // 앞 명령의 로그 쓰기가 실패해 「저장 실패」가 남은 채(파일이 잠겼다 — 그 답은 거절) 무르면, 무르기가 쓴 저장이 되돌린 판과 맞으니 「실패」를 지운다
                string dir = Path.Combine(saves, id);
                using (new FileStream(Path.Combine(dir, Sinnoh.Data.SaveStore.LogFile), FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var map = flow.Client.Choice(ChoiceKind.MapAction);
                    var end = flow.Client.Legal(map).Find(a => a.Kind == AnswerKind.EndTurn);
                    Assert.That(flow.Answer(map, end).Accepted, Is.False, "쓰지 못한 명령은 받아들이지 않는다");
                }
                Assert.That(flow.SaveError, Is.Not.Null);
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Failed));
                Assert.That((flow.Session.CommandCount, flow.Session.Hash), Is.EqualTo((walked, walkedHash)), "판은 그대로");

                // 저장도 되돌린 판이다: 한 번 더 무르고 타이틀로 나가 이어 받으면 걷기 전 자리
                Assert.That(flow.PressUndo(), Is.True);
                yield return Open(flow);
                Assert.That(flow.SaveError, Is.Null.Or.Empty, "저장 실패 없음(앞 명령의 실패도 지웠다)");
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Saved));
                flow.OpenTitle();
                yield return null;
                Assert.That(flow.Resume(id), Is.True, flow.ResumeError);
                yield return null;
                yield return Open(flow);
                Assert.That((flow.Session.CommandCount, flow.Session.Hash), Is.EqualTo((before, startHash)), "이어 받은 판 = 되돌린 판");
                Assert.That(flow.Session.UndoMode, Is.EqualTo(UndoMode.Turn), "무르기 설정도 머리에서");
                Assert.That(PieceCell(board), Is.EqualTo(start));
                flow.OpenTitle();
                yield return null;
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                if (Directory.Exists(saves)) Directory.Delete(saves, true);
            }
        }

        [UnityTest]
        public IEnumerator M10_무르기_솔로_운이드러나면단추가꺼지고까닭툴팁()
        {
            ScreenBoot boot = null;
            yield return Boot(b => boot = b);
            var flow = boot.Flow;
            var stage = boot.Root.Stage;
            var root = stage.Root;
            yield return ToBoard(flow, UndoMode.Turn);
            // 뒷면 숨겨진 장소로 걸어가 [공개](뒷면 토큰이 앞면이 된다 — undo.md §1-2 ③). 찾아보기 · 야생 배틀은 혼자 판에서 턴을 끝내므로(돌아오면 다음 턴 처음) 여기 쓰지 않는다
            yield return Reveal(flow);
            Assert.That((flow.Undo.StepTo, flow.Undo.Block), Is.EqualTo((-1, UndoBlock.Luck)), "숨겨진 장소 공개를 넘어 무르지 못한다");
            var undo = root.Q<Button>(BoardHud.UndoButton);
            Assert.That(undo, Is.Not.Null);
            Assert.That(undo.ClassListContains(UiStage.OffClass), Is.True, "흐린 단추");
            Assert.That(TipOf(stage, undo), Is.EqualTo(stage.T("screen.board.undo.block.luck").Replace("⁠", "")), "까닭 툴팁");
            stage.Tips.Hide();
            int commands = flow.Session.CommandCount;
            Assert.That(flow.PressUndo(), Is.False);
            Assert.That(flow.UndoKey(), Is.False);
            flow.OpenMenu();
            yield return null;
            var menuUndo = root.Q<Button>("board-menu-undo");
            Assert.That(menuUndo.ClassListContains(UiStage.OffClass), Is.True, "메뉴 줄도 흐림");
            Assert.That(((BoardMenuScreen)stage.Current).PressUndo(), Is.False);
            flow.CloseMenu();
            yield return null;
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "못 무르는 누르기는 판을 바꾸지 않는다");
            Assert.That(flow.Session.Undos, Is.EqualTo(0));
            // 그 뒤의 걷기는 무를 수 있다(운이 드러난 줄 뒤의 쉬는 때까지만) — 이동력이 남았으면
            int cell = CleanCell(flow, mustFind: false);
            if (cell >= 0)
            {
                int before = flow.Session.CommandCount;
                yield return WalkTo(flow, cell);
                Assert.That(flow.Undo.StepTo, Is.EqualTo(before));
                Assert.That(flow.PressUndo(), Is.True);
                yield return Open(flow);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(before));
                Assert.That(flow.Undo.Block, Is.EqualTo(UndoBlock.Luck), "다시 공개 뒤 — 더는 못 무른다");
            }
            flow.OpenTitle();
            yield return null;
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_무르기_솔로_되돌리기쪽에서지난턴으로()
        {
            ScreenBoot boot = null;
            yield return Boot(b => boot = b);
            var flow = boot.Flow;
            var stage = boot.Root.Stage;
            var root = stage.Root;
            var board = Object.FindAnyObjectByType<BoardBoot>();
            yield return ToBoard(flow, UndoMode.Free);
            // 1라운드: 한 칸 걷고 턴 종료 → 2라운드 → 턴 종료 → 3라운드
            int first = flow.Session.CommandCount, firstCell = flow.Client.View.Seats[0].Cell;
            ulong firstHash = flow.Session.Hash;
            yield return WalkTo(flow, CleanCell(flow));
            for (int t = 0; t < 2; t++)
            {
                int round = flow.Client.View.Round;
                Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "턴 종료 " + t);
                yield return null;
                for (int i = 0; i < 200 && !(flow.InputOpen && flow.Client.View.Round == round + 1); i++)
                {
                    flow.Skip(250);
                    yield return null;
                }
                Assert.That(flow.Client.View.Round, Is.EqualTo(round + 1));
            }
            yield return Open(flow);
            // 3라운드에도 한 수(지금 턴 처음 = 지금 상태면 목록에 넣지 않는다 — undo.md §1-4)
            yield return WalkTo(flow, CleanCell(flow));
            Assert.That(flow.RewindShown, Is.True, "자유 = 혼자 하기 플레이어가 목록을 연다");

            flow.OpenMenu();
            yield return null;
            var menu = (BoardMenuScreen)stage.Current;
            Assert.That(root.Q("board-menu-rewind"), Is.Not.Null);
            Assert.That(menu.OpenRewind(), Is.True);
            yield return null;
            var points = menu.RewindPoints;
            Assert.That(points.Select(p => p.Round).ToArray(), Is.EqualTo(new[] { 3, 2, 1 }), "새것부터 턴 시작마다 한 줄");
            Assert.That(points[0].Current, Is.True);
            Assert.That(root.Q("board-menu-rewind-row-0-now"), Is.Not.Null, "지금 턴 꼬리표");
            Assert.That(points[2].To, Is.EqualTo(first), "1라운드 줄 = 첫 턴의 첫 쉬는 때");
            Assert.That(points[2].Deeds.Any(d => d.Kind == UndoDeedKind.Moved), Is.True, "1라운드에 한 일 = 이동");
            Assert.That(root.Q<Label>("board-menu-rewind-row-2-deeds").text, Does.Contain(stage.T(UndoRules.DeedKey(UndoDeedKind.Moved))));
            Assert.That(FaceBox.Shown(root.Q("board-menu-rewind-row-2-face")), Is.EqualTo(flow.Protagonist(0)));
            // [되돌리기] → 확인 물음 → [되돌리기]
            Assert.That(root.Q<Button>("board-menu-rewind-row-2-go"), Is.Not.Null);
            Assert.That(menu.AskRewind(2), Is.True);   // 그 줄의 [되돌리기]가 부르는 함수(마우스 길은 MouseProbe undo-rewind)
            yield return null;
            Assert.That(menu.RewindAsking, Is.SameAs(points[2]));
            var ask = root.Q("board-menu-rewind-ask");
            Assert.That(ask, Is.Not.Null);
            Assert.That(ask.Query<Label>().ToList()[0].text.Replace("⁠", ""), Does.Contain("1라운드"));
            Assert.That(flow.Session.CommandCount, Is.Not.EqualTo(first), "묻는 동안은 그대로");
            Assert.That(root.Q<Button>("board-menu-rewind-yes"), Is.Not.Null);
            Assert.That(menu.ConfirmRewind(), Is.True);
            yield return null;
            yield return Open(flow);
            Assert.That(flow.MenuUp, Is.False, "확정하면 메뉴가 닫힌다");
            Assert.That((flow.Session.CommandCount, flow.Session.Hash), Is.EqualTo((first, firstHash)), "1라운드 턴 처음 상태 그대로");
            Assert.That(flow.Client.View.Round, Is.EqualTo(1));

            Assert.That(PieceCell(board), Is.EqualTo(firstCell), "말이 1라운드 자리");
            Assert.That(flow.Session.Epoch, Is.EqualTo(1));
            Assert.That((flow.Undo.StepTo, flow.Undo.Block), Is.EqualTo((-1, UndoBlock.TurnStart)));
            var last = LastLine(flow);
            Assert.That(last.Line.Key, Is.EqualTo(Sinnoh.Fx.BoardLog.K + "rewind"), "기록 줄 「○○은(는) n라운드로 되돌렸다」");
            Assert.That(flow.UndoPoints().Count, Is.EqualTo(0), "지금 턴 처음이라 되돌릴 곳이 없다");
            flow.OpenTitle();
            yield return null;
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_무르기_솔로_장치의CtrlZ와Esc()
        {
            ScreenBoot boot = null;
            yield return Boot(b => boot = b);
            VirtualInput input = null;
            var settings = InputSystem.settings;
            var backgroundWas = settings.backgroundBehavior;
#if UNITY_EDITOR
            var editorWas = settings.editorInputBehaviorInPlayMode;
#endif
            try
            {
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var root = stage.Root;
                yield return ToBoard(flow, UndoMode.Free);
                // 포커스 없는 에디터가 장치 입력을 버리지 않게(이 시험 동안만 — MouseInputTests와 같은 꼴)
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
                settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
                input = new VirtualInput();
                for (int i = 0; i < 3; i++) yield return null;

                IEnumerator Press(params Key[] keys)
                {
                    InputSystem.QueueStateEvent(input.Keys, new KeyboardState(keys));
                    yield return null;
                    yield return null;
                    input.Release();
                    yield return null;
                    yield return null;
                }

                int before = flow.Session.CommandCount;
                yield return Press(Key.LeftCtrl, Key.Z);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(before), "턴 처음의 Ctrl+Z는 아무 일도 없다");
                yield return Press(Key.Z);
                yield return WalkTo(flow, CleanCell(flow));
                int walked = flow.Session.CommandCount;
                yield return Press(Key.Z);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(walked), "Ctrl 없이 Z는 무르지 않는다");
                // 메뉴가 떠 있으면 Ctrl+Z는 듣지 않는다(입력이 닫혔다)
                flow.OpenMenu();
                yield return null;
                yield return Press(Key.LeftCtrl, Key.Z);
                Assert.That((flow.Session.CommandCount, flow.MenuUp), Is.EqualTo((walked, true)), "메뉴 위의 Ctrl+Z");
                yield return Press(Key.Escape);
                Assert.That(flow.MenuUp, Is.False, "Esc = 메뉴 닫기");
                yield return Open(flow);
                yield return Press(Key.LeftCtrl, Key.Z);
                yield return Open(flow);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(before), "Ctrl+Z = 한 수 무르기");
                Assert.That(flow.Session.Undos, Is.EqualTo(1));
                Assert.That(LastLine(flow).Line.Key, Is.EqualTo(Sinnoh.Fx.BoardLog.K + "undo"));

                // 되돌리기 쪽 · 확인 물음은 Esc 키로 한 단계씩(물음 → 쪽 → 메뉴 → 보드), 판은 그대로
                yield return WalkTo(flow, CleanCell(flow));
                walked = flow.Session.CommandCount;
                flow.OpenMenu();
                yield return null;
                var menu = (BoardMenuScreen)stage.Current;
                Assert.That(menu.OpenRewind(), Is.True);
                Assert.That(menu.AskRewind(0), Is.True);
                yield return null;
                Assert.That(root.Q("board-menu-rewind-ask"), Is.Not.Null);
                yield return Press(Key.Escape);
                Assert.That((menu.RewindAsking, menu.Page), Is.EqualTo(((UndoPoint)null, BoardMenuScreen.RewindPage)), "물음 → 쪽");
                yield return Press(Key.Escape);
                Assert.That(menu.Page, Is.EqualTo(BoardMenuScreen.MainPage), "쪽 → 메뉴");
                yield return Press(Key.Escape);
                Assert.That(flow.MenuUp, Is.False, "메뉴 → 보드");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(walked), "Esc로 닫은 물음은 되돌리지 않는다");
                flow.OpenTitle();
                yield return null;
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                settings.backgroundBehavior = backgroundWas;
#if UNITY_EDITOR
                settings.editorInputBehaviorInPlayMode = editorWas;
#endif
                input?.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator M10_무르기_솔로_배틀중메뉴로무르면배틀앞보드_같은행동은같은결과()
        {
            ScreenBoot boot = null;
            yield return Boot(b => boot = b);
            var flow = boot.Flow;
            var stage = boot.Root.Stage;
            var root = stage.Root;
            var pilot = FlowPilot.Start(flow, SoloTrail.Defeat, undo: UndoMode.Free);
            // 길잡이의 답을 화면으로 누르며 낸 답을 명령 수마다 적는다 — 되돌린 뒤 같은 답을 다시 낸다
            var said = new Dictionary<int, ChoiceAnswer>();
            var rests = new Dictionary<int, ulong>();
            bool Drive(System.Func<bool> until, bool replay)
            {
                for (int i = 0; i < 4000; i++)
                {
                    if (flow.OnBoard && flow.InputOpen && flow.Stage.Current == null && flow.Battle == null && flow.Client.Choice(ChoiceKind.MapAction) != null)
                        rests[flow.Session.CommandCount] = flow.Session.Hash;
                    if (until()) return true;
                    bool pressed = false;
                    var battle = flow.Stage.Current as BattleScreen;
                    if (battle != null && battle.ShowsResult && !battle.AskingReward && flow.Client.Choice(ChoiceKind.Reward) == null) pressed = battle.Confirm();
                    else
                    {
                        var choice = flow.Client.FirstChoice();
                        int at = flow.Session.CommandCount;
                        ChoiceAnswer answer = null;
                        if (choice != null) answer = replay ? (said.TryGetValue(at, out var a) ? a : null) : pilot.Decide(flow.Ctx, flow.Client.View, choice, flow.Client.Legal(choice));
                        if (answer != null && FlowPilot.Press(flow, choice, answer))
                        {
                            pressed = true;
                            if (!replay) said[at] = answer;
                        }
                    }
                    if (!pressed) flow.Skip(500);
                }
                return until();
            }

            // ① 찾아보기 → 야생 배틀을 끝내고 보드로(배틀 안에서 본 무르기 목표 = 배틀 앞 보드 — 지금이 쉬는 때가 아니면 그 앞의 마지막 쉬는 때)
            int to = -1;
            bool battled = false;
            Assert.That(Drive(() =>
            {
                if (flow.Battle != null && flow.Stage.Current is BattleScreen && flow.UndoReady) to = to < 0 ? flow.Undo.StepTo : to;
                battled |= flow.Battle != null;
                return battled && flow.Battle == null && flow.Stage.Current == null && flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null;
            }, false), Is.True, "배틀 뒤 보드까지 가지 못했다");
            yield return null;
            int after = flow.Session.CommandCount;
            ulong afterHash = flow.Session.Hash;
            Assert.That(to, Is.GreaterThanOrEqualTo(0), "배틀 중에 무르기 목표가 있었다(자유)");
            Assert.That(rests.ContainsKey(to), Is.True, "목표 " + to + "는 보드가 열렸던 쉬는 때");

            // ② 찾아보기 · 야생 배틀은 혼자 판에서 턴을 끝낸다 — 개인 무르기는 턴 처음을 넘지 못하니(TurnStart) 「되돌리기」로 그 배틀이 있던 턴의 처음으로
            var point = flow.UndoPoints().Where(p => p.To <= to).OrderByDescending(p => p.To).FirstOrDefault();
            Assert.That(point, Is.Not.Null, "배틀이 있던 턴의 시작이 목록에 있다");
            Assert.That(flow.RewindTo(point.To), Is.True);
            yield return Open(flow);
            Assert.That(flow.Session.CommandCount, Is.EqualTo(point.To), "그 턴 처음");
            if (rests.TryGetValue(point.To, out var startHash)) Assert.That(flow.Session.Hash, Is.EqualTo(startHash), "그 턴 처음 상태 그대로");
            Assert.That(flow.Battle, Is.Null);

            // ③ 같은 답으로 다시 배틀에 들어가 → 배틀 중 [게임 메뉴] → 「무르기」 → 배틀 앞 보드
            Assert.That(Drive(() => flow.Battle != null && flow.Stage.Current is BattleScreen && flow.UndoReady, true), Is.True, "다시 배틀에 들어가지 못했다");
            yield return null;
            Assert.That(flow.Undo.StepTo, Is.EqualTo(to), "같은 목표");
            Assert.That(flow.UndoOpen || flow.UndoKey(), Is.False, "배틀 화면 위에서는 보드 HUD 단추 · Ctrl+Z로 무르지 않는다(메뉴로)");
            flow.OpenMenu();
            yield return null;
            Assert.That(flow.MenuUp, Is.True, "배틀 중 게임 메뉴");
            var menuUndo = root.Q<Button>("board-menu-undo");
            Assert.That(menuUndo, Is.Not.Null);
            Assert.That(menuUndo.ClassListContains(UiStage.OffClass), Is.False, "배틀 중에도 메뉴 「무르기」는 켜져 있다(자유)");
            Assert.That(stage.Info, Is.InstanceOf<BoardMenuScreen>(), "배틀 화면 위의 게임 메뉴(정보 창 층 — T35)");
            Assert.That(((BoardMenuScreen)stage.Info).PressUndo(), Is.True, "메뉴 「무르기」 줄이 부르는 함수");
            yield return null;
            yield return Open(flow);
            Assert.That(flow.MenuUp, Is.False);
            Assert.That(stage.Current, Is.Null, "배틀 화면이 걷혔다");
            Assert.That(flow.Battle, Is.Null);
            Assert.That(flow.Client.View.Battle, Is.Null);
            Assert.That((flow.Session.CommandCount, flow.Session.Hash), Is.EqualTo((to, rests[to])), "배틀 앞 보드");
            Assert.That(flow.Client.Choice(ChoiceKind.MapAction), Is.Not.Null, "맵 행동이 다시 열렸다");
            Assert.That(LastLine(flow).Line.Key, Is.EqualTo(Sinnoh.Fx.BoardLog.K + "undo"));

            // ④ 같은 답을 끝까지 다시 내면 같은 결과(조우 카드 · 주사위 · 배틀 결과가 바이트까지 같다 — 재굴림 낚시 불가)
            Assert.That(Drive(() => flow.Session.CommandCount >= after && flow.Battle == null && flow.Stage.Current == null && flow.InputOpen, true), Is.True, "다시 배틀 뒤 보드까지 가지 못했다");
            Assert.That((flow.Session.CommandCount, flow.Session.Hash), Is.EqualTo((after, afterHash)), "같은 행동 = 같은 결과");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            flow.OpenTitle();
            yield return null;
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
