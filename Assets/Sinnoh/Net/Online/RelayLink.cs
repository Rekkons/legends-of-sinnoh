using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Sinnoh.Data;
using Unity.Netcode;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Debug = UnityEngine.Debug;

namespace Sinnoh.Net
{
    /// <summary>
    /// Unity Relay 연결(02 §2-1 [인터넷], isekai Net/Online/RelayLink를 옮김). 통신은 늘 WebSocket이라 Relay 접속 종류도 "wss"(웹 손님과 같은 길).
    /// - 방장(<see cref="HostFor"/> · <see cref="HostAsync"/>): 익명 로그인 → CreateAllocation(maxConnections, 지역 asia-northeast1 — 실패하면 기본 지역) → GetJoinCode → UnityTransport에 Relay(wss).
    ///   그 뒤 NetHostSession.Start가 StartHost한다(NetBootstrap.StartHost가 Relay 설정을 덮지 않는다).
    /// - 손님(<see cref="Joiner"/>): 익명 로그인 → JoinAllocation(코드) → wss 연결 정보. NetClientSession이 시도마다(재접속도 같은 코드) 부른다.
    /// 실패 = <see cref="OnlineError"/>(RelayAuth: 로그인·서비스·할당·한도 / RelayCode: 참가 코드가 없거나 틀림).
    /// </summary>
    public static class RelayLink
    {
        public const string Region = "asia-northeast1";   // 도쿄 — 한국에서 가장 가까운 지역(isekai 2026-10-09 확인)
        public const string ConnectionType = "wss";

        /// <summary>NetClientSession이 쓰는 손님 참가. 시험은 가짜로 바꾼다.</summary>
        public static readonly IRelayJoin<RelayServerData> Joiner = new RelayJoiner();

        /// <summary>마지막 방장 할당의 지역(로그·확인용).</summary>
        public static string LastRegion { get; private set; }

        /// <summary>manager에 Relay를 넣는 방장 할당(InternetRoom이 부른다).</summary>
        public static IRelayHost HostFor(NetworkManager manager) => new RelayHoster(manager ? manager : throw new ArgumentNullException(nameof(manager)));

        sealed class RelayJoiner : IRelayJoin<RelayServerData>
        {
            public async void Join(string code, Action<RelayServerData> ok, Action<string> fail)
            {
                RelayServerData data;
                try { data = await JoinAsync(code); }
                catch (OnlineError e)
                {
                    Debug.LogWarning("[Net] Relay 참가 실패: " + e.Message);
                    fail(e.Code);
                    return;
                }
                ok(data);
            }
        }

        sealed class RelayHoster : IRelayHost
        {
            readonly NetworkManager manager;
            public RelayHoster(NetworkManager manager) => this.manager = manager;

            public async void Allocate(int maxConnections, Action<string> ok, Action<string> fail)
            {
                string code;
                try { code = await HostAsync(manager, maxConnections); }
                catch (OnlineError e)
                {
                    Debug.LogWarning("[Net] Relay로 방을 열지 못했다: " + e.Message);
                    fail(e.Code);
                    return;
                }
                ok(code);
            }
        }

        /// <summary>방장: 할당을 만들고 manager의 UnityTransport에 Relay(wss)를 넣은 뒤 참가 코드를 돌려준다. 다음은 NetHostSession.Start.</summary>
        public static async Task<string> HostAsync(NetworkManager manager, int maxConnections)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            var (allocation, code) = await AllocateAsync(maxConnections);
            NetBootstrap.UseRelay(manager, allocation.ToRelayServerData(ConnectionType));
            return code;
        }

        /// <summary>손님: 참가 코드로 wss 연결 정보를 받는다.</summary>
        public static async Task<RelayServerData> JoinAsync(string code)
        {
            await UgsBoot.SignInAsync();
            try
            {
                var join = await RelayService.Instance.JoinAllocationAsync(code);
                return join.ToRelayServerData(ConnectionType);
            }
            catch (Exception e)
            {
                throw new OnlineError(JoinErrorCode(e is RelayServiceException r ? r.Reason : RelayExceptionReason.Unknown), e);
            }
        }

        /// <summary>JoinAllocation 실패 이유 → 코드. 코드가 없거나 틀림·방이 닫힘이면 RelayCode, 그 밖(로그인·네트워크·서비스·한도)은 RelayAuth.</summary>
        public static string JoinErrorCode(RelayExceptionReason reason)
        {
            switch (reason)
            {
                case RelayExceptionReason.JoinCodeNotFound:
                case RelayExceptionReason.AllocationNotFound:
                case RelayExceptionReason.EntityNotFound:
                case RelayExceptionReason.InvalidRequest:
                case RelayExceptionReason.InvalidArgument:
                    return OnlineFail.RelayCode;
                default:
                    return OnlineFail.RelayAuth;
            }
        }

        static async Task<(Allocation allocation, string code)> AllocateAsync(int maxConnections)
        {
            if (maxConnections < 1) throw new ArgumentOutOfRangeException(nameof(maxConnections));
            await UgsBoot.SignInAsync();
            try
            {
                Allocation allocation;
                try { allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections, Region); }
                catch (RelayServiceException e)
                {
                    Debug.LogWarning($"[Net] Relay 지역 {Region} 할당 실패, 기본 지역으로: {e.Reason} {e.Message}");
                    allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
                }
                LastRegion = allocation.Region;
                var code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                return (allocation, code);
            }
            catch (Exception e)
            {
                throw new OnlineError(OnlineFail.RelayAuth, e);
            }
        }

        /// <summary>
        /// 실제 UGS 확인(PlayMode Explicit 시험 — 관문 밖): 로그인 → 할당(asia-northeast1) → 참가 코드 → 그 코드로 참가 → 둘 다 wss인지. 돌려주는 값 = (코드, 지역, 걸린 ms).
        /// 할당은 NGO에 붙이지 않아 Relay가 곧 지운다(연결 없는 할당은 시간이 지나면 없어진다).
        /// </summary>
        public static async Task<(string code, string region, long ms)> ProbeAsync(int maxConnections)
        {
            var watch = Stopwatch.StartNew();
            var (allocation, code) = await AllocateAsync(maxConnections);
            var host = allocation.ToRelayServerData(ConnectionType);
            var guest = await JoinAsync(code);
            if (host.IsWebSocket == 0 || guest.IsWebSocket == 0) throw new OnlineError(OnlineFail.RelayAuth, new Exception("wss 접속 정보가 아니다"));
            Debug.Log($"[Net] Relay 확인: 코드 {code}, 지역 {allocation.Region}, {watch.ElapsedMilliseconds} ms");
            return (code, allocation.Region, watch.ElapsedMilliseconds);
        }
    }
}
