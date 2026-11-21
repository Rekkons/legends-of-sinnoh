using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
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
    /// 장소·카드(plan §4 「장소·카드」, 04 §3-9): 마을 창(S23 — 전체 회복 · 공중날기 S25) · 숨겨진 장소(S34 — 묻는 동안 뜬 토큰) · 트레이너 카드 판(S40)과
    /// 보드 쪽 화면 묶음 전체의 흐름(타이틀 → 솔로 → 셋업 → 막 카드 → 보드에서 [트레이너 카드]·[정비]·기록·마을 창 → 찾아보기 → 야생 배틀 → 1막 조무래기 배틀까지).
    /// 화면의 함수로만 누른다. Gen 겉모양·무대 그림이 있어야 돈다.
    /// </summary>
    public sealed class PlaceFlowTests
    {
        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < end) yield return null;
        }

        sealed class Rig
        {
            public ScreenBoot Boot;
            public GameFlow Flow;
            public UiStage Stage;
            public BoardBoot Board;
            public BoardCameraRig Camera;
            public RenderTexture Rt;
        }

        static IEnumerator Open(Rig rig)
        {
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                rig.Boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (rig.Boot != null && rig.Boot.Root != null && rig.Boot.Root.Stage.Root != null) break;
            }
            Assert.That(rig.Boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(rig.Boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            rig.Flow = rig.Boot.Flow;
            rig.Boot.PointerFromMouse = false;
            rig.Stage = rig.Boot.Root.Stage;
            rig.Board = Object.FindAnyObjectByType<BoardBoot>();
            rig.Camera = UnityEngine.Camera.main.GetComponent<BoardCameraRig>();
            rig.Rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rig.Rt.Create();
            rig.Boot.Root.SetTarget(rig.Rt);
            for (int i = 0; i < 4; i++) yield return null;
        }

        static void Shut(Rig rig)
        {
            var boot = Object.FindAnyObjectByType<ScreenBoot>();
            if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
            if (rig.Rt != null)
            {
                rig.Rt.Release();
                Object.Destroy(rig.Rt);
            }
        }

        static IEnumerator WaitInput(GameFlow flow)
        {
            for (int i = 0; i < 200 && !flow.InputOpen; i++)
            {
                flow.Skip(100);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator M4_타이틀에서셋업막카드보드의카드판정비기록마을창을지나찾아보기와야생배틀뒤조무래기배틀까지에러로그0()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            var rig = new Rig();
            try
            {
                yield return Open(rig);
                var flow = rig.Flow;
                var stage = rig.Stage;
                var tokens = rig.Board.Tokens;

                // ── 타이틀 → 혼자 하기(S09) ──
                Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
                var title = (TitleScreen)stage.Current;
                int soloIndex = -1;
                for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Solo) soloIndex = i;
                Assert.That(title.Activate(soloIndex), Is.True);
                yield return null;
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<SoloScreen>());

                // ── 셋업(S12·S13): 찾아 둔 판(실제 진행 — 손본 것 없음)을 화면으로 끝낸다 ──
                var trail = SoloTrail.Defeat;
                var pilot = FlowPilot.Start(flow, trail);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");
                var client = flow.Client;
                var hud = flow.Hud;
                int home = flow.Ctx.Fixtures.Fixtures[client.View.Seats[0].HomeVillage].Cell;

                // ── 막 카드(S14): 서막 카드가 뜨는 동안 창은 열리지 않는다 ──
                stage.Tweens.Advance(1600);
                yield return null;
                Assert.That(hud.ActCard.Active, Is.True, "서막 카드");
                Assert.That(stage.Root.Q<Button>("board-hud-trainer").ClassListContains(UiStage.OffClass), Is.True, "막 카드가 도는 동안의 [트레이너 카드]는 흐린 단추");
                Assert.That(flow.OpenTrainer(), Is.True, "그래도 보기만 하는 판은 열린다(결정 125 — 정보 창 층)");
                Assert.That(stage.Info, Is.InstanceOf<TrainerScreen>());
                flow.CloseTrainer();
                Assert.That(stage.Info, Is.Null);
                Assert.That(flow.OpenVillage(), Is.False, "입력이 닫혀 있으면 마을 창도 열리지 않는다");
                flow.Skip(flow.OpeningMs);
                yield return WaitInput(flow);
                Assert.That(flow.InputOpen, Is.True, "첫 턴: 입력 열림");

                // ── 보드 HUD: 회색 단추가 없다. [마을]·[도전]·[턴 종료] ──
                Assert.That(hud.Acts, Is.EqualTo(new[] { BoardHud.Village, BoardHud.Craft, BoardHud.Challenge, BoardHud.EndTurn }), "고향 마을의 첫 턴(1막 보상 빨강 규토리 3개로 몬스터볼을 만들 수 있다 — [제작], M5)");
                Assert.That(stage.Root.Q("board-hud-buttons").Q(className: "sn-hud-off"), Is.Null, "회색 단추는 없다");
                Assert.That(stage.Root.Q<Button>("board-hud-act-" + BoardHud.Village).text, Is.EqualTo("마을"));

                // ── S40 트레이너 카드 판: 브론즈 — 능력 둘 다 잠김(rulings A20-2), 보기만 ──
                var cardButton = stage.Root.Q<Button>("board-hud-trainer");
                Assert.That(cardButton.enabledSelf, Is.True);
                Assert.That(cardButton.text, Is.EqualTo("트레이너 카드"));
                int commands = flow.Session.CommandCount;
                Assert.That(flow.OpenTrainer(), Is.True);
                yield return null;
                var trainer = (TrainerScreen)stage.Current;
                var cardDef = flow.Ctx.Cards.TrainerCards[client.View.Seats[0].TrainerCard];
                Assert.That(trainer.Tier, Is.EqualTo(1));
                Assert.That(stage.Root.Q<Label>("board-trainer-name").text, Is.EqualTo(stage.T("trainer_card." + cardDef.Key)));
                Assert.That(stage.Root.Q<Label>("board-trainer-tier").text, Is.EqualTo("브론즈 ★☆☆"));
                Assert.That(stage.Root.Q("board-trainer-art").Q(className: "sn-trainer-art").resolvedStyle.backgroundImage.texture, Is.Not.Null, "직업 그림");
                for (int slot = 1; slot <= 2; slot++)
                {
                    var block = stage.Root.Q("board-trainer-ability-" + slot);
                    Assert.That(block.ClassListContains("sn-trainer-locked"), Is.True, "브론즈: 능력 " + slot + " 잠김");
                    Assert.That(stage.Root.Q<Label>("board-trainer-ability-" + slot + "-lock").text, Is.EqualTo("잠김"));
                    string text = stage.Root.Q<Label>("board-trainer-ability-" + slot + "-text").text.Replace("⁠", "");
                    Assert.That(text, Is.EqualTo(stage.T("trainer_ability." + cardDef.Key + "." + slot + ".text")), "능력 문구는 줄로 다 적는다");
                }
                Assert.That(stage.Root.Q("board-trainer-next").Q<Label>().text.Replace("⁠", ""), Is.EqualTo("적당한 포켓몬 1마리를 쓰러뜨리거나 얻으면 실버가 됩니다."));
                Assert.That(TrainerScreen.Unlocked(2, 1) && !TrainerScreen.Unlocked(2, 2) && TrainerScreen.Unlocked(3, 2), Is.True, "파랑은 실버, 빨강은 골드에서 해금");
                Assert.That(flow.InputOpen, Is.False, "판이 떠 있는 동안 보드 입력은 꺼진다");
                flow.CloseTrainer();
                yield return null;
                Assert.That(stage.Current, Is.Null);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "보기만 한다 — 답을 내지 않는다");

                // ── [정비](S18) · 기록(S21): 열고 닫는다 ──
                Assert.That(flow.OpenParty(), Is.True);
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<PartyScreen>());
                flow.CloseParty();
                yield return null;
                flow.OpenLog();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<LogScreen>());
                Assert.That(((LogScreen)stage.Current).Total, Is.GreaterThan(3), "셋업·막·조무래기 줄");
                flow.CloseLog();
                yield return null;
                Assert.That(stage.Current, Is.Null);
                Assert.That(flow.InputOpen, Is.True);

                // ── S23 마을 창: 조무래기가 점거한 고향 마을 — [전체 회복]은 되고 공중날기는 까닭과 함께 막혀 있다 ──
                Assert.That(hud.Press(BoardHud.Village), Is.True);
                yield return null;
                var village = (VillageScreen)stage.Current;
                Assert.That(stage.Root.Q<Label>("board-village-title").text, Is.EqualTo(stage.T(flow.Ctx.Fixtures.Fixtures[client.View.Seats[0].HomeVillage].Key)));
                Assert.That(stage.Root.Q("board-village-grunt"), Is.Not.Null, "조무래기 점거 표시");
                Assert.That(village.CanHeal, Is.True);
                Assert.That(village.HealWhy, Is.Null);
                Assert.That(stage.Root.Q<Button>("board-village-heal").enabledSelf, Is.True);
                Assert.That(village.CanFly, Is.False);
                Assert.That(village.FlyWhy, Is.EqualTo("screen.village.fly.grunt"));
                Assert.That(stage.Root.Q<Label>("board-village-fly-why").text, Is.EqualTo("조무래기가 점거 중인 마을에서는 사용할 수 없습니다."));
                Assert.That(stage.Root.Q<Button>("board-village-fly").enabledSelf, Is.False);
                Assert.That(village.Dests.Count, Is.EqualTo(7), "본맵 마을 8 − 지금 선 마을(울트라메가로폴리스는 없다)");
                foreach (int f in village.Dests) Assert.That(village.PickDest(f), Is.False, "막힌 동안에는 목적지를 고르지 못한다");
                Assert.That(village.PressFly(), Is.False);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands));
                // 회복 소리를 셀 자리는 [전체 회복] 전부터(회복 단계는 다음 프레임에 시작한다)
                var sound = rig.Boot.Sound;
                Assert.That(sound.Effects.Find(SoundKeys.Center), Is.Not.Null, "Gen 효과음 표(build.mjs --only se)");
                int heardAt = sound.Heard.Count;
                Assert.That(village.PressHeal(), Is.True, "[전체 회복]");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1), "회복 답 하나");
                yield return null;
                Assert.That(stage.Current, Is.SameAs(village), "회복한 뒤에도 창은 그대로다");
                Assert.That(village.CanHeal, Is.False, "턴당 1회");
                Assert.That(village.HealWhy, Is.EqualTo("screen.village.used"));
                Assert.That(stage.Root.Q<Label>("board-village-heal-why").text, Is.EqualTo("이번 턴에 이미 사용했습니다."));
                Assert.That(stage.Root.Q("board-hud-used").Query<Label>().ToList().Exists(l => l.text == "전체 회복"), "아래 판 「이번 턴」에 전체 회복");
                // 회복(17행)에서 파티 칸이 반짝일 때 HUD가 회복 반짝임을 찍는다(결정 69) · 회복 소리는 다 찰 때 한 번만(결정 73 ⑥ — 건너뛰는 연출의 소리도 내고(SoundOnSkip), 재생 층의 「한 프레임에 같은 키 한 번」은 Tick으로 프레임마다 연다)
                flow.SoundOnSkip = true;
                bool healFx = false;
                for (int i = 0; i < 400 && (!healFx || !flow.Fx.Sequencer.IsEmpty); i++)
                {
                    flow.Skip(20);
                    sound.Tick(20);
                    yield return null;
                    healFx |= hud.Sheets != null && hud.Sheets.Shown.Count > 0 && hud.Sheets.Shown[0].StartsWith(SheetBeats.Heal + "#");
                }
                flow.SoundOnSkip = false;
                Assert.That(healFx, Is.True, "[전체 회복]의 반짝임(BoardHud.Sheets)");
                Assert.That(flow.Fx.Sequencer.IsEmpty, Is.True, "회복 연출이 끝났다");
                int heals = 0;
                for (int i = heardAt; i < sound.Heard.Count; i++) if (sound.Heard[i].Kind == "se" && sound.Heard[i].Key == SoundKeys.Center) heals++;
                Assert.That(heals, Is.EqualTo(1), "회복 소리는 다 찰 때 한 번(결정 73 ⑥ D3 나)");
                flow.CloseVillage();
                yield return WaitInput(flow);
                Assert.That(stage.Current, Is.Null);
                Assert.That(flow.InputOpen, Is.True, "창을 닫으면 보드 입력이 다시 열린다");
                Assert.That(hud.Shows(BoardHud.Village), Is.True, "[마을]은 다 쓴 뒤에도 남는다(까닭을 창에서 읽는다)");

                // ── 찾아보기 → 조우 창(S45) → 야생 배틀 → 보드 → 고향 마을에서 [도전] → 1막 조무래기 배틀까지(길잡이가 화면으로 누른다) ──
                bool encounterSeen = false, wildSeen = false, galactic = false;
                int spotAsks = 0, greyButtons = 0, villageButtons = 0;
                for (int guard = 0; guard < 12000 && !galactic; guard++)
                {
                    if (stage.Current is EncounterScreen) encounterSeen = true;
                    if (stage.Current is BattleScreen && flow.Battle != null)
                    {
                        if (flow.Battle.Kind == BattleKind.Galactic) { galactic = true; break; }
                        wildSeen = true;
                    }
                    if (flow.InputOpen)
                    {
                        if (stage.Root.Q("board-hud-buttons").Q(className: "sn-hud-off") != null) greyButtons++;
                        if (hud.Shows(BoardHud.Village))
                        {
                            Assert.That(flow.AtVillage(out _, out _), Is.True, "[마을]은 마을 행동을 하는 곳에서만");
                            villageButtons++;
                        }
                        else if (client.Choice(ChoiceKind.MapAction) != null) Assert.That(flow.AtVillage(out _, out _), Is.False);
                        var ask = client.Choice(ChoiceKind.HiddenSpot);
                        if (ask != null)
                        {
                            // S34: 멈춰 서서 묻는 동안 그 칸의 토큰이 말 머리 위에 떠 있다(정체와 무관하게 뒷면 그대로)
                            spotAsks++;
                            Assert.That(tokens.RaisedCell, Is.EqualTo(client.View.Seats[0].Cell), "묻는 동안 토큰이 떠 있다");
                            Assert.That(tokens.RaisedHeight, Is.EqualTo(BoardTokens.HoverHeight).Within(1e-4f));
                            Assert.That(tokens.RaisedFlip, Is.EqualTo(0f));
                            Assert.That(hud.Acts, Is.EqualTo(new[] { BoardHud.Reveal, BoardHud.Pass }));
                        }
                        else Assert.That(tokens.RaisedCell, Is.EqualTo(-1), "묻지 않을 때는 모든 토큰이 제자리");
                    }
                    if (!FlowPilot.Step(flow, pilot)) flow.Skip(250);
                    yield return null;
                }
                Assert.That(galactic, Is.True, trail.Name + ": 조무래기 배틀까지 가지 못했다 — 화면 " + (stage.Current != null ? stage.Current.Name : "보드") + ", 선택 "
                    + (client.FirstChoice() != null ? client.FirstChoice().Kind + "/" + client.FirstChoice().Sub : "없음") + ", 받을 화면 없는 선택 " + flow.NotReadyShown + "번");
                Assert.That(encounterSeen, Is.True, "찾아보기 뒤 조우 창");
                Assert.That(wildSeen, Is.True, "야생 배틀");
                Assert.That(villageButtons, Is.GreaterThan(0));
                Assert.That(greyButtons, Is.EqualTo(0), "솔로 서막~1막에 회색 단추가 한 번도 없었다");
                Assert.That(client.View.Seats[0].Cell, Is.EqualTo(home), "조무래기 배틀은 고향 마을에서");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0), "「이 선택 화면은 아직 준비 중입니다.」가 한 번도 뜨지 않았다");
                foreach (var spec in BoardHud.MapActs) Assert.That(spec.Ready, Is.True, spec.Id);
                TestContext.WriteLine("숨겨진 장소에 멈춘 횟수 " + spotAsks);
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (rig.Flow != null) rig.Flow.SoundOnSkip = false;
                Shut(rig);
            }
        }

        [UnityTest]
        public IEnumerator A5_조무래기를이긴뒤마을창에서목적지와규토리를골라날아가면말과카메라가도착마을에서고숨겨진장소는공개하면뒤집힌다()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            var rig = new Rig();
            try
            {
                yield return Open(rig);
                var flow = rig.Flow;
                var stage = rig.Stage;
                var board = rig.Board;
                var tokens = board.Tokens;

                // 스타팅 포켓몬 하나로 조무래기 둘을 이기는 판(BattleFlowTests와 같은 판): 첫 기술만 누른다
                flow.StartSolo(new SoloSettings(), 179, 99);
                var setup = (SetupVillageScreen)stage.Current;
                setup.Pick(0);
                Assert.That(setup.Confirm(), Is.True);
                flow.Skip(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(1);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                yield return null;
                var client = flow.Client;
                flow.Skip(flow.OpeningMs);
                var hud = flow.Hud;   // 보드 HUD는 셋업 연출이 끝난 뒤에 뜬다
                yield return WaitInput(flow);
                Assert.That(hud.Press(BoardHud.Challenge), Is.True);
                for (int guard = 0; guard < 8000; guard++)
                {
                    var battle = stage.Current as BattleScreen;
                    if (battle != null && battle.ShowsResult) break;
                    if (battle != null && battle.Open && battle.AskingToken) battle.PressToken(true);
                    else if (battle == null || !battle.Open || battle.Moves.Count == 0 || !battle.PressMove(0)) flow.Skip(250);
                    yield return null;
                }
                Assert.That(flow.Battle != null && flow.Battle.Won, Is.True, "찾아 둔 판은 이긴다");
                for (int guard = 0; guard < 400 && !flow.InputOpen; guard++)
                {
                    var battle = stage.Current as BattleScreen;
                    if (battle != null && battle.ShowsResult && !battle.AskingReward) battle.Confirm();
                    else flow.Skip(250);
                    yield return null;
                }
                Assert.That(flow.InputOpen, Is.True, "이긴 뒤 다음 내 턴");
                var me = client.View.Seats[0];
                int from = me.Cell;
                Assert.That(SetupFlow.GruntAt(client.View, from), Is.False, "조무래기가 물러났다");
                Assert.That(me.Apricorns[(int)ApricornColor.Red], Is.GreaterThan(0), "1막 보상 빨강 규토리");

                // ── S23·S25: 공중날기가 열려 있다 — 목적지 하나 + 규토리 색 하나 → [출발] ──
                Assert.That(hud.Press(BoardHud.Village), Is.True);
                yield return null;
                var village = (VillageScreen)stage.Current;
                Assert.That(stage.Root.Q("board-village-grunt"), Is.Null);
                Assert.That(village.CanFly, Is.True);
                Assert.That(village.FlyWhy, Is.Null);
                Assert.That(stage.Root.Q("board-village-fly-why"), Is.Null);
                var choice = client.Choice(ChoiceKind.MapAction);
                var legal = client.Legal(choice).FindAll(a => a.Kind == AnswerKind.Fly);
                foreach (int f in village.Dests) Assert.That(village.CanGo(f), Is.EqualTo(legal.Exists(a => a.Target == f)), "켜진 목적지 = 엔진의 합법 답");
                for (int c = 0; c < SeatState.ApricornColors; c++) Assert.That(village.CanPay(c), Is.EqualTo(legal.Exists(a => a.Arg == c)), "켜진 규토리 색 = 엔진의 합법 답");
                Assert.That(village.Pay, Is.EqualTo((int)ApricornColor.Red), "가진 색이 골라져 있다");
                // 없는 색: 혼자 하는 판은 조무래기를 이기면 스탬프 2(빨강·파랑·노랑 1 — rulings A30)라 파랑도 생긴다 — 가지지 않은 첫 색으로 본다
                int absent = System.Array.FindIndex(me.Apricorns, n => n == 0);
                Assert.That(absent, Is.GreaterThanOrEqualTo(0), "가지지 않은 색이 있다");
                Assert.That(village.CanPay(absent), Is.False);
                Assert.That(village.PickColor(absent), Is.False, "없는 색은 고르지 못한다");
                Assert.That(stage.Root.Q<Button>("board-village-fly").enabledSelf, Is.False, "목적지를 고르기 전에는 [출발]이 꺼져 있다");
                Assert.That(village.PressFly(), Is.False);

                // ── T18 ①: [지도에서 고르기] → 창이 오른쪽 기둥으로 비키고(가림막 없음 · 공중날기 칸만) 판 전체가 보인다 · 갈 수 있는 마을 = 합법 Fly 답의 마을이 칠해지고
                // 지금 선 마을은 흐리게 + 까닭 · 보드의 마을 칸을 누르면 그 목적지 · 칩에 올리면 강조가 따라간다 · Esc = 창으로 + 카메라 되돌림 ──
                var fixtures = flow.Ctx.Fixtures.Fixtures;
                var look = flow.SaveLook();
                Assert.That(stage.Root.Q<Button>("board-village-map").enabledSelf, Is.True, "[지도에서 고르기]");
                Assert.That(village.BeginMap(), Is.True);
                yield return null;
                Assert.That(flow.FlyPick, Is.SameAs(village));
                Assert.That(stage.Root.Q("board-village-scrim").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "가림막 없음");
                Assert.That(stage.Root.Q("board-village-panel").ClassListContains("sn-village-side"), Is.True, "오른쪽 기둥");
                Assert.That(stage.Root.Q("board-village-heal"), Is.Null, "보드에서 고르는 동안은 공중날기 칸만");
                Assert.That(rig.Camera.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Full), "판 전체가 보인다");
                Assert.That(rig.Camera.InputEnabled, Is.True, "보드 카메라 입력이 산다(끌기 · 휠 · Q · E)");
                var lit = new List<int>();
                foreach (var answer in legal) if (!lit.Contains(fixtures[answer.Target].Cell)) lit.Add(fixtures[answer.Target].Cell);
                Assert.That(board.Marks.Reach, Is.EquivalentTo(lit), "칠한 칸 = 합법 Fly 답의 마을");
                Assert.That(board.Marks.Dims, Does.Contain(from), "지금 선 마을은 흐리게");
                Assert.That(village.WhyAt(from), Is.EqualTo("screen.village.fly.why.here"));
                village.HoverCell(from);
                string fromName = null;
                foreach (var x in fixtures) if (x.Kind == FixtureKind.Village && x.Cell == from) fromName = stage.T(x.Key);
                Assert.That(village.HintText, Is.EqualTo(stage.T("screen.village.fly.why.here").Replace("{place}", fromName)), "흐린 마을에 올리면 까닭 한 줄");
                village.HoverCell(-1);
                Assert.That(village.HintText, Is.EqualTo(stage.T("screen.village.fly.map.hint")));
                // 보드의 마을 칸을 누른다(화면에 보이고 · 창 위가 아니고 · 그 점이 그 칸인 갈 수 있는 마을)
                int clicked = -1;
                Vector2 clickPx = default;
                foreach (int f in village.Dests)
                {
                    if (!village.CanGo(f)) continue;
                    int cell = fixtures[f].Cell;
                    if (!BoardPick.Screen(board, UnityEngine.Camera.main, cell, 1920, 1080, out var px) || px.x < 0 || px.x >= 1920 || px.y < 0 || px.y >= 1080 || flow.OverUi(px.x, px.y)) continue;
                    if (BoardPick.Cell(board, UnityEngine.Camera.main, px.x, px.y, 1920, 1080) != cell) continue;
                    clicked = f;
                    clickPx = px;
                    break;
                }
                Assert.That(clicked, Is.GreaterThanOrEqualTo(0), "보드에 보이는 갈 수 있는 마을");
                int commands = flow.Session.CommandCount;
                Assert.That(flow.Pointer(clickPx.x, clickPx.y, false), Is.EqualTo(fixtures[clicked].Cell), "창이 떠 있어도 보드의 칸을 읽는다");
                Assert.That(board.Marks.Target, Is.EqualTo(fixtures[clicked].Cell), "올린 마을이 강조된다");
                Assert.That(village.HintText, Is.EqualTo(stage.T("screen.village.fly.map.pick").Replace("{place}", stage.T(fixtures[clicked].Key))));
                flow.Pointer(clickPx.x, clickPx.y, true);
                Assert.That(village.Dest, Is.EqualTo(clicked), "보드에서 누른 마을이 목적지");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "고르기만 한다(답은 [출발])");
                Assert.That(stage.Root.Q<Button>("board-village-fly").enabledSelf, Is.True);
                Assert.That((board.Marks.Target, board.Marks.Held), Is.EqualTo((fixtures[clicked].Cell, false)), "마우스가 그 칸 위 = 금색 칠");
                flow.Pointer(1, 1, false);
                Assert.That((board.Marks.Target, board.Marks.Held), Is.EqualTo((fixtures[clicked].Cell, true)), "고른 목적지 = 금색 테두리");
                Assert.That(stage.Root.Q("board-village-dest-" + fixtures[clicked].Key.Replace('.', '-')).ClassListContains("sn-party-picked"), Is.True, "칩도 골라졌다");
                int other = -1;
                foreach (int f in village.Dests) if (village.CanGo(f) && f != clicked) { other = f; break; }
                if (other >= 0)
                {
                    var before = rig.Camera.Target;
                    bool clear = flow.CellClear(fixtures[other].Cell);
                    Assert.That(village.HoverDest(other), Is.EqualTo(!clear), "판 전체에 이미 보이면 카메라는 두고, 가려 있으면 그 마을로");
                    Assert.That((board.Marks.Target, board.Marks.Held), Is.EqualTo((fixtures[other].Cell, false)), "칩에 올리면 보드 강조가 따라간다");
                    if (clear) Assert.That(rig.Camera.Target, Is.EqualTo(before));
                    else Assert.That(Vector3.Distance(rig.Camera.Target, board.CellTop(fixtures[other].Cell)), Is.LessThan(1e-3f), "카메라도 그 마을로");
                    village.HoverDest(-1);
                }
                Assert.That(flow.Escape(), Is.True, "Esc");
                yield return null;
                Assert.That(stage.Current, Is.SameAs(village), "Esc = 창으로(창은 그대로)");
                Assert.That(village.Picking, Is.False);
                Assert.That(stage.Root.Q("board-village-heal"), Is.Not.Null, "창 꼴로 돌아왔다");
                Assert.That(board.Marks.Dims.Count, Is.EqualTo(0), "보드 칠하기를 걷었다");
                Assert.That((rig.Camera.CurrentZoom, rig.Camera.Target), Is.EqualTo((look.Zoom, look.Target)), "카메라는 고르기 전 자리로");
                Assert.That(village.Dest, Is.EqualTo(clicked), "고른 목적지는 남는다");

                int dest = -1;
                foreach (int f in village.Dests) if (village.CanGo(f)) { dest = f; break; }
                Assert.That(village.PickDest(dest), Is.True);
                yield return null;
                string destKey = flow.Ctx.Fixtures.Fixtures[dest].Key;
                Assert.That(stage.Root.Q("board-village-dest-" + destKey.Replace('.', '-')).ClassListContains("sn-party-picked"), Is.True);
                Assert.That(stage.Root.Q<Button>("board-village-fly").enabledSelf, Is.True);
                int red = me.Apricorns[(int)ApricornColor.Red], moves = me.MovesLeft;
                int to = flow.Ctx.Fixtures.Fixtures[dest].Cell;
                var piece = board.Pieces.Of(0).Root.transform;
                Assert.That(village.PressFly(), Is.True, "[출발]");
                Assert.That(stage.Current, Is.Null, "날아가는 것이 보이게 창이 닫힌다");
                Assert.That(client.View.Seats[0].Cell, Is.EqualTo(to));
                Assert.That(client.View.Seats[0].Apricorns[(int)ApricornColor.Red], Is.EqualTo(red - 1), "규토리 1개");
                Assert.That(client.View.Seats[0].MovesLeft, Is.EqualTo(moves), "이동력은 쓰지 않는다(G12)");
                Assert.That(flow.InputOpen, Is.False, "날아가는 동안 입력 닫힘");

                // ── 16행: 말이 호를 그리며 날아간다(가운데에서 두 마을의 칸 윗면보다 높다) · 카메라의 보는 점이 따라간다 ──
                stage.Tweens.Advance(1);   // 대기열이 항목을 시작한다
                var step = FlyBeats.StepOf(flow.Fx.Sequencer.Current);
                Assert.That(step, Is.Not.Null, "공중날기 단계가 재생 중이다");
                // 결정 167: 단계 앞의 비전기술 컷인 — 띠 · 찌르호크에 탄 내 주인공, 그동안 말은 떠난 마을에 · 택시 찌르호크는 아직 없다
                var startAt = piece.localPosition;
                stage.Tweens.Advance(step.StartMs + FlyCutinBeats.InMs - flow.Fx.Sequencer.LocalMs);
                yield return null;
                Assert.That(flow.FlyFx, Is.Not.Null);
                Assert.That(flow.FlyFx.Shown && flow.FlyFx.Pose.Riders, Is.True, "컷인 띠 위로 찌르호크가 가운데에 왔다");
                Assert.That(flow.FlyFx.RiderShown, Is.EqualTo(flow.Protagonist(0)), "탄 것은 그 좌석의 주인공");
                Assert.That(flow.FlyFx.Element.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(stage.Root.Q("board-fly-cutin-bird"), Is.Not.Null, "찌르호크 도트");
                Assert.That(stage.Root.Q("board-fly-cutin-band").resolvedStyle.height, Is.EqualTo(FlyCutinBeats.BandTexels * flow.FlyFx.Texel / (float)stage.Grade.PanelScale).Within(1f), "띠 = 80텍셀 × 정수배");
                Assert.That(Vector3.Distance(piece.localPosition, startAt), Is.LessThan(1e-3f), "컷인 동안 말은 떠나지 않는다");
                Assert.That(flow.BirdShown(out _), Is.False, "택시 찌르호크는 컷인 뒤에 내려온다");
                Assert.That(flow.InputOpen, Is.False);
                stage.Tweens.Advance(FlyBeats.CutinMs - FlyCutinBeats.InMs + 1);
                yield return null;
                Assert.That(flow.FlyFx.Shown, Is.False, "컷인이 끝나면 띠가 걷힌다");
                Assert.That(flow.FlyFx.Element.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(flow.BirdShown(out _), Is.True, "이어서 택시");
                stage.Tweens.Advance(step.StartMs + FlyBeats.CutinOf(step.Ms) + FlyBeats.TaxiMs(step.Ms) / 2 - flow.Fx.Sequencer.LocalMs);
                yield return null;
                Vector3 a = board.CellTop(from), b = board.CellTop(to);
                var mid = Vector3.Lerp(a, b, 0.5f);
                Assert.That(piece.localPosition.y, Is.GreaterThan(mid.y + GameFlow.FlyMinHeight * 0.9f), "가운데에서 떠 있다");
                Assert.That(new Vector2(piece.localPosition.x - mid.x, piece.localPosition.z - mid.z).magnitude, Is.LessThan(1.5f), "두 마을의 가운데쯤(칸 안 자리만큼 어긋난다)");
                Assert.That(Vector3.Distance(rig.Camera.Target, mid), Is.LessThan(0.6f), "카메라의 보는 점이 따라간다");
                yield return WaitInput(flow);
                Assert.That(flow.InputOpen, Is.True, "내려앉으면 입력이 열린다");
                Assert.That(board.Pieces.Of(0).Spot.Cell, Is.EqualTo(to), "말이 도착 마을에 섰다");
                Assert.That(rig.Camera.Target, Is.EqualTo(b), "카메라가 도착 마을을 본다");
                Assert.That(stage.Root.Q("board-hud-used").Query<Label>().ToList().Exists(l => l.text == "공중날기"), "아래 판 「이번 턴」에 공중날기");
                Assert.That(new List<string>(hud.RecentLog).Exists(l => l.Contains(stage.T(destKey)) && l.Contains("날아갔다")), Is.True, string.Join(" | ", hud.RecentLog));

                // 턴당 1회: 도착 마을의 마을 창에서 공중날기는 까닭과 함께 꺼져 있다
                Assert.That(hud.Press(BoardHud.Village), Is.True);
                yield return null;
                village = (VillageScreen)stage.Current;
                Assert.That(stage.Root.Q<Label>("board-village-title").text, Is.EqualTo(stage.T(destKey)));
                Assert.That(village.CanFly, Is.False);
                Assert.That(village.FlyWhy, Is.EqualTo("screen.village.used"));
                flow.CloseVillage();
                yield return WaitInput(flow);

                // ── S34·25행: 가장 가까운 야영지(숨겨진 장소)까지 걸어가 멈춘다 → 토큰이 떠 있다 → [공개] → 뒤집히고 앞면으로 내려앉는다 ──
                // 공개 뒤에도 그 칸에 남는 것은 야영지뿐이다(M5 결과 5종 — 규토리 더미·떨어진 별·유물은 맵에서 빠지고 우두머리는 배틀) — 가는 길의 다른 숨겨진 장소는 지나간다
                PendingChoice ask = null;
                for (int guard = 0; guard < 400 && ask == null; guard++)
                {
                    if (!flow.InputOpen) { flow.Skip(250); yield return null; continue; }
                    var camps = Camps(flow);
                    ask = client.Choice(ChoiceKind.HiddenSpot);
                    if (ask != null)
                    {
                        if (camps.Contains(client.View.Seats[0].Cell)) break;
                        Assert.That(hud.Press(BoardHud.Pass), Is.True);
                        ask = null;
                        yield return null;
                        continue;
                    }
                    var view = client.View;
                    int here = view.Seats[0].Cell, goal = -1, best = -1, bestLeft = int.MaxValue;
                    var reach = MoveRules.Reachable(flow.Ctx.Board, here, 400, view.Options.ElevationMode);
                    foreach (var t in view.Tokens)
                        if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0 && camps.Contains(t.Cell) && reach[t.Cell] > 0 && reach[t.Cell] != MoveRules.Unreachable && (goal < 0 || reach[t.Cell] < reach[goal])) goal = t.Cell;
                    Assert.That(goal, Is.GreaterThanOrEqualTo(0), "닿을 수 있는 야영지");
                    foreach (int cell in hud.Reach)
                    {
                        int left = cell == goal ? 0 : MoveRules.Reachable(flow.Ctx.Board, cell, 400, view.Options.ElevationMode)[goal];
                        if (left != MoveRules.Unreachable && left < bestLeft) { best = cell; bestLeft = left; }
                    }
                    if (best >= 0 && bestLeft < reach[goal]) Assert.That(hud.Walk(best), Is.True);
                    else Assert.That(hud.Press(BoardHud.EndTurn), Is.True);
                    yield return null;
                }
                Assert.That(ask, Is.Not.Null, "숨겨진 장소에 멈추면 묻는다");
                int spotCell = client.View.Seats[0].Cell;
                Assert.That(tokens.RaisedCell, Is.EqualTo(spotCell), "묻는 동안 토큰이 떠 있다");
                float hover = flow.SpotHover(spotCell);
                Assert.That(hover, Is.GreaterThan(BoardTokens.HoverHeight), "말 머리의 좌석 번호 핀 위(지금 줌의 말 높이로 센다)");
                Assert.That(tokens.RaisedHeight, Is.EqualTo(hover).Within(1e-4f));
                // 걷는 길 가운데서 멈췄으면(결정 71 ③) 안내가 「지나는 길에 …」다
                Assert.That(stage.Root.Q<Label>("board-hud-guide").text, Is.EqualTo(ask.Options.Count > 0 ? "지나는 길에 숨겨진 장소가 있습니다. 공개하겠습니까?" : "숨겨진 장소를 공개하겠습니까?"));
                Assert.That(hud.CellText(spotCell), Does.EndWith(stage.T("screen.token.hidden")));
                Assert.That(hud.Press(BoardHud.Reveal), Is.True);
                stage.Tweens.Advance(1);
                // 보기는 이미 앞면이지만 글은 토큰이 뒤집힌 뒤에 바뀐다(칸 정보 줄 · 공개 정보 카드)
                TokenState revealed = null;
                foreach (var t in client.View.Tokens) if (t.Kind == TokenKind.HiddenSpot && t.Cell == spotCell) revealed = t;
                Assert.That(revealed.FaceUp, Is.True);
                Assert.That(flow.TokenFaceUp(revealed), Is.False, "뒤집히기 전");
                Assert.That(hud.CellText(spotCell), Does.EndWith(stage.T("screen.token.hidden")), "뒤집히기 전에는 정체를 적지 않는다");
                Assert.That(stage.Root.Q<Label>("board-hud-cell").text, Does.EndWith(stage.T("screen.token.hidden")));
                var spotStep = SpotBeats.StepOf(flow.Fx.Sequencer.Current);
                Assert.That(spotStep, Is.Not.Null, "공개 단계가 재생 중이다");
                int flipEnd = spotStep.Ms * SpotBeats.FlipPermille / 1000;
                stage.Tweens.Advance(spotStep.StartMs + flipEnd / 2 - flow.Fx.Sequencer.LocalMs);
                Assert.That(flow.Sheets.Shown, Has.Some.StartsWith(SheetBeats.Star + "#"), "앞면으로 바뀔 때 토큰 둘레의 반짝임(결정 69 — GameFlow.Sheets)");
                Assert.That(tokens.RaisedCell, Is.EqualTo(spotCell));
                Assert.That(tokens.RaisedFlip, Is.EqualTo(0.5f).Within(0.02f), "뒤집히는 가운데");
                Assert.That(tokens.RaisedHeight, Is.GreaterThan(hover), "뒤집히는 동안 더 떠오른다");
                stage.Tweens.Advance(flipEnd / 2 + 10);
                Assert.That(tokens.RaisedFlip, Is.EqualTo(1f).Within(1e-3f), "앞면");
                Assert.That(flow.TokenFaceUp(revealed), Is.True);
                Assert.That(stage.Root.Q<Label>("board-hud-cell").text, Does.EndWith(stage.T(flow.Ctx.Items.Items[revealed.Def].Key)), "뒤집히면 칸 정보 줄이 그 장소의 이름으로 바뀐다");
                yield return WaitInput(flow);
                // 공개한 앞면은 말이 그 칸에 서 있는 동안 머리 위에 떠 있다(내려앉으면 말에 깔려 보이지 않는다)
                Assert.That(tokens.RaisedCell, Is.EqualTo(spotCell), "앞면으로 머리 위에 머문다");
                Assert.That(tokens.RaisedFlip, Is.EqualTo(1f));
                Assert.That(tokens.RaisedHeight, Is.EqualTo(flow.SpotHover(spotCell)).Within(1e-4f));
                BoardTokens.Token shown = default;
                foreach (var t in tokens.Shown) if (t.Cell == spotCell && t.Kind == TokenKind.HiddenSpot) shown = t;
                Assert.That(shown.FaceUp, Is.True, "앞면으로 보드에 놓였다");
                Assert.That(shown.Key, Is.Not.Null);
                Assert.That(hud.CellText(spotCell), Does.Contain(stage.T(shown.Key)), "칸 정보 줄에 그 장소의 이름");
                Assert.That(client.Choice(ChoiceKind.MapAction), Is.Not.Null, "턴은 이어진다");

                // ── 25행: 공개한 그 명령 안에서 맵에서 빠지는 결과(규토리 더미 — rulings A25, 떨어진 별 · 유물 · 우두머리도 같은 길 — UiShots board-spot-*)도 공개 연출이 끝날 때까지 토큰이 떠서 뒤집히고 앞면을 보인 뒤 사라진다 ──
                // (규칙 끝 감사: 보기에서 곧바로 빠진 토큰을 SyncTokens가 지워 뒤집기 · 앞면 · 반짝임이 안 나왔다)
                ask = null;
                for (int guard = 0; guard < 400 && ask == null; guard++)
                {
                    if (!flow.InputOpen) { flow.Skip(250); yield return null; continue; }
                    var leaving = Leaving(flow);
                    ask = client.Choice(ChoiceKind.HiddenSpot);
                    if (ask != null)
                    {
                        if (leaving.Contains(client.View.Seats[0].Cell)) break;
                        Assert.That(hud.Press(BoardHud.Pass), Is.True);
                        ask = null;
                        yield return null;
                        continue;
                    }
                    var view = client.View;
                    int here = view.Seats[0].Cell, goal = -1, best = -1, bestLeft = int.MaxValue;
                    var reach = MoveRules.Reachable(flow.Ctx.Board, here, 400, view.Options.ElevationMode);
                    foreach (var t in view.Tokens)
                        if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0 && leaving.Contains(t.Cell) && reach[t.Cell] > 0 && reach[t.Cell] != MoveRules.Unreachable && (goal < 0 || reach[t.Cell] < reach[goal])) goal = t.Cell;
                    Assert.That(goal, Is.GreaterThanOrEqualTo(0), "닿을 수 있는 규토리 더미");
                    foreach (int cell in hud.Reach)
                    {
                        int left = cell == goal ? 0 : MoveRules.Reachable(flow.Ctx.Board, cell, 400, view.Options.ElevationMode)[goal];
                        if (left != MoveRules.Unreachable && left < bestLeft) { best = cell; bestLeft = left; }
                    }
                    if (best >= 0 && bestLeft < reach[goal]) Assert.That(hud.Walk(best), Is.True);
                    else Assert.That(hud.Press(BoardHud.EndTurn), Is.True);
                    yield return null;
                }
                Assert.That(ask, Is.Not.Null, "규토리 더미에 멈추면 묻는다");
                int goneCell = client.View.Seats[0].Cell;
                Assert.That(tokens.RaisedDrawn, Is.True, "묻는 동안 토큰이 떠 있다");
                Assert.That(hud.Press(BoardHud.Reveal), Is.True);
                stage.Tweens.Advance(1);
                Assert.That(client.View.Tokens.Exists(t => t.Kind == TokenKind.HiddenSpot && t.Cell == goneCell), Is.False, "보기에서는 공개한 그 명령 안에 맵에서 빠졌다");
                var goneStep = SpotBeats.StepOf(flow.Fx.Sequencer.Current);
                Assert.That(goneStep, Is.Not.Null, "공개 단계가 재생 중이다");
                int goneFlip = goneStep.Ms * SpotBeats.FlipPermille / 1000, goneHold = goneStep.Ms * SpotBeats.HoldPermille / 1000;
                stage.Tweens.Advance(goneStep.StartMs + goneFlip / 2 - flow.Fx.Sequencer.LocalMs);
                Assert.That(tokens.RaisedCell, Is.EqualTo(goneCell));
                Assert.That(tokens.RaisedDrawn, Is.True, "뒤집히는 토큰이 그려진다");
                Assert.That(tokens.RaisedFlip, Is.EqualTo(0.5f).Within(0.02f), "뒤집히는 가운데");
                Assert.That(flow.Sheets.Shown, Has.Some.StartsWith(SheetBeats.Star + "#"), "앞면으로 바뀔 때의 반짝임");
                stage.Tweens.Advance((goneFlip + goneHold) / 2 - goneFlip / 2);
                Assert.That(tokens.RaisedDrawn && tokens.RaisedHasFront, Is.True, "앞면 그림으로 머문다");
                Assert.That(tokens.RaisedFlip, Is.EqualTo(1f).Within(1e-3f));
                // 26행(M5 「맵 결과·배틀 안 연출」): 뒤집힌 뒤 결과 단계 — 토큰이 부풀었다 터지며 사라지고 규토리가 튀어 「○○ 규토리 +n」
                var stashStep = ResultRows.SpotStep(flow.Fx.Sequencer.Current);
                Assert.That(stashStep, Is.Not.Null, "결과 단계(26행)가 공개 단계 뒤에 선다");
                Assert.That(stashStep.Row, Is.EqualTo(ResultRows.Stash));
                stage.Tweens.Advance(stashStep.StartMs + SpotResultBeats.PopMs - 60 - flow.Fx.Sequencer.LocalMs);
                Assert.That(tokens.RaisedDrawn, Is.True, "터지기 전에는 떠 있다");
                Assert.That(tokens.RaisedSwell, Is.GreaterThan(1.1f), "부푼다");
                stage.Tweens.Advance(60 + 350);
                Assert.That(tokens.RaisedDrawn, Is.False, "터지면 토큰이 사라진다");
                Assert.That(flow.Results.LabelShown, Does.Contain("규토리 +"), "결과 꼬리표");
                Assert.That(flow.Results.FlyShown, Is.GreaterThan(0), "규토리가 튄다");
                yield return WaitInput(flow);
                Assert.That(tokens.RaisedSwell, Is.EqualTo(1f), "다음 토큰은 제 크기");
                Assert.That(tokens.RaisedDrawn, Is.False, "공개 연출이 끝나면 치운다");
                Assert.That(new List<BoardTokens.Token>(tokens.Shown).Exists(t => t.Kind == TokenKind.HiddenSpot && t.Cell == goneCell), Is.False, "보드에서도 빠졌다");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                Shut(rig);
            }
        }

        /// <summary>뒷면 규토리 더미가 있는 칸(엔진 원본에서) — 공개한 명령 안에서 맵에서 빠지고 배틀·고르기를 열지 않으며 앞면 그림이 있는 결과.</summary>
        static HashSet<int> Leaving(GameFlow flow)
        {
            var r = new HashSet<int>();
            foreach (var t in flow.Session.HostCopy().Tokens)
            {
                if (t.Kind != TokenKind.HiddenSpot || t.FaceUp || t.Destroyed || t.Cell < 0) continue;
                if (HiddenSpotRules.Stash(flow.Ctx, t.Def, out _, out _)) r.Add(t.Cell);
            }
            return r;
        }

        // 결정 71: 지금 칸에서 뒷면 숨겨진 장소 spot을 경유 칸으로 거쳐 그 이웃 beyond(뒷면 숨겨진 장소 없음)로 가는 길이 남은 이동력 안이고, 길에서 처음 멈출 칸이 spot인 짝. 없으면 false
        /// <summary>뒷면 야영지가 있는 칸(엔진 원본에서 — 보기는 뒷면의 정체를 가린다).</summary>
        static HashSet<int> Camps(GameFlow flow)
        {
            var r = new HashSet<int>();
            int camp = flow.Ctx.Items.Ids.IndexOf(HiddenSpotRules.Campsite);
            foreach (var t in flow.Session.HostCopy().Tokens)
                if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0 && t.Def == camp) r.Add(t.Cell);
            return r;
        }

        /// <summary>그 칸의 뒷면 숨겨진 장소가 우두머리인가(엔진 원본에서).</summary>
        static bool AlphaAt(GameFlow flow, int cell)
        {
            foreach (var t in flow.Session.HostCopy().Tokens)
                if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell == cell && flow.Ctx.Items.Items[t.Def].Kind == ItemKind.AlphaPokemon) return true;
            return false;
        }

        static bool PassPair(GameFlow flow, BoardHud hud, int skip, out int spot, out int beyond)
        {
            var view = flow.Client.View;
            var me = view.Seats[flow.Client.Seat];
            var reach = new List<int>(hud.Reach);
            spot = beyond = -1;
            foreach (var t in view.Tokens)
            {
                if (t.Kind != TokenKind.HiddenSpot || t.FaceUp || t.Destroyed || t.Cell < 0 || t.Cell == skip || !reach.Contains(t.Cell)) continue;
                for (int d = 0; d < Board.Directions; d++)
                {
                    int b = flow.Ctx.Board.Neighbor(t.Cell, d);
                    if (b == Board.NoCell || b == me.Cell || !reach.Contains(b) || MapActions.FaceDownSpotAt(view, b) >= 0) continue;
                    var route = MoveRules.Route(flow.Ctx.Board, me.Cell, new[] { t.Cell }, b, me.MovesLeft, view.Options.ElevationMode);
                    if (route == null) continue;
                    var stops = MapActions.StopsOn(view, flow.Ctx, flow.Client.Seat, route.GetRange(1, route.Count - 1));
                    if (stops.Count == 0 || stops[0] != t.Cell) continue;
                    spot = t.Cell;
                    beyond = b;
                    return true;
                }
            }
            return false;
        }

        [UnityTest]
        public IEnumerator G12_결정71_경유칸으로길을고르면길위숨겨진장소에표지_걷다멈춰묻고지나가면남은길_공개하면남은길을버린다()
        {
            // 원작 커뮤니티 확인 → 사용자 결정 71 ①②③
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            var rig = new Rig();
            try
            {
                yield return Open(rig);
                var flow = rig.Flow;
                var stage = rig.Stage;
                var board = rig.Board;
                flow.StartSolo(new SoloSettings(), 179, 99);
                var setup = (SetupVillageScreen)stage.Current;
                setup.Pick(0);
                Assert.That(setup.Confirm(), Is.True);
                flow.Skip(flow.QueueLeftMs);
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(1);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                yield return null;
                var client = flow.Client;
                flow.Skip(flow.OpeningMs);
                var hud = flow.Hud;
                yield return WaitInput(flow);

                int passed = -1;
                bool didPass = false, didReveal = false;
                for (int guard = 0; guard < 400 && !(didPass && didReveal); guard++)
                {
                    if (!flow.InputOpen) { flow.Skip(250); yield return null; continue; }
                    var stray = client.Choice(ChoiceKind.HiddenSpot);
                    if (stray != null) { Assert.That(hud.Press(BoardHud.Pass), Is.True); yield return null; continue; }   // 다가가는 길에 멈춘 숨겨진 장소는 지나간다
                    var view = client.View;
                    if (PassPair(flow, hud, passed, out int spot, out int beyond))
                    {
                        int here = view.Seats[0].Cell, moves = view.Seats[0].MovesLeft;
                        // ① ② 고른 칸 → 경유 칸: 길 위의 숨겨진 장소에 하늘색 표지, 경유 칸은 흰 테두리, 안내에 그 수
                        Assert.That(hud.PickCell(beyond), Is.True);
                        Assert.That(hud.AddVia(spot), Is.True, "경유 칸");
                        var route = MoveRules.Route(flow.Ctx.Board, here, new[] { spot }, beyond, moves, view.Options.ElevationMode);
                        Assert.That(hud.PickedPath, Is.EqualTo(route));
                        Assert.That(board.Marks.Vias, Is.EqualTo(new[] { spot }));
                        Assert.That(board.Marks.Stops, Has.Member(spot), "길 위 숨겨진 장소 표지");
                        Assert.That(board.Marks.Stops, Is.EqualTo(hud.PathStops), "표지 = 엔진이 멈출 칸");
                        Assert.That(stage.Root.Q<Label>("board-hud-guide").text, Does.EndWith("숨겨진 장소 " + hud.PathStops.Count + "곳(?)"));   // T15: 「(?)」 = 우두머리 주의 마우스 오버
                        Assert.That(hud.Back(), Is.True, "오른쪽 단추·Esc = 경유 칸 하나 빼기");
                        Assert.That((hud.PickedCell, hud.Vias.Count), Is.EqualTo((beyond, 0)));
                        Assert.That(hud.AddVia(spot), Is.True);
                        int commands = flow.Session.CommandCount;
                        Assert.That(hud.ConfirmMove(), Is.True, "[이동] = 고른 길 그대로");
                        Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1));
                        yield return WaitInput(flow);
                        // ③ 걷다가 숨겨진 장소에서 멈춰 묻는다 — 남은 길이 칠해져 있다
                        var ask = client.Choice(ChoiceKind.HiddenSpot);
                        Assert.That(ask, Is.Not.Null, "길 위의 숨겨진 장소에서 묻는다");
                        Assert.That(client.View.Seats[0].Cell, Is.EqualTo(spot));
                        Assert.That(ask.Options[ask.Options.Count - 1], Is.EqualTo(beyond), "남은 길");
                        Assert.That(stage.Root.Q<Label>("board-hud-guide").text, Is.EqualTo("지나는 길에 숨겨진 장소가 있습니다. 공개하겠습니까?"));
                        Assert.That(board.Pieces.Of(0).Spot.Cell, Is.EqualTo(spot), "말이 그 칸에 섰다");
                        Assert.That((board.Marks.Visible, board.Marks.Target), Is.EqualTo((true, beyond)), "지나가면 이어 걸을 길");
                        Assert.That(board.Marks.Stops, Has.Member(spot), "묻는 칸(지금 선 숨겨진 장소)에도 하늘색 표지 — 고르는 단계와 이어진다(검토 반영)");
                        if (!didPass)
                        {
                            Assert.That(hud.Press(BoardHud.Pass), Is.True, "[지나가기]");
                            yield return WaitInput(flow);
                            Assert.That(client.View.Seats[0].Cell, Is.EqualTo(beyond), "남은 길을 이어 걸었다");
                            Assert.That(client.View.Seats[0].MovesLeft, Is.EqualTo(moves - (route.Count - 1)));
                            Assert.That(board.Pieces.Of(0).Spot.Cell, Is.EqualTo(beyond));
                            Assert.That(client.Choice(ChoiceKind.MapAction), Is.Not.Null);
                            didPass = true;
                            passed = spot;
                        }
                        else if (AlphaAt(flow, spot))
                        {
                            // 우두머리는 공개하면 강제 배틀(A17 — 배틀이 턴을 끝낸다): 이 시험은 맵에서 공개하는 길만 본다 — 지나가고 다음 짝을 찾는다
                            Assert.That(hud.Press(BoardHud.Pass), Is.True);
                            yield return WaitInput(flow);
                            passed = spot;
                        }
                        else
                        {
                            int left = client.View.Seats[0].MovesLeft;
                            Assert.That(hud.Press(BoardHud.Reveal), Is.True, "[공개]");
                            yield return WaitInput(flow);
                            Assert.That(client.View.Seats[0].Cell, Is.EqualTo(spot), "공개하면 남은 길은 버리고 그 칸에 선다");
                            Assert.That(client.View.Seats[0].MovesLeft, Is.EqualTo(left), "남은 이동력 그대로");
                            Assert.That(MapActions.FaceDownSpotAt(client.View, spot), Is.LessThan(0), "앞면");
                            didReveal = true;
                        }
                        continue;
                    }
                    // 아직 짝이 없다: 가장 가까운 뒷면 숨겨진 장소 쪽으로 걷거나 턴을 넘긴다
                    int at = view.Seats[0].Cell, goal = -1, best = -1, bestLeft = int.MaxValue;
                    var far = MoveRules.Reachable(flow.Ctx.Board, at, 400, view.Options.ElevationMode);
                    foreach (var t in view.Tokens)
                        if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0 && t.Cell != passed && far[t.Cell] > 1 && (goal < 0 || far[t.Cell] < far[goal])) goal = t.Cell;
                    Assert.That(goal, Is.GreaterThanOrEqualTo(0), "닿을 수 있는 숨겨진 장소");
                    foreach (int cell in hud.Reach)
                    {
                        int left = MoveRules.Reachable(flow.Ctx.Board, cell, 400, view.Options.ElevationMode)[goal];
                        if (cell != goal && left != MoveRules.Unreachable && left >= 1 && left < bestLeft) { best = cell; bestLeft = left; }
                    }
                    if (best >= 0 && bestLeft < far[goal] && view.Seats[0].MovesLeft == 3) Assert.That(hud.Walk(best), Is.True);
                    else Assert.That(hud.Press(BoardHud.EndTurn), Is.True);
                    yield return null;
                }
                Assert.That((didPass, didReveal), Is.EqualTo((true, true)), "지나가기 · 공개 둘 다");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                Shut(rig);
            }
        }

        [UnityTest]
        public IEnumerator G12_결정71_칸을눌러걸으면최단길가운데숨겨진장소에서멈춘다_StopsBefore가빈칸만바로도착한다()
        {
            // 결정 71 · 원작 커뮤니티 확인: [이동]·hud.Walk는 늘 고른 길(최단 길)을 내고 엔진은 길 가운데 뒷면 숨겨진 장소에서 멈춘다.
            // 걸은 뒤 바로 「누른 칸에 도착」을 단정하던 확인(ScreenProbe flow-board-walk · MouseProbe 회전 뒤 걸음)은 BoardHud.StopsBefore가 빈 칸을 고른다(검토 반영)
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            var rig = new Rig();
            try
            {
                yield return Open(rig);
                var flow = rig.Flow;
                var stage = rig.Stage;
                var board = rig.Board;
                flow.StartSolo(new SoloSettings(), 179, 99);
                var setup = (SetupVillageScreen)stage.Current;
                setup.Pick(0);
                Assert.That(setup.Confirm(), Is.True);
                flow.Skip(flow.QueueLeftMs);
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(1);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True);
                yield return null;
                var client = flow.Client;
                flow.Skip(flow.OpeningMs);
                var hud = flow.Hud;
                yield return WaitInput(flow);

                bool stopped = false, arrived = false;
                for (int guard = 0; guard < 400 && !(stopped && arrived); guard++)
                {
                    if (!flow.InputOpen) { flow.Skip(250); yield return null; continue; }
                    if (client.Choice(ChoiceKind.HiddenSpot) != null) { Assert.That(hud.Press(BoardHud.Pass), Is.True); yield return null; continue; }
                    var view = client.View;
                    var me = view.Seats[0];
                    int blocked = -1, clear = -1;
                    foreach (int cell in hud.Reach)
                    {
                        var way = MoveRules.Path(flow.Ctx.Board, me.Cell, cell, me.MovesLeft, view.Options.ElevationMode);
                        var all = MapActions.StopsOn(view, flow.Ctx, 0, way.GetRange(1, way.Count - 1));
                        var before = hud.StopsBefore(cell);
                        all.Remove(cell);
                        Assert.That(before, Is.EqualTo(all), "칸 " + cell + ": 최단 길 가운데 멈출 칸 = 엔진과 같은 셈");
                        if (before.Count > 0 && blocked < 0 && !stopped) blocked = cell;
                        if (before.Count == 0 && MapActions.FaceDownSpotAt(view, cell) < 0 && way.Count > 2 && clear < 0 && !arrived) clear = cell;
                    }
                    if (blocked >= 0)
                    {
                        int first = hud.StopsBefore(blocked)[0];
                        Assert.That(hud.Walk(blocked), Is.True);
                        yield return WaitInput(flow);
                        Assert.That(client.View.Seats[0].Cell, Is.EqualTo(first), "누른 칸 " + blocked + "이 아니라 길 가운데 숨겨진 장소에 선다");
                        Assert.That(client.Choice(ChoiceKind.HiddenSpot), Is.Not.Null, "그 칸에서 묻는다");
                        Assert.That(board.Marks.Stops, Has.Member(first), "묻는 칸에 하늘색 표지");
                        Assert.That(hud.Press(BoardHud.Pass), Is.True);
                        yield return WaitInput(flow);
                        stopped = true;
                        continue;
                    }
                    if (clear >= 0)
                    {
                        Assert.That(hud.Walk(clear), Is.True);
                        yield return WaitInput(flow);
                        Assert.That(client.View.Seats[0].Cell, Is.EqualTo(clear), "StopsBefore가 빈 칸은 누른 칸에 바로 선다");
                        Assert.That(board.Pieces.Of(0).Spot.Cell, Is.EqualTo(clear));
                        arrived = true;
                        continue;
                    }
                    Assert.That(hud.Press(BoardHud.EndTurn), Is.True);
                    yield return null;
                }
                Assert.That((stopped, arrived), Is.EqualTo((true, true)), "길 가운데 멈춤 · 바로 도착 둘 다");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                Shut(rig);
            }
        }
    }
}
