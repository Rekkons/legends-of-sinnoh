using System;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>설치 견주기(결정 161 4-1) — 임시 폴더에 가짜 설치를 만든다(사용자 설치 폴더에 쓰지 않는다).</summary>
    public sealed class InstallScanTests
    {
        string dir;

        [SetUp]
        public void Make()
        {
            dir = Path.Combine(Path.GetTempPath(), "sinnoh-scan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "Sinnoh_Data", "Managed"));
        }

        [TearDown]
        public void Drop()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        void Put(string rel, string text) => File.WriteAllText(Path.Combine(dir, rel), text, new UTF8Encoding(false));

        static string ShaOf(string text) => InstallScan.Hex(System.Security.Cryptography.SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(text)));

        static ReleaseManifest Manifest(params (string path, string text)[] files)
        {
            var m = new ReleaseManifest { Version = "0.2.0" };
            foreach (var f in files) m.Files.Add(new ReleaseFile { Path = f.path, Size = Encoding.UTF8.GetByteCount(f.text), Sha256 = ShaOf(f.text) });
            return m;
        }

        [Test]
        public void M10_견주기_크기다르면안잼_캐시맞으면캐시_수정시각바뀌면다시잼()
        {
            Put("Sinnoh.exe", "exe-1");
            Put("Sinnoh_Data/Managed/a.dll", "aaaa");
            Put("Sinnoh_Data/old.bin", "old");
            var m = Manifest(("Sinnoh.exe", "exe-1"), ("Sinnoh_Data/Managed/a.dll", "bbbbbb"), ("Sinnoh_Data/missing", "x"));
            long[] last = { -1, -1 };
            var scan = InstallScan.Run(dir, m, (done, total) => { last[0] = done; last[1] = total; });
            Assert.That(scan.Files.ConvertAll(f => f.Path + ":" + f.Size + ":" + (f.Sha256 ?? "-")),
                Is.EqualTo(new[] { "Sinnoh.exe:5:" + ShaOf("exe-1"), "Sinnoh_Data/Managed/a.dll:4:-" }), "없는 파일은 빠지고 크기가 다르면 재지 않는다");
            Assert.That(last, Is.EqualTo(new long[] { 5, 5 }), "잰 바이트 = 크기가 같은 파일만");
            Assert.That(scan.Disk, Is.EqualTo(new[] { "Sinnoh_Data/Managed/a.dll", "Sinnoh_Data/old.bin" }));
            Assert.That(File.Exists(Path.Combine(dir, "_update", InstallScan.CacheFile)), Is.True, "캐시를 쓴다");

            // 캐시가 맞으면 다시 재지 않는다(캐시 지문을 일부러 바꿔 두면 그 값이 나온다)
            string cache = Path.Combine(dir, "_update", InstallScan.CacheFile);
            File.WriteAllText(cache, File.ReadAllText(cache).Replace(ShaOf("exe-1"), new string('7', 64)));
            Assert.That(InstallScan.Run(dir, m).Files[0].Sha256, Is.EqualTo(new string('7', 64)), "캐시 지문");

            // 수정 시각이 바뀌면 다시 잰다
            File.SetLastWriteTimeUtc(Path.Combine(dir, "Sinnoh.exe"), File.GetLastWriteTimeUtc(Path.Combine(dir, "Sinnoh.exe")).AddSeconds(-5));
            Assert.That(InstallScan.Run(dir, m).Files[0].Sha256, Is.EqualTo(ShaOf("exe-1")), "수정 시각이 다르면 다시 잰다");

            File.WriteAllText(cache, "{망가짐");
            Assert.That(InstallScan.Run(dir, m).Files[0].Sha256, Is.EqualTo(ShaOf("exe-1")), "망가진 캐시는 버린다");
        }

        [Test]
        public void M10_견주기_update폴더는뺌_취소()
        {
            Put("Sinnoh.exe", "exe");
            Directory.CreateDirectory(Path.Combine(dir, "_update", "stage"));
            Put("_update/stage/x.part", "zz");
            var m = Manifest(("Sinnoh.exe", "exe"));
            m.Files.Add(new ReleaseFile { Path = "_update/stage/x.part", Size = 2, Sha256 = new string('a', 64) });
            var scan = InstallScan.Run(dir, m);
            Assert.That(scan.Files.ConvertAll(f => f.Path), Is.EqualTo(new[] { "Sinnoh.exe" }), "_update/는 견주지 않는다");
            Assert.That(scan.Disk, Is.Empty);

            var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => InstallScan.Run(dir, m, null, cts.Token));
        }
    }
}
