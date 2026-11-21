using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>plan M2 조건 2·3: 03 §1-4 숫자를 생성 JSON을 로더로 읽어 다시 센다(build.mjs와 같은 기대값), 왕복·contentHash.</summary>
    public class CardDbTests
    {
        CardDb db;

        [OneTimeSetUp]
        public void LoadOnce() => db = TestData.LoadCardDb();

        static int Count<T>(IReadOnlyList<T> rows, Func<T, bool> p)
        {
            int n = 0;
            foreach (var r in rows)
                if (p(r)) n++;
            return n;
        }

        [Test]
        public void M2_포켓몬390행_장수633_종322_650이상39()
        {
            Assert.AreEqual(390, db.Pokemon.Count);
            int copies = 0;
            var species = new SortedSet<int>();
            foreach (var p in db.Pokemon)
            {
                copies += p.Copies;
                species.Add(p.Dex);
            }
            Assert.AreEqual(633, copies);
            Assert.AreEqual(322, species.Count);
            Assert.AreEqual(39, Count(new List<int>(species), d => d >= 650));
        }

        [TestCase(EncounterTier.Strong, 234)]
        [TestCase(EncounterTier.Moderate, 168)]
        [TestCase(EncounterTier.Weak, 85)]
        [TestCase(EncounterTier.Legendary, 84)]
        [TestCase(EncounterTier.Starter, 18)]
        [TestCase(EncounterTier.Commander, 15)]
        [TestCase(EncounterTier.UltraBeast, 9)]
        [TestCase(EncounterTier.Boss, 8)]
        [TestCase(EncounterTier.Grunt, 8)]
        [TestCase(EncounterTier.UltraBurst, 4)]
        public void M2_등급별장수(EncounterTier tier, int copies)
        {
            int n = 0;
            foreach (var p in db.Pokemon)
                if (p.Tier == tier) n += p.Copies;
            Assert.AreEqual(copies, n);
        }

        [Test]
        public void M2_기술236_트레이너카드51_능력102()
        {
            Assert.AreEqual(236, db.Moves.Count);
            Assert.AreEqual(51, db.TrainerCards.Count);
            int abilities = 0;
            foreach (var c in db.TrainerCards)
            {
                abilities += c.Abilities.Count;
                Assert.AreEqual(AbilityColor.Blue, c.Abilities[0].Color, c.Key);
                Assert.AreEqual(AbilityColor.Red, c.Abilities[1].Color, c.Key);
            }
            Assert.AreEqual(102, abilities);
            Assert.AreEqual("Team Builder", db.TrainerCards[db.TrainerCardIds.IndexOf("ace_trainer")].Abilities[0].Name);
        }

        [Test]
        public void M2_참조무결성_진화대상_시그니처_타입()
        {
            int evolving = 0, galactic = 0;
            foreach (var p in db.Pokemon)
            {
                foreach (int e in p.EvolveInto)
                {
                    Assert.That(e, Is.InRange(0, db.Pokemon.Count - 1), p.Key);
                    Assert.AreNotEqual(p.Key, db.Pokemon[e].Key);
                    Assert.AreEqual(GalacticTrainer.None, db.Pokemon[e].Trainer, $"{p.Key} → 야생 카드로 진화");
                }
                if (p.EvolveInto.Count > 0) evolving++;
                Assert.That(p.Signature, Is.InRange(0, db.Moves.Count - 1), p.Key);
                foreach (var t in p.Types) AssertInTypeChart(t, p.Key);
                foreach (var t in p.LearnTypes) AssertInTypeChart(t, p.Key);
                bool galacticTier = p.Tier == EncounterTier.Grunt || p.Tier == EncounterTier.Commander || p.Tier == EncounterTier.Boss;
                Assert.AreEqual(galacticTier, p.Trainer != GalacticTrainer.None, p.Key);
                if (galacticTier) galactic++;
            }
            foreach (var m in db.Moves) AssertInTypeChart(m.Type, m.Key);
            Assert.AreEqual(31, galactic, "갤럭시단 카드 = 조무래기 8 + 간부 15 + 태홍 8");
            Assert.That(evolving, Is.GreaterThan(0));
            var eevee = db.Pokemon[db.PokemonIds.IndexOf("133:0")];
            Assert.AreEqual(8, eevee.EvolveInto.Count, "R14 진화 분기");
        }

        static void AssertInTypeChart(PokemonType t, string key)
        {
            Assert.That((int)t, Is.InRange(0, TypeChart.TypeCount - 1), key);
            Assert.IsTrue(Enum.IsDefined(typeof(PokemonType), t), key);
        }

        [Test]
        public void M2_키는dex_state_갤럭시단은주인_동쪽바다는접미사()
        {
            var ids = db.PokemonIds;
            Assert.AreEqual(GalacticTrainer.None, db.Pokemon[ids.IndexOf("198:0")].Trainer);
            Assert.AreEqual(GalacticTrainer.FemaleGrunt, db.Pokemon[ids.IndexOf("198:0@female_grunt")].Trainer);
            Assert.AreEqual(GalacticTrainer.Jupiter, db.Pokemon[ids.IndexOf("480:0@jupiter")].Trainer);
            Assert.AreEqual(Climate.Cold, db.Pokemon[ids.IndexOf("422:0")].Climate);
            Assert.AreEqual(Climate.Warm, db.Pokemon[ids.IndexOf("422-e:0")].Climate);
            Assert.AreEqual(ids.IndexOf("423-e:0"), db.Pokemon[ids.IndexOf("422-e:0")].EvolveInto[0]);
            var frost = db.Pokemon[ids.IndexOf("479:3")];
            Assert.AreEqual(479, frost.Dex);
            Assert.AreEqual(3, frost.State);
            Assert.AreEqual("blizzard", db.MoveIds[frost.Signature]);
        }

        [Test]
        public void M2_로더왕복_같은바이트()
        {
            var utf8 = new UTF8Encoding(false);
            CollectionAssert.AreEqual(TestData.ReadBytes("pokemon.json"), utf8.GetBytes(CardDbLoader.WritePokemon(db)), "pokemon.json");
            CollectionAssert.AreEqual(TestData.ReadBytes("moves.json"), utf8.GetBytes(CardDbLoader.WriteMoves(db)), "moves.json");
            CollectionAssert.AreEqual(TestData.ReadBytes("trainer_cards.json"), utf8.GetBytes(CardDbLoader.WriteTrainerCards(db)), "trainer_cards.json");
        }

        [Test]
        public void M2_contentHash다시계산은기록값()
        {
            var manifest = ContentManifest.Load(TestData.ReadText("manifest.json"));
            CollectionAssert.AreEqual(new[] { "board", "cards", "decks", "effects", "fixtures", "items", "moves", "pokemon", "trainer_cards" }, manifest.ContentTables);
            foreach (var t in manifest.Tables)
                Assert.AreEqual(t.Sha256, ContentManifest.Sha256Hex(TestData.ReadBytes(t.File)), t.File);
            Assert.AreEqual(351, manifest.Find("board").Rows);
            Assert.AreEqual(db.Pokemon.Count, manifest.Find("pokemon").Rows);
            Assert.AreEqual(db.Moves.Count, manifest.Find("moves").Rows);
            Assert.AreEqual(db.TrainerCards.Count, manifest.Find("trainer_cards").Rows);
            Assert.AreEqual(manifest.ContentHash, manifest.ComputeContentHash());
            Assert.That(manifest.ContentHash, Does.Match("^[0-9a-f]{64}$"));
        }

        // ── 엄격한 로더: 고친 JSON은 FormatException ──

        static string ReplaceFirst(string s, string from, string to)
        {
            int i = s.IndexOf(from, StringComparison.Ordinal);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), $"시험 준비: \"{from}\"가 없다");
            return s.Substring(0, i) + to + s.Substring(i + from.Length);
        }

        static void AssertRejected(string pokemon, string moves, string trainerCards) =>
            Assert.Throws<FormatException>(() => CardDbLoader.Load(pokemon, moves, trainerCards));

        [Test]
        public void M2_로더는모르는필드와빠진필드를거부()
        {
            string p = TestData.ReadText("pokemon.json"), m = TestData.ReadText("moves.json"), t = TestData.ReadText("trainer_cards.json");
            AssertRejected(p, ReplaceFirst(m, "\"maxTotal\":", "\"extra\":1,\"maxTotal\":"), t);
            AssertRejected(ReplaceFirst(p, "\"copies\":1,", ""), m, t);
            AssertRejected(p, m, ReplaceFirst(t, "\"slot\":1,", "\"slot\":1,\"key\":\"x\","));
        }

        [Test]
        public void M2_로더는모르는값을거부()
        {
            string p = TestData.ReadText("pokemon.json"), m = TestData.ReadText("moves.json"), t = TestData.ReadText("trainer_cards.json");
            AssertRejected(p, ReplaceFirst(m, "\"type\":\"Bug\"", "\"type\":\"bug\""), t);
            AssertRejected(p, ReplaceFirst(m, "\"type\":\"Bug\"", "\"type\":\"12\""), t);
            AssertRejected(ReplaceFirst(p, "\"biome\":\"Forest\"", "\"biome\":\"None\""), m, t);
            AssertRejected(ReplaceFirst(p, "\"tier\":\"Starter\"", "\"tier\":\"starter\""), m, t);
            AssertRejected(p, m, ReplaceFirst(t, "\"color\":\"Blue\"", "\"color\":\"Red\""));
            AssertRejected(ReplaceFirst(p, "\"key\":\"387:0\"", "\"key\":\"387:1\""), m, t);
        }

        [Test]
        public void M2_로더는없는키참조와중복키를거부()
        {
            string p = TestData.ReadText("pokemon.json"), m = TestData.ReadText("moves.json"), t = TestData.ReadText("trainer_cards.json");
            AssertRejected(ReplaceFirst(p, "\"evolveInto\":[\"388:0\"]", "\"evolveInto\":[\"9999:0\"]"), m, t);
            AssertRejected(ReplaceFirst(p, "\"signature\":\"razor_leaf\"", "\"signature\":\"razor_leaves\""), m, t);
            AssertRejected(p, ReplaceFirst(m, "\"key\":\"bug_bite\"", "\"key\":\"struggle_bug\""), t);
        }

        [Test]
        public void M2_로더는같은필드두번을거부()
        {
            string p = TestData.ReadText("pokemon.json"), m = TestData.ReadText("moves.json"), t = TestData.ReadText("trainer_cards.json");
            AssertRejected(ReplaceFirst(p, "\"health\":6,", "\"health\":6,\"health\":99,"), m, t);
            AssertRejected(p, ReplaceFirst(m, "\"maxTotal\":12,", "\"maxTotal\":12,\"maxTotal\":12,"), t);
            string bd = TestData.ReadText("board.json");
            Assert.Throws<FormatException>(() => BoardLoader.Load(ReplaceFirst(bd, "\"tint\":\"#64affa32\"", "\"tint\":\"#64affa32\",\"tint\":\"#000000ff\"")));
            string mf = TestData.ReadText("manifest.json");
            Assert.Throws<FormatException>(() => ContentManifest.Load(ReplaceFirst(mf, "\"rows\": 351,", "\"rows\": 351,\n      \"rows\": 352,")));
        }

        [Test]
        public void M2_로더는형이다른값을거부_문자열수_수문자열_문자열참거짓()
        {
            string p = TestData.ReadText("pokemon.json"), m = TestData.ReadText("moves.json"), t = TestData.ReadText("trainer_cards.json");
            AssertRejected(ReplaceFirst(p, "\"health\":6,", "\"health\":\"6\","), m, t);
            AssertRejected(ReplaceFirst(p, "\"starter\":true}", "\"starter\":\"true\"}"), m, t);
            AssertRejected(ReplaceFirst(p, "\"evolveCost\":4,", "\"evolveCost\":\"4\","), m, t);
            AssertRejected(p, ReplaceFirst(m, "\"power\":50,", "\"power\":50.0,"), t);
            AssertRejected(p, m, ReplaceFirst(t, "\"class\":\"Ace Trainer\"", "\"class\":5"));
            AssertRejected(p, m, ReplaceFirst(t, "\"slot\":1,", "\"slot\":[1],"));
            string bd = TestData.ReadText("board.json");
            Assert.Throws<FormatException>(() => BoardLoader.Load(ReplaceFirst(bd, "\"x\":-21.45,", "\"x\":\"-21.45\",")));
            Assert.Throws<FormatException>(() => BoardLoader.Load(ReplaceFirst(bd, "\"wild\":true,", "\"wild\":1,")));
        }

        [Test]
        public void M2_키접미사는겹치는키가있을때만()
        {
            string p = TestData.ReadText("pokemon.json"), m = TestData.ReadText("moves.json"), t = TestData.ReadText("trainer_cards.json");
            Assert.IsTrue(db.PokemonIds.TryIndexOf("422-e:0", out _), "동쪽 바다 422-e:0은 422:0과 겹쳐 접미사를 남긴다");
            AssertRejected(ReplaceFirst(p, "\"key\":\"387:0\"", "\"key\":\"387-z:0\""), m, t);
        }

        [Test]
        public void M2_IdTable은키와색인을서로바꾸고모르는키는오류()
        {
            var ids = new IdTable("t", new[] { "a", "b", "c" });
            Assert.AreEqual(3, ids.Count);
            Assert.AreEqual(1, ids.IndexOf("b"));
            Assert.AreEqual("c", ids[2]);
            Assert.IsFalse(ids.TryIndexOf("d", out _));
            Assert.Throws<KeyNotFoundException>(() => ids.IndexOf("d"));
            Assert.Throws<KeyNotFoundException>(() => ids.IndexOf(null));
            Assert.Throws<ArgumentException>(() => new IdTable("t", new[] { "a", "a" }));
            Assert.Throws<ArgumentException>(() => new IdTable("t", new[] { "a", "" }));
            for (int i = 0; i < db.PokemonIds.Count; i++) Assert.AreEqual(i, db.PokemonIds.IndexOf(db.Pokemon[i].Key));
        }
    }
}
