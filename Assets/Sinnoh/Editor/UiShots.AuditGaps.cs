using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M8 · M9 마감 감사 2차가 「장면이 없어 못 봤다」고 남긴 것(A11 — <c>Logs/Review/m9/close-audit/README.md</c>)을 채우는 장면:
    /// 결정 123 · T25 전설 장소 피규어의 나타남(유물을 얻으면 커지며 선다) · 사라짐(그 전설을 이기면 없다), 결정 128 수호신 · 실버디 피규어(울트라 버스트 판에서만),
    /// 결정 121 · T13 울트라 버스트 방 로비의 조종 줄 잠금. 장면마다 그 상태(서 있나 · 크기 · 잠김)를 단정한다. 모두 보드 위 화면이다. 그림 <c>Logs/Review/m10/audit-gaps/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] AuditGapScreens =
        {
            // T25 나타남: 감사의 꽃밭에 선 좌석 — 아무도 그라시데아꽃이 없다(피규어 없음) → 옆 칸 숨겨진 장소에서 그라시데아꽃을 얻은 뒤 커지는 중 → 다 선 때
            Screen("a11-t25-hidden", s => SiteAppear(s, 0)),
            Screen("a11-t25-grow", s => SiteAppear(s, 1)),
            Screen("a11-t25-stand", s => SiteAppear(s, 2)),
            // T25 사라짐: 마그마스톤을 가진 좌석이 마그마의 방에 선 판(히드런이 서 있다) → 히드런을 이기고 [확인] 뒤 보드(피규어 없음)
            Screen("a11-t25-before", s => SiteGone(s, false)),
            Screen("a11-t25-gone", s => SiteGone(s, true)),
            // 결정 128: 조건 유물이 없는 전설 장소 넷(호수 동굴 셋 · 신오신전) — 울트라 버스트 판은 섰고 신오의 심장 판은 없다 · 의지의 시련 뒤 아그놈을 이기면 아그놈만 사라진다
            Screen("a11-d128-ub", s => GuardianSites(s, true)),
            Screen("a11-d128-heart", s => GuardianSites(s, false)),
            Screen("a11-d128-gone", GuardianGone),
            // T13 · 결정 121: 울트라 버스트 방의 방장 로비 — 사람 조종으로 저장된 방이라도 갤럭시단 · UB 줄은 「AI 조종」에 잠김 · 「사람 조종」 툴팁
            Screen("a11-t13-ub-lock", s => UbLobbyLock(s, false)),
            Screen("a11-t13-ub-lock-tip", s => UbLobbyLock(s, true)),
        };

        const string GardenOfGratitude = "legendary_site.garden_of_gratitude", LakeValor = "legendary_site.lake_valor_cavern";

        static readonly string[] GuardianSiteKeys = { "legendary_site.lake_verity_cavern", "legendary_site.lake_acuity_cavern", LakeValor, "legendary_site.temple_of_sinnoh" };

        // 그 전설 장소의 피규어 키(피규어 표의 키 = 전설 카드의 첫 키)
        static string SiteKey(Ctx ctx, string site)
        {
            int f = ctx.Fixtures.Ids.IndexOf(site);
            if (f < 0) throw new InvalidOperationException("고정물 " + site + "가 없다");
            return ctx.Cards.Pokemon[ctx.Fixtures.Fixtures[f].Legendary[0]].Key;
        }

        // 그 칸을 가까이 본다(피규어가 보이게)
        static void LookAt(UiShot s, GameFlow flow, int cell)
        {
            s.Rig.Set(BoardCameraRig.Zoom.Close, s.Rig.Yaw, s.Board.CellTop(cell));
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
        }

        // stage 0 = 공개 전(없음) · 1 = 커지는 중(나타남 길이의 반) · 2 = 다 섰다
        static void SiteAppear(UiShot s, int stage)
        {
            var ctx = s.Board.Ctx;
            int site = FixtureCell(ctx, GardenOfGratitude), gracidea = ctx.Items.Ids.IndexOf("item.gracidea_flower");
            string key = SiteKey(ctx, GardenOfGratitude);
            var flow = Seats(s, 1, st => st.Seats[0].Cell = site);
            // 걸어서 닿는 옆 칸(토큰 · 고정물 없음)에 그라시데아꽃 숨겨진 장소를 둔다 — 걷기 칸은 HUD의 갈 수 있는 칸에서 고른다(지형 · 높이 규칙 그대로)
            var near = MoveRules.Reachable(ctx.Board, site, 1, flow.Client.View.Options.ElevationMode);
            int spot = -1;
            bool Fixed(int c)
            {
                foreach (var x in ctx.Fixtures.Fixtures) if (x.Cell == c) return true;
                return false;
            }
            foreach (int c in flow.Hud.Reach)
                if (spot < 0 && c != site && c >= 0 && c < near.Length && near[c] == 1 && !flow.Client.View.Tokens.Exists(t => t.Cell == c && !t.Destroyed) && !Fixed(c)) spot = c;
            if (spot < 0) throw new InvalidOperationException("감사의 꽃밭 옆에 숨겨진 장소를 둘 빈 칸이 없다");
            flow.Rebase(st =>
            {
                var token = st.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && t.Def == gracidea)
                            ?? st.Tokens.Find(t => t.Kind == TokenKind.HiddenSpot && !t.FaceUp && !t.Destroyed && t.Cell >= 0)
                            ?? throw new InvalidOperationException("숨겨진 장소 토큰이 없다");
                token.Def = gracidea;
                token.Cell = spot;
                token.FaceUp = false;
                token.Destroyed = false;
                token.Loose = false;
            });
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            DealSettle(s, flow, MyMap);
            if (flow.FigureShown(key, out _)) throw new InvalidOperationException("아무도 그라시데아꽃이 없는데 쉐이미 피규어가 서 있다(결정 123)");
            if (stage > 0)
            {
                if (!flow.Hud.Walk(spot)) throw new InvalidOperationException("옆 칸으로 걷기가 거절됐다");
                for (int i = 0; i < 80 && !(flow.InputOpen && flow.Client.Choice(ChoiceKind.HiddenSpot) != null && flow.Hud.Shows(BoardHud.Reveal)); i++) Pass(flow, 100);
                if (flow.Client.Choice(ChoiceKind.HiddenSpot) == null) throw new InvalidOperationException("숨겨진 장소에서 「공개하겠습니까?」가 열리지 않았다");
                if (!flow.Hud.Press(BoardHud.Reveal)) throw new InvalidOperationException("[공개]가 거절됐다");
                if (flow.FigureShown(key, out _)) throw new InvalidOperationException("유물 연출이 다 보이기 전에 피규어가 섰다");
                // 유물 연출(30행)이 다 보인 뒤(대기열이 빈 때) 나타나기 시작한다 — 처음 선 프레임부터 그 길이만큼
                for (int i = 0; i < 400 && (flow.Fx == null || !flow.Fx.Sequencer.IsEmpty); i++)
                {
                    Pass(flow, 50);
                    if (!flow.Fx.Sequencer.IsEmpty && flow.FigureShown(key, out _)) throw new InvalidOperationException("대기열이 돌고 있는데 피규어가 섰다");
                }
                for (int i = 0; i < 100 && !flow.FigureShown(key, out _); i++) Pass(flow, 16);
                if (!flow.FigureShown(key, out float first)) throw new InvalidOperationException("그라시데아꽃을 얻었는데 쉐이미 피규어가 서지 않았다(결정 123)");
                if (first > 0.2f) throw new InvalidOperationException("나타남이 0에서 커지지 않았다(처음 크기 " + first + ")");
                Pass(flow, stage == 1 ? GameFlow.SiteAppearMs / 2 : GameFlow.SiteAppearMs + 100);
                flow.FigureShown(key, out float size);
                if (stage == 1 ? !(size > 0.3f && size < 0.95f) : Mathf.Abs(size - 1f) > 0.01f) throw new InvalidOperationException("쉐이미 피규어 크기가 박자와 다르다: " + size + " (단계 " + stage + ")");
                if (!flow.Client.View.Seats[0].Bag.Contains(gracidea)) throw new InvalidOperationException("그라시데아꽃이 가방에 없다");
            }
            LookAt(s, flow, site);
        }

        // 마그마의 방: 마그마스톤 · 강한 파티를 가진 혼자 판 — after면 히드런을 이기고 결과 창 [확인] 뒤 보드
        static void SiteGone(UiShot s, bool after)
        {
            var ctx = s.Board.Ctx;
            int cell = FixtureCell(ctx, MagmaChamber), fixture = ctx.Fixtures.Ids.IndexOf(MagmaChamber);
            string key = SiteKey(ctx, MagmaChamber);
            var flow = Seats(s, 1, st =>
            {
                GiveTeam(ctx, st);
                st.Seats[0].Cell = cell;
                st.Seats[0].Bag.Add(ctx.Items.Ids.IndexOf("item.magma_stone"));
            });
            if (!flow.FigureShown(key, out float stood) || stood < 0.99f) throw new InvalidOperationException("마그마스톤을 가진 판인데 히드런 피규어가 다 서 있지 않다(" + stood + ")");
            if (after)
            {
                WinBack(s, flow, "히드런", true);
                if (LegendRules.SiteCard(flow.Client.View, ctx, fixture) >= 0) throw new InvalidOperationException("이겼는데 히드런이 전설 덱에 남았다");
                if (flow.FigureShown(key, out _)) throw new InvalidOperationException("히드런을 이긴 뒤에도 피규어가 서 있다(결정 123 · T25)");
            }
            LookAt(s, flow, cell);
        }

        // [찾아보기] → 이길 때까지 싸워 결과 창 [확인] → 보드의 대기열이 비고 사라지는 연출(빛으로 흩어짐)이 끝날 때까지
        static void WinBack(UiShot s, GameFlow flow, string foe, bool search)
        {
            if (search && !flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException(foe + ": [찾아보기]가 거절됐다");
            var screen = Fight(flow, b => b.ShowsResult);
            for (int i = 0; i < 40 && (screen.AskingReward || !screen.Ready); i++)
            {
                if (screen.AskingReward) screen.PressReward(screen.Rewards.Count - 1);
                Pass(flow, BattleStepMs);
            }
            if (flow.Battle == null || !flow.Battle.Won) throw new InvalidOperationException(foe + "에게 이기지 못했다");
            if (!screen.Confirm()) throw new InvalidOperationException(foe + ": 결과 창 [확인]이 거절됐다");
            for (int i = 0; i < 300 && (flow.Stage.Current != null || flow.Fx == null || !flow.Fx.Sequencer.IsEmpty); i++) Pass(flow, 100);
            Pass(flow, ScatterBeats.Ms + 100);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
        }

        // 호수 동굴(입지호수)에 선 판 — 울트라 버스트 판이면 넷 모두 섰고, 신오의 심장 판이면 넷 모두 없다
        static void GuardianSites(UiShot s, bool ub)
        {
            var ctx = s.Board.Ctx;
            int cell = FixtureCell(ctx, LakeValor);
            var flow = ub ? UbGame(s, st => st.Seats[0].Cell = cell) : Seats(s, 1, st => st.Seats[0].Cell = cell);
            foreach (string site in GuardianSiteKeys)
            {
                bool shown = flow.FigureShown(SiteKey(ctx, site), out float size);
                if (shown != ub || ub && size < 0.99f) throw new InvalidOperationException(site + " 피규어: 보임 " + shown + " · 크기 " + size + " (" + (ub ? "울트라 버스트 판 — 서야 한다" : "신오의 심장 판 — 없어야 한다") + ", 결정 128)");
            }
            LookAt(s, flow, cell);
        }

        // 의지의 시련을 넘긴 울트라 버스트 판 — 입지호수 동굴에서 아그놈을 이기면 아그놈 피규어만 사라지고 나머지 셋은 그대로
        static void GuardianGone(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = UbAt(s, Lake, more: st => st.Ub.TrialsOvercome[(int)Shard.Willpower] = true);
            WinBack(s, flow, "아그놈", false);   // UbAt이 [찾아보기]를 눌렀다
            foreach (string site in GuardianSiteKeys)
            {
                bool want = site != LakeValor, shown = flow.FigureShown(SiteKey(ctx, site), out _);
                if (shown != want) throw new InvalidOperationException(site + " 피규어: 보임 " + shown + " (아그놈을 이긴 뒤 — " + (want ? "그대로 서야 한다" : "사라져야 한다") + ", 결정 128)");
            }
            LookAt(s, flow, FixtureCell(ctx, LakeValor));
        }

        // 울트라 버스트 방(사람 조종으로 저장된 갤럭시단 줄)의 방장 로비 — 갤럭시단 · UB 줄 모두 「AI 조종」이 골라져 있고 둘 다 잠김, 눌러도 보내지 않는다
        static void UbLobbyLock(UiShot s, bool tip)
        {
            var lobby = new FakeLobby
            {
                IsHost = true, Seat = 0, JoinCode = "Q7K2MZ",
                Room = new RoomState
                {
                    Phase = RoomPhase.Lobby, RoomId = "room-shot",
                    Settings = new RoomSettings { Seats = 4, Campaign = Campaign.UltraBurst, Galactic = Control.Human },
                    Seats = new List<RoomSeat>
                    {
                        new RoomSeat { Nickname = "방장트레이너", Host = true, Connected = true, Protagonist = "pt_player_male" },
                        new RoomSeat { Nickname = "빛나", Ready = true, Connected = true, Protagonist = "pt_player_female" },
                        null, null, null, null,
                    },
                },
            };
            var flow = RoomLobby(s, lobby);
            var root = flow.Stage.Root;
            VisualElement Opt(string row, string id) => root.Q("room-set-opt-" + row + "-" + id) ?? throw new InvalidOperationException("요소 없음: room-set-opt-" + row + "-" + id);
            foreach (string row in new[] { "galactic", "ultra" })
            {
                var ai = Opt(row, "ai");
                var human = Opt(row, "human");
                if (!ai.ClassListContains("sn-opt-picked") || human.ClassListContains("sn-opt-picked")) throw new InvalidOperationException(row + " 줄: 「AI 조종」이 골라져 있지 않다(결정 121 — 사람 조종으로 저장된 방이라도)");
                if (!ai.ClassListContains("sn-off") || !human.ClassListContains("sn-off")) throw new InvalidOperationException(row + " 줄이 잠기지 않았다(결정 121)");
                int sent = lobby.Sent.Count;
                using (var e = ClickEvent.GetPooled())
                {
                    e.target = human;
                    human.SendEvent(e);
                }
                if (lobby.Sent.Count != sent) throw new InvalidOperationException(row + " 줄의 「사람 조종」을 누르니 설정을 보냈다");
            }
            if (tip)
            {
                flow.Stage.Tips.Show(Opt("galactic", "human"));
                s.Panel.Tick(3);
                string want = Rekkon.UiKit.KoText.JoinWords(flow.Stage.T("screen.room.set.control.ub_lock.tip"));   // 툴팁 본문은 어절을 이어 붙여 그린다
                bool found = false;
                flow.Stage.Tips.Element.Query<Label>().ForEach(l => found |= l.text != null && l.text.Contains(want));
                if (!found) throw new InvalidOperationException("「사람 조종」 툴팁이 울트라 버스트 잠금 문구가 아니다");
            }
        }
    }
}
