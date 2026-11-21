using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 사용자 결정 199(2026-11-21): ① 무리 줄 칸에 마우스 = 그 포켓몬 카드(아직 안 나온 칸 · 쓰러진 칸) ② 알파 스트라이크 타입 줄의 상성 0 = 「±0」 배지.
    /// 판은 row-24-swarm · row-81-dot(왜곡 게이트 무리 배틀)와 row-76 알파 스트라이크 타입 줄과 같다. 그림 <c>Logs/Review/m10/swarm-alpha/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] SwarmAlphaScreens =
        {
            Battle("swarm-tip-next", s => SwarmTip(s, 1, 3)),   // 아직 안 나온 셋째
            Battle("swarm-tip-done", s => SwarmTip(s, 2, 1)),   // 쓰러진 첫째(흐린 칸)
            Battle("alpha-even", AlphaEven),                    // 타입 줄 — 상성 0은 「±0」
        };

        static void SwarmTip(UiShot s, int stage, int n)
        {
            var flow = SwarmBattle(s, stage);
            var screen = (BattleScreen)flow.Stage.Current;
            if (!screen.HoverSwarm(n)) throw new InvalidOperationException("무리 칸 " + n + "에 카드가 걸리지 않았다");
            s.Panel.Tick(3);
            var view = flow.Client.View;
            int want = view.Pokemon[view.Battle.Sides[1 - flow.Battle.MySide].Roster[n - 1]].Def;
            if (screen.MonTipShown != want) throw new InvalidOperationException("카드 " + screen.MonTipShown + " ≠ 무리 칸 " + n + "의 포켓몬 " + want);
            var card = screen.MonTipElement.worldBound;
            var root = flow.Stage.Root.worldBound;
            if (card.xMin < root.xMin || card.xMax > root.xMax || card.yMin < root.yMin || card.yMax > root.yMax) throw new InvalidOperationException("카드가 화면 밖: " + card);
            var bad = WorkUi.Overflows(screen.MonTipElement);
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
        }

        static void AlphaEven(UiShot s)
        {
            // 전설 특수기술을 가진 펄기아(물 · 드래곤 · 전기 · 얼음) — 조무래기의 강철 포켓몬에게 물 · 전기는 상성 0
            var flow = Grunts(s, "484:2", true, 0);
            var screen = Fight(flow, MyTurn);
            int strike = -1;
            for (int i = 0; i < screen.Moves.Count; i++) if (screen.Moves[i].Target == BattleRules.AlphaStrike) strike = i;
            if (strike < 0 || !screen.PressMove(strike) || !screen.AlphaOpen) throw new InvalidOperationException("타입 줄이 열리지 않았다");
            s.Panel.Tick(3);
            int even = 0;
            for (int i = 0; i < screen.AlphaTypes.Count; i++)
            {
                var row = flow.Stage.Root.Q("battle-alpha-" + (i + 1));
                if (row == null) throw new InvalidOperationException("타입 줄 " + (i + 1) + "이 없다");
                bool badge = false;
                row.Query<Label>().ForEach(l => { if (l.ClassListContains("kit-badge")) { badge = true; if (l.text == BattleScreen.Even) even++; } });
                if (!badge && (PokemonType)screen.AlphaTypes[i].Values[0] != PokemonType.Typeless) throw new InvalidOperationException("타입 줄 " + (i + 1) + "에 상성 배지가 없다");
            }
            if (even == 0) throw new InvalidOperationException("상성 0인 타입 줄이 없다(「±0」을 보지 못했다)");
        }
    }
}
