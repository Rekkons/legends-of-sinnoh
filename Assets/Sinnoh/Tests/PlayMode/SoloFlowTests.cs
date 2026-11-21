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
    /// 타이틀 → 혼자 하기(S09) → 게임 준비(S12·S13) → 보드 → 이동 한 번 → 턴 종료 → 다음 라운드 내 턴(plan §3 M4-3 조건 ①의 앞부분, §4 「솔로 설정·셋업」·「보드 화면」).
    /// 화면의 함수로 고르고, 보드의 칸은 화면 좌표로 고른다(<see cref="GameFlow.Pointer"/> — 실제 마우스 장치는 쓰지 않는다).
    /// Gen 겉모양(글꼴·창틀)이 있어야 돈다.
    /// </summary>
    public sealed class SoloFlowTests
    {
        // 조건이 될 때까지 프레임을 보낸다(실시각 상한). 연출 길이만큼 기다리는 데 쓴다
        static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        [UnityTest]
        public IEnumerator M4_타이틀에서혼자하기로셋업을끝내고이동과턴종료로다음라운드까지에러로그0()
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
                Assert.That(boot.StringsJson, Is.Not.Null, "씬이 화면 문자열 표를 가리키지 않는다");
                Assert.That(boot.DiceRolls, Is.Not.Null, "씬이 주사위 녹화본 묶음(rolls.bytes)을 가리키지 않는다 — 굴림판에 주사위가 구르지 않는다");
                Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                var flow = boot.Flow;
                Assert.That(flow, Is.Not.Null);
                boot.PointerFromMouse = false;   // 보드의 칸은 이 시험이 화면 좌표로 넣는다(장치의 마우스 자리를 섞지 않는다)
                Assert.That(flow.Ctx, Is.Not.Null, "씬이 덱·효과 표를 가리키지 않아 게임 데이터가 없다");
                var stage = boot.Root.Stage;
                var board = Object.FindAnyObjectByType<BoardBoot>();
                var cam = Camera.main;
                var rig = cam.GetComponent<BoardCameraRig>();
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(stage.Grade, Is.EqualTo(new UiGrade(1080, 1)));

                // ── S02 설정 「말 그림」(04 §3-1, 결정 52): 기본은 주인공 도트, 직업 그림을 고르면 뒤의 보드 말이 바로 바뀐다 ──
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(6));
                Assert.That(board.Pieces.Pieces[0].Style, Is.EqualTo(PieceStyle.Dot), "타이틀 뒤 시험 판 말도 내 설정의 양식(기본 = 주인공 도트)");
                flow.OpenSettings();
                yield return null;
                var settings = (SettingsScreen)stage.Current;
                Assert.That(stage.Root.Q("settings-row-piece"), Is.Not.Null);
                Assert.That(stage.Root.Q("settings-opt-piece-dot").Q<Label>().text, Is.EqualTo("주인공 도트"));
                Assert.That(stage.Root.Q("settings-opt-piece-job").Q<Label>().text, Is.EqualTo("직업 그림"));
                Assert.That(settings.Piece, Is.EqualTo(PieceStyle.Dot));
                Assert.That(stage.Root.Q("settings-opt-piece-dot").ClassListContains("sn-opt-picked"));
                settings.PickPiece(PieceStyle.Job);
                Assert.That(flow.PieceStyle, Is.EqualTo(PieceStyle.Job));
                Assert.That(stage.Root.Q("settings-opt-piece-job").ClassListContains("sn-opt-picked"));
                Assert.That(stage.Root.Q("settings-opt-piece-dot").ClassListContains("sn-opt-picked"), Is.False);
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(6));
                foreach (var p in board.Pieces.Pieces) Assert.That(p.Style, Is.EqualTo(PieceStyle.Job), "보드 말이 바로 직업 그림으로");
                flow.OpenTitle();
                yield return null;
                Assert.That(board.Pieces.Pieces[0].Style, Is.EqualTo(PieceStyle.Job), "화면을 바꿔도 설정이 남는다");
                // 저장(결정 70 ⑤): 음량과 같은 곳(SoundPrefs)에 쥔다 — 그 설정으로 다시 만든 흐름(다음 실행)도 직업 그림. 에디터는 사용자 값(PlayerPrefs)에 쓰지 않는다
                Assert.That(flow.SoundPrefs.Piece, Is.EqualTo(PieceStyle.Job), "말 그림은 음량 설정과 같은 곳에");
                Assert.That(flow.SoundPrefs, Is.Not.InstanceOf<StoredSoundPrefs>(), "에디터는 사용자의 설정에 쓰지 않는다");
                Assert.That(SoundPrefs.ClampPiece(99), Is.EqualTo(PieceStyle.Dot), "모르는 저장 값은 기본");
                boot.UseSaves(null);
                Assert.That(boot.Flow, Is.Not.SameAs(flow));
                flow = boot.Flow;
                yield return null;
                Assert.That(flow.PieceStyle, Is.EqualTo(PieceStyle.Job), "다시 만든 흐름도 저장된 말 그림으로");
                Assert.That(board.Pieces.Pieces[0].Style, Is.EqualTo(PieceStyle.Job));
                flow.OpenSettings();
                yield return null;
                Assert.That(stage.Root.Q("settings-opt-piece-job").ClassListContains("sn-opt-picked"), "다시 열면 고른 값이 그대로");
                ((SettingsScreen)stage.Current).PickPiece(PieceStyle.Dot);
                foreach (var p in board.Pieces.Pieces) Assert.That(p.Style, Is.EqualTo(PieceStyle.Dot));
                flow.OpenTitle();
                yield return null;

                // ── S01 타이틀: 「혼자 하기」가 켜져 있다 ──
                Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
                var title = (TitleScreen)stage.Current;
                int soloIndex = -1;
                for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Solo) soloIndex = i;
                Assert.That(title.Ready(title.Shown[soloIndex]), "화면 흐름이 「혼자 하기」의 갈 곳을 넣는다");
                Assert.That(stage.Root.Q("title-item-solo").Q(className: "sn-off"), Is.Null, "켜진 줄은 흐리지 않다");
                Assert.That(stage.Root.Q("title-item-credits").Q(className: "sn-off"), Is.Null, "「크레딧」 켜짐(M9 S03)");
                Assert.That(stage.Root.Q("title-item-host").Q(className: "sn-off"), Is.Null, "「방 만들기」 켜짐(M7 화면 ①)");
                Assert.That(stage.Root.Q("title-item-solo").Q<Label>(className: Kit.F3).text, Is.EqualTo("혼자 하기"), "글은 문자열 표에서");
                Assert.That(rig.InputEnabled, Is.False, "타이틀: 보드 입력 끔");
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(6), "타이틀 뒤는 시험 판 말 6");
                Assert.That(title.Activate(soloIndex), Is.True);

                // ── S09 솔로 설정 ──
                yield return null;
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<SoloScreen>());
                var solo = (SoloScreen)stage.Current;
                Assert.That(stage.Root.Query("title").ToList().Count, Is.EqualTo(0), "화면은 한 번에 하나");
                foreach (var row in new[] { SoloScreen.Protagonist, SoloScreen.Campaign, SoloScreen.TrainerCards, SoloScreen.Onslaught, SoloScreen.UbDifficulty, SoloScreen.GalacticAi, SoloScreen.Rules })
                    Assert.That(stage.Root.Q("solo-row-" + row), Is.Not.Null, row);
                Assert.That(solo.Settings.Campaign, Is.EqualTo(Campaign.HeartOfSinnoh));
                Assert.That(solo.Settings.TrainerCards, Is.False, "트레이너 카드 기본값 = 끔(결정 134)");
                Assert.That(stage.Root.Q("solo-opt-campaign-hos").ClassListContains("sn-opt-picked"));
                Assert.That(stage.Root.Q("solo-opt-trainer_cards-off").ClassListContains("sn-opt-picked"), "처음부터 「끔」이 골라져 있다");
                stage.Tips.Show(stage.Root.Q("solo-opt-trainer_cards-on"));
                Assert.That(stage.Tips.Element.Query<Label>().ToList().Exists(l => l.text.Replace("⁠", "").StartsWith("테스트 중인 기능입니다.")), Is.True, "「켬」 마우스 오버 = 테스트 중인 기능(결정 134)");
                stage.Tips.Hide();
                Assert.That(stage.Root.Q("solo-opt-campaign-ub").ClassListContains("sn-off"), Is.False, "울트라 버스트는 고를 수 있다(M6 「UB 시작」)");
                Assert.That(solo.Settings.GalacticAi, Is.EqualTo(Sinnoh.Rules.Session.GalacticAiLevel.Strong), "갤럭시단 AI 기본값 = 「강함」(결정 118 ②)");
                Assert.That(stage.Root.Q("solo-opt-galactic_ai-strong").ClassListContains("sn-opt-picked"), "처음부터 「강함」이 골라져 있다");
                Assert.That(solo.Set(SoloScreen.GalacticAi, "basic"), Is.True);
                Assert.That(solo.Set(SoloScreen.GalacticAi, "strong"), Is.True, "강한 AI는 M8 「연결」부터 고를 수 있다");
                Assert.That(stage.Root.Q("solo-opt-galactic_ai-strong").ClassListContains("sn-off"), Is.False, "「강함」 회색 풀림");
                Assert.That(solo.Set(SoloScreen.GalacticAi, "basic"), Is.True, "이 판은 원칙 AI로 간다");
                Assert.That(solo.Set(SoloScreen.TrainerCards, "off"), Is.True, "트레이너 카드를 끈 판도 시작할 수 있다(말 = 주인공 도트)");
                Assert.That(solo.Set(SoloScreen.TrainerCards, "on"), Is.True);
                // 「주인공」 줄(결정 52·60): 8명의 필드 도트, 기본은 Pt 남자 주인공. 고르면 설정에 실린다
                Assert.That(solo.Settings.Protagonist, Is.EqualTo(Protagonists.Default));
                foreach (string key in Protagonists.Keys)
                {
                    var opt = stage.Root.Q("solo-opt-" + SoloScreen.Protagonist + "-" + key);
                    Assert.That(opt, Is.Not.Null, key);
                    Assert.That(opt.Q(className: "sn-proto").childCount, Is.EqualTo(1), key + ": 필드 도트 그림이 있다");
                    Assert.That(opt.ClassListContains("sn-opt-picked"), Is.EqualTo(key == Protagonists.Default), key);
                }
                Assert.That(stage.Root.Q("solo-opt-" + SoloScreen.Protagonist + "-pt_player_male").worldBound.width, Is.EqualTo(64f + 2f * 3f + 2f).Within(0.01f), "도트 칸 = 32의 2배(1080 벌) + 안쪽 여백·테두리");
                Assert.That(solo.Set(SoloScreen.Protagonist, "hgss_001_lyra"), Is.True);
                Assert.That(solo.Settings.Protagonist, Is.EqualTo("hgss_001_lyra"));
                Assert.That(stage.Root.Q("solo-opt-" + SoloScreen.Protagonist + "-hgss_001_lyra").ClassListContains("sn-opt-picked"));
                Assert.That(stage.Root.Q("solo-opt-" + SoloScreen.Protagonist + "-pt_player_male").ClassListContains("sn-opt-picked"), Is.False);
                Assert.That(solo.RowEnabled(SoloScreen.Onslaught), Is.False, "울트라 버스트 전용 줄은 신오의 심장에서 듣지 않는다");
                Assert.That(solo.Set(SoloScreen.UbDifficulty, "master"), Is.False);
                Assert.That(solo.Settings.UbDifficulty, Is.EqualTo(UbDifficulty.NewTrainer));
                Assert.That(stage.Root.Q("solo-row-" + SoloScreen.UbDifficulty).ClassListContains("sn-off"));
                // 울트라 버스트를 고르면 그 판에서만 듣는 줄(울트라 온슬롯 · 난이도)이 켜진다 — 온슬롯 기본은 끔(A21_2)
                Assert.That(solo.Set(SoloScreen.Campaign, "ub"), Is.True);
                Assert.That(solo.RowEnabled(SoloScreen.Onslaught), Is.True);
                Assert.That(stage.Root.Q("solo-row-" + SoloScreen.Onslaught).ClassListContains("sn-off"), Is.False);
                Assert.That(stage.Root.Q("solo-opt-" + SoloScreen.Onslaught + "-off").ClassListContains("sn-opt-picked"), "온슬롯 기본 = 사용 안 함");
                Assert.That(solo.Set(SoloScreen.UbDifficulty, "master"), Is.True);
                Assert.That(solo.Settings.ToOptions().UbDifficulty, Is.EqualTo(UbDifficulty.MasterTrainer));
                Assert.That(solo.Set(SoloScreen.UbDifficulty, "new"), Is.True);
                Assert.That(solo.Set(SoloScreen.Campaign, "hos"), Is.True);
                // 설명은 마우스 오버: 아직 고를 수 없는 값은 준비 중이라고 적는다
                stage.Tips.Show(stage.Root.Q("solo-opt-rules-custom"));   // 강한 AI는 M8 「연결」부터 고를 수 있다 — 아직 없는 값은 세부 규칙 「직접 설정」
                var tipLabels = stage.Tips.Element.Query<Label>().ToList();
                Assert.That(tipLabels[0].text, Is.EqualTo("직접 설정"));
                Assert.That(tipLabels[1].text.Replace("⁠", ""), Is.EqualTo("아직 준비 중입니다."));
                Assert.That(tipLabels[1].ClassListContains(Kit.F2) && !tipLabels[1].ClassListContains(Kit.F1), "설명 문장은 본문 크기(1080 벌 24px)");
                stage.Tips.Hide();
                var panel = stage.Root.Q("solo-panel").worldBound;
                Assert.That(panel.xMax, Is.EqualTo(SoloScreen.ColumnRight * 1.5f).Within(0.01f), "SoloScreen.ColumnRight = USS의 판 오른쪽 변(1080 벌 ×1.5)");
                yield return null;
                Assert.That(rig.Target, Is.EqualTo(BoardBackdrop.Target(1920, 1080, SoloScreen.ColumnRight)), "설정 뒤 보드 자리");
                Assert.That(TitleScreenTests.BoardLeftEdge(cam, 1920, 1080), Is.GreaterThanOrEqualTo(panel.xMax), "보드가 설정 판 밑으로 들어가지 않는다");
                Assert.That(rig.InputEnabled, Is.False, "솔로 설정: 보드 입력 끔");

                // 뒤로 → 타이틀 → 다시 혼자 하기, 시작 → 뒤로(설정으로)
                solo.Back();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
                ((TitleScreen)stage.Current).Activate(soloIndex);
                yield return null;
                solo = (SoloScreen)stage.Current;
                Assert.That(solo.Set(SoloScreen.GalacticAi, "basic"), Is.True, "이 판은 원칙 AI로 간다(기본값은 「강함」)");
                solo.Start();   // 시드는 흐름이 정한다(아무 시드나)
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<SetupVillageScreen>());
                Assert.That(flow.Session, Is.Not.Null);
                ((SetupVillageScreen)stage.Current).Back();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<SoloScreen>());
                Assert.That(flow.Session, Is.Null, "뒤로 가면 그 게임을 떠난다");
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(6), "게임을 떠나면 시험 판 말로 돌아온다");

                // ── 게임 시작(고정 시드) → S12 고향 마을 ──
                Assert.That(((SoloScreen)stage.Current).Settings.Protagonist, Is.EqualTo(Protagonists.Default), "시작하지 않고 떠난 화면의 값은 남지 않는다(시작한 판의 값만 남는다)");
                Assert.That(((SoloScreen)stage.Current).Set(SoloScreen.Protagonist, "hgss_001_lyra"), Is.True);
                Assert.That(((SoloScreen)stage.Current).Set(SoloScreen.GalacticAi, "basic"), Is.True, "원칙 AI로");
                Assert.That(((SoloScreen)stage.Current).Set(SoloScreen.TrainerCards, "on"), Is.True, "이 판은 트레이너 카드를 쓴다(기본값은 끔 — 결정 134)");
                flow.StartSolo(((SoloScreen)stage.Current).Settings, 11, 99);
                yield return null;
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<SetupVillageScreen>());
                var village = (SetupVillageScreen)stage.Current;
                var client = flow.Client;
                Assert.That(client.View.Seats.Length, Is.EqualTo(1), "솔로 = 좌석 하나");
                var legal = client.Legal(client.Choice(ChoiceKind.VillagePick));
                Assert.That(village.Villages, Is.EqualTo(legal.ConvertAll(a => a.Target)), "화면의 줄 = 엔진의 합법 답");
                Assert.That(village.Villages.Count, Is.EqualTo(8));
                Assert.That(stage.Root.Q("setup-village-item-ultra_megalopolis"), Is.Null, "울트라메가로폴리스는 울트라 버스트에서만(G12)");
                int stark = -1;
                for (int i = 0; i < village.Villages.Count; i++) if (flow.Ctx.Fixtures.Fixtures[village.Villages[i]].Key == "village.stark") stark = i;
                Assert.That(stage.Root.Q("setup-village-item-stark").Q<Label>(className: Kit.F2).text, Is.EqualTo("무쇠시티"), "마을 이름은 문자열 표에서");
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(0), "게임이 시작되면 시험 판 말을 치운다(아직 고른 것이 없다)");
                Assert.That(rig.InputEnabled, Is.False, "셋업: 보드 입력 끔");
                Assert.That(stage.Root.Q<Button>("setup-village-confirm").enabledSelf, Is.False);
                Assert.That(village.Confirm(), Is.False, "고르기 전에는 확정하지 못한다");
                village.Pick(stark);
                yield return null;
                yield return null;
                Assert.That(stage.Root.Q("setup-village-item-stark").ClassListContains("sn-picked"));
                Assert.That(stage.Root.Q<Button>("setup-village-confirm").enabledSelf, Is.True);
                Assert.That(stage.Root.Q("setup-village-art").resolvedStyle.backgroundImage.texture, Is.Not.Null, "마을 카드 그림(Gen)");
                Assert.That(stage.Root.Q("setup-village-detail").Query(className: "sn-mon").ToList().Count, Is.EqualTo(2), "스타팅 포켓몬 둘");
                Assert.That(stage.Root.Q("setup-village-col").worldBound.xMax, Is.EqualTo(SetupVillageScreen.ColumnRight * 1.5f).Within(0.01f), "SetupVillageScreen.ColumnRight = USS");
                Assert.That(client.View.Seats[0].HomeVillage, Is.EqualTo(-1), "고르기만 해서는 답을 내지 않는다");
                int starkFixture = village.Villages[stark];
                Assert.That(village.Confirm(), Is.True);

                // ── 고향 마을 연출(04 §8-8 2행, 결정 66 ③): 마을 화면이 걷히고 좌석 띠가 뜨며 카메라가 마을로 다가가고 말이 떨어져 내려앉는다 ──
                Assert.That(stage.Current, Is.InstanceOf<SetupDropScreen>(), "마을 화면이 걷히고 좌석 띠만 뜬다");
                var drop = (SetupDropScreen)stage.Current;
                int homeCell = flow.Ctx.Fixtures.Fixtures[starkFixture].Cell;
                int villageMs = PlayTiming.RowMs(new VillagePicked(), VillageBeats.Row);
                Assert.That(flow.SetupOpen, Is.False, "연출이 도는 동안 셋업 입력은 닫혀 있다");
                Assert.That(flow.QueueLeftMs, Is.EqualTo(villageMs + PlayTiming.RowMs(null, SetupRows.Deal) + FxTune.ClickGuardMs.Value), "고향 마을 1800 + 카드 받기 800 + 클릭 막기");
                var early = board.Pieces.Of(0);
                Assert.That(early == null || !early.Root.activeSelf, Is.True, "떨어지기 전에는 말이 보이지 않는다");
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Full));
                int fallAt = VillageBeats.FallAt(villageMs), landAt = villageMs * VillageBeats.LandPermille / 1000;
                stage.Tweens.Advance(fallAt + 100);
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Region), "카메라가 먼저 지역 줌으로 다가갔다");
                Assert.That(rig.Target, Is.EqualTo(board.CellTop(homeCell)), "고른 마을을 본다");
                var falling = board.Pieces.Of(0);
                Assert.That(falling, Is.Not.Null, "말은 고향 마을을 고른 좌석부터 선다");
                Assert.That(falling.Root.activeSelf, Is.True);
                Assert.That(falling.Style, Is.EqualTo(PieceStyle.Dot), "트레이너 카드를 고르기 전에는 주인공 도트");
                Assert.That(falling.Root.transform.localPosition.y, Is.GreaterThan(board.CellTop(homeCell).y + 1f), "위에서 떨어지는 중");
                Assert.That(drop.Seat, Is.EqualTo(0));
                Assert.That(drop.Coloured, Is.False, "내려앉기 전에는 좌석 색이 없다");
                Assert.That(FaceBox.SeatColorOf(stage.Root.Q("setup-drop-face")), Is.EqualTo(SeatColors.Unassigned));
                Assert.That(FaceBox.Shown(stage.Root.Q("setup-drop-face")), Is.EqualTo(flow.Protagonist(0)), "좌석 띠의 얼굴 = 그 좌석의 주인공");
                Assert.That(stage.Root.Q<Label>("setup-drop-village").text, Is.EqualTo("무쇠시티"));
                foreach (var e in stage.Root.Q("setup-drop-band").Query().ToList()) Assert.That(e.pickingMode, Is.EqualTo(PickingMode.Ignore), "좌석 띠는 마우스를 받지 않는다: " + e.name);
                stage.Tweens.Advance(landAt - fallAt - 100 + 300);
                Assert.That(drop.Coloured, Is.True, "내려앉으면 좌석 색으로 칠해진다");
                Assert.That(FaceBox.SeatColorOf(stage.Root.Q("setup-drop-face")), Is.EqualTo(SeatColors.ForVillage("village.stark")));
                Assert.That(board.Pieces.Of(0).Root.transform.localPosition.y, Is.EqualTo(board.CellTop(homeCell).y).Within(0.05f), "내려앉았다");
                stage.Tweens.Advance(villageMs - landAt - 300);

                // ── 트레이너 카드 받기(1행): 고르는 화면이 열리며 카드 3장이 뒷면으로 들어와 앞면으로 뒤집힌다. 그동안에는 고르지 못한다 ──
                Assert.That(stage.Current, Is.InstanceOf<SetupPickScreen>(), "고향 마을 연출이 끝나면 고르는 화면");
                var dealt = (SetupPickScreen)stage.Current;
                Assert.That(dealt.CardsFaceUp, Is.EqualTo(0), "카드는 뒷면으로 들어온다");
                Assert.That(stage.Root.Q("setup-pick-card-row").ClassListContains(SetupPickScreen.DealClip), Is.True, "들어오는 동안 카드는 판 안에서 잘린다(결정 70 ④)");
                dealt.PickStarter(0);
                Assert.That(dealt.Starter, Is.EqualTo(-1), "들어오는 동안에는 골라지지 않는다");
                stage.Tweens.Advance(PlayTiming.RowMs(null, SetupRows.Deal));
                Assert.That(dealt.CardsFaceUp, Is.EqualTo(3), "다 들어와 앞면");
                Assert.That(stage.Root.Q("setup-pick-card-row").ClassListContains(SetupPickScreen.DealClip), Is.False, "다 들어오면 자르지 않는다(올린 카드·들리는 카드는 줄 밖으로)");
                Assert.That(flow.SetupOpen, Is.False, "클릭 막기 300");
                stage.Tweens.Advance(FxTune.ClickGuardMs.Value);
                Assert.That(flow.SetupOpen, Is.True);

                // ── S12 스타팅 포켓몬 + S13 트레이너 카드 ──
                yield return null;
                yield return null;
                Assert.That(client.View.Seats[0].HomeVillage, Is.EqualTo(starkFixture));
                Assert.That(stage.Current, Is.InstanceOf<SetupPickScreen>());
                var pick = (SetupPickScreen)stage.Current;
                Assert.That(pick.Starters, Is.EqualTo(flow.Ctx.Fixtures.Fixtures[starkFixture].Starters), "고른 마을의 스타팅 포켓몬 둘");
                Assert.That(pick.Cards, Is.EquivalentTo(client.View.Seats[0].TrainerCardOffer), "받은 트레이너 카드 3장");
                Assert.That(pick.Cards.Count, Is.EqualTo(3));
                foreach (int p in pick.Starters)
                    Assert.That(stage.Root.Q("setup-pick-starter-" + flow.Ctx.Cards.Pokemon[p].Key.Replace(':', '-')), Is.Not.Null);
                foreach (int c in pick.Cards)
                {
                    string key = flow.Ctx.Cards.TrainerCards[c].Key;
                    var tile = stage.Root.Q("setup-pick-card-" + key);
                    Assert.That(tile, Is.Not.Null, key);
                    Assert.That(tile.Q(className: "sn-trainer-art").resolvedStyle.backgroundImage.texture, Is.Not.Null, key + " 직업 그림");
                    Assert.That(tile.worldBound.yMax, Is.LessThanOrEqualTo(1080f), "칸이 화면 안");
                    Assert.That(tile.worldBound.xMax, Is.LessThanOrEqualTo(1920f), "칸이 화면 안");
                    // 능력 문구는 칸에 바로 적는다(결정 56 ② — 문자열 표의 문구, 본문 크기, 칸 안에서 감긴다)
                    foreach (string slot in new[] { "1", "2" })
                    {
                        var block = tile.Q("setup-pick-card-" + key + "-ability-" + slot);
                        Assert.That(block, Is.Not.Null, key + " 능력 " + slot);
                        var text = block.Q<Label>("setup-pick-card-" + key + "-ability-" + slot + "-text");
                        Assert.That(text.text.Replace("⁠", ""), Is.EqualTo(stage.T("trainer_ability." + key + "." + slot + ".text")));
                        Assert.That(text.ClassListContains(Kit.F2), "능력 문구는 본문 크기(1080 벌 24px)");
                        Assert.That(text.worldBound.xMin, Is.GreaterThanOrEqualTo(tile.worldBound.xMin - 0.5f), key + " 문구가 칸 왼쪽 안");
                        Assert.That(text.worldBound.xMax, Is.LessThanOrEqualTo(tile.worldBound.xMax + 0.5f), key + " 문구가 칸 오른쪽 안");
                        Assert.That(text.worldBound.yMax, Is.LessThanOrEqualTo(tile.worldBound.yMax + 0.5f), key + " 문구가 칸 아래쪽 안");
                        Assert.That(text.worldBound.height, Is.GreaterThan(0f));
                    }
                }
                // 고유 기술 효과는 마우스 오버 그대로(문자열 표의 문구, 본문 크기)
                string firstMon = flow.Ctx.Cards.Pokemon[pick.Starters[0]].Key.Replace(':', '-');
                stage.Tips.Show(stage.Root.Q("setup-pick-starter-" + firstMon + "-move"));
                var moveTip = stage.Tips.Element.Query<Label>().ToList()[1];
                Assert.That(moveTip.text.Length, Is.GreaterThan(0));
                Assert.That(moveTip.ClassListContains(Kit.F2) && !moveTip.ClassListContains(Kit.F1), "효과 문장은 본문 크기(1080 벌 24px)");
                stage.Tips.Hide();
                Assert.That(pick.CanConfirm, Is.False);
                Assert.That(pick.Confirm(), Is.False, "둘 다 골라야 확정한다");
                pick.PickStarter(1);
                Assert.That(pick.CanConfirm, Is.False, "트레이너 카드도 골라야 한다(A20-1)");
                Assert.That(stage.Root.Q<Button>("setup-pick-confirm").enabledSelf, Is.False);
                pick.PickCard(2);
                Assert.That(stage.Root.Q<Button>("setup-pick-confirm").enabledSelf, Is.True);
                int starter = pick.Starters[1], card = pick.Cards[2];
                Assert.That(client.View.Phase, Is.EqualTo(Phase.Setup));
                Assert.That(pick.Confirm(), Is.True);

                // ── 고른 뒤 · 공개(1·3행): 보기는 답과 함께 바로 첫 턴이지만 고르는 화면이 남아 연출을 끝까지 보인다 ──
                Assert.That(client.View.Phase, Is.Not.EqualTo(Phase.Setup));
                Assert.That(stage.Current, Is.SameAs(pick), "고르는 화면이 남는다");
                Assert.That(stage.Hud, Is.Null, "보드 HUD는 셋업 연출 뒤에 뜬다");
                Assert.That(pick.StarterSent && pick.CardSent, Is.True);
                Assert.That(pick.Confirm(), Is.False, "낸 뒤에는 다시 내지 못한다");
                Assert.That(stage.Root.Q<Label>("setup-pick-guide").text, Is.EqualTo(stage.T("screen.setup.pick.done")));
                Assert.That(stage.Root.Q<Button>("setup-pick-confirm").enabledSelf, Is.False);
                Assert.That(flow.CanOpenParty, Is.False);
                stage.Tweens.Advance(PlayTiming.RowMs(null, SetupRows.Chosen));
                Assert.That(pick.BallShown, Is.True, "고른 스타팅 포켓몬은 볼로 들어갔다(공개 전)");
                Assert.That(pick.MonShown, Is.EqualTo(0f));
                Assert.That(pick.CardsFaceUp, Is.EqualTo(3), "트레이너 카드의 「고른 뒤」는 아직");
                stage.Tweens.Advance(flow.MsUntil(s => s.Row == SetupRows.Starter));
                Assert.That(pick.CardsFaceUp, Is.EqualTo(1), "고른 카드는 앞면 그대로(결정 104 ③ — 뒷면으로 뒤집지 않는다), 나머지 둘은 덱으로");
                stage.Tweens.Advance(flow.MsUntil(s => s.Row == SetupRows.Starter, 600));
                Assert.That(pick.MonShown, Is.EqualTo(1f), "볼이 열려 도트가 나타났다");
                Assert.That(pick.BallShown, Is.False);
                stage.Tweens.Advance(flow.MsUntil(s => s.Row == SetupRows.Reveal, 300));
                Assert.That(pick.CardsFaceUp, Is.EqualTo(1), "고른 카드는 공개 때도 앞면 그대로 들린다(다시 뒤집지 않는다)");
                Assert.That(board.Tokens.Shown.Count, Is.EqualTo(0), "숨겨진 장소 토큰은 아직 놓이지 않았다");
                Assert.That(stage.Current, Is.SameAs(pick));

                // ── 숨겨진 장소 배치(5행): 화면이 닫히고 HUD가 뜨며, 카메라가 전체 줌으로 물러나고 토큰이 칸 색인 순서로 떨어진다 ──
                stage.Tweens.Advance(flow.MsUntil(s => SpotDropBeats.Spots(s) != null) + SpotDropBeats.LeadMs + 300);
                Assert.That(stage.Current, Is.Null, "셋업 연출이 끝나 고르는 화면이 닫혔다");
                Assert.That(stage.Hud, Is.InstanceOf<BoardHud>());
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Full), "전체 줌으로 물러났다");
                Assert.That(board.Tokens.SpotsDropping, Is.True);
                Assert.That(board.Tokens.SpotsVisible, Is.GreaterThan(0).And.LessThan(board.Tokens.Shown.Count), "차례로 떨어지는 중");
                flow.Fx.SetPaused(true);   // 아래 보드 확인이 끝날 때까지 다음 연출(서막 카드 …)을 세워 둔다 — 프레임이 느려도 같은 순서로 본다
                stage.Tweens.Advance(PlayTiming.RowMs(new TokensPlaced { Villages = new int[0] }, SpotDropBeats.Row));
                Assert.That(board.Tokens.SpotsDropping, Is.False, "다 놓였다");
                Assert.That(board.Tokens.SpotsVisible, Is.EqualTo(board.Tokens.Shown.Count));

                // ── 보드: 게임 상태로 다시 섰다 ──
                yield return null;
                yield return null;
                var view = flow.Client.View;
                Assert.That(view.Phase, Is.EqualTo(Phase.Turn), "셋업 끝 → 첫 턴");
                Assert.That(flow.OnBoard, Is.True);
                Assert.That(stage.Current, Is.Null, "셋업 화면은 닫혔다(HUD 위에 창 없음)");
                Assert.That(stage.Hud, Is.InstanceOf<BoardHud>(), "보드 HUD가 떠 있다");
                Assert.That(stage.Root.Query(className: "sn-screen").ToList().Count, Is.EqualTo(1), "HUD 하나뿐");
                Assert.That(stage.Root.Q("board-hud"), Is.Not.Null);
                Assert.That(view.Seats[0].TrainerCard, Is.EqualTo(card));
                Assert.That(view.Pokemon[view.Seats[0].Party[0]].Def, Is.EqualTo(starter));
                Assert.That(flow.Client.Choice(ChoiceKind.MapAction), Is.Not.Null, "첫 턴의 행동 선택이 열려 있다");
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(1), "말 = 실제 좌석 하나");
                var piece = board.Pieces.Pieces[0];
                Assert.That(piece.Seat, Is.EqualTo(0));
                // 말 = 솔로 설정에서 고른 주인공의 필드 도트(기본 양식 — 결정 52·60), 배틀의 뒷모습도 같은 인물이다
                Assert.That(flow.Settings.Protagonist, Is.EqualTo("hgss_001_lyra"), "고른 주인공이 판에 실렸다");
                Assert.That(flow.Protagonist(0), Is.EqualTo("hgss_001_lyra"));
                Assert.That(flow.PieceStyle, Is.EqualTo(PieceStyle.Dot), "말 그림의 기본 = 주인공 도트");
                Assert.That(piece.Style, Is.EqualTo(PieceStyle.Dot));
                Assert.That(piece.Protagonist, Is.EqualTo("hgss_001_lyra"));
                Assert.That(piece.TextureKey, Is.EqualTo("Field/hgss_f.png"));
                Assert.That(piece.Facing, Is.EqualTo(Facing.South), "서 있으면 앞모습");
                Assert.That(BackThrow.Find(GenAssets.Load<TextAsset>(BoardPieces.TableKey).text, flow.Protagonist(0)).File, Is.EqualTo("Back/official/hgss_001_lyra.png"), "뒷모습 던지기 = 같은 주인공");
                Assert.That(piece.Trainer, Is.EqualTo(flow.Ctx.Cards.TrainerCards[card].Key), "직업 = 고른 트레이너 카드");
                // 말 그림 설정을 직업 그림으로 바꾸면 그 자리에 직업 그림으로 다시 서고, 되돌리면 주인공 도트다
                flow.PieceStyle = PieceStyle.Job;
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(1));
                Assert.That(board.Pieces.Pieces[0].Style, Is.EqualTo(PieceStyle.Job));
                Assert.That(board.Pieces.Pieces[0].TextureKey, Is.EqualTo("Trainers/Front/" + piece.Trainer + ".png"));
                Assert.That(board.Pieces.Pieces[0].Spot.Cell, Is.EqualTo(piece.Spot.Cell), "양식을 바꿔도 같은 칸");
                flow.PieceStyle = PieceStyle.Dot;
                piece = board.Pieces.Pieces[0];
                Assert.That(piece.Style, Is.EqualTo(PieceStyle.Dot));
                Assert.That(piece.Village, Is.EqualTo("village.stark"));
                Assert.That((Color32)piece.Color, Is.EqualTo(SeatColors.ForVillage("village.stark")), "좌석 색 = 고향 마을 색");
                int home = flow.Ctx.Fixtures.Fixtures[starkFixture].Cell;
                Assert.That(piece.Spot.Cell, Is.EqualTo(home), "말이 고향 마을에 선다");
                Assert.That(rig.InputEnabled, Is.True, "보드: 보드 입력 켬");
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Full), "게임 시작의 연출 동안 전체 줌(04 §8-8 5행) — 첫 내 턴 컷신이 내 말로 데려온다");
                Assert.That(rig.Target, Is.EqualTo(flow.WideLook()), "전체 줌의 보는 점 = 판이 위 띠와 아래 판 사이의 가운데에 오는 자리");
                Assert.That(rig.Target, Is.Not.EqualTo(Vector3.zero), "화면 한가운데가 아니다(판 아래쪽이 아래 판에 가린다)");
                // 세션 시각이 프레임마다 흐른다
                long before = flow.NowMs;
                for (int i = 0; i < 5; i++) yield return null;
                Assert.That(flow.NowMs, Is.GreaterThan(before));
                Assert.That(flow.Session.CommandCount, Is.EqualTo(3), "답 셋(마을·스타팅 포켓몬·트레이너 카드)");

                // ── 보드 HUD: 게임 첫 턴은 내 턴 컷신이 끝나야 입력이 열린다(04 §8-8 101행) ──
                var hud = flow.Hud;
                Assert.That(flow.Fx.Sequencer.Pending, Is.GreaterThan(0), "첫 턴의 연출이 기다리고 있다(위에서 세워 둠)");
                Assert.That(flow.InputOpen, Is.False, "컷신이 끝나기 전에는 입력 닫힘");
                Assert.That(hud.Reach, Is.Empty, "입력이 닫혀 있으면 이동 표시도 없다");
                Assert.That(board.Marks.Visible, Is.False);
                Assert.That(stage.Root.Q<Label>("board-hud-guide").text, Is.EqualTo("잠시 기다려 주십시오."));
                Assert.That(hud.Press(BoardHud.EndTurn), Is.False, "연출 중에는 단추를 눌러도 답을 내지 않는다");
                flow.Fx.SetPaused(false);

                // ── 게임 시작의 연출(04 §8-8 4·5행, S14): 서막 카드 → 고향 마을에 조무래기 토큰 → 1막 카드 → 내 턴 컷신 ──
                var tokens = board.Tokens;
                int spotsInView = 0, spotsShown = 0;
                foreach (var t in view.Tokens) if (t.Kind == TokenKind.HiddenSpot && t.Cell >= 0) spotsInView++;
                foreach (var t in tokens.Shown) if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp) spotsShown++;
                Assert.That(spotsInView, Is.GreaterThan(0));
                Assert.That(spotsShown, Is.EqualTo(spotsInView), "숨겨진 장소·유물 토큰이 뒷면으로 보드에 놓였다");
                bool anySpecial = false;
                foreach (var t in view.Tokens) if (t.Kind == TokenKind.HiddenSpot && t.Cell >= 0 && !t.FaceUp && t.Special) anySpecial = true;
                Assert.That(tokens.SpotRenderers.Count, Is.EqualTo(anySpecial ? 2 : 1), "뒷면 그림은 정체와 무관하게 같은 모양(B2) — 금색 ?(특별한 숨겨진 장소, 공개 값 — M6 표시)만 금빛 재질 하나 더");
                Assert.That(tokens.GenMissing, Is.False);
                Assert.That(tokens.GruntAt(home), Is.Null, "조무래기 토큰은 서막의 놓기 연출에서 나타난다");
                // 글도 보드 위 토큰과 같은 때에 바뀐다: 놓이기 전에는 칸 정보 줄 · 공개 정보 카드에 조무래기가 없다
                Assert.That(hud.CellText(home), Does.Not.Contain(stage.T("screen.token.grunt")), "서막 카드가 떠 있는 동안");
                Assert.That(stage.Root.Q<Label>("board-hud-cell").text, Does.Not.Contain(stage.T("screen.token.grunt")));
                Assert.That(hud.ShowCellInfo(home), Is.False, "아직 놓이지 않은 조무래기는 카드에 없다");
                var actBadge = stage.Root.Q<Label>("board-hud-act");
                Assert.That(actBadge.style.display.value, Is.EqualTo(DisplayStyle.None), "서막 카드가 착지하기 전에는 막 배지가 없다");
                Assert.That(hud.ActCard.Active, Is.False);
                stage.Tweens.Advance(1600);
                Assert.That(hud.ActCard.ShownAct, Is.EqualTo(0), "서막 카드");
                Assert.That(stage.Root.Q<Label>("act-card-act").text, Is.EqualTo("서막"));
                Assert.That(hud.ActCard.Stamped, Is.EqualTo(2), "제목 「침입」 두 글자가 박혔다");
                Assert.That(stage.Root.Q<Label>("act-card-goal").text.Replace("\u2060", ""), Is.EqualTo(stage.T("screen.act.goal.0")));
                Assert.That(stage.Root.Q("act-card-panel").style.opacity.value, Is.EqualTo(1f));
                Assert.That(stage.Root.Q("act-card-scrim").style.opacity.value, Is.EqualTo(1f).Within(1e-4f), "보드가 어두워졌다");
                Assert.That(stage.Root.Q("act-card-reward").style.display.value, Is.EqualTo(DisplayStyle.None), "서막은 보상 줄이 없다");
                foreach (var e in stage.Root.Q("act-card").Query().ToList()) Assert.That(e.pickingMode, Is.EqualTo(PickingMode.Ignore), "막 카드는 마우스를 받지 않는다: " + e.name);
                Assert.That(flow.InputOpen, Is.False, "막 카드는 끝까지 막는 컷신이다");
                stage.Tweens.Advance(3600 - 1600 + 100);
                Assert.That(hud.ActCard.Active, Is.False, "서막 카드가 걷혔다");
                Assert.That(actBadge.text, Is.EqualTo("서막"), "착지하면 위 띠 막 배지가 선다");
                var grunt = tokens.GruntAt(home);
                Assert.That(grunt, Is.Not.Null, "놓기 단계가 시작되면 고향 마을에 조무래기 토큰이 생긴다");
                Assert.That(stage.Root.Q<Label>("board-hud-cell").text, Does.EndWith(stage.T("screen.token.grunt")), "토큰이 놓이면 칸 정보 줄에 선다");
                Assert.That(hud.ShowCellInfo(home), Is.True);
                Assert.That(stage.Root.Q("info-card-token-grunt"), Is.Not.Null);
                hud.HideInfo();
                Assert.That(tokens.Dropping, Is.True, "떨어지는 중");
                Assert.That(grunt.gameObject.activeSelf, Is.False, "예비 300 동안은 아직 안 보인다");
                stage.Tweens.Advance(360);
                Assert.That(grunt.gameObject.activeSelf, Is.True);
                Assert.That(grunt.localPosition.y, Is.GreaterThan(board.CellTop(home).y + 0.5f), "위에서 떨어지는 중");
                stage.Tweens.Advance(1800 - 460 + 1600);
                Assert.That(grunt.localPosition.y, Is.EqualTo(board.CellTop(home).y).Within(1e-4f), "내려앉았다");
                Assert.That(grunt.localScale, Is.EqualTo(Vector3.one * BoardTokens.FullZoomScale), "전체 줌: 키운 크기");
                Assert.That(hud.ActCard.ShownAct, Is.EqualTo(1), "1막 카드");
                Assert.That(stage.Root.Q<Label>("act-card-reward").text, Is.EqualTo(stage.T("screen.act.reward.1")), "1막: 빨강 규토리 +3");
                Assert.That(actBadge.text, Is.EqualTo("서막"), "1막 카드가 착지하기 전");
                Assert.That(hud.RecentLog, Has.Member("갤럭시단 조무래기가 고향 마을을 점거했다!"), "기록 줄은 그 행동의 연출이 끝난 뒤에 붙는다");
                Assert.That(hud.RecentLog, Has.No.Member("1막 「여정」이 시작되었다"));
                Assert.That(flow.OpeningMs, Is.EqualTo(2000), "1막 카드의 남은 길이");
                stage.Tweens.Advance(flow.OpeningMs);
                Assert.That(actBadge.text, Is.EqualTo("1막"));
                Assert.That(hud.ActCard.Active, Is.False);
                Assert.That(hud.RecentLog, Has.Member("1막 「여정」이 시작되었다"));
                yield return Until(() => hud.Band.ActiveRow == FxRows.MyTurn, 5f);
                Assert.That(hud.Band.ActiveRow, Is.EqualTo(FxRows.MyTurn), "내 턴 컷신");
                Assert.That(stage.Root.Q("turn-band-cut").style.opacity.value, Is.GreaterThan(0f));
                Assert.That(stage.Root.Q("turn-band-strip").style.opacity.value, Is.EqualTo(0f), "게임 첫 턴은 넘김 띠 없이 컷신만");
                Assert.That(FaceBox.Shown(stage.Root.Q("turn-band-face-cut")), Is.EqualTo(flow.Protagonist(0)), "내 턴 컷신의 얼굴 상자 = 턴이 온 좌석의 주인공(결정 61 ③)");
                Assert.That(FaceBox.SeatColorOf(stage.Root.Q("turn-band-face-cut")), Is.EqualTo(SeatColors.ForVillage("village.stark")), "컷신 얼굴 상자 테두리 = 좌석 색");
                yield return Until(() => flow.InputOpen, 10f);
                Assert.That(flow.InputOpen, Is.True, "컷신이 끝나면 입력이 열린다");
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Region), "첫 내 턴 컷신이 지역 줌으로 돌아왔다");
                Assert.That(rig.Target, Is.EqualTo(board.CellTop(home)), "카메라가 내 말이 선 칸을 본다");
                Assert.That(grunt.localScale, Is.EqualTo(Vector3.one), "지역 줌: 제 크기");
                Assert.That(boot.Root.Stage.Tweens.Busy, Is.EqualTo(0), "연출이 끝나면 실행기가 빈다(유휴)");
                Assert.That(stage.Root.Q("turn-band-cut").style.opacity.value, Is.EqualTo(0f));

                // ── 기록(S21) · 칸 정보 줄 · 공개 정보 카드 ──
                Assert.That(hud.RecentLog.Count, Is.EqualTo(BoardHud.RecentLines), "왼쪽 위 기록 판 = 최근 줄 셋");
                Assert.That(hud.RecentLog[BoardHud.RecentLines - 1], Is.EqualTo("플레이어 1은 빨강 규토리 3개를 받았다"));
                Assert.That(hud.CellInfoText, Does.Contain("무쇠시티").And.Contain("갤럭시단 조무래기").And.Contain("높이"), "마우스가 보드 밖이면 내가 선 칸");
                Assert.That(BoardHud.UnderPanel(stage.Root, stage.Root.Q("board-hud-log").worldBound.center), Is.True, "기록 판도 HUD 판이다");
                flow.OpenLog();
                var logScreen = stage.Current as LogScreen;
                Assert.That(logScreen, Is.Not.Null, "[펼치기] → 기록 창");
                Assert.That(flow.InputOpen, Is.False, "기록 창이 떠 있는 동안 보드 입력은 닫힌다");
                Assert.That(rig.InputEnabled, Is.False);
                Assert.That(logScreen.Shown[0], Is.EqualTo("게임 준비"));
                Assert.That(logScreen.Shown, Has.Member("플레이어 1은 무쇠시티에서 여행을 시작했다"));
                Assert.That(logScreen.Shown, Has.Member("서막 「침입」이 시작되었다"));
                Assert.That(logScreen.Shown, Has.Member("1라운드"));
                Assert.That(logScreen.Pages, Is.EqualTo(1));
                Assert.That(logScreen.Turn(1), Is.False, "한 쪽뿐이면 넘어가지 않는다");
                flow.CloseLog();
                Assert.That(stage.Current, Is.Null);
                Assert.That(flow.InputOpen, Is.True, "닫으면 보드 입력이 다시 열린다");
                hud.ShowSeatInfo(0);
                Assert.That(hud.Info.Subject, Is.EqualTo("seat-1"), "좌석 칩 → 공개 정보 카드");
                Assert.That(stage.Root.Q("info-card-seat-1"), Is.Not.Null);
                Assert.That(stage.Root.Q("info-card-mon-0"), Is.Not.Null, "파티의 포켓몬 줄");
                foreach (var e in stage.Root.Q("info-card").Query().ToList()) Assert.That(e.pickingMode, Is.EqualTo(PickingMode.Ignore), "공개 정보 카드는 마우스를 받지 않는다: " + e.name);
                hud.HoverUi(null);
                Assert.That(hud.Info.Subject, Is.Null, "칩에서 벗어나면 걷힌다");
                hud.HoverUi(stage.Root.Q("board-hud-seat-1-face"));
                Assert.That(hud.Info.Subject, Is.EqualTo("seat-1"), "칩 안의 요소(얼굴 상자) 위여도 그 좌석의 카드");
                hud.HoverUi(stage.Root.Q("board-hud-menu"));
                Assert.That(hud.Info.Subject, Is.Null);
                hud.Hover(home);
                Assert.That(hud.Info.Subject, Is.EqualTo("cell-" + home), "내 말만 선 칸이어도 조무래기 토큰 정보는 뜬다");
                Assert.That(stage.Root.Q("info-card-token-grunt"), Is.Not.Null);
                Assert.That(stage.Root.Q("info-card-seat-1"), Is.Null, "보드 위 내 말에는 내 카드가 뜨지 않는다");
                int spotCell = -1, plainCell = -1;
                foreach (var t in flow.Client.View.Tokens) if (t.Kind == TokenKind.HiddenSpot && t.Cell >= 0 && !t.Special && spotCell < 0) spotCell = t.Cell;   // 보통 뒷면(금색 ?는 칸 정보 줄에 「(금색)」이 붙는다 — M6 표시)
                hud.Hover(spotCell);
                Assert.That(stage.Root.Q("info-card-token-hidden"), Is.Not.Null, "뒷면 숨겨진 장소");
                Assert.That(hud.CellInfoText, Does.EndWith("숨겨진 장소"), "뒷면은 정체 없이");
                for (int c = 0; c < flow.Ctx.Board.Count && plainCell < 0; c++)
                {
                    var subjectSeats = new List<int>();
                    var subjectTokens = new List<TokenState>();
                    InfoCard.Subjects(flow.Client.View, 0, c, subjectSeats, subjectTokens);
                    if (subjectSeats.Count == 0 && subjectTokens.Count == 0) plainCell = c;
                }
                hud.Hover(plainCell);
                Assert.That(hud.Info.Subject, Is.Null, "말도 토큰도 없는 칸에는 카드가 없다(칸 정보 줄만 바뀐다)");
                hud.Hover(-1);
                Assert.That(hud.CellInfoText, Does.Contain("무쇠시티"));
                // 남 좌석의 말: 보는 좌석이 아니면 그 좌석의 카드 대상이다(혼자 하기에는 남 좌석이 없어 셈만 본다)
                {
                    var seatsThere = new List<int>();
                    var tokensThere = new List<TokenState>();
                    InfoCard.Subjects(flow.Client.View, 1, home, seatsThere, tokensThere);
                    Assert.That(seatsThere, Is.EqualTo(new[] { 0 }));
                    Assert.That(tokensThere.Count, Is.EqualTo(1));
                }

                // 위 띠 · 아래 판: 글은 문자열 표, 값은 보기
                Assert.That(stage.Root.Q<Label>("board-hud-turn").text, Is.EqualTo("내 턴"));
                Assert.That(stage.Root.Q<Label>("board-hud-round").text, Is.EqualTo("1라운드"));
                Assert.That(stage.Root.Q<Label>("board-hud-act").text, Is.EqualTo(stage.T("screen.board.act." + view.Hos.Act)));
                Assert.That(stage.Root.Q("board-hud-seat-1").ClassListContains("sn-hud-seat-turn"), "턴인 좌석 칩 강조");
                Assert.That(stage.Root.Q("board-hud-seat-1").Q<Label>(className: "sn-hud-seat-name").text, Is.EqualTo("플레이어 1"));
                // 내 자원 줄(결정 98): 마우스를 올리지 않아도 규토리 일곱 색 · 여행 점수 · 볼 넷이 보기의 값으로 서 있다(0인 칸은 흐리게)
                var mySeat = view.Seats[flow.Client.Seat];
                for (int c = 0; c < mySeat.Apricorns.Length; c++)
                {
                    var item = stage.Root.Q(BoardHud.Res + "-apricorn-" + ((Sinnoh.Rules.ApricornColor)c).ToString().ToLowerInvariant());
                    Assert.That(item, Is.Not.Null, "자원 줄 규토리 칸 " + c);
                    Assert.That(item.Q<Label>().text, Is.EqualTo(mySeat.Apricorns[c].ToString()), "자원 줄 규토리 " + c);
                    Assert.That(item.ClassListContains(BoardHud.ResZero), Is.EqualTo(mySeat.Apricorns[c] == 0), "0인 칸만 흐리다 " + c);
                }
                for (int b = 0; b < mySeat.Balls.Length; b++)
                    Assert.That(stage.Root.Q(BoardHud.Res + "-ball-" + b).Q<Label>().text, Is.EqualTo(mySeat.Balls[b].ToString()), "자원 줄 볼 " + b);
                Assert.That(stage.Root.Q<Label>(BoardHud.Res + "-jp").text, Is.EqualTo(stage.T("screen.info.jp").Replace("{n}", mySeat.Jp.ToString())), "자원 줄 여행 점수");
                Assert.That(stage.Root.Q(BoardHud.Res).resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "자원 줄이 보인다");
                yield return null;   // 방금 다시 그린 좌석 칩의 배치가 서게 한 프레임
                var seatFace = stage.Root.Q("board-hud-seat-1-face");
                Assert.That(FaceBox.SeatColorOf(seatFace), Is.EqualTo(SeatColors.ForVillage("village.stark")), "얼굴 상자 테두리 = 좌석 색");
                Assert.That(FaceBox.Shown(seatFace), Is.EqualTo(flow.Protagonist(0)), "얼굴 상자 = 그 좌석 주인공의 얼굴(결정 54 ④)");
                Assert.That(seatFace.Q<Label>(className: "sn-face-seat").text, Is.EqualTo("1"), "좌석 번호 배지");
                var seatArt = seatFace.Q<Image>(className: "sn-face-art");
                Assert.That(seatArt.image, Is.Not.Null, "주인공 앞모습 그림(Gen Trainers/Hero)");
                Assert.That(seatArt.sourceRect.size, Is.EqualTo(new Vector2(30f, 30f)), "얼굴 조각 30×30");
                float faceSide = stage.Grade.TokenHeight == 720 ? 30f : 60f;
                Assert.That(seatArt.worldBound.size, Is.EqualTo(new Vector2(faceSide, faceSide)), "작은 상자는 조각의 정수배: 720 벌 ×1 · 1080 벌 ×2(결정 64)");
                Assert.That(stage.Root.Q("board-hud-mon-0"), Is.Not.Null, "파티의 스타팅 포켓몬");
                Assert.That(stage.Root.Q<Label>("board-hud-moves").text, Is.EqualTo("남은 이동력 3"));
                Assert.That(stage.Root.Q<Label>("board-hud-guide").text, Is.EqualTo("이동할 칸을 선택하십시오."));

                // 갈 수 있는 칸·단추 = 엔진의 합법 답 그대로
                var turnAct = client.Choice(ChoiceKind.MapAction);
                var turnLegal = client.Legal(turnAct);
                var moveCells = turnLegal.FindAll(a => a.Kind == AnswerKind.Move).ConvertAll(a => a.Target);
                Assert.That(moveCells.Count, Is.GreaterThan(0));
                Assert.That(hud.Reach, Is.EqualTo(moveCells), "이동 표시 = 합법 이동의 도착 칸");
                Assert.That(board.Marks.Reach, Is.EqualTo(moveCells));
                Assert.That(board.Marks.Visible, Is.True);
                Assert.That(hud.Acts, Does.Contain(BoardHud.EndTurn));
                foreach (string id in hud.Acts)
                {
                    var spec = System.Array.Find(BoardHud.MapActs, a => a.Id == id);
                    // [마을]은 답이 아니라 창을 여는 단추다 — 마을 행동을 하는 곳에 서 있을 때만 있다
                    if (spec.Window) Assert.That(flow.AtVillage(out _, out _), Is.True, id + ": 마을 행동을 하는 곳에서만");
                    else Assert.That(turnLegal.Exists(a => a.Kind == spec.Kind), id + ": 판의 단추는 합법 답에 있는 것만");
                    Assert.That(stage.Root.Q<Button>("board-hud-act-" + id), Is.Not.Null, id);
                }
                foreach (var spec in BoardHud.MapActs)
                    if (!spec.Window && !turnLegal.Exists(a => a.Kind == spec.Kind)) Assert.That(stage.Root.Q("board-hud-act-" + spec.Id), Is.Null, spec.Id + ": 합법이 아닌 행동은 단추가 없다");
                // [마을]이 켜져 있다(고향 마을 — 전체 회복 · 공중날기는 마을 창 S23에서). 회색 단추는 없다. 설명은 툴팁에
                Assert.That(hud.Acts, Does.Contain(BoardHud.Village), "고향 마을에 서 있다 — [마을]이 있다");
                Assert.That(turnLegal.Exists(a => a.Kind == AnswerKind.VillageHeal), Is.True, "전체 회복이 합법");
                Assert.That(stage.Root.Q("board-hud-act-" + BoardHud.Village).ClassListContains("sn-hud-off"), Is.False);
                Assert.That(stage.Root.Q<Button>("board-hud-act-" + BoardHud.Village).enabledSelf, Is.True);
                Assert.That(stage.Root.Q("board-hud-buttons").Q(className: "sn-hud-off"), Is.Null, "첫 턴의 판에 회색 단추는 없다");
                foreach (var spec in BoardHud.MapActs) Assert.That(spec.Ready, Is.True, spec.Id + ": 받을 화면이 없는 행동은 이제 없다");
                stage.Tips.Show(stage.Root.Q("board-hud-act-" + BoardHud.Village));
                Assert.That(stage.Tips.Element.Query<Label>().ToList().Exists(l => l.text.Replace("⁠", "") == stage.T("screen.board.action.village.tip")), "단추의 설명은 툴팁에");
                // 단추의 툴팁은 아래 판 밖(위)에 뜬다 — 판 안의 「행동 · 이번 턴 · 안내」 줄을 덮지 않는다(단추 바로 위가 아니다)
                yield return null;
                yield return null;
                {
                    var tipBox = stage.Tips.Element.worldBound;
                    var dockBox = stage.Root.Q("board-hud-dock").worldBound;
                    var healBox = stage.Root.Q("board-hud-act-" + BoardHud.Village).worldBound;
                    Assert.That(stage.Tips.Element.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible), "툴팁이 자리를 잡았다");
                    Assert.That(tipBox.yMax, Is.LessThanOrEqualTo(dockBox.yMin), "툴팁 아래 변이 아래 판 위 변보다 위");
                    Assert.That(tipBox.Overlaps(stage.Root.Q("board-hud-guide").worldBound), Is.False, "안내 줄을 덮지 않는다");
                    Assert.That(tipBox.Overlaps(stage.Root.Q("board-hud-used").worldBound), Is.False, "「이번 턴」 줄을 덮지 않는다");
                    Assert.That(tipBox.xMin, Is.EqualTo(healBox.xMin).Within(1f).Or.LessThan(healBox.xMin), "가로 자리는 단추에 맞춘다(화면 밖이면 안으로 민다)");
                }
                stage.Tips.Hide();
                Assert.That(flow.Session.CommandCount, Is.EqualTo(3));

                // 화면 좌표 ↔ 칸: 갈 수 있는 칸이 그려지는 점을 고르면 그 칸이다(앞의 높은 칸·HUD에 가린 칸은 뺀다).
                // HUD에 가린 칸 = 가운데가 아래 판·위 띠의 판 안에 있는 칸뿐이다 — 그 밖에서는 PC 층이 마우스를 받지 않는다(보이지 않는 넘김 띠·요약 판 포함)
                bool UnderHud(Vector2 at)
                {
                    var p = new Vector2(at.x, 1080f - at.y);
                    if (stage.Root.Q("board-hud-dock").worldBound.Contains(p) || stage.Root.Q("board-hud-status").worldBound.Contains(p)) return true;
                    foreach (var chip in stage.Root.Q("board-hud-seats").Children()) if (chip.worldBound.Contains(p)) return true;
                    return false;
                }
                int BoardOpen(IReadOnlyList<int> cells, string when)
                {
                    int open = 0;
                    foreach (int cell in cells)
                    {
                        if (!BoardPick.Screen(board, cam, cell, 1920, 1080, out var at) || at.x < 0 || at.x >= 1920 || at.y < 0 || at.y >= 1080 || UnderHud(at)) continue;
                        Assert.That(flow.OverUi(at.x, at.y), Is.False, when + ": 칸 " + cell + " (" + at.x + ", " + at.y + ")은 HUD 판 밖인데 PC 층이 마우스를 받는다");
                        open++;
                    }
                    // 넘김 띠·요약 판은 보이든 말든 마우스를 받지 않는다(글자 요소까지)
                    foreach (var part in stage.Root.Q("turn-band").Query<VisualElement>().ToList())
                    {
                        var wb = part.worldBound;
                        if (float.IsNaN(wb.width) || wb.width < 1f || wb.height < 1f || wb.center.x < 0f || wb.center.x >= 1920f || wb.center.y < 0f || wb.center.y >= 1080f) continue;
                        var at = new Vector2(wb.center.x, 1080f - wb.center.y);
                        if (!UnderHud(at)) Assert.That(flow.OverUi(at.x, at.y), Is.False, when + ": 넘김 띠의 요소(" + part.GetType().Name + " " + part.name + " " + wb + ")가 마우스를 받는다");
                    }
                    return open;
                }
                var steps = MoveRules.Reachable(flow.Ctx.Board, home, 3, view.Options.ElevationMode);
                int target = -1, picked = 0;
                Vector2 targetPx = default;
                bool targetClear = false;
                foreach (int cell in moveCells)
                {
                    Assert.That(BoardPick.Screen(board, cam, cell, 1920, 1080, out var px), Is.True);
                    if (px.x < 0 || px.x >= 1920 || px.y < 0 || px.y >= 1080 || UnderHud(px)) continue;
                    if (BoardPick.Cell(board, cam, px.x, px.y, 1920, 1080) != cell) continue;
                    picked++;
                    if (steps[cell] != 2 || targetClear) continue;
                    // 길 위에 뒷면 숨겨진 장소가 없는 칸을 먼저(결정 71 ③ — 있으면 걷다가 멈춰 묻는다. 그 흐름은 PlaceFlowTests G12_결정71_…)
                    var way = MoveRules.Path(flow.Ctx.Board, home, cell, 3, view.Options.ElevationMode);
                    bool clear = MapActions.StopsOn(view, flow.Ctx, 0, way.GetRange(1, way.Count - 1)).Count == 0;
                    if (target < 0 || clear) { target = cell; targetPx = px; targetClear = clear; }
                }
                Assert.That(targetClear, Is.True, "길 위에 숨겨진 장소가 없는 두 걸음 칸");
                Assert.That(picked, Is.GreaterThanOrEqualTo(moveCells.Count / 2), "갈 수 있는 칸의 절반 넘게는 가운데가 보인다");
                Assert.That(target, Is.GreaterThanOrEqualTo(0), "두 걸음 떨어진 칸");
                Assert.That(BoardOpen(moveCells, "첫 턴"), Is.GreaterThanOrEqualTo(picked), "HUD 판 밖의 갈 수 있는 칸은 모두 보드로 통한다");

                // HUD 위에서는 보드로 새지 않는다: 칸을 고르지 않고 휠도 막는다
                var dock = stage.Root.Q("board-hud-dock").worldBound;
                float dockX = dock.center.x, dockY = 1080f - dock.center.y;
                Assert.That(flow.OverUi(dockX, dockY), Is.True);
                Assert.That(flow.Pointer(dockX, dockY, true), Is.EqualTo(-1), "아래 판 위를 눌러도 그 밑의 칸이 눌리지 않는다");
                Assert.That(rig.WheelEnabled, Is.False, "HUD 위에서는 휠 줌을 듣지 않는다");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(3));
                Assert.That(flow.OverUi(targetPx.x, targetPx.y), Is.False, "보드 위(HUD 바탕은 마우스를 받지 않는다)");

                // 보드 위: 마우스를 올리면 그 칸까지 경로, 한 번 누르면 그 칸을 고르고(걷기 전 확인 — 결정 70 ⑱) 한 번 더 누르면 걷는다
                Assert.That(flow.Pointer(targetPx.x, targetPx.y, false), Is.EqualTo(target));
                Assert.That(rig.WheelEnabled, Is.True);
                Assert.That(hud.HoverCell, Is.EqualTo(target));
                Assert.That(board.Marks.Target, Is.EqualTo(target));
                Assert.That(board.Marks.Path.Count, Is.EqualTo(1), "두 걸음 = 사이 칸 하나");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(3), "올리기만 해서는 답을 내지 않는다");
                var pieceRoot = board.Pieces.Pieces[0].Root;
                var startPos = pieceRoot.transform.localPosition;
                var camStart = rig.Target;
                Assert.That(Vector3.Distance(camStart, board.CellTop(home)), Is.LessThan(1e-4f), "걷기 전: 카메라는 내 말 칸을 본다");
                Assert.That(hud.PickedCell, Is.EqualTo(-1));
                Assert.That(stage.Root.Q(BoardHud.MoveGo), Is.Null, "고르기 전에는 [이동]이 없다");
                Assert.That(flow.Pointer(targetPx.x, targetPx.y, true), Is.EqualTo(target));
                Assert.That(flow.Session.CommandCount, Is.EqualTo(3), "한 번 눌러서는 걷지 않는다");
                Assert.That(hud.PickedCell, Is.EqualTo(target), "누른 칸이 고른 칸");
                Assert.That(hud.PickedPath, Is.EqualTo(MoveRules.Path(flow.Ctx.Board, home, target, 3, flow.Client.View.Options.ElevationMode)), "보이는 길 = 엔진이 걸을 길");
                Assert.That(stage.Root.Q<Label>("board-hud-guide").text, Is.EqualTo("이동력 2 사용 · 1 남음"));
                Assert.That(stage.Root.Q<Button>(BoardHud.MoveGo).text, Is.EqualTo("이동"));
                Assert.That(stage.Root.Q<Label>("board-hud-moves").text, Is.EqualTo("남은 이동력 3"), "아직 쓰지 않았다");
                // 다른 칸에 마우스를 올려도 칠한 길은 고른 칸 그대로다
                flow.Pointer(dockX, dockY, false);
                Assert.That(hud.HoverCell, Is.EqualTo(-1));
                Assert.That((board.Marks.Target, board.Marks.Path.Count), Is.EqualTo((target, 1)), "고른 칸의 길이 남아 있다");
                // 고른 칸은 칠 대신 테두리로 그리고(그 칸의 토큰이 가려지지 않게), 마우스가 보드를 떠나도 칸 정보 줄은 고른 칸을 적는다 — 확인할 도착 칸이 화면에 남는다(검토 반영)
                Assert.That(board.Marks.Held, Is.True, "고른 칸 = 테두리");
                Assert.That(hud.HoverAny, Is.EqualTo(-1));
                Assert.That(hud.CellText(target), Is.Not.EqualTo(hud.CellText(home)));
                Assert.That(hud.CellInfoText, Is.EqualTo(hud.CellText(target)), "칸 정보 줄 = 고른 칸");
                flow.Pointer(targetPx.x, targetPx.y, false);
                Assert.That(board.Marks.Held, Is.True, "고른 칸에 마우스를 올려도 테두리 그대로");
                flow.Pointer(dockX, dockY, false);
                // 풀기: Esc · 갈 수 없는 칸(내가 선 칸) 누르기 — 답은 나가지 않는다
                Assert.That(flow.CancelPick(), Is.True, "Esc");
                Assert.That(hud.PickedCell, Is.EqualTo(-1));
                Assert.That(hud.CellInfoText, Is.EqualTo(hud.CellText(home)), "풀면 칸 정보 줄은 내가 선 칸으로");
                Assert.That(board.Marks.Held, Is.False);
                Assert.That(stage.Root.Q(BoardHud.MoveGo), Is.Null);
                Assert.That(stage.Root.Q<Label>("board-hud-guide").text, Is.EqualTo("이동할 칸을 선택하십시오."));
                Assert.That(flow.CancelPick(), Is.False, "고른 칸이 없으면 아무 일도 없다");
                Assert.That(hud.ClickCell(target), Is.False, "다시 고른다");
                Assert.That(hud.PickedCell, Is.EqualTo(target));
                Assert.That(hud.ClickCell(home), Is.False, "갈 수 없는 칸을 누르면 풀린다");
                Assert.That(hud.PickedCell, Is.EqualTo(-1));
                // 결정 71 ②(옛 결정 70 ⑱를 고침): 고른 뒤 다른 갈 수 있는 칸을 누르면 그 칸을 거쳐 가는 길(경유 칸).
                // 남은 이동력을 넘으면 그 칸을 새로 고른다 [가정]. Esc는 경유 칸을 하나 빼고, 갈 수 없는 칸을 누르면 처음부터 다시
                int other = moveCells.Find(c => c != target);
                Assert.That(hud.ClickCell(other), Is.False);
                Assert.That(hud.PickedCell, Is.EqualTo(other));
                var viaRoute = MoveRules.Route(flow.Ctx.Board, home, new[] { target }, other, 3, flow.Client.View.Options.ElevationMode);
                Assert.That(hud.ClickCell(target), Is.False);
                if (viaRoute != null)
                {
                    Assert.That((hud.PickedCell, hud.Vias.Count), Is.EqualTo((other, 1)), "누른 칸 = 경유 칸, 고른 칸은 그대로");
                    Assert.That(hud.PickedPath, Is.EqualTo(viaRoute), "보이는 길 = 경유 칸을 거치는 길");
                    Assert.That(board.Marks.Vias, Has.Member(target), "경유 칸은 흰 테두리");
                    Assert.That(flow.CancelPick(), Is.True, "Esc = 경유 칸 하나 빼기");
                    Assert.That((hud.PickedCell, hud.Vias.Count), Is.EqualTo((other, 0)));
                }
                else Assert.That(hud.PickedCell, Is.EqualTo(target), "이동력을 넘는 칸은 새로 고른다");
                Assert.That(hud.ClickCell(home), Is.False, "갈 수 없는 칸 = 처음부터 다시");
                Assert.That((hud.PickedCell, hud.Vias.Count), Is.EqualTo((-1, 0)));
                Assert.That(hud.ClickCell(target), Is.False);
                Assert.That(hud.PickedCell, Is.EqualTo(target), "고른 칸");
                // 결정 73 ③(C1 ③ 나 「우클릭도 된다고 안내하나 필요해」): 단추 줄의 [취소]는 고른 칸·거쳐 갈 칸을 한 번에 모두 푼다(오른쪽 클릭·Esc는 하나씩 — C1 ② 그대로),
                // 그 곁에 오른쪽 클릭 안내 한 줄
                int middle = hud.PickedPath[1];
                Assert.That(hud.ClickCell(middle), Is.False);
                Assert.That((hud.PickedCell, hud.Vias.Count), Is.EqualTo((target, 1)), "사이 칸 = 거쳐 갈 칸");
                var cancel = stage.Root.Q<Button>(BoardHud.MoveCancel);
                Assert.That(cancel, Is.Not.Null, "고른 동안 [취소]가 선다");
                Assert.That(cancel.text, Is.EqualTo("취소"));
                Assert.That(cancel.parent, Is.SameAs(stage.Root.Q<Button>(BoardHud.MoveGo).parent), "[이동]과 같은 단추 줄");
                Assert.That(stage.Root.Q<Label>(BoardHud.MoveHint).text, Is.EqualTo("오른쪽 클릭: 하나씩 취소"));
                // 키보드 「제출」(Enter · Space)은 화면 단추를 누르지 않는다(UiStage.NoSubmit — M6 화면 교차 검토 확인 A: 막지 않으면 아래처럼 눌린다)
                void Submit()
                {
                    using (var submit = NavigationSubmitEvent.GetPooled())
                    {
                        submit.target = cancel;
                        cancel.SendEvent(submit);
                    }
                }
                Submit();
                Assert.That((hud.PickedCell, hud.Vias.Count), Is.EqualTo((target, 1)), "키보드 제출로는 [취소]가 눌리지 않는다");
                // 창 없는 PlayMode에서는 포인터를 패널에 넣지 못해 단추를 제출 이벤트로 누른다 — 그동안만 막기를 뗀다
                stage.Root.UnregisterCallback<NavigationSubmitEvent>(UiStage.NoSubmit, TrickleDown.TrickleDown);
                Submit();
                stage.Root.RegisterCallback<NavigationSubmitEvent>(UiStage.NoSubmit, TrickleDown.TrickleDown);
                Assert.That((hud.PickedCell, hud.Vias.Count), Is.EqualTo((-1, 0)), "[취소] = 거쳐 갈 칸까지 모두 푼다");
                Assert.That(stage.Root.Q(BoardHud.MoveCancel), Is.Null, "풀면 [취소]도 없다");
                Assert.That(stage.Root.Q(BoardHud.MoveHint), Is.Null);
                Assert.That(hud.ClickCell(target), Is.False);
                Assert.That(hud.PickedCell, Is.EqualTo(target), "고른 칸");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(3), "고르고 푸는 동안 답은 나가지 않았다");
                // [이동] 단추와 같은 칸 다시 누르기는 같은 길이다 — 여기서는 같은 칸을 한 번 더 누른다
                Assert.That(flow.Pointer(targetPx.x, targetPx.y, true), Is.EqualTo(target));
                Assert.That(flow.Session.CommandCount, Is.EqualTo(4), "이동 답 하나");
                Assert.That(hud.PickedCell, Is.EqualTo(-1));
                view = flow.Client.View;
                Assert.That(view.Seats[0].Cell, Is.EqualTo(target), "보기는 바로 도착 칸");
                Assert.That(view.Seats[0].MovesLeft, Is.EqualTo(1));
                // 걷는 동안(04 §8-8 11행): 입력 닫힘, 이동 표시 걷힘, 말은 다시 세우지 않고 옮긴다
                Assert.That(flow.InputOpen, Is.False);
                Assert.That(hud.Reach, Is.Empty);
                Assert.That(board.Marks.Visible, Is.False);
                Assert.That(pieceRoot.transform.localPosition, Is.EqualTo(startPos), "걸음이 시작되기 전에는 떠난 칸에 그대로");
                // 프레임마다: 말은 지금 걸음의 두 칸 사이(걸음 안 시각만큼 간 자리)에 있고, 「남은 이동력」은 다 디딘 걸음만큼만 줄어 있다.
                // 어느 걸음의 어디인지는 걷기 시간표(FxWalk — dotnet 시험이 본다)로 읽고, 여기서는 그것이 말과 칩에 닿는지를 본다
                int strides = 0, firstStride = 0, followed = 0, stepFrames = 0;
                float walkT0 = Time.realtimeSinceStartup;
                while (!flow.InputOpen && Time.realtimeSinceStartup - walkT0 < 10f)
                {
                    var seq = flow.Fx.Sequencer;
                    var walkStep = seq.Current != null ? seq.Current.Step(110) : null;
                    if (walkStep != null && walkStep.Events.Count > 0)
                    {
                        var moved = (Moved)walkStep.Events[0];
                        Assert.That(moved.Path.Length, Is.EqualTo(3), "두 걸음 길");
                        Assert.That(moved.AllowanceBefore, Is.EqualTo(3));
                        var pose = FxWalk.At(moved, walkStep.Local(seq.LocalMs));
                        string at = "걷기 " + seq.LocalMs + "ms(걸음 " + pose.From + "→" + pose.To + ", " + pose.T + ")";
                        Assert.That(stage.Root.Q<Label>("board-hud-moves").text, Is.EqualTo("남은 이동력 " + (3 - pose.Landed)), at + ": 칩은 착지마다 하나씩 꺼진다");
                        var pos = pieceRoot.transform.localPosition;
                        Vector3 a = board.CellTop(moved.Path[pose.From]), b = board.CellTop(moved.Path[pose.To]);
                        // 주인공 도트 말: 가는 쪽을 보고(카메라 방향 0 — 오른쪽 = +x, 안쪽 = +z) 걷기 그림이 돈다
                        var walker = board.Pieces.Pieces[0];
                        Assert.That(FieldWalk.Leg(pose, out int legA, out int legB), Is.True, at);
                        var leg = board.CellTop(moved.Path[legB]) - board.CellTop(moved.Path[legA]);
                        Assert.That(walker.Facing, Is.EqualTo(FieldWalk.Face(leg.x, leg.z)), at + ": 가는 쪽을 본다");
                        Assert.That(walker.Cycle, Is.EqualTo(FieldWalk.Cycle(pose)), at + ": 걷기 그림의 자리");
                        Assert.That(walker.Frame, Is.EqualTo(walker.Cycles[(int)walker.Facing][walker.Cycle]), at);
                        if (walker.Cycle != 0) stepFrames++;
                        // 카메라 따라가기(결정 57 ①): 보는 점이 걷는 말의 땅 자리(튀는 높이 뺀 자리)를 따라간다 — 처음 CameraCatchMs 동안은 다가가는 중, 그 뒤로는 붙는다. 방향·줌은 그대로
                        var ground = Vector3.Lerp(a, b, pose.T);
                        int walkMs = walkStep.Local(seq.LocalMs);
                        if (walkMs >= GameFlow.CameraCatchMs)
                        {
                            Assert.That(Vector3.Distance(rig.Target, ground), Is.LessThan(1e-3f), at + ": 카메라의 보는 점이 걷는 말에 붙어 있다");
                            if (walkMs < walkStep.Ms) followed++;
                        }
                        else Assert.That(Vector3.Distance(rig.Target, ground), Is.LessThanOrEqualTo(Vector3.Distance(camStart, ground) + 1e-3f), at + ": 다가가는 중 — 떠난 자리보다 말에 가깝다");
                        Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Region), at + ": 줌 그대로");
                        Assert.That(rig.Yaw, Is.EqualTo(0), at + ": 방향 그대로");
                        if (pose.From != pose.To)
                        {
                            float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                            float fromA = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(a.x, a.z)), toB = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(b.x, b.z));
                            Assert.That(fromA + toB, Is.EqualTo(len).Within(1e-3f), at + ": 말이 지금 걸음의 두 칸을 잇는 선 위에 있다");
                            Assert.That(fromA / len, Is.EqualTo(pose.T).Within(1e-3f), at + ": 걸음 안 시각만큼 갔다");
                            Assert.That(pos.y, Is.GreaterThanOrEqualTo(Mathf.Min(a.y, b.y) - 1e-3f), at);
                            if (pose.T > 0.05f && pose.T < 0.95f)
                            {
                                strides++;
                                if (pose.From == 0) firstStride++;
                                Assert.That(Vector3.Distance(pos, board.CellTop(target)), Is.GreaterThan(0.05f), at + ": 걸음 한가운데의 말은 도착 칸에 있지 않다");
                                Assert.That(pos.y, Is.EqualTo(Mathf.Lerp(a.y, b.y, pose.T)).Within(1e-3f), at + ": 주인공 도트 말은 튀지 않고 걷는다(통통 튀는 것은 직업 그림 말)");
                            }
                        }
                        else Assert.That(Vector3.Distance(pos, b), Is.LessThan(1e-3f), at + ": 다 걸었으면 도착 칸 위");
                    }
                    yield return null;
                }
                Assert.That(strides, Is.GreaterThan(0), "걸음 한가운데의 프레임을 봤다");
                Assert.That(stepFrames, Is.GreaterThan(0), "발을 내민 걷기 그림의 프레임을 봤다");
                Assert.That(board.Pieces.Pieces[0].Facing, Is.EqualTo(Facing.South), "다 걸으면 앞모습으로 선다");
                Assert.That(board.Pieces.Pieces[0].Cycle, Is.EqualTo(0));
                Assert.That(firstStride, Is.GreaterThan(0), "첫 걸음(이동력 3 그대로, 첫 칸과 사이 칸의 사이)의 프레임을 봤다");
                Assert.That(followed, Is.GreaterThan(0), "카메라가 걷는 말에 붙어 가는 프레임을 봤다");
                // 걸은 뒤(결정 57 ①): 카메라는 도착 칸을 본다 — 말이 화면 한가운데(HUD 판 밖)에 있고, 그 화면 점을 고르면 그 칸이다
                Assert.That(Vector3.Distance(rig.Target, board.CellTop(target)), Is.LessThan(1e-4f), "걸은 뒤 카메라는 도착 칸을 본다");
                Assert.That(Vector3.Distance(rig.Target, camStart), Is.GreaterThan(1f), "카메라가 옮겨 갔다");
                Assert.That(BoardPick.Screen(board, cam, target, 1920, 1080, out var arrived), Is.True);
                Assert.That(arrived.x, Is.EqualTo(960f).Within(2f), "도착 칸이 화면 가운데");
                Assert.That(arrived.y, Is.EqualTo(540f).Within(2f));
                Assert.That(UnderHud(arrived), Is.False, "아래 HUD 판에 가리지 않는다");
                Assert.That(BoardPick.Cell(board, cam, arrived.x, arrived.y, 1920, 1080), Is.EqualTo(target), "따라간 뒤에도 화면 점 → 칸이 맞는다");
                {
                    var piecePx = cam.WorldToViewportPoint(pieceRoot.transform.position);
                    Assert.That(piecePx.x, Is.InRange(0.3f, 0.7f), "말(칸 안 자리)이 화면 가운데 쪽");
                    Assert.That(piecePx.y, Is.InRange(0.3f, 0.7f));
                }
                Assert.That(board.Pieces.Pieces[0].Root, Is.SameAs(pieceRoot), "말을 다시 만들지 않았다");
                Assert.That(flow.InputOpen, Is.True, "걸음이 끝나면 입력이 열린다");
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(1));
                Assert.That(board.Pieces.Pieces[0].Root, Is.SameAs(pieceRoot), "걷는 동안 말을 다시 세우지 않았다");
                Assert.That(board.Pieces.Pieces[0].Spot.Cell, Is.EqualTo(target), "말이 도착 칸에 섰다");
                var spot = board.Pieces.Pieces[0].Spot;
                Assert.That(Vector3.Distance(pieceRoot.transform.localPosition, new Vector3((float)spot.X, (float)spot.Y, (float)spot.Z)), Is.LessThan(1e-4f));

                // 숨겨진 장소에 멈췄으면 묻는다: 단추 = 그 선택의 합법 답 둘. 지나간다
                if (flow.Client.FirstChoice().Kind == ChoiceKind.HiddenSpot)
                {
                    Assert.That(hud.Acts, Is.EqualTo(new[] { BoardHud.Reveal, BoardHud.Pass }));
                    Assert.That(stage.Root.Q<Label>("board-hud-guide").text, Is.EqualTo("숨겨진 장소를 공개하겠습니까?"));
                    Assert.That(hud.Press(BoardHud.Pass), Is.True);
                    yield return Until(() => flow.InputOpen, 10f);
                }
                Assert.That(flow.Client.Choice(ChoiceKind.MapAction), Is.Not.Null, "턴이 이어진다");
                Assert.That(stage.Root.Q<Label>("board-hud-moves").text, Is.EqualTo("남은 이동력 1"));
                // 카메라가 따라간 뒤의 화면에서: 갈 수 있는 칸이 그려지는 점에 마우스를 올리면 그 칸이 잡힌다(칸 고르기 좌표와 이동 표시가 어긋나지 않는다)
                Assert.That(hud.Reach.Count, Is.GreaterThan(0), "남은 이동력 1로 갈 수 있는 칸");
                Assert.That(board.Marks.Reach, Is.EqualTo(hud.Reach));
                int afterPicked = 0;
                foreach (int cell in hud.Reach)
                {
                    if (!BoardPick.Screen(board, cam, cell, 1920, 1080, out var px) || px.x < 0 || px.x >= 1920 || px.y < 0 || px.y >= 1080 || UnderHud(px)) continue;
                    if (BoardPick.Cell(board, cam, px.x, px.y, 1920, 1080) != cell) continue;   // 앞의 높은 칸에 가린 칸
                    Assert.That(flow.Pointer(px.x, px.y, false), Is.EqualTo(cell), "걸은 뒤 칸 " + cell);
                    Assert.That(hud.HoverCell, Is.EqualTo(cell));
                    Assert.That(board.Marks.Target, Is.EqualTo(cell));
                    afterPicked++;
                }
                Assert.That(afterPicked, Is.GreaterThan(0), "걸은 뒤에도 갈 수 있는 칸을 화면에서 고를 수 있다(말이 화면 가운데라 이웃 칸이 보인다)");
                flow.Pointer(dockX, dockY, false);
                Assert.That(hud.HoverCell, Is.EqualTo(-1));

                // ── S22 게임 메뉴: HUD 위에 열리고, 열려 있는 동안 보드 입력·연출 대기열이 멈춘다 ──
                Assert.That(stage.Root.Q<Button>("board-hud-menu"), Is.Not.Null);
                flow.OpenMenu();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<BoardMenuScreen>());
                Assert.That(stage.Hud, Is.SameAs(hud), "HUD는 그대로 떠 있다");
                Assert.That(stage.Root.IndexOf(stage.Root.Q("board-menu")), Is.GreaterThan(stage.Root.IndexOf(stage.Root.Q("board-hud"))), "창이 HUD 위");
                Assert.That(rig.InputEnabled, Is.False, "메뉴: 보드 입력 끔");
                Assert.That(flow.InputOpen, Is.False);
                Assert.That(flow.Fx.Sequencer.Paused, Is.True);
                Assert.That(flow.Pointer(targetPx.x, targetPx.y, true), Is.EqualTo(-1), "메뉴 뒤의 칸은 눌리지 않는다");
                var menu = (BoardMenuScreen)stage.Current;
                Assert.That(stage.Root.Q<Label>("board-menu-save").text, Is.EqualTo("저장하지 않는 게임"), "저장 상태 줄(에디터의 흐름은 저장하지 않는다)");
                menu.AskQuit();
                Assert.That(menu.Asking, Is.True);
                var ask = stage.Root.Q("board-menu-ask").Query<Label>().ToList();
                Assert.That(ask[0].text, Is.EqualTo("타이틀로 나가겠습니까?"));
                Assert.That(ask[1].text.Replace("⁠", ""), Is.EqualTo("저장되지 않아 진행한 내용이 사라집니다."), "저장하지 않는 판(에디터의 흐름)이라는 사실을 적는다");
                menu.Cancel();
                Assert.That(stage.Root.Q("board-menu-ask"), Is.Null);
                Assert.That(flow.Session, Is.Not.Null);
                menu.Resume();
                yield return null;
                Assert.That(stage.Current, Is.Null);
                Assert.That(rig.InputEnabled, Is.True);
                Assert.That(flow.Fx.Sequencer.Paused, Is.False);
                Assert.That(flow.InputOpen, Is.True);

                // ── 턴 종료 → 솔로는 다음 라운드의 내 턴: 넘김 띠 500 → 내 턴 컷신 500(결정 57 ③ — 주운 것이 없는 턴이라 요약은 건너뛴다) ──
                int commands = flow.Session.CommandCount;
                var camBeforeEnd = rig.Target;
                stage.Tips.Show(stage.Root.Q("board-hud-act-" + BoardHud.EndTurn));
                Assert.That(stage.Tips.Target, Is.Not.Null, "입력이 열려 있을 때는 [턴 종료]의 툴팁이 뜬다");
                Assert.That(hud.Press(BoardHud.EndTurn), Is.True);
                // 누른 뒤 마우스를 그대로 두어도(꺼진 단추 위) 넘김 동안에는 단추 툴팁이 다시 뜨지 않는다
                Assert.That(stage.Tips.Target, Is.Null, "누르면 툴팁이 걷힌다");
                Assert.That(stage.Root.Q<Button>("board-hud-act-" + BoardHud.EndTurn), Is.Not.Null, "단추 자리는 그대로(꺼진 채)");
                foreach (string id in hud.Acts)
                {
                    stage.Tips.Show(stage.Root.Q("board-hud-act-" + id));
                    Assert.That(stage.Tips.Target, Is.Null, id + ": 입력이 닫힌 동안은 단추 툴팁이 걸려 있지 않다");
                }
                {
                    FxEntry turnEntry = null;
                    foreach (var queued in flow.Fx.Sequencer.Queued) if (queued.Step(FxRows.Handoff) != null) turnEntry = queued;
                    Assert.That(turnEntry, Is.Not.Null, "넘김 항목이 대기열에 있다");
                    Assert.That(turnEntry.Step(FxRows.TurnSummary), Is.Null, "「변화 없음」이면 요약 단계가 없다");
                    Assert.That(turnEntry.Step(FxRows.Handoff).Ms, Is.EqualTo(500));
                    Assert.That(turnEntry.Step(FxRows.MyTurn).Ms, Is.EqualTo(500));
                    Assert.That(turnEntry.LengthMs, Is.EqualTo(1000), "솔로의 턴 넘김은 1초");
                    var turnInfo = (TurnBandInfo)turnEntry.Step(FxRows.Handoff).Tag;
                    Assert.That((turnInfo.From, turnInfo.To), Is.EqualTo((0, 0)));
                    Assert.That(turnInfo.FromFace, Is.EqualTo(flow.Protagonist(0)), "넘김 띠에 싣는 얼굴 = 턴이 끝난 좌석의 주인공(결정 61 ③)");
                    Assert.That(turnInfo.ToFace, Is.EqualTo(flow.Protagonist(0)), "턴이 오는 좌석의 주인공");
                }
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1));
                view = flow.Client.View;
                Assert.That(view.Round, Is.EqualTo(2));
                Assert.That(view.ActiveSeat, Is.EqualTo(0));
                Assert.That(flow.InputOpen, Is.False, "넘김 연출 동안 입력 닫힘");
                yield return Until(() => hud.Band.ActiveRow != 0, 5f);
                Assert.That(hud.Band.ActiveRow, Is.EqualTo(FxRows.Handoff), "요약 없이 넘김 띠부터");
                Assert.That(stage.Root.Q("turn-band-summary").style.opacity.value, Is.EqualTo(0f), "요약 판은 뜨지 않는다");
                Assert.That(stage.Root.Q("turn-band-strip").Q<Label>(className: "sn-band-text").text, Is.EqualTo("2라운드"), "한 좌석뿐인 판은 띠에 라운드를 적는다");
                Assert.That(FaceBox.Shown(stage.Root.Q("turn-band-face-from")), Is.EqualTo(flow.Protagonist(0)), "넘김 띠의 얼굴 상자 = 그 좌석 주인공");
                Assert.That(stage.Root.Q("turn-band-face-to").style.display.value, Is.EqualTo(DisplayStyle.None), "한 좌석뿐인 판은 얼굴 하나");
                Assert.That(stage.Root.Q<Label>("board-hud-round").text, Is.EqualTo("1라운드"), "띠가 지나가는 동안에도 아직 앞 라운드");
                Assert.That(stage.Root.Q<Label>("board-hud-time").text, Is.EqualTo("낮"), "시간대 배지도 라운드 배지와 함께 바뀐다(결정 70 ⑥)");
                yield return Until(() => hud.Band.ActiveRow == FxRows.MyTurn, 5f);
                Assert.That(hud.Band.ActiveRow, Is.EqualTo(FxRows.MyTurn), "내 턴 컷신");
                Assert.That(stage.Root.Q<Label>("board-hud-round").text, Is.EqualTo("2라운드"), "띠가 끝나면 라운드 배지가 바뀐다");
                Assert.That(stage.Root.Q<Label>("board-hud-time").text, Is.EqualTo("저녁"), "2라운드 = 저녁");
                Assert.That(((BoardHud)stage.Hud).ShownTime, Is.EqualTo(DayTime.Evening));
                yield return Until(() => flow.InputOpen, 10f);
                Assert.That(flow.InputOpen, Is.True, "다음 라운드의 내 턴에 입력이 열린다");
                Assert.That(rig.Target, Is.EqualTo(camBeforeEnd), "솔로: 내 턴 컷신 동안 카메라는 이미 내 말을 보고 있어 움직이지 않는다");
                stage.Tips.Show(stage.Root.Q("board-hud-act-" + BoardHud.EndTurn));
                Assert.That(stage.Tips.Target, Is.Not.Null, "입력이 다시 열리면 단추 툴팁도 다시 걸린다");
                stage.Tips.Hide();
                Assert.That(BoardOpen(hud.Reach, "턴 종료 뒤"), Is.GreaterThan(0), "요약 줄이 채워진 뒤에도 HUD 판 밖의 칸은 보드로 통한다");
                Assert.That(stage.Root.Q<Label>("board-hud-round").text, Is.EqualTo("2라운드"));
                Assert.That(stage.Root.Q<Label>("board-hud-moves").text, Is.EqualTo("남은 이동력 3"));
                Assert.That(hud.Reach.Count, Is.GreaterThan(0), "다시 갈 수 있는 칸이 칠해진다");
                Assert.That(board.Marks.Visible, Is.True);
                home = target;   // 아래 카메라 확인은 지금 선 칸에서 본다

                // 보드에서 바꾼 시점(Q·E·휠)은 그대로 남는다: 카메라는 셋업이 끝난 그때 한 번만 놓는다 — 창 크기가 바뀌어도, 갱신이 와도 다시 놓지 않는다.
                // 보는 점만 걷는 말을 따라간다(결정 57 ①) — 방향·줌은 그때도 그대로
                var look = board.CellTop(home) + new Vector3(3f, 0f, -2f);
                rig.Set(BoardCameraRig.Zoom.Close, 2, look);
                flow.Layout(1600, 900);
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Close), "창 크기가 바뀌어도 줌 그대로");
                Assert.That(rig.Yaw, Is.EqualTo(2), "창 크기가 바뀌어도 방향 그대로");
                Assert.That(rig.Target, Is.EqualTo(look));
                yield return null;   // 화면 시작점이 원래 크기(1920×1080)로 다시 부른다
                yield return null;
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Close));
                Assert.That(rig.Yaw, Is.EqualTo(2));
                Assert.That(rig.Target, Is.EqualTo(look));

                // 다른 곳을 보다가 턴 종료(결정 57 ① [가정]): 넘김 띠 동안은 보던 곳 그대로, 내 턴 컷신 동안 내 말 칸으로 돌아온다(CameraCatchMs 뒤로는 붙어 있다). 방향·줌은 그대로
                {
                    var mine = board.CellTop(flow.Client.View.Seats[0].Cell);
                    Assert.That(Vector3.Distance(look, mine), Is.GreaterThan(1f));
                    Assert.That(hud.Press(BoardHud.EndTurn), Is.True);
                    Assert.That(rig.Target, Is.EqualTo(look), "턴 종료를 누른 것만으로는 카메라가 움직이지 않는다");
                    int bandFrames = 0, cutFrames = 0;
                    float cutT0 = Time.realtimeSinceStartup;
                    while (!flow.InputOpen && Time.realtimeSinceStartup - cutT0 < 10f)
                    {
                        var seq = flow.Fx.Sequencer;
                        var cut = seq.Current != null ? seq.Current.Step(FxRows.MyTurn) : null;
                        if (cut != null)
                        {
                            string at = "넘김 " + seq.LocalMs + "ms";
                            if (seq.LocalMs < cut.StartMs)
                            {
                                Assert.That(rig.Target, Is.EqualTo(look), at + ": 넘김 띠 동안은 보던 곳 그대로");
                                bandFrames++;
                            }
                            else if (seq.LocalMs - cut.StartMs >= Mathf.Min(GameFlow.CameraCatchMs, cut.Ms))
                            {
                                Assert.That(Vector3.Distance(rig.Target, mine), Is.LessThan(1e-3f), at + ": 내 턴 컷신 동안 카메라가 내 말 칸으로 돌아와 있다");
                                cutFrames++;
                            }
                        }
                        yield return null;
                    }
                    Assert.That(bandFrames, Is.GreaterThan(0), "넘김 띠의 프레임을 봤다");
                    Assert.That(cutFrames, Is.GreaterThan(0), "컷신 안(항목이 끝나기 전)에 카메라가 돌아와 있는 프레임을 봤다");
                    Assert.That(flow.InputOpen, Is.True);
                    Assert.That(flow.Client.View.Round, Is.EqualTo(3));
                    Assert.That(Vector3.Distance(rig.Target, mine), Is.LessThan(1e-4f), "입력이 열린 뒤 카메라는 내 말 칸을 본다");
                    Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Close), "돌아와도 줌 그대로");
                    Assert.That(rig.Yaw, Is.EqualTo(2), "돌아와도 방향 그대로");
                }

                // 한 번에 길게 보낸 시간(캡처·끊긴 프레임)이 항목을 통째로 건너뛰어도 카메라는 끝 자리에 놓인다: 걷기 → 도착 칸, 턴 넘김 → 내 말 칸
                {
                    look = board.CellTop(flow.Client.View.Seats[0].Cell) + new Vector3(3f, 0f, -2f);
                    rig.Set(BoardCameraRig.Zoom.Close, 2, look);
                    var skipAct = flow.Client.Choice(ChoiceKind.MapAction);
                    var skipMove = flow.Client.Legal(skipAct).Find(a => a.Kind == AnswerKind.Move);
                    Assert.That(skipMove, Is.Not.Null, "이동할 칸이 있다");
                    Assert.That(flow.Answer(skipAct, skipMove).Accepted, Is.True);
                    Assert.That(flow.Client.View.Seats[0].Cell, Is.EqualTo(skipMove.Target));
                    Assert.That(rig.Target, Is.EqualTo(look), "연출 시간이 흐르기 전에는 그대로");
                    stage.Tweens.Advance(5000);
                    Assert.That(flow.Fx.Sequencer.IsEmpty, Is.True, "걷기 항목을 한 번에 건너뛰었다");
                    Assert.That(Vector3.Distance(rig.Target, board.CellTop(skipMove.Target)), Is.LessThan(1e-4f), "건너뛴 걷기: 카메라는 도착 칸을 본다");
                    Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Close));
                    Assert.That(rig.Yaw, Is.EqualTo(2));
                    yield return Until(() => flow.InputOpen, 10f);
                    if (flow.Client.FirstChoice().Kind == ChoiceKind.HiddenSpot)
                    {
                        Assert.That(hud.Press(BoardHud.Pass), Is.True);
                        yield return Until(() => flow.InputOpen, 10f);
                    }

                    var mine = board.CellTop(flow.Client.View.Seats[0].Cell);
                    look = mine + new Vector3(3f, 0f, -2f);
                    rig.Set(BoardCameraRig.Zoom.Close, 2, look);
                    Assert.That(hud.Press(BoardHud.EndTurn), Is.True);
                    Assert.That(rig.Target, Is.EqualTo(look));
                    stage.Tweens.Advance(5000);
                    Assert.That(flow.Fx.Sequencer.IsEmpty, Is.True, "넘김 항목을 한 번에 건너뛰었다");
                    Assert.That(Vector3.Distance(rig.Target, mine), Is.LessThan(1e-4f), "건너뛴 턴 넘김: 카메라는 내 말 칸을 본다");
                    Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Close));
                    Assert.That(rig.Yaw, Is.EqualTo(2));
                    yield return Until(() => flow.InputOpen, 10f);
                    Assert.That(flow.Client.View.Round, Is.EqualTo(4));
                    look = mine + new Vector3(3f, 0f, -2f);
                    rig.Set(BoardCameraRig.Zoom.Close, 2, look);
                }
                int updates = flow.Client.Updates;
                var act = flow.Client.Choice(ChoiceKind.MapAction);
                var lastAnswer = flow.Client.Legal(act).Find(a => a.Kind == AnswerKind.Move);
                Assert.That(lastAnswer, Is.Not.Null, "이동할 칸이 있다");
                Assert.That(flow.Answer(act, lastAnswer).Accepted, Is.True);
                Assert.That(flow.Client.Updates, Is.GreaterThan(updates), "갱신이 왔다");
                yield return null;
                Assert.That(flow.OnBoard, Is.True);
                yield return Until(() => rig.Target != look, 5f);
                Assert.That(rig.Target, Is.Not.EqualTo(look), "걷기 시작하면 보는 점이 말 쪽으로 옮겨 간다(다른 곳을 보고 있었어도)");
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Close), "갱신이 와도, 카메라가 따라가도 줌 그대로");
                Assert.That(rig.Yaw, Is.EqualTo(2), "갱신이 와도, 카메라가 따라가도 방향 그대로");
                Assert.That(rig.InputEnabled, Is.True);

                // 타이틀로 돌아가면 시험 판으로. 조무래기 놓기 도중에 떠나도 「떨어지는 중」이 다음 판에 남지 않는다
                foreach (int c in new List<int>(tokens.GruntCells)) tokens.Drop(c, 0.5f);
                Assert.That(tokens.Dropping, Is.True, "고향 마을의 조무래기 토큰이 아직 있다");
                flow.OpenTitle();
                Assert.That(tokens.Dropping, Is.False, "게임을 떠나면 떨어지던 토큰도 지운다");
                yield return null;
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
                Assert.That(flow.Session, Is.Null);
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(6));
                Assert.That(rig.InputEnabled, Is.False);
                Assert.That(rig.CurrentZoom, Is.EqualTo(BoardCameraRig.Zoom.Full));
                Assert.That(rig.Target, Is.EqualTo(TitleScreen.BoardTarget(1920, 1080)));
                // 넘김 연출이 도는 중에 떠났다: HUD·이동 표시·대기열이 남지 않는다
                Assert.That(stage.Hud, Is.Null, "타이틀에는 보드 HUD가 없다");
                Assert.That(stage.Root.Q("board-hud"), Is.Null);
                Assert.That(flow.Fx, Is.Null);
                Assert.That(board.Marks.Visible, Is.False);
                yield return null;
                yield return null;
                Assert.That(boot.Root.Stage.Tweens.Busy, Is.EqualTo(0), "버린 대기열은 실행기에서 빠진다");
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (rt != null)
                {
                    var root = Object.FindAnyObjectByType<ScreenRoot>();
                    if (root != null) root.SetTarget(null);
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        /// <summary>
        /// 트레이너 카드를 끈 판(솔로 설정 「사용 안 함」, 04 §3-1 · plan §5 #38 ⑨): 직업이 없으니 말은 셋업이 끝난 뒤에 서고, 「말 그림」이 직업 그림이어도 고른 주인공의 도트다.
        /// </summary>
        [UnityTest]
        public IEnumerator M4_트레이너카드를끈판의말은마을을고르면서고설정이직업그림이어도주인공도트다_결정60()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
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
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var board = Object.FindAnyObjectByType<BoardBoot>();
                boot.PointerFromMouse = false;
                const string rosa = "b2w2_001_rosa";
                flow.PieceStyle = PieceStyle.Job;
                flow.StartSolo(new SoloSettings { TrainerCards = false, Protagonist = rosa }, 11, 99);
                yield return null;
                yield return null;
                var client = flow.Client;
                Assert.That(client.View.Options.TrainerCards, Is.False);

                // 마을을 고르기 전에는 말이 없다
                var looks = GameFlow.SeatLooks(flow.Ctx, client.View, flow.Protagonist, out var cells);
                Assert.That(looks[0].Protagonist, Is.Null, "마을을 고르기 전에는 주인공을 싣지 않는다");
                Assert.That(cells[0], Is.EqualTo(-1));
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(0));

                // 셋업 중: 고향 마을을 고르면 그 마을에 말이 떨어져 선다(04 §8-8 2행, 결정 66 ③) — 직업이 없으니 주인공 도트
                var village = (SetupVillageScreen)stage.Current;
                village.Pick(0);
                Assert.That(village.Confirm(), Is.True);
                Assert.That(flow.QueueLeftMs, Is.EqualTo(1800 + FxTune.ClickGuardMs.Value), "트레이너 카드를 끈 판은 카드 받기 연출이 없다");
                flow.Skip(flow.QueueLeftMs);
                yield return null;
                yield return null;
                Assert.That(client.View.Phase, Is.EqualTo(Phase.Setup));
                Assert.That(client.View.Seats[0].HomeVillage, Is.GreaterThanOrEqualTo(0));
                looks = GameFlow.SeatLooks(flow.Ctx, client.View, flow.Protagonist, out cells);
                Assert.That(looks[0].Trainer, Is.Null);
                Assert.That(looks[0].Protagonist, Is.EqualTo(rosa), "마을을 고른 좌석은 주인공을 싣는다");
                Assert.That(cells[0], Is.EqualTo(client.View.Seats[0].Cell), "고른 마을의 칸");
                Assert.That(BoardPieces.StyleOf(looks[0], PieceStyle.Job), Is.EqualTo(PieceStyle.Dot));
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(1), "고른 마을에 말이 섰다");
                Assert.That(board.Pieces.Pieces[0].Root.activeSelf, Is.True);

                // 스타팅 포켓몬만 고르면 확정된다(트레이너 카드 칸이 없다)
                var pick = (SetupPickScreen)stage.Current;
                Assert.That(pick.Cards.Count, Is.EqualTo(0));
                pick.PickStarter(0);
                Assert.That(pick.Confirm(), Is.True);
                Assert.That(pick.CardSent, Is.False);
                flow.Skip(flow.MsUntil(s => s.Row == ActBeats.Row));   // 고른 뒤 · 스타팅 포켓몬 공개 · 숨겨진 장소 배치를 지나 보드 HUD가 뜬다
                yield return null;
                yield return null;
                Assert.That(client.View.Phase, Is.Not.EqualTo(Phase.Setup));
                Assert.That(client.View.Seats[0].TrainerCard, Is.EqualTo(-1));

                // 셋업 뒤: 말 하나 = 고른 주인공의 도트(설정은 직업 그림 그대로)
                looks = GameFlow.SeatLooks(flow.Ctx, client.View, flow.Protagonist, out cells);
                Assert.That(looks[0].Trainer, Is.Null);
                Assert.That(looks[0].Protagonist, Is.EqualTo(rosa));
                Assert.That(cells[0], Is.EqualTo(client.View.Seats[0].Cell));
                Assert.That(cells[0], Is.GreaterThanOrEqualTo(0));
                Assert.That(flow.PieceStyle, Is.EqualTo(PieceStyle.Job));
                Assert.That(board.Pieces.Pieces.Count, Is.EqualTo(1), "셋업이 끝나면 내 말 하나");
                var piece = board.Pieces.Pieces[0];
                Assert.That(piece.Style, Is.EqualTo(PieceStyle.Dot), "직업이 없으면 설정이 직업 그림이어도 주인공 도트");
                Assert.That(piece.Protagonist, Is.EqualTo(rosa));
                Assert.That(piece.Trainer, Is.Null);
                Assert.That(piece.Texture, Is.Not.Null, "필드 도트 그림(Gen)");
                Assert.That(FaceBox.Shown(stage.Root.Q("board-hud-seat-1-face")), Is.EqualTo(rosa), "위 띠의 얼굴 상자도 고른 주인공");
                Assert.That(piece.Spot, Is.Not.Null, "말이 판 위에 섰다");
            }
            finally
            {
                Application.logMessageReceived -= Grab;
            }
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
