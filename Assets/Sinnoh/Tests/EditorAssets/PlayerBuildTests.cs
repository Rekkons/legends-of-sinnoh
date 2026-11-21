using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Editor;
using UnityEditor;

namespace Sinnoh.Tests.EditorAssets
{
    /// <summary>
    /// 보드 플레이어 빌드(M4-2e, d3-3 §2-4·§4-2): Gen 번들 목록이 보드 화면이 읽는 키를 모두 담고, 프레임 시간 요약이 문서의 백분위 정의대로다.
    /// 번들을 실제로 만들지는 않는다(빌드는 Sinnoh.Editor.BoardBuild 배치). Gen/이 없으면 건너뛴다.
    /// </summary>
    public class PlayerBuildTests
    {
        const string Gen = "Assets/Sinnoh/Gen/";

        [Test]
        public void M4_플레이어번들목록은보드Gen키를모두담는다()
        {
            foreach (var t in new[] { "Board/board.json", "Trainers/trainers.json", "Figures/figures.json" })
                if (!File.Exists(Gen + t)) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            var plan = BoardBuild.Plan();
            var where = new Dictionary<string, string>();
            foreach (var b in plan)
            {
                Assert.AreEqual(b.assetNames.Length, b.addressableNames.Length, b.assetBundleName);
                for (int i = 0; i < b.assetNames.Length; i++)
                {
                    if (b.assetBundleName == GenAssets.FigureShadersBundle) continue;
                    Assert.AreEqual(Gen + b.addressableNames[i], b.assetNames[i], "주소 이름 = Gen 키");
                    Assert.IsFalse(where.ContainsKey(b.addressableNames[i]), "키가 두 번들에 " + b.addressableNames[i]);
                    where[b.addressableNames[i]] = b.assetBundleName;
                }
            }

            // 보드 화면이 읽는 키(BoardView 디퓨즈·FixtureIcons 표와 그림·BoardFigures 표와 프리팹·BoardPieces 표와 앞모습)
            // + PC 층 겉모양(UiSkin 글꼴·창틀 둘 — 타이틀부터 플레이어가 읽는다, plan §4 「화면 뿌리·타이틀」)
            // + 셋업 화면(S12·S13)이 읽는 그림(plan §4 「솔로 설정·셋업」): 포켓몬 정지 도트 아틀라스, 마을 카드 그림 9, 체력·스피드 아이콘
            var want = new List<string>
            {
                FixtureIcons.TableKey, BoardFigures.TableKey, BoardPieces.TableKey, UiSkin.FontKey, UiSkin.PanelFrameKey, UiSkin.TipFrameKey,
                MonArt.TableKey, "Sprites/Static/static_0.png", KitGallery.HealthIconKey, KitGallery.SpeedIconKey,
            };
            // + 배틀 화면의 DS 층(plan §4 「배틀 화면 뼈대」): 지형 짝 표와 그 표의 무대 그림 전부, 체력 상자·HP 숫자·메시지 창, 원작 자산 글꼴 둘(표 + 아틀라스), 상태·타입 그림
            want.AddRange(new[]
            {
                DsStage.StageTableKey, DsStage.EnemyBoxKey, DsStage.PlayerBoxKey, DsStage.DigitsKey, DsStage.MessageBoxKey,
                DsStage.TokenKey, DsStage.TokenBackKey,   // 우선권 토큰 앞면·뒷면(결정 73 ⑦)
                EvolveScreen.BgKey,   // 진화 화면 배경(M9 다듬기 P10)
                FlyCutin.BandKey,     // 공중날기 컷인 띠(결정 167)
                DsFont.TableKey(DsStage.HudFont), DsFont.AtlasKey(DsStage.HudFont), DsFont.TableKey(DsStage.MessageFont), DsFont.AtlasKey(DsStage.MessageFont),
            });
            foreach (Sinnoh.Rules.StatusEffect status in System.Enum.GetValues(typeof(Sinnoh.Rules.StatusEffect)))
                if (status != Sinnoh.Rules.StatusEffect.None) want.Add(DsStage.StatusKey(status));
            foreach (Sinnoh.Rules.PokemonType type in System.Enum.GetValues(typeof(Sinnoh.Rules.PokemonType))) want.Add("Stage/Icons/type_" + type + ".png");
            want.AddRange(IconArt.All(IconArt.English));   // 영문 아이콘(결정 115 — 영문판 Pt 타입 19 · 상태 6)
            foreach (var t in JObject.Parse(File.ReadAllText(Gen + DsStage.StageTableKey))["terrain"])
                foreach (string part in new[] { "bg", "enemy", "player" }) want.Add("Stage/" + (string)t[part]);
            // 시간대 그림(결정 70 ⑥): 저녁·밤 무대 그림도 stage 번들에 있다
            foreach (var day in JObject.Parse(File.ReadAllText(Gen + DsStage.StageTableKey))["timed"])
                foreach (var time in new[] { Sinnoh.Fx.DayTime.Evening, Sinnoh.Fx.DayTime.Night }) want.Add("Stage/" + Sinnoh.Fx.DayTimes.Art((string)day, time, true));
            foreach (var f in JObject.Parse(File.ReadAllText("Assets/Sinnoh/Data/Json/fixtures.json"))["rows"])
                if ((string)f["kind"] == "Village") want.Add(SetupVillageScreen.ArtKey((string)f["key"]));
            var board = JObject.Parse(File.ReadAllText(Gen + "Board/board.json"));
            want.AddRange(board["hex"].Select(h => "Board/" + (string)h["file"]));
            want.AddRange(board["rows"].Select(r => "Board/" + (string)r["front"]).Distinct());
            want.AddRange(JObject.Parse(File.ReadAllText(Gen + "Trainers/trainers.json"))["fronts"].Select(r => "Trainers/" + (string)r["file"]));
            // 포획 연출(68행): 주인공 뒷모습 던지기 시트(표의 sequence가 있는 행 8) · 볼 넷(도는 프레임 + 열린 볼) · 갤럭시단 조무래기 앞모습
            var throws = Sinnoh.Fx.BackThrow.ParseAll(File.ReadAllText(Gen + "Trainers/trainers.json"));
            Assert.AreEqual(8, throws.Count, "볼 던지기 순서가 있는 주인공 8");
            want.AddRange(throws.Select(t => "Trainers/" + t.File));
            for (int ball = 0; ball < System.Enum.GetValues(typeof(Sinnoh.Rules.Ball)).Length; ball++)
            {
                want.Add(DsStage.BallFolder + Sinnoh.Fx.BattleFeed.BallItem(ball) + ".png");
                want.Add(DsStage.BallFolder + Sinnoh.Fx.BattleFeed.BallItem(ball) + "_open.png");
            }
            want.Add(Sinnoh.Fx.FoeArt.GruntMale);
            want.Add(Sinnoh.Fx.FoeArt.GruntFemale);
            want.AddRange(new[] { Sinnoh.Fx.FoeArt.Mars, Sinnoh.Fx.FoeArt.Jupiter, Sinnoh.Fx.FoeArt.Saturn, Sinnoh.Fx.FoeArt.Cyrus });   // 간부·태홍 앞모습(결정 80 L4 나)
            // 주인공 도트 말·주인공 고르기(04 §3-1): 필드 도트 표 + 주인공 8의 시트(행의 key = 주인공 키)
            want.Add(BoardPieces.FieldTableKey);
            var fields = JObject.Parse(File.ReadAllText(Gen + BoardPieces.FieldTableKey))["rows"];
            CollectionAssert.AreEquivalent(Sinnoh.Rules.Session.Protagonists.Keys.Concat(Sinnoh.Rules.Session.Protagonists.Guests), fields.Select(r => (string)r["key"]), "필드 도트 표의 주인공 = 고를 수 있는 주인공 8 + 손님 인물(결정 96 Q5 나)");
            want.AddRange(fields.Select(r => "Field/" + (string)r["file"]));
            // 얼굴 상자(결정 54 ④·61 ③): 주인공 앞모습 8(표의 heroes 행 — key = 주인공 키)
            var heroes = JObject.Parse(File.ReadAllText(Gen + "Trainers/trainers.json"))["heroes"];
            CollectionAssert.AreEquivalent(Sinnoh.Rules.Session.Protagonists.Keys.Concat(Sinnoh.Rules.Session.Protagonists.Guests), heroes.Select(r => (string)r["key"]), "얼굴 상자 그림의 주인공 = 고를 수 있는 주인공 8 + 손님 인물(결정 96 Q5 나)");
            want.AddRange(heroes.Select(r => "Trainers/" + (string)r["file"]));
            foreach (string key in Sinnoh.Rules.Session.Protagonists.Keys.Concat(Sinnoh.Rules.Session.Protagonists.Guests)) Assert.That(want, Does.Contain(FaceBox.ArtKey(key)), key + " 얼굴 상자가 읽는 키");
            var figures = JObject.Parse(File.ReadAllText(Gen + "Figures/figures.json"))["rows"].Select(r => "Figures/" + (string)r["prefab"]).ToList();
            want.AddRange(figures);
            // 소리(결정 66 ④): 상황 곡 표 + 그 표의 곡 · 효과음 표 + 키마다 클립
            want.Add(SoundPlayer.MusicTableKey);
            want.Add(SoundPlayer.SeTableKey);
            var music = Sinnoh.Fx.MusicBook.Parse(File.ReadAllText(Gen + SoundPlayer.MusicTableKey));
            var songs = System.Enum.GetValues(typeof(Sinnoh.Fx.MusicScene)).Cast<Sinnoh.Fx.MusicScene>().Select(music.File).Where(f => f != null).Select(SoundPlayer.MusicKey).Distinct().ToList();
            want.AddRange(songs);
            // 앞부분 클립(M9 소리 ② — 그 곡의 번들에 함께)
            var intros = System.Enum.GetValues(typeof(Sinnoh.Fx.MusicScene)).Cast<Sinnoh.Fx.MusicScene>().Select(music.Intro).Where(f => f != null).Select(SoundPlayer.MusicKey).Distinct().ToList();
            want.AddRange(intros);
            var effects = Sinnoh.Fx.SoundBook.Parse(File.ReadAllText(Gen + SoundPlayer.SeTableKey));
            foreach (string key in Sinnoh.Fx.SoundKeys.All) want.Add(SoundPlayer.EffectKey(effects.Find(key).File));
            // 튜토리얼 안내 마박사(M6.5, 결정 90 Q2 가): 소개 전신 도트 + 목차 얼굴
            want.Add(GenAssets.RowanFull);
            want.Add(GenAssets.RowanFace);
            // 설명서 그림(M9 「설명서 그림」, 결정 35): 표 + 표가 가리키는 그림(그림 속 글이 있으면 언어마다)
            Assert.That(File.Exists(Gen + GenAssets.RulebookTable), "설명서 그림 없음: node Tools/assets/build.mjs --only rulebook");
            var rulebook = JObject.Parse(File.ReadAllText(Gen + GenAssets.RulebookTable))["rows"];
            var figureFiles = rulebook.SelectMany(r => new[] { (string)r["ko"], (string)r["en"] }).Distinct().ToList();
            want.Add(GenAssets.RulebookTable);
            want.AddRange(figureFiles);
            var bad = new List<string>();
            foreach (var k in want.Distinct())
            {
                if (!where.TryGetValue(k, out var b)) bad.Add("목록에 없음 " + k);
                else if (b != GenAssets.BundleFor(k)) bad.Add($"{k}: 번들 {b} ≠ BundleFor {GenAssets.BundleFor(k)}");
                if (AssetDatabase.LoadMainAssetAtPath(Gen + k) == null) bad.Add("에셋 없음 " + k);
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));

