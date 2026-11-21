using System;
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
    /// 포켓몬 도감(S42 — 사용자 결정 105 ③, 04 §3-14): 타이틀과 게임 중에 열고 닫는다 · 덱마다 카드 수가 데이터와 같다 · 전국도감 번호 순 목록 굴리기 · 덱·타입·이름 거르기 · 상세 쪽 · 다른 폼 ·
    /// 숨은 정보(지금 판의 덱 상태)를 읽지 않는다 — 판의 덱이 줄어든 뒤에도 도감은 타이틀에서 본 것과 같다.
    /// 화면 요소·화면 함수로만 누른다. Gen 겉모양이 있어야 돈다.
    /// </summary>
    public sealed class DexFlowTests
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

        int TitleRow(string id)
        {
            var title = (TitleScreen)stage.Current;
            for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == id) return i;
            return -1;
        }

        // 도감이 보이는 것 전부를 글로: 전체 · 덱마다 「키×장수」 줄
        static string Dump(Ctx ctx, DexScreen dex)
        {
            var sb = new System.Text.StringBuilder();
            var decks = new List<DexDeck?> { null };
            foreach (var deck in DexBook.Tabs) decks.Add(deck);
            foreach (var deck in decks)
            {
                dex.PickDeck(deck);
                sb.Append(DexBook.Id(deck)).Append(':');
                foreach (var e in dex.Shown) sb.Append(ctx.Cards.Pokemon[e.Def].Key).Append('x').Append(e.Copies).Append(' ');
                sb.Append('\n');
            }
            return sb.ToString();
        }

        [UnityTest]
        public IEnumerator M7_도감을타이틀에서열어덱마다카드수가데이터와같고번호순목록거르기상세쪽이된다()
        {
            yield return Boot();
            var ctx = flow.Ctx;
            Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
            int row = TitleRow(TitleScreen.Dex);
            Assert.That(row, Is.GreaterThanOrEqualTo(0), "타이틀에 「포켓몬 도감」 줄");
            Assert.That(((TitleScreen)stage.Current).Activate(row), Is.True);
            yield return null;
            var dex = stage.Current as DexScreen;
            Assert.That(dex, Is.Not.Null, "도감이 열렸다");
            Assert.That(dex.InGame, Is.False);
            Assert.That(stage.Root.Q<Button>("dex-close").text, Is.EqualTo(stage.T("screen.common.back")));

            // 처음 = 전체: 포켓몬 덱에 든 카드가 한 줄씩(같은 카드는 한 번), 장수 = 모든 덱의 합, 전국도감 번호 순
            Assert.That(dex.Deck, Is.Null);
            Assert.That(stage.Root.Q("dex-deck-all").ClassListContains("sn-opt-picked"), Is.True);
            var keys = new HashSet<int>();
            int tableCards = 0;
            foreach (var d in ctx.Decks.Decks)
                if (d.Kind == DeckKind.Pokemon)
                {
                    foreach (var e in d.Cards) keys.Add(e.Card);
                    tableCards += d.Count;
                }
            Assert.That(dex.Shown.Count, Is.EqualTo(keys.Count), "전체 = 포켓몬 덱의 카드 종류 수");
            Assert.That(DexBook.Count(dex.Shown), Is.EqualTo(tableCards), "전체 장수 = 포켓몬 덱 아홉의 장수 합");
            for (int i = 1; i < dex.Shown.Count; i++)
                Assert.That(ctx.Cards.Pokemon[dex.Shown[i].Def].Dex, Is.GreaterThanOrEqualTo(ctx.Cards.Pokemon[dex.Shown[i - 1].Def].Dex), "전국도감 번호 순");

            // 덱마다: 종 수 · 장수 = 덱 표(decks.json)
            foreach (var deck in DexBook.Tabs)
            {
                dex.PickDeck(deck);
                yield return null;
                int want = 0, wantCards = 0;
                foreach (string key in DexBook.DeckKeys(deck))
                {
                    var def = ctx.Decks.Decks[ctx.Decks.DeckIds.IndexOf(key)];
                    want += def.Cards.Count;
                    wantCards += def.Count;
                }
                Assert.That(dex.Shown.Count, Is.EqualTo(want), deck + " 종 수");
                Assert.That(DexBook.Count(dex.Shown), Is.EqualTo(wantCards), deck + " 장수");
                Assert.That(stage.Root.Q<Label>("dex-count").text, Is.EqualTo(stage.T("screen.dex.count").Replace("{kinds}", want.ToString()).Replace("{cards}", wantCards.ToString())));
                Assert.That(stage.Root.Q("dex-deck-" + DexBook.Id(deck)).ClassListContains("sn-opt-picked"), Is.True);
            }

            // 목록: 약함 덱 — 판 아홉 줄, 첫 카드가 골라져 왼쪽 그림 칸에
            dex.PickDeck(DexDeck.Weak);
            yield return null;
            var first = ctx.Cards.Pokemon[dex.Shown[0].Def];
            Assert.That(dex.Picked, Is.EqualTo(0));
            Assert.That(dex.Detail, Is.False);
            Assert.That(stage.Root.Q<Label>("dex-pic-name").text, Is.EqualTo(stage.T("pokemon." + first.Key)));
            Assert.That(stage.Root.Q<Label>("dex-pic-no").text, Is.EqualTo(DexScreen.NoMark + first.Dex.ToString("000")));
            Assert.That(stage.Root.Q("dex-plate-" + (DexScreen.Rows - 1)), Is.Not.Null, "판 아홉 줄");
            Assert.That(stage.Root.Q("dex-plate-" + DexScreen.Rows), Is.Null);
            Assert.That(stage.Root.Q("dex-plate-0").ClassListContains("sn-dex-plate-picked"), Is.True);
            // 굴리기: 한 칸 · 한 화면 · 끝
            Assert.That(dex.Roll(1), Is.True);
            Assert.That(dex.Picked, Is.EqualTo(1));
            Assert.That(dex.Top, Is.EqualTo(0));
            Assert.That(dex.Roll(DexScreen.Rows), Is.True);
            yield return null;
            Assert.That(dex.Picked, Is.EqualTo(1 + DexScreen.Rows));
            Assert.That(dex.Top, Is.EqualTo(dex.Picked - DexScreen.Rows / 2), "고른 판이 가운데 줄");
            Assert.That(stage.Root.Q("dex-plate-" + DexScreen.Rows / 2).ClassListContains("sn-dex-plate-picked"), Is.True);
            Assert.That(stage.Root.Q<Label>("dex-pic-name").text, Is.EqualTo(stage.T("pokemon." + ctx.Cards.Pokemon[dex.Shown[dex.Picked].Def].Key)));
            Assert.That(dex.Pick(dex.Shown.Count - 1), Is.True);
            Assert.That(dex.Top, Is.EqualTo(dex.Shown.Count - DexScreen.Rows));
            Assert.That(dex.Roll(1), Is.False, "끝에서 더 굴러가지 않는다");
            Assert.That(dex.Pick(0) && !dex.Roll(-1), Is.True);

            // 상세 쪽: 포획률 3 · 든 덱 · 고유 기술 → Esc는 목록으로
            Assert.That(dex.OpenDetail(), Is.True);
            yield return null;
            Assert.That(stage.Root.Q("dex-list"), Is.Null);
            Assert.That(stage.Root.Q("dex-detail"), Is.Not.Null);
            Assert.That(DexBook.CatchRate(ctx, dex.Shown[0].Def), Is.EqualTo(3), "약함 = 포획률 3");
            Assert.That(stage.Root.Q("dex-detail-rate").Q<Label>(className: "kit-badge").text, Is.EqualTo(stage.T("screen.dex.rate.value").Replace("{n}", "3")));
            Assert.That(stage.Root.Q("dex-detail-move"), Is.Not.Null, "고유 기술");
            var inDecks = DexBook.Decks(ctx, dex.Shown[0].Def);
            Assert.That(inDecks.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(inDecks[0].Key, Is.EqualTo(DexDeck.Weak));
            Assert.That(stage.Root.Q("dex-detail-decks").Query<Label>(className: "kit-badge").ToList().Count, Is.EqualTo(inDecks.Count), "든 덱마다 배지");
            Assert.That(stage.Root.Q<Button>("dex-open").text, Is.EqualTo(stage.T("screen.dex.list")));
            Assert.That(flow.Escape(), Is.True);
            yield return null;
            Assert.That(stage.Current, Is.SameAs(dex), "상세 쪽의 Esc는 목록으로");
            Assert.That(dex.Detail, Is.False);
            Assert.That(stage.Root.Q("dex-list"), Is.Not.Null);
            Assert.That(stage.Root.Q<Button>("dex-open").text, Is.EqualTo(stage.T("screen.dex.detail")));

            // 등급별 포획률(결정 105 ①의 수) · 잡을 수 없는 카드
            dex.PickDeck(DexDeck.Moderate);
            Assert.That(DexBook.CatchRate(ctx, dex.Shown[0].Def), Is.EqualTo(4));
            dex.PickDeck(DexDeck.Strong);
            Assert.That(DexBook.CatchRate(ctx, dex.Shown[0].Def), Is.EqualTo(5));
            dex.PickDeck(DexDeck.Galactic);
            dex.OpenDetail();
            yield return null;
            Assert.That(DexBook.CatchRate(ctx, dex.Shown[0].Def), Is.EqualTo(0));
            Assert.That(stage.Root.Q("dex-detail-rate").Q<Label>(className: "kit-badge").text, Is.EqualTo(stage.T("screen.dex.rate.none")));

            // 다른 폼: 전설 덱의 면이 여럿인 카드
            dex.PickDeck(DexDeck.Legendary);
            int withFaces = -1;
            for (int i = 0; i < dex.Shown.Count && withFaces < 0; i++) if (DexBook.Faces(ctx, dex.Shown[i].Def).Count > 1) withFaces = i;
            Assert.That(withFaces, Is.GreaterThanOrEqualTo(0));
            Assert.That(DexBook.CatchRate(ctx, dex.Shown[withFaces].Def), Is.EqualTo(6));
            dex.Pick(withFaces);
            dex.OpenDetail();
            var faces = DexBook.Faces(ctx, dex.Shown[withFaces].Def);
            Assert.That(dex.Face(faces[1]), Is.True);
            yield return null;
            Assert.That(dex.Viewed, Is.EqualTo(faces[1]));
            Assert.That(dex.Shown[dex.Picked].Def, Is.EqualTo(faces[0]), "목록의 고른 카드는 그대로");
            Assert.That(stage.Root.Q("dex-face-" + ctx.Cards.Pokemon[faces[1]].State).ClassListContains("sn-opt-picked"), Is.True);
            Assert.That(stage.Root.Q<Label>("dex-pic-name").text, Is.EqualTo(stage.T("pokemon." + ctx.Cards.Pokemon[faces[1]].Key)), "그림 칸도 그 폼");
            Assert.That(dex.Back(), Is.True);
            Assert.That(dex.Viewed, Is.EqualTo(faces[0]), "목록으로 돌아오면 고른 카드의 면");

            // 거르기: 타입 · 이름
            dex.PickDeck(DexDeck.Strong);
            int all = dex.Shown.Count;
            dex.PickType(PokemonType.Fire);
            yield return null;
            Assert.That(dex.Shown.Count, Is.GreaterThan(0).And.LessThan(all));
            foreach (var e in dex.Shown) Assert.That(ctx.Cards.Pokemon[e.Def].Types, Does.Contain(PokemonType.Fire));
            Assert.That(stage.Root.Q("dex-type-fire").ClassListContains("sn-dex-type-picked"), Is.True);
            Assert.That(stage.Root.Q<Label>("dex-count").text, Does.EndWith(stage.T("screen.dex.found").Replace("{n}", dex.Shown.Count.ToString())));
            dex.PickType(PokemonType.Fire);   // 다시 누르면 전체
            Assert.That(dex.Type, Is.Null);
            Assert.That(dex.Shown.Count, Is.EqualTo(all));
            string name = stage.T("pokemon." + ctx.Cards.Pokemon[dex.Shown[3].Def].Key);
            stage.Root.Q<TextField>("dex-name").value = name;
            yield return null;
            Assert.That(dex.Search, Is.EqualTo(name), "이름 칸이 찾기를 건다");
            Assert.That(dex.Shown.Count, Is.GreaterThanOrEqualTo(1).And.LessThan(all));
            foreach (var e in dex.Shown) Assert.That(stage.T("pokemon." + ctx.Cards.Pokemon[e.Def].Key), Does.Contain(name));
            dex.Find("zzz");
            yield return null;
            Assert.That(dex.Shown.Count, Is.EqualTo(0));
            Assert.That(dex.Picked, Is.EqualTo(-1));
            Assert.That(stage.Root.Q("dex-empty"), Is.Not.Null);
            Assert.That(dex.OpenDetail(), Is.False, "고른 카드가 없으면 상세 쪽이 없다");
            dex.Find("");

            // Esc → 타이틀의 그 줄
            Assert.That(flow.Escape(), Is.True);
            yield return null;
            Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
            Assert.That(((TitleScreen)stage.Current).Picked, Is.EqualTo(TitleRow(TitleScreen.Dex)));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M7_도감을게임중에열고닫으며판의덱상태를읽지않는다()
        {
            yield return Boot();
            var ctx = flow.Ctx;
            // 타이틀에서 본 도감(판이 없다)
            flow.OpenTitleDex();
            yield return null;
            string atTitle = Dump(ctx, (DexScreen)stage.Current);
            flow.CloseDex();
            yield return null;
            Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());

            Assert.That(((TitleScreen)stage.Current).Activate(TitleRow(TitleScreen.Solo)), Is.True);
            yield return null;
            FlowPilot.Start(flow, SoloTrail.Defeat);
            yield return null;
            Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");
            // 연출 중(막 카드 — 입력이 닫혀 있다)에도 열린다
            Assert.That(flow.InputOpen, Is.False);
            Assert.That(flow.OpenDex(), Is.True, "연출 중에도 도감은 열린다");
            Assert.That(stage.Current, Is.InstanceOf<DexScreen>());
            flow.CloseDex();
            for (int i = 0; i < 200 && !flow.InputOpen; i++)
            {
                flow.Skip(100);
                yield return null;
            }
            Assert.That(flow.InputOpen, Is.True);
            var button = stage.Root.Q<Button>("board-hud-dex");
            Assert.That(button, Is.Not.Null, "위 띠의 [도감]");
            Assert.That(button.text, Is.EqualTo(stage.T("screen.dex.open")));

            // 판의 덱을 줄인다(약함 덱을 한 장만 남긴다) — 도감은 그대로다
            flow.Rebase(st =>
            {
                var weak = st.Decks[(int)DeckId.Weak];
                if (weak.Count > 1) weak.RemoveRange(1, weak.Count - 1);
            });
            for (int i = 0; i < 200 && !flow.InputOpen; i++)
            {
                flow.Skip(100);
                yield return null;
            }
            Assert.That(flow.Session.HostCopy().Decks[(int)DeckId.Weak].Count, Is.LessThanOrEqualTo(1), "판의 약함 덱은 줄었다");
            int commands = flow.Session.CommandCount;
            Assert.That(flow.OpenDex(), Is.True);
            yield return null;
            var dex = (DexScreen)stage.Current;
            Assert.That(dex.InGame, Is.True);
            Assert.That(stage.Root.Q<Button>("dex-close").text, Is.EqualTo(stage.T("screen.dex.close")));
            Assert.That(flow.InputOpen, Is.False, "창이 떠 있는 동안 보드 입력은 꺼진다");
            Assert.That(Dump(ctx, dex), Is.EqualTo(atTitle), "게임 중의 도감 = 타이틀의 도감(덱 구성만 — 남은 카드 · 순서와 무관)");
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "보기만 한다 — 답을 내지 않는다");
            Assert.That(flow.OpenDex(), Is.True, "이미 떠 있으면 그대로(결정 125 — 두 번 열지 않는다)");
            Assert.That(stage.Current, Is.SameAs(dex));
            Assert.That(stage.Info, Is.Null);

            // Esc로 닫고 보드로
            Assert.That(flow.Escape(), Is.True);
            yield return null;
            Assert.That(stage.Current, Is.Null);
            Assert.That(flow.InputOpen, Is.True);
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
