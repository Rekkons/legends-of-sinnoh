using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Net;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// plan M7 「튜토리얼 입구 · 연출 실접속」(결정 38 · 완료 조건 6 — 02 §3-2 · §5-7 · §8, 04 §8-6 · P15 [가정 M7-71~76]): 보드 씬의 화면 흐름으로 —
    /// ① 참가 화면(S06)의 [튜토리얼] → 튜토리얼 → [타이틀로] · 목차 [뒤로]가 참가 화면으로 돌아온다(참가 칸 글 그대로) ·
    /// ② 로비(S07) 손님의 [튜토리얼]은 자리를 둔 채(준비만 풀림) 들어가 로비로 돌아오고, 그 사이 방이 닫히면 참가 화면에 이유 ·
    /// ③ 손님 PC 화면(좌석 1)이 망 위에서 셋업 「고른 뒤」 꼬리표 · 좌석 칩의 방 닉네임 · 보드 턴(찾아보기) · 배틀 끝까지 ·
    /// ④ 6인 판(NetworkManager 6개 한 프로세스 · 실시각): 손님 화면이 갱신마다 방장 멈춤 끝 − 내 화면 대기열 끝을 적는다(최댓값 보고) · 방장의 Host 조정 값이 손님에 맞춰진다.
    /// 실제 UGS를 부르지 않는다(직접 연결 127.0.0.1만). 튜토리얼 기록은 메모리(사용자 설정 금지). Gen 겉모양이 있어야 돈다.
    /// </summary>
    public sealed class RoomSyncFlowTests
    {
        const string Loopback = "127.0.0.1";

        sealed class VClock
        {
            public long Now;
        }

        readonly List<string> errors = new List<string>();
        readonly List<NetRunner> runners = new List<NetRunner>();
        ScreenBoot boot;
        RenderTexture rt;

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
        }

        static void Report(string line)
        {
            Debug.Log(line);
            TestContext.Out.WriteLine(line);
        }

        IEnumerator Boot()
        {
            Application.logMessageReceived += Grab;
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            Assert.That(boot.Flow.Ctx, Is.Not.Null);
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            boot.Flow.Tutorial.Record = TutorialRecord.Memory();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Tunables.Shared.Reset();
            if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();   // 방 닫기 · 나가기
            foreach (var r in runners)
                if (r) r.Shutdown();
            float wait = Time.realtimeSinceStartup + 5;
            while (Time.realtimeSinceStartup < wait && runners.Exists(r => r && r.GetComponent<NetworkManager>() is var m && m != null && (m.IsListening || m.ShutdownInProgress)))
                yield return null;
            yield return null;
            foreach (var r in runners)
                if (r) UnityEngine.Object.Destroy(r.gameObject);
            runners.Clear();
            float end = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < end && UnityEngine.Object.FindObjectsByType<NetRunner>(FindObjectsSortMode.None).Length > 0) yield return null;
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<NetRunner>(FindObjectsSortMode.None).Length, "방이 남았다");
            Application.logMessageReceived -= Grab;
            if (rt != null)
            {
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                rt = null;
            }
            boot = null;
            errors.Clear();
        }

        // ── ① 참가 화면 → 튜토리얼 → 돌아오기 ──

        [UnityTest, NUnit.Framework.Timeout(120000)]
        public IEnumerator M7_입구_참가화면의튜토리얼로들어갔다가_돌아가기와목차뒤로가참가화면으로돌아온다_참가칸글은그대로()
        {
            yield return Boot();
            var flow = boot.Flow;
            var stage = flow.Stage;
            flow.Room.Board = RoomFlow.NoRooms;

            flow.Room.OpenJoin();
            var join = (RoomJoinScreen)stage.Current;
            join.SetTarget("192.168.0.7:7777");
            yield return null;
            var button = stage.Root.Q<Button>("room-join-tutorial");
            Assert.IsNotNull(button, "참가 화면의 [튜토리얼]");
            Assert.AreEqual("튜토리얼", button.text.Replace("⁠", ""));
            Assert.IsTrue(button.enabledSelf);
            Assert.IsTrue(flow.Room.CanTutorial);

            // 기록이 없으면 본편 첫 장으로 바로(타이틀의 [튜토리얼]과 같은 길)
            Assert.IsTrue(join.Tutorial(), "[튜토리얼]");
            Assert.IsTrue(flow.Tutorial.Running, "튜토리얼 판");
            Assert.IsTrue(flow.Room.InTutorial);
            Assert.IsFalse(flow.Room.Parked, "참가 화면에서는 방이 없다");
            Assert.IsNull(flow.Room.Net);
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(flow.Tutorial.Running, "몇 프레임 뒤에도 그대로");

            // 끝내기 창의 [타이틀로]는 이 길에서 「돌아가기」(결정 112 Q14) → 타이틀이 아니라 참가 화면(참가 칸 글 그대로)
            Assert.IsTrue(flow.Tutorial.OpenQuit());
            yield return null;
            Assert.AreEqual("돌아가기", stage.Root.Q<Button>("tut-quit-title").text.Replace("⁠", ""), "방 길의 끝내기 창");
            flow.Tutorial.ToTitle();
            Assert.IsInstanceOf<RoomJoinScreen>(stage.Current, "[타이틀로] → 들어온 참가 화면");
            Assert.AreEqual("192.168.0.7:7777", ((RoomJoinScreen)stage.Current).TargetText);
            Assert.IsFalse(flow.Tutorial.Running);
            Assert.IsFalse(flow.Room.InTutorial, "돌아왔다");
            yield return null;

            // 기록이 생겼으니 이번에는 목차 → [뒤로]도 참가 화면으로
            Assert.IsTrue(((RoomJoinScreen)stage.Current).Tutorial());
            Assert.IsInstanceOf<TutorialMenuScreen>(stage.Current, "기록이 있으면 목차");
            ((TutorialMenuScreen)stage.Current).Back();
            Assert.IsInstanceOf<RoomJoinScreen>(stage.Current, "목차 [뒤로] → 참가 화면");

            // 참가 화면의 [뒤로]는 타이틀 — 그 뒤 타이틀의 [튜토리얼]은 타이틀로 돌아온다(돌아갈 곳을 잊었다)
            ((RoomJoinScreen)stage.Current).Back();
            Assert.IsInstanceOf<TitleScreen>(stage.Current);
            flow.Tutorial.Open();
            Assert.IsInstanceOf<TutorialMenuScreen>(stage.Current);
            ((TutorialMenuScreen)stage.Current).Back();
            Assert.IsInstanceOf<TitleScreen>(stage.Current, "타이틀에서 들어간 튜토리얼은 타이틀로");
            Assert.AreEqual("screen.tutorial.menu.to_title", flow.Tutorial.TitleKey, "타이틀에서 들어간 튜토리얼의 끝내기 창은 「타이틀로」 그대로");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        // ── ② 로비 → 튜토리얼(자리 유지) → 로비 · 방 닫힘 ──

        [UnityTest, NUnit.Framework.Timeout(120000)]
        public IEnumerator M7_입구_로비손님의튜토리얼은자리를둔채준비만풀고_돌아오면로비_튜토리얼중방이닫히면참가화면에이유()
        {
            yield return Boot();
            const ushort port = 47831;
            var flow = boot.Flow;
            var stage = flow.Stage;
            flow.Room.Board = RoomFlow.NoRooms;
            var vc = new VClock();
            var host = OpenHost(flow, port, 2, vc);
            yield return JoinAsGuest(flow, port, "손님트레이너");
            var lobby = (LobbyScreen)stage.Current;
            Assert.IsTrue(lobby.ToggleReady(), "[준비 완료]");
            yield return Until(() => host.Host.Room.CanStart, 10, "손님 준비 → 시작 가능");
            yield return Until(() => stage.Root.Q<Button>("lobby-tutorial") != null, 5, "로비의 [튜토리얼]");
            Assert.IsNull(stage.Root.Q("lobby-start"), "손님 화면");

            // 자리를 둔 채 튜토리얼로: 연결 유지 · 준비는 풀린다(방장이 나 없이 시작하지 못한다)
            Assert.IsTrue(((LobbyScreen)stage.Current).Tutorial(), "[튜토리얼]");
            Assert.IsTrue(flow.Tutorial.Running);
            Assert.IsTrue(flow.Room.Parked, "방에 자리를 둔 채");
            Assert.IsNotNull(flow.Room.Net, "방은 그대로");
            yield return Until(() => !host.Host.Room.CanStart, 30, "준비가 풀렸다");
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(NetStatus.Connected, flow.Room.Net.Seat.Status, "연결 유지");
            Assert.IsTrue(host.Host.Room.Seats[1] != null && host.Host.Room.Seats[1].Connected && !host.Host.Room.Seats[1].Ready, "방장이 본 자리: 접속 · 준비 안 됨");
            Assert.IsFalse(flow.Room.InGame, "튜토리얼 판은 방 판이 아니다(응답 시계 · 핑 없음)");
            Assert.IsFalse(flow.InRoomGame);
            Assert.IsTrue(flow.Tutorial.Running, "방 흐름이 튜토리얼을 건드리지 않는다");

            // [돌아가기](결정 112 Q14) → 로비(같은 자리)
            Assert.IsTrue(flow.Tutorial.OpenQuit());
            yield return null;
            Assert.AreEqual("돌아가기", stage.Root.Q<Button>("tut-quit-title").text.Replace("⁠", ""), "로비 길의 끝내기 창");
            flow.Tutorial.ToTitle();
            Assert.IsInstanceOf<LobbyScreen>(stage.Current, "들어온 로비로");
            Assert.IsFalse(flow.Room.Parked);
            Assert.AreEqual(1, flow.Room.Net.Seat.Seat, "같은 자리");
            yield return null;
            Assert.IsTrue(((LobbyScreen)stage.Current).ToggleReady(), "돌아와 다시 준비");
            yield return Until(() => host.Host.Room.CanStart, 10, "다시 시작 가능");

            // 다시 튜토리얼 → 그 사이 방장이 방을 닫는다 → 참가 화면에 닫힘 이유(튜토리얼은 놓는다)
            yield return Until(() => stage.Root.Q<Button>("lobby-tutorial") != null, 5, "[튜토리얼]");
            Assert.IsTrue(((LobbyScreen)stage.Current).Tutorial());
            Assert.IsInstanceOf<TutorialMenuScreen>(stage.Current, "기록이 있으니 목차");
            Assert.IsTrue(flow.Room.Parked);
            host.Close();
            yield return Until(() => stage.Current is RoomJoinScreen, 15, "방이 닫히면 참가 화면");
            Assert.AreEqual(NetCode.HostClosed, flow.Room.FailCode);
            Assert.IsNull(flow.Room.Net);
            Assert.IsFalse(flow.Room.InTutorial);
            yield return null;
            Assert.IsNotNull(stage.Root.Q("room-join-fail"), "닫힘 이유 줄");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        // ── ③ 손님 화면으로 셋업 → 보드 턴 → 배틀 끝 ──

        [UnityTest, NUnit.Framework.Timeout(300000)]
        public IEnumerator M7_실접속_손님화면이망위에서셋업고른뒤꼬리표_좌석칩은방닉네임_보드턴찾아보기_배틀끝까지_에러로그0()
        {
            yield return Boot();
            const ushort port = 47832;
            var flow = boot.Flow;
            var stage = flow.Stage;
            flow.DriftLog = false;   // 가짜 시각으로 건너뛰는 판 — 차이는 ④가 실시각으로 잰다
            var vc = new VClock();
            var host = OpenHost(flow, port, 2, vc);
            var hostBot = new Bot(host.Local, host.Send, flow.Ctx, null);
            yield return JoinAsGuest(flow, port, "손님트레이너");
            Assert.IsTrue(((LobbyScreen)stage.Current).ToggleReady());
            yield return Until(() => host.Host.Room.CanStart, 10, "시작 가능");
            host.Send(new StartGame());
            yield return Until(() => flow.InRoomGame && flow.Client != null && flow.Client.View != null, 15, "방 판");
            int me = flow.Client.Seat;
            Assert.AreEqual(1, me);

            var pilot = new SoloPilot();
            flow.EventsSeen += pilot.Saw;
            var tags = new HashSet<ChoiceKind>();
            bool battled = false, sawBattle = false;
            int stuck = 0, lastChoice = -1;
            float end = Time.realtimeSinceStartup + 270;
            int idle = 0, last = -1;
            while (!(battled && flow.OnBoard && !(stage.Current is BattleScreen) && flow.Battle == null))
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail($"시간 초과: 손님 화면 보드 턴 → 배틀 끝(명령 {host.Host.Game?.CommandCount} · 배틀 봄 {sawBattle} · 화면 {stage.Current?.Name})");
                bool acted = hostBot.Step();
                // 「고른 뒤」(11행) 단계의 꼬리표 — 망 위에서는 답의 결과가 다음 갱신으로 온다
                foreach (var entry in flow.Fx.Sequencer.Queued)
                    foreach (var step in entry.Steps)
                        if (step.Row == SetupRows.Chosen && step.Tag is ChoiceKind k) tags.Add(k);
                if (flow.Battle != null) sawBattle = true;
                if (sawBattle && flow.Battle == null) battled = true;
                var choice = flow.Client.FirstChoice();
                if (choice != null && host.Host.HoldLeftMs(me) == 0)
                {
                    bool pressed = false;
                    if (stage.Current is SetupVillageScreen village && flow.Client.Choice(ChoiceKind.VillagePick) != null)
                    {
                        village.Pick(0);
                        pressed = village.Confirm();
                    }
                    else if (stage.Current is SetupPickScreen pick)
                    {
                        if (!pick.StarterSent)
                        {
                            pick.PickStarter(0);
                            pick.PickCard(0);
                            pressed = pick.Confirm();
                        }
                    }
                    else if (flow.OnBoard) pressed = FlowPilot.Step(flow, pilot);
                    acted |= pressed;
                    // 길잡이가 화면으로 누르지 못하는 선택이 오래 남으면(이 판의 시드에서 길잡이가 모르는 창) 기본 답으로 넘긴다 — 판이 서지 않게
                    stuck = pressed || choice.Id != lastChoice ? 0 : stuck + 1;
                    lastChoice = choice.Id;
                    if (stuck > 400 && flow.OnBoard)
                    {
                        flow.Answer(choice, choice.Default.Clone());
                        stuck = 0;
                    }
                }
                else if (flow.OnBoard && stage.Current is BattleScreen) acted |= FlowPilot.Step(flow, pilot);   // 결과 창 [확인]
                if (!host.Host.AwaitingAi)
                {
                    long hold = 0;
                    for (int s = 0; s < host.Host.SeatCount; s++) hold = Math.Max(hold, host.Host.HoldLeftMs(s));
                    vc.Now = Math.Max(vc.Now + 50 + hold, host.Host.AnimEndMs);
                    int n = host.Host.Game?.CommandCount ?? 0;
                    idle = acted || n != last ? 0 : idle + 1;
                    last = n;
                    if (idle >= 3)
                    {
                        long t = host.Host.NextEventMs;
                        if (t != long.MaxValue && t > vc.Now) vc.Now = t;
                        idle = 0;
                    }
                }
                flow.Skip(200);
                yield return null;
            }
            CollectionAssert.AreEquivalent(new[] { ChoiceKind.StarterPick, ChoiceKind.TrainerCardPick }, tags, "망 위에서도 「고른 뒤」 꼬리표가 붙는다(스타팅 포켓몬 · 트레이너 카드)");
            Assert.IsNull(flow.Session, "세션은 방장 PC에 있다");
            Assert.IsInstanceOf<BoardHud>(flow.Hud);
            // 좌석 칩 · 기록의 이름 = 방 닉네임(「플레이어 n」이 아니다)
            Assert.AreEqual("방장", flow.SeatName(0));
            Assert.AreEqual("손님트레이너", flow.SeatName(1));
            for (int i = 0; i < 3; i++) yield return null;
            var names = stage.Root.Query<Label>(className: "sn-hud-seat-name").ToList().Select(l => l.text.Replace("⁠", "")).ToList();
            CollectionAssert.Contains(names, "방장", "HUD 좌석 칩");
            CollectionAssert.DoesNotContain(names, "플레이어 1");
            Assert.AreEqual(0, flow.NotReadyShown, "받을 화면이 없는 선택이 없다");
            Assert.IsEmpty(hostBot.Bad, "합법 밖 거절 없음");
            Assert.AreEqual(StateHash.Of(host.Host.Game.ViewFor(me)), StateHash.Of(flow.Client.View), "손님 화면의 보기 = 방장이 준 내 보기");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        // ── ③-2 남의 야생 배틀(결정 114 · 120): 창이 없으면 저절로 열린다 · [보드로]로 접으면 뒤에서 끝까지 그린다 · 「○○ 야생 배틀 중」 [보기] → 배틀 화면 · Esc ──

        [UnityTest, NUnit.Framework.Timeout(300000)]
        public IEnumerator M7_관전야생전_남의야생배틀은창이없으면저절로열리고접으면뒤에서그린다_보기로다시열며_결과창없이끝나고끝시각이맞는다()
        {
            yield return Boot();
            const ushort port = 47836;
            var flow = boot.Flow;
            var stage = flow.Stage;
            flow.DriftLog = false;
            var vc = new VClock();
            var host = OpenHost(flow, port, 2, vc);
            // 방장 = 맵 행동에서 찾아보기를 먼저(없으면 한 칸 걷기) · 배틀은 길잡이 — 갤럭시단 도전만 되풀이하지 않게
            var hostBot = new Bot(host.Local, host.Send, flow.Ctx, new SoloPilot())
            {
                Prefer = (v, c, legal) => c.Kind != ChoiceKind.MapAction ? null : legal.Find(a => a.Kind == AnswerKind.Search) ?? (v.Seats[c.Seat].Moved ? null : legal.Find(a => a.Kind == AnswerKind.Move)),
            };
            yield return JoinAsGuest(flow, port, "손님트레이너");
            Assert.IsTrue(((LobbyScreen)stage.Current).ToggleReady());
            yield return Until(() => host.Host.Room.CanStart, 10, "시작 가능");
            host.Send(new StartGame());
            yield return Until(() => flow.InRoomGame && flow.Client != null && flow.Client.View != null, 15, "방 판");
            int me = flow.Client.Seat;
            var pilot = new SoloPilot();
            flow.EventsSeen += pilot.Saw;
            var battles = new List<string>();
            flow.EventsSeen += evs => { foreach (var e in evs) if (e is BattleStarted bs) battles.Add(bs.Kind + ":" + string.Join("/", System.Array.ConvertAll(bs.Sides ?? new BattleSideInfo[0], x => x == null ? "-" : x.Owner.ToString()))); };
            float end = Time.realtimeSinceStartup + 280;
            int idle = 0, last = -1, stuck = 0, lastChoice = -1;
            bool asideSeen = false, autoOpened = false, wrongOpen = false, drewInside = false, opened = false, ended = false;
            long driftBefore = 0;
            while (!ended)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail($"시간 초과: 방장의 야생 배틀(명령 {host.Host.Game?.CommandCount} · 봄 {asideSeen} · 화면 {stage.Current?.Name} · 본 배틀 {string.Join(" ", battles)} · WatchWild {flow.WatchWildBattles})");
                bool acted = hostBot.Step();
                if (flow.WildAside)
                {
                    if (!asideSeen) driftBefore = flow.Drift.MaxMs;
                    asideSeen = true;
                    if (stage.Current is BattleScreen && !flow.WatchingWild) wrongOpen = true;
                    foreach (var entry in flow.Fx.Sequencer.Queued)
                        foreach (var step in entry.Steps)
                            if (step.Events.Exists(e => e is DiceRolled || e is HpChanged)) drewInside = true;
                    if (!opened && flow.WatchingWild && stage.Current is BattleScreen && flow.OnBoard)
                    {
                        // 결정 120: 창이 없으니 저절로 열렸다 → [보드로](접기) → 다시 열지 않는다 · 표시 줄 [보기] → 그 배틀 화면 → Esc → 보드
                        autoOpened = true;
                        for (int i = 0; i < 3; i++) yield return null;
                        StringAssert.Contains("관전 중", flow.RoomLayer.WildText);
                        Assert.IsNotNull(stage.Root.Q("room-wild-back"), "[보드로]");
                        Assert.IsTrue(flow.CloseWildWatch(), "[보드로]");
                        Assert.IsNull(stage.Current, "보드로 돌아왔다(접기)");
                        Assert.IsTrue(flow.WildAside && flow.WildFolded, "배틀은 뒤에서 계속 · 접힘");
                        flow.Skip(200);
                        for (int i = 0; i < 3; i++) yield return null;
                        Assert.IsNull(stage.Current, "접은 배틀은 저절로 다시 열지 않는다");
                        StringAssert.Contains("방장 야생 배틀 중", flow.RoomLayer.WildText.Replace("⁠", ""));
                        Assert.IsNotNull(stage.Root.Q("room-wild-open"), "[보기]");
                        Assert.IsTrue(flow.OpenWildWatch(), "[보기]");
                        Assert.IsInstanceOf<BattleScreen>(stage.Current, "가운데 필드 = 그 배틀 화면");
                        Assert.IsTrue(flow.Escape(), "Esc");
                        Assert.IsFalse(flow.WatchingWild);
                        Assert.IsNull(stage.Current, "Esc → 보드");
                        opened = true;
                    }
                }
                else if (asideSeen && flow.Battle == null) ended = true;
                var choice = flow.Client.FirstChoice();
                if (choice != null && host.Host.HoldLeftMs(me) == 0)
                {
                    bool pressed = false;
                    if (stage.Current is SetupVillageScreen village && flow.Client.Choice(ChoiceKind.VillagePick) != null)
                    {
                        village.Pick(0);
                        pressed = village.Confirm();
                    }
                    else if (stage.Current is SetupPickScreen pick)
                    {
                        if (!pick.StarterSent)
                        {
                            pick.PickStarter(0);
                            pick.PickCard(0);
                            pressed = pick.Confirm();
                        }
                    }
                    else if (flow.OnBoard) pressed = FlowPilot.Step(flow, pilot);
                    acted |= pressed;
                    stuck = pressed || choice.Id != lastChoice ? 0 : stuck + 1;
                    lastChoice = choice.Id;
                    if (stuck > 400 && flow.OnBoard)
                    {
                        flow.Answer(choice, choice.Default.Clone());
                        stuck = 0;
                    }
                }
                else if (flow.OnBoard && stage.Current is BattleScreen && !flow.WildAside) acted |= FlowPilot.Step(flow, pilot);   // 내 배틀의 결과 창 [확인]
                if (!host.Host.AwaitingAi)
                {
                    long hold = 0;
                    for (int s = 0; s < host.Host.SeatCount; s++) hold = Math.Max(hold, host.Host.HoldLeftMs(s));
                    vc.Now = Math.Max(vc.Now + 50 + hold, host.Host.AnimEndMs);
                    int n = host.Host.Game?.CommandCount ?? 0;
                    idle = acted || n != last ? 0 : idle + 1;
                    last = n;
                    if (idle >= 3)
                    {
                        long t = host.Host.NextEventMs;
                        if (t != long.MaxValue && t > vc.Now) vc.Now = t;
                        idle = 0;
                    }
                }
                flow.Skip(200);
                yield return null;
            }
            Assert.IsTrue(opened, "저절로 열린 배틀을 접고 [보기]로 다시 열어 봤다");
            Assert.IsTrue(autoOpened, "창이 없으면 남의 야생 배틀도 가운데 필드로 저절로 연다(결정 120)");
            Assert.IsFalse(wrongOpen, "보는 중이 아닌데 배틀 화면이 열린 적 없다(접은 배틀 — 결정 114)");
            Assert.IsTrue(drewInside, "뒤에서 배틀 안(굴림 · 체력)까지 그렸다(결정 112 Q19)");
            Assert.IsFalse(stage.Current is BattleScreen, "보지 않던 배틀은 결과 창 없이 끝난다");
            Assert.IsFalse(flow.Fx.Sequencer.Paused, "화면 대기열이 결과 창을 기다리며 서지 않는다");
            Assert.AreEqual("", flow.RoomLayer.WildText, "끝나면 표시가 사라진다");
            Assert.That(flow.Drift.MaxMs, Is.LessThan(6000), $"끝 시각 맞춤: 방장 멈춤 끝 − 내 대기열 끝 최대 {flow.Drift.MaxMs} ms(배틀 전 {driftBefore} — 남의 야생 배틀을 빼던 때는 +13초)");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        // ── ④ 6인 판 · 실시각: 방장 멈춤 끝 − 손님 화면 대기열 끝 ──

        /// <summary>
        /// 내 화면이 방장 멈춤보다 늦게 끝나도 되는 한도(ms) [가정 M7-74]: 방장 멈춤과 손님 화면 대기열은 같은 길이 표에서 나오고, 손님 화면은 그리지 않는 행(남 좌석의 배틀 안 · 남의 창)을 덜어 내므로
        /// 차이(방장 − 내 화면)는 0 이상이어야 한다 — 음수(내 화면이 아직 보이는 중에 방장 멈춤이 끝나 내 시계가 먼저 돈다)는 셋업의 받은 카드 연출(1행 800 ms — 화면만 그리는 항목이라 방장 멈춤에 없다)
        /// + 프레임 · 루프백 지연만큼만. 양수(내 화면이 먼저 끝나 기다린다)는 기록만 한다.
        /// </summary>
        const long LateLimitMs = 800 + 400;

        [UnityTest, NUnit.Framework.Timeout(300000)]
        public IEnumerator M7_실접속_6인판실시각_손님화면이갱신마다방장멈춤끝과내대기열끝의차이를적는다_방장Host조정값은손님에맞춰진다()
        {
            yield return Boot();
            const ushort port = 47833;
            var flow = boot.Flow;
            var stage = flow.Stage;
            // 방장이 Host 조정 값을 바꿔 둔 방(개발 빌드 조정 창) — 판 시작 때 방 상태에 실려 손님에 온다(한 프로세스라 등록부는 하나 — 손님이 맞추는 셈은 EditMode M7_연출조정값_…)
            var walk = PlayTiming.WalkStepMs;
            var host = OpenHost(flow, port, 6, null);   // 실시각(Stopwatch)
            yield return JoinAsGuest(flow, port, "손님트레이너");
            var bots = new List<Bot> { new Bot(host.Local, host.Send, flow.Ctx, new SoloPilot()) };
            for (int i = 2; i < 6; i++)
            {
                var c = Join(port, Hi(flow, "g" + i, "봇" + i), "Guest" + i);
                int at = i;
                yield return Until(() => c.Status == NetStatus.Connected && c.Seat == at, 15, "손님 " + i);
                c.Send(new SetReady { Ready = true });
                bots.Add(new Bot(c, c.Send, flow.Ctx, new SoloPilot()));
            }
            Assert.IsTrue(((LobbyScreen)stage.Current).ToggleReady());
            yield return Until(() => host.Host.Room.CanStart, 10, "6명 준비");
            Tunables.Shared.Set(walk.Key, walk.Default + 60);   // 방장이 판을 시작할 때의 값
            host.Send(new StartGame());
            yield return Until(() => flow.InRoomGame && flow.Client != null && flow.Client.View != null, 15, "방 판");
            Assert.AreEqual(6, flow.Client.View.Seats.Length, "6인 판");
            var tune = flow.Room.Net.Seat.Room.Tune;
            Assert.AreEqual((1, walk.Key, walk.Default + 60), (tune.Count, tune[0].Key, tune[0].Value), "방장의 Host 조정 값이 방 상태에 실려 왔다");
            Assert.AreEqual(walk.Default + 60, walk.Value, "그 값으로 맞췄다");
            int me = flow.Client.Seat;

            var pilot = new SoloPilot();
            flow.EventsSeen += pilot.Saw;
            float began = Time.realtimeSinceStartup, boardAt = -1, stuckSince = -1;
            int lastChoice = -1, arrived = 0;
            // 판이 보드에 닿은 뒤 30초 또는 갱신 80개까지(실시각 — 연출을 건너뛰지 않는다)
            while (true)
            {
                float now = Time.realtimeSinceStartup;
                if (now - began > 200) Assert.Fail($"시간 초과: 6인 실시각 판(명령 {host.Host.Game?.CommandCount} · 갱신 {flow.Drift.Count} · 화면 {stage.Current?.Name})");
                if (flow.OnBoard && boardAt < 0) boardAt = now;
                if (boardAt >= 0 && (now - boardAt > 30 || flow.Drift.Count >= 80)) break;
                foreach (var b in bots) b.Step();
                if (stage.Current is SetupDropScreen) arrived++;
                var choice = flow.Client.FirstChoice();
                if (choice != null)
                {
                    bool pressed = false;
                    if (stage.Current is SetupVillageScreen village && flow.Client.Choice(ChoiceKind.VillagePick) != null)
                    {
                        if (flow.SetupOpen)
                        {
                            village.Pick(0);
                            pressed = village.Confirm();
                        }
                    }
                    else if (stage.Current is SetupPickScreen pick)
                    {
                        if (!pick.StarterSent && flow.SetupOpen)
                        {
                            pick.PickStarter(0);
                            pick.PickCard(0);
                            pressed = pick.Confirm();
                        }
                    }
                    else if (flow.OnBoard) pressed = FlowPilot.Step(flow, pilot);
                    if (pressed || choice.Id != lastChoice) stuckSince = now;
                    lastChoice = choice.Id;
                    // 화면으로 누르지 못한 채 6초(연출이 끝나고도) 남으면 기본 답으로 — 판이 서지 않게
                    if (flow.OnBoard && now - stuckSince > 6 && flow.Fx.Sequencer.IsEmpty)
                    {
                        flow.Answer(choice, choice.Default.Clone());
                        stuckSince = now;
                    }
                }
                else if (flow.OnBoard && stage.Current is BattleScreen) FlowPilot.Step(flow, pilot);
                yield return null;
            }
            var d = flow.Drift;
            Report($"[M7 Sync] 6인 판 실시각 — 손님 좌석 {me} · 갱신 {d.Count} · 방장 멈춤 끝 − 내 대기열 끝: 최대 +{d.MaxMs} ms · 최소 {d.MinMs} ms · 가장 큰 차이 {d.WorstMs} ms · 명령 {host.Host.Game.CommandCount} · 라운드 {flow.Client.View.Round} · "
                + $"바쁜 구간 최저 {boot.Root.MinBusyFps:0.0} fps · 좌석 도착 화면 {arrived} 프레임");
            Assert.That(d.Count, Is.GreaterThanOrEqualTo(10), "갱신마다 적었다");
            Assert.That(arrived, Is.GreaterThan(0), "6인 좌석 도착 장면(고향 마을에 말이 떨어짐)이 손님 화면에 떴다");
            Assert.That(d.MinMs, Is.GreaterThanOrEqualTo(-LateLimitMs), "내 화면 대기열이 방장 멈춤보다 늦게 끝나지 않는다(늦으면 연출 중에 내 시계가 돈다)");
            Assert.AreEqual("봇2", flow.SeatName(2), "좌석 이름 = 방 닉네임");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        // ── 차림 ──

        static Hello Hi(GameFlow flow, string token, string nick) =>
            new Hello { BuildId = Application.version, ContentHash = flow.ContentHash ?? "", Token = token, Nickname = nick, Platform = Platform.Pc };

        NetHostSession OpenHost(GameFlow flow, ushort port, int seats, VClock vc)
        {
            var m = NetBootstrap.Create("Host");
            var r = m.gameObject.AddComponent<NetRunner>();
            runners.Add(r);
            if (vc != null) r.Clock = () => vc.Now;
            var cfg = new HostConfig
            {
                OwnerToken = "own", OwnerNickname = "방장", RoomId = "room-sync-" + port, BuildId = Application.version, ContentHash = flow.ContentHash ?? "", Seed = 20261118, AiSeed = SoloTrail.AiSeed,
                VirtualAgent = new PrincipleAgent(), Settings = new RoomSettings { Seats = seats, Clock = ClockPreset.Off },
            };
            var host = r.OpenHost(m, flow.Ctx, cfg, Hi(flow, "own", "방장"));
            Assert.IsTrue(host.Start(port, Loopback), "방 열기 " + port);
            return host;
        }

        NetClientSession Join(ushort port, Hello hello, string name)
        {
            var m = NetBootstrap.Create(name);
            var r = m.gameObject.AddComponent<NetRunner>();
            runners.Add(r);
            var c = new NetClientSession(m);
            r.Session = c;
            Assert.IsTrue(c.Connect(JoinTarget.Direct(Loopback, port), hello));
            return c;
        }

        static IEnumerator JoinAsGuest(GameFlow flow, ushort port, string nick)
        {
            flow.Room.OpenJoin();
            var join = (RoomJoinScreen)flow.Stage.Current;
            join.SetNickname(nick);
            join.SetTarget(Loopback + ":" + port);
            Assert.IsTrue(join.Join(), "[참가]");
            yield return Until(() => flow.Stage.Current is LobbyScreen && flow.Room.Net != null && flow.Room.Net.Seat.Status == NetStatus.Connected, 15, "손님 화면 로비");
        }

        static IEnumerator Until(Func<bool> done, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("시간 초과: " + what);
                yield return null;
            }
        }

        /// <summary>
        /// 한 좌석의 봇: 받은 보기에서 내 첫 선택에 한 번 답한다 — 셋업은 첫 합법 답, 그 밖은 길잡이(<see cref="SoloPilot"/> — 찾아보기 · 배틀 · 도전)가 고른 답(없거나 거절되면 Default). E_HOLD 거절은 다시.
        /// 길잡이 없는 봇(Default만)의 합법 밖 거절은 <see cref="Bad"/>에 적는다.
        /// </summary>
        sealed class Bot
        {
            public readonly NetClientSession Session;
            readonly Action<RoomMsg> send;
            readonly Ctx ctx;
            readonly SoloPilot pilot;
            /// <summary>길잡이보다 먼저 묻는 고르기(null = 길잡이 · Default) — 야생 관전 시험의 방장은 찾아보기를 먼저 한다.</summary>
            public Func<GameState, PendingChoice, List<ChoiceAnswer>, ChoiceAnswer> Prefer;
            readonly HashSet<int> sent = new HashSet<int>();
            readonly HashSet<int> fallback = new HashSet<int>();
            public readonly List<string> Bad = new List<string>();

            public Bot(NetClientSession session, Action<RoomMsg> send, Ctx ctx, SoloPilot pilot)
            {
                Session = session;
                this.send = send;
                this.ctx = ctx;
                this.pilot = pilot;
                if (pilot != null) session.Updated += u => pilot.Saw(u.Events);
                session.Received += m =>
                {
                    if (!(m is RejectedMsg r)) return;
                    sent.Remove(r.ChoiceId);
                    if (r.Reason == RejectReason.Hold) return;
                    fallback.Add(r.ChoiceId);
                    if (pilot == null && (r.Reason == RejectReason.Illegal || r.Reason == RejectReason.BadCommand || r.Reason == RejectReason.NotYourChoice)) Bad.Add($"좌석 {session.Seat} 선택 {r.ChoiceId}: {r.Reason}");
                };
            }

            public bool Step()
            {
                var v = Session.View;
                if (Session.Status != NetStatus.Connected || v == null) return false;
                var c = v.Pending.Find(x => x.Seat == Session.Seat && !x.Answered && !sent.Contains(x.Id));
                if (c == null) return false;
                var legal = new List<ChoiceAnswer>();
                Engine.Legal(v, ctx, c, legal);
                ChoiceAnswer a = null;
                if (!fallback.Contains(c.Id))
                {
                    if (v.Phase == Phase.Setup) a = legal.Count > 0 ? legal[0] : null;
                    else if (Prefer != null && (a = Prefer(v, c, legal)) != null) { }
                    else if (pilot != null) a = pilot.Decide(ctx, v, c, legal);
                }
                a = a ?? c.Default.Clone();
                sent.Add(c.Id);
                send(new Submit { Command = Command.AnswerTo(c.Id, a) });
                return true;
            }
        }
    }
}
