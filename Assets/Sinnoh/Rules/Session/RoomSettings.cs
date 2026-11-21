using System;
using System.Globalization;

namespace Sinnoh.Rules.Session
{
    /// <summary>제한 시간 프리셋(02 §3-2·§5-4, 결정 25·30 ①). 기본 「느긋하게」. Custom = 방장이 칸마다 정한 길이(결정 140 ① — <see cref="RoomSettings.Custom"/>).</summary>
    public enum ClockPreset { Off, Relaxed, Normal, Custom }

    /// <summary>방장이 고치는 시계 칸 하나(결정 140 ① — isekai 방 설정의 스테퍼를 옮김): 이름 키 · 범위 · 한 번에 바꾸는 양(ms) [가정 #740].</summary>
    public sealed class ClockField
    {
        public string Key;
        public int MinMs, MaxMs, StepMs;
        public Func<ClockSettings, int> Get;
        public Action<ClockSettings, int> Set;

        public bool InRange(ClockSettings c) => Get(c) >= MinMs && Get(c) <= MaxMs;
    }

    /// <summary>일시정지를 거는 사람(결정 112 Q6 — isekai PauseMode를 옮김, 기본 방장만 [가정 M7-88]): 방장만(바로) · 모두(예약 — 내 차례나 내 선택이 오면 멈춘다) · 투표(다른 사람 모두 찬성하면 예약).</summary>
    public enum PauseMode { HostOnly, Everyone, Vote }

    /// <summary>가상 응답자(갤럭시단·UB)의 조종(결정 14 ③). 사람 = 배틀에 없는 다음 좌석(결정 29 ④).</summary>
    public enum Control { Ai, Human }

    /// <summary>
    /// 시계 길이(02 §5-4 표, ms) [가정 수치 — 02 §11 #1, 사용자는 대표 수치를 보고 「느긋하게」를 골랐다(결정 25)].
    /// 「끔」은 사람 좌석에 시간 초과가 없고, 끊긴 좌석만 <see cref="OfflineMs"/> 뒤에 기본값으로 넘긴다(§6-1).
    /// </summary>
    public sealed class ClockSettings
    {
        public ClockPreset Preset;
        public int MapTurnMs, BattleActionMs, LineupMs, ReactionMs, RespondMs, VillageMs, SetupPickMs;
        public int BankStartMs, BankRoundMs, BankMaxMs;
        /// <summary>시계 끔 방에서 끊긴 좌석의 선택을 기다리는 시간(02 §5-4 끝 줄 · §6-1).</summary>
        public int OfflineMs = 120000;

        public bool On => Preset != ClockPreset.Off;

        public static ClockSettings For(ClockPreset preset)
        {
            switch (preset)
            {
                case ClockPreset.Relaxed:
                    return new ClockSettings
                    {
                        Preset = preset, MapTurnMs = 180000, BattleActionMs = 60000, LineupMs = 120000, ReactionMs = 30000, RespondMs = 60000, VillageMs = 90000, SetupPickMs = 180000,
                        BankStartMs = 120000, BankRoundMs = 30000, BankMaxMs = 300000,
                    };
                case ClockPreset.Normal:
                    return new ClockSettings
                    {
                        Preset = preset, MapTurnMs = 90000, BattleActionMs = 30000, LineupMs = 60000, ReactionMs = 15000, RespondMs = 30000, VillageMs = 45000, SetupPickMs = 90000,
                        BankStartMs = 60000, BankRoundMs = 15000, BankMaxMs = 150000,
                    };
                default:
                    return new ClockSettings { Preset = ClockPreset.Off };
            }
        }

        public ClockSettings Clone() => (ClockSettings)MemberwiseClone();

