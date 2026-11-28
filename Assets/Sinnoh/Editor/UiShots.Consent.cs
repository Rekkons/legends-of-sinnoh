using System;
using System.Collections.Generic;
using Sinnoh.Client;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 플레이 기록 동의 화면(결정 126 ④ — M9): 첫 실행 알림(타이틀 위 창) · 설정 「플레이 기록 보내기」 줄 · [내 기록 지우기] 물음 · 결과 · 방장이 켠 방의 로비 줄 · 크레딧의 개인 정보 처리 줄.
    /// 흐름에 서버를 부르지 않는 창구(<see cref="PlayUploads.Quiet"/>)를 넣어 PC 플레이어처럼 연다. 장면마다 창 · 판이 화면 안 · 글 넘침을 단정한다. 모두 보드 위 화면이다.
    /// 그림은 <c>Logs/Review/m9/consent/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] ConsentScreens =
        {
            Screen("consent-title", s => ConsentTitle(s)),                                     // 첫 실행: 타이틀 위 알림 창(무엇 · 왜 · 끄는 곳 + [확인] · [설정 열기])
            Screen("consent-title-tip", s =>                                                   // 「개인 정보 처리 안내」에 마우스를 올린 모습
            {
                var flow = ConsentTitle(s);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("title-notice-more"));
            }),
            Screen("consent-settings", s => ConsentSettings(s, null)),                         // 설정: 「플레이 기록 보내기」 켬 · 끔 + [내 기록 지우기]
            Screen("consent-settings-ask", s => ConsentSettings(s, set => set.AskDelete())),  // [내 기록 지우기] → 물음 + [지우기] · [취소]
            Screen("consent-settings-done", s => ConsentSettings(s, set =>                     // [지우기] → 결과 한 줄(가짜 창구 — 3개)
            {
                set.AskDelete();
                var t = set.DeleteRecords();
                if (!t.IsCompleted || t.Result != 3) throw new InvalidOperationException("지우기 결과가 3이 아니다");
            })),
            Screen("consent-settings-off", s => ConsentSettings(s, set => set.PickUpload(false))),   // 끔을 고른 모습
            Screen("consent-lobby-guest", s => ConsentLobby(s, FakeLobby.Guest())),            // 방장이 켠 방의 손님 로비: 알림 줄
            Screen("consent-lobby-host", s => ConsentLobby(s, FakeLobby.Internet())),          // 방장 로비(6명 · 참가 코드): 알림 줄
            Screen("consent-lobby-tip", s =>                                                   // 알림 줄에 마우스를 올린 모습
            {
                var flow = ConsentLobby(s, FakeLobby.Guest());
                flow.Stage.Tips.Show(flow.Stage.Root.Q("lobby-records"));
            }),
            Screen("consent-credits", s =>                                                     // 크레딧: 라이선스 줄 아래 개인 정보 처리 줄 + 마우스 오버
            {
                Credits(s, null);
                var flow = s.Made;
                var line = flow.Stage.Root.Q<Label>("credits-privacy") ?? throw new InvalidOperationException("개인 정보 처리 줄이 없다");
                if (!System.Text.RegularExpressions.Regex.IsMatch(line.text.Replace("⁠", ""), @"PRIVACY(\.ko)?\.md")) throw new InvalidOperationException("개인 정보 처리 줄에 PRIVACY.md · PRIVACY.ko.md가 없다(결정 138): " + line.text);
                flow.Stage.Tips.Show(line);
            }),
        };

        static GameFlow ConsentFlow(UiShot s)
        {
            var flow = s.Flow();
            flow.Uploads = PlayUploads.Quiet(() => PlayUploads.Allowed(flow.SoundPrefs), deletes: 3);
            return flow;
        }

        static GameFlow ConsentTitle(UiShot s)
        {
            var flow = ConsentFlow(s);
            flow.OpenTitle(TitleScreen.Solo);
            var title = flow.Stage.Current as TitleScreen ?? throw new InvalidOperationException("타이틀이 열리지 않았다");
            if (!title.NoticeShown) throw new InvalidOperationException("첫 실행 알림 창이 없다");
            RoomFit(s, flow, "title-notice-panel");
            return flow;
        }

        static GameFlow ConsentSettings(UiShot s, Action<SettingsScreen> act)
        {
            var flow = ConsentFlow(s);
            flow.SoundPrefs.PlayNotice = true;
            flow.OpenSettings();
            var set = flow.Stage.Current as SettingsScreen ?? throw new InvalidOperationException("설정이 열리지 않았다");
            if (flow.Stage.Root.Q("settings-row-upload") == null) throw new InvalidOperationException("「플레이 기록 보내기」 줄이 없다");
            act?.Invoke(set);
            RoomFit(s, flow, "settings-panel");
            RoomFit(s, flow, "settings-foot");
            return flow;
        }

        static GameFlow ConsentLobby(UiShot s, FakeLobby lobby)
        {
            lobby.Room.Records = true;
            var flow = RoomLobby(s, lobby);
            if (flow.Stage.Root.Q("lobby-records") == null) throw new InvalidOperationException("로비 알림 줄이 없다");
            RoomFit(s, flow, "lobby-foot");
            return flow;
        }
    }
}
