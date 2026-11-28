using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using Unity.Netcode;
using UnityEngine;

namespace Sinnoh.Net
{
    /// <summary>
    /// 방 세션의 시간 구동(02 §1-2 · §6-3, isekai Net/NetRunner를 옮겨 러너 몫을 더함): 매 프레임 단조 시각(Stopwatch ms)을 세션의 Tick에 넣는다 —
    /// 호스트(<see cref="NetHostSession"/>)는 받아 둔 메시지 · 방 시계 · AI 답 넣기, 손님(<see cref="NetClientSession"/>)은 재접속 시계.
    /// 호스트 방은 <see cref="OpenHost"/>로 연다: AI 러너(작업 스레드 <see cref="AgentWorker"/>, 시계 = 이 Stopwatch)와 판마다 쓰기 스레드 저장(<see cref="SaveWriter"/>)을 방 설정에 꽂는다.
    /// [인터넷] 방은 그 뒤 <see cref="OpenInternet"/>(Relay → 코드 → 공개면 방 목록).
    /// 켜져 있는 동안 <c>Application.runInBackground</c>를 켠다(창이 포커스를 잃어도 프레임이 돌아 손님이 끊기지 않게 — 마지막 러너가 꺼지면 되돌림).
    /// 앱 종료: 호스트 방이면 <see cref="HoldQuit"/>가 종료를 한 번 미루고 방을 닫아(E_HOST_CLOSED) NGO 유예(<see cref="NetHostSession.CloseGraceMs"/>)와 방 목록 지우기가 끝나면
    /// (늦어도 <see cref="QuitWaitMs"/>) 다시 끝낸다 — 닫힘 이유가 손님에게 닿게. 그 뒤 Application.quitting·이 컴포넌트가 없어질 때 = <see cref="Shutdown"/>.
    /// </summary>
    public sealed class NetRunner : MonoBehaviour
    {
        /// <summary>앱 종료를 미루는 최대 시간.</summary>
        public const long QuitWaitMs = 3000;

        static int awake;
        static bool backgroundBefore;

        readonly Stopwatch watch = Stopwatch.StartNew();
        readonly List<IDisposable> owned = new List<IDisposable>();
        readonly List<SaveWriter> writers = new List<SaveWriter>();
        InternetRoom internet;
        long? quitAt;
        bool quitReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => awake = 0;

        /// <summary>세션에 넣을 시각(null = 이 Stopwatch). 시험이 가짜 시각으로 바꾼다 — AI 러너의 마감 시계는 늘 Stopwatch다.</summary>
        public Func<long> Clock { get; set; }
        public long NowMs => Clock != null ? Clock() : watch.ElapsedMilliseconds;
        public INetSession Session { get; set; }
        /// <summary>[인터넷] 방(<see cref="OpenInternet"/>). 없으면 null.</summary>
        public InternetRoom Internet => internet;
        /// <summary>앱 종료를 미루고 기다리는 중.</summary>
        public bool QuitPending => quitAt.HasValue && !quitReady;
        /// <summary>이 방이 만든 쓰기 스레드 저장(<see cref="SaveWriter"/> — 판마다 하나)의 쓰기 실패를 모은 것(다시 써서 됐어도 남는다). 화면 ②가 게임 메뉴 저장 줄 · 로그 경고로 보인다.</summary>
        public List<string> SaveProblems
        {
            get
            {
                var r = new List<string>();
                foreach (var w in writers) r.AddRange(w.Problems);
                return r;
            }
        }

        /// <summary>매 프레임 Session.Tick 뒤에 같은 시각으로 부른다.</summary>
        public event Action<long> Ticked;

        /// <summary>
        /// 이 PC에서 방을 연다(NGO는 아직 시작하지 않는다 — 직접 연결은 돌려받은 세션의 <see cref="NetHostSession.Start"/>, 인터넷은 <see cref="OpenInternet"/>). cfg.Runner = 새 AI 러너(이 컴포넌트가 갖는다),
        /// saves가 있으면 cfg.Store = SaveId → saves/SaveId의 쓰기 스레드 저장(사용자 저장 폴더는 조립 지점이 준다 — 시험은 임시 폴더). resume이 있으면 그 방 저장으로 이어하기 대기(HostSession.Resume).
        /// </summary>
        public NetHostSession OpenHost(NetworkManager manager, Ctx ctx, HostConfig cfg, Hello owner, string saves = null, SaveRecord resume = null)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            var worker = new AgentWorker(() => watch.ElapsedMilliseconds);
            owned.Add(worker);
            cfg.Runner = worker;
            if (saves != null)
                cfg.Store = id =>
                {
                    var w = new SaveWriter(Path.Combine(saves, id));
                    writers.Add(w);
                    return w;
                };
            var host = new NetHostSession(manager, owner,
                link => resume == null ? new HostSession(ctx, link, cfg, NowMs) : HostSession.Resume(ctx, link, cfg, resume, NowMs), () => NowMs);
            Session = host;
            return host;
        }

