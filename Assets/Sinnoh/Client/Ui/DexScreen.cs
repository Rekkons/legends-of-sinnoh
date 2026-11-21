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
    /// S42 포켓몬 도감(04 §3-14, 결정 105 ③ — 원작 TTS에서 언제든 넘겨 보던 포켓몬 덱): 덱마다 어떤 포켓몬 카드가 몇 장 들었는지 보는 화면.
    /// 겉모양은 신오 지방 세대(Pt) 포켓몬 도감의 짜임이다(사용자 2026-11-18): 화면을 덮는 초록 바탕 + 가운데 어두운 띠,
    /// <b>왼쪽에 고른 포켓몬의 큰 그림</b>(붉은 그림 칸 · 번호 · 이름), <b>오른쪽에 「No. · 이름」 판이 굴러가는 목록</b>(전국도감 번호 순 — 고른 판이 왼쪽으로 튀어나온다, 휠 · ▲▼),
    /// 고른 판을 다시 누르거나 [상세 보기]를 누르면 목록 자리에 <b>상세 쪽</b>(타입 · 체력 · 스피드 · 등급 · 포획률 · 서식지 · 배우는 타입 · 진화와 비용 · 든 덱과 장수 · 고유 기술과 효과 문구 · 다른 폼).
    /// 위에 덱 거르기(전체 + <see cref="DexBook.Tabs"/>) · 이름 찾기 · 타입 거르기 · 갤럭시단 덱에서만 서는 주인 거르기(<see cref="DexBook.Owners"/> — 결정 201 ③, 덱 줄 아래 한 줄 — 갤럭시단 탭에서만 선다). 도감 틀 그림은 원작 자산에서 뽑지 못해 화면 USS로 흉내 낸 것이다(04 §3-14).
    /// <b>숨은 정보를 흘리지 않는다</b>: 이 화면은 정적 데이터(<see cref="Ctx"/>)와 닫는 함수만 받는다 — 판의 상태를 받지 않아 지금 덱에 남은 카드 · 순서 · 뽑은 사람을 보일 수 없다.
    /// 타이틀 「포켓몬 도감」 줄과 보드 HUD 위 띠의 [도감](<see cref="GameFlow.OpenDex"/>)이 연다. 게임 중에는 기록 창처럼 화면 대기열을 멈추지 않고, 열 때가 된 조우 창 · 배틀 화면은 이 화면을 닫고 열린다.
    /// 요소 이름: <c>dex</c>·<c>dex-panel</c>·<c>dex-count</c>·<c>dex-deck-&lt;all|덱&gt;</c>·<c>dex-name</c>·<c>dex-type-all</c>·<c>dex-type-&lt;타입&gt;</c>·<c>dex-owners</c>·<c>dex-owner-&lt;all|주인&gt;</c>·<c>dex-pic</c>·<c>dex-pic-no</c>·<c>dex-pic-name</c>·<c>dex-open</c>·
    /// <c>dex-list</c>·<c>dex-plate-&lt;줄&gt;</c>·<c>dex-empty</c>·<c>dex-pos</c>·<c>dex-up</c>·<c>dex-down</c>·<c>dex-close</c>·
    /// <c>dex-detail</c>·<c>dex-detail-rate</c>·<c>dex-detail-decks</c>·<c>dex-detail-evolve</c>·<c>dex-detail-move</c>·<c>dex-face-&lt;상태&gt;</c>.
    /// </summary>
    public sealed class DexScreen : IUiScreen
    {
        /// <summary>목록에 한 번에 보이는 판의 수.</summary>
        public const int Rows = 9;

        /// <summary>이름 찾기 칸의 글자 수 상한.</summary>
        public const int NameMax = 12;

        /// <summary>기호(문자열 표에 두지 않는다 — 두 언어 화면에 같은 글): 장수의 곱셈 기호 · 목록 넘기기 단추 · 도감 번호 머리.</summary>
        public const string Times = "×", Up = "▲", Down = "▼", NoMark = "No.";

        readonly Ctx ctx;
        readonly Action close;
        readonly List<DexEntry> shown = new List<DexEntry>();
        readonly Dictionary<string, VisualElement> decks = new Dictionary<string, VisualElement>();
        readonly Dictionary<int, VisualElement> typeOpts = new Dictionary<int, VisualElement>();
        readonly Dictionary<string, VisualElement> ownerOpts = new Dictionary<string, VisualElement>();
        List<DexEntry> all = new List<DexEntry>();
        UiStage stage;
        MonArt mons;
        VisualElement pic, picInfo, right, typeAll, owners;
        Label count;
        Button open;

        /// <param name="ctx">정적 게임 데이터(카드 표 · 덱 표).</param>
        /// <param name="close">[닫기]·Esc가 하는 일(타이틀로 · 보드로).</param>
        /// <param name="inGame">게임 중에 열었나(닫기 글 「닫기」 — 타이틀에서는 「뒤로」).</param>
        public DexScreen(Ctx ctx, Action close, bool inGame)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            this.close = close ?? throw new ArgumentNullException(nameof(close));
            InGame = inGame;
        }

        public string Name => "dex";

        public bool InGame { get; }

        /// <summary>덱 거르기(null = 전체) · 타입 거르기(null = 전체) · 이름 찾기 글.</summary>
        public DexDeck? Deck { get; private set; }
        public PokemonType? Type { get; private set; }
        public string Search { get; private set; } = "";

        /// <summary>갤럭시단 덱의 주인 거르기(null = 전체 — 다른 덱을 고르면 전체로).</summary>
        public DexOwner? Owner { get; private set; }

        /// <summary>보는 덱의 카드 전체(타입 · 이름 거르기 앞) · 거른 뒤의 카드(전국도감 번호 순).</summary>
        public IReadOnlyList<DexEntry> All => all;
        public IReadOnlyList<DexEntry> Shown => shown;

        /// <summary>목록 맨 윗줄의 카드(<see cref="Shown"/> 색인).</summary>
        public int Top { get; private set; }

        /// <summary>고른 카드(<see cref="Shown"/> 색인 — 없으면 −1)와 그림 칸에 선 포켓몬(다른 폼을 보면 고른 카드와 다르다 — 없으면 −1).</summary>
        public int Picked { get; private set; } = -1;
        public int Viewed { get; private set; } = -1;

        /// <summary>상세 쪽을 보고 있나(아니면 목록).</summary>
        public bool Detail { get; private set; }

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            mons = MonArt.Load();
            decks.Clear();
            typeOpts.Clear();
            ownerOpts.Clear();
            var page = new VisualElement { name = "dex-back" };
            page.AddToClassList("sn-dex-back");
            var panel = Add(page, new VisualElement(), "dex-panel", "sn-dex-panel");

            var head = Add(panel, new VisualElement(), null, "sn-dex-head");
            Add(head, Kit.Text(stage.T("screen.dex.title"), Kit.FTitle), null, "sn-dex-title");
            count = Add(head, Kit.Text("", Kit.F2), "dex-count", "sn-dex-count");
            Add(head, Kit.Button(stage.T(InGame ? "screen.dex.close" : "screen.common.back"), KitButtonKind.Main, close), "dex-close", null);

            var bar = Add(panel, new VisualElement(), null, "sn-dex-bar");
            DeckOpt(bar, null);
            foreach (var deck in DexBook.Tabs) DeckOpt(bar, deck);
            Add(bar, Kit.Text(stage.T("screen.dex.name"), Kit.F2), null, "sn-dex-name-label");
            var field = RoomUi.Field(bar, "dex-name", Search, NameMax);
            field.RegisterValueChangedCallback(e => Find(e.newValue));

            owners = Add(panel, new VisualElement(), "dex-owners", "sn-dex-owners");
            Add(owners, Kit.Text(stage.T("screen.dex.owner"), Kit.F2), null, "sn-dex-owner-label");
            OwnerOpt(owners, null);
            foreach (var o in DexBook.Owners) OwnerOpt(owners, o);

            var types = Add(panel, new VisualElement(), null, "sn-dex-types");
            typeAll = Add(types, new VisualElement(), "dex-type-all", "sn-opt");
            typeAll.Add(Kit.Text(stage.T("screen.dex.type.all"), Kit.F2));
            stage.OnTap(typeAll, SoundKeys.Select, e => PickType(null));
            foreach (PokemonType t in Enum.GetValues(typeof(PokemonType)))
            {
                if (t == PokemonType.Typeless) continue;
                var type = t;
                var opt = Add(types, new VisualElement(), "dex-type-" + t.ToString().ToLowerInvariant(), "sn-dex-type");
                MonParts.TypeIcon(opt, t);
                stage.OnTap(opt, SoundKeys.Select, e => PickType(type));
                stage.Tips.Hook(opt, null, stage.T("ui.types." + t), TipSide.Below);
                typeOpts[(int)t] = opt;
            }

            // 가운데 어두운 띠: 왼쪽 그림 칸 + 오른쪽 목록(또는 상세 쪽)
            var band = Add(panel, new VisualElement(), null, "sn-dex-band");
            var left = Add(band, new VisualElement(), null, "sn-dex-left");
            pic = Add(left, new VisualElement(), "dex-pic", "sn-dex-pic");
            picInfo = Add(left, new VisualElement(), null, "sn-dex-pic-info");
            open = Add(left, Kit.Button("", KitButtonKind.Normal, () => { if (Detail) Back(); else OpenDetail(); }), "dex-open", "sn-dex-open");
            right = Add(band, new VisualElement(), null, "sn-dex-right");
            right.RegisterCallback<WheelEvent>(e =>
            {
                if (!Detail) Roll(e.delta.y < 0f ? -1 : 1);   // 아래로 굴리면 다음 포켓몬
                e.StopPropagation();
            });
            Add(panel, new VisualElement(), null, "sn-dex-tail");
            Detail = false;
            Load();
            return page;
        }

        void DeckOpt(VisualElement bar, DexDeck? deck)
        {
            string id = DexBook.Id(deck);
            var opt = Add(bar, new VisualElement(), "dex-deck-" + id, "sn-opt");
            opt.Add(Kit.Text(stage.T("screen.dex.deck." + id), Kit.F2));
            stage.OnTap(opt, SoundKeys.Select, e => PickDeck(deck));
            stage.Tips.Hook(opt, null, stage.T("screen.dex.deck." + id + ".tip"), TipSide.Below, reading: true);
            decks[id] = opt;
        }

        void OwnerOpt(VisualElement row, DexOwner? owner)
        {
            string id = DexBook.OwnerId(owner);
            var opt = Add(row, new VisualElement(), "dex-owner-" + id, "sn-opt");
            opt.Add(Kit.Text(stage.T("screen.dex.owner." + id), Kit.F2));
            stage.OnTap(opt, SoundKeys.Select, e => PickOwner(owner));
            stage.Tips.Hook(opt, null, stage.T("screen.dex.owner." + id + ".tip"), TipSide.Below, reading: true);
            ownerOpts[id] = opt;
        }

        public void Close()
        {
            stage = null;
        }

        /// <summary>덱으로 거른다(null = 전체 — 다른 거르기는 그대로, 목록 첫 카드로).</summary>
        public void PickDeck(DexDeck? deck)
        {
            Deck = deck;
            if (deck != DexDeck.Galactic) Owner = null;
            Load();
        }

        /// <summary>타입으로 거른다(null = 전체, 같은 타입을 다시 누르면 전체로).</summary>
        public void PickType(PokemonType? type)
        {
            Type = type.HasValue && Type == type ? null : type;
            Load();
        }

        /// <summary>갤럭시단 덱의 주인으로 거른다(null = 전체, 같은 주인을 다시 누르면 전체로). 갤럭시단 덱이 아니면 false.</summary>
        public bool PickOwner(DexOwner? owner)
        {
            if (Deck != DexDeck.Galactic) return false;
            Owner = owner.HasValue && Owner == owner ? null : owner;
            Load();
            return true;
        }

        /// <summary>이름으로 찾는다(빈 글 = 전체).</summary>
        public void Find(string name)
        {
            Search = name ?? "";
            Load();
        }

        /// <summary>목록을 굴린다: 고른 카드를 by만큼 옮긴다(+1 = 다음). 옮겼으면 true.</summary>
        public bool Roll(int by)
        {
            if (shown.Count == 0) return false;
            int to = Mathf.Clamp(Picked + by, 0, shown.Count - 1);
            return to != Picked && Pick(to);
        }

        /// <summary>카드를 고른다(<see cref="Shown"/> 색인) — 목록이 그 카드가 보이게 굴러가고 그림 칸에 선다.</summary>
        public bool Pick(int index)
        {
            if (index < 0 || index >= shown.Count) return false;
            Picked = index;
            Viewed = shown[index].Def;
            Top = Mathf.Clamp(index - Rows / 2, 0, Math.Max(0, shown.Count - Rows));
            Paint();
            return true;
        }

        /// <summary>고른 카드의 상세 쪽을 연다(목록 자리에). 고른 카드가 없으면 false.</summary>
        public bool OpenDetail()
        {
            if (Picked < 0) return false;
            Detail = true;
            Paint();
            return true;
        }

        /// <summary>상세 쪽에서 목록으로 돌아간다(Esc · [목록]). 목록을 보고 있었으면 false.</summary>
        public bool Back()
        {
            if (!Detail) return false;
            Detail = false;
            Viewed = Picked >= 0 ? shown[Picked].Def : -1;
            Paint();
            return true;
        }

        /// <summary>그림 칸 · 상세 쪽에 고른 카드의 다른 폼(면)을 세운다 — 포켓몬 표 색인.</summary>
        public bool Face(int def)
        {
            if (Picked < 0 || !DexBook.Faces(ctx, shown[Picked].Def).Contains(def)) return false;
            Viewed = def;
            Paint();
            return true;
        }

        void Load()
        {
            if (stage == null) return;
            all = DexBook.Cards(ctx, Deck);
            shown.Clear();
            shown.AddRange(DexBook.Filter(ctx, DexBook.ByOwner(ctx, all, Owner), Type, Search, stage.T));
            Top = 0;
            Picked = shown.Count > 0 ? 0 : -1;
            Viewed = Picked >= 0 ? shown[0].Def : -1;
            Detail = false;
            Paint();
        }

        void Paint()
        {
            var inv = CultureInfo.InvariantCulture;
            foreach (var kv in decks) kv.Value.EnableInClassList("sn-opt-picked", kv.Key == DexBook.Id(Deck));
            typeAll.EnableInClassList("sn-opt-picked", !Type.HasValue);
            foreach (var kv in typeOpts) kv.Value.EnableInClassList("sn-dex-type-picked", Type.HasValue && (int)Type.Value == kv.Key);
            owners.style.display = Deck == DexDeck.Galactic ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var kv in ownerOpts) kv.Value.EnableInClassList("sn-opt-picked", kv.Key == DexBook.OwnerId(Owner));
            string total = stage.T("screen.dex.count").Replace("{kinds}", all.Count.ToString(inv)).Replace("{cards}", DexBook.Count(all).ToString(inv));
            count.text = Type.HasValue || Owner.HasValue || !string.IsNullOrWhiteSpace(Search) ? total + " · " + stage.T("screen.dex.found").Replace("{n}", shown.Count.ToString(inv)) : total;
            PaintPic();
            right.Clear();
            if (Detail) PaintDetail();
            else PaintList();
        }

        // 왼쪽: 큰 그림 + 번호 · 이름 · 타입 · 체력 · 스피드
        void PaintPic()
        {
            var inv = CultureInfo.InvariantCulture;
            pic.Clear();
            picInfo.Clear();
            open.text = stage.T(Detail ? "screen.dex.list" : "screen.dex.detail");
            open.SetEnabled(Viewed >= 0);
            if (Viewed < 0) return;
            var def = ctx.Cards.Pokemon[Viewed];
            pic.Add(mons != null ? mons.Front(def.Key) : new VisualElement());
            var title = Add(picInfo, new VisualElement(), null, "sn-dex-line");
            Add(title, Kit.Badge(No(def), KitBadgeKind.Silver, Kit.F2), "dex-pic-no", null);
            Add(title, Kit.Text(stage.T("pokemon." + def.Key), Kit.F2), "dex-pic-name", "sn-dex-pic-name");
            var stats = Add(picInfo, new VisualElement(), null, "sn-dex-line");
            foreach (var t in def.Types) MonParts.TypeIcon(stats, t);
            var hp = Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.HealthIconKey), def.Health.ToString(inv));
            var sp = Kit.Pill(GenAssets.Load<Texture2D>(KitGallery.SpeedIconKey), def.Initiative.ToString(inv));
            stats.Add(hp);
            stats.Add(sp);
            stage.Tips.Hook(hp, stage.T("screen.common.health"), null, TipSide.Below);
            stage.Tips.Hook(sp, stage.T("screen.common.speed"), null, TipSide.Below);
        }

        static string No(PokemonDef def) => NoMark + def.Dex.ToString("000", CultureInfo.InvariantCulture);

        // 오른쪽 목록: 「No. · 이름」 판 Rows줄 + 자리 · ▲▼
        void PaintList()
        {
            var inv = CultureInfo.InvariantCulture;
            var list = Add(right, new VisualElement(), "dex-list", "sn-dex-list");
            for (int row = 0; row < Rows && Top + row < shown.Count; row++) list.Add(Plate(Top + row, row));
            if (shown.Count == 0) Add(list, Kit.Text(stage.T("screen.dex.empty"), Kit.F2), "dex-empty", "sn-dex-empty");
            var foot = Add(right, new VisualElement(), null, "sn-dex-foot");
            var up = Add(foot, Kit.Button(Up, KitButtonKind.Normal, () => Roll(-Rows)), "dex-up", null);
            Add(foot, Kit.Text((Picked + 1).ToString(inv) + "/" + shown.Count.ToString(inv), Kit.F2), "dex-pos", "sn-dex-pos");
            var down = Add(foot, Kit.Button(Down, KitButtonKind.Normal, () => Roll(Rows)), "dex-down", null);
            up.SetEnabled(Picked > 0);
            down.SetEnabled(Picked >= 0 && Picked < shown.Count - 1);
        }

        // 판 한 줄: 번호 · 이름(폼 꼬리는 상세 쪽에) · 타입 · 장수(둘 이상) — 고른 판은 왼쪽으로 튀어나온다. 고른 판을 다시 누르면 상세 쪽
        VisualElement Plate(int index, int row)
        {
            var inv = CultureInfo.InvariantCulture;
            var e = shown[index];
            var def = ctx.Cards.Pokemon[e.Def];
            var plate = new VisualElement { name = "dex-plate-" + row.ToString(inv) };
            plate.AddToClassList("sn-dex-plate");
            plate.EnableInClassList("sn-dex-plate-picked", index == Picked);
            Add(plate, new VisualElement(), null, "sn-dex-ball");
            Add(plate, Kit.Text(No(def), Kit.F2), null, "sn-dex-plate-no");
            Add(plate, Kit.Text(BoardHud.ShortName(stage.T("pokemon." + def.Key)), Kit.F2), null, "sn-dex-plate-name");
            string owner = DexBook.OwnerKey(def);
            if (owner != null) plate.Add(Kit.Badge(stage.T("screen.dex.deck.galactic"), KitBadgeKind.Plain, Kit.F1));
            if (e.Copies > 1 && Deck.HasValue) plate.Add(Kit.Badge(Times + e.Copies.ToString(inv), KitBadgeKind.Silver, Kit.F1));
            foreach (var t in def.Types) MonParts.TypeIcon(plate, t);
            stage.OnTap(plate, SoundKeys.Select, ev => { if (index == Picked) OpenDetail(); else Pick(index); });
            return plate;
        }

        // 오른쪽 상세 쪽
        void PaintDetail()
        {
            var inv = CultureInfo.InvariantCulture;
            var def = ctx.Cards.Pokemon[Viewed];
            var detail = Add(right, new VisualElement(), "dex-detail", "sn-dex-detail");
            var facts = Add(detail, new VisualElement(), null, "sn-dex-facts");

            // 등급 · 포획률(등급별 — 잡을 수 없는 카드는 「포획 불가」)
            string tier = stage.TOrNull("screen.encounter.grade." + MapActions.GradeOf(ctx, Viewed).ToString().ToLowerInvariant());
            if (tier != null) Fact(facts, "screen.mon.tier").Add(Kit.Text(tier, Kit.F2));
            int rate = DexBook.CatchRate(ctx, Viewed);
            var rateRow = Fact(facts, "screen.dex.rate");
            rateRow.name = "dex-detail-rate";
            rateRow.Add(rate > 0 ? Kit.Badge(stage.T("screen.dex.rate.value").Replace("{n}", rate.ToString(inv)), KitBadgeKind.Gold, Kit.F2) : Kit.Badge(stage.T("screen.dex.rate.none"), KitBadgeKind.Plain, Kit.F2));
            stage.Tips.Hook(rateRow, stage.T("screen.dex.rate"), stage.T(rate > 0 ? "screen.dex.rate.tip" : "screen.dex.rate.none.tip"), TipSide.Below, reading: true);
            string owner = DexBook.OwnerKey(def);
            if (owner != null) Fact(facts, "screen.dex.owner").Add(Kit.Text(stage.T(owner), Kit.F2));
            string biome = def.Biome != Biome.None ? stage.TOrNull("screen.encounter.biome." + def.Biome.ToString().ToLowerInvariant()) : null;
            string climate = def.Climate != Climate.None ? stage.TOrNull("screen.encounter.climate." + def.Climate.ToString().ToLowerInvariant()) : null;
            if (biome != null || climate != null) Fact(facts, "screen.mon.habitat").Add(Kit.Text(biome != null && climate != null ? biome + " · " + climate : biome ?? climate, Kit.F2));
            if (def.LearnTypes.Count > 0)
            {
                var learn = Fact(facts, "screen.party.learn");
                foreach (var t in def.LearnTypes) MonParts.TypeIcon(learn, t);
            }
            var evolve = Fact(facts, "screen.mon.evolve");
            evolve.name = "dex-detail-evolve";
            if (def.EvolveInto.Count == 0) Add(evolve, Kit.Text(stage.T("screen.mon.evolve.none"), Kit.F2), null, "kit-muted");
            else
            {
                foreach (int into in def.EvolveInto) evolve.Add(Kit.Text(stage.T("pokemon." + ctx.Cards.Pokemon[into].Key), Kit.F2));
                if (def.EvolveCost.HasValue) evolve.Add(Kit.Badge(stage.T("screen.mon.evolve.cost").Replace("{n}", def.EvolveCost.Value.ToString(inv)), KitBadgeKind.Plain, Kit.F2));
            }
            // 든 덱과 장수(고른 카드의 것 — 다른 폼은 그 카드의 면이라 따로 들지 않는다)
            var inDecks = Fact(facts, "screen.dex.copies");
            inDecks.name = "dex-detail-decks";
            foreach (var kv in DexBook.Decks(ctx, shown[Picked].Def))
                inDecks.Add(Kit.Badge(stage.T("screen.dex.copies.in").Replace("{deck}", stage.T("screen.dex.deck." + DexBook.Id(kv.Key))).Replace("{n}", kv.Value.ToString(inv)), KitBadgeKind.Plain, Kit.F2));

            // 고유 기술(효과 문구를 줄로 적는다)
            Add(detail, Kit.Text(stage.T("screen.mon.sig"), Kit.F2), null, "kit-muted");
            var move = Add(detail, new VisualElement(), "dex-detail-move", "sn-mon-move");
            move.Add(MonParts.MoveRow(stage, ctx, def.Signature, true, null));
            string text = stage.TOrNull(BattleFeed.MoveNameKey(ctx, def.Signature) + ".text");
            if (text != null) Add(move, Kit.Wrapped(text, Kit.F2), null, "sn-mon-text");

            // 다른 폼: 같은 카드의 면들 — 누르면 그림 칸과 이 쪽이 그 면으로
            var faces = DexBook.Faces(ctx, shown[Picked].Def);
            if (faces.Count > 0)
            {
                Add(detail, Kit.Text(stage.T("screen.dex.faces"), Kit.F2), null, "kit-muted");
                var row = Add(detail, new VisualElement(), null, "sn-dex-faces");
                foreach (int f in faces)
                {
                    int face = f;
                    var fd = ctx.Cards.Pokemon[f];
                    var opt = Add(row, new VisualElement(), "dex-face-" + fd.State.ToString(inv), "sn-opt");
                    opt.AddToClassList("sn-dex-face");
                    opt.Add(Kit.Text(stage.T("pokemon." + fd.Key), Kit.F2));
                    opt.EnableInClassList("sn-opt-picked", f == Viewed);
                    stage.OnTap(opt, SoundKeys.Select, e => Face(face));
                }
            }
        }

        VisualElement Fact(VisualElement parent, string labelKey)
        {
            var row = Add(parent, new VisualElement(), null, "sn-dex-line");
            Add(row, Kit.Text(stage.T(labelKey), Kit.F2), null, "sn-dex-label");
            return row;
        }

        static T Add<T>(VisualElement parent, T child, string name, string cls) where T : VisualElement
        {
            if (name != null) child.name = name;
            if (cls != null) child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
