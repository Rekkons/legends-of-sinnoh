using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M6 「UB 배틀 화면 묶음」의 UI 캡처 장면(04 §8-8 87~92행 · S60 · S64 — 울트라 버스트 4인 판, 시드 <see cref="UbSeed"/>): UB 배틀의 등장·기둥·「포획 불가」 · 프리즘 아머(88행) ·
    /// 울트라 온슬롯(89행) · 네크로즈마 페이즈·동전(90행) · 호수 시련(92행) · 울트라 버스트 결과 창(승리 · 균열 패배 · 모두 탈락).
    /// 이기는 장면은 봇 승리 길 판(UbPlayoutTests.LateGame)과 같은 꼴로 손본다: 내 파티를 전설 다섯(<see cref="SoloTrail.Team"/>)으로 · 싸울 쪽의 프리즘 아머를 치운다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] UbBattleScreens =
        {
            // 87행: 균열이 찢어지는 전환(보라 줄무늬) → UB 등장 → 내 차례(기둥 「울트라 비스트」 · 「포획 불가」 · 독 회색 [포획 불가])
            Screen("row-87-wipe", s => UbWipe(s)),
            Battle("row-87-intro", s => UbIntro(s)),
            Battle("battle-ub-turn", s => UbMyTurn(s, UbAt(s, Beast, prism: 4))),
            Battle("battle-ub-prism-tip", s => { var f = UbAt(s, Beast, prism: 4); UbMyTurn(s, f); f.Stage.Tips.Show(f.Stage.Root.Q("battle-foe-prism")); TipHas(f, "screen.battle.prism.title"); }),   // 상대 기둥 프리즘 칩의 마우스 오버 = 효과(결정 193)
            // 88행: 프리즘 아머가 막음(UB 쪽 6개 — 차례마다 −3) → UB를 쓰러뜨려 1개가 울트라 비스트 쪽으로 → 결과 창의 줄
            Battle("row-88-block", s => Seen(FightInto(s, UbAt(s, Beast, prism: 6), st => st.Row == UbBattleRows.Prism, 150), b => b.BlockShown > 0f, "프리즘 −n")),
            Battle("row-88-move", s => Seen(FightInto(s, UbAt(s, Beast), st => st.Row == UbBattleRows.PrismMove, 600), b => b.MoveShown > 0f, "프리즘이 옮겨 가는 판")),
            Battle("battle-ub-result", s => UbResultWindow(s, UbAt(s, Beast), "battle-result-prism")),
            // 89행 울트라 온슬롯(켠 판): 떨며 떠오름 → 앞면 · 효과 빛 → 내 차례의 기둥 칩
            Battle("row-89-rise", s => Seen(FightInto(s, UbAt(s, Beast, onslaught: true), st => st.Row == UbBattleRows.Onslaught, 200), b => b.CardShown > 0f, "온슬롯 카드")),
            Battle("row-89-front", s => { Seen(FightInto(s, UbAt(s, Beast, onslaught: true), st => st.Row == UbBattleRows.Onslaught, 600), b => b.CardShown > 0f && CardArt(s), "온슬롯 카드 앞면(그림)"); CardGap(s); }),
            Battle("battle-ub-onslaught", s => { var f = UbAt(s, Beast, onslaught: true); Fight(f, b => MyTurn(b) && b.OnslaughtShown > 0); f.Layout(s.Width, s.Height); s.Panel.Tick(3); }),
            Battle("battle-ub-onslaught-tip", s => OnslaughtTip(s)),   // 온슬롯 칩의 마우스 오버 = 원작 카드 그림 + 이름 + 효과 문구(결정 87 V24 나)
            // S60 네크로즈마: 1페이즈 내 차례(페이즈 1/3 · 프리즘 9)
            Battle("battle-necrozma-turn", s => UbMyTurn(s, UbAt(s, Necrozma, phase: 1, keepPrism: true))),
            // 90행 썬/문 동전(2페이즈 첫 도전 — 조우 전환 앞 보드): 도는 중 → 착지·결과 글
            Screen("row-90-coin", s => Coin(s, 500)),
            Screen("row-90-coin-land", s => Coin(s, 1300)),
            // 90행 페이즈 격파: 결과 창 「2페이즈로 나아간다」 → [확인] 뒤 보드의 빛 · 가운데 판 · 체력 막대
            Battle("battle-necrozma-win", s => UbResultWindow(s, UbAt(s, Necrozma, phase: 1), "battle-result-phase")),
            Screen("row-90-phase", s => PhaseBoard(s, 1500)),
            // S64 울트라 버스트 결과 창: 3페이즈 격파(모두의 승리) · 균열 70%(86행 덮개 뒤) · 모두 탈락(혼자 · 축복 없음 · 네크로즈마에게 짐)
            Screen("ub-result-won", s => UbFinal(s, Won(s))),
            Screen("ub-result-breach", s => UbFinal(s, UbTurnInto(s, st => st.Row == UbRows.Lost, 0, st => BreachTo(st, 69)))),
            Screen("ub-result-eliminated", s => UbFinal(s, Eliminated(s))),
            // 92행 호수 시련(입지호수 · 아그놈 — 의지의 시련): 내 차례(면역 · 의지의 시련 0/2 · 독 회색 [포획 불가] · DS 면역 막) → 진행 → 넘김(막이 깨지고 「시련 통과!」)
            Battle("battle-trial", s => UbMyTurn(s, UbAt(s, Lake, team: true))),
            Battle("row-92-progress", s => TrialProgressOnly(s)),   // 진행만 보이는 단계(넘기기 전 — M6 「연출 목록 채우기」, UiShots.Fill.cs)
            // 결정 194: 「무모한 돌격」이 붙은 내 포켓몬이 표 상성이 유리한 기술로 UB를 친 계산 줄 — 「상성 +n」 · 「무모한 돌격 +2」로 나뉜다
            Battle("battle-calc-reckless", s => RecklessCalc(s)),
            Battle("row-92-pass", s => Seen(FightInto(s, UbAt(s, Lake, team: true), st => st.Row == UbBattleRows.Trial && UbRows.First<TrialProgress>(st) is TrialProgress p && p.Done, 800), b => b.BannerShown > 0f, "시련 통과")),
        };

        enum UbFoe { Beast, Necrozma, Lake, Silvally }
        const UbFoe Beast = UbFoe.Beast, Necrozma = UbFoe.Necrozma, Lake = UbFoe.Lake, Silvally = UbFoe.Silvally;

        // 울트라 버스트 4인 판의 내 턴에서 그 상대의 칸에 서서(UB는 체력이 가장 작은 것) [찾아보기]까지. team = 내 파티를 전설 다섯으로(시련 · 이겨야 하는 장면)
        static GameFlow UbAt(UiShot s, UbFoe foe, int prism = 0, int phase = 1, bool keepPrism = false, bool onslaught = false, bool team = true, int seats = UbSeats, Action<GameState> more = null)
        {
            var ctx = s.Board.Ctx;
            var settings = new SoloSettings { Campaign = Campaign.UltraBurst, UltraOnslaught = onslaught };
            var flow = UbGame(s, null, seats, settings);
            flow.Rebase(st =>
            {
                if (team) GiveTeam(ctx, st);
                if (!keepPrism)
                {
                    // 싸울 쪽의 프리즘 아머: UB 배틀 = 울트라 비스트 쪽 prism개 · 네크로즈마 배틀 = 0(봇 승리 길 판처럼)
                    st.Ub.PrismUltraBeast = foe == Necrozma ? UbState.PrismArmourTotal : prism;
                    st.Ub.PrismUltraBurst = UbState.PrismArmourTotal - st.Ub.PrismUltraBeast;
                }
                st.Ub.NecrozmaPhase = phase;
                if (phase >= 2 && st.Ub.SunMoon < 0) st.Ub.SunMoon = UbNecrozma.Moon;
                st.Seats[st.ActiveSeat].Cell = FoeCell(ctx, st, foe);
                more?.Invoke(st);
            });
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(BoardReadyMs);
            DealSettle(s, flow, MyMap);
            if (!flow.Hud.Press(BoardHud.Search)) throw new InvalidOperationException(foe + " 칸에서 [찾아보기]가 거절됐다");
            return flow;
        }

        // 결정 194: 무모한 돌격 몫이 실린 계산 단계 끝(칸이 다 켜진 모습) — 둘째 상성 칸의 이름 · 값, 첫 칸 = 「상성」 단정
        static void RecklessCalc(UiShot s)
        {
            var ctx = s.Board.Ctx;
            int sig = -1;
            var flow = UbAt(s, Beast, more: st => sig = PickReckless(ctx, st));
            FightInto(s, flow, st => st.Events.Exists(e => e is DamageBreakdown db && Array.Exists(db.Steps, x => x.Kind == DamageStepKind.Effectiveness && x.Source.Kind == SourceKind.BreachCard)), -100, sig);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var split = root.Q("battle-calc-matchup-1") ?? throw new InvalidOperationException("계산 줄에 둘째 상성 칸이 없다");
            var first = root.Q("battle-calc-matchup-0") ?? throw new InvalidOperationException("계산 줄에 상성 칸이 없다");
            string card = flow.Stage.T(ctx.Decks.CardIds[BreachRules.CardOf(ctx, BreachKind.RecklessAssault)]);
            var a = split.Query<Label>().ToList();
            var b = first.Query<Label>().ToList();
            if (a[0].text != card || a[1].text != "+" + BreachRules.RecklessBonus || b[0].text != flow.Stage.T("screen.battle.calc.effectiveness"))
                throw new InvalidOperationException("상성 칸: 「" + b[0].text + " " + b[1].text + "」 · 「" + a[0].text + " " + a[1].text + "」");
            if (split.resolvedStyle.visibility != Visibility.Visible) throw new InvalidOperationException("둘째 상성 칸이 아직 켜지지 않았다");
            var bad = WorkUi.Overflows(root.Q("battle-calc-chips"));
            if (bad.Count > 0) throw new InvalidOperationException("계산 줄 글 넘침: " + string.Join(" / ", bad));
        }

        // 결정 194 확인 판(PlayMode UbBreachFlowTests.Reckless와 같은 꼴): 체력이 가장 작은 UB에게 고유 기술의 표 상성이 유리한 포켓몬 중 체력이 가장 큰 것 하나 + 첫 칸에 「무모한 돌격」 · 그 UB 칸
        static int PickReckless(Ctx ctx, GameState st)
        {
            TokenState weak = null;
            foreach (var t in st.Tokens)
                if (t.Kind == TokenKind.UltraBeast && t.Cell >= 0 && (weak == null || ctx.Cards.Pokemon[t.Def].Health < ctx.Cards.Pokemon[weak.Def].Health)) weak = t;
            if (weak == null) throw new InvalidOperationException("UB 토큰이 없다");
            var ub = ctx.Cards.Pokemon[weak.Def].Types;
            int pick = -1;
            for (int i = 0; i < ctx.Cards.Pokemon.Count; i++)
            {
                var def = ctx.Cards.Pokemon[i];
                int m = def.Signature;
                if (m < 0 || m >= ctx.Cards.Moves.Count || ctx.Cards.Moves[m].AttackStrength == null) continue;
                var type = BattleRules.MoveType(ctx, m);
                if (type == PokemonType.Typeless || TypeChart.Bonus(type, ub[0], ub.Count > 1 ? ub[1] : PokemonType.Typeless) <= 0) continue;
                if (pick < 0 || def.Health > ctx.Cards.Pokemon[pick].Health) pick = i;
            }
            if (pick < 0) throw new InvalidOperationException("유리한 포켓몬이 없다");
            var me = st.Seats[0];
            for (int i = 0; i < me.Party.Length; i++) me.Party[i] = -1;
            int mon = PokemonRules.Spawn(st, pick, 0, (int)DeckId.Legendary);
            PokemonRules.AddToParty(me, mon);
            st.Pokemon[mon].Breach[0] = BreachRules.CardOf(ctx, BreachKind.RecklessAssault);
            me.Cell = weak.Cell;
            return ctx.Cards.Pokemon[pick].Signature;
        }

        static int FoeCell(Ctx ctx, GameState st, UbFoe foe)
        {
            if (foe == Lake) return FixtureCell(ctx, "legendary_site.lake_valor_cavern");
            if (foe == Silvally) return FixtureCell(ctx, "legendary_site.temple_of_sinnoh");   // 93행 실버디(M6 「연출 목록 채우기」)
            int best = -1;
            foreach (var t in st.Tokens)
            {
                if (t.Cell < 0) continue;
                if (foe == Necrozma && t.Kind == TokenKind.Necrozma) return t.Cell;
                if (foe == Beast && t.Kind == TokenKind.UltraBeast && (best < 0 || ctx.Cards.Pokemon[t.Def].Health < ctx.Cards.Pokemon[st.Tokens.Find(x => x.Cell == best && x.Kind == TokenKind.UltraBeast).Def].Health)) best = t.Cell;
            }
            if (best < 0) throw new InvalidOperationException(foe + " 토큰이 맵에 없다");
            return best;
        }

        // 내 차례(기술을 고를 때)까지 보내고 기둥의 UB 칩을 단정한다
        static void UbMyTurn(UiShot s, GameFlow flow)
        {
            Fight(flow, MyTurn);   // 첫 출전 고르기(파티 여럿)부터 내 기술 차례까지
            for (int i = 0; i < 4; i++) Pass(flow, 300);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var b = (BattleScreen)flow.Stage.Current;
            var feed = flow.Battle;
            if (feed.Kind == BattleKind.UltraBeast || feed.Kind == BattleKind.Necrozma)
            {
                if (!b.UncatchShown) throw new InvalidOperationException("「포획 불가」 칩이 없다");
                if (feed.Kind == BattleKind.Necrozma && (b.PhaseShown != 1 || b.PrismShown != UbState.PrismArmourTotal)) throw new InvalidOperationException("네크로즈마 칩: 페이즈 " + b.PhaseShown + " · 프리즘 " + b.PrismShown);
            }
            if (feed.Kind == BattleKind.Trial && (!b.ImmuneShown || b.TrialShown == null)) throw new InvalidOperationException("시련 칩: 면역 " + b.ImmuneShown + " · 진행 " + (b.TrialShown ?? "없음"));
            var bag = flow.Stage.Root.Q<Button>("battle-bag");
            if (bag == null || bag.text.IndexOf(flow.Stage.T("screen.battle.bag.none"), StringComparison.Ordinal) < 0) throw new InvalidOperationException("독의 [가방] 자리가 「포획 불가」가 아니다: " + (bag != null ? bag.text : "없음"));
            var head = flow.Stage.Root.Q("battle-info-foe");
            string want = flow.Stage.T("screen.battle.foe." + BattleFeed.UbFoe(feed.Kind));
            if (head == null || head.Query<Label>().Where(l => l.text == want).First() == null) throw new InvalidOperationException("상대 기둥 머리가 「" + want + "」가 아니다");
            if (feed.Kind == BattleKind.UltraBeast) ArtWhole(flow.Stage.Root, "battle-foe-art");
        }

        // 기둥 도트 칸이 그림 조각을 다 담는다(결정 87 V22 ① — 페로코체 더듬이가 잘렸다): 칸 > 캔버스 > 조각
        static void ArtWhole(VisualElement root, string name)
        {
            var box = root.Q(name);
            if (box == null || box.childCount == 0 || box[0].childCount == 0) throw new InvalidOperationException(name + " 도트가 없다");
            UnityEngine.Rect at = box.worldBound, piece = box[0][0].worldBound;
            if (piece.xMin < at.xMin - 0.5f || piece.yMin < at.yMin - 0.5f || piece.xMax > at.xMax + 0.5f || piece.yMax > at.yMax + 0.5f) throw new InvalidOperationException(name + " 도트가 칸에 잘린다(칸 " + at + " · 그림 " + piece + ")");
        }

        // 890 온슬롯 카드: 제목 글이 그림 아래 변과 띄어져 있다(결정 87 V22 ①)
        static void CardGap(UiShot s)
        {
            var card = s.Panel.Root.Q("battle-ub-card");
            if (card == null || card.childCount < 3) throw new InvalidOperationException("온슬롯 카드가 없다");
            float gap = card[1].worldBound.yMin - card[0].worldBound.yMax;
            if (gap < 2f) throw new InvalidOperationException("온슬롯 카드 제목이 그림에 닿는다(사이 " + gap + "px)");
        }

        // 890 온슬롯 카드의 그림 칸에 원작 그림이 실려 보이나(결정 87 V24 나)
        static bool CardArt(UiShot s)
        {
            var art = s.Panel.Root.Q("battle-ub-card-art");
            return art != null && art.resolvedStyle.visibility == Visibility.Visible && art.resolvedStyle.backgroundImage.texture != null;
        }

        static void Seen(GameFlow flow, Func<BattleScreen, bool> seen, string what)
        {
            var b = flow.Stage.Current as BattleScreen ?? throw new InvalidOperationException("배틀 화면이 아니다");
            if (!seen(b)) throw new InvalidOperationException(what + "이(가) 보이지 않는다");
        }

        // 온슬롯 칩의 마우스 오버 카드: 그림이 실리고 · 화면 안에 들고 · 칩을 덮지 않는다
        static void OnslaughtTip(UiShot s)
        {
            var flow = UbAt(s, Beast, onslaught: true);
            var b = Fight(flow, x => MyTurn(x) && x.OnslaughtShown > 0);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            b.HoverOnslaught(true);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var tip = root.Q("battle-ub-onslaught-tip");
            var art = root.Q("battle-ub-onslaught-tip-art");
            if (!b.OnslaughtTipShown || tip == null || tip.resolvedStyle.display == DisplayStyle.None || tip.resolvedStyle.visibility != Visibility.Visible) throw new InvalidOperationException("온슬롯 칩의 그림 카드가 뜨지 않았다");
            if (art.resolvedStyle.backgroundImage.texture == null) throw new InvalidOperationException("온슬롯 그림 카드에 그림이 없다(" + GenAssets.OnslaughtArt + ")");
            UnityEngine.Rect at = tip.worldBound, chip = root.Q("battle-foe-onslaught").worldBound, all = root.worldBound;
            if (at.xMin < all.xMin || at.yMin < all.yMin || at.xMax > all.xMax + 0.5f || at.yMax > all.yMax + 0.5f) throw new InvalidOperationException("온슬롯 그림 카드가 화면 밖으로 나갔다 " + at);
            if (at.Overlaps(chip)) throw new InvalidOperationException("온슬롯 그림 카드가 칩을 덮는다 " + at + " · " + chip);
            if (string.IsNullOrEmpty(((Label)root.Q("battle-ub-onslaught-tip-text")).text)) throw new InvalidOperationException("온슬롯 그림 카드에 효과 문구가 없다");
        }

        // 87행 전환: 줄무늬가 다 덮인 때 — 보라 줄무늬
        static void UbWipe(UiShot s)
        {
            var flow = UbAt(s, Beast);
            var step = Queued(flow, st => st.Row == EnterRows.Wipe);
            if (step == null) throw new InvalidOperationException("조우 전환 단계가 대기열에 없다");
            TickedInto(s, flow, step, step.Ms * (EnterBeats.WipeSwapAt - 30) / 1000);   // 박자는 단계 길이의 천분율
            var wipe = flow.Stage.Root.Q("field-wipe");
            if (wipe == null || !wipe.ClassListContains("sn-wipe-ub")) throw new InvalidOperationException("UB 배틀의 전환 줄무늬가 보라가 아니다(줄무늬 " + (wipe != null) + " 덮음 " + flow.WipeCover + " 단계 시각 " + (flow.Fx.Sequencer.LocalMs - step.StartMs) + " · 단계 이벤트 " + string.Join(",", step.Events.ConvertAll(e => e is BattleStarted bs ? "BattleStarted:" + bs.Kind : e.GetType().Name)) + " · 지금 항목에 단계 " + (flow.Fx.Sequencer.Current != null && flow.Fx.Sequencer.Current.Steps.Contains(step)) + ")");
        }

        // 87행 등장: UB가 미끄러져 들어온 뒤 「울트라 비스트 ○○이(가) 나타났다!」
        static void UbIntro(UiShot s)
        {
            var flow = UbAt(s, Beast);
            FxStep step = null;
            for (int i = 0; i < BattleStepMax && (step = Queued(flow, WildIntro)) == null; i++)
            {
                // 파티가 여럿이면 엔진이 첫 출전을 먼저 묻는다 — 첫 칸을 낸다
                if (flow.Stage.Current is BattleScreen b0 && b0.Open && b0.Bench.Count > 0) b0.PressBench(0);
                else Pass(flow, 50);
            }
            if (step == null) throw new InvalidOperationException("UB 인트로 단계가 대기열에 없다");
            TickedInto(s, flow, step, EnterBeats.IntroSecondMs - 100);   // 내 포켓몬이 나오기 바로 앞(UB 등장 줄이 DS에)
            var b = (BattleScreen)flow.Stage.Current;
            string want = flow.Stage.T("screen.battle.log.send.ub").Substring(0, 6);
            if (b.Message == null || b.Message.IndexOf(want, StringComparison.Ordinal) < 0) throw new InvalidOperationException("DS 메시지가 UB 등장 문구가 아니다: " + b.Message);
        }

        // 배틀을 끝까지(이기는 쪽으로) — 결과 창이 다 내려앉은 모습. line = 있어야 하는 결과 줄
        static void UbResultWindow(UiShot s, GameFlow flow, string line)
        {
            Fight(flow, b => b.ShowsResult);
            for (int i = 0; i < BattleStepMax && !((BattleScreen)flow.Stage.Current).Ready; i++) Pass(flow, BattleStepMs);
            Pass(flow, 1000);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            if (!flow.Battle.Won) throw new InvalidOperationException("이 판은 졌다 — 이기는 장면이 아니다");
            if (flow.Stage.Root.Q(line) == null) throw new InvalidOperationException("결과 창에 " + line + " 줄이 없다");
        }

        // 2페이즈 첫 도전: [찾아보기] → 동전 단계 안의 ms
        static void Coin(UiShot s, int ms)
        {
            var flow = UbAt(s, Necrozma, phase: 2, more: st => st.Ub.SunMoon = -1);
            var step = Queued(flow, st => st.Row == UbBattleRows.Coin);
            if (step == null) throw new InvalidOperationException("동전 단계가 대기열에 없다");
            TickedInto(s, flow, step, ms);
            if (flow.Hud.Ub.CoinShown <= 0f) throw new InvalidOperationException("동전이 보이지 않는다");
        }

        // 1페이즈를 이겨 결과 창 [확인] → 보드의 페이즈 단계 안의 ms
        static void PhaseBoard(UiShot s, int ms)
        {
            var flow = UbAt(s, Necrozma, phase: 1);
            UbResultWindow(s, flow, "battle-result-phase");
            if (!((BattleScreen)flow.Stage.Current).Confirm()) throw new InvalidOperationException("[확인]이 거절됐다");
            FxStep step = null;
            for (int i = 0; i < 20 && (step = Queued(flow, st => st.Row == UbBattleRows.Phase)) == null; i++) Pass(flow, 1);
            if (step == null) throw new InvalidOperationException("페이즈 단계가 대기열에 없다");
            TickedInto(s, flow, step, ms);
            if (flow.Hud.Ub.PhaseShown <= 0f || flow.Hud.Ub.PhaseFill <= 0f) throw new InvalidOperationException("페이즈 판이 보이지 않는다");
            UbClean(flow.Hud);
        }

        // 3페이즈를 이겨 결과 창 [확인]까지
        static GameFlow Won(UiShot s)
        {
            // 3페이즈(체력 36)는 판의 난수에 따라 진다 — 게임 난수를 다시 심어 가며 이기는 첫 판(결정적)
            GameFlow flow = null;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                ulong at = seed;
                if (flow != null) s.Panel.Root.Clear();   // 진 판의 화면 요소를 걷고 새 판으로(같은 패널 하나)
                flow = UbAt(s, Necrozma, phase: 3, more: st => st.Rng.CopyFrom(new Pcg32(at)));
                Fight(flow, b => b.ShowsResult);
                if (flow.Battle.Won) break;
            }
            UbResultWindow(s, flow, "battle-result-title");
            if (!((BattleScreen)flow.Stage.Current).Confirm()) throw new InvalidOperationException("[확인]이 거절됐다");
            return flow;
        }

        // 혼자 하는 판 · 축복 없음 · 파티 체력 1: 네크로즈마에게 져 탈락 → 모두 탈락 → 결과 창 [확인]까지
        static GameFlow Eliminated(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = UbAt(s, Necrozma, phase: 1, team: false, seats: 1, more: st =>
            {
                st.Ub.Blessings = 0;
                foreach (int p in st.Seats[0].Party)
                    if (p >= 0) st.Pokemon[p].Damage = PokemonRules.MaxHp(ctx, st.Pokemon[p]) - 1;
            });
            Fight(flow, b => b.ShowsResult);
            for (int i = 0; i < BattleStepMax && !((BattleScreen)flow.Stage.Current).Ready; i++) Pass(flow, BattleStepMs);
            if (!((BattleScreen)flow.Stage.Current).Confirm()) throw new InvalidOperationException("[확인]이 거절됐다");
            return flow;
        }

        // 게임이 끝난 판: 결과 창(S64)이 열리고 연출이 다 돈 모습(제목 · 얼굴 · 파티 · 통계 · [타이틀로])
        static void UbFinal(UiShot s, GameFlow flow)
        {
            ResultScreen result = null;
            for (int i = 0; i < 200; i++)
            {
                result = flow.Stage.Current as ResultScreen;
                if (result != null && result.Ready) break;
                Pass(flow, 200);
            }
            if (result == null || !result.Ready) throw new InvalidOperationException("결과 창이 열리지 않았다(지금 " + (flow.Stage.Current != null ? flow.Stage.Current.Name : "없음") + ")");
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var bad = WorkUi.Overflows(flow.Stage.Root.Q("result-panel"));
            if (bad.Count > 0) throw new InvalidOperationException("결과 창 글 넘침: " + string.Join(" / ", bad));
        }
    }
}
