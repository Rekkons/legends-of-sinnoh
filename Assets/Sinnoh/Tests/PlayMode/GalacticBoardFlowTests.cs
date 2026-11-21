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
    /// 결정 201 ②(2026-11-22): 보드에서 갤럭시단 상대가 있는 칸에 마우스를 올리면 공개 정보 카드에 그 상대가 쓸 수 있는 포켓몬 명단이 뜬다 —
    /// 조무래기 토큰 칸 = 조무래기 덱 전체(무작위 2마리) · 2막 호수 = 그 호수 간부의 5장(가진 조각 수 + 2마리) · 3막 신오신전 = 태홍의 8장(6마리).
    /// 줄마다 마우스를 올리면 그 포켓몬의 카드(<see cref="MonTip"/>)가 뜨고, 카드 위의 마우스는 카드를 그 칸 그대로 붙든다(보드는 밑의 칸 그대로).
    /// 명단 수 · 이름은 덱 데이터(카드 키의 주인 꼬리)와 견준다. UiShots <c>galactic-hover-*</c>가 같은 판을 그림으로 찍는다.
    /// </summary>
    public sealed class GalacticBoardFlowTests
    {
        const ulong Seed = 20261117, AiSeed = 7;
        const int W = 1920, H = 1080;

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
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
        }

        // 혼자 판(신오의 심장) — 셋업 뒤 trail로 손본 판(null = 첫 턴 그대로), 대기열이 비고 입력이 열릴 때까지
        IEnumerator Open(SoloTrail trail)
        {
            var ctx = flow.Ctx;
            flow.StartSeats(new SoloSettings(), 1, Seed, AiSeed, trail != null ? st => trail.After(ctx, st) : (System.Action<GameState>)null);
            for (int i = 0; i < 4; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            for (int i = 0; i < 300 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty || !flow.InputOpen); i++)
            {
                flow.Skip(100);
                if (i % 10 == 0) yield return null;
            }
            yield return null;
            flow.Layout(W, H);
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

        string T(string key) => boot.Root.Stage.T(key);

        static string Wrapped(string text) => Rekkon.UiKit.KoText.JoinWords(text);

        // 덱 데이터에서 그 주인(카드 키 꼬리 「@mars」 — null = 전부)의 카드
        List<int> DeckCards(DeckId deck, string owner)
        {
            var list = new List<int>();
            foreach (var e in flow.Ctx.Deck(deck).Cards)
                if (owner == null || flow.Ctx.Cards.Pokemon[e.Card].Key.EndsWith("@" + owner)) list.Add(e.Card);
            return list;
        }

        VisualElement Card => flow.Hud.Info.Element;

        // 칸에 올려 명단을 띄우고: 칸 · 줄 수 · 줄마다 이름 · 「이 중 n마리」를 단정한다
        IEnumerator ExpectRoster(int cell, List<int> cards, string pickKey, int count, string head)
        {
            var hud = flow.Hud;
            hud.Hover(-1);
            hud.Hover(cell);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(hud.Info.Subject, Is.EqualTo("cell-" + cell), cell + ": 공개 정보 카드");
            Assert.That(hud.Info.HasRoster, Is.True, cell + ": 명단이 실렸다");
            Assert.That(Card.Q("info-card-galactic"), Is.Not.Null);
            int rows = 0;
            while (Card.Q("info-card-galactic-mon-" + rows) != null) rows++;
            Assert.That(rows, Is.EqualTo(cards.Count), cell + ": 명단 줄 수 = 덱 데이터 수");
            for (int i = 0; i < cards.Count; i++)
            {
                string name = T("pokemon." + flow.Ctx.Cards.Pokemon[cards[i]].Key);
                bool named = false;
                Card.Q("info-card-galactic-mon-" + i).Query<Label>().ForEach(l => named |= l.text == name);
                Assert.That(named, Is.True, cell + ": " + i + "번 줄 = " + name);
            }
            Assert.That(Card.Q<Label>("info-card-galactic-pick").text, Is.EqualTo(Wrapped(T(pickKey).Replace("{n}", count.ToString()))), cell + ": 낼 수");
            var headLabel = Card.Q<Label>("info-card-galactic-head");
            if (head == null) Assert.That(headLabel, Is.Null, "조무래기는 토큰 칸이 이름을 적는다");
            else
            {
                Assert.That(headLabel.text, Is.EqualTo(head), cell + ": 상대 이름");
                Assert.That(hud.CellInfoText, Does.Contain(head), cell + ": 칸 정보 줄에도 상대 이름");
            }
        }

        // 줄에 마우스가 들어오면 그 포켓몬 카드 · 나가면 걷힌다
        void ExpectTip(int index, int def)
        {
            var row = Card.Q("info-card-galactic-mon-" + index);
            Assert.That(row.pickingMode, Is.EqualTo(PickingMode.Position), "명단 줄은 마우스를 받는다");
            Send(PointerEnterEvent.GetPooled(), row);
            Assert.That(flow.Hud.Info.MonShown, Is.EqualTo(def), index + "번 줄의 마우스 오버 카드 = 그 포켓몬");
            Assert.That(flow.Hud.Info.MonTipElement.Q<Label>("mon-tip-name").text, Is.EqualTo(Wrapped(T("pokemon." + flow.Ctx.Cards.Pokemon[def].Key))));
            Send(PointerLeaveEvent.GetPooled(), row);
            Assert.That(flow.Hud.Info.MonShown, Is.EqualTo(-1), "줄에서 나가면 걷힌다");
        }

        static void Send(EventBase e, VisualElement target)
        {
            using (e)
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        [UnityTest]
        public IEnumerator M10_결정201_조무래기칸에마우스를올리면조무래기덱명단이뜨고줄마다포켓몬카드다()
        {
            yield return Boot();
            yield return Open(null);
            int home = flow.Client.View.Seats[0].Cell;
            var grunts = DeckCards(DeckId.GalacticGrunt, null);
            Assert.That(grunts.Count, Is.EqualTo(8), "조무래기 덱 8장");
            yield return ExpectRoster(home, grunts, "screen.info.galactic.pick.random", BattleFlow.GruntLineup, null);
            Assert.That(Card.Q("info-card-token-grunt"), Is.Not.Null, "조무래기 토큰 칸은 그대로");
            Assert.That(Card.pickingMode, Is.EqualTo(PickingMode.Position), "명단 카드는 마우스를 받는다");
            ExpectTip(3, grunts[3]);
            ExpectTip(7, grunts[7]);

            // 카드 위의 실제 포인터 길: 카드를 그 칸 그대로 붙들고, 보드 쪽으로는 PC 층이 아니다(칸 고르기 · 휠은 밑의 칸)
            var row = Card.Q("info-card-galactic-mon-5");
            int scale = boot.Root.Stage.Grade.PanelScale;
            var c = row.worldBound.center;
            float x = c.x * scale, y = H - c.y * scale;
            flow.Pointer(x, y, false);
            Assert.That(flow.Hud.InfoHeld, Is.True, "명단 카드 위 = 붙듦");
            Assert.That(flow.Hud.Info.Subject, Is.EqualTo("cell-" + home), "밑의 칸으로 바뀌지 않는다");
            Assert.That(flow.OverUi(x, y), Is.False, "보드 쪽으로는 카드가 없는 것처럼");
            flow.Pointer(x, y, false);
            Assert.That(flow.Hud.Info.Subject, Is.EqualTo("cell-" + home), "다음 프레임도 그대로");
            flow.Pointer(4f, H * 0.5f, false);
            Assert.That(flow.Hud.InfoHeld, Is.False, "카드에서 벗어나면 놓는다");

            // 좌석 칩의 카드는 그대로 마우스를 받지 않는다
            flow.Hud.ShowSeatInfo(0);
            Assert.That(flow.Hud.Info.HasRoster, Is.False);
            foreach (var e in Card.Query().ToList()) Assert.That(e.pickingMode, Is.EqualTo(PickingMode.Ignore), "좌석 카드: " + e.name);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_결정201_2막호수에마우스를올리면그호수간부의명단이뜨고낼수는조각수더하기2다()
        {
            yield return Boot();
            yield return Open(SoloTrail.Lake);
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            Assert.That(view.Hos.Act, Is.EqualTo(2), "막 길: 2막");
            int shards = HosFlow.ShardCount(view.Seats[0]);
            string[] owners = { "mars", "jupiter", "saturn" };   // p.21: 감정 = 마스 · 지식 = 주피터 · 의지 = 새턴
            for (int s = 0; s < 3; s++)
            {
                var cards = DeckCards(DeckId.GalacticCommander, owners[s]);
                Assert.That(cards.Count, Is.EqualTo(5), owners[s] + " 5장");
                string head = T("screen.info.galactic.commander").Replace("{name}", T("galactic." + owners[s]));
                yield return ExpectRoster(HosFlow.LakeCell(ctx, (Shard)s), cards, "screen.info.galactic.pick", shards + 2, head);
                ExpectTip(s, cards[s]);
            }
            // 2막에는 신오신전에 태홍이 없다
            int temple = HosFlow.TempleCell(ctx);
            flow.Hud.Hover(temple);
            Assert.That(flow.Hud.Info.Subject == null || Card.Q("info-card-galactic") == null, Is.True, "2막 신오신전: 명단 없음");
            Assert.That(flow.Hud.CellInfoText, Does.Not.Contain(T("galactic.cyrus")));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_결정201_3막신오신전에마우스를올리면태홍의명단8장중6마리다()
        {
            yield return Boot();
            yield return Open(SoloTrail.Heart);
            var ctx = flow.Ctx;
            Assert.That(flow.Client.View.Hos.Act, Is.EqualTo(3), "막 길: 3막");
            var cards = DeckCards(DeckId.GalacticBoss, "cyrus");
            Assert.That(cards.Count, Is.EqualTo(8), "태홍 8장");
            string head = T("screen.info.galactic.boss").Replace("{name}", T("galactic.cyrus"));
            yield return ExpectRoster(HosFlow.TempleCell(ctx), cards, "screen.info.galactic.pick", HosFlow.BossLineup, head);
            ExpectTip(0, cards[0]);
            ExpectTip(7, cards[7]);
            // 3막에도 호수의 간부는 그대로(조각 셋 — 5마리)
            yield return ExpectRoster(HosFlow.LakeCell(ctx, Shard.Emotion), DeckCards(DeckId.GalacticCommander, "mars"), "screen.info.galactic.pick", 5,
                T("screen.info.galactic.commander").Replace("{name}", T("galactic.mars")));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
