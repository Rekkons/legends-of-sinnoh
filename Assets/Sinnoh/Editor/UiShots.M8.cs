using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M8 「연결」의 확인 장면: 솔로 설정 · 방 설정의 갤럭시단 AI 「강함」 줄(고른 모습 + 마우스 오버 설명) · 강한 AI의 자막(「수읽기(승산 n%)」 — DS 위 가장자리 띠).
    /// 자막 장면은 찾아 둔 판(Defeat)을 「강함」으로 시작해 첫 사유 있는 AI 답까지 간다 — AI는 이 스레드에서 바로 묻는다(<see cref="GameFlow.AiThread"/> 끔 — 같은 판이면 같은 그림).
    /// 그림은 <c>Logs/Review/m8/connect/</c>에 모은다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] M8Screens =
        {
            Screen("m8-solo-strong", s =>                                                   // 솔로 설정: 「강함」을 고르고 마우스를 올린 모습
            {
                var flow = s.Flow();
                flow.OpenSolo();
                var solo = (SoloScreen)flow.Stage.Current;
                if (!solo.Set(SoloScreen.GalacticAi, "strong") || solo.Settings.GalacticAi != GalacticAiLevel.Strong) throw new InvalidOperationException("솔로 설정에서 「강함」을 고르지 못했다");
                s.Panel.Tick(3);
                var opt = flow.Stage.Root.Q("solo-opt-" + SoloScreen.GalacticAi + "-strong");
                if (opt == null || !opt.ClassListContains("sn-opt-picked") || opt.ClassListContains("sn-off")) throw new InvalidOperationException("「강함」 칸이 골라진 꼴이 아니다(회색?)");
                flow.Stage.Tips.Show(opt);
            }),
            Screen("m8-room-strong", s =>                                                   // 방 설정(방장): 갤럭시단 AI 「강함」이 골라진 방 + 마우스 오버
            {
                var lobby = FakeLobby.Internet();
                lobby.Room.Settings.GalacticAi = GalacticAiLevel.Strong;
                var flow = RoomLobby(s, lobby);
                var opt = flow.Stage.Root.Q("room-set-opt-galactic_ai-strong") ?? throw new InvalidOperationException("방 설정에 「강함」 칸이 없다");
                if (!opt.enabledSelf) throw new InvalidOperationException("방 설정의 「강함」이 꺼져 있다");
                flow.Stage.Tips.Show(opt);
            }),
            Screen("battle-ai-read", s => ReadMoment(s, 300)),                              // 강한 AI의 자막: 「갤럭시단의 선택: 수읽기(승산 n%)」
        };

        // 찾아 둔 판(Defeat)을 「강함」으로 시작해 사유 있는 AI 답이 화면 대기열에 들어오면 그 단계 시작까지 건너뛰고 연출 시각만 ms 보낸다(AiMoment와 같은 길)
        static void ReadMoment(UiShot s, int ms)
        {
            var flow = s.Flow();
            flow.AiThread = false;
            var pilot = FlowPilot.Start(flow, SoloTrail.Defeat, galactic: GalacticAiLevel.Strong);
            flow.Layout(s.Width, s.Height);
            void Wait(int x)
            {
                flow.Tick(x);
                if (!FxShots.AiQueued(flow)) flow.Stage.Tweens.Advance(x);
            }
            if (!FlowPilot.Run(flow, pilot, () => FxShots.AiQueued(flow), Wait, BattleStepMs, BattleStepMax * 10)) throw new InvalidOperationException("defeat(강함): AI 자막까지 가지 못했다");
            flow.Skip(flow.MsUntil(st => st.Row == Sinnoh.Fx.AiLineBeats.Row && Sinnoh.Fx.AiLineBeats.Answer(st) != null));
            flow.Stage.Tweens.Advance(ms);
            var battle = flow.Stage.Current as BattleScreen ?? throw new InvalidOperationException("배틀 화면이 아니다");
            s.Panel.Tick(1);
            string rule = flow.Stage.T("screen.battle.ai.rule.lookahead");
            if (battle.AiLine == null || !battle.AiLine.Contains(rule)) throw new InvalidOperationException("강한 AI 자막이 아니다: " + (battle.AiLine ?? "(없음)"));
            UnityEngine.Debug.Log("[UiShots] battle-ai-read: " + battle.AiLine);
        }
    }
}
