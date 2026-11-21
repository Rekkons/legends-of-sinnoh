using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Rules;
using Sinnoh.Rules.Tutorial;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M6.5 「클릭 경로 ③」(07 §8 완료 조건 3): 확장 장 E9~E15를 목차에서 골라 끝 창까지 사람처럼 누른다(틀 <see cref="TutorialClickPath"/>).
    /// 화면에 뜨는 단계 = 셋업(트레이너 카드 고르기 demo)까지의 판 차리기를 빨리감기로 지난 뒤의 모든 단계(07 T62).
    /// 봇이 있는 장(E10 · E13 · E14)은 bot 단계마다 봇 턴 띠가 뜨고 봇이 그 단계에서 풀어 준 만큼만 답했는지를 틀이 본다.
    /// </summary>
    public sealed class TutorialClickPathE9Tests
    {
        // 화면에 뜨는 단계: 셋업을 끝내는 demo(트레이너 카드 고르기) 뒤부터 — 막 앞 판 손보기가 그때 걸린다
        static List<string> Visible(string id)
        {
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "Sinnoh/Data/Json/tutorial/" + id + ".json"));
            var ctx = Object.FindAnyObjectByType<Sinnoh.Client.ScreenBoot>().Flow.Ctx;
            var script = Sinnoh.Data.TutorialLoader.Load(json, ctx, "tutorial/" + id + ".json");
            int from = 0;
            for (int i = 0; i < script.Steps.Count; i++)
                if (script.Steps[i].Auto.Exists(p => p.Choice == ChoiceKind.TrainerCardPick)) from = i + 1;
            var all = new List<string>();
            for (int i = 0; i < script.Steps.Count; i++)
            {
                var st = script.Steps[i];
                if (i >= from) all.Add(id + "." + st.Id);
                // 빨리감기로 지나는 판 차리기 = 말없는 demo·bot(07 T160) · 화면에 뜨는 단계 = 모두 대사가 있다
                Assert.That(i < from ? (st.Advance == StepAdvance.Demo || st.Advance == StepAdvance.Bot) && st.Text == null && st.Anchor == null : st.Text != null, Is.True,
                    $"{id}.{st.Id}: " + (i < from ? "빨리감기로 지난 단계에 사람 몫" : "화면에 뜨는 단계에 대사가 없다"));
            }
            return all;
        }

        static IEnumerator Chapter(string id) => TutorialClickPath.Run(id, p =>
        {
            CollectionAssert.AreEqual(Visible(id), p.Steps, id + " 단계를 차례로 모두 눌렀다");
            Assert.That(p.Menu.Record.Seen(id), Is.True, id + " 완료");
        });

        [UnityTest]
        public IEnumerator M65_E9_클릭경로() => Chapter("e09");

        [UnityTest]
        public IEnumerator M65_E10_클릭경로() => Chapter("e10");

        [UnityTest]
        public IEnumerator M65_E11_클릭경로() => Chapter("e11");

        [UnityTest]
        public IEnumerator M65_E12_클릭경로() => Chapter("e12");

        [UnityTest]
        public IEnumerator M65_E13_클릭경로() => Chapter("e13");

        [UnityTest]
        public IEnumerator M65_E14_클릭경로() => Chapter("e14");

        [UnityTest]
        public IEnumerator M65_E15_클릭경로() => Chapter("e15");
    }
}
