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
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// plan M7 「웹 손님」의 관문 밖 확인([Explicit][Category("Online")] — 이름을 짚어 손으로 한 번, 02 §2-2 · §8): 실제 웹 손님판 빌드(<c>Builds/WebGL</c>)와 실제 cloudflared가 있어야 한다.
    /// ① 방장(이 에디터 — 직접 연결 127.0.0.1:7777 + 페이지 서버)이 방을 열어 두고, 밖에서 띄운 헤드리스 브라우저의 웹 손님이 페이지 → 참가 → 준비까지 오면 게임을 시작한다
    ///    (브라우저 쪽 몰기 · 그림은 밖의 스크립트 — plan-log 「M7 웹 손님」). ② cloudflared 임시 터널로 페이지가 실제로 나가는지(<see cref="GuestWeb.OpenPage"/> → https 주소 → <c>/alive</c>).
    /// 포트는 루프백에만 연다. 터널은 시험 끝에 끈다.
    /// </summary>
    public sealed class WebGuestLiveTests
    {
        const string Loopback = "127.0.0.1";

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        static void Report(string line)
        {
            Debug.Log(line);
            TestContext.Out.WriteLine(line);
        }

        [UnityTest, Explicit("Builds/WebGL + 밖의 헤드리스 브라우저 필요"), Category("Online"), NUnit.Framework.Timeout(420000)]
        public IEnumerator M7_Online_웹손님_헤드리스브라우저가직접연결페이지로들어와준비하면게임을시작한다()
        {
            string web = WebFiles.FindRoot(ProjectRoot, true);
            if (web == null) Assert.Ignore("Builds/WebGL 없음(Sinnoh.Editor.BoardBuild.WebGL 먼저)");
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            ScreenBoot boot = null;
            for (int i = 0; i < 30 && (boot == null || boot.Root == null); i++)
            {
                yield return null;
                boot = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
            }
            Assert.That(boot != null && boot.Root != null, "화면 뿌리 없음");
            boot.PointerFromMouse = false;
            var flow = boot.Flow;
            LobbyScreen.Clipboard = _ => { };
            flow.Room.Listen = Loopback;
            flow.Room.WebRoot = web;
            flow.Room.OpenCreate();
            var create = (RoomCreateScreen)flow.Stage.Current;
            create.SetInternet(false);
            create.SetSeats(2);
            flow.Room.Create.GalacticAi = Sinnoh.Rules.Session.GalacticAiLevel.Principle;   // 원칙 AI로(새 방 기본값은 「강함」 — 결정 118 ②) · 트레이너 카드 켬(새 방 기본값은 끔 — 결정 134)
            flow.Room.Create.TrainerCards = true;
            Assert.IsTrue(create.Start(), "직접 연결 방(7777이 비어 있어야 한다)");
            var net = flow.Room.Net;
            Assert.That(net.WebPort, Is.GreaterThan(0), "페이지 서버");
            Report($"[M7 WebLive] 방 열림 — 페이지 http://{Loopback}:{net.WebPort}/ · 게임 {net.Port}");
            try
            {
                float end = Time.realtimeSinceStartup + 240;
                RoomSeat guest = null;
                while (Time.realtimeSinceStartup < end && guest == null)
                {
                    guest = net.Room.Seats.FirstOrDefault(s => s != null && !s.Host);
                    yield return null;
                }
                Assert.IsNotNull(guest, "웹 손님이 들어오지 않았다");
                Assert.AreEqual(Platform.Web, guest.Platform, "웹 손님판으로 들어왔다");
                Report($"[M7 WebLive] 웹 손님 참가 — 닉네임 「{guest.Nickname}」 · {guest.Platform}");
                while (Time.realtimeSinceStartup < end && !net.Room.CanStart) yield return null;
                Assert.IsTrue(net.Room.CanStart, "웹 손님이 준비하지 않았다");
                Report("[M7 WebLive] 웹 손님 준비 완료 — 게임 시작");
                var lobby = (LobbyScreen)flow.Stage.Current;
                while (Time.realtimeSinceStartup < end && !lobby.CanStart) yield return null;   // 화면이 방 상태를 옮긴 뒤(다음 프레임)
                Assert.IsTrue(lobby.Start(), "[게임 시작]");
                while (Time.realtimeSinceStartup < end && !flow.InRoomGame) yield return null;
                Assert.IsTrue(flow.InRoomGame, "방 판");
                Report("[M7 WebLive] 방 판 시작 — 웹 손님 화면을 찍을 시간 12초(브라우저는 그 뒤까지 열어 둔다)");
                float hold = Time.realtimeSinceStartup + 12;
                while (Time.realtimeSinceStartup < hold) yield return null;
                Assert.IsTrue(net.Room.Seats.Any(s => s != null && !s.Host && s.Connected), "게임 중에도 웹 손님이 붙어 있다");
                Report("[M7 WebLive] 끝 — 웹 손님 접속 유지");
            }
            finally
            {
                flow.OpenTitle();
            }
            float gone = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < gone && UnityEngine.Object.FindObjectsByType<NetRunner>(FindObjectsSortMode.None).Length > 0) yield return null;
        }

        /// <summary>
        /// plan M7 「연출 실접속」의 웹 몫(완료 조건 6 — 웹 프레임 · 첫 SE · 웹 손님판 핑): 방장(이 에디터의 화면)이 직접 연결 방을 열고, 밖의 헤드리스 브라우저 웹 손님이 들어와 준비하면 판을 시작해
        /// 방장 화면이 자기 좌석을 스스로 몬다(셋업 → 보드 — 실시각, 연출을 건너뛰지 않는다). 보드에 닿으면 6초마다 내 말의 칸에 핑을 찍고, 웹 손님이 찍은 핑(Alt + 클릭)이 오면 적는다.
        /// 웹 손님 쪽의 프레임 · 효과음 미리 열기 · 차이 줄은 브라우저 콘솔(<c>[Frame]</c> · <c>[Sound]</c> · <c>[Sync]</c>)에 남는다 — 밖의 스크립트가 읽는다.
        /// 보드에 닿은 뒤 SINNOH_WEB_HOLD초(기본 240) 동안, 또는 SINNOH_WEB_STOP 파일이 생길 때까지 방을 열어 둔다.
        /// </summary>
        [UnityTest, Explicit("Builds/WebGL + 밖의 헤드리스 브라우저 필요"), Category("Online"), NUnit.Framework.Timeout(1800000)]
        public IEnumerator M7_Online_웹손님_연출실접속_방장화면이판을몰고핑을주고받는동안웹손님을붙여둔다()
        {
            string web = WebFiles.FindRoot(ProjectRoot, true);
            if (web == null) Assert.Ignore("Builds/WebGL 없음(Sinnoh.Editor.BoardBuild.WebGL 먼저)");
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            ScreenBoot boot = null;
            for (int i = 0; i < 30 && (boot == null || boot.Root == null); i++)
            {
                yield return null;
                boot = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
            }
            Assert.That(boot != null && boot.Root != null, "화면 뿌리 없음");
            boot.PointerFromMouse = false;
            var flow = boot.Flow;
            var stage = flow.Stage;
            LobbyScreen.Clipboard = _ => { };
            flow.Room.Listen = Loopback;
            flow.Room.WebRoot = web;
            flow.Room.OpenCreate();
            var create = (RoomCreateScreen)stage.Current;
            create.SetInternet(false);
            create.SetSeats(2);
            flow.Room.Create.GalacticAi = Sinnoh.Rules.Session.GalacticAiLevel.Principle;   // 원칙 AI로(새 방 기본값은 「강함」 — 결정 118 ②) · 트레이너 카드 켬(새 방 기본값은 끔 — 결정 134)
            flow.Room.Create.TrainerCards = true;
            Assert.IsTrue(create.Start(), "직접 연결 방(7777이 비어 있어야 한다)");
            var net = flow.Room.Net;
            Assert.That(net.WebPort, Is.GreaterThan(0), "페이지 서버");
            Report($"[M7 WebSync] 방 열림 — 페이지 http://{Loopback}:{net.WebPort}/ · 게임 {net.Port} · 프로토콜 판 {Protocol.Version}");
            int holdSec = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_WEB_HOLD"), out int h) && h > 0 ? h : 240;
            string stop = Environment.GetEnvironmentVariable("SINNOH_WEB_STOP");
            int guestPings = 0, myPings = 0, seenPings = 0;
            try
            {
                float end = Time.realtimeSinceStartup + 600;
                while (Time.realtimeSinceStartup < end && !net.Room.CanStart) yield return null;
                Assert.IsTrue(net.Room.CanStart, "웹 손님이 들어와 준비하지 않았다");
                var guest = net.Room.Seats.First(s => s != null && !s.Host);
                Assert.AreEqual(Platform.Web, guest.Platform, "웹 손님판으로 들어왔다");
                Report($"[M7 WebSync] 웹 손님 「{guest.Nickname}」 준비 완료 — 게임 시작");
                var lobby = (LobbyScreen)stage.Current;
                while (Time.realtimeSinceStartup < end && !lobby.CanStart) yield return null;
                Assert.IsTrue(lobby.Start(), "[게임 시작]");
                while (Time.realtimeSinceStartup < end && !(flow.InRoomGame && flow.Client != null && flow.Client.View != null)) yield return null;
                Assert.IsTrue(flow.InRoomGame, "방 판");
                int me = flow.Client.Seat;
                var pilot = new SoloPilot();
                flow.EventsSeen += pilot.Saw;
                float began = Time.realtimeSinceStartup, boardAt = -1, pingAt = 0, stuckSince = began;
                int lastChoice = -1;
                while (true)
                {
                    float now = Time.realtimeSinceStartup;
                    if (flow.OnBoard && boardAt < 0)
                    {
                        boardAt = now;
                        Report("[M7 WebSync] 방장 화면이 보드에 닿았다");
                    }
                    if (boardAt >= 0 && now - boardAt > holdSec) break;
                    if (!string.IsNullOrEmpty(stop) && File.Exists(stop)) break;
                    if (boardAt < 0 && now - began > 900) Assert.Fail("셋업이 끝나지 않았다(웹 손님이 고르지 않았다)");
                    // 핑: 내가 6초마다 내 말의 칸에 · 웹 손님 것이 오면 적는다
                    var live = flow.Room.Live;
                    if (live != null && live.PingsSeen > seenPings)
                    {
                        var last = live.Pings.Count > 0 ? live.Pings[live.Pings.Count - 1] : null;
                        seenPings = live.PingsSeen;
                        if (last != null && last.Seat != me)
                        {
                            guestPings++;
                            Report($"[M7 WebSync] 웹 손님의 핑을 받았다 — 좌석 {last.Seat} · 칸 {last.Cell}(모두 {guestPings})");
                        }
                    }
                    if (flow.OnBoard && now - pingAt > 6 && flow.Room.Ping(flow.Client.View.Seats[me].Cell))
                    {
                        pingAt = now;
                        myPings++;
                    }
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
                        if (flow.OnBoard && now - stuckSince > 6 && flow.Fx.Sequencer.IsEmpty)
                        {
                            flow.Answer(choice, choice.Default.Clone());
                            stuckSince = now;
                        }
                    }
                    else if (flow.OnBoard && stage.Current is BattleScreen) FlowPilot.Step(flow, pilot);
                    yield return null;
                }
                bool connected = net.Room.Seats.Any(s => s != null && !s.Host && s.Connected);
                var d = flow.Drift;
                Report($"[M7 WebSync] 끝 — 웹 손님 접속 {connected} · 내 핑 {myPings} · 받은 웹 손님 핑 {guestPings} · 명령 {net.Host.Host.Game?.CommandCount} · 방장 화면 차이(갱신 {d.Count}) 최대 +{d.MaxMs} · 최소 {d.MinMs} ms");
                Assert.IsTrue(connected, "게임 중에도 웹 손님이 붙어 있다");
            }
            finally
            {
                flow.OpenTitle();
            }
            float gone = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < gone && UnityEngine.Object.FindObjectsByType<NetRunner>(FindObjectsSortMode.None).Length > 0) yield return null;
        }

        [UnityTest, Explicit("Builds/WebGL + cloudflared + 인터넷 필요"), Category("Online"), NUnit.Framework.Timeout(240000)]
        public IEnumerator M7_Online_웹손님_cloudflared임시터널로페이지가나간다_닫으면프로세스를끝낸다()
        {
            string web = WebFiles.FindRoot(ProjectRoot, true);
            if (web == null) Assert.Ignore("Builds/WebGL 없음");
            string exe = CloudflaredTunnel.FindExe(ProjectRoot);
            if (exe == null) Assert.Ignore("cloudflared.exe 없음(node Tools/fetch-cloudflared.mjs)");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var guestWeb = new GuestWeb(web, exe, () => null, JoinTarget.DefaultPort);
            Assert.IsNull(guestWeb.PageMissing);
            var tunnel = guestWeb.OpenPage();
            try
            {
                while (tunnel.State == TunnelState.Starting && watch.ElapsedMilliseconds < 45000)
                {
                    tunnel.Tick(watch.ElapsedMilliseconds);
                    yield return null;
                }
                Assert.AreEqual(TunnelState.Ready, tunnel.State, "터널 주소");
                long gotMs = watch.ElapsedMilliseconds;
                StringAssert.EndsWith(JoinTarget.TunnelSuffix, tunnel.Url);
                Report($"[M7 WebLive] 터널 주소 {tunnel.Url} · {gotMs} ms · 원 서버 127.0.0.1:{guestWeb.Server.Port} · {exe}");
                // 새 터널 이름이 퍼질 때까지 몇 초 걸린다 — /alive가 「ok」일 때까지 다시
                string body = null, error = null;
                while (body != WebFiles.AliveBody && watch.ElapsedMilliseconds < 120000)
                {
                    using (var req = UnityWebRequest.Get(tunnel.Url + WebFiles.AlivePath))
                    {
                        req.timeout = 15;
                        yield return req.SendWebRequest();
                        body = req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : null;
                        error = req.error;
                    }
                    if (body != WebFiles.AliveBody)
                    {
                        float wait = Time.realtimeSinceStartup + 2;
                        while (Time.realtimeSinceStartup < wait) yield return null;
                    }
                }
                Assert.AreEqual(WebFiles.AliveBody, body, "터널 너머 /alive: " + error);
                Report($"[M7 WebLive] 터널 너머 /alive ok · {watch.ElapsedMilliseconds} ms");
                using (var req = UnityWebRequest.Get(tunnel.Url + "/index.html"))
                {
                    req.timeout = 30;
                    yield return req.SendWebRequest();
                    Assert.AreEqual(UnityWebRequest.Result.Success, req.result, req.error);
                    StringAssert.Contains("unity-canvas", req.downloadHandler.text);
                }
                using (var req = UnityWebRequest.Head(tunnel.Url + "/Build/WebGL.wasm.gz"))
                {
                    req.timeout = 30;
                    yield return req.SendWebRequest();
                    Assert.AreEqual(UnityWebRequest.Result.Success, req.result, req.error);
                    Report($"[M7 WebLive] 터널 너머 wasm 머리: Content-Encoding {req.GetResponseHeader("Content-Encoding")} · Content-Type {req.GetResponseHeader("Content-Type")} · 길이 {req.GetResponseHeader("Content-Length")}");
                }
                // 밖의 헤드리스 브라우저가 터널 주소로 열어 볼 시간(환경 변수 SINNOH_TUNNEL_HOLD = 초)
                if (int.TryParse(Environment.GetEnvironmentVariable("SINNOH_TUNNEL_HOLD"), out int holdSec) && holdSec > 0)
                {
                    float hold = Time.realtimeSinceStartup + holdSec;
                    while (Time.realtimeSinceStartup < hold)
                    {
                        tunnel.Tick(watch.ElapsedMilliseconds);
                        yield return null;
                    }
                    Report($"[M7 WebLive] {holdSec}초 열어 둠 — 서버가 받은 요청 {guestWeb.Server.Served} · 터널 {tunnel.State}");
                }
            }
            finally
            {
                tunnel.Dispose();
                guestWeb.Dispose();
            }
            Assert.AreEqual(TunnelState.Stopped, tunnel.State);
            float settle = Time.realtimeSinceStartup + 2;
            while (Time.realtimeSinceStartup < settle) yield return null;
            var left = System.Diagnostics.Process.GetProcessesByName("cloudflared");
            int mine = 0;
            foreach (var p in left)
            {
                try { if (string.Equals(Path.GetFullPath(p.MainModule.FileName), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase) && !p.HasExited) mine++; }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            Assert.AreEqual(0, mine, "우리 cloudflared가 남았다");
        }
    }
}
