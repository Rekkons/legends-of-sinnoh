using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>설치 폴더 경로 규칙(결정 161 K5) — 도우미(<c>Tools/updater/build.mjs</c> <c>PATH_TABLE</c> 33줄)와 같은 표: (경로, 규칙 통과, 지울 수 있음).</summary>
    public sealed class InstallPathTests
    {
        static readonly string A99 = new string('a', 99);

        static readonly (string rel, bool safe, bool deletable)[] Table =
        {
            ("Sinnoh.exe", true, false), ("Sinnoh_Data/Managed/a.dll", true, true), ("WebGL/StreamingAssets/content/cries.gz", true, true), ("Sinnoh_Data/{brace}.bin", true, true), ("Sinnoh_Data/글꼴.txt", true, true),
            ("", false, false), ("/a", false, false), ("a//b", false, false), ("./a", false, false), ("a/../b", false, false), ("..", false, false), ("a\\b", false, false), ("C:/a", false, false),
            ("a:b", false, false), ("CON", false, false), ("Sinnoh_Data/nul.txt", false, false), ("a.", false, false), ("a /b", false, false), ("_update/x", false, false), ("_UPDATE/x", false, false), ("a?b", false, false),
            ("Sinnoh_Data/x", true, true), ("WebGL/a/b", true, true), ("CREDITS.md", true, false), ("unins000.exe", true, false), ("notes.txt", true, false), ("Sinnoh_Data", true, false),
            ("sinnoh_data/x", true, true), ("Sinnoh_Data/COM1.log", false, false), ("lpt9", false, false), ("a\tb", false, false), (A99 + "/" + A99 + "/" + new string('a', 59), true, false), (A99 + "/" + A99 + "/" + new string('a', 60), false, false),
        };

        [Test]
        public void M10_K5경로표_도우미와같은33줄()
        {
            Assert.That(Table.Length, Is.EqualTo(33));
            foreach (var (rel, safe, deletable) in Table)
            {
                string name = rel.Length > 40 ? rel.Length + "자" : "「" + rel + "」";
                Assert.That(InstallPath.Safe(rel, out string why), Is.EqualTo(safe), name + " 규칙 — " + why);
                Assert.That(InstallPath.Deletable(rel), Is.EqualTo(deletable), name + " 지울 수 있음");
            }
            Assert.That(InstallPath.Safe(null, out _), Is.False);
        }

        [Test]
        public void M10_K5지울수있는자리_네폴더아래파일만()
        {
            foreach (var r in InstallPath.Roots) Assert.That(InstallPath.Deletable(r + "f"), Is.True, r);
            Assert.That(InstallPath.Deletable("Sinnoh_Data/"), Is.False, "폴더 자신");
            Assert.That(InstallPath.Deletable("Sinnoh_Data/../x"), Is.False, "규칙을 어긴 경로");
            Assert.That(InstallPath.Safe("COM10", out _), Is.True, "COM10은 예약 이름이 아니다");
        }

        [Test]
        public void M10_K5대소문자중복과_설치밖으로나가는합친경로()
        {
            Assert.That(InstallPath.Duplicate(new[] { "Sinnoh.exe", "Sinnoh_Data/a", "SINNOH.EXE" }), Is.EqualTo("SINNOH.EXE"));
            Assert.That(InstallPath.Duplicate(new[] { "a", "b" }), Is.Null);
            string install = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sinnoh-k5");
            Assert.That(InstallPath.Full(install, "Sinnoh_Data/a.dll"), Is.EqualTo(System.IO.Path.Combine(install, "Sinnoh_Data", "a.dll")));
            Assert.That(InstallPath.Full(install, "a/../../b"), Is.Null);
            Assert.That(InstallPath.Full(install, "_update/x"), Is.Null);
        }
    }
}
