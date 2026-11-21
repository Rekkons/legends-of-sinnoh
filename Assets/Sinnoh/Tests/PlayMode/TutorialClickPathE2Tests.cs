using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Rules.Tutorial;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M6.5 「클릭 경로 ②」(07 §8 완료 조건 3): 확장 장 E2~E8의 사람 클릭 경로 — 목차의 장 고르기 → 단계마다 실제 앵커 요소를 눌러 끝 창까지(틀 <see cref="TutorialClickPath"/>).
    /// 단계 차례 = 각본(막 앞 판 차리기 demo는 빨리감기로 지난다 — 07 T62) · 끝까지 본 장은 완료 · 에러 로그와 <c>[튜토리얼]</c> 경고 0.
    /// </summary>
    public sealed class TutorialClickPathE2Tests
    {
        // 화면에 뜨는 단계: 막 앞 판 각본(start.after)은 손보기가 걸릴 때까지 판 차리기를 빨리감기로 지난다(07 T62 — 장마다 앞 단계 수가 다르다: E7 셋 · E8 여섯).
        // 그래서 처음 뜬 단계부터 끝까지가 각본 그대로이고, 빨리감기로 지난 단계에는 사람 몫(지정 행동 · 창 열기 · 대사)이 없어야 한다 — 말없는 판 차리기(demo·bot, 07 T160)뿐 · 뜬 단계는 모두 대사가 있다
        static void CheckSteps(string id, List<string> shown)
        {
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "Sinnoh/Data/Json/tutorial/" + id + ".json"));
            var ctx = Object.FindAnyObjectByType<Sinnoh.Client.ScreenBoot>().Flow.Ctx;
            var script = Sinnoh.Data.TutorialLoader.Load(json, ctx, "tutorial/" + id + ".json");
            Assert.That(shown, Is.Not.Empty, id + ": 뜬 단계가 없다");
            int from = script.Steps.FindIndex(s => id + "." + s.Id == shown[0]);
            Assert.That(from, Is.GreaterThanOrEqualTo(0), id + ": 처음 뜬 단계가 각본에 없다 — " + shown[0]);
            if (script.Start.After == null) Assert.That(from, Is.EqualTo(0), id + ": 막 앞 판이 아닌 장은 첫 단계부터 뜬다");
            var all = new List<string>();
            for (int i = 0; i < script.Steps.Count; i++)
            {
                var st = script.Steps[i];
                if (i >= from)
                {
                    all.Add(id + "." + st.Id);
                    Assert.That(st.Text, Is.Not.Null, $"{id}.{st.Id}: 화면에 뜨는 단계에 대사가 없다");
                }
                else Assert.That((st.Advance == StepAdvance.Demo || st.Advance == StepAdvance.Bot) && st.Text == null && st.Anchor == null, Is.True,
                    $"{id}.{st.Id}: 빨리감기로 지난 단계에 사람 몫({st.Advance} · 대사 {st.Text ?? "없음"} · 앵커 {st.Anchor ?? "없음"})");
            }
            CollectionAssert.AreEqual(all, shown, id + " 단계를 차례로 모두 눌렀다");
        }

        static IEnumerator Chapter(string id) => TutorialClickPath.Run(id, p =>
        {
            CheckSteps(id, p.Steps);
            Assert.That(p.Menu.Record.Seen(id), Is.True, id + " 끝까지 본 장은 완료");
        });

        [UnityTest]
        public IEnumerator M65_E2_클릭경로() => TutorialClickPath.Run("e02", p =>
        {
            CheckSteps("e02", p.Steps);
            Assert.That(p.Menu.Record.Seen("e02"), Is.True, "e02 끝까지 본 장은 완료");
            Assert.That(p.Drags, Is.EqualTo(1), "S011 별 방패를 끌어 팽도리 칸에 놓았다(대사가 「끌어」 — 결정 107 ①)");
        });

        [UnityTest]
        public IEnumerator M65_E3_클릭경로() => Chapter("e03");

        [UnityTest]
        public IEnumerator M65_E4_클릭경로() => Chapter("e04");

        [UnityTest]
        public IEnumerator M65_E5_클릭경로() => Chapter("e05");

        [UnityTest]
        public IEnumerator M65_E6_클릭경로() => Chapter("e06");

        [UnityTest]
        public IEnumerator M65_E7_클릭경로() => Chapter("e07");

        [UnityTest]
        public IEnumerator M65_E8_클릭경로() => Chapter("e08");
    }
}
