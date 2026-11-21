using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M6 그림 에셋의 UI 캡처 장면: 트레이너 카드 전용 아이템 아이콘(rulings G1 — 정비 판의 가방 · 지닌물건 칸 · 트레이너 카드 판)과 왜곡 균열 카드 그림(막힌 기술 칸 — 정비 판 · 카드 확대).
    /// 판은 실제 진행의 첫 턴(스타팅 포켓몬 하나)을 손봐 연다: 전용 아이템 열하나를 쥐여 주고 · 대타출동은 가방에 · 드래곤의 정점은 스타팅 포켓몬이 지니고 · 기술 칸 2를 균열 카드(신속한 공격)가 막았다.
    /// 규칙으로는 한 판에 다 모일 수 없는 견본이다(카드 하나가 주는 아이템은 하나 둘) — 그림이 칸에 들어가는지만 본다. 글자 넘침은 <c>Fit</c>이 단정한다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] AssetScreens =
        {
            Screen("board-party-m6items", Fit(s => { if (!M6Kit(s).OpenParty()) throw new InvalidOperationException("정비 판이 열리지 않았다"); })),
            Screen("mon-detail-breach", Fit(s =>
            {
                var flow = M6Kit(s);
                if (!flow.OpenParty() || !flow.OpenMon(flow.Client.View.Seats[0].Party[0], 0)) throw new InvalidOperationException("카드 확대가 열리지 않았다");
            })),
            Screen("board-trainer-items", Fit(s => { if (!M6Kit(s).OpenTrainer()) throw new InvalidOperationException("트레이너 카드 판이 열리지 않았다"); })),
        };

        // 첫 턴 판에 전용 아이템 열하나 · 가방의 대타출동 · 지닌 드래곤의 정점 · 균열 카드가 막은 기술 칸 2를 쥐여 준다
        static GameFlow M6Kit(UiShot s)
        {
            var flow = ToBoard(s, BoardReadyMs);
            var ctx = flow.Ctx;
            flow.Rebase(st =>
            {
                var seat = st.Seats[0];
                for (int i = 0; i < ctx.Items.Items.Count; i++)
                    if (ctx.Items.Items[i].Kind == ItemKind.TrainerItem && ctx.Items.Items[i].Key != "item.apex_dragon") seat.CardItems.Add(i);
                seat.Bag.Add(ctx.Items.Ids.IndexOf("item.substitute"));
                var mon = st.Pokemon[seat.Party[0]];
                mon.HeldItem = ctx.Items.Ids.IndexOf("item.apex_dragon");
                mon.Moves[1] = -1;
                mon.Breach[1] = BreachRules.CardOf(ctx, BreachKind.RapidStrikes);
            });
            flow.Stage.Tweens.Advance(BoardReadyMs);
            flow.Layout(s.Width, s.Height);
            return flow;
        }
    }
}
