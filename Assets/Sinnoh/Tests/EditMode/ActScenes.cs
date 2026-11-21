using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 2막 이후·종막 장면(연출 계약 94~97행 · 막·종막 화면 시험이 같이 쓴다 — <c>ActTests</c>의 장면과 같은 꼴, M5 「막·종막 화면」).
    /// 이기는 배틀은 상대 명단의 체력을 1로 고치고 돈다. 시드는 그 장면이 이기는 첫 시드를 찾는다.
    /// </summary>
    static class ActScenes
    {
        /// <summary>이기는 장면에 쓰는 포켓몬(체력 36) — 체력을 1로 고친 상대 명단을 끝까지 버틴다.</summary>
        public const string Sturdy = "800:4";

        static readonly ChoiceAnswer ChallengeGalactic = ChoiceAnswer.Of(AnswerKind.Challenge, SeatId.Galactic);

        /// <summary>막 카드를 act막까지 공개한 장면 게임(좌석은 고향 마을, 차례 좌석 0).</summary>
        public static GameState Game(Ctx ctx, int seats, int act, ulong seed = 7)
        {
            var s = Battles.Game(ctx, seats, seed);
            var fx = Scene.Fx(s, null);
            while (s.Hos.Act < act) SetupFlow.RevealAct(s, ctx, fx, -1);
            return s;
        }

        /// <summary>좌석을 그 칸에 놓고(이동력 1) 맵 행동을 연다.</summary>
        public static void Stand(GameState s, Ctx ctx, int seat, int cell, IEventSink log = null)
        {
            var st = s.Seats[seat];
            if (st.Party[0] < 0) Scene.Give(s, ctx, seat, "387:0");
            st.Cell = cell;
            st.MovesLeft = 1;
            s.Pending.Clear();
            s.ActiveSeat = seat;
            TurnFlow.OpenMapAction(s, ctx, Scene.Fx(s, log, seat), seat);
        }

        /// <summary>좌석이 1막을 끝내고 조각 held개(앞에서부터)를 가진 것으로.</summary>
        public static void Progress(GameState s, int seat, int held, bool heart = false)
        {
            var st = s.Seats[seat];
            st.ActProgress = held == SeatState.ShardCount ? 2 : 1;
            for (int i = 0; i < held; i++) st.Shards[i] = true;
            st.OpenSlots = CampaignRules.OpenPartySlots(s.Campaign, held);
            st.Heart = heart;
        }

        /// <summary>지금 칸의 간부·태홍에게 도전해 명단(Default)을 받고, 상대 명단 체력을 1로 고쳐 끝까지 돈다.</summary>
        public static void Win(GameState s, Ctx ctx, ulong seed, EventLog log)
        {
            s.Rng = new Pcg32(seed);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, ChallengeGalactic, log);
            var lineup = Battles.Only(s, ChoiceKind.Lineup);
            Battles.Answer(s, ctx, lineup, lineup.Default.Clone(), log);
            foreach (int p in s.Battle.Sides[1].Roster) Battles.SetHp(s, ctx, p, 1);
            Battles.Drive(s, ctx, log);
        }

        /// <summary>좌석 0이 shard 호수에서 조각 held개를 가진 채 이긴 판(2인).</summary>
        public static GameState Lake(Ctx ctx, EventLog log, Shard shard, int held)
        {
            GameState Play(ulong sd, EventLog l)
            {
                var s = Game(ctx, 2, 2);
                Progress(s, 0, held);
                Scene.Give(s, ctx, 0, Sturdy);
                Stand(s, ctx, 0, HosFlow.LakeCell(ctx, shard), l);
                Win(s, ctx, sd, l);
                return s;
            }
            ulong seed = Battles.Seed(sd => Play(sd, new EventLog()).Seats[0].Shards[(int)shard], 200);
            return Play(seed, log);
        }

        /// <summary>좌석 0이 심장을 들고 신오신전에서 태홍을 이겨 종막 선택이 열린 판.</summary>
        public static GameState Ending(Ctx ctx, int seats, EventLog log)
        {
            GameState Play(ulong sd, EventLog l)
            {
                var s = Game(ctx, seats, 3);
                Progress(s, 0, 3, true);
                Scene.Give(s, ctx, 0, Sturdy);
                for (int i = 1; i < seats; i++) Scene.Give(s, ctx, i, "393:0");
                Stand(s, ctx, 0, HosFlow.TempleCell(ctx), l);
                Win(s, ctx, sd, l);
                return s;
            }
            ulong seed = Battles.Seed(sd => Play(sd, new EventLog()).Seats[0].ActProgress == 3, 200);
            return Play(seed, log);
        }

        /// <summary>좌석 0이 3막 시작의 사당에서 공물을 바친 판(빨강 규토리 1개).</summary>
        public static GameState Heart(Ctx ctx, EventLog log)
        {
            var s = Game(ctx, 2, 3);
            Progress(s, 0, 3);
            s.Seats[0].Apricorns[(int)ApricornColor.Red] = 1;
            Stand(s, ctx, 0, HosFlow.OriginShrineCell(ctx));
            var offer = Flow.MapLegal(s, ctx).Find(a => a.Kind == AnswerKind.Offer && a.Values.Count == 0);
            if (offer == null) throw new InvalidOperationException("공물 답이 없다");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, offer, log);
            return s;
        }
    }
}
