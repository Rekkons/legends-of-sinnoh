using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 곡을 고르는 상황(결정 66 ④, 03 §3-1 bgm.json). 상황마다 곡 하나 또는 무음이 Gen <c>Audio/Bgm/bgm.json</c>(추적 표 <c>Tools/assets/tables/bgm.json</c>)에 있다 — 이름이 표의 situation이다.
    /// Pt 원곡(M9 소리 ② — VGMTrans + FluidSynth 렌더, 앞부분 + 반복 클립). 배틀 곡은 엔진의 <see cref="BattleBgm"/>를 그대로 잇고(<see cref="MusicScenes.Battle"/>), 결과 창의 곡은 이긴 배틀의 종류를 따른다(<see cref="MusicScenes.Result"/>).
    /// </summary>
    public enum MusicScene
    {
        Title, Lobby, Setup, Prologue,
        FieldTemperate, FieldCold, FieldWarm, FieldNoble, FieldSpace, FinalAct,
        Encounter,
        BattleWild, BattleTrainer, BattleGalacticGrunt, BattleGalacticCommander, BattleGalacticBoss, BattleLegendary, BattleGod, BattleUltraBeast, BattleNecrozma,
        Result, ResultTrainer, ResultGalactic, GameOver,
    }

    /// <summary>상황 고르기(순수 셈 — 화면 흐름이 지금 화면·보기를 넣는다).</summary>
    public static class MusicScenes
    {
        /// <summary>배틀 상황 키(<c>BattleStarted.BgmKey</c>) → 상황.</summary>
        public static MusicScene Battle(BattleBgm bgm)
        {
            switch (bgm)
            {
                case BattleBgm.Trainer: return MusicScene.BattleTrainer;
                case BattleBgm.GalacticGrunt: return MusicScene.BattleGalacticGrunt;
                case BattleBgm.GalacticCommander: return MusicScene.BattleGalacticCommander;
                case BattleBgm.GalacticBoss: return MusicScene.BattleGalacticBoss;
                case BattleBgm.Legendary: return MusicScene.BattleLegendary;
                case BattleBgm.God: return MusicScene.BattleGod;
                case BattleBgm.UltraBeast: return MusicScene.BattleUltraBeast;
                case BattleBgm.Necrozma: return MusicScene.BattleNecrozma;
                default: return MusicScene.BattleWild;
            }
        }

        /// <summary>결과 창의 곡(M9 소리 ② — Pt 승리 곡 셋 [가정 plan §5 #520]): 트레이너끼리 · 갤럭시단(조무래기 · 간부 · 태홍) · 그 밖(야생 · 전설 · 신 · UB).</summary>
        public static MusicScene Result(BattleBgm bgm)
        {
            switch (bgm)
            {
                case BattleBgm.Trainer: return MusicScene.ResultTrainer;
                case BattleBgm.GalacticGrunt:
                case BattleBgm.GalacticCommander:
                case BattleBgm.GalacticBoss: return MusicScene.ResultGalactic;
                default: return MusicScene.Result;
            }
        }

        /// <summary>
        /// 보드의 곡 [가정 — plan §5 #52]: 서막(막 0) = <see cref="MusicScene.Prologue"/>, 종막(마지막 막) = <see cref="MusicScene.FinalAct"/>,
        /// 그 사이(1~3막)는 내 말이 선 칸의 기후 — 「지역」을 칸의 생물군(바다·숲…)이 아니라 기후 띠로 읽는다(걸을 때마다 곡이 바뀌지 않게). 막을 모르면(−1) 서막.
        /// </summary>
        public static MusicScene Board(int act, Climate climate)
        {
            if (act <= 0) return MusicScene.Prologue;
            if (act >= HosState.Acts - 1) return MusicScene.FinalAct;
            switch (climate)
            {
                case Climate.Cold: return MusicScene.FieldCold;
                case Climate.Warm: return MusicScene.FieldWarm;
                case Climate.Noble: return MusicScene.FieldNoble;
                case Climate.Space: return MusicScene.FieldSpace;
                default: return MusicScene.FieldTemperate;
            }
        }
    }

    /// <summary>
    /// 상황 곡 표(Gen <c>Audio/Bgm/bgm.json</c>): 상황 → 곡 파일(<c>Audio/Bgm/</c> 아래 상대 경로) 또는 무음(null).
    /// M9 소리 ②: 곡 파일 = 반복 클립(돌고 있는 둘째 반복), <see cref="Intro"/> = 그 앞에 한 번 트는 앞부분 클립(앞부분 + 첫 반복 — 없으면 반복 클립부터), <see cref="Once"/> = 반복 없이 한 번 트는 곡. 반복 자리(표본)는 <see cref="Loop"/>.
    /// </summary>
    public sealed class MusicBook
    {
        static readonly Regex Track = new Regex("^\\{\"situation\":\"([A-Za-z]+)\",\"file\":\"([^\"]+)\",\"title\":\"((?:[^\"\\\\]|\\\\.)*)\"(.*)\\}$");
        static readonly Regex IntroPart = new Regex("^,\"intro\":\"([^\"]+)\"");
        static readonly Regex LoopPart = new Regex("\"loopStart\":(\\d+),\"loopEnd\":(\\d+),\"introSamples\":(\\d+),\"loopSamples\":(\\d+)");
        static readonly Regex Silent = new Regex("^\\{\"situation\":\"([A-Za-z]+)\",\"silent\":true\\}");

        readonly Dictionary<MusicScene, string> files = new Dictionary<MusicScene, string>();
        readonly Dictionary<MusicScene, string> titles = new Dictionary<MusicScene, string>();
        readonly Dictionary<MusicScene, string> intros = new Dictionary<MusicScene, string>();
        readonly Dictionary<MusicScene, MusicLoop> loops = new Dictionary<MusicScene, MusicLoop>();
        readonly HashSet<MusicScene> once = new HashSet<MusicScene>();

        /// <summary>표에 줄이 있는 상황 수.</summary>
        public int Count => files.Count;

        /// <summary>표의 줄(행 하나 = 한 줄)을 읽는다. 모르는 상황 이름·같은 상황 두 번은 <see cref="FormatException"/>.</summary>
        public static MusicBook Parse(string table)
        {
            var book = new MusicBook();
            if (table == null) return book;
            foreach (var raw in table.Split('\n'))
            {
                string line = raw.TrimEnd('\r', ',');
                var m = Track.Match(line);
                var s = m.Success ? null : Silent.Match(line);
                if (!m.Success && !s.Success) continue;
                string name = (m.Success ? m : s).Groups[1].Value;
                if (!Enum.TryParse(name, false, out MusicScene scene) || !Enum.IsDefined(typeof(MusicScene), scene)) throw new FormatException("bgm.json: 모르는 상황 " + name);
                if (book.files.ContainsKey(scene)) throw new FormatException("bgm.json: 상황 두 번 " + name);
                book.files[scene] = m.Success ? m.Groups[2].Value : null;
                if (!m.Success) continue;
                book.titles[scene] = Regex.Unescape(m.Groups[3].Value);
                string rest = m.Groups[4].Value;
                var intro = IntroPart.Match(rest);
                if (intro.Success) book.intros[scene] = intro.Groups[1].Value;
                if (rest.Contains("\"once\":true")) book.once.Add(scene);
                var lp = LoopPart.Match(rest);
                if (lp.Success)
                    book.loops[scene] = new MusicLoop
                    {
                        Start = long.Parse(lp.Groups[1].Value, CultureInfo.InvariantCulture), End = long.Parse(lp.Groups[2].Value, CultureInfo.InvariantCulture),
                        IntroSamples = long.Parse(lp.Groups[3].Value, CultureInfo.InvariantCulture), LoopSamples = long.Parse(lp.Groups[4].Value, CultureInfo.InvariantCulture),
                    };
            }
            return book;
        }

        /// <summary>그 상황의 줄이 있나(곡이든 무음이든).</summary>
        public bool Has(MusicScene scene) => files.ContainsKey(scene);

        /// <summary>그 상황의 곡 파일. 무음이거나 줄이 없으면 null.</summary>
        public string File(MusicScene scene) => files.TryGetValue(scene, out var f) ? f : null;

        /// <summary>그 상황 곡의 앞부분 클립 파일(앞부분이 없는 곡 · 끝 곡 · 무음은 null).</summary>
        public string Intro(MusicScene scene) => intros.TryGetValue(scene, out var f) ? f : null;

        /// <summary>반복 없이 한 번 트는 곡인가(끝 곡).</summary>
        public bool Once(MusicScene scene) => once.Contains(scene);

        /// <summary>그 상황 곡의 반복 자리(표에 없으면 null — 옛 꼴의 표).</summary>
        public MusicLoop Loop(MusicScene scene) => loops.TryGetValue(scene, out var l) ? l : null;

        /// <summary>그 상황의 곡 제목(무음·없음은 null).</summary>
        public string Title(MusicScene scene) => titles.TryGetValue(scene, out var t) ? t : null;

        /// <summary>줄이 없는 상황(03 §7 「상황마다 클립이 있거나 무음」 — 비어 있어야 한다).</summary>
        public List<MusicScene> Missing()
        {
            var list = new List<MusicScene>();
            foreach (MusicScene s in Enum.GetValues(typeof(MusicScene))) if (!Has(s)) list.Add(s);
            return list;
        }
    }

    /// <summary>곡의 반복 자리(표본, 렌더 시각 — 03 §3-2): 반복 시작 · 끝, 앞부분 클립 길이(= 끝, 앞부분이 없으면 0) · 반복 클립 길이. 끝 곡은 Start 0 · End = 곡 길이.</summary>
    public sealed class MusicLoop
    {
        public long Start, End, IntroSamples, LoopSamples;
    }

    /// <summary>효과음 음량 무리(04 §8-4 「작은 순간 / 큰 순간 두 무리」).</summary>
    public enum SoundGroup { Small, Big }

    /// <summary>효과음 한 줄(Gen <c>Audio/Se/se.json</c>): 키 · 파일(<c>Audio/</c> 아래 상대 경로) · 무리 · 키 음량(dB) · 길이 · BGM 더킹(징글).</summary>
    public sealed class SoundEntry
    {
        public string Key, File, Seq;
        public SoundGroup Group;
        public int Db, Ms;
        public bool Duck;
    }

    /// <summary>효과음 표(Gen <c>Audio/Se/se.json</c>, 결정 66 ④): 키 → 줄. Pt SDAT 시퀀스 전체(M9 소리 ② — 그 전에는 첫 음 하나) · 원작 자산에 없는 것은 plinkfx 레시피(M9 소리 ③ — seq 칸이 「plinkfx:…」) — 사용자 고르기는 소리 ④(plan §5 #10).</summary>
    public sealed class SoundBook
    {
        static readonly Regex Row = new Regex("^\\{\"key\":\"([a-z0-9_]+)\",\"file\":\"([^\"]+)\",\"group\":\"(small|big)\",\"db\":(-?\\d+),\"ms\":(\\d+),\"seq\":\"([^\"]*)\"(.*)\\}$");

        readonly Dictionary<string, SoundEntry> rows = new Dictionary<string, SoundEntry>(StringComparer.Ordinal);

        public int Count => rows.Count;
        public IEnumerable<SoundEntry> Entries => rows.Values;

        public static SoundBook Parse(string table)
        {
            var book = new SoundBook();
            if (table == null) return book;
            foreach (var raw in table.Split('\n'))
            {
                var m = Row.Match(raw.TrimEnd('\r', ','));
                if (!m.Success) continue;
                var e = new SoundEntry
                {
                    Key = m.Groups[1].Value, File = m.Groups[2].Value, Group = m.Groups[3].Value == "big" ? SoundGroup.Big : SoundGroup.Small,
                    Db = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture), Ms = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture), Seq = m.Groups[6].Value,
                    Duck = m.Groups[7].Value.Contains("\"duck\":true"),
                };
                if (book.rows.ContainsKey(e.Key)) throw new FormatException("se.json: 키 두 번 " + e.Key);
                book.rows[e.Key] = e;
            }
            return book;
        }

        /// <summary>없으면 null.</summary>
        public SoundEntry Find(string key) => key != null && rows.TryGetValue(key, out var e) ? e : null;
    }

    /// <summary>
    /// 효과음 키(se.json의 key — 연출 단서 <see cref="FxCue.Sound"/>와 즉시 소리가 쓰는 이름). 뜻·원천은 <c>Tools/assets/tables/se.json</c>.
    /// 단서를 연출 행에 다는 일은 그 행의 화면이 한다(04 §8-4 — 이 묶음은 받아 트는 길까지).
    /// </summary>
    public static class SoundKeys
    {
        public const string Select = "select", Decide = "decide", Cancel = "cancel", Land = "land", Hit = "hit",
            BallThrow = "ball_throw", BallOpen = "ball_open", BallShake = "ball_shake", Capture = "capture", CardFlip = "card_flip", Dice = "dice", MyTurn = "my_turn";

        /// <summary>연출 단서에 소리를 달며 더한 키(plan §4 「연출 단서에 소리 달기」 — 뜻·원천은 같은 표): 주사위 굴림 · 기절 · 찾아보기 「!」 · 조우 전환 · 막 카드 · 회복 징글 대역 · 넘김 띠 · 토큰 놓기.</summary>
        public const string DiceRoll = "dice_roll", Faint = "faint", Bang = "bang", Wipe = "wipe", ActCard = "act_card", Heal = "heal", Band = "band", Token = "token";

        /// <summary>같은 칸에서 만남 「!」(04 §8-8 13행, 결정 73 ⑤ — 임시: 원작 시선 곡은 M9).</summary>
        public const string Meet = "meet";

        /// <summary>
        /// M9 소리 ③(원작 자산 SE 먼저 · 없으면 plinkfx 레시피 — 결정 112 Q13): 방 판 핑 · 감정 표현 · 기록 줄 도장 · 응답 시계 남은 10초(#273 · #221 — 옛 meet·select·token 대역) ·
        /// 순간이동·게이트(PL_WARP) · 반짝(DP_KIRAKIRA) · 스탬프 도장(PL_STAMP) · 동전 · 피로 칸(EXP) · 도망 · 하강음(귀환 · 탈락).
        /// </summary>
        public const string Ping = "ping", Emote = "emote", Mark = "mark", Timer = "timer", Warp = "warp", Shine = "shine", Stamp = "stamp", Coin = "coin", Exp = "exp", Flee = "flee", Fall = "fall";

        /// <summary>
        /// M9 소리 ②(곡 시퀀스의 징글 — VGMTrans + FluidSynth, 동안 BGM 더킹): 포켓몬센터 회복 징글 SEQ_ASA(8·17행) · 진화 곡 SEQ_SHINKA 앞머리(39행) · 팡파르 SEQ_FANFA5(포획 68행 · 진화 끝 39행) · 교환 곡 SEQ_KOUKAN 앞머리(52행).
        /// 같은 칸 만남 <see cref="Meet"/>도 시선 곡 SEQ_EYE_BOY 도입부로 바뀌었다(13행).
        /// </summary>
        public const string Center = "center", Evolve = "evolve", Fanfare = "fanfare", Trade = "trade";

        /// <summary>찾아보기 「!」(20행 — 결정 143 ⑥, 원작 자산 SE): 나무 · 왜곡된 나무 = 풀숲 소리 · 바다 칸(산호) = 물 소리. 옛 <see cref="Bang"/>(DP_FW230)은 찾아보기에서 빠졌다.</summary>
        public const string Search = "search", SearchReef = "search_reef";

        /// <summary>UB · 네크로즈마가 나무 · 산호 · 사당을 부숨(83행 — 결정 147 ①, 원작 자산 DP_W088 바위 던짐). 칸 파괴에만 쓴다 — VS 「!」 · 시련 막 깨짐은 <see cref="Bang"/> 그대로.</summary>
        public const string Destroy = "destroy";

        public static readonly string[] All = { Select, Decide, Cancel, Land, Hit, BallThrow, BallOpen, BallShake, Capture, CardFlip, Dice, MyTurn, DiceRoll, Faint, Bang, Wipe, ActCard, Heal, Band, Token, Meet,
            Ping, Emote, Mark, Timer, Warp, Shine, Stamp, Coin, Exp, Flee, Fall, Center, Evolve, Fanfare, Trade, Search, SearchReef, Destroy };

        /// <summary>
        /// 울음(소리 표의 층 <see cref="SoundLayer.Cry"/> — 효과음 표가 아니라 울음 표 <see cref="CryBook"/>, 결정 67 ① PokeAPI): 표 칸의 값은 <see cref="Cry"/>(그대로) · <see cref="CryLow"/>(기절 — 낮춰서).
        /// 단서의 키는 <c>cry:&lt;dex&gt;</c> · <c>cry_low:&lt;dex&gt;</c>(<see cref="CryKey"/>) — 재생 층이 <see cref="TryCry"/>로 푼다.
        /// </summary>
        public const string Cry = "cry", CryLow = "cry_low";

        public static string CryKey(int dex, bool low) => (low ? CryLow : Cry) + ":" + dex.ToString(CultureInfo.InvariantCulture);

        /// <summary>울음 단서 키면 true(종 번호 · 낮춤).</summary>
        public static bool TryCry(string key, out int dex, out bool low)
        {
            dex = 0;
            low = false;
            if (key == null) return false;
            int colon = key.IndexOf(':');
            if (colon < 0) return false;
            string head = key.Substring(0, colon);
            if (head != Cry && head != CryLow) return false;
            low = head == CryLow;
            return int.TryParse(key.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out dex) && dex > 0;
        }
    }

    /// <summary>
    /// 울음 표(Gen <c>Audio/Cry/cry.json</c> — build 묶음 cry, PokeAPI legacy #1~649 · latest #650+): 울음 번호 → 파일(<c>Audio/</c> 아래 상대 경로).
    /// 울음 번호 = 종 번호(도감), 폼 울음(결정 143 ④ — Tools/assets/tables/form_cries.json)은 PokeAPI 폼 id(10001+)이고 행의 <c>form</c> 칸에 포켓몬 표 키가 있다(<see cref="Form"/>).
    /// </summary>
    public sealed class CryBook
    {
        static readonly Regex Row = new Regex("^\\{\"dex\":(\\d+),\"file\":\"([^\"]+)\""), FormKey = new Regex(",\"form\":\"([^\"]+)\"\\}$");
        readonly Dictionary<int, string> files = new Dictionary<int, string>();
        readonly Dictionary<string, int> forms = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Count => files.Count;

        public static CryBook Parse(string table)
        {
            var book = new CryBook();
            if (table == null) return book;
            foreach (var raw in table.Split('\n'))
            {
                var m = Row.Match(raw.TrimEnd('\r', ','));
                if (!m.Success) continue;
                int dex = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (book.files.ContainsKey(dex)) throw new FormatException("cry.json: 종 두 번 " + dex);
                book.files[dex] = m.Groups[2].Value;
                var f = FormKey.Match(raw.TrimEnd('\r', ','));
                if (!f.Success) continue;
                if (book.forms.ContainsKey(f.Groups[1].Value)) throw new FormatException("cry.json: 폼 두 번 " + f.Groups[1].Value);
                book.forms[f.Groups[1].Value] = dex;
            }
            return book;
        }

        /// <summary>그 울음 번호의 파일(없으면 null).</summary>
        public string File(int dex) => files.TryGetValue(dex, out var f) ? f : null;

        /// <summary>그 포켓몬 표 키(<c>800:4</c> 등)의 폼 울음 번호 — 폼 울음이 없으면 0(종의 울음을 쓴다).</summary>
        public int Form(string pokemonKey) => pokemonKey != null && forms.TryGetValue(pokemonKey, out int id) ? id : 0;

        /// <summary>기절 울음의 높이(원작 5세대처럼 낮춰 낸다 [가정 plan §5 #352]).</summary>
        public const float LowPitch = 0.85f;
    }

    /// <summary>
    /// 음량 셈(04 §8-4 — 모두 [가정], 값은 조정 값 — plan §5 #51). 사용자 음량은 단계(0 = 끔 … <see cref="MaxStep"/>), 단계마다 dB(<see cref="StepDb"/>).
    /// BGM = 단계 dB + BGM 바탕(<see cref="BgmDb"/>) + 더킹, 효과음 = 단계 dB + 무리 dB(작은 순간 <see cref="SmallDb"/> · 큰 순간 <see cref="BigDb"/>) + 키 dB.
    /// </summary>
    public static class SoundMix
    {
        /// <summary>음량 단계 수 − 1: 0 = 끔, 1~10(S02의 슬라이더 — 결정 70 ⑭, 단계 수는 [가정 — plan §5]).</summary>
        public const int MaxStep = 10;

        /// <summary>기본 단계: BGM 6 · 효과음 8 — 옛 여섯 단계(끔·1~5)의 3·4와 같은 세기다(짝수 단계 = 옛 단계 × 2).</summary>
        public const int DefaultBgmStep = 6, DefaultSeStep = 8;

        /// <summary>단계 → dB(10 = 0 dB, 2 = −20 dB — 짝수 단계는 옛 1~5의 −20·−14·−9·−4·0 그대로, 홀수는 그 사이). 0(끔)은 <see cref="double.NegativeInfinity"/>.</summary>
        static readonly int[] Steps = { int.MinValue, -24, -20, -17, -14, -11, -9, -6, -4, -2, 0 };

        public static readonly Tunable BgmDb = Tunables.Shared.Define("fx.sound.bgmDb", -6, -40, 0, TunableScope.Local);
        public static readonly Tunable SmallDb = Tunables.Shared.Define("fx.sound.smallDb", -6, -40, 0, TunableScope.Local);
        public static readonly Tunable BigDb = Tunables.Shared.Define("fx.sound.bigDb", 0, -40, 6, TunableScope.Local);
        /// <summary>징글 동안 BGM 더킹(04 §8-4 「BGM −13dB·더킹」 IS00 #186).</summary>
        public static readonly Tunable DuckDb = Tunables.Shared.Define("fx.sound.duckDb", -13, -40, 0, TunableScope.Local);
        /// <summary>더킹이 내려가고 올라오는 시간.</summary>
        public static readonly Tunable DuckFadeMs = Tunables.Shared.Define("fx.sound.duckFadeMs", 150, 0, 2000, TunableScope.Local);
        /// <summary>곡을 바꿀 때 교차 페이드.</summary>
        public static readonly Tunable CrossfadeMs = Tunables.Shared.Define("fx.sound.crossfadeMs", 600, 0, 5000, TunableScope.Local);

        public static int ClampStep(int step) => Math.Max(0, Math.Min(MaxStep, step));

        public static double StepDb(int step)
        {
            step = ClampStep(step);
            return step == 0 ? double.NegativeInfinity : Steps[step];
        }

        /// <summary>dB → 선형(−∞ = 0).</summary>
        public static float Linear(double db) => double.IsNegativeInfinity(db) ? 0f : (float)Math.Pow(10.0, db / 20.0);

        /// <summary>BGM 음량(선형): 단계 · 바탕 · 더킹 무게(0 = 더킹 없음 · 1 = 다 내려감).</summary>
        public static float Music(int step, double duck01) => Linear(StepDb(step) + BgmDb.Value + DuckDb.Value * Math.Max(0.0, Math.Min(1.0, duck01)));

        /// <summary>효과음 음량(선형): 단계 · 무리 · 그 키의 dB.</summary>
        public static float Effect(int step, SoundGroup group, int keyDb) => Linear(StepDb(step) + (group == SoundGroup.Big ? BigDb.Value : SmallDb.Value) + keyDb);
    }

    /// <summary>
    /// 곡 교차 페이드(순수 — 시각은 밖에서 넣는다): 지금 곡(<see cref="Current"/>)은 0 → 1, 앞 곡(<see cref="Previous"/>)은 바꾼 순간의 크기 → 0으로 <see cref="SoundMix.CrossfadeMs"/>에 걸쳐 바뀐다.
    /// 페이드 가운데 또 바꾸면 그 순간의 지금 곡이 앞 곡이 되어 거기서부터 줄어든다(그 전의 앞 곡은 끊긴다). 무음(null)으로도 바꾼다.
    /// </summary>
    public sealed class MusicFade
    {
        int t, length;
        float outFrom;
        bool started;

        public string Current { get; private set; }
        public string Previous { get; private set; }

        /// <summary>곡을 바꾼다. 같은 곡이면 아무 일도 없다(false) — 처음 부를 때는 무음에서 들어온다.</summary>
        public bool Switch(string file)
        {
            if (started && file == Current) return false;
            outFrom = started ? CurrentGain : 0f;
            Previous = started ? Current : null;
            Current = file;
            started = true;
            t = 0;
            length = SoundMix.CrossfadeMs.Value;
            return true;
        }

        public void Advance(int dtMs)
        {
            if (dtMs < 0) throw new ArgumentOutOfRangeException(nameof(dtMs));
            t = (int)Math.Min(int.MaxValue / 2, (long)t + dtMs);
            if (t >= length) Previous = null;
        }

        float P => length <= 0 ? 1f : Math.Min(1f, t / (float)length);

        /// <summary>지금 곡의 크기(0 ~ 1).</summary>
        public float CurrentGain => Current == null ? 0f : P;

        /// <summary>앞 곡의 크기(0 ~ 1). 페이드가 끝났으면 0.</summary>
        public float PreviousGain => Previous == null ? 0f : outFrom * (1f - P);

        public bool Fading => Previous != null && P < 1f;
    }

    /// <summary>
    /// BGM 더킹(징글 동안, 04 §8-4): 징글마다 [시작, 끝)을 걸고, 시각마다 무게(0 ~ 1)를 셈한다 — 시작부터 <see cref="SoundMix.DuckFadeMs"/>에 걸쳐 1로, 끝 앞 그만큼에 걸쳐 0으로.
    /// 겹친 징글은 큰 무게를 쓴다. 지난 징글은 버린다.
    /// </summary>
    public sealed class MusicDuck
    {
        readonly List<(long Start, long End)> holds = new List<(long, long)>();

        public void Hold(long startMs, int lengthMs)
        {
            if (lengthMs <= 0) return;
            holds.Add((startMs, startMs + lengthMs));
        }

        public double Weight(long nowMs)
        {
            holds.RemoveAll(h => h.End <= nowMs);
            double fade = Math.Max(1, SoundMix.DuckFadeMs.Value), w = 0;
            foreach (var h in holds)
            {
                if (nowMs < h.Start) continue;
                double up = (nowMs - h.Start) / fade, down = (h.End - nowMs) / fade;
                w = Math.Max(w, Math.Min(1.0, Math.Min(up, down)));
            }
            return w;
        }
    }

    /// <summary>한 프레임에 같은 키가 여럿 와도 한 번만(04 §8-4 「행동 하나에 큰 소리는 한 번」의 가장 작은 꼴): 프레임마다 <see cref="Begin"/>, 낼 때마다 <see cref="Take"/>.</summary>
    public sealed class SoundFrame
    {
        readonly HashSet<string> taken = new HashSet<string>(StringComparer.Ordinal);

        public void Begin() => taken.Clear();

        /// <summary>이 프레임에 처음 온 키면 true.</summary>
        public bool Take(string key) => key != null && taken.Add(key);
    }
}
