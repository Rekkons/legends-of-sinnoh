using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
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
    /// M5 「스탬프·도전·거래 화면」(04 S35·S36·S37 · §8-8 47·51·52행)을 실행 중인 화면 흐름으로 본다:
    /// ① 사람 혼자 하는 판이 여행 스탬프를 받아(규토리 일곱 색 — 그 명령 끝에) 도장 덮개(47행)를 보고 [스탬프 n/7]로 스탬프 판을 연다.
    /// ② 좌석 둘 판에서 [거래] → 거래 창에 올리고 [제안] → 상대의 수락(확인용 좌석의 답) → 거래 띠(52행) → 받은 것이 내 판에.
    /// ③ 좌석 셋 판에서 [도전] → 상대 고르기 → 원격 도전 → VS 띠(51행).
    /// 화면 요소·화면 함수로만 누른다. Gen 겉모양·말 그림이 있어야 돈다.
    /// </summary>
    public sealed class DealFlowTests
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
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                rt = null;
            }
        }

        [Test]
        public void M9_스탬프시트의동그라미일곱은그림안에있고겹치지않는다_P12()
        {
            // 원작 시트 그림(4163×2084)의 동그라미 자리 표: 윗줄 넷 · 아랫줄 셋, 모두 그림 안이고 서로 겹치지 않는다(가운데 사이 ≥ 지름의 0.9 — 원작 동그라미도 테두리가 맞닿는다)
            Assert.That(StampScreen.Spots.Length, Is.EqualTo(SeatState.StampCount));
            for (int i = 0; i < StampScreen.Spots.Length; i++)
            {
                var c = StampScreen.Spots[i];
                Assert.That(c.x - StampScreen.SpotR >= 0 && c.x + StampScreen.SpotR <= StampScreen.SheetW && c.y - StampScreen.SpotR >= 0 && c.y + StampScreen.SpotR <= StampScreen.SheetH, Is.True, "칸 " + (i + 1));
                Assert.That(c.y, Is.EqualTo(StampScreen.Spots[i < 4 ? 0 : 4].y), "줄 " + (i + 1));
                for (int k = 0; k < i; k++) Assert.That(((Vector2)(c - StampScreen.Spots[k])).magnitude, Is.GreaterThan(StampScreen.SpotR * 1.8f), (k + 1) + " · " + (i + 1));
            }
            var e = new VisualElement();
            StampScreen.Place(e, 6);
            Assert.That(e.style.left.value.unit, Is.EqualTo(LengthUnit.Percent), "백분율로 놓는다(시트 크기와 무관)");
            Assert.That(e.style.left.value.value, Is.EqualTo((3055 - 435) * 100f / 4163).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator M5_사람혼자하는판이스탬프를받아도장을보고스탬프판을연다()
        {
            yield return Boot();
            var title = (TitleScreen)stage.Current;
            int soloIndex = -1;
            for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Solo) soloIndex = i;
            Assert.That(title.Activate(soloIndex), Is.True);
            yield return null;
            var events = new List<GameEvent>();
            flow.EventsSeen += events.AddRange;
            FlowPilot.Start(flow, SoloTrail.Defeat);
            yield return null;
            Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");
            // 규토리 일곱 색을 쥔 판으로 다시 연다(스탬프 3 — 다음 명령 끝에 받는다)
            flow.Rebase(st =>
            {
                for (int c = 0; c < st.Seats[0].Apricorns.Length; c++) st.Seats[0].Apricorns[c] = 1;
            });
            flow.EventsSeen += events.AddRange;
            yield return Settle(() => flow.InputOpen && flow.Hud != null);
            var button = stage.Root.Q<Button>(BoardHud.StampButton);
            Assert.That(button, Is.Not.Null, "아래 판의 [스탬프 n/7]");
            Assert.That(button.text, Is.EqualTo(stage.T("screen.board.stamp").Replace("{n}", "0")));
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");

            bool dropped = false, slammed = false, rewards = false, overAll = false;
            for (int guard = 0; guard < 600; guard++)
            {
                flow.Skip(50);
                yield return null;
                var deals = flow.Deals;
                if (deals != null && deals.StampShown)
                {
                    if (deals.Stamped == 0) dropped = true;
                    if (deals.Stamped == StampRules.AllApricorns + 1) slammed = true;
                    if (deals.RewardsShown == 3) rewards = true;
                    // 덮개는 판·창 위, 툴팁 아래
                    var root = stage.Root;
                    if (stage.Over == deals.Element && root.IndexOf(deals.Element) > root.IndexOf(root.Q(flow.Hud.Name)) && root.IndexOf(stage.Tips.Element) > root.IndexOf(deals.Element)) overAll = true;
                }
                if (rewards && flow.InputOpen) break;
            }
            var got = (StampGained)events.Find(e => e is StampGained);
            Assert.That(got, Is.Not.Null, "스탬프를 받았다");
            Assert.That(got.CountAfter, Is.EqualTo(1));
            Assert.That(got.Changes.Length, Is.EqualTo(3), "첫 보상 = 빨강·파랑·노랑 규토리");
            Assert.That(dropped, Is.True, "도장이 내려왔다(47행)");
            Assert.That(slammed, Is.True, "셋째 칸에 찍혔다");
            Assert.That(rewards, Is.True, "보상 세 줄이 내려앉았다");
            Assert.That(overAll, Is.True, "덮개는 무대의 맨 위 층(HUD 위 · 툴팁 아래)");
            yield return Settle(() => flow.InputOpen);
            Assert.That(flow.Deals.StampShown, Is.False, "단계가 끝나면 덮개가 걷힌다");
            Assert.That(flow.Log.Lines, Has.Some.Matches<BoardLogLine>(l => l.Line.Key == BoardLog.K + "stamp"), "기록 줄");

            button = stage.Root.Q<Button>(BoardHud.StampButton);
            Assert.That(button.text, Is.EqualTo(stage.T("screen.board.stamp").Replace("{n}", "1")));
            Assert.That(flow.OpenStamp(), Is.True, "[스탬프 1/7] → 스탬프 판(S37)");
            yield return null;
            yield return null;
            var sheet = stage.Current as StampScreen;
            Assert.That(sheet, Is.Not.Null);
            Assert.That(sheet.Count, Is.EqualTo(1));
            Assert.That(stage.Root.Q("board-stamp-slot-3").ClassListContains("sn-stamp-slot-on"), Is.True, "셋째 칸이 찍혀 있다");
            Assert.That(stage.Root.Q("board-stamp-slot-1").ClassListContains("sn-stamp-slot-on"), Is.False);
            // M9 다듬기 P12(결정 80 L18 나): 원작 시트 그림 위에 도장 — 칸은 그림의 동그라미 자리(시트 안), 받은 칸에만 도장이 보인다
            var paper = stage.Root.Q("board-stamp-sheet");
            Assert.That(paper.resolvedStyle.backgroundImage.texture, Is.Not.Null, "시트 그림(Gen Cards/Stamp/sheet.png)");
            for (int i = 1; i <= SeatState.StampCount; i++)
            {
                var slot = stage.Root.Q("board-stamp-slot-" + i);
                Assert.That(paper.worldBound.Contains(slot.worldBound.min) && paper.worldBound.Contains(slot.worldBound.max), Is.True, "칸 " + i + "이 시트 안");
                Assert.That(slot[0].resolvedStyle.opacity, Is.EqualTo(i == 3 ? 1f : 0f), "도장 " + i);
                Assert.That(slot[0].resolvedStyle.backgroundImage.texture, Is.Not.Null, "도장 그림");
            }
            Assert.That(stage.Root.Q("board-stamp-reward-2").ClassListContains("sn-stamp-reward-next"), Is.True, "다음 보상 = 2개째");
            Assert.That(WorkUi.Overflows(stage.Root.Q("board-stamp")), Is.Empty, "스탬프 판의 글이 넘치지 않는다");
            flow.CloseStamp();
            yield return null;
            Assert.That(stage.Current, Is.Null, "닫으면 보드로");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택이 없었다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // 좌석 여럿 확인 판을 열고 내 턴(입력이 열린 때)까지 넘긴다
        IEnumerator Seats(int seats, Action<GameState> prepare)
        {
            yield return Boot();
            flow.StartSeats(new SoloSettings(), seats, 20261116, 7, prepare);
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

        [UnityTest]
        public IEnumerator M5_좌석둘판에서거래를제안하고수락받아주고받는다()
        {
            yield return Seats(2, st =>
            {
                st.Seats[1].Cell = st.Seats[0].Cell;
                st.Seats[0].Apricorns[(int)ApricornColor.Blue] = 3;
                st.Seats[1].Jp = 4;
            });
            var events = new List<GameEvent>();
            flow.EventsSeen += events.AddRange;
            int me = flow.Client.Seat;
            Assert.That(flow.Hud.Shows(BoardHud.Trade), Is.True, "같은 칸의 상대 — [거래]");
            Assert.That(flow.Hud.Press(BoardHud.Trade), Is.True);
            yield return null;
            yield return null;
            var trade = stage.Current as TradeScreen;
            Assert.That(trade, Is.Not.Null, "거래 창(S36)");
            Assert.That(trade.Partner, Is.EqualTo(1 - me));
            Assert.That(stage.Root.Q<Button>("board-trade-propose").enabledSelf, Is.False, "빈 거래는 제안할 수 없다");
            // 칩을 누른다: 파랑 규토리 둘 · 상대의 여행 점수 셋
            Assert.That(trade.Bump(0, TradeKind.Apricorn, (int)ApricornColor.Blue), Is.True);
            Assert.That(trade.Bump(0, TradeKind.Apricorn, (int)ApricornColor.Blue), Is.True);
            for (int i = 0; i < 3; i++) Assert.That(trade.Bump(1, TradeKind.Jp, -1), Is.True);
            yield return null;
            yield return null;
            Assert.That(trade.Put(0, TradeKind.Apricorn, (int)ApricornColor.Blue), Is.EqualTo(2));
            Assert.That(stage.Root.Q("board-trade-give-apricorn-" + (int)ApricornColor.Blue).ClassListContains("sn-trade-chip-on"), Is.True, "올린 칩은 금색");
            Assert.That(stage.Root.Q<Button>("board-trade-propose").enabledSelf, Is.True, "[제안] 켜짐");
            Assert.That(WorkUi.Overflows(stage.Root.Q("board-trade")), Is.Empty, "거래 창의 글이 넘치지 않는다");
            Assert.That(trade.Propose(), Is.True, "[제안]");
            yield return null;
            Assert.That(stage.Current, Is.Null, "제안하면 창이 닫힌다");
            Assert.That(flow.TradeWaiting, Is.True, "상대의 응답을 기다리는 중");
            yield return Settle();
            Assert.That(stage.Root.Q<Label>("board-hud-guide").text, Is.EqualTo(stage.T("screen.board.guide.trade_wait")));

            Assert.That(flow.AnswerSeat(1 - me, a => a.Kind == AnswerKind.Accept), Is.True, "상대의 수락");
            bool band = false, glow = false;
            int chips = 0;
            for (int guard = 0; guard < 400; guard++)
            {
                flow.Skip(50);
                yield return null;
                var deals = flow.Deals;
                if (deals != null && deals.TradeShown)
                {
                    band = true;
                    chips = Mathf.Max(chips, deals.ChipsShown);
                    if (deals.GlowShown) glow = true;
                }
                if (band && flow.InputOpen) break;
            }
            Assert.That(events.Exists(e => e is TradeProposed), Is.True);
            var done = (TradeDone)events.Find(e => e is TradeDone);
            Assert.That(done, Is.Not.Null, "거래가 이루어졌다");
            Assert.That(band, Is.True, "거래 띠(52행)");
            Assert.That(chips, Is.EqualTo(2), "올린 것 둘이 건너갔다");
            Assert.That(glow, Is.True, "받는 쪽이 빛났다");
            yield return Settle(() => flow.InputOpen);
            var view = flow.Client.View;
            Assert.That(view.Seats[me].Apricorns[(int)ApricornColor.Blue], Is.EqualTo(1));
            Assert.That(view.Seats[1 - me].Apricorns[(int)ApricornColor.Blue], Is.EqualTo(2));
            Assert.That(view.Seats[1 - me].Jp, Is.EqualTo(1));
            Assert.That(flow.TradeWaiting, Is.False);
            Assert.That(flow.Client.Choice(ChoiceKind.MapAction), Is.Not.Null, "거래 뒤 내 턴이 이어진다(G7)");
            Assert.That(flow.Log.Lines, Has.Some.Matches<BoardLogLine>(l => l.Line.Key == BoardLog.K + "trade.done"), "기록 줄");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택이 없었다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M5_좌석셋판에서상대를골라원격도전하면VS가선다()
        {
            yield return Seats(3, st =>
            {
                st.Seats[1].Cell = st.Seats[0].Cell;
                st.Seats[0].Follower = SinnohGod.Dialga;
                st.Seats[2].Follower = SinnohGod.Palkia;
            });
            var events = new List<GameEvent>();
            flow.EventsSeen += events.AddRange;
            Assert.That(flow.Hud.Press(BoardHud.Challenge), Is.True, "[도전]");
            yield return null;
            yield return null;
            var pick = stage.Current as ChallengeScreen;
            Assert.That(pick, Is.Not.Null, "상대가 여럿 — 고르는 창(S35)");
            Assert.That(pick.Answers.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(FaceBox.Shown(stage.Root.Q("board-challenge-face-" + (1 + new List<ChoiceAnswer>(pick.Answers).FindIndex(a => a.Target == 2)))), Is.EqualTo(flow.Protagonist(2)), "줄의 얼굴 상자 = 그 좌석의 주인공");
            Assert.That(WorkUi.Overflows(stage.Root.Q("board-challenge")), Is.Empty, "고르는 창의 글이 넘치지 않는다");
            Assert.That(pick.PressSeat(2), Is.True, "좌석 3에게 원격 도전");
            bool slid = false, marked = false, faces = false;
            for (int guard = 0; guard < 400; guard++)
            {
                flow.Skip(50);
                yield return null;
                var deals = flow.Deals;
                if (deals != null && deals.VsShown)
                {
                    slid = true;
                    if (deals.VsMarked) marked = true;
                    if (FaceBox.Shown(stage.Root.Q("deal-vs-face-a")) == flow.Protagonist(flow.Client.Seat) && FaceBox.Shown(stage.Root.Q("deal-vs-face-b")) == flow.Protagonist(2)) faces = true;
                }
                if (marked && flow.Fx.Sequencer.IsEmpty) break;
            }
            var issued = (ChallengeIssued)events.Find(e => e is ChallengeIssued);
            Assert.That(issued, Is.Not.Null);
            Assert.That(issued.Remote, Is.True, "원격 도전(p.19)");
            Assert.That(slid, Is.True, "VS 띠(51행)");
            Assert.That(marked, Is.True, "VS가 박혔다");
            Assert.That(faces, Is.True, "양쪽 얼굴 상자 = 두 좌석의 주인공");
            Assert.That(stage.Current, Is.Null, "고르는 창은 닫혔다");
            // 상대(확인용 좌석)의 거절 → 내 턴이 이어진다
            Assert.That(flow.AnswerSeat(2, a => a.Kind == AnswerKind.Decline), Is.True, "상대의 거절");
            yield return Settle(() => flow.InputOpen);
            Assert.That(flow.Client.Choice(ChoiceKind.MapAction), Is.Not.Null);
            Assert.That(flow.Log.Lines, Has.Some.Matches<BoardLogLine>(l => l.Line.Key == BoardLog.K + "challenge.declined"), "기록 줄");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
