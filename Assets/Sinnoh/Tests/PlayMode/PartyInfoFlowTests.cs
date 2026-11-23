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
    /// M9 플레이 시험 T30 · T23 · T27 · T17: 정비 판의 기술 카드가 많으면 풀이 휠로 굴러가 마지막 카드까지 보이고(고치기 전 = 아래가 잘림), 카드마다 앞면 포켓몬의 등급 배지 · 「○○ 카드의 뒷면」 마우스 오버,
    /// 우두머리 포켓몬은 지닌물건 칸에 우두머리 토큰(뗄 수 없음) + 카드에 「우두머리」 칩, 기술을 배우지 않은 전설은 「비스트 부스트」 칩, 가방의 유물은 마우스 오버에 효과 글.
    /// </summary>
    public sealed class PartyInfoFlowTests
    {
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
            if (rt != null) rt.Release();
            rt = null;
        }

        static int DefOf(Ctx ctx, string key)
        {
            for (int i = 0; i < ctx.Cards.Pokemon.Count; i++) if (ctx.Cards.Pokemon[i].Key == key) return i;
            Assert.Fail("포켓몬 없음: " + key);
            return -1;
        }

        [UnityTest]
        public IEnumerator M9_T30_정비판기술카드풀은굴러가고카드마다등급과뒷면_T23우두머리토큰_T27비스트부스트_T17유물효과()
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
            Assert.That(boot, Is.Not.Null);
            var flow = boot.Flow;
            var stage = boot.Root.Stage;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;

            var ctx = flow.Ctx;
            int alpha = -1, legend = -1, first = -1;
            flow.StartSeats(new SoloSettings(), 1, 20261112, 7, state =>
            {
                var st = state.Seats[0];
                st.OpenSlots = st.Party.Length;
                alpha = PokemonRules.Spawn(state, DefOf(ctx, "448:0"), 0, 0);
                state.Pokemon[alpha].Alpha = true;
                state.Pokemon[alpha].HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, state.Pokemon[alpha]);
                PokemonRules.AddToParty(st, alpha);
                legend = PokemonRules.Spawn(state, DefOf(ctx, "480:0"), 0, (int)DeckId.Legendary);
                state.Pokemon[legend].HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, state.Pokemon[legend]);   // 전설 배틀에서 잡은 것처럼(BattleFlow)
                PokemonRules.AddToParty(st, legend);
                foreach (var deck in new[] { DeckId.Strong, DeckId.Moderate, DeckId.Weak })
                    for (int i = 0, n = 0; i < ctx.Cards.Pokemon.Count && n < 5; i++)
                    {
                        var def = ctx.Cards.Pokemon[i];
                        if (def.Signature < 0 || st.StampMoves.Contains(def.Signature) || !state.Decks[(int)deck].Contains(i)) continue;
                        if (first < 0) first = i;
                        st.StampCards.Add((int)deck);
                        st.StampCards.Add(i);
                        st.StampMoves.Add(def.Signature);
                        n++;
                    }
                for (int i = 0; i < ctx.Items.Items.Count; i++) if (ctx.Items.Items[i].Key == "item.magma_stone") st.Bag.Add(i);
            });
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            for (int i = 0; i < 300 && !flow.CanOpenParty; i++)
            {
                flow.Skip(100);
                yield return null;
            }
            Assert.That(flow.OpenParty(), Is.True);
            for (int i = 0; i < 3; i++) yield return null;
            var party = (PartyScreen)stage.Current;
            var root = stage.Root;
            Assert.That(party.Pool.Count, Is.EqualTo(15));

            // T30 ②: 풀이 판 아래로 넘치면 휠로 굴러간다 — 끝까지 굴리면 마지막 카드가 칸 안에 든다
            var scroll = root.Q<ScrollView>("board-party-pool-scroll");
            Assert.That(scroll, Is.Not.Null, "기술 카드 풀이 굴러가는 칸 안에 있다(고치기 전: 아래가 잘림)");
            var last = root.Q("board-party-pool-" + party.Pool.Count);
            Assert.That(last.worldBound.yMax, Is.GreaterThan(scroll.contentViewport.worldBound.yMax), "재현 조건: 카드가 칸보다 많다");
            scroll.scrollOffset = new Vector2(0, scroll.contentContainer.layout.height);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(last.worldBound.yMax, Is.LessThanOrEqualTo(scroll.contentViewport.worldBound.yMax + 0.5f), "끝까지 굴리면 마지막 카드가 보인다");

            // T30 ①: 카드마다 앞면 포켓몬 카드의 등급 배지 + 마우스 오버
            var back = root.Q<Label>("board-party-pool-1-back");
            Assert.That(back, Is.Not.Null, "첫 기술 카드의 등급 배지");
            Assert.That(back.text, Is.EqualTo(stage.T("screen.encounter.grade.strong")), "강함 덱에서 뽑은 카드");
            stage.Tips.Show(back);
            Assert.That(stage.Tips.Element.Q<Label>(className: "kit-title").text, Is.EqualTo(stage.T("screen.party.pool.back").Replace("{mon}", stage.T("pokemon." + ctx.Cards.Pokemon[first].Key))));
            stage.Tips.Hide();

            // T23: 우두머리 = 지닌물건 칸에 토큰(뗄 수 없음) + 카드의 「우두머리」 칩 · T27: 기술을 배우지 않은 전설 = 「비스트 부스트」 칩
            int ai = PokemonRules.PartyIndexOf(flow.Client.View.Seats[0], alpha), li = PokemonRules.PartyIndexOf(flow.Client.View.Seats[0], legend);
            // 결정 206 ③: 정비 칸 = 원작 카드 — 우두머리 토큰은 카드의 지닌물건 동그라미(첫 토큰), 비스트 부스트는 카드 밑 전설 전술 판의 상자
            var slot = root.Q("board-party-item-" + ai);
            Assert.That(slot.ClassListContains("sn-party-alpha"), Is.True, "우두머리 토큰 칸");
            Assert.That(slot.userData, Is.EqualTo(stage.T("item.alpha_pokemon")));
            Assert.That(root.Q("board-party-mon-" + ai).Q("card-item-1"), Is.Not.Null, "카드의 우두머리 토큰 그림");
            Assert.That(party.PressItemSlot(ai, 0), Is.False, "뗄 수 없다");
            var beast = root.Q("board-party-slot-" + li + "-2");
            Assert.That(beast, Is.Not.Null, "전설의 비스트 부스트 상자");
            Assert.That(beast.ClassListContains(MonParts.LegendSlotClass) && (string)beast.userData == stage.T("screen.passive.beast_boost"), Is.True, "칸 2 = 비스트 부스트");
            Assert.That(root.Q("board-party-mon-0").Q(className: MonParts.LegendSlotClass), Is.Null, "스타팅 포켓몬에는 없다");
            Assert.That(stage.TOrNull("item.alpha_pokemon.text"), Is.Not.Null);

            // T17: 가방의 유물 · 지닌물건 효과 글
            var relic = root.Q("board-party-relic-1");
            Assert.That(relic, Is.Not.Null, "유물 칩");
            stage.Tips.Show(relic);
            string tipText = string.Concat(stage.Tips.Element.Query<Label>().ToList().ConvertAll(l => l.text)).Replace("⁠", "").Replace(" ", "").Replace("\n", "");
            Assert.That(tipText, Does.Contain(stage.T("item.magma_stone.text").Replace(" ", "")), "유물 마우스 오버 = 효과 글");
            stage.Tips.Hide();
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
