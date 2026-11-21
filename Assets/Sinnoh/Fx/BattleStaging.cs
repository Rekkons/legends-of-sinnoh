using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 배틀 연출의 화면 상수(ms — 04 §8-8 53~67행의 단계 안 박자, spikes/dice-roll §4). 단계 길이는 방장과 같은 <c>PlayTiming</c>이 세고,
    /// 여기 값은 그 단계 안에서 화면이 나누는 박자라 방장 멈춤에 들지 않는다 [가정 — 목업이 정하지 않은 값은 캡처로 맞췄다].
    /// </summary>
    public static class BattleBeats
    {
        /// <summary>기술 줄 ↔ 굴림판(04 §4-4 「150~350」): 굴림 단계가 시작하고 이만큼 뒤에 바뀌기 시작해 <see cref="DockSwapMs"/> 동안 들어온다.</summary>
        public const int DockSwapStartMs = 150, DockSwapMs = 200;
        /// <summary>세기(56행): 짚는 고리와 결과 줄 아이콘이 튀는 시간.</summary>
        public const int RingMs = 100, CountPopMs = 80;
        /// <summary>
        /// 기술 사용(59행): 당김 → 돌진 → 제자리. 세 토막의 길이와 px. 결정 143 ⑦부터 당김은 준비 동작(<see cref="PrepBeats"/> — 굴림과 함께)이 하고,
        /// 59행 단계는 맞는 순간인 돌진 → 제자리만이다(<see cref="LungeHitMs"/> + <see cref="LungeHomeMs"/> = <c>PlayTiming.MoveHitMs</c>). <see cref="LungeBackMs"/>는 옛 박자 기록으로 둔다.
        /// </summary>
        public const int LungeBackMs = 150, LungeHitMs = 150, LungeHomeMs = 150, LungeBackPx = 3, LungeHitPx = 6;
        /// <summary>준비 동작(결정 143 ⑦): 굴림이 시작하고 이만큼 동안 당기고, 그 뒤 <see cref="ChargeEveryMs"/>마다 <see cref="ChargePx"/>px 떨며 기를 모은다.</summary>
        public const int PrepInMs = 150, ChargeEveryMs = 100, ChargePx = 1;
        /// <summary>상성(60행): 맞은 도트가 깜빡이는 시간·한 번의 길이, 효과가 굉장하면 흔들리는 시간·px, 그 뒤에 문장이 뜬다.</summary>
        public const int BlinkMs = 600, BlinkEveryMs = 80, ShakeMs = 360, ShakeEveryMs = 60, ShakePx = 2, MatchupLineMs = 900;
        /// <summary>기절(65행): 도트가 내려가는 시간(그 뒤에 「쓰러졌다!」).</summary>
        public const int FaintSinkMs = 600;
        /// <summary>도망(67행): 도트가 옆으로 빠지는 시간과 거리(DS px).</summary>
        public const int FledMs = 600, FledPx = 96;
        /// <summary>선턴(54행): 먼저 움직이는 쪽 체력 상자가 흔들리는 시간·한 번의 길이.</summary>
        public const int OrderBobMs = 600, OrderBobEveryMs = 150;
        /// <summary>차례인 쪽 체력 상자의 흔들림(55행 — 늘 도는 장식): 한 번의 길이.</summary>
        public const int TurnBobEveryMs = 320;
    }

    /// <summary>
    /// 배틀 화면이 그리는 행(04 §8-8, §8-9 「배틀 연출」 표): 선턴 54 · 굴림 56 · 면 바꾸기 57 · 기술 59 · 상성 60 · 계산 줄 61 · 체력 61 · 상태 이상 62·63 · 전투 피로 64 · 기절 65 · 교체 66 · 도망 67.
    /// 들어가고 나오는 것(§8-9 「들어가고 나오는 연출」): 인트로 53 · 포획 68 · 잡은 뒤 69(결말 대사가 머무는 박자) · 결과 70 · 막 목표 94(결과 창의 줄).
    /// AI 자막 73(사유가 있는 답만 — <see cref="AiLineBeats"/>). 귀환 71은 보드가 그린다(<see cref="WhiteoutBeats"/>).
    /// 아직 그리지 않는 배틀 행(응징 74 · 교체 금지 75 — M5)은 화면 대기열에서 덜어 낸다 — 그릴 것이 없는데 입력만 막히지 않게.
    /// </summary>
    public static class BattleRows
    {
        public static bool Built(int row)
        {
            switch (row)
            {
                case 540:
                case 560:
                case 570:
                case 590:
                case 600:
                case 611:
                case 612:
                case 620:
                case 630:
                case 641:
                case 642:
                case 650:
                case FxRows.SendIn:
                case 670:
                case FxRows.Intro:
                case CaptureStage.Row:
                case 690:
                case 700:
                case 940:
                case AiLineBeats.Row:
                    return true;
                default:
                    return ResultRows.Battle(row)    // M5 「맵 결과·배틀 안 연출」: 승리 보상 46 · 응징 74 · 교체 금지 75 · 신 둘째 폼 78 · 폼 바꾸기 79
                        || UbBattleRows.Battle(row); // M6 「UB 배틀 화면 묶음」: 프리즘 아머 88 · 울트라 온슬롯 89 · 호수 시련 92
            }
        }

        /// <summary>
        /// 그 단계에 그릴 것이 있나(배틀 행이 아니면 늘 true): 교체(66행)는 불러들이거나 내보내는 포켓몬이 있을 때만(쓰러진 포켓몬·배틀 끝의 정리는 그릴 것이 없다),
        /// 도망·기권(67행)은 야생이 도망칠 때만(기권의 모습은 아직 없다), AI 자막(73행)은 사유가 있는 답만.
        /// </summary>
        public static bool Draws(FxStep step)
        {
            if (step == null) return false;
            if (step.Row == FxRows.SendIn)
            {
                foreach (var e in step.Events)
                    if (e is SentIn || (e is SwitchedOut so && (so.Reason == SwitchReason.Switch || so.Reason == SwitchReason.Forced))) return true;
                return false;
            }
            if (step.Row == FxRows.Intro)
            {
                foreach (var e in step.Events) if (e is SentIn) return true;
                return false;
            }
            if (step.Row == 670)
            {
                foreach (var e in step.Events) if (e is Fled) return true;
                return false;
            }
            if (step.Row == AiLineBeats.Row) return AiLineBeats.Answer(step) != null;
            return true;
        }
    }

    /// <summary>
    /// AI 자막(04 §8-8 73행): 갤럭시단 같은 가상 좌석의 AI가 고른 답의 사유를 DS 위 가장자리 띠로 보인다(04는 「메시지 창 위」 — 내 체력 상자를 가려 옮겼다 [가정]) — 왼쪽에서 미끄러져 들어와 머물다 걷힌 뒤 기술 연출이 이어진다.
    /// 길이는 방장과 같은 값(<c>PlayTiming.RowMs</c> 730행 — 1000). 사유가 없는 답(출전 · 응징 옮기기 — 원칙 AI가 사유를 싣지 않는다)은 그릴 것이 없어 덜어 낸다(<see cref="BattleRows.Draws"/>).
    /// 생각 시간은 앞 연출 뒤에 숨는다(AI 답은 앞 항목이 다 재생된 뒤에 온다) [가정 — plan §5 #58].
    /// </summary>
    public static class AiLineBeats
    {
        public const int Row = 730;

        /// <summary>들어오는 시간 · 걷히는 시간(ms).</summary>
        public const int InMs = 150, OutMs = 150;

        /// <summary>그 단계가 실은 사유 있는 AI 답(없으면 null).</summary>
        public static ChoiceAnswered Answer(FxStep step)
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is ChoiceAnswered a && a.ByAi && a.Reason != null && a.Reason.Rule != AiRule.None) return a;
            return null;
        }

        /// <summary>그 항목의 AI 자막 단계(사유가 없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry)
        {
            var step = entry != null ? entry.Step(Row) : null;
            return step != null && Answer(step) != null ? step : null;
        }

        /// <summary>띠의 가로 자리(−1 = 왼쪽 밖 ~ 0 = 제자리)와 보이는 만큼(0 ~ 1) — 단계 안 시각.</summary>
        public static void At(int ms, int total, out float slide, out float alpha)
        {
            slide = -1f;
            alpha = 0f;
            if (total <= 0 || ms < 0 || ms >= total) return;
            slide = Tween.At(ms, 0, InMs, -1f, 0f, Ease.OutQuad);
            alpha = ms < total - OutMs ? 1f : Tween.At(ms, total - OutMs, OutMs, 1f, 0f, Ease.InQuad);
        }

        /// <summary>사유의 문자열 키(<c>screen.battle.ai.rule.*</c>).</summary>
        public static string RuleKey(AiRule rule)
        {
            switch (rule)
            {
                case AiRule.Signature: return "screen.battle.ai.rule.signature";
                case AiRule.BestValue: return "screen.battle.ai.rule.best_value";
                case AiRule.PlanB: return "screen.battle.ai.rule.plan_b";
                case AiRule.Lookahead: return "screen.battle.ai.rule.lookahead";   // 강한 AI(M8 「연결」) — 줄은 승산 꼴(screen.battle.ai.line.read)
                default: return null;
            }
        }
    }

    /// <summary>
    /// 이벤트가 화면에 드러나는 때(04 §8-3 — 「결과가 연출보다 먼저 보이지 않는다」): 항목 안에서 그 이벤트의 단계가 시작할 때.
    /// 단계가 없는 이벤트(행이 없거나 부품이 없어 덜어 낸 행)는 바로 앞 단계가 끝날 때, 앞 단계가 없으면 항목 시작이다(표시 값 단서의 착지 규칙과 같다).
    /// 단계 중간이 「맞음」인 것은 따로 늦춘다: 값이 바뀌는 것(체력·피로·주사위 합)은 단계 끝(= 단서가 닿는 때), 피해 계산은 총합 칸이 켜질 때,
    /// 상성 문장은 깜빡임 뒤, 기절·도망은 도트가 빠진 뒤, 교체로 나오는 포켓몬은 66행 단계의 절반.
    /// 기다리는 항목의 이벤트는 모두 아직 드러나지 않았다.
    /// </summary>
    public static class FxReveal
    {
        /// <summary>그 항목 안에서 이벤트가 드러나는 항목 시각(ms). 그 항목의 이벤트가 아니면 0.</summary>
        public static int At(FxEntry entry, GameEvent e)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (entry.Reveal == null) Build(entry);
            return e != null && entry.Reveal.TryGetValue(e, out int ms) ? ms : 0;
        }

        /// <summary>그 항목의 이벤트면 드러나는 항목 시각을 주고 true, 아니면 false.</summary>
        public static bool TryAt(FxEntry entry, GameEvent e, out int ms)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (entry.Reveal == null) Build(entry);
            ms = 0;
            return e != null && entry.Reveal.TryGetValue(e, out ms);
        }

        static void Build(FxEntry entry)
        {
            var of = new Dictionary<GameEvent, FxStep>();
            foreach (var s in entry.Steps)
                foreach (var e in s.Events)
                    if (!of.ContainsKey(e)) of.Add(e, s);
            var map = new Dictionary<GameEvent, int>();
            int after = 0;
            foreach (var e in entry.Events)
            {
                if (map.ContainsKey(e)) continue;
                if (of.TryGetValue(e, out var s))
                {
                    map.Add(e, Offset(entry, s, e));
                    after = s.EndMs;
                }
                else map.Add(e, after);
            }
            entry.Reveal = map;
        }

        static int Offset(FxEntry entry, FxStep s, GameEvent e)
        {
            switch (e)
            {
                case HpChanged _:
                case StatusTicked _:
                case FatigueChanged _:
                case DiceRolled _:
                    return s.EndMs;
                case DamageBreakdown _: return s.StartMs + Math.Min(s.Ms, PlayTiming.DamageCalcMs.Value);
                case TypeMatchup _: return s.StartMs + Math.Min(s.Ms, BattleBeats.MatchupLineMs);
                case Fainted _: return s.StartMs + Math.Min(s.Ms, BattleBeats.FaintSinkMs);
                case Fled _: return s.StartMs + Math.Min(s.Ms, BattleBeats.FledMs);
                case CaptureAttempt c: return s.StartMs + Math.Min(s.Ms, CaptureStage.VerdictMs(c));
                case InitiativeTokenAt _ when s.Row == FxRows.Intro: return s.StartMs + Math.Min(s.Ms, InitiativeBeats.PlaceAtMs);   // 인트로 끝에 놓일 때(결정 73 ⑦)
                case SentIn _ when s.Row == FxRows.Intro:
                {
                    // 인트로(53행): 한 단계에 둘이 나오면 뒤에 적힌 쪽(야생·상대)이 먼저, 앞에 적힌 쪽(도전자 = 나)이 그 뒤에 나온다 [가정]
                    bool later = false;
                    bool seen = false;
                    foreach (var x in s.Events)
                    {
                        if (ReferenceEquals(x, e)) seen = true;
                        else if (seen && x is SentIn) later = true;
                    }
                    // 주인공이 던지는 첫 출전이 혼자 나오는 단계(첫 출전을 따로 고른 항목)는 뒷모습이 들어와 던질 틈을 둔다(결정 99 ②)
                    int lead = later ? EnterBeats.IntroSecondMs : IntroStage.Thrown(entry, s, (SentIn)e) ? EnterBeats.IntroPickMs : EnterBeats.IntroLeadMs;
                    return s.StartMs + Math.Min(s.Ms, lead);
                }
                case SentIn _:
                    if (s.Row == FxRows.SendIn)
                        foreach (var x in s.Events)
                        {
                            if (ReferenceEquals(x, e)) break;
                            if (x is SwitchedOut) return s.StartMs + s.Ms / 2;
                        }
                    return s.StartMs;
                default: return s.StartMs;
            }
        }

        /// <summary>대기열에서 아직 드러나지 않은 이벤트를 <paramref name="into"/>에 모은다(비우고 채운다): 재생 중인 항목의 뒤쪽 + 기다리는 항목 전부.</summary>
        public static void Hidden(FxSequencer seq, HashSet<GameEvent> into)
        {
            if (seq == null) throw new ArgumentNullException(nameof(seq));
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            foreach (var entry in seq.Queued)
            {
                bool playing = ReferenceEquals(entry, seq.Current);
                foreach (var e in entry.Events)
                    if (!playing || At(entry, e) > seq.LocalMs) into.Add(e);
            }
        }
    }

    /// <summary>
    /// 지금 화면에 드러난 배틀의 모습(<see cref="BattleFeed.See"/>가 만든다): 양쪽에 나와 있는 포켓몬 · 무대에서 빠졌나 · 상태 이상 · 우선권 토큰 · 마지막 피해 계산과 그 체력 변화 · 끝났나 · 드러난 기록 줄 수.
    /// </summary>
    public sealed class BattleSeen
    {
        /// <summary>그 쪽에 나와 있는 포켓몬(없으면 −1).</summary>
        public readonly int[] Active = { -1, -1 };
        /// <summary>그 쪽 포켓몬이 무대에서 빠졌다(쓰러짐 · 불러들임 · 도망 · 잡힘) — 체력 상자만 남는다.</summary>
        public readonly bool[] Out = new bool[2];
        public int TokenSide = -1;
        /// <summary>
        /// 기술을 골라 준비 동작 중인 포켓몬(결정 143 ⑦ — 드러난 <see cref="MoveSelected"/>부터 그 공격의 <see cref="MoveUsed"/>가 드러날 때까지, 없으면 −1)과 그 고른 기술(첫 MoveSelected).
        /// 재굴림 창 · 반응 창이 열려 항목 사이에 멈춘 동안에도 당긴 자세 · 「○○의 △△!」가 남는다.
        /// </summary>
        public int Winding = -1;
        public MoveSelected WindMove;
        /// <summary>마지막 피해 계산과 그 피해로 바뀐 체력(아직 착지하지 않았으면 null), 마지막 포획 시도.</summary>
        public DamageBreakdown Damage;
        public HpChanged DamageHp;
        public CaptureAttempt Capture;
        public bool Ended;
        /// <summary>드러난 기록 줄 수.</summary>
        public int Lines;

        readonly Dictionary<int, StatusEffect> status = new Dictionary<int, StatusEffect>();
        readonly HashSet<int> pending = new HashSet<int>();

        /// <summary>이벤트 하나를 받는다(받은 순서대로) — shown = 화면에 드러났나(아직이면 「걸림」만 적어 둔다).</summary>
        public void Take(GameEvent e, bool shown)
        {
            if (!shown)
            {
                if (e is StatusApplied wait && wait.Result == StatusResult.Applied) pending.Add(wait.PokemonId);
                return;
            }
            switch (e)
            {
                case InitiativeTokenAt t:
                    TokenSide = t.Side;
                    break;
                case OrderDecided o:
                    if (o.TokenTo != GameEvent.U) TokenSide = o.TokenTo;
                    Unwind();
                    break;
                case MoveSelected ms:
                {
                    // 준비 동작(결정 143 ⑦): 그 공격의 첫 고른 기술(흉내내기 · 손가락흔들기가 부른 둘째 MoveSelected는 맞는 순간의 줄이 알린다)
                    int who = ms.Side >= 0 && ms.Side < 2 ? Active[ms.Side] : -1;
                    if (who >= 0 && (Winding != who || WindMove == null))
                    {
                        Winding = who;
                        WindMove = ms;
                    }
                    break;
                }
                case MoveUsed _:
                    Unwind();
                    break;
                case SentIn s:
                    if (s.Side >= 0 && s.Side < 2)
                    {
                        if (Winding >= 0 && Active[s.Side] == Winding && s.PokemonId != Winding) Unwind();
                        Active[s.Side] = s.PokemonId;
                        Out[s.Side] = false;
                    }
                    status[s.PokemonId] = Enum.IsDefined(typeof(StatusEffect), s.Status) ? s.Status : StatusEffect.None;
                    break;
                case SwitchedOut so:
                    // 쓰러진 포켓몬은 기절이, 배틀 끝의 정리는 결과 창이 맡는다 — 여기서는 불러들인 것만 무대에서 뺀다
                    if ((so.Reason == SwitchReason.Switch || so.Reason == SwitchReason.Forced) && so.Side >= 0 && so.Side < 2 && Active[so.Side] == so.PokemonId) Out[so.Side] = true;
                    break;
                case Fainted f:
                {
                    int side = SideOf(f.PokemonId, f.Side);
                    if (side >= 0) Out[side] = true;
                    if (f.PokemonId == Winding) Unwind();
                    break;
                }
                case FormChanged fc:
                    // 신의 둘째 폼(78행): 쓰러져 무대에서 빠졌던 첫 폼이 둘째 폼으로 다시 선다 — 그 단계가 끝난 뒤에도 서 있다(출전 이벤트가 따로 오지 않는다)
                    if (fc.Temporary && fc.Source.Kind == SourceKind.Rule && fc.Source.Id == (int)RuleSource.CelestialAura)
                    {
                        int side = SideOf(fc.PokemonId, GameEvent.U);
                        if (side >= 0) Out[side] = false;
                    }
                    break;
                case StatusApplied a:
                    if (a.Result == StatusResult.Applied) status[a.PokemonId] = a.Status;
                    break;
                case DamageBreakdown d:
                    Damage = d;
                    DamageHp = null;
                    Capture = null;
                    break;
                case HpChanged h:
                    if (Damage != null && DamageHp == null && h.PokemonId == Damage.TargetId) DamageHp = h;
                    break;
                case CaptureAttempt c:
                    Capture = c;
                    Unwind();
                    if (c.Success)
                    {
                        int side = SideOf(c.TargetId, GameEvent.U);
                        if (side >= 0) Out[side] = true;
                    }
                    break;
                case Fled fl:
                    if (fl.Side >= 0 && fl.Side < 2) Out[fl.Side] = true;
                    Unwind();
                    break;
                case BattleEnded _:
                    Ended = true;
                    Unwind();
                    break;
            }
        }

        void Unwind()
        {
            Winding = -1;
            WindMove = null;
        }

        int SideOf(int pokemon, int hint)
        {
            if (hint >= 0 && hint < 2 && Active[hint] == pokemon) return hint;
            for (int k = 0; k < 2; k++) if (Active[k] == pokemon) return k;
            return -1;
        }

        /// <summary>
        /// 그 포켓몬의 드러난 상태 이상: 아직 드러나지 않은 「걸림」이 있으면 그 전의 상태, 없으면 보기의 값(<paramref name="view"/>) 그대로.
        /// 풀리는 것은 이벤트가 없어 보기를 따른다.
        /// </summary>
        public StatusEffect Status(int pokemon, StatusEffect view) => pending.Contains(pokemon) ? (status.TryGetValue(pokemon, out var s) ? s : StatusEffect.None) : view;

        /// <summary>그 포켓몬의 드러난 둘째 상태 이상(둘은 한 기술이 함께 건다 — 「걸림」이 아직 드러나지 않았으면 없다).</summary>
        public StatusEffect Status2(int pokemon, StatusEffect view) => pending.Contains(pokemon) ? StatusEffect.None : view;
    }

    /// <summary>굴림판 머리의 세기(04 §8-8 56행 착지, spikes/dice-roll §4): 왼쪽에 멈춘 것부터 하나씩 결과 줄에 서고 합계가 오른다.</summary>
    public static class BattleRoll
    {
        /// <summary>그 항목의 굴림 단계(56행 — 없으면 null).</summary>
        public static FxStep StepOf(FxEntry entry) => entry?.Step(560);

        /// <summary>굴림판이 독에 떠 있을 때인가: 지금 항목에 굴림 단계가 있고, 시작한 뒤 <see cref="BattleBeats.DockSwapStartMs"/>가 지났다(항목이 끝날 때까지 떠 있다).</summary>
        public static bool Showing(FxSequencer seq)
        {
            var roll = seq != null ? StepOf(seq.Current) : null;
            return roll != null && roll.Events.Count > 0 && seq.LocalMs >= roll.StartMs + BattleBeats.DockSwapStartMs;
        }

        /// <summary>결과 줄에 설 면(엔진 순서): 굴린 면에 그 항목의 면 바꾸기를 모두 넣은 것. 지운 주사위는 None, 더한 주사위는 뒤에 붙는다.</summary>
        public static List<AttackDieFace> FinalFaces(FxEntry entry)
        {
            var faces = new List<AttackDieFace>();
            var roll = StepOf(entry);
            if (roll == null || roll.Events.Count == 0 || !(roll.Events[0] is DiceRolled d) || d.Faces == null) return faces;
            faces.AddRange(d.Faces);
            foreach (var e in entry.Events)
            {
                if (!(e is DiceFacesChanged c) || c.Changes == null) continue;
                foreach (var ch in c.Changes)
                {
                    if (ch.Index < 0) continue;
                    while (faces.Count <= ch.Index) faces.Add(AttackDieFace.None);
                    faces[ch.Index] = ch.After;
                }
            }
            return faces;
        }

        /// <summary>면 바꾸기로 바뀐 주사위인가(엔진 순서) — 결과 줄에서 회색으로 선다.</summary>
        public static bool Changed(FxEntry entry, int index)
        {
            foreach (var e in entry.Events)
                if (e is DiceFacesChanged c && c.Changes != null)
                    foreach (var ch in c.Changes)
                        if (ch.Index == index) return true;
            return false;
        }

        /// <summary>
        /// 세기가 시작하는 항목 시각(재생 <see cref="DiceRollScene.CountMs"/>와 같은 식 — 녹화본 없이 굴림판 머리만 그릴 때 쓴다):
        /// 던짐 + 굴림 위쪽 끝 + (바로 이어진 면 바꾸기 단계마다 한 박자) + 틈.
        /// </summary>
        public static int CountMs(FxEntry entry)
        {
            var roll = StepOf(entry);
            if (roll == null) return 0;
            int cursor = roll.StartMs + PlayTiming.DiceThrowMs.Value + PlayTiming.DiceRollMaxMs.Value, prevEnd = roll.EndMs;
            foreach (var step in entry.Steps)
            {
                if (step == roll || step.StartMs < roll.EndMs) continue;
                if (step.StartMs != prevEnd) break;
                bool any = false;
                foreach (var e in step.Events)
                    if (e is DiceFacesChanged d && d.Changes != null && d.Changes.Length > 0) any = true;
                if (!any) break;
                cursor += PlayTiming.FaceFlipMs.Value;
                prevEnd = step.EndMs;
            }
            return cursor + PlayTiming.DiceCountGapMs.Value;
        }

        /// <summary>그 시각까지 센 주사위 수(0 ~ <paramref name="dice"/>): 세기 시작에 첫째, 그 뒤 <c>PlayTiming.DiceCountPerMs</c>마다 하나.</summary>
        public static int Counted(int countMs, int dice, int localMs)
        {
            if (dice <= 0 || localMs < countMs) return 0;
            int per = Math.Max(1, PlayTiming.DiceCountPerMs.Value);
            return Math.Min(dice, (localMs - countMs) / per + 1);
        }

        /// <summary>k번째(0부터) 주사위를 센 때.</summary>
        public static int CountedAt(int countMs, int k) => countMs + k * Math.Max(1, PlayTiming.DiceCountPerMs.Value);

        /// <summary>앞에서부터 <paramref name="count"/>개의 눈 합(심볼 0 · 일반 1 · 급소 2 — 규칙의 <c>AttackDice.Sum</c>).</summary>
        public static int Sum(IReadOnlyList<AttackDieFace> faces, int count)
        {
            var head = new List<AttackDieFace>();
            for (int i = 0; i < count && i < faces.Count; i++) head.Add(faces[i]);
            return AttackDice.Sum(head);
        }

        /// <summary>
        /// 계산 줄(61행): 그 단계 안 시각에 켜진 칸 수(총합 앞의 칸 <paramref name="chips"/>개 가운데). 첫 칸은 바로, 둘째는 150, 그 뒤 100마다(spikes/dice-roll §4),
        /// 총합은 <c>PlayTiming.DamageCalcMs</c>에 켜진다(<see cref="TotalLit"/>) — 칸이 더 생겨도 총합의 때는 그대로다.
        /// </summary>
        public static int CalcLit(int chips, int stepMs)
        {
            if (stepMs < 0) return 0;
            int total = PlayTiming.DamageCalcMs.Value, lit = 0;
            for (int i = 0; i < chips; i++)
            {
                int at = i == 0 ? 0 : Math.Min(Math.Max(0, total - 50), 150 + (i - 1) * 100);
                if (stepMs >= at) lit++;
            }
            return lit;
        }

        public static bool TotalLit(int stepMs) => stepMs >= PlayTiming.DamageCalcMs.Value;
    }

    /// <summary>우선권 토큰이 그 순간 DS에 놓인 모습(<see cref="InitiativeBeats.Of"/>). 자리는 화면이 쪽마다 정한다(체력 상자 옆).</summary>
    public struct InitiativePose
    {
        /// <summary>떠나는 쪽 · 가는 쪽(배틀의 쪽 0·1). 머무를 때는 둘이 같고, 토큰이 없으면 둘 다 −1.</summary>
        public int From, To;
        /// <summary>From 자리에서 To 자리로 간 만큼(0 ~ 1).</summary>
        public float Travel;
        /// <summary>자리에서 위로 뜬 만큼(DS px — 떨어지는 중 · 포물선).</summary>
        public int Lift;
        /// <summary>도는 동전의 보이는 폭(0 ~ 1)과 뒷면이 보이나.</summary>
        public float Width;
        public bool Back;
        /// <summary>빛나는 만큼(0 ~ 1 — 하얗게)과 둘레 반짝이의 자리(0 ~ 3, 없으면 −1).</summary>
        public float White;
        public int Twinkle;
    }

    /// <summary>
    /// 우선권 토큰(04 §8-8 53·54행, 결정 73 ⑦ — 그림 = 원작 TTS 토큰을 도트로 다시 그린 Gen <c>Stage/Hud/initiative_token.png</c>·<c>_back.png</c>):
    /// 인트로 끝에 도전자 쪽에 떨어져 놓이고(53행 3500 안 — 방장 길이 그대로), 동률을 묻는 동안 빛나며(늘 도는 시각), 쓰면 돌며 상대 쪽으로 옮겨 간다(54행 「토큰 +600」 = 선턴 단계의 앞 600 — 상자 흔들림은 그 뒤).
    /// 지금 항목의 단계와 그 안 시각만으로 정한다 — 같은 시각이면 같은 모습이다 [가정 — 박자·높이는 캡처로 맞춤].
    /// </summary>
    public static class InitiativeBeats
    {
        /// <summary>인트로(53행): 놓이기 시작하는 단계 안 시각(둘째 포켓몬의 체력 상자가 다 들어온 2400 뒤) · 떨어지는 시간 · 그 가운데 튀어 오르는 시간 · 높이(DS px).</summary>
        public const int PlaceAtMs = 2600, DropMs = 300, BounceMs = 100, DropPx = 24, BouncePx = 2;
        /// <summary>넘기기(54행): 도는 시간(= <c>PlayTiming</c> 540의 토큰 몫 600) · 바퀴 수 · 포물선 높이(DS px).</summary>
        public const int PassMs = 600, PassTurns = 2, ArcPx = 18;
        /// <summary>묻는 동안: 반짝이가 한 칸 옮기는 간격 · 한 번 빛나는 길이(= 네 칸).</summary>
        public const int TwinkleEveryMs = 200, GlowEveryMs = 4 * TwinkleEveryMs;
        static readonly float[] GlowSteps = { 0f, 0.25f, 0.5f, 0.25f };

        /// <param name="side">드러난 토큰의 쪽(<see cref="BattleSeen.TokenSide"/>, 없으면 −1).</param>
        /// <param name="asking">동률이라 토큰을 쓸지 묻는 중인가.</param>
        /// <param name="ambientMs">늘 도는 장식의 시각(묻는 동안은 대기열이 비어 있다).</param>
        public static InitiativePose Of(FxSequencer seq, int side, bool asking, int ambientMs)
        {
            var pose = new InitiativePose { From = side, To = side, Width = 1f, Twinkle = -1 };
            bool moving = false;
            var entry = seq != null ? seq.Current : null;
            if (entry != null)
                foreach (var s in entry.Steps)
                {
                    if (s.Layer != FxLayer.Item || seq.LocalMs < s.StartMs) continue;
                    int t = seq.LocalMs - s.StartMs;
                    foreach (var e in s.Events)
                        switch (e)
                        {
                            case InitiativeTokenAt it:
                            {
                                int u = seq.LocalMs - FxReveal.At(entry, it);
                                if (u < 0 || u >= DropMs || it.Side < 0) break;
                                pose.From = pose.To = it.Side;
                                int fall = DropMs - BounceMs;
                                pose.Lift = u < fall
                                    ? (int)Math.Round(DropPx * (1f - Tween.Apply(Ease.InQuad, Tween.Progress(u, 0, fall))))
                                    : (int)Math.Round(BouncePx * Math.Sin(Math.PI * Tween.Progress(u, fall, BounceMs)));
                                moving = true;
                                break;
                            }
                            case OrderDecided o when o.TokenUsed && o.TokenTo >= 0 && o.TokenTo < 2 && t < PassMs:
                            {
                                float u = Tween.Progress(t, 0, PassMs);
                                pose.From = 1 - o.TokenTo;
                                pose.To = o.TokenTo;
                                pose.Travel = Tween.Apply(Ease.InOutQuad, u);
                                pose.Lift = (int)Math.Round(ArcPx * 4f * u * (1f - u));
                                double c = Math.Cos(u * PassTurns * 2 * Math.PI);
                                pose.Width = (float)Math.Abs(c);
                                pose.Back = c < 0;
                                moving = true;
                                break;
                            }
                        }
                }
            if (asking && !moving && pose.From >= 0)
            {
                int step = Math.Max(0, ambientMs) / TwinkleEveryMs;
                pose.White = GlowSteps[step % GlowSteps.Length];
                pose.Twinkle = step % 4;
            }
            return pose;
        }
    }

    /// <summary>
    /// 준비 동작(결정 143 ⑦ — 사용자): 공격 연출을 「준비」와 「맞는 순간」으로 나눈다.
    /// 기술을 고르면 공격한 도트가 굴림(56행)이 시작할 때 당기고(<see cref="BattleBeats.PrepInMs"/>) 떨며 기를 모은다 — DS 메시지는 「○○의 △△!」(<see cref="BattleFeed.PrepLine"/>).
    /// 맞음 · 피해 · 상성처럼 결과가 드러나는 것은 주사위가 최종(재굴림 · 반응 창 — 디아루가 · 트레이너 AfterRoll 포함)이 된 뒤의 59행부터다.
    /// 재굴림 창이 열려 항목 사이에 멈춘 동안은 당긴 자세로 기다린다(<see cref="BattleSeen.Winding"/>). 박자(방장 길이)는 굴림 단계 안이라 늘지 않는다.
    /// </summary>
    public static class PrepBeats
    {
        /// <summary>
        /// 그 포켓몬을 상대 반대쪽으로 당긴 만큼(DS px, 0 = 제자리): 지금 항목에 그 포켓몬의 공격 굴림이 시작했고 그 뒤의 맞는 순간(59행)이 아직이면 굴림 시작에서 잰 박자,
        /// 아니면 준비 동작 중(<paramref name="winding"/>)이면 다 당긴 자세(떨지 않는다 — 대기열이 멈춘 동안 화면이 다시 그리지 않아도 같은 모습).
        /// </summary>
        public static int Back(FxEntry entry, int localMs, int pokemon, bool winding)
        {
            int from = -1;
            if (entry != null && pokemon >= 0)
                foreach (var s in entry.Steps)
                {
                    if (s.Layer != FxLayer.Item || localMs < s.StartMs) continue;
                    if (s.Row == 560 && s.Events.Count > 0 && s.Events[0] is DiceRolled d && d.Kind == DiceKind.Attack && d.PokemonId == pokemon) from = s.StartMs;
                    else if (s.Row == 590)
                        foreach (var e in s.Events)
                            if (e is MoveUsed m && m.PokemonId == pokemon) from = -2;   // 맞는 순간이 시작했다
                }
            if (from >= 0) return At(localMs - from);
            if (from == -2) return 0;
            return winding ? BattleBeats.LungeBackPx : 0;
        }

        /// <summary>굴림이 시작한 뒤 u ms의 당김(DS px): 당겨 들어가고, 다 당긴 뒤에는 한 박자씩 1px 더 당겨 떤다.</summary>
        public static int At(int u)
        {
            if (u < 0) return 0;
            if (u < BattleBeats.PrepInMs) return (int)Math.Round(BattleBeats.LungeBackPx * Tween.Apply(Ease.OutQuad, Tween.Progress(u, 0, BattleBeats.PrepInMs)));
            return BattleBeats.LungeBackPx + ((u - BattleBeats.PrepInMs) / BattleBeats.ChargeEveryMs % 2 == 1 ? BattleBeats.ChargePx : 0);
        }
    }

    /// <summary>DS 화면의 포켓몬 하나가 그 순간 움직인 만큼(DS px)과 체력 막대의 중간값.</summary>
    public struct MonPose
    {
        /// <summary>도트를 옮긴 만큼.</summary>
        public int Dx, Dy;
        /// <summary>도트가 발판 아래로 내려간 만큼(0 ~ 1, 기절).</summary>
        public float Sink;
        /// <summary>도트를 그리지 않는 순간(맞아서 깜빡임).</summary>
        public bool Blink;
        /// <summary>체력 상자를 옮긴 만큼(아래로 · 옆으로)과 아직 들어오지 않았나(인트로).</summary>
        public int BoxDy, BoxDx;
        public bool BoxHidden;
        /// <summary>체력 막대가 깎이는 중이면 그 순간의 체력(<see cref="Draining"/>). 숫자는 착지 때 바뀐다(표시 값).</summary>
        public bool Draining;
        public float Bar;
    }

    /// <summary>
    /// 배틀 단계의 DS 쪽 움직임(04 §8-8 54·59·60·61·63·64·65·67행): 지금 항목의 단계와 그 안 시각만으로 정한다 — 같은 시각이면 같은 모습이다.
    /// 플래시·화면 흔들림은 없다(결정 53 ②). 기술마다의 이펙트는 없다(타입 공용도 아직).
    /// </summary>
    public static class BattlePose
    {
        /// <param name="pokemon">그 쪽에 나와 있는 포켓몬.</param>
        /// <param name="side">배틀의 쪽(0·1).</param>
        /// <param name="enemy">DS 화면의 상대 자리(오른쪽 위)에 그리나 — 돌진·도망의 방향.</param>
        /// <param name="winding">드러난 준비 동작 중인 포켓몬(<see cref="BattleSeen.Winding"/> — 항목 사이에 멈춘 동안 당긴 자세로 남는다, 없으면 −1).</param>
        public static MonPose Of(FxSequencer seq, int pokemon, int side, bool enemy, int winding = -1)
        {
            var pose = new MonPose();
            var entry = seq != null ? seq.Current : null;
            if (pokemon < 0) return pose;
            int toward = enemy ? -1 : 1;   // 상대 쪽으로 가는 x 방향
            // 준비 동작(결정 143 ⑦): 굴림과 함께 당겨 기를 모으고, 맞는 순간(59행)이 시작할 때까지 그 자세 — 재굴림 창이 열려 있는 동안도
            int back = PrepBeats.Back(entry, entry != null ? seq.LocalMs : 0, pokemon, winding == pokemon);
            if (back != 0) pose.Dx = -toward * back;
            if (entry == null) return pose;
            foreach (var s in entry.Steps)
            {
                if (s.Layer != FxLayer.Item || seq.LocalMs < s.StartMs) continue;
                int t = seq.LocalMs - s.StartMs;
                bool active = t < s.Ms;
                foreach (var e in s.Events)
                    switch (e)
                    {
                        case OrderDecided o when active && o.First == side:
                        {
                            // 토큰을 썼으면 토큰이 상대 쪽으로 다 간 뒤에 흔들린다(54행 「토큰 +600」)
                            int u = t - (o.TokenUsed ? InitiativeBeats.PassMs : 0);
                            if (u >= 0 && u < BattleBeats.OrderBobMs && u / BattleBeats.OrderBobEveryMs % 2 == 0) pose.BoxDy = 1;
                            break;
                        }
                        case MoveUsed m when active && m.PokemonId == pokemon:
                            // 맞는 순간(결정 143 ⑦ — 당김은 굴림과 함께 준비 동작이 했다): 당긴 자세에서 돌진 → 제자리.
                            // 옛 박자(결정 143 전): 당김 150 → 돌진 150 → 제자리 150 + 문장 읽기 = 1400
                            pose.Dx = t < BattleBeats.LungeHitMs ? toward * BattleBeats.LungeHitPx : 0;
                            break;
                        case TypeMatchup tm when active && tm.DefenderId == pokemon:
                            if (t < BattleBeats.BlinkMs && t / BattleBeats.BlinkEveryMs % 2 == 1) pose.Blink = true;
                            if (tm.Delta > 0 && t < BattleBeats.ShakeMs) pose.Dx = t / BattleBeats.ShakeEveryMs % 2 == 0 ? BattleBeats.ShakePx : -BattleBeats.ShakePx;
                            break;
                        case HpChanged h when active && h.PokemonId == pokemon:
                            Drain(ref pose, h.Before, h.After, t, s.Ms);
                            break;
                        case StatusTicked st when active && st.PokemonId == pokemon && st.HpBefore != GameEvent.U && st.HpAfter != GameEvent.U:
                            Drain(ref pose, st.HpBefore, st.HpAfter, t, s.Ms);
                            break;
                        case SentIn si when s.Row == FxRows.Intro && si.PokemonId == pokemon:
                        {
                            // 인트로(53행): 도트가 옆에서 미끄러져 들어오고 체력 상자가 뒤따른다
                            int u = seq.LocalMs - FxReveal.At(entry, si);
                            if (u < 0) break;
                            int from = enemy ? 1 : -1;
                            // 내 쪽의 첫 출전은 주인공이 던진 볼에서 나온다 — 미끄러져 들어오지 않는다(결정 99 ②, 볼·뒷모습은 IntroStage)
                            // 상대 트레이너의 첫 출전도 볼에서 나온다(M9 다듬기 P05 — IntroStage.FoePose)
                            // 첫 출전을 고른 항목의 야생 쪽 상대는 고르는 동안 이미 서 있었다(결정 143 ⑤) — 다시 미끄러져 들어오지 않는다
                            if (!(enemy ? IntroStage.FoeThrown(entry, s, si) : IntroStage.Thrown(entry, s, si)) && entry.Kind != ActionKind.SendIn) pose.Dx = from * (int)Math.Round(EnterBeats.IntroSlidePx * (1f - Tween.Apply(Ease.OutQuad, Tween.Progress(u, 0, EnterBeats.IntroSlideMs))));
                            if (u < EnterBeats.IntroBoxLeadMs) pose.BoxHidden = true;
                            else pose.BoxDx = -from * (int)Math.Round(EnterBeats.IntroBoxPx * (1f - Tween.Apply(Ease.OutQuad, Tween.Progress(u, EnterBeats.IntroBoxLeadMs, EnterBeats.IntroBoxMs))));
                            break;
                        }
                        case Fainted f when f.PokemonId == pokemon:
                            pose.Sink = Math.Max(pose.Sink, Tween.Progress(t, 0, BattleBeats.FaintSinkMs));
                            break;
                        case Fled fl when fl.Side == side:
                            pose.Dx = -toward * (int)Math.Round(BattleBeats.FledPx * Tween.Apply(Ease.InQuad, Tween.Progress(t, 0, BattleBeats.FledMs)));
                            break;
                    }
            }
            return pose;
        }

        // 체력 막대: 빠르게, 그다음 느리게(61행 [원작 충실]). 같은 단계에 그 포켓몬의 변화가 둘이면 뒤의 것이 마지막 값이다
        static void Drain(ref MonPose pose, int before, int after, int t, int ms)
        {
            pose.Draining = true;
            pose.Bar = Tween.At(t, 0, ms, before, after, Ease.OutCubic);
        }
    }
}
