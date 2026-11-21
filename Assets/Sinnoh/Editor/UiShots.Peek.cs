using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M9 다듬기 P09 「남의 고르기 보기」 확인 장면(결정 112 Q16, 02 §4-10): 좌석 여럿 확인용 판에서 좌석 2(빛나)가 공개 선택을 열고, 가짜 방이 그 좌석의 「가리킴 · 누름」 소식을 받은 모습 —
    /// 위 가운데 줄의 후보 칸 · 좌석 색 테두리(가리킴) · 좌석 색 바탕(누름). 보내는 쪽(내 조우 창 · 은행상회 칸에서 마우스를 올림 → 방에 보냄 · 간격)도 여기서 단정한다. 실제 망은 PlayMode <c>RoomGameFlowTests</c>.
    /// 그림 <c>Logs/Review/m9/polish4/P09/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] PeekScreens =
        {
            Screen("room-peek-encounter", s => PeekShown(s, PeekEncounter(s), PeekSpot.Encounter, 1, false)),         // 빛나가 조우 카드 가운데 둘째를 가리킨다: 좌석 색 테두리
            Screen("room-peek-encounter-press", s => PeekShown(s, PeekEncounter(s), PeekSpot.Encounter, 0, true)),    // 첫째를 눌렀다: 좌석 색 바탕
            Screen("room-peek-encounter-idle", s => PeekShown(s, PeekEncounter(s), PeekSpot.Encounter, -1, false)),   // 가리키는 것이 없어도 후보 줄은 보인다(「○○ 조우 고르는 중」)
            Screen("room-peek-shrine", s => PeekShown(s, PeekShrine(s), PeekSpot.Shrine, 2, false)),                  // 사당 카드 3장 가운데 셋째를 가리킨다
            Screen("room-peek-bank", s => PeekShown(s, PeekTurn(s), PeekSpot.Bank, BankRules.Bottom, false)),         // 은행상회 아래 줄을 가리킨다(규토리 그림 줄)
            Screen("room-peek-bank-both", s => PeekShown(s, PeekTurn(s), PeekSpot.Bank, BankRules.Both, true)),       // [두 줄 모두]를 눌렀다: 두 줄 다 좌석 색 바탕
            Screen("room-peek-muted", s =>                                                                           // 그 좌석의 핑을 끈 화면: 후보 줄은 보이고 표시는 없다
            {
                var flow = PeekEncounter(s);
                var live = PeekRoom(s, flow);
                live.SetMuted(1, true, true);
                live.TakePeek(new PeekMsg { From = 1, Spot = PeekSpot.Encounter, Index = 0, Press = true });
                PeekLaid(s, flow);
                PeekWatched(s, flow, PeekSpot.Encounter, -1);
                if (flow.RoomLayer.PeekSeat != 1 || flow.RoomLayer.PeekIndex != -1 || flow.RoomLayer.PeekMarked(1)) throw new InvalidOperationException("핑을 끈 좌석의 표시가 보인다");
            }),
            Screen("room-peek-send", s =>                                                                            // 보내는 쪽: 내 조우 창에서 카드에 마우스를 올림 → 방에 보냄(간격 350ms · 마지막 모습만) · 고르면 누름
            {
                var flow = Seats(s, 2, st => PeekSpotFor(s, st, 0));
                long now = RoomNow;
                var live = new RoomLive(() => now) { Seat = 0, IsHost = true, Room = FakeGame(flow, true).Room, Clock = FakeGame(flow, true).Clock };
                flow.Room.ShowGame(live);
                var screen = PeekMine(s, flow);
                var choice = flow.Client.Choice(ChoiceKind.EncounterPick);
                int before = live.Sent.Count;
                if (!screen.Hover(0) || !screen.Hover(1) || !screen.Hover(0)) throw new InvalidOperationException("조우 카드에 올린 것이 방에 가지 않았다");
                if (live.Sent.Count != before + 1 || !(live.Sent[before] is SendPeek first) || first.Spot != PeekSpot.Encounter || first.Index != choice.Options.IndexOf(screen.Cards[0].Target) || first.Press)
                    throw new InvalidOperationException("첫 소식만 바로 가야 한다(간격 안의 것은 쥔다): " + (live.Sent.Count - before));
                now += PeekRate.GapMs - 1;
                Pass(flow, 0);
                if (live.Sent.Count != before + 1) throw new InvalidOperationException("간격이 지나기 전에 또 보냈다");
                screen.Hover(1);
                now += 1;
                Pass(flow, 0);   // 방 판 덮개 층이 매 프레임 마지막 모습을 보낸다
                if (live.Sent.Count != before + 2 || ((SendPeek)live.Sent[before + 1]).Index != choice.Options.IndexOf(screen.Cards[1].Target)) throw new InvalidOperationException("간격 뒤에 마지막 모습이 가지 않았다");
                now += PeekRate.GapMs;
                screen.Hover(-1);
                if (((SendPeek)live.Sent[live.Sent.Count - 1]).Index != -1) throw new InvalidOperationException("떠남(−1)이 가지 않았다");
                foreach (var m in live.Sent)
                    if (m is SendPeek p && (p.Index < -1 || p.Index >= choice.Options.Count)) throw new InvalidOperationException("후보 밖 자리: " + p.Index);
                now += PeekRate.GapMs;
                screen.Hover(1);
                s.Panel.Tick(3);
            }),
        };

        // 그 좌석을 조우 토큰이 있는 빈 칸에 세운다(첫 맵 행동에서 바로 찾아볼 수 있게)
        static void PeekSpotFor(UiShot s, GameState st, int seat)
        {
            for (int c = 0; c < s.Made.Ctx.Board.Count; c++)
            {
                if (MapActions.EncounterTokenAt(st, c) < 0 || Array.Exists(st.Seats, x => x.Cell == c)) continue;
                st.Seats[seat].Cell = c;
                return;
            }
            throw new InvalidOperationException("조우 토큰이 있는 빈 칸이 없다");
        }

        // 방 판 덮개(가짜 방 — 좌석 2 = 빛나)를 얹는다
        static RoomLive PeekRoom(UiShot s, GameFlow flow)
        {
            var live = FakeGame(flow, true);
            live.Room.Seats[2].Connected = true;
            live.Clock.Seats[2].Connected = true;
            flow.Room.ShowGame(live);
            flow.Stage.Bind(flow.Stage.Root, flow.Stage.Grade);
            return live;
        }

        static void PeekLaid(UiShot s, GameFlow flow)
        {
            Pass(flow, 0);
            flow.Hud?.Refresh();
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
        }

        // 좌석 2의 턴(보드 · 내 입력은 닫힘)
        static GameFlow PeekTurn(UiShot s, Action<GameState> prepare = null, int seats = 3)
        {
            var flow = Seats(s, seats, prepare);
            for (int k = 0; k < seats * 2 && flow.Client.View.ActiveSeat != 1; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.BoardOpen && f.Hud != null && f.Client.View.ActiveSeat == 1);
            return flow;
        }

        // 좌석 2가 찾아보기를 해 조우 고르기를 열었다(내 화면의 공개 연출이 다 돈 뒤)
        static GameFlow PeekEncounter(UiShot s)
        {
            var flow = PeekTurn(s, st => PeekSpotFor(s, st, 1));
            for (int i = 0; i < 400 && PeekRules.Open(flow.Client.View, 1, PeekSpot.Encounter) == null; i++)
            {
                Pass(flow, 100);
                if (flow.Fx != null && !flow.Fx.Sequencer.InputOpen) continue;
                if (flow.AnswerSeat(1, a => a.Kind == AnswerKind.Search)) continue;
                if (flow.AnswerSeat(1, a => a.Kind == AnswerKind.Move)) continue;
                flow.AnswerSeat(1, a => a.Kind == AnswerKind.Decline);
            }
            if (PeekRules.Open(flow.Client.View, 1, PeekSpot.Encounter) == null)
            {
                var c = flow.Client.View.Pending.Find(x => x.Seat == 1 && !x.Answered);
                throw new InvalidOperationException("좌석 2가 찾아보기까지 가지 못했다: 턴 " + flow.Client.View.ActiveSeat + " · 선택 " + (c != null ? c.Kind + "/" + c.Sub : "없음"));
            }
            DealSettle(s, flow, f => PeekRules.Open(f.Client.View, 1, PeekSpot.Encounter) != null && f.Fx.Sequencer.IsEmpty);
            if (flow.Stage.Current != null) throw new InvalidOperationException("남의 조우 고르기에 창이 열렸다: " + flow.Stage.Current.Name);
            return flow;
        }

        // 좌석 2가 사당에서 분홍 규토리 + 7점을 바쳐 카드 3장 고르기를 열었다
        static GameFlow PeekShrine(UiShot s)
        {
            var flow = PeekTurn(s, st => SoloTrail.PrepareShrine(s.Made.Ctx, st, 1, SoloTrail.ShrineTop));
            if (!flow.AnswerSeat(1, a => a.Kind == AnswerKind.Offer && a.Values.Count > 0)) throw new InvalidOperationException("좌석 2의 공물(+7점)이 거절됐다");
            DealSettle(s, flow, f => PeekRules.Open(f.Client.View, 1, PeekSpot.Shrine) != null && f.Fx.Sequencer.IsEmpty);
            if (flow.Stage.Current != null) throw new InvalidOperationException("남의 사당 카드 고르기에 창이 열렸다: " + flow.Stage.Current.Name);
            return flow;
        }

        // 내 턴의 조우 창(입력이 열린 때)
        static EncounterScreen PeekMine(UiShot s, GameFlow flow)
        {
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                EndActive(flow);
                Pass(flow, BoardReadyMs * 8);
            }
            DealSettle(s, flow, f => f.InputOpen && f.Hud != null && f.Client.Choice(ChoiceKind.MapAction) != null);
            for (int i = 0; i < 40 && flow.Client.Choice(ChoiceKind.EncounterPick) == null; i++)
            {
                var c = flow.Client.FirstChoice();
                if (c == null || flow.Fx != null && !flow.Fx.Sequencer.InputOpen) { Pass(flow, 250); continue; }
                var legal = flow.Client.Legal(c);
                var a = legal.Find(x => x.Kind == AnswerKind.Search) ?? legal.Find(x => x.Kind == AnswerKind.Move) ?? legal.Find(x => x.Kind == AnswerKind.Decline);
                if (a == null || !flow.Answer(c, a).Accepted) throw new InvalidOperationException("내 찾아보기까지 가지 못했다");
                Pass(flow, 250);
            }
            DealSettle(s, flow, f => f.Stage.Current is EncounterScreen e && e.Open);
            return (EncounterScreen)flow.Stage.Current;
        }

        // 결정 120 ①: 남의 조우 · 사당 카드 고르기는 보기 창이 내 창처럼 보이고 가리킴은 그 창의 카드 테두리다(위 가운데 줄은 뜨지 않는다) — 단정한 뒤 [닫기]로 접어 줄을 세운다
        static void PeekWatched(UiShot s, GameFlow flow, PeekSpot spot, int index)
        {
            var w = flow.Watch;
            bool kind = spot == PeekSpot.Encounter ? w is EncounterScreen : w is ShrineScreen;
            if (!flow.WatchShown || w == null || w.Watched != 1 || !kind) throw new InvalidOperationException("남의 고르기에 보기 창이 뜨지 않았다(" + (w != null ? w.Name + " · 좌석 " + w.Watched : "없음") + ")");
            int pointed = w is EncounterScreen e ? e.Pointed : ((ShrineScreen)w).Pointed;
            if (pointed != index) throw new InvalidOperationException("보기 창의 가리킴이 다르다: " + pointed + " ≠ " + index);
            if (flow.RoomLayer.PeekSeat != -1) throw new InvalidOperationException("보기 창이 떠 있는데 위 가운데 줄도 떴다");
            if (!flow.CloseWatch()) throw new InvalidOperationException("보기 창이 접히지 않았다");
            PeekLaid(s, flow);
        }

        // 가짜 방이 좌석 2의 소식을 받은 뒤의 줄(보기 창을 접은 뒤): 후보 칸 수 · 가리킨 칸의 좌석 색 테두리 · 누른 칸의 좌석 색 바탕 · 줄이 화면 · 기둥 안 · 글 넘침
        static void PeekShown(UiShot s, GameFlow flow, PeekSpot spot, int index, bool press)
        {
            var live = PeekRoom(s, flow);
            if (index >= 0) live.TakePeek(new PeekMsg { From = 1, Spot = spot, Index = index, Press = press });
            live.TakePeek(new PeekMsg { From = live.Seat, Spot = spot, Index = 0, Press = true });   // 내 것이 돌아와도 그리지 않는다
            PeekLaid(s, flow);
            if (spot != PeekSpot.Bank) PeekWatched(s, flow, spot, index);
            var layer = flow.RoomLayer;
            var view = flow.Client.View;
            var choice = PeekRules.Open(view, 1, spot);
            int slots = spot == PeekSpot.Bank ? 2 : choice.Options.Count;
            if (layer.PeekSeat != 1 || layer.PeekSpot != spot || layer.PeekSlots != slots || layer.PeekIndex != index || layer.PeekPress != (press && index >= 0))
                throw new InvalidOperationException("남의 고르기 줄: 좌석 " + layer.PeekSeat + " · " + layer.PeekSpot + " · 칸 " + layer.PeekSlots + "/" + slots + " · 가리킴 " + layer.PeekIndex + " · 누름 " + layer.PeekPress);
            var strip = flow.Stage.Root.Q("room-peek") ?? throw new InvalidOperationException("room-peek 없음");
            var label = strip.Q<Label>("room-peek-text");
            if (label == null || !label.text.Replace("⁠", "").Contains(flow.SeatName(1))) throw new InvalidOperationException("줄 머리에 그 좌석 이름이 없다: " + label?.text);
            Color32 tone = flow.SeatColor(1);
            for (int n = 1; n <= slots; n++)
            {
                var tile = strip.Q("room-peek-opt-" + n) ?? throw new InvalidOperationException("후보 칸 " + n + " 없음");
                bool want = index == n - 1 || (spot == PeekSpot.Bank && index == BankRules.Both);
                if (layer.PeekMarked(n) != want || layer.PeekMarked(n, true) != (want && press)) throw new InvalidOperationException("칸 " + n + " 표시가 다르다(가리킴 " + layer.PeekMarked(n) + " · 누름 " + layer.PeekMarked(n, true) + ")");
                if (want)
                {
                    var edge = (Color32)tile.resolvedStyle.borderTopColor;
                    if (edge.r != tone.r || edge.g != tone.g || edge.b != tone.b) throw new InvalidOperationException("칸 " + n + " 테두리가 좌석 색이 아니다: " + edge + " / " + tone);
                    var fill = (Color32)tile.resolvedStyle.backgroundColor;
                    if (press != (fill.r == tone.r && fill.g == tone.g && fill.b == tone.b)) throw new InvalidOperationException("칸 " + n + " 바탕(누름 " + press + "): " + fill);
                }
                if (tile.worldBound.width < 8f || tile.worldBound.xMin < strip.worldBound.xMin - 0.5f || tile.worldBound.xMax > strip.worldBound.xMax + 0.5f) throw new InvalidOperationException("칸 " + n + "이 줄 밖: " + tile.worldBound + " / " + strip.worldBound);
            }
            RoomFit(s, flow, "room-layer-col");
            RoomFit(s, flow, "room-peek");
            if (flow.Stage.Current != null) throw new InvalidOperationException("창이 열렸다: " + flow.Stage.Current.Name);
            // 위 띠 · 기록 판 · 아래 판과 겹치지 않는다
            Apart(flow.Stage.Root, "room-peek", "board-hud-seats");
            Apart(flow.Stage.Root, "room-peek", "board-hud-status");
            Apart(flow.Stage.Root, "room-peek", "board-hud-log");
            Apart(flow.Stage.Root, "room-peek", "board-hud-dock");
        }
    }
}
