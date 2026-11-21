using System.Collections.Generic;

namespace Sinnoh.Rules.Session
{
    /// <summary>프로토콜 판(02 §4-6). 메시지 모양이 바뀌면 올린다 — 다르면 승인 단계에서 E_VERSION.</summary>
    public static class Protocol
    {
        /// <summary>2 = M7 「세션 ③」(투표 · 자리 확인 대기 · 이어하기 대기 — StartVote·CastVote·AssignSeat·RefuseClaim, RoomState.Vote·Claims). 3 = 「호스트 러너 · 세션 ③-2」(SetAutoPass · ReturnToLobby · RoomPhase.Closed).
        /// 4 = 「핑 찍기」(결정 97 — <see cref="PlacePing"/> · <see cref="PingMsg"/>, RoomPing.cs).
        /// 5 = 「튜토리얼 입구 · 연출 실접속」(<see cref="RoomState.Tune"/> — 방장의 연출 조정 값).
        /// 6 = 「소통」(결정 102 — 핑 아무 데나 · 감정 표현 · 도장: <see cref="SendSocial"/> · <see cref="SocialMsg"/>, RoomSocial.cs — 옛 PlacePing · PingMsg를 갈음).
        /// 7 = 「결정 112 반영」(일시정지 규칙 — <see cref="RoomSettings.Pause"/> · <see cref="SetPaused.All"/> · <see cref="RoomState.PauseReserved"/> · <see cref="VoteKind.Pause"/> · PAUSE_DENIED).
        /// 8 = 「좌석의 주인공」(M9 다듬기 P06 — <see cref="PickProtagonist"/> · <see cref="RoomSeat.Protagonist"/> · <see cref="Hello.Protagonist"/>).
        /// 9 = 「남의 고르기 보기」(M9 다듬기 P09 — <see cref="SendPeek"/> · <see cref="PeekMsg"/>, RoomPeek.cs).
        /// 10 = 「플레이 기록 알림」(결정 126 ④ — <see cref="RoomState.Records"/>: 방장이 플레이 기록을 보낸다 → 로비 한 줄).</summary>
        /// 11 = 「남이 가리키는 것」(결정 120 — <see cref="PeekSpot.Village"/> · <see cref="PeekSpot.Move"/>: 고향 마을 고르기 · 배틀 기술 고르기).</summary>
        /// 12 = 「시간 세부 조절」(결정 140 ① — <see cref="ClockPreset.Custom"/> · <see cref="RoomSettings.Custom"/>: 방장이 칸마다 정한 제한 시간).
        /// 13 = 「방 채팅」(결정 140 ② — <see cref="SendChat"/> · <see cref="ChatMsg"/> · <see cref="RoomSettings.Chat"/>, RoomChat.cs.).
        /// 14 = 「무르기」(결정 149 — <see cref="UndoStep"/> · <see cref="RequestUndoPoints"/> · <see cref="RewindTo"/> · <see cref="UndoPointsMsg"/> · <see cref="Submit.Epoch"/> · <see cref="RoomSettings.Undo"/> ·
        /// GameSnapshot · GameDelta의 Undo · Rewind · UNDONE · REWOUND · UNDO_DENIED · <see cref="NoticeMsg.Round"/> · <see cref="RejectReason.Rewound"/>).</summary>
        public const int Version = 14;
    }

    /// <summary>접속한 기기(02 §3-1 줄의 PC/웹).</summary>
    public enum Platform { Pc, Web }

    /// <summary>
    /// 방 단계(02 §4-3 RoomUpdate). Result = 게임이 끝남(S64 — 방장의 [로비로] = <see cref="ReturnToLobby"/>). Waiting = 이어하기 대기실(S10 — 저장된 좌석이 돌아오기를 기다린다, 02 §6-3).
    /// Closed = 방을 닫았다(<see cref="HostSession.Close"/> — 손님에게는 E_HOST_CLOSED 알림으로 간다).
    /// </summary>
    public enum RoomPhase { Lobby, InGame, Result, Waiting, Closed }

    /// <summary>
    /// 투표 종류(02 §6-2): 강퇴(가결 → E_KICKED · 토큰 금지 · 좌석은 끝까지 기본값) · 자리 비움(가결 → 자리 비움, 그 좌석이 무엇이든 내면 풀린다) ·
    /// 일시정지(방 설정 「투표」 — 대상 = 요청한 좌석, 가결 → 그 좌석의 일시정지 예약 · 30초 · 투표할 좌석이 없으면 통과, 결정 112 Q6).
    /// </summary>
    public enum VoteKind { Kick, Away, Pause }

