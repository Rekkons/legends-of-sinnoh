using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 장면 빌더(01 §7-4 ③, §12 「규칙 단위」). 셋업·턴 흐름(M3-d)을 거치지 않고 시험할 자리를 바로 만든다: 좌석 파티, 배틀, 열린 선택.
    /// 실제 흐름이 생기면 같은 자리를 흐름으로도 만들어 비교한다.
    /// </summary>
    static class Scene
    {
        /// <summary>새 게임을 턴 단계로(라운드 1, 차례 좌석 0).</summary>
        public static GameState Game(Ctx ctx, int seats, ulong seed = 7, RuleOptions opt = null)
        {
            var s = Engine.NewGame(ctx, opt ?? new RuleOptions(), seats, seed);
            s.Phase = Phase.Turn;
            s.Round = 1;
            s.TurnId = 1;
            s.ActiveSeat = 0;
            return s;
        }

        public static int Def(Ctx ctx, string key) => ctx.Cards.PokemonIds.IndexOf(key);

        public static int Move(Ctx ctx, string key) => ctx.Cards.MoveIds.IndexOf(key);

        public static int Item(Ctx ctx, string key) => ctx.Items.Ids.IndexOf(key);

        /// <summary>좌석 파티 맨 뒤에 포켓몬을 넣는다(약한 덱에서 온 것으로).</summary>
        public static int Give(GameState s, Ctx ctx, int seat, string key)
        {
            int id = PokemonRules.Spawn(s, Def(ctx, key), seat, (int)DeckId.Weak);
            Assert.GreaterOrEqual(PokemonRules.AddToParty(s.Seats[seat], id), 0, "열린 파티 칸이 없다");
            return id;
        }

        /// <summary>좌석 밖 포켓몬(야생·갤럭시단).</summary>
        public static int Spawn(GameState s, Ctx ctx, string key, int owner) => PokemonRules.Spawn(s, Def(ctx, key), owner, (int)DeckId.Weak);

        /// <summary>
        /// 배틀을 놓는다. Side 0 = 좌석 seat0(파티 전부, 첫 포켓몬 출전), Side 1 = owner1(좌석이면 그 파티, 아니면 roster1). 기술 고르기 단계, 배틀 턴 Side 0.
        /// </summary>
        public static BattleState Battle(GameState s, BattleKind kind, int seat0, int owner1, params int[] roster1)
        {
            var b = new BattleState { Id = s.NextBattleId++, Kind = kind, Step = BattleStep.SelectAction, Turn = 1, ActiveSide = 0 };
            SetSide(s, b.Sides[0], seat0, null);
            SetSide(s, b.Sides[1], owner1, roster1);
            s.Battle = b;
            s.Phase = Phase.Battle;
            return b;
        }

        static void SetSide(GameState s, BattleSide side, int owner, int[] roster)
        {
            side.Owner = owner;
            side.Responder = owner;
            if (owner >= 0)
            {
                foreach (int p in s.Seats[owner].Party)
                    if (p >= 0) side.Roster.Add(p);
            }
            else side.Roster.AddRange(roster);
            side.Active = side.Roster[0];
        }

        /// <summary>시험용 행동을 열어 둔 Emitter(선택을 여는 「앞 행동」).</summary>
        public static Emitter Fx(GameState s, IEventSink sink, int seat = SeatId.System, ActionKind kind = ActionKind.TurnStart)
        {
            var fx = new Emitter(s, sink);
            fx.Begin(seat, kind, AutoKind.Auto);
            return fx;
        }

        public static PendingChoice Open(GameState s, Ctx ctx, IEventSink sink, PendingChoice c)
        {
            ChoiceRules.Open(s, ctx, Fx(s, sink), c);
            return c;
        }

        public static List<ChoiceAnswer> Legal(GameState s, Ctx ctx, PendingChoice c)
        {
            var l = new List<ChoiceAnswer>();
            Engine.Legal(s, ctx, c, l);
            return l;
        }

        public static void Accept(SubmitResult r, string what = "")
        {
            Assert.IsTrue(r.Accepted, $"{what} {r}");
        }

        public static void Reject(SubmitResult r, RejectReason reason, string what = "")
        {
            Assert.IsFalse(r.Accepted, $"{what}: 받아들였다");
            Assert.AreEqual(reason, r.Reason, what);
        }

        /// <summary>덱 맨 위에서 n장(빼지 않고 본다).</summary>
        public static List<int> Top(GameState s, DeckId deck, int n)
        {
            var pile = s.Decks[(int)deck];
            var r = new List<int>();
            for (int i = 0; i < n; i++) r.Add(pile[pile.Count - 1 - i]);
            return r;
        }
    }
}
