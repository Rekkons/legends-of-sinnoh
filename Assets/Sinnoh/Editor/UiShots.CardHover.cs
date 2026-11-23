using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 결정 206 ①(목업 B — <c>Logs/Review/card/</c> 1차 B-battle · B-party · B-wild와 나란히 견준다): 견본 판 <see cref="SoloTrail.PrepareCards"/>의 파티로
    /// <c>card-tip-battle</c>(첫 출전 고르기 줄의 토대부기 — 기술 띠 둘 · 지닌물건 글 상자 둘) · <c>-legend</c>(디아루가 — 전설 전술 판) · <c>card-zoom</c>(정비 → 초염몽 카드 확대 — 기술 + 균열 띠) ·
    /// <c>-alpha</c>(렌트라 — 우두머리 토큰 · 비스트 부스트만) · <c>card-tip-wild</c>(사당 교환 목록의 야생 카드 = p22-hover). 영어는 <c>-uiLang en</c>(en 카드). 그림 <c>Logs/Review/card/impl2a/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] CardHoverScreens =
        {
            Battle("card-tip-battle", s => CardTipBattle(s, 0, 2, 2)),
            Battle("card-tip-battle-legend", s => CardTipBattle(s, 2, 1, 1)),
            Screen("card-zoom", Fit(s => CardZoom(s, 1, 2, 1))),
            Screen("card-zoom-alpha", Fit(s => CardZoom(s, 3, 2, 1))),
            Screen("card-tip-wild", s => Hover22(s)),
        };

        // 첫 출전 고르기 줄에서 파티 n번째 포켓몬 칸에 마우스 — 카드 + 오른쪽 기둥(띠 수 · 글 상자 수), 기록 기둥 아래 끝에 바닥(독 · 화면 안은 Inspect)
        static void CardTipBattle(UiShot s, int n, int strips, int notes)
        {
            var ctx = s.Board.Ctx;
            int[] ids = null;
            var flow = Seats(s, 1, st => ids = SoloTrail.PrepareCards(ctx, st));
            if (!flow.Hud.Press(BoardHud.Challenge)) throw new InvalidOperationException("[도전]이 거절됐다");
            var screen = Fight(flow, b => b.Open && b.Bench.Count >= 2 && flow.Client.FirstChoice() != null && flow.Client.FirstChoice().Kind == ChoiceKind.SendIn);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            int cell = -1;
            for (int i = 0; i < screen.Bench.Count; i++) if (screen.Bench[i].Target == ids[n]) cell = i + 1;
            if (cell < 0) throw new InvalidOperationException("고르기 줄에 파티 " + n + "번 포켓몬이 없다");
            MonTipAt(s, flow, "battle-bench-" + cell);
            CardCount(screen.MonTipElement, strips, notes, "card-tip-battle");
        }

        // 정비 판 → 파티 n번째 포켓몬 카드 확대(MonScreen) — 원작 카드 + 오른쪽 기둥 · 진화 줄 · [닫기], 화면 안
        static void CardZoom(UiShot s, int n, int strips, int notes)
        {
            var ctx = s.Board.Ctx;
            int[] ids = null;
            var flow = Seats(s, 1, st => ids = SoloTrail.PrepareCards(ctx, st));
            if (!flow.OpenParty()) throw new InvalidOperationException("정비 판이 열리지 않았다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (!flow.OpenMon(ids[n], 0)) throw new InvalidOperationException("카드 확대가 열리지 않았다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var zoom = flow.Stage.Current as MonScreen ?? throw new InvalidOperationException("카드 확대가 무대의 화면이 아니다");
            if (zoom.Stack == null || !zoom.Stack.Art) throw new InvalidOperationException("카드 확대가 원작 카드가 아니다(" + zoom.Stack?.FaceKey + ")");
            var root = flow.Stage.Root;
            CardCount(root.Q("mon-detail-panel"), strips, notes, "card-zoom");
            var card = zoom.Stack.Root;
            var block = root.Q("mon-detail-block");
            if (card.Q("mon-detail-evolve") == null || block?.Q("mon-detail-close") == null) throw new InvalidOperationException("기둥에 진화 줄 · 묶음에 [닫기]가 없다");
            var box = block.worldBound;
            var all = root.worldBound;
            if (box.xMin < all.xMin - 0.5f || box.yMin < all.yMin - 0.5f || box.xMax > all.xMax + 0.5f || box.yMax > all.yMax + 0.5f) throw new InvalidOperationException("카드 확대가 화면 밖이다(" + box + ")");
            var dock = root.Q("board-hud-dock");
            if (dock != null && box.yMax > dock.worldBound.yMin + 0.5f) throw new InvalidOperationException("카드 확대가 아래 판을 덮는다(" + box + " · 아래 판 위 " + dock.worldBound.yMin + ")");
            if (!box.Contains(block.Q("mon-detail-close").worldBound.center)) throw new InvalidOperationException("[닫기]가 묶음 밖이다");
            UnityEngine.Debug.Log($"[UiShots] card-zoom {s.Grade.Height}: {zoom.Stack.FaceKey} 카드 {box}");
        }

        static void CardCount(VisualElement box, int strips, int notes, string what)
        {
            int st = 0, no = 0;
            while (box.Q("card-strip-" + (st + 1)) != null) st++;
            while (box.Q("card-note-" + (no + 1)) != null) no++;
            if (st != strips || no != notes) throw new InvalidOperationException(what + ": 띠 " + st + " · 글 상자 " + no + " ≠ " + strips + " · " + notes);
        }
    }
}
