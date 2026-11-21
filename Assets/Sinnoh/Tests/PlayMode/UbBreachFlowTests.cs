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
    /// 결정 185(11-21 실제 방 플레이): 내가 먼저 답하고 기다리는 동안 [확인]을 눌러도 묶음이 끝날 때까지 결과 창이 남아 뒤에 오는 UB 기술 가르치기를 받는다 · 남이 쓰러뜨린 판은 답 순서와 상관없이 내 턴이 온다. 결과 창 없이 남은 배틀 뒤 가르치기(다시 연 판)는 카드 보상 창이 받는다.
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
            if (b != null && b.Flow != null) b.Flow.HostPaced = false;
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
        int Prepare(GameState st, ulong rng, int seat = 0)
        {
            var ctx = flow.Ctx;
            var me = st.Seats[seat];
            me.OpenSlots = me.Party.Length;
            for (int i = 0; i < me.Party.Length; i++) me.Party[i] = -1;
            foreach (string key in SoloTrail.Team)
                PokemonRules.AddToParty(me, PokemonRules.Spawn(st, ctx.Cards.PokemonIds.IndexOf(key), seat, (int)DeckId.Legendary));
            int mine = me.Party[0], other = -1;
            foreach (int p in st.Seats[1 - seat].Party) if (p >= 0 && other < 0) other = p;
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

        /// <summary>
        /// 결정 189: 울트라 버스트 판의 UB · 네크로즈마 피규어마다 발밑 붉은 바닥 고리(자리 = 피규어의 땅) — 위 띠 균열 칩을 누르면 카메라의 보는 점이 UB의 땅으로 가고 그 고리만 번쩍이다 끝나면 제 색,
        /// 다시 누르면 다음 UB. 창이 떠 있으면(카메라가 키를 받지 않는 때) 누르기를 받지 않는다.
        /// </summary>
        [UnityTest]
        public IEnumerator M10_균열칩을누르면카메라가UB로가고고리가번쩍이며다시누르면다음UB_결정189()
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
            flow = boot.Flow;
            stage = boot.Root.Stage;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            flow.StartSeats(new SoloSettings { Campaign = Campaign.UltraBurst }, 2, 20261119, 7);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                Assert.That(flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == AnswerKind.EndTurn), Is.True, "남 좌석의 턴 종료");
                yield return Until(() => flow.Fx.Sequencer.IsEmpty, "턴 넘김");
            }
            yield return Until(MyMap, "내 턴");

            // ① 고리: 선 UB마다 하나, 자리 = 피규어의 땅
            var rings = UnityEngine.Object.FindAnyObjectByType<BeastRings>();
            Assert.That(rings, Is.Not.Null, "바닥 고리");
            int n = flow.BeastsShown.Count;
            Assert.That(n, Is.EqualTo(UbState.Beasts + 1), "UB 9 + 네크로즈마");
            Assert.That(rings.Points.Count, Is.EqualTo(n));
            for (int i = 0; i < n; i++) Assert.That((rings.Points[i] - flow.BeastGrounds[i]).sqrMagnitude, Is.LessThan(1e-6f), "고리 " + i + " = 그 UB의 땅");
            Assert.That(rings.Lit, Has.None.True, "누르기 전에는 번쩍이지 않는다");

            // ② 찾기: 칩 클릭 → 보는 점이 첫 UB의 땅 · 그 고리만 번쩍임 → 끝나면 제 색
            var rig = UnityEngine.Object.FindAnyObjectByType<BoardCameraRig>();
            var chip = stage.Root.Q(BoardUb.Gauge);
            Assert.That(chip, Is.Not.Null, "균열 칩");
            Assert.That(flow.CanFindBeast, Is.True, "보드 · 내 턴 · 창 없음");
            Click(chip);
            Assert.That(flow.Found, Is.EqualTo(flow.BeastsShown[0]), "첫 누름 = 첫 UB");
            Assert.That(flow.Finding, Is.True);
            flow.Skip(100);
            yield return null;
            Assert.That(rings.Lit[0], Is.True, "찾은 UB의 고리가 번쩍인다");
            for (int i = 1; i < n; i++) Assert.That(rings.Lit[i], Is.False, "다른 고리는 그대로");
            flow.Skip(GameFlow.FindMs);
            yield return null;
            Assert.That(flow.Finding, Is.False, "찾기가 끝났다");
            Assert.That((rig.Target - flow.BeastGrounds[0]).magnitude, Is.LessThan(1e-3f), "보는 점 = 첫 UB의 땅");
            Assert.That(rings.Lit, Has.None.True, "끝나면 제 색");

            // 다시 누르면 다음 UB
            Click(chip);
            Assert.That(flow.Found, Is.EqualTo(flow.BeastsShown[1]), "둘째 누름 = 다음 UB");
            flow.Skip(GameFlow.FindMs);
            yield return null;
            Assert.That((rig.Target - flow.BeastGrounds[1]).magnitude, Is.LessThan(1e-3f), "보는 점 = 둘째 UB의 땅");

            // 창이 떠 있으면(정비 판) 받지 않는다 [가정 — 카메라가 키를 받는 때와 같다]
            Assert.That(flow.OpenParty(), Is.True, "[정비]");
            yield return null;
            Assert.That(flow.CanFindBeast, Is.False, "창 위에서는 찾지 않는다");
            Assert.That(flow.FindBeast(), Is.False);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        /// <summary>
        /// 결정 194: 균열 카드 「무모한 돌격」이 붙은 내 포켓몬이 표 상성이 유리한 기술로 UB를 치면, 배틀 계산 줄의 상성 칸이 「상성 +n」 · 「무모한 돌격 +2」로 나뉜다(합 = 엔진의 상성).
        /// </summary>
        [UnityTest]
        public IEnumerator M10_무모한돌격이붙으면계산줄의상성칸이표상성과카드몫으로나뉜다_결정194()
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
            flow = boot.Flow;
            stage = boot.Root.Stage;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            flow.StartSeats(new SoloSettings { Campaign = Campaign.UltraBurst }, 2, 20261119, 7);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                Assert.That(flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == AnswerKind.EndTurn), Is.True, "남 좌석의 턴 종료");
                yield return Until(() => flow.Fx.Sequencer.IsEmpty, "턴 넘김");
            }
            yield return Until(MyMap, "내 턴");
            int sig = -1, table = 0;
            flow.Rebase(st => sig = Reckless(flow.Ctx, st, out table));
            yield return null;
            yield return Until(MyMap, "손본 판의 내 턴");
            Assert.That(flow.Hud.Press(BoardHud.Search), Is.True, "UB 칸에서 [찾아보기]");

            string card = stage.T(flow.Ctx.Decks.CardIds[BreachRules.CardOf(flow.Ctx, BreachKind.RecklessAssault)]);
            bool seen = false;
            for (int i = 0; i < 3000 && !seen; i++)
            {
                var b = stage.Current as BattleScreen;
                if (b == null && flow.Battle == null) Assert.Fail("배틀 화면이 열리지 않았다");
                if (b != null && b.ShowsResult) break;
                var split = stage.Root.Q("battle-calc-matchup-1");
                if (b != null && split != null && split.resolvedStyle.visibility == Visibility.Visible)
                {
                    var labels = split.Query<Label>().ToList();
                    Assert.That(labels[0].text, Is.EqualTo(card), "둘째 상성 칸 이름 = 무모한 돌격");
                    Assert.That(labels[1].text, Is.EqualTo("+" + BreachRules.RecklessBonus), "카드 몫 +2");
                    var first = stage.Root.Q("battle-calc-matchup-0").Query<Label>().ToList();
                    Assert.That(first[0].text, Is.EqualTo(stage.T("screen.battle.calc.effectiveness")), "첫 상성 칸 = 표 상성");
                    Assert.That(first[1].text, Does.Match(@"^\+[1-9]"), "표 상성 값(유리 — 카드는 유리한 상성에만 붙는다)");
                    Assert.That(b.CalcMatchups, Is.EqualTo(2));
                    seen = true;
                    break;
                }
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
                        int row = 0;
                        for (int k = 0; k < b.Moves.Count; k++) if (b.Moves[k].Target == sig) row = k;
                        b.PressMove(row);
                    }
                }
                flow.Skip(100);
                yield return null;
            }
            Assert.That(seen, Is.True, "계산 줄에 무모한 돌격 칸이 섰다(표 상성 " + table + ")");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        /// <summary>
        /// 결정 194 확인 판: 내 파티 = 체력이 가장 작은 UB에게 고유 기술의 표 상성이 유리한(&gt; 0) 포켓몬 중 체력이 가장 큰 것 하나 + 그 포켓몬 첫 칸에 「무모한 돌격」 ·
        /// 그 UB 칸에 선다 · 프리즘 아머를 치운다. 그 포켓몬의 고유 기술 · 표 상성을 돌려준다.
        /// </summary>
        internal static int Reckless(Ctx ctx, GameState st, out int table)
        {
            TokenState weak = null;
            foreach (var t in st.Tokens)
                if (t.Kind == TokenKind.UltraBeast && t.Cell >= 0 && (weak == null || ctx.Cards.Pokemon[t.Def].Health < ctx.Cards.Pokemon[weak.Def].Health)) weak = t;
            Assert.That(weak, Is.Not.Null, "UB 토큰");
            var ub = ctx.Cards.Pokemon[weak.Def].Types;
            int pick = -1;
            table = 0;
            for (int i = 0; i < ctx.Cards.Pokemon.Count; i++)
            {
                var def = ctx.Cards.Pokemon[i];
                int m = def.Signature;
                if (m < 0 || m >= ctx.Cards.Moves.Count || ctx.Cards.Moves[m].AttackStrength == null) continue;
                var type = BattleRules.MoveType(ctx, m);
                int bonus = type == PokemonType.Typeless ? 0 : TypeChart.Bonus(type, ub[0], ub.Count > 1 ? ub[1] : PokemonType.Typeless);
                if (bonus <= 0 || (pick >= 0 && def.Health <= ctx.Cards.Pokemon[pick].Health)) continue;
                pick = i;
                table = bonus;
            }
            Assert.That(pick, Is.GreaterThanOrEqualTo(0), "유리한 포켓몬");
            var me = st.Seats[0];
            for (int i = 0; i < me.Party.Length; i++) me.Party[i] = -1;
            int mon = PokemonRules.Spawn(st, pick, 0, (int)DeckId.Legendary);
            PokemonRules.AddToParty(me, mon);
            st.Pokemon[mon].Breach[0] = BreachRules.CardOf(ctx, BreachKind.RecklessAssault);
            st.Ub.PrismUltraBeast = 0;
            st.Ub.PrismUltraBurst = UbState.PrismArmourTotal;
            me.Cell = weak.Cell;
            return ctx.Cards.Pokemon[pick].Signature;
        }

        static void Click(VisualElement e)
        {
            using (var ev = ClickEvent.GetPooled())
            {
                ev.target = e;
                e.SendEvent(ev);
            }
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

        // 2좌석 UB 판 → 내 턴 → UB 칸에서 [찾아보기] → 이길 때까지 → 결과 창 위의 균열 카드 버리기·옮기기 창. 내 첫 포켓몬을 돌려준다
        IEnumerator WinUb(bool hostPaced, Action<int> mine)
        {
            yield return Boot();
            int mon = -1;
            for (ulong rng = 1; rng <= 8; rng++)
            {
                flow.StartSeats(new SoloSettings { Campaign = Campaign.UltraBurst }, 2, 20261119, 7);
                flow.HostPaced = hostPaced;
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
                flow.HostPaced = hostPaced;
                yield return null;
                yield return Until(MyMap, "손본 판의 내 턴");
                Assert.That(flow.Hud.Press(BoardHud.Search), Is.True, "UB 칸에서 [찾아보기]");
                yield return Fight();
                if (flow.Battle.Won) break;
            }
            Assert.That(flow.Battle != null && flow.Battle.Won, Is.True, "UB를 이기는 판을 찾지 못했다");
            yield return Until(() => flow.Top is BreachScreen && flow.BattleOver, "결과 창 위의 균열 카드 창");
            mine(mon);
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

        // 좌석 2(남)의 열린 선택(호스트 사본) — 없으면 null
        PendingChoice OtherChoice() => flow.Session.HostCopy().Pending.Find(c => c.Seat == 1 && !c.Answered);

        // 좌석 2가 낼 답: 배틀은 첫 기술, 그 밖은 기본 답
        ChoiceAnswer OtherAnswer(PendingChoice c)
        {
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(flow.Session.HostCopy(), flow.Ctx, c, legal);
            return legal.Find(a => a.Kind == AnswerKind.UseMove) ?? c.Default.Clone();
        }

        bool Release(PendingChoice c) => c != null && BreachScreen.Owns(c) && c.Subject == BreachRules.Release;

        // 결정 185의 남 쪽 화면: 좌석 2가 UB를 쓰러뜨린 판 — 그 좌석이 버리기·옮기기에 먼저 답하고 내가 나중에 답해도(또는 반대), 가르치기 뒤 턴이 나에게 넘어와 맵 행동을 누를 수 있다
        [UnityTest]
        public IEnumerator M10_결정185_남이UB를쓰러뜨린뒤균열카드창_답순서와상관없이내턴이와서맵행동을누를수있다_에러로그0([Values(false, true)] bool otherFirst)
        {
            yield return Boot();
            int theirs = -1, mine = -1;
            bool won = false;
            for (ulong rng = 1; rng <= 8 && !won; rng++)
            {
                flow.StartSeats(new SoloSettings { Campaign = Campaign.UltraBurst }, 2, 20261119, 7);
                flow.HostPaced = true;   // 방 판처럼(방장 시각)
                for (int i = 0; i < 6; i++) yield return null;
                flow.Skip(flow.OpeningMs);
                for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != 1; k++)
                {
                    yield return Until(MyMap, "내 턴");
                    var map = flow.Client.Choice(ChoiceKind.MapAction);
                    Assert.That(flow.Answer(map, flow.Client.Legal(map).Find(a => a.Kind == AnswerKind.EndTurn)).Accepted, Is.True, "내 턴 종료");
                    yield return Until(() => flow.Fx.Sequencer.IsEmpty, "턴 넘김");
                }
                Assert.That(flow.Client.View.ActiveSeat, Is.EqualTo(1), "좌석 2의 턴");
                ulong at = rng;
                flow.Rebase(st =>
                {
                    theirs = Prepare(st, at, 1);
                    foreach (int p in st.Seats[0].Party) if (p >= 0 && mine < 0) mine = p;
                });
                flow.HostPaced = true;
                yield return null;
                Assert.That(flow.AnswerSeat(1, a => a.Kind == AnswerKind.Search), Is.True, "좌석 2가 UB 칸에서 찾아보기");
                // 좌석 2가 첫 기술로 싸운다 — 내 운명 반응 창 등은 넘긴다
                for (int i = 0; i < 3000; i++)
                {
                    var c = OtherChoice();
                    if (Release(c)) { won = true; break; }
                    var me = flow.Client.FirstChoice();
                    if (me != null && !BreachScreen.Owns(me))
                    {
                        var legal = flow.Client.Legal(me);
                        flow.Answer(me, legal.Find(a => a.Kind == AnswerKind.Decline) ?? me.Default.Clone());
                    }
                    else if (c != null && c.Kind != ChoiceKind.MapAction) flow.AnswerSeatWith(1, OtherAnswer(c));
                    else if (c != null) break;   // 졌다(맵 행동으로 돌아옴)
                    flow.Skip(200);
                    yield return null;
                }
            }
            Assert.That(won, Is.True, "좌석 2가 UB를 이기는 판을 찾지 못했다");

            // ── 묶음: 남이 먼저(실제 방 기록 311 · 312) 또는 내가 먼저 ──
            yield return Until(() => flow.Top is BreachScreen, "내 균열 카드 창");
            var discard = ChoiceAnswer.Pick(theirs, 0);
            if (otherFirst)
            {
                Assert.That(flow.AnswerSeatWith(1, discard), Is.True, "좌석 2가 먼저 [버리기]");
                for (int i = 0; i < 5; i++) { flow.Skip(200); yield return null; }
            }
            yield return Until(() => flow.Top is BreachScreen w0 && BreachScreen.ChoiceOf(flow.Client) != null && flow.AnswerOpen, "내 창의 단추가 듣는다");
            var w = (BreachScreen)flow.Top;
            Assert.That(w.PressSlot(mine, 0), Is.True, "내 카드 칸");
            Assert.That(w.PressDiscard(), Is.True, "내 [버리기]");
            if (!otherFirst)
            {
                for (int i = 0; i < 5; i++) { flow.Skip(200); yield return null; }
                Assert.That(flow.AnswerSeatWith(1, discard), Is.True, "좌석 2가 나중에 [버리기]");
            }

            // ── 좌석 2의 배틀 뒤 선택(가르치기)을 넘기면 좌석 2의 턴이 끝나고(라운드 끝의 UB 턴 건너뛰기 물음은 둘 다 기본 답) 내 턴이 온다: 결과 창이 남았으면 [확인] ──
            for (int i = 0; i < 900 && !MyMap(); i++)
            {
                var c = OtherChoice();
                if (c != null && c.Kind == ChoiceKind.Reward) flow.AnswerSeatWith(1, ChoiceAnswer.Of(AnswerKind.Decline));
                else if (c != null && c.Kind != ChoiceKind.MapAction) flow.AnswerSeatWith(1, c.Default.Clone());
                var me = flow.Client.FirstChoice();
                if (me != null && me.Kind != ChoiceKind.MapAction && flow.BoardOpen) flow.Answer(me, me.Default.Clone());
                if (stage.Current is BattleScreen b && b.ShowsResult && !b.AskingReward) b.Confirm();
                flow.Skip(200);
                yield return null;
            }
            Assert.That(MyMap(), Is.True, "내 턴의 맵 행동 — 화면 " + (stage.Current != null ? stage.Current.Name : "보드") + " · 위 " + (flow.Top != null ? flow.Top.Name : "없음") + " · 차례 " + flow.Client.View.ActiveSeat + " · 남 선택 " + (OtherChoice() != null ? OtherChoice().Kind + "/" + OtherChoice().Sub : "없음"));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // 결정 185 마무리: 결과 창 없이 배틀 뒤 선택(기술 가르치기 — Window = BattleEnd)이 남은 판(재접속 · 다시 연 판과 같은 꼴 — 따라가는 배틀이 없다)을 연다.
        // 카드 보상 창(「배틀 보상」)이 받고, 받을 화면이 없는 선택은 없다(NotReadyShown 0) · 답하면 그 턴이 끝나 좌석 2의 턴이 온다
        [UnityTest]
        public IEnumerator M10_결정185_결과창없이남은배틀뒤가르치기는보상창이받고_답하면턴이넘어간다_에러로그0()
        {
            yield return Boot();
            flow.StartSeats(new SoloSettings { Campaign = Campaign.UltraBurst }, 2, 20261119, 7);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                Assert.That(flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == AnswerKind.EndTurn), Is.True, "남 좌석의 턴 종료");
                yield return Until(() => flow.Fx.Sequencer.IsEmpty, "턴 넘김");
            }
            yield return Until(MyMap, "내 턴");
            // 배틀이 끝나 가르치기만 남은 판으로 손본다: 맵 행동 대신 배틀 끝 가르치기(내 파티가 배울 수 있는 첫 기술)
            int taught = -1;
            flow.Rebase(st =>
            {
                var ctx = flow.Ctx;
                st.Pending.RemoveAll(c => c.Kind == ChoiceKind.MapAction);
                for (int move = 0; move < ctx.Cards.Moves.Count && taught < 0; move++)
                {
                    var c = new PendingChoice { Kind = ChoiceKind.Reward, Sub = ChoiceSub.TeachMove, Seat = 0, Subject = move, Window = Sinnoh.Rules.Window.BattleEnd };
                    var legal = new List<ChoiceAnswer>();
                    ChoiceRules.Legal(st, ctx, c, legal);
                    if (legal.Count <= 1) continue;
                    ChoiceRules.Open(st, ctx, new Emitter(st, null), c);   // 이벤트 없이(손본 판)
                    taught = move;
                }
            });
            Assert.That(taught, Is.GreaterThanOrEqualTo(0), "가르칠 수 있는 기술");
            yield return Until(() => stage.Current is CardRewardScreen && flow.BoardOpen, "결과 창 없이 남은 가르치기를 보상 창이 받는다");
            Assert.That(flow.Battle, Is.Null, "따라가는 배틀이 없다");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택이 없다");
            Assert.That(stage.Root.Q<Label>("board-card-reward-head").text.Replace("⁠", ""), Is.EqualTo(stage.T("screen.battle.reward.head").Replace("⁠", "")), "머리 = 배틀 보상");
            var reward = (CardRewardScreen)stage.Current;
            int skip = -1;
            for (int i = 0; i < reward.Answers.Count; i++) if (reward.Answers[i].Kind == AnswerKind.Decline) skip = i;
            Assert.That(skip, Is.GreaterThanOrEqualTo(0), "[가르치지 않기]");
            Assert.That(stage.Root.Q<Button>("board-card-reward-" + (skip + 1)), Is.Not.Null, "[가르치지 않기] 단추");
            Assert.That(reward.Press(skip), Is.True, "[가르치지 않기]");
            yield return Until(() => !(stage.Current is CardRewardScreen) && flow.Client.View.Pending.Exists(c => c.Seat == 1 && c.Kind == ChoiceKind.MapAction), "창이 닫히고 턴이 좌석 2로 넘어간다");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // 결정 185(11-21 실제 방 플레이 「ksmk9699의 턴에서 넘어갈 수 없어」): UB를 쓰러뜨린 좌석이 버리기·옮기기에 먼저 답하고 남이 나중에 답한 판 —
        // 배틀 뒤 선택(UB 기술 가르치기)을 결과 창에서 받고 [확인]으로 보드에 돌아가 턴이 넘어간다(T22의 반대 순서)
        [UnityTest]
        public IEnumerator M10_결정185_UB격파뒤균열카드창에내가먼저답하고남이나중에답해도_가르치기와확인으로턴이넘어간다_에러로그0([Values(false, true)] bool hostPaced)
        {
            int mon = -1;
            yield return WinUb(hostPaced, m => mon = m);
            var w = (BreachScreen)flow.Top;
            Assert.That(w.PressSlot(mon, 0), Is.True, "붙은 카드 칸을 고른다");
            Assert.That(w.PressDiscard(), Is.True, "내가 먼저 [버리기]");
            for (int i = 0; i < 5; i++)
            {
                flow.Skip(200);
                yield return null;
            }
            Assert.That(BreachScreen.Active(flow.Client.View), Is.True, "남이 아직 고르는 중");
            // 기다리는 동안 창을 접고 결과 창 [확인]을 누른다(실제 방에서 손님이 한 일 — 그때 배틀 화면이 닫혀 뒤에 온 가르치기를 받을 창이 없었다): 묶음이 끝날 때까지 닫지 않는다
            if (flow.Top is BreachScreen waiting && !waiting.Folded) Assert.That(waiting.ToggleFold(), Is.True, "기다리는 창 접기");
            yield return null;
            var held = stage.Current as BattleScreen;
            Assert.That(held != null && held.ShowsResult, Is.True, "결과 창");
            bool closed = held.Confirm();
            yield return null;
            Assert.That(flow.Battle != null && ReferenceEquals(stage.Current, held), Is.True, "묶음이 끝나기 전 [확인]은 결과 창을 닫지 않는다 — 가르치기가 올 곳");
            Assert.That(closed, Is.False);
            Assert.That(stage.Root.Q<Button>("battle-result-ok") is Button ok && !ok.enabledSelf, Is.True, "묶음이 끝날 때까지 [확인]이 꺼져 있다");
            Assert.That(flow.AnswerSeat(1, a => a.Kind == AnswerKind.Decline), Is.True, "좌석 2가 나중에 [안 함]");
            yield return Until(() => !BreachScreen.Active(flow.Client.View) && !(flow.Top is BreachScreen), "묶음이 끝나 창이 닫힌다");
            Assert.That(flow.Client.View.Pokemon[mon].Breach[0], Is.LessThan(0), "카드를 버렸다");

            // 배틀 뒤 선택(UB 기술 가르치기)은 결과 창이 받는다 — 화면의 단추로 넘긴다
            for (int i = 0; i < 600 && flow.Client.Choice(ChoiceKind.Reward) != null; i++)
            {
                if (stage.Current is BattleScreen b && b.AskingReward)
                {
                    int skip = -1;
                    for (int k = 0; k < b.Rewards.Count; k++) if (b.Rewards[k].Kind == AnswerKind.Decline) skip = k;
                    b.PressReward(skip >= 0 ? skip : 0);
                }
                flow.Skip(200);
                yield return null;
            }
            Assert.That(flow.Client.Choice(ChoiceKind.Reward), Is.Null, "배틀 뒤 선택을 결과 창에서 답했다 — 화면 " + (stage.Current != null ? stage.Current.Name : "보드"));
            yield return Until(() => stage.Current is BattleScreen bs && bs.ShowsResult && !bs.AskingReward, "결과 창 [확인]을 누를 수 있다");
            Assert.That(((BattleScreen)stage.Current).Confirm(), Is.True, "결과 창 [확인]");
            yield return null;
            Assert.That(flow.Battle, Is.Null, "배틀이 닫혔다");
            yield return Until(() => flow.Client.View.ActiveSeat == 1 && flow.Client.View.Pending.Exists(c => c.Seat == 1 && c.Kind == ChoiceKind.MapAction), "턴이 좌석 2로 넘어간다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
