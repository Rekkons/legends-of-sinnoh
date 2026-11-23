using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
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
    /// 결정 207(2026-11-23 「배틀화면에서 지닌물건 항상 볼 수 있도록 하자」) → 결정 206 ②(Q9): 배틀 기둥 카드의 지닌물건 동그라미(<c>battle-me-item-n</c> · <c>battle-foe-item-n</c>)가 늘 보인다 —
    /// 동그라미 수 = 그 포켓몬의 지닌물건 수(둘째 칸 포함) · 카드 마우스 오버 글 판 = 이름 · 효과 글 · 쓰러져 다음 포켓몬이 나오면 그 포켓몬의 것으로 바뀐다 · 없으면 동그라미가 없다.
    /// 판은 좌석 둘의 트레이너전(상대도 사람 좌석 — 공개 값). UiShots <c>battle-held*</c>가 같은 판을 찍는다.
    /// </summary>
    public sealed class BattleHeldFlowTests
    {
        const ulong Seed = 20261112, AiSeed = 7;

        readonly List<string> errors = new List<string>();
        RenderTexture rt;

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
            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
            }
            rt = null;
        }

        static bool MyTurn(BattleScreen b) => b.Open && b.Moves.Count > 0 && !b.AskingToken && !b.AskingOrder && !b.AskingFate && !b.AskingAsk && !b.AskingPick && b.Bench.Count == 0;

        // 그 쪽 지닌물건 동그라미(결정 206 ② — 카드의 토큰, -item-n)의 아이템 이름(칸 순서) — 카드 마우스 오버 글 판의 지닌물건 줄 머리로 읽는다
        static List<string> Cells(UiStage stage, BattleScreen screen, string name)
        {
            var got = new List<string>();
            // 동그라미 = 우두머리 토큰(-alpha) + 지닌물건(-item-n) — 모두 그림이 있다(토큰 그림이 없는 지닌물건은 아이템 그림 — 검토 지적 15 · 24)
            var circles = new List<VisualElement>();
            var alpha = stage.Root.Q(name + "-alpha");
            if (alpha != null) circles.Add(alpha);
            for (int k = 1; stage.Root.Q(name + "-item-" + k) != null; k++) circles.Add(stage.Root.Q(name + "-item-" + k));
            foreach (var c in circles)
                Assert.That(c.resolvedStyle.backgroundImage.texture, Is.Not.Null, "동그라미 그림이 있다(" + c.name + ")");
            int n = circles.Count;
            Assert.That(screen.HoverCard(name == "battle-me"), Is.True, name + " 카드 마우스 오버");
            var tip = screen.CardTipElement;
            for (int i = 1; i <= n; i++)
            {
                var row = tip.Q("battle-card-tip-item-" + i);
                Assert.That(row, Is.Not.Null, "글 판 지닌물건 줄 " + i + "(" + name + ")");
                var labels = row.Query<Label>().ToList();
                Assert.That(labels.Count >= 2 && labels[1].text.Length > 0, Is.True, "글 판 줄 = 이름 + 효과 글(" + name + " " + i + ")");
                got.Add(labels[0].text.Replace("\u2060", ""));
            }
            Assert.That(tip.Q("battle-card-tip-item-" + (n + 1)), Is.Null, "글 판 지닌물건 줄 수 = 동그라미 수(" + name + ")");
            return got;
        }

        static List<string> Want(GameFlow flow, PokemonState pk)
        {
            var want = new List<string>();
            if (pk.Alpha) want.Add(flow.Stage.T("item.alpha_pokemon"));
            foreach (int held in new[] { pk.HeldItem, pk.HeldItem2 })
                if (held >= 0) want.Add(flow.Stage.T(flow.Ctx.Items.Items[held].Key));
            return want;
        }

        [UnityTest]
        public IEnumerator M10_결정207_배틀기둥에양쪽지닌물건이늘보이고교체하면그포켓몬것으로바뀐다()
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
            Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            var flow = boot.Flow;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            var board = Object.FindAnyObjectByType<BoardBoot>();
            if (board == null || board.Pieces.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저");

            var ctx = flow.Ctx;
            int lead = -1, second = -1, foeMon = -1;
            flow.StartSeats(new SoloSettings(), 2, Seed, AiSeed, state =>
            {
                // 두 좌석을 첫 좌석 고향 마을 옆의 마을 밖 칸에(도전을 거절하지 못한다 — 응답 없이 바로 배틀)
                int home = state.Seats[0].Cell, cell = -1;
                for (int dir = 0; dir < 6 && cell < 0; dir++)
                {
                    int n = ctx.Board.Neighbor(home, dir);
                    if (n >= 0 && !BattleRules.InVillage(ctx, n)) cell = n;
                }
                Assert.That(cell, Is.GreaterThanOrEqualTo(0), "고향 마을 옆 마을 밖 칸");
                foreach (var seat in state.Seats) seat.Cell = cell;
                var st = state.Seats[0];
                st.OpenSlots = st.Party.Length;
                for (int i = 0; i < st.Party.Length; i++) st.Party[i] = -1;
                foreach (string key in new[] { "129:0", "483:2" }) PokemonRules.AddToParty(st, PokemonRules.Spawn(state, ctx.Cards.PokemonIds.IndexOf(key), 0, (int)DeckId.Legendary));
                lead = st.Party[0];
                second = st.Party[1];
                foeMon = state.Seats[1].Party[0];
                var first = state.Pokemon[lead];
                first.Damage = Math.Max(0, PokemonRules.MaxHp(ctx, first) - 1);   // 체력 1 — 쓰러지면 둘째가 나온다
                first.HeldItem = ctx.Items.Ids.IndexOf(HeldItems.ChoiceBand);
                first.HeldItem2 = ctx.Items.Ids.IndexOf(HeldItems.LuckyEgg);   // 둘째 칸(backpacker R)
                state.Pokemon[second].HeldItem = ctx.Items.Ids.IndexOf(HeldItems.StarShield);
                state.Pokemon[foeMon].HeldItem = ctx.Items.Ids.IndexOf(HeldItems.Eviolite);
                state.Pokemon[foeMon].HeldItem2 = ctx.Items.Ids.IndexOf("item.apex_dragon");   // 원작 토큰 그림이 없는 지닌물건 — 동그라미는 아이템 그림(검토 지적 15)
            });
            for (int i = 0; i < 4; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            for (int i = 0; i < 300 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty || !flow.InputOpen); i++)
            {
                flow.Skip(100);
                if (i % 10 == 0) yield return null;
            }
            var mine = flow.Client.FirstChoice();
            for (int i = 0; i < 300 && !(mine != null && mine.Kind == ChoiceKind.MapAction && flow.InputOpen); i++)
            {
                int active = flow.Client.View.ActiveSeat;
                if (flow.InputOpen && active != flow.Client.Seat) flow.AnswerSeat(active, a => a.Kind == AnswerKind.EndTurn);   // 좌석 2가 먼저면 그 턴을 넘긴다
                flow.Skip(100);
                if (i % 10 == 0) yield return null;
                mine = flow.Client.FirstChoice();
            }
            Assert.That(mine != null && mine.Kind == ChoiceKind.MapAction, Is.True, "내 맵 행동 차례(" + (mine != null ? mine.Kind.ToString() : "선택 없음") + ")");
            var challenge = flow.Client.Legal(mine).Find(a => a.Kind == AnswerKind.Challenge && a.Target == 1);
            Assert.That(challenge, Is.Not.Null, "좌석 2에게 [도전]");
            Assert.That(flow.Answer(mine, challenge).Accepted, Is.True);

            var stage = flow.Stage;
            bool sawLead = false, sawSecond = false;
            for (int guard = 0; guard < 8000 && !sawSecond; guard++)
            {
                var screen = stage.Current as BattleScreen;
                if (screen != null && screen.ShowsResult) Assert.Fail("둘째 포켓몬을 보기 전에 배틀이 끝났다");
                if (screen != null && MyTurn(screen))
                {
                    yield return null;
                    var view = flow.Client.View;
                    int me = screen.ShownMon(flow.Battle.MySide).Id, foe = screen.ShownMon(1 - flow.Battle.MySide).Id;
                    Assert.That(foe, Is.EqualTo(foeMon), "상대 = 좌석 2의 첫 포켓몬");
                    Assert.That(Cells(stage, screen, "battle-foe"), Is.EqualTo(Want(flow, view.Pokemon[foe])), "상대 쪽 지닌물건 동그라미 = 그 포켓몬의 것(남의 포켓몬도 공개)");
                    Assert.That(Cells(stage, screen, "battle-me"), Is.EqualTo(Want(flow, view.Pokemon[me])), "내 쪽 지닌물건 동그라미 = 나와 있는 포켓몬의 것");
                    if (me == lead && !sawLead)
                    {
                        Assert.That(Cells(stage, screen, "battle-me").Count, Is.EqualTo(2), "첫 포켓몬은 둘(둘째 칸 포함)");
                        // 효과 글: 글 판 지닌물건 첫 줄의 둘째 글 = item.<키>.text
                        Assert.That(screen.HoverCard(true), Is.True);
                        var labels = screen.CardTipElement.Q("battle-card-tip-item-1").Query<Label>().ToList();
                        Assert.That(labels[1].text.Replace("\u2060", ""), Is.EqualTo(stage.T(HeldItems.ChoiceBand + ".text").Replace("\u2060", "")), "효과 글");
                        // 동그라미는 카드(내 쪽 기둥) 안에 원작 토큰 그림으로
                        var item = stage.Root.Q("battle-me-item-1");
                        Assert.That(stage.Root.Q("battle-me").Contains(item), Is.True, "지닌물건 동그라미는 내 카드 안");
                        sawLead = true;
                    }
                    if (me == second)
                    {
                        Assert.That(sawLead, Is.True, "첫 포켓몬을 먼저 봤다");
                        Assert.That(Cells(stage, screen, "battle-me").Count, Is.EqualTo(1), "쓰러진 뒤 나온 포켓몬의 하나로 바뀐다");
                        sawSecond = true;
                        break;
                    }
                }
                if (screen != null && screen.Open)
                {
                    if (screen.AskingToken) screen.PressToken(true);
                    else if (screen.AskingOrder) screen.PressOrder(0);
                    else if (screen.AskingFate) screen.PressFatePass();
                    else if (screen.AskingAsk) screen.PressAsk(false);
                    else if (screen.AskingPick) { if (!screen.PressPick(-1)) screen.PressPick(0); }
                    else if (screen.Bench.Count > 0)
                    {
                        int k = 0;
                        for (int j = 0; j < screen.Bench.Count; j++) if (screen.Bench[j].Target == lead) k = j;
                        if (!screen.PressStay()) screen.PressBench(k);
                    }
                    else if (screen.Moves.Count > 0) screen.PressMove(0);
                }
                else flow.AnswerSeat(1, a => a.Kind != AnswerKind.Forfeit && !(a.Kind == AnswerKind.UseMove && a.Arg >= 0));
                flow.Skip(250);
                if (guard % 8 == 0) yield return null;
            }
            Assert.That(sawLead, Is.True, "첫 포켓몬(둘을 지님)의 차례를 봤다");
            Assert.That(sawSecond, Is.True, "둘째 포켓몬(하나를 지님)의 차례를 봤다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
