using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Data;
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
    /// plan M7 「화면 ② 대기실 · 재접속 · 메뉴 · 응답 시계」(04 S10 · S11 · S22 · 02 §5-7 · §6 · §7): 보드 씬의 화면 흐름으로 —
    /// ① 손님 PC의 화면(좌석 ≠ 0)이 로비 → 셋업을 화면으로 지나 보드까지(화면 ① 남은 것) ·
    /// ② 게임 중 손님 망이 끊기면 재접속 덮개 → 3초 뒤 같은 자리로 돌아와 덮개가 걷힘 → 방장이 방을 닫으면 덮개에 닫힘 이유 + [다시 시도] → [타이틀로] ·
    /// ③ 강퇴 투표 판(대상 아닌 접속 좌석 — 내 [찬성]으로 가결 → 대상은 E_KICKED · 알림 줄) ·
    /// ④ 방장 화면: 방 저장(직접 연결) → 셋업의 [방 닫기] → 타이틀 「이어하기」 → S04 방 저장 칸 → S05 「방 다시 열기」 → S10 이어하기 대기실 → 손님이 [다시 시도]로 돌아오면 저절로 재개.
    /// 실제 UGS를 부르지 않는다(직접 연결 127.0.0.1만). 저장은 임시 폴더(사용자 저장 폴더 금지). Gen 겉모양이 있어야 돈다.
    /// </summary>
    /// <para>⑤ 핑 찍기(결정 97, 02 §4-8): 방장이 찍으면 손님 화면의 그 칸에 표시 · 손님의 Alt + 클릭 · 빈도 제한 · 4초 뒤 사라짐.</para>
    /// <para>⑥ 소통(결정 102): 보드 밖 공개 대상의 핑이 손님 화면의 같은 요소에 · 종류 고리 · 감정 표현 말풍선 · 도장 · 상대별 끄기.</para>
    public sealed class RoomGameFlowTests
    {
        const string Loopback = "127.0.0.1";

        sealed class VClock
        {
            public long Now;
        }

        readonly List<string> errors = new List<string>();
        readonly List<NetRunner> runners = new List<NetRunner>();
        readonly List<string> dirs = new List<string>();
        ScreenBoot boot;
        RenderTexture rt;

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
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
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();   // 방 닫기 · 나가기
            foreach (var r in runners)
                if (r) r.Shutdown();
            // NGO 종료(방 닫기 유예 1초 포함)가 끝난 뒤에 없앤다 — 도는 중에 없애면 Transport가 이미 버린 연결 표를 읽는다(NetLoopTests와 같다)
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
            foreach (var d in dirs)
                try
                {
                    if (Directory.Exists(d)) Directory.Delete(d, true);
                }
                catch (IOException) { }
            dirs.Clear();
        }

        // ── ① 손님 PC 화면으로 보드까지 ──

        [UnityTest, NUnit.Framework.Timeout(180000)]
        public IEnumerator M7_화면2_손님PC화면이좌석1로로비준비_셋업을화면으로지나보드까지_방장은봇_에러로그0()
        {
            yield return Boot();
            const ushort port = 47821;
            var flow = boot.Flow;
            var vc = new VClock();
            var host = OpenHost(flow, port, 2, vc);
            var hostBot = new Bot(host.Local, host.Send, flow.Ctx);
            yield return JoinAsGuest(flow, port, "손님트레이너");
            Assert.AreEqual(1, flow.Room.Net.Seat.Seat, "손님 자리(좌석 ≠ 0)");
            Assert.IsInstanceOf<LobbyScreen>(flow.Stage.Current);
            // M9 다듬기 P06: 로비 자리 줄의 주인공 도트 · 「내 주인공」 칸 — 손님이 방장과 같은 인물을 골라도 된다(방이 돌려준 방 상태로 그린다)
            const string hostHero = "hgss_001_lyra", myHero = "b2w2_001_rosa";
            var lobby = (LobbyScreen)flow.Stage.Current;
            Assert.AreEqual(Protagonists.Keys[1], lobby.ProtagonistShown(1), "고르지 않은 손님은 자리 번호의 인물");
            Assert.IsNotNull(flow.Stage.Root.Q("lobby-proto-" + myHero), "「내 주인공」 여덟 칸");
            Assert.IsTrue(lobby.PickProtagonist(hostHero), "내 주인공 고르기");
            host.Send(new PickProtagonist { Key = hostHero });
            yield return Until(() => ((LobbyScreen)flow.Stage.Current).ProtagonistShown(0) == hostHero && ((LobbyScreen)flow.Stage.Current).ProtagonistShown(1) == hostHero, 10, "두 자리 줄이 같은 인물(겹쳐 고르기)");
            Assert.IsTrue(((LobbyScreen)flow.Stage.Current).PickProtagonist(myHero));
            yield return Until(() => ((LobbyScreen)flow.Stage.Current).ProtagonistShown(1) == myHero, 10, "내 자리 줄이 고른 인물로");
            Assert.IsTrue(flow.Stage.Root.Q("lobby-proto-" + myHero).ClassListContains("sn-opt-picked"), "고른 칸이 금색");
            Assert.IsFalse(flow.Stage.Root.Q("lobby-proto-" + hostHero).ClassListContains("sn-opt-picked"));
            Assert.IsTrue(((LobbyScreen)flow.Stage.Current).ToggleReady(), "[준비 완료]");
            yield return Until(() => host.Host.Room.CanStart, 10, "손님 준비 → 시작 가능");
            host.Send(new StartGame());
            yield return Until(() => flow.InRoomGame && flow.Client != null && flow.Client.View != null, 15, "손님 화면이 방 판에 들어간다");
            Assert.AreEqual(1, flow.Client.Seat, "내 좌석 = 1(손님)");
            Assert.IsNull(flow.Session, "세션은 방장 PC에 있다");

            yield return Drive(flow, host, vc, new[] { hostBot }, () => flow.OnBoard && flow.Hud != null, 120, "손님 화면 셋업 → 보드", answerSetup: true);
            Assert.IsInstanceOf<BoardHud>(flow.Hud);
            // P06: 방 판의 좌석 주인공 = 로비에서 고른 값 — 좌석 칩 얼굴 · 보드 말이 같이 쓴다
            Assert.AreEqual((hostHero, myHero), (flow.Protagonist(0), flow.Protagonist(1)), "좌석마다 로비에서 고른 인물");
            Assert.AreEqual(hostHero, FaceBox.Shown(flow.Stage.Root.Q("board-hud-seat-1-face")), "방장 좌석 칩 얼굴");
            Assert.AreEqual(myHero, FaceBox.Shown(flow.Stage.Root.Q("board-hud-seat-2-face")), "내 좌석 칩 얼굴");
            Assert.AreEqual((hostHero, myHero), (flow.BoardPieces.Of(0)?.Protagonist, flow.BoardPieces.Of(1)?.Protagonist), "보드 말");
            Assert.AreEqual(0, flow.NotReadyShown, "받을 화면이 없는 선택이 없다");
            Assert.AreNotEqual(Phase.Setup, host.Host.Game.ViewFor(1).Phase, "방 판의 셋업이 끝났다");
            Assert.IsNotNull(flow.RoomLayer?.Element?.parent, "방 판 덮개 층이 붙었다");
            Assert.IsFalse(flow.RoomLayer.CoverUp);
            Assert.IsEmpty(hostBot.Bad, "합법 밖 거절 없음");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        // ── ② 끊김 → 재접속 덮개 → 같은 자리 · 방 닫힘 → 이유 → 타이틀 ──

        [UnityTest, NUnit.Framework.Timeout(120000)]
        public IEnumerator M7_화면2_게임중손님망이끊기면재접속덮개_3초뒤같은자리로돌아와걷힘_방장이닫으면닫힘이유와다시시도_타이틀로()
        {
            yield return Boot();
            const ushort port = 47822;
            var flow = boot.Flow;
            var stage = flow.Stage;
            var vc = new VClock();
            var host = OpenHost(flow, port, 2, vc);
            yield return JoinAsGuest(flow, port, "손님트레이너");
            ((LobbyScreen)stage.Current).ToggleReady();
            yield return Until(() => host.Host.Room.CanStart, 10, "시작 가능");
            host.Send(new StartGame());
            yield return Until(() => flow.InRoomGame && flow.Client != null && flow.Client.View != null, 15, "방 판");
            int seat = flow.Client.Seat;

            // ① 손님 쪽 망이 끊긴 것처럼(사유 없음) → 덮개 「다시 연결하는 중」
            var manager = flow.Room.Net.Manager;
            Assert.IsNotNull(manager);
            manager.Shutdown();
            yield return Until(() => flow.RoomLayer != null && flow.RoomLayer.CoverUp && flow.Room.Live.Status == NetStatus.Reconnecting, 10, "재접속 덮개");
            Assert.IsNotNull(stage.Root.Q("room-reconnect-status"), "다시 연결하는 중 줄");
            Assert.AreEqual("연결이 끊겼습니다", Text(stage.Root, "room-reconnect-title"));
            Assert.IsNotNull(stage.Root.Q("room-reconnect-leave"), "[타이틀로]");
            Assert.IsTrue(flow.InRoomGame, "게임 화면은 그대로 — 덮개만");
            yield return Until(() => flow.Room.Live.Status == NetStatus.Connected && !flow.RoomLayer.CoverUp, 30, "3초 뒤 같은 토큰으로 다시 붙어 덮개가 걷힌다");
            Assert.AreEqual(seat, flow.Room.Net.Seat.Seat, "같은 자리");
            Assert.AreEqual(seat, flow.Client.Seat);
            yield return Until(() => flow.Client.View != null && StateHash.Of(flow.Client.View) == StateHash.Of(host.Host.Game.ViewFor(seat)), 10, "지금 보기(스냅샷)로 화면이 다시 섰다");
            Assert.IsTrue(host.Host.Room.Seats[seat].Connected, "방장이 본 자리도 다시 접속");

            // ② 방장이 방을 닫는다 → 덮개에 닫힘 이유 + [다시 시도](게임 중에 닫혔다 — 같은 주소로 다시 열면 붙는다) → [타이틀로]
            host.Close();
            yield return Until(() => flow.Room.Live != null && flow.Room.Live.Status == NetStatus.Failed && flow.RoomLayer.CoverUp, 15, "방 닫힘 → 덮개");
            Assert.AreEqual(NetCode.HostClosed, flow.Room.Live.FailCode);
            yield return null;
            Assert.AreEqual("방장이 방을 닫았습니다.", Text(stage.Root, "room-reconnect-fail-text"));
            Assert.IsNotNull(stage.Root.Q("room-reconnect-retry"), "[다시 시도]");
            flow.RoomLayer.Leave();
            Assert.IsInstanceOf<TitleScreen>(stage.Current, "[타이틀로]");
            Assert.IsNull(flow.Room.Net);
            Assert.IsFalse(flow.InRoomGame);
            yield return null;
            Assert.IsTrue(flow.RoomLayer.Element.parent == null, "덮개 층이 떨어졌다");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        // ── ③ 투표(강퇴 가결) ──

        [UnityTest, NUnit.Framework.Timeout(120000)]
        public IEnumerator M7_화면2_강퇴투표판이투표할좌석에뜨고_찬성하면가결_대상은E_KICKED_알림줄()
        {
            yield return Boot();
            const ushort port = 47823;
            var flow = boot.Flow;
            var stage = flow.Stage;
            var vc = new VClock();
            var host = OpenHost(flow, port, 3, vc);
            yield return JoinAsGuest(flow, port, "손님트레이너");
            var other = Join(port, Hi(flow, "g2", "용식"), "Guest2");
            yield return Until(() => other.Status == NetStatus.Connected, 15, "손님 둘째");
            Assert.AreEqual((1, 2), (flow.Room.Net.Seat.Seat, other.Seat));
            ((LobbyScreen)stage.Current).ToggleReady();
            other.Send(new SetReady { Ready = true });
            yield return Until(() => host.Host.Room.CanStart, 10, "시작 가능");
            host.Send(new StartGame());
            yield return Until(() => flow.InRoomGame && flow.Client != null && flow.Client.View != null && other.View != null, 15, "방 판");

            // 방장이 좌석 2(용식) 강퇴 투표를 연다 → 투표할 좌석 = 방장(연 좌석 — 찬성) · 나
            host.Send(new StartVote { Target = 2, Kind = VoteKind.Kick });
            yield return Until(() => flow.RoomLayer != null && flow.RoomLayer.CanVote && stage.Root.Q("room-vote-yes") != null, 10, "투표 판 · [찬성]");
            Assert.AreEqual("강퇴 투표: 용식", Text(stage.Root, "room-vote-title"));
            StringAssert.StartsWith("찬성 1/2", Text(stage.Root, "room-vote-count"));
            Assert.IsTrue(flow.RoomLayer.Cast(true), "[찬성]");
            yield return Until(() => other.Status == NetStatus.Failed, 10, "대상은 끊긴다");
            Assert.AreEqual(NetCode.Kicked, other.FailCode);
            Assert.IsTrue(host.Host.Room.Seats[2].Kicked, "강퇴됨");
            yield return Until(() => flow.Room.Live.Room.Vote == null && Notices(stage.Root).Any(t => t == "용식: 강퇴 투표가 가결되었습니다."), 10, "투표 판이 닫히고 알림 줄");
            Assert.IsNull(stage.Root.Q("room-vote-yes"));
            Assert.IsFalse(flow.RoomLayer.CoverUp, "나는 그대로");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        // ── ④ 방 저장 → S04 → S05 다시 열기 → S10 → 재개 ──

        [UnityTest, NUnit.Framework.Timeout(180000)]
        public IEnumerator M7_화면2_방장방저장을셋업에서닫고_S04방저장칸에서다시열면대기실_손님이돌아오면저절로재개([Values(5UL, 1UL)] ulong seed)
        {
            yield return Boot();
            const ushort port = 47824;
            string top = Path.Combine(Path.GetTempPath(), "sinnoh-roomgame-" + Guid.NewGuid().ToString("N"));
            dirs.Add(top);
            string saves = Path.Combine(top, "saves");
            Directory.CreateDirectory(saves);
            // 고향 마을을 먼저 고르는 좌석은 판 시드가 정한다(SetupFlow) — 시드 5 = 방장 먼저 · 1 = 손님 먼저(손님의 고향 마을 연출이 도는 동안 내 선택이 이미 열려 있다). 두 순서를 다 돈다
            boot.Entropy = () => seed;
            boot.UseSaves(saves);   // 방 저장은 그 옆 room-saves(임시 폴더 안) — 사용자 저장 폴더 금지
            var flow = boot.Flow;
            var stage = flow.Stage;
            var vc = new VClock();
            flow.Room.Listen = Loopback;
            flow.Room.Port = port;
            flow.Room.Clock = () => vc.Now;
            Assert.AreEqual(Path.Combine(top, "room-saves"), RoomFlow.RoomSaves(saves));

            // 방 만들기(직접 연결 · 2명) → 손님 봇 → 시작
            flow.Room.OpenCreate();
            var create = (RoomCreateScreen)stage.Current;
            create.SetInternet(false);
            create.SetSeats(2);
            flow.Room.Create.GalacticAi = Sinnoh.Rules.Session.GalacticAiLevel.Principle;   // 이 판은 원칙 AI로(새 방 기본값은 「강함」 — 결정 118 ②)
            flow.Room.Create.TrainerCards = true;   // 트레이너 카드를 쓰는 판(새 방 기본값은 끔 — 결정 134)
            Assert.IsTrue(create.Start(), "[방 만들기]");
            var guest = Join(port, Hi(flow, "g1", "빛나"), "Guest1");
            yield return Until(() => guest.Status == NetStatus.Connected, 15, "손님 접속");
            guest.Send(new SetReady { Ready = true });
            yield return Until(() => flow.Room.Live.Room != null && flow.Room.Live.Room.CanStart, 10, "시작 가능");
            Assert.IsTrue(((LobbyScreen)stage.Current).Start(), "[게임 시작]");
            yield return Until(() => flow.InRoomGame && flow.Client != null && flow.Client.View != null, 15, "방 판");
            var host = flow.Room.Net.Host;
            var bot = new Bot(guest, guest.Send, flow.Ctx);
            Assert.AreEqual(seed == 5UL ? 0 : 1, host.Host.Game.ViewFor(0).Pending.Find(c => c.Kind == ChoiceKind.VillagePick).Seat, "시드가 정한 첫 좌석");

            // 셋업: 내 고향 마을 차례까지(손님 봇이 먼저일 수 있다) → [방 닫기](셋업의 [뒤로] 자리)
            yield return Drive(flow, host, vc, new[] { bot }, () => stage.Current is SetupVillageScreen && flow.Client.Choice(ChoiceKind.VillagePick) != null, 60, "내 고향 마을 차례", answerSetup: false);
            Assert.AreEqual("방 닫기", (stage.Root.Q<Button>("setup-village-back")?.text ?? "").Replace("⁠", ""), "방 판 셋업의 [뒤로] = [방 닫기]");
            string roomId = host.Host.SaveId;
            var oldManager = flow.Room.Net.Manager;
            // 묻는 창(결정 112 Q8): [취소]면 그대로 · 다시 [방 닫기] → 창의 [방 닫기]
            var village = (SetupVillageScreen)stage.Current;
            village.Back();
            Assert.IsTrue(village.Asking, "[방 닫기]는 먼저 묻는다");
            Assert.IsNotNull(stage.Root.Q("setup-village-ask-yes"));
            Assert.IsNotNull(flow.Room.Net, "묻는 동안 방은 그대로");
            village.CancelLeave();
            Assert.IsFalse(village.Asking);
            Assert.AreSame(village, stage.Current, "[취소] → 고르기 그대로");
            village.Back();
            village.Leave();
            Assert.IsInstanceOf<TitleScreen>(stage.Current, "타이틀로");
            Assert.IsNull(flow.Room.Net);
            yield return Until(() => guest.Status == NetStatus.Failed, 15, "손님은 E_HOST_CLOSED");
            Assert.AreEqual((NetCode.HostClosed, (RoomPhase?)RoomPhase.InGame), (guest.FailCode, guest.FailPhase));
            yield return Until(() => !oldManager || !oldManager.IsListening, 10, "옛 방 NGO 종료");
            yield return null;

            // 타이틀 「이어하기」 → S04: 방 저장 칸
            var title = (TitleScreen)stage.Current;
            Assert.IsTrue(title.Ready(title.Shown.First(i => i.Id == TitleScreen.Continue)), "방 저장이 「이어하기」를 켠다");
            flow.OpenSaves();
            var list = (SaveListScreen)stage.Current;
            Assert.AreEqual(1, list.Entries.Count, "방 저장 하나(솔로 저장 없음)");
            Assert.AreEqual(roomId, list.Entries[0].SaveId);
            Assert.IsTrue(list.IsRoom(roomId));
            StringAssert.StartsWith("방 2명 · ", list.Lines[0]);
            Assert.IsNotNull(stage.Root.Q("saves-resume-0"), "[이어하기]");
            yield return null;
            var names = stage.Root.Q<Label>("saves-names-0");
            Assert.IsNotNull(names, "방 저장 칸 둘째 줄 = 함께 한 닉네임(결정 112 Q7)");
            StringAssert.Contains("빛나", names.text.Replace("⁠", ""));

            // [이어하기] → S05 「방 다시 열기」 → 같은 주소로 → S10
            Assert.IsTrue(list.Resume(roomId));
            var reopen = stage.Current as RoomCreateScreen;
            Assert.IsNotNull(reopen, "S05");
            Assert.IsTrue(reopen.Reopening, "「방 다시 열기」");
            Assert.IsNotNull(stage.Root.Q("room-create-resume"), "저장한 게임 한 줄");
            Assert.IsNull(stage.Root.Q("room-create-row-seats"), "자리 수 · 캠페인은 저장된 값");
            reopen.SetInternet(false);
            Assert.IsTrue(reopen.Start(), "[방 다시 열기]");
            var wait = stage.Current as LobbyScreen;
            Assert.IsNotNull(wait);
            yield return Until(() => flow.Room.Live?.Room != null && ((LobbyScreen)stage.Current).Waiting && stage.Root.Q("room-wait-resume") != null, 10, "S10 이어하기 대기실 · [지금 재개]");
            Assert.AreEqual("이어하기 대기실", Text(stage.Root, "lobby-head"));
            Assert.AreEqual("기다리는 중", Text(stage.Root, "lobby-seat-1-state"));
            Assert.AreEqual("빛나", Text(stage.Root, "lobby-seat-1-name"));

            // 손님이 [다시 시도] → 같은 주소에 다시 붙어 같은 자리 → 모두 돌아와 저절로 재개 → 방장 화면이 방 판으로
            guest.Retry();
            Assert.AreEqual(NetStatus.Reconnecting, guest.Status);
            yield return Until(() => guest.Status == NetStatus.Connected && flow.InRoomGame && flow.Client != null && flow.Client.View != null, 30, "모두 돌아와 재개");
            Assert.AreEqual(1, guest.Seat, "같은 자리");
            Assert.AreEqual(RoomPhase.InGame, flow.Room.Net.Host.Host.Phase);
            Assert.AreEqual(roomId, flow.Room.Net.Host.Host.SaveId, "같은 저장에 이어 쓴다");
            Assert.AreEqual(0, flow.Client.Seat);
            Assert.AreEqual(Phase.Setup, flow.Client.View.Phase, "셋업의 쉬는 때로");
            yield return Until(() => stage.Current is SetupVillageScreen || stage.Current is SetupPickScreen, 10, "셋업 화면");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        // ── ⑤ 핑 찍기(결정 97) ──

        [UnityTest, NUnit.Framework.Timeout(240000)]
        public IEnumerator M7_핑_방장이찍으면손님화면의그칸에좌석색표시와이름_4초뒤사라짐_손님은Alt클릭으로찍고5초에3번까지_혼자하기는없음()
        {
            yield return Boot();
            const ushort port = 47826;
            boot.PointerFromMouse = false;   // 마우스 자리 · 단추는 이 시험이 흐름에 직접 넣는다(누른 채 프레임을 넘기는 종류 고리)
            var flow = boot.Flow;
            var stage = flow.Stage;
            var vc = new VClock();
            var host = OpenHost(flow, port, 2, vc);
            var hostBot = new Bot(host.Local, host.Send, flow.Ctx);
            var hostGot = new List<SocialMsg>();
            host.Local.Received += m => { if (m is SocialMsg p && p.Kind == SocialKind.Ping) hostGot.Add(p); };
            Assert.IsFalse(flow.Room.Ping(3), "방 판이 아니면(타이틀 · 혼자 하기) 핑이 없다");
            yield return ToBoard(flow, host, vc, hostBot, port);
            var live = flow.Room.Live;
            Assert.IsEmpty(live.Pings);

            // 화면 안에 있고 HUD에 가리지 않은 칸(마우스를 올리면 그 칸이 잡힌다) — 카메라가 말을 따라 움직이므로 쓸 때마다 다시 찾는다
            float scale = stage.Grade.PanelScale;
            int height = boot.Root.Size.y;
            bool OpenCell(int skip, out int found, out Vector2 at)
            {
                for (int cell = 0; cell < flow.Ctx.Board.Count; cell++)
                {
                    if (cell == skip || !flow.CellPoint(cell, out var panel)) continue;
                    at = new Vector2(panel.x * scale, height - panel.y * scale);
                    if (at.x < 160 || at.x > boot.Root.Size.x - 160 || at.y < 160 || at.y > height - 160) continue;
                    if (flow.Pointer(at.x, at.y, false) != cell) continue;
                    found = cell;
                    return true;
                }
                found = -1;
                at = default;
                return false;
            }
            var open = new int[2];
            Assert.IsTrue(OpenCell(-1, out open[0], out _), "화면 안의 빈 칸");

            // ① 방장(좌석 0)이 찍는다 → 손님 화면의 그 칸에 표시 + 「방장 · 여기 봐」
            host.Send(new SendSocial { Kind = SocialKind.Ping, Code = 0, Target = PingTarget.Cell(open[0]) });
            yield return Until(() => live.Pings.Count == 1, 10, "손님이 핑을 받는다");
            Assert.AreEqual((0, open[0]), (live.Pings[0].Seat, live.Pings[0].Cell));
            yield return null;
            yield return null;
            var mark = stage.Root.Q("room-ping-0");
            Assert.IsNotNull(mark, "핑 표시");
            Assert.AreEqual(1, flow.RoomLayer.PingsShown);
            Assert.IsTrue(flow.CellPoint(open[0], out var want));
            Assert.That(Vector2.Distance(flow.RoomLayer.PingPoint(0), want), Is.LessThan(40f), "그 칸 위(카메라가 말을 따라 움직이는 중이면 한 프레임만큼 어긋난다)");
            Assert.AreEqual("방장 · " + stage.T("screen.room.ping.kind.0"), Text(stage.Root, "room-ping-0-name"));
            Assert.AreEqual(1, hostGot.Count, "찍은 방장도 받는다");

            // ② 손님(좌석 1): Alt + 클릭 = 「여기 봐」 핑(칸 고르기 · 걷기는 하지 않는다) — 누른 자리가 실린다(그 칸 + 칸 안 위치)
            int picked = flow.Hud.PickedCell, commands = host.Host.Game.CommandCount;
            Assert.IsTrue(OpenCell(open[0], out open[1], out var click), "화면 안의 다른 빈 칸");
            flow.PingKey = true;
            Assert.AreEqual(-1, flow.Pointer(click.x, click.y, true), "Alt를 누른 채 누른 것은 핑의 것이다(칸으로 새지 않는다)");
            flow.PingKey = false;
            yield return Until(() => hostGot.Count == 2, 10, "방장이 손님의 핑을 받는다");
            Assert.AreEqual((1, 0, PingAt.Cell, open[1], true), (hostGot[1].From, hostGot[1].Code, hostGot[1].Target.At, hostGot[1].Target.N, hostGot[1].Target.Pos));
            Assert.AreEqual((picked, commands), (flow.Hud.PickedCell, host.Host.Game.CommandCount), "핑은 칸을 고르지도 명령을 내지도 않는다");
            yield return Until(() => live.Pings.Count == 2, 10, "내 핑도 내 화면에");
            yield return null;
            Assert.AreEqual(2, flow.RoomLayer.PingsShown);
            Assert.AreEqual("손님트레이너 · " + stage.T("screen.room.ping.kind.0"), Text(stage.Root, "room-ping-1-name"));

            // ②-2 종류 고리: Alt를 누른 채 누르고 오른쪽으로 끌면 고리가 뜨고(오른쪽 = ?), 떼면 그 종류로 처음 누른 자리에 찍는다. 그동안 보드는 끌리지 않는다
            Assert.IsTrue(OpenCell(-1, out int wheelCell, out var down), "고리를 띄울 칸");
            flow.PingKey = true;
            flow.Pointer(down.x, down.y, true, false);
            Assert.IsFalse(flow.RoomLayer.WheelUp, "막 누른 때");
            flow.Pointer(down.x + 60 * scale, down.y, true, false);
            Assert.IsTrue(flow.RoomLayer.WheelUp, "끌면 종류 고리");
            Assert.AreEqual(1, flow.RoomLayer.WheelSector, "오른쪽 = ?");
            Assert.IsFalse(flow.Dragging, "보드는 끌리지 않는다");
            yield return null;
            Assert.AreNotEqual(DisplayStyle.None, stage.Root.Q("room-ping-wheel").resolvedStyle.display, "고리가 보인다");
            flow.Pointer(down.x + 60 * scale, down.y, false, false);
            flow.PingKey = false;
            Assert.IsFalse(flow.RoomLayer.WheelUp);
            yield return Until(() => hostGot.Count == 3, 10, "방장이 「?」 핑을 받는다");
            Assert.AreEqual((1, 1, wheelCell), (hostGot[2].From, hostGot[2].Code, hostGot[2].Target.N), "처음 누른 칸에 ?");
            // 가운데에서 떼면 취소
            Assert.AreEqual(-1, RoomLayer.SectorOf(new Vector2(3f, -4f)));
            Assert.AreEqual((0, 2, 3), (RoomLayer.SectorOf(new Vector2(5f, -40f)), RoomLayer.SectorOf(new Vector2(-5f, 40f)), RoomLayer.SectorOf(new Vector2(-40f, 5f))), "위 여기 봐 · 아래 좋아 · 왼쪽 조심");

            // ③ 빈도 제한: 5초에 3번 — 넷째는 보내지 않는다(방도 버린다)
            Assert.IsTrue(flow.Room.Ping(open[0]));
            Assert.IsFalse(flow.Room.Ping(open[1]), "넷째");
            host.Send(new SendSocial { Kind = SocialKind.Ping, Code = 2, Target = PingTarget.Cell(open[1]) });
            yield return Until(() => hostGot.Count == 5, 10, "손님 셋 + 방장 둘");
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(3, hostGot.FindAll(p => p.From == 1).Count);

            // ④ 4초 뒤 사라진다
            yield return Until(() => flow.RoomLayer.PingsShown == 0 && stage.Root.Q("room-ping-0") == null, 10, "핑이 사라진다");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        // ── ⑥ 소통(결정 102): 보드 밖 대상의 핑 · 감정 표현 · 도장 ──

        [UnityTest, NUnit.Framework.Timeout(240000)]
        public IEnumerator M7_소통_방장이보드밖대상에핑하면손님화면의같은요소에표시_감정표현말풍선과메뉴_기록줄도장_상대별끄기_혼자하기는없음()
        {
            yield return Boot();
            const ushort port = 47827;
            boot.PointerFromMouse = false;
            var flow = boot.Flow;
            var stage = flow.Stage;
            var vc = new VClock();
            var host = OpenHost(flow, port, 2, vc);
            var hostBot = new Bot(host.Local, host.Send, flow.Ctx);
            var hostGot = new List<SocialMsg>();
            host.Local.Received += m => { if (m is SocialMsg p) hostGot.Add(p); };
            Assert.IsFalse(flow.Room.Emote(0), "방 판이 아니면 감정 표현이 없다");
            Assert.IsFalse(flow.Room.Mark(0, 0, 0), "방 판이 아니면 도장이 없다");
            Assert.IsTrue(flow.RoomLayer == null || !flow.RoomLayer.ToggleEmotes(), "방 판이 아니면 메뉴가 열리지 않는다");
            Assert.IsTrue(flow.RoomLayer == null || !flow.RoomLayer.ChipMenu(0), "방 판이 아니면 칩 오른쪽 클릭 메뉴도 없다");
            yield return ToBoard(flow, host, vc, hostBot, port);
            var live = flow.Room.Live;
            var layer = flow.RoomLayer;
            Assert.IsNull(stage.Root.Q("room-emote"), "가운데 줄의 [감정 표현] 단추는 뺐다(결정 112 Q12)");
            Rect Box(string name)
            {
                var e = stage.Root.Q(name);
                Assert.IsNotNull(e, name);
                return e.worldBound;
            }

            // ① 방장이 손님 좌석 칩의 오른쪽 아래에 「조심」 핑 → 손님 화면의 그 칩 안 같은 비율 지점
            host.Send(new SendSocial { Kind = SocialKind.Ping, Code = 3, Target = PingTarget.OfSeat(1).At2(750, 750) });
            yield return Until(() => live.Pings.Count == 1 && layer.PingsShown == 1, 10, "좌석 칩 핑");
            var chip = Box("board-hud-seat-2");
            var at = layer.PingPoint(0);
            Assert.That(at.x, Is.EqualTo(chip.xMin + 0.75f * chip.width).Within(1.5f));
            Assert.That(at.y, Is.EqualTo(chip.yMin + 0.75f * chip.height).Within(1.5f));
            Assert.AreEqual("방장 · " + stage.T("screen.room.ping.kind.3"), Text(stage.Root, "room-ping-0-name"));
            Assert.AreEqual("!", Text(stage.Root, "room-ping-0-glyph"));

            // ② 모두가 보는 위 띠의 라운드 배지 → 같은 요소 / 방장의 「내 파티 칸」(내 화면에는 그 좌석의 아래 판이 없다) → 방장의 좌석 칩
            int round = PingSpots.Find("board-hud-round", out _), mon = PingSpots.Find("board-hud-mon-0", out int monK);
            vc.Now += 6000;
            host.Send(new SendSocial { Kind = SocialKind.Ping, Code = 1, Target = PingTarget.Spot(round) });
            host.Send(new SendSocial { Kind = SocialKind.Ping, Code = 2, Target = PingTarget.Spot(mon, monK, 1).At2(100, 100) });
            yield return Until(() => live.Pings.Count == 3 && layer.PingsShown == 3, 10, "보드 밖 핑 둘");
            Assert.That(Vector2.Distance(layer.PingPoint(1), Box("board-hud-round").center), Is.LessThan(1.5f), "라운드 배지 가운데");
            Assert.AreEqual(0, live.Pings[2].Target.Seat, "「내 것」의 주인은 보낸 좌석(방장이 새로 적는다)");
            Assert.That(Vector2.Distance(layer.PingPoint(2), Box("board-hud-seat-1").center), Is.LessThan(1.5f), "남의 아래 판은 그 좌석의 칩으로");

            // ③ 손님이 자기 화면에서 대상을 찾는다: 내 파티 칸 = 허용 목록의 줄(주인 = 나) · 좌석 칩 = 좌석 · 메뉴 단추 같은 목록 밖 요소 = 없음
            var mine = layer.TargetAt(Box("board-hud-mon-0").center);
            Assert.IsNotNull(mine, "내 파티 칸");
            Assert.AreEqual((PingAt.Spot, mon, 0, 1, true), (mine.At, mine.N, mine.K, mine.Seat, mine.Pos));
            Assert.That(mine.X, Is.InRange(400, 600));
            var seat = layer.TargetAt(Box("board-hud-seat-1").center);
            Assert.AreEqual((PingAt.Seat, 0), (seat.At, seat.Seat), "방장의 좌석 칩");
            Assert.IsNull(layer.TargetAt(Box("board-hud-menu").center), "[메뉴] 단추는 공개 대상이 아니다");
            int pings = hostGot.FindAll(p => p.Kind == SocialKind.Ping).Count;
            Assert.IsTrue(layer.PingHere(Box("board-hud-mon-0").center, 2), "내 파티 칸에 「좋아」");
            yield return Until(() => hostGot.FindAll(p => p.Kind == SocialKind.Ping).Count == pings + 1, 10, "방장이 손님의 보드 밖 핑을 받는다");
            var got = hostGot.FindLast(p => p.Kind == SocialKind.Ping);
            Assert.AreEqual((1, 2, PingAt.Spot, mon, 0, 1), (got.From, got.Code, got.Target.At, got.Target.N, got.Target.K, got.Target.Seat));

            // ④ 감정 표현: 방장이 보내면 방장 좌석 칩 아래 말풍선(문구 그대로) → 3.2초 뒤 사라진다
            host.Send(new SendSocial { Kind = SocialKind.Emote, Code = 5 });
            yield return Until(() => layer.EmoteShown(0) != null, 10, "방장의 말풍선");
            Assert.AreEqual("아르세우스가 당신을 태울 것입니다!", layer.EmoteShown(0)?.Replace("⁠", ""));
            Assert.IsNotNull(stage.Root.Q("room-emote-0"));
            yield return null;
            var bubble = Box("room-emote-0");
            var hostChip = Box("board-hud-seat-1");
            Assert.That(bubble.yMin, Is.GreaterThanOrEqualTo(hostChip.yMax), "칩 아래");
            Assert.That(bubble.xMin, Is.EqualTo(hostChip.xMin).Within(2f), "얼굴 쪽에 맞춘다");
            // 손님: 내 좌석 칩 오른쪽 클릭(결정 112 Q12 — 남의 칩은 열지 않는다) → 내 칩 아래 메뉴(일곱 줄 + 상대별 끄기) → 한 줄 → 방장이 받고 메뉴가 닫힌다 · 2초 안의 둘째는 보내지 않는다
            Assert.IsFalse(layer.ChipMenu(0), "남(방장)의 칩");
            Assert.IsFalse(layer.EmotesOpen);
            Assert.IsTrue(layer.ChipMenu(1), "내 칩");
            yield return null;
            yield return null;
            var menuBox = Box("room-emote-menu");
            var myChip = Box("board-hud-seat-2");
            Assert.That(menuBox.yMin, Is.GreaterThanOrEqualTo(myChip.yMax), "메뉴는 내 칩 아래");
            Assert.That(menuBox.xMin, Is.EqualTo(myChip.xMin).Within(2f), "내 칩 왼쪽에 맞춘다");
            for (int i = 0; i < SocialCodes.Emote; i++) Assert.IsNotNull(stage.Root.Q("room-emote-line-" + i), "문구 " + i);
            Assert.AreEqual("제 포켓몬이 당신을 조각낼 거예요", stage.Root.Q<Button>("room-emote-line-6").text.Replace("\u2060", ""));
            Assert.IsNotNull(stage.Root.Q("room-emote-mute-0"), "방장 반응 끄기");
            Assert.IsNotNull(stage.Root.Q("room-ping-mute-0"), "방장 핑 끄기");
            Assert.IsNull(stage.Root.Q("room-emote-mute-1"), "내 줄은 없다");
            Assert.IsTrue(layer.PressEmote(0));
            Assert.IsFalse(layer.EmotesOpen, "보내면 닫힌다");
            Assert.IsFalse(layer.PressEmote(1), "2초에 1번");
            yield return Until(() => hostGot.Exists(p => p.Kind == SocialKind.Emote && p.From == 1), 10, "방장이 손님의 감정 표현을 받는다");
            yield return Until(() => layer.EmoteShown(1) != null, 10, "내 말풍선도 내 화면에");
            Assert.AreEqual("반갑습니다!", layer.EmoteShown(1)?.Replace("⁠", ""));
            yield return Until(() => layer.EmoteShown(0) == null, 10, "방장의 말풍선이 사라진다");

            // 일시정지(결정 112 Q6 — 방 설정 기본 「방장만」): 손님 메뉴의 [일시정지]는 꺼져 있다 · 방장이 걸면 띠 「건 사람: 방장 · n:nn 지남」, 손님에게 풀기 단추는 없다
            Assert.AreEqual(PauseMode.HostOnly, live.PauseMode);
            flow.OpenMenu();
            var menu = (BoardMenuScreen)stage.Current;
            yield return null;
            Assert.IsFalse(stage.Root.Q<Button>("board-menu-pause").enabledSelf, "손님은 걸 수 없다");
            Assert.IsFalse(menu.TogglePause());
            flow.CloseMenu();
            host.Send(new SetPaused { On = true });
            yield return Until(() => live.Paused, 10, "방장의 일시정지");
            yield return null;
            StringAssert.Contains("건 사람: 방장", layer.PauseText);
            StringAssert.Contains("지남", layer.PauseText);
            Assert.IsNull(stage.Root.Q("room-pause-off"), "손님은 방장의 정지를 풀지 못한다");
            Assert.IsFalse(layer.Unpause());
            host.Send(new SetPaused { On = false });
            yield return Until(() => !live.Paused, 10, "방장이 풀었다");
            yield return null;
            Assert.AreEqual("", layer.PauseText);

            // ⑤ 도장: 기록 창에서 방장의 행동 줄에 「대단해」 → 방장이 받고 그 줄 끝에 내 색 칩. 내 행동 줄에는 메뉴가 뜨지 않는다
            BoardLogLineOf(flow, 0, out int theirs);
            BoardLogLineOf(flow, 1, out int own);
            Assert.GreaterOrEqual(theirs, 0, "방장의 기록 줄");
            Assert.GreaterOrEqual(own, 0, "내 기록 줄");
            flow.OpenLog();
            yield return Until(() => stage.Root.Q("board-log-panel") != null && stage.Root.Q("board-log-panel").Q(RoomLayer.LogRowName(theirs)) != null, 20, "기록 창에 방장의 줄");
            yield return null;
            Assert.IsFalse(layer.OpenMarks(own, 1), "내 행동에는 도장을 찍지 못한다");
            Assert.IsTrue(layer.OpenMarks(theirs, 0), "남의 행동 줄");
            Assert.AreEqual(theirs, layer.MarksOpenFor);
            yield return null;
            Assert.AreNotEqual(DisplayStyle.None, stage.Root.Q("room-mark-menu").resolvedStyle.display, "도장 메뉴");
            Assert.IsTrue(layer.PressMark(0));
            Assert.AreEqual(-1, layer.MarksOpenFor, "찍으면 닫힌다");
            yield return Until(() => hostGot.Exists(p => p.Kind == SocialKind.Mark), 10, "방장이 도장을 받는다");
            var markGot = hostGot.Find(p => p.Kind == SocialKind.Mark);
            Assert.AreEqual((1, 0, theirs, 0), (markGot.From, markGot.Code, markGot.Seq, markGot.Owner));
            yield return Until(() => layer.MarkShown(theirs, 0).Count == 1, 10, "그 줄에 도장 칩");
            Assert.AreEqual(stage.T("screen.room.mark.kind.0"), layer.MarkShown(theirs, 0)[0].Replace("⁠", ""));
            // 한 사람 1개: 다시 찍으면 바뀐다(갈음)
            live.AddMark(1, theirs, 0, 2);
            yield return null;
            yield return null;
            Assert.AreEqual(1, layer.MarkShown(theirs, 0).Count);
            Assert.AreEqual(stage.T("screen.room.mark.kind.2"), layer.MarkShown(theirs, 0)[0].Replace("⁠", ""));
            flow.CloseLog();

            // ⑥ 상대별 끄기: 방장의 반응을 끄면 방장의 감정 표현이 내 화면에 뜨지 않는다 — 핑은 그대로 · 다시 켜면 보인다
            Assert.IsTrue(layer.ToggleMute(0, false));
            Assert.IsTrue(live.Muted(0, false));
            Assert.IsFalse(layer.ToggleMute(1, false), "나는 끌 수 없다");
            vc.Now += 6000;
            int emotes = hostGot.FindAll(p => p.Kind == SocialKind.Emote && p.From == 0).Count, seen = live.EmotesSeen, pingsSeen = live.PingsSeen;
            host.Send(new SendSocial { Kind = SocialKind.Emote, Code = 2 });
            host.Send(new SendSocial { Kind = SocialKind.Ping, Code = 0, Target = PingTarget.OfSeat(0) });
            yield return Until(() => hostGot.FindAll(p => p.Kind == SocialKind.Emote && p.From == 0).Count == emotes + 1 && live.PingsSeen == pingsSeen + 1, 10, "방장이 보냈다");
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(seen, live.EmotesSeen, "끈 좌석의 감정 표현은 받지 않는다");
            Assert.IsNull(layer.EmoteShown(0));
            Assert.IsTrue(layer.ToggleMute(0, false));
            vc.Now += 6000;
            host.Send(new SendSocial { Kind = SocialKind.Emote, Code = 4 });
            yield return Until(() => layer.EmoteShown(0) != null, 10, "다시 켜면 보인다");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        // 손님 화면(좌석 1)이 로비 → 셋업 → 보드까지(방장 = 봇)
        IEnumerator ToBoard(GameFlow flow, NetHostSession host, VClock vc, Bot hostBot, ushort port)
        {
            yield return JoinAsGuest(flow, port, "손님트레이너");
            Assert.IsTrue(((LobbyScreen)flow.Stage.Current).ToggleReady());
            yield return Until(() => host.Host.Room.CanStart, 10, "시작 가능");
            host.Send(new StartGame());
            yield return Until(() => flow.InRoomGame && flow.Client != null && flow.Client.View != null, 15, "방 판");
            yield return Drive(flow, host, vc, new[] { hostBot }, () => flow.OnBoard && flow.Hud != null, 120, "손님 화면 셋업 → 보드", answerSetup: true);
            for (int i = 0; i < 5; i++) yield return null;
        }

        // 그 좌석이 주인인 기록 줄의 행동 번호(없으면 −1)
        static void BoardLogLineOf(GameFlow flow, int seat, out int seq)
        {
            seq = -1;
            foreach (var line in flow.Log.Lines)
                if (line.Kind == Sinnoh.Fx.BoardLogKind.Line && line.Seat == seat && line.Seq >= 0) seq = line.Seq;
        }

        // ── 차림 ──

        static Hello Hi(GameFlow flow, string token, string nick) =>
            new Hello { BuildId = Application.version, ContentHash = flow.ContentHash ?? "", Token = token, Nickname = nick, Platform = Platform.Pc };

        // ── ⑦ 남의 고르기 보기(M9 다듬기 P09 — 결정 112 Q16, 02 §4-10) ──

        [UnityTest, NUnit.Framework.Timeout(240000)]
        public IEnumerator M9_남의고르기_방장이은행상회줄을가리키면손님화면에좌석색칸_누르면바탕_떠나면사라짐_열리지않은자리와남의턴은오지않는다()
        {
            yield return Boot();
            const ushort port = 47828;
            var flow = boot.Flow;
            var stage = flow.Stage;
            var vc = new VClock();
            var host = OpenHost(flow, port, 2, vc);
            var hostBot = new Bot(host.Local, host.Send, flow.Ctx);
            var hostGot = new List<PeekMsg>();
            host.Local.Received += m => { if (m is PeekMsg p && p.Spot != PeekSpot.Village) hostGot.Add(p); };   // 셋업의 고향 마을 가리킴(손님 화면이 보낸다 — 결정 120 · Protocol 10)은 뺀다
            Assert.IsFalse(flow.Room.Peek(PeekSpot.Bank, 0), "방 판이 아니면(타이틀 · 혼자 하기) 보내지 않는다");
            yield return ToBoard(flow, host, vc, hostBot, port);
            var live = flow.Room.Live;
            var layer = flow.RoomLayer;
            // 방장(좌석 0)의 턴으로: 내 턴이면 [턴 종료] — 방장 봇은 더 돌리지 않는다(맵 행동이 열린 채)
            if (flow.Client.View.ActiveSeat == flow.Client.Seat)
            {
                yield return Drive(flow, host, vc, new Bot[0], () => flow.InputOpen && flow.Hud != null && flow.Hud.Shows(BoardHud.EndTurn), 60, "내 턴의 [턴 종료]", answerSetup: false);
                Assert.IsTrue(flow.Hud.Press(BoardHud.EndTurn), "[턴 종료]");
            }
            yield return Drive(flow, host, vc, new Bot[0], () => PeekRules.Open(flow.Client.View, 0, PeekSpot.Bank) != null && PeekRules.Open(host.Local.View, 0, PeekSpot.Bank) != null && flow.Fx.Sequencer.IsEmpty,
                60, "방장 턴의 맵 행동(손님 화면의 연출이 끝난 뒤)", answerSetup: false);
            Assert.AreEqual(-1, layer.PeekSeat, "소식이 오기 전에는 은행상회 줄이 없다");
            Assert.GreaterOrEqual(BankRules.Card(flow.Client.View), 0, "은행상회 덱 맨 위 카드(공개)");

            // ① 방장이 아래 줄을 가리킨다 → 손님 화면 위 가운데 줄에 두 줄 + 아래 줄의 좌석 색 테두리. 방장에게는 되돌아오지 않는다
            vc.Now += PeekRate.WindowMs;
            host.Send(new SendPeek { Spot = PeekSpot.Bank, Index = BankRules.Bottom });
            yield return Until(() => layer.PeekSeat == 0, 10, "손님 화면의 은행상회 줄");
            Assert.AreEqual((PeekSpot.Bank, 2, BankRules.Bottom, false), (layer.PeekSpot, layer.PeekSlots, layer.PeekIndex, layer.PeekPress));
            Assert.IsTrue(layer.PeekMarked(2) && !layer.PeekMarked(1) && !layer.PeekMarked(2, true), "아래 줄만 테두리");
            yield return null;
            var tile = stage.Root.Q("room-peek-opt-2");
            Color32 tone = flow.SeatColor(0), edge = tile.resolvedStyle.borderTopColor;
            Assert.AreEqual((tone.r, tone.g, tone.b), (edge.r, edge.g, edge.b), "테두리 = 방장 좌석 색");
            StringAssert.Contains("방장", stage.Root.Q<Label>("room-peek-text").text, "줄 머리 = 방 닉네임");
            Assert.IsEmpty(hostGot, "보낸 좌석에게는 넘기지 않는다");

            // ② [두 줄 모두]를 눌렀다 → 두 줄 다 좌석 색 바탕
            vc.Now += PeekRate.WindowMs;
            host.Send(new SendPeek { Spot = PeekSpot.Bank, Index = BankRules.Both, Press = true });
            yield return Until(() => layer.PeekPress, 10, "누름");
            Assert.IsTrue(layer.PeekMarked(1, true) && layer.PeekMarked(2, true));

            // ③ 열려 있지 않은 자리(조우 · 사당) · 범위 밖 · 남의 턴인 좌석(나)이 보낸 것은 오지 않는다
            int seen = live.PeeksSeen;
            vc.Now += PeekRate.WindowMs;
            host.Send(new SendPeek { Spot = PeekSpot.Encounter, Index = 0 });
            host.Send(new SendPeek { Spot = PeekSpot.Shrine, Index = 0, Press = true });
            host.Send(new SendPeek { Spot = PeekSpot.Bank, Index = PeekRules.BankRows });
            live.Send(new SendPeek { Spot = PeekSpot.Bank, Index = 0 });
            for (int i = 0; i < 40; i++) yield return null;
            Assert.AreEqual(seen, live.PeeksSeen, "손님이 받은 소식 수");
            Assert.IsEmpty(hostGot, "내 턴이 아닌 좌석의 것은 방장에게도 가지 않는다");

            // ④ 떠남(−1) → 줄이 사라진다
            vc.Now += PeekRate.WindowMs;
            host.Send(new SendPeek { Spot = PeekSpot.Bank, Index = -1 });
            yield return Until(() => layer.PeekSeat == -1, 10, "줄이 사라짐");
            Assert.AreEqual(DisplayStyle.None, stage.Root.Q("room-peek").resolvedStyle.display);
            Assert.IsEmpty(hostBot.Bad, "합법 밖 거절 없음");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        NetHostSession OpenHost(GameFlow flow, ushort port, int seats, VClock vc)
        {
            var m = NetBootstrap.Create("Host");
            var r = m.gameObject.AddComponent<NetRunner>();
            runners.Add(r);
            r.Clock = () => vc.Now;
            var cfg = new HostConfig
            {
                OwnerToken = "own", OwnerNickname = "방장", RoomId = "room-game-" + port, BuildId = Application.version, ContentHash = flow.ContentHash ?? "", Seed = 20261118, AiSeed = SoloTrail.AiSeed,
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

        // 화면 흐름이 S06 참가 칸 「127.0.0.1:포트」로 붙어 로비까지
        static IEnumerator JoinAsGuest(GameFlow flow, ushort port, string nick)
        {
            flow.Room.OpenJoin();
            var join = (RoomJoinScreen)flow.Stage.Current;
            join.SetNickname(nick);
            join.SetTarget(Loopback + ":" + port);
            Assert.IsTrue(join.Join(), "[참가]");
            yield return Until(() => flow.Stage.Current is LobbyScreen && flow.Room.Net != null && flow.Room.Net.Seat.Status == NetStatus.Connected, 15, "손님 화면 로비");
        }

        static string Text(VisualElement root, string name) => (root.Q(name) is Label l ? l.text : root.Q(name)?.Q<Label>()?.text)?.Replace("⁠", "");

        static IEnumerable<string> Notices(VisualElement root)
        {
            for (int i = 0; i < 8; i++)
            {
                var t = Text(root, "room-notice-" + i + "-text");
                if (t != null) yield return t;
            }
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
        /// 봇이 답하고(answerSetup이면 화면 흐름도 셋업 화면으로 답한다) 방의 가짜 시각을 연출 끝 · 차단 끝까지 옮긴다 — 세 프레임 동안 아무도 답하지 않으면 다음 정해진 일까지. 흐름의 화면 시계도 200ms씩.
        /// 내 선택이 열렸는데 내 화면이 아직 앞 연출(남 좌석의 고향 마을 연출 등)을 보이는 중이면 다음 정해진 일로 건너뛰지 않는다 — 그 일이 내 응답 시계(느긋함 90초)의 시간 초과라서 화면이 서기 전에 내 선택이 기본값으로 넘어간다.
        /// </summary>
        static IEnumerator Drive(GameFlow flow, NetHostSession host, VClock vc, IEnumerable<Bot> bots, Func<bool> done, float seconds, string what, bool answerSetup)
        {
            float end = Time.realtimeSinceStartup + seconds;
            int idle = 0, last = -1;
            var stage = flow.Stage;
            while (!done())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail($"시간 초과: {what}(명령 {host.Host.Game?.CommandCount})");
                bool acted = false;
                foreach (var b in bots) acted |= b.Step();
                if (answerSetup && flow.Client != null && flow.Client.FirstChoice() != null && host.Host.HoldLeftMs(flow.Client.Seat) == 0)
                {
                    if (stage.Current is SetupVillageScreen village && flow.Client.Choice(ChoiceKind.VillagePick) != null)
                    {
                        village.Pick(0);
                        acted |= village.Confirm();
                    }
                    else if (stage.Current is SetupPickScreen pick && !pick.StarterSent)
                    {
                        pick.PickStarter(0);
                        pick.PickCard(0);
                        acted |= pick.Confirm();
                    }
                }
                if (!host.Host.AwaitingAi)
                {
                    long hold = 0;
                    for (int s = 0; s < host.Host.SeatCount; s++) hold = Math.Max(hold, host.Host.HoldLeftMs(s));
                    vc.Now = Math.Max(vc.Now + 50 + hold, host.Host.AnimEndMs);
                    int n = host.Host.Game?.CommandCount ?? 0;
                    idle = acted || n != last ? 0 : idle + 1;
                    last = n;
                    bool showing = flow.Client != null && flow.Client.FirstChoice() != null && (!flow.SetupOpen || stage.Current is SetupDropScreen);
                    if (idle >= 3 && !showing)
                    {
                        long t = host.Host.NextEventMs;
                        if (t != long.MaxValue && t > vc.Now) vc.Now = t;
                        idle = 0;
                    }
                }
                flow.Skip(200);
                yield return null;
            }
        }

        /// <summary>한 좌석의 봇(NetLoopTests와 같은 꼴): 받은 보기에서 내 첫 선택에 한 번 답한다(셋업은 첫 합법 답, 그 밖은 Default). E_HOLD 거절은 다시.</summary>
        sealed class Bot
        {
            public readonly NetClientSession Session;
            readonly Action<RoomMsg> send;
            readonly Ctx ctx;
            readonly HashSet<int> sent = new HashSet<int>();
            readonly HashSet<int> fallback = new HashSet<int>();
            public readonly List<string> Bad = new List<string>();

            public Bot(NetClientSession session, Action<RoomMsg> send, Ctx ctx)
            {
                Session = session;
                this.send = send;
                this.ctx = ctx;
                session.Received += m =>
                {
                    if (!(m is RejectedMsg r)) return;
                    sent.Remove(r.ChoiceId);
                    if (r.Reason == RejectReason.Hold) return;
                    fallback.Add(r.ChoiceId);
                    if (r.Reason == RejectReason.Illegal || r.Reason == RejectReason.BadCommand || r.Reason == RejectReason.NotYourChoice) Bad.Add($"좌석 {session.Seat} 선택 {r.ChoiceId}: {r.Reason}");
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
                ChoiceAnswer a = !fallback.Contains(c.Id) && v.Phase == Phase.Setup && legal.Count > 0 ? legal[0] : c.Default.Clone();
                sent.Add(c.Id);
                send(new Submit { Command = Command.AnswerTo(c.Id, a) });
                return true;
            }
        }
    }
}
