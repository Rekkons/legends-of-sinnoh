using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
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
    /// M5 마감 답 반영 나-1(결정 80 N1 가 — 사람이 못 쓰던 규칙): 사람 좌석이 화면의 함수로만 누르는 길.
    /// 혼자 판을 손봐(금강옥을 지닌 디아루가 · 애크러뱃을 배운 찌르꼬) 정비 판의 [폼체인지] → 물음 줄 → 오리진폼, 고향 마을의 조무래기에게 도전해
    /// 차례 칸의 [대기 포켓몬 기술] → 그 줄 → 찌르꼬가 나오며 애크러뱃을 쓴다. DS 체력 상자의 둘째 상태 배지도 본다. Gen 겉모양·무대 그림이 있어야 돈다.
    /// </summary>
    public sealed class LeftoverFlowTests
    {
        const ulong Seed = 20261112, AiSeed = 7;

        static void Wait(GameFlow flow)
        {
            for (int i = 0; i < 400 && !flow.InputOpen; i++) flow.Skip(100);
        }

        [UnityTest]
        public IEnumerator M5_유물폼체인지는정비판에서_대기포켓몬의기술은차례칸에서고른다_G20_A26()
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
                yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
                ScreenBoot boot = null;
                for (int i = 0; i < 30; i++)
                {
                    yield return null;
                    boot = Object.FindAnyObjectByType<ScreenBoot>();
                    if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
                }
                Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
                Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
                boot.PointerFromMouse = false;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                boot.Root.SetTarget(rt);   // 1080 벌로 배치한다(글자 자리 단언)
                var flow = boot.Flow;
                var stage = boot.Root.Stage;
                var ctx = flow.Ctx;
                int dialga = ctx.Cards.PokemonIds.IndexOf("483:1"), origin = ctx.Cards.PokemonIds.IndexOf("483:2"), starly = ctx.Cards.PokemonIds.IndexOf("396:0");
                int acrobatics = ctx.Cards.MoveIds.IndexOf("acrobatics"), artifact = ctx.Items.Ids.IndexOf("item.adamant_artifact");
                Assert.That(new[] { dialga, origin, starly, acrobatics, artifact }, Has.None.Negative);

                flow.StartSeats(new SoloSettings(), 1, Seed, AiSeed, state =>
                {
                    var st = state.Seats[0];
                    int starter = st.Party[0];
                    st.OpenSlots = st.Party.Length;
                    st.Party[0] = PokemonRules.Spawn(state, dialga, 0, (int)DeckId.Legendary);
                    st.Party[1] = PokemonRules.Spawn(state, starly, 0, (int)DeckId.Legendary);
                    st.Party[2] = starter;
                    var lead = state.Pokemon[st.Party[0]];
                    lead.HeldItem = artifact;
                    lead.Status = StatusEffect.Poisoned;
                    lead.Status2 = StatusEffect.Burned;
                    state.Pokemon[st.Party[1]].Moves[0] = acrobatics;
                });
                yield return null;
                flow.Skip(flow.OpeningMs);
                Wait(flow);
                var client = flow.Client;
                int lead0 = client.View.Seats[0].Party[0], bird = client.View.Seats[0].Party[1];

                // ── 유물 폼체인지(leftovers §1 #4): 유물을 지닌 포켓몬의 칸에만 [폼체인지] → 물음 줄 → 정비 명령 ChangeForm ──
                Assert.That(flow.OpenParty(), Is.True, "정비 판");
                yield return null;
                var party = (PartyScreen)stage.Current;
                Assert.That(stage.Root.Q<Button>("board-party-form-0"), Is.Not.Null, "금강옥을 지닌 디아루가");
                Assert.That(stage.Root.Q("board-party-form-1"), Is.Null, "폼이 없는 포켓몬에는 줄이 없다");
                Assert.That(party.Forms(lead0), Is.EqualTo(new List<int> { origin }));
                Assert.That(party.Forms(bird), Is.Empty);
                Assert.That(party.PressForm(1), Is.False);
                Assert.That(party.PressForm(0), Is.True);
                Assert.That(party.Asking, Is.True);
                Assert.That(party.AskText, Is.EqualTo("디아루가를 오리진폼으로 폼체인지하겠습니까?"));
                Assert.That(stage.Root.Q("board-party-ask-next"), Is.Null, "갈 폼이 하나면 [다른 폼]은 없다");
                for (int i = 0; i < 3; i++) yield return null;
                var bad = WorkUi.Overflows(stage.Root.Q("board-party"));
                Assert.That(bad, Is.Empty, "정비 판: " + string.Join(" / ", bad));
                Assert.That(client.View.Pokemon[lead0].Def, Is.EqualTo(dialga), "아직 묻는 중");
                Assert.That(party.Confirm(), Is.True);
                yield return null;
                Assert.That(client.View.Pokemon[lead0].Def, Is.EqualTo(origin), "오리진폼");
                Assert.That(party.Forms(lead0), Is.EqualTo(new List<int> { dialga }), "기본 폼으로는 언제든 돌아간다");
                flow.Skip(400);
                Assert.That(party.PressForm(0), Is.True);
                Assert.That(party.AskText, Is.EqualTo("디아루가를 기본 폼으로 폼체인지하겠습니까?")); // 결정 163: 영문 카드 이름 「Dialga」 그대로 — 꼬리 없는 기본 폼은 screen.party.form.base
                party.Cancel();
                flow.CloseParty();
                yield return null;
                Wait(flow);

                // ── 대기 포켓몬의 기술(leftovers §1 #3): 조무래기에게 도전 → 차례 칸의 [대기 포켓몬 기술] → 그 줄 → 찌르꼬가 나오며 애크러뱃 ──
                Assert.That(flow.Hud.Press(BoardHud.Challenge), Is.True, "[도전]");
                BattleScreen battle = null;
                for (int guard = 0; guard < 400; guard++)
                {
                    battle = stage.Current as BattleScreen;
                    if (battle != null && battle.Open && battle.Bench.Count > 0) Assert.That(battle.PressBench(0), Is.True, "첫 출전 = 디아루가");
                    else if (battle != null && battle.Open && battle.AskingToken) battle.PressToken(true);
                    else if (battle != null && battle.Open && battle.Moves.Count > 0) break;
                    flow.Skip(250);
                    yield return null;
                }
                Assert.That(battle, Is.Not.Null, "배틀 화면");
                Assert.That(battle.Open && battle.Moves.Count > 0, Is.True, "내 차례");
                stage.Tweens.Advance(0);
                yield return null;
                // DS 체력 상자의 둘째 상태 배지(leftovers §1 #7)
                Assert.That(battle.Painted.Player.Status, Is.EqualTo(StatusEffect.Poisoned));
                Assert.That(battle.Painted.Player.Status2, Is.EqualTo(StatusEffect.Burned), "둘째 상태도 DS 상자에 싣는다");
                Assert.That(battle.Painted.Enemy.Status2, Is.EqualTo(StatusEffect.None));

                foreach (var a in battle.Moves) Assert.That(a.Arg, Is.LessThan(0), "기술 줄에는 나와 있는 포켓몬의 기술만");
                Assert.That(battle.PartyMovesOn, Is.True, "합법 답에 대기 포켓몬의 기술이 있다");
                Assert.That(stage.Root.Q<Button>("battle-partymoves"), Is.Not.Null);
                Assert.That(stage.Root.Q<Button>("battle-partymoves").text, Is.EqualTo("대기 포켓몬 기술"));
                Assert.That(battle.PartyOpen, Is.False);
                Assert.That(battle.OpenPartyMoves(), Is.True, "누르면 그 줄이 열린다(답은 아직 내지 않는다)");
                Assert.That(battle.PartyOpen, Is.True);
                Assert.That(client.Choice(ChoiceKind.BattleAction), Is.Not.Null, "아직 고르는 중");
                Assert.That(battle.PartyMoves.Count, Is.EqualTo(1));
                Assert.That(battle.PartyMoves[0].Target, Is.EqualTo(acrobatics));
                Assert.That(battle.PartyMoves[0].Arg, Is.EqualTo(bird));
                Assert.That(stage.Root.Q("battle-partymove-1"), Is.Not.Null);
                Assert.That(stage.Root.Q("battle-move-1"), Is.Null, "기술 줄 자리가 바뀐다");
                Assert.That(stage.Root.Q<Label>("battle-guide").text.Replace("⁠", ""), Is.EqualTo("교체해 나올 포켓몬의 기술을 선택하십시오."));
                for (int i = 0; i < 3; i++) yield return null;
                bad = WorkUi.Overflows(stage.Root.Q("battle-dock"));
                Assert.That(bad, Is.Empty, "대기 포켓몬의 기술 줄: " + string.Join(" / ", bad));
                Assert.That(battle.ClosePartyMoves(), Is.True, "[뒤로]");
                Assert.That(stage.Root.Q("battle-move-1"), Is.Not.Null);
                Assert.That(battle.OpenPartyMoves(), Is.True);
                Assert.That(battle.PressPartyMove(0), Is.True, "그 답(UseMove · Arg = 대기 포켓몬)을 낸다");
                Assert.That(battle.PartyOpen, Is.False);
                var used = flow.Battle.Events.Find(e => e is MoveUsed m && m.MoveKey == acrobatics) as MoveUsed;
                Assert.That(used, Is.Not.Null, "애크러뱃을 썼다");
                Assert.That(used.PokemonId, Is.EqualTo(bird), "찌르꼬가 나와서 쓴다");
                Assert.That(flow.Battle.Events.Exists(e => e is SwitchedOut so && so.PokemonId == lead0), Is.True, "디아루가는 들어간다");
                Assert.That(flow.Battle.Events.Exists(e => e is SentIn si && si.PokemonId == bird), Is.True);
                for (int i = 0; i < 400 && !battle.Ready; i++)
                {
                    flow.Skip(100);
                    if (i % 20 == 0) yield return null;
                }
                Assert.That(flow.NotReadyShown, Is.EqualTo(0), "받을 화면이 없는 선택은 없다");
                Assert.That(errors, Is.Empty, "에러 로그: " + string.Join(" | ", errors));
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
    }
}
