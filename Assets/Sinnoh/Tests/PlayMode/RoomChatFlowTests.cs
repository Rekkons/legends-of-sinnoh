using System;
using System.Collections;
using System.Collections.Generic;
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
    /// 결정 140 ② 방 채팅 — 2좌석 방을 실제 루프백(직접 연결 127.0.0.1 · NGO 둘)으로: 손님 화면(이 흐름)의 로비 채팅 판 → 방장 글 · 내 글(Enter)이 망을 돌아 그려짐 ·
    /// 글 칸 포커스 = 글 치기(카메라 · 단축키 막힘) · Esc = 포커스를 놓음(로비는 그대로) · 연타 안내 → 게임 시작 뒤 덮개 층의 접힌 창(로비 기록 그대로 · 이름은 받을 때의 것) ·
    /// 새 글 = 안 읽은 수 + 미리 보기 · 펼침 · Esc = 접기 · 긴 기록 스크롤(높이 고정 · 막대 · 휠 · 위로 굴린 채 새 줄이 와도 그 자리) → 방장이 끄면 창이 사라진다. 실제 UGS를 부르지 않는다. Gen 겉모양이 있어야 돈다.
    /// </summary>
    public sealed class RoomChatFlowTests
    {
        const string Loopback = "127.0.0.1";
        /// <summary>한글 줄바꿈용 낱말 잇기 글자(키트가 Wrapped 글에 넣는다 — 견줄 때 뺀다).</summary>
        const string Joiner = "\u2060";

        readonly List<string> errors = new List<string>();
        readonly List<NetRunner> runners = new List<NetRunner>();
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
            boot.Flow.Tutorial.Record = TutorialRecord.Memory();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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

        [UnityTest, NUnit.Framework.Timeout(240000)]
        public IEnumerator M10_결정140_채팅_2좌석실제루프백_로비와게임중_글칸초점은단축키막음_Enter보내기Esc접기_새글표시_긴기록스크롤_끄면사라짐()
        {
            yield return Boot();
            const ushort port = 47861;
            var flow = boot.Flow;
            var stage = flow.Stage;
            flow.Room.Board = RoomFlow.NoRooms;
            var host = OpenHost(flow, port, 2);
            yield return JoinAsGuest(flow, port, "손님트레이너");
            var lobby = (LobbyScreen)stage.Current;
            var chat = lobby.Chat;
            Assert.That(chat, Is.Not.Null, "방이 있는 로비의 채팅 판");
            yield return Until(() => chat.Shown, 5, "켠 방의 채팅 판");
            Assert.That(stage.Root.Q("chat-empty").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "빈 기록 안내");
            Assert.That(stage.Root.Q("room-set-opt-chat-on"), Is.Not.Null, "방 설정 판의 채팅 줄(손님은 읽기만)");
            var live = flow.Room.Live;

            // ① 방장 글 → 손님 화면(이름 · 얼굴 상자)
            host.Send(new SendChat { Text = "안녕하세요 <b>굵게</b>" });
            yield return Until(() => live.Chat.Count == 1, 10, "방장 글");
            yield return null;
            Assert.That(live.Chat[0].Seat, Is.EqualTo(0));
            Assert.That(stage.Root.Q<Label>("chat-line-0-name").text.Replace(Joiner, ""), Is.EqualTo("방장"));
            var first = stage.Root.Q<Label>("chat-line-0-text");
            Assert.That(first.text.Replace(Joiner, ""), Is.EqualTo("안녕하세요 <b>굵게</b>"), "서식 태그는 글자 그대로");
            Assert.That(first.enableRichText, Is.False);
            Assert.That(stage.Root.Q("chat-line-0-who").Q(FaceBox.OwnerName), Is.Not.Null, "얼굴 상자");

            // ② 글 칸 포커스 = 글 치기(카메라 Q · E · 단축키 막힘) → Enter = 보내기(망을 돌아 내 줄) → Esc = 포커스만 놓음(로비 그대로)
            var rig = UnityEngine.Object.FindAnyObjectByType<BoardCameraRig>();
            chat.Field.Focus();
            yield return null;
            Assert.That(flow.Typing, Is.True, "글 칸 포커스 = 글 치기");
            if (rig != null) Assert.That(rig.KeysEnabled, Is.False, "카메라 Q · E가 글쇠를 받지 않는다");
            chat.SetText("반갑습니다");
            Key(chat.Field, '\0', KeyCode.Return);
            yield return Until(() => live.Chat.Count == 2, 10, "내 글이 방을 돌아온다");
            Assert.That((live.Chat[1].Seat, live.Chat[1].Name, live.Chat[1].Text), Is.EqualTo((1, "손님트레이너", "반갑습니다")));
            Assert.That(chat.Draft, Is.Empty, "보낸 뒤 글 칸은 빈다");
            Assert.That(stage.Root.Q("chat-line-1-who"), Is.Not.Null, "보낸 사람이 바뀌면 머리");
            chat.SetText("하나 더");
            Assert.That(chat.Submit(), Is.False, "1초 안의 둘째 줄은 연타 제한");
            Assert.That(stage.Root.Q("chat-wait").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "잠시 후 다시 안내");
            Assert.That(chat.Draft, Is.EqualTo("하나 더"), "걸린 글은 칸에 남는다");
            if (!chat.Focused) chat.Field.Focus();
            yield return null;
            Key(chat.Field, '\0', KeyCode.Escape);
            Assert.That(flow.Escape(), Is.True, "같은 프레임의 Esc(ScreenBoot)도 채팅이 받는다");
            yield return null;
            Assert.That(flow.Typing, Is.False, "Esc = 글 치기 끝");
            if (rig != null) Assert.That(rig.KeysEnabled, Is.True);
            Assert.That(stage.Current, Is.SameAs(lobby), "로비는 그대로");
            Assert.That(flow.Escape(), Is.False, "포커스가 없으면 채팅은 Esc를 받지 않는다");
            chat.SetText("");

            // ③ 게임 시작 → 덮개 층의 접힌 창(로비 기록 그대로 · 받을 때의 이름)
            Assert.That(lobby.ToggleReady(), Is.True);
            yield return Until(() => host.Host.Room.CanStart, 10, "손님 준비");
            host.Send(new StartGame());
            yield return Until(() => flow.InRoomGame && flow.RoomLayer?.Chat != null && flow.RoomLayer.Chat.Shown, 30, "방 판의 채팅 창");
            var game = flow.RoomLayer.Chat;
            Assert.That(game.Open, Is.False, "방 판은 접힌 채 시작");
            Assert.That(stage.Root.Q("chat-tab").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(stage.Root.Q("chat-panel").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(game.Unread, Is.EqualTo(0), "로비에서 본 줄은 안 읽은 수에 없다");

            // ④ 새 글(방장) → 안 읽은 수 + 미리 보기 · 내 글은 세지 않는다
            yield return new WaitForSecondsRealtime(1.1f);
            host.Send(new SendChat { Text = "게임 시작!" });
            yield return Until(() => game.Unread == 1, 10, "안 읽은 수");
            Assert.That(game.PreviewText, Is.EqualTo("방장: 게임 시작!"));
            Assert.That(stage.Root.Q<Label>("chat-unread").text, Is.EqualTo("1"));
            Assert.That(live.SendChat("내 글"), Is.True);
            yield return Until(() => live.Chat.Count == 4, 10, "내 글");
            yield return null;
            Assert.That(game.Unread, Is.EqualTo(1), "내 글은 안 읽은 수에 없다");

            // ⑤ 펼침 = 안 읽음 0 · 글 칸 포커스 → Esc(흐름) = 접기 · 글 치기 끝
            game.Unfold();
            yield return null;
            Assert.That((game.Open, game.Unread), Is.EqualTo((true, 0)));
            Assert.That(flow.Typing, Is.True, "펼치면 글 칸에 포커스");
            Assert.That(flow.Escape(), Is.True);
            yield return null;
            Assert.That((game.Open, flow.Typing), Is.EqualTo((false, false)), "Esc = 접기");

            // ⑥ 긴 기록: 60줄 → 높이 고정 · 막대 · 끝 줄이 보임 → 맨 위로 굴리면 첫 줄 · 위로 굴린 채 새 줄이 와도 그 자리
            game.Unfold();
            yield return null;
            float empty = stage.Root.Q("chat-list").worldBound.height;
            for (int i = 0; i < 60; i++) live.TakeChat(new ChatMsg { From = i % 2, Text = "긴 기록 " + i + " — 줄이 넘치면 기록 칸 안에서 굴러야 한다" });
            for (int i = 0; i < 6; i++) yield return null;
            var list = game.List;
            Assert.That(list.worldBound.height, Is.EqualTo(empty).Within(0.5f), "기록 칸 높이 그대로");
            Assert.That(list.verticalScroller.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "세로 막대");
            Assert.That(list.verticalScroller.highValue, Is.GreaterThan(empty), "굴릴 거리");
            string lastName = "chat-line-" + (live.ChatSeen - 1);
            Assert.That(Inside(list, stage.Root.Q(lastName)), Is.True, "끝 줄이 보인다(새 줄을 따라감)");
            var panel = stage.Root.Q("chat-panel").worldBound;
            Assert.That(panel.yMin >= 0 && panel.yMax <= stage.Root.worldBound.yMax, Is.True, "창이 화면 안 " + panel);
            list.verticalScroller.value = 0;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(Inside(list, stage.Root.Q("chat-line-0")), Is.True, "맨 위 = 첫 줄");
            Assert.That(Inside(list, stage.Root.Q(lastName)), Is.False);
            float at = list.scrollOffset.y;
            Wheel(list, 3f);
            for (int i = 0; i < 2; i++) yield return null;
            Assert.That(list.scrollOffset.y, Is.GreaterThan(at + 1f), "휠 아래로");
            at = list.scrollOffset.y;
            live.TakeChat(new ChatMsg { From = 0, Text = "위로 굴린 채 받은 줄" });
            for (int i = 0; i < 4; i++) yield return null;
            Assert.That(list.scrollOffset.y, Is.EqualTo(at).Within(1f), "읽던 자리를 끌어내리지 않는다");
            game.ScrollToEnd();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(Inside(list, stage.Root.Q("chat-line-" + (live.ChatSeen - 1))), Is.True, "끝으로");

            // ⑦ 방장이 게임 중에 끈다 → 창이 사라지고 글 치기도 끝
            game.Field.Focus();
            yield return null;
            Assert.That(flow.Typing, Is.True);
            var off = host.Host.Room.Settings.Clone();
            off.Chat = false;
            host.Send(new ChangeSettings { Settings = off });
            yield return Until(() => !game.Shown, 10, "끈 방");
            Assert.That(flow.Typing, Is.False, "창이 사라지면 글 치기도 끝");
            Assert.That(live.SendChat("꺼진 방"), Is.False);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // ── 차림(RoomSyncFlowTests와 같은 꼴) ──

        static bool Inside(ScrollView list, VisualElement row)
        {
            var view = list.contentViewport.worldBound;
            var r = row.worldBound;
            return r.yMin >= view.yMin - 1f && r.yMax <= view.yMax + 1f;
        }

        static void Key(TextField field, char c, KeyCode code)
        {
            var target = field.panel.focusController.focusedElement as VisualElement ?? field;
            using (var e = KeyDownEvent.GetPooled(c, code, EventModifiers.None)) target.SendEvent(e);
        }

        static void Wheel(ScrollView sv, float lines)
        {
            var target = sv.contentContainer;
            using (var w = WheelEvent.GetPooled())
            {
                typeof(WheelEvent).GetProperty("delta").SetValue(w, new Vector3(0, lines, 0));
                typeof(MouseEventBase<WheelEvent>).GetProperty("mousePosition").SetValue(w, (Vector2)sv.contentViewport.worldBound.center);
                w.target = target;
                target.SendEvent(w);
            }
        }

        static Hello Hi(GameFlow flow, string token, string nick) =>
            new Hello { BuildId = Application.version, ContentHash = flow.ContentHash ?? "", Token = token, Nickname = nick, Platform = Platform.Pc };

        NetHostSession OpenHost(GameFlow flow, ushort port, int seats)
        {
            var m = NetBootstrap.Create("Host");
            var r = m.gameObject.AddComponent<NetRunner>();
            runners.Add(r);
            var cfg = new HostConfig
            {
                OwnerToken = "own", OwnerNickname = "방장", RoomId = "room-chat-" + port, BuildId = Application.version, ContentHash = flow.ContentHash ?? "", Seed = 20261120, AiSeed = SoloTrail.AiSeed,
                VirtualAgent = new PrincipleAgent(), Settings = new RoomSettings { Seats = seats, Clock = ClockPreset.Off, Chat = true },
            };
            var host = r.OpenHost(m, flow.Ctx, cfg, Hi(flow, "own", "방장"));
            Assert.IsTrue(host.Start(port, Loopback), "방 열기 " + port);
            return host;
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
    }
}
