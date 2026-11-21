using System;
using System.IO;
using Sinnoh.Data;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 시험이 저장소 안 생성 데이터(Assets/Sinnoh/Data/Json, build.mjs가 만듦)를 읽는다. Research/는 읽지 않는다.
    /// Unity는 작업 폴더가 프로젝트 루트이고 dotnet test는 Tools/dotnet-test/bin/…/net8.0이라, 두 곳에서 위로 올라가며 찾는다.
    /// </summary>
    static class TestData
    {
        const string JsonDir = "Assets/Sinnoh/Data/Json";

        public static string RepoRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                    if (Directory.Exists(Path.Combine(d.FullName, JsonDir)))
                        return d.FullName;
            throw new DirectoryNotFoundException($"{JsonDir}를 찾지 못했다(작업 폴더 {Directory.GetCurrentDirectory()}, 기준 {AppContext.BaseDirectory})");
        }

        public static string JsonPath(string file) => Path.Combine(RepoRoot(), JsonDir, file);

        /// <summary>Load(Stream) 경로로 읽는다(Load(string)도 그 안에서 지난다).</summary>
        public static Board LoadBoard()
        {
            using (var s = File.OpenRead(JsonPath("board.json")))
                return BoardLoader.Load(s);
        }

        /// <summary>Load(Stream) 경로로 읽는다(Load(string)도 그 안에서 지난다).</summary>
        public static CardDb LoadCardDb()
        {
            using (var p = File.OpenRead(JsonPath("pokemon.json")))
            using (var m = File.OpenRead(JsonPath("moves.json")))
            using (var t = File.OpenRead(JsonPath("trainer_cards.json")))
                return CardDbLoader.Load(p, m, t);
        }

        /// <summary>items.json을 Load(Stream) 경로로 읽는다.</summary>
        public static ItemDb LoadItems()
        {
            using (var s = File.OpenRead(JsonPath("items.json")))
                return ItemDbLoader.Load(s);
        }

        /// <summary>decks.json + cards.json을 Load(Stream) 경로로 읽는다.</summary>
        public static DeckDb LoadDecks(CardDb db)
        {
            using (var d = File.OpenRead(JsonPath("decks.json")))
            using (var c = File.OpenRead(JsonPath("cards.json")))
                return DeckDbLoader.Load(d, c, db);
        }

        /// <summary>fixtures.json을 Load(Stream) 경로로 읽는다.</summary>
        public static FixtureDb LoadFixtures(Board board, CardDb db, ItemDb items)
        {
            using (var s = File.OpenRead(JsonPath("fixtures.json")))
                return FixtureDbLoader.Load(s, board, db, items);
        }

        /// <summary>effects.json을 Load(Stream) 경로로 읽는다(M3-f).</summary>
        public static EffectDb LoadEffects(CardDb db)
        {
            using (var s = File.OpenRead(JsonPath("effects.json")))
                return EffectDbLoader.Load(s, db);
        }

        /// <summary>엔진 정적 데이터 묶음(01 §3-1 Ctx)을 M2·M3-f 로더로 만든다.</summary>
        public static Ctx LoadCtx()
        {
            var board = LoadBoard();
            var db = LoadCardDb();
            var items = LoadItems();
            return new Ctx(board, db, LoadDecks(db), LoadFixtures(board, db, items), items, LoadEffects(db));
        }

        /// <summary>strings.&lt;lang&gt;.json을 Load(Stream) 경로로 읽는다.</summary>
        public static StringTable LoadStrings(string lang)
        {
            using (var s = File.OpenRead(JsonPath($"strings.{lang}.json")))
                return StringTable.Load(s);
        }

        public static string ReadText(string file) => File.ReadAllText(JsonPath(file), new System.Text.UTF8Encoding(false));

        public static byte[] ReadBytes(string file) => File.ReadAllBytes(JsonPath(file));
    }
}
