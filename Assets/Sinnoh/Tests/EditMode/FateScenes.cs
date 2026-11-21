using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 운명의 변화 장면(M5 「운명·응답 화면」 — 04 §8-8 7·14·42~45행의 연출 계약이 쓴다): 실제 흐름(맵 행동 · 트레이너전)에서 카드를 받고·버리고·낸다.
    /// </summary>
    static class FateScenes
    {
        static void Hand(Ctx ctx, GameState s, int seat, params FateCard[] cards)
        {
            s.Seats[seat].FateHand.Clear();
            foreach (var c in cards) s.Seats[seat].FateHand.Add(FateRules.CardKey(ctx, c));
        }

        static void EndTurn(GameState s, Ctx ctx, EventLog log) => Flow.Answer(s, ctx, ChoiceKind.MapAction, ChoiceAnswer.Of(AnswerKind.EndTurn), log);

        /// <summary>7행: 떨어진 별 4개 — 다음 좌석의 턴 시작에 1장.</summary>
        public static void Star(Ctx ctx, EventLog log)
        {
            var s = Flow.Started(ctx, 2, 32);
            s.FallenStars = 4;
            EndTurn(s, ctx, log);
        }

        /// <summary>42행: 손패 5장인 다음 좌석이 떨어진 별로 6장째를 받고 → 한 장을 버린다.</summary>
        public static void Discard(Ctx ctx, EventLog log)
        {
            var s = Flow.Started(ctx, 2, 5);
            int next = 1 - s.ActiveSeat;
            Hand(ctx, s, next, FateCard.Dialga, FateCard.Dialga, FateCard.Palkia, FateCard.Giratina, FateCard.Arceus);
            s.FallenStars = 4;
            EndTurn(s, ctx, log);
            Flow.Answer(s, ctx, ChoiceKind.Discard, ChoiceAnswer.Pick(FateRules.CardKey(ctx, FateCard.Giratina)), log);
        }

        /// <summary>14행: 내 턴에 펄기아 1장(첫 합법 칸).</summary>
        public static void Teleport(Ctx ctx, EventLog log)
        {
            var s = Flow.Started(ctx, 2, 32);
            Hand(ctx, s, s.ActiveSeat, FateCard.Palkia);
            var ask = Flow.Only(s, ChoiceKind.MapAction);
            var play = Scene.Legal(s, ctx, ask).Find(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Palkia && a.Arg == 1);
            Assert.IsNotNull(play, "펄기아 1장 답이 없다");
            Flow.Answer(s, ctx, ChoiceKind.MapAction, play, log);
        }

        /// <summary>트레이너전(좌석 0 해피너스 대 좌석 1 악식킹): 좌석 0의 손패 hand · slash를 고른 뒤 굴리기 전 창까지.</summary>
        static GameState Duel(Ctx ctx, EventLog log, params FateCard[] hand)
        {
            var s = Battles.Game(ctx, 2);
            int a = Scene.Give(s, ctx, 0, "242:0");
            Scene.Give(s, ctx, 1, "799:0");
            Battles.Start(s, ctx, log, BattleKind.Trainer, 0, 1, -1);
            if (s.Pending.Count == 1 && s.Pending[0].Sub == ChoiceSub.InitiativeToken) Battles.Answer(s, ctx, s.Pending[0], ChoiceAnswer.Of(AnswerKind.Accept), log);
            s.Pokemon[a].Moves[0] = Scene.Move(ctx, "slash");
            Hand(ctx, s, 0, hand);
            Battles.Answer(s, ctx, ChoiceKind.BattleAction, Battles.Move(Scene.Move(ctx, "slash")), log);
            return s;
        }

        static PendingChoice FateAsk(GameState s, Window w) => s.Pending.Find(x => x.Kind == ChoiceKind.Reaction && x.Sub == ChoiceSub.Fate && x.Seat == 0 && x.Window == w && !x.Answered);

        /// <summary>그 창(좌석 0)에 pick이 고른 답을 내고, 다른 좌석의 같은 창은 「안 씀」.</summary>
        static void Answer(Ctx ctx, GameState s, EventLog log, Window w, System.Predicate<ChoiceAnswer> pick)
        {
            for (var o = s.Pending.Find(c => c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.Fate && c.Window == w && c.Seat != 0 && !c.Answered); o != null;
                 o = s.Pending.Find(c => c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.Fate && c.Window == w && c.Seat != 0 && !c.Answered))
                Battles.Answer(s, ctx, o, ChoiceAnswer.Of(AnswerKind.Decline), log);
            var ask = FateAsk(s, w);
            Assert.IsNotNull(ask, $"좌석 0의 {w} 운명 창이 없다");
            var a = pick == null ? ChoiceAnswer.Of(AnswerKind.Decline) : Scene.Legal(s, ctx, ask).Find(pick);
            Assert.IsNotNull(a, $"{w}: 고를 답이 없다");
            Battles.Answer(s, ctx, ask, a, log);
        }

        /// <summary>43행: 디아루가 1장으로 첫 주사위를 다시 굴린다.</summary>
        public static void Reroll(Ctx ctx, EventLog log)
        {
            var s = Duel(ctx, log, FateCard.Dialga);
            Answer(ctx, s, log, Window.BeforeRoll, null);
            Answer(ctx, s, log, Window.AfterRoll, a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Dialga && a.Arg == 1);
        }

        /// <summary>44행: 기라티나 1장(굴리기 전 창).</summary>
        public static void Sacrifice(Ctx ctx, EventLog log)
        {
            var s = Duel(ctx, log, FateCard.Giratina);
            Answer(ctx, s, log, Window.BeforeRoll, a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Giratina && a.Arg == 1);
        }

        /// <summary>45행: 디아루가 3장(디아루가 둘 + 아르세우스 와일드카드)으로 주사위 모두를 다시 굴린다.</summary>
        public static void Play3(Ctx ctx, EventLog log)
        {
            var s = Duel(ctx, log, FateCard.Dialga, FateCard.Dialga, FateCard.Arceus);
            Answer(ctx, s, log, Window.BeforeRoll, null);
            Answer(ctx, s, log, Window.AfterRoll, a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Dialga && a.Arg == FateRules.Play3Count && FateRules.WildsOf(a) < 0);
        }
    }
}
