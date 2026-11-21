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
    /// 좌석 손님으로 솔로 1막을 화면 없이 끝까지 돈다(plan §4 「조우·배틀 2단계」): 나무·산호로 걸어가 찾아보기 → 조우 고르기 → 야생 배틀 → 배틀 뒤 선택 → 고향 마을로 돌아가 도전.
    /// 답은 확인용 길잡이(<see cref="SoloPilot"/>)가 고른다 — 캡처·플레이 시험·플레이어 확인이 같은 판(<see cref="SoloTrail"/>)을 화면으로 지난다.
    /// 규칙·데이터가 바뀌어 찾아 둔 판이 달라지면 여기서 드러난다: 마을 0~7 × 스타팅 0~1 × 시드를 돌려 같은 일이 일어나는 판을 다시 찾아 <see cref="SoloTrail"/>을 고친다.
    /// </summary>
    public class SoloPilotTests
    {
        Ctx ctx;
        StringTable ko;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            ctx = TestData.LoadCtx();
            ko = TestData.LoadStrings("ko");
        }

        sealed class Run
        {
            public LocalSession Session;
            public SeatClient Client;
            public SoloPilot Pilot;
            public readonly List<GameEvent> Events = new List<GameEvent>();
            public readonly List<string> Asked = new List<string>();
            public readonly List<BattleFeed> Feeds = new List<BattleFeed>();
            public BattleFeed Feed;
            public int Answers;
            public long Now;

            public List<T> Of<T>() where T : GameEvent
            {
                var list = new List<T>();
                foreach (var e in Events) if (e is T t) list.Add(t);
                return list;
            }

            /// <summary>갤럭시단 배틀이 끝났나([도전]까지 갔다).</summary>
            public bool Challenged()
            {
                bool galactic = false;
                foreach (var e in Events)
                    if (e is BattleStarted b) galactic = b.Kind == BattleKind.Galactic;
                    else if (e is BattleEnded && galactic) return true;
                return false;
            }
        }

        // 판 하나를 길잡이로 돈다: 셋업은 판의 색인대로, 그 뒤는 길잡이. 화면이 하는 것과 같게 — 합법 답이 「안 씀」 하나뿐인 운명 카드 반응 창은 그 답을 낸다.
        // each = 답을 내기 직전에 본다(선택 · 합법 답)
        // afterSetup = 셋업이 끝난 판(첫 턴의 선택이 열린 호스트 원본의 사본)을 손봐 그 상태로 잇는다(빈 찾아보기처럼 셋업 뒤에만 만들 수 있는 판)
        Run Play(SoloTrail trail, Func<Run, bool> until, Action<Run, PendingChoice, List<ChoiceAnswer>> each = null, Action<GameState> afterSetup = null)
        {
            var run = new Run { Pilot = trail.Pilot() };
            SeatClient client = null;
            void Attach(LocalSession session)
            {
                client?.Close();
                run.Session = session;
                run.Client = client = new SeatClient(session.Link, ctx, SoloGame.Seat);
                client.Updated += u =>
                {
                    run.Events.AddRange(u.Events);
                    run.Pilot.Saw(u.Events);
                    foreach (var e in u.Events)
                        if (e is BattleStarted)
                        {
                            run.Feed = new BattleFeed(ctx, SoloGame.Seat);
                            run.Feeds.Add(run.Feed);
                        }
                    if (run.Feed != null) run.Feed.Feed(u.View, u.Events);
                };
            }
            Attach(SoloGame.Start(ctx, new SoloSettings(), trail.Seed, SoloTrail.AiSeed, new PrincipleAgent(), 0, prepare: trail.Prepared ? trail.Prepare : (Action<GameState>)null));
            for (int i = 0; i < 20000 && run.Answers < 800 && !until(run); i++)
            {
                if (afterSetup != null && client.Choice(ChoiceKind.MapAction) != null)
                {
                    var state = run.Session.HostCopy();
                    afterSetup(state);
                    afterSetup = null;
                    Attach(LocalSession.FromState(ctx, state, new SessionConfig { VirtualAgent = new PrincipleAgent(), Ai = new SoloSettings().ToAi(SoloTrail.AiSeed) }, run.Now));
                }
                var c = client.FirstChoice();
                if (c == null)
                {
                    run.Now += 60000;
                    run.Session.Tick(run.Now);
                    continue;
                }
                var legal = client.Legal(c);
                ChoiceAnswer a;
                if (c.Kind == ChoiceKind.VillagePick) a = legal[trail.Village];
                else if (c.Kind == ChoiceKind.StarterPick) a = legal[trail.Starter];
                else if (c.Kind == ChoiceKind.TrainerCardPick) a = legal[0];
                else a = run.Pilot.Decide(ctx, client.View, c, legal);
                string asked = c.Kind + (c.Sub != ChoiceSub.None ? "/" + c.Sub : "");
                if (!run.Asked.Contains(asked)) run.Asked.Add(asked);
                Assert.IsNotNull(a, $"{trail.Name}: 길잡이가 받을 줄 모르는 선택 {asked}");
                Assert.IsTrue(legal.Exists(x => x.SameAs(a)), $"{trail.Name}: {asked}의 답 {a}이(가) 합법 답이 아니다");
                each?.Invoke(run, c, legal);
                Assert.IsTrue(client.Answer(c, a, run.Now).Accepted, $"{trail.Name}: {asked} {a}");
                run.Answers++;
            }
            Assert.IsTrue(until(run), $"{trail.Name}: 끝까지 가지 못했다(답 {run.Answers}개, 물은 것 {string.Join(",", run.Asked)})");
            return run;
        }

        /// <summary>화면이 받는 선택(조우·배틀 2단계까지): 이 밖의 것이 솔로 1막에서 열리면 「아직 준비 중」으로 판이 멈춘다.</summary>
        static readonly string[] Screens =
        {
            "VillagePick", "StarterPick", "TrainerCardPick", "MapAction", "HiddenSpot", "EncounterPick", "SendIn", "NextPokemon", "SwitchWindow", "BattleAction",
            "Reaction/InitiativeToken", "Reward/TeachMove", "Reward/PartyFull", "Reward/CoralColor",
            "Reaction/Fate", "Discard", // 귀환 뒤 받는 운명(M5): 낼 카드가 없는 운명 창 · 6장째 버리기는 화면이 생길 때까지 GameFlow가 넘긴다(PassEmptyReaction)
        };

        [Test]
        public void M4_좌석손님으로찾아보기에서격파와기술가르치기를거쳐도전까지돈다_S5_1()
        {
            int picks = 0;
            var run = Play(SoloTrail.Defeat, r => r.Challenged(), (r, c, legal) =>
            {
                if (c.Kind != ChoiceKind.EncounterPick) return;
                // 조우 고르기: 합법 답 = 카드마다 하나(카드, 줄 안 자리) — 화면의 카드·[배틀!] 수와 같다. 그 뒤에 고르기 전 제작 답(룰북 p.2 — 화면은 제작 화면 묶음)
                picks++;
                legal = legal.FindAll(a => a.Kind != AnswerKind.Craft);
                var drawn = r.Of<EncounterDrawn>();
                Assert.AreEqual(drawn[drawn.Count - 1].Cards.Length, c.Options.Count);
                Assert.AreEqual(c.Options.Count, legal.Count);
                for (int i = 0; i < legal.Count; i++)
                {
                    Assert.AreEqual(AnswerKind.Pick, legal[i].Kind);
                    Assert.AreEqual(c.Options[i], legal[i].Target);
                    Assert.AreEqual(i, legal[i].Arg);
                }
                Assert.AreEqual(r.Client.View.Seats[0].Cell, c.Subject, "Subject = 찾아본 칸");
            });
            Assert.AreEqual(1, picks);
            Assert.AreEqual(1, run.Of<Searched>().Count);
            Assert.AreEqual(3, run.Of<EncounterDrawn>()[0].Cards.Length, "약한 나무·산호 = 3장");
            Assert.AreEqual(1, run.Of<EncounterPicked>().Count);
            var battles = run.Of<BattleStarted>();
            Assert.AreEqual(2, battles.Count, "야생 배틀 → 갤럭시단 배틀");
            Assert.AreEqual(BattleKind.Wild, battles[0].Kind);
            Assert.AreEqual(BattleKind.Galactic, battles[1].Kind);
            var ends = run.Of<BattleEnded>();
            Assert.AreEqual(BattleResult.Win, ends[0].Result);
            Assert.AreEqual(0, ends[0].Winner, "야생을 쓰러뜨렸다");
            Assert.That(run.Asked, Does.Contain("Reward/TeachMove"), "쓰러뜨린 야생의 고유 기술을 가르칠지 묻는다(p.9)");
            Assert.AreEqual(1, run.Of<MoveTaught>().Count, "길잡이는 가르친다");
            Assert.That(run.Of<Healed>().Count, Is.GreaterThan(0), "고향 마을에서 [전체 회복]");
            foreach (string asked in run.Asked) Assert.That(Screens, Does.Contain(asked), "화면이 받는 선택");

            // 야생 배틀의 따라가기: 끝난 뒤의 기술 가르치기가 결과 창에 실린다
            var feed = run.Feeds[0];
            Assert.IsTrue(feed.Won);
            Assert.IsNotNull(feed.Taught, "배틀 뒤 선택의 답(MoveTaught)을 받는다");
            Assert.AreEqual(-1, feed.CaughtId);
            Assert.That(feed.FoeId, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void M4_야생이도망치면받을규토리색을묻는다_A8()
        {
            PendingChoice coral = null;
            List<ChoiceAnswer> coralLegal = null;
            var run = Play(SoloTrail.Coral, r => r.Challenged(), (r, c, legal) =>
            {
                if (c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.CoralColor) { coral = c; coralLegal = legal; Assert.IsFalse(r.Feed.Ended, "색을 묻는 동안 배틀은 아직 끝나지 않았다"); }
            });
            Assert.IsNotNull(coral, "섞인 산호에서 도망치면 색을 묻는다");
            Assert.That(coralLegal.Count, Is.GreaterThan(1), "받을 색이 갈릴 때만 묻는다");
            foreach (var a in coralLegal) Assert.AreEqual(coral.Count, a.Values.Count);
            var feed = run.Feeds[0];
            Assert.AreEqual(BattleResult.Fled, feed.Result);
            Assert.IsFalse(feed.Won);
            int got = 0;
            foreach (var c in feed.FledChanges) got += c.After - c.Before;
            Assert.AreEqual(coral.Count, got, "남긴 규토리 = 고른 개수");
            Assert.AreEqual(BattleFeed.K + "fled", feed.Lines[feed.Lines.Count - 1].Key, "마지막 줄 = 도망쳤다(끝 줄을 따로 달지 않는다)");
            foreach (string asked in run.Asked) Assert.That(Screens, Does.Contain(asked));
        }

        [Test]
        public void M4_볼을쥐여준판은포획출전교체파티가득을거쳐1막목표까지돈다_G13()
        {
            int partyFull = 0, sendIn = 0, switchWindow = 0, next = 0, throws = 0;
            var run = Play(SoloTrail.Catch, r => r.Challenged(), (r, c, legal) =>
            {
                if (c.Kind == ChoiceKind.Reward && c.Sub == ChoiceSub.PartyFull)
                {
                    partyFull++;
                    Assert.IsTrue(r.Feed.Ended, "파티 가득은 배틀이 끝난 뒤에 묻는다");
                    Assert.AreEqual(c.Subject, legal[0].Target, "첫 답 = 막 잡은 포켓몬(가진 것을 잃지 않는 답)");
                    Assert.AreEqual(1 + PokemonRules.PartyCount(r.Client.View.Seats[0]), legal.Count, "잡은 포켓몬 + 파티");
                    Assert.AreEqual(c.Subject, r.Feed.CaughtId);
                }
                else if (c.Kind == ChoiceKind.SendIn) { sendIn++; foreach (var a in legal) Assert.AreEqual(AnswerKind.Pick, a.Kind); }
                else if (c.Kind == ChoiceKind.NextPokemon) next++;
                else if (c.Kind == ChoiceKind.SwitchWindow) { switchWindow++; Assert.AreEqual(AnswerKind.Decline, legal[0].Kind, "교체 안 함이 첫 답"); }
                else if (c.Kind == ChoiceKind.BattleAction && legal.Exists(a => a.Kind == AnswerKind.ThrowBall)) throws++;
            });
            Assert.AreEqual(1, partyFull, "셋째 포획에서 파티가 가득 찬다(칸 3)");
            Assert.That(sendIn, Is.GreaterThan(0), "파티가 둘 이상이면 출전을 고른다");
            Assert.That(switchWindow + next, Is.GreaterThan(0), "교체 창·다음 포켓몬");
            var caps = run.Of<CaptureAttempt>();
            Assert.AreEqual(throws, caps.Count, "볼 던지기 답 하나 = 포획 시도 하나");
            Assert.AreEqual(3, caps.FindAll(c => c.Success).Count);
            Assert.AreEqual(1, run.Of<Released>().Count, "파티 가득: 하나를 놓아준다");
            Assert.AreEqual(1, run.Client.View.Seats[0].ActProgress, "조무래기를 이겨 1막 목표");
            Assert.AreEqual(9 - caps.Count, run.Client.View.Seats[0].Balls[(int)Ball.Regular]);
            foreach (string asked in run.Asked) Assert.That(Screens, Does.Contain(asked));

            // 포획 줄: 던졌다 → 주사위(합 / 포획률) → 잡았다·나와 버렸다. 값은 이벤트 그대로, 글은 문자열 표에
            int seen = 0;
            foreach (var feed in run.Feeds)
            {
                if (feed.Kind != BattleKind.Wild) continue;
                var rolls = feed.Lines.FindAll(l => l.Key == BattleFeed.K + "capture.roll");
                var mine = caps.GetRange(seen, rolls.Count);
                seen += rolls.Count;
                Assert.AreEqual(rolls.Count, feed.Lines.FindAll(l => l.Key == BattleFeed.K + "ball").Count);
                for (int i = 0; i < rolls.Count; i++)
                {
                    Assert.AreEqual(mine[i].Sum.ToString(), rolls[i].Args.Find(a => a.Name == "sum").Value);
                    Assert.AreEqual(mine[i].CatchRate.ToString(), rolls[i].Args.Find(a => a.Name == "rate").Value);
                }
                Assert.AreEqual(BattleResult.Captured, feed.Result);
                Assert.IsTrue(feed.Won, "잡은 쪽이 이긴 쪽");
                Assert.AreSame(mine[mine.Count - 1], feed.LastCapture, "계산 줄 = 마지막 포획 시도");
                Assert.AreEqual(mine[mine.Count - 1].TargetId, feed.CaughtId);
                Assert.AreEqual(BattleFeed.K + "capture.ok", feed.Lines[feed.Lines.Count - 1].Key);
                foreach (var line in feed.Lines)
                {
                    Assert.IsTrue(ko.TryGet(line.Key, out _), "문자열 표에 없는 줄 " + line.Key);
                    string text = line.Text(k => ko[k]);
                    Assert.That(text, Does.Not.Contain("{").And.Not.Contain("(는)").And.Not.Contain("(를)").And.Not.Contain("(가)"), line.Key);
                }
            }
            Assert.AreEqual(caps.Count, seen);
            var full = run.Feeds.Find(f => f.ReleasedKey != null);
            Assert.IsNotNull(full, "놓아준 포켓몬이 결과 창에 실린다");
        }

        [Test]
        public void M4_찾아보기에아무것도없으면조우없이규토리줍기가열린다_G12()
        {
            bool tookOffered = false;
            // 약한 조우 덱을 비운 판: 약한 나무·산호를 찾아봐도 맞는 카드가 없다(셋업이 남은 스타팅 포켓몬을 약한 덱에 넣으므로 셋업 뒤에 비운다)
            var run = Play(SoloTrail.Defeat, r => r.Of<ApricornsTaken>().Count > 0, (r, c, legal) =>
            {
                Assert.AreNotEqual(ChoiceKind.EncounterPick, c.Kind, "카드가 없으면 조우 고르기를 열지 않는다");
                if (c.Kind == ChoiceKind.MapAction && r.Of<EncounterDrawn>().Count > 0)
                {
                    tookOffered = legal.Exists(a => a.Kind == AnswerKind.TakeApricorns);
                    Assert.IsFalse(legal.Exists(a => a.Kind == AnswerKind.Search), "같은 턴에 같은 칸을 다시 찾아보지 못한다");
                }
            }, s => s.Decks[(int)DeckId.Weak].Clear());
            Assert.AreEqual(0, run.Of<EncounterDrawn>()[0].Cards.Length);
            Assert.IsTrue(tookOffered, "빈 찾아보기 뒤 규토리 줍기가 열린다");
            Assert.AreEqual(0, run.Of<BattleStarted>().Count);
            Assert.AreEqual(1, run.Pilot.Hunted, "길잡이는 빈 찾아보기를 한 번으로 센다(같은 칸을 맴돌지 않는다)");
        }

        [Test]
        public void M4_나무등급에따라조우카드는3장2장1장이다_S5_1()
        {
            foreach (var pair in new[] { (SoloTrail.Defeat, 3), (SoloTrail.Two, 2), (SoloTrail.One, 1) })
            {
                var run = Play(pair.Item1, r => r.Of<EncounterDrawn>().Count > 0);
                Assert.AreEqual(pair.Item2, run.Of<EncounterDrawn>()[0].Cards.Length, pair.Item1.Name);
                var pick = run.Client.Choice(ChoiceKind.EncounterPick);
                Assert.IsNotNull(pick);
                Assert.AreEqual(pair.Item2, run.Client.Legal(pick).FindAll(a => a.Kind == AnswerKind.Pick).Count);
                foreach (var card in run.Of<EncounterDrawn>()[0].Cards) Assert.AreEqual(pair.Item1.Tier, card.Grade);
            }
        }

        [Test]
        public void M4_결정73_Defeat판의첫야생배틀에서스피드가같아우선권토큰을묻는다()
        {
            // 우선권 토큰의 놓기·빛남·넘기기 캡처(FxShots token-* · UiShots battle-token-ask)가 이 판을 지난다 — 판이 달라지면 동률 판을 다시 찾는다
            var run = Play(SoloTrail.Defeat, r => r.Asked.Contains("Reaction/InitiativeToken"));
            Assert.AreEqual(1, run.Of<BattleStarted>().Count, "첫 배틀");
            Assert.AreEqual(BattleKind.Wild, run.Of<BattleStarted>()[0].Kind);
            var token = run.Of<InitiativeTokenAt>();
            Assert.AreEqual(1, token.Count);
            Assert.AreEqual(0, token[0].Side, "도전자(나) 쪽에 토큰");
            var tie = run.Of<OrderDecided>().FindLast(o => o.First < 0);
            Assert.IsNotNull(tie, "묻기 전의 비교(First −1)");
            Assert.AreEqual(tie.Sides[0].Final, tie.Sides[1].Final, "스피드가 같다");
        }

        [Test]
        public void M4_손본판은볼과덱만다르고시드가같으면같은판이다()
        {
            var plain = SoloGame.Start(ctx, new SoloSettings(), 1, SoloTrail.AiSeed, new PrincipleAgent(), 0);
            var given = SoloGame.Start(ctx, new SoloSettings(), 1, SoloTrail.AiSeed, new PrincipleAgent(), 0, prepare: SoloTrail.Catch.Prepare);
            var a = plain.HostCopy();
            var b = given.HostCopy();
            Assert.AreEqual(0, a.Seats[0].Balls[(int)Ball.Regular], "볼을 얻는 길(제작)은 아직 없다 — 새 판은 볼 0");
            Assert.AreEqual(9, b.Seats[0].Balls[(int)Ball.Regular]);
            b.Seats[0].Balls[(int)Ball.Regular] = 0;
            Assert.AreEqual(StateHash.Of(a), StateHash.Of(b), "볼 말고는 같은 상태");
        }

        // ── 막 길(M5 「막·종막 화면」 — 셋업 뒤 손본 판: SoloTrail.Lake·Heart, 캡처·PlayMode 흐름 시험이 화면으로 같은 길을 지난다) ──

        [Test]
        public void M5_막길_입지호수에서새턴을이겨조각셋과3막카드()
        {
            var trail = SoloTrail.Lake;
            var run = Play(trail, r => r.Of<ActRevealed>().Exists(a => a.FirstSeat == 0), afterSetup: s => trail.After(ctx, s));
            var g = run.Of<GalacticBattle>();
            Assert.AreEqual(1, g.Count);
            Assert.AreEqual(GalacticLeader.Commander, g[0].Leader);
            Assert.AreEqual(4, g[0].LineupCount, "새턴의 명단 = 조각 수 2 + 2(A16)");
            Assert.AreEqual(Shard.Willpower, run.Of<ShardGained>()[0].Shard);
            Assert.AreEqual(6, run.Of<PartySlotUnlocked>()[0].After);
            Assert.AreEqual(2, run.Of<ActGoalMet>()[0].Act);
            Assert.AreEqual(0, run.Of<ActRevealed>().FindLast(a => true).FirstSeat, "3막 카드에 처음 달성한 좌석");
        }

        [Test]
        public void M5_막길_심장공물에서태홍을거쳐영웅으로게임끝()
        {
            var trail = SoloTrail.Heart;
            var run = Play(trail, r => r.Of<GameEnded>().Count > 0, afterSetup: s => trail.After(ctx, s));
            Assert.AreEqual(1, run.Of<HeartRestored>().Count, "시작의 사당 공물(3막)");
            var g = run.Of<GalacticBattle>();
            Assert.AreEqual(GalacticLeader.Cyrus, g[g.Count - 1].Leader);
            Assert.AreEqual(HosFlow.BossLineup, g[g.Count - 1].LineupCount);
            Assert.AreEqual(3, run.Of<ActGoalMet>().FindLast(x => true).Act);
            CollectionAssert.Contains(run.Asked, "Ending/EndingChoice");
            Assert.IsFalse(run.Of<EndingChosen>()[0].Traitor);
            Assert.AreEqual(GameEndReason.Hero, run.Of<GameEnded>()[0].Reason);
            Assert.IsTrue(run.Session.GameOver);
            TestContext.WriteLine($"막 길 heart: 답 {run.Answers} · 배틀 {run.Of<BattleStarted>().Count} · 물은 것 {string.Join(",", run.Asked)}");
        }

        // ── 운명 길(M5 「운명·응답 화면」 — SoloTrail.Fate·FateGiratina, PlayMode 흐름 시험·캡처가 화면으로 같은 길을 지난다) ──

        [Test]
        public void M5_운명길_6장째를버리고펄기아로순간이동하고디아루가3장_다음턴떨어진별()
        {
            var trail = SoloTrail.Fate;
            var run = Play(trail, r => r.Of<FateDealt>().Exists(d => d.Source.Kind == SourceKind.Rule && d.Source.Id == (int)RuleSource.FallenStar), afterSetup: s => trail.After(ctx, s));
            Assert.AreEqual(1, run.Of<FateDiscarded>().Count, "6장째를 버렸다(A12)");
            Assert.AreEqual(1, run.Of<Teleported>().Count, "펄기아 1장");
            var d3 = run.Of<FatePlayed>().Find(p => p.Card == FateCard.Dialga && p.Play3);
            Assert.IsNotNull(d3, "디아루가 3장");
            Assert.AreEqual((int)FateCard.Dialga, d3.WildcardAs, "아르세우스가 와일드카드로");
            Assert.IsTrue(run.Of<DiceFacesChanged>().Exists(c => c.Source.Kind == SourceKind.Fate), "다시 굴렸다(43행)");
            TestContext.WriteLine($"운명 길: 답 {run.Answers} · 물은 것 {string.Join(",", run.Asked)}");
        }

        [Test]
        public void M5_사당길_3장을뽑아선물과균열을처리하고펄기아가강림한다()
        {
            var trail = SoloTrail.Shrine;
            var run = Play(trail, r => r.Of<TurnStarted>().Count >= 2 && r.Of<GodDescended>().Count > 0, afterSetup: s => trail.After(ctx, s));
            var offered = run.Of<ShrineOffered>();
            Assert.AreEqual(1, offered.Count, "공물은 턴당 1번");
            Assert.IsTrue(offered[0].Extra, "여행 점수 7을 더 냈다");
            Assert.AreEqual(2, offered[0].Tier, "분홍 = 2단계");
            Assert.AreEqual(3, run.Of<ShrineCardsDrawn>()[0].CardKeys.Length, "3장");
            var gift = run.Of<ApricornsGained>().Find(g => g.Source.Kind == SourceKind.ShrineEvent);
            Assert.IsNotNull(gift, "뜻밖의 선물");
            Assert.AreEqual(2, gift.Changes[0].After - gift.Changes[0].Before, "검정 2개(2단계)");
            Assert.AreEqual((int)ApricornColor.Black, gift.Changes[0].Key);
            Assert.AreEqual((int)SinnohGod.Palkia, run.Of<GodDescended>()[0].God, "혼자 따르니 펄기아");
            Assert.IsFalse(run.Of<ShrineBlessing>().Exists(b => b.CardKey == ShrineRules.CardKey(ctx, ShrineRules.TranquilMoment)), "남은 한 장(평온한 순간)은 버린다");
            CollectionAssert.IsSubsetOf(new[] { "Reward/ShrinePick", "Reward/ShrineApricorns", "Simultaneous/Follower" }, run.Asked);
            var moves = run.Of<MoveAllowanceSet>();
            var last = moves[moves.Count - 1];
            Assert.AreEqual(last.Base + FractureRules.FollowerMoves, last.Total, "다음 턴: 추종자 이동력 +1");
            TestContext.WriteLine($"사당 길: 답 {run.Answers} · 물은 것 {string.Join(",", run.Asked)}");
        }

        [Test]
        public void M5_운명길_기라티나1장을굴리기전에낸다()
        {
            var trail = SoloTrail.FateGiratina;
            var run = Play(trail, r => r.Of<FatePlayed>().Count > 0, afterSetup: s => trail.After(ctx, s));
            Assert.AreEqual(FateCard.Giratina, run.Of<FatePlayed>()[0].Card);
            Assert.IsTrue(run.Of<HpChanged>().Exists(h => h.Source.Kind == SourceKind.Fate && h.Before - h.After == FateRules.SacrificeDamage), "희생 2(44행)");
        }
    }
}
