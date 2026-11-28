using System;
using System.Collections.Generic;
using System.Globalization;
using Sinnoh.Data;
using Sinnoh.Rules.Session;
using UnityEngine;

namespace Sinnoh.Client
{
    // 게임 안 [버그 신고](M9 — 결정 132 ①): 게임 메뉴의 쪽(BoardMenuScreen.BugPage) · 타이틀 설정에서 연 창(BugReportScreen)이 같은 몸(BugReportPanel)을 쓴다.
    // 보내는 길은 플레이 기록 올리기 창구(PlayUploads.SendBug — Cloud Save Player Files · 익명 로그인, 파일 이름 앞머리 bug_).
    public sealed partial class GameFlow
    {
        /// <summary>
        /// 버그 신고를 보낼 수 있나 — 올리는 창구가 있는 PC 플레이어만(<see cref="Uploads"/>). 에디터는 창구가 없어 [버그 신고]가 「PC판에서만」으로 잠긴다.
        /// 플레이 기록 설정이 꺼져 있어도 보낸다(사람이 직접 누른 것 [가정 plan §5 #680]).
        /// </summary>
        public bool BugReady => Uploads != null;

        /// <summary>
        /// 버그 신고에 싣는 로그 파일: 플레이어 = Player.log(<see cref="Application.consoleLogPath"/>), 에디터 = 없음(null — Editor.log를 싣지 않는다).
        /// 시험 · 캡처는 임시 파일을 준다(실제 Player.log를 읽지 않게).
        /// </summary>
        public string BugLogPath
        {
            get => bugLog ?? (Application.isEditor ? null : Application.consoleLogPath);
            set => bugLog = value;
        }
        string bugLog;

        /// <summary>어디서 보내나: tutorial · host · guest · solo(판이 열려 있다) · title.</summary>
        public string BugRole =>
            Tutorial.Running ? "tutorial"
            : Room.InGame ? (Room.Live.IsHost ? "host" : "guest")
            : Client != null ? "solo" : "title";

        /// <summary>지금 판의 저장 폴더(솔로 = 내 저장 · 방장 = 방 저장). 판이 없음 · 튜토리얼 · 손님 · 저장하지 않는 판이면 null — 판 기록 없이 보낸다.</summary>
        public string BugSaveDir => Client == null || Tutorial.Running ? null : SaveId != null ? SaveStore.PathOf(saves, SaveId) : Room.HostSaveDir;

        /// <summary>버그 신고 묶음의 머리(설명 + 판 · 화면 몫 — 로그 · 기록 몫은 <see cref="BugReport.Pack"/>이 채운다).</summary>
        public BugReportInfo BugInfo(string description)
        {
            var size = window.Size;
            return new BugReportInfo
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                Description = description,
                GameVersion = Application.version,
                RulesVersion = SaveRecord.CurrentRulesVersion,
                ProtocolVersion = Protocol.Version,
                Lang = Lang,
                Screen = size.x.ToString(CultureInfo.InvariantCulture) + "x" + size.y.ToString(CultureInfo.InvariantCulture) + (window.Fullscreen ? " full" : ""),
                Platform = Application.platform.ToString(),
                Role = BugRole,
                SentAtMs = clock(),
            };
        }

        /// <summary>
        /// 로그에서 가릴 글: 사용자 폴더 경로 · 사용자 이름 · 내 닉네임 · 방 좌석의 닉네임 · 방에 있으면 내 재접속 토큰(방 밖에서는 토큰을 새로 만들지 않게 읽지 않는다).
        /// 판 기록 쪽은 <see cref="PlayRecord.Scrub"/>가 지운다.
        /// </summary>
        public List<string> BugSecrets()
        {
            var list = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                list.Add(home);
                list.Add(home.Replace('\\', '/'));
            }
            list.Add(Environment.UserName);
            list.Add(RoomIdentity.Nickname);
            var live = Room.Live;
            if (live != null)
            {
                list.Add(RoomIdentity.Token);
                if (live.Room != null && live.Room.Seats != null)
                    foreach (var seat in live.Room.Seats)
                        if (seat != null) list.Add(seat.Nickname);
            }
            return list;
        }

        /// <summary>타이틀 설정의 [버그 신고]: 설정 자리에 신고 창을 연다([취소] · [닫기] · Esc = 설정으로). 창구가 없으면 아무 일도 없다.</summary>
        public void OpenTitleBug()
        {
            if (!BugReady || !(Stage.Current is SettingsScreen set) || set.InGame) return;   // 창구가 없으면(에디터) 열지 않는다
            Stage.Open(new BugReportScreen(this, OpenSettings));
            Restage();
        }
    }
}
