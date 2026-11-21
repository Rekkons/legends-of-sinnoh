using System;
using System.Text;
using NUnit.Framework;
using Sinnoh.Data;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>새 판 알림(결정 148 ① · 151): Ed25519 서명 확인 · 판 번호 견주기 · latest.json 읽기. 서명 견본은 Node 포장 도구(<c>Tools/release/pack.mjs</c> signLatest)가 RFC 8032 시험 키로 만든 것.</summary>
    public sealed class ReleaseFeedTests
    {
        // RFC 8032 §7.1 시험 1: 시드 9d61b19d… 의 공개 키 · 빈 글의 서명
        static readonly byte[] RfcKey = Hex("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        static readonly byte[] RfcSig = Hex("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");

        const string Manifest = "{\"app\":\"sinnoh\",\"version\":\"0.2.0\",\"date\":\"2026-11-21\",\"page\":\"https://github.com/Rekkons/legends-of-sinnoh/releases\",\"base\":\"\",\"packages\":[{\"path\":\"Sinnoh-0.2.0-setup.exe\",\"size\":10,\"sha256\":\"ab\"}],\"files\":[{\"path\":\"Sinnoh.exe\",\"size\":3,\"sha256\":\"cd\"},{\"path\":\"Sinnoh_Data/글꼴.txt\",\"size\":5,\"sha256\":\"ef\"}]}";
        const string Signature = "QHFHog0PgXaNyUVLYgSrorFH2tWqd2Aom+4E+nqmao0aHsrHMX68vVvBzBD83OLYuSnFsE/7oyj7t1mMu0xBAg==";

        static string Latest(string manifest = Manifest, string sig = Signature, int format = 1) =>
            "{\"format\":" + format + ",\"manifest\":\"" + manifest.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",\"signature\":\"" + sig + "\"}";

        [Test]
        public void M10_Ed25519_RFC8032시험1과_고친글은틀림()
        {
            Assert.That(Ed25519.Verify(RfcKey, new byte[0], RfcSig), Is.True, "RFC 8032 시험 1");
            Assert.That(Ed25519.Verify(RfcKey, new byte[] { 0 }, RfcSig), Is.False, "글이 다르면");
            var flipped = (byte[])RfcSig.Clone();
            flipped[5] ^= 1;
            Assert.That(Ed25519.Verify(RfcKey, new byte[0], flipped), Is.False, "서명 한 비트");
            var key2 = (byte[])RfcKey.Clone();
            key2[0] ^= 1;
            Assert.That(Ed25519.Verify(key2, new byte[0], RfcSig), Is.False, "다른 키");
            var bigS = (byte[])RfcSig.Clone();
            for (int i = 32; i < 64; i++) bigS[i] = 0xFF;
            Assert.That(Ed25519.Verify(RfcKey, new byte[0], bigS), Is.False, "S ≥ L");
            Assert.That(Ed25519.Verify(new byte[31], new byte[0], RfcSig), Is.False, "키 길이");
            Assert.That(Ed25519.Verify(RfcKey, new byte[0], new byte[63]), Is.False, "서명 길이");
            Assert.That(Ed25519.Verify(RfcKey, Encoding.UTF8.GetBytes(Manifest), Convert.FromBase64String(Signature)), Is.True, "Node가 서명한 manifest(한글 경로 — UTF-8 바이트 그대로)");
        }

        [Test]
        public void M10_판번호_꼴과견주기()
        {
            Assert.That(ReleaseVersion.TryParse("0.9.0", out var v), Is.True);
            Assert.That((v.Major, v.Minor, v.Patch, v.Tail), Is.EqualTo((0, 9, 0, "")));
            foreach (var bad in new[] { null, "", "0.9", "0.9.0.1", "v1.0.0", "1.0.0-", "1.0.x", "1.0.0-한", " 1.0.0" })
                Assert.That(ReleaseVersion.TryParse(bad, out _), Is.False, bad ?? "null");
            Assert.That(ReleaseVersion.Newer("0.10.0", "0.9.9"), Is.True, "수로 견준다(글자가 아니라)");
            Assert.That(ReleaseVersion.Newer("1.0.0", "1.0.0-rc.1"), Is.True, "꼬리 없는 판이 뒤");
            Assert.That(ReleaseVersion.Newer("1.0.0-rc.2", "1.0.0-rc.1"), Is.True);
            Assert.That(ReleaseVersion.Newer("1.0.0", "1.0.0"), Is.False, "같은 판");
            Assert.That(ReleaseVersion.Newer("0.9.0", "0.10.0"), Is.False, "옛 판");
            Assert.That(ReleaseVersion.Newer("2.0.0", "개발판"), Is.False, "내 판이 꼴이 아니면 알리지 않는다");
            Assert.That(ReleaseVersion.TryParse("1.2.3-rc.1", out var t) ? t.ToString() : null, Is.EqualTo("1.2.3-rc.1"));
        }

        [Test]
        public void M10_판정보읽기_서명이맞을때만_새판이면번호()
        {
            var m = ReleaseFeed.Read(Latest(), RfcKey, out string why);
            Assert.That(m, Is.Not.Null, why);
            Assert.That((m.Version, m.Date, m.Packages.Count, m.Files.Count), Is.EqualTo(("0.2.0", "2026-11-21", 1, 2)));
            Assert.That((m.Files[1].Path, m.Files[1].Size, m.Files[1].Sha256), Is.EqualTo(("Sinnoh_Data/글꼴.txt", 5L, "ef")));
            Assert.That(ReleaseFeed.Notice(Latest(), RfcKey, "0.1.0", out _), Is.EqualTo("0.2.0"));
            Assert.That(ReleaseFeed.Notice(Latest(), RfcKey, "0.2.0", out why), Is.Null);
            Assert.That(why, Is.EqualTo("same"));
            Assert.That(ReleaseFeed.Notice(Latest(), RfcKey, "0.3.0", out _), Is.Null, "옛 판은 알리지 않는다");

            // 실패는 예외 없이 null + 까닭
            Assert.That(ReleaseFeed.Read(Latest(Manifest.Replace("0.2.0", "9.9.9")), RfcKey, out why), Is.Null);
            Assert.That(why, Is.EqualTo("signature"), "고친 manifest");
            Assert.That(ReleaseFeed.Read(Latest(), ReleaseFeed.Key, out why), Is.Null);
            Assert.That(why, Is.EqualTo("signature"), "우리 키가 아닌 서명");
            Assert.That(ReleaseFeed.Read(Latest(format: 2), RfcKey, out why), Is.Null);
            Assert.That(why, Is.EqualTo("format"));
            Assert.That(ReleaseFeed.Read(Latest(sig: "!!"), RfcKey, out why), Is.Null);
            Assert.That(why, Is.EqualTo("parse"), "base64가 아님");
            foreach (var junk in new[] { null, "", "<html>404</html>", "{}", "[1,2]" })
                Assert.That(ReleaseFeed.Read(junk, RfcKey, out _), Is.Null, junk ?? "null");
        }

        [Test]
        public void M10_판정보설정값_공개키32바이트_받기는GitHub()
        {
            Assert.That(ReleaseFeed.Key.Length, Is.EqualTo(32), "PublicKey = Ed25519 공개 키(pack.mjs --public-key와 같아야 한다)");
            Assert.That(ReleaseFeed.Page, Does.StartWith("https://github.com/Rekkons/legends-of-sinnoh/"), "결정 150: 받기 링크는 GitHub Releases에만");
            Assert.That(ReleaseFeed.Url, Is.Empty.Or.StartWith("https://"), "판 정보 주소 — R2가 정해지기 전에는 비어 있다");
        }

        static byte[] Hex(string s)
        {
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }
    }
}