            // 묶음: board(표 + 디퓨즈 5 + 아이콘), trainers(표 + 앞모습 51), figures(표), 피규어 하나 = 번들 하나(프리팹 하나), 셰이더 공용
            var byName = plan.ToDictionary(b => b.assetBundleName, b => b);
            Assert.AreEqual(1 + 5 + Directory.GetFiles(Gen + "Board/Icons", "*.png").Length, byName["board"].assetNames.Length, "board");
            int backs = Directory.GetFiles(Gen + "Trainers/Back/official", "*.png").Length;
            Assert.AreEqual(17, backs, "공식 뒷모습 17(주인공 8의 볼 던지기 시트 포함)");
            Assert.AreEqual(1 + 51 + 9 + backs + 1 + 9, byName["trainers"].assetNames.Length, "trainers = 표 + 앞모습 51 + 주인공 앞모습 8 + 손님 인물 1(결정 96 Q5 나 — 용식) + 공식 뒷모습 + 필드 도트 표 + 시트 9");
            Assert.AreEqual(1, byName["figures"].assetNames.Length, "figures");
            foreach (var f in figures) Assert.AreEqual(1, byName[GenAssets.BundleFor(f)].assetNames.Length, f);
            Assert.That(byName.ContainsKey(GenAssets.FigureShadersBundle), "셰이더 공용 번들");
            Assert.AreEqual(3, byName["ui"].assetNames.Length, "ui = 글꼴 + 창틀 둘");
            Assert.AreEqual(3, byName["sprites"].assetNames.Length, "sprites = 정지 도트 표 + 아틀라스 + 팬 폼 효과 표(M5 「맵 결과·배틀 안 연출」)");
            Assert.AreEqual(9 + 2 + 9 + 4 + 7 + 2 + 5 + 7 + 4 + 3 + 12 + 2 + 11 + 9 + 1 + 11 + 6 + 13 + 2 + 2, byName["cards"].assetNames.Length, "(+ 2 = 여행 스탬프 시트 · 도장 — M9 다듬기 P12) (+ 2 = 금색 ? 뒷면 · 울트라 온슬롯 카드 그림 — 결정 87 V8·V24 나) (+ 13 = 트레이너 카드 틀 — 등급별 바탕·아래 띠·뒷면 9 · 자물쇠 2 · 등급 아이콘 2, M6 해금 연출 · + 11 + 6 = 전용 아이템 아이콘 · 균열 카드 그림 — M6 그림 에셋 · + 2 = 신오의 심장 · 수호신의 서 그림 — M5 마감 검토 · + 11 + 9 + 1 = 유물 아이콘 · 사당 카드 그림(전설 8 + 축복 1) · 종막 문양 — 결정 80 L14·L12·L1 나) cards = 마을 카드 그림 9 + 체력·스피드 아이콘 + 조우 등급 그림 9 + 볼 4 + 규토리 7 + 트레이너 카드 세로 뒷면 부품 2 + 운명 카드 5(앞면 넷 · 뒷면) + 지닌물건 7 + 스튜 4(제작 창 S26) + 추종자 아이콘 3(사당 판 S33) + 유물·우두머리 토큰 12(숨겨진 장소 결과 26~30행)");
            foreach (string god in new[] { "dialga", "palkia", "giratina" }) Assert.AreEqual("cards", where[GenAssets.GodIcons + god + ".png"], god);
            foreach (string token in GenAssets.SpotTokens) Assert.AreEqual("cards", where[GenAssets.ItemTokens + token + ".png"], token);
            foreach (string fate in new[] { "dialga", "palkia", "giratina", "arceus", "back" }) Assert.AreEqual("cards", where[GenAssets.FateArt + fate + ".png"], fate);
            foreach (var k in new[] { Sinnoh.Client.SetupPickScreen.BackFrameKey, Sinnoh.Client.SetupPickScreen.BackEmblemKey }) Assert.AreEqual("cards", where[k], k);
            foreach (string part in new[] { "bronze_base", "silver_base", "gold_base", "bronze_bar", "silver_bar", "gold_bar", "locked_blue", "locked_red", "icon_moderate", "icon_strong" })
                Assert.AreEqual("cards", where[GenAssets.TrainerFrame + part + ".png"], part);   // 해금 연출의 카드 틀(M6 — CardFx)
            foreach (string ball in new[] { "poke_ball", "greater_ball", "ultimate_ball", "keystone_ball" })
                Assert.AreEqual("cards", where[GenAssets.ItemIcons + ball + ".png"], ball);
            foreach (string icon in new[] { GenAssets.HeartIcon, GenAssets.ScrollIcon }) Assert.AreEqual("cards", where[GenAssets.ItemIcons + icon], icon);   // HUD 심장 회복(94행) · 거래 창 「수호신의 서」 칩
            foreach (string token in GenAssets.SpotTokens)   // 유물 아이콘 열하나(결정 80 L14 나 — 우두머리는 아이콘이 없다)
            {
                string icon = GenAssets.RelicIconKey("item." + token);
                if (token == "alpha_pokemon") Assert.IsNull(icon, token);
                else Assert.AreEqual("cards", where[icon], token);
            }
            Assert.AreEqual(GenAssets.ItemIcons + "adamant_orb.png", GenAssets.RelicIconKey("item.adamant_artifact"));
            Assert.AreEqual(GenAssets.ItemIcons + "secret_key.png", GenAssets.RelicIconKey("item.secret_key"));
            foreach (string card in new[] { "song_of_frost", "song_of_thunder", "song_of_fire", "visitor_from_afar", "shadow_emerges", "beacon_of_light", "giant_awakens", "celestial_fracture", "blessing" })
                Assert.AreEqual("cards", where[GenAssets.ShrineArt + card + ".png"], card);   // 사당 카드 그림(결정 80 L12 나)
            Assert.AreEqual("cards", where[GenAssets.EndingArt], "종막 문양(결정 80 L1 나)");
            foreach (var item in Sinnoh.Data.ItemDbLoader.Load(File.ReadAllText("Assets/Sinnoh/Data/Json/items.json")).Items)   // 전용 아이템 아이콘 = items.json TrainerItem 열하나(rulings G1 — M6 그림 에셋, 화면은 WorkUi.IconKey로 찾는다)
                if (item.Kind == Sinnoh.Rules.ItemKind.TrainerItem) Assert.AreEqual("cards", where[Sinnoh.Client.WorkUi.IconKey(item)], item.Key);
            foreach (string card in new[] { "reckless_assault", "rapid_strikes", "gravitational_drag", "nimble_frame", "chromatic_union", "status_overload" })
                Assert.AreEqual("cards", where[GenAssets.BreachArt + card + ".png"], card);   // 왜곡 균열 카드 그림(M6 그림 에셋)
            Assert.AreEqual("cards", where[GenAssets.SpecialSpotBack], "금색 ? 뒷면(결정 87 V8 나)");
            Assert.AreEqual("cards", where[GenAssets.OnslaughtArt], "울트라 온슬롯 카드 그림(결정 87 V24 나)");
            Assert.AreEqual("cards", where[GenAssets.StampSheet], "여행 스탬프 시트(M9 다듬기 P12)");
            Assert.AreEqual("cards", where[GenAssets.StampSeal], "여행 스탬프 도장");
            Assert.IsNull(GenAssets.BundleFor(GenAssets.ItemTokens + "hidden_spot_back.png"), "보통 뒷면은 board 번들의 것을 쓴다");
            Assert.IsNull(GenAssets.BundleFor(GenAssets.ItemIcons + "artifact_omni_core.png"), "v1.4a에 없는 아이템 그림은 번들에 없다");
            int stageFiles = Directory.GetFiles(Gen + "Stage", "*", SearchOption.AllDirectories).Count(f => !f.EndsWith(".meta")) - 2;   // ui 번들의 창틀 둘은 뺀다
            int romFonts = Directory.GetFiles(Gen + "Fonts", "pt_*").Count(f => !f.EndsWith(".meta"));
            Assert.AreEqual(stageFiles + romFonts, byName["stage"].assetNames.Length, "stage = Gen/Stage 전부(창틀 둘 빼고) + 원작 자산 글꼴 pt_*");
            Assert.AreEqual("ui", GenAssets.BundleFor(UiSkin.PanelFrameKey), "PC 층 창틀은 ui 번들 그대로(Stage/ 아래지만 stage가 아니다)");
            Assert.AreEqual(1 + 1 + Sinnoh.Fx.SoundKeys.All.Length, byName["audio"].assetNames.Length, "audio = 상황 곡 표 + 효과음 표 + 클립(곡은 곡마다 번들 — M7 웹 손님 나눠 받기, 03 §5)");
            foreach (var song in songs)
            {
                StringAssert.StartsWith(GenAssets.MusicBundlePrefix, GenAssets.BundleFor(song), song);
                Assert.AreEqual(1 + intros.Count(i => GenAssets.BundleFor(i) == GenAssets.BundleFor(song)), byName[GenAssets.BundleFor(song)].assetNames.Length, song + " — 곡 하나 = 번들 하나(반복 클립 + 앞부분 클립)");
                Assert.That(GenAssets.Split(GenAssets.BundleFor(song)), song + " — 나눠 받기");
            }
            Assert.That(GenAssets.Split(GenAssets.BundleFor(figures[0])) && GenAssets.Split(GenAssets.FigureShadersBundle) && !GenAssets.Split("audio") && !GenAssets.Split("board"), "웹 첫 받기 = 피규어 · 곡 밖 번들");
            foreach (var intro in intros) Assert.That(songs.Select(GenAssets.BundleFor), Does.Contain(GenAssets.BundleFor(intro)), intro + " — 앞부분 클립은 그 곡의 번들");
            Assert.That(intros.Count, Is.GreaterThan(0).And.LessThanOrEqualTo(songs.Count), "앞부분 클립(M9 소리 ②)");
            // 울음(M9 소리 ③): 표 + 종 322 = 번들 cries 하나 · 웹은 나눠 받기
            Assert.AreEqual(GenAssets.CryBundle, GenAssets.BundleFor(SoundPlayer.CryTableKey));
            Assert.AreEqual(1 + 322 + 3, byName[GenAssets.CryBundle].assetNames.Length, "cries = 울음 표 + pokemon.json의 종 322 + 폼 울음 3(네크로즈마 — 결정 143 ④)");
            Assert.That(GenAssets.Split(GenAssets.CryBundle), "울음은 나눠 받기(웹 첫 받기 문턱 — 03 §5)");
            Assert.AreEqual(2, byName["tutorial"].assetNames.Length, "tutorial = 마박사 전신 도트 + 목차 얼굴(M6.5)");
            Assert.AreEqual(71, rulebook.Count(), "설명서 그림 자리 71(M9 「설명서 글 ①②」 66 − 상태 아이콘 띠 1 + 상태 표 줄 머리 아이콘 6 — 결정 143 ③)");
            Assert.AreEqual(1 + figureFiles.Count, byName[GenAssets.RulebookBundle].assetNames.Length, "rulebook = 표 + 그림(한 벌 + 언어마다)");
            Assert.That(GenAssets.Split(GenAssets.RulebookBundle), "설명서 그림은 나눠 받기(웹 첫 받기 40 MB 밖)");
            Assert.AreEqual(11 + figures.Count + 1 + songs.Count, plan.Count, "번들 수 = board·trainers·figures·ui·sprites·cards·stage·audio·tutorial·rulebook·cries + 피규어 + fig_shaders + 곡");
            Assert.IsNull(GenAssets.BundleFor("Sprites/Anim/front.png"), "화면이 아직 읽지 않는 키는 번들에 넣지 않는다(그 화면을 만들 때·M7)");
        }

