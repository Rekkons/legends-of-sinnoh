using System;
using System.Collections.Generic;
using System.IO;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M6 「UB 시작」의 UI 캡처 장면: 타이틀에서 여는 울트라 버스트 혼자 하기 판(S09 「캠페인」 울트라 버스트 → 셋업 S12·S13 → 보드) · UB 판 저장·이어 받기 · UB 기록 줄(S21) ·
    /// 네크로즈마 2·3페이즈 폼(보드 피규어 · DS 도트). 균열 게이지 · UB 턴 띠 · 균열 카드 창(목업 M1~M3)은 그리지 않는다.
    /// 혼자 하는 판은 좌석 하나라 [턴 종료]마다 라운드가 끝나 UB 턴이 돈다. 저장은 임시 폴더만 쓴다(RULE-SAVE).
    /// </summary>
    public static partial class UiShots
    {
        static SoloSettings UbSolo() => new SoloSettings { Campaign = Campaign.UltraBurst, UltraOnslaught = true, UbDifficulty = UbDifficulty.VeteranTrainer };

        public static readonly KeyValuePair<string, Action<UiShot>>[] UbStartScreens =
        {
            // S09: 「캠페인」 울트라 버스트 — 울트라 버스트에서만 듣는 두 줄(울트라 온슬롯 · 난이도)이 켜진다(온슬롯 사용 · 베테랑을 고른 모습)
            Screen("ub-start-solo", s => UbSoloScreen(s)),
            Screen("ub-start-solo-tip", s =>                                    // 울트라 온슬롯 「사용」에 마우스를 올린 모습
            {
                var flow = UbSoloScreen(s);
                s.Panel.Tick(3);
                flow.Stage.Tips.Show(flow.Stage.Root.Q("solo-opt-" + SoloScreen.Onslaught + "-on"));
            }),
            // S12 고향 마을: 울트라 버스트에서는 울트라메가로폴리스(깨어진 세계)도 고향 마을이다(G12)
            Screen("ub-start-village", Fit(s => UbVillage(s))),
            // S13 스타팅 포켓몬 · 트레이너 카드(울트라 버스트도 같다)
            Screen("ub-start-pick", s => UbPick(s)),
            // 보드: 첫 내 턴 — 규토리 빨강·파랑·노랑 3씩 · 파티 n/6 · 깨어진 세계와 UB 9 · 네크로즈마(전체 줌)
            Screen("ub-start-board", Clean(s => UbStartBoard(s), "board-hud-party")),
            // S21 기록 창: UB 턴 둘 뒤(UB 이동 · 밟기 · 파괴 · 균열 강도)
            Screen("ub-start-log", Fit(s =>
            {
                var flow = UbStartBoard(s);
                UbEndTurns(s, flow, 2);
                flow.OpenLog();
                s.Panel.Tick(3);
                bool any = false;
                foreach (var line in flow.Log.Lines) any |= line.Line.Key == BoardLog.K + "ub.move";
                if (!any) throw new InvalidOperationException("기록에 UB 이동 줄이 없다");
            })),
            // 저장 → 타이틀 → 이어 받기: UB 턴 둘 뒤의 판이 같은 보드(UB 피규어 칸 · 깨어진 세계 · 부서진 칸)로 선다
            Screen("ub-start-resumed", s => UbResumed(s)),
            // 네크로즈마 페이즈 폼(보드 피규어 — 깨어진 세계 가운데, 지역 줌): 2페이즈 썬(황혼의 갈기) · 문(새벽의 날개) · 3페이즈 울트라네크로즈마
            Screen("ub-necrozma-dusk", s => NecrozmaLook(s, 2, UbNecrozma.Sun, UbNecrozma.DuskMane)),
            Screen("ub-necrozma-dawn", s => NecrozmaLook(s, 2, UbNecrozma.Moon, UbNecrozma.DawnWings)),
            Screen("ub-necrozma-ultra", s => NecrozmaLook(s, 3, UbNecrozma.Moon, UbNecrozma.Ultra)),
            // 82행 무거운 UB의 착지: 내려앉아 가장 눌린 순간(화면은 흔들지 않는다 — 결정 53 ②)
            Screen("row-82-heavy", s => UbTurnInto(s, st => st.Row == UbRows.Move && UbRows.First<UbMoved>(st) is UbMoved m
                && UbMoveBeats.Heavy(s.Board.Ctx.Cards.Pokemon[m.UbId].Key), 400 * (UbMoveBeats.HeavyLandPermille + 1000) / 2000)),
            // DS: 네크로즈마 토큰 칸에서 [찾아보기] → 그 페이즈 카드의 도트(2페이즈 썬 · 문 · 3페이즈 울트라네크로즈마 — DS 이름은 폼 이름을 뺀 「네크로즈마」, M6 「UB 배틀 화면 묶음」)
            Screen("battle-necrozma-dawn", s => NecrozmaBattle(s, 2, UbNecrozma.Moon)),
            Screen("battle-necrozma-dusk", s => NecrozmaBattle(s, 2, UbNecrozma.Sun)),
            Screen("battle-necrozma-ultra", s => NecrozmaBattle(s, 3, UbNecrozma.Moon)),
        };

        static GameFlow UbSoloScreen(UiShot s)
        {
            var flow = s.Flow();
            flow.OpenSolo();
            var solo = (SoloScreen)flow.Stage.Current;
            if (!solo.Set(SoloScreen.Campaign, "ub") || !solo.Set(SoloScreen.Onslaught, "on") || !solo.Set(SoloScreen.UbDifficulty, "veteran") || !solo.Set(SoloScreen.TrainerCards, "on"))   // 트레이너 카드 고르기까지 찍는다(기본값은 끔 — 결정 134)
                throw new InvalidOperationException("울트라 버스트 줄을 고르지 못했다");
            if (!solo.RowEnabled(SoloScreen.Onslaught) || !solo.RowEnabled(SoloScreen.UbDifficulty)) throw new InvalidOperationException("울트라 버스트인데 전용 줄이 꺼져 있다");
            return flow;
        }

        static GameFlow UbVillage(UiShot s, GameFlow made = null)
        {
            var flow = made ?? s.Flow();
            flow.StartSolo(UbSolo(), UbSeed, AiSeed);
            if (!(flow.Stage.Current is SetupVillageScreen v)) throw new InvalidOperationException("고향 마을 고르기가 열리지 않았다");
            int mega = flow.Ctx.Fixtures.Ids.IndexOf("village.ultra_megalopolis");
            if (!new List<int>(v.Villages).Contains(mega)) throw new InvalidOperationException("울트라 버스트인데 울트라메가로폴리스가 고향 마을 줄에 없다");
            if (!s.Board.DistortionWorldVisible) throw new InvalidOperationException("울트라 버스트 셋업인데 깨어진 세계가 숨어 있다");
            return flow;
        }

        // 무쇠시티(신오의 심장 장면과 같은 마을)를 확정하고 고르는 화면까지
        static GameFlow UbPick(UiShot s, GameFlow made = null)
        {
            var flow = PickVillage(UbVillage(s, made));
            if (!((SetupVillageScreen)flow.Stage.Current).Confirm()) throw new InvalidOperationException("고향 마을 확정이 거절됐다");
            flow.Stage.Tweens.Advance(flow.QueueLeftMs);
            if (!(flow.Stage.Current is SetupPickScreen)) throw new InvalidOperationException("고르는 화면이 열리지 않았다");
            return flow;
        }

        static GameFlow UbStartBoard(UiShot s, GameFlow made = null)
        {
            var flow = UbPick(s, made);
            var pick = (SetupPickScreen)flow.Stage.Current;
            pick.PickStarter(0);
            pick.PickCard(1);
            if (!pick.Confirm()) throw new InvalidOperationException("셋업 확정이 거절됐다");
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(flow.OpeningMs);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            for (int i = 0; i < 40 && !flow.InputOpen; i++) Pass(flow, 500);
            if (!flow.InputOpen || flow.Hud == null) throw new InvalidOperationException("첫 내 턴의 입력이 열리지 않았다");
            var me = flow.Client.View.Seats[0];
            if (me.OpenSlots != 6) throw new InvalidOperationException("파티 칸 " + me.OpenSlots + " ≠ 6");
            foreach (var c in new[] { ApricornColor.Red, ApricornColor.Blue, ApricornColor.Yellow })
                if (me.Apricorns[(int)c] < UltraBurstSetup.StartingApricorns) throw new InvalidOperationException(c + " 규토리 " + me.Apricorns[(int)c] + " < 3");
            if (flow.Client.View.Ub.Blessings != 1) throw new InvalidOperationException("베테랑인데 축복 " + flow.Client.View.Ub.Blessings);
            Beasts(s, flow);
            s.Rig.Set(BoardCameraRig.Zoom.Full, s.Rig.Yaw, flow.WideLook());
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            return flow;
        }

        // [턴 종료]를 n번(좌석 하나 — 매번 라운드 끝의 UB 턴이 돈다), 다음 내 턴의 입력이 열릴 때까지
        static void UbEndTurns(UiShot s, GameFlow flow, int n)
        {
            for (int t = 0; t < n; t++)
            {
                if (!flow.Hud.Press(BoardHud.EndTurn)) throw new InvalidOperationException("턴 종료가 거절됐다");
                for (int i = 0; i < 80 && !(flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null); i++)
                {
                    // 시간 초과 기본 답이 받는 선택(균열 카드 붙이기 등)은 흐름이 저절로 넘긴다 — 시간을 보낸다.
                    // 라운드 끝 「UB 턴을 건너뛰겠습니까?」(결정 84 U2 — 축복이 남았다)는 건너뛰지 않는다고 답한다
                    DeclineUbSkip(flow);
                    Pass(flow, 500);
                }
                if (!flow.InputOpen) throw new InvalidOperationException("UB 턴 뒤 내 턴이 오지 않았다");
            }
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            s.Panel.Tick(3);
        }

        static void UbResumed(UiShot s)
        {
            string root = Path.Combine(Path.GetTempPath(), "sinnoh-uishots-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var flow = UbStartBoard(s, s.Flow(root, () => SaveClock));
                UbEndTurns(s, flow, 2);
                string id = flow.SaveId ?? throw new InvalidOperationException("저장하지 않는 판이다");
                var before = flow.Session.HostCopy();
                ulong hash = flow.Session.Hash;
                flow.OpenTitle();
                if (s.Board.DistortionWorldVisible) throw new InvalidOperationException("타이틀 뒤(시험 판)에서 깨어진 세계가 보인다");
                if (!flow.Resume(id)) throw new InvalidOperationException("이어 받지 못했다: " + flow.ResumeError);
                flow.Layout(s.Width, s.Height);
                flow.Stage.Tweens.Advance(BoardReadyMs);
                if (flow.Session.Hash != hash) throw new InvalidOperationException("이어 받은 판이 끊은 판과 다르다");
                if (!s.Board.DistortionWorldVisible) throw new InvalidOperationException("이어 받은 울트라 버스트 판에서 깨어진 세계가 숨어 있다");
                Beasts(s, flow);
                var view = flow.Client.View;
                for (int i = 0; i < before.Tokens.Count; i++)
                    if (before.Tokens[i].Cell != view.Tokens[i].Cell || before.Tokens[i].Destroyed != view.Tokens[i].Destroyed)
                        throw new InvalidOperationException("토큰 " + i + "(" + before.Tokens[i].Kind + ")이 이어 받은 판에서 다르다");
                s.Rig.Set(BoardCameraRig.Zoom.Full, s.Rig.Yaw, flow.WideLook());
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        // 페이즈를 손본 4인 UB 판(UB 보드 장면과 같은 판)에서 네크로즈마 칸을 지역 줌으로 — 피규어가 그 페이즈 폼으로 섰는지
        static void NecrozmaLook(UiShot s, int phase, int coin, string want)
        {
            UbLook(s, st => { st.Ub.NecrozmaPhase = phase; st.Ub.SunMoon = coin; }, BoardCameraRig.Zoom.Region, (flow, st) => BeastCell(flow, st, TokenKind.Necrozma));
            var shown = s.Made.NecrozmaShown();
            if (shown != want) throw new InvalidOperationException("네크로즈마 피규어 " + (shown ?? "없음") + " ≠ " + want);
        }

        // 내 말을 네크로즈마 토큰 칸에 세운 판(페이즈 손봄) → [찾아보기] → 배틀 화면에서 내 차례
        static void NecrozmaBattle(UiShot s, int phase, int coin)
        {
            var flow = UbGame(s, st =>
            {
                st.Ub.NecrozmaPhase = phase;
                st.Ub.SunMoon = coin;
                foreach (var t in st.Tokens)
                    if (t.Kind == TokenKind.Necrozma) st.Seats[0].Cell = t.Cell;
            });
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException("네크로즈마 칸에서 [찾아보기]가 거절됐다");
            // 배틀 화면이 열리고 그 쪽 도트(배틀 기록의 보이는 값)가 그 페이즈 카드가 될 때까지 — 네크로즈마가 먼저 쓰러뜨려 판이 곧 끝나도 등장 순간을 찍는다
            int want = UbNecrozma.Card(flow.Ctx, phase, coin);
            bool shown = false;
            for (int i = 0; i < BattleStepMax && !shown; i++)
            {
                if (flow.Stage.Current is BattleScreen && flow.Battle != null)
                    for (int side = 0; side < 2; side++)
                        if (flow.Battle.Mon(side).Exists && flow.Battle.Mon(side).Def == want) shown = true;
                if (!shown) Pass(flow, 100);
            }
            if (!shown) throw new InvalidOperationException("배틀 화면에 네크로즈마 " + phase + "페이즈 도트(" + flow.Ctx.Cards.Pokemon[want].Key + ")가 서지 않았다");
            for (int i = 0; i < 6; i++) Pass(flow, 400);   // 조우 전환(22행) 줄무늬가 걷히고 도트가 다 나타난 뒤
            s.Panel.Tick(3);
        }
    }
}
