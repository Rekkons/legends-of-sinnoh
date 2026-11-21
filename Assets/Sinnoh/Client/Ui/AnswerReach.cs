using System.Collections.Generic;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 화면이 「지금 낼 수 있는 답」을 알리는 자리(M9 실행 감사 ② 화면 도달 검사). 화면이 이것을 구현하면 <see cref="AnswerReach"/>가 그 화면의 말을 먼저 듣는다 —
    /// into에 지금 열린 화면 또는 한 번 눌러 여는 화면의 누를 수 있는 단추가 내는 답을 더한다(쪽 넘김 · 숨김 · 꺼짐으로 못 누르는 답은 넣지 않는다).
    /// 지금은 구현한 화면이 없다(다른 작업 폴더가 같은 화면 파일을 고치는 중이라 화면 파일은 건드리지 않았다) — 화면의 공개 상태를 읽는 갈래가 <see cref="AnswerReach"/>에 있다.
    /// </summary>
    public interface IAnswerOffer
    {
        /// <summary>그 선택에서 이 화면이 낼 수 있는 답을 into에 더한다. 이 선택을 받지 않는 화면이면 false.</summary>
        bool Offers(PendingChoice choice, List<ChoiceAnswer> into);
    }

    /// <summary>
    /// 확인용(게임 화면은 쓰지 않는다): 사람 좌석의 선택마다 합법 답이 화면의 누를 수 있는 단추로 나오는지 본다. 단추를 누르지 않는다 — 고르기 · 창은 열어 보고 되돌린다
    /// (능력 고르기 · 마을 · 제작 · 진화 · 사당 판 · 배틀 독의 줄). 답 하나가 곧바로 나가는 단추(답이 하나뿐인 [능력] · 칸 하나 답)는 누르지 않고 단추가 있는 것으로 센다.
    /// 덮는 범위: 맵 행동(칸 이동 · 행동 단추 · 마을 · 제작 · 진화 · 공물 · 능력 · 날씨 휠 · 운명 · 잠수) · 숨겨진 장소 · 보드의 카드 물음 · 조우 · 배틀 화면이 받는 선택(기술 · 가방 · 대기 · 보상 · 카드 물음 · 우선권 · 순서 · 응답) ·
    /// 6장째 버리기 · 창 갈래(AnswerReach.Screens — 사당 판의 고르기 · 천상의 균열 · 카드 보상 창 · 카드 바꾸기 · 진화 분기 · 옛 기술 · 종막 · 왜곡 균열 카드 · 축복 · S41 응답 · 거래 카드 고르기).
    /// 그 밖은 <see cref="Result.Uncovered"/>로 센다.
    /// </summary>
    public static partial class AnswerReach
    {
        public sealed class Result
        {
            /// <summary>화면이 받을 차례가 아니었다(연출 중 · 받는 화면이 아직 없음) — 세지 않는다.</summary>
            public bool NotReady;
            /// <summary>덮지 않는 선택 종류.</summary>
            public bool Uncovered;
            /// <summary>합법 답인데 누를 단추가 없다 — 답과 까닭.</summary>
            public readonly List<(ChoiceAnswer Answer, string Why)> Missing = new List<(ChoiceAnswer, string)>();
            /// <summary>본 합법 답 수.</summary>
            public int Legal;
            /// <summary>받은 화면(덮은 범위 표 — 맵 · 조우 · 손패 창 · 배틀 화면 · 보드 카드 물음 · 창 갈래 이름).</summary>
            public string Where;
        }

        public static Result Check(GameFlow flow, PendingChoice c)
        {
            var r = new Result();
            var client = flow.Client;
            if (client == null || client.View == null || c == null) { r.NotReady = true; return r; }
            var legal = client.Legal(c);
            r.Legal = legal.Count;
            if (flow.Top is IAnswerOffer offer)
            {
                var into = new List<ChoiceAnswer>();
                if (offer.Offers(c, into))
                {
                    foreach (var a in legal)
                        if (!into.Exists(x => x.SameAs(a))) r.Missing.Add((a, "화면이 알린 답에 없음"));
                    return r;
                }
            }
            switch (c.Kind)
            {
                case ChoiceKind.MapAction:
                case ChoiceKind.HiddenSpot:
                    r.Where = "보드 맵";
                    if (!flow.InputOpen || flow.Hud == null) { r.NotReady = true; return r; }
                    Map(flow, c, legal, r);
                    return r;
                case ChoiceKind.EncounterPick:
                {
                    r.Where = "조우 창";
                    var screen = flow.Stage.Current as EncounterScreen;
                    if (screen == null || !screen.Open) { r.NotReady = true; return r; }
                    foreach (var a in legal)
                    {
                        if (a.Kind == AnswerKind.Pick && Has(screen.Cards, a)) continue;
                        if (a.Kind == AnswerKind.UseAbility && screen.RetryOn) continue;
                        if (a.Kind == AnswerKind.Craft) continue; // 고르기 전 제작 — 조우 창의 제작 길은 이 검사가 보지 않는다
                        r.Missing.Add((a, "조우 창에 없음"));
                    }
                    return r;
                }
            }
            if (Screens(flow, c, legal, r)) return r;   // 창이 받는 선택(AnswerReach.Screens)
            if (c.Kind == ChoiceKind.Discard)
            {
                r.Where = "손패 창(버리기)";
                // 6장째 버리기: 저절로 열린 손패 창(배틀 화면 위에도 — GameFlow.Top)
                if (!(flow.Top is FateScreen fate)) { r.NotReady = true; return r; }
                foreach (var a in legal)
                    if (!Contains(fate.Hand, a.Target)) r.Missing.Add((a, "손패 창에 그 카드가 없음"));
                return r;
            }
            if (flow.Stage.Current is BattleScreen battle)
            {
                // 끝난 배틀의 결과 창은 배틀 뒤 보상(Reward)만 받는다 — 그 밖의 선택(backpacker B 회복 · 배틀 중에 풀린 카드의 고르기 등 BattleFlow.Close의 카드 물음)은
                // [확인](GameFlow.CloseBattle) 뒤 보드의 카드 물음 단추가 받는다(BattleScreen.Refresh). 받을 화면이 아직 없다 — [확인] 뒤에 다시 본다(전체 판 X6 「배틀 화면에 없음」 2회는 이 오탐)
                if (flow.Battle != null && flow.Battle.Ended && c.Kind != ChoiceKind.Reward)
                {
                    if (flow.BattleOver && client.Choice(ChoiceKind.Reward) != null && legal.Count > 0) r.Missing.Add((legal[0], "결과 창 [확인]을 누를 수 없음(보상이 남음)"));
                    else r.NotReady = true;
                    return r;
                }
                // 굴림 연출 중에는 독이 고르기 단추를 아직 그리지 않는다(BattleScreen — !rolling)
                if (!battle.Ready || (battle.AskingPick && battle.Picks.Count == 0 && legal.Exists(a => a.Kind == AnswerKind.Pick))) { r.NotReady = true; return r; }
                Battle(battle, c, legal, r);
                return r;
            }
            if (flow.Battle != null && !flow.Battle.Ended && Battling(c)) { r.NotReady = true; return r; } // 배틀 화면이 아직 열리기 전(조우 전환)
            if (flow.Hud != null && flow.Hud.CardAsking && BoardHud.BoardCardAsk(c))
            {
                r.Where = "보드 카드 물음";
                if (!flow.InputOpen) { r.NotReady = true; return r; }
                foreach (var a in legal)
                {
                    if (Has(flow.Hud.CardAnswers, a) && !(flow.Hud.CardCells && a.Kind == AnswerKind.Pick)) continue;
                    if (flow.Hud.CardCells && a.Kind == AnswerKind.Pick && Contains(flow.Hud.Reach, a.Target)) continue;
                    r.Missing.Add((a, "보드 카드 물음 단추에 없음"));
                }
                return r;
            }
            r.Uncovered = true;
            return r;
        }

        /// <summary>배틀 화면이 받는 선택 꼴.</summary>
        static bool Battling(PendingChoice c) =>
            c.Kind == ChoiceKind.BattleAction || c.Kind == ChoiceKind.SendIn || c.Kind == ChoiceKind.NextPokemon || c.Kind == ChoiceKind.SwitchWindow || c.Kind == ChoiceKind.Lineup || c.Kind == ChoiceKind.Reaction;

        static bool Has(IReadOnlyList<ChoiceAnswer> list, ChoiceAnswer a)
        {
            if (list == null) return false;
            foreach (var x in list)
                if (x.SameAs(a)) return true;
            return false;
        }

        static bool Contains(IReadOnlyList<int> list, int v)
        {
            if (list == null) return false;
            foreach (int x in list)
                if (x == v) return true;
            return false;
        }

        // ── 맵 ──

        static void Map(GameFlow flow, PendingChoice c, List<ChoiceAnswer> legal, Result r)
        {
            var hud = flow.Hud;
            if (c.Kind == ChoiceKind.HiddenSpot)
            {
                foreach (var a in legal)
                    if (!hud.Shows(a.Kind == AnswerKind.Accept ? BoardHud.Reveal : BoardHud.Pass)) r.Missing.Add((a, "숨겨진 장소 단추 없음"));
                return;
            }
            var abilityChecked = new List<ChoiceAnswer>();
            foreach (var a in legal)
            {
                switch (a.Kind)
                {
                    case AnswerKind.Move:
                        if (!Contains(hud.Reach, a.Target)) { r.Missing.Add((a, "칸이 칠해지지 않음")); break; }
                        // 같은 칸의 무료 지형 이동과 보통 걸음이 둘 다 합법이면 화면이 고르는 쪽만 낼 수 있다(경유 칸 없이)
                        var other = legal.Find(x => x.Kind == AnswerKind.Move && x.Target == a.Target && x.Arg != a.Arg && x.Values.Count == 0);
                        if (other != null && (a.Arg == TrainerRules.FreeArg || other.Arg == TrainerRules.FreeArg) && hud.PickCell(a.Target))
                        {
                            bool free = hud.PickedFree;
                            // [이동] 곁의 바꾸기 단추(무료 지형 이동 ↔ 이동력으로 걷기 — #660)로 반대 걸음을 고를 수 있으면 그 걸음으로 본다
                            if (free != (a.Arg == TrainerRules.FreeArg) && hud.FreeChoosable && hud.ToggleFree()) free = hud.PickedFree;
                            hud.Unpick();
                            if (free != (a.Arg == TrainerRules.FreeArg)) r.Missing.Add((a, free ? "무료 길이 있으면 [이동]이 늘 무료로 낸다 — 보통 걸음을 고를 수 없음(경유 칸 없이)" : "[이동]이 보통 걸음으로 낸다 — 무료 지형 이동을 고를 수 없음(경유 칸 없이)"));
                        }
                        break;
                    case AnswerKind.VillageHeal:
                    case AnswerKind.Fly:
                    case AnswerKind.Ginkgo:
                        Village(flow, a, r);
                        break;
                    case AnswerKind.Craft:
                        if (!hud.Shows(BoardHud.Craft)) { r.Missing.Add((a, "[제작] 단추 없음")); break; }
                        if (!(flow.OpenCraft() && flow.Stage.Current is CraftScreen craft)) { r.Missing.Add((a, "제작 창이 열리지 않음")); break; }
                        bool make = craft.CanMake(a.Target);
                        flow.CloseCraft();
                        if (!make) r.Missing.Add((a, "제작 창에 그 아이템이 없음"));
                        break;
                    case AnswerKind.Evolve:
                        Evolve(flow, a, r);
                        break;
                    case AnswerKind.Offer:
                        if (!hud.Shows(BoardHud.Offer)) { r.Missing.Add((a, "[공물] 단추 없음")); break; }
                        if (!(flow.OpenShrine() && flow.Stage.Current is ShrineScreen shrine)) { r.Missing.Add((a, "사당 판이 열리지 않음")); break; }
                        bool offered = shrine.Offers().Exists(x => x.SameAs(a));
                        flow.CloseShrine();
                        if (!offered) r.Missing.Add((a, "사당 판이 그 공물을 내지 않음"));
                        break;
                    case AnswerKind.UseAbility:
                        Ability(flow, c, legal, a, r);
                        break;
                    case AnswerKind.EndTurn:
                        if (a.Target >= 0 ? !hud.Shows(BoardHud.Dive) : !hud.Shows(BoardHud.EndTurn)) r.Missing.Add((a, a.Target >= 0 ? "[잠수] 능력 단추 없음" : "[턴 종료] 없음"));
                        break;
                    case AnswerKind.PlayFate:
                    {
                        var fate = flow.Stage.Root.Q<Button>(BoardHud.FateButton);
                        if (fate == null || !fate.enabledSelf) r.Missing.Add((a, "[운명의 변화] 단추 없음"));
                        break;
                    }
                    default:
                    {
                        var act = System.Array.Find(BoardHud.MapActs, x => x.Kind == a.Kind);
                        if (act == null) r.Missing.Add((a, "맵 행동 단추 표에 없는 답 종류"));
                        else if (!hud.Shows(act.Id)) r.Missing.Add((a, $"[{act.Id}] 단추 없음"));
                        break;
                    }
                }
            }
        }

        static void Village(GameFlow flow, ChoiceAnswer a, Result r)
        {
            // 마을 밖 칸의 공중날기(bird_keeper B 새 휘슬 — 감사 G1-1 고친 뒤)는 [날기] 단추가 공중날기 칸만 있는 마을 창을 연다
            bool whistle = a.Kind == AnswerKind.Fly && !flow.Hud.Shows(BoardHud.Village);
            if (whistle && !flow.Hud.Shows(BoardHud.Fly)) { r.Missing.Add((a, "[날기] 단추 없음(마을 밖 · 새 휘슬)")); return; }
            if (!whistle && !flow.Hud.Shows(BoardHud.Village)) { r.Missing.Add((a, "[마을] 단추 없음(마을 · 야영지에 서 있지 않음)")); return; }
            if (!((whistle ? flow.OpenWhistle() : flow.OpenVillage()) && flow.Stage.Current is VillageScreen village)) { r.Missing.Add((a, "마을 창이 열리지 않음")); return; }
            bool ok = a.Kind == AnswerKind.VillageHeal ? village.CanHeal : a.Kind == AnswerKind.Fly ? village.CanGo(a.Target) && village.CanPay(a.Arg) : village.Bank != null;
            // T18 ①: 공중날기 목적지는 보드에서 고른다(새 휘슬 창은 처음부터, 마을 창은 [지도에서 고르기]) — 그 마을 칸이 칠해져야 누를 수 있다
            bool lit = !ok || a.Kind != AnswerKind.Fly || ((village.Picking || village.BeginMap()) && Contains(village.Lit, flow.Ctx.Fixtures.Fixtures[a.Target].Cell));
            flow.CloseVillage();
            if (!ok) r.Missing.Add((a, "마을 창에 그 답이 없음"));
            else if (!lit) r.Missing.Add((a, "보드에서 고를 때 그 마을이 칠해지지 않음"));
        }

        static void Evolve(GameFlow flow, ChoiceAnswer a, Result r)
        {
            if (!flow.Hud.Shows(BoardHud.Evolve)) { r.Missing.Add((a, "[진화] 단추 없음")); return; }
            if (!(flow.OpenEvolve() && flow.Stage.Current is EvolveScreen evolve)) { r.Missing.Add((a, "진화 창이 열리지 않음")); return; }
            bool ok;
            if (a.Arg == 2) ok = evolve.EggsOf(a.Target).Exists(x => x.SameAs(a));
            else
            {
                bool half = evolve.Half;
                evolve.SetHalf(a.Arg == 1);
                ok = evolve.CanEvolve(a.Target);
                evolve.SetHalf(half);
            }
            flow.CloseEvolve();
            if (!ok) r.Missing.Add((a, "진화 창에 그 답이 없음"));
        }

        /// <summary>능력 · 날씨 휠: 단추 → (칸) → (묶음) → 값 단추가 지금 쪽에 보이나. 쪽을 넘겨야 보이면 「둘째 쪽」.</summary>
        static void Ability(GameFlow flow, PendingChoice c, List<ChoiceAnswer> legal, ChoiceAnswer a, Result r)
        {
            var hud = flow.Hud;
            bool weather = CardAsk.IsWeather(a);
            if (!hud.Shows(weather ? BoardHud.Weather : BoardHud.Ability)) { r.Missing.Add((a, weather ? "[날씨 휠] 단추 없음" : "[능력] 단추 없음")); return; }
            var group = legal.FindAll(x => x.Kind == AnswerKind.UseAbility && CardAsk.IsWeather(x) == weather);
            if (group.Count == 1 && hud.CellFor(group[0]) < 0) return; // 단추가 곧바로 낸다
            if (!hud.AbilityPicking && !hud.BeginAbility(weather)) { r.Missing.Add((a, "능력 고르기가 열리지 않음")); return; }
            try
            {
                if (Has(hud.AbilityShown, a)) return;
                int cell = hud.CellFor(a);
                if (cell >= 0)
                {
                    if (!Contains(hud.Reach, cell)) { r.Missing.Add((a, "능력의 칸이 칠해지지 않음")); return; }
                    int here = group.FindAll(x => hud.CellFor(x) == cell).Count;
                    if (here == 1) return; // 칸을 누르면 곧바로 낸다
                    if (!hud.AbilityAt(cell)) { r.Missing.Add((a, "능력의 칸을 눌러도 고르기가 안 열림")); return; }
                    // 염력(psychic B — T33): 그 칸의 일이 공물뿐이면 칸 누르기가 떨어진 사당 판을 연다 — 판이 안쪽 답(색 · 여행 점수 7)을 내나 보고 닫는다
                    if (flow.Stage.Current is ShrineScreen far && far.Reach == cell)
                    {
                        bool offered = ShrineScreen.IsReachOffer(a, cell) && far.Offers().Exists(x => x.SameAs(TrainerRules.ReachInner(a)));
                        flow.CloseShrine();
                        if (!offered) r.Missing.Add((a, "염력 사당 판이 그 공물을 내지 않음"));
                        return;
                    }
                }
                for (int depth = 0; depth < 4; depth++)
                {
                    if (Has(hud.AbilityShown, a)) return;
                    int row = hud.AbilityRowOf(a);
                    if (row < 0) { r.Missing.Add((a, "능력 고르기 단추에 없음")); return; }
                    if (!hud.AbilityRowShown(row)) { r.Missing.Add((a, $"능력 고르기의 다른 쪽(지금 {hud.AbilityPage + 1}/{hud.AbilityPages}쪽)에 숨음")); return; }
                    if (!hud.AbilityRowIsGroup(row)) return;
                    hud.PressAbilityRow(row); // 묶음: 안으로(답을 내지 않는다)
                }
                r.Missing.Add((a, "능력 묶음이 너무 깊음"));
            }
            finally { hud.CancelAbility(); }
        }

        // ── 배틀 ──

        static void Battle(BattleScreen b, PendingChoice c, List<ChoiceAnswer> legal, Result r)
        {
            r.Where = "배틀 화면";
            var offered = new List<ChoiceAnswer>();
            void Add(IReadOnlyList<ChoiceAnswer> list)
            {
                if (list != null) offered.AddRange(list);
            }
            Add(b.Moves);
            Add(b.Bench);
            Add(b.Rewards);
            Add(b.Picks);
            Add(b.Orders);
            if (b.OpenBag()) { Add(b.Balls); b.CloseRow(); } else Add(b.Balls);
            if (b.OpenAlpha()) { Add(b.AlphaTypes); b.CloseRow(); }
            if (b.OpenPartyMoves()) { Add(b.PartyMoves); b.CloseRow(); }
            if (b.OpenAbility()) { Add(b.AbilityPicks); b.CloseRow(); }
            bool yesNo = b.AskingAsk || b.AskingToken || b.AskingRespond;
            foreach (var a in legal)
            {
                if (Has(offered, a)) continue;
                if (yesNo && (a.Kind == AnswerKind.Accept || a.Kind == AnswerKind.Decline)) continue;
                if (b.AskingPick && a.Kind == AnswerKind.Decline) continue; // 차례 칸의 [넘기기]
                if (a.Kind == AnswerKind.Forfeit && c.Kind == ChoiceKind.BattleAction) continue; // [기권]
                if (a.Kind == AnswerKind.Decline && b.CanStay) continue;
                if (c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.Fate && b.AskingFate) continue; // 운명 창 — 카드별 단추는 이 검사가 보지 않는다
                if (a.Kind == AnswerKind.UseAbility && b.CherishOn) continue; // 프레셔스 볼
                r.Missing.Add((a, $"배틀 화면에 없음({c.Kind}/{c.Sub})"));
            }
        }
    }
}
