using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Editor;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.EditorAssets
{
    /// <summary>
    /// 연출 캡처 스모크(04 §8-7, plan M4-3 「연출 층 뼈대」): 이벤트 대본 → 화면 대기열 → 부품 Seek(ms)가 엔진 쪽에서 이어지는지.
    /// 견본 장면(<see cref="FxSample"/>)의 표시 값·막대가 단서에 맞춰 바뀌는지, 같은 시각이면 한 번에 보내도 나눠 보내도 같은 그림인지 본다.
    /// Gen/이 없으면 건너뛰고, 그래픽 장치가 없으면(-nographics) 그리는 시험만 건너뛴다.
    /// </summary>
    public class FxShotsTests
    {
        [Test]
        public void M4_연출견본은단서가닿을때표시값이바뀐다()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            try
            {
                var sample = new FxSample(new VisualElement(), skin, FxShots.Grade);
                Assert.AreEqual("체력 20 / 20", sample.HpText, "보기는 이미 9지만 화면은 앞 값을 붙잡는다");
                Assert.AreEqual(1f, sample.BarRatio);
                Assert.AreEqual(1, sample.Runner.Busy);

                sample.Seek(3900);   // 첫 공격의 체력 막대가 깎이는 중(3560~4360)
                Assert.AreEqual("체력 20 / 20", sample.HpText, "숫자는 착지 전");
                Assert.Less(sample.BarRatio, 1f);
                Assert.Greater(sample.BarRatio, 13f / 20f);

                sample.Seek(4500);   // 착지 뒤, 둘째 공격 시작
                Assert.AreEqual("체력 13 / 20", sample.HpText);
                Assert.AreEqual(13f / 20f, sample.BarRatio, 1e-6);
                Assert.AreEqual(2, sample.Sequencer.Current.Seq, "둘째 항목 재생 중");
                Assert.AreEqual(0, sample.Sequencer.Pending);

                sample.Seek(9100);   // 다 끝남(4360 × 2 + 클릭 막기 300)
                Assert.AreEqual("체력 9 / 20", sample.HpText, "대기열이 비면 보기와 같다");
                Assert.IsTrue(sample.Sequencer.IsEmpty);
                Assert.IsTrue(sample.Sequencer.InputOpen);
                Assert.AreEqual(0, sample.Runner.Busy, "실행기에서 빠졌다");
            }
            finally
            {
                UiSkin.Release(skin);
            }
        }

        [Test]
        public void M4_연출캡처는한번에보내도나눠보내도같은그림이고시각마다다르다()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var layoutLogs = new List<string>();
            Color32[] prev = null;
            foreach (int ms in new[] { 3400, 3900 })   // 피해 숫자가 나는 중 · 체력 막대가 깎이는 중
            {
                var jump = FxShots.Shoot("sample", ms, 0, layoutLogs);
                if (jump == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
                var stepped = FxShots.Shoot("sample", ms, FxShots.FrameMs, layoutLogs);
                Assert.AreEqual(FxShots.Grade.Width * FxShots.Grade.Height, jump.Length);
                int differing = 0;
                var colors = new HashSet<Color32>();
                for (int i = 0; i < jump.Length; i++)
                {
                    if (!jump[i].Equals(stepped[i])) differing++;
                    colors.Add(jump[i]);
                }
                Assert.AreEqual(0, differing, ms + "ms: 한 번에 보낸 것과 나눠 보낸 것이 다른 픽셀");
                Assert.Greater(colors.Count, 4, ms + "ms: 색 수");
                if (prev != null)
                {
                    int moved = 0;
                    for (int i = 0; i < jump.Length; i++) if (!jump[i].Equals(prev[i])) moved++;
                    Assert.Greater(moved, 0, ms + "ms: 앞 시각과 같은 그림(움직이지 않았다)");
                }
                prev = jump;
            }
            CollectionAssert.IsEmpty(layoutLogs, "Layout update failed to stabilize");
        }

        [Test]
        public void M4_턴넘김견본은요약띠컷신이차례로돌고끝나면걷힌다_결정53_57()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            try
            {
                // 결정 57: 두 좌석 = 요약 800 · 띠 600 · 컷신 600(2초), 한 좌석 판 = 띠 500 · 컷신 500(1초)이고 요약 800은 주운 것이 있는 턴에만
                foreach (var v in new[] { (two: true, gain: true, sum: 800, band: 600, cut: 600), (two: false, gain: true, sum: 800, band: 500, cut: 500), (two: false, gain: false, sum: 0, band: 500, cut: 500) })
                {
                    string at = (v.two ? "두 좌석" : "한 좌석") + (v.gain ? " · 주운 것 있음: " : " · 주운 것 없음: ");
                    var root = new VisualElement();
                    var sample = new FxTurnSample(root, skin, FxShots.Grade, UiShots.Strings(), v.two, v.gain);
                    var summary = root.Q("turn-band-summary");
                    var strip = root.Q("turn-band-strip");
                    var cut = root.Q("turn-band-cut");
                    Assert.AreEqual(0, sample.Band.ActiveRow, at + "시작 전");
                    Assert.AreEqual(0f, summary.style.opacity.value);

                    if (v.sum > 0)
                    {
                        sample.Seek(v.sum / 2);   // 요약 머묾(오름은 처음 1/5)
                        Assert.AreEqual(FxRows.TurnSummary, sample.Band.ActiveRow, at + "요약");
                        Assert.AreEqual(1f, summary.style.opacity.value, 1e-6);
                        Assert.AreEqual(0f, strip.style.opacity.value);
                        Assert.AreEqual("플레이어 1의 턴 결과", summary.Q<Label>(className: "kit-title").text, "머리 = 「○○의 턴 결과」(ui-text-guide §3)");
                        Assert.AreEqual("규토리 +3", summary.Query<Label>().ToList()[1].text);
                    }

                    sample.Seek(v.sum + v.band / 2);   // 띠: 들어옴 1/4 · 머묾 1/2 · 나감 1/4 — 한가운데는 머묾
                    Assert.AreEqual(FxRows.Handoff, sample.Band.ActiveRow, at + "띠(요약이 없으면 띠부터)");
                    Assert.AreEqual(1f, strip.style.opacity.value);
                    Assert.AreEqual(0f, strip.style.translate.value.x.value, "머무는 동안 제자리");
                    Assert.AreEqual(0f, summary.style.opacity.value, "띠가 들어오면 요약은 걷힌다(요약이 없는 턴에는 처음부터 없다)");
                    Assert.AreEqual(v.two ? "플레이어 1 ▶ 플레이어 2" : "2라운드", strip.Q<Label>(className: "sn-band-text").text, "한 좌석뿐이면 라운드를 적는다");
                    Assert.AreEqual(0f, cut.style.opacity.value, "컷신은 띠와 겹치지 않는다(결정 55 ②)");
                    // 머묾은 띠 길이의 절반이다 — 줄인 길이에서도 글자를 읽을 시간이 남는다(머묾의 처음과 끝에서 제자리)
                    sample.Seek(v.sum + v.band * 3 / 4 - 1);
                    Assert.AreEqual(0f, strip.style.translate.value.x.value, at + "머묾 끝");

                    sample.Seek(v.sum + v.band + v.cut / 2);   // 컷신: 열림 1/6 · 머묾 · 끝 1/6 동안 걷힘
                    Assert.AreEqual(FxRows.MyTurn, sample.Band.ActiveRow, at + "컷신");
                    Assert.AreEqual(1f, cut.style.opacity.value);
                    Assert.AreEqual(1f, cut.style.scale.value.value.y, 1e-6, "다 열렸다");
                    Assert.AreEqual(0f, strip.style.opacity.value);
                    Assert.AreEqual("내 턴", cut.Q<Label>(className: "sn-band-text-big").text);
                    Assert.IsFalse(sample.Sequencer.InputOpen);

                    sample.Seek(v.sum + v.band + v.cut - 1);
                    Assert.IsFalse(sample.Sequencer.IsEmpty, at + "넘김 길이 = 요약 + 띠 + 컷신");
                    sample.Seek(v.sum + v.band + v.cut);
                    Assert.IsTrue(sample.Sequencer.IsEmpty, at + "넘김이 끝났다(여럿 2000 · 솔로 1000 · 솔로 + 요약 1800)");
                    Assert.IsFalse(sample.Sequencer.InputOpen, "클릭 막기 300");

                    sample.Seek(v.sum + v.band + v.cut + 300);   // 끝 + 클릭 막기
                    Assert.AreEqual(0, sample.Band.ActiveRow);
                    Assert.AreEqual(0f, cut.style.opacity.value, "착지 뒤 걷힌다");
                    Assert.IsTrue(sample.Sequencer.InputOpen, "컷신 끝에 입력이 열린다");
                    Assert.AreEqual(0, sample.Runner.Busy);
                }
            }
            finally
            {
                UiSkin.Release(skin);
            }
        }

        [Test]
        public void M4_턴넘김캡처는한번에보내도나눠보내도같은그림이다()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var layoutLogs = new List<string>();
            foreach (int ms in new[] { 860, 1440 })   // 띠가 들어오는 중 · 컷신이 열리는 중(요약 0~800 · 띠 ~1400 · 컷신 ~2000)
            {
                var jump = FxShots.Shoot("turn-band-2p", ms, 0, layoutLogs);
                if (jump == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
                var stepped = FxShots.Shoot("turn-band-2p", ms, FxShots.FrameMs, layoutLogs);
                int differing = 0;
                var colors = new HashSet<Color32>();
                for (int i = 0; i < jump.Length; i++)
                {
                    if (!jump[i].Equals(stepped[i])) differing++;
                    colors.Add(jump[i]);
                }
                Assert.AreEqual(0, differing, ms + "ms: 한 번에 보낸 것과 나눠 보낸 것이 다른 픽셀");
                Assert.Greater(colors.Count, 2, ms + "ms: 띠가 그려졌다");
            }
            CollectionAssert.IsEmpty(layoutLogs, "Layout update failed to stabilize");
        }

        // ── 배틀 연출(plan §4 「배틀 연출」, 04 §8-9): 실제 판에서 기술을 누른 뒤의 화면 대기열 ──

        [Test]
        public void M4_배틀공격연출은굴림기술상성계산체력의차례로돌고착지때체력이바뀐다()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            try
            {
                var root = new VisualElement();
                var seek = FxShots.BattleAttack(root, skin, FxShots.DiceGrade);   // 고정 시드 판: [도전] → 내 차례에 첫 기술을 눌렀다
                var flow = FxShots.BattleFlow;
                var stage = flow.Stage;
                var screen = (BattleScreen)stage.Current;
                var seq = flow.Fx.Sequencer;
                Assert.IsNotNull(screen.Ds, "DS 층(Gen 무대·글꼴)");
                seek(1);
                var entry = seq.Current;
                Assert.IsNotNull(entry, "공격 항목이 재생 중");

                // 단계 순서(04 §8-8): 굴림 56 → 기술 59 → 상성 60 → 계산 줄 61 → (상태 62) → 체력 61
                FxStep roll = entry.Step(560), move = entry.Step(590), match = entry.Step(600), calc = entry.Step(611), drain = entry.Step(612);
                foreach (var s in new[] { roll, move, match, calc, drain }) Assert.IsNotNull(s);
                Assert.Less(roll.StartMs, move.StartMs);
                Assert.Less(move.StartMs, match.StartMs);
                Assert.Less(match.StartMs, calc.StartMs);
                Assert.Less(calc.StartMs, drain.StartMs);
                foreach (var s in entry.Steps) Assert.IsTrue(GameFlow.Built(s), "대기열에 남은 단계 " + s.Row + "는 부품이 있는 행이다");
                var rolled = (DiceRolled)roll.Events[0];
                var used = (MoveUsed)move.Events[0];
                var dmg = (DamageBreakdown)calc.Events[0];
                HpChanged hit = null;
                foreach (var e in drain.Events) if (e is HpChanged h && h.PokemonId == dmg.TargetId) hit = h;
                Assert.IsNotNull(hit, "이 판의 첫 공격은 피해가 들어간다");
                Assert.AreNotEqual(hit.Before, hit.After);
                int foe = 1 - flow.Battle.MySide;
                Assert.AreEqual(hit.PokemonId, screen.ShownMon(foe).Id);

                // 누른 직후: 입력이 닫히고, 기술 줄은 아직 보인다(굴림판은 150 뒤)
                Assert.IsFalse(screen.Ready);
                Assert.IsFalse(screen.Open);
                Assert.IsFalse(screen.Rolling);
                Assert.IsFalse(screen.PressMove(0), "연출 중에는 답이 나가지 않는다");
                Assert.AreEqual(hit.Before, screen.ShownMon(foe).Hp, "보기는 이미 새 체력이지만 화면은 앞 값");
                Assert.AreNotEqual(hit.Before, Sinnoh.Rules.PokemonRules.Hp(flow.Ctx, flow.Client.View.Pokemon[hit.PokemonId]));

                // 굴림(56행): 굴림판 → 다 세면 결과 줄 = 엔진의 면, 합계 = 눈 합
                seek(roll.StartMs + 400);
                Assert.IsTrue(screen.Rolling, "기술 줄 자리가 굴림판으로");
                Assert.IsTrue(screen.RollBoardOn, "녹화본 재생");
                Assert.IsNull(root.Q("battle-move-1"));
                Assert.IsEmpty(screen.RollFaces);
                Assert.AreEqual(0, screen.RollSum);
                Assert.AreEqual(0, screen.CalcLit, "계산 줄은 아직 비어 있다");
                string moveName = stage.T(BattleFeed.MoveNameKey(flow.Ctx, used.MoveKey));
                // 결정 143 ⑦ 준비 동작: 기술 문장과 당긴 자세는 굴림과 함께(옛 단정 「기술 문장은 기술 단계가 시작한 뒤에」 — 주사위가 다 선 뒤에야 공격 모습이 나와 템포가 느렸다)
                StringAssert.Contains(moveName + "!", screen.Message, "굴리는 동안 「○○의 △△!」");
                Assert.Less(screen.Painted.Player.Dx, 0, "공격한 내 도트가 상대 반대쪽(왼쪽)으로 당겨 있다");
                Assert.AreEqual(0, screen.Painted.Enemy.Dx, "맞을 쪽은 그대로");
                seek(roll.EndMs);
                CollectionAssert.AreEqual(rolled.Faces, screen.RollFaces, "결과 줄의 면 = 엔진이 정한 면");
                Assert.AreEqual(AttackDice.Sum(rolled.Faces), screen.RollSum);
                Assert.AreEqual(rolled.Faces.Length, root.Query(className: "sn-battle-rollface").ToList().Count, "면 그림 수");

                // 기술(59행) · 상성(60행): 메시지 창의 문장이 그 단계에 뜬다
                if (move.StartMs - 1 >= roll.EndMs)
                {
                    seek(move.StartMs - 1);
                    Assert.AreEqual(0, screen.CalcLit, "맞기 전에는 이 공격의 계산이 없다(결과는 59행 뒤에만 — 결정 143 ⑦)");
                }
                seek(move.StartMs + 1);
                StringAssert.Contains(moveName + "!", screen.Message, "「○○의 △△!」");
                Assert.AreEqual(BattleBeats.LungeHitPx, screen.Painted.Player.Dx, "맞는 순간: 당긴 자세에서 돌진");
                Assert.AreEqual(hit.Before, screen.ShownMon(foe).Hp);

                // 계산 줄(결정 171 ①): 맞는 순간(59행 시작)부터 칸이 왼쪽부터 켜지고 총합이 350에 켜진다 — 상성(60행)을 기다리지 않는다. 체력 줄은 아직 없다
                Assert.AreEqual(move.StartMs, screen.CalcFromMs, "계산 줄은 59행 시작부터 잰다");
                Assert.AreEqual(1, screen.CalcLit, "주사위 칸(맞는 순간 + 1)");
                Assert.AreEqual(Visibility.Hidden, root.Q("battle-calc-total").style.visibility.value);
                seek(move.StartMs + 150);
                Assert.AreEqual(2, screen.CalcLit, "+ 효과 칸");
                seek(move.StartMs + PlayTiming.DamageCalcMs.Value);
                Assert.Less(move.StartMs + PlayTiming.DamageCalcMs.Value, calc.StartMs, "총합은 계산 단계(61행)보다 먼저 선다");
                Assert.AreEqual(4, screen.CalcLit, "+ 상성 칸 + 총합");
                Assert.AreNotEqual(Visibility.Hidden, root.Q("battle-calc-total").style.visibility.value);
                Assert.AreEqual(dmg.Final.ToString(), root.Q("battle-calc-total").Query<Label>().ToList()[1].text);
                Assert.IsFalse(screen.CalcTaken, "내가 준 피해(상대가 맞았다)");
                Assert.IsTrue(root.Q("battle-calc-target").ClassListContains("sn-battle-calc-tag-foe"), "상대 이름표 = 붉은 바탕");
                Assert.IsNotNull(root.Q("battle-calc-face").Q(className: "sn-mon"), "맞은 포켓몬의 작은 도트");
                Assert.IsFalse(screen.CalcHpShown);
                seek(calc.StartMs);
                Assert.AreEqual(4, screen.CalcLit, "계산 단계 동안에도 다 켜진 채");

                // 체력(61행): 깎이는 동안 숫자는 옛 값이고 막대만 움직인다 → 착지에 새 값
                seek(drain.StartMs + drain.Ms / 2);
                Assert.AreEqual(hit.Before, screen.ShownMon(foe).Hp, "착지 전에는 옛 체력");
                var pose = BattlePose.Of(seq, hit.PokemonId, foe, true);
                Assert.IsTrue(pose.Draining);
                Assert.Less(pose.Bar, hit.Before);
                Assert.Greater(pose.Bar, hit.After);
                Assert.IsFalse(screen.CalcHpShown);
                seek(drain.EndMs);
                Assert.AreEqual(hit.After, screen.ShownMon(foe).Hp, "착지 뒤에는 새 체력");
                Assert.IsTrue(screen.CalcHpShown, "계산 줄 아래 「♥앞 → ♥뒤」");
                var hpLine = root.Q("battle-calc-hp").Query<Label>().ToList().ConvertAll(l => l.text);
                CollectionAssert.Contains(hpLine, hit.Before.ToString());
                CollectionAssert.Contains(hpLine, hit.After.ToString());

                // 끝: 항목이 끝나면 기술 줄 자리로 돌아오고, 클릭 막기 300 뒤 입력이 열린다
                seek(entry.LengthMs);
                Assert.IsFalse(screen.Rolling, "공격 항목이 끝나면 굴림판이 걷힌다");
                Assert.IsNull(root.Q("battle-roll"));
                Assert.IsFalse(screen.Ready, "클릭 막기");
                seek(entry.LengthMs + 300);
                Assert.IsTrue(screen.Ready, "연출이 끝나면 입력이 열린다");
                Assert.AreEqual(flow.Battle.Lines.Count, screen.ShownLineCount, "기록 줄이 다 붙었다");
            }
            finally
            {
                FxShots.Release();
                UiSkin.Release(skin);
            }
        }

        [Test]
        public void M4_결정73_우선권토큰은DS내상자옆에떨어져놓이고쓰면돌며상대상자옆으로간다()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var mine = DsStage.PlayerBoxAt + DsStage.PlayerTokenFromBox;
            var theirs = DsStage.EnemyBoxAt + DsStage.EnemyTokenFromBox;
            var grade = FxShots.DiceGrade;
            float foeMonY;   // 기둥 상대 블록의 포켓몬 줄 높이(패널 배치) — 토큰이 상대 머리 줄로 넘어와도 밀리지 않는다(결정 73 검토 반영)
            float FoeMonY(UiShotPanel p, VisualElement r)
            {
                p.Tick(3);
                return r.Q("battle-foe").worldBound.y;
            }
            try
            {
                // 놓기(53행): Defeat 판의 첫 야생 배틀 인트로 — 체력 상자가 다 들어온 뒤 내(도전자) 상자 옆에 떨어진다. 기둥의 그림도 그때
                using var panel = new UiShotPanel(grade.Width, grade.Height, grade.PanelScale);
                var root = panel.Root;
                var seek = FxShots.RowScene(root, skin, grade, SoloTrail.Defeat, FxShots.TokenPlaced);
                var screen = (BattleScreen)FxShots.BattleFlow.Stage.Current;
                Assert.IsTrue(GenAssets.Load<Texture2D>(DsStage.TokenKey) != null && GenAssets.Load<Texture2D>(DsStage.TokenBackKey) != null, "토큰 그림 앞면·뒷면(Gen stage 묶음)");
                seek(InitiativeBeats.PlaceAtMs - 100);
                Assert.IsNull(screen.Painted.Token, "놓이기 전에는 없다");
                Assert.IsNull(root.Q("battle-me-token"));
                seek(InitiativeBeats.PlaceAtMs + 50);
                var falling = screen.Painted.Token;
                Assert.IsNotNull(falling, "떨어지는 중");
                seek(InitiativeBeats.PlaceAtMs + InitiativeBeats.DropMs + 100);
                var placed = screen.Painted.Token;
                Assert.AreEqual((mine.x, mine.y), (placed.X, placed.Y), "내 체력 상자 옆");
                Assert.Less(falling.Y, placed.Y, "위에서 떨어졌다");
                Assert.AreEqual(DsStage.TokenSize, placed.Width);
                Assert.IsNotNull(root.Q("battle-me-token"), "기둥 내 줄에 토큰 그림");
                Assert.IsNull(root.Q("battle-foe-token"));
                foeMonY = FoeMonY(panel, root);
            }
            finally
            {
                FxShots.Release();
            }
            try
            {
                // 넘기기(54행): [사용]을 누른 뒤 선턴 단계의 앞 600 동안 돌며 상대 상자 옆으로 간다
                using var panel = new UiShotPanel(grade.Width, grade.Height, grade.PanelScale);
                var root = panel.Root;
                var seek = FxShots.RowScene(root, skin, grade, SoloTrail.Defeat, FxShots.TokenPassed);
                var screen = (BattleScreen)FxShots.BattleFlow.Stage.Current;
                seek(InitiativeBeats.PassMs / 2);
                var mid = screen.Painted.Token;
                Assert.IsNotNull(mid, "도는 중");
                Assert.That(mid.Y, Is.LessThan(Mathf.Max(mine.y, theirs.y)), "두 자리 사이(포물선)");
                bool narrow = false;
                for (int ms = InitiativeBeats.PassMs / 2 + 20; ms < InitiativeBeats.PassMs; ms += 20)
                {
                    seek(ms);
                    narrow |= screen.Painted.Token != null && screen.Painted.Token.Width < DsStage.TokenSize;
                }
                Assert.IsTrue(narrow, "동전처럼 돈다");
                seek(InitiativeBeats.PassMs + 50);
                var landed = screen.Painted.Token;
                Assert.AreEqual((theirs.x, theirs.y), (landed.X, landed.Y), "상대 체력 상자 옆");
                Assert.IsNotNull(root.Q("battle-foe-token"), "기둥 상대 줄로 옮겨 간다");
                Assert.IsNull(root.Q("battle-me-token"));
                Assert.AreEqual(foeMonY, FoeMonY(panel, root), 0.5f, "토큰이 상대 머리 줄로 와도 아래 블록이 밀리지 않는다");
            }
            finally
            {
                FxShots.Release();
                UiSkin.Release(skin);
            }
        }

        [Test]
        public void M4_배틀연출캡처는한번에보내도나눠보내도같은그림이다()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var layoutLogs = new List<string>();
            Color32[] prev = null;
            // 체력 막대가 깎이는 중 · 착지 뒤(공격), 졸음으로 주사위가 넘어가는 중(찾아 둔 판 Coral — 내 공격 뒤 야생의 공격)
            foreach (var shot in new[] { ("battle-attack", FxShots.AttackDrainMs), ("battle-attack", FxShots.AttackLandedMs), ("battle-flip", FxShots.FlipShotsMs[2]) })
            {
                var jump = FxShots.Shoot(shot.Item1, shot.Item2, 0, layoutLogs);
                if (jump == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
                var stepped = FxShots.Shoot(shot.Item1, shot.Item2, FxShots.FrameMs, layoutLogs);
                int differing = 0;
                var colors = new HashSet<Color32>();
                for (int i = 0; i < jump.Length; i++)
                {
                    if (!jump[i].Equals(stepped[i])) differing++;
                    colors.Add(jump[i]);
                }
                Assert.AreEqual(0, differing, shot.Item1 + " " + shot.Item2 + "ms: 한 번에 보낸 것과 나눠 보낸 것이 다른 픽셀");
                Assert.Greater(colors.Count, 64, shot.Item1 + " " + shot.Item2 + "ms: 배틀 화면이 그려졌다(DS 무대 · 굴림판 · 기둥)");
                if (prev != null && prev.Length == jump.Length)
                {
                    int moved = 0;
                    for (int i = 0; i < jump.Length; i++) if (!jump[i].Equals(prev[i])) moved++;
                    Assert.Greater(moved, 0, shot.Item1 + " " + shot.Item2 + "ms: 앞 그림과 같다");
                }
                prev = jump;
            }
            CollectionAssert.IsEmpty(layoutLogs, "Layout update failed to stabilize");
        }

        [Test]
        public void M4_포획연출은성공과실패셋이결정54의시각에판정나고끝난다()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            // 결정 54 ①(목업 r5-fx) 시각을 결정 126 ③(던지기 약 1.5배, plan §5 #624)으로 늘린 값 — 판정 · 전체는 PlayTiming 한 표(성공 3890 / 4790)
            Assert.AreEqual((3890, 4790), (PlayTiming.CaptureVerdictMs(3), PlayTiming.CaptureMs(true, 3)), "성공 판정 / 전체");
            var cases = new[] { ("성공", true, 3), ("1 모자람", false, 2), ("2 모자람", false, 1), ("3 모자람", false, 0) }
                .Select(x => (x.Item1, x.Item2, x.Item3, PlayTiming.CaptureVerdictMs(x.Item3), PlayTiming.CaptureMs(x.Item2, x.Item3))).ToArray();
            foreach (var (name, ok, shakes, verdict, total) in cases)
            {
                try
                {
                    var root = new VisualElement();
                    var seek = FxShots.CaptureScene(root, skin, FxShots.DiceGrade, shakes);   // 찾아 둔 판: 야생 배틀의 내 차례에 첫 볼을 던졌다
                    var flow = FxShots.BattleFlow;
                    var screen = (BattleScreen)flow.Stage.Current;
                    var seq = flow.Fx.Sequencer;
                    seek(1);
                    var entry = seq.Current;
                    Assert.IsNotNull(entry, name + ": 포획 항목이 재생 중");
                    var step = CaptureStage.StepOf(entry);
                    Assert.IsNotNull(step, name + ": 포획 단계(68행)");
                    var c = (CaptureAttempt)step.Events[0];
                    Assert.AreEqual((ok, shakes), (c.Success, c.Shakes), name + ": 찾아 둔 판의 첫 볼");
                    Assert.AreEqual(0, step.StartMs);
                    Assert.AreEqual(total, step.Ms, name + " 전체");
                    foreach (var s in entry.Steps) Assert.IsTrue(GameFlow.Built(s), "대기열에 남은 단계 " + s.Row + "는 부품이 있는 행이다");

                    // 던짐과 굴림이 함께 시작한다: 뒷모습이 들어오고(왼쪽 밖 → 180에 제자리) 독은 포획 굴림판(몸 색 #E0E4E8)
                    Assert.IsTrue(screen.RollIsCapture, name + ": 포획 굴림판");
                    Assert.IsTrue(screen.RollBoardOn, name + ": 녹화본 재생");
                    Assert.IsNotNull(root.Q("battle-roll"));
                    Assert.IsTrue(screen.Capture.Trainer && screen.Capture.TrainerDx < 0, name + ": 뒷모습이 들어오는 중");
                    Assert.IsFalse(screen.Capture.Ball, "볼은 아직 손에");
                    Assert.IsFalse(screen.Ready, "연출 중에는 입력이 닫힌다");
                    int stock = flow.Client.View.Seats[0].Balls[c.BallKey];
                    Assert.AreEqual("×" + (stock + 1), root.Q<Label>("battle-roll-stock").text, name + ": 던지기 전의 볼 재고(표시 값)");
                    seek(EnterBeats.ReleaseMs);
                    Assert.IsTrue(screen.Capture.Ball, name + ": 260에 볼이 손을 떠난다");
                    Assert.AreEqual(0, screen.Capture.TrainerDx);
                    Assert.AreEqual("×" + stock, root.Q<Label>("battle-roll-stock").text, name + ": 던지는 순간 줄어든다");
                    Assert.That(screen.Message, Does.Contain(flow.Stage.T(BattleFeed.BallKey(c.BallKey))), name + ": 「○○볼을 던졌다!」");
                    seek(EnterBeats.FlyEndMs);
                    Assert.AreEqual(-1, screen.Capture.BallFrame, name + ": 600에 볼이 열린다");
                    seek(EnterBeats.SuckEndMs);
                    Assert.IsFalse(screen.Capture.Mon, name + ": 1000에 포켓몬이 볼 속에");
                    Assert.IsTrue(screen.ShownMon(1 - flow.Battle.MySide).Exists, "체력 상자는 남아 있다");

                    // 세기: 굴림이 멈춘 뒤 판정에서 거꾸로 맞춘 간격(130~250)으로, 합계는 판정 100ms 전
                    Assert.AreEqual(verdict, screen.CaptureVerdictAtMs, name + " 판정 시각");
                    Assert.AreEqual(verdict - 100, screen.CaptureSumAtMs, name + " 합계 시각");
                    Assert.That(screen.CaptureCountEveryMs, Is.InRange(EnterBeats.CountMinMs, EnterBeats.CountMaxMs), name + " 세기 간격");
                    seek(verdict - 101);
                    Assert.AreEqual(c.Faces.Length - 1, screen.RollFaces.Count, name + ": 마지막 주사위는 아직");
                    Assert.IsFalse(screen.CaptureSumLit);
                    seek(verdict - 100);
                    Assert.AreEqual(c.Faces.Length, screen.RollFaces.Count);
                    CollectionAssert.AreEqual(CaptureStage.FinalFaces(c), screen.RollFaces, name + ": 결과 줄 = 엔진의 면");
                    Assert.AreEqual(c.Sum, screen.RollSum, name + ": 합계 = 엔진의 합");
                    Assert.IsTrue(screen.CaptureSumLit, name + ": 계산 줄의 합 칸");
                    Assert.IsFalse(screen.CaptureVerdictLit, name + ": 판정 전");
                    int before = screen.ShownLineCount;
                    seek(verdict - 1);
                    Assert.IsFalse(screen.CaptureVerdictLit);
                    Assert.IsFalse(screen.Capture.Mon, name + ": 판정 전에는 볼 속");
                    seek(verdict);
                    Assert.IsTrue(screen.CaptureVerdictLit, name + ": 판정(성공·실패 칸)");
                    Assert.AreEqual(ok ? "성공" : "실패", root.Q("battle-calc-capture").Query<Label>().ToList()[1].text);
                    Assert.AreEqual(ok, screen.Capture.BallDark, name + ": 성공 = 딸깍(볼이 어두워진다)");
                    Assert.AreEqual(ok ? -99 : -1, ok ? -99 : screen.Capture.BallFrame, name + ": 실패 = 볼이 열린다");
                    Assert.Greater(screen.ShownLineCount, before, name + ": 기록 줄은 판정 때 붙는다");
                    seek(CaptureStage.LineMs(c));
                    Assert.That(screen.Message, Does.StartWith(ok ? "신난다!" : shakes == 2 ? "아쉽다!" : shakes == 1 ? "아아!" : "안 돼!"), name + ": 결말 대사");

                    // 끝(전체 길이): 성공 = 뒷모습과 닫힌 볼이 남고 잡힌 포켓몬은 없다 · 실패 = 뒷모습이 물러나고 포켓몬이 서 있다
                    seek(total - 1);
                    Assert.AreSame(entry, seq.Current);
                    Assert.AreEqual(total - 1, seq.LocalMs);
                    if (ok)
                    {
                        seek(total);
                        Assert.IsTrue(screen.Capture.Trainer && screen.Capture.TrainerDx == 0 && screen.Capture.Ball, name + ": 성공하면 남는다");
                        Assert.IsFalse(screen.ResultShown, "결과 창은 잡은 뒤(69행) 다음 — 결과 단계에서");
                        var end = entry.Step(700);
                        seek(end.StartMs);
                        Assert.IsTrue(screen.ResultShown, name + ": 결과 창");
                        Assert.IsFalse(screen.ShowsResult, "[확인]은 아직");
                        Assert.AreEqual(0, screen.ResultLanded);
                        Assert.AreEqual(2, screen.ResultLines, "여행 점수 · 규토리");
                        seek(end.StartMs + EnterBeats.ResultLeadMs);
                        Assert.AreEqual(1, screen.ResultLanded, "첫 줄(여행 점수)");
                        seek(end.StartMs + EnterBeats.ResultLeadMs + EnterBeats.ResultEveryMs);
                        Assert.AreEqual(2, screen.ResultLanded, "둘째 줄(규토리)");
                        Assert.AreEqual(Visibility.Hidden, root.Q("battle-result-foot").style.visibility.value);
                        seek(entry.LengthMs);
                        Assert.IsTrue(screen.ShowsResult, name + ": 그 항목이 다 재생되면 [확인]");
                        Assert.AreEqual(Visibility.Visible, root.Q("battle-result-foot").style.visibility.value);
                        Assert.IsTrue(seq.Paused, "턴 넘김은 [확인]을 기다린다");
                        Assert.IsTrue(screen.Capture.Trainer && screen.Capture.Ball && screen.Capture.BallDark, "결과 창 뒤의 DS에도 뒷모습과 볼이 남아 있다");
                    }
                    else
                    {
                        Assert.Less(screen.Capture.TrainerDx, 0, name + ": 물러나는 중");
                        seek(total);
                        Assert.IsFalse(screen.Capture.Trainer || screen.Capture.Ball, name + ": 실패하면 물러난다");
                        Assert.IsFalse(screen.ResultShown);
                        Assert.IsFalse(flow.Battle.Ended, "배틀은 이어진다");
                    }
                }
                finally
                {
                    FxShots.Release();
                }
            }
            UiSkin.Release(skin);
        }

        // 포획 단계 안 시각 t의 단계 이름(CaptureStage.Pose와 같은 경계): 들어옴 · 쥠 · 날아감 · 빨려 듦 · 튐 · 쉼 · 흔들림 · 합계(판정 100 전부터) · 판정 · 대사 · 끝 뒤
        static string CapturePhase(CaptureAttempt c, int t, int sumAt, int total)
        {
            int verdict = CaptureStage.VerdictMs(c);
            if (t >= total) return "after";
            if (t >= CaptureStage.LineMs(c)) return "line";
            if (t >= verdict) return "verdict";
            if (t >= sumAt) return "sum";
            if (c.Shakes > 0 && t >= CaptureStage.ShakeStartMs(c)) return "shake";
            if (t >= EnterBeats.BounceEndMs) return "rest";
            if (t >= EnterBeats.SuckEndMs) return "bounce";
            if (t >= EnterBeats.FlyEndMs) return "suck";
            if (t >= EnterBeats.ReleaseMs) return "fly";
            return t < EnterBeats.TrainerInMs ? "in" : "hold";
        }

        [Test]
        public void M10_포획캡처시각은지금박자의의도한단계에떨어진다_결정126()
        {
            // M10 감사 ②: 던지기 1.5배(결정 126 ③)로 옛 시각 둘이 같은 그림(쉼 · 세기 전)에 떨어졌다 — 찍는 시각은 박자에서 세고, 여기서 그 단계를 지킨다
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var cases = new[]
            {
                ("capture-success", 3, FxShots.CatchShotsMs, "in fly fly suck suck bounce bounce shake shake sum verdict line line after after after after after", 8),
                ("capture-fail-2", 2, FxShots.Miss2ShotsMs, "fly suck bounce shake sum verdict verdict line line after", -1),
                ("capture-fail-1", 1, FxShots.Miss1ShotsMs, "fly bounce rest shake sum verdict verdict line line after", -1),
                ("capture-fail-0", 0, FxShots.Miss0ShotsMs, "fly bounce bounce rest sum verdict verdict line line after", -1),
            };
            try
            {
                foreach (var (name, shakes, shots, phases, counting) in cases)
                {
                    try
                    {
                        var root = new VisualElement();
                        var seek = FxShots.CaptureScene(root, skin, FxShots.DiceGrade, shakes);
                        var flow = FxShots.BattleFlow;
                        var screen = (BattleScreen)flow.Stage.Current;
                        seek(1);
                        var step = CaptureStage.StepOf(flow.Fx.Sequencer.Current);
                        var c = (CaptureAttempt)step.Events[0];
                        int n = c.Faces.Length, total = step.Ms, sumAt = screen.CaptureSumAtMs - step.StartMs, every = screen.CaptureCountEveryMs, first = sumAt - (n - 1) * every;
                        var roll = step.Tag as DiceRollScene;
                        Assert.IsNotNull(roll, name + ": 포획 굴림 녹화본");
                        int rollMs = roll.ThrowMs - step.StartMs + roll.Clip.LengthMs;
                        // 결정 126 전(판정 = max(2350, 1650 + 470 × 흔들림))의 세기 시작과 견준다 — 같은 녹화본 · 같은 주사위 수
                        int oldVerdict = System.Math.Max(2350, 1650 + 470 * shakes), oldEvery = CaptureStage.CountEveryMs(oldVerdict, rollMs, n), oldFirst = CaptureStage.CountAtMs(oldVerdict, oldEvery, n, 0);
                        TestContext.WriteLine($"{name}: 주사위 {n} · 굴림 멈춤 {rollMs} · 판정 {CaptureStage.VerdictMs(c)}(옛 {oldVerdict}) · 세기 간격 {every}(옛 {oldEvery}) · 세기 시작 {first}(옛 {oldFirst}, +{first - oldFirst}) · 합계 {sumAt} · 끝 {total}");
                        Assert.AreEqual(CaptureStage.CountAtMs(CaptureStage.VerdictMs(c), every, n, 0), first, name + ": 세기 시작 = 판정에서 거꾸로");
                        Assert.GreaterOrEqual(first, rollMs + EnterBeats.RollGapMs, name + ": 굴림이 멈춘 뒤에 센다");

                        var want = phases.Split(' ');
                        Assert.AreEqual(want.Length, shots.Length, name + ": 시각 수");
                        for (int i = 0; i < shots.Length; i++)
                        {
                            if (i > 0) Assert.Greater(shots[i], shots[i - 1], name + ": 시각은 오름차순");
                            Assert.AreEqual(want[i], CapturePhase(c, shots[i], sumAt, total), name + " " + shots[i] + "ms의 단계");
                        }
                        if (counting >= 0)
                        {
                            seek(shots[counting]);
                            Assert.That(screen.RollFaces.Count, Is.InRange(1, n - 1), name + " " + shots[counting] + "ms: 주사위를 세는 중");
                        }
                    }
                    finally
                    {
                        FxShots.Release();
                    }
                }

                // 갤럭시단 등장(72행 — 결정 143 ⑤): 들어옴 · 섬 · 명단 볼이 떨어지는 중 · 끝까지 서 있음(물러나지 않는다)
                var groot = new VisualElement();
                var gseek = FxShots.RowScene(groot, skin, FxShots.DiceGrade, SoloTrail.Defeat, st => st.Row == EnterRows.Galactic);
                var gflow = FxShots.BattleFlow;
                var gscreen = (BattleScreen)gflow.Stage.Current;
                gseek(1);
                var gstep = gflow.Fx.Sequencer.Current.Step(EnterRows.Galactic);
                int lineup = LineupBeats.Lineup(gstep) != null ? LineupBeats.Lineup(gstep).LineupCount : 0;
                Assert.Greater(lineup, 0, "Defeat 판 조무래기의 명단 볼");
                var g = FxShots.GalacticShotsMs;
                Assert.Less(g[0], EnterBeats.FoeInMs, "들어오는 중");
                Assert.That(g[1], Is.InRange(EnterBeats.FoeInMs, LineupBeats.BallAt(0) - 1), "다 들어와 섬(볼 전)");
                Assert.That(g[2], Is.InRange(LineupBeats.BallAt(0), LineupBeats.BallAt(0) + LineupBeats.DropMs - 1), "첫 볼이 떨어지는 중");
                Assert.That(g[3], Is.InRange(LineupBeats.BallAt(lineup - 1) + LineupBeats.DropMs, gstep.Ms - 1), "명단이 다 놓인 뒤 · 단계 끝 전");
                gseek(g[3]);
                Assert.IsTrue(gscreen.FoeTrainerShown, "단계 끝까지 조무래기가 서 있다(결정 143 ⑤)");
                Assert.AreEqual(lineup, gscreen.LineupShown, "명단 볼이 다 놓였다");
            }
            finally
            {
                FxShots.Release();
                UiSkin.Release(skin);
            }

            // 제작 · 스튜(36 · 37행 — 플레이 시험 T11, 결단의 머리띠 = 볼 아님 → PlayTiming.CraftMs)
            var m = FxShots.CraftShotsMs;
            Assert.Less(m[0], CraftBeats.GlowMs, "빛이 차오름");
            Assert.That(m[1], Is.InRange(CraftBeats.SuckFromMs, CraftBeats.SuckFromMs + CraftBeats.SuckMs - 1), "첫 재료가 빨려 듦");
            Assert.That(m[2], Is.InRange(CraftBeats.SuckFromMs + CraftBeats.SuckEveryMs * 5, CraftBeats.FlashAt - 1), "여섯째 재료가 빨려 듦");
            Assert.That(m[3], Is.InRange(CraftBeats.FlashAt, CraftBeats.FlashAt + CraftBeats.FlashMs - 1), "펑");
            Assert.That(m[4], Is.InRange(CraftBeats.FlashAt + CraftBeats.PopMs / 3, CraftBeats.FlashAt + CraftBeats.PopMs - 1), "커졌다 돌아오는 중");
            Assert.That(m[5], Is.InRange(CraftBeats.FlashAt + CraftBeats.FlashMs, CraftBeats.FlashAt + CraftBeats.PlusMs - 1), "「+1」이 떠오르고 빛이 걷히는 중");
            Assert.That(m[6], Is.GreaterThanOrEqualTo(PlayTiming.CraftMs), "제작 단계 끝 뒤(개수가 바뀜)");
            var w = FxShots.StewShotsMs.Select(x => x - PlayTiming.CraftMs).ToArray();
            Assert.That(w[0], Is.InRange(0, StewBeats.FirstMs - 1), "스튜 첫 튐");
            Assert.That(w[1], Is.InRange(StewBeats.FirstMs, StewBeats.FirstMs + StewBeats.SecondMs - 1), "스튜 둘째 튐");
            Assert.That(w[2], Is.InRange(StewBeats.BadgeAt, StewBeats.BadgeAt + StewBeats.BadgeMs - 1), "꼬리표가 뜨는 중");
            Assert.That(w[3], Is.InRange(StewBeats.BadgeAt + StewBeats.BadgeMs, 1500 - 1), "다 뜬 꼬리표 · 스튜 단계(1500) 안");
        }

        [Test]
        public void M65_배틀시작에주인공뒷모습이볼을던져첫포켓몬을내보낸다_결정99()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var table = GenAssets.Load<TextAsset>(BoardPieces.TableKey);
            Assert.IsNotNull(table, "Gen Trainers/trainers.json");
            try
            {
                // 야생 배틀을 연 항목의 인트로(53행 — 야생 +300 · 내 포켓몬 +1750): 인트로 단계 시작이 0
                var seek = FxShots.RowScene(new VisualElement(), skin, FxShots.DiceGrade, SoloTrail.Catch, st => st.Row == FxRows.Intro);
                var flow = FxShots.BattleFlow;
                var screen = (BattleScreen)flow.Stage.Current;
                string who = flow.Protagonist(flow.Client.Seat);
                var back = BackThrow.Find(table.text, who);
                Assert.IsNotNull(back, who + ": 던지기 행");
                int at = EnterBeats.IntroSecondMs;
                seek(EnterBeats.IntroLeadMs - 50);
                Assert.IsNull(screen.Painted.Back, "뒷모습은 상대와 같은 때(+300) 들어온다 — 그 앞에는 없다");
                seek(at - IntroStage.ThrowLeadMs - 100);
                Assert.IsNotNull(screen.Painted.Back, "던지기 전: 뒷모습이 서 있다");
                Assert.AreEqual("Trainers/" + back.File, screen.Painted.Back.Key, "내 주인공의 뒷모습 시트");
                back.Cell(back.Still, out int stillX, out int stillY);
                Assert.AreEqual((stillX, stillY), (screen.Painted.Back.Sx, screen.Painted.Back.Sy), "서 있는 프레임");
                Assert.AreEqual(DsStage.TrainerAt.x - back.OriginX, screen.Painted.Back.X, "제자리(포획 때와 같은 자리)");
                Assert.IsNull(screen.Painted.Ball, "아직 볼이 없다");
                Assert.IsNull(screen.Painted.Player, "내 포켓몬은 아직 볼 속");
                seek(at - EnterBeats.IntroFlyMs);
                Assert.IsNotNull(screen.Painted.Ball, "볼이 손을 떠났다");
                back.HandAt(DsStage.TrainerAt.x, DsStage.TrainerAt.y, out int hx, out int hy);
                Assert.AreEqual((hx, hy), (screen.Intro.BallX, screen.Intro.BallY), "볼 출발점 = 그 주인공의 손(결정 61 ①)");
                back.Cell(back.FrameAtTick(back.Leave), out int leaveX, out int leaveY);
                Assert.AreEqual((leaveX, leaveY), (screen.Painted.Back.Sx, screen.Painted.Back.Sy), "볼이 나타나는 순간 뒷모습은 손 자리를 잰 프레임(결정 62)");
                seek(at + 20);
                Assert.IsNotNull(screen.Painted.Player, "볼이 열리며 내 포켓몬이 나온다");
                Assert.Less(screen.Painted.Player.Scale, 0.5f, "작게 나와 커진다");
                Assert.Greater(screen.Painted.Player.White, 0.5f, "하얀 채");
                Assert.AreEqual(0, screen.Painted.Player.Dx, "옆에서 미끄러져 들어오지 않는다");
                StringAssert.EndsWith("_open.png", screen.Painted.Ball.Key, "열린 볼");
                seek(at + EnterBeats.IntroPopMs + 150);
                Assert.IsNull(screen.Painted.Back, "던진 뒤 뒷모습은 물러났다");
                Assert.IsNull(screen.Painted.Ball);
                Assert.AreEqual(1f, screen.Painted.Player.Scale);
                Assert.AreEqual(0f, screen.Painted.Player.White);
            }
            finally
            {
                FxShots.Release();
            }
            UiSkin.Release(skin);
        }

        [Test]
        public void M4_배틀화면의볼은그주인공의손자리에서나온다_결정61()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var table = GenAssets.Load<TextAsset>(BoardPieces.TableKey);
            Assert.IsNotNull(table, "Gen Trainers/trainers.json");
            // 손을 떠나는 260ms의 볼 자리(DS px) = 뒷모습 고정점 − origin + 표의 hand. 4세대는 뻗은 손 (53, 97 — 결정 62에서 다시 잼), 5세대는 머리 위로 든 손(plan §4 결정 61 ①의 표)
            var cases = new[] { ("pt_player_male", 53, 97), ("bw_000_hilbert", 30, 28), ("b2w2_001_rosa", 41, 40) };
            foreach (var (who, x, y) in cases)
            {
                try
                {
                    var root = new VisualElement();
                    var seek = FxShots.CaptureScene(root, skin, FxShots.DiceGrade, 3, who);
                    var flow = FxShots.BattleFlow;
                    var screen = (BattleScreen)flow.Stage.Current;
                    Assert.AreEqual(who, flow.Protagonist(flow.Client.Seat), "고른 주인공");
                    seek(EnterBeats.ReleaseMs - 1);
                    Assert.IsFalse(screen.Capture.Ball, who + ": 볼은 아직 손에");
                    seek(EnterBeats.ReleaseMs);
                    Assert.IsTrue(screen.Capture.Ball, who + ": 260에 볼이 손을 떠난다");
                    var back = BackThrow.Find(table.text, who);
                    Assert.IsNotNull(back, who + ": 던지기 행");
                    back.HandAt(DsStage.TrainerAt.x, DsStage.TrainerAt.y, out int hx, out int hy);
                    Assert.AreEqual((hx, hy), (screen.Capture.BallX, screen.Capture.BallY), who + ": 화면의 볼 출발점 = BackThrow.HandAt(DsStage.TrainerAt)");
                    Assert.AreEqual((x, y), (screen.Capture.BallX, screen.Capture.BallY), who + ": 볼 출발점(DS px)");
                }
                finally
                {
                    FxShots.Release();
                }
            }
            UiSkin.Release(skin);
        }

        [Test]
        public void M4_배틀화면은뒷모습이나와있는동안체력상자를찍지않는다_결정62()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var table = GenAssets.Load<TextAsset>(BoardPieces.TableKey);
            Assert.IsNotNull(table, "Gen Trainers/trainers.json");
            // (흔들림 수, 주인공): 성공 = 5세대(든 손이 상대 체력 상자 자리) · 실패 = 4세대
            foreach (var (shakes, who) in new[] { (3, "bw_000_hilbert"), (2, "pt_player_male") })
            {
                try
                {
                    var root = new VisualElement();
                    var seek = FxShots.CaptureScene(root, skin, FxShots.DiceGrade, shakes, who);
                    var flow = FxShots.BattleFlow;
                    var screen = (BattleScreen)flow.Stage.Current;
                    seek(1);
                    var entry = flow.Fx.Sequencer.Current;
                    int total = CaptureStage.StepOf(entry).Ms;
                    var back = BackThrow.Find(table.text, who);
                    Assert.IsNotNull(back, who);
                    // 들어오는 중: 두 상자가 바깥쪽으로 물러난다(상대 것 왼쪽 · 내 것 오른쪽)
                    seek(EnterBeats.TrainerInMs / 2);
                    Assert.IsTrue(screen.Painted.Enemy.BoxShown && screen.Painted.Player.BoxShown, who + ": 물러나는 중");
                    Assert.Less(screen.Painted.Enemy.BoxDx, 0, who + ": 상대 체력 상자는 왼쪽으로");
                    Assert.Greater(screen.Painted.Player.BoxDx, 0, who + ": 내 체력 상자는 오른쪽으로");
                    // 볼이 나타나는 260ms: 체력 상자는 없고, 뒷모습은 손 자리를 잰 프레임(5세대 = 든 손에 볼이 있는 마지막 프레임)이며 내 포켓몬은 그대로 서 있다
                    seek(EnterBeats.ReleaseMs);
                    Assert.IsTrue(screen.Capture.Ball && screen.Capture.BoxHidden, who);
                    Assert.IsFalse(screen.Painted.Enemy.BoxShown || screen.Painted.Player.BoxShown, who + ": 뒷모습이 서 있는 동안 체력 상자 둘 다 없다");
                    Assert.IsTrue(screen.Painted.Player.Standing, who + ": 내 포켓몬은 숨기지 않는다");
                    back.Cell(back.FrameAtTick(back.Leave), out int cx, out int cy);
                    Assert.AreEqual((cx, cy), (screen.Painted.Back.Sx, screen.Painted.Back.Sy), who + ": 260ms의 뒷모습 프레임 = leave 틱의 프레임");
                    Assert.AreEqual(DsStage.TrainerAt.x - back.OriginX, screen.Painted.Back.X, who + ": 뒷모습 자리는 그대로(결정 55 ①)");
                    seek(total - EnterBeats.LeaveMs - 1);
                    Assert.IsFalse(screen.Painted.Player.BoxShown, who + ": 판정 뒤에도");
                    if (shakes < 3)
                    {
                        // 실패: 뒷모습이 물러나는 동안 돌아와, 다 물러나면 제자리
                        seek(total - EnterBeats.LeaveMs / 2);
                        Assert.IsTrue(screen.Painted.Enemy.BoxShown && screen.Painted.Player.BoxShown, who + ": 돌아오는 중");
                        Assert.Less(screen.Painted.Enemy.BoxDx, 0);
                        seek(total);
                        Assert.IsNull(screen.Painted.Back, who + ": 뒷모습이 물러났다");
                        Assert.IsTrue(screen.Painted.Enemy.BoxShown && screen.Painted.Player.BoxShown, who + ": 체력 상자가 돌아왔다");
                        Assert.AreEqual((0, 0), (screen.Painted.Enemy.BoxDx, screen.Painted.Player.BoxDx), who + ": 제자리");
                    }
                    else
                    {
                        // 성공: 숨긴 채 결과 창으로 — [확인]이 뜰 때까지 뒷모습이 남고 체력 상자는 없다
                        seek(entry.Step(700).StartMs);
                        Assert.IsTrue(screen.ResultShown);
                        Assert.IsNotNull(screen.Painted.Back, who + ": 뒷모습이 남는다");
                        Assert.IsFalse(screen.Painted.Player.BoxShown, who + ": 결과 창 동안에도 체력 상자는 없다");
                        seek(entry.LengthMs);
                        Assert.IsTrue(screen.ShowsResult);
                        Assert.IsNotNull(screen.Painted.Back);
                        Assert.IsFalse(screen.Painted.Player.BoxShown, who + ": [확인] 때에도");
                        Assert.IsTrue(screen.Painted.Enemy == null || !screen.Painted.Enemy.BoxShown, who + ": 잡힌 포켓몬의 상자도 없다");
                    }
                }
                finally
                {
                    FxShots.Release();
                }
            }
            UiSkin.Release(skin);
        }

        // 그 시트가 고정점 (x, y)에 찍혔나(프레임은 보지 않는다)
        static DsSprite FxAt(BattleScreen screen, string key, int x, int y)
        {
            var sheet = SheetArt.Of(key);
            Assert.IsNotNull(sheet, "Gen Stage/Fx 표에 " + key);
            if (screen.Painted == null || screen.Painted.Fx == null) return null;
            return screen.Painted.Fx.Find(s => s.Key == SheetArt.TextureKey(sheet) && s.X == x - sheet.Ox && s.Y == y - sheet.Oy);
        }

        static int FxCount(BattleScreen screen) => screen.Painted != null && screen.Painted.Fx != null ? screen.Painted.Fx.Count : 0;

        [Test]
        public void M4_배틀화면은도트이펙트를그포켓몬의자리와때에찍는다_결정69()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            if (SheetArt.Of(SheetBeats.Dust) == null) Assert.Ignore("M4 Gen Stage/Fx 없음: rebuild-all 먼저");
            Vector2Int foe = DsStage.EnemyFoot, me = DsStage.PlayerFoot;
            try
            {
                // 기술 맞음(61행): 내 기술에 맞은 상대 — 체력이 깎이기 시작할 때 상대 몸 가운데에 기술 타입 색
                var seek = FxShots.BattleAttack(new VisualElement(), skin, FxShots.DiceGrade);
                var screen = (BattleScreen)FxShots.BattleFlow.Stage.Current;
                seek(1);
                var entry = FxShots.BattleFlow.Fx.Sequencer.Current;
                var drain = entry.Step(612);
                Assert.IsNotNull(drain);
                int target = -1;
                var type = PokemonType.Typeless;
                foreach (var e in entry.Events) if (e is MoveUsed m) { target = m.TargetId; type = m.Type; }
                seek(drain.StartMs - 20);
                Assert.AreEqual(0, FxCount(screen), "체력이 깎이기 전에는 불꽃이 없다");
                seek(drain.StartMs + 20);
                Assert.IsTrue(SheetBeats.MoveType(entry, target, out _));
                Assert.IsNotNull(FxAt(screen, SheetBeats.Hit(type, true), foe.x, foe.y - SheetBeats.BodyAbovePx), "맞는 불꽃: 상대 발판 위 몸 가운데");
                Assert.AreEqual(1, FxCount(screen), "맞은 쪽에만");
            }
            finally
            {
                FxShots.Release();
            }
            try
            {
                // 인트로(53행): 배틀 시작에는 볼 열림 빛이 없다(결정 70 ⑬ ③ — 내 포켓몬이 드러나는 1750 앞뒤)
                var seek = FxShots.RowScene(new VisualElement(), skin, FxShots.DiceGrade, SoloTrail.Catch, st => st.Row == FxRows.Intro);
                var screen = (BattleScreen)FxShots.BattleFlow.Stage.Current;
                foreach (int ms in new[] { 1800, 1910, 1960, 2110 })
                {
                    seek(ms);
                    Assert.AreEqual(0, FxCount(screen), "인트로 " + ms + "ms");
                }
            }
            finally
            {
                FxShots.Release();
            }
            try
            {
                // 포획(68행): 볼이 열릴 때 상대 머리 위의 빛(단계 안 시각으로 프레임을 고른다) → 성공 판정에는 pixelfx 반짝임이 없다(결정 70 ⑬ ④)
                var seek = FxShots.CaptureScene(new VisualElement(), skin, FxShots.DiceGrade, 3);
                var screen = (BattleScreen)FxShots.BattleFlow.Stage.Current;
                seek(EnterBeats.FlyEndMs - 10);
                Assert.AreEqual(0, FxCount(screen), "볼이 열리기 전");
                var open = SheetArt.Of(SheetBeats.Open);
                int at = EnterBeats.FlyEndMs + open.Ms + 20;
                seek(at);
                var light = FxAt(screen, SheetBeats.Open, foe.x, foe.y - EnterBeats.OpenAbovePx);
                Assert.IsNotNull(light, "볼 열림 빛: 볼이 열린 자리");
                Assert.AreEqual(open.FrameAt(at - EnterBeats.FlyEndMs) * open.Fw, light.Sx, "프레임 = 포획 단계 안 시각");
                seek(CaptureStage.VerdictMs(FxShots.BattleFlow.Battle.LastCapture) + 20);
                Assert.AreEqual(0, FxCount(screen), "성공 판정: 이펙트 시트 없음(원래 별 셋만)");
            }
            finally
            {
                FxShots.Release();
            }
            var dust = SheetArt.Of(SheetBeats.Dust);
            int rise = BattleBeats.FaintSinkMs - SheetBeats.FaintDustLeadMs + dust.Ms + 20;
            try
            {
                // 기절(65행) — 상대: 발판 고정점의 먼지
                var seek = FxShots.RowScene(new VisualElement(), skin, FxShots.DiceGrade, SoloTrail.Defeat, st => st.Row == 650);
                var screen = (BattleScreen)FxShots.BattleFlow.Stage.Current;
                seek(rise);
                Assert.IsNotNull(FxAt(screen, SheetBeats.Dust, foe.x, foe.y), "상대 기절 먼지: 발판 고정점");
            }
            finally
            {
                FxShots.Release();
            }
            try
            {
                // 기절 — 내 포켓몬: 발 점이 메시지 창 윗줄 아래라 창 바로 위로 올려 찍는다(창이 덮지 않는다)
                var seek = FxShots.RowScene(new VisualElement(), skin, FxShots.DiceGrade, SoloTrail.Coral, st => st.Row == 650);
                var screen = (BattleScreen)FxShots.BattleFlow.Stage.Current;
                seek(rise);
                Assert.IsNull(FxAt(screen, SheetBeats.Dust, foe.x, foe.y), "쓰러진 것은 내 포켓몬");
                Assert.AreEqual(1, FxCount(screen));
                var puff = screen.Painted.Fx[0];
                Assert.AreEqual(SheetArt.TextureKey(dust), puff.Key);
                Assert.AreEqual(me.x - dust.Ox, puff.X);
                Assert.AreEqual(DsStage.MessageRect.y, puff.Y + puff.Sh, "내 기절 먼지의 아랫줄 = 메시지 창 바로 위");
            }
            finally
            {
                FxShots.Release();
            }
            UiSkin.Release(skin);
        }

        [Test]
        public void M4_들어가고나오는연출캡처는한번에보내도나눠보내도같은그림이다()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var layoutLogs = new List<string>();
            Color32[] prev = null;
            // 볼이 날아가는 중 · 판정(별) · 실패해 튀어나오는 중 · 조우 카드가 뒤집히는 중 · 전환 줄무늬가 닫히는 중 · 야생이 미끄러져 들어오는 중
            foreach (var shot in new[] { ("capture-success", FxShots.CatchShotsMs[2]), ("capture-success", FxShots.CaptureVerdictMs(3) + 40), ("capture-fail-0", FxShots.CaptureVerdictMs(0) + 50), ("encounter-flip", 2430), ("battle-enter", 1500), ("battle-enter", 3200) })
            {
                var jump = FxShots.Shoot(shot.Item1, shot.Item2, 0, layoutLogs);
                if (jump == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
                var stepped = FxShots.Shoot(shot.Item1, shot.Item2, FxShots.FrameMs, layoutLogs);
                int differing = 0;
                var colors = new HashSet<Color32>();
                for (int i = 0; i < jump.Length; i++)
                {
                    if (!jump[i].Equals(stepped[i])) differing++;
                    colors.Add(jump[i]);
                }
                Assert.AreEqual(0, differing, shot.Item1 + " " + shot.Item2 + "ms: 한 번에 보낸 것과 나눠 보낸 것이 다른 픽셀");
                Assert.Greater(colors.Count, 32, shot.Item1 + " " + shot.Item2 + "ms: 화면이 그려졌다");
                if (prev != null && prev.Length == jump.Length)
                {
                    int moved = 0;
                    for (int i = 0; i < jump.Length; i++) if (!jump[i].Equals(prev[i])) moved++;
                    Assert.Greater(moved, 0, shot.Item1 + " " + shot.Item2 + "ms: 앞 그림과 같다");
                }
                prev = jump;
            }
            CollectionAssert.IsEmpty(layoutLogs, "Layout update failed to stabilize");
        }

        [Test]
        public void M4_굴림견본은면바꾸기와재굴림뒤엔진결과로멈춘다()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var variant = FxDiceSample.Find("dice-all");   // 일반·일반·심볼·일반 → 첫째가 심볼로 넘어가고(졸음) 셋째를 급소로 다시 굴림
            var sample = new FxDiceSample(new VisualElement(), skin, FxShots.DiceGrade, FxShots.LoadDiceLibrary(), variant);
            try
            {
                Assert.AreEqual(618, sample.Board.GridW * sample.Board.Dot, "1080 굴림판 618×186, 주사위 48px");
                Assert.AreEqual(186, sample.Board.GridH * sample.Board.Dot);
                Assert.AreEqual(48f, sample.Board.CellsPerUnit * sample.Board.Dot);
                Assert.AreEqual(400, sample.Scene.ThrowMs);
                Assert.AreEqual(AttackDieFace.None, sample.ShownFace(0), "던지기 전");
                sample.Seek(2100);   // 다 멈춤(위쪽 끝 2000)
                for (int k = 0; k < 4; k++) Assert.AreEqual(variant.Faces[k], sample.ShownFace(k), k + "번째");
                sample.Seek(2800);   // 넘김 2450~2730
                Assert.AreEqual(AttackDieFace.StatusSymbol, sample.ShownFace(0));
                Assert.AreEqual(2000 + 730 + 250, sample.Scene.CountMs, "세기는 면 바꾸기 한 박자 뒤");
                sample.Seek(sample.RerollAtMs + DiceRollScene.GlitchMs);
                Assert.AreEqual(AttackDieFace.CriticalHit, sample.ShownFace(2));
                Assert.AreEqual(AttackDieFace.RegularHit, sample.ShownFace(1));
                Assert.AreEqual(AttackDieFace.RegularHit, sample.ShownFace(3));

                // 굴림판 그림: 도트풍으로 줄인 RT에 주사위 색(노랑)·회색(넘어간 주사위)·외곽선 잉크색이 있고, 색 수가 제한돼 있다
                var rt = sample.Board.Output;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = active;
                var counts = new Dictionary<Color32, int>();
                foreach (var p in tex.GetPixels32()) counts[p] = counts.TryGetValue(p, out int c) ? c + 1 : 1;
                Object.DestroyImmediate(tex);
                Assert.AreEqual(309, rt.width);
                Assert.AreEqual(93, rt.height);
                Assert.That(counts.Count, Is.InRange(8, 24), "색 제한(주사위 팔레트 19 + 바닥 5 안)");
                Assert.Greater(counts.TryGetValue(FxDiceSample.Body, out int body) ? body : 0, 300, "주사위 몸 색");
                Assert.Greater(counts.TryGetValue(new Color32(0xB0, 0xB8, 0xC0, 0xFF), out int gray) ? gray : 0, 100, "넘어간 주사위는 회색");
                Assert.Greater(counts.TryGetValue(new Color32(0x28, 0x30, 0x38, 0xFF), out int ink) ? ink : 0, 200, "외곽선·글리프 잉크색");
            }
            finally
            {
                sample.Dispose();
                UiSkin.Release(skin);
            }
        }

        [Test]
        public void M4_굴림캡처는한번에보내도나눠보내도같은그림이다()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            var layoutLogs = new List<string>();
            foreach (int ms in new[] { 700, 4400 })   // 구르는 중 · 글리치 재굴림 중
            {
                var jump = FxShots.Shoot("dice-all", ms, 0, layoutLogs);
                if (jump == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
                var stepped = FxShots.Shoot("dice-all", ms, FxShots.FrameMs, layoutLogs);
                Assert.AreEqual(FxShots.DiceGrade.Width * FxShots.DiceGrade.Height, jump.Length);
                int differing = 0;
                for (int i = 0; i < jump.Length; i++) if (!jump[i].Equals(stepped[i])) differing++;
                Assert.AreEqual(0, differing, ms + "ms: 한 번에 보낸 것과 나눠 보낸 것이 다른 픽셀");
            }
            CollectionAssert.IsEmpty(layoutLogs, "Layout update failed to stabilize");
        }

        // ── M9 다듬기 2: 조우 카드 연출(21행 — 가정 #415~#417) ──

        static VisualElement Card(VisualElement root, int index) => root.Q("encounter-card-" + (index + 1));
        static float Ty(VisualElement e) => e.style.translate.value.y.value;
        static Vector3 Sc(VisualElement e) => e.style.scale.value.value;
        static float Op(VisualElement e) => e.style.opacity.value;

        [Test]
        public void M9_조우카드_강한카드는늦게떠올라쿵떨어지고착지하면장식이걷힌다()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M9 Gen 없음: rebuild-all 먼저");
            try
            {
                var root = new VisualElement();
                var seek = FxShots.FlowTrail(root, skin, FxShots.DiceGrade, SoloTrail.One, FxShots.SearchOn);   // 강한 나무에서 [찾아보기]를 눌렀다
                var flow = FxShots.BattleFlow;
                int reveal = flow.MsUntil(s => s.Row == EnterRows.Reveal);
                seek(reveal + 50);
                var screen = flow.Stage.Current as EncounterScreen;
                Assert.IsNotNull(screen, "공개 단계가 시작하면 조우 창");
                Assert.AreEqual(1, screen.Cards.Count, "강한 나무: 1장");
                Assert.IsTrue(screen.HeavyAt(0));
                Assert.AreEqual(0, screen.FaceUp, "강한 카드는 첫 뒤집기(300)보다 늦게 — 아직 뒷면");
                Assert.AreEqual(0f, Ty(Card(root, 0)));
                int start = EncounterCards.StartAt(new[] { true }, 0);
                seek(reveal + start + EnterBeats.HeavyRiseMs);
                Assert.Less(Ty(Card(root, 0)), -EncounterScreen.RisePx, "예비: 떠올랐다(1080 벌 = 1.5배)");
                int land = EncounterCards.LandAt(new[] { true }, 0);
                seek(reveal + land);
                Assert.AreEqual(0f, Ty(Card(root, 0)), "닿았다");
                Assert.Less(Sc(Card(root, 0)).y, 1f, "맞음: 바닥에서 눌린다");
                Assert.AreEqual(1, screen.FaceUp);
                seek(reveal + land + EnterBeats.HeavySquashMs + 10);
                Assert.AreEqual(1f, Sc(Card(root, 0)).y, 1e-4, "착지: 펴졌다");
                Assert.AreEqual(0f, Ty(Card(root, 0)));
            }
            finally
            {
                FxShots.Release();
                UiSkin.Release(skin);
            }
        }

        [Test]
        public void M9_조우카드_연출끄기면떠오름눌림들림없이보이는카드는제자리다()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M9 Gen 없음: rebuild-all 먼저");
            try
            {
                var root = new VisualElement();
                var seek = FxShots.FlowTrail(root, skin, FxShots.DiceGrade, SoloTrail.One, FxShots.SearchOn, speed: FxSpeed.Off);
                var flow = FxShots.BattleFlow;
                EncounterScreen screen = null;
                int ms = 0;
                // 끄기: 남은 대기열을 한 번에 보낸다 — 창이 뜬 때부터 강한 카드는 장식 없는 앞면
                for (; ms < 3000 && screen == null; ms += 16)
                {
                    seek(ms);
                    screen = flow.Stage.Current as EncounterScreen;
                }
                Assert.IsNotNull(screen, "조우 창");
                Assert.IsTrue(screen.HeavyAt(0));
                for (int end = ms + 1500; ms < end; ms += 16)
                {
                    seek(ms);
                    Assert.AreEqual(1, screen.FaceUp, ms + "ms: 앞면");
                    Assert.AreEqual(0f, Ty(Card(root, 0)), ms + "ms: 떠오르지 않는다");
                    Assert.AreEqual(Vector3.one, Sc(Card(root, 0)), ms + "ms: 눌리지 않는다");
                }
                Assert.IsTrue(screen.Open, "고를 수 있다");
                screen.Hover(0, true);
                seek(ms += 200);
                Assert.AreEqual(0f, screen.LiftOf(0), "끄기: 마우스 오버로 들리지 않는다");
                Assert.AreEqual(0f, Ty(Card(root, 0)));

                // 고른 뒤(끄기): 쓸려 가는 중간 모습 없이 고른 카드만 제자리에 남는다
                FxShots.Release();
                root = new VisualElement();
                seek = FxShots.FlowTrail(root, skin, FxShots.DiceGrade, SoloTrail.Catch, FxShots.EncounterOpen, speed: FxSpeed.Off);
                flow = FxShots.BattleFlow;
                screen = (EncounterScreen)flow.Stage.Current;
                Assert.GreaterOrEqual(screen.Picked, 0);
                for (ms = 16; ms <= 600; ms += 16)
                {
                    seek(ms);
                    if (flow.Stage.Current != screen) break;   // 전환도 건너뛰어 배틀 화면이 열렸다
                    for (int i = 0; i < screen.Cards.Count; i++)
                    {
                        var card = Card(root, i);
                        if (i == screen.Picked) Assert.AreEqual(0f, Ty(card), ms + "ms: 고른 카드는 제자리");
                        else Assert.AreEqual(0f, Op(card), ms + "ms: 고르지 않은 카드는 바로 덱으로(보이지 않는다)");
                    }
                }
            }
            finally
            {
                FxShots.Release();
                UiSkin.Release(skin);
            }
        }

        [Test]
        public void M9_조우카드_마우스오버로들리고빠름은반만에끄기는없으며고른뒤나머지는덱으로쓸려간다()
        {
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M9 Gen 없음: rebuild-all 먼저");
            try
            {
                foreach (var speed in new[] { FxSpeed.Normal, FxSpeed.Fast, FxSpeed.Off })
                {
                    var root = new VisualElement();
                    var seek = FxShots.EncounterHover(root, skin, FxShots.DiceGrade, 1, speed);
                    var screen = (EncounterScreen)FxShots.BattleFlow.Stage.Current;
                    int length = EncounterCards.HoverMs(speed);
                    if (length > 0)
                    {
                        seek(length / 2);
                        Assert.Greater(screen.LiftOf(1), 0f, speed + ": 들리는 중");
                        Assert.Less(screen.LiftOf(1), 1f, speed + ": 들리는 중");
                    }
                    int up = System.Math.Max(length, 1);
                    seek(up);
                    float want = speed == FxSpeed.Off ? 0f : 1f;
                    Assert.AreEqual(want, screen.LiftOf(1), speed + ": 다 들렸다(끄기는 0)");
                    Assert.AreEqual(-want * EncounterScreen.LiftPx * 1.5f, Ty(Card(root, 1)), 0.51f, speed + ": 1080 벌 px");
                    Assert.AreEqual(0f, screen.LiftOf(0), "다른 카드는 그대로");
                    screen.Hover(1, false);
                    seek(up + length + 1);
                    Assert.AreEqual(0f, screen.LiftOf(1), speed + ": 벗어나면 내려앉는다");
                    FxShots.Release();
                }
                Assert.AreEqual(EnterBeats.HoverMs / 2, EncounterCards.HoverMs(FxSpeed.Fast), "빠름 = 반(기본 fx.fastPercent 50)");

                var root2 = new VisualElement();
                var seek2 = FxShots.FlowTrail(root2, skin, FxShots.DiceGrade, SoloTrail.Catch, FxShots.EncounterOpen);   // 카드를 골랐다
                var picked = (EncounterScreen)FxShots.BattleFlow.Stage.Current;
                int chosen = picked.Picked;
                Assert.GreaterOrEqual(chosen, 0);
                int other = chosen == 0 ? 1 : 0;
                seek2(EnterBeats.SweepFlipMs + EnterBeats.SweepSlideMs / 2);
                Assert.Greater(Ty(Card(root2, other)), 0f, "덱(아래)으로 쓸려 내려가는 중");
                Assert.Less(Op(Card(root2, other)), 1f);
                Assert.AreEqual(DisplayStyle.Flex, root2.Q("encounter-card-" + (other + 1) + "-back").style.display.value, "뒷면으로 뒤집혔다");
                seek2(EnterBeats.SweepEveryMs * 2 + EnterBeats.SweepFlipMs + EnterBeats.SweepSlideMs);
                for (int i = 0; i < picked.Cards.Count; i++)
                    Assert.AreEqual(i == chosen ? 1f : 0f, Op(Card(root2, i)), i + "번: 고른 카드만 남는다");
                Assert.AreEqual(0f, Ty(Card(root2, chosen)));
            }
            finally
            {
                FxShots.Release();
                UiSkin.Release(skin);
            }
        }

        [Test]
        public void M10_P39_기라티나그림자장면은DS를박자대로일그러뜨린다()
        {
            // 마감 감사 A7(P39를 웹에서 못 봄)의 연출 캡처 몫: 운명 길 FateGiratina의 희생(44행) — 그림자가 덮는 동안 DS의 그려진 진폭 = 박자, 셰이더가 있다
            var skin = UiSkin.Load();
            if (skin == null) Assert.Ignore("M4 Gen 없음: rebuild-all 먼저");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("M4 그래픽 없음(-nographics): 렌더 시험 건너뜀");
            try
            {
                Assert.IsNotNull(Shader.Find(DsStage.WarpShaderName), "DS 일그러짐 셰이더");
                Assert.IsTrue(FxShots.Scenes.Any(s => s.Name == "p39-shadow"), "연출 캡처 장면 p39-shadow");
                var root = new VisualElement();
                var seek = FxShots.WarpSeen(FxShots.RowScene(root, skin, FxShots.Grade, SoloTrail.FateGiratina, st => st.Row == FateRows.Sacrifice), FxShots.P39ShadowShotsMs);
                foreach (int ms in FxShots.P39ShadowShotsMs) Assert.DoesNotThrow(() => seek(ms), ms + "ms");
                var screen = (BattleScreen)FxShots.BattleFlow.Stage.Current;
                Assert.Greater(screen.Ds.WarpDrawn, 0.5f, "마지막 시각에도 일그러짐이 그려졌다");
            }
            finally
            {
                FxShots.Release();
                UiSkin.Release(skin);
            }
        }
    }
}