        /// <summary>
        /// 방장이 고치는 칸(결정 140 ① — 방 설정 화면의 순서) [가정 #740]: 턴 · 출전 순서 · 고향 마을 15초~5분 5초 단위, 배틀 기술 · 반응 · 응답 10초~2분 5초 단위(isekai 응답 시간),
        /// 스타터 고르기 30초~10분 30초 단위(isekai 준비), 예비 시간 시작 · 상한 0~30분 30초 단위, 라운드 충전 0~10분 5초 단위(isekai 뱅크 · 충전 — isekai는 10초 단위지만 「보통」 15초가 칸에 맞게). 두 프리셋 값은 모두 범위 안이다.
        /// </summary>
        public static readonly ClockField[] Fields =
        {
            new ClockField { Key = "map_turn", MinMs = 15000, MaxMs = 300000, StepMs = 5000, Get = c => c.MapTurnMs, Set = (c, v) => c.MapTurnMs = v },
            new ClockField { Key = "battle_action", MinMs = 10000, MaxMs = 120000, StepMs = 5000, Get = c => c.BattleActionMs, Set = (c, v) => c.BattleActionMs = v },
            new ClockField { Key = "lineup", MinMs = 15000, MaxMs = 300000, StepMs = 5000, Get = c => c.LineupMs, Set = (c, v) => c.LineupMs = v },
            new ClockField { Key = "reaction", MinMs = 10000, MaxMs = 120000, StepMs = 5000, Get = c => c.ReactionMs, Set = (c, v) => c.ReactionMs = v },
            new ClockField { Key = "respond", MinMs = 10000, MaxMs = 120000, StepMs = 5000, Get = c => c.RespondMs, Set = (c, v) => c.RespondMs = v },
            new ClockField { Key = "village", MinMs = 15000, MaxMs = 300000, StepMs = 5000, Get = c => c.VillageMs, Set = (c, v) => c.VillageMs = v },
            new ClockField { Key = "setup_pick", MinMs = 30000, MaxMs = 600000, StepMs = 30000, Get = c => c.SetupPickMs, Set = (c, v) => c.SetupPickMs = v },
            new ClockField { Key = "bank_start", MinMs = 0, MaxMs = 1800000, StepMs = 30000, Get = c => c.BankStartMs, Set = (c, v) => c.BankStartMs = v },
            new ClockField { Key = "bank_round", MinMs = 0, MaxMs = 600000, StepMs = 5000, Get = c => c.BankRoundMs, Set = (c, v) => c.BankRoundMs = v },
            new ClockField { Key = "bank_max", MinMs = 0, MaxMs = 1800000, StepMs = 30000, Get = c => c.BankMaxMs, Set = (c, v) => c.BankMaxMs = v },
        };

        public static ClockField Field(string key) => Array.Find(Fields, f => f.Key == key);

        /// <summary>고치는 칸의 길이가 모두 같은가(프리셋 이름 · 끊김 대기는 보지 않는다).</summary>
        public bool SameLengths(ClockSettings other) => other != null && Array.TrueForAll(Fields, f => f.Get(this) == f.Get(other));
    }

    /// <summary>
    /// 방 설정(02 §3-2, S08). 방장만 바꾼다. 로비에서는 모두, 게임 중에는 제한 시간(다음 선택부터)·조종·갤럭시단 AI 세기(다음 배틀부터)만(<see cref="InGameChange"/>).
    /// 규칙 세부는 rulings 기본값(결정 14 ①) — 고르는 값만 <see cref="ToOptions"/>에 넣는다. 저장은 자동(결정 14 ⑥)이라 칸이 없다.
    /// </summary>
    public sealed class RoomSettings
    {
        public const int MinSeats = 2;

