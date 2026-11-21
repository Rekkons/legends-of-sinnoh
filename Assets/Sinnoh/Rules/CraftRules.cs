using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 제작(S10, 룰북 p.14 「During your turn and whilst outside of battle, you may craft items from the Apricorns」, M5 「제작·은행·진화」).
    /// 레시피 = <see cref="ItemDef.Recipe"/>(14개: 볼 4 · 스튜 4 · 지닌물건 6). MapAction 답 Craft(Target = 아이템 색인) — 재료 규토리를 내고
    /// 볼은 볼 칸으로, 지닌물건은 가방으로(들리기는 정비 HoldItem), 스튜는 만들자마자 효과(「Instant effect. Each type of stew can only be crafted once per turn」).
    /// 스튜 넷: 회복스튜 = 파티 전체 회복 · 자극적인 스튜 = 이번 턴 이동력 +3(우두머리 막기는 <see cref="AlphaWarded"/> — 우두머리 배틀 묶음이 읽는다) ·
    /// 우러난 스튜 = 이번 턴 진화 한 번 반값(EvolveRules — 어느 진화에 쓸지 플레이어가 고른다) · 매혹적인 스튜 = 그 턴의 다음 야생 조우에서 한 번, 내 공격·포획 굴림의 심볼 1개 → 일반 타격 1개(쓸지 묻는다 — <see cref="AskAttack"/>·BattleFlow 포획).
    /// 기록은 GameState.Used에 둔다(UsageKind.Stew·InfusedStew·AlluringStew) — 새 상태 칸이 없다.
    /// </summary>
    public static class CraftRules
    {
        public const string RestoringStew = "item.restoring_stew";
        public const string AlluringStew = "item.alluring_stew";
        public const string ZestyStew = "item.zesty_stew";
        public const string InfusedStew = "item.infused_stew";

        /// <summary>자극적인 스튜가 더하는 이동력(p.14 「Move 3 additional tiles this turn」).</summary>
        public const int ZestyMoves = 3;

        static readonly string[] BallKeys = { "item.poke_ball", "item.greater_ball", "item.ultimate_ball", "item.keystone_ball" };

        /// <summary>그 볼 등급의 아이템 색인.</summary>
        public static int BallItem(Ctx ctx, Ball ball) => ctx.Items.Ids.IndexOf(BallKeys[(int)ball]);

        /// <summary>볼 아이템의 볼 등급. 볼이 아니면 −1.</summary>
        public static int BallOf(Ctx ctx, int item) => item >= 0 && item < ctx.Items.Items.Count ? Array.IndexOf(BallKeys, ctx.Items.Items[item].Key) : -1;

        static bool Is(Ctx ctx, int item, string key) => ctx.Items.Items[item].Key == key;

        /// <summary>그 규토리들을 낼 수 있나(색마다 개수).</summary>
        public static bool CanPay(SeatState st, IReadOnlyList<ApricornColor> cost)
        {
            var need = new int[SeatState.ApricornColors];
            foreach (var c in cost) need[(int)c]++;
            for (int i = 0; i < need.Length; i++)
                if (st.Apricorns[i] < need[i]) return false;
            return true;
        }

        /// <summary>그 아이템을 지금 만들 수 있나: 레시피가 있고, 재료가 있고, 스튜면 이번 턴에 그 스튜를 아직 만들지 않았다.</summary>
        public static bool CanCraft(GameState s, Ctx ctx, int seat, int item)
        {
            if (seat < 0 || seat >= s.SeatCount || item < 0 || item >= ctx.Items.Items.Count) return false;
            var d = ctx.Items.Items[item];
            if (d.Recipe == null || !CanPay(s.Seats[seat], TrainerRules.Recipe(s, ctx, seat, d))) return false; // ace_trainer B: 배틀 아이템은 색마다 하나(rulings A39)
            return d.Kind != ItemKind.Stew || !MapActions.Used(s, seat, UsageKind.Stew, item);
        }

        /// <summary>MapAction의 제작 답(아이템 데이터 순서, G9).</summary>
        public static void Legal(GameState s, Ctx ctx, int seat, List<ChoiceAnswer> into)
        {
            var bonus = new List<int>();
            for (int i = 0; i < ctx.Items.Items.Count; i++)
            {
                if (!CanCraft(s, ctx, seat, i)) continue;
                into.Add(ChoiceAnswer.Of(AnswerKind.Craft, i));
                TrainerRules.Bonuses(s, ctx, seat, i, bonus); // picnicker R 스튜 · poke_fan R 배틀 아이템 하나 더(Arg = 공짜 몫, rulings A39)
                foreach (int j in bonus) into.Add(ChoiceAnswer.Of(AnswerKind.Craft, i, j));
            }
        }

        /// <summary>규토리를 내거나(sign −1) 받는다(+1). 바뀐 색마다 ResChange(색 표 순서).</summary>
        internal static void MoveApricorns(SeatState st, int seat, IReadOnlyList<ApricornColor> colors, int sign, List<ResChange> changes)
        {
            var n = new int[SeatState.ApricornColors];
            foreach (var c in colors) n[(int)c]++;
            for (int color = 0; color < n.Length; color++)
            {
                if (n[color] == 0) continue;
                int before = st.Apricorns[color];
                st.Apricorns[color] += sign * n[color];
                changes.Add(new ResChange { Seat = seat, Res = ResKind.Apricorn, Key = color, Before = before, After = st.Apricorns[color] });
            }
        }

        /// <summary>제작 답 적용. 턴은 이어진다.</summary>
        public static void Apply(GameState s, Ctx ctx, Emitter fx, int seat, int item, int bonus = -1)
        {
            Make(s, ctx, fx, seat, item, bonus);
            MapActions.Continue(s, ctx, fx, seat);
        }

        /// <summary>만든다(재료를 내고 볼 칸·가방으로, 스튜는 바로 효과). 다음 선택은 부르는 쪽이 연다 — 맵 행동이면 MapAction, 조우 고르기면 같은 고르기(MapActions.ApplyEncounterPick).</summary>
        internal static void Make(GameState s, Ctx ctx, Emitter fx, int seat, int item, int bonus = -1)
        {
            var st = s.Seats[seat];
            var d = ctx.Items.Items[item];
            var changes = new List<ResChange>();
            var recipe = TrainerRules.Recipe(s, ctx, seat, d);
            if (recipe != d.Recipe) TrainerRules.Used(s, fx, seat, AbilityColor.Blue); // ace_trainer B
            MoveApricorns(st, seat, recipe, -1, changes);
            switch (d.Kind)
            {
                case ItemKind.Ball:
                {
                    int ball = BallOf(ctx, item);
                    int before = st.Balls[ball];
                    st.Balls[ball]++;
                    changes.Add(new ResChange { Seat = seat, Res = ResKind.Ball, Key = ball, Before = before, After = st.Balls[ball] });
                    break;
                }
                case ItemKind.Stew:
                    break;
                default:
                {
                    int before = 0;
                    foreach (int b in st.Bag)
                        if (b == item) before++;
                    st.Bag.Add(item);
                    changes.Add(new ResChange { Seat = seat, Res = ResKind.Item, Key = item, Before = before, After = before + 1 });
                    break;
                }
            }
            if (bonus >= 0) TrainerRules.Bonus(s, ctx, fx, seat, item, bonus, changes);
            if (fx.On) fx.Add(new Crafted { Seat = seat, ItemKey = item, Changes = changes.ToArray() });
            if (d.Kind == ItemKind.Stew) Eat(s, ctx, fx, seat, item);
            if (d.Kind == ItemKind.Stew && bonus >= 0) Eat(s, ctx, fx, seat, bonus, true); // picnicker R: 다른 스튜의 효과를 공짜로
        }

        /// <summary>스튜 효과. free = 화려한 만찬(사당 카드)의 공짜 스튜 — 만든 스튜의 「턴당 1번」을 쓰지 않는다(ShrineRules).</summary>
        internal static void Eat(GameState s, Ctx ctx, Emitter fx, int seat, int item, bool free = false)
        {
            var st = s.Seats[seat];
            if (!free) MapActions.Mark(s, fx, seat, UsageKind.Stew, item, false);
            int moves = 0;
            if (Is(ctx, item, ZestyStew))
            {
                moves = ZestyMoves;
                st.MovesLeft += moves;
                st.MoveBonus += moves;
            }
            // 화려한 만찬의 스튜와 만든 같은 스튜는 둘 다 쓴다(원작 커뮤니티 확인 — 만찬의 자극적인 스튜 + 만든 자극적인 스튜) — 먹은 스튜마다 한 번씩 남는다
            else if (Is(ctx, item, InfusedStew)) MapActions.Mark(s, fx, seat, UsageKind.InfusedStew, -1, false);
            // 그 턴의 다음 야생 조우에서 한 번(만든 턴이 끝나면 사라진다 — 원작 커뮤니티 확인)
            else if (Is(ctx, item, AlluringStew)) MapActions.Mark(s, fx, seat, UsageKind.AlluringStew, item, false);
            if (fx.On) fx.Add(new StewEaten { Seat = seat, Source = new SourceRef(SourceKind.Item, item), ItemKey = item, Moves = moves, MovesLeft = st.MovesLeft });
            if (Is(ctx, item, RestoringStew)) TurnFlow.HealParty(s, ctx, fx, seat, new SourceRef(SourceKind.Item, item));
        }

        /// <summary>이번 턴 자극적인 스튜를 먹었다 — 우두머리 포켓몬이 이 좌석과 배틀하지 않는다(p.14 「whilst preventing Alpha Pokémon from battling you」).</summary>
        public static bool AlphaWarded(GameState s, Ctx ctx, int seat) => MapActions.Used(s, seat, UsageKind.Stew, ctx.Items.Ids.IndexOf(ZestyStew));

        /// <summary>이번 턴 우러난 스튜의 진화 반값이 남아 있다(p.14 「Discard only half of the Journey Points needed to evolve 1 Pokémon this turn, rounded down」).</summary>
        public static bool InfusedReady(GameState s, int seat) => MapActions.Used(s, seat, UsageKind.InfusedStew);

        /// <summary>우러난 스튜의 반값을 썼다(진화 한 번 — 먹은 스튜 하나 몫).</summary>
        internal static void SpendInfused(GameState s, int seat) => SpendOne(s, seat, UsageKind.InfusedStew);

        /// <summary>그 좌석의 그 기록 하나를 지운다(먹은 스튜 하나 몫을 썼다).</summary>
        static void SpendOne(GameState s, int seat, UsageKind kind)
        {
            for (int i = s.Used.Count - 1; i >= 0; i--)
                if (s.Used[i].Kind == kind && s.Used[i].Seat == seat)
                {
                    s.Used.RemoveAt(i);
                    return;
                }
        }

        /// <summary>매혹적인 스튜가 다음 야생 조우를 기다린다.</summary>
        public static bool AlluringReady(GameState s, int seat)
        {
            foreach (var u in s.Used)
                if (u.Kind == UsageKind.AlluringStew && u.Seat == seat) return true;
            return false;
        }

        /// <summary>그 좌석이 지금 배틀의 그 굴림(faces)에 매혹적인 스튜를 쓸 수 있나: 야생 조우(트레이너전·갤럭시단·배신자 연전이 아닌 배틀)이고, 스튜가 남았고, 굴림에 상태 이상 심볼이 있다.</summary>
        public static bool AlluringOpen(GameState s, int seat, IReadOnlyList<AttackDieFace> faces)
        {
            var b = s.Battle;
            if (b == null || seat < 0 || b.Kind == BattleKind.Trainer || b.Kind == BattleKind.Traitor || b.Kind == BattleKind.Galactic || !AlluringReady(s, seat)) return false;
            foreach (var f in faces)
                if (f == AttackDieFace.StatusSymbol) return true;
            return false;
        }

        static void ClearAlluring(GameState s, int seat)
        {
            for (int i = s.Used.Count - 1; i >= 0; i--)
                if (s.Used[i].Kind == UsageKind.AlluringStew && s.Used[i].Seat == seat) s.Used.RemoveAt(i);
        }

        /// <summary>그 굴림에 매혹적인 스튜를 쓴 꼴: 왼쪽 첫 상태 이상 심볼 → 일반 타격(새 배열, 심볼이 없으면 그대로 복사).</summary>
        public static AttackDieFace[] Allured(IReadOnlyList<AttackDieFace> faces)
        {
            var r = new AttackDieFace[faces.Count];
            for (int i = 0; i < r.Length; i++) r[i] = faces[i];
            int at = Array.IndexOf(r, AttackDieFace.StatusSymbol);
            if (at >= 0) r[at] = AttackDieFace.RegularHit;
            return r;
        }

        /// <summary>
        /// 내 공격 굴림(굴린 뒤 면 바꾸기·결정 72 순서 선택이 모두 끝난 뒤, 굴린 뒤 반응 창 앞 — [가정])에 매혹적인 스튜를 쓸지 묻는다(Reaction/AlluringStew, Subject = 0).
        /// 물었으면 true(배틀은 답이 잇는다). 「안 씀」이 첫 답이고, 기본(시간 초과·봇)은 결과가 좋아질 때만 씀(<see cref="AlluringHelps"/>).
        /// </summary>
        internal static bool AskAttack(GameState s, Ctx ctx, Emitter fx, BattleState b)
        {
            if (b.Move == -1 || b.ActiveSide < 0) return false;
            var side = b.Sides[b.ActiveSide];
            if (!AlluringOpen(s, side.Owner, b.Faces)) return false;
            ChoiceRules.Open(s, ctx, fx, new PendingChoice
            {
                Kind = ChoiceKind.Reaction, Sub = ChoiceSub.AlluringStew, Seat = side.Responder, Subject = 0,
                Source = new SourceRef(SourceKind.Item, ctx.Items.Ids.IndexOf(AlluringStew)),
            });
            return true;
        }

        /// <summary>
        /// 쓰면 결과가 좋아지나(봇·시간 초과의 기본, [가정]): 공격 굴림 = 이번 공격의 최종 피해가 늘어난다(쓰러뜨림 포함) · 포획 굴림 = 실패가 성공이 된다(물을 때 실은 두 합).
        /// </summary>
        public static bool AlluringHelps(GameState s, Ctx ctx, PendingChoice c)
        {
            var b = s.Battle;
            if (b == null) return false;
            if (c.Subject == 1)
            {
                int target = b.Sides[1 - ChoiceRules.BattleSideOf(s, c.Seat)].Active;
                var tp = s.Pokemon[target];
                int rate = BattleRules.CatchRate(MapActions.GradeOf(ctx, tp.Def), tp.Alpha);
                return c.Options[2] < rate && c.Options[3] >= rate;
            }
            return BattleFlow.Damage(s, ctx, b, Allured(b.Faces), null).Final > BattleFlow.Damage(s, ctx, b, b.Faces, null).Final;
        }

        /// <summary>매혹적인 스튜 묻기의 답: 쓰면 그 스튜 하나 몫이 끝(공격 굴림이면 그 굴림의 첫 심볼 → 일반 타격, DiceFacesChanged 출처 = 스튜). 그다음 멈춘 자리(굴린 뒤 반응 창 · 포획 판정)를 잇는다.</summary>
        internal static void ApplyAlluring(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            var b = s.Battle;
            int seat = b.Sides[ChoiceRules.BattleSideOf(s, c.Seat)].Owner;
            bool use = a.Kind == AnswerKind.Accept && AlluringReady(s, seat);
            if (use) SpendOne(s, seat, UsageKind.AlluringStew); // 둘이면 하나는 그 조우의 다른 굴림에 남는다 [가정 plan §5 #95 ③ — 한 굴림에 하나]
            if (c.Subject == 1)
            {
                BattleFlow.ResumeCapture(s, ctx, fx, c, use);
                return;
            }
            if (use)
            {
                int at = b.Faces.IndexOf(AttackDieFace.StatusSymbol);
                var before = AttackDice.Src(b.Faces);
                b.Faces[at] = AttackDieFace.RegularHit;
                if (fx.On)
                    fx.Add(new DiceFacesChanged
                    {
                        Source = c.Source, SrcBefore = before, SrcAfter = AttackDice.Src(b.Faces),
                        Changes = new[] { new FaceChange { Index = at, Before = AttackDieFace.StatusSymbol, After = AttackDieFace.RegularHit } },
                    });
            }
            Reactions.Open(s, ctx, fx, Window.AfterRoll, true);
            BattleFlow.Run(s, ctx, fx);
        }

        /// <summary>배틀이 끝났다: 야생 조우(트레이너전·갤럭시단이 아닌 배틀)였으면 그 좌석의 매혹적인 스튜는 쓰지 않았어도 사라진다(「your next wild encounter」).</summary>
        internal static void BattleEnded(GameState s, BattleState b)
        {
            if (b.Kind == BattleKind.Trainer || b.Kind == BattleKind.Traitor || b.Kind == BattleKind.Galactic) return;
            if (b.Sides[0].Owner >= 0) ClearAlluring(s, b.Sides[0].Owner);
        }
    }
}
