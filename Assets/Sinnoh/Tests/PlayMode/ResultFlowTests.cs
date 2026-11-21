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
    /// 맵 결과·배틀 안 연출(M5 — 04 §8-8 26~30 · 46 · 74~79행): 사람 좌석이 화면의 함수로만 누르는 길.
    /// 혼자 판을 손봐(전설 특수기술을 가진 로토무가 맨 앞) 고향 마을의 조무래기에게 도전한다 — 기술 줄의 「알파 스트라이크」 → 타입 줄 → 고른 타입 색 오라(76행),
    /// 유물이 든 숨겨진 장소를 공개하면 결과 꼬리표와 날아가는 유물(30행). 받을 화면이 없는 선택은 0번이다. Gen 겉모양·무대 그림이 있어야 돈다.
    /// </summary>
    public sealed class ResultFlowTests
    {
        const ulong Seed = 20261112, AiSeed = 7;

        static IEnumerator Open()
        {
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
        }

        static void Wait(GameFlow flow)
        {
            for (int i = 0; i < 400 && !flow.InputOpen; i++) flow.Skip(100);
        }

        [UnityTest]
        public IEnumerator M5_알파스트라이크는타입줄에서고르고_유물은꼬리표와함께내판으로온다()
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
                yield return Open();
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
                Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);   // 1080 벌로 배치한다(글자 자리 단언)
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var ctx = flow.Ctx;
                int rotom = ctx.Cards.PokemonIds.IndexOf("479:1"), relic = ctx.Items.Ids.IndexOf("item.gracidea_flower");
                int spotCell = -1;
                flow.StartSeats(new SoloSettings(), 1, Seed, AiSeed, state =>
                {
                    // 맨 앞에 전설 특수기술을 가진 로토무(스타팅 포켓몬은 뒤로) · 가장 가까운 뒷면 숨겨진 장소를 유물(그라시데아 꽃)로
                    var st = state.Seats[0];
                    int starter = st.Party[0];
                    st.OpenSlots = st.Party.Length;
                    st.Party[0] = PokemonRules.Spawn(state, rotom, 0, (int)DeckId.Legendary);
                    st.Party[1] = starter;
                    state.Pokemon[st.Party[0]].HasLegendaryMove = true;
                    var from = MoveRules.Reachable(ctx.Board, st.Cell, 400, state.Options.ElevationMode);
                    TokenState goal = null;
                    foreach (var t in state.Tokens)
                        if (t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0 && from[t.Cell] > 0 && from[t.Cell] != MoveRules.Unreachable && (goal == null || from[t.Cell] < from[goal.Cell])) goal = t;
                    Assert.That(goal, Is.Not.Null, "닿을 수 있는 숨겨진 장소");
                    foreach (var t in state.Tokens)
                        if (t != goal && t.Kind == TokenKind.HiddenSpot && t.Def == relic) t.Def = goal.Def;
                    goal.Def = relic;
                    spotCell = goal.Cell;
                });
                yield return null;
                flow.Skip(flow.OpeningMs);
                Wait(flow);
                Assert.That(flow.InputOpen, Is.True, "첫 턴: 입력 열림");
                var client = flow.Client;

                // ── 30행: 유물이 든 숨겨진 장소까지 걸어가 [공개] → 뒤집힌 뒤 결과 단계 — 빛기둥 · 유물이 내 판으로 · 이름 꼬리표 ──
                for (int guard = 0; guard < 120; guard++)
                {
                    if (!flow.InputOpen) { flow.Skip(250); yield return null; continue; }
                    if (client.Choice(ChoiceKind.HiddenSpot) != null)
                    {
                        if (client.View.Seats[0].Cell == spotCell) break;
                        Assert.That(flow.Hud.Press(BoardHud.Pass), Is.True, "가는 길의 다른 숨겨진 장소는 지나간다");
                        yield return null;
                        continue;
                    }
                    var view = client.View;
                    int here = view.Seats[0].Cell, best = -1, bestLeft = int.MaxValue;
                    var reach = MoveRules.Reachable(ctx.Board, here, 400, view.Options.ElevationMode);
                    foreach (int cell in flow.Hud.Reach)
                    {
                        int left = cell == spotCell ? 0 : MoveRules.Reachable(ctx.Board, cell, 400, view.Options.ElevationMode)[spotCell];
                        if (left != MoveRules.Unreachable && left < bestLeft) { best = cell; bestLeft = left; }
                    }
                    if (best >= 0 && bestLeft < reach[spotCell]) Assert.That(flow.Hud.Walk(best), Is.True);
                    else Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True);
                    yield return null;
                }
                var ask = client.Choice(ChoiceKind.HiddenSpot);
                Assert.That(ask, Is.Not.Null, "숨겨진 장소에 멈추면 묻는다");
                Assert.That(client.View.Seats[0].Cell, Is.EqualTo(spotCell), "유물을 둔 칸");
                Assert.That(flow.Hud.Press(BoardHud.Reveal), Is.True);
                stage.Tweens.Advance(1);
                var result = ResultRows.SpotStep(flow.Fx.Sequencer.Current);
                Assert.That(result, Is.Not.Null, "결과 단계(30행)가 공개 단계 뒤에 선다");
                Assert.That(result.Row, Is.EqualTo(ResultRows.Relic));
                Assert.That(client.View.Seats[0].Bag, Does.Contain(relic), "보기에서는 이미 가방에 들었다");
                stage.Tweens.Advance(result.StartMs + 250 - flow.Fx.Sequencer.LocalMs);
                Assert.That(flow.Results, Is.Not.Null, "결과 층");
                Assert.That(flow.Results.GlowShown, Is.GreaterThan(0.5f), "빛기둥");
                Assert.That(flow.Results.LabelShown, Is.Null, "유물이 닿기 전에는 이름을 적지 않는다");
                stage.Tweens.Advance(SpotResultBeats.LiftMs + 350 - 250);
                Assert.That(flow.Results.FlyShown, Is.EqualTo(1), "유물이 호를 그려 날아간다");
                Assert.That(rig(boot).Tokens.RaisedDrawn, Is.False, "떠오른 뒤 토큰은 보드에 없다");
                stage.Tweens.Advance(SpotResultBeats.FlyMs + 100);
                Assert.That(flow.Results.LabelShown, Is.EqualTo(stage.T("item.gracidea_flower")), "닿으면 이름");
                Assert.That(flow.Results.SubShown, Does.Contain("+1"), "위력이 오르는 타입");
                Assert.That(flow.Results.LabelIconShown, Is.True, "꼬리표에 유물 그림(결정 80 L14 나)");
                for (int i = 0; i < 3; i++) yield return null;
                var bad = WorkUi.Overflows(flow.Results.Element);
                Assert.That(bad, Is.Empty, "결과 꼬리표: " + string.Join(" / ", bad));
                Wait(flow);
                Assert.That(flow.Results.LabelShown, Is.Null, "연출이 끝나면 걷힌다");

                // ── 76행: 조무래기에게 도전 → 기술 줄의 「알파 스트라이크」 → 타입 줄(배울 수 있는 타입마다) → 고른 타입 색 오라 ──
                // 고향 마을로 돌아가지 않고 판을 다시 연다(같은 손질 — 고향 마을 칸에서 시작한다)
                flow.StartSeats(new SoloSettings(), 1, Seed, AiSeed, state =>
                {
                    var st = state.Seats[0];
                    int starter = st.Party[0];
                    st.OpenSlots = st.Party.Length;
                    st.Party[0] = PokemonRules.Spawn(state, rotom, 0, (int)DeckId.Legendary);
                    st.Party[1] = starter;
                    state.Pokemon[st.Party[0]].HasLegendaryMove = true;
                });
                yield return null;
                flow.Skip(flow.OpeningMs);
                Wait(flow);
                client = flow.Client;
                Assert.That(flow.Hud.Press(BoardHud.Challenge), Is.True, "[도전]");
                BattleScreen battle = null;
                for (int guard = 0; guard < 400; guard++)
                {
                    battle = stage.Current as BattleScreen;
                    if (battle != null && battle.Open && battle.Bench.Count > 0) Assert.That(battle.PressBench(0), Is.True, "첫 출전 = 로토무");
                    else if (battle != null && battle.Open && battle.AskingToken) battle.PressToken(true);
                    else if (battle != null && battle.Open && battle.Moves.Count > 0) break;
                    flow.Skip(250);
                    yield return null;
                }
                Assert.That(battle, Is.Not.Null, "배틀 화면");
                int row = -1, strikes = 0;
                for (int i = 0; i < battle.Moves.Count; i++)
                    if (battle.Moves[i].Target == BattleRules.AlphaStrike) { row = i; strikes++; }
                Assert.That(strikes, Is.EqualTo(1), "알파 스트라이크는 기술 줄 한 줄(타입은 눌러서 고른다)");
                Assert.That(battle.AlphaOpen, Is.False);
                Assert.That(battle.PressMove(row), Is.True, "누르면 타입 줄이 열린다(답은 아직 내지 않는다)");
                Assert.That(battle.AlphaOpen, Is.True);
                Assert.That(client.Choice(ChoiceKind.BattleAction), Is.Not.Null, "아직 고르는 중");
                var types = new List<int>();
                foreach (var t in ctx.Cards.Pokemon[rotom].LearnTypes) if (!types.Contains((int)t)) types.Add((int)t);
                Assert.That(battle.AlphaTypes.Count, Is.EqualTo(types.Count), "배울 수 있는 타입마다 한 줄");
                for (int i = 0; i < 3; i++) yield return null;
                bad = WorkUi.Overflows(stage.Root.Q("battle-moves"));
                Assert.That(bad, Is.Empty, "타입 줄: " + string.Join(" / ", bad));
                Assert.That(battle.CloseAlpha(), Is.True, "[뒤로]");
                Assert.That(battle.AlphaOpen, Is.False);
                Assert.That(battle.OpenAlpha(), Is.True);
                var picked = battle.AlphaTypes[0];
                Assert.That(battle.PressAlpha(0), Is.True, "타입을 고르면 그 타입의 알파 스트라이크");
                FxStep used = null;
                foreach (var entry in flow.Fx.Sequencer.Queued)
                    foreach (var st in entry.Steps)
                        if (st.Row == 590 && st.Events.Exists(e => e is MoveUsed m && m.MoveKey == BattleRules.AlphaStrike)) used = st;
                Assert.That(used, Is.Not.Null, "알파 스트라이크의 기술 단계");
                Assert.That((int)((MoveUsed)used.Events.Find(e => e is MoveUsed)).Type, Is.EqualTo(picked.Values[0]), "고른 타입으로 친다");
                flow.Skip(flow.MsUntil(st => ReferenceEquals(st, used)) + 300);
                stage.Tweens.Advance(0);
                Assert.That(battle.PlayerAura, Is.EqualTo(AuraKind.AlphaStrike), "고른 타입 색 오라가 내 도트 둘레에");
                Assert.That(battle.Painted.Player.AuraPx, Is.GreaterThan(0));
                Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택은 없다");
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

        [UnityTest]
        public IEnumerator M9_전설장소를찾아보면_그칸의피규어가솟았다내려앉고_전설배틀이열린다()
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
                yield return Open();
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
                Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var ctx = flow.Ctx;
                // 그랜드 샤토(로토무 — 낮은 피규어)에 비밀키를 쥐고 선 혼자 판
                int cell = ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf("legendary_site.grand_chateau")].Cell, key = ctx.Items.Ids.IndexOf("item.secret_key");
                flow.StartSeats(new SoloSettings(), 1, Seed, AiSeed, state =>
                {
                    state.Seats[0].Cell = cell;
                    state.Seats[0].Bag.Add(key);
                });
                yield return null;
                flow.Skip(flow.OpeningMs);
                Wait(flow);
                Assert.That(flow.InputOpen, Is.True, "첫 턴: 입력 열림");
                Assert.That(flow.FigureRise(cell), Is.EqualTo(0f).Within(0.001f), "찾아보기 전: 피규어는 제자리");
                float top = flow.FigureRiseTop(cell);
                Assert.That(top, Is.GreaterThan(BoardFigures.RiseHeight), "낮은 피규어는 기본보다 높이 솟는다(M9 P25 — 가정 #510)");

                Assert.That(flow.Hud.Press(BoardHud.Search), Is.True, "[찾아보기](전설 장소 · 비밀키)");
                stage.Tweens.Advance(1);
                var seq = flow.Fx.Sequencer;
                var step = LegendSiteBeats.StepOf(seq.Current, out var started);
                Assert.That(step, Is.Not.Null, "전설 배틀의 조우 전환 단계(22행 — 앞 1000ms가 23행)");
                Assert.That(started.Cell, Is.EqualTo(cell));
                stage.Tweens.Advance(step.StartMs + LegendSiteBeats.RiseAtMs - 50 - seq.LocalMs);
                Assert.That(flow.FigureRise(cell), Is.EqualTo(0f).Within(0.001f), "땅이 울리기 전: 제자리");
                stage.Tweens.Advance(50 + LegendSiteBeats.RiseMs / 2);
                Assert.That(flow.FigureRise(cell), Is.EqualTo(top).Within(0.02f), "가장 높이 솟았다");
                stage.Tweens.Advance(LegendSiteBeats.RiseMs / 4);
                Assert.That(flow.FigureRise(cell), Is.GreaterThan(0.1f).And.LessThan(top - 0.1f), "내려앉는 중");
                stage.Tweens.Advance(LegendSiteBeats.RiseMs / 4 + 20);
                Assert.That(flow.FigureRise(cell), Is.EqualTo(0f).Within(0.001f), "내려앉았다(23행 끝)");
                for (int guard = 0; guard < 200 && !(stage.Current is BattleScreen); guard++)
                {
                    flow.Skip(100);
                    yield return null;
                }
                Assert.That(stage.Current, Is.InstanceOf<BattleScreen>(), "전환 뒤 전설 배틀 화면");
                Assert.That(flow.Battle.Kind, Is.EqualTo(BattleKind.Legendary));
                Assert.That(flow.FigureRise(cell), Is.EqualTo(0f).Within(0.001f), "배틀 중: 제자리");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택은 없다");
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

        static BoardBoot rig(ScreenBoot boot) => Object.FindAnyObjectByType<BoardBoot>();
    }
}
