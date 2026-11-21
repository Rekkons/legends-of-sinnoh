using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 결정 143 ⑤ · ⑦(2026-11-20 플레이 다듬기)의 배틀 화면 장면.
    /// ⑤ 선출 중 = 첫 출전(파티 여럿)을 고르는 동안에도 DS 상대 자리에 상대(야생 · UB = 그 포켓몬, 갤럭시단 = 그 트레이너, 좌석 = 그 플레이어 주인공 앞모습), 내 자리에 내 주인공 뒷모습.
    /// ⑦ 준비 동작 = 기술을 누르면 굴림과 함께 공격한 도트가 당겨 떨고 DS에 「○○의 △△!」(맞음 · 피해는 주사위가 최종이 된 뒤).
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] Tempo143Screens =
        {
            Battle("b143-lineup-wild", s => { AlphaSendIn(s); LineupCheck(s, true, null); }),   // 우두머리 조우 · 첫 출전을 고르는 중(T14 기둥과 같은 마리가 DS에도)
            Battle("b143-lineup-ub", s => LineupCheck(s, LineupInto(s, UbAt(s, Beast, team: true)), true, null)),   // 울트라 비스트(4인 UB 판 · 파티 = 전설 다섯)
            Battle("b143-lineup-galactic", s => LineupGalactic(s)),   // 고향 마을의 갤럭시단 조무래기(파티 둘)
            Battle("b143-lineup-player", s => LineupPlayer(s)),       // 같은 칸의 좌석 2에게 도전(파티 둘씩) — 상대 자리 = 좌석 2 주인공의 앞모습
            Battle("b143-prep-roll", s => PrepRoll(s)),               // 기술을 누르고 1200 — 굴리는 중에 당긴 자세 · 「○○의 △△!」
        };

        // 파티 둘: 스타팅 옆에 한 마리 더(첫 출전을 묻게)
        static void TwoMons(Ctx ctx, GameState st, int seat)
        {
            var who = st.Seats[seat];
            who.OpenSlots = Math.Max(who.OpenSlots, 2);
            PokemonRules.AddToParty(who, PokemonRules.Spawn(st, ctx.Cards.PokemonIds.IndexOf("421:1"), 1, (int)DeckId.Legendary));
        }

        // 첫 출전을 묻는 배틀 화면이 열려 입력이 열릴 때까지
        static GameFlow LineupInto(UiShot s, GameFlow flow)
        {
            BattleScreen screen = null;
            for (int i = 0; i < 400; i++)
            {
                Pass(flow, 100);
                screen = flow.Stage.Current as BattleScreen;
                if (screen != null && screen.Open && flow.Client.Choice(ChoiceKind.SendIn) != null) break;
            }
            if (screen == null || flow.Client.Choice(ChoiceKind.SendIn) == null) throw new InvalidOperationException("첫 출전을 묻는 배틀 화면이 아니다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            return flow;
        }

        static void LineupCheck(UiShot s, bool pokemon, string front) => LineupCheck(s, s.Made, pokemon, front);

        // 선출 중 DS: 내 자리 = 주인공 뒷모습, 상대 자리 = 포켓몬(체력 상자 없이) 또는 트레이너 앞모습
        static void LineupCheck(UiShot s, GameFlow flow, bool pokemon, string front)
        {
            var screen = flow.Stage.Current as BattleScreen;
            if (screen == null || flow.Client.Choice(ChoiceKind.SendIn) == null) throw new InvalidOperationException("첫 출전을 고르는 중이 아니다");
            var ds = screen.Painted;
            if (!screen.LineupBackShown || ds == null || ds.Back == null) throw new InvalidOperationException("내 자리에 주인공 뒷모습이 없다");
            if (ds.Player != null) throw new InvalidOperationException("고르기 전인데 내 포켓몬이 서 있다");
            if (pokemon && (!screen.LineupFoeShown || ds.Enemy == null || ds.Enemy.BoxShown)) throw new InvalidOperationException("상대 자리에 야생 쪽 포켓몬이 (체력 상자 없이) 서 있지 않다");
            if (front != null && (screen.LineupFrontShown != front || ds.Foe == null || ds.Foe.Key != front)) throw new InvalidOperationException("상대 자리의 트레이너 앞모습이 " + front + "가 아니다: " + (screen.LineupFrontShown ?? "없음"));
        }

        static void LineupGalactic(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = MyTurn(s, 1, st => TwoMons(ctx, st, 0));
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다(고향 마을의 조무래기)");
            LineupInto(s, flow);
            LineupCheck(s, flow, false, ((BattleScreen)flow.Stage.Current).FoeKey);
        }

        static void LineupPlayer(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = MyTurn(s, 2, st =>
            {
                st.Seats[1].Cell = st.Seats[0].Cell;
                TwoMons(ctx, st, 0);
                TwoMons(ctx, st, 1);
            });
            if (!flow.Hud.Press(BoardHud.Challenge) || !(flow.Stage.Current is ChallengeScreen pick)) throw new InvalidOperationException("[도전]이 상대 고르는 창을 열지 않았다");
            if (!pick.PressSeat(1)) throw new InvalidOperationException("좌석 2에게 도전이 거절됐다");
            bool accepted = false;
            for (int i = 0; i < 80 && !accepted; i++)
            {
                accepted = flow.AnswerSeat(1, a => a.Kind == AnswerKind.Accept);
                if (!accepted) Pass(flow, 200);
            }
            if (!accepted) throw new InvalidOperationException("좌석 2가 도전을 받지 못했다");
            LineupInto(s, flow);
            if (flow.Battle.Kind == BattleKind.Galactic || FxWild(flow.Battle.Kind)) throw new InvalidOperationException("좌석끼리의 배틀이 아니다: " + flow.Battle.Kind);
            LineupCheck(s, flow, false, FaceBox.ArtKey(flow.Protagonist(1)));
        }

        static bool FxWild(BattleKind kind) => Sinnoh.Fx.FxRows.WildLike(kind);

        static void PrepRoll(UiShot s)
        {
            var flow = ToBattle(s);
            var screen = (BattleScreen)flow.Stage.Current;
            if (!screen.PressMove(0)) throw new InvalidOperationException("기술 답이 거절됐다");
            Pass(flow, 1200);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (!screen.Rolling) throw new InvalidOperationException("굴리는 중이 아니다");
            if (screen.Painted == null || screen.Painted.Player == null || screen.Painted.Player.Dx >= 0) throw new InvalidOperationException("굴리는 동안 공격한 내 도트가 당겨 있지 않다");
            if (screen.Message.IndexOf('!') < 0) throw new InvalidOperationException("굴리는 동안 DS 메시지가 「○○의 △△!」가 아니다: " + screen.Message);
        }
    }
}
