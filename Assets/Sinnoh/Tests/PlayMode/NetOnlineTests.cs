using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Net;
using Sinnoh.Rules.Session;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// plan M7 「Net ② 인터넷 방 · 방 목록」의 NGO 몫(02 §2-1 · §2-5 · §3-1 · §6-3, 결정 92 · 95).
    /// 관문 시험: 가짜 Relay 참가 실패(망·UGS 없음) · 앱 종료를 미뤄 닫힘 이유가 손님에게 닿음 · runInBackground.
    /// 실제 UGS(익명 로그인 · Relay · Lobby)는 [Explicit][Category("Online")] — 관문 밖에서 손으로 한 번(Unity Services는 Play 모드에서만 초기화된다).
    /// 같은 프로세스라 방장·손님이 같은 익명 플레이어다(isekai OnlineRelayTests와 같음). 만든 방·로비는 시험 끝에 지운다.
    /// </summary>
    public sealed partial class NetLoopTests
    {
        [UnityTest, NUnit.Framework.Timeout(30000)]
        public IEnumerator M7_Relay손님_참가가실패하면Failed_코드없음은NET_RELAY_CODE_서버실패NET_RELAY_AUTH는직접연결안내()
        {
            foreach (var code in new[] { OnlineFail.RelayCode, OnlineFail.RelayAuth })
            {
                var r = NewRunner("RelayGuest-" + code);
                var c = new NetClientSession(r.GetComponent<NetworkManager>());
                r.Session = c;
                string asked = null;
                c.FakeRelayJoin((joinCode, fail) => { asked = joinCode; fail(code); });
                Assert.IsTrue(JoinTarget.TryParse("q7k2mz", out var target));
                Assert.IsTrue(c.Connect(target, Hi("relay-" + code)), "참가 시도는 시작된다(결과는 Status)");
                Assert.AreEqual("Q7K2MZ", asked, "참가 코드로 참가(대문자로)");
                Assert.AreEqual((NetStatus.Failed, code, 1), (c.Status, c.FailCode, c.RelayTries));
                Assert.AreEqual(code == OnlineFail.RelayAuth, OnlineFail.SuggestDirect(c.FailCode), "Unity 서버 실패면 [직접 연결] 안내(결정 92)");
                Assert.IsFalse(r.GetComponent<NetworkManager>().IsListening, "NGO를 시작하지 않았다");
                yield return null;
            }
        }

        [UnityTest, NUnit.Framework.Timeout(60000)]
        public IEnumerator M7_앱종료_방장이끝내면종료를미루고방을닫아_손님은끊김보다닫힘이유를받은뒤끝난다_러너가있으면runInBackground()
        {
            const ushort port = 47796;
            var host = OpenHost(port, Cfg("net-quit", 3), new VClock());
            var g1 = Join(port, Hi("g1"), "Guest1");
            yield return Until(() => g1.Status == NetStatus.Connected && Members(host.Local.Room) == 2, 15, "손님 접속");
            Assert.IsTrue(Application.runInBackground, "러너가 켜져 있는 동안 창이 포커스를 잃어도 프레임이 돈다");

            var hr = runners.First(x => ReferenceEquals(x.Session, host));
            var gr = runners.First(x => ReferenceEquals(x.Session, g1));
            Assert.IsTrue(gr.HoldQuit(), "손님은 막지 않는다");
            Assert.IsFalse(hr.HoldQuit(), "방장이 끝내면 종료를 한 번 미룬다");
            Assert.AreEqual(RoomPhase.Closed, host.Host.Phase, "방을 닫았다(마지막 쓰기 · E_HOST_CLOSED)");
            Assert.IsTrue(hr.QuitPending && host.ShuttingDown, "NGO 유예 동안 기다린다");
            Assert.IsFalse(hr.HoldQuit(), "기다리는 동안 다시 불러도 미룬다");
            var watch = Stopwatch.StartNew();
            yield return Until(() => g1.Status == NetStatus.Failed, 10, "손님 끊김");
            Assert.AreEqual(NetCode.HostClosed, g1.FailCode, "끊김(NET_LOST)이 아니라 닫힘 이유");
            yield return Until(() => !hr.QuitPending, 5, "유예가 끝나면 다시 끝낸다");
            Assert.Less(watch.ElapsedMilliseconds, NetRunner.QuitWaitMs + 1000, "늦어도 3초");
            Assert.IsTrue(hr.HoldQuit(), "그 뒤에는 막지 않는다");
        }

        // ── 실제 UGS(관문 밖) ──

        const string OnlineProfile = "sinnoh-online-test";

        [UnityTest, Explicit("인터넷·UGS 필요"), Category("Online"), NUnit.Framework.Timeout(90000)]
        public IEnumerator M7_Online_익명로그인_Relay할당_참가코드로참가_둘다wss()
        {
            UgsBoot.ProfileOverride = OnlineProfile;
            var watch = Stopwatch.StartNew();
            var task = RelayLink.ProbeAsync(InternetRoom.MaxConnections(6));
            yield return Wait(task, 60, "Relay 확인");
            var (code, region, ms) = task.Result;
            Assert.IsTrue(JoinTarget.IsCode(code), "참가 코드 6자: " + code);
            Report($"[M7 Online] 익명 로그인 → Relay 할당 → 코드 {code} → 참가: 지역 {region} · {ms} ms(로그인 포함 {watch.ElapsedMilliseconds} ms) · 플레이어 {UgsBoot.PlayerId}");
        }

        [UnityTest, Explicit("인터넷·UGS 필요"), Category("Online"), NUnit.Framework.Timeout(180000)]
        public IEnumerator M7_Online_공개방은목록에떠서골라Relay로들어가고_하트비트_인원갱신_비공개는목록에없고_닫으면지운다()
        {
            UgsBoot.ProfileOverride = OnlineProfile;
            var watch = Stopwatch.StartNew();
            var stamp = RoomListingModel.Stamp(Hi("own"));

            var hr = NewRunner("OnlineHost");
            var host = hr.OpenHost(hr.GetComponent<NetworkManager>(), LoadCtx(), Cfg("online-pub", 6), Hi("own"));
            var net = hr.OpenInternet(host, new ListingOptions { HostName = "sinnoh-test", Stamp = stamp });
            yield return Until(() => net.State != InternetRoom.Phase.Starting, 60, "인터넷 방 열기");
            Assert.AreEqual(InternetRoom.Phase.Ready, net.State, "Relay로 열려야 한다: " + net.FailCode);
            Assert.AreEqual(NetStatus.Connected, host.Local.Status);
            long open = watch.ElapsedMilliseconds;
            yield return Until(() => net.Listing.Published || net.ListFail != null, 30, "목록에 올리기");
            Assert.IsTrue(net.Listing.Published, "Lobby 만들기: " + net.ListFail);
            long listed = watch.ElapsedMilliseconds;

            var box = new QueryBox();
            yield return Browse(stamp, box, "목록 조회");
            var q = box.Q;
            var row = q.Rooms.FirstOrDefault(x => x.JoinCode == net.JoinCode);
            Assert.IsNotNull(row, $"공개 방이 목록에 뜬다(코드 {net.JoinCode}, 받은 줄 {q.Rooms.Count})");
            Assert.IsTrue(row.Joinable(stamp));
            Assert.AreEqual((1, 6, "own"), (row.Members, row.Seats, row.Host), "방 이름 = 1번 자리(방장) 닉네임");

            var guest = Join(JoinTarget.Relay(row.JoinCode), Hi("g1"), "OnlineGuest");
            yield return Until(() => guest.Status != NetStatus.Connecting, 45, "Relay 참가");
            Assert.AreEqual(NetStatus.Connected, guest.Status, "골라서 Relay(wss)로 들어간다: " + guest.FailCode);
            yield return Until(() => Members(host.Local.Room) == 2 && Members(guest.Room) == 2, 15, "로비 2칸");
            long joined = watch.ElapsedMilliseconds;

            yield return Until(() => net.Listing.Beats >= 1 && net.Listing.Idle, 30, "하트비트");
            Assert.IsTrue(net.Listing.Published, "하트비트 뒤에도 목록에 있다: " + net.ListFail);
            yield return Browse(stamp, box, "다시 조회");
            Assert.AreEqual(2, box.Q.Rooms.FirstOrDefault(x => x.JoinCode == net.JoinCode)?.Members, "인원 갱신");

            var pr = NewRunner("OnlinePrivate");
            var hidden = pr.OpenHost(pr.GetComponent<NetworkManager>(), LoadCtx(), Cfg("online-priv", 3), Hi("own"));
            var privateNet = pr.OpenInternet(hidden, new ListingOptions { Public = false, Stamp = stamp });
            yield return Until(() => privateNet.State != InternetRoom.Phase.Starting, 60, "비공개 방 열기");
            Assert.AreEqual(InternetRoom.Phase.Ready, privateNet.State, privateNet.FailCode);
            yield return Browse(stamp, box, "비공개 뒤 조회");
            Assert.IsTrue(box.Q.Rooms.Any(x => x.JoinCode == net.JoinCode), "공개 방은 그대로 있다(조회가 됐다)");
            Assert.IsFalse(box.Q.Rooms.Any(x => x.JoinCode == privateNet.JoinCode), "비공개 방은 목록에 없다");
            Assert.IsNull(privateNet.Listing);
            var guest2 = Join(JoinTarget.Relay(privateNet.JoinCode), Hi("g2"), "PrivateGuest");
            yield return Until(() => guest2.Status != NetStatus.Connecting, 45, "비공개 방 코드로 참가");
            Assert.AreEqual(NetStatus.Connected, guest2.Status, "비공개 방은 코드로 들어간다: " + guest2.FailCode);
            hidden.Close();

            host.Close();
            yield return Until(() => net.State == InternetRoom.Phase.Closed && net.Listing.Deleted, 30, "닫으면 지운다");
            yield return Browse(stamp, box, "닫은 뒤 조회");
            Assert.IsFalse(box.Q.Rooms.Any(x => x.Id == row.Id), "닫은 방은 목록에 없다");
            Report($"[M7 Online] 인터넷 방: 지역 {RelayLink.LastRegion} · 열기 {open} ms · 목록 올리기 {listed} ms · 조회 → Relay 참가 → 로비 2칸 {joined} ms · 하트비트 {net.Listing.Beats} · 갱신 {net.Listing.Updates} · 비공개 방 목록에 없음·코드로 들어감 · 닫고 지움 {watch.ElapsedMilliseconds} ms");
        }

        sealed class QueryBox
        {
            public ListingQuery Q;
        }

        // 방 목록 조회 — UGS Lobby 조회는 요청 빈도 한도가 있어(11-18 실측 RateLimited) 1.5초 뒤 세 번까지 다시 한다
        static IEnumerator Browse(string stamp, QueryBox box, string what)
        {
            for (int i = 0; i < 4; i++)
            {
                if (i > 0)
                {
                    float t = Time.realtimeSinceStartup + 1.5f;
                    while (Time.realtimeSinceStartup < t) yield return null;
                }
                box.Q = null;
                RoomListing.Browse(stamp, x => box.Q = x);
                yield return Until(() => box.Q != null, 30, what);
                if (box.Q.Ok) yield break;
            }
            Assert.Fail(what + " 실패: " + box.Q.FailCode);
        }

        static IEnumerator Wait(Task task, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("시간 초과: " + what);
                yield return null;
            }
            if (task.IsFaulted) Assert.Fail(what + " 실패: " + task.Exception?.GetBaseException());
        }
    }
}
