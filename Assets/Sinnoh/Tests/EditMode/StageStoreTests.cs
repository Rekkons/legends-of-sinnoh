using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>대기 자리 stage/(결정 161 K3): .part 확인 → 이름 바꿈 · 틀리면 지움 · 묶음 zip에서 필요한 항목만 풀기. 임시 폴더만 쓴다.</summary>
    public sealed class StageStoreTests
    {
        string dir;

        [SetUp]
        public void Make()
        {
            dir = Path.Combine(Path.GetTempPath(), "sinnoh-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void Drop()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        static string ShaOf(byte[] b) => InstallScan.Hex(System.Security.Cryptography.SHA256.Create().ComputeHash(b));

        [Test]
        public void M10_대기자리_part이어쓰기와확인_틀리면지움()
        {
            var body = Encoding.UTF8.GetBytes("hello stage");
            string sha = ShaOf(body);
            File.WriteAllBytes(StageStore.PartOf(dir, sha), Encoding.UTF8.GetBytes("hello"));
            Assert.That(StageStore.PartLength(dir, sha), Is.EqualTo(5), "받다 만 조각");
            using (var s = new FileStream(StageStore.PartOf(dir, sha), FileMode.Append)) s.Write(body, 5, body.Length - 5);
            Assert.That(StageStore.Commit(dir, sha, body.Length, out string why), Is.True, why);
            Assert.That(File.Exists(StageStore.PartOf(dir, sha)), Is.False);
            Assert.That(File.ReadAllBytes(StageStore.FileOf(dir, sha)), Is.EqualTo(body));
            var f = new ReleaseFile { Path = "Sinnoh.exe", Size = body.Length, Sha256 = sha };
            Assert.That(StageStore.Staged(dir, new[] { f }), Is.EquivalentTo(new[] { sha }));
            Assert.That(StageStore.Staged(dir, new[] { new ReleaseFile { Path = "x", Size = 1, Sha256 = sha } }), Is.Empty, "크기가 다르면 받아 둔 것이 아니다");

            string other = new string('1', 64);
            File.WriteAllBytes(StageStore.PartOf(dir, other), body);
            Assert.That(StageStore.Commit(dir, other, body.Length, out why), Is.False);
            Assert.That((why, File.Exists(StageStore.PartOf(dir, other)), File.Exists(StageStore.FileOf(dir, other))), Is.EqualTo(("sha", false, false)), "지문이 틀리면 지운다");
            File.WriteAllBytes(StageStore.PartOf(dir, other), body);
            Assert.That(StageStore.Commit(dir, other, body.Length + 1, out why), Is.False);
            Assert.That(why, Is.EqualTo("size"));
        }

        [Test]
        public void M10_대기자리_zip에서필요한항목만풀기()
        {
            // pack writeZip 꼴: 맨 위 폴더 한 조각 + UTF-8 이름, deflate · 저장
            byte[] a = Encoding.UTF8.GetBytes(new string('a', 4000)), b = Encoding.UTF8.GetBytes("글꼴"), c = Encoding.UTF8.GetBytes("ccc");
            string zipFile = Path.Combine(dir, "pkg.zip");
            using (var zip = new ZipArchive(new FileStream(zipFile, FileMode.Create), ZipArchiveMode.Create))
            {
                void Add(string name, byte[] data, CompressionLevel level)
                {
                    using (var s = zip.CreateEntry("Legends of Sinnoh/" + name, level).Open()) s.Write(data, 0, data.Length);
                }

                Add("Sinnoh_Data/a.bin", a, CompressionLevel.Optimal);
                Add("Sinnoh_Data/글꼴.txt", b, CompressionLevel.NoCompression);
                Add("WebGL/c.gz", c, CompressionLevel.Optimal);
            }
            var m = new ReleaseManifest { Version = "0.2.0" };
            m.Files.Add(new ReleaseFile { Path = "Sinnoh_Data/a.bin", Size = a.Length, Sha256 = ShaOf(a) });
            m.Files.Add(new ReleaseFile { Path = "Sinnoh_Data/글꼴.txt", Size = b.Length, Sha256 = ShaOf(b) });
            m.Files.Add(new ReleaseFile { Path = "WebGL/c.gz", Size = c.Length, Sha256 = ShaOf(c) });
            var want = new[] { m.Files[0], m.Files[1] };
            Assert.That(StageStore.Unzip(zipFile, m, want, dir), Is.Null);
            Assert.That(File.ReadAllBytes(StageStore.FileOf(dir, ShaOf(a))), Is.EqualTo(a));
            Assert.That(File.ReadAllBytes(StageStore.FileOf(dir, ShaOf(b))), Is.EqualTo(b), "한글 이름");
            Assert.That(File.Exists(StageStore.FileOf(dir, ShaOf(c))), Is.False, "필요한 것만 푼다");

            var lie = new ReleaseFile { Path = "WebGL/c.gz", Size = c.Length, Sha256 = new string('2', 64) };
            var m2 = new ReleaseManifest { Version = "0.2.0" };
            m2.Files.Add(lie);
            Assert.That(StageStore.Unzip(zipFile, m2, new[] { lie }, dir), Is.EqualTo("sha"), "zip 항목의 지문이 manifest와 다르면");
            var gone = new ReleaseFile { Path = "Sinnoh_Data/none", Size = 1, Sha256 = new string('3', 64) };
            m2.Files.Add(gone);
            Assert.That(StageStore.Unzip(zipFile, m2, new[] { gone }, dir), Is.EqualTo("missing"));
            File.WriteAllText(zipFile, "zip이 아님");
            Assert.That(StageStore.Unzip(zipFile, m, want, dir), Is.EqualTo("zip"));
        }
    }
}