        /// <summary>
        /// [인터넷] 방(02 §2-1, 결정 95): <see cref="OpenHost"/>로 만든 방을 Relay로 연다 — 할당(maxConnections = 자리 수 6 − 1 [가정 M7-35]) → StartHost → 참가 코드,
        /// listing.Public이면 UGS Lobby 방 목록에 올린다(비공개·null = 코드로만). 결과는 돌려받은 <see cref="InternetRoom"/>의 State·Changed(실패면 SuggestDirect — [직접 연결] 안내, 결정 92).
        /// 매 프레임 Tick(터널 · 방 목록 갱신·하트비트)과 끝낼 때 목록 지우기 · 터널 끄기는 이 컴포넌트가 한다. relay·board는 시험용(null = UGS).
        /// tunnel = Relay 실패 때의 게임 터널(<see cref="GameTunnel"/>, 시험은 가짜). null = 터널 없음 · Relay가 실패하면 바로 Failed.
        /// </summary>
        public InternetRoom OpenInternet(NetHostSession host, ListingOptions listing, IRelayHost relay = null, ILobbyBoard board = null, IGameTunnel tunnel = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (internet != null) throw new InvalidOperationException("이미 인터넷 방을 열었다");
            var manager = GetComponent<NetworkManager>();
            internet = new InternetRoom(relay ?? RelayLink.HostFor(manager),
                () => host.Start() ? null : host.Local.FailCode ?? OnlineFail.RelayAuth,
                () => host.Host.Room, listing, board ?? (listing != null && listing.Public ? RoomListing.Ugs : null), SeatId.MaxSeats, tunnel);
            owned.Add(internet);
            internet.Open();
            return internet;
        }

        /// <summary>
        /// 다 쓴 방을 버린다(<see cref="NetRoom.Dispose"/>): <see cref="Shutdown"/> 뒤 NGO가 멈추면(방 닫기 유예 포함) 이 GameObject를 없앤다 —
        /// 도는 중에 없애면 Transport가 버린 연결 표를 읽는다(NetLoopTests 정리와 같은 순서).
        /// </summary>
        public void Release()
        {
            Shutdown();
            releasing = true;
        }

        bool releasing;

        void Update()
        {
            if (releasing)
            {
                var m = GetComponent<NetworkManager>();
                if (m == null || (!m.IsListening && !m.ShutdownInProgress)) Destroy(gameObject);
                return;
            }
            long now = NowMs;
            Session?.Tick(now);
            internet?.Tick(watch.ElapsedMilliseconds);   // 방 목록 하트비트는 실제 시각
            Ticked?.Invoke(now);
            if (QuitPending && (QuitSettled || watch.ElapsedMilliseconds - quitAt.Value >= QuitWaitMs))
            {
                quitReady = true;
                Application.Quit();
            }
        }

        bool QuitSettled => !(Session is NetHostSession h && h.ShuttingDown) && (internet == null || internet.Idle);

        void OnEnable()
        {
            if (awake++ == 0)
            {
                backgroundBefore = Application.runInBackground;
                Application.runInBackground = true;
            }
            Application.wantsToQuit += HoldQuit;
            Application.quitting += Shutdown;
        }

        void OnDisable()
        {
            if (--awake == 0) Application.runInBackground = backgroundBefore;
            Application.wantsToQuit -= HoldQuit;
            Application.quitting -= Shutdown;
        }

        void OnDestroy() => Shutdown();

        /// <summary>
        /// Application.wantsToQuit: 호스트 방이 열려 있으면 한 번 false(종료를 미룸) — 방을 닫고(마지막 쓰기 → E_HOST_CLOSED) 방 목록을 지운 뒤, NGO 유예와 지우기가 끝나면 Update가 다시 Application.Quit.
        /// 그 밖(손님 · 방 없음 · 이미 기다림이 끝남)은 true.
        /// </summary>
        internal bool HoldQuit()
        {
            if (quitReady) return true;
            if (quitAt.HasValue) return false;
            if (!(Session is NetHostSession host) || host.Local.Status != NetStatus.Connected) return true;
            quitAt = watch.ElapsedMilliseconds;
            host.Close();
            internet?.Dispose();
            if (QuitSettled)
            {
                quitReady = true;
                return true;
            }
            return false;
        }

        /// <summary>세션을 닫고(호스트면 방 닫기 — 마지막 쓰기) 버린 뒤 러너·인터넷 방(목록 지우기)을 버린다. 두 번 불러도 된다.</summary>
        public void Shutdown()
        {
            var s = Session;
            Session = null;
            (s as IDisposable)?.Dispose();
            foreach (var d in owned) d.Dispose();
            owned.Clear();
            internet = null;
        }
    }
}
