using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Net;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>방 만들기 S05에서 고른 값(실행하는 동안 남는다 — 다시 열면 그대로).</summary>
    public sealed class RoomCreateOptions
    {
        /// <summary>[인터넷](추천, 기본) — false면 [직접 연결](결정 92).</summary>
        public bool Internet = true;
        /// <summary>[인터넷] 방을 방 목록에 올리나(결정 95 — 기본 켬, 끄면 참가 코드로만) [가정 M7-6].</summary>
        public bool Public = true;
        public int Seats = SeatId.MaxSeats;
        public Campaign Campaign = Campaign.HeartOfSinnoh;
        /// <summary>새 방의 갤럭시단 AI 세기 — 기본 「강함」(결정 118 ②). S05에 줄은 없고(로비 방 설정에서 바꾼다) 시험이 원칙으로 낮춰 연다.</summary>
        public GalacticAiLevel GalacticAi = SoloGame.NewGameAi;
        /// <summary>새 방의 트레이너 카드 — 기본 끔(결정 134). S05에 줄은 없고(로비 방 설정에서 바꾼다) 카드를 쓰는 시험이 켜고 연다.</summary>
        public bool TrainerCards = SoloGame.NewGameTrainerCards;
    }

    /// <summary>로비 화면(S07·S08)이 읽는 방 — 실제 방(<see cref="NetRoom"/>)이나 캡처의 가짜.</summary>
    public interface ILobbySource
    {
        /// <summary>방 상태(로비 자리 · 방 설정 · 시작 가능). 아직 없으면 null.</summary>
        RoomState Room { get; }
        /// <summary>내 자리(0부터).</summary>
        int Seat { get; }
        bool IsHost { get; }
        /// <summary>[인터넷] 방의 참가 코드(방장 화면만 — 없으면 null).</summary>
        string JoinCode { get; }
        /// <summary>[인터넷] 방이 방 목록에 올라 있나 · 목록 실패 코드(없으면 null).</summary>
        bool Listed { get; }
        string ListFail { get; }
        /// <summary>[직접 연결] 방장 화면의 「이 주소를 친구에게」(주소 · 종류 Tailscale/LAN). 인터넷 방·손님이면 빈 목록.</summary>
        IReadOnlyList<KeyValuePair<string, string>> Addresses { get; }
        /// <summary>[직접 연결] 포트(아니면 0).</summary>
        int Port { get; }
        /// <summary>[인터넷] 방이 Relay 대신 두 번째 터널로 열렸다 — <see cref="JoinCode"/>는 <c>wss://터널</c>(02 §2-1).</summary>
        bool Tunneled { get; }
        /// <summary>방장 화면의 웹 손님 링크(인터넷 = 터널 페이지 하나 · 직접 연결 = 주소마다). 없으면 빈 목록.</summary>
        IReadOnlyList<string> WebLinks { get; }
        /// <summary>웹 링크가 없는 까닭(<see cref="WebFail"/>) · 아직 만드는 중(터널 주소를 기다림).</summary>
        string WebNotice { get; }
        bool WebPending { get; }
        void Send(RoomMsg msg);
    }

    /// <summary>
    /// 방 화면의 흐름(M7 화면 ① — 02 §2·§3-1·§3-2, 04 S05~S08, 결정 92·95): 타이틀 「방 만들기」 → S05(<see cref="RoomCreateScreen"/>) → 방을 열면 로비(S07·S08 <see cref="LobbyScreen"/>),
    /// 타이틀 「참가하기」 → S06(<see cref="RoomJoinScreen"/> — 참가 칸 · 공개 방 목록) → 붙으면 로비. 방이 게임을 시작해 내 좌석의 첫 보기가 오면 <see cref="GameFlow.EnterRoom"/>(셋업 · 보드).
    /// 실패는 연 화면(S05 · S06)에 코드 문구로 보인다(<see cref="NetFailText"/>) — [인터넷] 방이 Unity 서버를 못 쓰면 「Unity 서버를 쓸 수 없습니다」 + [직접 연결로](결정 92, <see cref="ToDirect"/>).
    /// 망 일은 <see cref="NetRoom"/>(Sinnoh.Net)이 하고 이것은 화면만 고른다 — 매 프레임 <see cref="Tick"/>(GameFlow.Tick이 부른다)이 상태가 바뀐 것을 화면에 옮긴다.
    /// 시험·캡처는 <see cref="Board"/>(방 목록)·<see cref="Relay"/>(Relay 할당)를 가짜로, <see cref="Listen"/>·<see cref="Port"/>를 127.0.0.1·시험 포트로 바꾼다 — 실제 UGS를 부르지 않게.
    /// </summary>
    public sealed class RoomFlow
    {
        /// <summary>닉네임 최대 글자 수 [가정 M7-44].</summary>
        public const int NicknameMax = 12;

        readonly GameFlow flow;
        readonly Stopwatch watch = Stopwatch.StartNew();
        readonly RefreshGate refresh = new RefreshGate();
        NetRoom room;
        string nickname;
        bool entered, faked;
        int lastQuery;
        // 방 판에 들어갈 때 볼 스냅샷 수: [로비로] · 이어하기 대기로 돌아온 뒤에는 새 판의 스냅샷이 와야 들어간다(받아 둔 보기는 앞 판의 것)
        int enterAfter = -1;
        int savedProblems;

        public RoomFlow(GameFlow flow) => this.flow = flow ?? throw new ArgumentNullException(nameof(flow));

        // ── 시험·캡처가 바꾸는 곳 ──

        /// <summary>방 목록 서비스(null = UGS Lobby). 시험·캡처는 가짜를 넣는다.</summary>
        public ILobbyBoard Board { get; set; }
        /// <summary>[인터넷] 방의 Relay 할당(null = UGS Relay). 시험은 실패하는 가짜를 넣는다.</summary>
        public IRelayHost Relay { get; set; }
        /// <summary>[직접 연결] 수신 주소(기본 0.0.0.0 — 시험은 127.0.0.1, 방화벽 창을 띄우지 않으려고).</summary>
        public string Listen { get; set; } = NetBootstrap.ListenAll;
        /// <summary>[직접 연결] 포트(기본 7777, 02 §2-3).</summary>
        public ushort Port { get; set; } = NetBootstrap.DefaultPort;
        /// <summary>방장 방의 시각(null = Stopwatch). 시험이 가짜 시각으로 바꾼다.</summary>
        public Func<long> Clock { get; set; }
        /// <summary>손님 접속을 Connect 전에 손본다(시험의 가짜 Relay 참가 — <c>NetClientSession.FakeRelayJoin</c>).</summary>
        public Action<NetClientSession> PrepareGuest { get; set; }
        /// <summary>웹 손님판 폴더(02 §2-2 — 직접 연결 페이지 서버 · 인터넷 페이지 터널). 기본 = 실행 파일 옆 WebGL/(에디터는 null — <see cref="NetRoom.DefaultWebRoot"/>). 시험은 임시 폴더.</summary>
        public string WebRoot { get; set; } = NetRoom.DefaultWebRoot();
        /// <summary>cloudflared.exe(인터넷 방 웹 링크 · Relay 실패 때 두 번째 터널). 기본 = <see cref="NetRoom.DefaultCloudflared"/>(에디터는 null).</summary>
        public string Cloudflared { get; set; } = NetRoom.DefaultCloudflared();
        /// <summary>[인터넷] 방의 웹 손님 몫을 바꾼다(시험의 가짜 터널 — null = WebRoot · Cloudflared로 만든 것).</summary>
        public IGuestWeb Web { get; set; }

        // ── 상태 ──

        /// <summary>S05에서 고른 값.</summary>
        public RoomCreateOptions Create { get; } = new RoomCreateOptions();
        /// <summary>지금 방(없으면 null).</summary>
        public NetRoom Net => room;
        /// <summary>로비 화면이 읽는 방(지금 방 — 없으면 null).</summary>
        public ILobbySource Lobby { get; private set; }
        /// <summary>지금 방의 모습(로비 뒤 — 게임 화면의 응답 시계 · 투표 · 알림 · 재접속 덮개 · 게임 메뉴의 방 줄이 읽는다). 없으면 null.</summary>
        public RoomLive Live { get; private set; }
        /// <summary>방 판의 화면(응답 시계 · 재접속 덮개 · 메뉴의 방 줄)을 보일 때인가: 방 판에 들어가 있거나 캡처·확인용 가짜 방(<see cref="ShowGame"/>).</summary>
        public bool InGame => Live != null && (faked || flow.InRoomGame);
        /// <summary>방을 여는 중(S05 — [인터넷] Relay 할당 · 방 목록) 또는 접속하는 중(S06).</summary>
        public bool Busy => room != null && !entered && step != Step.Lobby;
        /// <summary>마지막 실패(문구 키는 <see cref="NetFailText.Key"/>)와 값. 없으면 null.</summary>
        public string FailCode { get; private set; }
        public string FailDetail { get; private set; }
        /// <summary>마지막 실패가 Unity 서버 쪽이라 [직접 연결]을 권한다(결정 92).</summary>
        public bool SuggestDirect => OnlineFail.SuggestDirect(FailCode);
        /// <summary>S06 공개 방 목록(아직 받지 않았으면 null) · 받는 중.</summary>
        public ListingQuery Listing { get; private set; }
        public bool Browsing { get; private set; }
        /// <summary>[새로 고침]을 다시 누를 수 있을 때까지 남은 ms(0 = 지금).</summary>
        public long RefreshLeftMs => refresh.LeftMs(watch.ElapsedMilliseconds);

        enum Step { None, Opening, Joining, Lobby }

        Step step;

        /// <summary>내 기기 토큰(02 §6-1 — 남에게 보내지 않는다). 플레이어는 프로필(-profile)마다 PlayerPrefs, 에디터·확인용은 실행하는 동안만.</summary>
        public string Token => RoomIdentity.Token;

        /// <summary>닉네임(S05·S06 칸). 비면 기본 「트레이너」. 방을 열거나 참가할 때 남긴다(플레이어는 PlayerPrefs — 에디터·확인용은 실행하는 동안만).</summary>
        public string Nickname
        {
            get => nickname ??= RoomIdentity.Nickname ?? flow.Stage.T("screen.room.nickname.default");
            set
            {
                var s = (value ?? "").Trim();
                if (s.Length > NicknameMax) s = s.Substring(0, NicknameMax);
                nickname = s.Length > 0 ? s : flow.Stage.T("screen.room.nickname.default");
            }
        }

        /// <summary>이 빌드의 접속 인사(02 §4-6): 빌드 표지 = Application.version [가정 M7-45] · 데이터 해시 = 보드의 것.</summary>
        public Hello HelloFor(string token, string nick) => new Hello
        {
            BuildId = Application.version, ContentHash = flow.ContentHash ?? "", Token = token, Nickname = nick,
            Platform = Application.platform == RuntimePlatform.WebGLPlayer ? Platform.Web : Platform.Pc,
            Protagonist = RoomIdentity.Protagonist,
        };

        /// <summary>
        /// 로비의 「내 주인공」(M9 다듬기 P06): 내 좌석의 주인공을 그 인물로 바꿔 달라고 방에 보낸다(표시는 방이 돌려준 방 상태로) — 고른 값은 이 기기에 남겨 다음 방의 첫 값으로 쓴다.
        /// 로비가 아니거나 모르는 키면 false.
        /// </summary>
        public bool PickProtagonist(string key)
        {
            var src = Lobby;
            if (src == null || src.Room == null || src.Room.Phase != RoomPhase.Lobby || src.Seat < 0 || !Protagonists.Has(key)) return false;
            RoomIdentity.Protagonist = key;
            src.Send(new PickProtagonist { Key = key });
            return true;
        }

        /// <summary>내 빌드 표지(방 목록의 「버전 다름」).</summary>
        public string Stamp => RoomListingModel.Stamp(HelloFor(Token, Nickname));

        // ── 화면 열기 ──

        /// <summary>S05 방 만들기(타이틀 「방 만들기」). 하던 게임·방은 떠난다.</summary>
        public void OpenCreate()
        {
            Close();
            ClearFail();
            ResumeFrom = null;
            ResumeId = null;
            flow.ShowRoomScreen(new RoomCreateScreen(flow));
        }

        // ── 방 저장 이어 열기(S04 → S05 → S10 — 02 §6-3 · §7) ──

        /// <summary>이어 열 방 저장(S04 [이어하기] → S05 「방 다시 열기」). 없으면 null(새 방).</summary>
        public SaveRecord ResumeFrom { get; private set; }
        public string ResumeId { get; private set; }

        /// <summary>
        /// 방 저장 목록(S04 — 02 §7 [가정 M7-47] 방 저장 폴더 <c>…/room-saves</c>): 머리만 읽는다. 데이터가 달라졌거나(해시 · 키 목록) 방 저장이 아닌 것은 <see cref="SaveProblem.DataChanged"/>. 저장 폴더가 없으면 빈 목록.
        /// </summary>
        public List<SaveEntry> Saves()
        {
            string root = RoomSaves(flow.SaveRoot);
            var list = root != null ? SaveStore.List(root) : new List<SaveEntry>();
            if (flow.Ctx == null) return list;
            var tables = SaveRecord.IdTablesOf(flow.Ctx);
            foreach (var e in list)
            {
                if (!e.Readable) continue;
                string diff = e.Head.Room == null || e.Head.Room.Seats == null ? "방 저장이 아니다"
                    : flow.ContentHash != null && e.Head.ContentHash != null && flow.ContentHash != e.Head.ContentHash ? "데이터 해시가 다르다"
                    : SaveRecord.IdTablesDiff(e.Head.IdTables, tables);
                if (diff != null) e.Fail(SaveProblem.DataChanged, diff);
            }
            SaveStore.Sort(list);
            return list;
        }

        /// <summary>방 저장을 지운다(S04 [삭제]). 지금 연 방의 저장은 지우지 않는다. 지우지 못하면 false — 던지지 않는다.</summary>
        public bool DeleteSave(string saveId)
        {
            string root = RoomSaves(flow.SaveRoot);
            if (root == null || (room != null && room.IsHost && room.Host.Host.SaveId == saveId)) return false;
            try
            {
                return SaveStore.Delete(root, saveId);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// S04 방 저장 [이어하기]: 저장을 읽어 S05를 「방 다시 열기」로 연다(연결 방식만 고른다 — 자리 수 · 캠페인 · 방 설정은 저장된 값). 읽지 못하거나 끝난 판이면 false(화면은 그대로).
        /// </summary>
        public bool OpenResume(string saveId)
        {
            string root = RoomSaves(flow.SaveRoot);
            if (root == null || flow.Ctx == null) return false;
            SaveRecord record;
            try
            {
                record = SaveStore.Load(SaveStore.PathOf(root, saveId));
                if (record.Head?.Room == null) throw new NotSupportedException("방 저장이 아니다");
                if (record.Head.Summary != null && record.Head.Summary.Finished) throw new NotSupportedException("끝난 판이다");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[Room] 방 저장을 열지 못했다(" + saveId + "): " + e.Message);
                return false;
            }
            Close();
            ClearFail();
            ResumeFrom = record;
            ResumeId = saveId;
            flow.ShowRoomScreen(new RoomCreateScreen(flow));
            return true;
        }

        /// <summary>S05 「방 다시 열기」의 [뒤로]: 저장 목록(S04)으로.</summary>
        public void BackToSaves()
        {
            ResumeFrom = null;
            ResumeId = null;
            flow.OpenSaves();
        }

        /// <summary>S06 참가하기(타이틀 「참가하기」): 화면을 열 때 방 목록을 한 번 받는다(빈도 막기 안이면 받지 않는다). 하던 게임·방은 떠난다.</summary>
        public void OpenJoin(string prefill)
        {
            RoomJoinScreen.Prefill(prefill);
            OpenJoin();
        }

        /// <summary>웹 손님판이 켜질 때(02 §8): 페이지 주소가 참가 대상을 담고 있으면(<see cref="JoinTarget.FromPage"/>) 참가 칸을 채운 S06을 연다. 열었으면 true.</summary>
        public bool OpenFromPage(string pageUrl)
        {
            var target = JoinTarget.FromPage(pageUrl);
            if (target == null) return false;
            OpenJoin(target.Text);
            return true;
        }

        /// <summary>S06 참가하기(타이틀 「참가하기」).</summary>
        public void OpenJoin()
        {
            Close();
            ClearFail();
            flow.ShowRoomScreen(new RoomJoinScreen(flow));
            Refresh();
        }

        // ── 튜토리얼 입구(결정 38, plan M7 「튜토리얼 입구」 [가정 M7-71·72]) ──

        enum Away { None, Join, Lobby }

        Away away;

        /// <summary>참가 화면(S06) · 로비(S07)의 [튜토리얼]을 쓸 수 있나: 튜토리얼 데이터가 있고, 접속하는 중이 아니고, 로비면 손님 자리(방장은 방을 지킨다 · 이어하기 대기실 S10은 없음).</summary>
        public bool CanTutorial => flow.Tutorial.Available && !Busy && !flow.InRoomGame
            && (room == null || (step == Step.Lobby && !room.IsHost && room.Seat.Status == NetStatus.Connected && room.Room != null && room.Room.Phase == RoomPhase.Lobby));

        /// <summary>로비에 자리를 둔 채 튜토리얼을 하는 중인가 — 그동안 흐름이 판을 떠나도(<c>GameFlow.Leave</c>) 방은 닫지 않는다.</summary>
        public bool Parked => away == Away.Lobby && room != null;

        /// <summary>튜토리얼에 들어온 곳이 참가 화면 · 로비인가(돌아갈 곳이 있다).</summary>
        public bool InTutorial => away != Away.None;

        /// <summary>
        /// 참가 화면 · 로비의 [튜토리얼](웹 손님판은 타이틀에 튜토리얼 줄이 없다 — 여기가 입구, PC판도 같은 단추): 타이틀의 [튜토리얼]과 같은 길(<see cref="TutorialMenu.Open"/> — 기록이 없으면 본편 첫 장, 있으면 목차)로 연다.
        /// 참가 화면에서는 방이 없으니 그대로 가고(참가 칸 글은 남는다), 로비에서는 **연결을 유지한다** — 자리는 그대로 · 준비는 푼다(방장이 나 없이 시작하지 못하게). 튜토리얼의 [타이틀로] · 목차 [뒤로]가
        /// 들어온 곳으로 돌아오고(<see cref="BackFromTutorial"/>), 그 사이 방이 닫히거나 내보내지면 참가 화면에 그 이유를 보인다(<see cref="Watch"/>). 열지 못하면 false.
        /// </summary>
        public bool OpenTutorial()
        {
            if (!CanTutorial) return false;
            bool lobby = room != null;
            if (lobby)
            {
                var mine = room.Room != null && room.Seat.Seat >= 0 && room.Seat.Seat < room.Room.Seats.Count ? room.Room.Seats[room.Seat.Seat] : null;
                if (mine != null && mine.Ready) room.Send(new SetReady { Ready = false });
            }
            away = lobby ? Away.Lobby : Away.Join;
            flow.Tutorial.Open();
            return true;
        }

        /// <summary>
        /// 흐름이 타이틀을 열려 할 때(<c>GameFlow.OpenTitle</c>) 부른다: 참가 화면 · 로비에서 들어간 튜토리얼이 「튜토리얼」 줄로 돌아가려는 것이면(fromTutorial) 들어온 곳을 대신 열고 true —
        /// 방이 살아 있으면 로비, 아니면 참가 화면. 다른 줄로 가는 타이틀이면 돌아갈 곳을 잊고 false(타이틀이 열린다 — 자리를 둔 방도 그때 떠난다).
        /// </summary>
        public bool BackFromTutorial(bool fromTutorial)
        {
            var from = away;
            away = Away.None;
            if (from == Away.None || !fromTutorial) return false;
            if (from == Away.Lobby && room != null && step == Step.Lobby) OpenLobby();
            else OpenJoin();
            return true;
        }

        /// <summary>확인용(캡처): 가짜 방으로 로비(이어하기 대기실 — 방 단계 Waiting)를 연다.</summary>
        public void ShowLobby(ILobbySource source)
        {
            Lobby = source ?? throw new ArgumentNullException(nameof(source));
            flow.ShowRoomScreen(new LobbyScreen(flow, source));
        }

        /// <summary>
        /// 확인용(캡처 · 플레이어 확인): 지금 화면(혼자 하는 판 · 타이틀) 위에 가짜 방의 게임 화면 몫(응답 시계 · 일시정지 · 투표 · 알림 · 재접속 덮개 · 게임 메뉴의 방 줄)을 보인다 — 망 없이.
        /// 방을 떠나면(<see cref="Close"/>) 없어진다.
        /// </summary>
        public void ShowGame(RoomLive fake)
        {
            if (fake == null) throw new ArgumentNullException(nameof(fake));
            if (!fake.Fake) throw new ArgumentException("가짜 방만 — 실제 방은 접속이 만든다", nameof(fake));
            Live = fake;
            Lobby = fake;
            faked = true;
            if (fake.ToneOf == null) fake.ToneOf = flow.SeatColor;
        }

        /// <summary>확인용(캡처): 실패 문구를 그 화면에 보인다(S05 = create, 아니면 S06).</summary>
        public void ShowFail(string code, string detail, bool create)
        {
            FailCode = code;
            FailDetail = detail;
            flow.ShowRoomScreen(create ? (IUiScreen)new RoomCreateScreen(flow) : new RoomJoinScreen(flow));
        }

        // ── 방 만들기(S05) ──

        /// <summary>
        /// S05 [방 만들기]: 고른 값으로 이 PC에서 방을 연다. [직접 연결]은 그 자리에서 열고 로비로, [인터넷]은 Relay 할당이 끝나면 로비로(그동안 S05에 「방을 여는 중」).
        /// 실패하면 S05에 문구(포트 사용 중 · Unity 서버를 쓸 수 없음 → [직접 연결로]) — 그 자리에서 실패하면 false. 웹 손님판은 방을 열 수 없다(false).
        /// </summary>
        public bool Host()
        {
            if (!NetBootstrap.CanHost || flow.Ctx == null || Busy) return false;
            Close();
            ClearFail();
            var resume = ResumeFrom;
            var settings = resume != null ? resume.Head.Room.Settings.Clone() : new RoomSettings { Seats = Create.Seats, GalacticAi = Create.GalacticAi, TrainerCards = Create.TrainerCards, Campaign = SoloGame.CampaignReady(Create.Campaign) ? Create.Campaign : Campaign.HeartOfSinnoh };
            if (!GameFlow.TradingReady) settings.Trading = false;   // 결정 124: 거래는 준비 중(새 방 · 이어 연 방)
            if (resume == null) settings.UseClockMemo(RoomIdentity.ClockMemo);   // 결정 140 ①: 지난번에 고른 제한 시간(읽을 수 없거나 범위 밖이면 기본 「느긋하게」)
            string token = Token, nick = Nickname;
            RoomIdentity.Nickname = nick;
            var cfg = new HostConfig
            {
                OwnerToken = token, OwnerNickname = nick, OwnerProtagonist = RoomIdentity.Protagonist,
                RoomId = resume != null ? resume.Head.SaveId ?? ResumeId : "room-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture),
                BuildId = Application.version, ContentHash = flow.ContentHash ?? "", Seed = flow.NextSeed(), AiSeed = flow.NextSeed(), VirtualAgent = StrongAgent.Virtual(), Settings = settings,   // 원칙 + 강한 AI — 세기 · 조종은 HostSession이 방 설정에서 맞춘다(M8 「연결」)
                SavedAt = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Records = flow.RecordsOn,   // 방장의 플레이 기록 보내기(결정 126 ④) → 로비 알림 줄
            };
            var hello = HelloFor(token, nick);
            try
            {
                room = NetRoom.OpenHost(flow.Ctx, cfg, hello, RoomSaves(flow.SaveRoot), resume);
            }
            catch (NotSupportedException e)   // 이어 여는 저장의 형식 판 · 데이터가 다르다(HostSession.Resume — 손님을 받기 전에)
            {
                UnityEngine.Debug.LogWarning("[Room] 방 저장을 이어 열지 못했다: " + e.Message);
                room = null;
                Failed(NetFailText.ResumeFail, null, true);
                return false;
            }
            if (Clock != null) room.Clock = Clock;
            entered = false;
            if (!Create.Internet)
            {
                if (!room.StartDirect(Port, Listen, WebRoot))
                {
                    Failed(room.Seat.FailCode ?? NetFail.PortBusy, null, true);
                    return false;
                }
                OpenLobby();
                return true;
            }
            step = Step.Opening;
            // 이어 연 방은 방 목록에 올리지 않는다(돌아올 사람만 — 참가 코드로) [가정 M7-53]
            var net = room.StartInternet(new ListingOptions { Public = Create.Public && resume == null, HostName = nick, Stamp = RoomListingModel.Stamp(hello) }, Relay, Board, WebRoot, Cloudflared, Port, Web);
            flow.ShowRoomScreen(new RoomCreateScreen(flow));
            Opened(net);   // 가짜 Relay는 그 자리에서 끝난다
            return room != null;   // 그 자리에서 실패했으면(Relay 할당 실패) 버렸다
        }

        /// <summary>[직접 연결로](결정 92): Unity 서버를 쓸 수 없을 때 같은 값으로 [직접 연결] 방을 연다.</summary>
        public bool ToDirect()
        {
            Create.Internet = false;
            return Host();
        }

        /// <summary>
        /// 방 저장 폴더(02 §7 [가정 M7-47]): 솔로 저장 폴더(<c>…/saves</c>) 옆 <c>…/room-saves</c> — 방 저장은 저장 목록(S04)이 아직 읽지 않아(화면 ②) 솔로 목록에 「읽을 수 없음」으로 섞이지 않게.
        /// 저장 폴더가 없으면(에디터·확인용) null = 저장 안 함.
        /// </summary>
        public static string RoomSaves(string saves) => string.IsNullOrEmpty(saves) ? null : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(saves)) ?? saves, "room-saves");

        /// <summary>방장이 지금 쓰는 판의 저장 폴더(플레이 기록 올리기 — 02 §12). 손님 · 저장하지 않는 방 · 방 밖이면 null.</summary>
        public string HostSaveDir => room != null && room.IsHost && room.Saving ? Path.Combine(RoomSaves(flow.SaveRoot), room.Host.Host.SaveId) : null;

        void Opened(InternetRoom net)
        {
            if (net == null || room == null || step != Step.Opening) return;
            if (net.State == InternetRoom.Phase.Ready) OpenLobby();
            else if (net.State == InternetRoom.Phase.Failed) Failed(net.FailCode, null, true);
        }

        // ── 참가(S06) ──

        /// <summary>
        /// S06 [참가]: 참가 칸 하나(코드 6자 · 링크 · <c>IP[:포트]</c> · <c>wss://</c>, <see cref="JoinTarget"/>)로 붙는다. 형식이 틀리면 그 자리에서 NET_BAD_ADDRESS(false).
        /// 붙는 동안 S06에 「연결하는 중」, Welcome을 받으면 로비로, 실패하면 S06에 문구.
        /// </summary>
        public bool Join(string text)
        {
            if (Busy) return false;
            ClearFail();
            if (!JoinTarget.TryParse(text, out var target))
            {
                Failed(NetFail.BadAddress, null, false);
                return false;
            }
            return Join(target);
        }

        /// <summary>S06 방 목록 줄을 눌렀다: 그 방의 참가 코드로 붙는다(Relay). 들어갈 수 없는 줄(가득 참 · 버전 다름)은 아무 일도 없다(false).</summary>
        public bool JoinListed(ListedRoom row)
        {
            if (row == null || !row.Joinable(Stamp) || Busy) return false;
            ClearFail();
            return Join(JoinTarget.Relay(row.JoinCode));
        }

        bool Join(JoinTarget target)
        {
            Close();
            RoomIdentity.Nickname = Nickname;
            room = NetRoom.Join(target, HelloFor(Token, Nickname), PrepareGuest);
            entered = false;
            step = Step.Joining;
            flow.ShowRoomScreen(new RoomJoinScreen(flow));
            Watch();   // 가짜 Relay 실패 · 바로 실패는 그 자리에서
            return true;
        }

        /// <summary>S06 [새로 고침](+ 화면을 열 때 한 번): 공개 방 목록을 받는다 — 지난 조회부터 <see cref="RefreshGate.GapMs"/> 안이면 받지 않는다(UGS Lobby 빈도 한도 [가정 M7-43]).</summary>
        public bool Refresh()
        {
            if (Browsing || !refresh.TryStart(watch.ElapsedMilliseconds)) return false;
            Browsing = true;
            int mine = ++lastQuery;
            RoomListing.Browse(Stamp, q =>
            {
                if (mine != lastQuery) return;
                Browsing = false;
                Listing = q;
                (flow.Stage.Current as RoomJoinScreen)?.Refresh();
            }, Board);
            (flow.Stage.Current as RoomJoinScreen)?.Refresh();
            return true;
        }

        // ── 로비 ──

        // 로비(S07) · 이어하기 대기실(S10 — 방 단계 Waiting)을 연다. 같은 화면이 방 단계로 모양을 고른다
        void OpenLobby()
        {
            step = Step.Lobby;
            if (Live == null || Live.Net != room)
            {
                Live?.Detach();
                Live = new RoomLive(room, () => watch.ElapsedMilliseconds, room.IsHost && room.Port > 0 ? NetRoom.LocalAddresses() : null) { ToneOf = flow.SeatColor };   // 채팅 줄의 좌석 색(결정 140 ②)
                faked = false;
            }
            Lobby = Live;
            flow.ShowRoomScreen(new LobbyScreen(flow, Lobby));
        }

        /// <summary>로비 · 대기실 · 방 판의 [나가기]·[방 닫기]: 방을 떠나 타이틀로(방장은 방 닫기 — 마지막 쓰기 · 손님은 E_HOST_CLOSED). 이어 연 방이면 타이틀의 「이어하기」 줄로.</summary>
        public void Leave()
        {
            bool hosted = room == null || room.IsHost;
            bool resumed = ResumeFrom != null || (room != null && room.IsHost && room.Saving && entered);
            Close();
            ResumeFrom = null;
            ResumeId = null;
            flow.OpenTitle(hosted ? (resumed ? TitleScreen.Continue : TitleScreen.Host) : TitleScreen.Join);
        }

        /// <summary>방을 버린다(방장 = 방 닫기 · 손님 = 나가기). 화면은 바꾸지 않는다. 흐름이 하던 게임을 떠날 때(타이틀 · 혼자 하기 …) 부른다.</summary>
        public void Close()
        {
            var r = room;
            room = null;
            Lobby = null;
            Live?.Detach();
            Live = null;
            faked = false;
            step = Step.None;
            entered = false;
            enterAfter = -1;
            savedProblems = 0;
            r?.Dispose();
        }

        /// <summary>핑 찍기(결정 97, 02 §4-8): 방 판의 보드에서 그 칸에 표시를 찍어 모두에게 보인다. 방 판이 아니거나(혼자 하기 · 튜토리얼) 보드가 아니거나 빈도 제한이면 false.</summary>
        public bool Ping(int cell) => InGame && Live != null && flow.OnBoard && Live.Ping(cell);

        /// <summary>핑(결정 102 ①): 그 대상(보드의 누른 자리 · 보드 밖 공개 대상)에 그 종류(0 여기 봐 · 1 ? · 2 좋아 · 3 조심)로. 방 판이 아니거나 연타 제한이면 false.</summary>
        public bool Ping(PingTarget target, int code) => InGame && Live != null && Live.Social(SocialKind.Ping, code, target);

        /// <summary>감정 표현(결정 102 ②): 정해진 말 일곱 줄 가운데 하나를 내 좌석 얼굴 옆 말풍선으로. 방 판이 아니거나 연타 제한(2초에 1번 · 20초에 5번)이면 false.</summary>
        public bool Emote(int code) => InGame && Live != null && Live.Social(SocialKind.Emote, code);

        /// <summary>도장(결정 102 ③): 남의 행동(기록 줄의 행동 번호 · 그 줄의 주인 좌석)에 0 대단해 · 1 헉 · 2 아깝다. 내 행동 · 방 판이 아님 · 연타 제한(1초에 1번 · 10초에 5번)이면 false.</summary>
        public bool Mark(int seq, int owner, int code) => InGame && Live != null && Live.Social(SocialKind.Mark, code, null, seq, owner);

        /// <summary>
        /// 남의 고르기 보기(M9 다듬기 P09, 02 §4-10): 내 공개 선택 화면(조우 창 · 사당 카드 고르기 · 은행상회 칸)에서 지금 가리키는 · 누른 자리를 방에 알린다(다른 좌석의 화면에 내 좌석 색으로).
        /// 방 판이 아니면(혼자 하기 · 튜토리얼) 아무 일도 없다(false). 비공개 선택 화면은 부르지 않는다 — 방장도 공개 선택이 아니면 넘기지 않는다.
        /// </summary>
        public bool Peek(PeekSpot spot, int index, bool press = false) => InGame && Live != null && Live.Peek(spot, index, press);

        /// <summary>배틀 독 「이번 배틀 묻지 않기」(04 §5 ① — 방 판만): 이 배틀 동안 내 운명 카드 반응 창을 묻지 않고 「안 씀」. 보냈으면 true.</summary>
        public bool AutoPass()
        {
            if (!InGame) return false;
            Live.Send(new SetAutoPass { Sub = ChoiceSub.Fate, On = true });
            return true;
        }

        // ── 매 프레임 ──

        /// <summary>상태가 바뀐 것을 화면에 옮긴다(GameFlow.Tick이 매 프레임): 인터넷 방 열림·실패 · 접속 · 로비 갱신 · 게임 시작 → 방 판 · 로비로 돌아옴 · 끊김.</summary>
        public void Tick()
        {
            (flow.Stage.Current as RoomJoinScreen)?.TickRefresh();   // [새로 고침]의 남은 초
            KeepAwake.Set(room != null && KeepAwake.Wanted(room.IsHost, room.Seat.Status));   // 방이 열린 동안 PC 절전 막기
            if (room == null) return;
            // 창이 비활성일 때 내 차례 알림(작업 표시줄 · 웹 탭 제목) — 방 판에서 내 좌석에 새 선택이 열리면 한 번
            if (entered && flow.InRoomGame && AttentionFlash.Changed(flow.Client?.FirstChoice()?.Id ?? -1) && flow.SoundPrefs.Alert) AttentionFlash.Now();   // 설정에서 끌 수 있다(결정 112 Q17)
            if (step == Step.Opening)
            {
                Opened(room.Internet);
                return;
            }
            Live?.Pull();
            WarnSaves();
            Watch();
        }

        // 쓰기 스레드 저장의 새 실패를 로그 경고로(02 §7 [가정 M7-27] — 화면은 게임 메뉴 저장 줄 · 알림)
        void WarnSaves()
        {
            if (Live == null || !Live.IsHost || Live.SaveProblems.Count <= savedProblems) return;
            for (int i = savedProblems; i < Live.SaveProblems.Count; i++) UnityEngine.Debug.LogWarning("[Room] 방 저장 쓰기 실패: " + Live.SaveProblems[i]);
            savedProblems = Live.SaveProblems.Count;
        }

        void Watch()
        {
            if (room == null) return;
            var seat = room.Seat;
            if (seat.Status == NetStatus.Failed || seat.Status == NetStatus.Closed)
            {
                if (entered) return;   // 게임 중 끊김 · 닫힘 · 강퇴는 재접속 덮개(S11 — RoomLayer)가 보인다
                Failed(seat.FailCode ?? NetFail.Lost, seat.FailDetail, room.IsHost);
                return;
            }
            if (seat.Status != NetStatus.Connected) return;
            if (step == Step.Joining) OpenLobby();
            var phase = seat.Room != null ? seat.Room.Phase : RoomPhase.Lobby;
            if (!entered && phase == RoomPhase.InGame && seat.View != null && seat.Seat >= 0 && seat.Stream.Snapshots > enterAfter)
            {
                entered = true;
                away = Away.None;   // 튜토리얼을 하던 중이면 판이 시작됐으니 방 판으로 데려온다
                // 방장의 연출 조정 값(Host)에 맞춘다 — 방장 멈춤과 내 화면 대기열의 길이가 같게(02 §3-2 [가정 M7-73])
                Tunables.Shared.MatchHost(seat.Room.Tune);
                flow.EnterRoom(room.SeatLink(), seat.Seat, seat.View.Seats.Length, flow.NextSeed());
                return;
            }
            // [로비로](결과 뒤 방장 — 02 §3-1): 같은 방 · 같은 자리로 · 방장이 방을 닫았다가 같은 주소로 이어 열었다(이어하기 대기 S10 — [다시 시도]로 붙었다)
            if (entered && (phase == RoomPhase.Lobby || phase == RoomPhase.Waiting))
            {
                entered = false;
                enterAfter = seat.Stream.Snapshots;
                OpenLobby();
                return;
            }
            if (!entered) (flow.Stage.Current as LobbyScreen)?.Sync();
        }

        void Failed(string code, string detail, bool create)
        {
            away = Away.None;
            Close();
            FailCode = code;
            FailDetail = detail;
            flow.ShowRoomScreen(create ? (IUiScreen)new RoomCreateScreen(flow) : new RoomJoinScreen(flow));
        }

        void ClearFail()
        {
            FailCode = null;
            FailDetail = null;
        }

        /// <summary>빈 방 목록(확인용 — 플레이어 확인 <c>MouseProbe</c>가 실제 UGS Lobby 대신 넣는다). 올리기·갱신은 실패, 조회는 늘 빈 목록.</summary>
        public static readonly ILobbyBoard NoRooms = new EmptyBoard();

        sealed class EmptyBoard : ILobbyBoard
        {
            public void Create(string name, int maxPlayers, IDictionary<string, string> data, Action<string> ok, Action<string> fail) => fail(OnlineFail.ListFail);
            public void Update(string lobbyId, IDictionary<string, string> data, int? maxPlayers, Action ok, Action<string, bool> fail) => fail(OnlineFail.ListFail, true);
            public void Heartbeat(string lobbyId, Action ok, Action<string, bool> fail) => fail(OnlineFail.ListFail, true);
            public void Delete(string lobbyId, Action done) => done();
            public void Query(Action<IReadOnlyList<LobbyRow>> ok, Action<string> fail) => ok(new LobbyRow[0]);
        }
    }

    /// <summary>
    /// 내 기기 토큰·닉네임(02 §6-1 · S05·S06): 플레이어는 PlayerPrefs(토큰은 UGS 프로필 -profile마다 — 한 PC 두 창이 다른 자리), 에디터(플레이 시험)·확인용(ScreenProbe·MouseProbe)은
    /// 실행하는 동안만 쥔다 — 시험·확인이 사용자의 값을 바꾸지 않게(<see cref="ScreenBoot.DefaultSaves"/>와 같은 규칙).
    /// </summary>
    public static class RoomIdentity
    {
        const string TokenKey = "sinnoh.room.token", NickKey = "sinnoh.room.nickname", ProtoKey = "sinnoh.room.protagonist", ClockKey = "sinnoh.room.clock";

        static string token, nick, proto, clock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => token = nick = proto = clock = null;

        /// <summary>방장이 마지막으로 고른 제한 시간(<see cref="RoomSettings.ClockMemo"/> — 결정 140 ①, 없으면 null). 새 방이 <see cref="RoomSettings.UseClockMemo"/>로 읽는다.</summary>
        public static string ClockMemo
        {
            get => clock ?? (Stored && PlayerPrefs.HasKey(ClockKey) ? clock = PlayerPrefs.GetString(ClockKey) : null);
            set
            {
                clock = value;
                if (!Stored || value == null) return;
                PlayerPrefs.SetString(ClockKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>지난번 로비에서 고른 주인공(없으면 null — 방이 자리 번호로 정한다, M9 다듬기 P06).</summary>
        public static string Protagonist
        {
            get => proto ?? (Stored && PlayerPrefs.HasKey(ProtoKey) ? proto = PlayerPrefs.GetString(ProtoKey) : null);
            set
            {
                proto = value;
                if (!Stored || value == null) return;
                PlayerPrefs.SetString(ProtoKey, value);
                PlayerPrefs.Save();
            }
        }

        static bool Stored
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                return !Application.isEditor && Array.IndexOf(args, ScreenProbe.Arg) < 0 && Array.IndexOf(args, MouseProbe.Arg) < 0;
            }
        }

        public static string Token
        {
            get
            {
                if (token != null) return token;
                string key = TokenKey + (UgsBoot.ProfileFor(Environment.GetCommandLineArgs()) is string p ? "." + p : "");
                if (Stored && PlayerPrefs.HasKey(key)) return token = PlayerPrefs.GetString(key);
                token = Guid.NewGuid().ToString("N");
                if (Stored)
                {
                    PlayerPrefs.SetString(key, token);
                    PlayerPrefs.Save();
                }
                return token;
            }
        }

        /// <summary>남긴 닉네임(없으면 null).</summary>
        public static string Nickname
        {
            get => nick ?? (Stored && PlayerPrefs.HasKey(NickKey) ? nick = PlayerPrefs.GetString(NickKey) : null);
            set
            {
                nick = value;
                if (!Stored || value == null) return;
                PlayerPrefs.SetString(NickKey, value);
                PlayerPrefs.Save();
            }
        }
    }
}
