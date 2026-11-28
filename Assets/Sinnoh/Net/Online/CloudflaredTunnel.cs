using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Sinnoh.Data;
using Debug = UnityEngine.Debug;

namespace Sinnoh.Net
{
    /// <summary>
    /// Cloudflare 임시 터널(Quick Tunnel, 02 §2-1 · §2-2 — isekai Net/Online/CloudflaredTunnel을 옮김): 방장 PC에서 <c>cloudflared.exe tunnel --no-autoupdate --url http://127.0.0.1:포트</c>를 창 없이 띄워
    /// 출력에서 <c>https://….trycloudflare.com</c>을 읽는다(읽기 · 30초 · 끝남 판정은 Data <see cref="TunnelWatch"/>). 원 서버가 TcpListener라 Host 헤더를 바꾸지 않는다(isekai의 --http-host-header는 HttpListener 몫).
    /// - 실행 파일(<see cref="FindExe"/>): 실행 파일 옆 <c>cloudflared.exe</c> → <c>Tools/bin/cloudflared.exe</c>(개발 — <c>node Tools/fetch-cloudflared.mjs</c>, 결정 91: winget <c>Cloudflare.cloudflared</c>) → winget·MSI 설치 자리.
    /// - 수명: <see cref="Dispose"/>(방 닫기)·앱 종료(Application.quitting)에서 프로세스를 끝낸다. 강제 종료로 남은 것은 다음 실행의 첫 <see cref="Start"/>가 같은 경로의 것만 끝낸다(<see cref="CleanupStale"/>).
    /// </summary>
    public sealed class CloudflaredTunnel : ITunnel
    {
        public const string ExeName = "cloudflared.exe";

        static readonly List<CloudflaredTunnel> live = new List<CloudflaredTunnel>();
        static bool cleaned, hooked;

        readonly Process process;
        readonly TunnelWatch watch = new TunnelWatch();
        bool killed;

        CloudflaredTunnel(Process process) => this.process = process;

        public TunnelState State => watch.State;
        public string Url => watch.Url;

        /// <summary>
        /// cloudflared.exe 경로. appRoot = Application.dataPath의 부모(빌드는 실행 파일 폴더, 에디터는 프로젝트 폴더). 없으면 null.
        /// 찾는 곳: appRoot · appRoot/Tools/bin · winget 링크(<c>%LOCALAPPDATA%/Microsoft/WinGet/Links</c>) · MSI 설치(<c>Program Files (x86)/cloudflared</c> · <c>Program Files/cloudflared</c>).
        /// </summary>
        public static string FindExe(string appRoot)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(appRoot))
            {
                list.Add(Path.Combine(appRoot, ExeName));
                list.Add(Path.Combine(appRoot, "Tools", "bin", ExeName));
            }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local)) list.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", ExeName));
            foreach (var pf in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
            {
                var dir = Environment.GetFolderPath(pf);
                if (!string.IsNullOrEmpty(dir)) list.Add(Path.Combine(dir, "cloudflared", ExeName));
            }
            foreach (var p in list)
                if (File.Exists(p)) return Path.GetFullPath(p);
            return null;
        }

        /// <summary>터널을 띄운다. 실행 파일이 없거나 시작하지 못하면 Failed 상태로 돌려준다(예외 없음).</summary>
        public static CloudflaredTunnel Start(string exe, int port)
        {
            if (string.IsNullOrEmpty(exe))
            {
                Debug.LogWarning("[Net] cloudflared.exe가 없다(빌드 옆 · Tools/bin — node Tools/fetch-cloudflared.mjs)");
                var none = new CloudflaredTunnel(null) { killed = true };
                none.watch.FailNow();
                return none;
            }
            if (!cleaned) { cleaned = true; if (Owned(exe)) CleanupStale(exe); }
            if (!hooked) { hooked = true; UnityEngine.Application.quitting += StopAll; }
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(exe, TunnelWatch.Arguments(port))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true,
            };
            var t = new CloudflaredTunnel(p);
            p.OutputDataReceived += (_, e) => t.watch.Scan(e.Data);
            p.ErrorDataReceived += (_, e) => t.watch.Scan(e.Data);
            p.Exited += (_, __) => t.watch.Exited();
            try
            {
                if (!p.Start()) throw new InvalidOperationException("Process.Start false");
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] cloudflared를 띄우지 못했다: " + e.Message);
                p.Dispose();
                t.killed = true;
                t.watch.FailNow();
                return t;
            }
            lock (live) live.Add(t);
            Debug.Log($"[Net] cloudflared 터널 시작 → 127.0.0.1:{port}");
            return t;
        }

        /// <summary>매 프레임(InternetRoom.Tick — NetRunner). 주소를 받으면 Ready, 30초 · 끝남이면 Failed(프로세스도 끝낸다).</summary>
        public void Tick(long nowMs)
        {
            if (!watch.Tick(nowMs)) return;
            if (watch.State == TunnelState.Ready) Debug.Log("[Net] cloudflared 주소 " + watch.Url);
            else if (watch.State == TunnelState.Failed)
            {
                Debug.LogWarning("[Net] cloudflared가 주소를 주지 않았거나 끝났다");
                Kill();
            }
        }

        public void Dispose()
        {
            Kill();
            watch.Stop();
        }

        void Kill()
        {
            lock (live) live.Remove(this);
            if (killed) return;
            killed = true;
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception e) { Debug.LogWarning("[Net] cloudflared 종료 실패: " + e.Message); }
            try { process.Dispose(); } catch { /* 이미 정리됨 */ }
        }

        static void StopAll()
        {
            CloudflaredTunnel[] all;
            lock (live) all = live.ToArray();
            foreach (var t in all) t.Dispose();
        }

        /// <summary>게임이 가진 사본인가(실행 파일 옆 · Tools/bin). 설치된 cloudflared(Program Files · winget)는 사용자가 서비스로도 쓰므로 남은 것 정리를 하지 않는다.</summary>
        static bool Owned(string exe)
        {
            var full = Path.GetFullPath(exe);
            foreach (var f in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
            {
                var dir = Environment.GetFolderPath(f);
                if (!string.IsNullOrEmpty(dir) && full.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>지난 실행이 남긴 cloudflared 가운데 우리 경로(exe)의 것만 끝낸다. 다른 곳의 cloudflared(사용자가 따로 쓰는 것)는 건드리지 않는다.</summary>
        public static void CleanupStale(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return;
            var mine = Path.GetFullPath(exe);
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(p.MainModule.FileName), mine, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log("[Net] 남은 cloudflared 정리: pid " + p.Id);
                        p.Kill();
                    }
                }
                catch (Exception e) { Debug.LogWarning("[Net] cloudflared 확인 실패(건너뜀): " + e.Message); }
                finally { p.Dispose(); }
            }
        }
    }
}
