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

        // 자동 업데이트 칸(결정 161 K1 — store · auto)이 있는 manifest(같은 RFC 시험 키로 Node가 서명)
        const string AutoManifest = "{\"app\":\"sinnoh\",\"version\":\"0.2.0\",\"date\":\"2026-11-21\",\"page\":\"https://github.com/Rekkons/legends-of-sinnoh/releases\",\"base\":\"https://dl.example/releases/0.2.0/\",\"store\":\"https://dl.example/files/\",\"auto\":true,\"packages\":[],\"files\":[{\"path\":\"Sinnoh.exe\",\"size\":3,\"sha256\":\"cd\"}]}";
        const string AutoSignature = "uLCr9u8n05pI/dnIWkaXRTayPcQAIF7YNn/VSmMmGq4bn21ElYjBdDE3Pxwv8tzMF7rwbUD02ig3E1QoejGKBg==";

        [Test]
        public void M10_판정보읽기_store와auto_옛꼴은자동없음_서명뒤store를고치면틀림()
        {
            string json = Latest(AutoManifest, AutoSignature);
            var m = ReleaseFeed.Read(json, RfcKey, out string why);
            Assert.That(m, Is.Not.Null, why);
            Assert.That((m.Store, m.Auto, m.Base), Is.EqualTo(("https://dl.example/files/", true, "https://dl.example/releases/0.2.0/")));
            Assert.That(m.Text, Is.EqualTo(json), "받은 글 그대로(도우미가 같은 글의 서명을 다시 본다)");

            var old = ReleaseFeed.Read(Latest(), RfcKey, out why);
            Assert.That((old.Store, old.Auto), Is.EqualTo(("", false)), "0.1.0 꼴(칸 없음) = 자동 업데이트 없음");

            Assert.That(ReleaseFeed.Read(Latest(AutoManifest.Replace("dl.example/files", "evil.example/files"), AutoSignature), RfcKey, out why), Is.Null);
            Assert.That(why, Is.EqualTo("signature"), "서명 뒤 store를 고치면");
            Assert.That(ReleaseFeed.Read(Latest(AutoManifest.Replace("\"auto\":true", "\"auto\":false"), AutoSignature), RfcKey, out why), Is.Null);
            Assert.That(why, Is.EqualTo("signature"), "서명 뒤 auto를 고치면");
        }

        // 자동 업데이트 판(결정 161)의 pack.mjs 꼴 그대로(store · auto · files에 도우미 · 들여 쓴 겉 글) — RFC 시험 키로 Node가 서명
        const string NewManifest = "{\"app\":\"sinnoh\",\"version\":\"0.2.0\",\"date\":\"2026-11-21\",\"page\":\"https://github.com/Rekkons/legends-of-sinnoh/releases\",\"base\":\"https://sinnoh-dl.legends-of-sinnoh.workers.dev/releases/0.2.0/\",\"store\":\"https://sinnoh-dl.legends-of-sinnoh.workers.dev/files/\",\"auto\":true,\"packages\":[{\"path\":\"Sinnoh-0.2.0-setup.exe\",\"size\":10,\"sha256\":\"abababababababababababababababababababababababababababababababab\"},{\"path\":\"Sinnoh-0.2.0-win64.zip\",\"size\":11,\"sha256\":\"cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd\"}],\"files\":[{\"path\":\"Sinnoh.exe\",\"size\":3,\"sha256\":\"efefefefefefefefefefefefefefefefefefefefefefefefefefefefefefefef\"},{\"path\":\"SinnohUpdater.exe\",\"size\":50176,\"sha256\":\"2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e\"},{\"path\":\"Sinnoh_Data/글꼴.txt\",\"size\":5,\"sha256\":\"0101010101010101010101010101010101010101010101010101010101010101\"}]}";
        const string NewSignature = "Wg6STtPOuIpq0dgF78kriTZ72z1J7d/EgZmnewQ97aG2EZDpskPiP1/mvrKJNr/TcrOuLX8FwooSy0GpJCbaDA==";

        [Test]
        public void M10_배포한010판파서가_자동업데이트꼴판정보를읽고_새판을알린다()
        {
            // pack.mjs signLatest의 겉 글(JSON.stringify(…, null, 2) + 줄바꿈)
            string json = "{\n  \"format\": 1,\n  \"manifest\": \"" + NewManifest.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",\n  \"signature\": \"" + NewSignature + "\"\n}\n";
            Assert.That(V010.Notice(json, RfcKey, "0.1.0", out string why), Is.EqualTo("0.2.0"), "0.1.0의 알림 규칙: " + why);
            Assert.That(V010.Notice(json, RfcKey, "0.2.0", out why), Is.Null);
            Assert.That(why, Is.EqualTo("same"));
            Assert.That(V010.Notice(json.Replace("\\\"auto\\\":true", "\\\"auto\\\":false"), RfcKey, "0.1.0", out why), Is.Null, "서명 뒤 새 칸을 고치면 0.1.0도 거부");
            Assert.That(why, Is.EqualTo("signature"));
            Assert.That(ReleaseFeed.Notice(json, RfcKey, "0.1.0", out why), Is.EqualTo("0.2.0"), "지금 판의 알림 규칙도 같은 답");
            var m = ReleaseFeed.Read(json, RfcKey, out why);
            Assert.That((m.Store, m.Auto, m.Files.Count, m.Files[1].Path), Is.EqualTo(("https://sinnoh-dl.legends-of-sinnoh.workers.dev/files/", true, 3, "SinnohUpdater.exe")), why);
        }

        /// <summary>
        /// 배포한 0.1.0의 판 정보 읽기(git 79d6dafb <c>Assets/Sinnoh/Data/ReleaseFeed.cs</c> Read · Notice — 고치지 않는다): 이미 깔린 0.1.0이 새 꼴 latest.json을 받아들이는지 보는 기준.
        /// 판 정보 꼴을 바꿀 때 이 사본이 거부하면 0.1.0 사용자는 새 판 알림을 못 받는다(format · manifest · signature · app · version은 그대로 둔다).
        /// </summary>
        static class V010
        {
            public static string Notice(string json, byte[] publicKey, string local, out string why)
            {
                if (!Read(json, publicKey, out why, out string version)) return null;
                if (!ReleaseVersion.Newer(version, local)) { why = "same"; return null; }
                return version;
            }

            static bool Read(string json, byte[] publicKey, out string why, out string version)
            {
                version = null;
                try
                {
                    var outer = Newtonsoft.Json.Linq.JObject.Parse(json ?? "");
                    if ((int?)outer["format"] != 1) { why = "format"; return false; }
                    string manifest = (string)outer["manifest"], sig = (string)outer["signature"];
                    if (string.IsNullOrEmpty(manifest) || string.IsNullOrEmpty(sig)) { why = "fields"; return false; }
                    if (!Ed25519.Verify(publicKey, Encoding.UTF8.GetBytes(manifest), Convert.FromBase64String(sig))) { why = "signature"; return false; }
                    var m = Newtonsoft.Json.Linq.JObject.Parse(manifest);
                    if ((string)m["app"] != "sinnoh") { why = "app"; return false; }
                    version = (string)m["version"];
                    if (!ReleaseVersion.TryParse(version, out _)) { why = "version"; return false; }
                    foreach (var list in new[] { m["packages"], m["files"] })
                        if (list is Newtonsoft.Json.Linq.JArray a)
                            foreach (var f in a) _ = ((string)f["path"], (long?)f["size"] ?? 0, (string)f["sha256"]);
                    why = null;
                    return true;
                }
                catch (Exception e) when (e is Newtonsoft.Json.JsonException || e is FormatException || e is InvalidCastException || e is ArgumentException || e is OverflowException)
                {
                    why = "parse";
                    return false;
                }
            }
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
