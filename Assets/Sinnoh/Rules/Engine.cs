using System;
using System.Collections.Generic;
using System.Text;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 정적 데이터 묶음(01 §3-1 Ctx). 엔진은 정적 메서드뿐이고 숨은 인스턴스 상태가 없어, 복제한 상태에 같은 Ctx로 그대로 돌린다.
    /// 만들 때 DeckId마다 덱 키("deck." + snake_case)와 덱 종류가 맞는지 확인한다.
    /// </summary>
    public sealed class Ctx
    {
        public readonly Board Board;
        public readonly CardDb Cards;
        public readonly DeckDb Decks;
        public readonly FixtureDb Fixtures;
        public readonly ItemDb Items;
        /// <summary>효과 정의(01 §7-1, effects.json + 상태이상 기본 효과, M3-f).</summary>
        public readonly EffectDb Effects;

        readonly int[] deckIndex;

        public Ctx(Board board, CardDb cards, DeckDb decks, FixtureDb fixtures, ItemDb items, EffectDb effects)
        {
            Board = board ?? throw new ArgumentNullException(nameof(board));
            Cards = cards ?? throw new ArgumentNullException(nameof(cards));
            Decks = decks ?? throw new ArgumentNullException(nameof(decks));
            Fixtures = fixtures ?? throw new ArgumentNullException(nameof(fixtures));
            Items = items ?? throw new ArgumentNullException(nameof(items));
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
            if (effects.MoveCount != cards.Moves.Count) throw new ArgumentException($"효과 표의 기술 수 {effects.MoveCount} ≠ 기술 표 {cards.Moves.Count}", nameof(effects));
            deckIndex = new int[GameState.DeckCount];
            for (int i = 0; i < deckIndex.Length; i++)
            {
                var id = (DeckId)i;
                int d = decks.DeckIds.IndexOf(DeckKey(id));
                if (decks.Decks[d].Kind != KindOf(id))
                    throw new ArgumentException($"{DeckKey(id)}: 덱 종류 {decks.Decks[d].Kind} ≠ {KindOf(id)}", nameof(decks));
                deckIndex[i] = d;
            }
        }

        public DeckDef Deck(DeckId id) => Decks.Decks[deckIndex[(int)id]];

        /// <summary>"deck." + snake_case(이름). 예: ChangeOfFate → deck.change_of_fate.</summary>
        public static string DeckKey(DeckId id)
        {
            string name = id.ToString();
            var sb = new StringBuilder("deck.", 5 + name.Length * 2);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>그 덱이 담는 카드의 표.</summary>
        public static DeckKind KindOf(DeckId id) =>
            id == DeckId.TrainerCard ? DeckKind.TrainerCard : id >= DeckId.Act ? DeckKind.Card : DeckKind.Pokemon;
    }

    /// <summary>더미 조작. 끝 = 맨 위(01 §2-2 Piles).</summary>
    public static class Piles
    {
        /// <summary>Fisher–Yates(끝에서 앞으로, j = rng.Next(i + 1)). 섞은 순서는 호스트 원본에만 남는다(01 §3-5).</summary>
        public static void Shuffle(List<int> pile, IRng rng)
        {
            if (pile == null) throw new ArgumentNullException(nameof(pile));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            for (int i = pile.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = pile[i];
                pile[i] = pile[j];
                pile[j] = t;
            }
            Rig.Current?.Shuffled(pile); // 튜토리얼 손보기(07 §6-1) — 평소 null
        }
    }

    /// <summary>
    /// 규칙 엔진 진입점(01 §3-1). 정적 메서드뿐이다. Submit·ViewFor·FilterFor·Legal은 EngineSubmit.cs(M3-c).
    /// </summary>
    public static partial class Engine
    {
        /// <summary>
        /// 새 게임 상태(01 §2-2). 좌석 1~6(결정 14 ②: 1인 솔로 포함), 표는 Ctx(M2 로더). 단계는 Setup이고 열린 선택은 아직 없다 —
        /// 셋업 진행(트레이너 카드 3장, 마을·스타터 고르기, 숨겨진 장소 배치 R3, 서막)은 바로 뒤에 부르는 Engine.Start(SetupFlow)가 한다.
        /// 만드는 것 [가정]:
        /// ① 좌석: 열린 파티 칸(A3: 신오의 심장 3, 울트라 버스트 6), 나머지는 빈 값.
        /// ② 캠페인 상태: 신오의 심장 = 막 미공개, 울트라 버스트 = 축복(A14)·프리즘 아머 9(UB 쪽).
        /// ③ 덱: decks.json 구성 × 장수. 고르는 덱(스타터·고향 마을)은 데이터 순서, 막은 데이터 순서로 서막이 맨 위, 나머지는 DeckId 순서로 섞는다
        ///    (사당·균열·은행·운명은 원작 setup_game이 섞는다, 트레이너 카드는 배분 전에 섞는다 A20-1, 조우 덱은 뽑을 때 또 섞는다 R1).
        /// ④ 토큰: 나무·산호·왜곡된 나무·사당·전설 사당은 고정물 자리에 앞면, 숨겨진 장소·유물·우두머리 토큰 52개는 섞은 순서로 보드 밖에 뒷면
        ///    (토큰 순번이 정체를 흘리지 않게).
        /// </summary>
        public static GameState NewGame(Ctx ctx, RuleOptions opt, int seatCount, ulong seed, Campaign campaign = Campaign.HeartOfSinnoh)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (opt == null) throw new ArgumentNullException(nameof(opt));
            if (seatCount < 1 || seatCount > SeatId.MaxSeats)
                throw new ArgumentOutOfRangeException(nameof(seatCount), seatCount, $"좌석은 1~{SeatId.MaxSeats}");

            var s = new GameState();
            s.Options.CopyFrom(opt);
            s.Campaign = campaign;
            s.Phase = Phase.Setup;
            s.Rng = new Pcg32(seed);

            s.Seats = new SeatState[seatCount];
            int openSlots = CampaignRules.OpenPartySlots(campaign, 0);
            for (int i = 0; i < seatCount; i++) s.Seats[i] = new SeatState { OpenSlots = openSlots };

            if (campaign == Campaign.HeartOfSinnoh) s.Hos = new HosState();
            else s.Ub = new UbState { Blessings = CampaignRules.StartingBlessings(opt.UbDifficulty) };

            BuildDecks(ctx, s);
            PlaceFixtureTokens(ctx, s);
            AddHiddenSpotTokens(ctx, s);
            return s;
        }

        static void BuildDecks(Ctx ctx, GameState s)
        {
            for (int i = 0; i < GameState.DeckCount; i++)
            {
                var id = (DeckId)i;
                var pile = s.Decks[i];
                foreach (var e in ctx.Deck(id).Cards)
                    for (int k = 0; k < e.Copies; k++) pile.Add(e.Card);
                switch (id)
                {
                    case DeckId.Starter:
                    case DeckId.HomeVillage:
                        break;
                    case DeckId.Act:
                        pile.Reverse(); // 끝 = 맨 위 = 데이터 첫 카드(서막)
                        break;
                    default:
                        Piles.Shuffle(pile, s.Rng);
                        break;
                }
            }
        }

        static void PlaceFixtureTokens(Ctx ctx, GameState s)
        {
            var fixtures = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
            {
                TokenKind kind;
                switch (fixtures[f].Kind)
                {
                    case FixtureKind.ApricornTree: kind = TokenKind.ApricornTree; break;
                    case FixtureKind.ApricornReef: kind = TokenKind.ApricornReef; break;
                    case FixtureKind.DistortedTree: kind = TokenKind.DistortedTree; break;
                    case FixtureKind.Shrine: kind = TokenKind.Shrine; break;
                    case FixtureKind.LegendaryShrine: kind = TokenKind.LegendaryShrine; break;
                    default: continue; // 마을·전설 장소·게이트·택시는 고정물(정적)
                }
                s.Tokens.Add(new TokenState { Kind = kind, Def = f, Cell = fixtures[f].Cell, FaceUp = true });
            }
        }

        static void AddHiddenSpotTokens(Ctx ctx, GameState s)
        {
            var defs = new List<int>();
            var items = ctx.Items.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var k = items[i].Kind;
                if (k != ItemKind.HiddenSpot && k != ItemKind.AlphaPokemon && k != ItemKind.Artifact && k != ItemKind.CelestialArtifact) continue;
                int copies = items[i].Copies ?? throw new InvalidOperationException($"{items[i].Key}: 셋업 토큰인데 개수가 없다");
                for (int c = 0; c < copies; c++) defs.Add(i);
            }
            Piles.Shuffle(defs, s.Rng);
            foreach (int d in defs) s.Tokens.Add(new TokenState { Kind = TokenKind.HiddenSpot, Def = d, Cell = -1, FaceUp = false, Special = items[d].Kind != ItemKind.HiddenSpot });
        }
    }
}
