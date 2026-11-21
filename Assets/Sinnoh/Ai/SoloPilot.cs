using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Ai
{
    /// <summary>
    /// 확인용 길잡이(plan §4 「조우·배틀 2단계」): 솔로 판에서 사람 좌석이 「나무·산호를 찾아가 찾아보기 → 조우 → 야생 배틀 → 고향으로 돌아가 도전」을 하도록 답을 하나씩 고른다.
    /// 게임이 쓰는 AI가 아니다 — 캡처(<c>UiShots</c>)·플레이 시험·플레이어 확인(<c>ScreenProbe</c>·<c>MouseProbe</c>)·dotnet 시험이 같은 판을 같은 길로 지나려고 한 곳에 둔다
    /// (명령 순서가 같으면 시드가 같은 판은 늘 같다 — 화면으로 누르든 링크로 답하든).
    /// 보기와 합법 답만 본다. 난수를 쓰지 않는다. 고른 답은 늘 합법 답 가운데 하나다.
    /// </summary>
    public sealed class SoloPilot
    {
        /// <summary>찾아갈 나무·산호의 등급(약한 3장 · 적당한 2장 · 강한 1장).</summary>
        public EncounterTier Tier = EncounterTier.Weak;

        /// <summary>야생 배틀을 이만큼 치른 뒤에는 고향 마을로 돌아가 도전한다(회복할 것이 있으면 먼저 [전체 회복]).</summary>
        public int Hunts = 1;

        /// <summary>볼이 있으면 기술 대신 던진다.</summary>
        public bool Throw = true;

        /// <summary>조우 카드 줄에서 고를 자리(카드가 모자라면 마지막 장).</summary>
        public int Card;

        /// <summary>기술을 가르칠 수 있으면 가르친다(첫 포켓몬의 첫 칸). 아니면 가르치지 않는다.</summary>
        public bool Teach = true;

        /// <summary>파티가 가득 찼을 때 놓아줄 것: true = 파티의 첫 포켓몬, false = 막 잡은 포켓몬.</summary>
        public bool ReleaseOld;

        /// <summary>교체 창에서 교체한다(첫 대기 포켓몬). 아니면 「교체 안 함」.</summary>
        public bool Switch;

        /// <summary>
        /// 막 길(M5 「막·종막 화면」): 찾아보기 대신 막 목표로 간다 — 3막 시작의 사당에서 [공물](첫 공물 답 — 3장 뽑기 아님) → 신오신전 → [도전](간부·태홍),
        /// 그 칸에서 바로 도전할 수 있으면 도전한다. 종막에서는 <see cref="Traitor"/>대로 고르고, 배신자의 순서는 기본(다음 좌석부터 시계 방향).
        /// </summary>
        public bool Finale;

        /// <summary>종막 선택에서 배신자를 고른다(아니면 영웅).</summary>
        public bool Traitor;

        /// <summary>
        /// 운명 길(M5 「운명·응답 화면」 — <see cref="SoloTrail.Fate"/>·<see cref="SoloTrail.FateGiratina"/>): 맵 행동에서 펄기아 1장을 한 번(첫 합법 칸) ·
        /// 굴린 뒤 창에서 디아루가 3장(자동 나눔) · 손에 디아루가가 없으면 굴리기 전 창에서 기라티나 1장. 나머지 운명 창은 안 씀.
        /// </summary>
        public bool Fates;

        /// <summary>
        /// 사당 길(M5 「사당·균열 화면」 — <see cref="SoloTrail.Shrine"/>): 맵 행동에서 분홍 규토리 + 여행 점수 7 공물(없으면 첫 공물) → 3장 중 첫 카드부터 →
        /// 받을 규토리는 마지막 묶음(검정) → 추종자는 펄기아. 공물을 바친 뒤에는 턴을 끝낸다.
        /// </summary>
        public bool Shrines;

        /// <summary>지금까지 치른 야생 배틀 수(<see cref="Saw"/>로 센다).</summary>
        public int Hunted { get; private set; }

        /// <summary>받은 이벤트를 알린다: 끝난 야생 배틀과 빈 찾아보기(조우가 없었다 — 같은 칸을 다시 찾지 않게)를 센다.</summary>
        public void Saw(IReadOnlyList<GameEvent> events)
        {
            if (events == null) return;
            foreach (var e in events)
                if (e is BattleStarted bs) wild = bs.Kind != BattleKind.Galactic && bs.Kind != BattleKind.Trainer && bs.Kind != BattleKind.Traitor;
                else if (e is BattleEnded && wild) { Hunted++; wild = false; }
                else if (e is EncounterDrawn d && (d.Cards == null || d.Cards.Length == 0)) Hunted++;
        }

        bool wild;

        /// <summary>그 선택의 답을 고른다. 받을 줄 모르는 종류면 null(대신 답하지 않는다).</summary>
        public ChoiceAnswer Decide(Ctx ctx, GameState view, PendingChoice choice, List<ChoiceAnswer> legal)
        {
            if (ctx == null || view == null || choice == null || legal == null || legal.Count == 0) return null;
            switch (choice.Kind)
            {
                case ChoiceKind.MapAction: return Map(ctx, view, choice.Seat, legal);
                case ChoiceKind.HiddenSpot: return Find(legal, AnswerKind.Decline);
                case ChoiceKind.Ending:
                    if (choice.Sub == ChoiceSub.TraitorOrder) return legal.Find(a => a.SameAs(choice.Default)) ?? legal[0];
                    return legal.Find(a => a.Kind == AnswerKind.Pick && a.Target == (Traitor ? 1 : 0)) ?? legal[0];
                case ChoiceKind.Discard: return legal[legal.Count - 1];   // 운명 손패 6장째(A12): 손패의 마지막 종류
                case ChoiceKind.Respond:
                    // 울트라 버스트 라운드 끝 「UB 턴을 건너뛰겠습니까?」: 건너뛰지 않는다 · 축복 동의: 막지 않는다(기본 — 결정 84 U1·U2). 도전·거래는 받지 않는다
                    if (choice.Sub == ChoiceSub.UbSkip || choice.Sub == ChoiceSub.Blessing) return legal.Find(a => a.SameAs(choice.Default)) ?? legal[0];
                    return null;
                case ChoiceKind.Simultaneous:
                    // 천상의 균열의 추종자(사당 판 S33): 사당 길은 펄기아, 그 밖은 기본 답(지금 추종자 — 처음이면 안 고름)
                    if (choice.Sub != ChoiceSub.Follower) return null;
                    return (Shrines ? legal.Find(a => a.Kind == AnswerKind.Pick && a.Target == (int)SinnohGod.Palkia) : null) ?? legal.Find(a => a.SameAs(choice.Default)) ?? legal[0];
                case ChoiceKind.EncounterPick:
                {
                    var cards = legal.FindAll(a => a.Kind == AnswerKind.Pick);   // 조우 고르기의 제작 답(A-2)은 고르지 않는다
                    return cards[Math.Min(Math.Max(0, Card), cards.Count - 1)];
                }
                case ChoiceKind.SendIn:
                case ChoiceKind.NextPokemon: return Find(legal, AnswerKind.Pick);
                case ChoiceKind.SwitchWindow: return Switch ? Find(legal, AnswerKind.Pick) ?? Find(legal, AnswerKind.Decline) : Find(legal, AnswerKind.Decline);
                case ChoiceKind.BattleAction:
                    return (Throw ? Find(legal, AnswerKind.ThrowBall) : null) ?? Find(legal, AnswerKind.UseMove);
                case ChoiceKind.Reaction:
                    if (Fates && choice.Sub == ChoiceSub.Fate) return Fate(ctx, view, choice.Seat, legal);
                    if (choice.Sub == ChoiceSub.EffectOrder) return legal[0];   // 결정 72 순서 창: 기본 순서(졸림 먼저)
                    if (choice.Sub == ChoiceSub.AlluringStew) return legal.Find(a => a.SameAs(choice.Default)) ?? legal[0];   // 매혹적인 스튜: 결과가 좋아질 때만(기본)
                    if (choice.Sub == ChoiceSub.EffectPick) return legal.Find(a => a.SameAs(choice.Default)) ?? legal[0];     // 기술 효과의 고르기(rulings G20): 기본 답(안 함이 있으면 안 함)
                    return choice.Sub == ChoiceSub.InitiativeToken ? Find(legal, AnswerKind.Accept) : Find(legal, AnswerKind.Decline);
                case ChoiceKind.Reward:
                    switch (choice.Sub)
                    {
                        case ChoiceSub.TeachMove: return Teach ? Find(legal, AnswerKind.Pick) ?? Find(legal, AnswerKind.Decline) : Find(legal, AnswerKind.Decline);
                        case ChoiceSub.PartyFull: return ReleaseOld && legal.Count > 1 ? legal[1] : legal[0];
                        case ChoiceSub.CoralColor: return legal[0];
                        // 사당 카드의 고르기(사당 판 S32 — 막 길의 3막 공물이 뽑는 카드 포함): 기본 답(첫 답 — 「안 함」이 있으면 안 함). 사당 길은 받을 규토리만 마지막 묶음(검정)
                        default:
                            if (choice.Source.Kind != SourceKind.ShrineEvent && choice.Sub != ChoiceSub.SharedBlessing) return null;
                            if (Shrines && choice.Sub == ChoiceSub.ShrineApricorns) return legal[legal.Count - 1];
                            return legal.Find(a => a.SameAs(choice.Default)) ?? legal[0];
                    }
                default: return null;
            }
        }

        static ChoiceAnswer Find(List<ChoiceAnswer> legal, AnswerKind kind) => legal.Find(a => a.Kind == kind);

        // 운명 길의 반응 창: 디아루가 3장(자동 나눔) → 디아루가가 손에 없으면 기라티나 1장 → 안 씀
        static ChoiceAnswer Fate(Ctx ctx, GameState view, int seat, List<ChoiceAnswer> legal)
        {
            ChoiceAnswer d3 = null;   // 주사위를 가장 많이 다시 굴리는 묶음(자동 나눔)
            foreach (var a in legal)
                if (a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Dialga && a.Arg == FateRules.Play3Count && FateRules.WildsOf(a) < 0 && (d3 == null || a.Values.Count > d3.Values.Count)) d3 = a;
            if (d3 != null) return d3;
            bool dialga = view.Seats[seat].FateHand.Contains(FateRules.CardKey(ctx, FateCard.Dialga));
            var g = legal.Find(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Giratina && a.Arg == 1);
            if (g != null && !dialga) return g;
            return Find(legal, AnswerKind.Decline);
        }

        ChoiceAnswer Map(Ctx ctx, GameState view, int seat, List<ChoiceAnswer> legal)
        {
            var me = view.Seats[seat];
            int goal;
            var take = Find(legal, AnswerKind.TakeApricorns);   // 빈 찾아보기 뒤(rulings G12): 규토리를 줍고 턴이 끝난다
            if (take != null) return take;
            if (Fates)
            {
                // 운명 길: 펄기아 1장으로 순간이동(칸 색인 순서의 마지막 [가정]) — 손에 펄기아가 있는 동안만 합법 답에 있다(상태를 따로 적지 않는다: 화면이 답을 못 받으면 다시 고른다)
                var warp = legal.FindLast(a => a.Kind == AnswerKind.PlayFate && a.Target == (int)FateCard.Palkia && a.Arg == 1);
                if (warp != null) return warp;
            }
            if (Shrines)
            {
                // 사당 길: 분홍 규토리 + 여행 점수 7(3장 뽑기) → 없으면 첫 공물 → 바친 뒤에는 턴 종료
                var give = legal.Find(a => a.Kind == AnswerKind.Offer && a.Arg == (int)ApricornColor.Pink && a.Values.Count > 0) ?? legal.Find(a => a.Kind == AnswerKind.Offer);
                return give ?? Find(legal, AnswerKind.EndTurn);
            }
            if (Finale)
            {
                // 막 길: 공물(3막 심장) → 도전(간부·태홍) → 그 자리로 걷기
                var offer = legal.Find(a => a.Kind == AnswerKind.Offer && a.Values.Count == 0);
                if (offer != null && !me.Heart && view.Hos != null && view.Hos.Act >= 3) return offer;
                var duel = Find(legal, AnswerKind.Challenge);
                if (duel != null) return duel;
                goal = view.Hos == null ? -1 : me.Heart ? HosFlow.TempleCell(ctx) : view.Hos.Act >= 3 && me.ActProgress == 2 ? HosFlow.OriginShrineCell(ctx) : -1;
            }
            else if (Hunted < Hunts)
            {
                var search = legal.Find(a => a.Kind == AnswerKind.Search && a.Target < 0 && a.Arg < 0); // 나무·산호 찾아보기만(무리·전설·신 배틀은 찾아 둔 판에 넣지 않는다, rulings A28)
                if (search != null) return search;
                goal = NearestToken(ctx, view, me.Cell);
            }
            else
            {
                var challenge = Find(legal, AnswerKind.Challenge);
                var heal = Find(legal, AnswerKind.VillageHeal);
                if (heal != null && Hurt(ctx, view, me)) return heal;
                if (challenge != null) return challenge;
                goal = me.HomeVillage >= 0 ? ctx.Fixtures.Fixtures[me.HomeVillage].Cell : -1;
            }
            if (goal < 0 || goal == me.Cell) return Find(legal, AnswerKind.EndTurn);

            // 갈 수 있는 칸 가운데 목표까지 남는 걸음이 가장 적은 칸(같으면 칸 번호가 작은 것)
            ChoiceAnswer best = null;
            int bestLeft = int.MaxValue;
            var from = MoveRules.Reachable(ctx.Board, me.Cell, Far, view.Options.ElevationMode);
            int now = from[goal];
            foreach (var a in legal)
            {
                if (a.Kind != AnswerKind.Move) continue;
                int left = a.Target == goal ? 0 : MoveRules.Reachable(ctx.Board, a.Target, Far, view.Options.ElevationMode)[goal];
                if (left == MoveRules.Unreachable) continue;
                if (left < bestLeft || (left == bestLeft && a.Target < best.Target)) { best = a; bestLeft = left; }
            }
            if (best == null || (now != MoveRules.Unreachable && bestLeft >= now)) return Find(legal, AnswerKind.EndTurn);
            return best;
        }

        const int Far = 400;

        static bool Hurt(Ctx ctx, GameState view, SeatState me)
        {
            foreach (int id in me.Party)
            {
                if (id < 0) continue;
                var p = view.Pokemon[id];
                if (PokemonRules.Hp(ctx, p) < PokemonRules.MaxHp(ctx, p) || p.Status != StatusEffect.None) return true;
            }
            return false;
        }

        // 걸어서 가장 가까운 그 등급의 나무·산호(같으면 칸 번호가 작은 것). 이번 턴에 이미 비어 있던 칸은 뺀다
        int NearestToken(Ctx ctx, GameState view, int cell)
        {
            var steps = MoveRules.Reachable(ctx.Board, cell, Far, view.Options.ElevationMode);
            int best = -1;
            foreach (var t in view.Tokens)
            {
                if (t.Destroyed || t.Cell < 0 || (t.Kind != TokenKind.ApricornTree && t.Kind != TokenKind.ApricornReef)) continue;
                if (ctx.Fixtures.Fixtures[t.Def].Tier != Tier || steps[t.Cell] == MoveRules.Unreachable) continue;
                if (best < 0 || steps[t.Cell] < steps[best] || (steps[t.Cell] == steps[best] && t.Cell < best)) best = t.Cell;
            }
            return best;
        }
    }
}

