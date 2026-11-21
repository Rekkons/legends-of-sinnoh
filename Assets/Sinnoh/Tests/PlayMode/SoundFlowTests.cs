using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 소리 재생 층(plan §4 「소리 뼈대」, 결정 66 ④, 04 §8-4): 타이틀 곡 · 즉시 소리(같은 키가 한 프레임에 겹치면 한 번) · S02 음량 줄 → 음원 음량 ·
    /// 화면 대기열의 소리 단서 → 재생 층(그 항목·시각이 적힌다). 소리가 실제로 나는지는 사람이 듣는다 — 여기서는 음원의 클립·음량과 낸 소리 기록만 본다.
    /// 음량 설정은 에디터에서 실행하는 동안만 쥔다(사용자 값을 건드리지 않는다 — <see cref="ScreenBoot.DefaultSoundPrefs"/>). Gen 소리 표(build.mjs audio·se)가 있어야 돈다.
    /// </summary>
    public sealed class SoundFlowTests
    {
        static List<SoundHeard> Since(SoundPlayer sound, int at)
        {
            var list = new List<SoundHeard>();
            for (int i = at; i < sound.Heard.Count; i++) list.Add(sound.Heard[i]);
            return list;
        }

        [UnityTest]
        public IEnumerator M4_타이틀곡과즉시소리와음량줄과소리단서가재생층에닿는다()
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
                var sound = boot.Sound;
                Assert.That(sound, Is.Not.Null, "재생 층");
                if (sound.Music.Count == 0 || sound.Effects.Count == 0) Assert.Ignore("M4 Gen 없음: 소리 표(build.mjs audio·se) 먼저");
                var flow = boot.Flow;
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                Assert.That(flow.SoundPrefs, Is.Not.InstanceOf<StoredSoundPrefs>(), "에디터는 사용자의 음량 설정에 쓰지 않는다");
                for (int i = 0; i < 4; i++) yield return null;

                // ── 타이틀 곡 ──
                Assert.That(sound.Scene, Is.EqualTo(MusicScene.Title));
                Assert.That(sound.MusicSource.clip, Is.Not.Null, "타이틀 곡 클립");
                Assert.That(sound.MusicSource.clip.name, Is.EqualTo(System.IO.Path.GetFileNameWithoutExtension(sound.Music.File(MusicScene.Title))));
                Assert.That(sound.MusicSource.loop, Is.True);
                // M9 소리 ②: 곡 파일 = 반복 클립, 앞부분이 있는 곡이면 앞부분 음원에 앞부분 클립(그 뒤에 반복 클립을 예약으로 잇는다)
                string titleIntro = sound.Music.Intro(MusicScene.Title);
                Assert.That(sound.IntroSource.clip != null ? sound.IntroSource.clip.name : null, Is.EqualTo(titleIntro != null ? System.IO.Path.GetFileNameWithoutExtension(titleIntro) : null), "앞부분 클립");
                Assert.That(sound.IntroSource.loop, Is.False, "앞부분은 한 번");
                Assert.That(sound.Music.Once(MusicScene.GameOver), Is.True, "게임 끝 곡은 한 번 틀고 끝(「The End」)");

                // ── 즉시 소리: 같은 프레임 같은 키는 한 번, 다른 키는 낸다 ──
                int at = sound.Heard.Count;
                flow.PlaySound(SoundKeys.Decide);
                flow.PlaySound(SoundKeys.Decide);
                flow.PlaySound(SoundKeys.Select);
                var now = Since(sound, at).FindAll(h => h.Kind == "se");
                Assert.That(now.ConvertAll(h => h.Key), Is.EqualTo(new List<string> { SoundKeys.Decide, SoundKeys.Select }), "같은 프레임의 같은 키는 한 번");
                Assert.That(now[0].From, Is.EqualTo("now"));
                Assert.That(now[0].Clip, Is.EqualTo(SoundKeys.Decide), "효과음 클립(Gen/Audio/Se)");
                var decide = sound.Effects.Find(SoundKeys.Decide);
                Assert.That(now[0].Volume, Is.EqualTo(SoundMix.Effect(flow.SoundPrefs.Se, decide.Group, decide.Db)).Within(1e-6f));
                yield return null;
                at = sound.Heard.Count;
                flow.PlaySound(SoundKeys.Decide);
                Assert.That(sound.Heard.Count, Is.EqualTo(at + 1), "다음 프레임에는 다시 낸다");

                // ── S02 음량 줄: 배경음악 끔 → 곡 음원 음량 0, 효과음을 바꾸면 그 음량으로 확정 소리 ──
                flow.OpenSettings();
                yield return null;
                var set = (SettingsScreen)boot.Root.Stage.Current;
                Assert.That(boot.Root.Stage.Root.Q("settings-row-bgm"), Is.Not.Null);
                // 슬라이더(결정 70 ⑭): 줄마다 하나 — 값 = 흐름의 음량, 오른쪽 글 = 그 값
                Assert.That(set.Slider("se"), Is.SameAs(boot.Root.Stage.Root.Q("settings-slider-se")));
                Assert.That((set.Slider("bgm").Max, set.Slider("bgm").Value, set.Slider("se").Value), Is.EqualTo((SoundMix.MaxStep, flow.SoundPrefs.Bgm, flow.SoundPrefs.Se)));
                Assert.That(boot.Root.Stage.Root.Q<Label>("settings-val-bgm").text, Is.EqualTo(flow.SoundPrefs.Bgm.ToString()));
                Assert.That(sound.Scene, Is.EqualTo(MusicScene.Title), "설정은 타이틀 곡 그대로");
                int bgmWas = flow.SoundPrefs.Bgm, seWas = flow.SoundPrefs.Se;
                sound.Tick(SoundMix.CrossfadeMs.Value);   // 교차 페이드를 끝낸다(재생 층의 시계는 실시각 — 흐름의 Skip은 닿지 않는다)
                Assert.That(sound.MusicSource.volume, Is.EqualTo(SoundMix.Music(bgmWas, 0)).Within(1e-4f), "페이드가 끝난 곡 = 단계 음량");
                set.PickBgm(0);
                yield return null;
                Assert.That(sound.MusicSource.volume, Is.EqualTo(0f), "배경음악 끔");
                Assert.That((set.Slider("bgm").Value, boot.Root.Stage.Root.Q<Label>("settings-val-bgm").text), Is.EqualTo((0, "끔")));
                // 슬라이더를 움직이면(누르거나 끈 것과 같은 길 — KitSlider.Set) 그 음량이 된다
                set.Slider("bgm").Set(7);
                Assert.That((flow.SoundPrefs.Bgm, boot.Root.Stage.Root.Q<Label>("settings-val-bgm").text), Is.EqualTo((7, "7")));
                yield return null;
                Assert.That(sound.MusicSource.volume, Is.EqualTo(SoundMix.Music(7, 0)).Within(1e-4f), "슬라이더 7");
                // 홈을 누른 자리의 값: 손잡이의 가운데를 누르면 그 값, 오른쪽 끝은 가장 큰 값
                var bar = set.Slider("bgm");
                Assert.That(bar.contentRect.width, Is.GreaterThan(0f), "슬라이더가 배치됐다");
                Assert.That(bar.ValueAt(bar.Q("kit-slider-knob").resolvedStyle.left + bar.Q("kit-slider-knob").resolvedStyle.width * 0.5f), Is.EqualTo(7));
                Assert.That((bar.ValueAt(bar.contentRect.width), bar.ValueAt(0f)), Is.EqualTo((SoundMix.MaxStep, 0)));
                set.PickBgm(0);
                at = sound.Heard.Count;
                set.PickSe(1);
                Assert.That(sound.Heard[sound.Heard.Count - 1].Key, Is.EqualTo(SoundKeys.Decide));
                Assert.That(sound.Heard[sound.Heard.Count - 1].Volume, Is.EqualTo(SoundMix.Effect(1, decide.Group, decide.Db)).Within(1e-6f), "고른 음량으로 확정 소리");
                set.PickBgm(bgmWas);
                yield return null;
                set.PickSe(seWas);
                // 「연출」 줄(P17): 고른 값이 흐름의 연출 속도(음량과 같은 곳 — 에디터는 실행하는 동안만)
                Assert.That(boot.Root.Stage.Root.Q("settings-row-fx"), Is.Not.Null);
                Assert.That(set.Fx, Is.EqualTo(FxSpeed.Normal), "기본은 보통");
                Assert.That(boot.Root.Stage.Root.Q("settings-opt-fx-normal").ClassListContains("sn-opt-picked"), Is.True);
                set.PickFx(FxSpeed.Fast);
                Assert.That(flow.FxSpeed, Is.EqualTo(FxSpeed.Fast));
                Assert.That(flow.SoundPrefs.Fx, Is.EqualTo(FxSpeed.Fast));
                Assert.That(boot.Root.Stage.Root.Q("settings-opt-fx-fast").ClassListContains("sn-opt-picked"), Is.True);
                Assert.That(boot.Root.Stage.Root.Q("settings-opt-fx-normal").ClassListContains("sn-opt-picked"), Is.False);
                set.PickFx(FxSpeed.Normal);
                // 「차례 알림」 줄(결정 112 Q17): 기본 켬 · 끄면 음량과 같은 곳에 남는다(에디터는 실행하는 동안만)
                Assert.That(boot.Root.Stage.Root.Q("settings-row-alert"), Is.Not.Null);
                Assert.That(set.Alert, Is.True, "기본은 켬");
                Assert.That(boot.Root.Stage.Root.Q("settings-opt-alert-on").ClassListContains("sn-opt-picked"), Is.True);
                set.PickAlert(false);
                Assert.That(flow.SoundPrefs.Alert, Is.False);
                Assert.That(boot.Root.Stage.Root.Q("settings-opt-alert-off").ClassListContains("sn-opt-picked"), Is.True);
                Assert.That(boot.Root.Stage.Root.Q("settings-opt-alert-on").ClassListContains("sn-opt-picked"), Is.False);
                set.PickAlert(true);

                // ── 게임에 들어가면 보드의 곡, 화면 대기열의 소리 단서가 그 항목·시각과 함께 재생 층에 닿는다 ──
                FlowPilot.Start(flow, SoloTrail.Defeat);
                yield return null;
                Assert.That(flow.OnBoard, Is.True);
                flow.Skip(flow.OpeningMs);
                yield return null;
                var board = flow.Music();
                Assert.That(board == MusicScene.Prologue || board.ToString().StartsWith("Field", System.StringComparison.Ordinal), Is.True, "보드의 곡: " + board);
                Assert.That(sound.Scene, Is.EqualTo(board));
                var cue = new FxEntry { Seq = 900001, QueueMs = 400 };
                cue.AddSound(100, SoundKeys.Land);
                cue.AddSound(100, SoundKeys.Land);
                cue.AddSound(250, SoundKeys.Hit);
                flow.Skip(flow.QueueLeftMs);   // 앞 항목(내 턴 컷신 등)을 다 보낸다
                yield return null;
                Assert.That(flow.Fx.Sequencer.IsEmpty, Is.True, "대기열이 비었다");
                at = sound.Heard.Count;
                flow.Fx.Enqueue(new[] { cue });
                boot.Root.Stage.Tweens.Advance(500);   // 연출 시각을 보낸다(건너뛰기 Skip은 무음 — 아래)
                yield return null;
                var cues = Since(sound, at).FindAll(h => h.Kind == "se");
                Assert.That(cues.ConvertAll(h => h.Key + " " + h.From), Is.EqualTo(new List<string> { SoundKeys.Land + " 900001@100", SoundKeys.Hit + " 900001@250" }),
                    "단서 = 항목·항목 안 시각, 같은 프레임의 같은 키는 한 번");
                Assert.That(cues[1].Clip, Is.EqualTo(SoundKeys.Hit));
                // 건너뛰기(flow.Skip)는 무음 — 확인용으로 켜면(SoundOnSkip) 낸다
                var skipped = new FxEntry { Seq = 900002, QueueMs = 400 };
                skipped.AddSound(100, SoundKeys.Land);
                at = sound.Heard.Count;
                flow.Fx.Enqueue(new[] { skipped });
                flow.Skip(500);
                yield return null;
                Assert.That(Since(sound, at).FindAll(h => h.Kind == "se"), Is.Empty, "건너뛴 연출의 소리는 나지 않는다");
                Assert.That(flow.Fx.Sequencer.Muted, Is.False, "건너뛰기가 끝나면 무음을 푼다");
                var logged = new FxEntry { Seq = 900003, QueueMs = 400 };
                logged.AddSound(100, SoundKeys.Land);
                flow.SoundOnSkip = true;
                flow.Fx.Enqueue(new[] { logged });
                flow.Skip(500);
                flow.SoundOnSkip = false;
                yield return null;
                Assert.That(Since(sound, at).FindAll(h => h.Kind == "se").ConvertAll(h => h.From), Is.EqualTo(new List<string> { "900003@100" }));

                // 「연출 끄기」(04 §8-4): 갱신 때 대표 소리 하나(받은 순서로 첫 단서)만 재생 층에 닿고, 지나간 연출의 단서는 나지 않는다 — 하던 판의 대기열에도 바로 걸린다
                flow.FxSpeed = FxSpeed.Off;
                Assert.That(flow.Fx.Sequencer.Speed, Is.EqualTo(FxSpeed.Off));
                yield return null;
                var offed = new FxEntry { Seq = 900004, QueueMs = 400 };
                offed.AddSound(250, SoundKeys.Hit);
                offed.AddSound(100, SoundKeys.Land);
                at = sound.Heard.Count;
                flow.Fx.Enqueue(new[] { offed });
                Assert.That(Since(sound, at).FindAll(h => h.Kind == "se").ConvertAll(h => h.Key + " " + h.From), Is.EqualTo(new List<string> { SoundKeys.Land + " 900004@100" }), "받을 때 대표 소리 한 번");
                boot.Root.Stage.Tweens.Advance(16);
                yield return null;
                Assert.That(flow.Fx.Sequencer.IsEmpty, Is.True, "다음 시간에 다 보냈다");
                Assert.That(Since(sound, at).FindAll(h => h.Kind == "se").Count, Is.EqualTo(1), "지나간 연출의 단서는 나지 않는다");
                flow.FxSpeed = FxSpeed.Normal;

                // ── 타이틀로 나가면 타이틀 곡으로 교차 페이드 ──
                string boardClip = sound.MusicSource.clip != null ? sound.MusicSource.clip.name : null;
                flow.OpenTitle();
                yield return null;
                Assert.That(sound.Scene, Is.EqualTo(MusicScene.Title));
                Assert.That(sound.MusicSource.clip.name, Is.EqualTo(System.IO.Path.GetFileNameWithoutExtension(sound.Music.File(MusicScene.Title))));
                if (boardClip != sound.MusicSource.clip.name)
                    Assert.That(sound.FadingSource.clip != null ? sound.FadingSource.clip.name : null, Is.EqualTo(boardClip), "나가는 쪽 음원은 보드의 곡");

                // ── 사용자 결정 130: 방 만들기(방 로비 화면들)는 로비 곡(마박사 소개), 타이틀로 돌아오면 타이틀 곡 ──
                flow.Room.OpenCreate();
                yield return null;
                Assert.That(boot.Root.Stage.Current, Is.InstanceOf<RoomCreateScreen>());
                Assert.That(sound.Scene, Is.EqualTo(MusicScene.Lobby), "방 만들기 = 로비 곡");
                Assert.That(sound.MusicSource.clip.name, Is.EqualTo(System.IO.Path.GetFileNameWithoutExtension(sound.Music.File(MusicScene.Lobby))));
                flow.OpenTitle();
                yield return null;
                Assert.That(sound.Scene, Is.EqualTo(MusicScene.Title), "타이틀로 돌아오면 타이틀 곡");
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
            }
        }

        [UnityTest]
        public IEnumerator M4_판의연출소리는그단계의키프레임에재생층에닿는다()
        {
            // 볼을 쥐여 준 판: 셋업 → 게임 시작 → 찾아보기 → 조우 → 야생 배틀 → 포획. 화면이 단 소리 단서가 재생 층에 닿고(그 행 · 클립), 즉시 소리 단추 규칙이 맞는지 본다
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            GameFlow flow = null;
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
                Assert.That(boot != null && boot.Root != null, Is.True, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                var sound = boot.Sound;
                if (sound.Music.Count == 0 || sound.Effects.Count == 0) Assert.Ignore("M4 Gen 없음: 소리 표(build.mjs audio·se) 먼저");
                flow = boot.Flow;
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                yield return null;

                // 즉시 소리: 키트 단추는 이름으로 확정 · 취소, 꺼진 단추는 무음(04 §8-4)
                Assert.That(UiStage.ButtonSound(new Button { name = "setup-pick-confirm" }), Is.EqualTo(SoundKeys.Decide));
                Assert.That(UiStage.ButtonSound(new Button { name = "settings-back" }), Is.Null, "뒤로는 무음(결정 70 ⑯)");
                Assert.That(UiStage.ButtonSound(new Button { name = "board-menu-quit-no" }), Is.Null, "취소는 무음");
                Assert.That(UiStage.ButtonSound(new Button { name = "board-log-close" }), Is.Null, "닫기는 무음");
                Assert.That(UiStage.ButtonSound(new Button { name = "board-hud-act-search" }), Is.EqualTo(SoundKeys.Decide), "「no」가 마디가 아니면 확정");
                Assert.That(UiStage.ButtonSound(new Button { name = "board-bless-fold" }), Is.EqualTo(SoundKeys.Select), "[접기]는 확정이 아니다(결정 87 V19 ①)");
                var off = new Button { name = "battle-forfeit" };
                off.SetEnabled(false);
                Assert.That(UiStage.ButtonSound(off), Is.Null, "꺼진 단추는 무음");
                int tapAt = sound.Heard.Count;
                boot.Root.Stage.Tap(SoundKeys.Select);
                Assert.That(sound.Heard[sound.Heard.Count - 1].From, Is.EqualTo("now"), "무대에 알린 즉시 소리가 흐름을 거쳐 재생 층에");
                Assert.That(sound.Heard.Count, Is.EqualTo(tapAt + 1));

                var calls = new List<SoundCall>();
                flow.Sound += c => { if (!c.Immediate) calls.Add(c); };
                flow.SoundOnSkip = true;   // 확인용: 건너뛰는 연출의 소리도 낸다(단서 시각 로그와 같은 길)
                int heardAt = sound.Heard.Count;
                var pilot = FlowPilot.Start(flow, SoloTrail.Catch);
                bool caught = FlowPilot.Run(flow, pilot, () => flow.Battle != null && flow.BattleOver, ms => flow.Skip(ms), 500, 4000, false);
                Assert.That(caught, Is.True, "포획 결과 창까지 가지 못했다");
                Assert.That(flow.Battle.Result.ToString(), Is.EqualTo("Captured"), "볼을 쥐여 준 판은 첫 볼에 잡는다");
                var keys = calls.ConvertAll(c => c.Key);
                foreach (string key in new[] { SoundKeys.Land, SoundKeys.CardFlip, SoundKeys.BallOpen, SoundKeys.Token, SoundKeys.ActCard, SoundKeys.MyTurn, SoundKeys.Wipe,
                             SoundKeys.DiceRoll, SoundKeys.Dice, SoundKeys.BallThrow, SoundKeys.BallShake, SoundKeys.Capture })
                    Assert.That(keys, Does.Contain(key), "판 한 바퀴의 연출 소리: " + key);
                // 찾아보기 「!」는 결정 143 ⑥부터 bang이 아니라 칸에 따라 search(나무) · search_reef(산호) — 옛 기대 SoundKeys.Bang
                Assert.That(keys.Contains(SoundKeys.Search) || keys.Contains(SoundKeys.SearchReef), Is.True, "판 한 바퀴의 연출 소리: 찾아보기(search · search_reef)");
                foreach (var c in calls)
                {
                    Assert.That(FxBuilt.Drawn(c.Row) || c.Row == 80, Is.True, c.Key + ": " + c.Row + "행은 화면이 그리는 행이 아니다");
                    Assert.That(c.Seq >= -1 && c.CueMs >= 0, Is.True);
                }
                for (int i = 1; i < calls.Count; i++) Assert.That(calls[i].CueMs, Is.GreaterThanOrEqualTo(calls[i - 1].CueMs), "단서는 시퀀서 시각 순서로 난다");
                // 흔들림 3번 → 마지막 흔들림 뒤 딸깍(같은 항목) · 클립은 audio 쪽 Gen에서
                var shakes = calls.FindAll(c => c.Key == SoundKeys.BallShake);
                var click = calls.Find(c => c.Key == SoundKeys.Capture);
                Assert.That(shakes.Count, Is.EqualTo(3));
                Assert.That(click.Seq, Is.EqualTo(shakes[2].Seq));
                Assert.That(click.AtMs - shakes[2].AtMs, Is.EqualTo(Sinnoh.Rules.Session.PlayTiming.CaptureShakeMs.Value), "딸깍 = 마지막 흔들림 한 번 뒤(판정)");
                var heard = Since(sound, heardAt).FindAll(h => h.Kind == "se");
                // 울음(M9 소리 ③ — 층 Cry): 스타터 · 배틀 인트로(야생이 드러남 · 내 포켓몬 볼 열림 + CryLagMs) — 울음 표(Gen Audio/Cry)의 종 클립으로 재생 층에 닿는다
                var cryCalls = calls.FindAll(c => SoundKeys.TryCry(c.Key, out _, out _));
                Assert.That(cryCalls.Exists(c => c.Row == 30), Is.True, "스타터 울음(3행)");
                Assert.That(cryCalls.FindAll(c => c.Row == FxRows.Intro).Count, Is.GreaterThanOrEqualTo(2), "인트로: 야생 · 내 포켓몬 울음(53행)");
                var introOpen = calls.Find(c => c.Row == FxRows.Intro && c.Key == SoundKeys.BallOpen);
                Assert.That(cryCalls.Exists(c => c.Row == FxRows.Intro && c.Seq == introOpen.Seq && c.AtMs == introOpen.AtMs + SoundCues.CryLagMs), Is.True, "내 포켓몬 울음 = 볼 열림 + CryLagMs(04 §8-4 울음은 SE를 밀지 않고 뒤에)");
                var cryHeard = Since(sound, heardAt).FindAll(h => h.Kind == "cry");
                foreach (var c in cryCalls)
                {
                    SoundKeys.TryCry(c.Key, out int dex, out _);
                    var h = cryHeard.Find(x => x.Key == c.Key);
                    Assert.That(h.Key, Is.EqualTo(c.Key), c.Key + "가 재생 층에 닿지 않았다");
                    Assert.That(h.Clip, Is.EqualTo(dex.ToString()), c.Key + " 클립(Gen/Audio/Cry/" + dex + ".ogg)");
                    Assert.That(sound.ClipFor(c.Key), Is.Not.Null);
                }
                foreach (string key in new HashSet<string>(keys))
                {
                    if (SoundKeys.TryCry(key, out _, out _)) continue;
                    var h = heard.Find(x => x.Key == key);
                    Assert.That(h.Key, Is.EqualTo(key), key + "가 재생 층에 닿지 않았다");
                    Assert.That(h.Clip, Is.EqualTo(key), key + " 클립(Gen/Audio/Se/" + key + ".wav)");
                    Assert.That(h.Row, Is.Not.EqualTo(0), key + " 행이 적힌다");
                    Assert.That(sound.ClipFor(key), Is.Not.Null);
                }
                Assert.That(errors, Is.Empty, string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (flow != null)
                {
                    flow.SoundOnSkip = false;
                    flow.OpenTitle();
                }
                var b = Object.FindAnyObjectByType<ScreenBoot>();
                if (b != null && b.Root != null) b.Root.SetTarget(null);
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
            }
        }

        // 클릭 하나를 그 칸에 보낸다(창 없는 에디터의 패널에는 포인터를 넣지 못해 ClickEvent를 바로 보낸다)
        static void Click(VisualElement element)
        {
            using (var e = ClickEvent.GetPooled())
            {
                e.target = element;
                element.SendEvent(e);
            }
        }

        static int NowSelects(SoundPlayer sound, int at) => Since(sound, at).FindAll(h => h.Kind == "se" && h.From == "now" && h.Key == SoundKeys.Select).Count;

        [UnityTest]
        public IEnumerator M4_입력이닫힌고르기칸과흐린단추와꺼진효과음의더킹은소리가없다()
        {
            // plan §5 #55 「입력이 닫혔을 때 누른 것은 소리 없음」: 셋업 고르기 칸은 연출 동안 · 답을 낸 뒤 무음, 흐린 단추(sn-hud-off)도 무음, 효과음을 끄면 징글 더킹도 없다
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            GameFlow flow = null;
            int seWas = -1;
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
                Assert.That(boot != null && boot.Root != null, Is.True, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                var sound = boot.Sound;
                if (sound.Music.Count == 0 || sound.Effects.Count == 0) Assert.Ignore("M4 Gen 없음: 소리 표(build.mjs audio·se) 먼저");
                flow = boot.Flow;
                var stage = boot.Root.Stage;
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                for (int i = 0; i < 4; i++) yield return null;

                // ── 흐린 단추(켜 둔 채 sn-hud-off — 툴팁을 띄우려고 끄지 않은 단추)는 꺼진 단추처럼 무음 ──
                var grey = new Button { name = "battle-bag" };
                grey.AddToClassList("sn-hud-off");
                Assert.That(UiStage.ButtonSound(grey), Is.Null, "흐린 단추는 무음");
                Assert.That(UiStage.ButtonSound(new Button { name = "battle-bag" }), Is.EqualTo(SoundKeys.Decide));

                // ── 효과음 끔: 징글(duck)이 나지 않으니 BGM도 내려가지 않는다. 효과음이 켜져 있으면 내려간다 ──
                Assert.That(sound.Effects.Find(SoundKeys.Heal).Duck, Is.True, "heal = 징글 대역");
                seWas = flow.SoundPrefs.Se;
                int bgm = flow.SoundPrefs.Bgm;
                Assert.That(bgm, Is.GreaterThan(0));
                float level = SoundMix.Music(bgm, 0);
                flow.SoundPrefs.Se = 0;
                sound.Tick(SoundMix.CrossfadeMs.Value + 3000);   // 페이드·앞 더킹을 끝낸다
                Assert.That(sound.MusicSource.volume, Is.EqualTo(level).Within(1e-4f));
                sound.Play(new SoundCall { Key = SoundKeys.Heal, Immediate = true });
                sound.Tick(30);
                Assert.That(sound.MusicSource.volume, Is.EqualTo(level).Within(1e-4f), "효과음을 끄면 소리 없는 징글이 BGM을 내리지 않는다");
                flow.SoundPrefs.Se = SoundMix.MaxStep;
                sound.Tick(3000);
                sound.Play(new SoundCall { Key = SoundKeys.Heal, Immediate = true });
                sound.Tick(30);
                Assert.That(sound.MusicSource.volume, Is.LessThan(level - 1e-4f), "효과음이 켜져 있으면 징글 동안 BGM 더킹");
                flow.SoundPrefs.Se = seWas;
                sound.Tick(3000);

                // ── 셋업 고르기 칸: 입력이 열렸을 때만 고르기 소리 ──
                flow.StartSolo(new Sinnoh.Rules.Session.SoloSettings(), 11, 99);
                yield return null;
                yield return null;
                var village = (SetupVillageScreen)stage.Current;
                Assert.That(flow.SetupOpen, Is.True);
                int at = sound.Heard.Count;
                Click(stage.Root.Q(className: "sn-setup-item"));
                yield return null;
                Assert.That(NowSelects(sound, at), Is.EqualTo(1), "열린 마을 줄을 누르면 고르기 소리(클릭이 칸에 닿았다)");
                Assert.That(village.Picked, Is.EqualTo(0));
                Assert.That(village.Confirm(), Is.True);
                flow.Skip(Sinnoh.Rules.Session.PlayTiming.RowMs(new Sinnoh.Rules.VillagePicked(), VillageBeats.Row));   // 고향 마을 연출 → 카드가 들어오는 중
                yield return null;
                var pick = (SetupPickScreen)stage.Current;
                Assert.That(flow.SetupOpen, Is.False, "카드가 들어오는 동안");
                var starter = stage.Root.Q(className: "sn-pick-tile");
                var card = stage.Root.Q(className: "sn-pick-card");
                at = sound.Heard.Count;
                Click(starter);
                Click(card);
                yield return null;
                Assert.That(pick.Starter, Is.EqualTo(-1), "들어오는 동안에는 골라지지 않는다");
                Assert.That(NowSelects(sound, at), Is.EqualTo(0), "들어오는 동안 누른 칸은 무음");
                flow.Skip(flow.QueueLeftMs);
                yield return null;
                Assert.That(flow.SetupOpen, Is.True);
                at = sound.Heard.Count;
                Click(starter);
                yield return null;
                Assert.That(pick.Starter, Is.EqualTo(0));
                Assert.That(NowSelects(sound, at), Is.EqualTo(1), "열린 칸은 고르기 소리");
                Click(card);
                yield return null;
                Assert.That(pick.Confirm(), Is.True);
                at = sound.Heard.Count;
                Click(starter);
                Click(card);
                yield return null;
                Assert.That(NowSelects(sound, at), Is.EqualTo(0), "[확정] 뒤에 누른 칸은 무음");
                Assert.That(errors, Is.Empty, string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                if (flow != null)
                {
                    if (seWas >= 0) flow.SoundPrefs.Se = seWas;
                    flow.OpenTitle();
                }
                var b = Object.FindAnyObjectByType<ScreenBoot>();
                if (b != null && b.Root != null) b.Root.SetTarget(null);
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
            }
        }
    }
}
