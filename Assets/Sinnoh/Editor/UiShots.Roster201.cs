using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 사용자 결정 201 ①(2026-11-22): 갤럭시단 배틀의 앞면으로 공개된 명단 — 결정 206 ②부터 상대 기둥 머리 줄의 [상대 엔트리 n] → 엔트리 판(<c>battle-entry-n</c> — 카드 + 기술 띠,
    /// 꼬리표 「배틀 중」 · 「대기」 · 「기절」, 쓰러진 카드 흐림). 판은 막 길 <c>SoloTrail.Lake</c>(새턴 4) · <c>Heart</c>(태홍 6) · 고향 마을 조무래기(2) · 관전(좌석 2의 조무래기전).
    /// 그림 <c>Logs/Review/m10/galactic-lineup/</c>. PlayMode <c>GalacticRosterFlowTests</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] RosterScreens =
        {
            Battle("roster-sendin", s => RosterAt(s, SoloTrail.Lake, b => b.Open && b.Bench.Count > 0 && b.Moves.Count == 0, HosFlow.CommanderLineup(2))),   // 첫 출전을 고르는 동안 — 명단은 이미 공개
            Battle("roster-saturn", s => RosterAt(s, SoloTrail.Lake, MyTurn, HosFlow.CommanderLineup(2))),
            Battle("roster-saturn-tip", s => RosterTip(s, RosterAt(s, SoloTrail.Lake, MyTurn, HosFlow.CommanderLineup(2)), 4)),   // 아직 안 나온 넷째
            Battle("roster-saturn-done", s => RosterTip(s, RosterAt(s, SoloTrail.Lake, b => b.RosterDone >= 1 && MyTurn(b), HosFlow.CommanderLineup(2)), -1)),   // 쓰러진 칸(흐림)
            Battle("roster-cyrus", s => RosterAt(s, SoloTrail.Heart, MyTurn, HosFlow.BossLineup)),
            Battle("roster-cyrus-tip", s => RosterTip(s, RosterAt(s, SoloTrail.Heart, MyTurn, HosFlow.BossLineup), HosFlow.BossLineup)),
            Battle("roster-grunt", s => RosterGrunt(s)),
            Battle("roster-spectate", s => RosterSpectate(s)),
        };

        static GameFlow RosterAt(UiShot s, SoloTrail trail, Func<BattleScreen, bool> until, int count)
        {
            var flow = Trail(s, trail, f => f.Stage.Current is BattleScreen b && b.RosterCount > 0 && until(b));
            RosterCheck(flow, count);
            return flow;
        }

        static void RosterGrunt(UiShot s)
        {
            var flow = Grunts(s, "484:2", false, 0, "483:2");
            Fight(flow, MyTurn);
            RosterCheck(flow, BattleFlow.GruntLineup);
        }

        static void RosterSpectate(UiShot s)
        {
            Spectate(s, false);
            RosterCheck(s.Made, BattleFlow.GruntLineup);
        }

        // 결정 206 ②: 명단 줄 대신 [상대 엔트리 n] — 판을 열어 카드 수 = 명단 수 · 흐린(기절) 카드 수 = 쓰러진 수 · 나와 있는 카드는 흐리지 않다 · 단추 글의 n
        static void RosterCheck(GameFlow flow, int count)
        {
            var screen = (BattleScreen)flow.Stage.Current;
            var roster = flow.Client.View.Battle.Sides[1 - flow.Battle.MySide].Roster;
            if (screen.RosterCount != count || roster.Count != count) throw new InvalidOperationException("명단 " + screen.RosterCount + " · 보기 명단 " + roster.Count + " ≠ " + count);
            var button = flow.Stage.Root.Q<Button>(BattleScreen.EntryFoeButton) ?? throw new InvalidOperationException("[상대 엔트리] 단추가 없다");
            if (!button.text.EndsWith(" " + count, StringComparison.Ordinal)) throw new InvalidOperationException("[상대 엔트리] 단추 글 「" + button.text + "」의 수가 " + count + "가 아니다");
            if (!screen.EntryOpen && !screen.OpenEntry(false, false)) throw new InvalidOperationException("상대 엔트리 판이 열리지 않았다");
                        if (screen.EntryShown.Count != count) throw new InvalidOperationException("엔트리 카드 " + screen.EntryShown.Count + " ≠ " + count);
            int dim = 0;
            for (int n = 1; n <= count; n++)
            {
                var slot = flow.Stage.Root.Q(BattleScreen.EntryPanel + "-" + n);
                if (slot == null) throw new InvalidOperationException("엔트리 카드 " + n + "이 없다");
                if (slot.ClassListContains(BattleScreen.RosterDoneClass)) dim++;
            }
            if (dim != screen.RosterDone) throw new InvalidOperationException("흐린 카드 " + dim + " ≠ 쓰러진 수 " + screen.RosterDone);
            if (screen.RosterActive >= 0 && flow.Stage.Root.Q(BattleScreen.EntryPanel + "-" + (screen.RosterActive + 1)).ClassListContains(BattleScreen.RosterDoneClass))
                throw new InvalidOperationException("나와 있는 카드가 흐리다");
        }

        // n번째 카드(−1 = 처음 쓰러진 카드) — 카드 = 그 포켓몬 · 꼬리표 · 판이 화면 안 · 글 넘침 없음
        static void RosterTip(UiShot s, GameFlow flow, int n)
        {
            var screen = (BattleScreen)flow.Stage.Current;
            if (n < 0)
                for (int k = 1; k <= screen.RosterCount && n < 0; k++)
                    if (flow.Stage.Root.Q(BattleScreen.EntryPanel + "-" + k).ClassListContains(BattleScreen.RosterDoneClass)) n = k;
            if (!screen.HoverRoster(n)) throw new InvalidOperationException("엔트리 카드 " + n + "이 없다");
            var view = flow.Client.View;
            int want = view.Pokemon[view.Battle.Sides[1 - flow.Battle.MySide].Roster[n - 1]].Def;
            int got = view.Pokemon[screen.EntryShown[n - 1]].Def;
            if (got != want) throw new InvalidOperationException("카드 " + got + " ≠ 명단 " + n + "의 포켓몬 " + want);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var panel = flow.Stage.Root.Q(BattleScreen.EntryPanel);
            var card = panel.worldBound;
            var root = flow.Stage.Root.worldBound;
            if (card.xMin < root.xMin || card.xMax > root.xMax + 0.5f || card.yMin < root.yMin || card.yMax > root.yMax + 0.5f) throw new InvalidOperationException("엔트리 판이 화면 밖: " + card);
            var bad = WorkUi.Overflows(panel);
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
        }
    }
}
