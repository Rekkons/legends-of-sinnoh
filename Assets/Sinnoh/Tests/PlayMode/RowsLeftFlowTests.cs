using System;
using System.Collections;
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
    /// 남은 M4 연출 행(결정 70 ② A2 — 04 §8-8 6·9·13·19·99·102행)을 실행 중인 화면 흐름으로 본다: 좌석 여럿인 확인용 판(<see cref="GameFlow.StartSeats"/>)에서
    /// 엔진이 그 이벤트를 내게 한 뒤(다른 좌석의 답은 <see cref="GameFlow.AnswerSeat"/>) 그 단계의 한 순간으로 건너뛰어 화면 부품의 모습을 단정한다. Gen 겉모양·말 그림이 있어야 돈다.
    /// </summary>
    public sealed class RowsLeftFlowTests
    {
        const int W = 1920, H = 1080;

        ScreenBoot boot;
        GameFlow flow;
        BoardBoot board;
        RenderTexture rt;

        IEnumerator Open(int seats, Action<GameState> prepare = null, bool liveSetup = false)
        {
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
            boot.PointerFromMouse = false;
            board = UnityEngine.Object.FindAnyObjectByType<BoardBoot>();
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            if (board.Pieces.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저 — 말 확인 건너뜀(03 §7)");
            flow.StartSeats(new SoloSettings(), seats, 20261112, 7, prepare, liveSetup);
            for (int i = 0; i < 6; i++) yield return null;
            yield return Settle();
        }

        IEnumerator Settle()
        {
            for (int i = 0; i < 300 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty); i++)
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
            var b = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
            rt = null;
            if (flow != null) flow.SoundOnSkip = false;
            Tunables.Shared.Reset();
        }

        void EndActive()
        {
            int active = flow.Client.View.ActiveSeat;
            bool ok = active == flow.Client.Seat ? flow.Hud.Press(BoardHud.EndTurn) : flow.AnswerSeat(active, a => a.Kind == AnswerKind.EndTurn);
            Assert.That(ok, Is.True, "좌석 " + (active + 1) + "의 턴 종료");
        }

        FxStep Queued(Func<FxStep, bool> match)
        {
            foreach (var entry in flow.Fx.Sequencer.Queued)
                foreach (var s in entry.Steps)
                    if (match(s)) return s;
            return null;
        }

        // 그 단계의 시작에서 ms 지난 순간으로
        IEnumerator Into(FxStep step, int ms)
        {
            flow.Skip(flow.MsUntil(s => ReferenceEquals(s, step)) + ms);
            yield return null;
        }

        [UnityTest]
        public IEnumerator M4_여럿판의턴시작은배너와이동력과라운드띠와보호해제를그린다()
        {
            // 다음 턴인 좌석에 도전 보호를 걸어 둔 두 좌석 판(시험용 상태 — 실제로는 트레이너 배틀이 건다)
            int shielded = -1;
            yield return Open(2, state => state.Seats[shielded = (state.ActiveSeat + 1) % state.Seats.Length].Protected = true);
            var hud = flow.Hud;
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.ShieldShown(shielded), Is.True, "보호 중인 좌석 칩에 보호 배지");
            Assert.That(hud.ShieldShown(1 - shielded), Is.False);

            // 보호 배지는 칩의 배치 밖에 얹힌다 — 배지가 생기고 없어질 때 칩 폭이 그대로다(옆 칩이 옮겨지지 않는다, 검토 반영)
            yield return null;
            var root = boot.Root.Stage.Root;
            float ChipWidth(int seat) => root.Q("board-hud-seat-" + (seat + 1)).worldBound.width;
            float shieldedWidth = ChipWidth(shielded);   // 배지가 있을 때의 폭 — 풀린 뒤(아래)에도 같아야 한다
            Assert.That(shieldedWidth, Is.GreaterThan(0f));
            {
                var chip = root.Q("board-hud-seat-" + (shielded + 1)).worldBound;
                var tag = root.Q("board-hud-seat-" + (shielded + 1) + "-shield").worldBound;
                Assert.That(tag.xMin, Is.GreaterThanOrEqualTo(chip.xMin), "배지는 칩의 가로 범위 안");
                Assert.That(tag.xMax, Is.LessThanOrEqualTo(chip.xMax));
            }

            bool sawMine = false, sawOther = false, sawRound = false, sawShield = false;
            string movesText = flow.Stage.T("screen.board.moves_left");
            for (int turn = 0; turn < 4; turn++)
            {
                EndActive();
                var start = Queued(s => s.Row == TurnStartBeats.Row);
                Assert.That(start, Is.Not.Null, "좌석 여럿인 판은 턴 시작 단계(6행)를 그린다");
                Assert.That(start.Ms, Is.EqualTo(1400));
                var info = start.Tag as TurnStartInfo;
                Assert.That(info, Is.Not.Null);
                Assert.That(info.Seat, Is.EqualTo(flow.Client.View.ActiveSeat));
                Assert.That(info.Moves, Is.EqualTo(3));
                var shield = Queued(s => s.Row == ShieldBeats.Row);

                string MovesNow() => boot.Root.Stage.Root.Q<Label>("board-hud-moves").text;
                if (info.Mine)
                {
                    // 내 턴: 이동력은 단계 처음부터 하나씩 차오른다(그 전 — 넘김 띠 · 컷신 동안은 0)
                    yield return Into(start, -100);
                    Assert.That(MovesNow(), Is.EqualTo(movesText.Replace("{n}", "0")), "차오르기 전(컷신의 끝)");
                    flow.Skip(205);   // 105: 둘 켜짐(100 ~ 199 — 프레임이 지나며 조금 더 흐른다)
                    yield return null;
                    Assert.That(MovesNow(), Is.EqualTo(movesText.Replace("{n}", "2")), "단계 처음부터 100ms마다 하나");
                    flow.Skip(295);   // 400
                    yield return null;
                }
                else yield return Into(start, 400);
                Assert.That(hud.Band.BannerShown, Is.EqualTo(!info.Mine), "배너는 남의 턴에만(내 턴은 101행 컷신)");
                Assert.That(hud.Band.RoundShown, Is.EqualTo(info.Round > 0), "라운드 띠는 라운드의 첫 턴에만");
                if (info.Round > 0)
                {
                    sawRound = true;
                    Assert.That(info.Round, Is.EqualTo(flow.Client.View.Round));
                }
                if (info.Mine)
                {
                    sawMine = true;
                    Assert.That(MovesNow(), Is.EqualTo(movesText.Replace("{n}", "3")), "다 찼다(단계는 1400까지 이어진다)");
                }
                else
                {
                    sawOther = true;
                    Assert.That(FaceBox.Shown(boot.Root.Stage.Root.Q("turn-start-face")), Is.EqualTo(flow.Protagonist(info.Seat)), "배너의 얼굴 = 그 좌석의 주인공");
                    // 배너는 내용 폭이고 얼굴 상자가 왼쪽 끝에 붙는다(검토 반영: 고정 폭 가운데 모음은 얼굴 왼쪽에 빈 칸이 하나 더 있는 것처럼 보였다), 화면 가운데에 선다
                    var bannerBox = root.Q("turn-start-banner").worldBound;
                    var faceBox = root.Q("turn-start-face").worldBound;
                    var textBox = root.Q(className: "sn-turn-banner-text").worldBound;
                    Assert.That(faceBox.xMin - bannerBox.xMin, Is.LessThanOrEqualTo(4f), "얼굴 상자 왼쪽에 빈 칸이 없다");
                    Assert.That(bannerBox.xMax, Is.GreaterThan(textBox.xMax).And.LessThan(textBox.xMax + 40f), "배너 폭 = 내용 폭 + 오른쪽 안쪽 여백");
                    Assert.That(bannerBox.center.x, Is.EqualTo(root.worldBound.center.x).Within(1f), "화면 가운데");
                    flow.Skip(700);   // 1100: 배너가 나갔다
                    yield return null;
                    Assert.That(hud.Band.BannerShown, Is.False);
                    Assert.That(hud.Band.RoundShown, Is.False, "라운드 띠는 900에 끝난다");
                }
                if (shield != null)
                {
                    sawShield = true;
                    Assert.That(ShieldBeats.Seat(shield), Is.EqualTo(shielded));
                    Assert.That(hud.ShieldShown(shielded), Is.True, "풀리는 연출이 끝날 때까지 배지가 남는다");
                    yield return Into(shield, 60);
                    var badge = boot.Root.Stage.Root.Q("board-hud-seat-" + (shielded + 1) + "-shield");
                    Assert.That(badge, Is.Not.Null);
                    Assert.That(badge.ClassListContains("sn-hud-shield-lit"), Is.True, "풀리기 시작: 배지가 밝아진다(늘리지 않는다)");
                    Assert.That(badge.style.scale.keyword, Is.EqualTo(StyleKeyword.Null), "배지에 늘이기를 걸지 않는다");
                    flow.Skip(385);   // 445
                    yield return null;
                    badge = boot.Root.Stage.Root.Q("board-hud-seat-" + (shielded + 1) + "-shield");
                    Assert.That(badge, Is.Not.Null);
                    Assert.That(badge.ClassListContains("sn-hud-shield-lit"), Is.False);
                    Assert.That(badge.style.opacity.value, Is.LessThan(0.5f), "떨어지며 흐려지는 중");
                    Assert.That(badge.ClassListContains("sn-hud-shield-crack"), Is.True);
                }
                yield return Settle();
                Assert.That(hud.Band.BannerShown || hud.Band.RoundShown, Is.False);
                if (sawShield) Assert.That(hud.ShieldShown(shielded), Is.False, "보호가 풀렸다");
                if (sawShield) Assert.That(ChipWidth(shielded), Is.EqualTo(shieldedWidth).Within(0.5f), "배지가 없어져도 칩 폭은 그대로");
                if (info.Mine) Assert.That(boot.Root.Stage.Root.Q<Label>("board-hud-moves").text, Is.EqualTo(movesText.Replace("{n}", "3")));
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            }
            Assert.That(sawMine && sawOther && sawRound && sawShield, Is.True, $"내 턴 {sawMine} · 남의 턴 {sawOther} · 라운드 {sawRound} · 보호 해제 {sawShield}");
        }

        [UnityTest]
        public IEnumerator M4_남의말이선칸에걸어들어가면느낌표가뜬다()
        {
            int target = -1;
            yield return Open(2, state =>
            {
                var walker = state.Seats[state.ActiveSeat];
                var steps = MoveRules.Reachable(flow.Ctx.Board, walker.Cell, walker.MovesLeft, state.Options.ElevationMode);
                for (int c = 0; c < steps.Length && target < 0; c++) if (steps[c] == 2) target = c;
                state.Seats[(state.ActiveSeat + 1) % state.Seats.Length].Cell = target;
            });
            Assert.That(target, Is.GreaterThanOrEqualTo(0));
            var hud = flow.Hud;
            Assert.That(hud.MeetShown, Is.False);
            // 만남 소리(결정 73 ⑤): 건너뛰는 연출의 소리도 내게 하고(SoundOnSkip) 재생 층에 닿은 것을 센다
            var sound = boot.Sound;
            Assert.That(sound.Effects.Find(SoundKeys.Meet), Is.Not.Null, "Gen 효과음 표에 meet(build.mjs --only se)");
            flow.SoundOnSkip = true;
            int heardAt = sound.Heard.Count;
            bool walked = flow.Client.View.ActiveSeat == flow.Client.Seat ? hud.Walk(target) : flow.WalkActive(target);
            Assert.That(walked, Is.True);
            var walk = Queued(s => s.Row == 110);
            Assert.That(walk, Is.Not.Null);
            int landed = FxWalk.LandedAt((Moved)walk.Events[0]);
            yield return Into(walk, landed - 60);
            Assert.That(hud.MeetShown, Is.False, "닿기 전");
            flow.Skip(160);
            yield return null;
            Assert.That(hud.MeetShown, Is.True, "닿은 뒤 「!」");
            Assert.That(boot.Root.Stage.Root.Q("board-hud-bang").style.opacity.value, Is.EqualTo(1f));
            flow.Skip(MeetBeats.Ms);
            yield return null;
            Assert.That(hud.MeetShown, Is.False, "잠깐 뜨고 사라진다");
            yield return Settle();
            Assert.That(boot.Root.Stage.Root.Q("board-hud-bang").style.opacity.value, Is.EqualTo(0f));
            Assert.That(board.Pieces.StackAt(target), Is.Not.Null, "두 말은 묶음으로 묶인다(결정 66 ①)");
            flow.SoundOnSkip = false;
            int meets = 0;
            for (int i = heardAt; i < sound.Heard.Count; i++)
                if (sound.Heard[i].Kind == "se" && sound.Heard[i].Key == SoundKeys.Meet)
                {
                    meets++;
                    Assert.That(sound.Heard[i].Row, Is.EqualTo(110), "걷기 단계에 얹힌다(13행은 대기열 밖 행)");
                }
            Assert.That(meets, Is.EqualTo(1), "만남 소리 한 번");
        }

        [UnityTest]
        public IEnumerator M4_규토리줍기는떨어져튕기고모이며글이뜬다()
        {
            int cell = -1;
            yield return Open(1, state => cell = ApricornBeats.Stage(flow.Ctx, state, state.ActiveSeat));
            Assert.That(cell, Is.GreaterThanOrEqualTo(0));
            var hud = flow.Hud;
            int had = 0;
            foreach (int n in flow.Client.View.Seats[0].Apricorns) had += n;
            Assert.That(hud.Press(BoardHud.Search), Is.True, "[찾아보기]");
            yield return Settle();
            Assert.That(flow.Stage.Current, Is.Null, "조우가 없으면 조우 창이 열리지 않는다");
            Assert.That(hud.Shows(BoardHud.TakeApricorns), Is.True, "빈 찾아보기 뒤 [규토리 줍기]");
            Assert.That(hud.Press(BoardHud.TakeApricorns), Is.True);
            var step = Queued(s => s.Row == ApricornBeats.Row);
            Assert.That(step, Is.Not.Null, "규토리 줍기 단계(19행)");
            yield return Into(step, 500);
            Assert.That(hud.DropsShown, Is.EqualTo(0), "흔들리는 동안에는 아직 떨어지지 않는다");
            flow.Skip(650);   // 1150: 첫째는 닿았고 둘째가 떨어지는 중
            yield return null;
            Assert.That(hud.DropsShown, Is.EqualTo(2));
            Assert.That(hud.GainShown, Is.EqualTo(0f));
            flow.Skip(850);   // 2000: 모이며 글이 뜬다
            yield return null;
            Assert.That(hud.GainShown, Is.EqualTo(1f));
            Assert.That(boot.Root.Stage.Root.Q("board-hud-gain").Q<Label>().text, Is.EqualTo(flow.Stage.T("screen.board.summary.apricorns").Replace("{n}", "3")));
            yield return Settle();
            Assert.That(hud.GainShown, Is.EqualTo(0f));
            Assert.That(hud.DropsShown, Is.EqualTo(0));
            int have = 0;
            foreach (int n in flow.Client.View.Seats[0].Apricorns) have += n;
            Assert.That(have, Is.EqualTo(had + 3));
            Assert.That(flow.InputOpen, Is.True, "턴이 넘어가 다시 내 턴");
        }

        [UnityTest]
        public IEnumerator M4_셋업의동시공개는좌석칩이한꺼번에뒤집힌다()
        {
            const int seats = 3;
            yield return Open(seats, null, true);
            for (int guard = 0; !(flow.Stage.Current is SetupPickScreen); guard++)
            {
                Assert.That(guard, Is.LessThan(40), "고르는 화면까지 가지 못했다");
                if (flow.Stage.Current is SetupVillageScreen village && village.Watched < 0)   // 남이 고르는 동안의 보기 화면(결정 120 ① · T2)은 넘긴다
                {
                    village.Pick(0);
                    Assert.That(village.Confirm(), Is.True);
                }
                else
                {
                    bool any = false;
                    for (int seat = 1; seat < seats && !any; seat++) any = flow.AnswerSeat(seat);
                }
                yield return Settle();
            }
            var pick = (SetupPickScreen)flow.Stage.Current;
            Assert.That(boot.Root.Stage.Root.Q("setup-pick-seats"), Is.Not.Null, "좌석 여럿인 판의 고르는 화면에는 좌석 줄이 있다");
            Assert.That(pick.SeatsDone, Is.EqualTo(0));
            pick.PickStarter(0);
            pick.PickCard(1);
            Assert.That(pick.Confirm(), Is.True);
            yield return Settle();
            Assert.That(flow.Stage.Current, Is.SameAs(pick), "남이 고르는 동안 고르는 화면이 남는다");
            Assert.That(pick.SeatsDone, Is.EqualTo(1));
            Assert.That(boot.Root.Stage.Root.Q<Label>("setup-pick-seat-1-text").text, Is.EqualTo(flow.Stage.T("screen.setup.seat.done")));
            Assert.That(boot.Root.Stage.Root.Q<Label>("setup-pick-seat-2-text").text, Is.EqualTo(flow.Stage.T("screen.setup.seat.picking")));
            Assert.That(flow.AnswerSeat(1) && flow.AnswerSeat(1), Is.True);
            yield return Settle();
            Assert.That(pick.SeatsDone, Is.EqualTo(2));
            Assert.That(flow.AnswerSeat(2) && flow.AnswerSeat(2), Is.True);
            var step = Queued(s => s.Row == RevealAllBeats.Row);
            Assert.That(step, Is.Not.Null, "동시 공개 단계(99행)");
            Assert.That(step.Ms, Is.EqualTo(2000));
            yield return Into(step, RevealAllBeats.FlipAt(0) + RevealAllBeats.FlipMs / 2 + 10);
            Assert.That(pick.SeatsRevealed, Is.EqualTo(1), "좌석마다 60ms 어긋난다 — 첫 칩만 앞면");
            flow.Skip(600);
            yield return null;
            Assert.That(pick.SeatsRevealed, Is.EqualTo(seats));
            var view = flow.Client.View;
            for (int s = 0; s < seats; s++)
            {
                string mon = flow.Stage.T("pokemon." + flow.Ctx.Cards.Pokemon[view.Pokemon[view.Seats[s].Party[0]].Def].Key);
                Assert.That(boot.Root.Stage.Root.Q<Label>("setup-pick-seat-" + (s + 1) + "-text").text, Is.EqualTo(mon), "좌석 " + (s + 1) + "이 고른 스타팅 포켓몬");
            }
            yield return Settle();
            Assert.That(flow.Hud, Is.Not.Null, "공개가 끝나면 보드 HUD");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
        }
    }
}
