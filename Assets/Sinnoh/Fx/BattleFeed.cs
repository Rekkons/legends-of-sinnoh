using System;
using System.Collections.Generic;
using System.Globalization;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 한국어 조사 고르기: 문장 안의 「은(는)」·「이(가)」·「을(를)」·「과(와)」·「(으)로」를 바로 앞 글자의 받침에 맞춰 하나로 줄인다
    /// (「렌트라은(는)」 → 「렌트라는」). 앞 글자가 숫자면 읽는 소리(「1」 = 일)의 받침에 맞추고, 한글 음절도 숫자도 아니면(로마자·없음) 그대로 둔다 — 틀린 조사를 고르지 않는다.
    /// 「(으)로」는 받침이 없거나 ㄹ 받침이면 「로」다. 이름이 닫는 괄호로 끝나면(「깝질무 (동쪽바다 폼)」) 괄호 안 마지막 글자에 맞춘다.
    /// </summary>
    public static class KoJosa
    {
        static readonly string[][] Pairs =
        {
            new[] { "은(는)", "은", "는" }, new[] { "이(가)", "이", "가" }, new[] { "을(를)", "을", "를" }, new[] { "과(와)", "과", "와" }, new[] { "(으)로", "으로", "로" },
        };

        // 숫자 0~9를 읽는 소리(영·일·이·삼·사·오·육·칠·팔·구)의 받침: 0 = 없음, 8 = ㄹ, 그 밖 = 있음(값은 한글 음절의 받침 번호 — ㄹ만 가린다)
        static readonly int[] DigitFinal = { 21, 8, 0, 16, 0, 0, 1, 8, 8, 0 };

        /// <summary>
        /// 조사 고르기는 한국어 글에만 건다(M9 언어 ②): 한글 음절이 하나도 없는 글(영어 표의 글 — strings.en에는 한글이 없다, 빌드 <c>checkLanguagePair</c>)에는
        /// 영어 복수형 한 규칙만 건다 [가정 — plan §5 #381]: 「숫자 + 낱말 1~3개 + (s)」는 숫자가 1이면 「(s)」를 빼고 아니면 「s」(「1 status symbol(s)」 → 「1 status symbol」 · 「3 Apricorn(s)」 → 「3 Apricorns」).
        /// 언어를 따로 받지 않는 까닭: 한 프로세스에 좌석마다 다른 언어의 무대가 함께 돌 수 있다(결정 34 — 시험 · 캡처).
        /// </summary>
        public static string Fix(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('(') < 0) return text;
            if (!HasHangul(text)) return text.IndexOf("(s)", StringComparison.Ordinal) < 0 ? text : EnPlural.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + (m.Groups[1].Value == "1" ? "" : "s"));
            foreach (var p in Pairs)
            {
                int at = 0;
                while ((at = text.IndexOf(p[0], at, StringComparison.Ordinal)) >= 0)
                {
                    int k = at - 1;
                    while (k > 0 && (text[k] == ')' || text[k] == '」' || text[k] == '』')) k--;   // 닫는 괄호·낫표는 건너뛴다(「침입」이)
                    char before = k >= 0 ? text[k] : ' ';
                    int final;
                    if (before >= '0' && before <= '9') final = DigitFinal[before - '0'];   // 숫자는 읽는 소리의 받침(「플레이어 1은」·「2는」)
                    else if (before < 0xAC00 || before > 0xD7A3) { at += p[0].Length; continue; }
                    else final = (before - 0xAC00) % 28;
                    bool closed = final != 0 && !(p[0] == "(으)로" && final == 8);   // 8 = ㄹ
                    string pick = closed ? p[1] : p[2];
                    text = text.Substring(0, at) + pick + text.Substring(at + p[0].Length);
                    at += pick.Length;
                }
            }
            return text;
        }

        static readonly System.Text.RegularExpressions.Regex EnPlural = new System.Text.RegularExpressions.Regex(@"\b(\d+)((?: [A-Za-z]+){1,3})\(s\)");

        static bool HasHangul(string text)
        {
            foreach (char c in text)
                if (c >= 0xAC00 && c <= 0xD7A3) return true;
            return false;
        }
    }

    /// <summary>
    /// 기록 줄의 빈칸 하나: <c>{Name}</c> 자리에 들어갈 것. <see cref="Keys"/>가 있으면 문자열 표의 글(여럿이면 「, 」로 잇는다), 없으면 <see cref="Value"/> 그대로(숫자).
    /// </summary>
    public sealed class BattleArg
    {
        public string Name;
        public string Value;
        public string[] Keys;
    }

    /// <summary>
    /// 배틀 기록 한 줄(04 §4-4 오른쪽 기둥, ui-text-guide §3 Pt 문체): 문자열 표의 키 + 빈칸. 글은 화면이 문자열 표로 만든다(<see cref="Text"/>) — 여기에는 한국어가 없다.
    /// <see cref="Side"/> = 그 줄의 주인 쪽(배틀 Side 0·1, 어느 쪽도 아니면 −1 — 줄 머리 색).
    /// </summary>
    public sealed class BattleLine
    {
        public string Key;
        public int Side = -1;
        /// <summary>이 줄을 낸 이벤트 — 화면은 그 이벤트가 연출에서 드러난 뒤에 줄을 붙인다(<see cref="FxReveal"/>, 결과가 먼저 새지 않게).</summary>
        public GameEvent Event;
        public readonly List<BattleArg> Args = new List<BattleArg>();

        public BattleLine With(string name, string key)
        {
            Args.Add(new BattleArg { Name = name, Keys = new[] { key } });
            return this;
        }

        public BattleLine WithKeys(string name, string[] keys)
        {
            Args.Add(new BattleArg { Name = name, Keys = keys });
            return this;
        }

        public BattleLine WithValue(string name, int value)
        {
            Args.Add(new BattleArg { Name = name, Value = value.ToString(CultureInfo.InvariantCulture) });
            return this;
        }

        /// <summary>문자열 표(<paramref name="t"/>: 키 → 글)로 줄을 만든다. 조사는 들어간 이름에 맞춘다.</summary>
        public string Text(Func<string, string> t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            string s = t(Key);
            foreach (var a in Args)
            {
                string v = a.Value;
                if (a.Keys != null)
                {
                    var parts = new string[a.Keys.Length];
                    for (int i = 0; i < parts.Length; i++) parts[i] = t(a.Keys[i]);
                    v = string.Join(", ", parts);
                }
                s = s.Replace("{" + a.Name + "}", v ?? "");
            }
            return KoJosa.Fix(s);
        }
    }

    /// <summary>배틀 화면이 그릴 포켓몬 하나의 지금 값(체력 상자 · 기둥). 없으면 <see cref="Id"/> = −1.</summary>
    public struct BattleMon
    {
        public int Id;
        /// <summary>포켓몬 표 색인.</summary>
        public int Def;
        public int Hp, MaxHp, Fatigue;
        public StatusEffect Status;
        /// <summary>둘째 상태 이상(동시에 둘 거는 기술만 — rulings G20). 없으면 None.</summary>
        public StatusEffect Status2;
        /// <summary>보정을 넣은 스피드와 카드 값(보정이 없으면 같다).</summary>
        public int Speed, BaseSpeed;
        public bool Exists => Id >= 0;
        public bool Fainted => Exists && Hp <= 0;
    }

    /// <summary>
    /// 배틀 하나를 화면이 따라가는 곳(04 §4 — 내 좌석이 받은 갱신만으로 만든다): 배틀 기록 줄, 마지막 피해 계산, 끝난 결과.
    /// <c>BattleStarted</c>가 든 갱신부터 받는다(<see cref="Feed"/>). 배틀이 끝나면 보기에서 배틀이 사라지므로(<c>GameState.Battle</c> = null, 갤럭시단 포켓몬은 자리도 비워진다)
    /// 마지막으로 배틀이 살아 있던 보기(<see cref="Live"/>)와 이벤트가 실은 마지막 체력·피로를 들고 있다가 결과 창이 그 모습을 그린다.
    /// 값을 읽는 곳은 <see cref="Mon"/> 하나다 — 살아 있는 배틀은 보기에서 읽고(연출 단계에서는 여기서 표시 값으로 바꾼다), 끝난 배틀은 붙잡아 둔 값에서 읽는다.
    /// 규칙을 세지 않는다: 줄은 이벤트가 실은 값 그대로다.
    /// </summary>
    public sealed class BattleFeed
    {
        /// <summary>기록에 남기는 줄 수(넘으면 오래된 줄부터 버린다 — 기둥에 보이는 것은 끝의 몇 줄이다).</summary>
        public const int MaxLines = 200;

        public const string K = "screen.battle.log.";

        /// <summary>폼 바꾸기 줄의 새 폼 인자 키 앞머리(「form:pokemon.421:2」 — 화면은 폼 꼬리만 적는다).</summary>
        public const string FormArg = "form:";

        readonly Ctx ctx;
        readonly Dictionary<int, int> species = new Dictionary<int, int>();
        readonly Dictionary<int, int> hp = new Dictionary<int, int>();
        readonly Dictionary<int, int> fatigue = new Dictionary<int, int>();

        /// <summary>보는 좌석.</summary>
        public readonly int Seat;

        public int BattleId { get; private set; } = -1;
        public BattleKind Kind { get; private set; }
        /// <summary>배틀이 벌어진 칸(BattleStarted.Cell — 모르면 -1). 조무래기 그림 고르기(<see cref="FoeArt"/>)가 읽는다.</summary>
        public int Cell { get; private set; } = -1;
        public Biome Biome { get; private set; }
        public Climate Climate { get; private set; }

        /// <summary>내 좌석의 쪽(0·1). 내가 당사자가 아니면 0(도전자 시점, 04 §4-6 [가정]).</summary>
        public int MySide { get; private set; }

        /// <summary>내 좌석이 당사자인가.</summary>
        public bool Mine { get; private set; }

        /// <summary>결과 창이 받은 것을 적는 좌석(플레이 시험 T16 — 남의 배틀도 그 사람 화면과 같게): 내 배틀이면 나, 남의 배틀이면 아래쪽(쪽 0)의 주인. 모르면 −1.</summary>
        public int Subject { get; private set; } = -1;

        public bool Started => BattleId >= 0;
        public bool Ended { get; private set; }
        /// <summary>이긴 쪽(없으면 −1).</summary>
        public int Winner { get; private set; } = -1;
        public BattleResult Result { get; private set; }
        /// <summary>이긴 좌석이 받은 것(JP·규토리).</summary>
        public ResChange[] Changes { get; private set; } = new ResChange[0];
        /// <summary>이 배틀로 내 좌석이 막 목표를 이뤘다(1막 해방 = 1). 아니면 −1.</summary>
        public int GoalMetAct { get; private set; } = -1;
        /// <summary>갤럭시단 배틀의 상대(조무래기 · 간부 · 태홍 — 명단 공개 GalacticBattle).</summary>
        public GalacticLeader Leader { get; private set; } = GalacticLeader.Grunt;
        /// <summary>간부·태홍 배틀의 그 인물(명단 첫 카드의 트레이너 — 앞모습 그림 <see cref="FoeArt.For"/>). 조무래기 · 명단을 못 본 좌석은 None.</summary>
        public GalacticTrainer Foe { get; private set; } = GalacticTrainer.None;
        /// <summary>갤럭시단 상대의 문자열 키 꼬리(조무래기 「」 · 간부 「.commander」 · 태홍 「.cyrus」).</summary>
        public string LeaderSuffix => Leader == GalacticLeader.Commander ? ".commander" : Leader == GalacticLeader.Cyrus ? ".cyrus" : "";
        /// <summary>이 배틀로 내 좌석이 받은 조각(Shard 값 — 2막 간부, 94행). 없으면 −1.</summary>
        public int ShardGot { get; private set; } = -1;
        /// <summary>이 배틀에서(배틀 중 · 배틀 끝과 함께) 내가 받은 여행 스탬프(받은 순서 — 결과 창의 줄, 47행).</summary>
        public readonly List<StampGained> Stamps = new List<StampGained>();
        /// <summary>이 배틀에서(배틀 중 · 배틀 끝과 함께) 오른 내 트레이너 카드 등급(받은 순서 — 결과 창의 「○○ 카드 실버 해금!」 줄, 70행 · M6 「트레이너 카드 해금 연출」).</summary>
        public readonly List<CardUnlocked> Unlocks = new List<CardUnlocked>();
        /// <summary>이 배틀로 열린 내 파티 칸 수(2막 조각 — A2·A3).</summary>
        public int SlotsOpened { get; private set; }
        /// <summary>이 배틀 뒤 공개된 막(없으면 −1).</summary>
        public int RevealedAct { get; private set; } = -1;
        /// <summary>내 좌석이 쓸 포켓몬이 없어 고향 마을로 돌아갔다.</summary>
        public bool Whiteout { get; private set; }

        /// <summary>그때 옮겨 간 칸(team_rocket R 「travel to any tile」이면 고른 칸 — 고향 마을이 아닐 수 있다). 없으면 −1.</summary>
        public int WhiteoutTo { get; private set; } = -1;

        /// <summary>가장 새 보기와 마지막으로 배틀이 살아 있던 보기.</summary>
        public GameState View { get; private set; }
        public GameState Live { get; private set; }

        public readonly List<BattleLine> Lines = new List<BattleLine>();

        /// <summary>이 배틀에서 받은 이벤트(받은 순서, 배틀 끝까지). 화면이 「지금 드러난 모습」을 다시 셀 때 쓴다(<see cref="See"/>).</summary>
        public readonly List<GameEvent> Events = new List<GameEvent>();
        GameEvent taking;
        /// <summary>마지막 굴림의 주사위 줄과 그 눈(면 바꾸기가 오면 이 줄을 고친다).</summary>
        BattleLine diceLine;
        readonly List<AttackDieFace> diceFaces = new List<AttackDieFace>();

        /// <summary>마지막 피해 계산(독의 계산 줄). 없으면 null.</summary>
        public DamageBreakdown LastDamage { get; private set; }

        /// <summary>우선권 토큰을 가진 쪽(없으면 −1).</summary>
        public int TokenSide { get; private set; } = -1;

        /// <summary>마지막 포획 시도(독의 계산 줄 — 그 뒤에 피해 계산이 오면 비운다). 없으면 null.</summary>
        public CaptureAttempt LastCapture { get; private set; }

        /// <summary>이 배틀에서 잡은 포켓몬(없으면 −1). 파티가 가득 차 아직 파티 밖이어도 여기에 있다.</summary>
        public int CaughtId { get; private set; } = -1;

        /// <summary>상대 쪽에 마지막으로 나온 포켓몬(결과 창의 「○○을(를) 잡았다!」·「야생 ○○은(는) 도망쳤다!」). 없으면 −1.</summary>
        public int FoeId { get; private set; } = -1;

        /// <summary>야생이 도망치며 남긴 것(규토리). 도망치지 않았으면 빈 배열.</summary>
        public ResChange[] FledChanges { get; private set; } = new ResChange[0];

        /// <summary>배틀 뒤 놓아준 포켓몬의 이름 키(파티 가득 — 없으면 null)와 가르친 기술(없으면 null).</summary>
        /// <summary>트레이너 배틀 승리 보상으로 가져온 것(46행 — 배틀 끝 뒤의 답, 내가 이긴 좌석일 때만. 없으면 null).</summary>
        public SpoilsTaken Spoils { get; private set; }

        public string ReleasedKey { get; private set; }
        public MoveTaught Taught { get; private set; }

        // ── 울트라 버스트(M6 「UB 배틀 화면 묶음」 — 88~92행) ──

        /// <summary>이 배틀의 UB 격파로 옮겨 간 프리즘 아머(88행 — 결과 창의 줄). 없으면 null.</summary>
        public PrismMoved PrismMove { get; private set; }

        /// <summary>이 배틀로 쓰러뜨리거나 잡은 호수 수호신(92행 — 결과 창 「모두 이동력 +2」). 없으면 null.</summary>
        public GuardianDown Guardian { get; private set; }

        /// <summary>이 배틀로 쓰러뜨린 네크로즈마 페이즈(90행 — 배틀 끝 뒤에 온다). 없으면 null.</summary>
        public NecrozmaPhase PhaseDown { get; private set; }

        /// <summary>이 배틀 끝과 함께 게임이 끝났다(네크로즈마 3페이즈 격파 · 모두 탈락). 아니면 null.</summary>
        public GameEnded GameEnd { get; private set; }

        /// <summary>시련 배틀의 호수(0 감정 · 1 지식 · 2 의지 — 수호신이 나오면 안다). 아니면 −1.</summary>
        public int TrialLake { get; private set; } = -1;

        /// <summary>배틀 종류마다 상대 쪽 문자열 키 꼬리(<c>screen.battle.foe.&lt;꼬리&gt;</c> · 기록 줄 <c>send.&lt;꼬리&gt;</c>): 울트라 비스트 「ub」 · 네크로즈마 「necrozma」 · 시련 「trial」, 그 밖은 null.</summary>
        public static string UbFoe(BattleKind kind) => kind == BattleKind.UltraBeast ? "ub" : kind == BattleKind.Necrozma ? "necrozma" : kind == BattleKind.Trial ? "trial" : null;

        public BattleFeed(Ctx ctx, int seat)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Seat = seat;
        }

        /// <summary>내가 이겼나(끝난 뒤).</summary>
        public bool Won => Ended && Winner == MySide;

        /// <summary>승패가 갈리지 않았나(무승부 — 끝난 뒤).</summary>
        public bool Drawn => Ended && Winner < 0;

        /// <summary>갱신 하나를 받는다: 보기와 그 갱신의 이벤트(받은 순서).</summary>
        public void Feed(GameState view, IReadOnlyList<GameEvent> events)
        {
            if (view != null)
            {
                View = view;
                if (view.Battle != null && !Ended) Live = view;
            }
            if (events == null) return;
            foreach (var e in events) Take(e);
            if (Lines.Count > MaxLines) Lines.RemoveRange(0, Lines.Count - MaxLines);
        }

        void Take(GameEvent e)
        {
            if (e is BattleStarted bs)
            {
                if (Started) return;   // 한 번에 배틀 하나
                BattleId = bs.BattleId;
                Kind = bs.Kind;
                Cell = bs.Cell == GameEvent.U ? -1 : bs.Cell;
                Biome = bs.Biome;
                Climate = bs.Climate;
                MySide = 0;
                Mine = false;
                if (bs.Sides != null)
                    for (int k = 0; k < bs.Sides.Length; k++)
                        if (bs.Sides[k] != null && bs.Sides[k].Owner == Seat) { MySide = k; Mine = true; }
                Subject = Mine ? Seat : bs.Sides != null && bs.Sides.Length > 0 && bs.Sides[0] != null ? bs.Sides[0].Owner : -1;
                taking = e;   // 이 줄은 배틀 시작(조우 전환 22행)과 함께 드러난다
                if (Kind == BattleKind.Galactic) Add(new BattleLine { Key = K + "start.galactic", Side = 1 - MySide });
                else if (Kind == BattleKind.Alpha) Add(new BattleLine { Key = K + "start.alpha", Side = 1 - MySide });   // 우두머리(77행) · 무리(24행) — M5 「맵 결과·배틀 안 연출」
                else if (Kind == BattleKind.Swarm) Add(new BattleLine { Key = K + "start.swarm", Side = 1 - MySide });
                return;
            }
            if (!Started) return;
            taking = e;
            if (e is StampGained stamp && stamp.Seat == Subject) Stamps.Add(stamp);   // 배틀 중·배틀 끝에 받은 여행 스탬프(47행 — 결과 창의 줄, 결정 74 · 남의 배틀은 그 좌석의 것 — T16)
            if (e is CardUnlocked unlock && unlock.Seat == Subject) Unlocks.Add(unlock);  // 배틀 중·배틀 끝에 오른 트레이너 카드(70행 결과 창 해금 줄 — 큰 연출 48행은 [확인] 뒤 보드에서)
            if (Ended)
            {
                // 배틀 끝과 같은 갱신에 이어 오는 것: 1막 해방 · 고향 마을로 · 다음 막 공개. 배틀 뒤 선택의 답: 놓아주기 · 기술 가르치기
                if (e is ActGoalMet g && g.Seat == Seat) GoalMetAct = g.Act;
                else if (e is ShardGained sg && sg.Seat == Seat) ShardGot = (int)sg.Shard;   // 2막(94행): 간부를 이겨 받은 조각 · 열린 파티 칸
                else if (e is PartySlotUnlocked ps && ps.Seat == Seat) SlotsOpened += Math.Max(0, ps.After - ps.Before);
                else if (e is Released rl) ReleasedKey = rl.Species >= 0 ? "pokemon." + ctx.Cards.Pokemon[rl.Species].Key : K + "mon.unknown";
                else if (e is MoveTaught mt) Taught = mt;
                else if (e is SpoilsTaken sp && sp.Seat == Seat) Spoils = sp;
                else if (e is Whiteout w && w.Seat == Seat) { Whiteout = true; WhiteoutTo = w.ToCell; }
                else if (e is ActRevealed r) RevealedAct = r.ActKey;
                else if (e is NecrozmaPhase np) PhaseDown = np;   // 90행: 페이즈 격파는 배틀 끝 뒤(결과 창의 줄 · 보드 연출은 [확인] 뒤)
                else if (e is GameEnded ge) GameEnd = ge;
                return;
            }
            Events.Add(e);
            switch (e)
            {
                case GalacticBattle gb:
                    // 2막 간부 · 3막 태홍(M5 「막·종막 화면」): 명단 공개가 배틀 시작 뒤에 온다 — 시작 줄을 그 인물의 줄로 바꾼다
                    Leader = gb.Leader;
                    if (gb.Lineup != null && gb.Lineup.Length > 0 && gb.Lineup[0] >= 0 && gb.Lineup[0] < ctx.Cards.Pokemon.Count) Foe = ctx.Cards.Pokemon[gb.Lineup[0]].Trainer;
                    if (gb.Leader != GalacticLeader.Grunt)
                        foreach (var l in Lines)
                            if (l.Key == K + "start.galactic") l.Key = K + "start.galactic" + LeaderSuffix;
                    break;
                case InitiativeTokenAt t:
                    TokenSide = t.Side;
                    break;
                case SentIn s:
                    species[s.PokemonId] = s.Species;
                    hp[s.PokemonId] = s.Hp;
                    if (s.Side != MySide) FoeId = s.PokemonId;
                    string ub = UbFoe(Kind);
                    Add(new BattleLine { Key = K + (s.Side == MySide ? "send.mine" : Kind == BattleKind.Galactic ? "send.galactic" + LeaderSuffix : Kind == BattleKind.Trainer ? "send.trainer" : ub != null ? "send." + ub : "send.wild"), Side = s.Side }.With("mon", MonKey(s.PokemonId)));
                    // 호수 시련(92행): 수호신이 나오면 그 시련의 조건 — 이미 넘긴 시련(판에 한 번)이면 줄이 없다
                    if (s.Side != MySide && Kind == BattleKind.Trial)
                    {
                        TrialLake = UbTrial.LakeOf(ctx, s.Species);
                        var seenState = Source ?? View;
                        var ubState = seenState != null ? seenState.Ub : null;
                        if (TrialLake >= 0 && ubState != null && !ubState.TrialsOvercome[TrialLake])
                            Add(new BattleLine { Key = K + "trial.start." + TrialLake.ToString(CultureInfo.InvariantCulture), Side = s.Side });
                    }
                    break;
                case PrismReduced pr:
                    Add(new BattleLine { Key = K + "prism", Side = pr.Side }.WithValue("n", pr.Amount));
                    break;
                case PrismMoved pm:
                    PrismMove = pm;
                    Add(new BattleLine { Key = K + "prism.moved", Side = -1 });
                    break;
                case OnslaughtDrawn od:
                    Add(new BattleLine { Key = K + "onslaught", Side = 1 - MySide }.With("card", OnslaughtKey(od.Effect)));
                    break;
                case TrialProgress tp:
                    Add(new BattleLine { Key = K + (tp.Done ? "trial.done" : "trial.progress"), Side = 1 - MySide }.With("trial", "screen.battle.trial." + tp.Lake.ToString(CultureInfo.InvariantCulture)).WithValue("n", tp.Count).WithValue("max", tp.Goal));
                    break;
                case GuardianDown gd:
                    Guardian = gd;
                    Add(new BattleLine { Key = K + "guardian", Side = -1 }.WithValue("n", UbTrial.MovePerGuardian));
                    break;
                case OrderDecided o:
                    if (o.TokenTo != GameEvent.U) TokenSide = o.TokenTo;
                    if (o.First >= 0)
                    {
                        int first = ActiveOf(o.First);
                        if (first >= 0) Add(new BattleLine { Key = K + (o.TokenUsed ? "order.token" : "order"), Side = o.First }.With("mon", MonKey(first)));
                    }
                    break;
                case MoveUsed m:
                    // 알파 스트라이크는 고른 타입을 함께 말한다(플레이 시험 T20 — 「잠만보의 알파 스트라이크 · 강철!」)
                    if (m.MoveKey == BattleRules.AlphaStrike && m.Type != (PokemonType)GameEvent.U && m.Type != PokemonType.Typeless)
                        Add(new BattleLine { Key = K + "move.alpha", Side = m.Side }.With("mon", MonKey(m.PokemonId)).With("move", MoveKey(m.MoveKey)).With("type", "ui.types." + m.Type));
                    else Add(new BattleLine { Key = K + "move", Side = m.Side }.With("mon", MonKey(m.PokemonId)).With("move", MoveKey(m.MoveKey)));
                    // 흉내내기·손가락흔들기가 부른 기술(같은 공격의 둘째 MoveSelected — MoveHandlers.Call): 「○○의 흉내내기로 △△이(가) 나왔다!」
                    int called = CalledBy(m);
                    if (called != int.MinValue) Add(new BattleLine { Key = K + "move.called", Side = m.Side }.With("move", MoveKey(m.MoveKey)).With("called", MoveKey(called)));
                    break;
                case DiceRolled d:
                    diceLine = null;
                    if (d.Faces != null)
                    {
                        diceFaces.Clear();
                        diceFaces.AddRange(d.Faces);
                        diceLine = new BattleLine { Key = K + "dice", Side = d.Side };
                        DiceArgs(diceLine);
                        Add(diceLine);
                    }
                    if (d.FatigueAfter != GameEvent.U) fatigue[d.PokemonId] = d.FatigueAfter;
                    break;
                case DiceFacesChanged fc:
                    // 면 바꾸기(졸음·마비 …): 그 굴림의 주사위 줄을 바꾼 뒤의 눈·합으로 고친다 — 굴림판의 결과 줄(BattleRoll.FinalFaces)·계산 줄·데미지가 쓰는 값이다.
                    // 줄은 굴림이 드러날 때(세기가 끝난 뒤) 붙으므로 바꾸기 전 눈이 화면에 먼저 나오지 않는다
                    if (diceLine != null && fc.Changes != null && fc.Changes.Length > 0)
                    {
                        foreach (var ch in fc.Changes)
                        {
                            if (ch.Index < 0) continue;
                            while (diceFaces.Count <= ch.Index) diceFaces.Add(AttackDieFace.None);
                            diceFaces[ch.Index] = ch.After;
                        }
                        DiceArgs(diceLine);
                    }
                    break;
                case TypeMatchup tm:
                    if (!tm.Typeless && tm.Delta != 0)
                        Add(new BattleLine { Key = K + (tm.Delta > 0 ? "matchup.super" : tm.Delta <= -4 ? "matchup.none" : "matchup.weak"), Side = SideOf(tm.AttackerId) });
                    break;
                case DamageBreakdown db:
                    LastDamage = db;
                    LastCapture = null;
                    Add(new BattleLine { Key = K + "damage", Side = 1 - SideOf(db.TargetId) }.With("mon", MonKey(db.TargetId)).WithValue("n", db.Final));
                    break;
                case HpChanged h:
                    hp[h.PokemonId] = h.After;
                    if (h.After > 0 && h.After != h.Before)
                        Add(new BattleLine { Key = K + (h.After < h.Before ? "hp" : "hp.up"), Side = SideOf(h.PokemonId) }.With("mon", MonKey(h.PokemonId)).WithValue("n", h.After));
                    break;
                case StatusTicked st:
                    if (st.HpAfter != GameEvent.U)
                    {
                        hp[st.PokemonId] = st.HpAfter;
                        if (st.HpBefore != GameEvent.U && st.HpAfter < st.HpBefore)
                            Add(new BattleLine { Key = K + "tick", Side = SideOf(st.PokemonId) }.With("mon", MonKey(st.PokemonId)).WithValue("n", st.HpBefore - st.HpAfter));
                    }
                    break;
                case RetributionMoved rm:
                    if (rm.Amount > 0) Add(new BattleLine { Key = K + "retribution", Side = rm.FromSide }.With("mon", MonKey(rm.ToPokemonId)).WithValue("n", rm.Amount));
                    break;
                case FatigueChanged f:
                    fatigue[f.PokemonId] = f.After;
                    if (f.Cause == FatigueCause.BeastBoost) Add(new BattleLine { Key = K + "beast_boost", Side = SideOf(f.PokemonId) }.With("mon", MonKey(f.PokemonId)));   // 76행
                    break;
                case FormChanged fc:
                    // 폼 바꾸기(78·79행): 그 뒤의 기록 줄은 새 폼의 이름으로 적는다
                    if (fc.Temporary)
                    {
                        bool god = fc.Source.Kind == SourceKind.Rule && fc.Source.Id == (int)RuleSource.CelestialAura;
                        string before = MonKey(fc.PokemonId);
                        species[fc.PokemonId] = fc.After;
                        Add(new BattleLine { Key = K + (god ? "form.god" : "form"), Side = SideOf(fc.PokemonId) }.With("mon", before).With("form", god ? MonKey(fc.PokemonId) : FormArg + MonKey(fc.PokemonId)));
                    }
                    else species[fc.PokemonId] = fc.After;
                    break;
                case SwitchLocked sl:
                {
                    // 교체 금지(75행): 이유 다섯(RetributionBeats.ReasonKey의 화면 글과 짝 — 기록 줄은 screen.battle.log.lock.*)
                    int locked = ActiveOf(sl.Side);
                    Add(new BattleLine { Key = K + "lock." + LockName(sl.Reason), Side = sl.Side }.With("mon", locked >= 0 ? MonKey(locked) : K + "mon.unknown"));
                    break;
                }
                case StatusApplied sa:
                    if (sa.Result == StatusResult.Applied) Add(new BattleLine { Key = K + "status." + sa.Status.ToString().ToLowerInvariant(), Side = SideOf(sa.PokemonId) }.With("mon", MonKey(sa.PokemonId)));
                    else if (sa.Result == StatusResult.Immune) Add(new BattleLine { Key = K + "status.immune", Side = SideOf(sa.PokemonId) }.With("mon", MonKey(sa.PokemonId)));
                    break;
                case Fainted fa:
                    hp[fa.PokemonId] = 0;
                    Add(new BattleLine { Key = K + "faint", Side = fa.Side != GameEvent.U ? fa.Side : SideOf(fa.PokemonId) }.With("mon", MonKey(fa.PokemonId)));
                    break;
                case SwitchedOut so:
                    if (so.Reason == SwitchReason.Switch || so.Reason == SwitchReason.Forced)
                        Add(new BattleLine { Key = K + (so.Side == MySide ? "recall.mine" : "recall.foe"), Side = so.Side }.With("mon", MonKey(so.PokemonId)));
                    break;
                case CaptureAttempt ca:
                {
                    LastCapture = ca;
                    Add(new BattleLine { Key = K + "ball", Side = ca.Side }.With("ball", BallKey(ca.BallKey)));
                    // 눈은 합을 센 눈으로 적는다: 상태 이상인 대상이면 심볼 하나가 일반 타격이다(A7 — CaptureAttempt.Faces는 굴린 눈, Sum은 바꾼 뒤의 합)
                    var faces = new List<string>();
                    int converted = CaptureStage.Converted(ca);
                    if (ca.Faces != null)
                        for (int i = 0; i < ca.Faces.Length; i++)
                            if (ca.Faces[i] != AttackDieFace.None) faces.Add(FaceKey(i == converted ? AttackDieFace.RegularHit : ca.Faces[i]));
                    Add(new BattleLine { Key = K + "capture.roll", Side = ca.Side }.WithValue("n", faces.Count).WithKeys("faces", faces.ToArray()).WithValue("sum", ca.Sum).WithValue("rate", ca.CatchRate));
                    Add(new BattleLine { Key = K + (ca.Success ? "capture.ok" : "capture.fail." + Math.Max(0, Math.Min(2, ca.Shakes)).ToString(System.Globalization.CultureInfo.InvariantCulture)), Side = ca.Side }.With("mon", MonKey(ca.TargetId)));
                    if (ca.Success) CaughtId = ca.TargetId;
                    break;
                }
                case Fled fl:
                    FledChanges = fl.Changes ?? new ResChange[0];
                    Add(new BattleLine { Key = K + "fled", Side = fl.Side }.With("mon", MonKey(FoeId)));
                    break;
                case BattleEnded be:
                    Ended = true;
                    Winner = be.Winner;
                    Result = be.Result;
                    Changes = be.Changes ?? new ResChange[0];
                    // 포획·도망은 바로 앞 줄(「잡았다!」·「도망쳤다!」)이 끝을 알린다
                    if (be.Result != BattleResult.Captured && be.Result != BattleResult.Fled) Add(new BattleLine { Key = K + (be.Result == BattleResult.Forfeited ? (Won ? "end.forfeit.win" : "end.forfeit") : Drawn ? "end.draw" : Won ? "end.win" : "end.lose"), Side = Winner });
                    break;
            }
        }

        /// <summary>
        /// 준비 동작의 DS 문장(결정 143 ⑦): 기술을 고른 뒤 굴림과 함께 뜨는 「○○의 △△!」(알파 스트라이크는 고른 타입 — 59행의 기술 줄과 같은 꼴). 기록 줄에는 넣지 않는다 —
        /// 기록 줄은 맞는 순간(MoveUsed)에 붙는다. 준비 동작 중이 아니면 null.
        /// </summary>
        public BattleLine PrepLine(BattleSeen seen)
        {
            var ms = seen != null ? seen.WindMove : null;
            if (ms == null || seen.Winding < 0) return null;
            if (ms.MoveKey == BattleRules.AlphaStrike && ms.Type != (PokemonType)GameEvent.U && ms.Type != PokemonType.Typeless)
                return new BattleLine { Key = K + "move.alpha", Side = ms.Side, Event = ms }.With("mon", MonKey(seen.Winding)).With("move", MoveKey(ms.MoveKey)).With("type", "ui.types." + ms.Type);
            return new BattleLine { Key = K + "move", Side = ms.Side, Event = ms }.With("mon", MonKey(seen.Winding)).With("move", MoveKey(ms.MoveKey));
        }

        /// <summary>
        /// 그 공격이 부른 기술(흉내내기·손가락흔들기 — 그 공격의 MoveSelected 뒤에 같은 쪽의 다른 MoveSelected가 이어 왔다). 없으면 <c>int.MinValue</c>.
        /// 굴리기 전에 끊긴 앞 공격의 MoveSelected는 치지 않는다(이 공격의 기술을 고른 마지막 MoveSelected부터 본다).
        /// </summary>
        int CalledBy(MoveUsed used)
        {
            int called = int.MinValue;
            for (int i = Events.Count - 2; i >= 0; i--)
            {
                if (Events[i] is MoveUsed) break;
                if (!(Events[i] is MoveSelected sel) || sel.Side != used.Side) continue;
                if (sel.MoveKey == used.MoveKey) return called;
                if (called == int.MinValue) called = sel.MoveKey;
            }
            return int.MinValue;
        }

        /// <summary>잠금 이유의 글 키 꼬리(<c>screen.battle.log.lock.&lt;꼬리&gt;</c> · <c>screen.battle.lock.&lt;꼬리&gt;</c>).</summary>
        public static string LockName(SwitchLockReason reason)
        {
            switch (reason)
            {
                case SwitchLockReason.GalacticNoSwitchAfterKo: return "galactic";
                case SwitchLockReason.Frozen: return "frozen";
                case SwitchLockReason.BlastCrystal: return "blast_crystal";
                case SwitchLockReason.NoEscape: return "no_escape";
                default: return "move";
            }
        }

        static string FaceKey(AttackDieFace f) => K + "face." + (f == AttackDieFace.StatusSymbol ? "status" : f == AttackDieFace.CriticalHit ? "crit" : "hit");

        // 주사위 줄의 빈칸을 지금 눈(diceFaces)으로 채운다: 남은 주사위 수 · 눈 · 합(지운 주사위는 뺀다)
        void DiceArgs(BattleLine line)
        {
            var faces = new List<string>();
            foreach (var f in diceFaces)
                if (f != AttackDieFace.None) faces.Add(FaceKey(f));
            line.Args.Clear();
            line.WithValue("n", faces.Count).WithKeys("faces", faces.ToArray()).WithValue("sum", AttackDice.Sum(diceFaces));
        }

        void Add(BattleLine line)
        {
            line.Event = taking;
            Lines.Add(line);
        }

        /// <summary>
        /// 그 갱신의 줄을 화면에 드러나는 순서로 다시 놓는다(<see cref="FxReveal"/> — 같은 때면 받은 순서 그대로). 받은 순서와 드러나는 순서가 다를 수 있다:
        /// 인트로는 야생이 먼저 나오고 내 포켓몬이 그 뒤에 나오지만 이벤트는 내 쪽이 먼저다. <paramref name="entries"/> = 그 갱신의 항목(그릴 단계만 남긴 뒤).
        /// 그 항목의 이벤트가 아닌 줄(앞선 갱신의 줄)은 제자리에 둔다.
        /// </summary>
        public void Order(IReadOnlyList<FxEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var at = new List<int>();     // 줄의 자리
            var keys = new List<long>();  // (항목 차례, 드러나는 때)
            for (int i = 0; i < Lines.Count; i++)
                for (int k = 0; k < entries.Count; k++)
                    if (FxReveal.TryAt(entries[k], Lines[i].Event, out int ms))
                    {
                        at.Add(i);
                        keys.Add(((long)k << 32) + ms);
                        break;
                    }
            var moved = new List<BattleLine>();
            foreach (int i in at) moved.Add(Lines[i]);
            for (int i = 1; i < moved.Count; i++)   // 삽입 정렬(받은 순서를 지킨다 — 줄 수가 적다)
            {
                var line = moved[i];
                long key = keys[i];
                int j = i - 1;
                for (; j >= 0 && keys[j] > key; j--)
                {
                    moved[j + 1] = moved[j];
                    keys[j + 1] = keys[j];
                }
                moved[j + 1] = line;
                keys[j + 1] = key;
            }
            for (int i = 0; i < at.Count; i++) Lines[at[i]] = moved[i];
        }

        /// <summary>
        /// 지금 화면에 드러난 배틀의 모습(04 §8-3): 받은 이벤트 가운데 <paramref name="hidden"/>(연출이 아직 닿지 않은 것 — <see cref="FxReveal.Hidden"/>)을 뺀 것만으로 다시 센다.
        /// 보기는 갱신을 받는 순간 마지막 상태가 되지만(쓰러진 포켓몬 자리에 다음 포켓몬이 이미 서 있다) 화면은 연출이 닿는 차례대로 바뀐다.
        /// null이나 빈 모음이면 받은 것이 모두 드러난 모습이다. 체력·전투 피로는 여기가 아니라 표시 값(<c>FxShown</c>)이 붙잡는다.
        /// </summary>
        public BattleSeen See(ICollection<GameEvent> hidden)
        {
            var seen = new BattleSeen();
            bool any = hidden != null && hidden.Count > 0;
            foreach (var e in Events)
            {
                if (any && hidden.Contains(e)) { seen.Take(e, false); continue; }
                seen.Take(e, true);
            }
            foreach (var line in Lines)
                if (!any || line.Event == null || !hidden.Contains(line.Event)) seen.Lines++;
            // 드러난 출전이 하나도 없는 쪽은(받은 것이 다 드러났는데도) 보기의 값으로 메운다 — 이어하기처럼 출전 이벤트를 받지 못한 배틀
            if (!any)
                for (int k = 0; k < 2; k++)
                    if (seen.Active[k] < 0) seen.Active[k] = ActiveOf(k);
            return seen;
        }

        /// <summary>드러난 줄만(받은 순서). <paramref name="hidden"/>은 <see cref="See"/>와 같다.</summary>
        public bool Shows(BattleLine line, ICollection<GameEvent> hidden) => hidden == null || hidden.Count == 0 || line.Event == null || !hidden.Contains(line.Event);

        // ── 읽기 ──

        /// <summary>그 쪽에 지금 나와 있는 포켓몬(끝난 배틀이면 끝나기 직전에 나와 있던 포켓몬). 없으면 −1.</summary>
        public int ActiveOf(int side)
        {
            var s = Source;
            if (s == null || s.Battle == null || side < 0 || side >= s.Battle.Sides.Length) return -1;
            return s.Battle.Sides[side].Active;
        }

        /// <summary>읽을 보기: 살아 있는 배틀은 가장 새 보기, 끝났으면 마지막으로 살아 있던 보기.</summary>
        public GameState Source => !Ended && View != null && View.Battle != null ? View : Live;

        /// <summary>
        /// 그 쪽의 지금 포켓몬 값(체력 상자·기둥이 그리는 값은 모두 여기서 온다). 살아 있는 배틀은 보기 그대로,
        /// 끝난 배틀은 마지막으로 살아 있던 보기 + 이벤트가 실은 마지막 체력·피로다(마지막 한 방은 그 보기에 아직 없다).
        /// </summary>
        public BattleMon Mon(int side) => MonOf(ActiveOf(side));

        public BattleMon MonOf(int id)
        {
            var mon = new BattleMon { Id = -1, Def = -1 };
            var s = Source;
            if (s == null || id < 0 || id >= s.Pokemon.Count || s.Pokemon[id].Def < 0) return mon;
            var p = s.Pokemon[id];
            var def = ctx.Cards.Pokemon[p.Def];
            mon.Id = id;
            mon.Def = p.Def;
            mon.MaxHp = PokemonRules.MaxHp(ctx, p);
            mon.Hp = PokemonRules.Hp(ctx, p);
            mon.Fatigue = p.Fatigue;
            mon.Status = p.Status;
            mon.Status2 = p.Status2;
            if (Ended)
            {
                if (hp.TryGetValue(id, out int h)) mon.Hp = h;
                if (fatigue.TryGetValue(id, out int f)) mon.Fatigue = f;
            }
            mon.BaseSpeed = def.Initiative;
            var mods = new List<Mod>();
            BattleRules.PriorityMods(s, ctx, id, mods);
            mon.Speed = mon.BaseSpeed;
            foreach (var m in mods) mon.Speed += m.Delta;
            return mon;
        }

        int SideOf(int pokemon)
        {
            var s = Source;
            if (s != null && s.Battle != null)
                for (int k = 0; k < s.Battle.Sides.Length; k++)
                {
                    var side = s.Battle.Sides[k];
                    if (side.Active == pokemon || side.Roster.Contains(pokemon)) return k;
                    if (side.Owner >= 0 && side.Owner < s.Seats.Length && Array.IndexOf(s.Seats[side.Owner].Party, pokemon) >= 0) return k;
                }
            return -1;
        }

        /// <summary>포켓몬 이름의 문자열 키.</summary>
        public string MonKey(int pokemon)
        {
            int def = -1;
            if (!species.TryGetValue(pokemon, out def))
            {
                var s = Source;
                def = s != null && pokemon >= 0 && pokemon < s.Pokemon.Count ? s.Pokemon[pokemon].Def : -1;
            }
            return def >= 0 ? "pokemon." + ctx.Cards.Pokemon[def].Key : K + "mon.unknown";
        }

        /// <summary>기술 이름의 문자열 키(플랜 B는 기술 표 밖이다).</summary>
        public string MoveKey(int move) => MoveNameKey(ctx, move);

        /// <summary>볼 등급의 아이템 키(문자열 표 <c>item.&lt;키&gt;</c>, 그림 <c>Cards/Tokens/item_icons/&lt;키&gt;.png</c>).</summary>
        public static string BallItem(int ball)
        {
            switch ((Ball)ball)
            {
                case Ball.Regular: return "poke_ball";
                case Ball.Greater: return "greater_ball";
                case Ball.Ultimate: return "ultimate_ball";
                default: return "keystone_ball";
            }
        }

        public static string BallKey(int ball) => "item." + BallItem(ball);

        /// <summary>울트라 온슬롯 카드(번호 1~6)의 짧은 이름 키(기둥 칩 · 기록 줄). 효과 문구는 데이터의 <c>ultra_onslaught.&lt;n&gt;.text.*</c>.</summary>
        public static string OnslaughtKey(int effect) => "screen.battle.onslaught." + Math.Max(1, Math.Min(6, effect)).ToString(CultureInfo.InvariantCulture);

        public static string MoveNameKey(Ctx ctx, int move) => move == BattleRules.PlanB ? "screen.battle.move.plan_b" : move == BattleRules.AlphaStrike ? "screen.battle.move.alpha_strike" : "move." + ctx.Cards.Moves[move].Key;
    }
}