namespace Sinnoh.Ai
{
    /// <summary>
    /// 찾아 둔 판(확인용): 시드 · 고향 마을 색인 · 스타팅 포켓몬 색인(합법 답 순서 — 트레이너 카드는 늘 첫 장, AI 시드 <see cref="AiSeed"/>) + 길잡이 설정 + 쥐여 줄 볼.
    /// 규칙·데이터가 바뀌어 판이 달라지면 dotnet 시험 <c>SoloPilotTests</c>가 알려 준다 — 그때 다시 찾는다(그 시험의 「찾기」).
    /// <para>볼을 쥐여 준 판(<see cref="Balls"/> &gt; 0)은 손본 판이다: 볼을 얻는 길(제작 — M5)이 아직 없어 실제 진행으로는 포획에 닿지 못한다.
    /// 포획·출전·교체·파티 가득 화면은 이 판으로만 지난다.</para>
    /// </summary>
    public sealed class SoloTrail
    {
        public const ulong AiSeed = 99;

        public readonly string Name;
        public readonly ulong Seed;
        public readonly int Village, Starter, Hunts, Balls;
        public readonly EncounterTier Tier;

        SoloTrail(string name, ulong seed, int village, int starter, EncounterTier tier, int hunts, int balls)
        {
            Name = name;
            Seed = seed;
            Village = village;
            Starter = starter;
            Tier = tier;
            Hunts = hunts;
            Balls = balls;
        }

