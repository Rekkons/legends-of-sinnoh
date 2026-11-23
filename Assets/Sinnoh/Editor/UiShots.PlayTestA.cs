using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M9 플레이 시험 2026-11-19 「창 · 입력 · 정보 표시」 묶음(T35 · 결정 125 · T21 · T5 · T17 · T23 · T30 · T27 · T32)의 장면 — 그림 <c>Logs/Review/m9/playtest-a/</c>.
    /// pta-fate-* · pta-breach-party · pta-battle-* = 물음 창 · 배틀 위의 게임 메뉴 · 정보 창 층(보기만 하는 정비 판의 잠긴 까닭 줄) · pta-party-* = 정비 판(기술 카드 풀이 많은 판 · 우두머리 · 전설 · 유물).
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] PlayTestAScreens =
        {
            Screen("pta-fate-menu", s => FateInfo(s, f => f.OpenMenu(), () => s.Made.Stage.Info is BoardMenuScreen)),        // 6장째 버리기 창 위의 게임 메뉴(T35)
            Screen("pta-fate-party", s => FateInfo(s, f => f.OpenParty(), () => Locked(s.Made, "screen.peek.lock.window"))),  // 버리기 창 위의 정비 판 — 보기만(결정 125)
            Screen("pta-breach-party", s =>                                                                                     // 왜곡 균열 카드 창 위의 정비 판 — 균열 카드를 고르며 능력치를 본다(T34)
            {
                BreachAttach(s, false);
                var flow = s.Made;
                var under = flow.Stage.Current;
                if (!flow.OpenParty() || !Locked(flow, "screen.peek.lock.window") || flow.Stage.Current != under) throw new InvalidOperationException("균열 카드 창 위에 보기만 하는 정비 판이 없다");
                s.Panel.Tick(3);
                PeekCheck(s, "board-party-panel");
            }),
            Screen("pta-battle-party", s => BattleInfo(s, f => f.OpenParty(), () => Locked(s.Made, "screen.peek.lock.battle"), "board-party-panel")),   // 연출 중의 배틀 위 정비 판(보기만)
            Screen("pta-battle-menu", s => BattleInfo(s, f => { f.OpenMenu(); return f.MenuUp; }, () => s.Made.Stage.Info is BoardMenuScreen, null)),   // 배틀 위의 게임 메뉴(T35)
            Screen("pta-types", s => Types(s, Seats(s, 1), false)),                                                            // 보드의 [상성](T5): 상성표 + 설명서의 타입 상성 · 상태 이상 절
            Screen("pta-types-tip", s => Types(s, Seats(s, 1), true)),                                                         // 머리 칸(타입 그림)에 마우스: 타입 이름 · 쪽
            Screen("pta-types-fate", s => FateInfo(s, f => f.ToggleTypes(), () => s.Made.Stage.Info is TypeChartScreen)),      // 버리기 창 위의 상성 창(정보 창 층)
            Screen("pta-types-battle", s => BattleInfo(s, f => f.ToggleTypes(), () => s.Made.Stage.Info is TypeChartScreen, "board-types-panel")),   // 배틀 위의 상성 창
            Screen("pta-party", s => PartyA(s)),                                                                               // 정비 판: 기술 카드 14장 · 우두머리 · 전설 · 유물(전 · 후)
            Screen("pta-party-relic-tip", s =>                                                                                  // 가방의 유물에 마우스(T17)
            {
                PartyA(s);
                var chip = s.Made.Stage.Root.Q("board-party-relic-1") ?? s.Made.Stage.Root.Q("board-party-bag-1");
                if (chip == null) throw new InvalidOperationException("유물 · 가방 칩이 없다");
                s.Made.Stage.Tips.Show(chip);
            }),
            Screen("pta-party-scroll", s =>                                                                                     // 기술 카드 풀을 끝까지 굴린 모습(T30 ②)
            {
                PartyA(s);
                var sv = s.Made.Stage.Root.Q<ScrollView>("board-party-pool-scroll") ?? throw new InvalidOperationException("기술 카드 풀이 굴러가는 칸이 아니다");
                sv.scrollOffset = new UnityEngine.Vector2(0, sv.contentContainer.layout.height);
                s.Panel.Tick(3);
                var last = s.Made.Stage.Root.Q("board-party-pool-14").worldBound;
                if (last.yMax > sv.contentViewport.worldBound.yMax + 0.5f) throw new InvalidOperationException("끝까지 굴려도 마지막 카드가 칸 밖이다");
            }),
            Screen("pta-party-pool-tip", s => PartyTip(s, "board-party-pool-1-back")),                                         // 기술 카드의 등급 배지에 마우스: 「○○ 카드의 뒷면」(T30 ①)
            Screen("pta-party-alpha-tip", s => PartyTip(s, "board-party-item-1")),                                              // 우두머리 토큰 칸에 마우스(T23)
            Screen("pta-party-passive-tip", s => PartyTip(s, "board-party-slot-2-2")),                                          // 전설의 「비스트 부스트」(결정 206 ③ — 카드 전술 판의 상자)에 마우스(T27)
            Screen("pta-mon-alpha", s =>                                                                                        // 우두머리 포켓몬의 카드 확대: 카드 지닌물건 동그라미의 우두머리 토큰 + 기둥의 효과 글 상자(T23 · 결정 206 ①)
            {
                PartyA(s);
                if (!s.Made.OpenMon(s.Made.Client.View.Seats[0].Party[1], 0)) throw new InvalidOperationException("카드 확대가 열리지 않았다");
                s.Panel.Tick(3);
                var zoom = s.Made.Stage.Root.Q("mon-detail-panel");
                if (zoom?.Q("card-item-1") == null || zoom.Q("card-note-1") == null) throw new InvalidOperationException("카드 확대에 우두머리 토큰 · 효과 글 상자가 없다");
            }),
            Screen("pta-hud-passive", s => HudPassive(s, false, null)),                                                        // 아래 판 파티 칸: 우두머리 루카리오(우두머리 · 비스트 부스트) · 유크시(비스트 부스트) 칩(T23 · T27)
            Screen("pta-hud-passive-tip", s => HudPassive(s, false, "board-hud-mon-2-passive-beast")),                          // 파티 칸의 「비스트 부스트」에 마우스 — 효과 글이 위로(판을 덮지 않게)
            Screen("pta-hud-passive-6", s => HudPassive(s, true, null)),                                                        // 여섯 마리 · 우두머리 다섯(칩 둘씩): 가장 빠듯한 파티 칸
            Screen("pta-solo-trading", s =>                                                                                    // 솔로 설정의 거래 줄(T32) + 「켜기」에 마우스
            {
                var flow = s.Flow();
                flow.OpenSolo();
                s.Panel.Tick(3);
                var opt = flow.Stage.Root.Q("solo-opt-" + SoloScreen.Trading + "-on") ?? throw new InvalidOperationException("솔로 설정에 거래 줄이 없다");
                flow.Stage.Tips.Show(opt);
            }),
            Screen("pta-room-trading", s =>                                                                                     // 방 설정(방장)의 거래 줄: 「켜기」 준비 중 + 마우스(T32)
            {
                var lobby = FakeLobby.Internet();
                lobby.Room.Settings.Trading = false;   // 방을 열 때처럼(RoomFlow.Host — 결정 124)
                var flow = RoomLobby(s, lobby);
                var opt = flow.Stage.Root.Q("room-set-opt-trading-on") ?? throw new InvalidOperationException("방 설정에 거래 줄이 없다");
                flow.Stage.Tips.Show(opt);
            }),
        };

        // 보드에서 위 띠 [상성]: 판 · 칸이 화면 안 · 판 안이고 글이 넘치지 않는다(tip = 첫 머리 칸에 마우스)
        static void Types(UiShot s, GameFlow flow, bool tip)
        {
            if (!flow.ToggleTypes() || !(flow.Stage.Current is TypeChartScreen)) throw new InvalidOperationException("[상성] 창이 열리지 않았다");
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var panel = root.Q("board-types-panel").worldBound;
            var chart = root.Q("board-types-chart").worldBound;
            if (chart.xMax > panel.xMax + 0.5f || chart.yMax > panel.yMax + 0.5f) throw new InvalidOperationException("상성표가 판 밖으로 나간다(" + chart + " / " + panel + ")");
            var dock = root.Q("board-hud-dock").worldBound;
            if (panel.yMax > dock.yMin + 0.5f) throw new InvalidOperationException("상성 창이 아래 판을 덮는다");
            var bad = WorkUi.Overflows(root.Q("board-types-panel"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
            TypesCorner(root);
            TypesStatusIcons(root);
            if (!tip) return;
            // 모서리 칸도 sn-types-head(결정 143 ②)라 툴팁이 없다 — 모서리가 아닌 첫 머리 칸(받는 쪽 노말)
            var head = root.Q("board-types-chart").Query(className: "sn-types-head").Where(e => !e.ClassListContains("sn-types-corner")).First()
                       ?? throw new InvalidOperationException("상성표에 타입 머리 칸이 없다");
            flow.Stage.Tips.Show(head);
            s.Panel.Tick(3);
            var tipEl = flow.Stage.Tips.Element;
            var title = tipEl.Q<Label>(className: "kit-title");
            string want = flow.Stage.T("ui.types." + PokemonType.Normal);
            if (flow.Stage.Tips.Target != head || tipEl.resolvedStyle.display == DisplayStyle.None || tipEl.resolvedStyle.visibility != Visibility.Visible || title == null || title.text != want)
                throw new InvalidOperationException("머리 칸의 툴팁이 뜨지 않았다(제목 「" + title?.text + "」 ≠ 「" + want + "」)");
        }

        // 결정 143 ②: 표 위 설명 줄 없음 · 모서리 칸의 「방어」(오른쪽 위)는 대각선 위, 「공격」(왼쪽 아래)은 대각선 아래 — 둘 다 칸 안, 서로 안 겹침
        static void TypesCorner(VisualElement root)
        {
            if (root.Q("board-types-axis") != null) throw new InvalidOperationException("상성표 위 설명 줄이 남았다");
            var c = root.Q("board-types-corner")?.worldBound ?? throw new InvalidOperationException("모서리 칸이 없다");
            var def = root.Q("board-types-corner-defend").worldBound;
            var atk = root.Q("board-types-corner-attack").worldBound;
            bool In(UnityEngine.Rect r) => r.xMin >= c.xMin - 0.5f && r.yMin >= c.yMin - 0.5f && r.xMax <= c.xMax + 0.5f && r.yMax <= c.yMax + 0.5f;
            float LineY(float x) => c.yMin + (x - c.xMin) * c.height / c.width;   // 왼쪽 위 → 오른쪽 아래
            if (!In(def) || !In(atk) || def.Overlaps(atk)) throw new InvalidOperationException("모서리 칸 축 이름이 칸 밖이거나 겹친다(칸 " + c + " · 방어 " + def + " · 공격 " + atk + ")");
            if (def.yMax > LineY(def.xMin) + 0.5f) throw new InvalidOperationException("「방어」가 대각선에 걸린다(" + def + " · 칸 " + c + ")");
            if (atk.yMin < LineY(atk.xMax) - 0.5f) throw new InvalidOperationException("「공격」이 대각선에 걸린다(" + atk + " · 칸 " + c + ")");
        }

        // 결정 143 ③: 상태 이상 표의 아이콘은 줄마다 이름 글 왼쪽 같은 줄(여섯) · 표 아래 아이콘 띠 없음
        static void TypesStatusIcons(VisualElement root)
        {
            var side = root.Q("board-types-side");
            var icons = side.Query(className: "sn-book-td-icon").ToList();
            if (icons.Count != 6) throw new InvalidOperationException("상태 이상 줄 머리 아이콘 " + icons.Count + " ≠ 6");
            if (side.Q(className: "sn-book-figure") != null) throw new InvalidOperationException("표 아래 아이콘 띠가 남았다");
            foreach (var icon in icons)
            {
                var a = icon.worldBound;
                var l = icon.parent.Q<Label>().worldBound;
                if (icon.resolvedStyle.backgroundImage.texture == null) throw new InvalidOperationException(icon.name + ": 그림이 없다");
                if (a.xMax > l.xMin + 0.5f || a.center.y < l.yMin || a.center.y > l.yMax) throw new InvalidOperationException(icon.name + "이 이름 글 앞 같은 줄이 아니다(" + a + " · 글 " + l + ")");
            }
        }

        // 정보 창 층의 내 정비 판이 그 까닭으로 잠겼나
        static bool Locked(GameFlow flow, string why) => flow.Stage.Info is PartyScreen p && p.Lock == why;

        // 6장째 버리기 창(운명 길) 위에 연다 — 아래 창은 그대로 · 답은 나가지 않는다
        static void FateInfo(UiShot s, Action<GameFlow> open, Func<bool> shown)
        {
            var flow = FateDiscard(s, false);
            var under = flow.Stage.Current;
            int commands = flow.Session.CommandCount;
            open(flow);
            if (!shown() || flow.Stage.Current != under || flow.Session.CommandCount != commands) throw new InvalidOperationException("버리기 창 위에 정보 창 층이 서지 않았다");
            s.Panel.Tick(3);
            PeekCheck(s, flow.Stage.Info is PartyScreen ? "board-party-panel" : null);
        }

        // 기술 카드를 쥐여 준 판(SoloTrail.Kit)의 첫 야생 배틀 — 연출 중(정비를 못 하는 때)에 연다
        static void BattleInfo(UiShot s, Func<GameFlow, bool> open, Func<bool> shown, string panel)
        {
            var flow = Trail(s, Sinnoh.Ai.SoloTrail.Kit, f => Battle(f, b => b.MaintOn && b.Moves.Count > 0));
            var battle = (BattleScreen)flow.Stage.Current;
            if (!battle.PressMove(0)) throw new InvalidOperationException("기술을 고르지 못했다");
            Pass(flow, 200);
            if (battle.MaintOn) throw new InvalidOperationException("기술을 고른 뒤에도 정비가 켜져 있다");
            if (!open(flow) || !shown() || flow.Stage.Current != battle) throw new InvalidOperationException("배틀 위에 정보 창 층이 서지 않았다");
            s.Panel.Tick(3);
            PeekCheck(s, panel);
        }

        // 정보 창이 화면 안에 있고 글이 넘치지 않는다 · 위 띠 [메뉴]가 맨 위에서 눌린다(게임 메뉴가 덮은 때는 빼고)
        static void PeekCheck(UiShot s, string panel)
        {
            var root = s.Made.Stage.Root;
            var info = s.Made.Stage.Info;
            if (info == null) throw new InvalidOperationException("정보 창 층이 비었다");
            var bad = WorkUi.Overflows(root.Q(info.Name));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
            if (panel != null)
            {
                var box = root.Q(panel).worldBound;
                if (box.xMin < -0.5f || box.yMin < -0.5f || box.xMax > root.worldBound.xMax + 0.5f || box.yMax > root.worldBound.yMax + 0.5f) throw new InvalidOperationException(panel + "이 화면 밖으로 나간다");
            }
            if (info is BoardMenuScreen) return;
            var menu = root.Q("board-hud-menu");
            var hit = root.panel.Pick(menu.worldBound.center);
            if (hit == null || (hit != menu && !menu.Contains(hit))) throw new InvalidOperationException("정보 창 위에서 [메뉴]가 눌리지 않는다(" + (hit != null ? hit.name : "없음") + ")");
        }

        // 정비 판: 강함 · 보통 · 약함 덱의 카드 14장을 스탬프 보상으로 받은 판(기술 카드 풀이 판 아래로 넘친다) + 우두머리 포켓몬 · 전설(비스트 부스트) · 가방의 유물
        static PartyScreen PartyA(UiShot s)
        {
            var ctx = s.Board.Ctx;
            var flow = Seats(s, 1, state =>
            {
                var st = state.Seats[0];
                st.OpenSlots = st.Party.Length;
                int alpha = AddMon(state, ctx, "448:0");   // 루카리오 — 우두머리
                state.Pokemon[alpha].Alpha = true;
                state.Pokemon[alpha].HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, state.Pokemon[alpha]);   // 우두머리 배틀에서 잡은 것처럼(BattleFlow)
                int legend = PokemonRules.Spawn(state, DefOf(ctx, "480:0"), 0, (int)DeckId.Legendary);   // 유크시 — 전설(배운 기술 0 · 비스트 부스트)
                state.Pokemon[legend].HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, state.Pokemon[legend]);   // 전설 배틀에서 잡은 것처럼
                PokemonRules.AddToParty(st, legend);
                int n = 0;
                foreach (var deck in new[] { DeckId.Strong, DeckId.Moderate, DeckId.Weak })
                    for (int i = 0; i < ctx.Cards.Pokemon.Count && n < 14; i++)
                    {
                        var def = ctx.Cards.Pokemon[i];
                        if (def.Signature < 0 || st.StampMoves.Contains(def.Signature) || !state.Decks[(int)deck].Contains(i)) continue;
                        st.StampCards.Add((int)deck);
                        st.StampCards.Add(i);
                        st.StampMoves.Add(def.Signature);
                        n++;
                        if (n % 5 == 0) break;
                    }
                st.Bag.Add(ItemOf(ctx, "item.magma_stone"));
                st.Bag.Add(ItemOf(ctx, "item.lucky_egg"));
            });
            if (!flow.OpenParty() || !(flow.Stage.Current is PartyScreen party)) throw new InvalidOperationException("정비 판이 열리지 않았다");
            s.Panel.Tick(3);
            return party;
        }

        // 정비 판의 그 요소에 마우스(툴팁이 화면 안 · 글 넘침 없음)
        static void PartyTip(UiShot s, string name)
        {
            PartyA(s);
            var e = s.Made.Stage.Root.Q(name) ?? throw new InvalidOperationException("요소 없음: " + name);
            s.Made.Stage.Tips.Show(e);
            s.Panel.Tick(3);
            var bad = WorkUi.Overflows(s.Made.Stage.Root.Q("board-party"));
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
        }

        // 보드 HUD 아래 판의 파티 칸 패시브 칩(T23 · T27): six = 스타팅 + 우두머리 다섯(배운 기술 0 — 칩 둘씩), 아니면 스타팅 + 우두머리 루카리오 + 유크시(전설).
        // 칩 줄이 칸 · 파티 자리 안이고 글이 넘치지 않으며, 우두머리 칸에는 칩 둘 · 전설 칸에는 비스트 부스트 하나. tip = 그 칩에 마우스(툴팁이 화면 안 · 아래 판을 덮지 않음)
        static void HudPassive(UiShot s, bool six, string tip)
        {
            var ctx = s.Board.Ctx;
            var alphas = new List<int>();
            int legend = -1;
            var flow = Seats(s, 1, state =>
            {
                var st = state.Seats[0];
                st.OpenSlots = st.Party.Length;
                foreach (var key in six ? new[] { "448:0", "445:0", "462:0", "472:0", "448:0" } : new[] { "448:0" })
                {
                    int id = AddMon(state, ctx, key);
                    state.Pokemon[id].Alpha = true;
                    state.Pokemon[id].HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, state.Pokemon[id]);   // 우두머리 배틀에서 잡은 것처럼(BattleFlow)
                    alphas.Add(id);
                }
                if (six) return;
                legend = PokemonRules.Spawn(state, DefOf(ctx, "480:0"), 0, (int)DeckId.Legendary);   // 유크시 — 전설(배운 기술 0 · 비스트 부스트)
                state.Pokemon[legend].HasLegendaryMove = BattleRules.LegendaryMovesEligible(ctx, state.Pokemon[legend]);
                PokemonRules.AddToParty(st, legend);
            });
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var party = root.Q("board-hud-party").worldBound;
            var me = flow.Client.View.Seats[0];
            int both = 0;
            for (int i = 0; i < me.Party.Length; i++)
            {
                int id = me.Party[i];
                if (id < 0) continue;
                var tile = root.Q("board-hud-mon-" + i) ?? throw new InvalidOperationException("파티 칸 " + i + "이 없다");
                var row = root.Q("board-hud-mon-" + i + "-passive");
                var pk = flow.Client.View.Pokemon[id];
                bool alpha = pk.Alpha, beast = LegendRules.HasBeastBoost(pk);
                if (alpha != alphas.Contains(id) || (id == legend && !beast)) throw new InvalidOperationException("손본 판이 아니다: 칸 " + i + " 우두머리 " + alpha + " · 비스트 부스트 " + beast);
                if ((row != null) != (alpha || beast)) throw new InvalidOperationException("파티 칸 " + i + " 패시브 칩 줄 " + (row != null ? "있음" : "없음") + " (우두머리 " + alpha + " · 비스트 부스트 " + beast + ")");
                if (row == null) continue;
                if ((row.Q("board-hud-mon-" + i + "-passive-alpha") != null) != alpha || (row.Q("board-hud-mon-" + i + "-passive-beast") != null) != beast) throw new InvalidOperationException("파티 칸 " + i + " 칩이 맞지 않다(우두머리 " + alpha + " · 비스트 부스트 " + beast + ")");
                if (alpha && beast) both++;
                foreach (var chip in row.Children())
                {
                    var b = chip.worldBound;
                    if (b.xMax > tile.worldBound.xMax + 0.5f || b.xMax > party.xMax + 0.5f || b.yMax > party.yMax + 0.5f) throw new InvalidOperationException("파티 칸 " + i + " 칩 " + chip.name + "이 칸 · 파티 자리 밖이다(" + b + " · 칸 " + tile.worldBound + " · 파티 " + party + ")");
                }
            }
            var bad = WorkUi.Overflows(root.Q("board-hud-party"));
            if (bad.Count > 0) throw new InvalidOperationException("파티 칸 글 넘침: " + string.Join(" / ", bad));
            UnityEngine.Debug.Log("[UiShots] pta-hud-passive 칩 둘인 칸 " + both + " · 파티 " + party);
            if (tip == null) return;
            var target = root.Q(tip) ?? throw new InvalidOperationException("요소 없음: " + tip);
            flow.Stage.Tips.Show(target);
            s.Panel.Tick(3);
            var t = flow.Stage.Tips.Element.worldBound;
            if (t.yMin < -0.5f || t.yMax > root.worldBound.yMax + 0.5f || t.Overlaps(root.Q("board-hud-dock").worldBound)) throw new InvalidOperationException("패시브 칩 툴팁이 화면 밖이거나 아래 판을 덮는다(" + t + ")");
        }

        static int DefOf(Ctx ctx, string key)
        {
            for (int i = 0; i < ctx.Cards.Pokemon.Count; i++) if (ctx.Cards.Pokemon[i].Key == key) return i;
            throw new InvalidOperationException("포켓몬 없음: " + key);
        }
    }
}