    /// <summary>승인 거절·알림 코드(02 §2-5 표 · §4-3 Notice). 화면 문구는 04·screens.json이 이 코드로 고른다.</summary>
    public static class NetCode
    {
        public const string Version = "E_VERSION";
        public const string Content = "E_CONTENT";
        public const string Kicked = "E_KICKED";
        public const string Duplicate = "E_DUPLICATE";
        public const string Full = "E_FULL";
        public const string InGame = "E_IN_GAME";
        public const string BadHello = "E_BAD_HELLO";
        public const string HostClosed = "E_HOST_CLOSED";
        /// <summary>자리 확인 대기에서 방장이 들이지 않았다(02 §6-3).</summary>
        public const string SeatRefused = "E_SEAT_REFUSED";

        /// <summary>승인은 됐지만 좌석이 없다 — 방장이 자리를 맡기거나 거절할 때까지 기다린다(02 §6-3 자리 확인 대기).</summary>
        public const string SeatClaim = "SEAT_CLAIM";
        public const string VotePassed = "VOTE_PASSED";
        public const string VoteFailed = "VOTE_FAILED";
        /// <summary>투표를 열 수 없다(이미 투표 중 · 대상이 틀림 · 부결 뒤 대기 중 — 연 좌석에게만).</summary>
        public const string VoteDenied = "VOTE_DENIED";

        public const string PlayerLeft = "PLAYER_LEFT";
        public const string PlayerBack = "PLAYER_BACK";
        public const string AutoResolved = "AUTO_RESOLVED";
        public const string Away = "AWAY";
        public const string AwayCleared = "AWAY_CLEARED";
        public const string Paused = "PAUSED";
        public const string Resumed = "RESUMED";
        /// <summary>방 설정 「방장만」인데 손님이 일시정지를 걸려 했다(그 좌석에게만 — 결정 112 Q6).</summary>
        public const string PauseDenied = "PAUSE_DENIED";
        /// <summary>좌석이 한 수 무르기를 했다(전원 — Seat = 무른 좌석, 결정 149).</summary>
        public const string Undone = "UNDONE";
        /// <summary>방장이 지난 턴으로 되돌렸다(전원 — Seat = 방장).</summary>
        public const string Rewound = "REWOUND";
        /// <summary>무르기 · 되돌리기 요청을 받지 않았다(낡은 To · 못 하는 때 · 일시정지 — 그 좌석에게만).</summary>
        public const string UndoDenied = "UNDO_DENIED";
    }

    /// <summary>
    /// 접속 인사(02 §4-2 Hello — NGO 승인 데이터에 싣는다). Token = 기기 프로필의 GUID(남에게 보내지 않는다, §6-1).
    /// 순서(§4-6): ProtocolVersion·BuildId·RulesVersion → E_VERSION, ContentHash → E_CONTENT, 금지 토큰 → E_KICKED, 접속 중인 같은 토큰 → E_DUPLICATE, 단계 → E_FULL·E_IN_GAME.
    /// </summary>
    public sealed class Hello
    {
        public int ProtocolVersion = Protocol.Version;
        public string BuildId = "";
        public int RulesVersion = SaveRecord.CurrentRulesVersion;
        public string ContentHash;
        public string Token;
        public string Nickname;
        public Platform Platform;
        /// <summary>이 기기가 지난번 로비에서 고른 주인공(없으면 null — 방이 자리 번호로 정한다, P06). 게임 중 재접속에서는 보지 않는다(좌석의 값 그대로).</summary>
        public string Protagonist;
    }

    /// <summary>승인 결과. 거절이면 Code(<see cref="NetCode"/>)와 화면에 넣을 값(Detail, 예 「4/6」).</summary>
    public sealed class Approval
    {
        public bool Ok;
        public string Code;
        public string Detail;
        public int Seat = -1;

        public static Approval Accept(int seat) => new Approval { Ok = true, Seat = seat };

        public static Approval Reject(string code, string detail = null) => new Approval { Ok = false, Code = code, Detail = detail };

        /// <summary>자리 확인 대기(02 §6-3): 연결은 받고 좌석은 없다(Seat −1, Code = <see cref="NetCode.SeatClaim"/>). 방장이 AssignSeat·RefuseClaim으로 정한다.</summary>
        public static Approval Claim() => new Approval { Ok = true, Code = NetCode.SeatClaim };

