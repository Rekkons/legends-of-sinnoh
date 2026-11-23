using System.Collections;
using System.Collections.Generic;
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
    /// 결정 206 ③(목업 r10-party-row6 · r10-party-drag): 정비 판의 칸 여섯이 원작 세로 쌓기 카드(<see cref="CardStack"/>)이고 빈 기술 칸은 그리지 않으며,
    /// [◀][▶][놓아주기] 줄은 기술 수와 상관없이 여섯 칸 모두 맨 아래 같은 높이. 기술 카드를 끄는 동안에만 받을 수 있는 빈 칸에 띠 크기의 홈이 서고(찬 칸은 금색 테), 끄는 것은 그 기술 띠.
    /// 끌어 놓기로 기술 가르치기 · 지닌물건(카드의 지닌물건 동그라미) · 순서 바꾸기. 판 = 카드 쌓기 견본(<see cref="SoloTrail.PrepareCards"/>의 다섯) + 팽도리 · 기술 카드 셋 · 행복의알,
    /// 720 벌(가장 좁은 높이). UiShots <c>party-card*</c>가 같은 판을 찍는다.
    /// </summary>
    public sealed class PartyCardFlowTests
    {
        const ulong Seed = 20261123, AiSeed = 7;

        static void Board(Ctx ctx, GameState state)
        {
            SoloTrail.PrepareCards(ctx, state);
            var st = state.Seats[0];
            PokemonRules.AddToParty(st, PokemonRules.Spawn(state, ctx.Cards.PokemonIds.IndexOf("393:0"), 0, (int)DeckId.Starter));
            foreach (string move in new[] { "aqua_jet", "razor_leaf", "giga_drain" }) st.StampMoves.Add(ctx.Cards.MoveIds.IndexOf(move));
            st.Bag.Add(ctx.Items.Ids.IndexOf("item.lucky_egg"));
        }

        [UnityTest]
        public IEnumerator M10_결정206_정비판칸은세로쌓기카드이고끄는동안만홈이서며끌어놓기로가르치고지니고옮긴다()
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
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var ctx = flow.Ctx;
                for (int i = 0; i < 4; i++) yield return null;
                flow.StartSeats(new SoloSettings(), 1, Seed, AiSeed, state => Board(ctx, state));
                yield return null;
                flow.Skip(flow.OpeningMs);
                for (int i = 0; i < 300 && !flow.CanOpenParty; i++)
                {
                    flow.Skip(100);
                    yield return null;
                }
                Assert.That(flow.OpenParty(), Is.True, "정비 판");
                for (int i = 0; i < 3; i++) yield return null;
                var party = (PartyScreen)stage.Current;
                var root = stage.Root;
                var client = flow.Client;
                var me = client.View.Seats[client.Seat];
                int piplup = me.Party[5], torterra = me.Party[0];

                // ── 칸 여섯 = 원작 카드 · 띠 수(빈 칸 없음) · 단추 줄 같은 높이 ──
                int[] want = { 2, 2, 1, 2, 0, 0 };
                float foot = float.NaN;
                var panel = root.Q("board-party-panel").worldBound;
                for (int i = 0; i < PartyScreen.Columns; i++)
                {
                    var mon = root.Q("board-party-mon-" + i);
                    Assert.That(mon?.Q("card-face"), Is.Not.Null, "칸 " + i + " = 원작 카드 그림");
                    Assert.That(Strips(mon), Is.EqualTo(want[i]), "칸 " + i + " 띠 수(빈 칸 없음)");
                    var release = root.Q("board-party-release-" + i).worldBound;
                    if (float.IsNaN(foot)) foot = release.yMin;
                    Assert.That(release.yMin, Is.EqualTo(foot).Within(0.5f), "칸 " + i + " [놓아주기] 줄 = 같은 높이");
                    Assert.That(root.Q("board-party-left-" + i).worldBound.yMin, Is.EqualTo(foot).Within(0.5f), "칸 " + i + " [◀] 같은 줄");
                    Assert.That(mon.Q("card-stack").worldBound.yMax, Is.LessThanOrEqualTo(release.yMin + 0.5f), "칸 " + i + " 쌓기는 단추 줄 위에서 끝난다");
                    Assert.That(release.yMax, Is.LessThanOrEqualTo(panel.yMax + 0.5f), "단추 줄이 판 안");
                    Assert.That(root.Q("board-party-col-" + i).worldBound.xMax, Is.LessThanOrEqualTo(root.Q("board-party-side").worldBound.xMin + 0.5f), "칸이 오른쪽 기둥과 안 겹침");
                }
                Assert.That(root.Q(className: "sn-party-slot-empty"), Is.Null, "「빈 칸」 기술 줄이 없다");
                Assert.That(root.Q(className: "sn-card-hole"), Is.Null, "끌기 전에는 홈이 없다");
                Assert.That(root.Q("board-party-slot-5-1"), Is.Null, "팽도리의 빈 칸은 그리지 않는다");
                Assert.That(root.Q("board-party-slot-0-1").ClassListContains("sn-pcard-hit"), Is.True, "토대부기 칸 1 = 띠 자리");
                Assert.That(root.Q("board-party-sig-0"), Is.Not.Null, "★ 고유 기술 자리");
                Assert.That(root.Q("board-party-status-0-poisoned"), Is.Not.Null, "독 주사위");

                // ── 기술 카드를 끄는 동안: 받을 수 있는 빈 칸에 홈(띠 크기) · 끄는 것은 그 기술 띠 ──
                int aqua = -1;
                for (int n = 0; n < party.Pool.Count; n++) if (party.Pool[n] == ctx.Cards.MoveIds.IndexOf("aqua_jet")) aqua = n;
                Assert.That(party.BeginDrag(PartyScreen.Hold.Move, aqua), Is.True);
                yield return null;
                yield return null;
                for (int k = 1; k <= 2; k++)
                {
                    var hole = root.Q("board-party-slot-5-" + k);
                    Assert.That(hole, Is.Not.Null, "팽도리 빈 칸 " + k + "에 홈");
                    Assert.That(hole.parent.ClassListContains("sn-card-hole") && hole.ClassListContains("sn-party-ok"), Is.True, "홈은 빛나는 받는 칸");
                    float h = Mathf.Round(hole.parent.worldBound.width * CardStack.MoveStripPx / (CardStack.WidthCm * CardStack.PxPerCm));
                    Assert.That(hole.parent.worldBound.height, Is.EqualTo(h).Within(1f), "홈 = 기술 띠 크기");
                }
                Assert.That(Strips(root.Q("board-party-mon-5")), Is.EqualTo(0), "홈은 띠로 세지 않는다");
                Assert.That(root.Q("board-party-mon-5").Q("card-stack").worldBound.yMax, Is.LessThanOrEqualTo(foot + 0.5f), "홈 둘을 끼워도 단추 줄 위");
                for (int i = 0; i < 5; i++)
                    for (int k = 0; k < PokemonState.MoveSlots; k++)
                        if (!party.CanPut(i, k) && root.Q("board-party-slot-" + i + "-" + (k + 1)) is VisualElement e) Assert.That(e.parent.ClassListContains("sn-card-hole"), Is.False, "받지 못하는 칸에는 홈이 없다 " + i + "-" + k);
                var ghost = root.Q("board-party-ghost").Q(className: "sn-pcard-ghost");
                Assert.That(ghost, Is.Not.Null, "끄는 것 = 기술 띠");
                Assert.That(ghost.resolvedStyle.backgroundImage.texture, Is.SameAs(GenAssets.Load<Texture2D>(GenAssets.CardMoves + "ko/aqua_jet.png")));
                // 홈에 놓으면 가르친다(칸 2)
                var second = root.Q("board-party-slot-5-2").worldBound.center;
                party.DragTo(second);
                Assert.That(party.EndDrag(second), Is.True, "홈에 놓기");
                yield return null;
                Assert.That(client.View.Pokemon[piplup].Moves[1], Is.EqualTo(ctx.Cards.MoveIds.IndexOf("aqua_jet")), "칸 2에 가르쳤다");
                Assert.That(Strips(root.Q("board-party-mon-5")), Is.EqualTo(1), "띠 하나");
                Assert.That(root.Q("board-party-slot-5-1"), Is.Null, "남은 빈 칸은 다시 그리지 않는다");
                Assert.That(root.Q(className: "sn-card-hole"), Is.Null, "놓은 뒤 홈이 걷힌다");
                flow.Skip(1000);
                yield return null;

                // ── 찬 칸 = 금색 테(바꿀 수 있음 — 물음) · 홈 없음 ──
                int leaf = -1;
                for (int n = 0; n < party.Pool.Count && leaf < 0; n++)
                {
                    party.PickMove(n);
                    if (party.CanPut(0, 0)) leaf = n;
                    party.Cancel();
                }
                Assert.That(leaf, Is.GreaterThanOrEqualTo(0), "토대부기가 배울 수 있는 기술 카드");
                Assert.That(party.BeginDrag(PartyScreen.Hold.Move, leaf), Is.True);
                yield return null;
                var full = root.Q("board-party-slot-0-1");
                Assert.That(full.ClassListContains("sn-party-ok") && !full.parent.ClassListContains("sn-card-hole"), Is.True, "토대부기의 찬 칸이 빛난다");
                Assert.That(full.resolvedStyle.borderTopWidth, Is.GreaterThan(0f), "금색 테");
                Assert.That(party.EndDrag(full.worldBound.center), Is.True);
                Assert.That(party.Asking, Is.True, "찬 칸에 놓으면 묻는다");
                party.Cancel();
                yield return null;

                // ── 지닌물건: 가방에서 끌어 카드의 지닌물건 동그라미에 ──
                Assert.That(party.BeginDrag(PartyScreen.Hold.Item, 0), Is.True);
                yield return null;
                var spot = root.Q("board-party-item-5");
                Assert.That(spot.ClassListContains("sn-party-ok"), Is.True, "팽도리 지닌물건 자리가 빛난다");
                Assert.That(root.Q("board-party-mon-5").Q("card-face").worldBound.Contains(spot.worldBound.center), Is.True, "받는 자리 = 카드 앞면의 동그라미");
                Assert.That(party.EndDrag(spot.worldBound.center), Is.True);
                yield return null;
                Assert.That(client.View.Pokemon[piplup].HeldItem, Is.EqualTo(ctx.Items.Ids.IndexOf("item.lucky_egg")), "끌어 놓아 지니게 했다");
                Assert.That(root.Q("board-party-mon-5").Q("card-item-1"), Is.Not.Null, "카드에 토큰");
                flow.Skip(1000);
                yield return null;

                // ── 순서: 팽도리 카드를 끌어 맨 앞 칸에 ──
                Assert.That(party.BeginDrag(PartyScreen.Hold.Mon, 5), Is.True);
                yield return null;
                Assert.That(root.Q("board-party-mon-0").Q(className: "sn-pcard-ring"), Is.Not.Null, "받는 카드에 테");
                var front = root.Q("board-party-col-0").worldBound.center;
                party.DragTo(front);
                Assert.That(party.EndDrag(front), Is.True);
                yield return null;
                Assert.That(client.View.Seats[client.Seat].Party[0], Is.EqualTo(piplup), "맨 앞으로");
                Assert.That(client.View.Seats[client.Seat].Party[1], Is.EqualTo(torterra), "사이 포켓몬은 한 칸씩 밀린다");
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

        // 카드에 끼운 띠 수(홈은 세지 않는다)
        static int Strips(VisualElement mon)
        {
            int n = 0;
            for (int i = 1; mon.Q("card-strip-" + i) is VisualElement e; i++)
                if (!e.ClassListContains("sn-card-hole")) n++;
            return n;
        }
    }
}
