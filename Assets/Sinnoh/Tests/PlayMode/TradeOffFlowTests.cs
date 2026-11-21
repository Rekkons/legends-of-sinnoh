using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// M9 플레이 시험 T32 · 결정 124(플레이어 간 거래 끄기): 솔로 설정의 거래 줄 「켜기」는 준비 중(고를 수 없음 · 끔에 고정) · 새 판은 거래 없이 · 거래가 켜진 채 저장된 판을 이어 하면 거래가 잠긴다.
    /// 세이브는 임시 폴더에 쓰고 지운다.
    /// </summary>
    public sealed class TradeOffFlowTests
    {
        const long Clock = 1794000000000;
        readonly List<string> errors = new List<string>();
        RenderTexture rt;
        string root;

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
            if (rt != null) rt.Release();
            rt = null;
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
            root = null;
        }

        [UnityTest]
        public IEnumerator M9_T32_거래줄은준비중이고새판은거래없이_켜진채저장된판을이어하면거래가잠긴다()
        {
            errors.Clear();
            Application.logMessageReceived += Grab;
            root = Path.Combine(Path.GetTempPath(), "sinnoh-trade-off-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            ScreenBoot boot = null;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null);
            var flow = boot.Flow;
            var stage = boot.Root.Stage;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            boot.UseSaves(root, () => Clock);
            flow = boot.Flow;   // 저장 폴더를 쥔 흐름(UseSaves가 새로 만들 수 있다)
            stage = boot.Root.Stage;

            // 솔로 설정: 거래 「켜기」는 회색(준비 중) — 고를 수 없고 끔에 고정
            Assert.That(GameFlow.TradingReady, Is.False);
            Assert.That(flow.Settings.Trading, Is.False, "처음 값 = 거래 없음");
            flow.OpenSolo();
            yield return null;
            var solo = (SoloScreen)stage.Current;
            Assert.That(solo.Set(SoloScreen.Trading, "on"), Is.False, "「켜기」를 고르지 못한다");
            Assert.That(solo.Settings.Trading, Is.False);
            Assert.That(stage.Root.Q("solo-opt-" + SoloScreen.Trading + "-on").ClassListContains("sn-off"), Is.True, "회색");
            Assert.That(stage.Root.Q("solo-opt-" + SoloScreen.Trading + "-off").ClassListContains("sn-opt-picked"), Is.True, "끔이 골라져 있다");

            // 새 판은 규칙에서 거래가 꺼져 있다(솔로 설정 그대로)
            solo.Start();
            yield return null;
            Assert.That(flow.Client.View.Options.Trading, Is.False, "새 판 = 거래 없음");
            Assert.That(flow.TradeLocked, Is.False, "새 판은 규칙이 막는다(화면 잠금은 이어 한 판 · 방 판)");

            // 거래가 켜진 채 저장된 판(확인용 — 규칙 기본값)을 이어 하면 화면에서 잠근다
            flow.StartSolo(new SoloSettings { Trading = true }, SoloTrail.Defeat.Seed, SoloTrail.AiSeed);
            yield return null;
            Assert.That(flow.Client.View.Options.Trading, Is.True);
            Assert.That(flow.TradeLocked, Is.False);
            flow.OpenTitle();
            yield return null;
            Assert.That(flow.Resume(GameFlow.SoloSlot), Is.True, flow.ResumeError);
            yield return null;
            Assert.That(flow.Client.View.Options.Trading, Is.True, "저장된 판의 규칙 값은 그대로");
            Assert.That(flow.TradeLocked, Is.True, "이어 한 판은 거래를 잠근다(결정 124)");
            Assert.That(flow.TradePartners(), Is.Empty);
            Assert.That(flow.OpenTrade(), Is.False);
            flow.OpenTitle();
            yield return null;
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_결정124_로비방설정의거래줄은_켜진채저장된설정도사용안함이골라지고사용은꺼진다()
        {
            errors.Clear();
            Application.logMessageReceived += Grab;
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            ScreenBoot boot = null;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null);
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            var flow = boot.Flow;
            var stage = boot.Root.Stage;
            Assert.That(GameFlow.TradingReady, Is.False);

            // 방장 · 손님 모두: 거래가 켜진 채 온 방 설정(규칙 기본값 — 옛 저장 · 가짜 방)도 「사용 안 함」이 골라져 보이고 「사용」은 흐린 「준비 중」
            foreach (bool host in new[] { true, false })
            {
                var lobby = new Lobby { IsHost = host, Seat = host ? 0 : 1 };
                Assert.That(lobby.Room.Settings.Trading, Is.True, "켜진 채 온 설정");
                flow.OpenTitle();
                flow.Room.ShowLobby(lobby);
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<LobbyScreen>());
                var on = stage.Root.Q("room-set-opt-" + RoomSettingsPanel.Trading + "-on");
                var off = stage.Root.Q("room-set-opt-" + RoomSettingsPanel.Trading + "-off");
                string who = host ? "방장" : "손님";
                Assert.That(on, Is.Not.Null, who);
                Assert.That(off, Is.Not.Null, who);
                Assert.That(off.ClassListContains("sn-opt-picked"), Is.True, who + ": 「사용 안 함」이 골라져 있다");
                Assert.That(on.ClassListContains("sn-opt-picked"), Is.False, who + ": 「사용」은 골라져 있지 않다");
                Assert.That(on.ClassListContains("sn-off"), Is.True, who + ": 「사용」은 흐리다(준비 중)");
                using (var click = ClickEvent.GetPooled())
                {
                    click.target = on;
                    on.SendEvent(click);
                }
                yield return null;
                Assert.That(lobby.Sent.Exists(m => m is ChangeSettings c && c.Settings.Trading), Is.False, who + ": 「사용」을 보내지 않았다");
            }
            flow.OpenTitle();
            yield return null;
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // 가짜 방(망 없음): 거래가 켜진 방 설정(RoomSettings 기본값) · 두 자리 · 보낸 것은 적기만 한다
        sealed class Lobby : ILobbySource
        {
            public RoomState Room { get; } = new RoomState
            {
                Phase = RoomPhase.Lobby, RoomId = "room-trade-off", Settings = new RoomSettings { Seats = 2 },
                Seats = new List<RoomSeat> { new RoomSeat { Nickname = "방장", Host = true, Connected = true }, new RoomSeat { Nickname = "손님", Connected = true } },
            };
            public int Seat { get; set; }
            public bool IsHost { get; set; }
            public string JoinCode => null;
            public bool Listed => false;
            public string ListFail => null;
            public IReadOnlyList<KeyValuePair<string, string>> Addresses { get; } = new KeyValuePair<string, string>[0];
            public int Port => 0;
            public bool Tunneled => false;
            public IReadOnlyList<string> WebLinks { get; } = new string[0];
            public string WebNotice => null;
            public bool WebPending => false;
            public readonly List<RoomMsg> Sent = new List<RoomMsg>();
            public void Send(RoomMsg msg) => Sent.Add(msg);
        }
    }
}
