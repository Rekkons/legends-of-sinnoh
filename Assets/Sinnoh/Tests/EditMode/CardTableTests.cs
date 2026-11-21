using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M5 「카드·아이템 데이터」(완료 조건 2, 03 §1-3·§1-4 이미지 전용 카드 줄): 손으로 옮긴 카드 문구(Tools/data/authored/cards/)와
    /// 아이템 레시피·스튜·스탬프(authored/items.json)가 생성 JSON에 실렸는지 로더로 읽어 다시 센다. 영문 본문은 카드 그림을 보고 옮긴 것이라
    /// 덱마다 규칙이 갈리는 글 한두 곳을 글자 그대로 맞대 본다(한국어판 오역이 있던 곳 — rulings A1·A15).
    /// </summary>
    public class CardTableTests
    {
        CardDb db;
        DeckDb decks;
        ItemDb items;
        StringTable ko, en;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            db = TestData.LoadCardDb();
            decks = TestData.LoadDecks(db);
            items = TestData.LoadItems();
            ko = TestData.LoadStrings("ko");
            en = TestData.LoadStrings("en");
        }

        /// <summary>그 덱의 앞면이 모두 verified·src 있음·본문(또는 거래) 있음이고, 이름·본문 줄마다 두 언어 문자열이 있다(영어 문자열 = 데이터의 영문).</summary>
        List<CardDef> Deck(string deck, int faces, int copies)
        {
            int d = decks.DeckIds.IndexOf("deck." + deck);
            Assert.That(d, Is.GreaterThanOrEqualTo(0), deck);
            var list = new List<CardDef>();
            int sum = 0;
            foreach (var c in decks.Cards)
            {
                if (c.Deck != d) continue;
                list.Add(c);
                sum += c.Copies;
                Assert.That(c.Verified, Does.Match("^20[0-9]{2}-[0-9]{2}-[0-9]{2}$"), c.Key);
                Assert.That(c.SrcKo, Is.Not.Empty, c.Key);
                Assert.That(c.SrcEn, Does.Contain(".png#"), c.Key + ": 영문 출처 = 원작 카드 그림의 시트 칸");
                Assert.IsTrue(ko.Contains(c.Key) && en.Contains(c.Key), c.Key + ": 이름 문자열");
                Assert.AreEqual(deck == "ginkgo_trade", c.En == null, c.Key);
                Assert.AreEqual(deck == "ginkgo_trade", c.Trades != null, c.Key);
                if (c.En == null) continue;
                for (int i = 0; i < c.En.Count; i++)
                {
                    string key = c.Key + ".text." + (i + 1);
                    Assert.AreEqual(c.En[i], en[key], key);
                    Assert.That(ko[key], Is.Not.Empty, key);
                }
                Assert.IsFalse(en.Contains(c.Key + ".text." + (c.En.Count + 1)), c.Key + ": 본문 줄 수보다 많은 문자열");
            }
            Assert.AreEqual(faces, list.Count, deck + " 고유 앞면");
            Assert.AreEqual(copies, sum, deck + " 장수");
            return list;
        }

        CardDef Card(string key) => decks.Cards[decks.CardIds.IndexOf(key)];

        static string Colors(IReadOnlyList<ApricornColor> list) => string.Join("+", list);

        static string Trade(CardDef c) => Colors(c.Trades[0].Give) + ">" + Colors(c.Trades[0].Get) + " " + Colors(c.Trades[1].Give) + ">" + Colors(c.Trades[1].Get);

        [Test]
        public void M5_카드표_act()
        {
            Deck("act", 5, 5);
            CollectionAssert.Contains(Card("act.journey").En, "Objective: Defeat the 2 Team Galactic Grunts at your home village.", "rulings A1: 배틀 1번(한국어판 「2번 승리」는 오역)");
            CollectionAssert.Contains(Card("act.journey").En, "Reward: +2 Movement per Turn");
            CollectionAssert.Contains(Card("act.shards").En, "Reward: +1 Party Slot per Shard");
            CollectionAssert.Contains(Card("act.heart").En, "Once one trainer completes this objective: They draw the Ending card.");
            Assert.That(ko["act.journey.text.3"], Does.Not.Contain("2번"), "한국어 문자열도 영문 기준으로 고쳤다");
            Assert.AreEqual("여정", ko["act.journey"]);
        }

        [Test]
        public void M5_카드표_home_village()
        {
            var list = Deck("home_village", 9, 9);
            foreach (var c in list)
            {
                Assert.AreEqual("Choose your starter", c.En[c.Key == "home_village.ultra_megalopolis" ? 1 : 0], c.Key);
                // 결정 173: TTS 자리 색 안내 줄(「Place this card in your hand…」)은 뺐다 — 고르기 줄이 끝 줄
                Assert.AreEqual(c.Key == "home_village.ultra_megalopolis" ? 2 : 1, c.En.Count, c.Key);
                Assert.IsFalse(ko.Contains(c.Key + ".text." + (c.En.Count + 1)), c.Key + ": 한국어에도 빠진 줄이 없다");
                // 카드 이름 = 그 마을 이름(고정물 문자열)
                Assert.AreEqual(ko["village." + c.Key.Substring("home_village.".Length)], ko[c.Key], c.Key);
            }
            Assert.AreEqual("Ultra Burst Campaign Only", Card("home_village.ultra_megalopolis").En[0]);
        }

        [Test]
        public void M5_카드표_shrine_event()
        {
            var list = Deck("shrine_event", 15, 35);
            int blessings = 0;
            foreach (var c in list)
                if (c.En[0] == "4+ trainers: Choose another trainer to also get this effect") { blessings++; Assert.AreEqual(3, c.Copies, c.Key); }
            Assert.AreEqual(7, blessings, "사당의 축복 7종 × 3장(A19 4인 이상 공유 줄)");
            Assert.AreEqual(7, Card("shrine_event.celestial_fracture").Copies);
            Assert.AreEqual("Gain 2/4/6 Journey Points.", Card("shrine_event.tranquil_moment").En[1]);
            Assert.AreEqual("Gain a Greater Ball/Ultimate Ball/Keystone Ball.", Card("shrine_event.hidden_treasure").En[1]);
            Assert.AreEqual("All trainers gain 1 Black Apricorn. Place the Darkrai token on the Moonglade Shrine (replaces the Cresselia token).", Card("shrine_event.shadow_emerges").En[0]);
            Assert.That(Card("shrine_event.celestial_fracture").En[0], Does.EndWith("on the Origin Shrine (you choose if tied)."));
        }

        [Test]
        public void M5_카드표_change_of_fate()
        {
            Deck("change_of_fate", 4, 42);
            CollectionAssert.AreEqual(new[] { "Reroll 1", "Play 3: Reroll Any" }, Card("change_of_fate.dialga").En);
            CollectionAssert.AreEqual(new[] { "Teleport 3", "Play 3: Teleport Anywhere" }, Card("change_of_fate.palkia").En);
            CollectionAssert.AreEqual(new[] { "Sacrifice 2", "Play 3: Sacrifice All" }, Card("change_of_fate.giratina").En);
            CollectionAssert.AreEqual(new[] { "Wildcard", "Play 3: Blessing of Arceus" }, Card("change_of_fate.arceus").En);
            Assert.AreEqual(6, Card("change_of_fate.arceus").Copies);
            // 결정 43 ⑨: 카드 앞면 말은 한국어판 그대로
            Assert.AreEqual("재굴림 1", ko["change_of_fate.dialga.text.1"]);
            Assert.AreEqual("순간이동 3", ko["change_of_fate.palkia.text.1"]);
            Assert.AreEqual("희생 2", ko["change_of_fate.giratina.text.1"]);
            Assert.AreEqual("와일드카드", ko["change_of_fate.arceus.text.1"]);
        }

        [Test]
        public void M5_카드표_ginkgo_trade()
        {
            var list = Deck("ginkgo_trade", 9, 9);
            var want = new[]
            {
                "Yellow+Red>Pink Green+White>Black", "Blue+Yellow>Green White+Pink>Black", "Red+Blue>White Pink+Green>Black",
                "Pink>Red+Blue+Yellow Black>Pink+Green+White", "Green>Red+Blue+Yellow Black>Pink+Green+White", "White>Red+Blue+Yellow Black>Pink+Green+White",
                "Red+Red>Blue+Yellow Pink+Pink>Green+White", "Blue+Blue>Yellow+Red Green+Green>White+Pink", "Yellow+Yellow>Red+Blue White+White>Pink+Green",
            };
            for (int i = 0; i < want.Length; i++)
            {
                Assert.AreEqual("ginkgo_trade." + (i + 1), list[i].Key);
                Assert.AreEqual(want[i], Trade(list[i]), list[i].Key + "(rules-summary §11, A4)");
            }
        }

        [Test]
        public void M5_카드표_distortion_breach()
        {
            var list = Deck("distortion_breach", 6, 36);
            foreach (var c in list) Assert.AreEqual("You must attach this ability to a Pokémon in your party.", c.En[0], c.Key);
            Assert.AreEqual("The moves of the user are now Typeless, but their attack strengths are increased by 1.", Card("distortion_breach.chromatic_union").En[1],
                "rulings A15: +1(한국어판 「1 감소」는 오역)");
            Assert.That(ko["distortion_breach.chromatic_union.text.2"], Does.Contain("위력이 1 오릅니다"));
            Assert.AreEqual("Reduce the initiative of the user by 3 but increase the base health of the user by 3.", Card("distortion_breach.gravitational_drag").En[1]);
        }

        [Test]
        public void M5_카드표_ultra_onslaught()
        {
            var list = Deck("ultra_onslaught", 6, 6);
            var last = new[]
            {
                "Heal 4 damage and all status effects.", "Replace all status symbols with critical hits.",
                "Immediately trigger the effects of Beast Boost. If Beast Boost is already active, add to its effect.",
                "When next attacked, all regular hits damage the attacker as well.", "Use Alpha Strike twice this turn.",
                "Deal double damage to the opposing Pokémon if they have any Distortion Breach cards attached.",
            };
            for (int i = 0; i < last.Length; i++)
            {
                Assert.AreEqual("ultra_onslaught." + (i + 1), list[i].Key);
                Assert.AreEqual(last[i], list[i].En[list[i].En.Count - 1], list[i].Key);
            }
            Assert.AreEqual("Do not attack this turn.", list[0].En[0]);
        }

        [Test]
        public void M5_카드표_고유앞면54모두verified()
        {
            Assert.AreEqual(54, decks.Cards.Count);
            foreach (var c in decks.Cards) Assert.That(c.Verified, Is.Not.Empty, c.Key);
        }

        ItemDef Item(string key) => items.Items[items.Ids.IndexOf(key)];

        [Test]
        public void M5_아이템표_레시피14_볼4_스튜4_배틀아이템6_스탬프7()
        {
            var recipes = new (string key, ItemKind kind, string recipe)[]
            {
                ("item.poke_ball", ItemKind.Ball, "Red"), ("item.greater_ball", ItemKind.Ball, "Blue+Green+Red"),
                ("item.ultimate_ball", ItemKind.Ball, "Black+Yellow"), ("item.keystone_ball", ItemKind.Ball, "Black+Black+Pink+White"),
                ("item.restoring_stew", ItemKind.Stew, "Yellow+Blue"), ("item.alluring_stew", ItemKind.Stew, "Green+Red"),
                ("item.zesty_stew", ItemKind.Stew, "White+Yellow"), ("item.infused_stew", ItemKind.Stew, "Pink+White"),
                ("item.blast_crystal", ItemKind.Held, "Red+Red+Red+Yellow+Yellow+Black"), ("item.star_shield", ItemKind.Held, "Blue+Blue+Blue+Pink+Pink+Black"),
                ("item.lucky_egg", ItemKind.Held, "Yellow+Yellow+Yellow+Green+Green+Black"), ("item.eviolite", ItemKind.Held, "Pink+Pink+Pink+White+White+Black"),
                ("item.swiftness_sash", ItemKind.Held, "Green+Green+Green+Blue+Blue+Black"), ("item.choice_band", ItemKind.Held, "White+White+White+Red+Red+Black"),
            };
            foreach (var (key, kind, recipe) in recipes)
            {
                var it = Item(key);
                Assert.AreEqual(kind, it.Kind, key);
                Assert.AreEqual(recipe, string.Join("+", it.Recipe), key + "(rules-summary §10)");
                Assert.AreEqual(it.En, en[key + ".text"], key);
                Assert.That(ko[key + ".text"], Is.Not.Empty, key);
                Assert.IsTrue(ko.Contains(key), key + ": 이름");
            }
            int withRecipe = 0, stews = 0, stamps = 0;
            foreach (var it in items.Items)
            {
                if (it.Recipe != null) withRecipe++;
                if (it.Kind == ItemKind.Stew) stews++;
                if (it.Kind != ItemKind.Stamp) continue;
                stamps++;
                Assert.AreEqual("stamp." + stamps, it.Key);
                Assert.AreEqual(it.En, en[it.Key + ".text"], it.Key);
                Assert.IsNull(it.Recipe, it.Key);
            }
            Assert.AreEqual(14, withRecipe);
            Assert.AreEqual(4, stews);
            Assert.AreEqual(7, stamps);
            Assert.IsNull(Item("item.heart_of_sinnoh").Recipe, "제작하지 않는 지닌물건");
            Assert.AreEqual("Roll 6 dice when rolling to capture.", Item("item.keystone_ball").En);
            Assert.AreEqual("You fail to capture a wild Pokémon 2 turns in a row during a battle.", Item("stamp.1").En);
            // 세이브에서 뽑은 행의 색인은 그대로다(새 행은 뒤에 붙는다) — 저장·녹화본이 아이템 색인을 든다
            Assert.AreEqual(ItemKind.Ball, items.Items[32].Kind);
            Assert.AreEqual(ItemKind.Stew, items.Items[33].Kind);
        }

        [Test]
        public void M5_카드아이템표로더는빈칸_모르는색_본문과거래둘다를거부()
        {
            string cd = TestData.ReadText("cards.json"), dk = TestData.ReadText("decks.json"), it = TestData.ReadText("items.json");
            void Cards(string from, string to)
            {
                int i = cd.IndexOf(from, StringComparison.Ordinal);
                Assert.That(i, Is.GreaterThanOrEqualTo(0), $"시험 준비: \"{from}\"가 없다");
                Assert.Throws<FormatException>(() => DeckDbLoader.Load(dk, cd.Substring(0, i) + to + cd.Substring(i + from.Length), db), from);
            }
            void Items(string from, string to)
            {
                int i = it.IndexOf(from, StringComparison.Ordinal);
                Assert.That(i, Is.GreaterThanOrEqualTo(0), $"시험 준비: \"{from}\"가 없다");
                Assert.Throws<FormatException>(() => ItemDbLoader.Load(it.Substring(0, i) + to + it.Substring(i + from.Length)), from);
            }
            Cards("\"verified\":\"2026-11-14\"", "\"verified\":\"\"");
            Cards("\"verified\":\"2026-11-14\"", "\"verified\":null");
            Cards("\"src\":{\"ko\":\"rules-summary §16.2 · raw/cards/acts#1\"", "\"src\":{\"ko\":\"\"");
            Cards("\"en\":[\"Reroll 1\",\"Play 3: Reroll Any\"]", "\"en\":[]");
            Cards("\"en\":[\"Reroll 1\",\"Play 3: Reroll Any\"],\"trades\":null", "\"en\":null,\"trades\":null");
            Cards("\"give\":[\"Yellow\",\"Red\"]", "\"give\":[\"Yellow\",\"Purple\"]");
            Cards("\"give\":[\"Yellow\",\"Red\"]", "\"give\":[]");
            Cards("\"en\":null,\"trades\":[", "\"en\":[\"x\"],\"trades\":[");
            Items("\"recipe\":[\"Red\"]", "\"recipe\":[\"red\"]");
            Items("\"recipe\":[\"Red\"]", "\"recipe\":[]");
            Items("\"en\":\"Roll 3 dice when rolling to capture.\",\"recipe\":[\"Red\"]", "\"en\":null,\"recipe\":[\"Red\"]");
            Items("\"key\":\"stamp.1\"", "\"key\":\"item.stamp_1\"");
            Items("\"key\":\"item.restoring_stew\",\"kind\":\"Stew\"", "\"key\":\"item.restoring_stew\",\"kind\":\"Soup\"");
        }
    }
}
