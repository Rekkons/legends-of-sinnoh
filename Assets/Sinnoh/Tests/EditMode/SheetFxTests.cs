using System.Collections.Generic;
using NUnit.Framework;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 도트 이펙트 시트(결정 69, pixelfx)의 순수 부분: 프레임 셈(<see cref="FxSheet"/>) · 포획 자리(<see cref="SheetBeats.Capture"/>) · 항목이 끝난 뒤의 꼬리 시각(<see cref="FxSequencer.SinceLastMs"/>).
    /// 실제 판에서 배틀 자리가 서는지는 <see cref="PlaceStagingTests"/>.
    /// </summary>
    public class SheetFxTests
    {
        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        const string Line = "{\"key\":\"dust\",\"file\":\"Fx/dust.png\",\"id\":\"impacts/dust-puff\",\"fw\":36,\"fh\":10,\"frames\":6,\"ms\":45,\"ox\":19,\"oy\":9,\"loop\":false},";

        [Test]
        public void M4_시트프레임은시작한뒤의시간만으로고르고다돌면그리지않는다()
        {
            var s = FxSheet.Parse(Line);
            Assert.That(s, Is.Not.Null);
            Assert.That((s.Key, s.File, s.Fw, s.Fh, s.Frames, s.Ms, s.Ox, s.Oy, s.Loop), Is.EqualTo(("dust", "Fx/dust.png", 36, 10, 6, 45, 19, 9, false)));
            Assert.That(s.TotalMs, Is.EqualTo(270));
            Assert.That(s.FrameAt(-1), Is.EqualTo(-1), "시작 전");
            Assert.That(s.FrameAt(0), Is.EqualTo(0));
            Assert.That(s.FrameAt(44), Is.EqualTo(0));
            Assert.That(s.FrameAt(45), Is.EqualTo(1));
            Assert.That(s.FrameAt(269), Is.EqualTo(5));
            Assert.That(s.FrameAt(270), Is.EqualTo(-1), "한 번 도는 시트는 끝나면 없다");
            Assert.That(s.FrameAt(long.MaxValue / 4), Is.EqualTo(-1));
            s.Loop = true;
            Assert.That(s.FrameAt(270), Is.EqualTo(0), "도는 시트");
            Assert.That(s.FrameAt(270 + 46), Is.EqualTo(1));
            Assert.That(FxSheet.Parse("{\"schema\":1,"), Is.Null);
            Assert.That(FxSheet.ParseAll("{\"schema\":1,\n\"rows\":[\n" + Line + "\n" + Line.Replace("dust", "open") + "\n]}").Count, Is.EqualTo(2));
            // 키: 타입마다 · 자리마다(상대 = 오른쪽으로, 나 = 왼쪽으로)
            Assert.That(SheetBeats.Hit(PokemonType.Fire, true), Is.EqualTo("hit_Fire"));
            Assert.That(SheetBeats.Hit(PokemonType.Water, false), Is.EqualTo("hitl_Water"));
            Assert.That(SheetBeats.Hit((PokemonType)GameEvent.U, true), Is.EqualTo("hit_Typeless"), "모르는 타입은 무속성 색");
            Assert.That(SheetBeats.All().Count, Is.EqualTo(4 + 2 * System.Enum.GetValues(typeof(PokemonType)).Length));
        }

        [Test]
        public void M4_포획이펙트는볼이열릴때빛_실패는다시빛_성공판정에는없다_결정70()
        {
            var list = new List<FxStamp>();
            foreach (bool success in new[] { true, false })
            {
                var c = new CaptureAttempt { Success = success, Shakes = success ? 3 : 1 };
                int verdict = CaptureStage.VerdictMs(c);
                list.Clear();
                SheetBeats.Capture(c, EnterBeats.FlyEndMs + 30, 192, 92, list);
                Assert.That(list.Count, Is.EqualTo(success ? 1 : 2), "성공 판정의 반짝임은 뺐다(결정 70 ⑬ ④)");
                Assert.That((list[0].Key, list[0].Ms, list[0].X, list[0].Y), Is.EqualTo((SheetBeats.Open, 30, 192, 92 - EnterBeats.OpenAbovePx)), "볼이 열린 자리");
                if (!success) Assert.That(list[1].Ms, Is.LessThan(0), "판정 전");
                list.Clear();
                SheetBeats.Capture(c, verdict + 60, 192, 92, list);
                Assert.That(list.Exists(x => x.Key == SheetBeats.Star), Is.False, "포획에는 pixelfx 반짝임이 없다");
                if (!success) Assert.That((list[1].Key, list[1].Ms, list[1].X, list[1].Y), Is.EqualTo((SheetBeats.Open, 60, 192, 92 - EnterBeats.RestAbovePx)), "볼이 놓인 자리");
                // 같은 시각이면 같은 값(앞 호출에 기대지 않는다)
                var again = new List<FxStamp>();
                SheetBeats.Capture(c, verdict + 60, 192, 92, again);
                Assert.That(again, Is.EqualTo(list));
            }
            list.Clear();
            SheetBeats.Capture(null, 100, 0, 0, list);
            SheetBeats.Capture(new CaptureAttempt(), -1, 0, 0, list);
            Assert.That(list, Is.Empty);
        }

        [Test]
        public void M4_항목이끝난뒤의꼬리시각은나눠보내도같고끄기에서는없다()
        {
            var whole = new FxSequencer();
            Assert.That(whole.SinceLastMs, Is.GreaterThan(1000000), "끝난 항목이 없다");
            whole.Enqueue(new[] { new FxEntry { Seq = 1, QueueMs = 1000 } });
            whole.Advance(1130);
            Assert.That(whole.Last.Seq, Is.EqualTo(1));
            Assert.That(whole.SinceLastMs, Is.EqualTo(130));
            var split = new FxSequencer();
            split.Enqueue(new[] { new FxEntry { Seq = 1, QueueMs = 1000 } });
            for (int t = 0; t < 1130; t += 16) split.Advance(System.Math.Min(16, 1130 - t));
            Assert.That(split.SinceLastMs, Is.EqualTo(130), "16ms씩 보내도 같다");
            // 빠름: 대기열은 두 배로, 꼬리는 실시각
            var fast = new FxSequencer { Speed = FxSpeed.Fast };
            fast.Enqueue(new[] { new FxEntry { Seq = 1, QueueMs = 1000 } });
            fast.Advance(600);
            Assert.That(fast.SinceLastMs, Is.EqualTo(100));
            // 끄기: 보이지 않고 지나간 항목의 꼬리는 그리지 않는다
            var off = new FxSequencer { Speed = FxSpeed.Off };
            off.Enqueue(new[] { new FxEntry { Seq = 1, QueueMs = 1000 } });
            off.Advance(16);
            Assert.That(off.Last.Seq, Is.EqualTo(1));
            Assert.That(off.SinceLastMs, Is.GreaterThan(1000000));
        }
    }
}
