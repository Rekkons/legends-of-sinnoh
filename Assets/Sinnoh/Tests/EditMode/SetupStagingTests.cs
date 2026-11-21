using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan §4 「셋업 연출」(결정 66 ③ — 04 §8-8 1~3·5행)의 순수 부분: 받은 카드 · 고른 뒤 · 공개 · 고향 마을 · 스타팅 포켓몬 · 숨겨진 장소 배치의 시간표와,
    /// 그 단계가 화면 대기열의 어디쯤인지 묻는 것(<see cref="SetupRows"/>). 길이는 방장과 같은 값(<c>PlayTiming.RowMs</c>)이다.
    /// </summary>
    public class SetupStagingTests
    {
        [Test]
        public void M4_받은카드는어긋나게들어와뒷면에서앞면으로뒤집힌다()
        {
            int total = PlayTiming.RowMs(null, SetupRows.Deal);
            Assert.That(total, Is.EqualTo(800), "길이는 방장과 같은 값");
            for (int i = 0; i < 3; i++)
            {
                var before = DealBeats.Card(i, -1, total);
                Assert.That(before.Shown, Is.False, "기다리는 동안 보이지 않는다");
                Assert.That(before.Slide, Is.EqualTo(1f));
                var done = DealBeats.Card(i, total, total);
                Assert.That(done.Shown && done.Face, Is.True, "다 받으면 앞면");
                Assert.That(done.Slide, Is.EqualTo(0f));
                Assert.That(done.ScaleX, Is.EqualTo(1f));
            }
            // 60ms씩 어긋난다: 둘째 장이 나타나기 전에 첫 장은 이미 들어오는 중이다
            int every = total * DealBeats.EveryPermille / 1000;
            Assert.That(every, Is.EqualTo(60));
            Assert.That(DealBeats.Card(0, every - 1, total).Shown, Is.True);
            Assert.That(DealBeats.Card(1, every - 1, total).Shown, Is.False);
            Assert.That(DealBeats.Card(2, 2 * every, total).Shown, Is.True);
            // 미끄러지는 동안은 뒷면, 닿은 뒤 뒤집힌다(가운데에서 그림이 바뀌고 가로가 0을 지난다). 마지막 장도 길이 안에 끝난다
            int slide = total * DealBeats.SlidePermille / 1000, flip = total * DealBeats.FlipPermille / 1000;
            Assert.That(DealBeats.Card(0, slide, total).Face, Is.False);
            Assert.That(DealBeats.Card(0, slide + flip / 2 - 1, total).Face, Is.False);
            Assert.That(DealBeats.Card(0, slide + flip / 2, total).Face, Is.True);
            Assert.That(DealBeats.Card(0, slide + flip / 2, total).ScaleX, Is.EqualTo(0f).Within(0.02f));
            Assert.That(2 * every + slide + flip, Is.LessThanOrEqualTo(total));
            float last = 1f;
            for (int ms = 0; ms <= total; ms += 20)
            {
                var p = DealBeats.Card(1, ms, total);
                Assert.That(p.Slide, Is.LessThanOrEqualTo(last + 1e-5f), ms + "ms — 물러나지 않는다");
                Assert.That(p.ScaleX, Is.InRange(0f, 1f), ms + "ms");
                last = p.Slide;
            }
        }

        [Test]
        public void M4_고른뒤에는나머지가물러나고고른것이가려진뒤공개된다()
        {
            int total = PlayTiming.RowMs(null, SetupRows.Chosen);
            Assert.That(total, Is.EqualTo(1200));
            var before = ChosenBeats.At(-1, total);
            Assert.That(before.Away, Is.EqualTo(0f));
            Assert.That(before.Hide, Is.EqualTo(0f));
            var end = ChosenBeats.At(total, total);
            Assert.That(end.Away, Is.EqualTo(1f));
            Assert.That(end.Hide, Is.EqualTo(1f));
            Assert.That(end.Wobble, Is.EqualTo(0f));
            Assert.That(ChosenBeats.At(total * ChosenBeats.AwayPermille / 1000, total).Away, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(ChosenBeats.At(total * ChosenBeats.HideFromPermille / 1000, total).Hide, Is.EqualTo(0f));
            Assert.That(ChosenBeats.At(total * ChosenBeats.HideToPermille / 1000, total).Hide, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(ChosenBeats.FlipScale(0f, out bool face), Is.EqualTo(1f));
            Assert.That(face, Is.True);
            Assert.That(ChosenBeats.FlipScale(0.5f, out face), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(face, Is.False, "가운데에서 뒷면으로 바뀐다");
            Assert.That(ChosenBeats.FlipScale(1f, out face), Is.EqualTo(1f));
            Assert.That(face, Is.False);

            // 트레이너 카드 공개(12행 2500): 뒷면으로 기다리다 들리며 앞면으로 뒤집히고, 금속광이 한 번 지나간 뒤 내려앉는다
            int reveal = PlayTiming.RowMs(null, SetupRows.Reveal);
            Assert.That(reveal, Is.EqualTo(2500));
            Assert.That(RevealBeats.At(-1, reveal).Face, Is.False);
            Assert.That(RevealBeats.At(reveal * RevealBeats.LiftPermille / 1000, reveal).Lift, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(RevealBeats.At(reveal * RevealBeats.FlipToPermille / 1000, reveal).Face, Is.True);
            Assert.That(RevealBeats.At(reveal * RevealBeats.FlipToPermille / 1000, reveal).ScaleX, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(RevealBeats.At(reveal * RevealBeats.FlipToPermille / 1000 - 1, reveal).Gloss, Is.LessThan(0f), "뒤집히는 동안에는 금속광이 없다");
            Assert.That(RevealBeats.At((reveal * RevealBeats.FlipToPermille / 1000 + reveal * RevealBeats.GlossToPermille / 1000) / 2, reveal).Gloss, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(RevealBeats.At(reveal * RevealBeats.GlossToPermille / 1000, reveal).Gloss, Is.LessThan(0f), "금속광은 한 번뿐이다");
            Assert.That(RevealBeats.At(reveal * RevealBeats.LandFromPermille / 1000, reveal).Land, Is.EqualTo(0f));
            Assert.That(RevealBeats.At(reveal, reveal).Land, Is.EqualTo(1f));
        }

        [Test]
        public void M4_스타팅포켓몬은볼이흔들리다열려도트가나타나고볼로돌아가떨어진다()
        {
            int total = PlayTiming.RowMs(null, SetupRows.Starter);
            Assert.That(total, Is.EqualTo(2200));
            var wait = StarterBeats.At(-1, total);
            Assert.That(wait.Ball, Is.True, "공개 전에는 볼이 기다린다");
            Assert.That(wait.Mon, Is.EqualTo(0f));
            int wobble = total * StarterBeats.WobblePermille / 1000, open = total * StarterBeats.OpenPermille / 1000, mon = total * StarterBeats.MonToPermille / 1000;
            int back = total * StarterBeats.BackPermille / 1000, ball = total * StarterBeats.BallPermille / 1000;
            bool left = false, right = false;
            for (int ms = 0; ms < wobble; ms += 10)
            {
                var p = StarterBeats.At(ms, total);
                Assert.That(p.Ball, Is.True);
                left |= p.Wobble < -0.5f;
                right |= p.Wobble > 0.5f;
            }
            Assert.That(left && right, Is.True, "좌우로 흔들린다");
            Assert.That(StarterBeats.At((wobble + open) / 2, total).Hop, Is.EqualTo(1f).Within(0.01f), "튀어 오른다");
            var opened = StarterBeats.At(open, total);
            Assert.That(opened.Ball, Is.False, "열렸다");
            Assert.That(opened.Open, Is.EqualTo(1f));
            Assert.That(opened.Flash, Is.EqualTo(1f), "흰 빛");
            Assert.That(opened.Mon, Is.EqualTo(0f));
            var shown = StarterBeats.At((mon + back) / 2, total);
            Assert.That(shown.Mon, Is.EqualTo(1f), "도트가 다 나타났다");
            Assert.That(shown.Flash, Is.EqualTo(0f), "흰 빛이 걷혔다");
            Assert.That(shown.Open, Is.EqualTo(0f));
            var closing = StarterBeats.At(ball, total);
            Assert.That(closing.Ball, Is.True, "볼로 줄었다");
            Assert.That(closing.Mon, Is.EqualTo(0f));
            Assert.That(closing.Drop, Is.EqualTo(0f));
            Assert.That(StarterBeats.At(total, total).Drop, Is.EqualTo(1f), "파티로 들어갔다");
        }

        [Test]
        public void M4_고향마을은카메라가다가가고맥박이두번친뒤말이떨어져내려앉는다()
        {
            int total = PlayTiming.RowMs(new VillagePicked(), VillageBeats.Row);
            Assert.That(total, Is.EqualTo(1800));
            Assert.That(VillageBeats.At(0, total).Fall, Is.LessThan(0f), "말은 아직 없다");
            Assert.That(VillageBeats.At(total * VillageBeats.GlidePermille / 1000, total).Glide, Is.EqualTo(1f).Within(1e-4f), "카메라가 먼저 닿는다");
            int pulses = 0;
            bool on = false;
            for (int ms = 0; ms < total; ms += 5)
            {
                var p = VillageBeats.At(ms, total);
                if (p.Pulse && !on) pulses++;
                on = p.Pulse;
            }
            Assert.That(pulses, Is.EqualTo(VillageBeats.Pulses), "맥박 두 번");
            int fall = VillageBeats.FallAt(total), land = total * VillageBeats.LandPermille / 1000;
            Assert.That(fall, Is.GreaterThanOrEqualTo(total * (VillageBeats.PulseFromPermille + VillageBeats.PulseEveryPermille * VillageBeats.Pulses) / 1000), "맥박이 끝난 뒤에 떨어진다");
            Assert.That(VillageBeats.At(fall - 1, total).Fall, Is.LessThan(0f));
            Assert.That(VillageBeats.At(fall, total).Fall, Is.EqualTo(0f));
            Assert.That(VillageBeats.At(land - 1, total).Landed, Is.False);
            var landed = VillageBeats.At(land, total);
            Assert.That(landed.Landed, Is.True, "내려앉는 때 좌석 칩이 칠해진다");
            Assert.That(landed.Fall, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(VillageBeats.At((land + total * VillageBeats.HopToPermille / 1000) / 2, total).Hop, Is.EqualTo(1f).Within(0.01f), "한 번 튄다");
            var end = VillageBeats.At(total, total);
            Assert.That(end.Fall, Is.EqualTo(1f));
            Assert.That(end.Hop, Is.EqualTo(0f));
            float last = -1f;
            for (int ms = 0; ms <= total; ms += 20)
            {
                float f = VillageBeats.At(ms, total).Fall;
                Assert.That(f, Is.GreaterThanOrEqualTo(last - 1e-5f), ms + "ms — 다시 올라가지 않는다");
                last = f;
            }
        }

        [Test]
        public void B2_숨겨진장소토큰은칸색인순서로떨어지고길이안에다내려앉는다()
        {
            var placed = new TokensPlaced { Kind = TokenKind.HiddenSpot, Cells = new int[40], Villages = new int[0] };
            int total = PlayTiming.RowMs(placed, SpotDropBeats.Row);
            Assert.That(total, Is.EqualTo(1500), "마을 없는 놓기 = 1500(방장과 같은 값)");
            int n = placed.Cells.Length;
            Assert.That(SpotDropBeats.DropAt(total, 0, n), Is.EqualTo(SpotDropBeats.LeadMs), "카메라가 물러난 뒤 첫 토큰");
            for (int i = 1; i < n; i++)
                Assert.That(SpotDropBeats.DropAt(total, i, n), Is.GreaterThanOrEqualTo(SpotDropBeats.DropAt(total, i - 1, n)), "칸 색인 순서(놓은 순서가 아니다)");
            Assert.That(SpotDropBeats.DropAt(total, n - 1, n) + SpotDropBeats.FallMs + SpotDropBeats.TailMs, Is.LessThanOrEqualTo(total), "마지막 토큰도 길이 안에 내려앉는다");
            Assert.That(SpotDropBeats.Fall(0, total, 0, n), Is.LessThan(0f));
            Assert.That(SpotDropBeats.Fall(total, total, n - 1, n), Is.EqualTo(1f));
            Assert.That(SpotDropBeats.Wide(SpotDropBeats.LeadMs), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(SpotDropBeats.Squash(SpotDropBeats.LeadMs + SpotDropBeats.FallMs, total, 0, n), Is.LessThan(1f), "내려앉는 순간 눌린다");
            Assert.That(SpotDropBeats.Squash(total, total, 0, n), Is.EqualTo(1f));
            // 토큰 하나뿐인 판도 된다
            Assert.That(SpotDropBeats.DropAt(total, 0, 1), Is.EqualTo(SpotDropBeats.LeadMs));

            // 조무래기 놓기와 같은 행(50)이지만 단계는 서로 다른 것으로 가린다
            var spots = new FxStep { Row = SpotDropBeats.Row, Ms = total };
            spots.Events.Add(placed);
            Assert.That(SpotDropBeats.Spots(spots), Is.SameAs(placed));
            Assert.That(TokenBeats.Grunts(spots), Is.Null);
            var grunts = new FxStep { Row = TokenBeats.Row, Ms = 1800 };
            grunts.Events.Add(new TokensPlaced { Kind = TokenKind.Grunt, Cells = new[] { 3 }, Villages = new[] { 1 } });
            Assert.That(SpotDropBeats.Spots(grunts), Is.Null);
        }

        [Test]
        public void M4_셋업단계가대기열의어디쯤인지와남았는지를안다()
        {
            // 항목 둘: [고향 마을 1800] · [스타팅 포켓몬을 고른 뒤 1200 → 공개 2200]
            var village = new FxEntry { Seq = 1, QueueMs = 1800 };
            var v = new FxStep { Row = SetupRows.Village, Layer = FxLayer.Item, Ms = 1800 };
            v.Events.Add(new VillagePicked { Seat = 0, Cell = 7 });
            village.Steps.Add(v);
            var picks = new FxEntry { Seq = 2, QueueMs = 3400 };
            picks.Steps.Add(new FxStep { Row = SetupRows.Chosen, Layer = FxLayer.Item, Ms = 1200, Tag = ChoiceKind.StarterPick });
            picks.Steps.Add(new FxStep { Row = SetupRows.Starter, Layer = FxLayer.Item, StartMs = 1200, Ms = 2200 });
            var seq = new FxSequencer();
            Assert.That(SetupRows.Pending(seq), Is.False);
            Assert.That(SetupRows.At(seq, SetupRows.Village, null, out _), Is.EqualTo(SetupRows.Gone));
            seq.Enqueue(new List<FxEntry> { village, picks });
            Assert.That(SetupRows.Pending(seq), Is.True);
            Assert.That(SetupRows.Pending(seq, SetupRows.Village), Is.True);
            Assert.That(SetupRows.At(seq, SetupRows.Village, null, out var step), Is.EqualTo(SetupRows.Waiting), "아직 시작하지 않았다");
            Assert.That(VillageBeats.Picked(step).Cell, Is.EqualTo(7));
            Assert.That(SetupRows.Ms(SetupRows.Waiting, step, true, 0, out int total), Is.EqualTo(-1));
            Assert.That(total, Is.EqualTo(1800));

            seq.Advance(600);
            Assert.That(SetupRows.At(seq, SetupRows.Village, null, out _), Is.EqualTo(600));
            Assert.That(SetupRows.At(seq, SetupRows.Starter, null, out _), Is.EqualTo(SetupRows.Waiting));
            Assert.That(SetupRows.At(seq, SetupRows.Chosen, ChoiceKind.TrainerCardPick, out _), Is.EqualTo(SetupRows.Gone), "꼬리표가 다른 「고른 뒤」는 치지 않는다");
            Assert.That(SetupRows.At(seq, SetupRows.Chosen, ChoiceKind.StarterPick, out _), Is.EqualTo(SetupRows.Waiting));

            seq.Advance(1200 + 1500);   // 마을 항목이 끝나고 다음 항목의 1500
            Assert.That(SetupRows.Pending(seq, SetupRows.Village), Is.False, "고향 마을 단계는 끝났다 — 고르는 화면이 열린다");
            Assert.That(SetupRows.Pending(seq), Is.True, "고른 뒤의 연출이 남았다 — 보드 HUD는 아직");
            Assert.That(SetupRows.At(seq, SetupRows.Chosen, ChoiceKind.StarterPick, out step), Is.EqualTo(1200), "끝난 단계는 길이(끝 모습)");
            Assert.That(SetupRows.At(seq, SetupRows.Starter, null, out _), Is.EqualTo(300));
            Assert.That(SetupRows.EndOf(picks), Is.EqualTo(3400));

            seq.Advance(1900);
            Assert.That(SetupRows.Pending(seq), Is.False, "셋업 연출이 다 보였다");
            Assert.That(SetupRows.At(seq, SetupRows.Starter, null, out step), Is.EqualTo(SetupRows.Gone));
            Assert.That(SetupRows.Ms(SetupRows.Gone, step, true, 2200, out total), Is.EqualTo(2200), "지난 단계 = 끝 모습");
            Assert.That(SetupRows.Ms(SetupRows.Gone, step, false, 2200, out total), Is.EqualTo(-1), "아직 받지 않은 단계 = 처음 모습");
            foreach (int row in new[] { SetupRows.Deal, SetupRows.Chosen, SetupRows.Reveal, SetupRows.Village, SetupRows.Starter })
                Assert.That(SetupRows.Built(row), Is.True);
            Assert.That(SetupRows.Built(990), Is.False, "동시 선택 공개(99행)는 아직 그리지 않는다");
        }
    }
}
