using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;
using static Sinnoh.Client.WorkUi;

namespace Sinnoh.Client
{
    /// <summary>
    /// S27 진화 창(04 §3-4 「내 차례에만」 · §8-8 39행, 룰북 p.5 · rulings A9 — EvolveRules). 보드 HUD의 [진화]로 연다. 한 창이 진화 하나를 끝까지 맡는다:
    /// <list type="number">
    /// <item>고르기: 파티 포켓몬마다 그림 · 이름 → 진화 뒤 이름(갈래가 여럿이면 「n갈래」) · 비용 「여행 점수 n」 · [진화]. 우러난 스튜의 반값이 남았으면 [반값으로](켜면 비용이 반 — 어느 진화에 쓸지 고른다, 결정 76 ⑦).
    /// 못 하는 포켓몬은 흐리게 + 까닭(진화 없음 · 여행 점수 부족).</item>
    /// <item>갈래(Reward/EvolveBranch, R14 — 이브이): 진화할 모습 카드 가운데 하나를 누른다.</item>
    /// <item>진화(39행): DS 층(256×192 정수배 — <see cref="DsStage"/>)에 원작처럼 「…오잉!?」 → 흰 실루엣이 두 모습 사이를 점점 빠르게 오감(결정 75 C2 가) → 흰 빛 → 「축하합니다!」.</item>
    /// <item>옛 기술(Reward/EvolveTeach, p.5 「regardless of the types it can learn」): 옛 시그니처 기술을 칸 1 · 칸 2에 가르치거나 [가르치지 않기]. 진화한 포켓몬이 지금 가진 기술(★ 고유 기술 + 칸의 기술 — 결정 181)을 함께 보인다.</item>
    /// </list>
    /// 규칙을 다시 세지 않는다: 단추는 열린 선택의 합법 답 그대로이고, 비용·까닭은 엔진 함수(<see cref="EvolveRules.Cost"/>)와 보기에서 읽는다.
    /// 진화 연출 · 갈래 · 옛 기술을 묻는 동안은 닫히지 않는다. 이어 받은 판에서 갈래·옛 기술을 묻고 있으면 <see cref="GameFlow"/>가 이 창을 연다.
    /// 요소 이름: <c>board-evolve</c>·<c>board-evolve-panel</c>·<c>board-evolve-close</c>·<c>board-evolve-jp</c>·<c>board-evolve-half</c>·<c>board-evolve-mon-&lt;i&gt;</c>·<c>board-evolve-go-&lt;i&gt;</c>·
    /// <c>board-evolve-branch-&lt;n&gt;</c>·<c>board-evolve-ds</c>·<c>board-evolve-flash</c>·<c>board-evolve-now</c>·<c>board-evolve-sig</c>·<c>board-evolve-teach-&lt;0|1&gt;</c>·<c>board-evolve-keep</c>.
    /// </summary>
    public sealed class EvolveScreen : IWorkScreen, IWatchScreen
    {
        public enum Mode { Pick, Branch, Show, Teach, Egg }

        readonly GameFlow flow;
        readonly int watch;
        readonly List<ChoiceAnswer> evolves = new List<ChoiceAnswer>();
        readonly List<int> listed = new List<int>();
        UiStage stage;
        VisualElement panel, dsBox, flash;
        Label jpOut;
        DsStage ds;
        int painted = -1;
        bool paintedOpen;
        Mode paintedMode = (Mode)(-1);

        /// <param name="watch">보는 좌석(결정 120 ① — 남의 진화 연출(39행)을 내 창과 같은 꼴로 보기만, −1 = 내 창). 그 좌석의 진화 단계가 끝나면 닫힌다.</param>
        public EvolveScreen(GameFlow flow, int watch = -1)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            this.watch = watch;
        }

        public int Watched => watch;

        /// <summary>보기 창: 그 좌석의 진화 단계가 아직 끝나지 않고 남았나.</summary>
        public bool Live => watch >= 0 && Pending(flow.Fx != null ? flow.Fx.Sequencer : null, out _) != null;

        int Seat => watch >= 0 ? watch : flow.Client.Seat;

        public string Name => "board-evolve";

        /// <summary>지금 보이는 모습.</summary>
        public Mode Now { get; private set; }

        /// <summary>반값(우러난 스튜)으로 진화하나 — 반값 답이 있을 때만 켤 수 있다.</summary>
        public bool Half { get; private set; }

