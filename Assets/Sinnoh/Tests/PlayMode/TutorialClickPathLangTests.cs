using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M9 「튜토리얼 잇기」: 영어 설정(S02 「언어」 — <c>GameFlow.Lang</c>)에서도 각본 튜토리얼의 사람 클릭 경로가 끝 창까지 지난다(틀 <see cref="TutorialClickPath"/> — 단계 · 앵커 · 구멍 밖 삼킴 · 소리 단정은 한국어와 같다).
    /// 단계마다 말풍선 글과 층 단추에 한글이 없어야 한다. E1(운명 손패 · 배틀 안 운명 창) · E3(마을 창 · 은행상회 · 공중날기 — 영어 글이 긴 창).
    /// </summary>
    public sealed class TutorialClickPathLangTests
    {
        static IEnumerator English(string id) => TutorialClickPath.Run(id, p =>
        {
            Assert.That(p.Texts.Count, Is.EqualTo(p.Steps.Count), id + ": 단계마다 말풍선 글을 보았다");
            Assert.That(p.Menu.Record.Seen(id), Is.True, id + " 완료");
            UnityEngine.Debug.Log($"[클릭 경로 en] {id}: 단계 {p.Steps.Count} · 첫 말풍선 「{p.Texts[0]}」");
        }, "en");

        [UnityTest]
        public IEnumerator M9_영어_E1_클릭경로() => English("e01");

        [UnityTest]
        public IEnumerator M9_영어_E3_클릭경로() => English("e03");
    }
}
