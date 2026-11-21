using System;
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
    /// 결정 196(사용자 11-21 「버려진 카드들을 확인할 수 있나」): 위 띠 [버림 더미] → 덱마다 버려진 카드를 보는 창(<see cref="DiscardScreen"/>) —
    /// 덱 장수 = 보기의 버림 더미 수 · 포켓몬 카드 마우스 오버 = 포켓몬 카드 · 사당 이벤트 카드 마우스 오버 = 이름과 문구 · 다시 누르면 닫힘(결정 176) · Esc · 남의 턴에도 열린다.
    /// </summary>
    public sealed class DiscardFlowTests
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
            Assert.That(until(), Is.True, what);
        }

        static readonly System.Reflection.MethodInfo ClickInvoke = typeof(Clickable).GetMethod("Invoke", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, new[] { typeof(EventBase) }, null);

        // 키트 단추를 누른다(그 단추의 Clickable — 마우스를 뗄 때와 같은 동작, MouseInputTests와 같은 꼴)
        static void Click(VisualElement e)
        {
            var button = (Button)e;
            Assert.That(ClickInvoke, Is.Not.Null, "Clickable.Invoke(EventBase)를 찾지 못했다(Unity 판이 바뀌었다)");
            using (var click = ClickEvent.GetPooled())
            {
                click.target = button;
                ClickInvoke.Invoke(button.clickable, new object[] { click });
            }
        }

        static string Plain(string s) => s.Replace("⁠", "");

        [UnityTest]
        public IEnumerator M10_결정196_버림더미창은덱마다장수와카드를보이고_마우스오버카드_다시누르면닫힘_Esc_남의턴에도_에러로그0()
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

            flow.StartSeats(new SoloSettings(), 2, 20261121, 7);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                Assert.That(flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == AnswerKind.EndTurn), Is.True, "남 좌석의 턴 종료");
                yield return Until(() => flow.Fx.Sequencer.IsEmpty, "턴 넘김");
            }
            var ctx = flow.Ctx;
            int mon = ctx.Decks.Decks[(int)DeckId.Weak].Cards[0].Card, mon2 = ctx.Decks.Decks[(int)DeckId.Weak].Cards[1].Card;
            int shrine = ctx.Decks.CardIds.IndexOf("shrine_event.equivalent_exchange");
            Assert.That(shrine, Is.GreaterThanOrEqualTo(0), "동등한 교환 카드");
            flow.Rebase(st =>
            {
                st.Discards[(int)DeckId.Weak].AddRange(new[] { mon, mon2, mon });
                st.Discards[(int)DeckId.ShrineEvent].Add(shrine);
            });
            yield return Until(() => flow.Hud != null && flow.OnBoard && flow.BoardOpen, "보드");
            var view = flow.Client.View;

            // ── 위 띠 [버림 더미] → 창: 버린 카드가 있는 덱만 · 장수 = 보기의 버림 더미 수 ──
            var button = stage.Root.Q("board-hud-discards");
            Assert.That(button, Is.Not.Null, "위 띠 [버림 더미]");
            Click(button);
            yield return null;
            Assert.That(flow.Front, Is.InstanceOf<DiscardScreen>(), "창이 열린다");
            var w = (DiscardScreen)flow.Front;
            var decks = DiscardScreen.Decks(view);
            CollectionAssert.Contains(decks, (int)DeckId.Weak);
            CollectionAssert.Contains(decks, (int)DeckId.ShrineEvent);
            foreach (int d in decks)
            {
                string snake = DiscardScreen.Snake(ctx, d);
                var count = stage.Root.Q<Label>("board-discards-deck-" + snake + "-count");
                Assert.That(count, Is.Not.Null, snake + " 장수 칸");
                Assert.That(count.text, Is.EqualTo(view.Discards[d].Count.ToString()), snake + " 장수 = 보기의 버림 더미 수");
            }

            // ── 약함 덱: 같은 카드는 한 칸 + ×n(최근 것부터) · 마우스 오버 = 포켓몬 카드 ──
            Assert.That(w.PickDeck((int)DeckId.Weak), Is.True);
            yield return null;
            Assert.That(w.Cards.Count, Is.EqualTo(2), "같은 카드는 한 칸");
            Assert.That((w.Cards[0].Key, w.Cards[0].Value), Is.EqualTo((mon, 2)), "맨 위(최근에 버린 것)부터 · 장수");
            Assert.That(stage.Root.Q("board-discards-card-1"), Is.Not.Null);
            Assert.That(w.Hover(1), Is.True);
            Assert.That(w.MonShown, Is.EqualTo(mon), "마우스 오버 = 그 포켓몬 카드");

            // ── 기술 면(결정 196 덧): 칸 수 그대로 · 칸 = 그 카드의 고유 기술 줄(PokemonDef.Signature) · 마우스 오버 = 기술 이름 + 효과 ──
            int shown = w.Cards.Count;
            Assert.That(stage.Root.Q<Button>("board-discards-face").enabledSelf, Is.True, "포켓몬 덱이면 면 바꾸기가 켜져 있다");
            Assert.That(w.Flip(), Is.True, "[기술 면]");
            yield return null;
            Assert.That(w.MoveFace, Is.True);
            Assert.That(w.Cards.Count, Is.EqualTo(shown), "칸 수 그대로");
            int sig = ctx.Cards.Pokemon[mon].Signature;
            Assert.That(sig, Is.GreaterThanOrEqualTo(0), "고유 기술");
            var moveRow = stage.Root.Q("board-discards-card-1").Q(className: "sn-enc-move");
            Assert.That(moveRow, Is.Not.Null, "칸이 기술 줄");
            string moveKey = BattleFeed.MoveNameKey(ctx, sig);
            Assert.That(moveRow.Query<Label>().ToList().Exists(l => Plain(l.text).Contains(Plain(stage.T(moveKey)))), Is.True, "기술 줄 = 그 카드의 고유 기술");
            Assert.That(w.Hover(1), Is.True);
            var moveTip = stage.Tips.Element.Query<Label>().ToList().ConvertAll(l => Plain(l.text));
            Assert.That(moveTip, Does.Contain(Plain(stage.T(moveKey))), "툴팁 머리 = 기술 이름");
            Assert.That(moveTip.Exists(t => t.Contains(Plain(stage.T(moveKey + ".text")))), Is.True, "툴팁 본문 = 기술 효과: " + string.Join(" | ", moveTip));
            stage.Tips.Hide();
            // 창을 다시 열어도 기술 면 그대로 [가정]
            flow.CloseDiscards();
            yield return null;
            Assert.That(flow.ToggleDiscards(), Is.True);
            yield return null;
            w = (DiscardScreen)flow.Front;
            Assert.That(w.PickDeck((int)DeckId.Weak), Is.True);
            Assert.That(w.MoveFace && stage.Root.Q("board-discards-card-1").Q(className: "sn-enc-move") != null, Is.True, "다시 열어도 기술 면");
            Assert.That(w.Flip(), Is.True, "[포켓몬 면]");
            Assert.That(stage.Root.Q("board-discards-card-1").Q(className: "sn-enc-move"), Is.Null, "포켓몬 면");

            // ── 사당 이벤트 덱: 마우스 오버 = 카드 이름 + 문구 ──
            Assert.That(w.PickDeck((int)DeckId.ShrineEvent), Is.True);
            yield return null;
            Assert.That(stage.Root.Q<Button>("board-discards-face").enabledSelf, Is.False, "포켓몬 덱이 아니면 면 바꾸기가 꺼져 있다");
            Assert.That(w.Flip(), Is.False);
            Assert.That(w.Hover(1), Is.True);
            var tip = stage.Tips.Element.Query<Label>().ToList().ConvertAll(l => Plain(l.text));
            Assert.That(tip, Does.Contain(Plain(stage.T("shrine_event.equivalent_exchange"))), "툴팁 머리 = 카드 이름");
            string text = DiscardScreen.CardText(stage, "shrine_event.equivalent_exchange");
            Assert.That(text, Is.Not.Null.And.Not.Empty, "카드 문구");
            Assert.That(tip.Exists(t => t.Contains(Plain(text.Split('\n')[0]))), Is.True, "툴팁 본문 = 카드 문구: " + string.Join(" | ", tip));
            stage.Tips.Hide();

            // ── 다시 누르면 닫힌다(결정 176) · Esc도 닫는다 ──
            Click(button);
            yield return null;
            Assert.That(flow.Front is DiscardScreen, Is.False, "다시 누르면 닫힌다");
            Assert.That(flow.ToggleDiscards(), Is.True);
            yield return null;
            Assert.That(flow.Escape(), Is.True, "Esc");
            yield return null;
            Assert.That(flow.Front is DiscardScreen, Is.False, "Esc로 닫힌다");

            // ── 남의 턴에도 열린다 ──
            var map = flow.Client.Choice(ChoiceKind.MapAction);
            Assert.That(flow.Answer(map, flow.Client.Legal(map).Find(a => a.Kind == AnswerKind.EndTurn)).Accepted, Is.True, "턴 종료");
            yield return Until(() => flow.Client.View.ActiveSeat != flow.Client.Seat && flow.Fx.Sequencer.IsEmpty, "남의 턴");
            Assert.That(flow.ToggleDiscards(), Is.True, "남의 턴에 연다");
            yield return null;
            Assert.That(flow.Front, Is.InstanceOf<DiscardScreen>());
            flow.CloseDiscards();
            yield return null;
            Assert.That(flow.Front is DiscardScreen, Is.False);
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
