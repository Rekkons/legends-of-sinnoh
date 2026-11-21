using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M5 마감 답 반영 마(결정 80 N2 나 — 여럿 판 흐름 시험): 견본 그림으로만 지났던 여럿 판의 흐름을 좌석 여럿인 확인 판(<see cref="GameFlow.StartSeats"/> — 한 PC의 LocalSession,
    /// 남 좌석은 확인용 답 <see cref="GameFlow.AnswerSeat"/>)으로 실제로 돌고 판정까지 단정한다: ① 4인 나눔(평온한 순간) ② 2인 균열 동률 ③ 6인 선택 현황 ④ 4인 전설 이벤트
    /// ⑤ 3인 배신자 순서 ⑥ 남 좌석의 배틀을 관전(폼 바꾸기 · 응징) ⑦ 남의 배틀을 보는 동안 내 거래 응답의 [수락] → 줄 운명 카드 고르기. 내 좌석은 화면 함수로만 누른다. 네트워크(M7)는 없다.
    /// </summary>
    public sealed class MultiFlowTests
    {
        const int W = 1920, H = 1080;

        ScreenBoot boot;
        GameFlow flow;
        UiStage stage;
        RenderTexture rt;
        readonly List<string> errors = new List<string>();
        readonly List<GameEvent> events = new List<GameEvent>();

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
        }

        IEnumerator Boot()
        {
            errors.Clear();
            events.Clear();
            Application.logMessageReceived += Grab;
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            flow = boot.Flow;
            stage = boot.Root.Stage;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
        }

        IEnumerator Settle(Func<bool> until = null)
        {
            for (int i = 0; i < 400 && ((flow.Fx == null || !flow.Fx.Sequencer.IsEmpty) || (until != null && !until())); i++)
            {
                flow.Skip(100);
                yield return null;
            }
            flow.Skip(400);
            yield return null;
            yield return null;
        }

        // 그 모습이 될 때까지(연출이 남아 있어도) 시각을 보낸다
        IEnumerator Until(Func<bool> until, string what)
        {
            for (int i = 0; i < 600 && !until(); i++)
            {
                flow.Skip(100);
                yield return null;
            }
            Assert.That(until(), Is.True, what + " — 화면 " + (stage.Current != null ? stage.Current.Name : "보드") + " · 선택 " + (flow.Client.FirstChoice() != null ? flow.Client.FirstChoice().Kind + "/" + flow.Client.FirstChoice().Sub : "없음"));
        }

        [TearDown]
        public void Shut()
        {
            Application.logMessageReceived -= Grab;
            if (flow != null) flow.EventsSeen -= events.AddRange;
            var b = UnityEngine.Object.FindAnyObjectByType<ScreenBoot>();
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                rt = null;
            }
        }

        // 좌석 여럿 확인 판을 열고 내 턴(입력이 열린 때)까지 넘긴다 — 그 뒤의 이벤트를 모은다
        IEnumerator Seats(int seats, Action<GameState> prepare)
        {
            yield return Boot();
            flow.StartSeats(new SoloSettings(), seats, 20261116, 7, prepare);
            for (int i = 0; i < 6; i++) yield return null;
            yield return Settle();
            for (int k = 0; k < seats * 2 && flow.Client.View.ActiveSeat != flow.Client.Seat; k++)
            {
                Assert.That(flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == AnswerKind.EndTurn), Is.True, "남 좌석의 턴 종료");
                yield return Settle();
            }
            yield return Settle(() => flow.InputOpen && flow.Client.Choice(ChoiceKind.MapAction) != null);
            Assert.That(flow.Client.View.ActiveSeat, Is.EqualTo(flow.Client.Seat), "내 턴");
            flow.EventsSeen += events.AddRange;
        }

        // 내 좌석(0)이 풀밭 사당에서 분홍 규토리를 바친다(사당 판 [공물] → 색 → [바치기]) — 덱 맨 위는 prepare가 놓았다
        IEnumerator Offer()
        {
            Assert.That(flow.Client.Seat, Is.EqualTo(0), "확인 판의 내 좌석");
            Assert.That(flow.Hud.Press(BoardHud.Offer), Is.True, "[공물]");
            yield return null;
            var shrine = stage.Current as ShrineScreen;
            Assert.That(shrine, Is.Not.Null, "사당 판");
            Assert.That(shrine.PickColor((int)ApricornColor.Pink), Is.True);
            Assert.That(shrine.PressOffer(), Is.True, "[바치기]");
        }

        ShrineScreen Shrine => stage.Current as ShrineScreen;

        /// <summary>결정 186: 고대의 지혜로 배울 기술을 고른 뒤 바꿀 칸 — 원래 기술이 든 칩에 마우스를 올리면 그 기술의 이름 + 효과가 뜬다.</summary>
        [UnityTest]
        public IEnumerator M10_고대의지혜가르치기에서원래기술칩에마우스를올리면그기술의이름과효과가뜬다_결정186()
        {
            int given = -1;
            yield return Seats(2, st =>
            {
                SoloTrail.PrepareShrine(flow.Ctx, st, 0, ShrineRules.AncientWisdom);
                var deck = st.Decks[(int)DeckId.Moderate];
                var pile = st.Discards[(int)DeckId.Moderate];
                for (int i = 0; i < 12 && deck.Count > 0; i++)
                {
                    pile.Add(deck[deck.Count - 1]);
                    deck.RemoveAt(deck.Count - 1);
                }
                given = flow.Ctx.Cards.Pokemon[flow.Ctx.Deck(DeckId.Legendary).Cards[0].Card].Signature;   // 버려진 보통 카드의 기술과 겹치지 않는 기술
                st.Pokemon[st.Seats[0].Party[0]].Moves[0] = given;
            });
            yield return Offer();
            yield return Until(() => Shrine != null && Shrine.Shown == ShrineScreen.Mode.Move && Shrine.Open, "배울 기술 고르기");
            Assert.That(Shrine.PickOption(0) && Shrine.Confirm(), Is.True, "배울 기술");
            yield return Until(() => Shrine != null && Shrine.Shown == ShrineScreen.Mode.Teach && Shrine.Open, "바꿀 기술 칸");
            int tips = 0;
            for (int i = 0; i < Shrine.Options.Count; i++)
            {
                var a = Shrine.Options[i];
                if (a.Kind != AnswerKind.Pick || flow.Client.View.Pokemon[a.Target].Moves[a.Arg] != given) continue;
                EncounterFlowTests.TipIs(stage, stage.Root.Q("board-shrine-opt-" + (i + 1)), flow.Ctx, given);
                tips++;
            }
            Assert.That(tips, Is.EqualTo(1), "원래 기술이 든 칸 칩 하나");
        }

        /// <summary>결정 186: pokemon_breeder B 교배 기술 칸 — 기술이 든 칸 하나에 툴팁 하나(그 기술의 이름 + 효과 + 안내 한 줄), 진화형 기술 줄도 이름 + 효과.</summary>
        [UnityTest]
        public IEnumerator M10_교배기술칸에마우스를올리면지금기술의이름과효과와안내가뜬다_결정186()
        {
            int given = -1;
            yield return Seats(1, st =>
            {
                var seat = st.Seats[0];
                seat.TrainerCard = flow.Ctx.Cards.TrainerCardIds.IndexOf("pokemon_breeder");
                seat.TrainerCardTier = TrainerRules.Gold;
                seat.Jp = System.Math.Max(seat.Jp, 30);
                given = flow.Ctx.Cards.Pokemon[flow.Ctx.Deck(DeckId.Legendary).Cards[0].Card].Signature;
                st.Pokemon[seat.Party[0]].Moves[0] = given;
            });
            Assert.That(flow.OpenEvolve(), Is.True, "진화 창");
            yield return null;
            var evolve = stage.Current as EvolveScreen;
            Assert.That(evolve, Is.Not.Null);
            Assert.That(evolve.OpenEgg(evolve.Listed[0]), Is.True, "[교배 기술]");
            yield return null;
            var box = stage.Root.Q("board-evolve-egg-0-0");
            Assert.That(box, Is.Not.Null, "칸 1");
            EncounterFlowTests.TipIs(stage, box, flow.Ctx, given, stage.T("screen.evolve.egg.slot.tip"));
            var inner = box.Q(className: "sn-enc-move");
            Assert.That(inner, Is.Not.Null, "칸 1의 지금 기술 줄");
            stage.Tips.Show(inner);
            Assert.That(stage.Tips.Target, Is.Null, "칸 안의 기술 줄에는 따로 툴팁을 걸지 않는다(칸 하나에 하나)");
            stage.Tips.Hide();
            int to = evolve.EggsOf(evolve.EggFor)[0].Values[0];
            EncounterFlowTests.TipIs(stage, stage.Root.Q("board-evolve-egg-move-0").Q(className: "sn-enc-move"), flow.Ctx, flow.Ctx.Cards.Pokemon[to].Signature);
        }

        [UnityTest]
        public IEnumerator M5_4인판에서평온한순간의축복을고른좌석과나눈다()
        {
            yield return Seats(4, st => SoloTrail.PrepareShrine(flow.Ctx, st, 0, ShrineRules.TranquilMoment));
            yield return Offer();
            yield return Until(() => Shrine != null && Shrine.Shown == ShrineScreen.Mode.Share && Shrine.Open, "함께 받을 트레이너 고르기(4인 이상)");
            var seatsOffered = new List<int>();
            foreach (var a in Shrine.Options) seatsOffered.Add(a.Target);
            Assert.That(seatsOffered, Is.EquivalentTo(new[] { 1, 2, 3 }), "고를 수 있는 좌석 = 남 좌석 셋");
            int to = Shrine.Options[1].Target;
            Assert.That(Shrine.PickOption(1), Is.True);
            Assert.That(Shrine.Confirm(), Is.True, "[확정]");
            bool tagged = false;
            yield return Until(() =>
            {
                if (flow.Hud != null && flow.Hud.DidSeat >= 0) tagged = true;
                return flow.InputOpen && stage.Current == null;
            }, "나눔 뒤 보드로");
            var shared = events.FindAll(e => e is ShrineBlessing);
            Assert.That(shared.Exists(e => e.Seat == 0 && ((ShrineBlessing)e).From == 0), Is.True, "내가 축복을 받았다");
            Assert.That(shared.Exists(e => e.Seat == to && ((ShrineBlessing)e).From == 0), Is.True, "고른 좌석이 축복을 나눠 받았다");
            Assert.That(shared.FindAll(e => e.Seat != 0 && e.Seat != to), Is.Empty, "고르지 않은 좌석은 받지 않는다");
            Assert.That(tagged, Is.False, "내가 나눠 준 축복은 남 좌석 꼬리표가 아니다(사당 판이 그린다)");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M5_2인균열이동률이면뽑은좌석이강림할신을고른다()
        {
            yield return Seats(2, st => SoloTrail.PrepareShrine(flow.Ctx, st, 0, ShrineRules.CelestialFracture));
            yield return Offer();
            yield return Until(() => Shrine != null && Shrine.Shown == ShrineScreen.Mode.Follower && Shrine.Open, "추종자 고르기");
            Assert.That(flow.AnswerSeat(1, a => a.Kind == AnswerKind.Pick && a.Target == (int)SinnohGod.Dialga), Is.True, "좌석 2 = 디아루가");
            yield return Until(() => Shrine != null && Shrine.Open, "좌석 2의 답 뒤에도 판이 열려 있다");
            Assert.That(Shrine.Press(ChoiceAnswer.Pick((int)SinnohGod.Palkia)), Is.True, "나 = 펄기아");
            yield return Until(() => Shrine != null && Shrine.Shown == ShrineScreen.Mode.Tie && Shrine.Open, "동률(1 · 1) — 뽑은 내가 고른다");
            var gods = new List<int>();
            foreach (var a in Shrine.Options) gods.Add(a.Target);
            Assert.That(gods.Contains((int)SinnohGod.Dialga) && gods.Contains((int)SinnohGod.Palkia), Is.True, "동률인 두 신");
            Assert.That(gods.Contains((int)SinnohGod.Giratina), Is.False, "표가 없는 신은 고를 수 없다");
            Assert.That(WorkUi.Overflows(stage.Root.Q("board-shrine")), Is.Empty, "동률 판의 글이 넘치지 않는다");
            Assert.That(Shrine.Press(ChoiceAnswer.Pick((int)SinnohGod.Dialga)), Is.True, "디아루가를 강림시킨다");
            yield return Until(() => flow.InputOpen && stage.Current == null && flow.Hud.FollowerShown(1), "강림 뒤 보드로");
            Assert.That(events.Exists(e => e is FollowerTally), Is.True, "집계");
            var view = flow.Client.View;
            Assert.That(FractureRules.OriginGod(view, flow.Ctx), Is.EqualTo(SinnohGod.Dialga), "동률에서 고른 신이 강림했다");
            Assert.That(flow.Hud.FollowTone(0), Is.EqualTo(SinnohGod.Palkia), "내 칩 = 펄기아 색");
            Assert.That(flow.Hud.FollowTone(1), Is.EqualTo(SinnohGod.Dialga), "좌석 2 칩 = 디아루가 색");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M5_6인균열의선택현황은답한좌석마다선택완료로바뀐다()
        {
            yield return Seats(6, st => SoloTrail.PrepareShrine(flow.Ctx, st, 0, ShrineRules.CelestialFracture));
            yield return Offer();
            yield return Until(() => Shrine != null && Shrine.Shown == ShrineScreen.Mode.Follower && Shrine.Open, "추종자 고르기(6인)");
            string done = stage.T("screen.setup.seat.done"), picking = stage.T("screen.setup.seat.picking");
            var gods = new[] { SinnohGod.Palkia, SinnohGod.Dialga, SinnohGod.Palkia, SinnohGod.Giratina, SinnohGod.Palkia, SinnohGod.Giratina };
            for (int seat = 1; seat < 6; seat++)
            {
                int god = (int)gods[seat];
                Assert.That(flow.AnswerSeat(seat, a => a.Kind == AnswerKind.Pick && a.Target == god), Is.True, "좌석 " + (seat + 1) + "의 추종자");
                yield return Until(() => Shrine != null && Shrine.Open, "판이 열려 있다");
                yield return null;
                for (int k = 1; k < 6; k++)
                {
                    var badge = stage.Root.Q<Label>("board-shrine-seat-" + (k + 1) + "-state");
                    Assert.That(badge, Is.Not.Null, "좌석 줄 " + (k + 1));
                    Assert.That(badge.text, Is.EqualTo(k <= seat ? done : picking), "좌석 " + (k + 1) + "의 현황(답한 좌석 " + seat + ")");
                }
                Assert.That(flow.Client.View.Seats[seat].Follower, Is.Not.EqualTo(gods[seat]).Or.EqualTo(gods[seat]), "보기");
            }
            Assert.That(WorkUi.Overflows(stage.Root.Q("board-shrine")), Is.Empty, "6인 좌석 줄의 글이 넘치지 않는다");
            Assert.That(Shrine.Press(ChoiceAnswer.Pick((int)gods[0])), Is.True, "나 = 펄기아");
            yield return Until(() => flow.InputOpen && stage.Current == null && flow.Hud.FollowerShown(5), "공개 · 강림 뒤 보드로");
            var tally = (FollowerTally)events.Find(e => e is FollowerTally);
            Assert.That(tally, Is.Not.Null, "집계");
            Assert.That(FractureRules.OriginGod(flow.Client.View, flow.Ctx), Is.EqualTo(SinnohGod.Palkia), "가장 많은 신(펄기아 3)이 강림했다");
            for (int seat = 0; seat < 6; seat++) Assert.That(flow.Hud.FollowTone(seat), Is.EqualTo(gods[seat]), "좌석 " + (seat + 1) + " 칩의 신 색");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M5_4인전설이벤트는모든좌석에규토리를주고전설을사당에놓는다()
        {
            yield return Seats(4, st => SoloTrail.PrepareShrine(flow.Ctx, st, 0, "shrine_event.song_of_frost"));
            var before = new int[4];
            for (int s = 0; s < 4; s++) before[s] = flow.Client.View.Seats[s].Apricorns[(int)ApricornColor.Blue];
            yield return Offer();
            bool dark = false;
            yield return Until(() =>
            {
                if (flow.Fx != null && ShrineRows.Playing(flow.Fx.Sequencer, ShrineRows.Legend, out _) != null) dark = true;
                return dark && flow.Fx.Sequencer.IsEmpty && flow.BoardOpen;
            }, "전설 이벤트(330) 뒤");
            var legend = (LegendEvent)events.Find(e => e is LegendEvent);
            Assert.That(legend, Is.Not.Null, "전설 이벤트");
            Assert.That(legend.Placed, Is.True, "전설 토큰을 놓았다");
            var view = flow.Client.View;
            Assert.That(view.Tokens.Exists(t => t.Cell == legend.Cell), Is.True, "그 사당 칸에 토큰");
            int gained = view.Seats[0].Apricorns[(int)ApricornColor.Blue] - before[0];
            Assert.That(gained, Is.GreaterThan(0), "바친 좌석도 받았다");
            for (int s = 1; s < 4; s++)
                Assert.That(view.Seats[s].Apricorns[(int)ApricornColor.Blue] - before[s], Is.EqualTo(gained), "좌석 " + (s + 1) + "도 같은 수를 받았다(모든 좌석)");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M5_3인종막에서배신자가고른순서대로연전한다()
        {
            yield return Boot();
            flow.StartSeats(new SoloSettings(), 3, 20261116, 7, st => SoloTrail.PrepareEnding(flow.Ctx, st));
            flow.EventsSeen += events.AddRange;
            yield return Until(() => stage.Current is EndingScreen e && e.Asking && flow.BoardOpen, "종막 선택(S62)");
            var ending = (EndingScreen)stage.Current;
            Assert.That(ending.Pick(1), Is.True, "[배신자]");
            yield return Until(() => stage.Current is EndingScreen e && e.Ordering && flow.BoardOpen, "배신자의 순서 고르기(S63)");
            ending = (EndingScreen)stage.Current;
            Assert.That(WorkUi.Overflows(stage.Root.Q(ending.Name)), Is.Empty, "순서 고르기의 글이 넘치지 않는다");
            Assert.That(ending.PickSeat(2), Is.True, "좌석 3 먼저");
            Assert.That(ending.PickSeat(1), Is.True, "다음 좌석 2");
            Assert.That(ending.ConfirmOrder(), Is.True, "[확정]");
            yield return Until(() => events.Exists(e => e is BattleStarted), "첫 연전이 시작됐다");
            var set = (TraitorOrderSet)events.Find(e => e is TraitorOrderSet);
            Assert.That(set, Is.Not.Null, "순서 공개");
            Assert.That(set.Seats, Is.EqualTo(new[] { 2, 1 }), "고른 순서");
            Assert.That(flow.Client.View.Hos.Traitor, Is.EqualTo(0), "배신자 = 나");
            var first = (BattleStarted)events.Find(e => e is BattleStarted);
            Assert.That(first.Kind, Is.EqualTo(BattleKind.Traitor), "배신자 연전");
            var owners = new List<int>();
            foreach (var side in first.Sides) if (side != null) owners.Add(side.Owner);
            Assert.That(owners, Is.EquivalentTo(new[] { 0, 2 }), "첫 상대 = 먼저 고른 좌석 3");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        // 그 좌석에게 열린 배틀 선택에 답한다(기술 = 첫 기술 · 물음 = 넘기기 · 그 밖 = 첫 답) — 없으면 false
        // move ≥ 0 = 나와 있는 포켓몬이 그 기술을 가졌으면 그 기술 · 우선권 토큰은 쓴다(Accept)
        bool AnswerBattle(int seat, int move = -1)
        {
            return (move >= 0 && flow.AnswerSeat(seat, a => a.Kind == AnswerKind.UseMove && a.Target == move && a.Arg < 0))
                || flow.AnswerSeat(seat, a => a.Kind == AnswerKind.Accept)
                || flow.AnswerSeat(seat, a => a.Kind == AnswerKind.UseMove && a.Arg < 0)
                || flow.AnswerSeat(seat, a => a.Kind == AnswerKind.Decline)
                || flow.AnswerSeat(seat);
        }

        // 그 좌석의 파티를 keys(포켓몬 키:폼)로 바꾼다(스타팅 포켓몬은 뒤에)
        static void GiveTeam(Ctx ctx, GameState state, int seat, params string[] keys)
        {
            var st = state.Seats[seat];
            st.OpenSlots = st.Party.Length;
            int starter = st.Party[0];
            for (int i = 0; i < st.Party.Length; i++) st.Party[i] = -1;
            foreach (string key in keys) PokemonRules.AddToParty(st, PokemonRules.Spawn(state, ctx.Cards.PokemonIds.IndexOf(key), seat, (int)DeckId.Legendary));
            if (starter >= 0 && PokemonRules.PartyCount(st) < st.OpenSlots) PokemonRules.AddToParty(st, starter);
        }

        [UnityTest]
        public IEnumerator M5_남의배틀을보는동안거래의수락은줄운명카드를고르게한다()
        {
            // 좌석 셋: 좌석 2의 턴에 좌석 3(같은 신)이 나에게 거래를 제안하고(남의 턴의 거래 — 결정 75 B5 나), 응답이 열린 채 좌석 2가 자기 고향 마을의 조무래기와 싸운다 —
            // 배틀 중에는 새 제안을 낼 수 없고(TradeRules) 내 응답이 열려 있으면 내 [도전]이 서지 않으니, 배틀 화면 위에서 거래에 답하는 길은 이것이다
            int dialga = -1, palkia = -1;
            yield return Seats(3, st =>
            {
                var ctx = flow.Ctx;
                st.Seats[0].Follower = SinnohGod.Dialga;
                st.Seats[2].Follower = SinnohGod.Dialga;
                st.Seats[2].Apricorns[(int)ApricornColor.Blue] = 2;
                GiveTeam(ctx, st, 1, "483:2", "484:2");   // 조무래기의 첫 공격에 쓰러지지 않게
                var deck = st.Decks[(int)DeckId.ChangeOfFate];
                dialga = FateRules.CardKey(ctx, FateCard.Dialga);
                palkia = FateRules.CardKey(ctx, FateCard.Palkia);
                foreach (int key in new[] { dialga, palkia })
                    if (deck.Remove(key)) st.Seats[0].FateHand.Add(key);
            });
            int me = flow.Client.Seat;
            Assert.That(me, Is.EqualTo(0), "확인 판의 내 좌석");
            for (int k = 0; k < 3 && flow.Client.View.ActiveSeat != 1; k++)
            {
                if (flow.Client.View.ActiveSeat == me) Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");
                else Assert.That(flow.AnswerSeat(flow.Client.View.ActiveSeat, a => a.Kind == AnswerKind.EndTurn), Is.True);
                yield return Settle();
            }
            Assert.That(flow.Client.View.ActiveSeat, Is.EqualTo(1), "좌석 2의 턴");
            var offer = ChoiceAnswer.Of(AnswerKind.ProposeTrade, me);
            offer.Values.AddRange(new[] { 0, (int)TradeKind.Apricorn, (int)ApricornColor.Blue, 1, 1, (int)TradeKind.Fate, -1, 1 });
            Assert.That(flow.ProposeSeat(2, offer), Is.True, "좌석 3의 거래 제안");
            yield return Settle();
            var legal1 = new List<string>();
            flow.AnswerSeat(1, a => { legal1.Add(a.Kind + ":" + a.Target); return false; });
            Assert.That(flow.AnswerSeat(1, a => a.Kind == AnswerKind.Challenge && a.Target != me && a.Target != 2), Is.True, "응답이 열린 채 좌석 2가 조무래기에게 도전 — 좌석 2의 답 " + string.Join(",", legal1));
            BattleScreen screen = null;
            for (int guard = 0; guard < 600; guard++)
            {
                flow.Skip(100);
                yield return null;
                screen = stage.Current as BattleScreen;
                if (screen != null && screen.Open && screen.AskingRespond) break;
                if (flow.Fx != null && flow.Fx.Sequencer.InputOpen) AnswerBattle(1);
            }
            Assert.That(screen, Is.Not.Null, "좌석 2의 배틀을 내 화면에서 본다");
            Assert.That(screen.AskingRespond, Is.True, "배틀 독의 거래 응답");
            Assert.That(screen.PressRespond(true), Is.True, "[수락]");
            yield return null;
            yield return null;
            var cards = screen.Over as TradePickScreen;
            Assert.That(cards, Is.Not.Null, "배틀 화면 위에 줄 카드 고르는 창");
            Assert.That(flow.Top, Is.SameAs(cards));
            Assert.That(WorkUi.Overflows(stage.Root.Q("board-trade-pick")), Is.Empty, "고르는 창의 글이 넘치지 않는다");
            Assert.That(stage.Root.Q<Button>("board-trade-pick-accept").enabledSelf, Is.False, "고르기 전에는 [수락]이 꺼져 있다");
            Assert.That(cards.Pick(flow.Client.View.Seats[me].FateHand.IndexOf(palkia)), Is.True, "펄기아를 고른다");
            yield return null;
            Assert.That(cards.Accept(), Is.True, "[수락]");
            for (int guard = 0; guard < 100 && screen.Over != null; guard++)
            {
                flow.Skip(100);
                yield return null;
            }
            Assert.That(events.Exists(e => e is TradeDone), Is.True, "거래가 이루어졌다");
            var view = flow.Client.View;
            Assert.That(view.Seats[me].FateHand.Contains(palkia), Is.False, "고른 카드를 줬다");
            Assert.That(view.Seats[me].FateHand.Contains(dialga), Is.True, "고르지 않은 카드는 남는다");
            Assert.That(screen.Over, Is.Null, "고르는 창이 닫히고 배틀로");
            Assert.That(stage.Current, Is.SameAs(screen));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M5_남좌석의배틀을관전하며응징과결과창을본다()
        {
            // 좌석 2: 체리꼬(시그니처로 폼을 바꾼다 — 79행) · 디아루가 · 펄기아. 자기 고향 마을의 조무래기와 싸운다(응징이 쌓인다 — 74행).
            // 이 판(시드 고정)에서는 조무래기가 먼저 쳐 체리꼬가 기술을 쓰기 전에 쓰러진다 — 폼 바꾸기는 일어났을 때만 단정한다(응징은 늘 쌓인다)
            yield return Seats(2, st => GiveTeam(flow.Ctx, st, 1, "421:1", "483:2", "484:2"));
            int me = flow.Client.Seat, other = 1 - me;
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");
            yield return Until(() => flow.Client.View.ActiveSeat == other && flow.BoardOpen, "좌석 2의 턴");
            Assert.That(flow.AnswerSeat(other, a => a.Kind == AnswerKind.Challenge && a.Target != me), Is.True, "좌석 2가 조무래기에게 도전");
            bool opened = false, form = false, stored = false, notMine = false, formStep = false, thrown = false, dockChecked = false;
            BattleScreen screen = null;
            for (int guard = 0; guard < 3000; guard++)
            {
                flow.Skip(100);
                yield return null;
                screen = stage.Current as BattleScreen;
                if (screen != null)
                {
                    opened = true;
                    if (flow.Battle != null && !flow.Battle.Mine) notMine = true;
                    if (screen.FormShown) form = true;
                    if (screen.Intro.Trainer)
                    {
                        thrown = true;   // 배틀 시작의 첫 출전: 그 좌석의 주인공 뒷모습이 던진다(결정 99 ② — 보는 좌석의 주인공이 아니다)
                        Assert.That(screen.ThrowerSeat, Is.EqualTo(other), "관전: 뒷모습은 배틀하는 좌석의 주인공");
                        Assert.That(screen.Painted.Back, Is.Not.Null, "관전: DS에 뒷모습");
                        Assert.That(screen.Painted.Back.Key, Does.Contain(flow.Protagonist(other)), "관전: 그 좌석 주인공의 뒷모습 시트");
                    }
                    if (flow.Fx != null && ResultRows.Playing(flow.Fx.Sequencer, ResultRows.Form) != null) formStep = true;
                    if (screen.RetributionShown > 0) stored = true;
                    if (screen.ShowsResult) break;
                    if (screen.Moves.Count > 0)   // 남의 배틀의 기술 줄은 보기 전용(결정 120 · T12 — 옛 「관전하는 좌석에는 기술 줄이 없다」)
                    {
                        Assert.That(screen.WatchMoves, Is.True, "관전의 기술 줄 = 보기 전용");
                        Assert.That(screen.PressMove(0), Is.False, "관전하는 좌석은 기술을 누를 수 없다");
                    }
                    if (stage.Root.Q("battle-side") != null)
                    {
                        // 관전 독(M7 · [가정 #400]): [가방] · [기권]은 없고 눌러도 아무 일 없다 — [정비]는 그대로
                        Assert.That(stage.Root.Q("battle-bag"), Is.Null, "관전 독에 [가방]이 없다");
                        Assert.That(stage.Root.Q("battle-forfeit"), Is.Null, "관전 독에 [기권]이 없다");
                        Assert.That(stage.Root.Q("battle-maint"), Is.Not.Null, "관전 독에도 [정비]는 있다");
                        Assert.That(screen.BagOn || screen.ForfeitOn, Is.False);
                        int commands = flow.Session.CommandCount;
                        Assert.That(screen.OpenBag(), Is.False, "관전 중 [가방]을 열 수 없다");
                        Assert.That(screen.PressForfeit(), Is.False, "관전 중 기권할 수 없다");
                        Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "관전 독이 명령을 내지 않았다");
                        dockChecked = true;
                    }
                }
                if (flow.Fx != null && flow.Fx.Sequencer.InputOpen) AnswerBattle(other, flow.Ctx.Cards.MoveIds.IndexOf("gather_sunlight"));
            }
            Assert.That(opened, Is.True, "남 좌석의 배틀을 내 화면에서 본다");
            Assert.That(notMine, Is.True, "내가 당사자가 아닌 배틀");
            Assert.That(dockChecked, Is.True, "관전 독의 단추를 봤다");
            Assert.That(thrown, Is.True, "남의 배틀 시작에도 그 좌석의 뒷모습이 볼을 던진다(결정 99 ②)");
            Assert.That(events.Exists(e => e is BattleStarted), Is.True);
            if (events.Exists(e => e is FormChanged f && f.Temporary)) Assert.That(form, Is.True, "폼 바꾸기(79행)를 관전 쪽에서도 그린다 — 790 단계 재생 " + formStep);
            Assert.That(events.Exists(e => e is RetributionStored), Is.True, "응징이 쌓였다(74행)");
            Assert.That(stored, Is.True, "쌓인 응징을 관전 쪽 기둥에도 그린다");
            Assert.That(screen != null && screen.ShowsResult, Is.True, "결과 창");
            Assert.That(screen.Confirm(), Is.True, "[확인]");
            yield return Settle();
            Assert.That(stage.Current is BattleScreen, Is.False, "배틀 화면이 닫혔다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M7_관전배틀의기둥은싸우는좌석을그리고내운명단추는없다()
        {
            // M7 마감 감사 1: 남의 배틀을 볼 때 기둥 「내 쪽」이 내 좌석(이름 · 얼굴 · 대기 · [운명의 변화])으로 그려져 나와 있는 포켓몬(싸우는 좌석)과 섞였다.
            // 좌석 2(포켓몬 넷)가 조무래기와 싸우고 나(좌석 1 — 스타팅 하나)는 본다: 기둥 = 좌석 2의 이름 · 얼굴 · 대기, 내 [운명의 변화] 없음, 결과 창 = 「좌석 2의 승리!/패배…」
            yield return Seats(2, st => GiveTeam(flow.Ctx, st, 1, "421:1", "483:2", "484:2"));
            int me = flow.Client.Seat, other = 1 - me;
            Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "[턴 종료]");
            yield return Until(() => flow.Client.View.ActiveSeat == other && flow.BoardOpen, "좌석 2의 턴");
            Assert.That(flow.AnswerSeat(other, a => a.Kind == AnswerKind.Challenge && a.Target != me), Is.True, "좌석 2가 조무래기에게 도전");
            bool column = false, bench = false;
            int roster = 0;
            BattleScreen screen = null;
            for (int guard = 0; guard < 3000; guard++)
            {
                flow.Skip(100);
                yield return null;
                screen = stage.Current as BattleScreen;
                if (screen != null && screen.InfoSeat >= 0)
                {
                    Assert.That(flow.Battle.Mine, Is.False, "내가 당사자가 아닌 배틀");
                    Assert.That(screen.InfoSeat, Is.EqualTo(other), "기둥 내 쪽 = 싸우는 좌석");
                    var block = stage.Root.Q("battle-info-me");
                    var face = stage.Root.Q("battle-me-face");
                    Assert.That(face.Q<Label>(className: "sn-face-seat").text, Is.EqualTo((other + 1).ToString()), "얼굴 상자의 좌석 번호");
                    Assert.That(FaceBox.Shown(face), Is.EqualTo(flow.Protagonist(other)), "얼굴 = 싸우는 좌석의 주인공");
                    Assert.That(block.Query<Label>().ToList().Exists(l => l.text == flow.SeatName(other)), Is.True, "이름 = 싸우는 좌석");
                    Assert.That(block.Query<Label>().ToList().Exists(l => l.text == flow.SeatName(me)), Is.False, "내 이름이 섞이지 않는다");
                    Assert.That(stage.Root.Q(BattleScreen.FateButton), Is.Null, "남의 배틀에는 내 [운명의 변화]가 없다");
                    Assert.That(screen.FateOn, Is.False);
                    column = true;
                    roster = System.Math.Max(roster, screen.RosterCount);   // 결정 201 ①: 남의 갤럭시단 배틀에도 상대 명단 줄
                    var active = screen.ShownMon(flow.Battle.MySide);
                    var box = stage.Root.Q("battle-bench");
                    if (active.Exists && box != null)
                    {
                        int want = 0;
                        foreach (int id in flow.Battle.Source.Seats[other].Party)
                            if (id >= 0 && id != active.Id && flow.Battle.MonOf(id).Exists) want++;
                        Assert.That(want, Is.GreaterThan(1), "좌석 2의 대기 포켓몬이 여럿(내 파티는 하나뿐 — 섞이면 갈린다)");
                        Assert.That(box.childCount - 1, Is.EqualTo(want), "대기 = 싸우는 좌석의 파티(머리 줄 빼고)");
                        bench = true;
                    }
                    if (screen.ShowsResult) break;
                }
                if (flow.Fx != null && flow.Fx.Sequencer.InputOpen) AnswerBattle(other, flow.Ctx.Cards.MoveIds.IndexOf("gather_sunlight"));
            }
            Assert.That(column, Is.True, "관전 배틀의 기둥을 봤다");
            Assert.That(bench, Is.True, "대기 줄을 봤다");
            Assert.That(roster, Is.EqualTo(BattleFlow.GruntLineup), "관전 배틀에도 상대 명단 줄(조무래기 둘 — 결정 201 ①)");
            Assert.That(screen != null && screen.ShowsResult, Is.True, "결과 창");
            string title = stage.Root.Q<Label>("battle-result-title").text.Replace("⁠", "");   // Kit.Wrapped의 낱말 잇기 글자를 뺀다
            string want2 = stage.T(flow.Battle.Drawn ? "screen.battle.result.draw" : flow.Battle.Won ? "screen.battle.result.win.seat" : "screen.battle.result.lose.seat").Replace("{name}", flow.SeatName(other));
            Assert.That(title, Is.EqualTo(KoJosa.Fix(want2)),"결과 창 머리 = 싸운 좌석의 승패(「승리!」가 나인 듯 보이지 않게)");
            Assert.That(screen.Confirm(), Is.True, "[확인]");
            yield return Settle();
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityTest]
        public IEnumerator M9_4인달빛공터의바꿔치기는_있던전설이빛으로흩어진뒤새전설이선다()
        {
            const string Cresselia = "488:0", Darkrai = "491:0";
            yield return Seats(4, st =>
            {
                // 크레세리아 토큰이 선 달빛공터 · 덱 맨 위 「솟구치는 그림자」(다크라이가 크레세리아를 바꿔 치운다)
                SoloTrail.PrepareShrine(flow.Ctx, st, 0, "shrine_event.shadow_emerges");
                int def = flow.Ctx.Cards.PokemonIds.IndexOf(Cresselia), cell = flow.Ctx.Fixtures.Fixtures[flow.Ctx.Fixtures.Ids.IndexOf("legendary_shrine.moonglade_shrine")].Cell;
                var token = st.Tokens.Find(t => t.Kind == TokenKind.Legend && t.Def == def);
                if (token == null) st.Tokens.Add(new TokenState { Kind = TokenKind.Legend, Def = def, Cell = cell, FaceUp = true });
                else token.Cell = cell;
            });
            Assert.That(flow.FigureShown(Cresselia, out float whole), Is.True, "달빛공터에 크레세리아 피규어");
            Assert.That(whole, Is.EqualTo(1f).Within(0.001f));
            Assert.That(flow.FigureShown(Darkrai, out _), Is.False, "다크라이는 아직 없다");
            yield return Offer();
            bool dark = false, shrinking = false, motes = false, early = false;
            yield return Until(() =>
            {
                if (flow.Fx != null && ShrineRows.Playing(flow.Fx.Sequencer, ShrineRows.Legend, out int ms) != null)
                {
                    dark = true;
                    bool on = flow.FigureShown(Cresselia, out float size);
                    if (on && size < 0.95f) shrinking = true;
                    if (flow.Hud != null && flow.Hud.ShrineFx.MotesShown > 0) motes = true;
                    if (flow.FigureShown(Darkrai, out _) && ms < LegendBeats.FallFromMs) early = true;
                }
                return dark && flow.Fx.Sequencer.IsEmpty && flow.BoardOpen;
            }, "전설 이벤트(330) 뒤");
            var legend = (LegendEvent)events.Find(e => e is LegendEvent);
            Assert.That(legend, Is.Not.Null, "전설 이벤트");
            Assert.That(legend.ReplacedSpecies, Is.GreaterThanOrEqualTo(0), "바꿔치기");
            Assert.That(shrinking, Is.True, "크레세리아 피규어가 줄어들며 흩어졌다(M9 P37)");
            Assert.That(motes, Is.True, "빛 알갱이가 보였다");
            Assert.That(early, Is.False, "새 전설은 어두워진 뒤(500ms)부터 내려온다");
            Assert.That(flow.FigureShown(Cresselia, out _), Is.False, "크레세리아는 사라졌다");
            Assert.That(flow.FigureShown(Darkrai, out float landed), Is.True, "다크라이가 섰다");
            Assert.That(landed, Is.EqualTo(1f).Within(0.001f), "제 크기");
            Assert.That(flow.Hud.ShrineFx.MotesShown, Is.EqualTo(0), "알갱이는 걷혔다");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        /// <summary>
        /// M9 다듬기 P22(결정 116): 동등한 교환의 교환할 야생 카드 목록이 길면(한 쪽 15장을 넘으면) 이름 찾기 · 타입 거르기가 서고 쪽 수가 따라 준다 — 찾기 칸은 글을 치는 동안 다시 만들어지지 않고,
        /// 걸러져 안 보이는 카드로는 [확정]하지 못한다. 야생 카드에 마우스를 올리면 그 카드의 모습 · 스펙 카드가 뜬다. 거르기 · 마우스 오버는 답을 내지 않고, 찾은 카드를 골라 [확정]하면 교환된다.
        /// </summary>
        [UnityTest]
        public IEnumerator M9_사당교환의야생카드긴목록을찾고거르고마우스오버카드를본다()
        {
            yield return Seats(2, st => SoloTrail.PrepareShrine(flow.Ctx, st, 0, ShrineRules.EquivalentExchange));
            yield return Offer();
            yield return Until(() => Shrine != null && Shrine.Shown == ShrineScreen.Mode.Exchange && Shrine.Open, "교환할 포켓몬 고르기");
            Assert.That(Shrine.Filtering, Is.False, "짧은 목록(내 포켓몬)에는 찾기 줄이 없다");
            Assert.That(stage.Root.Q("board-shrine-find"), Is.Null);
            Assert.That(Shrine.PickOption(0) && Shrine.Confirm(), Is.True, "교환할 포켓몬");
            yield return Until(() => Shrine != null && Shrine.Shown == ShrineScreen.Mode.ExchangeCard && Shrine.Open, "교환할 야생 카드 고르기");
            var shrine = Shrine;
            int all = shrine.Options.Count, last = all - 1;
            int commands = flow.Session.CommandCount;
            Assert.That(all, Is.GreaterThan(ShrineScreen.PageSize), "긴 목록");
            Assert.That(shrine.Filtering, Is.True, "찾기 줄이 섰다");
            Assert.That(shrine.PageRows, Is.EqualTo(ShrineScreen.FindPageSize));
            Assert.That(shrine.Pages, Is.EqualTo((all + ShrineScreen.FindPageSize - 1) / ShrineScreen.FindPageSize));
            Assert.That(shrine.ShownOptions.Count, Is.EqualTo(all));
            var field = stage.Root.Q<TextField>("board-shrine-find");
            Assert.That(field, Is.Not.Null, "찾기 칸");
            Assert.That(stage.Root.Q("board-shrine-opt-" + all), Is.Null, "마지막 카드는 첫 쪽에 없다");

            // 이름(화면 이름 전체 → 영문 이름 대문자)으로 찾는다: 마지막 쪽의 카드가 첫 쪽에 온다
            int target = shrine.Options[last].Target;
            var def = flow.Ctx.Cards.Pokemon[target];
            Assert.That(shrine.Find(stage.T("pokemon." + def.Key)), Is.True);
            Assert.That(stage.Root.Q<TextField>("board-shrine-find"), Is.SameAs(field), "찾는 동안 칸은 다시 만들어지지 않는다");
            Assert.That(shrine.ShownOptions, Does.Contain(last));
            Assert.That(shrine.ShownOptions.Count, Is.LessThan(all));
            Assert.That(shrine.Page, Is.EqualTo(0));
            Assert.That(stage.Root.Q("board-shrine-opt-" + all), Is.Not.Null, "찾은 카드가 보인다");
            Assert.That(shrine.Find(def.Name.ToUpperInvariant()), Is.True);
            Assert.That(shrine.ShownOptions, Does.Contain(last), "영문 이름 · 대소문자 무시");

            // 고른 카드가 걸러져 사라지면 [확정]하지 못한다
            Assert.That(shrine.PickOption(last), Is.True);
            Assert.That(stage.Root.Q<Button>("board-shrine-go").enabledSelf, Is.True);
            Assert.That(shrine.Search, Is.EqualTo(def.Name.ToUpperInvariant()), "고른 뒤에도 찾는 글은 그대로");
            Assert.That(shrine.Find("zzzz"), Is.True);
            Assert.That(shrine.ShownOptions, Is.Empty);
            Assert.That(stage.Root.Q("board-shrine-none"), Is.Not.Null, "맞는 카드가 없다는 글");
            Assert.That(stage.Root.Q<Button>("board-shrine-go").enabledSelf, Is.False);
            Assert.That(shrine.Confirm(), Is.False, "안 보이는 카드로는 확정하지 못한다");
            Assert.That(shrine.Find(""), Is.True);
            Assert.That(shrine.ShownOptions.Count, Is.EqualTo(all), "빈 글 = 전체");

            // 타입 거르기: 그 타입의 카드만 · 같은 칩을 다시 누르면 풀린다
            var type = def.Types[0];
            Assert.That(shrine.PickType(type), Is.True);
            Assert.That(shrine.ShownOptions.Count, Is.InRange(1, all - 1));
            foreach (int i in shrine.ShownOptions) Assert.That(flow.Ctx.Cards.Pokemon[shrine.Options[i].Target].Types, Does.Contain(type));
            Assert.That(stage.Root.Q("board-shrine-type-" + type.ToString().ToLowerInvariant()).ClassListContains("sn-shrine-chip-picked"), Is.True);
            Assert.That(shrine.PickType(type), Is.True);
            Assert.That(shrine.TypeFilter, Is.Null);
            Assert.That(shrine.ShownOptions.Count, Is.EqualTo(all));

            // 마우스 오버 카드: 그 야생 카드의 모습 · 스펙
            Assert.That(shrine.HoverMon(0), Is.True);
            yield return null;
            Assert.That(shrine.MonShown, Is.EqualTo(shrine.Options[0].Target));
            var tip = stage.Root.Q("mon-tip");
            Assert.That(tip, Is.Not.Null);
            Assert.That(tip.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(tip.Q("mon-tip-rate"), Is.Not.Null, "포획률");
            Assert.That(tip.Q("mon-tip-move"), Is.Not.Null, "고유 기술");
            Assert.That(shrine.HoverMon(last), Is.False, "지금 쪽에 없는 줄");
            Assert.That(shrine.MonShown, Is.EqualTo(-1));
            Assert.That(flow.Session.CommandCount, Is.EqualTo(commands), "찾기 · 거르기 · 마우스 오버는 답을 내지 않는다");

            // 찾은 카드를 골라 [확정] → 교환
            Assert.That(shrine.Find(stage.T("pokemon." + def.Key)), Is.True);
            Assert.That(shrine.PickOption(last), Is.True);
            Assert.That(shrine.Confirm(), Is.True, "[확정]");
            yield return Until(() => flow.InputOpen && stage.Current == null, "교환 뒤 보드로");
            Assert.That(stage.Root.Q("mon-tip"), Is.Null, "판이 닫히면 마우스 오버 카드도 걷힌다");
            Assert.That(flow.Typing, Is.False);
            var view = flow.Client.View;
            bool got = false;
            foreach (int id in view.Seats[0].Party) got |= id >= 0 && view.Pokemon[id].Def == target;
            Assert.That(got, Is.True, "찾아 고른 야생 카드가 파티에 들어왔다");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
