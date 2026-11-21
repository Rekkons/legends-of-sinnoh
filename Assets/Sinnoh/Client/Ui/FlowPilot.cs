using System;
using Sinnoh.Ai;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Client
{
    /// <summary>
    /// 확인용 길잡이를 화면에 잇는다(캡처 <c>UiShots</c> · 플레이 시험 · 플레이어 확인 <c>ScreenProbe</c>·<c>MouseProbe</c>): 찾아 둔 판(<see cref="SoloTrail"/>)을 시작하고,
    /// 길잡이(<see cref="SoloPilot"/>)가 고른 답을 **화면의 함수로** 누른다 — 세션에 바로 답하지 않는다. 화면에 그 답을 낼 요소가 없으면 누르지 못한다(그것이 확인하려는 것이다).
    /// 게임 화면은 이것을 쓰지 않는다.
    /// </summary>
    public static class FlowPilot
    {
        /// <summary>
        /// 찾아 둔 판을 시작해 셋업(고향 마을 · 스타팅 포켓몬 · 첫 트레이너 카드)을 화면으로 끝낸다. 돌려주는 길잡이는 그 판의 이벤트를 따라간다.
        /// 셋업 연출(04 §8-8 1~3행 · 숨겨진 장소 배치)은 연출 시각으로 건너뛴다 — 돌아올 때는 보드 HUD가 떠 있고 서막 카드가 막 시작한다.
        /// 셋업 화면이 열리지 않거나 확정이 거절되면 InvalidOperationException.
        /// </summary>
        /// <param name="protagonist">내 좌석의 주인공(겉모양뿐 — 판의 길은 같다). null이면 기본.</param>
        /// <param name="galactic">갤럭시단 AI 세기(M8 「연결」 — 「강함」이면 첫 갤럭시단 결정부터 판의 길이 찾아 둔 판과 갈릴 수 있다).</param>
        /// <param name="undo">무르기 설정(결정 149 — 무르기 흐름 시험 · 캡처). 기본은 끔(찾아 둔 판들이 그대로 쓴다).</param>
        public static SoloPilot Start(GameFlow flow, SoloTrail trail, string protagonist = null, GalacticAiLevel galactic = GalacticAiLevel.Principle, UndoMode undo = UndoMode.Off)
        {
            if (flow == null) throw new ArgumentNullException(nameof(flow));
            if (trail == null) throw new ArgumentNullException(nameof(trail));
            var pilot = trail.Pilot();
            flow.EventsSeen += pilot.Saw;
            flow.StartSolo(new SoloSettings { Protagonist = protagonist ?? Protagonists.Default, GalacticAi = galactic, Undo = undo }, trail.Seed, SoloTrail.AiSeed, trail.PrepareFor(flow.Ctx));
            var village = flow.Stage.Current as SetupVillageScreen ?? throw new InvalidOperationException("고향 마을 고르기가 열리지 않았다");
            village.Pick(trail.Village);
            if (!village.Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
            flow.Skip(flow.QueueLeftMs);   // 고향 마을 연출(말이 떨어짐) → 카드가 들어옴(셋업 연출 — 결정 66 ③)
            var pick = flow.Stage.Current as SetupPickScreen ?? throw new InvalidOperationException("스타팅 포켓몬 고르기가 열리지 않았다");
            pick.PickStarter(trail.Starter);
            pick.PickCard(0);
            if (!pick.Confirm()) throw new InvalidOperationException("셋업 확정이 거절됐다");
            // 셋업의 고른 뒤·공개 연출과 숨겨진 장소 배치를 지나 서막 카드 앞까지(보드 HUD가 떠 있다 — 그 연출은 셋업 시험·캡처가 따로 본다)
            flow.Skip(flow.MsUntil(step => step.Row == Sinnoh.Fx.ActBeats.Row));
            // 막 길(SoloTrail.After — M5 「막·종막 화면」): 셋업이 끝난 판을 손봐 그 상태로 다시 연다(SoloPilotTests의 셋업 뒤 손보기와 같은 길). 게임 시작 연출은 없다 — 곧바로 첫 턴의 보드
            if (trail.After != null) flow.Rebase(s => trail.After(flow.Ctx, s));
            return pilot;
        }

        /// <summary>
        /// 한 걸음: 지금 내 좌석에 열린 선택에 길잡이의 답을 화면으로 누른다. 결과 창이 떠 있고 더 물을 것이 없으면 [확인]을 누른다(<paramref name="confirm"/>).
        /// 누른 것이 있으면 true. 기다려야 하거나(연출·상대 차례) 화면이 그 답을 받지 못하면 false.
        /// </summary>
        public static bool Step(GameFlow flow, SoloPilot pilot, bool confirm = true)
        {
            var client = flow.Client;
            if (client == null || client.View == null) return false;
            var battle = flow.Stage.Current as BattleScreen;
            if (battle != null && battle.ShowsResult && !battle.AskingReward && client.Choice(ChoiceKind.Reward) == null) return confirm && battle.Confirm();
            var choice = client.FirstChoice();
            if (choice == null) return false;
            var answer = pilot.Decide(flow.Ctx, client.View, choice, client.Legal(choice));
            return answer != null && Press(flow, choice, answer);
        }

        /// <summary>그 선택의 그 답을 화면으로 누른다: 보드 HUD(칸 · 단추) · 마을 창 · 조우 창 · 배틀 화면.</summary>
        public static bool Press(GameFlow flow, PendingChoice choice, ChoiceAnswer answer)
        {
            if (flow.Stage.Current is CardRewardScreen reward && CardRewardScreen.Owns(choice)) return reward.Press(answer);   // 카드가 연 놓아주기·가르치기
            if (flow.Stage.Current is CardSwapScreen swap && CardSwapScreen.Owns(flow.Ctx, choice)) return swap.Press(answer);   // 카드 바꾸기(부채꼴 창)
            switch (choice.Kind)
            {
                case ChoiceKind.MapAction:
                case ChoiceKind.HiddenSpot:
                {
                    var hud = flow.Hud;
                    if (hud == null || !flow.InputOpen) return false;
                    if (answer.Kind == AnswerKind.Move) return hud.Walk(answer.Target);   // 칸을 고르고 → [이동](걷기 전 확인의 두 단계)
                    // 마을 행동(전체 회복 · 공중날기)은 [마을] → 마을 창에서 누르고 창을 닫는다
                    if (answer.Kind == AnswerKind.VillageHeal || answer.Kind == AnswerKind.Fly)
                    {
                        // bird_keeper B: 마을 밖 칸이면 [날기] → 공중날기 칸만 있는 창(감사 G1-1)
                        if (!hud.Press(answer.Kind == AnswerKind.Fly && !flow.AtVillage(out _, out _) ? BoardHud.Fly : BoardHud.Village)) return false;
                        var village = flow.Stage.Current as VillageScreen;
                        if (village == null) return false;
                        bool pressed = answer.Kind == AnswerKind.VillageHeal ? village.PressHeal() : village.PickDest(answer.Target) && village.PickColor(answer.Arg) && village.PressFly();
                        flow.CloseVillage();
                        return pressed;
                    }
                    if (answer.Kind == AnswerKind.PlayFate) return PlayFate(flow, hud, answer);
                    // 공물(S31): [공물] → 사당 판에서 색 · 여행 점수 7 → [바치기](판은 남아 카드를 그린다)
                    if (answer.Kind == AnswerKind.Offer) return hud.Press(BoardHud.Offer) && flow.Stage.Current is ShrineScreen offering && offering.Press(answer);
                    // 트레이너 카드의 맵 능력 · 날씨 휠: [능력 이름]·[날씨 휠] → (칸) → (값 단추)
                    if (answer.Kind == AnswerKind.UseAbility) return PressAbility(hud, answer) || FarShrine(flow, answer);
                    // team_aqua B·team_magma B: [능력 이름] → 옮길 칸(턴 종료)
                    if (answer.Kind == AnswerKind.EndTurn && answer.Target >= 0) return (hud.AbilityPicking || hud.Press(BoardHud.Dive)) && hud.AbilityAt(answer.Target);
                    var acts = choice.Kind == ChoiceKind.MapAction ? BoardHud.MapActs : BoardHud.SpotActs;
                    var act = Array.Find(acts, a => a.Kind == answer.Kind);
                    return act != null && hud.Press(act.Id);
                }
                case ChoiceKind.EncounterPick:
                {
                    var screen = flow.Stage.Current as EncounterScreen;
                    if (screen == null) return false;
                    if (answer.Kind == AnswerKind.UseAbility) return screen.Retry();   // ace_trainer B 다시 찾아보기
                    for (int i = 0; i < screen.Cards.Count; i++)
                        if (screen.Cards[i].SameAs(answer)) return screen.Pick(i);
                    return false;
                }
                case ChoiceKind.Discard:
                {
                    // 6장째 버리기(S30): 저절로 열린 손패 창에서 그 카드를 고르고 [버리기]
                    var fate = flow.Top as FateScreen;
                    return fate != null && fate.DiscardCard(answer.Target);
                }
                case ChoiceKind.Respond:
                {
                    // S41 응답: 보드면 「행동」 칸 자리의 프롬프트, 배틀 화면이 떠 있으면 그 차례 칸
                    if (flow.Stage.Current is BattleScreen b) return b.Press(answer);
                    var hud = flow.Hud;
                    return hud != null && hud.Prompting && hud.PressPrompt(answer.Kind == AnswerKind.Accept);
                }
                case ChoiceKind.Ending:
                {
                    // 종막 선택(S62) · 배신자 순서(S63): 종막 창의 카드 [선택] · 좌석을 순서대로 누르고 [확정]
                    var ending = flow.Stage.Current as EndingScreen;
                    if (ending == null) return false;
                    if (choice.Sub != ChoiceSub.TraitorOrder) return ending.Pick(answer.Target);
                    ending.ResetOrder();
                    foreach (int seat in answer.Values)
                        if (!ending.PickSeat(seat)) return false;
                    return ending.ConfirmOrder();
                }
                default:
                {
                    // 보드에서 열린 트레이너 카드 물음: 「행동」 칸의 단추
                    if (flow.Hud != null && flow.Hud.CardAsking && !(flow.Stage.Current is BattleScreen))
                    {
                        var list = flow.Hud.CardAnswers;
                        for (int i = 0; i < list.Count; i++)
                            if (list[i].SameAs(answer)) return flow.Hud.PressCard(i);
                        return false;
                    }
                    // 사당 카드의 고르기 · 천상의 균열(S32·S33): 사당 판에서 고르고 [확정]
                    if (flow.Stage.Current is ShrineScreen shrine) return shrine.Press(answer);
                    var screen = flow.Stage.Current as BattleScreen;
                    return screen != null && screen.Press(answer);
                }
            }
        }

        /// <summary>맵 행동의 UseAbility: 능력 단추로 고르기를 연다(답이 하나면 바로 낸다) → 칸이 있으면 그 칸 → 값 단추. 염력 공물은 사당 판이 열린 채 false(<see cref="FarShrine"/>가 잇는다).</summary>
        static bool PressAbility(BoardHud hud, ChoiceAnswer answer)
        {
            if (!hud.AbilityPicking && !hud.Press(CardAsk.IsWeather(answer) ? BoardHud.Weather : BoardHud.Ability)) return false;
            if (!hud.AbilityPicking) return !ShrineScreen.IsReachOffer(answer, answer.Target);   // 하나뿐이라 바로 냈다(염력 공물이면 판이 열렸다)
            for (int i = 0; i < hud.AbilityShown.Count; i++)
                if (hud.AbilityShown[i].SameAs(answer)) return hud.PickAbility(i);
            return hud.AbilityAt(hud.CellFor(answer)) && (!hud.AbilityPicking ? !ShrineScreen.IsReachOffer(answer, answer.Target) : PressShown(hud, answer));
        }

        // 염력 공물(T33): 열린 떨어진 사당 판에서 안쪽 답(색 · 여행 점수 7)을 누른다
        static bool FarShrine(GameFlow flow, ChoiceAnswer answer) =>
            ShrineScreen.IsReachOffer(answer, answer.Target) && flow.Stage.Current is ShrineScreen far && far.Reach == answer.Target && far.Press(TrainerRules.ReachInner(answer));

        // 보이는 단추만 누른다: 답이 든 단추가 묶음(psychic B)이면 그 안으로 들어가 다시 찾는다
        static bool PressShown(BoardHud hud, ChoiceAnswer answer)
        {
            for (int k = 0; k < 4 && hud.AbilityPicking; k++)
            {
                int row = hud.AbilityRowOf(answer);
                if (row < 0) return false;
                for (int p = 0; !hud.AbilityRowShown(row) && p < hud.AbilityPages; p++) hud.NextAbilityPage();   // 그 단추가 보이는 쪽으로
                bool group = hud.AbilityRowIsGroup(row);
                if (!hud.PressAbilityRow(row)) return false;
                if (ShrineScreen.IsReachOffer(answer, answer.Target)) return false;   // 사당 판이 열렸다 — FarShrine이 잇는다
                if (!group) return true;
            }
            return false;
        }

        /// <summary>맵 행동의 운명 카드: [운명의 변화] → 손패 창에서 그 카드 → (3장이면 아르세우스 칩) → [n장 사용] → 펄기아면 보드에서 그 칸.</summary>
        static bool PlayFate(GameFlow flow, BoardHud hud, ChoiceAnswer answer)
        {
            if (!(flow.Top is FateScreen) && !flow.OpenFate()) return false;
            var fate = (FateScreen)flow.Top;
            var card = (FateCard)answer.Target;
            int at = -1;
            for (int i = 0; i < fate.Hand.Count; i++)
                if (FateRules.KindOf(flow.Ctx, fate.Hand[i]) == card) { at = i; break; }
            if (at < 0 || !fate.Pick(at)) return false;
            int wild = FateRules.WildsOf(answer);
            if (wild >= 0 && !fate.PickWild(Math.Max(0, fate.Splits(card).IndexOf(wild)))) return false;
            if (!fate.Use(answer.Arg)) return false;
            if (!hud.Warping) return true;
            return answer.Values.Count > 0 && hud.Warp(answer.Values[0]);
        }

        /// <summary>
        /// <paramref name="until"/>이 참이 될 때까지 걸음을 잇는다. 누를 것이 없을 때마다 <paramref name="pass"/>(ms)로 시간을 보낸다(세션 시각 + 연출 시각 — 부르는 쪽이 정한다).
        /// 걸음 수 상한에 닿으면 false.
        /// </summary>
        public static bool Run(GameFlow flow, SoloPilot pilot, Func<bool> until, Action<int> pass, int stepMs = 500, int max = 4000, bool confirm = true)
        {
            for (int i = 0; i < max; i++)
            {
                if (until()) return true;
                if (!Step(flow, pilot, confirm)) pass(stepMs);
            }
            return until();
        }
    }
}
