using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 사용자 결정 184(2026-11-21): 비스트 부스트가 쌓인 포켓몬의 DS 도트 둘레 빛 알갱이 — 새로 쌓이는 순간의 터짐(641 단계 앞)과 쌓여 있는 동안 오르는 알갱이.
    /// UB 배틀(울트라 비스트가 내 체력 1인 포켓몬을 쓰러뜨려 쌓는다 — 상대 쪽) · 조무래기 배틀(전설 특수기술을 가진 디아루가가 쓰러뜨려 쌓는다 — 내 쪽, 다음 공격에 쓰면 지워진다).
    /// 장면마다 알갱이 수(<see cref="BattleScreen.EnemyMotes"/> · <see cref="BattleScreen.PlayerMotes"/>)를 단정한다. 그림 <c>Logs/Review/m10/beast-boost/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] BeastBoostScreens =
        {
            Screen("beast-boost-ub-burst", s => UbBoost(s, 150)),                       // UB가 쌓는 순간: 몸 가운데서 둘레로 터짐
            Screen("beast-boost-ub-stay", s => UbBoost(s, -1)),                         // 쌓여 있는 동안(내 다음 포켓몬을 고르는 때): 발 둘레에서 오르는 알갱이
            Screen("beast-boost-mine-stay", s => MineBoost(s, false)),                  // 내 디아루가에 쌓여 있는 내 차례
            Screen("beast-boost-mine-used", s => MineBoost(s, true)),                   // 다음 공격에 써서 지워진 뒤
        };

        static bool BoostOf(FxStep st, Func<int, bool> who)
        {
            if (st.Row != BoostMotes.Row) return false;
            foreach (var e in st.Events)
                if (e is FatigueChanged f && f.Cause == FatigueCause.BeastBoost && who(f.PokemonId)) return true;
            return false;
        }

        // UB 배틀: 내 맨 앞 포켓몬은 체력 1 — UB가 쓰러뜨리면 UB 쪽에 비스트 부스트가 쌓인다. ms ≥ 0 = 641 단계 안 그 시각, −1 = 단계가 끝난 뒤(다음 포켓몬을 고르는 때)
        static void UbBoost(UiShot s, int ms)
        {
            var ctx = s.Board.Ctx;
            var flow = UbAt(s, Beast, more: st =>
            {
                var first = st.Pokemon[st.Seats[st.ActiveSeat].Party[0]];
                first.Damage = Math.Max(0, PokemonRules.MaxHp(ctx, first) - 1);
            });
            Func<int, bool> isFoe = id => id >= 0 && id < flow.Client.View.Pokemon.Count && flow.Client.View.Pokemon[id].Def >= 0 && ctx.Cards.Pokemon[flow.Client.View.Pokemon[id].Def].Tier == EncounterTier.UltraBeast;
            if (ms >= 0)
            {
                FightInto(s, flow, st => BoostOf(st, isFoe), ms);
                var screen = (BattleScreen)flow.Stage.Current;
                if (screen.EnemyMotes != BoostMotes.BurstCount) throw new InvalidOperationException("쌓이는 순간의 터짐 " + screen.EnemyMotes + " ≠ " + BoostMotes.BurstCount);
                return;
            }
            var step = default(FxStep);
            Fight(flow, b => (step = Queued(flow, st => BoostOf(st, isFoe))) != null);
            Laid(s, flow, step, step.Ms + 200);
            var shown = (BattleScreen)flow.Stage.Current;
            flow.Stage.Tweens.Advance(BoostMotes.StepMs * 3);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var view = flow.Client.View;
            if (view.Battle == null || view.Battle.Sides[1 - flow.Battle.MySide].BeastBoost <= 0) throw new InvalidOperationException("UB 쪽에 비스트 부스트가 쌓이지 않았다");
            if (shown.EnemyMotes < BoostMotes.StayBase) throw new InvalidOperationException("쌓여 있는데 오르는 알갱이가 " + shown.EnemyMotes);
            if (shown.PlayerMotes != 0) throw new InvalidOperationException("내 쪽에 알갱이가 있다");
        }

        // 조무래기 배틀: 전설 특수기술을 가진 디아루가(화염방사 하나)가 조무래기 포켓몬을 쓰러뜨려 내 쪽에 쌓는다 → 내 차례(오르는 알갱이) · used면 다음 공격에 쓴 뒤(없음)
        static void MineBoost(UiShot s, bool used)
        {
            var ctx = s.Board.Ctx;
            int fire = ctx.Cards.MoveIds.IndexOf("flamethrower"), mine = -1;
            var flow = Seats(s, 1, state =>
            {
                GiveTeam(ctx, state, "483:2", "484:2");
                mine = state.Seats[0].Party[0];
                var first = state.Pokemon[mine];
                first.HasLegendaryMove = true;
                first.Moves[0] = fire;
            });
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
            FightInto(s, flow, st => BoostOf(st, id => id == mine), 150, fire);
            var screen = Fight(flow, MyTurn, fire);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var side = flow.Client.View.Battle.Sides[flow.Battle.MySide];
            if (side.Active != mine || side.BeastBoost <= 0) throw new InvalidOperationException("내 디아루가에 비스트 부스트가 쌓여 있지 않다");
            if (screen.PlayerMotes < BoostMotes.StayBase) throw new InvalidOperationException("쌓여 있는데 오르는 알갱이가 " + screen.PlayerMotes);
            if (!used) return;
            int row = 0;
            for (int i = 0; i < screen.Moves.Count; i++) if (screen.Moves[i].Target == fire) row = i;
            if (!screen.PressMove(row)) throw new InvalidOperationException("화염방사가 거절됐다");
            var move = Queued(flow, st => st.Row == 590);
            if (move == null) throw new InvalidOperationException("기술 단계가 대기열에 없다");
            Laid(s, flow, move, 200);
            var now = flow.Client.View.Battle;
            if (now != null && now.Sides[flow.Battle.MySide].BeastBoost == 0 && screen.PlayerMotes != 0) throw new InvalidOperationException("쓴 뒤에도 알갱이가 " + screen.PlayerMotes);
        }
    }
}
