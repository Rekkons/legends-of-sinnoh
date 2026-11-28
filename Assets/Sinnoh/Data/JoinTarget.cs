using System;
using System.Globalization;

namespace Sinnoh.Data
{
    public enum JoinKind
    {
        /// <summary>ws:// IPv4 또는 호스트 이름 + 포트(직접 연결 — 같은 공유기 · Tailscale, 02 §2-1).</summary>
        Direct,
        /// <summary>wss://호스트 이름[:포트](터널처럼 TLS를 앞에서 끝내는 곳, 02 §2-1 Relay 실패 때의 터널).</summary>
        Secure,
        /// <summary>Unity Relay 참가 코드(인터넷 방 — Net ②).</summary>
        Relay,
    }

    /// <summary>
    /// 참가 칸 입력 하나를 접속 대상으로 읽는다(02 §2-1 「참가 칸 하나」, isekai Net/Online/JoinTarget을 옮김 — 엔진 무관이라 Data [가정 M7-28]). 받는 것:
    /// <c>wss://호스트[:포트][/]</c>, 참가 코드(영숫자 6자),
    /// IPv4 또는 <c>IPv4:포트</c>(<see cref="TryParseAddress"/>), 호스트 이름 또는 <c>호스트 이름:포트</c>.
    /// 포트를 빼면 직접 연결은 <see cref="DefaultPort"/>, wss는 443. <c>*.trycloudflare.com</c>은 포트를 적지 않으면 wss로 본다. 영숫자 6자 한 단어는 호스트 이름이 아니라 참가 코드로 본다.
    /// </summary>
    public sealed class JoinTarget
    {
        /// <summary>게임 포트(02 §2-3 — TCP WebSocket).</summary>
        public const ushort DefaultPort = 7777;
        public const ushort SecurePort = 443;
        public const int CodeLength = 6;
        public const string TunnelSuffix = ".trycloudflare.com";

        public JoinKind Kind { get; private set; }
        /// <summary>Direct: IPv4 또는 호스트 이름(소문자), Secure: 호스트 이름(소문자), Relay: null.</summary>
        public string Host { get; private set; }
        public ushort Port { get; private set; }
        /// <summary>Relay 참가 코드(대문자). 그 밖에는 null.</summary>
        public string Code { get; private set; }

        public static JoinTarget Direct(string host, ushort port) => new JoinTarget { Kind = JoinKind.Direct, Host = host, Port = port };
        public static JoinTarget Secure(string host, ushort port = SecurePort) => new JoinTarget { Kind = JoinKind.Secure, Host = host, Port = port };
        public static JoinTarget Relay(string code) => new JoinTarget { Kind = JoinKind.Relay, Code = code };

        /// <summary>참가 칸에 다시 넣으면 같은 대상이 되는 글: <c>호스트:포트</c> / <c>wss://호스트</c>(443이면 포트 생략) / 참가 코드.</summary>
        public string Text
        {
            get
            {
                switch (Kind)
                {
                    case JoinKind.Relay: return Code;
                    case JoinKind.Secure: return "wss://" + Host + (Port == SecurePort ? "" : ":" + Port.ToString(CultureInfo.InvariantCulture));
                    default: return Host + ":" + Port.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        public override string ToString() => Text;

        public static bool TryParse(string input, out JoinTarget target)
        {
            target = null;
            var s = input?.Trim();
            if (string.IsNullOrEmpty(s)) return false;
            if (StartsWith(s, "wss://")) return TryParseSecure(s.Substring(6), out target);
            if (IsCode(s))
            {
                target = Relay(s.ToUpperInvariant());
                return true;
            }
            if (TryParseAddress(s, out var ip, out var port))
            {
                target = Direct(ip, port);
                return true;
            }
            if (!SplitHostPort(s, out var host, out var p)) return false;
            if (p == 0) target = host.EndsWith(TunnelSuffix, StringComparison.Ordinal) ? Secure(host) : Direct(host, DefaultPort);
            else target = Direct(host, p);
            return true;
        }

        /// <summary>
        /// 직접 연결 주소 <c>IP</c> 또는 <c>IP:포트</c>(02 §2-1): IPv4 점 4개(앞 0은 지운다) + 포트 1~65535, 포트를 빼면 <see cref="DefaultPort"/>. 틀리면 false(접속 시도 없이 NET_BAD_ADDRESS).
        /// 호스트 이름은 받지 않는다(<see cref="TryParse"/>가 따로 읽는다).
        /// </summary>
        public static bool TryParseAddress(string input, out string ip, out ushort port)
        {
            ip = null;
            port = DefaultPort;
            var s = input?.Trim();
            if (string.IsNullOrEmpty(s)) return false;
            int colon = s.IndexOf(':');
            var host = colon < 0 ? s : s.Substring(0, colon);
            if (colon >= 0)
            {
                if (!int.TryParse(s.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int p) || p < 1 || p > 65535) return false;
                port = (ushort)p;
            }
            var parts = host.Split('.');
            if (parts.Length != 4) return false;
            var octets = new string[4];
            for (int i = 0; i < 4; i++)
            {
                if (parts[i].Length < 1 || parts[i].Length > 3) return false;
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int o) || o > 255) return false;
                octets[i] = o.ToString(CultureInfo.InvariantCulture);
            }
            ip = string.Join(".", octets);
            return true;
        }

        /// <summary>참가 코드 모양인가: 영숫자 6자.</summary>
        public static bool IsCode(string s)
        {
            if (s == null || s.Length != CodeLength) return false;
            foreach (char c in s)
                if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9')) return false;
            return true;
        }

        // wss:// 뒤: 호스트[:포트][/] — 경로는 받지 않는다(NGO WebSocket 경로는 "/")
        static bool TryParseSecure(string rest, out JoinTarget target)
        {
            target = null;
            if (rest.EndsWith("/", StringComparison.Ordinal)) rest = rest.Substring(0, rest.Length - 1);
            if (rest.IndexOf('/') >= 0 || rest.IndexOf('?') >= 0) return false;
            if (!SplitHostPort(rest, out var host, out var port)) return false;
            target = Secure(host, port == 0 ? SecurePort : port);
            return true;
        }

        // 호스트 이름[:포트]. port 0 = 적지 않음. 이름은 DNS 라벨 규칙(1~63자 영숫자·하이픈, 앞뒤 하이픈 없음)이고 모두 숫자인 이름(틀린 IP)은 거절한다
        static bool SplitHostPort(string s, out string host, out ushort port)
        {
            host = null;
            port = 0;
            int colon = s.IndexOf(':');
            var h = colon < 0 ? s : s.Substring(0, colon);
            if (colon >= 0)
            {
                if (!int.TryParse(s.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int p) || p < 1 || p > 65535) return false;
                port = (ushort)p;
            }
            if (h.Length == 0 || h.Length > 253) return false;
            bool allNumeric = true;
            foreach (var label in h.Split('.'))
            {
                if (label.Length < 1 || label.Length > 63 || label[0] == '-' || label[label.Length - 1] == '-') return false;
                foreach (char c in label)
                {
                    if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-')) return false;
                    if (!(c >= '0' && c <= '9')) allNumeric = false;
                }
            }
            if (allNumeric) return false;
            host = h.ToLowerInvariant();
            return true;
        }

        static bool StartsWith(string s, string prefix) => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