        public Campaign Campaign = Campaign.HeartOfSinnoh;
        /// <summary>자리 수 2~6(결정 4). 방장이 빈 자리를 [닫기]로 줄인다(02 §3-1).</summary>
        public int Seats = SeatId.MaxSeats;
        public bool TrainerCards = true;
        /// <summary>울트라 온슬롯(울트라 버스트에서만 듣는다).</summary>
        public bool UltraOnslaught;
        public UbDifficulty UbDifficulty = UbDifficulty.NewTrainer;
        public Control Galactic = Control.Ai;
        /// <summary>UB·네크로즈마 조종 — 결정 121로 쓰지 않는다(울트라 버스트 모드는 늘 AI · 방 설정 화면은 잠근다). 저장 · 프로토콜 꼴을 지키려고 칸만 남긴다.</summary>
        public Control UltraBeast = Control.Ai;
        /// <summary>갤럭시단 AI 세기(결정 26). 이 형의 기본은 원칙 AI(시험이 그대로 쓴다) — 방 만들기가 여는 새 방은 <see cref="SoloGame.NewGameAi"/>(「강함」, 02 §3-2 · 결정 118 ②)로 연다.</summary>
        public GalacticAiLevel GalacticAi = GalacticAiLevel.Principle;
        public ClockPreset Clock = ClockPreset.Relaxed;
        /// <summary>직접 설정한 길이(<see cref="Clock"/> = Custom일 때만 — 결정 140 ①). 실제 길이는 늘 <see cref="ClockLengths"/>로 읽는다.</summary>
        public ClockSettings Custom;
        /// <summary>플레이어끼리의 거래(결정 75 ⑤).</summary>
        public bool Trading = true;
        /// <summary>일시정지를 거는 사람(결정 112 Q6 — 로비에서만 바꾼다).</summary>
        public PauseMode Pause = PauseMode.HostOnly;
        /// <summary>방 채팅(결정 140 ② — 로비 · 게임 중, 기본 끔). 게임 중에도 방장이 켜고 끈다(<see cref="InGameChange"/> [가정 plan §5 #750]). 규칙 · 판과 무관하다.</summary>
        public bool Chat;
        /// <summary>무르기(결정 149 · 154 ①② — 기본 끔, 로비에서만 바꾼다: <see cref="InGameChange"/>가 옮기지 않는다). 규칙이 아니라 <see cref="ToOptions"/>에 넣지 않는다 — 방 핵의 <see cref="SessionConfig.Undo"/>로 간다.</summary>
        public UndoMode Undo;

        public RuleOptions ToOptions() => new RuleOptions
        {
            TrainerCards = TrainerCards,
            Trading = Trading,
            UltraOnslaught = Campaign == Campaign.UltraBurst && UltraOnslaught,
            UbDifficulty = UbDifficulty,
        };

        /// <summary>AI·조종 설정. 조종 좌석은 판마다 정해지므로(배틀에 없는 다음 좌석) 여기에는 AI 값만 둔다 — 방(HostSession)이 사람 조종을 판단한다.</summary>
        public AiSettings ToAi(ulong aiSeed) => new AiSettings { GalacticAiLevel = GalacticAi, Seed = aiSeed };

        public RoomSettings Clone()
        {
            var r = (RoomSettings)MemberwiseClone();
            r.Custom = Custom?.Clone();
            return r;
        }

        /// <summary>시계가 실제로 쓰는 길이(결정 140 ① — 한 곳): 직접 설정이면 그 값(새 사본), 아니면 그 프리셋.</summary>
        public ClockSettings ClockLengths()
        {
            if (Clock != ClockPreset.Custom || Custom == null) return ClockSettings.For(Clock);
            var c = Custom.Clone();
            c.Preset = ClockPreset.Custom;
            c.OfflineMs = new ClockSettings().OfflineMs;
            return c;
        }

        /// <summary>길이 표를 고른다: 두 프리셋 가운데 길이가 모두 같은 것이 있으면 그 프리셋으로(직접 설정을 비운다), 아니면 직접 설정으로.</summary>
        public void UseClock(ClockSettings lengths)
        {
            if (lengths == null) throw new ArgumentNullException(nameof(lengths));
            foreach (var p in new[] { ClockPreset.Relaxed, ClockPreset.Normal })
                if (ClockSettings.For(p).SameLengths(lengths))
                {
                    Clock = p;
                    Custom = null;
                    return;
                }
            Clock = ClockPreset.Custom;
            Custom = lengths.Clone();
            Custom.Preset = ClockPreset.Custom;
        }

        /// <summary>
        /// 스테퍼 한 번(결정 140 ①): 그 칸을 dir(−1 · +1) × 한 번 양만큼 범위 안에서 옮긴다. 예비 시간 상한은 시작값 밑으로 내려가지 않게 짝을 함께 옮긴다.
        /// 시계가 꺼졌거나 이미 끝에 닿았으면 false(바꾸지 않는다).
        /// </summary>
        public bool StepClock(string key, int dir)
        {
            var f = ClockSettings.Field(key);
            if (f == null || Clock == ClockPreset.Off || dir == 0) return false;
            var c = ClockLengths();
            int was = f.Get(c);
            int v = Math.Max(f.MinMs, Math.Min(f.MaxMs, was + Math.Sign(dir) * f.StepMs));
            if (v == was) return false;
            f.Set(c, v);
            if (key == "bank_start" && c.BankMaxMs < v) c.BankMaxMs = v;
            if (key == "bank_max" && c.BankStartMs > v) c.BankStartMs = v;
            UseClock(c);
            return true;
        }

