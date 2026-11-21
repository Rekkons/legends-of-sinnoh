using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 사용자 결정 179(2026-11-21): 울트라 버스트 4인 판(시드 20261117)에서 UB · 네크로즈마 피규어에 마우스를 올린 모습(칸 정보 줄 · 공개 정보 카드의 이름 — 네크로즈마는 폼 이름까지),
    /// 호수 수호신 피규어에 올린 모습(시련 이름 · 조건 · 면역 · 보상 / 넘긴 뒤 「시련 통과」), UB가 옮겨 가는 연출 동안 도착 칸에 올린 모습(반을 넘긴 뒤 — 피규어가 보이는 칸).
    /// 마우스는 실제 길(<see cref="GameFlow.Pointer(float, float, bool)"/> — 피규어 머리 쪽)로 넣고, 장면마다 글 · 카드가 화면 안 · 글 넘침을 단정한다. 모두 보드 위. 그림 <c>Logs/Review/m10/ub-hover/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] UbHoverScreens =
        {
            Screen("ub-hover-beast", s => UbHover(s, null, (sh, flow) => BeastAim(sh, flow, TokenKind.UltraBeast))),          // UB 피규어 위: 이름
            Screen("ub-hover-necrozma", s => UbHover(s, st => { st.Ub.NecrozmaPhase = 2; st.Ub.SunMoon = UbNecrozma.Sun; }, (sh, flow) => BeastAim(sh, flow, TokenKind.Necrozma))),   // 2페이즈 황혼의 갈기: 폼 이름까지
            Screen("ub-hover-trial", s => UbHover(s, null, (sh, flow) => GuardianAim(sh, flow, LakeValor))),                  // 아그놈: 의지의 시련 · 조건 · 면역 · 보상
            Screen("ub-hover-trial-passed", s => UbHover(s, st => st.Ub.TrialsOvercome[(int)Shard.Willpower] = true, (sh, flow) => GuardianAim(sh, flow, LakeValor))),   // 넘긴 뒤: 시련 통과
            Screen("ub-hover-trial-emotion", s => UbHover(s, null, (sh, flow) => GuardianAim(sh, flow, "legendary_site.lake_verity_cavern"))),   // 엠라이트: 감정의 시련
            Screen("ub-hover-move", UbHoverMove),                                                                             // UB가 옮겨 가는 중(80%) — 도착 칸에 이름
        };

        // 가리킬 피규어 · 그 칸 · 칸 정보 줄과 카드에 있어야 할 이름 · 카드 블록
        struct HoverAim
        {
            public BoardFigures.Figure Figure;
            public int Cell;
            public string Name, Block;
        }

        static HoverAim BeastAim(UiShot s, GameFlow flow, TokenKind kind)
        {
            var view = flow.Client.View;
            foreach (var t in view.Tokens)
            {
                if (t.Kind != kind || t.Cell < 0) continue;
                string key = kind == TokenKind.Necrozma ? UbBoard.NecrozmaForm(view.Ub, null) : flow.Ctx.Cards.Pokemon[t.Def].Key;
                return new HoverAim { Figure = HoverFigure(s, key), Cell = t.Cell, Name = flow.Stage.T("pokemon." + key), Block = "info-card-beast" };
            }
            throw new InvalidOperationException(kind + " 토큰이 맵에 없다");
        }

        static HoverAim GuardianAim(UiShot s, GameFlow flow, string site)
        {
            var ctx = flow.Ctx;
            string key = SiteKey(ctx, site);
            if (!flow.FigureShown(key, out _)) throw new InvalidOperationException(site + ": 수호신 피규어가 서 있지 않다(결정 128)");
            return new HoverAim { Figure = HoverFigure(s, key), Cell = FixtureCell(ctx, site), Name = flow.Stage.T("pokemon." + key), Block = "info-card-trial" };
        }

        // 그 키의 피규어(네크로즈마 폼은 판의 1페이즈 자리에 세운 폼)
        static BoardFigures.Figure HoverFigure(UiShot s, string key)
        {
            foreach (var f in s.Board.Figures.Figures)
                if (f.Key == key) return f;
            foreach (var f in s.Board.Figures.Figures)   // 네크로즈마의 다른 폼(판에 자리가 없는 행)
                if (f.Group == GameFlow.NecrozmaFigures && s.Board.Figures.FormOf(f, key) is BoardFigures.Figure form && form.Key == key) return form;
            throw new InvalidOperationException("피규어 없음: " + key);
        }

        static void UbHover(UiShot s, Action<GameState> prepare, Func<UiShot, GameFlow, HoverAim> aim)
        {
            var flow = UbGame(s, prepare);
            PointAt(s, flow, aim(s, flow));
        }

        // 피규어 머리 쪽(구운 경계 높이 80%)에 마우스를 둔다 — 카메라는 그 칸을 가까이(카드가 붙는 오른쪽이 넉넉하게 한 칸 왼쪽에서 본다)
        static void PointAt(UiShot s, GameFlow flow, HoverAim a)
        {
            LookAt(s, flow, a.Cell);
            if (!a.Figure.Root.activeInHierarchy) throw new InvalidOperationException(a.Name + ": 피규어가 서 있지 않다");
            var bb = BoardFigures.BakedBounds(a.Figure.Root);
            var top = new Vector3(bb.center.x, bb.min.y + bb.size.y * 0.8f, bb.center.z);
            var cam = s.Rig.GetComponent<Camera>();
            if (!BoardPick.ScreenAt(cam, top, s.Width, s.Height, out var px)) throw new InvalidOperationException(a.Name + ": 피규어가 카메라 뒤");
            flow.Pointer(px.x, px.y, false);
            s.Panel.Tick(3);
            HoverCheck(s, flow, a);
        }

        // 칸 정보 줄 · 공개 정보 카드: 그 칸 · 그 이름, 카드가 화면 안, 글 넘침 없음
        static void HoverCheck(UiShot s, GameFlow flow, HoverAim a)
        {
            var hud = flow.Hud;
            if (hud.HoverAny != a.Cell) throw new InvalidOperationException(a.Name + ": 칸 정보 줄의 칸 " + hud.HoverAny + " ≠ 피규어의 칸 " + a.Cell);
            if (hud.CellInfoText == null || !hud.CellInfoText.Contains(a.Name)) throw new InvalidOperationException("칸 정보 줄에 「" + a.Name + "」가 없다: " + hud.CellInfoText);
            if (hud.Info.Subject != "cell-" + a.Cell) throw new InvalidOperationException(a.Name + ": 공개 정보 카드가 그 칸이 아니다(" + hud.Info.Subject + ")");
            var card = hud.Info.Element;
            bool named = false;
            card.Query<VisualElement>(a.Block).ForEach(b => b.Query<Label>().ForEach(l => named |= l.text == a.Name));
            if (!named) throw new InvalidOperationException(a.Name + ": 카드 " + a.Block + "에 이름이 없다");
            var box = card.worldBound;
            var root = flow.Stage.Root.worldBound;
            if (box.xMin < root.xMin || box.xMax > root.xMax || box.yMin < root.yMin || box.yMax > root.yMax) throw new InvalidOperationException("공개 정보 카드가 화면 밖: " + box);
            var bad = WorkUi.Overflows(card);
            bad.AddRange(WorkUi.Overflows(flow.Stage.Root.Q("board-hud-cell")));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + bad.Count + "곳: " + string.Join(" / ", bad));
        }

        // 라운드 끝 UB 턴의 첫 이동 단계 80%(반을 넘김): 도착 칸에 마우스 — 그 UB 이름 · 출발 칸에는 없다
        static void UbHoverMove(UiShot s)
        {
            var flow = UbTurnInto(s, st => st.Row == UbRows.Move, 0);
            var step = Queued(flow, st => st.Row == UbRows.Move);
            var m = UbRows.First<UbMoved>(step);
            string name = flow.Stage.T("pokemon." + flow.Ctx.Cards.Pokemon[m.UbId].Key);
            flow.Hud.Hover(m.To);
            if (flow.Hud.CellInfoText.Contains(name)) throw new InvalidOperationException("이동 단계 처음인데 도착 칸에 이름이 있다");
            flow.Stage.Tweens.Advance(step.Ms * 8 / 10);
            s.Rig.Set(BoardCameraRig.Zoom.Close, s.Rig.Yaw, s.Board.CellTop(m.To));
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            flow.Hud.Hover(m.To);   // 마우스를 그대로 둔 채(같은 칸) — 피규어가 반을 넘으면 다시 적는다
            s.Panel.Tick(3);
            if (flow.BeastCell(m.UbId) != m.To) throw new InvalidOperationException("80%인데 보이는 칸이 도착 칸이 아니다");
            HoverCheck(s, flow, new HoverAim { Cell = m.To, Name = name, Block = "info-card-beast" });
            var at = flow.Hud.CellText(m.From);
            if (at.Contains(name)) throw new InvalidOperationException("반을 넘긴 뒤 출발 칸에 이름이 남았다");
        }
    }
}
