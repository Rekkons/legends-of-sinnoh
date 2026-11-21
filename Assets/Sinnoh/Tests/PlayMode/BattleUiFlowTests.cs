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
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 배틀 화면 묶음(2026-11-21 — 결정 169 · 170 · 171 + E 기권 확인)을 실행 중인 화면 흐름으로 본다: 포켓몬 고르기 칸의 마우스 오버 카드(기록 기둥 위 — 독 · DS를 가리지 않는다) ·
    /// 차례 칸으로 옮긴 [운명의 변화] · 계산 줄은 맞는 순간(59행)부터 · 누가 맞았나(준/받은 피해) · E 한 번 = 안내, 두 번 = 기권. UiShots <c>battle-mon-tip*</c> · <c>battle-fate-turn</c> · <c>battle-calc-*</c> · <c>battle-forfeit-key</c>가 같은 판을 찍는다.
    /// </summary>
    public sealed class BattleUiFlowTests
    {
        const ulong Seed = 20261112, AiSeed = 7;

        ScreenBoot boot;
        GameFlow flow;
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
                boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            flow = boot.Flow;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            var board = Object.FindAnyObjectByType<BoardBoot>();
            if (board == null || board.Pieces.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저");
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

        // 혼자 판 · 손본 파티(lead 체력 leadHp — 0이면 그대로) → 고향 마을 조무래기에게 [도전]
        IEnumerator Grunts(string lead, int leadHp, params string[] bench)
        {
            var ctx = flow.Ctx;
            flow.StartSeats(new SoloSettings(), 1, Seed, AiSeed, state =>
            {
                var st = state.Seats[0];
                st.OpenSlots = st.Party.Length;
                int starter = st.Party[0];
                for (int i = 0; i < st.Party.Length; i++) st.Party[i] = -1;
                var keys = new List<string> { lead };
                keys.AddRange(bench);
                foreach (string key in keys) PokemonRules.AddToParty(st, PokemonRules.Spawn(state, ctx.Cards.PokemonIds.IndexOf(key), 0, (int)DeckId.Legendary));
                if (starter >= 0 && PokemonRules.PartyCount(st) < st.OpenSlots) PokemonRules.AddToParty(st, starter);
                var first = state.Pokemon[st.Party[0]];
                if (leadHp > 0) first.Damage = Math.Max(0, PokemonRules.MaxHp(ctx, first) - leadHp);
            });
            for (int i = 0; i < 4; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            for (int i = 0; i < 300 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty || !flow.InputOpen); i++)
            {
                flow.Skip(100);
                if (i % 10 == 0) yield return null;
            }
            yield return null;
            Assert.That(flow.Hud.Press(BoardHud.Challenge), Is.True, "[도전](고향 마을의 조무래기)");
        }

        // seen이 참이 될 때까지 싸운다(첫 기술 · 첫 칸 · 넘기기). step = 한 번에 보내는 ms. check = 매 걸음 뒤에 보는 것
        IEnumerator Fight(Func<BattleScreen, bool> seen, int step = 500, Action<BattleScreen> check = null)
        {
            for (int guard = 0; guard < 8000; guard++)
            {
                var screen = flow.Stage.Current as BattleScreen;
                if (screen != null && seen(screen)) yield break;
                if (screen != null && screen.ShowsResult) Assert.Fail("그 모습이 되기 전에 배틀이 끝났다");
                if (screen != null && screen.Open)
                {
                    if (screen.AskingToken) screen.PressToken(true);
                    else if (screen.AskingOrder) screen.PressOrder(0);
                    else if (screen.AskingFate) screen.PressFatePass();
                    else if (screen.AskingAsk) screen.PressAsk(false);
                    else if (screen.AskingPick) { if (!screen.PressPick(-1)) screen.PressPick(0); }
                    else if (screen.Bench.Count > 0) { if (!screen.PressStay()) screen.PressBench(0); }
                    else if (screen.Moves.Count > 0) screen.PressMove(0);
                }
                flow.Skip(step);
                if (screen != null) check?.Invoke(screen);
                if (guard % 8 == 0) yield return null;
            }
            Assert.Fail("그 모습까지 가지 못했다");
        }

        static bool MyTurn(BattleScreen b) => b.Open && b.Moves.Count > 0 && !b.AskingToken && !b.AskingOrder && !b.AskingFate && !b.AskingAsk && !b.AskingPick && b.Bench.Count == 0;

        static void Apart(VisualElement root, Rect card, string other)
        {
            var o = root.Q(other);
            Assert.That(o, Is.Not.Null, other);
            Assert.That(o.worldBound.Overlaps(card), Is.False, "카드가 " + other + "을 가린다(카드 " + card + " · " + o.worldBound + ")");
        }

        [UnityTest]
        public IEnumerator M10_결정169_170_포켓몬고르기칸에마우스를올리면그카드가기록기둥위에뜨고운명단추는차례칸아래에있다()
        {
            yield return Boot();
            yield return Grunts("483:2", 0, "484:2", "487:2");
            yield return Fight(b => b.Open && b.Bench.Count >= 2 && flow.Client.FirstChoice() != null && flow.Client.FirstChoice().Kind == ChoiceKind.SendIn);
            var screen = (BattleScreen)flow.Stage.Current;
            var root = flow.Stage.Root;
            for (int i = 0; i < 3; i++) yield return null;

            // 결정 169: 첫 출전 고르기의 둘째 칸 = 그 포켓몬의 카드(체력 · 스피드 · 타입 · ★ 고유 기술 + 배운 칸 · 효과 문구)
            int id = screen.Bench[1].Target;
            var pk = flow.Client.View.Pokemon[id];
            Assert.That(screen.HoverMon("battle-bench-2"), Is.True, "칸에 카드가 걸려 있다");
            Assert.That(screen.HoverMon("battle-nothing"), Is.False);
            Assert.That(screen.MonTipShown, Is.EqualTo(pk.Def), "그 칸의 포켓몬");
            for (int i = 0; i < 3; i++) yield return null;
            var tip = screen.MonTipElement;
            Assert.That(tip.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(tip.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible), "크기가 정해진 뒤 자리를 잡아 보인다");
            Assert.That(tip.Q("mon-tip-name"), Is.Not.Null);
            Assert.That(tip.Q("mon-tip-stats"), Is.Not.Null, "체력 · 스피드");
            Assert.That(tip.Q("mon-tip-learn").Query(className: "sn-type-icon").ToList().Count, Is.EqualTo(flow.Ctx.Cards.Pokemon[pk.Def].LearnTypes.Count), "배울 수 있는 타입(결정 182 — 알파 스트라이크의 타입)");
            for (int k = 0; k <= pk.Moves.Length; k++) Assert.That(tip.Q("mon-tip-move-" + k), Is.Not.Null, "기술 칸 " + k + "(0 = ★ 고유 기술)");
            Assert.That(tip.Q(className: "sn-mon-text"), Is.Not.Null, "기술 효과 문구");
            var card = tip.worldBound;
            var screenBox = root.worldBound;
            Assert.That(card.xMin >= screenBox.xMin - 0.5f && card.yMin >= screenBox.yMin - 0.5f && card.xMax <= screenBox.xMax + 0.5f && card.yMax <= screenBox.yMax + 0.5f, Is.True, "화면 안(" + card + ")");
            Apart(root, card, "battle-dock");
            Apart(root, card, "battle-ds-frame");
            var log = root.Q("battle-log").worldBound;
            Assert.That(card.xMin >= log.xMin - 0.5f && card.xMax <= log.xMax + 0.5f, Is.True, "기록 기둥 폭 안(" + card + " · " + log + ")");
            // 칸을 누르면(답) 카드가 걷힌다(칸이 떨어진다)
            Assert.That(screen.PressBench(0), Is.True);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(screen.MonTipShown, Is.EqualTo(-1), "칸이 사라지면 카드도 걷힌다");

            // 결정 170: 내 차례 — [운명의 변화]는 차례 칸 맨 아래(스피드 줄 아래), 기둥에는 없다
            yield return Fight(MyTurn);
            for (int i = 0; i < 3; i++) yield return null;
            var fate = root.Q(BattleScreen.FateButton);
            Assert.That(fate, Is.Not.Null, "[운명의 변화]");
            Assert.That(root.Q("battle-turn").Contains(fate), Is.True, "독 차례 칸 안");
            Assert.That(root.Q("battle-info").Q(BattleScreen.FateButton), Is.Null, "기둥에는 없다");
            Assert.That(fate.worldBound.yMin, Is.GreaterThanOrEqualTo(root.Q("battle-turn-speed").worldBound.yMax - 0.5f), "스피드 줄 아래");
            Assert.That(fate.worldBound.yMax, Is.LessThanOrEqualTo(root.Q("battle-turn").worldBound.yMax + 0.5f), "차례 칸 안");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_결정171_피해합계는맞는순간에서고준피해와받은피해가갈린다()
        {
            yield return Boot();
            yield return Grunts("129:0", 0, "483:2");   // 잉어킹이 앞에 — 조무래기가 때리는 것(받은 피해)도 본다
            var root = flow.Stage.Root;
            int early = 0, dealt = 0, taken = 0;
            void Check(BattleScreen b)
            {
                var cur = flow.Fx.Sequencer.Current;
                var hit = cur != null ? cur.Step(590) : null;
                var calc = cur != null ? cur.Step(611) : null;
                if (hit == null || calc == null || calc.Events.Count == 0) return;
                int local = flow.Fx.Sequencer.LocalMs;
                var dmg = (DamageBreakdown)calc.Events[0];
                if (dmg.Steps == null) return;
                Assert.That(b.CalcFromMs, Is.EqualTo(hit.StartMs), "계산 줄은 59행 시작부터");
                if (local < hit.StartMs + PlayTiming.DamageCalcMs.Value || local >= calc.StartMs) return;
                // 맞는 순간 + 350 ~ 계산 단계 전(상성 60행 동안): 이 공격의 데미지가 이미 서 있다
                var total = root.Q("battle-calc-total");
                Assert.That(total, Is.Not.Null, "맞은 뒤의 데미지 칸");
                Assert.That(total.style.visibility.value, Is.Not.EqualTo(Visibility.Hidden), "합계가 59행 + 350에 선다(61행을 기다리지 않는다)");
                Assert.That(total.Query<Label>().ToList()[1].text, Is.EqualTo(dmg.Final.ToString()));
                bool mine = Array.IndexOf(flow.Client.View.Seats[flow.Client.Seat].Party, dmg.TargetId) >= 0;
                Assert.That(b.CalcTaken, Is.EqualTo(mine), "준/받은 피해");
                Assert.That(root.Q("battle-calc-body").ClassListContains("sn-battle-calc-taken"), Is.EqualTo(mine), "받은 피해는 오른쪽");
                Assert.That(root.Q("battle-calc-target").ClassListContains("sn-battle-calc-tag-foe"), Is.EqualTo(!mine), "상대 = 붉은 이름표");
                Assert.That(root.Q("battle-calc-face").Q(className: "sn-mon"), Is.Not.Null, "맞은 포켓몬의 작은 도트");
                early++;
                if (mine) taken++;
                else dealt++;
            }
            yield return Fight(b => (dealt > 0 && taken > 0) || b.ShowsResult, 50, Check);
            Assert.That(early, Is.GreaterThan(0), "맞는 순간 뒤 · 계산 단계 전의 모습을 본 적이 있다");
            Assert.That(taken, Is.GreaterThan(0), "받은 피해를 본 적이 있다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_E기권은두번눌러야하고한번이면안내만뜬다()
        {
            yield return Boot();
            yield return Grunts("129:0", 1, "483:2");   // 체력 1인 잉어킹이 쓰러진 뒤 기권이 켜진다
            yield return Fight(b => MyTurn(b) && b.ForfeitOn);
            var screen = (BattleScreen)flow.Stage.Current;
            var root = flow.Stage.Root;
            int commands = flow.Session.CommandCount;
            Assert.That(screen.KeyTarget('E'), Is.SameAs(root.Q("battle-forfeit")), "E = [기권]");

            Assert.That(flow.BattleKey('E'), Is.True, "E를 받았다");
            Assert.That(screen.ForfeitArmed, Is.True, "한 번 = 안내만");
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "답이 나가지 않았다");
            Assert.That(flow.Stage.Current, Is.SameAs(screen));
            Assert.That((root.Q("battle-guide") as TextElement)?.text.Replace("⁠", ""), Is.EqualTo(flow.Stage.T("screen.battle.forfeit.again")), "차례 칸 안내 한 줄");

            Assert.That(flow.BattleKey('E'), Is.True, "E 다시");
            Assert.That(screen.ForfeitArmed, Is.False);
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1), "두 번째 E = 기권 답");
            for (int i = 0; i < 400 && !screen.ShowsResult; i++)
            {
                flow.Skip(200);
                if (i % 10 == 0) yield return null;
            }
            Assert.That(screen.ShowsResult, Is.True, "기권해 배틀이 끝났다");
            Assert.That(flow.Battle.Won, Is.False);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_배틀화면이떠있는동안보드HUD자원줄은숨고닫으면다시보인다()
        {
            yield return Boot();
            yield return Grunts("129:0", 1, "483:2");
            var root = flow.Stage.Root;
            var res = root.Q(BoardHud.Res);
            Assert.That(res, Is.Not.Null, "자원 줄");
            yield return Fight(b => MyTurn(b) && b.ForfeitOn);
            var screen = (BattleScreen)flow.Stage.Current;
            for (int i = 0; i < 3; i++) yield return null;
            // 자원 줄은 아래 판 위로 솟아 배틀 독보다 높다 — 반투명 덮개 아래로 글자가 비치지 않게 숨는다
            Assert.That(res.resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden), "배틀 화면이 떠 있는 동안 숨는다");
            Assert.That(flow.BattleKey('E') && flow.BattleKey('E'), Is.True, "E 두 번 = 기권");
            for (int i = 0; i < 400 && !screen.ShowsResult; i++)
            {
                flow.Skip(200);
                if (i % 10 == 0) yield return null;
            }
            Assert.That(screen.ShowsResult, Is.True);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(res.resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden), "결과 창 동안도");
            Assert.That(screen.Confirm(), Is.True, "[확인]");
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(flow.Stage.Current is BattleScreen, Is.False, "배틀 화면이 닫혔다");
            Assert.That(root.Q(BoardHud.Res).resolvedStyle.visibility, Is.EqualTo(Visibility.Visible), "보드로 돌아오면 다시 보인다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
