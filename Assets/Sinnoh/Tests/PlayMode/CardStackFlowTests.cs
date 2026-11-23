using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rekkon.UiKit;
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
    /// 결정 206 1단계 「카드 쌓기 부품」(<see cref="CardStack"/>): 견본 판 <see cref="SoloTrail.PrepareCards"/>(목업 r10-stats의 다섯 + 갤럭시단 조무래기)의 카드를 실제 화면 뿌리에 쌓아
    /// 띠 수(빈 칸 없음) · 토큰 · ♥ · ≫ 상자 값과 꼬리표 · 기절 · 폼 앞면 · 영어 카드 · 도트 카드 대신 · 툴팁 자리를 본다. UiShots <c>card-sheet</c>가 같은 판을 찍는다.
    /// </summary>
    public sealed class CardStackFlowTests
    {
        const ulong Seed = 20261123, AiSeed = 7;

        GameFlow flow;
        UiStage stage;
        Ctx ctx;
        int[] ids;
        RenderTexture rt;
        readonly List<string> errors = new List<string>();

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
        }

        IEnumerator Open()
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
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            flow = boot.Flow;
            stage = boot.Root.Stage;
            ctx = flow.Ctx;
            flow.StartSeats(new SoloSettings(), 1, Seed, AiSeed, state => ids = SoloTrail.PrepareCards(ctx, state));
            yield return null;
            flow.Skip(flow.OpeningMs);
            yield return null;
        }

        void Close()
        {
            Application.logMessageReceived -= Grab;
            GenAssets.Withheld = null;
            var shut = Object.FindAnyObjectByType<ScreenBoot>();
            if (shut != null && shut.Root != null) shut.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
                rt = null;
            }
        }

        CardStack Put(int n, float width = 216f, int? retribution = null)
        {
            var stack = CardStack.Build(stage, ctx, CardMon.Of(ctx, flow.Client.View, ids[n], retribution: retribution), width);
            stage.Root.Add(stack.Root);
            return stack;
        }

        static string Shown(CardStack s, string name) => s.Root.Q(name)?.userData as string;

        static int Count(CardStack s, string prefix)
        {
            int n = 0;
            while (s.Root.Q(prefix + (n + 1)) != null) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator M10_결정206_카드쌓기는띠토큰상자를실제값으로그린다()
        {
            try
            {
                yield return Open();
                var stacks = Enumerable.Range(0, ids.Length).Select(n => Put(n, 216f, n == ids.Length - 1 ? 2 : (int?)null)).ToList();
                yield return null;
                yield return null;
                foreach (var s in stacks) Assert.That(s.Art, Is.True, s.FaceKey + " — 카드 그림(node Tools/assets/build.mjs --only cardfaces)");
                // 띠: 빈 칸은 그리지 않는다 · 전설 전술 판은 한 장 · 갤럭시단 포켓몬은 갤럭시단 전술 판
                Assert.That(stacks.Select(s => s.StripCount), Is.EqualTo(new[] { 2, 2, 1, 2, 0, 1 }), "띠 수 — 토대부기 기술 둘 · 초염몽 기술 + 균열 · 디아루가 전술 판 · 렌트라 기술 + 비스트 부스트 · 찌르호크 없음 · 동미러 갤럭시단 판");
                for (int i = 0; i < stacks.Count; i++) Assert.That(Count(stacks[i], "card-strip-"), Is.EqualTo(stacks[i].StripCount), "card-strip-n 요소 = 띠 수");
                // 쌓기 높이: 카드 28 cm + 띠마다(띠 높이 − 숨는 몫) — 위 블럭 아래 끝 = 띠의 18.75 cm 자리
                var tor = stacks[0];
                float u = 216f / CardStack.WidthCm;
                var strip1 = tor.Root.Q("card-strip-1");
                Assert.That(strip1.resolvedStyle.top, Is.EqualTo(Mathf.Round(Mathf.Round(28f * u) - CardStack.HideCm * u)).Within(0.6f), "첫 띠는 카드 아래 끝에서 숨는 몫만큼 위");
                Assert.That(tor.Root.resolvedStyle.height, Is.EqualTo(tor.Height).Within(0.6f));
                // 토큰: 우두머리 토큰 먼저 · 지닌물건 둘째 칸
                Assert.That(stacks.Select(s => Count(s, "card-item-")), Is.EqualTo(new[] { 2, 1, 1, 1, 0, 0 }), "지닌물건 · 우두머리 토큰 수(렌트라 = 우두머리 토큰)");
                Assert.That(stacks.Select(s => Count(s, "card-status-")), Is.EqualTo(new[] { 1, 1, 0, 0, 0, 0 }), "상태 주사위(독 · 마비)");
                // ♥ · ≫ 상자(결정 206 Q3 · Q11): 지금/최대 + 증감 — 카드 값과 같으면 덮지 않는다
                Assert.That(Shown(tor, "card-hp"), Is.EqualTo("4/10"), "토대부기 피해 6");
                Assert.That(Shown(tor, "card-hp-tag"), Is.Null);
                Assert.That(Shown(stacks[1], "card-speed"), Is.EqualTo("8"), "초염몽 마비 11 → 8");
                Assert.That(Shown(stacks[1], "card-speed-tag"), Is.EqualTo("−3"));
                Assert.That(Shown(stacks[1], "card-hp"), Is.Null, "체력 가득 · 최대 그대로면 덮지 않는다");
                Assert.That(Shown(stacks[3], "card-hp"), Is.EqualTo("10/12"), "렌트라 우두머리 최대 +4 · 피해 2");
                Assert.That(Shown(stacks[3], "card-hp-tag"), Is.EqualTo("+4"));
                Assert.That(Shown(stacks[2], "card-hp"), Is.Null, "디아루가 덮지 않음");
                Assert.That(stacks[2].Root.Q("card-alpha-type-1"), Is.Not.Null, "알파 스트라이크 배울 수 있는 타입 그림");
                Assert.That(stacks[3].Root.Q("card-emblem"), Is.Not.Null, "비스트 부스트만 남은 판의 표지");
                Assert.That(Shown(stacks[5], "card-retribution"), Is.EqualTo("2"), "은하의 응징 주사위");
                // 기절: 회색 + 배지 + 0/최대
                Assert.That(stacks[4].Root.ClassListContains("sn-card-out"), Is.True, "찌르호크 기절 회색");
                Assert.That(stacks[4].Root.Q("card-fainted"), Is.Not.Null);
                StringAssert.StartsWith("0/", Shown(stacks[4], "card-hp"));
                Assert.That(stacks.Take(4).All(s => !s.Root.ClassListContains("sn-card-out")), Is.True);
                // 폼: 그 상태의 앞면 · 갤럭시단 주인 카드
                StringAssert.EndsWith("/ko/483_2.png", stacks[2].FaceKey, "디아루가 오리진폼 앞면");
                StringAssert.EndsWith("/ko/436_0@male_grunt.png", stacks[5].FaceKey, "조무래기 동미러 = 갤럭시단 카드");
                // 툴팁 자리: ★ 고유 기술 · 띠 · 전술 상자 · 토큰 · 상태 — 거는 것은 쓰는 화면
                var kinds = stacks.SelectMany(s => s.Parts).Select(p => p.Kind).ToList();
                foreach (var k in new[] { CardPartKind.Signature, CardPartKind.Move, CardPartKind.Breach, CardPartKind.AlphaStrike, CardPartKind.BeastBoost, CardPartKind.PlanB, CardPartKind.Retribution, CardPartKind.Item, CardPartKind.Status })
                    Assert.That(kinds, Does.Contain(k), "자리 " + k);
                var crunch = tor.Parts.First(p => p.Kind == CardPartKind.Move);
                Assert.That(crunch.Title, Is.EqualTo(stage.T("move.crunch")));
                tor.HookTips();
                Assert.That(crunch.Element.pickingMode, Is.EqualTo(PickingMode.Position), "건 자리만 마우스를 받는다");
                Assert.That(tor.Root.pickingMode, Is.EqualTo(PickingMode.Ignore));
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally { Close(); }
        }

        static int Notes(VisualElement col)
        {
            int n = 0;
            while (col.Q("card-note-" + (n + 1)) != null) n++;
            return n;
        }

        // 기록 기둥 자리를 흉내 낸 판(화면 오른쪽 · 위 띠 아래 · 독 위) — 마우스 오버 카드가 그 아래 끝에 바닥을 맞춘다
        VisualElement Pillar()
        {
            var on = new VisualElement { name = "test-pillar", pickingMode = PickingMode.Ignore };
            on.style.position = Position.Absolute;
            on.style.right = 12;
            on.style.top = 90;
            on.style.width = 528;
            on.style.height = 740;
            stage.Root.Add(on);
            return on;
        }

        [UnityTest]
        public IEnumerator M10_결정206_마우스오버카드와카드확대는원작카드로그린다()
        {
            try
            {
                yield return Open();
                var view = flow.Client.View;
                var on = Pillar();
                var target = new VisualElement { name = "test-target" };
                target.style.position = Position.Absolute;
                target.style.left = 40;
                target.style.top = 400;
                target.style.width = target.style.height = 40;
                stage.Root.Add(target);
                yield return null;
                var tip = new MonTip(stage, ctx);
                var screenBox = stage.Root.worldBound;

                // ① 내 포켓몬(결정 169 자리): 카드 + 오른쪽 기둥 — 토대부기 = 띠 둘(깨물어부수기 · 지진) · 글 상자 둘(구애머리띠 · 화산의 돌)
                var tor = view.Pokemon[ids[0]];
                tip.ShowMon(target, tor, PokemonRules.Hp(ctx, tor), 8, on);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(tip.Shown, Is.EqualTo(tor.Def));
                Assert.That(tip.Stack != null && tip.Stack.Art, Is.True, "원작 카드");
                Assert.That(tip.Element.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible));
                var col = tip.Stack.Column;
                Assert.That(col, Is.Not.Null, "옆으로 펼친 짜임(card-col)");
                Assert.That(tip.Stack.StripCount, Is.EqualTo(2), "띠 둘");
                Assert.That(col.Q("card-strip-2"), Is.Not.Null, "띠는 오른쪽 기둥에");
                Assert.That(tip.Stack.Root.Q("card-face").Q("card-strip-1"), Is.Null, "카드 밑이 아니다");
                Assert.That(Notes(tip.Stack.Root), Is.EqualTo(2), "지닌물건 글 상자 둘");
                Assert.That(tip.Stack.Root.Q("card-note-1").Query<Label>().ToList().Exists(l => l.text.Replace("⁠", "") == stage.T("item.choice_band")), Is.True, "글 상자 = 지닌물건 이름");
                Assert.That(col.worldBound.xMin, Is.GreaterThanOrEqualTo(tip.Stack.Root.Q("card-face").worldBound.xMax), "기둥은 카드 오른쪽");
                var card = tip.Element.worldBound;
                Assert.That(card.yMax, Is.EqualTo(on.worldBound.yMax).Within(1f), "판 아래 끝에 바닥");
                Assert.That(card.xMax, Is.LessThanOrEqualTo(on.worldBound.xMax + 0.5f), "판 오른쪽 끝 안");
                Assert.That(card.yMin, Is.GreaterThanOrEqualTo(screenBox.yMin - 0.5f), "화면 위 끝 안");
                Assert.That(tip.Element.pickingMode == PickingMode.Ignore && tip.Stack.Parts.All(p => p.Element.pickingMode == PickingMode.Ignore), Is.True, "마우스를 받지 않는다");
                // 기절: 회색
                var star = view.Pokemon[ids[4]];
                tip.ShowMon(target, star, 0, ctx.Cards.Pokemon[star.Def].Initiative, on);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(tip.Stack.Root.ClassListContains("sn-card-out"), Is.True, "기절 회색");
                tip.Hide();
                Assert.That(tip.Shown, Is.EqualTo(-1));

                // ② 야생 카드: 앞면만 + 포획률 칩(덮지 않을 판 옆)
                int wild = ctx.Cards.PokemonIds.IndexOf("405:1");
                tip.Show(target, wild, TipSide.Left, on);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(tip.Stack != null && tip.Stack.Art && tip.Stack.StripCount == 0 && tip.Stack.Column == null, Is.True, "야생 카드 = 앞면만(쌓기)");
                var rate = tip.Element.Q<Label>("mon-tip-rate") ?? tip.Element.Q("mon-tip-rate").Q<Label>();
                StringAssert.StartsWith(stage.T("screen.dex.rate"), rate.text.Replace("⁠", ""), "포획률 칩");
                Assert.That(tip.Element.Q("mon-tip-rate").worldBound.yMin, Is.GreaterThanOrEqualTo(tip.Stack.Root.worldBound.yMax - 0.5f), "칩은 카드 아래");
                Assert.That(tip.Element.Q("mon-tip-rate").worldBound.yMax, Is.LessThan(stage.Root.worldBound.yMax - 0.5f), "칩이 화면 아래 끝에 걸리지 않는다(검토 지적 10)");
                Assert.That(tip.Element.worldBound.Overlaps(on.worldBound), Is.False, "덮지 않을 판을 덮지 않는다");

                // ③ 영어 화면 = en 카드
                Assert.That(stage.UseLang("en"), Is.True);
                tip.ShowMon(target, tor, PokemonRules.Hp(ctx, tor), 8, on);
                for (int i = 0; i < 3; i++) yield return null;
                StringAssert.Contains("/en/", tip.Stack.FaceKey, "영어 = en 카드");
                Assert.That(col = tip.Stack.Column, Is.Not.Null);
                Assert.That(tip.Stack.Root.Q("card-note-1").Query<Label>().ToList().Exists(l => l.text.Replace("⁠", "") == stage.T("item.choice_band")), Is.True, "글 상자도 영어");
                tip.Detach();
                Assert.That(stage.UseLang("ko"), Is.True);
                on.RemoveFromHierarchy();
                target.RemoveFromHierarchy();

                // ④ 정비 카드 확대(S38 — 목업 B-party): 카드 + 오른쪽 기둥 + 진화 줄 · [닫기], 화면 안
                for (int i = 0; i < 300 && !flow.CanOpenParty; i++)
                {
                    flow.Skip(100);
                    yield return null;
                }
                Assert.That(flow.OpenParty(), Is.True, "정비 판");
                yield return null;
                Assert.That(flow.OpenMon(ids[3], 0), Is.True, "카드 확대");
                for (int i = 0; i < 4; i++) yield return null;
                var zoom = (MonScreen)stage.Current;
                Assert.That(zoom.Stack != null && zoom.Stack.Art, Is.True, "원작 카드");
                Assert.That(zoom.Stack.StripCount, Is.EqualTo(2), "렌트라 = 깜짝베기 + 비스트 부스트");
                Assert.That(zoom.Stack.Root.Q("card-emblem"), Is.Not.Null, "전설 표지");
                Assert.That(Notes(zoom.Stack.Root), Is.EqualTo(1), "우두머리 토큰 글 상자");
                Assert.That((string)zoom.Stack.Root.Q("card-hp").userData, Is.EqualTo("10/12"), "지금/최대");
                Assert.That(stage.Root.Q("mon-detail-move-0"), Is.Not.Null, "★ 고유 기술 자리");
                Assert.That(zoom.Stack.Root.Q("mon-detail-evolve"), Is.Not.Null);
                var block = stage.Root.Q("mon-detail-block");
                var close = block.Q("mon-detail-close");
                Assert.That(close, Is.Not.Null, "[닫기]는 카드 묶음 오른쪽 위(검토 지적 1)");
                var zbox = block.worldBound;
                Assert.That(zbox.yMin >= screenBox.yMin - 0.5f && zbox.yMax <= screenBox.yMax + 0.5f && zbox.xMax <= screenBox.xMax + 0.5f, Is.True, "화면 안(" + zbox + ")");
                Assert.That(zbox.yMax, Is.LessThanOrEqualTo(stage.Root.Q("board-hud-dock").worldBound.yMin + 0.5f), "아래 판 윗변 안(아래 판 단추를 덮지 않는다)");
                Assert.That(zbox.Contains(close.worldBound.center), Is.True, "[닫기]는 묶음 안");
                Assert.That(block.pickingMode, Is.EqualTo(PickingMode.Position), "카드를 눌러도 창 밖 누르기가 아니다");
                // 영어로 바꾸면 열린 창을 다시 만든다 — en 카드
                Assert.That(stage.UseLang("en"), Is.True);
                for (int i = 0; i < 3; i++) yield return null;
                StringAssert.Contains("/en/", ((MonScreen)stage.Current).Stack.FaceKey, "카드 확대도 en 카드");
                Assert.That(stage.UseLang("ko"), Is.True);
                yield return null;
                flow.CloseMon();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<PartyScreen>(), "[닫기] = 정비 판");
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                if (stage != null) stage.UseLang("ko");
                Close();
            }
        }

        [UnityTest]
        public IEnumerator M10_결정206_영어화면은en카드_그림이없으면도트카드()
        {
            try
            {
                yield return Open();
                Assert.That(stage.UseLang("en"), Is.True, "영어 표");
                var en = Put(0);
                Assert.That(en.Art, Is.True);
                StringAssert.EndsWith("/en/389_1.png", en.FaceKey, "영어 화면 = en 카드");
                // 웹 손님판 · 짝 없는 카드: 카드 그림이 없으면 지금 도트 카드(MonTip 짜임)로 대신
                GenAssets.Withheld = key => key.StartsWith(GenAssets.CardFaces) ? false : (bool?)null;
                var dot = Put(4);
                yield return null;
                Assert.That(dot.Art, Is.False, "카드 그림 없음");
                Assert.That(dot.Root.Q("mon-tip-name"), Is.Not.Null, "도트 카드(이름 줄)");
                Assert.That(dot.Root.Q("card-face"), Is.Null);
                Assert.That(dot.Root.ClassListContains("sn-card-out"), Is.True, "도트 카드도 기절 회색");
                Assert.That(dot.Parts, Is.Empty, "도트 카드는 글을 다 적는다(툴팁 자리 없음)");
                GenAssets.Withheld = null;
                // 야생 카드: 앞면만(띠 · 토큰 · 상자 없음)
                int wild = ctx.Cards.PokemonIds.IndexOf("405:1");
                var card = CardStack.Build(stage, ctx, CardMon.Wild(ctx, wild), 216f);
                stage.Root.Add(card.Root);
                Assert.That(card.Art && card.StripCount == 0 && card.Root.Q("card-item-1") == null && card.Root.Q("card-hp") == null, Is.True, "야생 카드 = 앞면만");
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                if (stage != null) stage.UseLang("ko");
                Close();
            }
        }
    }
}
