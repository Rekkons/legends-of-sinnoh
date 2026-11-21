using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 트레이너 카드 — 해금될 때: 전용 아이템 능력 10(M6 묶음 10, rulings A41): dragon_tamer B·R(드래곤의 정점) · gentleman R(프레셔스 볼) · guitarist B·R(앰프 주사위) ·
    /// parasol_lady B·R(날씨 휠 — 판 상태 GameState.Weather) · poke_maniac R(대타출동) · tourist B·R(랜드마크 투어 · 전설 스냅).
    /// 「When unlocked」의 받기는 <see cref="ItemUnlockedNow"/>(OnUnlock), 고를 것(첫 날씨 · 약한 덱의 드래곤)은 「무녀·카드 바꾸기」의 미뤄 둔 물음(UnlockDue)으로 묻는다.
    /// 배틀 물음 대타출동은 효과 프레임(<see cref="AbilityItems"/>) · 앰프는 굴린 뒤 반응 창의 반응(결정 134 — <see cref="AmpLegal"/>), 맵 행동(날씨 바꾸기 · 랜드마크 도장)은 UseAbility 답이다.
    /// </summary>
    public static partial class TrainerRules
    {
        /// <summary>dragon_tamer B 「costs an extra 5 Journey Points to evolve」 · R 「+6 health, +2 initiative and +1 attack strength」.</summary>
        public const int ApexEvolveCost = 5, ApexHealth = 6, ApexInitiative = 2, ApexPower = 1;
        /// <summary>parasol_lady B 「discarding 3 Journey Points at a Shrine」 · 「+2 attack strength」 · R 「take 2 less damage」.</summary>
        public const int WeatherCost = 3, WeatherPower = 2, UmbrellaGuard = 2;
        /// <summary>앰프 주사위 면 0~5(rulings G1) — 5에서 더 오르지 않는다 [가정 plan §5 #147 ①].</summary>
        public const int AmpMax = 5;
        /// <summary>tourist R 「if you complete your Landmark Tour sheet, gain 30 Journey Points」.</summary>
        public const int TourJp = 30;
        /// <summary>UseAbility 답의 Target: 날씨 바꾸기(맵 행동 — Arg = 새 날씨) · 프레셔스 볼 던지기(배틀 행동).</summary>
        public const int WeatherTarget = -3, CherishTarget = -2;
        /// <summary>SeatState.CherishIn: 손에 있다(카드가 풀렸으면 던질 수 있다). 0 이상 = 그 볼로 잡은 포켓몬.</summary>
        public const int CherishInHand = -1;
        /// <summary>UnlockDue 종류: 첫 날씨 고르기 · 약한 덱의 드래곤 고르기.</summary>
        public const int DueWeather = 3, DueDragon = 4;
        /// <summary>배틀 물음 프레임의 능력 번호(Locals[0]).</summary>
        public const int AbSubstitute = 12;   // 11 = 옛 guitarist 앰프 물음(결정 134로 반응 창으로 옮김)
        /// <summary>BattleState.CardAsk 비트: 이번 공격의 앰프 올리기 · 대타출동 물음을 지났다 · 앰프를 썼다(결정 134 — 반응 창, 공격당 한 번).</summary>
        public const int AskedAmp = 8, AskedSub = 16, UsedAmp = 64;
        /// <summary>굴린 뒤 반응 창의 앰프 답 UseAbility의 Target(Arg = 1 · 2배 — 결정 134). psychic R 답의 Target(면 0~2)과 겹치지 않는다.</summary>
        public const int AmpTarget = -4;

        /// <summary>날씨 휠 네 칸(rulings G1 — items_weather_wheel.png): 0 쾌청(불꽃) · 1 눈(얼음) · 2 비(물) · 3 모래바람(바위).</summary>
        public static readonly PokemonType[] WeatherTypes = { PokemonType.Fire, PokemonType.Ice, PokemonType.Water, PokemonType.Rock };

        /// <summary>지금 날씨의 타입(날씨 휠이 없으면 null).</summary>
        public static PokemonType? WeatherType(GameState s) => s.Weather >= 0 && s.Weather < WeatherTypes.Length ? WeatherTypes[s.Weather] : (PokemonType?)null;

        static SourceRef WeatherSource(Ctx ctx) => new SourceRef(SourceKind.TrainerCard, ctx.Cards.TrainerCardIds.IndexOf("parasol_lady"));

        static int ItemId(Ctx ctx, string key) => ctx.Items.Ids.IndexOf(key);

        /// <summary>뗄 수 없는 지닌물건(드래곤의 정점 「cannot be unequipped」) — 정비로 바꾸거나 내려놓지 못하고 놓아주면 가방으로 가지 않는다(사라진다 [가정 §5 #147 ②]).</summary>
        public static bool Bound(Ctx ctx, int item) => item >= 0 && item == ItemId(ctx, "item.apex_dragon");

        /// <summary>정비로 지니게 할 수 있는 전용 아이템(대타출동 「held item」).</summary>
        public static bool Holdable(Ctx ctx, int item) => item >= 0 && item == ItemId(ctx, "item.substitute");

        // ── 해금될 때(OnUnlock) ──

        /// <summary>이 묶음의 「When unlocked」. 처리했으면 true.</summary>
        static bool ItemUnlockedNow(GameState s, Ctx ctx, Emitter fx, int seat, AbilityColor color)
        {
            var st = s.Seats[seat];
            if (color == Blue && Has(s, ctx, seat, "dragon_tamer", Blue))
            {
                Hatch(s, ctx, fx, seat);
                return true;
            }
            if (color == Red && Has(s, ctx, seat, "gentleman", Red))
            {
                // 「When unlocked, you receive the Cherish Ball.」
                Give(s, ctx, st, "item.cherish_ball");
                st.CherishIn = CherishInHand;
                Used(s, fx, seat, Red);
                return true;
            }
            if (color == Blue && Has(s, ctx, seat, "guitarist", Blue))
            {
                // 「When unlocked, you receive an Amplifier dice.」 — 0에서 시작
                Give(s, ctx, st, "item.amplifier");
                st.Amp = 0;
                Used(s, fx, seat, Blue);
                return true;
            }
            if (color == Blue && Has(s, ctx, seat, "parasol_lady", Blue))
            {
                // 「place the Weather Wheel in the middle of the arena on a Weather of your choice」 — 고르기는 강제(첫 날씨 무료, A33) · 미뤄 묻는다
                Give(s, ctx, st, "item.weather_wheel");
                Give(s, ctx, st, "item.weather_wheel_base");
                if (s.Weather < 0)
                {
                    st.UnlockDue.Add(DueWeather);
                    st.UnlockDue.Add(0);
                }
                return true;
            }
            if (color == Red && Has(s, ctx, seat, "poke_maniac", Red))
            {
                // 「When unlocked, you receive the Substitute held item.」 — 가방으로(정비 HoldItem으로 지니게 한다)
                Give(s, ctx, st, "item.substitute");
                st.Bag.Add(ItemId(ctx, "item.substitute"));
                Used(s, fx, seat, Red);
                return true;
            }
            if (color == Blue && Has(s, ctx, seat, "tourist", Blue))
            {
                // 「When unlocked, place the Landmark Tour sheet in your hand.」
                Give(s, ctx, st, "item.landmark_tour");
                Used(s, fx, seat, Blue);
                return true;
            }
            if (color == Red && Has(s, ctx, seat, "tourist", Red))
            {
                Completed(s, ctx, fx, seat); // 이미 다 채웠으면 그 자리에서 JP 30 [가정 §5 #147 ⑨]
                return true;
            }
            return false;
        }

        // ── dragon_tamer B·R ──

        /// <summary>약한 덱의 드래곤 타입 카드(덱 위에서부터, 같은 카드는 한 번).</summary>
        static List<int> Dragons(GameState s, Ctx ctx)
        {
            var r = new List<int>();
            var deck = s.Decks[(int)DeckId.Weak];
            for (int i = deck.Count - 1; i >= 0; i--)
                if (!r.Contains(deck[i]) && IsDragon(ctx, deck[i])) r.Add(deck[i]);
            return r;
        }

        static bool IsDragon(Ctx ctx, int def)
        {
            foreach (var t in ctx.Cards.Pokemon[def].Types)
                if (t == PokemonType.Dragon) return true;
            return false;
        }

        /// <summary>
        /// dragon_tamer B 「When unlocked, search the Weak Pokémon deck for a Dragon-type Pokémon and add it to your party」: 하나뿐이면 바로, 여럿이면 미뤄 고른다(강제 — 첫 후보가 기본).
        /// 없으면 G3(rulings G3 — 아무것도 없다).
        /// </summary>
        static void Hatch(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            var dragons = Dragons(s, ctx);
            if (dragons.Count == 0) return;
            if (dragons.Count == 1)
            {
                Hatchling(s, ctx, fx, seat, dragons[0]);
                return;
            }
            s.Seats[seat].UnlockDue.Add(DueDragon);
            s.Seats[seat].UnlockDue.Add(0);
        }

        /// <summary>그 드래곤을 약한 덱에서 꺼내(덱은 섞는다 — 찾아본 뒤 [가정 §5 #147 ③]) 드래곤의 정점을 지니게 하고 파티에.</summary>
        static void Hatchling(GameState s, Ctx ctx, Emitter fx, int seat, int def)
        {
            var deck = s.Decks[(int)DeckId.Weak];
            if (!deck.Remove(def)) return;
            Piles.Shuffle(deck, s.Rng);
            int p = PokemonRules.Spawn(s, def, seat, (int)DeckId.Weak);
            s.Pokemon[p].HeldItem = ItemId(ctx, "item.apex_dragon"); // 「receives the Apex Dragon held item which cannot be unequipped」
            Used(s, fx, seat, Blue, p);
            Join(s, ctx, fx, seat, p);
            Obtained(s, ctx, fx, seat, p, ObtainCause.CardEffect);
        }

        /// <summary>그 포켓몬이 주인의 드래곤의 정점이다(주인 좌석이 dragon_tamer color가 풀린 카드 — 「Your Apex Dragon」, 남이 가져가면 없다).</summary>
        static bool Apex(GameState s, Ctx ctx, PokemonState p, AbilityColor color) =>
            p.Def >= 0 && Seated(s, p.Owner) && Has(s, ctx, p.Owner, "dragon_tamer", color) && PokemonRules.HoldsItem(p, ItemId(ctx, "item.apex_dragon"));

        /// <summary>dragon_tamer B 「Your Apex Dragon costs an extra 5 Journey Points to evolve at all stages」(EvolveRules.Cost — 반값 앞, 원작 커뮤니티 확인).</summary>
        public static int ApexCost(GameState s, Ctx ctx, int pokemon) => Apex(s, ctx, s.Pokemon[pokemon], Blue) ? ApexEvolveCost : 0;

        // ── gentleman R ──

        /// <summary>프레셔스 볼을 던질 수 있나(카드 빨강 · 손에 있다).</summary>
        public static bool CherishReady(GameState s, Ctx ctx, int seat) => Seated(s, seat) && Has(s, ctx, seat, "gentleman", Red) && s.Seats[seat].CherishIn == CherishInHand;

        /// <summary>
        /// 놓아주었다(MaintenanceRules.Release): 프레셔스 볼로 잡은 포켓몬이면 볼이 손으로 돌아온다(「if you release the captured Pokémon, you get the Cherish Ball back」).
        /// 배틀 중에는 어떤 포켓몬도 놓아줄 수 없어(MaintenanceRules.TimingCheck — 사용자 결정 84 T10 ③ 나, 원작 커뮤니티 확인) 볼을 잃는 길이 없다.
        /// </summary>
        internal static void Released(GameState s, int pokemon)
        {
            foreach (var st in s.Seats)
                if (st.CherishIn == pokemon) st.CherishIn = CherishInHand;
        }

        // ── guitarist B·R ──

        /// <summary>굴린 상태 심볼만큼 앰프를 올린다(「For every status symbol you roll, increase your Amplifier dice by 1」 — 5에서 멈춤).</summary>
        static void Charge(GameState s, Ctx ctx, int seat, int symbols)
        {
            if (symbols <= 0 || !Seated(s, seat) || !Has(s, ctx, seat, "guitarist", Blue)) return;
            var st = s.Seats[seat];
            if (st.Amp < 0) return;
            st.Amp = Math.Min(AmpMax, st.Amp + symbols);
        }

        /// <summary>포획 굴림의 상태 심볼도 센다(원작 커뮤니티 확인, A33). raw = 굴린 면(정확도로 바꾸기 전).</summary>
        internal static void CaptureCharge(GameState s, Ctx ctx, int seat, IReadOnlyList<AttackDieFace> raw) => Charge(s, ctx, seat, AttackDice.Count(raw, AttackDieFace.StatusSymbol));

        /// <summary>
        /// 굴린 뒤(면 바꾸기 순서 다음, 매혹적인 스튜·반응 창 앞): 이 공격 굴림의 상태 심볼만큼 앰프를 올린다(면 바꾸기가 끝난 결과로 센다 [가정 §5 #147 ①] — 반응 창의 디아루가 · psychic R은 그 뒤라 세지 않는다).
        /// 쓸지는 굴린 뒤 반응 창에서 묻는다(사용자 결정 134 — <see cref="AmpLegal"/>).
        /// </summary>
        internal static void Amplify(GameState s, Ctx ctx, BattleState b)
        {
            if ((b.CardAsk & AskedAmp) != 0) return;
            b.CardAsk |= AskedAmp;
            int seat = b.Sides[b.ActiveSide].Owner;
            if (b.Move == -1 || !Seated(s, seat)) return;
            Charge(s, ctx, seat, AttackDice.Count(b.Faces, AttackDieFace.StatusSymbol));
        }

        /// <summary>
        /// guitarist B 「You may reset the Amplifier dice to add its value as damage to any attack」을 지금 굴린 뒤 창에서 그 좌석이 쓸 수 있나(사용자 결정 134 — 결정 131 psychic R과 같은 꼴:
        /// 따로 묻던 자리(PostRoll)를 굴린 뒤 반응 창(Window.AfterRoll)으로 옮겼다 — 디아루가 · psychic R과 같은 창 · 같은 바퀴, G6). 같은 공격에서 올린 값도 쓴다(원작 커뮤니티 확인).
        /// 공격하는 좌석 · 공격 굴림(포획 굴림 아님) · 이번 공격에 아직 안 씀 · 앰프 &gt; 0 · 이 배틀에서 두 배로 쓰지 않았음(R). 모두 공개 값이다.
        /// </summary>
        public static bool AmpOpen(GameState s, Ctx ctx, int seat)
        {
            var b = s.Battle;
            if (b == null || b.ActiveSide < 0 || b.Step == BattleStep.Capture || b.Move == -1) return false;
            int k = b.ActiveSide;
            if (b.Sides[k].Owner != seat || (b.CardAsk & UsedAmp) != 0 || !Seated(s, seat)) return false;
            return Has(s, ctx, seat, "guitarist", Blue) && s.Seats[seat].Amp > 0 && !b.Sides[k].AmpSpent;
        }

        /// <summary>
        /// 굴린 뒤 반응 창(Reaction/Fate 선택)에 더하는 앰프 답: UseAbility(<see cref="AmpTarget"/>, 1) · 전기 기술이고 guitarist R이면 UseAbility(AmpTarget, 2)도
        /// (R 「When you attack with an Electric-type move, you may double the value … Afterwards, you can no longer use your Amplifier dice during this battle」).
        /// </summary>
        public static void AmpLegal(GameState s, Ctx ctx, int seat, Window w, List<ChoiceAnswer> into)
        {
            if (w != Window.AfterRoll || !AmpOpen(s, ctx, seat)) return;
            into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility, AmpTarget, 1));
            if (Has(s, ctx, seat, "guitarist", Red) && BattleRules.AttackType(s, ctx, s.Battle.Move) == PokemonType.Electric) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility, AmpTarget, 2));
        }

        /// <summary>반응 창의 앰프 답 적용(Reactions.Apply): 이번 공격 피해에 앰프 × 배수를 더하고 0으로. 앞 반응 때문에 못 쓰게 됐으면 효과 없이 false.</summary>
        internal static bool ApplyAmp(GameState s, Ctx ctx, Emitter fx, int seat, ChoiceAnswer a)
        {
            if (a.Kind != AnswerKind.UseAbility || a.Target != AmpTarget || (a.Arg != 1 && a.Arg != 2) || !AmpOpen(s, ctx, seat)) return false;
            var b = s.Battle;
            int k = b.ActiveSide;
            var st = s.Seats[seat];
            b.CardAsk |= UsedAmp;
            b.AmpDamage = st.Amp * a.Arg;
            st.Amp = 0;
            if (a.Arg == 2) b.Sides[k].AmpSpent = true;
            Used(s, fx, seat, a.Arg == 2 ? Red : Blue, b.Sides[k].Active);
            return true;
        }

        /// <summary>봇의 앰프 답(원칙 AI · 상대 모델 — [가정 plan §5 #618]): 쓸 수 있으면 쓴다 — 두 배가 있으면 두 배. 앰프 답이 없으면 null.</summary>
        public static ChoiceAnswer AmpBest(List<ChoiceAnswer> legal)
        {
            ChoiceAnswer best = null;
            foreach (var a in legal)
                if (a.Kind == AnswerKind.UseAbility && a.Target == AmpTarget && (best == null || a.Arg > best.Arg)) best = a;
            return best;
        }

        /// <summary>앰프 피해(효과 피해 ② — EffectRules.DamageMods가 TrainerRules.DamageMods로 부른다): 이번 공격에 쓴 값.</summary>
        static void AmpMod(GameState s, int attacker, List<Mod> into)
        {
            var b = s.Battle;
            if (b == null || b.AmpDamage <= 0 || b.ActiveSide < 0 || b.Sides[b.ActiveSide].Active != attacker) return;
            into.Add(new Mod { Source = CardSource(s, b.Sides[b.ActiveSide].Owner), Delta = b.AmpDamage });
        }

        // ── parasol_lady B·R ──

        /// <summary>
        /// parasol_lady B 「All moves that match the Weather type gain +2 attack strength」: 날씨 휠을 가진 좌석(해금 때 받은 전용 아이템 — SeatState.CardItems)의 포켓몬이 쓰는 기술만
        /// (사용자 결정 84 T10 ② 나 — 야생·가상 쪽 · 다른 좌석의 기술에는 없다. 옛 [가정]은 「All moves」대로 누구든).
        /// </summary>
        static void WeatherMod(GameState s, Ctx ctx, PokemonType type, int seat, List<Mod> into)
        {
            if (WeatherType(s) != type || !Seated(s, seat) || !s.Seats[seat].CardItems.Contains(ItemId(ctx, "item.weather_wheel"))) return;
            into.Add(new Mod { Source = WeatherSource(ctx), Delta = WeatherPower });
        }

        /// <summary>날씨를 놓거나 바꾼다(WeatherSet). cost = JP 3을 낸 좌석(−1 = 해금 때의 무료 놓기).</summary>
        static void SetWeather(GameState s, Ctx ctx, Emitter fx, int seat, int weather, bool cost)
        {
            int before = s.Weather;
            s.Weather = weather;
            var changes = new List<ResChange>(1);
            if (cost)
            {
                var st = s.Seats[seat];
                changes.Add(new ResChange { Seat = seat, Res = ResKind.Jp, Key = -1, Before = st.Jp, After = st.Jp - WeatherCost });
                st.Jp -= WeatherCost;
            }
            else Used(s, fx, seat, Blue, weather);
            if (fx.On) fx.Add(new WeatherSet { Seat = seat, Source = WeatherSource(ctx), Before = before, Weather = weather, Changes = changes.ToArray() });
        }

        /// <summary>
        /// 맵 행동의 날씨 바꾸기(「Any trainer may change the Weather by discarding 3 Journey Points at a Shrine」 — 카드 주인도, A33): 날씨 휠이 놓였고 · 사당 칸(사당 · 전설 사당 토큰, 부서지지 않음)이고 ·
        /// 상호작용할 수 있고 · JP 3 이상이면 지금과 다른 날씨마다 UseAbility(WeatherTarget, 날씨). 횟수 제한은 없다(문구 그대로).
        /// </summary>
        internal static void WeatherAnswers(GameState s, Ctx ctx, int seat, bool interact, List<ChoiceAnswer> into)
        {
            if (s.Weather < 0 || !interact || seat < 0 || seat >= s.SeatCount) return;
            var st = s.Seats[seat];
            if (st.Jp < WeatherCost || ShrineRules.ShrineAt(s, st.Cell) < 0) return;
            for (int w = 0; w < WeatherTypes.Length; w++)
                if (w != s.Weather) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility, WeatherTarget, w));
        }

        /// <summary>맵 행동 UseAbility(WeatherTarget)의 적용. 그 답이 아니면 false.</summary>
        internal static bool ChangeWeather(GameState s, Ctx ctx, Emitter fx, PendingChoice c, ChoiceAnswer a)
        {
            if (a.Target != WeatherTarget) return false;
            SetWeather(s, ctx, fx, c.Seat, a.Arg, true);
            MapActions.Continue(s, ctx, fx, c.Seat);
            return true;
        }

        // ── poke_maniac R ──

        /// <summary>
        /// 피해 계산 직전(BattleStep.PreDamage — 굴린 뒤 창이 닫힌 뒤): 공격받는 쪽 좌석의 벤치(열린 칸 · 쓸 수 있는)에 대타출동을 지닌 포켓몬이 있으면
        /// 「may be switched in from your party just before any damage is applied from an opposing attack」을 묻는다 — 나와 있는 포켓몬이 얼음이어도(원작 커뮤니티 확인).
        /// 들어오면 이번 공격의 피해·효과는 그 포켓몬이 받는다(피해를 그 포켓몬으로 다시 센다 [가정 §5 #147 ⑥]). 프레임을 쌓았으면 true.
        /// </summary>
        internal static bool Substitute(GameState s, Ctx ctx, BattleState b)
        {
            if ((b.CardAsk & AskedSub) != 0) return false;
            b.CardAsk |= AskedSub;
            if (b.Move == -1 || b.ActiveSide < 0) return false;
            int k = 1 - b.ActiveSide;
            int seat = b.Sides[k].Owner;
            if (seat < 0 || seat >= s.SeatCount || b.Sides[k].Active < 0) return false;
            int holder = SubstituteOf(s, ctx, b, k);
            if (holder < 0) return false;
            Push(s, seat, AbSubstitute, holder, b.Sides[b.ActiveSide].Active);
            return true;
        }

        static int SubstituteOf(GameState s, Ctx ctx, BattleState b, int k)
        {
            int sub = ItemId(ctx, "item.substitute");
            var st = s.Seats[b.Sides[k].Owner];
            for (int i = 0; i < st.Party.Length; i++)
            {
                int p = st.Party[i];
                if (p < 0 || p == b.Sides[k].Active || !PokemonRules.Fights(st, i) || BattleRules.Taken(s, b.Sides[k], p)) continue; // 빼앗은 포켓몬은 그 배틀에 못 나온다(결정 84 T7 ③)
                var pk = s.Pokemon[p];
                if (!PokemonRules.IsFainted(ctx, pk) && PokemonRules.HoldsItem(pk, sub)) return p;
            }
            return -1;
        }

        // ── tourist B·R ──

        /// <summary>유물 전설 장소 8곳(유물 조건이 있는 전설 장소 — 고정물 순서 = 도장 칸 순서, rulings G1).</summary>
        public static List<int> Landmarks(Ctx ctx)
        {
            var r = new List<int>(8);
            var fixtures = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
                if (fixtures[f].Kind == FixtureKind.LegendarySite && fixtures[f].Artifact.Count > 0) r.Add(f);
            return r;
        }

        static bool TourDone(Ctx ctx, SeatState st) => st.Tour == (1 << Landmarks(ctx).Count) - 1;

        /// <summary>
        /// tourist B의 맵 행동: 선 칸이 아직 도장을 찍지 않은 유물 전설 장소면 UseAbility(그 고정물) — 「Once at each Artifact Legendary Location」(턴 제한 없음 · 유물이 없어도 된다 — 문구에 없다).
        /// </summary>
        static void TourAnswers(GameState s, Ctx ctx, int seat, List<ChoiceAnswer> into)
        {
            var st = s.Seats[seat];
            var marks = Landmarks(ctx);
            for (int i = 0; i < marks.Count; i++)
                if ((st.Tour & (1 << i)) == 0 && ctx.Fixtures.Fixtures[marks[i]].Cell == st.Cell) into.Add(ChoiceAnswer.Of(AnswerKind.UseAbility, marks[i]));
        }

        /// <summary>
        /// 도장: 「stamp it on your Landmark Tour sheet to search for the Legendary Pokémon there and make a clone of its card」 — 그 장소의 전설 가운데 전설 덱에 있는 첫 카드를 스냅으로(덱은 그대로).
        /// 덱에 없으면(잡혔거나 쓰러졌다) 도장만 [가정 §5 #147 ⑧]. 다 채우면 tourist R JP 30.
        /// </summary>
        static void Stamp(GameState s, Ctx ctx, Emitter fx, int seat, int fixture)
        {
            var st = s.Seats[seat];
            int i = Landmarks(ctx).IndexOf(fixture);
            st.Tour |= 1 << i;
            int card = LegendRules.SiteCard(s, ctx, fixture);
            if (card >= 0) st.Snaps.Add(card);
            Used(s, fx, seat, Blue, fixture);
            if (TourDone(ctx, st)) Completed(s, ctx, fx, seat);
        }

        static void Completed(GameState s, Ctx ctx, Emitter fx, int seat)
        {
            // tourist R 「if you complete your Landmark Tour sheet, gain 30 Journey Points」 — 다 채운 때와 빨강이 풀린 때 가운데 늦은 쪽에 한 번
            var st = s.Seats[seat];
            if (!Has(s, ctx, seat, "tourist", Red) || !TourDone(ctx, st)) return;
            st.Jp += TourJp;
            Used(s, fx, seat, Red);
        }

        /// <summary>tourist R의 기술 가르치기(정비 SetMove — 「You may teach the Legendary Snaps you have collected as moves to any Pokémon in your party that can learn them」): 그 기술이 가진 스냅의 시그니처면 그 스냅.</summary>
        public static int SnapOf(GameState s, Ctx ctx, int seat, int move)
        {
            if (!Seated(s, seat) || !Has(s, ctx, seat, "tourist", Red)) return -1;
            var snaps = s.Seats[seat].Snaps;
            for (int i = 0; i < snaps.Count; i++)
                if (ctx.Cards.Pokemon[snaps[i]].Signature == move) return i;
            return -1;
        }

        // ── 배틀 물음 처리기(AbilityAfter의 나머지) ──

        static EffectStepResult AbilityItems(GameState s, Ctx ctx, Emitter fx, EffectFrame f, int seat, int responder, int code)
        {
            var b = s.Battle;
            switch (code & 15)
            {
                case AbSubstitute:
                {
                    int k = b.Sides[0].Owner == seat ? 0 : 1;
                    if (b.Sides[k].Active < 0 || f.User < 0 || PokemonRules.PartyIndexOf(s.Seats[seat], f.User) < 0 || PokemonRules.IsFainted(ctx, s.Pokemon[f.User])) return EffectStepResult.Done;
                    var r = Option(s, ctx, fx, f, responder, SlotOf(Red), out bool yes);
                    if (r != EffectStepResult.Continue) return r;
                    if (!yes) return EffectStepResult.Done;
                    BattleFlow.SwapIn(s, ctx, fx, b, k, f.User);
                    if (Has(s, ctx, seat, "poke_maniac", Red)) Used(s, fx, seat, Red, f.User);
                    return EffectStepResult.Done;
                }
                default:
                    return EffectStepResult.Done;
            }
        }
    }
}