        [Test]
        public void M9_배포문서셋을출력폴더에복사한다()
        {
            // plan §5 #534: LICENSE.md · THIRD-PARTY-NOTICES.md · CREDITS.md + 결정 126 ④ PRIVACY.md, 결정 138: LICENSE · PRIVACY의 한국어 *.ko.md + CREDITS.ko.md, 결정 152: THIRD-PARTY-NOTICES.ko.md — 실제 Builds/는 건드리지 않는다(임시 폴더)
            CollectionAssert.AreEqual(new[] { "LICENSE.md", "LICENSE.ko.md", "THIRD-PARTY-NOTICES.md", "THIRD-PARTY-NOTICES.ko.md", "CREDITS.md", "CREDITS.ko.md", "PRIVACY.md", "PRIVACY.ko.md" }, BoardBuild.NoticeFiles);
            foreach (string name in BoardBuild.NoticeFiles) Assert.That(File.Exists(name), name + " — 프로젝트 뿌리에 있다(빌드가 복사할 원본)");
            string tmp = Path.Combine(Path.GetTempPath(), "sinnoh-notices-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                string src = Path.Combine(tmp, "src"), dst = Path.Combine(tmp, "out", "Win64");
                Directory.CreateDirectory(src);
                File.WriteAllText(Path.Combine(src, "LICENSE.md"), "a");
                File.WriteAllText(Path.Combine(src, "THIRD-PARTY-NOTICES.md"), "b");
                Assert.Throws<FileNotFoundException>(() => BoardBuild.CopyNotices(src, dst), "하나라도 없으면 실패");
                Assert.IsFalse(Directory.Exists(dst), "실패하면 아무것도 쓰지 않는다");
                File.WriteAllText(Path.Combine(src, "CREDITS.md"), "c");
                File.WriteAllText(Path.Combine(src, "PRIVACY.md"), "d");
                File.WriteAllText(Path.Combine(src, "LICENSE.ko.md"), "e");
                File.WriteAllText(Path.Combine(src, "PRIVACY.ko.md"), "f");
                File.WriteAllText(Path.Combine(src, "CREDITS.ko.md"), "g");
                File.WriteAllText(Path.Combine(src, "THIRD-PARTY-NOTICES.ko.md"), "h");
                BoardBuild.CopyNotices(src, dst);
                CollectionAssert.AreEquivalent(BoardBuild.NoticeFiles, Directory.GetFiles(dst).Select(Path.GetFileName));
                File.WriteAllText(Path.Combine(src, "LICENSE.md"), "a2");
                BoardBuild.CopyNotices(src, dst);   // 다시 빌드 — 덮어쓴다
                Assert.AreEqual("a2", File.ReadAllText(Path.Combine(dst, "LICENSE.md")));
                Assert.AreEqual("c", File.ReadAllText(Path.Combine(dst, "CREDITS.md")));
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }

        [Test]
        public void M4_프레임요약은가까운순위백분위()
        {
            // d3-3 §2-4: 순위 = ⌈p·n⌉(정렬 뒤). 1~100을 섞어 넣는다
            var v = new List<double>();
            for (int i = 0; i < 100; i++) v.Add((i * 37 % 100) + 1);
            var s = FrameProbe.Summarize(v);
            Assert.AreEqual(100, s.Count);
            Assert.AreEqual(50.0, s.Median);
            Assert.AreEqual(95.0, s.P95);
            Assert.AreEqual(99.0, s.P99);
            Assert.AreEqual(100.0, s.Max);
            Assert.AreEqual(50.5, s.Mean, 1e-9);
            var one = FrameProbe.Summarize(new[] { 7.25 });
            Assert.AreEqual(7.25, one.P95);
            Assert.AreEqual(7.25, one.Median);
            Assert.AreEqual(0, FrameProbe.Summarize(new double[0]).Count);
        }

        [Test]
        public void M4_프레임타이밍은지연을맞춰같은프레임끼리짝짓는다()
        {
            // M4-2 검토 반영: FrameTimingManager 표본은 몇 프레임 늦게 읽힌다(배치 플레이어 3). 표본을 읽은 프레임 번호로 짝지어야 max(cpu, dt)가 같은 프레임 값이다.
            // dt 12프레임, 표본은 예열 끝 2프레임(색인 −2·−1)부터 읽혔고 지연 3, 색인 7에서 읽은 표본 하나가 빠짐(같은 frameStartTimestamp)
            var dt = new List<double> { 2.0, 2.1, 2.2, 9.0, 2.4, 2.5, 2.6, 2.7, 2.8, 2.9, 3.0, 3.1 };
            var cpuAt = new List<int>();
            var cpu = new List<double>();
            for (int read = -2; read < dt.Count; read++)
            {
                if (read == 7) continue; // 빠진 표본(프레임 4의 타이밍)
                int frame = read - 3;
                cpuAt.Add(read);
                cpu.Add(frame >= 0 ? dt[frame] - 0.01 : 5.0); // 같은 프레임이면 dt보다 0.01 작다, 예열 프레임 값은 5
            }
            var at = FrameProbe.Align(dt, cpuAt, cpu, FrameProbe.MaxLag, out int lag, out double diff);
            Assert.AreEqual(3, lag, "지연 3프레임을 찾는다");
            Assert.AreEqual(0.01, diff, 1e-9, "같은 프레임끼리 |cpu − dt| 평균");
            Assert.AreEqual(dt.Count, at.Length);
            for (int j = 0; j < dt.Count; j++)
            {
                if (j == 4 || j >= dt.Count - 3) { Assert.AreEqual(-1, at[j], $"프레임 {j}: 짝 없음(빠진 표본·끝 3프레임은 아직 안 읽힘)"); continue; }
                Assert.AreEqual(dt[j] - 0.01, cpu[at[j]], 1e-12, $"프레임 {j}의 짝은 같은 프레임 표본");
            }
            var frames = FrameProbe.FrameTimes(dt, at, cpu, new double[cpu.Count], out string rule);
            Assert.AreEqual("max(cpuFrameTime,unscaledDeltaTime) same frame", rule);
            CollectionAssert.AreEqual(dt, frames, "짝 없는 프레임도 dt로 남고(끝쪽 dt를 버리지 않음), 예열 표본 5.0은 섞이지 않는다");
            // 같은 번호끼리 짝지으면(고치기 전) 예열 값 5.0과 프레임 3의 튐 9.0이 다른 프레임에 붙는다 — 이 시험이 그 차이를 본다
            Assert.AreEqual(9.0, frames.Max());
            Assert.AreEqual(1, frames.Count(f => f >= 5.0));
        }

        [Test]
        public void M4_모든품질의렌더배율은1()
        {
            // 말은 화면 공간 정수배로 그린다(d3-3 §2-3 ③). 렌더 배율이 1이 아니면 줄인 타깃에 그린 뒤 바이리니어로 늘려 정수배·최근접이 깨진다
            // — WebGL 기본 품질(Mobile_RPAsset)이 0.8이던 것을 1로(M4-2 검토 반영, 웹 프레임은 M7에서 잰다)
            var names = UnityEngine.QualitySettings.names;
            Assert.Greater(names.Length, 0);
            for (int i = 0; i < names.Length; i++)
            {
                var rp = UnityEngine.QualitySettings.GetRenderPipelineAssetAt(i) ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
                var urp = rp as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                Assert.That(urp, Is.Not.Null, names[i] + " 품질의 URP 자산");
                Assert.AreEqual(1f, urp.renderScale, names[i] + " 품질(" + urp.name + ") 렌더 배율");
            }
        }

        [Test]
        public void M4_창테두리를끌수없다_결정59()
        {
            // 결정 59(58을 거둠): 창 테두리를 끌어 크기를 바꾸지 않는다 — 크기는 설정 화면의 지정 해상도로만. 기본 실행 모드(전체 화면 창)는 그대로 [가정]
            Assert.IsFalse(PlayerSettings.resizableWindow, "ProjectSettings resizableWindow");
            Assert.AreEqual(UnityEngine.FullScreenMode.FullScreenWindow, PlayerSettings.fullScreenMode);
        }

        [Test]
        public void M10_게임아이콘은크기마다같은크기의그림_결정159()
        {
            // 프로젝트 설정에 들어 있어야 한다(BoardBuild.ApplyIcons — 빌드마다 다시 넣는다). 크기마다 그 크기의 Point · 무압축 그림(Unity가 흐리게 늘리지 않게)
            var def = PlayerSettings.GetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, IconKind.Any);
            Assert.AreEqual(1, def.Length, "기본 아이콘");
            Assert.AreEqual(BoardBuild.IconPath(256), AssetDatabase.GetAssetPath(def[0]), "기본 아이콘");
            var sizes = PlayerSettings.GetIconSizes(UnityEditor.Build.NamedBuildTarget.Standalone, IconKind.Any);
            var icons = PlayerSettings.GetIcons(UnityEditor.Build.NamedBuildTarget.Standalone, IconKind.Any);
            Assert.AreEqual(sizes.Length, icons.Length);
            for (int i = 0; i < sizes.Length; i++)
            {
                Assert.That(icons[i], Is.Not.Null, "Standalone " + sizes[i]);
                string path = AssetDatabase.GetAssetPath(icons[i]);
                Assert.AreEqual(BoardBuild.IconPath(sizes[i]), path);
                Assert.AreEqual(sizes[i], icons[i].width, path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.AreEqual(UnityEngine.FilterMode.Point, ti.filterMode, path);
                Assert.AreEqual(TextureImporterCompression.Uncompressed, ti.textureCompression, path);
                Assert.IsFalse(ti.mipmapEnabled, path);
            }
            Assert.That(File.Exists("Assets/WebGLTemplates/Sinnoh/favicon.ico"), "웹 손님판 탭 아이콘");
        }
    }
}
