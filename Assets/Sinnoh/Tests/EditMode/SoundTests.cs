using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M4-3 「소리 뼈대」(결정 66 ④, 04 §8-4, 03 §3·§7): 상황 곡 고르기 · 표 읽기 · 음량 단계·무리·더킹 셈 · 교차 페이드 · 한 프레임 같은 키 한 번.
    /// M9 소리 ②: 곡은 Pt 원곡(build.mjs bgm 묶음 — 앞부분 · 반복 클립과 반복 자리) · 반복 이음 시험(완료 조건 1).
    /// 표 시험은 Gen 본체(build.mjs bgm·se 묶음)를 System.IO로 본다 — 본체가 없는 새 사본에서는 건너뛴다(03 §7). 재생(Unity 음원)은 PlayMode 시험이 본다.
    /// </summary>
    public class SoundTests
    {
        static readonly Encoding Utf8 = new UTF8Encoding(false);
        static string Root => TestData.RepoRoot();

        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        static string GenText(string rel)
        {
            string p = Path.Combine(Root, "Assets/Sinnoh/Gen", rel);
            if (!File.Exists(p)) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저 (" + rel + ")");
            return File.ReadAllText(p, Utf8);
        }

        [Test]
        public void M4_상황곡표는상황마다곡이나무음()
        {
            var book = MusicBook.Parse(GenText("Audio/Bgm/bgm.json"));
            CollectionAssert.IsEmpty(book.Missing(), "줄이 없는 상황(03 §7 「상황마다 클립이 있거나 무음」)");
            Assert.AreEqual(Enum.GetValues(typeof(MusicScene)).Length, book.Count);
            foreach (MusicScene s in Enum.GetValues(typeof(MusicScene)))
            {
                string f = book.File(s);
                if (f == null) continue;
                Assert.IsTrue(File.Exists(Path.Combine(Root, "Assets/Sinnoh/Gen/Audio/Bgm", f)), s + ": " + f);
                Assert.IsNotEmpty(book.Title(s), s + " 제목");
            }
            // 배틀 곡은 Pt 원곡(03 §3-1 표 — M9 소리 ②)
            Assert.AreEqual("SEQ_BA_POKE", book.Title(MusicScene.BattleWild));
            Assert.AreEqual("SEQ_BA_GINGA", book.Title(MusicScene.BattleGalacticGrunt));
            Assert.AreEqual(book.File(MusicScene.Setup), book.File(MusicScene.Prologue), "셋업 → 서막은 끊기지 않는다");
            // 추적 표와 같은 상황 이름(빌드가 이름을 바꾸지 않았다)
            string table = File.ReadAllText(Path.Combine(Root, "Tools/assets/tables/bgm.json"), Utf8);
            foreach (MusicScene s in Enum.GetValues(typeof(MusicScene))) StringAssert.Contains("{\"situation\":\"" + s + "\"", table, s.ToString());
        }

        [Test]
        public void M4_효과음표는키마다파일이있다()
        {
            var book = SoundBook.Parse(GenText("Audio/Se/se.json"));
            Assert.AreEqual(SoundKeys.All.Length, book.Count, "표의 키 = SoundKeys.All");
            foreach (string key in SoundKeys.All)
            {
                var e = book.Find(key);
                Assert.IsNotNull(e, key);
                Assert.That(e.Seq.StartsWith("SEQ_") || e.Seq.StartsWith("plinkfx:sounds/"), key + " 원천은 SDAT 시퀀스(효과음 SEQ_SE_* · 징글 곡 — M9 소리 ②) · 원작 자산에 없는 것은 plinkfx 레시피(M9 소리 ③): " + e.Seq);
                string path = Path.Combine(Root, "Assets/Sinnoh/Gen/Audio", e.File);
                Assert.IsTrue(File.Exists(path), key + ": " + e.File);
                var b = File.ReadAllBytes(path);
                Assert.AreEqual("RIFF", Encoding.ASCII.GetString(b, 0, 4), key);
                Assert.AreEqual("WAVE", Encoding.ASCII.GetString(b, 8, 4), key);
                Assert.AreEqual(1, BitConverter.ToInt16(b, 22), key + " 모노");
                Assert.AreEqual(32768, BitConverter.ToInt32(b, 24), key + " 32768 Hz");
                Assert.AreEqual(16, BitConverter.ToInt16(b, 34), key + " 16비트");
                int samples = BitConverter.ToInt32(b, 40) / 2;
                Assert.AreEqual(b.Length - 44, samples * 2, key + " 자료 길이");
                Assert.That(Math.Abs(samples * 1000.0 / 32768 - e.Ms), Is.LessThan(1.0), key + " 표의 길이 = 클립 길이");
                Assert.That(e.Ms, Is.InRange(20, 6000), key + " 길이(시퀀스 전체 — 징글은 몇 초)");
                int peak = 0;
                for (int i = 0; i < samples; i++) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(b, 44 + 2 * i)));
                Assert.That(peak, Is.GreaterThan(16000), key + " 무음이 아니다(봉우리 −1 dBFS로 맞춤)");
            }
            Assert.AreEqual(SoundGroup.Big, book.Find(SoundKeys.MyTurn).Group, "내 차례는 큰 순간(04 §8-4)");
            var turn = book.Find(SoundKeys.MyTurn);
            var sel = book.Find(SoundKeys.Select);
            Assert.Greater(SoundMix.Effect(8, turn.Group, turn.Db), SoundMix.Effect(8, sel.Group, sel.Db), "내 차례는 다른 SE보다 크게(무리 + 키 dB)");
            Assert.That(turn.Db, Is.LessThanOrEqualTo(0), "결정 70 ⑮ — M9 소리 ③에서 +2 dB를 뺐다");
            Assert.AreEqual(SoundGroup.Big, book.Find(SoundKeys.Timer).Group, "응답 요청(남은 10초)도 다른 SE보다 크게(04 §8-4)");
            StringAssert.StartsWith("plinkfx:", book.Find(SoundKeys.Land).Seq, "결정 70 ⑮ — plinkfx 밝은 착지 [가정 §5 #353]");
            // M9 소리 ②: 곡 시퀀스의 징글(동안 BGM 더킹) — 회복 SEQ_ASA · 시선 곡 도입부 · 진화 곡 · 팡파르 · 교환 곡
            foreach (var (key, seq) in new[] { (SoundKeys.Center, "SEQ_ASA"), (SoundKeys.Meet, "SEQ_EYE_BOY"), (SoundKeys.Evolve, "SEQ_SHINKA"), (SoundKeys.Fanfare, "SEQ_FANFA5"), (SoundKeys.Trade, "SEQ_KOUKAN") })
            {
                Assert.AreEqual(seq, book.Find(key).Seq, key);
                Assert.IsTrue(book.Find(key).Duck, key + " 징글은 BGM 더킹");
            }
            // 시퀀스 전체 렌더(소리 ③의 첫 음 하나 → 전체): 음이 여럿인 효과음은 첫 음보다 길다
            Assert.That(book.Find(SoundKeys.MyTurn).Ms, Is.GreaterThan(300), "PINPON은 두 음");
        }

        // 16비트 PCM WAV 하나: 채널 수 · 표본 수 · 자료 자리
        static byte[] Wav(string rel, out int channels, out int samples)
        {
            string path = Path.Combine(Root, "Assets/Sinnoh/Gen/Audio/Bgm", rel);
            Assert.IsTrue(File.Exists(path), rel);
            var b = File.ReadAllBytes(path);
            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(b, 0, 4), rel);
            Assert.AreEqual("WAVE", Encoding.ASCII.GetString(b, 8, 4), rel);
            channels = BitConverter.ToInt16(b, 22);
            Assert.AreEqual(32768, BitConverter.ToInt32(b, 24), rel + " 32768 Hz(03 §3-2)");
            Assert.AreEqual(16, BitConverter.ToInt16(b, 34), rel + " 16비트");
            samples = BitConverter.ToInt32(b, 40) / (2 * channels);
            Assert.AreEqual(b.Length - 44, samples * 2 * channels, rel + " 자료 길이");
            return b;
        }

        static IEnumerable<MusicScene> Scenes => Enum.GetValues(typeof(MusicScene)).Cast<MusicScene>();

        [Test]
        public void M9_소리_상황곡은Pt원곡이고반복자리가클립길이안이다()
        {
            var book = MusicBook.Parse(GenText("Audio/Bgm/bgm.json"));
            int loops = 0, intros = 0, once = 0;
            foreach (var s in Scenes)
            {
                string f = book.File(s);
                if (f == null) continue;
                if (TtsScenes.Contains(s)) continue;   // 원작 TTS 곡 둘은 아래 시험(M9_소리_UB네크로즈마곡은원작TTS곡이고통짜반복이다)
                StringAssert.StartsWith("pt/SEQ_", f, s + " — Pt 원곡(VGMTrans + FluidSynth)");
                Assert.AreEqual(Path.GetFileNameWithoutExtension(f), book.Title(s), s + " 제목 = SSEQ 이름");
                var lp = book.Loop(s);
                Assert.IsNotNull(lp, s + " 반복 자리");
                Wav(f, out int ch, out int n);
                Assert.AreEqual(2, ch, s + " 스테레오");
                Assert.AreEqual(lp.LoopSamples, n, s + " 표의 반복 클립 길이 = 클립 길이");
                Assert.That(n, Is.GreaterThan(32768 * 3), s + " 3초보다 길다");
                if (book.Once(s))
                {
                    once++;
                    Assert.IsNull(book.Intro(s), s + " 끝 곡은 클립 하나");
                    Assert.AreEqual((0L, (long)n), (lp.Start, lp.End), s + " 끝 곡의 자리 = 곡 전체");
                    continue;
                }
                loops++;
                Assert.That(lp.Start, Is.GreaterThanOrEqualTo(0).And.LessThan(lp.End), s + " 반복 시작 < 끝");
                // 둘째 반복(돌고 있는 상태)의 길이는 첫 반복과 거의 같다(앞부분의 빠르기가 첫 반복 머리에 남는 곡만 조금 다르다)
                Assert.That(Math.Abs((lp.End - lp.Start) - lp.LoopSamples), Is.LessThan(lp.LoopSamples / 50), s + " 반복 길이");
                string intro = book.Intro(s);
                Assert.AreEqual(lp.Start > 0, intro != null, s + " 앞부분이 있는 곡만 앞부분 클립");
                if (intro == null) { Assert.AreEqual(0L, lp.IntroSamples, s.ToString()); continue; }
                intros++;
                Wav(intro, out int ich, out int inN);
                Assert.AreEqual(2, ich, s + " 앞부분 스테레오");
                Assert.AreEqual(lp.IntroSamples, inN, s + " 표의 앞부분 길이 = 클립 길이");
                Assert.AreEqual(lp.End, inN, s + " 앞부분 클립 = 렌더의 [0, 반복 끝) — 반복 자리가 클립 길이 안");
                Assert.That(lp.Start, Is.LessThan(inN), s + " 반복 시작이 앞부분 클립 안");
            }
            Assert.That(loops, Is.GreaterThanOrEqualTo(18), "반복하는 상황 곡(원작 TTS 곡 둘 밖)");
            Assert.That(intros, Is.GreaterThan(0));
            Assert.AreEqual(1, once, "끝 곡은 게임 끝 하나");
            Assert.IsTrue(book.Once(MusicScene.GameOver), "게임 끝 = 「The End」 곡(결정 87 V17 — 한 번 틀고 끝)");
            Assert.AreEqual(book.File(MusicScene.Setup), book.File(MusicScene.Prologue), "셋업 → 서막은 끊기지 않는다");
        }

        [Test]
        public void M9_결정130_타이틀은오프닝영상곡을통째되풀이하고로비는마박사곡이다()
        {
            // 사용자 결정 130: 타이틀 = SEQ_TITLE00(Pt 오프닝 영상 곡 — 원작 자산 시퀀스에 반복 자리가 없다 → 끝나면 처음부터), 방 로비 = SEQ_OPENING(마박사 소개 곡)
            var book = MusicBook.Parse(GenText("Audio/Bgm/bgm.json"));
            Assert.AreEqual(("pt/SEQ_TITLE00.wav", (string)null, false), (book.File(MusicScene.Title), book.Intro(MusicScene.Title), book.Once(MusicScene.Title)), "타이틀: 앞부분 없이 되풀이(once 아님)");
            Wav(book.File(MusicScene.Title), out _, out int n);
            var lp = book.Loop(MusicScene.Title);
            Assert.AreEqual((0L, (long)n, (long)n), (lp.Start, lp.End, lp.LoopSamples), "반복 = 곡 전체");
            StringAssert.Contains("\"whole\":true", GenText("Audio/Bgm/bgm.json").Split('\n').First(l => l.Contains("\"situation\":\"Title\"")));
            Assert.AreEqual("pt/SEQ_OPENING.wav", book.File(MusicScene.Lobby));
            Assert.AreNotEqual(book.File(MusicScene.Title), book.File(MusicScene.Lobby));
        }

        static readonly MusicScene[] TtsScenes = { MusicScene.BattleUltraBeast, MusicScene.BattleNecrozma };

        /// <summary>
        /// Pt에 맞는 곡이 없는 두 상황은 원작 TTS판 음악의 녹음(mp3 → 같은 포맷 WAV)이다(plan §5 #525): 반복 자리가 없으니 앞부분 클립 없이 곡 전체를 되풀이한다(통짜 반복).
        /// 이음매(곡 끝 → 곡 처음) 걸음은 <see cref="M9_소리_반복이음매는튀지않는다"/>가 다른 곡과 같은 기준으로 본다.
        /// </summary>
        [Test]
        public void M9_소리_UB네크로즈마곡은원작TTS곡이고통짜반복이다()
        {
            var book = MusicBook.Parse(GenText("Audio/Bgm/bgm.json"));
            Assert.AreEqual(("tts/ultra_beast.wav", "Ultra Beast"), (book.File(MusicScene.BattleUltraBeast), book.Title(MusicScene.BattleUltraBeast)));
            Assert.AreEqual(("tts/ultra_burst.wav", "Ultra Burst"), (book.File(MusicScene.BattleNecrozma), book.Title(MusicScene.BattleNecrozma)));
            foreach (var s in Scenes)
            {
                string f = book.File(s);
                if (f == null) continue;
                Assert.AreEqual(TtsScenes.Contains(s), f.StartsWith("tts/"), s + " — 원작 TTS 곡은 이 둘뿐(나머지는 Pt 원곡)");
                if (!TtsScenes.Contains(s)) continue;
                Assert.IsNull(book.Intro(s), s + " 앞부분 클립 없음");
                Assert.IsFalse(book.Once(s), s + " 반복한다");
                Wav(f, out int ch, out int n);
                Assert.AreEqual(2, ch, s + " 스테레오");
                var lp = book.Loop(s);
                Assert.AreEqual((0L, (long)n, 0L, (long)n), (lp.Start, lp.End, lp.IntroSamples, lp.LoopSamples), s + " 반복 = 곡 전체");
                Assert.That(n, Is.GreaterThan(32768 * 60), s + " 1분보다 길다");
            }
            string table = File.ReadAllText(Path.Combine(Root, "Tools/assets/tables/bgm.json"), Utf8);
            StringAssert.Contains("{\"situation\":\"BattleUltraBeast\",\"tts\":\"Ultra Beast\"", table);
            StringAssert.Contains("{\"situation\":\"BattleNecrozma\",\"tts\":\"Ultra Burst\"", table);
        }

        /// <summary>
        /// 완료 조건 1 「루프 이음 시험」: 재생은 앞부분 클립 → 반복 클립 → 반복 클립 …이다. 두 이음매(앞부분 끝 → 반복 처음, 반복 끝 → 반복 처음)에서
        /// 이웃 표본의 걸음(16비트, 두 채널 중 큰 것)이 그 곡 안에서 흔한 걸음을 넘지 않아야 한다 — 기준 = 반복 클립 안 이웃 표본 걸음의 99.9 백분위 이하(튀면 걸음이 그 곡의 가장 가파른 0.1%보다 커진다).
        /// </summary>
        [Test]
        public void M9_소리_반복이음매는튀지않는다()
        {
            var book = MusicBook.Parse(GenText("Audio/Bgm/bgm.json"));
            var seen = new HashSet<string>();
            var report = new StringBuilder();
            foreach (var s in Scenes)
            {
                string f = book.File(s);
                if (f == null || book.Once(s) || !seen.Add(f)) continue;
                var b = Wav(f, out int ch, out int n);
                int Step(byte[] x, int i, byte[] y, int j)
                {
                    int d = 0;
                    for (int c = 0; c < ch; c++) d = Math.Max(d, Math.Abs(BitConverter.ToInt16(x, 44 + (i * ch + c) * 2) - BitConverter.ToInt16(y, 44 + (j * ch + c) * 2)));
                    return d;
                }
                var hist = new int[65536];
                for (int i = 1; i < n; i++) hist[Step(b, i, b, i - 1)]++;
                int p999 = 0;
                for (long acc = 0, d = 65535; d >= 0; d--) { acc += hist[d]; if (acc * 1000 >= n) { p999 = (int)d; break; } }
                int seam = Step(b, 0, b, n - 1);
                Assert.That(seam, Is.LessThanOrEqualTo(p999), f + " 반복 끝 → 반복 처음의 걸음 " + seam + " > 99.9 백분위 " + p999);
                int lead = -1;
                string intro = book.Intro(s);
                if (intro != null)
                {
                    var a = Wav(intro, out int ich, out int inN);
                    Assert.AreEqual(ch, ich);
                    lead = Step(b, 0, a, inN - 1);
                    Assert.That(lead, Is.LessThanOrEqualTo(p999), f + " 앞부분 끝 → 반복 처음의 걸음 " + lead + " > 99.9 백분위 " + p999);
                }
                report.Append(f).Append('\t').Append(seam).Append('\t').Append(lead).Append('\t').Append(p999).Append('\n');
            }
            Assert.That(seen.Count, Is.GreaterThanOrEqualTo(20), "반복 곡 수");
            string outPath = Environment.GetEnvironmentVariable("SINNOH_BGM_SEAMS");
            if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, "file\tloop_seam\tintro_seam\tp999\n" + report, Utf8);
        }

        [Test]
        public void M9_소리_결과곡은이긴배틀의종류를따른다()
        {
            Assert.AreEqual(MusicScene.ResultTrainer, MusicScenes.Result(BattleBgm.Trainer));
            foreach (var g in new[] { BattleBgm.GalacticGrunt, BattleBgm.GalacticCommander, BattleBgm.GalacticBoss }) Assert.AreEqual(MusicScene.ResultGalactic, MusicScenes.Result(g), g.ToString());
            foreach (BattleBgm bgm in Enum.GetValues(typeof(BattleBgm)))
            {
                if (bgm == BattleBgm.Trainer || bgm == BattleBgm.GalacticGrunt || bgm == BattleBgm.GalacticCommander || bgm == BattleBgm.GalacticBoss) continue;
                Assert.AreEqual(MusicScene.Result, MusicScenes.Result(bgm), bgm.ToString());
            }
        }

        [Test]
        public void M9_소리_곡표는앞부분과끝곡과반복자리를읽는다()
        {
            var book = MusicBook.Parse("{\"schema\":2,\"rows\":[\n"
                + "{\"situation\":\"Title\",\"file\":\"pt/A.wav\",\"title\":\"A\",\"intro\":\"pt/A.intro.wav\",\"loopStart\":100,\"loopEnd\":900,\"introSamples\":900,\"loopSamples\":800,\"seam\":{\"step\":3,\"max\":9,\"p999\":5,\"blend\":-20},\"rmsDb\":-16,\"peakDb\":-1},\n"
                + "{\"situation\":\"Setup\",\"file\":\"pt/B.wav\",\"title\":\"B\",\"loopStart\":0,\"loopEnd\":500,\"introSamples\":0,\"loopSamples\":500},\n"
                + "{\"situation\":\"GameOver\",\"file\":\"pt/C.wav\",\"title\":\"C\",\"once\":true,\"loopStart\":0,\"loopEnd\":700,\"introSamples\":0,\"loopSamples\":700}\n]}\n");
            Assert.AreEqual(("pt/A.wav", "pt/A.intro.wav", false), (book.File(MusicScene.Title), book.Intro(MusicScene.Title), book.Once(MusicScene.Title)));
            var lp = book.Loop(MusicScene.Title);
            Assert.AreEqual((100L, 900L, 900L, 800L), (lp.Start, lp.End, lp.IntroSamples, lp.LoopSamples));
            Assert.AreEqual(("pt/B.wav", (string)null, false), (book.File(MusicScene.Setup), book.Intro(MusicScene.Setup), book.Once(MusicScene.Setup)));
            Assert.AreEqual(("pt/C.wav", (string)null, true), (book.File(MusicScene.GameOver), book.Intro(MusicScene.GameOver), book.Once(MusicScene.GameOver)));
            Assert.IsNull(book.Loop(MusicScene.Encounter));
            Assert.IsNull(book.Intro(MusicScene.Encounter));
        }

        [Test]
        public void M4_보드곡은막과기후로고른다()
        {
            Assert.AreEqual(MusicScene.Prologue, MusicScenes.Board(0, Climate.Cold), "서막은 기후와 무관");
            Assert.AreEqual(MusicScene.Prologue, MusicScenes.Board(-1, Climate.Warm), "막을 모르면 서막");
            Assert.AreEqual(MusicScene.FieldCold, MusicScenes.Board(1, Climate.Cold));
            Assert.AreEqual(MusicScene.FieldWarm, MusicScenes.Board(2, Climate.Warm));
            Assert.AreEqual(MusicScene.FieldNoble, MusicScenes.Board(3, Climate.Noble));
            Assert.AreEqual(MusicScene.FieldSpace, MusicScenes.Board(1, Climate.Space));
            Assert.AreEqual(MusicScene.FieldTemperate, MusicScenes.Board(1, Climate.Temperate));
            Assert.AreEqual(MusicScene.FieldTemperate, MusicScenes.Board(1, Climate.None), "기후 없음 = 온대");
            Assert.AreEqual(MusicScene.FinalAct, MusicScenes.Board(HosState.Acts - 1, Climate.Cold), "종막");
        }

        [Test]
        public void M4_배틀곡은엔진의배틀상황키를잇는다()
        {
            var seen = new HashSet<MusicScene>();
            foreach (BattleBgm bgm in Enum.GetValues(typeof(BattleBgm)))
            {
                var s = MusicScenes.Battle(bgm);
                Assert.AreEqual("Battle" + bgm, s.ToString(), "상황 이름 = Battle + BattleBgm");
                Assert.IsTrue(seen.Add(s), bgm + " 두 상황이 같은 곡 칸");
            }
            Assert.AreEqual(MusicScene.BattleGalacticGrunt, MusicScenes.Battle(BattleRules.BgmOf(BattleKind.Galactic)));
            Assert.AreEqual(MusicScene.BattleWild, MusicScenes.Battle(BattleRules.BgmOf(BattleKind.Wild)));
        }

        [Test]
        public void M4_표읽기는모르는상황과겹친줄을거절한다()
        {
            var book = MusicBook.Parse("{\"schema\":1,\"rows\":[\n{\"situation\":\"Title\",\"file\":\"tts/a.mp3\",\"title\":\"A\"},\n{\"situation\":\"GameOver\",\"silent\":true}\n]}\n");
            Assert.AreEqual("tts/a.mp3", book.File(MusicScene.Title));
            Assert.IsTrue(book.Has(MusicScene.GameOver));
            Assert.IsNull(book.File(MusicScene.GameOver), "무음");
            Assert.IsFalse(book.Has(MusicScene.Encounter));
            Assert.Contains(MusicScene.Encounter, book.Missing());
            Assert.Throws<FormatException>(() => MusicBook.Parse("{\"situation\":\"Credits\",\"silent\":true}"), "모르는 상황");   // 옛 예 「Lobby」는 결정 130으로 상황이 됐다
            Assert.Throws<FormatException>(() => MusicBook.Parse("{\"situation\":\"Title\",\"silent\":true}\n{\"situation\":\"Title\",\"silent\":true}"), "같은 상황 두 번");
            var se = SoundBook.Parse("{\"key\":\"hit\",\"file\":\"Se/hit.wav\",\"group\":\"big\",\"db\":-3,\"ms\":144,\"seq\":\"SEQ_SE_DP_KOUKA_M\",\"duck\":true}");
            Assert.AreEqual(SoundGroup.Big, se.Find("hit").Group);
            Assert.AreEqual(-3, se.Find("hit").Db);
            Assert.IsTrue(se.Find("hit").Duck);
            Assert.IsNull(se.Find("없음"));
            Assert.Throws<FormatException>(() => SoundBook.Parse("{\"key\":\"a\",\"file\":\"Se/a.wav\",\"group\":\"small\",\"db\":0,\"ms\":1,\"seq\":\"x\"}\n{\"key\":\"a\",\"file\":\"Se/a.wav\",\"group\":\"small\",\"db\":0,\"ms\":1,\"seq\":\"x\"}"));
        }

        [Test]
        public void M4_음량은단계와무리와더킹으로센다()
        {
            Assert.AreEqual(0f, SoundMix.Music(0, 0), "끔");
            Assert.AreEqual(0f, SoundMix.Effect(0, SoundGroup.Big, 6), "끔");
            float prev = 0f;
            for (int step = 1; step <= SoundMix.MaxStep; step++)
            {
                float v = SoundMix.Effect(step, SoundGroup.Small, 0);
                Assert.Greater(v, prev, "단계가 오르면 커진다 " + step);
                prev = v;
            }
            Assert.AreEqual(SoundMix.Effect(SoundMix.MaxStep, SoundGroup.Small, 0), SoundMix.Effect(SoundMix.MaxStep + 4, SoundGroup.Small, 0), "단계는 잘린다");
            Assert.AreEqual(1f, SoundMix.Effect(SoundMix.MaxStep, SoundGroup.Big, 0), 1e-6f, "큰 순간 · 가장 큰 단계 = 0 dB");
            // 슬라이더(결정 70 ⑭): 0~10 — 짝수 단계는 옛 여섯 단계(끔·1~5)의 세기 그대로, 기본값은 옛 3·4와 같은 세기
            Assert.AreEqual(10, SoundMix.MaxStep);
            CollectionAssert.AreEqual(new[] { -20.0, -14.0, -9.0, -4.0, 0.0 }, new[] { SoundMix.StepDb(2), SoundMix.StepDb(4), SoundMix.StepDb(6), SoundMix.StepDb(8), SoundMix.StepDb(10) });
            Assert.AreEqual((-9.0, -4.0), (SoundMix.StepDb(SoundMix.DefaultBgmStep), SoundMix.StepDb(SoundMix.DefaultSeStep)), "기본 = 옛 배경음악 3 · 효과음 4의 세기");
            Assert.AreEqual(SoundMix.Linear(SoundMix.SmallDb.Value), SoundMix.Effect(SoundMix.MaxStep, SoundGroup.Small, 0), 1e-6f, "작은 순간 = 무리 dB");
            // 더킹: 다 내려가면 −13 dB(조정 값)
            double ratio = 20 * Math.Log10(SoundMix.Music(4, 1) / SoundMix.Music(4, 0));
            Assert.AreEqual(-13.0, ratio, 1e-3);
            Tunables.Shared.Set("fx.sound.duckDb", -20);
            Assert.AreEqual(-20.0, 20 * Math.Log10(SoundMix.Music(4, 1) / SoundMix.Music(4, 0)), 1e-3, "더킹 깊이는 조정 값");
            Assert.AreEqual(TunableScope.Local, SoundMix.DuckDb.Scope);
            Assert.AreEqual(TunableScope.Local, SoundMix.CrossfadeMs.Scope);
        }

        [Test]
        public void M4_곡바꾸기는교차페이드()
        {
            int ms = SoundMix.CrossfadeMs.Value;
            var f = new MusicFade();
            Assert.IsTrue(f.Switch("a"));
            Assert.AreEqual(0f, f.CurrentGain, "처음 곡도 무음에서 들어온다");
            Assert.IsNull(f.Previous);
            f.Advance(ms);
            Assert.AreEqual(1f, f.CurrentGain);
            Assert.IsFalse(f.Switch("a"), "같은 곡이면 그대로");
            Assert.IsTrue(f.Switch("b"));
            Assert.AreEqual("a", f.Previous);
            Assert.AreEqual(1f, f.PreviousGain);
            Assert.AreEqual(0f, f.CurrentGain);
            f.Advance(ms / 2);
            Assert.AreEqual(0.5f, f.CurrentGain, 0.01f);
            Assert.AreEqual(0.5f, f.PreviousGain, 0.01f);
            Assert.IsTrue(f.Fading);
            // 페이드 가운데 또 바꾸면: 지금 곡(b, 0.5)이 거기서부터 줄고 a는 끊긴다
            Assert.IsTrue(f.Switch("c"));
            Assert.AreEqual("b", f.Previous);
            Assert.AreEqual(0.5f, f.PreviousGain, 0.01f);
            f.Advance(ms);
            Assert.IsNull(f.Previous, "다 나간 곡은 놓는다");
            Assert.AreEqual(0f, f.PreviousGain);
            Assert.IsFalse(f.Fading);
            // 무음으로
            Assert.IsTrue(f.Switch(null));
            Assert.AreEqual(0f, f.CurrentGain);
            Assert.AreEqual(1f, f.PreviousGain);
        }

        [Test]
        public void M4_더킹은징글동안내려갔다올라온다()
        {
            int fade = SoundMix.DuckFadeMs.Value;
            var d = new MusicDuck();
            Assert.AreEqual(0.0, d.Weight(0));
            d.Hold(1000, 2000);
            Assert.AreEqual(0.0, d.Weight(999));
            Assert.AreEqual(0.5, d.Weight(1000 + fade / 2), 0.01);
            Assert.AreEqual(1.0, d.Weight(2000));
            Assert.AreEqual(0.5, d.Weight(3000 - fade / 2), 0.01);
            Assert.AreEqual(0.0, d.Weight(3000), "끝나면 다시 0");
            d.Hold(4000, 0);
            Assert.AreEqual(0.0, d.Weight(4000), "길이 0은 걸지 않는다");
        }

        [Test]
        public void M4_한프레임에같은키는한번()
        {
            var f = new SoundFrame();
            f.Begin();
            Assert.IsTrue(f.Take(SoundKeys.Hit));
            Assert.IsFalse(f.Take(SoundKeys.Hit), "같은 프레임 같은 키");
            Assert.IsTrue(f.Take(SoundKeys.Land), "다른 키는 낸다");
            Assert.IsFalse(f.Take(null));
            f.Begin();
            Assert.IsTrue(f.Take(SoundKeys.Hit), "다음 프레임은 다시 낸다");
        }

        [Test]
        public void M4_소리단서는시퀀서시계로닿고다시보기는무음()
        {
            // 단서를 받아 트는 길의 앞쪽(04 §8-4): 소리 단서는 (지난 시각, 지금]을 훑어 시퀀서의 Sound로 나온다 — 한 번에 보낸 것 = 잘게 보낸 것, Muted면 안 나온다
            var heard = new List<string>();
            var seq = new FxSequencer();
            seq.Sound += c => heard.Add(c.Sound + "@" + c.AtMs);
            var e = new FxEntry { Seq = 1, QueueMs = 1000 };
            e.AddSound(0, SoundKeys.CardFlip);
            e.AddSound(400, SoundKeys.Land);
            e.AddSound(400, SoundKeys.Hit);
            seq.Enqueue(new[] { e });
            seq.Advance(399);
            CollectionAssert.AreEqual(new[] { "card_flip@0" }, heard);
            seq.Advance(1);
            CollectionAssert.AreEqual(new[] { "card_flip@0", "land@400", "hit@400" }, heard);
            var muted = new FxSequencer { Muted = true };
            int n = 0;
            muted.Sound += c => n++;
            var m = new FxEntry { Seq = 2, QueueMs = 100 };
            m.AddSound(50, SoundKeys.Hit);
            muted.Enqueue(new[] { m });
            muted.Advance(100);
            Assert.AreEqual(0, n);
            Assert.That(SoundKeys.All.Distinct().Count(), Is.EqualTo(SoundKeys.All.Length));
        }

        [Test]
        public void M9_울음표는쓰는종마다PokeAPI울음이있다()
        {
            // 결정 31 ⑤ · 67 ①: 울음은 PokeAPI 한 출처 — pokemon.json의 종(dex) 322 모두, #1~649 legacy · #650 이후 latest
            var book = CryBook.Parse(GenText("Audio/Cry/cry.json"));
            var dexes = TestData.LoadCtx().Cards.Pokemon.Select(p => p.Dex).Distinct().ToList();
            Assert.AreEqual(322, dexes.Count, "결정 31 ⑤ 322종");
            Assert.AreEqual(dexes.Count + 3, book.Count, "표 = 쓰는 종 + 폼 울음 3(결정 143 ④)");
            foreach (int dex in dexes)
            {
                string f = book.File(dex);
                Assert.IsNotNull(f, "#" + dex);
                var b = File.ReadAllBytes(Path.Combine(Root, "Assets/Sinnoh/Gen/Audio", f));
                Assert.AreEqual("OggS", Encoding.ASCII.GetString(b, 0, 4), "#" + dex + " Ogg");
            }
            StringAssert.Contains("PokeAPI/cries", GenText("Audio/Cry/cry.json"));
            Assert.IsNull(book.File(9999));
            // 단서 키
            Assert.AreEqual("cry:483", SoundKeys.CryKey(483, false));
            Assert.AreEqual("cry_low:25", SoundKeys.CryKey(25, true));
            Assert.IsTrue(SoundKeys.TryCry("cry_low:25", out int d, out bool low) && d == 25 && low);
            Assert.IsFalse(SoundKeys.TryCry(SoundKeys.Hit, out _, out _));
            Assert.IsFalse(SoundKeys.TryCry("cry:x", out _, out _));
            Assert.IsFalse(SoundKeys.TryCry("cry:0", out _, out _));
            Assert.Throws<FormatException>(() => CryBook.Parse("{\"dex\":1,\"file\":\"Cry/1.ogg\"}\n{\"dex\":1,\"file\":\"Cry/1.ogg\"}"));
            Assert.That(CryBook.LowPitch, Is.InRange(0.7f, 0.95f), "기절 울음은 낮춰서 [가정 §5 #352]");
        }

        [Test]
        public void M10_결정143_네크로즈마폼은PokeAPI폼울음이고다른폼은종의울음이다()
        {
            // 폼 울음 표(Tools/assets/tables/form_cries.json) → Gen cry.json 행의 form 칸 → CryBook.Form
            var book = CryBook.Parse(GenText("Audio/Cry/cry.json"));
            var ctx = TestData.LoadCtx();
            var keys = new HashSet<string>(ctx.Cards.Pokemon.Select(p => p.Key));
            var table = File.ReadAllText(Path.Combine(Root, "Tools/assets/tables/form_cries.json"));
            var rows = System.Text.RegularExpressions.Regex.Matches(table, "\\{\"key\":\"([^\"]+)\",\"id\":(\\d+)");
            Assert.AreEqual(3, rows.Count, "폼 울음 셋(네크로즈마 2·3페이즈 · 울트라 네크로즈마)");
            foreach (System.Text.RegularExpressions.Match m in rows)
            {
                string key = m.Groups[1].Value;
                int id = int.Parse(m.Groups[2].Value);
                Assert.IsTrue(keys.Contains(key), key + " = 포켓몬 표 키");
                Assert.AreEqual(id, book.Form(key), key + " → PokeAPI 폼 id");
                Assert.IsNotNull(book.File(id), key + " 울음 파일");
                Assert.AreNotEqual(File.ReadAllBytes(Path.Combine(Root, "Assets/Sinnoh/Gen/Audio", book.File(800))).Length,
                    File.ReadAllBytes(Path.Combine(Root, "Assets/Sinnoh/Gen/Audio", book.File(id))).Length, key + ": 종(800)과 다른 울음");
            }
            Assert.AreEqual(10157, book.Form(UbNecrozma.Ultra), "울트라 네크로즈마");
            Assert.AreEqual(0, book.Form(UbNecrozma.Phase1), "1페이즈 네크로즈마 = 종의 울음");
            Assert.AreEqual(0, book.Form("487:2"), "폼 울음 표 밖의 폼 = 종의 울음");
            Assert.AreEqual(0, book.Form(null));
            Assert.Throws<FormatException>(() => CryBook.Parse("{\"dex\":10155,\"file\":\"Cry/10155.ogg\",\"bytes\":1,\"form\":\"800:2\"}\n{\"dex\":10156,\"file\":\"Cry/10156.ogg\",\"bytes\":1,\"form\":\"800:2\"}"), "같은 폼 두 번");
        }
    }
}
