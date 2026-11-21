using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Rekkon.UiKit;
using Sinnoh.Client;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>화면 뿌리와 S01 타이틀(plan §4 「화면 뿌리·타이틀」, 04 §1·§9-3·§8-6). Gen 겉모양(글꼴·창틀)이 있어야 돈다.</summary>
    public sealed class TitleScreenTests
    {
        [UnityTest]
        public IEnumerator M4_보드씬을열면타이틀이뜨고에러로그0()
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
                Assert.That(boot.Theme, Is.Not.Null, "씬이 실행용 테마를 가리키지 않는다");
                Assert.That(boot.BuildShaders.Count, Is.EqualTo(ScreenProbe.FoundShaders.Length));
                for (int i = 0; i < ScreenProbe.FoundShaders.Length; i++)
                    Assert.That(boot.BuildShaders[i] != null ? boot.BuildShaders[i].name : null, Is.EqualTo(ScreenProbe.FoundShaders[i]), "씬이 가리켜야 플레이어 빌드에 들어간다");
                Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                var root = boot.Root;
                var stage = root.Stage;
                Assert.That(stage.Root, Is.Not.Null, "패널이 뿌리 요소를 주지 않았다");
                Assert.That(root.Theme, Is.SameAs(boot.Theme));

                // 창 크기 대신 RT 크기로: 등급 사이 창은 다 들어가는 가장 큰 등급, 패널은 창 전체를 덮는다
                rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(stage.Grade, Is.EqualTo(new UiGrade(720, 1)));
                Assert.That(stage.Root.layout.width, Is.EqualTo(1600f).Within(0.01f));
                Assert.That(stage.Root.layout.height, Is.EqualTo(900f).Within(0.01f));

                Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
                var title = (TitleScreen)stage.Current;
                var page = stage.Root.Q("title");
                Assert.That(page, Is.Not.Null);
                Assert.That(page.layout.width, Is.EqualTo(1600f).Within(0.01f), "화면 요소가 패널을 덮는다");
                Assert.That(title.Shown.Count, Is.EqualTo(10), "「튜토리얼」 줄(M6.5) · 「포켓몬 도감」 줄(결정 105 ③) · 「룰 설명서」 줄(M9 S43)까지 열");
                foreach (var item in title.Shown) Assert.That(stage.Root.Q("title-item-" + item.Id), Is.Not.Null, item.Id);
                Assert.That(title.Shown[title.Picked].Id, Is.EqualTo(TitleScreen.Solo));
                var foot = stage.Root.Q("title-foot").worldBound;
                Assert.That(foot.yMax, Is.EqualTo(900f).Within(0.01f), "아래 띠는 창 아래에 붙는다");
                Assert.That(foot.width, Is.EqualTo(1600f).Within(0.01f));
                Assert.That(stage.Root.Q("title-menu").worldBound.x, Is.EqualTo(60f).Within(0.01f), "720 토큰 벌");
                Assert.That(stage.Root.Q("title-menu").worldBound.xMax, Is.EqualTo(TitleScreen.MenuRight).Within(0.01f), "TitleScreen.MenuRight = USS의 메뉴 판 오른쪽 변");
                var cam = Camera.main;
                var rig = cam.GetComponent<BoardCameraRig>();
                Assert.That(rig.Target, Is.EqualTo(TitleScreen.BoardTarget(1600, 900)), "타이틀 뒤 보드 자리");
                Assert.That(BoardLeftEdge(cam, 1600, 900), Is.GreaterThanOrEqualTo(TitleScreen.MenuRight), "16:9: 보드가 메뉴 판 밑으로 들어가지 않는다");
                var pixels = new Texture2D(1600, 900, TextureFormat.RGBA32, false);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                RenderTexture.active = prev;
                int drawn = 0;
                foreach (var p in pixels.GetPixels32()) if (p.a > 0) drawn++;
                Object.Destroy(pixels);
                Assert.That(drawn, Is.GreaterThan(50000), "패널이 그려졌다(메뉴 판·설명 창·아래 띠)");
                Assert.That(drawn, Is.LessThan(1600 * 900 / 2), "바탕은 투명 — 보드가 보인다");

                // 창이 커지면 다시 고른다: 2560×1440 = 720 토큰 × 배율 2
                rt.Release();
                Object.Destroy(rt);
                rt = new RenderTexture(2560, 1440, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(stage.Grade, Is.EqualTo(new UiGrade(720, 2)));
                Assert.That(stage.Root.layout.width, Is.EqualTo(1280f).Within(0.01f));
                rt.Release();
                Object.Destroy(rt);
                // 16:9보다 좁은 창(16:10): 보드를 오른쪽으로 밀어 메뉴 판을 피하고, 오른쪽 끝은 창 안에 남는다
                rt = new RenderTexture(1920, 1200, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(stage.Grade, Is.EqualTo(new UiGrade(1080, 1)));
                Assert.That(TitleScreen.BoardShiftPx(1920, 1200), Is.GreaterThan(TitleScreen.BoardShiftPx(1920, 1080)));
                Assert.That(rig.Target, Is.EqualTo(TitleScreen.BoardTarget(1920, 1200)), "창 크기가 바뀌면 보드 자리도 다시 놓는다");
                Assert.That(BoardLeftEdge(cam, 1920, 1200), Is.GreaterThanOrEqualTo(stage.Root.Q("title-menu").worldBound.xMax), "16:10: 보드가 메뉴 판 밑으로 들어가지 않는다");
                Assert.That(BoardLeftEdge(cam, 1920, 1200, fromRight: true), Is.LessThan(1920), "보드 오른쪽 끝이 창 안");
                Assert.That(TitleScreen.BoardShiftPx(3440, 1440), Is.EqualTo(0f), "넓은 창은 밀지 않는다");
                rt.Release();
                Object.Destroy(rt);
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(stage.Grade, Is.EqualTo(new UiGrade(1080, 1)));
                Assert.That(stage.Root.Q("title-menu").worldBound.x, Is.EqualTo(90f).Within(0.01f), "1080 토큰 벌로 바뀐다");

                // 화면 뿌리를 껐다 켜면 패널이 뿌리를 다시 준다(같은 뿌리일 수도 있다) — 화면·툴팁이 겹쳐 쌓이지 않고, 열린 화면은 닫혔다가 다시 만들어진다
                var counted = new Counted();
                stage.Open(counted);
                yield return null;
                Assert.That((counted.Builds, counted.Closes), Is.EqualTo((1, 0)));
                root.gameObject.SetActive(false);
                yield return null;
                root.gameObject.SetActive(true);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(stage.Root.Query("counted").ToList().Count, Is.EqualTo(1), "껐다 켠 뒤 화면 요소 하나");
                Assert.That(stage.Root.Query("kit-tip").ToList().Count, Is.EqualTo(1), "껐다 켠 뒤 툴팁 하나");
                Assert.That(counted.Builds - counted.Closes, Is.EqualTo(1), "다시 만들기 전에 닫는다(Build와 Close는 짝)");
                Assert.That(counted.Builds, Is.GreaterThan(1), "껐다 켜면 패널이 뿌리를 다시 준다");
                int builds = counted.Builds;
                stage.Bind(stage.Root, stage.Grade);
                Assert.That(stage.Root.Query("counted").ToList().Count, Is.EqualTo(1), "같은 뿌리를 다시 받아도 화면 요소 하나");
                Assert.That(stage.Root.Query("kit-tip").ToList().Count, Is.EqualTo(1));
                Assert.That(stage.Root.IndexOf(stage.Tips.Element), Is.GreaterThan(stage.Root.IndexOf(stage.Root.Q("counted"))), "툴팁이 화면 위");
                Assert.That((counted.Builds, counted.Closes), Is.EqualTo((builds + 1, builds)));
                Assert.That(stage.Grade, Is.EqualTo(new UiGrade(1080, 1)), "껐다 켜도 등급 그대로");

                // 밑층(UiStage.Under — 보드 이펙트 판): 판이 먼저 떠 있는 데에 처음 들어와도(이어 받은 판) 맨 아래고, 그 뒤 여는 화면은 판 위다. 뿌리를 다시 받아도 그대로
                {
                    var hudScreen = new Counted { Label = "counted-hud" };
                    var under = new VisualElement { name = "counted-under" };
                    stage.Shut();
                    stage.OpenHud(hudScreen);
                    stage.SetUnder(under);
                    stage.Open(counted);
                    int[] Order() => new[] { stage.Root.IndexOf(under), stage.Root.IndexOf(stage.Root.Q("counted-hud")), stage.Root.IndexOf(stage.Root.Q("counted")), stage.Root.IndexOf(stage.Tips.Element) };
                    Assert.That(Order(), Is.EqualTo(new[] { 0, 1, 2, 3 }), "밑층 → 판 → 화면 → 툴팁");
                    stage.Bind(stage.Root, stage.Grade);
                    Assert.That(Order(), Is.EqualTo(new[] { 0, 1, 2, 3 }), "뿌리를 다시 받은 뒤에도");
                    stage.ShutHud();
                    Assert.That(stage.Root.IndexOf(stage.Root.Q("counted")), Is.EqualTo(1), "판이 없으면 화면이 밑층 바로 위");
                    stage.SetUnder(null);
                    Assert.That(under.parent, Is.Null, "밑층을 뗀다");
                    Assert.That(stage.Root.IndexOf(stage.Root.Q("counted")), Is.EqualTo(0));
                }

                Assert.That(rig.InputEnabled, Is.False, "타이틀이 덮고 있는 동안 보드 입력(Q·E·휠)은 듣지 않는다");

                // 고르기·누르기: 없는 화면은 아무 일도 없고, 종료만 듣는다
                int quits = 0;
                var mine = new TitleScreen(() => quits++, web: false);
                stage.Open(mine);
                yield return null;
                Assert.That(stage.Root.Query("title").ToList().Count, Is.EqualTo(1), "화면은 한 번에 하나");
                int solo = IndexOf(mine, TitleScreen.Solo), quit = IndexOf(mine, TitleScreen.Quit), host = IndexOf(mine, TitleScreen.Host);
                mine.Pick(host);
                Assert.That(stage.Root.Q("title-item-" + TitleScreen.Host).ClassListContains("sn-title-item-picked"));
                Assert.That(stage.Root.Q("title-item-" + TitleScreen.Solo).ClassListContains("sn-title-item-picked"), Is.False);
                Assert.That(mine.Activate(solo), Is.False, "갈 곳을 받지 않은 「혼자 하기」는 아직 없는 화면이다(화면 흐름이 넣는다 — SoloFlowTests)");
                Assert.That(quits, Is.EqualTo(0));
                Assert.That(mine.Activate(quit), Is.True);
                Assert.That(quits, Is.EqualTo(1));
                var web = new TitleScreen(() => { }, web: true);
                stage.Open(web);
                Assert.That(web.Shown.Count, Is.EqualTo(5), "웹 손님판은 참가 전용 — 「참가하기」·「튜토리얼」(결정 112 Q18)·「포켓몬 도감」(결정 105 ③)·「룰 설명서」(M9 S43)·「설정」만(02 §8 [가정 M7-62])");
                Assert.That(web.Shown[0].Id, Is.EqualTo(TitleScreen.Join));
                Assert.That(web.Shown[1].Id, Is.EqualTo(TitleScreen.Tutorial));
                Assert.That(web.Shown[2].Id, Is.EqualTo(TitleScreen.Dex));
                Assert.That(web.Shown[3].Id, Is.EqualTo(TitleScreen.Rulebook));
                Assert.That(web.Shown[4].Id, Is.EqualTo(TitleScreen.Settings));

                // 설정(S02, 결정 59): 갈 곳을 받은 「설정」은 듣는다. 화면 크기 = 모니터 안에 드는 것 + 전체 화면, 누르면 바로 바뀐다(창은 고정 값으로 — 엔진의 창을 건드리지 않는다)
                Assert.That(mine.Activate(IndexOf(mine, TitleScreen.Settings)), Is.False, "갈 곳을 받지 않은 「설정」");
                int opened = 0;
                var withSettings = new TitleScreen(() => { }, web: false, settings: () => opened++);
                stage.Open(withSettings);
                Assert.That(withSettings.Activate(IndexOf(withSettings, TitleScreen.Settings)), Is.True);
                Assert.That(opened, Is.EqualTo(1));
                var window = new FixedWindow(new Vector2Int(2560, 1440), new Vector2Int(1280, 720));
                int backs = 0;
                var settings = new SettingsScreen(window, () => backs++);
                stage.Open(settings);
                yield return null;
                Assert.That(settings.Sizes, Is.EqualTo(new[] { (1280, 720), (1600, 900), (1920, 1080) }));
                Assert.That(stage.Root.Query(className: "sn-set-opt").ToList().Count, Is.EqualTo(4), "창 크기 셋 + 전체 화면");
                Assert.That(stage.Root.Q("settings-opt-window-2560x1440"), Is.Null, "모니터와 같거나 큰 크기는 없다");
                Assert.That(settings.Picked, Is.EqualTo("1280x720"));
                Assert.That(stage.Root.Q("settings-opt-window-1280x720").ClassListContains("sn-opt-picked"));
                Assert.That(stage.Root.Q("settings-col").worldBound.xMax, Is.EqualTo(settings.PanelRight * 1.5f).Within(0.01f), "PanelRight = USS의 판 오른쪽 변(1080 벌)");
                Assert.That(settings.Pick(1600, 900), Is.True);
                Assert.That((window.Size, window.Fullscreen), Is.EqualTo((new Vector2Int(1600, 900), false)));
                Assert.That(stage.Root.Q("settings-opt-window-1600x900").ClassListContains("sn-opt-picked"));
                Assert.That(stage.Root.Q("settings-opt-window-1280x720").ClassListContains("sn-opt-picked"), Is.False);
                Assert.That(stage.Root.Q("settings-row-piece"), Is.Null, "흐름 없이 연 설정 화면에는 「말 그림」 줄이 없다");
                Assert.That(settings.Pick(3840, 2160), Is.False, "목록에 없는 크기");
                Assert.That(window.Size, Is.EqualTo(new Vector2Int(1600, 900)));
                settings.PickFull();
                Assert.That((window.Size, window.Fullscreen), Is.EqualTo((new Vector2Int(2560, 1440), true)), "전체 화면 = 모니터 크기");
                Assert.That(settings.Picked, Is.EqualTo(SettingsScreen.Full));
                Assert.That(new SettingsScreen(window, () => { }).Picked, Is.EqualTo(SettingsScreen.Full), "다시 열면 지금 상태가 골라져 있다");
                Assert.That(new SettingsScreen(new FixedWindow(new Vector2Int(1920, 1080), new Vector2Int(1902, 1008)), () => { }).Picked, Is.Null, "목록에 없는 창 크기면 고른 값 없음");
                Assert.That(backs, Is.EqualTo(0));
                stage.Open(web);

                // 유휴: 실행기에 걸린 것이 없으면 프레임을 내린다. 걸리면 처음 값으로
                yield return null;
                Assert.That(stage.Tweens.Busy, Is.EqualTo(0));
                Assert.That(root.Idle, Is.True);   // 프레임 값(30)은 창 없는 플레이어에서 본다(ScreenProbe) — 시험 실행기가 targetFrameRate를 되돌린다
                var part = new Part();
                stage.Tweens.Play(part, 60000);
                yield return null;
                yield return null;
                Assert.That(root.Idle, Is.False);
                Assert.That(Application.targetFrameRate, Is.Not.EqualTo(ScreenRoot.IdleFps));
                Assert.That(part.Seeks, Is.GreaterThan(1), "프레임마다 실행기가 돈다");
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
            }
            // 화면 뿌리가 없어지면 프레임 설정이 처음 값으로 돌아온다
            Object.Destroy(Object.FindAnyObjectByType<ScreenRoot>().gameObject);
            yield return null;
            Assert.That(Application.targetFrameRate, Is.Not.EqualTo(ScreenRoot.IdleFps));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        static int IndexOf(TitleScreen title, string id)
        {
            for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == id) return i;
            return -1;
        }

        /// <summary>보드 카메라를 이 크기로 그려, 배경색이 아닌 픽셀이 있는 가장 왼쪽 열(fromRight면 가장 오른쪽 열 + 1)을 물리 px로 돌려준다.</summary>
        internal static int BoardLeftEdge(Camera cam, int w, int h, bool fromRight = false)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            OffscreenShot.Render(cam, rt, tex);
            OffscreenShot.Render(cam, rt, tex);
            var px = tex.GetPixels32();
            Color32 bg = cam.backgroundColor;
            int left = w, right = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (Mathf.Abs(px[i].r - bg.r) + Mathf.Abs(px[i].g - bg.g) + Mathf.Abs(px[i].b - bg.b) <= 12) continue;
                int x = i % w;
                if (x < left) left = x;
                if (x >= right) right = x + 1;
            }
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(tex);
            return fromRight ? right : left;
        }

        sealed class Counted : IUiScreen
        {
            public int Builds, Closes;
            public string Label = "counted";
            public string Name => Label;
            public VisualElement Build(UiStage stage) { Builds++; return new VisualElement(); }
            public void Close() => Closes++;
        }

        sealed class Part : ISeekable
        {
            public int Seeks;
            public void Seek(int ms) => Seeks++;
        }
    }
}
