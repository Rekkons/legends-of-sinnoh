using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S35 도전할 상대 고르기(04 §2 — M5 「스탬프·도전·거래 화면」, rulings A29 ②): 보드 HUD의 [도전]이 도전할 수 있는 상대가 여럿이거나 원격 도전(다른 신의 추종자 — p.19)이 있을 때 여는 작은 창.
    /// 줄 하나 = 합법 답 하나(엔진 순서): 얼굴 상자 · 이름 · 「같은 칸」/「원격」 · 「거절 가능」(마을 안 · 원격)/「거절 불가」 · [도전]. 갤럭시단(조무래기 · 간부 · 태홍)도 같은 줄로 선다.
    /// 상대가 하나뿐인 같은 칸 도전은 창 없이 바로 낸다(<see cref="BoardHud.Press"/>). 도전하면 VS 연출(51행) 뒤 상대의 응답(S41) 또는 배틀로 넘어간다.
    /// 요소 이름: <c>board-challenge</c>·<c>board-challenge-panel</c>·<c>board-challenge-close</c>·<c>board-challenge-row-&lt;n&gt;</c>·<c>board-challenge-go-&lt;n&gt;</c>(n = 합법 답 순서, 1부터).
    /// </summary>
    public sealed class ChallengeScreen : IUiScreen
    {
        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel;
        readonly List<ChoiceAnswer> answers = new List<ChoiceAnswer>();

        public ChallengeScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-challenge";

        /// <summary>지금 그린 도전 답(보이는 순서 — 같은 칸 플레이어 · 갤럭시단 · 원격).</summary>
        public IReadOnlyList<ChoiceAnswer> Answers => answers;

        /// <summary>내 맵 행동의 도전 답들(없으면 빈 목록).</summary>
        public static List<ChoiceAnswer> Legal(GameFlow flow)
        {
            var client = flow.Client;
            var choice = client != null ? client.Choice(ChoiceKind.MapAction) : null;
            return choice != null ? client.Legal(choice).FindAll(a => a.Kind == AnswerKind.Challenge) : new List<ChoiceAnswer>();
        }

        /// <summary>창을 거쳐야 하나: 도전 답이 여럿이거나 원격 도전이 있다.</summary>
        public static bool Needed(List<ChoiceAnswer> challenges) => challenges.Count > 1 || challenges.Exists(a => a.Arg == TradeRules.RemoteArg);

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판 HUD)의 입력을 먹지 않는다 — 창 밖 누르기는 어둠이 받는다(T35)
            var scrim = Add(page, new VisualElement(), "board-challenge-scrim", "sn-party-scrim");
            scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseChallenge());
            panel = Add(page, Kit.Panel(stage.Skin), "board-challenge-panel", "sn-challenge-panel");
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
        }

        /// <summary>보기·입력이 바뀌었다: 도전할 상대가 없어졌으면 닫고, 아니면 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null) return;
            var view = flow.Client != null ? flow.Client.View : null;
            answers.Clear();
            answers.AddRange(Legal(flow));
            // 같은 칸 상대가 먼저 보이게: 같은 칸 플레이어 → 갤럭시단(내 칸) → 원격(엔진 순서는 좌석 순이라 원격이 위에 오기도 했다)
            var sorted = new List<ChoiceAnswer>(answers);
            answers.Clear();
            for (int rank = 0; rank < 3; rank++)
                foreach (var a in sorted)
                    if (Rank(a) == rank) answers.Add(a);
            if (view == null || answers.Count == 0)
            {
                flow.CloseChallenge();
                return;
            }
            var inv = CultureInfo.InvariantCulture;
            stage.Tips.Hide();
            panel.Clear();
            bool open = flow.BoardOpen;
            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            Add(head, Kit.Text(stage.T("screen.challenge.title"), Kit.FTitle), "board-challenge-title", "kit-title");
            Add(head, Kit.Button(stage.T("screen.challenge.close"), KitButtonKind.Normal, () => flow.CloseChallenge()), "board-challenge-close", null);
            for (int i = 0; i < answers.Count; i++)
            {
                int index = i;
                var a = answers[i];
                string n = (i + 1).ToString(inv);
                var row = Add(panel, new VisualElement(), "board-challenge-row-" + n, "sn-challenge-row");
                var who = Add(row, new VisualElement(), null, "sn-challenge-who");
                if (a.Target >= 0 && a.Target < view.Seats.Length)
                {
                    var face = Add(who, FaceBox.Make(), "board-challenge-face-" + n, null);
                    FaceBox.Paint(face, a.Target, flow.SeatColor(a.Target), flow.Protagonist(a.Target));
                    who.Add(Kit.Text(flow.SeatName(a.Target), Kit.F2));
                    bool remote = a.Arg == TradeRules.RemoteArg;
                    who.Add(Kit.Badge(stage.T(remote ? "screen.challenge.tag.remote" : "screen.challenge.tag.here"), KitBadgeKind.Plain, Kit.F2));
                    // 거절: 원격은 늘 · 같은 칸은 마을 안에서만(p.3 — 엔진의 판정과 같은 함수)
                    bool refuse = remote || BattleRules.InVillage(flow.Ctx, view.Seats[a.Target].Cell);
                    var tag = Kit.Badge(stage.T(refuse ? "screen.challenge.tag.refuse" : "screen.challenge.tag.forced"), refuse ? KitBadgeKind.Plain : KitBadgeKind.Warn, Kit.F2);
                    who.Add(tag);
                    stage.Tips.Hook(tag, null, stage.T("screen.challenge.tag.refuse.tip"), TipSide.Above, reading: true, clear: panel);
                }
                else who.Add(Kit.Text(stage.T("screen.challenge.galactic"), Kit.F2));
                var go = Add(row, Kit.Button(stage.T("screen.challenge.go"), KitButtonKind.Main, () => Press(index)), "board-challenge-go-" + n, null);
                go.SetEnabled(open);
                if (open) stage.Tips.Hook(go, null, stage.T("screen.challenge.go.tip"), TipSide.Above, reading: true, clear: panel);
            }
        }

        static int Rank(ChoiceAnswer a) => a.Arg == TradeRules.RemoteArg ? 2 : a.Target >= 0 ? 0 : 1;

        /// <summary>그 줄의 [도전]: 창을 닫고 그 답을 낸다. 받아들여졌으면 true.</summary>
        public bool Press(int index)
        {
            var choice = flow.Client != null ? flow.Client.Choice(ChoiceKind.MapAction) : null;
            if (choice == null || !flow.BoardOpen || index < 0 || index >= answers.Count) return false;
            var answer = answers[index];
            stage?.Tips.Hide();
            flow.CloseChallenge();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>확인용(길잡이·시험): 그 좌석에게 도전한다.</summary>
        public bool PressSeat(int seat) => Press(answers.FindIndex(a => a.Target == seat));

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
