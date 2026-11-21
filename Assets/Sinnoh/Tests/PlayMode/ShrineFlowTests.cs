using System.Collections;
using System.Collections.Generic;
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
    /// 사람 혼자 하는 판이 사당에 공물을 바치고 카드를 처리하고, 천상의 균열에서 추종자를 고른다(M5 「사당·균열 화면」 — 04 S31~S33, §8-8 31·32·34행): 타이틀 → 혼자 하기 → 셋업(화면) →
    /// 사당 길 <see cref="SoloTrail.Shrine"/>(풀밭 사당 · 덱 맨 위 뜻밖의 선물 → 천상의 균열 → 평온한 순간) → [공물] → 사당 판에서 분홍 규토리 + 여행 점수 7 → [바치기] →
    /// 3장이 내려와 뒤집힘 → 뜻밖의 선물을 골라 검정 규토리 2개 → 천상의 균열을 골라 추종자 펄기아 → 공개·집계 → 펄기아 강림(피규어가 내려앉음) → 좌석 칩의 추종자 아이콘 → 다음 턴 이동력 +1.
    /// 답은 길잡이가 고르고 <see cref="FlowPilot"/>이 화면 요소로 누른다(dotnet <c>SoloPilotTests.M5_사당길_…</c>가 같은 판을 화면 없이 돈다). 받을 화면이 없는 선택은 0번.
    /// </summary>
    public sealed class ShrineFlowTests
    {
        [UnityTest]
        public IEnumerator M5_사람혼자하는판이공물을바치고카드를처리하고추종자를골라신이강림한다()
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

                var title = (TitleScreen)stage.Current;
                int soloIndex = -1;
                for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Solo) soloIndex = i;
                Assert.That(title.Activate(soloIndex), Is.True);
                yield return null;
                var events = new List<GameEvent>();
                flow.EventsSeen += events.AddRange;
                var pilot = FlowPilot.Start(flow, SoloTrail.Shrine);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");
                string notReady = stage.T("screen.board.guide.not_ready");
                var board = Object.FindAnyObjectByType<BoardBoot>();
                BoardFigures.Figure palkia = null;
                if (board != null && board.Figures != null)
                    foreach (var f in board.Figures.Figures)
                        if (f.Group == GameFlow.ShrineFigures && flow.Ctx.Cards.PokemonIds.TryIndexOf(f.Key, out int def) && def == FractureRules.GodCard(flow.Ctx, SinnohGod.Palkia)) palkia = f;
                Assert.That(palkia, Is.Not.Null, "보드에 펄기아 피규어가 있다");
                Assert.That(palkia.Root.activeSelf, Is.False, "게임 중에는 신 토큰이 놓이기 전까지 신 피규어가 서지 않는다");

                var modes = new HashSet<ShrineScreen.Mode>();
                bool offerButton = false, cardsDropped = false, tallyFlip = false, falling = false, moveTag = false, chip = false, closed = false;
                int cardsMax = 0, artMax = 0, skyTier = 0;
                float skyTop = 0f;
                var gifts = new HashSet<Sinnoh.Fx.BlessGift>();
                float t0 = Time.realtimeSinceStartup;
                for (int guard = 0; guard < 40000 && Time.realtimeSinceStartup - t0 < 240f; guard++)
                {
                    flow.Skip(150);
                    yield return null;
                    var guide = stage.Root.Q<Label>("board-hud-guide");
                    if (guide != null) Assert.That(guide.text, Is.Not.EqualTo(notReady), "HUD에 준비 중 안내가 떴다");
                    if (stage.Root.Q("board-hud-act-" + BoardHud.Offer) != null) offerButton = true;
                    var seq = flow.Fx.Sequencer;
                    if (stage.Current is ShrineScreen shrine)
                    {
                        modes.Add(shrine.Shown);
                        cardsMax = Mathf.Max(cardsMax, shrine.Cards.Count);
                        if (shrine.Shown == ShrineScreen.Mode.Cards) artMax = Mathf.Max(artMax, stage.Root.Query(className: "sn-shrine-card-art").ToList().Count);
                        if (shrine.Shown == ShrineScreen.Mode.Fx && Sinnoh.Fx.ShrineRows.Playing(seq, Sinnoh.Fx.ShrineRows.Offer, out int ms) != null && ms > Sinnoh.Fx.OfferBeats.CardFromMs && shrine.Cards.Count == 3) cardsDropped = true;
                        if (Sinnoh.Fx.ShrineRows.Playing(seq, Sinnoh.Fx.ShrineRows.Tally, out _) != null && stage.Root.Q("board-shrine-bar-palkia") != null) tallyFlip = true;
                    }
                    if (Sinnoh.Fx.ShrineRows.Playing(seq, Sinnoh.Fx.ShrineRows.Descend, out int dms) != null)
                    {
                        if (palkia.Root.activeSelf && palkia.Root.transform.localPosition.y > (float)palkia.Spot.Y + 0.1f) falling = true;
                        if (flow.Hud != null && flow.Hud.ShrineFx.MoveShown > 0) moveTag = true;
                    }
                    // M9 다듬기 6: 판 위 사당의 공물 빛기둥(P36) · 축복의 고유 착지(P35)
                    if (flow.Hud != null && flow.Hud.ShrineFx.SkyShown > 0)
                    {
                        skyTier = flow.Hud.ShrineFx.SkyShown;
                        skyTop = Mathf.Max(skyTop, flow.Hud.ShrineFx.SkyHeight);
                    }
                    if (stage.Current is ShrineScreen blessing && blessing.GiftsShown > 0) gifts.Add(blessing.GiftKind);
                    if (flow.Hud != null && flow.Hud.FollowerShown(0)) chip = true;
                    if (chip && stage.Current == null && events.Exists(e => e is GodDescended)) closed = true;
                    if (closed && flow.InputOpen && events.FindAll(e => e is TurnStarted).Count >= 2) break;
                    FlowPilot.Step(flow, pilot);
                }

                Assert.That(offerButton, Is.True, "[공물] 단추(보통 사당)");
                Assert.That(modes.Contains(ShrineScreen.Mode.Fx), Is.True, "[공물] → 사당 판(S31)에서 [바치기] — 판이 남아 연출을 그렸다");
                var offered = (ShrineOffered)events.Find(e => e is ShrineOffered);
                Assert.That(offered, Is.Not.Null, "공물을 바쳤다(31행)");
                Assert.That(offered.Extra, Is.True, "여행 점수 7을 더 냈다");
                Assert.That(cardsDropped, Is.True, "3장이 내려왔다(31행)");
                Assert.That(skyTier, Is.EqualTo(2), "판 위 사당에 2단계(분홍) 빛기둥이 섰다(M9 P36)");
                Assert.That(skyTop, Is.EqualTo(Sinnoh.Fx.OfferBeats.BoardHeight(2)).Within(0.001f), "2단계 높이까지 솟았다");
                Assert.That(gifts, Is.EquivalentTo(new[] { Sinnoh.Fx.BlessGift.Apricorn }), "뜻밖의 선물의 고유 착지 — 규토리가 튀어나온다(M9 P35)");
                Assert.That(cardsMax, Is.EqualTo(3));
                Assert.That(modes.Contains(ShrineScreen.Mode.Cards), Is.True, "3장 중 고르기(S32)");
                Assert.That(artMax, Is.EqualTo(3), "3장 모두 원작 카드 그림(결정 80 L12 나)");
                Assert.That(modes.Contains(ShrineScreen.Mode.Apricorns), Is.True, "받을 규토리 고르기(뜻밖의 선물)");
                Assert.That(events.Exists(e => e is ApricornsGained g && g.Source.Kind == SourceKind.ShrineEvent), Is.True, "규토리를 받았다");
                Assert.That(modes.Contains(ShrineScreen.Mode.Follower), Is.True, "추종자 고르기(S33)");
                Assert.That(tallyFlip, Is.True, "공개·집계(34행)를 사당 판이 그렸다");
                var god = (GodDescended)events.Find(e => e is GodDescended);
                Assert.That(god, Is.Not.Null, "신 강림");
                Assert.That(god.God, Is.EqualTo((int)SinnohGod.Palkia));
                Assert.That(falling, Is.True, "펄기아 피규어가 하늘에서 내려왔다");
                Assert.That(palkia.Root.activeSelf, Is.True, "내려앉은 뒤 서 있다");
                Assert.That(palkia.Root.transform.localPosition.y, Is.EqualTo((float)palkia.Spot.Y).Within(0.001f), "제자리에 섰다");
                Assert.That(moveTag, Is.True, "추종자 칩 아래 「이동력 +1」");
                Assert.That(chip, Is.True, "좌석 칩의 추종자 아이콘(U11)");
                Assert.That(closed, Is.True, "할 일이 끝나면 사당 판이 닫힌다");
                Assert.That(flow.Client.View.Seats[0].Follower, Is.EqualTo(SinnohGod.Palkia));
                Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택이 없었다");
                Assert.That(WorkUi.Overflows(stage.Root.Q("board-hud-seats")), Is.Empty, "좌석 칩의 글이 넘치지 않는다");
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (rt != null) rt.Release();
            }
        }
    }
}
