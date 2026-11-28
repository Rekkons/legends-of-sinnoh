using System;
using System.Collections.Generic;
using Sinnoh.Fx;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 사용자 음량(S02 「배경음악」·「효과음」, 단계 0 = 끔 ~ <see cref="SoundMix.MaxStep"/>)과 연출 속도(S02 「연출」 — <see cref="FxSpeed"/>, 같은 곳에 둔다) · 말 그림(S02 「말 그림」 — <see cref="PieceStyle"/>, 결정 70 ⑤). 이 바탕 클래스는 실행하는 동안만 쥐는 값이다 —
    /// 캡처·시험·에디터·확인용 플레이어가 쓴다(사용자 값을 건드리지 않게). 플레이어는 <see cref="StoredSoundPrefs"/>(창 크기와 같은 곳 — 엔진의 PlayerPrefs, Windows는 레지스트리).
    /// </summary>
    public class SoundPrefs
    {
        int bgm = SoundMix.DefaultBgmStep, se = SoundMix.DefaultSeStep;
        FxSpeed fx;
        PieceStyle piece = PieceStyle.Dot;
        bool alert = true;
        string lang;

        /// <summary>
        /// 화면 글의 언어(S02 「언어」 — M9 언어 ②, plan §5 #304: <c>ko</c> · <c>en</c>). null = 아직 고르지 않았다(플레이어는 OS 언어 — <see cref="ScreenBoot.LangOf"/>, 에디터 · 시험 · 확인용 프로브는 화면 시작점이 준 표 그대로 — 한국어).
        /// 보는 사람의 값이라 판의 세이브 · 방 통신에는 넣지 않는다(결정 34 — 좌석마다 자기 언어).
        /// </summary>
        public virtual string Lang
        {
            get => lang;
            set => lang = ClampLang(value);
        }

        /// <summary>저장된 값을 언어로(모르는 값은 null — 고르지 않은 것과 같다).</summary>
        public static string ClampLang(string value) => value == "ko" || value == "en" ? value : null;

        /// <summary>내 차례 알림(S02 「차례 알림」 — 결정 112 Q17, 기본 켬): 방 판에서 창이 비활성일 때 작업 표시줄 깜빡임(<see cref="AttentionFlash"/>).</summary>
        public virtual bool Alert
        {
            get => alert;
            set => alert = value;
        }

        /// <summary>
        /// 플레이 기록 보내기(02 §12 — <see cref="PlayUploads"/>, 결정 126 ④): **기본 켜짐** · 설정 화면 「플레이 기록 보내기」에서 끈다.
        /// 첫 실행 알림(<see cref="PlayNotice"/>)을 보기 전에는 켜져 있어도 올리지 않는다 [가정 plan §5 #650] — 실행 인자 <c>-sinnohPlayUpload</c>(<see cref="ScreenBoot"/>)는 그 실행에만 알림 없이 켠다.
        /// </summary>
        public virtual bool PlayUpload { get; set; } = true;

        /// <summary>첫 실행의 플레이 기록 알림(타이틀 위 창 — <see cref="GameFlow.PlayNoticeDue"/>)을 봤나. 기본 = 아직(한 번 [확인] · [설정 열기]를 누르면 켜진다).</summary>
        public virtual bool PlayNotice { get; set; }

        /// <summary>말 그림의 양식(기본 주인공 도트).</summary>
        public virtual PieceStyle Piece
        {
            get => piece;
            set => piece = ClampPiece((int)value);
        }

        /// <summary>저장된 값을 말 그림 양식으로(모르는 값은 주인공 도트).</summary>
        public static PieceStyle ClampPiece(int value) => value == (int)PieceStyle.Job ? PieceStyle.Job : PieceStyle.Dot;

        /// <summary>연출 속도(기본 보통).</summary>
        public virtual FxSpeed Fx
        {
            get => fx;
            set => fx = ClampFx((int)value);
        }

        /// <summary>저장된 값을 연출 속도로(모르는 값은 보통).</summary>
        public static FxSpeed ClampFx(int value) => value == (int)FxSpeed.Fast || value == (int)FxSpeed.Off ? (FxSpeed)value : FxSpeed.Normal;

        public virtual int Bgm
        {
            get => bgm;
            set => bgm = SoundMix.ClampStep(value);
        }

        public virtual int Se
        {
            get => se;
            set => se = SoundMix.ClampStep(value);
        }
    }

    /// <summary>플레이어의 음량 설정: 엔진의 PlayerPrefs(창 크기 「Screenmanager …」와 같은 레지스트리 키 아래)에 바로 쓴다.</summary>
    public sealed class StoredSoundPrefs : SoundPrefs
    {
        /// <summary>음량 키(0~10 — 결정 70 ⑭). 옛 키(<see cref="OldBgmKey"/>·<see cref="OldSeKey"/> — 끔·1~5)만 있으면 그 값의 두 배로 읽는다(같은 세기).</summary>
        public const string BgmKey = "sinnoh.sound.bgm10", SeKey = "sinnoh.sound.se10", OldBgmKey = "sinnoh.sound.bgm", OldSeKey = "sinnoh.sound.se", FxKey = "sinnoh.fx.speed", PieceKey = "sinnoh.piece.style", AlertKey = "sinnoh.alert.turn", LangKey = "sinnoh.ui.lang", PlayUploadKey = "sinnoh.play.upload", PlayNoticeKey = "sinnoh.play.notice";

        public override bool PlayUpload
        {
            get => PlayerPrefs.GetInt(PlayUploadKey, 1) != 0;
            set { PlayerPrefs.SetInt(PlayUploadKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public override bool PlayNotice
        {
            get => PlayerPrefs.GetInt(PlayNoticeKey, 0) != 0;
            set { PlayerPrefs.SetInt(PlayNoticeKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public override string Lang
        {
            get => ClampLang(PlayerPrefs.GetString(LangKey, ""));
            set
            {
                string v = ClampLang(value);
                if (v == null) PlayerPrefs.DeleteKey(LangKey);
                else PlayerPrefs.SetString(LangKey, v);
                PlayerPrefs.Save();
            }
        }

        public override bool Alert
        {
            get => PlayerPrefs.GetInt(AlertKey, 1) != 0;
            set { PlayerPrefs.SetInt(AlertKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public override PieceStyle Piece
        {
            get => ClampPiece(PlayerPrefs.GetInt(PieceKey, (int)PieceStyle.Dot));
            set { PlayerPrefs.SetInt(PieceKey, (int)ClampPiece((int)value)); PlayerPrefs.Save(); }
        }

        public override FxSpeed Fx
        {
            get => ClampFx(PlayerPrefs.GetInt(FxKey, (int)FxSpeed.Normal));
            set { PlayerPrefs.SetInt(FxKey, (int)ClampFx((int)value)); PlayerPrefs.Save(); }
        }

        public override int Bgm
        {
            get => Read(BgmKey, OldBgmKey, SoundMix.DefaultBgmStep);
            set { PlayerPrefs.SetInt(BgmKey, SoundMix.ClampStep(value)); PlayerPrefs.Save(); }
        }

        public override int Se
        {
            get => Read(SeKey, OldSeKey, SoundMix.DefaultSeStep);
            set { PlayerPrefs.SetInt(SeKey, SoundMix.ClampStep(value)); PlayerPrefs.Save(); }
        }

        static int Read(string key, string oldKey, int fallback) =>
            SoundMix.ClampStep(PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : PlayerPrefs.HasKey(oldKey) ? PlayerPrefs.GetInt(oldKey) * 2 : fallback);
    }

    /// <summary>소리 하나를 내라는 부름(화면 흐름 → 재생 층): 연출 단서(<see cref="Seq"/>·<see cref="AtMs"/> = 그 항목·항목 안 시각)이거나 즉시 소리(<see cref="Immediate"/>).</summary>
    public struct SoundCall
    {
        public string Key;
        public bool Immediate;
        public int Seq, AtMs;
        /// <summary>단서를 단 행(04 §8-8 행 × 10 + 단계, 즉시 소리는 0)과 그 단서의 시퀀서 시각(<see cref="Sinnoh.Fx.FxSequencer.CueMs"/> — 단서 시각 로그).</summary>
        public int Row;
        public long CueMs;
    }

    /// <summary>재생 층이 낸 소리 한 줄(단서 시각 로그 — 사람이 듣기 전의 근거, 결정 66 ④).</summary>
    public struct SoundHeard
    {
        /// <summary>재생 층 시각(만든 뒤 흐른 ms).</summary>
        public long AtMs;
        /// <summary>「bgm」 · 「se」 · 「cry」(울음 — M9 소리 ③).</summary>
        public string Kind;
        /// <summary>효과음 키 · 곡 상황 이름.</summary>
        public string Key;
        /// <summary>낸 클립 이름(무음 곡이면 null).</summary>
        public string Clip;
        /// <summary>낸 음량(선형, 효과음) — 곡은 0.</summary>
        public float Volume;
        /// <summary>단서면 「Seq@AtMs」, 즉시 소리면 「now」, 곡이면 「scene」.</summary>
        public string From;
        /// <summary>단서를 단 행(즉시 소리·곡은 0).</summary>
        public int Row;
    }

    /// <summary>
    /// 소리 재생 층(결정 66 ④, 04 §8-4): BGM 하나(곡을 바꿀 때 교차 페이드 — 곡 벌 둘, 벌마다 앞부분 음원 + 반복 음원: M9 소리 ②) + 효과음 여럿(음원 하나에 겹쳐 낸다, 같은 키가 한 프레임에 겹치면 한 번).
    /// 고르기·음량·페이드·더킹 셈은 <c>Sinnoh.Fx</c>(<see cref="MusicBook"/>·<see cref="SoundBook"/>·<see cref="SoundMix"/>·<see cref="MusicFade"/>·<see cref="MusicDuck"/>·<see cref="SoundFrame"/>)에 있고 여기는 Unity 음원에 옮길 뿐이다.
    /// 화면 흐름(<see cref="GameFlow"/>)이 프레임마다 상황(<see cref="GameFlow.Music"/>)과 음량(<see cref="GameFlow.SoundPrefs"/>)을 주고, 소리 부름(<see cref="GameFlow.Sound"/> — 연출 단서·즉시 소리)을 보낸다.
    /// 표·클립은 Gen(<see cref="GenAssets"/>, 플레이어는 번들 audio)에서 읽는다 — 없으면 무음으로 돈다(죽지 않는다). 오디오 장치가 없어도(창 없는 플레이어·배치) 음원은 그냥 소리가 나지 않는다.
    /// </summary>
    public sealed class SoundPlayer : MonoBehaviour
    {
        public const string MusicTableKey = "Audio/Bgm/bgm.json", SeTableKey = "Audio/Se/se.json", CryTableKey = "Audio/Cry/cry.json";
        const int HeardMax = 512;

        /// <summary>곡 파일(<c>bgm.json</c>의 file) → Gen 키.</summary>
        public static string MusicKey(string file) => "Audio/Bgm/" + file;

        /// <summary>효과음 파일(<c>se.json</c>의 file) → Gen 키.</summary>
        public static string EffectKey(string file) => "Audio/" + file;

        /// <summary>
        /// 곡 한 벌(M9 소리 ② — 03 §3-2 「앞부분 + 반복 이음」): 반복 음원(<see cref="Loop"/> — 곡 파일 = 반복 클립을 loop로, 끝 곡이면 한 번) + 앞부분 음원(<see cref="Intro"/> — 앞부분 클립을 한 번).
        /// 앞부분 클립이 끝나는 표본에 반복 클립이 시작하게 둘을 같은 DSP 시각 기준으로 예약한다(<see cref="AudioSource.PlayScheduled"/>).
        /// </summary>
        sealed class Deck
        {
            public AudioSource Intro, Loop;

            public float Volume { set { Intro.volume = value; Loop.volume = value; } }
            public bool Playing => Intro.isPlaying || Loop.isPlaying;

            public void Stop()
            {
                Intro.Stop();
                Loop.Stop();
            }
        }

        /// <summary>예약 재생의 앞 여유(스트리밍 클립이 첫 버퍼를 채울 시간).</summary>
        const double ScheduleLeadSec = 0.1;

        readonly Deck[] music = new Deck[2];
        AudioSource effects, cries;
        int front;
        readonly MusicFade fade = new MusicFade();
        readonly MusicDuck duck = new MusicDuck();
        readonly SoundFrame frame = new SoundFrame();
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        readonly List<SoundHeard> heard = new List<SoundHeard>();
        SoundPrefs prefs = new SoundPrefs();
        bool sceneSet;

        public MusicBook Music { get; private set; } = new MusicBook();
        public SoundBook Effects { get; private set; } = new SoundBook();

        /// <summary>울음 표(M9 소리 ③ — Gen <c>Audio/Cry/cry.json</c> — 번들 cries). 없으면 빈 표(울음 없이 돈다).</summary>
        public CryBook Cries { get; private set; } = new CryBook();

        /// <summary>재생 층 시각(ms) — <see cref="Tick"/>으로 흐른 시간.</summary>
        public long NowMs { get; private set; }

        /// <summary>지금 고른 상황(아직 없으면 null).</summary>
        public MusicScene? Scene { get; private set; }

        /// <summary>지금 곡(들어오는 쪽)의 반복 음원. 확인용 — 클립 이름이 곡이다(무음이면 클립 null).</summary>
        public AudioSource MusicSource => music[front].Loop;

        /// <summary>지금 곡의 앞부분 음원(확인용 — 앞부분이 없는 곡이면 클립 null).</summary>
        public AudioSource IntroSource => music[front].Intro;

        /// <summary>교차 페이드에서 나가는 쪽의 음원(확인용).</summary>
        public AudioSource FadingSource => music[1 - front].Loop;

        /// <summary>지금 곡의 파일(무음이면 null).</summary>
        public string MusicFile => fade.Current;

        /// <summary>낸 소리 기록(오래된 것부터, 최근 <see cref="HeardMax"/>줄).</summary>
        public IReadOnlyList<SoundHeard> Heard => heard;

        /// <summary>표 둘을 Gen에서 읽은 재생 층을 만든다. 표가 없으면 빈 표로(소리 없이) 돈다.</summary>
        public static SoundPlayer Create(Transform parent)
        {
            var go = new GameObject("Sound");
            if (parent != null) go.transform.SetParent(parent, false);
            var p = go.AddComponent<SoundPlayer>();
            p.Init(GenAssets.Load<TextAsset>(MusicTableKey), GenAssets.Load<TextAsset>(SeTableKey));
            p.LoadCries();
            p.Preload();
            return p;
        }

        void Init(TextAsset musicTable, TextAsset seTable)
        {
            try { Music = MusicBook.Parse(musicTable != null ? musicTable.text : null); }
            catch (FormatException e) { Debug.LogWarning("[Sound] " + e.Message); }
            try { Effects = SoundBook.Parse(seTable != null ? seTable.text : null); }
            catch (FormatException e) { Debug.LogWarning("[Sound] " + e.Message); }
            if (musicTable == null || seTable == null) Debug.LogWarning("[Sound] 소리 표 없음(" + (musicTable == null ? MusicTableKey + " " : "") + (seTable == null ? SeTableKey : "") + ") — rebuild-all, 플레이어는 BoardBuild의 audio 번들");
            for (int i = 0; i < music.Length; i++)
            {
                music[i] = new Deck { Loop = MusicSourceNew(true), Intro = MusicSourceNew(false) };
            }
            effects = gameObject.AddComponent<AudioSource>();
            effects.playOnAwake = false;
            effects.spatialBlend = 0f;
            cries = gameObject.AddComponent<AudioSource>();   // 울음(기절은 높이를 낮춘다 — 효과음 음원과 따로)
            cries.playOnAwake = false;
            cries.spatialBlend = 0f;
        }

        AudioSource MusicSourceNew(bool loop)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;
            src.volume = 0f;
            return src;
        }

        // 곡 벌에 곡을 걸고 튼다: 앞부분 클립이 있으면 앞부분 → (그 길이 뒤) 반복 클립을 예약으로 잇고, 없으면 반복 클립부터. 곡 클립이 없으면 false
        bool StartDeck(Deck deck, MusicScene scene)
        {
            string file = Music.File(scene), intro = Music.Intro(scene);
            deck.Stop();
            deck.Intro.clip = null;
            deck.Loop.clip = file != null ? Clip(MusicKey(file)) : null;
            deck.Loop.loop = !Music.Once(scene);
            if (deck.Loop.clip == null) return false;
            var lead = intro != null ? Clip(MusicKey(intro)) : null;
            if (lead == null)
            {
                deck.Loop.Play();
                return true;
            }
            deck.Intro.clip = lead;
            double at = AudioSettings.dspTime + ScheduleLeadSec;
            deck.Intro.PlayScheduled(at);
            deck.Loop.PlayScheduled(at + (double)lead.samples / lead.frequency);
            return true;
        }

        // 울음 표
        void LoadCries()
        {
            var table = GenAssets.Load<TextAsset>(CryTableKey);
            if (table == null) return;
            try { Cries = CryBook.Parse(table.text); }
            catch (FormatException e) { Debug.LogWarning("[Sound] " + e.Message); }
        }

        /// <summary>미리 연 효과음 클립 수(<see cref="Preload"/>).</summary>
        public int Preloaded { get; private set; }

        /// <summary>
        /// 효과음 · 징글 클립을 모두 미리 연다(04 §8-6 · 02 §8 — plan M7 「연출 실접속」): 클립을 처음 낼 때 읽으면 그 첫 효과음이 늦을 수 있다.
        /// 첫 화면(재생 층을 만들 때)에서 표의 클립을 다 읽어 두고 소리 데이터를 올린다. 곡(BGM)은 열지 않는다(스트리밍).
        /// </summary>
        public int Preload()
        {
            int n = 0;
            foreach (var e in Effects.Entries)
            {
                var clip = Clip(EffectKey(e.File));
                if (clip == null) continue;
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                n++;
            }
            Preloaded = n;
            return n;
        }

        /// <summary>음량 설정을 붙인다(화면 흐름의 것). 다음 <see cref="Tick"/>부터 그 값.</summary>
        public void Use(SoundPrefs soundPrefs) => prefs = soundPrefs ?? new SoundPrefs();

        /// <summary>
        /// 상황을 정한다: 바뀌면 그 상황의 곡으로 교차 페이드(무음 상황이면 지금 곡이 줄어들어 멈춘다). 같은 곡을 쓰는 상황끼리는(셋업 → 서막) 끊기지 않는다.
        /// </summary>
        public void Want(MusicScene scene)
        {
            if (sceneSet && Scene == scene) return;
            sceneSet = true;
            Scene = scene;
            string file = Music.File(scene);
            if (!fade.Switch(file)) return;
            front = 1 - front;
            var deck = music[front];
            deck.Volume = 0f;
            StartDeck(deck, scene);
            Note("bgm", scene.ToString(), deck.Loop.clip != null ? deck.Loop.clip.name : null, 0f, "scene");
        }

        /// <summary>
        /// 효과음 하나를 낸다(연출 단서 · 즉시 소리). 같은 키가 이 프레임에 이미 났으면 내지 않는다(false). 표에 없는 키·클립이 없는 키도 false(경고는 키마다 한 번).
        /// 징글(표의 duck)이면 그 길이 동안 BGM을 더킹한다 — 실제로 낸 때만(효과음 끔 · 클립 없음이면 BGM도 그대로).
        /// </summary>
        public bool Play(SoundCall call)
        {
            if (SoundKeys.TryCry(call.Key, out int dex, out bool low)) return PlayCry(call, dex, low);
            var e = Effects.Find(call.Key);
            if (e == null) { Warn("키 없음 " + call.Key); return false; }
            if (!frame.Take(call.Key)) return false;
            var clip = Clip(EffectKey(e.File));
            float vol = SoundMix.Effect(prefs.Se, e.Group, e.Db);
            bool sounded = clip != null && vol > 0f;
            if (sounded) effects.PlayOneShot(clip, vol);
            if (e.Duck && sounded) duck.Hold(NowMs, Math.Max(e.Ms, clip != null ? Mathf.CeilToInt(clip.length * 1000f) : 0));
            Note("se", call.Key, clip != null ? clip.name : null, vol, call.Immediate ? "now" : call.Seq + "@" + call.AtMs, call.Row);
            return clip != null;
        }

        /// <summary>
        /// 울음 하나(소리 표 층 Cry — M9 소리 ③): 큰 순간 무리 음량 · 기절이면 높이 <see cref="CryBook.LowPitch"/>. 표에 없는 종이면 내지 않는다(false — 04 §8-6 「늦으면 대표 SE」 대신 무음 [가정 plan §5 #352]).
        /// </summary>
        bool PlayCry(SoundCall call, int dex, bool low)
        {
            if (!frame.Take(call.Key)) return false;
            string file = Cries.File(dex);
            var clip = file != null ? Clip(EffectKey(file)) : null;
            float vol = SoundMix.Effect(prefs.Se, SoundGroup.Big, 0);
            if (clip != null && vol > 0f)
            {
                cries.pitch = low ? CryBook.LowPitch : 1f;
                cries.PlayOneShot(clip, vol);
            }
            Note("cry", call.Key, clip != null ? clip.name : null, vol, call.Immediate ? "now" : call.Seq + "@" + call.AtMs, call.Row);
            return clip != null;
        }

        /// <summary>시간을 보낸다: 페이드·더킹을 음원 음량에 옮기고, 다 나간 곡을 멈춘다. 프레임마다 한 번(같은 키 한 번 세기도 여기서 새로 연다).</summary>
        public void Tick(int dtMs)
        {
            if (dtMs < 0) dtMs = 0;
            NowMs += dtMs;
            fade.Advance(dtMs);
            frame.Begin();
            float level = SoundMix.Music(prefs.Bgm, duck.Weight(NowMs));
            music[front].Volume = level * fade.CurrentGain;
            var back = music[1 - front];
            back.Volume = level * fade.PreviousGain;
            if (!fade.Fading && back.Playing) back.Stop();
        }

        AudioClip Clip(string key)
        {
            if (clips.TryGetValue(key, out var c)) return c;
            c = GenAssets.Load<AudioClip>(key);
            if (c == null) Warn("클립 없음 " + key);
            clips[key] = c;
            return c;
        }

        /// <summary>그 효과음 키의 클립(표에 없거나 클립이 없으면 null). 확인용 — 단서 시각 로그가 클립 이름·길이를 적는다.</summary>
        public AudioClip ClipFor(string key)
        {
            if (SoundKeys.TryCry(key, out int dex, out _))
            {
                string file = Cries.File(dex);
                return file != null ? Clip(EffectKey(file)) : null;
            }
            var e = Effects.Find(key);
            return e != null ? Clip(EffectKey(e.File)) : null;
        }

        void Note(string kind, string key, string clip, float volume, string from, int row = 0)
        {
            heard.Add(new SoundHeard { AtMs = NowMs, Kind = kind, Key = key, Clip = clip, Volume = volume, From = from, Row = row });
            if (heard.Count > HeardMax) heard.RemoveAt(0);
        }

        void Warn(string what)
        {
            if (warned.Add(what)) Debug.LogWarning("[Sound] " + what);
        }
    }
}
