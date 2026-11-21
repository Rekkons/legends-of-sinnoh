using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 팬 폼 효과 표(03 §2-1 · §8 #8, 결정 5·74 ③ — Gen <c>Sprites/Static/fan_forms.json</c> = 추적 표 <c>Tools/assets/tables/fan_forms.json</c> 그대로): (종, 폼) → 기본 도트에 입히는 효과.
    /// aura = 도트 둘레 오라(<c>AuraBeats.StayPx</c>) + 옅은 색조 · glow = 오라 + 물들임 · tint = 물들임만(결정 80 L15 나 — 오라를 두껍게, 도트에도 옅은 색조).
    /// </summary>
    public sealed class FanForms
    {
        public const string TableKey = "Sprites/Static/fan_forms.json";

        [Serializable] sealed class Row { public string key; public string effect; public string color; public float strength; }
        [Serializable] sealed class Table { public Row[] rows; }

        readonly Dictionary<string, Row> rows = new Dictionary<string, Row>(StringComparer.Ordinal);

        public int Count => rows.Count;

        public static FanForms Load()
        {
            var forms = new FanForms();
            var json = GenAssets.Load<TextAsset>(TableKey);
            if (json == null) return forms;
            var table = JsonUtility.FromJson<Table>(json.text);
            if (table != null && table.rows != null)
                foreach (var r in table.rows)
                    if (r != null && r.key != null) forms.rows[r.key] = r;
            return forms;
        }

        /// <summary>그 포켓몬 키(「389:2」 — @ 꼬리는 뗀다)가 팬 폼이면 오라 색·두께와 물들임을 준다.</summary>
        public bool TryGet(string pokemonKey, out Color32 aura, out int px, out Color32 tint)
        {
            aura = tint = default;
            px = 0;
            if (pokemonKey == null || !rows.TryGetValue(MonArt.ArtKey(pokemonKey), out var r) || !ColorUtility.TryParseHtmlString(r.color, out var c)) return false;
            Color32 c32 = c;
            if (r.effect != "tint")
            {
                aura = new Color32(c32.r, c32.g, c32.b, 0xFF);
                px = AuraBeats.StayPx;
            }
            float wash = r.effect == "aura" ? AuraBeats.StayTint : r.strength * (r.effect == "glow" ? 0.5f : 1f);
            tint = new Color32(c32.r, c32.g, c32.b, (byte)Mathf.RoundToInt(Mathf.Clamp01(wash) * 255f));
            return true;
        }
    }

    /// <summary>
    /// 배틀 화면의 M5 「맵 결과·배틀 안 연출」 몫(04 §8-8 24·46·74~79·81행, S55): 알파 스트라이크의 타입 고르는 줄 · 기둥의 덧칩(우두머리 · 은하의 응징 · 교체 불가 · 둘째 상태 이상 · 무리 진행) ·
    /// DS 층의 오라·폼 바꾸기·빛기둥 · 결과 창의 승리 보상. 박자는 <c>Sinnoh.Fx</c> <c>ResultStaging.cs</c>.
    /// </summary>
    public sealed partial class BattleScreen
    {
        // ── 알파 스트라이크(76행 — rulings A26: 답 = 배울 수 있는 타입마다 UseMove(Target −3, Values [타입])) ──

        bool alphaOpen;
        readonly List<ChoiceAnswer> alphaTypes = new List<ChoiceAnswer>();

        /// <summary>알파 스트라이크의 타입 줄이 떠 있나 · 그 줄의 답(위에서부터 — 확인용).</summary>
        public bool AlphaOpen => alphaOpen;
        public IReadOnlyList<ChoiceAnswer> AlphaTypes => alphaTypes;

        /// <summary>기술 줄의 「알파 스트라이크」: 기술 줄 자리를 타입 줄로 바꾼다(엔진에 답을 내지 않는다).</summary>
        public bool OpenAlpha()
        {
            if (!Open || alphaOpen) return false;
            alphaOpen = true;
            Refresh();
            return true;
        }

        /// <summary>타입 줄의 [뒤로].</summary>
        public bool CloseAlpha()
        {
            if (!alphaOpen) return false;
            alphaOpen = false;
            Refresh();
            return true;
        }

        /// <summary>타입 줄을 눌렀다(위에서부터 0): 그 타입의 알파 스트라이크 답을 낸다.</summary>
        public bool PressAlpha(int index)
        {
            if (!Open || !alphaOpen || index < 0 || index >= alphaTypes.Count) return false;
            var choice = flow.Client.Choice(ChoiceKind.BattleAction);
            if (choice == null) return false;
            stage.Tips.Hide();
            var answer = alphaTypes[index];
            alphaOpen = false;
            return flow.Answer(choice, answer).Accepted;
        }

        // ── 대기 포켓몬의 기술(lunge·acrobatics 「This move can be used from your party」 — rulings G20: 답 = UseMove(기술, Arg = 그 대기 포켓몬)) ──

        bool partyOpen;
        readonly List<ChoiceAnswer> partyMoves = new List<ChoiceAnswer>();

        /// <summary>대기 포켓몬의 기술 줄이 떠 있나 · 그 줄의 답(엔진 순서 — 확인용) · 차례 칸의 [대기 포켓몬 기술]이 켜져 있나(그런 합법 답이 있고 입력이 열려 있다).</summary>
        public bool PartyOpen => partyOpen;
        public IReadOnlyList<ChoiceAnswer> PartyMoves => partyMoves;
        public bool PartyMovesOn { get; private set; }

        /// <summary>대기 포켓몬의 기술 줄의 쪽(기술 줄과 같은 넓은 줄 셋씩 — 넷부터는 차례 칸의 [다음]으로 넘긴다)과 쪽 수.</summary>
        public int PartyPage { get; private set; }
        public int PartyPages { get; private set; }

        static bool IsPartyMove(ChoiceAnswer a) => a.Kind == AnswerKind.UseMove && a.Arg >= 0;

        /// <summary>차례 칸의 [대기 포켓몬 기술]: 기술 줄 자리를 대기 포켓몬의 기술 줄로 바꾼다(엔진에 답을 내지 않는다).</summary>
        public bool OpenPartyMoves()
        {
            if (!PartyMovesOn || partyOpen) return false;
            partyOpen = true;
            PartyPage = 0;
            bag = alphaOpen = false;
            Refresh();
            return true;
        }

        /// <summary>대기 포켓몬의 기술 줄의 [뒤로].</summary>
        public bool ClosePartyMoves()
        {
            if (!partyOpen) return false;
            partyOpen = false;
            Refresh();
            return true;
        }

        /// <summary>차례 칸의 [다음 n/m]: 대기 포켓몬의 기술 줄의 다음 쪽(끝이면 처음으로).</summary>
        public bool NextPartyMoves()
        {
            if (!partyOpen || PartyPages < 2) return false;
            PartyPage = (PartyPage + 1) % PartyPages;
            Refresh();
            return true;
        }

        /// <summary>대기 포켓몬의 기술 줄을 눌렀다(<see cref="PartyMoves"/>의 색인 — 0부터, 지금 쪽의 줄만): 그 포켓몬이 나오며 그 기술을 쓰는 답을 낸다.</summary>
        public bool PressPartyMove(int index)
        {
            if (!Open || !partyOpen || index < 0 || index >= partyMoves.Count || index / MoveRows != PartyPage) return false;   // 지금 쪽에 보이는 줄만(길잡이도 쪽을 넘겨 누른다)
            var choice = flow.Client.Choice(ChoiceKind.BattleAction);
            if (choice == null) return false;
            stage.Tips.Hide();
            var answer = partyMoves[index];
            partyOpen = false;
            return flow.Answer(choice, answer).Accepted;
        }

        // 대기 포켓몬의 기술 줄(지금 쪽의 셋): 타입 그림 · 기술 이름 · 쓰는 포켓몬 · 위력 · 상성 배지. [뒤로]·[다음]은 차례 칸(FillDock)
        void FillPartyMoves(VisualElement list, BattleFeed feed, BattleMon foe, List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            var state = feed.Source;
            foreach (var a in legal)
                if (IsPartyMove(a)) partyMoves.Add(a);
            PartyPages = (partyMoves.Count + MoveRows - 1) / MoveRows;
            if (PartyPage >= PartyPages) PartyPage = 0;
            int shown = 0;
            for (int i = PartyPage * MoveRows; i < partyMoves.Count && shown < MoveRows; i++, shown++)
            {
                int index = i, move = partyMoves[i].Target;
                var user = feed.MonOf(partyMoves[i].Arg);
                string nameKey = BattleFeed.MoveNameKey(ctx, move);
                var type = BattleRules.MoveType(ctx, move);
                var row = Add(list, new VisualElement(), "battle-partymove-" + (i + 1).ToString(inv), "sn-battle-move");
                TypeIcon(row, IconArt.Type(type), stage.T("ui.types." + type));
                Add(row, Kit.Text(stage.T(nameKey), Kit.F2), null, "sn-battle-move-name");
                if (user.Exists) Add(row, Kit.Badge(MonName(user), KitBadgeKind.Plain, Kit.F2), "battle-partymove-mon-" + (i + 1).ToString(inv), null);
                Add(row, Kit.Text(stage.T("screen.battle.power"), Kit.F2), null, "kit-muted");
                bool unknown = ctx.Cards.Moves[move].AttackStrength == null;
                int basePower = BattleRules.MoveStrength(ctx, move), power = basePower;
                if (!unknown && state != null && user.Exists) power = BattleRules.RollPower(state, ctx, move, user.Id, foe.Id, new List<Mod>());
                row.Add(Kit.Pill(null, unknown ? "?" : power.ToString(inv), unknown || power == basePower ? KitTone.Base : power < basePower ? KitTone.Low : KitTone.High));
                if (foe.Exists && type != PokemonType.Typeless)
                {
                    var types = ctx.Cards.Pokemon[foe.Def].Types;
                    int bonus = TypeChart.Bonus(type, types[0], types.Count > 1 ? types[1] : PokemonType.Typeless);
                    if (bonus != 0) row.Add(Kit.Badge(Signed(bonus), bonus > 0 ? KitBadgeKind.Ok : KitBadgeKind.Red, Kit.F2));
                }
                row.SetEnabled(open);
                stage.OnTap(row, SoundKeys.Decide, e => PressPartyMove(index));
                if (open) stage.Tips.Hook(row, stage.T(nameKey), stage.TOrNull(nameKey + ".text"), TipSide.Above, reading: true, clear: dock);
            }
            for (int i = shown; i < MoveRows; i++) Add(list, new VisualElement(), null, "sn-battle-move").AddToClassList("sn-battle-move-empty");
        }

        // ── 트레이너 카드의 배틀 행동(M6 「트레이너 카드 화면」 — UseAbility: lady R 완전 회복 Target = 포켓몬 · gentleman R 프레셔스 볼 Target = CherishTarget) ──

        /// <summary>차례 칸·운명 창의 요소 이름: [고급상처약] · 그 줄의 [뒤로] · [프레셔스 볼] · 운명 창의 회복 줄 · 차례 칸의 「카드 — 능력」 머리.</summary>
        public const string AbilityButton = "battle-ability", AbilityBack = "battle-ability-back", CherishButton = "battle-cherish", FatePotion = "battle-fate-potion", CardHead = "battle-card-head";

        bool abilityOpen;
        readonly List<ChoiceAnswer> abilityPicks = new List<ChoiceAnswer>();

        /// <summary>회복할 포켓몬 줄이 떠 있나 · 그 줄의 답 · 차례 칸의 능력 단추·[프레셔스 볼]이 켜져 있나.</summary>
        public bool AbilityOpen => abilityOpen;
        public IReadOnlyList<ChoiceAnswer> AbilityPicks => abilityPicks;
        public bool AbilityOn { get; private set; }
        public bool CherishOn { get; private set; }

        /// <summary>회복할 포켓몬 줄은 2×3 한 쪽(결정 87 V3 — 파티 여섯이 다 든다, 쪽 넘김 없음): 줄 판의 클래스.</summary>
        public const string AbilityGrid = "sn-battle-moves-grid";

        static bool IsCherish(ChoiceAnswer a) => a.Kind == AnswerKind.UseAbility && a.Target == TrainerRules.CherishTarget;
        static bool IsPotion(ChoiceAnswer a) => a.Kind == AnswerKind.UseAbility && a.Target >= 0;

        /// <summary>차례 칸의 능력 단추: 기술 줄 자리를 회복할 포켓몬 줄로 바꾼다(엔진에 답을 내지 않는다).</summary>
        public bool OpenAbility()
        {
            if (!AbilityOn || abilityOpen) return false;
            abilityOpen = true;
            bag = alphaOpen = partyOpen = false;
            Refresh();
            return true;
        }

        /// <summary>회복할 포켓몬 줄의 [뒤로].</summary>
        public bool CloseAbility()
        {
            if (!abilityOpen) return false;
            abilityOpen = false;
            Refresh();
            return true;
        }

        /// <summary>회복할 포켓몬 줄을 눌렀다(<see cref="AbilityPicks"/>의 색인 — 지금 쪽의 줄만): 그 답을 낸다(턴을 쓰지 않는다 — 다시 내 행동 고르기).</summary>
        public bool PressAbility(int index)
        {
            if (!Open || !abilityOpen || index < 0 || index >= abilityPicks.Count) return false;
            var choice = flow.Client.Choice(ChoiceKind.BattleAction);
            if (choice == null) return false;
            stage.Tips.Hide();
            var answer = abilityPicks[index];
            abilityOpen = false;
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>차례 칸의 [프레셔스 볼]: 굴리지 않고 잡는 답을 낸다.</summary>
        public bool PressCherish()
        {
            if (!Open || !CherishOn) return false;
            var choice = flow.Client.Choice(ChoiceKind.BattleAction);
            var answer = choice != null ? flow.Client.Legal(choice).Find(IsCherish) : null;
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>운명 창의 회복 줄(lady R): 그 포켓몬을 회복하는 답을 낸다.</summary>
        public bool PressFatePotion(int pokemon)
        {
            if (!Open || !AskingFate) return false;
            var choice = flow.Client.Choice(ChoiceKind.Reaction);
            var answer = choice != null ? flow.Client.Legal(choice).Find(a => a.Kind == AnswerKind.UseAbility && a.Target == pokemon) : null;
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        // ── psychic R(사용자 결정 131 — 굴린 뒤 반응 창의 반응, 운명 줄과 같은 선택) ──

        /// <summary>운명 줄의 psychic R 줄(능력 이름 + 새 눈 단추 <c>battle-fate-psychic-status</c>·<c>-hit</c>·<c>-crit</c>). 바꿀 주사위는 위 주사위 줄(<see cref="ToggleFateDie"/>)에서 하나 고른다.</summary>
        public const string PsychicRow = "battle-fate-psychic";

        /// <summary>새 눈 단추의 꼬리(면 순서 — 심볼 · 일반 · 크리티컬) = 글 키 <c>screen.battle.fate.psychic.&lt;꼬리&gt;</c>.</summary>
        static readonly string[] PsychicFaces = { "status", "hit", "crit" };

        // 합법 답에 psychic R(UseAbility — Target = 바꿀 면, Arg = 새 면)이 있나: 운명 창의 UseAbility는 그 좌석 카드가 psychic일 때만 이 뜻이다
        bool PsychicOn(List<ChoiceAnswer> legal) =>
            CardAsk.CardKey(flow.Ctx, CardAsk.CardOf(flow.Client.View, flow.Client.Seat)) == "psychic" && legal.Exists(a => a.Kind == AnswerKind.UseAbility);

        /// <summary>지금 고른 주사위(하나)를 그 눈으로 바꾸는 psychic R 답(합법 답에 없거나 주사위를 하나만 고르지 않았으면 null).</summary>
        public ChoiceAnswer PsychicAnswer(int to)
        {
            var choice = flow.Client != null ? flow.Client.Choice(ChoiceKind.Reaction) : null;
            var faces = flow.Client != null && flow.Client.View != null && flow.Client.View.Battle != null ? flow.Client.View.Battle.Faces : null;
            if (choice == null || choice.Sub != ChoiceSub.Fate || faces == null || fateDice.Count != 1) return null;
            int die = fateDice.Min;
            if (die < 0 || die >= faces.Count) return null;
            var legal = flow.Client.Legal(choice);
            if (!PsychicOn(legal)) return null;
            return legal.Find(a => a.Kind == AnswerKind.UseAbility && a.Target == (int)faces[die] && a.Arg == to);
        }

        /// <summary>psychic R 줄의 새 눈 단추: 고른 주사위를 그 눈으로 바꾸는 답을 낸다. 받아들여졌으면 true.</summary>
        public bool PressPsychic(int to)
        {
            if (!Open || !AskingFate) return false;
            var answer = PsychicAnswer(to);
            var choice = flow.Client.Choice(ChoiceKind.Reaction);
            if (answer == null || choice == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        // 길잡이 · 프로브(Press): 답의 면을 가진 첫 주사위를 고르고 새 눈 단추를 누른다
        bool PressPsychicAnswer(ChoiceAnswer answer)
        {
            var faces = flow.Client.View.Battle != null ? flow.Client.View.Battle.Faces : null;
            int die = faces != null ? faces.IndexOf((AttackDieFace)answer.Target) : -1;
            if (!Open || die < 0) return false;
            fateDice.Clear();
            fateDice.Add(die);
            return PressPsychic(answer.Arg);
        }

        // ── guitarist B·R 앰프(사용자 결정 134 — psychic R과 같은 꼴, 운명 줄과 같은 선택) ──

        /// <summary>운명 줄의 앰프 줄(능력 이름 + 단추 <c>battle-fate-amp-1</c>(데미지 +n) · <c>-2</c>(전기 기술 · guitarist R의 두 배)).</summary>
        public const string AmpRow = "battle-fate-amp";

        // 합법 답의 앰프 답(UseAbility — Target = TrainerRules.AmpTarget, Arg = 배수)
        static bool IsAmp(ChoiceAnswer a) => a.Kind == AnswerKind.UseAbility && a.Target == TrainerRules.AmpTarget;

        /// <summary>앰프 줄의 단추: 그 배수(1 · 2)로 앰프를 쓰는 답을 낸다. 받아들여졌으면 true.</summary>
        public bool PressAmp(int times)
        {
            if (!Open || !AskingFate) return false;
            var choice = flow.Client.Choice(ChoiceKind.Reaction);
            var answer = choice != null ? flow.Client.Legal(choice).Find(a => IsAmp(a) && a.Arg == times) : null;
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        // 회복할 포켓몬 줄: 도트 이름 · 체력(지금/최대) · 상태 이상 배지. [뒤로]는 차례 칸(FillDock)
        void FillAbility(VisualElement list, BattleFeed feed, List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            foreach (var a in legal)
                if (IsPotion(a)) abilityPicks.Add(a);
            list.AddToClassList(AbilityGrid);
            int shown = 0, cells = MoveRows * 2;
            for (int i = 0; i < abilityPicks.Count && shown < cells; i++, shown++)
            {
                int index = i, id = abilityPicks[i].Target;
                var p = view.Pokemon[id];
                var row = Add(list, new VisualElement(), "battle-ability-" + (i + 1).ToString(inv), "sn-battle-move");
                row.AddToClassList("sn-battle-move-half");
                Add(row, Kit.Text(CardAsk.PokemonName(stage, ctx, view, id), Kit.F2), null, "sn-battle-move-name");
                int max = PokemonRules.MaxHp(ctx, p);
                row.Add(Kit.Pill(null, Math.Max(0, max - p.Damage).ToString(inv) + "/" + max.ToString(inv), p.Damage > 0 ? KitTone.Low : KitTone.Base));
                string status = MonParts.StatusKey(p.Status);
                if (status != null) row.Add(Kit.Badge(stage.T(status), KitBadgeKind.Red, Kit.F1));
                row.SetEnabled(open);
                stage.OnTap(row, SoundKeys.Decide, e => PressAbility(index));
            }
            for (int i = shown; i < cells; i++)
            {
                var empty = Add(list, new VisualElement(), null, "sn-battle-move");
                empty.AddToClassList("sn-battle-move-empty");
                empty.AddToClassList("sn-battle-move-half");
            }
        }

        static PokemonType AlphaType(ChoiceAnswer a) => a.Values != null && a.Values.Count > 0 ? (PokemonType)a.Values[0] : PokemonType.Typeless;

        // 타입 줄: 볼 줄과 같은 꼴(두 줄씩) — 타입 그림 · 이름 · 상성 배지, 끝에 [뒤로]
        void FillAlpha(VisualElement list, BattleMon foe, List<ChoiceAnswer> legal, bool open)
        {
            var inv = CultureInfo.InvariantCulture;
            list.AddToClassList("sn-battle-balls");
            foreach (var a in legal)
            {
                if (a.Kind != AnswerKind.UseMove || a.Target != BattleRules.AlphaStrike || a.Arg >= 0) continue;
                int index = alphaTypes.Count;
                alphaTypes.Add(a);
                var type = AlphaType(a);
                var row = Add(list, new VisualElement(), "battle-alpha-" + (index + 1).ToString(inv), "sn-battle-move");
                row.AddToClassList("sn-battle-ball");
                TypeIcon(row, IconArt.Type(type), stage.T("ui.types." + type));
                Add(row, Kit.Text(stage.T("ui.types." + type), Kit.F2), null, "sn-battle-move-name");
                if (foe.Exists && type != PokemonType.Typeless)
                {
                    var types = flow.Ctx.Cards.Pokemon[foe.Def].Types;
                    int bonus = TypeChart.Bonus(type, types[0], types.Count > 1 ? types[1] : PokemonType.Typeless);
                    if (bonus != 0) row.Add(Kit.Badge(Signed(bonus), bonus > 0 ? KitBadgeKind.Ok : KitBadgeKind.Red, Kit.F2));
                }
                row.SetEnabled(open);
                stage.OnTap(row, SoundKeys.Decide, e => PressAlpha(index));
            }
            var back = Add(list, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, () => CloseAlpha()), "battle-alpha-back", "sn-battle-ball");
            back.SetEnabled(open);
        }

        // ── 드러난 폼(78·79행): 보기는 답과 함께 새 폼이다 — 폼이 바뀌는 연출이 닿기 전에는 옛 폼으로 그린다 ──

        BattleMon Reform(BattleMon mon)
        {
            var feed = flow.Battle;
            if (feed == null || !mon.Exists) return mon;
            bool ascended = false;
            int before = -1;
            foreach (var e in feed.Events)
            {
                if (!(e is FormChanged fc) || fc.PokemonId != mon.Id) continue;
                if (hidden.Contains(e)) { if (before < 0) before = fc.Before; }
                else if (fc.Temporary && fc.Source.Kind == SourceKind.Rule && fc.Source.Id == (int)RuleSource.CelestialAura) ascended = true;
            }
            if (before >= 0)
            {
                mon.Def = before;
                mon.MaxHp = flow.Ctx.Cards.Pokemon[before].Health + (mon.MaxHp - flow.Ctx.Cards.Pokemon[feed.MonOf(mon.Id).Def].Health);
            }
            // 둘째 폼은 완전 회복해 나온다(체력을 싣는 이벤트가 없다 — 쓰러질 때 붙잡힌 0을 보기 값으로 푼다)
            if (ascended && mon.Hp <= 0)
            {
                int view = feed.MonOf(mon.Id).Hp;
                if (view > 0) mon.Hp = view;
            }
            return mon;
        }

        // ── 기둥의 덧칩 ──

        VisualElement retribution, orb;
        readonly VisualElement[] locks = new VisualElement[2];
        readonly List<VisualElement> swarmDots = new List<VisualElement>();
        readonly List<VisualElement> swarmSlots = new List<VisualElement>();

        /// <summary>상대 기둥의 「은하의 응징 n」(없으면 0) · 그 쪽(0 내 쪽 · 1 상대 쪽)의 교체 불가 이유 글(없으면 null) · 무리 진행(끝난 수 / 명단 수 — 무리가 아니면 0) — 확인용.</summary>
        public int RetributionShown { get; private set; }
        public string LockShown(int side) => side >= 0 && side < 2 ? lockText[side] : null;
        public int SwarmDone { get; private set; }
        public int SwarmCount { get; private set; }
        /// <summary>우두머리 칩이 떠 있나 · 지금 DS에 찍힌 오라(상대 · 내 쪽) — 확인용.</summary>
        public bool AlphaChipShown { get; private set; }
        public AuraKind EnemyAura { get; private set; }
        public AuraKind PlayerAura { get; private set; }
        /// <summary>폼 바꾸기(78·79행)가 지금 그려지고 있나 — 확인용.</summary>
        public bool FormShown { get; private set; }

        /// <summary>응징 구슬이 보이는 만큼(0 ~ 1)과 그 자리(화면 좌표) — 확인용.</summary>
        public float OrbShown => orb != null ? orb.style.opacity.value : 0f;
        public Vector2 OrbAt { get; private set; }

        readonly string[] lockText = new string[2];

        // 드러난 이벤트로 센 것: 응징 저장 수 · 쪽마다 마지막 잠금(그 뒤 그 쪽이 새로 나왔으면 풀린 것으로 본다)
        void ReadRows(BattleFeed feed, out int stored, SwitchLocked[] locked)
        {
            stored = 0;
            locked[0] = locked[1] = null;
            foreach (var e in feed.Events)
            {
                if (hidden.Contains(e)) continue;
                switch (e)
                {
                    case RetributionStored rs: stored = Math.Max(0, rs.After); break;
                    case RetributionMoved _: stored = 0; break;
                    case SwitchLocked sl: if (sl.Side >= 0 && sl.Side < 2) locked[sl.Side] = sl; break;
                    case MoveUsed _: locked[0] = locked[1] = null; break;   // 잠금은 그 교체 창 하나의 일이다 — 다음 공격이 시작되면 칩을 뗀다
                }
            }
        }

        readonly SwitchLocked[] lockedNow = new SwitchLocked[2];

        void FillExtras(BattleFeed feed, BattleMon me, BattleMon foe)
        {
            var inv = CultureInfo.InvariantCulture;
            retribution = null;
            locks[0] = locks[1] = null;
            lockText[0] = lockText[1] = null;
            swarmDots.Clear();
            swarmSlots.Clear();
            RetributionShown = SwarmDone = SwarmCount = 0;
            AlphaChipShown = false;
            foreach (var hit in dsHits) hit.RemoveFromHierarchy();
            dsHits.Clear();
            var state = feed.Source;
            if (info == null || state == null) return;
            ReadRows(feed, out int stored, lockedNow);
            for (int k = 0; k < 2; k++)
            {
                bool mine = k == 0;
                var block = info.Q(mine ? "battle-info-me" : "battle-info-foe");
                var mon = mine ? me : foe;
                if (block == null) continue;
                int side = mine ? feed.MySide : 1 - feed.MySide;
                var line = new VisualElement { name = mine ? "battle-me-extra" : "battle-foe-extra" };
                line.AddToClassList("sn-battle-line");
                line.AddToClassList("sn-battle-extra");
                // 둘째 상태 이상(상태 둘 — rulings G20)
                if (mon.Exists && mon.Id < state.Pokemon.Count)
                {
                    var second = state.Pokemon[mon.Id].Status2;
                    if (second != StatusEffect.None) TypeIcon(line, DsStage.StatusKey(second), stage.TOrNull("screen.status." + second.ToString().ToLowerInvariant()));
                }
                // 우두머리(77행): 체력 +4
                if (!mine && mon.Exists && mon.Id < state.Pokemon.Count && state.Pokemon[mon.Id].Alpha)
                {
                    var chip = Add(line, Kit.Badge(stage.T("screen.battle.alpha.chip").Replace("{n}", SpotResultBeats.HpBonus.ToString(inv)), KitBadgeKind.Red, Kit.F2), "battle-foe-alpha", null);
                    stage.Tips.Hook(chip, stage.T("item.alpha_pokemon"), stage.T("screen.battle.alpha.tip"), TipSide.Below, reading: true);
                    AlphaChipShown = true;
                }
                // 천상의 오라(78행 — 신 배틀): 걸려 있으면 포획 7 · 둘째 폼, 천상의 유물로 막았으면 「오라 무효」
                if (!mine && feed.Kind == BattleKind.God && state.Battle != null)
                {
                    bool aura = LegendRules.AuraOn(state, flow.Ctx, state.Battle);
                    var chip = Add(line, Kit.Badge(stage.T(aura ? "screen.battle.aura.chip" : "screen.battle.aura.off"), aura ? KitBadgeKind.Gold : KitBadgeKind.Silver, Kit.F2), "battle-foe-aura", null);
                    stage.Tips.Hook(chip, stage.T("screen.battle.aura"), stage.T(aura ? "screen.battle.aura.tip" : "screen.battle.aura.off.tip"), TipSide.Below, reading: true);
                }
                // 은하의 응징(74행): 저장된 수
                if (!mine && feed.Kind == BattleKind.Galactic && stored > 0)
                {
                    retribution = Add(line, Kit.Badge(stage.T("screen.battle.retribution.chip").Replace("{n}", stored.ToString(inv)), KitBadgeKind.Red, Kit.F2), "battle-foe-retribution", null);
                    FxUi.OriginLeft(retribution);   // 톡 커질 때 기둥 밖(왼쪽)으로 나가지 않게
                    stage.Tips.Hook(retribution, stage.T("screen.battle.retribution"), stage.T("screen.battle.retribution.tip"), TipSide.Below, reading: true);
                    RetributionShown = stored;
                }
                // 교체 금지(75행): 자물쇠 칩 + 이유(마우스 오버)
                if (lockedNow[side] != null)
                {
                    lockText[k] = stage.T(RetributionBeats.ReasonKey(lockedNow[side].Reason));
                    locks[k] = Add(line, Kit.Badge(stage.T("screen.battle.lock.chip"), KitBadgeKind.Silver, Kit.F2), mine ? "battle-me-lock" : "battle-foe-lock", null);
                    FxUi.OriginLeft(locks[k]);
                    stage.Tips.Hook(locks[k], stage.T("screen.battle.lock.chip"), lockText[k], TipSide.Below, reading: true);
                }
                if (!mine) FillUb(feed, line, mon, state);   // 울트라 버스트: 페이즈 · 포획 불가 · 프리즘 아머 · 시련 · 면역 · 온슬롯(BattleScreen.Ub.cs)
                if (line.childCount > 0) block.Add(line);
            }
            // DS 체력 상자 옆의 작은 그림(결정 80 L16 나 — DsMon.Retribution·Locked)에 마우스를 올리면 기둥의 칩과 같은 설명
            if (dsBox != null)
            {
                if (RetributionShown > 0) DsHit("battle-ds-retribution", DsStage.RetributionRect(RetributionShown), stage.T("screen.battle.retribution"), stage.T("screen.battle.retribution.tip"));
                if (lockText[1] != null) DsHit("battle-ds-foe-lock", DsStage.LockRect(true), stage.T("screen.battle.lock.chip"), lockText[1]);
                if (lockText[0] != null) DsHit("battle-ds-me-lock", DsStage.LockRect(false), stage.T("screen.battle.lock.chip"), lockText[0]);
            }
            FillSwarm(feed, state);
        }

        readonly List<VisualElement> dsHits = new List<VisualElement>();

        // DS 그림 위의 보이지 않는 칸(자리 = DS px → 화면의 비율)
        void DsHit(string name, RectInt at, string title, string text)
        {
            var hit = Add(dsBox, new VisualElement(), name, "sn-battle-ds-hit");
            hit.style.left = Length.Percent(at.x * 100f / DsStage.Width);
            hit.style.top = Length.Percent(at.y * 100f / DsStage.Height);
            hit.style.width = Length.Percent(at.width * 100f / DsStage.Width);
            hit.style.height = Length.Percent(at.height * 100f / DsStage.Height);
            stage.Tips.Hook(hit, title, text, TipSide.Below, reading: true);
            dsHits.Add(hit);
        }

        // 무리 배틀(S55 — 24·81행): 「남은 포켓몬」 줄 자리에 진행 점 ●○○ + 명단 세 칸(등급 · 이름 · 끝난 것은 흐리게)
        void FillSwarm(BattleFeed feed, GameState state)
        {
            if (feed.Kind != BattleKind.Swarm || state.Battle == null) return;
            var block = info.Q("battle-info-foe");
            if (block == null) return;
            var left = block.Q("battle-foe-left");
            if (left != null) left.RemoveFromHierarchy();
            var roster = state.Battle.Sides[1 - feed.MySide].Roster;
            var head = Add(block, new VisualElement(), "battle-swarm", "sn-battle-line");
            Add(head, Kit.Text(stage.T("screen.battle.swarm"), Kit.F2), null, "kit-muted");
            var slots = Add(block, new VisualElement(), "battle-swarm-slots", "sn-battle-swarm");
            info.AddToClassList("sn-battle-swarm-info");   // 영어 1080 벌: 등급 + 이름 칸이 세 줄이라 기둥의 줄 사이를 좁힌다(USS sn-lang-en, M9 lang3)
            SwarmCount = roster.Count;
            for (int i = 0; i < roster.Count; i++)
            {
                int id = roster[i];
                bool caught = false;
                foreach (var e in feed.Events)
                    if (e is CaptureAttempt c && c.Success && c.TargetId == id && !hidden.Contains(e)) caught = true;
                var mon = Shown(id);
                bool done = caught || (mon.Exists && mon.Fainted) || (!mon.Exists && id != seen.Active[1 - feed.MySide] && feed.Ended);
                if (done) SwarmDone++;
                var dot = Add(head, new VisualElement(), "battle-swarm-dot-" + (i + 1).ToString(CultureInfo.InvariantCulture), "sn-battle-swarm-dot");
                if (done) dot.AddToClassList("sn-battle-swarm-dot-on");
                swarmDots.Add(dot);
                int def = id >= 0 && id < state.Pokemon.Count ? state.Pokemon[id].Def : -1;
                var slot = Add(slots, new VisualElement(), "battle-swarm-slot-" + (i + 1).ToString(CultureInfo.InvariantCulture), "sn-battle-swarm-slot");
                if (def >= 0)
                {
                    var grade = MapActions.GradeOf(flow.Ctx, def);
                    slot.Add(Kit.Badge(stage.T("screen.encounter.grade." + grade.ToString().ToLowerInvariant()), id == seen.Active[1 - feed.MySide] && !done ? KitBadgeKind.Gold : KitBadgeKind.Plain, Kit.F2));
                    var name = Add(slot, Kit.Text(BoardHud.ShortName(stage.T("pokemon." + flow.Ctx.Cards.Pokemon[def].Key)), Kit.F2), null, "sn-battle-swarm-name");
                    if (done) name.AddToClassList("kit-muted");
                    if (caught) slot.Add(Kit.Badge(stage.T("screen.battle.swarm.caught"), KitBadgeKind.Ok, Kit.F2));
                }
                else slot.Add(Kit.Text(stage.T(caught ? "screen.battle.swarm.caught" : "screen.battle.swarm.done"), Kit.F2));
                swarmSlots.Add(slot);
            }
        }

        // 덧칩의 움직임(74·75·81행 · 24행): 응징 구슬·배지 · 자물쇠 톡 · 진행 점 톡 · 명단이 하나씩 선다
        void AnimateRows()
        {
            AnimateUb();
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            var cur = seq != null ? seq.Current : null;
            // 응징 저장(741) · 옮기기(742)
            float orbShow = 0f, orbFly = 0f, burst = 0f, pop = 1f;
            bool toBadge = true;
            var stored = ResultRows.Playing(seq, ResultRows.Stored);
            var moved = ResultRows.Playing(seq, ResultRows.Moved);
            if (stored != null) RetributionBeats.Stored(local - stored.StartMs, stored.Ms, out orbFly, out orbShow, out pop);
            else if (moved != null)
            {
                RetributionBeats.Moved(local - moved.StartMs, moved.Ms, out orbFly, out orbShow, out burst);
                toBadge = false;
            }
            if (retribution != null) FxUi.Scale(retribution, pop, pop);
            if (orb != null)
            {
                var box = dsBox != null ? dsBox.worldBound : default(Rect);
                var chip = retribution != null ? retribution.worldBound : default(Rect);
                bool laid = !float.IsNaN(box.width) && box.width > 0f && orb.parent != null;
                if (laid && (orbShow > 0f || burst > 0f))
                {
                    // DS 화면 안의 두 자리(상대 상자 · 내 포켓몬)와 기둥의 배지 사이를 난다
                    var feed = flow.Battle;
                    var badge = !float.IsNaN(chip.width) && chip.width > 0f ? chip.center : new Vector2(box.xMin - 40f, box.yMin + 40f);
                    var faint = new Vector2(box.xMin + box.width * 0.75f, box.yMin + box.height * 0.3f);
                    var target = new Vector2(box.xMin + box.width * 0.25f, box.yMin + box.height * 0.6f);
                    var from = toBadge ? faint : badge;
                    var to = toBadge ? badge : target;
                    var at = orb.parent.WorldToLocal(Vector2.Lerp(from, to, orbFly));
                    FxUi.Move(orb, at.x, at.y);
                    OrbAt = at;
                    float s = burst > 0f ? 1f + 2f * (1f - burst) : 1f;
                    FxUi.Scale(orb, s, s);
                    FxUi.Fade(orb, burst > 0f ? burst : orbShow);
                }
                else FxUi.Fade(orb, 0f);
            }
            // 교체 금지(750)
            var locking = ResultRows.Playing(seq, ResultRows.Lock);
            var lockedEvent = ResultRows.EventOf<SwitchLocked>(locking);
            for (int k = 0; k < 2; k++)
            {
                if (locks[k] == null) continue;
                int side = k == 0 ? flow.Battle.MySide : 1 - flow.Battle.MySide;
                float s = lockedEvent != null && lockedEvent.Side == side ? RetributionBeats.Lock(local - locking.StartMs, locking.Ms) : 1f;
                FxUi.Scale(locks[k], s, s);
            }
            // 무리: 인트로 동안 명단이 약한 것부터 하나씩 서고(24행), 끝난 점은 그 기절·포획 단계 뒤에 톡 커진다(81행)
            if (swarmSlots.Count > 0)
            {
                var intro = cur != null ? cur.Step(FxRows.Intro) : null;
                bool opening = intro != null && cur.Step(EnterRows.Wipe) != null && local < intro.EndMs;
                int shown = opening ? (local < intro.StartMs ? 0 : SwarmBeats.LineAt(local - intro.StartMs, swarmSlots.Count)) : swarmSlots.Count;
                for (int i = 0; i < swarmSlots.Count; i++)
                {
                    FxUi.Fade(swarmSlots[i], i < shown ? 1f : 0f);
                    float s = opening && i < shown ? SwarmBeats.LinePop(local - intro.StartMs, i) : 1f;
                    FxUi.Scale(swarmSlots[i], s, s);
                }
                var down = cur != null ? cur.Step(650) ?? cur.Step(CaptureStage.Row) : null;
                for (int i = 0; i < swarmDots.Count; i++)
                {
                    float s = i == SwarmDone - 1 && down != null && local >= down.EndMs ? SwarmBeats.DotPop(local - down.EndMs) : 1f;
                    FxUi.Scale(swarmDots[i], s, s);
                }
            }
        }

        // ── DS 층: 오라(76·77행 · 팬 폼) · 폼 바꾸기(78·79행) ──

        FanForms fans;

        /// <summary>타입 색(알파 스트라이크의 오라 — Pt 타입 그림의 바탕색에 맞춘 값 [가정]).</summary>
        public static Color32 TypeColor(PokemonType type)
        {
            switch (type)
            {
                case PokemonType.Fire: return new Color32(0xF0, 0x80, 0x30, 0xFF);
                case PokemonType.Water: return new Color32(0x68, 0x90, 0xF0, 0xFF);
                case PokemonType.Electric: return new Color32(0xF8, 0xD0, 0x30, 0xFF);
                case PokemonType.Grass: return new Color32(0x78, 0xC8, 0x50, 0xFF);
                case PokemonType.Ice: return new Color32(0x98, 0xD8, 0xD8, 0xFF);
                case PokemonType.Fighting: return new Color32(0xC0, 0x30, 0x28, 0xFF);
                case PokemonType.Poison: return new Color32(0xA0, 0x40, 0xA0, 0xFF);
                case PokemonType.Ground: return new Color32(0xE0, 0xC0, 0x68, 0xFF);
                case PokemonType.Flying: return new Color32(0xA8, 0x90, 0xF0, 0xFF);
                case PokemonType.Psychic: return new Color32(0xF8, 0x58, 0x88, 0xFF);
                case PokemonType.Bug: return new Color32(0xA8, 0xB8, 0x20, 0xFF);
                case PokemonType.Rock: return new Color32(0xB8, 0xA0, 0x38, 0xFF);
                case PokemonType.Ghost: return new Color32(0x70, 0x58, 0x98, 0xFF);
                case PokemonType.Dragon: return new Color32(0x70, 0x38, 0xF8, 0xFF);
                case PokemonType.Dark: return new Color32(0x70, 0x58, 0x48, 0xFF);
                case PokemonType.Steel: return new Color32(0xB8, 0xB8, 0xD0, 0xFF);
                case PokemonType.Fairy: return new Color32(0xEE, 0x99, 0xAC, 0xFF);
                default: return new Color32(0xA8, 0xA8, 0x78, 0xFF);
            }
        }

        static readonly Color32 RedAura = new Color32(0xE0, 0x30, 0x28, 0xFF), WhiteAura = new Color32(0xFF, 0xFF, 0xFF, 0xFF), GodAura = new Color32(0xFF, 0xE8, 0x90, 0xFF);

        // 장면의 포켓몬 둘에 오라·폼 바꾸기를 입힌다(값은 지금 항목과 그 안 시각 · 드러난 이벤트에서만)
        void PaintRows(BattleFeed feed, DsScene scene, BattleMon me, BattleMon foe)
        {
            if (fans == null) fans = FanForms.Load();
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            var cur = seq != null ? seq.Current : null;
            var state = feed.Source;
            EnemyAura = PlayerAura = AuraKind.None;
            FormShown = false;
            for (int k = 0; k < 2; k++)
            {
                bool enemy = k == 0;
                var mon = enemy ? foe : me;
                var ds = enemy ? scene.Enemy : scene.Player;
                if (!mon.Exists) continue;
                // 폼 바꾸기(790) · 신 둘째 폼(780): 그 단계 동안 옛·새 모습과 흰 빛 · 가라앉음 · 빛기둥
                FxStep formStep = null;
                FormChanged changed = null;
                if (cur != null)
                    foreach (var s in cur.Steps)
                    {
                        if (s.Row != ResultRows.Form && s.Row != ResultRows.GodPhase) continue;
                        var fc = FormBeats.Changed(s);
                        if (fc != null && fc.PokemonId == mon.Id && local >= s.StartMs && local < s.EndMs) { formStep = s; changed = fc; }
                    }
                if (formStep == null && FormBeats.SlumpAhead(cur, local, mon.Id))
                {
                    // 신의 첫 폼(P29): 앞의 기절 단계에서는 가라앉지 않고 선 채 — 쓰러지듯 미끄러지는 것은 둘째 폼 단계(780)의 처음이 그린다
                    if (ds == null)
                    {
                        ds = ToDs(mon, enemy ? 1 - feed.MySide : feed.MySide, enemy);
                        if (enemy) scene.Enemy = ds; else scene.Player = ds;
                    }
                    if (ds != null) { ds.Standing = true; ds.Sink = 0f; }
                }
                if (formStep != null)
                {
                    bool god = formStep.Row == ResultRows.GodPhase;
                    var pose = FormBeats.At(local - formStep.StartMs, formStep.Ms, god);
                    if (ds == null)
                    {
                        // 쓰러져 무대에서 빠진 첫 폼(신): 이 단계 동안 다시 세운다
                        ds = ToDs(mon, enemy ? 1 - feed.MySide : feed.MySide, enemy);
                        if (enemy) scene.Enemy = ds; else scene.Player = ds;
                    }
                    if (ds != null)
                    {
                        int def = pose.After ? changed.After : changed.Before;
                        ds.Key = flow.Ctx.Cards.Pokemon[def].Key;
                        ds.Name = LineT("pokemon." + ds.Key);
                        ds.Standing = true;
                        ds.White = Mathf.Max(ds.White, pose.White);
                        ds.Sink = god ? pose.Sink : Mathf.Max(ds.Sink, pose.Sink);   // 신의 첫 폼은 기절로 가라앉아 있다 — 이 단계가 다시 솟게 한다
                        if (god) { ds.Dx = ds.Dy = 0; ds.Scale = 1f; }
                        if (god)
                        {
                            ds.Bar = pose.Fill * ds.MaxHp;
                            if (pose.Pillar > 0f)
                            {
                                var foot = enemy ? DsStage.EnemyFoot : DsStage.PlayerFoot;
                                if (scene.Rects == null) scene.Rects = new List<DsRect>();
                                scene.Rects.Add(new DsRect { X = foot.x - 28, Y = 0, W = 56, H = foot.y + 4, Color = new Color32(0xFF, 0xF8, 0xD0, (byte)Mathf.RoundToInt(pose.Pillar * 200f)) });
                                scene.Rects.Add(new DsRect { X = foot.x - 14, Y = 0, W = 28, H = foot.y + 4, Color = new Color32(0xFF, 0xFF, 0xFF, (byte)Mathf.RoundToInt(pose.Pillar * 230f)) });
                            }
                        }
                        FormShown = true;
                    }
                }
                if (ds == null) continue;
                // 오라: 알파 스트라이크(59행 안) > 비스트 부스트(641) > 폼 빛 > 우두머리 > 팬 폼
                var aura = default(AuraPose);
                var used = cur != null ? cur.Step(590) : null;
                if (used != null && local >= used.StartMs)
                    foreach (var e in used.Events)
                        if (e is MoveUsed mu && mu.MoveKey == BattleRules.AlphaStrike && mu.PokemonId == mon.Id) aura = AuraBeats.Strike(local - used.StartMs, (int)mu.Type);
                if (!aura.On)
                {
                    var tired = cur != null ? cur.Step(641) : null;
                    if (tired != null && local >= tired.StartMs && local < tired.EndMs)
                        foreach (var e in tired.Events)
                            if (e is FatigueChanged f && f.Cause == FatigueCause.BeastBoost && f.PokemonId == mon.Id) aura = AuraBeats.Boost(local - tired.StartMs, tired.Ms);
                }
                if (!aura.On && formStep != null && formStep.Row == ResultRows.Form) aura = new AuraPose { Kind = AuraKind.Form, Px = ds.White > 0.5f ? 2 : 0, Type = -1 };
                if (!aura.On && state != null && mon.Id < state.Pokemon.Count && state.Pokemon[mon.Id].Alpha && ds.Standing)
                {
                    // 우두머리(77행): 인트로에서 선 뒤 처음 1초는 굵게, 그 뒤 얇게 남는다
                    var intro = cur != null ? cur.Step(FxRows.Intro) : null;
                    int since = intro != null && local < intro.EndMs ? local - intro.StartMs - EnterBeats.IntroLeadMs - EnterBeats.IntroSlideMs : int.MaxValue / 2;
                    aura = AuraBeats.Alpha(since);
                }
                // 천상의 오라가 걸린 신(78행 — 결정 80 L15 나): 옅은 금빛 오라가 남는다(폼이 바뀌는 단계 동안은 빛기둥이 대신한다)
                if (!aura.On && enemy && formStep == null && ds.Standing && feed.Kind == BattleKind.God && state != null && state.Battle != null && LegendRules.AuraOn(state, flow.Ctx, state.Battle)) aura = AuraBeats.God();
                Color32 color = aura.Kind == AuraKind.AlphaStrike ? TypeColor((PokemonType)aura.Type) : aura.Kind == AuraKind.Form ? WhiteAura : aura.Kind == AuraKind.God ? GodAura : RedAura;
                if (aura.On)
                {
                    ds.Aura = color;
                    ds.AuraPx = aura.Px;
                    if (aura.Tint > 0f) ds.Tint = new Color32(color.r, color.g, color.b, (byte)Mathf.RoundToInt(Mathf.Clamp01(aura.Tint) * 255f));
                }
                else if (fans.TryGet(ds.Key, out var fanAura, out int fanPx, out var fanTint))
                {
                    ds.Aura = fanAura;
                    ds.AuraPx = fanPx;
                    ds.Tint = fanTint;
                    aura.Kind = AuraKind.Fan;
                }
                if (enemy) EnemyAura = aura.Kind; else PlayerAura = aura.Kind;
            }
            // 응징 · 교체 불가(결정 80 L16 나): 기둥의 칩과 같은 값(드러난 이벤트에서 — FillExtras)을 체력 상자 옆의 작은 그림으로도
            if (scene.Enemy != null)
            {
                scene.Enemy.Retribution = RetributionShown;
                scene.Enemy.Locked = lockText[1] != null;
            }
            if (scene.Player != null) scene.Player.Locked = lockText[0] != null;
            PaintUb(scene);
        }

        // ── 트레이너 배틀 승리 보상(46행 — Reward/TrainerReward: 상대 유물 하나 또는 운명 최대 2장) ──

        void FillSpoils(VisualElement box, PendingChoice choice, List<ChoiceAnswer> legal, bool ready)
        {
            var inv = CultureInfo.InvariantCulture;
            var ctx = flow.Ctx;
            string loser = choice.Subject >= 0 && choice.Subject < flow.Client.View.Seats.Length ? flow.SeatName(choice.Subject) : "";
            Add(box, Kit.Wrapped(KoJosa.Fix(stage.T("screen.battle.reward.spoils").Replace("{name}", loser)), Kit.F2), "battle-reward-ask", null);
            var row = Add(box, new VisualElement(), null, "sn-battle-reward-row");
            row.AddToClassList("sn-battle-spoils");
            for (int i = 0; i < legal.Count; i++)
            {
                int index = i;
                var a = legal[i];
                Button button;
                if (a.Kind == AnswerKind.Pick && a.Target >= 0 && a.Target < ctx.Items.Items.Count)
                {
                    string key = ctx.Items.Items[a.Target].Key;
                    button = Add(row, Kit.Button("", KitButtonKind.Normal, () => PressReward(index)), "battle-reward-" + (i + 1).ToString(inv), "sn-battle-spoil");
                    var tex = GenAssets.Load<Texture2D>(GenAssets.SpotTokenKey(key));
                    if (tex != null)
                    {
                        var icon = Add(button, new VisualElement(), null, "sn-battle-spoil-icon");
                        icon.style.backgroundImage = new StyleBackground(tex);
                    }
                    Add(button, Kit.Text(stage.T(key), Kit.F2), null, "sn-battle-spoil-name");
                    if (ready) stage.Tips.Hook(button, stage.T(key), stage.TOrNull(key + ".text"), TipSide.Above, reading: true);
                }
                else
                {
                    button = Add(row, Kit.Button("", KitButtonKind.Normal, () => PressReward(index)), "battle-reward-" + (i + 1).ToString(inv), "sn-battle-spoil");
                    var tex = FateCards.Back();
                    if (tex != null)
                    {
                        var icon = Add(button, new VisualElement(), null, "sn-battle-spoil-icon");
                        icon.style.backgroundImage = new StyleBackground(tex);
                    }
                    Add(button, Kit.Text(stage.T("screen.battle.reward.spoils.fate").Replace("{n}", Math.Max(0, a.Target).ToString(inv)), Kit.F2), null, "sn-battle-spoil-name");
                }
                button.SetEnabled(ready);
            }
        }

        // 결과 창의 승리 보상 줄(「○○을(를) 가져왔다!」 · 「운명의 변화 2장을 받았다」) — 그 항목(461)이 재생 중이면 그때 내려앉는다
        void SpoilsLine(VisualElement lines, BattleFeed feed)
        {
            if (feed.Spoils == null) return;
            var inv = CultureInfo.InvariantCulture;
            int relic = SpoilsBeats.Relic(feed.Spoils, out int fate);
            string text = relic >= 0 && relic < flow.Ctx.Items.Items.Count
                ? KoJosa.Fix(stage.T("screen.battle.result.spoils.relic").Replace("{item}", stage.T(flow.Ctx.Items.Items[relic].Key)))
                : stage.T("screen.battle.result.spoils.fate").Replace("{n}", fate.ToString(inv));
            var line = Add(lines, Kit.Wrapped(text, Kit.F2), "battle-result-spoils", null);
            line.pickingMode = PickingMode.Ignore;
            var cur = flow.Fx != null ? flow.Fx.Sequencer.Current : null;
            var step = cur != null ? cur.Step(SpoilsBeats.Row) : null;
            spoilsLine = line;
            spoilsAt = step != null ? step.StartMs + SpoilsBeats.DropAtMs : -1;
        }

        VisualElement spoilsLine;
        int spoilsAt = -1;

        void AnimateSpoils()
        {
            if (spoilsLine == null || spoilsLine.parent == null) return;
            var cur = flow.Fx != null ? flow.Fx.Sequencer.Current : null;
            bool playing = cur != null && cur.Step(SpoilsBeats.Row) != null && spoilsAt >= 0;
            float u = playing ? Tween.Apply(Ease.OutQuad, Tween.Progress(local, spoilsAt, EnterBeats.ResultDropMs)) : 1f;
            FxUi.Fade(spoilsLine, u);
            FxUi.Move(spoilsLine, 0f, -EnterBeats.ResultDropPx * (1f - u));
        }
    }
}
