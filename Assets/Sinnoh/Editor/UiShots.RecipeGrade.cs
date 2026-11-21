using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 사용자 결정 191 · 192(2026-11-21): 정비 판의 [조합표](판 위를 덮는 보기 창 — 제작 창의 레시피 줄 그대로, 효과는 마우스 오버 · 배틀 중에도)와
    /// 기술 카드 등급 상자의 등급 색(약함 · 보통 · 강함). 판은 PlayTestA의 정비 판(강함 · 보통 · 약함 기술 카드 14장). 그림 <c>Logs/Review/m10/recipe-grade/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] RecipeGradeScreens =
        {
            Screen("recipe-book", s => RecipeBook(s, null)),                                   // 정비 판 위의 조합표
            Screen("recipe-book-tip", s => RecipeBook(s, "board-party-recipe-choice_band")),    // 지닌물건 줄에 마우스 — 효과 글
            Screen("recipe-book-battle", RecipeBattle),                                        // 배틀 중 [정비] → [조합표]
            Screen("grade-pool", GradePool),                                                   // 기술 카드의 등급 상자 색
        };

        static int RecipeData(Ctx ctx)
        {
            int n = 0;
            foreach (var d in ctx.Items.Items)
                if (d.Recipe != null && (d.Kind == ItemKind.Ball || d.Kind == ItemKind.Stew || d.Kind == ItemKind.Held)) n++;
            return n;
        }

        // 조합표가 열렸고 줄 수 = 데이터 수 · 판이 화면 안 · 글 넘침 없음
        static void RecipeCheck(UiShot s, PartyScreen party)
        {
            var root = s.Made.Stage.Root;
            if (!party.RecipesOpen) throw new InvalidOperationException("조합표가 열리지 않았다");
            int want = RecipeData(s.Made.Ctx);
            if (party.RecipeCount != want || root.Query(className: "sn-craft-row").ToList().FindAll(e => e.name != null && e.name.StartsWith("board-party-recipe-", StringComparison.Ordinal)).Count != want)
                throw new InvalidOperationException("조합표 줄 " + party.RecipeCount + " ≠ 레시피 " + want);
            if (root.Q("board-party-recipes").Query<Button>().ToList().Count != 1) throw new InvalidOperationException("조합표에 [닫기] 말고 누를 단추가 있다");
            var box = root.Q("board-party-recipes").worldBound;
            if (box.xMin < root.worldBound.xMin || box.xMax > root.worldBound.xMax || box.yMin < root.worldBound.yMin || box.yMax > root.worldBound.yMax) throw new InvalidOperationException("조합표가 화면 밖: " + box);
            var bad = WorkUi.Overflows(root.Q("board-party-recipes"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
        }

        static void RecipeBook(UiShot s, string tip)
        {
            var party = PartyA(s);
            party.ToggleRecipes();
            s.Panel.Tick(3);
            RecipeCheck(s, party);
            if (tip == null) return;
            var row = s.Made.Stage.Root.Q(tip) ?? throw new InvalidOperationException("요소 없음: " + tip);
            s.Made.Stage.Tips.Show(row);
            s.Panel.Tick(3);
        }

        static void RecipeBattle(UiShot s)
        {
            var flow = Grunts(s, "483:2", false, 0);
            Fight(flow, MyTurn);
            if (!flow.OpenParty() || !(flow.Top is PartyScreen party)) throw new InvalidOperationException("배틀 중 정비 판이 열리지 않았다");
            s.Panel.Tick(3);
            party.ToggleRecipes();
            s.Panel.Tick(3);
            RecipeCheck(s, party);
        }

        static void GradePool(UiShot s)
        {
            var party = PartyA(s);
            var root = s.Made.Stage.Root;
            foreach (var g in new[] { "strong", "moderate", "weak" })
                if (root.Q("board-party-pool").Query(className: "sn-grade-" + g).ToList().Count == 0) throw new InvalidOperationException("기술 카드에 「" + g + "」 등급 색 상자가 없다");
            var bad = WorkUi.Overflows(root.Q("board-party"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
        }
    }
}
