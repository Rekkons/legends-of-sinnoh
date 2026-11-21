using System.Collections.Generic;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 화면 도달 검사의 창 갈래(M9 실행 감사 ② 넓히기): 사당 판의 고르기 · 천상의 균열(추종자 · 동률) · 카드 보상 창 · 카드 바꾸기 · 진화 분기 · 옛 기술 · 종막 · 왜곡 균열 카드 ·
    /// 아르세우스의 축복(물음 · 부활 · 위 띠 동의) · 보드의 S41 응답 프롬프트 · 거래 수락 카드 고르기. 화면의 공개 상태와 요소 이름만 읽는다(누르지 않는다 — 창 안 고르기도 바꾸지 않는다).
    /// 「누를 수 있다」 = 그 답을 내는 요소가 지금 그려져 있고 켜져 있고(꺼짐 · 흐림 클래스 없음) 숨지 않았고 화면 밖이 아니다 — 쪽 넘김 뒤에만 보이는 줄은 「다른 쪽」으로 못 누름에 센다.
    /// 화면이 같은 꼴 답 가운데 첫 것만 내는 길(예: [수락] = 첫 Accept)이면 둘째부터는 못 누름이다.
    /// </summary>
    public static partial class AnswerReach
    {
        /// <summary>창이 받는 선택이면 r을 채우고 true(받을 창이 아직 없으면 NotReady). 이 갈래가 보지 않는 선택이면 false.</summary>
        static bool Screens(GameFlow flow, PendingChoice c, List<ChoiceAnswer> legal, Result r)
        {
            var client = flow.Client;
            if (CardRewardScreen.Owns(c))
            {
                r.Where = "카드 보상 창";
                if (!(flow.Stage.Current is CardRewardScreen reward) || !flow.BoardOpen) { r.NotReady = true; return true; }
                foreach (var a in legal)
                {
                    int i = Index(reward.Answers, a);
                    bool drawn = i >= 0 && (a.Kind == AnswerKind.Pick || (c.Sub == ChoiceSub.TeachMove && a.Kind == AnswerKind.Decline));
                    Need(flow, r, a, drawn ? "board-card-reward-" + (i + 1) : null, "카드 보상 창");
                }
                return true;
            }
            if (CardSwapScreen.Owns(flow.Ctx, c))
            {
                r.Where = "카드 바꾸기 창";
                if (!(flow.Stage.Current is CardSwapScreen swap) || !flow.BoardOpen) { r.NotReady = true; return true; }
                foreach (var a in legal)
                {
                    int i = Index(swap.Answers, a);
                    Need(flow, r, a, i < 0 ? null : a.Kind == AnswerKind.Pick ? "board-card-swap-card-" + (i + 1) : CardSwapScreen.Pass, "카드 바꾸기 창");
                }
                return true;
            }
            if (EvolveScreen.Owns(c))
            {
                r.Where = "진화 창";
                var want = c.Sub == ChoiceSub.EvolveTeach ? EvolveScreen.Mode.Teach : EvolveScreen.Mode.Branch;
                if (!(flow.Stage.Current is EvolveScreen evolve) || !flow.BoardOpen || evolve.Now != want) { r.NotReady = true; return true; }
                foreach (var a in legal)
                {
                    string name = null;
                    if (want == EvolveScreen.Mode.Branch)
                    {
                        int i = Index(evolve.Branches, a);
                        if (i >= 0) name = "board-evolve-branch-" + (i + 1);
                    }
                    // 옛 기술: 칸 단추는 그 칸의 첫 Pick · [가르치지 않기]는 첫 Decline을 낸다(EvolveScreen.PressTeach)
                    else if (a.Kind == AnswerKind.Decline && First(legal, x => x.Kind == AnswerKind.Decline, a)) name = "board-evolve-keep";
                    else if (a.Kind == AnswerKind.Pick && a.Arg >= 0 && a.Arg < PokemonState.MoveSlots && First(legal, x => x.Kind == AnswerKind.Pick && x.Arg == a.Arg, a)) name = "board-evolve-teach-" + a.Arg;
                    Need(flow, r, a, name, want == EvolveScreen.Mode.Branch ? "진화 갈래 카드" : "진화 창 옛 기술");
                }
                return true;
            }
            if (c.Kind == ChoiceKind.Ending)
            {
                r.Where = "종막 창";
                if (!(flow.Stage.Current is EndingScreen ending) || !flow.BoardOpen) { r.NotReady = true; return true; }
                Ending(flow, ending, c, legal, r);
                return true;
            }
            if (BreachScreen.Owns(c))
            {
                r.Where = "균열 카드 창";
                if (!(flow.Top is BreachScreen breach) || !flow.AnswerOpen || breach.Folded || !Same(BreachScreen.ChoiceOf(client), c)) { r.NotReady = true; return true; }
                Breach(flow, c, legal, r);
                return true;
            }
            if (BlessScreen.Owns(c))
            {
                r.Where = "축복 창";
                if (!(flow.Top is BlessScreen bless) || !flow.BoardOpen || bless.Folded || bless.IsPanel) { r.NotReady = true; return true; }
                foreach (var a in legal)
                {
                    string name = null;
                    if (c.Sub == ChoiceSub.UbSkip && (a.Kind == AnswerKind.Accept || a.Kind == AnswerKind.Decline) && First(legal, x => x.Kind == a.Kind, a))
                        name = a.Kind == AnswerKind.Accept ? "board-bless-yes" : "board-bless-no";
                    else if (c.Sub == ChoiceSub.Revive && a.Kind == AnswerKind.Pick && First(legal, x => x.Kind == AnswerKind.Pick && x.Target == a.Target, a))
                        name = "board-bless-revive-" + (a.Target + 1);
                    Need(flow, r, a, name, "축복 창");
                }
                return true;
            }
            if (GameFlow.ShrineWaits(c))
            {
                r.Where = "사당 판";
                if (!(flow.Stage.Current is ShrineScreen shrine) || !shrine.Open || shrine.Watched >= 0 || !Same(ShrineScreen.ChoiceOf(client), c)
                    || shrine.Shown == ShrineScreen.Mode.None || shrine.Shown == ShrineScreen.Mode.Offer || shrine.Shown == ShrineScreen.Mode.Fx) { r.NotReady = true; return true; }
                Shrine(flow, shrine, c, legal, r);
                return true;
            }
            if (c.Kind == ChoiceKind.Respond && !(flow.Stage.Current is BattleScreen))
            {
                if (c.Sub == ChoiceSub.Blessing)
                {
                    r.Where = "위 띠 축복 동의";
                    // 위 띠 동의 물음: [허용] = 첫 Accept · [막기] = 첫 Decline(BoardHud.PressBless)
                    if (flow.Hud == null || !flow.InputOpen || !flow.Hud.BlessAsking) { r.NotReady = true; return true; }
                    foreach (var a in legal)
                    {
                        bool yn = (a.Kind == AnswerKind.Accept || a.Kind == AnswerKind.Decline) && First(legal, x => x.Kind == a.Kind, a);
                        Need(flow, r, a, yn ? BoardHud.BlessAsk + (a.Kind == AnswerKind.Accept ? "-allow" : "-block") : null, "축복 동의 물음");
                    }
                    return true;
                }
                if (flow.Stage.Current is TradePickScreen)
                {
                    r.Where = "거래 카드 고르기";
                    if (!flow.AnswerOpen) { r.NotReady = true; return true; }
                    TradePick(flow, c, legal, r);
                    return true;
                }
                r.Where = "S41 프롬프트";
                // S41 프롬프트: [수락] = PromptYes(첫 Accept — 받아들이는 답이 여럿인 거래면 줄 카드 고르기 창) · [거절] = PromptNo
                var hud = flow.Hud;
                if (hud == null || !flow.InputOpen || !hud.Prompting) { r.NotReady = true; return true; }
                bool picks = c.Sub == ChoiceSub.Trade && legal.FindAll(x => x.Kind == AnswerKind.Accept).Count > 1;
                foreach (var a in legal)
                {
                    if (a.Kind == AnswerKind.Accept && picks)
                    {
                        string why = Blocked(flow, BoardHud.PromptAccept);
                        if (why != null) r.Missing.Add((a, "S41 [수락] " + why));
                        else if (!TradePickable(flow, c, legal, a)) r.Missing.Add((a, "거래 수락 카드 고르기 창이 그 답을 만들지 못함"));
                        continue;
                    }
                    string name = a.Kind == AnswerKind.Accept && a.SameAs(hud.PromptYes) ? BoardHud.PromptAccept : a.Kind == AnswerKind.Decline && a.SameAs(hud.PromptNo) ? BoardHud.PromptDecline : null;
                    Need(flow, r, a, name, "S41 프롬프트");
                }
                return true;
            }
            return false;
        }

        /// <summary>그 답을 내는 요소(name — null = 그런 요소가 없다)가 누를 수 있나. 못 누르면 Missing에 「창 이름 + 까닭」.</summary>
        static void Need(GameFlow flow, Result r, ChoiceAnswer a, string name, string where)
        {
            if (name == null) { r.Missing.Add((a, where + "에 그 답의 단추가 없음")); return; }
            string why = Blocked(flow, name);
            if (why != null) r.Missing.Add((a, where + " 단추 " + why));
        }

        /// <summary>
        /// 사람이 누를 수 있는 요소인가: 있고 · 켜져 있고 · 흐림 클래스(<see cref="UiStage.OffClass"/> · 사당 칸 off · 파티 칸 no)가 없고 · 자기나 조상이 숨지 않았고 ·
        /// (배치가 끝났으면) 무대 뿌리 사각형과 겹친다. 누를 수 있으면 null, 아니면 까닭(없음 · 꺼짐 · 흐림 · 숨김 · 화면 밖).
        /// </summary>
        static string Blocked(GameFlow flow, string name)
        {
            var root = flow.Stage.Root;
            var e = root != null ? root.Q(name) : null;
            if (e == null) return "없음";
            if (!e.enabledInHierarchy) return "꺼짐";
            if (e.ClassListContains(UiStage.OffClass) || e.ClassListContains("sn-shrine-chip-off") || e.ClassListContains("sn-party-no")) return "흐림";
            if (e.resolvedStyle.visibility == Visibility.Hidden) return "숨김";
            for (var p = e; p != null; p = p.parent)
                if (p.style.display == DisplayStyle.None || p.resolvedStyle.display == DisplayStyle.None) return "숨김";
            var box = e.worldBound;
            var screen = root.worldBound;
            if (Laid(box) && Laid(screen) && !screen.Overlaps(box)) return "화면 밖";
            return null;
        }

        static bool Laid(UnityEngine.Rect b) => !float.IsNaN(b.x) && !float.IsNaN(b.y) && b.width > 0f && b.height > 0f;

        static bool Same(PendingChoice x, PendingChoice y) => x != null && y != null && x.Id == y.Id;

        static int Index(IReadOnlyList<ChoiceAnswer> list, ChoiceAnswer a)
        {
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i].SameAs(a)) return i;
            return -1;
        }

        /// <summary>a가 legal에서 그 조건을 만족하는 첫 답인가(화면 단추가 첫 것만 내는 길).</summary>
        static bool First(List<ChoiceAnswer> legal, System.Predicate<ChoiceAnswer> match, ChoiceAnswer a)
        {
            var f = legal.Find(match);
            return f != null && f.SameAs(a);
        }

        // ── 종막(S62 · S63) ──

        static void Ending(GameFlow flow, EndingScreen ending, PendingChoice c, List<ChoiceAnswer> legal, Result r)
        {
            foreach (var a in legal)
            {
                if (c.Sub == ChoiceSub.EndingChoice)
                {
                    // 카드의 [선택](0 영웅 · 1 배신자)은 그 Target의 첫 Pick을 낸다(EndingScreen.Pick)
                    bool card = a.Kind == AnswerKind.Pick && (a.Target == 0 || a.Target == 1) && First(legal, x => x.Kind == AnswerKind.Pick && x.Target == a.Target, a);
                    Need(flow, r, a, card ? "ending-pick-" + a.Target : null, "종막 창");
                    continue;
                }
                if (c.Sub != ChoiceSub.TraitorOrder) { r.Missing.Add((a, $"종막 창이 그리지 않는 꼴({c.Sub})")); continue; }
                // 배신자 순서: 좌석 칩을 모두 한 번씩 누르고 [확정] = PickMany(누른 순서) — Options의 순열만 낼 수 있다
                if (!ending.Ordering) { r.Missing.Add((a, "종막 순서 고르기가 열리지 않음")); continue; }
                bool perm = a.Kind == AnswerKind.Pick && a.Target < 0 && a.Arg < 0 && a.Values.Count == c.Options.Count;
                for (int i = 0; perm && i < a.Values.Count; i++)
                    perm = c.Options.Contains(a.Values[i]) && a.Values.IndexOf(a.Values[i]) == i;
                if (!perm) { r.Missing.Add((a, "종막 순서 창은 상대 좌석 전부의 순서만 낸다")); continue; }
                foreach (int seat in a.Values)
                {
                    string why = Blocked(flow, "ending-order-seat-" + seat);
                    if (why != null) { r.Missing.Add((a, "종막 순서 좌석 칩 " + why)); break; }
                }
            }
        }

        // ── 왜곡 균열 카드(S58) ──

        static void Breach(GameFlow flow, PendingChoice c, List<ChoiceAnswer> legal, Result r)
        {
            var view = flow.Client.View;
            bool release = c.Subject == BreachRules.Release;
            var party = view.Seats[c.Seat].Party;
            bool Mine(int p) => p >= 0 && p < view.Pokemon.Count && System.Array.IndexOf(party, p) >= 0;
            bool Lit(int p, int slot)
            {
                var e = flow.Stage.Root.Q("board-breach-slot-" + p + "-" + slot);
                return e != null && e.ClassListContains("sn-breach-chip-lit");
            }
            int loose = 0;
            foreach (var a in legal)
            {
                if (a.Kind == AnswerKind.Decline)
                {
                    // 붙이기: [보관]은 첫 답이 Decline일 때만 선다 · 버리기·옮기기: [안 함]
                    Need(flow, r, a, release ? "board-breach-skip" : legal[0].SameAs(a) ? "board-breach-store" : null, "균열 창");
                    continue;
                }
                if (a.Kind != AnswerKind.Pick) { r.Missing.Add((a, "균열 창에 그 답의 단추가 없음")); continue; }
                if (release && a.Target == BreachRules.HandTarget)
                {
                    Need(flow, r, a, "board-breach-loose-" + (++loose), "균열 창 손·보관 카드");
                    continue;
                }
                bool slotOk = Mine(a.Target) && a.Arg >= 0 && a.Arg < PokemonState.MoveSlots;
                if (!release)
                {
                    if (!slotOk || a.Values.Count > 0) { r.Missing.Add((a, "균열 창(붙이기)에 그 답의 칸이 없음")); continue; }
                    if (!Lit(a.Target, a.Arg)) r.Missing.Add((a, "균열 창 붙일 칸이 켜지지 않음"));
                    continue;
                }
                // 버리기: 붙은 카드 칸을 고르고 [버리기] · 옮기기: 그 칸 → 다른 포켓몬의 균열 카드 없는 칸 → [옮기기]
                if (!slotOk || view.Pokemon[a.Target].Breach[a.Arg] < 0 || !Lit(a.Target, a.Arg)) { r.Missing.Add((a, "균열 창 붙은 카드 칸을 고를 수 없음")); continue; }
                if (a.Values.Count == 0) { Need(flow, r, a, "board-breach-discard", "균열 창 [버리기]"); continue; }
                bool to = a.Values.Count == 2 && Mine(a.Values[0]) && a.Values[1] >= 0 && a.Values[1] < PokemonState.MoveSlots && view.Pokemon[a.Values[0]].Breach[a.Values[1]] < 0;
                if (!to || flow.Stage.Root.Q("board-breach-slot-" + a.Values[0] + "-" + a.Values[1]) == null) r.Missing.Add((a, "균열 창 옮길 칸이 없음"));
            }
        }

        // ── 사당 판의 고르기 · 천상의 균열(S32 · S33) ──

        static readonly SinnohGod[] Gods = { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina };

        static void Shrine(GameFlow flow, ShrineScreen s, PendingChoice c, List<ChoiceAnswer> legal, Result r)
        {
            var mode = s.Shown;
            if (mode == ShrineScreen.Mode.Wait)
            {
                // 내 선택이 열렸는데 판이 「기다리는 중」으로 그렸다 — 고를 칸이 없다
                foreach (var a in legal) r.Missing.Add((a, $"사당 판이 그 선택을 그리지 않음({c.Kind}/{c.Sub} → 기다리는 중)"));
                return;
            }
            bool gods = mode == ShrineScreen.Mode.Follower || mode == ShrineScreen.Mode.Tie;
            List<ChoiceAnswer> units = null;
            foreach (var a in legal)
            {
                if (a.Kind == AnswerKind.Decline)
                {
                    // 추종자 · 동률 = 「고르지 않기」 칸, 그 밖 = [하지 않기](첫 Decline — ShrineScreen.PressDecline)
                    Need(flow, r, a, gods ? "board-shrine-nogod" : First(legal, x => x.Kind == AnswerKind.Decline, a) ? "board-shrine-skip" : null, "사당 판");
                    continue;
                }
                if (mode == ShrineScreen.Mode.Apricorns || mode == ShrineScreen.Mode.Stews)
                {
                    // 묶음 고르기: 칩을 Count번 눌러 모은 것(정렬)과 값이 같은 첫 답을 낸다(ShrineScreen.Answer) — 규토리는 같은 색 여럿, 스튜는 서로 다른 것만
                    if (units == null)
                    {
                        units = new List<ChoiceAnswer>();
                        foreach (var x in legal) if (x.Kind != AnswerKind.Decline) units.Add(x);
                    }
                    if (!Bundle(c, mode == ShrineScreen.Mode.Stews, units, a)) r.Missing.Add((a, mode == ShrineScreen.Mode.Stews ? "사당 판 스튜 칩으로 만들 수 없는 묶음" : "사당 판 규토리 칩으로 만들 수 없는 묶음"));
                    continue;
                }
                int i = Index(s.Options, a);
                if (i < 0) { r.Missing.Add((a, $"사당 판 목록에 없음({mode})")); continue; }
                switch (mode)
                {
                    case ShrineScreen.Mode.Cards:
                        Need(flow, r, a, "board-shrine-card-" + (i + 1), "사당 판 카드");
                        break;
                    case ShrineScreen.Mode.Exchange:
                    case ShrineScreen.Mode.ExchangeCard:
                    case ShrineScreen.Mode.Move:
                    {
                        string why = Blocked(flow, "board-shrine-opt-" + (i + 1));
                        if (why == null) break;
                        int at = IndexOf(s.ShownOptions, i);
                        r.Missing.Add((a, why == "없음" && at >= 0 ? $"사당 판 목록의 다른 쪽(쪽 넘김 — 지금 {s.Page + 1}/{s.Pages}쪽)" : why == "없음" ? "사당 판 목록에서 걸러져 안 보임" : "사당 판 목록 칸 " + why));
                        break;
                    }
                    case ShrineScreen.Mode.Teach:
                    {
                        // 포켓몬 칩 → 그 포켓몬의 칸 칩. 칩 순서 = 답에 처음 나온 포켓몬 순(ShrineScreen.PaintTeach)
                        var seen = new List<int>();
                        foreach (var o in s.Options) if (!seen.Contains(o.Target)) seen.Add(o.Target);
                        if (seen.Count > 1) Need(flow, r, a, "board-shrine-mon-" + (seen.IndexOf(a.Target) + 1), "사당 판 가르칠 포켓몬");
                        else Need(flow, r, a, "board-shrine-opt-" + (i + 1), "사당 판 기술 칸");   // 포켓몬이 하나면 칸이 곧바로 선다
                        break;
                    }
                    case ShrineScreen.Mode.Share:
                        Need(flow, r, a, "board-shrine-seat-" + (a.Target + 1), "사당 판 나눌 좌석");
                        break;
                    case ShrineScreen.Mode.Follower:
                    case ShrineScreen.Mode.Tie:
                    {
                        bool god = a.Kind == AnswerKind.Pick && System.Array.IndexOf(Gods, (SinnohGod)a.Target) >= 0 && First(legal, x => x.Kind == AnswerKind.Pick && x.Target == a.Target, a);
                        Need(flow, r, a, god ? "board-shrine-god-" + ((SinnohGod)a.Target).ToString().ToLowerInvariant() : null, "사당 판 신 칸");
                        break;
                    }
                    default:
                        r.Missing.Add((a, $"사당 판이 그 꼴을 그리지 않음({mode})"));
                        break;
                }
            }
        }

        static int IndexOf(IReadOnlyList<int> list, int v)
        {
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == v) return i;
            return -1;
        }

        /// <summary>규토리 · 스튜 묶음: 칩으로 모을 수 있는 값이고(규토리 = 일곱 색 · 스튜 = 답들에 나온 아이템, 서로 다름) Count개이며, 모은 것의 정렬과 같은 첫 답이 a인가.</summary>
        static bool Bundle(PendingChoice c, bool stews, List<ChoiceAnswer> units, ChoiceAnswer a)
        {
            if (a.Values.Count != c.Count) return false;
            var items = new List<int>();
            foreach (var x in units) foreach (int v in x.Values) if (!items.Contains(v)) items.Add(v);
            for (int i = 0; i < a.Values.Count; i++)
            {
                int v = a.Values[i];
                if (stews ? !items.Contains(v) || a.Values.IndexOf(v) != i : v < 0 || v >= SeatState.ApricornColors) return false;
            }
            var want = new List<int>(a.Values);
            want.Sort();
            var f = units.Find(x => SameList(x.Values, want));
            return f != null && f.SameAs(a);
        }

        static bool SameList(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ── 거래 수락에 줄 운명 카드 고르기(TradePickScreen) ──

        static void TradePick(GameFlow flow, PendingChoice c, List<ChoiceAnswer> legal, Result r)
        {
            foreach (var a in legal)
            {
                if (a.Kind == AnswerKind.Decline) { Need(flow, r, a, First(legal, x => x.Kind == AnswerKind.Decline, a) ? "board-trade-pick-decline" : null, "거래 카드 고르기 창"); continue; }
                if (a.Kind != AnswerKind.Accept || !TradePickable(flow, c, legal, a)) r.Missing.Add((a, "거래 수락 카드 고르기 창이 그 답을 만들지 못함"));
            }
        }

        /// <summary>손패에서 Asked장을 골라(정렬) 같은 값의 첫 Accept를 내는 창이 a를 낼 수 있나 — 장수 · 손패에 있는 카드 · 첫 답.</summary>
        static bool TradePickable(GameFlow flow, PendingChoice c, List<ChoiceAnswer> legal, ChoiceAnswer a)
        {
            int ask = TradePickScreen.Asked(flow, c);
            if (a.Values.Count != ask || ask == 0) return false;
            var hand = new List<int>(flow.Client.View.Seats[flow.Client.Seat].FateHand);
            foreach (int v in a.Values)
                if (!hand.Remove(v)) return false;
            var want = new List<int>(a.Values);
            want.Sort();
            return First(legal, x => x.Kind == AnswerKind.Accept && SameList(x.Values, want), a);
        }
    }
}
