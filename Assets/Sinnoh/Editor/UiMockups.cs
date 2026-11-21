using System;
using System.Collections.Generic;
using System.IO;
using Rekkon.UiKit;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    // 목업 전용(M5 「목업 둘」, 04 §7 5·6) — 제품 화면이 아니다. 고른 안을 화면으로 만들 때 이 파일은 지운다.
    //   사당 S31~S33 · 운명 S30·S41의 배치 안을 실제 보드 HUD 위에 그려 창 없이 찍는다(UiShots와 같은 길 — 키트 부품·L1 토큰).
    //   배치: Unity.exe -batchmode -projectPath . -executeMethod Sinnoh.Editor.UiMockups.Run -logFile <log> [-mockOut <폴더>] [-mockOnly <이름 일부>]
    //   출력: <폴더>/mockup-shrine/shrine-<안>-<장면>-<720|1080>.png · <폴더>/mockup-fate/fate-<안>-<장면>-<720|1080>.png (기본 폴더 Logs/Review/m5)
    //   자리·크기는 720 화면 기준 값을 코드에 적고 1080에서 ×1.5 한다(목업이라 화면 USS를 늘리지 않는다). 견본 글은 여기 상수 —
    //   카드 이름·효과는 cards.json의 키와 한국어판 문구(Research/rules-summary §12·§13), 사당 이름은 문자열 표.
    public static partial class UiMockups
    {
        const string OutRoot = "Logs/Review/m5";

        static readonly Color Paper = Hex(0xf8f8f8), PaperDim = Hex(0xe8ecf0), Line = Hex(0xc0c8d0), Edge = Hex(0x283038), Gold = Hex(0xf8d040),
            Steel = Hex(0xd8e0e8), Blue = Hex(0x5890d8), CardNight = Hex(0x262a3c), Scrim = new Color(16 / 255f, 24 / 255f, 32 / 255f, 0.62f);

        static float k = 1;            // 720 기준 값 → 이 등급의 px
        static UiShot shot;
        static GameFlow flow;
        static UiStage stage;

        sealed class Scene
        {
            public string Set, Name;
            public bool OthersTurn;
            public Action<VisualElement> Build;
        }

        static readonly Scene[] Scenes =
        {
            new Scene { Set = "shrine", Name = "A-offering", Build = l => ShrineA(l, Offer) },
            new Scene { Set = "shrine", Name = "A-event", Build = l => ShrineA(l, Event) },
            new Scene { Set = "shrine", Name = "A-rift", Build = l => ShrineA(l, Rift) },
            new Scene { Set = "shrine", Name = "B-offering", Build = l => ShrineB(l, Offer) },
            new Scene { Set = "shrine", Name = "B-event", Build = l => ShrineB(l, Event) },
            new Scene { Set = "shrine", Name = "B-rift", Build = l => ShrineB(l, Rift) },
            new Scene { Set = "shrine", Name = "C-offering", Build = l => ShrineC(l, Offer) },
            new Scene { Set = "shrine", Name = "C-event", Build = l => ShrineC(l, Event) },
            new Scene { Set = "shrine", Name = "C-rift", Build = l => ShrineC(l, Rift) },
            new Scene { Set = "fate", Name = "A-hand", Build = l => FateA(l, 0) },
            new Scene { Set = "fate", Name = "A-discard", Build = l => FateA(l, 1) },
            new Scene { Set = "fate", Name = "A-prompt", OthersTurn = true, Build = l => FateA(l, 2) },
            new Scene { Set = "fate", Name = "B-hand", Build = l => FateB(l, 0) },
            new Scene { Set = "fate", Name = "B-discard", Build = l => FateB(l, 1) },
            new Scene { Set = "fate", Name = "B-prompt", OthersTurn = true, Build = l => FateB(l, 2) },
            new Scene { Set = "fate", Name = "C-hand", Build = l => FateC(l, 0) },
            new Scene { Set = "fate", Name = "C-discard", Build = l => FateC(l, 1) },
            new Scene { Set = "fate", Name = "C-prompt", OthersTurn = true, Build = l => FateC(l, 2) },
        };

        public static void Run()
        {
            bool ok = false;
            try
            {
                string outRoot = Arg("-mockOut", OutRoot), only = Arg("-mockOnly", "");
                var logs = new List<string>();
                int images = 0;
                foreach (var scene in Scenes)
                {
                    if (only != "" && !(scene.Set + "-" + scene.Name).Contains(only)) continue;
                    string dir = Path.Combine(outRoot, "mockup-" + scene.Set);
                    Directory.CreateDirectory(dir);
                    for (int g = 0; g < 2; g++)   // 720 · 1080만
                    {
                        var grade = UiShots.Grades[g];
                        var px = UiShots.Shoot(s => Compose(s, scene), true, grade, logs, grade.Width, grade.Height);
                        if (px == null) throw new InvalidOperationException("Gen 없음(글꼴·창틀)");
                        UiShots.WritePng(Path.Combine(dir, scene.Set + "-" + scene.Name + "-" + grade.Height + ".png"), px, grade.Width, grade.Height);
                        images++;
                    }
                }
                foreach (string log in logs) Debug.LogWarning("[UiMockups] 배치 흔들림: " + log);
                Debug.Log("[UiMockups] 그림 " + images + "장 → " + outRoot);
                ok = images > 0;
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { UiShots.CloseBoard(); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        // 4인 판(A19의 공유가 보이게)을 보드까지 보내고, 그 HUD 위에 목업 층을 얹는다. 내 말은 풀밭 사당 칸에 세운다
        static void Compose(UiShot s, Scene scene)
        {
            shot = s;
            k = s.Grade.TokenHeight == 1080 ? 1.5f : 1f;
            flow = UiShots.Seats(s, 4, state =>
            {
                foreach (var f in s.Board.Ctx.Fixtures.Fixtures)
                    if (f.Key == "shrine.meadow_shrine") state.Seats[0].Cell = f.Cell;
                var mine = state.Seats[0];
                mine.Jp = 9;
                for (int c = 0; c < mine.Apricorns.Length; c++) mine.Apricorns[c] = Have[c];
            });
            // 4인 판은 내 좌석이 첫 턴이 아닐 수 있다 — 내 턴 장면이면 내 턴까지, 남의 턴 장면이면 남의 턴까지 넘긴다
            for (int guard = 0; guard < 8 && (flow.Client.View.ActiveSeat == flow.Client.Seat) == scene.OthersTurn; guard++)
            {
                UiShots.EndActive(flow);
                flow.Stage.Tweens.Advance(8000);
                flow.Layout(s.Width, s.Height);
            }
            stage = flow.Stage;
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            Inset(layer, 0, 0, 0, 0);
            stage.Root.Add(layer);
            hover = null;
            strip = false;
            scene.Build(layer);
            if (hover != null)
            {
                s.Panel.Tick(3);
                stage.Tips.Element.BringToFront();
                stage.Tips.Show(hover);
            }
        }

        static VisualElement hover;
        static bool strip;   // 안 C(아래 띠): 좌석 칩을 한 줄로

        // ── 견본(실제 데이터) ──

        // 내 규토리[ApricornColor: 빨강 파랑 노랑 분홍 초록 하양 검정] — 등급은 1단계 빨강·파랑·노랑 / 2단계 분홍·초록·하양 / 3단계 검정
        static readonly int[] Have = { 2, 1, 0, 1, 0, 0, 1 };
        static readonly ApricornColor[][] Tiers =
        {
            new[] { ApricornColor.Red, ApricornColor.Blue, ApricornColor.Yellow },
            new[] { ApricornColor.Pink, ApricornColor.Green, ApricornColor.White },
            new[] { ApricornColor.Black },
        };

        // 운명의 변화 4종(cards.json change_of_fate.* — 앞면 큰 글씨·마우스 오버는 04 §10-4)
        static readonly string[] GodKey = { "dialga", "palkia", "giratina", "arceus" };
        static readonly string[] GodName = { "디아루가", "펄기아", "기라티나", "아르세우스" };
        static readonly string[] FateLine = { "재굴림 1", "순간이동 3", "희생 2", "와일드카드" };
        static readonly string[] FateTip =
        {
            "1장: 주사위 1개 다시 굴리기\n3장: 원하는 만큼 재굴림",
            "1장: 최대 3칸 순간이동(높이 무시)\n3장: 보드 어디로든 순간이동",
            "1장: 공격 전에 내 포켓몬이 데미지 2, 다음 기술 위력 +1\n3장: 남은 체력을 모두 잃고 그 절반만큼 다음 기술 위력 +",
            "다른 카드 3장을 쓸 때 그중 1장 대신 냅니다.",
        };
        static readonly int[] Hand = { 0, 0, 1, 2, 3 };       // 디아루가 2 · 펄기아 · 기라티나 · 아르세우스
        const int Picked = 2;                                  // 고른 카드 = 펄기아(배틀 밖 내 턴에 쓸 수 있는 것)
        static bool OnBoard(int god) => god == 1;              // 배틀 밖 효과는 펄기아뿐(디아루가·기라티나는 배틀 중)

        // ── 작은 도구 ──

        static Color Hex(int rgb) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1);
        static float P(float u) => Mathf.Round(u * k);

        static T Add<T>(VisualElement parent, T child) where T : VisualElement
        {
            parent.Add(child);
            return child;
        }

        static VisualElement Box(VisualElement parent, bool row = false)
        {
            var e = Add(parent, new VisualElement());
            e.style.flexShrink = 0;
            if (row) { e.style.flexDirection = FlexDirection.Row; e.style.alignItems = Align.Center; }
            return e;
        }

        static void Inset(VisualElement e, float? left, float? top, float? right, float? bottom)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = P(left.Value);
            if (top.HasValue) e.style.top = P(top.Value);
            if (right.HasValue) e.style.right = P(right.Value);
            if (bottom.HasValue) e.style.bottom = P(bottom.Value);
        }

        static void Border(VisualElement e, Color c, float px)
        {
            e.style.borderLeftColor = e.style.borderRightColor = e.style.borderTopColor = e.style.borderBottomColor = c;
            e.style.borderLeftWidth = e.style.borderRightWidth = e.style.borderTopWidth = e.style.borderBottomWidth = px;
        }

        static void Pad(VisualElement e, float u) => e.style.paddingLeft = e.style.paddingRight = e.style.paddingTop = e.style.paddingBottom = P(u);
        static void Margin(VisualElement e, float l, float t, float r, float b)
        {
            e.style.marginLeft = P(l); e.style.marginTop = P(t); e.style.marginRight = P(r); e.style.marginBottom = P(b);
        }

        static Label Text(VisualElement parent, string text, string cls = null, string size = Kit.F2)
        {
            var label = Add(parent, Kit.Text(text, size));
            label.style.flexShrink = 0;
            if (cls != null) label.AddToClassList(cls);
            return label;
        }

        static Label Wrapped(VisualElement parent, string text, string cls = null)
        {
            var label = Add(parent, Kit.Wrapped(text, Kit.F2));
            if (cls != null) label.AddToClassList(cls);
            return label;
        }

        static Label Badge(VisualElement parent, string text, KitBadgeKind kind = KitBadgeKind.Plain)
        {
            var badge = Add(parent, Kit.Badge(text, kind, Kit.F2));
            Margin(badge, 4, 0, 0, 0);
            return badge;
        }

        static Button Btn(VisualElement parent, string text, KitButtonKind kind = KitButtonKind.Normal, bool on = true)
        {
            var b = Add(parent, Kit.Button(text, kind));
            b.SetEnabled(on);
            Margin(b, 4, 0, 0, 0);
            return b;
        }

        static VisualElement ScrimOver(VisualElement layer)
        {
            var scrim = Add(layer, new VisualElement());
            scrim.AddToClassList("sn-party-scrim");   // 위 띠·아래 판을 덮지 않는 어둠(정비·마을 창과 같다)
            return scrim;
        }

        // 머리 줄: 제목(+ 배지) … 오른쪽 끝 요소
        static VisualElement Head(VisualElement parent, string title, out VisualElement right)
        {
            var head = Box(parent, true);
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.marginBottom = P(4);
            var left = Box(head, true);
            Text(left, title, "kit-title", Kit.F3);
            right = Box(head, true);
            return left;
        }

        // 묶음 칸: 옅은 판 + 작은 제목 줄
        static VisualElement Sec(VisualElement parent, string title, out VisualElement head)
        {
            var box = Box(parent);
            box.style.backgroundColor = PaperDim;
            Border(box, Line, 1);
            Pad(box, 6);
            Margin(box, 3, 3, 3, 3);
            head = Box(box, true);
            head.style.marginBottom = P(4);
            Text(head, title, "kit-title");
            return box;
        }

        // 누를 칸(칩): 고름 = 금색, 못 누름 = 흐림
        static VisualElement Chip(VisualElement parent, bool picked = false, bool off = false)
        {
            var chip = Box(parent, true);
            chip.style.backgroundColor = picked ? Gold : Paper;
            Border(chip, picked ? Edge : Line, 1);
            chip.style.paddingLeft = chip.style.paddingRight = P(4);
            chip.style.paddingTop = chip.style.paddingBottom = P(2);
            Margin(chip, 0, 2, 4, 2);
            if (off) chip.style.opacity = 0.4f;
            return chip;
        }

        static void ApricornIcon(VisualElement parent, ApricornColor color)
        {
            var icon = Add(parent, new VisualElement());
            icon.AddToClassList("sn-icon");
            var tex = GenAssets.Load<Texture2D>(EncounterScreen.ApricornKey(color));
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
        }

        static VisualElement ApricornChip(VisualElement parent, ApricornColor color, string text, bool picked, bool off)
        {
            var chip = Chip(parent, picked, off);
            ApricornIcon(chip, color);
            Text(chip, text).style.marginLeft = P(2);
            return chip;
        }

        static VisualElement SeatChip(VisualElement parent, int seat, bool picked, string badge = null, KitBadgeKind kind = KitBadgeKind.Plain)
        {
            var chip = Chip(parent, picked);
            var face = Add(chip, FaceBox.Make());
            FaceBox.Paint(face, seat, flow.SeatColor(seat), flow.Protagonist(seat));
            Text(chip, flow.SeatName(seat)).style.marginLeft = P(4);
            if (badge != null) Badge(chip, badge, kind);
            return chip;
        }

        // 사당 카드(그림 없음 — Gen에 사당 카드 그림이 아직 없다): 종류 배지 · 이름 · 단계 줄 셋(이번 단계만 금색) · 아랫줄
        static VisualElement ShrineCard(VisualElement parent, float width, string kind, KitBadgeKind kindColor, string name, string[] tiers, int lit, string foot)
        {
            var card = Add(parent, Kit.Panel(stage.Skin));
            card.style.flexShrink = 0;
            if (width > 0) card.style.width = P(width);
            Margin(card, 3, 3, 3, 3);
            var top = Box(card, true);
            Text(top, name, "kit-title", Kit.F3);
            Badge(top, kind, kindColor);
            if (tiers != null)
                for (int t = 0; t < tiers.Length; t++)
                {
                    var line = Box(card, true);
                    line.style.marginTop = P(3);
                    var step = Add(line, Kit.Badge((t + 1) + "단계", t == lit ? KitBadgeKind.Gold : KitBadgeKind.Plain, Kit.F2));
                    step.style.marginRight = P(5);
                    var text = Text(line, tiers[t], t == lit ? null : "kit-muted");
                }
            if (foot != null) Wrapped(card, foot, "kit-muted").style.marginTop = P(4);
            return card;
        }

        static VisualElement Timer(VisualElement parent, string left)
        {
            var row = Box(parent, true);
            var track = Box(row);
            track.style.width = P(70); track.style.height = P(8);
            track.style.backgroundColor = Steel;
            Border(track, Edge, 1);
            var fill = Box(track);
            fill.style.width = Length.Percent(80); fill.style.flexGrow = 1;
            fill.style.backgroundColor = Blue;
            Text(row, left).style.marginLeft = P(4);
            return row;
        }

        // ── 사당 장면의 내용(안 셋이 같이 쓴다): 묶음 칸들을 parent에 차례로 넣는다. wide = 가로로 늘어놓는 배치 ──

        const string ShrineName = "shrine.meadow_shrine";

        // S31 공물: 규토리 1개(등급 = 효과 단계) + 여행 점수 7(3장 뽑아 2장) → [바치기]
        static string Offer(VisualElement body, bool wide, VisualElement actions)
        {
            var give = Sec(body, "바칠 규토리", out var giveHead);
            Badge(giveHead, "1개");
            for (int t = 0; t < Tiers.Length; t++)
            {
                var line = Box(give, true);
                Add(line, Kit.Badge((t + 1) + "단계", KitBadgeKind.Plain, Kit.F2)).style.marginRight = P(5);
                foreach (var color in Tiers[t])
                    ApricornChip(line, color, Have[(int)color].ToString(), color == ApricornColor.Pink, Have[(int)color] == 0);
            }
            var jp = Sec(body, "여행 점수", out var jpHead);
            Badge(jpHead, "보유 9");
            var use = Chip(jp, false);
            use.style.alignSelf = Align.FlexStart;
            var check = Box(use);
            check.style.width = check.style.height = P(10);
            check.style.backgroundColor = Paper;
            Border(check, Edge, 1);
            Text(use, "7점 더 바치기").style.marginLeft = P(4);
            Text(jp, "3장 뽑아 2장 선택", "kit-muted");
            Btn(actions, "바치기", KitButtonKind.Main);
            return null;   // 닫을 수 있다
        }

        // S32 카드 결과 + 4인 이상 공유(A19): 뜻밖의 선물 2단계 = 원하는 규토리 2개 → 내 것 고르기 + 함께 받을 트레이너 1명
        static string Event(VisualElement body, bool wide, VisualElement actions)
        {
            ShrineCard(body, wide ? 250 : 0, "사당의 축복", KitBadgeKind.Ok, "뜻밖의 선물", new[] { "원하는 규토리 1개", "원하는 규토리 2개", "원하는 규토리 3개" }, 1, null);
            var pick = Sec(body, "받을 규토리", out var pickHead);
            Badge(pickHead, "1/2", KitBadgeKind.Warn);
            var colors = Box(pick, true);
            colors.style.flexWrap = Wrap.Wrap;
            if (wide) colors.style.maxWidth = P(250);
            for (int c = 0; c < 7; c++)
            {
                var chip = Chip(colors, c == (int)ApricornColor.Black);
                ApricornIcon(chip, (ApricornColor)c);
                if (c == (int)ApricornColor.Black) Text(chip, "1").style.marginLeft = P(2);
            }
            var share = Sec(body, "함께 받을 트레이너", out var shareHead);
            Badge(shareHead, "1명");
            var seats = Box(share, wide);
            if (wide && !strip) { seats.style.flexWrap = Wrap.Wrap; seats.style.maxWidth = P(340); }
            for (int seat = 1; seat < 4; seat++) SeatChip(seats, seat, seat == 2).style.alignSelf = Align.FlexStart;
            Btn(actions, "확인", KitButtonKind.Main, false);
            return "사당의 축복";
        }

        // S33 천상의 균열: 모두 운명의 변화 1장 → 추종자 비밀 선택(각자 동시에) → 공개
        static string Rift(VisualElement body, bool wide, VisualElement actions)
        {
            ShrineCard(body, wide ? 250 : 0, "모두에게", KitBadgeKind.Warn, "천상의 균열", null, -1, "모두 운명의 변화 1장을 받고 추종자를 고릅니다.");
            var pick = Sec(body, "내 추종자", out var pickHead);
            Badge(pickHead, "비공개", KitBadgeKind.Silver);
            var gods = Box(pick, true);
            for (int g = 0; g < 3; g++)
            {
                var chip = Chip(gods, g == 1);
                chip.style.flexDirection = FlexDirection.Column;
                var icon = Box(chip);
                icon.style.width = icon.style.height = P(wide ? 48 : 40);
                var tex = GenAssets.Load<Texture2D>("Cards/Tokens/celestial_icons/" + GodKey[g] + ".png");
                if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
                Text(chip, GodName[g]);
            }
            var wait = Sec(body, "선택 현황", out var waitHead);
            Badge(waitHead, "2/4");
            var seats = Box(wait, wide);
            if (wide && !strip) { seats.style.flexWrap = Wrap.Wrap; seats.style.maxWidth = P(430); }
            for (int seat = 0; seat < 4; seat++)
            {
                bool done = seat == 1 || seat == 2;
                SeatChip(seats, seat, false, done ? "완료" : "선택 중", done ? KitBadgeKind.Ok : KitBadgeKind.Plain).style.alignSelf = Align.FlexStart;
            }
            Btn(actions, "확정", KitButtonKind.Main);
            return "천상의 균열";
        }

        delegate string ShrineBody(VisualElement body, bool wide, VisualElement actions);

        static void ShrineHead(VisualElement panel, ShrineBody content, VisualElement body, bool wide)
        {
            var left = Head(panel, stage.T(ShrineName), out var right);
            panel.Add(body);
            string must = content(body, wide, right);
            if (must == null)
            {
                var once = Badge(left, "턴당 1회");
                Btn(right, "닫기");
            }
        }

        // 안 A — 가운데 창: 마을 창처럼 필드를 어둡게 덮고 가운데에 창 하나. 묶음 칸은 가로로
        static void ShrineA(VisualElement layer, ShrineBody content)
        {
            ScrimOver(layer);
            var holder = Add(layer, new VisualElement());
            Inset(holder, 0, 150, 0, null);
            holder.style.alignItems = Align.Center;
            var panel = Add(holder, Kit.Panel(stage.Skin));
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.alignItems = Align.FlexStart;
            ShrineHead(panel, content, body, true);
        }

        // 안 B — 오른쪽 기둥: 필드를 덮지 않고 오른쪽에 세로 판. 보드의 사당·말이 그대로 보인다
        static void ShrineB(VisualElement layer, ShrineBody content)
        {
            var panel = Add(layer, Kit.Panel(stage.Skin));
            Inset(panel, null, 68, 4, null);
            panel.style.width = P(300);
            var body = new VisualElement();
            ShrineHead(panel, content, body, false);
        }

        // 안 C — 아래 띠: 아래 판 바로 위에 가로 띠. 조우 카드처럼 아래에서 올라온 자리, 보드 위쪽은 그대로
        static void ShrineC(VisualElement layer, ShrineBody content)
        {
            var panel = Add(layer, Kit.Panel(stage.Skin));
            Inset(panel, 4, null, 4, 130);
            strip = true;
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.alignItems = Align.FlexStart;
            body.style.justifyContent = Justify.Center;
            ShrineHead(panel, content, body, true);
        }

        // ── 운명 ──

        // 카드 앞면: 원작 그림의 그림 부분 + 이름 + 큰 글씨 한 줄(04 §10-4). 세로 카드
        static VisualElement FateCard(VisualElement parent, int god, float width, float artHeight, bool picked, bool off, string tag = null, KitBadgeKind tagKind = KitBadgeKind.Plain)
        {
            var card = Box(parent);
            card.style.width = P(width);
            card.style.backgroundColor = picked ? Gold : Paper;
            Border(card, picked ? Edge : Line, picked ? 2 : 1);
            card.style.alignItems = Align.Center;
            card.style.paddingBottom = P(3);
            Margin(card, 2, picked ? 0 : 8, 2, picked ? 8 : 0);
            var art = FateArt(card, god, artHeight);
            art.style.alignSelf = Align.Stretch;
            Text(card, GodName[god]).style.marginTop = P(2);
            Text(card, FateLine[god], "kit-title");
            if (tag != null) Margin(Badge(card, tag, tagKind), 0, 2, 0, 0);
            if (off) card.style.opacity = 0.65f;
            return card;
        }

        static Image FateArt(VisualElement parent, int god, float height)
        {
            var tex = GenAssets.Load<Texture2D>("Cards/Fate/" + GodKey[god] + ".png");
            // 원작 카드 560×980에서 실루엣 자리(x 60~500 · y 140~540)만
            var art = Add(parent, new Image { image = tex, scaleMode = ScaleMode.ScaleToFit, uv = new Rect(60 / 560f, 1 - 540 / 980f, 440 / 560f, 400 / 980f) });
            art.style.height = P(height);
            art.style.flexShrink = 0;
            art.style.backgroundColor = CardNight;
            return art;
        }

        static string HandCount(int n) => "운명의 변화 " + n + "/5";

        // 손패 줄(가로): 장면 0 = 고르기, 1 = 6장째 버리기
        static VisualElement HandRow(VisualElement parent, int scene, float width, float art)
        {
            var row = Box(parent, true);
            row.style.alignItems = Align.FlexStart;
            for (int i = 0; i < Hand.Length; i++)
            {
                if (scene == 1) FateCard(row, Hand[i], width, art, i == 0, false);
                else
                {
                    var card = FateCard(row, Hand[i], width, art, i == Picked, !OnBoard(Hand[i]), OnBoard(Hand[i]) ? null : Hand[i] == 3 ? "3장용" : "배틀 중");
                    if (i == Picked && scene == 0 && width < 120) Hover(card, Hand[i]);
                }
            }
            if (scene == 1) FateCard(row, 2, width, art, false, false, "새 카드", KitBadgeKind.Ok);
            return row;
        }

        static void Hover(VisualElement card, int god, TipSide side = TipSide.Above)
        {
            stage.Tips.Hook(card, GodName[god], FateTip[god], side, reading: true);
            hover = card;
        }

        const string DiscardGuide = "버릴 카드 1장을 선택하십시오.";

        // 남의 턴에 온 물음(S41) 견본: 플레이어 3이 사당 축복 「뜻밖의 선물」 2단계를 나에게 나눴다(A19) → 원하는 규토리 2개
        static void PromptWho(VisualElement parent)
        {
            var face = Add(parent, FaceBox.Make());
            FaceBox.Paint(face, 2, flow.SeatColor(2), flow.Protagonist(2));
            var words = Box(parent);
            words.style.marginLeft = P(6);
            Text(words, flow.SeatName(2) + "의 사당 축복");
            Text(words, "뜻밖의 선물: 규토리 2개", "kit-title");
        }

        static void PromptPick(VisualElement parent)
        {
            for (int c = 0; c < 7; c++)
            {
                var chip = Chip(parent, c == (int)ApricornColor.Green);
                ApricornIcon(chip, (ApricornColor)c);
                if (c == (int)ApricornColor.Green) Text(chip, "1").style.marginLeft = P(2);
            }
        }

        // 안 A — 아래 손패 줄: 손패가 아래 판 위 왼쪽에 늘 펼쳐져 있다. 물음(S41)은 오른쪽 아래 구석 알림 창
        static void FateA(VisualElement layer, int scene)
        {
            if (scene == 1) ScrimOver(layer);
            var panel = Add(layer, Kit.Panel(stage.Skin));
            Inset(panel, 4, null, null, 130);
            var left = Head(panel, HandCount(scene == 1 ? 6 : 5), out var right);
            if (scene == 1)
            {
                Text(right, DiscardGuide);
                Btn(right, "버리기", KitButtonKind.End);
            }
            else if (scene == 0)
            {
                Badge(left, "턴당 1회");
            }
            if (scene == 2) { HandRowIdle(panel, 100, 56); PromptCorner(layer); }
            else
            {
                var row = HandRow(panel, scene, 100, 56);
                if (scene == 0) Btn(row, "사용", KitButtonKind.Main).style.alignSelf = Align.Center;   // 툴팁(카드 위)에 가리지 않게 카드 줄 끝에
            }
        }

        // 남의 턴: 손패는 보이지만 쓸 수 없다(고른 카드 없음)
        static void HandRowIdle(VisualElement parent, float width, float art)
        {
            var row = Box(parent, true);
            for (int i = 0; i < Hand.Length; i++) FateCard(row, Hand[i], width, art, false, false);
        }

        static void PromptCorner(VisualElement layer)
        {
            var box = Add(layer, Kit.Panel(stage.Skin));
            Inset(box, null, null, 4, 130);
            var who = Box(box, true);
            PromptWho(who);
            var pick = Box(box, true);
            pick.style.marginTop = P(6);
            PromptPick(pick);
            var foot = Box(box, true);
            foot.style.justifyContent = Justify.SpaceBetween;
            foot.style.marginTop = P(6);
            Timer(foot, "24초");
            Btn(foot, "확인", KitButtonKind.Main, false);
        }

        // 안 B — 가운데 창: 평소에는 닫혀 있고 [운명]으로 연다. 큰 카드 + 고른 카드의 효과 한 줄. 물음(S41)은 위 띠 아래 가운데 띠
        static void FateB(VisualElement layer, int scene)
        {
            if (scene == 2)
            {
                var holderTop = Add(layer, new VisualElement());
                Inset(holderTop, 0, 68, 4, null);
                holderTop.style.alignItems = Align.FlexEnd;   // 왼쪽의 기록 판을 덮지 않게 오른쪽으로
                var band = Add(holderTop, Kit.Panel(stage.Skin));
                band.style.flexDirection = FlexDirection.Row;
                band.style.alignItems = Align.Center;
                PromptWho(band);
                var pick = Box(band, true);
                pick.style.marginLeft = P(14);
                PromptPick(pick);
                var end = Box(band, true);
                end.style.marginLeft = P(10);
                Timer(end, "24초");
                Btn(end, "확인", KitButtonKind.Main, false);
                return;
            }
            ScrimOver(layer);
            var holder = Add(layer, new VisualElement());
            Inset(holder, 0, 150, 0, null);
            holder.style.alignItems = Align.Center;
            var panel = Add(holder, Kit.Panel(stage.Skin));
            var left = Head(panel, HandCount(scene == 1 ? 6 : 5), out var right);
            if (scene == 0) { Badge(left, "턴당 1회"); Btn(right, "닫기"); }
            HandRow(panel, scene, 150, 110);
            var foot = Box(panel, true);
            foot.style.justifyContent = Justify.SpaceBetween;
            foot.style.marginTop = P(4);
            if (scene == 0)
            {
                Text(foot, "펄기아: 최대 3칸 순간이동(높이 무시)");
                Btn(foot, "사용", KitButtonKind.Main);
            }
            else
            {
                Text(foot, DiscardGuide);
                Btn(foot, "버리기", KitButtonKind.End);
            }
        }

        // 안 C — 오른쪽 세로 줄: 손패가 오른쪽 끝에 작은 줄 목록으로 늘 있다. 물음(S41)은 아래 판의 「행동」 칸 자리에 뜬다
        static void FateC(VisualElement layer, int scene)
        {
            if (scene == 1) ScrimOver(layer);
            var panel = Add(layer, Kit.Panel(stage.Skin));
            Inset(panel, null, null, 4, 130);
            panel.style.width = P(250);
            var left = Head(panel, HandCount(scene == 1 ? 6 : 5), out var right);
            if (scene == 1) Wrapped(panel, DiscardGuide);
            int n = scene == 1 ? 6 : 5;
            for (int i = 0; i < n; i++)
            {
                int god = i < 5 ? Hand[i] : 2;
                bool picked = scene == 0 ? i == Picked : scene == 1 && i == 0;
                bool off = scene == 0 && !OnBoard(god);
                var line = Chip(panel, picked);
                line.style.marginRight = 0;
                if (off) line.style.opacity = 0.5f;
                var art = FateArt(line, god, 30);
                art.style.width = P(30);
                var words = Box(line);
                words.style.marginLeft = P(5);
                words.style.flexGrow = 1;
                Text(words, GodName[god]);
                Text(words, FateLine[god], "kit-title");
                if (i == 5) Badge(line, "새 카드", KitBadgeKind.Ok);
                else if (off) Badge(line, god == 3 ? "3장용" : "배틀 중");
                if (picked && scene == 0) Hover(line, god, TipSide.Left);
            }
            if (scene != 2)
            {
                var foot = Box(panel, true);
                foot.style.justifyContent = Justify.FlexEnd;
                foot.style.marginTop = P(4);
                if (scene == 0) Btn(foot, "사용", KitButtonKind.Main);
                else Btn(foot, "버리기", KitButtonKind.End);
            }
            else
            {
                // 아래 판의 오른쪽 칸(「행동」) 자리를 덮는 물음 판
                var ask = Add(layer, Kit.Panel(stage.Skin));
                Inset(ask, 846, null, 6, 6);
                ask.style.height = P(112);
                var who = Box(ask, true);
                who.style.justifyContent = Justify.SpaceBetween;
                var name = Box(who, true);
                PromptWho(name);
                Timer(who, "24초");
                var pick = Box(ask, true);
                pick.style.marginTop = P(6);
                pick.style.justifyContent = Justify.SpaceBetween;
                var chips = Box(pick, true);
                PromptPick(chips);
                Btn(pick, "확인", KitButtonKind.Main, false);
            }
        }
    }
}
