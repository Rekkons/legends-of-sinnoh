using System;
using Sinnoh.Data;

namespace Sinnoh.Net
{
    /// <summary>
    /// [인터넷] 방의 예비 길(Data <see cref="IGameTunnel"/> — 02 §2-1): Relay가 실패하면 게임을 127.0.0.1에 열고(<c>startLoopback</c>) 게임 포트 앞에 cloudflared 임시 터널.
    /// cloudflared를 찾는 곳은 부르는 쪽이 준다(null = 없음).
    /// </summary>
    public sealed class GameTunnel : IGameTunnel
    {
        readonly string cloudflared;
        readonly Func<string> startLoopback;
        readonly int gamePort;

        /// <param name="cloudflared">cloudflared.exe(<see cref="CloudflaredTunnel.FindExe"/>). null = 없음(Relay 실패면 터널 없이 실패).</param>
        /// <param name="startLoopback">게임을 127.0.0.1:gamePort에 연다 — null = 열림, 아니면 실패 코드.</param>
        public GameTunnel(string cloudflared, Func<string> startLoopback, int gamePort)
        {
            this.cloudflared = cloudflared;
            this.startLoopback = startLoopback ?? throw new ArgumentNullException(nameof(startLoopback));
            this.gamePort = gamePort;
        }

        public bool CanTunnel => cloudflared != null;

        public string StartGameLoopback() => startLoopback();

        public ITunnel OpenGame() => CloudflaredTunnel.Start(cloudflared, gamePort);

        public void Dispose() { }
    }
}
