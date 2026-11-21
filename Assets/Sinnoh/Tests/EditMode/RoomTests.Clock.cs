using System;
using NUnit.Framework;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 결정 140 ①: 방 설정의 직접 설정(<see cref="ClockPreset.Custom"/> · <see cref="RoomSettings.Custom"/>) —
    /// 스테퍼 한 번 · 범위 · 프리셋 되돌림 · 검사 · 기억 글, 실제 <see cref="TurnClock"/> 길이(로비 · 게임 중 바꾸기), 전선 · 저장 왕복.
    /// </summary>
    public partial class RoomTests
    {
        static ClockSettings Lengths(RoomSettings s) => s.ClockLengths();

        [Test]
        public void M10_시간세부_스테퍼는범위안에서한칸씩_프리셋과같아지면프리셋으로_틀린값은막는다_결정140()
        {
            foreach (var p in new[] { ClockPreset.Relaxed, ClockPreset.Normal })
                foreach (var f in ClockSettings.Fields)
                {
                    Assert.IsTrue(f.InRange(ClockSettings.For(p)), $"{p} {f.Key}는 범위 안");
                    Assert.AreEqual(0, (f.Get(ClockSettings.For(p)) - f.MinMs) % f.StepMs, $"{p} {f.Key}는 스테퍼 칸 위(두 프리셋 사이를 오갈 수 있다)");
                }
            Assert.AreEqual(10, ClockSettings.Fields.Length, "턴 · 배틀 기술 · 출전 순서 · 반응 · 응답 · 고향 마을 · 스타터 · 예비 시작 · 충전 · 상한");

            var s = new RoomSettings();
            Assert.AreEqual(ClockPreset.Relaxed, s.Clock, "새 방 기본 「느긋하게」");
            Assert.IsTrue(s.StepClock("map_turn", +1));
            Assert.AreEqual((ClockPreset.Custom, 185000), (s.Clock, s.Custom.MapTurnMs), "턴 +5초 → 직접 설정");
            Assert.AreEqual(185000, Lengths(s).MapTurnMs);
            Assert.AreEqual(ClockPreset.Custom, Lengths(s).Preset);
            Assert.IsTrue(Lengths(s).On);
            Assert.IsNull(s.Problem(1));
            Assert.IsTrue(s.StepClock("map_turn", -1));
            Assert.AreEqual(ClockPreset.Relaxed, s.Clock, "프리셋과 같아지면 그 프리셋으로");
            Assert.IsNull(s.Custom);

            // 「보통」 값을 칸마다 맞추면 「보통」
            foreach (var f in ClockSettings.Fields)
                for (int guard = 0; f.Get(Lengths(s)) != f.Get(ClockSettings.For(ClockPreset.Normal)); guard++)
                {
                    Assert.Less(guard, 200, f.Key + "가 「보통」 값에 닿지 않는다(한 번 양이 맞지 않음)");
                    Assert.IsTrue(s.StepClock(f.Key, f.Get(Lengths(s)) > f.Get(ClockSettings.For(ClockPreset.Normal)) ? -1 : +1), f.Key);
                }
            Assert.AreEqual(ClockPreset.Normal, s.Clock);

            // 범위 끝: 턴은 15초에서 멈춘다 · 응답은 2분에서
            for (int i = 0; i < 100; i++) s.StepClock("map_turn", -1);
            Assert.AreEqual(15000, Lengths(s).MapTurnMs);
            Assert.IsFalse(s.StepClock("map_turn", -1), "끝에 닿으면 바꾸지 않는다");
            for (int i = 0; i < 100; i++) s.StepClock("respond", +1);
            Assert.AreEqual(120000, Lengths(s).RespondMs);
            Assert.IsFalse(s.StepClock("respond", +1));
            Assert.IsFalse(s.StepClock("nope", +1), "모르는 칸");

            // 예비 시간: 시작을 상한 위로 올리면 상한도 · 상한을 시작 밑으로 내리면 시작도
            var b = new RoomSettings { Clock = ClockPreset.Normal };   // 시작 60초 · 상한 150초
            for (int i = 0; i < 4; i++) b.StepClock("bank_start", +1);
            Assert.AreEqual((180000, 180000), (b.Custom.BankStartMs, b.Custom.BankMaxMs));
            for (int i = 0; i < 2; i++) b.StepClock("bank_max", -1);
            Assert.AreEqual((120000, 120000), (b.Custom.BankStartMs, b.Custom.BankMaxMs));

            // 시계 끔에는 칸이 없다
            var off = new RoomSettings { Clock = ClockPreset.Off };
            Assert.IsFalse(off.StepClock("map_turn", +1));
            Assert.AreEqual(ClockPreset.Off, off.Clock);
            Assert.IsFalse(Lengths(off).On);

            // 검사: 직접 설정인데 칸이 없음 · 범위 밖 · 시작 > 상한 · 모르는 프리셋
            Assert.AreEqual("clock_custom", new RoomSettings { Clock = ClockPreset.Custom }.Problem(1));
            var bad = new RoomSettings { Clock = ClockPreset.Custom, Custom = ClockSettings.For(ClockPreset.Relaxed) };
            bad.Custom.MapTurnMs = 1000;
            Assert.AreEqual("clock_map_turn", bad.Problem(1));
            bad.Custom.MapTurnMs = 900000;
            Assert.AreEqual("clock_map_turn", bad.Problem(1));
            bad.Custom.MapTurnMs = 60000;
            bad.Custom.BankStartMs = 300000;
            bad.Custom.BankMaxMs = 120000;
            Assert.AreEqual("clock_bank", bad.Problem(1));
            Assert.AreEqual("enum", new RoomSettings { Clock = (ClockPreset)9 }.Problem(1));
            Assert.IsNull(new RoomSettings { Clock = ClockPreset.Normal, Custom = bad.Custom }.Problem(1), "프리셋이면 남은 직접 설정은 보지 않는다");
            Assert.AreEqual(ClockSettings.For(ClockPreset.Normal).MapTurnMs, new RoomSettings { Clock = ClockPreset.Normal, Custom = bad.Custom }.ClockLengths().MapTurnMs, "길이는 프리셋");

            // 사본은 깊게 · 게임 중 바꾸기는 직접 설정을 옮긴다
            var a = new RoomSettings();
            a.StepClock("reaction", +1);
            var copy = a.Clone();
            copy.Custom.ReactionMs = 99000;
            Assert.AreEqual(35000, a.Custom.ReactionMs, "Clone이 직접 설정을 나눠 쓰지 않는다");
            var live = new RoomSettings { Seats = 4 }.InGameChange(copy);
            Assert.AreEqual((ClockPreset.Custom, 99000, 4), (live.Clock, live.Custom.ReactionMs, live.Seats));
            copy.Custom.ReactionMs = 1;
            Assert.AreEqual(99000, live.Custom.ReactionMs);
        }

        [Test]
        public void M10_시간세부_기억글은되살리고_읽을수없거나범위밖이면기본값_결정140()
        {
            var s = new RoomSettings();
            s.StepClock("village", -1);
            s.StepClock("bank_round", +1);
            string memo = s.ClockMemo();
            StringAssert.StartsWith("3;", memo);
            var back = new RoomSettings();
            Assert.IsTrue(back.UseClockMemo(memo));
            Assert.AreEqual(ClockPreset.Custom, back.Clock);
            Assert.IsTrue(Lengths(back).SameLengths(Lengths(s)));

            Assert.AreEqual("2", new RoomSettings { Clock = ClockPreset.Normal }.ClockMemo());
            var normal = new RoomSettings();
            Assert.IsTrue(normal.UseClockMemo("2"));
            Assert.AreEqual(ClockPreset.Normal, normal.Clock);
            var offed = new RoomSettings();
            Assert.IsTrue(offed.UseClockMemo("0"));
            Assert.AreEqual(ClockPreset.Off, offed.Clock);

            // 직접 설정 글인데 값이 프리셋과 같으면 그 프리셋으로
            var same = new RoomSettings();
            Assert.IsTrue(same.UseClockMemo("3;" + string.Join(",", Array.ConvertAll(ClockSettings.Fields, f => f.Get(ClockSettings.For(ClockPreset.Normal)).ToString()))));
            Assert.AreEqual(ClockPreset.Normal, same.Clock);

            string outOfRange = "3;" + string.Join(",", Array.ConvertAll(ClockSettings.Fields, f => (f.Key == "map_turn" ? 999000 : f.Get(ClockSettings.For(ClockPreset.Relaxed))).ToString()));
            foreach (var junk in new[] { null, "", "x", "9", "1;2", "3", "3;1,2,3", "3;a,b,c,d,e,f,g,h,i,j", outOfRange })
            {
                var d = new RoomSettings();
                Assert.IsFalse(d.UseClockMemo(junk), junk ?? "null");
                Assert.AreEqual((ClockPreset.Relaxed, (ClockSettings)null), (d.Clock, d.Custom), "그대로 기본값: " + junk);
            }
        }

        [Test]
        public void M10_시간세부_직접설정이실제시계길이_게임중바꾸면다음선택부터_결정140()
        {
            var custom = ClockSettings.For(ClockPreset.Relaxed);
            custom.VillageMs = 40000;
            var settings = new RoomSettings();
            settings.UseClock(custom);
            Assert.AreEqual(ClockPreset.Custom, settings.Clock);
            var r = new Room(ctx, Cfg(settings), 2);
            int x = -1;
            r.Until(() => (x = Seat(r)) >= 0);
            var c = r.Mine(x);
            Assert.AreEqual(ChoiceKind.VillagePick, c.Kind);
            r.Now = r.Host.AnimEndMs;
            r.Host.Tick(r.Now);
            Assert.AreEqual(ClockPreset.Custom, r.Host.Clock.Preset, "손님 시계 표시의 프리셋 = 직접 설정");
            Assert.AreEqual(r.Now + 40000, r.Host.NextEventMs, "고향 마을 = 직접 정한 40초");
            Assert.AreEqual(40000, r.Net.Last<RoomUpdate>(1).Room.Settings.ClockLengths().VillageMs, "손님이 받은 방 설정에 직접 정한 길이가 있다(막대 길이 — ClockView.LengthMs)");

            // 게임 중: 손님은 못 바꾸고 · 틀린 값은 막고 · 방장 값은 다음 선택부터
            var wanted = r.Host.Settings;
            wanted.StepClock("village", -1);
            wanted.StepClock("village", -1);
            wanted.StepClock("village", -1);
            Assert.AreEqual(25000, wanted.Custom.VillageMs);
            r.Host.OnMessage(r.Conn[1], new ChangeSettings { Settings = wanted }, r.Now);
            Assert.AreEqual(40000, r.Host.Settings.Custom.VillageMs, "손님은 못 바꾼다");
            var broken = wanted.Clone();
            broken.Custom.VillageMs = 1;
            r.Host.OnMessage(r.Conn[0], new ChangeSettings { Settings = broken }, r.Now);
            Assert.AreEqual(40000, r.Host.Settings.Custom.VillageMs, "범위 밖은 방이 막는다");
            r.Host.OnMessage(r.Conn[0], new ChangeSettings { Settings = wanted }, r.Now);
            Assert.AreEqual(25000, r.Host.Settings.Custom.VillageMs);
            Assert.AreEqual(25000, r.Net.Last<RoomUpdate>(1).Room.Settings.Custom.VillageMs, "손님에게 방 설정이 간다");
            Assert.AreEqual(r.Now + 40000, r.Host.NextEventMs, "이미 흐르는 선택은 옛 길이 그대로");
            r.Now += 40000;
            r.Host.Tick(r.Now);
            Assert.IsFalse(r.IsOpen(c.Id), "40초에 시간 초과");

            int y = -1;
            r.Until(() => (y = Seat(r)) >= 0);
            Assert.AreEqual(ChoiceKind.VillagePick, r.Mine(y).Kind);
            r.Now = Math.Max(r.Now, r.Host.AnimEndMs);
            r.Host.Tick(r.Now);
            Assert.AreEqual(r.Now + 25000, r.Host.NextEventMs, "다음 고향 마을 = 25초");

            // 프리셋으로 되돌리면 직접 설정이 비고 길이는 프리셋
            var reset = r.Host.Settings;
            reset.Clock = ClockPreset.Normal;
            reset.Custom = null;
            r.Host.OnMessage(r.Conn[0], new ChangeSettings { Settings = reset }, r.Now);
            Assert.AreEqual((ClockPreset.Normal, (ClockSettings)null), (r.Host.Settings.Clock, r.Host.Settings.Custom));
            Assert.AreEqual(ClockPreset.Normal, r.Host.Clock.Preset);
        }

        [Test]
        public void M10_시간세부_전선과저장과방목록왕복_결정140()
        {
            var s = new RoomSettings { Seats = 3 };
            s.StepClock("battle_action", +1);
            s.StepClock("bank_max", +1);
            string json = MessageCodec.Encode(new ChangeSettings { Settings = s }, 4);
            Assert.IsTrue(MessageCodec.TryDecodeRoom(json, out var msg, out _, out string error), error);
            var got = ((ChangeSettings)msg).Settings;
            Assert.AreEqual(ClockPreset.Custom, got.Clock);
            Assert.IsTrue(got.ClockLengths().SameLengths(s.ClockLengths()), "전선 왕복");
            Assert.AreEqual(65000, ClockView.LengthMs(got.ClockLengths(), ClockKind.BattleAction));

            var head = new SaveRoom { Settings = s };
            var saved = SaveStore.Deserialize<SaveRoom>(SaveStore.Serialize(head, false));
            Assert.AreEqual(ClockPreset.Custom, saved.Settings.Clock);
            Assert.IsTrue(saved.Settings.ClockLengths().SameLengths(s.ClockLengths()), "저장 왕복");
            var old = SaveStore.Deserialize<SaveRoom>(SaveStore.Serialize(new SaveRoom { Settings = new RoomSettings { Clock = ClockPreset.Normal } }, false).Replace(",\"Custom\":null", ""));
            Assert.AreEqual((ClockPreset.Normal, (ClockSettings)null), (old.Settings.Clock, old.Settings.Custom), "칸이 없던 옛 저장");

            var listing = ListingData.FromDictionary(new ListingData { Clock = ClockPreset.Custom }.ToDictionary());
            Assert.AreEqual(ClockPreset.Custom, listing.Clock, "방 목록 줄의 「직접 설정」");
        }

        [Test]
        public void M10_감사메모_시계막대전체길이는그단계의길이_고향마을과스타터를따로_결정140()
        {
            // M8 · M9 마감 감사 2차 참고 ①: 셋업 막대 전체가 고향 마을 · 스타터 고르기 둘 중 긴 쪽이었다(고향 마을 20초 막대가 2/3에서 시작)
            var s = new RoomSettings { Seats = 2, Clock = ClockPreset.Custom, Custom = ClockSettings.For(ClockPreset.Normal) };
            s.Custom.Preset = ClockPreset.Custom;
            s.Custom.VillageMs = 20000;
            s.Custom.SetupPickMs = 30000;
            s.Custom.LineupMs = 40000;
            var c = s.ClockLengths();
            Assert.AreEqual(20000, ClockView.LengthMs(c, ClockKind.Setup, ChoiceKind.VillagePick), "고향 마을 = 그 길이(직접 설정)");
            Assert.AreEqual(30000, ClockView.LengthMs(c, ClockKind.Setup, ChoiceKind.StarterPick), "스타터 고르기");
            Assert.AreEqual(30000, ClockView.LengthMs(c, ClockKind.Setup, ChoiceKind.TrainerCardPick), "트레이너 카드 고르기도 같은 셋업 단위(TurnClock)");
            Assert.AreEqual(40000, ClockView.LengthMs(c, ClockKind.BattleAction, ChoiceKind.Lineup), "출전 순서도 그 단계의 길이(TurnClock과 같다)");
            Assert.AreEqual(c.BattleActionMs, ClockView.LengthMs(c, ClockKind.BattleAction, ChoiceKind.BattleAction));
            Assert.AreEqual(c.MapTurnMs, ClockView.LengthMs(c, ClockKind.MapTurn, ChoiceKind.MapAction));
            Assert.AreEqual(0, ClockView.LengthMs(ClockSettings.For(ClockPreset.Off), ClockKind.Setup, ChoiceKind.VillagePick), "시계를 끈 방");
        }
    }
}
