using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    public sealed class SmokeTests
    {
        /// <summary>
        /// 보드 씬(빌드 설정의 `Assets/Sinnoh/Scenes/Board.unity`)을 열고 몇 프레임 돌려도 에러·예외 로그가 0이고,
        /// BoardBoot가 board.json 351칸을 Sinnoh.Data로 읽었다(M4-2a, d3-3 §4). Gen/ 본체가 없어도 돈다(씬은 Gen을 참조하지 않는다, 03 §0).
        /// </summary>
        [UnityTest]
        public IEnumerator M4_보드씬열면에러로그0()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }
            Application.logMessageReceived += Grab;
            try
            {
                yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
                for (int i = 0; i < 10; i++) yield return null;
            }
            finally { Application.logMessageReceived -= Grab; }

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo("Assets/Sinnoh/Scenes/Board.unity"));
            var boot = Object.FindAnyObjectByType<BoardBoot>();
            Assert.That(boot, Is.Not.Null, "BoardBoot 없음");
            Assert.That(boot.Board.Count, Is.EqualTo(351));
            Assert.That(boot.Looks.Count, Is.EqualTo(351));
        }

        /// <summary>
        /// 보드 씬이 실행 중에 만든 메시·재질·텍스처(DontSave = DontUnloadUnusedAsset이라 씬을 내려도 안 지워진다)는 컴포넌트가 지워질 때 함께 지운다(M4-2 검토 반영).
        /// 씬을 세 번 열어도 수가 늘지 않는다 — 전에는 FixtureIcons(메시·재질 58씩)·BoardPieces(메시 6·재질 12·텍스처 12)가 열 때마다 남았다.
        /// </summary>
        [UnityTest]
        public IEnumerator M4_보드씬을다시열어도만든메시재질이남지않는다()
        {
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            yield return null;
            int first = Made(out string firstText);
            for (int i = 0; i < 2; i++)
            {
                yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
                yield return null;
                yield return null;
            }
            int third = Made(out string thirdText);
            Assert.Greater(first, 0, "보드가 만든 것이 없다");
            Assert.AreEqual(first, third, $"씬을 다시 열 때마다 남는다: 처음 {firstText} → 세 번째 {thirdText}");
        }

        /// <summary>
        /// 한 줄 스모크(plan §3 M4-3 조건 ①): 시험 하나가 씬을 한 번 열고 끊지 않고 지난다 — 타이틀 → 혼자 하기 → 셋업 → 보드 → 찾아보기 → 조우 창 → 야생 배틀 → 포획 →
        /// 1막 조무래기 배틀 → 저장 → 이어하기. 에러 로그 0.
        /// 포획은 볼을 얻는 길(제작, M5)이 없어 볼을 쥐여 준 판(<see cref="SoloTrail.Catch"/> — 손본 판은 저장하지 않는다)으로 지나고, 이어서 같은 화면 뿌리에서 타이틀로 나가
        /// 실제 판(<see cref="SoloTrail.Defeat"/>의 시드 — [게임 시작]부터 화면으로)을 찾아보기 → 조우 → 격파 → 조무래기 배틀 → 자동 저장 → 게임 메뉴로 나가기 → 타이틀 「이어하기」 → 저장 목록 [이어하기]까지 몬다.
        /// 걸음마다의 자세한 단정은 조각 시험(<c>EncounterFlowTests</c>·<c>BattleFlowTests</c>·<c>SaveResumeFlowTests</c>)이 본다 — 여기는 역마다 닿았는지와 에러 로그만. 세이브는 임시 폴더.
        /// </summary>
        [UnityTest]
        public IEnumerator M4_타이틀에서솔로셋업보드찾아보기조우배틀포획조무래기배틀저장이어하기를한번에지난다_에러로그0()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            string root = Path.Combine(Application.temporaryCachePath, "sinnoh-smoke-test-" + System.Guid.NewGuid().ToString("N"));
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
                boot.PointerFromMouse = false;
                var stage = boot.Root.Stage;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                // [게임 시작]이 고르는 시드를 찾아 둔 판의 것으로(게임 시드 → AI 시드 순으로 읽는다) — 실제 판도 끝이 정해져 있게
                int asked = 0;
                boot.Entropy = () => asked++ % 2 == 0 ? SoloTrail.Defeat.Seed : SoloTrail.AiSeed;
                boot.UseSaves(root, () => 1794000000000);
                var flow = boot.Flow;
                var events = new List<GameEvent>();
                flow.EventsSeen += events.AddRange;

                // ── ① 타이틀 → 혼자 하기 → 셋업 → 보드 → 찾아보기 → 조우 → 야생 배틀 → 포획(볼을 쥐여 준 판) ──
                var title = (TitleScreen)stage.Current;
                Assert.That(title.Activate(Row(title, TitleScreen.Solo)), Is.True, "혼자 하기");
                yield return null;
                Assert.That(stage.Current, Is.InstanceOf<SoloScreen>());
                var pilot = FlowPilot.Start(flow, SoloTrail.Catch);
                yield return null;
                Assert.That(flow.OnBoard, Is.True, "셋업을 끝내 보드에 있다");
                Assert.That(flow.SaveId, Is.Null, "손본 판은 저장하지 않는다");
                bool searched = false, encounter = false, battle = false;
                for (int i = 0; i < 4000; i++)
                {
                    searched |= flow.Client.Choice(ChoiceKind.EncounterPick) != null;
                    encounter |= stage.Current is EncounterScreen;
                    battle |= stage.Current is BattleScreen;
                    if (events.Exists(e => e is BattleEnded b && b.Result == BattleResult.Captured) && flow.Battle == null && flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null) break;
                    if (!FlowPilot.Step(flow, pilot)) flow.Skip(500);
                    yield return null;
                }
                Assert.That(searched, Is.True, "[찾아보기]");
                Assert.That(encounter, Is.True, "조우 창");
                Assert.That(battle, Is.True, "배틀 화면");
                Assert.That(events.Exists(e => e is CaptureAttempt c && c.Success), Is.True, "포획");
                Assert.That(events.Exists(e => e is PokemonGained), Is.True, "잡은 포켓몬이 파티에");
                Assert.That(flow.InputOpen && flow.Battle == null, Is.True, "포획 뒤 보드로 돌아와 내 턴");

                // ── ② 게임 메뉴로 타이틀 → 혼자 하기 → [게임 시작] → 셋업 → 보드 → 찾아보기 → 조우 → 격파 → 1막 조무래기 배틀(실제 판 — 처음부터 저장된다) ──
                flow.OpenMenu();
                yield return null;
                ((BoardMenuScreen)stage.Current).AskQuit();
                ((BoardMenuScreen)stage.Current).Quit();
                yield return null;
                title = (TitleScreen)stage.Current;
                Assert.That(title.Ready(title.Shown[Row(title, TitleScreen.Continue)]), Is.False, "손본 판은 저장에 남지 않았다");
                Assert.That(title.Activate(Row(title, TitleScreen.Solo)), Is.True);
                yield return null;
                events.Clear();
                var trail = SoloTrail.Defeat;
                pilot = trail.Pilot();
                flow.EventsSeen += pilot.Saw;
                ((SoloScreen)stage.Current).Set(SoloScreen.GalacticAi, "basic");   // 원칙 AI로(기본값은 「강함」 — 결정 118 ②)
                ((SoloScreen)stage.Current).Set(SoloScreen.TrainerCards, "on");   // 트레이너 카드를 쓰는 판(기본값은 끔 — 결정 134)
                ((SoloScreen)stage.Current).Start();
                var village = (SetupVillageScreen)stage.Current;
                village.Pick(trail.Village);
                Assert.That(village.Confirm(), Is.True, "고향 마을");
                flow.Skip(flow.QueueLeftMs);
                var pick = (SetupPickScreen)stage.Current;
                pick.PickStarter(trail.Starter);
                pick.PickCard(0);
                Assert.That(pick.Confirm(), Is.True, "스타팅 포켓몬 · 트레이너 카드");
                yield return null;
                string id = flow.SaveId;
                Assert.That(id, Does.StartWith("solo-"), "「혼자 하기」로 시작한 판은 처음부터 저장된다");
                Assert.That(flow.OnBoard, Is.True);
                searched = encounter = battle = false;
                for (int i = 0; i < 6000; i++)
                {
                    searched |= flow.Client.Choice(ChoiceKind.EncounterPick) != null;
                    encounter |= stage.Current is EncounterScreen;
                    battle |= stage.Current is BattleScreen;
                    if (events.FindAll(e => e is BattleEnded).Count == 2 && flow.Battle == null && flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null) break;
                    if (!FlowPilot.Step(flow, pilot)) flow.Skip(500);
                    yield return null;
                }
                Assert.That(searched && encounter && battle, Is.True, "찾아보기 · 조우 창 · 배틀 화면");
                var kinds = events.FindAll(e => e is BattleStarted).ConvertAll(e => ((BattleStarted)e).Kind);
                Assert.That(kinds, Is.EqualTo(new List<BattleKind> { BattleKind.Wild, BattleKind.Galactic }), "야생 배틀 → 1막 조무래기 배틀");
                Assert.That(events.FindAll(e => e is BattleEnded).Count, Is.EqualTo(2), "두 배틀이 끝났다");
                Assert.That(flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null, Is.True, "조무래기 배틀 뒤 보드에서 내 턴");
                Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택은 없었다");

                // ── ③ 저장: 명령마다 로그 한 줄 · 쉬는 때의 스냅샷 → 게임 메뉴로 나간다 ──
                Assert.That(flow.SaveStatus, Is.EqualTo(SaveStatus.Saved));
                Assert.That(SaveStore.ReadLog(Path.Combine(root, id)).Count, Is.EqualTo(flow.Session.CommandCount), "명령마다 로그 한 줄");
                ulong hash = flow.Session.Hash;
                int count = flow.Session.CommandCount, round = flow.Client.View.Round;
                flow.OpenMenu();
                yield return null;
                var menu = (BoardMenuScreen)stage.Current;
                Assert.That(menu.SaveText, Does.StartWith("자동 저장됨"));
                menu.AskQuit();
                menu.Quit();
                yield return null;
                Assert.That(flow.Session, Is.Null);

                // ── ④ 이어하기: 타이틀 「이어하기」 → 저장 목록 [이어하기] → 나갈 때와 같은 상태의 보드에서 한 턴 더 ──
                title = (TitleScreen)stage.Current;
                int cont = Row(title, TitleScreen.Continue);
                Assert.That(title.Ready(title.Shown[cont]), Is.True, "저장이 있으면 「이어하기」가 켜진다");
                Assert.That(title.Activate(cont), Is.True);
                yield return null;
                var list = (SaveListScreen)stage.Current;
                Assert.That(list.Entries.Count, Is.EqualTo(1), "저장은 실제 판 하나");
                Assert.That(list.Resume(id), Is.True, flow.ResumeError);
                yield return null;
                Assert.That(stage.Current, Is.Null, "저장 목록이 닫히고 보드");
                Assert.That(flow.OnBoard, Is.True);
                Assert.That(flow.Session.Rewound, Is.EqualTo(0), "쉬는 때에 나갔다");
                Assert.That(flow.Session.Hash, Is.EqualTo(hash), "나갈 때와 같은 상태");
                Assert.That(flow.Session.CommandCount, Is.EqualTo(count));
                Assert.That(flow.Client.View.Round, Is.EqualTo(round));
                for (int i = 0; i < 4; i++) yield return null;
                flow.Skip(1000);
                yield return null;
                Assert.That(flow.InputOpen, Is.True, "이어 받은 뒤 내 턴");
                Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "이어서 턴 종료");
                yield return null;
                for (int i = 0; i < 400 && !(flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null); i++)
                {
                    flow.Skip(250);
                    yield return null;
                }
                Assert.That(flow.Client.View.Round, Is.EqualTo(round + 1), "이어 한 판이 다음 턴으로 간다");
                Assert.That(flow.SaveId, Is.EqualTo(id), "같은 저장에 이어 쓴다");
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Flow != null) boot.Flow.OpenTitle();
                if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        static int Row(TitleScreen title, string id)
        {
            for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == id) return i;
            return -1;
        }

        // 보드 화면 코드가 이름을 붙여 만드는 것만 센다(BoardView 「Hex *」 재질·칸 메시, FixtureIcons 「Icon(s) *」, BoardPieces 「Piece *」「Pin *」「Ring」)
        static int Made(out string text)
        {
            int meshes = 0, materials = 0, textures = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
                if (m != null && (m.name.StartsWith("Icons ") || m.name.StartsWith("Piece ") || m.name.StartsWith("Hex"))) meshes++;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && (m.name.StartsWith("Icon ") || m.name.StartsWith("Piece ") || m.name.StartsWith("Pin ") || m.name.StartsWith("Hex "))) materials++;
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (t != null && (t.name == "Ring" || t.name.StartsWith("Pin "))) textures++;
            text = $"메시 {meshes}·재질 {materials}·텍스처 {textures}";
            return meshes + materials + textures;
        }
    }
}
