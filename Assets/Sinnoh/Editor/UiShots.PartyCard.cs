using System;
using System.Collections.Generic;
using System.Globalization;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 결정 206 ③(<see cref="PartyScreen"/> — 목업 r10-party-row6 · r10-party-drag): 카드 쌓기 견본 판(<see cref="SoloTrail.PrepareCards"/>의 다섯 + 팽도리)에
    /// 기술 카드(아쿠아제트 · 토대부기가 배울 수 있는 카드)와 가방의 지닌물건을 쥐여 정비 판을 연다. 단정 <see cref="PartyCardCheck"/>: 카드 여섯 = 원작 카드 그림 · 띠 수(빈 칸 없음) ·
    /// [◀][▶][놓아주기] 줄이 여섯 칸 모두 같은 높이 · 쌓기가 단추 줄 위에서 끝남 · 칸이 판 안 · 오른쪽 기둥과 안 겹침. 끄는 장면은 홈(띠와 같은 크기) · 금색 테 · 따라오는 띠를 본다.
    /// 그림 <c>Logs/Review/card/impl2c/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] PartyCardScreens =
        {
            Screen("party-card", Fit(s => PartyCard(s))),                         // 여섯 칸 한 줄(r10-party-row6)
            Screen("party-card-drag", Fit(s => PartyCardDrag(s, "aqua_jet", 5, 2))),    // 아쿠아제트를 끄는 중: 팽도리 빈 칸 둘에 홈 · 끄는 띠는 둘째 홈 위(r10-party-drag)
            Screen("party-card-full", Fit(s => PartyCardDrag(s, null, 0, 1))),          // 토대부기가 배울 수 있는 카드를 끄는 중: 찬 칸(배운 기술) = 금색 테 · 빈 칸이 없으니 홈 없음
            Screen("party-card-item", Fit(PartyCardItem)),                        // 가방의 지닌물건을 끄는 중: 받을 수 있는 카드의 지닌물건 동그라미가 빛난다
            Screen("party-card-order", Fit(PartyCardOrder)),                      // 팽도리 카드를 끌어 맨 앞 칸 위: 받는 카드 테(결정 107 ①)
            Screen("party-card-forget", Fit(PartyCardForget)),                    // 토대부기 첫 띠(깨물어부수기)를 눌러 잊게 할지 묻는 줄(오른쪽 기둥)
        };

        // 견본 판: PrepareCards의 다섯(토대부기 · 초염몽 · 디아루가 · 렌트라 · 찌르호크 — 갤럭시단 조무래기는 파티 밖) + 팽도리 · 기술 카드 셋 · 가방의 지닌물건 하나
        internal static void PartyCardBoard(Ctx ctx, GameState state)
        {
            SoloTrail.PrepareCards(ctx, state);
            var st = state.Seats[0];
            PokemonRules.AddToParty(st, PokemonRules.Spawn(state, ctx.Cards.PokemonIds.IndexOf("393:0"), 0, (int)DeckId.Starter));
            foreach (string move in new[] { "aqua_jet", "razor_leaf", "giga_drain" }) st.StampMoves.Add(ctx.Cards.MoveIds.IndexOf(move));
            st.Bag.Add(ctx.Items.Ids.IndexOf("item.lucky_egg"));
        }

        static PartyScreen PartyCard(UiShot s)
        {
            var flow = Seats(s, 1, st => PartyCardBoard(s.Board.Ctx, st));
            if (!flow.OpenParty() || !(flow.Stage.Current is PartyScreen party)) throw new InvalidOperationException("정비 판이 열리지 않았다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            PartyCardCheck(flow, party.Dragging);
            return party;
        }

        // move = 끌 기술(null이면 토대부기가 배울 수 있는 첫 카드) · 놓을 자리 = 칸 col의 기술 칸 slot(1|2)
        static void PartyCardDrag(UiShot s, string move, int col, int slot)
        {
            var party = PartyCard(s);
            var flow = s.Made;
            var root = flow.Stage.Root;
            int n = -1;
            for (int i = 0; i < party.Pool.Count && n < 0; i++)
                if (move != null ? flow.Ctx.Cards.Moves[party.Pool[i]].Key == move : party.PickMove(i) && party.CanPut(0, 0)) n = i;
            if (move == null) party.Cancel();
            if (n < 0) throw new InvalidOperationException("끌 기술 카드가 풀에 없다");
            if (!party.BeginDrag(PartyScreen.Hold.Move, n)) throw new InvalidOperationException("기술 카드를 끌지 못했다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var target = root.Q("board-party-slot-" + col + "-" + slot);
            if (target == null || !target.ClassListContains("sn-party-ok")) throw new InvalidOperationException("칸 " + col + "-" + slot + "이 빛나지 않는다");
            // 포인터를 홈의 왼쪽 위 가까이 — 따라오는 띠가 그 홈에 거의 겹친다(목업 r10-party-drag의 꼴), 찬 칸은 가운데
            party.DragTo(move != null ? target.worldBound.position + new Vector2(4f, 4f) : target.worldBound.center);
            s.Panel.Tick(3);
            PartyCardCheck(flow, true);
            var ghost = root.Q("board-party-ghost").Q(className: "sn-pcard-ghost");
            if (ghost == null || ghost.resolvedStyle.backgroundImage.texture == null) throw new InvalidOperationException("따라오는 것이 기술 띠 그림이 아니다");
            var view = flow.Client.View;
            var me = view.Seats[flow.Client.Seat];
            int holes = 0;
            for (int i = 0; i < PartyScreen.Columns; i++)
            {
                var pk = me.Party[i] >= 0 ? view.Pokemon[me.Party[i]] : null;
                for (int k = 0; pk != null && k < PokemonState.MoveSlots; k++)
                {
                    var e = root.Q("board-party-slot-" + i + "-" + (k + 1));
                    bool empty = pk.Moves[k] < 0 && pk.Breach[k] < 0 && LegendRules.TacticAt(pk, k) == LegendTactic.None;
                    bool can = party.CanPut(i, k);
                    if (empty && (e != null) != can) throw new InvalidOperationException("칸 " + i + "-" + (k + 1) + ": 홈 " + (e != null) + " · 받음 " + can);
                    if (e == null) continue;
                    if (e.ClassListContains("sn-party-ok") != can) throw new InvalidOperationException("칸 " + i + "-" + (k + 1) + ": 빛남이 엔진 검사와 다르다");
                    if (!empty) continue;
                    holes++;
                    var hole = e.parent;   // 홈 띠(카드 쌓기의 card-strip-n)
                    if (!hole.ClassListContains("sn-card-hole")) throw new InvalidOperationException("칸 " + i + "-" + (k + 1) + ": 홈 띠가 아니다");
                    float want = Mathf.Round(hole.resolvedStyle.width * CardStack.MoveStripPx / (CardStack.WidthCm * CardStack.PxPerCm));
                    if (Mathf.Abs(hole.resolvedStyle.height - want) > 0.6f) throw new InvalidOperationException("홈 높이 " + hole.resolvedStyle.height + " ≠ 기술 띠 " + want);
                    if (Mathf.Abs(ghost.resolvedStyle.width - hole.resolvedStyle.width) > 0.6f) throw new InvalidOperationException("끄는 띠 폭 " + ghost.resolvedStyle.width + " ≠ 홈 폭 " + hole.resolvedStyle.width);
                }
            }
            if (move != null && (holes < 2 || root.Q("board-party-slot-5-1") == null || root.Q("board-party-slot-5-2") == null)) throw new InvalidOperationException("팽도리 빈 칸 둘에 홈이 없다(홈 " + holes + ")");
            Debug.Log("[UiShots] party-card-drag 홈 " + holes + " · 띠 " + ghost.resolvedStyle.width + "×" + ghost.resolvedStyle.height);
        }

        static void PartyCardItem(UiShot s)
        {
            var party = PartyCard(s);
            var flow = s.Made;
            var root = flow.Stage.Root;
            if (!party.BeginDrag(PartyScreen.Hold.Item, 0)) throw new InvalidOperationException("지닌물건을 끌지 못했다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var spot = root.Q("board-party-item-5");
            if (spot == null || !spot.ClassListContains("sn-party-ok")) throw new InvalidOperationException("팽도리의 지닌물건 동그라미가 빛나지 않는다");
            var face = root.Q("board-party-mon-5").Q("card-face").worldBound;
            if (!face.Contains(spot.worldBound.center)) throw new InvalidOperationException("지닌물건 동그라미가 카드 앞면 밖이다");
            party.DragTo(spot.worldBound.center);
            s.Panel.Tick(3);
            var token = root.Q("board-party-ghost").Q(className: "sn-pcard-ghost") ?? throw new InvalidOperationException("따라오는 것이 지닌물건 토큰이 아니다");
            if (!token.worldBound.Contains(spot.worldBound.center)) throw new InvalidOperationException("토큰이 동그라미 위에 있지 않다(" + token.worldBound + ")");   // 검토 지적 14: 칩이 카드 타입 줄 위에 얹히지 않는다
        }

        static void PartyCardOrder(UiShot s)
        {
            var party = PartyCard(s);
            var flow = s.Made;
            var root = flow.Stage.Root;
            if (!party.BeginDrag(PartyScreen.Hold.Mon, 5)) throw new InvalidOperationException("팽도리 카드를 끌지 못했다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var first = root.Q("board-party-mon-0");
            if (!first.ClassListContains("sn-party-ok") || first.Q(className: "sn-pcard-ring") == null) throw new InvalidOperationException("맨 앞 카드가 받는 테를 두르지 않았다");
            party.DragTo(root.Q("board-party-col-0").worldBound.center);
            s.Panel.Tick(3);
            PartyCardCheck(flow, true);
        }

        static void PartyCardForget(UiShot s)
        {
            var party = PartyCard(s);
            var flow = s.Made;
            var root = flow.Stage.Root;
            if (!party.PressSlot(0, 0) || !party.Asking) throw new InvalidOperationException("잊게 하기 물음이 뜨지 않았다");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var ask = root.Q("board-party-ask").worldBound;
            var side = root.Q("board-party-side").worldBound;
            if (ask.xMin < side.xMin - 0.5f || ask.xMax > side.xMax + 0.5f || ask.yMin < side.yMin - 0.5f) throw new InvalidOperationException("물음 줄이 오른쪽 기둥 밖이다(" + ask + " · 기둥 " + side + ")");
            if (root.Q("board-party-ask-yes").worldBound.xMax > side.xMax + 0.5f) throw new InvalidOperationException("물음 줄 단추가 기둥 밖이다");
            PartyCardCheck(flow, false);
        }

        // 칸마다 카드 쌓기(홈 포함)의 맨 아래가 단추 줄 위(내 판) · 판 안
        static void StacksFit(GameFlow flow)
        {
            var root = flow.Stage.Root;
            var panel = root.Q("board-party-panel").worldBound;
            for (int i = 0; i < PartyScreen.Columns; i++)
            {
                var stack = root.Q("board-party-mon-" + i)?.Q("card-stack");
                if (stack == null) continue;
                var release = root.Q("board-party-release-" + i);
                float floor = release != null ? release.worldBound.yMin : panel.yMax;
                if (stack.worldBound.yMax > floor + 0.5f) throw new InvalidOperationException("칸 " + i + ": 쌓기 끝 " + stack.worldBound.yMax + "이 단추 줄 · 판 끝 " + floor + "을 넘는다");
            }
        }

        /// <summary>
        /// 정비 판 카드 칸 단정(결정 206 ③): 칸 여섯 = 원작 카드 그림 · 끄지 않을 때 띠 수 = 견본 그대로(빈 칸 없음, 「빈 칸」 글 없음) · [◀][▶][놓아주기] 줄이 모두 같은 높이 ·
        /// 쌓기 맨 아래(홈 포함)가 단추 줄 위 · 칸이 판 안 · 칸 줄과 오른쪽 기둥이 겹치지 않음.
        /// </summary>
        static void PartyCardCheck(GameFlow flow, bool dragging)
        {
            var root = flow.Stage.Root;
            var panel = root.Q("board-party-panel").worldBound;
            var side = root.Q("board-party-side").worldBound;
            int[] want = { 2, 2, 1, 2, 0, 0 };   // 토대부기 기술 둘 · 초염몽 기술 + 균열 · 디아루가 전술 판 · 렌트라 기술 + 비스트 부스트 · 찌르호크 · 팽도리
            float foot = float.NaN, lowest = 0f;
            var heights = new List<string>();
            for (int i = 0; i < PartyScreen.Columns; i++)
            {
                var mon = root.Q("board-party-mon-" + i);
                if (mon == null || mon.Q("card-face") == null) throw new InvalidOperationException("칸 " + i + ": 원작 카드 그림이 아니다");
                int strips = 0;
                for (int n = 1; mon.Q("card-strip-" + n.ToString(CultureInfo.InvariantCulture)) is VisualElement e; n++)
                    if (!e.ClassListContains("sn-card-hole")) strips++;
                if (strips != want[i]) throw new InvalidOperationException("칸 " + i + ": 띠 " + strips + " ≠ " + want[i]);
                var col = root.Q("board-party-col-" + i).worldBound;
                if (col.xMin < panel.xMin - 0.5f || col.xMax > panel.xMax + 0.5f || col.yMax > panel.yMax + 0.5f) throw new InvalidOperationException("칸 " + i + "이 판 밖이다(" + col + " · 판 " + panel + ")");
                if (col.xMax > side.xMin + 0.5f) throw new InvalidOperationException("칸 " + i + "이 오른쪽 기둥과 겹친다");
                var release = root.Q("board-party-release-" + i).worldBound;
                if (float.IsNaN(foot)) foot = release.yMin;
                else if (Mathf.Abs(release.yMin - foot) > 0.5f) throw new InvalidOperationException("칸 " + i + "의 단추 줄 높이 " + release.yMin + " ≠ " + foot);
                if (release.yMax > panel.yMax + 0.5f) throw new InvalidOperationException("단추 줄이 판 밖이다");
                var stack = mon.Q("card-stack").worldBound;
                if (stack.yMax > release.yMin + 0.5f) throw new InvalidOperationException("칸 " + i + ": 쌓기 끝 " + stack.yMax + "이 단추 줄 " + release.yMin + "을 덮는다");
                lowest = Mathf.Max(lowest, stack.yMax);
                heights.Add(stack.height.ToString("0"));
            }
            if (root.Q(className: "sn-party-slot-empty") != null) throw new InvalidOperationException("「빈 칸」 기술 줄이 남았다");
            if (side.xMax > panel.xMax + 0.5f || side.yMax > panel.yMax + 0.5f) throw new InvalidOperationException("오른쪽 기둥이 판 밖이다");
            Debug.Log("[UiShots] party-card 쌓기 " + string.Join("/", heights) + " · 가장 낮은 끝 " + lowest.ToString("0") + " · 단추 줄 " + foot.ToString("0") + " · 판 끝 " + panel.yMax.ToString("0") + (dragging ? " · 끄는 중" : ""));
        }
    }
}