        /// <summary>실제 진행(볼 없음): 약한 나무 → 3장 → 격파 → 기술 가르치기 → 고향에서 [전체 회복] → [도전](진다).</summary>
        public static readonly SoloTrail Defeat = new SoloTrail("defeat", 3, 0, 0, EncounterTier.Weak, 1, 0);
        /// <summary>실제 진행(볼 없음): 약한 산호 → 내 포켓몬이 쓰러져 야생이 도망 → 받을 규토리 색 고르기 → [도전].</summary>
        public static readonly SoloTrail Coral = new SoloTrail("coral", 1, 3, 0, EncounterTier.Weak, 1, 0);
        /// <summary>실제 진행: 적당한 나무(2장) · 강한 나무(1장).</summary>
        public static readonly SoloTrail Two = new SoloTrail("two", 3, 0, 0, EncounterTier.Moderate, 1, 0);
        public static readonly SoloTrail One = new SoloTrail("one", 3, 0, 0, EncounterTier.Strong, 1, 0);
        /// <summary>손본 판(몬스터볼 9): 세 번 잡는다 → 출전 고르기 · 셋째에 파티 가득 → [도전]에서 교체 창 · 다음 포켓몬 → 이겨서 1막 목표.</summary>
        public static readonly SoloTrail Catch = new SoloTrail("catch", 1, 0, 0, EncounterTier.Weak, 3, 9);

