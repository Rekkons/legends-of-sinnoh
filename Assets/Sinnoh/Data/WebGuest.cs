using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Sinnoh.Data
{
    /// <summary>웹 손님 페이지 · 터널의 알림 코드(02 §2-5 — 문구는 screens.json <c>screen.net.fail.*</c>). 방은 그대로 열리고 웹 링크만 없다.</summary>
    public static class WebFail
    {
        /// <summary>웹 링크(cloudflared 임시 터널)를 만들지 못함 · 터널이 끊김. PC판 친구는 코드로.</summary>
        public const string Tunnel = "NET_TUNNEL";
        /// <summary>실행 파일 옆 <c>WebGL/</c>(웹 손님판)이 없다. PC판 친구는 코드로.</summary>
        public const string NoWebGl = "NET_NO_WEBGL";
        /// <summary>cloudflared.exe가 없다(빌드 옆 · Tools/bin · winget 설치 자리, 결정 91).</summary>
        public const string NoCloudflared = "NET_NO_CLOUDFLARED";
    }

    /// <summary>손님에게 줄 웹 링크(02 §2-1, isekai OnlineLinks를 옮김 — 직접 연결 페이지를 더함).</summary>
    public static class OnlineLinks
    {
        /// <summary>웹 손님 페이지 서버의 첫 포트(02 §2-2 — 차 있으면 8081~8089).</summary>
        public const int PagePort = 8080;

        /// <summary>Relay 방: <c>https://페이지/?code=참가 코드</c>.</summary>
        public static string CodeLink(string pageUrl, string code) => Base(pageUrl) + "?code=" + Uri.EscapeDataString(code);

        /// <summary>게임도 터널로 연 방(Relay 실패 때 두 번째 터널): <c>https://페이지/?join=게임 터널 호스트</c>.</summary>
        public static string JoinLink(string pageUrl, string gameHost) => Base(pageUrl) + "?join=" + Uri.EscapeDataString(gameHost);

        /// <summary>
        /// [직접 연결] 방의 웹 링크: <c>http://주소:페이지 포트/</c> — 페이지가 그 주소로 <c>ws://주소:7777</c>을 채운다(<see cref="JoinTarget.FromPage"/>).
        /// 게임 포트가 7777이 아니면 <c>?port=게임 포트</c>를 붙인다.
        /// </summary>
        public static string DirectPage(string address, int pagePort, int gamePort)
        {
            if (string.IsNullOrEmpty(address)) throw new ArgumentException("주소가 없다", nameof(address));
            var s = "http://" + address + ":" + pagePort.ToString(CultureInfo.InvariantCulture) + "/";
            return gamePort == JoinTarget.DefaultPort ? s : s + "?port=" + gamePort.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>https 주소의 호스트(<c>https://abc.trycloudflare.com</c> → <c>abc.trycloudflare.com</c>). 읽을 수 없으면 null.</summary>
        public static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

        static string Base(string pageUrl)
        {
            if (string.IsNullOrEmpty(pageUrl)) throw new ArgumentException("페이지 주소가 없다", nameof(pageUrl));
            return pageUrl.EndsWith("/", StringComparison.Ordinal) ? pageUrl : pageUrl + "/";
        }
    }

    public enum TunnelState { Starting, Ready, Failed, Stopped }

    /// <summary>임시 터널 하나(Net의 CloudflaredTunnel — 시험은 가짜). 시각은 <see cref="Tick"/>으로만 들어온다.</summary>
    public interface ITunnel : IDisposable
    {
        TunnelState State { get; }
        /// <summary>받은 주소 <c>https://….trycloudflare.com</c>(Ready 전에는 null).</summary>
        string Url { get; }
        void Tick(long nowMs);
    }

    /// <summary>
    /// [인터넷] 방의 웹 손님 몫(Net의 GuestWeb — 시험은 가짜): 페이지 서버(루프백) + 페이지 터널, Relay가 실패하면 게임을 루프백에 열고 두 번째 터널(02 §2-1 · §2-2).
    /// </summary>
    public interface IGuestWeb : IDisposable
    {
        /// <summary>페이지를 낼 수 없는 까닭(<see cref="WebFail.NoWebGl"/> · <see cref="WebFail.NoCloudflared"/>). null = 낼 수 있다.</summary>
        string PageMissing { get; }
        /// <summary>터널 도구(cloudflared)가 있다 — Relay 실패 때 게임 터널을 쓸 수 있다.</summary>
        bool CanTunnel { get; }
        /// <summary>페이지 서버를 루프백에 열고 그 앞에 터널을 띄운다. 서버를 못 열면 Failed 터널.</summary>
        ITunnel OpenPage();
        /// <summary>게임을 루프백(127.0.0.1)에 연다(두 번째 터널만 받는다). null = 열림, 아니면 실패 코드.</summary>
        string StartGameLoopback();
        /// <summary>루프백 게임 포트 앞에 두 번째 터널.</summary>
        ITunnel OpenGame();
    }

    /// <summary>
    /// cloudflared 출력 읽기 · 상태(isekai CloudflaredTunnel의 순수 몫 — 프로세스는 Net): 표준 출력·오류의 줄(<see cref="Scan"/> — 다른 스레드에서 와도 된다)에서
    /// <c>https://….trycloudflare.com</c>을 찾으면 Ready, 30초 안에 못 찾거나 프로세스가 끝나면 Failed. Ready 뒤에 끝나도 Failed(주소가 사라짐).
    /// </summary>
    public sealed class TunnelWatch
    {
        public const long TimeoutMs = 30000;

        static readonly Regex UrlPattern = new Regex(@"https://([a-z0-9-]+)\.trycloudflare\.com", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        readonly object gate = new object();
        string found;
        bool exited;
        long? startedAt;

        public TunnelState State { get; private set; }
        public string Url { get; private set; }

        /// <summary>출력 한 줄에서 터널 주소를 찾는다. 없으면 null. API 주소(<c>api.trycloudflare.com</c> — 오류 메시지에 나옴)는 터널 주소가 아니다.</summary>
        public static string FindUrl(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            for (var m = UrlPattern.Match(line); m.Success; m = m.NextMatch())
                if (!string.Equals(m.Groups[1].Value, "api", StringComparison.OrdinalIgnoreCase)) return m.Value.ToLowerInvariant();
            return null;
        }

        /// <summary>cloudflared 인자: 그 루프백 포트의 http 앞에 임시 터널(자동 갱신 끔).</summary>
        public static string Arguments(int port) => "tunnel --no-autoupdate --url http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);

        public void Scan(string line)
        {
            var url = FindUrl(line);
            if (url == null) return;
            lock (gate) if (found == null) found = url;
        }

        public void Exited()
        {
            lock (gate) exited = true;
        }

        /// <summary>시작하지 못했다(실행 파일 없음 · 프로세스 시작 실패).</summary>
        public void FailNow() => State = TunnelState.Failed;

        public void Stop()
        {
            if (State != TunnelState.Failed) State = TunnelState.Stopped;
        }

        /// <summary>매 프레임. 상태가 바뀌었으면 true(Failed가 되면 부르는 쪽이 프로세스를 끝낸다).</summary>
        public bool Tick(long nowMs)
        {
            if (State == TunnelState.Failed || State == TunnelState.Stopped) return false;
            if (!startedAt.HasValue) startedAt = nowMs;
            string url;
            bool gone;
            lock (gate) { url = found; gone = exited; }
            if (State == TunnelState.Starting && url != null)
            {
                Url = url;
                State = TunnelState.Ready;
                return true;
            }
            if (gone || (State == TunnelState.Starting && nowMs - startedAt.Value >= TimeoutMs))
            {
                State = TunnelState.Failed;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 웹 손님 페이지 서버의 순수 몫(isekai WebGuestServer · Tools/serve-web.mjs와 같은 규칙 — 소켓은 Net의 WebGuestServer):
    /// 요청 머리 읽기 · 경로 → 파일 · MIME과 gzip(<c>.gz</c> · gzip인 <c>.unityweb</c> = <c>Content-Encoding: gzip</c> + 원래 확장자의 MIME) · ETag(크기 + 수정 시각) · 조건부 요청(304).
    /// 시각은 유닉스 ms로 받는다(Data는 시계를 읽지 않는다).
    /// </summary>
    public static class WebFiles
    {
        /// <summary>손님판 폴더 이름(실행 파일 옆 — 02 §2-2).</summary>
        public const string FolderName = "WebGL";
        /// <summary>방(이 서버)이 살아 있나 — 파일과 상관없이 200 「ok」 + no-store.</summary>
        public const string AlivePath = "/alive";
        public const string AliveBody = "ok";
        /// <summary>요청 머리 최대 바이트(넘으면 431).</summary>
        public const int MaxHead = 8192;

        static readonly Dictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8", [".js"] = "application/javascript", [".css"] = "text/css; charset=utf-8",
            [".json"] = "application/json; charset=utf-8", [".wasm"] = "application/wasm", [".data"] = "application/octet-stream",
            [".symbols.json"] = "application/json; charset=utf-8", [".png"] = "image/png", [".jpg"] = "image/jpeg", [".ico"] = "image/x-icon",
            [".svg"] = "image/svg+xml", [".txt"] = "text/plain; charset=utf-8", [".md"] = "text/plain; charset=utf-8",[".webmanifest"] = "application/manifest+json",
        };

        /// <summary>손님판 폴더(index.html이 있는 곳): <c>appRoot/WebGL</c>, 에디터면 그것 또는 <c>appRoot/Builds/WebGL</c>(BoardBuild의 출력). 없으면 null.</summary>
        public static string FindRoot(string appRoot, bool editor)
        {
            if (string.IsNullOrEmpty(appRoot)) return null;
            var list = new List<string> { Path.Combine(appRoot, FolderName) };
            if (editor) list.Add(Path.Combine(appRoot, "Builds", FolderName));
            foreach (var dir in list)
                if (File.Exists(Path.Combine(dir, "index.html"))) return Path.GetFullPath(dir);
            return null;
        }

        /// <summary>요청 머리(첫 줄 + 머리 줄, 빈 줄 앞까지): method · 경로(RawUrl) · 머리(이름 소문자). 틀리면 false.</summary>
        public static bool TryParseHead(string text, out string method, out string target, out Dictionary<string, string> headers)
        {
            method = target = null;
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return false;
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length != 3 || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal) || first[0].Length == 0 || first[1].Length == 0) return false;
            method = first[0];
            target = first[1];
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) break;
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) return false;
                headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            return true;
        }

        /// <summary>요청 경로가 생존 확인 경로인가(쿼리·조각은 무시).</summary>
        public static bool IsAlive(string rawUrl)
        {
            if (string.IsNullOrEmpty(rawUrl)) return false;
            int q = rawUrl.IndexOfAny(new[] { '?', '#' });
            return string.Equals(q < 0 ? rawUrl : rawUrl.Substring(0, q), AlivePath, StringComparison.Ordinal);
        }

        /// <summary>요청 경로(쿼리 포함 가능) → root 안의 파일 경로. 200 = path에 있음, 400 = 읽을 수 없는 경로, 403 = root 밖.</summary>
        public static int Resolve(string root, string rawUrl, out string path)
        {
            path = null;
            if (string.IsNullOrEmpty(rawUrl) || rawUrl[0] != '/') return 400;
            int q = rawUrl.IndexOfAny(new[] { '?', '#' });
            string rel;
            try { rel = Uri.UnescapeDataString(q < 0 ? rawUrl : rawUrl.Substring(0, q)).TrimStart('/', '\\'); }
            catch (Exception) { return 400; }
            var full = Path.GetFullPath(root);
            string p;
            try { p = Path.GetFullPath(Path.Combine(full, rel)); }
            catch (Exception) { return 400; }
            var top = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(p.TrimEnd(Path.DirectorySeparatorChar), top, StringComparison.OrdinalIgnoreCase)
                && !p.StartsWith(top + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 403;
            path = p;
            return 200;
        }

        /// <summary>파일 검증 값 <c>"크기16진-수정시각(유닉스 ms)16진"</c> — serve-web.mjs와 같은 식(st.size, Math.floor(st.mtimeMs)).</summary>
        public static string ETagOf(long length, long mtimeMs) =>
            "\"" + length.ToString("x", CultureInfo.InvariantCulture) + "-" + mtimeMs.ToString("x", CultureInfo.InvariantCulture) + "\"";

        /// <summary>
        /// 조건부 요청이 지금 파일과 맞나(맞으면 304). If-None-Match가 있으면 그것만 본다(쉼표 목록 · <c>*</c> · 약한 표시 <c>W/</c>),
        /// 없으면 If-Modified-Since(유닉스 ms — 머리를 읽은 쪽이 바꾼다, 못 읽으면 null)가 파일 수정 시각(초 단위로 자름) 이후인가.
        /// </summary>
        public static bool IsNotModified(string ifNoneMatch, long? ifModifiedSinceMs, string etag, long mtimeMs)
        {
            if (!string.IsNullOrEmpty(ifNoneMatch))
            {
                foreach (var part in ifNoneMatch.Split(','))
                {
                    var t = part.Trim();
                    if (t.StartsWith("W/", StringComparison.Ordinal)) t = t.Substring(2);
                    if (t == "*" || t == etag) return true;
                }
                return false;
            }
            if (!ifModifiedSinceMs.HasValue) return false;
            return mtimeMs / 1000 * 1000 <= ifModifiedSinceMs.Value;
        }

        public struct ContentInfo
        {
            public string Type;
            public bool Gzip;
        }

        /// <summary>
        /// 보낼 MIME과 gzip 여부: <c>.gz</c>는 gzip, <c>.unityweb</c>는 첫 2바이트가 1f 8b일 때만 gzip(압축 해제 대체가 켜진 빌드).
        /// MIME은 압축 확장자를 뗀 원래 이름으로 고르고, 모르는 확장자(번들 — 확장자 없음)는 application/octet-stream.
        /// </summary>
        public static ContentInfo Describe(string path, byte[] head)
        {
            var name = path ?? "";
            bool gzip = false;
            if (name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) { name = name.Substring(0, name.Length - 3); gzip = true; }
            else if (name.EndsWith(".unityweb", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 9);
                gzip = head != null && head.Length >= 2 && head[0] == 0x1f && head[1] == 0x8b;
            }
            var ext = name.EndsWith(".symbols.json", StringComparison.OrdinalIgnoreCase) ? ".symbols.json" : Path.GetExtension(name);
            return new ContentInfo { Type = !string.IsNullOrEmpty(ext) && Types.TryGetValue(ext, out var t) ? t : "application/octet-stream", Gzip = gzip };
        }
    }
}
