using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 방 판 덮개 층의 「남의 고르기 보기」(M9 다듬기 P09 — 결정 112 Q16, 04 §8-1 P15, 02 §4-10): 다른 좌석이 공개 선택(조우 카드 · 사당 카드 고르기 · 은행상회 줄)을 고르는 동안
    /// 위 가운데 줄에 「○○ — 조우 고르는 중」 + 그 후보 칸들(내 보기의 같은 선택에서 읽는다 — 전선으로는 자리 번호만 온다)을 보이고, 그 좌석이 가리키는 칸은 좌석 색 테두리 · 누른 칸은 좌석 색 바탕으로 칠한다.
    /// 그 좌석의 고르기를 보기 창(결정 120 ① — <see cref="GameFlow.Watch"/>)이 내 창처럼 보이고 있으면 이 줄은 뜨지 않는다(내가 창을 열어 보기 창이 숨은 동안만).
    /// 조우 · 사당은 그 선택이 열려 있는 동안(가리키는 것이 없어도 후보는 보인다), 은행상회는 가리키는 소식이 온 뒤 <see cref="PeekRate.BankShowMs"/> 동안.
    /// 내 화면의 공개 연출(카드 공개 21행 · 공물 31행)이 다 돈 뒤에만 보인다. 마우스를 받지 않는다(머리 글의 툴팁만).
    /// 요소 이름: <c>room-peek</c> · <c>room-peek-text</c> · <c>room-peek-opt-&lt;n&gt;</c>(클래스 <c>sn-room-peek-on</c> · <c>sn-room-peek-press</c>).
    /// </summary>
    public sealed partial class RoomLayer
    {
        VisualElement peek;
        string peekKey;

        /// <summary>지금 보이는 남의 고르기(없으면 좌석 −1) — 시험 · 확인.</summary>
        public int PeekSeat { get; private set; } = -1;
        public PeekSpot PeekSpot { get; private set; }
        /// <summary>보이는 후보 칸 수 · 그 좌석이 가리키는 칸(없으면 −1) · 눌렀나.</summary>
        public int PeekSlots { get; private set; }
        public int PeekIndex { get; private set; } = -1;
        public bool PeekPress { get; private set; }

        static readonly PeekSpot[] PeekChoices = { PeekSpot.Encounter, PeekSpot.Shrine };

        void Peek(RoomLive live)
        {
            live.FlushPeek();   // 내가 가리키는 것의 마지막 모습(보내는 간격이 지났으면)
            var view = flow.Client != null ? flow.Client.View : null;
            int me = live.Seat, seat = -1;
            var spot = PeekSpot.None;
            PendingChoice choice = null;
            RoomPeek mark = null;
            bool quiet = flow.Fx == null || flow.Fx.Sequencer.IsEmpty;
            if (view != null && quiet)
                for (int s = 0; s < view.Seats.Length && seat < 0; s++)
                {
                    if (s == me) continue;
                    if (flow.WatchShown && flow.Watch.Watched == s) continue;   // 그 좌석의 고르기를 보기 창이 내 창처럼 보이고 있다(결정 120 ①)
                    var m = live.PeekOf(s);
                    foreach (var sp in PeekChoices)
                    {
                        var c = PeekRules.Open(view, s, sp);
                        if (c == null || c.Options.Count == 0 || c.Options.Contains(Engine.Unknown)) continue;
                        seat = s;
                        spot = sp;
                        choice = c;
                        mark = m != null && m.Spot == sp && m.Index < c.Options.Count ? m : null;
                        break;
                    }
                    if (seat < 0 && m != null && m.Spot == PeekSpot.Bank && live.NowMs - m.AtMs < PeekRate.BankShowMs && PeekRules.Open(view, s, PeekSpot.Bank) != null && BankRows(view) != null)
                    {
                        seat = s;
                        spot = PeekSpot.Bank;
                        mark = m;
                    }
                }
            string key = seat < 0 ? "" : seat + "|" + spot + "|" + (choice != null ? choice.Id + ":" + string.Join(",", choice.Options) : BankRules.Card(view).ToString(CultureInfo.InvariantCulture))
                + "|" + (mark != null ? mark.Index + (mark.Press ? "!" : "") : "-") + "|" + Name(live, seat);
            if (key == peekKey) return;
            peekKey = key;
            peek.Clear();
            peek.style.display = seat >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            PeekSeat = seat;
            PeekSpot = spot;
            PeekIndex = mark != null ? mark.Index : -1;
            PeekPress = mark != null && mark.Press;
            PeekSlots = 0;
            if (seat < 0) return;

            string kind = spot == PeekSpot.Encounter ? "encounter" : spot == PeekSpot.Shrine ? "shrine" : "bank";
            var label = Add(peek, Kit.Text(Stage.T("screen.room.peek." + kind).Replace("{name}", Name(live, seat)), Kit.F2), "room-peek-text", "sn-room-strip-text");
            Stage.Tips.Hook(label, null, Stage.T("screen.room.peek.tip"), TipSide.Below, reading: true);
            var ctx = flow.Ctx;
            var tiles = new List<VisualElement>();
            if (spot == PeekSpot.Bank)
            {
                var rows = BankRows(view);
                for (int r = 0; r < rows.Count && r < 2; r++)
                {
                    var tile = Tile(tiles);
                    foreach (var c in rows[r].Give) WorkUi.Apricorn(tile, c);
                    Add(tile, Kit.Text(WorkUi.Arrow, Kit.F2), null, null);
                    foreach (var c in rows[r].Get) WorkUi.Apricorn(tile, c);
                }
            }
            else
                foreach (int id in choice.Options)
                {
                    string text = spot == PeekSpot.Encounter ? (id >= 0 && id < ctx.Cards.Pokemon.Count ? Stage.T("pokemon." + ctx.Cards.Pokemon[id].Key) : "?")
                        : id >= 0 && id < ctx.Decks.CardIds.Count ? Stage.T(ctx.Decks.CardIds[id]) : "?";
                    Add(Tile(tiles), Kit.Text(text, Kit.F2), null, null);
                }
            PeekSlots = tiles.Count;
            if (mark == null) return;
            var color = flow.SeatColor(seat);
            for (int i = 0; i < tiles.Count; i++)
            {
                bool on = mark.Index == i || (spot == PeekSpot.Bank && mark.Index == BankRules.Both);
                if (!on) continue;
                var tone = new StyleColor((UnityEngine.Color)color);
                var t = tiles[i];
                t.AddToClassList("sn-room-peek-on");
                t.style.borderLeftColor = t.style.borderRightColor = t.style.borderTopColor = t.style.borderBottomColor = tone;
                if (!mark.Press) continue;
                t.AddToClassList("sn-room-peek-press");
                t.style.backgroundColor = tone;
                var ink = new StyleColor((UnityEngine.Color)SeatColors.Ink(color));
                t.Query<Label>().ForEach(l => l.style.color = ink);
            }
        }

        VisualElement Tile(List<VisualElement> tiles)
        {
            var tile = Add(peek, new VisualElement { pickingMode = PickingMode.Ignore }, "room-peek-opt-" + (tiles.Count + 1).ToString(CultureInfo.InvariantCulture), "sn-room-peek-opt");
            tiles.Add(tile);
            return tile;
        }

        // 은행상회 덱 맨 위 거래 카드의 줄(공개 — rulings A4). 없으면 null
        IReadOnlyList<CardTrade> BankRows(GameState view)
        {
            int card = BankRules.Card(view);
            var cards = flow.Ctx.Decks.Cards;
            var t = card >= 0 && card < cards.Count ? cards[card].Trades : null;
            return t != null && t.Count > 0 ? t : null;
        }

        /// <summary>그 후보 칸(1부터)이 그 좌석 색으로 칠해졌나(가리킴 = 테두리 · 누름 = 바탕) — 시험 · 확인.</summary>
        public bool PeekMarked(int n, bool press = false) => peek != null && peek.Q("room-peek-opt-" + n.ToString(CultureInfo.InvariantCulture)) is VisualElement t && t.ClassListContains(press ? "sn-room-peek-press" : "sn-room-peek-on");
    }
}
