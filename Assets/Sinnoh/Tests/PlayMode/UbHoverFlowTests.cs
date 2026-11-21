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
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 결정 179(2026-11-21): 울트라 버스트 판에서 UB · 네크로즈마가 선 칸(또는 그 피규어)에 마우스를 올리면 칸 정보 줄 · 공개 정보 카드에 그 이름(네크로즈마는 폼 이름까지)이,
    /// 시련이 남은 호수 수호신이 선 칸 · 피규어에 올리면 그 시련(조건 · 면역 · 보상 — 넘긴 뒤에는 「시련 통과」)이 뜬다. 보이는 칸은 피규어와 같은 셈(<see cref="UbBoard.Of"/>)이라
    /// UB가 옮겨 가는 연출 동안에도 피규어가 보이는 칸을 따른다. UiShots <c>ub-hover-*</c>(UiShots.UbHover.cs)가 같은 판을 그림으로 찍는다. Gen 겉모양 · 피규어가 있어야 돈다.
    /// </summary>
    public sealed class UbHoverFlowTests
    {
        const ulong Seed = 20261117, AiSeed = 7;
        const int W = 1920, H = 1080;
        const string LakeValor = "legendary_site.lake_valor_cavern";

        ScreenBoot boot;
        GameFlow flow;
        BoardBoot board;
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
            board = Object.FindAnyObjectByType<BoardBoot>();
            if (board == null || board.Pieces.GenMissing || board.Figures == null || board.Figures.GenMissing) Assert.Ignore("Gen 없음: rebuild-all 먼저");
        }

        // 울트라 버스트 혼자 판 — 셋업 뒤 대기열이 비고 입력이 열릴 때까지
        IEnumerator Open(System.Action<GameState> prepare)
        {
            flow.StartSeats(new SoloSettings { Campaign = Campaign.UltraBurst }, 1, Seed, AiSeed, prepare);
            for (int i = 0; i < 4; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            yield return Settle();
            flow.Layout(W, H);
        }

        IEnumerator Settle()
        {
            for (int i = 0; i < 300 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty || !flow.InputOpen); i++)
            {
                flow.Skip(100);
                if (i % 10 == 0) yield return null;
            }
            yield return null;
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

        // 공개 정보 카드에서 그 이름의 블록에 그 글이 있나
        bool CardHas(string block, string text)
        {
            bool found = false;
            flow.Hud.Info.Element.Query<VisualElement>(block).ForEach(b => b.Query<Label>().ForEach(l => found |= l.text == text));
            return found;
        }

        BoardFigures.Figure Figure(string key)
        {
            foreach (var f in board.Figures.Figures)
                if (f.Key == key) return f;
            Assert.Fail("피규어 없음: " + key);
            return null;
        }

        // 피규어의 머리 쪽(구운 경계 높이 90%)을 가리킨다 — 방향 여섯 가운데 땅으로는 다른 칸인 방향이 있어야 피규어 고르기를 본 것이다
        IEnumerator PointFigure(BoardFigures.Figure fig, int cell, string name)
        {
            var cam = Camera.main;
            var rig = cam.GetComponent<BoardCameraRig>();
            bool behind = false;
            for (int yaw = 0; yaw < 6; yaw++)
            {
                rig.Set(BoardCameraRig.Zoom.Close, yaw, board.CellTop(cell));
                flow.Layout(W, H);
                yield return null;
                var bb = BoardFigures.BakedBounds(fig.Root);
                var top = new Vector3(bb.center.x, bb.min.y + bb.size.y * 0.9f, bb.center.z);
                Assert.That(BoardPick.ScreenAt(cam, top, W, H, out var px), Is.True, yaw + ": 피규어가 카메라 앞");
                int ground = BoardPick.Cell(board, cam, px.x, px.y, W, H);
                flow.Pointer(px.x, px.y, false);
                Assert.That(flow.Hud.HoverAny, Is.EqualTo(cell), yaw + ": 피규어 위 = 그 피규어의 칸(땅의 칸 " + ground + ")");
                Assert.That(flow.Hud.CellInfoText, Does.Contain(name), yaw + ": 칸 정보 줄");
                Assert.That(flow.Hud.Info.Subject, Is.EqualTo("cell-" + cell), yaw + ": 공개 정보 카드");
                behind |= ground != cell;
            }
            Assert.That(behind, Is.True, "피규어 머리 쪽이 땅으로는 늘 같은 칸이라 피규어 고르기를 보지 못했다");
            flow.Pointer(4f, H * 0.5f, false);
        }

        [UnityTest]
        public IEnumerator M10_결정179_UB칸에마우스를올리면그UB이름이칸정보줄과공개정보카드에뜬다()
        {
            yield return Boot();
            yield return Open(null);
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            var hud = flow.Hud;
            int n = 0;
            TokenState first = null;
            foreach (var t in view.Tokens)
            {
                if ((t.Kind != TokenKind.UltraBeast && t.Kind != TokenKind.Necrozma) || t.Cell < 0) continue;
                string key = t.Kind == TokenKind.Necrozma ? UbBoard.NecrozmaForm(view.Ub, null) : ctx.Cards.Pokemon[t.Def].Key;
                string name = T("pokemon." + key);
                hud.Hover(t.Cell);
                Assert.That(hud.CellInfoText, Does.Contain(name), key + ": 칸 정보 줄");
                Assert.That(hud.Info.Subject, Is.EqualTo("cell-" + t.Cell), key + ": 공개 정보 카드");
                Assert.That(CardHas("info-card-beast", name), Is.True, key + ": 카드의 이름");
                // 가리킬 UB = 가장 키가 큰 피규어(머리 쪽이 땅으로는 뒤 칸이 되게)
                if (t.Kind == TokenKind.UltraBeast && (first == null || Figure(key).FittedSize.y > Figure(ctx.Cards.Pokemon[first.Def].Key).FittedSize.y)) first = t;
                n++;
            }
            Assert.That(n, Is.EqualTo(UbState.Beasts + 1), "UB 9 · 네크로즈마");
            Assert.That(T("pokemon." + UbBoard.NecrozmaForm(view.Ub, null)), Is.EqualTo(T("pokemon." + UbNecrozma.Phase1)), "1페이즈 네크로즈마");

            // UB가 없는 칸: 이름이 없다
            int empty = flow.Client.View.Seats[0].Cell;
            hud.Hover(empty);
            Assert.That(hud.CellInfoText, Does.Not.Contain(T("pokemon." + ctx.Cards.Pokemon[first.Def].Key)));

            // 피규어 위(땅으로는 뒤 칸이어도 그 UB의 칸)
            string firstName = T("pokemon." + ctx.Cards.Pokemon[first.Def].Key);
            yield return PointFigure(Figure(ctx.Cards.Pokemon[first.Def].Key), first.Cell, firstName);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        static FxStep MoveStep(GameFlow flow)
        {
            if (flow.Fx == null) return null;
            foreach (var e in flow.Fx.Sequencer.Queued)
                foreach (var s in e.Steps)
                    if (s.Row == UbRows.Move && UbRows.First<UbMoved>(s) != null) return s;
            return null;
        }

        void DeclineSkip()
        {
            foreach (var c in flow.Client.View.Pending.FindAll(x => !x.Answered && x.Kind == ChoiceKind.Respond && x.Sub == ChoiceSub.UbSkip && x.Seat == flow.Client.Seat))
            {
                var no = flow.Client.Legal(c).Find(a => a.Kind == AnswerKind.Decline);
                if (no != null) flow.Answer(c, no);
            }
        }

        [UnityTest]
        public IEnumerator M10_결정179_UB가옮겨가는연출동안칸정보줄은피규어가보이는칸을따른다()
        {
            yield return Boot();
            yield return Open(null);
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");
            FxStep step = null;
            for (int i = 0; i < 100 && (step = MoveStep(flow)) == null; i++)
            {
                DeclineSkip();
                flow.Skip(20);
                if (i % 10 == 0) yield return null;
            }
            Assert.That(step, Is.Not.Null, "라운드 끝 UB 턴의 이동 단계(820)");
            var m = UbRows.First<UbMoved>(step);
            string name = T("pokemon." + flow.Ctx.Cards.Pokemon[m.UbId].Key);
            var hud = flow.Hud;
            // 이동 단계 전: 보기는 이미 끝 값이지만 피규어는 출발 칸 — 칸 정보도 출발 칸
            hud.Hover(m.To);
            Assert.That(hud.CellInfoText, Does.Not.Contain(name), "이동 전 — 도착 칸에는 아직 없다");
            hud.Hover(m.From);
            Assert.That(hud.CellInfoText, Does.Contain(name), "이동 전 — 출발 칸");
            Assert.That(flow.BeastCell(m.UbId), Is.EqualTo(m.From));
            // 뛰기 시작(20%): 아직 출발 칸 쪽
            flow.Skip(flow.MsUntil(s => ReferenceEquals(s, step), 200));
            yield return null;
            hud = flow.Hud;
            hud.Hover(m.From);
            Assert.That(hud.CellInfoText, Does.Contain(name), "뛰기 시작 — 출발 칸 쪽");
            hud.Hover(m.To);
            Assert.That(hud.CellInfoText, Does.Not.Contain(name), "뛰기 시작 — 도착 칸에는 아직");
            // 반을 넘긴 뒤(80%): 마우스를 그대로 둬도 도착 칸에 이름이 선다
            flow.Skip(flow.MsUntil(s => ReferenceEquals(s, step), 800));
            yield return null;
            hud = flow.Hud;
            hud.Hover(m.To);
            Assert.That(hud.CellInfoText, Does.Contain(name), "반을 넘긴 뒤 — 같은 칸 그대로 다시 적는다");
            Assert.That(CardHas("info-card-beast", name), Is.True, "공개 정보 카드도 다시 그린다");
            hud.Hover(m.From);
            Assert.That(hud.CellInfoText, Does.Not.Contain(name), "반을 넘긴 뒤 — 출발 칸에는 없다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_결정179_호수수호신칸에마우스를올리면시련내용이뜨고넘긴뒤에는시련통과다()
        {
            yield return Boot();
            var ctx = flow.Ctx;
            int fixture = ctx.Fixtures.Ids.IndexOf(LakeValor), cell = ctx.Fixtures.Fixtures[fixture].Cell;
            int azelf = ctx.Fixtures.Fixtures[fixture].Legendary[0];
            string key = ctx.Cards.Pokemon[azelf].Key, name = T("pokemon." + key);
            Assert.That(UbTrial.LakeOf(ctx, azelf), Is.EqualTo((int)Shard.Willpower), "입지호수 동굴 = 아그놈");
            yield return Open(null);
            var hud = flow.Hud;
            hud.Hover(cell);
            Assert.That(hud.CellInfoText, Does.Contain(name), "칸 정보 줄의 수호신 이름");
            Assert.That(hud.Info.Subject, Is.EqualTo("cell-" + cell));
            Assert.That(CardHas("info-card-trial", name), Is.True, "카드 머리 = 수호신 이름");
            Assert.That(CardHas("info-card-trial-badge", T("screen.battle.trial.2")), Is.True, "의지의 시련 배지");
            Assert.That(CardHas("info-card-trial-goal", Wrapped(T("screen.battle.trial.tip.2"))), Is.True, "시련 조건");
            Assert.That(CardHas("info-card-trial-immune", Wrapped(T("screen.battle.immune.tip"))), Is.True, "면역");
            Assert.That(CardHas("info-card-trial-reward", Wrapped(T("screen.info.trial.reward"))), Is.True, "보상");
            yield return PointFigure(Figure(key), cell, name);
            // 다른 두 호수도 그 시련
            for (int lake = 0; lake < 2; lake++)
            {
                int f = -1, guardian = UbTrial.Guardian(ctx, lake);
                for (int i = 0; i < ctx.Fixtures.Fixtures.Count && f < 0; i++)
                    if (ctx.Fixtures.Fixtures[i].Kind == FixtureKind.LegendarySite)
                        foreach (int c in ctx.Fixtures.Fixtures[i].Legendary)
                            if (c == guardian) f = i;
                Assert.That(f, Is.GreaterThanOrEqualTo(0), "호수 " + lake);
                flow.Hud.Hover(ctx.Fixtures.Fixtures[f].Cell);
                Assert.That(CardHas("info-card-trial-badge", T("screen.battle.trial." + lake)), Is.True, "호수 " + lake + "의 시련 배지");
            }

            // 의지의 시련을 넘긴 판(도망쳐 덱으로 돌아온 아그놈): 「시련 통과」 · 면역 줄 없음
            flow.Rebase(st => st.Ub.TrialsOvercome[(int)Shard.Willpower] = true);
            yield return Settle();
            flow.Layout(W, H);
            hud = flow.Hud;
            hud.Hover(cell);
            Assert.That(CardHas("info-card-trial-badge", T("screen.battle.trial.passed")), Is.True, "시련 통과 배지");
            Assert.That(CardHas("info-card-trial-goal", Wrapped(T("screen.info.trial.passed"))), Is.True, "넘긴 뒤 한 줄");
            Assert.That(hud.Info.Element.Q("info-card-trial-immune"), Is.Null, "넘긴 뒤에는 면역이 없다");

            // 쓰러뜨리거나 잡은 뒤(전설 덱에 없다): 피규어가 흩어지고 나면 이름 · 시련이 없다
            flow.Rebase(st => st.Decks[(int)DeckId.Legendary].Remove(azelf));
            yield return Settle();
            flow.Skip(ScatterBeats.Ms + 100);
            yield return null;
            Assert.That(flow.FigureShown(key, out _), Is.False, "아그놈 피규어가 사라졌다");
            hud = flow.Hud;
            hud.Hover(-1);
            hud.Hover(cell);
            Assert.That(hud.CellInfoText, Does.Not.Contain(name), "피규어가 없으면 이름도 없다");
            Assert.That(hud.Info.Subject == null || hud.Info.Element.Q("info-card-trial") == null, Is.True, "시련 카드 없음");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
