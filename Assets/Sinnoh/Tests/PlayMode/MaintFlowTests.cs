using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 정비(plan §4 「정비」, 04 §3-5·§10-3): 보드 HUD의 [정비] → 정비 판(S18) → 기술 가르치기(S28 — 집기 · 빛나는 칸 · 찬 칸 물음 · 잊게 하기) → 지닌물건(S29 — 지니게 하기 · 옮기기 · 가방으로) →
    /// 순서 바꾸기 → 카드 확대(S38) → 놓아주기 → 닫기. 화면의 함수로만 누르고, 켜진 칸이 엔진의 정비 검사(<c>Client.CanMaintain</c>)와 같은지 본다.
    /// 판은 기술 카드·지닌물건을 쥐여 준 판(<see cref="SoloTrail.Kit"/> — 얻는 길은 M5)이고 두 마리를 잡아 파티 셋에서 연다. Gen 겉모양·무대 그림이 있어야 돈다.
    /// </summary>
    public sealed class MaintFlowTests
    {
        [UnityTest]
        public IEnumerator M4_정비판에서기술가르치기지닌물건순서놓아주기를화면으로하고켜진칸은엔진검사와같다_G8()
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
                Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                var flow = boot.Flow;
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                var pilot = FlowPilot.Start(flow, SoloTrail.Kit);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");
                var client = flow.Client;
                var ctx = flow.Ctx;

                // 게임 시작의 막 카드(전원 차단) 동안에는 정비할 수 없다 — 판은 열리되 보기만(결정 125: 흐린 [정비] · 정보 창 층)
                Assert.That(flow.CanOpenParty, Is.False, "막 카드가 도는 동안에는 정비할 수 없다");
                Assert.That(stage.Root.Q<Button>("board-hud-maint").ClassListContains(UiStage.OffClass), Is.True, "흐린 [정비]");
                Assert.That(flow.OpenParty(), Is.True, "보기만 하는 판은 열린다(결정 125)");
                Assert.That(stage.Info, Is.InstanceOf<PartyScreen>());
                Assert.That(((PartyScreen)stage.Info).Lock, Is.EqualTo("screen.peek.lock.fx"));
                flow.CloseParty();
                Assert.That(stage.Info, Is.Null);
                flow.Skip(flow.OpeningMs);
                yield return null;
                Assert.That(flow.CanOpenParty, Is.True, "막 카드가 끝나면 켜진다(내 턴 컷신 중에도 — 정비는 none)");
                yield return null;
                Assert.That(stage.Root.Q<Button>("board-hud-maint").enabledSelf, Is.True);
                Assert.That(stage.Root.Q<Button>("board-hud-maint").text, Is.EqualTo("정비"));

                // 두 마리를 잡아 파티 셋이 된 내 턴까지(길잡이가 화면으로 누른다)
                for (int guard = 0; guard < 6000 && !(flow.InputOpen && PokemonRules.PartyCount(client.View.Seats[client.Seat]) >= 3); guard++)
                {
                    if (!FlowPilot.Step(flow, pilot)) flow.Skip(500);
                    yield return null;
                }
                Assert.That(flow.InputOpen && PokemonRules.PartyCount(client.View.Seats[client.Seat]) >= 3, Is.True, "파티 셋인 내 턴");
                var me = client.View.Seats[client.Seat];

                // ── S18: 좌석 칩을 눌러도, [정비]·T로도 열린다 ──
                Assert.That(flow.Hud.ClickUi(stage.Root.Q("board-hud-seat-1")), Is.True, "좌석 칩 누르기");
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<PartyScreen>());
                flow.ToggleParty();
                yield return null;
                Assert.That(stage.Current, Is.Null, "T로 닫는다");
                flow.ToggleParty();
                yield return null;
                var party = (PartyScreen)stage.Current;
                Assert.That(party.Mine, Is.True);
                Assert.That(flow.InputOpen, Is.False, "판이 떠 있는 동안 보드 입력은 꺼진다");
                Assert.That(flow.Fx.Sequencer.Paused, Is.False, "화면 대기열은 멈추지 않는다");
                Assert.That(stage.Root.Q<Label>("board-party-title").text, Is.EqualTo("정비 · 플레이어 1의 판"));
                Assert.That(party.Pool.Count, Is.EqualTo(SoloTrail.KitMoves.Length));
                Assert.That(party.Bag.Count, Is.EqualTo(SoloTrail.KitItems.Length));
                for (int i = 0; i < PartyScreen.Columns; i++) Assert.That(stage.Root.Q("board-party-col-" + i), Is.Not.Null);
                Assert.That(stage.Root.Q("board-party-col-3").Q<Label>().text, Is.EqualTo("잠긴 칸"), "열린 칸은 셋(rulings A3)");
                Assert.That(stage.Root.Q("board-party-sig-0"), Is.Not.Null);
                Assert.That(stage.Root.Q("board-party-pool-6"), Is.Not.Null);
                Assert.That(stage.Root.Q("board-party-bag-3"), Is.Not.Null);
                Assert.That(stage.Root.Q<Button>("board-party-left-0").enabledSelf, Is.False, "맨 앞은 더 앞으로 가지 못한다");
                Assert.That(stage.Root.Q<Button>("board-party-right-0").enabledSelf, Is.True);

                // ── S28: 기술 카드를 집으면 넣을 수 있는 칸만 빛난다(엔진 검사와 1:1) ──
                Assert.That(party.PickMove(0), Is.True);
                yield return null;
                Assert.That(party.Held, Is.EqualTo(PartyScreen.Hold.Move));
                int ok = 0, no = 0;
                for (int i = 0; i < 3; i++)
                    for (int k = 0; k < PokemonState.MoveSlots; k++)
                    {
                        var m = new Maintenance { Kind = MaintKind.SetMove, Pokemon = me.Party[i], Slot = k, Value = party.HeldValue };
                        bool can = client.CanMaintain(m) == RejectReason.None;
                        var slot = stage.Root.Q("board-party-slot-" + i + "-" + (k + 1));
                        Assert.That(party.CanPut(i, k), Is.EqualTo(can));
                        // 결정 206 ③: 빈 칸은 받을 수 있을 때만 홈이 선다(받지 못하는 빈 칸은 그리지 않는다) · 찬 칸은 띠 그대로 빛남 / 흐림
                        var pk = client.View.Pokemon[me.Party[i]];
                        bool empty = pk.Moves[k] < 0 && pk.Breach[k] < 0 && LegendRules.TacticAt(pk, k) == LegendTactic.None;
                        if (empty && !can)
                        {
                            Assert.That(slot, Is.Null, "받지 못하는 빈 칸 " + i + "-" + k + "은 그리지 않는다");
                            no++;
                            continue;
                        }
                        Assert.That(slot, Is.Not.Null, "칸 " + i + "-" + k);
                        if (empty) Assert.That(slot.parent.ClassListContains("sn-card-hole"), Is.True, "받는 빈 칸 = 홈 " + i + "-" + k);
                        Assert.That(slot.ClassListContains("sn-party-ok"), Is.EqualTo(can), "칸 " + i + "-" + k);
                        Assert.That(slot.ClassListContains("sn-party-no"), Is.EqualTo(!can));
                        if (can) ok++; else no++;
                    }
                Assert.That(ok, Is.GreaterThan(0), "배울 수 있는 포켓몬이 있다");
                Assert.That(no, Is.GreaterThan(0), "배울 수 없는 포켓몬이 있다");
                Assert.That(party.GuideText, Does.Contain(stage.T("move." + SoloTrail.KitMoves[0])));
                int first = party.HeldValue, starter = me.Party[0];
                for (int i = 0; i < 3; i++)
                    if (!party.CanPut(i, 0)) Assert.That(party.PressSlot(i, 0), Is.False, "빛나지 않는 칸은 받지 않는다");
                Assert.That(party.Held, Is.EqualTo(PartyScreen.Hold.Move), "받지 않은 뒤에도 카드는 집힌 채다");
                Assert.That(party.CanPut(0, 0), Is.True, "스타팅 포켓몬(팽도리)은 첫 카드(파도타기)를 배울 수 있다");
                Assert.That(party.PressSlot(0, 0), Is.True, "빈 칸에는 바로 가르친다");
                yield return null;
                Assert.That(client.View.Pokemon[starter].Moves[0], Is.EqualTo(first));
                Assert.That(party.Held, Is.EqualTo(PartyScreen.Hold.None));
                Assert.That(party.Asking, Is.False);

                // 찬 칸에 다른 기술: 한 번 더 묻는다 → 취소 / 가르치기
                int other = -1;
                for (int n = 0; n < party.Pool.Count && other < 0; n++)
                {
                    party.PickMove(n);
                    if (party.CanPut(0, 0)) other = n;
                    else party.Cancel();
                }
                Assert.That(other, Is.GreaterThanOrEqualTo(0), "스타팅 포켓몬이 배울 수 있는 다른 카드");
                int second = party.HeldValue;
                Assert.That(party.PressSlot(0, 0), Is.True);
                yield return null;
                Assert.That(party.Asking, Is.True);
                Assert.That(party.AskText, Does.Contain(stage.T("move." + ctx.Cards.Moves[first].Key)).And.Contain(stage.T("move." + ctx.Cards.Moves[second].Key)));
                Assert.That(party.AskText, Does.Not.Contain("("), "조사를 골랐다");
                Assert.That(stage.Root.Q("board-party-ask-yes"), Is.Not.Null);
                Assert.That(client.View.Pokemon[starter].Moves[0], Is.EqualTo(first), "묻는 동안에는 바뀌지 않는다");
                party.Cancel();
                yield return null;
                Assert.That(party.Asking, Is.False);
                Assert.That(stage.Root.Q("board-party-ask-yes"), Is.Null);
                party.PickMove(other);
                Assert.That(party.PressSlot(0, 0) && party.Confirm(), Is.True);
                yield return null;
                Assert.That(client.View.Pokemon[starter].Moves[0], Is.EqualTo(second), "밀려난 기술은 버린다(G10)");
                Assert.That(client.View.Seats[client.Seat].StampMoves.Contains(first), Is.False);

                // 잊게 하기: 아무것도 집지 않고 배운 기술을 누른다 → 묻는다
                Assert.That(party.PressSlot(0, 1), Is.False, "빈 칸은 누를 것이 없다");
                Assert.That(party.PressSlot(0, 0), Is.True);
                Assert.That(party.Asking, Is.True);
                Assert.That(party.AskText, Does.EndWith("잊게 하겠습니까?"));
                Assert.That(party.Confirm(), Is.True);
                yield return null;
                Assert.That(client.View.Pokemon[starter].Moves[0], Is.EqualTo(-1));

                // ── S29: 지닌물건을 지니게 하고, 다른 포켓몬으로 옮기고, 가방으로 돌려놓는다 ──
                int band = party.Bag[0];
                Assert.That(party.PickItem(0), Is.True);
                yield return null;
                for (int i = 0; i < 3; i++) Assert.That(stage.Root.Q("board-party-item-" + i).ClassListContains("sn-party-ok"), Is.True, "지닌물건 칸 " + i);
                Assert.That(party.PressItemSlot(1), Is.True);
                yield return null;
                Assert.That(client.View.Pokemon[me.Party[1]].HeldItem, Is.EqualTo(band));
                Assert.That(party.Bag.Count, Is.EqualTo(SoloTrail.KitItems.Length - 1));
                Assert.That(stage.Root.Q("board-party-item-1").userData, Is.EqualTo(stage.T(ctx.Items.Items[band].Key)), "카드의 지닌물건 동그라미(결정 206 ③)");
                Assert.That(stage.Root.Q("board-party-mon-1").Q("card-item-1"), Is.Not.Null, "카드에 지닌물건 토큰");
                Assert.That(party.PressItemSlot(1), Is.True, "지닌 것을 집는다");
                Assert.That(party.HeldFrom, Is.EqualTo(me.Party[1]));
                Assert.That(party.PressItemSlot(2), Is.True, "다른 포켓몬으로 옮긴다");
                yield return null;
                Assert.That(client.View.Pokemon[me.Party[1]].HeldItem, Is.EqualTo(-1));
                Assert.That(client.View.Pokemon[me.Party[2]].HeldItem, Is.EqualTo(band));
                Assert.That(party.PressBag(), Is.False, "집은 것이 없으면 가방은 받지 않는다");
                Assert.That(party.PressItemSlot(2) && party.PressBag(), Is.True, "가방으로");
                yield return null;
                Assert.That(client.View.Pokemon[me.Party[2]].HeldItem, Is.EqualTo(-1));
                Assert.That(party.Bag.Count, Is.EqualTo(SoloTrail.KitItems.Length));

                // ── 순서 바꾸기 ──
                int a = me.Party[0], b = me.Party[1];
                Assert.That(party.Shift(0, -1), Is.False);
                Assert.That(party.Shift(0, 1), Is.True);
                yield return null;
                Assert.That(client.View.Seats[client.Seat].Party[0], Is.EqualTo(b));
                Assert.That(client.View.Seats[client.Seat].Party[1], Is.EqualTo(a));
                Assert.That(stage.Root.Q("board-hud-mon-0").Q<Label>().text, Is.EqualTo(stage.T("pokemon." + ctx.Cards.Pokemon[client.View.Pokemon[b].Def].Key)), "아래 판의 파티도 새 순서다");

                // ── S38: 카드 확대 → 닫으면 정비 판으로 ──
                Assert.That(party.PressMon(1), Is.True);
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<MonScreen>());
                // 결정 206 ①(목업 B-party): 원작 카드 앞면 + 오른쪽 기둥(띠 · 지닌물건 글 상자) + 진화 줄 · [닫기]
                var zoom = (MonScreen)stage.Current;
                var zpk = client.View.Pokemon[a];
                Assert.That(zoom.Stack, Is.Not.Null, "원작 카드로 그린다");
                Assert.That(zoom.Stack.Art, Is.True, zoom.Stack.FaceKey);
                StringAssert.EndsWith("/" + ctx.Cards.Pokemon[zpk.Def].Key.Replace(':', '_') + ".png", zoom.Stack.FaceKey, "그 포켓몬의 카드");
                var zcol = zoom.Stack.Column;
                Assert.That(zcol, Is.Not.Null, "오른쪽 기둥");
                Assert.That(zcol.worldBound.xMin, Is.GreaterThanOrEqualTo(zoom.Stack.Root.Q("card-face").worldBound.xMax), "기둥은 카드 오른쪽");
                Assert.That(zoom.Stack.StripCount, Is.EqualTo(CardMon.Of(ctx, client.View, a).Slots.Count), "띠 = 배운 칸(빈 칸은 없다)");
                int zitems = (zpk.Alpha ? 1 : 0) + (zpk.HeldItem >= 0 ? 1 : 0) + (zpk.HeldItem2 >= 0 ? 1 : 0), znotes = 0;
                while (zoom.Stack.Root.Q("card-note-" + (znotes + 1)) != null) znotes++;
                Assert.That(znotes, Is.EqualTo(zitems), "지닌물건 글 상자 = 지닌물건 수");
                Assert.That(stage.Root.Q("mon-detail-move-0"), Is.Not.Null, "★ 고유 기술 자리");
                Assert.That(zoom.Stack.Root.Q("mon-detail-evolve"), Is.Not.Null, "진화 줄(카드에 없는 값)");
                Assert.That(stage.Root.Q("mon-detail-block").Q("mon-detail-close"), Is.Not.Null, "[닫기]는 카드 묶음 오른쪽 위(검토 지적 1)");
                var zbox = stage.Root.Q("mon-detail-block").worldBound;
                Assert.That(zbox.yMin >= stage.Root.worldBound.yMin - 0.5f && zbox.yMax <= stage.Root.worldBound.yMax + 0.5f, Is.True, "화면 안(" + zbox + ")");
                flow.CloseMon();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<PartyScreen>());
                party = (PartyScreen)stage.Current;

                // ── 놓아주기: 묻고, 마지막 한 마리는 놓아주지 못한다(p.27) ──
                Assert.That(party.PressRelease(2), Is.True);
                Assert.That(party.Asking, Is.True);
                Assert.That(party.AskText, Does.Contain("놓아주겠습니까?"));
                Assert.That(party.Confirm(), Is.True);
                yield return null;
                Assert.That(PokemonRules.PartyCount(client.View.Seats[client.Seat]), Is.EqualTo(2));
                Assert.That(party.PressRelease(1) && party.Confirm(), Is.True);
                yield return null;
                Assert.That(PokemonRules.PartyCount(client.View.Seats[client.Seat]), Is.EqualTo(1));
                Assert.That(party.PressRelease(0), Is.False, "마지막 포켓몬");
                Assert.That(stage.Root.Q("board-party-release-0").ClassListContains("sn-hud-off"), Is.True);
                Assert.That(stage.Root.Q("board-party-col-1").Q<Label>().text, Is.EqualTo("빈 칸"));

                // 기록 줄: 가르침 · 잊음 · 지님 · 놓아줌
                var keys = new List<string>();
                foreach (var line in flow.Log.Lines) keys.Add(line.Line.Key);
                foreach (string key in new[] { "taught", "forgot", "held", "unheld", "released" }) Assert.That(keys, Does.Contain("screen.log.line." + key));

                // 닫으면 보드 입력이 다시 열린다
                flow.CloseParty();
                yield return null;
                Assert.That(stage.Current, Is.Null);
                // 정비 이벤트도 화면 대기열을 지나간다(그릴 것은 없다) — 마지막 것 뒤의 클릭 막기 300ms가 지나면 열린다
                for (int i = 0; i < 20 && !flow.InputOpen; i++)
                {
                    flow.Skip(100);
                    yield return null;
                }
                Assert.That(flow.InputOpen, Is.True);
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }

        [UnityTest]
        public IEnumerator M4_정비판에서끌어놓으면두번누르기와같은명령이나가고빛나지않는곳에놓으면제자리로()
        {
            // 결정 70 ⑪ #42 「정비에 끌어 놓기도 넣는다」: 기술 카드·지닌물건을 끌어 빛나는 칸에 놓으면 두 번 누르기와 같은 정비가 나간다. 놓을 수 없는 곳이면 내려놓는다.
            // 화면의 끌기 함수(BeginDrag·DragTo·EndDrag — 포인터 처리기가 부르는 것과 같다)로 본다. 창 없는 시험은 패널에 포인터를 넣지 못한다(실제 마우스 길은 MouseProbe).
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
                Assert.That(boot != null && boot.Root != null, Is.True, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                var flow = boot.Flow;
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                FlowPilot.Start(flow, SoloTrail.Kit);
                yield return null;
                flow.Skip(flow.OpeningMs);
                yield return null;
                var client = flow.Client;
                Assert.That(flow.OpenParty(), Is.True, "정비 판");
                yield return null;
                yield return null;
                var party = (PartyScreen)stage.Current;
                int starter = client.View.Seats[client.Seat].Party[0];
                var ghost = stage.Root.Q("board-party-ghost");
                Assert.That(ghost, Is.Not.Null);
                Assert.That(ghost.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "끌기 전에는 따라오는 카드가 없다");

                // ── 기술 카드: 끌기 시작 = 집힌다(빛나는 칸) + 이름표가 포인터를 따라온다 ──
                int commands = flow.Session.CommandCount;
                Assert.That(party.BeginDrag(PartyScreen.Hold.Move, 0), Is.True);
                yield return null;
                Assert.That(party.Dragging, Is.True);
                Assert.That(party.Held, Is.EqualTo(PartyScreen.Hold.Move));
                int first = party.HeldValue;
                Assert.That(ghost.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                // 결정 73 ④ · 결정 206 ③: 그 기술 띠(포켓몬 카드 뒷면의 홈 ~ 아래 끝 — 원작 그림)가 판의 카드와 같은 폭으로 따라온다
                var ghostCard = ghost.Q(className: "sn-pcard-ghost");
                Assert.That(ghostCard, Is.Not.Null, "따라오는 것 = 기술 띠");
                var stripTex = GenAssets.Load<Texture2D>(GenAssets.CardMoves + "ko/" + SoloTrail.KitMoves[0] + ".png");
                Assert.That(stripTex, Is.Not.Null, "기술 띠 그림(Gen Cards/Moves)");
                Assert.That(ghostCard.resolvedStyle.backgroundImage.texture, Is.SameAs(stripTex), "끄는 기술의 띠 그림");
                Assert.That(ghostCard.worldBound.width, Is.EqualTo(stage.Root.Q("board-party-mon-0").worldBound.width).Within(1f), "판의 카드와 같은 폭");
                Assert.That(ghost.pickingMode, Is.EqualTo(PickingMode.Ignore));
                Assert.That(ghostCard.pickingMode, Is.EqualTo(PickingMode.Ignore), "따라오는 카드는 마우스를 받지 않는다(놓을 칸을 가리지 않게)");
                var slot = stage.Root.Q("board-party-slot-0-1");
                Assert.That(slot.ClassListContains("sn-party-ok"), Is.True, "스타팅 포켓몬의 빈 칸이 빛난다");
                Assert.That(slot.parent.ClassListContains("sn-card-hole"), Is.True, "빈 칸 = 끄는 동안만 서는 홈");
                Assert.That(slot.parent.worldBound.height, Is.EqualTo(Mathf.Round(slot.parent.worldBound.width * CardStack.MoveStripPx / (CardStack.WidthCm * CardStack.PxPerCm))).Within(1f), "홈 = 기술 띠 크기");
                Assert.That(party.BeginDrag(PartyScreen.Hold.Move, 1), Is.False, "끄는 중에는 다른 것을 끌지 못한다");
                Vector2 mid = slot.worldBound.center + new Vector2(-40f, 60f);
                party.DragTo(mid);
                yield return null;
                Assert.That(party.GhostAt, Is.EqualTo(mid));
                Assert.That(Vector2.Distance(ghost.worldBound.position, mid), Is.LessThan(40f), "카드는 포인터 곁에 있다");

                // 놓을 수 없는 곳(창 밖 귀퉁이)에 놓으면 제자리로: 명령이 나가지 않고 집은 것을 내려놓는다
                Assert.That(party.EndDrag(new Vector2(3f, 3f)), Is.False);
                yield return null;
                Assert.That(party.Dragging, Is.False);
                Assert.That(party.Held, Is.EqualTo(PartyScreen.Hold.None), "내려놓았다");
                Assert.That(ghost.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "명령 없음");
                Assert.That(party.Pool, Does.Contain(first), "카드는 풀에 그대로");
                Assert.That(client.View.Pokemon[starter].Moves[0], Is.LessThan(0));

                // 빛나는 칸에 놓으면 그 칸을 누른 것과 같은 정비(가르치기)가 나간다
                Assert.That(party.BeginDrag(PartyScreen.Hold.Move, 0), Is.True);
                yield return null;
                slot = stage.Root.Q("board-party-slot-0-1");
                party.DragTo(slot.worldBound.center);
                Assert.That(party.EndDrag(slot.worldBound.center), Is.True);
                yield return null;
                Assert.That(client.View.Pokemon[starter].Moves[0], Is.EqualTo(first), "끌어 놓아 가르쳤다");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1));
                Assert.That(party.Held, Is.EqualTo(PartyScreen.Hold.None));
                Assert.That(party.Dragging, Is.False);
                flow.Skip(1000);
                yield return null;

                // ── 지닌물건: 가방에서 끌어 지닌물건 칸에 ──
                Assert.That(party.BeginDrag(PartyScreen.Hold.Item, 0), Is.True);
                yield return null;
                int item = party.HeldValue;
                var itemSlot = stage.Root.Q("board-party-item-0");
                Assert.That(itemSlot.ClassListContains("sn-party-ok"), Is.True);
                // 검토 지적 14: 카드 그림 판에서는 카드 동그라미에 얹히는 토큰 그림 하나가 따라온다(가방 칩이 카드 타입 줄 위에 얹히지 않게)
                var ghostToken = ghost.Q(className: "sn-pcard-ghost");
                Assert.That(ghostToken, Is.Not.Null, "따라오는 것 = 지닌물건 토큰");
                Assert.That(ghostToken.style.backgroundImage.value.texture, Is.EqualTo(GenAssets.Load<Texture2D>(CardStack.TokenOrIcon(flow.Ctx, item))), "그 지닌물건의 토큰 그림");
                Assert.That(ghost.Q(className: "sn-party-chip"), Is.Null, "가방 칩이 아니다");
                Assert.That(itemSlot.ClassListContains("sn-pcard-item") && stage.Root.Q("board-party-mon-0").Q("card-face").worldBound.Contains(itemSlot.worldBound.center), Is.True, "받는 자리 = 카드의 지닌물건 동그라미");
                Assert.That(party.EndDrag(itemSlot.worldBound.center), Is.True);
                yield return null;
                Assert.That(client.View.Pokemon[starter].HeldItem, Is.EqualTo(item), "끌어 놓아 지니게 했다");
                flow.Skip(1000);
                yield return null;

                // 포켓몬이 지닌 것을 끌어 가방에 놓으면 가방으로 돌아간다(가방이 빛난다)
                Assert.That(party.BeginDrag(PartyScreen.Hold.Item, 0, true), Is.True);
                yield return null;
                Assert.That(party.HeldFrom, Is.EqualTo(starter));
                Assert.That(ghost.Q(className: "sn-pcard-ghost").style.backgroundImage.value.texture, Is.EqualTo(GenAssets.Load<Texture2D>(CardStack.TokenOrIcon(flow.Ctx, item))), "포켓몬이 지닌 것도 토큰이 따라온다");
                var bagBox = stage.Root.Q("board-party-bag");
                Assert.That(bagBox.ClassListContains("sn-party-ok"), Is.True);
                Assert.That(party.EndDrag(bagBox.worldBound.center), Is.True);
                yield return null;
                Assert.That(client.View.Pokemon[starter].HeldItem, Is.LessThan(0), "가방으로 돌아갔다");
                flow.Skip(1000);
                yield return null;

                // 두 번 누르기는 그대로다: 누르면 집히고(끄는 중이 아니다), 다시 누르면 내려놓는다
                Assert.That(party.PickItem(0), Is.True);
                Assert.That(party.Held, Is.EqualTo(PartyScreen.Hold.Item));
                Assert.That(party.Dragging, Is.False);
                Assert.That(party.EndDrag(itemSlot.worldBound.center), Is.False, "끌지 않았으면 놓기도 없다");
                Assert.That(party.Held, Is.EqualTo(PartyScreen.Hold.Item));
                Assert.That(party.PickItem(0), Is.True);
                Assert.That(party.Held, Is.EqualTo(PartyScreen.Hold.None));

                // 끄는 중에 판이 닫히면 끌기도 끝난다
                Assert.That(party.BeginDrag(PartyScreen.Hold.Move, 0), Is.True);
                flow.CloseParty();
                yield return null;
                Assert.That(party.Dragging, Is.False);
                Assert.That(stage.Current, Is.Null);
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }

        [UnityTest]
        public IEnumerator M4_배틀독의정비로배틀화면위에정비판을열어기술을가르치고닫으면배틀로돌아오며연출중에는꺼진다_G10()
        {
            // 배틀 중 정비(04 §3-5, rulings G10): 배틀 참가 중에도 「언제든」 정비(순서 · 기술 가르치기)는 내 공격을 처리하는 동안이 아니면 받는다 — 놓아주기·지닌물건은 배틀 밖에서만.
            // 판은 기술 카드·지닌물건을 쥐여 준 판(SoloTrail.Kit)의 첫 야생 배틀(파티 하나)
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
                Assert.That(boot != null && boot.Root != null, Is.True, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                var flow = boot.Flow;
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                var pilot = FlowPilot.Start(flow, SoloTrail.Kit);
                yield return null;
                var client = flow.Client;
                bool MyMove() => stage.Current is BattleScreen bs && bs.Open && bs.Moves.Count > 0;
                for (int guard = 0; guard < 6000 && !MyMove(); guard++)
                {
                    if (!FlowPilot.Step(flow, pilot)) flow.Skip(500);
                    yield return null;
                }
                Assert.That(MyMove(), Is.True, "첫 야생 배틀의 내 차례");
                var battle = (BattleScreen)stage.Current;
                int starter = client.View.Seats[client.Seat].Party[0];

                // ── 독의 [정비]: 켜져 있고(흐린 단추가 아님) T로도 연다 — 배틀 화면은 그대로, 그 위에 정비 판 ──
                Assert.That(battle.MaintOn, Is.True);
                Assert.That(flow.CanOpenParty, Is.True);
                var handle = stage.Root.Q<Button>("battle-maint");
                Assert.That(handle, Is.Not.Null);
                Assert.That(handle.ClassListContains(UiStage.OffClass), Is.False);
                Assert.That(handle.text, Is.EqualTo("정비"));
                flow.ToggleParty();
                yield return null;
                Assert.That(stage.Current, Is.SameAs(battle), "배틀 화면은 닫히지 않는다");
                var party = flow.Top as PartyScreen;
                Assert.That(party, Is.Not.Null, "배틀 화면 위에 정비 판");
                Assert.That(battle.Over, Is.SameAs(party));
                Assert.That(party.Mine, Is.True);
                Assert.That(stage.Root.Q<Button>("board-party-close").text, Is.EqualTo("← 배틀로"));
                Assert.That(flow.OpenTrainer(), Is.True, "배틀 위 정비 판 위에도 트레이너 카드는 정보 창 층에(결정 125)");
                Assert.That(stage.Info, Is.InstanceOf<TrainerScreen>());
                flow.CloseTrainer();
                Assert.That(stage.Info, Is.Null);
                Assert.That(battle.Over, Is.SameAs(party), "정비 판은 그대로");
                yield return null;
                // 판은 독 위에서 끝나고(겹치지 않는다), 판 아래로 보이는 독의 단추 자리는 어둠(판의 바탕)이 받는다 — 독 단추는 눌리지 않는다
                var dockBox = stage.Root.Q("battle-dock").worldBound;
                Assert.That(stage.Root.Q("board-party-panel").worldBound.yMax, Is.LessThanOrEqualTo(dockBox.yMin), "정비 판이 독을 덮지 않는다");
                var scrim = stage.Root.Q("board-party-scrim");
                Assert.That(scrim.worldBound.yMax, Is.GreaterThanOrEqualTo(dockBox.yMax), "어둠이 독까지 덮는다");
                foreach (string name in new[] { "battle-maint", "battle-move-1", "battle-bag", "battle-forfeit" })
                {
                    var under = stage.Root.Q(name);
                    Assert.That(under, Is.Not.Null, name);
                    Assert.That(stage.Root.panel.Pick(under.worldBound.center), Is.SameAs(scrim), "판이 열린 동안 독의 " + name + " 자리를 누르면 어둠이 받는다");
                }
                Assert.That(party.Pool.Count, Is.EqualTo(SoloTrail.KitMoves.Length));

                // 지닌물건은 배틀 밖에서만(엔진 검사 그대로 — 집어도 넣을 칸이 빛나지 않는다)
                Assert.That(party.PickItem(0), Is.True);
                var hold = new Maintenance { Kind = MaintKind.HoldItem, Pokemon = starter, Value = party.HeldValue };
                Assert.That(flow.CanMaintain(hold), Is.EqualTo(RejectReason.Timing));
                Assert.That(party.CanPut(0), Is.False);
                Assert.That(stage.Root.Q("board-party-item-0").ClassListContains("sn-party-no"), Is.True);
                party.Cancel();

                // 기술 가르치기: 빈 칸에 바로 — 판은 떠 있는 채, 배틀의 기술 줄이 다시 열린 선택으로 바뀐다
                Assert.That(party.PickMove(0), Is.True);
                int surf = party.HeldValue;
                Assert.That(party.CanPut(0, 0), Is.True, "스타팅 포켓몬(팽도리)은 배틀 중에도 첫 카드(파도타기)를 배울 수 있다");
                Assert.That(party.PressSlot(0, 0), Is.True);
                yield return null;
                Assert.That(client.View.Pokemon[starter].Moves[0], Is.EqualTo(surf));
                Assert.That(flow.Top, Is.SameAs(party), "정비의 항목(길이 0)은 판을 닫지 않는다");

                // 그 아래에서 연출이 시작되면 판이 닫힌다(보이지 않는 곳에서 배틀이 흘러가지 않게)
                flow.Fx.Enqueue(new[] { new Sinnoh.Fx.FxEntry { Seq = 900100, QueueMs = 400 } });
                stage.Tweens.Advance(16);
                yield return null;
                Assert.That(flow.Top, Is.SameAs(battle), "연출이 시작되면 배틀로 돌아온다");
                Assert.That(battle.Over, Is.Null);
                Assert.That(flow.OpenParty(), Is.True, "연출 중에는 보기만 하는 판으로 열린다(결정 125)");
                Assert.That(stage.Info is PartyScreen peek && peek.Lock == "screen.peek.lock.battle" && battle.Over == null, Is.True, "정보 창 층 · 잠김");
                Assert.That(stage.Root.Q("board-party-ask-yes"), Is.Null);
                flow.CloseParty();
                Assert.That(stage.Info, Is.Null);
                Assert.That(flow.Top, Is.SameAs(battle));
                yield return null;
                Assert.That(stage.Root.Q<Button>("battle-maint").ClassListContains(UiStage.OffClass), Is.True, "연출 중의 [정비]는 흐린 단추");
                for (int i = 0; i < 20 && !battle.Open; i++)
                {
                    flow.Skip(100);
                    yield return null;
                }
                Assert.That(battle.Open, Is.True);

                // 열고 [← 배틀로]로 닫으면 배틀로 돌아오고, 기술 줄에 가르친 기술이 있다
                Assert.That(flow.OpenParty(), Is.True);
                yield return null;
                flow.CloseParty();
                yield return null;
                Assert.That(flow.Top, Is.SameAs(battle));
                Assert.That(battle.Moves.Count(a => a.Target == surf), Is.EqualTo(1), "다시 열린 배틀 선택에 가르친 기술");

                // 연출 「끄기」: 받은 항목이 한 프레임에 다 지나가도(재생 중인 항목이 화면에 보이지 않는다) 판은 닫힌다
                var speed = flow.FxSpeed;
                flow.FxSpeed = Sinnoh.Fx.FxSpeed.Off;
                try
                {
                    Assert.That(flow.OpenParty(), Is.True);
                    yield return null;
                    Assert.That(battle.Over, Is.Not.Null);
                    flow.Fx.Enqueue(new[] { new Sinnoh.Fx.FxEntry { Seq = 900101, QueueMs = 400 } });
                    stage.Tweens.Advance(16);
                    yield return null;
                    Assert.That(flow.Fx.Sequencer.IsEmpty, Is.True, "끄기는 한 번에 다 보낸다");
                    Assert.That(flow.Top, Is.SameAs(battle), "끄기에서도 연출이 오면 배틀로 돌아온다");
                    Assert.That(battle.Over, Is.Null);
                }
                finally
                {
                    flow.FxSpeed = speed;
                }
                for (int i = 0; i < 20 && !battle.Open; i++)
                {
                    flow.Skip(100);
                    yield return null;
                }
                Assert.That(battle.Open, Is.True);

                // 기술을 고르면 내 공격의 연출 · 처리 동안 [정비]는 꺼진다
                Assert.That(battle.PressMove(battle.Moves.ToList().FindIndex(a => a.Target == surf)), Is.True);
                flow.Skip(100);
                yield return null;
                Assert.That(battle.MaintOn, Is.False);
                Assert.That(flow.CanOpenParty, Is.False);
                Assert.That(stage.Root.Q<Button>("battle-maint").ClassListContains(UiStage.OffClass), Is.True);
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var b = Object.FindAnyObjectByType<ScreenBoot>();
                if (b != null && b.Root != null) b.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }
        /// <summary>
        /// 결정 195(11-21 「비스트 부스트나 알파스트라이크 있는 칸이면 빈칸으로 표시하지 마」): 전설 특수기술을 가진 포켓몬의 빈 기술 칸은 「빈 칸」이 아니라
        /// 알파 스트라이크(칸 1 — 배우는 타입 그림) · 비스트 부스트(칸 2)로 보인다 — 정비 판 · 기술 카드를 집은 가르치기 · 카드 확대(MonScreen).
        /// </summary>
        [UnityTest]
        public IEnumerator M10_전설특수기술이차지한빈칸은알파스트라이크와비스트부스트로보인다_결정195()
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
                Assert.That(boot != null && boot.Root != null, Is.True, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                var flow = boot.Flow;
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                FlowPilot.Start(flow, SoloTrail.Kit);
                yield return null;
                flow.Skip(flow.OpeningMs);
                yield return null;
                int seat = flow.Client.Seat, starter = flow.Client.View.Seats[seat].Party[0];
                flow.Rebase(st =>
                {
                    var pk = st.Pokemon[starter];
                    pk.HasLegendaryMove = true;
                    for (int k = 0; k < pk.Moves.Length; k++) pk.Moves[k] = -1;
                });
                yield return null;
                flow.Skip(flow.OpeningMs);
                yield return null;
                var pkv = flow.Client.View.Pokemon[starter];
                Assert.That(LegendRules.TacticAt(pkv, 0), Is.EqualTo(LegendTactic.AlphaStrike), "엔진: 칸 1 = 알파 스트라이크");
                string alpha = stage.T("screen.battle.move.alpha_strike"), beast = stage.T("screen.passive.beast_boost");
                var learn = new List<int>();
                LegendRules.AlphaTypes(flow.Ctx, pkv, learn);

                void Slot(VisualElement row, string name, string where)
                {
                    Assert.That(row, Is.Not.Null, where);
                    Assert.That(row.ClassListContains(MonParts.LegendSlotClass) || row.Q(className: MonParts.LegendSlotClass) != null, Is.True, where + ": 전설 특수기술 칸");
                    // 정비 판(결정 206 ③) = 카드 밑 전설 전술 판의 상자(이름 = 그 자리의 툴팁 머리) · 카드 확대 = 줄 글
                    Assert.That(row.userData as string == name || row.Query<Label>().ToList().Exists(l => l.text == name), Is.True, where + ": 「" + name + "」");
                    Assert.That(row.Query<Label>().ToList().Exists(l => l.text == stage.T("screen.party.slot.empty")), Is.False, where + ": 「빈 칸」이 아니다");
                }

                // 정비 판
                for (int i = 0; i < 300 && !flow.CanOpenParty; i++)
                {
                    flow.Skip(100);
                    yield return null;
                }
                Assert.That(flow.OpenParty(), Is.True, "정비 판");
                yield return null;
                yield return null;
                var party = (PartyScreen)stage.Current;
                Slot(stage.Root.Q("board-party-slot-0-1"), alpha, "정비 판 칸 1");
                Slot(stage.Root.Q("board-party-slot-0-2"), beast, "정비 판 칸 2");

                // 기술 카드를 집어 가르칠 칸을 고르는 중에도 그대로(가르치면 덮인다 — 마우스 오버)
                Assert.That(party.PickMove(0), Is.True, "기술 카드를 집는다");
                yield return null;
                Slot(stage.Root.Q("board-party-slot-0-1"), alpha, "가르치기 칸 1");

                // 카드 확대
                party.PickMove(0);
                yield return null;
                Assert.That(flow.OpenMon(starter, seat), Is.True, "카드 확대");
                yield return null;
                yield return null;
                // 결정 206 ①: 카드 확대 = 원작 카드 — 오른쪽 기둥의 첫 띠가 전설 전술 판 한 장(알파 스트라이크 + 배울 수 있는 타입 그림 · 비스트 부스트)
                var zoom = (MonScreen)stage.Current;
                Assert.That(zoom.Stack != null && zoom.Stack.Art, Is.True, "원작 카드");
                Assert.That(zoom.Stack.StripCount, Is.EqualTo(1), "전술 판 한 장");
                Assert.That(zoom.Stack.Column.Q("card-strip-1"), Is.Not.Null);
                int types = 0;
                while (zoom.Stack.Column.Q("card-alpha-type-" + (types + 1)) != null) types++;
                Assert.That(types, Is.EqualTo(learn.Count), "알파 스트라이크 = 배우는 타입 그림");
                Assert.That(zoom.Stack.Parts.Any(p => p.Kind == CardPartKind.AlphaStrike && p.Title == alpha), Is.True, "알파 스트라이크 자리(효과 글 툴팁)");
                Assert.That(zoom.Stack.Parts.Any(p => p.Kind == CardPartKind.BeastBoost && p.Title == beast), Is.True, "비스트 부스트 자리");
                Assert.That(errors, Is.Empty, string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var b = Object.FindAnyObjectByType<ScreenBoot>();
                if (b != null && b.Root != null) b.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }

        [UnityTest]
        public IEnumerator M10_결정180_독걸린포켓몬은아래판파티칸과정비판에독아이콘이서고마을회복뒤에는사라진다()
        {
            // 결정 180(2026-11-21 「정비창 안 연 상태에서 하단에 내포켓몬들 상태에서 상태이상도 보여야 해」): 배틀 밖에도 남는 상태 이상을 아래 판 파티 칸에 배틀과 같은 그림으로.
            // 판 = SoloTrail.Kit의 첫 턴(고향 마을) — 스타팅 포켓몬에 독을 건 뒤 [마을] → 전체 회복
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
                Assert.That(boot != null && boot.Root != null, Is.True, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                var flow = boot.Flow;
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                FlowPilot.Start(flow, SoloTrail.Kit);
                yield return null;
                flow.Rebase(st => st.Pokemon[st.Seats[0].Party[0]].Status = StatusEffect.Poisoned);
                for (int guard = 0; guard < 60 && !flow.InputOpen; guard++)
                {
                    flow.Skip(500);
                    yield return null;
                }
                Assert.That(flow.InputOpen, Is.True, "첫 턴의 입력");
                var client = flow.Client;
                int starter = client.View.Seats[client.Seat].Party[0];
                Assert.That(client.View.Pokemon[starter].Status, Is.EqualTo(StatusEffect.Poisoned));

                // 아래 판 파티 칸: 독 그림 하나(배틀과 같은 IconArt 키) · 마우스 오버 = 이름 + 효과
                var icon = stage.Root.Q("board-hud-mon-0-status-poisoned");
                Assert.That(icon, Is.Not.Null, "파티 칸의 독 아이콘");
                Assert.That(stage.Root.Q("board-hud-mon-0-status").Query(className: "sn-status-icon").ToList().Count, Is.EqualTo(1), "걸린 상태 하나 = 아이콘 하나");
                Assert.That(icon.style.backgroundImage.value.texture, Is.SameAs(GenAssets.Load<Texture2D>(IconArt.Status(StatusEffect.Poisoned))), "배틀 상태 배지와 같은 그림");
                stage.Tips.Show(icon);
                var tipText = stage.Tips.Element.Query<Label>().ToList().ConvertAll(l => l.text.Replace("⁠", ""));
                Assert.That(tipText, Does.Contain(stage.T("screen.status.poisoned")), "툴팁 머리 = 상태 이름");
                Assert.That(tipText.Exists(t => t.Contains(stage.T("screen.status.poisoned.tip").Replace("⁠", ""))), Is.True, "툴팁 본문 = 짧은 효과: " + string.Join(" | ", tipText));
                stage.Tips.Hide();

                // 정비 판(S18)의 같은 카드에도 같은 그림
                Assert.That(flow.OpenParty(), Is.True);
                yield return null;
                Assert.That(stage.Root.Q("board-party-status-0-poisoned"), Is.Not.Null, "정비 판 카드의 독 아이콘");
                flow.CloseParty();
                yield return null;

                // [마을] → 전체 회복(상태 이상도 풀린다, S3_3): 칸의 아이콘이 사라진다
                Assert.That(flow.OpenVillage(), Is.True, "고향 마을의 마을 창");
                var village = flow.Top as VillageScreen;
                Assert.That(village, Is.Not.Null);
                Assert.That(village.PressHeal(), Is.True, "전체 회복");
                for (int guard = 0; guard < 40 && client.View.Pokemon[starter].Status != StatusEffect.None; guard++)
                {
                    flow.Skip(250);
                    yield return null;
                }
                flow.Skip(3000);
                yield return null;
                Assert.That(client.View.Pokemon[starter].Status, Is.EqualTo(StatusEffect.None), "회복으로 독이 풀렸다");
                Assert.That(stage.Root.Q("board-hud-mon-0-status"), Is.Null, "회복 뒤에는 파티 칸에 상태 아이콘이 없다");
                Assert.That(stage.Root.Q("board-hud-mon-0"), Is.Not.Null, "파티 칸은 그대로");
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var b = Object.FindAnyObjectByType<ScreenBoot>();
                if (b != null && b.Root != null) b.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }
    }
}
