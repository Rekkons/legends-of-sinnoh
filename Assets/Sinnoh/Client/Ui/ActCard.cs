using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>막 카드 한 장에 띄울 것(04 S14): 몇 막인가, 처음 달성해 공개한 좌석(서막·1막은 없음). 화면 흐름이 이벤트에서 모아 단계의 꼬리표(<see cref="FxStep.Tag"/>)로 붙인다.</summary>
    public sealed class ActCardInfo
    {
        /// <summary>막(0 서막 · 1~3 · 4 종막).</summary>
        public int Act;
        /// <summary>그 앞 막의 목표를 처음 달성한 좌석(없으면 −1)과 그 이름·색.</summary>
        public int FirstSeat = -1;
        public string FirstName;
        /// <summary>그 좌석의 주인공(키 — 얼굴 상자).</summary>
        public string FirstFace;
        public Color32 FirstColor;
    }

    /// <summary>
    /// S14 막 카드(04 §8-8 4행 — 막이 바뀔 때와 게임을 시작할 때 서막·1막): 보드가 어두워지고, 카드가 위에서 떨어지며 가로로 뒤집히고, 제목 글자가 하나씩 박힌 뒤
    /// 마지막 글자에 한 번 흔들리고, 목표 문장과 보상 줄이 뜬 다음 걷힌다. 길이는 대기열 단계가 정하고(<c>PlayTiming</c> 40행 3600 — 방장과 같다) 박자는 <see cref="ActBeats"/>다.
    /// 여기는 그 모습을 옮기기·늘이기·흐리기로만 그린다(04 §8-6). 전원 차단 컷신이라 건너뛰지 못한다(§8-1 P2) — 도는 동안 입력은 닫혀 있다.
    /// 카드 그림(원작 막 카드 5장)은 Gen에 없어 글자 카드로 그린다 [가정]: 막 이름 배지 · 큰 제목(본문 글자 ×3) · 목표 문장 · 보상 줄(1막 = 빨강 규토리 +3) · 처음 달성한 좌석.
    /// 마우스를 받지 않는다(요소마다 <c>PickingMode.Ignore</c> — 보이지 않는 카드가 보드의 칸 고르기·휠을 막지 않게, <see cref="TurnBand"/>와 같다).
    /// 요소 이름: <c>act-card</c>·<c>act-card-scrim</c>·<c>act-card-panel</c>·<c>act-card-act</c>·<c>act-card-title</c>·<c>act-card-goal</c>·<c>act-card-reward</c>·<c>act-card-first</c>.
    /// </summary>
    public sealed class ActCard
    {
        /// <summary>어둠의 가장 짙은 값(sRGB에서 고른 값 — USS <c>--sn-battle-scrim</c>의 0.7과 같아야 한다). 도중 값은 Linear 보정(<see cref="LinearAlpha.Dim"/>)으로 옮긴다.</summary>
        public const float DimAlpha = 0.7f;
        /// <summary>제목 글자의 배수(본문 글자 ×3 — 1080 벌에서 72px, 넘김 띠의 큰 글자와 같은 예외).</summary>
        public const int TitleTimes = 3;
        /// <summary>흔들림 폭(패널 px, 720 벌 기준 — 1080 벌은 ×1.5).</summary>
        public const float ShakePx = 6f;

        readonly UiStage stage;
        readonly VisualElement scrim, panel, letters, first;
        readonly Label act, goal, reward, firstName;
        readonly List<Label> glyphs = new List<Label>();
        ActCardInfo shown;

        public readonly VisualElement Element;

        /// <summary>지금 카드를 그리고 있나(막 카드 단계가 재생 중이다).</summary>
        public bool Active { get; private set; }

        /// <summary>지금 그리고 있는 카드의 막(없으면 −1) — 시험·캡처가 본다.</summary>
        public int ShownAct => Active && shown != null ? shown.Act : -1;

        /// <summary>지금 박혀 있는 제목 글자 수.</summary>
        public int Stamped { get; private set; }

        /// <summary>목표 문장까지 다 뜬 모습인가(글자가 다 박히고 흔들림이 끝난 뒤 — 걷히기 전) — 확인·캡처가 찍을 때를 고른다.</summary>
        public bool GoalShown { get; private set; }

        public ActCard(UiStage stage)
        {
            this.stage = stage;
            Element = new VisualElement { name = "act-card" };
            Element.AddToClassList("sn-band-layer");
            scrim = Add(Element, new VisualElement(), "act-card-scrim", "sn-act-scrim");
            panel = Add(Element, Kit.Panel(stage.Skin), "act-card-panel", "sn-act-card");
            act = Add(panel, Kit.Badge("", KitBadgeKind.Silver, Kit.F2), "act-card-act", null);
            letters = Add(panel, new VisualElement(), "act-card-title", "sn-act-letters");
            goal = Add(panel, Kit.Wrapped("", Kit.F2), "act-card-goal", "sn-act-goal");
            reward = Add(panel, Kit.Badge("", KitBadgeKind.Gold, Kit.F2), "act-card-reward", "sn-act-reward");
            first = Add(panel, new VisualElement(), "act-card-first", "sn-act-first");
            first.Add(FaceBox.Make());
            firstName = Add(first, Kit.Text("", Kit.F2), null, "sn-hud-seat-name");
            FxUi.Fade(scrim, 0f);
            FxUi.Fade(panel, 0f);
            Unpick(Element);
        }

        static void Unpick(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var child in e.Children()) Unpick(child);
        }

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }

        void Fill(ActCardInfo info)
        {
            if (ReferenceEquals(info, shown)) return;
            shown = info;
            string n = info.Act.ToString(CultureInfo.InvariantCulture);
            act.text = stage.T(BoardLog.ActKey(info.Act));
            string title = stage.T(BoardLog.ActTitleKey(info.Act));
            letters.Clear();
            glyphs.Clear();
            foreach (char c in title)
            {
                var box = Add(letters, new VisualElement(), null, "sn-act-letter");
                var glyph = Add(box, Kit.Text(c.ToString(), Kit.F2), null, "sn-act-glyph");
                glyphs.Add(glyph);
            }
            goal.text = KoText.JoinWords(stage.T("screen.act.goal." + n));
            string gain = stage.TOrNull("screen.act.reward." + n);
            reward.text = gain ?? "";
            reward.style.display = gain != null ? DisplayStyle.Flex : DisplayStyle.None;
            bool hasFirst = info.FirstSeat >= 0;
            first.style.display = hasFirst ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasFirst)
            {
                FaceBox.Paint(first.ElementAt(0), info.FirstSeat, info.FirstColor, info.FirstFace);
                firstName.text = stage.T("screen.act.first").Replace("{name}", info.FirstName ?? "");
            }
            Unpick(Element);
        }

        /// <summary>대기열의 지금 항목·시각으로 그린다. 막 카드 단계가 돌고 있지 않으면 걷는다.</summary>
        public void Show(FxSequencer seq) => Show(seq.Current, seq.LocalMs);

        public void Show(FxEntry entry, int localMs)
        {
            var step = ActBeats.StepOf(entry);
            var info = step != null ? step.Tag as ActCardInfo : null;
            Active = info != null && localMs >= step.StartMs && localMs < step.EndMs;
            if (!Active)
            {
                Stamped = 0;
                GoalShown = false;
                FxUi.Fade(scrim, 0f);
                FxUi.Fade(panel, 0f);
                return;
            }
            Fill(info);
            var pose = ActBeats.At(localMs - step.StartMs, step.Ms, glyphs.Count);
            // 어둠: 고른 값(0 ~ 0.7)을 Linear로 옮긴 알파를, USS가 이미 넣어 둔 가장 짙은 알파에 대한 비율로
            FxUi.Fade(scrim, LinearAlpha.Dim(DimAlpha * pose.Dim) / LinearAlpha.Dim(DimAlpha));
            FxUi.Fade(panel, pose.Card);
            float height = stage.Root != null && !float.IsNaN(stage.Root.layout.height) ? stage.Root.layout.height : 720f;
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            FxUi.Move(panel, pose.Shake * ShakePx * unit, pose.Drop * height * 0.75f);
            FxUi.Scale(panel, Mathf.Max(0.02f, pose.Flip), 1f);
            Stamped = pose.Letters;
            GoalShown = pose.Goal >= 1f && pose.Reward >= 1f && !pose.Landed;
            for (int i = 0; i < glyphs.Count; i++)
            {
                bool on = i < pose.Letters;
                float s = TitleTimes * (i == pose.Letters - 1 ? pose.Stamp : 1f);
                FxUi.Fade(glyphs[i], on ? 1f : 0f);
                FxUi.Scale(glyphs[i], s, s);
            }
            FxUi.Fade(goal, pose.Goal);
            FxUi.Fade(reward, pose.Reward);
            FxUi.Fade(first, pose.Reward);
        }
    }
}
