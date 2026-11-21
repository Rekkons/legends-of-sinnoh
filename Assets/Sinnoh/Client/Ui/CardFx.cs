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
    /// 트레이너 카드 덮개(M6 「트레이너 카드 해금 연출」 — 04 §8-8 48·49·50행, 결정 84 M5 가). 무대의 맨 위 층(<see cref="UiStage.Over"/> — 스탬프·VS·거래 덮개 <see cref="DealFx"/> 안의 맨 위)에
    /// <see cref="GameFlow"/>가 하나 둔다: 해금(내 좌석 — 보드를 어둡게 덮고 원작 카드 틀의 큰 카드: 등급 아이콘이 박히고 자물쇠가 흔들리다 깨지며 카드가 뒤집혀 다음 등급 · 금속광 · 열린 칸의 금 테두리) ·
    /// 카드 바꾸기(옛 카드가 뒤집혀 새 카드 골드) · 능력 사용(왼쪽 위에 작은 카드가 떠올라 쓴 능력 칸이 빛나고 통통). 남 좌석의 것은 좌석 칩 꼬리표(<see cref="BoardHud"/> — <see cref="OtherActs"/>).
    /// 카드 틀 그림은 Gen <c>Cards/Trainer/&lt;등급&gt;_base·_bar · locked_blue·red · icon_moderate·strong</c>(원작 tc-assets — cards 번들), 직업 그림은 <see cref="BoardPieces.FrontKey"/>.
    /// 박자는 <c>Sinnoh.Fx</c> <see cref="UnlockBeats"/>·<see cref="SwapBeats"/>·<see cref="AbilityBeats"/>, 시간은 대기열 시각만(옮기기·늘이기·흐리기 — FxUi). 자리·크기·색은 USS <c>sn-card-fx-*</c>.
    /// 요소 이름: <c>card-fx</c>·<c>card-fx-holder</c>·<c>card-fx-title</c>·<c>card-fx-card</c>·<c>card-fx-cell-&lt;칸&gt;</c>·<c>card-fx-lock-&lt;칸&gt;</c>·<c>card-fx-icon</c>·<c>card-fx-tags</c>·<c>card-fx-mini</c>·<c>card-fx-mini-name</c> · 남 좌석의 얼굴 상자 <c>card-fx-owner</c>·<c>card-fx-mini-owner</c>.
    /// </summary>
    public sealed class CardFx
    {
        /// <summary>능력 칸에 효과 문구까지 싣는 글자 수(넘으면 능력 이름만 — 1080 벌의 칸 크기, 전체 문구는 트레이너 카드 판 S40) [가정 #162 ②].</summary>
        public const int BodyMax = 48;
        /// <summary>720 벌 px: 등급 아이콘이 날아오는 거리(가로 · 세로) · 자물쇠 조각이 떨어지는 거리.</summary>
        public const float IconFromX = -160f, IconFromY = 120f, FallPx = 60f, ShardSpreadPx = 24f;

        static readonly string[] TierNames = { "", "bronze", "silver", "gold" };

        readonly UiStage stage;
        readonly GameFlow flow;
        readonly VisualElement scrim, holder, box, card, icon, sheen, edge, mini, miniCard, miniGlow, ownerFace, miniFace;
        readonly VisualElement[] shards = new VisualElement[2];
        readonly Label title, miniName;
        readonly VisualElement tags;
        readonly VisualElement[] cells = new VisualElement[2], lits = new VisualElement[2], locks = new VisualElement[2];
        string faceKey, tagsKey, miniKey;
        int ownerSeat = -2;

        public readonly VisualElement Element;

        /// <summary>지금 보이는 것(확인용): 해금 덮개 · 카드 바꾸기 덮개 · 보이는 등급(1 ~ 3, 없으면 0) · 깨진 자물쇠 수 · 열린 칸이 빛나나 · 작은 카드(능력 사용).</summary>
        public bool UnlockShown { get; private set; }
        public bool SwapShown { get; private set; }
        public int ShownTier { get; private set; }
        public int Broken { get; private set; }
        public bool LitShown { get; private set; }
        public bool AbilityShown { get; private set; }

        /// <summary>지금 그리는 덮개의 남 좌석(제목 앞 얼굴 상자 · 「○○의 트레이너 카드」 — 결정 120 · #707). 내 것이거나 보이지 않으면 −1.</summary>
        public int OwnerShown { get; private set; } = -1;

        public CardFx(UiStage stage, GameFlow flow)
        {
            this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Element = Box(null, "card-fx", "sn-card-fx");
            scrim = Box(Element, null, "sn-card-fx-scrim");
            holder = Box(Element, "card-fx-holder", "sn-card-fx-holder");
            var head = Box(holder, "card-fx-owner-line", "sn-owner");   // 남 좌석이면 그 좌석의 얼굴 상자 + 「○○의 트레이너 카드」(결정 120 · #707)
            head.AddToClassList("sn-card-fx-ownerline");
            ownerFace = FaceBox.Tag();
            ownerFace.name = "card-fx-owner";
            head.Add(ownerFace);
            title = Text(head, "", Kit.FTitle, "card-fx-title", "sn-card-fx-title");
            title.AddToClassList("kit-title");
            box = Box(holder, null, "sn-card-fx-box");
            card = Box(box, "card-fx-card", "sn-card-fx-card");
            for (int i = 0; i < shards.Length; i++)
            {
                shards[i] = Pic(box, null, "Cards/Trainer/locked_blue.png", "sn-card-fx-shard");
                shards[i].AddToClassList(i == 0 ? "sn-card-fx-shard-a" : "sn-card-fx-shard-b");
                ((Image)shards[i]).uv = i == 0 ? new Rect(0f, 0f, 0.35f, 0.3f) : new Rect(0.65f, 0.7f, 0.35f, 0.3f);   // 자물쇠 그림의 왼쪽 아래(자물쇠 삼각) · 오른쪽 위 모서리
            }
            icon = Pic(box, "card-fx-icon", "Cards/Trainer/icon_moderate.png", "sn-card-fx-icon");
            tags = Box(holder, "card-fx-tags", "sn-card-fx-tags");
            mini = Box(Element, "card-fx-mini", "sn-card-fx-mini");
            miniFace = FaceBox.Tag();
            miniFace.name = "card-fx-mini-owner";
            mini.Add(miniFace);
            miniCard = Box(mini, null, "sn-card-fx-minicard");
            miniName = Kit.Badge("", KitBadgeKind.Plain, Kit.F2);
            miniName.name = "card-fx-mini-name";
            miniName.pickingMode = PickingMode.Ignore;
            miniName.AddToClassList("sn-card-fx-mininame");
            mini.Add(miniName);
            sheen = Box(null, null, "sn-card-fx-sheen");
            edge = Box(null, null, "sn-card-fx-edge");
            miniGlow = Box(null, null, "sn-card-fx-glow");
            Hide();
        }

        static VisualElement Box(VisualElement parent, string name, string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            if (name != null) e.name = name;
            if (cls != null) e.AddToClassList(cls);
            parent?.Add(e);
            return e;
        }

        static Label Text(VisualElement parent, string text, string font, string name, string cls)
        {
            var l = Kit.Text(text, font);
            l.pickingMode = PickingMode.Ignore;
            if (name != null) l.name = name;
            if (cls != null) l.AddToClassList(cls);
            parent?.Add(l);
            return l;
        }

        static Image Pic(VisualElement parent, string name, string key, string cls)
        {
            var img = new Image { image = GenAssets.Load<Texture2D>(key), scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            if (name != null) img.name = name;
            img.AddToClassList(cls);
            parent?.Add(img);
            return img;
        }

        float Unit => stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;

        TrainerCardDef Def(int key) => key >= 0 && key < flow.Ctx.Cards.TrainerCards.Count ? flow.Ctx.Cards.TrainerCards[key] : null;

        string AbilityName(int key, int slot)
        {
            var def = Def(key);
            return def != null ? stage.T("trainer_ability." + def.Key + "." + slot.ToString(CultureInfo.InvariantCulture)) : "";
        }

        void Hide()
        {
            FxUi.Fade(scrim, 0f);
            FxUi.Fade(holder, 0f);
            FxUi.Fade(mini, 0f);
            Pickable(false);
            UnlockShown = SwapShown = LitShown = AbilityShown = false;
            ShownTier = Broken = 0;
            OwnerShown = -1;
        }

        // 덮개의 주인(−1 = 내 것): 남 좌석이면 제목 · 작은 카드 앞에 그 좌석의 얼굴 상자, 내 것이면 숨긴다(좌석이 바뀔 때만 다시 칠한다)
        void Owner(int seat)
        {
            OwnerShown = seat;
            if (ownerSeat == seat) return;
            ownerSeat = seat;
            foreach (var face in new[] { ownerFace, miniFace })
            {
                face.style.display = seat >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (seat >= 0) FaceBox.Paint(face, seat, flow.SeatColor(seat), flow.Protagonist(seat));
            }
        }

        // 제목 글: 남 좌석이면 「○○의 …」 꼴(key + ".seat" — 문자열 표에 짝으로 있다)
        string Titled(string mine, string seat, int owner) => owner < 0 ? stage.T(mine) : stage.T(seat).Replace("{name}", flow.SeatName(owner));

        // 큰 카드의 능력 칸은 카드가 보일 때만 마우스를 받는다(짧은 문구의 마우스 오버 — 결정 87 V11 라). 숨긴 카드(opacity 0)의 칸이 화면 가운데의 클릭을 삼켰다
        // (MouseProbe `ending-click-order` — 배신자 순서 칩 · [확정]이 눌리지 않았다)
        void Pickable(bool on)
        {
            foreach (var cell in cells)
                if (cell != null && cell.childCount > 0) cell.pickingMode = on ? PickingMode.Position : PickingMode.Ignore;
        }

        /// <summary>
        /// 그 순간을 그린다(대기열이 없거나 이 덮개의 단계가 아니면 모두 숨긴다). 남 좌석의 해금·바꾸기·능력도 내 것과 같게 그린다 — 내 창 · 메뉴가 떠 있지 않을 때만(결정 120 ① — <see cref="GameFlow.ShowsOthers"/>,
        /// 창이 떠 있으면 좌석 칩 꼬리표만).
        /// </summary>
        public void Show(FxSequencer seq)
        {
            Hide();
            int me = flow.Client != null ? flow.Client.Seat : -1;
            bool others = flow.ShowsOthers;
            var step = CardRows.Playing(seq, UnlockBeats.Row, out int ms);
            var unlock = CardRows.First<CardUnlocked>(step);
            if (unlock != null && (unlock.Seat == me || others))
            {
                ShowUnlock(unlock, ms, step.Ms, unlock.Seat == me ? -1 : unlock.Seat);
                return;
            }
            step = CardRows.Playing(seq, SwapBeats.Row, out ms);
            var swap = CardRows.First<TrainerCardSwapped>(step);
            if (swap != null && (swap.Seat == me || others))
            {
                ShowSwap(swap, ms, step.Ms, swap.Seat == me ? -1 : swap.Seat);
                return;
            }
            step = CardRows.Playing(seq, AbilityBeats.Row, out ms);
            var used = CardRows.First<AbilityUsed>(step);
            if (used != null && (used.Seat == me || others)) ShowAbility(used, ms, step.Ms, used.Seat == me ? -1 : used.Seat);
        }

        void ShowUnlock(CardUnlocked e, int ms, int total, int owner)
        {
            int breaks = UnlockBeats.Breaks(e);
            var pose = UnlockBeats.At(ms, total, breaks);
            if (pose.Show <= 0f) return;
            UnlockShown = true;
            Owner(owner);
            int before = Mathf.Clamp(e.TierBefore, 1, 3), after = Mathf.Clamp(e.TierAfter, 1, 3);
            int tier = Mathf.Clamp(before + pose.Face, before, after);
            Paint(e.CardKey, tier, true);
            Pickable(true);
            ShownTier = tier;
            Broken = pose.Broken;
            FxUi.Fade(scrim, pose.Show);
            FxUi.Fade(holder, pose.Show);
            FxUi.Scale(card, pose.Flip, 1f);
            float unit = Unit;
            // 자물쇠: 지금 등급에서 잠긴 칸만 — 다음에 깨질 자물쇠가 흔들리고, 깨진 것은 바로 사라진다(면은 뒤집기 한가운데에 바뀐다)
            int next = pose.Broken < breaks && e.AbilitySlots != null && pose.Broken < e.AbilitySlots.Length ? e.AbilitySlots[pose.Broken] : -1;
            for (int s = 1; s <= 2; s++)
            {
                bool broken = false;
                if (e.AbilitySlots != null)
                    for (int k = 0; k < Math.Min(pose.Broken, e.AbilitySlots.Length); k++)
                        if (e.AbilitySlots[k] == s) broken = true;
                bool locked = !TrainerScreen.Unlocked(tier, s) && !broken;
                locks[s - 1].style.display = locked ? DisplayStyle.Flex : DisplayStyle.None;
                FxUi.Move(locks[s - 1], s == next ? pose.Shake * unit : 0f, 0f);
                cells[s - 1].EnableInClassList("sn-card-fx-cell-locked", locked);
            }
            // 깨진 자물쇠 조각: 마지막으로 깨진 칸에서 양쪽으로 갈라져 떨어진다
            int lastSlot = pose.Broken > 0 && e.AbilitySlots != null && pose.Broken - 1 < e.AbilitySlots.Length ? e.AbilitySlots[pose.Broken - 1] : 1;
            for (int i = 0; i < shards.Length; i++)
            {
                shards[i].EnableInClassList("sn-card-fx-at-1", lastSlot == 1);
                shards[i].EnableInClassList("sn-card-fx-at-2", lastSlot == 2);
                ((Image)shards[i]).image = GenAssets.Load<Texture2D>(lastSlot == 2 ? "Cards/Trainer/locked_red.png" : "Cards/Trainer/locked_blue.png");
                bool falling = pose.Fall >= 0f && pose.Fall < 1f;
                FxUi.Fade(shards[i], falling ? 1f - pose.Fall : 0f);
                FxUi.Move(shards[i], falling ? (i == 0 ? -1f : 1f) * ShardSpreadPx * pose.Fall * unit : 0f, falling ? FallPx * pose.Fall * unit : 0f);
            }
            // 원인 포켓몬의 등급 아이콘: 왼쪽 아래에서 날아와 아래 띠에 박힌다
            ((Image)icon).image = GenAssets.Load<Texture2D>(e.CauseGrade == EncounterTier.Strong ? "Cards/Trainer/icon_strong.png" : "Cards/Trainer/icon_moderate.png");
            FxUi.Fade(icon, pose.Icon < 0f ? 0f : Mathf.Clamp01(pose.Icon * 3f));
            float away = pose.Icon < 0f ? 1f : 1f - pose.Icon;
            FxUi.Move(icon, IconFromX * away * unit, IconFromY * away * unit);
            FxUi.Scale(icon, pose.IconScale, pose.IconScale);
            Sheen(pose.Sheen);
            LitShown = pose.Lit;
            FxUi.Fade(edge, pose.Lit ? 1f : pose.Sheen >= 0f ? pose.Sheen : 0f);
            edge.EnableInClassList("sn-card-fx-edge-gold", after >= 3);
            var opened = new List<int>();
            if (e.AbilitySlots != null) opened.AddRange(e.AbilitySlots);
            for (int s = 1; s <= 2; s++) FxUi.Fade(lits[s - 1], pose.Lit && opened.Contains(s) ? 1f : 0f);
            string tierName = stage.T("screen.trainer.tier." + after.ToString(CultureInfo.InvariantCulture));
            title.text = pose.Lit ? Titled("screen.card.fx.tier", "screen.card.fx.tier.seat", owner).Replace("{tier}", tierName) : Titled("screen.card.fx.title", "screen.card.fx.title.seat", owner);
            title.EnableInClassList("sn-card-fx-title-gold", pose.Lit && after >= 3);
            title.EnableInClassList("sn-card-fx-title-silver", pose.Lit && after == 2);
            // 꼬리표: 아이콘이 박힌 뒤 원인 등급 → 착지에 「실버 ★★☆」 + 열린 능력마다 「「능력」 해금」
            if (pose.Lit)
            {
                var names = new List<string>();
                foreach (int s in opened) names.Add(AbilityName(e.CardKey, s));
                Tags("lit:" + after + ":" + string.Join("|", names), after, names);
                FxUi.Fade(tags, pose.Tags);
            }
            else
            {
                Tags("cause:" + e.CauseGrade, 0, new List<string> { stage.T(e.CauseGrade == EncounterTier.Strong ? "screen.card.fx.cause.strong" : "screen.card.fx.cause.moderate") });
                FxUi.Fade(tags, pose.Icon >= 1f ? 1f : 0f);
            }
        }

        void ShowSwap(TrainerCardSwapped e, int ms, int total, int owner)
        {
            var pose = SwapBeats.At(ms, total);
            if (pose.Show <= 0f) return;
            SwapShown = true;
            Owner(owner);
            int key = pose.Face > 0 ? e.ToCard : e.FromCard;
            Paint(key, TrainerRules.Gold, true);
            Pickable(true);
            ShownTier = TrainerRules.Gold;
            FxUi.Fade(scrim, pose.Show);
            FxUi.Fade(holder, pose.Show);
            FxUi.Scale(card, pose.Flip, 1f);
            for (int s = 1; s <= 2; s++)
            {
                locks[s - 1].style.display = DisplayStyle.None;
                cells[s - 1].RemoveFromClassList("sn-card-fx-cell-locked");
                FxUi.Fade(lits[s - 1], pose.Lit ? 1f : 0f);
            }
            foreach (var s in shards) FxUi.Fade(s, 0f);
            FxUi.Fade(icon, 0f);
            Sheen(pose.Sheen);
            LitShown = pose.Lit;
            FxUi.Fade(edge, pose.Lit ? 1f : pose.Sheen >= 0f ? pose.Sheen : 0f);
            edge.EnableInClassList("sn-card-fx-edge-gold", true);
            title.text = pose.Lit ? Titled("screen.card.fx.swap", "screen.card.fx.swap.seat", owner) : Titled("screen.card.fx.title", "screen.card.fx.title.seat", owner);
            title.EnableInClassList("sn-card-fx-title-gold", pose.Lit);
            title.RemoveFromClassList("sn-card-fx-title-silver");
            var def = Def(e.ToCard);
            Tags("swap:" + e.ToCard, TrainerRules.Gold, new List<string> { def != null ? stage.T("trainer_card." + def.Key) : "" });
            FxUi.Fade(tags, pose.Tags);
        }

        void ShowAbility(AbilityUsed e, int ms, int total, int owner)
        {
            var pose = AbilityBeats.At(ms, total);
            if (pose.Show <= 0f) return;
            AbilityShown = true;
            Owner(owner);
            var view = flow.Client.View;
            int tier = e.Seat >= 0 && e.Seat < view.Seats.Length ? Mathf.Clamp(view.Seats[e.Seat].TrainerCardTier, 1, 3) : TrainerRules.Gold;
            int slot = e.Ability == AbilityColor.Red ? 2 : 1;
            string key = e.CardKey + ":" + tier + ":" + slot + ":" + owner;
            if (miniKey != key)
            {
                miniKey = key;
                miniCard.Clear();
                Face(miniCard, e.CardKey, tier, false, null, null, null);
                miniGlow.RemoveFromHierarchy();
                miniGlow.EnableInClassList("sn-card-fx-glow-1", slot == 1);
                miniGlow.EnableInClassList("sn-card-fx-glow-2", slot == 2);
                miniGlow.EnableInClassList("sn-card-fx-cell-1", slot == 1);
                miniGlow.EnableInClassList("sn-card-fx-cell-2", slot == 2);
                miniCard.Add(miniGlow);
                string ability = AbilityName(e.CardKey, slot);
                miniName.text = owner < 0 ? ability : stage.T("screen.card.fx.ability.seat").Replace("{name}", flow.SeatName(owner)).Replace("{ability}", ability);
            }
            UnderLog();
            FxUi.Fade(mini, pose.Show);
            FxUi.Move(mini, 0f, -pose.Rise * Unit);
            FxUi.Fade(miniGlow, pose.Glow);
        }

        // 작은 카드는 왼쪽 위 기록 판(줄이 쌓이면 자란다) 바로 아래 — 판 아래 끝을 읽어 그 밑에 둔다(틈은 USS margin). 판이 숨었거나(배틀 화면) 배치 전이면 USS 자리(배틀 정보 기둥 위)
        void UnderLog()
        {
            var log = stage.Root?.Q("board-hud-log");
            var r = log != null && log.style.display.value != DisplayStyle.None ? log.worldBound : Rect.zero;
            if (float.IsNaN(r.yMax) || r.height <= 0f)
            {
                mini.style.top = StyleKeyword.Null;
                return;
            }
            mini.style.top = Element.WorldToLocal(new Vector2(0f, r.yMax)).y;
        }

        // 금속광: 카드 위를 왼쪽에서 오른쪽으로 지나는 흰 띠(카드 폭은 배치에서 읽는다 — 배치 전이면 그리지 않는다)
        void Sheen(float at)
        {
            float w = card.resolvedStyle.width;
            if (at < 0f || float.IsNaN(w) || w <= 0f)
            {
                FxUi.Fade(sheen, 0f);
                return;
            }
            FxUi.Fade(sheen, 1f);
            FxUi.Move(sheen, Mathf.Lerp(-0.15f, 1.05f, at) * w, 0f);
        }

        // 큰 카드의 앞면을 (카드, 등급)이 바뀔 때만 다시 만든다
        void Paint(int cardKey, int tier, bool text)
        {
            string key = cardKey + ":" + tier + ":" + text;
            if (faceKey == key) return;
            faceKey = key;
            card.Clear();
            Face(card, cardKey, tier, text, cells, lits, locks);
            card.Add(sheen);
            card.Add(edge);
            edge.EnableInClassList("sn-card-fx-edge-gold", tier >= 3);
        }

        // 원작 카드 틀 한 장: 바탕 · 직업 그림 · 아래 띠 · 이름 · 별 · 능력 칸 둘(이름 + 짧으면 문구) · 칸의 금 테두리 · 자물쇠
        internal void Face(VisualElement parent, int cardKey, int tier, bool text, VisualElement[] cellOut, VisualElement[] litOut, VisualElement[] lockOut)
        {
            var inv = CultureInfo.InvariantCulture;
            var def = Def(cardKey);
            tier = Mathf.Clamp(tier, 1, 3);
            Pic(parent, null, "Cards/Trainer/" + TierNames[tier] + "_base.png", "sn-card-fx-fill");
            string front = def != null ? BoardPieces.FrontKey(def.Key) : null;
            if (front != null) Pic(parent, null, front, "sn-card-fx-art").scaleMode = ScaleMode.ScaleToFit;
            Pic(parent, null, "Cards/Trainer/" + TierNames[tier] + "_bar.png", "sn-card-fx-bar");
            if (text)
            {
                var head = Box(parent, null, "sn-card-fx-head");
                Text(head, def != null ? stage.T("trainer_card." + def.Key) : "", Kit.F2, null, "sn-card-fx-name");
                var foot = Box(parent, null, "sn-card-fx-foot");
                Text(foot, TrainerScreen.Stars(tier), Kit.F2, "card-fx-stars", "sn-card-fx-stars");
            }
            for (int s = 1; s <= 2; s++)
            {
                var cell = Box(parent, cellOut != null ? "card-fx-cell-" + s.ToString(inv) : null, "sn-card-fx-cell");
                cell.AddToClassList("sn-card-fx-cell-" + s.ToString(inv));
                if (text && def != null)
                {
                    string k = "trainer_ability." + def.Key + "." + s.ToString(inv);
                    Text(cell, stage.T(k), Kit.F2, null, "sn-card-fx-ability");
                    string body = stage.T(k + ".short");   // 짧은 문구(48자 이하 — 결정 87 V11 라), 전체 문구는 마우스 오버
                    cell.pickingMode = PickingMode.Position;
                    stage.Tips.Hook(cell, stage.T(k), stage.T(k + ".text"), TipSide.Below, reading: true);
                    if (body.Length <= BodyMax)
                    {
                        var l = Kit.Wrapped(body, Kit.F2);
                        l.pickingMode = PickingMode.Ignore;
                        l.AddToClassList("sn-card-fx-body");
                        cell.Add(l);
                    }
                }
                bool locked = !TrainerScreen.Unlocked(tier, s);
                cell.EnableInClassList("sn-card-fx-cell-locked", locked && text);
                if (cellOut != null) cellOut[s - 1] = cell;
                var lit = Box(cell, null, "sn-card-fx-lit");
                if (litOut != null) litOut[s - 1] = lit;
                var lockImg = Pic(parent, lockOut != null ? "card-fx-lock-" + s.ToString(inv) : null, s == 1 ? "Cards/Trainer/locked_blue.png" : "Cards/Trainer/locked_red.png", "sn-card-fx-lock");
                lockImg.AddToClassList("sn-card-fx-lock-" + s.ToString(inv));
                lockImg.style.display = locked ? DisplayStyle.Flex : DisplayStyle.None;
                if (lockOut != null) lockOut[s - 1] = lockImg;
            }
        }

        // 카드 아래 꼬리표 줄: tier > 0이면 첫 꼬리표가 등급 배지(「실버 ★★☆」), 나머지는 글(능력 해금 · 새 카드 이름 · 원인 등급)
        void Tags(string key, int tier, List<string> texts)
        {
            if (tagsKey == key) return;
            tagsKey = key;
            tags.Clear();
            if (tier > 0)
                Tag(stage.T("screen.trainer.tier." + tier.ToString(CultureInfo.InvariantCulture)) + " " + TrainerScreen.Stars(tier), tier >= 3 ? KitBadgeKind.Gold : KitBadgeKind.Silver);
            foreach (var t in texts)
                Tag(key.StartsWith("lit:", StringComparison.Ordinal) ? stage.T("screen.card.fx.unlocked").Replace("{ability}", t) : t, key.StartsWith("cause:", StringComparison.Ordinal) ? KitBadgeKind.Plain : KitBadgeKind.Ok);
        }

        void Tag(string text, KitBadgeKind kind)
        {
            var b = Kit.Badge(text, kind, Kit.F2);
            b.pickingMode = PickingMode.Ignore;
            b.AddToClassList("sn-card-fx-tag");
            tags.Add(b);
        }
    }
}
