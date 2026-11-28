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
    /// <summary>카드 밑에 끼우는 띠 하나(결정 206): 배운 기술 · 균열 카드 · 전설 전술 판(알파 스트라이크 + 비스트 부스트 / 비스트 부스트만) · 갤럭시단 전술 판(플랜 B + 은하의 응징) ·
    /// 홈(정비 판에서 기술 카드를 끄는 동안 받을 수 있는 빈 칸 — 기술 띠와 같은 크기 · 자리, 그림 없음 · 그림자 없음, 결정 206 ③).</summary>
    public enum CardSlotKind { Move, Breach, Legend, LegendBeast, Galactic, Hole }

    public struct CardSlot
    {
        public CardSlotKind Kind;
        /// <summary>배운 기술(기술 표 색인) · 균열 카드(덱 카드 색인) — 그 밖은 −1.</summary>
        public int Move, Breach;
    }

    /// <summary>
    /// 포켓몬 카드 한 장에 그릴 값(결정 206 — <see cref="CardStack"/>): 카드(포켓몬 표 색인) · 지금 체력 / 최대 · 지금 스피드 · 상태 · 지닌물건(우두머리 토큰 먼저) · 끼운 띠(빈 칸은 없다) · 은하의 응징.
    /// 화면은 <see cref="Of"/>(판의 포켓몬 — 배틀 화면은 그리는 체력 · 스피드를 넘긴다)나 <see cref="Wild"/>(야생 카드 — 앞면만)로 만든다.
    /// </summary>
    public sealed class CardMon
    {
        /// <summary><see cref="Items"/>의 우두머리 토큰(옮길 수 없다 — 동그라미에 먼저).</summary>
        public const int AlphaToken = -2;

        public int Def = -1;
        /// <summary>판의 포켓몬(null = 야생 카드 — 앞면만, 피해 · 상태 · 띠 없음).</summary>
        public PokemonState Pk;
        public int Hp, MaxHp, Speed;
        public readonly List<StatusEffect> Status = new List<StatusEffect>(2);
        /// <summary>지닌물건(아이템 표 색인 · <see cref="AlphaToken"/>).</summary>
        public readonly List<int> Items = new List<int>(3);
        public readonly List<CardSlot> Slots = new List<CardSlot>(4);
        /// <summary>그 쪽 은하의 응징에 쌓인 피해(0이면 주사위 없음 — 결정 206 4차 [가정 r4-①]).</summary>
        public int Retribution;

        public bool Fainted => Pk != null && Hp <= 0;

        /// <summary>
        /// 판의 포켓몬 하나: 체력 · 최대 체력(우두머리 · 트레이너 카드 포함 — <see cref="PokemonRules.MaxHp"/>) · 스피드(카드 값 + 마비 · 지닌물건 · 균열 · 트레이너 카드 — <see cref="BattleRules.PriorityMods"/>) ·
        /// 띠(기술 칸 순서 — 배운 기술 · 균열 카드 · 남은 전설 특수기술, 갤럭시단 포켓몬은 끝에 갤럭시단 전술 판 — 플랜 B는 배운 기술과 상관없이 늘 쓴다 rulings G13) · 응징(배틀 중 그 쪽 값).
        /// hp · speed · retribution을 주면 그 값(배틀 화면이 그리는 지금 값). hole(칸)이 참인 빈 칸(기술 · 균열 · 전술 없음)에는 그 자리에 홈 띠를 끼운다(정비 판 — 결정 206 ③).
        /// </summary>
        public static CardMon Of(Ctx ctx, GameState s, int pokemon, int? hp = null, int? speed = null, int? retribution = null, Func<int, bool> hole = null)
        {
            var pk = s.Pokemon[pokemon];
            var def = ctx.Cards.Pokemon[pk.Def];
            var m = new CardMon { Def = pk.Def, Pk = pk, MaxHp = PokemonRules.MaxHp(ctx, pk) };
            m.Hp = hp ?? PokemonRules.Hp(ctx, pk);
            if (speed.HasValue) m.Speed = speed.Value;
            else
            {
                var mods = new List<Mod>();
                BattleRules.PriorityMods(s, ctx, pokemon, mods);
                m.Speed = def.Initiative;
                foreach (var mod in mods) m.Speed += mod.Delta;
            }
            Fill(m, pk, hole);
            if (retribution.HasValue) m.Retribution = retribution.Value;
            else if (s.Battle != null)
                foreach (var side in s.Battle.Sides)
                    if (side.Active == pokemon || side.Roster.Contains(pokemon)) m.Retribution = side.Retribution;
            return m;
        }

        /// <summary>
        /// 판의 포켓몬 하나를 화면이 그리는 지금 체력 · 스피드로(마우스 오버 카드 — 판 상태를 쥐지 않는 화면, 결정 206 ①). 띠 · 토큰 · 상태는 <see cref="Of(Ctx, GameState, int, int?, int?, int?)"/>와 같다 —
        /// 응징은 넘긴 값(모르면 0 = 주사위 없음).
        /// </summary>
        public static CardMon Of(Ctx ctx, PokemonState pk, int hp, int speed, int retribution = 0)
        {
            var m = new CardMon { Def = pk.Def, Pk = pk, MaxHp = PokemonRules.MaxHp(ctx, pk), Hp = hp, Speed = speed, Retribution = retribution };
            Fill(m, pk);
            return m;
        }

        // 상태 · 지닌물건(우두머리 토큰 먼저) · 끼운 띠
        static void Fill(CardMon m, PokemonState pk, Func<int, bool> hole = null)
        {
            StatusRules.Statuses(pk, m.Status);
            if (pk.Alpha) m.Items.Add(AlphaToken);
            if (pk.HeldItem >= 0) m.Items.Add(pk.HeldItem);
            if (pk.HeldItem2 >= 0) m.Items.Add(pk.HeldItem2);
            for (int k = 0; k < pk.Moves.Length; k++)
            {
                if (pk.Moves[k] >= 0) m.Slots.Add(new CardSlot { Kind = CardSlotKind.Move, Move = pk.Moves[k], Breach = -1 });
                else if (k < pk.Breach.Length && pk.Breach[k] >= 0) m.Slots.Add(new CardSlot { Kind = CardSlotKind.Breach, Move = -1, Breach = pk.Breach[k] });
                else
                {
                    var t = LegendRules.TacticAt(pk, k);
                    bool board = m.Slots.Count > 0 && m.Slots[m.Slots.Count - 1].Kind == CardSlotKind.Legend;
                    // 알파 스트라이크가 남았으면 비스트 부스트도 남았다(배운 기술 0) — 원작 전술 판 한 장(두 상자)
                    if (t == LegendTactic.AlphaStrike) m.Slots.Add(new CardSlot { Kind = CardSlotKind.Legend, Move = -1, Breach = -1 });
                    else if (t == LegendTactic.BeastBoost && !board) m.Slots.Add(new CardSlot { Kind = CardSlotKind.LegendBeast, Move = -1, Breach = -1 });
                    else if (t == LegendTactic.None && hole != null && hole(k)) m.Slots.Add(new CardSlot { Kind = CardSlotKind.Hole, Move = -1, Breach = -1 });
                }
            }
            if (pk.Owner == SeatId.Galactic) m.Slots.Add(new CardSlot { Kind = CardSlotKind.Galactic, Move = -1, Breach = -1 });
        }

        /// <summary>야생 카드(카드 앞면 그대로 — 체력 · 스피드는 카드 값).</summary>
        public static CardMon Wild(Ctx ctx, int def)
        {
            var d = ctx.Cards.Pokemon[def];
            return new CardMon { Def = def, Hp = d.Health, MaxHp = d.Health, Speed = d.Initiative };
        }
    }

    /// <summary>
    /// 카드 짜임(결정 206): Stack = 원작 세로 쌓기(띠를 카드 밑에) · Spread = 옆으로 펼치기(목업 B — 카드 오른쪽 기둥에 띠 + 지닌물건 글 상자, 마우스 오버 카드 · 정비 카드 확대).
    /// </summary>
    public enum CardLayout { Stack, Spread }

    /// <summary>카드 쌓기의 마우스 오버 자리(쓰는 화면이 <see cref="CardStack.HookTips"/>로 툴팁을 걸거나 직접 고른다).</summary>
    public enum CardPartKind { Signature, Move, Breach, AlphaStrike, BeastBoost, PlanB, Retribution, Item, Status, Hole }

    public sealed class CardPart
    {
        public CardPartKind Kind;
        public VisualElement Element;
        public string Title, Text;
    }

    /// <summary>
    /// 포켓몬 카드 쌓기(결정 206 1단계 — 목업 10차 <c>Logs/Review/card/</c>): 원작 생성기로 구운 카드 앞면(<see cref="GenAssets.CardFaces"/> — 폼은 그 상태의 앞면, 영어 화면이면 en 카드) 밑에
    /// 끼운 띠(<see cref="CardMon.Slots"/> — 기술 카드 = 포켓몬 카드 뒷면의 홈~아래 끝 · 균열 카드 효과 띠 · 아레나 전술 판, 빈 칸은 그리지 않는다)를 겹쳐 쌓고,
    /// 카드 위에 지닌물건 · 우두머리 토큰(그림 판 왼쪽 아래 동그라미, 둘째는 반 겹침) · 상태 주사위 · ♥ · ≫ 상자 덧칠(지금/최대 + ±증감 꼬리표 — 원작 상자 · 숫자 글꼴 그림) ·
    /// 응징 주사위 · 기절(회색 + 배지)을 얹는다. 크기는 부모가 준 폭(<c>width</c> = 카드 16 cm)으로 — 모든 자리는 cm × 폭/16.
    /// 겹침(목업 9 · 10차): 위 블럭(카드 · 앞 띠)의 아래 끝 = 뒤 띠 뒷면의 18.75 cm(끼우는 홈 바로 위), 숨는 몫 <see cref="HideCm"/>, 위 블럭마다 실루엣을 따라가는 짙은 테 + 그림자(USS 색 · 코드 크기).
    /// 카드 그림이 없으면(짝 없는 카드) 지금 도트 카드(<see cref="MonTip"/> 짜임)로 대신한다(<see cref="Art"/> 거짓).
    /// 마우스를 받지 않는다 — 쓰는 화면이 <see cref="HookTips"/>(기술 · 균열 · 전술 · 지닌물건 · 상태 효과 글)를 부른다.
    /// 요소 이름: <c>card-stack</c> · <c>card-face</c> · <c>card-strip-n</c> · <c>card-item-n</c> · <c>card-status-n</c> · <c>card-hp</c>(·<c>-tag</c>) · <c>card-speed</c>(·<c>-tag</c>) · <c>card-retribution</c> · <c>card-fainted</c> · <c>card-emblem</c> ·
    /// <c>card-alpha-type-n</c> — 숫자 칸의 <c>userData</c>는 그린 글(「10/12」 · 「+4」).
    /// 옆으로 펼치기(<see cref="CardLayout.Spread"/> — 결정 206 ① 마우스 오버 카드 · 정비 카드 확대, 목업 B): 띠를 카드 오른쪽 기둥 <c>card-col</c>(<see cref="Column"/>)에 같은 겹침으로 쌓고(첫 띠는 위를 둥글게)
    /// 그 밑에 지닌물건 글 상자 <c>card-note-n</c>(토큰 그림 · 이름 · 효과 — 마우스를 받지 않는 카드도 다 읽힌다)를 둔다.
    /// </summary>
    public sealed class CardStack
    {
        public const float WidthCm = 16f, HeightCm = 28f;
        /// <summary>구운 그림의 px/cm(앞면 432 = 16 cm — Tools/assets/lib/card-faces.mjs PX_CM).</summary>
        public const float PxPerCm = 27f;
        /// <summary>띠 그림 맨 위에서 위 블럭 아래 끝까지(숨는 몫 — card-faces.mjs STRIP_HIDE_CM = 18.75 − (28 − 284/27)).</summary>
        public const float HideCm = 18.75f - (28f - 284f / 27f);

        // 전술 판 띠 위의 자리(띠 그림 px — 굽기 보고 anchors, card-faces.mjs tacticLayout): 상자 윗변 · 알파 스트라이크 타입 그림 줄 · 전설 표지
        const float LegendBeastTop = 295.36f, LegendIconsY = 148.44f;
        const float GalacticRetriTop = 303.86f;
        const float EmblemX = 187.78f, EmblemY = 31.31f, EmblemSize = 51.49f;
        // 숫자 글자 그림 중 숫자 키의 몫(La Oriental · Barlow 글자 그림 높이 대비 — 목업 3차 · 5차 측정)
        const float NumCap = 0.611f, TagCap = 0.6f, DieCap = 0.534f;

        readonly UiStage stage;
        readonly Ctx ctx;
        readonly CardMon mon;
        readonly float u;
        readonly string lang;
        readonly List<CardPart> parts = new List<CardPart>();
        readonly List<VisualElement> shaded = new List<VisualElement>();

        public VisualElement Root { get; }
        /// <summary>원작 카드 그림으로 그렸나(거짓 = 도트 카드로 대신).</summary>
        public bool Art { get; }
        /// <summary>앞면 그림 키(그린 것 — 대신했으면 그 키).</summary>
        public string FaceKey { get; }
        /// <summary>끼운 띠 수(<c>card-strip-n</c>).</summary>
        public int StripCount { get; private set; }
        /// <summary>쌓은 높이(px — 도트 카드면 NaN, 배치 뒤 Root.layout).</summary>
        public float Height { get; private set; } = float.NaN;
        public IReadOnlyList<CardPart> Parts => parts;
        /// <summary>옆으로 펼친 짜임(<see cref="CardLayout.Spread"/>)의 오른쪽 기둥(<c>card-col</c> — 띠 · 지닌물건 글 상자, 쓰는 화면이 줄을 더 붙인다). 쌓기 · 도트 카드면 null.</summary>
        public VisualElement Column { get; private set; }

        public static CardStack Build(UiStage stage, Ctx ctx, CardMon mon, float width, CardLayout layout = CardLayout.Stack) => new CardStack(stage, ctx, mon, width, layout);

        /// <summary>그 카드를 원작 카드 그림으로 그릴 수 있나(앞면 · 띠 · 숫자 글자가 모두 있다 — 거짓이면 <see cref="Build"/>가 도트 카드로 대신한다).</summary>
        public static bool CanDraw(UiStage stage, Ctx ctx, CardMon mon) => Textures(ctx, mon, stage.Lang == "en" ? "en" : "ko", out _, out _, out _);

        static bool Textures(Ctx ctx, CardMon mon, string lang, out string faceKey, out Texture2D face, out List<Texture2D> strips)
        {
            faceKey = GenAssets.CardFaces + lang + "/" + ctx.Cards.Pokemon[mon.Def].Key.Replace(':', '_') + ".png";
            strips = new List<Texture2D>();
            face = GenAssets.Load<Texture2D>(faceKey);
            if (face == null || GenAssets.Load<Texture2D>(Digit("num", '0')) == null) return false;
            foreach (var slot in mon.Slots)
            {
                if (slot.Kind == CardSlotKind.Hole) { strips.Add(null); continue; }   // 홈은 그림 없이 띠 크기만(결정 206 ③)
                var tex = GenAssets.Load<Texture2D>(StripKey(ctx, lang, slot));
                if (tex == null) return false;
                strips.Add(tex);
            }
            return true;
        }

        CardStack(UiStage stage, Ctx ctx, CardMon mon, float width, CardLayout layout)
        {
            this.stage = stage;
            this.ctx = ctx;
            this.mon = mon;
            u = width / WidthCm;
            lang = stage.Lang == "en" ? "en" : "ko";
            Root = new VisualElement { name = "card-stack", pickingMode = PickingMode.Ignore };
            Root.AddToClassList("sn-card-stack");
            Art = Textures(ctx, mon, lang, out string faceKey, out var face, out var strips);
            FaceKey = faceKey;
            if (mon.Fainted) Root.AddToClassList("sn-card-out");
            if (!Art) { Dot(width, layout == CardLayout.Stack); return; }
            if (layout == CardLayout.Spread) Spread(face, strips);
            else Stack(face, strips);
        }

        static string StripKey(Ctx ctx, string lang, CardSlot slot)
        {
            switch (slot.Kind)
            {
                case CardSlotKind.Move: return GenAssets.CardMoves + lang + "/" + ctx.Cards.Moves[slot.Move].Key + ".png";
                case CardSlotKind.Breach: return GenAssets.CardStrips + lang + "/breach_" + BreachName(ctx, slot.Breach) + ".png";
                case CardSlotKind.Legend: return GenAssets.CardStrips + lang + "/legend.png";
                case CardSlotKind.LegendBeast: return GenAssets.CardStrips + lang + "/legend_beast.png";
                default: return GenAssets.CardStrips + lang + "/galactic.png";
            }
        }

        static string BreachName(Ctx ctx, int card)
        {
            const string head = "distortion_breach.";
            string key = card >= 0 && card < ctx.Decks.CardIds.Count ? ctx.Decks.CardIds[card] : "";
            return key.StartsWith(head, StringComparison.Ordinal) ? key.Substring(head.Length) : key;
        }

        static string Digit(string set, char c) => GenAssets.CardDigits + set + "_" + (c == '/' ? "slash" : c == '+' ? "plus" : c == '−' ? "minus" : c.ToString()) + ".png";

        // ── 원작 카드 그림 ──

        void Stack(Texture2D faceTex, List<Texture2D> strips)
        {
            float w = Mathf.Round(WidthCm * u);
            var face = Block(faceTex, 0f, Mathf.Round(HeightCm * u));
            face.name = "card-face";
            var blocks = new List<VisualElement> { face };
            float bottom = Mathf.Round(HeightCm * u);
            for (int i = 0; i < mon.Slots.Count; i++)
            {
                float top = Mathf.Round(bottom - HideCm * u), h = Mathf.Round(strips[i] != null ? w * strips[i].height / strips[i].width : w * MoveStripPx / (WidthCm * PxPerCm));
                var strip = Block(strips[i], top, h);
                strip.name = "card-strip-" + (i + 1).ToString(CultureInfo.InvariantCulture);
                if (strips[i] == null) HoleLook(strip, h);
                StripParts(strip, mon.Slots[i], h);
                blocks.Add(strip);
                bottom = top + h;
            }
            StripCount = mon.Slots.Count;
            Height = bottom;
            Root.style.width = w;
            Root.style.height = bottom;
            // 뒤 띠부터 넣는다(앞 블럭이 위에 그려진다) · 아래에 무엇이 깔린 블럭마다 실루엣 그림자
            for (int i = blocks.Count - 1; i >= 0; i--) Root.Add(blocks[i]);
            for (int i = 0; i < blocks.Count - 1; i++)
                if (i == 0 || mon.Slots[i - 1].Kind != CardSlotKind.Hole) shaded.Add(blocks[i]);   // 홈은 그림자가 없다(목업 5차)
            if (shaded.Count > 0) Root.RegisterCallback<CustomStyleResolvedEvent>(Shade);
            Overlays(face);
            // 비스트 부스트만 남은 판: 두 상자 사이 표지가 앞 띠 아래 끝에 걸린다 — 표지만 맨 위에(목업 9차 보충)
            for (int i = 0; i < mon.Slots.Count; i++)
                if (mon.Slots[i].Kind == CardSlotKind.LegendBeast)
                {
                    float k = w / (WidthCm * PxPerCm), top = blocks[i + 1].style.top.value.value;
                    var emblem = Abs(Root, GenAssets.Load<Texture2D>(GenAssets.CardStrips + "legend_emblem.png"), EmblemX * k, top + EmblemY * k, EmblemSize * k, EmblemSize * k);
                    emblem.name = "card-emblem";
                    RoundMask(emblem);
                }
        }

        // 옆으로 펼치기(목업 B · 5차 「위에 아무것도 없는 첫 띠는 위를 둥글게」 · 10차 겹침): 왼쪽 카드 앞면, 오른쪽 기둥에 띠를 같은 겹침으로 쌓고 그 밑에 지닌물건 글 상자
        void Spread(Texture2D faceTex, List<Texture2D> strips)
        {
            float w = Mathf.Round(WidthCm * u);
            Root.style.flexDirection = FlexDirection.Row;
            Root.style.alignItems = Align.FlexStart;
            var face = Block(faceTex, 0f, Mathf.Round(HeightCm * u));
            face.name = "card-face";
            face.style.position = Position.Relative;
            face.style.flexShrink = 0;
            Root.Add(face);
            Overlays(face);
            Column = new VisualElement { name = "card-col", pickingMode = PickingMode.Ignore };
            Column.style.width = w;
            Column.style.marginLeft = Mathf.Round(0.4f * u);
            Column.style.flexShrink = 0;
            Root.Add(Column);
            if (strips.Count > 0)
            {
                var pile = new VisualElement { name = "card-pile", pickingMode = PickingMode.Ignore };
                pile.style.width = w;
                pile.style.flexShrink = 0;
                var blocks = new List<VisualElement>();
                // 첫 띠가 전술 판이면 그 머리(위 카드 밑으로 숨는 몫 — 몬스터볼 칸의 흰 반원)를 잘라 내고 기둥 맨 위를 카드 윗모서리처럼 둥글게(목업 1차 B · C, 검토 지적 13)
                var firstKind = mon.Slots.Count > 0 ? mon.Slots[0].Kind : CardSlotKind.Move;
                float skip = firstKind == CardSlotKind.Legend || firstKind == CardSlotKind.LegendBeast || firstKind == CardSlotKind.Galactic ? Mathf.Round(HideCm * u) : 0f;
                float bottom = 0f;
                for (int i = 0; i < mon.Slots.Count; i++)
                {
                    float top = i == 0 ? -skip : Mathf.Round(bottom - HideCm * u), h = Mathf.Round(w * strips[i].height / strips[i].width);
                    var strip = Block(strips[i], top, h);
                    strip.name = "card-strip-" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    StripParts(strip, mon.Slots[i], h);
                    if (i == 0 && skip <= 0f)
                    {
                        // 첫 띠는 위 카드 밑에 들어가지 않는다 — 잘린 윗변 대신 카드 윗모서리처럼 둥글게
                        float r = Mathf.Round(0.6f * u);
                        strip.style.borderTopLeftRadius = strip.style.borderTopRightRadius = r;
                        strip.style.overflow = Overflow.Hidden;
                    }
                    blocks.Add(strip);
                    bottom = top + h;
                }
                if (skip > 0f)
                {
                    float r = Mathf.Round(0.6f * u);
                    pile.style.borderTopLeftRadius = pile.style.borderTopRightRadius = r;
                    pile.style.overflow = Overflow.Hidden;
                }
                pile.style.height = bottom;
                for (int i = blocks.Count - 1; i >= 0; i--) pile.Add(blocks[i]);
                for (int i = 0; i < blocks.Count - 1; i++) shaded.Add(blocks[i]);
                for (int i = 0; i < mon.Slots.Count; i++)
                    if (mon.Slots[i].Kind == CardSlotKind.LegendBeast)
                    {
                        float k = w / (WidthCm * PxPerCm), top = blocks[i].style.top.value.value;
                        var emblem = Abs(pile, GenAssets.Load<Texture2D>(GenAssets.CardStrips + "legend_emblem.png"), EmblemX * k, top + EmblemY * k, EmblemSize * k, EmblemSize * k);
                        emblem.name = "card-emblem";
                        RoundMask(emblem);
                    }
                Column.Add(pile);
            }
            StripCount = mon.Slots.Count;
            Height = Mathf.Round(HeightCm * u);
            if (shaded.Count > 0) Root.RegisterCallback<CustomStyleResolvedEvent>(Shade);
            Notes(w);
        }

        /// <summary>
        /// 옆으로 펼친 짜임의 기둥이 자리보다 길 때(지닌물건 글이 길다 — 영어 · 1080 벌): 띠 밑의 상자들(지닌물건 글 · 쓰는 화면이 붙인 줄)을 그 오른쪽 새 기둥 <c>card-col-2</c>로 옮긴다.
        /// 카드를 줄이면 글 상자가 좁아져 오히려 길어지니 쓰는 화면이 줄이기 전에 먼저 부른다. 옮겼으면 참(한 번만).
        /// </summary>
        public bool SplitColumn()
        {
            if (Column == null || Column.parent != Root || Root.Q("card-col-2") != null) return false;
            var pile = Column.Q("card-pile");
            var rest = new List<VisualElement>();
            foreach (var child in Column.Children()) if (child != pile) rest.Add(child);
            if (pile == null || rest.Count == 0) return false;
            var extra = new VisualElement { name = "card-col-2", pickingMode = PickingMode.Ignore };
            extra.style.width = Column.style.width;
            extra.style.marginLeft = Column.style.marginLeft;
            extra.style.flexShrink = 0;
            foreach (var child in rest) extra.Add(child);
            rest[0].style.marginTop = 0;
            Root.Add(extra);
            return true;
        }

        // 지닌물건 글 상자(마우스를 받지 않는 카드도 효과를 다 읽게 — 목업 B): 토큰 그림 + 이름 + 효과 글. 우두머리 토큰 먼저
        void Notes(float w)
        {
            if (mon.Pk == null) return;
            for (int i = 0; i < mon.Items.Count; i++)
            {
                int item = mon.Items[i];
                string key = item == CardMon.AlphaToken ? "item.alpha_pokemon" : ctx.Items.Items[item].Key;
                var box = new VisualElement { name = "card-note-" + (i + 1).ToString(CultureInfo.InvariantCulture), pickingMode = PickingMode.Ignore };
                box.AddToClassList("sn-card-note");
                box.style.width = w;
                box.style.marginTop = Mathf.Round(0.2f * u);
                float edge = Mathf.Max(2f, Mathf.Round(0.25f * u));
                box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = edge;
                Radius(box, 0.5f * u);
                box.style.paddingLeft = box.style.paddingRight = Mathf.Round(0.5f * u);
                box.style.paddingTop = box.style.paddingBottom = Mathf.Round(0.3f * u);
                var head = new VisualElement { pickingMode = PickingMode.Ignore };
                head.AddToClassList("sn-card-note-head");
                box.Add(head);
                string tokenKey = TokenOrIcon(ctx, item);
                var tex = tokenKey != null ? GenAssets.Load<Texture2D>(tokenKey) : null;
                if (tex != null)
                {
                    var pic = Abs(head, tex, 0f, 0f, 1.8f * u, 1.8f * u);
                    pic.style.position = Position.Relative;
                    pic.style.marginRight = Mathf.Round(0.3f * u);
                }
                var name = Kit.Text(stage.T(key), Kit.F2);
                name.AddToClassList("sn-card-note-ink");
                head.Add(name);
                string text = stage.TOrNull(key + ".text");
                if (text != null)
                {
                    var body = Kit.Wrapped(text, Kit.F2);
                    body.AddToClassList("sn-card-note-ink");
                    box.Add(body);
                }
                Column.Add(box);
            }
        }

        VisualElement Block(Texture2D tex, float top, float height)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = 0;
            e.style.top = top;
            e.style.width = Mathf.Round(WidthCm * u);
            e.style.height = height;
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            return e;
        }

        /// <summary>기술 띠 그림의 높이(구운 그림 px — card-faces.mjs 기술 띠 = 뒷면 아래 284 px). 홈의 높이.</summary>
        public const float MoveStripPx = 284f;

        /// <summary>
        /// 칸 하나의 보이는 몫(띠의 위 블럭 밑으로 숨지 않는 자리)을 칸마다 따로 둥근 윤곽으로 보일 때의 안쪽 여백(px — 홈 · 정비 판의 받는 칸 테가 이웃 칸과 붙어 한 덩어리로 보이지 않게, 검토 지적 6) · 모서리(카드 모서리와 같은 몫).
        /// </summary>
        public static float SlotInset(float u) => Mathf.Max(2f, Mathf.Round(0.2f * u));
        public static float SlotRadius(float u) => Mathf.Round(0.55f * u);

        // 홈(정비 판 — 끄는 기술 카드를 받는 빈 칸): 띠의 보이는 몫에 칸마다 따로 둥근 금색 판(네모 덩어리 · 칸막이가 아니다 — 띠 자리 그대로, 그림 · 그림자 없음) · 색은 USS sn-card-hole
        void HoleLook(VisualElement strip, float h)
        {
            float g = SlotInset(u), seat = Mathf.Round(HideCm * u);
            var look = Abs(strip, null, g, seat + g, WidthCm * u - 2f * g, h - seat - 2f * g);
            look.name = "card-hole";
            look.AddToClassList("sn-card-holelook");
            strip.AddToClassList("sn-card-hole");   // 띠 자리 = 홈 칸(표지 — 모양은 안쪽 판)
            Radius(look, 0.55f * u);
            float edge = Mathf.Max(2f, Mathf.Round(0.15f * u));
            look.style.borderTopWidth = look.style.borderBottomWidth = look.style.borderLeftWidth = look.style.borderRightWidth = edge;
        }

        /// <summary>지닌물건 동그라미 i(0부터 — 우두머리 토큰 · 지닌물건 순)의 자리 · 지름(cm, 앞면 왼쪽 위 기준 — 원작 1.75, 12.75 · 3.5 cm, 둘째부터 오른쪽 아래로 반 겹침 [가정 ③]).</summary>
        public static void ItemSpot(int i, out float x, out float y, out float size)
        {
            size = i == 0 ? 3.3f : 2.7f;
            x = i == 0 ? 1.85f : 4.1f + (i - 1) * 2.2f;
            y = i == 0 ? 12.85f : 14.3f;
        }

        static readonly CustomStyleProperty<Color> RimColor = new CustomStyleProperty<Color>("--sn-card-rim"), LineColor = new CustomStyleProperty<Color>("--sn-card-line"), SoftColor = new CustomStyleProperty<Color>("--sn-card-soft");

        // 블럭 실루엣(둥근 모서리 · 투명 테 그대로)을 따라가는 그림자 셋(목업 10차): 둘레 짙은 테 · 아래로 짙은 선 · 부드러운 그림자 — 크기는 카드 크기에 맞춘다
        void Shade(CustomStyleResolvedEvent e)
        {
            if (!e.customStyle.TryGetValue(RimColor, out var rim) || !e.customStyle.TryGetValue(LineColor, out var line) || !e.customStyle.TryGetValue(SoftColor, out var soft)) return;
            var filters = new List<FilterFunction>
            {
                Drop(0f, Mathf.Max(1f, Mathf.Round(0.07f * u)), rim),
                Drop(Mathf.Max(1f, Mathf.Round(0.06f * u)), Mathf.Max(1f, Mathf.Round(0.05f * u)), line),
                Drop(Mathf.Max(2f, Mathf.Round(0.22f * u)), Mathf.Max(2f, Mathf.Round(0.3f * u)), soft),
            };
            foreach (var b in shaded) b.style.filter = new StyleList<FilterFunction>(filters);
        }

        static FilterFunction Drop(float dy, float blur, Color c)
        {
            var d = new FilterFunction(FilterFunctionType.DropShadow);
            d.AddParameter(new FilterParameter(0f));
            d.AddParameter(new FilterParameter(dy));
            d.AddParameter(new FilterParameter(blur));
            d.AddParameter(new FilterParameter(c));
            return d;
        }

        // 띠 위의 것: 마우스 오버 자리 · 알파 스트라이크 타입 그림 · 응징 주사위
        void StripParts(VisualElement strip, CardSlot slot, float h)
        {
            float k = Mathf.Round(WidthCm * u) / (WidthCm * PxPerCm);   // 띠 그림 px → 화면 px
            float seat = HideCm * u;
            switch (slot.Kind)
            {
                case CardSlotKind.Move:
                {
                    string key = BattleFeed.MoveNameKey(ctx, slot.Move);
                    Part(CardPartKind.Move, Hit(strip, seat, h), stage.T(key), stage.TOrNull(key + ".text"));
                    break;
                }
                case CardSlotKind.Breach:
                {
                    string key = ctx.Decks.CardIds[slot.Breach];
                    Part(CardPartKind.Breach, Hit(strip, seat, h), stage.T(key), MonParts.BreachText(stage, key) + "\n" + stage.T("screen.party.slot.breach.tip"));
                    break;
                }
                case CardSlotKind.Legend:
                {
                    Part(CardPartKind.AlphaStrike, Hit(strip, seat, LegendBeastTop * k), stage.T(MonParts.TacticKey(LegendTactic.AlphaStrike)), MonParts.TacticTip(stage, LegendTactic.AlphaStrike));
                    Part(CardPartKind.BeastBoost, Hit(strip, LegendBeastTop * k, h), stage.T(MonParts.TacticKey(LegendTactic.BeastBoost)), MonParts.TacticTip(stage, LegendTactic.BeastBoost));
                    // 배울 수 있는 타입 그림(알파 스트라이크는 그 가운데 하나로 쓴다 — 굽지 않고 포켓몬마다 얹는다)
                    var learn = new List<int>(4);
                    LegendRules.AlphaTypes(ctx, mon.Pk, learn);
                    float size = Mathf.Round(0.9f * u), gap = Mathf.Round(0.2f * u), x = (WidthCm * u - learn.Count * size - (learn.Count - 1) * gap) / 2f;
                    for (int i = 0; i < learn.Count; i++)
                    {
                        var icon = Abs(strip, GenAssets.Load<Texture2D>("Cards/Generator/types/" + ((PokemonType)learn[i]).ToString().ToLowerInvariant() + ".png"), x + i * (size + gap), LegendIconsY * k - size / 2f, size, size);
                        icon.name = "card-alpha-type-" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    }
                    break;
                }
                case CardSlotKind.LegendBeast:
                    Part(CardPartKind.BeastBoost, Hit(strip, seat, h), stage.T(MonParts.TacticKey(LegendTactic.BeastBoost)), MonParts.TacticTip(stage, LegendTactic.BeastBoost));
                    break;
                case CardSlotKind.Hole:
                    Part(CardPartKind.Hole, Hit(strip, seat, h), null, null);   // 글 없음(쓰는 화면이 받는 칸으로 쓴다)
                    break;
                default:
                {
                    Part(CardPartKind.PlanB, Hit(strip, seat, GalacticRetriTop * k), stage.T("screen.battle.move.plan_b"), stage.TOrNull("screen.battle.move.plan_b.text"));
                    string title = mon.Retribution > 0 ? stage.T("screen.battle.retribution.chip").Replace("{n}", mon.Retribution.ToString(CultureInfo.InvariantCulture)) : stage.T("screen.battle.retribution");
                    Part(CardPartKind.Retribution, Hit(strip, GalacticRetriTop * k, h), title, stage.TOrNull("screen.battle.retribution.tip"));
                    if (mon.Retribution > 0)
                    {
                        // 쌓인 피해 = 응징 상자 오른쪽 위의 피해 주사위(0이면 없음)
                        float d = Mathf.Max(2.4f * u, 20f);
                        var die = Die(strip, WidthCm * u - 0.8f * u - d, GalacticRetriTop * k + 0.9f * u, d, mon.Retribution);
                        die.name = "card-retribution";
                    }
                    break;
                }
            }
        }

        // 마우스 오버 자리(띠의 보이는 몫 · 상자 하나): 그리지 않는 칸
        static VisualElement Hit(VisualElement parent, float top, float bottom) => Abs(parent, null, 0f, top, parent.style.width.value.value, Mathf.Max(0f, bottom - top));

        void Part(CardPartKind kind, VisualElement el, string title, string text) => parts.Add(new CardPart { Kind = kind, Element = el, Title = title, Text = text });

        // 카드 위에 얹는 것(원작 cm 자리 — 목업 Card · StatPatch)
        void Overlays(VisualElement face)
        {
            var def = ctx.Cards.Pokemon[mon.Def];
            string sig = BattleFeed.MoveNameKey(ctx, def.Signature);
            Part(CardPartKind.Signature, Abs(face, null, 0.75f * u, 19.75f * u, 14.5f * u, 7.5f * u), MonParts.Star + stage.T(sig), stage.TOrNull(sig + ".text"));
            if (mon.Pk == null) return;
            // 지닌물건 동그라미(원작 1.75, 12.75 · 3.5 cm): 우두머리 토큰 먼저, 둘째부터 오른쪽 아래로 반 겹침 [가정 ③]
            for (int i = 0; i < mon.Items.Count && i < 3; i++)
            {
                int item = mon.Items[i];
                string key = item == CardMon.AlphaToken ? "item.alpha_pokemon" : ctx.Items.Items[item].Key;
                ItemSpot(i, out float x, out float y, out float size);
                string tokenKey = TokenOrIcon(ctx, item);   // 토큰 그림이 없는 지닌물건(대타출동 · 전용 아이템)은 아이템 그림(검토 지적 15)
                var token = Abs(face, tokenKey != null ? GenAssets.Load<Texture2D>(tokenKey) : null, x * u, y * u, size * u, size * u);
                token.name = "card-item-" + (i + 1).ToString(CultureInfo.InvariantCulture);
                Part(CardPartKind.Item, token, stage.T(key), stage.TOrNull(key + ".text"));
            }
            // 상태 주사위: 그림 판 왼쪽 위에서 아래로(둘째 상태도)
            for (int i = 0; i < mon.Status.Count; i++)
            {
                string key = MonParts.StatusKey(mon.Status[i]);
                var die = Abs(face, GenAssets.Load<Texture2D>("Cards/Dice/status_" + mon.Status[i].ToString().ToLowerInvariant() + ".png"), 1.1f * u, (3.4f + i * 2.65f) * u, 2.4f * u, 2.4f * u);
                die.name = "card-status-" + (i + 1).ToString(CultureInfo.InvariantCulture);
                Part(CardPartKind.Status, die, stage.T(key), stage.TOrNull(key + ".tip"));
            }
            // ♥ · ≫ 상자: 지금 값이 카드 값과 다를 때만 덮는다(최대 체력이 바뀌었으면 가득해도 — 결정 206 Q3)
            int maxDelta = mon.MaxHp - def.Health;
            if (mon.Hp < mon.MaxHp || mon.Fainted || maxDelta != 0) StatPatch(face, 7.75f, Math.Max(0, mon.Hp), mon.MaxHp, maxDelta, "card-hp");
            if (mon.Speed != def.Initiative) StatPatch(face, 10.25f, mon.Speed, -1, mon.Speed - def.Initiative, "card-speed");
            if (mon.Fainted)
            {
                var ko = Kit.Badge(stage.T("screen.info.fainted"), KitBadgeKind.Red, Kit.F2);
                ko.name = "card-fainted";
                ko.pickingMode = PickingMode.Ignore;
                ko.style.position = Position.Absolute;
                ko.style.left = Mathf.Round(6.2f * u);
                ko.style.top = Mathf.Round(9.5f * u);
                face.Add(ko);
            }
        }

        /// <summary>지닌물건(아이템 표 색인 · <see cref="CardMon.AlphaToken"/>)을 카드 동그라미에 얹을 그림 키: 원작 토큰 그림, 없으면 아이템 그림(<see cref="WorkUi.IconKey"/>), 그것도 없으면 null.</summary>
        public static string TokenOrIcon(Ctx ctx, int item)
        {
            if (item == CardMon.AlphaToken) return TokenKey(item, "item.alpha_pokemon");
            if (item < 0 || item >= ctx.Items.Items.Count) return null;
            return TokenKey(item, ctx.Items.Items[item].Key) ?? WorkUi.IconKey(ctx.Items.Items[item]);
        }

        static string TokenKey(int item, string key)
        {
            if (item == CardMon.AlphaToken) return GenAssets.ItemTokens + "alpha_pokemon.png";
            string name = key.Substring("item.".Length);
            if (GenAssets.SpotTokenKey(key) != null || Array.IndexOf(GenAssets.HeldTokens, name) >= 0) return GenAssets.ItemTokens + name + ".png";
            return null;
        }

        // 원작 상자(흰 둥근 판 + ♥ · ≫)를 인쇄된 상자 위에 덮고 원작 숫자 자리(중심 12.75 cm · 숫자 키 0.85 cm)에 지금 값(+ 작게 /최대), 증감은 상자 왼쪽 꼬리표(목업 3 · 4차)
        void StatPatch(VisualElement face, float top, int value, int max, int delta, string name)
        {
            var inv = CultureInfo.InvariantCulture;
            bool hp = max > 0;
            Abs(face, GenAssets.Load<Texture2D>("Cards/Generator/stats_bases/" + (hp ? "health" : "initiative") + ".png"), 11.75f * u, top * u, 3.5f * u, 2f * u);
            string big = value.ToString(inv), small = hp ? "/" + max.ToString(inv) : null;
            float line = 0.85f * u / NumCap, low = line * 0.6f;
            float room = 1.8f * u, wide = GlyphWidth("num", big, line) + (small != null ? GlyphWidth("num", small, low) : 0f), scale = Mathf.Min(1f, room / Mathf.Max(1f, wide));
            // 작은 카드(배틀 기둥 · 엔트리)에서 두 자리 「14/14」가 뭉개지지 않게 숫자 키에 바닥(큰 숫자 8 px · 작은 /최대 6 px — 인쇄된 숫자보다 커져도): 자리에 넘치면 상자를 왼쪽으로 늘린다(검토 지적 2)
            float bigLine = Mathf.Max(line * scale, 8f / NumCap), lowLine = Mathf.Max(low * scale, 6f / NumCap);
            float rowH = Mathf.Max(2f * u, bigLine + 2f), rowTop = top * u + u - rowH / 2f;
            float need = GlyphWidth("num", big, bigLine) + (small != null ? GlyphWidth("num", small, lowLine) : 0f);
            float extra = need > room + 0.5f ? Mathf.Ceil(need - room + 0.2f * u) : 0f;
            if (extra > 0f)
            {
                float padH = Mathf.Max(1.76f * u, bigLine + 1f);
                var pad = Abs(face, null, 11.85f * u - extra - 0.15f * u, top * u + u - padH / 2f, extra + 0.4f * u, padH);
                pad.AddToClassList("sn-card-statpad");
                Radius(pad, 0.45f * u);
            }
            var row = Abs(face, null, 11.85f * u - extra, rowTop, room + extra, rowH);
            row.name = name;
            row.userData = big + (small ?? "");
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;
            row.style.alignItems = Align.FlexEnd;
            row.style.paddingBottom = Mathf.Round((rowH - bigLine) / 2f);
            Glyphs(row, "num", big, bigLine, "sn-card-num");
            if (small != null) Glyphs(row, "num", small, lowLine, "sn-card-num").AddToClassList("sn-card-num-max");
            if (delta == 0) return;
            string text = (delta > 0 ? "+" : "−") + Math.Abs(delta).ToString(inv);
            var tag = new VisualElement { name = name + "-tag", pickingMode = PickingMode.Ignore, userData = text };
            tag.AddToClassList("sn-card-tag");
            if (delta < 0) tag.AddToClassList("sn-card-tag-down");
            tag.style.position = Position.Absolute;
            tag.style.right = Mathf.Round((WidthCm - 11.75f + 0.12f) * u + (extra > 0f ? extra + 0.15f * u : 0f));
            tag.style.top = Mathf.Round((top + 0.35f) * u);
            float edge = Mathf.Max(2f, Mathf.Round(0.12f * u));
            tag.style.borderTopWidth = tag.style.borderBottomWidth = tag.style.borderLeftWidth = tag.style.borderRightWidth = edge;
            Radius(tag, 0.45f * u);
            tag.style.paddingLeft = tag.style.paddingRight = Mathf.Round(0.2f * u);
            var glyphs = Glyphs(tag, "tag", text, Mathf.Max(0.62f * u, 9f) / TagCap);
            glyphs.style.marginTop = Mathf.Round(0.05f * u);
            face.Add(tag);
        }

        // 받은 피해 주사위 모양(원작 피해 주사위 — 짙은 면 · 분홍 테 + 원작 숫자 글꼴 흰 글 · 짙은 테, 면의 60 %)
        VisualElement Die(VisualElement parent, float x, float y, float size, int n)
        {
            var box = Abs(parent, null, x, y, size, size);
            box.AddToClassList("sn-card-die");
            box.userData = n.ToString(CultureInfo.InvariantCulture);
            float edge = Mathf.Max(2f, Mathf.Round(size * 0.07f));
            box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = edge;
            Radius(box, size * 0.12f);
            Glyphs(box, "die", n.ToString(CultureInfo.InvariantCulture), size * 0.6f / DieCap);
            return box;
        }

        float GlyphWidth(string set, string text, float height)
        {
            float w = 0f;
            foreach (char c in text)
            {
                var tex = GenAssets.Load<Texture2D>(Digit(set, c));
                if (tex != null) w += height * tex.width / tex.height;
            }
            return w;
        }

        // 글자 그림을 나란히(같은 글꼴 · 크기 그림은 높이가 같아 밑줄이 맞는다)
        static VisualElement Glyphs(VisualElement parent, string set, string text, float height, string cls = null)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexShrink = 0;
            foreach (char c in text)
            {
                var tex = GenAssets.Load<Texture2D>(Digit(set, c));
                if (tex == null) continue;
                var g = new VisualElement { pickingMode = PickingMode.Ignore };
                g.style.width = height * tex.width / tex.height;
                g.style.height = height;
                g.style.flexShrink = 0;
                g.style.backgroundImage = new StyleBackground(tex);
                if (cls != null) g.AddToClassList(cls);   // 물들이기(-unity-background-image-tint-color)는 그 그림 요소에
                row.Add(g);
            }
            parent.Add(row);
            return row;
        }

        // 전설 표지(판 그림에서 오린 원 하나): 앞 띠 밑으로 들어간 판 위에 얹으면 오린 네모의 위 모서리가 보인다 — 원으로 가린다(검토 지적 4)
        static void RoundMask(VisualElement e)
        {
            float r = Mathf.Floor(e.style.width.value.value / 2f);
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
            e.style.overflow = Overflow.Hidden;
        }

        static void Radius(VisualElement e, float r)
        {
            float v = Mathf.Round(r);
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = v;
        }

        static VisualElement Abs(VisualElement parent, Texture2D tex, float x, float y, float w, float h)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = Mathf.Round(x);
            e.style.top = Mathf.Round(y);
            e.style.width = Mathf.Round(w);
            e.style.height = Mathf.Round(h);
            e.style.flexShrink = 0;
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            parent.Add(e);
            return e;
        }

        // ── 도트 카드(카드 그림이 없을 때) ──

        void Dot(float width, bool slim)
        {
            var tip = new MonTip(stage, ctx);
            var card = mon.Pk != null ? tip.Dot(mon.Pk, mon.Hp, mon.Speed) : tip.DotWild(mon.Def);
            // 좁은 자리(배틀 기둥 · 엔트리 판 — 카드 폭 ≈ 기둥 반)의 도트 카드: 도트 위 · 값 아래로 세우고 기술 글은 뺀다(글은 카드 글 판이 맡는다) —
            // 체력 · 지닌물건 칸이 카드 밖으로 나가 옆 카드 밑에 깔리지 않게(검토 지적 21)
            if (slim) card.AddToClassList("sn-card-dotslim");
            card.style.width = Mathf.Round(width);
            Root.style.width = Mathf.Round(width);
            Root.Add(card);
        }

        // ── 마우스 오버 ──

        /// <summary>
        /// 자리마다(★ 고유 기술 · 끼운 띠 · 전술 판 상자 · 지닌물건 · 상태) 효과 글 툴팁을 건다(<see cref="KitTooltip.Hook"/> — 본문 글 크기). 그 자리만 마우스를 받는다.
        /// 도트 카드면 걸 자리가 없다(그 카드는 글을 다 적는다).
        /// </summary>
        public void HookTips(TipSide side = TipSide.Below, VisualElement clear = null)
        {
            foreach (var p in parts)
            {
                if (p.Title == null) continue;
                p.Element.pickingMode = PickingMode.Position;
                stage.Tips.Hook(p.Element, p.Title, p.Text, side, reading: true, clear: clear);
            }
        }
    }
}
