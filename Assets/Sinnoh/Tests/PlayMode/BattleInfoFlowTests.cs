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
    /// 결정 206 ②(2026-11-23 「원작 세로쌓기 카드를 좌 우로 놓자 … 엔트리를 볼 수 있는 버튼을 따로」): 배틀 정보 기둥 = 좌우 카드 둘(같은 폭 · 기둥 안) +
    /// 머리 줄 [내 엔트리 n] · [상대 엔트리 n](명단이 둘 이상인 쪽만) → 엔트리 판(카드 + 기술 띠 · 꼬리표 「배틀 중」 · 「대기」 · 「기절」 · 단추 다시 누르기 · Esc = 닫기 — 결정 176) ·
    /// 카드 마우스 오버 = 글 판 · 상대가 쓰러지면 다음 포켓몬 카드로 바뀌고 갤럭시단 띠에 응징 피해 주사위가 선다.
    /// 판은 견본 여섯(<see cref="SoloTrail.PrepareCards"/>)으로 고향 마을 조무래기에게 [도전]. UiShots <c>battle-info-*</c> · <c>battle-entry*</c>가 같은 판을 찍는다.
    /// </summary>
    public sealed class BattleInfoFlowTests
    {
        const ulong Seed = 20261112, AiSeed = 7;

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
            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
            }
            rt = null;
        }

        static bool MyTurn(BattleScreen b) => b.Open && b.Moves.Count > 0 && !b.AskingToken && !b.AskingOrder && !b.AskingFate && !b.AskingAsk && !b.AskingPick && b.Bench.Count == 0;

        // 두 쪽 카드가 그 쪽 칸 · 기둥 안이고 폭이 같다
        static void Cards(UiStage stage, string at)
        {
            var root = stage.Root;
            var info = root.Q("battle-info").worldBound;
            float width = -1f;
            foreach (string side in new[] { "battle-me", "battle-foe" })
            {
                var card = root.Q(side);
                Assert.That(card, Is.Not.Null, at + " " + side + " 카드");
                Assert.That(root.Q(side == "battle-me" ? "battle-info-me" : "battle-info-foe").Contains(card), Is.True, at + " " + side + "는 그 쪽 기둥 칸에");
                var box = card.worldBound;
                var holder = root.Q(side + "-cards").worldBound;
                Assert.That(box.xMin >= holder.xMin - 0.5f && box.xMax <= holder.xMax + 0.5f && box.yMax <= holder.yMax + 0.5f, Is.True, at + " " + side + " 카드가 칸 안(" + box + " · " + holder + ")");
                Assert.That(box.xMax <= info.xMax && box.yMax <= info.yMax, Is.True, at + " " + side + " 기둥 안");
                Assert.That(root.Q(side + "-art").resolvedStyle.backgroundImage.texture, Is.Not.Null, at + " " + side + " 카드 앞면 그림");
                if (width < 0f) width = box.width;
                else Assert.That(box.width, Is.EqualTo(width).Within(1f), at + " 두 쪽 카드 폭이 같다");
            }
            Assert.That(root.Q("battle-me-hp"), Is.Not.Null, at + " ♥ 상자 칸(튜토리얼 앵커)");
            Assert.That(root.Q("battle-me-fatigue"), Is.Not.Null, at + " 전투 피로(카드 밖 덧칩 줄)");
        }

        [UnityTest]
        public IEnumerator M10_결정206_배틀기둥은좌우카드이고엔트리판은단추와Esc로열고닫히며상대가바뀌면카드도바뀐다()
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
            var flow = boot.Flow;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            var board = Object.FindAnyObjectByType<BoardBoot>();
            if (board == null || board.Pieces.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저");

            var ctx = flow.Ctx;
            int[] team = null;
            flow.StartSeats(new SoloSettings(), 1, Seed, AiSeed, state => team = SoloTrail.PrepareCards(ctx, state));
            for (int i = 0; i < 4; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            for (int i = 0; i < 300 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty || !flow.InputOpen); i++)
            {
                flow.Skip(100);
                if (i % 10 == 0) yield return null;
            }
            Assert.That(flow.Hud.Press(BoardHud.Challenge), Is.True, "[도전] — 고향 마을 조무래기");
            var stage = flow.Stage;
            var root = stage.Root;
            BattleScreen screen = null;
            int firstFoe = -1;
            bool sawEntries = false, sawNext = false;
            for (int guard = 0; guard < 8000 && !sawNext; guard++)
            {
                screen = stage.Current as BattleScreen;
                if (screen != null && screen.ShowsResult) break;
                if (screen != null && MyTurn(screen))
                {
                    yield return null;
                    yield return null;
                    int foe = screen.ShownMon(1 - flow.Battle.MySide).Id;
                    Cards(stage, sawEntries ? "다음 상대" : "첫 차례");
                    if (!sawEntries)
                    {
                        firstFoe = foe;
                        // 머리 줄 단추: [내 엔트리 5](찌르호크는 기절이어도 엔트리) · [상대 엔트리 2](조무래기 둘)
                        var mine = root.Q<Button>(BattleScreen.EntryMineButton);
                        var theirs = root.Q<Button>(BattleScreen.EntryFoeButton);
                        Assert.That(mine, Is.Not.Null, "[내 엔트리]");
                        Assert.That(theirs, Is.Not.Null, "[상대 엔트리]");
                        Assert.That(mine.text, Is.EqualTo(stage.T("screen.battle.entry.mine").Replace("{n}", (team.Length - 1).ToString())));
                        Assert.That(theirs.text, Is.EqualTo(stage.T("screen.battle.entry.foe").Replace("{n}", BattleFlow.GruntLineup.ToString())));
                        // 상대 카드 = 갤럭시단 전술 띠(플랜 B + 은하의 응징)
                        Assert.That(root.Q("battle-foe").Q("card-strip-1"), Is.Not.Null, "갤럭시단 포켓몬 카드 밑 갤럭시단 띠");

                        // 내 엔트리: 다섯 장 · 꼬리표(배틀 중 하나 · 기절 하나) · 다시 누르기 = 닫기
                        Assert.That(screen.ToggleEntry(true), Is.True, "[내 엔트리] = 판 열기");
                        yield return null;
                        Assert.That(screen.EntryOpen && screen.EntryMine, Is.True);
                        Assert.That(screen.EntryShown, Is.EquivalentTo(new ArraySegment<int>(team, 0, team.Length - 1)), "내 엔트리 = 내 파티(견본 다섯)");
                        int active = 0, out_ = 0;
                        for (int n = 1; n <= team.Length - 1; n++)
                        {
                            string tag = screen.EntryTag(n);
                            Assert.That(tag, Is.Not.Null, "꼬리표 " + n);
                            if (tag == "screen.battle.entry.active") active++;
                            if (tag == "screen.info.fainted") out_++;
                        }
                        Assert.That((active, out_), Is.EqualTo((1, 1)), "「배틀 중」 하나 · 「기절」 하나(찌르호크)");
                        var panel = root.Q(BattleScreen.EntryPanel).worldBound;
                        Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(root.Q("battle-info").worldBound.xMax - 0.5f), "판은 기둥 오른쪽부터");
                        Assert.That(panel.xMax, Is.LessThanOrEqualTo(root.worldBound.xMax + 0.5f), "화면 안");
                        Assert.That(root.Q<Button>(BattleScreen.EntryMineButton).ClassListContains(BattleScreen.EntryOnClass), Is.True, "열린 쪽 단추에 켜짐 표시");
                        Assert.That(screen.ToggleEntry(true), Is.False, "다시 누르기 = 닫기(결정 176)");
                        Assert.That(screen.EntryOpen, Is.False);

                        // 상대 엔트리: 두 장 · Esc = 닫기(배틀 화면은 그대로)
                        Assert.That(screen.ToggleEntry(false), Is.True, "[상대 엔트리] = 판 열기");
                        yield return null;
                        Assert.That(screen.EntryShown.Count, Is.EqualTo(BattleFlow.GruntLineup));
                        Assert.That(flow.Escape(), Is.True, "Esc");
                        Assert.That(screen.EntryOpen, Is.False, "Esc = 엔트리 판 닫기");
                        Assert.That(stage.Current, Is.SameAs(screen), "배틀 화면은 그대로");

                        // 카드 마우스 오버 = 글 판(★ 고유 기술 · 지닌물건 둘 · 상태)
                        Assert.That(screen.HoverCard(true), Is.True);
                        yield return null;
                        var tip = screen.CardTipElement;
                        Assert.That(tip.Q("battle-card-tip-move-0"), Is.Not.Null, "★ 고유 기술 줄");
                        Assert.That(tip.Q("battle-card-tip-item-2"), Is.Not.Null, "지닌물건 둘");
                        Assert.That(tip.Q("battle-card-tip-status-1"), Is.Not.Null, "상태(독)");
                        Assert.That(tip.worldBound.Overlaps(root.Q("battle-dock").worldBound), Is.False, "글 판이 독을 덮지 않는다");
                        sawEntries = true;
                    }
                    else if (foe != firstFoe)
                    {
                        // 상대가 쓰러지고 다음 포켓몬이 나왔다: 카드 = 그 포켓몬 · 응징 2 = 칩 + 갤럭시단 띠의 피해 주사위
                        var view = flow.Client.View;
                        Assert.That(screen.HoverCard(false), Is.True);
                        Assert.That(screen.CardTipElement.Q<Label>("battle-card-tip-name").text.Replace("⁠", ""), Is.EqualTo(stage.T("pokemon." + ctx.Cards.Pokemon[view.Pokemon[foe].Def].Key)), "상대 카드 = 다음 포켓몬");
                        Assert.That(screen.RetributionShown, Is.EqualTo(2), "응징 2");
                        Assert.That(root.Q("battle-foe").Q("battle-foe-retribution"), Is.Not.Null, "응징 칸 = 카드 갤럭시단 띠의 상자(칩이 아니다 — 검토 지적 8)");
                        Assert.That(root.Q("battle-foe-extra").Q("battle-foe-retribution"), Is.Null, "덧칩 줄에 응징 칩이 겹쳐 서지 않는다");
                        Assert.That(root.Q("battle-foe").Q("card-retribution")?.userData, Is.EqualTo("2"), "갤럭시단 띠의 피해 주사위 = 2");
                        sawNext = true;
                    }
                }
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
                flow.Skip(250);
                if (guard % 8 == 0) yield return null;
            }
            Assert.That(sawEntries, Is.True, "내 차례에 기둥 · 엔트리를 봤다");
            Assert.That(sawNext, Is.True, "상대가 쓰러진 뒤 다음 포켓몬 카드를 봤다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
