using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Data;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M6.5 「화면 ② 목차·입구」 확인 장면(07 §2·§9-3): 타이틀 [튜토리얼] + 「처음이라면!」 딱지 · 목차(기록 없음 · 완료/이어서 섞임 · 이어서 툴팁) ·
    /// 끝내기 창(말풍선·게임 메뉴가 여는 창) · 게임 메뉴(튜토리얼 판) · 본편 끝 창(7장 건너뛰기). 기록은 실행하는 동안만 쥐는 것(<see cref="TutorialRecord.Memory"/> — 사용자 기록에 쓰지 않는다).
    /// 장면마다 그 화면의 글이 칸 밖으로 넘치지 않는지(<see cref="WorkUi.Overflows"/>)를 단정한다. 튜토리얼 판 장면에는 목차가 붙인 튜토리얼 층(말풍선·덮개 — 07 T60)도 함께 찍힌다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] TutorialMenuScreens =
        {
            Screen("tut-menu-title", s =>                                       // 기록이 없는 타이틀: 「튜토리얼」 줄을 고르고 딱지
            {
                var flow = s.Flow();
                flow.OpenTitle(TitleScreen.Tutorial);
                var title = (TitleScreen)flow.Stage.Current;
                if (!title.FirstTimeShown) throw new InvalidOperationException("「처음이라면!」 딱지가 없다");
                TutFit(s, flow, "title-menu");
            }),
            Screen("tut-menu-empty", s => TutMenu(s, null)),                    // 기록이 없는 목차(본편 · 확장 장 — 단계가 없는 장은 「준비 중」)
            Screen("tut-menu-mixed", s => TutMenu(s, MixedRecord)),             // 완료 둘(0·1장) + 이어서(3장) + 확장 장 완료 하나(기록만 — 칸 모양 확인)
            Screen("tut-menu-tip", s =>                                         // 이어서 줄에 마우스를 올린 모습
            {
                var flow = TutMenu(s, MixedRecord);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("tut-menu-ch-main3"));
            }),
            Screen("tut-menu-quit", s =>                                        // 3장 시작(빨리감기로 연 판)에서 끝내기 창
            {
                var flow = TutBoard(s);
                if (!flow.Tutorial.OpenQuit()) throw new InvalidOperationException("끝내기 창이 열리지 않았다");
                TutFit(s, flow, "tut-quit-panel");
            }),
            Screen("tut-menu-gamemenu", s =>                                    // 튜토리얼 판의 게임 메뉴: 저장 줄 없음(결정 96 Q8) + [튜토리얼 끝내기]
            {
                var flow = TutBoard(s);
                flow.OpenMenu();
                Pass(flow, 16);   // 게임 메뉴가 떠 있으면 튜토리얼 층은 다음 프레임에 숨는다(07 T63)
                if (!(flow.Stage.Current is BoardMenuScreen)) throw new InvalidOperationException("게임 메뉴가 열리지 않았다");
                if (flow.Stage.Root.Q("board-menu-save") != null) throw new InvalidOperationException("튜토리얼 판의 게임 메뉴에 저장 줄이 있다(결정 96 Q8)");
                TutFit(s, flow, "board-menu-panel");
            }),
            Screen("tut-menu-end", s =>                                         // 본편 끝 창: 마지막 장(7장 — 각본 셋)을 [이 장 건너뛰기]로 끝까지 보낸 판(종막 선택 창 위)
            {
                var flow = TutBoard(s, "main7");
                if (!flow.Tutorial.SkipChapter() || !flow.Tutorial.Ending) throw new InvalidOperationException("마지막 장을 건너뛰어도 끝 창이 열리지 않았다");
                if (flow.Tutorial.Record.Seen("main7")) throw new InvalidOperationException("건너뛴 장이 완료로 적혔다");
                flow.Layout(s.Width, s.Height);
                flow.Stage.Tweens.Advance(BoardReadyMs);
                TutFit(s, flow, "tut-end-panel");
            }),
            // ── M9 「튜토리얼 → 설명서 더 보기」(07 T220): 목차의 완료한 장 [?] · 끝 창의 절 단추 → 룰 설명서 큰 창을 덮어 연다 ──
            Screen("tut-book-menu", s =>                                        // 완료한 장(본편 둘 · 확장 장 둘)의 [?] + 마우스 오버
            {
                var flow = TutMenu(s, BookRecord);
                var root = flow.Stage.Root;
                foreach (string id in new[] { "main0", "main1", "e01", "e05" })
                {
                    var b = root.Q("tut-menu-book-" + id) ?? throw new InvalidOperationException("[?] 단추가 없다: " + id);
                    var cell = root.Q("tut-menu-ch-" + id).worldBound;
                    var at = b.worldBound;
                    if (at.width < 1 || at.xMin < cell.xMin - 0.5f || at.xMax > cell.xMax + 0.5f || at.yMin < cell.yMin - 0.5f || at.yMax > cell.yMax + 0.5f)
                        throw new InvalidOperationException("[?] 단추가 그 장의 줄 · 칸 밖이다(" + id + ") " + at + " / " + cell);
                }
                if (root.Q("tut-menu-book-main3") != null) throw new InvalidOperationException("완료하지 않은 장에 [?]가 있다");
                flow.Stage.Tips.Show(root.Q("tut-menu-book-main1"));
            }),
            Screen("tut-book-menu-open", s =>                                   // 목차의 [?] → 룰 설명서 큰 창(그 장의 첫 절)이 목차 위에
            {
                var flow = TutMenu(s, BookRecord);
                var menu = (TutorialMenuScreen)flow.Stage.Current;
                if (!menu.More("main1") || flow.Tutorial.Book.Current.Key != "overview.setup") throw new InvalidOperationException("목차의 [?]가 그 장의 첫 절을 열지 않았다");
                TutBookCheck(s, flow);
            }),
            Screen("tut-book-menu-all", s => TutMenu(s, r =>                    // 23장을 모두 완료한 목차([?] 23개 — 판이 화면 안 · 글 넘침)
            {
                foreach (var ch in s.Made.Tutorial.Chapters) r.MarkSeen(ch.Id);
            })),
            Screen("tut-book-end", s => TutBookEnd(s, "main7", 3)),             // 본편 끝 창: 절 단추 셋
            Screen("tut-book-end-extra", s => TutBookEnd(s, "e13", 4)),         // 확장 장 끝 창: 절 단추 넷(가장 많은 장 — 두 줄로 감긴다)
            Screen("tut-book-end-open", s =>                                    // 끝 창의 절 단추 → 룰 설명서 큰 창이 끝 창 위에 → 닫으면 끝 창
            {
                var flow = TutBookEnd(s, "e04", 2);
                if (!flow.Tutorial.OpenBook("pokemon.evolution")) throw new InvalidOperationException("끝 창에서 설명서가 열리지 않았다");
                TutBookCheck(s, flow);
                if (!flow.Escape() || flow.Tutorial.BookUp || !flow.Tutorial.Ending) throw new InvalidOperationException("Esc로 설명서를 닫으면 끝 창이어야 한다");
                if (!flow.Tutorial.OpenBook("pokemon.evolution")) throw new InvalidOperationException("다시 열리지 않았다");
                s.Panel.Tick(3);
            }),
        };

        static void BookRecord(TutorialRecord r)
        {
            MixedRecord(r);
            r.MarkSeen("e05");
        }

        // 그 장을 [이 장 건너뛰기]로 끝까지 보낸 끝 창: 「설명서에서 더 보기」 절 단추 수 · 판 안 · 글 넘침
        static GameFlow TutBookEnd(UiShot s, string chapter, int buttons)
        {
            var flow = s.Flow();
            if (!flow.Tutorial.Start(chapter)) throw new InvalidOperationException("장을 시작하지 못했다: " + chapter);
            flow.Layout(s.Width, s.Height);
            Pass(flow, BoardReadyMs);
            if (!flow.Tutorial.SkipChapter() || !flow.Tutorial.Ending) throw new InvalidOperationException(chapter + ": 건너뛰어도 끝 창이 열리지 않았다");
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            TutFit(s, flow, "tut-end-panel");
            var panel = flow.Stage.Root.Q("tut-end-panel");
            var box = panel.Q("tut-end-book") ?? throw new InvalidOperationException(chapter + ": 끝 창에 「설명서에서 더 보기」가 없다");
            for (int i = 0; i < buttons; i++)
            {
                var b = box.Q("tut-end-book-" + i) ?? throw new InvalidOperationException(chapter + ": 절 단추 " + i + "가 없다");
                var at = b.worldBound;
                var outer = panel.worldBound;
                if (at.width < 1 || at.xMin < outer.xMin || at.xMax > outer.xMax || at.yMin < outer.yMin || at.yMax > outer.yMax)
                    throw new InvalidOperationException(chapter + ": 절 단추 " + i + "가 끝 창 밖이다 " + at + " / " + outer);
            }
            if (box.Q("tut-end-book-" + buttons) != null) throw new InvalidOperationException(chapter + ": 절 단추가 " + buttons + "개보다 많다");
            return flow;
        }

        // 덮어 연 룰 설명서: 판이 화면 안 · 칸이 판 안 · 글 넘침(BookCheck와 같은 단정 — 무대의 화면이 아니라 튜토리얼이 쥔 창)
        static void TutBookCheck(UiShot s, GameFlow flow)
        {
            s.Panel.Tick(3);
            var book = flow.Tutorial.Book ?? throw new InvalidOperationException("설명서가 떠 있지 않다");
            var root = flow.Stage.Root;
            var page = root.Q("rulebook") ?? throw new InvalidOperationException("rulebook 요소가 없다");
            string who = book.Lang + " " + book.Current.Key;
            var panel = root.Q("rulebook-panel");
            BookInside(page.worldBound, panel.worldBound, "판", who);
            foreach (var child in panel.Children()) BookInside(panel.worldBound, child.worldBound, "판의 칸", who);
            var bad = WorkUi.Overflows(page);
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + bad.Count + "곳(" + who + "): " + string.Join(" / ", bad));
        }

        static void MixedRecord(TutorialRecord r)
        {
            r.Enter("main0");
            r.MarkSeen("main0");
            r.Enter("main1");
            r.MarkSeen("main1");
            r.MarkSeen("e01");
            r.Enter("main3");
        }

        static GameFlow TutMenu(UiShot s, Action<TutorialRecord> record)
        {
            var flow = s.Flow();
            var r = TutorialRecord.Memory();
            record?.Invoke(r);
            flow.Tutorial.Record = r;
            flow.OpenTutorialMenu();
            var menu = (TutorialMenuScreen)flow.Stage.Current;
            if (menu.Cells.Count != flow.Tutorial.Chapters.Count) throw new InvalidOperationException("목차 칸 " + menu.Cells.Count + " ≠ 장 " + flow.Tutorial.Chapters.Count);
            TutFit(s, flow, "tut-menu-col");
            return flow;
        }

        // 목차에서 본편 장(기본 3장)을 고른 판(앞 장은 연출 없이 보냈다): 보드 HUD가 서고 연출이 빈 때
        static GameFlow TutBoard(UiShot s, string chapter = "main3")
        {
            var flow = s.Flow();
            if (!flow.Tutorial.Start(chapter)) throw new InvalidOperationException("본편 장을 시작하지 못했다: " + chapter);
            if (!flow.OnBoard) throw new InvalidOperationException(chapter + " 시작 국면이 보드가 아니다");
            flow.Layout(s.Width, s.Height);
            Pass(flow, BoardReadyMs);
            return flow;
        }

        // 배치를 세우고 그 판의 글 넘침을 단정한다
        static void TutFit(UiShot s, GameFlow flow, string name)
        {
            s.Panel.Tick(3);
            var e = flow.Stage.Root.Q(name) ?? throw new InvalidOperationException("요소 없음: " + name);
            var bad = WorkUi.Overflows(e);
            if (bad.Count > 0) throw new InvalidOperationException(name + " 글 넘침: " + string.Join(" / ", bad));
            var root = flow.Stage.Root.worldBound;
            var at = e.worldBound;
            if (at.xMin < root.xMin - 1f || at.yMin < root.yMin - 1f || at.xMax > root.xMax + 1f || at.yMax > root.yMax + 1f)
                throw new InvalidOperationException(name + "이 화면 밖으로 나간다(" + at + " · 화면 " + root + ")");
        }
    }
}
