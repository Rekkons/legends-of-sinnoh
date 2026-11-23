using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 결정 206 ② 배틀 정보 기둥(목업 <c>Logs/Review/card/</c> r10-battle-full · -hover · -entry · -entry-mine · r2-battle-swarm · -swarm-hover): 좌우 카드 · 카드 글 판 · 엔트리 판 · 무리 엇겹침.
    /// 판은 견본 여섯(<see cref="SoloTrail.PrepareCards"/> — 토대부기 · 초염몽 · 디아루가 · 렌트라 · 찌르호크(기절) · 팽도리)으로 고향 마을 조무래기에게 [도전]한 내 차례, 무리는 왜곡 게이트.
    /// 단정: 카드가 쪽 칸 · 기둥 안 · 두 쪽 같은 폭(<see cref="InfoCards"/>) · 판이 기둥 오른쪽부터 화면 안 · 카드 수 · 꼬리표 · 글 넘침(<see cref="EntryCheck"/>) · 글 판이 화면 안 · 독을 덮지 않음(<see cref="TipCheck"/>).
    /// 그림 <c>Logs/Review/card/impl2b/</c>. PlayMode <c>BattleInfoFlowTests</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] BattleInfoScreens =
        {
            Battle("battle-info-full", s => InfoCards(InfoDuel(s, MyTurn), true)),                                     // 좌우 카드 + [내 엔트리 6] · [상대 엔트리 2]
            Battle("battle-info-retri", s => InfoCards(InfoDuel(s, b => MyTurn(b) && b.RetributionShown > 0), true)),   // 동미러가 쓰러진 뒤 — 응징 칩 + 갤럭시단 띠의 피해 주사위
            Battle("battle-info-hover", s => TipCheck(s, InfoDuel(s, MyTurn), true)),                                   // 내 카드 마우스 오버 = 글 판(기술 · 지닌물건 · 상태)
            Battle("battle-info-hover-foe", s => TipCheck(s, InfoDuel(s, MyTurn), false)),                              // 상대 카드(플랜 B · 상성)
            Battle("battle-entry", s => EntryCheck(s, InfoDuel(s, MyTurn), false)),                                     // [상대 엔트리] — 카드 + 갤럭시단 띠 · 꼬리표
            Battle("battle-entry-mine", s => EntryCheck(s, InfoDuel(s, MyTurn), true)),                                 // [내 엔트리] — 여섯 장(기절 포함)
            Battle("battle-swarm-cards", s => SwarmCards(s, false)),                                                    // 무리: 카드 셋 엇겹침(나와 있는 것이 맨 앞)
            Battle("battle-swarm-hover", s => SwarmCards(s, true)),                                                     // 엇겹친 카드에 마우스 = 셋을 펼친 판
        };

        // 견본 여섯으로 고향 마을 조무래기에게 [도전] → until까지
        static GameFlow InfoDuel(UiShot s, Func<BattleScreen, bool> until)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 1, st => SoloTrail.PrepareCards(ctx, st));
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
            Fight(flow, until);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            return flow;
        }

        // 두 쪽 카드: 카드 그림 · 쪽 카드 칸 안(잘리지 않음) · 기둥 안 · 같은 폭 · 머리 줄 엔트리 단추(명단이 둘 이상인 쪽만, 글의 n)
        static GameFlow InfoCards(GameFlow flow, bool both)
        {
            var root = flow.Stage.Root;
            var screen = (BattleScreen)flow.Stage.Current;
            var info = root.Q("battle-info").worldBound;
            float width = -1f;
            foreach (string side in new[] { "battle-me", "battle-foe" })
            {
                var card = root.Q(side) ?? throw new InvalidOperationException(side + " 카드가 없다");
                var art = root.Q(side + "-art") ?? throw new InvalidOperationException(side + "-art가 없다");
                if (art.resolvedStyle.backgroundImage.texture == null) throw new InvalidOperationException(side + " 카드 앞면 그림이 없다(cardfaces)");
                var holder = root.Q(side + "-cards").worldBound;
                var box = card.worldBound;
                if (box.width <= 0f || box.xMin < holder.xMin - 0.5f || box.xMax > holder.xMax + 0.5f || box.yMax > holder.yMax + 0.5f) throw new InvalidOperationException(side + " 카드가 칸 밖으로 잘린다(" + box + " · 칸 " + holder + ")");
                if (box.xMin < info.xMin || box.xMax > info.xMax || box.yMax > info.yMax) throw new InvalidOperationException(side + " 카드가 기둥 밖이다");
                if (side == "battle-foe" && root.Q("battle-swarm-slot-1") != null) continue;
                var hp = root.Q(side + "-hp") ?? throw new InvalidOperationException(side + "-hp 칸이 없다");
                if (!card.Contains(hp)) throw new InvalidOperationException(side + "-hp가 카드 밖이다");
                if (width < 0f) width = box.width;
                else if (Math.Abs(width - box.width) > 1f) throw new InvalidOperationException("두 쪽 카드 폭이 다르다(" + width + " · " + box.width + ")");
            }
            if (!screen.FitCardsDone) throw new InvalidOperationException("카드 폭을 자리에 맞추지 않았다");
            for (int k = 0; k < 2; k++)
            {
                int n = screen.EntryCount(k == 0);
                var button = root.Q<Button>(k == 0 ? BattleScreen.EntryMineButton : BattleScreen.EntryFoeButton);
                if ((n >= 2) != (button != null)) throw new InvalidOperationException((k == 0 ? "[내 엔트리]" : "[상대 엔트리]") + " 단추 " + (button != null ? "있음" : "없음") + " · 엔트리 " + n);
                if (button != null && !button.text.EndsWith(" " + n, StringComparison.Ordinal)) throw new InvalidOperationException("엔트리 단추 글 「" + button.text + "」의 수가 " + n + "가 아니다");
                if (button != null && button.worldBound.xMax > info.xMax + 0.5f) throw new InvalidOperationException("엔트리 단추가 기둥 밖이다");
            }
            Debug.Log($"[UiShots] battle-info {flow.Stage.Grade.TokenHeight}: 카드 폭 {screen.CardWidth} · 기둥 {info}");
            return flow;
        }

        // 카드 글 판: 뜨고 · 화면 안 · 독 · 기둥과 겹치지 않음 · 줄(기술 · 지닌물건 · 상태) · 글 넘침 없음
        static void TipCheck(UiShot s, GameFlow flow, bool mine)
        {
            InfoCards(flow, true);
            var screen = (BattleScreen)flow.Stage.Current;
            if (!screen.HoverCard(mine)) throw new InvalidOperationException("카드에 글 판이 걸려 있지 않다");
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var tip = screen.CardTipElement;
            if (tip == null || tip.resolvedStyle.display == DisplayStyle.None) throw new InvalidOperationException("카드 글 판이 뜨지 않았다");
            Rect at = tip.worldBound, all = root.worldBound;
            if (at.xMin < all.xMin || at.yMin < all.yMin || at.xMax > all.xMax + 0.5f || at.yMax > all.yMax + 0.5f) throw new InvalidOperationException("글 판이 화면 밖이다 " + at);
            foreach (string other in new[] { "battle-dock", "battle-info" })
                if (root.Q(other).worldBound.Overlaps(at)) throw new InvalidOperationException("글 판이 " + other + "을 덮는다(" + at + " · " + root.Q(other).worldBound + ")");
            if (tip.Q("battle-card-tip-move-0") == null) throw new InvalidOperationException("글 판에 ★ 고유 기술 줄이 없다");
            var pk = flow.Client.View.Pokemon[screen.ShownMon(mine ? flow.Battle.MySide : 1 - flow.Battle.MySide).Id];
            int items = (pk.Alpha ? 1 : 0) + (pk.HeldItem >= 0 ? 1 : 0) + (pk.HeldItem2 >= 0 ? 1 : 0);
            if ((tip.Q("battle-card-tip-item-" + items) == null) != (items == 0) || tip.Q("battle-card-tip-item-" + (items + 1)) != null) throw new InvalidOperationException("글 판 지닌물건 줄 수가 " + items + "가 아니다");
            var bad = WorkUi.Overflows(tip);
            if (bad.Count > 0) throw new InvalidOperationException("글 판 글자 넘침 " + string.Join(" / ", bad));
        }

        // 엔트리 판: 단추로 열고(다시 누르기 · Esc = 닫힘 확인 뒤 다시 연다) · 기둥 오른쪽부터 화면 안 · 카드 수 = 엔트리 · 카드가 판 안 · 꼬리표(배틀 중 하나) · 글 넘침 없음
        static void EntryCheck(UiShot s, GameFlow flow, bool mine)
        {
            InfoCards(flow, true);
            var screen = (BattleScreen)flow.Stage.Current;
            var root = flow.Stage.Root;
            var button = root.Q<Button>(mine ? BattleScreen.EntryMineButton : BattleScreen.EntryFoeButton) ?? throw new InvalidOperationException("엔트리 단추가 없다");
            if (!screen.ToggleEntry(mine) || !screen.EntryOpen) throw new InvalidOperationException("엔트리 판이 열리지 않았다");
            if (screen.ToggleEntry(mine) || screen.EntryOpen) throw new InvalidOperationException("단추를 다시 눌러도 닫히지 않았다(결정 176)");
            screen.ToggleEntry(mine);
            if (!flow.Escape() || screen.EntryOpen) throw new InvalidOperationException("Esc로 닫히지 않았다");
            if (flow.Stage.Current != screen) throw new InvalidOperationException("Esc가 배틀 화면까지 걷었다");
            screen.ToggleEntry(mine);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (!root.Q<Button>(mine ? BattleScreen.EntryMineButton : BattleScreen.EntryFoeButton).ClassListContains(BattleScreen.EntryOnClass)) throw new InvalidOperationException("열린 쪽 단추에 켜짐 표시가 없다");
            var panel = root.Q(BattleScreen.EntryPanel);
            Rect at = panel.worldBound, all = root.worldBound, info = root.Q("battle-info").worldBound, dock = root.Q("battle-dock").worldBound;
            if (at.xMin < info.xMax - 0.5f || at.xMax > all.xMax + 0.5f || at.yMin < all.yMin || at.yMax > dock.yMin + 0.5f) throw new InvalidOperationException("엔트리 판 자리 " + at + "(기둥 " + info + " · 독 " + dock + ")");
            int n = screen.EntryCount(mine);
            if (screen.EntryShown.Count != n) throw new InvalidOperationException("판 카드 " + screen.EntryShown.Count + " ≠ 엔트리 " + n);
            int active = 0;
            for (int i = 1; i <= n; i++)
            {
                var col = root.Q(BattleScreen.EntryPanel + "-" + i) ?? throw new InvalidOperationException("판 카드 " + i + "이 없다");
                var box = col.worldBound;
                if (box.xMax > at.xMax + 0.5f || box.yMax > at.yMax + 0.5f) throw new InvalidOperationException("판 카드 " + i + "이 판 밖이다(" + box + " · 판 " + at + ")");
                string tag = screen.EntryTag(i) ?? throw new InvalidOperationException("판 카드 " + i + "에 꼬리표가 없다");
                if (tag == "screen.battle.entry.active") active++;
            }
            if (active != 1) throw new InvalidOperationException("「배틀 중」 꼬리표가 " + active + "개");
            var bad = WorkUi.Overflows(panel);
            if (bad.Count > 0) throw new InvalidOperationException("엔트리 판 글자 넘침 " + string.Join(" / ", bad));
            Debug.Log($"[UiShots] battle-entry {flow.Stage.Grade.TokenHeight} {(mine ? "mine" : "foe")}: 카드 {n} · 판 {at}");
        }

        // 무리 배틀 내 차례: 카드 셋 엇겹침(맨 앞 = 나와 있는 것) · 기둥 안 — hover면 셋을 펼친 판
        static void SwarmCards(UiShot s, bool hover)
        {
            var flow = SwarmBattle(s, 1);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            InfoCards(flow, true);
            var screen = (BattleScreen)flow.Stage.Current;
            var root = flow.Stage.Root;
            var roster = flow.Client.View.Battle.Sides[1 - flow.Battle.MySide].Roster;
            for (int i = 1; i <= roster.Count; i++)
                if (root.Q("battle-swarm-slot-" + i) == null) throw new InvalidOperationException("무리 카드 " + i + "이 없다");
            var box = root.Q("battle-foe");
            var front = box[box.childCount - 1];   // 뒤 카드부터 넣었다 — 마지막 = 맨 앞
            int active = screen.ShownMon(1 - flow.Battle.MySide).Id;
            if (front.name != "battle-swarm-slot-" + (roster.IndexOf(active) + 1)) throw new InvalidOperationException("맨 앞 카드 " + front.name + "가 나와 있는 포켓몬이 아니다");
            if (!hover) return;
            if (!screen.HoverSwarm(2)) throw new InvalidOperationException("무리 카드에 마우스 — 판이 뜨지 않았다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (!screen.EntryOpen || screen.EntryShown.Count != roster.Count) throw new InvalidOperationException("셋을 펼치지 않았다(" + screen.EntryShown.Count + ")");
            var bad = WorkUi.Overflows(root.Q(BattleScreen.EntryPanel));
            if (bad.Count > 0) throw new InvalidOperationException("엔트리 판 글자 넘침 " + string.Join(" / ", bad));
        }
    }
}
