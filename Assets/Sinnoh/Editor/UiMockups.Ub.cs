using System;
using System.Collections.Generic;
using System.IO;
using Rekkon.UiKit;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    // 목업 전용(M6 「목업 UB · 해금 연출」, 04 §7 7 · §8-7 · §8-8 48행) — 제품 화면이 아니다. 고른 안을 화면으로 만들 때 이 파일은 지운다.
    //   S56 균열 게이지 · S57 UB 턴 · S58 균열 카드 · S59·S61 축복·탈락 · 48행 해금의 배치 안을 실제 울트라 버스트 판(4인, 시드 UbSeed) 위에 그려 창 없이 찍는다.
    //   판은 셋업 뒤 모든 좌석이 [턴 종료]만 하며 라운드를 돌려(UB 턴은 엔진 그대로) 왜곡 균열 카드가 두 번 돌 때까지 보낸 실제 상태다 —
    //   균열 강도·프리즘·축복·UB 자리·손에 든 균열 카드·붙은 균열 카드·마지막 UB 턴의 이동과 파괴는 모두 그 판에서 읽는다.
    //   배치: Unity.exe -batchmode -projectPath . -executeMethod Sinnoh.Editor.UiMockups.RunUb -logFile <log> [-mockOut <폴더>] [-mockOnly <이름 일부>]
    //   출력: <폴더>/M<n>-<설명>-<720|1080>.png (기본 폴더 Logs/Review/m6/ask1) · 로그의 [UiMockups] 줄에 판 값
    public static partial class UiMockups
    {
        const string UbOutRoot = "Logs/Review/m6/ask1";
        public const ulong UbSeed = 20261117;
        const int UbSeats = 4;

        static readonly Color Breach = Hex(0x9048d0), Danger = Hex(0xd83848), Ink = Hex(0x283038), Night = new Color(16 / 255f, 12 / 255f, 32 / 255f, 0.55f);

        sealed class UbScene
        {
            public string File;
            public bool Attach;          // 균열 카드 붙이기가 열린 순간에 멈춘다(아니면 그 뒤 내 턴)
            public int Eliminate = -1;   // 이 좌석을 탈락시킨 손본 판(S61 견본)
            public string Over;          // UiShots 장면 위에 얹는다(그 장면의 판)
            public Action<VisualElement> Build;
        }

        static readonly UbScene[] UbScenes =
        {
            new UbScene { File = "M1-위띠칩", Build = l => { GaugeChip(true); } },
            new UbScene { File = "M1-오른쪽세로막대", Build = GaugeSide },
            new UbScene { File = "M1-아래판위띠", Build = GaugeStrip },
            new UbScene { File = "M2-보드위연출", Build = UbOnBoard },
            new UbScene { File = "M2-가운데띠요약", Build = UbSummary },
            new UbScene { File = "M3-파티칸에붙이기", Attach = true, Build = AttachDock },
            new UbScene { File = "M3-가운데창", Attach = true, Build = AttachCentre },
            new UbScene { File = "M3-오른쪽기둥", Attach = true, Build = AttachPillar },
            new UbScene { File = "M4-탈락과축복", Eliminate = 3, Build = Blessing },
            new UbScene { File = "M5-결과창한줄", Over = "battle-captured", Build = UnlockResult },
            new UbScene { File = "M5-카드뒤집기-자물쇠", Build = l => UnlockFlip(l, 0) },
            new UbScene { File = "M5-카드뒤집기-뒤집힘", Build = l => UnlockFlip(l, 1) },
            new UbScene { File = "M5-카드뒤집기-등급테두리", Build = l => UnlockFlip(l, 2) },
        };

        public static void RunUb()
        {
            bool ok = false;
            try
            {
                string outRoot = Arg("-mockOut", UbOutRoot), only = Arg("-mockOnly", "");
                Directory.CreateDirectory(outRoot);
                var logs = new List<string>();
                int images = 0;
                foreach (var scene in UbScenes)
                {
                    if (only != "" && !scene.File.Contains(only)) continue;
                    for (int g = 0; g < 2; g++)   // 720 · 1080만
                    {
                        var grade = UiShots.Grades[g];
                        var px = UiShots.Shoot(s => ComposeUb(s, scene), true, grade, logs, grade.Width, grade.Height);
                        if (px == null) throw new InvalidOperationException("Gen 없음(글꼴·창틀)");
                        UiShots.WritePng(Path.Combine(outRoot, scene.File + "-" + grade.Height + ".png"), px, grade.Width, grade.Height);
                        images++;
                    }
                    if (scene.Over == null) Debug.Log("[UiMockups] " + scene.File + " 판: " + facts);
                }
                foreach (string log in logs) Debug.LogWarning("[UiMockups] 배치 흔들림: " + log);
                Debug.Log("[UiMockups] 그림 " + images + "장 → " + outRoot);
                ok = images > 0;
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { UiShots.CloseBoard(); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        // ── 실제 판 ──

        sealed class UbMove { public int Def, From, To; }
        sealed class UbHit { public int Cell, By; public TokenKind Kind; }

        sealed class UbFacts
        {
            public int Rounds, Breach, Targets, Destroyed, NextNeed, PrismBurst, PrismBeast, Blessings, Phase;
            public int TurnRound, TurnBefore, TurnAfter, TurnMoves, Strike, Dealt;
            public readonly List<UbMove> Moves = new List<UbMove>();
            public readonly List<UbHit> Hits = new List<UbHit>();
            public readonly List<int> Hand = new List<int>();   // 좌석 0이 손에 든 균열 카드(카드 색인)

            public override string ToString()
            {
                string hand = "";
                foreach (int c in Hand) hand += (hand == "" ? "" : ",") + c;
                return "시드 " + UbSeed + " · 4인 · 라운드 " + Rounds + " · 균열 " + Breach + "% (파괴 " + Destroyed + "/" + Targets + ", 다음 10%까지 " + NextNeed + ")"
                    + " · 프리즘 " + PrismBurst + "/" + PrismBeast + " · 축복 " + Blessings + " · 네크로즈마 " + Phase + "페이즈"
                    + " · 마지막 UB 턴(라운드 " + TurnRound + "): " + TurnMoves + "칸, 이동 " + Moves.Count + " · 파괴 " + Hits.Count + "(네크로즈마 " + Strike + ") · 균열 " + TurnBefore + "→" + TurnAfter + " · 카드 " + Dealt + "장씩"
                    + " · 좌석 1 손 [" + hand + "]";
            }
        }

        static UbFacts facts = new UbFacts();

        // 셋업 뒤 모든 좌석이 [턴 종료]만 한다(그 밖의 선택은 시간 초과 기본 답 — 균열 카드 붙이기는 첫 빈 칸). 균열 카드가 두 번 돌면 멈춘다:
        // attach = 둘째 카드를 붙이는 동시 선택이 열린 순간, 아니면 그 카드를 붙인 뒤 좌석 0의 내 턴
        static void Advance(GameState st, Ctx ctx, bool attach)
        {
            facts = new UbFacts();
            var log = new EventLog();
            int deals = 0;
            for (int guard = 0; ; guard++)
            {
                if (guard > 4000) throw new InvalidOperationException("균열 카드가 두 번 돌지 않는다");
                if (st.Phase == Phase.GameOver) throw new InvalidOperationException("판이 끝났다(균열 70%?)");
                var c = st.Pending.Find(x => !x.Answered && x.Kind != ChoiceKind.MapAction) ?? st.Pending.Find(x => !x.Answered);
                if (c == null) throw new InvalidOperationException("열린 선택이 없다");
                if (deals >= 2)
                {
                    if (attach && c.Kind == ChoiceKind.Simultaneous && c.Sub == ChoiceSub.BreachAttach) break;
                    if (!attach && c.Kind == ChoiceKind.MapAction && c.Seat == 0) break;
                }
                log.Clear();
                var r = c.Kind == ChoiceKind.MapAction
                    ? Engine.Submit(st, ctx, c.Seat, Command.AnswerTo(c.Id, ChoiceAnswer.Of(AnswerKind.EndTurn)), log)
                    : Engine.Submit(st, ctx, SeatId.System, Command.TimeoutOf(c.Id), log);
                if (!r.Accepted) throw new InvalidOperationException("거절: " + c.Kind + "/" + c.Sub + " " + r.Reason);
                var started = log.OfType<UbTurnStarted>();
                if (started.Count > 0) Remember(log, started[0]);
                if (log.OfType<BreachCardsDealt>().Count > 0) deals++;
            }
            facts.Rounds = st.Round;
            facts.Breach = UbTurn.Breach(st);
            foreach (var t in st.Tokens)
                if (UbTurn.IsTarget(t.Kind) && t.Cell >= 0) { facts.Targets++; if (t.Destroyed) facts.Destroyed++; }
            int next = (facts.Breach / 10 + 1) * 10;
            for (int d = facts.Destroyed; d <= facts.Targets; d++)
                if (UltraBurstRules.BreachStrength(d, facts.Targets) >= next) { facts.NextNeed = d - facts.Destroyed; break; }
            facts.PrismBurst = st.Ub.PrismUltraBurst;
            facts.PrismBeast = st.Ub.PrismUltraBeast;
            facts.Blessings = st.Ub.Blessings;
            facts.Phase = st.Ub.NecrozmaPhase;
            facts.Hand.AddRange(UbTurn.BreachHand(st, 0));
        }

        static void Remember(EventLog log, UbTurnStarted started)
        {
            facts.Moves.Clear();
            facts.Hits.Clear();
            facts.TurnRound = started.Round;
            facts.TurnMoves = started.TotalMoves;
            facts.TurnBefore = facts.TurnAfter = started.Breach;
            facts.Strike = 0;
            facts.Dealt = 0;
            foreach (var e in log.Events)
            {
                if (e is UbMoved m) facts.Moves.Add(new UbMove { Def = m.UbId, From = m.From, To = m.To });
                else if (e is CellDestroyed d) facts.Hits.Add(new UbHit { Cell = d.Cell, By = d.ByUbId, Kind = d.TokenKind });
                else if (e is NecrozmaStrike n) facts.Strike = n.Cells != null ? n.Cells.Length : 0;
                else if (e is BreachChanged b) facts.TurnAfter = b.After;
                else if (e is BreachCardsDealt dealt && dealt.PerSeat != null && dealt.PerSeat.Length > 0) facts.Dealt = dealt.PerSeat[0].CardKeys.Length;
            }
        }

        static void ComposeUb(UiShot s, UbScene scene)
        {
            shot = s;
            k = s.Grade.TokenHeight == 1080 ? 1.5f : 1f;
            hover = null;
            if (scene.Over != null)
            {
                Action<UiShot> build = null;
                foreach (var e in UiShots.AllScreens) if (e.Key == scene.Over) build = e.Value;
                if (build == null) throw new InvalidOperationException("UiShots 장면 없음: " + scene.Over);
                build(s);
                flow = s.Made;
            }
            else
            {
                var ctx = s.Board.Ctx;
                flow = s.Flow();
                flow.StartSeats(new SoloSettings { Campaign = Campaign.UltraBurst }, UbSeats, UbSeed, UiShots.AiSeed, st =>
                {
                    Advance(st, ctx, scene.Attach);
                    if (scene.Eliminate >= 0) st.Seats[scene.Eliminate].Eliminated = true;
                });
                flow.Layout(s.Width, s.Height);
                flow.Stage.Tweens.Advance(flow.OpeningMs);
                flow.Stage.Tweens.Advance(UiShots.BoardReadyMs);
                flow.Layout(s.Width, s.Height);
                PlaceBeasts(s, null);
            }
            stage = flow.Stage;
            s.Panel.Tick(3);
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            Inset(layer, 0, 0, 0, 0);
            stage.Root.Add(layer);
            scene.Build(layer);
            s.Panel.Tick(3);
            if (hover != null)
            {
                stage.Tips.Element.BringToFront();
                stage.Tips.Show(hover);
            }
        }

        // UB 피규어를 판의 UB 토큰 칸에 세운다(보드는 아직 M4-2 배치 자리에 세워 둔다 — UB 보드 화면 묶음 몫). moved = 이 UB(포켓몬 색인)만 그 칸에
        static void PlaceBeasts(UiShot s, UbMove moved)
        {
            if (s.Board == null || s.Board.Figures == null) return;
            var view = flow.Client.View;
            foreach (var t in view.Tokens)
            {
                if ((t.Kind != TokenKind.UltraBeast && t.Kind != TokenKind.Necrozma) || t.Cell < 0) continue;
                int cell = moved != null && moved.Def == t.Def ? moved.To : t.Cell;
                string key = flow.Ctx.Cards.Pokemon[t.Def].Key;
                foreach (var f in s.Board.Figures.Figures)
                    if (f.Key == key && f.Root != null) f.Root.transform.localPosition = s.Board.CellTop(cell);
            }
        }

        // ── 작은 도구 ──

        static Rect Bound(string name)
        {
            var e = stage.Root.Q(name);
            if (e == null) throw new InvalidOperationException("HUD 요소 없음: " + name);
            return e.worldBound;
        }

        static void At(VisualElement e, Rect r)
        {
            e.style.position = Position.Absolute;
            e.style.left = r.x;
            e.style.top = r.y;
            e.style.width = r.width;
            e.style.height = r.height;
        }

        static string UbName(int def) => BoardHud.ShortName(stage.T("pokemon." + flow.Ctx.Cards.Pokemon[def].Key));

        static string HitName(TokenKind kind) => kind == TokenKind.Shrine ? "사당" : kind == TokenKind.ApricornReef ? "규토리 산호" : "규토리 나무";

        static string CardKey(int card) => flow.Ctx.Decks.CardIds[card];

        // 균열 막대: 0~70%를 10% 일곱 칸으로(마지막 칸 = 패배 앞 빨강). len 0 = 늘어남
        static VisualElement GaugeBar(VisualElement parent, float len, float thick, bool vertical, int breach)
        {
            var track = Box(parent);
            track.style.flexDirection = vertical ? FlexDirection.ColumnReverse : FlexDirection.Row;
            if (vertical) { track.style.width = P(thick); if (len > 0) track.style.height = P(len); else track.style.flexGrow = 1; }
            else { track.style.height = P(thick); if (len > 0) track.style.width = P(len); else track.style.flexGrow = 1; }
            track.style.backgroundColor = Steel;
            Border(track, Edge, 1);
            for (int i = 0; i < 7; i++)
            {
                var seg = Add(track, new VisualElement());
                seg.style.flexGrow = 1;
                if (i < 6)
                {
                    if (vertical) { seg.style.borderTopWidth = 1; seg.style.borderTopColor = Line; }
                    else { seg.style.borderRightWidth = 1; seg.style.borderRightColor = Line; }
                }
                if (i == 6) seg.style.backgroundColor = new Color(Danger.r, Danger.g, Danger.b, 0.25f);
                float f = Mathf.Clamp01((breach - i * 10) / 10f);
                if (f <= 0) continue;
                var fill = Add(seg, new VisualElement());
                fill.style.position = Position.Absolute;
                fill.style.left = 0;
                fill.style.bottom = 0;
                if (vertical) { fill.style.right = 0; fill.style.height = Length.Percent(f * 100); }
                else { fill.style.top = 0; fill.style.width = Length.Percent(f * 100); }
                fill.style.backgroundColor = i >= 6 ? Danger : Breach;
            }
            return track;
        }

        const string BreachTitle = "균열 강도";

        static string BreachTip() =>
            "70%가 되면 패배합니다.\n10%를 넘을 때마다 모두 왜곡 균열 카드를 받습니다.\n다음 10%까지: 나무·사당 " + facts.NextNeed + "개\n다음 UB 턴: " + UltraBurstRules.TotalMoves(facts.Breach) + "칸 이동";

        static string PrismTip() => "네크로즈마 " + facts.PrismBurst + " · 울트라 비스트 " + facts.PrismBeast + "\nUB를 쓰러뜨릴 때마다 1개가 울트라 비스트 쪽으로 옮겨 갑니다.";

        const string BlessTip = "탈락한 트레이너를 부활시키거나 다음 UB 턴을 건너뜁니다.";

        static Label PrismBadge(VisualElement parent)
        {
            var b = Badge(parent, "프리즘 " + facts.PrismBurst, KitBadgeKind.Silver);
            stage.Tips.Hook(b, "프리즘 아머", PrismTip(), TipSide.Below, reading: true);
            return b;
        }

        static Label BlessBadge(VisualElement parent)
        {
            var b = Badge(parent, "축복 " + facts.Blessings, KitBadgeKind.Gold);
            stage.Tips.Hook(b, "아르세우스의 축복", BlessTip, TipSide.Below, reading: true);
            return b;
        }

        // ── M1 균열 게이지(S56) ──

        // 가 — 위 띠 오른쪽(상태 줄 안, [메뉴] 앞)에 칩: 「균열 ▮▮▯ 23%」 · 프리즘 · 축복
        static VisualElement GaugeChip(bool tip)
        {
            var status = stage.Root.Q("board-hud-status");
            var menu = status.Q("board-hud-menu");
            int at = status.IndexOf(menu);
            var chip = new VisualElement();
            chip.style.flexDirection = FlexDirection.Row;
            chip.style.alignItems = Align.Center;
            chip.style.flexShrink = 0;
            Margin(chip, 4, 0, 2, 0);
            Text(chip, "균열");
            var bar = GaugeBar(chip, 56, 8, false, facts.Breach);
            Margin(bar, 4, 0, 4, 0);
            Text(chip, facts.Breach + "%", "kit-title");
            status.Insert(at, chip);
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Row;
            group.style.alignItems = Align.Center;
            group.style.flexShrink = 0;
            PrismBadge(group);
            BlessBadge(group);
            Margin(group, 0, 0, 4, 0);
            status.Insert(at + 1, group);
            stage.Tips.Hook(chip, BreachTitle + " " + facts.Breach + "%", BreachTip(), TipSide.Below, reading: true);
            if (tip) hover = chip;
            return chip;
        }

        static float TopY => k > 1 ? 101 : 56;

        // 나 — 보드 오른쪽 가장자리 세로 막대: 위 = 70% 패배선, 아래에서 차오름. 막대 아래 프리즘 · 축복
        static void GaugeSide(VisualElement layer)
        {
            var panel = Add(layer, Kit.Panel(stage.Skin));
            panel.style.position = Position.Absolute;
            panel.style.top = TopY;
            panel.style.right = P(4);
            panel.style.bottom = P(134);
            panel.style.width = P(80);
            panel.style.alignItems = Align.Center;
            Text(panel, "균열");
            Text(panel, facts.Breach + "%", "kit-title", Kit.F3);
            var body = Box(panel, true);
            body.style.flexGrow = 1;
            body.style.alignItems = Align.Stretch;
            Margin(body, 0, 4, 0, 4);
            var marks = Box(body);
            marks.style.justifyContent = Justify.SpaceBetween;
            marks.style.marginRight = P(3);
            for (int v = 70; v >= 0; v -= 10)
            {
                var m = Text(marks, v.ToString(), v == 70 ? null : "kit-muted");
                if (v == 70) m.style.color = Danger;
            }
            GaugeBar(body, 0, 14, true, facts.Breach);
            PrismBadge(panel).style.marginLeft = 0;
            var bless = BlessBadge(panel);
            bless.style.marginLeft = 0;
            bless.style.marginTop = P(3);
        }

        // 다 — 아래 판 바로 위 가로 띠: 「균열 강도」 + 긴 막대(10% 눈금, 70% 패배) + 값 · 프리즘 · 축복
        static void GaugeStrip(VisualElement layer)
        {
            var panel = Add(layer, Kit.Panel(stage.Skin));
            Inset(panel, 4, null, 4, 128);
            panel.style.flexDirection = FlexDirection.Row;
            panel.style.alignItems = Align.Center;
            Text(panel, BreachTitle, "kit-title").style.marginRight = P(6);
            var mid = Box(panel);
            mid.style.flexGrow = 1;
            mid.style.flexShrink = 1;
            GaugeBar(mid, 0, 10, false, facts.Breach).style.alignSelf = Align.Stretch;
            var scale = Box(mid, true);
            for (int v = 10; v <= 70; v += 10)
            {
                var cell = Box(scale);
                cell.style.flexGrow = 1;
                cell.style.alignItems = Align.FlexEnd;
                var m = Text(cell, v == 70 ? "70% 패배" : v + "%", v == 70 ? null : "kit-muted");
                if (v == 70) m.style.color = Danger;
            }
            Text(panel, facts.Breach + "%", "kit-title", Kit.F3).style.marginLeft = P(8);
            PrismBadge(panel).style.marginLeft = P(8);
            BlessBadge(panel);
        }

        // ── M2 UB 턴(S57) ──

        static int MidMove => facts.Moves.Count == 0 ? -1 : Math.Min(facts.Moves.Count - 1, Math.Max(0, facts.Moves.Count / 2));

        // 가 — 보드 위 연출 + 기록 줄: 이동 중인 UB 위에 「자동」, 위 가운데 작은 띠 「울트라 비스트의 턴 · 이동 n/m」, 기록 줄에 한 칸씩
        static void UbOnBoard(VisualElement layer)
        {
            int mid = MidMove;
            if (mid < 0) throw new InvalidOperationException("마지막 UB 턴에 이동이 없다");
            var move = facts.Moves[mid];
            PlaceBeasts(shot, move);
            GaugeChip(false);
            // 위 가운데 띠
            var holder = Add(layer, new VisualElement { pickingMode = PickingMode.Ignore });
            holder.style.position = Position.Absolute;
            holder.style.left = 0;
            holder.style.right = 0;
            holder.style.top = TopY;
            holder.style.alignItems = Align.Center;
            var band = Add(holder, Kit.Panel(stage.Skin));
            band.style.flexDirection = FlexDirection.Row;
            band.style.alignItems = Align.Center;
            Text(band, "울트라 비스트의 턴", "kit-title", Kit.FTitle);
            Badge(band, "자동", KitBadgeKind.Silver).style.marginLeft = P(8);
            Badge(band, "이동 " + (mid + 1) + "/" + facts.TurnMoves, KitBadgeKind.Warn);
            // 기록 줄
            var lines = stage.Root.Q("board-hud-log-lines");
            lines.Clear();
            var said = new List<string> { "울트라 비스트의 턴이다!" };
            for (int i = 0; i <= mid; i++)
            {
                var m = facts.Moves[i];
                said.Add(KoJosa.Fix(UbName(m.Def) + "이(가) 움직였다!"));
                foreach (var h in facts.Hits)
                    if (h.Cell == m.To && h.By == m.Def) said.Add(KoJosa.Fix(UbName(m.Def) + "이(가) " + HitName(h.Kind) + "을(를) 쓰러뜨렸다!"));
            }
            for (int i = Math.Max(0, said.Count - 3); i < said.Count; i++) Text(lines, said[i]);
            // 이동 중인 UB 머리 위 꼬리표 — 카메라를 그 칸으로
            var cam = shot.Rig.GetComponent<Camera>();
            var top = shot.Board.CellTop(move.To);
            shot.Rig.Set(BoardCameraRig.Zoom.Region, 0, top);
            float tall = 2.2f;
            foreach (var f in shot.Board.Figures.Figures)
                if (f.Key == flow.Ctx.Cards.Pokemon[move.Def].Key) tall = f.FittedSize.y + 0.3f;
            if (BoardPick.ScreenAt(cam, top + Vector3.up * tall, shot.Width, shot.Height, out var px))
            {
                var tag = Add(layer, new VisualElement { pickingMode = PickingMode.Ignore });
                tag.style.position = Position.Absolute;
                tag.style.left = px.x - P(60);
                tag.style.width = P(120);
                tag.style.top = shot.Height - px.y - P(22);
                tag.style.alignItems = Align.Center;
                var row = Box(tag, true);
                Add(row, Kit.Badge(UbName(move.Def), KitBadgeKind.Plain, Kit.F2));
                Badge(row, "자동", KitBadgeKind.Silver);
            }
        }

        // 나 — 가운데 띠 요약: UB 턴이 끝나면 보드 위 가운데에 한 장(어둡게 덮지 않음) — UB마다 칸 수 · 쓰러뜨린 것, 네크로즈마, 균열 변화
        static void UbSummary(VisualElement layer)
        {
            var holder = Add(layer, new VisualElement { pickingMode = PickingMode.Ignore });
            Inset(holder, 0, 150, 0, null);
            holder.style.alignItems = Align.Center;
            var panel = Add(holder, Kit.Panel(stage.Skin));
            var head = Box(panel, true);
            head.style.justifyContent = Justify.SpaceBetween;
            var left = Box(head, true);
            Text(left, "울트라 비스트의 턴", "kit-title", Kit.FTitle);
            Badge(left, "자동", KitBadgeKind.Silver).style.marginLeft = P(8);
            Badge(left, facts.TurnMoves + "칸 이동");
            var tiles = Box(panel, true);
            tiles.style.alignItems = Align.FlexStart;
            Margin(tiles, 0, 4, 0, 4);
            var order = new List<int>();
            foreach (var m in facts.Moves) if (!order.Contains(m.Def)) order.Add(m.Def);
            var art = MonArt.Load();
            foreach (int def in order)
            {
                int steps = 0;
                foreach (var m in facts.Moves) if (m.Def == def) steps++;
                var hits = new Dictionary<TokenKind, int>();
                foreach (var h in facts.Hits) if (h.By == def) hits[h.Kind] = hits.TryGetValue(h.Kind, out int n) ? n + 1 : 1;
                var tile = Sec(tiles, UbName(def), out var tileHead);
                tile.style.alignItems = Align.Center;
                tile.style.minWidth = P(96);
                if (art != null) tile.Add(art.Front(flow.Ctx.Cards.Pokemon[def].Key));
                Text(tile, steps + "칸").style.marginTop = P(2);
                foreach (var kv in hits) Add(tile, Kit.Badge(HitName(kv.Key) + " " + kv.Value, KitBadgeKind.Red, Kit.F2)).style.marginTop = P(2);
            }
            if (facts.Strike > 0)
            {
                var necro = Sec(tiles, "네크로즈마", out var necroHead);
                necro.style.alignItems = Align.Center;
                necro.style.minWidth = P(96);
                if (art != null) necro.Add(art.Front("800:1"));
                Add(necro, Kit.Badge("원격 파괴 " + facts.Strike, KitBadgeKind.Red, Kit.F2));
            }
            var foot = Box(panel, true);
            foot.style.marginTop = P(4);
            Text(foot, BreachTitle, "kit-title");
            Text(foot, facts.TurnBefore + "% →").style.marginLeft = P(6);
            Text(foot, facts.TurnAfter + "%", "kit-title", Kit.F3).style.marginLeft = P(4);
            var bar = GaugeBar(foot, 140, 10, false, facts.TurnAfter);
            Margin(bar, 8, 0, 0, 0);
            if (facts.Dealt > 0) Badge(foot, "왜곡 균열 카드 " + facts.Dealt + "장씩", KitBadgeKind.Warn).style.marginLeft = P(10);
        }

        // ── M3 균열 카드 붙이기(S58) ──

        sealed class Slot { public int Mon, Index, Move = -1, Card = -1; }

        // 좌석 0 파티의 기술 칸(배운 기술 둘). 고른 칸 = 시간 초과 기본 답(빈 칸이 있으면 첫 빈 칸, 없으면 첫 칸)
        static List<Slot> Slots(out Slot picked)
        {
            var view = flow.Client.View;
            var list = new List<Slot>();
            foreach (int id in view.Seats[0].Party)
            {
                if (id < 0 || id >= view.Pokemon.Count || view.Pokemon[id].Def < 0) continue;
                var p = view.Pokemon[id];
                for (int i = 0; i < 2; i++) list.Add(new Slot { Mon = id, Index = i, Move = p.Moves[i], Card = p.Breach[i] });
            }
            picked = list.Find(x => x.Card < 0 && x.Move < 0) ?? list.Find(x => x.Card < 0);
            return list;
        }

        static string CardName(int card) => stage.T(CardKey(card));
        static string CardText(int card) => stage.T(CardKey(card) + ".text.2");

        // 기술 칸 하나: 배운 기술 / 빈 칸 / 붙은 균열 카드(보라)
        static VisualElement SlotChip(VisualElement parent, Slot slot, bool picked, int incoming)
        {
            var chip = Chip(parent, picked);
            chip.style.minWidth = P(120);
            if (slot.Card >= 0)
            {
                chip.style.backgroundColor = new Color(Breach.r, Breach.g, Breach.b, 0.25f);
                Add(chip, Kit.Badge("균열", KitBadgeKind.Red, Kit.F2)).style.marginRight = P(3);
                Text(chip, CardName(slot.Card));
                chip.SetEnabled(false);
            }
            else if (picked)
            {
                Add(chip, Kit.Badge("균열", KitBadgeKind.Red, Kit.F2)).style.marginRight = P(3);
                Text(chip, CardName(incoming), "kit-title");
            }
            else if (slot.Move >= 0) chip.Add(MonParts.MoveRow(stage, flow.Ctx, slot.Move, false, null));
            else Text(chip, "빈 칸", "kit-muted");
            return chip;
        }

        static VisualElement MonBlock(VisualElement parent, int id, List<Slot> slots, Slot picked, int incoming, bool row)
        {
            var view = flow.Client.View;
            var block = Box(parent, true);
            block.style.alignItems = Align.Center;
            Margin(block, 0, 2, 8, 2);
            var art = MonArt.Load();
            if (art != null) block.Add(art.Front(flow.Ctx.Cards.Pokemon[view.Pokemon[id].Def].Key));
            var right = Box(block);
            Text(right, BoardHud.ShortName(stage.T("pokemon." + flow.Ctx.Cards.Pokemon[view.Pokemon[id].Def].Key)));
            var line = Box(right, row);
            foreach (var s in slots) if (s.Mon == id) SlotChip(line, s, s == picked, incoming);
            return block;
        }

        // 좌석 칩 아래 꼬리표: 붙이기 현황(견본 — 실제로는 넷 다 고르는 중이다)
        static void SeatProgress(VisualElement layer)
        {
            for (int seat = 0; seat < UbSeats; seat++)
            {
                var r = Bound("board-hud-seat-" + (seat + 1));
                bool done = seat == 1 || seat == 2;
                var tag = Add(layer, Kit.Badge(done ? "완료" : "고르는 중", done ? KitBadgeKind.Ok : KitBadgeKind.Plain, Kit.F2));
                tag.style.position = Position.Absolute;
                tag.style.right = shot.Width - r.xMax + P(3);
                tag.style.top = r.yMax - P(13);
            }
        }

        // 아래 판 「행동」 칸의 안내 줄(화면이 없어 「준비 중」이 뜬다)을 견본 문구로
        static void AttachGuide()
        {
            var guide = stage.Root.Q<Label>("board-hud-guide");
            if (guide != null) guide.text = "왜곡 균열 카드를 붙이는 중입니다.";
        }

        static void CardFace(VisualElement parent, int card, float width)
        {
            var face = Add(parent, Kit.Panel(stage.Skin));
            if (width > 0) face.style.width = P(width);
            face.style.flexShrink = 0;
            Margin(face, 3, 3, 3, 3);
            var top = Box(face, true);
            top.style.flexWrap = Wrap.Wrap;
            Text(top, CardName(card), "kit-title", Kit.F3);
            var kind = Box(face, true);
            Add(kind, Kit.Badge("왜곡 균열", KitBadgeKind.Red, Kit.F2));
            Badge(kind, "반드시 붙임");
            Wrapped(face, CardText(card)).style.marginTop = P(4);
        }

        // 가 — 아래 판 파티 칸에 붙는 꼴: 파티 칸 자리에 「카드 + 기술 칸」, 「행동」 칸에 안내와 [붙이기]. 카드 효과는 마우스 오버
        static void AttachDock(VisualElement layer)
        {
            int card = facts.Hand.Count > 0 ? facts.Hand[0] : -1;
            if (card < 0) throw new InvalidOperationException("손에 든 균열 카드가 없다");
            var slots = Slots(out var picked);
            var dock = Bound("board-hud-dock");
            var acts = Bound("board-hud-actions");
            var box = Add(layer, Kit.Panel(stage.Skin));
            At(box, new Rect(dock.x + P(2), dock.y + P(2), acts.x - dock.x - P(4), dock.height - P(4)));
            var head = Box(box, true);
            Text(head, "왜곡 균열 카드", "kit-title");
            var cardChip = Chip(head, false);
            cardChip.style.backgroundColor = new Color(Breach.r, Breach.g, Breach.b, 0.25f);
            Margin(cardChip, 6, 0, 0, 0);
            Text(cardChip, CardName(card), "kit-title");
            if (facts.Hand.Count > 1) Badge(head, "+" + (facts.Hand.Count - 1) + "장");
            stage.Tips.Hook(cardChip, CardName(card), CardText(card), TipSide.Above, reading: true);
            hover = cardChip;
            var body = Box(box, true);
            body.style.alignItems = Align.FlexStart;
            var ids = new List<int>();
            foreach (var s in slots) if (!ids.Contains(s.Mon)) ids.Add(s.Mon);
            foreach (int id in ids) MonBlock(body, id, slots, picked, card, true);
            var ask = Add(layer, Kit.Panel(stage.Skin));
            At(ask, new Rect(acts.x, dock.y + P(2), dock.xMax - acts.x - P(2), dock.height - P(4)));
            Text(ask, "균열 카드", "kit-title");
            Wrapped(ask, "카드를 붙일 기술 칸을 선택하십시오.");
            var foot = Box(ask, true);
            foot.style.justifyContent = Justify.SpaceBetween;
            foot.style.marginTop = P(4);
            Timer(foot, "24초");
            Btn(foot, "붙이기", KitButtonKind.Main);
            SeatProgress(layer);
        }

        // 나 — 가운데 고르는 창: 보드를 어둡게 덮고 카드 앞면(효과 글) + 파티 기술 칸 + 모두의 현황
        static void AttachCentre(VisualElement layer)
        {
            int card = facts.Hand[0];
            var slots = Slots(out var picked);
            AttachGuide();
            ScrimOver(layer);
            var holder = Add(layer, new VisualElement());
            Inset(holder, 0, 130, 0, null);
            holder.style.alignItems = Align.Center;
            var panel = Add(holder, Kit.Panel(stage.Skin));
            var left = Head(panel, "왜곡 균열 카드", out var right);
            Badge(left, "모두 동시에");
            Timer(right, "24초");
            var body = Box(panel, true);
            body.style.alignItems = Align.FlexStart;
            CardFace(body, card, 210);
            var mons = Sec(body, "붙일 기술 칸", out var monsHead);
            var ids = new List<int>();
            foreach (var s in slots) if (!ids.Contains(s.Mon)) ids.Add(s.Mon);
            foreach (int id in ids) MonBlock(mons, id, slots, picked, card, true);
            var foot = Box(panel, true);
            foot.style.justifyContent = Justify.SpaceBetween;
            foot.style.marginTop = P(4);
            var seats = Box(foot, true);
            for (int seat = 0; seat < UbSeats; seat++)
            {
                bool done = seat == 1 || seat == 2;
                SeatChip(seats, seat, false, done ? "완료" : "고르는 중", done ? KitBadgeKind.Ok : KitBadgeKind.Plain);
            }
            Btn(foot, "붙이기", KitButtonKind.Main);
        }

        // 다 — 오른쪽 기둥(사당 판과 같은 자리): 보드를 덮지 않고 세로로 카드 · 기술 칸 · 현황
        static void AttachPillar(VisualElement layer)
        {
            int card = facts.Hand[0];
            var slots = Slots(out var picked);
            AttachGuide();
            var panel = Add(layer, Kit.Panel(stage.Skin));
            Inset(panel, null, 68, 4, null);
            panel.style.width = P(300);
            var left = Head(panel, "왜곡 균열 카드", out var right);
            Btn(right, "붙이기", KitButtonKind.Main);
            CardFace(panel, card, 0);
            var mons = Sec(panel, "붙일 기술 칸", out var monsHead);
            var ids = new List<int>();
            foreach (var s in slots) if (!ids.Contains(s.Mon)) ids.Add(s.Mon);
            foreach (int id in ids) MonBlock(mons, id, slots, picked, card, false);
            var wait = Sec(panel, "선택 현황", out var waitHead);
            Badge(waitHead, "2/" + UbSeats);
            for (int seat = 0; seat < UbSeats; seat++)
            {
                bool done = seat == 1 || seat == 2;
                SeatChip(wait, seat, false, done ? "완료" : "고르는 중", done ? KitBadgeKind.Ok : KitBadgeKind.Plain).style.alignSelf = Align.FlexStart;
            }
        }

        // ── M4 탈락 · 축복(S59 · S61) ──

        // 탈락한 좌석 칩은 회색 + 「탈락」, 위 띠 [축복 n]을 누르면 그 아래 작은 판: [UB 턴 건너뛰기] · [부활] 좌석마다
        static void Blessing(VisualElement layer)
        {
            GaugeChip(false);
            var view = flow.Client.View;
            for (int seat = 0; seat < UbSeats; seat++)
            {
                if (!view.Seats[seat].Eliminated) continue;
                var r = Bound("board-hud-seat-" + (seat + 1));
                var veil = Add(layer, new VisualElement { pickingMode = PickingMode.Ignore });
                At(veil, r);
                veil.style.backgroundColor = new Color(0.35f, 0.38f, 0.42f, 0.7f);
                var crack = Add(veil, new VisualElement());
                crack.style.position = Position.Absolute;
                crack.style.left = Length.Percent(30);
                crack.style.top = -P(2);
                crack.style.width = P(2);
                crack.style.height = r.height + P(4);
                crack.style.backgroundColor = Ink;
                crack.style.rotate = new Rotate(new Angle(18));
                var tag = Add(veil, Kit.Badge("탈락", KitBadgeKind.Plain, Kit.F2));
                tag.style.position = Position.Absolute;
                tag.style.right = P(4);
                tag.style.top = P(4);
            }
            var status = Bound("board-hud-status");
            var panel = Add(layer, Kit.Panel(stage.Skin));
            panel.style.position = Position.Absolute;
            panel.style.right = P(4);
            panel.style.top = status.yMax + P(4);
            panel.style.width = P(330);
            var left = Head(panel, "아르세우스의 축복", out var right);
            Badge(left, facts.Blessings + "개", KitBadgeKind.Gold);
            Btn(right, "닫기");
            var skip = Sec(panel, "UB 턴 건너뛰기", out var skipHead);
            Wrapped(skip, "다음 UB 턴에 울트라 비스트가 움직이지 않습니다.", "kit-muted");
            var go = Btn(skip, "건너뛰기", KitButtonKind.Main);
            go.style.alignSelf = Align.FlexEnd;
            var revive = Sec(panel, "부활", out var reviveHead);
            for (int seat = 0; seat < UbSeats; seat++)
            {
                if (!view.Seats[seat].Eliminated) continue;
                var line = Box(revive, true);
                line.style.justifyContent = Justify.SpaceBetween;
                SeatChip(line, seat, false, "탈락");
                Btn(line, "부활", KitButtonKind.Main);
            }
        }

        // ── M5 해금 연출(48행) ──

        // 원작 카드 틀(Gen Cards/Trainer — 1120×641): 파랑 칸(36,136, 364×384) · 빨강 칸(416,136) · 자물쇠(380×400) · 그림 원(오른쪽) · 아래 등급 띠(1120×60)
        const float CardW = 1120f, CardH = 641f;

        static Image Pic(VisualElement parent, string key, float x, float y, float w, float h, float scale)
        {
            var img = Add(parent, new Image { image = GenAssets.Load<Texture2D>(key), scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore });
            img.style.position = Position.Absolute;
            img.style.left = x * scale;
            img.style.top = y * scale;
            img.style.width = w * scale;
            img.style.height = h * scale;
            return img;
        }

        static readonly string[] TierName = { "", "bronze", "silver", "gold" };

        // 트레이너 카드 앞면 한 장(px 폭). tier = 틀 등급, lockBlue/lockRed = 자물쇠, lit = 해금된 칸을 밝힘
        static VisualElement TrainerFace(VisualElement parent, int seat, float width, int tier, bool lockBlue, bool lockRed, bool lit, bool text = true)
        {
            var view = flow.Client.View;
            var def = flow.Ctx.Cards.TrainerCards[view.Seats[seat].TrainerCard];
            float s = width / CardW;
            var card = Add(parent, new VisualElement { pickingMode = PickingMode.Ignore });
            card.style.width = width;
            card.style.height = CardH * s;
            card.style.flexShrink = 0;
            card.style.overflow = Overflow.Hidden;
            Pic(card, "Cards/Trainer/" + TierName[tier] + "_base.png", 0, 0, CardW, CardH, s);
            string front = BoardPieces.FrontKey(def.Key);
            if (front != null)
            {
                var art = Pic(card, front, 760, 150, 340, 420, s);
                art.scaleMode = ScaleMode.ScaleToFit;
            }
            Pic(card, "Cards/Trainer/" + TierName[tier] + "_bar.png", 0, 566, CardW, 60, s);
            var name = Add(card, Kit.Text(text ? stage.T("trainer_card." + def.Key) : "", Kit.F2));
            name.style.position = Position.Absolute;
            name.style.left = 560 * s;
            name.style.top = 68 * s;
            name.style.color = Color.white;
            foreach (var ab in def.Abilities)
            {
                float x = ab.Slot == 1 ? 36 : 416;
                var cell = Add(card, new VisualElement { pickingMode = PickingMode.Ignore });
                cell.style.position = Position.Absolute;
                cell.style.left = (x + 14) * s;
                cell.style.top = (136 + 30) * s;
                cell.style.width = (364 - 28) * s;
                cell.style.height = (384 - 44) * s;
                string key = "trainer_ability." + def.Key + "." + ab.Slot;
                bool locked = ab.Slot == 1 ? lockBlue : lockRed;
                if (!text)
                {
                    if (!locked && lit && ab.Slot == tier - 1) Border(cell, Gold, Mathf.Max(1, P(2)));
                    continue;
                }
                var t = Add(cell, Kit.Text(stage.T(key), Kit.F2));
                t.AddToClassList("kit-title");
                t.style.color = Color.white;
                var body = Add(cell, Kit.Wrapped(stage.T(key + ".text"), Kit.F2));
                body.style.color = Color.white;
                body.style.marginTop = P(2);
                if (locked) cell.style.opacity = 0.35f;
                else if (lit && ab.Slot == tier - 1) Border(cell, Gold, Mathf.Max(1, P(2)));
            }
            return card;
        }

        // 금속광: 카드 위를 비스듬히 쓰는 흰 띠
        static void Sheen(VisualElement card, float at, float width)
        {
            var band = Add(card, new VisualElement { pickingMode = PickingMode.Ignore });
            band.style.position = Position.Absolute;
            band.style.left = Length.Percent(at);
            band.style.top = Length.Percent(-40);
            band.style.width = P(width);
            band.style.height = Length.Percent(180);
            band.style.backgroundColor = new Color(1, 1, 1, 0.55f);
            band.style.rotate = new Rotate(new Angle(24));
        }

        static TrainerCardDef SeatCard(int seat) => flow.Ctx.Cards.TrainerCards[flow.Client.View.Seats[seat].TrainerCard];

        static string BlueName(int seat)
        {
            var def = SeatCard(seat);
            return stage.T("trainer_ability." + def.Key + ".1");
        }

        // 가 — 배틀 결과 창 안 한 줄 + 작은 카드가 반짝임(창은 UiShots battle-captured — 코코파스를 잡은 판을 빌렸다)
        static void UnlockResult(VisualElement layer)
        {
            var lines = stage.Root.Q("battle-result-lines");
            if (lines == null) throw new InvalidOperationException("결과 창이 없다");
            var row = Add(lines, new VisualElement());
            row.style.alignItems = Align.Center;
            row.style.alignSelf = Align.Center;
            row.style.marginTop = P(6);
            var card = TrainerFace(row, 0, P(140), 2, false, true, true, false);
            Sheen(card, 55, 12);
            Border(card, Steel, Mathf.Max(1, P(2)));
            Text(row, "트레이너 카드가 실버가 되었다!", "kit-title").style.marginTop = P(4);
            Text(row, "「" + BlueName(0) + "」 해금");
        }

        // 나 — 보드를 덮고 가운데 큰 카드: 0 = 자물쇠가 흔들림(브론즈) · 1 = 뒤집히는 중(실버 뒷면 + 깨진 자물쇠) · 2 = 실버 앞면 + 금속광 테두리
        static void UnlockFlip(VisualElement layer, int moment)
        {
            var scrim = Add(layer, new VisualElement());
            scrim.AddToClassList("sn-act-scrim");
            var holder = Add(layer, new VisualElement { pickingMode = PickingMode.Ignore });
            Inset(holder, 0, 0, 0, 0);
            holder.style.alignItems = Align.Center;
            holder.style.justifyContent = Justify.Center;
            float w = P(480), s = w / CardW;
            var title = Text(holder, moment == 2 ? "트레이너 카드 실버!" : "트레이너 카드", "kit-title", Kit.FTitle);
            title.style.color = Color.white;
            title.style.marginBottom = P(8);
            if (moment == 2) title.style.color = Steel;
            var stageBox = Add(holder, new VisualElement { pickingMode = PickingMode.Ignore });
            stageBox.style.width = w;
            stageBox.style.height = CardH * s;
            if (moment == 0)
            {
                var card = TrainerFace(stageBox, 0, w, 1, true, true, false);
                var lockImg = Pic(card, "Cards/Trainer/locked_blue.png", 28, 128, 380, 400, s);
                lockImg.style.rotate = new Rotate(new Angle(-7));
                var icon = Pic(stageBox, "Cards/Trainer/icon_moderate.png", 470, 470, 110, 110, s);
                icon.style.rotate = new Rotate(new Angle(-15));
                var tag = Add(holder, Kit.Badge("적당한 포켓몬을 쓰러뜨렸다", KitBadgeKind.Plain, Kit.F2));
                tag.style.marginTop = P(8);
            }
            else if (moment == 1)
            {
                var flip = Add(stageBox, new VisualElement { pickingMode = PickingMode.Ignore });
                flip.style.width = w;
                flip.style.height = CardH * s;
                flip.style.overflow = Overflow.Hidden;
                flip.style.scale = new Scale(new Vector3(0.32f, 1f, 1f));
                Pic(flip, "Cards/Trainer/silver_back.png", 0, 0, CardW, 640, s);
                Sheen(flip, 30, 60);
                var a = Pic(stageBox, "Cards/Trainer/locked_blue.png", 90, 330, 190, 200, s);
                a.style.rotate = new Rotate(new Angle(-28));
                a.style.opacity = 0.75f;
                var b = Pic(stageBox, "Cards/Trainer/locked_blue.png", 260, 420, 190, 200, s);
                b.style.rotate = new Rotate(new Angle(34));
                b.style.opacity = 0.6f;
            }
            else
            {
                var card = TrainerFace(stageBox, 0, w, 2, false, true, true);
                Pic(card, "Cards/Trainer/locked_red.png", 408, 128, 380, 400, s);
                Sheen(card, 82, 10);
                Border(card, Steel, Mathf.Max(2, P(3)));
                var tags = Box(holder, true);
                tags.style.marginTop = P(8);
                Add(tags, Kit.Badge("실버 " + TrainerScreen.Stars(2), KitBadgeKind.Silver, Kit.F2));
                Badge(tags, "「" + BlueName(0) + "」 해금", KitBadgeKind.Ok);
            }
        }
    }
}
