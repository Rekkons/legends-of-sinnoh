using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>받기 계획(결정 161 4-1 — 순수): 무엇을 바꾸고 받고 지우나 · zip으로 바꾸는 문턱 · 거부 까닭.</summary>
    public sealed class UpdatePlanTests
    {
        static string Sha(char c) => new string(c, 64);

        static ReleaseFile F(string path, long size, char sha) => new ReleaseFile { Path = path, Size = size, Sha256 = Sha(sha) };

        static LocalFile L(string path, long size, char? sha) => new LocalFile { Path = path, Size = size, Sha256 = sha.HasValue ? Sha(sha.Value) : null };

        static ReleaseManifest Manifest(params ReleaseFile[] files)
        {
            var m = new ReleaseManifest { Version = "0.2.0", Store = "https://dl.example/files/", Base = "https://dl.example/releases/0.2.0/", Auto = true };
            m.Files.Add(F(UpdatePlan.Exe, 10, 'e'));
            m.Files.Add(F(UpdatePlan.Helper, 20, 'f'));
            m.Files.AddRange(files);
            m.Packages.Add(new ReleaseFile { Path = "Sinnoh-0.2.0-win64.zip", Size = 999, Sha256 = Sha('9') });
            return m;
        }

        static readonly LocalFile[] Base = { L(UpdatePlan.Exe, 10, 'e'), L(UpdatePlan.Helper, 20, 'f') };

        static List<LocalFile> With(params LocalFile[] more)
        {
            var l = new List<LocalFile>(Base);
            l.AddRange(more);
            return l;
        }

        static UpdatePlan Make(ReleaseManifest m, List<LocalFile> local, string[] disk = null, ICollection<string> staged = null) =>
            UpdatePlan.Make(m, "0.1.0", local, disk ?? new string[0], staged ?? new HashSet<string>());

        [Test]
        public void M10_계획_크기만다름_지문만다름_같음_새파일()
        {
            var m = Manifest(F("Sinnoh_Data/a", 5, 'a'), F("Sinnoh_Data/b", 6, 'b'), F("Sinnoh_Data/c", 7, 'c'), F("Sinnoh_Data/new", 8, 'd'));
            var plan = Make(m, With(L("Sinnoh_Data/a", 4, null), L("Sinnoh_Data/b", 6, '0'), L("Sinnoh_Data/c", 7, 'c')));
            Assert.That(plan.Why, Is.Null);
            Assert.That(plan.Put.ConvertAll(p => p.Path), Is.EqualTo(new[] { "Sinnoh_Data/a", "Sinnoh_Data/b", "Sinnoh_Data/new" }), "크기 다름 · 지문 다름 · 새 파일 — 같은 것은 빠진다");
            Assert.That(plan.Fetch.Count, Is.EqualTo(3));
            Assert.That(plan.Bytes, Is.EqualTo(5 + 6 + 8));
            Assert.That((plan.UseZip, plan.Delete.Count, plan.Version, plan.From), Is.EqualTo((false, 0, "0.2.0", "0.1.0")));
            Assert.That(plan.Disk, Is.EqualTo(19 + UpdatePlan.Margin));
            Assert.That(UpdatePlan.UrlOf(m, plan.Fetch[0], false), Is.EqualTo("https://dl.example/files/" + Sha('a')), "받을 주소 = store + sha(K2)");
        }

        [Test]
        public void M10_계획_지울파일은자리안의새manifest에없는것만()
        {
            var m = Manifest(F("Sinnoh_Data/keep", 1, 'a'));
            var plan = Make(m, With(L("Sinnoh_Data/keep", 1, 'a')), new[] { "Sinnoh_Data/keep", "Sinnoh_Data/old.bin", "WebGL/Build/x.gz", "SINNOH_DATA/KEEP" });
            Assert.That(plan.Delete, Is.EqualTo(new[] { "Sinnoh_Data/old.bin", "WebGL/Build/x.gz" }), "대소문자만 다른 같은 파일은 남긴다");
            var top = Make(m, With(L("Sinnoh_Data/keep", 1, 'a')), new[] { "notes.txt", "CREDITS.md", "unins000.exe" });
            Assert.That(top.Delete, Is.Empty, "맨 위 파일은 지우지 않는다");
        }

        [Test]
        public void M10_계획_같은지문두경로는한번_stage에있으면안받음()
        {
            var m = Manifest(F("Sinnoh_Data/a", 5, 'a'), F("WebGL/a", 5, 'a'), F("Sinnoh_Data/b", 6, 'b'));
            var plan = Make(m, With());
            Assert.That(plan.Put.Count, Is.EqualTo(3));
            Assert.That(plan.Fetch.ConvertAll(f => f.Sha256), Is.EqualTo(new[] { Sha('a'), Sha('b') }), "같은 내용은 한 번");
            Assert.That(plan.Bytes, Is.EqualTo(11));
            var staged = Make(m, With(), staged: new HashSet<string> { Sha('a') });
            Assert.That(staged.Put.Count, Is.EqualTo(3), "바꿀 목록은 그대로");
            Assert.That(staged.Fetch.ConvertAll(f => f.Sha256), Is.EqualTo(new[] { Sha('b') }), "받아 둔 지문은 다시 받지 않는다");
        }

        [Test]
        public void M10_계획_받을지문이60을넘으면zip하나()
        {
            var files = new List<ReleaseFile>();
            for (int i = 0; i < UpdatePlan.ZipOver; i++) files.Add(new ReleaseFile { Path = "Sinnoh_Data/f" + i, Size = 1, Sha256 = (i + 1).ToString("x64") });
            var m = Manifest(files.ToArray());
            var sixty = Make(m, new List<LocalFile>(Base));
            Assert.That((sixty.Fetch.Count, sixty.UseZip), Is.EqualTo((60, false)), "60개는 파일마다");
            m.Files.Add(F("Sinnoh_Data/one-more", 1, 'c'));
            var over = Make(m, new List<LocalFile>(Base));
            Assert.That((over.Fetch.Count, over.UseZip, over.Zip?.Path), Is.EqualTo((61, true, "Sinnoh-0.2.0-win64.zip")));
            Assert.That(over.Download, Is.EqualTo(999));
            Assert.That(over.Disk, Is.EqualTo(61 + 999 + UpdatePlan.Margin), "zip + 푼 크기");
            Assert.That(UpdatePlan.UrlOf(m, over.Zip, true), Is.EqualTo("https://dl.example/releases/0.2.0/Sinnoh-0.2.0-win64.zip"));
            m.Base = "";
            Assert.That(Make(m, new List<LocalFile>(Base)).UseZip, Is.False, "base가 없으면 파일마다");
        }

        [Test]
        public void M10_계획거부_auto거짓_store빔_도우미없음_대소문자중복_옛판_주소()
        {
            string Why(System.Action<ReleaseManifest> edit, string local = "0.1.0", bool loopback = false)
            {
                var m = Manifest(F("Sinnoh_Data/a", 1, 'a'));
                edit(m);
                return UpdatePlan.Refuse(m, local, loopback);
            }

            Assert.That(Why(m => { }), Is.Null);
            Assert.That(Why(m => m.Auto = false), Is.EqualTo("manual"));
            Assert.That(Why(m => m.Store = ""), Is.EqualTo("store"));
            Assert.That(Why(m => m.Files.RemoveAll(f => f.Path == UpdatePlan.Helper)), Is.EqualTo("helper"));
            Assert.That(Why(m => m.Files.Add(F("SINNOH_DATA/A", 1, 'b'))), Is.EqualTo("dup"));
            Assert.That(Why(m => { }, "0.2.0"), Is.EqualTo("old"), "같은 판");
            Assert.That(Why(m => { }, "0.3.0"), Is.EqualTo("old"), "옛 판(다운그레이드)");
            Assert.That(Why(m => m.Files.Add(F("_update/x", 1, 'b'))), Is.EqualTo("path"));
            Assert.That(Why(m => m.Files.Add(new ReleaseFile { Path = "Sinnoh_Data/z", Size = 1, Sha256 = "AB" })), Is.EqualTo("sha"));
            Assert.That(Why(m => m.Store = "http://dl.example/files/"), Is.EqualTo("url"), "https만");
            Assert.That(Why(m => m.Store = "https://dl.example/files"), Is.EqualTo("url"), "store는 /로 끝난다");
            Assert.That(Why(m => m.Store = "http://127.0.0.1:5000/files/"), Is.EqualTo("url"), "프로브가 아니면 loopback도 거부");
            Assert.That(Why(m => { m.Store = "http://127.0.0.1:5000/files/"; m.Base = "http://127.0.0.1:5000/releases/0.2.0/"; }, loopback: true), Is.Null, "끝에서 끝 프로브");
            Assert.That(UpdatePlan.Refuse(null, "0.1.0"), Is.EqualTo("feed"));
            var refused = UpdatePlan.Make(Manifest(), "0.2.0", new List<LocalFile>(), new string[0], null);
            Assert.That((refused.Why, refused.Put.Count), Is.EqualTo(("old", 0)), "거부면 아무것도 계획하지 않는다");
        }
    }
}
