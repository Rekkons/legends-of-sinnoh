using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S64 결과(04 §2, §8-8 97행 — M5 「막·종막 화면」): 게임이 끝나면 HUD 위에 열린다(<see cref="GameFlow.FinaleStage"/>).
    /// 판이 어두워지는 동안 승리 좌석의 말로 카메라가 다가가고 → 제목(영웅 · 배신자의 승리 · 배신자를 막았다) → 승리 좌석의 파티 포켓몬이 하나씩(전당 등록 리듬) → 통계(라운드 · 여행 점수 · 포켓몬 · 스탬프).
    /// 박자는 <see cref="ResultBeats"/>, 길이는 방장과 같은 8000(전원 차단). 연출이 끝나면 [타이틀로]가 켜진다(여럿 판의 「다 같이 로비로」는 M7).
    /// 끝난 까닭은 보기에서 읽는다(배신자가 없으면 영웅 · 승자가 배신자면 배신자의 승리 · 아니면 배신자를 막았다) — 이어 받은 끝난 판도 같은 창이다.
    /// 요소 이름: <c>result</c>·<c>result-scrim</c>·<c>result-panel</c>·<c>result-title</c>·<c>result-winner</c>·<c>result-party</c>·<c>result-mon-&lt;i&gt;</c>·<c>result-stats</c>·<c>result-title-button</c>.
    /// </summary>
    public sealed class ResultScreen : IUiScreen
    {
        readonly GameFlow flow;
        UiStage stage;
        VisualElement scrim, panel, title, winnerRow, stats;
        Button toTitle;
        readonly List<VisualElement> mons = new List<VisualElement>();

        public ResultScreen(GameFlow flow) => this.flow = flow ?? throw new ArgumentNullException(nameof(flow));

        public string Name => "result";

        /// <summary>끝난 까닭(보기에서).</summary>
        public GameEndReason Reason { get; private set; }

        /// <summary>승리 좌석(없으면 −1).</summary>
        public int Winner { get; private set; } = -1;

        /// <summary>지금 보이는 파티 포켓몬 수 · 통계가 다 떴나 · [타이틀로]가 켜졌나 — 확인용.</summary>
        public int MonsShown { get; private set; }
        public bool StatsShown { get; private set; }
        public bool Ready { get; private set; }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 위 띠의 [메뉴] 등 HUD 단추는 어둠 너머로도 눌린다(T35)
            scrim = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "result-scrim", "sn-act-scrim");
            panel = Add(page, Kit.Panel(stage.Skin), "result-panel", "sn-result-panel");
            Paint();
            if (flow.Fx != null) ShowFx(flow.Fx.Sequencer);
            else Pose(ResultBeats.Done(mons.Count));
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = null;
            mons.Clear();
        }

        public void Refresh()
        {
            if (stage == null) return;
            if (flow.Fx != null) ShowFx(flow.Fx.Sequencer);
        }

        void Paint()
        {
            panel.Clear();
            mons.Clear();
            var view = flow.Client != null ? flow.Client.View : null;
            if (view == null) return;
            if (view.Hos == null)
            {
                if (view.Ub != null) PaintUb(view);   // 울트라 버스트(M6 「UB 배틀 화면 묶음」)
                return;
            }
            var h = view.Hos;
            Winner = h.Winner;
            Reason = h.Traitor < 0 ? GameEndReason.Hero : h.Winner == h.Traitor ? GameEndReason.TraitorWon : GameEndReason.TraitorDefeated;
            var inv = CultureInfo.InvariantCulture;
            title = Add(panel, Kit.Text(stage.T("screen.result.title." + (Reason == GameEndReason.Hero ? "hero" : Reason == GameEndReason.TraitorWon ? "traitor_won" : "traitor_defeated")), Kit.FTitle), "result-title", "kit-title");
            title.AddToClassList("sn-result-title");
            winnerRow = Add(panel, new VisualElement(), "result-winner", "sn-result-winner");
            if (Winner >= 0 && Winner < view.Seats.Length)
            {
                var face = FaceBox.Make(true);
                winnerRow.Add(face);
                FaceBox.Paint(face, Winner, flow.SeatColor(Winner), flow.Protagonist(Winner));
                Add(winnerRow, Kit.Text(stage.T("screen.result.winner").Replace("{name}", flow.SeatName(Winner)), Kit.F2), null, "sn-result-name");
            }
            // 승리 좌석의 파티(전당 등록 — BW 정지 도트, 파티 순서)
            var party = Add(panel, new VisualElement(), "result-party", "sn-result-party");
            var art = MonArt.Load();
            var seat = Winner >= 0 && Winner < view.Seats.Length ? view.Seats[Winner] : null;
            if (seat != null && art != null)
                foreach (int id in seat.Party)
                {
                    if (id < 0 || id >= view.Pokemon.Count || view.Pokemon[id].Def < 0) continue;
                    var tile = Add(party, new VisualElement { pickingMode = PickingMode.Ignore }, "result-mon-" + mons.Count.ToString(inv), "sn-result-mon");
                    tile.Add(art.Front(flow.Ctx.Cards.Pokemon[view.Pokemon[id].Def].Key));
                    mons.Add(tile);
                }
            // 통계: 라운드 · 여행 점수 · 포켓몬(파티 수) · 스탬프(모은 수)
            stats = Add(panel, new VisualElement(), "result-stats", "sn-result-stats");
            int party6 = 0, stamps = 0;
            if (seat != null)
            {
                foreach (int id in seat.Party) if (id >= 0) party6++;
                foreach (bool b in seat.Stamps) if (b) stamps++;
            }
            Stat("round", view.Round);
            Stat("jp", seat != null ? seat.Jp : 0);
            Stat("mons", party6);
            Stat("stamps", stamps);
            stage.Tips.Hook(stats, null, stage.T("screen.result.stats.tip"), TipSide.Above, reading: true);
            var foot = Add(panel, new VisualElement(), null, "sn-result-foot");
            toTitle = Add(foot, Kit.Button(stage.T("screen.result.title_button"), KitButtonKind.Main, () => PressTitle()), "result-title-button", null);
        }

        void Stat(string key, int value) => Stat(key, value.ToString(CultureInfo.InvariantCulture));

        void Stat(string key, string value)
        {
            var row = Add(stats, new VisualElement(), "result-stat-" + key, "sn-result-stat");
            Add(row, Kit.Text(stage.T("screen.result." + key), Kit.F2), null, "kit-muted");
            Add(row, Kit.Text(value, Kit.F2), null, "sn-result-value");
        }

        /// <summary>
        /// 울트라 버스트 판의 결과(04 §2 「네크로즈마 3페이즈 격파 = 승리 / 균열 강도 70% = 패배 → S64」 · A54 ⑧ 모두의 승리 · 모두 탈락): 제목 · 모든 좌석의 얼굴 + 「모두의 승리/패배」 ·
        /// 내 좌석의 파티 · 통계(라운드 · 균열 강도 · 쓰러뜨린 UB n/9 · 네크로즈마 페이즈 n/3 · 남은 축복) [가정 plan §5 #159].
        /// </summary>
        void PaintUb(GameState view)
        {
            var ub = view.Ub;
            var inv = CultureInfo.InvariantCulture;
            bool won = ub.Result == UbResult.Won;
            bool all = true;
            foreach (var st in view.Seats) all &= st.Eliminated;
            Reason = won ? GameEndReason.NecrozmaDefeated : all ? GameEndReason.AllEliminated : GameEndReason.BreachLost;
            Winner = -1;
            title = Add(panel, Kit.Text(stage.T("screen.result.title." + (won ? "ub_won" : all ? "ub_eliminated" : "ub_breach")), Kit.FTitle), "result-title", "kit-title");
            title.AddToClassList("sn-result-title");
            winnerRow = Add(panel, new VisualElement(), "result-winner", "sn-result-winner");
            for (int i = 0; i < view.Seats.Length; i++)
            {
                var face = FaceBox.Make(true);
                face.name = "result-face-" + i.ToString(inv);
                winnerRow.Add(face);
                FaceBox.Paint(face, i, flow.SeatColor(i), flow.Protagonist(i));
            }
            Add(winnerRow, Kit.Text(stage.T(won ? "screen.result.ub.won" : "screen.result.ub.lost"), Kit.F2), null, "sn-result-name");
            var party = Add(panel, new VisualElement(), "result-party", "sn-result-party");
            var art = MonArt.Load();
            int me = flow.Client.Seat;
            var seat = me >= 0 && me < view.Seats.Length ? view.Seats[me] : null;
            if (seat != null && art != null)
                foreach (int id in seat.Party)
                {
                    if (id < 0 || id >= view.Pokemon.Count || view.Pokemon[id].Def < 0) continue;
                    var tile = Add(party, new VisualElement { pickingMode = PickingMode.Ignore }, "result-mon-" + mons.Count.ToString(inv), "sn-result-mon");
                    tile.Add(art.Front(flow.Ctx.Cards.Pokemon[view.Pokemon[id].Def].Key));
                    mons.Add(tile);
                }
            stats = Add(panel, new VisualElement(), "result-stats", "sn-result-stats");
            Stat("round", view.Round);
            Stat("breach", UbGauge.Breach(view.Tokens).ToString(inv) + "%");   // 글리프 「%」·「/」만 코드
            Stat("beasts", ub.Defeated.ToString(inv) + "/" + UbState.Beasts.ToString(inv));
            Stat("phase", (won ? UbNecrozma.Phases : Math.Max(0, ub.NecrozmaPhase - 1)).ToString(inv) + "/" + UbNecrozma.Phases.ToString(inv));
            Stat("blessings", ub.Blessings);
            stage.Tips.Hook(stats, null, stage.T("screen.result.stats.ub.tip"), TipSide.Above, reading: true);
            var foot = Add(panel, new VisualElement(), null, "sn-result-foot");
            toTitle = Add(foot, Kit.Button(stage.T("screen.result.title_button"), KitButtonKind.Main, () => PressTitle()), "result-title-button", null);
        }

        /// <summary>[타이틀로]: 연출이 끝났으면 판을 떠나 타이틀로. 아직이면 false.</summary>
        public bool PressTitle()
        {
            if (!Ready) return false;
            stage?.Tips.Hide();
            flow.LeaveFinished();
            return true;
        }

        /// <summary>대기열의 지금 시각으로 그린다(97행 단계가 없으면 다 보인 모습).</summary>
        public void ShowFx(FxSequencer seq)
        {
            if (stage == null || panel == null) return;
            var step = seq != null ? ResultBeats.StepOf(seq.Current) : null;
            bool playing = step != null && seq.LocalMs >= step.StartMs && seq.LocalMs < step.EndMs;
            Pose(playing ? ResultBeats.At(seq.LocalMs - step.StartMs, step.Ms, mons.Count) : ResultBeats.Done(mons.Count));
            bool ready = !playing && (seq == null || seq.IsEmpty);
            if (ready != Ready)
            {
                Ready = ready;
                toTitle?.SetEnabled(ready);
            }
            else toTitle?.SetEnabled(ready);
        }

        void Pose(ResultPose p)
        {
            FxUi.Fade(scrim, p.Dim);
            FxUi.Fade(panel, p.Title);
            MonsShown = Math.Min(p.Mons, mons.Count);
            for (int i = 0; i < mons.Count; i++)
            {
                bool on = i < MonsShown;
                FxUi.Fade(mons[i], on ? 1f : 0f);
                float s = on && i == MonsShown - 1 ? p.Pop : 1f;
                FxUi.Scale(mons[i], s, s);
            }
            if (stats != null) FxUi.Fade(stats, p.Stats);
            StatsShown = p.Stats >= 1f;
        }

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
