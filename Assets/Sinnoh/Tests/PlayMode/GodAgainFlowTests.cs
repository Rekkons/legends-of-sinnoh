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
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 결정 198(2026-11-21 — 울트라 버스트 방 판에서 펄기아를 잡았더니 「한 번 더!」가 떴는데 턴이 넘어갔다): 신 격파(80행)의 배너 · 기록 줄은 추가 턴이 실제로 오는 때만 「한 번 더」다.
    /// 화면은 캠페인을 다시 따지지 않고 엔진의 「한 턴 더」 표(UsageMarked GodVictory — <see cref="GodBeats.Extra"/>)를 본다. 신오의 심장 판 = 「한 번 더!」 + 같은 좌석의 추가 턴,
    /// 울트라 버스트 판 = 「신을 넘어섰다!」 + 추가 턴 없음. 둘 다 시작의 사당의 기라티나(첫 폼)를 이긴다. UiShots <c>god-again-*</c>가 같은 판을 찍는다.
    /// </summary>
    public sealed class GodAgainFlowTests
    {
        const ulong Seed = 20261112, AiSeed = 7;

        [UnityTest]
        public IEnumerator M10_결정198_신오의심장판의신격파는한번더() => Run(Campaign.HeartOfSinnoh);

        [UnityTest]
        public IEnumerator M10_결정198_울트라버스트판의신격파는한번더가없다() => Run(Campaign.UltraBurst);

        IEnumerator Run(Campaign campaign)
        {
            bool heart = campaign == Campaign.HeartOfSinnoh;
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
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var ctx = flow.Ctx;
                flow.StartSeats(new SoloSettings { Campaign = campaign }, 1, Seed, AiSeed, state =>
                {
                    var st = state.Seats[0];
                    st.OpenSlots = st.Party.Length;
                    int starter = st.Party[0];
                    for (int i = 0; i < st.Party.Length; i++) st.Party[i] = -1;
                    foreach (string key in SoloTrail.Team) PokemonRules.AddToParty(st, PokemonRules.Spawn(state, ctx.Cards.PokemonIds.IndexOf(key), 0, (int)DeckId.Legendary));
                    if (starter >= 0 && PokemonRules.PartyCount(st) < st.OpenSlots) PokemonRules.AddToParty(st, starter);
                    int cell = HosFlow.OriginShrineCell(ctx);
                    st.Cell = cell;
                    state.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = ctx.Cards.PokemonIds.IndexOf("487:1"), Cell = cell, FaceUp = true });
                });
                yield return null;
                flow.Skip(flow.OpeningMs);
                for (int i = 0; i < 300 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty || !flow.InputOpen); i++) { flow.Skip(100); if (i % 10 == 0) yield return null; }
                int turn = flow.Client.View.TurnId;
                Assert.That(flow.Hud.Press(BoardHud.Search), Is.True, "[찾아보기] — 시작의 사당의 신");
                BattleScreen screen = null;
                for (int guard = 0; guard < 4000; guard++)
                {
                    screen = stage.Current as BattleScreen;
                    if (screen != null && screen.ShowsResult) break;
                    if (screen != null && screen.Open)
                    {
                        if (screen.AskingToken) screen.PressToken(true);
                        else if (screen.AskingOrder) screen.PressOrder(0);
                        else if (screen.AskingFate) screen.PressFatePass();
                        else if (screen.AskingAsk) screen.PressAsk(false);
                        else if (screen.AskingPick) { if (!screen.PressPick(-1)) screen.PressPick(0); }
                        else if (screen.Bench.Count > 0) { if (!screen.PressStay()) screen.PressBench(0); }
                        else if (screen.Moves.Count > 0)
                        {
                            if (screen.Moves[0].Target == BattleRules.AlphaStrike) { screen.OpenAlpha(); screen.PressAlpha(0); }
                            else screen.PressMove(0);
                        }
                    }
                    flow.Skip(500);
                    if (guard % 4 == 0) yield return null;
                }
                Assert.That(screen != null && screen.ShowsResult, Is.True, "결과 창");
                Assert.That(flow.Battle.Won, Is.True, "신을 이겼다");
                for (int i = 0; i < 40 && (screen.AskingReward || !screen.Ready); i++)
                {
                    if (screen.AskingReward) screen.PressReward(screen.Rewards.Count - 1);
                    flow.Skip(500);
                    yield return null;
                }
                FxStep god = null;
                foreach (var entry in flow.Fx.Sequencer.Queued) god = god ?? GodBeats.StepOf(entry);
                Assert.That(god, Is.Not.Null, "신 격파 단계(800)");
                Assert.That(screen.Confirm(), Is.True, "결과 창 [확인]");
                flow.Skip(flow.MsUntil(st => ReferenceEquals(st, god)) + 700);
                stage.Tweens.Advance(0);
                yield return null;
                var fx = flow.Results;
                Assert.That(fx, Is.Not.Null);
                Assert.That(fx.BannerShown, Is.True, "신 격파 배너");
                Assert.That(fx.BannerText, Is.EqualTo(stage.T(heart ? "screen.board.god.again" : "screen.board.god.done")), heart ? "추가 턴이 오면 「한 번 더!」" : "추가 턴이 없으면 「한 번 더!」가 아니다");
                bool again = false, done = false;
                foreach (var l in flow.Log.Lines)
                {
                    again |= l.Line != null && l.Line.Key == BoardLog.K + "god";
                    done |= l.Line != null && l.Line.Key == BoardLog.K + "god.done";
                }
                Assert.That(again, Is.EqualTo(heart), "기록 줄 「한 번 더」");
                Assert.That(done, Is.EqualTo(!heart), "기록 줄 「신을 넘어섰다」");
                // 엔진 사실과 같은가: 연출을 다 보낸 뒤 같은 좌석의 추가 턴(신오의 심장)인가
                for (int i = 0; i < 300 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty); i++) { flow.Skip(100); if (i % 10 == 0) yield return null; }
                var view = flow.Client.View;
                if (heart) Assert.That(view.ActiveSeat == 0 && view.TurnId > turn, Is.True, "신오의 심장: 같은 좌석의 추가 턴");
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var shut = Object.FindAnyObjectByType<ScreenBoot>();
                if (shut != null && shut.Root != null) shut.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }
    }
}
