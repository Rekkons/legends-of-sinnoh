using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Rekkon.UiKit;
using Sinnoh.Ai;
using Sinnoh.Client;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// plan M9 「언어 ② 설정 · 좌석마다 · 글꼴」(결정 111 ② · plan §5 #304): S02 「언어」 줄 하나로 내 화면 글이 바로 바뀐다 — 설정 화면 · 타이틀 · 보드 HUD(기록 줄 포함)가
    /// 그 언어의 표로 다시 그려지고, 게임 메뉴의 설정에서 바꿔도 뒤의 보드 HUD가 바뀌며, 다시 만든 흐름(다음 실행)도 그 언어다.
    /// 에디터의 설정은 실행하는 동안만 쥔다(사용자 PlayerPrefs를 건드리지 않는다 — <see cref="ScreenBoot.DefaultSoundPrefs"/>). Gen 겉모양이 있어야 돈다.
    /// </summary>
    public sealed class LangFlowTests
    {
        const int W = 1920, H = 1080;
        static readonly Regex Hangul = new Regex("[가-힣]");

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
                boot = Object.FindAnyObjectByType<ScreenBoot>();
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
            var b = Object.FindAnyObjectByType<ScreenBoot>();
            if (b != null && b.Flow != null) b.Flow.Lang = "ko";
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                rt = null;
            }
        }

        // 보이는 글 가운데 한글이 든 것(요소 이름 + 글)
        List<string> HangulShown(VisualElement root)
        {
            var found = new List<string>();
            root.Query<TextElement>().ForEach(t =>
            {
                if (string.IsNullOrEmpty(t.text) || !Hangul.IsMatch(t.text)) return;
                for (var x = (VisualElement)t; x != null; x = x.parent)
                    if (x.resolvedStyle.display == DisplayStyle.None) return;
                var named = (VisualElement)t;
                while (named != null && string.IsNullOrEmpty(named.name)) named = named.parent;
                found.Add((named != null ? named.name : "?") + "「" + t.text + "」");
            });
            return found;
        }

        int TitleRow(string id)
        {
            var title = (TitleScreen)stage.Current;
            for (int i = 0; i < title.Shown.Count; i++) if (title.Shown[i].Id == id) return i;
            return -1;
        }

        [UnityTest]
        public IEnumerator M9_언어를바꾸면설정타이틀보드글이바로바뀌고게임메뉴에서도되며다시켜도남는다()
        {
            yield return Boot();
            Assert.That(boot.StringsEnJson, Is.Not.Null, "씬이 영어 화면 문자열 표(strings.en.json)를 가리키지 않는다");
            var ko = stage.TableOf("ko");
            var en = stage.TableOf("en");
            Assert.That(ko, Is.Not.Null);
            Assert.That(en, Is.Not.Null, "무대가 영어 표를 찾지 못한다(ScreenBoot → UiStage.Tables)");
            Assert.That(en.Lang, Is.EqualTo("en"));
            Assert.That(flow.Lang, Is.EqualTo("ko"), "처음(고르지 않음) = 한국어");
            Assert.That(flow.SoundPrefs.Lang, Is.Null);
            Assert.That(flow.SoundPrefs, Is.Not.InstanceOf<StoredSoundPrefs>(), "에디터는 사용자의 설정에 쓰지 않는다");

            // ── S02 「언어」 줄: 맨 위 · 값 이름은 그 언어로(어느 언어 화면에서도 같은 글 — #304) ──
            flow.OpenSettings();
            yield return null;
            var panel = stage.Root.Q("settings-panel");
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel[0].name, Is.EqualTo("settings-row-lang"), "「언어」는 맨 위 줄(목업 1안)");
            Assert.That(stage.Root.Q("settings-opt-lang-ko").Q<Label>().text, Is.EqualTo(ko["screen.settings.lang.name"]));
            Assert.That(stage.Root.Q("settings-opt-lang-en").Q<Label>().text, Is.EqualTo(en["screen.settings.lang.name"]));
            Assert.That(stage.Root.Q("settings-opt-lang-ko").ClassListContains("sn-opt-picked"));
            ((SettingsScreen)stage.Current).PickLang("en");
            yield return null;
            Assert.That(flow.Lang, Is.EqualTo("en"));
            Assert.That(stage.Strings, Is.SameAs(en));
            Assert.That(stage.Current, Is.InstanceOf<SettingsScreen>(), "설정 화면은 그대로(다시 그렸다)");
            Assert.That(stage.Root.Q("settings-head").Q<Label>().text, Is.EqualTo(en["screen.settings.title"]), "설정 화면이 바로 영어로");
            Assert.That(stage.Root.Q("settings-opt-lang-ko").Q<Label>().text, Is.EqualTo(ko["screen.settings.lang.name"]), "「한국어」는 영어 화면에서도 「한국어」");
            Assert.That(stage.Root.Q("settings-opt-lang-en").ClassListContains("sn-opt-picked"));
            Assert.That(stage.Root.Q("settings-opt-lang-ko").ClassListContains("sn-opt-picked"), Is.False);
            Assert.That(flow.SoundPrefs.Lang, Is.EqualTo("en"), "음량과 같은 곳에 쥔다");

            // ── 타이틀 ──
            flow.OpenTitle();
            yield return null;
            Assert.That(stage.Root.Q("title-item-solo").Q<Label>(className: Kit.F3).text, Is.EqualTo(en["screen.title.solo"]));
            var titleKo = HangulShown(stage.Root);
            Assert.That(titleKo, Is.Empty, "영어 타이틀에 한글 글: " + string.Join(", ", titleKo));

            // ── 보드: HUD · 기록 줄 · 좌석 이름까지 영어 ──
            Assert.That(((TitleScreen)stage.Current).Activate(TitleRow(TitleScreen.Solo)), Is.True);
            yield return null;
            FlowPilot.Start(flow, SoloTrail.Defeat);
            for (int i = 0; i < 200 && !flow.InputOpen; i++)
            {
                flow.Skip(100);
                yield return null;
            }
            Assert.That(flow.InputOpen, Is.True);
            Assert.That(stage.Root.Q<Button>("board-hud-dex").text, Is.EqualTo(en["screen.dex.open"]));
            var boardKo = HangulShown(stage.Root);
            Assert.That(boardKo, Is.Empty, "영어 보드에 한글 글: " + string.Join(", ", boardKo));

            // ── 게임 메뉴의 설정에서 한국어로: 뒤의 보드 HUD도 바로 ──
            flow.OpenMenu();
            flow.OpenMenuSettings();
            yield return null;
            var set = (SettingsScreen)stage.Current;
            Assert.That(set.InGame, Is.True);
            set.PickLang("ko");
            yield return null;
            Assert.That(stage.Current, Is.SameAs(set), "게임 중 설정 그대로");
            Assert.That(stage.Hud, Is.Not.Null, "보드 HUD 그대로");
            Assert.That(stage.Root.Q<Button>("board-hud-dex").text, Is.EqualTo(ko["screen.dex.open"]), "뒤의 보드 HUD가 바로 한국어로");
            Assert.That(stage.Root.Q("settings-head").Q<Label>().text, Is.EqualTo(ko["screen.settings.title"]));
            set.PickLang("en");
            yield return null;
            flow.CloseMenuSettings();
            flow.CloseMenu();
            yield return null;
            Assert.That(stage.Current, Is.Null);
            Assert.That(stage.Root.Q<Button>("board-hud-dex").text, Is.EqualTo(en["screen.dex.open"]));
            flow.Skip(1000);
            yield return null;
            Assert.That(flow.Session, Is.Not.Null, "판은 그대로 이어 간다");

            // ── 다시 켜도(그 설정으로 다시 만든 흐름) 영어 ──
            boot.UseSaves(null);
            Assert.That(boot.Flow, Is.Not.SameAs(flow));
            flow = boot.Flow;
            yield return null;
            Assert.That(flow.Lang, Is.EqualTo("en"), "다시 만든 흐름도 저장된 언어로");
            Assert.That(stage.Current, Is.InstanceOf<TitleScreen>());
            Assert.That(stage.Root.Q("title-item-solo").Q<Label>(className: Kit.F3).text, Is.EqualTo(en["screen.title.solo"]));
            flow.Lang = "ko";
            yield return null;
            Assert.That(stage.Root.Q("title-item-solo").Q<Label>(className: Kit.F3).text, Is.EqualTo(ko["screen.title.solo"]), "흐름에서 바로 바꿔도(설정 화면 밖) 열린 화면이 다시 그려진다");
            flow.Lang = "fr";
            Assert.That(flow.Lang, Is.EqualTo("ko"), "표가 없는 언어는 받지 않는다");
            Assert.That(flow.SoundPrefs.Lang, Is.EqualTo("ko"));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // 결정 146 ④ [가정]: 저장한 언어가 없으면 OS 언어 — 한국어만 ko, 그 밖은 en. 저장한 값이 먼저, 모르는 저장값은 고르지 않은 것과 같다
        [Test]
        public void M10_저장한언어가없으면OS언어로고르고한국어밖은영어()
        {
            Assert.That(ScreenBoot.LangOf(SystemLanguage.Korean, null), Is.EqualTo("ko"));
            Assert.That(ScreenBoot.LangOf(SystemLanguage.English, null), Is.EqualTo("en"));
            Assert.That(ScreenBoot.LangOf(SystemLanguage.Japanese, null), Is.EqualTo("en"), "표가 없는 언어는 영어");
            Assert.That(ScreenBoot.LangOf(SystemLanguage.Unknown, null), Is.EqualTo("en"));
            Assert.That(ScreenBoot.LangOf(SystemLanguage.English, "ko"), Is.EqualTo("ko"), "저장한 언어가 먼저");
            Assert.That(ScreenBoot.LangOf(SystemLanguage.Korean, "en"), Is.EqualTo("en"));
            Assert.That(ScreenBoot.LangOf(SystemLanguage.Korean, "fr"), Is.EqualTo("ko"), "모르는 저장값 = 고르지 않음");
            Assert.That(ScreenBoot.LangOf(SystemLanguage.German, ""), Is.EqualTo("en"));
        }
    }
}
