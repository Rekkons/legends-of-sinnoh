using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    // ── 이벤트(01 §3-7 「캠페인」·「카드」) ──

    /// <summary>
    /// 사당 공물(p.17): Seat = 바친 좌석, Source = 그 사당(Shrine, 고정물 색인), ShrineKey = 같은 고정물, Tier = 바친 규토리 등급 1~3, Extra = 여행 점수 7을 더 내고 3장을 뽑았다.
    /// Changes = 낸 규토리·JP(시작의 사당 3막 공물이면 규토리는 앞선 ApricornSpent가 싣고 여기는 JP만).
    /// </summary>
    public sealed class ShrineOffered : GameEvent
    {
        public int ShrineKey = U;
        public int Tier = U;
        public bool Extra;
        public ResChange[] Changes;
    }

    /// <summary>사당 이벤트 카드를 뽑음(공개). CardKeys = 뽑은 카드(카드 표 색인, 1장 또는 3장) — 곧바로 버림 더미에 들고 처리·고르기는 그 뒤 이벤트가 말한다.</summary>
    public sealed class ShrineCardsDrawn : GameEvent
    {
        public int Tier = U;
        public int[] CardKeys;
    }

    /// <summary>
    /// 사당의 축복 한 번(A19): Seat = 효과를 받는 좌석, Source = 그 카드(ShrineEvent), From = 공물을 바친 좌석(나눠 받았으면 Seat ≠ From), Tier = 단계.
    /// Changes = 곧바로 바뀐 것(JP · 볼). 고르는 것(규토리 색 · 교환 · 기술 · 스튜)은 그 답의 이벤트가 싣는다.
    /// </summary>
    public sealed class ShrineBlessing : GameEvent
    {
        public int CardKey = U;
        public int From = U;
        public int Tier = U;
        public ResChange[] Changes;
    }

    /// <summary>
    /// 전설 이벤트(p.18): Source = 그 카드, ShrineKey = 전설 사당(고정물), Species = 놓을 전설(포켓몬 표 색인), Cell = 그 사당 칸, Placed = 토큰을 놓았다
    /// (그 전설이 이미 잡히거나 쓰러져 게임에서 빠졌으면 false), ReplacedSpecies = 바꿔 치운 전설(달빛공터 다크라이 ↔ 크레세리아, 없으면 −1). 규토리는 앞선 ApricornsGained.
    /// </summary>
    public sealed class LegendEvent : GameEvent
    {
        public int ShrineKey = U;
        public int Species = U;
        public int Cell = U;
        public bool Placed;
        public int ReplacedSpecies = U;
    }

    /// <summary>동등한 교환(사당의 축복): 그 포켓몬 카드가 From → To(포켓몬 표 색인)로 바뀌었다. 붙은 것(기술·지닌물건·볼·우두머리 토큰·피해·상태)은 그대로. FromDiscard = 버림 더미에서 찾았다.</summary>
    public sealed class PokemonExchanged : GameEvent
    {
        public int PokemonId = U;
        public int From = U;
        public int To = U;
        public bool FromDiscard;
    }

    /// <summary>
    /// 처리 중인 사당 공물(GameState.Shrine, 없으면 null) — 선택을 사이에 두고 이어 갈 값. Options = 아직 고르지 않은 뽑은 카드(3장 뽑기), Picks = 더 고를 장수,
    /// Card = 지금 처리하는 카드(없으면 −1), Stage = 그 카드의 단계(<see cref="ShrineRules"/>), SharedTo = 나눠 받는 좌석(A19, 없으면 −1).
    /// </summary>
    public sealed class ShrineState : IStatePart<ShrineState>
    {
        public int Seat = -1;
        public int Tier;
        public bool Extra;
        public List<int> Options = new List<int>();
        public int Picks;
        public int Card = -1;
        public int Stage;
        public int SharedTo = -1;

        public void CopyFrom(ShrineState o)
        {
            Seat = o.Seat;
            Tier = o.Tier;
            Extra = o.Extra;
            StateParts.CopyInts(Options, o.Options);
            Picks = o.Picks;
            Card = o.Card;
            Stage = o.Stage;
            SharedTo = o.SharedTo;
        }

        public void Walk(IStateWriter w)
        {
            w.Int(Seat);
            w.Int(Tier);
            StateParts.Bool(w, Extra);
            StateParts.Ints(w, Options);
            w.Int(Picks);
            w.Int(Card);
            w.Int(Stage);
            w.Int(SharedTo);
        }

        public void Read(IStateReader r)
        {
            Seat = r.Int();
            Tier = r.Int();
            Extra = StateParts.ReadBool(r);
            StateParts.ReadInts(r, Options);
            Picks = r.Int();
            Card = r.Int();
            Stage = r.Int();
            SharedTo = r.Int();
        }
    }

    /// <summary>
    /// 사당 공물과 사당 이벤트 카드 35장(룰북 p.17~18, 카드 shrine_event.json, rulings A19·A27, M5 「사당 공물·카드」).
    /// - 공물(MapAction Offer, Target = 사당 고정물, Arg = 바칠 규토리 색, Values = [7]이면 여행 점수 7을 더 내고 3장 뽑기): 사당·전설 사당(시작의 사당 포함) 칸에서, 상호작용할 수 있을 때(G11),
    ///   턴당 1번(p.17 「Once per turn」 — 어느 사당이든 합쳐 1번, UsageKind.ShrineOffer). 그 사당에 전설 토큰이 있거나 시작의 사당에 신 토큰이 있으면 못 바친다(p.18).
    ///   3막 시작의 사당 공물(심장 회복, HosFlow.RestoreHeart)도 같은 공물이라 카드를 뽑고 턴당 1번을 함께 센다(원작 커뮤니티 확인).
    /// - 단계 = 바친 규토리 등급(빨강·파랑·노랑 1 · 분홍·초록·하양 2 · 검정 3) — 카드의 「/」 값. 덱이 바닥나면 버림 더미를 섞는다. 뽑은 카드는 곧바로 버림 더미로(공개).
    /// - 3장 뽑기: 한 장 골라 처리 → 결과를 보고 다음 장(원작 커뮤니티 확인), 남은 한 장은 버린다(Reward/ShrinePick).
    /// - 사당의 축복 7종 × 3: 바친 좌석이 먼저, 4인 이상이면 카드마다 다른 좌석 하나를 골라(Reward/SharedBlessing) 같은 단계로 — 받은 좌석이 자기 것을 고른다(결정 75 ②).
    ///   운명적 조우 = 운명 1/2/3장 · 뜻밖의 선물 = 아무 색 1/2/3개(Reward/ShrineApricorns) · 동등한 교환 = 약한/적당한/강한 이하 파티 포켓몬 하나를 같은 등급 야생 카드로(ShrineExchange → ShrineExchangeCard) ·
    ///   고대의 지혜 = 적당한/강한/전설 이하 버림 더미 포켓몬의 기술 하나(ShrineMove → TeachMove) · 화려한 만찬 = 다른 스튜 1/2/3개를 공짜로(ShrineStews — 나눠 받은 좌석은 자기 다음 턴 시작에) ·
    ///   숨겨진 보물 = 수퍼볼/하이퍼볼/키스톤볼 · 평온한 순간 = JP 2/4/6.
    /// - 전설 이벤트 7: 모든 좌석이 그 규토리를 얻고, 전설 토큰을 그 사당에 놓는다(게임에서 빠진 전설이면 놓지 않는다). 나누지 않는다.
    /// - 천상의 균열 7: 모든 좌석이 운명 1장 → <see cref="FractureFollowers"/>(추종자 비밀 선택·공개·신 강림 — FractureRules, rulings A31).
    /// 처리 중 상태는 GameState.Shrine(<see cref="ShrineState"/>), 화려한 만찬을 나눠 받은 좌석은 사용 기록 Banquet(Key = 단계, Subject = 카드)으로 기다린다.
    /// </summary>
    public static class ShrineRules
    {
        /// <summary>3장 뽑기에 더 내는 여행 점수(p.17 「By additionally discarding 7 Journey Points」).</summary>
        public const int ExtraCost = 7;
        /// <summary>3장 뽑기: 뽑는 장수 · 고르는 장수.</summary>
        public const int ExtraDraw = 3, ExtraPicks = 2;
        /// <summary>나누는 인원(p.18 「When there are 4 or more trainers」).</summary>
        public const int ShareSeats = 4;

        public const string FatefulEncounter = "shrine_event.fateful_encounter";
        public const string UnexpectedGift = "shrine_event.unexpected_gift";
        public const string EquivalentExchange = "shrine_event.equivalent_exchange";
        public const string AncientWisdom = "shrine_event.ancient_wisdom";
        public const string SplendidBanquet = "shrine_event.splendid_banquet";
        public const string HiddenTreasure = "shrine_event.hidden_treasure";
        public const string TranquilMoment = "shrine_event.tranquil_moment";
        public const string CelestialFracture = "shrine_event.celestial_fracture";

        /// <summary>사당의 축복 7종(카드 데이터 순서).</summary>
        static readonly string[] Blessings = { FatefulEncounter, UnexpectedGift, EquivalentExchange, AncientWisdom, SplendidBanquet, HiddenTreasure, TranquilMoment };

        /// <summary>전설 이벤트 7: 카드 → (전설 사당 고정물, 전설 포켓몬 키, 모두가 얻는 규토리 색, 개수, 바꿔 치우는 전설 키 또는 null). 카드 문구 그대로(card_fronts_0.png #1~7).</summary>
        static readonly (string card, string shrine, string species, ApricornColor color, int count, string replaces)[] Legends =
        {
            ("shrine_event.song_of_frost", "legendary_shrine.frostsong_shrine", "144:0", ApricornColor.Blue, 3, null),
            ("shrine_event.song_of_thunder", "legendary_shrine.thundersong_shrine", "145:0", ApricornColor.Yellow, 3, null),
            ("shrine_event.song_of_fire", "legendary_shrine.firesong_shrine", "146:0", ApricornColor.Red, 3, null),
            ("shrine_event.visitor_from_afar", "legendary_shrine.timeless_shrine", "251:0", ApricornColor.Green, 2, null),
            ("shrine_event.shadow_emerges", "legendary_shrine.moonglade_shrine", "491:0", ApricornColor.Black, 1, "488:0"),
            ("shrine_event.beacon_of_light", "legendary_shrine.moonglade_shrine", "488:0", ApricornColor.Pink, 2, "491:0"),
            ("shrine_event.giant_awakens", "legendary_shrine.colossus_shrine", "486:0", ApricornColor.White, 2, null),
        };

        /// <summary>
        /// 전설 이벤트 카드면 true와 그 내용(화면이 카드 글을 적는다 — M5 「사당·균열 화면」): 전설(포켓몬 표 색인) · 그 전설 사당(고정물 색인) · 모두가 얻는 규토리 색 · 개수.
        /// </summary>
        public static bool LegendOf(Ctx ctx, int card, out int species, out int shrine, out ApricornColor color, out int count)
        {
            species = shrine = -1;
            color = ApricornColor.Red;
            count = 0;
            if (card < 0 || card >= ctx.Decks.CardIds.Count) return false;
            string key = KeyOf(ctx, card);
            foreach (var l in Legends)
            {
                if (l.card != key) continue;
                species = ctx.Cards.PokemonIds.IndexOf(l.species);
                shrine = ctx.Fixtures.Ids.IndexOf(l.shrine);
                color = l.color;
                count = l.count;
                return true;
            }
            return false;
        }

        // ShrineState.Stage: 바친 좌석이 처리 → (4인 이상 축복) 나눌 좌석 묻기 → 나눠 받은 좌석이 처리 → 끝
        const int StageSelf = 0, StageAfterSelf = 1, StageShareAsk = 2, StageShared = 3, StageDone = 4;

        const int Pile = (int)DeckId.ShrineEvent;

        public static int CardKey(Ctx ctx, string key) => ctx.Decks.CardIds.IndexOf(key);

        static string KeyOf(Ctx ctx, int card) => ctx.Decks.CardIds[card];

        public static bool IsBlessing(Ctx ctx, int card) => Array.IndexOf(Blessings, KeyOf(ctx, card)) >= 0;

        static SourceRef CardSource(int card) => new SourceRef(SourceKind.ShrineEvent, card);

        // ── 공물 ──

        /// <summary>그 칸의 사당(사당·전설 사당 고정물 색인 — 시작의 사당 포함, 파괴되지 않은 것). 없으면 −1.</summary>
        public static int ShrineAt(GameState s, int cell)
        {
            if (cell < 0) return -1;
            foreach (var t in s.Tokens)
                if ((t.Kind == TokenKind.Shrine || t.Kind == TokenKind.LegendaryShrine) && t.Cell == cell && !t.Destroyed) return t.Def;
            return -1;
        }

        /// <summary>그 칸에 전설 토큰이 있나(p.18 「You cannot make a Shrine Offering at their Shrine whilst the token is there」).</summary>
        public static bool LegendAt(GameState s, int cell)
        {
            foreach (var t in s.Tokens)
                if (t.Kind == TokenKind.Legend && t.Cell == cell && cell >= 0 && !t.Destroyed) return true;
            return false;
        }

        /// <summary>
        /// 그 좌석이 지금 칸의 사당에 공물을 바칠 수 있나(바칠 규토리는 보지 않는다): 사당 칸이고, 이번 턴에 아직 안 바쳤고, 전설 토큰·(시작의 사당이면) 신 토큰이 없다.
        /// 상호작용 조건(G11)은 MapActions가 본다.
        /// </summary>
        public static bool CanOffer(GameState s, Ctx ctx, int seat, out int shrine)
        {
            var st = s.Seats[seat];
            shrine = ShrineAt(s, st.Cell);
            if (shrine < 0 || MapActions.Used(s, seat, UsageKind.ShrineOffer) || LegendAt(s, st.Cell)) return false;
            if (shrine == HosFlow.OriginShrine(ctx) && HosFlow.GodAtOrigin(s, ctx)) return false;
            return true;
        }

        /// <summary>
        /// MapAction의 공물 답(G9 원문 순서: 1장 뽑기 → 3장 뽑기, 그 안은 규토리 색 순서): Offer(사당, 색) · JP가 7 이상이면 Offer(사당, 색, Values [7]).
        /// interact = 상호작용할 수 있다(G11).
        /// </summary>
        public static void Legal(GameState s, Ctx ctx, int seat, bool interact, List<ChoiceAnswer> into)
        {
            if (!interact || !CanOffer(s, ctx, seat, out int shrine)) return;
            var st = s.Seats[seat];
            for (int color = 0; color < SeatState.ApricornColors; color++)
                if (st.Apricorns[color] > 0) into.Add(ChoiceAnswer.Of(AnswerKind.Offer, shrine, color));
            if (st.Jp < ExtraCost) return;
            for (int color = 0; color < SeatState.ApricornColors; color++)
            {
                if (st.Apricorns[color] <= 0) continue;
                var a = ChoiceAnswer.Of(AnswerKind.Offer, shrine, color);
                a.Values.Add(ExtraCost);
                into.Add(a);
            }
        }

        /// <summary>
        /// 공물 답 적용: 턴당 1번을 적고, 규토리(+ JP 7)를 내고(3막 시작의 사당이면 심장 회복 — HosFlow.RestoreHeart가 규토리를 낸다), 카드를 뽑아 처리한다.
        /// 다 처리하면 턴이 이어진다(MapAction을 다시 연다).
        /// </summary>
        public static void Offer(GameState s, Ctx ctx, Emitter fx, int seat, ChoiceAnswer a)
        {
            int shrine = a.Target, color = a.Arg;
            bool extra = a.Values.Count > 0;
            var st = s.Seats[seat];
            MapActions.Mark(s, fx, seat, UsageKind.ShrineOffer);
            var changes = new List<ResChange>();
            if (shrine == HosFlow.OriginShrine(ctx) && HosFlow.CanRestoreHeart(s, ctx, seat)) HosFlow.RestoreHeart(s, ctx, fx, seat, color);
            else
            {
                int before = st.Apricorns[color];
                st.Apricorns[color]--;
                changes.Add(new ResChange { Seat = seat, Res = ResKind.Apricorn, Key = color, Before = before, After = before - 1 });
                TrainerRules.OfferingTaken(s, ctx, fx, seat, color); // delinquent B: 남이 바친 규토리를 얻는다(rulings A38)
            }
            if (extra)
            {
                int before = st.Jp;
                st.Jp -= ExtraCost;
                changes.Add(new ResChange { Seat = seat, Res = ResKind.Jp, Key = -1, Before = before, After = st.Jp });
            }
            int tier = MapActions.ApricornGrade((ApricornColor)color);
            if (fx.On) fx.Add(new ShrineOffered { Seat = seat, Source = new SourceRef(SourceKind.Shrine, shrine), ShrineKey = shrine, Tier = tier, Extra = extra, Changes = changes.ToArray() });

            var drawn = new List<int>(ExtraDraw + 1);
            int count = (extra ? ExtraDraw : 1) + TrainerRules.ShrineKeeper(s, ctx, fx, seat); // expert B: 1장 더 뽑고 쓰는 수는 그대로(rulings A39)
            for (int i = 0; i < count; i++)
            {
                int card = Draw(s, fx);
                if (card < 0) break;
                drawn.Add(card);
            }
            // 공개되고 처리 뒤 버린다 — 처리하는 동안에도 버림 더미에 둔다(카드 수 보존). 다 뽑은 뒤에 넣는다: 3장을 뽑는 동안 덱이 바닥나 섞는 버림 더미는
            // 그 전에 버린 카드뿐이다(p.17 「draw 3 cards in total and choose 2 of them」 — 방금 뽑은 카드가 다시 뽑히지 않는다, M5 규칙 끝 검토 반영)
            s.Discards[Pile].AddRange(drawn);
            if (fx.On) fx.Add(new ShrineCardsDrawn { Seat = seat, Tier = tier, CardKeys = drawn.ToArray() });
            if (drawn.Count == 0)
            {
                MapActions.Continue(s, ctx, fx, seat);
                return;
            }
            s.Shrine = new ShrineState { Seat = seat, Tier = tier, Extra = extra, Picks = extra ? ExtraPicks : 1 };
            s.Shrine.Options.AddRange(drawn);
            Next(s, ctx, fx);
        }

        static int Draw(GameState s, Emitter fx)
        {
            var deck = s.Decks[Pile];
            if (deck.Count == 0)
            {
                var discards = s.Discards[Pile];
                if (discards.Count == 0) return -1;
                deck.AddRange(discards);
                discards.Clear();
                Piles.Shuffle(deck, s.Rng);
                if (fx.On) fx.Add(new PileShuffled { Pile = DeckId.ShrineEvent });
            }
            int card = deck[deck.Count - 1];
            deck.RemoveAt(deck.Count - 1);
            return card;
        }

        // ── 진행 ──

        /// <summary>
        /// 처리 중인 공물을 이어 간다: 선택을 열면 멈추고(그 답이 다시 부른다), 끝나면 상태를 비우고 턴을 잇는다.
        /// 다른 묶음이 이 흐름 안에서 연 선택(「천상의 균열」 추종자 고르기)을 다 처리한 뒤에도 이것을 부른다.
        /// </summary>
        public static void Resume(GameState s, Ctx ctx, Emitter fx)
        {
            if (s.Shrine != null) Next(s, ctx, fx);
        }

        static void Next(GameState s, Ctx ctx, Emitter fx)
        {
            var sh = s.Shrine;
            for (int guard = 0; ; guard++)
            {
                if (guard > 64) throw new InvalidOperationException("사당 처리가 멈추지 않는다");
                if (sh.Card < 0)
                {
                    if (sh.Picks <= 0 || sh.Options.Count == 0)
                    {
                        Finish(s, ctx, fx);
                        return;
                    }
                    if (AllSame(sh.Options)) // 한 장뿐이거나 같은 카드뿐이면 고를 것이 없다
                    {
                        Take(sh, sh.Options[0]);
                        continue;
                    }
                    var pick = Choice(ChoiceSub.ShrinePick, sh.Seat, CardSource(-1));
                    pick.Options.AddRange(sh.Options);
                    ChoiceRules.Open(s, ctx, fx, pick);
                    return;
                }
                switch (sh.Stage)
                {
                    case StageSelf:
                        sh.Stage = StageAfterSelf;
                        if (Resolve(s, ctx, fx, sh.Seat, false)) return;
                        break;
                    case StageAfterSelf:
                        if (IsBlessing(ctx, sh.Card) && s.SeatCount >= ShareSeats)
                        {
                            sh.Stage = StageShareAsk;
                            ChoiceRules.Open(s, ctx, fx, Choice(ChoiceSub.SharedBlessing, sh.Seat, CardSource(sh.Card)));
                            return;
                        }
                        sh.Stage = StageDone;
                        break;
                    case StageShared:
                        sh.Stage = StageDone;
                        if (Resolve(s, ctx, fx, sh.SharedTo, true)) return;
                        break;
                    case StageDone:
                        sh.Card = -1;
                        sh.SharedTo = -1;
                        sh.Stage = StageSelf;
                        break;
                    default:
                        throw new InvalidOperationException($"사당 처리 단계 {sh.Stage}에서 멈춰 있어야 한다(선택 대기)");
                }
            }
        }

        static bool AllSame(List<int> l)
        {
            foreach (int x in l)
                if (x != l[0]) return false;
            return true;
        }

        static void Take(ShrineState sh, int card)
        {
            sh.Card = card;
            sh.Stage = StageSelf;
            sh.Options.Remove(card);
            sh.Picks--;
            if (sh.Picks <= 0) sh.Options.Clear(); // 고르지 않은 카드는 버린다(이미 버림 더미에 있다)
        }

        static void Finish(GameState s, Ctx ctx, Emitter fx)
        {
            int seat = s.Shrine.Seat;
            s.Shrine = null;
            MapActions.Continue(s, ctx, fx, seat);
        }

        static PendingChoice Choice(ChoiceSub sub, int seat, SourceRef src) => new PendingChoice { Kind = ChoiceKind.Reward, Sub = sub, Seat = seat, Source = src };

        // ── 카드 효과 ──

        /// <summary>지금 카드(sh.Card)를 그 좌석에게 처리한다. 선택을 열었으면 true(그 답이 Next를 부른다).</summary>
        static bool Resolve(GameState s, Ctx ctx, Emitter fx, int seat, bool shared)
        {
            var sh = s.Shrine;
            int card = sh.Card, tier = sh.Tier;
            string key = KeyOf(ctx, card);
            var src = CardSource(card);
            var st = s.Seats[seat];
            switch (key)
            {
                case FatefulEncounter:
                    Blessed(fx, seat, sh, null);
                    FateRules.Deal(s, ctx, fx, src, seat, tier); // 「Draw 1/2/3 Change of Fate cards」 — 넘치면 A12 버리기(뒤 선택은 미뤄진다)
                    return false;
                case UnexpectedGift:
                {
                    Blessed(fx, seat, sh, null);
                    var c = Choice(ChoiceSub.ShrineApricorns, seat, src);
                    c.Count = tier;
                    ChoiceRules.Open(s, ctx, fx, c);
                    return true;
                }
                case EquivalentExchange:
                {
                    Blessed(fx, seat, sh, null);
                    var c = Choice(ChoiceSub.ShrineExchange, seat, src);
                    c.Count = tier;
                    if (Legal(s, ctx, c).Count <= 1) return false; // 바꿀 것이 없다(G3)
                    ChoiceRules.Open(s, ctx, fx, c);
                    return true;
                }
                case AncientWisdom:
                {
                    Blessed(fx, seat, sh, null);
                    var c = Choice(ChoiceSub.ShrineMove, seat, src);
                    c.Count = tier;
                    c.Options.AddRange(WisdomMoves(s, ctx, tier)); // 뽑은 때의 버림 더미 — 그 뒤 놓아준 포켓몬의 기술은 안 된다(원작 커뮤니티 확인)
                    if (Legal(s, ctx, c).Count <= 1) return false; // 배울 기술이 없다(G3 — 원작 커뮤니티 확인)
                    ChoiceRules.Open(s, ctx, fx, c);
                    return true;
                }
                case SplendidBanquet:
                    Blessed(fx, seat, sh, null);
                    if (shared)
                    {
                        // p.19 「When giving this effect to another trainer, they can choose the Apricorn Stews they want when it is their turn」
                        s.Used.Add(new UsageMark { Seat = seat, Kind = UsageKind.Banquet, Key = tier, Scope = -1, Subject = card });
                        return false;
                    }
                    ChoiceRules.Open(s, ctx, fx, Stews(ctx, seat, tier, card, false));
                    return true;
                case HiddenTreasure:
                {
                    int ball = tier; // 1/2/3 = 수퍼볼/하이퍼볼/키스톤볼(Ball 값)
                    int before = st.Balls[ball];
                    st.Balls[ball]++;
                    Blessed(fx, seat, sh, new[] { new ResChange { Seat = seat, Res = ResKind.Ball, Key = ball, Before = before, After = st.Balls[ball] } });
                    return false;
                }
                case TranquilMoment:
                {
                    int before = st.Jp;
                    st.Jp += 2 * tier;
                    Blessed(fx, seat, sh, new[] { new ResChange { Seat = seat, Res = ResKind.Jp, Key = -1, Before = before, After = st.Jp } });
                    return false;
                }
                case CelestialFracture:
                {
                    // 「All trainers draw 1 Change of Fate card」 — 바친 좌석부터 시계 방향
                    var seats = new List<int>(s.SeatCount);
                    for (int i = 0; i < s.SeatCount; i++) seats.Add((seat + i) % s.SeatCount);
                    FateRules.Deal(s, ctx, fx, src, seats, 1);
                    return FractureFollowers(s, ctx, fx, seat);
                }
                default:
                    foreach (var l in Legends)
                        if (l.card == key)
                        {
                            Legend(s, ctx, fx, seat, card, l.shrine, l.species, l.color, l.count, l.replaces);
                            return false;
                        }
                    throw new InvalidOperationException($"사당 이벤트 카드 {key}");
            }
        }

        static void Blessed(Emitter fx, int seat, ShrineState sh, ResChange[] changes)
        {
            if (fx.On) fx.Add(new ShrineBlessing { Seat = seat, Source = CardSource(sh.Card), CardKey = sh.Card, From = sh.Seat, Tier = sh.Tier, Changes = changes ?? new ResChange[0] });
        }

        /// <summary>
        /// 천상의 균열의 이어 받는 고리: 운명 1장을 나눈 뒤 추종자 비밀 선택(Simultaneous/Follower)을 열고 → 공개 → 최다 신 토큰을 시작의 사당에 둔다(<see cref="FractureRules"/>, rulings A31).
        /// 선택을 열었으니 true — 그 묶음(과 동률 고르기)을 다 처리한 뒤 FractureRules가 <see cref="Resume"/>을 부른다.
        /// </summary>
        static bool FractureFollowers(GameState s, Ctx ctx, Emitter fx, int seat) => FractureRules.Open(s, ctx, fx, seat, s.Shrine.Card);

        /// <summary>
        /// 전설 이벤트(p.18 「All trainers gain the Apricorns specified on the card. Find the corresponding Legendary Pokémon token and place it on the Shrine specified on the card.」):
        /// 모든 좌석(바친 좌석부터 시계 방향)이 규토리를 얻고(ApricornsGained), 전설 토큰을 그 사당에 놓는다. 그 전설이 게임에서 빠졌으면(잡히거나 쓰러져 전설 덱에 없다 —
        /// p.18 「The token is removed from the game once the Legendary Pokémon is defeated or captured」) 놓지 않는다. 이미 그 사당에 있으면 그대로. 달빛공터는 다른 쪽 토큰을 치운다.
        /// </summary>
        static void Legend(GameState s, Ctx ctx, Emitter fx, int seat, int card, string shrineKey, string speciesKey, ApricornColor color, int count, string replacesKey)
        {
            var changes = new List<ResChange>(s.SeatCount);
            for (int i = 0; i < s.SeatCount; i++)
            {
                int who = (seat + i) % s.SeatCount;
                int before = s.Seats[who].Apricorns[(int)color];
                s.Seats[who].Apricorns[(int)color] += count;
                changes.Add(new ResChange { Seat = who, Res = ResKind.Apricorn, Key = (int)color, Before = before, After = before + count });
            }
            if (fx.On) fx.Add(new ApricornsGained { Seat = seat, Source = CardSource(card), Changes = changes.ToArray() });

            int shrine = ctx.Fixtures.Ids.IndexOf(shrineKey);
            int cell = ctx.Fixtures.Fixtures[shrine].Cell;
            int species = ctx.Cards.PokemonIds.IndexOf(speciesKey);
            int replaced = -1;
            bool placed = s.Decks[(int)DeckId.Legendary].Contains(species) && ShrineAt(s, cell) == shrine;
            if (placed)
            {
                TokenState token = null;
                foreach (var t in s.Tokens)
                    if (t.Kind == TokenKind.Legend && t.Def == species) token = t;
                if (replacesKey != null)
                {
                    int other = ctx.Cards.PokemonIds.IndexOf(replacesKey);
                    foreach (var t in s.Tokens)
                    {
                        if (t.Kind != TokenKind.Legend || t.Def != other || t.Cell != cell) continue;
                        t.Cell = -1;
                        replaced = other;
                        if (fx.On) fx.Add(new TokenRemoved { Seat = seat, Cell = cell, TokenKind = TokenKind.Legend });
                    }
                }
                if (token == null) s.Tokens.Add(new TokenState { Kind = TokenKind.Legend, Def = species, Cell = cell, FaceUp = true });
                else token.Cell = cell;
            }
            if (fx.On) fx.Add(new LegendEvent { Seat = seat, Source = CardSource(card), ShrineKey = shrine, Species = species, Cell = cell, Placed = placed, ReplacedSpecies = replaced });
        }

        static PendingChoice Stews(Ctx ctx, int seat, int tier, int card, bool later)
        {
            var c = Choice(ChoiceSub.ShrineStews, seat, CardSource(card));
            c.Count = Math.Min(tier, StewItems(ctx).Count);
            c.Subject = later ? 1 : 0;
            return c;
        }

        static List<int> StewItems(Ctx ctx)
        {
            var r = new List<int>(4);
            for (int i = 0; i < ctx.Items.Items.Count; i++)
                if (ctx.Items.Items[i].Kind == ItemKind.Stew) r.Add(i);
            return r;
        }

        // ── 화려한 만찬을 나눠 받은 좌석의 턴 시작 ──

        /// <summary>
        /// 턴 시작(TurnFlow.StartTurn, 맵 행동을 열기 전): 화려한 만찬을 나눠 받아 기다리는 좌석이면 스튜를 고르게 한다(Reward/ShrineStews, Subject = 1). 물었으면 true —
        /// 답이 맵 행동을 연다. 원작 커뮤니티 확인.
        /// </summary>
        public static bool TurnStarted(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            for (int i = 0; i < s.Used.Count; i++)
            {
                var u = s.Used[i];
                if (u.Kind != UsageKind.Banquet || u.Seat != seat) continue;
                s.Used.RemoveAt(i);
                ChoiceRules.Open(s, ctx, fx, Stews(ctx, seat, u.Key, u.Subject, true));
                return true;
            }
            return false;
        }

        // ── 선택 ──

        static List<ChoiceAnswer> Legal(GameState s, Ctx ctx, PendingChoice c)
        {
            var l = new List<ChoiceAnswer>();
            ChoiceRules.Legal(s, ctx, c, l);
            return l;
        }

        /// <summary>이 묶음의 Reward 선택의 합법 답(G9). 순서는 각 갈래 설명.</summary>
        public static void Legal(GameState s, Ctx ctx, PendingChoice c, List<ChoiceAnswer> into)
        {
            switch (c.Sub)
            {
                case ChoiceSub.ShrinePick:
                    // 뽑은 카드 중 하나(뽑은 순서, 같은 카드는 한 번)
                    for (int i = 0; i < c.Options.Count; i++)
                        if (c.Options.IndexOf(c.Options[i]) == i) into.Add(ChoiceAnswer.Pick(c.Options[i]));
                    break;
                case ChoiceSub.ShrineApricorns:
                    // 아무 색 Count개(같은 색 여러 개 됨 — 원작 커뮤니티 확인). Values = 색 오름차순(빨강 3개가 첫 답)
                    Multisets(SeatState.ApricornColors, c.Count, v => into.Add(ChoiceAnswer.PickMany(v)));
                    break;
                case ChoiceSub.ShrineStews:
                {
                    // 서로 다른 스튜 Count개(「You cannot choose the same stew effect more than once」). Values = 아이템 색인 오름차순(데이터 순서)
                    var stews = StewItems(ctx);
                    Subsets(stews.Count, c.Count, idx =>
                    {
                        var v = new List<int>(idx.Count);
                        foreach (int i in idx) v.Add(stews[i]);
                        into.Add(ChoiceAnswer.PickMany(v));
                    });
                    break;
                }
                case ChoiceSub.ShrineExchange:
                    // 안 함(「You may」) → 바꿀 수 있는 파티 포켓몬(파티 순서): 등급이 단계 이하(약한/적당한/강한 — 스타터는 약한)이고 그 조우 덱이나 버림 더미에 카드가 있다
                    // (장수만 본다 — 덱 내용은 보기에서 가린다. 조우 덱의 카드는 모두 그 등급이다)
                    into.Add(ChoiceAnswer.Of(AnswerKind.Decline));
                    foreach (int p in s.Seats[c.Seat].Party)
                    {
                        int grade = p >= 0 ? ExchangeGrade(ctx, s.Pokemon[p], c.Count) : -1;
                        if (grade < 0 || BreachRules.Any(s.Pokemon[p])) continue; // 균열 카드가 붙은 포켓몬은 파티를 떠나지 못한다(p.24)
                        int deck = (int)MapActions.DeckOf((EncounterTier)grade);
                        if (s.Decks[deck].Count + s.Discards[deck].Count > 0) into.Add(ChoiceAnswer.Pick(p));
                    }
                    break;
                case ChoiceSub.ShrineExchangeCard:
                    // 안 함 → 그 등급 야생 카드(열 때 Options = [카드, 더미(0 덱 · 1 버림 더미)] 짝): Pick(카드, 더미)
                    into.Add(ChoiceAnswer.Of(AnswerKind.Decline));
                    for (int i = 0; i + 1 < c.Options.Count; i += 2) into.Add(ChoiceAnswer.Pick(c.Options[i], c.Options[i + 1]));
                    break;
                case ChoiceSub.ShrineMove:
                    // 안 함 → 버림 더미 포켓몬(단계 이하 등급)의 기술(열 때 Options, 기술 색인 순서) — 파티에 배울 수 있는 포켓몬이 있는 것만
                    into.Add(ChoiceAnswer.Of(AnswerKind.Decline));
                    foreach (int m in c.Options)
                        if (AnyLearns(s, ctx, c.Seat, m)) into.Add(ChoiceAnswer.Pick(m));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(c.Sub), c.Sub, "사당 선택이 아니다");
            }
        }

        /// <summary>이 묶음이 Legal을 맡는 Reward 갈래인가(ChoiceRules.RewardOptions가 묻는다).</summary>
        public static bool Owns(ChoiceSub sub) =>
            sub == ChoiceSub.ShrinePick || sub == ChoiceSub.ShrineApricorns || sub == ChoiceSub.ShrineStews || sub == ChoiceSub.ShrineExchange
            || sub == ChoiceSub.ShrineExchangeCard || sub == ChoiceSub.ShrineMove;

        /// <summary>
        /// 사당 카드가 연 Reward 선택의 답(Engine — Source = ShrineEvent): 고르기 · 규토리 · 스튜 · 교환 · 기술(TeachMove) · 나눌 좌석(SharedBlessing). 끝나면 처리를 잇는다.
        /// 묻는 사이 정비로 대상이 없어졌으면 그 일은 건너뛴다(G3).
        /// </summary>
        public static void ApplyReward(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            var src = c.Source;
            switch (c.Sub)
            {
                case ChoiceSub.ShrinePick:
                    Take(s.Shrine, a.Target);
                    break;
                case ChoiceSub.SharedBlessing:
                    s.Shrine.SharedTo = a.Target;
                    s.Shrine.Stage = StageShared;
                    break;
                case ChoiceSub.ShrineApricorns:
                {
                    var st = s.Seats[c.Seat];
                    var changes = new List<ResChange>();
                    var colors = new List<ApricornColor>(a.Values.Count);
                    foreach (int v in a.Values) colors.Add((ApricornColor)v);
                    CraftRules.MoveApricorns(st, c.Seat, colors, +1, changes);
                    if (fx.On) fx.Add(new ApricornsGained { Seat = c.Seat, Source = src, Changes = changes.ToArray() });
                    break;
                }
                case ChoiceSub.ShrineStews:
                    // 공짜 스튜: 만든 스튜의 「턴당 1번」을 쓰지 않는다(만찬으로 먹고 같은 스튜를 또 만들 수 있다 — 원작 커뮤니티 확인, 만든 것과 겹쳐도 된다 — 원작 커뮤니티 확인)
                    foreach (int item in a.Values) CraftRules.Eat(s, ctx, fx, c.Seat, item, true);
                    if (c.Subject == 1)
                    {
                        if (!TurnStarted(s, ctx, fx, c.Seat)) MapActions.Continue(s, ctx, fx, c.Seat);
                        return;
                    }
                    break;
                case ChoiceSub.ShrineExchange:
                {
                    if (a.Kind != AnswerKind.Pick || PokemonRules.PartyIndexOf(s.Seats[c.Seat], a.Target) < 0) break;
                    int grade = ExchangeGrade(ctx, s.Pokemon[a.Target], c.Count);
                    var cards = grade >= 0 ? ExchangeCards(s, ctx, grade) : new List<int>();
                    if (cards.Count == 0) break;
                    var next = Choice(ChoiceSub.ShrineExchangeCard, c.Seat, src);
                    next.Subject = a.Target;
                    next.Count = grade;
                    next.Options.AddRange(cards);
                    ChoiceRules.Open(s, ctx, fx, next);
                    return;
                }
                case ChoiceSub.ShrineExchangeCard:
                    if (a.Kind == AnswerKind.Pick) Exchange(s, ctx, fx, c.Seat, c.Subject, (EncounterTier)c.Count, a.Target, a.Arg == 1, src);
                    break;
                case ChoiceSub.ShrineMove:
                {
                    if (a.Kind != AnswerKind.Pick) break;
                    var teach = Choice(ChoiceSub.TeachMove, c.Seat, src);
                    teach.Subject = a.Target;
                    if (Legal(s, ctx, teach).Count <= 1) break;
                    ChoiceRules.Open(s, ctx, fx, teach);
                    return;
                }
                case ChoiceSub.TeachMove:
                    if (a.Kind == AnswerKind.Pick && PokemonRules.PartyIndexOf(s.Seats[c.Seat], a.Target) >= 0)
                    {
                        var p = s.Pokemon[a.Target];
                        int old = p.Moves[a.Arg];
                        p.Moves[a.Arg] = c.Subject;
                        EvolveRules.Forgot(s, p, old);
                        if (fx.On) fx.Add(new MoveTaught { Seat = c.Seat, Source = src, PokemonId = a.Target, Slot = a.Arg, MoveKey = c.Subject, ReplacedKey = old });
                    }
                    break;
                default:
                    throw new InvalidOperationException($"사당 선택 {c.Sub}");
            }
            if (s.Shrine != null) Next(s, ctx, fx);
        }

        // ── 동등한 교환 ──

        /// <summary>등급 순위(「up to」 비교): 스타터·약한 1 · 적당한 2 · 강한 3 · 전설 4 · 그 밖(UB·갤럭시단) 99.</summary>
        static int Rank(EncounterTier t)
        {
            switch (t)
            {
                case EncounterTier.Starter:
                case EncounterTier.Weak: return 1;
                case EncounterTier.Moderate: return 2;
                case EncounterTier.Strong: return 3;
                case EncounterTier.Legendary: return 4;
                default: return 99;
            }
        }

        /// <summary>
        /// 그 포켓몬을 동등한 교환으로 바꿀 수 있으면 그 등급(약한·적당한·강한), 아니면 −1. 단계 1/2/3 = 「up to a Weak/Moderate/Strong」. 우두머리는 카드가 강한 등급이라 단계 3에서 된다
        /// (원작 커뮤니티 확인). 전설은 안 된다.
        /// </summary>
        static int ExchangeGrade(Ctx ctx, PokemonState p, int tier)
        {
            var t = ctx.Cards.Pokemon[p.Def].Tier;
            int r = Rank(t);
            if (r > 3 || r > tier) return -1;
            return (int)(t == EncounterTier.Starter ? EncounterTier.Weak : t);
        }

        /// <summary>
        /// 그 등급의 야생 카드: 그 조우 덱(덱 → 버림 더미, 원작 커뮤니티 확인 p.19 · 원작 커뮤니티 확인), 더미 안은 카드 색인 오름차순, 같은 카드는 한 번.
        /// 돌려주는 값 = [카드, 더미(0 덱 · 1 버림 더미)] 짝.
        /// </summary>
        static List<int> ExchangeCards(GameState s, Ctx ctx, int grade)
        {
            int deck = (int)MapActions.DeckOf((EncounterTier)grade);
            var r = new List<int>();
            for (int pile = 0; pile < 2; pile++)
            {
                var cards = new List<int>();
                foreach (int card in pile == 0 ? s.Decks[deck] : s.Discards[deck])
                    if ((int)MapActions.GradeOf(ctx, card) == grade && !cards.Contains(card)) cards.Add(card);
                cards.Sort();
                foreach (int card in cards)
                {
                    r.Add(card);
                    r.Add(pile);
                }
            }
            return r;
        }

        /// <summary>
        /// 교환: 고른 카드를 더미에서 빼고 그 포켓몬 카드가 된다 — 붙은 것은 그대로(「whilst keeping everything else attached」: 배운 기술은 배울 수 없는 타입이어도 · 우두머리 토큰 — p.19,
        /// 지닌물건 · 볼 · 밑에 둔 옛 카드 · 피해 · 상태). 옛 카드는 새 카드를 꺼낸 쪽(덱 / 버림 더미)의 제 덱으로(원작 커뮤니티 확인) — 덱이면 섞는다.
        /// 피해는 새 최대 체력을 넘지 않게 자른다 [가정].
        /// </summary>
        static void Exchange(GameState s, Ctx ctx, Emitter fx, int seat, int pokemon, EncounterTier grade, int card, bool fromDiscard, SourceRef src)
        {
            if (PokemonRules.PartyIndexOf(s.Seats[seat], pokemon) < 0) return;
            int deck = (int)MapActions.DeckOf(grade);
            var from = fromDiscard ? s.Discards[deck] : s.Decks[deck];
            if (!from.Remove(card)) return;
            var p = s.Pokemon[pokemon];
            int old = p.Def;
            if (p.Deck >= 0)
            {
                var back = fromDiscard ? s.Discards[p.Deck] : s.Decks[p.Deck];
                back.Add(old);
                if (!fromDiscard)
                {
                    Piles.Shuffle(back, s.Rng);
                    if (fx.On) fx.Add(new PileShuffled { Pile = (DeckId)p.Deck });
                }
            }
            p.Def = card;
            p.Deck = deck;
            p.FormBeforeBattle = -1;
            p.Damage = Math.Min(p.Damage, PokemonRules.MaxHp(ctx, p));
            if (fx.On) fx.Add(new PokemonExchanged { Seat = seat, Source = src, PokemonId = pokemon, From = old, To = card, FromDiscard = fromDiscard });
            TrainerRules.Obtained(s, ctx, fx, seat, pokemon, ObtainCause.CardEffect); // 트레이너 카드 해금 「obtain」(rulings A20-3 — 등가 교환)
        }

        // ── 고대의 지혜 ──

        /// <summary>
        /// 배울 수 있는 기술(기술 색인 오름차순): 포켓몬 덱들의 버림 더미에 있는 카드 중 등급이 단계 1/2/3 = 적당한/강한/전설 이하(「up to」 — 더 낮은 등급도, 원작 커뮤니티 확인)인 것의
        /// 시그니처 기술, 여러 폼 카드면 다른 폼의 시그니처도(원작 커뮤니티 확인).
        /// 카드는 버림 더미에 그대로 둔다(배틀 뒤 기술 가르치기처럼 기술만 옮긴다) [가정].
        /// </summary>
        static List<int> WisdomMoves(GameState s, Ctx ctx, int tier)
        {
            var moves = new List<int>();
            var piles = new[] { DeckId.Starter, DeckId.Weak, DeckId.Moderate, DeckId.Strong, DeckId.Evolution, DeckId.Legendary };
            foreach (var d in piles)
                foreach (int card in s.Discards[(int)d])
                {
                    var def = ctx.Cards.Pokemon[card];
                    if (Rank(def.Tier) > tier + 1) continue;
                    if (!moves.Contains(def.Signature)) moves.Add(def.Signature);
                    if (def.State < 1) continue;
                    foreach (var other in ctx.Cards.Pokemon)
                        if (other.Dex == def.Dex && other.State >= 1 && !moves.Contains(other.Signature)) moves.Add(other.Signature);
                }
            moves.Sort();
            return moves;
        }

        static bool AnyLearns(GameState s, Ctx ctx, int seat, int move)
        {
            var type = ctx.Cards.Moves[move].Type;
            foreach (int p in s.Seats[seat].Party)
                if (p >= 0 && ChoiceRules.CanLearn(ctx, s.Pokemon[p], type)) return true;
            return false;
        }

        // ── 조합 ──

        /// <summary>0..n−1에서 k개 중복 조합(오름차순), 사전 순.</summary>
        static void Multisets(int n, int k, Action<List<int>> f)
        {
            var v = new List<int>(k);
            void Go(int from)
            {
                if (v.Count == k)
                {
                    f(new List<int>(v));
                    return;
                }
                for (int i = from; i < n; i++)
                {
                    v.Add(i);
                    Go(i);
                    v.RemoveAt(v.Count - 1);
                }
            }
            Go(0);
        }

        /// <summary>0..n−1에서 k개 조합(오름차순), 사전 순.</summary>
        static void Subsets(int n, int k, Action<List<int>> f)
        {
            var v = new List<int>(k);
            void Go(int from)
            {
                if (v.Count == k)
                {
                    f(new List<int>(v));
                    return;
                }
                for (int i = from; i < n; i++)
                {
                    v.Add(i);
                    Go(i + 1);
                    v.RemoveAt(v.Count - 1);
                }
            }
            Go(0);
        }
    }
}
