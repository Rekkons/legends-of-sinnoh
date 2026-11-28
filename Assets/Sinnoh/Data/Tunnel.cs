using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Sinnoh.Data
{
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
    /// [인터넷] 방의 예비 길(Net의 GameTunnel — 시험은 가짜, 02 §2-1): Relay가 실패하면 게임을 루프백에 열고 그 앞에 cloudflared 임시 터널 — PC판 손님은 <c>wss://터널</c>로 붙는다.
    /// </summary>
    public interface IGameTunnel : IDisposable
    {
        /// <summary>터널 도구(cloudflared)가 있다 — Relay 실패 때 게임 터널을 쓸 수 있다.</summary>
        bool CanTunnel { get; }
        /// <summary>게임을 루프백(127.0.0.1)에 연다(터널만 받는다). null = 열림, 아니면 실패 코드.</summary>
        string StartGameLoopback();
        /// <summary>루프백 게임 포트 앞에 터널.</summary>
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
}
