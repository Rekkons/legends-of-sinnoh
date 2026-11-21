#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using Sinnoh.Data;

namespace Sinnoh.Net
{
    /// <summary>
    /// [인터넷] 방의 웹 손님 몫(Data <see cref="IGuestWeb"/> — 02 §2-1 · §2-2): 손님판 폴더(<c>WebGL/</c>)를 루프백 <see cref="WebGuestServer"/>로 내고 그 앞에 cloudflared 임시 터널,
    /// Relay가 실패하면 게임을 127.0.0.1에 열고(<c>startLoopback</c>) 게임 포트 앞에 두 번째 터널. 폴더 · cloudflared를 찾는 곳은 부르는 쪽이 준다(null = 없음).
    /// </summary>
    public sealed class GuestWeb : IGuestWeb
    {
        readonly string webRoot, cloudflared;
        readonly Func<string> startLoopback;
        readonly int gamePort;
        WebGuestServer server;

        /// <param name="webRoot">손님판 폴더(index.html이 있는 곳 — <see cref="WebFiles.FindRoot"/>). null = 없음(NET_NO_WEBGL).</param>
        /// <param name="cloudflared">cloudflared.exe(<see cref="CloudflaredTunnel.FindExe"/>). null = 없음(NET_NO_CLOUDFLARED · Relay 실패면 터널 없이 실패).</param>
        /// <param name="startLoopback">게임을 127.0.0.1:gamePort에 연다 — null = 열림, 아니면 실패 코드.</param>
        public GuestWeb(string webRoot, string cloudflared, Func<string> startLoopback, int gamePort)
        {
            this.webRoot = webRoot;
            this.cloudflared = cloudflared;
            this.startLoopback = startLoopback ?? throw new ArgumentNullException(nameof(startLoopback));
            this.gamePort = gamePort;
        }

        public string PageMissing => webRoot == null ? WebFail.NoWebGl : cloudflared == null ? WebFail.NoCloudflared : null;
        public bool CanTunnel => cloudflared != null;
        /// <summary>페이지 서버(열었으면).</summary>
        public WebGuestServer Server => server;

        public ITunnel OpenPage()
        {
            server?.Dispose();
            server = webRoot != null ? WebGuestServer.Start(webRoot) : null;
            return CloudflaredTunnel.Start(server != null ? cloudflared : null, server != null ? server.Port : 0);
        }

        public string StartGameLoopback() => startLoopback();

        public ITunnel OpenGame() => CloudflaredTunnel.Start(cloudflared, gamePort);

        public void Dispose()
        {
            server?.Dispose();
            server = null;
        }
    }
}
#endif
