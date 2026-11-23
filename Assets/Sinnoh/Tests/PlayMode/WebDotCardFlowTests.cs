using System.Collections;
using NUnit.Framework;
using Sinnoh.Client;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 결정 206 검토 지적 21: 카드 그림이 없는 길(웹 손님판 — 카드 번들을 싣지 않아 배틀 기둥이 도트 카드)에서도 기둥 칸 이름(<c>battle-me-hp</c> · <c>battle-foe-alpha</c> 등)이 붙어
    /// 각본 튜토리얼의 그 앵커(E5 S013 · E6 S012)를 사람처럼 눌러 지난다(틀 <see cref="TutorialClickPath"/>). 에디터에서는 <see cref="CardStack.NoArt"/>로 그 길을 연다.
    /// </summary>
    public sealed class WebDotCardFlowTests
    {
        static IEnumerator Dots(string id)
        {
            CardStack.NoArt = true;
            try
            {
                yield return TutorialClickPath.Run(id, p => Assert.That(p.Menu.Record.Seen(id), Is.True, id + " 완료(도트 카드 기둥)"));
            }
            finally
            {
                CardStack.NoArt = false;
            }
        }

        [UnityTest]
        public IEnumerator M10_결정206_웹도트카드기둥_E5우두머리앵커() => Dots("e05");

        [UnityTest]
        public IEnumerator M10_결정206_웹도트카드기둥_E6체력앵커() => Dots("e06");
    }
}