        public override string ToString() => Ok ? (Seat >= 0 ? $"Ok(seat {Seat})" : $"Ok({Code})") : $"{Code}{(Detail != null ? " " + Detail : "")}";
    }

    /// <summary>호스트 → 연결(02 §1-1). 방(HostSession)이 쓴다 — 연결 번호는 NGO clientId(호스트 화면 = 0) 또는 시험의 번호.</summary>
    public interface IRoomLink
    {
        void Send(int conn, ServerMsg msg);

        /// <summary>그 연결을 끊는다(code = 끊는 이유, 손님 화면 문구).</summary>
        void Close(int conn, string code);
    }

    // ── 손님 → 호스트 (02 §4-2) ──

    /// <summary>방 메시지(손님 → 호스트). 승인(Hello)은 메시지가 아니라 <see cref="HostSession.Approve"/>다.</summary>
    public abstract class RoomMsg { }

    public sealed class SetProfile : RoomMsg
    {
        public string Nickname;
    }

    /// <summary>
    /// 내 좌석의 주인공을 고른다(M9 다듬기 P06 — 결정 112 Q4): 로비에서만, <see cref="Protagonists.Keys"/> 여덟 가운데(남과 겹쳐도 된다 [가정 M7-94]). 틀린 키 · 게임 중이면 버린다.
    /// 겉모양뿐이다(보드 말 · 얼굴 상자 · 볼 던지는 뒷모습) — 규칙 · 해시 밖, 방 저장의 머리(<see cref="SaveHead.Protagonists"/>)에 남는다.
    /// </summary>
    public sealed class PickProtagonist : RoomMsg
    {
        public string Key;
    }

    public sealed class SetReady : RoomMsg
    {
        public bool Ready;
    }

    /// <summary>방장만. 로비에서는 모든 칸, 게임 중에는 <see cref="RoomSettings.InGameChange"/>의 칸만.</summary>
    public sealed class ChangeSettings : RoomMsg
    {
        public RoomSettings Settings;
    }

    /// <summary>방장만 · 로비만. 그 토큰은 이 방에 다시 못 들어온다(E_KICKED, 02 §3-1).</summary>
    public sealed class KickFromLobby : RoomMsg
    {
        public int Seat;
    }

    /// <summary>방장만 · 로비만. 2명 이상이 모두 준비하면 시작한다(02 §3-1).</summary>
    public sealed class StartGame : RoomMsg { }

    /// <summary>게임 명령(02 §4-2 Submit). 사람 조종자는 가상 응답자 선택의 ChoiceId로 답한다.</summary>
    public sealed class Submit : RoomMsg
    {
        public Command Command;
        /// <summary>낸 좌석이 받은 되돌림 횟수(<see cref="ClientMsg.Epoch"/>) — 방의 지금 값보다 작으면 <see cref="RejectReason.Rewound"/>(되돌림 알림 전에 낸 명령, 결정 149).</summary>
        public int Epoch;
    }

    /// <summary>개인 「한 수 무르기」(결정 149): To = 내 <see cref="UndoOffer.StepTo"/> 그대로. 다르거나 못 하는 때 · 일시정지면 그 좌석에 UNDO_DENIED.</summary>
    public sealed class UndoStep : RoomMsg
    {
        public int To;
    }

    /// <summary>방장만 · 게임 중 · 결과 화면: 되돌리기 목록을 달라(답 = <see cref="UndoPointsMsg"/>, 방장에게만).</summary>
    public sealed class RequestUndoPoints : RoomMsg { }

    /// <summary>방장만 · 게임 중 · 결과 화면(일시정지 중에도): 그 턴 시작(<see cref="UndoPoint.To"/>)으로 모두를 되돌린다. 목록에 없으면 UNDO_DENIED.</summary>
    public sealed class RewindTo : RoomMsg
    {
        public int To;
    }

    /// <summary>
    /// 일시정지(02 §5-7 — 결정 112 Q6 [가정 M7-88]): On = 걸기(방 설정 <see cref="RoomSettings.Pause"/>에 따라 바로 · 예약 · 투표) · 끔 = 내 정지 · 예약만 풀기(방장은 시스템 정지도) ·
    /// All = 방장의 [모두 해제](정지 · 예약 모두). 모두 풀리면 재개한다.
    /// </summary>
    public sealed class SetPaused : RoomMsg
    {
        public bool On;
        public bool All;
    }

