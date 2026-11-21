using System;
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

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M9 플레이 시험 T22(조작 불능): 2좌석 울트라 버스트 판에서 UB를 쓰러뜨린 뒤 결과 창 위에 뜨는 균열 카드 버리기·옮기기 창(S58) —
    /// 다른 좌석이 먼저 답해 그 항목이 [확인]을 기다리며 선 화면 대기열에 걸려도 내 창의 단추·카드 칸이 듣고, 접으면 결과 창 [확인]을 누를 수 있다.
    /// </summary>
    public sealed class UbBreachFlowTests
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

        IEnumerator Until(Func<bool> until, string what)
        {
            for (int i = 0; i < 600 && !until(); i++)
            {
                flow.Skip(100);
                yield return null;
            }
            Assert.That(until(), Is.True, what + " — 화면 " + (stage.Current != null ? stage.Current.Name : "보드") + " · 선택 " + (flow.Client.FirstChoice() != null ? flow.Client.FirstChoice().Kind + "/" + flow.Client.FirstChoice().Sub : "없음"));
        }

        bool MyMap() => flow.InputOpen && flow.Hud != null && flow.Client.View.ActiveSeat == flow.Client.Seat && flow.Client.Choice(ChoiceKind.MapAction) != null;

        // 내 좌석(0)의 파티를 전설 다섯으로 바꾸고 체력이 가장 작은 UB의 칸에 세운다 · 두 좌석의 첫 포켓몬 첫 기술 칸에 균열 카드. 내 첫 포켓몬을 돌려준다
        int Prepare(GameState st, ulong rng)
        {
            var ctx = flow.Ctx;
            var me = st.Seats[0];
            me.OpenSlots = me.Party.Length;
            for (int i = 0; i < me.Party.Length; i++) me.Party[i] = -1;
            foreach (string key in SoloTrail.Team)
                PokemonRules.AddToParty(me, PokemonRules.Spawn(st, ctx.Cards.PokemonIds.IndexOf(key), 0, (int)DeckId.Legendary));
            int mine = me.Party[0], other = -1;
            foreach (int p in st.Seats[1].Party) if (p >= 0 && other < 0) other = p;
            st.Pokemon[mine].Breach[0] = BreachRules.CardOf(ctx, BreachKind.StatusOverload);
            st.Pokemon[other].Breach[0] = BreachRules.CardOf(ctx, BreachKind.RapidStrikes);
            st.Ub.PrismUltraBeast = 0;
            st.Ub.PrismUltraBurst = UbState.PrismArmourTotal;
            TokenState weak = null;
            foreach (var t in st.Tokens)
                if (t.Kind == TokenKind.UltraBeast && t.Cell >= 0 && (weak == null || ctx.Cards.Pokemon[t.Def].Health < ctx.Cards.Pokemon[weak.Def].Health)) weak = t;
            Assert.That(weak, Is.Not.Null, "UB 토큰");
            me.Cell = weak.Cell;
            st.Rng.CopyFrom(new Pcg32(rng));
            return mine;
        }

        // 결과 창이 뜰 때까지 배틀 화면의 첫 답으로 싸운다
        IEnumerator Fight()
        {
            for (int i = 0; i < 3000; i++)
            {
                var b = stage.Current as BattleScreen;
                if (b != null && b.ShowsResult) yield break;
                if (b != null && b.Open)
                {
                    if (b.AskingToken) b.PressToken(true);
                    else if (b.AskingOrder) b.PressOrder(0);
                    else if (b.AskingFate) b.PressFatePass();
                    else if (b.AskingAsk) b.PressAsk(false);
                    else if (b.AskingPick) { if (!b.PressPick(-1)) b.PressPick(0); }
                    else if (b.Bench.Count > 0) { if (!b.PressStay()) b.PressBench(0); }
                    else if (b.Moves.Count > 0)
                    {
                        if (b.Moves[0].Target == BattleRules.AlphaStrike) { b.OpenAlpha(); b.PressAlpha(0); }
                        else b.PressMove(0);
                    }
                }
                flow.Skip(200);
                yield return null;
            }
            Assert.Fail("배틀이 끝나지 않는다");
        }

        [UnityTest]
        public IEnumerator M9_UB격파뒤결과창위균열카드창은남이먼저답해도단추가듣고접으면확인을누를수있다_에러로그0()
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

            // ── 2좌석 울트라 버스트 판 → 내 턴 → UB 칸에서 [찾아보기] → 이길 때까지(판의 난수를 다시 심어 이기는 첫 판 — 결정적) ──
            int mon = -1;
            for (ulong rng = 1; rng <= 8; rng++)
            {
                flow.StartSeats(new SoloSettings { Campaign = Campaign.UltraBurst }, 2, 20261119, 7);
                for (int i = 0; i < 6; i++) yield return null;
                flow.Skip(flow.OpeningMs);
                for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
                {
                    Assert.That(flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == AnswerKind.EndTurn), Is.True, "남 좌석의 턴 종료");
                    yield return Until(() => flow.Fx.Sequencer.IsEmpty, "턴 넘김");
                }
                yield return Until(MyMap, "내 턴");
                ulong at = rng;
                flow.Rebase(st => mon = Prepare(st, at));
                yield return null;
                yield return Until(MyMap, "손본 판의 내 턴");
                Assert.That(flow.Hud.Press(BoardHud.Search), Is.True, "UB 칸에서 [찾아보기]");
                yield return Fight();
                if (flow.Battle.Won) break;
            }
            Assert.That(flow.Battle != null && flow.Battle.Won, Is.True, "UB를 이기는 판을 찾지 못했다");

            // ── 결과 창 위에 균열 카드 버리기·옮기기 창(모든 좌석에게 한꺼번에 묻는다) ──
            yield return Until(() => flow.Top is BreachScreen && flow.BattleOver, "결과 창 위의 균열 카드 창");
            var w = (BreachScreen)flow.Top;
            Assert.That(BreachScreen.Releasing(flow.Client.View), Is.True, "버리기·옮기기 꼴");
            Assert.That(stage.Root.Q<Button>("board-breach-skip").enabledSelf, Is.True, "처음에는 [안 함]이 켜져 있다");

            // ── 다른 좌석이 먼저 답한다: 그 항목은 [확인]을 기다리며 선 대기열에 걸린다 ──
            Assert.That(flow.AnswerSeat(1, a => a.Kind == AnswerKind.Decline), Is.True, "좌석 2의 [안 함]");
            for (int i = 0; i < 5; i++)
            {
                flow.Skip(200);
                yield return null;
            }
            Assert.That(flow.BattleOver, Is.True);
            Assert.That(flow.BoardOpen, Is.False, "재현 조건: 남의 답이 선 대기열에 걸려 있다");
            Assert.That(flow.Top, Is.SameAs(w), "창은 그대로 떠 있다");
            Assert.That(stage.Root.Q<Button>("board-breach-skip").enabledSelf, Is.True, "남이 먼저 답해도 [안 함]이 켜져 있다(T22)");

            // ── 접으면 창 뿌리가 마우스를 받지 않는다: 결과 창 [확인]이 눌린다 ──
            var ok = stage.Root.Q("battle-result-ok");
            Assert.That(ok, Is.Not.Null, "결과 창 [확인]");
            Assert.That(w.ToggleFold(), Is.True);
            yield return null;
            var hit = stage.Root.panel.Pick(ok.worldBound.center);
            Assert.That(hit != null && (hit == ok || ok.Contains(hit)), Is.True, "접힌 창 아래 [확인]이 눌린다 — 맨 위 " + (hit != null ? hit.name + "/" + hit.GetType().Name : "없음"));
            Assert.That(w.ToggleFold(), Is.True);
            yield return null;

            // ── 카드 칸 · [버리기]가 듣고 답이 들어간다 ──
            Assert.That(w.PressSlot(mon, 0), Is.True, "붙은 카드 칸을 고른다");
            Assert.That(stage.Root.Q<Button>("board-breach-discard").enabledSelf, Is.True, "[버리기]가 켜진다");
            Assert.That(w.PressDiscard(), Is.True, "[버리기]");
            yield return Until(() => !(flow.Top is BreachScreen), "묶음이 끝나 창이 닫힌다");
            Assert.That(flow.Client.View.Pokemon[mon].Breach[0], Is.LessThan(0), "카드를 버렸다");

            // ── 배틀 뒤 선택(UB 기술 가르치기)을 넘기고 [확인]으로 보드에 ──
            for (int i = 0; i < 50 && flow.Client.Choice(ChoiceKind.Reward) != null; i++)
            {
                var reward = flow.Client.Choice(ChoiceKind.Reward);
                var legal = flow.Client.Legal(reward);
                Assert.That(flow.Answer(reward, legal.Find(a => a.Kind == AnswerKind.Decline) ?? legal[0]).Accepted, Is.True, "배틀 뒤 선택");
                flow.Skip(200);
                yield return null;
            }
            Assert.That(stage.Current is BattleScreen battle && battle.Confirm(), Is.True, "결과 창 [확인]");
            yield return null;
            Assert.That(flow.Battle, Is.Null, "배틀이 닫혔다");
            Assert.That(stage.Current is BattleScreen, Is.False);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
