using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 새 판 알림(결정 148 ① — 자동 업데이트 1단계)을 화면 흐름으로: 판 정보가 없으면 알림이 없고, 서명이 틀리거나 꼴이 아닌 글은 조용히 넘기고,
    /// 확인을 마친 새 판은 열린 타이틀에 바로 「새 버전 x.y.z — [받기]」(받기 = GitHub Releases), 타이틀을 다시 열어도 남는다. 웹 손님판 타이틀에는 없다.
    /// 서명 확인 · 판 견주기는 EditMode <c>ReleaseFeedTests</c>.
    /// </summary>
    public sealed class ReleaseNoticeFlowTests
    {
        [UnityTest]
        public IEnumerator M10_새판알림_타이틀에뜨고_받기는GitHub_다시열어도남고_틀린글은조용히_웹손님판은없음()
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
                boot.PointerFromMouse = false;
                // 창 크기 대신 RT(1280×720 — 720 등급): 시험 실행기의 창 크기와 상관없이 자리를 본다
                rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;
                var flow = boot.Flow;
                var opened = new List<string>();
                flow.OpenUrl = opened.Add;
                var root = flow.Stage.Root;

                flow.OpenTitle(TitleScreen.Solo);
                var title = (TitleScreen)flow.Stage.Current;
                Assert.That(title.UpdateShown, Is.False, "판 정보를 받지 않으면 알림 없음(에디터는 읽지 않는다)");
                Assert.That(flow.NewVersion, Is.Null);
                Assert.That(root.Q<Label>("title-version").text, Is.EqualTo("v" + Application.version), "아래 띠의 판 번호 = Application.version(버그 신고 · 접속 인사와 같은 값)");

                // 틀린 글(오프라인 페이지 · 다른 키의 서명 · 빈 글)은 조용히 넘긴다
                foreach (var junk in new[] { "<html>offline</html>", "", "{\"format\":1,\"manifest\":\"{\\\"app\\\":\\\"sinnoh\\\",\\\"version\\\":\\\"99.0.0\\\"}\",\"signature\":\"" + new string('A', 86) + "==\"}" })
                    flow.TakeRelease(junk);
                Assert.That(title.UpdateShown, Is.False, "서명이 맞지 않으면 알리지 않는다");
                Assert.That(flow.NewVersion, Is.Null);

                // 확인을 마친 새 판: 열린 타이틀에 바로
                flow.ShowRelease("99.0.0");
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(title.UpdateShown, Is.True);
                Assert.That(root.Q<Label>("title-update-text").text, Does.Contain("99.0.0"));
                var box = root.Q("title-update").worldBound;
                Assert.That(root.worldBound.Contains(box.min) && root.worldBound.Contains(box.max - Vector2.one), Is.True, "알림이 화면 안: " + box);
                foreach (var other in new[] { "title-menu", "title-desc", "title-foot" })
                    Assert.That(box.Overlaps(root.Q(other).worldBound), Is.False, "알림이 " + other + "을(를) 덮는다");
                Assert.That(root.Q<Button>("title-update-get"), Is.Not.Null, "[받기]");
                Assert.That(title.PressUpdate(), Is.True);
                Assert.That(opened, Is.EqualTo(new[] { ReleaseFeed.Page }), "[받기] = GitHub Releases 페이지(결정 150)");

                // 타이틀을 다시 열어도(설정 → 뒤로 · 판을 떠남) 남는다
                flow.OpenSettings();
                flow.OpenTitle();
                Assert.That(((TitleScreen)flow.Stage.Current).UpdateShown, Is.True, "다시 연 타이틀에도");

                // 웹 손님판 타이틀에는 없다(방장이 웹 손님판을 내준다)
                var web = new TitleScreen(web: true, update: "99.0.0", download: () => opened.Add("web"));
                flow.Stage.Open(web);
                web.ShowUpdate("99.0.0", () => opened.Add("web"));
                Assert.That(web.UpdateShown, Is.False);
                Assert.That(root.Q("title-update"), Is.Null);
                Assert.That(web.PressUpdate(), Is.False);
                Assert.That(opened.Count, Is.EqualTo(1));
                flow.OpenTitle();
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
            }
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