    /// <summary>방장만: 끊긴 좌석을 바로 자리 비움으로(02 §6-2). 끄기는 돌아오면 저절로 풀린다.</summary>
    public sealed class MarkAway : RoomMsg
    {
        public int Seat;
    }

    /// <summary>투표를 연다(02 §6-2): 게임 중 · 대상이 아닌 접속 좌석. 연 좌석은 찬성으로 친다. 방장은 강퇴 대상이 될 수 없다.</summary>
    public sealed class StartVote : RoomMsg
    {
        public int Target;
        public VoteKind Kind;
    }

    /// <summary>열린 투표에 찬반(대상이 아닌 접속 좌석). 반대 하나면 부결이다.</summary>
    public sealed class CastVote : RoomMsg
    {
        public int VoteId;
        public bool Yes;
    }

    /// <summary>방장만: 자리 확인 대기의 연결을 접속하지 않은 좌석에 맡긴다 — 그 좌석의 토큰이 새 토큰으로 바뀐다(02 §6-3).</summary>
    public sealed class AssignSeat : RoomMsg
    {
        public int ClaimId;
        public int Seat;
    }

    /// <summary>방장만: 자리 확인 대기의 연결을 들이지 않는다(E_SEAT_REFUSED로 끊는다).</summary>
    public sealed class RefuseClaim : RoomMsg
    {
        public int ClaimId;
    }

    /// <summary>
    /// 「이번 배틀 묻지 않기」(04 §5 ① — 본인 · 게임 중 · 배틀 중): 이 배틀 동안 내 그 갈래 숨은 손패 창(HandGated — 지금은 운명 카드 Fate뿐)을 묻지 않고 최소 시간에 「안 씀」.
    /// 배틀이 바뀌면 풀린다. On = false면 끈다.
    /// </summary>
    public sealed class SetAutoPass : RoomMsg
    {
        public ChoiceSub Sub = ChoiceSub.Fate;
        public bool On = true;
    }

    /// <summary>방장만 · 결과(S64) 뒤: 같은 방 · 같은 자리로 로비(02 §3-1). 다음 판은 새 시드 · 새 SaveId(<see cref="HostConfig.Store"/>).</summary>
    public sealed class ReturnToLobby : RoomMsg { }

    /// <summary>지금 보기를 다시 받는다(02 §4-4 — 델타 기준이 어긋날 때).</summary>
    public sealed class RequestResync : RoomMsg { }

    /// <summary>방을 나간다. 로비면 자리를 비우고, 게임 중이면 끊김과 같다.</summary>
    public sealed class LeaveRoom : RoomMsg { }

    // ── 호스트 → 손님 (02 §4-3) ──

    public sealed class Welcome : ServerMsg
    {
        public int YourSeat;
        public string RoomId;
        public bool IsHost;
        public int ProtocolVersion = Protocol.Version;
    }

    /// <summary>방 상태가 바뀔 때 전원(02 §4-3 RoomUpdate).</summary>
    public sealed class RoomUpdate : ServerMsg
    {
        public RoomState Room;
    }

    /// <summary>알림(끊김·재접속·시간 초과 처리·자리 비움·일시정지·투표 결과). Seat = 대상 좌석(없으면 −1), Kind = 시간 초과된 선택 종류, Vote = 투표 알림의 종류, Round = 무르기 · 되돌리기 알림의 되돌린 라운드(그 밖은 0).</summary>
    public sealed class NoticeMsg : ServerMsg
    {
        public string Code;
        public int Seat = -1;
        public ChoiceKind Kind;
        public VoteKind Vote;
        public int Round;
    }

    /// <summary>되돌리기 목록(<see cref="RequestUndoPoints"/>의 답 — 요청한 방장에게만, 새것부터).</summary>
    public sealed class UndoPointsMsg : ServerMsg
    {
        public List<UndoPoint> Points = new List<UndoPoint>();
    }

    /// <summary>시계만 바뀔 때(일시정지·자리 비움·끊김 — 02 §4-3 ClockUpdate).</summary>
    public sealed class ClockUpdate : ServerMsg
    {
        public ClockState Clock;
    }

