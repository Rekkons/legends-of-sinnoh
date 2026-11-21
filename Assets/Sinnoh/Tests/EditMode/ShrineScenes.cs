using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「사당·균열 화면」의 장면(04 §8-8 31~34행 — 연출 계약 · 박자 시험이 같이 쓴다): 턴인 좌석이 풀밭 사당에 서서(찾아 둔 판과 같은 손보기 <see cref="SoloTrail.PrepareShrine(Ctx, GameState, int, string[])"/>)
    /// 분홍 규토리(2단계)를 바친다. 사당 이벤트 덱 맨 위 카드를 장면마다 정한다.
    /// </summary>
    public static class ShrineScenes
    {
        public const ulong Seed = 32;

        /// <summary>seats 좌석 판의 첫 턴에 공물을 바친다(top = 덱 맨 위부터, extra = 여행 점수 7을 더 내고 3장).</summary>
        public static GameState Offer(Ctx ctx, EventLog log, int seats, bool extra, params string[] top)
        {
            var s = Flow.Started(ctx, seats, Seed);
            int seat = s.ActiveSeat;
            SoloTrail.PrepareShrine(ctx, s, seat, top);
            int shrine = ShrineRules.ShrineAt(s, s.Seats[seat].Cell);
            var a = ChoiceAnswer.Of(AnswerKind.Offer, shrine, (int)ApricornColor.Pink);
            if (extra) a.Values.Add(ShrineRules.ExtraCost);
            Flow.Answer(s, ctx, ChoiceKind.MapAction, a, log);
            return s;
        }

        /// <summary>31·32행: 평온한 순간(2단계 = 여행 점수 +4) — 고를 것 없이 곧바로 축복.</summary>
        public static GameState Tranquil(Ctx ctx, EventLog log) => Offer(ctx, log, 2, false, ShrineRules.TranquilMoment);

        /// <summary>33행: 서리의 노래 — 모두 파랑 규토리 3개 · 프리져 토큰이 서리노래 사당에.</summary>
        public static GameState Legend(Ctx ctx, EventLog log) => Offer(ctx, log, 2, false, "shrine_event.song_of_frost");

        /// <summary>34행: 천상의 균열 — 모든 좌석이 펄기아를 따라 펄기아가 강림한다.</summary>
        public static GameState Fracture(Ctx ctx, EventLog log, int seats = 2)
        {
            var s = Offer(ctx, log, seats, false, ShrineRules.CelestialFracture);
            var group = s.Pending.FindAll(c => c.Kind == ChoiceKind.Simultaneous && c.Sub == ChoiceSub.Follower);
            Assert.AreEqual(seats, group.Count, "모든 좌석에게 묻는다");
            foreach (var c in new List<PendingChoice>(group))
                Scene.Accept(Engine.Submit(s, ctx, c.Seat, Command.AnswerTo(c.Id, ChoiceAnswer.Pick((int)SinnohGod.Palkia)), log), "좌석 " + c.Seat + " 펄기아");
            return s;
        }
    }
}
