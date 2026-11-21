using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 다듬기 P09(결정 112 Q16, 04 P15, 02 §4-10): 남의 고르기 보기 — 방 층 메시지 <see cref="SendPeek"/> · <see cref="PeekMsg"/>.
    /// 공개 선택(조우 카드 · 사당 카드 · 은행상회 줄)에서만 넘기고, 비공개 선택에서는 전선에 아무것도 나가지 않는다(보기 누출 금지).
    /// </summary>
    public partial class RoomTests
    {
        // 찾아볼 수 있으면 찾아보고(조우 고르기를 연다), 그 밖은 기본값
        ChoiceAnswer SearchFirst(GameState view, PendingChoice c)
        {
            if (c.Kind == ChoiceKind.HiddenSpot) return ChoiceAnswer.Of(AnswerKind.Decline);
            if (c.Kind != ChoiceKind.MapAction) return null;
            var legal = new List<ChoiceAnswer>();
            Engine.Legal(view, ctx, c, legal);
            return legal.Find(a => a.Kind == AnswerKind.Search) ?? legal.Find(a => a.Kind == AnswerKind.Move);
        }

        static int PickerOf(Room r, ChoiceKind kind)
        {
            for (int s = 0; s < r.Host.SeatCount; s++)
                if (r.Mine(s)?.Kind == kind) return s;
            return -1;
        }

        static int Peeks(Room r, int seat) => r.Net.All<PeekMsg>(r.Conn[seat]).Count;

        [Test]
        public void M9_남의고르기_공개선택만_보낸좌석을뺀전원에게_범위밖과남의선택은버리고_초당넷까지()
        {
            var r = new Room(ctx, Cfg(new RoomSettings { Seats = 3, Clock = ClockPreset.Off }), 3);
            r.Until(() => PickerOf(r, ChoiceKind.EncounterPick) >= 0, SearchFirst);
            int who = PickerOf(r, ChoiceKind.EncounterPick);
            var choice = r.Mine(who);
            int other = (who + 1) % 3, third = (who + 2) % 3;
            Assert.Greater(choice.Options.Count, 0, "조우 카드(공개 후보)");
            CollectionAssert.AreEqual(choice.Options, r.View(other).Pending.Find(c => c.Id == choice.Id).Options, "남의 보기에도 같은 후보(공개 선택)");
            r.Now += 5000;

            // 고르는 좌석이 가리킨다 → 다른 두 좌석만 받는다(보낸 좌석에게는 되돌리지 않는다)
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Encounter, Index = 0 }, r.Now);
            Assert.AreEqual((0, 1, 1), (Peeks(r, who), Peeks(r, other), Peeks(r, third)));
            var got = r.Net.Last<PeekMsg>(r.Conn[other]);
            Assert.AreEqual((who, PeekSpot.Encounter, 0, false), (got.From, got.Spot, got.Index, got.Press));
            // 전선 위 바이트: 허용된 칸 넷뿐(카드 번호 · 이름 같은 내용은 싣지 않는다) · 90바이트 밑
            string wire = MessageCodec.Encode(got);
            Assert.AreEqual("{\"V\":" + Protocol.Version + ",\"Seq\":0,\"Type\":\"PeekMsg\",\"Body\":{\"From\":" + who + ",\"Spot\":1,\"Index\":0,\"Press\":false}}", wire);
            Assert.Less(System.Text.Encoding.UTF8.GetByteCount(wire), 90);

            // 범위 밖 · 다른 자리(열려 있지 않은 사당 · 모르는 자리) · 고르지 않는 좌석이 보낸 것은 버린다
            int before = Peeks(r, other);
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Encounter, Index = choice.Options.Count }, r.Now);
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Encounter, Index = -2 }, r.Now);
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Shrine, Index = 0 }, r.Now);
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.None, Index = 0 }, r.Now);
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = (PeekSpot)99, Index = 0 }, r.Now);
            r.Host.OnMessage(r.Conn[other], new SendPeek { Spot = PeekSpot.Encounter, Index = 0 }, r.Now);
            Assert.AreEqual(before, Peeks(r, other));
            Assert.AreEqual(0, Peeks(r, who), "남이 보낸 틀린 것도 고르는 좌석에게 가지 않는다");

            // 누름 · 떠남(−1 — 누름 표시는 지운다)
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Encounter, Index = choice.Options.Count - 1, Press = true }, r.Now);
            got = r.Net.Last<PeekMsg>(r.Conn[third]);
            Assert.AreEqual((choice.Options.Count - 1, true), (got.Index, got.Press));
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Encounter, Index = -1, Press = true }, r.Now);
            got = r.Net.Last<PeekMsg>(r.Conn[third]);
            Assert.AreEqual((-1, false), (got.Index, got.Press));

            // 빈도: 좌석마다 1초에 넷까지(여기까지 셋) — 넘으면 조용히 버리고 1초가 지나면 다시 받는다
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Encounter, Index = 0 }, r.Now);
            int full = Peeks(r, other);
            for (int i = 0; i < 6; i++) r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Encounter, Index = i % 2 }, r.Now + 10 * i);
            Assert.AreEqual(full, Peeks(r, other), "한도를 넘은 것");
            r.Now += PeekRate.WindowMs;
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Encounter, Index = 0 }, r.Now);
            Assert.AreEqual(full + 1, Peeks(r, other));

            // 게임 상태 · 명령 수는 그대로(방 층 메시지) — 고른 뒤에는(선택이 닫힘) 넘기지 않는다
            int commands = r.Host.Game.CommandCount;
            ulong hash = r.Host.Game.Hash;
            r.Now += PeekRate.WindowMs;
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Encounter, Index = 0 }, r.Now);
            Assert.AreEqual((commands, hash), (r.Host.Game.CommandCount, r.Host.Game.Hash));
            r.Answer(who, choice, choice.Default.Clone());
            before = Peeks(r, other);
            r.Now += PeekRate.WindowMs;
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Encounter, Index = 0 }, r.Now);
            Assert.AreEqual(before, Peeks(r, other), "닫힌 선택");
        }

        [Test]
        public void M9_남의고르기_은행상회는내턴의맵행동동안만_줄셋()
        {
            var r = new Room(ctx, Cfg(new RoomSettings { Seats = 2, Clock = ClockPreset.Off }), 2);
            r.Until(() => PickerOf(r, ChoiceKind.MapAction) >= 0);
            int who = PickerOf(r, ChoiceKind.MapAction), other = 1 - who;
            r.Now += 5000;
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Bank, Index = BankRules.Both, Press = true }, r.Now);
            var got = r.Net.Last<PeekMsg>(r.Conn[other]);
            Assert.IsNotNull(got);
            Assert.AreEqual((who, PeekSpot.Bank, BankRules.Both, true), (got.From, got.Spot, got.Index, got.Press));
            int before = Peeks(r, other);
            r.Host.OnMessage(r.Conn[who], new SendPeek { Spot = PeekSpot.Bank, Index = PeekRules.BankRows }, r.Now);
            r.Host.OnMessage(r.Conn[other], new SendPeek { Spot = PeekSpot.Bank, Index = 0 }, r.Now);   // 남의 턴에 창을 보기만 하는 좌석
            Assert.AreEqual(before, Peeks(r, other));
            Assert.AreEqual(0, Peeks(r, who));
        }

        [Test]
        public void M9_남의고르기_비공개선택에서는전선에아무것도나가지않는다()
        {
            // 셋업: 고향 마을 · 스타팅 · 트레이너 카드 고르기 — 어느 자리 · 어느 번호로 보내도 넘기지 않는다
            var r = new Room(ctx, Cfg(new RoomSettings { Seats = 3, Clock = ClockPreset.Off }), 3);
            var kinds = new HashSet<ChoiceKind>();
            for (int step = 0; step < 400 && r.Host.Game.ViewFor(0).Phase == Phase.Setup; step++)
            {
                for (int s = 0; s < 3; s++)
                {
                    var mine = r.Mine(s);
                    if (mine != null) kinds.Add(mine.Kind);
                    r.Now += PeekRate.WindowMs;
                    foreach (PeekSpot spot in System.Enum.GetValues(typeof(PeekSpot)))
                        for (int index = -1; index < 4; index++)
                            if (spot != PeekSpot.Village)   // 고향 마을 고르기는 공개(결정 120 — 아래 따로)
                                r.Host.OnMessage(r.Conn[s], new SendPeek { Spot = spot, Index = index, Press = index == 1 }, r.Now);
                }
                if (!r.Step())
                {
                    long t = r.Host.NextEventMs;
                    if (t == long.MaxValue) break;
                    r.Now = System.Math.Max(r.Now, t);
                    r.Host.Tick(r.Now);
                }
            }
            CollectionAssert.IsSubsetOf(new[] { ChoiceKind.StarterPick, ChoiceKind.TrainerCardPick }, kinds, "셋업의 비공개 고르기를 지났다");
            for (int s = 0; s < 3; s++)
            {
                Assert.AreEqual(0, Peeks(r, s), "좌석 " + s);
                foreach (var m in r.Net.Got[r.Conn[s]])
                    if (!(m is GameUpdate)) StringAssert.DoesNotContain("Peek", MessageCodec.Encode(m), "전선 바이트에 남의 고르기가 없다");
            }

            // 넘길 메시지를 만드는 한 곳(PeekRules.Relay): 남에게 가리는 선택(비밀 묶음 · 버리기 · 트레이너 카드 · 거래 응답 · 조우 덱 찾기)과 허용 목록 밖 선택(스타팅 · 운명 반응 · 손패)은 어느 자리로도 null
            var hidden = new[]
            {
                new PendingChoice { Seat = 1, Kind = ChoiceKind.EncounterPick, Secret = true, Options = { 3, 4, 5 } },
                new PendingChoice { Seat = 1, Kind = ChoiceKind.Discard, Options = { 3, 4, 5 } },
                new PendingChoice { Seat = 1, Kind = ChoiceKind.TrainerCardPick, Options = { 3, 4, 5 } },
                new PendingChoice { Seat = 1, Kind = ChoiceKind.StarterPick, Options = { 3, 4, 5 } },
                new PendingChoice { Seat = 1, Kind = ChoiceKind.Respond, Sub = ChoiceSub.Trade, Options = { 3, 4, 5 } },
                new PendingChoice { Seat = 1, Kind = ChoiceKind.Reaction, Sub = ChoiceSub.Fate, HandGated = true, Options = { 3, 4, 5 } },
                new PendingChoice { Seat = 1, Kind = ChoiceKind.Reward, Sub = ChoiceSub.ShrineExchangeCard, Options = { 3, 4, 5 } },
                new PendingChoice { Seat = 1, Kind = ChoiceKind.Simultaneous, Sub = ChoiceSub.Follower, Secret = true, Options = { 3, 4, 5 } },
                new PendingChoice { Seat = 1, Kind = ChoiceKind.Reward, Sub = ChoiceSub.ShrinePick, Answered = true, Options = { 3, 4, 5 } },
            };
            foreach (var c in hidden)
            {
                var s = r.Host.Game.HostCopy();
                s.Pending.Clear();
                s.Pending.Add(c);
                foreach (PeekSpot spot in System.Enum.GetValues(typeof(PeekSpot)))
                    for (int index = -1; index < 4; index++)
                        Assert.IsNull(PeekRules.Relay(new SendPeek { Spot = spot, Index = index }, 1, s), c.Kind + "/" + c.Sub + " → " + spot + " " + index);
            }
            // 대조: 같은 꼴의 공개 선택은 넘긴다(위 단정이 헛돌지 않게)
            var open = r.Host.Game.HostCopy();
            open.Pending.Clear();
            open.Pending.Add(new PendingChoice { Seat = 1, Kind = ChoiceKind.Reward, Sub = ChoiceSub.ShrinePick, Options = { 3, 4, 5 } });
            Assert.IsNotNull(PeekRules.Relay(new SendPeek { Spot = PeekSpot.Shrine, Index = 2 }, 1, open));
            Assert.IsNull(PeekRules.Relay(new SendPeek { Spot = PeekSpot.Shrine, Index = 2 }, 0, open), "다른 좌석");

            // 결정 120(Protocol 10): 고향 마을 고르기 · 배틀 기술 고르기도 넘긴다 — Index는 공개 표의 색인(마을 고정물 · 기술), 그 좌석의 그 선택이 열려 있을 때만
            var village = r.Host.Game.HostCopy();
            village.Pending.Clear();
            village.Pending.Add(new PendingChoice { Seat = 1, Kind = ChoiceKind.VillagePick });
            var relayed = PeekRules.Relay(new SendPeek { Spot = PeekSpot.Village, Index = 7, Press = true }, 1, village);
            Assert.AreEqual((1, PeekSpot.Village, 7, true), (relayed.From, relayed.Spot, relayed.Index, relayed.Press));
            Assert.IsNull(PeekRules.Relay(new SendPeek { Spot = PeekSpot.Village, Index = PeekRules.KeySlots }, 1, village), "범위 밖");
            Assert.IsNull(PeekRules.Relay(new SendPeek { Spot = PeekSpot.Village, Index = 7 }, 0, village), "다른 좌석");
            Assert.IsNull(PeekRules.Relay(new SendPeek { Spot = PeekSpot.Move, Index = 7 }, 1, village), "배틀 기술 고르기가 열려 있지 않다");
            var battle = r.Host.Game.HostCopy();
            battle.Pending.Clear();
            battle.Pending.Add(new PendingChoice { Seat = 1, Kind = ChoiceKind.BattleAction });
            Assert.IsNotNull(PeekRules.Relay(new SendPeek { Spot = PeekSpot.Move, Index = 120 }, 1, battle));
            Assert.IsNull(PeekRules.Relay(new SendPeek { Spot = PeekSpot.Village, Index = 7 }, 1, battle), "고향 마을 고르기가 열려 있지 않다");
            battle.Pending[0].Secret = true;
            Assert.IsNull(PeekRules.Relay(new SendPeek { Spot = PeekSpot.Move, Index = 120 }, 1, battle), "비밀 선택");

            // 로비에서는 버린다
            var lobby = new Room(ctx, net => new HostSession(ctx, net, Cfg(), 0));
            Assert.IsTrue(lobby.Host.Approve(0, Hi("owner"), 0).Ok);
            Assert.IsTrue(lobby.Host.Approve(1, Hi("g1"), 0).Ok);
            lobby.Host.OnMessage(0, new SendPeek { Spot = PeekSpot.Bank, Index = 0 }, 0);
            Assert.AreEqual(0, lobby.Net.All<PeekMsg>(1).Count);
        }
    }
}
