using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 결정 196 버림 더미 보기 창(<see cref="DiscardScreen"/>)의 UI 캡처 장면: 버린 카드를 넣어 둔 판(약함 덱 전부 · 사당 이벤트 · 운명의 변화 · 왜곡 균열)에서 위 띠 [버림 더미] —
    /// 덱 목록 · 카드 칸 · 쪽 넘김 · 포켓몬 카드 마우스 오버 · 사당 이벤트 카드 마우스 오버(문구). 판이 화면 안 · 아래 판을 덮지 않고 · 글이 칸 밖으로 넘치지 않는다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] DiscardScreens =
        {
            Screen("discards", s => Discards(s, (int)DeckId.Weak, 0)),
            Screen("discards-mon-tip", s => Discards(s, (int)DeckId.Weak, 1)),
            Screen("discards-card-tip", s => Discards(s, (int)DeckId.ShrineEvent, 1)),
            Screen("discards-fate", s => Discards(s, (int)DeckId.ChangeOfFate, 0)),
            // 결정 196 덧: 포켓몬 카드의 기술 면(카드 뒷면 = 고유 기술) · 기술 줄 마우스 오버(이름 + 효과)
            Screen("discards-moves", s => Discards(s, (int)DeckId.Weak, 0, true)),
            Screen("discards-moves-tip", s => Discards(s, (int)DeckId.Weak, 1, true)),
        };

        // 버린 카드를 넣어 둔 판에서 창을 열고 그 덱을 본다(hover = 그 쪽의 n번째 칸에 마우스 — 0이면 없음)
        static void Discards(UiShot s, int deck, int hover, bool moves = false)
        {
            var flow = Seats(s, 2, st =>
            {
                var ctx = s.Board.Ctx;
                var weak = st.Discards[(int)DeckId.Weak];
                foreach (var e in ctx.Decks.Decks[(int)DeckId.Weak].Cards) weak.Add(e.Card);
                weak.Add(ctx.Decks.Decks[(int)DeckId.Weak].Cards[0].Card);   // 같은 카드 둘 = 「×2」
                foreach (string key in new[] { "shrine_event.equivalent_exchange", "shrine_event.ancient_wisdom", "shrine_event.beacon_of_light" })
                    st.Discards[(int)DeckId.ShrineEvent].Add(ctx.Decks.CardIds.IndexOf(key));
                foreach (string key in new[] { "change_of_fate.arceus", "change_of_fate.dialga", "change_of_fate.dialga" })
                    st.Discards[(int)DeckId.ChangeOfFate].Add(ctx.Decks.CardIds.IndexOf(key));
                st.Discards[(int)DeckId.DistortionBreach].Add(ctx.Decks.CardIds.IndexOf("distortion_breach.chromatic_union"));
            });
            if (!flow.ToggleDiscards() || !(flow.Front is DiscardScreen w)) throw new InvalidOperationException("[버림 더미] 창이 열리지 않았다");
            if (!w.PickDeck(deck)) throw new InvalidOperationException("덱 " + deck + "을(를) 고르지 못했다");
            if (moves && (!w.Flip() || !w.MoveFace)) throw new InvalidOperationException("[기술 면]으로 바꾸지 못했다");
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var panel = root.Q("board-discards-panel").worldBound;
            var whole = root.worldBound;
            if (panel.xMin < whole.xMin - 0.5f || panel.xMax > whole.xMax + 0.5f || panel.yMax > whole.yMax + 0.5f) throw new InvalidOperationException("창이 화면 밖이다: " + panel);
            var dock = root.Q("board-hud-dock").worldBound;
            if (panel.yMax > dock.yMin + 0.5f) throw new InvalidOperationException("창이 아래 판을 덮는다");
            var list = root.Q("board-discards-list").worldBound;
            foreach (var chip in root.Q("board-discards-list").Children())
                if (chip.worldBound.yMax > list.yMax + 0.5f || chip.worldBound.xMax > list.xMax + 0.5f) throw new InvalidOperationException("카드 칸이 목록 밖이다: " + chip.name);
            var bad = WorkUi.Overflows(root.Q("board-discards-panel")).FindAll(m => !m.Contains("글이 잘린다"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
            if (hover > 0 && !w.Hover(hover)) throw new InvalidOperationException("카드 칸 " + hover + "이(가) 없다");
            s.Panel.Tick(3);
        }
    }
}
