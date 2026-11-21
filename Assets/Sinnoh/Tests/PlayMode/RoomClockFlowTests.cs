using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 결정 140 ①를 방 설정 화면으로(망 없이 — 가짜 방 <see cref="RoomLive"/>): 방장의 스테퍼 → 보낸 방 설정 · 직접 설정 칸 · 프리셋 되돌림 · 범위 끝 흐림 ·
    /// 시계 끔이면 칸 없음 · 손님은 읽기만 + 바뀐 칸 「변경됨」(준비 완료로 비움) · 게임 메뉴의 방 설정 쪽이 방이 돌려준 상태로 다시 그려진다. 방(HostSession)의 길이는 EditMode <c>RoomTests.M10_…</c>.
    /// </summary>
    public sealed class RoomClockFlowTests
    {
        static RoomState Lobby(RoomSettings settings, bool guestReady = false, RoomPhase phase = RoomPhase.Lobby) => new RoomState
        {
            Phase = phase, RoomId = "room-clock", Settings = settings,
            Seats = new List<RoomSeat>
            {
                new RoomSeat { Nickname = "방장", Host = true, Connected = true },
                new RoomSeat { Nickname = "손님", Connected = true, Ready = guestReady },
                new RoomSeat { Nickname = "셋째", Connected = true },
            },
        };

        static string Value(VisualElement root, string key) => root.Q("room-set-clock-" + key + "-value")?.Q<Label>()?.text ?? (root.Q("room-set-clock-" + key + "-value") as Label)?.text;

        static void Click(VisualElement e)
        {
            Assert.That(e, Is.Not.Null, "누를 칸이 없다");
            using (var ev = ClickEvent.GetPooled())
            {
                ev.target = e;
                e.SendEvent(ev);
            }
        }

        static RoomSettings LastSent(RoomLive live) => (live.Sent.LastOrDefault(m => m is ChangeSettings) as ChangeSettings)?.Settings;

        [UnityTest]
        public IEnumerator M10_시간세부_방장스테퍼_직접설정_프리셋되돌림_손님읽기전용과변경됨_게임메뉴다시그리기_결정140()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            string memoWas = RoomIdentity.ClockMemo;
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
                boot.PointerFromMouse = false;
                for (int i = 0; i < 4; i++) yield return null;
                var flow = boot.Flow;
                var stage = flow.Stage;
                var root = stage.Root;

                // ── 방장 로비: 기본 「느긋하게」 + 열 칸 ──
                var host = new RoomLive(() => 0) { IsHost = true, Seat = 0, Room = Lobby(new RoomSettings { Seats = 3 }) };
                flow.OpenTitle();
                flow.Room.ShowLobby(host);
                yield return null;
                var lobby = stage.Current as LobbyScreen;
                Assert.That(lobby, Is.Not.Null, "로비");
                Assert.That(root.Q("room-set-clock-grid"), Is.Not.Null, "시간 세부 칸");
                foreach (var f in ClockSettings.Fields)
                {
                    Assert.That(root.Q("room-set-clock-" + f.Key + "-minus"), Is.Not.Null, f.Key + " [−]");
                    Assert.That(root.Q("room-set-clock-" + f.Key + "-plus"), Is.Not.Null, f.Key + " [+]");
                    Assert.That(Value(root, f.Key), Is.EqualTo(ClockView.Text(f.Get(ClockSettings.For(ClockPreset.Relaxed)))), f.Key + " 값");
                }
                Assert.That(root.Q("room-set-opt-clock-relaxed").ClassListContains("sn-opt-picked"), Is.True);
                Assert.That(root.Q("room-set-opt-clock-custom"), Is.Null, "프리셋이면 「직접 설정」 칸이 없다");

                // [+] 턴 → 직접 설정으로 보낸다 → 방이 돌려주면 「직접 설정」 · 3:05
                Click(root.Q("room-set-clock-map_turn-plus"));
                yield return null;
                var sent = LastSent(host);
                Assert.That(sent, Is.Not.Null, "[+]가 방 설정을 보냈다");
                Assert.That((sent.Clock, sent.Custom?.MapTurnMs), Is.EqualTo((ClockPreset.Custom, (int?)185000)));
                Assert.That(RoomIdentity.ClockMemo, Is.EqualTo(sent.ClockMemo()), "다음 새 방에 쓸 값을 남긴다");
                host.Room = Lobby(sent);
                lobby.Sync();
                yield return null;
                Assert.That(Value(root, "map_turn"), Is.EqualTo("3:05"));
                Assert.That(root.Q("room-set-opt-clock-custom")?.ClassListContains("sn-opt-picked"), Is.True, "「직접 설정」");
                Assert.That(root.Q("room-set-opt-clock-relaxed").ClassListContains("sn-opt-picked"), Is.False);

                // [−] → 프리셋과 같아져 「느긋하게」로
                Click(root.Q("room-set-clock-map_turn-minus"));
                yield return null;
                sent = LastSent(host);
                Assert.That((sent.Clock, sent.Custom), Is.EqualTo((ClockPreset.Relaxed, (ClockSettings)null)), "프리셋과 같으면 그 프리셋");
                host.Room = Lobby(sent);
                lobby.Sync();
                yield return null;
                Assert.That(root.Q("room-set-opt-clock-custom"), Is.Null);
                Assert.That(root.Q("room-set-opt-clock-relaxed").ClassListContains("sn-opt-picked"), Is.True);

                // 범위 끝: 턴 15초면 [−] 흐림 · 누르지 않는다
                var low = new RoomSettings { Seats = 3 };
                for (int i = 0; i < 40; i++) low.StepClock("map_turn", -1);
                host.Room = Lobby(low);
                lobby.Sync();
                yield return null;
                Assert.That(Value(root, "map_turn"), Is.EqualTo("0:15"));
                Assert.That(root.Q("room-set-clock-map_turn-minus").ClassListContains("sn-off"), Is.True, "끝에 닿으면 흐림");
                int count = host.Sent.Count;
                Click(root.Q("room-set-clock-map_turn-minus"));
                yield return null;
                Assert.That(host.Sent.Count, Is.EqualTo(count), "흐린 [−]는 보내지 않는다");
                Assert.That(lobby.Settings.StepClock("map_turn", -1), Is.False);

                // 프리셋 「보통」을 누르면 직접 설정을 비운다 · 「끔」이면 칸이 없다
                Assert.That(lobby.Settings.PickClock(ClockPreset.Normal), Is.True);
                Assert.That((LastSent(host).Clock, LastSent(host).Custom), Is.EqualTo((ClockPreset.Normal, (ClockSettings)null)));
                Assert.That(lobby.Settings.PickClock(ClockPreset.Off), Is.True);
                host.Room = Lobby(LastSent(host));
                lobby.Sync();
                yield return null;
                Assert.That(root.Q("room-set-clock-grid"), Is.Null, "시계 끔에는 칸이 없다");

                // ── 손님: 읽기만 · 바뀐 칸에 「변경됨」 · 준비 완료하면 비운다 ──
                var guest = new RoomLive(() => 0) { IsHost = false, Seat = 1, Room = Lobby(new RoomSettings { Seats = 3 }) };
                flow.OpenTitle();
                flow.Room.ShowLobby(guest);
                yield return null;
                lobby = (LobbyScreen)stage.Current;
                Assert.That(root.Q("room-set-clock-map_turn-plus"), Is.Null, "손님은 [+]가 없다");
                Assert.That(root.Q("room-set-clock-map_turn-changed"), Is.Null);
                Assert.That(lobby.Settings.StepClock("map_turn", +1), Is.False, "손님은 바꾸지 못한다");
                var changed = new RoomSettings { Seats = 3 };
                for (int i = 0; i < 4; i++) changed.StepClock("map_turn", +1);
                guest.Room = Lobby(changed);
                lobby.Sync();
                yield return null;
                Assert.That(Value(root, "map_turn"), Is.EqualTo("3:20"));
                Assert.That(root.Q("room-set-clock-map_turn-changed"), Is.Not.Null, "바뀐 칸 「변경됨」");
                Assert.That(root.Q("room-set-clock-changed"), Is.Not.Null, "프리셋 줄도(느긋하게 → 직접 설정)");
                Assert.That(root.Q("room-set-clock-reaction-changed"), Is.Null, "안 바뀐 칸은 없다");
                guest.Room = Lobby(changed, guestReady: true);
                lobby.Sync();
                yield return null;
                Assert.That(root.Q("room-set-clock-map_turn-changed"), Is.Null, "준비 완료하면 비운다");
                Assert.That(lobby.Settings.Changed, Is.Empty);

                // ── 게임 메뉴의 방 설정 쪽: 방장 [+] → 방이 돌려준 상태로 다시 그린다 ──
                flow.OpenTitle();
                flow.StartSeats(new SoloSettings(), 3, 11, 12);
                flow.Skip(flow.OpeningMs + 4000);
                var game = new RoomLive(() => 0) { IsHost = true, Seat = 0, Room = Lobby(new RoomSettings { Seats = 3 }, phase: RoomPhase.InGame) };
                flow.Room.ShowGame(game);
                yield return null;
                flow.OpenMenu();
                var menu = (stage.Info as BoardMenuScreen) ?? (stage.Current as BoardMenuScreen);
                Assert.That(menu, Is.Not.Null, "게임 메뉴");
                menu.Show(BoardMenuScreen.SetPage);
                yield return null;
                Assert.That(Value(root, "reaction"), Is.EqualTo("0:30"));
                Click(root.Q("room-set-clock-reaction-plus"));
                yield return null;
                sent = LastSent(game);
                Assert.That(sent?.Custom?.ReactionMs, Is.EqualTo(35000), "게임 중에도 바꿀 수 있다");
                game.Room = Lobby(sent, phase: RoomPhase.InGame);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(Value(root, "reaction"), Is.EqualTo("0:35"), "방이 돌려준 상태로 메뉴가 다시 그려졌다");
                Assert.That(root.Q("room-set-opt-clock-custom"), Is.Not.Null);
                flow.OpenTitle();

                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                RoomIdentity.ClockMemo = memoWas;
            }
        }
    }
}
