using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M9 다듬기 8(결정 116 — P14 · P38 · P39)의 UI 캡처 장면.
    /// P14 조우 전환(22행)의 무늬를 상대에 따라: 야생 · 전설 · 트레이너(갤럭시단) · 울트라 비스트 — 닫히는 중 · 다 덮이기 직전 · 열리는 중(시각은 단계 길이의 천분율).
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] Polish8Screens =
        {
            Screen("p14-wild-close", s => WipeShot(s, WipeWild(s), 400, WipeStyle.Wild)),
            Screen("p14-wild-shut", s => WipeShot(s, WipeWild(s), 470, WipeStyle.Wild)),
            Screen("p14-wild-open", s => WipeShot(s, WipeWild(s), 700, WipeStyle.Wild)),
            Screen("p14-legend-light", s => WipeShot(s, WipeLegend(s), 270, WipeStyle.Legend)),
            Screen("p14-legend-close", s => WipeShot(s, WipeLegend(s), 400, WipeStyle.Legend)),
            Screen("p14-legend-shut", s => WipeShot(s, WipeLegend(s), 470, WipeStyle.Legend)),
            Screen("p14-legend-open", s => WipeShot(s, WipeLegend(s), 700, WipeStyle.Legend)),
            Screen("p14-trainer-close", s => WipeShot(s, WipeTrainer(s), 400, WipeStyle.Trainer)),
            Screen("p14-trainer-shut", s => WipeShot(s, WipeTrainer(s), 470, WipeStyle.Trainer)),
            Screen("p14-trainer-open", s => WipeShot(s, WipeTrainer(s), 700, WipeStyle.Trainer)),
            Screen("p14-rift-close", s => WipeShot(s, UbAt(s, Beast, team: false), 400, WipeStyle.Rift)),
            Screen("p14-rift-shut", s => WipeShot(s, UbAt(s, Beast, team: false), 470, WipeStyle.Rift)),
            Screen("p14-rift-open", s => WipeShot(s, UbAt(s, Beast, team: false), 700, WipeStyle.Rift)),
            // P39 공간 일그러짐: 깨어진 세계의 잔물결 · 칸이 부서지는 순간 · 신 강림의 번개 뒤 / DS — 기라티나의 그림자 · UB 배틀이 열린 직후
            Screen("p39-world", s => { UbLook(s, null, BoardCameraRig.Zoom.Region, (flow, st) => BeastCell(flow, st, TokenKind.Necrozma)); Rippled(s, RippleBeats.WholeWorld); }),
            Screen("p39-break", s => { UbTurnInto(s, st => st.Row == UbRows.Destroy, 500); Rippled(s, 0); }),
            Screen("p39-descend", s => { ShrineDescend(s, DescendBeats.BoltAt + 215); Rippled(s, 0); }),
            Screen("p39-shadow", s => { FateBattlePlayed(s, SoloTrail.FateGiratina, FateRows.Sacrifice, 700); Warped(s); }),
            Screen("p39-rift", s => { WipeShot(s, UbAt(s, Beast, team: false), 780, WipeStyle.Rift); Warped(s); }),
        };

        // 보드의 일그러짐이 켜져 있나(cell = WholeWorld면 깨어진 세계 전체, 0 이상이면 한 칸 둘레)
        static void Rippled(UiShot s, int cell)
        {
            float step = (float)Sinnoh.Data.BoardLayout.HexRadius * 2f;
            if (!BoardRipple.On || BoardRipple.Amp <= 0f) throw new InvalidOperationException("보드의 공간 일그러짐이 켜지지 않았다");
            bool burst = BoardRipple.Reach <= RippleBeats.BurstCells * step + 0.01f;
            if (burst != (cell != RippleBeats.WholeWorld)) throw new InvalidOperationException("일그러짐의 범위가 다르다(닿는 거리 " + BoardRipple.Reach + " · 진폭 " + BoardRipple.Amp + ")");
            if (BoardRipple.Amp > RippleBeats.Burst * step / 2f + 0.001f) throw new InvalidOperationException("진폭이 한도를 넘었다: " + BoardRipple.Amp);
        }

        // DS 화면이 일그러져 찍혔나
        static void Warped(UiShot s)
        {
            var b = s.Made != null ? s.Made.Stage.Current as BattleScreen : null;
            if (b == null || b.Ds == null) throw new InvalidOperationException("배틀 화면(DS)이 없다");
            if (b.Ds.WarpDrawn < 0.5f || b.Painted == null || b.Painted.WarpPx != b.Ds.WarpDrawn) throw new InvalidOperationException("DS 화면이 일그러져 찍히지 않았다(진폭 " + b.Ds.WarpDrawn + " px — 셰이더 " + DsStage.WarpShaderName + ")");
        }

        // 조우 전환 단계의 그 천분율 시각 — 줄무늬가 떠 있고 무늬 갈래가 그 상대의 것인지
        static void WipeShot(UiShot s, GameFlow flow, int permille, WipeStyle want)
        {
            var step = Queued(flow, st => st.Row == EnterRows.Wipe);
            if (step == null) throw new InvalidOperationException("조우 전환 단계가 대기열에 없다");
            TickedInto(s, flow, step, step.Ms * permille / 1000);
            if (flow.WipeStyle != want) throw new InvalidOperationException("전환 무늬가 " + want + "이 아니다: " + flow.WipeStyle);
            if (flow.WipeCover <= 0f && flow.WipeLight <= 0f) throw new InvalidOperationException("전환 줄무늬도 빛도 보이지 않는다(" + permille + "‰)");
        }

        // 야생: 풀밭에서 찾아보기 → 첫 카드를 고른다
        static GameFlow WipeWild(UiShot s)
        {
            var flow = Trail(s, SoloTrail.Catch, Encounter);
            if (!((EncounterScreen)flow.Stage.Current).Pick(0)) throw new InvalidOperationException("카드 고르기가 거절됐다");
            return flow;
        }

        // 전설: 비밀키를 가진 좌석이 그랜드 샤토에서 [찾아보기](23행 장면과 같은 판)
        static GameFlow WipeLegend(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 1, state =>
            {
                state.Seats[0].Cell = FixtureCell(ctx, "legendary_site.grand_chateau");   // 파티는 스타팅 포켓몬 하나 그대로(여럿이면 엔진이 첫 출전부터 물어 열리는 동안 DS가 비어 있다)
                state.Seats[0].Bag.Add(ctx.Items.Ids.IndexOf("item.secret_key"));
            });
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException("[찾아보기]가 거절됐다(전설 장소 · 비밀키)");
            return flow;
        }

        // 트레이너: 고향 마을의 1막 조무래기에게 [도전]
        static GameFlow WipeTrainer(UiShot s)
        {
            var flow = ToBoard(s, BoardReadyMs);
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다");
            return flow;
        }
    }
}
