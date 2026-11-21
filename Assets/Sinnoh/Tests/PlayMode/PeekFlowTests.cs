using System;
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
    /// M9 플레이 시험 T35 · 결정 125 · T21: 물음 창(6장째 버리기 · 축복 물음)이 떠 있어도 게임 메뉴와 HUD 단추가 눌리고, 정보 창(정비 · 도감 · 설명서)은
    /// 그 위 정보 창 층에 보기만 하는 창으로 열린다 — 닫으면 아래 창이 그대로이고 답은 나가지 않는다. 배틀 위에서도 같다. 축복 물음을 접으면 보드 카메라가 움직인다.
    /// </summary>
    public sealed class PeekFlowTests
    {
        const int W = 1920, H = 1080;
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
            var b = Object.FindAnyObjectByType<ScreenBoot>();
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                rt = null;
            }
        }

        IEnumerator Boot()
        {
            errors.Clear();
            Application.logMessageReceived += Grab;
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = Object.FindAnyObjectByType<ScreenBoot>();
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
        }

        IEnumerator Until(Func<bool> until, string what)
        {
            for (int i = 0; i < 600 && !until(); i++)
            {
                flow.Skip(100);
                yield return null;
            }
            Assert.That(until(), Is.True, what + " — 화면 " + (stage.Current != null ? stage.Current.Name : "보드"));
        }

        // HUD 단추가 맨 위에서 눌리나(그 자리의 맨 위 요소가 그 단추이거나 그 안)
        void Pickable(string name, string where)
        {
            var button = stage.Root.Q(name);
            Assert.That(button, Is.Not.Null, name);
            var hit = stage.Root.panel.Pick(button.worldBound.center);
            Assert.That(hit != null && (hit == button || button.Contains(hit)), Is.True, where + ": " + name + "이 눌린다 — 맨 위 " + (hit != null ? hit.name + "/" + hit.GetType().Name : "없음"));
        }

        // 정보 창을 정보 창 층에 열고 Esc로 닫는다 — 아래 창(under)은 그대로
        void PeekAndBack(Func<bool> open, Type kind, IUiScreen under, string where)
        {
            Assert.That(open(), Is.True, where + ": " + kind.Name + " 열기");
            Assert.That(stage.Info, Is.InstanceOf(kind), where + ": 정보 창 층에");
            Assert.That(stage.Current, Is.SameAs(under), where + ": 아래 창은 그대로");
            Assert.That(flow.InputOpen, Is.False);
            Assert.That(flow.Escape(), Is.True, where + ": Esc = 정보 창 닫기");
            Assert.That(stage.Info, Is.Null, where + ": 닫혔다");
            Assert.That(stage.Current, Is.SameAs(under), where + ": 아래 창으로 돌아왔다");
        }

        [UnityTest]
        public IEnumerator M9_T35_물음창과배틀위에서도게임메뉴와정보창이열리고닫으면아래창이그대로다()
        {
            yield return Boot();
            var title = (TitleScreen)stage.Current;
            int soloIndex = -1;
            for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Solo) soloIndex = i;
            Assert.That(title.Activate(soloIndex), Is.True);
            yield return null;
            var pilot = FlowPilot.Start(flow, SoloTrail.Fate);
            yield return null;
            Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");

            bool asked = false, battled = false;
            float t0 = Time.realtimeSinceStartup;
            for (int guard = 0; guard < 40000 && Time.realtimeSinceStartup - t0 < 240f && !battled; guard++)
            {
                flow.Skip(150);
                yield return null;
                // ── 6장째 버리기(보드의 물음 창): 위 띠 [메뉴] · 아래 판 [정비]가 맨 위에서 눌린다 ──
                if (!asked && stage.Current is FateScreen f && f.Discarding && flow.BoardOpen && stage.Info == null)
                {
                    for (int i = 0; i < 3; i++) yield return null;
                    int commands = flow.Session.CommandCount;
                    Pickable("board-hud-menu", "버리기 창");
                    Pickable("board-hud-maint", "버리기 창");
                    Pickable("board-hud-dex", "버리기 창");

                    // 게임 메뉴: 물음 창 위에 열리고(대기열은 선다) Esc로 닫으면 물음 창이 그대로
                    flow.OpenMenu();
                    Assert.That(flow.MenuUp, Is.True, "버리기 창 위의 게임 메뉴");
                    Assert.That(stage.Info, Is.InstanceOf<BoardMenuScreen>());
                    Assert.That(stage.Current, Is.SameAs(f));
                    Assert.That(flow.OpenParty(), Is.False, "메뉴 아래로는 정보 창을 열지 않는다");
                    flow.OpenMenuSettings();
                    Assert.That(stage.Info is SettingsScreen set && set.InGame, Is.True, "메뉴에서 연 설정도 같은 층");
                    Assert.That(flow.Escape(), Is.True);
                    Assert.That(stage.Info, Is.InstanceOf<BoardMenuScreen>(), "설정 → 메뉴");
                    Assert.That(flow.Escape(), Is.True);
                    Assert.That(flow.MenuUp, Is.False);
                    Assert.That(stage.Info, Is.Null);
                    Assert.That(stage.Current, Is.SameAs(f));
                    Assert.That(f.Discarding, Is.True, "물음은 그대로");

                    // 정비 판: 보기만(잠긴 까닭 한 줄) — 카드 칸 · 단추가 없다
                    Assert.That(flow.OpenParty(), Is.True);
                    var party = (PartyScreen)stage.Info;
                    Assert.That(party.Lock, Is.EqualTo("screen.peek.lock.window"));
                    Assert.That(party.GuideText, Is.EqualTo(stage.T("screen.peek.lock.window")));
                    Assert.That(stage.Root.Q("board-party-release-0"), Is.Null, "놓아주기 · 순서 단추가 없다");
                    Assert.That(party.PickMove(0), Is.False, "기술 카드를 집지 못한다");
                    Assert.That(flow.Escape(), Is.True);
                    Assert.That(stage.Current, Is.SameAs(f));

                    PeekAndBack(flow.OpenDex, typeof(DexScreen), f, "버리기 창");
                    PeekAndBack(flow.ToggleBookSide, typeof(RulebookScreen), f, "버리기 창");
                    Pickable("board-hud-types", "버리기 창");
                    PeekAndBack(flow.ToggleTypes, typeof(TypeChartScreen), f, "버리기 창");   // 타입 상성 · 상태 이상(T5)
                    Assert.That(flow.OpenFate(), Is.True, "손패 창이 이미 떠 있다");
                    Assert.That(stage.Info, Is.Null, "두 번 열지 않는다");
                    Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "여닫기만 했다 — 답은 나가지 않았다");
                    Assert.That(f.Discarding, Is.True);
                    asked = true;
                }
                // ── 배틀(연출 중 — 정비를 못 하는 때): 메뉴 · 보기만 하는 정비 판 · 도감 · 손패 ──
                if (asked && stage.Current is BattleScreen b && !b.Ready && b.Over == null && stage.Info == null && flow.Battle != null && !flow.Battle.Ended)
                {
                    int commands = flow.Session.CommandCount;
                    Pickable("board-hud-menu", "배틀");
                    flow.OpenMenu();
                    Assert.That(flow.MenuUp && stage.Info is BoardMenuScreen && stage.Current == b, Is.True, "배틀 위의 게임 메뉴");
                    flow.CloseMenu();
                    Assert.That(stage.Info, Is.Null);
                    Assert.That(flow.MenuUp, Is.False);
                    Assert.That(flow.OpenParty(), Is.True);
                    Assert.That(((PartyScreen)stage.Info).Lock, Is.EqualTo("screen.peek.lock.battle"));
                    Assert.That(b.Over, Is.Null, "배틀 화면의 위 창이 아니라 정보 창 층");
                    Assert.That(flow.Escape(), Is.True);
                    Assert.That(stage.Current, Is.SameAs(b));
                    PeekAndBack(flow.OpenDex, typeof(DexScreen), b, "배틀");
                    PeekAndBack(flow.OpenFate, typeof(FateScreen), b, "배틀");
                    PeekAndBack(flow.ToggleTypes, typeof(TypeChartScreen), b, "배틀");
                    Assert.That(flow.Session.CommandCount, Is.EqualTo(commands));
                    battled = true;
                    break;
                }
                FlowPilot.Step(flow, pilot);
            }
            Assert.That(asked, Is.True, "버리기 창 위에서 열어 봤다");
            Assert.That(battled, Is.True, "배틀 위에서 열어 봤다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M9_T21_축복물음을접으면보드카메라가움직이고_아래창이닫히면정보창이화면자리로옮겨간다()
        {
            yield return Boot();
            flow.StartSeats(new SoloSettings { Campaign = Campaign.UltraBurst }, 1, 20261117, 7);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            yield return Until(() => flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null, "내 턴");
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "턴 종료");
            yield return Until(() => flow.Top is BlessScreen q && !q.IsPanel && flow.BoardOpen, "라운드 끝의 「UB 턴을 건너뛰겠습니까?」");
            for (int i = 0; i < 3; i++) yield return null;
            var ask = (BlessScreen)stage.Current;
            var rig = Object.FindAnyObjectByType<BoardCameraRig>();
            Assert.That(rig, Is.Not.Null);
            Pickable("board-hud-menu", "축복 물음");
            Assert.That(rig.InputEnabled, Is.False, "펼친 물음 창 아래 보드 카메라는 서 있다");
            Assert.That(flow.OverUi(W / 2f, H / 2f), Is.False, "창 뿌리는 판 밖의 입력을 먹지 않는다(T35)");

            // 접으면 카메라가 움직인다(T21) — 펼치면 다시 선다
            Assert.That(ask.ToggleFold(), Is.True);
            yield return null;
            Assert.That(flow.Folded, Is.True);
            Assert.That(rig.InputEnabled, Is.True, "접은 동안 보드 카메라(Q · E · 휠 · 끌기)");
            Assert.That(flow.InputOpen, Is.False, "칸 고르기 · 행동은 그대로 닫혀 있다");
            Assert.That(ask.ToggleFold(), Is.True);
            yield return null;
            Assert.That(flow.Folded, Is.False);
            Assert.That(rig.InputEnabled, Is.False);

            // 물음 위에 연 정비 판(보기만)은 물음에 답해 창이 닫히면 화면 자리로 옮겨 조작을 받는다
            int commands = flow.Session.CommandCount;
            Assert.That(flow.OpenParty(), Is.True);
            var party = (PartyScreen)stage.Info;
            Assert.That(party.Lock, Is.EqualTo("screen.peek.lock.window"));
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands));
            var choice = BlessScreen.ChoiceOf(flow.Client);
            Assert.That(choice, Is.Not.Null);
            var no = flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.Decline);
            Assert.That(no, Is.Not.Null, "건너뛰지 않는다");
            Assert.That(flow.Answer(choice, no).Accepted, Is.True);
            yield return null;
            Assert.That(stage.Current is BlessScreen, Is.False, "물음 창이 닫혔다");
            Assert.That(stage.Info, Is.Null, "정보 창 층이 비었다");
            Assert.That(stage.Current, Is.SameAs(party), "정비 판이 화면 자리로");
            Assert.That(party.Lock, Is.Null, "이제 조작을 받는다");
            flow.CloseParty();
            Assert.That(stage.Current, Is.Null);

            // 보드에서 [상성](T5): 화면 자리에 — 상성표는 규칙 표 그대로(불꽃 → 풀 +2 · 노말 → 고스트 -4 · 보통 = 빈 칸), 오른쪽에 설명서의 상태 이상 표
            Assert.That(flow.ToggleTypes(), Is.True);
            yield return null;
            Assert.That(stage.Current, Is.InstanceOf<TypeChartScreen>());
            Assert.That(stage.Root.Q(TypeChartScreen.CellName(PokemonType.Fire, PokemonType.Grass)).Q<Label>().text, Is.EqualTo("+2"));
            Assert.That(stage.Root.Q(TypeChartScreen.CellName(PokemonType.Normal, PokemonType.Ghost)).Q<Label>().text, Is.EqualTo("-4"));
            Assert.That(stage.Root.Q(TypeChartScreen.CellName(PokemonType.Water, PokemonType.Grass)).Q<Label>().text, Is.EqualTo("-2"));
            Assert.That(stage.Root.Q(TypeChartScreen.CellName(PokemonType.Normal, PokemonType.Normal)).Q<Label>(), Is.Null);
            Assert.That(stage.Root.Q("board-types-side").Q(className: "sn-book-table"), Is.Not.Null, "상태 이상 표(설명서 battle.status)");
            // 결정 143 ②③: 표 위 설명 줄 없이 모서리 칸에 축 이름 · 상태 이상 아이콘은 줄마다 이름 앞(아래 아이콘 띠 없음)
            Assert.That(stage.Root.Q("board-types-axis"), Is.Null, "상성표 위 설명 줄은 없다");
            Assert.That(stage.Root.Q("board-types-corner-attack"), Is.Not.Null, "모서리 칸 「공격」");
            Assert.That(stage.Root.Q("board-types-corner-defend"), Is.Not.Null, "모서리 칸 「방어」");
            var side = stage.Root.Q("board-types-side");
            Assert.That(side.Query(className: "sn-book-td-icon").ToList().Count, Is.EqualTo(6), "상태 이상 줄 머리 아이콘 6");
            Assert.That(side.Q(className: "sn-book-figure"), Is.Null, "표 아래 아이콘 띠는 없다");
            Assert.That(flow.ToggleTypes(), Is.False, "한 번 더 누르면 닫는다");
            Assert.That(stage.Current, Is.Null);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // MouseProbe 단계 Peek(T35 · 결정 125 · T21)가 지나는 판과 자리: 같은 판(MouseProbe.StartPeekGame)에서 축복 물음(첫 라운드 끝) → 균열 카드 창(UB 턴 뒤 내 턴 시작)이 차례로 열리고,
        // 그때 프로브가 누를 [메뉴] · [상성] · [정비] · [도감]이 맨 위에서 눌리며, 접은 축복 물음 아래 화면 가운데(끌기 시작점)는 PC 층이 받지 않는다. 실제 마우스 길은 묶음 끝 감사의 window-probe
        [UnityTest]
        public IEnumerator M9_T35_마우스확인판_균열창과축복물음위의단추가맨위에서눌리고접으면가운데가비어있다()
        {
            yield return Boot();
            MouseProbe.StartPeekGame(flow, 20261112, 7);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            var rig = Object.FindAnyObjectByType<BoardCameraRig>();
            Assert.That(rig, Is.Not.Null);
            var seen = new List<string>();
            for (int guard = 0; guard < 1500 && seen.Count < 2; guard++)
            {
                if (!seen.Contains("breach") && flow.Top is BreachScreen breach && flow.BoardOpen)
                {
                    Assert.That(seen, Does.Contain("bless"), "축복 물음(첫 라운드 끝)이 먼저 — 균열 카드 창은 UB 턴 뒤 내 턴 시작에");
                    for (int i = 0; i < 3; i++) yield return null;
                    foreach (var b in new[] { "board-hud-menu", "board-hud-types", "board-hud-maint", "board-hud-dex" }) Pickable(b, "균열 카드 창");
                    var attach = flow.Client.Legal(BreachScreen.ChoiceOf(flow.Client))[0];
                    Assert.That(flow.Answer(BreachScreen.ChoiceOf(flow.Client), attach).Accepted, Is.True, "붙이기");
                    seen.Add("breach");
                }
                else if (!seen.Contains("bless") && flow.Top is BlessScreen ask && !ask.IsPanel && flow.BoardOpen)
                {
                    for (int i = 0; i < 3; i++) yield return null;
                    foreach (var b in new[] { "board-hud-menu", "board-hud-types", "board-hud-maint", "board-hud-dex" }) Pickable(b, "축복 물음");
                    Assert.That(ask.ToggleFold(), Is.True);
                    yield return null;
                    Assert.That(flow.OverUi(W / 2f, H / 2f), Is.False, "접은 물음 아래 화면 가운데(끌기 시작점)");
                    Assert.That(rig.InputEnabled, Is.True);
                    Pickable("board-bless-fold", "접은 축복 물음");
                    Assert.That(ask.ToggleFold(), Is.True);
                    yield return null;
                    var choice = BlessScreen.ChoiceOf(flow.Client);
                    Assert.That(flow.Answer(choice, flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.Decline)).Accepted, Is.True, "건너뛰지 않음");
                    seen.Add("bless");
                }
                else if (flow.Top == null && flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null && flow.Hud.Shows(BoardHud.EndTurn))
                    Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "턴 종료");
                flow.Skip(300);
                yield return null;
            }
            Assert.That(seen, Is.EqualTo(new[] { "bless", "breach" }), "MouseProbe.PeekSteps의 차례(" + string.Join(", ", MouseProbe.PeekSteps) + ")");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // MouseProbe 단계 FlyMap(T18 ① — MouseProbe.FlyMapSteps)이 지나는 판과 자리: 같은 판(MouseProbe.StartFlyGame — 고향이 아닌 마을 · 빨강 규토리)의 첫 턴에 [마을]이 맨 위에서 눌리고
        // 공중날기가 열려 [지도에서 고르기] → 화면에 보이는 갈 수 있는 마을 칸이 있고(창 위가 아님) 그 점을 누르면 목적지 · 답 없음 → [취소] = 창 꼴 · 카메라 되돌림. 실제 마우스 길은 묶음 끝 감사의 window-probe
        [UnityTest]
        public IEnumerator M9_T18_마우스확인판_첫턴마을에서공중날기가열리고보드의마을칸을눌러고른뒤취소하면카메라가돌아온다()
        {
            yield return Boot();
            MouseProbe.StartFlyGame(flow, 20261112, 7);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            yield return Until(() => flow.InputOpen && flow.Hud != null && flow.Hud.Shows(BoardHud.Village), "옮겨 둔 마을의 [마을]");
            var rig = Object.FindAnyObjectByType<BoardCameraRig>();
            var board = Object.FindAnyObjectByType<BoardBoot>();
            for (int i = 0; i < 3; i++) yield return null;
            Pickable("board-hud-act-" + BoardHud.Village, "보드");
            Assert.That(flow.Hud.Press(BoardHud.Village), Is.True);
            yield return null;
            var village = (VillageScreen)stage.Current;
            Assert.That(village.CanFly, Is.True, "첫 턴에도 공중날기가 열린다(조무래기 없는 마을 · 규토리)");
            for (int i = 0; i < 3; i++) yield return null;
            Pickable("board-village-map", "마을 창");
            var look = flow.SaveLook();
            Assert.That(village.BeginMap(), Is.True, "[지도에서 고르기]");
            for (int i = 0; i < 4; i++) yield return null;
            Assert.That(flow.FlyPick, Is.SameAs(village));
            Assert.That(flow.BoardMarks.Dims.Count, Is.GreaterThan(0), "지금 선 마을 · 조무래기가 선 고향 마을은 흐리게");
            var fixtures = flow.Ctx.Fixtures.Fixtures;
            var cam = Camera.main;
            int picked = -1;
            Vector2 px = default;
            foreach (int f in village.Dests)
            {
                if (!village.CanGo(f)) continue;
                int cell = fixtures[f].Cell;
                if (!BoardPick.Screen(board, cam, cell, W, H, out var p) || p.x < 0 || p.x >= W || p.y < 0 || p.y >= H || flow.OverUi(p.x, p.y)) continue;
                if (BoardPick.Cell(board, cam, p.x, p.y, W, H) != cell) continue;
                picked = f;
                px = p;
                break;
            }
            Assert.That(picked, Is.GreaterThanOrEqualTo(0), "판 전체 줌에 창에 가리지 않은 갈 수 있는 마을 칸이 있다");
            int commands = flow.Session.CommandCount;
            Assert.That(flow.Pointer(px.x, px.y, false), Is.EqualTo(fixtures[picked].Cell));
            flow.Pointer(px.x, px.y, true);
            flow.Pointer(px.x, px.y, false);
            Assert.That(village.Dest, Is.EqualTo(picked), "보드에서 누른 마을이 목적지");
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "고르기만 한다");
            yield return null;
            Pickable("board-village-cancel", "보드에서 고르기");
            Assert.That(village.EndMap(), Is.True, "[취소]");
            yield return null;
            Assert.That(village.Picking, Is.False);
            Assert.That((rig.CurrentZoom, rig.Yaw), Is.EqualTo((look.Zoom, look.Yaw)), "카메라 줌 · 방향이 고르기 전으로");
            Assert.That(Vector3.Distance(rig.Target, look.Target), Is.LessThan(1e-3f), "보는 점이 고르기 전으로");
            Assert.That(MouseProbe.FlyMapSteps, Is.EqualTo(new[] { "fly-map-open", "fly-map-click", "fly-map-cancel" }));
            flow.CloseVillage();
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
