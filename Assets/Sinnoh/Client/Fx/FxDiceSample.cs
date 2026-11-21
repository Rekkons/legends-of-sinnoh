using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 주사위 굴림 견본(plan M4-3 「주사위 굴림」, 04 §8-8 43·56·57행): 이벤트 대본(공격 한 번) → <see cref="FxPlanner"/> → 대기열 → <see cref="DiceRollScene"/>(녹화본 재생) → <see cref="DiceBoard"/>(도트풍).
    /// 게임 화면이 아니다 — 굴림판이 들어갈 배틀 독(안내 띠·결과 줄·합계·꼬리표·고리)은 화면 단계에서 만든다. 여기에는 굴림판 그림(실제 크기와 3배)과 확인용 글만 있다.
    /// 배치 캡처 <c>Sinnoh.Editor.FxShots</c>가 <see cref="Seek"/>로 정한 시각에 보내 찍는다.
    /// </summary>
    public sealed class FxDiceSample : IDisposable
    {
        const AttackDieFace S = AttackDieFace.StatusSymbol, R = AttackDieFace.RegularHit, C = AttackDieFace.CriticalHit;
        const int Zoom = 3;
        /// <summary>견본 주사위 색: 전기 타입(시제품과 같은 노랑).</summary>
        public static readonly Color32 Body = new Color32(0xF8, 0xD0, 0x30, 0xFF);

        /// <summary>견본 하나: 엔진 결과, 면 바꾸기(엔진 번호, 없으면 −1)와 새 면, 재굴림(엔진 번호, 없으면 −1)과 새 면, 녹화본을 고르는 씨앗, 찍는 시각.</summary>
        public sealed class Variant
        {
            public string Name;
            public AttackDieFace[] Faces;
            public int Flip = -1, Reroll = -1;
            public AttackDieFace FlipTo = S, RerollTo = C;
            public ulong Seed;
            public int[] ShotsMs;
        }

        // 시각(항목 시각 ms, 기본 조정 값): 던짐 400 · 다 멈춤(위쪽 끝) 2000 · 면 바꾸기 넘김 2450~2730 · 세기 2250(면 바꾸기가 있으면 2980)부터 주사위당 130 · 재굴림 = 세기 끝 + 600부터 600
        public static readonly Variant[] Variants =
        {
            new Variant { Name = "dice2", Faces = new[] { R, C }, Seed = 2, ShotsMs = new[] { 460, 580, 700, 880, 1120, 2100 } },
            new Variant { Name = "dice4", Faces = new[] { R, S, C, R }, Seed = 4, ShotsMs = new[] { 460, 580, 700, 880, 1120, 2100 } },
            new Variant { Name = "dice6", Faces = new[] { C, R, R, S, R, R }, Seed = 6, ShotsMs = new[] { 460, 580, 700, 880, 1120, 2100 } },
            new Variant { Name = "dice-flip", Faces = new[] { R, R, C }, Flip = 0, Seed = 3, ShotsMs = new[] { 2100, 2520, 2590, 2660, 2800 } },
            new Variant { Name = "dice-glitch", Faces = new[] { R, R, S, R }, Reroll = 2, Seed = 14, ShotsMs = new[] { 3300, 3410, 3570, 3720, 3870, 4100 } },
            new Variant { Name = "dice-all", Faces = new[] { R, R, S, R }, Flip = 0, Reroll = 2, Seed = 41, ShotsMs = new[] { 700, 2100, 2590, 4140, 4400, 4590, 4800 } },
        };

        public static Variant Find(string name)
        {
            foreach (var v in Variants) if (v.Name == name) return v;
            throw new ArgumentException("모르는 견본: " + name, nameof(name));
        }

        public readonly TweenRunner Runner = new TweenRunner();
        public readonly FxSequencer Sequencer = new FxSequencer();
        public readonly DiceRollScene Scene;
        public readonly DiceBoard Board;
        /// <summary>재굴림이 시작하는 항목 시각(없으면 −1).</summary>
        public readonly int RerollAtMs = -1;
        readonly FxPlayer player;
        readonly FxEntry entry;
        readonly Variant variant;
        readonly DicePick pick;
        readonly VisualElement small, big;
        readonly Label result, shown, clip, clock;
        readonly DiePose[] poses = new DiePose[DiceGeo.MaxDice], scratch = new DiePose[DiceGeo.MaxDice];
        int nowMs;

        public FxDiceSample(VisualElement root, KitSkin skin, UiGrade grade, DiceLibrary library, Variant variant)
        {
            this.variant = variant;
            Kit.Attach(root, skin, grade);
            var page = new VisualElement { name = "fx-dice-sample" };
            page.style.position = Position.Absolute;
            page.style.left = page.style.top = page.style.right = page.style.bottom = 0;
            page.style.backgroundColor = new StyleColor(new Color32(0x18, 0x20, 0x2C, 0xFF));
            root.Insert(0, page);

            Board = new DiceBoard(grade.TokenHeight, Body);
            int w = Board.GridW * Board.Dot, h = Board.GridH * Board.Dot;
            small = Slot(page, "dice-board", 24, 24, w, h);
            big = Slot(page, "dice-board-zoom", 24, 24 + h + 24, w * Zoom, h * Zoom);

            var panel = Kit.Panel(skin);
            panel.style.position = Position.Absolute;
            panel.style.left = 24 + w + 24;
            panel.style.top = 24;
            panel.style.width = 760;
            page.Add(panel);
            var title = Kit.Text("주사위 굴림 견본 · " + variant.Name + " (게임 화면 아님)", Kit.F2);
            title.AddToClassList("kit-title");
            panel.Add(title);
            result = Line(panel);
            shown = Line(panel);
            clip = Line(panel);
            clock = Line(panel);

            // 대본: 공격 한 번(굴림 → 면 바꾸기). 녹화본은 씨앗으로 고른다(섞기 주머니 + 앞뒤 뒤집기)
            int n = variant.Faces.Length;
            var events = new List<GameEvent>
            {
                new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.BattleAction, Auto = AutoKind.Human },
                new DiceRolled { Seq = 1, Seat = 0, PokemonId = 2, Faces = variant.Faces },
            };
            if (variant.Flip >= 0)
                events.Add(new DiceFacesChanged
                {
                    Seq = 1, Seat = 0, Source = new SourceRef(SourceKind.Status, 0),
                    Changes = new[] { new FaceChange { Index = variant.Flip, Before = variant.Faces[variant.Flip], After = variant.FlipTo } },
                });
            entry = new FxPlanner(0).Plan(events)[0];
            pick = new DiceBag(new Pcg32(variant.Seed), library).Next(n);
            Scene = DiceRollScene.Plan(entry, library.Clip(pick), pick.Mirror, (int)variant.Seed);
            if (variant.Reroll >= 0)
            {
                // 재굴림 이벤트는 M5다(43행). 여기서는 세기가 끝나고 600 뒤에 건다
                RerollAtMs = Scene.CountMs + PlayTiming.DiceCountPerMs.Value * n + 600;
                Scene.AddReroll(RerollAtMs, variant.Reroll, variant.RerollTo);
            }

            player = new FxPlayer(Runner, Sequencer, Show);
            player.Enqueue(new[] { entry });
            Show(Sequencer);
        }

        static VisualElement Slot(VisualElement parent, string name, int left, int top, int width, int height)
        {
            var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = left;
            e.style.top = top;
            e.style.width = width;
            e.style.height = height;
            parent.Add(e);
            return e;
        }

        static Label Line(VisualElement parent)
        {
            var label = Kit.Text("", Kit.F1);
            label.AddToClassList("kit-mt2");
            parent.Add(label);
            return label;
        }

        /// <summary>대본 시작부터 <paramref name="ms"/>가 지난 모습으로 보낸다(앞으로만).</summary>
        public void Seek(int ms)
        {
            if (ms < nowMs) throw new ArgumentOutOfRangeException(nameof(ms), ms, "앞으로만 간다 — 되돌리려면 새로 만든다");
            Runner.Advance(ms - nowMs);
            nowMs = ms;
            Show(Sequencer);
        }

        /// <summary>그 시각에 왼쪽부터 보이는 면(엔진 순서).</summary>
        public AttackDieFace ShownFace(int index) => Scene.ShownFace(index, Local, scratch);

        int Local => Sequencer.Current != null ? Sequencer.LocalMs : Sequencer.Last != null ? entry.LengthMs : 0;

        static string Name(AttackDieFace f) => f == S ? "심볼" : f == R ? "일반" : f == C ? "급소" : "–";

        void Show(FxSequencer seq)
        {
            int local = Local, n = Scene.Dice;
            Scene.Pose(local, poses, out var glitch);
            Board.Render(poses, n, glitch);
            // 같은 RT를 실제 크기와 3배로 보인다(Point). 그림이 바뀌었으니 다시 그리게 한다
            small.style.backgroundImage = Background.FromRenderTexture(Board.Output);
            big.style.backgroundImage = Background.FromRenderTexture(Board.Output);
            small.MarkDirtyRepaint();
            big.MarkDirtyRepaint();

            var want = new List<string>();
            var see = new List<string>();
            for (int k = 0; k < n; k++)
            {
                var face = variant.Faces[k];
                if (k == variant.Flip) face = variant.FlipTo;
                if (k == variant.Reroll) face = variant.RerollTo;
                want.Add(Name(face));
                see.Add(Name(ShownFace(k)));
            }
            result.text = "엔진 결과(끝, 왼쪽부터): " + string.Join(" · ", want);
            shown.text = "지금 보이는 면(왼쪽부터): " + string.Join(" · ", see);
            var c = Scene.Clip;
            clip.text = $"녹화본: 주사위 {pick.Dice}개 · {pick.Clip}번{(pick.Mirror ? " · 앞뒤 뒤집음" : "")} · 길이 {c.LengthMs}ms";
            clock.text = $"항목 {local} / {entry.LengthMs}ms · 던짐 {Scene.ThrowMs} · 세기 {Scene.CountMs}" + (RerollAtMs >= 0 ? $" · 재굴림 {RerollAtMs}" : "");
        }

        public void Dispose() => Board.Dispose();
    }
}
