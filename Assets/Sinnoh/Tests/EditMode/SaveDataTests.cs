using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M2 조건 2·3(M2-c): 세이브에서 뽑은 fixtures·decks·cards·items와 board 덮개(틴트·디퓨즈)를 로더로 읽어 03 §1-4 숫자를 다시 센다
    /// (build.mjs lib/save.mjs와 같은 기대값). 참조 무결성·로더 왕복·엄격한 로더.
    /// </summary>
    public class SaveDataTests
    {
        Board board;
        IReadOnlyList<CellLook> looks;
        CardDb db;
        ItemDb items;
        DeckDb decks;
        FixtureDb fixtures;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            board = BoardLoader.Load(TestData.ReadText("board.json"), out looks);
            db = TestData.LoadCardDb();
            items = TestData.LoadItems();
            decks = TestData.LoadDecks(db);
            fixtures = TestData.LoadFixtures(board, db, items);
        }

        static int Count<T>(IEnumerable<T> rows, Func<T, bool> p)
        {
            int n = 0;
            foreach (var r in rows)
                if (p(r)) n++;
            return n;
        }

        static int Sum<T>(IEnumerable<T> rows, Func<T, int> f)
        {
            int n = 0;
            foreach (var r in rows) n += f(r);
            return n;
        }

        FixtureDef Fixture(string key) => fixtures.Fixtures[fixtures.Ids.IndexOf(key)];
        DeckDef Deck(string key) => decks.Decks[decks.DeckIds.IndexOf(key)];

        // ── 고정물 ──

        [Test]
        public void M2_고정물174_마을9_나무산호118_왜곡나무6_전설장소12_전설사당7_사당13_게이트8_택시1()
        {
            Assert.AreEqual(174, fixtures.Fixtures.Count);
            var want = new (FixtureKind kind, int n)[]
            {
                (FixtureKind.Village, 9), (FixtureKind.ApricornTree, 86), (FixtureKind.ApricornReef, 32), (FixtureKind.DistortedTree, 6),
                (FixtureKind.LegendarySite, 12), (FixtureKind.LegendaryShrine, 7), (FixtureKind.Shrine, 13), (FixtureKind.Gate, 8), (FixtureKind.Taxi, 1),
            };
            foreach (var (kind, n) in want) Assert.AreEqual(n, Count(fixtures.Fixtures, f => f.Kind == kind), kind.ToString());
            // 나무·산호 118 = 약함 62·보통 44·강함 12(inventory §1.1)
            bool Tree(FixtureDef f) => f.Kind == FixtureKind.ApricornTree || f.Kind == FixtureKind.ApricornReef;
            Assert.AreEqual(62, Count(fixtures.Fixtures, f => Tree(f) && f.Tier == EncounterTier.Weak));
            Assert.AreEqual(44, Count(fixtures.Fixtures, f => Tree(f) && f.Tier == EncounterTier.Moderate));
            Assert.AreEqual(12, Count(fixtures.Fixtures, f => Tree(f) && f.Tier == EncounterTier.Strong));
        }

        [Test]
        public void M2_고정물은칸에붙고_깨어진세계는울트라메가로폴리스_왜곡나무_게이트반쪽만()
        {
            foreach (var f in fixtures.Fixtures)
            {
                var cell = board[f.Cell];
                bool dw = f.Kind == FixtureKind.DistortedTree || f.Key == "village.ultra_megalopolis" || f.Key.EndsWith("_distortion", StringComparison.Ordinal);
                Assert.AreEqual(dw, cell.DistortionWorld, f.Key);
                if (f.Kind == FixtureKind.ApricornReef) Assert.AreEqual(Biome.Ocean, cell.Biome, f.Key + ": 산호는 바다");
                if (f.Kind == FixtureKind.ApricornTree) Assert.AreNotEqual(Biome.Ocean, cell.Biome, f.Key + ": 나무는 바다 밖");
            }
            Assert.AreEqual(11, Count(fixtures.Fixtures, f => board[f.Cell].DistortionWorld));
            // 칸 하나에 둘 있는 곳은 장막시티와 공중날기 찌르호크(택시)뿐
            var seen = new Dictionary<int, string>();
            var shared = new List<string>();
            foreach (var f in fixtures.Fixtures)
            {
                if (seen.TryGetValue(f.Cell, out var other)) shared.Add(other + "+" + f.Key);
                else seen.Add(f.Cell, f.Key);
            }
            CollectionAssert.AreEqual(new[] { "village.nesburgh+taxi.bird" }, shared);
        }

        [Test]
        public void M2_게이트4쌍_짝은같은이름의본맵과깨어진세계()
        {
            int pairs = 0;
            foreach (var f in fixtures.Fixtures)
            {
                if (f.Kind != FixtureKind.Gate) continue;
                var p = fixtures.Fixtures[f.Pair];
                Assert.AreEqual(f.Key.Substring(0, f.Key.LastIndexOf('_')), p.Key.Substring(0, p.Key.LastIndexOf('_')), f.Key);
                Assert.AreNotEqual(board[f.Cell].DistortionWorld, board[p.Cell].DistortionWorld, f.Key);
                if (!board[f.Cell].DistortionWorld) pairs++;
            }
            Assert.AreEqual(4, pairs);
            Assert.AreEqual("gate.lilly_distortion", fixtures.Ids[Fixture("gate.lilly_main").Pair]);
        }

        [Test]
        public void M2_참조무결성_고정물의전설은전설카드_유물은유물아이템()
        {
            foreach (var f in fixtures.Fixtures)
            {
                foreach (int p in f.Legendary)
                {
                    var def = db.Pokemon[p];
                    Assert.AreEqual(EncounterTier.Legendary, def.Tier, f.Key);
                    Assert.AreEqual(GalacticTrainer.None, def.Trainer, f.Key);
                    Assert.LessOrEqual(def.State, 1, f.Key + ": 전설 덱 카드의 첫 면");
                }
                foreach (int it in f.Artifact) Assert.AreEqual(ItemKind.Artifact, items.Items[it].Kind, f.Key);
            }
            string Keys(IReadOnlyList<int> ids, IdTable t)
            {
                var s = new List<string>();
                foreach (int i in ids) s.Add(t[i]);
                return string.Join(" ", s);
            }
            var garden = Fixture("legendary_site.garden_of_gratitude");
            Assert.AreEqual("492:1", Keys(garden.Legendary, db.PokemonIds));
            Assert.AreEqual("item.gracidea_flower", Keys(garden.Artifact, items.Ids));
            Assert.AreEqual("item.rockice_core item.steelrock_core", Keys(Fixture("legendary_site.valley_ruins").Artifact, items.Ids));
            Assert.AreEqual("483:1 484:1 487:1", Keys(Fixture("legendary_shrine.origin_shrine").Legendary, db.PokemonIds));
            Assert.AreEqual("491:0 488:0", Keys(Fixture("legendary_shrine.moonglade_shrine").Legendary, db.PokemonIds));
            // 8개 유물이 모두 어느 전설 장소의 조건이다
            var used = new SortedSet<int>();
            foreach (var f in fixtures.Fixtures) foreach (int it in f.Artifact) used.Add(it);
            Assert.AreEqual(Count(items.Items, it => it.Kind == ItemKind.Artifact), used.Count);
        }

        [Test]
        public void A8_나무는제색규토리3개_산호는등급별그림3개()
        {
            foreach (var f in fixtures.Fixtures)
            {
                if (f.Kind == FixtureKind.ApricornTree)
                {
                    Assert.AreEqual(3, f.Apricorns.Count, f.Key);
                    Assert.AreEqual(f.Apricorns[0], f.Apricorns[1], f.Key);
                    Assert.AreEqual(f.Apricorns[0], f.Apricorns[2], f.Key);
                    StringAssert.StartsWith("apricorn_tree." + f.Apricorns[0].ToString().ToLowerInvariant() + "_", f.Key);
                }
                else if (f.Kind == FixtureKind.ApricornReef)
                {
                    var want = f.Tier == EncounterTier.Weak ? new[] { ApricornColor.Red, ApricornColor.Blue, ApricornColor.Yellow }
                        : f.Tier == EncounterTier.Moderate ? new[] { ApricornColor.Pink, ApricornColor.Green, ApricornColor.White }
                        : new[] { ApricornColor.Black, ApricornColor.Black, ApricornColor.Black };
                    CollectionAssert.AreEqual(want, f.Apricorns, f.Key);
                }
                else Assert.AreEqual(0, f.Apricorns.Count, f.Key);
            }
        }

        // ── 덱·이미지 카드 ──

        [Test]
        public void M2_덱17_749장_대체상태179()
        {
            Assert.AreEqual(17, decks.Decks.Count);
            Assert.AreEqual(749, Sum(decks.Decks, d => d.Count));
            Assert.AreEqual(179, Sum(decks.Decks, d => d.AltStateCount));
            var want = new (string key, int cards, int alts)[]
            {
                ("deck.starter", 18, 0), ("deck.weak", 80, 0), ("deck.moderate", 95, 4), ("deck.strong", 98, 20), ("deck.evolution", 202, 41),
                ("deck.legendary", 32, 12), ("deck.galactic_grunt", 8, 0), ("deck.galactic_commander", 15, 0), ("deck.galactic_boss", 8, 0),
                ("deck.trainer_card", 51, 102), ("deck.act", 5, 0), ("deck.home_village", 9, 0), ("deck.shrine_event", 35, 0),
                ("deck.change_of_fate", 42, 0), ("deck.ginkgo_trade", 9, 0), ("deck.distortion_breach", 36, 0), ("deck.ultra_onslaught", 6, 0),
            };
            foreach (var (key, cards, alts) in want)
            {
                Assert.AreEqual(cards, Deck(key).Count, key);
                Assert.AreEqual(alts, Deck(key).AltStateCount, key + " 대체 상태");
            }
        }

        [Test]
        public void M2_참조무결성_포켓몬덱장수는pokemon표copies와같다()
        {
            var seen = new int[db.Pokemon.Count];
            foreach (var d in decks.Decks)
            {
                if (d.Kind != DeckKind.Pokemon) continue;
                foreach (var e in d.Cards)
                {
                    Assert.LessOrEqual(db.Pokemon[e.Card].State, 1, d.Key + " 바탕 카드 " + db.PokemonIds[e.Card]);
                    seen[e.Card] += e.Copies;
                }
                foreach (var e in d.AltStates)
                {
                    var alt = db.Pokemon[e.Card];
                    Assert.GreaterOrEqual(alt.State, 2, d.Key + " 대체 상태 " + alt.Key);
                    Assert.IsTrue(Count(d.Cards, b => db.Pokemon[b.Card].Dex == alt.Dex && db.Pokemon[b.Card].Trainer == alt.Trainer) > 0, alt.Key + "의 바탕 카드가 같은 덱에 없다");
                    seen[e.Card] += e.Copies;
                }
            }
            for (int i = 0; i < db.Pokemon.Count; i++) Assert.AreEqual(db.Pokemon[i].Copies, seen[i], db.Pokemon[i].Key);
            Assert.AreEqual(633, Sum(seen, n => n));
        }

        [Test]
        public void M2_트레이너카드덱은직업51종_다른면은실버골드2개씩()
        {
            var t = Deck("deck.trainer_card");
            Assert.AreEqual(DeckKind.TrainerCard, t.Kind);
            Assert.AreEqual(db.TrainerCards.Count, t.Cards.Count);
            for (int i = 0; i < t.Cards.Count; i++)
            {
                Assert.AreEqual(i, t.Cards[i].Card);
                Assert.AreEqual(1, t.Cards[i].Copies);
                Assert.AreEqual(i, t.AltStates[i].Card);
                Assert.AreEqual(2, t.AltStates[i].Copies);
            }
        }

        [Test]
        public void M2_이미지카드고유앞면54_사당15_운명4_균열6_은행9_고향9_막5_온슬롯6()
        {
            Assert.AreEqual(54, decks.Cards.Count);
            var want = new (string deck, int faces)[]
            {
                ("deck.shrine_event", 15), ("deck.change_of_fate", 4), ("deck.distortion_breach", 6), ("deck.ginkgo_trade", 9),
                ("deck.home_village", 9), ("deck.act", 5), ("deck.ultra_onslaught", 6),
            };
            foreach (var (deck, faces) in want)
            {
                int d = decks.DeckIds.IndexOf(deck);
                Assert.AreEqual(faces, Count(decks.Cards, c => c.Deck == d), deck);
                Assert.AreEqual(faces, decks.Decks[d].Cards.Count, deck);
            }
            int onslaught = decks.DeckIds.IndexOf("deck.ultra_onslaught");
            Assert.AreEqual(136, Sum(decks.Cards, c => c.Deck == onslaught ? 0 : c.Copies));
            Assert.AreEqual(6, Sum(decks.Cards, c => c.Deck == onslaught ? c.Copies : 0));
            Assert.AreEqual(7, decks.Cards[decks.CardIds.IndexOf("shrine_event.celestial_fracture")].Copies);
            Assert.AreEqual(6, decks.Cards[decks.CardIds.IndexOf("change_of_fate.arceus")].Copies);
            // 고향 마을 카드는 마을 9곳과 하나씩 맞는다
            foreach (var f in fixtures.Fixtures)
                if (f.Kind == FixtureKind.Village)
                    Assert.IsTrue(decks.CardIds.TryIndexOf("home_village." + f.Key.Substring("village.".Length), out _), f.Key);
        }

        // ── 아이템 ──

        [Test]
        public void M2_숨겨진장소35_별8_야영지6_더미21_유물토큰17_우두머리6_유물8_천상3()
        {
            int Copies(ItemKind k) => Sum(items.Items, it => it.Kind == k ? it.Copies ?? 0 : 0);
            int Of(string key) => items.Items[items.Ids.IndexOf(key)].Copies ?? 0;
            Assert.AreEqual(35, Copies(ItemKind.HiddenSpot));
            Assert.AreEqual(8, Of("hidden_spot.fallen_star"));
            Assert.AreEqual(6, Of("hidden_spot.campsite"));
            Assert.AreEqual(7, Count(items.Items, it => it.Kind == ItemKind.HiddenSpot && it.Key.EndsWith("_apricorn_stash", StringComparison.Ordinal) && it.Copies == 3));
            Assert.AreEqual(17, Copies(ItemKind.AlphaPokemon) + Copies(ItemKind.Artifact) + Copies(ItemKind.CelestialArtifact));
            Assert.AreEqual(6, Copies(ItemKind.AlphaPokemon));
            Assert.AreEqual(8, Copies(ItemKind.Artifact));
            Assert.AreEqual(3, Copies(ItemKind.CelestialArtifact));
            Assert.AreEqual(6, Of("item.scroll_of_nobility"));
            Assert.IsNull(items.Items[items.Ids.IndexOf("item.poke_ball")].Copies);
        }

        // ── 보드 덮개 ──

        [Test]
        public void M2_보드351칸_덮개틴트_디퓨즈_숲은기후별텍스처()
        {
            Assert.AreEqual(351, looks.Count);
            var want = new (TileDiffuse d, int n)[]
            {
                (TileDiffuse.Standard, 295), (TileDiffuse.ForestCold, 16), (TileDiffuse.ForestTemperate, 21), (TileDiffuse.ForestWarm, 18), (TileDiffuse.Secret, 1),
            };
            foreach (var (d, n) in want) Assert.AreEqual(n, Count(looks, l => l.Diffuse == d), d.ToString());
            for (int i = 0; i < board.Count; i++)
            {
                var c = board[i];
                StringAssert.IsMatch("^#[0-9a-f]{8}$", looks[i].Tint);
                bool forest = looks[i].Diffuse == TileDiffuse.ForestCold || looks[i].Diffuse == TileDiffuse.ForestTemperate || looks[i].Diffuse == TileDiffuse.ForestWarm;
                Assert.AreEqual(c.Biome == Biome.Forest, forest, c.Coord.ToString());
                if (forest) Assert.AreEqual("Forest" + c.Climate, looks[i].Diffuse.ToString(), c.Coord.ToString());
            }
        }

        // ── 이름 ──

        [Test]
        public void M2_고정물아이템이름_한영누락0()
        {
            var ko = TestData.LoadStrings("ko");
            var en = TestData.LoadStrings("en");
            var keys = new List<string>();
            foreach (var f in fixtures.Fixtures)
                if (f.Kind == FixtureKind.Village || f.Kind == FixtureKind.LegendarySite || f.Kind == FixtureKind.LegendaryShrine || f.Kind == FixtureKind.Shrine || f.Kind == FixtureKind.Gate)
                    keys.Add(f.Key);
            foreach (var it in items.Items) keys.Add(it.Key);
            Assert.AreEqual(9 + 12 + 7 + 13 + 8 + items.Items.Count, keys.Count);
            foreach (var k in keys)
            {
                Assert.IsTrue(en.TryGet(k, out var e) && e.Length > 0, "strings.en " + k);
                Assert.IsTrue(ko.TryGet(k, out var v) && System.Text.RegularExpressions.Regex.IsMatch(v, "[가-힣]"), "strings.ko " + k);
            }
            Assert.AreEqual("Stark Village", en["village.stark"]);
            Assert.AreEqual("무쇠시티", ko["village.stark"]);
            Assert.AreEqual("Seafarer Egg", en["item.seafarer_egg"]);
        }

        // ── 왕복·엄격 ──

        [Test]
        public void M2_세이브표로더왕복_같은바이트()
        {
            var utf8 = new UTF8Encoding(false);
            CollectionAssert.AreEqual(TestData.ReadBytes("board.json"), utf8.GetBytes(BoardLoader.Write(board, looks)), "board.json");
            CollectionAssert.AreEqual(TestData.ReadBytes("fixtures.json"), utf8.GetBytes(FixtureDbLoader.Write(fixtures, board, db, items)), "fixtures.json");
            CollectionAssert.AreEqual(TestData.ReadBytes("decks.json"), utf8.GetBytes(DeckDbLoader.WriteDecks(decks, db)), "decks.json");
            CollectionAssert.AreEqual(TestData.ReadBytes("cards.json"), utf8.GetBytes(DeckDbLoader.WriteCards(decks)), "cards.json");
            CollectionAssert.AreEqual(TestData.ReadBytes("items.json"), utf8.GetBytes(ItemDbLoader.Write(items)), "items.json");
        }

        static string ReplaceFirst(string s, string from, string to)
        {
            int i = s.IndexOf(from, StringComparison.Ordinal);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), $"시험 준비: \"{from}\"가 없다");
            return s.Substring(0, i) + to + s.Substring(i + from.Length);
        }

        [Test]
        public void M2_세이브표로더는모르는필드_모르는값_없는키_종류와맞지않는값을거부()
        {
            string it = TestData.ReadText("items.json"), fx = TestData.ReadText("fixtures.json");
            string dk = TestData.ReadText("decks.json"), cd = TestData.ReadText("cards.json"), bd = TestData.ReadText("board.json");
            void Items(string json) => Assert.Throws<FormatException>(() => ItemDbLoader.Load(json));
            void Fixtures(string json) => Assert.Throws<FormatException>(() => FixtureDbLoader.Load(json, board, db, items));
            void Decks(string d, string c) => Assert.Throws<FormatException>(() => DeckDbLoader.Load(d, c, db));
            void Board(string json) => Assert.Throws<FormatException>(() => BoardLoader.Load(json));

            Items(ReplaceFirst(it, "\"kind\":\"HiddenSpot\",", "\"kind\":\"HiddenSpot\",\"extra\":1,"));
            Items(ReplaceFirst(it, "\"kind\":\"Held\"", "\"kind\":\"held\""));
            Items(ReplaceFirst(it, "\"key\":\"hidden_spot.campsite\"", "\"key\":\"item.campsite\""));
            Items(ReplaceFirst(it, "\"kind\":\"Artifact\",\"copies\":1", "\"kind\":\"Artifact\",\"copies\":null"));

            Fixtures(ReplaceFirst(fx, "\"cell\":\"-8,-2\"", "\"cell\":\"99,99\""));
            Fixtures(ReplaceFirst(fx, "\"legendary\":[\"378:0\"]", "\"legendary\":[\"9999:0\"]"));
            Fixtures(ReplaceFirst(fx, "\"item.seafarer_egg\"", "\"item.seafarer_eggs\""));
            Fixtures(ReplaceFirst(fx, "\"pair\":\"gate.lilly_distortion\"", "\"pair\":\"gate.lilly_main\""));
            Fixtures(ReplaceFirst(fx, "\"kind\":\"Village\",\"cell\":\"-8,-2\",\"tier\":null", "\"kind\":\"Village\",\"cell\":\"-8,-2\",\"tier\":\"Weak\""));
            Fixtures(ReplaceFirst(fx, "\"key\":\"village.canalave\",\"kind\":\"Village\"", "\"key\":\"village.canalave\",\"kind\":\"Shrine\""));
            Fixtures(ReplaceFirst(fx, "\"apricorns\":[\"Pink\",\"Pink\",\"Pink\"]", "\"apricorns\":[\"Pink\",\"Pink\",\"pink\"]"));

            Decks(ReplaceFirst(dk, "{\"card\":\"387:0\",\"copies\":1}", "{\"card\":\"9999:0\",\"copies\":1}"), cd);
            Decks(ReplaceFirst(dk, "\"kind\":\"Pokemon\"", "\"kind\":\"pokemon\""), cd);
            Decks(dk, ReplaceFirst(cd, "{\"key\":\"act.incursion\",\"deck\":\"deck.act\",\"copies\":1,", "{\"key\":\"act.incursion\",\"deck\":\"deck.act\",\"copies\":2,"));
            Decks(dk, ReplaceFirst(cd, "\"key\":\"act.incursion\",\"deck\":\"deck.act\"", "\"key\":\"act.incursion\",\"deck\":\"deck.home_village\""));
            Decks(ReplaceFirst(dk, "{\"card\":\"act.incursion\",\"copies\":1},", "{\"card\":\"act.incursion\",\"copies\":1},{\"card\":\"act.incursion\",\"copies\":1},"), cd);

            Board(ReplaceFirst(bd, "\"tint\":\"#64affa32\"", "\"tint\":\"#64affa\""));
            Board(ReplaceFirst(bd, "\"diffuse\":\"Standard\"", "\"diffuse\":\"Forest\""));
            Items(ReplaceFirst(it, "\"copies\":3,", "\"copies\":\"3\","));
            Items(ReplaceFirst(it, "\"copies\":3,", "\"copies\":3,\"copies\":4,"));
        }

        [Test]
        public void M2_칸에는카드전용지형값Ruins_Noble_Space를거부()
        {
            string bd = TestData.ReadText("board.json");
            Assert.Throws<FormatException>(() => BoardLoader.Load(ReplaceFirst(bd, "\"biome\":\"Ocean\"", "\"biome\":\"Ruins\"")));
            Assert.Throws<FormatException>(() => BoardLoader.Load(ReplaceFirst(bd, "\"climate\":\"Cold\"", "\"climate\":\"Noble\"")));
            Assert.Throws<FormatException>(() => BoardLoader.Load(ReplaceFirst(bd, "\"climate\":\"Cold\"", "\"climate\":\"Space\"")));
        }

        [Test]
        public void M2_덱면규칙_포켓몬다른면은state2이상이고바탕이덱에_트레이너카드다른면은2()
        {
            string dk = TestData.ReadText("decks.json"), cd = TestData.ReadText("cards.json");
            void Decks(string d) => Assert.Throws<FormatException>(() => DeckDbLoader.Load(d, cd, db));
            Decks(ReplaceFirst(dk, "{\"card\":\"479:2\",\"copies\":1}", "{\"card\":\"387:0\",\"copies\":1}"));
            Decks(ReplaceFirst(dk, "{\"card\":\"387:0\",\"copies\":1}", "{\"card\":\"479:2\",\"copies\":1}"));
            Decks(ReplaceFirst(dk, "{\"card\":\"ace_trainer\",\"copies\":2}", "{\"card\":\"ace_trainer\",\"copies\":3}"));
            Decks(ReplaceFirst(dk, "{\"card\":\"ace_trainer\",\"copies\":1}", "{\"card\":\"ace_trainer\",\"copies\":2}"));
        }
    }
}
