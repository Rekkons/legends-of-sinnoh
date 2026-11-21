using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 게임이 끝난 까닭(GameEnded.Reason, S16_3). Hero = 영웅을 골랐다, TraitorWon = 배신자가 모두 이겼다, TraitorDefeated = 배신자를 이긴 좌석이 나왔다.
    /// BreachLost = 울트라 버스트의 균열이 70% 이상(R8 — 모두 패배, Winners 빈 배열. UbTurn).
    /// NecrozmaDefeated = 네크로즈마 3페이즈 격파(모두 승리 — Winners = 모든 좌석) · AllEliminated = 모두 탈락했고 축복이 없다(모두 패배, UbNecrozma — rulings A54).
    /// </summary>
    public enum GameEndReason { Hero, TraitorWon, TraitorDefeated, BreachLost, NecrozmaDefeated, AllEliminated }

    /// <summary>
    /// 신오의 심장 2막·3막·종막(01 §8-1, rulings G17, M5 「막 뼈대」). 1막(조무래기·해방)은 BattleFlow·SetupFlow에 있다.
    /// 자리(룰북 p.21·22, fixtures.json): 진실호수 = 마스 = 감정의 조각, 예지호수 = 주피터 = 지식의 조각, 입지호수 = 새턴 = 의지의 조각(호수 칸 = 그 호수 동굴 고정물의 칸),
    /// 시작의 사당 = 심장 회복, 신오신전 = 태홍.
    /// 흐름: 호수에서 도전(MapAction Challenge, 대상 갤럭시단) → 조종자가 명단을 고른다(Lineup: 간부 5마리 중 조각 수 + 2, 태홍 8마리 중 6 — A16) → 명단 공개(GalacticBattle) →
    /// 도전자가 첫 포켓몬(원작 커뮤니티 확인) → 배틀(BattleFlow, BattleKind.Galactic). 이기면 <see cref="GalacticWon"/>.
    /// 종막: 태홍을 이긴 좌석이 영웅·배신자를 고른다(Ending). 영웅 = 즉시 승리. 배신자 = 턴이 멈추고 모두 회복 → 고른 순서로 연전(BattleKind.Traitor, 배신자가 도전자) →
    /// 이길 때마다 배신자 회복 + 쓴 운명 카드 반환(<see cref="ReturnUsedFate"/> — 운명 묶음이 채운다), 지거나 비기면 그 좌석이 승리, 모두 이기면 배신자 승리.
    /// </summary>
    public static class HosFlow
    {
        /// <summary>호수 동굴 고정물 키[Shard](p.21: Emotion = Lake Verity, Knowledge = Lake Acuity, Willpower = Lake Valor).</summary>
        static readonly string[] LakeKeys =
        {
            "legendary_site.lake_verity_cavern", "legendary_site.lake_acuity_cavern", "legendary_site.lake_valor_cavern",
        };

        /// <summary>호수의 간부[Shard](p.21: Mars · Jupiter · Saturn).</summary>
        static readonly GalacticTrainer[] Commanders = { GalacticTrainer.Mars, GalacticTrainer.Jupiter, GalacticTrainer.Saturn };

        public const string OriginShrineKey = "legendary_shrine.origin_shrine";
        public const string TempleKey = "legendary_site.temple_of_sinnoh";
        public const string HeartItemKey = "item.heart_of_sinnoh";
        public const string ActTwoKey = "act.shards";
        public const string ActThreeKey = "act.heart";
        public const string EndingKey = "act.choice";

        /// <summary>태홍이 내는 포켓몬 수(p.22 「chooses 6 of his 8 Pokémon」).</summary>
        public const int BossLineup = 6;

        /// <summary>간부가 내는 포켓몬 수(p.21 「2/3/4 (increases for each Shard you already have)」).</summary>
        public static int CommanderLineup(int shards) => 2 + shards;

        public static GalacticTrainer CommanderOf(Shard shard) => Commanders[(int)shard];

        /// <summary>그 간부가 지키는 조각. 간부가 아니면 −1.</summary>
        public static int ShardOf(GalacticTrainer trainer) => Array.IndexOf(Commanders, trainer);

        public static int LakeCell(Ctx ctx, Shard shard) => ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf(LakeKeys[(int)shard])].Cell;

        /// <summary>그 칸이 호수면 그 호수의 조각(Shard 값), 아니면 −1.</summary>
        public static int LakeShard(Ctx ctx, int cell)
        {
            for (int i = 0; i < LakeKeys.Length; i++)
                if (LakeCell(ctx, (Shard)i) == cell) return i;
            return -1;
        }

        public static int OriginShrine(Ctx ctx) => ctx.Fixtures.Ids.IndexOf(OriginShrineKey);

        public static int OriginShrineCell(Ctx ctx) => ctx.Fixtures.Fixtures[OriginShrine(ctx)].Cell;

        public static int TempleCell(Ctx ctx) => ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf(TempleKey)].Cell;

        public static int ShardCount(SeatState st)
        {
            int n = 0;
            foreach (bool b in st.Shards)
                if (b) n++;
            return n;
        }

        /// <summary>
        /// 그 좌석이 지금 칸에서 도전할 수 있는 갤럭시단 간부·태홍(2막·3막). 없으면 None.
        /// 간부: 2막이 공개됐고, 1막을 끝냈고(p.21 「Other trainers still must finish Act I」), 그 호수의 조각이 아직 없다.
        /// 태홍: 3막이 공개됐고, 조각 셋을 모았고, 심장을 회복했고(p.22 「required to battle Cyrus」), 아직 이기지 않았다.
        /// </summary>
        public static GalacticTrainer LeaderAt(GameState s, Ctx ctx, int seat)
        {
            if (s.Hos == null) return GalacticTrainer.None;
            var st = s.Seats[seat];
            if (st.Cell < 0) return GalacticTrainer.None;
            int shard = LakeShard(ctx, st.Cell);
            if (shard >= 0) return s.Hos.Act >= 2 && st.ActProgress >= 1 && !st.Shards[shard] ? Commanders[shard] : GalacticTrainer.None;
            if (st.Cell == TempleCell(ctx) && s.Hos.Act >= 3 && st.ActProgress == 2 && st.Heart) return GalacticTrainer.Cyrus;
            return GalacticTrainer.None;
        }

        /// <summary>시작의 사당에 신오의 신 토큰이 있나(p.22 — 있으면 공물을 바칠 수 없다. 토큰은 천상의 균열이 놓는다 — FractureRules).</summary>
        public static bool GodAtOrigin(GameState s, Ctx ctx)
        {
            int cell = OriginShrineCell(ctx);
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.God && t.Cell == cell && !t.Destroyed) return true;
            return false;
        }

        /// <summary>
        /// 3막의 시작의 사당 공물(p.22)을 바칠 수 있나: 3막이 공개됐고, 조각 셋을 모았고, 심장을 아직 회복하지 않았고, 그 사당 칸에 서 있고, 신 토큰이 없다.
        /// 바칠 규토리(색마다 답 하나)는 MapActions.Legal이 본다.
        /// </summary>
        public static bool CanRestoreHeart(GameState s, Ctx ctx, int seat)
        {
            if (s.Hos == null || s.Hos.Act < 3) return false;
            var st = s.Seats[seat];
            return st.ActProgress == 2 && !st.Heart && st.Cell == OriginShrineCell(ctx) && !GodAtOrigin(s, ctx);
        }

        // ── 2막·3막: 명단 → 배틀 ──

        /// <summary>
        /// 간부·태홍에게 도전했다: 조종자(갤럭시단 가상 응답자, A16)에게 명단을 묻는다. Options = 그 인물의 포켓몬(덱 데이터 순서), Count = 낼 수,
        /// Subject = 도전한 좌석. 카드는 답할 때까지 덱에 그대로 있다.
        /// </summary>
        public static void OpenLineup(GameState s, Ctx ctx, Emitter fx, int seat, GalacticTrainer leader)
        {
            bool boss = leader == GalacticTrainer.Cyrus;
            var c = new PendingChoice
            {
                Kind = ChoiceKind.Lineup, Seat = SeatId.Galactic, Subject = seat,
                Count = boss ? BossLineup : CommanderLineup(ShardCount(s.Seats[seat])),
                Source = new SourceRef(SourceKind.Act, ctx.Decks.CardIds.IndexOf(boss ? ActThreeKey : ActTwoKey)),
            };
            foreach (var e in ctx.Deck(boss ? DeckId.GalacticBoss : DeckId.GalacticCommander).Cards)
                if (ctx.Cards.Pokemon[e.Card].Trainer == leader) c.Options.Add(e.Card);
            ChoiceRules.Open(s, ctx, fx, c);
        }

        /// <summary>
        /// 갤럭시단 배틀에서 이겼다(무승부는 승리가 아니다 — A1, 원작 커뮤니티 확인). leader = 상대 명단의 인물.
        /// 돌려주는 값 = 턴 끝 뒤에 할 일: 0 없음 · 1 다음 막 공개(처음 달성) · 2 종막 카드 공개 + 영웅·배신자 선택.
        /// </summary>
        public static int GalacticWon(GameState s, Ctx ctx, Emitter fx, int seat, GalacticTrainer leader)
        {
            var st = s.Seats[seat];
            int shard = ShardOf(leader);
            if (shard >= 0)
            {
                if (st.Shards[shard]) return 0;
                st.Shards[shard] = true;
                int count = ShardCount(st);
                int before = st.OpenSlots;
                st.OpenSlots = CampaignRules.OpenPartySlots(s.Campaign, count); // A2·A3
                if (fx.On)
                {
                    fx.Add(new ShardGained { Seat = seat, Shard = (Shard)shard, Count = count, Cell = st.Cell });
                    fx.Add(new PartySlotUnlocked { Seat = seat, Before = before, After = st.OpenSlots });
                }
                if (count < SeatState.ShardCount) return 0;
                st.ActProgress = 2;
                bool first = s.Hos.FirstSeat[2] < 0;
                if (first) s.Hos.FirstSeat[2] = seat;
                if (fx.On) fx.Add(new ActGoalMet { Seat = seat, Act = 2, First = first, Changes = new ResChange[0] });
                return first ? 1 : 0;
            }
            if (leader == GalacticTrainer.Cyrus)
            {
                st.ActProgress = 3;
                s.Hos.FirstSeat[3] = seat;
                if (fx.On) fx.Add(new ActGoalMet { Seat = seat, Act = 3, First = true, Changes = new ResChange[0] });
                return 2;
            }
            return 0;
        }

        /// <summary>
        /// 3막 시작의 사당 공물(MapAction Offer, p.22): 규토리 1개를 바치고(p.17 「discard an Apricorn whilst at any Shrine」) 조각 셋으로 심장을 회복한다 —
        /// 조각은 그대로 남고, 신오의 심장(지닌물건)이 가방에 들어온다. 턴은 이어진다. 그 공물로 뽑는 사당 이벤트 카드는 「사당 공물·카드」 묶음이 잇는다.
        /// </summary>
        public static void RestoreHeart(GameState s, Ctx ctx, Emitter fx, int seat, int color)
        {
            var st = s.Seats[seat];
            int before = st.Apricorns[color];
            st.Apricorns[color]--;
            st.Heart = true;
            int item = ctx.Items.Ids.IndexOf(HeartItemKey);
            st.Bag.Add(item);
            if (!fx.On) return;
            var src = new SourceRef(SourceKind.Shrine, OriginShrine(ctx));
            fx.Add(new ApricornSpent
            {
                Seat = seat, Source = src,
                Changes = new[] { new ResChange { Seat = seat, Res = ResKind.Apricorn, Key = color, Before = before, After = before - 1 } },
            });
            fx.Add(new HeartRestored { Seat = seat, Source = src, ItemKey = item, Cell = st.Cell });
        }

        // ── 종막 ──

        /// <summary>태홍을 이긴 좌석에게 영웅·배신자를 묻는다(S16_3). 턴은 여기서 멈춘다(다음 턴을 열지 않는다).</summary>
        public static void OpenEnding(GameState s, Ctx ctx, Emitter fx, int seat) =>
            ChoiceRules.Open(s, ctx, fx, new PendingChoice
            {
                Kind = ChoiceKind.Ending, Sub = ChoiceSub.EndingChoice, Seat = seat,
                Source = new SourceRef(SourceKind.Act, ctx.Decks.CardIds.IndexOf(EndingKey)),
            });

        /// <summary>종막 답: 영웅·배신자(Pick 0·1) / 배신자의 도전 순서(PickMany 좌석).</summary>
        public static void ApplyEnding(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            if (c.Sub == ChoiceSub.TraitorOrder)
            {
                SetOrder(s, ctx, fx, c.Seat, a.Values);
                return;
            }
            bool traitor = a.Target == 1;
            if (fx.On) fx.Add(new EndingChosen { Seat = c.Seat, Traitor = traitor });
            if (!traitor)
            {
                End(s, fx, GameEndReason.Hero, c.Seat);
                return;
            }
            s.Hos.Traitor = c.Seat;
            // p.22 「Turns stop and all trainers have their parties fully restored.」 — 차례 좌석부터 시계 방향
            for (int i = 0; i < s.SeatCount; i++) TurnFlow.HealParty(s, ctx, fx, (c.Seat + i) % s.SeatCount, SourceRef.Rule(RuleSource.TraitorReturn));
            var others = new List<int>();
            for (int i = 1; i < s.SeatCount; i++) others.Add((c.Seat + i) % s.SeatCount);
            if (others.Count <= 1)
            {
                SetOrder(s, ctx, fx, c.Seat, others); // 혼자면 상대가 없어 곧바로 배신자 승리(rulings G17 [가정])
                return;
            }
            var order = new PendingChoice { Kind = ChoiceKind.Ending, Sub = ChoiceSub.TraitorOrder, Seat = c.Seat, Source = c.Source };
            order.Options.AddRange(others);
            ChoiceRules.Open(s, ctx, fx, order);
        }

        static void SetOrder(GameState s, Ctx ctx, Emitter fx, int traitor, List<int> order)
        {
            s.Hos.TraitorOrder.Clear();
            s.Hos.TraitorOrder.AddRange(order);
            s.Hos.TraitorIndex = 0;
            if (fx.On) fx.Add(new TraitorOrderSet { Seat = traitor, Seats = order.ToArray() });
            NextTraitorBattle(s, ctx, fx);
        }

        /// <summary>배신자의 다음 도전(새 Seq, 자동): 남은 상대가 없으면 배신자 승리. 도전은 거절할 수 없고 말은 움직이지 않는다(Remote).</summary>
        static void NextTraitorBattle(GameState s, Ctx ctx, Emitter fx)
        {
            var h = s.Hos;
            if (h.TraitorIndex >= h.TraitorOrder.Count)
            {
                End(s, fx, GameEndReason.TraitorWon, h.Traitor);
                return;
            }
            int target = h.TraitorOrder[h.TraitorIndex];
            fx.Begin(h.Traitor, ActionKind.Challenge, AutoKind.Auto);
            if (fx.On)
            {
                fx.Add(new ChallengeIssued { Seat = h.Traitor, From = h.Traitor, To = target, Cell = s.Seats[target].Cell, Remote = true, CanRefuse = false });
                fx.Add(new ChallengeAnswered { Seat = target, To = target, Accept = true, ByTimeout = false });
            }
            BattleFlow.StartTraitor(s, ctx, fx, h.Traitor, target);
        }

        /// <summary>
        /// 배신자 배틀이 끝났다(BattleFlow.Conclude). winner = 이긴 쪽(0 = 배신자, 1 = 상대, −1 = 무승부).
        /// 배신자가 이기면 회복 + 쓴 운명 카드 반환 뒤 다음 상대. 지거나 비기면 그 상대가 승리한다 — 무승부는 도전한 쪽의 패배(rulings G17 [가정], 원작 커뮤니티 확인의 갤럭시단 무승부와 같은 꼴).
        /// </summary>
        public static void AfterTraitorBattle(GameState s, Ctx ctx, Emitter fx, int winner)
        {
            var h = s.Hos;
            int target = h.TraitorOrder[h.TraitorIndex];
            if (winner != 0)
            {
                End(s, fx, GameEndReason.TraitorDefeated, target);
                return;
            }
            TurnFlow.HealParty(s, ctx, fx, h.Traitor, SourceRef.Rule(RuleSource.TraitorReturn));
            ReturnUsedFate(s, ctx, fx, h.Traitor);
            h.TraitorIndex++;
            NextTraitorBattle(s, ctx, fx);
        }

        /// <summary>
        /// p.22 「return any Change of Fate cards used to your hand」 — 배신자가 된 뒤 낸 카드(HosState.TraitorFate)가 버림 더미에서 손으로(FateRules.ReturnUsed, M5 「운명 딜·손패」).
        /// </summary>
        static void ReturnUsedFate(GameState s, Ctx ctx, Emitter fx, int seat) => FateRules.ReturnUsed(s, ctx, fx, seat);

        static void End(GameState s, Emitter fx, GameEndReason reason, int winner)
        {
            s.Phase = Phase.GameOver;
            s.Hos.Winner = winner;
            if (fx.On) fx.Add(new GameEnded { Seat = winner, Reason = reason, Winners = new[] { winner } });
        }
    }
}
