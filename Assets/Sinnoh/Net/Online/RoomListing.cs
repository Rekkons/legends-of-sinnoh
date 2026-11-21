using System;
using System.Collections.Generic;
using System.Linq;
using Sinnoh.Data;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace Sinnoh.Net
{
    /// <summary>
    /// 공개 방 목록의 UGS Lobby 층(결정 95, isekai Net/Online/RoomListing을 고쳐 옮김 — 판단은 Data <see cref="ListingPublisher"/>·<see cref="RoomListingModel"/>).
    /// 목록은 안내판일 뿐이고 실제 연결은 Relay 참가 코드(데이터 j) 그대로다. 로그인은 Relay와 같은 길(<see cref="UgsBoot.SignInAsync"/>).
    /// 실패는 던지지 않고 코드로(<see cref="OnlineFail.RelayAuth"/> 로그인 · <see cref="OnlineFail.ListFail"/> Lobby) — 대시보드에서 Lobby가 꺼져 있어도 방 열기·코드 참가는 그대로다.
    /// 콜백은 메인 스레드(UnitySynchronizationContext — 메인 스레드에서 불렀을 때).
    /// </summary>
    public sealed class RoomListing : ILobbyBoard
    {
        public const int QueryCount = 100;   // 한 번에 받는 로비 수(UGS 최대)

        public static readonly RoomListing Ugs = new RoomListing();

        /// <summary>손님: 공개 방 목록(로비 단계 방만, <see cref="RoomListingModel.Order"/> 순서). myStamp = 내 빌드 표지(RoomListingModel.Stamp(내 Hello)).</summary>
        public static void Browse(string myStamp, Action<ListingQuery> done, ILobbyBoard board = null)
        {
            if (done == null) throw new ArgumentNullException(nameof(done));
            (board ?? Ugs).Query(rows => done(ListingQuery.Done(rows, myStamp)), code => done(ListingQuery.Fail(code)));
        }

        public async void Create(string name, int maxPlayers, IDictionary<string, string> data, Action<string> ok, Action<string> fail)
        {
            if (!await SignIn(fail, "만들기")) return;
            try
            {
                var lobby = await LobbyService.Instance.CreateLobbyAsync(name, maxPlayers, new CreateLobbyOptions { IsPrivate = false, Data = ToData(data) });
                ok(lobby.Id);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] 방 목록에 올리지 못했다: " + Describe(e));
                fail(OnlineFail.ListFail);
            }
        }

        public async void Update(string lobbyId, IDictionary<string, string> data, int? maxPlayers, Action ok, Action<string, bool> fail)
        {
            try
            {
                var options = new UpdateLobbyOptions { Data = ToData(data) };
                if (maxPlayers.HasValue) options.MaxPlayers = maxPlayers.Value;
                await LobbyService.Instance.UpdateLobbyAsync(lobbyId, options);
                ok();
            }
            catch (Exception e) { Lost(e, "갱신", fail); }
        }

        public async void Heartbeat(string lobbyId, Action ok, Action<string, bool> fail)
        {
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
                ok();
            }
            catch (Exception e) { Lost(e, "하트비트", fail); }
        }

        public async void Delete(string lobbyId, Action done)
        {
            try { await LobbyService.Instance.DeleteLobbyAsync(lobbyId); }
            catch (Exception e) { Debug.LogWarning("[Net] 방 목록에서 지우지 못했다: " + Describe(e)); }
            done();
        }

        public async void Query(Action<IReadOnlyList<LobbyRow>> ok, Action<string> fail)
        {
            if (!await SignIn(fail, "조회")) return;
            try
            {
                var response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count = QueryCount,
                    Order = new List<QueryOrder> { new QueryOrder(false, QueryOrder.FieldOptions.Created) },
                });
                ok((response?.Results ?? new List<Lobby>()).Select(l => new LobbyRow
                {
                    Id = l.Id, CreatedMs = new DateTimeOffset(DateTime.SpecifyKind(l.Created, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                    Data = l.Data == null ? new Dictionary<string, string>() : l.Data.ToDictionary(kv => kv.Key, kv => kv.Value?.Value),
                }).ToList());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] 방 목록을 불러오지 못했다: " + Describe(e));
                fail(OnlineFail.ListFail);
            }
        }

        static async System.Threading.Tasks.Task<bool> SignIn(Action<string> fail, string what)
        {
            try
            {
                await UgsBoot.SignInAsync();
                return true;
            }
            catch (OnlineError e)
            {
                Debug.LogWarning($"[Net] 방 목록 {what}: 로그인 실패: " + e.Message);
                fail(e.Code);
                return false;
            }
        }

        // 로비가 없어졌으면(만료·지워짐·권한 없음) gone — 더 보내지 않는다. 그 밖(한도·네트워크)은 다음 차례에 다시
        static void Lost(Exception e, string what, Action<string, bool> fail)
        {
            Debug.LogWarning($"[Net] 방 목록 {what} 실패: " + Describe(e));
            bool gone = e is LobbyServiceException l && (l.Reason == LobbyExceptionReason.LobbyNotFound || l.Reason == LobbyExceptionReason.EntityNotFound || l.Reason == LobbyExceptionReason.Forbidden);
            fail(OnlineFail.ListFail, gone);
        }

        static Dictionary<string, DataObject> ToData(IDictionary<string, string> data) =>
            data.ToDictionary(kv => kv.Key, kv => new DataObject(DataObject.VisibilityOptions.Public, kv.Value));

        static string Describe(Exception e) => e is LobbyServiceException l ? $"{l.Reason} {l.Message}" : e.Message;
    }
}