        /// <summary>
        /// 포획 실패 판(볼을 쥐여 준 판 — <see cref="Catch"/>와 시드만 다르다): 첫 볼이 빗나간다 — 눈 합이 포획률 3에 1·2·3 모자라 볼이 2·1·0번 흔들린다(04 §8-8 68행의 실패 박자 셋, 결정 54 ①).
        /// </summary>
        public static readonly SoloTrail Miss2 = new SoloTrail("miss2", 3, 0, 0, EncounterTier.Weak, 3, 9);
        public static readonly SoloTrail Miss1 = new SoloTrail("miss1", 6, 0, 0, EncounterTier.Weak, 3, 9);
        public static readonly SoloTrail Miss0 = new SoloTrail("miss0", 283, 0, 0, EncounterTier.Weak, 3, 9);

        /// <summary>
        /// 손본 판(정비 확인용 — <see cref="Catch"/>와 같은 길): 볼에 더해 기술 카드 풀과 가방의 지닌물건을 쥐여 준다(<see cref="GiveKit"/>).
        /// 스탬프 보상 기술(트레이너전 — M5)과 지닌물건(제작 — M5)을 얻는 길이 아직 없어 실제 진행으로는 기술 가르치기·지닌물건 화면에 넣을 것이 생기지 않는다.
        /// </summary>
        public static readonly SoloTrail Kit = new SoloTrail("kit", 1, 0, 0, EncounterTier.Weak, 3, 9) { WithKit = true };

        /// <summary>정비 확인용으로 쥐여 주는 기술 카드(moves.json 키 — 물 둘(이 판의 스타팅 팽도리가 배운다) · 풀 · 불꽃 · 땅 · 악)와 지닌물건(items.json 키).</summary>
        public static readonly string[] KitMoves = { "surf", "water_pulse", "giga_drain", "flamethrower", "earthquake", "bite" };
        public static readonly string[] KitItems = { "item.choice_band", "item.swiftness_sash", "item.lucky_egg" };

        /// <summary>
        /// 막 길(M5 「막·종막 화면」 — 셋업 뒤 상태를 손본 판, <see cref="After"/>): 2막 · 조각 둘을 가진 좌석이 입지호수(의지)에 서서 [도전] → 새턴의 명단 볼 4개(72행) →
        /// 이기면 조각 · 파티 칸 · 2막 목표(94행) → 3막 카드(4행, 「플레이어 1 달성」).
        /// </summary>
        public static readonly SoloTrail Lake = new SoloTrail("lake", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareLake };

        /// <summary>
        /// 막 길(셋업 뒤 상태를 손본 판): 3막 · 조각 셋을 가진 좌석이 시작의 사당에 서서 [공물](심장 — 94행) → 신오신전까지 걸어 [도전] → 태홍의 명단 볼 6개 →
        /// 이기면 3막 목표 → 종막 카드(4행) → 종막 선택(95행 — 영웅) → 게임 끝(97행). 사람 혼자 하는 판이 종막까지 가는 길이다(PlayMode 흐름 시험).
        /// </summary>
        public static readonly SoloTrail Heart = new SoloTrail("heart", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareHeart };

        /// <summary>
        /// 막 길(셋업 뒤 상태를 손본 판): 종막 앞 — 태홍을 이긴 좌석 0에게 종막 선택(영웅·배신자)이 열린 판(<see cref="PrepareEnding"/>).
        /// 튜토리얼 막 맛보기 7-3(07 §4-3)이 쓴다 — 태홍 배틀(명단 6)을 대신 두면 길어 그 뒤에서 연다.
        /// </summary>
        public static readonly SoloTrail Ending = new SoloTrail("ending", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareEnding };

        /// <summary>
        /// 셋업이 끝난 판(첫 맵 행동이 열린 호스트 원본의 사본)을 손보는 함수 — 막 길만(<see cref="Lake"/>·<see cref="Heart"/>). 없으면 null.
        /// 손본 판은 그 상태로 새 세션을 연다(<c>LocalSession.FromState</c> — 저장하지 않는다). 2막 이후를 실제 진행으로 가려면 판 하나가 수백 턴이라(봇 판 평균 명령 1만) 막 목표 앞에서 시작한다.
        /// </summary>
        public Action<Ctx, GameState> After { get; private set; }

        /// <summary>
        /// 튜토리얼 각본의 막 앞 판 손보기 이름(<c>start.after</c>, 07 T25) → <see cref="After"/>. 모르는 이름은 null. 화면(<c>TutorialMenu</c>)과 시험이 같이 쓰는 한 곳 —
        /// 각본에 새 막 길을 쓰면 여기에 더한다.
        /// </summary>
        public static Action<Ctx, GameState> AfterNamed(string name)
        {
            foreach (var t in new[] { Lake, Heart, Ending, Shrine, Fate, FateGiratina, Workshop, Market, Duo, ShrineLegend, Fracture, Stamp, Duel, Swarm, CardSwap, UbBoard, UbBattle, UbTrial })
                if (t.Name == name) return t.After;
            return null;
        }

