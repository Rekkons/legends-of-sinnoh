using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 룰 설명서 S43(결정 100 · 111 ① — 큰 창 B · 게임 중 옆 판 C)과 크레딧 S03의 확인 장면(M9 「설명서 화면 S43 · 크레딧 S03」): 타이틀 줄 · 목차 · 절(표 · 그림 · cards 블록) · 링크 · 찾기 ·
    /// 고침 툴팁 · 영어 · 모든 절 · 게임 메뉴에서 연 큰 창 · 위 띠 [설명서] · 옆 판(칩 · 펼친 절 · 찾기 · 크게 보기) · 크레딧(묶음 셋).
    /// 모든 장면이 글 넘침(<see cref="WorkUi.Overflows"/>)과 판 · 칸이 화면 · 판 안에 드는지를 단정한다(<c>Fit</c> 꼴). 그림은 <c>Logs/Review/m9/rulebook-ui/</c>에 모은다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] RulebookScreens =
        {
            Screen("rulebook-title", s => BookTitle(s, TitleScreen.Rulebook)),                       // 타이틀의 「룰 설명서」 줄(메뉴 판이 설명 판과 겹치지 않는다)
            Screen("rulebook-toc", s => BookOpen(s, null)),                                          // 타이틀 → 큰 창: 첫 절(표지 그림) · 목차 나무
            Screen("rulebook-section", s => BookOpen(s, b => b.Show("pokemon.encounter"))),          // 절 하나: 링크 둘 · 디지털판 꼬리표
            Screen("rulebook-table", s => BookOpen(s, b => b.Show("pokemon.apricorns"))),            // 표 + 그림 셋
            Screen("rulebook-figure", s => BookOpen(s, b => b.Show("battle.example"))),              // 그림이 많은 긴 절(위치 막대)
            Screen("rulebook-cards", s => BookOpen(s, b => b.Show("trainer_cards.cards"))),          // cards 블록 — 트레이너 카드 51(게임 표의 이름 · 능력)
            Screen("rulebook-recipes", s => BookOpen(s, b => b.Show("recipes.items"))),              // cards 블록 — 아이템 그림 · 레시피 규토리
            Screen("rulebook-link", s => BookOpen(s, b => { b.Show("pokemon.encounter"); BookFollow(b, "hidden_spots.artifacts"); })),   // 링크 「[유물 참조]」 → 그 절
            Screen("rulebook-search", s => BookOpen(s, b => { b.Find("규토리"); b.Show(b.Hits[0].Section.Key); }, lang: "ko")),   // 한국어판 설명서(찾는 말이 한국어 — 영어 화면 전수에서도) · 찾기 결과(절마다 n곳 · 조각) + 본문 칠하기
            Screen("rulebook-en", s => BookOpen(s, b => b.Show("overview.village"), lang: "en")),    // 영문 원본(설명서 언어 = en)
            Screen("rulebook-all", s => BookAll(s)),                                                 // 두 언어의 모든 절을 세워 글 넘침을 본다(끝 모습 = 영어 마지막 절)
            Screen("rulebook-board", s =>                                                            // 게임 메뉴 [룰 설명서] → 게임 중 큰 창(보드 위)
            {
                var flow = ToBoard(s, BoardReadyMs);
                flow.OpenMenu();
                if (!flow.OpenMenuRulebook() || !(flow.Stage.Current is RulebookScreen b) || !b.InGame) throw new InvalidOperationException("게임 메뉴에서 큰 창이 열리지 않았다");
                b.Show("overview.turn");
                BookCheck(s, flow);
            }),
            Screen("rulebook-hud", s =>                                                              // 위 띠 [설명서] 단추 + 마우스 오버(도감 · 메뉴와 겹치지 않는다)
            {
                var flow = ToBoard(s, BoardReadyMs);
                s.Panel.Tick(3);
                var root = flow.Stage.Root;
                var button = root.Q("board-hud-book") ?? throw new InvalidOperationException("[설명서] 단추가 없다");
                Apart(root, "board-hud-book", "board-hud-dex");
                Apart(root, "board-hud-book", "board-hud-menu");
                flow.Stage.Tips.Show(button);
            }),
            Screen("rulebook-hud-6", s => { Seats(s, 6); s.Panel.Tick(3); BookTopBand(s); }),       // 6인 판의 위 띠(좌석 칩 줄과 상태 판이 겹치지 않는다)
            Screen("rulebook-side", s => BookSide(s, null)),                                         // 옆 판: 지금 칸(고향 마을) 칩 · 첫 칩의 절 펼침
            Screen("rulebook-side-chip", s => BookSide(s, side => side.Pick(side.Chips[1]))),       // 둘째 칩(공중날기)
            Screen("rulebook-side-search", s => BookSide(s, side => side.Find("회복"))),             // 옆 판 찾기(찾은 절 줄 · n곳)
            Screen("rulebook-side-big", s =>                                                         // 옆 판 [크게 보기] → 큰 창(펼친 절)
            {
                var flow = BookSideFlow(s);
                var side = flow.BookSide;
                string picked = side.Picked;
                if (!side.Big() || !(flow.Stage.Current is RulebookScreen b) || b.Current.Key != picked) throw new InvalidOperationException("[크게 보기]가 펼친 절의 큰 창을 열지 않았다");
                BookCheck(s, flow);
            }),
            Screen("credits", s => Credits(s, null)),                                                // 크레딧: 원작 탭 · 고지 문구
            Screen("credits-pokemon", s => Credits(s, "pokemon")),                                   // 포켓몬 권리자 한 줄 · PokeAPI
            Screen("credits-art", s => Credits(s, "art")),                                           // 그림: Smogon 작가 25 · 쇼다운 작가 이름 줄
            Screen("credits-sound", s => Credits(s, "sound")),                                       // 소리 · 글꼴
            Screen("credits-program", s => Credits(s, "program")),                                   // 프로그램
            Screen("credits-title", s => BookTitle(s, TitleScreen.Credits)),                         // 타이틀의 「크레딧」 줄
        };

        static void BookTitle(UiShot s, string pick)
        {
            var flow = s.Flow();
            flow.OpenTitle(pick);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var title = (TitleScreen)flow.Stage.Current;
            if (root.Q("title-item-" + pick) == null || !title.Ready(Array.Find(TitleScreen.Items, i => i.Id == pick))) throw new InvalidOperationException("타이틀의 " + pick + " 줄이 꺼져 있다");
            Apart(root, "title-menu", "title-desc");
            Apart(root, "title-menu", "title-foot");
            var bad = WorkUi.Overflows(root.Q("title-menu"));
            if (bad.Count > 0) throw new InvalidOperationException("타이틀 메뉴 글 넘침: " + string.Join(" / ", bad));
        }

        static void BookOpen(UiShot s, Action<RulebookScreen> then, string lang = null)
        {
            var flow = s.Flow();
            flow.OpenTitle();
            RulebookScreen b;
            if (lang == null)
            {
                flow.OpenTitleRulebook();
                b = flow.Stage.Current as RulebookScreen ?? throw new InvalidOperationException("큰 창이 열리지 않았다");
            }
            else
            {
                var book = flow.Book(lang) ?? throw new InvalidOperationException("설명서 " + lang + " 없음");
                b = new RulebookScreen(book, flow.Ctx, lang, null, () => { }, false);
                flow.Stage.Open(b);
            }
            then?.Invoke(b);
            BookCheck(s, flow);
        }

        // 링크(UI Toolkit 링크 태그 — 시험은 화면 함수로): 그 절 글에 그 링크가 있어야 한다
        static void BookFollow(RulebookScreen b, string key)
        {
            if (!b.View.Links.Contains(key)) throw new InvalidOperationException(b.Current.Key + "에 링크 " + key + "가 없다");
            if (!b.Show(key) || b.Current.Key != key) throw new InvalidOperationException("링크가 그 절로 가지 않았다");
        }

        static void BookAll(UiShot s)
        {
            var flow = s.Flow();
            flow.OpenTitle();
            int seen = 0;
            foreach (string lang in new[] { "ko", "en" })
            {
                var book = flow.Book(lang);
                var b = new RulebookScreen(book, flow.Ctx, lang, null, () => { }, false);
                flow.Stage.Open(b);
                foreach (var sec in book.Sections)
                {
                    b.Show(sec.Key);
                    BookCheck(s, flow);
                    seen++;
                }
            }
            Debug.Log("[UiShots] rulebook-all 절 " + seen + "개(두 언어)를 세워 글 넘침을 봤다");
        }

        // 큰 창 · 크레딧: 판이 화면 안 · 목차 · 본문 칸이 판 안 · 글이 잘리거나 겹치지 않는다
        static void BookCheck(UiShot s, GameFlow flow)
        {
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var top = flow.Stage.Current ?? throw new InvalidOperationException("창이 없다");
            var page = root.Q(top.Name) ?? throw new InvalidOperationException(top.Name + " 요소가 없다");
            string who = top is RulebookScreen rb ? rb.Lang + " " + rb.Current.Key : top.Name;
            var panel = root.Q(top.Name + "-panel");
            BookInside(page.worldBound, panel.worldBound, "판", who);
            foreach (var child in panel.Children()) BookInside(panel.worldBound, child.worldBound, "판의 칸", who);
            var read = page.Q(className: "sn-book-read");
            if (read != null) foreach (var child in read.Children()) BookInside(read.worldBound, child.worldBound, "본문 칸", who);
            var bad = WorkUi.Overflows(page);
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + bad.Count + "곳(" + who + "): " + string.Join(" / ", bad));
        }

        static void BookInside(Rect outer, Rect b, string what, string who)
        {
            if (b.width < 1 || b.xMin < outer.xMin - 0.5f || b.xMax > outer.xMax + 0.5f || b.yMin < outer.yMin - 0.5f || b.yMax > outer.yMax + 0.5f)
                throw new InvalidOperationException(what + "이(가) 밖으로 나갔다(" + who + ") " + b + " / " + outer);
        }

        // 위 띠: [설명서]가 상태 판 안 · 좌석 칩 줄과 상태 판이 겹치지 않는다 · 글 넘침
        static void BookTopBand(UiShot s)
        {
            var root = s.Made.Stage.Root;
            if (root.Q("board-hud-book") == null) throw new InvalidOperationException("[설명서] 단추가 없다");
            Apart(root, "board-hud-seats", "board-hud-status");
            BookInside(root.Q("board-hud-status").worldBound, root.Q("board-hud-book").worldBound, "[설명서]", "위 띠");
            BookInside(root.worldBound, root.Q("board-hud-status").worldBound, "상태 판", "위 띠");
            var bad = WorkUi.Overflows(root.Q("board-hud-status"));
            if (bad.Count == 0) return;
            // [설명서]를 빼고도 넘치는 것(그 전부터의 일 — 6인 1080에서 좌석 칩 줄이 상태 판을 미는 것)은 경고만, [설명서] 때문에 새로 생긴 넘침이면 실패
            var book = root.Q("board-hud-book");
            book.style.display = DisplayStyle.None;
            s.Panel.Tick(3);
            var before = WorkUi.Overflows(root.Q("board-hud-status"));
            book.style.display = StyleKeyword.Null;
            s.Panel.Tick(3);
            var added = bad.FindAll(b => !before.Exists(x => x.Split('「')[0] == b.Split('「')[0]));
            if (added.Count > 0) throw new InvalidOperationException("위 띠 글 넘침([설명서] 때문): " + string.Join(" / ", added));
            Debug.LogWarning("[UiShots] 위 띠: [설명서] 전부터 넘침 " + before.Count + "곳 — " + string.Join(" / ", before) + " · 상태 판 " + root.Q("board-hud-status").worldBound + " · 좌석 " + root.Q("board-hud-seats").worldBound);
        }

        static GameFlow BookSideFlow(UiShot s)
        {
            var flow = ToBoard(s, BoardReadyMs);
            if (!flow.ToggleBookSide()) throw new InvalidOperationException("옆 판이 열리지 않았다");
            flow.Skip(16);
            s.Panel.Tick(3);
            return flow;
        }

        static void BookSide(UiShot s, Action<RulebookSide> then)
        {
            var flow = BookSideFlow(s);
            var side = flow.BookSide;
            if (side.Chips.Count < 2) throw new InvalidOperationException("지금 칸 칩이 둘보다 적다: " + string.Join(",", side.Chips));
            then?.Invoke(side);
            flow.Skip(16);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var panel = root.Q("rulebook-side") ?? throw new InvalidOperationException("옆 판이 붙지 않았다");
            if (flow.Stage.Current != null) throw new InvalidOperationException("옆 판은 창이 아니다 — 위에 창이 열렸다");
            if (!flow.InputOpen) throw new InvalidOperationException("옆 판이 열려도 보드 입력은 열려 있다");
            BookInside(root.worldBound, panel.worldBound, "옆 판", "C");
            Apart(root, "rulebook-side", "board-hud-dock");
            Apart(root, "rulebook-side", "board-hud-status");
            Apart(root, "rulebook-side", "board-hud-log");
            foreach (var child in panel.Children()) BookInside(panel.worldBound, child.worldBound, "옆 판의 칸", "C");
            var bad = WorkUi.Overflows(panel);
            if (bad.Count > 0) throw new InvalidOperationException("옆 판 글 넘침: " + string.Join(" / ", bad));
        }

        static void Credits(UiShot s, string id)
        {
            var flow = s.Flow();
            flow.OpenTitle();
            flow.OpenCredits();
            var c = flow.Stage.Current as CreditsScreen ?? throw new InvalidOperationException("크레딧이 열리지 않았다");
            if (id != null && !c.Pick(id)) throw new InvalidOperationException("크레딧 묶음 " + id + " 없음");
            BookCheck(s, flow);
            // 묶음마다 세워 글 넘침을 본다(끝 모습 = 고른 묶음)
            string picked = c.Picked.Id;
            foreach (var sec in flow.Credits().Sections)
            {
                c.Pick(sec.Id);
                BookCheck(s, flow);
            }
            c.Pick(picked);
            s.Panel.Tick(3);
        }
    }
}