    /// <summary>로비 한 줄(02 §3-1 — 얼굴·닉네임·♛ 방장·PC/웹·준비·연결·자리 비움). 핑은 Net이 채운다.</summary>
    public sealed class RoomSeat
    {
        public string Nickname;
        public Platform Platform;
        public bool Host;
        public bool Ready;
        public bool Connected;
        public bool Away;
        /// <summary>강퇴 투표로 나갔다(끝까지 기본값).</summary>
        public bool Kicked;
        /// <summary>좌석의 주인공 키(<see cref="Protagonists.Keys"/> — P06). 모든 좌석이 서로의 것을 본다.</summary>
        public string Protagonist;
    }

    public sealed class RoomState
    {
        public RoomPhase Phase;
        public string RoomId;
        public RoomSettings Settings;
        /// <summary>로비: 자리 수(설정 Seats)만큼, null = 빈 자리(들어온 순서대로 빈 자리에 앉는다). 게임 중: 좌석 순서(빈 자리는 시작 때 빠졌다).</summary>
        public List<RoomSeat> Seats = new List<RoomSeat>();
        /// <summary>[게임 시작]을 켤 수 있나(2명 이상 · 손님 모두 준비 · 모두 접속).</summary>
        public bool CanStart;
        public List<int> PausedBy = new List<int>();
        /// <summary>일시정지 예약한 좌석(오름차순 — 그 좌석의 차례나 선택이 오면 정지로 옮긴다, 결정 112 Q6).</summary>
        public List<int> PauseReserved = new List<int>();
        /// <summary>열린 투표(없으면 null).</summary>
        public RoomVote Vote;
        /// <summary>자리 확인 대기 목록 — 방장에게만 채운다(02 §4-3). 손님에게는 빈 목록.</summary>
        public List<RoomClaim> Claims = new List<RoomClaim>();
        /// <summary>
        /// 방장의 연출 조정 값(Host — 기본값과 다른 것만, 02 §3-2 「연출 조정 값」 숨은 줄 [가정 M7-73]): 판이 시작될 때의 값을 게임 중 내내 싣는다. 손님은 방 판에 들어갈 때 맞춘다
        /// (<see cref="Tunables.MatchHost"/> — 방장 멈춤과 내 화면 대기열의 길이가 같아진다). 로비 · 손대지 않은 방은 빈 목록.
        /// </summary>
        public List<TuneValue> Tune = new List<TuneValue>();
        /// <summary>방장이 플레이 기록을 보낸다(결정 126 ④ — 방장 PC의 설정 · 방을 열 때의 값 <see cref="HostConfig.Records"/>). 로비에 알림 한 줄(손님 · 방장 모두). 손님의 PC는 보내지 않는다.</summary>
        public bool Records;
    }

    /// <summary>열린 투표 하나(02 §6-2). Waiting = 아직 답하지 않은 투표 좌석(대상을 뺀 접속 좌석 중), LeftMs = 남은 시간(바깥 시각 — 일시정지 중에도 흐른다 [가정 M7-18]).</summary>
    public sealed class RoomVote
    {
        public int Id;
        public VoteKind Kind;
        public int Target;
        public int By;
        public List<int> Yes = new List<int>();
        public List<int> Waiting = new List<int>();
        public long LeftMs;
    }

    /// <summary>자리 확인 대기 한 줄(02 §6-3): 닉네임 · PC/웹 · 추천 좌석(닉네임이 같은 접속하지 않은 좌석, 없으면 −1). 토큰은 싣지 않는다.</summary>
    public sealed class RoomClaim
    {
        public int Id;
        public string Nickname;
        public Platform Platform;
        public int Suggested = -1;
    }

    /// <summary>
    /// 시계 표시(02 §5-7 끝 줄): 호스트만 세고 손님은 남은 ms를 자기 화면에서만 줄인다. PausedBy에 −1(<see cref="HostSession.SystemPause"/>) = 시스템 정지.
    /// AnimLeftMs = 방장 멈춤(연출) 남은 ms — 그동안 시계 표시도 멈춘다. HoldLeftMs = 그 좌석이 범위 안인 차단 컷신 남은 ms(좌석마다 다르다 — 그동안 그 좌석의 답은 E_HOLD).
    /// </summary>
    public sealed class ClockState
    {
        public ClockPreset Preset;
        public List<int> PausedBy = new List<int>();
        public long AnimLeftMs;
        public long HoldLeftMs;
        public List<SeatClock> Seats = new List<SeatClock>();
    }
}
