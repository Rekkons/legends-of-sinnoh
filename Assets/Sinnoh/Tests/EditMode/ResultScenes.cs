using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M5 「맵 결과·배틀 안 연출」의 장면(04 §8-8 23·24·26~30·46·74~81행의 연출 계약과 박자 시험이 쓴다): 실제 흐름에서 숨겨진 장소를 공개하고 · 전설 장소·게이트·신 토큰을 찾아보고 ·
    /// 알파 스트라이크·비스트 부스트·폼 기술을 쓰고 · 트레이너 배틀의 보상을 받는다.
    /// </summary>
    static class ResultScenes
    {
        static ChoiceAnswer A(AnswerKind k, int target = -1) => ChoiceAnswer.Of(k, target);

        /// <summary>26~30행: 뒷면 숨겨진 장소 하나를 key로 바꾸고 이웃 칸에서 걸어 들어가 공개한다(공개 답의 이벤트). tune = 공개 전에 판을 손본다.</summary>
        public static List<GameEvent> Spot(Ctx ctx, string key, Action<GameState> tune = null)
        {
            var s = Flow.Started(ctx, 2, 12);
            foreach (var t in s.Tokens)
            {
                if (t.Kind != TokenKind.HiddenSpot || t.Cell < 0 || t.FaceUp) continue;
                int n = Flow.StepInto(s, ctx, t.Cell);
                if (n < 0) continue;
                t.Def = Scene.Item(ctx, key);
                var st = s.Seats[s.ActiveSeat];
                st.Cell = n;
                st.MovesLeft = 3;
                tune?.Invoke(s);
                Flow.Answer(s, ctx, ChoiceKind.MapAction, A(AnswerKind.Move, t.Cell));
                var log = new EventLog();
                Flow.Answer(s, ctx, ChoiceKind.HiddenSpot, A(AnswerKind.Accept), log);
                return log.Events;
            }
            Assert.Fail("들어갈 수 있는 숨겨진 장소가 없다");
            return null;
        }

        static int Fixture(Ctx ctx, string key)
        {
            int f = ctx.Fixtures.Ids.IndexOf(key);
            Assert.GreaterOrEqual(f, 0, key);
            return f;
        }

        /// <summary>실제 판(2인)의 차례 좌석을 그 칸에 세운다. lead = 파티 맨 앞에 세울 포켓몬.</summary>
        static GameState At(Ctx ctx, int cell, string lead)
        {
            var s = Flow.Started(ctx, 2, 12);
            var st = s.Seats[s.ActiveSeat];
            st.Cell = cell;
            st.OpenSlots = 6;
            if (lead != null)
            {
                int id = Scene.Give(s, ctx, s.ActiveSeat, lead);
                int at = PokemonRules.PartyIndexOf(st, id);
                st.Party[at] = st.Party[0];
                st.Party[0] = id;
            }
            return s;
        }

        /// <summary>배틀(과 배틀 뒤 선택)이 끝날 때까지 기본 답으로 돈다 — 매 선택 전에 내 파티를 회복하고 출전한 야생의 체력을 1로(이긴다).</summary>
        static void Win(Ctx ctx, GameState s, int seat, EventLog log)
        {
            for (int i = 0; i < 600; i++)
            {
                var c = s.Pending.Find(x => !x.Answered && (s.Battle != null || x.Window == Window.BattleEnd));
                if (c == null) return;
                foreach (int p in s.Seats[seat].Party)
                    if (p >= 0)
                    {
                        s.Pokemon[p].Damage = 0;
                        s.Pokemon[p].Fatigue = 0;
                        StatusRules.Clear(s.Pokemon[p]);
                    }
                int w = s.Battle != null ? s.Battle.Sides[1].Active : -1;
                if (w >= 0) s.Pokemon[w].Damage = PokemonRules.MaxHp(ctx, s.Pokemon[w]) - 1;
                Battles.Answer(s, ctx, c, c.Default.Clone(), log);
            }
            Assert.Fail("배틀이 끝나지 않는다");
        }

        static ChoiceAnswer Search(GameState s, Ctx ctx, int fixture)
        {
            var a = Flow.MapLegal(s, ctx).Find(x => x.Kind == AnswerKind.Search && x.Arg == fixture);
            Assert.IsNotNull(a, "찾아보기 답이 없다");
            return a;
        }

        /// <summary>23행: 비밀키를 가진 좌석이 전설 장소(그랜드 샤토)에서 찾아보기 → 전설 배틀 시작.</summary>
        public static List<GameEvent> LegendSite(Ctx ctx)
        {
            int site = Fixture(ctx, "legendary_site.grand_chateau");
            var s = At(ctx, ctx.Fixtures.Fixtures[site].Cell, null);
            s.Seats[s.ActiveSeat].Bag.Add(Scene.Item(ctx, "item.secret_key"));
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, Search(s, ctx, site), log);
            return log.Events;
        }

        /// <summary>24·81행: 왜곡 게이트에서 찾아보기 → 무리 배틀 셋을 모두 이긴다(JP 7).</summary>
        public static List<GameEvent> Swarm(Ctx ctx)
        {
            int gate = Fixture(ctx, "gate.deepmarsh_main");
            var s = At(ctx, ctx.Fixtures.Fixtures[gate].Cell, "242:0");
            int seat = s.ActiveSeat;
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, Search(s, ctx, gate), log);
            Win(ctx, s, seat, log);
            return log.Events;
        }

        /// <summary>78·80행: 시작의 사당의 신 토큰(기라티나)과 싸워 이긴다 — 천상의 오라라 둘째 폼이 나오고, 이기면 토큰이 빠지고 한 턴 더.</summary>
        public static List<GameEvent> God(Ctx ctx)
        {
            var s = At(ctx, HosFlow.OriginShrineCell(ctx), "706:0");
            int seat = s.ActiveSeat;
            s.Tokens.Add(new TokenState { Kind = TokenKind.God, Def = Scene.Def(ctx, "487:1"), Cell = HosFlow.OriginShrineCell(ctx), FaceUp = true });
            var log = new EventLog();
            Flow.Answer(s, ctx, ChoiceKind.MapAction, Search(s, ctx, HosFlow.OriginShrine(ctx)), log);
            Win(ctx, s, seat, log);
            return log.Events;
        }

        /// <summary>트레이너 배틀: 좌석 0(a) 대 좌석 1(d + dBench) — 좌석 0의 기술 고르기가 열려 있다.</summary>
        static GameState Duel(Ctx ctx, EventLog log, string a, string d, bool legend, out int mine, out int foe, params string[] dBench)
        {
            var s = Battles.Game(ctx, 2);
            mine = Scene.Give(s, ctx, 0, a);
            s.Pokemon[mine].HasLegendaryMove = legend;
            foe = Scene.Give(s, ctx, 1, d);
            foreach (var k in dBench) Scene.Give(s, ctx, 1, k);
            Battles.Start(s, ctx, log, BattleKind.Trainer, 0, 1, -1);
            for (int guard = 0; guard < 6 && s.Pending.Count == 1; guard++)
            {
                var c = s.Pending[0];
                if (c.Kind == ChoiceKind.SendIn) Battles.Answer(s, ctx, c, ChoiceAnswer.Pick(c.Seat == 0 ? mine : foe), log);
                else if (c.Sub == ChoiceSub.InitiativeToken) Battles.Answer(s, ctx, c, ChoiceAnswer.Of(AnswerKind.Accept), log);
                else break;
            }
            Assert.AreEqual(0, Battles.Only(s, ChoiceKind.BattleAction).Seat, "좌석 0의 차례");
            return s;
        }

        /// <summary>76행: 전설 특수기술을 가진 로토무가 알파 스트라이크(에스퍼)를 쓴다.</summary>
        public static List<GameEvent> AlphaStrike(Ctx ctx)
        {
            var log = new EventLog();
            var s = Duel(ctx, log, "479:1", "242:0", true, out _, out _);
            log.Clear();
            Battles.Answer(s, ctx, s.Pending[0], ChoiceRules.AlphaStrikeAnswer((int)PokemonType.Psychic), log);
            return log.Events;
        }

        /// <summary>76행 비스트 부스트: 로토무(피로 3)가 체력 1 잉어킹을 쓰러뜨린다 — 맞는 시드를 찾는다.</summary>
        public static List<GameEvent> BeastBoost(Ctx ctx)
        {
            List<GameEvent> Try(ulong seed)
            {
                var log = new EventLog();
                var s = Duel(ctx, log, "479:1", "129:0", true, out int mine, out int foe, "242:0");
                Battles.SetHp(s, ctx, foe, 1);
                s.Pokemon[mine].Fatigue = 3;
                s.Rng = new Pcg32(seed);
                log.Clear();
                Battles.Answer(s, ctx, s.Pending[0], Battles.Move(ctx.Cards.Pokemon[Scene.Def(ctx, "479:1")].Signature), log);
                return log.Events;
            }
            ulong found = Battles.Seed(seed => Try(seed).Exists(e => e is FatigueChanged f && f.Cause == FatigueCause.BeastBoost));
            return Try(found);
        }

        /// <summary>79행: 체리꼬(네거폼)가 시그니처 gather_sunlight로 포지폼이 된다.</summary>
        public static List<GameEvent> Form(Ctx ctx)
        {
            var log = new EventLog();
            var s = Duel(ctx, log, "421:1", "242:0", false, out _, out _);
            log.Clear();
            Battles.Answer(s, ctx, s.Pending[0], Battles.Move(Scene.Move(ctx, "gather_sunlight")), log);
            return log.Events;
        }

        /// <summary>46행: 좌석 0이 좌석 1(가방에 비밀키 · 운명 3장)을 트레이너 배틀로 이기고 보상을 고른다(relic = 유물, 아니면 운명 2장). ask = 보상 선택이 열린 채로 돌려준다(답하지 않는다).</summary>
        public static List<GameEvent> Spoils(Ctx ctx, bool relic, out GameState state, bool ask = false)
        {
            GameState Play(ulong seed, EventLog log)
            {
                var s = Battles.Game(ctx, 2, seed);
                s.Phase = Phase.Turn;
                s.ActiveSeat = 0;
                Scene.Give(s, ctx, 0, "242:0");
                Battles.SetHp(s, ctx, Scene.Give(s, ctx, 1, "387:0"), 1);
                s.Seats[1].Bag.Add(Scene.Item(ctx, "item.secret_key"));
                s.Seats[1].FateHand.AddRange(Scene.Top(s, DeckId.ChangeOfFate, 3));
                foreach (int c in s.Seats[1].FateHand) s.Decks[(int)DeckId.ChangeOfFate].Remove(c);
                Battles.Start(s, ctx, log, BattleKind.Trainer, 0, 1, -1);
                for (int i = 0; i < 300 && s.Battle != null; i++)
                {
                    var c = s.Pending.Find(x => !x.Answered);
                    Battles.Answer(s, ctx, c, c.Default.Clone(), log);
                }
                return s;
            }
            ulong found = Battles.Seed(seed => Play(seed, new EventLog()).Pending.Exists(x => x.Kind == ChoiceKind.Reward && x.Sub == ChoiceSub.TrainerReward && x.Seat == 0));
            var all = new EventLog();
            state = Play(found, all);
            if (ask) return all.Events;
            var reward = state.Pending.Find(x => x.Kind == ChoiceKind.Reward && x.Sub == ChoiceSub.TrainerReward);
            var legal = Scene.Legal(state, ctx, reward);
            var pick = legal.Find(a => relic ? a.Kind == AnswerKind.Pick : a.Kind == AnswerKind.Accept);
            Assert.IsNotNull(pick, "보상 답이 없다");
            Battles.Answer(state, ctx, reward, pick, all);
            return all.Events;
        }

        /// <summary>74행 옮기기: 응징 2가 쌓인 조무래기 배틀 — 갤럭시단이 응징을 옮길 때까지 기본 답으로 돈다(양쪽 모두 쓰러지지 않게 체력을 채운다).</summary>
        public static List<GameEvent> Retribution(Ctx ctx)
        {
            var s = Battles.Game(ctx, 2);
            int mine = Scene.Give(s, ctx, 0, "390:0");
            int g0 = Scene.Spawn(s, ctx, "436:0@male_grunt", SeatId.Galactic), g1 = Scene.Spawn(s, ctx, "41:0@female_grunt", SeatId.Galactic);
            var log = new EventLog();
            Battles.Start(s, ctx, log, BattleKind.Galactic, 0, SeatId.Galactic, -1, g0, g1);
            s.Battle.Sides[1].Retribution = 2;
            for (int i = 0; i < 80 && s.Battle != null && !log.Events.Exists(e => e is RetributionMoved); i++)
            {
                var c = s.Pending.Find(x => !x.Answered);
                if (c == null) break;
                foreach (int p in new[] { mine, g0, g1 })
                    if (s.Pokemon[p].Def >= 0) { s.Pokemon[p].Damage = 0; s.Pokemon[p].Fatigue = 0; }
                Battles.Answer(s, ctx, c, c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.Retribution ? ChoiceAnswer.Of(AnswerKind.Accept) : c.Default.Clone(), log);
            }
            return log.Events;
        }

        /// <summary>75행: 조무래기의 주뱃이 체력 1인 내 모부기를 쓰러뜨린다 — 갤럭시단은 쓰러뜨린 직후 교체하지 못한다(A16).</summary>
        public static List<GameEvent> Locked(Ctx ctx)
        {
            var s = Battles.Game(ctx, 2, 7, new RuleOptions { GalacticNoSwitchAfterKo = true });
            int mine = Scene.Give(s, ctx, 0, "387:0");
            Scene.Give(s, ctx, 0, "399:0");
            Battles.SetHp(s, ctx, mine, 1);
            int g0 = Scene.Spawn(s, ctx, "41:0@female_grunt", SeatId.Galactic), g1 = Scene.Spawn(s, ctx, "436:0@male_grunt", SeatId.Galactic);
            var log = new EventLog();
            Battles.Start(s, ctx, log, BattleKind.Galactic, 0, SeatId.Galactic, -1, g0, g1);
            for (int i = 0; i < 40 && s.Battle != null && !log.Events.Exists(e => e is SwitchLocked); i++)
            {
                var c = s.Pending.Find(x => !x.Answered);
                if (c == null) break;
                if (c.Kind == ChoiceKind.SendIn && c.Seat == 0) Battles.Answer(s, ctx, c, ChoiceAnswer.Pick(mine), log);
                else Battles.Answer(s, ctx, c, c.Default.Clone(), log);
                if (s.Battle != null && s.Pokemon[mine].Def >= 0 && PokemonRules.Hp(ctx, s.Pokemon[mine]) > 1) Battles.SetHp(s, ctx, mine, 1);
            }
            return log.Events;
        }
    }
}
