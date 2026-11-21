using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// plan M4-3 「연출 층 뼈대」(04 §8-2~§8-4·§8-6): 화면 대기열(FIFO·건너뛰기 없음·일시정지·「빈다」), 표시 상태, 단서 구간 훑기, 토스트, 실행기 연결.
    /// 시각은 모두 인자로 넣는다(엔진·실시각 없음).
    /// </summary>
    public class FxQueueTests
    {
        [TearDown]
        public void ResetTunables() => Tunables.Shared.Reset();

        static FxEntry Entry(int seq, int queueMs, int blockMs = 0) => new FxEntry { Seq = seq, QueueMs = queueMs, BlockMs = blockMs };

        [Test]
        public void M4_대기열은받은순서대로건너뛰지않고재생한다()
        {
            var seq = new FxSequencer();
            var started = new List<int>();
            var finished = new List<int>();
            seq.Started += e => started.Add(e.Seq);
            seq.Finished += e => finished.Add(e.Seq);
            seq.Enqueue(new[] { Entry(1, 1000), Entry(2, 0), Entry(3, 500, 300) });
            Assert.AreEqual(3, seq.Pending);
            Assert.IsNull(seq.Current, "넣기만 해서는 시작하지 않는다(시간은 Advance로만 흐른다)");

            seq.Advance(0);
            Assert.AreEqual(1, seq.Current.Seq);
            Assert.AreEqual(2, seq.Pending, "「다음 2」");
            seq.Advance(999);
            Assert.AreEqual(1, seq.Current.Seq);
            Assert.AreEqual(999, seq.LocalMs);

            // 한 번에 길게 보내도 항목을 건너뛰지 않는다: 1이 끝나고, 길이 0인 2가 지나가고, 3이 남은 시간만큼 재생된다
            seq.Advance(401);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, started);
            CollectionAssert.AreEqual(new[] { 1, 2 }, finished);
            Assert.AreEqual(3, seq.Current.Seq);
            Assert.AreEqual(400, seq.LocalMs);
            Assert.AreEqual(2, seq.Last.Seq, "끝난 항목의 결과는 다음 항목이 시작될 때까지 남는다");
            Assert.AreEqual(0, seq.Pending);

            seq.Advance(399);
            Assert.IsFalse(seq.IsEmpty, "3의 길이 = 대기열 500 + 차단 컷신 300");
            seq.Advance(1);
            Assert.IsTrue(seq.IsEmpty);
            Assert.AreEqual(3, seq.Last.Seq);
            Assert.AreEqual(1800, seq.NowMs, "걸린 시간 = 항목 길이 합(줄이지 않는다)");
        }

        [Test]
        public void M4_대기열이비는것은받은이벤트를다재생한것()
        {
            var seq = new FxSequencer();
            Assert.IsTrue(seq.IsEmpty);
            Assert.IsTrue(seq.InputOpen, "재생한 것이 없으면 입력은 처음부터 열려 있다");
            Assert.IsTrue(seq.Idle);

            // 찾아보기(Seq 7)만 받았다 — 뒤에 조우 고르기(같은 행동의 뒷부분, Continues = 7)가 올 테지만 Seq가 닫히기를 기다리지 않는다(P4)
            seq.Enqueue(new[] { Entry(7, 2000) });
            Assert.IsFalse(seq.IsEmpty, "받았으나 아직 재생하지 않은 것이 있다");
            Assert.IsFalse(seq.InputOpen);
            seq.Advance(1999);
            Assert.IsFalse(seq.IsEmpty);
            seq.Advance(1);
            Assert.IsTrue(seq.IsEmpty, "받은 것을 다 재생했다 = 빈다");

            // 비자마자 열지 않는다: 클릭 막기 300ms(04 §8-5)
            Assert.IsFalse(seq.InputOpen);
            Assert.IsFalse(seq.Idle, "클릭 막기가 끝날 때까지는 시계가 흘러야 한다");
            seq.Advance(FxTune.ClickGuardMs.Value - 1);
            Assert.IsFalse(seq.InputOpen);
            seq.Advance(1);
            Assert.IsTrue(seq.InputOpen);
            Assert.IsTrue(seq.Idle);

            // 길게 한 번에 보내도 빈 시각은 항목이 끝난 때다
            seq.Enqueue(new[] { Entry(8, 100) });
            seq.Advance(100 + FxTune.ClickGuardMs.Value);
            Assert.IsTrue(seq.InputOpen);
        }

        [Test]
        public void M4_일시정지는지금항목을끝까지재생하고다음항목을시작하지않는다()
        {
            var seq = new FxSequencer();
            seq.Enqueue(new[] { Entry(1, 1000), Entry(2, 1000) });
            seq.Advance(400);
            seq.Paused = true;
            seq.Advance(5000);
            Assert.IsNull(seq.Current, "지금 항목은 끝까지");
            Assert.AreEqual(1, seq.Last.Seq);
            Assert.AreEqual(1, seq.Pending, "다음 항목은 시작하지 않는다");
            Assert.IsFalse(seq.IsEmpty, "기다리는 항목이 있으면 빈 것이 아니다");
            Assert.IsFalse(seq.InputOpen);

            seq.Paused = false;
            seq.Advance(0);
            Assert.AreEqual(2, seq.Current.Seq);
            Assert.AreEqual(0, seq.LocalMs, "멈춘 동안 흐른 시간은 다음 항목에 들지 않는다");
            seq.Advance(1000);
            Assert.IsTrue(seq.IsEmpty);
        }

        [Test]
        public void M4_표시값은단서가닿을때바뀌고대기열이비면보기와같다()
        {
            var hp = ShownKey.Hp(42);
            var balls = ShownKey.Resource(0, ResKind.Ball, 0);
            var a = Entry(1, 1000);
            a.AddShown(600, hp, 20, 13);
            var b = Entry(2, 1000);
            b.AddShown(500, hp, 13, 9);
            b.AddShown(900, balls, 3, 2);

            // 갱신을 받는 순간 보기는 이미 최종 값(9, 2)이다. 화면은 붙잡은 값을 그린다
            const int viewHp = 9, viewBalls = 2;
            var seq = new FxSequencer();
            Assert.AreEqual(viewHp, seq.Shown.Get(hp, viewHp), "붙잡은 값이 없으면 보기의 값");
            seq.Enqueue(new[] { a, b });
            Assert.AreEqual(20, seq.Shown.Get(hp, viewHp), "넣는 순간 그 항목이 바꿀 값을 Before로 붙잡는다(앞 항목의 Before가 먼저)");
            Assert.AreEqual(3, seq.Shown.Get(balls, viewBalls));

            seq.Advance(599);
            Assert.AreEqual(20, seq.Shown.Get(hp, viewHp));
            seq.Advance(1);
            Assert.AreEqual(13, seq.Shown.Get(hp, viewHp), "착지 = 단서가 닿는 순간");
            seq.Advance(400 + 499);
            Assert.AreEqual(13, seq.Shown.Get(hp, viewHp), "둘째 항목의 단서 전");
            seq.Advance(1);
            Assert.AreEqual(9, seq.Shown.Get(hp, viewHp));
            Assert.AreEqual(3, seq.Shown.Get(balls, viewBalls));
            seq.Advance(400);
            Assert.AreEqual(2, seq.Shown.Get(balls, viewBalls));
            Assert.AreEqual(2, seq.Shown.Count);

            seq.Advance(100);
            Assert.IsTrue(seq.IsEmpty);
            Assert.AreEqual(0, seq.Shown.Count, "대기열이 비면 붙잡은 값을 모두 놓는다");
            Assert.AreEqual(7, seq.Shown.Get(hp, 7), "그 뒤로는 보기를 따른다");
        }

        [Test]
        public void M4_표시값은이벤트의Before에서After로간다()
        {
            // 이벤트가 싣고 온 값(01 §3-6 ③)이 단서가 된다: HP, 피로 칸, 좌석 자원. 남에게 가려진 Changes(null)는 건너뛴다
            var e = Entry(1, 1000);
            FxShown.AddCues(e, new HpChanged { PokemonId = 5, Before = 8, After = 3 }, 100);
            FxShown.AddCues(e, new FatigueChanged { PokemonId = 5, Before = 1, After = 2 }, 200);
            FxShown.AddCues(e, new Healed { Pokemon = new[] { new HealEntry { Id = 6, HpBefore = 2, HpAfter = 9 } } }, 300);
            FxShown.AddCues(e, new BattleEnded { Changes = new[] { new ResChange { Seat = 1, Res = ResKind.Jp, Key = 0, Before = 4, After = 6 } } }, 400);
            FxShown.AddCues(e, new CaptureAttempt { Changes = null }, 500);
            FxShown.AddCues(e, new Moved(), 600);
            // 독·혼란 피해는 StatusTicked가, 주운 규토리는 ApricornsTaken이 필드로 싣는다(HpChanged·Changes[]가 따로 오지 않는다). 채우지 않은 값(U)은 건너뛴다
            FxShown.AddCues(e, new StatusTicked { PokemonId = 7, Status = StatusEffect.Poisoned, Effect = -2, HpBefore = 5, HpAfter = 3 }, 700);
            FxShown.AddCues(e, new ApricornsTaken { Seat = 2, Color = (int)ApricornColor.Pink, Count = 2, Before = 1, After = 3 }, 800);
            FxShown.AddCues(e, new StatusTicked { PokemonId = 7 }, 900);
            FxShown.AddCues(e, new ApricornsTaken { Seat = 2, Color = 0 }, 900);
            Assert.AreEqual(6, e.Cues.Count);
            Assert.AreEqual(ShownKey.Hp(5), e.Cues[0].Key);
            Assert.AreEqual(ShownKey.Fatigue(5), e.Cues[1].Key);
            Assert.AreEqual(ShownKey.Hp(6), e.Cues[2].Key);
            Assert.AreEqual(ShownKey.Resource(1, ResKind.Jp, 0), e.Cues[3].Key);
            Assert.AreEqual(4, e.Cues[3].Before);
            Assert.AreEqual(6, e.Cues[3].After);
            Assert.AreEqual(ShownKey.Hp(7), e.Cues[4].Key);
            Assert.AreEqual(5, e.Cues[4].Before);
            Assert.AreEqual(3, e.Cues[4].After);
            Assert.AreEqual(ShownKey.Resource(2, ResKind.Apricorn, (int)ApricornColor.Pink), e.Cues[5].Key);
            Assert.AreEqual(1, e.Cues[5].Before);
            Assert.AreEqual(3, e.Cues[5].After);
            Assert.AreNotEqual(ShownKey.Hp(5), ShownKey.Fatigue(5));
        }

        [Test]
        public void M4_상태이상피해는그단계가끝날때보이고기절뒤옛값이남지않는다()
        {
            // 공격 피해(HpChanged 10 → 5) 뒤 독 피해(StatusTicked 5 → 3) → 기절은 다른 포켓몬. 보기는 받는 순간 3이다
            var hp = ShownKey.Hp(7);
            var ev = new List<GameEvent>
            {
                new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.BattleAction },
                new HpChanged { Seq = 1, Seat = 0, PokemonId = 7, Before = 10, After = 5 },
                new StatusTicked { Seq = 1, Seat = 0, PokemonId = 7, Status = StatusEffect.Poisoned, Effect = -2, HpBefore = 5, HpAfter = 3 },
            };
            var entry = new FxPlanner(0).Plan(ev)[0];
            int hit = entry.Step(612).EndMs, tick = entry.Step(630).EndMs;
            Assert.Less(hit, tick);
            Assert.Less(tick, entry.LengthMs, "독 단계가 끝난 뒤에도 보기 시간이 남는다(그동안 옛 값이 남으면 안 된다)");
            var seq = new FxSequencer();
            seq.Enqueue(new[] { entry });
            Assert.AreEqual(10, seq.Shown.Get(hp, 3));
            seq.Advance(hit);
            Assert.AreEqual(5, seq.Shown.Get(hp, 3), "공격 피해 착지");
            seq.Advance(tick - hit - 1);
            Assert.AreEqual(5, seq.Shown.Get(hp, 3), "독 단계가 끝나기 전");
            seq.Advance(1);
            Assert.AreEqual(3, seq.Shown.Get(hp, 3), "독 피해는 독 단계가 끝날 때 착지한다(대기열이 빌 때가 아니다)");

            // 독 피해만 있는 항목: 굴림이 시작되기 전에 최종 값이 보이지 않는다
            var only = new FxPlanner(0).Plan(new List<GameEvent>
            {
                new ActionStarted { Seq = 2, Seat = 0, Kind = ActionKind.BattleAction },
                new DiceRolled { Seq = 2, Seat = 0, Faces = new AttackDieFace[2] },
                new StatusTicked { Seq = 2, Seat = 0, PokemonId = 8, Status = StatusEffect.Confused, Effect = -2, HpBefore = 3, HpAfter = 1 },
            })[0];
            seq = new FxSequencer();
            seq.Enqueue(new[] { only });
            Assert.AreEqual(3, seq.Shown.Get(ShownKey.Hp(8), 1), "넣는 순간 Before로 붙잡는다");
            seq.Advance(only.Step(630).EndMs);
            Assert.AreEqual(1, seq.Shown.Get(ShownKey.Hp(8), 1));

            // 동시 피해 묶음에서 단계 순서와 이벤트 순서가 어긋날 때: 남의 공격 피해(61행 단계가 먼저 생김) → 독 5 → 3(63행) → 같은 포켓몬의 공격 피해 3 → 1(61행 단계에 합쳐짐).
            // 같은 값의 단서는 이벤트 순서대로 닿는다 — 뒤 이벤트를 앞 단서의 시각으로 늦춘다(마지막에 보이는 값 = 마지막 이벤트의 After)
            var mixed = new FxPlanner(0).Plan(new List<GameEvent>
            {
                new ActionStarted { Seq = 4, Seat = 0, Kind = ActionKind.BattleAction },
                new HpChanged { Seq = 4, Seat = 0, PokemonId = 1, Before = 9, After = 6 },
                new StatusTicked { Seq = 4, Seat = 0, PokemonId = 7, Status = StatusEffect.Poisoned, Effect = -2, HpBefore = 5, HpAfter = 3 },
                new HpChanged { Seq = 4, Seat = 0, PokemonId = 7, Before = 3, After = 1 },
            })[0];
            Assert.AreEqual(1, mixed.Steps.FindAll(s => s.Row == 612).Count, "61행 단계는 하나(같은 박자)");
            seq = new FxSequencer();
            seq.Enqueue(new[] { mixed });
            seq.Advance(mixed.Step(612).EndMs);
            Assert.AreEqual(6, seq.Shown.Get(ShownKey.Hp(1), 6));
            Assert.AreEqual(5, seq.Shown.Get(hp, 1), "독 피해보다 먼저 닿지 않는다");
            seq.Advance(mixed.Step(630).EndMs - mixed.Step(612).EndMs);
            Assert.AreEqual(1, seq.Shown.Get(hp, 1), "독 5 → 3 뒤 공격 3 → 1");
            Assert.IsFalse(seq.IsEmpty, "대기열이 비기 전에 이미 마지막 값이다");


            var red = ShownKey.Resource(0, ResKind.Apricorn, 0);
            var take = new FxPlanner(0).Plan(new List<GameEvent>
            {
                new ActionStarted { Seq = 3, Seat = 0, Kind = ActionKind.TakeApricorns },
                new ApricornsTaken { Seq = 3, Seat = 0, Color = 0, Count = 2, Before = 1, After = 3 },
            })[0];
            seq = new FxSequencer();
            seq.Enqueue(new[] { take });
            Assert.AreEqual(1, seq.Shown.Get(red, 3));
            seq.Advance(take.Step(190).EndMs - 1);
            Assert.AreEqual(1, seq.Shown.Get(red, 3));
            seq.Advance(1);
            Assert.AreEqual(3, seq.Shown.Get(red, 3));
        }

        /// <summary>값(Before → After)을 싣지만 표시 값 단서로 달지 않는 이벤트 타입과 그 이유. 화면을 만들며 ShownKey가 생기면 FxShown.AddCues로 옮긴다.</summary>
        static readonly Dictionary<Type, string> NotCued = new Dictionary<Type, string>
        {
            { typeof(Moved), "남은 이동력(AllowanceBefore/After) — 이동력 칩 화면을 만들 때(plan §4)" },
            { typeof(CardUnlocked), "트레이너 카드 등급(TierBefore/After) — 해금 연출(48행)과 S40 등급 표시는 M6 「트레이너 카드 화면」 묶음이 만든다" },
            { typeof(PartyReordered), "파티 순서 배열 — 정비 화면이 보기를 바로 그린다(연출 단계 없음)" },
            { typeof(FormChanged), "폼 — 값이 아니라 그림이 바뀐다(그 화면을 만들 때)" },
            { typeof(DiceRolled), "피로 칸(FatigueBefore/After) — 같은 값을 FatigueChanged(Cause = Roll)가 함께 싣고 그것이 단서다" },
            { typeof(DiceFacesChanged), "주사위 면·개수 — 굴림판 부품이 이벤트로 그린다(보기에 남는 값이 아니다)" },
            { typeof(RetributionStored), "은하의 응징 — 그 표시를 만들 때(M5)" },
            { typeof(PartySlotUnlocked), "열린 파티 칸 — 94행은 배틀 결과 창의 줄(「파티 칸이 하나 열렸다」)로 그리고, HUD의 파티 칸은 보기를 바로 그린다(M5 「막·종막 화면」)" },
            { typeof(BreachChanged), "균열 강도 — 균열 게이지(S56) 화면을 만들 때(M6 「UB 보드 화면」)" },
            { typeof(BreachBracket), "균열 구간 — 같은 게이지의 85행 연출을 만들 때(M6 「UB 보드 화면」)" },
            { typeof(PrismMoved), "프리즘 아머 이동 — 88행 연출을 만들 때(M6 「UB 배틀·카드 화면」)" },
        };

        static bool CarriesValue(Type t)
        {
            foreach (var f in t.GetFields())
            {
                if (f.Name.EndsWith("Before", StringComparison.Ordinal) || f.Name.EndsWith("After", StringComparison.Ordinal)) return true;
                var el = f.FieldType.IsArray ? f.FieldType.GetElementType() : null;
                if (el == null) continue;
                if (el == typeof(ResChange)) return true;
                foreach (var ef in el.GetFields())
                    if (ef.Name.EndsWith("Before", StringComparison.Ordinal) && ef.FieldType == typeof(int)) return true;
            }
            return false;
        }

        [Test]
        public void M4_값을싣는이벤트는모두단서가되거나제외()
        {
            // 01 §3-6 ③: Before/After·Changes[]를 싣는 이벤트 타입은 모두 FxShown.AddCues가 단서로 달거나, 위 제외 목록에 이유와 함께 있다(빠뜨리면 그 값이 연출과 따로 바뀐다)
            int cued = 0;
            foreach (var t in typeof(GameEvent).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(GameEvent).IsAssignableFrom(t) || !CarriesValue(t)) continue;
                var e = (GameEvent)Activator.CreateInstance(t);
                e.Seat = 0;
                foreach (var f in t.GetFields())
                {
                    if (f.DeclaringType == typeof(GameEvent)) continue;
                    if (f.FieldType == typeof(int)) f.SetValue(e, f.Name.EndsWith("After", StringComparison.Ordinal) ? 2 : 1);
                    else if (f.FieldType == typeof(ResChange[])) f.SetValue(e, new[] { new ResChange { Seat = 0, Res = ResKind.Jp, Key = 0, Before = 1, After = 2 } });
                    else if (f.FieldType == typeof(HealEntry[])) f.SetValue(e, new[] { new HealEntry { Id = 1, HpBefore = 1, HpAfter = 2 } });
                    else if (f.FieldType == typeof(FateDeal[])) f.SetValue(e, new[] { new FateDeal { Seat = 0, Before = 1, After = 2 } });
                }
                var entry = Entry(1, 1000);
                FxShown.AddCues(entry, e, 0);
                if (NotCued.ContainsKey(t)) Assert.AreEqual(0, entry.Cues.Count, $"{t.Name}: 단서가 생겼다 — 제외 목록에서 뺀다");
                else
                {
                    Assert.Greater(entry.Cues.Count, 0, $"{t.Name}: 값(Before/After·Changes[])을 싣는데 표시 값 단서가 없다 — FxShown.AddCues에 더하거나 제외 목록에 이유를 적는다");
                    foreach (var c in entry.Cues)
                    {
                        Assert.AreEqual(1, c.Before, t.Name);
                        Assert.AreEqual(2, c.After, t.Name);
                    }
                    cued++;
                }
            }
            Assert.AreEqual(23, cued, "단서가 되는 타입: HpChanged·StatusTicked·FatigueChanged·Healed·ApricornsTaken + Changes[] 열다섯(M6 날씨 바꾸기 WeatherSet · M5 제작·은행상회·진화 셋 · 트레이너전 승리 보상 SpoilsTaken · 사당 공물 ShrineOffered·축복 ShrineBlessing · 여행 스탬프 StampGained · 거래 TradeDone 포함) + 운명 손패 장수 셋(FateDealt·FateDiscarded·FatePlayed — M5 「운명·응답 화면」)");
            foreach (var t in NotCued.Keys) Assert.IsTrue(CarriesValue(t), $"{t.Name}: 값을 싣지 않는데 제외 목록에 있다");
        }

        [Test]
        public void M4_단서는지난시각부터지금까지구간을훑어낸다()
        {
            var e = Entry(1, 1000);
            e.AddSound(500, "b");
            e.AddSound(0, "a");
            e.AddSound(500, "c");
            e.AddSound(1000, "d");
            CollectionAssert.AreEqual(new[] { 0, 500, 500, 1000 }, e.Cues.ConvertAll(c => c.AtMs), "시각 순, 같은 시각은 넣은 순");

            // (지난 시각, 지금]: 지난 시각의 단서는 다시 내지 않고, 지금의 단서는 낸다
            Assert.AreEqual("", Swept(e, 0, 499));
            Assert.AreEqual("bc", Swept(e, 499, 500));
            Assert.AreEqual("", Swept(e, 500, 999));
            Assert.AreEqual("a", Swept(e, -1, 0), "항목을 시작할 때 시각 0의 단서");
            Assert.AreEqual("abcd", Swept(e, -1, 1000));

            // 시퀀서: 한 번에 보내도, 프레임으로 잘게 보내도, 멈췄다 가도 같은 단서가 같은 순서로 한 번씩
            foreach (int stepMs in new[] { 1000, 16, 7, 1 })
            {
                var seq = new FxSequencer();
                string heard = "";
                var at = new List<long>();
                seq.Sound += c => { heard += c.Sound; at.Add(seq.LocalMs); };
                seq.Enqueue(new[] { e });
                for (int t = 0; t < 1000; t += stepMs)
                {
                    seq.Advance(System.Math.Min(stepMs, 1000 - t));
                    if (t == 0) seq.Advance(0);   // 시간이 흐르지 않은 프레임(멈춤)에는 다시 내지 않는다
                }
                Assert.AreEqual("abcd", heard, "걸음 " + stepMs);
            }

            // 다시 보기·재접속은 무음: 소리 단서만 빠지고 표시 값 단서는 닿는다
            var muted = new FxSequencer { Muted = true };
            int sounds = 0;
            muted.Sound += c => sounds++;
            var m = Entry(2, 100);
            m.AddSound(50, "x");
            m.AddShown(50, ShownKey.Hp(1), 5, 4);
            muted.Enqueue(new[] { m, Entry(3, 100) });
            muted.Advance(60);
            Assert.AreEqual(0, sounds);
            Assert.AreEqual(4, muted.Shown.Get(ShownKey.Hp(1), 0));
        }

        static string Swept(FxEntry e, int prev, int now)
        {
            var into = new List<FxCue>();
            FxCues.Sweep(e.Cues, prev, now, into);
            string s = "";
            foreach (var c in into) s += c.Sound;
            return s;
        }

        [Test]
        public void M4_항목길이밖에단단서도항목이끝날때닿는다()
        {
            var e = Entry(1, 100);
            e.AddShown(250, ShownKey.Hp(1), 5, 4);
            var seq = new FxSequencer();
            string heard = "";
            seq.Sound += c => heard += c.Sound;
            e.AddSound(300, "late");
            seq.Enqueue(new[] { e, Entry(2, 100) });
            seq.Advance(100);
            Assert.AreEqual(4, seq.Shown.Get(ShownKey.Hp(1), 0), "표시 값이 어긋난 채 다음 항목으로 넘어가지 않는다");
            Assert.AreEqual("late", heard);
        }

        [Test]
        public void M4_토스트는정한시간뒤내려가고셋까지만뜬다()
        {
            var seq = new FxSequencer();
            seq.Toast("a");
            Assert.IsFalse(seq.Idle, "토스트가 떠 있으면 시계가 흘러야 한다");
            seq.Advance(1000);
            seq.Toast("b");
            seq.Toast("c");
            seq.Toast("d");
            Assert.AreEqual(FxTune.ToastMax.Value, seq.Toasts.Count);
            Assert.AreEqual("b", seq.Toasts[0].Text, "넘치면 가장 오래된 것이 내려간다");
            seq.Advance(FxTune.ToastMs.Value - 1);
            Assert.AreEqual(3, seq.Toasts.Count);
            seq.Advance(1);
            Assert.AreEqual(0, seq.Toasts.Count);
            Assert.IsTrue(seq.Idle);

            // 대기열이 일시정지여도 토스트는 같은 시계로 내려간다(층은 달라도 시간축은 하나)
            seq.Paused = true;
            seq.Enqueue(new[] { Entry(1, 100) });
            seq.Toast("e");
            seq.Advance(FxTune.ToastMs.Value);
            Assert.AreEqual(0, seq.Toasts.Count);
            Assert.AreEqual(1, seq.Pending);
        }

        [Test]
        public void M4_이어받는항목은앞항목을찾는다()
        {
            var seq = new FxSequencer();
            var search = Entry(10, 100);
            var pick = Entry(11, 100);
            pick.Continues = 10;
            var react = Entry(12, 100);
            react.ReactsTo = 11;
            seq.Enqueue(new[] { search });
            seq.Advance(100);
            Assert.IsTrue(seq.IsEmpty);
            // 남이 고르는 동안 앞 항목은 끝난 채 남아 있고, 이어지는 항목이 오면 그 화면에서 시작한다
            seq.Enqueue(new[] { pick, react });
            seq.Advance(0);
            Assert.AreSame(pick, seq.Current);
            Assert.AreSame(search, seq.Find(seq.Current.Continues), "끝난 앞 항목");
            Assert.AreSame(pick, seq.Find(react.ReactsTo), "재생 중인 항목");
            Assert.AreSame(react, seq.Find(12), "기다리는 항목");
            Assert.IsNull(seq.Find(-1));
            Assert.IsNull(seq.Find(99));
        }

        [Test]
        public void M4_지금도는단계와장식은항목시각으로고른다()
        {
            var e = Entry(1, 1000, 300);
            e.Steps.Add(new FxStep { Row = 40, Layer = FxLayer.Block, StartMs = 0, Ms = 300 });
            e.Steps.Add(new FxStep { Row = 560, Layer = FxLayer.Item, StartMs = 300, Ms = 500 });
            var chip = e.AddDeco(350, 200, "chip");
            Assert.AreEqual(300, e.BlockStepsMs);
            Assert.AreEqual(500, e.StepsMs, "장식은 길이에 들지 않는다");
            Assert.AreEqual(1300, e.LengthMs);

            var seq = new FxSequencer();
            seq.Enqueue(new[] { e });
            var active = new List<FxStep>();
            seq.Advance(299);
            seq.Active(active);
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(FxLayer.Block, active[0].Layer, "① 차단 컷신이 도는 동안 ③ 단계는 시작하지 않는다");
            active.Clear();
            seq.Advance(101);
            seq.Active(active);
            CollectionAssert.AreEqual(new[] { FxLayer.Item, FxLayer.Deco }, active.ConvertAll(s => s.Layer));
            Assert.AreEqual(100, active[0].Local(seq.LocalMs), "부품이 받는 시각 = 항목 시각 − 단계 시작");
            Assert.AreEqual(50, chip.Local(seq.LocalMs), "④ 장식은 부모 항목의 시각을 따른다");
            active.Clear();
            seq.Advance(500);
            seq.Active(active);
            Assert.AreEqual(0, active.Count, "단계가 다 끝난 뒤는 보기 시간(멈춘 화면)");
            Assert.AreEqual(500, e.Steps[1].Local(seq.LocalMs), "지난 단계는 끝 모양");
            Assert.IsFalse(seq.IsEmpty);
        }

        [Test]
        public void M4_시퀀서는실행기에걸려바쁨을세고비면빠진다()
        {
            var runner = new TweenRunner();
            var seq = new FxSequencer();
            int frames = 0;
            var player = new FxPlayer(runner, seq, s => frames++);
            Assert.AreEqual(0, runner.Busy);

            var e = Entry(1, 500);
            e.AddShown(500, ShownKey.Hp(1), 5, 4);
            player.Enqueue(new[] { e });
            Assert.AreEqual(1, runner.Busy, "대기열에 일이 생기면 바쁨 1");
            player.Enqueue(new[] { Entry(2, 500) });
            Assert.AreEqual(1, runner.Busy, "두 번 걸리지 않는다");

            runner.Advance(999);
            Assert.AreEqual(1, runner.Busy);
            runner.Advance(1);
            Assert.IsTrue(seq.IsEmpty);
            Assert.AreEqual(1, runner.Busy, "클릭 막기 동안은 아직 돈다");
            runner.Advance(FxTune.ClickGuardMs.Value);
            Assert.AreEqual(0, runner.Busy, "다 비면 실행기에서 빠진다");
            int was = frames;
            runner.Advance(16);
            Assert.AreEqual(was, frames, "유휴 때 매 프레임 일 0");

            player.Toast("알림");
            Assert.AreEqual(1, runner.Busy, "다시 일이 생기면 다시 걸린다");
            runner.Advance(FxTune.ToastMs.Value);
            Assert.AreEqual(0, runner.Busy);
        }

        [Test]
        public void M4_일시정지로서있는대기열은바쁨에서빠지고풀면다시돈다()
        {
            var runner = new TweenRunner();
            var seq = new FxSequencer();
            var player = new FxPlayer(runner, seq, null);
            player.Enqueue(new[] { Entry(1, 500), Entry(2, 500) });
            runner.Advance(100);
            player.SetPaused(true);
            Assert.AreEqual(1, runner.Busy, "지금 항목은 끝까지 재생한다");
            runner.Advance(100);
            Assert.AreEqual(1, runner.Busy, "재생 중인 항목이 남아 있으면 기다리는 항목이 있어도 서 있는 것이 아니다");
            Assert.IsFalse(seq.Stalled);
            Assert.AreEqual(1, seq.Current.Seq);
            Assert.AreEqual(200, seq.LocalMs);
            runner.Advance(100);
            Assert.AreEqual(300, seq.LocalMs, "실행기에서 빠지지 않고 계속 흐른다");
            runner.Advance(200);
            Assert.IsNull(seq.Current);
            Assert.IsTrue(seq.Stalled);
            Assert.AreEqual(0, runner.Busy, "기다리는 항목만 남으면 바쁨이 아니다(일시정지 내내 유휴)");
            Assert.IsFalse(seq.IsEmpty, "빈 것은 아니다 — 입력은 열리지 않는다");
            Assert.IsFalse(seq.InputOpen);

            player.Enqueue(new[] { Entry(3, 500) });
            Assert.AreEqual(0, runner.Busy, "서 있는 동안 받은 항목도 기다린다");
            player.Toast("알림");
            Assert.AreEqual(1, runner.Busy, "토스트는 일시정지 중에도 흐른다");
            runner.Advance(FxTune.ToastMs.Value);
            Assert.AreEqual(0, runner.Busy);

            player.SetPaused(false);
            Assert.AreEqual(1, runner.Busy, "풀면 다시 걸린다");
            runner.Advance(0);
            Assert.AreEqual(2, seq.Current.Seq);
            runner.Advance(1000 + FxTune.ClickGuardMs.Value);
            Assert.IsTrue(seq.IsEmpty);
            Assert.AreEqual(0, runner.Busy);
        }

        // ── S02 「연출」(04 §8-1 P17 · §8-4): 내 화면의 재생 속도 ──

        [Test]
        public void M4_연출보통은그대로고빠름은대기열이배율만큼빨리비고클릭막기는실시각이다_P17()
        {
            // 보통: 받은 길이 그대로
            var normal = new FxSequencer();
            normal.Enqueue(new[] { Entry(1, 1000), Entry(2, 600) });
            normal.Advance(1599);
            Assert.IsFalse(normal.IsEmpty);
            normal.Advance(1);
            Assert.IsTrue(normal.IsEmpty);
            Assert.AreEqual(1600, normal.NowMs);

            // 빠름(길이 50%): 실시각 800에 1600 길이가 다 재생되고, 소리 단서도 그 배율로 닿는다
            Assert.AreEqual(50, FxTune.FastPercent.Value);
            var fast = new FxSequencer { Speed = FxSpeed.Fast };
            var heard = new List<string>();
            fast.Sound += c => heard.Add(c.Sound + "@" + fast.CueSeq);
            var second = Entry(2, 600);
            second.AddSound(400, SoundKeys.Hit);
            fast.Enqueue(new[] { Entry(1, 1000), second });
            Assert.AreEqual(1600, fast.LeftMs);
            fast.Advance(699);
            Assert.AreEqual(2, fast.Current.Seq);
            Assert.AreEqual(398, fast.LocalMs, "실시각 699 = 연출 시각 1398");
            Assert.IsEmpty(heard);
            fast.Advance(1);
            CollectionAssert.AreEqual(new[] { SoundKeys.Hit + "@2" }, heard, "소리 단서는 연출 시각 1400(실시각 700)");
            fast.Advance(100);
            Assert.IsTrue(fast.IsEmpty, "실시각 800에 빈다(보통의 절반)");
            Assert.AreEqual(1600, fast.NowMs);
            // 비고 난 뒤는 실시각: 클릭 막기 300은 줄지 않는다
            fast.Advance(FxTune.ClickGuardMs.Value - 1);
            Assert.IsFalse(fast.InputOpen);
            fast.Advance(1);
            Assert.IsTrue(fast.InputOpen);

            // 한 번에 보내도 나눠 보낸 것과 같다(대기열을 넘는 실시각은 빈 뒤의 시간이다)
            var once = new FxSequencer { Speed = FxSpeed.Fast };
            once.Enqueue(new[] { Entry(1, 1000), Entry(2, 600) });
            once.Advance(800 + FxTune.ClickGuardMs.Value);
            Assert.IsTrue(once.InputOpen);
            Assert.AreEqual(fast.NowMs, once.NowMs);
        }

        [Test]
        public void M4_연출끄기는받은항목을소리없이한번에보내고갱신마다대표소리하나_P17()
        {
            var seq = new FxSequencer { Speed = FxSpeed.Off };
            var heard = new List<string>();
            var finished = new List<int>();
            seq.Sound += c => heard.Add(c.Sound + "@" + seq.CueSeq);
            seq.Finished += e => finished.Add(e.Seq);
            // 갱신 하나 = 항목 둘. 대표 소리 = 받은 순서로 첫 소리 단서(첫 항목의 착지 소리)
            var walk = Entry(5, 1000);
            walk.AddShown(900, ShownKey.Hp(1), 10, 4);
            walk.AddSound(800, SoundKeys.Hit);
            walk.AddSound(100, SoundKeys.Land);
            var roll = Entry(6, 500);
            roll.AddSound(0, SoundKeys.Dice);
            seq.Enqueue(new[] { walk, roll });
            CollectionAssert.AreEqual(new[] { SoundKeys.Land + "@5" }, heard, "받을 때 대표 소리 한 번");
            Assert.AreEqual(10, seq.Shown.Get(ShownKey.Hp(1), 4), "착지 전에는 앞 값");

            seq.Advance(16);
            CollectionAssert.AreEqual(new[] { 5, 6 }, finished, "다음 시간에 다 재생했다(건너뛰지 않고 차례로 지나간다)");
            Assert.IsTrue(seq.IsEmpty);
            Assert.AreEqual(1, heard.Count, "지나간 항목의 소리 단서는 내지 않는다");
            Assert.IsFalse(seq.Muted, "무음을 푼다");
            Assert.AreEqual(4, seq.Shown.Get(ShownKey.Hp(1), 4), "착지는 닿았다(빈 뒤 = 보기의 값)");
            // 클릭 막기는 빈 뒤 실시각 300
            seq.Advance(FxTune.ClickGuardMs.Value - 17);
            Assert.IsFalse(seq.InputOpen);
            seq.Advance(1);
            Assert.IsTrue(seq.InputOpen);

            // 소리 단서가 없는 갱신 · 무음(건너뛰기·다시 보기)이면 대표 소리도 없다
            seq.Enqueue(new[] { Entry(7, 400) });
            seq.Muted = true;
            var quiet = Entry(8, 400);
            quiet.AddSound(0, SoundKeys.Land);
            seq.Enqueue(new[] { quiet });
            seq.Muted = false;
            Assert.AreEqual(1, heard.Count);
            seq.Advance(16);
            Assert.IsTrue(seq.IsEmpty);
            Assert.AreEqual(1, heard.Count);
            // 보통으로 돌리면 그대로 재생한다
            seq.Speed = FxSpeed.Normal;
            var plain = Entry(9, 400);
            plain.AddSound(100, SoundKeys.Land);
            seq.Enqueue(new[] { plain });
            Assert.AreEqual(1, heard.Count, "보통은 받을 때 내지 않는다");
            seq.Advance(1000);
            Assert.AreEqual(2, heard.Count);
            Assert.AreEqual(SoundKeys.Land + "@9", heard[1]);
        }

        [Test]
        public void M4_연출끄기도배틀끝의멈춤에서서고풀면나머지를보낸다_P17()
        {
            // 배틀 끝 항목이 다 재생되면 화면이 대기열을 세운다(결과 창 [확인]까지) — 끄기여도 그 뒤의 턴 넘김은 결과 창 뒤에서 지나가지 않는다
            var seq = new FxSequencer { Speed = FxSpeed.Off };
            var finished = new List<int>();
            seq.Finished += e =>
            {
                finished.Add(e.Seq);
                if (e.Seq == 3) seq.Paused = true;
            };
            seq.Enqueue(new[] { Entry(3, 2000), Entry(4, 1000) });
            seq.Advance(16);
            CollectionAssert.AreEqual(new[] { 3 }, finished);
            Assert.AreEqual(1, seq.Pending, "턴 넘김은 서 있다");
            Assert.AreEqual(0, seq.LeftMs, "일시정지면 남은 것은 재생 중인 항목뿐");
            seq.Advance(5000);
            Assert.AreEqual(1, seq.Pending);
            seq.Paused = false;
            seq.Advance(16);
            CollectionAssert.AreEqual(new[] { 3, 4 }, finished);
            Assert.IsTrue(seq.IsEmpty);
        }

        [Test]
        public void M4_연출빠름과끄기에서도토스트는실시각으로내려간다_P17()
        {
            // 끄기: 남은 대기열(5000)을 한 번에 보내도 토스트(4500)는 실시각 16ms만 지났다
            var off = new FxSequencer { Speed = FxSpeed.Off };
            off.Toast("a");
            off.Enqueue(new[] { Entry(1, 5000) });
            off.Advance(16);
            Assert.IsTrue(off.IsEmpty);
            Assert.AreEqual(1, off.Toasts.Count, "대기열을 한 번에 보내도 토스트는 남는다");
            off.Advance(FxTune.ToastMs.Value - 17);
            Assert.AreEqual(1, off.Toasts.Count);
            off.Advance(1);
            Assert.AreEqual(0, off.Toasts.Count);

            // 빠름(50%): 대기열이 도는 동안에도 토스트 수명은 줄지 않는다
            var fast = new FxSequencer { Speed = FxSpeed.Fast };
            fast.Toast("a");
            fast.Enqueue(new[] { Entry(1, 20000) });
            fast.Advance(2300);
            Assert.AreEqual(4600, fast.LocalMs);
            Assert.AreEqual(1, fast.Toasts.Count, "실시각 2300 < 4500");
            fast.Advance(FxTune.ToastMs.Value - 2301);
            Assert.AreEqual(1, fast.Toasts.Count);
            fast.Advance(1);
            Assert.AreEqual(0, fast.Toasts.Count);
            // 대기열이 도는 중에 띄운 토스트도 실시각
            fast.Toast("b");
            fast.Advance(FxTune.ToastMs.Value - 1);
            Assert.AreEqual(1, fast.Toasts.Count);
            fast.Advance(1);
            Assert.AreEqual(0, fast.Toasts.Count);
        }

        [Test]
        public void M9_따라잡기는늦은만큼만대기열시계를두배로돌리고_비면그친다_T4()
        {
            // 항목 둘(3000 + 2000)에 1500 늦었다: 실시각 3500에 다 비어야 한다(1500 동안 두 배 → 1500 덜기)
            var seq = new FxSequencer();
            seq.Enqueue(new[] { Entry(1, 3000), Entry(2, 2000) });
            seq.CatchUpMs = 1500;
            for (int i = 0; i < 34; i++) seq.Advance(100);
            Assert.IsFalse(seq.IsEmpty, "3400 ms: 아직 100 남음");
            Assert.AreEqual(100, seq.Current.LengthMs - seq.LocalMs);
            Assert.AreEqual(0, seq.CatchUpMs, "늦은 만큼을 다 덜었다");
            seq.Advance(100);
            Assert.IsTrue(seq.IsEmpty, "실시각 3500에 비었다(5000 − 1500)");

            // 남은 대기열보다 크게 늦었다고 해도 대기열을 넘어 보내지 않고, 비면 그친다
            var more = new FxSequencer();
            more.Enqueue(new[] { Entry(1, 1000) });
            more.CatchUpMs = 9000;
            more.Advance(400);
            Assert.AreEqual(800, more.LocalMs, "두 배(400 → 800)");
            more.Advance(400);
            Assert.IsTrue(more.IsEmpty);
            Assert.AreEqual(0, more.CatchUpMs, "비면 따라잡기가 그친다 — 다음 항목을 빨리 돌리지 않는다");
            more.Enqueue(new[] { Entry(2, 1000) });
            more.Advance(400);
            Assert.AreEqual(400, more.LocalMs, "다음 항목은 제 박자");

            // 떠나면(Clear) 남은 따라잡기도 버린다
            more.CatchUpMs = 500;
            more.Clear();
            Assert.AreEqual(0, more.CatchUpMs);
        }
    }
}
