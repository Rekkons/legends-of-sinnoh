using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M5 마감 답 반영 마(결정 80 N2 나 — 여럿 판에서만 나오는 것을 견본 판으로): 좌석 여럿인 확인 판(<see cref="GameFlow.StartSeats"/> — 한 PC의 LocalSession, 남 좌석은 확인용 답)에서
    /// ① 남 좌석 정비 판(S39)의 「여행 스탬프 n/7」 → 그 좌석의 스탬프 판 ② 남 좌석의 제작·공물이 그 좌석 칩의 꼬리표로 보인다
    /// ③ 거래 창의 여행 점수 [+5점]. 화면 요소·화면 함수로만 누른다.
    /// </summary>
    public sealed class SeatsFlowTests
    {
        const int W = 1920, H = 1080;

        ScreenBoot boot;
        GameFlow flow;
        UiStage stage;
        RenderTexture rt;
        readonly List<string> errors = new List<string>();

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
        }

        IEnumerator Boot()
        {
            errors.Clear();
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
            flow = boot.Flow;
            stage = boot.Root.Stage;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
        }

        IEnumerator Settle(Func<bool> until = null)
        {
            for (int i = 0; i < 400 && ((flow.Fx == null || !flow.Fx.Sequencer.IsEmpty) || (until != null && !until())); i++)
            {
                flow.Skip(100);
                yield return null;
            }
            flow.Skip(400);
            yield return null;
            yield return null;
        }

        [TearDown]
        public void Shut()
        {
            Application.logMessageReceived -= Grab;
            var b = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
            if (b != null && b.Flow != null) b.Flow.HostPaced = false;
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                rt = null;
            }
        }

        // 좌석 여럿 확인 판을 열고 내 턴(입력이 열린 때)까지 넘긴다
        IEnumerator Seats(int seats, Action<GameState> prepare, int[] clockMs = null)
        {
            yield return Boot();
            flow.StartSeats(new SoloSettings(), seats, 20261116, 7, prepare, clockMs: clockMs);
            for (int i = 0; i < 6; i++) yield return null;
            yield return Settle();
            for (int k = 0; k < seats * 2 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                Assert.That(flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == AnswerKind.EndTurn), Is.True, "남 좌석의 턴 종료");
                yield return Settle();
            }
            yield return Settle(() => flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null);
            Assert.That(flow.Client.View.ActiveSeat, Is.EqualTo(flow.Client.Seat), "내 턴");
        }

        // 도장 덮개(47행)가 떠 있는 동안 왼쪽 위 얼굴 상자 · 이름 줄이 그 좌석이었나(결정 202)
        IEnumerator StampFaceOf(int seat, Action<bool> seen)
        {
            bool shown = false, faced = true;
            for (int guard = 0; guard < 400; guard++)
            {
                flow.Skip(50);
                yield return null;
                if (flow.Deals == null || !flow.Deals.StampShown) continue;
                shown = true;
                var face = stage.Root.Q("deal-stamp-face");
                if (FaceBox.Shown(face) != flow.Protagonist(seat) || !FaceBox.SeatColorOf(face).Equals(flow.SeatColor(seat))
                    || stage.Root.Q<Label>("deal-stamp-owner-name").text != stage.T("screen.stamp.fx.owner").Replace("{name}", flow.SeatName(seat))) faced = false;
                if (flow.Deals.RewardsShown > 0) break;
            }
            seen(shown && faced);
        }

        [UnityTest]
        public IEnumerator M10_도장덮개왼쪽위에스탬프를받은좌석의얼굴이선다_결정202()
        {
            yield return Seats(2, null);
            int me = flow.Client.Seat, other = 1 - me;
            Assert.That(flow.Protagonist(me), Is.Not.EqualTo(flow.Protagonist(other)), "좌석마다 다른 주인공");
            // ① 남의 턴에 찍힌 내 스탬프: 좌석 2의 턴에 내가 규토리 일곱 색을 쥐고 → 좌석 2가 한 칸 걸은 명령 끝에 내 스탬프
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");
            yield return Settle(() => flow.Client.View.ActiveSeat == other && flow.BoardOpen);
            flow.Rebase(st =>
            {
                for (int c = 0; c < st.Seats[me].Apricorns.Length; c++) st.Seats[me].Apricorns[c] = Math.Max(1, st.Seats[me].Apricorns[c]);
            });
            for (int i = 0; i < 4; i++) yield return null;
            Assert.That(flow.AnswerSeat(other, a => a.Kind == AnswerKind.Move), Is.True, "좌석 2가 걷는다");
            bool mine = false;
            yield return StampFaceOf(me, ok => mine = ok);
            Assert.That(flow.Client.View.ActiveSeat, Is.EqualTo(other), "남의 턴");
            Assert.That(mine, Is.True, "남의 턴에 찍힌 내 스탬프 덮개 — 얼굴 · 이름 = 나");
            yield return Settle();
            // ② 방장 시각 판의 남의 스탬프 덮개: 얼굴 · 이름 = 그 좌석
            flow.Rebase(st =>
            {
                for (int c = 0; c < st.Seats[other].Apricorns.Length; c++) st.Seats[other].Apricorns[c] = Math.Max(1, st.Seats[other].Apricorns[c]);
            });
            flow.HostPaced = true;
            for (int i = 0; i < 4; i++) yield return null;
            Assert.That(flow.AnswerSeat(other, a => a.Kind == AnswerKind.EndTurn), Is.True, "좌석 2의 턴 종료");
            bool theirs = false;
            yield return StampFaceOf(other, ok => theirs = ok);
            Assert.That(theirs, Is.True, "남의 스탬프 덮개 — 얼굴 · 이름 = 그 좌석");
            flow.HostPaced = false;
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M5_남좌석의정비판에서그좌석의스탬프판을연다()
        {
            yield return Seats(2, st =>
            {
                st.Seats[1].Stamps[StampRules.TrainerWin] = true;
                st.Seats[1].Stamps[StampRules.HeldItems] = true;
            });
            int me = flow.Client.Seat, other = 1 - me;
            Assert.That(flow.OpenParty(other), Is.True, "남 좌석의 판(S39)");
            yield return null;
            yield return null;
            var line = stage.Root.Q<Label>("board-party-stamps");
            Assert.That(line, Is.Not.Null, "정비 판의 「여행 스탬프 n/7」");
            int expected = other == 1 ? 2 : 0;
            Assert.That(line.text, Is.EqualTo(stage.T("screen.info.stamps").Replace("{n}", expected.ToString())));
            Assert.That(flow.OpenSeatStamp(other), Is.True, "누르면 그 좌석의 스탬프 판");
            yield return null;
            yield return null;
            var sheet = stage.Current as StampScreen;
            Assert.That(sheet, Is.Not.Null, "스탬프 판(S37)");
            Assert.That(sheet.Seat, Is.EqualTo(other));
            Assert.That(sheet.Count, Is.EqualTo(expected));
            Assert.That(stage.Root.Q<Label>("board-stamp-title").text, Is.EqualTo(stage.T("screen.stamp.title.seat").Replace("{name}", flow.SeatName(other)).Replace("{n}", expected.ToString())), "제목에 그 좌석 이름");
            Assert.That(stage.Root.Q("board-stamp-slot-" + (StampRules.TrainerWin + 1)).ClassListContains("sn-stamp-slot-on"), Is.EqualTo(other == 1), "그 좌석이 받은 칸");
            Assert.That(WorkUi.Overflows(stage.Root.Q("board-stamp")), Is.Empty, "남의 스탬프 판 글이 넘치지 않는다");
            Assert.That(flow.Escape(), Is.True, "Esc로 닫힌다");
            yield return null;
            Assert.That(stage.Current, Is.Null);
            Assert.That(flow.OpenSeatStamp(other), Is.False, "정비 판 밖에서는 열지 않는다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M5_남좌석의제작과공물은그좌석칩의꼬리표로보인다()
        {
            yield return Seats(2, st =>
            {
                for (int s = 0; s < 2; s++)
                {
                    for (int c = 0; c < st.Seats[s].Apricorns.Length; c++) st.Seats[s].Apricorns[c] = 3;
                    st.Seats[s].Jp = 20;
                }
            });
            int me = flow.Client.Seat, other = 1 - me;
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");
            yield return Settle(() => flow.Client.View.ActiveSeat == other && flow.BoardOpen);
            var events = new List<GameEvent>();
            flow.EventsSeen += events.AddRange;

            // 남 좌석의 제작: 그 단계 동안 그 좌석 칩 아래에 「제작」 꼬리표(창은 열리지 않는다)
            Assert.That(flow.AnswerSeat(other, a => a.Kind == AnswerKind.Craft), Is.True, "남 좌석의 제작");
            bool tagged = false, mineTagged = false, clean = true;
            string text = null;
            for (int guard = 0; guard < 300; guard++)
            {
                flow.Skip(50);
                yield return null;
                if (flow.Hud.DidSeat == other && flow.Hud.DidKey == "craft")
                {
                    var tag = stage.Root.Q<Label>(BoardHud.DidName(other));
                    if (tag != null && tag.resolvedStyle.opacity > 0.9f)
                    {
                        tagged = true;
                        text = tag.text;
                        var chip = stage.Root.Q("board-hud-seat-" + (other + 1)).worldBound;
                        if (!(tag.worldBound.xMin >= chip.xMin - 0.5f && tag.worldBound.xMax <= chip.xMax + 0.5f)) clean = false;
                        if (WorkUi.Overflows(stage.Root.Q("board-hud-seats")).Count > 0) clean = false;
                    }
                }
                if (stage.Root.Q(BoardHud.DidName(me)) != null) mineTagged = true;
                if (stage.Current != null) clean = false;
                if (tagged && flow.Fx.Sequencer.IsEmpty) break;
            }
            Assert.That(events.Exists(e => e is Crafted c && c.Seat == other), Is.True, "제작했다");
            Assert.That(tagged, Is.True, "그 좌석 칩의 「제작」 꼬리표");
            Assert.That(text, Is.EqualTo(stage.T("screen.board.did.craft")));
            Assert.That(clean, Is.True, "꼬리표가 칩 폭 안 · 글이 넘치지 않음 · 창이 열리지 않음");
            Assert.That(mineTagged, Is.False, "내 칩에는 꼬리표가 없다");
            yield return Settle();
            Assert.That(stage.Root.Q(BoardHud.DidName(other)), Is.Null, "단계가 끝나면 꼬리표가 걷힌다");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M9_남의제작은내창과같은보기창으로보이고_내창을열면숨고_끝나면닫힌다_결정120()
        {
            yield return Seats(2, st =>
            {
                for (int s = 0; s < 2; s++)
                {
                    for (int c = 0; c < st.Seats[s].Apricorns.Length; c++) st.Seats[s].Apricorns[c] = 3;
                    st.Seats[s].Jp = 20;
                }
            });
            int me = flow.Client.Seat, other = 1 - me;
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");
            yield return Settle(() => flow.Client.View.ActiveSeat == other && flow.BoardOpen);
            Assert.That(flow.AnswerSeat(other, a => a.Kind == AnswerKind.Craft), Is.True, "남 좌석의 제작");
            CraftScreen seen = null;
            string made = null;
            bool buttons = false, hid = false, back = false, current = false;
            for (int guard = 0; guard < 300; guard++)
            {
                flow.Skip(50);
                yield return null;
                if (stage.Current != null && !(stage.Current is PartyScreen)) current = true;
                if (flow.Watch is CraftScreen c && c.Watched == other)
                {
                    seen = c;
                    if (c.MadeText != null) made = c.MadeText;
                    if (stage.Root.Q("board-watch") == null || stage.Root.Q("board-watch").Q(className: "sn-craft-make") != null) buttons = true;
                    // 내 창(정비)을 열면 보기 창은 숨고, 닫으면 다시 보인다(덮지 않는다 — 결정 22 Q6)
                    if (!hid && c.Playing >= 0 && flow.OpenParty())
                    {
                        yield return null;
                        flow.Skip(1);
                        hid = !flow.WatchShown;
                        Assert.That(flow.Escape(), Is.True, "Esc = 정비 닫기");
                        flow.Skip(1);
                        yield return null;
                        back = flow.WatchShown || flow.Watch == null;
                    }
                }
                if (seen != null && flow.Watch == null && flow.Fx.Sequencer.IsEmpty) break;
            }
            Assert.That(seen, Is.Not.Null, "남의 제작 = 제작 창과 같은 보기 창");
            Assert.That(made, Is.Not.Null.And.Not.Empty, "머리 줄에 만든 것");
            Assert.That(buttons, Is.False, "보기 창은 HUD 아래(board-watch) · [제작] 단추 없음");
            Assert.That(current, Is.False, "보기 창은 무대의 화면이 아니다(내 창을 여는 길은 그대로)");
            Assert.That(hid, Is.True, "내 창을 열면 숨는다");
            Assert.That(back, Is.True, "닫으면 다시 보인다");
            yield return Settle();
            Assert.That(flow.Watch, Is.Null, "그 좌석의 제작 단계가 끝나면 닫힌다");
            Assert.That(stage.Root.Q("board-watch"), Is.Null);
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_남의정비는보기창이안뜨고_그좌석칩꼬리표로보인다_결정177()
        {
            int band = -1;
            yield return Seats(2, st =>
            {
                band = flow.Ctx.Items.Ids.IndexOf("item.choice_band");
                for (int s = 0; s < 2; s++) st.Seats[s].Bag.Add(band);
            });
            int me = flow.Client.Seat, other = 1 - me;
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");
            yield return Settle(() => flow.Client.View.ActiveSeat == other && flow.BoardOpen);
            int mon = flow.Client.View.Seats[other].Party[0];
            Assert.That(flow.MaintainSeat(other, new Maintenance { Kind = MaintKind.HoldItem, Pokemon = mon, Value = band }), Is.True, "남 좌석의 지니게 하기");
            bool tagged = false, watched = false, current = false;
            string text = null;
            for (int guard = 0; guard < 300; guard++)
            {
                flow.Skip(50);
                yield return null;
                if (flow.Watch != null || flow.WatchShown || stage.Root.Q("board-watch") != null) watched = true;
                if (stage.Current != null) current = true;
                if (flow.Hud.DidSeat == other && flow.Hud.DidKey == "maint")
                {
                    tagged = true;
                    var tag = stage.Root.Q<Label>(BoardHud.DidName(other));
                    if (tag != null) text = tag.text;
                }
                if (tagged && flow.Fx.Sequencer.IsEmpty) break;
            }
            Assert.That(tagged, Is.True, "그 좌석 칩에 「정비」 꼬리표");
            Assert.That(text, Is.EqualTo(stage.T("screen.board.did.maint")), "꼬리표 글");
            Assert.That(watched, Is.False, "남의 정비는 보기 창(board-watch)을 띄우지 않는다");
            Assert.That(current, Is.False, "창도 열지 않는다");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M9_남의조우고르기는카드로보이고_단추가없고_고르면닫힌다_결정120_T7()
        {
            yield return Seats(2, st =>
            {
                for (int c = 0; c < flow.Ctx.Board.Count; c++)
                {
                    if (MapActions.EncounterTokenAt(st, c) < 0 || Array.Exists(st.Seats, x => x.Cell == c)) continue;
                    st.Seats[1].Cell = c;
                    break;
                }
            });
            int me = flow.Client.Seat, other = 1 - me;
            flow.WatchWildBattles = true;   // 방 판처럼 남의 야생 배틀도 그린다(결정 120 ② — 고른 뒤 그 배틀이 저절로 열린다)
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");
            yield return Settle(() => flow.Client.View.ActiveSeat == other && flow.BoardOpen);
            Assert.That(flow.AnswerSeat(other, a => a.Kind == AnswerKind.Search), Is.True, "남 좌석의 찾아보기");
            EncounterScreen seen = null;
            for (int guard = 0; guard < 300 && seen == null; guard++)
            {
                flow.Skip(50);
                yield return null;
                if (flow.WatchShown && flow.Watch is EncounterScreen e && e.Watched == other) seen = e;
            }
            Assert.That(seen, Is.Not.Null, "남의 조우 고르기 = 조우 창과 같은 카드");
            yield return Settle();
            var choice = PeekRules.Open(flow.Client.View, other, PeekSpot.Encounter);
            Assert.That(choice, Is.Not.Null);
            Assert.That(seen.Cards.Count, Is.EqualTo(choice.Options.Count), "후보 카드 수");
            var watch = stage.Root.Q("board-watch");
            Assert.That(watch.Q("encounter-card-1"), Is.Not.Null, "카드");
            Assert.That(watch.Q("encounter-pick-1"), Is.Null, "[배틀!] 없음(보기 전용)");
            Assert.That(watch.Q<Label>("encounter-watch").text, Does.Contain(flow.SeatName(other)), "누가 고르는 중");
            var face = watch.Q(FaceBox.OwnerName);   // 머리 제목 앞 그 좌석 얼굴 상자(좌석 색) — #707
            Assert.That(face, Is.Not.Null, "머리에 그 좌석 얼굴 상자");
            Assert.That(FaceBox.SeatColorOf(face), Is.EqualTo(flow.SeatColor(other)), "얼굴 상자 = 그 좌석 색");
            Assert.That(face.parent.Q<Label>("encounter-ask").text, Does.Contain(flow.SeatName(other)), "같은 줄에 「○○의 조우」");
            Assert.That(seen.Open, Is.False);
            Assert.That(stage.Current, Is.Null);
            Assert.That(flow.AnswerSeat(other, a => a.Kind == AnswerKind.Pick), Is.True, "남 좌석이 고른다");
            bool picked = false;
            for (int guard = 0; guard < 400 && flow.Watch != null; guard++)
            {
                flow.Skip(50);
                yield return null;
                if (seen.Picked >= 0) picked = true;
            }
            Assert.That(picked, Is.True, "고른 카드가 커진다(내 창처럼)");
            Assert.That(flow.Watch, Is.Null, "고른 뒤 전환이 닫히면 닫힌다");
            Assert.That(stage.Current is BattleScreen, Is.True, "그 야생 배틀이 가운데에 열린다(결정 120 ②)");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M9_남이고향마을을고르는동안같은화면을보기만하고_스타팅스펙이보인다_T2_T3()
        {
            yield return Boot();
            flow.StartSeats(new SoloSettings(), 2, 20261116, 7, null, liveSetup: true);
            for (int i = 0; i < 6; i++) yield return null;
            yield return Settle(() => stage.Current is SetupVillageScreen);
            var mine = stage.Current as SetupVillageScreen;
            Assert.That(mine, Is.Not.Null, "내 고향 마을 고르기");
            Assert.That(mine.Watched, Is.EqualTo(-1));
            // T3: 마을을 고르면 스타팅 포켓몬 둘의 타입 · 체력 · 스피드 · ★ 고유 기술(이 게임 데이터)
            mine.Pick(0);
            yield return null;
            for (int n = 1; n <= 2; n++)
            {
                Assert.That(stage.Root.Q("setup-village-starter-" + n + "-types"), Is.Not.Null, "스타팅 " + n + " 타입");
                Assert.That(stage.Root.Q("setup-village-starter-" + n + "-stats"), Is.Not.Null, "스타팅 " + n + " 체력 · 스피드");
                Assert.That(stage.Root.Q("setup-village-starter-" + n + "-move"), Is.Not.Null, "스타팅 " + n + " 고유 기술");
            }
            Assert.That(WorkUi.Overflows(stage.Root.Q("setup-village-detail")), Is.Empty, "스펙 글이 넘치지 않는다");
            Assert.That(mine.Confirm(), Is.True, "[확정]");
            // T2: 좌석 2가 고르는 동안 내 화면 = 같은 화면의 보기(누가 고르는지 · 후보 · [확정] 없음)
            SetupVillageScreen watch = null;
            for (int guard = 0; guard < 200 && watch == null; guard++)
            {
                flow.Skip(100);
                yield return null;
                if (stage.Current is SetupVillageScreen v && v.Watched == 1) watch = v;
            }
            Assert.That(watch, Is.Not.Null, "남의 고향 마을 고르기를 보는 화면");
            Assert.That(stage.Root.Q<Label>("setup-village-guide").text, Does.Contain(flow.SeatName(1)), "누가 고르는 중");
            Assert.That(stage.Root.Q("setup-village-confirm").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "[확정] 없음");
            Assert.That(watch.Villages.Count, Is.EqualTo(flow.Client.Legal(SetupVillageScreen.PickOf(flow.Client.View, 1)).Count), "그 좌석의 후보(남은 마을)");
            Assert.That(watch.Villages.Count, Is.GreaterThan(0));
            watch.Pick(0);
            Assert.That(watch.Picked, Is.EqualTo(0), "내 화면에서만 훑어본다");
            Assert.That(watch.Confirm(), Is.False, "답을 내지 않는다");
            Assert.That(flow.AnswerSeat(1), Is.True, "좌석 2가 고른다");
            yield return Settle(() => !(stage.Current is SetupVillageScreen));
            Assert.That(stage.Current is SetupVillageScreen, Is.False, "고르면 보기 화면이 걷힌다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M5_거래창의여행점수는5점씩올린다()
        {
            yield return Seats(2, st =>
            {
                st.Seats[1].Cell = st.Seats[0].Cell;
                st.Seats[0].Jp = 3;
                st.Seats[1].Jp = 12;
            });
            Assert.That(flow.Hud.Press(BoardHud.Trade), Is.True, "[거래]");
            yield return null;
            yield return null;
            var trade = stage.Current as TradeScreen;
            Assert.That(trade, Is.Not.Null);
            Assert.That(stage.Root.Q<Button>("board-trade-ask-jp-more"), Is.Not.Null, "상대의 여행 점수 12 — [+5점]");
            Assert.That(stage.Root.Q<Button>("board-trade-give-jp-more"), Is.Null, "내 여행 점수 3 — 칩만");
            Assert.That(trade.More(1), Is.True, "[+5점]");
            yield return null;
            Assert.That(trade.Put(1, TradeKind.Jp, -1), Is.EqualTo(5));
            Assert.That(trade.More(1), Is.True);
            Assert.That(trade.Put(1, TradeKind.Jp, -1), Is.EqualTo(10));
            Assert.That(trade.More(1), Is.True);
            Assert.That(trade.Put(1, TradeKind.Jp, -1), Is.EqualTo(12), "가진 점수에서 멈춘다");
            Assert.That(trade.Bump(1, TradeKind.Jp, -1), Is.True);
            Assert.That(trade.Put(1, TradeKind.Jp, -1), Is.EqualTo(0), "다 올렸으면 0으로");
            Assert.That(trade.Bump(1, TradeKind.Jp, -1), Is.True);
            Assert.That(trade.Put(1, TradeKind.Jp, -1), Is.EqualTo(1), "칩은 1점씩 그대로");
            yield return null;
            Assert.That(WorkUi.Overflows(stage.Root.Q("board-trade")), Is.Empty, "거래 창의 글이 넘치지 않는다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M9_남좌석해금소리는덮개와같은박자_내창을열어덮개가숨으면내지않는다_결정120()
        {
            yield return Seats(2, null);
            var sound = boot.Sound;
            if (sound == null || sound.Effects.Count == 0) Assert.Ignore("M4 Gen 없음: 소리 표(build.mjs se) 먼저");
            int me = flow.Client.Seat, other = 1 - me;
            int hiker = flow.Ctx.Cards.TrainerCardIds.IndexOf("hiker");
            // 남 좌석의 해금(48행) 항목 하나 — 화면이 대기열에 넣는 것과 같은 길(SoundCues.Add, 남·관전 칸)
            FxEntry Unlocked(int seq)
            {
                var e = new FxEntry { Seq = seq, Seat = other, QueueMs = PlayTiming.UnlockMs };
                var step = new FxStep { Row = UnlockBeats.Row, Layer = FxLayer.Item, Ms = PlayTiming.UnlockMs, StartMs = 0 };
                step.Events.Add(new CardUnlocked { Seat = other, CardKey = hiker, TierBefore = 1, TierAfter = 2, CauseGrade = EncounterTier.Moderate, AbilitySlots = new[] { 1 } });
                e.Steps.Add(step);
                e.Events.AddRange(step.Events);
                SoundCues.Add(e, false);
                return e;
            }
            bool covered = false;
            IEnumerator Play(FxEntry e, List<string> keys)
            {
                covered = false;
                int at = sound.Heard.Count;
                flow.Fx.Enqueue(new[] { e });
                for (int t = 0; t <= PlayTiming.UnlockMs + 300; t += 100)
                {
                    stage.Tweens.Advance(100);
                    yield return null;
                    if (flow.Cards != null && flow.Cards.UnlockShown) covered = true;
                }
                for (int i = at; i < sound.Heard.Count; i++)
                    if (sound.Heard[i].Kind == "se" && sound.Heard[i].From.StartsWith(e.Seq + "@", StringComparison.Ordinal)) keys.Add(sound.Heard[i].Key);
            }
            Assert.That(flow.Fx.Sequencer.IsEmpty, Is.True, "대기열이 비었다");
            Assert.That(flow.ShowsOthers, Is.True, "내 창 · 메뉴 없음");
            var heard = new List<string>();
            yield return Play(Unlocked(900101), heard);
            Assert.That(covered, Is.True, "남 좌석의 해금 = 큰 덮개");
            Assert.That(heard, Is.EqualTo(new List<string> { SoundKeys.Token, SoundKeys.Hit, SoundKeys.CardFlip, SoundKeys.Shine }), "내 해금과 같은 박자의 소리(아이콘 · 깨짐 · 뒤집기 · 착지)");
            Assert.That(flow.Fx.Sequencer.IsEmpty, Is.True, "해금 단계가 끝났다");
            // 내 창(정비)을 열어 두면 덮개가 숨고 소리도 내지 않는다(#709)
            Assert.That(flow.OpenParty(), Is.True, "내 정비 판");
            yield return null;
            Assert.That(flow.ShowsOthers, Is.False);
            var muted = new List<string>();
            yield return Play(Unlocked(900102), muted);
            Assert.That(covered, Is.False, "창 뒤 — 덮개 없음");
            Assert.That(muted, Is.Empty, "덮개가 숨으면 소리도 없다");
            Assert.That(flow.Escape(), Is.True, "Esc = 정비 닫기");
            yield return null;
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        static string Plain(string s) => s?.Replace("⁠", "");

        // M8 · M9 마감 감사 2차 참고 ②: 손님 화면 턴 배지가 차례가 넘어간 뒤 약 3초 「내 턴」으로 남았다(넘김 띠가 끝날 때 바뀜).
        // 넘김 연출(요약 · 띠)이 시작하는 순간 넘겨받은 좌석으로 — 내게 오는 턴은 그대로 내 턴 컷신이 끝날 때 「내 턴」 [가정 plan §5 #776]
        [UnityTest]
        public IEnumerator M10_감사메모_턴배지는차례가넘어가는순간바뀌고_내게오는턴은컷신뒤에내턴()
        {
            yield return Seats(2, null);
            int me = flow.Client.Seat, other = 1 - me;
            string mine = stage.T("screen.board.my_turn");
            string Badge() => Plain(stage.Root.Q<Label>("board-hud-turn")?.text);
            bool Lit(int seat) => stage.Root.Q("board-hud-seat-" + (seat + 1)) is VisualElement chip && chip.ClassListContains("sn-hud-seat-turn");
            Assert.That(Badge(), Is.EqualTo(mine));

            // 내 턴 → 남: 넘김 연출 동안 배지 · 칩 강조는 이미 넘겨받은 좌석
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");
            int during = 0;
            var wrong = new List<string>();
            for (int guard = 0; guard < 300 && (during == 0 || !flow.Fx.Sequencer.IsEmpty); guard++)
            {
                flow.Skip(50);
                yield return null;
                int row = flow.Hud.Band.ActiveRow;
                if (row != FxRows.TurnSummary && row != FxRows.Handoff) continue;
                during++;
                if (Badge() == mine || !Lit(other) || Lit(me)) wrong.Add(row + "@" + flow.Fx.Sequencer.LocalMs + " " + Badge());
            }
            Assert.That(during, Is.GreaterThan(0), "넘김 연출을 봤다");
            Assert.That(wrong, Is.Empty, "넘김 연출 동안 「내 턴」이 남았다: " + string.Join(" | ", wrong));
            Assert.That(Badge(), Is.EqualTo(Plain(flow.SeatName(other))));

            // 남 → 나: 띠 동안은 아직 남의 턴, 내 턴 컷신이 끝나면 「내 턴」
            yield return Settle(() => flow.Client.Choice(ChoiceKind.MapAction) == null);
            Assert.That(flow.AnswerSeat(other, a => a.Kind == AnswerKind.EndTurn), Is.True, "남 좌석의 턴 종료");
            int strip = 0, early = 0;
            for (int guard = 0; guard < 300 && (strip == 0 || !flow.Fx.Sequencer.IsEmpty); guard++)
            {
                flow.Skip(50);
                yield return null;
                if (flow.Hud.Band.ActiveRow != FxRows.Handoff) continue;
                strip++;
                if (Badge() == mine) early++;
            }
            Assert.That(strip, Is.GreaterThan(0), "넘김 띠를 봤다");
            Assert.That(early, Is.EqualTo(0), "내게 오는 턴의 「내 턴」은 컷신 뒤");
            yield return Settle(() => flow.InputOpen);
            Assert.That(Badge(), Is.EqualTo(mine));
            Assert.That(Lit(me), Is.True);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // M8 · M9 마감 감사 2차 참고 ④: 손님이 [확인]을 누르지 않은 막 안내 창이 시간 초과(기본 답) 뒤에도 남았다.
        // 내 선택이 시간 초과로 닫히면 막 안내 창 · 그 선택에 답하던 창(제작 등)도 닫는다 — 사람이 연 읽기 창(기록)은 그대로 [가정 plan §5 #777]
        [UnityTest]
        public IEnumerator M10_감사메모_시간초과로내선택이닫히면막안내창과그선택의창도닫힌다_읽기창은그대로()
        {
            var clock = new int[Enum.GetValues(typeof(ClockKind)).Length];
            clock[(int)ClockKind.MapTurn] = 30000;
            yield return Seats(2, st =>
            {
                for (int s = 0; s < 2; s++)
                {
                    for (int c = 0; c < st.Seats[s].Apricorns.Length; c++) st.Seats[s].Apricorns[c] = 3;
                    st.Seats[s].Jp = 20;
                }
            }, clock);
            int me = flow.Client.Seat;

            // 내 맵 행동이 시간 초과로 기본 답(턴 종료)이 될 때까지
            IEnumerator TimeOut()
            {
                long was = flow.TimedOutAtMs;
                for (int guard = 0; guard < 400 && flow.TimedOutAtMs == was; guard++)
                {
                    flow.Skip(250);
                    yield return null;
                }
                Assert.That(flow.TimedOutAtMs, Is.Not.EqualTo(was), "내 맵 행동이 시간 초과로 넘어갔다");
                yield return null;
            }

            // 남 좌석의 턴을 넘겨 다시 내 턴(입력이 열린 때)까지
            IEnumerator Mine()
            {
                for (int k = 0; k < 6 && flow.Client.View.ActiveSeat != me; k++)
                {
                    yield return Settle(() => flow.Client.View.ActiveSeat == me || flow.Client.Choice(ChoiceKind.MapAction) == null);
                    if (flow.Client.View.ActiveSeat != me) flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == AnswerKind.EndTurn);
                }
                yield return Settle(() => flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null);
                Assert.That(flow.Client.View.ActiveSeat, Is.EqualTo(me), "다시 내 턴");
            }

            Assert.That(flow.OpenAct(), Is.True, "막 안내 창(막 배지)");
            Assert.That(stage.Current, Is.InstanceOf<ActScreen>());
            yield return TimeOut();
            Assert.That(stage.Current, Is.Not.InstanceOf<ActScreen>(), "시간 초과로 내 턴이 넘어가면 막 안내 창도 닫힌다");

            yield return Mine();
            Assert.That(flow.OpenCraft(), Is.True, "[제작]");
            Assert.That(stage.Current, Is.InstanceOf<CraftScreen>());
            yield return TimeOut();
            Assert.That(stage.Current, Is.Not.InstanceOf<CraftScreen>(), "그 선택에 답하던 제작 창도 닫힌다");

            yield return Mine();
            flow.OpenLog();
            Assert.That(stage.Current, Is.InstanceOf<LogScreen>());
            yield return TimeOut();
            Assert.That(stage.Current, Is.InstanceOf<LogScreen>(), "사람이 연 기록 창은 그대로");
            flow.CloseLog();
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
