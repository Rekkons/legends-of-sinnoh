using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M7 「세션 ①」 · 02 §10 「시계」(가짜 시각): TurnClock 단위·흐름·뱅크(02 §5-3~§5-5)와 끊김·자리 비움의 기다림(§6-1). 수치는 §5-4 「느긋하게」.
    /// </summary>
    public class TurnClockTests
    {
        static ClockChoice C(int id, int seat, ChoiceKind kind, int turn = 1, bool own = true, bool offline = false, bool away = false, long notBefore = long.MinValue) =>
            new ClockChoice { Id = id, Seat = seat, Kind = kind, Clock = ChoiceRules.ClockOf(kind), TurnId = turn, Own = own, Offline = offline, Away = away, NotBefore = notBefore };

        static TurnClock Relaxed(int seats = 3) => new TurnClock(ClockSettings.For(ClockPreset.Relaxed), seats, 0);

        static long Next(TurnClock t) => t.NextExpiry(out _);

        [Test]
        public void M7_시계_MapTurn은턴한묶음을나눠쓰고뱅크로넘어간다()
        {
            var t = Relaxed();
            t.Sync(0, 1, new[] { C(1, 0, ChoiceKind.MapAction) });
            Assert.AreEqual(180000 + 120000, Next(t), "턴 180초 + 뱅크 시작 120초");
            t.AdvanceTo(100000);
            t.Sync(100000, 1, new[] { C(2, 0, ChoiceKind.HiddenSpot) }); // 같은 턴의 다음 MapTurn 선택이 남은 시간을 잇는다
            Assert.AreEqual(100000 + 80000 + 120000, Next(t));
            t.AdvanceTo(250000);
            Assert.AreEqual(50000, t.BankMs(0), "기본 시간을 다 쓰고 뱅크에서 70초");
            t.Sync(250000, 1, new[] { C(3, 0, ChoiceKind.MapAction) });
            Assert.AreEqual(300000, Next(t), "남은 것은 뱅크뿐");
            t.Sync(250000, 2, new[] { C(4, 0, ChoiceKind.MapAction, turn: 2) });
            Assert.AreEqual(250000 + 180000 + 50000, Next(t), "새 턴은 처음부터(02 §5-3 추가 턴도 새 TurnId)");

            t.OnRound(1);
            Assert.AreEqual(50000, t.BankMs(0), "1라운드는 시작값 그대로");
            t.OnRound(2);
            Assert.AreEqual(80000, t.BankMs(0), "라운드마다 + 30초");
            t.OnRound(2);
            Assert.AreEqual(80000, t.BankMs(0), "같은 라운드는 한 번");
            t.OnRound(20);
            Assert.AreEqual(300000, t.BankMs(0), "상한 300초");
            Assert.AreEqual(300000, t.BankMs(1));
        }

        [Test]
        public void M7_시계_남의응답과배틀중에는맵시계가멈추고좌석에는하나만흐른다()
        {
            var t = Relaxed();
            t.Sync(0, 1, new[] { C(1, 0, ChoiceKind.MapAction), C(2, 1, ChoiceKind.Respond) });
            Assert.AreEqual(60000, t.NextExpiry(out int id), "도전 응답 60초만 흐른다");
            Assert.AreEqual(2, id);
            t.AdvanceTo(30000);
            var seats = t.Seats();
            Assert.AreEqual(ClockKind.None, seats[0].Running, "차례 좌석의 MapTurn은 남의 응답 동안 멈춘다");
            Assert.AreEqual(ClockKind.Respond, seats[1].Running);
            Assert.AreEqual(30000, seats[1].LeftMs);
            t.Sync(30000, 1, new[] { C(1, 0, ChoiceKind.MapAction) });
            Assert.AreEqual(30000 + 180000 + 120000, Next(t), "응답이 끝나면 MapTurn이 그대로 다시 흐른다");

            // 배틀: 자기 포켓몬 결정은 뱅크를 쓰고, 다른 좌석의 MapTurn은 멈춘다
            t.Sync(30000, 1, new[] { C(5, 0, ChoiceKind.BattleAction), C(6, 2, ChoiceKind.MapAction) });
            Assert.AreEqual(30000 + 60000 + 120000, t.NextExpiry(out id));
            Assert.AreEqual(5, id);
            Assert.AreEqual(ClockKind.None, t.Seats()[2].Running);
            // 조종자의 명단(Lineup)은 2배 · 뱅크 없음
            t.Sync(30000, 1, new[] { C(7, 2, ChoiceKind.Lineup, own: false) });
            Assert.AreEqual(30000 + 120000, Next(t));
            // 조종자의 배틀 결정도 뱅크 없음
            t.Sync(30000, 1, new[] { C(8, 2, ChoiceKind.BattleAction, own: false) });
            Assert.AreEqual(30000 + 60000, Next(t));
            // 한 좌석에 반응과 배틀 결정이 같이 열리면 반응(30초)만 센다
            t.Sync(30000, 1, new[] { C(9, 0, ChoiceKind.BattleAction), C(10, 0, ChoiceKind.Reaction) });
            Assert.AreEqual(30000 + 30000, t.NextExpiry(out id));
            Assert.AreEqual(10, id);
        }

        [Test]
        public void M7_시계_다시열린선택은남은시간을잇고셋업묶음은좌석마다한번()
        {
            var t = Relaxed();
            t.Sync(0, 1, new[] { C(7, 0, ChoiceKind.BattleAction) });
            t.AdvanceTo(20000);
            t.Rename(7, 8); // 정비로 다시 열림(ChoiceReissued)
            t.Sync(20000, 1, new[] { C(8, 0, ChoiceKind.BattleAction) });
            Assert.AreEqual(20000 + 40000 + 120000, Next(t), "정비로 시간을 늘리지 못한다(02 §5-3)");
            t.Sync(20000, 1, new[] { C(9, 0, ChoiceKind.BattleAction) });
            Assert.AreEqual(20000 + 60000 + 120000, Next(t), "새 결정은 새로");

            var s = Relaxed();
            s.Sync(0, 0, new[] { C(10, 1, ChoiceKind.StarterPick), C(11, 1, ChoiceKind.TrainerCardPick), C(12, 2, ChoiceKind.VillagePick) });
            Assert.AreEqual(90000, s.NextExpiry(out int id), "마을 90초 · 셋업은 뱅크 없음");
            Assert.AreEqual(12, id);
            s.AdvanceTo(100000);
            s.Sync(100000, 0, new[] { C(11, 1, ChoiceKind.TrainerCardPick) });
            Assert.AreEqual(100000 + 80000, Next(s), "스타터 + 카드는 좌석마다 묶음 180초 한 번");
        }

        [Test]
        public void M7_시계_연출멈춤전에는줄지않고끊긴좌석은뱅크없이_자리비움은0초()
        {
            var t = Relaxed();
            t.Sync(5000, 1, new[] { C(1, 0, ChoiceKind.MapAction) });
            Assert.AreEqual(5000 + 180000 + 120000, Next(t), "멈춤 끝에서 시작");
            t.AdvanceTo(4000);
            Assert.AreEqual(180000, t.Seats()[0].LeftMs, "멈춤 동안은 줄지 않는다");
            t.AdvanceTo(15000);
            t.Sync(15000, 1, new[] { C(1, 0, ChoiceKind.MapAction, offline: true) });
            Assert.AreEqual(15000 + 170000, Next(t), "끊긴 좌석은 남은 기본 시간만(뱅크 안 씀)");
            t.Sync(15000, 1, new[] { C(1, 0, ChoiceKind.MapAction, away: true) });
            Assert.AreEqual(15000, Next(t), "자리 비움은 0초");
            t.Sync(15000, 1, new[] { C(2, 0, ChoiceKind.Reaction, away: true, notBefore: 15800) });
            Assert.AreEqual(15800, Next(t), "숨은 손패 창은 최소 시간 전에 넘기지 않는다");

            var off = new TurnClock(ClockSettings.For(ClockPreset.Off), 2, 0);
            off.Sync(0, 1, new[] { C(1, 0, ChoiceKind.MapAction) });
            Assert.AreEqual(long.MaxValue, Next(off), "끔 방은 시간 초과가 없다");
            Assert.AreEqual(0, off.BankMs(0));
            off.Sync(0, 1, new[] { C(1, 0, ChoiceKind.MapAction, offline: true) });
            Assert.AreEqual(120000, Next(off), "끔 방의 끊긴 좌석은 120초");

            var normal = ClockSettings.For(ClockPreset.Normal);
            Assert.AreEqual(new[] { 90000, 30000, 60000, 15000, 30000, 45000, 90000, 60000, 15000, 150000 },
                new[] { normal.MapTurnMs, normal.BattleActionMs, normal.LineupMs, normal.ReactionMs, normal.RespondMs, normal.VillageMs, normal.SetupPickMs, normal.BankStartMs, normal.BankRoundMs, normal.BankMaxMs }, "02 §5-4 「보통」");
        }
    }
}
