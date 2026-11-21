using System;
using System.Collections.Generic;
using System.Text;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M9 다듬기 4 · 6 확인 장면(P06 · P09 · P25 · P28 · P29 · P35 · P36 · P37에서 확인하지 못하고 남긴 것): 연출 「빠름」 · 「끄기」로 돌린 여섯 연출(빠름 = 그 단계가 실시각 반 길이 · 박자 함수 그대로,
    /// 끄기 = 장식이 한 프레임도 보이지 않음, 두 쪽 모두 끝 모습이 보통 속도와 같다) · 신의 첫 폼이 기절 단계에서 서 있는 순간(P29) · 작은 전설 장소 피규어(P25 — 로토무 · 쉐이미) ·
    /// 실제 이벤트 길(로비 「내 주인공」 칸의 ClickEvent · 조우 카드의 PointerEnterEvent — 창 없는 시험과 MouseProbe가 지나지 않는 방 판의 길). 그림 <c>Logs/Review/m9/polish6-check/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] Polish6CheckScreens =
        {
            // ① 연출 「빠름」 · 「끄기」 — 찍히는 것은 대기열이 빈 뒤의 끝 모습(보통 속도의 끝 모습과 글로 견준 뒤)
            Screen("p6c-25-fast", s => Sped(s, SpedSite, FxSpeed.Fast)),
            Screen("p6c-25-off", s => Sped(s, SpedSite, FxSpeed.Off)),
            Screen("p6c-28-fast", s => Sped(s, SpedScatter, FxSpeed.Fast)),
            Screen("p6c-28-off", s => Sped(s, SpedScatter, FxSpeed.Off)),
            Screen("p6c-29-fast", s => Sped(s, SpedSlump, FxSpeed.Fast)),
            Screen("p6c-29-off", s => Sped(s, SpedSlump, FxSpeed.Off)),
            Screen("p6c-37-fast", s => Sped(s, SpedGlade, FxSpeed.Fast)),
            Screen("p6c-37-off", s => Sped(s, SpedGlade, FxSpeed.Off)),
            Screen("p6c-36-fast", s => Sped(s, SpedSky, FxSpeed.Fast)),
            Screen("p6c-36-off", s => Sped(s, SpedSky, FxSpeed.Off)),
            Screen("p6c-35-fast", s => Sped(s, SpedBless(ShrineRules.FatefulEncounter), FxSpeed.Fast)),
            Screen("p6c-35-off", s => Sped(s, SpedBless(ShrineRules.FatefulEncounter), FxSpeed.Off)),
            Screen("p6c-35-tranquil-fast", s => Sped(s, SpedBless(ShrineRules.TranquilMoment), FxSpeed.Fast)),
            Screen("p6c-35-tranquil-off", s => Sped(s, SpedBless(ShrineRules.TranquilMoment), FxSpeed.Off)),
            // ② P29: 신의 첫 폼이 쓰러진 기절 단계 — 다른 포켓몬이면 다 가라앉았을 때(600ms) · 둘째 폼 단계 바로 앞
            Battle("p6c-29-faint", s => GodFaint(s, false)),
            Battle("p6c-29-faint-end", s => GodFaint(s, true)),
            // ③ P25 작은 피규어: 그랜드 샤토의 로토무(비밀키) · 감사의 꽃밭의 쉐이미(그라시데아 꽃) — 솟기 전 · 가장 높이 · 내려앉는 중
            Screen("p6c-25-rotom-before", FitBoard(s => SiteRise(s, LegendSiteBeats.RiseAtMs - 150, GrandChateau, "item.secret_key"))),
            Screen("p6c-25-rotom-rise", FitBoard(s => SiteRise(s, LegendSiteBeats.RiseAtMs + LegendSiteBeats.RiseMs / 2, GrandChateau, "item.secret_key"))),
            Screen("p6c-25-rotom-land", FitBoard(s => SiteRise(s, LegendSiteBeats.Ms - 60, GrandChateau, "item.secret_key"))),
            Screen("p6c-25-shaymin-rise", FitBoard(s => SiteRise(s, LegendSiteBeats.RiseAtMs + LegendSiteBeats.RiseMs / 2, "legendary_site.garden_of_gratitude", "item.gracidea_flower"))),
            Screen("p6c-25-heatran-rise", FitBoard(s => SiteRise(s, LegendSiteBeats.RiseAtMs + LegendSiteBeats.RiseMs / 2))),
            // ⑦ 실제 이벤트 길: 로비 「내 주인공」 칸의 ClickEvent → 방에 보냄 · 조우 카드의 PointerEnterEvent · PointerLeaveEvent → 방에 「가리킴」 · 「떠남」
            Screen("p6c-lobby-click", LobbyClick),
            Screen("p6c-peek-enter", PeekEnter),
        };

        const string GrandChateau = "legendary_site.grand_chateau";

        // 연출 하나: 보통 속도로 그 단계가 대기열에 설 때까지 · 빠름으로 볼 단계 안 시각 · 그 시각의 단정(박자 함수와 견줌) · 끄기에서 장식이 남았으면 그 까닭
        sealed class SpedFx
        {
            public Func<UiShot, FxStep> Open;
            public int MidMs;
            public Action<UiShot, int> Check;
            public Func<UiShot, string> Bare;
            /// <summary>보통 속도에서는 카메라가 그 연출을 따라가 머문다(끄기에서는 가지 않는다 — 끝 모습은 보는 점을 맞춘 뒤 견준다).</summary>
            public bool CameraStays;
        }

        static readonly SpedFx SpedSite = new SpedFx
        {
            Open = s => SiteOpen(s, MagmaChamber, "item.magma_stone"),
            MidMs = LegendSiteBeats.RiseAtMs + LegendSiteBeats.RiseMs / 2,
            Check = (s, ms) => SiteCheck(s, MagmaChamber, ms),
            Bare = s =>
            {
                // −1 = 선 피규어 없음: 배틀 동안 그 전설 카드가 덱 밖이라 결정 123대로 곧바로 사라진다(끄기) — 솟은 것이 아니다
                float rise = s.Made.FigureRise(FixtureCell(s.Board.Ctx, MagmaChamber));
                if (rise > 0.001f) return "피규어가 솟았다(" + rise + ")";
                return s.Made.Results != null && (s.Made.Results.FlyShown > 0 || s.Made.Results.GlowShown > 0f) ? "유물 공명이 보인다" : null;
            },
        };

        static readonly SpedFx SpedScatter = new SpedFx
        {
            Open = s => Queued(GodBattle(s, ResultRows.GodGone, 0), st => st.Row == ResultRows.GodGone),
            MidMs = 450,
            Check = GodScatterCheck,
            Bare = s =>
            {
                if (s.Made.FigureShown("487:1", out _)) return "신 피규어가 남았다";
                if (s.Made.Results != null && s.Made.Results.MotesShown > 0) return "빛 알갱이가 보인다";
                return s.Made.Results != null && s.Made.Results.BannerShown ? "「한 번 더!」가 보인다" : null;
            },
        };

        static readonly SpedFx SpedSlump = new SpedFx
        {
            Open = s => Queued(GodBattle(s, ResultRows.GodPhase, 0), st => st.Row == ResultRows.GodPhase),
            MidMs = 3200 * 20 / 100,
            Check = GodSlumpCheck,
            Bare = s =>
            {
                var screen = s.Made.Stage.Current as BattleScreen;
                var enemy = screen != null && screen.Painted != null ? screen.Painted.Enemy : null;
                if (enemy == null || !enemy.Standing) return "신이 서 있지 않다(" + (enemy == null ? "없음" : enemy.Key) + ")";
                if (screen.FormShown) return "폼 바꾸기를 그리는 중이다";
                return enemy.Key != "487:2" || enemy.Sink != 0f || enemy.White != 0f ? "둘째 폼이 제 모습이 아니다(" + enemy.Key + " · 깊이 " + enemy.Sink + " · 흰 빛 " + enemy.White + ")" : null;
            },
        };

        static readonly SpedFx SpedGlade = new SpedFx
        {
            Open = GladeOpen,
            CameraStays = true,
            MidMs = LegendBeats.ScatterFromMs + 420,
            Check = GladeCheck,
            Bare = s =>
            {
                var flow = s.Made;
                if (flow.FigureShown(Cresselia, out _)) return "크레세리아 피규어가 남았다";
                if (flow.Hud != null && flow.Hud.ShrineFx.MotesShown > 0) return "빛 알갱이가 보인다";
                if (!flow.FigureShown(Darkrai, out float size) || Mathf.Abs(size - 1f) > 0.001f) return "다크라이가 제 크기로 서 있지 않다(" + size + ")";
                float rise = flow.FigureRise(FixtureCell(s.Board.Ctx, "legendary_shrine.moonglade_shrine"));
                return Mathf.Abs(rise) > 0.001f ? "다크라이가 떠 있다(" + rise + ")" : null;
            },
        };

        static readonly SpedFx SpedSky = new SpedFx
        {
            Open = s => OfferOpen(s, ApricornColor.Pink),
            MidMs = OfferBeats.PillarAt + 350,
            Check = (s, ms) => OfferCheck(s, ApricornColor.Pink, ms),
            Bare = s =>
            {
                var fx = s.Made.Hud != null ? s.Made.Hud.ShrineFx : null;
                var sky = s.Made.Stage.Root.Q("board-hud-shrine-sky");
                return fx != null && fx.SkyShown != 0 ? "판 위 빛기둥이 섰다(" + fx.SkyShown + "단계)" : sky != null && sky.resolvedStyle.opacity > 0f ? "빛기둥 요소가 보인다" : null;
            },
        };

        static SpedFx SpedBless(string card) => new SpedFx
        {
            Open = s => BlessOpen(s, card),
            MidMs = BlessBeats.GiftFromMs + 260,
            Check = (s, ms) => BlessCheck(s, card, ms),
            Bare = s =>
            {
                var shrine = s.Made.Stage.Current as ShrineScreen;
                if (shrine != null && (shrine.GiftsShown > 0 || shrine.GiftText != null)) return "축복 그림이 보인다(" + shrine.GiftsShown + " · " + shrine.GiftText + ")";
                var badge = s.Made.Stage.Root.Q("board-shrine-bless");
                return badge != null && badge.resolvedStyle.opacity > 0f ? "축복 꼬리표가 보인다" : null;
            },
        };

        // 그 단계 안의 시각(시작 전 −1 · 끝난 뒤 int.MaxValue)
        static int StepLocal(GameFlow flow, FxStep step)
        {
            var seq = flow.Fx.Sequencer;
            foreach (var entry in seq.Queued)
                if (entry.Steps.Contains(step))
                {
                    if (!ReferenceEquals(entry, seq.Current) || seq.LocalMs < step.StartMs) return -1;
                    return seq.LocalMs < step.EndMs ? seq.LocalMs - step.StartMs : int.MaxValue;
                }
            return int.MaxValue;
        }

        /// <summary>
        /// 그 연출을 S02 「연출」 빠름 · 끄기로 돌린다. 먼저 보통 속도로 끝까지 보낸 판의 끝 모습을 글로 적어 두고(견줄 판), 새 판을 그 단계의 시작까지 보통으로 보낸 뒤 속도를 건다.
        /// 빠름: 실시각 MidMs × fx.fastPercent%에 단계 안 시각이 MidMs(±2)이고 그 순간의 그림이 박자 함수대로 · 단계는 실시각으로 그 비율 길이에 끝난다(2ms 앞에는 아직 · 2ms 뒤에는 끝).
        /// 끄기: 다음 프레임에 남은 대기열이 한 번에 지나가고(그 단계도) 그 뒤 여섯 프레임 내내 장식이 없다. 두 쪽 모두 대기열이 빈 뒤의 모습(화면 요소 · 카메라 · 피규어 · DS 포켓몬 · 열린 선택)이 견줄 판과 같다.
        /// </summary>
        static void Sped(UiShot s, SpedFx fx, FxSpeed speed)
        {
            var open = fx.Open(s);
            Laid(s, s.Made, open, 0);
            string plain = EndText(s, s.Made);
            var looked = s.Rig != null ? s.Rig.Target : default(Vector3);
            s.Panel.Root.Clear();

            var step = fx.Open(s);
            var flow = s.Made;
            var seq = flow.Fx.Sequencer;
            Laid(s, flow, step, 0);
            if (StepLocal(flow, step) > 0) throw new InvalidOperationException("단계의 시작이 아니다(" + StepLocal(flow, step) + "ms)");
            flow.FxSpeed = speed;
            if (seq.Speed != speed) throw new InvalidOperationException("속도가 대기열에 걸리지 않았다");
            if (speed == FxSpeed.Fast)
            {
                int pct = FxTune.FastPercent.Value;
                flow.Stage.Tweens.Advance(fx.MidMs * pct / 100);
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(3);
                int local = StepLocal(flow, step);
                if (Math.Abs(local - fx.MidMs) > 2) throw new InvalidOperationException("빠름: 실시각 " + fx.MidMs * pct / 100 + "ms에 단계 안 " + local + "ms(" + fx.MidMs + "ms여야 한다)");
                fx.Check(s, local);
                int left = (step.Ms - local) * pct / 100;
                flow.Stage.Tweens.Advance(left - 2);
                if (StepLocal(flow, step) == int.MaxValue) throw new InvalidOperationException("빠름: 단계가 반 길이보다 일찍 끝났다");
                flow.Stage.Tweens.Advance(4);
                if (StepLocal(flow, step) != int.MaxValue) throw new InvalidOperationException("빠름: 단계가 실시각 " + step.Ms * pct / 100 + "ms에 끝나지 않았다(단계 안 " + StepLocal(flow, step) + " / " + step.Ms + ")");
            }
            else
            {
                for (int i = 0; i < 6; i++)
                {
                    flow.Stage.Tweens.Advance(16);
                    flow.Layout(s.Width, s.Height);
                    s.Panel.Tick(1);
                    if (StepLocal(flow, step) != int.MaxValue) throw new InvalidOperationException("끄기: 단계가 대기열에 남았다(" + i + "번째 프레임)");
                    string left = fx.Bare(s);
                    if (left != null) throw new InvalidOperationException("끄기: " + i + "번째 프레임에 " + left);
                }
            }
            if (speed == FxSpeed.Off && fx.CameraStays && s.Rig != null)
            {
                // 끄기: 그 단계를 재생하지 않아 카메라가 그 사당으로 다가가지 않는다(보통 · 빠름에서는 다가가 머문다) — 보는 점을 맞춘 뒤 나머지를 견준다
                Debug.Log("[UiShots] 끄기: 카메라의 보는 점 " + s.Rig.Target.ToString("F2") + " (보통 속도 " + looked.ToString("F2") + ")");
                if ((s.Rig.Target - looked).magnitude < 0.01f) throw new InvalidOperationException("끄기인데 카메라가 그 연출을 따라갔다");
                s.Rig.LookAt(looked);
            }
            string sped = EndText(s, flow);
            flow.FxSpeed = FxSpeed.Normal;
            if (plain == sped) return;
            string[] a = plain.Split('\n'), b = sped.Split('\n');
            int at = 0;
            while (at < a.Length && at < b.Length && a[at] == b[at]) at++;
            throw new InvalidOperationException(speed + ": 끝 모습이 보통 속도와 다르다(" + at + "번째 줄): " + (at < a.Length ? a[at] : "-") + " ≠ " + (at < b.Length ? b[at] : "-"));
        }

        // 대기열이 빌 때까지 + 6초(토스트가 내려간 뒤)를 보낸 뒤의 모습을 글로: 맨 위 화면 · 열린 선택 · 카메라 · 판 위 피규어 · DS 포켓몬 · 보이는 화면 요소(SkipDump)
        static string EndText(UiShot s, GameFlow flow)
        {
            var seq = flow.Fx.Sequencer;
            for (int n = 0; n < 400 && !seq.IsEmpty; n++) Pass(flow, 250);
            for (int n = 0; n < 24; n++) Pass(flow, 250);
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var choice = flow.Client.FirstChoice();
            var text = new StringBuilder();
            text.Append("대기열 ").Append(seq.IsEmpty).Append(" · 화면 ").Append(flow.Stage.Current != null ? flow.Stage.Current.Name : "보드").Append(" · 입력 ").Append(flow.BoardOpen)
                .Append(" · 선택 ").Append(choice != null ? choice.Kind + "/" + choice.Sub : "없음").Append('\n');
            if (s.Rig != null) text.Append("카메라 ").Append(s.Rig.transform.position.ToString("F2")).Append(' ').Append(s.Rig.transform.eulerAngles.ToString("F1")).Append('\n');
            if (s.Board != null && s.Board.Figures != null)
                foreach (var f in s.Board.Figures.Figures)
                    if (f.Root != null)
                        text.Append("피규어 ").Append(f.Key).Append(' ').Append(f.Root.activeSelf).Append(' ').Append(f.Root.activeSelf ? f.Root.transform.localPosition.ToString("F2") + " " + f.Root.transform.localScale.ToString("F3") : "").Append('\n');
            if (flow.Stage.Current is BattleScreen battle && battle.Painted != null)
                foreach (var mon in new[] { battle.Painted.Enemy, battle.Painted.Player })
                    text.Append("DS ").Append(mon == null ? "없음" : mon.Key + " " + mon.Standing + " 깊이 " + mon.Sink.ToString("F2") + " 흰 빛 " + mon.White.ToString("F2") + " 체력 " + mon.Hp + "/" + mon.MaxHp).Append('\n');
            EndDump(flow.Stage.Root, text);
            return text.ToString();
        }

        // 보이는 화면 요소만(이름 · 클래스 · 글 · 자리 · 불투명도) — 숨긴 것 · 다 흐려진 것(연출 층의 쉬는 요소)은 그 아래까지 뺀다
        static void EndDump(VisualElement e, StringBuilder text)
        {
            if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == Visibility.Hidden || e.resolvedStyle.opacity <= 0f) return;
            var box = e.worldBound;
            text.Append(e.name).Append('|').Append(string.Join(" ", e.GetClasses())).Append('|').Append(e is TextElement t ? t.text : "").Append('|')
                .Append(Math.Round(box.x)).Append(',').Append(Math.Round(box.y)).Append(',').Append(Math.Round(box.width)).Append(',').Append(Math.Round(box.height)).Append('|')
                .Append(Math.Round(e.resolvedStyle.opacity, 2)).Append('\n');
            foreach (var child in e.Children()) EndDump(child, text);
        }

        // P29: 시작의 사당의 신(기라티나)과 싸워 첫 폼을 쓰러뜨린 항목 — 그 기절 단계 안(end = 둘째 폼 단계가 시작되기 1ms 앞). 다른 포켓몬이면 가라앉아 사라질 때 신의 첫 폼은 선 채다
        static void GodFaint(UiShot s, bool end)
        {
            var flow = GodBattle(s, -1, 0);
            FxStep form = null;
            Fight(flow, screen => (form = Queued(flow, st => st.Row == ResultRows.GodPhase)) != null);
            var changed = FormBeats.Changed(form) ?? throw new InvalidOperationException("둘째 폼 단계에 폼 바뀜이 없다");
            var seq = flow.Fx.Sequencer;
            FxEntry held = null;
            FxStep faint = null;
            foreach (var entry in seq.Queued)
                if (entry.Steps.Contains(form))
                {
                    held = entry;
                    foreach (var st in entry.Steps)
                        if (st.StartMs < form.StartMs && st.Events.Exists(e => e is Fainted f && f.PokemonId == changed.PokemonId)) faint = st;
                }
            if (faint == null) throw new InvalidOperationException("둘째 폼 단계 앞에 그 포켓몬의 기절 단계가 없다");
            if (ReferenceEquals(held, seq.Current) && seq.LocalMs > faint.StartMs) throw new InvalidOperationException("기절 단계를 이미 지났다(" + seq.LocalMs + " > " + faint.StartMs + ")");
            int ms = end ? form.StartMs - faint.StartMs - 1 : Math.Min(BattleBeats.FaintSinkMs, faint.Ms - 1);
            Laid(s, flow, faint, ms);
            if (!ReferenceEquals(seq.Current, held) || seq.LocalMs != faint.StartMs + ms) throw new InvalidOperationException("기절 단계 안이 아니다(" + seq.LocalMs + " / " + (faint.StartMs + ms) + ")");
            if (!FormBeats.SlumpAhead(seq.Current, seq.LocalMs, changed.PokemonId)) throw new InvalidOperationException("둘째 폼 단계가 뒤에 남았는데 SlumpAhead가 아니다");
            var battle = (BattleScreen)flow.Stage.Current;
            var enemy = battle.Painted.Enemy;
            if (enemy == null || !enemy.Standing) throw new InvalidOperationException("기절 단계에서 신의 첫 폼이 서 있지 않다");
            if (enemy.Sink != 0f || enemy.Key != "487:1" || battle.FormShown) throw new InvalidOperationException("기절 단계의 첫 폼: 깊이 " + enemy.Sink + " · " + enemy.Key + " · 폼 연출 " + battle.FormShown);
            if (enemy.Hp != 0) throw new InvalidOperationException("쓰러진 첫 폼의 체력이 0이 아니다: " + enemy.Hp);
            Debug.Log("[UiShots] p6c-29-faint 기절 단계 " + faint.Row + "행 " + faint.Ms + "ms 안 " + ms + "ms · 둘째 폼 단계까지 " + (form.StartMs - faint.StartMs - ms) + "ms");
        }

        // P06의 실제 이벤트 길: 손님 로비의 「내 주인공」 칸에 ClickEvent → 방에 PickProtagonist 한 통. 자리 줄의 도트(보기만)는 눌러도 보내지 않는다
        static void LobbyClick(UiShot s)
        {
            const string Key = "b2w2_001_rosa";
            var lobby = FakeLobby.Guest();
            var flow = RoomLobby(s, lobby);
            var root = flow.Stage.Root;
            var opt = root.Q("lobby-proto-" + Key) ?? throw new InvalidOperationException("「내 주인공」 칸이 없다");
            int before = lobby.Sent.Count;
            Send(ClickEvent.GetPooled(), opt);
            if (lobby.Sent.Count != before + 1 || !(lobby.Sent[before] is PickProtagonist pick) || pick.Key != Key) throw new InvalidOperationException("칸을 눌렀는데 방에 보내지 않았다(" + (lobby.Sent.Count - before) + "통)");
            var dot = root.Q("lobby-seat-0-proto") ?? throw new InvalidOperationException("자리 줄의 주인공 도트가 없다");
            Send(ClickEvent.GetPooled(), dot);
            if (lobby.Sent.Count != before + 1) throw new InvalidOperationException("자리 줄의 도트를 눌렀는데 방에 보냈다");
            // 방이 돌려준 방 상태 → 금색 칸
            var next = new RoomState { Phase = RoomPhase.Lobby, RoomId = lobby.Room.RoomId, Settings = lobby.Room.Settings, Seats = new List<RoomSeat>(lobby.Room.Seats) };
            next.Seats[lobby.Seat] = new RoomSeat { Nickname = next.Seats[lobby.Seat].Nickname, Connected = true, Protagonist = Key };
            lobby.Room = next;
            var screen = (LobbyScreen)flow.Stage.Current;
            screen.Sync();
            RoomFit(s, flow, "lobby-col");
            opt = root.Q("lobby-proto-" + Key);
            if (screen.ProtagonistShown(lobby.Seat) != Key || opt == null || !opt.ClassListContains("sn-opt-picked")) throw new InvalidOperationException("고른 주인공이 줄 · 칸에 보이지 않는다");
        }

        // P09의 실제 이벤트 길: 방 판(가짜 방)의 내 조우 창 — 카드에 PointerEnterEvent → 방에 「가리킴」(그 카드의 공개 후보 자리) · PointerLeaveEvent → 「떠남」(−1)
        static void PeekEnter(UiShot s)
        {
            var flow = Seats(s, 2, st => PeekSpotFor(s, st, 0));
            long now = RoomNow;
            var live = new RoomLive(() => now) { Seat = 0, IsHost = true, Room = FakeGame(flow, true).Room, Clock = FakeGame(flow, true).Clock };
            flow.Room.ShowGame(live);
            var screen = PeekMine(s, flow);
            s.Panel.Tick(3);
            var choice = flow.Client.Choice(ChoiceKind.EncounterPick);
            int n = screen.Cards.Count;
            // 포인터 들어옴 · 나감은 카드를 품은 자리 칸이 받는다(다듬기 2 — 들린 카드가 포인터 밑을 벗어나도 깜빡이지 않게). Enter · Leave는 부모로 올라가지 않으니 자리 칸에 보낸다
            var card = flow.Stage.Root.Q("encounter-slot-" + n) ?? throw new InvalidOperationException("조우 카드 자리 칸이 없다");
            int before = live.Sent.Count;
            Send(PointerEnterEvent.GetPooled(), card);
            if (live.Sent.Count != before + 1 || !(live.Sent[before] is SendPeek enter) || enter.Spot != PeekSpot.Encounter || enter.Press || enter.Index != choice.Options.IndexOf(screen.Cards[n - 1].Target))
                throw new InvalidOperationException("카드에 마우스가 들어왔는데 방에 「가리킴」이 가지 않았다(" + (live.Sent.Count - before) + "통)");
            now += PeekRate.GapMs;
            Send(PointerLeaveEvent.GetPooled(), card);
            if (live.Sent.Count != before + 2 || !(live.Sent[before + 1] is SendPeek leave) || leave.Index != -1) throw new InvalidOperationException("마우스가 떠났는데 방에 「떠남」이 가지 않았다(" + (live.Sent.Count - before) + "통)");
            now += PeekRate.GapMs;
            Send(PointerEnterEvent.GetPooled(), card);
            if (live.Sent.Count != before + 3) throw new InvalidOperationException("다시 들어온 것이 가지 않았다");
            s.Panel.Tick(3);
        }

        static void Send(EventBase e, VisualElement target)
        {
            using (e)
            {
                e.target = target;
                target.SendEvent(e);
            }
        }
    }
}
