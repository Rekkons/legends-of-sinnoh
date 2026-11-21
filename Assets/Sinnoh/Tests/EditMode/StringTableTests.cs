using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>plan M2 조건 4: 한글 이름 누락 0(322종), strings.ko에 ff1ce0 10묶음, strings.en 이름 누락 0. 왕복·엄격한 로더(조건 3).</summary>
    public class StringTableTests
    {
        CardDb db;
        StringTable ko, en;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            db = TestData.LoadCardDb();
            ko = TestData.LoadStrings("ko");
            en = TestData.LoadStrings("en");
        }

        /// <summary>ff1ce0 묶음 → 키 수(korean.md §8.1). 원작 module_language_english와 같다.</summary>
        static readonly (string Group, int Keys)[] Ff1ce0Groups =
        {
            ("arena_dice_roller", 6), ("errors", 2), ("music_box", 9), ("pokedex", 14), ("setup_panel", 3),
            ("teleport", 1), ("tooltips", 1), ("types", 19), ("ub_control_panel", 3), ("village", 3),
        };

        static bool HasHangul(string s)
        {
            foreach (char c in s)
                if (c >= '가' && c <= '힣') return true;
            return false;
        }

        /// <summary>데이터 행마다 이름 키(species·pokemon·move·trainer_card).</summary>
        IEnumerable<string> NameKeys()
        {
            var species = new SortedSet<int>();
            foreach (var p in db.Pokemon)
            {
                species.Add(p.Dex);
                yield return "pokemon." + p.Key;
            }
            foreach (int dex in species) yield return "species." + dex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var m in db.Moves) yield return "move." + m.Key;
            foreach (var t in db.TrainerCards) yield return "trainer_card." + t.Key;
        }

        [Test]
        public void M2_한글이름누락0_종322_카드390_기술236_직업51()
        {
            var missing = new List<string>();
            int species = 0;
            foreach (var key in NameKeys())
            {
                if (!ko.TryGet(key, out var text) || !HasHangul(text)) missing.Add(key);
                if (key.StartsWith("species.", StringComparison.Ordinal)) species++;
            }
            Assert.AreEqual(322, species);
            CollectionAssert.IsEmpty(missing, "한글 이름이 없는 키");
            foreach (var m in db.Moves) Assert.IsTrue(HasHangul(ko["move." + m.Key + ".text"]), m.Key);
            Assert.AreEqual("로토무 (스핀로토무)", ko["pokemon.479:2"]);
            Assert.AreEqual("트리토돈 (서쪽바다 폼)", ko["pokemon.423:0@jupiter"], "갤럭시단 카드: 주인 꼬리를 빼고 같은 영문의 야생 카드 이름");
            Assert.AreEqual("한카리아스", ko["species.445"]);
        }

        [Test]
        public void M2_strings_ko에ff1ce0_10묶음_61키()
        {
            foreach (var table in new[] { ko, en })
            {
                var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var key in table.Keys)
                {
                    if (!key.StartsWith("ui.", StringComparison.Ordinal)) continue;
                    int dot = key.IndexOf('.', 3);
                    Assert.That(dot, Is.GreaterThan(3), key);
                    string group = key.Substring(3, dot - 3);
                    counts[group] = counts.TryGetValue(group, out int n) ? n + 1 : 1;
                }
                Assert.AreEqual(Ff1ce0Groups.Length, counts.Count, $"strings.{table.Lang} 묶음 수");
                foreach (var (group, keys) in Ff1ce0Groups)
                    Assert.AreEqual(keys, counts.TryGetValue(group, out int n) ? n : 0, $"strings.{table.Lang} ui.{group}");
            }
            Assert.AreEqual("신오의 심장 캠페인", ko["ui.setup_panel.HOS_CAMPAIGN_LABEL"]);
            Assert.AreEqual("에스퍼", ko["ui.types.Psychic"]);
            Assert.IsTrue(HasHangul(ko["ui.music_box.OFFLINE_LABEL"]), "한국어판에서 영어로 남은 문자열도 옮긴다");
        }

        [Test]
        public void M2_strings_en이름누락0_xlsx원문()
        {
            var missing = new List<string>();
            foreach (var key in NameKeys())
                if (!en.TryGet(key, out var text) || text.Trim().Length == 0) missing.Add(key);
            CollectionAssert.IsEmpty(missing, "영어 이름이 없는 키");
            // 카드 이름 = xlsx internal_name. 폼 면이 바탕 면과 internal_name이 같으면(수호신 폼·오리진폼) 원작 영문 세이브 카드 이름
            var names = new HashSet<string>();
            foreach (var p in db.Pokemon)
            {
                bool sameAsBase = false;
                foreach (var q in db.Pokemon)
                    if (p.State >= 2 && q.State == 1 && q.Dex == p.Dex && q.Trainer == p.Trainer && q.Name == p.Name) sameAsBase = true;
                if (sameAsBase) StringAssert.StartsWith(p.Name + " (", en["pokemon." + p.Key], p.Key);
                else Assert.AreEqual(p.Name, en["pokemon." + p.Key], p.Key);
                Assert.IsTrue(names.Add(p.Dex + "@" + p.Trainer + "=" + en["pokemon." + p.Key]), $"{p.Key}: 같은 종·주인 안에서 영어 카드 이름이 겹친다");
            }
            Assert.AreEqual("Torterra (Noble Form)", en["pokemon.389:2"]);
            Assert.AreEqual("Dialga (Adamant Form)", en["pokemon.483:2"]);
            Assert.AreEqual("Palkia (Lustrous Form)", en["pokemon.484:2"]);
            foreach (var m in db.Moves)
            {
                Assert.AreEqual(m.Name, en["move." + m.Key], m.Key);
                Assert.AreEqual(m.En, en["move." + m.Key + ".text"], m.Key);
            }
            foreach (var t in db.TrainerCards) Assert.AreEqual(t.Class, en["trainer_card." + t.Key], t.Key);
            Assert.AreEqual("Rotom", en["species.479"]);
            Assert.AreEqual("Heart of Sinnoh Campaign", en["ui.setup_panel.HOS_CAMPAIGN_LABEL"]);
        }

        [Test]
        public void M2_한영키집합이같다()
        {
            CollectionAssert.AreEqual(en.Keys, ko.Keys);
            Assert.AreEqual("ko", ko.Lang);
            Assert.AreEqual("en", en.Lang);
            var manifest = ContentManifest.Load(TestData.ReadText("manifest.json"));
            Assert.AreEqual(ko.Count, manifest.Find("strings.ko").Rows);
            Assert.AreEqual(en.Count, manifest.Find("strings.en").Rows);
            CollectionAssert.DoesNotContain(manifest.ContentTables, "strings.ko", "엔진은 문자열 표를 읽지 않는다(01 §11)");
        }

        /// <summary>
        /// Tools/data/authored/terms-ko.json의 규칙 정규식(re) 전체. 빌드가 한국어 값에 적용하는 규칙이라 결과에는 어느 것도 남지 않아야 한다
        /// (바뀐 값 to는 어느 re에도 맞지 않는다). JSON 문자열 이스케이프가 없는 꼴만 받는다.
        /// </summary>
        static List<string> TermPatterns()
        {
            string json = File.ReadAllText(Path.Combine(TestData.RepoRoot(), "Tools/data/authored/terms-ko.json"), new UTF8Encoding(false));
            var list = new List<string>();
            foreach (Match m in Regex.Matches(json, "\"re\":\\s*\"([^\"]*)\""))
            {
                Assert.IsFalse(m.Groups[1].Value.Contains("\\"), $"terms-ko.json re 이스케이프는 시험이 읽지 못한다: {m.Groups[1].Value}");
                list.Add(m.Groups[1].Value);
            }
            Assert.AreEqual(Regex.Matches(json, "\"re\"").Count, list.Count, "terms-ko.json re를 모두 읽었다");
            return list;
        }

        [Test]
        public void M2_용어는korean9권장열()
        {
            var patterns = TermPatterns();
            Assert.AreEqual(49, patterns.Count, "terms-ko.json 규칙 수");
            var terms = new List<Regex>();
            foreach (var p in patterns) terms.Add(new Regex(p, RegexOptions.CultureInvariant));
            foreach (var key in ko.Keys)
            {
                string v = ko[key];
                foreach (var re in terms)
                    Assert.IsFalse(re.IsMatch(v), $"{key}: terms-ko.json 「{re}」(korean.md §9)가 남았다 — {v}");
                foreach (var old in new[] { "전투피로", "울트라비스트", "프리즘아머", "피로함", "현재 우선권" })
                    Assert.IsFalse(v.Contains(old), $"{key}: 「{old}」(korean.md §9) — {v}");
                Assert.IsFalse(v.Replace("전투 피로", "").Contains("전투"), $"{key}: UI는 배틀(§9 #26) — {v}");
            }
            StringAssert.StartsWith("전투 피로:", ko["ui.arena_dice_roller.FATIGUED_TOOLTIP"]);
            Assert.AreEqual("전수목", ko["pokemon.796:0"], "영문 Xurkitree 그대로(결정 163 — 옛 꼬리 「(울트라 비스트)」 없음)");
        }

        /// <summary>결정 42·43: 화면 문구(ui.*)에 쓰지 않는 말 — 어색한 말투와 공식 용어(불꽃·순간이동·얼음·졸음·지닌물건·위력·급소·스피드)로 바꾼 옛 말.</summary>
        internal static readonly string[] BannedUiWords =
        {
            "턴 끝", "턴끝", "공간이동", "빙결", "졸림", "잠듦", "소지 아이템", "소지아이템", "공격력", "크리티컬", "우선도", "타입없음", "상태이상", "맵", "들리기", "불 타입", "불타입",
        };

        [Test]
        public void M4_화면문구에금지어없음_결정42_43()
        {
            // ui.* = 원작 언어 모듈(ff1ce0), screen.* = 우리 화면(Tools/data/authored/screens.json, M4-3)
            int ui = 0, screen = 0;
            foreach (var key in ko.Keys)
            {
                bool isUi = key.StartsWith("ui.", StringComparison.Ordinal), isScreen = key.StartsWith("screen.", StringComparison.Ordinal);
                if (!isUi && !isScreen) continue;
                if (isUi) ui++; else screen++;
                foreach (var w in BannedUiWords) Assert.IsFalse(ko[key].Contains(w), $"{key}: 「{w}」(결정 42·43) — {ko[key]}");
            }
            Assert.AreEqual(61, ui, "ui.* 키 수");
            Assert.Greater(screen, 0, "screen.* 키가 있다");
            Assert.AreEqual("불꽃", ko["ui.types.Fire"], "공식 타입명(원작 자산 0617)");
            Assert.AreEqual("타입 없음", ko["ui.types.Typeless"]);
            Assert.AreEqual("여기로 순간이동", ko["ui.teleport.TELEPORT_BUTTON_TOOLTIP"]);
            Assert.AreEqual("위력 전환", ko["ui.arena_dice_roller.DICE_BUTTON_TOOLTIP"], "Toggle Attack Strength — Attack Strength = 위력");
        }

        /// <summary>
        /// 우리 화면 문구(screen.*, plan §4 「솔로 설정·셋업」): 두 언어에 같은 키, 한국어에는 한글, 안내 문장(마침표로 끝남)은 합쇼체(ui-text-guide §1-4).
        /// 타이틀 메뉴 일곱 줄은 이름과 설명 첫 줄이 있다.
        /// </summary>
        [Test]
        public void M4_화면문구는문자열표에있고안내는합쇼체()
        {
            int n = 0;
            foreach (var key in ko.Keys)
            {
                if (!key.StartsWith("screen.", StringComparison.Ordinal)) continue;
                n++;
                Assert.IsTrue(en.Contains(key), key + ": 영어 표에 없다");
                string v = ko[key];
                Assert.IsTrue(HasHangul(v), key + ": 한글이 없다");
                // 감정 표현 문구(screen.room.emote.line.*)는 사용자가 정한 대사 그대로다(결정 102 — 안내 문장이 아니라 캐릭터 말)
                if (v.EndsWith(".", StringComparison.Ordinal) && !key.StartsWith("screen.room.emote.line.", StringComparison.Ordinal))
                    Assert.IsTrue(v.EndsWith("니다.", StringComparison.Ordinal) || v.EndsWith("십시오.", StringComparison.Ordinal), $"{key}: 안내는 합쇼체 — {v}");
            }
            foreach (var key in en.Keys)
                if (key.StartsWith("screen.", StringComparison.Ordinal)) Assert.IsTrue(ko.Contains(key), key + ": 한국어 표에 없다");
            Assert.Greater(n, 40);
            foreach (var id in new[] { "continue", "host", "join", "solo", "settings", "credits", "quit" })
            {
                Assert.IsTrue(ko.Contains("screen.title." + id), id);
                Assert.IsTrue(ko.Contains("screen.title." + id + ".desc1"), id);
            }
            Assert.AreEqual("혼자 하기", ko["screen.title.solo"]);
            Assert.AreEqual("사용 안 함", ko["screen.common.off"], "ui-text-guide §2-1");
        }

        /// <summary>트레이너 카드 능력 102개의 이름·문구(trainer_ability.&lt;키&gt;.&lt;1|2&gt;[.text], S13이 읽는다): 영어 = 데이터(xlsx), 한국어에는 한글. 용어는 지침대로(스피드·위력·데미지)이고,
        /// 화면에 그대로 뜨는 글이라 화면 금지어(<see cref="BannedUiWords"/>)와 평서형 끝 「~한다.」도 없다(ui-text-guide §1-6·§2-1).</summary>
        [Test]
        public void M4_트레이너카드능력이름문구102()
        {
            int n = 0;
            foreach (var t in db.TrainerCards)
                foreach (var a in t.Abilities)
                {
                    string key = "trainer_ability." + t.Key + "." + a.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Assert.AreEqual(a.Name, en[key], key);
                    Assert.AreEqual(a.En, en[key + ".text"], key);
                    Assert.IsTrue(HasHangul(ko[key]), key + ": " + ko[key]);
                    Assert.IsTrue(HasHangul(ko[key + ".text"]), key);
                    foreach (var w in new[] { "피해", "스피드을" })   // 「우선권 토큰」은 따로 있는 말이다(ui-text-guide §2-1)
                        Assert.IsFalse(ko[key + ".text"].Contains(w), $"{key}: 「{w}」 — {ko[key + ".text"]}");
                    foreach (var w in BannedUiWords)
                        Assert.IsFalse(ko[key + ".text"].Contains(w), $"{key}: 「{w}」(결정 42·43) — {ko[key + ".text"]}");
                    Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(ko[key + ".text"], @"[^니]다\.(\s|$)"), $"{key}: 평서형(ui-text-guide §1-6) — {ko[key + ".text"]}");
                    n++;
                }
            Assert.AreEqual(102, n);
            Assert.AreEqual("팀 빌더", ko["trainer_ability.ace_trainer.1"]);
            Assert.AreEqual("강한 포켓몬은 스피드 +1.", ko["trainer_ability.ace_trainer.2.text"]);
        }

        [Test]
        public void M6_트레이너카드능력짧은문구102_48자이하_결정87()
        {
            // 사용자 결정 87 V11 라: 해금 카드의 능력 칸은 짧은 문구(trainer_ability.<키>.<n>.short), 전체 문구는 마우스 오버
            int n = 0, own = 0, ownEn = 0;
            foreach (var t in db.TrainerCards)
                foreach (var a in t.Abilities)
                {
                    string key = "trainer_ability." + t.Key + "." + a.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    string s = ko[key + ".short"], full = ko[key + ".text"];
                    Assert.IsFalse(string.IsNullOrWhiteSpace(s), key);
                    Assert.LessOrEqual(s.Length, 48, $"{key}: {s}");
                    Assert.IsTrue(HasHangul(s), key);
                    if (full.Length > 48) Assert.AreNotEqual(full, s, key);
                    if (s != full) own++;
                    foreach (var w in BannedUiWords)
                        Assert.IsFalse(s.Contains(w), $"{key}: 「{w}」(결정 42·43) — {s}");
                    foreach (var w in new[] { "피해", "당신", "타일", "슬롯" })
                        Assert.IsFalse(s.Contains(w), $"{key}: 「{w}」 — {s}");
                    Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(s, @"[^니]다\.(\s|$)"), $"{key}: 평서형(ui-text-guide §1-6) — {s}");
                    // 영어(M9 언어 ①): 전체 영문이 48자를 넘으면 trainer-short-en.json의 짧은 문구 — 그대로 두면 능력 칸에 문구가 뜨지 않는다(CardFx.BodyMax)
                    string sEn = en[key + ".short"];
                    Assert.IsFalse(string.IsNullOrWhiteSpace(sEn), key + " en");
                    Assert.LessOrEqual(sEn.Length, 48, $"{key} en: {sEn}");
                    Assert.IsFalse(HasHangul(sEn), key + " en");
                    if (a.En.Length > 48) Assert.AreNotEqual(a.En, sEn, key + " en"); else Assert.AreEqual(a.En, sEn, key + " en");
                    if (sEn != a.En) ownEn++;
                    n++;
                }
            Assert.AreEqual(102, n);
            Assert.AreEqual(68, own, "따로 쓴 짧은 문구(나머지는 전체 문구가 48자 이하라 그대로 — 결정 163 새 번역 기준)");
            Assert.AreEqual(101, ownEn, "따로 쓴 영어 짧은 문구(Competitive Edge 하나만 전체 영문이 48자 이하)");
        }

        [Test]
        public void M6_트레이너카드문구는화면말을쓴다_결정87()
        {
            // 사용자 결정 87 W1·W3·W6·W7·W8·W9: 「당신」 · 카드 말(타일 · 슬롯 · 캠핑장 · 이동점 · 지불 · 장비) · TTS 조작 설명이 없다
            foreach (var t in db.TrainerCards)
                foreach (var a in t.Abilities)
                {
                    string key = "trainer_ability." + t.Key + "." + a.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".text";
                    foreach (var w in new[] { "당신", "타일", "슬롯", "캠핑장", "이동점", "지불", "장비", "장착", "검색", "펜으로" })
                        Assert.IsFalse(ko[key].Contains(w), $"{key}: 「{w}」 — {ko[key]}");
                }
            Assert.AreEqual("한 턴에 한 번, 2칸 안의 칸에서 할 일을 그 칸에 가지 않고 할 수 있습니다.", ko["trainer_ability.psychic.1.text"], "W1 ①");
            StringAssert.StartsWith("한 턴에 한 번, 바다 칸에 있을 때 버린 숨겨진 장소 더미를 섞어 1장을 뽑아 내 칸에 놓습니다.", ko["trainer_ability.scuba_diver.1.text"], "W1 ②");
            Assert.AreEqual("풀 타입 포켓몬과 바위 타입 포켓몬은 스피드 +3. 하지만 최대 체력 −2.", ko["trainer_ability.gardener.2.text"], "W7");
            Assert.AreEqual("바위 타입 포켓몬과 땅 타입 포켓몬은 체력 +4. 하지만 스피드 −1.", ko["trainer_ability.hiker.2.text"], "W7");
            StringAssert.Contains("눈금만큼 한 공격의 데미지를 더하고 0으로 되돌릴 수 있습니다.", ko["trainer_ability.guitarist.1.text"], "W8");
            StringAssert.EndsWith("장작을 3개 추가합니다.", ko["trainer_ability.kindler.1.text"], "W6");
            // W10 ①: 이 게임만의 기술 이름은 띄어 쓴다(공식 기술명은 붙여 쓴 그대로)
            Assert.AreEqual("시간의 수호", ko["move.temporal_guard"]);
            Assert.AreEqual("타오르는 용맹", ko["move.blazing_valor"]);
            Assert.AreEqual("멸망의노래", ko["move.perish_song"], "공식 이름은 그대로");
        }

        [Test]
        public void M4_기술이름은현행공식한국어_결정44()
        {
            // Tools/data/reports/move-names-ko.tsv(빌드가 씀): 기술마다 한 줄, ko = strings.ko
            string tsv = File.ReadAllText(Path.Combine(TestData.RepoRoot(), "Tools/data/reports/move-names-ko.tsv"), new UTF8Encoding(false));
            var lines = tsv.TrimEnd('\n').Split('\n');
            Assert.AreEqual("key\ten\tid\tko_3ja2\tko\tdiff\tsrc\tnote", lines[0]);
            var bySrc = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var keys = new HashSet<string>();
            int romDiffers = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var f = lines[i].Split('\t');
                Assert.AreEqual(8, f.Length, lines[i]);
                Assert.IsTrue(keys.Add(f[0]), "겹친 키 " + f[0]);
                Assert.AreEqual(ko[f[0]], f[4], f[0] + ": 보고서 ko = strings.ko");
                Assert.AreEqual(f[3] == f[4] ? "same" : "changed", f[5], f[0]);
                string src = f[6].StartsWith("pokeapi:", StringComparison.Ordinal) ? "pokeapi" : f[6];
                bySrc[src] = bySrc.TryGetValue(src, out int n) ? n + 1 : 1;
                if (f[7].Contains("원작 자산(")) romDiffers++;
            }
            Assert.AreEqual(db.Moves.Count, keys.Count, "기술마다 한 줄");
            foreach (var m in db.Moves) Assert.IsTrue(keys.Contains("move." + m.Key), m.Key);
            CollectionAssert.AreEquivalent(new[] { "ours", "pokeapi" }, bySrc.Keys, "원천 = 현행 공식(PokeAPI) · 영문에서 옮긴 이름(이 게임만 — 결정 163). 원작 자산은 대조용(note)");
            Assert.AreEqual(33, bySrc["ours"], "이 게임만의 기술");
            Assert.AreEqual(10, romDiffers, "원작 자산(Pt·B2W2) 이름과 현행 이름이 다른 기술 — note에 옛 이름");
            Assert.AreEqual("웨이브태클", ko["move.wave_crash"], "6세대 이후 = PokeAPI 한국어(3JA2 「웨이브 크러쉬」)");
            Assert.AreEqual("심판의뭉치", ko["move.judgement"], "원작 철자 Judgement = Judgment");
            // 결정 44 ②: 원작 자산 이름과 현행 공식 이름이 다르면 현행 이름(결정 43 ⑩을 바꿈). 괄호 = 원작 자산(Pt) 이름
            Assert.AreEqual("메가혼", ko["move.megahorn"], "(메가폰)");
            Assert.AreEqual("앙코르", ko["move.encore"], "(앵콜)");
            Assert.AreEqual("달빛", ko["move.moonlight"], "(달의불빛)");
            Assert.AreEqual("맹독엄니", ko["move.poison_fang"], "(독엄니)");
            Assert.AreEqual("벌레먹기", ko["move.bug_bite"], "(벌레먹음)");
            Assert.AreEqual("화염바퀴", ko["move.flame_wheel"], "(화염자동차)");
            Assert.AreEqual("에어커터", ko["move.air_cutter"], "(에어컷터)");
            Assert.AreEqual("본러시", ko["move.bone_rush"], "(본러쉬)");
            Assert.AreEqual("록블라스트", ko["move.rock_blast"], "(락블레스트)");
            Assert.AreEqual("메탈클로", ko["move.metal_claw"], "(메탈크로우)");
            Assert.AreEqual("배고픔휠", ko["move.hangry_wheel"], "이 게임만의 기술은 영문에서 옮긴 이름(결정 163)");
            // 3JA2 오역 고침(ui-text-audit §2-1): 「사용자의 다음 공격」 → 다음에 공격받을 때, 대상 타겟 → 사용자
            StringAssert.Contains("사용자가 다음에 공격받을 때", ko["move.close_combat.text"]);
            StringAssert.StartsWith("사용자를 졸음 상태로", ko["move.zen_restoration.text"]);
            StringAssert.Contains("다른 포켓몬", ko["move.surf.text"]);
            // 검토 반영: 같은 「each other」 틀의 노래 기술 3개, 졸음 활용형(「타겟은 잠듭니다」), 불비달마 폼 이름
            foreach (var song in new[] { "fire_song", "frost_song", "thunder_song" })
                StringAssert.Contains("다른 포켓몬", ko["move." + song + ".text"], song + ": each other");
            StringAssert.EndsWith("상대를 졸음 상태로 만듭니다.", ko["move.moonblast.text"], "결정 163: 상태 이상은 「상대를 ○○ 상태로 만듭니다」");
            StringAssert.EndsWith("상대를 졸음 상태로 만듭니다.", ko["move.night_daze.text"]);
            StringAssert.Contains("노말모드로 폼체인지", ko["move.zen_restoration.text"], "Blazing Form = pokemon.555:1 노말모드");
        }

        [Test]
        public void M2_직업과갤럭시단인물은원작자산공식이름()
        {
            Assert.AreEqual("곤충채집소년", ko["trainer_card.bug_catcher"], "카드 「곤충채집 소년」 → 원작 자산");
            Assert.AreEqual("수영팬티소년", ko["trainer_card.swimmer"], "카드 「수영팬티 소년」 → 원작 자산");
            Assert.AreEqual("엘리트 트레이너", ko["trainer_card.ace_trainer"]);
            Assert.AreEqual("백금 신도", ko["trainer_card.platinum_acolyte"], "원작 자산에 없는 직업은 영문에서 옮김(결정 163)");
            Assert.AreEqual("태홍", ko["galactic.cyrus"]);
            Assert.AreEqual("마스", ko["galactic.mars"]);
            Assert.AreEqual("주피터", ko["galactic.jupiter"]);
            Assert.AreEqual("새턴", ko["galactic.saturn"]);
            Assert.AreEqual("갤럭시단 조무래기", ko["galactic.female_grunt"]);
            foreach (GalacticTrainer g in Enum.GetValues(typeof(GalacticTrainer)))
            {
                if (g == GalacticTrainer.None) continue;
                var key = "galactic." + Snake(g.ToString());
                Assert.IsTrue(ko.Contains(key) && en.Contains(key), key);
            }
        }

        static string Snake(string pascal)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                if (char.IsUpper(pascal[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(pascal[i]));
            }
            return sb.ToString();
        }

        [Test]
        public void M2_문자열표로더왕복_같은바이트()
        {
            var utf8 = new UTF8Encoding(false);
            CollectionAssert.AreEqual(TestData.ReadBytes("strings.ko.json"), utf8.GetBytes(ko.Write()), "strings.ko.json");
            CollectionAssert.AreEqual(TestData.ReadBytes("strings.en.json"), utf8.GetBytes(en.Write()), "strings.en.json");
        }

        [Test]
        public void M2_문자열표로더는모르는필드_중복키_순서어긋남_빈값을거부()
        {
            const string ok = "{\"schema\":1,\"lang\":\"ko\",\"strings\":{\n\"a.x\":\"가\",\n\"b.y\":\"나\"\n}}\n";
            Assert.AreEqual(2, StringTable.Load(ok).Count);
            Assert.AreEqual(ok, StringTable.Load(ok).Write());
            foreach (var bad in new[]
            {
                "{\"schema\":1,\"lang\":\"ko\",\"strings\":{\"a.x\":\"가\"},\"extra\":1}",
                "{\"schema\":2,\"lang\":\"ko\",\"strings\":{\"a.x\":\"가\"}}",
                "{\"schema\":1,\"lang\":\"jp\",\"strings\":{\"a.x\":\"가\"}}",
                "{\"schema\":1,\"strings\":{\"a.x\":\"가\"}}",
                "{\"schema\":1,\"lang\":\"ko\",\"strings\":{\"a.x\":\"가\",\"a.x\":\"나\"}}",
                "{\"schema\":1,\"lang\":\"ko\",\"strings\":{\"b.y\":\"가\",\"a.x\":\"나\"}}",
                "{\"schema\":1,\"lang\":\"ko\",\"strings\":{\"a.x\":\"\"}}",
                "{\"schema\":1,\"lang\":\"ko\",\"strings\":{\"a.x\":1}}",
                "{\"schema\":1,\"lang\":\"ko\",\"strings\":{\"a x\":\"가\"}}",
            })
                Assert.Throws<FormatException>(() => StringTable.Load(bad), bad);
            Assert.Throws<KeyNotFoundException>(() => { var _ = ko["없는.키"]; });
        }

        [Test]
        public void A20_6_새조련사_피크닉걸_날기문구에공중날기와합쳐턴에한번_결정84()
        {
            // 사용자 결정 84 T11 ① 가 + 메모 「문구에 적어」: 규칙(공중날기와 합쳐 턴에 한 번 — rulings A36)은 그대로, 카드 능력 문구(마우스 오버)에 한 줄
            foreach (var key in new[] { "trainer_ability.bird_keeper.1.text", "trainer_ability.picnicker.1.text" })
                StringAssert.EndsWith("공중날기와 합쳐 한 턴에 한 번입니다.", ko[key], key);
            StringAssert.Contains("규토리 1개를 소모해", ko["trainer_ability.bird_keeper.1.text"], "영문 「by discarding 1 Apricorn」 — 자원은 소모(ui-text-guide §2-1)");
        }

        [Test]
        public void M9_배포문서줄은화면언어의파일을가리킨다_결정138()
        {
            // 결정 138 · 152: LICENSE · THIRD-PARTY-NOTICES · PRIVACY는 영어 *.md + 한국어 *.ko.md — 한국어 화면은 .ko.md, 영어 화면은 .md를 가리키고 그 파일이 뿌리에 있다(배포본에 BoardBuild.CopyNotices가 넣는다)
            string root = TestData.RepoRoot();
            foreach (var (lang, table) in new[] { ("ko", ko), ("en", en) })
                foreach (var (key, doc) in new[] { ("screen.credits.license", "LICENSE"), ("screen.credits.license", "THIRD-PARTY-NOTICES"), ("screen.credits.privacy", "PRIVACY"), ("screen.privacy.tip", "PRIVACY") })
                {
                    string want = lang == "ko" ? doc + ".ko.md" : doc + ".md", other = lang == "ko" ? doc + ".md" : doc + ".ko.md";
                    StringAssert.Contains(want, table[key], $"{lang} {key}");
                    StringAssert.DoesNotContain(other, table[key].Replace(want, ""), $"{lang} {key}: 다른 언어 파일을 가리키지 않는다");
                    Assert.That(File.Exists(Path.Combine(root, want)), want + " — 프로젝트 뿌리에 있다");
                }
        }
    }
}
