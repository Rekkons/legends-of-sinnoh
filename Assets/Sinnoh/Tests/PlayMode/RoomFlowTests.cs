using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Net;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// plan M7 「화면 ① 방 만들기 · 참가 · 로비 · 방 설정」(04 S05~S08 · 02 §2-1 · §2-5 · §3-1, 결정 92·95): 보드 씬의 화면 흐름으로 —
    /// ① 타이틀 → 방 만들기(직접 연결 127.0.0.1) → 다른 NetworkManager의 손님이 「IP:포트」로 참가 → 로비 준비 → [게임 시작] → 셋업을 화면으로 지나 보드(에러 로그 0) ·
    /// ② [인터넷] 방의 Relay가 실패(가짜)하면 「Unity 서버를 쓸 수 없습니다」 + [직접 연결로] → 직접 연결 방 로비 ·
    /// ③ 참가하기의 방 목록 줄(가짜 서비스 — 회색 줄 · [새로 고침] 빈도 막기) · 틀린 주소 · 줄을 누르면 그 코드로 Relay 참가(가짜 실패 → 「방을 찾을 수 없습니다」).
    /// 실제 UGS를 부르지 않는다(방 목록 · Relay 할당 · Relay 참가 모두 가짜). 방장은 127.0.0.1에만 연다(방화벽 창을 띄우지 않으려고). Gen 겉모양이 있어야 돈다.
    /// </summary>
    public sealed class RoomFlowTests
    {
        const string Loopback = "127.0.0.1";

        sealed class VClock
        {
            public long Now;
        }

        /// <summary>UGS Lobby 대신: 조회는 그 자리에서 끝난다(만들기·갱신은 실패 — 공개 방 올리기는 이 시험이 보지 않는다).</summary>
        sealed class FakeBoard : ILobbyBoard
        {
            public readonly List<LobbyRow> Rows = new List<LobbyRow>();
            public int Queries;
            public void Create(string name, int maxPlayers, IDictionary<string, string> data, Action<string> ok, Action<string> fail) => fail(OnlineFail.ListFail);
            public void Update(string lobbyId, IDictionary<string, string> data, int? maxPlayers, Action ok, Action<string, bool> fail) => fail(OnlineFail.ListFail, true);
            public void Heartbeat(string lobbyId, Action ok, Action<string, bool> fail) => fail(OnlineFail.ListFail, true);
            public void Delete(string lobbyId, Action done) => done();

            public void Query(Action<IReadOnlyList<LobbyRow>> ok, Action<string> fail)
            {
                Queries++;
                ok(Rows);
            }
        }

        /// <summary>Relay 할당이 늘 실패한다(Unity 서버를 쓸 수 없음 — 결정 92).</summary>
        sealed class DownRelay : IRelayHost
        {
            public int Calls;

            public void Allocate(int maxConnections, Action<string> ok, Action<string> fail)
            {
                Calls++;
                fail(OnlineFail.RelayAuth);
            }
        }

        readonly List<string> errors = new List<string>();
        readonly List<NetRoom> guests = new List<NetRoom>();
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
            if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();   // 방 닫기
            foreach (var g in guests) g.Dispose();
            guests.Clear();
            float end = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < end && UnityEngine.Object.FindObjectsByType<NetRunner>(FindObjectsSortMode.None).Length > 0) yield return null;
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<NetRunner>(FindObjectsSortMode.None).Length, "방이 남았다(NetRoom.Dispose → NGO가 멈추면 없앤다)");
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

        static IEnumerator Until(Func<bool> done, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("시간 초과: " + what);
                yield return null;
            }
        }

        static int Members(RoomState room) => room == null ? 0 : room.Seats.Count(s => s != null);

        // 화면 글(감김 글은 낱말 사이에 U+2060이 끼어 있다 — Kit.Wrapped)
        static string Text(VisualElement root, string name) => (root.Q(name) is Label l ? l.text : root.Q(name)?.Q<Label>()?.text)?.Replace("⁠", "");

        [UnityTest, NUnit.Framework.Timeout(180000)]
        public IEnumerator M7_화면_타이틀에서방만들기_직접연결127001에손님이IP포트로참가해준비_게임시작하면셋업을지나보드까지_에러로그0()
        {
            yield return Boot();
            const ushort port = 47811;
            var flow = boot.Flow;
            var stage = flow.Stage;
            var vc = new VClock();
            var board = new FakeBoard();
            flow.Room.Listen = Loopback;
            flow.Room.Port = port;
            flow.Room.Board = board;
            flow.Room.Clock = () => vc.Now;

            // 타이틀 「방 만들기」 → S05
            flow.OpenTitle();
            var title = (TitleScreen)stage.Current;
            int host = title.Shown.ToList().FindIndex(i => i.Id == TitleScreen.Host);
            Assert.IsTrue(title.Ready(title.Shown[host]), "「방 만들기」 줄이 켜졌다");
            Assert.IsTrue(title.Activate(host));
            var create = stage.Current as RoomCreateScreen;
            Assert.IsNotNull(create, "S05 방 만들기");
            Assert.IsTrue(flow.Room.Create.Internet, "기본은 [인터넷](추천)");
            Assert.IsNotNull(stage.Root.Q("room-create-opt-mode-direct"), "[직접 연결]은 늘 보인다(결정 92)");
            create.SetInternet(false);
            create.SetSeats(4);
            create.SetNickname("방장트레이너");
            Assert.IsFalse(flow.Room.Create.TrainerCards, "새 방의 트레이너 카드 기본값 = 끔(결정 134)");
            flow.Room.Create.TrainerCards = true;   // 이 판은 트레이너 카드를 쓴다(셋업에서 카드 고르기까지)
            Assert.IsTrue(stage.Root.Q("room-create-opt-list-public").ClassListContains("sn-off"), "직접 연결 방은 목록 줄이 흐리다");
            Assert.IsTrue(create.Start(), "[방 만들기]");
            Assert.AreEqual(Sinnoh.Rules.Session.GalacticAiLevel.Strong, flow.Room.Net.Room.Settings.GalacticAi, "새 방의 갤럭시단 AI 기본값 = 「강함」(결정 118 ②)");
            var lobby = stage.Current as LobbyScreen;
            Assert.IsNotNull(lobby, "직접 연결은 그 자리에서 열고 로비로");
            Assert.AreEqual(0, board.Queries, "직접 연결은 방 목록을 쓰지 않는다");
            var net = flow.Room.Net;
            Assert.IsTrue(net.IsHost);
            Assert.AreEqual(port, net.Port);
            yield return null;
            Assert.AreEqual("방장트레이너", Text(stage.Root, "lobby-seat-0-name"));
            Assert.IsNotNull(stage.Root.Q("lobby-seat-0-host"), "♛ 방장");
            Assert.IsNotNull(stage.Root.Q("lobby-port"), "직접 연결 포트");
            Assert.AreEqual("빈 자리", Text(stage.Root, "lobby-seat-1-name"));
            Assert.AreEqual("닫힌 자리", Text(stage.Root, "lobby-seat-4-name"), "자리 수 4 밖은 닫힌 자리");
            Assert.IsFalse(stage.Root.Q<Button>("lobby-start").enabledSelf, "손님이 없으면 [게임 시작] 꺼짐");

            // 다른 NetworkManager의 손님이 참가 칸 「127.0.0.1:포트」로
            Assert.IsTrue(JoinTarget.TryParse(Loopback + ":" + port, out var target));
            var guest = NetRoom.Join(target, flow.Room.HelloFor("guest-token", "손님"));
            guests.Add(guest);
            yield return Until(() => guest.Seat.Status == NetStatus.Connected && Text(stage.Root, "lobby-seat-1-name") == "손님", 15, "손님 접속 → 로비 2번 자리");
            Assert.AreEqual(1, guest.Seat.Seat);
            Assert.AreEqual("준비 중", Text(stage.Root, "lobby-seat-1-state"));
            Assert.IsNotNull(stage.Root.Q("lobby-seat-1-kick"), "방장에게 [내보내기]");
            Assert.IsFalse(lobby.Start(), "손님이 준비하지 않으면 시작하지 않는다");

            // 방 설정(S08): 방장이 제한 시간을 「끔」으로 → 손님 방 상태에도
            Assert.IsTrue(lobby.Settings.Change(s => s.Clock = ClockPreset.Off));
            yield return Until(() => guest.Room != null && guest.Room.Settings.Clock == ClockPreset.Off, 10, "방 설정이 손님에게");
            Assert.IsTrue(stage.Root.Q("room-set-opt-clock-off").ClassListContains("sn-opt-picked"));

            guest.Send(new SetReady { Ready = true });
            yield return Until(() => stage.Root.Q<Button>("lobby-start") != null && stage.Root.Q<Button>("lobby-start").enabledSelf, 10, "손님 준비 → [게임 시작] 켜짐");
            Assert.AreEqual("준비 완료", Text(stage.Root, "lobby-seat-1-state"));
            Assert.IsTrue(lobby.Start(), "[게임 시작]");
            yield return Until(() => flow.InRoomGame && flow.Client != null && flow.Client.View != null, 15, "방 판에 들어간다(내 좌석의 첫 보기)");
            Assert.IsNull(flow.Session, "세션은 방(방장 PC의 HostSession)에 있다 — 흐름은 좌석 보기만");
            Assert.AreEqual(0, flow.Client.Seat);
            Assert.AreEqual(2, flow.Client.View.Seats.Length, "빈 자리는 빠졌다");

            // 셋업을 화면으로(방장) · 손님은 첫 합법 답 → 보드
            // 차단 컷신(E_HOLD) 동안은 그 좌석이 답하지 않는다 — 방 시각을 연출 끝 · 차단 끝으로 옮긴다(NetLoopTests.Drive와 같은 꼴). 같은 선택이 30프레임 넘게 남으면 다시 낸다(거절)
            var hosted = net.Host.Host;
            int guestSent = -1, guestWait = 0;
            yield return Until(() =>
            {
                var gv = guest.Seat.View;
                var gc = gv != null ? gv.Pending.FirstOrDefault(c => c.Seat == guest.Seat.Seat && !c.Answered) : null;
                if (gc != null && hosted.HoldLeftMs(guest.Seat.Seat) == 0 && (gc.Id != guestSent || ++guestWait > 30))
                {
                    var legal = new List<ChoiceAnswer>();
                    Engine.Legal(gv, flow.Ctx, gc, legal);
                    if (legal.Count > 0) guest.Send(new Submit { Command = Command.AnswerTo(gc.Id, legal[0]) });
                    guestSent = gc.Id;
                    guestWait = 0;
                }
                if (flow.Client.FirstChoice() != null && hosted.HoldLeftMs(0) == 0)
                {
                    if (stage.Current is SetupVillageScreen village && flow.Client.Choice(ChoiceKind.VillagePick) != null)
                    {
                        village.Pick(0);
                        village.Confirm();
                    }
                    else if (stage.Current is SetupPickScreen pick && !pick.StarterSent)
                    {
                        pick.PickStarter(0);
                        pick.PickCard(0);
                        pick.Confirm();
                    }
                }
                long hold = 0;
                for (int s = 0; s < hosted.SeatCount; s++) hold = Math.Max(hold, hosted.HoldLeftMs(s));
                if (!hosted.AwaitingAi) vc.Now = Math.Max(vc.Now + 50 + hold, hosted.AnimEndMs);
                flow.Skip(200);
                return flow.OnBoard && flow.Hud != null;
            }, 90, "셋업 → 보드");
            Assert.IsTrue(flow.OnBoard, "보드");
            Assert.IsInstanceOf<BoardHud>(flow.Hud);
            Assert.AreEqual(0, flow.NotReadyShown, "받을 화면이 없는 선택이 없다");
            Assert.Greater(hosted.Game.CommandCount, 0, "방이 셋업 명령을 받았다");
            Assert.AreEqual(Phase.Setup == hosted.Game.ViewFor(0).Phase, false, "방 판도 셋업이 끝났다");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        [UnityTest, NUnit.Framework.Timeout(60000)]
        public IEnumerator M7_화면_인터넷방Relay가실패하면_Unity서버를쓸수없습니다와직접연결로_누르면같은값으로직접연결방로비()
        {
            yield return Boot();
            const ushort port = 47812;
            var flow = boot.Flow;
            var stage = flow.Stage;
            var relay = new DownRelay();
            flow.Room.Listen = Loopback;
            flow.Room.Port = port;
            flow.Room.Board = new FakeBoard();
            flow.Room.Relay = relay;
            flow.Room.OpenCreate();
            var create = (RoomCreateScreen)stage.Current;
            create.SetSeats(3);
            Assert.IsTrue(flow.Room.Create.Internet && flow.Room.Create.Public, "기본: [인터넷] · 목록에 올리기");
            Assert.IsFalse(create.Start(), "Relay 할당 실패 — 방을 열지 않는다");
            Assert.AreEqual(1, relay.Calls);
            Assert.IsNull(flow.Room.Net, "실패한 방은 버렸다");
            Assert.IsInstanceOf<RoomCreateScreen>(stage.Current, "S05에 남는다");
            Assert.AreEqual(OnlineFail.RelayAuth, flow.Room.FailCode);
            yield return null;
            Assert.AreEqual("Unity 서버를 쓸 수 없습니다.", Text(stage.Root, "room-create-fail-text"));
            var toDirect = stage.Root.Q<Button>("room-create-to-direct");
            Assert.IsNotNull(toDirect, "[직접 연결로](결정 92)");
            Assert.IsTrue(((RoomCreateScreen)stage.Current).ToDirect());
            Assert.IsInstanceOf<LobbyScreen>(stage.Current, "직접 연결 방 로비");
            Assert.IsFalse(flow.Room.Create.Internet);
            Assert.AreEqual(((int)port, 3), (flow.Room.Net.Port, flow.Room.Net.Room.Settings.Seats), "같은 값(자리 수 3)으로 직접 연결");
            Assert.AreEqual(1, relay.Calls, "직접 연결은 Relay를 부르지 않는다");
            yield return null;
            Assert.IsNull(stage.Root.Q("lobby-code"), "참가 코드 대신");
            Assert.IsNotNull(stage.Root.Q("lobby-port"), "직접 연결 주소·포트");
            flow.Room.Leave();
            Assert.IsInstanceOf<TitleScreen>(stage.Current, "[방 닫기] → 타이틀");
            Assert.IsNull(flow.Room.Net);
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }

        [UnityTest, NUnit.Framework.Timeout(60000)]
        public IEnumerator M7_화면_참가하기_방목록줄을가짜서비스로보이고가득참버전다름은회색_새로고침은5초에한번_틀린주소와코드실패는문구()
        {
            yield return Boot();
            var flow = boot.Flow;
            var stage = flow.Stage;
            var board = new FakeBoard();
            string stamp = flow.Room.Stamp;
            void Row(string id, long created, string host, int members, int seats, string st, string state = ListingData.Lobby) =>
                board.Rows.Add(new LobbyRow
                {
                    Id = id, CreatedMs = created,
                    Data = new ListingData { Host = host, Members = members, Seats = seats, Stamp = st, State = state, JoinCode = "Q7K2M" + id.Substring(id.Length - 1) }.ToDictionary(),
                });
            Row("lb-1", 1, "빛나", 2, 4, stamp);
            Row("lb-2", 2, "가득", 3, 3, stamp);
            Row("lb-3", 3, "다른판", 1, 6, "00000000");
            Row("lb-4", 4, "게임중", 2, 2, stamp, ListingData.Game);
            Row("lb-5", 5, "용식", 1, 6, stamp);
            flow.Room.Board = board;
            var attempts = new List<string>();
            flow.Room.PrepareGuest = c => c.FakeRelayJoin((code, fail) => { attempts.Add(code); fail(OnlineFail.RelayCode); });

            flow.OpenTitle();
            var title = (TitleScreen)stage.Current;
            Assert.IsTrue(title.Activate(title.Shown.ToList().FindIndex(i => i.Id == TitleScreen.Join)), "타이틀 「참가하기」");
            var join = stage.Current as RoomJoinScreen;
            Assert.IsNotNull(join, "S06");
            Assert.AreEqual(1, board.Queries, "화면을 열 때 한 번");
            yield return null;
            CollectionAssert.AreEqual(new[] { "용식", "빛나", "가득", "다른판" }, join.Rows.Select(r => r.Host).ToArray(), "들어갈 수 있는 방(최근 순) → 가득 참 → 버전 다름 · 게임 중인 방은 없다");
            Assert.IsFalse(stage.Root.Q("room-join-row-0").ClassListContains("sn-off"));
            Assert.IsTrue(stage.Root.Q("room-join-row-2").ClassListContains("sn-off"), "가득 찬 방은 회색");
            Assert.IsTrue(stage.Root.Q("room-join-row-3").ClassListContains("sn-off"), "버전 다른 방은 회색");
            Assert.IsFalse(join.PressRefresh(), "[새로 고침]은 5초 안에 다시 받지 않는다");
            Assert.IsFalse(stage.Root.Q<Button>("room-join-refresh").enabledSelf);
            Assert.AreEqual(1, board.Queries);
            Assert.IsFalse(join.PressRow(2), "가득 찬 줄은 누르지 않는다");

            // 틀린 주소 → 「주소 형식이 올바르지 않습니다.」(접속 시도 없음)
            join.SetTarget("100.101.102");
            Assert.IsFalse(join.Join());
            Assert.IsNull(flow.Room.Net);
            yield return null;
            Assert.AreEqual("주소 형식이 올바르지 않습니다.", Text(stage.Root, "room-join-fail-text"));

            // 줄을 누르면 그 방의 코드로 Relay 참가(가짜 — 코드 없음) → 「방을 찾을 수 없습니다.」
            Assert.IsTrue(((RoomJoinScreen)stage.Current).PressRow(0));
            CollectionAssert.AreEqual(new[] { "Q7K2M5" }, attempts, "그 줄의 참가 코드");
            yield return null;
            Assert.IsInstanceOf<RoomJoinScreen>(stage.Current, "S06에 남는다");
            Assert.AreEqual(OnlineFail.RelayCode, flow.Room.FailCode);
            Assert.AreEqual("방을 찾을 수 없습니다.", Text(stage.Root, "room-join-fail-text"));
            Assert.IsNull(stage.Root.Q("room-join-direct-hint"), "코드가 틀린 것은 직접 연결 안내가 아니다");
            Assert.AreEqual((4, 1), (((RoomJoinScreen)stage.Current).Rows.Count, board.Queries), "목록은 그대로(다시 받지 않았다)");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }
    }
}
