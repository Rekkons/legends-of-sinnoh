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
    /// 연출 층 뼈대 견본(plan M4-3 「연출 층 뼈대」, 04 §8-7): 이벤트 대본(공격 두 번) → <see cref="FxPlanner"/> → 대기열 → 부품 <c>Seek(ms)</c>가 이어지는지 보는 확인용 장면.
    /// 게임 화면이 아니다 — 굴림판·계산 줄·DS 체력 상자는 화면 단계에서 만든다. 여기에는 체력 숫자(표시 값)·체력 막대(트윈)·날아가는 피해 숫자·대기열 상태 글만 있다.
    /// 배치 캡처 <c>Sinnoh.Editor.FxShots</c>가 <see cref="Seek"/>로 정한 시각에 보내 찍는다.
    /// </summary>
    public sealed class FxSample
    {
        public const int PokemonId = 1, MaxHp = 20;
        /// <summary>보기의 값: 대본이 다 적용된 뒤의 체력. 갱신을 받는 순간 보기는 이미 이 값이다.</summary>
        public const int ViewHp = 9;
        /// <summary>계산 줄이 다 켜진 뒤 숫자가 날기 전에 쉬는 시간(화면 상수 — spikes/dice-roll §4 「+ 300 뒤 비행 400」).</summary>
        public const int FlyGapMs = 300;
        const int BarWidth = 400, FlyX = -160, FlyY = -44;

        /// <summary>찍는 시각(대본 시작부터 ms): 굴리는 중 · 피해 숫자가 나는 중 · 체력 막대가 깎이는 중 · 첫 착지 뒤(둘째 항목 시작) · 다 끝나 대기열이 빈 뒤.</summary>
        public static readonly int[] ShotsMs = { 1200, 3400, 3900, 4500, 9100 };

        public readonly TweenRunner Runner = new TweenRunner();
        public readonly FxSequencer Sequencer = new FxSequencer();
        readonly FxPlayer player;
        readonly Label hp, queue, step, sounds, input, fly;
        readonly VisualElement fill;
        readonly List<string> heard = new List<string>();
        int nowMs;

        /// <summary>체력 글(「체력 13 / 20」) — 표시 값으로 그린다.</summary>
        public string HpText => hp.text;
        /// <summary>체력 막대가 찬 비율(0~1).</summary>
        public float BarRatio { get; private set; }

        /// <summary>이벤트 대본: 좌석 0의 공격 두 번(체력 20 → 13 → 9). 실제 판 대신 이벤트만 짠다(04 §8-7).</summary>
        public static List<GameEvent> Script()
        {
            var faces = new[] { AttackDieFace.RegularHit, AttackDieFace.CriticalHit };
            return new List<GameEvent>
            {
                new ActionStarted { Seq = 1, Seat = 0, Kind = ActionKind.BattleAction, Auto = AutoKind.Human },
                new DiceRolled { Seq = 1, Seat = 0, PokemonId = 2, Faces = faces },
                new DamageBreakdown { Seq = 1, Seat = 0, TargetId = PokemonId, Final = 7 },
                new HpChanged { Seq = 1, Seat = 0, PokemonId = PokemonId, Before = 20, After = 13, Max = MaxHp },
                new ActionStarted { Seq = 2, Seat = 0, Kind = ActionKind.BattleAction, Auto = AutoKind.Human },
                new DiceRolled { Seq = 2, Seat = 0, PokemonId = 2, Faces = faces },
                new DamageBreakdown { Seq = 2, Seat = 0, TargetId = PokemonId, Final = 4 },
                new HpChanged { Seq = 2, Seat = 0, PokemonId = PokemonId, Before = 13, After = ViewHp, Max = MaxHp },
            };
        }

        /// <summary><paramref name="root"/>(패널을 덮는 요소)에 견본을 만들고 대본을 대기열에 넣는다. 시각 0의 모습이다.</summary>
        public FxSample(VisualElement root, KitSkin skin, UiGrade grade)
        {
            Kit.Attach(root, skin, grade);
            var page = new VisualElement { name = "fx-sample" };
            page.style.position = Position.Absolute;
            page.style.left = page.style.top = page.style.right = page.style.bottom = 0;
            page.style.backgroundColor = new StyleColor(new Color32(0x58, 0x78, 0xA0, 0xFF));
            root.Insert(0, page);

            var panel = Kit.Panel(skin);
            panel.style.position = Position.Absolute;
            panel.style.left = panel.style.top = 24;
            panel.style.width = 640;
            page.Add(panel);
            var title = Kit.Text("연출 층 뼈대 견본 (게임 화면 아님)", Kit.F2);
            title.AddToClassList("kit-title");
            panel.Add(title);

            hp = Kit.Text("", Kit.F3);
            hp.name = "fx-sample-hp";
            hp.AddToClassList("kit-mt6");
            panel.Add(hp);

            var track = new VisualElement { name = "fx-sample-bar" };
            track.AddToClassList("kit-mt4");
            track.style.width = BarWidth;
            track.style.height = 16;
            track.style.backgroundColor = new StyleColor(new Color32(0x20, 0x28, 0x38, 0xFF));
            fill = new VisualElement();
            fill.style.width = BarWidth;
            fill.style.height = 16;
            fill.style.backgroundColor = new StyleColor(new Color32(0x48, 0xC0, 0x60, 0xFF));
            FxUi.OriginLeft(fill);
            track.Add(fill);
            panel.Add(track);

            queue = Line(panel, "kit-mt6");
            step = Line(panel, "kit-mt2");
            sounds = Line(panel, "kit-mt2");
            input = Line(panel, "kit-mt2");

            // 피해 숫자: 계산 줄 자리(판 오른쪽)에서 체력 막대 끝으로 날아간다. 자리는 만들 때 한 번, 연출은 옮기기·흐리기만
            fly = Kit.Text("", Kit.F3);
            fly.name = "fx-sample-fly";
            fly.pickingMode = PickingMode.Ignore;
            fly.style.position = Position.Absolute;
            fly.style.left = 560;
            fly.style.top = 120;
            fly.style.color = new StyleColor(new Color32(0xE0, 0x40, 0x38, 0xFF));
            page.Add(fly);

            Sequencer.Sound += cue => heard.Add($"{cue.Sound}({Sequencer.Current.Seq}번 {cue.AtMs}ms)");
            player = new FxPlayer(Runner, Sequencer, Show);
            var entries = new FxPlanner(0).Plan(Script());
            // 소리는 연출 시간표의 키 프레임에 단다(04 §8-4): 피격음 = 체력 막대가 깎이기 시작할 때
            foreach (var e in entries)
            {
                var drain = e.Step(612);
                if (drain != null) e.AddSound(drain.StartMs, "피격");
            }
            player.Enqueue(entries);
            Show(Sequencer);
            Hud();
        }

        static Label Line(VisualElement parent, string margin)
        {
            var label = Kit.Text("", Kit.F1);
            label.AddToClassList(margin);
            label.AddToClassList("kit-muted");
            parent.Add(label);
            return label;
        }

        /// <summary>대본 시작부터 <paramref name="ms"/>가 지난 모습으로 보낸다(앞으로만). 실행기 하나가 대기열과 부품을 함께 돌린다.</summary>
        public void Seek(int ms)
        {
            if (ms < nowMs) throw new ArgumentOutOfRangeException(nameof(ms), ms, "앞으로만 간다 — 되돌리려면 새로 만든다");
            Runner.Advance(ms - nowMs);
            nowMs = ms;
            Hud();
        }

        void Hud() => input.text = (Sequencer.InputOpen ? "입력 열림" : "입력 닫힘") + " · 실행기 바쁨 " + Runner.Busy + " · " + nowMs + "ms";

        /// <summary>지금 항목·시각으로 부품을 그린다(실행기가 프레임마다 부른다).</summary>
        void Show(FxSequencer seq)
        {
            var entry = seq.Current;
            int local = seq.LocalMs;

            // 표시 값: 숫자는 착지 단서가 닿을 때 바뀐다. 막대는 깎이는 단계 동안 Before → After(빠르게 → 느리게)
            int shown = seq.Shown.Get(ShownKey.Hp(PokemonId), ViewHp);
            hp.text = $"체력 {shown} / {MaxHp}";
            float value = shown;
            var drain = entry?.Step(612);
            if (drain != null)
            {
                var h = (HpChanged)drain.Events[0];
                value = Tween.At(drain.Local(local), 0, drain.Ms, h.Before, h.After, Ease.OutQuad);
            }
            BarRatio = value / MaxHp;
            FxUi.Scale(fill, BarRatio, 1f);

            // 피해 숫자: 계산 줄(61행 앞부분)이 끝나고 FlyGapMs 뒤에 날기 시작해 그 단계 끝에 닿는다
            var calc = entry?.Step(611);
            int flyStart = PlayTiming.DamageCalcMs.Value + FlyGapMs;
            int t = calc == null ? -1 : local - calc.StartMs;
            if (calc == null || t < flyStart || t >= calc.Ms) FxUi.Fade(fly, 0f);
            else
            {
                fly.text = "−" + ((DamageBreakdown)calc.Events[0]).Final;
                float p = Tween.Apply(Ease.InQuad, Tween.Progress(t, flyStart, calc.Ms - flyStart));
                FxUi.Move(fly, FlyX * p, FlyY * p);
                FxUi.Fade(fly, 1f);
            }

            queue.text = entry != null ? $"항목 {entry.Seq}번 · {local} / {entry.LengthMs}ms · 다음 {seq.Pending}"
                : seq.IsEmpty ? "대기열 빔 (표시 값 = 보기)" : $"기다리는 항목 {seq.Pending}";
            step.text = "단계: " + StepName(entry, local);
            sounds.text = "소리 단서: " + (heard.Count == 0 ? "없음" : string.Join(", ", heard));
        }

        static string StepName(FxEntry entry, int local)
        {
            if (entry == null) return "없음";
            foreach (var s in entry.Steps)
            {
                if (local < s.StartMs || local >= s.EndMs) continue;
                switch (s.Row)
                {
                    case 560: return "주사위 굴림 (56행)";
                    case 611: return "계산 줄 · 숫자 비행 (61행)";
                    case 612: return "체력 감소 (61행)";
                    default: return s.Row.ToString();
                }
            }
            return "결과 보기";
        }
    }
}
