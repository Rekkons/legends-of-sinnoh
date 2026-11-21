using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// 배틀 화면이 따라가는 것(plan §4 「배틀 화면 뼈대」, 04 §4-7): 조사 고르기, 기록 줄(이벤트 값 그대로 · 문자열 표의 글), 끝난 배틀의 마지막 값.
    /// 판은 화면과 같은 길로 만든다 — 솔로 게임을 링크로 답해 셋업을 끝내고 고향 마을에서 [도전](1막 조무래기 배틀), 기술은 늘 첫 줄.
    /// </summary>
    public class BattleFeedTests
    {
        Ctx ctx;
        StringTable ko;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            ctx = TestData.LoadCtx();
            ko = TestData.LoadStrings("ko");
        }

        /// <summary>찾아 둔 판(시드 · 고향 마을 색인 · 스타팅 포켓몬 색인, AI 시드 99 · 첫 트레이너 카드 · 늘 첫 기술): 이기는 판과 지는 판. PlayMode 흐름 시험이 같은 판을 쓴다.</summary>
        public static readonly (ulong Seed, int Village, int Starter) WinGame = (179, 0, 1), LoseGame = (1, 0, 0);

        sealed class Played
        {
            public SeatClient Client;
            public BattleFeed Feed;
            public readonly List<GameEvent> Events = new List<GameEvent>();
            public int Answers;
        }

        // 셋업(첫 마을 · 첫 스타팅 포켓몬 · 첫 트레이너 카드) → [도전] → 기술은 늘 첫 줄, 우선권 토큰은 쓴다 → 배틀 끝까지
        Played Play(ulong seed, int village = 0, int starter = 0)
        {
            var session = SoloGame.Start(ctx, new SoloSettings(), seed, 99, new PrincipleAgent(), 0);
            var p = new Played { Client = new SeatClient(session.Link, ctx, SoloGame.Seat) };
            var client = p.Client;
            client.Updated += u =>
            {
                p.Events.AddRange(u.Events);
                if (p.Feed == null)
                    foreach (var e in u.Events)
                        if (e is BattleStarted) p.Feed = new BattleFeed(ctx, SoloGame.Seat);
                if (p.Feed != null) p.Feed.Feed(u.View, u.Events);
            };
            long now = 0;
            foreach (var kind in new[] { ChoiceKind.VillagePick, ChoiceKind.StarterPick, ChoiceKind.TrainerCardPick })
            {
                var c = client.Choice(kind);
                Assert.IsNotNull(c, kind.ToString());
                int pick0 = kind == ChoiceKind.VillagePick ? village : kind == ChoiceKind.StarterPick ? starter : 0;
                Assert.IsTrue(client.Answer(c, client.Legal(c)[pick0], now).Accepted, kind.ToString());
            }
            var map = client.Choice(ChoiceKind.MapAction);
            Assert.IsNotNull(map, "첫 턴");
            var challenge = client.Legal(map).Find(a => a.Kind == AnswerKind.Challenge);
            Assert.IsNotNull(challenge, "고향 마을에서 조무래기에게 도전할 수 있다(S16_2)");
            Assert.AreEqual(SeatId.Galactic, challenge.Target);
            Assert.IsTrue(client.Answer(map, challenge, now).Accepted);
            Assert.IsNotNull(p.Feed, "도전 답의 갱신에 BattleStarted가 온다");
            for (int i = 0; i < 2000 && !p.Feed.Ended; i++)
            {
                now += 60000;
                session.Tick(now);
                if (p.Feed.Ended) break;   // 상대(AI)의 답으로 끝났다
                var c = client.FirstChoice();
                if (c == null) continue;
                var legal = client.Legal(c);
                ChoiceAnswer pick;
                if (c.Kind == ChoiceKind.BattleAction) pick = legal.Find(a => a.Kind == AnswerKind.UseMove);
                else if (c.Kind == ChoiceKind.Reaction && c.Sub == ChoiceSub.InitiativeToken) pick = legal.Find(a => a.Kind == AnswerKind.Accept);
                else { Assert.Fail($"시드 {seed}: 첫 조무래기 배틀에서 받을 화면이 없는 선택 {c.Kind}/{c.Sub} — " + string.Join(",", p.Events.ConvertAll(e => e.GetType().Name).GetRange(Math.Max(0, p.Events.Count - 30), Math.Min(30, p.Events.Count)))); return null; }
                Assert.IsTrue(client.Answer(c, pick, now).Accepted);
                p.Answers++;
            }
            Assert.IsTrue(p.Feed.Ended, $"시드 {seed}: 배틀이 끝나지 않았다");
            return p;
        }

        [Test]
        public void M4_조사는앞글자의받침에맞춘다()
        {
            Assert.AreEqual("렌트라는 쓰러졌다!", KoJosa.Fix("렌트라은(는) 쓰러졌다!"));
            Assert.AreEqual("동미러를 내보냈다!", KoJosa.Fix("동미러을(를) 내보냈다!"));
            Assert.AreEqual("엠페르트가 나타났다!", KoJosa.Fix("엠페르트이(가) 나타났다!"));
            Assert.AreEqual("불꽃숭이는 무엇을 할까?", KoJosa.Fix("불꽃숭이은(는) 무엇을 할까?"));
            Assert.AreEqual("팽도리는", KoJosa.Fix("팽도리은(는)"));
            Assert.AreEqual("모부기는", KoJosa.Fix("모부기은(는)"));
            Assert.AreEqual("찌르꼬는 · 꼬링크가 · 비버니를", KoJosa.Fix("찌르꼬은(는) · 꼬링크이(가) · 비버니을(를)"));
            Assert.AreEqual("고라파덕은 · 주뱃이 · 무우마직을", KoJosa.Fix("고라파덕은(는) · 주뱃이(가) · 무우마직을(를)"));
            Assert.AreEqual("2막이 시작됩니다.", KoJosa.Fix("2막이(가) 시작됩니다."));
            Assert.AreEqual("마을로 · 길로 · 숲으로", KoJosa.Fix("마을(으)로 · 길(으)로 · 숲(으)로"), "ㄹ 받침은 「로」");
            Assert.AreEqual("Rekkon은(는)", KoJosa.Fix("Rekkon은(는)"), "한글이 아니면 고르지 않는다");
            Assert.AreEqual("야생 깝질무 (동쪽바다 폼)이 나타났다!", KoJosa.Fix("야생 깝질무 (동쪽바다 폼)이(가) 나타났다!"), "괄호로 끝나는 이름은 괄호 안 마지막 글자에 맞춘다");
            Assert.AreEqual("로토무 (히트)를", KoJosa.Fix("로토무 (히트)을(를)"));
            Assert.AreEqual("", KoJosa.Fix(""));
            Assert.IsNull(KoJosa.Fix(null));
        }

        [Test]
        public void M4_배틀기록줄은이벤트값그대로이고글은문자열표에있다()
        {
            int seen = 0;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var p = Play(seed);
                var feed = p.Feed;
                Assert.IsTrue(feed.Mine);
                Assert.AreEqual(0, feed.MySide, "도전자 = Side 0");
                Assert.AreEqual(BattleKind.Galactic, feed.Kind);
                Assert.AreEqual(BattleFeed.K + "start.galactic", feed.Lines[0].Key);

                // 이벤트 → 줄: 기술 · 주사위 · 피해 · 기절 · 끝이 이벤트 수만큼 있고, 순서는 받은 순서다
                var battle = new List<GameEvent>();
                bool inBattle = false;
                foreach (var e in p.Events)
                {
                    if (e is BattleStarted) inBattle = true;
                    if (inBattle) battle.Add(e);
                    if (e is BattleEnded) break;
                }
                int Count(string key) => feed.Lines.FindAll(l => l.Key == BattleFeed.K + key).Count;
                Assert.AreEqual(battle.FindAll(e => e is MoveUsed).Count, Count("move"), $"시드 {seed}: 기술 줄");
                Assert.AreEqual(battle.FindAll(e => e is DiceRolled).Count, Count("dice"), $"시드 {seed}: 주사위 줄");
                Assert.AreEqual(battle.FindAll(e => e is DamageBreakdown).Count, Count("damage"), $"시드 {seed}: 데미지 줄");
                Assert.AreEqual(battle.FindAll(e => e is Fainted).Count, Count("faint"), $"시드 {seed}: 기절 줄");
                Assert.AreEqual(battle.FindAll(e => e is SentIn).Count, Count("send.mine") + Count("send.galactic"), $"시드 {seed}: 출전 줄");
                var lastDamage = (DamageBreakdown)battle.FindLast(e => e is DamageBreakdown);
                Assert.AreSame(lastDamage, feed.LastDamage, "계산 줄 = 마지막 피해 계산");
                Assert.That(feed.Lines[feed.Lines.Count - 1].Key, Does.StartWith(BattleFeed.K + "end."), "마지막 줄 = 배틀 끝");

                // 주사위 줄의 눈·합 = 이벤트 값(굴림에 그 굴림의 면 바꾸기 — 지속 보정의 더하기·지우기 포함 — 를 넣은 마지막 눈. 지운 주사위는 낱말이 없다)
                var rolls = battle.FindAll(e => e is DiceRolled);
                var diceLines = feed.Lines.FindAll(l => l.Key == BattleFeed.K + "dice");
                for (int i = 0; i < rolls.Count; i++)
                {
                    var roll = (DiceRolled)rolls[i];
                    var final = new List<AttackDieFace>(roll.Faces);
                    for (int at = battle.IndexOf(roll) + 1; at < battle.Count && !(battle[at] is DiceRolled); at++)
                    {
                        if (!(battle[at] is DiceFacesChanged fc)) continue;
                        foreach (var ch in fc.Changes)
                        {
                            while (final.Count <= ch.Index) final.Add(AttackDieFace.None);
                            final[ch.Index] = ch.After;
                        }
                    }
                    Assert.AreEqual(AttackDice.Sum(final).ToString(), diceLines[i].Args.Find(a => a.Name == "sum").Value);
                    Assert.AreEqual(final.FindAll(f => f != AttackDieFace.None).Count, diceLines[i].Args.Find(a => a.Name == "faces").Keys.Length, "면마다 낱말 하나");
                    Assert.AreEqual(roll.Side, diceLines[i].Side);
                }

                // 글: 줄의 키와 빈칸의 키가 모두 문자열 표에 있고, 만든 글에 빈칸·조사 괄호가 남지 않는다
                foreach (var line in feed.Lines)
                {
                    Assert.IsTrue(ko.Contains(line.Key), line.Key);
                    foreach (var a in line.Args)
                        if (a.Keys != null) foreach (var k in a.Keys) Assert.IsTrue(ko.Contains(k), line.Key + " → " + k);
                    string text = line.Text(k => ko[k]);
                    Assert.That(text, Does.Not.Contain("{").And.Not.Contain("은(는)").And.Not.Contain("이(가)").And.Not.Contain("을(를)"), line.Key);
                    Assert.That(text.Length, Is.GreaterThan(0));
                }
                seen++;
            }
            Assert.AreEqual(12, seen);
        }

        [Test]
        public void M5_좌석끼리배틀첫줄은트레이너문구()
        {
            // M4 마감에서 찾음: 갤럭시단이 아니면 모두 「앗! 야생 ○○이(가) 튀어나왔다!」였다 — 좌석끼리 배틀은 트레이너 문구
            string Sent(BattleKind kind)
            {
                var feed = new BattleFeed(ctx, SoloGame.Seat);
                feed.Feed(null, new GameEvent[]
                {
                    new BattleStarted { BattleId = 1, Kind = kind },
                    new SentIn { Side = 1, PokemonId = 3, Species = 0, Hp = 4, MaxHp = 4 },
                });
                return feed.Lines.Find(l => l.Key.StartsWith(BattleFeed.K + "send.")).Key;
            }
            Assert.AreEqual(BattleFeed.K + "send.trainer", Sent(BattleKind.Trainer));
            Assert.AreEqual(BattleFeed.K + "send.wild", Sent(BattleKind.Wild));
            Assert.AreEqual(BattleFeed.K + "send.galactic", Sent(BattleKind.Galactic));
            Assert.IsTrue(ko.Contains(BattleFeed.K + "send.trainer"));
        }

        [Test]
        public void M9_알파스트라이크는고른타입을함께말한다_T20()
        {
            // 플레이 시험 T20 「잠만보는 알파스트라이크 . 강철 을 사용했다 이런식으로」: 알파 스트라이크의 기술 줄에 고른 타입(MoveUsed.Type) — 다른 기술은 그대로
            int ember = ctx.Cards.MoveIds.IndexOf("ember");
            var feed = new BattleFeed(ctx, SoloGame.Seat);
            feed.Feed(null, new GameEvent[]
            {
                new BattleStarted { BattleId = 1, Kind = BattleKind.Alpha },
                new MoveUsed { Side = 1, PokemonId = 3, MoveKey = BattleRules.AlphaStrike, Type = PokemonType.Steel },
                new MoveUsed { Side = 0, PokemonId = 2, MoveKey = ember, Type = PokemonType.Fire },
            });
            var alpha = feed.Lines.FindAll(l => l.Key == BattleFeed.K + "move.alpha");
            Assert.AreEqual(1, alpha.Count);
            StringAssert.EndsWith("의 알파 스트라이크 · 강철!", alpha[0].Text(k => ko[k]));
            Assert.AreEqual(1, feed.Lines.FindAll(l => l.Key == BattleFeed.K + "move").Count, "다른 기술은 「○○의 △△!」 그대로");
            Assert.AreEqual(1, alpha[0].Side);
        }

        [Test]
        public void M5_흉내내기와손가락흔들기는부른기술을한줄로알린다()
        {
            // leftovers §1 #6: 같은 공격의 둘째 MoveSelected(MoveHandlers.Call) = 부른 기술 — 기술 줄 바로 뒤에 「○○(으)로 △△이(가) 나왔다!」
            int mimic = ctx.Cards.MoveIds.IndexOf("mimic"), metronome = ctx.Cards.MoveIds.IndexOf("metronome"), ember = ctx.Cards.MoveIds.IndexOf("ember"), surf = ctx.Cards.MoveIds.IndexOf("surf");
            var feed = new BattleFeed(ctx, SoloGame.Seat);
            feed.Feed(null, new GameEvent[]
            {
                new BattleStarted { BattleId = 1, Kind = BattleKind.Wild },
                new MoveSelected { Side = 0, MoveKey = mimic },
                new MoveSelected { Side = 0, MoveKey = ember },
                new MoveUsed { Side = 0, PokemonId = 2, MoveKey = mimic },
            });
            var called = feed.Lines.FindAll(l => l.Key == BattleFeed.K + "move.called");
            Assert.AreEqual(1, called.Count);
            Assert.AreEqual("흉내내기로 불꽃세례가 나왔다!", called[0].Text(k => ko[k]));
            Assert.AreEqual(BattleFeed.K + "move", feed.Lines[feed.Lines.IndexOf(called[0]) - 1].Key, "기술 줄 바로 뒤");
            Assert.AreEqual(0, called[0].Side);

            feed.Feed(null, new GameEvent[]
            {
                new MoveSelected { Side = 1, MoveKey = metronome },
                new MoveSelected { Side = 1, MoveKey = surf },
                new MoveUsed { Side = 1, PokemonId = 3, MoveKey = metronome },
            });
            called = feed.Lines.FindAll(l => l.Key == BattleFeed.K + "move.called");
            Assert.AreEqual(2, called.Count);
            Assert.AreEqual("손가락흔들기로 파도타기가 나왔다!", called[1].Text(k => ko[k]));

            // 부르지 않은 공격 · 굴리기 전에 끊긴 앞 공격의 선택은 치지 않는다
            feed.Feed(null, new GameEvent[]
            {
                new MoveSelected { Side = 0, MoveKey = ember },
                new MoveUsed { Side = 0, PokemonId = 2, MoveKey = ember },
                new MoveSelected { Side = 0, MoveKey = ember },   // 굴리기 전에 끊겼다
                new MoveSelected { Side = 1, MoveKey = surf },
                new MoveUsed { Side = 1, PokemonId = 3, MoveKey = surf },
                new MoveSelected { Side = 0, MoveKey = mimic },   // 끊겼다
                new MoveSelected { Side = 0, MoveKey = surf },
                new MoveUsed { Side = 0, PokemonId = 2, MoveKey = surf },
            });
            Assert.AreEqual(2, feed.Lines.FindAll(l => l.Key == BattleFeed.K + "move.called").Count);
        }

        [Test]
        public void M4_주사위줄은면바꾸기뒤의눈과합을적는다()
        {
            // 굴림판의 결과 줄·계산 줄의 「주사위」 칸·데미지는 면 바꾸기 뒤의 눈으로 센다(01 §6-4, BattleRoll.FinalFaces) — 기록 줄도 같은 눈·합이어야 한다
            const AttackDieFace S = AttackDieFace.StatusSymbol, R = AttackDieFace.RegularHit, C = AttackDieFace.CriticalHit;
            string[] Faces(BattleLine line) => line.Args.Find(a => a.Name == "faces").Keys;
            string Value(BattleLine line, string name) => line.Args.Find(a => a.Name == name).Value;
            const string crit = BattleFeed.K + "face.crit", hit = BattleFeed.K + "face.hit", status = BattleFeed.K + "face.status";

            // 졸음: 둘째 주사위(일반)가 심볼로 — 합 4 → 3
            var feed = new BattleFeed(ctx, SoloGame.Seat);
            feed.Feed(null, new GameEvent[]
            {
                new BattleStarted { BattleId = 1, Kind = BattleKind.Wild },
                new DiceRolled { Side = 0, PokemonId = 2, Faces = new[] { C, R, R }, Sum = 4 },
                new DiceFacesChanged { Source = new SourceRef(SourceKind.Status, (int)StatusEffect.Drowsy), Changes = new[] { new FaceChange { Index = 1, Before = R, After = S } } },
            });
            var dice = feed.Lines.FindAll(l => l.Key == BattleFeed.K + "dice");
            Assert.AreEqual(1, dice.Count, "면 바꾸기는 줄을 더하지 않고 주사위 줄을 고친다");
            Assert.AreEqual(new[] { crit, status, hit }, Faces(dice[0]));
            Assert.AreEqual("3", Value(dice[0], "sum"), "합 = 바꾼 뒤의 눈(데미지 계산의 주사위 칸)");
            Assert.AreEqual("3", Value(dice[0], "n"));
            Assert.AreEqual("주사위 3개: 급소, 상태 이상, 일반 (합계 3)", dice[0].Text(k => ko[k]));
            Assert.IsInstanceOf<DiceRolled>(dice[0].Event, "드러나는 때는 그대로 굴림(세기가 끝난 뒤)");

            // 마비: 일반 타격을 지운다 — 지운 주사위는 줄에서 빠진다. 다음 굴림의 줄은 따로다
            feed.Feed(null, new GameEvent[]
            {
                new DiceRolled { Side = 1, PokemonId = 3, Faces = new[] { R, C, R }, Sum = 4 },
                new DiceFacesChanged { Changes = new[] { new FaceChange { Index = 0, Before = R, After = AttackDieFace.None }, new FaceChange { Index = 2, Before = R, After = AttackDieFace.None } } },
                new DiceFacesChanged { Changes = new FaceChange[0] },
            });
            dice = feed.Lines.FindAll(l => l.Key == BattleFeed.K + "dice");
            Assert.AreEqual(2, dice.Count);
            Assert.AreEqual(new[] { crit }, Faces(dice[1]));
            Assert.AreEqual("2", Value(dice[1], "sum"));
            Assert.AreEqual("1", Value(dice[1], "n"));
            Assert.AreEqual("3", Value(dice[0], "sum"), "앞 굴림의 줄은 그대로");

            // 포획: 상태 이상인 대상이면 심볼 하나가 일반 타격으로 센다(A7) — 합(정확도 적용 뒤)과 눈이 한 줄 안에서 맞는다
            feed.Feed(null, new GameEvent[]
            {
                new CaptureAttempt { Side = 0, BallKey = (int)Ball.Regular, TargetId = 3, Faces = new[] { S, R, S }, Sum = 2, CatchRate = 3, StatusConverted = true, Shakes = 2 },
            });
            var roll = feed.Lines.Find(l => l.Key == BattleFeed.K + "capture.roll");
            Assert.AreEqual(new[] { hit, hit, status }, Faces(roll), "바뀐 심볼은 일반으로 적는다(첫 심볼 하나)");
            Assert.AreEqual("2", Value(roll, "sum"));
            feed.Feed(null, new GameEvent[]
            {
                new CaptureAttempt { Side = 0, BallKey = (int)Ball.Regular, TargetId = 3, Faces = new[] { S, R, R }, Sum = 2, CatchRate = 3, Shakes = 2 },
            });
            roll = feed.Lines.FindLast(l => l.Key == BattleFeed.K + "capture.roll");
            Assert.AreEqual(new[] { status, hit, hit }, Faces(roll), "상태 이상이 아닌 대상: 굴린 눈 그대로");
        }

        [Test]
        public void M4_끝난배틀은마지막값을붙잡고이기면1막목표가풀린다()
        {
            // 스타팅 포켓몬 하나로 조무래기 둘을 이기는 판은 드물다(마을 8 × 스타팅 2 × 시드 1~300 = 4800판 중 하나) — 찾아 둔 판을 쓴다
            int wins = 0, losses = 0;
            foreach (var game in new[] { WinGame, LoseGame })
            {
                ulong seed = game.Seed;
                var p = Play(game.Seed, game.Village, game.Starter);
                var feed = p.Feed;
                var view = p.Client.View;
                Assert.IsNull(view.Battle, "끝난 배틀은 보기에 없다");
                Assert.IsNotNull(feed.Live, "마지막으로 살아 있던 보기를 들고 있다");
                Assert.AreSame(feed.Live, feed.Source);
                var ended = (BattleEnded)p.Events.FindLast(e => e is BattleEnded);
                Assert.AreEqual(ended.Winner, feed.Winner);
                Assert.AreEqual(ended.Result, feed.Result);
                var me = feed.Mon(feed.MySide);
                var foe = feed.Mon(1 - feed.MySide);
                Assert.IsTrue(me.Exists, "끝나기 직전에 나와 있던 내 포켓몬");
                Assert.AreEqual(view.Pokemon[view.Seats[0].Party[0]].Def, me.Def);
                // 마지막 한 방은 마지막으로 살아 있던 보기에 아직 없다 — 이벤트가 실은 값으로 읽는다(공격 피해 HpChanged · 상태 이상 피해 StatusTicked)
                var last = new Dictionary<int, int>();
                foreach (var e in p.Events)
                {
                    if (e is BattleEnded) break;
                    if (e is HpChanged h)
                    {
                        last[h.PokemonId] = h.After;
                        if (h.PokemonId == me.Id) Assert.That(h.Max, Is.EqualTo(me.MaxHp));
                    }
                    else if (e is StatusTicked t && t.HpAfter != GameEvent.U) last[t.PokemonId] = t.HpAfter;
                }
                if (last.TryGetValue(me.Id, out int mine)) Assert.AreEqual(mine, me.Hp, $"시드 {seed}: 내 포켓몬의 마지막 체력");
                if (foe.Exists && last.TryGetValue(foe.Id, out int theirs)) Assert.AreEqual(theirs, foe.Hp, $"시드 {seed}: 상대 포켓몬의 마지막 체력");
                if (feed.Won)
                {
                    wins++;
                    Assert.AreEqual(1, view.Seats[0].ActProgress, "이기면 1막 해방(A1)");
                    Assert.AreEqual(1, feed.GoalMetAct, "결과 창이 적는 달성 막");
                    Assert.IsFalse(feed.Whiteout);
                    Assert.That(feed.RevealedAct, Is.GreaterThanOrEqualTo(0), "처음 해방한 좌석 → 2막 공개");
                    Assert.AreEqual(BattleFeed.K + "end.win", feed.Lines[feed.Lines.Count - 1].Key);
                    Assert.IsTrue(foe.Fainted || !foe.Exists, "상대의 마지막 포켓몬은 쓰러졌다");
                }
                else
                {
                    losses++;
                    Assert.AreEqual(0, view.Seats[0].ActProgress);
                    Assert.AreEqual(-1, feed.GoalMetAct);
                    Assert.IsTrue(me.Fainted, "스타팅 포켓몬 하나뿐이라 지면 쓰러진 것이다");
                    Assert.IsTrue(feed.Whiteout, "쓸 포켓몬이 없어 고향 마을로(S7_8)");
                    Assert.AreEqual(BattleFeed.K + (feed.Drawn ? "end.draw" : "end.lose"), feed.Lines[feed.Lines.Count - 1].Key);
                }
                // 배틀 끝 → 턴 끝 → 다음 턴(같은 갱신)
                Assert.AreEqual(Phase.Turn, view.Phase);
                Assert.IsNotNull(p.Client.Choice(ChoiceKind.MapAction), "다음 턴의 행동 선택이 열려 있다");
            }
            Assert.AreEqual(1, wins, "이기는 판");
            Assert.AreEqual(1, losses, "지는 판");
        }
    }
}
