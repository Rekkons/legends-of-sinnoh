using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M6 「UB 화면 묶음 1」(결정 84 M1 가 · M2 가)의 UI 캡처 장면: 울트라 버스트 4인 판(시드 20261117 — UB 목업과 같은 판)의 위 띠 균열 칩(S56) · UB 턴의 위 가운데 띠와 「자동」 꼬리표(S57) ·
    /// 102행 UB 페이즈 띠 · 84행 빛 모음과 빛줄기 · 85행 10% 구간(균열을 9%로 올려 둔 판) · 86행 70% 패배(69%로 올려 둔 판). 보드 쪽 행이라 FxShots에는 장면이 없다(UiShots.Ub.cs와 같다).
    /// 장면마다 그 요소가 떠 있는지와 균열 칩 · 작은 칩 · 띠의 글 넘침(<see cref="BoardUb.Overflows"/>)을 단정한다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] UbHudScreens =
        {
            // S56 균열 칩: 첫 내 턴(0%) · 마우스 오버 · 네 라운드 뒤(균열이 오른 판 — 마우스 오버)
            Screen("ub-gauge", s => Gauge(s, UbGame(s), false)),
            Screen("ub-gauge-tip", s => Gauge(s, UbGame(s), true)),
            Screen("ub-gauge-late", s => Gauge(s, UbGame(s, st => UbRounds(s.Board.Ctx, st, 4)), true)),
            // 6인 판: 위 띠 폭이 모자라 칩이 상태 판 아래 오른쪽 판으로(BoardUb.RowSeats)
            Screen("ub-gauge-6", s => Gauge(s, UbGame(s, null, 6), true)),
            // 위 띠 프리즘 칩의 마우스 오버 = 개수 · 효과 · 옮겨 감(결정 193)
            Screen("ub-prism-tip", s => PrismTip(s, UbGame(s))),
            // 82행: UB 턴 시작 — 가운데 페이즈 띠(102행) · 균열 칩 눌림 → 첫 이동 가운데(위 가운데 띠 · 「자동」 꼬리표) → 첫 착지 뒤
            Screen("row-82-phase", s => Hud(s, UbTurnInto(s, st => st.Row == UbRows.Start, 380), h => h.Band.UbPhaseShown, "UB 페이즈 띠")),
            Screen("row-82-band", s => Hud(s, UbTurnInto(s, st => st.Row == UbRows.Move, 200), h => h.Ub.BandShown >= 1f && h.Ub.TagFor >= 0, "위 가운데 띠 · 꼬리표")),
            // 84행: 네크로즈마가 빛을 모음 → 첫 빛줄기
            Screen("row-84-charge", s => Hud(s, UbTurnInto(s, st => st.Row == UbRows.Strike, 1000), h => h.Ub.GlowShown > 0.5f, "빛 고리")),
            Screen("row-84-beam", s => Hud(s, UbTurnInto(s, StrikeBeats.ByNecrozma, 130), h => h.Ub.BeamShown > 0f && Behind(s.Panel.Root, BoardUb.Beam, "board-hud-top"), "빛줄기(위 띠 아래 층)")),
            // 85행(균열 9%로 올려 둔 판): 붉은 맥동 → 좌석 칩으로 나는 균열 카드
            Screen("row-85-pulse", s => Hud(s, UbTurnInto(s, st => st.Row == UbRows.Bracket, BracketBeats.PulsePeakMs, st => BreachTo(st, 9)), h => h.Ub.EdgeShown > 0.9f, "붉은 맥동")),
            Screen("row-85-cards", s => Hud(s, UbTurnInto(s, st => st.Row == UbRows.Bracket, BracketBeats.CardFromMs + BracketBeats.CardEveryMs * 2, st => BreachTo(st, 9)), h => h.Ub.CardsFlying >= 2, "나는 카드")),
            // 86행(균열 69%로 올려 둔 판): 보라로 물들고 어두워지는 중 → 「패배」
            Screen("row-86-dark", s => Hud(s, UbTurnInto(s, st => st.Row == UbRows.Lost, 2200, st => BreachTo(st, 69)), h => h.Ub.EndShown > 0f, "패배 덮개")),
            Screen("row-86-end", s => Hud(s, UbTurnInto(s, st => st.Row == UbRows.Lost, 4300, st => BreachTo(st, 69)), h => h.Ub.EndShown >= 1f, "패배 글")),
        };

        // 균열을 percent%까지 올려 둔다(나무·산호·사당 토큰을 앞에서부터 파괴 — 85·86행 장면)
        static void BreachTo(GameState st, int percent)
        {
            foreach (var t in st.Tokens)
            {
                if (UbTurn.Breach(st) >= percent) break;
                if (UbTurn.IsTarget(t.Kind) && t.Cell >= 0 && !t.Destroyed) t.Destroyed = true;
            }
            if (UbTurn.Breach(st) != percent) throw new InvalidOperationException("균열을 " + percent + "%로 맞추지 못했다: " + UbTurn.Breach(st));
        }

        // 균열 칩: 보기 값과 같은 숫자 · 작은 칩 둘 · 글 넘침 없음(tip = 마우스 오버를 띄운다)
        static void Gauge(UiShot s, GameFlow flow, bool tip)
        {
            s.Panel.Tick(3);
            var hud = flow.Hud;
            var view = flow.Client.View;
            int want = UbGauge.Breach(view.Tokens);
            if (hud.Ub.ShownBreach != want) throw new InvalidOperationException("균열 칩 " + hud.Ub.ShownBreach + " ≠ 보기 " + want);
            var root = flow.Stage.Root;
            var value = root.Q<Label>("board-hud-ub-value");
            if (value == null || !value.text.StartsWith(want.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)) throw new InvalidOperationException("균열 칩 글: " + (value != null ? value.text : "없음"));
            foreach (var name in new[] { BoardUb.Gauge, BoardUb.Prism, BoardUb.Bless })
            {
                var e = root.Q(name);
                if (e == null || e.resolvedStyle.display == DisplayStyle.None) throw new InvalidOperationException(name + "이 보이지 않는다");
            }
            UbClean(hud);
            if (tip) flow.Stage.Tips.Show(root.Q(BoardUb.Gauge));
        }

        // 프리즘 칩에 마우스: 툴팁에 효과 문구(screen.board.ub.prism.tip.effect)가 있다
        static void PrismTip(UiShot s, GameFlow flow)
        {
            Gauge(s, flow, false);
            flow.Stage.Tips.Show(flow.Stage.Root.Q(BoardUb.Prism));
            TipHas(flow, "screen.board.ub.prism.tip.effect");
        }

        // 떠 있는 툴팁의 글에 그 키의 문구가 있다
        static void TipHas(GameFlow flow, string key)
        {
            string want = flow.Stage.T(key).Replace("⁠", "");
            foreach (var l in flow.Stage.Tips.Element.Query<Label>().ToList())
                if (l.text.Replace("⁠", "").Contains(want)) return;
            throw new InvalidOperationException("툴팁에 " + key + " 문구가 없다");
        }

        // 연출 장면: 그 요소가 떠 있고(seen) 칩·띠의 글이 넘치지 않는다
        static void Hud(UiShot s, GameFlow flow, Func<BoardHud, bool> seen, string what)
        {
            var hud = flow.Hud;
            if (hud == null || hud.Ub == null) throw new InvalidOperationException("보드 HUD가 없다");
            if (!seen(hud)) throw new InvalidOperationException(what + "이(가) 보이지 않는다");
            UbClean(hud);
        }

        // a가 b보다 먼저 그려지나(같은 부모 안에서 앞 — b가 그 위에 그려진다)
        static bool Behind(VisualElement root, string a, string b)
        {
            var ea = root.Q(a);
            var eb = root.Q(b);
            return ea != null && eb != null && ea.parent == eb.parent && ea.parent.IndexOf(ea) < ea.parent.IndexOf(eb);
        }

        static void UbClean(BoardHud hud)
        {
            var bad = hud.Ub.Overflows();
            if (bad.Count > 0) throw new InvalidOperationException("UB 칩 글 넘침: " + string.Join(" / ", bad));
        }
    }
}
