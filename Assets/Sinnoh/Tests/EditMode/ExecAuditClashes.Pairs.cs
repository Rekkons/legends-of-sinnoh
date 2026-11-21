using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 실행 감사 ④ 충돌 장면 넓히기(2026-11-20): 배틀 창(우선권 · 굴리기 전 · 굴린 뒤 · 피해 직전 · 피해 뒤)의 후보 쌍을 <b>생성 장면</b>으로 모두 돌린다.
    /// 출처마다 조건(<see cref="Need"/> — 포켓몬 타입 · 등급 · 기술 타입 · 지닌물건 · 상태 · 묻는 답)을 적어 두고, 쌍마다 두 조건을 함께 맞추는 장면(트레이너전 좌석 0 → 1, 좌석 2는 배틀 밖)을
    /// 골라 「없음 · A만 · B만 · 둘 다」 넷을 돌린다. 단정:
    /// <list type="bullet">
    /// <item>각 출처의 몫(그 출처가 낸 이벤트 · 공격력 Mod · 피해 단계 · 우선도 Mod)이 「혼자」와 「둘 다」에서 같다 — 서로 영향 없음. 다르면 <see cref="Changed"/>(규칙상 바뀜, 근거) · <see cref="Known"/>(버그) 표에 있어야 한다.</item>
    /// <item>피해 직전: 최종 피해 = 없음 + 각자의 몫(더하기 · 빼기), 상한 · 문턱이 끼면 01 §6-4 순서(⑤ 감소 → ⑥ 문턱 → ⑦ 상한)대로.</item>
    /// <item>우선권: 좌석마다 순서 값 = 없음 + 각자의 몫, 먼저 움직이는 쪽 = 큰 쪽(같으면 우선권 토큰 · ninja_boy B는 토큰으로).</item>
    /// </list>
    /// 같은 순간에 설 수 없는 쌍(야생 배틀에서만 · 같은 카드 · 조건이 서로 어긋남)과 그 장면에서 출처가 일하지 않는 쌍은 까닭을 단다(<see cref="PairResult"/>).
    /// </summary>
    public static partial class ExecAuditClashes
    {
        public enum Verdict { Same, Changed, Known, Ask, Idle, Apart, Scene, Differs }

        public sealed class PairResult
        {
            public Spot Spot;
            public string Pair, Note;
            public Verdict Verdict;
            public bool Covered => Verdict == Verdict.Same || Verdict == Verdict.Changed || Verdict == Verdict.Known || Verdict == Verdict.Ask || Verdict == Verdict.Scene;
            public override string ToString() => $"{Spot}\t{Pair}\t{Verdict}\t{Note}";
        }

        static readonly PokemonType[] NoTypes = new PokemonType[0];

        sealed class Need
        {
            public PokemonType[] Own = NoTypes, OwnNot = NoTypes, FoeNot = NoTypes, Seat1 = NoTypes;
            public PokemonType? Move;
            /// <summary>기술 타입이 이 가운데 하나(날씨 타입 — Prep이 공격 기술 타입으로 날씨를 놓는다).</summary>
            public PokemonType[] MoveIn;
            /// <summary>내 포켓몬이 기술과 같은 타입(chef R 마스터워크).</summary>
            public bool SameType;
            /// <summary>기술이 받는 포켓몬에게 효과가 좋다(scientist R).</summary>
            public bool Effective;
            public EncounterTier? Tier;
            public string Species, Bench;
            /// <summary>내 출전 포켓몬이 지닐 물건. ItemSource면 그 물건이 출처(카드 없음 — 「안 씀」 = 안 지님), 아니면 조건이라 늘 지닌다.</summary>
            public string Item;
            public bool ItemSource;
            public FateCard? Fate;
            public Window FateWindow;
            /// <summary>Side 출처의 자리(−1 = 남는 쪽).</summary>
            public int Seat = -1;
            public AttackDieFace[] Faces;
            public Action<Arena, int, int> Prep;
            public Func<Arena, PendingChoice, List<ChoiceAnswer>, ChoiceAnswer> Ask;
            public int Cap, Threshold;
            /// <summary>쌍을 세우지 못하는 까닭(야생 배틀에서만 · 장면 밖).</summary>
            public string Wild, Skip;
            public Need Alt;
        }

        static readonly AttackDieFace[] Pattern = { C, C, S, R, R, R, R, R, R };
        // 시드는 9면 전체로 찾는다 — 드문 면이 길게 이어지면 시드가 없어 뒤는 일반 타격으로 채운다(공격력이 늘어 더 굴리는 주사위도 정해진 면)
        static readonly AttackDieFace[] Blanks = { S, S, S, S, S, R, R, R, R };
        static readonly AttackDieFace[] Crits = { C, C, C, C, C, R, R, R, R };
        static readonly AttackDieFace[] TwoSymbols = { S, S, C, R, R, R, R, R, R };
        static readonly Dictionary<string, ulong> Seeds = new Dictionary<string, ulong>();

        static Dictionary<string, Need> Needs(Ctx ctx, Spot spot)
        {
            ChoiceAnswer Max(PendingChoice c, List<ChoiceAnswer> legal)
            {
                ChoiceAnswer best = null;
                foreach (var a in legal)
                    if (a.Kind == AnswerKind.Pick && (best == null || a.Target > best.Target)) best = a;
                return best;
            }
            string God(SinnohGod g)
            {
                for (int i = 0; i < ctx.Cards.Pokemon.Count; i++)
                    if (LegendRules.GodOf(ctx, i) == g) return ctx.Cards.Pokemon[i].Key;
                return null;
            }
            Action<Arena, int, int> Chosen = (t, seat, mon) => t.S.Pokemon[mon].Chosen = true;
            const string wildOnly = "야생 배틀에서만(상대 쪽에 좌석이 없다)";
            const string ownFate = "제 운명 3장 셈(추종자 토큰 2장 몫) — 다른 좌석 출처의 조건 · 값을 바꾸지 않는다";
            var n = new Dictionary<string, Need>
            {
                // ── 우선권 ──
                { "ace_trainer.R", new Need { Tier = EncounterTier.Strong } },
                { "bug_catcher.R", new Need { Own = new[] { PokemonType.Bug }, Move = PokemonType.Bug } },
                { "diamond_acolyte.R", new Need { Species = God(SinnohGod.Dialga), Prep = Chosen } },
                { "pearl_acolyte.R", new Need { Species = God(SinnohGod.Palkia), Prep = Chosen } },
                { "platinum_acolyte.R", new Need { Species = God(SinnohGod.Giratina), Prep = Chosen } },
                { "dragon_tamer.R", new Need { Item = "item.apex_dragon" } },
                { "gardener.R", new Need { Own = new[] { PokemonType.Grass } } },
                { "hiker.R", new Need { Own = new[] { PokemonType.Ground } } },
                { "ninja_boy.B", new Need { Seat = 0 } },
                { "ninja_boy.R", new Need { Seat = 0 } },
                { "pokemon_breeder.R", new Need { Prep = (t, seat, mon) => t.S.Pokemon[mon].EggMove = true } },
                { "pokemon_ranger.R", new Need { Tier = EncounterTier.Moderate, Prep = (t, seat, mon) => t.S.Seats[seat].OpenSlots = Math.Max(t.S.Seats[seat].OpenSlots, CampaignRules.OpenPartySlots(t.S.Campaign, 0) + 2) } },
                { "swimmer.B", new Need { Own = new[] { PokemonType.Water } } },
                { "youngster.B", new Need { Tier = EncounterTier.Weak } },
                { "item.swiftness_sash", new Need { Item = "item.swiftness_sash", ItemSource = true } },
                // ── 굴리기 전 ──
                { "chef.R", new Need { SameType = true, Prep = (t, seat, mon) => { for (int c = 0; c < SeatState.ApricornColors; c++) t.S.Seats[seat].Apricorns[c] = 1; } } },
                { "fate.giratina", new Need { Fate = FateCard.Giratina, FateWindow = Window.BeforeRoll } },
                { "ginkgo_merchant.R", new Need { Prep = (t, seat, mon) => { for (int c = 0; c < SeatState.ApricornColors; c++) t.S.Seats[seat].Apricorns[c] = 1; }, Ask = (t, c, legal) => Max(c, legal) } },
                { "platinum_acolyte.B", new Need { Skip = ownFate } },
                { "swimmer.R", new Need { Move = PokemonType.Water, Ask = (t, c, legal) => legal.Find(a => a.Kind == AnswerKind.Pick && a.Target == (int)PokemonType.Bug) } },
                { "triathlete.R", new Need { Prep = Triathlon, Ask = (t, c, legal) => legal.Find(a => a.Kind == AnswerKind.Pick && a.Target == (int)PokemonType.Ice) } },
                { "team_aqua.R", new Need { Move = PokemonType.Water, Alt = new Need { Seat1 = new[] { PokemonType.Water } } } },
                { "team_magma.R", new Need { Move = PokemonType.Fire, Alt = new Need { Seat1 = new[] { PokemonType.Fire } } } },
                // ── 굴린 뒤 ──
                { "aroma_lady.B", new Need { Wild = "포획 굴림(나무)에서만 — " + wildOnly } },
                { "fisherman.B", new Need { Wild = "포획 굴림(산호)에서만 — " + wildOnly } },
                { "diamond_acolyte.B", new Need { Skip = ownFate } },
                { "guitarist.B", new Need { Prep = (t, seat, mon) => t.S.Seats[seat].Amp = 2, Ask = (t, c, legal) => Max(c, legal) } },
                { "psychic.R", new Need { Move = PokemonType.Psychic } },
                { "veteran.R", new Need { Move = PokemonType.Dragon, Ask = (t, c, legal) => Max(c, legal) } },
                { "fate.dialga", new Need { Fate = FateCard.Dialga, FateWindow = Window.AfterRoll } },
                { "item.lucky_egg", new Need { Item = "item.lucky_egg", ItemSource = true, Faces = TwoSymbols } },
                { "item.eviolite", new Need { Item = "item.eviolite", ItemSource = true, Tier = EncounterTier.Weak } },
                // ── 피해 직전 ──
                { "artist.R", new Need { Prep = Masterpiece } },
                { "beauty.B", new Need() },
                { "beauty.R", new Need { Move = PokemonType.Normal } },
                { "black_belt.R", new Need { Own = new[] { PokemonType.Fighting }, Prep = (t, seat, mon) => t.S.Pokemon[mon].Status = StatusEffect.Burned } },
                { "essentia.R", new Need { Wild = "움직이지 않고 건 야생 배틀의 첫 공격 — " + wildOnly } },
                { "guitarist.R", new Need { Move = PokemonType.Electric, Prep = (t, seat, mon) => t.S.Seats[seat].Amp = 2, Ask = (t, c, legal) => Max(c, legal) } },
                { "hex_maniac.B", new Need { Skip = "상태를 거는 op 앞 물음 — 피해 보정이 아니다(받는 쪽과 겹치는 것은 별 방패의 상태이상 면역 꼴 · 장면 C08)" } },
                { "hex_maniac.R", new Need { Move = PokemonType.Ghost } },
                { "kindler.R", new Need { Move = PokemonType.Fire, Prep = (t, seat, mon) => t.S.Seats[seat].Firewood = 2 } },
                { "parasol_lady.B", new Need { MoveIn = TrainerRules.WeatherTypes, Prep = (t, seat, mon) => { Weather(t); t.S.Seats[seat].CardItems.Add(Scene.Item(ctx, "item.weather_wheel")); } } },
                { "ruin_maniac.R", new Need { Move = PokemonType.Fire, Item = "item.magma_stone" } },
                { "scientist.R", new Need { Effective = true } },
                { "sky_trainer.R", new Need { Own = new[] { PokemonType.Flying }, FoeNot = new[] { PokemonType.Flying } } },
                { "item.choice_band", new Need { Item = "item.choice_band", ItemSource = true } },
                // 문턱은 혼자서 실제로 0으로 만들어야 일한다 — 짝의 상시 보정(화상 −2 · 상성)에 맞춰 굴림을 바꿔 본다
                { "bird_keeper.R", new Need { Own = new[] { PokemonType.Flying }, Threshold = 2, Faces = new[] { R, R, S, S, S, R, R, R, R },
                    Alt = new Need { Own = new[] { PokemonType.Flying }, Threshold = 2, Faces = new[] { R, S, S, S, S, R, R, R, R },
                        Alt = new Need { Own = new[] { PokemonType.Flying }, Threshold = 2, Faces = new[] { R, R, R, R, S, R, R, R, R },
                            Alt = new Need { Own = new[] { PokemonType.Flying }, Threshold = 2, Faces = Blanks } } } } },
                { "delinquent.R", new Need { Own = new[] { PokemonType.Dark } } },
                { "parasol_lady.R", new Need { MoveIn = TrainerRules.WeatherTypes, Prep = (t, seat, mon) => Weather(t) } },
                { "poke_fan.B", new Need { Tier = EncounterTier.Weak, Cap = 3 } },
                { "poke_maniac.R", new Need { Prep = (t, seat, mon) => t.S.Pokemon[Bench(t, seat)].HeldItem = Scene.Item(ctx, "item.substitute") } },
                { "item.substitute", new Need { Item = "item.substitute", ItemSource = true } },
                { "roller_skater.R", new Need { Cap = 6, Faces = Crits } },
                { "item.star_shield", new Need { Item = "item.star_shield", ItemSource = true } },
                { "item.blast_crystal", new Need { Item = "item.blast_crystal", ItemSource = true } },
                // ── 피해 뒤 ──
                { "aroma_lady.R", new Need { Move = PokemonType.Grass } },
                { "expert.R", new Need { Skip = "비스트 부스트(전설 포켓몬의 기술)가 돌 때 — 트레이너전 장면 밖" } },
                { "fairy_tale_girl.B", new Need { Skip = "제 포켓몬이 회복할 때만 — 이 창의 짝(다른 좌석 카드)은 fairy_tale_girl 좌석의 포켓몬을 회복시키지 않는다" } },
                { "fairy_tale_girl.R", new Need { Seat = 0, Own = new[] { PokemonType.Fairy }, Prep = (t, seat, mon) => t.S.Pokemon[mon].Status = StatusEffect.Drowsy } },
                { "roller_skater.B", new Need { Wild = "야생이 도망칠 때 — " + wildOnly } },
                { "scuba_diver.R", new Need { Move = PokemonType.Water, Prep = (t, seat, mon) => Battles.SetHp(t.S, ctx, Foe(t, seat), 1) } },
                { "team_rocket.R", new Need { Seat = 1, Prep = (t, seat, mon) => Battles.SetHp(t.S, ctx, mon, 1) } },
                { "undivided_acolyte.R", new Need { Faces = Blanks, Prep = (t, seat, mon) => Battles.SetHp(t.S, ctx, Foe(t, seat), 2) } },
                { "worker.R", new Need { Seat = 1, OwnNot = new[] { PokemonType.Steel }, Bench = "411:0", Prep = (t, seat, mon) => Battles.SetHp(t.S, ctx, mon, 1) } },
                // ── 배틀 뒤(트레이너전: 좌석 0이 좌석 1의 마지막 포켓몬을 쓰러뜨린다) ──
                { "artist.B", new Need { Wild = "야생을 쓰러뜨린 뒤 — " + wildOnly } },
                { "black_belt.B", new Need { Wild = "야생을 쓰러뜨린 뒤 — " + wildOnly } },
                { "bug_catcher.B", new Need { Wild = "벌레 타입을 잡은 뒤 — " + wildOnly } },
                { "gentleman.B", new Need { Wild = "야생을 잡거나 쓰러뜨린 뒤 — " + wildOnly } },
                { "backpacker.B", new Need { Seat = 0, Prep = (t, seat, mon) => t.S.Pokemon[mon].Damage = 2 } },
                { "collector.B", new Need { Seat = 0 } },
                { "essentia.B", new Need { Seat = 0, Prep = (t, seat, mon) => { t.S.ActiveSeat = seat; t.S.Seats[seat].MovesLeft = 3; } } },
                { "interviewers.B", new Need { Skip = "트레이너전 뒤 스탬프를 얻는 것은 이긴 좌석뿐이고 그 좌석은 곧바로 도전 보호라 묻지 않는다(장면 C16) — 야생 배틀 짝(JP · 기술 · 추가 턴 · 규토리)은 이 장면에서 스탬프를 만들지 않는다" } },
            };
            // 배틀 뒤의 ruin_maniac R(「cannot be taken by other trainers」)은 진 쪽 — 유물을 가방에
            if (spot == Spot.BattleEnd) n["ruin_maniac.R"] = new Need { Seat = 1, Prep = (t, seat, mon) => t.S.Seats[seat].Bag.Add(Scene.Item(ctx, "item.magma_stone")) };
            return n;
        }

        static int Bench(Arena t, int seat) => seat == 0 ? t.Mine[1] : t.Theirs[1];

        /// <summary>날씨를 공격 기술의 타입으로(Stage가 공격 기술을 좌석 0 출전 포켓몬의 기술 칸 0에 먼저 넣는다).</summary>
        static void Weather(Arena t)
        {
            int m = t.S.Pokemon[t.A].Moves[0];
            t.S.Weather = m >= 0 ? Array.IndexOf(TrainerRules.WeatherTypes, t.Ctx.Cards.Moves[m].Type) : -1;
        }
        static int Foe(Arena t, int seat) => seat == 0 ? t.D : t.A;

        /// <summary>triathlete R: 내 포켓몬이 세 타입의 기술을 안다(시그니처 · 공격 기술 · 얼음 기술 하나).</summary>
        static void Triathlon(Arena t, int seat, int mon)
        {
            int ice = FirstMove(t.Ctx, PokemonType.Ice);
            if (ice >= 0) t.S.Pokemon[mon].Moves[1] = ice;
        }

        static int FirstMove(Ctx ctx, PokemonType type)
        {
            for (int m = 0; m < ctx.Cards.Moves.Count; m++)
                if (ctx.Cards.Moves[m].Type == type && ctx.Cards.Moves[m].AttackStrength.HasValue) return m;
            return -1;
        }

        /// <summary>artist R 「a move of every type」: 파티를 채워 18타입을 덮는다(빈 몫은 타입없음 기술 — 와일드카드). 출전 포켓몬의 기술 칸 0은 공격 기술이 쓴다.</summary>
        static void Masterpiece(Arena t, int seat, int mon)
        {
            var ctx = t.Ctx;
            var st = t.S.Seats[seat];
            st.OpenSlots = st.Party.Length;
            var seen = new bool[TypeChart.TypeCount];
            int typeless = FirstMove(ctx, PokemonType.Typeless);
            void See(int m)
            {
                if (m >= 0) seen[(int)ctx.Cards.Moves[m].Type] = true;
            }
            foreach (int p in st.Party)
                if (p >= 0) See(ctx.Cards.Pokemon[t.S.Pokemon[p].Def].Signature);
            See(t.S.Pokemon[mon].Moves[0]);
            // 빈 칸은 시그니처 타입이 아직 없는 포켓몬으로
            while (Array.IndexOf(st.Party, -1) >= 0)
            {
                string key = Bidoof;
                for (int i = 0; i < ctx.Cards.Pokemon.Count; i++)
                {
                    var d = ctx.Cards.Pokemon[i];
                    if (d.Legendary || d.Key.IndexOf('@') >= 0 || d.Tier != EncounterTier.Weak) continue;
                    var ty = ctx.Cards.Moves[d.Signature].Type;
                    if (ty != PokemonType.Typeless && !seen[(int)ty]) { key = d.Key; break; }
                }
                See(ctx.Cards.Pokemon[t.S.Pokemon[Scene.Give(t.S, ctx, seat, key)].Def].Signature);
            }
            foreach (int p in st.Party)
            {
                if (p < 0) continue;
                for (int slot = p == mon ? 1 : 0; slot < PokemonState.MoveSlots; slot++)
                {
                    int pick = -1;
                    for (int ty = 1; ty < TypeChart.TypeCount && pick < 0; ty++)
                        if (!seen[ty]) pick = FirstMove(ctx, (PokemonType)ty);
                    if (pick < 0) pick = typeless;
                    t.S.Pokemon[p].Moves[slot] = pick;
                    See(pick);
                }
            }
        }

        // ── 장면 고르기 ──

        sealed class Plan
        {
            public string Att, Def, BenchA, BenchD;
            public int Move;
            public AttackDieFace[] Faces;
            public int SeatA, SeatB;
            public Need A, B;
        }

        static Role RoleOf(string key, Spot spot)
        {
            if (Card.TryGetValue(key, out var spots) || Other.TryGetValue(key, out spots))
                foreach (var (sp, role) in spots)
                    if (sp == spot) return role;
            return Role.Side;
        }

        static string CardKey(string source) => source.Substring(0, source.LastIndexOf('.'));

        static Plan Solve(Ctx ctx, Spot spot, string a, string b, Need na, Need nb, out string why)
        {
            why = null;
            var ra = RoleOf(a, spot);
            var rb = RoleOf(b, spot);
            int Fixed(Role r, Need n) => r == Role.Att ? 0 : r == Role.Def ? 1 : n.Seat;
            int sa = Fixed(ra, na), sb = Fixed(rb, nb);
            if (sa < 0 && sb < 0) { sa = 0; sb = 1; }
            else if (sa < 0) sa = 1 - sb;
            else if (sb < 0) sb = 1 - sa;
            if (sa == sb) { why = "같은 자리가 필요하다"; return null; }
            if (na.Faces != null && nb.Faces != null && na.Faces != nb.Faces) { why = "주사위 조건이 어긋난다"; return null; }
            if (na.Move != null && nb.Move != null && na.Move != nb.Move) { why = $"기술 타입이 어긋난다({na.Move} · {nb.Move})"; return null; }
            var must = new[] { new List<PokemonType>(), new List<PokemonType>() };
            var not = new[] { new List<PokemonType>(), new List<PokemonType>() };
            var tier = new EncounterTier?[2];
            var species = new string[2];
            var bench = new string[2];
            var same = new bool[2];
            bool eff = false;
            foreach (var (n, k) in new[] { (na, sa), (nb, sb) })
            {
                must[k].AddRange(n.Own);
                not[k].AddRange(n.OwnNot);
                not[1 - k].AddRange(n.FoeNot);
                must[1].AddRange(n.Seat1);
                if (n.Tier != null)
                {
                    if (tier[k] != null && tier[k] != n.Tier) { why = "등급이 어긋난다"; return null; }
                    tier[k] = n.Tier;
                }
                if (n.Species != null) species[k] = n.Species;
                if (n.Bench != null) bench[k] = n.Bench;
                same[k] |= n.SameType;
                if (n.Effective && k == 0) eff = true;
            }
            var fixedMove = na.Move ?? nb.Move;
            var types = new List<PokemonType>();
            if (fixedMove != null) types.Add(fixedMove.Value);
            else
            {
                types.Add(PokemonType.Normal);
                for (int ty = 1; ty < TypeChart.TypeCount; ty++)
                    if ((PokemonType)ty != PokemonType.Normal) types.Add((PokemonType)ty);
            }
            foreach (var n in new[] { na, nb })
                if (n.MoveIn != null) types.RemoveAll(ty => Array.IndexOf(n.MoveIn, ty) < 0);
            if (types.Count == 0) { why = $"기술 타입이 어긋난다({na.Move?.ToString() ?? "날씨"} · {nb.Move?.ToString() ?? "날씨"})"; return null; }
            foreach (var ty in types)
            {
                int move = PlainMove(ctx, ty);
                if (move < 0) continue;
                string att = Species(ctx, must[0], not[0], tier[0], species[0], same[0] ? ty : (PokemonType?)null, null, true);
                string def = Species(ctx, must[1], not[1], tier[1], species[1], same[1] ? ty : (PokemonType?)null, eff ? ty : (PokemonType?)null, false, ty);
                if (att == null || def == null) continue;
                return new Plan { Att = att, Def = def, BenchA = bench[0] ?? Turtwig, BenchD = bench[1] ?? def, Move = move, Faces = na.Faces ?? nb.Faces ?? Pattern, SeatA = sa, SeatB = sb, A = na, B = nb };
            }
            why = "포켓몬 · 기술 조건이 함께 맞지 않는다";
            return null;
        }

        static readonly string[] Busy = { "status", "Burn", "Poison", "Paralys", "Drowsy", "Confus", "Frozen", "Freeze", "switch", "Switch", "heal", "faint", "Faint", "fatigue", "roll the", "flee", "double", "type", "damage to", "instead" };

        /// <summary>그 타입의 공격 기술(공격력 3~6) 가운데 이번 굴림 · 피해에 덜 끼는 효과(상태 · 교체 · 회복 · 2배 · 타입 낱말이 없는 · 짧은 문구)부터 — 모든 기술에 효과 문구가 있다.</summary>
        static int PlainMove(Ctx ctx, PokemonType type)
        {
            int best = -1, score = int.MaxValue;
            for (int m = 0; m < ctx.Cards.Moves.Count; m++)
            {
                var d = ctx.Cards.Moves[m];
                if (d.Type != type || !d.AttackStrength.HasValue || d.AttackStrength < 3 || d.AttackStrength > 6) continue;
                int s = d.En.Length + Math.Abs(d.AttackStrength.Value - 5) * 40;
                foreach (var w in Busy)
                    if (d.En.IndexOf(w, StringComparison.Ordinal) >= 0) s += 1000;
                if (s < score) { score = s; best = m; }
            }
            return best;
        }

        static string Species(Ctx ctx, List<PokemonType> must, List<PokemonType> not, EncounterTier? tier, string key, PokemonType? sameType, PokemonType? weakTo, bool fast, PokemonType? moveType = null)
        {
            string best = null;
            int score = int.MinValue;
            for (int i = 0; i < ctx.Cards.Pokemon.Count; i++)
            {
                var d = ctx.Cards.Pokemon[i];
                if (d.Key.IndexOf('@') >= 0) continue;
                if (key != null) { if (d.Key != key) continue; }
                else if (d.Legendary || !(d.Tier == EncounterTier.Starter || d.Tier == EncounterTier.Weak || d.Tier == EncounterTier.Moderate || d.Tier == EncounterTier.Strong)) continue;
                if (tier != null && d.Tier != tier) continue;
                bool ok = true;
                foreach (var ty in must) ok &= Contains(d.Types, ty);
                foreach (var ty in not) ok &= !Contains(d.Types, ty);
                if (sameType != null) ok &= Contains(d.Types, sameType.Value);
                var t1 = d.Types[0];
                var t2 = d.Types.Count > 1 ? d.Types[1] : PokemonType.Typeless;
                if (weakTo != null) ok &= TypeChart.Bonus(weakTo.Value, t1, t2) > 0;
                if (!ok) continue;
                // 좌석 0 = 빠르고 단단하게(먼저 공격) · 좌석 1 = 느리고 단단하게, 상성 0(피해가 0으로 깎이지 않게)
                int s = (fast ? d.Initiative : -d.Initiative) * 100 + d.Health;
                if (!fast && weakTo == null && moveType != null && TypeChart.Bonus(moveType.Value, t1, t2) != 0) s -= 10000;
                if (s > score) { score = s; best = d.Key; }
            }
            return best;
        }

        static bool Contains(IReadOnlyList<PokemonType> list, PokemonType t)
        {
            foreach (var x in list)
                if (x == t) return true;
            return false;
        }

        // ── 돌리기 ──

        sealed class Trace
        {
            public readonly List<string>[] Foot = { new List<string>(), new List<string>() };
            public int Final = -1, First = -1;
            public readonly int[] Order = new int[2];
            public string Fail;
        }

        static ulong SeedOf(AttackDieFace[] faces)
        {
            string k = string.Join(",", faces);
            if (!Seeds.TryGetValue(k, out var seed)) Seeds[k] = seed = SeedFor(faces);
            return seed;
        }

        static Trace Stage(Ctx ctx, Spot spot, string a, string b, Plan p, bool useA, bool useB)
        {
            var tr = new Trace();
            var needs = new[] { (a, p.A, p.SeatA, useA), (b, p.B, p.SeatB, useB) };
            var cards = new string[3];
            foreach (var (key, n, seat, use) in needs)
                if (use && n.Fate == null && !n.ItemSource) cards[seat] = CardKey(key);
            Arena t;
            try
            {
                // 배틀 뒤 = 좌석 1의 마지막 포켓몬(체력 1)을 쓰러뜨려 배틀을 끝낸다
                t = Field(ctx, cards, new[] { p.Att, p.BenchA }, spot == Spot.BattleEnd ? new[] { p.Def } : new[] { p.Def, p.BenchD }, x =>
                {
                    if (spot == Spot.BattleEnd) Battles.SetHp(x.S, ctx, x.D, 1);
                    Hand(ctx, x.S, 0);
                    Hand(ctx, x.S, 1);
                    if (spot != Spot.Order && ctx.Cards.Pokemon[x.S.Pokemon[x.A].Def].Signature != p.Move) x.S.Pokemon[x.A].Moves[0] = p.Move;
                    foreach (var (key, n, seat, use) in needs)
                    {
                        int mon = seat == 0 ? x.A : x.D;
                        if (n.Item != null && (use || !n.ItemSource)) x.S.Pokemon[n.Item == "item.substitute" ? Bench(x, seat) : mon].HeldItem = Scene.Item(ctx, n.Item);
                        n.Prep?.Invoke(x, seat, mon);
                        if (use && n.Fate != null) Hand(ctx, x.S, seat, n.Fate.Value);
                    }
                    // poke_maniac R은 카드가 있을 때만 대타를 쥔다(Prep이 늘 쥐여 주므로 안 쓰면 거둔다)
                    foreach (var (key, n, seat, use) in needs)
                        if (key == "poke_maniac.R" && !use) x.S.Pokemon[Bench(x, seat)].HeldItem = -1;
                }, settle: false);
            }
            catch (AssertionException e)
            {
                tr.Fail = "장면을 세우지 못함: " + e.Message;
                return tr;
            }
            var token = t.S.Pending.Find(x => !x.Answered && x.Sub == ChoiceSub.InitiativeToken);
            if (token != null) Battles.Answer(t.S, ctx, token, ChoiceAnswer.Of(AnswerKind.Accept), t.Log);
            Resolve(t, (c, legal) => Pick(t, needs, c, legal), 20);
            var first = t.S.Pending.Find(x => !x.Answered && x.Kind == ChoiceKind.BattleAction);
            tr.First = first?.Seat ?? -1;
            var mods = new List<Mod>();
            for (int k = 0; k < 2; k++) tr.Order[k] = BattleRules.OrderValue(t.S, ctx, t.S.Battle.Sides[k].Active, mods, out _);
            if (spot == Spot.Order)
            {
                for (int i = 0; i < 2; i++)
                {
                    var (key, n, seat, use) = needs[i];
                    BattleRules.PriorityMods(t.S, ctx, t.S.Battle.Sides[seat].Active, mods);
                    foreach (var m in mods)
                        if (Match(ctx, t, key, n, seat, m.Source, seat)) tr.Foot[i].Add($"Prio:{m.Delta}");
                    Collect(ctx, t, key, n, seat, t.Log.Events.GetRange(t.From, t.Log.Events.Count - t.From), tr.Foot[i]);
                }
                return tr;
            }
            if (tr.First != 0)
            {
                tr.Fail = "좌석 0이 먼저 움직이지 않는다";
                return tr;
            }
            var ch = Battles.Only(t.S, ChoiceKind.BattleAction);
            int active = t.S.Battle.Sides[0].Active;
            if (ctx.Cards.Pokemon[t.S.Pokemon[active].Def].Signature != p.Move) t.S.Pokemon[active].Moves[0] = p.Move;
            t.S.Rng = new Pcg32(SeedOf(p.Faces)); // 패턴 전체로 찾은 시드 — 공격력이 바뀌어 주사위가 늘어도 앞의 면은 같다
            t.From = t.Log.Events.Count;
            Battles.Answer(t.S, ctx, ch, Battles.Move(p.Move), t.Log);
            try
            {
                Resolve(t, (c, legal) => Pick(t, needs, c, legal), 60);
            }
            catch (AssertionException e)
            {
                tr.Fail = "선택이 끝나지 않음: " + e.Message;
                return tr;
            }
            var events = t.Events;
            var bd = All<DamageBreakdown>(events);
            tr.Final = bd.Count > 0 ? bd[0].Final : -1;
            for (int i = 0; i < 2; i++)
            {
                var (key, n, seat, _) = needs[i];
                Collect(ctx, t, key, n, seat, events, tr.Foot[i]);
            }
            return tr;
        }

        static ChoiceAnswer Pick(Arena t, (string, Need, int, bool)[] needs, PendingChoice c, List<ChoiceAnswer> legal)
        {
            foreach (var (key, n, seat, use) in needs)
            {
                if (!use) continue;
                if (n.Fate != null)
                {
                    if (c.Sub != ChoiceSub.Fate || c.Seat != seat) continue;
                    if (c.Window != n.FateWindow) return Decline(legal);
                    return Fate(legal, n.Fate.Value, n.Fate == FateCard.Dialga ? 0 : -1) ?? Decline(legal);
                }
                // 대타출동 물음의 출처는 그 좌석의 카드(없으면 카드 −1)
                bool own = key == "item.substitute" ? c.Seat == seat && c.Source.Kind == SourceKind.TrainerCard
                    : n.ItemSource ? c.Seat == seat && c.Source.Kind == SourceKind.Item : FromCard(t, c, seat);
                if (!own) continue;
                var r = n.Ask?.Invoke(t, c, legal);
                if (r != null) return r;
                var yes = legal.Find(x => x.Kind == AnswerKind.Accept);
                if (yes != null) return yes;
                var any = legal.Find(x => x.Kind != AnswerKind.Decline);
                return any ?? c.Default.Clone();
            }
            if (c.Sub == ChoiceSub.Fate) return Decline(legal);
            return null;
        }

        static bool Match(Ctx ctx, Arena t, string key, Need n, int seat, SourceRef r, int eventSeat)
        {
            if (n.Fate != null) return r.Kind == SourceKind.Fate && (eventSeat == seat || eventSeat == GameEvent.U);
            if (key == "item.substitute" && r.Kind == SourceKind.TrainerCard && r.Id == -1 && eventSeat == seat) return true; // 카드 없는 좌석의 대타출동 물음
            if (n.ItemSource) return r.Kind == SourceKind.Item && r.Id == Scene.Item(ctx, n.Item);
            return r.Kind == SourceKind.TrainerCard && r.Id == ctx.Cards.TrainerCardIds.IndexOf(CardKey(key));
        }

        static void Collect(Ctx ctx, Arena t, string key, Need n, int seat, List<GameEvent> events, List<string> into)
        {
            foreach (var e in events)
            {
                if (e is DiceRolled dr && dr.PowerMods != null)
                    foreach (var m in dr.PowerMods)
                        if (Match(ctx, t, key, n, seat, m.Source, GameEvent.U)) into.Add($"Power{dr.Side}:{m.Delta}");
                if (e is DamageBreakdown db)
                    foreach (var st in db.Steps)
                        if (Match(ctx, t, key, n, seat, st.Source, GameEvent.U)) into.Add(st.Kind == DamageStepKind.Cap || st.Kind == DamageStepKind.Threshold ? $"~{st.Kind}" : $"{st.Kind}:{st.Value}");
                // 기절 · 교체 · 배틀 끝은 그 피해의 결과라 몫에서 뺀다(어느 피해로 쓰러졌나는 피해 값이 말한다)
                if (e is Fainted || e is SwitchedOut || e is SentIn || e is BattleEnded) continue;
                if (Match(ctx, t, key, n, seat, e.Source, e.Seat)) into.Add(e.GetType().Name);
            }
            into.Sort(string.CompareOrdinal);
        }

        static string Foot(List<string> f) => f.Count == 0 ? "-" : string.Join(",", f);

        /// <summary>견줄 몫: 상한 · 문턱이 실제로 깎았나(~)는 다른 쪽 값에 따라 갈리는 것이 규칙이라 빼고 최종 피해 식으로 본다.</summary>
        static string Firm(List<string> f) => Foot(f.FindAll(x => !x.StartsWith("~")));

        // ── 판정 ──

        /// <summary>
        /// 규칙상 바뀌는 쌍(창 · 쌍 → 근거): 한쪽이 다른 쪽의 조건을 바꾸거나 같은 단계에 겹쳐 값이 달라진다 — 「서로 영향 없음」의 예외. 생성 장면이 다르게 나오면 여기 있어야 통과한다.
        /// </summary>
        static readonly Dictionary<string, string> Changed = new Dictionary<string, string>
        {
            // 우선권: ninja_boy B는 「우선도와 상관없이」 토큰을 쓰게 하는 능력이라 느릴 때만 쓴 것이다 — 무녀 빨강의 −3으로 상대가 더 느려지면 보통 토큰(B 없이 · R의 Toxin Dart만, A34)
            { "Order:ninja_boy.B + pearl_acolyte.R", "뒤가 조건을 잃음(A34 ninja_boy B — 이미 빠르면 B가 필요 없다)" },
            { "Order:ninja_boy.R + pearl_acolyte.R", "뒤가 조건을 잃음(A34 ninja_boy B — 이미 빠르면 B가 필요 없다)" },
            // 굴리기 전: triathlete R이 공격 타입을 얼음으로 바꾸면 물 · 불꽃 기술 +1이 빠진다(장면 C02 swimmer R × team_aqua R과 같은 꼴)
            { "BeforeRoll:team_aqua.R + triathlete.R", "뒤가 조건을 잃음(A34 triathlete R 타입 바꾸기 → A32 team_aqua R 물 기술만 — C02 꼴)" },
            { "BeforeRoll:team_magma.R + triathlete.R", "뒤가 조건을 잃음(A34 triathlete R 타입 바꾸기 → A32 team_magma R 불꽃 기술만 — C02 꼴)" },
            // 피해 직전: kindler R의 2 피해도 받는 쪽 카드를 지난다(사용자 결정 135) — 비행 포켓몬의 「2 이하 무효」 문턱이 2를 지운다
            { "PreDamage:bird_keeper.R + kindler.R", "결정 135(A32 「from attacks」 — kindler R 2 피해에도 bird_keeper R 문턱)" },
            // 피해 뒤: undivided_acolyte R이 쓰러뜨려 다음 강철이 나오면 worker R(A35 — 앞이 뒤의 조건을 만드는 사슬)
            { "AfterDamage:undivided_acolyte.R + worker.R", "사슬(A35 undivided_acolyte R 기절 → 강철 출전 → worker R)" },
        };

        /// <summary>남의 맵 행동 창의 장면 밖 쌍(장면 C17 · C19 밖) — 까닭.</summary>
        static readonly Dictionary<string, (Verdict, string)> MapNotes = new Dictionary<string, (Verdict, string)>
        {
            { "collector.B + delinquent.B", (Verdict.Idle, "고를 것 없음 — 공물은 스탬프를 주지 않아 collector B(스탬프 보상 두 번 · 묻지 않음)가 같은 행동에 서지 않는다") },
            { "collector.B + ginkgo_merchant.B", (Verdict.Idle, "고를 것 없음 — 상인 몫(거래 줄마다 버린 규토리)과 수집가 몫(스탬프 보상 두 번)은 서로의 조건 · 값을 바꾸지 않는다(C17 · C19가 같은 거래를 돈다)") },
            { "delinquent.B + ginkgo_merchant.B", (Verdict.Apart, "다른 행동에 붙는 반응(사당 공물 · 은행 거래) — 한 행동에 둘이 같이 서지 않는다") },
            { "delinquent.B + interviewers.B", (Verdict.Idle, "공물 장면은 세우지 않음 — 같은 길(남의 행동으로 받은 규토리 → 일곱 색 스탬프 → 인터뷰어 · ScoopNext)은 C17이 본다") },
        };

        /// <summary>생성 장면에서 찾은 버그(쌍 → 무엇) — 고치지 않는다. 재현이 사라지면 시험이 알린다.</summary>
        static readonly Dictionary<string, string> KnownPairs = new Dictionary<string, string>
        {
        };

        /// <summary>해석이 갈려 물을 것(쌍 → 무엇).</summary>
        static readonly Dictionary<string, string> AskPairs = new Dictionary<string, string>
        {
        };

        /// <summary>그 창의 후보 쌍을 모두 생성 장면으로 돌린다(장면 표 <see cref="Rows"/>가 덮은 쌍은 「장면」으로 센다).</summary>
        public static List<PairResult> RunPairs(Ctx ctx, Spot spot)
        {
            var missing = new List<string>();
            var cand = Candidates(Sources(ctx, missing));
            var r = new List<PairResult>();
            if (!cand.TryGetValue(spot, out var set)) return r;
            var scenes = new Dictionary<string, string>();
            foreach (var row in Rows()) if (row.Spot == spot) scenes[PairKey(row.A, row.B)] = row.Id;
            var needs = Needs(ctx, spot);
            var pairs = new List<string>(set);
            pairs.Sort(string.CompareOrdinal);
            foreach (var pair in pairs)
            {
                var parts = pair.Split(new[] { " + " }, StringSplitOptions.None);
                string a = parts[0], b = parts[1];
                var res = new PairResult { Spot = spot, Pair = pair };
                r.Add(res);
                if (scenes.TryGetValue(pair, out var id)) { res.Verdict = Verdict.Scene; res.Note = "장면 " + id; continue; }
                if (a.Contains(".") && b.Contains(".") && !a.StartsWith("item.") && !a.StartsWith("fate.") && CardKey(a) == CardKey(b))
                {
                    res.Verdict = Verdict.Apart;
                    res.Note = "같은 카드의 두 능력 — 카드는 한 장이라 다른 좌석끼리 설 수 없다";
                    continue;
                }
                if (spot == Spot.MapOther)
                {
                    if (MapNotes.TryGetValue(pair, out var note)) { res.Verdict = note.Item1; res.Note = note.Item2; }
                    else { res.Verdict = Verdict.Differs; res.Note = "남의 맵 행동 쌍 — 장면도 까닭도 없음"; }
                    continue;
                }
                if (!needs.TryGetValue(a, out var na) || !needs.TryGetValue(b, out var nb))
                {
                    res.Verdict = Verdict.Differs;
                    res.Note = "조건 표에 없는 출처";
                    continue;
                }
                string stop = na.Skip ?? nb.Skip;
                if (stop != null) { res.Verdict = Verdict.Idle; res.Note = $"{(na.Skip != null ? a : b)}: {stop}"; continue; }
                stop = na.Wild ?? nb.Wild;
                if (stop != null) { res.Verdict = Verdict.Apart; res.Note = $"{(na.Wild != null ? a : b)}: {stop}"; continue; }
                Judge(ctx, spot, a, b, na, nb, res);
            }
            return r;
        }

        static IEnumerable<Need> Chain(Need n)
        {
            for (var x = n; x != null; x = x.Alt) yield return x;
        }

        /// <summary>조건 묶음(출처마다 Alt 사슬)을 차례로 세워 보고, 둘 다 일하는 첫 장면으로 판정한다.</summary>
        static void Judge(Ctx ctx, Spot spot, string a, string b, Need na, Need nb, PairResult res)
        {
            string why = null;
            bool any = false;
            foreach (var x in Chain(na))
                foreach (var y in Chain(nb))
                {
                    var plan = Solve(ctx, spot, a, b, x, y, out var w);
                    if (plan == null) { why = why ?? w; continue; }
                    any = true;
                    JudgePlan(ctx, spot, a, b, plan, res);
                    if (res.Verdict != Verdict.Idle) return;
                }
            if (!any) { res.Verdict = Verdict.Apart; res.Note = "조건이 함께 서지 않음: " + why; }
        }

        static void JudgePlan(Ctx ctx, Spot spot, string a, string b, Plan plan, PairResult res)
        {
            var na = plan.A;
            var nb = plan.B;
            var none = Stage(ctx, spot, a, b, plan, false, false);
            var onlyA = Stage(ctx, spot, a, b, plan, true, false);
            var onlyB = Stage(ctx, spot, a, b, plan, false, true);
            var both = Stage(ctx, spot, a, b, plan, true, true);
            string scene = $"{plan.Att}({plan.SeatA}:{a.Split('.')[0]}) → {plan.Def} · {ctx.Cards.Moves[plan.Move].Key}";
            foreach (var x in new[] { none, onlyA, onlyB, both })
                if (x.Fail != null) { res.Verdict = Verdict.Differs; res.Note = $"{scene} · {x.Fail}"; return; }
            // 혼자서도 둘이서도 몫이 없으면 그 장면에서 일하지 않는 것(혼자는 없고 둘이서만 있으면 앞이 뒤의 조건을 만드는 사슬 — 차이로 본다)
            var miss = new List<string>();
            if (onlyA.Foot[0].Count == 0 && both.Foot[0].Count == 0) miss.Add($"{a} 일하지 않음");
            if (onlyB.Foot[1].Count == 0 && both.Foot[1].Count == 0) miss.Add($"{b} 일하지 않음");
            if (miss.Count > 0)
            {
                res.Verdict = Verdict.Idle;
                res.Note = $"{scene} · {string.Join(" · ", miss)}";
                return;
            }
            var diff = new List<string>();
            if (Firm(onlyA.Foot[0]) != Firm(both.Foot[0])) diff.Add($"{a} 몫 {Foot(onlyA.Foot[0])} → 둘 다 {Foot(both.Foot[0])}");
            if (Firm(onlyB.Foot[1]) != Firm(both.Foot[1])) diff.Add($"{b} 몫 {Foot(onlyB.Foot[1])} → 둘 다 {Foot(both.Foot[1])}");
            if (spot == Spot.PreDamage)
            {
                int want = FinalOf(none.Final, onlyA.Final, onlyB.Final, na, nb);
                if (both.Final != want) diff.Add($"최종 피해 {both.Final}(기대 {want} — 없음 {none.Final} · A {onlyA.Final} · B {onlyB.Final})");
            }
            if (spot == Spot.Order)
            {
                for (int k = 0; k < 2; k++)
                {
                    int want = onlyA.Order[k] + onlyB.Order[k] - none.Order[k];
                    if (both.Order[k] != want) diff.Add($"좌석 {k} 순서 값 {both.Order[k]}(기대 {want})");
                }
                bool ninja = a == "ninja_boy.B" || b == "ninja_boy.B";
                int first = both.Order[0] >= both.Order[1] || ninja ? 0 : 1;
                if (both.First != first) diff.Add($"먼저 움직임 좌석 {both.First}(기대 {first} — 순서 값 {both.Order[0]} · {both.Order[1]})");
            }
            string seen = $"{scene} · {a} {Foot(both.Foot[0])} · {b} {Foot(both.Foot[1])}{(spot == Spot.PreDamage ? $" · 피해 {none.Final}/{onlyA.Final}/{onlyB.Final}/{both.Final}" : spot == Spot.Order ? $" · 순서 {both.Order[0]}:{both.Order[1]} 먼저 {both.First}" : "")}";
            string key = $"{spot}:{res.Pair}";
            if (diff.Count == 0)
            {
                res.Verdict = Verdict.Same;
                res.Note = seen;
                if (Changed.ContainsKey(key) || KnownPairs.ContainsKey(key) || AskPairs.ContainsKey(key)) { res.Verdict = Verdict.Differs; res.Note = "표에 적힌 차이가 더는 나지 않는다 — 표에서 지운다 · " + seen; }
                return;
            }
            res.Note = $"{seen} · 차이 {string.Join(" / ", diff)}";
            if (Changed.TryGetValue(key, out var why2)) { res.Verdict = Verdict.Changed; res.Note = why2 + " · " + res.Note; }
            else if (KnownPairs.TryGetValue(key, out why2)) { res.Verdict = Verdict.Known; res.Note = why2 + " · " + res.Note; }
            else if (AskPairs.TryGetValue(key, out why2)) { res.Verdict = Verdict.Ask; res.Note = why2 + " · " + res.Note; }
            else res.Verdict = Verdict.Differs;
        }

        /// <summary>둘 다의 기대 최종 피해: 더하기 · 빼기는 각자 몫의 합, 상한은 마지막(⑦) · 문턱은 감소 뒤(⑥) — 상한 · 문턱 쪽은 다른 쪽 혼자의 값에 건다.</summary>
        static int FinalOf(int none, int a, int b, Need na, Need nb)
        {
            if (na.Cap > 0) return Math.Min(na.Cap, b);
            if (nb.Cap > 0) return Math.Min(nb.Cap, a);
            if (na.Threshold > 0) return b <= na.Threshold ? 0 : b;
            if (nb.Threshold > 0) return a <= nb.Threshold ? 0 : a;
            return Math.Max(0, a + b - none);
        }

        /// <summary>창마다 결과 수(덮음 · 못 덮음) 줄.</summary>
        public static string Summary(List<PairResult> list)
        {
            var count = new Dictionary<Verdict, int>();
            foreach (var x in list) count[x.Verdict] = (count.TryGetValue(x.Verdict, out var c) ? c : 0) + 1;
            var sb = new StringBuilder();
            int covered = 0;
            foreach (var x in list)
                if (x.Covered) covered++;
            sb.Append($"후보 {list.Count} · 덮음 {covered}");
            foreach (var kv in count) sb.Append($" · {kv.Key} {kv.Value}");
            return sb.ToString();
        }
    }
}
