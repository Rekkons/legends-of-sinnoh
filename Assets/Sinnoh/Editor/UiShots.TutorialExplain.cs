using System;
using System.Collections.Generic;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 사용자 결정 204(2026-11-22 「튜토리얼에서 설명이 너무 부실한 것들이 많아」): 설명을 더한 단계를 그 단계 그대로 찍는다(<see cref="TutClick"/> — 진행기 자동 진행으로 그 단계까지,
    /// 구멍이 없거나 마박사 · 말풍선이 덮으면 실패). 걷기(칸을 두 번 눌러 걷기 · 오른쪽 클릭으로 물리기) · 볼 던지기 · 디아루가 · 기라티나 · 균열 강도 · 왜곡 균열 카드 · 프리즘 아머 ·
    /// 라운드 끝 물음 · 게이트. 말풍선 줄 수는 <c>tut-layer-lines</c>가 본다. 그림 <c>Logs/Review/m10/tutorial-explain/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] TutorialExplainScreens =
        {
            Screen("tut-explain-main-s302", s => TutClick(s, "main", "S302")),
            Screen("tut-explain-main-s406", s => TutClick(s, "main", "S406")),
            Screen("tut-explain-main-s501", s => TutClick(s, "main", "S501")),
            Screen("tut-explain-e01-s011", s => TutClick(s, "e01", "S011")),
            Screen("tut-explain-e01-s013", s => TutClick(s, "e01", "S013")),
            Screen("tut-explain-e05-s006", s => TutClick(s, "e05", "S006")),
            Screen("tut-explain-e13-s007", s => TutClick(s, "e13", "S007")),
            Screen("tut-explain-e13-s015", s => TutClick(s, "e13", "S015")),
            Screen("tut-explain-e14-s011", s => TutSee(s, "e14", "S011")),
            Screen("tut-explain-e14-s015", s => TutClick(s, "e14", "S015")),
            Screen("tut-explain-e15-s013", s => TutClick(s, "e15", "S013")),
        };

        // 앵커 없는 말풍선 단계([다음]만): 그 단계까지 보내 말풍선만 찍는다
        static void TutSee(UiShot s, string script, string stepId)
        {
            TutStart(s, TutorialScriptAsset(s.Board.Ctx, script), stepId);
            for (int i = 0; i < 4; i++) TutSettle(s);
        }
    }
}
