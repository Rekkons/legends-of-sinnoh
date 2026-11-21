using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rekkon.UiKit;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Data;
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
    /// 저장 → 이어 받기(plan §3 M4-3 조건 ①, §4 「저장 기반·자동 저장」)를 화면 흐름(<see cref="GameFlow"/>)으로: 찾아 둔 판을 화면으로 몰다가 야생 배틀 한가운데에서 타이틀로 나가고,
    /// 저장 목록에서 그 판을 이어 받아 끝까지 간 판이 끊지 않은 판(화면 없이 같은 답)과 같은지 본다. 저장 화면(S04)은 없이 흐름의 함수만 쓴다.
    /// 둘째 시험은 저장 화면(04 §9-12)으로: 타이틀 → 혼자 하기 → 셋업 → 턴 몇 번 → 게임 메뉴(저장 상태 줄)로 나가기 → 타이틀 「이어하기」 → 저장 목록(S04) [이어하기] → 같은 상태의 보드, 그리고 목록의 [삭제].
    /// 셋째 시험은 저장의 흠(쓰기 실패 때의 흐름·나가기 안내 · 데이터 해시 · 남이 고친 머리).
    /// 세이브는 임시 폴더에 쓰고 지운다(에디터의 흐름은 기본이 저장 안 함 — <see cref="ScreenBoot.DefaultSaves"/>).
    /// </summary>
    public sealed class SaveResumeFlowTests
    {
        const long Clock = 1794000000000;
        const string Hero = "hgss_001_lyra";

        // 끊지 않은 판: 화면 없이 같은 길잡이로 [도전]이 끝난 뒤 보드에서 행동을 고르는 때까지
        static void Whole(Ctx ctx, SoloTrail trail, out ulong hash, out int count)
        {
            var session = SoloGame.Start(ctx, new SoloSettings(), trail.Seed, SoloTrail.AiSeed, new PrincipleAgent(), 0);
            var client = new SeatClient(session.Link, ctx, SoloGame.Seat);
            var pilot = trail.Pilot();
            bool galactic = false, challenged = false;
            client.Updated += u =>
            {
                pilot.Saw(u.Events);
                foreach (var e in u.Events)
                    if (e is BattleStarted b) galactic = b.Kind == BattleKind.Galactic;
                    else if (e is BattleEnded && galactic) challenged = true;
            };
            long now = 0;
            for (int i = 0; i < 20000; i++)
            {
                var c = client.FirstChoice();
                if (c == null)
                {
                    now += 60000;
                    session.Tick(now);
                    continue;
                }
                if (challenged && c.Kind == ChoiceKind.MapAction) break;
                var legal = client.Legal(c);
                ChoiceAnswer a;
                if (c.Kind == ChoiceKind.VillagePick) a = legal[trail.Village];
                else if (c.Kind == ChoiceKind.StarterPick) a = legal[trail.Starter];
                else if (c.Kind == ChoiceKind.TrainerCardPick) a = legal[0];
                else a = pilot.Decide(ctx, client.View, c, legal);
                Assert.That(client.Answer(c, a, now).Accepted, Is.True, c.Kind + " " + a);
            }
            Assert.That(challenged, Is.True, "끊지 않은 판이 [도전]까지 가지 못했다");
            hash = session.Hash;
            count = session.CommandCount;
        }

        static int Row(TitleScreen title, string id)
        {
            for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == id) return i;
            return -1;
        }

        [UnityTest]
        public IEnumerator M4_타이틀에서혼자하기로턴을넘기다나가이어하기로같은상태에돌아오고목록에서지운다_에러로그0()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-save-test-" + System.Guid.NewGuid().ToString("N"));
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
                var rig = Camera.main.GetComponent<BoardCameraRig>();

                // ── 저장이 없는 타이틀: 「이어하기」는 회색이고 까닭이 설명 창에 적힌다 ──
                var title = (TitleScreen)stage.Current;
                int cont = Row(title, TitleScreen.Continue), solo = Row(title, TitleScreen.Solo);
                Assert.That(title.Ready(title.Shown[cont]), Is.False, "저장이 없으면 꺼져 있다");
                Assert.That(title.Shown[title.Picked].Id, Is.EqualTo(TitleScreen.Solo));
                Assert.That(title.SaveLine, Is.EqualTo("저장한 게임 없음"));
                Assert.That(stage.Root.Q("title-item-" + TitleScreen.Continue).Q(className: "sn-off"), Is.Not.Null, "회색");
                title.Pick(cont);
                Assert.That(stage.Root.Q("title-desc").Query<Label>().ToList()[1].text, Is.EqualTo("저장한 게임이 없습니다."), "못 고르는 까닭");
                Assert.That(title.Activate(cont), Is.False);
                Assert.That(stage.Current, Is.SameAs(title));

                // ── 혼자 하기 → 솔로 설정 → 셋업 → 보드 ──
                Assert.That(title.Activate(solo), Is.True);
                var set = (SoloScreen)stage.Current;
                Assert.That(set.Set(SoloScreen.Protagonist, Hero), Is.True);
                Assert.That(set.Set(SoloScreen.GalacticAi, "basic"), Is.True, "이 판은 원칙 AI로(기본값은 「강함」 — 결정 118 ②)");
                Assert.That(set.Set(SoloScreen.TrainerCards, "on"), Is.True, "트레이너 카드를 쓰는 판(기본값은 끔 — 결정 134)");
                set.Start();
                var village = (SetupVillageScreen)stage.Current;
                village.Pick(0);
                Assert.That(village.Confirm(), Is.True);
                flow.Skip(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(0);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                yield return null;
                string id = flow.SaveId;
                Assert.That(id, Does.StartWith("solo-"), "「혼자 하기」로 시작한 판은 처음부터 저장된다");
                // 스타팅 공개(3행)에서 볼이 열릴 때 고르는 화면이 볼 열림 빛을 찍는다(결정 69)
                bool opened = false;
                for (int i = 0; i < 300 && !opened && stage.Current == pick; i++)
                {
                    flow.Skip(30);
                    yield return null;
                    opened = pick.Sheets != null && pick.Sheets.Shown.Count > 0 && pick.Sheets.Shown[0].StartsWith(SheetBeats.Open + "#");
                }
                Assert.That(opened, Is.True, "스타팅 볼이 열릴 때의 빛(SetupPickScreen.Sheets)");
                flow.Skip(flow.OpeningMs);
                for (int i = 0; i < 200 && !flow.InputOpen; i++)
                {
                    flow.Skip(250);
                    yield return null;
                }
                Assert.That(flow.InputOpen, Is.True, "첫 턴");

                // ── 턴 몇 번 ──
                for (int t = 0; t < 3; t++)
                {
                    Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "턴 종료 " + t);
                    yield return null;
                    for (int i = 0; i < 400 && !(flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null); i++)
                    {
                        flow.Skip(250);
                        yield return null;
                    }
                    Assert.That(flow.InputOpen, Is.True, "다음 턴 " + t);
                }
                int round = flow.Client.View.Round, count = flow.Session.CommandCount, cell = flow.Client.View.Seats[SoloGame.Seat].Cell;
                ulong hash = flow.Session.Hash;
                Assert.That(round, Is.EqualTo(4));
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));

                // ── 게임 메뉴: 저장 상태 줄 → 나가기(저장된 판이라는 안내) ──
                flow.OpenMenu();
                yield return null;
                var menu = (BoardMenuScreen)stage.Current;
                string saved = "자동 저장됨 · " + GameFlow.SaveTime(Clock, date: false);
                Assert.That(menu.SaveText, Is.EqualTo(saved));
                Assert.That(stage.Root.Q<Label>("board-menu-save").text, Is.EqualTo(saved), "저장 상태 줄");
                menu.AskQuit();
                var ask = stage.Root.Q("board-menu-ask").Query<Label>().ToList();
                Assert.That(ask[0].text, Is.EqualTo("타이틀로 나가겠습니까?"));
                Assert.That(ask[1].text.Replace("⁠", ""), Is.EqualTo("진행한 내용은 저장되어 있습니다."));
                menu.Quit();
                yield return null;

                // ── 타이틀: 「이어하기」가 켜지고 골라져 있다 ──
                title = (TitleScreen)stage.Current;
                Assert.That(flow.Session, Is.Null);
                Assert.That(title.Ready(title.Shown[cont]), Is.True, "저장이 있으면 켜진다");
                Assert.That(title.Picked, Is.EqualTo(cont), "저장이 있으면 처음 고른 줄이 「이어하기」");
                Assert.That(title.SaveLine, Is.EqualTo("신오의 심장 · 1막 · 4라운드"), "가장 최근 판의 요약");
                Assert.That(stage.Root.Q<Label>("title-save-line").text, Is.EqualTo(title.SaveLine));

                // 읽을 수 없는 저장을 하나 둔다 → 저장 목록: 읽을 수 있는 칸이 앞, 깨진 칸은 흐리고 [삭제]만
                Directory.CreateDirectory(Path.Combine(root, "broken"));
                File.WriteAllText(Path.Combine(root, "broken", SaveStore.HeadFile), "{ 깨짐");
                Assert.That(title.Activate(cont), Is.True);
                yield return null;
                var list = (SaveListScreen)stage.Current;
                Assert.That(stage.Root.Q("saves"), Is.Not.Null);
                Assert.That(rig.Target, Is.EqualTo(BoardBackdrop.Target(1920, 1080, SaveListScreen.ColumnRight)), "뒤의 보드는 판 오른쪽으로");
                Assert.That(list.Entries.Count, Is.EqualTo(2));
                Assert.That(list.Entries[0].SaveId, Is.EqualTo(id));
                Assert.That(list.Lines[0], Is.EqualTo("신오의 심장 · 1막 · 4라운드"));
                Assert.That(FaceBox.Shown(stage.Root.Q("saves-face-0")), Is.EqualTo(Hero), "저장한 주인공의 얼굴");
                Assert.That(stage.Root.Q("saves-party-0").childCount, Is.EqualTo(1), "파티 도트(스타팅 포켓몬)");
                Assert.That(stage.Root.Q("saves-row-0").Query<Label>().ToList().Exists(l => l.text.EndsWith(GameFlow.SaveTime(Clock))), Is.True, "저장 시각(현지)");
                Assert.That(stage.Root.Q("saves-resume-0"), Is.Not.Null);
                Assert.That(list.Lines[1], Is.EqualTo("읽을 수 없는 저장"));
                Assert.That(stage.Root.Q("saves-row-1").ClassListContains("sn-off"), Is.True, "흐리게");
                Assert.That(stage.Root.Q("saves-row-1").Query<Label>().ToList().Exists(l => l.text == "파일이 손상되었습니다."), Is.True, "까닭");
                Assert.That(stage.Root.Q("saves-resume-1"), Is.Null, "읽을 수 없는 저장에는 [이어하기]가 없다");
                Assert.That(stage.Root.Q("saves-delete-1"), Is.Not.Null);
                Assert.That(list.Resume("broken"), Is.False);
                Assert.That(stage.Current, Is.SameAs(list));

                // [삭제] → 한 번 더 묻는다 → [취소] → 다시 [삭제] → [삭제]
                list.AskDelete("broken");
                Assert.That(list.Asking, Is.EqualTo("broken"));
                Assert.That(stage.Root.Q<Label>("saves-ask").text, Is.EqualTo("이 저장을 삭제하겠습니까?"));
                Assert.That(stage.Root.Q("saves-delete-yes"), Is.Not.Null);
                Assert.That(stage.Root.Q("saves-delete-1"), Is.Null, "묻는 칸의 단추는 [삭제]·[취소]");
                list.CancelDelete();
                Assert.That(list.Asking, Is.Null);
                Assert.That(Directory.Exists(Path.Combine(root, "broken")), Is.True, "[취소]는 지우지 않는다");
                Assert.That(list.Delete(), Is.False, "묻지 않고는 지우지 않는다");
                list.AskDelete("broken");
                Assert.That(list.Delete(), Is.True);
                Assert.That(Directory.Exists(Path.Combine(root, "broken")), Is.False);
                Assert.That(list.Entries.Count, Is.EqualTo(1));
                Assert.That(stage.Root.Q("saves-row-1"), Is.Null);

                // ── [이어하기]: 나갈 때와 같은 상태의 보드. 게임 시작 연출 없이 내 턴, 기록 판은 한 줄, 카메라는 내 말 ──
                Assert.That(list.Resume(id), Is.True, flow.ResumeError);
                yield return null;
                Assert.That(stage.Current, Is.Null, "저장 목록이 닫히고 보드");
                Assert.That(flow.OnBoard, Is.True);
                Assert.That(flow.Session.Hash, Is.EqualTo(hash), "같은 상태");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(count));
                Assert.That(flow.Session.Rewound, Is.EqualTo(0), "쉬는 때에 나갔다");
                Assert.That(flow.Client.View.Round, Is.EqualTo(round));
                Assert.That(flow.Protagonist(SoloGame.Seat), Is.EqualTo(Hero));
                Assert.That(flow.Fx.Sequencer.IsEmpty, Is.True, "막 카드·턴 넘김을 다시 틀지 않는다");
                for (int i = 0; i < 4; i++) yield return null;
                flow.Skip(1000);
                yield return null;
                Assert.That(flow.InputOpen, Is.True);
                Assert.That(flow.Client.Choice(ChoiceKind.MapAction), Is.Not.Null, "내 턴");
                Assert.That(flow.Hud.RecentLog, Is.EqualTo(new[] { "저장한 곳에서 이어서 시작했다" }), "기록 판");
                Assert.That(Vector3.Distance(rig.Target, board.CellTop(cell)), Is.LessThan(1e-3f), "카메라는 내 말로");
                Assert.That(board.Pieces.Of(SoloGame.Seat), Is.Not.Null);

                // 이어서 한 턴: 같은 저장에 이어 쓴다
                Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True);
                yield return null;
                for (int i = 0; i < 400 && !(flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null); i++)
                {
                    flow.Skip(250);
                    yield return null;
                }
                Assert.That(flow.Client.View.Round, Is.EqualTo(round + 1));
                Assert.That(flow.SaveId, Is.EqualTo(id));
                Assert.That(SaveStore.ReadLog(Path.Combine(root, id)).Count, Is.EqualTo(flow.Session.CommandCount), "이어 쓴 로그");

                // ── 이어 받은 판의 보드 이펙트 판(결정 69): HUD가 먼저 뜬 뒤에 처음 들어와도 맨 아래 — 그 뒤 여는 창은 HUD 위. 걸음 끝에는 먼지가 없고(결정 70 ⑬ ⑥), 그려 둔 이펙트는 게임을 떠나면 지워진다 ──
                Assert.That(flow.Hud.Reach.Count, Is.GreaterThan(0));
                Assert.That(flow.Hud.PickCell(flow.Hud.Reach[0]), Is.True, "칸을 고른다(걷기 전 확인 — 결정 70 ⑱)");
                Assert.That(flow.Hud.ConfirmMove(), Is.True, "[이동]: 한 번 걷는다");
                bool dust = false;
                for (int i = 0; i < 400 && !(flow.InputOpen && flow.Fx.Sequencer.Current == null); i++)
                {
                    flow.Skip(30);
                    dust |= flow.Sheets != null && flow.Sheets.Shown.Count > 0;
                }
                Assert.That(flow.InputOpen, Is.True, "다 걸었다");
                Assert.That(dust, Is.False, "걷기 끝에는 착지 먼지가 없다(결정 70 ⑬ ⑥)");
                Assert.That(flow.Sheets, Is.Not.Null, "보드 이펙트 판(GameFlow.Sheets)");
                flow.Sheets.Begin();
                flow.Sheets.Add(SheetBeats.Dust, 10, 100f, 100f, 2f);
                flow.Sheets.End();
                flow.OpenMenu();
                Assert.That(stage.Current, Is.InstanceOf<BoardMenuScreen>());
                int sheetsAt = stage.Root.IndexOf(flow.Sheets.Element), hudAt = stage.Root.IndexOf(stage.Root.Q("board-hud")), menuAt = stage.Root.IndexOf(stage.Root.Q("board-menu"));
                Assert.That(sheetsAt, Is.EqualTo(0), "보드 이펙트 판은 맨 아래");
                Assert.That(hudAt, Is.GreaterThan(sheetsAt), "HUD가 이펙트 판 위");
                Assert.That(menuAt, Is.GreaterThan(hudAt), "이어 받은 판에서도 창이 HUD 위");
                Assert.That(flow.Sheets.Shown.Count, Is.GreaterThan(0), "그려 둔 먼지가 메뉴 뒤에 남아 있다");
                // ── 게임 메뉴의 [설정](결정 70 ⑨): 같은 설정 화면이 HUD 위에 — 게임은 그대로, 카메라도 그대로. 말 그림을 바꾸면 말이 지금 칸에 다시 서고, 창 크기(등급)가 바뀌어도 화면이 남는다 ──
                Assert.That(stage.Root.Q("board-menu-settings"), Is.Not.Null);
                for (int i = 0; i < 5; i++) yield return null;
                var camWas = Camera.main.transform.position;
                int myCell = flow.Client.View.Seats[SoloGame.Seat].Cell, commandsWas = flow.Session.CommandCount;
                ((BoardMenuScreen)stage.Current).OpenSettings();
                Assert.That(stage.Current, Is.InstanceOf<SettingsScreen>());
                var inGame = (SettingsScreen)stage.Current;
                Assert.That(inGame.InGame && flow.MenuUp && flow.OnBoard, Is.True);
                Assert.That(stage.Root.Q("settings-scrim"), Is.Not.Null, "뒤의 HUD를 덮는다");
                Assert.That(stage.Root.Q("board-hud"), Is.Not.Null, "HUD는 그대로");
                Assert.That(flow.Fx.Sequencer.Paused, Is.True, "화면 대기열은 메뉴 때처럼 서 있다");
                yield return null;
                yield return null;
                Assert.That(Camera.main.transform.position, Is.EqualTo(camWas), "게임 중 설정은 카메라를 타이틀 전경 자리로 옮기지 않는다");
                Assert.That(rig.InputEnabled, Is.False);
                var standing = board.Pieces.Of(SoloGame.Seat).Root.transform.position;
                inGame.PickPiece(PieceStyle.Job);
                inGame.PickPiece(PieceStyle.Dot);
                Assert.That(board.Pieces.Of(SoloGame.Seat).Style, Is.EqualTo(PieceStyle.Dot));
                Assert.That(Vector3.Distance(board.Pieces.Of(SoloGame.Seat).Root.transform.position, standing), Is.LessThan(1e-3f), "말 그림을 바꿔도 내 말은 지금 선 칸(" + myCell + ")에");
                // 창 크기를 바꾼 것처럼 그리는 곳의 크기를 바꾼다(1080 → 720 → 1080): 등급만 바뀌고 설정 화면·HUD·게임은 그대로
                var small = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                small.Create();
                boot.Root.SetTarget(small);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(stage.Grade, Is.EqualTo(new UiGrade(720, 1)), "게임 중 등급이 바뀐다");
                Assert.That(stage.Current, Is.SameAs(inGame));
                Assert.That(stage.Root.Q("board-hud"), Is.Not.Null);
                Assert.That(Camera.main.transform.position, Is.EqualTo(camWas), "크기가 바뀌어도 보던 곳 그대로");
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 3; i++) yield return null;
                small.Release();
                Object.Destroy(small);
                Assert.That(stage.Grade, Is.EqualTo(new UiGrade(1080, 1)));
                inGame.Back();
                Assert.That(stage.Current, Is.InstanceOf<BoardMenuScreen>(), "[뒤로]는 게임 메뉴로");
                Assert.That((flow.Session.CommandCount, flow.OnBoard, flow.SaveId), Is.EqualTo((commandsWas, true, id)), "게임은 그대로");
                Assert.That(stage.Root.IndexOf(stage.Root.Q("board-menu")), Is.GreaterThan(stage.Root.IndexOf(stage.Root.Q("board-hud"))));
                ((BoardMenuScreen)stage.Current).AskQuit();
                ((BoardMenuScreen)stage.Current).Quit();
                Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
                Assert.That(flow.Sheets.Shown, Is.Empty, "떠난 게임의 먼지가 타이틀에 남지 않는다");
                foreach (var img in flow.Sheets.Element.Children()) Assert.That(img.style.display.value, Is.EqualTo(DisplayStyle.None), img.name);

                // ── 나가서 목록에서 지운다 → 빈 목록 → [뒤로] → 「이어하기」가 꺼진다 ──
                flow.OpenTitle();
                yield return null;
                Assert.That(((TitleScreen)stage.Current).SaveLine, Is.EqualTo("신오의 심장 · 1막 · 5라운드"));
                flow.OpenSaves();
                yield return null;
                list = (SaveListScreen)stage.Current;
                list.AskDelete(id);
                Assert.That(list.Delete(), Is.True);
                Assert.That(list.Entries, Is.Empty);
                Assert.That(stage.Root.Q<Label>("saves-empty").text, Is.EqualTo("저장한 게임이 없습니다."));
                list.Back();
                yield return null;
                title = (TitleScreen)stage.Current;
                Assert.That(title.Ready(title.Shown[cont]), Is.False);
                Assert.That(title.Picked, Is.EqualTo(cont), "[뒤로]는 「이어하기」 줄로");
                Assert.That(flow.Saves(), Is.Empty);
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [UnityTest]
        public IEnumerator M4_배틀한가운데에서나갔다가이어받으면그앞으로돌아와끝이끊지않은판과같다_에러로그0()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-save-test-" + System.Guid.NewGuid().ToString("N"));
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
                Assert.That(boot.Flow.SaveRoot, Is.Null, "에디터의 흐름은 기본이 저장 안 함(사용자의 저장 폴더에 쓰지 않는다)");
                Assert.That(boot.Flow.Saves(), Is.Empty);
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;

                boot.UseSaves(root, () => Clock);
                var flow = boot.Flow;
                Assert.That(flow.SaveRoot, Is.EqualTo(root));
                Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Off), "게임 밖");
                Whole(flow.Ctx, SoloTrail.Defeat, out ulong final, out int finalCount);

                // ── 새 판: 처음부터 저장된다 ──
                bool galactic = false, challenged = false;
                flow.EventsSeen += events =>
                {
                    foreach (var e in events)
                        if (e is BattleStarted b) galactic = b.Kind == BattleKind.Galactic;
                        else if (e is BattleEnded && galactic) challenged = true;
                };
                var pilot = FlowPilot.Start(flow, SoloTrail.Defeat, Hero);
                yield return null;
                string id = flow.SaveId;
                Assert.That(id, Does.StartWith("solo-"));
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Saved));
                Assert.That(flow.SavedAtMs, Is.EqualTo(Clock));
                Assert.That(Directory.Exists(Path.Combine(root, id)), Is.True);
                Assert.That(flow.DeleteSave(id), Is.False, "하고 있는 판의 저장은 지우지 않는다");
                flow.Skip(flow.OpeningMs);
                yield return null;

                // 야생 배틀 한가운데까지(배틀 화면에서 내 행동을 고르는 때)
                for (int i = 0; i < 800; i++)
                {
                    var c = flow.Client.FirstChoice();
                    if (flow.Battle != null && stage.Current is BattleScreen && c != null && c.Kind == ChoiceKind.BattleAction) break;
                    if (!FlowPilot.Step(flow, pilot)) flow.Skip(250);
                    yield return null;
                }
                Assert.That(stage.Current, Is.InstanceOf<BattleScreen>(), "야생 배틀에 닿지 못했다");
                Assert.That(flow.Client.View.Battle, Is.Not.Null);
                int cut = flow.Session.CommandCount;
                Assert.That(SaveStore.ReadLog(Path.Combine(root, id)).Count, Is.EqualTo(cut), "명령마다 로그 한 줄");
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Saved));

                // ── 타이틀로 나간다 → 저장 목록 ──
                flow.OpenTitle();
                yield return null;
                Assert.That(flow.Session, Is.Null);
                Assert.That(flow.SaveId, Is.Null);
                var list = flow.Saves();
                Assert.That(list.Count, Is.EqualTo(1));
                Assert.That(list[0].SaveId, Is.EqualTo(id));
                Assert.That(GameFlow.CanResume(list[0]), Is.True, list[0].Detail);
                Assert.That(list[0].Head.Protagonists, Is.EqualTo(new[] { Hero }));
                Assert.That(list[0].Head.Summary.SavedAtMs, Is.EqualTo(Clock));
                Assert.That(list[0].Head.Summary.Seats[0].Party.Count, Is.GreaterThanOrEqualTo(1));
                Assert.That(list[0].Head.Summary.Seats[0].HomeVillage, Is.GreaterThanOrEqualTo(0));

                // 읽지 못하는 저장: 하던 화면을 그대로 두고 false
                Directory.CreateDirectory(Path.Combine(root, "broken"));
                File.WriteAllText(Path.Combine(root, "broken", SaveStore.HeadFile), "{ 깨짐");
                var withBroken = flow.Saves();
                Assert.That(withBroken.Count, Is.EqualTo(2));
                Assert.That(withBroken[1].SaveId, Is.EqualTo("broken"));
                Assert.That(GameFlow.CanResume(withBroken[1]), Is.False);
                Assert.That(flow.Resume("broken"), Is.False);
                Assert.That(flow.ResumeError, Is.Not.Null);
                Assert.That(flow.Resume("없는-저장"), Is.False);
                Assert.That(flow.Resume("../밖"), Is.False);
                Assert.That(stage.Current, Is.InstanceOf<TitleScreen>(), "실패한 이어 받기는 화면을 바꾸지 않는다");
                Assert.That(flow.Session, Is.Null);
                Assert.That(flow.DeleteSave("broken"), Is.True);
                Assert.That(flow.Saves().Count, Is.EqualTo(1));

                // ── 이어 받기: 배틀 앞의 쉬는 때로 돌아온다 ──
                Assert.That(flow.Resume(id), Is.True, flow.ResumeError);
                yield return null;
                Assert.That(flow.ResumeError, Is.Null);
                Assert.That(flow.OnBoard, Is.True);
                Assert.That(flow.Hud, Is.Not.Null, "보드 HUD");
                Assert.That(stage.Current, Is.Null, "배틀 화면이 아니라 보드");
                Assert.That(flow.Battle, Is.Null);
                Assert.That(flow.Session.Rewound, Is.GreaterThan(0));
                Assert.That(flow.Session.CommandCount, Is.EqualTo(cut - flow.Session.Rewound));
                Assert.That(flow.Client.View.Battle, Is.Null);
                Assert.That(flow.Client.Choice(ChoiceKind.MapAction), Is.Not.Null);
                Assert.That(flow.Protagonist(SoloGame.Seat), Is.EqualTo(Hero), "주인공은 저장에서");
                Assert.That(flow.Settings.Protagonist, Is.EqualTo(Hero));
                Assert.That(flow.SaveId, Is.EqualTo(id));
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Saved));
                var board = Object.FindAnyObjectByType<BoardBoot>();
                var piece = board.Pieces.Of(SoloGame.Seat);
                Assert.That(piece, Is.Not.Null, "내 말이 보드에 선다");
                for (int i = 0; i < 4; i++) yield return null;
                flow.Skip(1000);
                yield return null;
                Assert.That(flow.InputOpen, Is.True, "이어 받은 뒤 입력이 열린다");
                Assert.That(flow.Fx.Sequencer.IsEmpty, Is.True, "지나간 연출을 다시 틀지 않는다");

                // ── 이어서 끝까지: 끊지 않은 판과 같은 끝 ──
                for (int i = 0; i < 3000; i++)
                {
                    if (challenged && flow.Battle == null && flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null) break;
                    if (!FlowPilot.Step(flow, pilot)) flow.Skip(250);
                    yield return null;
                }
                Assert.That(challenged, Is.True, "[도전]까지 가지 못했다");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(finalCount));
                Assert.That(flow.Session.Hash, Is.EqualTo(final), "이어 한 판의 끝 = 끊지 않은 판의 끝");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Saved));
                Assert.That(SaveStore.ReadLog(Path.Combine(root, id)).Count, Is.EqualTo(finalCount), "이어 쓴 로그");

                // ── 나가서 지운다 ──
                flow.OpenTitle();
                yield return null;
                Assert.That(flow.DeleteSave(id), Is.True);
                Assert.That(flow.Saves(), Is.Empty);

                // ── 자동 저장 한 칸(결정 70 ③): 새 판은 늘 같은 칸에 쓰고, 다시 시작하면 그 칸만 덮는다 — 다른 이름의 저장(전에 판마다 만들던 것)은 그대로 ──
                Assert.That(id, Is.EqualTo(GameFlow.SoloSlot));
                Assert.That(flow.SoloSlotInUse, Is.False);
                string kept = Path.Combine(root, "solo-20261101-120000-0old");
                Directory.CreateDirectory(kept);
                File.WriteAllText(Path.Combine(kept, "keep.txt"), "x");
                flow.StartSolo(new SoloSettings(), SoloTrail.Defeat.Seed, SoloTrail.AiSeed);
                Assert.That(flow.SaveId, Is.EqualTo(GameFlow.SoloSlot));
                string slot = Path.Combine(root, GameFlow.SoloSlot);
                Assert.That(File.Exists(Path.Combine(slot, SaveStore.HeadFile)), Is.True, "시작하자마자 저장된다");
                flow.OpenTitle();
                yield return null;
                Assert.That(flow.SoloSlotInUse, Is.True);
                flow.OpenSolo();
                yield return null;
                Assert.That(((SoloScreen)stage.Current).Overwrites, Is.True);
                Assert.That(stage.Root.Q<Label>("solo-overwrite").text.Replace("\u2060", ""), Is.EqualTo("새 게임을 시작하면 자동 저장된 게임을 덮어씁니다."));
                File.WriteAllText(Path.Combine(slot, "marker.txt"), "앞 판");
                ((SoloScreen)stage.Current).Start();
                Assert.That(flow.SaveId, Is.EqualTo(GameFlow.SoloSlot), "새 판도 같은 칸");
                Assert.That(File.Exists(Path.Combine(slot, "marker.txt")), Is.False, "앞 판의 칸을 덮었다(폴더를 새로 만든다)");
                Assert.That(File.Exists(Path.Combine(slot, SaveStore.HeadFile)), Is.True);
                Assert.That(File.Exists(Path.Combine(kept, "keep.txt")), Is.True, "다른 이름의 저장은 건드리지 않는다");
                Assert.That(Directory.GetDirectories(root).Length, Is.EqualTo(2), "자동 저장 한 칸 + 옛 저장");
                flow.OpenTitle();
                yield return null;
                Assert.That(flow.Saves().FindAll(e => e.Readable).Count, Is.EqualTo(1), "이어할 수 있는 것은 한 칸");
                Assert.That(flow.DeleteSave(GameFlow.SoloSlot), Is.True);
                Assert.That(flow.SoloSlotInUse, Is.False);
                flow.OpenSolo();
                yield return null;
                Assert.That(stage.Root.Q("solo-overwrite"), Is.Null, "덮을 것이 없으면 알림도 없다");
                flow.OpenTitle();
                yield return null;
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        // 내 다음 보드 행동이 열리고 입력이 열릴 때까지 보낸다
        static IEnumerator NextTurn(GameFlow f)
        {
            yield return null;
            for (int i = 0; i < 400 && !(f.InputOpen && f.Client.Choice(ChoiceKind.MapAction) != null); i++)
            {
                f.Skip(250);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator M4_쓰기실패는답을거절하고사라진다고하지않으며_데이터해시가다른저장과고친머리는목록에서걸러진다_에러로그0()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            FileStream held = null;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-save-test-" + System.Guid.NewGuid().ToString("N"));
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

                // ── 데이터 해시: 씬이 읽은 표에서 센 값 = manifest.json의 contentHash ──
                string manifestHash = ContentManifest.Load(File.ReadAllText("Assets/Sinnoh/Data/Json/manifest.json")).ContentHash;
                Assert.That(Object.FindAnyObjectByType<BoardBoot>().ContentHash, Is.EqualTo(manifestHash), "보드가 읽은 표의 묶음 해시(표 목록이 바뀌었으면 BoardBoot.ContentHash의 이름·표를 맞춘다)");
                Assert.That(flow.ContentHash, Is.EqualTo(manifestHash));

                // ── 새 판 → 보드 ──
                flow.StartSolo(new SoloSettings { Protagonist = Hero }, SoloTrail.Defeat.Seed, SoloTrail.AiSeed);
                var village = (SetupVillageScreen)stage.Current;
                village.Pick(0);
                Assert.That(village.Confirm(), Is.True);
                flow.Skip(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(0);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                yield return null;
                string id = flow.SaveId;
                string dir = Path.Combine(root, id);
                flow.Skip(flow.OpeningMs);
                yield return NextTurn(flow);
                Assert.That(flow.InputOpen, Is.True, "첫 턴");
                Assert.That(SaveStore.Peek(dir).Head.ContentHash, Is.EqualTo(manifestHash), "새 판의 머리에 데이터 해시가 적힌다");
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Saved));

                // ── 로그 줄 쓰기 실패(파일이 잠겼다): 답이 거절되고 판은 그대로, 저장 상태 「실패」 — 나가기 안내는 「사라집니다」가 아니다 ──
                int count = flow.Session.CommandCount;
                ulong hash = flow.Session.Hash;
                held = new FileStream(Path.Combine(dir, SaveStore.LogFile), FileMode.Open, FileAccess.Read, FileShare.None);
                var end = flow.Client.Legal(flow.Client.Choice(ChoiceKind.MapAction)).Find(a => a.Kind == AnswerKind.EndTurn);
                Assert.That(flow.Answer(flow.Client.Choice(ChoiceKind.MapAction), end).Accepted, Is.False, "쓰지 못한 명령은 받아들이지 않는다");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(count));
                Assert.That(flow.Session.Hash, Is.EqualTo(hash), "명령 전으로 되돌렸다");
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Failed));
                Assert.That(flow.SaveError, Is.Not.Null);
                Assert.That(flow.Client.Choice(ChoiceKind.MapAction), Is.Not.Null, "같은 선택이 그대로 열려 있다");
                flow.OpenMenu();
                yield return null;
                var menu = (BoardMenuScreen)stage.Current;
                Assert.That(menu.SaveText, Is.EqualTo("저장하지 못했습니다."));
                Assert.That(menu.QuitNoteKey, Is.EqualTo("screen.board.menu.quit.failed"));
                menu.AskQuit();
                Assert.That(stage.Root.Q("board-menu-ask").Query<Label>().ToList()[1].text.Replace("⁠", ""), Is.EqualTo("마지막으로 저장된 곳부터 이어할 수 있습니다."));
                menu.Cancel();
                menu.Resume();
                yield return null;
                held.Dispose();
                held = null;

                // 다음 쓰기가 되면 풀린다
                Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "파일이 풀린 뒤의 턴 종료");
                yield return NextTurn(flow);
                Assert.That(flow.Client.View.Round, Is.EqualTo(2));
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Saved));
                Assert.That(flow.SaveError, Is.Null);
                Assert.That(SaveStore.ReadLog(dir).Count, Is.EqualTo(flow.Session.CommandCount));

                // ── 스냅샷·머리 쓰기 실패(머리 파일이 잠겼다): 판은 이어 가고 「실패」 — 로그로 그 자리까지 이어 받는다 ──
                held = new FileStream(Path.Combine(dir, SaveStore.HeadFile), FileMode.Open, FileAccess.Read, FileShare.None);
                Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "스냅샷을 못 써도 명령은 받아들인다");
                yield return NextTurn(flow);
                Assert.That(flow.Client.View.Round, Is.EqualTo(3));
                Assert.That(flow.Session.SnapshotFailed, Is.True);
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Failed));
                Assert.That(flow.SaveError, Is.Null, "로그 줄은 쓰였다");
                flow.OpenMenu();
                yield return null;
                menu = (BoardMenuScreen)stage.Current;
                Assert.That(menu.QuitNoteKey, Is.EqualTo("screen.board.menu.quit.failed"), "저장하는 판의 쓰기 실패는 「사라집니다」가 아니다");
                count = flow.Session.CommandCount;
                hash = flow.Session.Hash;
                held.Dispose();
                held = null;
                menu.AskQuit();
                menu.Quit();
                yield return null;
                Assert.That(SaveStore.Peek(dir).Head.Summary.Round, Is.LessThan(3), "머리(요약)는 실패하기 전의 것");
                Assert.That(flow.Resume(id), Is.True, flow.ResumeError);
                yield return null;
                Assert.That(flow.Session.CommandCount, Is.EqualTo(count), "나갈 때의 자리까지 돌아왔다(사라지지 않았다)");
                Assert.That(flow.Session.Hash, Is.EqualTo(hash));
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Saved));
                flow.OpenMenu();
                yield return null;
                Assert.That(((BoardMenuScreen)stage.Current).QuitNoteKey, Is.EqualTo("screen.board.menu.quit.saved"));
                flow.OpenTitle();
                yield return null;

                // 저장하지 않는 판(확인용)만 「사라집니다」
                flow.StartSolo(new SoloSettings(), SoloTrail.Defeat.Seed, SoloTrail.AiSeed, s => { });
                Assert.That(flow.SaveId, Is.Null);
                Assert.That(new BoardMenuScreen(flow).QuitNoteKey, Is.EqualTo("screen.board.menu.quit.unsaved"));
                flow.OpenTitle();
                yield return null;

                // ── 남이 고친 머리: 날짜로 바꿀 수 없는 저장 시각이어도 목록이 열리고 그 칸을 지울 수 있다 ──
                Assert.That(GameFlow.SaveTime(999999999999999999), Is.EqualTo(""));
                Assert.That(GameFlow.SaveTime(long.MaxValue, date: false), Is.EqualTo(""));
                Assert.DoesNotThrow(() => GameFlow.SaveTime(253402300799999));
                Assert.That(GameFlow.SaveTime(Clock), Is.Not.EqualTo(""));
                string headPath = Path.Combine(dir, SaveStore.HeadFile);
                string good = File.ReadAllText(headPath);
                string timed = System.Text.RegularExpressions.Regex.Replace(good, "\"SavedAtMs\": [0-9]+", "\"SavedAtMs\": 999999999999999999");
                Assert.That(timed, Is.Not.EqualTo(good));
                File.WriteAllText(headPath, timed);
                Assert.DoesNotThrow(() => flow.OpenSaves());
                yield return null;
                var list = (SaveListScreen)stage.Current;
                Assert.That(list.Entries.Count, Is.EqualTo(1));
                Assert.That(list.Entries[0].Readable, Is.True);
                Assert.That(stage.Root.Q("saves-resume-0"), Is.Not.Null);
                Assert.That(stage.Root.Q("saves-delete-0"), Is.Not.Null);
                Assert.That(stage.Root.Q("saves-row-0").Query<Label>().ToList().Exists(l => l.text.EndsWith(" · ")), Is.False, "시각이 없으면 가운뎃점도 없다");

                // ── 데이터 해시가 다른 저장(키 목록은 같다): 「읽을 수 없는 저장 — 게임 데이터가 달라져…」, 이어 받지 않는다 ──
                Assert.That(good, Does.Contain(manifestHash));
                File.WriteAllText(headPath, good.Replace(manifestHash, new string('0', 64)));
                var changed = flow.Saves();
                Assert.That(changed[0].Problem, Is.EqualTo(SaveProblem.DataChanged), changed[0].Detail);
                Assert.That(GameFlow.CanResume(changed[0]), Is.False);
                Assert.That(flow.Resume(id), Is.False);
                Assert.That(flow.ResumeError, Does.Contain("해시"));
                flow.OpenSaves();
                yield return null;
                list = (SaveListScreen)stage.Current;
                Assert.That(list.Lines[0], Is.EqualTo("읽을 수 없는 저장"));
                Assert.That(stage.Root.Q("saves-row-0").Query<Label>().ToList().Exists(l => l.text.Replace("⁠", "") == "게임 데이터가 달라져 이어할 수 없습니다."), Is.True, "까닭");
                Assert.That(stage.Root.Q("saves-resume-0"), Is.Null);
                // 해시가 없는 머리(해시를 적기 전의 저장)는 키 목록만 본다
                File.WriteAllText(headPath, good.Replace("\"" + manifestHash + "\"", "null"));
                Assert.That(GameFlow.CanResume(flow.Saves()[0]), Is.True);
                list.AskDelete(id);
                Assert.That(list.Delete(), Is.True);
                Assert.That(flow.Saves(), Is.Empty);
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (held != null) held.Dispose();
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [UnityTest]
        public IEnumerator M4_셋업가운데에서나갔다가이어받으면셋업연출을다시틀지않고고른뒤의연출은그대로돈다_결정66()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-save-test-" + System.Guid.NewGuid().ToString("N"));
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

                // 새 판: 고향 마을을 확정하고, 그 연출(말이 떨어지는 중)의 한가운데에서 타이틀로 나간다
                flow.OpenSolo();
                ((SoloScreen)stage.Current).Set(SoloScreen.GalacticAi, "basic");   // 원칙 AI로(기본값은 「강함」 — 결정 118 ②)
                ((SoloScreen)stage.Current).Set(SoloScreen.TrainerCards, "on");   // 이어 받은 뒤 카드 고르기까지(기본값은 끔 — 결정 134)
                ((SoloScreen)stage.Current).Start();
                var village = (SetupVillageScreen)stage.Current;
                village.Pick(0);
                Assert.That(village.Confirm(), Is.True);
                string id = flow.SaveId;
                Assert.That(id, Does.StartWith("solo-"));
                int home = flow.Client.View.Seats[0].Cell;
                stage.Tweens.Advance(700);
                Assert.That(stage.Current, Is.InstanceOf<SetupDropScreen>(), "고향 마을 연출 중");
                flow.OpenTitle();
                yield return null;
                Assert.That(flow.Fx, Is.Null, "떠난 판의 연출은 버린다");

                // 이어 받기: 셋업 가운데(스타팅 포켓몬·트레이너 카드 고르기)로 돌아온다 — 고향 마을 연출도 카드 받기 연출도 다시 틀지 않는다
                Assert.That(flow.Resume(id), Is.True, flow.ResumeError);
                yield return null;
                yield return null;
                Assert.That(flow.Client.View.Phase, Is.EqualTo(Phase.Setup));
                Assert.That(stage.Current, Is.InstanceOf<SetupPickScreen>(), "연출 없이 바로 고르는 화면");
                Assert.That(flow.Fx.Sequencer.IsEmpty, Is.True, "이어 받은 판은 셋업 연출을 다시 틀지 않는다");
                Assert.That(flow.SetupOpen, Is.True, "바로 고를 수 있다");
                var pick = (SetupPickScreen)stage.Current;
                Assert.That(pick.Cards.Count, Is.EqualTo(3));
                Assert.That(pick.CardsFaceUp, Is.EqualTo(3), "카드는 이미 앞면으로 놓여 있다");
                foreach (var back in stage.Root.Query(className: "sn-pick-back").ToList()) Assert.That(back.style.display.value, Is.EqualTo(DisplayStyle.None), "뒷면이 덮여 있지 않다");
                var piece = board.Pieces.Of(0);
                Assert.That(piece, Is.Not.Null, "말은 고른 마을에 이미 서 있다");
                Assert.That(piece.Root.activeSelf, Is.True);
                Assert.That(piece.Spot.Cell, Is.EqualTo(home));
                Assert.That(piece.Root.transform.localPosition.y, Is.EqualTo(board.CellTop(home).y).Within(0.05f), "떨어지는 중이 아니다");

                // 이어서 고르면 고른 뒤·공개 연출은 그대로 돌고(받은 카드 연출은 끼어들지 않는다) 보드로 간다
                pick.PickStarter(0);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                foreach (var entry in flow.Fx.Sequencer.Queued) Assert.That(entry.Step(Sinnoh.Fx.SetupRows.Deal), Is.Null, "받은 카드 단계가 다시 서지 않는다");
                Assert.That(stage.Current, Is.SameAs(pick), "고른 뒤의 연출 동안 화면이 남는다");
                Assert.That(Sinnoh.Fx.SetupRows.Pending(flow.Fx.Sequencer), Is.True);
                flow.Skip(flow.OpeningMs);
                yield return null;
                Assert.That(stage.Current, Is.Null);
                Assert.That(flow.Hud, Is.Not.Null, "보드 HUD");
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Saved));
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
