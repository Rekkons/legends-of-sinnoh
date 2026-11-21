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
    /// 보드 HUD(04 §3 B안 — 목업 r3 board, 결정 16): 화면 아래에 늘 떠 있는 판(<see cref="UiStage.Hud"/>)이고 그 위에 창(게임 메뉴 등)이 열린다.
    /// <list type="bullet">
    /// <item>위 띠: S17 좌석 띠(좌석마다 칩 — 얼굴 상자(<see cref="FaceBox"/>: 주인공 얼굴 + 좌석 색 테두리 + 번호 배지)·이름, 턴인 좌석은 강조) + 지금 누구의 턴인지 · 라운드 · 막 · [메뉴](S22).</item>
    /// <item>아래 판: S18 내 파티 요약(도트 · 이름 · 체력 · 스피드) + S16·S20 행동(남은 이동력 · 이번 턴에 쓴 것 · 안내 한 줄 · 단추).</item>
    /// <item>S19 이동 표시: 내 턴에 갈 수 있는 칸을 보드에 칠하고(<see cref="BoardMarks"/>), 마우스를 올린 칸까지 경로를 보이고, 누르면 그 칸으로 걷는다.</item>
    /// <item>턴 넘김(요약 → 넘김 띠 → 내 턴 컷신)은 <see cref="TurnBand"/>가, 막 카드(S14)는 <see cref="ActCard"/>가 화면 대기열에 맞춰 그린다.</item>
    /// <item>왼쪽 위 기록 판(S21 — 최근 줄 셋 + [펼치기] → <see cref="LogScreen"/>), 아래 판의 칸 정보 줄, 좌석 칩·보드 위 말·토큰의 공개 정보 카드(<see cref="InfoCard"/>).</item>
    /// <item>아래 판의 [정비](단축키 T) → 정비 판(<see cref="PartyScreen"/> — S18·S28·S29). 좌석 칩을 누르면 그 좌석의 판(남 좌석은 읽기만 — S39).</item>
    /// <item>아래 판의 [트레이너 카드] → 트레이너 카드 판(<see cref="TrainerScreen"/> — S40). 트레이너 카드를 끈 판에서는 단추가 없다.</item>
    /// </list>
    /// 규칙을 다시 세지 않는다: 단추·갈 수 있는 칸은 엔진의 합법 답(<c>Client.Legal</c>) 그대로이고, 누르면 그 답을 낸다(<see cref="GameFlow.Answer"/>).
    /// 마을 행동(전체 회복 · 공중날기)은 단추 [마을] 하나로 모아 마을 창(<see cref="VillageScreen"/> — S23)에서 낸다. 받을 화면이 없는 행동의 단추는 회색 + 「아직 준비 중입니다.」다(지금은 없다).
    /// 받을 화면이 없는 종류의 선택이 열리면 안내 줄에 준비 중이라고만 적고 답하지 않는다(대신 답해 넘기지 않는다, <see cref="GameFlow.NotReadyShown"/>).
    /// 조우 고르기는 조우 창(<see cref="EncounterScreen"/>), 배틀의 선택은 배틀 화면(<see cref="BattleScreen"/>)이 HUD 위에서 받는다.
    /// 연출이 도는 동안(<see cref="GameFlow.InputOpen"/> = false)에는 단추가 꺼지고 이동 표시가 걷힌다(04 §8-1 P4).
    /// 요소 이름: <c>board-hud</c>·<c>board-hud-seat-&lt;n&gt;</c>·<c>board-hud-turn</c>·<c>board-hud-round</c>·<c>board-hud-time</c>·<c>board-hud-act</c>·<c>board-hud-menu</c>·
    /// <c>board-hud-log</c>·<c>board-hud-log-open</c>·<c>board-hud-log-lines</c>·<c>board-hud-cell</c>·
    /// <c>board-hud-dock</c>·<c>board-hud-party</c>·<c>board-hud-maint</c>·<c>board-hud-trainer</c>·<c>board-hud-mon-&lt;i&gt;</c>·<c>board-hud-actions</c>·<c>board-hud-moves</c>·<c>board-hud-used</c>·<c>board-hud-guide</c>·<c>board-hud-act-&lt;id&gt;</c>·
    /// <c>board-hud-res</c>(내 자원 줄 — 결정 98: <c>-apricorn-&lt;색&gt;</c>·<c>-jp</c>·<c>-ball-&lt;n&gt;</c>).
    /// </summary>
    public sealed class BoardHud : IUiScreen
    {
        /// <summary>단추 하나의 모양: 엔진 답 종류 → 이름(문자열 표 <c>screen.board.action.&lt;Id&gt;</c>) · 이 묶음에서 누를 수 있나 · 단추 모양.</summary>
        public sealed class Act
        {
            public readonly string Id;
            public readonly AnswerKind Kind;
            /// <summary>false = 그 뒤를 받을 화면이 아직 없다(회색 + 「아직 준비 중입니다.」).</summary>
            public readonly bool Ready;
            public readonly KitButtonKind Look;
            /// <summary>true = 답을 내지 않고 창을 여는 단추([마을] → 마을 창 S23 · [제작] → 제작 창 S26 · [진화] → 진화 창 S27). [마을]은 합법 답이 아니라 서 있는 칸으로 올린다 — 이번 턴에 다 써도 까닭을 창에서 읽는다. [제작]·[진화]는 그 답이 있을 때만.</summary>
            public readonly bool Window;

            public Act(string id, AnswerKind kind, bool ready, KitButtonKind look = KitButtonKind.Normal, bool window = false)
            {
                Id = id;
                Kind = kind;
                Ready = ready;
                Look = look;
                Window = window;
            }
        }

        public const string Search = "search", TakeApricorns = "take_apricorns", Village = "village", Fly = "fly", Challenge = "challenge", Offer = "offer", EndTurn = "end_turn",
            Reveal = "reveal", Pass = "pass", Craft = "craft", Evolve = "evolve";

        /// <summary>
        /// 맵 행동의 단추(왼쪽 → 오른쪽). 이동은 단추가 아니라 칸 누르기다. 마을 행동(전체 회복 · 공중날기)은 [마을]이 여는 마을 창에서 낸다(04 §3-4).
        /// 은행상회는 마을 창, 제작·진화는 그 창이 답을 낸다(M5 「제작·은행·진화 화면」). 여기 없는 답 종류(사당 등 — M5 이후)는 단추를 만들지 않는다.
        /// </summary>
        public static readonly Act[] MapActs =
        {
            new Act(Search, AnswerKind.Search, true),
            new Act(TakeApricorns, AnswerKind.TakeApricorns, true),
            new Act(Village, AnswerKind.VillageHeal, true, window: true),
            new Act(Fly, AnswerKind.Fly, true, window: true),         // bird_keeper B 새 휘슬(rulings A36): 마을 밖 칸에서 날기 답이 있을 때 — 마을 창의 공중날기 칸만 연다(감사 G1-1)
            new Act(Craft, AnswerKind.Craft, true, window: true),     // 제작 창(S26 — M5 「제작·은행·진화 화면」): 제작 답이 있을 때
            new Act(Evolve, AnswerKind.Evolve, true, window: true),   // 진화 창(S27): 진화 답이 있을 때
            new Act(Challenge, AnswerKind.Challenge, true),
            new Act(Offer, AnswerKind.Offer, true, window: true),   // 사당 판(S31 — M5 「사당·균열 화면」): 공물 답이 있을 때. 3막 시작의 사당의 심장 회복도 같은 판 · 같은 답
            new Act(EndTurn, AnswerKind.EndTurn, true, KitButtonKind.End),
        };

        /// <summary>숨겨진 장소에 멈췄을 때의 단추(S34 묻기 — 결과 화면은 M5).</summary>
        public static readonly Act[] SpotActs =
        {
            new Act(Reveal, AnswerKind.Accept, true, KitButtonKind.Main),
            new Act(Pass, AnswerKind.Decline, true),
        };

        /// <summary>[운명의 변화] 단추 · 순간이동 [취소] · 응답 프롬프트의 요소 이름.</summary>
        /// <summary>「행동」 머리 줄 오른쪽 끝의 [스탬프 n/7](S37) · 행동 단추 줄의 [거래](S36 — 맵 행동의 합법 답에 늘어놓지 않아 <see cref="MapActs"/> 밖이다: <see cref="TradeRules.Partners"/>로 켠다, 남의 턴에도).</summary>
        public const string StampButton = "board-hud-stamp", Trade = "trade";

        /// <summary>「행동」 머리 줄의 [무르기](결정 149 — [스탬프] 바로 왼쪽, 무르기를 켠 판에만). <see cref="AfterUndoClass"/> = 그 오른쪽 이웃(스탬프 · 오른쪽 클릭 안내)이 붙어 서는 클래스.</summary>
        public const string UndoButton = "board-hud-undo", AfterUndoClass = "sn-hud-after-undo";

        Button undoButton;
        /// <summary>공개된 추종자의 신 색 바탕(결정 80 L13 나 — 좌석 칩의 이름 · 사당 판 좌석 줄의 배지): 이 클래스 + 「-dialga」·「-palkia」·「-giratina」(USS).</summary>
        public const string FollowClass = "sn-hud-follow";
        /// <summary>좌석이 이 수 이상이면 좌석 칩의 큰 신 아이콘을 빼고 이름의 색 바탕만 남긴다(사당 판이 다섯 명부터 얼굴 대신 색 점을 쓰는 것과 같은 금).</summary>
        public const int FollowIconSeats = 5;
        /// <summary>좌석이 <see cref="FollowIconSeats"/> 이상인 위 띠(<c>board-hud-top</c>)의 클래스 — 1080 벌은 턴 배지를 숨긴다(결정 112 Q15, USS).</summary>
        public const string ManyClass = "sn-hud-many";
        /// <summary>좌석이 이 수 이상이면 위 띠(<c>board-hud-top</c>)에 <see cref="ToolsDownClass"/> — 1080 벌은 보기 단추 묶음(<c>board-hud-tools</c>)을 상태 판 둘째 줄로 내린다(칩 이름 한글 6자 자리 — 결정 112 Q15, USS [가정 plan §5 #607]).</summary>
        public const int ToolsDownSeats = 4;
        public const string ToolsDownClass = "sn-hud-tools-down";

        /// <summary>그 좌석 칩의 이름이 신 색 바탕으로 칠해져 있나(없으면 None) — 확인용.</summary>
        public SinnohGod FollowTone(int seat)
        {
            var chip = seats != null ? seats.Q("board-hud-seat-" + (seat + 1).ToString(CultureInfo.InvariantCulture)) : null;
            var name = chip != null ? chip.Q(className: "sn-hud-seat-name") : null;
            if (name == null || !name.ClassListContains(FollowClass)) return SinnohGod.None;
            foreach (var god in new[] { SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina, SinnohGod.Arceus })
                if (name.ClassListContains(FollowClass + "-" + god.ToString().ToLowerInvariant())) return god;
            return SinnohGod.None;
        }
        Button stampButton;
        public const string FateButton = "board-hud-fate", WarpCancel = "board-hud-warp-cancel", PromptAccept = "board-hud-prompt-accept", PromptDecline = "board-hud-prompt-decline";

        readonly List<ChoiceAnswer> warps = new List<ChoiceAnswer>();

        // ── 트레이너 카드(M6 「트레이너 카드 화면」): 맵 행동의 UseAbility 답 — 단추 [능력 이름]·[날씨 휠] → 고르기(값 단추 · 칸 누르기) · 보드에서 열린 카드 물음 ──

        /// <summary>행동 단추 줄의 능력 단추 이름(<c>board-hud-act-</c> 뒤): 그 좌석 카드의 파랑 능력 · 날씨 휠(누구나 — 사당에서 JP 3).</summary>
        public const string Ability = "ability", Weather = "weather", Dive = "dive";   // dive = team_aqua B·team_magma B 「턴을 끝내며 다른 바다·산 칸으로」(EndTurn 답의 Target)
        /// <summary>고르는 동안의 [취소] · 카드 물음·고르기의 머리 「카드 — 능력」.</summary>
        public const string AbilityCancel = "board-hud-ability-cancel", AbilityBack = "board-hud-ability-back", AbilityNext = "board-hud-ability-next", CardHead = "board-hud-card-head";

        readonly List<ChoiceAnswer> abilityPicks = new List<ChoiceAnswer>();   // 고르는 중인 능력 답(비면 고르지 않는다)
        readonly List<ChoiceAnswer> abilityShown = new List<ChoiceAnswer>();   // 지금 단추로 보이는 것(칸을 고른 뒤면 그 칸의 답)
        readonly List<ChoiceAnswer> cardAnswers = new List<ChoiceAnswer>();
        int abilityCell = -1, abilityChoice = -1;
        bool abilityWeather;
        // psychic B의 값 단추 묶음(M6 화면 교차 검토 #1): 들어온 묶음 키(CardAsk.ReachGroup — 0 = 종류 · 1 = 공중날기 목적지) · 지금 단추 줄(하나 = 바로 답, 여럿 = 묶음) · 단추 이름
        readonly List<int> reachPath = new List<int>();
        readonly List<List<ChoiceAnswer>> abilityRows = new List<List<ChoiceAnswer>>();
        readonly List<string> abilityLabels = new List<string>();

        /// <summary>능력 답을 고르는 중인가(<see cref="BeginAbility"/>) · 지금 단추로 보이는 답 · 고른 칸(−1 = 아직).</summary>
        public bool AbilityPicking => abilityPicks.Count > 0;
        public IReadOnlyList<ChoiceAnswer> AbilityShown => abilityShown;
        public int AbilityCell => abilityCell;

        /// <summary>고르기의 단추 줄(묶음 포함 — 화면 순서)의 이름 · 단추 수 · 몇 단계 들어왔나(psychic B 묶음).</summary>
        public IReadOnlyList<string> AbilityLabels => abilityLabels;
        public int AbilityRows => abilityRows.Count;
        public int AbilityDepth => reachPath.Count;

        /// <summary>그 답이 든 단추(묶음이면 그 묶음 — 없으면 −1) · 그 단추가 묶음인가.</summary>
        public int AbilityRowOf(ChoiceAnswer a) => abilityRows.FindIndex(r => r.Exists(x => x.SameAs(a)));
        public bool AbilityRowIsGroup(int row) => row >= 0 && row < abilityRows.Count && abilityRows[row].Count > 1;

        /// <summary>고르기의 단추 하나를 눌렀다: 답 하나면 그 답을 내고, 묶음이면 그 안의 단추로 바꾼다(한 단계 물리기 = <see cref="CancelAbility"/>(false)).</summary>
        public bool PressAbilityRow(int row)
        {
            if (!flow.InputOpen || !AbilityRowShown(row)) return false;   // 지금 쪽에 보이는 단추만
            var r = abilityRows[row];
            if (ReachOffer(r[0])) return ReachShrine(r[0].Target);   // 염력 공물: 그 칸에 선 것과 같은 사당 판(T33)
            if (r.Count == 1) return AnswerAbility(r[0]);
            reachPath.Add(CardAsk.ReachGroup(r[0], reachPath.Count));
            abilityPage = 0;
            stage.Tips.Hide();
            Refresh();
            return true;
        }

        /// <summary>
        /// 값 단추는 쪽 넘김 없이 두 줄로 싣는다(결정 87 V2 나 — 고르는 동안 아래 판이 한 줄 높아진다 <see cref="TallClass"/>). 셈은 글자 수 어림:
        /// 단추 하나 = 이름 글자 수 + <see cref="AbilityPadChars"/>(그림이 있으면 한 번 더), 한 줄에 드는 수 <see cref="AbilityLineChars"/>(1080 벌 「행동」 칸 —
        /// 「찾아보기 · 도전 ○○ · 거래 ○○」 16자 + [취소]가 한 줄을 넘쳤다). 두 줄에도 못 드는 것(초능력자 공물 색 열넷 등)만 두 줄씩 쪽으로 남는다([가정] §5 #166).
        /// </summary>
        public const int AbilityLineChars = 16, AbilityPadChars = 2, AbilityTwoLines = 44, AbilityPagedLines = 36;
        /// <summary>고르는 동안 한 줄 높아진 아래 판의 클래스.</summary>
        public const string TallClass = "sn-hud-dock-tall";

        /// <summary>단추 줄이 실제 배치에서 한 줄을 넘었나(둘째 줄로 내려간 단추가 있다).</summary>
        public static bool Wraps(VisualElement row)
        {
            float top = float.NaN;
            foreach (var child in row.Children())
            {
                if (child.resolvedStyle.display == DisplayStyle.None || float.IsNaN(child.layout.y)) continue;
                if (float.IsNaN(top)) top = child.layout.y;
                else if (child.layout.y > top + 1f) return true;
            }
            return false;
        }
        readonly List<int> abilityPageStart = new List<int>();   // 쪽마다 첫 단추의 색인(쪽이 하나면 비어 있거나 0 하나)
        bool abilityTall;
        bool AbilityPaged => abilityPageStart.Count > 1;
        public int AbilityPage => abilityPage;
        public int AbilityPages => Math.Max(1, abilityPageStart.Count);
        int abilityPage;
        int AbilityFrom => AbilityPaged ? abilityPageStart[abilityPage] : 0;
        int AbilityTo => AbilityPaged && abilityPage + 1 < abilityPageStart.Count ? abilityPageStart[abilityPage + 1] : abilityRows.Count;

        /// <summary>그 단추가 지금 쪽에 보이나.</summary>
        public bool AbilityRowShown(int row) => row >= 0 && row < abilityRows.Count && row >= AbilityFrom && row < AbilityTo;

        /// <summary>[다음 n/m]: 단추 줄의 다음 쪽(끝이면 처음으로).</summary>
        public bool NextAbilityPage()
        {
            if (!AbilityPaged) return false;
            abilityPage = (abilityPage + 1) % AbilityPages;
            stage.Tips.Hide();
            Refresh();
            return true;
        }

        /// <summary>
        /// 고른 길이 뒷면 숨겨진 장소에서 멈출 때 안내 줄의 마우스 오버(T15 — 규칙은 그대로, rulings A17: 공개하면 우두머리와 바로 배틀 · 자극적인 스튜는 공개 전에): 이번 턴 이미 먹었으면 그 말,
        /// 지금 자극적인 스튜를 만들 수 있으면(맵 행동의 제작 답) [제작]으로 먼저 만드는 길을 한 줄 더한다.
        /// </summary>
        string AlphaWarnTip(PendingChoice choice)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            int seat = flow.Client != null ? flow.Client.Seat : -1;
            string tip = stage.T("screen.board.alpha_warn.tip");
            if (view == null || seat < 0 || choice == null) return tip;
            if (CraftRules.AlphaWarded(view, flow.Ctx, seat)) return tip + "\n" + stage.T("screen.board.alpha_warn.warded");
            int zesty = flow.Ctx.Items.Ids.IndexOf(CraftRules.ZestyStew);
            if (zesty >= 0 && flow.Client.Legal(choice).Exists(a => a.Kind == AnswerKind.Craft && a.Target == zesty)) return tip + "\n" + stage.T("screen.board.alpha_warn.stew");
            return tip;
        }

        // 칸의 답을 단추 줄로 나눈다: psychic B는 같은 종류(공물 · 은행상회 · 공중날기)를 한 단추로 묶고 누르면 그 안으로(공중날기는 목적지 → 색), 다른 카드는 답마다 단추 하나
        void SplitRows()
        {
            abilityRows.Clear();
            bool reaching = MyCardKey == "psychic" && !abilityWeather && abilityCell >= 0;
            if (!reaching) reachPath.Clear();
            var level = new List<ChoiceAnswer>(abilityShown);
            for (int l = 0; l < reachPath.Count; l++)
            {
                int d = l, key = reachPath[l];
                level.RemoveAll(a => CardAsk.ReachGroup(a, d) != key);
            }
            if (level.Count == 0 && reachPath.Count > 0)
            {
                reachPath.Clear();   // 묶음의 답이 사라졌다 — 처음 단계로
                level.AddRange(abilityShown);
            }
            int depth = reachPath.Count;
            foreach (var a in level)
            {
                if (flow.TradeLocked && CardAsk.IsTradeMark(a)) continue;   // 거래를 잠근 판(결정 124)은 염력 거래 단추를 세우지 않는다
                int key = reaching ? CardAsk.ReachGroup(a, depth) : -1;
                var row = key >= 0 ? abilityRows.Find(r => CardAsk.ReachGroup(r[0], depth) == key) : null;
                if (row == null) abilityRows.Add(row = new List<ChoiceAnswer>());
                row.Add(a);
            }
            // 두 줄에 다 들면 쪽 없이, 아니면 두 줄씩 쪽([다음] 자리를 남긴다)
            var cost = new List<int>();
            int chars = 0, total = 0;
            foreach (var r in abilityRows)
            {
                int len = RowLabel(r, reaching, depth).Length;
                chars += len;
                cost.Add(len + AbilityPadChars + (r.Count == 1 && CardAsk.MapIconKey(MyCardKey, r[0]) != null ? AbilityPadChars : 0));
                total += cost[cost.Count - 1];
            }
            abilityPageStart.Clear();
            abilityTall = abilityRows.Count > 3 || (abilityRows.Count > 2 && chars > AbilityLineChars);   // 한 줄을 넘는다(옛 쪽 넘김 조건)
            if (total > AbilityTwoLines)
            {
                int sum = 0;
                for (int i = 0; i < abilityRows.Count; i++)
                {
                    if (i == 0 || sum + cost[i] > AbilityPagedLines) { abilityPageStart.Add(i); sum = 0; }
                    sum += cost[i];
                }
            }
            if (abilityPage >= AbilityPages) abilityPage = 0;
            abilityShown.Clear();
            for (int i = 0; i < abilityRows.Count; i++)
                if (abilityRows[i].Count == 1 && AbilityRowShown(i)) abilityShown.Add(abilityRows[i][0]);   // 보이는 답만(길잡이 · PickAbility)
        }

        // 고르기 단추 하나의 이름(번호를 붙이기 전): psychic B는 안쪽 답의 값(묶음이면 묶음 이름), 다른 카드는 값 이름
        string RowLabel(List<ChoiceAnswer> row, bool reaching, int depth)
        {
            var a = row[0];
            return !reaching ? CardAsk.MapLabel(stage, flow.Ctx, MyCardKey, a) : row.Count == 1 ? CardAsk.ReachLabel(stage, flow.Ctx, a, depth, flow.SeatName) : CardAsk.ReachGroupLabel(stage, flow.Ctx, a, depth);
        }

        /// <summary>보드에서 카드 물음(출처 = 카드인 Reaction/EffectOption·EffectPick — 배틀 화면 밖)을 받는 중인가 · 그 단추의 답(엔진 순서).</summary>
        public bool CardAsking { get; private set; }

        /// <summary>보드의 카드 물음이 칸을 고른다(team_rocket R — 칠한 칸 = <see cref="Reach"/>, 누르면 <see cref="CardAt"/>).</summary>
        public bool CardCells { get; private set; }
        public IReadOnlyList<ChoiceAnswer> CardAnswers => cardAnswers;

        string MyCardKey => flow.Ctx != null && flow.Client != null ? CardAsk.CardKey(flow.Ctx, CardAsk.CardOf(flow.Client.View, flow.Client.Seat)) : "";

        int AbilityCellOf(ChoiceAnswer a) => a.Kind == AnswerKind.EndTurn ? a.Target : CardAsk.CellOf(flow.Ctx, flow.Client.View, MyCardKey, a);

        /// <summary>능력 답이 가리키는 보드 칸(없으면 −1 — 확인용).</summary>
        public int CellFor(ChoiceAnswer a) => AbilityCellOf(a);

        /// <summary>
        /// 행동 단추 [능력 이름]·[날씨 휠]: 그 무리의 답이 하나뿐이고 칸을 고르지 않으면 바로 내고, 아니면 고르기를 연다 — 값 단추(규토리 색 · 날씨 · psychic의 상호작용)나
        /// 보드의 칸(kindler 나무 · ruin_maniac 금색 ? · psychic 닿는 칸 · picnicker 야영지). 열었거나 냈으면 true.
        /// </summary>
        public bool BeginAbility(bool weather)
        {
            var choice = flow.Client != null ? flow.Client.Choice(ChoiceKind.MapAction) : null;
            if (!flow.InputOpen || choice == null) return false;
            var group = flow.Client.Legal(choice).FindAll(a => a.Kind == AnswerKind.UseAbility && CardAsk.IsWeather(a) == weather);
            return BeginPick(choice, group, weather);
        }

        /// <summary>[능력 이름](team_aqua B·team_magma B): 턴을 끝내며 옮길 칸을 칠한 고르기를 연다(EndTurn 답의 Target — rulings A36). 열었으면 true.</summary>
        public bool BeginDive()
        {
            var choice = flow.Client != null ? flow.Client.Choice(ChoiceKind.MapAction) : null;
            if (!flow.InputOpen || choice == null) return false;
            return BeginPick(choice, flow.Client.Legal(choice).FindAll(IsDive), false);
        }

        static bool IsDive(ChoiceAnswer a) => a.Kind == AnswerKind.EndTurn && a.Target >= 0;

        bool BeginPick(PendingChoice choice, List<ChoiceAnswer> group, bool weather)
        {
            if (group.Count == 0) return false;
            stage.Tips.Hide();
            if (group.Count == 1 && AbilityCellOf(group[0]) < 0) return flow.Answer(choice, group[0]).Accepted;
            abilityPicks.Clear();
            abilityPicks.AddRange(group);
            abilityWeather = weather;
            abilityCell = -1;
            reachPath.Clear();
            abilityPage = 0;
            abilityChoice = choice.Id;
            picked = -1;
            vias.Clear();
            Refresh();
            return true;
        }

        /// <summary>고르기를 그만둔다(칸을 골랐으면 칸만 푼다 — 한 단계 물리기). 고르는 중이었으면 true.</summary>
        public bool CancelAbility(bool all = true)
        {
            if (abilityPicks.Count == 0) return false;
            abilityPage = 0;
            if (!all && reachPath.Count > 0) reachPath.RemoveAt(reachPath.Count - 1);   // psychic 묶음에서 한 단계 나온다
            else if (!all && abilityCell >= 0) abilityCell = -1;
            else
            {
                abilityPicks.Clear();
                reachPath.Clear();
            }
            hover = -1;
            Refresh();
            return true;
        }

        /// <summary>고르는 중에 보드 칸을 눌렀다: 그 칸의 답이 하나면 내고, 여럿이면 그 칸의 답을 단추로 보인다. 그 칸의 답이 없으면 false.</summary>
        public bool AbilityAt(int cell)
        {
            if (!flow.InputOpen || abilityPicks.Count == 0) return false;
            var here = abilityPicks.FindAll(a => AbilityCellOf(a) == cell && cell >= 0);
            if (here.Count == 0) return false;
            if (here.TrueForAll(ReachOffer)) return ReachShrine(cell);   // 그 칸의 일이 공물뿐: 바로 사당 판
            if (here.Count == 1) return AnswerAbility(here[0]);
            abilityCell = cell;
            reachPath.Clear();
            abilityPage = 0;
            Refresh();
            return true;
        }

        // psychic B로 떨어진 사당에 바치는 답 — 값 단추 대신 그 칸의 사당 판(색 · 여행 점수 7 — T33 「염력으로 사당할때 여행점수는 같이 못바치네」: 단추가 쪽 넘김 뒤에 숨었다)
        bool ReachOffer(ChoiceAnswer a) => MyCardKey == "psychic" && ShrineScreen.IsReachOffer(a, a.Target);

        bool ReachShrine(int cell)
        {
            abilityPicks.Clear();
            abilityCell = -1;
            reachPath.Clear();
            hover = -1;
            stage.Tips.Hide();
            bool open = flow.OpenShrine(cell);
            Refresh();
            return open;
        }

        /// <summary>고르기의 값 단추(<see cref="AbilityShown"/>의 색인)를 눌렀다 — 그 답을 낸다.</summary>
        public bool PickAbility(int index) => index >= 0 && index < abilityShown.Count && AnswerAbility(abilityShown[index]);

        bool AnswerAbility(ChoiceAnswer answer)
        {
            var choice = flow.Client.Choice(ChoiceKind.MapAction);
            if (!flow.InputOpen || choice == null) return false;
            abilityPicks.Clear();
            abilityCell = -1;
            reachPath.Clear();
            hover = -1;
            stage.Tips.Hide();
            if (CardAsk.IsTradeMark(answer)) return flow.OpenTrade(answer.Values[0]);   // 염력 거래: 답이 아니라 그 좌석으로 거래 창(제안할 때 능력을 쓴다)
            return flow.Answer(choice, answer).Accepted;
        }

        // psychic B 「염력」 거래(사용자 결정 84 T12 ② 나): 2칸 안 좌석과의 거래는 거래 내용 때문에 합법 답으로 늘어놓을 수 없어(TradeRules.Telekinesis) 고르기에 표만 끼운다 —
        // 그 좌석의 칸이 칠해지고, 값 단추 「거래 ○○」를 누르면 그 좌석으로 거래 창. 같은 칸 · 같은 신의 추종자는 [거래] 단추 그대로(능력을 쓰지 않는 길)
        void AddTradeMarks(GameState view, int seat)
        {
            if (abilityWeather || MyCardKey != "psychic") return;
            foreach (int p in flow.TradePartners())
                if (TradeRules.ModeWith(view, flow.Ctx, seat, p) == TradeRules.Telekinesis) abilityPicks.Add(CardAsk.TradeMark(view, p));
        }

        /// <summary>보드의 카드 물음 단추(<see cref="CardAnswers"/>의 색인)를 눌렀다 — 그 답을 낸다.</summary>
        public bool PressCard(int index)
        {
            var choice = flow.Client != null ? flow.Client.FirstChoice() : null;
            if (!flow.InputOpen || !CardAsking || choice == null || index < 0 || index >= cardAnswers.Count) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, cardAnswers[index]).Accepted;
        }

        /// <summary>칸을 고르는 카드 물음(<see cref="CardCells"/>)에서 그 칸의 답을 낸다. 칠한 칸이 아니면 false.</summary>
        public bool CardAt(int cell)
        {
            if (!CardCells) return false;
            int index = cardAnswers.FindIndex(a => a.Kind == AnswerKind.Pick && a.Target == cell);
            return index >= 0 && PressCard(index);
        }

        /// <summary>보드 HUD가 받는 카드 물음인가(배틀 화면이 떠 있으면 배틀 독이 받는다).</summary>
        public static bool BoardCardAsk(PendingChoice c) => CardAsk.FromCard(c) && c.Kind == ChoiceKind.Reaction && (c.Sub == ChoiceSub.EffectOption || c.Sub == ChoiceSub.EffectPick);

        // 능력 단추 하나(무리 = 날씨 · 카드): 이름 · 마우스 오버(카드 — 능력 + 문구)
        void AbilityButtonOf(VisualElement buttons, bool weather, bool open, bool dive = false)
        {
            int card = CardAsk.CardOf(flow.Client.View, flow.Client.Seat);
            string id = dive ? Dive : weather ? Weather : Ability;
            var b = Add(buttons, Kit.Button(stage.T(weather ? "item.weather_wheel" : CardAsk.AbilityKey(flow.Ctx, card, 1)), KitButtonKind.Normal, () => Press(id)), "board-hud-act-" + id, null);
            b.SetEnabled(open);
            if (open)
            {
                if (weather) stage.Tips.Hook(b, stage.T("item.weather_wheel"), stage.T("screen.card.weather.tip"), TipSide.Above, reading: true, clear: dock);
                else stage.Tips.Hook(b, CardAsk.Head(stage, flow.Ctx, card, 1), CardAsk.Text(stage, flow.Ctx, card, 1), TipSide.Above, reading: true, clear: dock);
            }
            shownActs.Add(id);
        }
        readonly List<VisualElement> warpRings = new List<VisualElement>();
        readonly List<VisualElement> gateRings = new List<VisualElement>();
        VisualElement reachMark;
        readonly List<VisualElement> gateDots = new List<VisualElement>();
        FateFx fateFx;
        Label timeout;
        VisualElement promptBox, promptBar, promptClock;
        VisualElement blessAsk, revive;   // U1 위 띠 동의 물음 · 부활의 금빛 고리(M6 「UB 화면 묶음 2」)
        VisualElement blessClock, blessBar;   // 동의 물음의 남은 시간(S41 프롬프트와 같은 꼴)
        Label blessLeft;
        Label promptLeft;
        int paintedFate = -1;
        Vector2 fateHand = new Vector2(277f, 617f);

        /// <summary>운명 카드 연출 층(받기 · 버리기 · 카드 지나가기).</summary>
        public FateFx FateFx => fateFx;

        /// <summary>펄기아로 갈 칸을 고르는 중인가(<see cref="BeginWarp"/> — 칸을 누르면 그 순간이동 답을 낸다).</summary>
        public bool Warping => warps.Count > 0;

        /// <summary>S41 응답 프롬프트가 「행동」 칸 자리에 떠 있나.</summary>
        public bool Prompting { get; private set; }

        /// <summary>프롬프트의 [수락]·[거절] 답(합법 답 그대로 — 없으면 null).</summary>
        public ChoiceAnswer PromptYes { get; private set; }
        public ChoiceAnswer PromptNo { get; private set; }

        /// <summary>「시간 초과 · 사용 안 함」이 지금 보이나(98행).</summary>
        public bool TimeoutShown { get; private set; }

        /// <summary>걷는 말이 한 걸음에 튀는 높이(보드 유닛) [가정].</summary>
        public const float HopHeight = 0.45f;

        /// <summary>찾아보기의 「!」(04 §8-8 20행)가 말 머리에서 뜨는 틈(패널 px).</summary>
        public const int BangAbove = 4;

        /// <summary>「!」 — 원작의 느낌표 말풍선(기호라 문자열 표에 두지 않는다).</summary>
        public const string BangMark = "!";

        /// <summary>「!」가 지금 떠 있나(찾아보기 단계가 재생 중이다).</summary>
        public bool BangShown { get; private set; }

        readonly GameFlow flow;
        readonly List<int> reach = new List<int>();
        readonly List<string> shownActs = new List<string>();
        UiStage stage;
        VisualElement seats, party, actions, dock, bang, logBox, logLines, dark, gain, res;
        long paintedRes = -1;
        Label gainText;
        readonly List<VisualElement> drops = new List<VisualElement>();
        int paintedShield;

        /// <summary>같은 칸의 말을 만났을 때의 「!」(04 §8-8 13행)가 지금 떠 있나 · 규토리 줍기(19행)의 「규토리 +n」이 보이는 만큼 · 지금 보이는 떨어지는 규토리 수 — 확인용.</summary>
        public bool MeetShown { get; private set; }
        public float GainShown => gain != null ? gain.style.opacity.value : 0f;

        /// <summary>심장 회복(94행 3막)의 조각·심장이 지금 보이나 — 확인용.</summary>
        public bool HeartShown { get; private set; }

        /// <summary>심장 회복의 원이 사당 칸 위로 뜨는 높이 · 조각이 도는 반지름(720 벌 px).</summary>
        public const float HeartAbovePx = 96f, HeartRadiusPx = 34f;

        /// <summary>파티가 이만큼 이상이면 아래 판의 도트 칸을 줄인다(USS <c>sn-hud-mons-many</c>).</summary>
        public const int ManyMons = 5;
        VisualElement heart, heartIcon;
        readonly List<VisualElement> shards = new List<VisualElement>();
        public int DropsShown { get; private set; }

        /// <summary>한 번에 그리는 떨어지는 규토리 수의 위쪽 끝(나무·산호는 3개).</summary>
        public const int MaxDrops = 6;
        /// <summary>규토리가 떨어지기 시작하는 높이 · 「규토리 +n」이 떠오르는 높이 · 규토리 사이 틈 · 보호 배지가 떨어지는 거리(720 벌 px).</summary>
        public const float DropFromPx = 44f, DropFrontPx = 22f, GainRisePx = 18f, DropGapPx = 26f, ShieldFallPx = 14f;
        Label turn, round, time, act, cellInfo, weather;

        /// <summary>위 띠의 시간대 배지가 지금 보여 주는 시간대(보여 주는 라운드에서 셈 — <see cref="DayTimes"/>, 결정 70 ⑥).</summary>
        public DayTime ShownTime { get; private set; }
        TurnBand band;
        BoardUb ub;

        /// <summary>울트라 버스트 몫(균열 칩 · UB 턴 띠 · 84~86행 — M6 「UB 화면 묶음 1」).</summary>
        public BoardUb Ub => ub;
        ActCard actCard;
        InfoCard info;
        StackBadges stacks;
        SheetLayer sheets;

        /// <summary>HUD 판 위의 도트 이펙트 판(회복 반짝임 — 결정 69).</summary>
        public SheetLayer Sheets => sheets;
        Dictionary<int, List<FixtureDef>> fixturesAt;
        readonly List<BoardLogLine> logShown = new List<BoardLogLine>();
        bool paintedMaint;

        // 정보 창 단추(결정 125): 열 수 있으면 켜고, 그 창이 보기만 하는 때면 흐린 단추(OffClass — 소리 없음) + 「지금은 보기만」 툴팁
        void Peek(Button button, bool peek, bool here, string tipKey)
        {
            button.SetEnabled(peek);
            if (!peek) return;
            if (!here) button.AddToClassList(UiStage.OffClass);
            stage.Tips.Hook(button, null, here ? stage.T(tipKey) : stage.T(tipKey) + "\n" + stage.T("screen.peek.view"), TipSide.Above, reading: true, clear: dock);
        }

        long paintedHp;
        int hover = -1, paintedSeat = -1, paintedRound = -1, paintedAct = int.MinValue, paintedLog = -1, hoverAny = -1, hoverInfo = -1, infoSeat = -1, infoCell = -1, hoverLook;

        public BoardHud(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-hud";

        /// <summary>지금 칠한 갈 수 있는 칸(엔진의 합법 이동 답의 도착 칸). 입력이 닫혀 있거나 이동할 수 없으면 비어 있다.</summary>
        public IReadOnlyList<int> Reach => reach;

        /// <summary>지금 판에 올린 단추(이름, 왼쪽 → 오른쪽).</summary>
        public IReadOnlyList<string> Acts => shownActs;

        /// <summary>그 단추가 지금 판에 있나.</summary>
        public bool Shows(string id) => shownActs.Contains(id);

        /// <summary>마우스가 올라가 있는 갈 수 있는 칸(없으면 −1).</summary>
        public int HoverCell => hover;

        /// <summary>
        /// 걷기 전 확인(결정 70 ⑱ [가정 — plan §5]): 갈 수 있는 칸을 한 번 누르면 그 칸이 「고른 칸」이 된다(없으면 −1) — 엔진이 걸을 길(<see cref="PickedPath"/>)을 칠해 두고
        /// 아래 판에 쓰는 이동력·남는 이동력과 [이동]을 올린다. 고른 칸을 한 번 더 누르거나 [이동]을 누르면 걷고(<see cref="ConfirmMove"/>),
        /// 갈 수 있는 다른 칸을 누르면 그 칸을 거쳐 가는 길이 된다(경유 칸 — 결정 71 ② <see cref="Vias"/>: 남은 이동력 안이면 최단이 아니어도 된다. 넘으면 고른 칸이 그 칸으로 바뀐다 [가정]).
        /// 경유 칸을 다시 누르면 그 칸을 뺀다. 오른쪽 단추 · Esc는 마지막 경유 칸을 하나씩 빼고, 없으면 고른 칸을 푼다(<see cref="Back"/>) [가정].
        /// 갈 수 없는 칸 · 보드 밖을 누르면 처음부터 다시 고른다(<see cref="Unpick"/>). 입력이 닫히거나 그 길로 갈 수 없게 되면 스스로 풀린다.
        /// </summary>
        public int PickedCell => picked;

        int picked = -1;
        readonly List<int> vias = new List<int>();

        /// <summary>결정 71 ② 경유 칸(누른 순서 — 길이 이 차례로 거쳐 고른 칸에 닿는다). 고른 칸이 없으면 비어 있다.</summary>
        public IReadOnlyList<int> Vias => vias;

        /// <summary>
        /// 고른 칸까지 걸을 길(출발 칸부터 도착 칸까지 — 경유 칸을 구간마다 최단으로 잇는다, <c>MoveRules.Route</c>). 고른 칸이 없거나 그 길로 갈 수 없으면 null.
        /// [이동]은 이 길을 그대로 낸다(<c>ChoiceAnswer.MoveAlong</c>).
        /// </summary>
        public List<int> PickedPath => RouteOf(vias, picked);

        List<int> RouteOf(IReadOnlyList<int> through, int dest)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (dest < 0 || view == null) return null;
            int seat = flow.Client.Seat;
            var me = view.Seats[seat];
            var board = flow.Ctx.Board;
            // 무료 지형 이동(rulings A36 — hiker·swimmer 등 「2 tiles for free」): 경유 칸 없이 그 길로 닿으면 그 길(이동력을 쓰지 않는다 — 기본).
            // 「쓸지·언제 쓸지는 플레이어가 고른다」: 고른 칸에서 [이동력으로 걷기]를 누르면(paidCell) 보통 걸음 길(감사 G2-2 · G3-2 [가정 #660])
            int count = through != null ? through.Count : 0;
            if (count == 0 && dest != paidCell)
            {
                var free = TrainerRules.FreePath(view, flow.Ctx, seat, dest);
                if (free != null)
                {
                    free.Insert(0, me.Cell);
                    return free;
                }
            }
            // 그 좌석의 걷기 조건(MoveRules.MoveTerms — 높이 무시 · 내려서기·활공 · 길)으로 구간마다 가장 싼 길을 잇는다(엔진과 같은 셈 — 활공·길 이어 가기는 첫 구간에만 싣는다)
            var terms = TrainerRules.Terms(view, flow.Ctx, seat);
            var route = new List<int> { me.Cell };
            int at = me.Cell, left = me.MovesLeft;
            for (int i = 0; i <= count; i++)
            {
                int to = i < count ? through[i] : dest;
                int glide = i == 0 ? me.Glide : 0;
                bool chain = i == 0 && me.RoadChain;
                var leg = MoveRules.Path(board, at, to, left, terms, glide, chain);
                if (leg == null) return null;
                left -= MoveRules.PathCost(board, at, leg.GetRange(1, leg.Count - 1), terms, glide, chain);
                for (int k = 1; k < leg.Count; k++) route.Add(leg[k]);
                at = to;
            }
            return route;
        }

        /// <summary>고른 길이 무료 지형 이동인가(이동력을 쓰지 않는다 — [이동]이 Arg = FreeArg로 낸다).</summary>
        public bool PickedFree => IsFree(PickedPath);

        bool IsFree(List<int> path)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            return path != null && path.Count >= 2 && vias.Count == 0 && path[path.Count - 1] != paidCell && view != null && TrainerRules.FreeWalkAllowed(view, flow.Ctx, flow.Client.Seat, path.GetRange(1, path.Count - 1));
        }

        // 무료 지형 이동으로 닿는 고른 칸을 이동력으로 걷기로 바꾼 칸(없으면 −1 — 감사 G2-2 · G3-2)
        int paidCell = -1;

        /// <summary>
        /// 고른 칸(경유 칸 없음)이 무료 지형 이동으로도 · 보통 걸음으로도 닿나 — 그러면 [이동] 곁에 바꾸기 단추가 선다(<see cref="ToggleFree"/>, rulings A36 「쓸지·언제 쓸지는 플레이어가 고른다」 [가정 #660]).
        /// </summary>
        public bool FreeChoosable
        {
            get
            {
                var view = flow.Client != null ? flow.Client.View : null;
                if (picked < 0 || vias.Count > 0 || view == null || TrainerRules.FreePath(view, flow.Ctx, flow.Client.Seat, picked) == null) return false;
                int keep = paidCell;
                paidCell = picked;
                var paid = RouteOf(null, picked);
                paidCell = keep;
                return paid != null;
            }
        }

        /// <summary>고른 칸의 걸음을 무료 지형 이동 ↔ 이동력으로 걷기로 바꾼다. 바꿀 수 없으면 false.</summary>
        public bool ToggleFree()
        {
            if (!flow.InputOpen || !FreeChoosable) return false;
            paidCell = paidCell == picked ? -1 : picked;
            Refresh();
            return true;
        }

        /// <summary>[이동력으로 걷기] · [무료로 걷기] 단추의 요소 이름.</summary>
        public const string MoveFree = "board-hud-move-free";

        // 고른 길에 드는 이동력(무료 지형 이동이면 0 — 길·활공은 엔진과 같은 셈)
        int PathCostOf(List<int> path)
        {
            if (path == null || path.Count < 2) return 0;
            if (IsFree(path)) return 0;
            var view = flow.Client.View;
            var me = view.Seats[flow.Client.Seat];
            return MoveRules.PathCost(flow.Ctx.Board, me.Cell, path.GetRange(1, path.Count - 1), TrainerRules.Terms(view, flow.Ctx, flow.Client.Seat), me.Glide, me.RoadChain);
        }

        /// <summary>결정 71 ① 고른 길 위에서 걷다가 멈춰 공개할지 물을 뒷면 숨겨진 장소 칸(엔진과 같은 셈 — <c>MapActions.StopsOn</c>). 고른 칸이 없으면 비어 있다.</summary>
        public List<int> PathStops => StopsOf(PickedPath);

        List<int> StopsOf(List<int> path)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (path == null || path.Count < 2 || view == null) return new List<int>();
            return MapActions.StopsOn(view, flow.Ctx, flow.Client.Seat, path.GetRange(1, path.Count - 1), IsFree(path));
        }

        /// <summary>
        /// 결정 71: 경유 칸 없이 cell까지 최단 길로 걸으면 도착하기 전에(길 가운데) 멈춰 물을 칸(도착 칸은 뺀다). 걸은 뒤 도착 칸을 단정하는 확인(ScreenProbe·MouseProbe)은 이것이 빈 칸을 고른다.
        /// </summary>
        public List<int> StopsBefore(int cell)
        {
            var stops = StopsOf(RouteOf(null, cell));
            stops.Remove(cell);
            return stops;
        }

        /// <summary>[이동] 단추의 요소 이름.</summary>
        public const string MoveGo = "board-hud-move-go";

        /// <summary>[취소] 단추(결정 73 ③ — 고른 칸·거쳐 갈 칸을 모두 푼다)와 그 곁의 오른쪽 클릭 안내의 요소 이름.</summary>
        public const string MoveCancel = "board-hud-move-cancel", MoveHint = "board-hud-move-hint";

        public TurnBand Band => band;

        /// <summary>막 카드(S14 — 막 공개 연출).</summary>
        public ActCard ActCard => actCard;

        /// <summary>공개 정보 카드(좌석 칩·보드 위 말·토큰에 마우스를 올리면 뜬다).</summary>
        public InfoCard Info => info;

        /// <summary>묶음 표지(한 칸에 말 여럿 — 결정 66 ①).</summary>
        public StackBadges Stacks => stacks;

        /// <summary>묶음 표지를 지금 묶음·카메라·시각에 맞춘다(프레임마다 — 화면 흐름이 부른다).</summary>
        public void ShowStacks() => stacks?.Show(flow);

        /// <summary>마우스가 올라가 있는 칸(갈 수 없는 칸 포함, 없으면 −1) — 아래 판의 칸 정보 줄이 이 칸을 적는다(없으면 고른 칸, 그것도 없으면 내가 선 칸 — <see cref="InfoCell"/>).</summary>
        public int HoverAny => hoverAny;

        // 칸 정보 줄이 적는 칸: 마우스를 올린 칸 → 고른 칸(걷기 전 확인 — 마우스가 [이동] 단추로 가도 확인할 도착 칸이 남는다) → 내가 선 칸
        int InfoCell(int mine) => hoverAny >= 0 ? hoverAny : picked >= 0 ? picked : mine;

        /// <summary>아래 판의 칸 정보 줄에 지금 적힌 글.</summary>
        public string CellInfoText => cellInfo != null ? cellInfo.text : null;

        /// <summary>왼쪽 위 기록 판에 지금 보이는 줄(오래된 것부터 — 최대 <see cref="RecentLines"/>).</summary>
        public IReadOnlyList<string> RecentLog => recent;

        readonly List<string> recent = new List<string>();

        /// <summary>왼쪽 위 기록 판에 보이는 줄 수(목업 r3 board — 3줄).</summary>
        public const int RecentLines = 3;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            var page = new VisualElement { pickingMode = PickingMode.Ignore };
            // 묶음 표지(한 칸에 말 여럿)는 가장 아래 층: HUD 판·띠·창이 표지를 덮는다
            stacks = new StackBadges(stage);
            page.Add(stacks.Element);
            // 귀환(71행)의 어둠: 보드를 덮고 HUD 판 아래에 깔린다(마우스를 받지 않는다)
            dark = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-dark", "sn-hud-dark");
            FxUi.Fade(dark, 0f);

            // 부활의 금빛 고리(91행): 말 머리 위에서 발밑으로 내려온다(마우스를 받지 않는다)
            revive = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-revive", "sn-hud-revive");
            FxUi.Fade(revive, 0f);
            var top = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-top", "sn-hud-top");
            seats = Add(top, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-seats", "sn-hud-seats");
            var status = Add(top, Kit.Panel(stage.Skin), "board-hud-status", "sn-hud-status");
            turn = Add(status, Kit.Badge("", KitBadgeKind.Gold, Kit.F2), "board-hud-turn", null);
            round = Add(status, Kit.Badge("", KitBadgeKind.Plain, Kit.F2), "board-hud-round", null);
            // 시간대(결정 70 ⑥): 라운드마다 하나씩 지난다 — 라운드 배지 옆 작은 배지(배틀 무대의 배경이 이것을 따른다)
            time = Add(status, Kit.Badge("", KitBadgeKind.Plain, Kit.F2), "board-hud-time", "sn-hud-time");
            // 아르세우스의 축복 동의(결정 84 U1 나): 위 띠 바로 아래 가운데 — 누가 축복을 내면 다른 좌석에게 [허용]·[막기]
            blessAsk = Add(page, Kit.Panel(stage.Skin), BlessAsk, "sn-hud-bless-ask");
            blessAsk.style.display = DisplayStyle.None;
            stage.Tips.Hook(time, null, stage.T("screen.board.time.tip"), TipSide.Below, reading: true);
            // 날씨(parasol_lady — 날씨 휠, rulings A41): 놓인 뒤에만 — 「날씨 · 타입 +2」
            weather = Add(status, Kit.Badge("", KitBadgeKind.Plain, Kit.F2), WeatherBadge, null);
            stage.Tips.Hook(weather, null, stage.T("screen.board.weather.tip"), TipSide.Below, reading: true);
            act = Add(status, Kit.Badge("", KitBadgeKind.Silver, Kit.F2), "board-hud-act", null);
            stage.Tips.Hook(round, null, stage.T("screen.board.round.tip"), TipSide.Below, reading: true);
            stage.Tips.Hook(act, null, stage.T("screen.board.act.tip"), TipSide.Below, reading: true);
            stage.OnTap(act, SoundKeys.Select, e => flow.OpenAct());   // 막 배지를 누르면 막 안내 창을 다시 본다(결정 101 ②)
            // 보기 단추 묶음(도감 · 상성 · 설명서): 좌석 다섯 이상의 1080 벌은 상태 판 둘째 줄로 내린다(USS — 칩 이름 한글 6자 자리, 결정 112 Q15)
            var tools = Add(status, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-tools", "sn-hud-tools");
            // 포켓몬 도감(S42 — 결정 105 ③): 언제든 덱 구성을 본다
            var dex = Add(tools, Kit.Button(stage.T("screen.dex.open"), KitButtonKind.Normal, () => flow.OpenDex()), "board-hud-dex", null);
            stage.Tips.Hook(dex, null, stage.T("screen.dex.open.tip"), TipSide.Below, reading: true);
            // 타입 상성 · 상태 이상(플레이 시험 T5): 어느 화면에서든 — 창 · 배틀이 떠 있으면 그 위 정보 창 층에
            var types = Add(tools, Kit.Button(stage.T("screen.types.open"), KitButtonKind.Normal, () => flow.ToggleTypes()), "board-hud-types", null);
            stage.Tips.Hook(types, null, stage.T("screen.types.open.tip"), TipSide.Below, reading: true);
            // 버림 더미(결정 196): 덱마다 버려진 카드 — 사당 이벤트로 버림 더미에서 가져오기 전에 본다. 좌석 다섯 이상의 720 벌은 위 띠에 자리가 없어(칩 이름 한글 6자 — 결정 112 Q15) 기록 판 머리의 단추(board-hud-log-discards)로 [가정 결정 196]
            var discards = Add(tools, Kit.Button(stage.T("screen.discards.open"), KitButtonKind.Normal, () => flow.ToggleDiscards()), "board-hud-discards", null);
            stage.Tips.Hook(discards, stage.T("screen.discards.title"), stage.T("screen.discards.open.tip"), TipSide.Below, reading: true);
            var book = Add(tools, Kit.Button(RulebookView.Help, KitButtonKind.Normal, () => flow.ToggleBookSide()), "board-hud-book", null);   // 룰 설명서 옆 판 S43 C(M9 — 6인 1080 위 띠 폭 때문에 글리프 하나)
            stage.Tips.Hook(book, stage.T("screen.rulebook.title"), stage.T("screen.rulebook.open.tip"), TipSide.Below, reading: true);
            var menu = Add(status, Kit.Button(stage.T("screen.board.menu"), KitButtonKind.Normal, () => flow.OpenMenu()), "board-hud-menu", null);

            // 기록(S21): 좌석 칩 아래 판에 최근 줄 셋 + [펼치기](목업 r3 board). 줄은 그 행동의 연출이 끝난 뒤에 붙는다
            logBox = Add(page, Kit.Panel(stage.Skin), "board-hud-log", "sn-hud-log");
            var logHead = Add(logBox, new VisualElement(), null, "sn-hud-log-head");
            Add(logHead, Kit.Text(stage.T("screen.log.title"), Kit.F2), null, "kit-title");
            var logDiscards = Add(logHead, Kit.Button(stage.T("screen.discards.title"), KitButtonKind.Normal, () => flow.ToggleDiscards()), "board-hud-log-discards", "sn-hud-log-discards");
            stage.Tips.Hook(logDiscards, null, stage.T("screen.discards.open.tip"), TipSide.Below, reading: true);
            var logOpen = Add(logHead, Kit.Button(stage.T("screen.log.open"), KitButtonKind.Normal, () => flow.OpenLog()), "board-hud-log-open", null);
            stage.Tips.Hook(logOpen, null, stage.T("screen.log.open.tip"), TipSide.Below, reading: true);
            logLines = Add(logBox, new VisualElement(), "board-hud-log-lines", "sn-hud-log-lines");
            paintedLog = -1;

            dock = Add(page, Kit.Panel(stage.Skin), "board-hud-dock", "sn-hud-dock");
            party = Add(dock, new VisualElement(), "board-hud-party", "sn-hud-party");
            actions = Add(dock, new VisualElement(), "board-hud-actions", "sn-hud-actions");
            // 내 자원 줄(결정 98): 규토리 일곱 색 · 여행 점수 · 볼 넷 — 아래 판 왼쪽 위에 얹은 띠(판의 배치 밖: 판이 한 줄 높아져도 따라 올라간다). 마우스를 올리지 않아도 늘 보인다
            res = Add(dock, Kit.Panel(stage.Skin), Res, "sn-hud-res");
            // 보드 돌리기(결정 164): 아래 판 오른쪽 위에 얹은 작은 단추 둘(자원 줄의 오른쪽 짝 — 판의 배치 밖) — Q · E와 같은 함수(GameFlow.Turn), 키를 못 받는 때는 숨긴다(SyncTurn)
            rotate = Add(dock, new VisualElement(), Rotate, "sn-hud-rotate");
            var turnLeft = Add(rotate, Kit.Button(RotateLeftText, KitButtonKind.Normal, () => flow.Turn(-1)), RotateLeft, null);
            stage.Tips.Hook(turnLeft, stage.T("screen.board.rotate.left"), null, TipSide.Above);
            var turnRight = Add(rotate, Kit.Button(RotateRightText, KitButtonKind.Normal, () => flow.Turn(1)), RotateRight, null);
            stage.Tips.Hook(turnRight, stage.T("screen.board.rotate.right"), null, TipSide.Above);
            rotateShown = true;
            SyncTurn();

            // 찾아보기의 「!」: 말 머리 위에 뜬다(자리는 연출이 도는 동안 옮기기로 맞춘다). 마우스를 받지 않는다
            bang = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-bang", "sn-hud-bang");
            var mark = Kit.Text(BangMark, Kit.F3);
            mark.pickingMode = PickingMode.Ignore;
            bang.Add(mark);
            FxUi.Fade(bang, 0f);
            // 규토리 줍기(19행): 떨어지는 규토리 그림과 「규토리 +n」 글 — 자리는 연출이 옮기기로 맞춘다. 마우스를 받지 않는다
            drops.Clear();
            for (int i = 0; i < MaxDrops; i++)
            {
                var drop = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-drop-" + i.ToString(CultureInfo.InvariantCulture), "sn-hud-drop");
                FxUi.Fade(drop, 0f);
                drops.Add(drop);
            }
            gain = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-gain", "sn-hud-gain");
            gainText = Kit.Badge("", KitBadgeKind.Ok, Kit.F2);
            gainText.pickingMode = PickingMode.Ignore;
            gain.Add(gainText);
            FxUi.Fade(gain, 0f);
            // 심장 회복(94행 3막): 시작의 사당 위에서 조각 셋이 돌며 모여 심장이 된다. 마우스를 받지 않는다
            heart = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-heart", "sn-hud-heart");
            shards.Clear();
            for (int i = 0; i < SeatState.ShardCount; i++)
                shards.Add(Add(heart, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-shard-" + i.ToString(CultureInfo.InvariantCulture), "sn-hud-shard"));
            for (int i = 0; i < shards.Count; i++) shards[i].AddToClassList("sn-shard-" + i.ToString(CultureInfo.InvariantCulture));
            heartIcon = Add(heart, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-heart-icon", "sn-hud-heart-icon");
            var heartTex = GenAssets.Load<Texture2D>(GenAssets.ItemIcons + "heart_of_sinnoh.png");
            if (heartTex != null) heartIcon.style.backgroundImage = new StyleBackground(heartTex);
            FxUi.Fade(heart, 0f);
            // 펄기아 순간이동(14행): 출발·도착 칸의 분홍 고리. 마우스를 받지 않는다
            warpRings.Clear();
            foreach (string end in new[] { "from", "to" })
            {
                var ring = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-warp-" + end, "sn-hud-warp-ring");
                FxUi.Fade(ring, 0f);
                warpRings.Add(ring);
            }
            // 염력(psychic B)으로 고른 떨어진 칸(결정 120): 능력 사용 50행 동안 그 칸에 그 좌석 색 표시. 마우스를 받지 않는다
            reachMark = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, ReachMark, "sn-hud-reach-mark");
            FxUi.Fade(reachMark, 0f);
            // 왜곡 게이트 이동(15행): 두 게이트의 보라 고리와 그 사이의 보라 점선. 마우스를 받지 않는다
            gateRings.Clear();
            foreach (string end in new[] { "from", "to" })
            {
                var ring = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-gate-" + end, "sn-hud-gate-ring");
                FxUi.Fade(ring, 0f);
                gateRings.Add(ring);
            }
            gateDots.Clear();
            for (int i = 0; i < GateBeats.Dots; i++)
            {
                var dot = Add(page, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-gate-dot-" + i.ToString(CultureInfo.InvariantCulture), "sn-hud-gate-dot");
                FxUi.Fade(dot, 0f);
                gateDots.Add(dot);
            }
            band = new TurnBand(stage);
            page.Add(band.Element);
            // 운명 카드 연출(7·8·42·45행): 받기 · 버리기 · 카드 지나가기 — 띠·막 카드 아래, 마우스를 받지 않는다
            fateFx = new FateFx(stage, "board-hud");
            page.Add(fateFx.Element);
            // 사당·균열의 보드 쪽 연출(33·34행 — 어둠 · 번개 · 착지 고리 · 규토리 비 · 「이동력 +1」): 마우스를 받지 않는다
            shrineFx = new ShrineFx(stage, flow);
            page.Add(shrineFx.Element);
            // S41 시간 초과(98행): 「시간 초과 · 사용 안 함」이 행동 칸 위에 잠시 남는다
            timeout = Add(page, Kit.Badge(stage.T("screen.prompt.timeout"), KitBadgeKind.Plain, Kit.F2), "board-hud-timeout", "sn-hud-timeout");
            timeout.pickingMode = PickingMode.Ignore;
            FxUi.Fade(timeout, 0f);
            actCard = new ActCard(stage);
            page.Add(actCard.Element);
            // 도트 이펙트 판(결정 69 — 회복 반짝임): HUD 판 위에 찍는다. 마우스를 받지 않는다
            sheets = new SheetLayer("board-hud-sheets");
            page.Add(sheets.Element);
            // 울트라 버스트(M6 「UB 화면 묶음 1」): 위 띠의 균열 칩 · UB 턴 띠 · 빛줄기 · 구간 맥동 · 패배 덮개 — 신오의 심장 판에서는 보이지 않는다
            ub = new BoardUb(flow, stage, status, menu, page);
            info = new InfoCard(stage, flow);
            infoSeat = infoCell = -1;
            Refresh();
            if (flow.Fx != null) ShowFx(flow.Fx.Sequencer);
            ShowStacks();
            return page;
        }

        // ── 보드 돌리기 단추(결정 164) ──

        /// <summary>돌리기 단추 묶음 <c>board-hud-rotate</c>(아래 판 오른쪽 위) · ↺ <c>-left</c>(Q) · ↻ <c>-right</c>(E) — 튜토리얼 층도 늘 받는다(키처럼 카메라만 바꾼다).</summary>
        public const string Rotate = "board-hud-rotate", RotateLeft = Rotate + "-left", RotateRight = Rotate + "-right";

        // 단추 글: 돌리는 쪽 글리프 + 같은 일을 하는 키(키 글자는 언어와 무관 — 배틀 독의 단축키 배지처럼 코드에)
        const string RotateLeftText = "↺ Q", RotateRightText = "E ↻";

        VisualElement rotate;
        bool rotateShown;

        /// <summary>돌리기 단추가 보이나(키를 받는 때 — <see cref="GameFlow.CanTurn"/>, 남의 행동 보기 창이 아래 판을 덮는 동안은 숨김).</summary>
        public bool RotateShown => rotateShown;

        /// <summary>돌리기 단추를 Q · E를 받는 때에만 보이고, 자원 줄은 배틀 화면이 떠 있는 동안 숨긴다(프레임마다 — 화면 흐름의 Layout이 부른다).</summary>
        public void SyncTurn()
        {
            if (rotate == null) return;
            // 자원 줄(아래 판 위에 얹은 띠)은 배틀 화면이 떠 있는 동안 숨긴다 — 배틀 독보다 위로 솟아 반투명 덮개 아래로 글자가 비쳤다(M10)
            bool resOn = !(flow.Stage.Current is BattleScreen);
            if (res != null && resOn == res.ClassListContains(ResOff)) res.EnableInClassList(ResOff, !resOn);
            bool show = flow.CanTurn && !flow.WatchShown;
            if (show == rotateShown) return;
            rotateShown = show;
            rotate.EnableInClassList(RotateOff, !show);
        }

        /// <summary>돌리기 단추를 숨기는 클래스(USS — 카드 능력 두 줄로 아래 판이 높아진 동안도 같은 꼴로 숨긴다: 그때는 룰 설명서 옆 판과 자리가 겹친다).</summary>
        public const string RotateOff = "sn-hud-rotate-off";

        // ── 내 자원 줄(결정 98) ──

        /// <summary>내 자원 줄의 요소 이름(튜토리얼 앵커로도 쓴다): 줄 <c>board-hud-res</c> · 칸 <c>-apricorn-&lt;색&gt;</c>·<c>-jp</c>·<c>-ball-&lt;n&gt;</c>, 0인 칸은 <see cref="ResZero"/>로 흐리다.</summary>
        public const string Res = "board-hud-res", ResZero = "sn-hud-res-zero";

        /// <summary>자원 줄을 숨기는 클래스(배틀 화면이 떠 있는 동안 — <see cref="SyncTurn"/>).</summary>
        public const string ResOff = "sn-hud-res-off";

        // 자원의 표시 값: 연출이 착지하기 전에는 붙잡아 둔 값(04 §8-3 — 공개 정보 카드와 같은 값)
        int ResOf(ResKind kind, int key, int now) => flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Resource(flow.Client.Seat, kind, key), now) : now;

        // 지금 보여야 할 자원 값들을 한 수로(바뀌었나만 본다)
        long ResShown()
        {
            var view = flow.Client?.View;
            if (view == null) return -1;
            var me = view.Seats[flow.Client.Seat];
            long sum = ResOf(ResKind.Jp, -1, me.Jp);
            for (int c = 0; c < me.Apricorns.Length; c++) sum = sum * 131 + ResOf(ResKind.Apricorn, c, me.Apricorns[c]);
            for (int b = 0; b < me.Balls.Length; b++) sum = sum * 131 + ResOf(ResKind.Ball, b, me.Balls[b]);
            return sum;
        }

        void FillRes()
        {
            var view = flow.Client?.View;
            if (res == null || view == null) return;
            var inv = CultureInfo.InvariantCulture;
            var me = view.Seats[flow.Client.Seat];
            res.Clear();
            for (int c = 0; c < me.Apricorns.Length; c++)
            {
                var color = (ApricornColor)c;
                ResItem(Res + "-apricorn-" + color.ToString().ToLowerInvariant(), EncounterScreen.ApricornKey(color), ResOf(ResKind.Apricorn, c, me.Apricorns[c]), stage.T(WorkUi.ColorKey(color)) + " " + stage.T("screen.info.apricorns"));
            }
            Add(res, Kit.Text(stage.T("screen.info.jp").Replace("{n}", ResOf(ResKind.Jp, -1, me.Jp).ToString(inv)), Kit.F2), Res + "-jp", "sn-hud-res-jp");
            for (int b = 0; b < me.Balls.Length; b++)
                ResItem(Res + "-ball-" + b.ToString(inv), GenAssets.ItemIcons + BattleFeed.BallItem(b) + ".png", ResOf(ResKind.Ball, b, me.Balls[b]), stage.T(BattleFeed.BallKey(b)));
            paintedRes = ResShown();
        }

        void ResItem(string name, string icon, int n, string tip)
        {
            var item = Add(res, new VisualElement(), name, "sn-hud-res-item");
            WorkUi.Icon(item, icon, "sn-icon");
            item.Add(Kit.Text(n.ToString(CultureInfo.InvariantCulture), Kit.F2));
            if (n <= 0) item.AddToClassList(ResZero);
            stage.Tips.Hook(item, tip, null, TipSide.Above);
        }

        public void Close()
        {
            warps.Clear();
            info?.Detach();
            infoSeat = infoCell = -1;
            hoverAny = hoverInfo = -1;
            hover = -1;
            picked = -1;
            reach.Clear();
            flow.BoardMarks?.Hide();
        }

        // ── 그리기 ──

        /// <summary>보기·입력 상태가 바뀌었다: 위 띠 · 파티 · 행동 판 · 이동 표시를 지금 값으로 다시 그린다.</summary>
        public void Refresh()
        {
            if (stage == null || seats == null) return;
            var client = flow.Client;
            var view = client?.View;
            if (view == null) return;
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            bool open = flow.InputOpen;
            var choice = client.FirstChoice();
            var me = view.Seats[client.Seat];

            // 위 띠: 좌석 칩(남 좌석은 M7 — 지금은 보기의 좌석을 그대로 돈다) + 턴 · 라운드 · 막
            ShownTurn(view, out paintedSeat, out paintedRound);
            seats.Clear();
            for (int s = 0; s < view.Seats.Length; s++)
            {
                var chip = Add(seats, Kit.Panel(stage.Skin), "board-hud-seat-" + (s + 1).ToString(inv), "sn-hud-seat");
                if (s == paintedSeat) chip.AddToClassList("sn-hud-seat-turn");
                var face = Add(chip, FaceBox.Make(), "board-hud-seat-" + (s + 1).ToString(inv) + "-face", null);
                FaceBox.Paint(face, s, flow.SeatColor(s), flow.Protagonist(s));
                // 추종자(천상의 균열 — rulings U11: 공개된 추종자는 계속 보인다): 이름 바탕을 그 신의 색으로 칠하고 이름 뒤에 신 아이콘(결정 80 L13 나).
                // 비밀 선택 · 공개 연출(34행) 전에는 뒷면이라 보이지 않는다
                var god = ShownFollower(view, s);
                var name = Add(chip, Kit.Text(flow.SeatName(s), Kit.F2), null, "sn-hud-seat-name");
                // 위 띠가 모자라면 이름이 「…」로 줄어든다(USS) — 전체 이름은 마우스 오버(추종자 바탕이면 아래 추종자 툴팁의 머리에 이름을 싣는다)
                // 방 판의 내 칩: 오른쪽 클릭 = 감정 표현 메뉴(결정 112 Q12 — RoomLayer가 받는다)
                if (god == SinnohGod.None || view.Seats.Length < FollowIconSeats) stage.Tips.Hook(name, flow.SeatName(s), s == client.Seat && flow.Room.InGame ? stage.T("screen.room.emote.chip.tip") : null, TipSide.Below);
                if (god != SinnohGod.None)
                {
                    name.AddToClassList(FollowClass);
                    name.AddToClassList(FollowClass + "-" + god.ToString().ToLowerInvariant());
                    // 다섯 명 이상이면 큰 아이콘을 빼고 색 바탕만(1080 벌에서 칩 여섯 + 아이콘 여섯은 위 띠를 넘어 상태 판을 화면 밖으로 민다) — 마우스 오버는 이름에
                    VisualElement follower = name;
                    if (view.Seats.Length < FollowIconSeats)
                    {
                        follower = Add(chip, new VisualElement(), null, "sn-hud-follower");
                        var godTex = GenAssets.Load<Texture2D>(ShrineScreen.GodIcon(god));
                        if (godTex != null) follower.style.backgroundImage = new StyleBackground(godTex);
                    }
                    follower.name = "board-hud-seat-" + (s + 1).ToString(inv) + "-follower";
                    stage.Tips.Hook(follower, (follower == name ? flow.SeatName(s) + " · " : "") + stage.T("screen.shrine.follower.chip").Replace("{god}", stage.T(BoardLog.GodKey((int)god))),stage.T(god == SinnohGod.Arceus ? "screen.shrine.follower.arceus.tip" : "screen.shrine.follower.chip.tip"), TipSide.Below, reading: true);
                }
                // 도전 보호(트레이너전 뒤 — 다음 자기 턴 시작까지): 보호 배지. 풀리는 연출(9행)이 다 보일 때까지 남는다.
                // 칩의 배치 밖에 얹는다(오른쪽 아래 모서리에 걸친 꼬리표 — USS): 배지가 생기고 없어질 때 칩 폭이 바뀌어 옆 칩이 옮겨지지 않게
                var fall = flow.FallShown(s);
                if (fall.Grey >= 0.5f) chip.AddToClassList(OutClass);
                if (view.Seats[s].Eliminated || fall.Grey > 0f)
                {
                    var tag = Add(chip, Kit.Badge(stage.T("screen.board.seat.out"), KitBadgeKind.Warn, Kit.F2), "board-hud-seat-" + (s + 1).ToString(inv) + "-out", "sn-hud-out");
                    tag.style.display = fall.Crack ? DisplayStyle.Flex : DisplayStyle.None;
                    stage.Tips.Hook(tag, null, stage.T("screen.board.seat.out.tip"), TipSide.Below, reading: true);
                }
                if (Shielded(view, s))
                {
                    var shield = Add(chip, Kit.Badge(stage.T("screen.board.protected"), KitBadgeKind.Plain, Kit.F2), "board-hud-seat-" + (s + 1).ToString(inv) + "-shield", "sn-hud-shield");
                    stage.Tips.Hook(shield, null, stage.T("screen.board.protected.tip"), TipSide.Below, reading: true);
                }
                // worker B 도로 켬(이번 턴에 밟는 칸마다 도로 — SeatState.Building): 보호 배지와 같은 모서리(도로는 자기 턴에만 · 보호는 자기 턴이 오면 풀린다 — 같이 서지 않는다)
                else if (view.Seats[s].Building)
                {
                    var road = Add(chip, Kit.Badge(stage.T("screen.board.road"), KitBadgeKind.Gold, Kit.F2), RoadName(s), "sn-hud-road");
                    stage.Tips.Hook(road, null, KoJosa.Fix(stage.T("screen.board.road.tip").Replace("{name}", flow.SeatName(s))), TipSide.Below, reading: true);
                }
            }
            paintedShield = ShieldMask(view);
            paintedFollowers = FollowerMask(view);
            // 좌석 다섯 이상(결정 112 Q15 — 칩 이름이 한글 6자 고정 폭): 1080 벌은 위 띠가 모자라 턴 배지를 뺀다(턴인 좌석은 칩 테두리 · 넘김 띠가 알린다 [가정 M7-89])
            seats.parent?.EnableInClassList(ManyClass, view.Seats.Length >= FollowIconSeats);
            logBox?.EnableInClassList(ManyClass, view.Seats.Length >= FollowIconSeats);   // 720 벌의 버림 더미 단추 자리(결정 196 — USS)
            seats.parent?.EnableInClassList(ToolsDownClass, view.Seats.Length >= ToolsDownSeats);
            bool mine = view.ActiveSeat == client.Seat;
            turn.text = paintedSeat == client.Seat ? stage.T("screen.board.my_turn") : flow.SeatName(paintedSeat);
            stage.Tips.Hook(turn, turn.text, null, TipSide.Below);   // 긴 이름은 배지에서 「…」(USS) — 전체 이름은 마우스 오버
            round.text = stage.T("screen.board.round").Replace("{n}", paintedRound.ToString(inv));
            ShownTime = DayTimes.Of(paintedRound);
            time.text = stage.T(DayTimes.TextKey(ShownTime));
            foreach (DayTime t in Enum.GetValues(typeof(DayTime))) time.EnableInClassList("sn-hud-time-" + DayTimes.Name(t), t == ShownTime);
            var weatherType = TrainerRules.WeatherType(view);
            weather.style.display = weatherType.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (weatherType.HasValue)
            {
                weather.text = WeatherText(view.Weather);
                stage.Tips.Hook(weather, CardAsk.WeatherBonus(stage, view.Weather), stage.T("screen.board.weather.tip"), TipSide.Below, reading: true);
            }
            // 막 배지는 막 카드가 착지할 때 바뀐다(보기는 공개와 함께 바로 다음 막이다 — ActBeats.ShownAct)
            int actIndex = view.Hos != null ? ActBeats.ShownAct(view.Hos.Act, flow.Fx != null ? flow.Fx.Sequencer : null) : -1;
            paintedAct = actIndex;
            string actName = actIndex >= 0 ? stage.TOrNull("screen.board.act." + actIndex.ToString(inv)) : null;
            act.text = actName ?? "";
            act.style.display = actName != null ? DisplayStyle.Flex : DisplayStyle.None;
            ub?.Refresh();

            FillRes();

            // 내 파티
            party.Clear();
            int count = PokemonRules.PartyCount(me);
            var partyHead = Add(party, new VisualElement(), null, "sn-hud-party-head");
            var partyLeft = Add(partyHead, new VisualElement(), null, "sn-hud-party-left");
            Add(partyLeft, Kit.Text(stage.T("screen.board.party").Replace("{n}", count.ToString(inv)).Replace("{max}", me.OpenSlots.ToString(inv)), Kit.F2), null, "kit-title");
            // [정비] 손잡이(04 §3-5 — 단축키 T): 내 턴이 아니어도, 걷기·턴 넘김 연출 중에도 열린다. 결정 125: 막 카드 같은 전원 차단 연출 중 · 창이 떠 있는 동안에도 열리고
            // 그때는 보기만 하는 판(흐린 단추 + 까닭 툴팁 — 누르면 정보 창 층에 연다)
            bool canMaint = flow.CanOpenParty, peek = flow.CanPeek;
            var maint = Add(partyLeft, Kit.Button(stage.T("screen.board.maint"), KitButtonKind.Normal, () => flow.ToggleParty()), "board-hud-maint", null);   // 다시 누르면 닫힌다(결정 176 — T와 같은 함수)
            Peek(maint, peek, canMaint, "screen.board.maint.tip");
            paintedMaint = canMaint;
            // [트레이너 카드] → 트레이너 카드 판(S40). 트레이너 카드를 끈 판에서는 숨긴다. 여는 조건은 [정비]와 같다
            if (flow.HasTrainerCard(client.Seat))
            {
                var card = Add(partyLeft, Kit.Button(stage.T("screen.board.trainer"), KitButtonKind.Normal, () => flow.ToggleTrainer()), "board-hud-trainer", null);
                Peek(card, peek, canMaint, "screen.board.trainer.tip");
            }
            // [운명의 변화 n/5] → 손패 창(S30 — 결정 75 A2 가). 장수는 카드가 손에 닿을 때 바뀐다(표시 값). 여는 조건은 [정비]와 같다
            paintedFate = ShownFate(me, client.Seat);
            var fate = Add(partyLeft, Kit.Button(stage.T("screen.board.fate").Replace("{n}", paintedFate.ToString(inv)).Replace("{max}", FateRules.HandLimit.ToString(inv)), KitButtonKind.Normal, () => flow.ToggleFate()), FateButton, null);
            Peek(fate, peek, flow.CanOpenFate, "screen.board.fate.tip");
            // 칸 정보 줄(04 §3-6 — 목업 r3 「숲 · 온대 · 높이 1 · 초록 규토리 나무」): 마우스를 올린 칸, 없으면 고른 칸, 그것도 없으면 내가 선 칸(글은 아래에서 고른 칸이 정해진 뒤 넣는다). 목업은 「행동」 머리 아래 줄이지만 판이 낮아 파티 머리 줄의 오른쪽 끝에 둔다 [가정]
            cellInfo = Add(partyHead, Kit.Text("", Kit.F2), "board-hud-cell", "sn-hud-cell");
            stage.Tips.Hook(cellInfo, null, stage.T("screen.cell.info.tip"), TipSide.Above, reading: true, clear: dock);
            var tiles = Add(party, new VisualElement(), null, "sn-hud-mons");
            bool many = PokemonRules.PartyCount(me) >= ManyMons;
            if (many) tiles.AddToClassList("sn-hud-mons-many");   // 다섯 이상(2막부터): 도트 칸을 줄여 이름·체력 자리를 남긴다
            var mons = MonArt.Load();
            var heart = GenAssets.Load<Texture2D>(KitGallery.HealthIconKey);
            var speed = GenAssets.Load<Texture2D>(KitGallery.SpeedIconKey);
            for (int i = 0; i < me.Party.Length; i++)
            {
                int id = me.Party[i];
                if (id < 0) continue;
                var pk = view.Pokemon[id];
                var def = ctx.Cards.Pokemon[EvolveScreen.ShownDef(flow, id, pk.Def)];   // 진화 연출(39행)이 착지하기 전에는 진화 전 모습
                var tile = Add(tiles, new VisualElement(), "board-hud-mon-" + i.ToString(inv), "sn-hud-mon");
                tile.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
                // 상태 이상(결정 180): 배틀과 같은 Pt 상태 그림을 도트 왼쪽 위에 세로로 얹는다(칸의 배치 밖 — 칸 폭 · 높이가 그대로, 마우스 오버 = 이름 + 효과)
                var status = new VisualElement { name = "board-hud-mon-" + i.ToString(inv) + "-status" };
                status.AddToClassList("sn-hud-mon-status");
                if (MonParts.StatusIcons(stage, status, pk, status.name, TipSide.Above, dock) > 0) tile.Add(status);
                // 회복(8·17행)의 반짝임 — 칸을 덮는 흰 판(마우스를 받지 않는다, 연출이 opacity만 바꾼다)
                FxUi.Fade(Add(tile, new VisualElement { pickingMode = PickingMode.Ignore }, "board-hud-mon-" + i.ToString(inv) + "-flash", "sn-hud-mon-flash"), 0f);
                var info = Add(tile, new VisualElement(), null, "sn-hud-mon-info");
                string fullName = stage.T("pokemon." + def.Key);
                var nameLabel = Add(info, Kit.Text(ShortName(stage, flow.Lang, def.Key), Kit.F2), "board-hud-mon-" + i.ToString(inv) + "-name", "sn-hud-mon-name");   // 폼 꼬리 「(오리진폼)」는 칸에서 빼고 마우스 오버에(M5 「운명·응답 화면」 — 사용자 지적 11-16. 넷 이하도 같게 — 1080에서 「디아루가 (신오의…」로 잘렸다)
                if (nameLabel.text != fullName) stage.Tips.Hook(nameLabel, fullName, null, TipSide.Above);
                var stats = Add(info, new VisualElement(), null, "sn-pick-line");
                int max = PokemonRules.MaxHp(ctx, pk);
                int hpNow = PokemonRules.Hp(ctx, pk);
                int grown = ctx.Cards.Pokemon[pk.Def].Health - def.Health;   // 진화 연출이 착지하기 전: 체력도 진화 전 값(받은 데미지는 그대로 — 도트·이름만 옛 모습이고 체력은 벌써 「5/7」이었다)
                if (grown != 0)
                {
                    max -= grown;
                    hpNow = Math.Max(0, hpNow - grown);
                }
                int hp = flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Hp(id), hpNow) : hpNow;
                var hpPill = Kit.Pill(heart, hp.ToString(inv), hp * 2 <= max ? KitTone.Low : KitTone.Base, max.ToString(inv));
                var spPill = Kit.Pill(speed, def.Initiative.ToString(inv));
                hpPill.name = "board-hud-mon-" + i.ToString(inv) + "-hp";
                spPill.name = "board-hud-mon-" + i.ToString(inv) + "-speed";
                stats.Add(hpPill);
                stats.Add(spPill);   // 다섯 이상이면 체력·스피드를 세로로 쌓는다(USS sn-hud-mons-many — 숫자가 잘리지 않게)
                stage.Tips.Hook(hpPill, stage.T("screen.common.health"), null, TipSide.Above);
                stage.Tips.Hook(spPill, stage.T("screen.common.speed"), null, TipSide.Above);
                // 패시브(T23 · T27): 우두머리 · 비스트 부스트 — 정비 판 카드와 같은 작은 칩(마우스 오버 = 효과 글). 넷 이하 = 체력 · 스피드 아래 한 줄,
                // 다섯 이상 = 칸(도트 + 글) 아래 끝 한 줄(USS — 칸 폭을 넓히지 않고 넘치면 「…」)
                var passive = new VisualElement { name = "board-hud-mon-" + i.ToString(inv) + "-passive" };
                passive.AddToClassList("sn-hud-mon-passive");
                if (MonParts.Passives(stage, passive, pk, true, passive.name, true, TipSide.Above, dock) > 0)
                {
                    (many ? tile : info).Add(passive);
                    tile.AddToClassList("sn-hud-mon-passives");
                }
                MonTag(info, me, i, pk);   // 꼬리표는 체력·스피드 아래(결정 87 V1 ① — 도트 발을 덮었다)
            }

            // 행동 판
            actions.Clear();
            shownActs.Clear();
            cellInfo.text = CellText(InfoCell(me.Cell));
            // S41 응답 프롬프트(결정 75 A3 가 — 목업 C안: 「행동」 칸 자리): 남의 거래 제안 · 도전 응답. 내 대기열이 빈 뒤부터(98행)
            FillBlessAsk(view);
            if (open && choice != null && choice.Kind == ChoiceKind.Respond && choice.Sub != ChoiceSub.Blessing && !BlessScreen.Owns(choice))
            {
                reach.Clear();
                picked = -1;
                vias.Clear();
                warps.Clear();
                FillPrompt(view, choice);
                Finish();
                return;
            }
            Prompting = false;
            var head = Add(actions, new VisualElement(), null, "sn-hud-line");
            Add(head, Kit.Text(stage.T("screen.board.actions"), Kit.F2), null, "kit-title").AddToClassList("sn-hud-actions-title");
            if (mine)
            {
                var moves = Add(head, Kit.Badge(stage.T("screen.board.moves_left").Replace("{n}", ShownMoves(me).ToString(inv)), KitBadgeKind.Gold, Kit.F2), "board-hud-moves", null);
                stage.Tips.Hook(moves, null, stage.T("screen.board.moves_left.tip"), TipSide.Above, reading: true, clear: dock);
            }
            // 앰프 · 장작(결정 87 V9 — 카드 판을 열지 않고 본다). 칩이 서면 영어에서는 「Actions」 머리 글을 뺀다(USS sn-lang-en — [스탬프]와 겹친다, M9 lang3)
            int stateChips = CardAsk.StateChips(stage, flow.Ctx, head, me, CardState, TipSide.Above, dock);
            if (me.Building)   // worker B 도로 켬(이번 턴 — 내 HUD에도)
            {
                var road = Add(head, Kit.Badge(stage.T("screen.trainer.state.road"), KitBadgeKind.Plain, Kit.F2), CardState + "-road", null);
                stage.Tips.Hook(road, stage.T("trainer_ability.worker.1"), stage.TOrNull("trainer_ability.worker.1.text"), TipSide.Above, reading: true, clear: dock);
                stateChips++;
            }
            if (stateChips > 0) head.AddToClassList("sn-hud-actions-card");
            // [무르기](결정 149 · 154 — undo.md §3-1): [스탬프] 바로 왼쪽. 무르기를 켠 판에만 서고, 걷기 전 확인 중(오른쪽 클릭 안내)에도 남는다.
            // 켜짐 = 무를 수 있고 입력이 열렸다 · 못 무르면 흐린 단추(OffClass — 소리 없음) + 까닭 툴팁 · 연출 · 창 · 메뉴 중이면 꺼진 단추
            undoButton = null;
            if (flow.UndoShown)
            {
                undoButton = Add(head, Kit.Button(stage.T("screen.board.undo"), KitButtonKind.Normal, () => { if (flow.UndoOpen) flow.PressUndo(); }), UndoButton, "sn-hud-hint");
                if (flow.UndoOpen) stage.Tips.Hook(undoButton, null, stage.T("screen.board.undo.tip"), TipSide.Above, reading: true, clear: dock);
                else if (open && flow.UndoBlockText != null)
                {
                    undoButton.AddToClassList(UiStage.OffClass);
                    stage.Tips.Hook(undoButton, null, flow.UndoBlockText, TipSide.Above, reading: true, clear: dock);
                }
                else undoButton.SetEnabled(false);
            }
            // [스탬프 n/7] → 스탬프 판(S37): 「행동」 머리 줄의 오른쪽 끝(파티 머리 줄은 1080에서 칸 정보 줄까지 차 있다). 여는 조건은 [정비]와 같다.
            // 걷기 전 확인 중에는 이 자리에 오른쪽 클릭 안내가 선다(아래 MoveHint — 그동안은 숨긴다)
            stampButton = Add(head, Kit.Button(stage.T("screen.board.stamp").Replace("{n}", StampRules.Count(me).ToString(inv)), KitButtonKind.Normal, () => flow.ToggleStamp()), StampButton, "sn-hud-hint");
            if (undoButton != null) stampButton.AddToClassList(AfterUndoClass);   // [무르기]가 오른쪽으로 민다 — 둘이 붙어 선다
            stampButton.SetEnabled(paintedMaint || flow.StampOpen);   // 내 스탬프 판이 떠 있으면 다시 눌러 닫는다(결정 176)
            if (paintedMaint) stage.Tips.Hook(stampButton, null, stage.T("screen.board.stamp.tip"), TipSide.Above, reading: true, clear: dock);

            var used = Add(actions, new VisualElement(), "board-hud-used", "sn-hud-line");
            Add(used, Kit.Text(stage.T("screen.board.this_turn"), Kit.F2), null, "kit-muted");
            int marks = 0;
            foreach (var u in view.Used)
            {
                if (u.Seat != client.Seat || u.Scope != view.TurnId) continue;
                string name = UsedName(u.Kind);
                if (name == null) continue;
                var chip = Kit.Badge(stage.T("screen.board.used." + name), KitBadgeKind.Ok, Kit.F2);
                used.Add(chip);
                stage.Tips.Hook(chip, null, stage.T("screen.board.used.tip"), TipSide.Above, reading: true, clear: dock);
                marks++;
            }
            if (marks == 0) used.Add(Kit.Badge(stage.T("screen.board.used.none"), KitBadgeKind.Plain, Kit.F2));

            List<ChoiceAnswer> legal = choice != null ? client.Legal(choice) : null;
            reach.Clear();
            string guide;
            Act[] acts = null;
            // 능력 고르기는 그 맵 행동 선택이 열려 있는 동안만 — 선택이 바뀌었거나 답이 사라졌으면 푼다. 입력이 잠깐 닫힌 것(남 좌석의 갱신 · 창 · 짧은 연출)으로는 풀지 않는다(그리기만 쉰다 — M6 화면 교차 검토 #6)
            if (abilityPicks.Count > 0 && (choice == null || choice.Kind != ChoiceKind.MapAction || choice.Id != abilityChoice || warps.Count > 0)) abilityPicks.Clear();
            if (abilityPicks.Count > 0)
            {
                abilityPicks.RemoveAll(a => !legal.Exists(x => x.SameAs(a)));   // 거래 표(합법 답이 아님)도 빠진다 — 아래에서 다시 센다
                if (abilityPicks.Count > 0) AddTradeMarks(view, client.Seat);
            }
            if (abilityPicks.Count == 0)
            {
                abilityCell = -1;
                reachPath.Clear();
            }
            abilityShown.Clear();
            abilityRows.Clear();
            abilityPageStart.Clear();
            abilityTall = false;
            dock.RemoveFromClassList(TallClass);
            abilityLabels.Clear();
            cardAnswers.Clear();
            CardAsking = false;
            CardCells = false;
            string cardHead = null, cardTip = null;
            if (flow.TradeWaiting) guide = "screen.board.guide.trade_wait";   // 내 거래 제안에 상대가 답하는 중(S41은 상대 화면에)
            else if (choice != null && BreachScreen.Owns(choice)) guide = BreachScreen.Releasing(view) ? "screen.board.guide.breach_release" : "screen.board.guide.breach";   // 균열 카드는 가운데 창(S58)이 받는다
            else if (choice != null && (BlessScreen.Owns(choice) || (choice.Kind == ChoiceKind.Respond && choice.Sub == ChoiceSub.Blessing))) guide = "screen.board.guide.bless";   // 축복 물음 · 동의는 축복 창 · 위 띠 물음이 받는다
            else if (!open || choice == null || choice.Kind == ChoiceKind.Discard || (choice.Kind == ChoiceKind.Reward && choice.Sub == ChoiceSub.TrainerReward) || GameFlow.ShrineWaits(choice) || choice.Kind == ChoiceKind.Ending || EvolveScreen.Owns(choice) || CardRewardScreen.Takes(flow, choice)) guide = "screen.board.guide.wait";   // 운명 손패 버리기는 손패 창(S30)이 위에서 받는다 · 트레이너전 승리 보상·사당 카드의 고르기는 화면이 생길 때까지 GameFlow가 기본값으로 넘긴다
            else if (choice.Kind == ChoiceKind.MapAction && warps.Count > 0)
            {
                // 펄기아 순간이동(14행): 갈 수 있는 칸 = 고른 효과의 답들의 칸. 칸을 누르면 그 답을 낸다
                foreach (var a in warps) reach.Add(a.Values[0]);
                guide = "screen.board.guide.warp";
            }
            else if (choice.Kind == ChoiceKind.MapAction && abilityPicks.Count > 0)
            {
                // 능력 고르기: 칸을 고르는 답이면 그 칸들을 칠하고(누르면 그 칸의 답), 값 단추는 칸을 고른 뒤 · 칸이 없는 답(규토리 색 · 날씨)이면 바로
                bool cells = false;
                foreach (var a in abilityPicks)
                {
                    int c = AbilityCellOf(a);
                    if (c < 0) continue;
                    cells = true;
                    if (!reach.Contains(c)) reach.Add(c);
                }
                foreach (var a in abilityPicks)
                    if (!cells || (abilityCell >= 0 && AbilityCellOf(a) == abilityCell)) abilityShown.Add(a);
                SplitRows();
                guide = cells && abilityCell < 0 ? "screen.card.guide.cell" : "screen.card.guide.pick";
                int card = CardAsk.CardOf(view, client.Seat);
                cardHead = abilityWeather ? stage.T("item.weather_wheel") : CardAsk.Head(stage, flow.Ctx, card, 1);
                cardTip = abilityWeather ? stage.T("screen.card.weather.tip") : CardAsk.Text(stage, flow.Ctx, card, 1);
            }
            else if (BoardCardAsk(choice) && !(flow.Stage.Current is BattleScreen) && !(flow.Stage.Current is CardSwapScreen) && (view.Battle == null || (flow.Battle != null && flow.Battle.Ended) || flow.BoardPickInBattle))   // 배틀 안의 카드 물음은 배틀 독이 받는다 — 배틀 화면이 서기 전 틈에도 HUD가 받지 않는다(M6 화면 교차 검토 확인 C)
            {
                // 보드에서 열린 카드 물음(「When unlocked」의 고르기 · ruin_maniac 붙일 포켓몬 · backpacker 회복 · interviewers 「가서 도전할까」): 단추 = 합법 답 그대로
                // team_rocket R 날아갈 칸(값 = 칸 351개): 단추 대신 칸을 칠하고 칸을 누르면 그 답(배틀 화면은 그동안 걷혀 있다 — GameFlow.BoardPickInBattle) · [넘기기]만 단추
                CardAsking = true;
                cardAnswers.AddRange(legal);
                CardCells = CardAsk.KindOf(flow.Ctx, choice) == CardAsk.Value.Cell;
                if (CardCells) foreach (var a in legal) if (a.Kind == AnswerKind.Pick && !reach.Contains(a.Target)) reach.Add(a.Target);
                guide = choice.Sub == ChoiceSub.EffectOption ? "screen.card.guide.option" : CardCells ? "screen.card.guide.cell" : "screen.card.guide.pick";
                cardHead = CardAsk.Head(stage, flow.Ctx, choice);
                cardTip = CardAsk.Text(stage, flow.Ctx, choice);
            }
            else if (choice.Kind == ChoiceKind.MapAction)
            {
                foreach (var a in legal) if (a.Kind == AnswerKind.Move) reach.Add(a.Target);
                guide = reach.Count > 0 ? "screen.board.guide.move" : "screen.board.guide.act";
                acts = MapActs;
            }
            else if (choice.Kind == ChoiceKind.HiddenSpot)
            {
                guide = choice.Options.Count > 0 ? "screen.board.guide.hidden_spot_walk" : "screen.board.guide.hidden_spot";   // 결정 71 ③: 고른 길 가운데서 멈췄다
                acts = SpotActs;
            }
            else
            {
                guide = "screen.board.guide.not_ready";
                flow.NoteNotReady();
            }
            // 걷기 전 확인: 고른 칸은 입력이 열려 있고 아직 갈 수 있을 때만 남는다. 남아 있으면 안내 줄이 「이동력 n 사용 · m 남음」으로 바뀌고 단추 줄 맨 앞에 [이동]이 선다
            if (warps.Count > 0 && (choice == null || choice.Kind != ChoiceKind.MapAction)) warps.Clear();   // 입력이 잠깐 닫혀도 순간이동 고르기는 남긴다(#6)
            if (warps.Count > 0) picked = -1;
            if (picked >= 0 && (!open || !reach.Contains(picked))) picked = -1;
            var pickedPath = PickedPath;
            bool gatePicked = picked >= 0 && choice != null && choice.Kind == ChoiceKind.MapAction && GateTo(choice, picked) != null;   // 왜곡 게이트 건너편(15행): 걷는 길이 없다
            if (gatePicked) vias.Clear();
            else if (pickedPath == null) picked = -1;
            if (picked < 0) vias.Clear();
            int stopCount = picked >= 0 && !gatePicked ? StopsOf(pickedPath).Count : 0;   // 결정 71 ①: 길 위의 숨겨진 장소 수
            int pickedCost = picked >= 0 && !gatePicked ? PathCostOf(pickedPath) : 0;   // 길·활공은 칸 수와 다르다 · 무료 지형 이동은 0(M6 — MoveRules.MoveTerms)
            string guideText = picked < 0 ? stage.T(guide) : gatePicked ? stage.T("screen.board.guide.gate")
                : IsFree(pickedPath) ? stage.T("screen.board.guide.picked_free").Replace("{n}", (pickedPath.Count - 1).ToString(inv)).Replace("{k}", stopCount.ToString(inv))
                : stage.T(stopCount > 0 ? "screen.board.guide.picked_spots" : "screen.board.guide.picked").Replace("{n}", pickedCost.ToString(inv))
                    .Replace("{left}", (me.MovesLeft - pickedCost).ToString(inv)).Replace("{k}", stopCount.ToString(inv));
            if (cardHead != null)
            {
                used.style.display = DisplayStyle.None;   // 판 높이가 그대로라(1080에서 줄이 넘쳤다) 고르는 동안은 「이번 턴」 줄 자리에 머리를 둔다
                var headLine = Add(actions, Kit.Text(cardHead, Kit.F2), CardHead, "sn-hud-card-head");
                stage.Tips.Hook(headLine, null, cardTip, TipSide.Above, reading: true, clear: dock);
            }
            var guideLine = Add(actions, Kit.Text(guideText, Kit.F2), "board-hud-guide", "sn-hud-guide");
            if (stopCount > 0) stage.Tips.Hook(guideLine, stage.T("screen.board.alpha_warn"), AlphaWarnTip(choice), TipSide.Above, reading: true, clear: dock);

            var buttons = Add(actions, new VisualElement(), "board-hud-buttons", "sn-hud-buttons");
            // 배치가 서면 실제 줄 수로 아래 판 높이를 정한다 — 값 단추(글자 수 어림은 1080 벌 기준이라 720 벌은 한 줄에 드는데도 높아졌다)와
            // 영어 행동 단추(1080 벌에서 [마을] · [제작] · [진화] · [도전] · [턴 종료]가 두 줄 — M9 언어 ②). 판 높이는 단추 줄 폭을 바꾸지 않는다
            buttons.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (buttons.panel != null && buttons.childCount > 0) dock.EnableInClassList(TallClass, Wraps(buttons));
            });
            if (abilityPicks.Count > 0)
            {
                dock.EnableInClassList(TallClass, abilityTall);   // 값 단추가 두 줄이면 아래 판을 한 줄 높인다(결정 87 V2 나)
                // 고르기의 값 단추 + [취소](이름의 cancel 마디로 무음)
                // psychic B: 값 단추에 안쪽 답의 값(색 · 목적지 · 은행 줄 · 상대)을 싣고, 같은 종류가 여럿이면 묶음 단추 — 같은 이름이 두 번 서지 않는다(남으면 번호)
                bool reaching = MyCardKey == "psychic" && !abilityWeather && abilityCell >= 0;
                int depth = reachPath.Count;
                int from = AbilityFrom, to = AbilityTo;
                for (int i = from; i < to; i++)
                {
                    int row = i;
                    var a = abilityRows[i][0];
                    bool leaf = abilityRows[i].Count == 1;
                    string label = RowLabel(abilityRows[i], reaching, depth);
                    string plain = label;
                    for (int n = 2; abilityLabels.Contains(label); n++) label = plain + " " + n.ToString(inv);
                    abilityLabels.Add(label);
                    int c = AbilityCellOf(a);
                    var b = Add(buttons, CardAsk.Button(label, leaf ? CardAsk.MapIconKey(MyCardKey, a) : null, KitButtonKind.Main, () => PressAbilityRow(row)), "board-hud-ability-" + (i + 1).ToString(inv), null);
                    b.SetEnabled(open);
                    if (open && c >= 0) stage.Tips.Hook(b, null, CardAsk.IsTradeMark(a) ? stage.T("screen.trade.reach.tip") : CellText(c), TipSide.Above, reading: true, clear: dock);
                }
                if (AbilityPaged)
                {
                    // 두 줄에도 못 드는 것만 쪽 — [다음 n/m]
                    var next = Add(buttons, Kit.Button(stage.T("screen.battle.partymoves.next").Replace("{n}", (abilityPage + 1).ToString(inv)).Replace("{max}", AbilityPages.ToString(inv)), KitButtonKind.Normal, () => NextAbilityPage()), AbilityNext, null);
                    next.SetEnabled(open);
                }
                // 묶음 안에서는 [취소] 자리가 [뒤로](한 단계 나온다 — 오른쪽 클릭·Esc와 같다)
                var stop = depth > 0
                    ? Add(buttons, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, () => CancelAbility(false)), AbilityBack, null)
                    : Add(buttons, Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, () => CancelAbility()), AbilityCancel, null);
                stop.SetEnabled(open);
                acts = null;
            }
            if (CardAsking)
            {
                for (int i = 0; i < cardAnswers.Count; i++)
                {
                    int index = i;
                    var a = cardAnswers[i];
                    if (CardCells && a.Kind == AnswerKind.Pick) continue;   // 칸은 보드에서 고른다
                    bool pick = a.Kind == AnswerKind.Pick;
                    string label = a.Kind == AnswerKind.Accept ? stage.T("screen.battle.ask.yes") : a.Kind == AnswerKind.Decline ? stage.T("screen.battle.fate.pass") : CardAsk.PickLabel(stage, flow.Ctx, view, choice, a.Target);
                    var b = Add(buttons, CardAsk.Button(label, pick ? CardAsk.IconKey(flow.Ctx, choice, a.Target) : null, a.Kind == AnswerKind.Decline ? KitButtonKind.Normal : KitButtonKind.Main, () => PressCard(index)), "board-hud-card-" + (i + 1).ToString(inv), null);
                    b.SetEnabled(open);
                    string tip = pick ? CardAsk.CardTip(stage, flow.Ctx, choice, a.Target) : null;   // youngster R 바꿀 카드: 두 능력 이름
                    if (open && tip != null) stage.Tips.Hook(b, label, tip, TipSide.Above, reading: true, clear: dock);
                }
            }
            // 연출 중에도 단추 자리는 그대로 둔다(꺼진 채) — 판이 깜빡이지 않게. 선택이 닫혀 있으면 마지막 맵 행동 모양을 흐리게 둘 것이 없어 비운다
            if (acts == null && choice != null && !open) acts = choice.Kind == ChoiceKind.MapAction ? MapActs : choice.Kind == ChoiceKind.HiddenSpot ? SpotActs : null;
            if (warps.Count > 0)
            {
                // [취소] = 순간이동을 그만두고 손패 창으로(카드는 아직 쓰지 않았다). 이름의 cancel 마디로 무음
                var stop = Add(buttons, Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, () => CancelWarp()), WarpCancel, null);
                stage.Tips.Hook(stop, null, stage.T("screen.board.warp_cancel.tip"), TipSide.Above, reading: true, clear: dock);
                acts = null;
            }
            if (picked >= 0)
            {
                var go = Add(buttons, Kit.Button(stage.T("screen.board.action.move"), KitButtonKind.Main, () => ConfirmMove()), MoveGo, null);
                stage.Tips.Hook(go, null, stage.T("screen.board.action.move.tip"), TipSide.Above, reading: true, clear: dock);
                // 결정 73 ③: [취소] = 고른 칸·거쳐 갈 칸을 모두 푼다(오른쪽 클릭·Esc는 하나씩 — Back). 이름의 cancel 마디로 누르는 소리는 무음(결정 70 ⑯)
                var cancel = Add(buttons, Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, () => Unpick()), MoveCancel, null);
                stage.Tips.Hook(cancel, null, stage.T("screen.board.action.move_cancel.tip"), TipSide.Above, reading: true, clear: dock);
                // 무료 지형 이동 ↔ 이동력으로 걷기(감사 G2-2 · G3-2 — rulings A36 「쓸지·언제 쓸지는 플레이어가 고른다」 [가정 #660]): 지금 걸음의 반대를 단추로
                if (!gatePicked && FreeChoosable)
                {
                    string sw = paidCell == picked ? "screen.board.action.move_free" : "screen.board.action.move_paid";
                    var toggle = Add(buttons, Kit.Button(stage.T(sw), KitButtonKind.Normal, () => ToggleFree()), MoveFree, null);
                    stage.Tips.Hook(toggle, null, stage.T(sw + ".tip"), TipSide.Above, reading: true, clear: dock);
                }
                // 「우클릭도 된다고 안내하나 필요해」: 고른 동안 「행동」 머리 줄 오른쪽 끝에 오른쪽 클릭 안내(단추 줄은 [이동]·[취소]·행동 단추로 차서 줄이 넘친다)
                stampButton.RemoveFromHierarchy();
                // 카드 칩(앰프 · 장작 · 도로)과 [무르기]가 함께 선 머리 줄에는 안내가 들지 않는다(1080 벌 — 칩 하나만큼 넘친다): 안내는 빼고 [무르기]가 오른쪽 끝에 선다.
                // 같은 말은 [취소] 툴팁에 있다(screen.board.action.move_cancel.tip) [가정 — 무르기 D1 2차]
                if (undoButton == null || stateChips == 0)
                {
                    var hint = Add(head, Kit.Text(stage.T("screen.board.move_hint"), Kit.F2), MoveHint, "sn-hud-hint");
                    hint.AddToClassList("kit-muted");
                    if (undoButton != null) hint.AddToClassList(AfterUndoClass);
                }
            }
            bool atVillage = false, camp = false;
            if (acts == MapActs && flow.AtVillage(out _, out int villageHere))
            {
                atVillage = true;
                camp = villageHere < 0;
            }
            // [거래](S36): 거래할 상대가 있을 때 — 내 턴의 맵 행동 중 · 남의 턴(같은 신의 추종자, 결정 75 B5 나). 순간이동·걷기 확인·능력 고르기 중에는 올리지 않는다(염력 거래는 값 단추 「거래 ○○」 — 1080 벌 한 줄)
            if (open && warps.Count == 0 && picked < 0 && abilityPicks.Count == 0 && (choice == null || choice.Kind == ChoiceKind.MapAction) && flow.TradePartners().Count > 0)
            {
                var trade = Add(buttons, Kit.Button(stage.T("screen.board.action.trade"), KitButtonKind.Normal, () => Press(Trade)), "board-hud-act-" + Trade, null);
                stage.Tips.Hook(trade, null, stage.T("screen.board.action.trade.tip"), TipSide.Above, reading: true, clear: dock);
                shownActs.Add(Trade);
            }
            if (acts != null && legal != null)
                foreach (var a in acts)
                {
                    // 트레이너 카드의 맵 능력 · 날씨 휠: [턴 종료] 바로 앞(합법 답에 UseAbility가 있을 때 — 무리마다 단추 하나)
                    if (a.Kind == AnswerKind.EndTurn && acts == MapActs)
                    {
                        if (legal.Exists(x => x.Kind == AnswerKind.UseAbility && !CardAsk.IsWeather(x))) AbilityButtonOf(buttons, false, open);
                        if (legal.Exists(CardAsk.IsWeather)) AbilityButtonOf(buttons, true, open);
                        if (legal.Exists(IsDive)) AbilityButtonOf(buttons, false, open, true);
                    }
                    bool village = a.Kind == AnswerKind.VillageHeal;   // [마을]은 서 있는 칸으로, 제작·진화 창은 합법 답으로 올린다
                    if (village ? !atVillage : !legal.Exists(x => x.Kind == a.Kind)) continue;
                    if (a.Kind == AnswerKind.Fly && atVillage) continue;   // 마을에 서 있으면 공중날기는 [마을] 안에서
                    string id = a.Id;
                    // [마을]은 야영지에 서 있으면 「야영지」다
                    string label = village && camp ? "screen.board.action." + id + ".camp" : "screen.board.action." + id;
                    var button = Add(buttons, Kit.Button(stage.T(label), a.Look, () => Press(id)), "board-hud-act-" + id, null);
                    // 툴팁: 입력이 닫힌 동안(연출 중 — [턴 종료]를 누른 뒤 마우스가 그대로 단추 위에 있을 때)은 걸지 않는다. 입력이 열려 판을 다시 그릴 때 걸린다.
                    // 판 안의 「행동 · 이번 턴 · 안내」 줄을 덮지 않게 아래 판 밖(위)에 띄운다(clear)
                    if (!a.Ready)
                    {
                        // 꺼진 요소는 마우스를 받지 못해 툴팁이 뜨지 않는다 — 켜 둔 채 흐린 모양만 입히고 누름은 Press가 거른다
                        button.AddToClassList("sn-hud-off");
                        if (open) stage.Tips.Hook(button, stage.T("screen.board.action." + id), stage.T("screen.common.not_ready"), TipSide.Above, reading: true, clear: dock);
                    }
                    else
                    {
                        button.SetEnabled(open);
                        // 결정 71 ③: 고른 길 가운데의 물음이면 [공개]·[지나가기]의 툴팁이 남은 길을 말한다
                        bool walking = acts == SpotActs && choice != null && choice.Kind == ChoiceKind.HiddenSpot && choice.Options.Count > 0;
                        if (open) stage.Tips.Hook(button, null, stage.T("screen.board.action." + id + (walking ? ".walk.tip" : ".tip")), TipSide.Above, reading: true, clear: dock);
                    }
                    shownActs.Add(id);
                }

            Finish();
        }

        // Refresh의 끝(행동 판을 그린 뒤 — 프롬프트도 같이): 이동 표시 · 기록 · 공개 정보 카드 · 회복 반짝임
        void Finish()
        {
            paintedHp = PartyHp();
            if (hover >= 0 && !reach.Contains(hover)) hover = -1;
            Mark();
            PaintLog(true);
            // 떠 있는 공개 정보 카드는 새 값으로 다시 그린다(좌석 칩은 위에서 다시 만들었다)
            if (infoSeat >= 0) ShowSeatInfo(infoSeat);
            else if (infoCell >= 0) ShowCellInfo(infoCell);
            // 다시 지은 파티 칸에 지금 시각의 반짝임을 입힌다(회복 연출 중에 다시 그려도 한 프레임 꺼지지 않게)
            if (flow.Fx != null) ShowHeal(flow.Fx.Sequencer);
        }

        // ── 사당·균열(M5 「사당·균열 화면」) ──

        /// <summary>33·34행의 어두워진 하늘이 귀환 암전(71행)에 견준 세기.</summary>
        public const float ShrineDark = 0.45f;

        ShrineFx shrineFx;
        int paintedFollowers;

        /// <summary>사당·균열의 보드 쪽 연출 층(33·34행 · 나눠 받는 빛).</summary>
        public ShrineFx ShrineFx => shrineFx;

        /// <summary>좌석 칩에 지금 그리는 추종자(없음 · 뒷면이면 None): 공개된 값 — 비밀 선택 동안과 공개 연출(340)이 뒤집기 전에는 가린다.</summary>
        public SinnohGod ShownFollower(GameState view, int seat)
        {
            var god = view.Seats[seat].Follower;
            if (god != SinnohGod.Dialga && god != SinnohGod.Palkia && god != SinnohGod.Giratina && god != SinnohGod.Arceus) return SinnohGod.None;   // 아르세우스의 추종자(undivided_acolyte B — 세 신 모두로 센다, M6)
            if (FractureRules.Choosing(view) || ShrineRows.Pending(flow.Fx != null ? flow.Fx.Sequencer : null, ShrineRows.Tally) != null) return SinnohGod.None;
            return god;
        }

        /// <summary>그 좌석 칩에 추종자 아이콘이 있나(확인용).</summary>
        public bool FollowerShown(int seat) => seats != null && seats.Q("board-hud-seat-" + (seat + 1).ToString(CultureInfo.InvariantCulture) + "-follower") != null;

        int FollowerMask(GameState view)
        {
            int mask = 0;
            for (int s = 0; s < view.Seats.Length; s++) mask = mask * 4 + (int)ShownFollower(view, s);
            return mask;
        }

        void ShowShrine(FxSequencer seq)
        {
            if (shrineFx == null) return;
            var panel = stage.Root != null ? stage.Root.Q("board-shrine-card-1") : null;
            shrineFx.Show(seq, s => seats != null ? seats.Q("board-hud-seat-" + (s + 1).ToString(CultureInfo.InvariantCulture)) : null, panel);
            var view = flow.Client != null ? flow.Client.View : null;
            if (view != null && FollowerMask(view) != paintedFollowers) Refresh();   // 추종자가 공개됐다(340): 좌석 칩의 신 아이콘
        }

        // ── 운명의 변화 · 응답 프롬프트(M5 「운명·응답 화면」) ──

        // [운명의 변화]에 적는 장수: 카드가 손에 닿을 때 바뀐다(표시 값 — 보기는 받는 순간 이미 다음 값)
        int ShownFate(SeatState me, int seat) => flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.FateHand(seat), me.FateHand.Count) : me.FateHand.Count;

        /// <summary>좁은 파티 칸에 적는 이름: 폼 꼬리 「(…)」를 뺀다(전체는 마우스 오버).</summary>
        public static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int at = name.IndexOf('(');
            return at > 0 ? name.Substring(0, at).TrimEnd() : name;
        }

        /// <summary>파티 칸의 포켓몬 이름: <see cref="ShortName(string)"/> + 영어는 앞에 붙는 폼을 뺀 종 이름(「Origin Giratina」 → 「Giratina」 — 배틀 이름과 같은 규칙, M9 lang3). 전체는 마우스 오버.</summary>
        public static string ShortName(UiStage stage, string lang, string defKey)
        {
            string name = ShortName(stage.T("pokemon." + defKey));
            if (lang != "en") return name;
            int digits = 0;
            while (digits < defKey.Length && char.IsDigit(defKey[digits])) digits++;
            string species = digits > 0 ? stage.TOrNull("species." + defKey.Substring(0, digits)) : null;
            return !string.IsNullOrEmpty(species) && name.Length > species.Length && name.EndsWith(" " + species, StringComparison.Ordinal) ? species : name;
        }

        /// <summary>펄기아(손패 창의 [1장 사용]·[3장 사용]): 그 답들의 칸을 갈 수 있는 칸으로 칠하고 누르기를 기다린다.</summary>
        public void BeginWarp(List<ChoiceAnswer> answers)
        {
            warps.Clear();
            if (answers != null)
                foreach (var a in answers)
                    if (a.Kind == AnswerKind.PlayFate && a.Values.Count > 0 && a.Values[0] >= 0) warps.Add(a);
            picked = -1;
            vias.Clear();
            Refresh();
        }

        /// <summary>순간이동 칸 고르기를 그만둔다(카드는 쓰지 않았다). 고르는 중이었으면 true.</summary>
        public bool CancelWarp()
        {
            if (warps.Count == 0) return false;
            warps.Clear();
            hover = -1;
            Refresh();
            return true;
        }

        /// <summary>순간이동할 칸을 누른다: 그 칸의 답을 낸다. 갈 수 없는 칸이면 아무것도 하지 않고 false.</summary>
        public bool Warp(int cell)
        {
            if (!flow.InputOpen || warps.Count == 0) return false;
            var answer = warps.Find(a => a.Values[0] == cell);
            var choice = flow.Client.Choice(ChoiceKind.MapAction);
            if (answer == null || choice == null) return false;
            warps.Clear();
            hover = -1;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        // S41: 누가 · 무엇을 · [수락]·[거절] · 남은 시간(시계가 있는 판 — 결정 29 ⑥ 시간 초과 = 기본값(거절), rulings G9)
        void FillPrompt(GameState view, PendingChoice choice)
        {
            var client = flow.Client;
            var legal = client.Legal(choice);
            Prompting = true;
            PromptYes = legal.Find(a => a.Kind == AnswerKind.Accept);
            PromptNo = legal.Find(a => a.Kind == AnswerKind.Decline);
            promptBox = Add(actions, new VisualElement(), "board-hud-prompt", "sn-hud-prompt");
            var who = Add(promptBox, new VisualElement(), null, "sn-hud-prompt-who");
            int from = choice.Subject;
            if (from >= 0 && from < view.Seats.Length)
            {
                var face = Add(who, FaceBox.Make(), "board-hud-prompt-face", null);
                FaceBox.Paint(face, from, flow.SeatColor(from), flow.Protagonist(from));
            }
            var words = Add(who, new VisualElement(), null, "sn-hud-prompt-words");
            bool trade = choice.Sub == ChoiceSub.Trade;
            string name = from >= 0 && from < view.Seats.Length ? flow.SeatName(from) : "";
            Add(words, Kit.Text(stage.T(trade ? "screen.prompt.trade" : "screen.prompt.challenge").Replace("{name}", name), Kit.F2), "board-hud-prompt-title", "kit-title");
            if (trade)
            {
                // 받기·주기를 두 줄로(M5 마감 검토 — 한 줄이면 1080에서 가장 짧은 거래도 「주기」가 잘렸다). 규토리·볼은 글 대신 아이콘 + 수(결정 84 L8 나 — 거래 창 칩과 같은 그림),
                // 포켓몬·아이템·운명 카드·여행 점수는 글. 줄이 넘치면 넘친 조각을 감추고 「…」 — 전체 내용은 마우스 오버
                TradeLines(view, choice, out string get, out string give);
                string all = get + "\n" + give + "\n" + stage.T("screen.prompt.trade.tip");
                var getLine = TradeLine(words, view, choice, 0, "board-hud-prompt-line");
                var giveLine = TradeLine(words, view, choice, 1, "board-hud-prompt-line2");
                stage.Tips.Hook(getLine, null, all, TipSide.Above, reading: true, clear: dock);
                stage.Tips.Hook(giveLine, null, all, TipSide.Above, reading: true, clear: dock);
            }
            else Add(words, Kit.Text(stage.T("screen.prompt.challenge.ask"), Kit.F2), "board-hud-prompt-line", "sn-hud-prompt-line");
            promptClock = Add(who, new VisualElement(), "board-hud-prompt-clock", "sn-hud-prompt-clock");
            var track = Add(promptClock, new VisualElement(), null, "sn-hud-prompt-track");
            promptBar = Add(track, new VisualElement(), "board-hud-prompt-bar", "sn-hud-prompt-bar");
            promptLeft = Add(promptClock, Kit.Text("", Kit.F2), "board-hud-prompt-left", null);
            var buttons = Add(promptBox, new VisualElement(), "board-hud-buttons", "sn-hud-buttons");
            if (PromptYes != null)
            {
                var yes = Add(buttons, Kit.Button(stage.T("screen.prompt.accept"), KitButtonKind.Main, () => PressPrompt(true)), PromptAccept, null);
                stage.Tips.Hook(yes, null, stage.T(trade ? "screen.prompt.accept.trade.tip" : "screen.prompt.accept.challenge.tip"), TipSide.Above, reading: true, clear: dock);
            }
            if (PromptNo != null)
            {
                var no = Add(buttons, Kit.Button(stage.T("screen.prompt.decline"), KitButtonKind.Normal, () => PressPrompt(false)), PromptDecline, null);
                stage.Tips.Hook(no, null, stage.T("screen.prompt.decline.tip"), TipSide.Above, reading: true, clear: dock);
            }
            TickPrompt();
        }

        // 거래 내용 두 줄의 글(받는 것 · 주는 것 — 내 쪽에서, 마우스 오버): Options = [방식, 거래 줄(주는 쪽 · 종류 · 키 · 수)…]. 규토리는 색 · 볼은 볼 이름까지 적는다(줄에서는 아이콘이라)
        void TradeLines(GameState view, PendingChoice choice, out string getLine, out string giveLine)
        {
            var inv = CultureInfo.InvariantCulture;
            var get = new List<string>();
            var give = new List<string>();
            for (int i = 1; i + TradeRules.Stride - 1 < choice.Options.Count; i += TradeRules.Stride)
            {
                int side = choice.Options[i], kind = choice.Options[i + 1], key = choice.Options[i + 2], count = choice.Options[i + 3];
                string what;
                if (kind == (int)TradeKind.Pokemon && key >= 0 && key < view.Pokemon.Count && view.Pokemon[key].Def >= 0) what = stage.T("pokemon." + flow.Ctx.Cards.Pokemon[view.Pokemon[key].Def].Key);
                else if (kind == (int)TradeKind.Apricorn) what = stage.T(WorkUi.ColorKey((ApricornColor)key)) + " " + stage.T("screen.prompt.kind.apricorn") + " ×" + count.ToString(inv);
                else if (kind == (int)TradeKind.Ball) what = stage.T(BattleFeed.BallKey(key)) + " ×" + count.ToString(inv);
                else what = stage.T("screen.prompt.kind." + ((TradeKind)kind).ToString().ToLowerInvariant()) + " ×" + count.ToString(inv);
                if (side == 0) get.Add(what);
                else give.Add(what);
            }
            string none = stage.T("screen.prompt.nothing");
            getLine = stage.T("screen.prompt.trade.get").Replace("{get}", get.Count > 0 ? string.Join(" · ", get) : none);
            giveLine = stage.T("screen.prompt.trade.give").Replace("{give}", give.Count > 0 ? string.Join(" · ", give) : none);
        }

        // 거래 내용 한 줄(side 0 = 받기 · 1 = 주기): 머리 「받기:」 + 조각 — 규토리·볼 = 아이콘 + 「×n」(결정 84 L8 나), 포켓몬 = 이름(폼 꼬리 뺌), 그 밖 = 「종류 ×n」.
        // 넘치면 들어가지 않는 조각부터 감추고 그 자리에 「…」(<c>&lt;줄 이름&gt;-more</c>) — 배치가 바뀔 때마다 다시 맞춘다
        VisualElement TradeLine(VisualElement words, GameState view, PendingChoice choice, int side, string name)
        {
            var inv = CultureInfo.InvariantCulture;
            var line = Add(words, new VisualElement(), name, "sn-hud-prompt-line");
            line.AddToClassList("sn-hud-prompt-row");
            string head = stage.T(side == 0 ? "screen.prompt.trade.get" : "screen.prompt.trade.give");
            int brace = head.IndexOf('{');
            Add(line, Kit.Text((brace >= 0 ? head.Substring(0, brace) : head).TrimEnd(), Kit.F2), null, "sn-hud-prompt-piece");
            int pieces = 0;
            for (int i = 1; i + TradeRules.Stride - 1 < choice.Options.Count; i += TradeRules.Stride)
            {
                if (choice.Options[i] != side) continue;
                int kind = choice.Options[i + 1], key = choice.Options[i + 2], count = choice.Options[i + 3];
                var piece = Add(line, new VisualElement(), name + "-" + pieces.ToString(inv), "sn-hud-prompt-piece");
                pieces++;
                string times = "×" + count.ToString(inv);
                if (kind == (int)TradeKind.Apricorn)
                {
                    WorkUi.Apricorn(piece, (ApricornColor)key);
                    piece.Add(Kit.Text(times, Kit.F2));
                }
                else if (kind == (int)TradeKind.Ball)
                {
                    WorkUi.Icon(piece, GenAssets.ItemIcons + BattleFeed.BallItem(key) + ".png", "sn-icon");
                    piece.Add(Kit.Text(times, Kit.F2));
                }
                else if (kind == (int)TradeKind.Pokemon && key >= 0 && key < view.Pokemon.Count && view.Pokemon[key].Def >= 0)
                    piece.Add(Kit.Text(ShortName(stage.T("pokemon." + flow.Ctx.Cards.Pokemon[view.Pokemon[key].Def].Key)), Kit.F2));
                else piece.Add(Kit.Text(stage.T("screen.prompt.kind." + ((TradeKind)kind).ToString().ToLowerInvariant()) + " " + times, Kit.F2));
            }
            if (pieces == 0) Add(line, Kit.Text(stage.T("screen.prompt.nothing"), Kit.F2), null, "sn-hud-prompt-piece");
            var more = Add(line, Kit.Text(Ellipsis, Kit.F2), name + "-more", "sn-hud-prompt-more");
            line.RegisterCallback<GeometryChangedEvent>(e => FitLine(line, more));
            return line;
        }

        const string Ellipsis = "…";

        // 줄에 들지 않는 조각(머리 다음부터)을 감춘다(자리는 그대로 — visibility라 배치가 흔들리지 않는다) · 감춘 것이 있으면 마지막 보이는 조각 뒤에 「…」
        static void FitLine(VisualElement line, VisualElement more)
        {
            float room = line.contentRect.width;
            if (float.IsNaN(room) || room <= 0f) return;
            float dots = more.layout.width;
            VisualElement last = null;
            foreach (var c in line.Children()) if (c != more) last = c;
            bool over = last != null && last.layout.xMax > room + 0.5f;
            bool cut = false;
            float end = 0f;
            int index = 0;
            foreach (var c in line.Children())
            {
                if (c == more) continue;
                bool show = index++ == 0 || (!cut && (!over || c.layout.xMax <= room - dots));
                if (!show) cut = true;
                else end = c.layout.xMax;
                c.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
            }
            more.style.left = end;
            more.style.visibility = cut ? Visibility.Visible : Visibility.Hidden;
        }

        /// <summary>그 거래 줄(<c>board-hud-prompt-line</c> · <c>-line2</c>)에서 넘쳐 감춘 조각이 있나(「…」이 보이나) — 확인용.</summary>
        public bool PromptCut(string line)
        {
            var more = promptBox != null ? promptBox.Q(line + "-more") : null;
            return more != null && more.resolvedStyle.visibility == Visibility.Visible;
        }

        // ── 아르세우스의 축복 동의 · 탈락(M6 「UB 화면 묶음 2」) ──

        public const string BlessAsk = "board-hud-bless-ask", OutClass = "sn-hud-seat-out";

        /// <summary>위 띠 동의 물음이 떠 있나 · 내 답을 기다리나(확인용).</summary>
        public bool BlessAsking { get; private set; }
        public bool BlessWaiting { get; private set; }

        // 열린 축복 동의(Respond/Blessing — 낸 좌석 말고 모두에게 묶음으로): 내 것이 남았으면 누가 · 무엇을 · [막기]·[허용], 남의 답만 남았으면 「확인을 기다리는 중」
        void FillBlessAsk(GameState view)
        {
            if (blessAsk == null) return;
            var client = flow.Client;
            PendingChoice mine = null, any = null;
            foreach (var c in view.Pending)
            {
                if (c.Kind != ChoiceKind.Respond || c.Sub != ChoiceSub.Blessing || c.Answered) continue;
                if (any == null) any = c;
                if (c.Seat == client.Seat && mine == null) mine = c;
            }
            BlessAsking = mine != null;
            BlessWaiting = any != null && mine == null;
            blessAsk.Clear();
            blessClock = blessBar = null;
            blessLeft = null;
            blessAsk.style.display = any != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (any == null) return;
            var ask = mine ?? any;
            int from = ask.Subject, value = ask.Options.Count > 0 ? ask.Options[0] : UbBlessing.SkipTurn;
            if (from >= 0 && from < view.Seats.Length)
            {
                var face = Add(blessAsk, FaceBox.Make(), BlessAsk + "-face", null);
                FaceBox.Paint(face, from, flow.SeatColor(from), flow.Protagonist(from));
            }
            string text = value == UbBlessing.SkipTurn
                ? stage.T("screen.bless.consent.skip")
                : KoJosa.Fix(stage.T("screen.bless.consent.revive").Replace("{target}", flow.SeatName(value)));
            var line = Add(blessAsk, Kit.Wrapped(text.Replace("{name}", flow.SeatName(from)), Kit.F2), BlessAsk + "-text", "sn-hud-bless-text");
            line.style.whiteSpace = WhiteSpace.Normal;
            if (mine == null)
            {
                // 기다림 문구도 줄을 바꿔 물음 글과 폭을 나눈다(한 줄로 두면 1080에서 물음 글이 한두 낱말씩 다섯 줄로 눌렸다 — M6 마감 감사)
                var wait = Add(blessAsk, Kit.Wrapped(stage.T("screen.bless.consent.wait"), Kit.F2), BlessAsk + "-wait", "sn-hud-bless-text");
                wait.AddToClassList("kit-muted");
                wait.style.whiteSpace = WhiteSpace.Normal;
                return;
            }
            // 남은 시간(시간제한 판 — 시간 초과 = 허용): S41 응답 프롬프트와 같은 막대 · 초
            blessClock = Add(blessAsk, new VisualElement(), BlessAsk + "-clock", "sn-hud-prompt-clock");
            blessBar = Add(Add(blessClock, new VisualElement(), null, "sn-hud-prompt-track"), new VisualElement(), BlessAsk + "-bar", "sn-hud-prompt-bar");
            blessLeft = Add(blessClock, Kit.Text("", Kit.F2), BlessAsk + "-left", null);
            TickBless(mine);
            bool open = flow.InputOpen;
            var block = Add(blessAsk, Kit.Button(stage.T("screen.bless.consent.block"), KitButtonKind.Normal, () => PressBless(false)), BlessAsk + "-block", null);
            block.SetEnabled(open);
            if (open) stage.Tips.Hook(block, null, stage.T("screen.bless.consent.block.tip"), TipSide.Below, reading: true);
            Add(blessAsk, Kit.Button(stage.T("screen.bless.consent.allow"), KitButtonKind.Main, () => PressBless(true)), BlessAsk + "-allow", null).SetEnabled(open);
        }

        // 동의 물음의 남은 시간 막대 · 초(시계가 없는 판이면 숨긴다)
        void TickBless(PendingChoice mine)
        {
            if (blessClock == null) return;
            int total = 0;
            int left = mine != null ? flow.ClockLeftMs(mine, out total) : -1;
            blessClock.style.display = left >= 0 && total > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            BlessClockShown = left >= 0 && total > 0 ? left : -1;
            if (left < 0 || total <= 0) return;
            blessBar.style.width = Length.Percent(100f * left / total);
            blessLeft.text = stage.T("screen.prompt.left").Replace("{n}", ((left + 999) / 1000).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>동의 물음에 보이는 남은 시간(ms — 확인용, 막대가 없으면 −1).</summary>
        public int BlessClockShown { get; private set; } = -1;

        /// <summary>위 띠 동의 물음의 [허용](true)·[막기](false): 그 답을 낸다. 받아들여졌으면 true.</summary>
        public bool PressBless(bool allow)
        {
            var client = flow.Client;
            var view = client != null ? client.View : null;
            if (view == null || !flow.InputOpen) return false;
            var choice = view.Pending.Find(c => c.Seat == client.Seat && !c.Answered && c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Blessing);
            var answer = choice != null ? client.Legal(choice).Find(a => a.Kind == (allow ? AnswerKind.Accept : AnswerKind.Decline)) : null;
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        // 탈락·부활(91행): 좌석 칩의 회색 · 「탈락」 꼬리표, 부활하는 말 위의 금빛 고리
        void ShowFall(FxSequencer seq)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (view == null || seats == null) return;
            float ring = 0f;
            for (int s = 0; s < view.Seats.Length; s++)
            {
                var pose = flow.FallShown(s);
                string n = (s + 1).ToString(CultureInfo.InvariantCulture);
                var chip = seats.Q("board-hud-seat-" + n);
                if (chip != null) chip.EnableInClassList(OutClass, pose.Grey >= 0.5f);
                var tag = seats.Q("board-hud-seat-" + n + "-out");
                if (tag != null) tag.style.display = pose.Crack ? DisplayStyle.Flex : DisplayStyle.None;
                if (pose.RingAlpha <= 0f || revive == null || !flow.HeadPoint(flow.BoardPieces != null ? flow.BoardPieces.Of(s) : null, out var head)) continue;
                float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
                revive.style.left = head.x;
                revive.style.top = head.y - ReviveDropPx * unit + pose.Ring * ReviveDropPx * unit * 2f;
                ring = pose.RingAlpha;
            }
            if (revive != null) FxUi.Fade(revive, ring);
            ReviveShown = ring;
        }

        /// <summary>부활 고리가 내려오는 길이(px — 720 벌, 1080 벌은 1.5배). 머리 위 이만큼에서 발밑 쪽으로 두 배 내려온다.</summary>
        public const float ReviveDropPx = 28f;

        /// <summary>부활 고리의 지금 진하기(확인용 — 0 = 없음).</summary>
        public float ReviveShown { get; private set; }

        /// <summary>프롬프트의 [수락]·[거절]: 그 답을 낸다. 받아들여졌으면 true.</summary>
        public bool PressPrompt(bool accept)
        {
            var choice = flow.Client != null ? flow.Client.Choice(ChoiceKind.Respond) : null;
            var answer = accept ? PromptYes : PromptNo;
            if (!flow.InputOpen || !Prompting || choice == null || answer == null) return false;
            stage.Tips.Hide();
            // 거래 수락에 줄 운명의 변화 카드를 골라야 하면(받아들이는 답이 여럿) 고르는 창을 연다(S36)
            if (accept && choice.Sub == ChoiceSub.Trade && flow.Client.Legal(choice).FindAll(a => a.Kind == AnswerKind.Accept).Count > 1) return flow.OpenTradePick();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>
        /// 프레임마다(대기열 밖 — 98행): 프롬프트가 아래에서 올라오고 남은 시간 막대·초가 줄어든다(시계가 없는 판이면 숨긴다). 시간 초과로 기본값 답이 났으면 「시간 초과 · 사용 안 함」이 잠시 남는다.
        /// </summary>
        public void TickPrompt()
        {
            if (stage == null) return;
            var client = flow.Client;
            var choice = client != null ? client.Choice(ChoiceKind.Respond) : null;
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            if (Prompting && promptBox != null && choice != null)
            {
                float rise = PromptBeats.Rise(flow.NowMs - flow.PromptSeenMs(choice));
                FxUi.Move(promptBox, 0f, Mathf.Round((1f - rise) * PromptBeats.RisePx * unit));
                FxUi.Fade(promptBox, rise);
                int left = flow.ClockLeftMs(choice, out int total);
                if (promptClock != null) promptClock.style.display = left >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (left >= 0 && total > 0)
                {
                    promptBar.style.width = Length.Percent(100f * left / total);
                    promptLeft.text = stage.T("screen.prompt.left").Replace("{n}", ((left + 999) / 1000).ToString(CultureInfo.InvariantCulture));
                }
            }
            if (blessClock != null && client != null && client.View != null)
                TickBless(client.View.Pending.Find(c => c.Seat == client.Seat && !c.Answered && c.Kind == ChoiceKind.Respond && c.Sub == ChoiceSub.Blessing));
            bool late = flow.TimedOutAtMs != long.MinValue && flow.NowMs - flow.TimedOutAtMs < PromptBeats.TimeoutMs;
            if (timeout != null) FxUi.Fade(timeout, late ? 1f : 0f);
            TimeoutShown = late;
        }

        // 받기 · 버리기 · 카드 지나가기(7·8·42·45행): 내 카드는 [운명의 변화] 단추로, 남의 카드는 그 좌석 칩으로
        void ShowFate(FxSequencer seq)
        {
            var client = flow.Client;
            if (fateFx == null || client == null || client.View == null) return;
            var root = fateFx.Element.parent;
            var hand = fateFx.Centre(party != null ? party.Q(FateButton) : null) ?? fateHand;   // 다시 지은 단추가 아직 배치 전이면 앞 자리
            fateHand = hand;
            var star = fateFx.Centre(root != null ? root.Q("board-hud-status") : null) ?? new Vector2(1100f, 30f);
            float w = fateFx.Element.layout.width;
            var draw = new Vector2(float.IsNaN(w) || w <= 0f ? 640f : w * 0.5f, 60f);
            fateFx.Show(seq, client.Seat, flow.Ctx, hand, s => fateFx.Centre(seats != null ? seats.Q("board-hud-seat-" + (s + 1).ToString(CultureInfo.InvariantCulture)) : null), star, draw);
            if (ShownFate(client.View.Seats[client.Seat], client.Seat) != paintedFate) Refresh();   // 카드가 손에 닿았다: [운명의 변화]의 장수
        }

        // 펄기아 순간이동(14행): 출발 칸에서 분홍 고리가 커지며 옅어지고, 도착 칸에서 다시
        void ShowWarp(FxSequencer seq)
        {
            if (warpRings.Count < 2) return;
            var step = TeleportBeats.StepOf(seq.Current);
            var t = TeleportBeats.Event(step);
            if (step == null || t == null || seq.LocalMs < step.StartMs || seq.LocalMs >= step.EndMs)
            {
                foreach (var r in warpRings) FxUi.Fade(r, 0f);
                return;
            }
            var pose = TeleportBeats.At(seq.LocalMs - step.StartMs, step.Ms);
            Ring(warpRings[0], t.From, pose.RingFrom);
            Ring(warpRings[1], t.To, pose.RingTo);
        }

        // 왜곡 게이트 이동(15행): 가라앉는 게이트에서 보라 고리 → 두 게이트 사이에 보라 점선이 출발 쪽부터 뻗었다 옅어지고 → 솟아오른 게이트에서 착지 고리
        void ShowGate(FxSequencer seq)
        {
            if (gateRings.Count < 2) return;
            var step = GateBeats.StepOf(seq.Current);
            var g = GateBeats.Event(step);
            if (step == null || g == null || seq.LocalMs < step.StartMs || seq.LocalMs >= step.EndMs)
            {
                foreach (var r in gateRings) FxUi.Fade(r, 0f);
                foreach (var d in gateDots) FxUi.Fade(d, 0f);
                return;
            }
            var pose = GateBeats.At(seq.LocalMs - step.StartMs, step.Ms);
            Ring(gateRings[0], g.FromCell, pose.RingFrom);
            Ring(gateRings[1], g.ToCell, pose.RingTo);
            bool ends = flow.CellPoint(g.FromCell, out var a) & flow.CellPoint(g.ToCell, out var b);
            bool line = ends && pose.LineAlpha > 0f;
            for (int i = 0; i < gateDots.Count; i++)
            {
                float at = (i + 1f) / (gateDots.Count + 1f);
                if (!line || at > pose.Line)
                {
                    FxUi.Fade(gateDots[i], 0f);
                    continue;
                }
                FxUi.Move(gateDots[i], Mathf.Lerp(a.x, b.x, at), Mathf.Lerp(a.y, b.y, at));
                FxUi.Fade(gateDots[i], pose.LineAlpha);
            }
        }

        /// <summary>염력 칸 표시 요소 이름.</summary>
        public const string ReachMark = "board-hud-reach";

        /// <summary>지금 칠한 염력 칸(없으면 −1 — 시험 · 확인).</summary>
        public int ReachShown { get; private set; } = -1;

        // 염력(psychic B)으로 고른 떨어진 칸(결정 120 — 남과 나): 능력 사용 50행 단계 동안 그 칸 위에 그 좌석 색 테두리 + 옅은 바탕(능력 칸이 빛나는 박자 AbilityBeats.At의 Show · Glow)
        void ShowReach(FxSequencer seq)
        {
            if (reachMark == null) return;
            var step = CardRows.Playing(seq, CardRows.Ability, out int local);
            int cell = AbilityBeats.ReachCell(step, flow.Ctx);
            if (cell < 0 || local < 0 || !flow.CellPoint(cell, out var at))
            {
                ReachShown = -1;
                FxUi.Fade(reachMark, 0f);
                return;
            }
            var pose = AbilityBeats.At(local, step.Ms);
            var color = (Color)flow.SeatColor(WorkRows.SeatOf(step));
            reachMark.style.borderLeftColor = reachMark.style.borderRightColor = reachMark.style.borderTopColor = reachMark.style.borderBottomColor = color;
            reachMark.style.backgroundColor = new Color(color.r, color.g, color.b, 0.25f + 0.2f * pose.Glow);
            FxUi.Move(reachMark, at.x, at.y);
            FxUi.Scale(reachMark, 1f, 0.5f);
            FxUi.Fade(reachMark, pose.Show);
            ReachShown = cell;
        }

        void Ring(VisualElement ring, int cell, float p)
        {
            if (p <= 0f || p >= 1f || !flow.CellPoint(cell, out var at))
            {
                FxUi.Fade(ring, 0f);
                return;
            }
            FxUi.Move(ring, at.x, at.y);
            float s = 0.4f + 1.2f * p;
            FxUi.Scale(ring, s, s * 0.5f);
            FxUi.Fade(ring, 1f - p);
        }

        // ── 기록 · 칸 정보 · 공개 정보 카드 ──

        // 왼쪽 위 기록 판: 지금 보이는 줄 가운데 끝의 셋(라운드 머리는 뺀다). 보이는 줄 수가 그대로면 다시 그리지 않는다
        void PaintLog(bool force)
        {
            var log = flow.Log;
            if (log == null || logLines == null) return;
            // 배틀 화면이 떠 있는 동안은 숨긴다 — 배틀 정보 기둥 밖으로 판의 오른쪽 끝이 비친다(M4 마감에서 찾음)
            if (logBox != null) logBox.style.display = flow.Stage.Current is BattleScreen ? DisplayStyle.None : DisplayStyle.Flex;
            logShown.Clear();
            log.Shown(flow.Fx != null ? flow.Fx.Sequencer : null, logShown);
            if (!force && logShown.Count == paintedLog) return;
            paintedLog = logShown.Count;
            logLines.Clear();
            recent.Clear();
            int from = logShown.Count;
            for (int n = 0; from > 0 && n < RecentLines; from--)
                if (logShown[from - 1].Kind == BoardLogKind.Line) n++;
            for (int i = from; i < logShown.Count; i++)
            {
                var line = logShown[i];
                if (line.Kind != BoardLogKind.Line) continue;
                string text = line.Text(stage.T, flow.SeatName);
                recent.Add(text);
                logLines.Add(LogScreen.Row(flow, line, text, Kit.F1));
            }
            if (recent.Count == 0)
            {
                var none = Kit.Text(stage.T("screen.log.empty"), Kit.F1);
                none.AddToClassList("kit-muted");
                logLines.Add(none);
            }
            (stage.Current as LogScreen)?.Refresh();
        }

        /// <summary>
        /// 칸 정보 한 줄: 지형 · 기후 · 높이 · 그 칸에 있는 것(마을·사당·전설 장소·게이트의 이름, 나무·산호의 등급과 규토리 색, 숨겨진 장소·조무래기 토큰).
        /// 모두 공개 정보다(보드와 보기의 토큰 — 뒷면 숨겨진 장소는 정체 없이 「숨겨진 장소」). 토큰은 보드 위에 보이는 모습 그대로 적는다 —
        /// 아직 놓이지 않은 조무래기는 빼고, 뒤집히기 전의 공개는 「숨겨진 장소」다(<c>GameFlow.TokenShown</c>·<c>TokenFaceUp</c>). 공중날기 택시 자리는 적지 않는다(규칙 행동이 없는 TTS 물체 자리).
        /// </summary>
        /// <summary>위 띠의 날씨 배지 이름.</summary>
        public const string WeatherBadge = "board-hud-weather";
        /// <summary>「행동」 머리 줄의 앰프 · 장작 칩(결정 87 V9 — <c>board-hud-card-amp</c>·<c>-firewood</c>, <see cref="CardAsk.StateChips"/>).</summary>
        public const string CardState = "board-hud-card";

        // 파티 칸의 꼬리표(M6): 열리지 않은 칸의 포켓몬 = 「배틀 불가」(pokemon_ranger B — PokemonRules.Fights) · 월척 주사위의 면(fisherman) — 칸의 배치 밖(도트 아래 모서리)이라 칸 폭이 그대로다
        void MonTag(VisualElement info, SeatState me, int index, PokemonState pk)
        {
            var parts = new List<string>();
            var tips = new List<string>();
            if (!PokemonRules.Fights(me, index))
            {
                parts.Add(stage.T("screen.board.mon.locked"));
                tips.Add(stage.T("screen.board.mon.locked.tip"));
            }
            if (pk.CatchSize >= 0 && pk.CatchSize < TrainerRules.CatchSizes.Length)
            {
                string size = stage.T("screen.card.catch." + pk.CatchSize.ToString(CultureInfo.InvariantCulture));
                parts.Add(stage.T("screen.board.mon.catch").Replace("{size}", size));
                tips.Add(stage.T("screen.board.mon.catch.tip").Replace("{size}", size).Replace("{n}", TrainerRules.CatchSizes[pk.CatchSize].ToString("+0;-0;0", CultureInfo.InvariantCulture)));
            }
            if (parts.Count == 0) return;
            var tag = Add(info, Kit.Badge(string.Join(" · ", parts), KitBadgeKind.Plain, Kit.F2), "board-hud-mon-" + index.ToString(CultureInfo.InvariantCulture) + "-tag", "sn-hud-mon-tag");
            stage.Tips.Hook(tag, null, string.Join("\n", tips), TipSide.Above, reading: true, clear: dock);
        }

        public string CellText(int cell)
        {
            var ctx = flow.Ctx;
            var view = flow.Client != null ? flow.Client.View : null;
            if (ctx == null || cell < 0 || cell >= ctx.Board.Count) return "";
            var inv = CultureInfo.InvariantCulture;
            var c = ctx.Board[cell];
            var parts = new List<string>();
            if (c.Biome != Biome.None) parts.Add(stage.T("screen.encounter.biome." + c.Biome.ToString().ToLowerInvariant()));
            if (c.Climate != Climate.None) parts.Add(stage.T("screen.encounter.climate." + c.Climate.ToString().ToLowerInvariant()));
            parts.Add(stage.T("screen.cell.level").Replace("{n}", c.Floor.ToString(inv)));
            if (fixturesAt == null)
            {
                fixturesAt = new Dictionary<int, List<FixtureDef>>();
                foreach (var f in ctx.Fixtures.Fixtures)
                {
                    if (!fixturesAt.TryGetValue(f.Cell, out var list)) fixturesAt[f.Cell] = list = new List<FixtureDef>();
                    list.Add(f);
                }
            }
            if (fixturesAt.TryGetValue(cell, out var here))
                foreach (var f in here)
                {
                    string name = FixtureText(f);
                    if (name != null) parts.Add(name);
                }
            if (view != null)
                foreach (var t in view.Tokens)
                {
                    if (t.Cell != cell || !flow.TokenShown(t)) continue;
                    if (t.Kind == TokenKind.Grunt) parts.Add(stage.T("screen.token.grunt"));
                    else if (t.Kind == TokenKind.HiddenSpot)
                    {
                        // 금색 ?(특별한 숨겨진 장소 — 뒷면이어도 공개 값) · 앞면으로 남은 유물(ruin_maniac B가 공개만 했다 — 그 칸에 오면 줍는다)
                        bool up = flow.TokenFaceUp(t) && t.Def >= 0 && t.Def < ctx.Items.Items.Count;
                        string name = up ? stage.T(ctx.Items.Items[t.Def].Key) : stage.T(t.Special ? "screen.token.hidden.special" : "screen.token.hidden");
                        if (up && t.Loose) name += " " + stage.T("screen.token.loose");
                        parts.Add(name);
                    }
                }
            if (view != null && view.Roads.Contains(cell)) parts.Add(stage.T("screen.cell.road"));   // worker B가 놓은 길(감사 G5-1)
            // 결정 179: 그 칸에 지금 보이는 UB · 네크로즈마(폼 이름까지) · 시련이 남은 호수 수호신
            flow.BeastsAt(cell, beastKeys);
            foreach (string key in beastKeys) parts.Add(stage.T("pokemon." + key));
            int guardian = flow.GuardianAt(cell);
            if (guardian >= 0) parts.Add(stage.T("pokemon." + ctx.Cards.Pokemon[guardian].Key));
            return string.Join(" · ", parts);
        }

        readonly List<string> beastKeys = new List<string>();

        string FixtureText(FixtureDef f)
        {
            switch (f.Kind)
            {
                case FixtureKind.ApricornTree:
                case FixtureKind.ApricornReef:
                {
                    string grade = f.Tier.HasValue ? stage.T("screen.encounter.grade." + f.Tier.Value.ToString().ToLowerInvariant()) : "";
                    var colors = new List<string>();
                    foreach (var color in f.Apricorns)
                    {
                        string name = stage.T(BoardLog.ApricornKey((int)color));
                        if (!colors.Contains(name)) colors.Add(name);
                    }
                    return stage.T(f.Kind == FixtureKind.ApricornTree ? "screen.cell.tree" : "screen.cell.reef").Replace("{grade}", grade).Replace("{colors}", string.Join("·", colors));
                }
                case FixtureKind.DistortedTree: return stage.T("screen.cell.distorted_tree");
                case FixtureKind.Taxi: return null;
                default: return stage.TOrNull(f.Key);
            }
        }

        /// <summary>그 좌석의 공개 정보 카드를 좌석 칩 아래에 띄운다(마우스를 칩에 올렸을 때 — 캡처·시험은 직접 부른다).</summary>
        public void ShowSeatInfo(int seat)
        {
            var chip = seats != null ? seats.Q("board-hud-seat-" + (seat + 1).ToString(CultureInfo.InvariantCulture)) : null;
            if (chip == null || info == null || stage.Root == null) { HideInfo(); return; }
            infoSeat = seat;
            infoCell = -1;
            var at = stage.Root.WorldToLocal(chip.worldBound);
            info.ShowSeat(seat, at, TipSide.Below);
            // 카드가 기록 판을 덮는 자리(칩이 기록 판 위)면 기록 판을 숨긴다 — 카드 위로 기록 판의 윗테두리가 삐져나와 창틀이 두 겹으로 보인다(검토 반영)
            if (logBox != null) logBox.style.visibility = at.x < stage.Root.WorldToLocal(logBox.worldBound).xMax ? Visibility.Hidden : Visibility.Visible;
        }

        /// <summary>그 칸의 말·토큰 정보를 칸 옆에 띄운다. 띄울 것이 없으면(빈 칸 · 내 말만 선 칸) 걷는다.</summary>
        public bool ShowCellInfo(int cell)
        {
            if (info == null || cell < 0) { HideInfo(); return false; }
            Vector2 at;
            if (!flow.CellPoint(cell, out at)) at = stage.Root != null ? new Vector2(stage.Root.layout.width * 0.5f, stage.Root.layout.height * 0.4f) : default;
            float half = CellHalf;
            var target = new Rect(at.x - half, at.y - half, half * 2f, half * 2f);
            // 묶음 표지가 뜬 칸이면 카드가 표지와 사람 수 배지도 덮지 않게 비켜 선다
            if (stacks != null && stacks.BoundsOf(cell, out var badge)) target = Rect.MinMaxRect(Mathf.Min(target.xMin, badge.xMin), Mathf.Min(target.yMin, badge.yMin), Mathf.Max(target.xMax, badge.xMax), Mathf.Max(target.yMax, badge.yMax));
            bool shown = info.ShowCell(cell, target, TipSide.Right);
            infoSeat = -1;
            infoCell = shown ? cell : -1;
            if (logBox != null) logBox.style.visibility = Visibility.Visible;
            return shown;
        }

        /// <summary>칸을 가리키는 사각형의 반 변(패널 px) — 카드가 그 칸의 토큰·말을 덮지 않게 이만큼 비켜 선다.</summary>
        public const float CellHalf = 28f;

        public void HideInfo()
        {
            infoSeat = infoCell = -1;
            info?.Hide();
            if (logBox != null) logBox.style.visibility = Visibility.Visible;
        }

        /// <summary>
        /// 마우스 아래의 PC 층 요소(없으면 null)를 받는다(프레임마다): 좌석 칩 위면 그 좌석의 공개 정보 카드를 띄우고, 벗어나면 걷는다.
        /// 칩은 갱신 때마다 다시 만들어지므로 요소에 콜백을 걸지 않고 이름으로 찾는다.
        /// </summary>
        public void HoverUi(VisualElement picked)
        {
            int seat = SeatOf(picked);
            if (seat >= 0) { if (seat != infoSeat) ShowSeatInfo(seat); }
            else if (infoSeat >= 0) HideInfo();
        }

        /// <summary>
        /// 마우스로 PC 층 요소를 눌렀다(프레임마다 — 누른 프레임에만): 좌석 칩이면 그 좌석의 판(내 좌석 = 정비 판 S18, 남 좌석 = 읽기만 하는 S39)을 연다.
        /// 칩은 갱신 때마다 다시 만들어지므로 요소에 콜백을 걸지 않고 이름으로 찾는다(<see cref="HoverUi"/>와 같다). 연 것이 있으면 true.
        /// </summary>
        public bool ClickUi(VisualElement picked)
        {
            int seat = SeatOf(picked);
            if (seat < 0) return false;
            HideInfo();
            return flow.OpenParty(seat);
        }

        static int SeatOf(VisualElement picked)
        {
            for (var e = picked; e != null; e = e.parent)
            {
                if (e.name == null || !e.name.StartsWith("board-hud-seat-", StringComparison.Ordinal) || e.name.EndsWith("-face", StringComparison.Ordinal)) continue;
                if (int.TryParse(e.name.Substring("board-hud-seat-".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n - 1;
            }
            return -1;
        }

        // 위 띠에 보일 턴 좌석·라운드(04 §8-8 101·102행): 보기는 턴 종료 답과 함께 바로 다음 턴으로 넘어가지만, 화면은 넘김 연출이 거기 닿을 때 바뀐다 —
        // 라운드는 넘김 띠가 끝날 때, 턴 좌석(턴 배지·좌석 칩 강조)은 차례가 넘어가는 순간 — 넘김 연출(요약 · 띠)이 시작할 때(M8 · M9 마감 감사 2차 참고 ② — 띠가 끝날 때까지
        // 약 3초 「내 턴」으로 남았다 [가정 plan §5 #776]). 내게 오는 턴(내 턴 컷신이 있다)만 컷신이 끝날 때 「내 턴」이 된다. 그 전에는 끝난 턴의 값을 보인다.
        // 라운드를 미루지 않으면 「앞 턴의 결과」 요약이 떠 있는 동안 배지가 이미 다음 라운드다. 게임 첫 턴(끝난 턴이 없다)은 보기 그대로다
        void ShownTurn(GameState view, out int seat, out int round)
        {
            seat = view.ActiveSeat;
            round = view.Round;
            var seq = flow.Fx?.Sequencer;
            if (seq == null) return;
            foreach (var entry in seq.Queued)
            {
                FxStep summary = entry.Step(FxRows.TurnSummary), strip = entry.Step(FxRows.Handoff), cut = entry.Step(FxRows.MyTurn);
                var info = (summary ?? strip ?? cut)?.Tag as TurnBandInfo;
                if (info == null || info.From < 0) continue;
                bool playing = ReferenceEquals(entry, seq.Current);
                int roundAt = strip != null ? strip.EndMs : 0, seatAt = cut != null ? cut.EndMs : (summary ?? strip).StartMs;
                bool roundIn = playing && seq.LocalMs >= roundAt, seatIn = playing && seq.LocalMs >= seatAt;
                if (roundIn && seatIn) continue;
                seat = seatIn ? info.To : info.From;
                round = roundIn ? info.Round : info.FromRound;
                return;
            }
        }

        // 보호 배지를 보이나: 보기에서 보호 중이거나, 풀리는 연출(9행)이 대기열에 남았다
        bool Shielded(GameState view, int seat) => view.Seats[seat].Protected || ShieldBeats.Pending(flow.Fx?.Sequencer, seat);

        int ShieldMask(GameState view)
        {
            int mask = 0;
            for (int s = 0; s < view.Seats.Length; s++) if (Shielded(view, s)) mask |= 1 << s;
            return mask;
        }

        /// <summary>그 좌석 칩에 보호 배지가 있나(확인용).</summary>
        /// <summary>좌석 칩의 도로 켬 배지 이름(worker B — <c>board-hud-seat-&lt;n&gt;-road</c>).</summary>
        public static string RoadName(int seat) => "board-hud-seat-" + (seat + 1).ToString(CultureInfo.InvariantCulture) + "-road";

        /// <summary>그 좌석 칩에 도로 켬 배지가 섰나(시험 · 확인).</summary>
        public bool RoadShown(int seat) => seats != null && seats.Q(RoadName(seat)) != null;

        public bool ShieldShown(int seat) => seats != null && seats.Q("board-hud-seat-" + (seat + 1).ToString(CultureInfo.InvariantCulture) + "-shield") != null;

        // 걷는 동안에는 디딘 걸음만큼만 줄어든 값을 보인다(04 §8-8 11행 「착지마다 남은 이동력 칩 하나 꺼짐」)
        int ShownMoves(SeatState me)
        {
            var seq = flow.Fx?.Sequencer;
            var entry = seq?.Current;
            if (entry == null) return me.MovesLeft;
            // 여럿 판의 내 턴 시작(6행): 받은 이동력이 하나씩 차오른다(그 단계가 끝나면 다 찼다)
            var start = TurnStartBeats.StepOf(entry);
            if (start != null && start.Tag is TurnStartInfo ts && ts.Mine && seq.LocalMs < start.EndMs)
                return Math.Min(me.MovesLeft, TurnStartBeats.Lit(seq.LocalMs - start.StartMs, start.Ms, ts.Moves));
            FxStep walk = null;
            foreach (var s in entry.Steps)
                if (s.Row == 110 && s.Events.Count > 0 && s.Events[0].Seat == flow.Client.Seat && (walk == null || s.StartMs <= seq.LocalMs)) walk = s;
            if (walk == null) return me.MovesLeft;
            var m = (Moved)walk.Events[0];
            return m.AllowanceBefore - FxWalk.At(m, walk.Local(seq.LocalMs)).Landed;
        }

        static string UsedName(UsageKind kind)
        {
            switch (kind)
            {
                case UsageKind.VillageHeal: return "village_heal";
                case UsageKind.Ginkgo: return "ginkgo";
                case UsageKind.Fly: return "fly";
                case UsageKind.FateMap: return "fate";
                case UsageKind.ShrineOffer: return "shrine";
                default: return null;   // 빈 찾아보기·도전 기록은 「턴당 1회」 행동이 아니다
            }
        }

        void Mark()
        {
            var marks = flow.BoardMarks;
            if (marks == null) return;
            var flyPick = flow.FlyPick;   // T18 ①: 공중날기 목적지를 보드에서 고르는 동안은 그 창이 칠한다(갈 수 있는 마을 · 흐린 마을)
            if (flyPick != null)
            {
                flyPick.Mark(marks);
                return;
            }
            if (reach.Count == 0)
            {
                // 결정 71 ③: 고른 길 가운데의 숨겨진 장소 물음 동안은 남은 길(지나가기면 이어 걸을 길)을 칠해 둔다
                var ask = flow.InputOpen && flow.Client != null ? flow.Client.Choice(ChoiceKind.HiddenSpot) : null;
                if (ask == null || ask.Options.Count == 0) { marks.Hide(); return; }
                var rest = new List<int> { flow.Client.View.Seats[flow.Client.Seat].Cell };
                rest.AddRange(ask.Options);
                // 하늘색 고리 = 지금 묻는 칸(선 칸의 뒷면 숨겨진 장소 — 고르는 단계의 표지가 이어진다, 검토 반영) + 남은 길에서 또 멈출 칸
                var stops = MapActions.StopsOn(flow.Client.View, flow.Ctx, flow.Client.Seat, ask.Options, ask.Count == MapActions.FreeRest);
                stops.Insert(0, rest[0]);
                marks.Show(reach, rest, rest[rest.Count - 1], true, null, stops);
                return;
            }
            // 고른 칸이 있으면 그 칸까지의 길을 칠해 둔다(마우스를 다른 칸에 올려도 그대로 — 걷기 전 확인), 없으면 마우스를 올린 칸까지.
            if (warps.Count > 0 || abilityPicks.Count > 0 || CardCells)
            {
                marks.Show(reach, hover >= 0 ? new List<int> { hover } : abilityCell >= 0 ? new List<int> { abilityCell } : null, hover >= 0 ? hover : abilityCell, false, null, null);   // 순간이동: 걷지 않으니 길이 없다 — 마우스를 올린 칸만
                return;
            }
            // 길 = 엔진이 걸을 길(보기 + Rules 질의, 04 §8-8 10행 — 경유 칸은 구간마다 최단), 길 위의 뒷면 숨겨진 장소는 표지(결정 71 ①)
            int show = picked >= 0 ? picked : hover;
            var path = show >= 0 ? RouteOf(picked >= 0 ? (IReadOnlyList<int>)vias : null, show) : null;
            var mapChoice = path == null && show >= 0 ? flow.Client.Choice(ChoiceKind.MapAction) : null;
            if (mapChoice != null && GateTo(mapChoice, show) != null) path = new List<int> { show };   // 왜곡 게이트 건너편(15행): 걷지 않으니 길이 없다 — 그 칸만
            marks.Show(reach, path, show, picked >= 0, picked >= 0 ? vias : null, StopsOf(path));   // 고른 칸은 테두리로(그 칸의 토큰이 보이게 — 마우스를 올린 칸은 금색 칠)
        }

        // 위 띠 날씨 배지의 글 — 이름만(「모래바람」, 결정 87 V10 ①). 타입 보너스는 마우스 오버의 머리 줄(<see cref="CardAsk.WeatherBonus"/>)
        string WeatherText(int w)
        {
            w = Mathf.Clamp(w, 0, TrainerRules.WeatherTypes.Length - 1);
            return CardAsk.Weather(stage, w);
        }

        /// <summary>날씨 휠 연출(35행 — M6 「트레이너 카드 해금 연출」) 동안 위 띠 배지가 보이는 날씨(없으면 −1)와 아직 도는 중인가(확인용).</summary>
        public int WeatherShown { get; private set; } = -1;
        public bool WeatherSpinning { get; private set; }
        bool weatherSpun;

        // 날씨 휠 놓기·바꾸기(35행): 위 띠 날씨 배지가 돌림판처럼 날씨 넷을 돌다 고른 날씨에 멈추며 반짝(모든 좌석 — 박자 WeatherBeats)
        void ShowWeather(FxSequencer seq)
        {
            if (weather == null) return;
            var step = CardRows.Playing(seq, WeatherBeats.Row, out int ms);
            var e = CardRows.First<WeatherSet>(step);
            WeatherSpinning = false;
            if (e == null)
            {
                WeatherShown = -1;
                if (!weatherSpun) return;
                weatherSpun = false;
                weather.RemoveFromClassList("sn-hud-weather-pop");
                Refresh();
                return;
            }
            var pose = WeatherBeats.At(ms, e);
            weatherSpun = true;
            WeatherShown = pose.Shown;
            WeatherSpinning = pose.Spinning;
            weather.style.display = DisplayStyle.Flex;
            weather.text = WeatherText(pose.Shown);
            weather.EnableInClassList("sn-hud-weather-pop", pose.Scale > 1f);   // 멈추는 순간 배지가 밝아진다(픽셀 글자는 늘이지 않는다 — 이웃 배지를 덮지 않게)
        }

        /// <summary>화면 대기열의 지금 모습을 그린다(프레임마다 — 연출이 도는 동안만 불린다).</summary>
        public void ShowFx(FxSequencer seq)
        {
            if (band == null) return;
            band.Show(seq);
            actCard.Show(seq);
            ShowBang(seq);
            ShowShield(seq);
            ShowDid(seq);
            ShowFall(seq);
            ShowApricorns(seq);
            ShowHeal(seq);
            ShowHeart(seq);
            ShowShrine(seq);
            ShowDark(seq);
            ShowFate(seq);
            ShowWarp(seq);
            ShowReach(seq);
            ShowGate(seq);
            ub?.ShowFx(seq);
            ShowWeather(seq);
            PaintLog(false);
            if (flow.Battle == null && PartyHp() != paintedHp) Refresh();   // 체력이 착지했다(회복 — 표시 값): 파티 칸의 숫자를 바꾼다
            if (flow.Client?.View?.Hos != null && ActBeats.ShownAct(flow.Client.View.Hos.Act, seq) != paintedAct) Refresh();   // 막 카드가 착지했다: 막 배지를 바꾼다
            if (flow.Client?.View != null && ShieldMask(flow.Client.View) != paintedShield) Refresh();   // 보호가 걸렸다 · 풀리는 연출(9행)이 끝났다: 좌석 칩의 보호 배지
            if (ResShown() != paintedRes) FillRes();   // 규토리 · 여행 점수 · 볼이 착지했다(표시 값): 자원 줄의 숫자만 바꾼다
            if (flow.CanOpenParty != paintedMaint) Refresh();   // 전원 차단 연출(막 카드 · 조무래기 놓기)이 끝났다: [정비]를 켠다
            if (flow.Client?.View != null)
            {
                ShownTurn(flow.Client.View, out int seat, out int shownRound);
                if (seat != paintedSeat || shownRound != paintedRound) Refresh();   // 넘김 연출이 닿았다: 턴·라운드 배지를 바꾼다
            }
            var moves = actions?.Q<Label>("board-hud-moves");
            var client = flow.Client;
            if (moves != null && client?.View != null)
                moves.text = stage.T("screen.board.moves_left").Replace("{n}", ShownMoves(client.View.Seats[client.Seat]).ToString(CultureInfo.InvariantCulture));
        }

        // 심장 회복(94행 3막 — M5 「막·종막 화면」): 시작의 사당 칸 위에서 조각 셋(감정·지식·의지)이 원을 그리며 좁혀 들고, 모이면 신오의 심장이 톡 커졌다 자리 잡는다
        void ShowHeart(FxSequencer seq)
        {
            if (heart == null) return;
            var step = HeartBeats.StepOf(seq.Current);
            int t = step != null ? seq.LocalMs - step.StartMs : -1;
            HeartShown = false;
            if (step == null || t < 0 || t >= step.Ms || !flow.CellPoint(HosFlow.OriginShrineCell(flow.Ctx), out var at))
            {
                FxUi.Fade(heart, 0f);
                return;
            }
            var pose = HeartBeats.At(t, step.Ms);
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            FxUi.Move(heart, at.x, Mathf.Round(at.y - HeartAbovePx * unit));
            FxUi.Fade(heart, 1f);
            for (int i = 0; i < shards.Count; i++)
            {
                double a = pose.Angle + i * 2.0 * Math.PI / shards.Count;
                float r = pose.Radius * HeartRadiusPx * unit;
                FxUi.Move(shards[i], Mathf.Round((float)Math.Cos(a) * r), Mathf.Round((float)Math.Sin(a) * r * 0.6f));
                FxUi.Fade(shards[i], pose.Shards);
            }
            FxUi.Fade(heartIcon, pose.Heart);
            FxUi.Scale(heartIcon, pose.HeartScale, pose.HeartScale);
            HeartShown = pose.Shards > 0f || pose.Heart > 0f;
        }

        // 찾아보기(20행)의 예비: 내 말 머리 위 「!」가 튀어나와 머물다 사라진다(그 뒤 조우 창이 열린다)
        void ShowBang(FxSequencer seq)
        {
            var step = seq.Current != null ? seq.Current.Step(EnterRows.Search) : null;
            int t = step != null ? seq.LocalMs - step.StartMs : -1;
            var at = default(Vector2);
            BangShown = step != null && t >= 0 && t < Math.Min(step.Ms, EnterBeats.BangEndMs) && flow.Client != null && flow.PiecePoint(seq.Current.Seat >= 0 ? seq.Current.Seat : flow.Client.Seat, out at);   // 찾는 좌석의 말(항목의 좌석)
            // 같은 칸의 말을 만남(13행 — 걷기 항목 끝에 붙는다, 대기열 길이는 그대로): 걸어 들어간 칸에 다른 좌석의 말이 있으면 마지막 걸음이 닿을 때 그 말 머리 위에 「!」
            MeetShown = false;
            if (!BangShown && flow.Client != null)
            {
                int since = MeetBeats.Since(seq.Current, seq.LocalMs, out var met);
                if (since < 0 && seq.Last != null && !ReferenceEquals(seq.Last, seq.Current)) since = MeetBeats.Since(seq.Last, seq.Last.LengthMs + (int)Math.Min(seq.SinceLastMs, MeetBeats.Ms), out met);
                if (since >= 0 && since < MeetBeats.Ms && flow.SharedCell(met.Seat, met.Path[met.Path.Length - 1]) && flow.PiecePoint(met.Seat, out at))
                {
                    MeetShown = true;
                    t = since;
                }
            }
            if (!BangShown && !MeetShown)
            {
                FxUi.Fade(bang, 0f);
                return;
            }
            float s = Tween.At(t, 0, EnterBeats.BangPopMs, 0.4f, 1f, Ease.OutQuad);
            FxUi.Move(bang, at.x, at.y);
            FxUi.Scale(bang, s, s);
            FxUi.Fade(bang, 1f);
        }

        /// <summary>남 좌석의 공물·축복·제작·은행상회·진화 꼬리표(결정 80 N2 나 — <see cref="OtherActs"/>)가 걸린 좌석(없으면 −1)과 그 문구 키 꼬리.</summary>
        public int DidSeat { get; private set; } = -1;
        public string DidKey { get; private set; }

        /// <summary>좌석 칩 아래 꼬리표의 요소 이름.</summary>
        public static string DidName(int seat) => "board-hud-seat-" + (seat + 1).ToString(CultureInfo.InvariantCulture) + "-did";

        // 남 좌석의 공물·축복·제작·은행상회·진화(310·320·180·360·370·390행 — 그 좌석의 창은 내 화면에 없다): 그 단계가 도는 동안 그 좌석 칩 아래에 짧은 꼬리표가 떠올랐다 사라진다.
        // 칩은 갱신 때마다 다시 만들어지므로 프레임마다 없으면 다시 단다(보호 배지처럼 칩의 배치 밖 — 칩 폭이 그대로다)
        void ShowDid(FxSequencer seq)
        {
            var client = flow.Client;
            int count = client != null && client.View != null ? client.View.Seats.Length : 0;
            int t = -1;
            var step = client != null ? OtherActs.Playing(seq, client.Seat, count, out t) : null;
            int seat = step != null ? WorkRows.SeatOf(step) : -1;
            string key = step != null ? OtherActs.KeyOf(step) : null;
            if (seats == null) return;
            if (DidSeat >= 0 && (DidSeat != seat || DidKey != key)) seats.Q(DidName(DidSeat))?.RemoveFromHierarchy();
            DidSeat = seat;
            DidKey = key;
            if (seat < 0) return;
            var chip = seats.Q("board-hud-seat-" + (seat + 1).ToString(CultureInfo.InvariantCulture));
            if (chip == null) return;
            string text = stage.T("screen.board.did." + key);
            var stamp = step.Row == DealRows.Stamp ? StampBeats.Event(step) : null;   // 남의 스탬프(결정 178): 「스탬프 n/7」 — 잇달아 받으면 단계마다 수가 바뀐다
            if (stamp != null) text = text.Replace("{n}", stamp.CountAfter.ToString(CultureInfo.InvariantCulture));
            var tag = chip.Q(DidName(seat));
            if (tag == null)
            {
                tag = Add(chip, Kit.Badge(text, KitBadgeKind.Gold, Kit.F2), DidName(seat), "sn-hud-did");
                tag.pickingMode = PickingMode.Ignore;
            }
            else if (tag is Label shown && shown.text != text) shown.text = text;
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            float alpha = OtherActs.Alpha(t, step.Ms, out float rise);
            FxUi.Move(tag, 0f, Mathf.Round(rise * OtherActs.RisePx * unit));
            FxUi.Fade(tag, alpha);
        }

        // 도전 보호 해제(9행): 그 좌석 칩의 보호 배지가 밝아졌다가 금이 가고 아래로 떨어지며 사라진다(배지는 늘리지 않는다 — 픽셀 글자는 정수배만)
        void ShowShield(FxSequencer seq)
        {
            var step = ShieldBeats.StepOf(seq.Current);
            if (step == null || seats == null || seq.LocalMs < step.StartMs) return;
            var badge = seats.Q("board-hud-seat-" + (ShieldBeats.Seat(step) + 1).ToString(CultureInfo.InvariantCulture) + "-shield");
            if (badge == null) return;
            var pose = ShieldBeats.At(seq.LocalMs - step.StartMs, step.Ms);
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            badge.EnableInClassList("sn-hud-shield-lit", pose.Lit);
            badge.EnableInClassList("sn-hud-shield-crack", pose.Cracked);
            FxUi.Move(badge, 0f, Mathf.Round(pose.Fall * ShieldFallPx * unit));
            FxUi.Fade(badge, 1f - pose.Fall);
        }

        // 규토리 줍기(19행): 그 칸 위에서 규토리가 하나씩 떨어져 튕기고, 말 쪽으로 모이며 말 머리 위에 「규토리 +n」이 떠오른다
        void ShowApricorns(FxSequencer seq)
        {
            if (gain == null) return;
            var step = ApricornBeats.StepOf(seq.Current);
            int t = step != null ? seq.LocalMs - step.StartMs : -1;
            DropsShown = 0;
            var head = default(Vector2);
            int cell = -1;
            int seat = step != null ? ApricornBeats.Seat(step, out cell) : -1;
            if (step == null || t < 0 || t >= step.Ms || !flow.PiecePoint(seat, out head))
            {
                FxUi.Fade(gain, 0f);
                foreach (var d in drops) FxUi.Fade(d, 0f);
                return;
            }
            float unit = stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;
            var pose = ApricornBeats.At(t, step.Ms);
            var colors = ApricornBeats.Colors(step);
            // 규토리가 닿는 바닥 = 그 칸의 가운데(보드 없이 도는 흐름이면 머리 점 아래)
            if (!flow.CellPoint(cell, out var foot)) foot = head + new Vector2(0f, DropFromPx * unit);
            foot.y += DropFrontPx * unit;   // 말 앞쪽(화면 아래)에 떨어진다 — 얼굴을 가리지 않게
            for (int i = 0; i < drops.Count; i++)
            {
                float h = i < colors.Count ? ApricornBeats.Height(t, step.Ms, i) : -1f;
                if (h < 0f || pose.Gather >= 1f)
                {
                    FxUi.Fade(drops[i], 0f);
                    continue;
                }
                var tex = GenAssets.Load<Texture2D>(EncounterScreen.ApricornKey((ApricornColor)colors[i]));
                if (tex != null) drops[i].style.backgroundImage = new StyleBackground(tex);
                // 말의 양옆으로 번갈아 떨어진다(왼쪽 · 오른쪽 · 더 왼쪽 … — 가운데로 떨어지면 말의 얼굴을 지나간다)
                float side = (i % 2 == 0 ? -1f : 1f) * (i / 2 + 1);
                var at = foot + new Vector2(side * DropGapPx * unit, -h * DropFromPx * unit);
                at = Vector2.Lerp(at, head, pose.Gather);
                FxUi.Move(drops[i], Mathf.Round(at.x), Mathf.Round(at.y));
                FxUi.Fade(drops[i], 1f - pose.Gather);
                DropsShown++;
            }
            gainText.text = stage.T("screen.board.summary.apricorns").Replace("{n}", ApricornBeats.Count(step).ToString(CultureInfo.InvariantCulture));
            FxUi.Move(gain, head.x, Mathf.Round(head.y - pose.Rise * GainRisePx * unit));
            FxUi.Fade(gain, pose.Label);
        }

        // 회복(8·17행): 내 파티 칸이 징글 박자로 하나씩 반짝인다(체력은 단계 끝에 표시 값으로 한꺼번에 찬다)
        void ShowHeal(FxSequencer seq)
        {
            var client = flow.Client;
            if (party == null || client == null || client.View == null) return;
            var step = HealBeats.StepOf(seq.Current);
            var heal = step != null && seq.LocalMs >= step.StartMs ? HealBeats.Healing(step) : null;
            var me = client.View.Seats[client.Seat];
            sheets?.Begin();
            for (int i = 0; i < me.Party.Length; i++)
            {
                var flash = party.Q("board-hud-mon-" + i.ToString(CultureInfo.InvariantCulture) + "-flash");
                if (flash == null) continue;
                float f = 0f;
                if (heal != null && heal.Seat == client.Seat)
                {
                    int id = me.Party[i];
                    int k = Array.FindIndex(heal.Pokemon, p => p != null && p.Id == id);
                    if (k >= 0) f = HealBeats.Flash(step.Local(seq.LocalMs), step.Ms, k, heal.Pokemon.Length);
                    // 회복 반짝임(결정 69): 칸이 반짝이기 시작할 때 칸 왼쪽의 도트 위에서 오른다
                    var box = flash.worldBound;
                    if (k >= 0 && sheets != null && !float.IsNaN(box.width) && box.width > 0f)
                    {
                        var at = sheets.Element.WorldToLocal(new Vector2(box.xMin + box.height * 0.5f, box.yMin + box.height * 0.4f));
                        sheets.Add(SheetBeats.Heal, SheetBeats.HealMs(seq.LocalMs - step.StartMs, step.Ms, k, heal.Pokemon.Length), Mathf.Round(at.x), Mathf.Round(at.y), stage.Grade.TokenHeight >= 1080 ? 3f : 2f);
                    }
                }
                FxUi.Fade(flash, f);
            }
            sheets?.End();
        }

        // 귀환(71행)의 암전: 보드가 어두워졌다 걷힌다(Linear 보정 — 막 카드의 어둠과 같은 값)
        void ShowDark(FxSequencer seq)
        {
            if (dark == null) return;
            var step = WhiteoutBeats.StepOf(seq.Current);
            float d = step != null && seq.LocalMs >= step.StartMs ? WhiteoutBeats.At(step.Local(seq.LocalMs), step.Ms).Dark : 0f;
            if (shrineFx != null) d = Mathf.Max(d, shrineFx.DarkShown * ShrineDark);   // 전설 이벤트 · 신 강림(33·34행)의 어두워진 하늘 — 내려앉는 피규어가 보이게 귀환의 암전보다 옅다
            FxUi.Fade(dark, LinearAlpha.Dim(ActCard.DimAlpha * d) / LinearAlpha.Dim(ActCard.DimAlpha));
        }

        /// <summary>지금 어둠(71행)이 보이는 만큼(0 ~ 1 — 확인용).</summary>
        public float DarkShown => dark != null ? dark.style.opacity.value : 0f;

        // 내 파티의 그리는 체력(표시 값)의 지문 — 바뀌면 파티 칸을 다시 그린다
        long PartyHp()
        {
            var client = flow.Client;
            var view = client != null ? client.View : null;
            if (view == null) return 0;
            long h = 17;
            foreach (int id in view.Seats[client.Seat].Party)
            {
                if (id < 0) continue;
                int hp = PokemonRules.Hp(flow.Ctx, view.Pokemon[id]);
                h = h * 31 + (flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Hp(id), hp) : hp);
                h = h * 31 + EvolveScreen.ShownDef(flow, id, view.Pokemon[id].Def);   // 진화 연출이 착지하면 진화 뒤 체력으로
            }
            return h;
        }

        // ── 입력 ──

        /// <summary>
        /// 마우스가 그 칸 위에 있다(−1 = 보드 밖·HUD 위). 갈 수 있는 칸이면 거기까지의 경로를 칠한다.
        /// stack = 마우스 아래 묶음 표지의 칸(없으면 −1): 공개 정보 카드는 그 칸의 것을 띄운다 — 표지는 마우스를 받지 않아 칸 정보 줄·경로·누르기는 표지 밑의 칸 그대로다.
        /// figure = 마우스 아래 UB · 네크로즈마 · 호수 수호신 피규어의 칸(없으면 −1 — 결정 179): 칸 정보 줄 · 공개 정보 카드는 그 칸을 적고, 경로 · 누르기는 땅의 칸 그대로다.
        /// 피규어가 연출로 옮겨 가거나 폼 · 시련이 바뀌면(<see cref="GameFlow.FigureLook"/>) 마우스가 그대로여도 다시 적는다.
        /// </summary>
        public void Hover(int cell, int stack = -1, int figure = -1)
        {
            int show = stack >= 0 ? stack : figure >= 0 ? figure : cell;
            int line = figure >= 0 ? figure : cell, look = flow.FigureLook();
            if (line != hoverAny || show != hoverInfo || look != hoverLook)
            {
                hoverAny = line;
                hoverInfo = show;
                hoverLook = look;
                var view = flow.Client != null ? flow.Client.View : null;
                if (cellInfo != null && view != null) cellInfo.text = CellText(InfoCell(view.Seats[flow.Client.Seat].Cell));
                if (infoSeat < 0)
                {
                    if (show >= 0) ShowCellInfo(show);
                    else HideInfo();
                }
            }
            int next = cell >= 0 && reach.Contains(cell) ? cell : -1;
            if (next == hover) return;
            hover = next;
            Mark();
        }

        /// <summary>
        /// 칸을 눌렀다(마우스 — 걷기 전 확인, <see cref="PickedCell"/>): 고른 칸을 다시 누르면 걷고, 경유 칸을 다시 누르면 그 칸을 빼고, 갈 수 있는 다른 칸이면 그 칸을 거쳐 가는 길로
        /// 바꾸고(<see cref="AddVia"/> — 이동력을 넘으면 그 칸을 새로 고른다), 그 밖의 칸(−1 = 보드 밖)이면 고른 것을 푼다.
        /// 돌려주는 값 = 걸었나(이동 답이 받아들여졌다) — 고르기만 했으면 false.
        /// </summary>
        public bool ClickCell(int cell)
        {
            if (!flow.InputOpen) return false;
            if (warps.Count > 0) return Warp(cell);
            if (abilityPicks.Count > 0) return AbilityAt(cell);   // 능력 고르기 중: 칠한 칸만(그 밖은 아무것도 하지 않는다 — [취소]·오른쪽 클릭으로 그만둔다)
            if (CardCells) return CardAt(cell);   // team_rocket R 날아갈 칸
            if (cell >= 0 && cell == picked) return ConfirmMove();
            if (cell >= 0 && vias.Contains(cell))
            {
                vias.Remove(cell);
                Refresh();
                return false;
            }
            if (AddVia(cell)) return false;
            if (!PickCell(cell)) Unpick();
            return false;
        }

        /// <summary>갈 수 있는 칸을 고른다(걷지 않는다): 길을 칠하고 아래 판에 이동력과 [이동]을 올린다. 다른 칸을 고르면 경유 칸은 비운다. 입력이 닫혀 있거나 갈 수 없는 칸이면 false.</summary>
        public bool PickCell(int cell)
        {
            if (!flow.InputOpen || !reach.Contains(cell) || flow.Client.Choice(ChoiceKind.MapAction) == null) return false;
            if (picked == cell) return true;
            picked = cell;
            paidCell = -1;
            vias.Clear();
            Refresh();
            return true;
        }

        /// <summary>
        /// 결정 71 ② 경유 칸을 더한다: 고른 칸이 있고, 그 칸이 갈 수 있는 다른 칸이고, 지금 경유 칸 다음에 그 칸을 거쳐 고른 칸에 가는 길이 남은 이동력 안이면 더하고 true.
        /// 아니면 아무것도 바꾸지 않고 false(마우스는 그때 그 칸을 새로 고른다 — <see cref="ClickCell"/>).
        /// </summary>
        public bool AddVia(int cell)
        {
            if (!flow.InputOpen || picked < 0 || cell == picked || !reach.Contains(cell) || vias.Contains(cell)) return false;
            var next = new List<int>(vias) { cell };
            if (RouteOf(next, picked) == null) return false;
            vias.Add(cell);
            Refresh();
            return true;
        }

        /// <summary>고른 칸을 푼다(경유 칸도). 고른 칸이 있었으면 true.</summary>
        public bool Unpick()
        {
            if (picked < 0) return false;
            picked = -1;
            paidCell = -1;
            vias.Clear();
            Refresh();
            return true;
        }

        /// <summary>한 단계 물린다(오른쪽 단추 · Esc — 결정 71 ② [가정]): 마지막 경유 칸을 빼고, 경유 칸이 없으면 고른 칸을 푼다. 물린 것이 있으면 true.</summary>
        public bool Back()
        {
            if (warps.Count > 0) return CancelWarp();
            if (abilityPicks.Count > 0) return CancelAbility(false);
            if (vias.Count == 0) return Unpick();
            vias.RemoveAt(vias.Count - 1);
            Refresh();
            return true;
        }

        /// <summary>[이동] · 고른 칸을 다시 누름: 고른 길(경유 칸 포함)로 이동하는 답을 낸다(<c>ChoiceAnswer.MoveAlong</c> — 길 위의 숨겨진 장소마다 멈춰 묻는다, 결정 71). 받아들여졌으면 true.</summary>
        public bool ConfirmMove()
        {
            if (picked < 0 || !flow.InputOpen || !reach.Contains(picked)) return false;
            var choice = flow.Client.Choice(ChoiceKind.MapAction);
            var gate = choice != null && vias.Count == 0 ? GateTo(choice, picked) : null;   // 왜곡 게이트 건너편(15행): 걷지 않고 그 답 그대로
            var path = gate == null ? PickedPath : null;
            if (choice == null || (path == null && gate == null)) return false;
            var answer = gate ?? ChoiceAnswer.MoveAlong(path);
            if (gate == null && IsFree(path)) answer.Arg = TrainerRules.FreeArg;   // 무료 지형 이동(rulings A36) — 게이트 이동은 Arg = 건너편 게이트 그대로
            picked = -1;
            paidCell = -1;
            vias.Clear();
            hover = -1;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>그 칸이 지금 선 왜곡 게이트의 건너편이면 그 게이트 이동 답(Move · Arg = 건너편 게이트 — p.26, 이동력을 쓰지 않는다), 아니면 null.</summary>
        ChoiceAnswer GateTo(PendingChoice choice, int cell)
        {
            var view = flow.Client.View;
            foreach (var a in flow.Client.Legal(choice))
                if (a.Kind == AnswerKind.Move && a.Target == cell && UltraBurstSetup.IsTravel(view, flow.Ctx, flow.Client.Seat, a)) return a;
            return null;
        }

        /// <summary>확인용(길잡이 · 캡처 · 시험): 그 칸을 고르고 바로 걷는다 — 화면의 두 단계(<see cref="PickCell"/> → <see cref="ConfirmMove"/>)를 그대로 지난다.</summary>
        public bool Walk(int cell) => PickCell(cell) && ConfirmMove();

        /// <summary>확인용: 그 칸을 고르고 경유 칸을 차례로 더한 뒤 걷는다(결정 71 ② — 마우스의 누름 순서와 같다). 하나라도 거절되면 걷지 않고 false.</summary>
        public bool WalkVia(int cell, params int[] through)
        {
            if (!PickCell(cell)) return false;
            foreach (int c in through)
                if (!AddVia(c)) return false;
            return ConfirmMove();
        }

        /// <summary>단추를 눌렀다(이름 = <see cref="Act.Id"/>). 그 종류의 첫 합법 답을 낸다([마을]은 답 대신 마을 창을 연다). 아직 준비 중인 단추·입력이 닫힌 때·판에 없는 단추면 false.</summary>
        public bool Press(string id)
        {
            if (!flow.InputOpen || !shownActs.Contains(id)) return false;
            var client = flow.Client;
            if (id == Trade)
            {
                stage.Tips.Hide();
                return flow.OpenTrade();
            }
            if (id == Ability || id == Weather) return BeginAbility(id == Weather);
            if (id == Dive) return BeginDive();
            var choice = client.FirstChoice();
            if (choice == null) return false;
            var acts = choice.Kind == ChoiceKind.MapAction ? MapActs : choice.Kind == ChoiceKind.HiddenSpot ? SpotActs : null;
            var act = acts != null ? Array.Find(acts, a => a.Id == id) : null;
            if (act == null || !act.Ready) return false;
            if (act.Window)
            {
                stage.Tips.Hide();
                return id == Craft ? flow.OpenCraft() : id == Evolve ? flow.OpenEvolve() : id == Offer ? flow.OpenShrine() : id == Fly ? flow.OpenWhistle() : flow.OpenVillage();
            }
            var answer = client.Legal(choice).Find(a => a.Kind == act.Kind);
            if (answer == null) return false;
            stage.Tips.Hide();
            // [도전]: 상대가 여럿이거나 원격 도전이 있으면 고르는 창(S35)을 거친다
            if (id == Challenge && flow.OpenChallenge()) return true;
            return flow.Answer(choice, answer).Accepted;
        }

        /// <summary>
        /// 그 점(패널 좌표 — 왼쪽 위가 (0, 0))이 HUD 판의 사각형(아래 판 · 내 자원 줄 · 상태 판 · 좌석 칩) 안인가. 확인·시험이 「HUD에 가린 칸」을 가르는 데 쓴다 —
        /// 그 밖에서는 PC 층이 마우스를 받지 않아야 한다(보이지 않는 넘김 띠·요약 판 포함, <see cref="GameFlow.OverUi"/>).
        /// </summary>
        public static bool UnderPanel(VisualElement root, Vector2 panelPoint)
        {
            var dock = root.Q("board-hud-dock");
            var status = root.Q("board-hud-status");
            var log = root.Q("board-hud-log");
            if (log != null && log.worldBound.Contains(panelPoint)) return true;
            var res = root.Q(Res);   // 내 자원 줄(결정 98 — 아래 판 위에 얹은 띠)
            if (res != null && res.worldBound.Contains(panelPoint)) return true;
            if ((dock != null && dock.worldBound.Contains(panelPoint)) || (status != null && status.worldBound.Contains(panelPoint))) return true;
            var seats = root.Q("board-hud-seats");
            if (seats != null)
                foreach (var chip in seats.Children())
                    if (chip.worldBound.Contains(panelPoint)) return true;
            return false;
        }

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
