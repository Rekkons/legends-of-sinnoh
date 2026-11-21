using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Tutorial;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 각본 튜토리얼 층(M6.5 화면 ① — 07 §9-1): 진행기(<see cref="TutorialRunner"/>)와 화면 흐름(<see cref="GameFlow"/>)을 잇고, 문서 루트 맨 위(툴팁 바로 아래)에 덮개 → 마박사(작은 얼굴 — 장 첫 말풍선만 전신, 결정 96 Q2 다) → 봇 턴 띠 → 말풍선을 얹는다.
    /// <list type="bullet">
    /// <item>진행: 단계는 연출 대기열이 비고 입력이 열린 뒤에 띄운다. 끝내는 것은 화면 쪽이다 — next·free = [다음] · command = 받아들여진 지정 행동이 <c>auto</c> 수만큼(<see cref="TutorialRunner.Taken"/>)이고
    /// 연출·정해진 일(AI 답)이 다 지난 뒤 · bot = 띄울 때 봇 답을 풀고(<see cref="TutorialRunner.ReleaseBot"/>) 다 낸 뒤 · demo = [다음]이 진행기 <see cref="TutorialRunner.Play"/>(마박사가 대신 둔다) ·
    /// screen = 그 화면(<see cref="Screens"/>)이 맨 위에 열리면. 끝낼 때 <see cref="TutorialRunner.Finish"/>(대조) — 막 앞 판으로 다시 열면 진행기가 <see cref="GameFlow.Adopt"/>로 새 판에 들어간다.</item>
    /// <item>덮개·구멍: 앵커(<see cref="TutorialLayerAnchor"/>)의 부분 가운데 지금 누를 한 부분에만 둥근 구멍(결정 99 ① · 07 T180 — 그 부분을 누르면 다음 부분으로 옮긴다, <see cref="Part"/>). 마우스 오버는 통과하고, 구멍 밖 클릭만 패널 맨 위에서 삼키고 hint를 띄운다(보드 칸은 <see cref="GameFlow.ClickGate"/>).
    /// 받은 누르기의 떼기는 어디서든 받는다(끌어 놓기 — 결정 107 ①). 앵커를 가리는 보드 창이 떠 있으면 그 창의 [닫기]부터 비추고, 창 닫기를 비추는 동안 [다음]은 없다(<see cref="Guarding"/> — 결정 107 ②).
    /// [다음] 단계(next · demo)는 구멍 안도 막는다. 앵커를 <see cref="LostMs"/> 동안 못 찾으면 가운데 모드 + 경고 로그. 거르개가 막은 명령(<see cref="RejectReason.Tutorial"/>)도 hint.</item>
    /// <item>말풍선: 키트 창틀 판 + 대사(2줄 안쪽) + 단추줄 [다음] · [이 장 건너뛰기 ▶▶] · [튜토리얼 끝내기] — 뒤의 둘은 알림만 낸다(<see cref="SkipPressed"/> · <see cref="QuitPressed"/>, 목차·끝내기 창은 화면 ②).</item>
    /// <item>마박사(결정 96 Q2 다): 말풍선 옆에 작은 얼굴(필드 도트 <see cref="GenAssets.RowanFace"/>)만 — 전신(<see cref="GenAssets.RowanFull"/>)은 장 첫 말풍선(이 층에서 처음 뜨는 단계 · 장 번호가 바뀐 첫 단계)에만(<see cref="FullBody"/>).</item>
    /// <item>봇 턴 띠(좌석이 여럿인 각본 — 확장 장): 봇 좌석의 턴이면 위 띠 바로 아래에 그 봇의 얼굴 상자 + 「용식의 턴」(결정 96 Q5 나 — <c>tut-layer-band-face</c>).</item>
    /// </list>
    /// 프레임마다 <see cref="GameFlow.Ticked"/>로 돈다(실행 중 · 캡처 · 시험이 같은 길). 정적 상태가 없다. 겉모양 [가정 T30~T39](07 §10).
    /// 요소 이름: <c>tut-layer</c>·<c>tut-layer-dim</c>·<c>tut-layer-ring-&lt;n&gt;</c>·<c>tut-layer-dock</c>·<c>tut-layer-rowan</c>·<c>tut-layer-bubble</c>·<c>tut-layer-tail</c>·<c>tut-layer-text</c>·<c>tut-layer-next</c>·<c>tut-layer-skip</c>·<c>tut-layer-quit</c>·<c>tut-layer-band</c>.
    /// </summary>
    public sealed class TutorialLayer
    {
        public const string Name = "tut-layer";

        /// <summary>앵커를 이만큼(세션 ms) 못 찾으면 가운데 모드(07 §9-1 · isekai 07 §6-1).</summary>
        public const int LostMs = 2000;

        /// <summary>
        /// <c>screen:&lt;이름&gt;</c> 단계가 기다릴 수 있는 화면 이름(07 T27 — 화면 <c>IUiScreen.Name</c>, 맨 위 창 <see cref="GameFlow.Top"/>로 본다): 사람이 보드에서 스스로 여는 창들.
        /// 답해야 하는 화면(조우 · 배틀 · 셋업 · 결과)은 단계가 지정 행동으로 다루므로 넣지 않는다.
        /// </summary>
        public static readonly IReadOnlyList<string> Screens = new[]
        {
            "board-log", "board-party", "mon-detail", "board-trainer", "board-village", "board-craft", "board-evolve", "board-fate",
            "board-stamp", "board-shrine", "board-trade", "board-challenge", "board-bless", "board-menu",
        };

        readonly GameFlow flow;
        readonly UiStage stage;
        readonly VisualElement layer, dock, bubble, buttons, band, bandFace;
        readonly TutorialLayerDim dim;
        readonly Label text, bandText;
        readonly Button next, skip, quit;
        readonly Image rowan;
        readonly Tail tail;
        readonly List<VisualElement> rings = new List<VisualElement>();
        readonly List<Rect> holes = new List<Rect>();
        readonly List<VisualElement> anchorElements = new List<VisualElement>();
        VisualElement hooked;
        int shownIndex = -1, seenBlocked, reported, fullIndex = -1, shownChapter = int.MinValue;
        long lastMs, lostMs;
        bool released, warned, ended, stopped, covered;
        bool closing;   // 결과 창을 닫으면 끝나는 [다음] 단계(결정 104 ① — 구멍이 결과 창 [확인]을 품는다): [다음] 단추 없이 「[확인]을 눌러라」로 끝난다
        string[] parts = new string[0];
        int partAt, shownPart = -1, downPart = -1;
        bool moved;
        bool downPassed;   // 받은 누르기의 떼기는 어디서든 받는다 · 그 떼기를 따르는 같은 프레임의 클릭도(upFrame — 결정 107 ① 끌어 놓기)
        int upFrame = -1;
        bool nextHeld;   // [다음] 단계인데 창 닫기 부분을 비추는 중 — 창을 닫기 전에는 [다음]이 없다(결정 107 ②)
        VisualElement guardClose;   // 앵커를 가리는 창의 [닫기](결정 107 ② 안전장치 — 비추는 동안 이것 하나만 구멍)

        TutorialLayer(GameFlow flow)
        {
            this.flow = flow;
            stage = flow.Stage;
            layer = new VisualElement { name = Name, pickingMode = PickingMode.Ignore };
            layer.AddToClassList("sn-tut-layer");
            dim = new TutorialLayerDim();
            layer.Add(dim);
            band = Add(layer, new VisualElement { pickingMode = PickingMode.Ignore }, "tut-layer-band", "sn-tut-band");
            bandFace = Add(band, FaceBox.Make(), "tut-layer-band-face", "sn-tut-band-face");
            bandFace.pickingMode = PickingMode.Ignore;
            bandText = (Label)Add(band, Kit.Text("", Kit.F2), "tut-layer-band-text", null);
            bandText.pickingMode = PickingMode.Ignore;
            dock = Add(layer, new VisualElement { pickingMode = PickingMode.Ignore }, "tut-layer-dock", "sn-tut-dock");
            rowan = (Image)Add(dock, new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore }, "tut-layer-rowan", "sn-tut-rowan");
            bubble = Add(dock, Kit.Panel(stage.Skin), "tut-layer-bubble", "sn-tut-bubble");
            tail = (Tail)Add(bubble, new Tail(), "tut-layer-tail", "sn-tut-tail");
            text = (Label)Add(bubble, Kit.Wrapped("", Kit.F2), "tut-layer-text", "sn-tut-text");
            buttons = Add(bubble, new VisualElement(), "tut-layer-buttons", "sn-tut-buttons");
            next = (Button)Add(buttons, Kit.Button(stage.T("screen.tutorial.layer.next"), KitButtonKind.Main, () => Next()), "tut-layer-next", null);
            skip = (Button)Add(buttons, Kit.Button(stage.T("screen.tutorial.layer.skip"), KitButtonKind.Normal, Skip), "tut-layer-skip", null);
            quit = (Button)Add(buttons, Kit.Button(stage.T("screen.tutorial.layer.quit"), KitButtonKind.Normal, Quit), "tut-layer-quit", null);
            stage.Tips.Hook(skip, null, stage.T("screen.tutorial.layer.skip.tip"), TipSide.Above, reading: true);
            layer.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 화면 흐름에 넘긴 진행기(<see cref="TutorialRunner.Hand"/> — 화면 흐름이 그 판에 들어가 있다)에 층을 붙여 지금 단계부터 띄운다. 끝나거나 그만두면(<see cref="Stop"/>) 층을 걷는다.
        /// 여는 곳은 화면 ② <see cref="TutorialMenu"/> 한 곳이다(장 고르기 · 건너뛰기 · 이어 여는 각본 · 확인용 <see cref="TutorialMenu.Play"/> — 07 T60).
        /// </summary>
        internal static TutorialLayer Open(GameFlow flow, TutorialRunner runner)
        {
            if (flow == null) throw new ArgumentNullException(nameof(flow));
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (runner.Session == null || flow.Session != runner.Session) throw new InvalidOperationException("화면 흐름이 진행기의 판에 들어가 있지 않다(TutorialRunner.Hand 뒤에 연다)");
            var t = new TutorialLayer(flow) { Runner = runner };
            flow.ClickGate = t.PassAt;
            flow.KeyGate = t.PassKey;
            flow.Ticked += t.Tick;
            t.lastMs = flow.NowMs;
            t.Tick();
            return t;
        }

        /// <summary>진행기(판 · 단계 · 대조 어긋남).</summary>
        public TutorialRunner Runner { get; private set; }

        /// <summary>[이 장 건너뛰기 ▶▶]를 눌렀다(값 = 지금 장). 빨리감기는 화면 ②가 잇는다 — 층은 알리기만 한다.</summary>
        public event Action<int> SkipPressed;

        /// <summary>[튜토리얼 끝내기]를 눌렀다. 끝내기 창(07 §2-2)은 화면 ②가 잇는다 — 층은 알리기만 한다.</summary>
        public event Action QuitPressed;

        /// <summary>각본의 마지막 단계가 끝났다(층은 걷혔다). 본편 끝 창 · [목차로]는 화면 ②.</summary>
        public event Action Ended;

        /// <summary>층 뿌리 요소(<c>tut-layer</c>).</summary>
        public VisualElement Element => layer;

        /// <summary>말풍선이 지금 단계를 보이고 있다(대기열이 비기 전·단계 사이면 false).</summary>
        public bool Shown => shownIndex >= 0 && shownIndex == Runner.Index && !ended;

        /// <summary>지금 말풍선의 글(태그 그대로, 줄 잇기 문자 없이).</summary>
        public string ShownText { get; private set; }

        /// <summary>지금 단계의 장(끝났으면 −1).</summary>
        public int Chapter => Runner.Step != null ? Runner.Step.Chapter : -1;

        /// <summary>앵커를 못 찾아 가운데 모드로 바꿨다(지금 단계).</summary>
        public bool Center { get; private set; }

        /// <summary>지금 구멍(패널 좌표 — 지금 누를 앵커 부분 하나의 사각형, 결정 99 ① · 07 T180).</summary>
        public IReadOnlyList<Rect> Holes => holes;

        /// <summary>지금 비추는 앵커 부분(<see cref="TutorialLayerAnchor.Parts"/>의 색인 — 구멍이 없거나 가리는 창의 [닫기]를 비추면 −1). 한 번에 한 부분만 비춘다(결정 99 ①).</summary>
        public int Part => holes.Count > 0 && guardClose == null ? shownPart : -1;

        /// <summary>지금 비추는 앵커 부분의 글(가리는 창의 [닫기]를 비추면 그 이름 · 없으면 null).</summary>
        public string PartName => guardClose != null ? guardClose.name : Part >= 0 ? parts[shownPart] : null;

        /// <summary>
        /// 앵커를 가리는 창의 [닫기]를 비추는 중(결정 107 ② 안전장치 · 07 T201): 사람이 닫는 보드 창(<see cref="Screens"/>)이 맨 위에 떠 있는데 지금 비출 부분이 그 창 밖(또는 없음)이면
        /// 앵커 대신 그 창의 <c>&lt;창&gt;-close</c>를 비춘다 — 닫으면 앵커로 돌아간다. [다음] 단계면 그동안 [다음]이 없다.
        /// </summary>
        public bool Guarding => Shown && guardClose != null;

        /// <summary>[다음] 단추가 지금 떠 있다(next · free · demo 단계 · 결과 창 [확인]을 기다리지 않고 · 창 닫기 부분을 비추지 않을 때 — 결정 104 ① · 107 ②).</summary>
        public bool NextShown => Shown && next.style.display == DisplayStyle.Flex;

        /// <summary>hint를 띄운 수(구멍 밖 클릭 · 거르개가 막은 명령 — 단계가 바뀌어도 센다).</summary>
        public int Hints { get; private set; }

        /// <summary>지금 hint를 띄우고 있다(말풍선이 hint 글이거나 hint 표시).</summary>
        public bool Hinted { get; private set; }

        /// <summary>지금 말풍선 옆의 마박사가 전신이다(장 첫 말풍선 — 결정 96 Q2 다). 아니면 작은 얼굴.</summary>
        public bool FullBody { get; private set; }

        /// <summary>봇 턴 띠의 얼굴 상자에 그려진 인물(키 — 그림이 없으면 null).</summary>
        public string BandFace => FaceBox.Shown(bandFace);

        /// <summary>봇 턴 띠가 떠 있다(그 글은 <see cref="BandText"/>).</summary>
        public bool BandShown => layer.style.display == DisplayStyle.Flex && band.style.display == DisplayStyle.Flex;

        public string BandText => bandText.text;

        /// <summary>마박사·말풍선 자리(07 T31 · T133): 아래 · 위 · 가운데 · 오른쪽 아래 · 오른쪽 위.</summary>
        public string DockAt => dock.ClassListContains("sn-tut-dock-center") ? "center"
            : (dock.ClassListContains("sn-tut-dock-right") ? "right-" : "") + (dock.ClassListContains("sn-tut-dock-high") ? "high" : "low");

        /// <summary>층을 걷고 흐름에서 뗀다(진행기는 그대로 — 판은 화면 흐름에 남는다).</summary>
        public void Stop()
        {
            if (stopped) return;
            stopped = true;
            flow.Ticked -= Tick;
            if (flow.ClickGate == (Func<Vector2, bool>)PassAt) flow.ClickGate = null;
            if (flow.KeyGate == (Func<VisualElement, bool>)PassKey) flow.KeyGate = null;
            Unhook();
            layer.RemoveFromHierarchy();
        }

        /// <summary>[다음](next · free · demo 단계): 단계를 끝낸다. 받았으면 true.</summary>
        public bool Next()
        {
            var step = Runner.Step;
            if (!Shown || step == null || closing || nextHeld) return false;   // 닫아야 하는 창이 열려 있으면 [다음]이 그 창을 지나치지 않는다(결정 107 ②)
            switch (step.Advance)
            {
                case StepAdvance.Next:
                case StepAdvance.Free:
                    Finish();
                    return true;
                case StepAdvance.Demo:
                    try { Runner.Play(); }   // 마박사가 대신 둔다(07 T19) — 진행기가 auto를 내고 단계를 끝낸다
                    catch (InvalidOperationException e) { Debug.LogException(e); }
                    return true;
                default: return false;
            }
        }

        /// <summary>확인·캡처용: 지금 비추는 부분을 누른 것으로 치고 다음 부분으로 옮긴다(사람의 클릭이 지나는 것과 같은 길). 마지막 부분 · 칸 부분이면 false.</summary>
        public bool PressPart()
        {
            if (!Shown || shownPart < 0) return false;
            int from = shownPart;
            moved = false;
            MoveOn(from + 1);
            return partAt == from + 1 && moved;
        }

        /// <summary>[이 장 건너뛰기 ▶▶]: <see cref="SkipPressed"/>를 지금 장으로 알린다(층·진행기는 그대로).</summary>
        public void Skip()
        {
            if (Shown) SkipPressed?.Invoke(Chapter);
        }

        /// <summary>[튜토리얼 끝내기]: <see cref="QuitPressed"/>를 알린다(층·진행기는 그대로).</summary>
        public void Quit()
        {
            if (Shown) QuitPressed?.Invoke();
        }

        /// <summary>패널 점(왼쪽 위 (0, 0))의 클릭을 받나(보드 칸 · 좌석 칩 — <see cref="GameFlow.ClickGate"/>). 안 받으면 hint를 띄운다.</summary>
        public bool PassAt(Vector2 point)
        {
            if (Passes(point, null, Gate)) return true;
            Hint(true);
            return false;
        }

        /// <summary>단축키로 누르는 요소(배틀 독 — <see cref="GameFlow.KeyGate"/>, 결정 164)를 받나: 그 요소의 가운데를 누른 클릭과 같은 판정(받으면 다음 부분으로 옮긴다). 안 받으면 hint를 띄운다.</summary>
        public bool PassKey(VisualElement target)
        {
            if (target == null) return false;
            if (Passes(target.worldBound.center, target, Click)) return true;
            Hint(true);
            return false;
        }

        // 클릭 판정: 말풍선은 늘 · 단계가 떠 있지 않으면 늘 · 게임 메뉴·끝내기 창·끝 창이 떠 있으면 늘(층을 숨긴다 — 07 T63) · 배틀 결과 창 [확인]은 늘(T63) · [다음] 단계는 말풍선과 앵커의 창 닫기 단추만(T130) ·
        // 구멍이 있으면 구멍(또는 앵커 요소) 안만 · 구멍이 없는 입력 단계(앵커 없음 · 가운데 모드)는 모두
        // 지금 비추는 한 부분만 받는다(결정 99 ① · 07 T180): 그 부분을 누르면(뗄 때 · ClickEvent) 다음 부분으로 옮긴다 — 칸 부분과 마지막 부분은 옮기지 않는다.
        // 끌어 놓기(누른 부분 → 바로 다음 부분에서 뗌)는 떼는 것도 받는다
        const int Gate = -1, Down = 0, Up = 1, Click = 2;

        // 받은 누르기(구멍 안 · 말풍선 …)의 떼기는 어디서 떼든 받는다(결정 107 ① · 07 T200): 끌기는 판이 포인터를 잡아(capture) 떼기의 대상이 판이 되고, 놓는 칸이 구멍 밖일 수도 있다.
        // 떼기만으로는 단추가 눌리지 않고(누른 요소에서 떼야 클릭), 그 떼기 뒤의 클릭은 누른 자리의 요소(또는 그 위)에 온다 — 구멍 밖 단추를 누르는 길은 그대로 막힌다
        bool Passes(Vector2 point, VisualElement target, int kind)
        {
            if (kind == Click && upFrame == Time.frameCount)
            {
                upFrame = -1;
                return true;
            }
            bool held = kind == Up && downPassed;
            if (kind == Down || kind == Up)
            {
                downPassed = false;
                upFrame = -1;
            }
            bool pass = Decide(point, target, kind);
            if (kind == Down) downPassed = pass;
            if (held) upFrame = Time.frameCount;   // 그 떼기를 따라 같은 프레임에 오는 클릭(누른 자리의 요소)도 받는다 — 뗀 사이에 부분을 옮겨도 hint가 뜨지 않게
            return pass || held;
        }

        bool Decide(Vector2 point, VisualElement target, int kind)
        {
            if (stopped || ended || !Shown || flow.MenuUp) return true;
            if (target != null && (target == bubble || bubble.Contains(target))) return true;
            for (var e = target; e != null; e = e.parent)
                if (e.name == ResultOk || e.name == BoardHud.Rotate) return true;   // 보드 돌리기 단추(결정 164)는 Q · E처럼 늘 — 카메라만 바꾼다
            var step = Runner.Step;
            bool nextStep = step.Advance == StepAdvance.Next || step.Advance == StepAdvance.Demo;
            bool hit = false;
            if (target != null)
                foreach (var a in anchorElements)
                    if ((target == a || a.Contains(target)) && (!nextStep || TutorialLayerAnchor.IsDismiss(a.name))) hit = true;   // [다음] 단계는 앵커의 창 닫기 단추만(07 T130)
            if (!hit && !nextStep)
            {
                if (holes.Count == 0) return true;
                float gap = dim.Gap;
                foreach (var h in holes)
                    if (Rect.MinMaxRect(h.xMin - gap, h.yMin - gap, h.xMax + gap, h.yMax + gap).Contains(point)) hit = true;
            }
            if (hit)
            {
                if (guardClose != null) return true;   // 가리는 창의 [닫기]: 닫히면 앵커로 돌아간다(부분을 옮기지 않는다)
                if (kind == Down) downPart = shownPart;
                else if ((kind == Up && downPart == shownPart) || kind == Click) MoveOn(shownPart + 1);
                return true;
            }
            // 끌어 놓기: 지금 부분에서 누르고 바로 다음 부분에서 뗀다(가방의 지닌물건 → 파티 칸). 판이 포인터를 잡으면 떼기의 대상은 판이라 자리로도 본다(결정 107 ①)
            if (kind == Up && !nextStep && guardClose == null && downPart >= 0 && downPart == shownPart && downPart + 1 < parts.Length)
            {
                var rects = new List<Rect>();
                if (TutorialLayerAnchor.Find(flow, parts[downPart + 1], layer, rects, out var drop)
                    && (rects.Exists(r => r.Contains(point)) || (drop != null && target != null && (target == drop || drop.Contains(target)))))
                {
                    MoveOn(downPart + 1);
                    return true;
                }
            }
            return false;
        }

        // 다음 부분으로(한 프레임에 한 번 — 뗄 때 옮겼으면 뒤따르는 ClickEvent는 다시 옮기지 않는다). 칸 부분은 같은 칸을 두 번 누르므로(고르기 → 걷기) 옮기지 않는다
        void MoveOn(int to)
        {
            downPart = -1;
            if (moved || shownPart < 0 || to >= parts.Length || parts[shownPart].StartsWith(TutorialLayerAnchor.CellPrefix, StringComparison.Ordinal)) return;
            partAt = to;
            moved = true;
        }

        // 지금 비출 부분 하나를 찾는다(결정 99 ①): 닫힌 창의 닫기 부분은 건너뛰고(07 T130), 누를 차례의 부분이 안 보이면(앞 클릭이 헛돌았다 · 화면 함수로 지나갔다) 그 앞의 보이는 부분 → 뒤의 보이는 부분
        int Resolve(List<Rect> rects, List<VisualElement> els)
        {
            rects.Clear();
            els.Clear();
            if (parts.Length == 0) return -1;
            // 닫기 부분은 그 창이 떠 있는 동안만 — 건너뛴 것을 기억하지 않는다(단계가 뜬 뒤에 그 창이 다시 서면 닫기부터 비춘다, E14 S022)
            int at = Math.Min(partAt, parts.Length - 1);
            while (at < parts.Length - 1 && TutorialLayerAnchor.IsDismiss(parts[at]) && !One(at, rects, els)) at++;
            if (One(at, rects, els)) return at;
            for (int i = at - 1; i >= 0; i--)
                if (!TutorialLayerAnchor.IsDismiss(parts[i]) && One(i, rects, els)) return i;
            for (int i = at + 1; i < parts.Length; i++)
                if (One(i, rects, els)) return i;
            return -1;
        }

        // 앵커를 가리는 창(결정 107 ② 안전장치 · 07 T201): 사람이 닫는 보드 창(Screens)이 맨 위에 떠 있고 그 [닫기]가 보이는데, 지금 비출 부분을 못 찾았거나 그 창 밖(보드 칸 · HUD — 창이 떠 있는 동안은
        // 보드 입력이 닫힌다 GameFlow.InputOpen)이면 그 [닫기]. 앞 단계에서 창을 닫지 않고 넘어왔거나(단축키 · [다음]) 단계 중에 창을 열어도 진행이 막히지 않게 한다. 앵커 없는 단계 · 그 창을 기다리는 screen 단계 · 그 창 안의 부분은 그대로
        VisualElement Blocker(TutorialStep step, int part, List<VisualElement> els)
        {
            var top = flow.Top;
            if (parts.Length == 0 || top == null || !Closable(top.Name) || (step.Advance == StepAdvance.Screen && step.Screen == top.Name)) return null;
            var window = stage.Root.Q(top.Name);
            if (window == null || window.panel == null) return null;
            if (part >= 0 && els.Count > 0 && (els[0] == window || window.Contains(els[0]))) return null;
            foreach (var c in stage.Root.Query(top.Name + "-close").ToList())
                if (window.Contains(c) && TutorialLayerAnchor.Visible(c, layer, out _)) return c;
            return null;
        }

        static bool Closable(string name)
        {
            foreach (string s in Screens)
                if (s == name) return true;
            return false;
        }

        bool One(int i, List<Rect> rects, List<VisualElement> els)
        {
            els.Clear();
            if (!TutorialLayerAnchor.Find(flow, parts[i], layer, rects, out var el)) return false;
            if (el != null) els.Add(el);
            return true;
        }

        /// <summary>화면 글의 언어가 바뀌었다(<see cref="GameFlow.Lang"/> — 게임 메뉴의 설정): 단추 글과 지금 말풍선(대사 · hint)을 새 표의 글로. 봇 턴 띠는 다음 그림에서.</summary>
        internal void Relabel()
        {
            next.text = stage.T("screen.tutorial.layer.next");
            skip.text = stage.T("screen.tutorial.layer.skip");
            quit.text = stage.T("screen.tutorial.layer.quit");
            var step = Runner.Step;
            if (!Shown || step == null) return;
            SetText(stage.T(Hinted && step.Hint != null ? step.Hint : step.Text));
        }

        // 소리(소리 표 행 밖 「튜토리얼 hint」 — M9 [가정 plan §5 #391]): 층이 삼킨 누름 한 번에 select 한 번. 거르개가 막은 명령은 그 단추가 이미 확정 소리를 냈다
        void Hint(bool sound)
        {
            if (sound) stage.Tap(SoundKeys.Select);
            Hints++;
            Hinted = true;
            var step = Runner.Step;
            if (step != null && step.Hint != null) SetText(stage.T(step.Hint));
            bubble.AddToClassList("sn-tut-hinted");
        }

        // ── 프레임 ──

        void Tick()
        {
            if (stopped) return;
            long dt = Math.Max(0, flow.NowMs - lastMs);
            lastMs = flow.NowMs;
            Attach();
            Report();
            if (Runner.Finished)
            {
                if (!ended)
                {
                    ended = true;
                    layer.style.display = DisplayStyle.None;
                    Stop();
                    Ended?.Invoke();
                }
                return;
            }
            var step = Runner.Step;
            if (shownIndex != Runner.Index) Hide();
            if (Runner.Blocked != seenBlocked)
            {
                seenBlocked = Runner.Blocked;
                if (Shown)
                {
                    Hint(false);   // 거르개가 막은 명령(RejectReason.Tutorial) — 소리는 그 단추의 확정 소리뿐
                    partAt = 0;   // 잘못 고른 것 — 첫 부분부터 다시 비춘다(07 T180)
                }
            }
            if (!Shown)
            {
                // 정해진 일(AI·UB 답)이 남았으면 그 연출 뒤에 — 그 사이에 뜬 단계의 창이 다음 연출에 닫히지 않게(07 T131).
                // 배틀 결과 창이 떠 있는 동안에는 그 창을 말하는 단계만(결정 104 ① · T190) — 나머지는 [확인]으로 창을 닫은 뒤에 뜬다
                if (Idle && Quiet && (!flow.BattleOver || TutorialLayerAnchor.InResult(step.Anchor)))
                {
                    Show(step);
                    dt = 0;   // 단계가 뜨기 전의 시간은 앵커를 못 찾은 시간이 아니다
                }
            }
            else Progress(step);
            // 게임 메뉴(설정 포함)·끝내기 창·끝 창이 떠 있는 동안은 층을 숨긴다 — 말풍선이 메뉴를 덮지 않게, 메뉴 단추를 거르지 않게(07 T63). 숨은 동안은 앵커를 찾지 않는다(못 찾은 시간을 세지 않는다)
            bool cover = flow.MenuUp;
            if (Shown && Runner.Step == step && !cover)
            {
                Place(step, dt);
                Band();
            }
            if (cover) layer.style.display = DisplayStyle.None;
            else if (covered && Shown) layer.style.display = DisplayStyle.Flex;
            covered = cover;
        }

        /// <summary>결과 창을 닫으면 끝나는 단계다(결정 104 ① — [다음] 단추가 없다. 배틀 결과 창의 [확인]이 이 단계를 끝낸다).</summary>
        public bool Closing => Shown && closing;

        // 연출 대기열이 비고 클릭 막기 시간이 지났다(내 입력이 열릴 때와 같다) — 배틀 결과 창이 떠 대기열이 [확인]을 기다리는 동안도(결과 창을 가리키는 단계 · 07 T131 · T190)
        bool Idle => flow.Fx == null || flow.Fx.Sequencer.InputOpen || flow.BattleOver;

        // 정해진 일(가상 응답자 · 풀어 준 봇 답)이 남지 않았다 — 진행기의 Finish가 세션을 앞당기지 않게
        bool Quiet => Runner.Session.NextDueMs == long.MaxValue;

        void Progress(TutorialStep step)
        {
            switch (step.Advance)
            {
                case StepAdvance.Next:
                    if (closing && !flow.BattleOver) Finish();   // 결과 창 [확인]을 눌렀다(결정 104 ①)
                    break;
                case StepAdvance.Command:
                    if (Runner.Taken >= Math.Max(1, step.Auto.Count) && Idle && Quiet) Finish();
                    break;
                case StepAdvance.Bot:
                    if (!released)
                    {
                        released = true;
                        Runner.ReleaseBot();
                    }
                    else if (Runner.BotDone && Idle && Quiet) Finish();
                    break;
                case StepAdvance.Screen:
                    if (flow.Top != null && flow.Top.Name == step.Screen) Finish();
                    break;
            }
        }

        void Finish()
        {
            try { Runner.Finish(); }
            catch (InvalidOperationException e) { Debug.LogException(e); }
            Hide();
        }

        void Hide()
        {
            shownIndex = -1;
            ShownText = null;
            Hinted = false;
            bubble.RemoveFromClassList("sn-tut-hinted");
            layer.style.display = DisplayStyle.None;
        }

        void Show(TutorialStep step)
        {
            shownIndex = Runner.Index;
            released = false;
            warned = false;
            Center = false;
            lostMs = 0;
            dockScore.Clear();
            dockMode = null;
            anchorElements.Clear();
            holes.Clear();
            // 결과 창 단계인데 결과 창이 없으면(확인용 화면 함수로 지나갔다) 앵커 없는 단계로 띄운다 — 못 찾은 앵커로 세지 않는다
            parts = TutorialLayerAnchor.InResult(step.Anchor) && !flow.BattleOver ? new string[0] : TutorialLayerAnchor.Parts(step.Anchor);
            closing = nextHeld = false;
            guardClose = null;
            partAt = 0;
            shownPart = downPart = -1;
            SetText(stage.T(step.Text));
            bool nextStep = step.Advance == StepAdvance.Next || step.Advance == StepAdvance.Free || step.Advance == StepAdvance.Demo;
            next.style.display = nextStep ? DisplayStyle.Flex : DisplayStyle.None;
            // 마박사(결정 96 Q2 다): 작은 얼굴만 — 전신은 장 첫 말풍선(이 층에서 처음 뜨는 단계 · 장 번호가 바뀐 첫 단계)에만
            if (step.Chapter != shownChapter) fullIndex = Runner.Index;
            shownChapter = step.Chapter;
            FullBody = fullIndex == Runner.Index;
            dock.EnableInClassList("sn-tut-dock-face", !FullBody);
            var tex = GenAssets.Load<Texture2D>(FullBody ? GenAssets.RowanFull : GenAssets.RowanFace);
            if (tex != null)
            {
                int k = Math.Max(1, stage.Grade.TokenHeight / 360);   // 07 T29: 720 ×2 · 1080 ×3
                rowan.image = tex;
                rowan.style.width = tex.width * k;
                rowan.style.height = tex.height * k;
                rowan.style.display = DisplayStyle.Flex;
            }
            else rowan.style.display = DisplayStyle.None;
            layer.style.display = DisplayStyle.Flex;
            Place(step, 0);
            Band();
        }

        void SetText(string line)
        {
            ShownText = line;
            text.text = KoText.JoinWords(line);
        }

        // 앵커를 다시 찾아 구멍·테두리·마박사 자리를 맞춘다(카메라·배치가 움직이면 따라간다)
        void Place(TutorialStep step, long dt)
        {
            var found = new List<Rect>();
            var els = new List<VisualElement>();
            int part = Resolve(found, els);   // 지금 누를 한 부분만(결정 99 ①)
            var close = Blocker(step, part, els);   // 앵커를 가리는 창이 떠 있으면 그 창의 [닫기]부터(결정 107 ②)
            if (close != null)
            {
                found.Clear();
                els.Clear();
                found.Add(close.worldBound);
                els.Add(close);
            }
            bool ok = part >= 0 || close != null;
            moved = false;
            if (part != shownPart || close != guardClose)   // 비추는 부분이 바뀌었다 — 마박사·말풍선 자리를 다시 해 본다(07 T133)
            {
                dockScore.Clear();
                dockMode = null;
                downPart = -1;
            }
            shownPart = part;
            guardClose = close;
            holes.Clear();
            anchorElements.Clear();
            if (ok)
            {
                holes.AddRange(found);
                anchorElements.AddRange(els);
                lostMs = 0;   // 2초는 잇달아 못 찾은 시간(07 T39)
            }
            else if (parts.Length > 0 && !Center && Runner.Taken == 0)   // 지정 행동을 받은 뒤 그 창이 닫히며 앵커가 사라진 것(연출을 기다리는 중)은 세지 않는다
            {
                lostMs += dt;
                if (lostMs >= LostMs)
                {
                    Center = true;
                    if (!warned)
                    {
                        warned = true;
                        Debug.LogWarning($"[튜토리얼] {Runner.Script.Id}.{step.Id}: 앵커 「{step.Anchor}」를 {LostMs}ms 동안 찾지 못해 가운데 모드로 띄운다");
                    }
                }
            }
            // 결과 창을 닫으면 끝나는 단계(결정 104 ①): [다음] 단계의 구멍이 결과 창 [확인]을 품으면 [다음] 단추를 감추고 [확인]을 기다린다
            if (!closing && step.Advance == StepAdvance.Next && flow.BattleOver)
                foreach (var a in anchorElements)
                    if (a.name == ResultOk || a.Q(ResultOk) != null)
                    {
                        closing = true;
                        next.style.display = DisplayStyle.None;
                    }
            // 닫아야 하는 창이 열린 채로는 [다음]이 없다(결정 107 ② · 07 T201): 창 닫기 부분(…-close)을 비추거나 가리는 창의 [닫기]를 비추는 동안 — 닫으면 다시 선다
            if (!closing && (step.Advance == StepAdvance.Next || step.Advance == StepAdvance.Free || step.Advance == StepAdvance.Demo))
            {
                nextHeld = close != null || (part >= 0 && parts[part] != ResultOk && TutorialLayerAnchor.IsDismiss(parts[part]));
                next.style.display = nextHeld ? DisplayStyle.None : DisplayStyle.Flex;
            }
            bool nextStep = step.Advance == StepAdvance.Next || step.Advance == StepAdvance.Demo;
            // 덮개는 구멍이 있을 때 · [다음] 단계일 때만(구멍 없는 입력 단계에 화면을 덮지 않는다) [가정 T32]
            dim.style.display = holes.Count > 0 || nextStep ? DisplayStyle.Flex : DisplayStyle.None;
            dim.Set(holes);
            for (int i = 0; i < Math.Max(rings.Count, holes.Count); i++)
            {
                if (i >= rings.Count)
                {
                    var ring = new VisualElement { name = "tut-layer-ring-" + i, pickingMode = PickingMode.Ignore };
                    ring.AddToClassList("sn-tut-ring");
                    layer.Insert(layer.IndexOf(dim) + 1, ring);
                    rings.Add(ring);
                }
                var r = rings[i];
                if (i >= holes.Count)
                {
                    r.style.display = DisplayStyle.None;
                    continue;
                }
                var at = dim.Local(holes[i]);
                r.style.display = DisplayStyle.Flex;
                r.style.left = at.x;
                r.style.top = at.y;
                r.style.width = at.width;
                r.style.height = at.height;
            }
            // 마박사·말풍선 자리 [가정 T31]: 구멍이 있으면 구멍이 화면 아래 절반이면 위 · 아니면 아래, 구멍 없는 [다음] 단계(앵커 없음 · 가운데 모드)는 가운데, 구멍 없는 입력 단계는 위
            string at2;
            float height = layer.layout.height;
            if (holes.Count > 0)
            {
                float y = 0f;
                foreach (var h in holes) y = Math.Max(y, h.center.y);
                at2 = !float.IsNaN(height) && height > 0f && y > height * 0.5f ? "high" : "low";
            }
            else at2 = nextStep ? "center" : step.Advance == StepAdvance.Bot ? "low" : "high";
            // 그 자리의 마박사·말풍선이 구멍을 덮으면 다음 자리를 해 본다(07 T133): 위·아래 → 오른쪽 아래 → 오른쪽 위, 다 덮으면 가장 덜 덮는 자리
            at2 = holes.Count > 0 ? TryDock(at2 == "high" ? HighFirst : LowFirst) : (dockMode = at2);
            dock.EnableInClassList("sn-tut-dock-high", at2.EndsWith("high", StringComparison.Ordinal));
            dock.EnableInClassList("sn-tut-dock-center", at2 == "center");
            dock.EnableInClassList("sn-tut-dock-right", at2.StartsWith("right", StringComparison.Ordinal));
            tail.Right = at2.StartsWith("right", StringComparison.Ordinal);
            // 아래 자리는 아래 판(보드 HUD · 배틀 독) 바로 위에 선다 — 판의 단추·파티 칸을 덮지 않게. 판이 없으면 USS 자리
            float floor = float.MaxValue;
            foreach (string name in Floors)
                if (TutorialLayerAnchor.Visible(stage.Root.Q(name), layer, out var wb)) floor = Math.Min(floor, layer.WorldToLocal(new Vector2(0f, wb.yMin)).y);
            if (at2.EndsWith("low", StringComparison.Ordinal) && floor < float.MaxValue && !float.IsNaN(height) && height > 0f) dock.style.bottom = Mathf.Round(height - floor);
            else dock.style.bottom = StyleKeyword.Null;
        }

        static readonly string[] LowFirst = { "low", "high", "right-low", "right-high" }, HighFirst = { "high", "low", "right-low", "right-high" };
        readonly Dictionary<string, float> dockScore = new Dictionary<string, float>();
        string dockMode;

        // 지금 자리(앞 프레임에 건 것 — 배치가 그 자리로 섰을 때만 잰다)가 구멍을 덮으면 아직 안 해 본 자리로, 다 해 봤으면 가장 덜 덮는 자리로
        string TryDock(string[] modes)
        {
            if (dockMode == null || Array.IndexOf(modes, dockMode) < 0) return dockMode = modes[0];
            if (!Laid(dockMode)) return dockMode;
            float over = Covering();
            dockScore[dockMode] = over;
            if (over <= 0f) return dockMode;
            foreach (string m in modes)
                if (!dockScore.ContainsKey(m)) return dockMode = m;
            foreach (string m in modes)
                if (dockScore[m] < dockScore[dockMode]) dockMode = m;
            return dockMode;
        }

        // 배치가 그 자리의 클래스로 섰나(USS가 자리마다 다른 정렬·방향을 준다)
        bool Laid(string mode)
        {
            var rs = dock.resolvedStyle;
            var align = mode.EndsWith("high", StringComparison.Ordinal) ? Align.FlexStart : mode == "center" ? Align.Center : Align.FlexEnd;
            var dir = mode.StartsWith("right", StringComparison.Ordinal) ? FlexDirection.RowReverse : FlexDirection.Row;
            return rs.alignItems == align && rs.flexDirection == dir && !float.IsNaN(bubble.layout.width);
        }

        // 마박사·말풍선이 구멍(틈 포함)을 덮는 넓이
        float Covering()
        {
            float sum = 0f, gap = dim.Gap;
            foreach (var h in holes)
            {
                var r = Rect.MinMaxRect(h.xMin - gap, h.yMin - gap, h.xMax + gap, h.yMax + gap);
                sum += Area(r, bubble.worldBound);
                if (rowan.resolvedStyle.display == DisplayStyle.Flex) sum += Area(r, rowan.worldBound);
            }
            return sum;
        }

        static float Area(Rect a, Rect b)
        {
            float w = Math.Min(a.xMax, b.xMax) - Math.Max(a.xMin, b.xMin), h = Math.Min(a.yMax, b.yMax) - Math.Max(a.yMin, b.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        // 배틀 결과 창 [확인](07 T63): 어느 단계에서나 받는다 — 결과 창을 닫지 못하면 그 뒤 단계의 앵커(보드 HUD)가 가려진다
        const string ResultOk = "battle-result-ok";

        // 마박사가 그 위에 서는 아래 판(보드 HUD 아래 판 · 배틀 독)
        static readonly string[] Floors = { "board-hud-dock", "battle-dock" };

        // 봇 턴 띠(07 §9-1 — 확장 장만): 좌석이 여럿인 각본에서 bot 단계(그 봇 좌석)이거나 봇 좌석의 턴이면 위 띠 바로 아래에(셋업 밖) [가정 T34]
        void Band()
        {
            var view = flow.Client != null ? flow.Client.View : null;
            var step = Runner.Step;
            int seat = view != null && view.Phase != Phase.Setup && view.Phase != Phase.GameOver && Runner.Script.Start.Seats > 1
                ? step != null && step.Advance == StepAdvance.Bot ? step.BotSeat : view.ActiveSeat : -1;
            if (seat <= TutorialRunner.Me || seat >= view.Seats.Length)
            {
                band.style.display = DisplayStyle.None;
                return;
            }
            FaceBox.Paint(bandFace, seat, flow.SeatColor(seat), flow.Protagonist(seat));   // 그 봇의 얼굴(결정 96 Q5 나 — 좌석 칩과 같은 인물)
            string who = stage.TOrNull("screen.tutorial.layer.bot." + seat) ?? flow.SeatName(seat);
            bandText.text = stage.T("screen.tutorial.layer.bot_turn").Replace("{name}", who);
            band.style.display = DisplayStyle.Flex;
            var top = stage.Root.Q("board-hud-top");
            if (TutorialLayerAnchor.Visible(top, layer, out var wb)) band.style.top = layer.WorldToLocal(new Vector2(0f, wb.yMax)).y;
            else band.style.top = StyleKeyword.Null;
        }

        // 문서 루트 맨 위(툴팁 바로 아래 — 끝내기 창·끝 창이 떠 있으면 그 창 바로 아래)에 둔다 — 뿌리를 다시 받거나 윗층(연출 덮개)이 나중에 붙어도 늘 위로
        void Attach()
        {
            var root = stage.Root;
            if (root == null) return;
            var tips = stage.Tips != null ? stage.Tips.Element : null;
            var dialog = flow.Tutorial.Dialog;
            var above = dialog != null && dialog.parent == root ? dialog : tips;
            if (layer.parent != root) root.Add(layer);
            if (above != null && above.parent == root)
            {
                if (root.IndexOf(layer) != root.IndexOf(above) - 1) layer.PlaceBehind(above);
            }
            else if (root.IndexOf(layer) != root.childCount - 1) layer.BringToFront();
            Hook();
        }

        // 클릭 거르기: 패널 맨 위(뿌리보다 위 — 단추의 즉시 소리보다 먼저)에서 내려가는 길에 본다
        void Hook()
        {
            var top = stage.Root != null && stage.Root.panel != null ? stage.Root.panel.visualTree : null;
            if (top == hooked) return;
            Unhook();
            hooked = top;
            if (top == null) return;
            top.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            top.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            top.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
        }

        void Unhook()
        {
            if (hooked == null) return;
            hooked.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            hooked.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            hooked.UnregisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            hooked = null;
        }

        void OnDown(PointerDownEvent e) => Filter(e, e.position, Down);
        void OnUp(PointerUpEvent e) => Filter(e, e.position, Up);
        void OnClick(ClickEvent e) => Filter(e, e.position, Click);

        void Filter(EventBase e, Vector3 position, int kind)
        {
            var target = e.target as VisualElement;
            if (target == null || Passes(new Vector2(position.x, position.y), target, kind)) return;
            e.StopImmediatePropagation();
            if (kind == Up) return;
            bool fresh = kind == Down || !swallowedDown;   // 누름 한 번(삼킨 PointerDown → 그 ClickEvent)에 소리 한 번
            swallowedDown = kind == Down;
            Hint(fresh);
        }

        bool swallowedDown;

        // 대조 어긋남(각본과 엔진이 다르다)은 경고로 남긴다 — 화면은 이어 간다(시험 M65_*_각본_끝까지가 잡는다)
        void Report()
        {
            for (; reported < Runner.Failures.Count; reported++) Debug.LogWarning("[튜토리얼] 대조 어긋남 " + Runner.Failures[reported]);
        }

        static VisualElement Add(VisualElement parent, VisualElement child, string name, string cls)
        {
            child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }

        /// <summary>말풍선 꼬리(마박사 쪽 — 왼쪽을 가리키는 삼각형, 오른쪽 자리면 오른쪽을 가리킨다): 채움 = USS <c>color</c>, 테두리 = <c>border-left-color</c>.</summary>
        sealed class Tail : VisualElement
        {
            bool right;

            public Tail()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Paint;
            }

            public bool Right
            {
                set
                {
                    if (right == value) return;
                    right = value;
                    MarkDirtyRepaint();
                }
            }

            void Paint(MeshGenerationContext mgc)
            {
                float w = layout.width, h = layout.height;
                if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;
                var p = mgc.painter2D;
                Vector2 At(float x, float y) => new Vector2(right ? w - x : x, y);
                p.fillColor = resolvedStyle.borderLeftColor;
                p.BeginPath();
                p.MoveTo(At(w, 0f));
                p.LineTo(At(0f, h * 0.5f));
                p.LineTo(At(w, h));
                p.ClosePath();
                p.Fill();
                float edge = Mathf.Max(1f, Mathf.Round(h / 8f));
                p.fillColor = resolvedStyle.color;
                p.BeginPath();
                p.MoveTo(At(w, edge));
                p.LineTo(At(edge * 2f, h * 0.5f));
                p.LineTo(At(w, h - edge));
                p.ClosePath();
                p.Fill();
            }
        }
    }
}
