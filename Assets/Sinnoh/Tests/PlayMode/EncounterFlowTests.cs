using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Rekkon.UiKit;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 타이틀 → 혼자 하기 → 셋업 → 보드 → 나무·산호로 이동 → [찾아보기] → 조우 창(S45) → 야생 배틀 → 배틀 뒤 선택 → 결과 → 보드 → 고향 마을 → [전체 회복] → [도전] → 결과
    /// (plan §3 M4-3 완료 조건 ①의 순서, §4 「조우·배틀 2단계」, 04 §4-1·§4-8). 화면의 함수로만 누른다 — 길잡이(<see cref="SoloPilot"/>)가 고른 답을 <see cref="FlowPilot"/>이 화면 요소로 누르고,
    /// 누르기 전에 그 화면의 요소가 엔진의 합법 답(<c>Client.Legal</c>)과 1:1인지 본다. 받을 화면이 없는 선택(「이 선택 화면은 아직 준비 중입니다.」)이 한 번도 뜨지 않아야 한다.
    /// 판은 찾아 둔 판(<see cref="SoloTrail"/> — dotnet 시험 <c>SoloPilotTests</c>가 같은 판을 화면 없이 돈다): 실제 진행 둘(격파 · 도망)과 볼을 쥐여 준 판 하나(포획 — 볼을 얻는 길이 아직 없다).
    /// Gen 겉모양·무대 그림이 있어야 돈다.
    /// </summary>
    public sealed class EncounterFlowTests
    {
        /// <summary>판을 지나며 본 것.</summary>
        sealed class Seen
        {
            public int SearchButtons, HealButtons, ChallengeButtons, Encounters, Cards, WildTurns, Bags, Balls, Captures, SendIns, Switches, Nexts, Teaches, PartyFulls, Corals, Results, Heals;
            /// <summary>기술 가르치기에서 툴팁을 확인한 원래 기술 칸 단추 수(결정 186).</summary>
            public int TeachTips;
            /// <summary>연출에서 본 것: 조우 창이 뜨기 전(찾아보기 「!」) · 카드 뒷면 · 배틀 화면이 열리기 전의 전환 · 포획 판정 · 요약의 잡은 포켓몬 줄 · 결과 창이 떠 있는데 [확인]은 아직인 때.</summary>
            public int Bangs, Backs, Wipes, Verdicts, SummaryCaught, ResultWaits, Gained;
            /// <summary>내 포켓몬이 아직 화면에 나오지 않았는데(조우 전환 뒤 절반 · 인트로) 내 기술 고르기는 이미 와 있던 프레임 수.</summary>
            public int BeforeMine;
            public readonly List<string> Titles = new List<string>();
            /// <summary>소리(결정 66 ④): 곡 상황이 바뀔 때마다 「상황:클립」 한 줄, 상황의 곡과 음원의 클립이 어긋난 프레임.</summary>
            public readonly List<string> Music = new List<string>(), MusicBad = new List<string>();
            public readonly List<GameEvent> Events = new List<GameEvent>();
            public GameFlow Flow;

            public List<T> Of<T>() where T : GameEvent
            {
                var list = new List<T>();
                foreach (var e in Events) if (e is T t) list.Add(t);
                return list;
            }
        }

        [UnityTest]
        public IEnumerator M4_타이틀에서찾아보기조우격파기술가르치기를거쳐도전결과까지화면으로가고준비중안내0번_S5_1()
        {
            var seen = new Seen();
            yield return Walk(SoloTrail.Defeat, seen);
            Assert.That(seen.SearchButtons, Is.GreaterThan(0), "[찾아보기]가 켜져 있었다");
            Assert.That(seen.Encounters, Is.EqualTo(1), "조우 창");
            Assert.That(seen.Bangs, Is.GreaterThan(0), "찾아보기(「!」)가 도는 동안에는 조우 창이 없었다");
            Assert.That(seen.Backs, Is.GreaterThan(0), "조우 카드가 뒷면으로 놓였다가 뒤집혔다");
            Assert.That(seen.Wipes, Is.GreaterThan(0), "배틀 화면은 조우 전환의 가운데에 열렸다");
            Assert.That(seen.ResultWaits, Is.GreaterThan(0), "결과 창이 뜬 뒤 받은 것이 내려앉는 동안에는 [확인]이 없었다");
            Assert.That(seen.Cards, Is.EqualTo(3), "약한 나무: 카드 3장");
            Assert.That(seen.WildTurns, Is.GreaterThan(0), "야생 배틀의 내 차례");
            Assert.That(seen.Bags, Is.EqualTo(0), "볼이 없는 판에서는 [가방]이 켜지지 않는다(볼을 얻는 길 = 제작은 아직 없다)");
            Assert.That(seen.Teaches, Is.EqualTo(1), "격파 뒤 기술 가르치기 묻기");
            Assert.That(seen.Of<MoveTaught>().Count, Is.EqualTo(1));
            Assert.That(seen.HealButtons, Is.GreaterThan(0), "[마을]이 켜져 있었다(전체 회복은 마을 창에서)");
            Assert.That(seen.Heals, Is.EqualTo(1), "고향 마을에서 [전체 회복]을 눌렀다");
            Assert.That(seen.ChallengeButtons, Is.GreaterThan(0));
            Assert.That(seen.Results, Is.EqualTo(2), "결과 창 둘(야생 · 조무래기)");
            Assert.That(seen.Titles[0], Is.EqualTo("승리!"));
            var kinds = seen.Of<BattleStarted>().ConvertAll(b => b.Kind);
            Assert.That(kinds, Is.EqualTo(new List<BattleKind> { BattleKind.Wild, BattleKind.Galactic }));
        }

        [UnityTest]
        public IEnumerator M4_화면이바뀔때마다곡음원의클립이그상황의곡이다_타이틀보드조우야생결과조무래기()
        {
            // 소리 뼈대(결정 66 ④): 재생 층이 프레임마다 흐름의 곡 상황을 받아 그 상황의 곡(bgm.json)으로 음원 클립을 바꾼다 — 소리가 나는지는 사람이 듣는다
            var seen = new Seen();
            yield return Walk(SoloTrail.Defeat, seen);
            Assert.That(seen.MusicBad, Is.Empty, string.Join("\n", seen.MusicBad));
            var scenes = seen.Music.ConvertAll(m => m.Substring(0, m.IndexOf(':')));
            Assert.That(scenes[0], Is.EqualTo(nameof(MusicScene.Title)), string.Join(" → ", seen.Music));
            int at = 0;
            foreach (var want in new[] { MusicScene.Encounter, MusicScene.BattleWild, MusicScene.Result, MusicScene.BattleGalacticGrunt, MusicScene.ResultGalactic })
            {
                int i = scenes.IndexOf(want.ToString(), at);
                Assert.That(i, Is.GreaterThanOrEqualTo(0), want + " 차례 — " + string.Join(" → ", seen.Music));
                at = i + 1;
            }
            int boardAt = scenes.FindIndex(1, s => s == nameof(MusicScene.Prologue) || s.StartsWith("Field", System.StringComparison.Ordinal));
            Assert.That(boardAt, Is.InRange(1, scenes.IndexOf(nameof(MusicScene.Encounter)) - 1), "조우 전에 보드의 곡 — " + string.Join(" → ", seen.Music));
            int grunt = scenes.IndexOf(nameof(MusicScene.BattleGalacticGrunt));
            Assert.That(scenes.GetRange(0, grunt).FindLastIndex(s => s.StartsWith("Field", System.StringComparison.Ordinal) || s == nameof(MusicScene.Prologue)), Is.GreaterThan(scenes.IndexOf(nameof(MusicScene.Result))),
                "야생 결과 뒤 보드로 돌아오면 보드의 곡 — " + string.Join(" → ", seen.Music));
        }

        [UnityTest]
        public IEnumerator M4_야생이도망치면결과창에서받을규토리색을고른다_A8()
        {
            var seen = new Seen();
            yield return Walk(SoloTrail.Coral, seen);
            Assert.That(seen.Corals, Is.EqualTo(1), "받을 규토리 색 묻기");
            Assert.That(seen.Titles[0], Does.EndWith("도망쳤다!"));
            Assert.That(seen.Of<Fled>().Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator M4_볼을쥐여준판은가방포획출전교체파티가득을화면으로지나1막목표까지간다_G13()
        {
            var seen = new Seen();
            yield return Walk(SoloTrail.Catch, seen);
            Assert.That(seen.Bags, Is.GreaterThan(0), "[가방]");
            Assert.That(seen.Balls, Is.EqualTo(seen.Of<CaptureAttempt>().Count), "볼 줄 누르기 하나 = 포획 시도 하나");
            Assert.That(seen.Captures, Is.EqualTo(3), "「○○을(를) 잡았다!」 결과 창 셋");
            Assert.That(seen.Verdicts, Is.EqualTo(seen.Balls), "던질 때마다 포획 연출(뒷모습 · 포획 굴림 · 판정)을 봤다");
            Assert.That(seen.BeforeMine, Is.GreaterThan(0), "내 포켓몬이 나오기 전(기술 고르기는 이미 와 있다)을 봤다 — 그동안 기술 줄은 없었다");
            Assert.That(seen.SummaryCaught, Is.EqualTo(2), "턴 결과 요약에 잡은 포켓몬 줄(파티에 들어온 둘 — 셋째는 파티가 가득 차 놓아줬다)");
            Assert.That(seen.SendIns, Is.GreaterThan(0), "출전 포켓몬 고르기");
            Assert.That(seen.Switches + seen.Nexts, Is.GreaterThan(0), "교체 창·다음 포켓몬");
            Assert.That(seen.PartyFulls, Is.EqualTo(1), "파티 가득: 놓아줄 포켓몬 고르기");
            Assert.That(seen.Of<Released>().Count, Is.EqualTo(1));
            Assert.That(seen.Flow.Client.View.Seats[0].ActProgress, Is.EqualTo(1), "조무래기를 이겨 1막 목표");
            Assert.That(seen.Titles[seen.Titles.Count - 1], Is.EqualTo("승리!"));
        }

        [UnityTest]
        public IEnumerator M4_찾아보기가도는동안메뉴를열었다닫아도조우창이열리고느낌표는번호표위에뜬다()
        {
            // 검토 반영: [찾아보기] 뒤 조우 창은 화면 대기열(「!」 1750 → 카드 공개)이 열어 주는데, 그 사이에 [메뉴]를 열어 두면 대기열이 메뉴 뒤에서 끝나 창을 열 때를 놓쳤다
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            try
            {
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
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                var pilot = FlowPilot.Start(flow, SoloTrail.Defeat);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");
                var client = flow.Client;
                var hud = flow.Hud;

                // 길잡이가 나무 칸으로 걸어가 [찾아보기]를 누를 때까지(누르면 조우 고르기가 바로 온다 — 창은 아직이다)
                for (int guard = 0; guard < 2000 && client.Choice(ChoiceKind.EncounterPick) == null; guard++)
                {
                    if (!FlowPilot.Step(flow, pilot)) flow.Skip(500);
                    yield return null;
                }
                Assert.That(client.Choice(ChoiceKind.EncounterPick), Is.Not.Null, "[찾아보기]를 눌렀다");
                Assert.That(stage.Current, Is.Null, "조우 창은 카드 공개가 시작할 때 열린다");
                var seq = flow.Fx.Sequencer;
                stage.Tweens.Advance(200);
                yield return null;
                Assert.That(seq.Current, Is.Not.Null);
                Assert.That(seq.Current.Step(EnterRows.Search), Is.Not.Null, "찾아보기 항목이 재생 중");
                Assert.That(hud.BangShown, Is.True, "말 머리 위 「!」");

                // 「!」의 아래 가장자리는 좌석 번호 핀(그림 위 1텍셀 틈 + PinSize) 위다 — 핀을 덮지 않는다
                var board = Object.FindAnyObjectByType<BoardBoot>();
                var rig = Camera.main.GetComponent<BoardCameraRig>();
                Assert.That(flow.PiecePoint(client.Seat, out var bang), Is.True);
                Assert.That(BoardPick.Screen(board, Camera.main, client.View.Seats[client.Seat].Cell, 1920, 1080, out var foot), Is.True);
                var piece = board.Pieces.Of(client.Seat);
                int step = BoardPieces.ZoomK(rig.CurrentZoom), k = BoardPieces.Scale(piece, step, 1080);
                float above = 1080 - foot.y - bang.y * stage.Grade.PanelScale;   // 발 점에서 「!」 아래 가장자리까지(물리 px)
                Assert.That(above, Is.GreaterThanOrEqualTo(BoardPieces.PinTop(piece, step) * k), "「!」가 번호 핀 위에 뜬다(핀과 겹치지 않는다)");
                Assert.That(above, Is.LessThanOrEqualTo(BoardPieces.PinTop(piece, step) * k + (BoardHud.BangAbove + 1) * stage.Grade.PanelScale), "핀 바로 위다");

                // [메뉴]: 찾는 동안에도 열린다. 메뉴 뒤에서 찾아보기 항목이 끝나도 조우 창은 메뉴 위로 열리지 않고, 닫으면 열린다
                flow.OpenMenu();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<BoardMenuScreen>());
                for (int i = 0; i < 8; i++)
                {
                    flow.Skip(1000);
                    yield return null;
                    Assert.That(stage.Current, Is.InstanceOf<BoardMenuScreen>(), "메뉴가 떠 있는 동안 조우 창이 그 위로 열리지 않는다");
                }
                Assert.That(seq.IsEmpty, Is.True, "재생 중이던 찾아보기 항목은 메뉴 뒤에서 끝났다");
                ((BoardMenuScreen)stage.Current).Resume();
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<EncounterScreen>(), "메뉴를 닫으면 조우 창이 열린다");
                Assert.That(seq.Paused, Is.False);
                var encounter = (EncounterScreen)stage.Current;
                for (int i = 0; i < 20 && !encounter.Open; i++)
                {
                    flow.Skip(500);
                    yield return null;
                }
                Assert.That(encounter.Open, Is.True, "카드를 고를 수 있다");
                Assert.That(encounter.FaceUp, Is.EqualTo(encounter.Cards.Count), "카드는 모두 앞면이다");
                Assert.That(encounter.Cards.Count, Is.EqualTo(client.Legal(client.Choice(ChoiceKind.EncounterPick)).FindAll(a => a.Kind == AnswerKind.Pick).Count));
                Assert.That(stage.Root.Q<Label>("board-hud-guide").text, Is.Not.EqualTo(stage.T("screen.board.guide.not_ready")), "HUD에 준비 중 안내가 뜨지 않는다");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0));
                // 고르면 배틀로 이어진다
                Assert.That(FlowPilot.Step(flow, pilot), Is.True, "카드를 골랐다");
                Assert.That(flow.Battle, Is.Not.Null);
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }

        // 재생 층이 지금 고른 상황과 곡 음원의 클립(같은 Want에서 맞춘 짝): 상황이 바뀌면 한 줄, 클립이 그 상황의 곡이 아니면 어긋남
        static void NoteMusic(ScreenBoot boot, Seen seen)
        {
            var sound = boot.Sound;
            if (sound == null || sound.Scene == null) { seen.MusicBad.Add("재생 층이 곡 상황을 받지 않았다"); return; }
            var scene = sound.Scene.Value;
            string clip = sound.MusicSource.clip != null ? sound.MusicSource.clip.name : "";
            string file = sound.Music.File(scene);
            string want = file != null ? System.IO.Path.GetFileNameWithoutExtension(file) : "";
            if (clip != want) seen.MusicBad.Add(scene + ": 클립 「" + clip + "」 ≠ 「" + want + "」");
            string note = scene + ":" + clip;
            if (seen.Music.Count == 0 || seen.Music[seen.Music.Count - 1] != note) seen.Music.Add(note);
        }

        [UnityTest]
        public IEnumerator M10_격파뒤기술가르치기에서원래기술칸에마우스를올리면그기술의이름과효과가뜬다_결정186()
        {
            // 스타팅 포켓몬의 칸 1에 기술을 넣어 둔 격파 판: 결과 창의 칸 단추 = 바꿀 원래 기술 — 그 단추의 툴팁이 기술 줄과 같은 글이다
            var seen = new Seen();
            int given = -1;
            yield return Walk(SoloTrail.Defeat, seen, flow => flow.Rebase(st =>
            {
                var p = st.Pokemon[st.Seats[0].Party[0]];
                int sig = flow.Ctx.Cards.Pokemon[p.Def].Signature;
                for (int m = 0; m < flow.Ctx.Cards.Moves.Count && given < 0; m++)
                    if (m != sig && flow.Stage.TOrNull(BattleFeed.MoveNameKey(flow.Ctx, m) + ".text") != null) given = m;
                p.Moves[0] = given;
            }));
            Assert.That(given, Is.GreaterThanOrEqualTo(0));
            Assert.That(seen.Teaches, Is.EqualTo(1), "격파 뒤 기술 가르치기 묻기");
            Assert.That(seen.TeachTips, Is.GreaterThan(0), "원래 기술이 든 칸 단추의 툴팁을 봤다");
        }

        /// <summary>그 요소에 기술 툴팁이 걸렸고(<see cref="MonParts.MoveTip"/>) 제목 = 기술 이름, 본문 = 효과 문구(+ note 한 줄)인지 본다(결정 186).</summary>
        internal static void TipIs(UiStage stage, VisualElement target, Ctx ctx, int move, string note = null)
        {
            Assert.That(target, Is.Not.Null, "툴팁을 볼 요소");
            stage.Tips.Show(target);
            Assert.That(stage.Tips.Target, Is.SameAs(target), target.name + ": 툴팁이 걸리지 않았다");
            string key = BattleFeed.MoveNameKey(ctx, move);
            string text = stage.TOrNull(key + ".text");
            Assert.That(text, Is.Not.Null, key + ".text");
            if (note != null) text += "\n" + note;
            var labels = stage.Tips.Element.Query<Label>().ToList();
            Assert.That(labels[0].text, Is.EqualTo(stage.T(key)), target.name + ": 툴팁 제목 = 기술 이름");
            Assert.That(labels[labels.Count - 1].text, Is.EqualTo(KoText.JoinWords(text)), target.name + ": 툴팁 본문 = 효과 문구");
            stage.Tips.Hide();
        }

        /// <param name="prepare">셋업 뒤 보드에서 판을 손본다(<see cref="GameFlow.Rebase"/> 등 — null이면 그대로).</param>
        static IEnumerator Walk(SoloTrail trail, Seen seen, System.Action<GameFlow> prepare = null)
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            try
            {
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
                seen.Flow = flow;
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                NoteMusic(boot, seen);

                // ── 타이틀 → 혼자 하기 → (고정 시드로 시작) 셋업을 화면으로 ──
                Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
                var title = (TitleScreen)stage.Current;
                int soloIndex = -1;
                for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == TitleScreen.Solo) soloIndex = i;
                Assert.That(title.Activate(soloIndex), Is.True);
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<SoloScreen>());
                flow.EventsSeen += seen.Events.AddRange;
                var pilot = FlowPilot.Start(flow, trail);   // 찾아 둔 판의 시드로 시작한다([게임 시작]은 아무 시드나 고른다)
                if (prepare != null) prepare(flow);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");
                var client = flow.Client;
                var hud = flow.Hud;
                string notReady = stage.T("screen.board.guide.not_ready");

                // ── 보드 → … → 조무래기 배틀의 결과 창을 닫고 다음 턴까지 ──
                bool galacticEnded = false, done = false;
                float t0 = Time.realtimeSinceStartup;
                int guard = 0;
                while (!done && Time.realtimeSinceStartup - t0 < 240f && guard++ < 40000)
                {
                    flow.Skip(1000);   // 방장 멈춤과 화면 연출을 함께 건너뛴다(실시각으로는 공격 하나가 8초쯤이다)
                    yield return null;
                    NoteMusic(boot, seen);
                    var guide = stage.Root.Q<Label>("board-hud-guide");
                    if (guide != null) Assert.That(guide.text, Is.Not.EqualTo(notReady), "HUD에 준비 중 안내가 떴다");
                    var battleGuide = stage.Root.Q<Label>("battle-guide");
                    if (battleGuide != null) Assert.That(battleGuide.text.Replace("⁠", ""), Is.Not.EqualTo(notReady), "배틀 화면에 준비 중 안내가 떴다");

                    var battle = stage.Current as BattleScreen;
                    var encounter = stage.Current as EncounterScreen;
                    var choice = client.FirstChoice();
                    var legal = choice != null ? client.Legal(choice) : new List<ChoiceAnswer>();
                    var seq = flow.Fx.Sequencer;

                    // 내 포켓몬이 화면에 나오기 전(조우 전환 뒤 절반 · 인트로)에는 기술 고르기가 이미 와 있어도 기술 줄을 그리지 않는다(드러나기 전 정보가 먼저 보이지 않게)
                    if (battle != null && flow.Battle != null && !flow.Battle.Ended && !battle.ShownMon(flow.Battle.MySide).Exists)
                    {
                        Assert.That(battle.Moves.Count, Is.EqualTo(0), "내 포켓몬이 나오기 전에는 기술 줄이 없다");
                        Assert.That(stage.Root.Q("battle-move-1"), Is.Null);
                        if (choice != null && choice.Kind == ChoiceKind.BattleAction) seen.BeforeMine++;
                    }

                    // 배틀이 시작됐는데 배틀 화면이 아직이면: 조우 전환(22행)의 앞 절반이다 — 화면 대기열이 그 가운데에 닿아야 열린다
                    if (flow.Battle != null && battle == null)
                    {
                        var wipe = seq.Current != null ? seq.Current.Step(EnterRows.Wipe) : null;
                        Assert.That(wipe, Is.Not.Null, "배틀 화면이 없으면 전환이 재생 중이다");
                        Assert.That(seq.LocalMs, Is.LessThan(wipe.StartMs + wipe.Ms / 2), "전환의 가운데 전");
                        if (encounter != null) Assert.That(encounter.Picked, Is.GreaterThanOrEqualTo(0), "고른 카드가 강조된 채 조우 창이 남아 있다");
                        seen.Wipes++;
                        continue;
                    }
                    // 결과 창이 떴지만 그 항목이 아직 재생 중이다(70행 — 받은 것이 하나씩 내려앉는다): [확인]은 아직 듣지 않고, 턴 넘김 띠도 오지 않았다
                    if (battle != null && battle.ResultShown && !battle.ShowsResult && !battle.AskingReward)
                    {
                        Assert.That(battle.Confirm(), Is.False, "연출이 끝나기 전에는 [확인]이 듣지 않는다");
                        var foot = stage.Root.Q("battle-result-foot");
                        if (foot != null) Assert.That(foot.resolvedStyle.visibility, Is.EqualTo(Visibility.Hidden), "[확인]은 아직 보이지 않는다");
                        Assert.That(hud.Band.ActiveRow, Is.EqualTo(0), "결과 창 뒤에서 넘김 띠가 지나가지 않는다");
                        Assert.That(battle.ResultLanded, Is.LessThanOrEqualTo(battle.ResultLines));
                        seen.ResultWaits++;
                        continue;
                    }

                    if (battle != null && battle.ShowsResult && !battle.AskingReward && client.Choice(ChoiceKind.Reward) == null)
                    {
                        // 결과 창: 제목 + [확인]
                        var titleLabel = stage.Root.Q<Label>("battle-result-title");
                        string resultTitle = titleLabel.text.Replace(KoText.WordJoiner.ToString(), "");
                        var panelBox = stage.Root.Q("battle-result-panel").worldBound;
                        Assert.That(titleLabel.worldBound.xMin, Is.GreaterThanOrEqualTo(panelBox.xMin), "제목이 결과 창 안에: " + resultTitle);
                        Assert.That(titleLabel.worldBound.xMax, Is.LessThanOrEqualTo(panelBox.xMax), "제목이 결과 창 안에: " + resultTitle);
                        Assert.That(stage.Root.Q<Button>("battle-result-ok"), Is.Not.Null);
                        Assert.That(stage.Root.Q("battle-reward"), Is.Null, "묻는 것이 끝나면 선택 단추가 없다");
                        seen.Titles.Add(resultTitle);
                        seen.Results++;
                        if (flow.Battle.Result == BattleResult.Captured)
                        {
                            seen.Captures++;
                            Assert.That(resultTitle, Is.EqualTo(KoJosa.Fix(stage.T("screen.battle.result.captured").Replace("{mon}", stage.T(flow.Battle.MonKey(flow.Battle.CaughtId))))));
                        }
                        if (flow.Battle.Taught != null)
                            Assert.That(stage.Root.Q("battle-result-lines").Query<Label>().ToList().Exists(l => l.text.Contains(stage.T(BattleFeed.MoveNameKey(flow.Ctx, flow.Battle.Taught.MoveKey)))), "가르친 기술이 결과 창에");
                        if (flow.Battle.ReleasedKey != null)
                            Assert.That(stage.Root.Q("battle-result-lines").Query<Label>().ToList().Exists(l => l.text.Contains(stage.T(flow.Battle.ReleasedKey))), "놓아준 포켓몬이 결과 창에");
                        galacticEnded |= flow.Battle.Kind == BattleKind.Galactic;
                        // [확인] 전: 받은 것이 모두 내려앉았고, 턴 넘김(요약 → 띠 → 컷신)은 화면 대기열에서 기다린다
                        Assert.That(battle.ResultLanded, Is.EqualTo(battle.ResultLines), "결과 줄이 모두 내려앉았다");
                        Assert.That(stage.Root.Q("battle-result-foot").resolvedStyle.visibility, Is.EqualTo(Visibility.Visible));
                        Assert.That(hud.Band.ActiveRow, Is.EqualTo(0), "[확인] 전에는 넘김 띠가 오지 않는다");
                        Assert.That(seq.Paused, Is.True, "화면 대기열은 [확인]을 기다린다");
                        bool caught = flow.Battle.Result == BattleResult.Captured && seen.Of<PokemonGained>().Count > seen.Gained;
                        string caughtLine = caught ? KoJosa.Fix(stage.T("screen.board.summary.caught").Replace("{mon}", stage.T(flow.Battle.MonKey(flow.Battle.CaughtId)))) : null;
                        seen.Gained = seen.Of<PokemonGained>().Count;
                        bool turnEnds = seq.Pending > 0;
                        Assert.That(battle.Confirm(), Is.True);
                        Assert.That(stage.Current, Is.Null, "결과 창을 닫으면 보드");
                        if (turnEnds)
                        {
                            // 역전환: 줄무늬가 덮은 채로 시작해 걷힌다. 잡은 턴에는 요약이 먼저 뜨고 거기 잡은 포켓몬이 적힌다(결정 57 ③ — 변화가 있을 때만)
                            Assert.That(flow.WipeCover, Is.EqualTo(1f), "보드로 돌아오는 역전환이 덮은 채로 시작한다");
                            flow.Skip(EnterBeats.WipeBackMs + 100);
                            yield return null;
                            Assert.That(flow.WipeCover, Is.EqualTo(0f), "줄무늬가 걷혔다");
                            if (caught)
                            {
                                Assert.That(hud.Band.ActiveRow, Is.EqualTo(FxRows.TurnSummary), "턴 결과 요약");
                                var lines = stage.Root.Q("turn-band-summary").Query<Label>().ToList().ConvertAll(l => l.text);
                                Assert.That(lines, Does.Contain(caughtLine), "요약에 잡은 포켓몬");
                                Assert.That(lines.Exists(l => l.StartsWith("여행 점수 +")), "요약에 받은 여행 점수");
                                Assert.That(lines.Exists(l => l.StartsWith("규토리 +")), "요약에 받은 규토리");
                                seen.SummaryCaught++;
                            }
                        }
                        continue;
                    }
                    if (galacticEnded && flow.InputOpen && client.Choice(ChoiceKind.MapAction) != null) { done = true; break; }
                    if (choice == null) continue;

                    if (choice.Kind == ChoiceKind.MapAction && flow.InputOpen)
                    {
                        // 보드 HUD: 판의 단추는 모두 켜져 있다(회색 없음). 마을 행동은 [마을] 하나 — 마을 행동을 하는 곳에 서 있을 때만
                        foreach (var act in BoardHud.MapActs)
                        {
                            if (act.Kind == AnswerKind.VillageHeal ? !flow.AtVillage(out _, out _) : !legal.Exists(a => a.Kind == act.Kind)) continue;   // [마을]만 서 있는 칸으로(제작·진화 창은 합법 답으로 — M5)
                            var button = stage.Root.Q<Button>("board-hud-act-" + act.Id);
                            Assert.That(button, Is.Not.Null, act.Id);
                            Assert.That(button.ClassListContains("sn-hud-off"), Is.False, act.Id + ": 회색 단추는 없다");
                            Assert.That(button.enabledSelf, Is.True, act.Id);
                            if (act.Id == BoardHud.Search) seen.SearchButtons++;
                            else if (act.Id == BoardHud.Village) seen.HealButtons++;
                            else if (act.Id == BoardHud.Challenge) seen.ChallengeButtons++;
                        }
                    }
                    else if (choice.Kind == ChoiceKind.EncounterPick)
                    {
                        if (encounter == null)
                        {
                            // 찾아보기(20행 1750)가 도는 동안: 조우 창은 아직 없고 말 머리 위에 「!」가 뜬다. 창은 카드 공개(21행)가 시작할 때 열린다
                            var reveal = seq.Current != null ? seq.Current.Step(EnterRows.Reveal) : null;
                            Assert.That(reveal, Is.Not.Null, "조우 창이 없으면 찾아보기 항목이 재생 중이다");
                            Assert.That(seq.LocalMs, Is.LessThan(reveal.StartMs), "카드 공개가 시작하기 전");
                            Assert.That(flow.InputOpen, Is.False, "찾는 동안 보드 입력은 닫혀 있다");
                            if (seq.LocalMs < EnterBeats.BangEndMs) Assert.That(hud.BangShown, Is.True, "말 머리 위 「!」");
                            seen.Bangs++;
                            continue;
                        }
                        Assert.That(hud.BangShown, Is.False, "조우 창이 열리면 「!」는 없다");
                        if (encounter.FaceUp < encounter.Cards.Count)
                        {
                            // 카드가 뒷면으로 놓였다가 왼쪽부터 뒤집힌다 — 다 뒤집히기 전에는 고를 수 없다
                            Assert.That(encounter.Open, Is.False);
                            Assert.That(stage.Root.Q("encounter-card-" + encounter.Cards.Count + "-back").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "마지막 카드는 아직 뒷면");
                            seen.Backs++;
                        }
                        Assert.That(stage.Hud, Is.InstanceOf<BoardHud>(), "HUD는 그대로 있다");
                        Assert.That(flow.InputOpen, Is.False, "창이 열려 있으면 보드 입력은 닫힌다");
                        Assert.That(hud.Press(BoardHud.EndTurn), Is.False);
                        if (!encounter.Open) continue;
                        var cards = legal.FindAll(a => a.Kind == AnswerKind.Pick);
                        // 카드·[배틀!] = 합법 답의 카드(Pick)와 1:1(순서까지 — 그 뒤의 고르기 전 제작 답은 제작 화면 묶음이 받는다, 룰북 p.2), 카드 수 = 뽑힌 수
                        var drawn = seen.Of<EncounterDrawn>();
                        Assert.That(encounter.Cards.Count, Is.EqualTo(drawn[drawn.Count - 1].Cards.Length), "카드 수 = EncounterDrawn.Cards 수");
                        Assert.That(encounter.Cards.Count, Is.EqualTo(cards.Count), "[배틀!] 수 = 합법 답 수");
                        for (int i = 0; i < cards.Count; i++)
                        {
                            Assert.That(encounter.Cards[i].SameAs(cards[i]), "카드 " + (i + 1) + " = 합법 답 그대로");
                            Assert.That(stage.Root.Q("encounter-card-" + (i + 1)), Is.Not.Null);
                            Assert.That(stage.Root.Q<Button>("encounter-pick-" + (i + 1)).enabledSelf, Is.True);
                            Assert.That(stage.Root.Q<Label>("encounter-card-" + (i + 1) + "-name").text, Is.EqualTo(stage.T("pokemon." + flow.Ctx.Cards.Pokemon[cards[i].Target].Key)));
                        }
                        Assert.That(stage.Root.Q("encounter-card-" + (cards.Count + 1)), Is.Null, "합법 답에 없는 카드는 없다");
                        // 공통 값은 엔진 함수에서
                        var grade = MapActions.GradeOf(flow.Ctx, cards[0].Target);
                        Assert.That(stage.Root.Q<Label>("encounter-rate").text, Is.EqualTo("포획률 " + BattleRules.CatchRate(grade, false) + "+"));
                        Assert.That(stage.Root.Q<Label>("encounter-jp").text, Is.EqualTo("여행 점수 +" + BattleRules.RewardJp(grade, false, false)));
                        int token = MapActions.EncounterTokenAt(client.View, choice.Subject);
                        Assert.That(stage.Root.Q("encounter-reward").Query(className: "sn-icon").ToList().Count, Is.EqualTo(flow.Ctx.Fixtures.Fixtures[client.View.Tokens[token].Def].Apricorns.Count), "보상 규토리 = 그 칸 토큰의 규토리");
                        Assert.That(stage.Root.Q("encounter-prep").Query(className: "sn-enc-chip").ToList().Count, Is.EqualTo(SeatState.BallKinds), "배틀 전 준비: 볼 재고");
                        // 고유 기술 줄의 툴팁은 줄 아래(§10-2)
                        var moveRow = stage.Root.Q("encounter-card-1-move");
                        stage.Tips.Show(moveRow);
                        yield return null;
                        yield return null;
                        Assert.That(stage.Tips.Element.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible));
                        Assert.That(stage.Tips.Element.worldBound.yMin, Is.GreaterThanOrEqualTo(moveRow.worldBound.yMax - 0.5f), "툴팁이 기술 줄 아래에");
                        stage.Tips.Hide();
                        // 창이 화면 안에(위 띠 아래 · HUD 아래 판 위)
                        var panel = stage.Root.Q("encounter-panel").worldBound;
                        Assert.That(panel.yMax, Is.LessThanOrEqualTo(stage.Root.Q("board-hud-dock").worldBound.yMin), "조우 창이 HUD 아래 판을 덮지 않는다");
                        seen.Encounters++;
                        seen.Cards = cards.Count;
                    }
                    else if (battle != null && choice.Kind == ChoiceKind.BattleAction && battle.Open && !battle.AskingToken)
                    {
                        // 배틀의 내 차례: 기술 줄 + [가방](볼 던지기가 합법일 때만) + [기권] = 합법 답 전부
                        var useMoves = legal.FindAll(a => a.Kind == AnswerKind.UseMove && a.Arg < 0 && a.Target != BattleRules.AlphaStrike);   // 벤치에서 쓰는 기술(Arg = 벤치 포켓몬, rulings G20) · 알파 스트라이크(A26)는 줄에 없다 [가정 plan §5 #84 · #86 ①]
                        int fromBench = legal.FindAll(a => a.Kind == AnswerKind.UseMove && (a.Arg >= 0 || a.Target == BattleRules.AlphaStrike)).Count;
                        var throws = legal.FindAll(a => a.Kind == AnswerKind.ThrowBall);
                        bool canForfeit = legal.Exists(a => a.Kind == AnswerKind.Forfeit);
                        Assert.That(battle.Moves.Count, Is.EqualTo(useMoves.Count));
                        Assert.That(legal.Count, Is.EqualTo(useMoves.Count + fromBench + throws.Count + (canForfeit ? 1 : 0)), "합법 답 = 기술 줄 + 볼 줄 + 기권(+ 벤치 기술)");
                        Assert.That(stage.Root.Q("battle-party"), Is.Null, "[포켓몬] 단추는 없다(자발 교체는 규칙에 없다)");
                        Assert.That(battle.BagOn, Is.EqualTo(throws.Count > 0), "[가방]은 볼 던지기가 합법일 때만 켜진다");
                        Assert.That(stage.Root.Q("battle-bag").ClassListContains("sn-hud-off"), Is.EqualTo(throws.Count == 0));
                        if (flow.Battle.Kind != BattleKind.Galactic)
                        {
                            seen.WildTurns++;
                            Assert.That(stage.Root.Q("battle-foe-wild"), Is.Not.Null, "야생: 등급 · 포획률 · 보상 규토리");
                            // 상대 기술 칩 = 그 포켓몬의 기술(엔진 순서) + 카드 공격력. 카드에 공격력이 없는 기술(흉내내기 …)은 「0」이 아니라 「?」다
                            var foeMon = battle.ShownMon(1 - flow.Battle.MySide);
                            var foeMoves = new List<int>();
                            PokemonRules.MoveOptions(flow.Ctx, flow.Battle.Source.Pokemon[foeMon.Id], foeMoves);
                            var chips = stage.Root.Q("battle-foe-moves").Query<Label>(className: "kit-badge").ToList().FindAll(l => l.name != "battle-foe-move-bonus");   // 상성 배지(T19)는 기술 칩이 아니다
                            Assert.That(chips.Count, Is.EqualTo(foeMoves.Count), "상대 기술 칩 수");
                            for (int i = 0; i < foeMoves.Count; i++)
                            {
                                Assert.That(chips[i].text, Does.EndWith(stage.T(BattleFeed.MoveNameKey(flow.Ctx, foeMoves[i])) + " " + BattleScreen.CardStrength(flow.Ctx, foeMoves[i])));
                                // 결정 187: 칩 묶음 안에 그 기술의 타입 그림(타입 없는 전술 · 알파 스트라이크는 없다)
                                bool typed = BattleRules.MoveType(flow.Ctx, foeMoves[i]) != PokemonType.Typeless && foeMoves[i] != BattleRules.AlphaStrike;
                                Assert.That(stage.Root.Q("battle-foe-move-" + i).Query(className: "sn-type-icon").ToList().Count, Is.EqualTo(typed ? 1 : 0), "상대 기술 칩의 타입 그림 " + i);
                            }
                            int mimic = -1;
                            for (int i = 0; i < flow.Ctx.Cards.Moves.Count; i++) if (flow.Ctx.Cards.Moves[i].Key == "mimic") mimic = i;
                            Assert.That(flow.Ctx.Cards.Moves[mimic].AttackStrength, Is.Null, "흉내내기: 카드에 공격력이 없다");
                            Assert.That(BattleScreen.CardStrength(flow.Ctx, mimic), Is.EqualTo("?"));
                            Assert.That(BattleScreen.CardStrength(flow.Ctx, BattleRules.PlanB), Is.EqualTo(BattleRules.PlanBStrength.ToString()));
                        }
                        if (throws.Count == 0) Assert.That(battle.OpenBag(), Is.False, "꺼진 [가방]은 열리지 않는다");
                        else
                        {
                            // [가방] → 볼 줄 = 합법 ThrowBall(가진 볼만 · 등급 순서) + [뒤로]
                            int commands = flow.Session.CommandCount;
                            Assert.That(battle.OpenBag(), Is.True);
                            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "[가방]은 답을 내지 않는다");
                            Assert.That(battle.Balls.Count, Is.EqualTo(throws.Count), "볼 줄 수 = 합법 ThrowBall 수");
                            for (int i = 0; i < throws.Count; i++)
                            {
                                Assert.That(battle.Balls[i].SameAs(throws[i]));
                                var row = stage.Root.Q("battle-ball-" + (i + 1));
                                Assert.That(row, Is.Not.Null);
                                Assert.That(row.Q<Label>(className: "sn-battle-move-name").text, Is.EqualTo(stage.T(BattleFeed.BallKey(throws[i].Target))));
                            }
                            Assert.That(stage.Root.Q("battle-ball-" + (throws.Count + 1)), Is.Null);
                            Assert.That(stage.Root.Q("battle-move-1"), Is.Null, "볼 줄이 보이는 동안 기술 줄은 없다");
                            Assert.That(battle.PressMove(0), Is.False);
                            Assert.That(stage.Root.Q<Button>("battle-bag-back"), Is.Not.Null);
                            Assert.That(battle.CloseBag(), Is.True);
                            Assert.That(stage.Root.Q("battle-move-1"), Is.Not.Null, "[뒤로] → 기술 줄");
                            seen.Bags++;
                            // 볼을 던진다(길잡이의 답) → 계산 줄에 주사위 합 / 포획률
                            int attempts = seen.Of<CaptureAttempt>().Count;
                            Assert.That(FlowPilot.Step(flow, pilot), Is.True, "볼 줄 누르기");
                            seen.Balls++;
                            var tries = seen.Of<CaptureAttempt>();
                            Assert.That(tries.Count, Is.EqualTo(attempts + 1));
                            var last = tries[tries.Count - 1];
                            Assert.That(flow.Battle.LastCapture, Is.SameAs(last));
                            // 포획 연출(68행, 결정 54 ①): 던짐과 함께 포획 주사위가 구르고(몸 색 #E0E4E8), 내 주인공 뒷모습이 들어와 던진다.
                            // 합계는 판정 100ms 전에 서고, 성공·실패 칸은 판정(= max(2350, 1650 + 470 × 흔들림 수)) 때 켜진다
                            yield return null;
                            Assert.That(seq.Current, Is.Not.Null, "포획 항목이 재생 중이다");
                            var throwStep = CaptureStage.StepOf(seq.Current);
                            Assert.That(throwStep, Is.Not.Null);
                            Assert.That(throwStep.Ms, Is.EqualTo(Sinnoh.Rules.Session.PlayTiming.CaptureMs(last.Success, last.Shakes)), "포획 단계 길이");
                            Assert.That(battle.RollIsCapture, Is.True, "독의 굴림판 = 포획 굴림");
                            Assert.That(battle.RollBoardOn, Is.True, "포획 주사위 그림(녹화본 재생)");
                            Assert.That(battle.Capture.Trainer, Is.True, "내 주인공 뒷모습");
                            Assert.That(stage.Root.Q<Label>("battle-roll-count").text, Is.EqualTo("포획 주사위 " + last.Faces.Length + "개"));
                            Assert.That(battle.CaptureVerdictAtMs, Is.EqualTo(throwStep.StartMs + Sinnoh.Rules.Session.PlayTiming.CaptureVerdictMs(last.Shakes)), "판정 시각");
                            Assert.That(battle.CaptureSumAtMs, Is.EqualTo(battle.CaptureVerdictAtMs - EnterBeats.SumLeadMs), "합계는 판정 100ms 전");
                            Assert.That(battle.CaptureVerdictLit, Is.False, "판정 전에는 성공·실패가 보이지 않는다");
                            Assert.That(battle.PressMove(0), Is.False, "연출 중에는 답이 나가지 않는다");
                            int stock = client.View.Seats[0].Balls[last.BallKey];
                            stage.Tweens.Advance(throwStep.StartMs + EnterBeats.ReleaseMs - seq.LocalMs);
                            Assert.That(stage.Root.Q<Label>("battle-roll-stock").text, Is.EqualTo("×" + stock), "볼 재고는 던지는 순간 줄어든다");
                            Assert.That(battle.Capture.Ball, Is.True, "볼이 손을 떠났다");
                            stage.Tweens.Advance(battle.CaptureVerdictAtMs - 1 - seq.LocalMs);
                            Assert.That(battle.CaptureSumLit, Is.True, "합계가 섰다");
                            Assert.That(battle.RollSum, Is.EqualTo(last.Sum), "굴림판의 합계 = 엔진의 합");
                            Assert.That(battle.CaptureVerdictLit, Is.False, "판정 1ms 전");
                            Assert.That(battle.ShownLineCount, Is.LessThan(flow.Battle.Lines.Count), "기록 줄도 판정 전에는 붙지 않는다");
                            stage.Tweens.Advance(1);
                            Assert.That(battle.CaptureVerdictLit, Is.True, "판정");
                            Assert.That(battle.Capture.BallDark, Is.EqualTo(last.Success), "성공이면 볼이 어두워진다(딸깍)");
                            stage.Tweens.Advance(throwStep.StartMs + CaptureStage.LineMs(last) - seq.LocalMs);
                            Assert.That(battle.Message, Is.EqualTo(flow.Battle.Lines.FindLast(l => ReferenceEquals(l.Event, last) && l.Key != BattleFeed.K + "capture.roll").Text(stage.T)), "결말 대사");
                            if (last.Success) Assert.That(battle.Message, Does.StartWith("신난다!"));
                            seen.Verdicts++;
                            var chips = stage.Root.Q("battle-calc-chips").Query<Label>().ToList().ConvertAll(l => l.text);
                            Assert.That(chips, Does.Contain(last.Sum.ToString()), "주사위 합");
                            Assert.That(chips, Does.Contain(last.CatchRate + "+"), "포획률");
                            Assert.That(stage.Root.Q("battle-calc-capture").Query<Label>().ToList()[1].text, Is.EqualTo(last.Success ? "성공" : "실패"));
                            continue;
                        }
                    }
                    else if (battle != null && battle.Open && (choice.Kind == ChoiceKind.SendIn || choice.Kind == ChoiceKind.NextPokemon || choice.Kind == ChoiceKind.SwitchWindow))
                    {
                        // 대기 포켓몬 칸 = 합법 Pick, 교체 창이면 [교체 안 함]
                        var picks = legal.FindAll(a => a.Kind == AnswerKind.Pick);
                        bool stay = legal.Exists(a => a.Kind == AnswerKind.Decline);
                        Assert.That(battle.Bench.Count, Is.EqualTo(picks.Count), choice.Kind + ": 칸 수 = 합법 Pick 수");
                        Assert.That(legal.Count, Is.EqualTo(picks.Count + (stay ? 1 : 0)));
                        for (int i = 0; i < picks.Count; i++)
                        {
                            Assert.That(battle.Bench[i].SameAs(picks[i]));
                            var tile = stage.Root.Q("battle-bench-" + (i + 1));
                            Assert.That(tile, Is.Not.Null);
                            Assert.That(tile.enabledSelf, Is.True);
                            Assert.That(tile.Q(className: "sn-mon"), Is.Not.Null, "도트");
                        }
                        Assert.That(stage.Root.Q("battle-bench-" + (picks.Count + 1)), Is.Null);
                        Assert.That(battle.CanStay, Is.EqualTo(choice.Kind == ChoiceKind.SwitchWindow));
                        Assert.That(stage.Root.Q<Button>("battle-bench-stay") != null, Is.EqualTo(stay), "[교체 안 함]은 교체 창에만");
                        Assert.That(stage.Root.Q("battle-move-1"), Is.Null);
                        Assert.That(stage.Root.Q("battle-calc").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "대기 포켓몬 칸이 독 오른쪽까지 쓴다");
                        string want = stage.T(choice.Kind == ChoiceKind.SendIn ? "screen.battle.guide.sendin" : choice.Kind == ChoiceKind.NextPokemon ? "screen.battle.guide.next" : "screen.battle.guide.switch");
                        Assert.That(stage.Root.Q<Label>("battle-guide").text.Replace("⁠", ""), Is.EqualTo(want));
                        if (choice.Kind == ChoiceKind.SendIn) seen.SendIns++;
                        else if (choice.Kind == ChoiceKind.NextPokemon) seen.Nexts++;
                        else seen.Switches++;
                    }
                    else if (battle != null && choice.Kind == ChoiceKind.Reward && battle.Ready)
                    {
                        // 배틀 뒤 선택: 결과 창 안의 단추 = 합법 답과 1:1, 답하기 전에는 [확인]이 없다
                        Assert.That(battle.AskingReward, Is.True);
                        Assert.That(battle.Rewards.Count, Is.EqualTo(legal.Count), choice.Sub + ": 단추 수 = 합법 답 수");
                        for (int i = 0; i < legal.Count; i++)
                        {
                            Assert.That(battle.Rewards[i].SameAs(legal[i]));
                            var control = stage.Root.Q("battle-reward-" + (i + 1));
                            Assert.That(control, Is.Not.Null, choice.Sub + " 단추 " + (i + 1));
                            Assert.That(control.enabledSelf, Is.True);
                        }
                        Assert.That(stage.Root.Q("battle-reward-" + (legal.Count + 1)), Is.Null);
                        Assert.That(stage.Root.Q("battle-result-ok"), Is.Null, "답하기 전에는 [확인]이 없다");
                        Assert.That(battle.Confirm(), Is.False);
                        Assert.That(stage.Root.Q("battle-result").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                        if (choice.Sub == ChoiceSub.TeachMove)
                        {
                            seen.Teaches++;
                            Assert.That(flow.Battle.Ended, Is.True);
                            Assert.That(stage.Root.Q<Label>("battle-reward-ask").text.Replace("⁠", ""), Does.Contain(stage.T(BattleFeed.MoveNameKey(flow.Ctx, choice.Subject))));
                            // 결정 186: 새 기술 줄 · 원래 기술이 든 칸 단추에 마우스를 올리면 그 기술의 이름 + 효과 문구
                            TipIs(stage, stage.Root.Q("battle-reward-move"), flow.Ctx, choice.Subject);
                            for (int i = 0; i < legal.Count; i++)
                            {
                                if (legal[i].Kind != AnswerKind.Pick) continue;
                                int old = client.View.Pokemon[legal[i].Target].Moves[legal[i].Arg];
                                if (old < 0) continue;
                                TipIs(stage, stage.Root.Q("battle-reward-" + (i + 1)), flow.Ctx, old);
                                seen.TeachTips++;
                            }
                        }
                        else if (choice.Sub == ChoiceSub.PartyFull)
                        {
                            seen.PartyFulls++;
                            Assert.That(stage.Root.Q("battle-reward-1").Q<Label>(className: "kit-badge"), Is.Not.Null, "첫 칸 = 새로 잡은 포켓몬(배지)");
                        }
                        else if (choice.Sub == ChoiceSub.CoralColor)
                        {
                            seen.Corals++;
                            Assert.That(flow.Battle.Ended, Is.False, "색을 묻는 동안 배틀은 아직 끝나지 않았다");
                            for (int i = 0; i < legal.Count; i++)
                                Assert.That(stage.Root.Q("battle-reward-" + (i + 1)).Query(className: "sn-icon").ToList().Count, Is.EqualTo(choice.Count), "단추의 규토리 그림 수 = 고를 개수");
                        }
                    }

                    // 길잡이의 답을 화면으로 누른다([전체 회복]은 누른 뒤의 HUD를 본다)
                    var answer = pilot.Decide(flow.Ctx, client.View, choice, legal);
                    bool healing = answer != null && answer.Kind == AnswerKind.VillageHeal && flow.InputOpen;
                    bool pressed = FlowPilot.Step(flow, pilot);
                    if (healing && pressed)
                    {
                        seen.Heals++;
                        var view = client.View;
                        foreach (int id in view.Seats[0].Party)
                            if (id >= 0) Assert.That(PokemonRules.Hp(flow.Ctx, view.Pokemon[id]), Is.EqualTo(PokemonRules.MaxHp(flow.Ctx, view.Pokemon[id])), "전체 회복");
                        Assert.That(stage.Root.Q("board-hud-used").Query<Label>().ToList().Exists(l => l.text == stage.T("screen.board.used.village_heal")), "「이번 턴」에 전체 회복 표시");
                        // 턴당 1회: [마을]은 그대로 있고, 마을 창의 [전체 회복]이 꺼지며 까닭이 선다
                        for (int w = 0; w < 40 && !flow.InputOpen; w++) flow.Skip(100);   // 회복 연출(17행 2800 — 파티 칸 반짝임 · 징글 끝에 체력) + 클릭 막기 300ms
                        Assert.That(hud.Press(BoardHud.Village), Is.True, "[마을]은 회복한 뒤에도 열린다");
                        var villageScreen = (VillageScreen)stage.Current;
                        Assert.That(villageScreen.CanHeal, Is.False, "턴당 1회");
                        Assert.That(villageScreen.HealWhy, Is.EqualTo("screen.village.used"));
                        Assert.That(stage.Root.Q<Button>("board-village-heal").enabledSelf, Is.False);
                        Assert.That(villageScreen.PressHeal(), Is.False);
                        flow.CloseVillage();
                        Assert.That(stage.Current, Is.Null);
                        int p0 = view.Seats[0].Party[0];
                        string hpText = stage.Root.Q("board-hud-mon-0").Query<Label>().ToList().ConvertAll(l => l.text).Find(t => t == PokemonRules.MaxHp(flow.Ctx, view.Pokemon[p0]).ToString());
                        Assert.That(hpText, Is.Not.Null, "HUD의 체력이 바로 찬다");
                    }
                }
                Assert.That(done, Is.True, trail.Name + ": 끝까지 가지 못했다 — 화면 " + (stage.Current != null ? stage.Current.Name : "보드") + ", 선택 " + (client.FirstChoice() != null ? client.FirstChoice().Kind + "/" + client.FirstChoice().Sub : "없음")
                    + ", 받을 화면 없는 선택 " + flow.NotReadyShown + "번");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0), "「이 선택 화면은 아직 준비 중입니다.」가 한 번도 뜨지 않았다");
                Assert.That(stage.Current, Is.Null);
                Assert.That(flow.Battle, Is.Null);
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }
    }
}