        /// <summary>막 길의 파티(좌석 0): 전설 다섯 + 스타팅 포켓몬(맨 뒤) — 간부·태홍의 명단을 이기는 판을 찾기 쉽게.</summary>
        public static readonly string[] Team = { "483:2", "484:2", "487:2", "468:2", "389:2" };

        static void Advance(Ctx ctx, GameState s, int act, int held, bool heart)
        {
            var fx = new Emitter(s, null);
            while (s.Hos.Act < act) SetupFlow.RevealAct(s, ctx, fx, -1);
            var st = s.Seats[0];
            // 1막 해방(A1): 고향의 조무래기 토큰이 빠졌다
            st.ActProgress = held == SeatState.ShardCount ? 2 : 1;
            if (s.Hos.FirstSeat[1] < 0) s.Hos.FirstSeat[1] = 0;
            if (held == SeatState.ShardCount && s.Hos.FirstSeat[2] < 0) s.Hos.FirstSeat[2] = 0;
            int home = ctx.Fixtures.Fixtures[st.HomeVillage].Cell;
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.Grunt && t.Cell == home) t.Cell = -1;
            for (int i = 0; i < held; i++) st.Shards[i] = true;
            st.OpenSlots = CampaignRules.OpenPartySlots(s.Campaign, held);
            st.Heart = heart;
            // 파티: 전설 다섯을 앞에, 스타팅 포켓몬을 맨 뒤에
            int starter = st.Party[0];
            for (int i = 0; i < st.Party.Length; i++) st.Party[i] = -1;
            foreach (string key in Team)
                if (PokemonRules.PartyCount(st) < st.OpenSlots - (starter >= 0 ? 1 : 0))
                    PokemonRules.AddToParty(st, PokemonRules.Spawn(s, ctx.Cards.PokemonIds.IndexOf(key), 0, (int)DeckId.Legendary));
            if (starter >= 0) PokemonRules.AddToParty(st, starter);
        }

        static void PrepareLake(Ctx ctx, GameState s)
        {
            Advance(ctx, s, 2, 2, false);
            s.Seats[0].Cell = HosFlow.LakeCell(ctx, Shard.Willpower);
        }

        static void PrepareHeart(Ctx ctx, GameState s)
        {
            Advance(ctx, s, 3, SeatState.ShardCount, false);
            var st = s.Seats[0];
            st.Cell = HosFlow.OriginShrineCell(ctx);
            if (st.Apricorns[(int)ApricornColor.Red] < 1) st.Apricorns[(int)ApricornColor.Red] = 1;
        }

        /// <summary>
        /// 확인용(캡처 — 좌석 여럿인 판 포함): 셋업이 끝난 판을 종막 선택 앞으로 — 태홍을 이긴 좌석 0에게 영웅·배신자를 묻는다(종막 카드는 공개된 것으로).
        /// 다른 좌석에는 파티를 맞춰 준다(배신자 연전 상대).
        /// </summary>
        public static void PrepareEnding(Ctx ctx, GameState s)
        {
            Advance(ctx, s, 4, SeatState.ShardCount, true);
            s.Seats[0].ActProgress = 3;
            s.Hos.FirstSeat[3] = 0;
            s.Seats[0].Cell = HosFlow.TempleCell(ctx);
            for (int i = 1; i < s.SeatCount; i++)
            {
                var st = s.Seats[i];
                st.ActProgress = 1;
            }
            s.Pending.Clear();
            s.ActiveSeat = 0;
            var fx = new Emitter(s, null);
            fx.Begin(0, ActionKind.ActReveal, AutoKind.Auto);
            HosFlow.OpenEnding(s, ctx, fx, 0);
        }

        /// <summary>기술 카드 풀·가방도 쥐여 주는 판인가(<see cref="Kit"/>).</summary>
        public bool WithKit { get; private set; }

        /// <summary>
        /// 손본 판(제작·은행·진화 화면 확인용 — M5, <see cref="Defeat"/>와 같은 길): 규토리 일곱 색 <see cref="WorkApricorns"/>개씩 · 여행 점수 <see cref="WorkJp"/> ·
        /// 파티 첫 칸에 이브이(진화 갈래 R14 — 스타팅 포켓몬은 그 뒤 칸). 첫 턴부터 제작 · 은행상회 · 진화를 모두 할 수 있다.
        /// </summary>
        public static readonly SoloTrail Work = new SoloTrail("work", 3, 0, 0, EncounterTier.Weak, 1, 0) { WithWork = true };

        public const int WorkApricorns = 3, WorkJp = 20;

        /// <summary>제작·은행·진화 확인용으로 손본 판인가(<see cref="Work"/>).</summary>
        public bool WithWork { get; private set; }

        /// <summary>좌석 0에 규토리 · 여행 점수와 이브이를 쥐여 준다(<see cref="Work"/>).</summary>
        public static void GiveWork(Ctx ctx, GameState s)
        {
            var st = s.Seats[0];
            for (int c = 0; c < st.Apricorns.Length; c++) st.Apricorns[c] = WorkApricorns;
            st.Jp = WorkJp;
            int eevee = -1;
            for (int i = 0; i < ctx.Cards.Pokemon.Count; i++)
                if (ctx.Cards.Pokemon[i].Key == "133:0") eevee = i;
            if (eevee >= 0) PokemonRules.AddToParty(st, PokemonRules.Spawn(s, eevee, 0, (int)DeckId.Weak));
        }

        // ── 튜토리얼 확장 장(07 §5 — M6.5)의 막 앞 판 손보기(<see cref="After"/> — 셋업이 끝난 판, 각본 start.after 이름) ──

        /// <summary>제작·진화 장(E2 · E4): <see cref="GiveWork"/>(규토리 일곱 색 셋씩 · 여행 점수 20 · 이브이)를 건 판.</summary>
        public static readonly SoloTrail Workshop = new SoloTrail("workshop", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = GiveWork };

