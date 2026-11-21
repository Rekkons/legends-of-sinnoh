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
    /// 사용자 결정 201 ②(2026-11-22): 갤럭시단 상대가 있는 칸에 마우스를 올린 모습 — 공개 정보 카드의 명단(조무래기 토큰 칸 · 2막 입지호수의 새턴(막 길 <see cref="SoloTrail.Lake"/>) ·
    /// 3막 신오신전의 태홍(<see cref="SoloTrail.Heart"/>)) · 명단 줄에 마우스를 올린 포켓몬 카드(<c>-tip</c>). 장면마다 카드 · 포켓몬 카드가 화면 안 · 서로 안 겹침 · 줄 수 = 덱 데이터 · 글 넘침을 단정한다.
    /// 모두 보드 위. 그림 <c>Logs/Review/m10/galactic-board/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] GalacticHoverScreens =
        {
            Screen("galactic-hover-grunt", s => GalacticHover(s, null, f => f.Client.View.Seats[0].Cell, DeckId.GalacticGrunt, -1)),          // 고향 마을의 조무래기 8장 · 무작위 2마리
            Screen("galactic-hover-grunt-tip", s => GalacticHover(s, null, f => f.Client.View.Seats[0].Cell, DeckId.GalacticGrunt, 2)),
            Screen("galactic-hover-commander", s => GalacticHover(s, SoloTrail.Lake, f => HosFlow.LakeCell(f.Ctx, Shard.Willpower), DeckId.GalacticCommander, -1)),   // 입지호수 새턴 5장 · 조각 2 + 2마리
            Screen("galactic-hover-commander-tip", s => GalacticHover(s, SoloTrail.Lake, f => HosFlow.LakeCell(f.Ctx, Shard.Willpower), DeckId.GalacticCommander, 4)),
            Screen("galactic-hover-boss", s => GalacticHover(s, SoloTrail.Heart, f => HosFlow.TempleCell(f.Ctx), DeckId.GalacticBoss, -1)),   // 신오신전 태홍 8장 · 6마리
            Screen("galactic-hover-boss-tip", s => GalacticHover(s, SoloTrail.Heart, f => HosFlow.TempleCell(f.Ctx), DeckId.GalacticBoss, 7)),
        };

        static void GalacticHover(UiShot s, SoloTrail trail, Func<GameFlow, int> cellOf, DeckId deck, int tip)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 1, trail != null ? st => trail.After(ctx, st) : (Action<GameState>)null);
            int cell = cellOf(flow);
            s.Rig.Set(BoardCameraRig.Zoom.Region, s.Rig.Yaw, s.Board.CellTop(cell));
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var hud = flow.Hud;
            hud.Hover(cell);
            s.Panel.Tick(3);
            var info = hud.Info;
            if (info.Subject != "cell-" + cell || !info.HasRoster) throw new InvalidOperationException(cell + ": 명단 카드가 뜨지 않았다(" + info.Subject + ")");
            var cards = new List<int>();
            if (!InfoCard.GalacticFoe(ctx, flow.Client.View, flow.Client.Seat, cell, deck == DeckId.GalacticGrunt, cards, out _, out _)) throw new InvalidOperationException(cell + ": 상대 없음");
            int expect = 0;
            foreach (var e in ctx.Deck(deck).Cards)
                if (cards.Contains(e.Card)) expect++;
            int rows = 0;
            while (info.Element.Q("info-card-galactic-mon-" + rows) != null) rows++;
            if (rows != cards.Count || rows != expect || (deck == DeckId.GalacticCommander ? rows != 5 : rows != 8)) throw new InvalidOperationException(cell + ": 명단 줄 " + rows + " ≠ 덱 " + expect);
            var root = flow.Stage.Root.worldBound;
            var box = info.Element.worldBound;
            if (!RosterInside(box, root)) throw new InvalidOperationException("명단 카드가 화면 밖: " + box);
            // 마우스를 받는 카드라 위 띠 · 아래 판(자원 줄 · 돌리기 단추)의 단추를 덮지 않는다
            foreach (string name in new[] { "board-hud-top", "board-hud-dock", "board-hud-res", "board-hud-rotate" })
            {
                var e = flow.Stage.Root.Q(name);
                if (e != null && e.resolvedStyle.display == DisplayStyle.Flex && e.worldBound.Overlaps(box)) throw new InvalidOperationException("명단 카드 " + box + "가 " + name + " " + e.worldBound + "를 덮는다");
            }
            var bad = WorkUi.Overflows(info.Element);
            bad.AddRange(WorkUi.Overflows(flow.Stage.Root.Q("board-hud-cell")));
            if (tip >= 0)
            {
                if (!info.ShowRosterMon(tip)) throw new InvalidOperationException(tip + "번 줄이 없다");
                s.Panel.Tick(3);
                var mon = info.MonTipElement;
                if (info.MonShown != cards[tip] || mon == null || mon.resolvedStyle.display != DisplayStyle.Flex) throw new InvalidOperationException(tip + "번 줄의 포켓몬 카드가 뜨지 않았다");
                var mb = mon.worldBound;
                if (!RosterInside(mb, root)) throw new InvalidOperationException("포켓몬 카드가 화면 밖: " + mb);
                if (mb.Overlaps(box)) throw new InvalidOperationException("포켓몬 카드 " + mb + "가 명단 카드 " + box + "를 덮는다");
                bad.AddRange(WorkUi.Overflows(mon));
            }
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + bad.Count + "곳: " + string.Join(" / ", bad));
        }

        static bool RosterInside(Rect box, Rect root) => box.xMin >= root.xMin - 0.5f && box.xMax <= root.xMax + 0.5f && box.yMin >= root.yMin - 0.5f && box.yMax <= root.yMax + 0.5f;
    }
}
