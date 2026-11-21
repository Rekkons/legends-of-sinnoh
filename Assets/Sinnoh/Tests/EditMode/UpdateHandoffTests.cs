using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>게임 → 도우미 넘기기(결정 161 K3): plan.json 꼴 · 인자 따옴표 · run/ 도우미 고르기 · 켤 때 정리. 임시 폴더만 쓴다.</summary>
    public sealed class UpdateHandoffTests
    {
        string dir;

        [SetUp]
        public void Make()
        {
            dir = Path.Combine(Path.GetTempPath(), "sinnoh-handoff " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void Drop()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        [Test]
        public void M10_넘기기_plan꼴과_latest는받은글그대로()
        {
            var plan = new UpdatePlan { Version = "0.2.0", From = "0.1.1" };
            plan.Put.Add(new ReleaseFile { Path = "Sinnoh_Data/Managed/Sinnoh.Client.dll", Size = 123, Sha256 = new string('a', 64) });
            plan.Delete.Add("Sinnoh_Data/StreamingAssets/content/old");
            var m = new ReleaseManifest { Version = "0.2.0", Text = "{\"format\":1,\"manifest\":\"…\",\"signature\":\"x\"}" };
            string file = UpdateHandoff.Write(dir, m, plan, 1234, "Sinnoh.exe", new[] { "-screen-fullscreen", "0" });
            Assert.That(file, Is.EqualTo(Path.Combine(dir, "_update", "0.2.0", "plan.json")));
            Assert.That(File.ReadAllText(file), Is.EqualTo("{\"format\":1,\"version\":\"0.2.0\",\"from\":\"0.1.1\",\"pid\":1234,\"exe\":\"Sinnoh.exe\"," +
                "\"put\":[{\"path\":\"Sinnoh_Data/Managed/Sinnoh.Client.dll\",\"sha256\":\"" + new string('a', 64) + "\",\"size\":123}]," +
                "\"delete\":[\"Sinnoh_Data/StreamingAssets/content/old\"],\"args\":[\"-screen-fullscreen\",\"0\"]}"), "K3 plan.json format 1");
            var latest = File.ReadAllBytes(Path.Combine(dir, "_update", "0.2.0", "latest.json"));
            Assert.That(latest, Is.EqualTo(new System.Text.UTF8Encoding(false).GetBytes(m.Text)), "받은 글 그대로(BOM 없음)");
        }

        [Test]
        public void M10_넘기기_인자따옴표_공백_따옴표_끝역슬래시()
        {
            Assert.That(UpdateHandoff.Quote("plain"), Is.EqualTo("plain"));
            Assert.That(UpdateHandoff.Quote(""), Is.EqualTo("\"\""));
            Assert.That(UpdateHandoff.Quote(@"C:\Legends of Sinnoh"), Is.EqualTo("\"C:\\Legends of Sinnoh\""));
            Assert.That(UpdateHandoff.Quote(@"C:\a b\"), Is.EqualTo("\"C:\\a b\\\\\""), "끝 역슬래시는 두 배");
            Assert.That(UpdateHandoff.Quote("say \"hi\""), Is.EqualTo("\"say \\\"hi\\\"\""));
            Assert.That(UpdateHandoff.Quote(@"a\\""b"), Is.EqualTo("\"a\\\\\\\\\\\"b\""), "따옴표 앞 역슬래시는 두 배 + 1");
            Assert.That(UpdateHandoff.Quote(@"C:\x\"), Is.EqualTo(@"C:\x\"), "공백이 없으면 그대로");
            Assert.That(UpdateHandoff.Args(@"C:\Games\Legends of Sinnoh", @"C:\Games\Legends of Sinnoh\_update\0.2.0\plan.json"),
                Is.EqualTo("--install \"C:\\Games\\Legends of Sinnoh\" --plan \"C:\\Games\\Legends of Sinnoh\\_update\\0.2.0\\plan.json\""));
        }

        [Test]
        public void M10_넘기기_run도우미는새manifest의것_같은지문이면설치된것()
        {
            var m = new ReleaseManifest { Version = "0.2.0" };
            m.Files.Add(new ReleaseFile { Path = UpdatePlan.Helper, Size = 3, Sha256 = new string('b', 64) });
            File.WriteAllText(Path.Combine(dir, UpdatePlan.Helper), "old");
            string stage = StageStore.Dir(dir);
            Directory.CreateDirectory(stage);
            File.WriteAllText(StageStore.FileOf(stage, m.Files[0].Sha256), "new");
            var same = new List<LocalFile> { new LocalFile { Path = UpdatePlan.Helper, Size = 3, Sha256 = m.Files[0].Sha256 } };
            string exe = UpdateHandoff.RunHelper(dir, m, same, stage, out string why);
            Assert.That(exe, Is.EqualTo(Path.Combine(dir, "_update", "run", UpdatePlan.Helper)), why);
            Assert.That(File.ReadAllText(exe), Is.EqualTo("old"), "지문이 같으면 설치된 것을 복사");
            var differ = new List<LocalFile> { new LocalFile { Path = UpdatePlan.Helper, Size = 3, Sha256 = new string('c', 64) } };
            Assert.That(File.ReadAllText(UpdateHandoff.RunHelper(dir, m, differ, stage, out why)), Is.EqualTo("new"), "다르면 받아 둔 것");
            Assert.That(File.Exists(StageStore.FileOf(stage, m.Files[0].Sha256)), Is.True, "stage의 것은 남긴다(도우미가 설치에 넣는다)");
            File.Delete(StageStore.FileOf(stage, m.Files[0].Sha256));
            Assert.That(UpdateHandoff.RunHelper(dir, m, differ, stage, out why), Is.Null);
            Assert.That(why, Is.EqualTo("missing"));
        }

        [Test]
        public void M10_넘기기_켤때결과정리_성공은모두지움_실패는stage남김_되돌림실패는그대로()
        {
            string root = Path.Combine(dir, "_update");
            void Lay()
            {
                foreach (var d in new[] { "stage", "run", "0.2.0/backup", "0.1.9" }) Directory.CreateDirectory(Path.Combine(root, d));
                File.WriteAllText(Path.Combine(root, "stage", new string('a', 64)), "x");
                File.WriteAllText(Path.Combine(root, "hashes.json"), "{}");
                File.WriteAllText(Path.Combine(root, "updater.log"), "log");
            }

            Lay();
            File.WriteAllText(Path.Combine(root, "result.json"), "{\"format\":1,\"version\":\"0.2.0\",\"from\":\"0.1.1\",\"ok\":true,\"why\":\"\"}");
            var r = UpdateHandoff.ReadResult(dir);
            Assert.That((r.Ok, r.Version, r.From), Is.EqualTo((true, "0.2.0", "0.1.1")));
            UpdateHandoff.Tidy(dir, "0.2.0", r);
            Assert.That(Directory.GetFileSystemEntries(root), Is.EquivalentTo(new[] { Path.Combine(root, "hashes.json"), Path.Combine(root, "updater.log") }), "성공 — 캐시 · 로그만 남는다");

            Lay();
            File.WriteAllText(Path.Combine(root, "result.json"), "{\"format\":1,\"version\":\"0.2.0\",\"from\":\"0.1.1\",\"ok\":false,\"why\":\"share\"}");
            r = UpdateHandoff.ReadResult(dir);
            UpdateHandoff.Tidy(dir, "0.1.1", r);
            Assert.That(Directory.Exists(Path.Combine(root, "stage")), Is.True, "실패 — 받아 둔 것은 남긴다(다시 하면 이어서)");
            Assert.That(Directory.Exists(Path.Combine(root, "0.2.0")) || Directory.Exists(Path.Combine(root, "run")) || File.Exists(Path.Combine(root, "result.json")), Is.False);

            Lay();
            File.WriteAllText(Path.Combine(root, "result.json"), "{\"format\":1,\"version\":\"0.2.0\",\"from\":\"0.1.1\",\"ok\":false,\"why\":\"rollback\"}");
            UpdateHandoff.Tidy(dir, "0.1.1", UpdateHandoff.ReadResult(dir));
            Assert.That(Directory.Exists(Path.Combine(root, "0.2.0", "backup")) && File.Exists(Path.Combine(root, "result.json")), Is.True, "되돌림 실패 — backup을 지우지 않는다");

            File.WriteAllText(Path.Combine(root, "result.json"), "{망가짐");
            Assert.That(UpdateHandoff.ReadResult(dir), Is.Null);
            UpdateHandoff.Tidy(dir, "0.1.1", null);
            Assert.That((Directory.Exists(Path.Combine(root, "run")), Directory.Exists(Path.Combine(root, "stage"))), Is.EqualTo((false, true)), "결과가 없으면 run/만");
        }

        [Test]
        public void M10_넘기기_우리설치판정_쓰기판정_뮤텍스이름()
        {
            string data = Path.Combine(dir, "Sinnoh_Data");
            Directory.CreateDirectory(data);
            Assert.That(UpdateHandoff.InstallOf(data, "0.1.0"), Is.Null, "Sinnoh.exe 없음");
            File.WriteAllText(Path.Combine(dir, "Sinnoh.exe"), "");
            Assert.That(UpdateHandoff.InstallOf(data, "0.1.0"), Is.Null, "version.txt 없음");
            File.WriteAllText(Path.Combine(dir, "version.txt"), "0.1.0\r\n");
            Assert.That(UpdateHandoff.InstallOf(data, "0.1.0"), Is.EqualTo(Path.GetFullPath(dir)));
            Assert.That(UpdateHandoff.InstallOf(data, "0.1.1"), Is.Null, "다른 판의 version.txt");
            Assert.That(UpdateHandoff.InstallOf(data, "0.1.0", windows: Path.GetDirectoryName(dir)), Is.Null, "Windows 폴더 아래");
            Assert.That(UpdateHandoff.InstallOf(Path.GetPathRoot(dir) + "Sinnoh_Data", "0.1.0"), Is.Null, "드라이브 뿌리");

            Assert.That(UpdateHandoff.CanWrite(dir), Is.True);
            Assert.That(Directory.GetFiles(Path.Combine(dir, "_update")), Is.Empty, "판정 파일을 남기지 않는다");
            Assert.That(UpdateHandoff.FreeBytes(dir), Is.GreaterThan(0));

            string name = UpdateHandoff.MutexName(dir);
            Assert.That(name, Does.Match(@"^Local\\SinnohUpdate-[0-9a-f]{16}$"));
            Assert.That(UpdateHandoff.MutexName(dir.ToUpperInvariant() + "\\"), Is.EqualTo(name), "대소문자 · 끝 구분자와 무관");
            Assert.That(UpdateHandoff.MutexName(dir + "x"), Is.Not.EqualTo(name));
        }
    }
}
