using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 트레이너 카드가 연 물음·능력 단추의 이름(M6 「트레이너 카드 화면」 — 규칙은 rulings A33~A41): 머리 「카드 — 능력」(<see cref="Head"/>) · 고르는 값의 이름(<see cref="PickLabel"/>) ·
    /// 맵 행동 UseAbility 답의 이름·칸(<see cref="MapLabel"/>·<see cref="CellOf"/>). 값의 종류는 카드마다 하나다 — 처리기(TrainerRules.Battle·After·Items·Unlock·Map·Reward)가 고르게 하는 것.
    /// </summary>
    public static class CardAsk
    {
        /// <summary>고르기 값의 종류.</summary>
        public enum Value { Other, Status, Type, Color, Symbols, Cell, Card, Weather, Dragon, Pokemon }   // 옛 Face(psychic R 고르기)는 결정 131 · 옛 Amp(guitarist 앰프 배수)는 결정 134로 반응 창 줄(BattleScreen.PsychicRow · AmpRow)

        static readonly Dictionary<string, Value> Picks = new Dictionary<string, Value>
        {
            { "ninja_boy", Value.Status },        // R 독·혼란·졸음
            { "swimmer", Value.Type },            // R 벌레·비행
            { "triathlete", Value.Type },         // R 기술 타입 셋
            { "ginkgo_merchant", Value.Color },   // R 버릴 규토리
            { "veteran", Value.Symbols },         // R 상태 이상 심볼 1~2개
            { "team_rocket", Value.Cell },        // R 날아갈 칸
            { "youngster", Value.Card },          // R 바꿀 트레이너 카드
            { "parasol_lady", Value.Weather },    // B 첫 날씨
            { "dragon_tamer", Value.Dragon },     // B 약한 덱의 드래곤
            { "backpacker", Value.Pokemon },      // B 배틀 뒤 회복
            { "ruin_maniac", Value.Pokemon },     // B 유물을 붙일 포켓몬
        };

        // 고르기가 파랑 능력인 카드(나머지는 빨강)
        static readonly HashSet<string> BluePicks = new HashSet<string> { "parasol_lady", "dragon_tamer", "backpacker", "ruin_maniac" };

        /// <summary>출처가 트레이너 카드인 선택인가.</summary>
        public static bool FromCard(PendingChoice c) => c != null && c.Source.Kind == SourceKind.TrainerCard && c.Source.Id >= 0;

        /// <summary>카드 키(trainer_cards.json key). 모르는 번호면 빈 글.</summary>
        public static string CardKey(Ctx ctx, int card) => card >= 0 && card < ctx.Cards.TrainerCards.Count ? ctx.Cards.TrainerCards[card].Key : "";

        /// <summary>그 좌석 카드의 번호(없으면 −1).</summary>
        public static int CardOf(GameState view, int seat) => view != null && seat >= 0 && seat < view.Seats.Length ? view.Seats[seat].TrainerCard : -1;

        /// <summary>물음의 능력 칸(1 파랑 · 2 빨강): 「쓸까」는 Count(엔진이 싣는다 — 없으면 파랑), 고르기는 카드 표.</summary>
        public static int Slot(Ctx ctx, PendingChoice c)
        {
            if (c.Sub == ChoiceSub.EffectOption) return c.Count == 1 || c.Count == 2 ? c.Count : 1;
            if (c.Sub == ChoiceSub.EffectPick) return BluePicks.Contains(CardKey(ctx, c.Source.Id)) ? 1 : 2;
            return 1;   // Reward/TeachMove = scientist B
        }

        /// <summary>능력 이름의 문자열 키(<c>trainer_ability.카드.칸</c>).</summary>
        public static string AbilityKey(Ctx ctx, int card, int slot) => "trainer_ability." + CardKey(ctx, card) + "." + slot.ToString(CultureInfo.InvariantCulture);

        /// <summary>「카드 — 능력」.</summary>
        public static string Head(UiStage stage, Ctx ctx, int card, int slot) => stage.T("trainer_card." + CardKey(ctx, card)) + " — " + stage.T(AbilityKey(ctx, card, slot));   // 글리프 「—」만 코드에(두 언어 같다)

        public static string Head(UiStage stage, Ctx ctx, PendingChoice c) => Head(stage, ctx, c.Source.Id, Slot(ctx, c));

        /// <summary>능력 문구(마우스 오버) — 없으면 null.</summary>
        public static string Text(UiStage stage, Ctx ctx, int card, int slot) => stage.TOrNull(AbilityKey(ctx, card, slot) + ".text");

        public static string Text(UiStage stage, Ctx ctx, PendingChoice c) => Text(stage, ctx, c.Source.Id, Slot(ctx, c));

        /// <summary>그 카드의 고르기 값 종류.</summary>
        public static Value KindOf(Ctx ctx, PendingChoice c) => Picks.TryGetValue(CardKey(ctx, c.Source.Id), out var v) ? v : Value.Other;

        /// <summary>날씨 휠 칸의 이름(0 쾌청 · 1 눈 · 2 비 · 3 모래바람).</summary>
        public static string Weather(UiStage stage, int w) => stage.T("screen.card.weather." + w.ToString(CultureInfo.InvariantCulture));
        /// <summary>
        /// 카드가 쌓는 수 가운데 배틀 중에 쓰는 것(앰프 · 장작)의 칩 — 아래 판(보드 HUD 「행동」 머리 줄 · 배틀 독 차례 칸)에도 띄운다(결정 87 V9).
        /// 그 카드이거나 0보다 클 때만(트레이너 카드 판 <c>board-trainer-state</c>와 같은 조건). 요소 이름 <c>&lt;prefix&gt;-amp</c>·<c>-firewood</c>, 돌려주는 값 = 더한 칩 수.
        /// </summary>
        public static int StateChips(UiStage stage, Ctx ctx, VisualElement parent, SeatState seat, string prefix, TipSide side, VisualElement clear)
        {
            var inv = CultureInfo.InvariantCulture;
            string card = seat.TrainerCard >= 0 && seat.TrainerCard < ctx.Cards.TrainerCards.Count ? ctx.Cards.TrainerCards[seat.TrainerCard].Key : null;
            int n = 0;
            if (seat.Amp >= 0 || card == "guitarist")
            {
                var chip = Kit.Badge(stage.T("screen.trainer.state.amp").Replace("{n}", Math.Max(0, seat.Amp).ToString(inv)).Replace("{max}", TrainerRules.AmpMax.ToString(inv)), KitBadgeKind.Plain, Kit.F2);
                chip.name = prefix + "-amp";
                parent.Add(chip);
                stage.Tips.Hook(chip, stage.T("trainer_ability.guitarist.1"), stage.TOrNull("trainer_ability.guitarist.1.text"), side, reading: true, clear: clear);
                n++;
            }
            if (seat.Firewood > 0 || card == "kindler")
            {
                var chip = Kit.Badge(stage.T("screen.trainer.state.firewood").Replace("{n}", seat.Firewood.ToString(inv)), KitBadgeKind.Plain, Kit.F2);
                chip.name = prefix + "-firewood";
                parent.Add(chip);
                stage.Tips.Hook(chip, stage.T("trainer_ability.kindler.1"), stage.TOrNull("trainer_ability.kindler.1.text"), side, reading: true, clear: clear);
                n++;
            }
            return n;
        }

        /// <summary>날씨의 타입 보너스 한 줄(「물 타입 기술 위력 +2」 — 날씨 배지·배틀 기둥 칩의 마우스 오버 머리, 결정 87 V7 · V10 ①).</summary>
        public static string WeatherBonus(UiStage stage, int w)
        {
            w = Math.Max(0, Math.Min(w, TrainerRules.WeatherTypes.Length - 1));
            return stage.T("screen.board.weather.bonus").Replace("{type}", stage.T("ui.types." + TrainerRules.WeatherTypes[w])).Replace("{n}", TrainerRules.WeatherPower.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>고르기 값 하나의 이름.</summary>
        public static string PickLabel(UiStage stage, Ctx ctx, GameState view, PendingChoice c, int value)
        {
            var inv = CultureInfo.InvariantCulture;
            switch (KindOf(ctx, c))
            {
                case Value.Status: return stage.T(MonParts.StatusKey((StatusEffect)value) ?? "screen.status.none");
                case Value.Type: return stage.T("ui.types." + (PokemonType)value);
                case Value.Color: return stage.T("screen.apricorn." + ((ApricornColor)value).ToString().ToLowerInvariant());
                case Value.Symbols: return Sinnoh.Fx.KoJosa.Fix(stage.T("screen.card.pick.symbols").Replace("{n}", value.ToString(inv)));
                case Value.Card: return stage.T("trainer_card." + CardKey(ctx, value));
                case Value.Weather: return Weather(stage, value);
                case Value.Dragon: return value >= 0 && value < ctx.Cards.Pokemon.Count ? stage.T("pokemon." + ctx.Cards.Pokemon[value].Key) : value.ToString(inv);
                case Value.Pokemon: return PokemonName(stage, ctx, view, value);
                default: return value.ToString(inv);
            }
        }

        /// <summary>고르기 값의 그림 키(규토리 색 · 타입 — 이미 있는 아이콘, 없으면 null).</summary>
        public static string IconKey(Ctx ctx, PendingChoice c, int value)
        {
            switch (KindOf(ctx, c))
            {
                case Value.Color: return value >= 0 && value < SeatState.ApricornColors ? EncounterScreen.ApricornKey((ApricornColor)value) : null;
                case Value.Type: return Enum.IsDefined(typeof(PokemonType), value) ? TypeKey((PokemonType)value) : null;
                default: return null;
            }
        }

        static string TypeKey(PokemonType t) => IconArt.Type(t);

        /// <summary>맵 UseAbility 답의 그림 키(gardener · picnicker = 규토리 색, 아니면 null).</summary>
        public static string MapIconKey(string cardKey, ChoiceAnswer a)
        {
            if (IsWeather(a)) return null;
            if ((cardKey == "gardener" || cardKey == "picnicker") && a.Arg >= 0 && a.Arg < SeatState.ApricornColors) return EncounterScreen.ApricornKey((ApricornColor)a.Arg);
            // psychic B의 공물 · 공중날기: 안쪽 답의 규토리 색
            if (cardKey == "psychic" && IsReach(a) && ((AnswerKind)a.Arg == AnswerKind.Offer || (AnswerKind)a.Arg == AnswerKind.Fly) && a.Values[1] >= 0 && a.Values[1] < SeatState.ApricornColors)
                return EncounterScreen.ApricornKey((ApricornColor)a.Values[1]);
            return null;
        }

        static bool IsReach(ChoiceAnswer a) => a != null && a.Kind == AnswerKind.UseAbility && a.Values.Count >= 2;

        /// <summary>
        /// psychic B 값 단추의 묶음(M6 화면 교차 검토 #1 — 한 칸에 같은 종류의 답이 여럿이면 한 단계 더 고른다): level 0 = 공물 · 은행상회 · 공중날기는 종류로 묶는다,
        /// level 1 = 공중날기는 목적지 마을로 묶는다. −1 = 묶지 않는다(단추 하나).
        /// </summary>
        public static int ReachGroup(ChoiceAnswer a, int level)
        {
            if (!IsReach(a)) return -1;
            var kind = (AnswerKind)a.Arg;
            if (level == 0) return kind == AnswerKind.Offer || kind == AnswerKind.Ginkgo || kind == AnswerKind.Fly || kind == AnswerKind.UseAbility ? (int)kind : -1;   // UseAbility = 떨어진 사당의 날씨 바꾸기(T33)
            if (level == 1) return kind == AnswerKind.Fly ? a.Values[0] : -1;
            return -1;
        }

        /// <summary>psychic B 묶음 단추의 이름: level 0 = 종류(보드 단추와 같은 말) · level 1 = 공중날기 목적지 마을.</summary>
        public static string ReachGroupLabel(UiStage stage, Ctx ctx, ChoiceAnswer a, int level) =>
            level >= 1 && (AnswerKind)a.Arg == AnswerKind.Fly ? stage.T(ctx.Fixtures.Fixtures[a.Values[0]].Key) : stage.T(ReachKey((AnswerKind)a.Arg));

        /// <summary>
        /// psychic B 값 단추 하나의 이름(level = 묶음을 몇 단계 들어왔나): 종류 + 안쪽 답의 값 — 공물 = 규토리 색(+ 「JP 7 더」) · 공중날기 = 목적지 마을 + 색 · 은행상회 = 줄 ·
        /// 찾아보기 = 고정물(전설 장소 등) · 도전 = 상대. 들어온 단계의 말은 뺀다(공중날기 안 = 마을 + 색, 그 마을 안 = 색).
        /// </summary>
        public static string ReachLabel(UiStage stage, Ctx ctx, ChoiceAnswer a, int level, Func<int, string> seatName)
        {
            var kind = (AnswerKind)a.Arg;
            int target = a.Values[0], arg = a.Values[1];
            string head = level == 0 ? stage.T(ReachKey(kind)) : null;
            string value = null;
            switch (kind)
            {
                case AnswerKind.Offer:
                    value = Color(stage, arg);
                    if (a.Values.Count > 2) value += " " + stage.T("screen.card.reach.extra").Replace("{n}", a.Values[2].ToString(CultureInfo.InvariantCulture));
                    break;
                case AnswerKind.Fly:
                    value = level >= 2 ? Color(stage, arg) : stage.T(ctx.Fixtures.Fixtures[target].Key) + " " + Color(stage, arg);
                    break;
                case AnswerKind.Ginkgo:
                    value = stage.T(target == BankRules.Both ? "screen.bank.both" : target == BankRules.Top ? "screen.card.reach.bank_top" : "screen.card.reach.bank_bottom");
                    break;
                case AnswerKind.Search:
                case AnswerKind.Move:   // 떨어진 왜곡 게이트 건너기(Arg = 건너편 게이트 — T33)
                    if (arg >= 0 && arg < ctx.Fixtures.Fixtures.Count) value = stage.T(ctx.Fixtures.Fixtures[arg].Key);
                    break;
                case AnswerKind.UseAbility:   // 떨어진 사당의 날씨 바꾸기(Arg = 새 날씨 — T33)
                    value = Weather(stage, arg);
                    break;
                case AnswerKind.Challenge:
                case AnswerKind.ProposeTrade:   // 거래 표(TradeMark) — Values[0] = 상대 좌석
                    value = target == SeatId.Galactic ? stage.T("screen.challenge.galactic") : target >= 0 && seatName != null ? seatName(target) : null;
                    break;
            }
            if (head == null) return value ?? stage.T(ReachKey(kind));
            return value == null ? head : head + " " + value;
        }

        static string Color(UiStage stage, int color) =>
            color >= 0 && color < SeatState.ApricornColors ? stage.T("screen.apricorn." + ((ApricornColor)color).ToString().ToLowerInvariant()) : "";

        /// <summary>값 단추: 그림이 있으면 그림 + 이름(<c>sn-card-pick</c>), 없으면 이름만. 타입은 그림만(그림 안에 타입 이름이 있다 — 결정 87 V1 ①, 「벌레 벌레」로 겹쳐 읽혔다).</summary>
        public static Button Button(string label, string iconKey, KitButtonKind kind, Action click)
        {
            if (iconKey == null) return Kit.Button(label, kind, click);
            // 글 없는 단추 + 가로줄(그림 · 이름) — 단추 글에 자식을 더하면 글 자리 셈이 빠져 단추 폭이 안쪽 여백만 남는다
            bool type = iconKey.Contains("/type_");
            var b = Kit.Button("", kind, click);
            b.AddToClassList("sn-card-pick");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList(type ? "sn-card-pick-type" : "sn-card-pick-icon");
            var tex = GenAssets.Load<Texture2D>(iconKey);
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            b.Add(icon);
            if (type && tex != null)
            {
                b.AddToClassList("sn-card-pick-only");
                return b;
            }
            var text = new Label(label) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("sn-card-pick-text");
            b.Add(text);
            return b;
        }

        /// <summary>카드 값(youngster R 바꿀 카드)의 마우스 오버: 두 능력 이름(파랑 · 빨강). 카드 값이 아니면 null.</summary>
        public static string CardTip(UiStage stage, Ctx ctx, PendingChoice c, int value) =>
            KindOf(ctx, c) == Value.Card ? stage.T(AbilityKey(ctx, value, 1)) + "\n" + stage.T(AbilityKey(ctx, value, 2)) : null;

        /// <summary>보기의 포켓몬 이름(폼 꼬리 뺌).</summary>
        public static string PokemonName(UiStage stage, Ctx ctx, GameState view, int pokemon) =>
            view != null && pokemon >= 0 && pokemon < view.Pokemon.Count && view.Pokemon[pokemon].Def >= 0 ? BoardHud.ShortName(stage.T("pokemon." + ctx.Cards.Pokemon[view.Pokemon[pokemon].Def].Key)) : "";

        // ── 맵 행동의 UseAbility 답 ──

        /// <summary>날씨 바꾸기(누구나 — 날씨 휠, rulings A41)인가. 아니면 그 좌석 카드의 파랑 능력이다.</summary>
        public static bool IsWeather(ChoiceAnswer a) => a.Kind == AnswerKind.UseAbility && a.Target == TrainerRules.WeatherTarget;

        /// <summary>
        /// psychic B 「염력」 거래의 표(사용자 결정 84 T12 ② 나 — <see cref="TradeRules.Telekinesis"/>): 거래 내용은 합법 답으로 늘어놓을 수 없어 엔진 답이 아니라 값 단추 줄에만 끼우는 표다
        /// (꼴은 psychic 답과 같다 — Target = 상대의 칸 · Arg = ProposeTrade · Values = [상대 좌석, −1]). 누르면 그 좌석으로 거래 창을 연다(<see cref="GameFlow.OpenTrade"/>). 엔진에 보내지 않는다.
        /// </summary>
        public static ChoiceAnswer TradeMark(GameState view, int seat)
        {
            var a = ChoiceAnswer.Of(AnswerKind.UseAbility, view.Seats[seat].Cell, (int)AnswerKind.ProposeTrade);
            a.Values.Add(seat);
            a.Values.Add(-1);
            return a;
        }

        /// <summary><see cref="TradeMark"/>로 만든 표인가.</summary>
        public static bool IsTradeMark(ChoiceAnswer a) => IsReach(a) && a.Arg == (int)AnswerKind.ProposeTrade;

        /// <summary>답이 가리키는 보드 칸(없으면 −1): kindler·ruin_maniac = 그 토큰의 칸 · psychic·picnicker = Target 칸.</summary>
        public static int CellOf(Ctx ctx, GameState view, string cardKey, ChoiceAnswer a)
        {
            if (IsWeather(a)) return -1;
            switch (cardKey)
            {
                case "kindler":
                case "ruin_maniac":
                    return a.Target >= 0 && a.Target < view.Tokens.Count ? view.Tokens[a.Target].Cell : -1;
                case "psychic":
                case "picnicker":
                    return a.Target;
                default:
                    return -1;
            }
        }

        /// <summary>맵 행동 UseAbility 답 하나의 단추 이름(칸을 고르는 답은 칸 이름을 화면이 붙인다).</summary>
        public static string MapLabel(UiStage stage, Ctx ctx, string cardKey, ChoiceAnswer a)
        {
            if (IsWeather(a)) return Weather(stage, a.Arg);
            switch (cardKey)
            {
                case "gardener":
                case "picnicker":
                    return stage.T("screen.apricorn." + ((ApricornColor)a.Arg).ToString().ToLowerInvariant());
                case "psychic":
                    return stage.T(ReachKey((AnswerKind)a.Arg));
                default:
                    return stage.T("screen.card.use");
            }
        }

        // psychic B의 안쪽 답 이름(그 칸에서 할 수 있는 상호작용 — 보드 단추와 같은 말)
        static string ReachKey(AnswerKind kind)
        {
            switch (kind)
            {
                case AnswerKind.Search: return "screen.board.action.search";
                case AnswerKind.TakeApricorns: return "screen.board.action.take_apricorns";
                case AnswerKind.Accept: return "screen.board.action.reveal";
                case AnswerKind.Challenge: return "screen.board.action.challenge";
                case AnswerKind.Offer: return "screen.board.action.offer";
                case AnswerKind.VillageHeal: return "screen.village.heal";
                case AnswerKind.Fly: return "screen.village.fly";
                case AnswerKind.Ginkgo: return "screen.bank.title";
                case AnswerKind.ProposeTrade: return "screen.board.action.trade";
                case AnswerKind.Move: return "screen.board.action.move";
                case AnswerKind.UseAbility: return "item.weather_wheel";
                default: return "screen.card.use";
            }
        }
    }
}
