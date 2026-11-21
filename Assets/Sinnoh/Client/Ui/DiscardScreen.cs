using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 버림 더미 보기 창(결정 196 — 사용자 11-21 「버려진 카드들을 확인할 수 있나」 · 사당 이벤트로 버림 더미에서 기술 · 포켓몬을 가져오기 전에 미리 본다): 위 띠의 [버림 더미](<see cref="GameFlow.ToggleDiscards"/>)로
    /// 보드 · 물음 창 · 배틀 · 남의 턴 어디서든 연다(상성 창과 같은 층 — 창이 떠 있으면 그 위 정보 창 층). 버림 더미는 모두가 보는 공개 값(<see cref="GameState.Discards"/>)이다.
    /// 왼쪽 = 버린 카드가 있는 덱(이름 · 장수 — 누르면 그 덱), 오른쪽 = 그 덱의 카드(같은 카드는 한 칸 + 「×n」, 최근에 버린 것부터 · 쪽 넘김 <see cref="PageCards"/>).
    /// 마우스 오버: 포켓몬 = 포켓몬 카드(<see cref="MonTip"/> — 고유 기술 · 기술 효과까지), 그림 카드(사당 이벤트 · 운명의 변화 · 균열 · 온슬롯 · 막 …) = 이름과 카드 문구.
    /// 요소 이름: <c>board-discards</c>·<c>-panel</c>·<c>-close</c>·<c>-deck-&lt;덱&gt;</c>(덱 키의 snake — 예 <c>weak</c>)·<c>-deck-&lt;덱&gt;-count</c>·<c>-card-&lt;n&gt;</c>(그 쪽의 1부터)·<c>-page</c>·<c>-prev</c>·<c>-next</c>·<c>-none</c>.
    /// </summary>
    public sealed class DiscardScreen : IUiScreen
    {
        /// <summary>한 쪽의 카드 칸 수 [가정 결정 196 — 넘치면 쪽 넘김].</summary>
        public const int PageCards = 30;

        const string DeckHead = "deck.";
        readonly GameFlow flow;
        UiStage stage;
        VisualElement panel, deckBox, listBox, pager;
        MonTip monTip;
        MonArt mons;
        readonly List<KeyValuePair<int, int>> cards = new List<KeyValuePair<int, int>>();   // (카드, 장수) — 지금 덱

        public DiscardScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
        }

        public string Name => "board-discards";

        /// <summary>고른 덱(DeckId 색인 — 버린 카드가 없으면 −1).</summary>
        public int Deck { get; private set; } = -1;
        public int Page { get; private set; }
        public int Pages { get; private set; } = 1;

        /// <summary>지금 덱의 카드(카드 색인, 장수) — 화면 순서.</summary>
        public IReadOnlyList<KeyValuePair<int, int>> Cards => cards;

        /// <summary>버린 카드가 있는 덱(DeckId 색인, 덱 순서).</summary>
        public static List<int> Decks(GameState view)
        {
            var r = new List<int>();
            if (view == null) return r;
            for (int d = 0; d < view.Discards.Length; d++)
                if (view.Discards[d].Count > 0) r.Add(d);
            return r;
        }

        /// <summary>덱 키의 snake(「deck.weak」 → 「weak」) — 요소 이름 · 글 키에 쓴다.</summary>
        public static string Snake(Ctx ctx, int deck)
        {
            string key = ctx.Decks.Decks[deck].Key;
            return key.StartsWith(DeckHead, StringComparison.Ordinal) ? key.Substring(DeckHead.Length) : key;
        }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            mons = MonArt.Load();
            var page = new VisualElement { pickingMode = PickingMode.Ignore };   // 판 밖(위 띠 · 아래 판)의 입력을 먹지 않는다
            if (flow.Stage.Current is BattleScreen) page.AddToClassList("sn-party-battle");   // 배틀 위: 어둠이 독까지 덮는다
            var scrim = Add(page, new VisualElement(), "board-discards-scrim", "sn-party-scrim");
            scrim.RegisterCallback<PointerDownEvent>(e => flow.CloseDiscards());   // 창 밖을 누르면 닫힌다
            panel = Add(page, Kit.Panel(stage.Skin), "board-discards-panel", "sn-types-panel");
            var head = Add(panel, new VisualElement(), null, "sn-party-head");
            Add(head, Kit.Text(stage.T("screen.discards.title"), Kit.FTitle), "board-discards-title", "kit-title");
            Add(head, Kit.Button(stage.T("screen.discards.close"), KitButtonKind.Main, () => flow.CloseDiscards()), "board-discards-close", null);
            var body = Add(panel, new VisualElement(), null, "sn-discards-body");
            deckBox = Add(body, new VisualElement(), "board-discards-decks", "sn-discards-decks");
            var right = Add(body, new VisualElement(), null, "sn-discards-right");
            pager = Add(right, new VisualElement(), null, "sn-discards-pager");
            listBox = Add(right, new VisualElement(), "board-discards-list", "sn-discards-list");
            monTip = new MonTip(stage, flow.Ctx);
            Refresh();
            return page;
        }

        public void Close()
        {
            monTip?.Detach();
            monTip = null;
            stage = null;
            panel = deckBox = listBox = pager = null;
        }

        /// <summary>판의 버림 더미가 바뀌었을 수 있다: 다시 그린다(고른 덱 · 쪽은 남긴다 — 그 덱이 비었으면 첫 덱).</summary>
        public void Refresh()
        {
            if (stage == null || deckBox == null) return;
            var view = flow.Client != null ? flow.Client.View : null;
            var shown = Decks(view);
            if (!shown.Contains(Deck))
            {
                Deck = shown.Count > 0 ? shown[0] : -1;
                Page = 0;
            }
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            stage.Tips.Hide();
            monTip?.Hide();
            deckBox.Clear();
            foreach (int d in shown)
            {
                int deck = d;
                string snake = Snake(ctx, d);
                var chip = Add(deckBox, new VisualElement(), "board-discards-deck-" + snake, "sn-discards-deck");
                if (d == Deck) chip.AddToClassList("sn-discards-deck-picked");
                Add(chip, Kit.Text(stage.T("screen.discards.deck." + snake), Kit.F2), null, null);
                Add(chip, Kit.Badge(view.Discards[d].Count.ToString(inv), KitBadgeKind.Plain, Kit.F2), "board-discards-deck-" + snake + "-count", null);
                stage.OnTap(chip, SoundKeys.Select, e => PickDeck(deck));
            }
            FillList(view);
        }

        /// <summary>그 덱(DeckId 색인)을 본다. 버린 카드가 없는 덱이면 false.</summary>
        public bool PickDeck(int deck)
        {
            if (stage == null || !Decks(flow.Client.View).Contains(deck)) return false;
            Deck = deck;
            Page = 0;
            Refresh();
            return true;
        }

        /// <summary>쪽을 넘긴다(−1 · +1). 넘겼으면 true.</summary>
        public bool Turn(int delta)
        {
            int to = Mathf.Clamp(Page + delta, 0, Pages - 1);
            if (stage == null || to == Page) return false;
            Page = to;
            FillList(flow.Client.View);
            return true;
        }

        void FillList(GameState view)
        {
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            cards.Clear();
            listBox.Clear();
            pager.Clear();
            if (Deck < 0)
            {
                Pages = 1;
                Add(listBox, Kit.Text(stage.T("screen.discards.none"), Kit.F2), "board-discards-none", "kit-muted");
                return;
            }
            // 같은 카드는 한 칸(장수) — 더미의 끝(맨 위 = 최근에 버린 것)부터
            var pile = view.Discards[Deck];
            for (int i = pile.Count - 1; i >= 0; i--)
            {
                int at = cards.FindIndex(kv => kv.Key == pile[i]);
                if (at >= 0) cards[at] = new KeyValuePair<int, int>(pile[i], cards[at].Value + 1);
                else cards.Add(new KeyValuePair<int, int>(pile[i], 1));
            }
            Pages = Math.Max(1, (cards.Count + PageCards - 1) / PageCards);
            Page = Mathf.Clamp(Page, 0, Pages - 1);
            // 면 바꾸기(결정 196 덧 — 사용자 11-21 「뒷면(기술)으로 볼 수 있는 버튼」): 포켓몬 덱만 켠다. 면은 창을 다시 열어도 마지막 것(GameFlow.DiscardMoves) [가정]
            var kind = ctx.Decks.Decks[Deck].Kind;
            bool moves = MoveFace && kind == DeckKind.Pokemon;
            var face = Add(pager, Kit.Button(stage.T(MoveFace ? "screen.discards.face.mons" : "screen.discards.face.moves"), KitButtonKind.Normal, () => Flip()), "board-discards-face", null);
            face.SetEnabled(kind == DeckKind.Pokemon);
            stage.Tips.Hook(face, null, stage.T("screen.discards.face.tip"), TipSide.Below, reading: true);
            if (Pages > 1)
            {
                Add(pager, Kit.Badge((Page + 1).ToString(inv) + "/" + Pages.ToString(inv), KitBadgeKind.Plain, Kit.F2), "board-discards-page", null);
                Add(pager, Kit.Button(stage.T("screen.discards.prev"), KitButtonKind.Normal, () => Turn(-1)), "board-discards-prev", null).SetEnabled(Page > 0);
                Add(pager, Kit.Button(stage.T("screen.discards.next"), KitButtonKind.Normal, () => Turn(1)), "board-discards-next", null).SetEnabled(Page < Pages - 1);
            }
            for (int k = Page * PageCards; k < Math.Min(cards.Count, (Page + 1) * PageCards); k++)
            {
                int card = cards[k].Key, copies = cards[k].Value;
                var chip = Add(listBox, new VisualElement(), "board-discards-card-" + (k - Page * PageCards + 1).ToString(inv), "sn-discards-card");
                var art = Add(chip, new VisualElement(), null, "sn-discards-art");
                art.pickingMode = PickingMode.Ignore;
                string name;
                if (moves && card >= 0 && card < ctx.Cards.Pokemon.Count)
                {
                    // 기술 면 = 그 카드의 고유 기술(PokemonDef.Signature — 사당 「고대의 지혜」가 버림 더미에서 가져오는 기술 · ShrineRules.WisdomMoves). 기술 줄 그대로(마우스 오버 = 이름 + 효과)
                    art.style.display = DisplayStyle.None;
                    int move = ctx.Cards.Pokemon[card].Signature;
                    if (move >= 0) Add(chip, MonParts.MoveRow(stage, ctx, move, true, TipSide.Below), null, "sn-discards-move");
                    else Add(chip, Kit.Text(BoardHud.ShortName(stage.T("pokemon." + ctx.Cards.Pokemon[card].Key)), Kit.F2), null, "sn-discards-name");
                    if (copies > 1) Add(chip, Kit.Badge("×" + copies.ToString(inv), KitBadgeKind.Plain, Kit.F2), null, null);
                    continue;
                }
                if (kind == DeckKind.Pokemon && card >= 0 && card < ctx.Cards.Pokemon.Count)
                {
                    string key = ctx.Cards.Pokemon[card].Key;
                    if (mons != null && mons.Has(key)) art.Add(mons.Front(key));
                    name = BoardHud.ShortName(stage.T("pokemon." + key));
                    monTip.Hook(chip, card, TipSide.Below);   // 포켓몬 카드 그대로(고유 기술 · 기술 효과)
                }
                else if (kind == DeckKind.TrainerCard && card >= 0 && card < ctx.Cards.TrainerCards.Count)
                {
                    name = stage.T("trainer_card." + ctx.Cards.TrainerCards[card].Key);
                    art.style.display = DisplayStyle.None;
                    stage.Tips.Hook(chip, name, null, TipSide.Below);
                }
                else
                {
                    string key = card >= 0 && card < ctx.Decks.CardIds.Count ? ctx.Decks.CardIds[card] : "";
                    var tex = CardArt(key);
                    if (tex != null) art.style.backgroundImage = new StyleBackground(tex);
                    else art.style.display = DisplayStyle.None;   // 그림이 없는 카드(은행상회 · 막 · 고향 마을 …)는 이름만
                    name = stage.TOrNull(key) ?? key;
                    stage.Tips.Hook(chip, name, CardText(stage, key), TipSide.Below, reading: true);
                }
                Add(chip, Kit.Text(name, Kit.F2), null, "sn-discards-name");
                if (copies > 1) Add(chip, Kit.Badge("×" + copies.ToString(inv), KitBadgeKind.Plain, Kit.F2), null, null);
            }
        }

        /// <summary>그림 카드의 문구(카드 키의 .text.1 · .text.2 … 줄을 잇는다). 없으면 null.</summary>
        public static string CardText(UiStage stage, string key)
        {
            var lines = new List<string>();
            for (int i = 1; i <= 9; i++)
            {
                string line = stage.TOrNull(key + ".text." + i.ToString(CultureInfo.InvariantCulture));
                if (line == null) break;
                lines.Add(line);
            }
            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }

        // 그림 카드의 그림(있는 덱만 — 사당 이벤트 · 균열 · 운명의 변화 · 온슬롯). 없으면 null(이름만)
        static Texture2D CardArt(string key)
        {
            int dot = key.IndexOf('.');
            if (dot < 0) return null;
            string deck = key.Substring(0, dot), rest = key.Substring(dot + 1);
            switch (deck)
            {
                case "shrine_event": return GenAssets.Load<Texture2D>(GenAssets.ShrineArt + rest + ".png");
                case "distortion_breach": return GenAssets.Load<Texture2D>(GenAssets.BreachArt + rest + ".png");
                case "change_of_fate": return GenAssets.Load<Texture2D>(GenAssets.FateArt + rest + ".png");
                case "ultra_onslaught": return GenAssets.Load<Texture2D>(GenAssets.OnslaughtArt);
                default: return null;
            }
        }

        /// <summary>확인용(캡처 · 시험): 지금 쪽의 n번째 카드 칸(1부터)에 마우스를 올린 것처럼 마우스 오버를 띄운다. 칸이 없으면 false.</summary>
        public bool Hover(int n)
        {
            var chip = listBox != null ? listBox.Q("board-discards-card-" + n.ToString(CultureInfo.InvariantCulture)) : null;
            if (chip == null) return false;
            int k = Page * PageCards + n - 1;
            var row = chip.Q(className: "sn-enc-move");
            if (row != null) stage.Tips.Show(row);   // 기술 면: 기술 줄의 마우스 오버(이름 + 효과)
            else if (flow.Ctx.Decks.Decks[Deck].Kind == DeckKind.Pokemon) monTip.Show(chip, cards[k].Key, TipSide.Below);
            else stage.Tips.Show(chip);
            return true;
        }

        /// <summary>포켓몬 카드를 기술 면(카드 뒷면 = 고유 기술)으로 보나 — 창을 닫았다 열어도 남는다(<see cref="GameFlow.DiscardMoves"/>).</summary>
        public bool MoveFace => flow.DiscardMoves;

        /// <summary>[기술 면] · [포켓몬 면]: 포켓몬 덱 카드의 면을 바꾼다(포켓몬 덱이 아니면 false).</summary>
        public bool Flip()
        {
            if (stage == null || Deck < 0 || flow.Ctx.Decks.Decks[Deck].Kind != DeckKind.Pokemon) return false;
            flow.DiscardMoves = !flow.DiscardMoves;
            stage.Tips.Hide();
            monTip?.Hide();
            FillList(flow.Client.View);
            return true;
        }

        /// <summary>떠 있는 포켓몬 카드(없으면 −1).</summary>
        public int MonShown => monTip != null ? monTip.Shown : -1;

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
