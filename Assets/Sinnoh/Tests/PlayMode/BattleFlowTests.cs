using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Rekkon.UiKit;
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
    /// 보드 → [도전] → 배틀 화면 → 기술을 눌러 배틀 끝까지 → 결과 창 → [확인] → 보드로 돌아와 턴 넘김 → 다음 턴(plan §4 「배틀 화면 뼈대」, 04 §4-7).
    /// 판은 찾아 둔 고정 시드 둘이다(dotnet 시험 <c>BattleFeedTests.WinGame</c>·<c>LoseGame</c>과 같은 판): 이기는 판(1막 목표가 풀린다)과 지는 판(고향 마을로).
    /// 화면의 함수로 누른다(실제 마우스 길은 <c>MouseProbe</c>). 상대(AI)의 답은 방장 멈춤(연출 길이) 뒤에 오고 화면도 그 연출을 다 돈다(공격 하나가 8초쯤) —
    /// 세션 시각과 연출 시각을 프레임마다 더 흘려 기다림을 줄인다(<c>GameFlow.Skip</c>).
    /// 연출(plan §4 「배틀 연출」, 04 §8-9): 기술을 누르면 입력이 닫히고 기술 줄 자리가 굴림판으로 바뀐다 → 결과 줄의 면 = 엔진의 면 → 상대 체력은 착지 때 바뀐다 → 끝나면 입력이 열린다.
    /// Gen 겉모양·무대 그림이 있어야 돈다.
    /// </summary>
    public sealed class BattleFlowTests
    {
        const ulong AiSeed = 99;

        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        [UnityTest]
        public IEnumerator M4_도전해서이기면결과창뒤보드로돌아와1막목표가풀리고에러로그0() => Play(179, 0, 1, true);

        [UnityTest]
        public IEnumerator M4_도전해서지면결과창뒤고향마을에서다음턴이고에러로그0() => Play(1, 0, 0, false);

        [UnityTest]
        public IEnumerator M4_도전한뒤전환이도는동안메뉴를열었다닫아도배틀화면이열리고이어진다() => MenuDuringWipe(179, 0, 1);

        [UnityTest]
        public IEnumerator M4_지는판에서도전환이도는동안메뉴를열었다닫아도배틀화면이열리고이어진다() => MenuDuringWipe(1, 0, 0);

        [UnityTest]
        public IEnumerator M4_2라운드의배틀무대는저녁그림이다() => StageAtRound(1, DayTime.Evening);

        [UnityTest]
        public IEnumerator M4_3라운드의배틀무대는밤그림이다() => StageAtRound(2, DayTime.Night);

        // 배틀 화면이 그 라운드의 시간대 그림을 실제로 고르는지(결정 70 ⑥): 턴을 넘겨 라운드를 올린 뒤 [도전] — 첫 턴(낮)의 배틀만 보면 시간대 연결을 끊어도 통과한다(검토 지적)
        static IEnumerator StageAtRound(int turns, DayTime expected)
        {
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
                flow.StartSolo(new SoloSettings(), 179, AiSeed);
                yield return null;
                var village = (SetupVillageScreen)stage.Current;
                village.Pick(0);
                Assert.That(village.Confirm(), Is.True);
                flow.Skip(flow.QueueLeftMs);
                yield return null;
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(1);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                yield return null;
                flow.Skip(flow.OpeningMs);
                yield return Until(() => flow.InputOpen, 10f);
                for (int i = 0; i < turns; i++)
                {
                    Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "턴 종료");
                    yield return null;
                    flow.Skip(flow.QueueLeftMs);
                    yield return Until(() => flow.InputOpen, 10f);
                    Assert.That(flow.InputOpen, Is.True, "다음 라운드의 내 턴");
                }
                Assert.That(flow.Client.View.Round, Is.EqualTo(1 + turns));
                Assert.That(DayTimes.Of(flow.Client.View.Round), Is.EqualTo(expected));
                Assert.That(flow.Hud.Press(BoardHud.Challenge), Is.True, "[도전]");
                yield return null;
                yield return Until(() =>
                {
                    flow.Skip(250);
                    return stage.Current is BattleScreen b && b.StageBg != null;
                }, 30f);
                var battle = stage.Current as BattleScreen;
                Assert.That(battle, Is.Not.Null, "배틀 화면");
                Assert.That(battle.Time, Is.EqualTo(expected));
                DsStage.Terrain(flow.Battle.Biome, flow.Battle.Climate, DayTime.Day, out string dayBg, out _, out _);
                Assert.That(battle.StageBg, Is.Not.EqualTo(dayBg), "낮 그림이 아니다");
                Assert.That(battle.StageBg, Is.EqualTo(DayTimes.Art(dayBg, expected, true)), "그 시간대의 배경 그림");
                Assert.That(battle.StageBg, Does.EndWith("_" + DayTimes.Name(expected) + ".png"));
                Assert.That(GenAssets.Load<Texture2D>(battle.StageBg), Is.Not.Null, battle.StageBg);
            }
            finally
            {
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }

        static IEnumerator MenuDuringWipe(ulong seed, int villageIndex, int starterIndex)
        {
            // 검토 반영: [도전] 뒤 배틀 화면은 조우 전환(22행)의 가운데에 열리는데, 그 전에 [메뉴]를 열어 두면 상대(AI)의 갱신이 메뉴 위로 배틀 화면을 열어
            // 화면 대기열이 일시정지된 채 남았다(입력이 영영 열리지 않는다)
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
                flow.StartSolo(new SoloSettings(), seed, AiSeed);
                yield return null;
                var village = (SetupVillageScreen)stage.Current;
                village.Pick(villageIndex);
                Assert.That(village.Confirm(), Is.True);
                flow.Skip(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
                yield return null;
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(starterIndex);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                yield return null;
                flow.Skip(flow.OpeningMs);   // 게임 시작의 막 카드 · 조무래기 놓기(약 9초)는 건너뛴다 — 그 모습은 SoloFlowTests가 본다
                yield return Until(() => flow.InputOpen, 10f);
                Assert.That(flow.InputOpen, Is.True, "첫 턴: 입력 열림");
                Assert.That(flow.Hud.Press(BoardHud.Challenge), Is.True);
                yield return null;
                Assert.That(flow.Battle, Is.Not.Null);
                Assert.That(stage.Current, Is.Null, "배틀 화면은 전환의 가운데에 열린다");

                // [메뉴]: 전환이 도는 동안에도 열린다. 메뉴 뒤에서 전환이 끝나고 상대의 갱신이 와도 배틀 화면은 메뉴 위로 열리지 않는다
                var seq = flow.Fx.Sequencer;
                flow.OpenMenu();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<BoardMenuScreen>());
                Assert.That(seq.Paused, Is.True);
                for (int i = 0; i < 20; i++)
                {
                    flow.Skip(1000);
                    yield return null;
                    Assert.That(stage.Current, Is.InstanceOf<BoardMenuScreen>(), "메뉴가 떠 있는 동안 배틀 화면이 그 위로 열리지 않는다");
                }
                Assert.That(flow.WipeCover, Is.EqualTo(0f), "재생 중이던 전환은 메뉴 뒤에서 끝났다");

                // 닫으면 배틀 화면이 열리고 대기열이 이어져(인트로 …) 내 차례가 온다
                ((BoardMenuScreen)stage.Current).Resume();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<BattleScreen>(), "메뉴를 닫으면 배틀 화면이 열린다");
                Assert.That(seq.Paused, Is.False, "화면 대기열이 일시정지된 채 남지 않는다");
                var battle = (BattleScreen)stage.Current;
                // 인트로: 내 포켓몬이 화면에 나오기 전에는 기술 줄을 그리지 않는다(드러나기 전 정보가 먼저 보이지 않게)
                for (int i = 0; i < 600 && !(battle.Open && battle.Moves.Count > 0); i++)
                {
                    flow.Skip(100);
                    yield return null;
                    if (!battle.ShownMon(flow.Battle.MySide).Exists)
                    {
                        Assert.That(battle.Moves.Count, Is.EqualTo(0), "내 포켓몬이 나오기 전에는 기술 줄이 없다");
                        Assert.That(stage.Root.Q("battle-move-1"), Is.Null);
                    }
                    if (battle.AskingToken && battle.Open) battle.PressToken(true);
                }
                Assert.That(battle.Open, Is.True, "배틀 입력이 열린다");
                Assert.That(battle.Moves.Count, Is.GreaterThan(0), "내 기술 줄");
                Assert.That(battle.PressMove(0), Is.True);
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }

        static IEnumerator Play(ulong seed, int villageIndex, int starterIndex, bool wins)
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            var targets = new List<RenderTexture>();
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
                var rig = Camera.main.GetComponent<BoardCameraRig>();
                RenderTexture Target(int w, int h)
                {
                    var t = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    t.Create();
                    targets.Add(t);
                    return t;
                }
                boot.Root.SetTarget(Target(1920, 1080));
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(stage.Grade, Is.EqualTo(new UiGrade(1080, 1)));

                // ── 셋업: 찾아 둔 판(시드 · 마을 · 스타팅 포켓몬 · 첫 트레이너 카드) ──
                flow.StartSolo(new SoloSettings(), seed, AiSeed);
                yield return null;
                var village = (SetupVillageScreen)stage.Current;
                village.Pick(villageIndex);
                Assert.That(village.Confirm(), Is.True);
                flow.Skip(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
                yield return null;
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(starterIndex);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                yield return null;
                Assert.That(flow.OnBoard, Is.True);
                var client = flow.Client;
                int home = flow.Ctx.Fixtures.Fixtures[client.View.Seats[0].HomeVillage].Cell;
                flow.Skip(flow.OpeningMs);   // 게임 시작의 막 카드 · 조무래기 놓기(약 9초)는 건너뛴다 — 그 모습은 SoloFlowTests가 본다
                yield return Until(() => flow.InputOpen, 10f);
                Assert.That(flow.InputOpen, Is.True, "첫 턴: 입력 열림");
                int firstMoves = client.View.Seats[0].MovesLeft;
                int firstRound = client.View.Round;

                // ── 보드 HUD의 [도전]: 켜져 있다(회색이 아니다) ──
                var hud = flow.Hud;
                Assert.That(hud.Acts, Does.Contain(BoardHud.Challenge), "고향 마을의 조무래기에게 도전할 수 있다(합법 답)");
                var challenge = stage.Root.Q<Button>("board-hud-act-" + BoardHud.Challenge);
                Assert.That(challenge, Is.Not.Null);
                Assert.That(challenge.ClassListContains("sn-hud-off"), Is.False, "[도전]은 이제 준비된 단추다");
                Assert.That(challenge.enabledSelf, Is.True);
                int commands = flow.Session.CommandCount;
                Assert.That(hud.Press(BoardHud.Challenge), Is.True);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1), "도전 답 하나");

                // ── 조우 전환(04 §8-8 22행 — 갤럭시단 2500): 가운데 필드의 줄무늬가 닫히고, 가운데에서 배틀 화면이 HUD 위에 열리고, 줄무늬가 걷힌다 ──
                yield return null;
                Assert.That(stage.Current, Is.Null, "배틀 화면은 전환의 가운데에 열린다 — 그 전에는 보드가 보인다");
                Assert.That(flow.Battle, Is.Not.Null);
                Assert.That(flow.InputOpen, Is.False, "전환이 도는 동안 보드 입력은 닫혀 있다");
                var enter = flow.Fx.Sequencer;
                var wipe = enter.Current.Step(EnterRows.Wipe);
                Assert.That(wipe, Is.Not.Null, "조우 전환 단계");
                Assert.That(wipe.Ms, Is.EqualTo(2500));
                Assert.That(flow.WipeCover, Is.EqualTo(0f), "예비 동안은 줄무늬가 없다");
                stage.Tweens.Advance(wipe.StartMs + 1000 - enter.LocalMs);
                Assert.That(flow.WipeCover, Is.GreaterThan(0f).And.LessThan(1f), "줄무늬가 닫히는 중");
                Assert.That(stage.Root.Q("field-wipe"), Is.Not.Null);
                Assert.That(stage.Root.IndexOf(stage.Root.Q("field-wipe")), Is.EqualTo(stage.Root.childCount - 1), "줄무늬는 화면들 위에 얹힌다");
                Assert.That(stage.Current, Is.Null);
                // M9 다듬기 P14: 트레이너(갤럭시단)의 전환은 위에서 아래로 한 줄씩 닫힌다 — 첫 줄이 마지막 줄보다 먼저
                Assert.That(flow.WipeStyle, Is.EqualTo(WipeStyle.Trainer));
                Assert.That(stage.Root.Q("field-wipe-bar-1").style.scale.value.value.x, Is.GreaterThan(stage.Root.Q("field-wipe-bar-" + FieldWipe.Bars).style.scale.value.value.x));
                stage.Tweens.Advance(wipe.StartMs + 1250 - enter.LocalMs);
                Assert.That(flow.WipeCover, Is.EqualTo(1f), "가운데 필드가 다 덮인 때");
                Assert.That(stage.Root.Q("field-wipe-bar-" + FieldWipe.Bars).style.scale.value.value.x, Is.EqualTo(1f), "마지막 줄까지 다 닫혔다");
                Assert.That(stage.Current, Is.InstanceOf<BattleScreen>(), "배틀 화면");
                stage.Tweens.Advance(wipe.StartMs + 2000 - enter.LocalMs);
                Assert.That(flow.WipeCover, Is.EqualTo(0f), "줄무늬가 걷혔다");
                Assert.That(stage.Root.Q("field-wipe").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(stage.Root.Q("board-hud-log").style.display.value, Is.EqualTo(DisplayStyle.None), "배틀 화면이 떠 있는 동안 보드 기록 판은 숨는다(M4 마감에서 찾은 비침)");
                // 갤럭시단 등장(72행): 조무래기 앞모습이 상대 자리에 들어와 서고 「…승부를 걸어왔다!」
                var grunt = enter.Current.Step(EnterRows.Galactic);
                Assert.That(grunt, Is.Not.Null, "갤럭시단 등장 단계");
                stage.Tweens.Advance(grunt.StartMs + 1000 - enter.LocalMs);
                Assert.That(((BattleScreen)stage.Current).FoeTrainerShown, Is.True, "조무래기 앞모습");
                // 남·여는 조무래기가 선 칸으로 고른다(결정 70 ⑩) — 그 그림이 Gen에 있다
                Assert.That(((BattleScreen)stage.Current).FoeKey, Is.EqualTo(FoeArt.Grunt(flow.Battle.Cell)));
                Assert.That(flow.Battle.Cell, Is.GreaterThanOrEqualTo(0), "배틀 칸");
                Assert.That(GenAssets.Load<Texture2D>(FoeArt.GruntMale), Is.Not.Null);
                Assert.That(GenAssets.Load<Texture2D>(FoeArt.GruntFemale), Is.Not.Null);
                // 무대는 배틀이 벌어진 라운드의 시간대를 따른다(결정 70 ⑥) — 그 시간대의 배경 그림이 Gen에 있다
                var battleScreen = (BattleScreen)stage.Current;
                Assert.That(battleScreen.Time, Is.EqualTo(DayTimes.Of(flow.Client.View.Round)));
                DsStage.Terrain(flow.Battle.Biome, flow.Battle.Climate, battleScreen.Time, out string timedBg, out _, out _);
                Assert.That(battleScreen.StageBg, Is.EqualTo(timedBg));
                Assert.That(GenAssets.Load<Texture2D>(battleScreen.StageBg), Is.Not.Null, battleScreen.StageBg);
                Assert.That(((BattleScreen)stage.Current).Message, Is.EqualTo(stage.T("screen.battle.log.start.galactic")));
                Assert.That(stage.Hud, Is.InstanceOf<BoardHud>(), "HUD는 그대로 있다(위 띠)");
                Assert.That(flow.Battle, Is.Not.Null);
                Assert.That(flow.Battle.Kind, Is.EqualTo(BattleKind.Galactic));
                Assert.That(flow.InputOpen, Is.False, "창이 열려 있으면 보드 입력은 닫힌다");
                Assert.That(rig.InputEnabled, Is.False, "배틀 중: 보드 카메라 입력 끔");
                Assert.That(hud.Press(BoardHud.EndTurn), Is.False, "배틀 중에는 HUD 단추가 답을 내지 않는다");
                var screen = (BattleScreen)stage.Current;
                Assert.That(screen.Ds, Is.Not.Null, "DS 층(Gen 무대·글꼴·셰이더)");
                Assert.That(stage.Root.Q("battle-ds").resolvedStyle.backgroundImage.renderTexture, Is.SameAs(screen.Ds.Output), "DS 요소 = 256×192 RT");
                Assert.That(screen.Ds.Output.width, Is.EqualTo(256));
                Assert.That(screen.Ds.Output.height, Is.EqualTo(192));
                Assert.That(screen.Ds.Output.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(stage.Root.Q<Label>("battle-kind").text, Is.EqualTo("갤럭시단 배틀"));
                // 조무래기 둘 가운데 누가 먼저 나올지는 상대(AI)가 고른다 — 방장 멈춤 뒤에 답이 온다. 그동안은 내 포켓몬만 나와 있다
                yield return Until(() =>
                {
                    flow.Skip(500);
                    return screen.ShownMon(1).Exists;
                }, 30f);
                yield return null;
                Assert.That(screen.ShownMon(1).Exists, Is.True, "상대의 첫 포켓몬이 나왔다");
                Assert.That(stage.Root.Q("battle-info-me").Q(className: "sn-mon"), Is.Not.Null, "내 포켓몬");
                Assert.That(FaceBox.Shown(stage.Root.Q("battle-me-face")), Is.EqualTo(flow.Protagonist(flow.Client.Seat)), "배틀 기둥(내 쪽) 얼굴 상자 = 내 좌석 주인공(결정 61 ③)");
                Assert.That(stage.Root.Q("battle-foe-face"), Is.Null, "갤럭시단 쪽은 얼굴 상자 없이 이름만");
                Assert.That(stage.Root.Q("battle-info-foe").Q(className: "sn-mon"), Is.Not.Null, "상대 포켓몬(갤럭시단 카드도 같은 종의 도트)");
                Assert.That(stage.Root.Q("battle-log-lines").childCount, Is.GreaterThanOrEqualTo(3), "기록: 승부를 걸어왔다 · 출전 둘");

                // DS 화면에 무대가 그려졌다(빈 화면이 아니다)
                {
                    var tex = new Texture2D(256, 192, TextureFormat.RGBA32, false);
                    var prev = RenderTexture.active;
                    RenderTexture.active = screen.Ds.Output;
                    tex.ReadPixels(new Rect(0, 0, 256, 192), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    var colors = new HashSet<Color32>(tex.GetPixels32());
                    Object.Destroy(tex);
                    Assert.That(colors.Count, Is.GreaterThan(32), "DS 화면 색 수(배경 · 발판 · 도트 · 상자 · 글)");
                }

                // DS 자리·크기(04 §4-3): 등급마다 720 배치의 정수배, 등급 사이 창은 가운데 필드 한가운데
                foreach (var size in new[]
                {
                    new[] { 1280, 720, 384, 118, 512, 384 }, new[] { 2560, 1440, 768, 236, 1024, 768 }, new[] { 3840, 2160, 1152, 374, 1536, 1152 },
                    new[] { 1600, 900, 544, 208, 512, 384 }, new[] { 1920, 1080, 576, 187, 768, 576 },
                })
                {
                    boot.Root.SetTarget(Target(size[0], size[1]));
                    for (int i = 0; i < 4; i++) yield return null;
                    int scale = stage.Grade.PanelScale;
                    var wb = stage.Root.Q("battle-ds").worldBound;
                    string at = size[0] + "×" + size[1];
                    Assert.That(wb.x * scale, Is.EqualTo(size[2]), at + " DS 왼쪽");
                    Assert.That(wb.y * scale, Is.EqualTo(size[3]), at + " DS 위");
                    Assert.That(wb.width * scale, Is.EqualTo(size[4]), at + " DS 폭");
                    Assert.That(wb.height * scale, Is.EqualTo(size[5]), at + " DS 높이");
                    // 작은 얼굴 상자(위 띠 칩 · 기둥) = 조각 30×30의 정수배: 720 벌 ×1 · 1080 벌 ×2(결정 64). 기둥은 좌석 칩 아래에서 시작한다
                    float faceSide = stage.Grade.TokenHeight == 720 ? 30f : 60f;
                    Assert.That(stage.Root.Q("board-hud-seat-1-face").Q<Image>(className: "sn-face-art").worldBound.size, Is.EqualTo(new Vector2(faceSide, faceSide)), at + " 위 띠 얼굴 조각");
                    Assert.That(stage.Root.Q("battle-me-face").Q<Image>(className: "sn-face-art").worldBound.size, Is.EqualTo(new Vector2(faceSide, faceSide)), at + " 기둥 얼굴 조각");
                    Assert.That(stage.Root.Q("battle-info").worldBound.yMin, Is.GreaterThanOrEqualTo(stage.Root.Q("board-hud-seat-1").worldBound.yMax), at + " 기둥이 좌석 칩과 겹치지 않는다");
                    Assert.That(stage.Current, Is.InstanceOf<BattleScreen>(), at + ": 화면은 그대로");
                }
                screen = (BattleScreen)stage.Current;

                // ── 배틀: 내 차례마다 첫 기술, 우선권 토큰은 쓴다. 줄·단추 = 엔진의 합법 답 ──
                var got = new List<GameEvent>();
                void See(IReadOnlyList<GameEvent> evs) => got.AddRange(evs);
                flow.EventsSeen += See;
                int myTurns = 0, foeTurns = 0, guard = 0, busyPresses = 0, rollsSeen = 0, landings = 0, held = 0;
                float t0 = Time.realtimeSinceStartup;
                while (!screen.ShowsResult && Time.realtimeSinceStartup - t0 < 240f && guard++ < 40000)
                {
                    flow.Skip(250);   // 방장 멈춤과 화면 연출을 함께 건너뛴다(실시각으로는 공격 하나가 8초쯤이다)
                    yield return null;
                    Assert.That(stage.Current, Is.SameAs(screen), "배틀 내내 같은 화면(답마다 다시 만들지 않는다)");
                    if (screen.ShowsResult) break;
                    var choice = client.FirstChoice();
                    if (!screen.Ready)
                    {
                        // 연출이 도는 동안: 입력이 닫혀 있다 — 내 다음 선택이 이미 열려 있어도 기술 줄을 눌러 답이 나가지 않는다
                        Assert.That(screen.Open, Is.False);
                        if (choice != null && choice.Kind == ChoiceKind.BattleAction && !flow.Battle.Ended)
                        {
                            int was = flow.Session.CommandCount;
                            Assert.That(screen.PressMove(0), Is.False, "연출 중에는 답이 나가지 않는다");
                            var busyRow = stage.Root.Q("battle-move-1");
                            if (busyRow != null) Assert.That(busyRow.enabledSelf, Is.False, "연출 중의 기술 줄은 꺼져 있다");
                            Assert.That(flow.Session.CommandCount, Is.EqualTo(was));
                            busyPresses++;
                        }
                        continue;
                    }
                    if (choice == null)
                    {
                        // 상대(AI) 차례: 독은 「상대의 차례」만
                        if (!flow.Battle.Ended)
                        {
                            Assert.That(stage.Root.Q<Label>("battle-turn-title").text, Is.EqualTo("상대의 차례"));
                            Assert.That(screen.Moves, Is.Empty);
                            Assert.That(stage.Root.Q("battle-move-1"), Is.Null, "상대 차례에는 기술 줄이 없다");
                            foeTurns++;
                        }
                        continue;
                    }
                    if (!screen.Open) continue;   // 클릭 막기 시간
                    var legal = client.Legal(choice);
                    if (screen.AskingToken)
                    {
                        Assert.That(choice.Kind, Is.EqualTo(ChoiceKind.Reaction));
                        Assert.That(stage.Root.Q<Button>("battle-token-use").enabledSelf, Is.True);
                        Assert.That(screen.PressToken(true), Is.True);
                        continue;
                    }
                    Assert.That(choice.Kind, Is.EqualTo(ChoiceKind.BattleAction), "첫 조무래기 배틀에서 내게 열리는 선택은 기술 고르기와 우선권 토큰뿐이다");
                    var useMoves = legal.FindAll(a => a.Kind == AnswerKind.UseMove && a.Arg < 0 && a.Target != BattleRules.AlphaStrike);   // 벤치에서 쓰는 기술(Arg = 벤치 포켓몬, rulings G20) · 알파 스트라이크(A26)는 줄에 없다 [가정 plan §5 #84 · #86 ①]
                    Assert.That(screen.Moves.Count, Is.EqualTo(useMoves.Count), "기술 줄 수 = 합법 UseMove 수");
                    for (int i = 0; i < useMoves.Count; i++)
                    {
                        Assert.That(screen.Moves[i].SameAs(useMoves[i]), "기술 줄 " + (i + 1) + " = 합법 답 그대로(순서까지)");
                        var row = stage.Root.Q("battle-move-" + (i + 1));
                        Assert.That(row, Is.Not.Null, "기술 줄 " + (i + 1));
                        Assert.That(row.enabledSelf, Is.True);
                        string name = stage.T(BattleFeed.MoveNameKey(flow.Ctx, useMoves[i].Target));
                        Assert.That(row.Q<Label>(className: "sn-battle-move-name").text, Does.EndWith(name), "기술 이름은 문자열 표에서");
                    }
                    Assert.That(stage.Root.Q("battle-move-" + (useMoves.Count + 1)), Is.Null, "합법 답에 없는 기술 줄은 없다");
                    bool canForfeit = legal.Exists(a => a.Kind == AnswerKind.Forfeit);
                    Assert.That(screen.ForfeitOn, Is.EqualTo(canForfeit), "[기권]은 합법 답에 있을 때만 켜진다");
                    Assert.That(stage.Root.Q("battle-forfeit").ClassListContains("sn-hud-off"), Is.EqualTo(!canForfeit));
                    if (!canForfeit) Assert.That(screen.PressForfeit(), Is.False, "꺼진 [기권]은 답을 내지 않는다");
                    Assert.That(stage.Root.Q("battle-bag").ClassListContains("sn-hud-off"), "갤럭시단 배틀에서는 볼을 던질 수 없다 — [가방]은 회색");
                    Assert.That(screen.BagOn, Is.False);
                    Assert.That(screen.OpenBag(), Is.False, "꺼진 [가방]은 열리지 않는다");
                    Assert.That(stage.Root.Q("battle-party"), Is.Null, "[포켓몬] 단추는 없다(자발 교체는 규칙에 없다)");
                    Assert.That(legal.Count, Is.EqualTo(useMoves.Count + (canForfeit ? 1 : 0)), "합법 답 = 기술 줄 + 기권(그 밖의 답이 있으면 화면에 없는 답이다)");
                    string myMon = stage.T("pokemon." + flow.Ctx.Cards.Pokemon[flow.Battle.Mon(0).Def].Key);
                    Assert.That(stage.Root.Q<Label>("battle-turn-title").text, Is.EqualTo(myMon + "의 차례"));
                    // 기술 줄의 툴팁은 줄 위에 뜬다(독이 화면 맨 아래다)
                    if (myTurns == 0)
                    {
                        stage.Tips.Show(stage.Root.Q("battle-move-1"));
                        yield return null;
                        yield return null;
                        Assert.That(stage.Tips.Element.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible));
                        Assert.That(stage.Tips.Element.worldBound.yMax, Is.LessThanOrEqualTo(stage.Root.Q("battle-dock").worldBound.yMin), "툴팁이 독 위에");
                        Assert.That(stage.Tips.Element.Query<Label>().ToList()[0].text, Is.EqualTo(stage.T(BattleFeed.MoveNameKey(flow.Ctx, useMoves[0].Target))));
                        stage.Tips.Hide();
                    }
                    int lines = flow.Battle.Lines.Count, before = flow.Session.CommandCount, shownLines = screen.ShownLineCount, at = got.Count;
                    Assert.That(screen.PressMove(0), Is.True);
                    Assert.That(flow.Session.CommandCount, Is.GreaterThanOrEqualTo(before + 1), "기술 답 하나");
                    Assert.That(flow.Battle.Lines.Count, Is.GreaterThan(lines), "받은 결과는 바로 쌓이지만");
                    Assert.That(screen.ShownLineCount, Is.EqualTo(shownLines), "화면의 기록 줄은 연출이 닿을 때 붙는다(결과가 먼저 새지 않는다)");
                    Assert.That(screen.Open, Is.False, "답을 낸 뒤 연출이 도는 동안 입력이 닫힌다");
                    Assert.That(screen.PressMove(0), Is.False);
                    myTurns++;

                    // ── 이 공격의 연출: 굴림판 → 결과 줄의 면 = 엔진의 면 → 상대 체력은 착지 때 바뀐다 ──
                    var mine = got.GetRange(at, got.Count - at);
                    var rolled = mine.Find(e => e is DiceRolled d && d.Side == 0) as DiceRolled;
                    Assert.That(rolled, Is.Not.Null, "내 공격의 굴림");
                    var want = new List<AttackDieFace>(rolled.Faces);
                    foreach (var e in mine)
                        if (e is DiceFacesChanged changed && e.Seq == rolled.Seq && changed.Changes != null)
                            foreach (var c in changed.Changes)
                                if (c.Index >= 0 && c.Index < want.Count) want[c.Index] = c.After;
                    var dmg = mine.Find(e => e is DamageBreakdown && e.Seq == rolled.Seq) as DamageBreakdown;
                    var hit = dmg != null ? mine.Find(e => e is HpChanged h && e.Seq == rolled.Seq && h.PokemonId == dmg.TargetId) as HpChanged : null;
                    int foeId = screen.ShownMon(1).Id;
                    // 이 공격 앞의 마지막 피해 계산(누구의 것이든 — 칸이 있는 것만 계산 줄에 선다)
                    int rolledAt = got.IndexOf(rolled);
                    int lastAt = rolledAt > 0 ? got.FindLastIndex(rolledAt - 1, rolledAt, e => e is DamageBreakdown) : -1;
                    var lastDmg = lastAt >= 0 ? (DamageBreakdown)got[lastAt] : null;
                    if (lastDmg != null && lastDmg.Steps == null) lastDmg = null;
                    for (int k = 0; k < 400 && !screen.Rolling; k++)
                    {
                        stage.Tweens.Advance(50);
                        yield return null;
                    }
                    Assert.That(screen.Rolling, Is.True, "기술 줄 자리가 굴림판으로 바뀐다");
                    Assert.That(stage.Root.Q("battle-roll"), Is.Not.Null);
                    Assert.That(stage.Root.Q("battle-move-1"), Is.Null, "굴림판이 떠 있는 동안 기술 줄은 없다");
                    Assert.That(screen.RollBoardOn, Is.True, "녹화본 재생(씬의 rolls.bytes · 주사위 셰이더)");
                    Assert.That(stage.Root.Q("battle-roll-board").resolvedStyle.backgroundImage.renderTexture, Is.Not.Null, "굴림판 그림 = RT");
                    Assert.That(screen.RollFaces, Is.Empty, "굴리는 동안 결과 줄은 비어 있다");
                    Assert.That(screen.RollSum, Is.EqualTo(0));
                    // 계산 줄(결정 70 ⑧): 이 공격의 계산 단계가 올 때까지는 앞 공격의 계산을 남겨 둔다 — 첫 공격이면 비어 있다
                    Assert.That(screen.CalcLit, Is.EqualTo(0), "이 공격의 계산은 계산 단계가 올 때까지 켜지지 않는다");
                    if (lastDmg == null)
                    {
                        Assert.That(screen.CalcHeld, Is.False);
                        Assert.That(stage.Root.Q("battle-calc-total"), Is.Null, "첫 공격: 계산 줄은 비어 있다");
                        // 결정 101 ⑤: 계산 줄 머리에 주사위 6면(일반 타격 4 · 급소 1 · 상태 이상 1)이 늘 펼쳐져 있다
                        Assert.That(stage.Root.Q("battle-dienet"), Is.Not.Null, "주사위 6면 안내");
                        for (int f = 1; f <= BattleScreen.DieFaces.Length; f++) Assert.That(stage.Root.Q("battle-dienet-face-" + f), Is.Not.Null, "주사위 면 " + f);
                        Assert.That(BattleScreen.DieFaces, Is.EqualTo(new[] { AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.RegularHit, AttackDieFace.CriticalHit, AttackDieFace.StatusSymbol }));
                    }
                    else
                    {
                        Assert.That(screen.CalcHeld, Is.True, "굴림판이 떠도 앞 공격의 계산이 남아 있다");
                        Assert.That(stage.Root.Q("battle-calc-total").Query<Label>().ToList()[1].text, Is.EqualTo(lastDmg.Final.ToString()), "남겨 둔 계산 = 앞 공격의 데미지");
                        Assert.That(stage.Root.Q("battle-calc-total").resolvedStyle.visibility, Is.EqualTo(Visibility.Visible));
                        held++;
                    }
                    if (hit != null && hit.PokemonId == foeId) Assert.That(screen.ShownMon(1).Hp, Is.EqualTo(hit.Before), "굴리는 동안 상대 체력은 앞 값");
                    for (int k = 0; k < 400 && screen.Rolling && screen.RollFaces.Count < want.Count; k++)
                    {
                        stage.Tweens.Advance(50);
                        yield return null;
                    }
                    Assert.That(screen.RollFaces, Is.EqualTo(want), "결과 줄의 면 = 엔진이 정한 면(면 바꾸기 뒤)");
                    Assert.That(screen.RollSum, Is.EqualTo(AttackDice.Sum(want)));
                    Assert.That(stage.Root.Q<Label>("battle-roll-sum").text, Is.EqualTo(AttackDice.Sum(want).ToString()));
                    Assert.That(screen.Open, Is.False);
                    rollsSeen++;
                    if (hit != null && hit.PokemonId == foeId && hit.Before != hit.After)
                    {
                        for (int k = 0; k < 600 && screen.ShownMon(1).Id == foeId && screen.ShownMon(1).Hp == hit.Before; k++)
                        {
                            Assert.That(screen.CalcHpShown, Is.False, "체력 줄은 착지 뒤에 뜬다");
                            Assert.That(screen.Open, Is.False, "연출 중 입력 닫힘");
                            stage.Tweens.Advance(50);
                            yield return null;
                        }
                        Assert.That(screen.ShownMon(1).Id, Is.EqualTo(foeId));
                        Assert.That(screen.ShownMon(1).Hp, Is.EqualTo(hit.After), "착지: 상대 체력이 새 값으로");
                        Assert.That(stage.Root.Q("battle-calc-total").Query<Label>().ToList()[1].text, Is.EqualTo(dmg.Final.ToString()), "계산 줄의 데미지");
                        Assert.That(stage.Root.Q("battle-calc-total").resolvedStyle.visibility, Is.EqualTo(Visibility.Visible));
                        Assert.That(screen.CalcHpShown, Is.True, "계산 줄 아래 체력 변화");
                        landings++;
                    }
                }
                flow.EventsSeen -= See;
                Assert.That(screen.ShowsResult, Is.True, "배틀이 끝나 결과 창이 떴다");
                Assert.That(myTurns, Is.GreaterThan(0));
                Assert.That(rollsSeen, Is.EqualTo(myTurns), "내 공격마다 굴림판이 떴다");
                Assert.That(landings, Is.GreaterThan(0), "체력 착지를 본 공격이 있다");
                Assert.That(held, Is.GreaterThan(0), "앞 공격의 계산을 남겨 둔 굴림이 있다(결정 70 ⑧)");
                Assert.That(busyPresses, Is.GreaterThan(0), "연출 중에 눌러 본 적이 있다(상대의 공격이 보이는 동안 내 선택은 이미 열려 있다)");
                Assert.That(screen.ShownLineCount, Is.EqualTo(flow.Battle.Lines.Count), "끝까지 보인 뒤에는 기록 줄이 다 붙어 있다");

                // ── 결과 창: [확인]을 누르기 전에는 배틀 화면이 그대로이고 턴 넘김이 기다린다 ──
                var view = client.View;
                Assert.That(view.Battle, Is.Null, "엔진은 이미 배틀을 끝냈다");
                Assert.That(view.Phase, Is.EqualTo(Phase.Turn));
                Assert.That(flow.Battle.Won, Is.EqualTo(wins), "찾아 둔 판의 승패");
                Assert.That(view.Seats[0].ActProgress, Is.EqualTo(wins ? 1 : 0), "이기면 1막 목표가 풀린다(A1)");
                Assert.That(stage.Root.Q("battle-result").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(stage.Root.Q<Label>("battle-result-title").text.Replace(KoText.WordJoiner.ToString(), ""), Is.EqualTo(wins ? "승리!" : "패배…"));
                var resultTexts = stage.Root.Q("battle-result-lines").Query<Label>().ToList().ConvertAll(l => l.text.Replace("⁠", ""));
                if (wins) Assert.That(resultTexts, Does.Contain(stage.T("screen.battle.result.goal.1")), "1막 목표 달성 줄");
                // M9 다듬기 P19: 이 배틀로 받은 스탬프(트레이너 승리 — 첫 스탬프)의 보상 규토리가 스탬프 줄 아래 아이콘 줄로 선다(빨강 · 파랑 · 노랑 +1)
                if (wins)
                {
                    Assert.That(flow.Battle.Stamps.Count, Is.EqualTo(1));
                    var stampRow = stage.Root.Q("battle-result-stamp-reward");
                    Assert.That(stampRow, Is.Not.Null, "스탬프 보상 줄");
                    Assert.That(stampRow.Q<Label>().text, Is.EqualTo(stage.T("screen.battle.result.stamp.reward")));
                    Assert.That(screen.ResultStampApricorns.ConvertAll(kv => kv.Key), Is.EqualTo(new[] { ApricornColor.Red, ApricornColor.Blue, ApricornColor.Yellow }));
                    foreach (var kv in screen.ResultStampApricorns)
                    {
                        Assert.That(kv.Value, Is.EqualTo(1));
                        var stampChip = stage.Root.Q("battle-result-stamp-apricorn-" + kv.Key.ToString().ToLowerInvariant());
                        Assert.That(stampChip, Is.Not.Null, kv.Key + " 칩");
                        Assert.That(stampChip.Q<Label>().text, Is.EqualTo("+1"));
                        Assert.That(stampChip.worldBound.xMax, Is.LessThanOrEqualTo(stage.Root.Q("battle-result-panel").worldBound.xMax), "칩이 결과 창 안에");
                    }
                }
                if (!wins) Assert.That(resultTexts, Does.Contain(stage.T("screen.battle.result.whiteout")), "고향 마을로 돌아가는 줄");
                if (!wins) Assert.That(stage.Root.Q("battle-result-stamp-reward"), Is.Null, "진 판에는 스탬프 보상 줄이 없다");
                Assert.That(flow.Fx.Sequencer.Paused, Is.True, "결과 창이 떠 있는 동안 화면 대기열은 서 있다");
                Assert.That(flow.Fx.Sequencer.Pending, Is.GreaterThan(0), "턴 넘김이 기다린다");
                for (int i = 0; i < 20; i++) yield return null;
                Assert.That(stage.Current, Is.SameAs(screen), "[확인] 전에는 닫히지 않는다");
                Assert.That(hud.Band.ActiveRow, Is.Not.EqualTo(FxRows.Handoff).And.Not.EqualTo(FxRows.MyTurn), "넘김 띠는 아직 돌지 않는다");
                Assert.That(screen.PressMove(0), Is.False, "끝난 배틀에는 답을 내지 않는다");
                Assert.That(flow.InputOpen, Is.False);
                Assert.That(stage.Root.Q<Button>("battle-result-ok"), Is.Not.Null);
                int afterBattle = flow.Session.CommandCount;
                // M9 다듬기 P18(94행 941): 이긴 조무래기 토큰은 결과 창이 떠 있는 동안 보드에 그대로 있다 — [확인] 뒤에 날아간다
                var tokens = Object.FindAnyObjectByType<BoardBoot>().Tokens;
                var gruntToken = tokens.GruntAt(home);
                Assert.That(gruntToken, Is.Not.Null, "배틀 동안 고향 마을의 조무래기 토큰은 그대로");
                float gruntRest = gruntToken.localPosition.y;
                bool lifted = false, chip = false, chipLanded = false;
                Assert.That(screen.Confirm(), Is.True);

                // ── 보드로: 배틀 화면이 닫히고 턴 넘김(띠 → 컷신) 뒤 다음 턴의 입력이 열린다 ──
                Assert.That(stage.Current, Is.Null, "배틀 화면이 닫혔다");
                Assert.That(flow.Battle, Is.Null);
                Assert.That(flow.Fx.Sequencer.Paused, Is.False);
                Assert.That(stage.Root.Q("battle"), Is.Null);
                Assert.That(flow.InputOpen, Is.False, "턴 넘김이 도는 동안 입력 닫힘");
                bool band = false;
                float t1 = Time.realtimeSinceStartup;
                while (!flow.InputOpen && Time.realtimeSinceStartup - t1 < 15f)
                {
                    if (hud.Band.ActiveRow == FxRows.Handoff || hud.Band.ActiveRow == FxRows.MyTurn) band = true;
                    var gone = tokens.GruntAt(home);
                    if (gone != null && gone.gameObject.activeSelf && gone.localPosition.y > gruntRest + 0.2f) lifted = true;
                    if (flow.Results != null && flow.Results.MoveShown)
                    {
                        chip = true;
                        Assert.That(stage.Root.Q<Label>("board-result-move-text").text, Is.EqualTo(stage.T("screen.board.goal.move_bonus")));
                        if (flow.Results.MoveFly >= 1f) chipLanded = true;
                    }
                    yield return null;
                }
                if (wins)
                {
                    Assert.That(lifted, Is.True, "조무래기 토큰이 날아올랐다(94행 941)");
                    Assert.That(chip, Is.True, "「이동력 +2」 칩이 떴다");
                    Assert.That(chipLanded, Is.True, "칩이 내 남은 이동력 배지에 닿았다");
                    Assert.That(tokens.GruntAt(home), Is.Null, "토큰은 사라졌다");
                    Assert.That(flow.Results.MoveShown, Is.False, "칩은 남지 않는다");
                    Assert.That(tokens.Dropping, Is.False, "토큰 자세를 되돌렸다");
                }
                else
                {
                    Assert.That(lifted || chip, Is.False, "진 판에는 해방 연출이 없다");
                    Assert.That(tokens.GruntAt(home), Is.Not.Null, "조무래기는 그대로");
                }
                Assert.That(band, Is.True, "배틀 뒤 턴 넘김 띠·컷신이 돈다(TurnEndReason.Battle)");
                Assert.That(flow.InputOpen, Is.True, "다음 턴: HUD 입력이 다시 열린다");
                Assert.That(rig.InputEnabled, Is.True);
                Assert.That(boot.Root.Stage.Tweens.Busy, Is.EqualTo(0), "연출이 끝나면 실행기가 빈다");
                view = client.View;
                Assert.That(view.Round, Is.EqualTo(firstRound + 1), "배틀로 턴이 끝나 다음 라운드(한 좌석 판)");
                Assert.That(client.Choice(ChoiceKind.MapAction), Is.Not.Null, "다음 턴의 행동 선택");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(afterBattle), "[확인]은 화면의 일이다 — 엔진에 답을 내지 않는다");
                Assert.That(stage.Root.Q<Label>("board-hud-round").text, Is.EqualTo((firstRound + 1) + "라운드"));
                Assert.That(view.Seats[0].Cell, Is.EqualTo(home), "고향 마을(이기면 그 자리 · 지면 돌아온 자리)");
                if (wins)
                {
                    Assert.That(view.Seats[0].MovesLeft, Is.EqualTo(firstMoves + 2), "1막 해방 뒤 매 턴 이동력 +2(A1)");
                    Assert.That(stage.Root.Q<Label>("board-hud-moves").text, Is.EqualTo("남은 이동력 " + (firstMoves + 2)));
                    Assert.That(hud.Acts, Does.Not.Contain(BoardHud.Challenge), "조무래기를 이긴 뒤에는 도전할 상대가 없다");
                    Assert.That(stage.Root.Q<Label>("board-hud-act").text, Is.EqualTo(stage.T("screen.board.act." + view.Hos.Act)));
                }
                else
                {
                    Assert.That(view.Seats[0].MovesLeft, Is.EqualTo(firstMoves));
                    Assert.That(PokemonRules.Hp(flow.Ctx, view.Pokemon[view.Seats[0].Party[0]]), Is.GreaterThan(0), "고향 마을에서 회복해 다음 턴을 시작한다");
                    Assert.That(hud.Acts, Does.Contain(BoardHud.Challenge), "다시 도전할 수 있다");
                }
                Assert.That(hud.Reach.Count, Is.GreaterThan(0), "갈 수 있는 칸이 다시 칠해진다");

                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                foreach (var t in targets)
                {
                    t.Release();
                    Object.Destroy(t);
                }
            }
        }
    }
}
