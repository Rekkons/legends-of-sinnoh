using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 결정 191 · 192(2026-11-21): 정비 판의 [조합표]는 판 위를 덮는 보기 창(레시피 줄 = 데이터의 레시피 수 · 누를 단추는 [닫기]뿐)이고 다시 누르거나 Esc로 닫힌다(Esc는 조합표만 — 정비 판은 그대로).
    /// 기술 카드의 등급 상자는 그 등급 색 클래스(<c>sn-grade-&lt;등급&gt;</c>)를 단다. UiShots <c>recipe-*</c> · <c>grade-pool</c>이 같은 판을 찍는다.
    /// </summary>
    public sealed class RecipeGradeFlowTests
    {
        const ulong Seed = 20261112, AiSeed = 7;

        [UnityTest]
        public IEnumerator M10_결정191_192_조합표는정비판위에열고닫히고_기술카드등급상자에색이있다()
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
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var ctx = flow.Ctx;
                flow.StartSeats(new SoloSettings(), 1, Seed, AiSeed, state =>
                {
                    // 기술 카드 셋(강함 · 보통 · 약함 덱에서 한 장씩 — 스탬프 보상처럼)
                    var st = state.Seats[0];
                    foreach (var deck in new[] { DeckId.Strong, DeckId.Moderate, DeckId.Weak })
                        for (int i = 0; i < ctx.Cards.Pokemon.Count; i++)
                        {
                            var def = ctx.Cards.Pokemon[i];
                            if (def.Signature < 0 || st.StampMoves.Contains(def.Signature) || !state.Decks[(int)deck].Contains(i)) continue;
                            st.StampCards.Add((int)deck);
                            st.StampCards.Add(i);
                            st.StampMoves.Add(def.Signature);
                            break;
                        }
                });
                yield return null;
                flow.Skip(flow.OpeningMs);
                for (int i = 0; i < 400 && !flow.InputOpen; i++) flow.Skip(100);
                Assert.That(flow.OpenParty(), Is.True, "[정비]");
                yield return null;
                var party = (PartyScreen)stage.Current;
                var root = stage.Root;

                // 192: 기술 카드 등급 상자의 색
                foreach (var g in new[] { "strong", "moderate", "weak" })
                    Assert.That(root.Q("board-party-pool").Query(className: "sn-grade-" + g).ToList().Count, Is.EqualTo(1), g + " 등급 상자");

                // 191: 조합표
                int want = 0;
                foreach (var d in ctx.Items.Items) if (d.Recipe != null && (d.Kind == ItemKind.Ball || d.Kind == ItemKind.Stew || d.Kind == ItemKind.Held)) want++;
                Assert.That(want, Is.GreaterThan(0));
                Assert.That(root.Q("board-party-recipes-button"), Is.Not.Null, "[조합표]");
                party.ToggleRecipes();
                yield return null;
                Assert.That(party.RecipesOpen, Is.True, "누르면 열린다");
                Assert.That(party.RecipeCount, Is.EqualTo(want), "레시피 줄 = 데이터의 레시피 수");
                Assert.That(root.Q("board-party-recipes").Query(className: "sn-craft-row").ToList().Count, Is.EqualTo(want));
                Assert.That(root.Q("board-party-recipes").Query<Button>().ToList().Count, Is.EqualTo(1), "보기 전용 — [닫기]뿐");
                party.ToggleRecipes();
                Assert.That(party.RecipesOpen, Is.False, "다시 누르면 닫힌다(결정 176)");
                Assert.That(root.Q("board-party-recipes"), Is.Null);
                party.ToggleRecipes();
                Assert.That(flow.Escape(), Is.True, "Esc");
                Assert.That(party.RecipesOpen, Is.False, "Esc는 조합표를 닫는다");
                Assert.That(stage.Current, Is.SameAs(party), "정비 판은 그대로");
                Assert.That(flow.Escape(), Is.True);
                Assert.That(stage.Current, Is.Not.InstanceOf<PartyScreen>(), "한 번 더 Esc = 정비 판 닫기");
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var shut = Object.FindAnyObjectByType<ScreenBoot>();
                if (shut != null && shut.Root != null) shut.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }
    }
}