        /// <summary>고르기 줄에 선 파티 포켓몬(인스턴스, 파티 순서).</summary>
        public IReadOnlyList<int> Listed => listed;

        /// <summary>진화 연출(39행)의 지금 모습과 단계 안 시각(연출이 아니면 기본값 · −1) — 확인용.</summary>
        public EvolvePose Pose { get; private set; }
        public int PoseMs { get; private set; } = -1;

        /// <summary>그 선택을 이 창이 받나(진화 갈래 · 옛 기술 — 보드 HUD는 그동안 「기다리는 중」).</summary>
        public static bool Owns(PendingChoice c) => c != null && c.Kind == ChoiceKind.Reward && (c.Sub == ChoiceSub.EvolveBranch || c.Sub == ChoiceSub.EvolveTeach);

        /// <summary>내 좌석에 진화 갈래 · 옛 기술 선택이 열려 있나.</summary>
        public static bool Waits(Sinnoh.Rules.Session.SeatClient client)
        {
            var c = client != null ? client.Choice(ChoiceKind.Reward) : null;
            return Owns(c);
        }

        bool Open => watch < 0 && stage != null && flow.BoardOpen;

        /// <summary>
        /// 화면이 그릴 그 포켓몬의 모습(포켓몬 표 색인): 진화 연출(39행)이 대기열에 남아 아직 끝나지 않았으면 진화 전 모습(보기는 이미 진화 뒤다 — 04 §8-3 표시 값처럼 착지 때 바뀐다).
        /// 보드 HUD의 파티 칸이 쓴다.
        /// </summary>
        public static int ShownDef(GameFlow flow, int pokemon, int def)
        {
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            if (seq == null) return def;
            foreach (var entry in seq.Queued)
            {
                var step = entry.Step(EvolveBeats.Row);
                var ev = WorkRows.EventOf<Evolved>(step);
                if (ev == null || ev.PokemonId != pokemon) continue;
                if (ReferenceEquals(entry, seq.Current) && seq.LocalMs >= step.EndMs) continue;
                return ev.From;
            }
            return def;
        }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            ds = DsStage.Create();
            var page = new VisualElement { pickingMode = PickingMode.Ignore };
            if (watch < 0)   // 보기 창은 보드를 덮지 않는다
            {
                var scrim = Add(page, new VisualElement(), "board-evolve-scrim", "sn-party-scrim");
                scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseEvolve());
            }
            panel = Add(page, Kit.Panel(stage.Skin), "board-evolve-panel", "sn-evolve-panel");
            painted = -1;
            Refresh();
            return page;
        }

        public void Close()
        {
            stage = null;
            panel = dsBox = flash = null;
            jpOut = null;
            if (ds != null) ds.Dispose();
            ds = null;
        }

        public bool Draws(FxStep step) => step != null && step.Row == EvolveBeats.Row && flow.Client != null && WorkRows.SeatOf(step) == Seat;

        /// <summary>진화 연출·갈래·옛 기술을 묻는 동안이 아니다 — 닫아도 된다.</summary>
        public bool CanClose => Now == Mode.Pick || Now == Mode.Egg;

        // 아직 끝나지 않은 내 진화 단계(재생 중이면 단계 안 시각, 기다리는 중이면 0)
        FxStep Pending(FxSequencer seq, out int ms)
        {
            ms = 0;
            if (seq == null) return null;
            foreach (var entry in seq.Queued)
            {
                var step = entry.Step(EvolveBeats.Row);
                if (!Draws(step)) continue;
                if (ReferenceEquals(entry, seq.Current))
                {
                    int local = seq.LocalMs - step.StartMs;
                    if (local >= step.Ms) continue;
                    ms = Math.Max(0, local);
                }
                return step;
            }
            return null;
        }

        Mode ModeNow(FxSequencer seq, out PendingChoice reward)
        {
            reward = watch < 0 ? flow.Client.Choice(ChoiceKind.Reward) : null;
            if (!Owns(reward)) reward = null;
            if (Pending(seq, out _) != null) return Mode.Show;
            if (reward != null) return reward.Sub == ChoiceSub.EvolveTeach ? Mode.Teach : Mode.Branch;
            if (EggFor >= 0 && EggsOf(EggFor).Count > 0) return Mode.Egg;   // pokemon_breeder B 알 기술(진화 답 Arg 2)을 고르는 중
            EggFor = -1;
            return Mode.Pick;
        }

        /// <summary>다시 그린다. 내 맵 행동이 닫혔고(턴이 끝났다) 묻는 것·연출도 없으면 창을 닫는다.</summary>
        public void Refresh()
        {
            if (stage == null || panel == null || flow.Client == null || flow.Client.View == null) return;
            var client = flow.Client;
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            var mode = ModeNow(seq, out var reward);
            if (watch >= 0 && mode != Mode.Show) return;   // 보기 창은 진화 연출만(끝나면 GameFlow가 닫는다)
            if (mode == Mode.Pick && client.Choice(ChoiceKind.MapAction) == null)
            {
                flow.CloseEvolve();
                return;
            }
            bool open = Open;
            if (painted == client.Version && paintedOpen == open && paintedMode == mode)
            {
                Animate(seq);
                return;
            }
            painted = client.Version;
            paintedOpen = open;
            paintedMode = mode;
            Now = mode;
            panel.Clear();
            dsBox = flash = null;
            jpOut = null;
            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            var title = Add(head, new VisualElement(), null, "sn-party-line");
            var titleText = Add(title, Kit.Text(watch >= 0 ? KoJosa.Fix(stage.T("screen.evolve.title.seat").Replace("{name}", flow.SeatName(watch))) : stage.T("screen.evolve.title"), Kit.FTitle), "board-evolve-title", "kit-title");
            if (watch >= 0) FaceBox.Owner(flow, watch, titleText);
            var inv = CultureInfo.InvariantCulture;
            int seat = Seat;
            int jp = flow.Fx != null ? flow.Fx.Sequencer.Shown.Get(ShownKey.Resource(seat, ResKind.Jp, -1), client.View.Seats[seat].Jp) : client.View.Seats[seat].Jp;
            var jpChip = Add(title, Kit.Badge(stage.T("screen.evolve.jp").Replace("{n}", jp.ToString(inv)), KitBadgeKind.Gold, Kit.F2), "board-evolve-jp", null);
            stage.Tips.Hook(jpChip, null, stage.T("screen.evolve.jp.tip"), TipSide.Below, reading: true);
            var close = Add(head, Kit.Button(stage.T("screen.village.close"), KitButtonKind.Main, () => { if (watch >= 0) flow.CloseWatch(); else flow.CloseEvolve(); }), "board-evolve-close", null);
            close.SetEnabled(watch >= 0 || mode == Mode.Pick || mode == Mode.Egg);
            switch (mode)
            {
                case Mode.Pick: PaintPick(open); break;
                case Mode.Branch: PaintBranch(reward, open); break;
                case Mode.Show: PaintShow(title); break;
                case Mode.Teach: PaintTeach(reward, open); break;
                case Mode.Egg: PaintEgg(open); break;
            }
            Animate(seq);
        }

        // ── 고르기 ──

        void PaintPick(bool open)
        {
            var client = flow.Client;
            var view = client.View;
            var ctx = flow.Ctx;
            int seat = client.Seat;
            var inv = CultureInfo.InvariantCulture;
            var choice = client.Choice(ChoiceKind.MapAction);
            evolves.Clear();
            if (choice != null) foreach (var a in client.Legal(choice)) if (a.Kind == AnswerKind.Evolve) evolves.Add(a);
            bool halfOk = evolves.Exists(a => a.Arg == 1);
            if (!halfOk) Half = false;
            Add(panel, Kit.Text(stage.T("screen.evolve.ask"), Kit.F2), "board-evolve-guide", "kit-muted");
            if (halfOk)
            {
                var half = Add(panel, new VisualElement(), "board-evolve-half", "sn-village-pay");
                half.AddToClassList("sn-evolve-half");
                Icon(half, IconKey(ctx.Items.Items[ctx.Items.Ids.IndexOf(CraftRules.InfusedStew)]), "sn-icon");
                half.Add(Kit.Text(stage.T("screen.evolve.half"), Kit.F2));
                stage.Tips.Hook(half, null, stage.T("screen.evolve.half.tip"), TipSide.Below, reading: true);
                half.AddToClassList(open ? "sn-party-press" : "sn-party-no");
                if (open) stage.OnTap(half, SoundKeys.Select, e => SetHalf(!Half));
                if (Half) half.AddToClassList("sn-party-picked");
            }
            var mons = MonArt.Load();
            listed.Clear();
            var party = view.Seats[seat].Party;
            int n = 0;
            foreach (int p in party)
            {
                if (p < 0) continue;
                int index = n++;
                int pokemon = p;
                listed.Add(p);
                var def = ctx.Cards.Pokemon[view.Pokemon[p].Def];
                var row = Add(panel, new VisualElement(), "board-evolve-mon-" + index, "sn-evolve-row");
                row.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
                var info = Add(row, new VisualElement(), null, "sn-evolve-info");
                var line = Add(info, new VisualElement(), null, "sn-party-line");
                line.Add(Kit.Text(stage.T("pokemon." + def.Key), Kit.F2));
                var into = new List<int>();
                EvolveRules.Branches(view, ctx, p, into);
                string why = null;
                if (into.Count == 0) why = "screen.evolve.none";
                else
                {
                    line.Add(Kit.Text(Arrow, Kit.F2));
                    line.Add(Kit.Text(into.Count == 1 ? stage.T("pokemon." + ctx.Cards.Pokemon[into[0]].Key) : stage.T("screen.evolve.branches").Replace("{n}", into.Count.ToString(inv)), Kit.F2));
                    int cost = EvolveRules.Cost(view, ctx, seat, p, Half);
                    var costLine = Add(info, new VisualElement(), null, "sn-party-line");
                    costLine.Add(Kit.Badge(stage.T("screen.evolve.cost").Replace("{n}", cost.ToString(inv)), Half ? KitBadgeKind.Ok : KitBadgeKind.Plain, Kit.F2));
                    if (!evolves.Exists(a => a.Target == pokemon && a.Arg == (Half ? 1 : -1))) why = "screen.evolve.no_jp";
                }
                var buttons = Add(row, new VisualElement(), null, "sn-evolve-buttons");
                var go = Add(buttons, Kit.Button(stage.T("screen.evolve.go"), KitButtonKind.Main, () => PressEvolve(pokemon)), "board-evolve-go-" + index, null);
                go.SetEnabled(CanEvolve(pokemon));
                // pokemon_breeder B: [알 기술] — 진화 대신 진화형의 시그니처를 알 기술로(같은 비용 · 더는 진화하지 않는다, rulings A39)
                if (!Half && EggsOf(pokemon).Count > 0)
                {
                    var egg = Add(buttons, Kit.Button(stage.T("screen.evolve.egg"), KitButtonKind.Normal, () => OpenEgg(pokemon)), "board-evolve-egg-" + index, null);
                    egg.SetEnabled(open);
                    int card = CardAsk.CardOf(view, seat);
                    if (open) stage.Tips.Hook(egg, CardAsk.Head(stage, ctx, card, 1), CardAsk.Text(stage, ctx, card, 1), TipSide.Above, reading: true);
                }
                if (why != null)
                {
                    row.AddToClassList("sn-party-no");
                    Add(info, Kit.Text(stage.T(why), Kit.F2), null, "sn-village-why");
                }
            }
        }

        /// <summary>그 포켓몬을 지금 진화시킬 수 있나(반값을 켰으면 반값 답).</summary>
        public bool CanEvolve(int pokemon) => Open && Now == Mode.Pick && evolves.Exists(a => a.Target == pokemon && a.Arg == (Half ? 1 : -1));

        /// <summary>[반값으로]를 켜고 끈다. 반값 답이 없으면 false.</summary>
        public bool SetHalf(bool on)
        {
            if (stage == null || Now != Mode.Pick || (on && !evolves.Exists(a => a.Arg == 1))) return false;
            Half = on;
            painted = -1;
            Refresh();
            return true;
        }

        /// <summary>[진화]: 그 포켓몬(인스턴스)의 진화 답을 낸다. 받아들여졌으면 true — 갈래가 여럿이면 갈래를 묻고, 아니면 진화 연출이 이 창에서 돈다.</summary>
        public bool PressEvolve(int pokemon)
        {
            if (!CanEvolve(pokemon)) return false;
            var choice = flow.Client.Choice(ChoiceKind.MapAction);
            var answer = evolves.Find(a => a.Target == pokemon && a.Arg == (Half ? 1 : -1));
            if (choice == null || answer == null) return false;
            stage.Tips.Hide();
            bool ok = flow.Answer(choice, answer).Accepted;
            if (ok) Half = false;
            return ok;
        }

        // ── 알 기술(pokemon_breeder B — 진화 답 Arg 2, Values = [진화형, 칸]) ──

        /// <summary>알 기술을 고르는 포켓몬(인스턴스, 아니면 −1).</summary>
        public int EggFor { get; private set; } = -1;

        readonly List<ChoiceAnswer> eggs = new List<ChoiceAnswer>();

        /// <summary>그 포켓몬의 알 기술 답(엔진의 합법 답 그대로).</summary>
        public List<ChoiceAnswer> EggsOf(int pokemon)
        {
            var list = new List<ChoiceAnswer>();
            var choice = flow.Client != null ? flow.Client.Choice(ChoiceKind.MapAction) : null;
            if (choice != null) foreach (var a in flow.Client.Legal(choice)) if (a.Kind == AnswerKind.Evolve && a.Target == pokemon && a.Arg == TrainerRules.EggArg) list.Add(a);
            return list;
        }

        /// <summary>[알 기술]: 그 포켓몬의 알 기술 고르기를 연다(엔진에 답을 내지 않는다). 답이 없으면 false.</summary>
        public bool OpenEgg(int pokemon)
        {
            if (stage == null || !Open || Now != Mode.Pick || EggsOf(pokemon).Count == 0) return false;
            stage.Tips.Hide();
            EggFor = pokemon;
            Refresh();
            return true;
        }

        /// <summary>알 기술 고르기의 [뒤로].</summary>
        public bool CloseEgg()
        {
            if (EggFor < 0) return false;
            EggFor = -1;
            if (stage != null) stage.Tips.Hide();
            Refresh();
            return true;
        }

        void PaintEgg(bool open)
        {
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            var inv = CultureInfo.InvariantCulture;
            eggs.Clear();
            eggs.AddRange(EggsOf(EggFor));
            var p = view.Pokemon[EggFor];
            var def = ctx.Cards.Pokemon[p.Def];
            int card = CardAsk.CardOf(view, flow.Client.Seat);
            var head = Add(panel, Kit.Wrapped(CardAsk.Head(stage, ctx, card, 1), Kit.F2), "board-evolve-egg-head", "kit-muted");
            stage.Tips.Hook(head, null, CardAsk.Text(stage, ctx, card, 1), TipSide.Below, reading: true);
            int cost = EvolveRules.Cost(view, ctx, flow.Client.Seat, EggFor, false);
            Add(panel, Kit.Wrapped(KoJosa.Fix(stage.T("screen.evolve.egg.ask").Replace("{name}", stage.T("pokemon." + def.Key)).Replace("{n}", cost.ToString(inv))), Kit.F2), "board-evolve-guide", null);
            // 진화형마다: 그 시그니처 기술 줄 + 넣을 칸(엔진 답의 칸만 누를 수 있다)
            var tos = new List<int>();
            foreach (var a in eggs) if (!tos.Contains(a.Values[0])) tos.Add(a.Values[0]);
            for (int t = 0; t < tos.Count; t++)
            {
                int to = tos[t];
                var from = Add(panel, new VisualElement(), "board-evolve-egg-from-" + t, "sn-party-line");
                from.Add(Kit.Text(KoJosa.Fix(stage.T("screen.evolve.egg.from").Replace("{name}", stage.T("pokemon." + ctx.Cards.Pokemon[to].Key))), Kit.F2));
                var old = Add(panel, new VisualElement(), "board-evolve-egg-move-" + t, "sn-evolve-old");
                old.Add(MonParts.MoveRow(stage, ctx, ctx.Cards.Pokemon[to].Signature, false, TipSide.Below, panel));
                for (int slot = 0; slot < PokemonState.MoveSlots; slot++)
                {
                    int sl = slot;
                    var box = Add(panel, new VisualElement(), "board-evolve-egg-" + t + "-" + slot, "sn-evolve-slot");
                    box.Add(Kit.Text(stage.T("screen.evolve.slot").Replace("{n}", (slot + 1).ToString(inv)), Kit.F2));
                    if (p.Moves[slot] >= 0) box.Add(MonParts.MoveRow(stage, ctx, p.Moves[slot], false, null));
                    else box.Add(MonParts.TacticRow(stage, ctx, p, slot, TipSide.Below) ?? Kit.Text(stage.T("screen.evolve.slot.empty"), Kit.F2));   // 전설 특수기술이 차지한 빈 칸(결정 195)
                    bool can = open && eggs.Exists(a => a.Values[0] == to && a.Values[1] == sl);
                    box.AddToClassList(can ? "sn-party-press" : "sn-party-no");
                    if (can) stage.OnTap(box, SoundKeys.Decide, e => PressEgg(to, sl));
                    if (p.Moves[slot] >= 0) MonParts.MoveTip(stage, ctx, box, p.Moves[slot], TipSide.Below, panel, stage.T("screen.evolve.egg.slot.tip"));   // 칸 하나에 툴팁 하나 — 지금 기술의 효과 + 안내(결정 186)
                }
            }
            var back = Add(panel, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, () => CloseEgg()), "board-evolve-egg-back", "sn-evolve-keep");
            back.SetEnabled(open);
        }

        /// <summary>알 기술: 진화형 to의 시그니처를 그 칸에 가르치는 답을 낸다. 받아들여졌으면 true.</summary>
        public bool PressEgg(int to, int slot)
        {
            if (!Open || Now != Mode.Egg) return false;
            var choice = flow.Client.Choice(ChoiceKind.MapAction);
            var answer = eggs.Find(a => a.Values[0] == to && a.Values[1] == slot);
            if (choice == null || answer == null) return false;
            stage.Tips.Hide();
            bool ok = flow.Answer(choice, answer).Accepted;
            if (ok) EggFor = -1;
            return ok;
        }

        // ── 갈래 ──

        readonly List<ChoiceAnswer> branches = new List<ChoiceAnswer>();

        /// <summary>갈래 카드에 선 진화 뒤 모습(포켓몬 표 색인, 엔진의 답 순서).</summary>
        public IReadOnlyList<ChoiceAnswer> Branches => branches;

        void PaintBranch(PendingChoice reward, bool open)
        {
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            branches.Clear();
            branches.AddRange(flow.Client.Legal(reward));
            var from = ctx.Cards.Pokemon[view.Pokemon[reward.Subject].Def];
            Add(panel, Kit.Text(KoJosa.Fix(stage.T("screen.evolve.branch").Replace("{name}", stage.T("pokemon." + from.Key))), Kit.F2), "board-evolve-guide", null);
            var grid = Add(panel, new VisualElement(), null, "sn-evolve-branches");
            var mons = MonArt.Load();
            for (int i = 0; i < branches.Count; i++)
            {
                int index = i;
                var def = ctx.Cards.Pokemon[branches[i].Target];
                var card = Add(grid, new VisualElement(), "board-evolve-branch-" + (i + 1).ToString(CultureInfo.InvariantCulture), "sn-evolve-branch");
                card.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
                Add(card, Kit.Text(stage.T("pokemon." + def.Key), Kit.F2), null, "sn-evolve-branch-name");   // 도트 칸과 띄운다(결정 87 V1 ① — 큰 도트의 발에 이름이 닿았다)
                var types = Add(card, new VisualElement(), null, "sn-enc-line");
                foreach (var t in def.Types) MonParts.TypeIcon(types, t);
                card.AddToClassList(open ? "sn-party-press" : "sn-party-no");
                if (open) stage.OnTap(card, SoundKeys.Decide, e => PressBranch(index));
                stage.Tips.Hook(card, stage.T("pokemon." + def.Key), stage.TOrNull("screen.evolve.branch.tip"), TipSide.Above, reading: true);
            }
        }

        /// <summary>갈래 카드(왼쪽에서부터 0)를 고른다. 받아들여졌으면 true.</summary>
        public bool PressBranch(int index)
        {
            if (!Open || Now != Mode.Branch || index < 0 || index >= branches.Count) return false;
            var choice = flow.Client.Choice(ChoiceKind.Reward);
            if (!Owns(choice)) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, branches[index]).Accepted;
        }

        // ── 진화 연출(39행) ──


        void PaintShow(VisualElement title)
        {
            jpOut = Add(title, Kit.Text("", Kit.F2), "board-evolve-jp-out", "sn-evolve-jpout");
            var frame = Add(panel, new VisualElement(), "board-evolve-frame", "sn-battle-ds-frame");
            Kit.SetFrame(frame, stage.Skin.Panel);
            dsBox = Add(frame, new VisualElement(), "board-evolve-ds", "sn-evolve-ds");
            if (ds != null) dsBox.style.backgroundImage = Background.FromRenderTexture(ds.Output);
            flash = Add(dsBox, new VisualElement { pickingMode = PickingMode.Ignore }, "board-evolve-flash", "sn-evolve-flash");
            if (watch < 0) Add(panel, Kit.Text(stage.T("screen.evolve.skip"), Kit.F2), "board-evolve-skip", "kit-muted");   // 남의 진화는 건너뛸 수 없다(보기 창엔 안내 줄 없음)
        }

        /// <summary>
        /// 스페이스 바(결정 84 L11 — <see cref="GameFlow.SkipKey"/>): 재생 중인 내 진화 연출을 착지(<see cref="EvolveBeats.SkipAt"/> — 진화 뒤 모습의 제 색 + 「축하합니다!」)로 건너뛴다.
        /// 소리 없이(04 §8-4), 그 항목 안에서만 — 남은 꼬리(약 480ms)와 뒤 단계는 그대로 돈다. 건너뛰었으면 true.
        /// </summary>
        public bool Skip()
        {
            var seq = flow.Fx != null ? flow.Fx.Sequencer : null;
            if (stage == null || Now != Mode.Show || seq == null || seq.Current == null) return false;
            var step = Pending(seq, out int ms);
            if (step == null || !seq.Current.Steps.Contains(step)) return false;
            int at = EvolveBeats.SkipAt(step.Ms);
            if (ms >= at || !seq.SkipTo(step.StartMs + at)) return false;
            Animate(seq);
            return true;
        }

        /// <summary>DS 화면에서 진화하는 포켓몬의 발 자리(가운데 — 상대 발판 고정점에서 옮긴 만큼, DS px).</summary>
        /// <summary>진화 연출의 바탕(M9 다듬기 P10 — 결정 80 L11 나): 원작 Pt 진화 화면의 배경 그림(256×192, Gen stage 묶음이 원작 자산에서 짜 맞춘다).</summary>
        public const string BgKey = "Stage/Evolve/bg.png";

        public static readonly Vector2Int MonShift = new Vector2Int(DsStage.Width / 2 - 192, 40);

        // ── 옛 기술 ──

        readonly List<ChoiceAnswer> teach = new List<ChoiceAnswer>();

        void PaintTeach(PendingChoice reward, bool open)
        {
            var ctx = flow.Ctx;
            var view = flow.Client.View;
            teach.Clear();
            teach.AddRange(flow.Client.Legal(reward));
            int pokemon = reward.Options[0];
            var p = view.Pokemon[pokemon];
            var def = ctx.Cards.Pokemon[p.Def];
            Add(panel, Kit.Text(KoJosa.Fix(stage.T("screen.evolve.teach").Replace("{name}", stage.T("pokemon." + def.Key)).Replace("{move}", stage.T(BattleFeed.MoveNameKey(ctx, reward.Subject)))), Kit.F2), "board-evolve-guide", null);
            var old = Add(panel, new VisualElement(), "board-evolve-old", "sn-evolve-old");
            old.Add(MonParts.MoveRow(stage, ctx, reward.Subject, false, TipSide.Below, panel));
            // 진화한 포켓몬이 지금 가진 기술(결정 181): ★ 고유 기술(바꿀 수 없다) + 칸 1·2 — 정비 판과 같은 줄
            Add(panel, Kit.Text(KoJosa.Fix(stage.T("screen.evolve.teach.now").Replace("{name}", stage.T("pokemon." + def.Key))), Kit.F2), "board-evolve-now", "sn-village-label");
            var sig = Add(panel, new VisualElement(), "board-evolve-sig", "sn-evolve-slot");
            sig.AddToClassList("sn-evolve-fixed");
            sig.Add(Kit.Text(stage.T("screen.mon.sig"), Kit.F2));
            sig.Add(MonParts.MoveRow(stage, ctx, def.Signature, true, null));
            MonParts.MoveTip(stage, ctx, sig, def.Signature, TipSide.Below, panel, stage.T("screen.evolve.sig.tip"));   // 칸 하나에 툴팁 하나(결정 186 — 기술 효과 + 안내)
            for (int slot = 0; slot < PokemonState.MoveSlots; slot++)
            {
                int s = slot;
                var box = Add(panel, new VisualElement(), "board-evolve-teach-" + slot, "sn-evolve-slot");
                box.Add(Kit.Text(stage.T("screen.evolve.slot").Replace("{n}", (slot + 1).ToString(CultureInfo.InvariantCulture)), Kit.F2));
                if (p.Moves[slot] >= 0) box.Add(MonParts.MoveRow(stage, ctx, p.Moves[slot], false, null));
                else if (p.Breach[slot] >= 0) box.Add(MonParts.BreachRow(stage, ctx, p.Breach[slot], TipSide.Below));   // 균열 카드가 막은 칸(rulings A52 ①) — 가르칠 수 없다
                else box.Add(MonParts.TacticRow(stage, ctx, p, slot, TipSide.Below) ?? Kit.Text(stage.T("screen.evolve.slot.empty"), Kit.F2));   // 전설 특수기술이 차지한 빈 칸(결정 195)
                bool can = open && teach.Exists(a => a.Kind == AnswerKind.Pick && a.Arg == s);
                box.AddToClassList(can ? "sn-party-press" : "sn-party-no");
                if (can) stage.OnTap(box, SoundKeys.Decide, e => PressTeach(s));
                if (p.Moves[slot] >= 0) MonParts.MoveTip(stage, ctx, box, p.Moves[slot], TipSide.Below, panel, stage.T("screen.evolve.slot.tip"));   // 지금 기술의 효과 + 안내(결정 186)
            }
            var keep = Add(panel, Kit.Button(stage.T("screen.evolve.keep"), KitButtonKind.Normal, () => PressTeach(-1)), "board-evolve-keep", "sn-evolve-keep");
            keep.SetEnabled(open);
        }

        /// <summary>옛 기술: 그 칸(0·1)에 가르치거나(slot &lt; 0이면 가르치지 않음) 답한다. 받아들여졌으면 true.</summary>
        public bool PressTeach(int slot)
        {
            if (!Open || Now != Mode.Teach) return false;
            var choice = flow.Client.Choice(ChoiceKind.Reward);
            if (!Owns(choice)) return false;
            var answer = slot < 0 ? teach.Find(a => a.Kind == AnswerKind.Decline) : teach.Find(a => a.Kind == AnswerKind.Pick && a.Arg == slot);
            if (answer == null) return false;
            stage.Tips.Hide();
            return flow.Answer(choice, answer).Accepted;
        }

        // ── 프레임 ──

        public void ShowFx(FxSequencer seq)
        {
            if (stage == null || panel == null) return;
            var mode = ModeNow(seq, out _);
            if (Open != paintedOpen || mode != paintedMode) Refresh();
            else Animate(seq);
        }

        void Animate(FxSequencer seq)
        {
            var step = Pending(seq, out int ms);
            if (step == null || dsBox == null)
            {
                Pose = default;
                PoseMs = -1;
                return;
            }
            var ev = WorkRows.EventOf<Evolved>(step);
            if (ev == null) return;
            var pose = EvolveBeats.At(ms, step.Ms);
            Pose = pose;
            PoseMs = ms;
            var ctx = flow.Ctx;
            string fromKey = ctx.Cards.Pokemon[ev.From].Key, toKey = ctx.Cards.Pokemon[ev.To].Key;
            string fromName = stage.T("pokemon." + fromKey), toName = stage.T("pokemon." + toKey);
            string message = pose.Message == 2
                ? KoJosa.Fix(stage.T("screen.evolve.done").Replace("{name}", fromName).Replace("{to}", toName))
                : KoJosa.Fix(stage.T("screen.evolve.what").Replace("{name}", fromName));
            if (ds != null)
                ds.Draw(new DsScene
                {
                    Enemy = new DsMon { Key = pose.To ? toKey : fromKey, Name = "", BoxShown = false, Dx = MonShift.x, Dy = MonShift.y, White = pose.White },
                    Message = message,
                    Bg = BgKey,
                });
            if (flash != null) FxUi.Fade(flash, pose.Screen);
            if (jpOut != null)
            {
                jpOut.text = Signed(-ev.JpCost);
                FxUi.Fade(jpOut, 1f - pose.JpOut);
            }
        }
    }
}
