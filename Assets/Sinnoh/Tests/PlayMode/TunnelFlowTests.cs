using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// [인터넷] 방의 Relay 실패 때 터널(02 §2-1 [가정 M7-59~]): 망 · UGS · cloudflared 없이(가짜 터널) Relay가 실패하면 게임을 루프백에 열고 터널 → 로비에 「참가 주소 wss://…」 + [복사].
    /// 터널 도구가 없는 길은 <c>RoomFlowTests</c>(직접 연결 안내). 포트는 루프백에만 연다. 클립보드는 가짜로 바꿔 끼운다(사용자의 클립보드를 건드리지 않는다).
    /// </summary>
    public sealed class TunnelFlowTests
    {
        const string Loopback = "127.0.0.1";

        sealed class FakeTunnel : ITunnel
        {
            public TunnelState State { get; set; }
            public string Url { get; set; }
            public string Next;
            public bool Disposed;

            public void Tick(long nowMs)
            {
                if (Next == null) return;
                Url = Next;
                State = TunnelState.Ready;
                Next = null;
            }

            public void Dispose() => Disposed = true;
        }

        sealed class FakeGameTunnel : IGameTunnel
        {
            public Func<string> Loopback;
            public readonly FakeTunnel Game = new FakeTunnel();
            public bool Disposed;
            public bool CanTunnel => true;
            public string StartGameLoopback() => Loopback();
            public ITunnel OpenGame() => Game;
            public void Dispose() => Disposed = true;
        }

        sealed class DownRelay : IRelayHost
        {
            public void Allocate(int maxConnections, Action<string> ok, Action<string> fail) => fail(OnlineFail.RelayAuth);
        }

        readonly List<string> errors = new List<string>();
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
            yield return RoomGone();
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

        static IEnumerator RoomGone()
        {
            float end = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < end && UnityEngine.Object.FindObjectsByType<NetRunner>(FindObjectsSortMode.None).Length > 0) yield return null;
        }

        static string Text(VisualElement root, string name) => (root.Q(name) is Label l ? l.text : root.Q(name)?.Q<Label>()?.text)?.Replace("⁠", "");

        [UnityTest, NUnit.Framework.Timeout(90000)]
        public IEnumerator M7_터널_인터넷방Relay가실패해도터널이있으면루프백게임과터널_로비에참가주소wss()
        {
            yield return Boot();
            const ushort port = 47822;
            var flow = boot.Flow;
            var stage = flow.Stage;
            string copied = null;
            LobbyScreen.Clipboard = t => copied = t;
            var tunnel = new FakeGameTunnel();
            tunnel.Loopback = () => flow.Room.Net.Host.Start(port, Loopback) ? null : "NET_PORT_BUSY";
            flow.Room.Listen = Loopback;
            flow.Room.Port = port;
            flow.Room.Board = RoomFlow.NoRooms;
            flow.Room.Relay = new DownRelay();
            flow.Room.Tunnel = tunnel;
            flow.Room.OpenCreate();
            var create = (RoomCreateScreen)stage.Current;
            Assert.IsTrue(flow.Room.Create.Internet);
            Assert.IsTrue(create.Start(), "Relay는 실패했지만 터널로 여는 중");
            Assert.IsInstanceOf<RoomCreateScreen>(stage.Current, "터널 주소를 기다린다");
            Assert.AreEqual((InternetRoom.Phase.Starting, InternetRoom.GameRoute.Tunnel), (flow.Room.Net.Internet.State, flow.Room.Net.Internet.Route));
            tunnel.Game.Next = "https://lazy-dog.trycloudflare.com";
            for (int i = 0; i < 120 && !(stage.Current is LobbyScreen); i++) yield return null;
            Assert.IsInstanceOf<LobbyScreen>(stage.Current, "게임 터널 주소를 받으면 로비");
            yield return null;
            Assert.AreEqual("lazy-dog.trycloudflare.com", Text(stage.Root, "lobby-code"), "화면에는 호스트만 — 전체(wss://…)는 [복사]");
            Assert.AreEqual("참가 주소", Text(stage.Root, "lobby-code-label"));
            Assert.AreEqual("abcdefghij-abcdefghij-abcdefghij-abcdefghij…", LobbyScreen.ShortLink("wss://abcdefghij-abcdefghij-abcdefghij-abcdefghij-abcdefghij.trycloudflare.com"), "아주 긴 이름은 뒤를 줄인다");
            var lobby = (LobbyScreen)stage.Current;
            Assert.IsTrue(lobby.PressCopy("lobby-code-copy"));
            Assert.AreEqual("wss://lazy-dog.trycloudflare.com", copied);
            flow.Room.Leave();
            yield return RoomGone();
            Assert.IsTrue(tunnel.Game.Disposed && tunnel.Disposed, "방을 닫으면 터널을 끝낸다");
            CollectionAssert.IsEmpty(errors, "에러 로그");
        }
    }
}
