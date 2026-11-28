using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 「규칙 판정」 화면(결정 213 — <see cref="RulingsScreen"/>)의 확인 장면: 타이틀 줄 · 처음 모습 · 찾기 · 출처 하나만 · 절 하나 · 없음 · 모든 항목 · 게임 메뉴에서 연 창.
    /// 모든 장면이 글 넘침(<see cref="WorkUi.Overflows"/>)과 판 · 칸이 화면 · 판 안에 드는지, 목록에 줄이 섰는지를 단정한다. 영어는 <c>-uiLang en</c>. 그림은 <c>Logs/Review/rulings-screen/</c>에 모은다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] RulingsScreens =
        {
            Screen("rulings-title", s => BookTitle(s, TitleScreen.Rulings)),                          // 타이틀의 「규칙 판정」 줄(메뉴 판이 설명 판과 겹치지 않는다)
            Screen("rulings", s => RulingsOpen(s, null)),                                             // 처음 모습: 전체 · 출처 모두 켬
            Screen("rulings-search", s => RulingsOpen(s, r =>                                         // 찾기(띄어 쓴 낱말은 모두 — 표의 첫 물음에서 두 낱말을 가져온다)
            {
                var words = r.Shown[0].Q.Split(' ');
                int before = r.Shown.Count;
                r.Find(words[0] + "  " + words[words.Length > 2 ? 2 : words.Length - 1]);
                if (r.Shown.Count == 0 || r.Shown.Count >= before) throw new InvalidOperationException("찾기가 거르지 않았다: " + r.Query + " → " + r.Shown.Count);
            })),
            Screen("rulings-source", s => RulingsOpen(s, r =>                                         // 출처 하나만 켬(원작자 답변)
            {
                r.Toggle(RulingSource.Rulebook);
                r.Toggle(RulingSource.House);
                foreach (var x in r.Shown) if (x.Source != RulingSource.Creator) throw new InvalidOperationException("끈 출처가 남았다: " + x.Id);
                if (r.Shown.Count == 0) throw new InvalidOperationException("원작자 답변이 없다");
            })),
            Screen("rulings-section", s => RulingsOpen(s, r =>                                        // 묶음을 펴고 절 하나(위치 줄 없음)
            {
                var g = s.Made.RulingsOf(s.Made.BookLang).Groups[1];
                if (!r.Pick(g.Key, g.Sections[1].Key) || r.Shown.Count != g.Sections[1].Count) throw new InvalidOperationException("절 고르기");
            })),
            Screen("rulings-none", s => RulingsOpen(s, r =>                                           // 찾는 내용이 없다
            {
                r.Find("zzzz qqqq");
                if (r.Shown.Count != 0) throw new InvalidOperationException("없는 말을 찾았다");
            })),
            Screen("rulings-all", s => RulingsOpen(s, r =>                                            // 모든 항목을 굴려 가며 글 넘침을 본다(끝 모습 = 맨 아래)
            {
                var list = s.Made.Stage.Root.Q<ListView>("rulings-list");
                for (int i = 0; i < r.Shown.Count; i += 4)
                {
                    list.ScrollToItem(i);
                    RulingsCheck(s, s.Made);
                }
                list.ScrollToItem(r.Shown.Count - 1);
                Debug.Log("[UiShots] rulings-all " + r.Shown.Count + "항목을 굴려 글 넘침을 봤다");
            })),
            Screen("rulings-board", s =>                                                              // 게임 메뉴 [규칙 판정] → 게임 중 창(보드 위)
            {
                var flow = ToBoard(s, BoardReadyMs);
                flow.OpenMenu();
                s.Panel.Tick(3);
                if (flow.Stage.Root.Q("board-menu-rulings") == null) throw new InvalidOperationException("메뉴에 [규칙 판정]이 없다");
                var bad = WorkUi.Overflows(flow.Stage.Root.Q("board-menu-rulings").parent);
                if (bad.Count > 0) throw new InvalidOperationException("게임 메뉴 글 넘침: " + string.Join(" / ", bad));
                if (!flow.OpenMenuRulings() || !(flow.Stage.Current is RulingsScreen r) || !r.InGame) throw new InvalidOperationException("게임 메뉴에서 창이 열리지 않았다");
                RulingsCheck(s, flow);
            }),
        };

        static void RulingsOpen(UiShot s, Action<RulingsScreen> then)
        {
            var flow = s.Flow();
            flow.OpenTitle();
            flow.OpenTitleRulings();
            var r = flow.Stage.Current as RulingsScreen ?? throw new InvalidOperationException("「규칙 판정」이 열리지 않았다");
            RulingsCheck(s, flow);
            then?.Invoke(r);
            RulingsCheck(s, flow);
        }

        // 판이 화면 안 · 칸이 판 안 · 보이는 본문 칸이 본문 안 · 목록에 줄이 섰다(없으면 「없음」 줄) · 줄이 목록 폭 안 · 글이 잘리거나 겹치지 않는다
        static void RulingsCheck(UiShot s, GameFlow flow)
        {
            for (int i = 0; i < 4; i++) s.Panel.Tick(3);   // 목록은 줄 높이를 재며 여러 프레임에 걸쳐 선다
            var root = flow.Stage.Root;
            var r = (flow.Stage.Info ?? flow.Stage.Current) as RulingsScreen ?? throw new InvalidOperationException("「규칙 판정」 창이 없다");
            var page = root.Q(r.Name) ?? throw new InvalidOperationException("rulings 요소가 없다");
            string who = r.Query + "/" + r.Group + "/" + r.Section + "/" + r.SourceMask;
            var panel = root.Q("rulings-panel");
            BookInside(page.worldBound, panel.worldBound, "판", who);
            foreach (var child in panel.Children()) BookInside(panel.worldBound, child.worldBound, "판의 칸", who);
            var read = root.Q("rulings-read");
            foreach (var child in read.Children())
                if (child.resolvedStyle.display != DisplayStyle.None) BookInside(read.worldBound, child.worldBound, "본문 칸", who);
            var list = root.Q<ListView>("rulings-list");
            var rows = list.Query(className: "rulings-item").ToList();
            bool none = root.Q("rulings-none").resolvedStyle.display != DisplayStyle.None;
            if (none != (r.Shown.Count == 0)) throw new InvalidOperationException("「없음」 줄이 목록과 어긋난다(" + who + ")");
            if (r.Shown.Count > 0)
            {
                int seen = 0;
                foreach (var row in rows)
                {
                    if (row.worldBound.height < 1 || !row.worldBound.Overlaps(list.worldBound)) continue;
                    seen++;
                    if (row.worldBound.xMin < list.worldBound.xMin - 0.5f || row.worldBound.xMax > list.worldBound.xMax + 0.5f) throw new InvalidOperationException("줄이 목록 폭 밖(" + who + ") " + row.worldBound);
                }
                if (seen == 0) throw new InvalidOperationException("목록에 줄이 서지 않았다(" + who + ") 줄 " + rows.Count);
                if (rows.Count > 80) throw new InvalidOperationException("목록이 보이는 줄만 만들지 않는다: " + rows.Count);
            }
            var bad = WorkUi.Overflows(page);
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + bad.Count + "곳(" + who + "): " + string.Join(" / ", bad));
        }
    }
}
