using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 본편 해 보기 피드백(사용자 결정 101 — 화면 다섯)의 확인 장면: 카드 바꾸기 창(다섯 장이 겹치지 않는 줄 + 고정 미리보기) · 막 안내 창([확인]까지 · 얻은 것 · 막 배지로 다시) ·
    /// 배틀 결과 창의 규토리 색 아이콘 · 스탬프 조건 줄과 도장 덮개의 조건 · 계산 줄 머리의 주사위 6면. 그림은 <c>Logs/Review/play-feedback/</c>에 모은다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] PlayFeedbackScreens =
        {
            Screen("pf-swap", Fit(s => SwapFive(s, -1))),            // 결정 101 ①: 골드 해금 — 다섯 장을 한 줄에(겹치지 않는다), 고르기 전(미리보기는 안내)
            Screen("pf-swap-pick", Fit(s => SwapFive(s, 2))),        // 셋째 카드를 고른 뒤: 제자리에서 올라오고 테두리 · 아래 큰 미리보기 + 능력 문구 + [이 카드로 교체]
            Screen("pf-swap-pick-last", Fit(s => SwapFive(s, 4))),   // 마지막 카드를 골라도 나머지 넷이 그대로 읽힌다
            Screen("pf-act-notice", Fit(s => ActNotice(s, true))),   // 결정 101 ②: 1막 목표를 이룬 배틀의 [확인] 뒤 — 2막 카드 연출이 걷히면 막 안내 창(얻은 것 · 목표 · 얻는 것 · 달라지는 것)
            Screen("pf-act-badge", Fit(s => ActNotice(s, false))),   // 위 띠 막 배지를 눌러 다시 본 꼴(얻은 것 줄 · 「막이 바뀌었습니다」 없음)
            Screen("pf-result-apricorn", s => ResultApricorn(s)),    // 결정 101 ③: 결과 창의 규토리 줄 = 얻은 색마다 아이콘 + 수
            Screen("pf-result-stamp", s => ResultStamp(s)),          // 결정 101 ④: 결과 창의 스탬프 줄 아래 도장(번호) + 조건 문구 · 도장 덮개의 「달성: 조건」
            Screen("pf-stamp-fx", Clean(s => StampCond(s), "deal-stamp")),   // 도장 덮개(47행): 찍힌 칸 + 조건 문구 + 보상
            Screen("pf-dienet", s => DieNet(s, false)),              // 결정 101 ⑤: 배틀 독 계산 줄 머리의 주사위 6면(일반 타격 4 · 급소 1 · 상태 이상 1)
            Screen("pf-dienet-tip", s => DieNet(s, true)),           // 마우스 오버 = 한 줄 설명
        };

        // 반바지 꼬마 빨강의 골드 해금(카드 다섯 장): 한 걸음 걸으면 카드 바꾸기 창이 선다. pick ≥ 0이면 그 카드를 고른다
        static void SwapFive(UiShot s, int pick)
        {
            var ctx = s.Board.Ctx;
            var flow = CardSeat(s, "youngster", state =>
            {
                var st = state.Seats[0];
                st.TrainerCardOffer.Clear();
                foreach (string k in new[] { "hiker", "swimmer", "chef", "lady", "gentleman" }) st.TrainerCardOffer.Add(ctx.Cards.TrainerCardIds.IndexOf(k));
                st.UnlockDue.Add(TrainerRules.DueSwap);
                st.UnlockDue.Add(0);
            });
            var hud = flow.Hud;
            hud.Refresh();
            var root = flow.Stage.Root;
            WalkOne(flow, hud);
            var swap = (CardSwapScreen)Wait(flow, () => flow.Stage.Current is CardSwapScreen w && flow.BoardOpen && w.Answers.Count > 0);
            if (swap.Answers.Count != CardSwapScreen.Slots) throw new InvalidOperationException("카드 바꾸기 카드가 다섯이 아니다(" + swap.Answers.Count + ")");
            if (pick >= 0 && (!swap.Pick(pick) || swap.Picked != pick)) throw new InvalidOperationException("카드를 고르지 못했다");
            s.Panel.Tick(3);
            // 다섯 장이 서로 겹치지 않고(고른 카드가 남을 가리지 않는다) 모두 화면 안에 있다
            var page = root.Q("board-card-swap-page").worldBound;
            var boxes = new List<Rect>();
            for (int i = 1; i <= CardSwapScreen.Slots; i++)
            {
                var card = root.Q("board-card-swap-card-" + i) ?? throw new InvalidOperationException("카드 " + i + "이 없다");
                var b = card.worldBound;
                if (b.width < 1 || b.xMin < page.xMin - 0.5f || b.xMax > page.xMax + 0.5f || b.yMin < page.yMin - 0.5f || b.yMax > page.yMax + 0.5f) throw new InvalidOperationException("카드 " + i + "이 화면 밖이다 " + b);
                foreach (var o in boxes)
                    if (b.Overlaps(o)) throw new InvalidOperationException("카드 " + i + "이 다른 카드와 겹친다 " + b + " / " + o);
                boxes.Add(b);
                if (card.ClassListContains(CardSwapScreen.OnClass) != (i - 1 == pick)) throw new InvalidOperationException("카드 " + i + "의 고른 표시가 다르다");
            }
            // 큰 미리보기는 고르든 안 고르든 같은 자리이고 카드 줄과 겹치지 않는다
            var big = root.Q("board-card-swap-preview-card").worldBound;
            foreach (var o in boxes)
                if (big.Overlaps(o)) throw new InvalidOperationException("미리보기가 카드 줄과 겹친다 " + big + " / " + o);
            if (big.yMax > page.yMax + 0.5f || root.Q(CardSwapScreen.Go).worldBound.yMax > page.yMax + 0.5f) throw new InvalidOperationException("미리보기·[이 카드로 교체]가 화면 밖이다");
            if (root.Q(CardSwapScreen.Go).enabledSelf != (pick >= 0)) throw new InvalidOperationException("[이 카드로 교체]는 고른 뒤에만 켜진다");
            if ((root.Q("board-card-swap-preview-name") != null) != (pick >= 0)) throw new InvalidOperationException("미리보기의 카드 이름은 고른 뒤에만");
            Debug.Log($"[UiShots] pf-swap {s.Grade.Height}: 카드 {boxes.Count}장 · 첫 카드 {boxes[0]} · 미리보기 {big} · 고른 카드 {pick}");
        }

        // 막 안내 창: notice = 1막 목표를 이룬 배틀의 [확인] 뒤 저절로 뜬 것(얻은 것 + 2막) · 아니면 막 배지로 다시 본 것
        static void ActNotice(UiShot s, bool notice)
        {
            var flow = Win(s);
            flow.ActNotice = notice;
            flow.Layout(s.Width, s.Height);
            var battle = flow.Stage.Current as BattleScreen ?? throw new InvalidOperationException("이긴 판의 결과 창이 없다");
            for (int i = 0; i < 60 && !(flow.BattleOver && battle.Ready && !battle.AskingReward); i++) Pass(flow, BattleStepMs);
            if (!battle.Confirm()) throw new InvalidOperationException("결과 창 [확인]이 거절됐다");
            for (int i = 0; i < 400 && !(notice ? flow.Stage.Current is ActScreen : flow.InputOpen); i++) Pass(flow, 100);
            if (!notice)
            {
                if (flow.Stage.Current != null) throw new InvalidOperationException("ActNotice를 끈 판인데 창이 떴다: " + flow.Stage.Current.Name);
                if (!flow.OpenAct()) throw new InvalidOperationException("막 배지가 막 안내 창을 열지 않았다");
            }
            var act = flow.Stage.Current as ActScreen ?? throw new InvalidOperationException("막 안내 창이 없다(맨 위 " + (flow.Stage.Current != null ? flow.Stage.Current.Name : "-") + ")");
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            if (act.Act != 2) throw new InvalidOperationException("막 안내 창의 막 " + act.Act + "(바란 것 2)");
            if (act.Got != (notice ? 1 : -1) || act.Changed != notice) throw new InvalidOperationException($"얻은 것 {act.Got} · 바뀜 {act.Changed}");
            string want = notice ? "got,goal,gain,change" : "goal,gain,change";
            if (string.Join(",", act.Sections) != want) throw new InvalidOperationException("막 안내 창의 칸 " + string.Join(",", act.Sections) + "(바란 것 " + want + ")");
            if ((root.Q("board-act-new") != null) != notice || root.Q("board-act-ok") == null) throw new InvalidOperationException("막 안내 창의 배지·[확인]");
            Debug.Log($"[UiShots] pf-act {s.Grade.Height}: {act.Act}막 · 칸 {string.Join(",", act.Sections)} · 판 {root.Q("board-act-panel").worldBound}");
        }

        // 결과 창의 규토리 줄: 도망친 야생이 남긴 규토리(Coral 판) — 색마다 아이콘 + 수
        static void ResultApricorn(UiShot s)
        {
            var flow = Trail(s, SoloTrail.Coral, f => Battle(f, b => b.ShowsResult && !b.AskingReward), false);
            s.Panel.Tick(3);
            var battle = (BattleScreen)flow.Stage.Current;
            var root = flow.Stage.Root;
            var row = root.Q("battle-result-apricorn") ?? throw new InvalidOperationException("결과 창에 규토리 줄이 없다");
            if (battle.ResultApricorns.Count == 0) throw new InvalidOperationException("규토리 줄에 색이 없다");
            foreach (var kv in battle.ResultApricorns)
            {
                var chip = row.Q("battle-result-apricorn-" + kv.Key.ToString().ToLowerInvariant()) ?? throw new InvalidOperationException(kv.Key + " 칩이 없다");
                if (chip.childCount < 2 || chip.ElementAt(0).style.backgroundImage.value.texture == null) throw new InvalidOperationException(kv.Key + " 칩에 규토리 그림이 없다");
                if (chip.Q<Label>().text != "+" + kv.Value) throw new InvalidOperationException(kv.Key + " 칩의 수 " + chip.Q<Label>().text);
            }
            var bad = WorkUi.Overflows(root.Q("battle-result-panel"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
            Debug.Log($"[UiShots] pf-result-apricorn {s.Grade.Height}: 색 {battle.ResultApricorns.Count}");
        }

        // 결과 창의 스탬프 줄 + 조건 줄(혼자 하는 판 — 갤럭시단 승리 = 스탬프 2 「트레이너나 울트라 비스트에게 승리」)
        static void ResultStamp(UiShot s)
        {
            var flow = Win(s);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            if (flow.Battle.Stamps.Count != 1 || root.Q("battle-result-stamp") == null) throw new InvalidOperationException("결과 창에 스탬프 줄이 없다");
            var why = root.Q("battle-result-stamp-cond") ?? throw new InvalidOperationException("결과 창에 스탬프 조건 줄이 없다");
            int slot = DealFx.SlotOf(flow.Ctx, flow.Battle.Stamps[0].StampKey);
            string cond = flow.Stage.T("screen.stamp.cond." + (slot + 1));
            var labels = why.Query<Label>().ToList();
            if (labels.Count != 2 || labels[0].text != (slot + 1).ToString() || labels[1].text != cond) throw new InvalidOperationException("스탬프 조건 줄: 도장 번호 + 조건 문구가 아니다");
            Debug.Log($"[UiShots] pf-result-stamp {s.Grade.Height}: 스탬프 {slot + 1} 「{cond}」");
        }

        // 도장 덮개(47행): 찍힌 뒤 — 「달성: 조건」
        static void StampCond(UiShot s)
        {
            var flow = StampInto(s, 0, 1500);
            s.Panel.Tick(3);
            var cond = flow.Stage.Root.Q<Label>("deal-stamp-cond") ?? throw new InvalidOperationException("도장 덮개에 조건 줄이 없다");
            bool known = false;
            for (int i = 1; i <= SeatState.StampCount; i++) known |= cond.text == flow.Stage.T("screen.stamp.fx.cond").Replace("{cond}", flow.Stage.T("screen.stamp.cond." + i));
            if (!known) throw new InvalidOperationException("도장 덮개의 조건 문구 「" + cond.text + "」가 스탬프 조건 문구가 아니다");
            Debug.Log($"[UiShots] pf-stamp-fx {s.Grade.Height}: 「{cond.text}」");
        }

        // 계산 줄 머리의 주사위 6면: 배틀의 내 차례(굴리기 전에도 서 있다)
        static void DieNet(UiShot s, bool tip)
        {
            var flow = ToBattle(s);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var net = root.Q("battle-dienet") ?? throw new InvalidOperationException("계산 줄에 주사위 6면이 없다");
            int faces = 0;
            for (int i = 1; i <= BattleScreen.DieFaces.Length; i++) if (root.Q("battle-dienet-face-" + i) != null) faces++;
            if (faces != 6) throw new InvalidOperationException("주사위 면이 6개가 아니다(" + faces + ")");
            var calc = root.Q("battle-calc").worldBound;
            var b = net.worldBound;
            if (b.width < 1 || b.xMin < calc.xMin - 0.5f || b.xMax > calc.xMax + 0.5f || b.yMin < calc.yMin - 0.5f || b.yMax > calc.yMax + 0.5f) throw new InvalidOperationException("주사위 6면이 계산 줄 밖이다 " + b + " / " + calc);
            var bad = WorkUi.Overflows(root.Q("battle-dock"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
            if (tip) flow.Stage.Tips.Show(net);
            Debug.Log($"[UiShots] pf-dienet {s.Grade.Height}: 면 {faces} · 자리 {b}");
        }
    }
}
