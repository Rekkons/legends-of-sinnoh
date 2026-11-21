using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 턴 넘김 연출 견본(04 §8-7·§8-8 101행): 이벤트 대본(턴 종료 → 다음 턴) → <see cref="FxPlanner"/> → 대기열 → 보드 HUD와 같은 부품 <see cref="TurnBand"/>.
    /// 게임 화면이 아니다 — 보드 없이 띠만 본다. 배치 캡처 <c>Sinnoh.Editor.FxShots</c>가 <see cref="Seek"/>로 정한 시각에 보내 찍는다.
    /// 세 가지(결정 57): 두 좌석(「플레이어 1 ▶ 플레이어 2」, 보는 사람 = 턴이 오는 좌석 — 요약 800 · 띠 600 · 컷신 600), 혼자 하기(한 좌석 — 띠에 라운드를 적는다)의
    /// 주운 것이 있는 턴(요약 800 · 띠 500 · 컷신 500)과 주운 것이 없는 턴(요약 없이 띠 500 · 컷신 500).
    /// </summary>
    public sealed class FxTurnSample
    {
        /// <summary>찍는 시각(대본 시작부터 ms) — 두 좌석(0~800 요약 · ~1400 띠 · ~2000 컷신): 요약이 올라오는 중 · 요약 머묾 · 띠 들어오는 중 · 띠 머묾 · 띠 나가는 중 · 컷신 열리는 중 · 컷신 머묾 · 착지(걷히는 중).</summary>
        public static readonly int[] ShotsMs = { 60, 500, 860, 1100, 1340, 1440, 1700, 1960 };
        /// <summary>혼자 하기 · 주운 것이 있는 턴(0~800 요약 · ~1300 띠 · ~1800 컷신): 같은 여덟 순간.</summary>
        public static readonly int[] SoloGainShotsMs = { 60, 500, 850, 1050, 1250, 1340, 1550, 1770 };
        /// <summary>혼자 하기 · 주운 것이 없는 턴(0~500 띠 · ~1000 컷신): 띠 들어오는 중 · 머묾 · 나가는 중 · 컷신 열리는 중 · 머묾 · 착지.</summary>
        public static readonly int[] SoloShotsMs = { 50, 250, 450, 540, 750, 970 };

        public readonly TweenRunner Runner = new TweenRunner();
        public readonly FxSequencer Sequencer = new FxSequencer();
        public readonly TurnBand Band;
        readonly FxPlayer player;
        int nowMs;

        /// <summary>대본: 좌석 from의 턴이 끝나고(gain이면 그 턴에 규토리 3개를 주웠다) 좌석 to의 턴이 온다. 한 좌석뿐이면 새 라운드다.</summary>
        public static List<GameEvent> Script(int from, int to, int round, bool gain = true)
        {
            var events = new List<GameEvent>();
            if (gain)
            {
                events.Add(new ActionStarted { Seq = 0, Seat = from, Kind = ActionKind.TakeApricorns, Auto = AutoKind.Human });
                events.Add(new ApricornsTaken { Seq = 0, Seat = from, Count = 3 });
            }
            events.Add(new ActionStarted { Seq = 1, Seat = from, Kind = ActionKind.EndTurn, Auto = AutoKind.Human });
            events.Add(new TurnEnded { Seq = 1, Seat = from, TurnId = 1, Reason = TurnEndReason.EndTurn });
            if (from == to)
            {
                events.Add(new ActionStarted { Seq = 2, Seat = SeatId.System, Kind = ActionKind.RoundStart, Auto = AutoKind.Auto });
                events.Add(new RoundStarted { Seq = 2, Seat = SeatId.System, Round = round });
            }
            events.Add(new ActionStarted { Seq = 3, Seat = to, Kind = ActionKind.TurnStart, Auto = AutoKind.Auto });
            events.Add(new TurnStarted { Seq = 3, Seat = to, TurnId = 2, Round = round });
            return events;
        }

        /// <param name="twoSeats">false = 혼자 하기(좌석 1 → 좌석 1, 2라운드), true = 좌석 1 → 좌석 2(보는 사람 = 좌석 2).</param>
        /// <param name="gain">끝난 턴에 주운 것이 있나. 혼자 하기에서 false면 요약 없이 띠부터다(두 좌석은 「변화 없음」 요약이 선다).</param>
        public FxTurnSample(VisualElement root, KitSkin skin, UiGrade grade, StringTable strings, bool twoSeats, bool gain = true)
        {
            var stage = new UiStage(skin, strings);
            stage.Bind(root, grade);
            var page = new VisualElement { name = "fx-turn-sample" };
            page.AddToClassList("sn-screen");
            page.style.backgroundColor = new StyleColor(new Color32(0x58, 0x78, 0xA0, 0xFF));   // 견본 바탕(게임 화면 아님)
            root.Insert(0, page);
            Band = new TurnBand(stage);
            page.Add(Band.Element);

            int from = 0, to = twoSeats ? 1 : 0;
            var entries = new FxPlanner(to, twoSeats ? 2 : 1).Plan(Script(from, to, 2, gain));
            var info = new TurnBandInfo
            {
                From = from, To = to, Round = 2,
                FromColor = SeatColors.ForVillage("village.stark"), ToColor = SeatColors.ForVillage(twoSeats ? "village.canalave" : "village.stark"),
                FromFace = "pt_player_male", ToFace = twoSeats ? "bw_001_hilda" : "pt_player_male",
                FromName = stage.T("screen.board.seat").Replace("{n}", "1"), ToName = stage.T("screen.board.seat").Replace("{n}", twoSeats ? "2" : "1"),
            };
            if (gain) info.Lines.Add(stage.T("screen.board.summary.apricorns").Replace("{n}", "3"));
            foreach (var e in entries)
                foreach (var s in e.Steps)
                    if (s.Row == FxRows.TurnSummary || s.Row == FxRows.Handoff || s.Row == FxRows.MyTurn) s.Tag = info;
            // 이 견본은 턴 넘김(101행)만 본다 — 대본의 규토리 줍기(19행)와 여럿 판의 턴 시작(6행)은 덜어 낸다(그 행들은 UiShots row-19-*·row-06-*이 실제 판에서 찍는다)
            FxBuilt.Keep(entries, row => row == FxRows.TurnSummary || row == FxRows.Handoff || row == FxRows.MyTurn);
            player = new FxPlayer(Runner, Sequencer, Band.Show);
            player.Enqueue(entries);
            Band.Show(Sequencer);
        }

        /// <summary>대본 시작부터 <paramref name="ms"/>가 지난 모습으로 보낸다(앞으로만).</summary>
        public void Seek(int ms)
        {
            if (ms < nowMs) throw new ArgumentOutOfRangeException(nameof(ms), ms, "앞으로만 간다 — 되돌리려면 새로 만든다");
            Runner.Advance(ms - nowMs);
            nowMs = ms;
        }
    }
}
