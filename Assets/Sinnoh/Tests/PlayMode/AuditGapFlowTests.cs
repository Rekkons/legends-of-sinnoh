using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
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
    /// M8 · M9 마감 감사 2차가 「장면이 없어 못 봤다 · 프로브 판이 지나지 않았다」고 남긴 것(<c>Logs/Review/m9/close-audit/README.md</c> A5 · A11)을 실행 중인 화면 흐름으로 본다:
    /// 결정 123 · T25 전설 장소 피규어(유물을 얻으면 대기열이 빈 뒤 0에서 커지며 선다 · 그 전설을 이기면 사라진다), 결정 128(수호신 · 실버디 피규어는 울트라 버스트 판에서만),
    /// 결정 121 · T13(울트라 버스트 방 로비의 조종 줄 잠금), 소리 단서(<c>cue-log.tsv</c>와 같은 <see cref="GameFlow.Sound"/> 줄) — 진화(39행) · 폼 바뀜(79행)의 울음 · 같은 칸 만남(13행)의 meet 징글.
    /// UiShots <c>a11-*</c> 장면(UiShots.AuditGaps.cs)이 같은 판을 그림으로 찍는다. Gen 겉모양 · 말 · 소리 표가 있어야 돈다.
    /// </summary>
    public sealed class AuditGapFlowTests
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

        IEnumerator Open(int seats, Action<GameState> prepare, Campaign campaign = Campaign.HeartOfSinnoh)
        {
            flow.StartSeats(new SoloSettings { Campaign = campaign }, seats, Seed, AiSeed, prepare);
            for (int i = 0; i < 4; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            yield return Settle();
        }

        // 대기열이 비고 입력이 열릴 때까지
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
            if (flow != null) flow.SoundOnSkip = false;
            var b = Object.FindAnyObjectByType<ScreenBoot>();
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
            }
            rt = null;
        }

        static int Cell(Ctx ctx, string fixture) => ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf(fixture)].Cell;

        static string SiteKey(Ctx ctx, string fixture) => ctx.Cards.Pokemon[ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf(fixture)].Legendary[0]].Key;

        static void GiveTeam(Ctx ctx, GameState state, params string[] keys)
        {
            var st = state.Seats[0];
            st.OpenSlots = st.Party.Length;
            int starter = st.Party[0];
            for (int i = 0; i < st.Party.Length; i++) st.Party[i] = -1;
            foreach (string key in keys.Length > 0 ? keys : SoloTrail.Team) PokemonRules.AddToParty(st, PokemonRules.Spawn(state, ctx.Cards.PokemonIds.IndexOf(key), 0, (int)DeckId.Legendary));
            if (starter >= 0 && PokemonRules.PartyCount(st) < st.OpenSlots) PokemonRules.AddToParty(st, starter);
        }

        // 결과 창이 설 때까지 첫 기술만 누르며 싸운다(UiShots Fight와 같은 답 — 첫 줄, 알파 스트라이크면 첫 타입). seen이 참이 되면 거기서 멈춘다
        IEnumerator Fight(Func<bool> seen = null)
        {
            for (int guard = 0; guard < 4000; guard++)
            {
                if (seen != null && seen()) yield break;
                var screen = flow.Stage.Current as BattleScreen;
                if (screen != null && screen.ShowsResult) yield break;
                if (screen != null && screen.Open)
                {
                    if (screen.AskingToken) screen.PressToken(true);
                    else if (screen.AskingOrder) screen.PressOrder(0);
                    else if (screen.AskingFate) screen.PressFatePass();
                    else if (screen.AskingAsk) screen.PressAsk(false);
                    else if (screen.AskingPick) { if (!screen.PressPick(-1)) screen.PressPick(0); }
                    else if (screen.Bench.Count > 0) { if (!screen.PressStay()) screen.PressBench(0); }
                    else if (screen.Moves.Count > 0)
                    {
                        if (screen.Moves[0].Target == BattleRules.AlphaStrike) { screen.OpenAlpha(); screen.PressAlpha(0); }
                        else screen.PressMove(0);
                    }
                }
                flow.Skip(500);
                if (guard % 4 == 0) yield return null;
            }
            Assert.Fail("배틀이 끝나지 않았다");
        }

        // 이긴 결과 창 → 배틀 뒤 물음은 마지막 답 → [확인] → 보드의 대기열이 비고 빛으로 흩어지는 연출이 끝날 때까지
        IEnumerator WinBack()
        {
            yield return Fight();
            var screen = (BattleScreen)flow.Stage.Current;
            for (int i = 0; i < 40 && (screen.AskingReward || !screen.Ready); i++)
            {
                if (screen.AskingReward) screen.PressReward(screen.Rewards.Count - 1);
                flow.Skip(500);
                yield return null;
            }
            Assert.That(flow.Battle != null && flow.Battle.Won, Is.True, "이긴 배틀");
            Assert.That(screen.Confirm(), Is.True, "결과 창 [확인]");
            for (int i = 0; i < 300 && (flow.Stage.Current != null || flow.Fx == null || !flow.Fx.Sequencer.IsEmpty); i++)
            {
                flow.Skip(100);
                if (i % 10 == 0) yield return null;
            }
            flow.Skip(ScatterBeats.Ms + 100);
            yield return null;
        }

        [UnityTest]
        public IEnumerator M10_A11_결정123_유물을얻으면전설장소피규어가대기열뒤에0에서커지며선다()
        {
            yield return Boot();
            var ctx = flow.Ctx;
            const string garden = "legendary_site.garden_of_gratitude";
            int site = Cell(ctx, garden), gracidea = ctx.Items.Ids.IndexOf("item.gracidea_flower");
            string key = SiteKey(ctx, garden);
            yield return Open(1, st => st.Seats[0].Cell = site);
            // 걸어서 닿는 옆 칸(토큰 · 고정물 없음)에 그라시데아꽃 숨겨진 장소를 둔다
            var near = MoveRules.Reachable(ctx.Board, site, 1, flow.Client.View.Options.ElevationMode);
            int spot = -1;
            foreach (int c in flow.Hud.Reach)
            {
                if (spot >= 0 || c == site || near[c] != 1 || flow.Client.View.Tokens.Exists(t => t.Cell == c && !t.Destroyed)) continue;
                bool fixedCell = false;
                foreach (var x in ctx.Fixtures.Fixtures) fixedCell |= x.Cell == c;
                if (!fixedCell) spot = c;
            }
            Assert.That(spot, Is.GreaterThanOrEqualTo(0), "감사의 꽃밭 옆 빈 칸");
            flow.Rebase(st =>
            {
                var token = st.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && t.Def == gracidea) ?? st.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0);
                token.Def = gracidea;
                token.Cell = spot;
                token.FaceUp = false;
                token.Destroyed = false;
                token.Loose = false;
            });
            yield return Settle();
            Assert.That(flow.FigureShown(key, out _), Is.False, "아무도 그라시데아꽃이 없으면 쉐이미 피규어는 서지 않는다");

            Assert.That(flow.Hud.Walk(spot), Is.True, "옆 칸으로 걷기");
            for (int i = 0; i < 80 && !(flow.InputOpen && flow.Client.Choice(ChoiceKind.HiddenSpot) != null && flow.Hud.Shows(BoardHud.Reveal)); i++)
            {
                flow.Skip(100);
                yield return null;
            }
            Assert.That(flow.Client.Choice(ChoiceKind.HiddenSpot), Is.Not.Null, "「공개하겠습니까?」");
            Assert.That(flow.Hud.Press(BoardHud.Reveal), Is.True, "[공개]");
            bool earlyShown = false;
            for (int i = 0; i < 400 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty); i++)
            {
                flow.Skip(50);
                if (!flow.Fx.Sequencer.IsEmpty && flow.FigureShown(key, out _)) earlyShown = true;
                if (i % 10 == 0) yield return null;
            }
            Assert.That(earlyShown, Is.False, "유물 연출(30행)이 도는 동안에는 서지 않는다(대기열이 빈 뒤)");
            Assert.That(flow.Client.View.Seats[0].Bag, Does.Contain(gracidea), "그라시데아꽃을 얻었다");
            for (int i = 0; i < 100 && !flow.FigureShown(key, out _); i++) flow.Skip(16);
            Assert.That(flow.FigureShown(key, out float first), Is.True, "유물을 얻으면 선다(결정 123)");
            Assert.That(first, Is.LessThan(0.2f), "0에서 커지기 시작한다");
            flow.Skip(GameFlow.SiteAppearMs / 2);
            yield return null;
            Assert.That(flow.FigureShown(key, out float half), Is.True);
            Assert.That(half, Is.GreaterThan(0.3f).And.LessThan(0.95f), "나타남 길이의 반 — 커지는 중");
            flow.Skip(GameFlow.SiteAppearMs);
            yield return null;
            Assert.That(flow.FigureShown(key, out float whole), Is.True);
            Assert.That(whole, Is.EqualTo(1f).Within(0.01f), "다 섰다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_A11_결정123_전설장소의전설을이기면피규어가사라진다()
        {
            yield return Boot();
            var ctx = flow.Ctx;
            const string magma = "legendary_site.magma_chamber";
            int cell = Cell(ctx, magma), fixture = ctx.Fixtures.Ids.IndexOf(magma);
            string key = SiteKey(ctx, magma);
            yield return Open(1, st =>
            {
                GiveTeam(ctx, st);
                st.Seats[0].Cell = cell;
                st.Seats[0].Bag.Add(ctx.Items.Ids.IndexOf("item.magma_stone"));
            });
            Assert.That(flow.FigureShown(key, out float stood), Is.True, "마그마스톤을 가진 좌석이 있으면 히드런이 서 있다");
            Assert.That(stood, Is.EqualTo(1f).Within(0.01f));
            Assert.That(flow.Hud.Press(BoardHud.Search), Is.True, "[찾아보기]");
            yield return WinBack();
            Assert.That(LegendRules.SiteCard(flow.Client.View, ctx, fixture), Is.EqualTo(-1), "이긴 전설은 전설 덱에 없다");
            Assert.That(flow.FigureShown(key, out _), Is.False, "이긴 뒤 보드에 히드런 피규어가 없다(결정 123 · T25)");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        static readonly string[] Guardians = { "legendary_site.lake_verity_cavern", "legendary_site.lake_acuity_cavern", "legendary_site.lake_valor_cavern", "legendary_site.temple_of_sinnoh" };

        [UnityTest]
        public IEnumerator M10_A11_결정128_수호신과실버디피규어는울트라버스트판에서만선다()
        {
            yield return Boot();
            var ctx = flow.Ctx;
            int cell = Cell(ctx, Guardians[2]);
            yield return Open(1, st => st.Seats[0].Cell = cell, Campaign.UltraBurst);
            foreach (string site in Guardians)
            {
                Assert.That(flow.FigureShown(SiteKey(ctx, site), out float size), Is.True, site + ": 울트라 버스트 판에서는 선다");
                Assert.That(size, Is.EqualTo(1f).Within(0.01f), site);
            }
            yield return Open(1, st => st.Seats[0].Cell = cell);
            foreach (string site in Guardians) Assert.That(flow.FigureShown(SiteKey(ctx, site), out _), Is.False, site + ": 신오의 심장 판에서는 서지 않는다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        sealed class FakeLobby : ILobbySource
        {
            public RoomState Room { get; set; }
            public int Seat { get; set; }
            public bool IsHost { get; set; } = true;
            public string JoinCode => "Q7K2MZ";
            public bool Listed => false;
            public string ListFail => null;
            public IReadOnlyList<KeyValuePair<string, string>> Addresses { get; } = new KeyValuePair<string, string>[0];
            public int Port => 0;
            public bool Tunneled => false;
            public IReadOnlyList<string> WebLinks { get; } = new string[0];
            public string WebNotice => null;
            public bool WebPending => false;
            public readonly List<RoomMsg> Sent = new List<RoomMsg>();
            public void Send(RoomMsg msg) => Sent.Add(msg);

            public static FakeLobby With(Campaign campaign) => new FakeLobby
            {
                Room = new RoomState
                {
                    Phase = RoomPhase.Lobby, RoomId = "room-test",
                    Settings = new RoomSettings { Seats = 3, Campaign = campaign, Galactic = Control.Human },
                    Seats = new List<RoomSeat> { new RoomSeat { Nickname = "방장", Host = true, Connected = true }, new RoomSeat { Nickname = "손님", Connected = true }, null },
                },
            };
        }

        [UnityTest]
        public IEnumerator M10_A11_결정121_울트라버스트방로비는조종줄이AI조종에잠긴다()
        {
            yield return Boot();
            var root = boot.Root.Stage.Root;
            foreach (var campaign in new[] { Campaign.UltraBurst, Campaign.HeartOfSinnoh })
            {
                bool ub = campaign == Campaign.UltraBurst;
                var lobby = FakeLobby.With(campaign);
                flow.OpenTitle();
                flow.Room.ShowLobby(lobby);
                yield return null;
                yield return null;
                Assert.That(boot.Root.Stage.Current, Is.InstanceOf<LobbyScreen>());
                var ai = root.Q("room-set-opt-galactic-ai");
                var human = root.Q("room-set-opt-galactic-human");
                Assert.That(ai, Is.Not.Null);
                Assert.That(human, Is.Not.Null);
                Assert.That(ai.ClassListContains("sn-opt-picked"), Is.EqualTo(ub), campaign + ": 사람 조종으로 저장된 방 — 울트라 버스트면 「AI 조종」으로 보인다");
                Assert.That(human.ClassListContains("sn-opt-picked"), Is.EqualTo(!ub));
                Assert.That(human.ClassListContains("sn-off"), Is.EqualTo(ub), campaign + ": 「사람 조종」 잠김");
                Assert.That(ai.ClassListContains("sn-off"), Is.EqualTo(ub));
                foreach (string id in new[] { "ai", "human" })
                    Assert.That(root.Q("room-set-opt-ultra-" + id).ClassListContains("sn-off"), Is.True, "UB 줄은 늘 잠김");
                if (ub)
                {
                    using (var e = ClickEvent.GetPooled())
                    {
                        e.target = human;
                        human.SendEvent(e);
                    }
                    Assert.That(lobby.Sent, Is.Empty, "잠긴 「사람 조종」을 눌러도 설정을 보내지 않는다");
                    boot.Root.Stage.Tips.Show(human);
                    yield return null;
                    string want = Rekkon.UiKit.KoText.JoinWords(boot.Root.Stage.T("screen.room.set.control.ub_lock.tip"));   // 툴팁 본문은 어절을 이어 붙여 그린다
                    bool found = false;
                    boot.Root.Stage.Tips.Element.Query<Label>().ForEach(l => found |= l.text != null && l.text.Contains(want));
                    Assert.That(found, Is.True, "툴팁 = 울트라 버스트 잠금 문구");
                    boot.Root.Stage.Tips.Hide();
                }
            }
            flow.OpenTitle();
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // ── 소리 단서(A5): 플레이어 프로브의 cue-log.tsv와 같은 줄(GameFlow.Sound — 연출 단서만) ──

        List<SoundCall> Listen()
        {
            var calls = new List<SoundCall>();
            flow.Sound += c => { if (!c.Immediate) calls.Add(c); };
            flow.SoundOnSkip = true;   // 건너뛰는 연출의 소리도 낸다(단서 시각 로그와 같은 길)
            return calls;
        }

        // 그 행의 울음 단서가 그 종 번호로 났고, 재생 층에 그 종의 울음 클립으로 닿았다
        void CryHeard(List<SoundCall> calls, int heardAt, int row, string speciesKey, string what)
        {
            int dex = int.Parse(speciesKey.Substring(0, speciesKey.IndexOf(':')));
            var cry = calls.Find(c => c.Row == row && SoundKeys.TryCry(c.Key, out int d, out _) && d == dex);
            Assert.That(cry.Key, Is.Not.Null, what + ": " + row + "행 울음(cry:" + dex + ") 단서가 없다 — 난 단서: " + string.Join(" · ", calls.FindAll(c => c.Row == row).ConvertAll(c => c.Key)));
            var sound = boot.Sound;
            bool heard = false;
            for (int i = heardAt; i < sound.Heard.Count; i++)
                if (sound.Heard[i].Kind == "cry" && sound.Heard[i].Key == cry.Key && sound.Heard[i].Clip == dex.ToString()) heard = true;
            Assert.That(heard, Is.True, what + ": 울음이 재생 층에 닿지 않았다(" + cry.Key + ")");
            Assert.That(sound.ClipFor(cry.Key), Is.Not.Null, what + ": 클립(Gen/Audio/Cry/" + dex + ".ogg)");
        }

        [UnityTest]
        public IEnumerator M10_A5_진화하면39행의키프레임에진화한종의울음이난다()
        {
            yield return Boot();
            if (boot.Sound.Effects.Count == 0) Assert.Ignore("M4 Gen 없음: 소리 표 먼저");
            FlowPilot.Start(flow, SoloTrail.Work);
            yield return null;
            flow.Skip(flow.OpeningMs);
            yield return Settle();
            var calls = Listen();
            int heardAt = boot.Sound.Heard.Count;
            Assert.That(flow.Hud.Press(BoardHud.Evolve), Is.True, "[진화]");
            yield return null;
            var evolve = boot.Root.Stage.Current as EvolveScreen;
            Assert.That(evolve, Is.Not.Null, "진화 창");
            int starter = evolve.Listed[evolve.Listed.Count - 1];
            Assert.That(evolve.PressEvolve(starter), Is.True, "스타팅 포켓몬 진화");
            for (int i = 0; i < 200 && evolve.Now == EvolveScreen.Mode.Show; i++)
            {
                flow.Skip(100);
                if (i % 10 == 0) yield return null;
            }
            if (evolve.Now == EvolveScreen.Mode.Teach) evolve.PressTeach(-1);
            flow.Skip(400);
            yield return null;
            string to = flow.Ctx.Cards.Pokemon[flow.Client.View.Pokemon[starter].Def].Key;
            CryHeard(calls, heardAt, EvolveBeats.Row, to, "진화");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_A5_배틀에서폼이바뀌면79행의키프레임에그종의울음이난다()
        {
            // UiShots row-79-form과 같은 판: 체리꼬(흐린 날씨 폼)가 시그니처로 폼을 바꾼다 — 고향 마을의 조무래기에게 도전
            yield return Boot();
            if (boot.Sound.Effects.Count == 0) Assert.Ignore("M4 Gen 없음: 소리 표 먼저");
            var ctx = flow.Ctx;
            yield return Open(1, st => GiveTeam(ctx, st, "421:1", "483:2"));
            var calls = Listen();
            int heardAt = boot.Sound.Heard.Count;
            Assert.That(flow.Hud.Press(BoardHud.Challenge), Is.True, "[도전]");
            yield return Fight(() => calls.Exists(c => c.Row == ResultRows.Form && SoundKeys.TryCry(c.Key, out _, out _)));
            var form = calls.Find(c => c.Row == ResultRows.Form && SoundKeys.TryCry(c.Key, out _, out _));
            Assert.That(form.Key, Is.Not.Null, "79행 울음");
            SoundKeys.TryCry(form.Key, out int dex, out _);
            CryHeard(calls, heardAt, ResultRows.Form, dex + ":0", "폼 바뀜");
            Assert.That(dex, Is.EqualTo(421), "체리꼬의 울음");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M10_A5_남의말이선칸에걸어들어가면걷기단계에meet징글단서가난다()
        {
            yield return Boot();
            if (boot.Sound.Effects.Find(SoundKeys.Meet) == null) Assert.Ignore("M4 Gen 없음: 효과음 표(meet) 먼저");
            int target = -1;
            yield return Open(2, state =>
            {
                var walker = state.Seats[state.ActiveSeat];
                var steps = MoveRules.Reachable(flow.Ctx.Board, walker.Cell, walker.MovesLeft, state.Options.ElevationMode);
                for (int c = 0; c < steps.Length && target < 0; c++) if (steps[c] == 2) target = c;
                state.Seats[(state.ActiveSeat + 1) % state.Seats.Length].Cell = target;
            });
            Assert.That(target, Is.GreaterThanOrEqualTo(0));
            var calls = Listen();
            int heardAt = boot.Sound.Heard.Count;
            bool walked = flow.Client.View.ActiveSeat == flow.Client.Seat ? flow.Hud.Walk(target) : flow.WalkActive(target);
            Assert.That(walked, Is.True);
            yield return Settle();
            var meets = calls.FindAll(c => c.Key == SoundKeys.Meet);
            Assert.That(meets.Count, Is.EqualTo(1), "만남 단서 한 번");
            Assert.That(meets[0].Row, Is.EqualTo(110), "걷기 단계(11행)에 얹힌다 — 13행은 대기열 밖 행");
            Assert.That(boot.Sound.ClipFor(SoundKeys.Meet), Is.Not.Null, "meet 클립(Gen/Audio/Se)");
            bool heard = false;
            for (int i = heardAt; i < boot.Sound.Heard.Count; i++) heard |= boot.Sound.Heard[i].Key == SoundKeys.Meet;
            Assert.That(heard, Is.True, "재생 층에 닿았다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