        /// <summary>
        /// 은행상회·공중날기 장(E3): <see cref="GiveWork"/>에 더해 좌석 0을 조무래기가 없는 마을 <see cref="MarketVillage"/>에 세운 판 —
        /// 공중날기는 출발·도착 마을 모두 조무래기가 없어야 한다(A5 — 고향 마을에는 서막의 조무래기가 있다).
        /// </summary>
        public static readonly SoloTrail Market = new SoloTrail("market", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareMarket };

        public const string MarketVillage = "village.eterna";

        static void PrepareMarket(Ctx ctx, GameState s)
        {
            GiveWork(ctx, s);
            s.Seats[0].Cell = ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf(MarketVillage)].Cell;
        }

        /// <summary>배틀 장(E6): 파티 둘째 칸에 이브이만 넣은 판(규토리·여행 점수는 그대로 — 교체 창에 고를 포켓몬이 있게).</summary>
        public static readonly SoloTrail Duo = new SoloTrail("duo", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareDuo };

        static void PrepareDuo(Ctx ctx, GameState s) =>
            PokemonRules.AddToParty(s.Seats[0], PokemonRules.Spawn(s, ctx.Cards.PokemonIds.IndexOf("133:0"), 0, (int)DeckId.Weak));

        /// <summary>좌석 0에 정비 확인용 기술 카드와 지닌물건을 쥐여 준다.</summary>
        public static void GiveKit(Ctx ctx, GameState s)
        {
            foreach (string key in KitMoves)
                for (int i = 0; i < ctx.Cards.Moves.Count; i++)
                    if (ctx.Cards.Moves[i].Key == key) s.Seats[0].StampMoves.Add(i);
            foreach (string key in KitItems)
                for (int i = 0; i < ctx.Items.Items.Count; i++)
                    if (ctx.Items.Items[i].Key == key) s.Seats[0].Bag.Add(i);
        }

        /// <summary>시작 상태를 손보는 함수(<c>SoloGame.Start</c>의 prepare) — 손볼 것이 없는 판이면 null.</summary>
        public Action<GameState> PrepareFor(Ctx ctx)
        {
            if (!Prepared && !WithKit && !WithWork) return null;
            return s =>
            {
                Prepare(s);
                if (WithKit) GiveKit(ctx, s);
                if (WithWork) GiveWork(ctx, s);
            };
        }

        public SoloPilot Pilot() => new SoloPilot { Tier = Tier, Hunts = Hunts, Finale = After != null && !Fates && !Shrines, Fates = Fates, Shrines = Shrines };

        /// <summary>사당 길인가(길잡이 <see cref="SoloPilot.Shrines"/>).</summary>
        public bool Shrines { get; private set; }

        /// <summary>
        /// 사당 길(M5 「사당·균열 화면」 — 셋업 뒤 손본 판, <see cref="After"/>): 풀밭 사당에 서서 규토리(빨강 2 · 분홍 1 · 검정 1) · 여행 점수 9로 시작.
        /// 사당 이벤트 덱 맨 위 = 뜻밖의 선물 → 천상의 균열 → 평온한 순간. [공물](분홍 = 2단계 + 여행 점수 7 → 3장) → 뜻밖의 선물(규토리 2개) → 천상의 균열(운명 1장 · 추종자 펄기아 →
        /// 혼자 따르니 펄기아 강림) → 남은 한 장은 버린다. 다음 턴 시작에 추종자 이동력 +1.
        /// </summary>
        public static readonly SoloTrail Shrine = new SoloTrail("shrine", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareShrine, Shrines = true };

        /// <summary>사당 길의 사당 · 덱 맨 위 카드(위에서부터).</summary>
        public const string ShrinePlace = "shrine.meadow_shrine";
        public static readonly string[] ShrineTop = { ShrineRules.UnexpectedGift, ShrineRules.CelestialFracture, ShrineRules.TranquilMoment };

        /// <summary>좌석 seat를 그 사당에 세우고 규토리 · 여행 점수를 쥐여 주고, 사당 이벤트 덱 맨 위를 top(위에서부터)으로 놓는다(확인용 — 좌석 여럿인 판에도 쓴다).</summary>
        public static void PrepareShrine(Ctx ctx, GameState s, int seat, params string[] top)
        {
            var st = s.Seats[seat];
            st.Cell = ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf(ShrinePlace)].Cell;
            for (int c = 0; c < st.Apricorns.Length; c++) st.Apricorns[c] = 0;
            st.Apricorns[(int)ApricornColor.Red] = 2;
            st.Apricorns[(int)ApricornColor.Pink] = 1;
            st.Apricorns[(int)ApricornColor.Black] = 1;
            st.Jp = 9;
            var deck = s.Decks[(int)DeckId.ShrineEvent];
            for (int i = top.Length - 1; i >= 0; i--)
            {
                int card = ShrineRules.CardKey(ctx, top[i]);
                if (deck.Remove(card)) deck.Add(card);   // 덱의 끝이 맨 위다
            }
        }

        static void PrepareShrine(Ctx ctx, GameState s) => PrepareShrine(ctx, s, 0, ShrineTop);

        /// <summary>운명 길인가(길잡이 <see cref="SoloPilot.Fates"/> — 막 길의 맵 행동 대신 사냥 길).</summary>
        public bool Fates { get; private set; }

        /// <summary>
        /// 운명 길(M5 「운명·응답 화면」 — 셋업 뒤 손본 판, <see cref="After"/>): 운명 카드 6장(디아루가 2 · 아르세우스 2 · 펄기아 · 기라티나)으로 시작해 곧바로 6장째 버리기(S30 — 길잡이는 손패의 마지막 종류) →
        /// 펄기아 1장 순간이동(14행) → 약한 나무 → 첫 공격의 굴린 뒤 창에서 디아루가 3장(아르세우스 와일드카드 — 43·45행) → 떨어진 별 4개라 다음 턴 시작에 1장(7행).
        /// </summary>
        public static readonly SoloTrail Fate = new SoloTrail("fate", 3, 0, 0, EncounterTier.Weak, 1, 0) { After = PrepareFate, Fates = true };

        /// <summary>운명 길(기라티나): 손패가 기라티나 1장 — 첫 공격의 굴리기 전 창에서 희생(44행).</summary>
        public static readonly SoloTrail FateGiratina = new SoloTrail("fate-giratina", 3, 0, 0, EncounterTier.Weak, 1, 0) { After = PrepareGiratina, Fates = true };

        static void GiveFate(Ctx ctx, GameState s, params FateCard[] cards)
        {
            var deck = s.Decks[(int)DeckId.ChangeOfFate];
            foreach (var c in cards)
            {
                int key = FateRules.CardKey(ctx, c);
                if (!deck.Remove(key)) continue;
                s.Seats[0].FateHand.Add(key);
            }
        }

        static void PrepareFate(Ctx ctx, GameState s)
        {
            GiveFate(ctx, s, FateCard.Dialga, FateCard.Dialga, FateCard.Arceus, FateCard.Arceus, FateCard.Palkia, FateCard.Giratina);
            s.FallenStars = FateRules.StarsPerCard;
            FateRules.Overflow(s, ctx, new Emitter(s, null));
        }

        static void PrepareGiratina(Ctx ctx, GameState s) => GiveFate(ctx, s, FateCard.Giratina);

        // ── 튜토리얼 확장 장 ②(M6.5 E7~E11 — 07 §5-2): 각본의 start.after가 이름으로 꽂는 셋업 뒤 손보기. 길잡이 설정은 쓰지 않는다(각본이 답을 정한다) ──

        /// <summary>E7 사당: 풀밭 사당(<see cref="PrepareShrine(Ctx, GameState, int, string[])"/>) · 덱 맨 위 = 불꽃의 노래(전설 이벤트 — 파이어 토큰) → 평온한 순간 → 숨겨진 보물.</summary>
        public static readonly SoloTrail ShrineLegend = new SoloTrail("shrine-legend", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareShrineLegend };

        public static readonly string[] ShrineLegendTop = { "shrine_event.song_of_fire", ShrineRules.TranquilMoment, ShrineRules.HiddenTreasure };

        static void PrepareShrineLegend(Ctx ctx, GameState s) => PrepareShrine(ctx, s, 0, ShrineLegendTop);

        /// <summary>E8 천상의 균열(좌석 여럿): 좌석 0을 풀밭 사당에 · 덱 맨 위 = 천상의 균열 한 장.</summary>
        public static readonly SoloTrail Fracture = new SoloTrail("fracture", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareFracture };

        static void PrepareFracture(Ctx ctx, GameState s) => PrepareShrine(ctx, s, 0, ShrineRules.CelestialFracture);

        /// <summary>E9 여행 스탬프: 파티 셋(스타팅 + 약한 포켓몬 둘 — 둘이 지닌물건을 하나씩) · 가방에 지닌물건 하나 — 셋째를 지니게 하면 스탬프 「지닌물건 셋」.</summary>
        public static readonly SoloTrail Stamp = new SoloTrail("stamp", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareStamp };

        public static readonly string[] StampMons = { "399:0", "418:0" };

        static void PrepareStamp(Ctx ctx, GameState s)
        {
            var st = s.Seats[0];
            for (int i = 0; i < StampMons.Length; i++)
            {
                int id = PokemonRules.Spawn(s, ctx.Cards.PokemonIds.IndexOf(StampMons[i]), 0, (int)DeckId.Weak);
                PokemonRules.AddToParty(st, id);
                s.Pokemon[id].HeldItem = ctx.Items.Ids.IndexOf(KitItems[i]);
            }
            st.Bag.Add(ctx.Items.Ids.IndexOf(KitItems[2]));
        }

        /// <summary>E10 도전·거래(둘): 두 좌석이 좌석 0 고향 옆 칸(마을 밖)에 · 좌석 0 파티 앞에 전설 하나 · 좌석 1에 유물(화산의 돌) · 파랑 규토리 2 · 운명 2장(디아루가 · 아르세우스).</summary>
        public static readonly SoloTrail Duel = new SoloTrail("duel", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareDuel };

        static void PrepareDuel(Ctx ctx, GameState s)
        {
            int home = ctx.Fixtures.Fixtures[s.Seats[0].HomeVillage].Cell, cell = -1;
            for (int d = 0; d < 6 && cell < 0; d++)
            {
                int n = ctx.Board.Neighbor(home, d);
                if (n >= 0 && FixtureAt(ctx, n) < 0) cell = n;
            }
            s.Seats[0].Cell = s.Seats[1].Cell = cell;
            Lead(ctx, s, 0, Team[0]);
            s.Seats[1].Bag.Add(ctx.Items.Ids.IndexOf("item.magma_stone"));
            s.Seats[1].Apricorns[(int)ApricornColor.Blue] = 2;
            var deck = s.Decks[(int)DeckId.ChangeOfFate];
            foreach (var c in new[] { FateCard.Dialga, FateCard.Arceus })
            {
                int key = FateRules.CardKey(ctx, c);
                if (deck.Remove(key)) s.Seats[1].FateHand.Add(key);
            }
        }

        /// <summary>E11 무리 배틀: 좌석 0을 깊은늪 게이트(숲)에 · 파티 앞에 전설 하나.</summary>
        public static readonly SoloTrail Swarm = new SoloTrail("swarm", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareSwarm };

        public const string SwarmGate = "gate.deepmarsh_main";

        static void PrepareSwarm(Ctx ctx, GameState s)
        {
            s.Seats[0].Cell = ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf(SwarmGate)].Cell;
            Lead(ctx, s, 0, Team[0]);
        }

        // ── 튜토리얼 확장 장 ③(M6.5 E12~E15 — 07 §5-2): 위와 같은 꼴(각본의 start.after가 이름으로 꽂는다) ──

        /// <summary>
        /// E12 트레이너 카드 능력: 내 카드(셋업에서 고른 반바지 꼬마)를 실버로(파랑 해금 — 상시 보정을 다시 센다) · 강한 나무(검정 1 — 숲)에 · 파티 앞에 펄기아(물) · 그 뒤에 히드런(불꽃) ·
        /// 트레이너 카드 덱 맨 위 = 캠프파이어(kindler — 반바지 꼬마 빨강이 뽑을 5장의 첫 장).
        /// </summary>
        public static readonly SoloTrail CardSwap = new SoloTrail("card-swap", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareCardSwap };

        public const string CardSwapTree = "apricorn_tree.black_1", CardSwapLead = "485:0", CardSwapNext = "kindler";

        static void PrepareCardSwap(Ctx ctx, GameState s)
        {
            var st = s.Seats[0];
            st.TrainerCardTier = TrainerRules.Silver;
            st.Cell = ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf(CardSwapTree)].Cell;
            Lead(ctx, s, 0, CardSwapLead);
            Lead(ctx, s, 0, Team[1]);
            var deck = s.Decks[(int)DeckId.TrainerCard];
            int next = ctx.Cards.TrainerCardIds.IndexOf(CardSwapNext);
            if (deck.Remove(next)) deck.Add(next);   // 덱의 끝이 맨 위다
            TrainerRules.Refresh(s, ctx);
        }

        /// <summary>
        /// E13 울트라 버스트 ① 보드(좌석 둘): 몇 라운드가 지난 판 — 나무 <see cref="UbBoardDestroyed"/>그루가 이미 파괴됐다(균열 9% — 다음 UB 턴에 10% 구간을 넘어 균열 카드가 온다).
        /// 파괴할 나무는 토큰 순서의 뒤에서부터(시드와 상관없이 같은 나무).
        /// </summary>
        public static readonly SoloTrail UbBoard = new SoloTrail("ub-board", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareUbBoard };

        public const int UbBoardDestroyed = 12;

        static void PrepareUbBoard(Ctx ctx, GameState s)
        {
            int n = 0;
            for (int i = s.Tokens.Count - 1; i >= 0 && n < UbBoardDestroyed; i--)
            {
                var t = s.Tokens[i];
                if (t.Kind != TokenKind.ApricornTree || t.Cell < 0 || t.Destroyed) continue;
                t.Destroyed = true;
                n++;
            }
            UbTurn.Breach(s);
        }

        /// <summary>
        /// E14 울트라 버스트 ② 배틀(좌석 둘): 울트라 온슬롯 켬 · 가장 약한 UB(페로코체)를 깊은 늪 게이트(본 세계) 칸으로 옮기고 좌석 0을 그 칸에 · 파티 앞에 토게키스(페어리·비행 — 페로코체에게 상성이 좋다) · 그 뒤에 디아루가.
        /// 다음 내 턴에 게이트로 깨어진 세계에 건너가 두 칸 걸으면 네크로즈마 칸이다.
        /// </summary>
        public static readonly SoloTrail UbBattle = new SoloTrail("ub-battle", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareUbBattle };

        public const string UbBattleFoe = "795:0";

        static void PrepareUbBattle(Ctx ctx, GameState s)
        {
            s.Options.UltraOnslaught = true;
            int gate = ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf(SwarmGate)].Cell;
            int foe = ctx.Cards.PokemonIds.IndexOf(UbBattleFoe);
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.UltraBeast && t.Def == foe) t.Cell = gate;
            s.Seats[0].Cell = gate;
            Lead(ctx, s, 0, Team[0]);
            Lead(ctx, s, 0, Team[3]);
        }

        /// <summary>E15 울트라 버스트 ③ 시련(혼자): 좌석 0을 입지호수 동굴(아그놈 — 의지의 시련)에 · 파티 앞에 펄기아.</summary>
        public static readonly SoloTrail UbTrial = new SoloTrail("ub-trial", 3, 0, 0, EncounterTier.Weak, 0, 0) { After = PrepareUbTrial };

        public const string UbTrialSite = "legendary_site.lake_valor_cavern";

        static void PrepareUbTrial(Ctx ctx, GameState s)
        {
            s.Seats[0].Cell = ctx.Fixtures.Fixtures[ctx.Fixtures.Ids.IndexOf(UbTrialSite)].Cell;
            Lead(ctx, s, 0, Team[1]);
        }

        // 파티 맨 앞에 그 포켓몬(전설 덱)을 넣고 원래 파티를 뒤로
        static void Lead(Ctx ctx, GameState s, int seat, string key)
        {
            var st = s.Seats[seat];
            var old = new List<int>();
            foreach (int id in st.Party)
                if (id >= 0) old.Add(id);
            for (int i = 0; i < st.Party.Length; i++) st.Party[i] = -1;
            PokemonRules.AddToParty(st, PokemonRules.Spawn(s, ctx.Cards.PokemonIds.IndexOf(key), 0, (int)DeckId.Legendary));
            foreach (int id in old) PokemonRules.AddToParty(st, id);
        }

        static int FixtureAt(Ctx ctx, int cell)
        {
            for (int f = 0; f < ctx.Fixtures.Fixtures.Count; f++)
                if (ctx.Fixtures.Fixtures[f].Cell == cell) return f;
            return -1;
        }

        /// <summary>손본 판인가(시작 상태를 고친다).</summary>
        public bool Prepared => Balls > 0;

        /// <summary>시작 상태를 손본다(<c>SoloGame.Start</c>의 prepare). 손볼 것이 없는 판이면 부르지 않는다.</summary>
        public void Prepare(GameState s)
        {
            if (Balls > 0) s.Seats[0].Balls[(int)Ball.Regular] = Balls;
        }
    }
}