        /// <summary>시계 칸이 틀렸으면 그 이유, 맞으면 null — 직접 설정은 칸마다 범위 안이고 예비 시간 시작 ≤ 상한.</summary>
        public string ClockProblem()
        {
            if (!Enum.IsDefined(typeof(ClockPreset), Clock)) return "enum";
            if (Clock != ClockPreset.Custom) return null;
            if (Custom == null) return "clock_custom";
            foreach (var f in ClockSettings.Fields)
                if (!f.InRange(Custom)) return "clock_" + f.Key;
            return Custom.BankStartMs > Custom.BankMaxMs ? "clock_bank" : null;
        }

        /// <summary>방장이 고른 시계를 다음 방에 쓰려고 남기는 글(결정 140 ① — 「프리셋 번호」 또는 「3;칸 ms,…」).</summary>
        public string ClockMemo()
        {
            string p = ((int)Clock).ToString(CultureInfo.InvariantCulture);
            if (Clock != ClockPreset.Custom || Custom == null) return p;
            return p + ";" + string.Join(",", Array.ConvertAll(ClockSettings.Fields, f => f.Get(Custom).ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>남긴 글로 시계를 되살린다. 읽을 수 없거나 범위 밖이면 바꾸지 않고 false(새 방의 기본값 그대로).</summary>
        public bool UseClockMemo(string memo)
        {
            if (string.IsNullOrEmpty(memo)) return false;
            var parts = memo.Split(';');
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) || !Enum.IsDefined(typeof(ClockPreset), p)) return false;
            var preset = (ClockPreset)p;
            if (preset != ClockPreset.Custom)
            {
                if (parts.Length != 1) return false;
                Clock = preset;
                Custom = null;
                return true;
            }
            var ms = parts.Length == 2 ? parts[1].Split(',') : new string[0];
            if (ms.Length != ClockSettings.Fields.Length) return false;
            var c = new ClockSettings { Preset = ClockPreset.Custom };
            for (int i = 0; i < ms.Length; i++)
            {
                if (!int.TryParse(ms[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return false;
                ClockSettings.Fields[i].Set(c, v);
            }
            var test = new RoomSettings { Clock = ClockPreset.Custom, Custom = c };
            if (test.ClockProblem() != null) return false;
            UseClock(c);
            return true;
        }

        /// <summary>틀린 값이면 그 이유(영문 짧은 키), 맞으면 null. members = 지금 들어온 사람 수(자리 수를 그 밑으로 못 줄인다).</summary>
        public string Problem(int members)
        {
            if (Seats < MinSeats || Seats > SeatId.MaxSeats) return "seats";
            if (Seats < members) return "seats_below_members";
            if (!SoloGame.CampaignReady(Campaign)) return "campaign";
            if (!SoloGame.AiReady(GalacticAi)) return "galactic_ai";
            if (!Enum.IsDefined(typeof(ClockPreset), Clock) || !Enum.IsDefined(typeof(Control), Galactic) || !Enum.IsDefined(typeof(Control), UltraBeast) || !Enum.IsDefined(typeof(PauseMode), Pause) || !Enum.IsDefined(typeof(UndoMode), Undo)) return "enum";
            return ClockProblem();
        }

        /// <summary>게임 중 바꾸기(02 §3-2 「게임 중」 열): 지금 설정에 바꿀 수 있는 칸만 옮긴 사본. 나머지 칸은 무시한다.</summary>
        public RoomSettings InGameChange(RoomSettings wanted)
        {
            if (wanted == null) throw new ArgumentNullException(nameof(wanted));
            var r = Clone();
            r.Clock = wanted.Clock;
            r.Custom = wanted.Custom?.Clone();
            r.Galactic = wanted.Galactic;
            r.UltraBeast = wanted.UltraBeast;
            r.GalacticAi = wanted.GalacticAi;
            r.Chat = wanted.Chat;   // 채팅(결정 140 ②)
            // 무르기(Undo)는 옮기지 않는다 — 로비에서만(결정 154 ②)
            return r;
        }
    }
}
