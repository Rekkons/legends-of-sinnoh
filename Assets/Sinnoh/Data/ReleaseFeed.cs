using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Sinnoh.Data
{
    /// <summary>
    /// 사람이 보는 게임 판 번호(결정 148): <c>주.부.수[-꼬리]</c>(예 0.9.0 · 1.0.0-rc.1). 정본은 프로젝트 설정 bundleVersion(= <c>Application.version</c>) 한 곳 —
    /// 타이틀 아래 띠 · 버그 신고 · 방 접속 인사(BuildId) · 판 정보 파일(<c>Tools/release/pack.mjs</c>)이 같은 값을 쓴다. 프로토콜 판(<c>Protocol.Version</c>) · 세이브 규칙 판과는 다른 수다.
    /// </summary>
    public readonly struct ReleaseVersion : IComparable<ReleaseVersion>
    {
        public readonly int Major, Minor, Patch;
        /// <summary>꼬리(「-」 뒤 — 없으면 ""). 꼬리가 있는 판은 같은 수의 꼬리 없는 판보다 앞이다.</summary>
        public readonly string Tail;

        public ReleaseVersion(int major, int minor, int patch, string tail = "")
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Tail = tail ?? "";
        }

        public static bool TryParse(string text, out ReleaseVersion v)
        {
            v = default;
            if (string.IsNullOrEmpty(text)) return false;
            string core = text, tail = "";
            int dash = text.IndexOf('-');
            if (dash >= 0)
            {
                core = text.Substring(0, dash);
                tail = text.Substring(dash + 1);
                if (tail.Length == 0) return false;
                foreach (char c in tail) if (!(char.IsLetterOrDigit(c) && c < 128) && c != '.') return false;
            }
            var parts = core.Split('.');
            if (parts.Length != 3) return false;
            var n = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (parts[i].Length == 0 || parts[i].Length > 6) return false;
                foreach (char c in parts[i]) if (c < '0' || c > '9') return false;
                n[i] = int.Parse(parts[i], CultureInfo.InvariantCulture);
            }
            v = new ReleaseVersion(n[0], n[1], n[2], tail);
            return true;
        }

        public int CompareTo(ReleaseVersion o)
        {
            int c = Major.CompareTo(o.Major);
            if (c == 0) c = Minor.CompareTo(o.Minor);
            if (c == 0) c = Patch.CompareTo(o.Patch);
            if (c != 0) return c;
            if (Tail.Length == 0) return o.Tail.Length == 0 ? 0 : 1;
            return o.Tail.Length == 0 ? -1 : string.CompareOrdinal(Tail, o.Tail);
        }

        /// <summary>remote가 local보다 새 판인가. 둘 중 하나라도 판 번호 꼴이 아니면 거짓(알리지 않는다).</summary>
        public static bool Newer(string remote, string local) => TryParse(remote, out var r) && TryParse(local, out var l) && r.CompareTo(l) > 0;

        public override string ToString() => Major.ToString(CultureInfo.InvariantCulture) + "." + Minor.ToString(CultureInfo.InvariantCulture) + "." + Patch.ToString(CultureInfo.InvariantCulture) + (Tail.Length > 0 ? "-" + Tail : "");
    }

    /// <summary>판 정보 파일 속 받을 묶음 하나(설치 파일 · zip) 또는 게임 파일 하나.</summary>
    public sealed class ReleaseFile
    {
        public string Path;
        public long Size;
        public string Sha256;
    }

    /// <summary>서명을 확인한 판 정보(<c>latest.json</c>의 manifest).</summary>
    public sealed class ReleaseManifest
    {
        public string Version, Date;
        /// <summary>묶음 자리(<c>&lt;받기 주소&gt;/releases/&lt;판&gt;/</c> — zip 경로가 쓴다). 없으면 "".</summary>
        public string Base = "";
        /// <summary>파일 객체 자리(결정 161 K1 — <c>&lt;받기 주소&gt;/files/</c>, 받을 주소 = Store + sha256). 비면 자동 업데이트 없음([받기]만).</summary>
        public string Store = "";
        /// <summary>이 판을 자동 업데이트로 받아도 되나(K1 — 칸이 없는 0.1.0 꼴 · pack --manual이면 거짓).</summary>
        public bool Auto;
        /// <summary>받은 latest.json 글 그대로(도우미가 같은 글의 서명을 다시 확인한다 — 다시 직렬화하지 않는다).</summary>
        public string Text;
        public readonly List<ReleaseFile> Packages = new List<ReleaseFile>();
        public readonly List<ReleaseFile> Files = new List<ReleaseFile>();
    }

    /// <summary>
    /// 새 판 알림(결정 148 ① · 151): 판 정보 파일 <c>latest.json</c>(Cloudflare R2 — <see cref="Url"/>)을 읽어 우리 키의 Ed25519 서명을 확인한다.
    /// 파일 꼴(<c>Tools/release/pack.mjs</c>가 만든다): <c>{"format":1,"manifest":"&lt;JSON 글&gt;","signature":"&lt;base64&gt;"}</c> — 서명은 manifest 글의 UTF-8 바이트 그대로에 건다(다시 직렬화하지 않는다).
    /// manifest = <c>{"app":"sinnoh","version","date","page","base","store","auto","packages":[{path,size,sha256}],"files":[{path,size,sha256}]}</c>(files = 게임 파일 — 자동 업데이트가 견준다, store · auto = 결정 161 K1 · <c>Update/UpdatePlan</c>).
    /// 실패(꼴 · 서명 · 판 번호)는 예외 없이 null + 까닭 — 화면은 조용히 넘긴다.
    /// </summary>
    public static class ReleaseFeed
    {
        /// <summary>판 정보 파일 주소(받기 Worker — R2 `sinnoh-releases`, 결정 158 · `~/.sinnoh/r2.json` publicBaseUrl + /latest.json과 같아야 pack이 통과). 비어 있으면 게임은 읽지 않는다.</summary>
        public const string Url = "https://sinnoh-dl.legends-of-sinnoh.workers.dev/latest.json";

        /// <summary>[받기]가 여는 곳(결정 150 — 받기 링크는 GitHub README · Releases 페이지에만).</summary>
        public const string Page = "https://github.com/Rekkons/legends-of-sinnoh/releases";

        /// <summary>서명 확인 공개 키(Ed25519 32바이트 base64). 개인 키는 저장소 밖(<c><HOME>\.sinnoh\release-signing.key</c>) — 포장 도구가 이 값과 맞는지 본다.</summary>
        public const string PublicKey = "/joiwzLXVte13Vw9y51xNVnrMhl47qZXGIsxmD6RJW0=";

        public const int Format = 1;
        public const string App = "sinnoh";

        public static byte[] Key => Convert.FromBase64String(PublicKey);

        /// <summary>latest.json 글을 읽어 서명을 확인한다. 틀리면 null과 까닭(why — 로그용, 키 · 글 내용은 싣지 않는다).</summary>
        public static ReleaseManifest Read(string json, byte[] publicKey, out string why)
        {
            try
            {
                var outer = JObject.Parse(json ?? "");
                if ((int?)outer["format"] != Format) { why = "format"; return null; }
                string manifest = (string)outer["manifest"], sig = (string)outer["signature"];
                if (string.IsNullOrEmpty(manifest) || string.IsNullOrEmpty(sig)) { why = "fields"; return null; }
                if (!Ed25519.Verify(publicKey, Encoding.UTF8.GetBytes(manifest), Convert.FromBase64String(sig))) { why = "signature"; return null; }
                var m = JObject.Parse(manifest);
                if ((string)m["app"] != App) { why = "app"; return null; }
                var info = new ReleaseManifest { Version = (string)m["version"], Date = (string)m["date"], Base = (string)m["base"] ?? "", Store = (string)m["store"] ?? "", Auto = (bool?)m["auto"] ?? false, Text = json };
                if (!ReleaseVersion.TryParse(info.Version, out _)) { why = "version"; return null; }
                Files(m["packages"], info.Packages);
                Files(m["files"], info.Files);
                why = null;
                return info;
            }
            catch (Exception e) when (e is Newtonsoft.Json.JsonException || e is FormatException || e is InvalidCastException || e is ArgumentException || e is OverflowException)
            {
                why = "parse";
                return null;
            }
        }

        /// <summary>알릴 새 판 번호: 서명이 맞고 local보다 새 판이면 그 번호, 아니면 null(why = 까닭, 같거나 옛 판이면 "same").</summary>
        public static string Notice(string json, byte[] publicKey, string local, out string why)
        {
            var m = Read(json, publicKey, out why);
            if (m == null) return null;
            if (!ReleaseVersion.Newer(m.Version, local)) { why = "same"; return null; }
            return m.Version;
        }

        static void Files(JToken list, List<ReleaseFile> into)
        {
            if (!(list is JArray a)) return;
            foreach (var f in a) into.Add(new ReleaseFile { Path = (string)f["path"], Size = (long?)f["size"] ?? 0, Sha256 = (string)f["sha256"] });
        }
    }
}
