using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Tutorial;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 각본 튜토리얼의 사람 클릭 경로(M6.5 완료 조건 3 · 07 §8)와 앵커 표(조건 4)의 틀 — 확장 장 시험(<c>M65_E&lt;n&gt;_클릭경로</c>)이 같이 쓴다.
    /// <list type="bullet">
    /// <item><see cref="Run"/>: 보드 씬(1920×1080 RenderTexture) → 목차의 장 고르기(<see cref="TutorialMenu.Start"/>) → 단계마다 층이 뜬 뒤(대기열이 빔) 그 단계를 사람처럼 누른다 → 끝 창.
    /// 장이 이어 여는 각본(본편 7장의 preview2 · preview3)도 같은 길로 지난다. 에러 로그 · <c>[튜토리얼]</c> 경고(앵커 못 찾음 · 대조 어긋남)가 0이어야 한다.</item>
    /// <item>단계마다: 앵커 첫 부분이 그 순간 화면에 있고 보이고(투명 아님) 가려지지 않았다(패널 Pick) · 화면 전체가 아니다 → 구멍이 있는 단계면 구멍 밖 단추 클릭은 층이 삼킨다(hint) →
    /// next·free·demo = 말풍선 [다음] · <c>screen:</c> = 앵커 부분(창 닫기 → 여는 단추)을 차례로 · command = <c>auto</c> 답마다 그 답을 내는 화면 요소를 차례로(<see cref="PlanOf"/>) · bot = 기다림.
    /// 앵커 부분 가운데 창 닫기(<c>…-close</c> · 배틀 결과 [확인])는 단계 처음에 먼저 누른다(07 T130).</item>
    /// <item>누르기(<see cref="Press(VisualElement, string)"/>): 요소가 보이고 켜져 있고 · 그 가운데 점의 패널 Pick이 그 요소(또는 그 안·그 위 판)이고 · 그 요소로 보낸 ClickEvent가 층의 거르기(패널 맨 위
    /// TrickleDown)를 지나 닿아야 한다. 그 ClickEvent가 <c>stage.OnTap</c> 칸의 동작을 그대로 부르고, 키트 단추는 그 단추의 Clickable을 부른다(창 없는 PlayMode는 패널에 포인터를 넣지 못한다 — RULE-CAPTURE).
    /// 보드 칸은 <see cref="GameFlow.Pointer(float, float, bool)"/>(마우스와 같은 길 — <see cref="GameFlow.ClickGate"/>를 지난다)로 그 칸 가운데를 누른다(걷기 = 같은 칸 두 번).</item>
    /// </list>
    /// 쓰는 법: <c>[UnityTest] public IEnumerator M65_E2_클릭경로() => TutorialClickPath.Run("e02");</c> — 끝 국면을 더 보려면 <c>Run("e02", p =&gt; Assert.That(p.Flow.Client.View…))</c>.
    /// 표에 없는 답 꼴은 <see cref="PlanOf"/>에 한 줄 더한다(실패 글이 그 답을 적는다).
    /// </summary>
    public sealed class TutorialClickPath
    {
        public const int Width = 1920, Height = 1080;

        /// <summary>클릭 경로 시험 하나의 목표 시간(초) — 넘으면 경고 로그(관문 시간). 단정하지 않는다(관문은 Unity를 낮은 우선순위로 돌린다).</summary>
        public const double TargetSeconds = 30;

        public GameFlow Flow { get; private set; }

        /// <summary>에러 로그 · 「[튜토리얼]」 경고(앵커 못 찾음 · 대조 어긋남). 비어야 통과.</summary>
        public readonly List<string> Errors = new List<string>();

        /// <summary>지난 단계(「각본.단계」 — 화면에 뜬 차례).</summary>
        public readonly List<string> Steps = new List<string>();

        /// <summary>누른 수(요소 · 칸).</summary>
        public int Clicks { get; private set; }

        /// <summary>구멍 밖 단추 클릭을 층이 삼킨 것을 확인한 단계 수.</summary>
        public int Swallowed { get; private set; }

        /// <summary>「그 순간 그 한 곳만 구멍」을 본 횟수(단계가 뜰 때 · 부분 여럿인 단계의 누르기마다 — 결정 99 ①).</summary>
        public int OneChecked { get; private set; }

        /// <summary>부분 여럿인 단계에서 차례로 비춘 부분(「각본.단계 부분」 — 누르기 직전에 본 것).</summary>
        public readonly List<string> Lit = new List<string>();

        /// <summary>걸린 시간(초 — 장 고르기부터 끝 창까지).</summary>
        public double Seconds { get; private set; }

        RenderTexture rt;
        ScreenBoot boot;
        string lang;   // null = 한국어 그대로(Run의 lang)

        /// <summary>영어로 지날 때(<c>Run(…, lang: "en")</c>) 화면에 뜬 말풍선 글(단계마다 — 한글 0 단정).</summary>
        public readonly List<string> Texts = new List<string>();

        /// <summary>목차·장 고르기(튜토리얼 판 · 층 · 기록).</summary>
        public TutorialMenu Menu => Flow.Tutorial;

        TutorialMenu Tut => Flow.Tutorial;
        UiStage Stage => Flow.Stage;

        /// <summary>
        /// 그 장(목차 id — <c>main0</c> · <c>e01</c> …)을 목차에서 골라 끝 창까지 사람처럼 누른다. 끝 창 · 단계마다 대조 어긋남 0 · 에러 로그 0을 단정하고 <paramref name="check"/>(끝 국면 단정)를 부른다.
        /// </summary>
        public static IEnumerator Run(string chapter, Action<TutorialClickPath> check = null, string lang = null)
        {
            var path = new TutorialClickPath();
            Application.logMessageReceived += path.Grab;
            try
            {
                yield return path.Boot();
                if (lang != null)
                {
                    // 화면 글의 언어(M9 언어 ② — GameFlow.Lang 한 곳): 말풍선 · 단추 · 창이 그 언어로 그려진 채 같은 길을 지난다
                    path.Flow.Lang = lang;
                    Assert.That(path.Flow.Lang, Is.EqualTo(lang), "언어 표가 없다: " + lang);
                    path.lang = lang;
                }
                var watch = Stopwatch.StartNew();
                yield return path.Drive(chapter);
                path.Seconds = watch.Elapsed.TotalSeconds;
                Debug.Log($"[클릭 경로] {chapter}: 단계 {path.Steps.Count} · 클릭 {path.Clicks} · 구멍 밖 삼킴 확인 {path.Swallowed} · 한 곳만 구멍 확인 {path.OneChecked}(부분 옮김 {path.Lit.Count}) · {path.Seconds:F1}초");
                Debug.Log($"[클릭 경로] {chapter}: 말풍선 없이 닫은 결과 창 {path.ResultsClosed} · 결과 창 [확인]으로 끝난 단계 {string.Join(",", path.Closed)} · 창 위에 앵커 없이 뜬 단계 {string.Join(", ", path.OverWindow)} · 창이 열린 채 [다음] 없음 {string.Join(",", path.NextHeld)} · 끌어 놓기 {path.Drags}");
                if (path.Seconds > TargetSeconds) Debug.LogWarning($"[클릭 경로] {chapter}: {path.Seconds:F1}초 — 목표 {TargetSeconds}초를 넘었다");
                Assert.That(path.Tut.Ending, Is.True, chapter + ": 끝 창");
                check?.Invoke(path);
                Assert.That(path.Errors, Is.Empty, chapter + " 에러 로그·튜토리얼 경고:\n" + string.Join("\n", path.Errors));
            }
            finally
            {
                Application.logMessageReceived -= path.Grab;
                path.Release();
            }
        }

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(type + ": " + msg);
            else if (type == LogType.Warning && msg.StartsWith("[튜토리얼]", StringComparison.Ordinal)) Errors.Add("경고: " + msg);
        }

        /// <summary>보드 씬을 열고 1920×1080 RenderTexture에 그린다(튜토리얼 기록은 메모리 — 사용자 기록에 쓰지 않는다).</summary>
        public IEnumerator Boot()
        {
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot?.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            boot.PointerFromMouse = false;
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            Time.captureDeltaTime = 1f / 60f;   // 프레임 시각을 60fps로 — 배치의 긴 프레임이 화면 시계를 앞당겨 층의 「2초 못 찾음」이 실제보다 먼저 차지 않게
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
            Flow = boot.Flow;
            Tut.Record = TutorialRecord.Memory();
        }

        /// <summary>타이틀로 돌리고 RenderTexture를 놓는다.</summary>
        public void Release()
        {
            if (Flow != null && lang != null) Flow.Lang = "ko";
            if (Flow != null) Flow.OpenTitle();
            if (boot != null && boot.Root != null) boot.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
                rt = null;
            }
            Time.captureDeltaTime = 0f;
        }

        // ── 몰기 ──

        /// <summary>그 장을 목차에서 고르고 끝 창까지 단계마다 누른다(<see cref="Run"/>의 가운데).</summary>
        public IEnumerator Drive(string chapter)
        {
            Assert.That(Tut.Start(chapter), Is.True, chapter + ": 목차의 장 고르기");
            TutorialLayer lastLayer = null;
            int lastChapter = int.MinValue;
            for (int guard = 0; !Tut.Ending; guard++)
            {
                Assert.That(guard, Is.LessThan(600), chapter + ": 끝나지 않는다");
                var before = Tut.Runner;
                var bots = before != null ? BotCounts(before) : null;   // 층이 단계를 띄우기 전의 봇 답 수(bot 단계는 띄울 때 봇을 푼다)
                yield return Shown();
                Assert.That(Tut.Ending || (Tut.Layer != null && Tut.Layer.Shown), Is.True, "층이 단계를 띄우지 않는다 — " + Where());
                if (Tut.Ending) break;
                var layer = Tut.Layer;
                var runner = layer.Runner;
                var step = runner.Step;
                int index = runner.Index;
                string key = runner.Script.Id + "." + step.Id;
                Steps.Add(key);
                if (runner != before) bots = BotCounts(runner);   // 이어 연 각본(봇은 새로)
                yield return Settle(layer, step);   // 배치가 서고 층이 앵커를 다시 찾는다
                // 마박사(결정 96 Q2 다): 전신은 장 첫 말풍선(그 층에서 처음 뜨는 단계 · 장 번호가 바뀐 첫 단계)에만, 나머지는 작은 얼굴
                bool chapterFirst = layer != lastLayer || step.Chapter != lastChapter;
                lastLayer = layer;
                lastChapter = step.Chapter;
                Assert.That(layer.FullBody, Is.EqualTo(chapterFirst), key + ": 마박사 전신은 장 첫 말풍선에만(결정 96 Q2 다)");
                Assert.That(UiStage.ButtonSound(layer.Element.Q<Button>("tut-layer-next")), Is.EqualTo(Sinnoh.Fx.SoundKeys.Decide).Or.Null, key + ": [다음]의 소리 = 확정(행 밖 「튜토리얼 넘김」 — 숨긴 단추는 null)");
                if (lang != null)
                {
                    Texts.Add(key + " " + layer.ShownText);
                    Assert.That(System.Text.RegularExpressions.Regex.IsMatch(layer.ShownText ?? "", "[가-힣ㄱ-ㆎ]"), Is.False, $"{key}: {lang} 말풍선에 한글 — {layer.ShownText}");
                    foreach (string b in new[] { "tut-layer-next", "tut-layer-skip", "tut-layer-quit" })
                        Assert.That(System.Text.RegularExpressions.Regex.IsMatch(layer.Element.Q<Button>(b).text ?? "", "[가-힣]"), Is.False, $"{key}: {lang} 단추 {b}에 한글");
                }
                CheckAnchor(layer, step, key);
                CheckWindow(layer, step, key);
                CheckOutside(layer, step, key);
                yield return Do(layer, step, key);
                yield return Until(() => Tut.Ending || Tut.Runner != runner || runner.Index != index, () => key + " 단계가 끝나지 않는다 — " + Where());
                Assert.That(runner.Failures, Is.Empty, key + " 대조 어긋남:\n" + string.Join("\n", runner.Failures));
                CheckBots(runner, step, key, bots);
            }
        }

        string Where()
        {
            var step = Tut.Runner?.Step;
            string top = Flow.Top != null ? Flow.Top.Name : "-";
            var open = new List<string>();   // 내게 열린 선택(종류/세부)
            if (Flow.Client?.View != null)
                foreach (var c in Flow.Client.View.Pending)
                    if (c.Seat == Flow.Client.Seat && !c.Answered) open.Add(c.Kind + "/" + c.Sub);
            string battle = Stage.Current is BattleScreen b ? $" · 배틀 열림 {b.Open} 굴림 {b.Rolling} 결과 {b.ShowsResult} 끝 {Flow.Battle?.Ended} 대기열 {Flow.Fx?.Sequencer.IsEmpty}/{Flow.Fx?.Sequencer.InputOpen} 멈춤 {Flow.Fx?.Sequencer.Paused} 지금 {Flow.Fx?.Sequencer.Current?.Kind}@{Flow.Fx?.Sequencer.LocalMs} 남은 {Flow.Fx?.Sequencer.Pending}" : "";
            return $"{Tut.Script?.Id}.{step?.Id}({step?.Advance}) 맨 위 {top}{battle} · 받은 {Tut.Runner?.Taken} · 열린 선택 {string.Join(",", open)} · 층 {(Tut.Layer != null && Tut.Layer.Shown ? "뜸 " + Tut.Layer.DockAt : "안 뜸")}";
        }

        IEnumerator Do(TutorialLayer layer, TutorialStep step, string key)
        {
            // 창 닫기 부분(…-close · 배틀 결과 [확인]) — 그 창이 다음 앵커를 가리므로 사람이 먼저 닫는다(07 T130)
            var parts = TutorialLayerAnchor.Parts(step.Anchor);
            bool nextKind = step.Advance == StepAdvance.Next || step.Advance == StepAdvance.Free || step.Advance == StepAdvance.Demo;
            foreach (string part in parts)
                if (IsDismiss(part) && Find(part) != null)   // 그 창이 떠 있으면
                {
                    // [다음] 단계: 창이 열린 채로는 [다음]이 없고 눌러도 단계를 넘기지 않는다(결정 107 ② — 넘기면 다음 단계의 앵커가 그 창에 가려 막힌다)
                    if (nextKind && part != "battle-result-ok")
                    {
                        Assert.That(layer.NextShown, Is.False, $"{key}: 닫아야 하는 창({part})이 열려 있는데 [다음]이 있다(결정 107 ②)");
                        Assert.That(layer.Next(), Is.False, $"{key}: 창({part})이 열린 채 [다음]이 단계를 넘겼다(결정 107 ②)");
                        NextHeld.Add(key);
                    }
                    yield return Press(part, key + " 창 닫기");
                }
            switch (step.Advance)
            {
                case StepAdvance.Next:
                case StepAdvance.Free:
                case StepAdvance.Demo:
                    if (layer.Closing)
                    {
                        // 결과 창을 닫으면 끝나는 단계(결정 104 ①): [다음] 단추가 없고 결과 창 [확인]이 끝낸다
                        Assert.That(TutorialLayerAnchor.Visible(layer.Element.Q("tut-layer-next"), null, out _), Is.False, key + ": 결과 창을 닫는 단계에 [다음]이 있다");
                        Closed.Add(key);
                        yield return Press("battle-result-ok", key + " 결과 창 [확인]");
                    }
                    else yield return Press(layer.Element.Q("tut-layer-next"), key + " [다음]");
                    break;
                case StepAdvance.Screen:
                    foreach (string part in parts)
                        if (!IsDismiss(part)) yield return Press(part, key + " 창 열기");
                    break;
                case StepAdvance.Command:
                    foreach (var p in step.Auto)
                    {
                        int taken = layer.Runner.Taken, index = layer.Runner.Index;
                        yield return Answer(layer, step, p, key);
                        if (p.Choice == ChoiceKind.StarterPick) continue;   // 스타팅은 고르기만 — 트레이너 카드와 함께 [확정]이 낸다
                        yield return Until(() => layer.Runner.Taken > taken || layer.Runner.Index != index || Tut.Runner != layer.Runner, () => $"{key}: {p}를 눌렀는데 받지 않았다 — 막힘 {layer.Runner.Blocked} · {Where()}");
                    }
                    break;
                case StepAdvance.Bot:
                    // 좌석이 여럿인 각본: 봇 턴 띠(「용식의 턴」)가 떠 있다(07 §9-1 · T34)
                    if (layer.Runner.Script.Start.Seats > 1)
                    {
                        Assert.That(layer.BandShown, Is.True, key + ": bot 단계인데 봇 턴 띠가 없다");
                        // 띠의 얼굴 = 이름에 맞는 Pt 인물(결정 96 Q5 나 — 좌석 1 용식 = 라이벌 · 좌석 2 빛나 = 여자 주인공), 좌석 칩도 같은 인물
                        string who = step.BotSeat == 1 ? Sinnoh.Rules.Session.Protagonists.Rival : Sinnoh.Rules.Session.Protagonists.Dawn;
                        Assert.That(layer.BandFace, Is.EqualTo(who), key + ": 봇 턴 띠의 얼굴");
                        Assert.That(Flow.Protagonist(step.BotSeat), Is.EqualTo(who), key + ": 봇 좌석의 인물");
                    }
                    break;
            }
        }

        // 봇 좌석마다 낸 답 수(좌석 1부터 — 봇이 없으면 빈 목록)
        static List<int> BotCounts(TutorialRunner runner)
        {
            var counts = new List<int>();
            for (int seat = 1; runner.Bot(seat) != null; seat++) counts.Add(runner.Bot(seat).Consumed);
            return counts;
        }

        // 그 단계 동안 봇이 낸 답 = bot 단계면 그 봇 좌석의 botAnswers, 아니면 0(봇은 풀어 준 만큼만 움직인다 — 07 T37)
        static void CheckBots(TutorialRunner runner, TutorialStep step, string key, List<int> before)
        {
            for (int i = 0; i < before.Count; i++)
            {
                int seat = i + 1;
                int want = step.Advance == StepAdvance.Bot && step.BotSeat == seat ? step.BotAnswers : 0;
                Assert.That(runner.Bot(seat).Consumed - before[i], Is.EqualTo(want), $"{key}: 봇 좌석 {seat}가 이 단계에서 낸 답 수");
            }
        }

        /// <summary>말풍선 없이 사람이 닫은 배틀 결과 창 수(결정 104 ① — 결과 창이 떠 있는 동안에는 그 창을 말하는 단계만 뜬다).</summary>
        public int ResultsClosed { get; private set; }

        /// <summary>결과 창 [확인]으로 끝난 단계(결과 창을 말하는 단계 — [다음] 단추 없음).</summary>
        public List<string> Closed { get; } = new List<string>();

        /// <summary>창이 열린 채 [다음]이 없음을 확인한 [다음] 단계(결정 107 ② — 창 닫기 부분이 있는 단계).</summary>
        public List<string> NextHeld { get; } = new List<string>();

        /// <summary>끌어 놓기로 낸 수(결정 107 ① — 정비 판 순서 · 대사가 「끌어」인 지닌물건).</summary>
        public int Drags { get; private set; }

        /// <summary>창이 떠 있는데 앵커 없이 뜬 단계(「각본.단계 창」 — 대사가 그 창을 가리지 않고 말해도 되는지 사람이 읽어 본다).</summary>
        public List<string> OverWindow { get; } = new List<string>();

        // 층이 단계를 띄울 때까지 시각을 보낸다. 배틀 결과 창이 [확인]을 기다리는데 다음 단계가 그 창을 말하는 단계가 아니면 말풍선은 뜨지 않는다 — 사람처럼 [확인]으로 닫는다
        IEnumerator Shown()
        {
            for (int i = 0; i < 600 && !(Tut.Ending || (Tut.Layer != null && Tut.Layer.Shown)); i++)
            {
                var step = Tut.Runner?.Step;
                var ok = Flow.BattleOver && step != null && !TutorialLayerAnchor.InResult(step.Anchor) ? Find("battle-result-ok") : null;
                if (ok != null && ok.enabledInHierarchy)
                {
                    for (int f = 0; f < 3; f++) yield return null;   // 그 사이에 말풍선이 뜨면 안 된다
                    Assert.That(Tut.Layer == null || !Tut.Layer.Shown, Is.True, $"{Tut.Script?.Id}.{step.Id}: 배틀 결과 창이 떠 있는데 다음 말풍선이 떴다(결정 104 ①)");
                    if (Find("battle-result-ok") == null) continue;
                    ResultsClosed++;
                    yield return Press("battle-result-ok", $"{Tut.Script?.Id}.{step.Id} 앞 결과 창 [확인]");
                    continue;
                }
                Flow.Skip(Pace(500));
                yield return null;
            }
        }

        // 단계가 뜰 때 떠 있는 창(결정 104 ①): 배틀 결과 창이 [확인]을 기다리면 그 단계는 결과 창을 말하는 단계(앵커 첫 부분이 그 창 안)여야 하고,
        // 사람이 닫아야 하는 보드 창(TutorialLayer.Screens)이 맨 위면 앵커의 첫 부분(보이는 것)이 그 창 안이어야 한다 — 앵커가 없는 단계는 적어 둔다(OverWindow)
        void CheckWindow(TutorialLayer layer, TutorialStep step, string key)
        {
            if (Flow.BattleOver)
            {
                Assert.That(TutorialLayerAnchor.InResult(step.Anchor), Is.True, $"{key}: 배틀 결과 창이 떠 있는데 그 창 밖을 말하는 단계가 떴다(앵커 {step.Anchor ?? "없음"})");
                return;
            }
            var top = Flow.Top;
            if (top == null || !((IList<string>)TutorialLayer.Screens).Contains(top.Name)) return;
            if (step.Advance == StepAdvance.Screen && step.Screen == top.Name) return;   // 그 창을 열라는 단계가 이미 열린 창 위에 떴다(곧 끝난다)
            if (Find(top.Name + "-close") == null) return;   // 닫을 수 없는 창(답해야 하는 고르기가 열려 있는 사당 판 등)은 닫아야 하는 창이 아니다
            string first = First(Flow, layer, step.Anchor);
            if (first == null)
            {
                OverWindow.Add(key + " " + top.Name);
                return;
            }
            var window = Stage.Root.Q(top.Name);
            var el = first.Contains(":") ? null : Find(first);
            Assert.That(el != null && window != null && (el == window || window.Contains(el)), Is.True, $"{key}: 창 {top.Name}이 떠 있는데 앵커 첫 부분 {first}가 그 창 밖이다(결정 104 ① — 창 닫기 부분을 먼저)");
        }

        /// <summary>앵커 부분이 창 닫기 단추인가(…-close · 배틀 결과 [확인]).</summary>
        public static bool IsDismiss(string part) => TutorialLayerAnchor.IsDismiss(part);

        // 지정 행동 하나: 그 답이 열릴 때까지 기다리고, 화면 요소를 차례로 누른다
        IEnumerator Answer(TutorialLayer layer, TutorialStep step, AnswerPattern p, string key)
        {
            PendingChoice choice = null;
            ChoiceAnswer answer = null;
            if (!p.IsMaint)
                yield return Until(() =>
                {
                    choice = null;
                    answer = null;
                    var v = Flow.Client?.View;
                    if (v == null) return false;
                    foreach (var c in v.Pending)
                    {
                        if (c.Seat != Flow.Client.Seat || c.Answered) continue;
                        var a = p.First(c, Flow.Client.Legal(c)) ?? Proposal(c, p);
                        if (a == null) continue;
                        choice = c;
                        answer = a;
                        return true;
                    }
                    return false;
                }, () => $"{key}: {p}에 맞는 내 선택이 열리지 않는다 — {Where()}");
            foreach (var click in PlanOf(choice, answer, p))
            {
                if (click.Ready != null)
                {
                    yield return Until(click.Ready, () => $"{key} {p}: 누를 창의 입력이 열리지 않는다 — {Where()}");
                    yield return Frames(1);   // 열린 입력으로 칸을 다시 짓는다
                }
                if (click.Cell >= 0) yield return Cell(layer, click.Cell, key + " " + p);
                else if (click.Drop != null) yield return Drag(click.Name(), click.Drop(), key + " " + p);
                else yield return Press(click.Name(), key + " " + p);
            }
        }

        // 거래 제안은 합법 답에 늘어놓지 않는다(rulings A29) — 상대와 내용을 다 적은 표기는 그대로 답이다(진행기 TutorialRunner.Proposal과 같은 꼴)
        static ChoiceAnswer Proposal(PendingChoice c, AnswerPattern p)
        {
            if (c.Kind != ChoiceKind.MapAction || p.IsMaint || p.Kind != AnswerKind.ProposeTrade || !p.Target.HasValue || p.Values == null) return null;
            var a = ChoiceAnswer.Of(AnswerKind.ProposeTrade, p.Target.Value);
            a.Values.AddRange(p.Values);
            return a;
        }

        /// <summary>누를 것 하나: 요소 이름(그 순간에 정한다 — 앞 클릭이 연 줄의 색인 등) 또는 보드 칸.</summary>
        public struct Click
        {
            public Func<string> Name;
            public int Cell;
            /// <summary>누르기 전에 기다릴 것(없으면 null) — 사람이 그 창의 입력이 열린 것을 보고 누르는 칸(입력이 닫힌 채 지은 칸은 누르기를 받지 않는다).</summary>
            public Func<bool> Ready;
            /// <summary>끌어 놓을 곳(없으면 null — 누르기): Name의 요소를 눌러 이 요소까지 끌고 뗀다(<see cref="Drag"/> — 결정 107 ①).</summary>
            public Func<string> Drop;
            public static Click When(Func<bool> ready, string name) => new Click { Name = () => name, Cell = -1, Ready = ready };
            public static Click DragTo(Func<string> from, string to) => new Click { Name = from, Cell = -1, Drop = () => to };
            public static Click El(string name) => new Click { Name = () => name, Cell = -1 };
            public static Click El(Func<string> name) => new Click { Name = name, Cell = -1 };
            public static Click At(int cell) => new Click { Cell = cell };
        }

        /// <summary>
        /// 답 → 그 답을 내는 화면 요소(사람이 누르는 차례). 화면마다 요소 이름은 그 화면 파일의 머리 주석(04 각 화면 표 「요소 이름」).
        /// </summary>
        public IEnumerable<Click> PlanOf(PendingChoice c, ChoiceAnswer a, AnswerPattern p)
        {
            var ctx = Flow.Ctx;
            if (p.IsMaint)
            {
                if (p.Maint == MaintKind.PartyOrder && p.Values != null)
                {
                    // 정비 판 포켓몬 카드(board-party-mon-<칸>)를 끌어 앞 칸의 열(board-party-col-<칸>)에 놓는다 — 빼서 그 자리에 끼운다(결정 107 ① · 07 T200, 대사가 「끌어」)
                    var order = new List<int>();
                    for (int i = 0; i < p.Values.Length; i++) order.Add(i);
                    for (int i = 0; i < p.Values.Length; i++)
                    {
                        int at = order.IndexOf(p.Values[i]);
                        if (at == i) continue;
                        yield return Click.DragTo(() => "board-party-mon-" + at, "board-party-col-" + i);
                        int moved = order[at];
                        order.RemoveAt(at);
                        order.Insert(i, moved);
                    }
                    yield break;
                }
                if (p.Maint == MaintKind.HoldItem && p.Party.HasValue && p.Value.HasValue && p.Value.Value >= 0)
                {
                    // 정비 판 가방의 그 지닌물건(board-party-bag-<n>)을 집고 → 그 파티 칸의 지닌물건 칸(둘째 칸이면 item2-). 대사가 「끌어」라고 하면 끌어 놓는다(결정 107 ①)
                    int item = p.Value.Value;
                    Func<string> bagAt = () => "board-party-bag-" + (IndexIn(Party().Bag, item) + 1);
                    string slot = (p.Slot == 1 ? "board-party-item2-" : "board-party-item-") + p.Party.Value;
                    var said = Tut.Runner?.Step;
                    if (said != null && said.Text != null && Stage.T(said.Text).Contains("끌"))
                    {
                        yield return Click.DragTo(bagAt, slot);
                        yield break;
                    }
                    yield return Click.El(bagAt);
                    yield return Click.El(slot);
                    yield break;
                }
                if (p.Maint == MaintKind.UseBlessing && p.Value.HasValue)
                {
                    // 축복 판(S59): [UB 턴 건너뛰기] 또는 탈락한 좌석의 [부활](좌석 번호 1부터)
                    yield return Click.El(p.Value.Value == UbBlessing.SkipTurn ? "board-bless-skip" : "board-bless-revive-" + (p.Value.Value + 1));
                    yield break;
                }
                Assert.Fail("클릭 경로 표에 없는 정비 꼴: " + p);
            }
            // 트레이너 카드 · 기술 효과의 물음(Reaction — 운명 창 밖): 보드의 카드 바꾸기 창 · 보드 「행동」 칸의 카드 단추 · 배틀 차례 칸
            if (c.Kind == ChoiceKind.Reaction && c.Sub != ChoiceSub.Fate)
            {
                int at = Flow.Client.Legal(c).FindIndex(x => x.SameAs(a));
                if (CardSwapScreen.Owns(ctx, c))
                {
                    yield return Click.When(() => Flow.Top is CardSwapScreen && Flow.BoardOpen, "board-card-swap-card-" + (at + 1));   // 부채꼴의 카드를 앞으로 → [이 카드로 교체]
                    yield return Click.El(CardSwapScreen.Go);
                    yield break;
                }
                if (BoardHud.BoardCardAsk(c) && !(Stage.Current is BattleScreen))
                {
                    yield return Click.El("board-hud-card-" + (at + 1));
                    yield break;
                }
                switch (c.Sub)
                {
                    case ChoiceSub.InitiativeToken:
                        yield return Click.El(a.Kind == AnswerKind.Accept ? "battle-token-use" : "battle-token-pass");
                        yield break;
                    case ChoiceSub.AlluringStew:
                    case ChoiceSub.EffectOption:
                        yield return Click.El(a.Kind == AnswerKind.Accept ? "battle-ask-yes" : "battle-ask-no");
                        yield break;
                    case ChoiceSub.EffectPick:
                        yield return Click.El(a.Kind == AnswerKind.Decline ? "battle-ask-no" : "battle-pick-" + (IndexAmong(c, a, x => x.Kind == AnswerKind.Pick) + 1));
                        yield break;
                }
                Assert.Fail($"클릭 경로 표에 없는 물음: {c.Kind}/{c.Sub} {a}");
            }
            switch (c.Kind)
            {
                case ChoiceKind.VillagePick:
                    yield return Click.El("setup-village-item-" + ctx.Fixtures.Fixtures[a.Target].Key.Substring("village.".Length));
                    yield return Click.El("setup-village-confirm");
                    yield break;
                case ChoiceKind.StarterPick:
                    yield return Click.El("setup-pick-starter-" + ctx.Cards.Pokemon[a.Target].Key.Replace(':', '-'));
                    yield break;
                case ChoiceKind.TrainerCardPick:
                    yield return Click.El("setup-pick-card-" + ctx.Cards.TrainerCardIds[a.Target]);
                    yield return Click.El("setup-pick-confirm");
                    yield break;
                case ChoiceKind.EncounterPick:
                    yield return Click.El(() =>
                    {
                        var enc = Stage.Current as EncounterScreen;
                        Assert.That(enc, Is.Not.Null, "조우 창이 없다");
                        for (int i = 0; i < enc.Cards.Count; i++)
                            if (enc.Cards[i].SameAs(a)) return "encounter-pick-" + (i + 1);
                        return "encounter-pick-?";
                    });
                    yield break;
                case ChoiceKind.Discard:
                    yield return Click.El(() => "board-fate-card-" + (LastIndex(Fate().Hand, a.Target) + 1));
                    yield return Click.El("board-fate-discard");
                    yield break;
                case ChoiceKind.MapAction:
                case ChoiceKind.HiddenSpot:
                    switch (a.Kind)
                    {
                        case AnswerKind.Move:
                            yield return Click.At(a.Target);   // 고르기
                            yield return Click.At(a.Target);   // 같은 칸을 다시 누르면 걷는다(결정 70 ⑱)
                            yield break;
                        case AnswerKind.VillageHeal:
                            yield return Click.El("board-village-heal");
                            yield break;
                        case AnswerKind.Craft:
                            yield return Click.El("board-craft-make-" + ctx.Items.Items[a.Target].Key.Substring("item.".Length));
                            yield break;
                        case AnswerKind.PlayFate:
                            yield return Click.El(() =>
                            {
                                var hand = Fate().Hand;
                                for (int i = 0; i < hand.Count; i++)
                                    if (FateRules.KindOf(ctx, hand[i]) == (FateCard)a.Target) return "board-fate-card-" + (i + 1);
                                return "board-fate-card-?";
                            });
                            yield return Click.El("board-fate-use-" + a.Arg);
                            if (a.Values.Count > 0) yield return Click.At(a.Values[0]);   // 펄기아 — 갈 칸
                            yield break;
                        case AnswerKind.Ginkgo:   // 마을 창의 은행상회 칸(BankBox): [두 줄 모두] 또는 그 줄의 [거래]
                            yield return Click.El(a.Target == BankRules.Both ? "board-village-bank-both" : "board-village-bank-trade-" + a.Target);
                            yield break;
                        case AnswerKind.Fly:   // 마을 창: 목적지 마을 → (고른 색이 다르면) 규토리 색 → [출발]
                            yield return Click.El("board-village-dest-" + ctx.Fixtures.Fixtures[a.Target].Key.Replace('.', '-'));
                            if (Top<VillageScreen>().Pay != a.Arg || IsLit("board-village-color-" + ((ApricornColor)a.Arg).ToString().ToLowerInvariant())) yield return Click.El("board-village-color-" + ((ApricornColor)a.Arg).ToString().ToLowerInvariant());
                            yield return Click.El("board-village-fly");
                            yield break;
                        case AnswerKind.Evolve:   // 진화 창: (반값이면) [반값으로] → 그 포켓몬 줄의 [진화](줄 = 빈 칸을 뺀 파티 차례)
                            if ((a.Arg == 1) != Top<EvolveScreen>().Half) yield return Click.El("board-evolve-half");
                            yield return Click.El("board-evolve-go-" + IndexIn(Flow.Client.View.Seats[Flow.Client.Seat].Party, a.Target, skipEmpty: true));
                            yield break;
                        case AnswerKind.Offer:   // 사당 판 공물: 규토리 색 → (여행 점수 7이 다르면) [여행 점수 7 더] → [바치기]
                            if (Top<ShrineScreen>().OfferColor != a.Arg || IsLit("board-shrine-color-" + ((ApricornColor)a.Arg).ToString().ToLowerInvariant())) yield return Click.El("board-shrine-color-" + ((ApricornColor)a.Arg).ToString().ToLowerInvariant());
                            if ((a.Values.Count > 0) != Top<ShrineScreen>().Extra) yield return Click.El("board-shrine-extra");
                            yield return Click.El("board-shrine-go");
                            yield break;
                        case AnswerKind.ProposeTrade:
                            // 거래 창(S36 — 앞 단계 screen:board-trade가 열었다): 줄마다 [주는 쪽, 종류, 키, 수] → 그 칩을 수만큼 → [제안]
                            for (int i = 0; i + 3 < a.Values.Count; i += 4)
                                for (int n = 0; n < a.Values[i + 3]; n++)
                                    yield return Click.El("board-trade-" + (a.Values[i] == 0 ? "give-" : "ask-") + ((TradeKind)a.Values[i + 1]).ToString().ToLowerInvariant() + "-" + a.Values[i + 2]);
                            yield return Click.El("board-trade-propose");
                            yield break;
                        case AnswerKind.Challenge:
                            // [도전] — 상대가 여럿이거나 원격 도전이 있으면 상대 고르는 창(S35)의 [도전]
                            yield return Click.El("board-hud-act-" + BoardHud.Challenge);
                            var challenges = ChallengeScreen.Legal(Flow);
                            if (ChallengeScreen.Needed(challenges)) yield return Click.El("board-challenge-go-" + (challenges.FindIndex(x => x.SameAs(a)) + 1));
                            yield break;
                        case AnswerKind.UseAbility when !CardAsk.IsWeather(a):
                        {
                            // 트레이너 카드 능력: [능력 이름] → (칸) → (값 단추) — 무리에 답이 하나이고 칸이 없으면 바로 낸다
                            yield return Click.El("board-hud-act-" + BoardHud.Ability);
                            var group = Flow.Client.Legal(c).FindAll(x => x.Kind == AnswerKind.UseAbility && !CardAsk.IsWeather(x));
                            int cell = Flow.Hud.CellFor(a);
                            if (cell >= 0) yield return Click.At(cell);
                            else if (group.Count > 1) yield return Click.El(() => "board-hud-ability-" + (Flow.Hud.AbilityRowOf(a) + 1));
                            if (cell >= 0 && group.FindAll(x => Flow.Hud.CellFor(x) == cell).Count > 1)
                                yield return Click.El(() => "board-hud-ability-" + (Flow.Hud.AbilityRowOf(a) + 1));   // 그 칸의 답이 여럿 — 값 단추
                            yield break;
                        }
                    }
                    var act = Array.Find(c.Kind == ChoiceKind.MapAction ? BoardHud.MapActs : BoardHud.SpotActs, x => x.Kind == a.Kind);
                    Assert.That(act, Is.Not.Null, "클릭 경로 표에 없는 맵 답: " + a);
                    yield return Click.El("board-hud-act-" + act.Id);
                    yield break;
                case ChoiceKind.BattleAction:
                    if (a.Kind == AnswerKind.UseMove && a.Arg < 0)
                    {
                        yield return Click.El("battle-move-" + (IndexAmong(c, a, x => x.Kind == AnswerKind.UseMove && x.Arg < 0) + 1));
                        yield break;
                    }
                    if (a.Kind == AnswerKind.ThrowBall)
                    {
                        yield return Click.El("battle-bag");
                        yield return Click.El("battle-ball-" + (IndexAmong(c, a, x => x.Kind == AnswerKind.ThrowBall) + 1));
                        yield break;
                    }
                    break;
                case ChoiceKind.SendIn:
                case ChoiceKind.NextPokemon:
                case ChoiceKind.SwitchWindow:
                    if (a.Kind == AnswerKind.Pick)
                    {
                        yield return Click.El("battle-bench-" + (IndexAmong(c, a, x => x.Kind == AnswerKind.Pick) + 1));
                        yield break;
                    }
                    if (a.Kind == AnswerKind.Decline)
                    {
                        yield return Click.El("battle-bench-stay");
                        yield break;
                    }
                    break;
                case ChoiceKind.Respond:
                {
                    bool yes = a.Kind == AnswerKind.Accept;
                    if (c.Sub == ChoiceSub.UbSkip) yield return Click.El(yes ? "board-bless-yes" : "board-bless-no");   // 라운드 끝 「UB 턴을 건너뛰겠습니까?」 창
                    else if (c.Sub == ChoiceSub.Blessing) yield return Click.El(BoardHud.BlessAsk + (yes ? "-allow" : "-block"));   // 위 띠의 축복 동의
                    else if (Stage.Current is BattleScreen) yield return Click.El(yes ? "battle-ask-yes" : "battle-ask-no");
                    else yield return Click.El(yes ? BoardHud.PromptAccept : BoardHud.PromptDecline);   // S41 프롬프트
                    yield break;
                }
                case ChoiceKind.Reward:
                {
                    if (c.Sub == ChoiceSub.EvolveBranch && a.Kind == AnswerKind.Pick)
                    {
                        yield return Click.El("board-evolve-branch-" + (IndexAmong(c, a, x => true) + 1));   // 진화 창의 갈래 카드(합법 답 차례)
                        yield break;
                    }
                    if (c.Sub == ChoiceSub.EvolveTeach)
                    {
                        yield return Click.El(a.Kind == AnswerKind.Pick ? "board-evolve-teach-" + a.Arg : "board-evolve-keep");   // 옛 기술 칸 · [가르치지 않기]
                        yield break;
                    }
                    if (c.Sub == ChoiceSub.ShrinePick && a.Kind == AnswerKind.Pick)
                    {
                        yield return Click.El(() => "board-shrine-card-" + (ShrineOption(a) + 1));   // 사당 판의 뽑은 카드 → [확정]
                        yield return Click.El("board-shrine-go");
                        yield break;
                    }
                    // 단추 하나 = 합법 답 하나(엔진 순서): 보드의 카드가 연 고르기 창 · 배틀 결과 창의 보상 칸
                    int at = Flow.Client.Legal(c).FindIndex(x => x.SameAs(a));
                    yield return Click.El((CardRewardScreen.Owns(c) ? "board-card-reward-" : "battle-reward-") + (at + 1));
                    yield break;
                }
                case ChoiceKind.Simultaneous:
                    if (c.Sub == ChoiceSub.Follower && (a.Kind == AnswerKind.Pick || a.Kind == AnswerKind.Decline))
                    {
                        // 천상의 균열 추종자(비밀): 신 칸 또는 [안 고름] → [확정]
                        yield return Click.El(a.Kind == AnswerKind.Pick ? "board-shrine-god-" + ((SinnohGod)a.Target).ToString().ToLowerInvariant() : "board-shrine-nogod");
                        yield return Click.El("board-shrine-go");
                        yield break;
                    }
                    if (a.Kind == AnswerKind.Pick && a.Values.Count == 0 && a.Target >= 0 && a.Arg >= 0)
                    {
                        // 균열 카드 붙이기(S58): 그 포켓몬의 기술 칸 → [붙이기]
                        yield return Click.El("board-breach-slot-" + a.Target + "-" + a.Arg);
                        yield return Click.El("board-breach-go");
                        yield break;
                    }
                    break;
                case ChoiceKind.Reaction:
                    if (c.Sub == ChoiceSub.InitiativeToken && (a.Kind == AnswerKind.Accept || a.Kind == AnswerKind.Decline))
                    {
                        yield return Click.El(a.Kind == AnswerKind.Accept ? "battle-token-use" : "battle-token-pass");   // 우선권 토큰 [사용] · [안 씀]
                        yield break;
                    }
                    if ((c.Sub == ChoiceSub.EffectOption || c.Sub == ChoiceSub.AlluringStew) && (a.Kind == AnswerKind.Accept || a.Kind == AnswerKind.Decline))
                    {
                        yield return Click.El(a.Kind == AnswerKind.Accept ? "battle-ask-yes" : "battle-ask-no");   // 「쓸까」 물음 [예] · [넘기기]
                        yield break;
                    }
                    if (c.Sub == ChoiceSub.EffectPick && a.Kind == AnswerKind.Decline)
                    {
                        yield return Click.El("battle-ask-no");   // 기술 효과 고르기의 「안 함」 [넘기기]
                        yield break;
                    }
                    if (a.Kind == AnswerKind.Decline)
                    {
                        yield return Click.El("battle-fate-pass");
                        yield break;
                    }
                    if (a.Kind == AnswerKind.PlayFate)
                    {
                        if ((FateCard)a.Target == FateCard.Dialga)
                            foreach (int die in a.Values)
                                if (die >= 0) yield return Click.El("battle-fate-die-" + (die + 1));
                        yield return Click.El("battle-fate-" + FateCards.Key((FateCard)a.Target) + "-" + a.Arg);
                        yield break;
                    }
                    break;
            }
            Assert.Fail($"클릭 경로 표에 없는 답: {c.Kind} {a}");
        }

        // 층이 지금 그 부분을 비추고 있나(이미 골라져 있어도 각본이 짚는 부분은 사람이 누른다 — E3 S009의 빨강)
        bool IsLit(string part)
        {
            var layer = Tut.Layer;
            if (layer == null || !layer.Shown) return false;
            return Array.IndexOf(TutorialLayerAnchor.Parts(layer.Runner.Step.Anchor), part) >= 0;
        }

        FateScreen Fate()
        {
            var fate = Flow.Top as FateScreen;
            Assert.That(fate, Is.Not.Null, "운명 손패 창이 없다(맨 위 " + Flow.Top?.Name + ")");
            return fate;
        }

        // 맨 위 창이 그 화면인가(아니면 실패)
        T Top<T>() where T : class, IUiScreen
        {
            var top = Flow.Top as T;
            Assert.That(top, Is.Not.Null, typeof(T).Name + " 창이 없다(맨 위 " + Flow.Top?.Name + ")");
            return top;
        }

        PartyScreen Party() => Top<PartyScreen>();

        // 사당 판이 늘어놓은 고를 것(Options) 안의 그 답 자리
        int ShrineOption(ChoiceAnswer a)
        {
            var opts = Top<ShrineScreen>().Options;
            for (int i = 0; i < opts.Count; i++) if (opts[i].SameAs(a)) return i;
            Assert.Fail("사당 판의 고를 것에 없다: " + a);
            return -1;
        }

        // 목록 안의 그 값 자리(skipEmpty = 빈 칸 −1을 세지 않는다 — 진화 창 줄)
        static int IndexIn(IReadOnlyList<int> list, int value, bool skipEmpty = false)
        {
            for (int i = 0, n = 0; i < list.Count; i++)
            {
                if (skipEmpty && list[i] < 0) continue;
                if (list[i] == value) return n;
                n++;
            }
            Assert.Fail($"목록에 {value}가 없다");
            return -1;
        }

        static int IndexOf(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
            return -1;
        }

        static int LastIndex(IReadOnlyList<int> list, int value)
        {
            for (int i = list.Count - 1; i >= 0; i--) if (list[i] == value) return i;
            return -1;
        }

        // 화면이 줄을 세우는 차례(합법 답의 차례 그대로 — 그 꼴만 거른 것) 안의 그 답 자리
        int IndexAmong(PendingChoice c, ChoiceAnswer a, Predicate<ChoiceAnswer> kind)
        {
            int n = 0;
            foreach (var x in Flow.Client.Legal(c))
            {
                if (!kind(x)) continue;
                if (x.SameAs(a)) return n;
                n++;
            }
            Assert.Fail("합법 답에 없다: " + a);
            return -1;
        }

        // ── 앵커 표 ──

        /// <summary>
        /// 앵커 표(완료 조건 4): 그 장을 목차에서 골라 끝 창까지 진행기 자동 진행(<see cref="TutorialRunner.Play"/>)으로 지나며 단계마다 앵커 첫 부분을 찾는다(<see cref="Missing"/>) —
        /// 사람이 해야 하는 것(앵커의 창 닫기 부분 · <c>screen:</c> 단계의 창 열기)만 누른다. 한 줄씩 <paramref name="lines"/>에, 빠짐(못 찾음 · 화면 전체 · 가려짐)은 <paramref name="misses"/>에.
        /// 사람 몫을 누르지 못하거나 진행이 막히면 그 장은 거기서 빠짐 한 줄로 그만두고 다음 장으로 간다(한 번에 모든 장의 빠짐을 모은다). <paramref name="anchors"/> = 앵커가 있는 단계 수.
        /// </summary>
        public IEnumerator Survey(string chapter, List<string> lines, List<string> misses, Action<int> anchors)
        {
            Assert.That(Tut.Start(chapter), Is.True, chapter + ": 목차의 장 고르기");
            int count = 0;
            string stuck = null;
            for (int guard = 0; !Tut.Ending && stuck == null; guard++)
            {
                if (guard > 600) { stuck = "끝나지 않는다"; break; }
                yield return Shown();
                if (Tut.Ending) break;
                if (Tut.Layer == null || !Tut.Layer.Shown) { stuck = "층이 단계를 띄우지 않는다 — " + Where(); break; }
                var layer = Tut.Layer;
                var runner = layer.Runner;
                var step = runner.Step;
                int index = runner.Index;
                string key = runner.Script.Id + "." + step.Id;
                yield return Settle(layer, step);
                var parts = TutorialLayerAnchor.Parts(step.Anchor);
                if (parts.Length > 0)
                {
                    count++;
                    string first = First(Flow, layer, step.Anchor);
                    string why = Missing(Flow, layer, first);
                    var later = new List<string>();
                    foreach (string p in parts)
                        if (p != first) { string m = Missing(Flow, layer, p); later.Add(p + " " + (m ?? "잡힘") + (m == "못 찾음" && !p.Contains(":") ? "[" + Why(Flow, p) + "]" : "")); }
                    string line = $"{key} {step.Anchor}: {first} {why ?? "잡힘"}" + (later.Count > 0 ? " · 다른 부분 " + string.Join(", ", later) : "") + $" (맨 위 {Flow.Top?.Name ?? "-"}" + (why != null ? $" · 배틀 {(Flow.Battle != null ? (Flow.Battle.Ended ? "끝" : "중") : "-")} 결과 창 {Flow.BattleOver} · 대기열 {(Flow.Fx != null ? Flow.Fx.Sequencer.IsEmpty + "/" + Flow.Fx.Sequencer.InputOpen : "-")}" : "") + ")";
                    lines.Add(line);
                    Debug.Log("[앵커 표] " + line);
                    if (why != null) misses.Add(line);
                }
                foreach (string part in parts)
                    if (IsDismiss(part) && Find(part) != null) yield return Press(part, key + " 창 닫기");
                if (step.Advance == StepAdvance.Screen)
                {
                    foreach (string part in parts)
                    {
                        if (IsDismiss(part)) continue;
                        yield return Wait(() => Find(part) != null, 60);
                        if (Find(part) == null) { stuck = $"{key} 창 열기 단추 {part}가 보이지 않는다 — {Where()}"; break; }
                        yield return Press(part, key + " 창 열기");
                    }
                    if (stuck != null) break;
                }
                else
                {
                    try { if (Tut.Runner == runner && runner.Index == index) runner.Play(); }   // 앞 클릭(창 닫기) 사이에 층이 스스로 끝낸 단계(bot 등)는 건너뛴다 — 다음 단계를 자동으로 보내지 않게
                    catch (InvalidOperationException e) { stuck = key + " 자동 진행: " + e.Message; break; }
                }
                yield return Wait(() => Tut.Ending || Tut.Runner != runner || runner.Index != index);
                if (!(Tut.Ending || Tut.Runner != runner || runner.Index != index)) stuck = key + " 단계가 끝나지 않는다 — " + Where();
            }
            if (stuck != null)
            {
                misses.Add(chapter + " 멈춤: " + stuck);
                Debug.Log("[앵커 표] " + chapter + " 멈춤: " + stuck);
                Tut.ToMenu();
                yield return Frames(2);
            }
            anchors(count);
        }

        // 단계가 뜬 뒤 배치가 서고 층이 앵커를 다시 찾을 때까지 몇 프레임(시각은 보내지 않는다 — 갱신마다 다시 만드는 좌석 칩은 그 프레임에 배치가 없다)
        IEnumerator Settle(TutorialLayer layer, TutorialStep step)
        {
            yield return Frames(2);
            for (int i = 0; i < 60 && step.Anchor != null && Missing(Flow, layer, First(Flow, layer, step.Anchor)) != null; i++) yield return null;
        }

        // 조건이 설 때까지(단정 없이 — 앵커 표가 멈춤으로 적는다)
        IEnumerator Wait(Func<bool> done, int max = 600, int ms = 500)
        {
            for (int i = 0; i < max && !done(); i++)
            {
                Flow.Skip(Pace(ms));
                yield return null;
            }
        }

        // ── 단정 ──

        /// <summary>
        /// 앵커 첫 부분이 지금 화면에 있나: 못 찾음 · 화면 전체(구멍이 화면을 다 판다) · 가려짐(그 가운데의 패널 Pick이 다른 요소) → 그 까닭, 있으면 null.
        /// 칸은 그 가운데가 PC 층 요소 아래가 아니어야 한다(<see cref="GameFlow.OverUi"/>).
        /// </summary>
        public static string Missing(GameFlow flow, TutorialLayer layer, string part)
        {
            var rects = new List<Rect>();
            if (!TutorialLayerAnchor.Find(flow, part, layer.Element, rects, out var el)) return "못 찾음";
            var root = flow.Stage.Root;
            var screen = root.worldBound;
            foreach (var r in rects)
            {
                if (r.width >= screen.width - 1f && r.height >= screen.height - 1f) return "화면 전체";
                if (el != null)
                {
                    var picked = root.panel.Pick(r.center);
                    if (picked != null && picked != el && !el.Contains(picked) && !picked.Contains(el)) return "가려짐(" + Path(picked) + ")";
                }
                else
                {
                    float s = flow.Stage.Grade.PanelScale, h = screen.height * s;
                    if (part.StartsWith(TutorialLayerAnchor.CellPrefix, StringComparison.Ordinal) && flow.OverUi(r.center.x * s, h - r.center.y * s))
                        return "가려짐(" + Path(root.panel.Pick(r.center)) + ")";
                    if (part.StartsWith(TutorialLayerAnchor.DsPrefix, StringComparison.Ordinal))
                    {
                        var picked = root.panel.Pick(r.center);
                        var ds = root.Q("battle-ds");
                        if (picked != null && ds != null && picked != ds && !ds.Contains(picked) && !picked.Contains(ds)) return "가려짐(" + Path(picked) + ")";
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 단계가 뜰 때 보여야 하는 앵커 부분(앵커 표가 세는 것): 첫 부분 — 다만 창 닫기 부분(07 T130)은 그 창이 이미 닫혀 있으면 건너뛴다(닫을 것이 없다).
        /// </summary>
        public static string First(GameFlow flow, TutorialLayer layer, string anchor)
        {
            var parts = TutorialLayerAnchor.Parts(anchor);
            foreach (string p in parts)
                if (!TutorialLayerAnchor.IsDismiss(p) || Missing(flow, layer, p) != "못 찾음") return p;
            return parts.Length > 0 ? parts[parts.Length - 1] : null;
        }

        /// <summary>못 찾은 요소 이름 앵커의 까닭(진단 — 없음 · 감춘 조상 · 투명 · 크기 0).</summary>
        public static string Why(GameFlow flow, string part)
        {
            var all = flow.Stage.Root.Query(part).ToList();
            if (all.Count == 0) return "요소 없음";
            var parts = new List<string>();
            foreach (var el in all)
            {
                string why = "보임";
                for (var e = el; e != null; e = e.parent)
                {
                    var rs = e.resolvedStyle;
                    if (rs.display == DisplayStyle.None || e.style.display == DisplayStyle.None) { why = "display none @" + (e.name ?? "?"); break; }
                    if (rs.visibility == Visibility.Hidden || e.style.visibility == Visibility.Hidden) { why = "hidden @" + (e.name ?? "?"); break; }
                    if (rs.opacity < 0.01f) { why = "투명 @" + (e.name ?? "?"); break; }
                    if (float.IsNaN(e.layout.width) && (e.parent == null || !float.IsNaN(e.parent.layout.width))) { why = "배치 없음 @" + (e.name ?? "?") + " 부모 " + (e.parent?.name ?? "-"); break; }
                }
                why += el.panel == null ? " 패널 없음" : "";
                parts.Add(why + " " + el.worldBound);
            }
            return string.Join(" / ", parts);
        }

        static string Path(VisualElement e)
        {
            var names = new List<string>();
            for (int i = 0; e != null && i < 3; e = e.parent, i++) names.Add(string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name);
            return string.Join(" < ", names);
        }

        void CheckAnchor(TutorialLayer layer, TutorialStep step, string key)
        {
            if (step.Anchor == null) return;
            string first = First(Flow, layer, step.Anchor);
            string why = Missing(Flow, layer, first);
            Assert.That(why, Is.Null, $"{key} 앵커 「{first}」: {why}");
            Assert.That(layer.Holes.Count, Is.GreaterThan(0), key + ": 구멍이 없다");
            Assert.That(layer.PartName, Is.EqualTo(first), key + ": 단계가 뜰 때 비추는 부분 = 첫 부분(닫힌 창의 닫기는 빼고)");
            CheckOne(layer, key);
        }

        // 그 순간 그 한 곳만 구멍(결정 99 ① · 07 T180): 구멍 = 지금 비추는 부분 하나의 사각형 그대로(다른 부분은 보여도 구멍이 없다) · 금색 테두리도 그 수만큼
        void CheckOne(TutorialLayer layer, string key)
        {
            if (layer.Part < 0) return;
            var rects = new List<Rect>();
            Assert.That(TutorialLayerAnchor.Find(Flow, layer.PartName, layer.Element, rects, out _), Is.True, $"{key}: 비추는 부분 {layer.PartName}이 화면에 없다");
            Assert.That(layer.Holes.Count, Is.EqualTo(rects.Count), $"{key}: 구멍은 지금 누를 부분 {layer.PartName} 하나의 것만(구멍 {layer.Holes.Count})");
            for (int i = 0; i < rects.Count; i++)
                Assert.That(Vector2.Distance(layer.Holes[i].center, rects[i].center), Is.LessThan(2f), $"{key}: 구멍 {i} = 부분 {layer.PartName}의 자리");
            int rings = 0;
            foreach (var r in layer.Element.Query(className: "sn-tut-ring").ToList())
                if (r.resolvedStyle.display == DisplayStyle.Flex) rings++;
            Assert.That(rings, Is.EqualTo(rects.Count), $"{key}: 금색 테두리 수 = 구멍 수");
            OneChecked++;
        }

        // 누를 요소가 지금 비추는 부분 안인가(앵커 부분 여럿인 단계 — 그 부분만 빛나고 그 부분만 받는다)
        void CheckLit(VisualElement el, string what)
        {
            var layer = Tut.Layer;
            if (layer == null || !layer.Shown || layer.Part < 0 || layer.Element.Contains(el)) return;
            var parts = TutorialLayerAnchor.Parts(layer.Runner.Step.Anchor);
            if (parts.Length < 2) return;
            CheckOne(layer, what);
            var rects = new List<Rect>();
            if (!TutorialLayerAnchor.Find(Flow, layer.PartName, layer.Element, rects, out var lit) || lit == null) return;
            Assert.That(lit == el || lit.Contains(el) || el.Contains(lit), Is.True, $"{what}: 누를 요소가 지금 비추는 부분 {layer.PartName} 밖이다(부분 {layer.Part + 1}/{parts.Length})");
            if (!Lit.Contains(layer.Runner.Script.Id + "." + layer.Runner.Step.Id + " " + layer.PartName)) Lit.Add(layer.Runner.Script.Id + "." + layer.Runner.Step.Id + " " + layer.PartName);
        }

        // 구멍이 있는 단계(또는 [다음] 단계)면 구멍 밖 단추 하나에 보낸 클릭을 층이 삼키고 hint를 띄운다
        void CheckOutside(TutorialLayer layer, TutorialStep step, string key)
        {
            bool nextStep = step.Advance == StepAdvance.Next || step.Advance == StepAdvance.Demo;
            if (!nextStep && layer.Holes.Count == 0) return;
            var parts = new List<VisualElement>();
            TutorialLayerAnchor.Find(Flow, step.Anchor, layer.Element, new List<Rect>(), parts);
            var root = Stage.Root;
            foreach (var b in root.Query<Button>().ToList())
            {
                if (b.name == "battle-result-ok" || !b.enabledInHierarchy || !TutorialLayerAnchor.Visible(b, layer.Element, out var wb)) continue;
                if (parts.Exists(a => a == b || a.Contains(b) || b.Contains(a))) continue;
                bool inHole = false;
                foreach (var h in layer.Holes) inHole |= Rect.MinMaxRect(h.xMin - 16f, h.yMin - 16f, h.xMax + 16f, h.yMax + 16f).Overlaps(wb);
                if (inHole) continue;
                var picked = root.panel.Pick(wb.center);
                if (picked == null || (picked != b && !b.Contains(picked))) continue;
                int hints = layer.Hints;
                var taps = new List<string>();
                var sink = Stage.TapSink;
                Stage.TapSink = k => { taps.Add(k); sink?.Invoke(k); };
                try { Assert.That(Send(picked, b), Is.False, $"{key}: 구멍 밖 단추 {b.name}의 클릭을 층이 삼키지 않았다"); }
                finally { Stage.TapSink = sink; }
                Assert.That(layer.Hints, Is.EqualTo(hints + 1), key + ": 삼킨 클릭은 hint");
                Assert.That(taps, Is.EqualTo(new[] { Sinnoh.Fx.SoundKeys.Select }), key + ": 삼킨 클릭의 소리 = select 한 번(소리 표 행 밖 「튜토리얼 hint」 — M9)");
                Swallowed++;
                return;
            }
        }

        // ── 누르기 ──

        /// <summary>그 이름의 요소가 보일 때까지 기다렸다 누른다(<see cref="Press(VisualElement, string)"/>).</summary>
        public IEnumerator Press(string name, string what)
        {
            VisualElement el = null;
            // 50ms씩 30초까지 — 앞 답이 연 공격 연출 둘(약 7초씩)이 다 돌아야 기술 줄이 다시 선다(E11 S009)
            yield return Until(() => (el = Find(name)) != null && el.enabledInHierarchy, () => $"{what}: 요소 {name}이 보이지 않는다[{Why(Flow, name)}] — {Where()}", 600, 50);
            yield return Press(el, what + " " + name);
        }

        VisualElement Find(string name)
        {
            var layer = Tut.Layer?.Element;   // 층 안의 요소는 층의 단추(tut-layer-*)만
            foreach (var e in Stage.Root.Query(name).ToList())
            {
                if (!TutorialLayerAnchor.Visible(e, null, out _)) continue;
                if (layer != null && layer.Contains(e) && !name.StartsWith("tut-layer", StringComparison.Ordinal)) continue;
                return e;
            }
            return null;
        }

        /// <summary>
        /// 요소 하나를 누른다: 보이고 켜졌고 · 가운데 점의 패널 Pick이 그 요소(또는 그 안 · 그 위 판 — 칸이 마우스를 안 받을 때)이고 · 그 점의 요소로 보낸 ClickEvent가 층을 지나 그 요소에 닿는다.
        /// 닿은 ClickEvent가 <c>stage.OnTap</c> 칸의 동작을 부르고, 키트 단추면 그 단추의 Clickable을 부른다.
        /// </summary>
        public IEnumerator Press(VisualElement el, string what)
        {
            Assert.That(el, Is.Not.Null, what + ": 요소 없음");
            // 사람은 화면이 멈춘 뒤에 누른다 — 앞 클릭이 바꾼 배치(말풍선 글 · 아래 판 자리)가 두 프레임 그대로일 때까지(최대 10프레임)
            for (int i = 0, still = 0; i < 10 && still < 2; i++)
            {
                var before = el.worldBound;
                yield return null;
                still = el.worldBound == before ? still + 1 : 0;
            }
            Assert.That(TutorialLayerAnchor.Visible(el, null, out var wb), Is.True, what + ": 보이지 않는다");
            Assert.That(el.enabledInHierarchy, Is.True, what + ": 꺼져 있다");
            CheckLit(el, what);
            var picked = Stage.Root.panel.Pick(wb.center);
            Assert.That(picked != null && (picked == el || el.Contains(picked) || picked.Contains(el)), Is.True, $"{what}: 가려짐({Path(picked)} {picked?.worldBound} · 누를 요소 {wb} · 자리 {Tut.Layer?.DockAt})");
            var target = picked == el || el.Contains(picked) ? picked : el;
            int hints = Tut.Layer != null ? Tut.Layer.Hints : 0;
            Assert.That(Send(target, el), Is.True, $"{what}: 층이 클릭을 삼켰다(구멍 밖 — {Where()})");
            Assert.That(Tut.Layer == null || Tut.Layer.Hints == hints, Is.True, what + ": hint가 떴다");
            var button = el as Button;
            if (button != null) Invoke(button);
            Clicks++;
            yield return Frames(1);
        }

        /// <summary>
        /// 끌어 놓기 하나(결정 107 ① · 07 T200): from 요소 가운데에서 누르고 → to 요소 가운데까지 여섯 걸음 움직이고 → 뗀다. 포인터 사건(IMGUI 사건으로 만든 PointerDown·Move·Up)을
        /// 그 점의 요소(판이 포인터를 잡았으면 그 판)로 보내 층의 거르기(패널 맨 위 TrickleDown)를 지나야 하고 hint가 없어야 한다. from은 지금 비추는 부분 안이어야 한다.
        /// </summary>
        public IEnumerator Drag(string from, string to, string what)
        {
            VisualElement a = null, b = null;
            yield return Until(() => (a = Find(from)) != null && a.enabledInHierarchy && (b = Find(to)) != null, () => $"{what}: 끌 요소 {from} → {to}이 보이지 않는다 — {Where()}", 600, 50);
            for (int i = 0, still = 0; i < 10 && still < 2; i++)
            {
                var before = a.worldBound;
                yield return null;
                still = a.worldBound == before ? still + 1 : 0;
            }
            a = Find(from);
            b = Find(to);
            Assert.That(a != null && b != null, Is.True, $"{what}: 끌 요소 {from} → {to}");
            CheckLit(a, what + " " + from);
            Vector2 p0 = a.worldBound.center, p1 = b.worldBound.center;
            int hints = Tut.Layer != null ? Tut.Layer.Hints : 0;
            Assert.That(Pointer(EventType.MouseDown, p0), Is.True, $"{what}: 층이 {from} 누르기를 삼켰다({lastReach}) — {Where()}");
            for (int i = 1; i <= 6; i++)
            {
                Assert.That(Pointer(EventType.MouseDrag, Vector2.Lerp(p0, p1, i / 6f)), Is.True, $"{what}: 층이 끌기 움직임을 삼켰다({lastReach})");
                yield return null;
            }
            Assert.That(Pointer(EventType.MouseUp, p1), Is.True, $"{what}: 층이 {to} 위 떼기를 삼켰다({lastReach}) — {Where()}");
            Assert.That(Tut.Layer == null || Tut.Layer.Hints == hints, Is.True, what + ": 끌어 놓기에 hint가 떴다");
            Clicks++;
            Drags++;
            yield return Frames(1);
        }

        // 포인터 사건 하나를 그 점의 요소(패널 Pick — 판이 포인터를 잡았으면 그 판)로 보내고 그 요소가 받았나(층이 패널 맨 위에서 삼키면 false)
        bool Pointer(EventType type, Vector2 at)
        {
            var panel = Stage.Root.panel;
            var target = panel.GetCapturingElement(PointerId.mousePointerId) as VisualElement ?? panel.Pick(at);
            Assert.That(target, Is.Not.Null, $"{at}에 요소가 없다");
            var sys = new Event { type = type, mousePosition = at, button = 0, clickCount = 1 };
            if (type == EventType.MouseDown) return Reach(target, PointerDownEvent.GetPooled(sys));
            if (type == EventType.MouseUp) return Reach(target, PointerUpEvent.GetPooled(sys));
            return Reach(target, PointerMoveEvent.GetPooled(sys));
        }

        // 층의 거르기를 지났나: 패널 맨 위(visualTree의 TrickleDown — 층이 먼저 건 것) 뒤에 건 콜백이나 대상 요소의 콜백이 불리면 층이 멈추지 않은 것(StopImmediatePropagation이면 둘 다 안 불린다).
        // 판이 포인터를 잡으면(capture) 사건은 맨 위를 거치지 않고 그 판에만 간다 — 그때는 대상이 받았나로 본다
        static string lastReach;

        static bool Reach<T>(VisualElement target, T e) where T : EventBase<T>, new()
        {
            int got = 0, at = 0;
            EventCallback<T> seen = _ => got++;
            EventCallback<T> seenAt = _ => at++;
            var top = target.panel.visualTree;
            top.RegisterCallback(seen, TrickleDown.TrickleDown);
            target.RegisterCallback(seenAt, TrickleDown.TrickleDown);
            string name = target.name ?? target.GetType().Name;
            using (e)
            {
                e.target = target;
                target.SendEvent(e);
            }
            top.UnregisterCallback(seen, TrickleDown.TrickleDown);
            target.UnregisterCallback(seenAt, TrickleDown.TrickleDown);
            lastReach = $"{typeof(T).Name} 대상 {name} · 맨 위 {got} · 대상 {at}";
            return got > 0 || at > 0;
        }

        // ClickEvent를 target에 보내고 el이 받았나(층이 패널 맨 위에서 삼키면 false)
        static bool Send(VisualElement target, VisualElement el)
        {
            int got = 0;
            EventCallback<ClickEvent> seen = _ => got++;
            el.RegisterCallback(seen, TrickleDown.TrickleDown);
            using (var e = ClickEvent.GetPooled())
            {
                e.target = target;
                target.SendEvent(e);
            }
            el.UnregisterCallback(seen, TrickleDown.TrickleDown);
            return got > 0;
        }

        static readonly MethodInfo ClickInvoke = typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(EventBase) }, null);

        // 키트 단추를 누른다(그 단추의 Clickable — 마우스가 떼일 때와 같은 동작)
        static void Invoke(Button button)
        {
            Assert.That(ClickInvoke, Is.Not.Null, "Clickable.Invoke(EventBase)를 찾지 못했다(Unity 판이 바뀌었다)");
            using (var e = ClickEvent.GetPooled())
            {
                e.target = button;
                ClickInvoke.Invoke(button.clickable, new object[] { e });
            }
        }

        /// <summary>보드 칸을 마우스 길(<see cref="GameFlow.Pointer(float, float, bool)"/> → <see cref="GameFlow.ClickGate"/>)로 그 칸 가운데를 누른다. 그 점이 그 칸이고 · PC 층에 가려지지 않았고 · 층이 삼키지 않아야 한다.</summary>
        public IEnumerator Cell(TutorialLayer layer, int cell, string what)
        {
            Rect r = default;
            yield return Until(() => Flow.CellRect(cell, out r), () => $"{what}: 칸 {cell}을 화면에 비추지 못한다", 40);
            float s = Stage.Grade.PanelScale, h = Stage.Root.worldBound.height * s;
            float x = r.center.x * s, y = h - r.center.y * s;
            yield return Until(() => Flow.InputOpen && !Flow.OverUi(x, y), () => $"{what}: 칸 {cell}이 화면 요소에 가려졌다({Path(Stage.Root.panel.Pick(r.center))}) — {Where()}", 40);
            int hints = layer.Hints;
            int got = Flow.Pointer(x, y, true);
            Assert.That(got, Is.EqualTo(cell), $"{what}: 칸 {cell} 가운데를 눌렀는데 칸 {got}");
            Assert.That(layer.Hints, Is.EqualTo(hints), $"{what}: 층이 칸 {cell} 누르기를 삼켰다(구멍 밖)");
            Clicks++;
            yield return Frames(1);
        }

        // ── 기다리기 ──

        /// <summary>조건이 설 때까지 세션·연출 시각을 보내며 프레임을 돈다(배치가 서야 앵커를 찾는다).</summary>
        public IEnumerator Until(Func<bool> done, Func<string> what, int max = 600, int ms = 500)
        {
            for (int i = 0; i < max && !done(); i++)
            {
                Flow.Skip(Pace(ms));
                yield return null;
            }
            Assert.That(done(), Is.True, what());
        }

        // 보내는 시각: 단계가 떴는데 앵커를 아직 못 찾았으면 한 프레임(16ms)씩 — 창은 화면 프레임에 열리는데 큰 걸음으로 보내면 층의 「2초 못 찾음」이 세션 시각으로 먼저 찬다
        int Pace(int ms)
        {
            var layer = Tut.Layer;
            return layer != null && layer.Shown && layer.Runner.Step?.Anchor != null && layer.Holes.Count == 0 && layer.Runner.Taken == 0 ? 16 : ms;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }
    }
}
