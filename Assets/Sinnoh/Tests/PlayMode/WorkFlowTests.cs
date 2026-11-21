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
    /// 제작·은행·진화 화면(M5 — S26 제작 창 · S24 은행상회 · S27 진화 창, 04 §8-8 18·36·37·39행): 사람 좌석이 화면의 함수로만 누르는 길.
    /// 손본 판(<see cref="SoloTrail.Work"/> — 규토리 일곱 색 셋씩 · 여행 점수 20 · 파티 첫 칸 이브이)의 첫 턴에 보드 HUD [제작]·[마을]·[진화]로
    /// 몬스터볼 · 자극적인 스튜를 만들고, 은행상회 한 줄을 거래하고, 스타팅 포켓몬을 진화시켜 옛 기술을 가르치지 않고, 이브이를 갈래에서 골라 진화시켜 옛 기술을 가르친다.
    /// 연출은 그 창이 그린다(대기열의 그 시각에 창이 그 행을 그리는지). 받을 화면이 없는 선택은 0번이다. Gen 겉모양·무대 그림이 있어야 돈다.
    /// </summary>
    public sealed class WorkFlowTests
    {
        static IEnumerator Open(List<string> errors)
        {
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
        }

        // 글자 자리 단언(1920×1080 — 1080 벌): 그 창 안의 글이 잘리거나 칸 밖으로 넘치거나 옆 요소에 덮이지 않는다(WorkUi.Overflows)
        static IEnumerator Fits(UiStage stage, string name)
        {
            for (int i = 0; i < 3; i++) yield return null;
            var bad = WorkUi.Overflows(stage.Root.Q(name));
            Assert.That(bad, Is.Empty, name + ": " + string.Join(" / ", bad));
        }

        // 화면 대기열이 비고 클릭 막기가 지날 때까지
        static void Settle(GameFlow flow)
        {
            for (int i = 0; i < 200 && !flow.BoardOpen; i++) flow.Skip(100);
        }

        [UnityTest]
        public IEnumerator M5_사람좌석이제작창은행상회진화창으로만들고바꾸고진화시킨다()
        {
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }
            Application.logMessageReceived += Grab;
            RenderTexture rt = null;
            try
            {
                yield return Open(errors);
                var boot = Object.FindAnyObjectByType<ScreenBoot>();
                Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
                Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);   // 1080 벌로 배치한다(글자 자리 단언)
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                FlowPilot.Start(flow, SoloTrail.Work);
                yield return null;
                flow.Skip(flow.OpeningMs);
                for (int i = 0; i < 200 && !flow.InputOpen; i++) flow.Skip(100);
                Assert.That(flow.InputOpen, Is.True, "첫 턴: 입력 열림");
                var client = flow.Client;
                var ctx = flow.Ctx;
                int seat = client.Seat;
                var hud = flow.Hud;
                Assert.That(hud.Shows(BoardHud.Craft), Is.True, "제작 답이 있으면 [제작]");
                Assert.That(hud.Shows(BoardHud.Evolve), Is.True, "진화 답이 있으면 [진화]");
                Assert.That(hud.Shows(BoardHud.Village), Is.True);
                Assert.That(stage.Root.Q("board-hud-buttons").Q(className: UiStage.OffClass), Is.Null, "흐린(준비 중) 단추는 없다");

                // ── S26 제작 창: 몬스터볼(36행) — 창이 그대로 있고 그 창이 연출을 그린다 ──
                Assert.That(hud.Press(BoardHud.Craft), Is.True);
                yield return null;
                var craft = stage.Current as CraftScreen;
                Assert.That(craft, Is.Not.Null, "[제작] → 제작 창");
                yield return Fits(stage, craft.Name);
                int ball = craft.ItemOf("item.poke_ball"), zesty = craft.ItemOf(CraftRules.ZestyStew);
                Assert.That(craft.CanMake(ball), Is.True);
                int balls = client.View.Seats[seat].Balls[(int)Ball.Regular], red = client.View.Seats[seat].Apricorns[(int)ApricornColor.Red];
                Assert.That(craft.PressMake(ball), Is.True);
                stage.Tweens.Advance(CraftBeats.FlashAt + 40);
                Assert.That(craft.Playing, Is.EqualTo(ball), "36행: 만든 줄을 그 창이 그린다");
                Assert.That(stage.Root.Q("board-craft-plus-poke_ball").style.opacity.value, Is.GreaterThan(0f), "「+1」");
                Settle(flow);
                Assert.That(stage.Current, Is.SameAs(craft), "만든 뒤에도 창은 그대로");
                Assert.That(client.View.Seats[seat].Balls[(int)Ball.Regular], Is.EqualTo(balls + 1));
                Assert.That(client.View.Seats[seat].Apricorns[(int)ApricornColor.Red], Is.EqualTo(red - 1));

                // 자극적인 스튜(37행): 이동력 +3 · 같은 스튜는 턴당 1회
                int moves = client.View.Seats[seat].MovesLeft;
                Assert.That(craft.PressMake(zesty), Is.True);
                stage.Tweens.Advance(1500 + StewBeats.BadgeAt + 100);
                Assert.That(craft.Playing, Is.EqualTo(zesty), "37행: 스튜 그릇 · 효과 꼬리표");
                Settle(flow);
                Assert.That(client.View.Seats[seat].MovesLeft, Is.EqualTo(moves + CraftRules.ZestyMoves));
                Assert.That(craft.CanMake(zesty), Is.False, "같은 스튜는 턴당 1회");
                flow.CloseCraft();
                yield return null;
                Assert.That(stage.Current, Is.Null);

                // ── S24 은행상회(마을 창): 한 줄 거래(18행) — 거래 동안은 옛 카드, 뒤집기 뒤 새 맨 위 카드 ──
                Settle(flow);
                Assert.That(hud.Press(BoardHud.Village), Is.True);
                yield return null;
                var village = stage.Current as VillageScreen;
                Assert.That(village, Is.Not.Null);
                var bank = village.Bank;
                Assert.That(bank, Is.Not.Null, "마을 창에 은행상회 칸");
                yield return Fits(stage, village.Name);
                int card = BankRules.Card(client.View);
                Assert.That(bank.ShownCard, Is.EqualTo(card));
                int rows = bank.CanTrade(BankRules.Top) ? BankRules.Top : BankRules.Bottom;
                Assert.That(bank.CanTrade(rows), Is.True, "규토리 셋씩이면 한 줄은 된다");
                int commands = flow.Session.CommandCount;
                Assert.That(bank.Press(rows), Is.True);
                stage.Tweens.Advance(BankBeats.GetFromMs + 100);
                Assert.That(bank.ShownCard, Is.EqualTo(card), "거래하는 동안은 거래한 카드");
                Settle(flow);
                Assert.That(flow.Session.CommandCount, Is.EqualTo(commands + 1));
                Assert.That(bank.ShownCard, Is.EqualTo(BankRules.Card(client.View)), "섞은 뒤의 맨 위 카드");
                Assert.That(bank.Why, Is.EqualTo("screen.village.used"), "턴당 1회");
                Assert.That(bank.CanTrade(BankRules.Top) || bank.CanTrade(BankRules.Bottom) || bank.CanTrade(BankRules.Both), Is.False);
                flow.CloseVillage();
                yield return null;

                // ── S27 진화 창: 스타팅 포켓몬(39행 DS 연출) → 옛 기술은 가르치지 않음 ──
                Settle(flow);
                Assert.That(hud.Press(BoardHud.Evolve), Is.True);
                yield return null;
                var evolve = stage.Current as EvolveScreen;
                Assert.That(evolve, Is.Not.Null, "[진화] → 진화 창");
                yield return Fits(stage, evolve.Name);
                Assert.That(evolve.Listed.Count, Is.EqualTo(2), "이브이 · 스타팅 포켓몬");
                int eevee = evolve.Listed[0], starter = evolve.Listed[1];
                int starterDef = client.View.Pokemon[starter].Def;
                int jp = client.View.Seats[seat].Jp;
                Assert.That(evolve.PressEvolve(starter), Is.True);
                yield return null;
                Assert.That(evolve.Now, Is.EqualTo(EvolveScreen.Mode.Show), "39행: 진화 창이 DS로 그린다");
                Assert.That(stage.Root.Q("board-evolve-ds"), Is.Not.Null);
                stage.Tweens.Advance(2500);
                Assert.That(evolve.Pose.White, Is.EqualTo(1f), "흰 실루엣");
                Assert.That(flow.CloseEvolve, Throws.Nothing);
                Assert.That(stage.Current, Is.SameAs(evolve), "연출 동안은 닫히지 않는다");
                Settle(flow);
                Assert.That(client.View.Pokemon[starter].Def, Is.Not.EqualTo(starterDef), "진화했다");
                int cost = ctx.Cards.Pokemon[starterDef].EvolveCost.Value;
                Assert.That(client.View.Seats[seat].Jp, Is.EqualTo(jp - cost));
                if (evolve.Now == EvolveScreen.Mode.Teach)
                {
                    Assert.That(evolve.PressTeach(-1), Is.True, "[가르치지 않기]");
                    yield return null;
                    Settle(flow);
                }
                Assert.That(evolve.Now, Is.EqualTo(EvolveScreen.Mode.Pick));

                // ── 이브이: 갈래(R14) → 고른 모습 → 옛 기술을 칸 1에 가르침 ──
                Assert.That(evolve.PressEvolve(eevee), Is.True);
                yield return null;
                Assert.That(evolve.Now, Is.EqualTo(EvolveScreen.Mode.Branch), "갈래가 여럿이면 고른다");
                yield return Fits(stage, evolve.Name);
                Assert.That(evolve.Branches.Count, Is.EqualTo(ctx.Cards.Pokemon[client.View.Pokemon[eevee].Def].EvolveInto.Count));
                int to = evolve.Branches[1].Target;
                int eeveeDef = client.View.Pokemon[eevee].Def;
                Assert.That(evolve.PressBranch(1), Is.False, "클릭 막기(300ms) 동안은 누르지 못한다");
                Settle(flow);
                Assert.That(evolve.PressBranch(1), Is.True);
                yield return null;
                Assert.That(evolve.Now, Is.EqualTo(EvolveScreen.Mode.Show));
                // 결정 84 L11: 스페이스 바(GameFlow.SkipKey) = 오가는 중에 착지로 건너뛴다 — 진화 뒤 모습의 제 색 + 「축하합니다!」
                stage.Tweens.Advance(1500);
                Assert.That(evolve.Pose.White, Is.EqualTo(1f), "흰 실루엣이 오가는 중");
                Assert.That(stage.Root.Q("board-evolve-skip"), Is.Not.Null, "스페이스 바 안내");
                Assert.That(flow.SkipKey(), Is.True);
                Assert.That(evolve.PoseMs, Is.EqualTo(EvolveBeats.SkipAt(PlayTiming.EvolveMs)));
                Assert.That(evolve.Pose.To && evolve.Pose.Message == 2 && evolve.Pose.White == 0f, Is.True, "착지 모습");
                Assert.That(flow.SkipKey(), Is.False, "착지 뒤에는 더 건너뛰지 않는다");
                Settle(flow);
                Assert.That(client.View.Pokemon[eevee].Def, Is.EqualTo(to), "고른 모습으로");
                int old = ctx.Cards.Pokemon[eeveeDef].Signature;
                if (evolve.Now == EvolveScreen.Mode.Teach)
                {
                    yield return Fits(stage, evolve.Name);
                    Assert.That(evolve.PressTeach(0), Is.True, "칸 1에 가르침");
                    yield return null;
                    Settle(flow);
                    Assert.That(client.View.Pokemon[eevee].Moves[0], Is.EqualTo(old), "옛 시그니처 기술(p.5)");
                }
                Assert.That(evolve.Now, Is.EqualTo(EvolveScreen.Mode.Pick));
                flow.CloseEvolve();
                yield return null;
                Assert.That(stage.Current, Is.Null);
                Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면 없는 선택 0번");
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                var shut = Object.FindAnyObjectByType<ScreenBoot>();
                if (shut != null && shut.Root != null) shut.Root.SetTarget(null);
                if (rt != null)
                {
                    rt.Release();
                    Object.Destroy(rt);
                }
            }
        }

        [UnityTest]
        public IEnumerator M5_조우창의제작은조우창위에제작창을열고닫으면카드가그대로다()
        {
            yield return Open(null);
            var boot = Object.FindAnyObjectByType<ScreenBoot>();
            Assert.That(boot, Is.Not.Null);
            boot.PointerFromMouse = false;
            var flow = boot.Flow;
            var stage = boot.Root.Stage;
            var pilot = FlowPilot.Start(flow, SoloTrail.Work);
            yield return null;
            bool reached = FlowPilot.Run(flow, pilot, () => stage.Current is EncounterScreen e && e.Open, ms => flow.Skip(ms), 500, 4000);
            Assert.That(reached, Is.True, "찾아보기 → 조우 창");
            var encounter = (EncounterScreen)stage.Current;
            int cards = encounter.Cards.Count;
            Assert.That(stage.Root.Q("encounter-craft"), Is.Not.Null, "조우 창의 [제작](21행)");
            Assert.That(flow.OpenCraft(), Is.True);
            yield return null;
            var craft = stage.Current as CraftScreen;
            Assert.That(craft, Is.Not.Null, "조우 창 위의 제작 창");
            Assert.That(flow.Music(), Is.EqualTo(MusicScene.Encounter), "조우 곡 그대로");
            int ball = craft.ItemOf("item.poke_ball");
            Assert.That(craft.PressMake(ball), Is.True, "고르기 전 제작(룰북 p.2)");
            Settle(flow);
            Assert.That(stage.Current, Is.SameAs(craft));
            Assert.That(flow.Client.Choice(ChoiceKind.EncounterPick), Is.Not.Null, "조우 고르기가 다시 열려 있다");
            flow.CloseCraft();
            yield return null;
            var again = stage.Current as EncounterScreen;
            Assert.That(again, Is.Not.Null, "닫으면 조우 창이 다시 선다");
            Assert.That(again.Cards.Count, Is.EqualTo(cards), "카드가 그대로");
            Assert.That(again.FaceUp, Is.EqualTo(cards), "이미 앞면");
            Assert.That(flow.NotReadyShown, Is.EqualTo(0));
        }
    }
}
