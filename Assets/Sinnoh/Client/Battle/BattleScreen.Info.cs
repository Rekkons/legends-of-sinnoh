using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 배틀 정보 기둥(결정 206 ② — 목업 <c>Logs/Review/card/</c> 10차 · 2차 무리): 원작 세로 쌓기 카드(<see cref="CardStack"/>) 둘을 좌우로 — 왼쪽 = 아래쪽(내 쪽 · 남의 배틀이면 그 쪽 좌석), 오른쪽 = 상대.
    /// 쪽마다 이름 줄(얼굴 상자 · 이름) → 카드(기둥 높이에 맞춰 두 장을 같은 크기로) → 덧칩 줄(우선권 토큰 · 전투 피로 · 패시브 · 날씨 · 응징 · 교체 불가 · 오라 · UB · 무리 진행) · 야생 줄(등급 · 포획률 · 규토리).
    /// 카드에 마우스를 올리면 기둥 오른쪽(DS 위)에 기술 · 지닌물건 · 상태 글 판(<c>battle-card-tip</c>). 머리 줄 오른쪽 [내 엔트리 n] · [상대 엔트리 n](명단이 둘 이상인 쪽만) =
    /// 기둥 오른쪽부터 화면 오른쪽 끝까지 덮는 엔트리 판(카드 + 기술 띠 · 꼬리표 「배틀 중」 · 「대기」 · 「기절」 · [닫기] · Esc · 단추 다시 누르기 = 닫기 — 결정 176).
    /// 무리 배틀은 상대 카드 셋을 엇겹치고(나와 있는 것이 맨 앞), 마우스를 올리면 엔트리 판으로 셋을 펼친다. 모두 보기의 공개 값이다(남의 손패 · 비공개 값 없음).
    /// </summary>
    public sealed partial class BattleScreen
    {
        /// <summary>머리 줄의 엔트리 단추(결정 206 ②)와 엔트리 판 · 판 안 카드(<c>battle-entry-n</c>, 1부터)의 이름.</summary>
        public const string EntryMineButton = "battle-entry-mine", EntryFoeButton = "battle-entry-foe", EntryPanel = "battle-entry";

        /// <summary>엔트리 판이 열린 쪽의 단추에 입히는 클래스.</summary>
        public const string EntryOnClass = "sn-battle-entry-on";

        /// <summary>엔트리 판의 기절한 카드 칸 클래스(갤럭시단 명단의 쓰러진 칸 — 결정 201 ①이 옮겨 온 자리).</summary>
        public const string RosterDoneClass = "sn-battle-entry-out";

        /// <summary>엔트리 판에서 지금 배틀 중인 카드 칸의 클래스(노란 테 — 목업 10차).</summary>
        public const string EntryNowClass = "sn-battle-entry-now";

        /// <summary>엇겹친 무리 카드 사이(카드 폭 16 cm 기준 cm — 목업 2차 1.4u).</summary>
        const float SwarmStepCm = 1.4f;

        // 카드 폭(기둥 두 쪽이 같은 크기 — 자리 높이에 맞춰 GeometryChanged가 다시 정한다) · 엔트리 판 카드 폭
        float cardW = -1f, entryW = -1f;
        readonly VisualElement[] holders = new VisualElement[2];
        readonly BattleMon[] sideMons = new BattleMon[2];
        readonly float[] sideRatio = new float[2];
        readonly float[] sideWide = new float[2];
        readonly List<VisualElement> swarmCards = new List<VisualElement>();
        BattleFeed infoFeed;
        int storedNow;
        VisualElement cardTip;
        int entrySide = -1;   // 열린 엔트리 판: 0 = 내 쪽 · 1 = 상대 · −1 = 닫힘
        int entryShape = -1;  // 마지막으로 그린 판의 쪽 · 장수(폭을 다시 잴 때)
        bool entryHover;      // 무리 카드에 마우스를 올려 연 판(포인터가 떠나면 닫힌다)
        VisualElement entry, entryCards;
        readonly List<int> entryShown = new List<int>();
        readonly List<float> entryRatio = new List<float>();

        /// <summary>카드 글 판(마우스 오버)이 떠 있는 쪽(0 = 내 쪽 · 1 = 상대 · −1 = 없음)과 그 요소 — 확인용.</summary>
        public int CardTipSide { get; private set; } = -1;
        public VisualElement CardTipElement => cardTip;

        /// <summary>엔트리 판이 열려 있나 · 그 쪽(true = 내 쪽) · 판에 올린 포켓몬(엔트리 순서) — 확인용.</summary>
        public bool EntryOpen => entrySide >= 0 && entry != null && entry.style.display != DisplayStyle.None;
        public bool EntryMine => entrySide == 0;
        public IReadOnlyList<int> EntryShown => entryShown;

        /// <summary>기둥 카드의 폭(px) — 확인용.</summary>
        public float CardWidth => cardW;

        /// <summary>상대 기둥의 갤럭시단 명단(결정 201 ① → 결정 206 ② [상대 엔트리]): 명단 수 · 쓰러진 수 · 지금 나와 있는 칸(0부터, 없으면 −1) — 명단이 아니면 0 · 0 · −1(확인용).</summary>
        public int RosterCount { get; private set; }
        public int RosterDone { get; private set; }
        public int RosterActive { get; private set; } = -1;

        // ── 기둥 ──

        void FillInfo(BattleFeed feed, BattleMon me, BattleMon foe)
        {
            HideCardTip();
            info.Clear();
            infoFeed = feed;
            ReadRows(feed, out storedNow, lockedNow);
            var state = feed.Source;
            sideMons[0] = me;
            sideMons[1] = foe;

            // 머리 줄: 내 쪽 얼굴 상자 … [엔트리] 단추(「배틀 정보」 제목은 뺐다 — 얼굴 상자 · 엔트리 단추가 두 벌 · 영어에서 기둥 폭 안에 들게) · 상대 좌석 얼굴 상자(결정 61 ③ — 얼굴 상자는 이름 줄이 아니라 여기: 이름 줄을 글 한 줄로 낮춰 카드를 키운다)
            // 내 쪽 = 아래쪽(feed.MySide의 좌석 — 남의 배틀을 볼 때는 그 쪽에서 싸우는 좌석, M7 마감 감사 1)
            int seat = InfoSeat = SideSeat(feed);
            int foeSeat = state != null && state.Battle != null ? state.Battle.Sides[1 - feed.MySide].Owner : -1;
            var head = Add(info, new VisualElement(), null, "sn-battle-head");
            var headLeft = Add(head, new VisualElement(), null, "sn-battle-headside");
            var face = Add(headLeft, FaceBox.Make(), "battle-me-face", "sn-battle-headface");
            FaceBox.Paint(face, seat, flow.SeatColor(seat), flow.Protagonist(seat));
            var headRight = Add(head, new VisualElement(), null, "sn-battle-headside");
            var buttons = Add(headRight, new VisualElement(), "battle-entries", "sn-battle-entries");
            bool spectating = flow.Client == null || seat != flow.Client.Seat;
            for (int k = 0; k < 2; k++)
            {
                int count = EntryIds(feed, k == 0).Count;
                if (count < 2) continue;   // 명단이 하나뿐인 쪽은 단추가 없다(야생 1:1 = 내 쪽만)
                int side = k;
                string inv0 = count.ToString(CultureInfo.InvariantCulture);
                // 남의 배틀을 볼 때 아래쪽은 「내」 엔트리가 아니다 — 그 좌석 이름으로
                string label = k == 1 ? stage.T("screen.battle.entry.foe").Replace("{n}", inv0)
                    : spectating ? stage.T("screen.battle.entry.seat").Replace("{name}", flow.SeatName(seat)).Replace("{n}", inv0)
                    : stage.T("screen.battle.entry.mine").Replace("{n}", inv0);
                var button = Add(buttons, Kit.Button(label, KitButtonKind.Normal, () => ToggleEntry(side == 0)), k == 0 ? EntryMineButton : EntryFoeButton, "sn-battle-entry-button");
                if (entrySide == k && !entryHover) button.AddToClassList(EntryOnClass);
            }
            if (foeSeat >= 0)
            {
                var foeFace = Add(headRight, FaceBox.Make(), "battle-foe-face", "sn-battle-headface");
                foeFace.AddToClassList("sn-battle-headface-end");
                FaceBox.Paint(foeFace, foeSeat, flow.SeatColor(foeSeat), flow.Protagonist(foeSeat));
            }
            RosterStats(feed);

            var cols = Add(info, new VisualElement(), "battle-info-cols", "sn-battle-cols");
            var mine = Add(cols, new VisualElement(), "battle-info-me", "sn-battle-infoside");
            var who = Add(mine, new VisualElement(), "battle-me-head", "sn-battle-line");
            who.AddToClassList("sn-battle-sidehead");
            Add(who, Kit.Text(flow.SeatName(seat), Kit.F2), "battle-me-who", "sn-battle-sidename");
            holders[0] = Add(mine, new VisualElement(), "battle-me-cards", "sn-battle-cards");
            var meExtra = Add(mine, new VisualElement(), "battle-me-extra", "sn-battle-line");
            meExtra.AddToClassList("sn-battle-extra");
            if (seen.TokenSide == feed.MySide) Token(meExtra, "battle-me-token");   // 우선권 토큰: 덧칩 줄 맨 앞(카드 자리를 밀지 않는다 — 결정 73 검토)
            SideChips(meExtra, me, "battle-me");
            // 날씨(결정 87 V7): 놓여 있으면 내 덧칩 줄에 — 배틀 중에 날씨 보너스를 바로 본다(마우스 오버 = 「○ 타입 기술 위력 +2」)
            if (state != null && TrainerRules.WeatherType(state).HasValue)
            {
                var sky = Add(meExtra, Kit.Badge(CardAsk.Weather(stage, state.Weather), KitBadgeKind.Plain, Kit.F2), WeatherChip, null);
                stage.Tips.Hook(sky, CardAsk.WeatherBonus(stage, state.Weather), stage.T("screen.board.weather.tip"), TipSide.Below, reading: true);
            }

            // 상대 쪽: 사람(좌석)이면 그 좌석 이름(얼굴 상자는 머리 줄 오른쪽 끝), 야생 · 갤럭시단은 이름만(결정 54 ④)
            var other = Add(cols, new VisualElement(), "battle-info-foe", "sn-battle-infoside");
            other.AddToClassList("sn-battle-infoside-foe");
            var foeWho = Add(other, new VisualElement(), "battle-foe-head", "sn-battle-line");
            foeWho.AddToClassList("sn-battle-sidehead");
            string foeName;
            if (foeSeat >= 0) foeName = flow.SeatName(foeSeat);
            else
            {
                string foeKey = feed.Kind == BattleKind.Galactic ? "galactic" + feed.LeaderSuffix : feed.Kind == BattleKind.Trainer || feed.Kind == BattleKind.Traitor ? "other" : BattleFeed.UbFoe(feed.Kind) ?? "wild";
                foeName = stage.T("screen.battle.foe." + foeKey);
            }
            Add(foeWho, Kit.Text(foeName, Kit.F2), "battle-foe-who", "sn-battle-sidename");
            holders[1] = Add(other, new VisualElement(), "battle-foe-cards", "sn-battle-cards");
            var foeExtra = Add(other, new VisualElement(), "battle-foe-extra", "sn-battle-line");
            foeExtra.AddToClassList("sn-battle-extra");
            if (seen.TokenSide == 1 - feed.MySide) Token(foeExtra, "battle-foe-token");
            SideChips(foeExtra, foe, "battle-foe");
            if (foe.Exists && state != null && state.Battle != null && foe.Id < state.Pokemon.Count && ChoiceRules.CaptureAllowed(state.Battle) && !UbTrial.Guards(state, flow.Ctx, state.Battle, foe.Id))   // 시련을 넘기기 전의 수호신은 포획률 대신 「면역」 칩(FillUb)
            {
                // 야생: 조우 등급 · 포획률 · 보상 규토리(04 §4-1 — 값은 엔진 함수와 그 칸 토큰의 고정물에서)
                var inv = CultureInfo.InvariantCulture;
                var wild = state.Pokemon[foe.Id];
                var grade = MapActions.GradeOf(flow.Ctx, wild.Def);
                var line = Add(other, new VisualElement(), "battle-foe-wild", "sn-battle-line");
                line.AddToClassList("sn-battle-extra");
                line.Add(MonParts.GradeBadge(stage, wild.Alpha ? "alpha" : grade.ToString().ToLowerInvariant(), Kit.F2) ?? new Label());   // 등급 색(결정 192)
                int rate = BattleRules.CatchRate(grade, wild.Alpha);
                var rateChip = Add(line, Kit.Badge(stage.T("screen.encounter.rate").Replace("{n}", rate.ToString(inv)), KitBadgeKind.Silver, Kit.F2), "battle-foe-rate", null);
                stage.Tips.Hook(rateChip, null, stage.T("screen.encounter.rate.tip").Replace("{n}", rate.ToString(inv)), TipSide.Below, reading: true);
                int token = state.Battle.Token;
                if (token >= 0 && token < state.Tokens.Count && state.Tokens[token].Def >= 0)
                    foreach (var color in flow.Ctx.Fixtures.Fixtures[state.Tokens[token].Def].Apricorns) Icon(line, EncounterScreen.ApricornKey(color));
            }

            if (cardW <= 0f) cardW = GuessCardWidth();
            BuildCards();
            foreach (var h in holders) h.RegisterCallback<GeometryChangedEvent>(e => FitCards());
            cols.RegisterCallback<GeometryChangedEvent>(e => EvenSides());
            BuildEntry();
        }

        // 두 쪽의 이름 줄 · 덧칩 줄을 같은 높이로(카드 위 끝이 맞고, 카드 칸 높이가 같다 — 한쪽만 줄바꿈 · 덧칩이 없어도)
        void EvenSides()
        {
            if (info == null) return;
            // 이름 줄: 같은 높이(한쪽 이름만 두 줄이어도 카드 위 끝이 맞는다)
            var ha = info.Q("battle-me-head");
            var hb = info.Q("battle-foe-head");
            if (ha != null && hb != null && !float.IsNaN(ha.layout.height) && !float.IsNaN(hb.layout.height))
            {
                float h = Mathf.Max(ha.layout.height, hb.layout.height);
                foreach (var e in new[] { ha, hb })
                    if (h > 0f && Mathf.Abs(e.resolvedStyle.minHeight.value - h) > 0.5f) e.style.minHeight = h;
            }
            // 카드 아래 몫(덧칩 줄 + 야생 줄): 두 쪽 같은 높이 — 짧은 쪽 덧칩 줄을 늘린다(덧칩은 줄 위에 붙어 「전투 피로」가 같은 높이에 선다 · 카드 칸 높이도 같다)
            var sides = new[] { info.Q("battle-info-me"), info.Q("battle-info-foe") };
            var extras = new[] { info.Q("battle-me-extra"), info.Q("battle-foe-extra") };
            var tails = new float[2];
            for (int k = 0; k < 2; k++)
            {
                if (sides[k] == null || holders[k] == null || extras[k] == null || extras[k].style.display == DisplayStyle.None) return;
                float bottom = sides[k].layout.height, cards = holders[k].layout.yMax;
                if (float.IsNaN(bottom) || float.IsNaN(cards)) return;
                tails[k] = bottom - cards;
            }
            int shorter = tails[0] < tails[1] ? 0 : 1;
            float diff = Mathf.Abs(tails[0] - tails[1]);
            if (diff > 0.5f) extras[shorter].style.minHeight = extras[shorter].layout.height + diff;
        }

        // 쪽의 덧칩 줄 앞몫: 전투 피로(늘) · 패시브(비스트 부스트 — 우두머리는 카드의 토큰이 말한다)
        void SideChips(VisualElement line, BattleMon mon, string name)
        {
            if (!mon.Exists) return;
            var inv = CultureInfo.InvariantCulture;
            var fatigue = Kit.Badge(stage.T("screen.battle.fatigue").Replace("{n}", mon.Fatigue.ToString(inv)).Replace("{max}", FatigueRules.Max.ToString(inv)),
                mon.Fatigue >= FatigueRules.Max ? KitBadgeKind.Red : KitBadgeKind.Plain, Kit.F2);
            fatigue.name = name + "-fatigue";
            line.Add(fatigue);
            stage.Tips.Hook(fatigue, null, stage.T("screen.battle.fatigue.tip"), TipSide.Below, reading: true);
            var known = flow.Client != null ? flow.Client.View : null;
            if (known != null && mon.Id >= 0 && mon.Id < known.Pokemon.Count) MonParts.Passives(stage, line, known.Pokemon[mon.Id], false, name + "-passive");
        }

        // 첫 그리기(자리가 아직 정해지지 않았다)의 카드 폭: 기둥 폭의 반 남짓 — 배치가 끝나면 FitCards가 높이에 맞춰 다시 정한다
        float GuessCardWidth()
        {
            float w = info.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0f) w = stage.Root.layout.width * 0.27f;
            if (float.IsNaN(w) || w <= 0f) return CardStack.WidthCm * 9f;
            return Mathf.Floor(w * 0.43f);
        }

        // ── 카드 ──

        // 그리는 지금 값의 카드 한 장(배틀 화면이 그리는 체력 · 스피드 · 상태 · 폼, 응징은 드러난 이벤트로 센 값)
        CardStack MakeStack(BattleFeed feed, int id, int side, float width)
        {
            var state = feed.Source;
            var mon = Shown(id);
            CardMon m;
            if (state != null && id >= 0 && id < state.Pokemon.Count && state.Pokemon[id].Def >= 0)
            {
                int retri = feed.Kind == BattleKind.Galactic && side == 1 - feed.MySide ? storedNow : 0;
                m = CardMon.Of(flow.Ctx, state, id, mon.Exists ? mon.Hp : (int?)null, mon.Exists ? mon.Speed : (int?)null, retri);
            }
            else if (mon.Exists) m = CardMon.Wild(flow.Ctx, mon.Def);
            else return null;
            if (mon.Exists)
            {
                m.Def = mon.Def;   // 폼 바꾸기(78 · 79행)가 닿기 전에는 옛 폼
                m.MaxHp = mon.MaxHp;
                m.Hp = mon.Hp;
                m.Status.Clear();
                if (mon.Status != StatusEffect.None) m.Status.Add(mon.Status);
                if (mon.Status2 != StatusEffect.None) m.Status.Add(mon.Status2);
            }
            return CardStack.Build(stage, flow.Ctx, m, width);
        }

        void BuildCards()
        {
            var feed = infoFeed;
            if (feed == null) return;
            swarmCards.Clear();
            swarmSlots.Clear();
            for (int k = 0; k < 2; k++)
            {
                var holder = holders[k];
                if (holder == null) continue;
                holder.Clear();
                sideRatio[k] = 0f;
                sideWide[k] = 1f;
                var mon = sideMons[k];
                int side = k == 0 ? feed.MySide : 1 - feed.MySide;
                if (k == 1 && feed.Kind == BattleKind.Swarm && feed.Source != null && feed.Source.Battle != null)
                {
                    BuildSwarm(feed, holder);
                    continue;
                }
                if (!mon.Exists) continue;
                string name = k == 0 ? "battle-me" : "battle-foe";
                var stack = MakeStack(feed, mon.Id, side, cardW);
                if (stack == null) continue;
                var wrap = Add(holder, new VisualElement(), name, "sn-battle-card");
                wrap.Add(stack.Root);
                Mark(stack, name, mon);
                if (stack.Art && stack.Height > 0f) sideRatio[k] = stack.Height / cardW;
                int tip = k;
                wrap.RegisterCallback<PointerEnterEvent>(e => ShowCardTip(tip));
                wrap.RegisterCallback<PointerLeaveEvent>(e => { if (CardTipSide == tip) HideCardTip(); });
            }
        }

        // 카드 안 요소에 기둥 이름을 붙인다(튜토리얼 앵커 · 시험이 쓰던 이름 그대로): 앞면 = -art, 지닌물건 동그라미 = -item-n(우두머리 토큰은 -alpha),
        // 이름 띠 · ♥ 상자 자리 = 보이지 않는 칸 -name · -hp(♥ 칸의 userData = 「지금/최대」)
        void Mark(CardStack stack, string name, BattleMon mon)
        {
            var root = stack.Root;
            var face = root.Q("card-face");
            if (face == null)
            {
                // 도트 카드(카드 그림 없음): 같은 이름을 도트 카드의 칸에 붙인다(튜토리얼 앵커 e05 S013 · e06 S012 — battle-me-hp · battle-foe-alpha 등)
                if (root.childCount == 0) return;
                var dot = root[0];
                dot.name = name + "-art";
                var label = dot.Q("mon-tip-name");
                if (label != null) label.name = name + "-name";
                var stats = dot.Q("mon-tip-stats");
                if (stats != null && stats.childCount > 0)
                {
                    stats[0].name = name + "-hp";
                    stats[0].userData = Math.Max(0, mon.Hp).ToString(CultureInfo.InvariantCulture) + "/" + mon.MaxHp.ToString(CultureInfo.InvariantCulture);
                }
                var alpha = dot.Q("mon-tip-alpha");
                if (alpha != null) alpha.name = name + "-alpha";
                int held = 0;
                foreach (var item in dot.Query("mon-tip-item").ToList()) item.name = name + "-item-" + (++held).ToString(CultureInfo.InvariantCulture);
                return;
            }
            face.name = name + "-art";
            int n = 0;
            for (int i = 1; ; i++)
            {
                var item = root.Q("card-item-" + i.ToString(CultureInfo.InvariantCulture));
                if (item == null) break;
                var pk = flow.Client != null && mon.Id >= 0 && mon.Id < flow.Client.View.Pokemon.Count ? flow.Client.View.Pokemon[mon.Id] : null;
                item.name = i == 1 && pk != null && pk.Alpha ? name + "-alpha" : name + "-item-" + (++n).ToString(CultureInfo.InvariantCulture);
            }
            // 은하의 응징 칸(갤럭시단 띠의 둘째 상자 — 쌓인 피해 주사위가 있는 자리): 예전 칩 이름 그대로(튜토리얼 앵커 e06 S006)
            foreach (var p in stack.Parts)
                if (p.Kind == CardPartKind.Retribution) p.Element.name = name + "-retribution";
            float u = cardW / CardStack.WidthCm;
            Hit(face, name + "-name", 0f, 0f, CardStack.WidthCm * u, 2.6f * u, null);
            Hit(face, name + "-hp", 11.75f * u, 7.75f * u, 3.5f * u, 2f * u, Math.Max(0, mon.Hp).ToString(CultureInfo.InvariantCulture) + "/" + mon.MaxHp.ToString(CultureInfo.InvariantCulture));
        }

        static void Hit(VisualElement parent, string name, float x, float y, float w, float h, string data)
        {
            var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore, userData = data };
            e.style.position = Position.Absolute;
            e.style.left = Mathf.Round(x);
            e.style.top = Mathf.Round(y);
            e.style.width = Mathf.Round(w);
            e.style.height = Mathf.Round(h);
            parent.Add(e);
        }

        // 무리 배틀(S55 — 24 · 81행): 상대 카드 셋을 오른쪽 아래로 엇겹친다 — 나와 있는 것이 맨 앞, 끝난 것(기절 · 포획)은 회색. 마우스 오버 = 엔트리 판으로 셋을 펼친다(결정 206 ②)
        void BuildSwarm(BattleFeed feed, VisualElement holder)
        {
            var state = feed.Source;
            var roster = state.Battle.Sides[1 - feed.MySide].Roster;
            int active = seen.Active[1 - feed.MySide];
            var order = new List<int>(roster);
            if (order.Remove(active)) order.Insert(0, active);   // 맨 앞 = 나와 있는 것
            float u = cardW / CardStack.WidthCm, step = Mathf.Round(SwarmStepCm * u);
            var box = Add(holder, new VisualElement(), "battle-foe", "sn-battle-swarmcards");
            float tall = 0f;
            var made = new List<KeyValuePair<int, CardStack>>();
            foreach (int id in order)
            {
                var stack = MakeStack(feed, id, 1 - feed.MySide, cardW);
                if (stack == null) continue;
                made.Add(new KeyValuePair<int, CardStack>(id, stack));
                if (stack.Art && stack.Height > tall) tall = stack.Height;
            }
            // 뒤 카드부터 넣는다(앞 카드가 위에 그려진다)
            for (int i = made.Count - 1; i >= 0; i--)
            {
                int id = made[i].Key;
                var slot = Add(box, new VisualElement(), "battle-swarm-slot-" + (roster.IndexOf(id) + 1).ToString(CultureInfo.InvariantCulture), "sn-battle-swarmcard");
                slot.style.left = step * i;
                slot.style.top = step * i;
                slot.Add(made[i].Value.Root);
                if (SwarmOver(feed, id)) made[i].Value.Root.AddToClassList("sn-card-out");
                if (i == 0) Mark(made[i].Value, "battle-foe", Shown(id));
                swarmCards.Add(slot);
            }
            // 명단이 서는 순서(약한 것부터 — 24행 인트로)대로 페이드: roster 순서
            foreach (int id in roster)
                foreach (var slot in swarmCards)
                    if (slot.name == "battle-swarm-slot-" + (roster.IndexOf(id) + 1).ToString(CultureInfo.InvariantCulture)) swarmSlots.Add(slot);
            float spread = step * Math.Max(0, made.Count - 1);
            box.style.width = Mathf.Round(cardW) + spread;
            box.style.height = tall + spread;
            if (tall > 0f)
            {
                sideRatio[1] = (tall + spread) / cardW;
                sideWide[1] = (cardW + spread) / cardW;
            }
            box.RegisterCallback<PointerEnterEvent>(e => OpenEntry(false, true));
            box.RegisterCallback<PointerLeaveEvent>(e => { if (entryHover) CloseEntry(); });
        }

        // 그 포켓몬을 잡았나(드러난 포획 성공 — 무리 · 야생 명단)
        bool Caught(BattleFeed feed, int id)
        {
            foreach (var e in feed.Events)
                if (e is CaptureAttempt c && c.Success && c.TargetId == id && !hidden.Contains(e)) return true;
            return false;
        }

        // 무리의 그 포켓몬이 끝났나(잡힘 · 기절 · 결과가 난 뒤 나오지 않은 것)
        bool SwarmOver(BattleFeed feed, int id)
        {
            foreach (var e in feed.Events)
                if (e is CaptureAttempt c && c.Success && c.TargetId == id && !hidden.Contains(e)) return true;
            var mon = Shown(id);
            return (mon.Exists && mon.Fainted) || (!mon.Exists && id != seen.Active[1 - feed.MySide] && feed.Ended);
        }

        // 자리(카드 칸)에 맞춰 카드 폭을 다시 정한다: 두 쪽이 같은 폭 — 칸 폭 안 · 쌓은 높이가 칸 높이 안
        void FitCards()
        {
            if (stage == null || infoFeed == null) return;
            float best = float.MaxValue;
            bool any = false;
            for (int k = 0; k < 2; k++)
            {
                var h = holders[k];
                if (h == null || h.panel == null) continue;
                var r = h.layout;
                if (float.IsNaN(r.width) || r.width <= 0f || float.IsNaN(r.height) || r.height <= 0f) continue;
                float w = r.width / Math.Max(1f, sideWide[k]);
                if (sideRatio[k] > 0f) w = Math.Min(w, (r.height - 2f) / sideRatio[k]);   // 블럭마다 반올림한 높이 몫(1~2 px)을 남긴다
                best = Math.Min(best, w);
                any = true;
            }
            if (!any) return;
            best = Mathf.Floor(best);
            if (best < 8f || Math.Abs(best - cardW) < 1f) return;
            cardW = best;
            BuildCards();
            FitCardsDone = true;
        }

        /// <summary>카드 폭을 자리에 맞춘 적이 있나 — 확인용.</summary>
        public bool FitCardsDone { get; private set; }

        // ── 카드 글 판(마우스 오버 — 목업 r10-battle-hover) ──

        /// <summary>그 쪽(true = 내 쪽) 카드에 마우스를 올린 것처럼 글 판을 띄운다(캡처 · 시험). 그 쪽 카드가 없으면 false.</summary>
        public bool HoverCard(bool mine)
        {
            int k = mine ? 0 : 1;
            if (holders[k] == null || holders[k].childCount == 0 || !sideMons[k].Exists) return false;
            ShowCardTip(k);
            return CardTipSide == k;
        }

        void HideCardTip()
        {
            CardTipSide = -1;
            if (cardTip != null) cardTip.style.display = DisplayStyle.None;
        }

        // 기술(★ 고유 · 배운 기술 · 플랜 B · 알파 스트라이크 — 타입 · 위력 · 상대에게의 상성) · 균열 카드 · 비스트 부스트 · 지닌물건 · 상태의 이름과 글을 한 판에
        void ShowCardTip(int k)
        {
            var feed = infoFeed;
            var mon = sideMons[k];
            var state = feed != null ? feed.Source : null;
            if (page == null || state == null || !mon.Exists || mon.Id >= state.Pokemon.Count || state.Pokemon[mon.Id].Def < 0) return;
            var pk = state.Pokemon[mon.Id];
            var ctx = flow.Ctx;
            var inv = CultureInfo.InvariantCulture;
            if (cardTip == null)
            {
                cardTip = Kit.Panel(stage.Skin);
                cardTip.name = "battle-card-tip";
                cardTip.AddToClassList("sn-battle-cardtip");
                cardTip.pickingMode = PickingMode.Ignore;
                Below(cardTip);
                cardTip.RegisterCallback<GeometryChangedEvent>(e => FitTip());
            }
            cardTip.Clear();
            cardTip.style.width = StyleKeyword.Null;   // 폭은 USS 값부터(FitTip이 독에 닿으면 넓힌다)
            Add(cardTip, Kit.Wrapped(FullName(mon), Kit.F2), "battle-card-tip-name", "kit-title");
            var other = sideMons[1 - k];
            var otherTypes = other.Exists ? ctx.Cards.Pokemon[other.Def].Types : null;
            var moves = new List<int>();
            PokemonRules.MoveOptions(ctx, pk, moves);
            int signature = ctx.Cards.Pokemon[pk.Def].Signature, row = 0;
            foreach (int move in moves)
            {
                var head = TipRow("battle-card-tip-move-" + (row++).ToString(inv), BattleFeed.MoveNameKey(ctx, move), move == signature ? MonParts.Star : null, out var box);
                var mt = BattleRules.MoveType(ctx, move);
                if (mt != PokemonType.Typeless && move != BattleRules.AlphaStrike) Lead(head, () => TypeIcon(head, IconArt.Type(mt), stage.T("ui.types." + mt)));
                Add(head, new VisualElement(), null, "sn-battle-tipgap");
                head.Add(Kit.Badge(stage.T("screen.battle.power") + " " + CardStrength(ctx, move), move == signature ? KitBadgeKind.Gold : KitBadgeKind.Plain, Kit.F2));
                if (otherTypes != null && mt != PokemonType.Typeless)
                {
                    int bonus = TypeChart.Bonus(mt, otherTypes[0], otherTypes.Count > 1 ? otherTypes[1] : PokemonType.Typeless);
                    if (bonus != 0) Add(head, Kit.Badge(Signed(bonus), bonus > 0 ? KitBadgeKind.Ok : KitBadgeKind.Red, Kit.F2), k == 1 ? "battle-foe-move-bonus" : "battle-me-move-bonus", null);
                }
                if (move == BattleRules.AlphaStrike) AlphaTypeIcons(box, pk, k == 1 ? "battle-foe-alpha-types" : "battle-me-alpha-types");   // 결정 182: 어느 타입으로 올지 — 배울 수 있는 타입
                TipText(box, stage.TOrNull(BattleFeed.MoveNameKey(ctx, move) + ".text"));
            }
            for (int i = 0; i < pk.Breach.Length; i++)
            {
                if (pk.Breach[i] < 0 || (i < pk.Moves.Length && pk.Moves[i] >= 0) || pk.Breach[i] >= ctx.Decks.CardIds.Count) continue;
                string key = ctx.Decks.CardIds[pk.Breach[i]];
                TipRow("battle-card-tip-breach-" + i.ToString(inv), key, null, out var box);
                TipText(box, MonParts.BreachText(stage, key));
            }
            if (LegendRules.HasBeastBoost(pk))
            {
                TipRow("battle-card-tip-beast", MonParts.TacticKey(LegendTactic.BeastBoost), null, out var box);
                TipText(box, MonParts.TacticTip(stage, LegendTactic.BeastBoost));
            }
            if (pk.Owner == SeatId.Galactic)
            {
                // 갤럭시단 전술 판의 둘째 칸(카드 띠와 같다 — 쌓인 피해가 0이어도 효과 글과 함께, 목업 4차 Q12-3)
                int stored = feed.Kind == BattleKind.Galactic && k == 1 ? storedNow : 0;
                var head = TipRow("battle-card-tip-retribution", "screen.battle.retribution", null, out var box);
                if (stored > 0) ((Label)head[0]).text = stage.T("screen.battle.retribution.chip").Replace("{n}", stored.ToString(inv));
                TipText(box, stage.TOrNull("screen.battle.retribution.tip"));
            }
            int n = 0;
            var items = new List<int>();
            if (pk.Alpha) items.Add(CardMon.AlphaToken);
            if (pk.HeldItem >= 0) items.Add(pk.HeldItem);
            if (pk.HeldItem2 >= 0) items.Add(pk.HeldItem2);
            foreach (int item in items)
            {
                string key = item == CardMon.AlphaToken ? "item.alpha_pokemon" : ctx.Items.Items[item].Key;
                var head = TipRow("battle-card-tip-item-" + (++n).ToString(inv), key, null, out var box);
                string icon = item == CardMon.AlphaToken ? GenAssets.ItemTokens + "alpha_pokemon.png" : WorkUi.IconKey(ctx.Items.Items[item]);
                if (icon != null) Lead(head, () => Icon(head, icon));
                TipText(box, stage.TOrNull(key + ".text"));
            }
            n = 0;
            foreach (var status in new[] { mon.Status, mon.Status2 })
            {
                if (status == StatusEffect.None) continue;
                string key = MonParts.StatusKey(status);
                var head = TipRow("battle-card-tip-status-" + (++n).ToString(inv), key, null, out var box);
                Lead(head, () => TypeIcon(head, DsStage.StatusKey(status), null));
                TipText(box, stage.TOrNull(key + ".tip"));
            }
            CardTipSide = k;
            cardTip.style.display = DisplayStyle.Flex;
            PlaceBeside(cardTip, false);
        }

        // 글 판이 길어 독(화면 아래 판)에 닿으면 판을 넓혀 줄 수를 줄인다(영어 · 1080 벌 — 오른쪽 끝은 화면 안)
        void FitTip()
        {
            if (cardTip == null || CardTipSide < 0 || page == null) return;
            var dock = page.Q("battle-dock");
            var r = cardTip.worldBound;
            if (dock == null || float.IsNaN(r.height) || r.height <= 0f) return;
            float room = dock.worldBound.yMin - 4f - r.yMin;
            if (room <= 0f || r.height <= room) return;
            float maxW = page.worldBound.xMax - 8f - r.xMin;
            float want = Mathf.Min(maxW, Mathf.Ceil(r.width * r.height / room * 1.1f));
            if (want > r.width + 1f) cardTip.style.width = want;
        }

        // 글 판 한 줄: 머리(이름 — 앞 글자 「★」) + 아래 글 묶음
        VisualElement TipRow(string name, string key, string lead, out VisualElement box)
        {
            box = Add(cardTip, new VisualElement(), name, "sn-battle-tiprow");
            var head = Add(box, new VisualElement(), null, "sn-battle-tiphead");
            Add(head, Kit.Text((lead ?? "") + stage.T(key), Kit.F2), null, "sn-battle-tipname");
            return head;
        }

        // 머리 줄 맨 앞에 그림을 넣는다(그림이 없어 아무것도 붙지 않았으면 그대로)
        static void Lead(VisualElement head, Action add)
        {
            int before = head.childCount;
            add();
            if (head.childCount > before) head[head.childCount - 1].SendToBack();
        }

        static void TipText(VisualElement box, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Add(box, Kit.Wrapped(text, Kit.F2), null, "sn-battle-tiptext");
        }

        // 기둥 옆 판은 가운데 필드 바로 위 층에(독 · 결과 창 · 연출 덮개 아래)
        void Below(VisualElement el)
        {
            var field = page.Q("battle-field");
            int at = field != null ? page.IndexOf(field) : -1;
            if (at >= 0) page.Insert(at + 1, el);
            else page.Add(el);
        }

        /// <summary>기둥 자리가 바뀌면(창 크기) 옆 판을 다시 붙인다.</summary>
        void InfoMoved()
        {
            if (EntryOpen) PlaceBeside(entry, true);
            if (CardTipSide >= 0 && cardTip != null) PlaceBeside(cardTip, false);
        }

        /// <summary>화면을 닫을 때 기둥 몫을 비운다.</summary>
        void ShutInfo()
        {
            entry = entryCards = cardTip = null;
            entrySide = entryShape = -1;
            entryHover = false;
            CardTipSide = -1;
            holders[0] = holders[1] = null;
            swarmCards.Clear();
            infoFeed = null;
        }

        // 기둥 오른쪽에 붙여 놓는다(위 = 기둥 위 · 왼쪽 = 기둥 오른쪽 끝, 여백은 USS margin). full = 기둥 높이 그대로
        void PlaceBeside(VisualElement el, bool full)
        {
            if (info == null || page == null) return;
            var r = info.worldBound;
            if (float.IsNaN(r.width) || r.width <= 0f) return;
            var at = page.WorldToLocal(new Vector2(r.xMax, r.yMin));
            el.style.left = Mathf.Round(at.x);
            el.style.top = Mathf.Round(at.y);
            if (!full) return;
            // 무리 마우스 오버 판은 내용 높이(카드 바로 밑 꼬리표 — 검토 지적 5), [엔트리] 판은 기둥 높이
            el.style.height = el == entry && entryHover ? (StyleLength)StyleKeyword.Null : Mathf.Round(r.height);
            // 판 폭은 카드만큼(목업 10차 — 상대 두 장이면 좁다) · 가장 넓으면 화면 오른쪽 끝까지(왼쪽 기둥 여백과 같은 몫을 남긴다)
            float room = page.layout.width - r.xMin - at.x - el.resolvedStyle.marginLeft;
            if (!float.IsNaN(room) && room > 0f) el.style.maxWidth = Mathf.Floor(room);
        }

        // ── 엔트리 ──

        // 그 쪽의 엔트리(공개 값): 좌석이면 그 좌석 파티, 아니면(야생 · 무리 · 갤럭시단 · UB) 배틀 명단
        List<int> EntryIds(BattleFeed feed, bool mine)
        {
            var ids = new List<int>();
            var state = feed != null ? feed.Source : null;
            if (state == null || state.Battle == null) return ids;
            var side = state.Battle.Sides[mine ? feed.MySide : 1 - feed.MySide];
            if (side.Owner >= 0 && side.Owner < state.Seats.Length)
            {
                // 싸울 수 있는 칸만(pokemon_ranger B의 잠긴 칸은 배틀에 나오지 않는다 — PokemonRules.Fights)
                var seatState = state.Seats[side.Owner];
                for (int i = 0; i < seatState.Party.Length; i++)
                {
                    int id = seatState.Party[i];
                    if (id >= 0 && id < state.Pokemon.Count && state.Pokemon[id].Def >= 0 && PokemonRules.Fights(seatState, i) && !GainHidden(feed, id)) ids.Add(id);
                }
                if (side.Active >= 0 && !ids.Contains(side.Active)) ids.Insert(0, side.Active);
                // 연출이 아직 드러내지 않은 포획 · 빼앗기(그 쪽에서 빠진 포켓몬)는 드러날 때까지 엔트리에 남긴다
                foreach (var e in feed.Events)
                    if (e is CaptureAttempt c && c.Success && hidden.Contains(e) && c.Seat != side.Owner && !ids.Contains(c.TargetId) && c.TargetId >= 0 && c.TargetId < state.Pokemon.Count && state.Pokemon[c.TargetId].Def >= 0
                        && seen != null && c.TargetId == seen.Active[mine ? feed.MySide : 1 - feed.MySide]) ids.Add(c.TargetId);
            }
            else
            {
                foreach (int id in side.Roster)
                    if (id >= 0 && id < state.Pokemon.Count && state.Pokemon[id].Def >= 0) ids.Add(id);
                if (ids.Count == 0 && side.Active >= 0) ids.Add(side.Active);
            }
            return ids;
        }

        // 이 배틀에서 얻은 포켓몬(포획 · 빼앗기)인데 그 이벤트를 연출이 아직 드러내지 않았나 — 드러나기 전에는 엔트리 수 · 판에 세지 않는다
        bool GainHidden(BattleFeed feed, int id)
        {
            foreach (var e in feed.Events)
                if (e is PokemonGained g && g.PokemonId == id && hidden.Contains(e)) return true;
            return false;
        }

        /// <summary>그 쪽(true = 내 쪽)의 엔트리 수(머리 줄 단추의 n — 둘 이상일 때만 단추가 선다) — 확인용.</summary>
        public int EntryCount(bool mine) => EntryIds(flow.Battle, mine).Count;

        void RosterStats(BattleFeed feed)
        {
            RosterCount = RosterDone = 0;
            RosterActive = -1;
            var state = feed.Source;
            if (feed.Kind != BattleKind.Galactic || state == null || state.Battle == null) return;
            var side = state.Battle.Sides[1 - feed.MySide];
            if (side.Owner != SeatId.Galactic || side.Roster.Count < 2) return;
            RosterCount = side.Roster.Count;
            int active = seen.Active[1 - feed.MySide];
            for (int i = 0; i < side.Roster.Count; i++)
            {
                var mon = Shown(side.Roster[i]);
                if (!mon.Exists || mon.Fainted) RosterDone++;
                else if (side.Roster[i] == active) RosterActive = i;
            }
        }

        /// <summary>[내 엔트리] · [상대 엔트리]: 닫혀 있거나 다른 쪽이 열려 있으면 연다, 같은 쪽이 열려 있으면 닫는다(결정 176). 열렸으면 true.</summary>
        public bool ToggleEntry(bool mine)
        {
            if (entrySide == (mine ? 0 : 1) && !entryHover)
            {
                CloseEntry();
                return false;
            }
            return OpenEntry(mine, false);
        }

        /// <summary>엔트리 판을 연다(hover = 무리 카드에 마우스를 올려 연 판 — 포인터가 떠나면 닫힌다). 그 쪽 엔트리가 없으면 false.</summary>
        public bool OpenEntry(bool mine, bool hover)
        {
            if (stage == null || flow.Battle == null || EntryIds(flow.Battle, mine).Count == 0) return false;
            if (!hover && entrySide >= 0 && entryHover) entryHover = false;
            if (hover && entrySide >= 0 && !entryHover) return true;   // [엔트리]로 연 판은 마우스 오버가 바꾸지 않는다
            entrySide = mine ? 0 : 1;
            entryHover = hover;
            BuildEntry();
            Buttons();
            return EntryOpen;
        }

        /// <summary>엔트리 판을 닫는다([닫기] · Esc · 단추 다시 누르기). 닫았으면 true.</summary>
        public bool CloseEntry()
        {
            if (entrySide < 0) return false;
            entrySide = -1;
            entryHover = false;
            if (entry != null) entry.style.display = DisplayStyle.None;
            stage?.Tips.Hide();
            Buttons();
            return true;
        }

        void Buttons()
        {
            if (info == null) return;
            var mine = info.Q(EntryMineButton);
            var foe = info.Q(EntryFoeButton);
            mine?.EnableInClassList(EntryOnClass, entrySide == 0 && !entryHover);
            foe?.EnableInClassList(EntryOnClass, entrySide == 1 && !entryHover);
        }

        /// <summary>무리 카드(<c>battle-swarm-slot-n</c>, 1부터)에 마우스를 올린 것처럼 셋을 펼친 엔트리 판을 띄운다(캡처 · 시험). 그 칸이 없으면 false.</summary>
        public bool HoverSwarm(int n)
        {
            if (info == null || info.Q("battle-swarm-slot-" + n.ToString(CultureInfo.InvariantCulture)) == null) return false;
            return OpenEntry(false, true) && n <= entryShown.Count;
        }

        /// <summary>상대 엔트리 판(갤럭시단 명단)을 열고 n번째 카드(1부터)가 있나 — 옛 명단 줄 확인의 자리(결정 201 ① → 206 ②).</summary>
        public bool HoverRoster(int n)
        {
            if (RosterCount == 0 || n < 1 || n > RosterCount) return false;
            if (!(entrySide == 1 && !entryHover)) OpenEntry(false, false);
            return entry != null && entry.Q(EntryPanel + "-" + n.ToString(CultureInfo.InvariantCulture)) != null;
        }

        void BuildEntry()
        {
            entryShown.Clear();
            entryRatio.Clear();
            var feed = flow.Battle;
            if (entrySide < 0 || page == null || feed == null)
            {
                if (entry != null) entry.style.display = DisplayStyle.None;
                return;
            }
            if (flow.BattleOver)   // 결과 창이 뜨면 판을 걷는다(결과 창 아래 층이다)
            {
                CloseEntry();
                return;
            }
            bool mine = entrySide == 0;
            var ids = EntryIds(feed, mine);
            if (ids.Count == 0)
            {
                CloseEntry();
                return;
            }
            if (entry == null)
            {
                entry = Kit.Panel(stage.Skin);
                entry.name = EntryPanel;
                entry.AddToClassList("sn-battle-entry");
                Below(entry);
                entry.RegisterCallback<GeometryChangedEvent>(e => ReachLog());
            }
            entry.Clear();
            int shape = entrySide * 100 + ids.Count;
            if (shape != entryShape) entry.style.width = StyleKeyword.Null;   // 쪽 · 장수가 바뀌면 폭은 카드만큼부터 다시(ReachLog가 기록 기둥에 걸릴 때만 늘린다)
            entryShape = shape;
            var head = Add(entry, new VisualElement(), null, "sn-battle-entry-head");
            string owner = mine ? flow.SeatName(SideSeat(feed)) : ((Label)info?.Q("battle-foe-who"))?.text ?? "";
            Add(head, Kit.Text(stage.T("screen.battle.entry.title").Replace("{name}", owner), Kit.F2), "battle-entry-title", "kit-title");
            Add(head, Kit.Button(stage.T("screen.battle.entry.close"), KitButtonKind.Main, () => CloseEntry()), "battle-entry-close", null);   // 다른 판 [닫기]와 같은 색
            entry.EnableInClassList("sn-battle-entry-fit", entryHover);
            entryCards = Add(entry, new VisualElement(), "battle-entry-cards", "sn-battle-entry-cards");
            entryShown.AddRange(ids);
            if (entryW <= 0f) entryW = cardW > 0f ? cardW : GuessCardWidth();
            EntryCards();
            entryCards.RegisterCallback<GeometryChangedEvent>(e => FitEntry());
            entry.style.display = DisplayStyle.Flex;
            PlaceBeside(entry, true);
        }

        // 판 오른쪽 끝이 기록 기둥 가운데에 걸리면(기록 글이 반만 덮여 잘려 비친다) 기둥 오른쪽 끝까지 늘린다 — 기둥 앞에서 끝나면 그대로
        void ReachLog()
        {
            if (entry == null || logBox == null || entry.style.display == DisplayStyle.None) return;
            var r = entry.worldBound;
            var log = logBox.worldBound;
            if (float.IsNaN(r.width) || float.IsNaN(log.width) || log.width <= 0f) return;
            if (r.xMax <= log.xMin + 0.5f || r.xMax >= log.xMax - 0.5f) return;
            float w = Mathf.Ceil(log.xMax - r.xMin);
            entry.style.maxWidth = w;
            entry.style.width = w;
        }

        void EntryCards()
        {
            var feed = flow.Battle;
            if (entryCards == null || feed == null) return;
            entryCards.Clear();
            entryRatio.Clear();
            int side = entrySide == 0 ? feed.MySide : 1 - feed.MySide;
            int active = seen != null ? seen.Active[side] : -1;
            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < entryShown.Count; i++)
            {
                int id = entryShown[i];
                var col = Add(entryCards, new VisualElement(), EntryPanel + "-" + (i + 1).ToString(inv), "sn-battle-entry-col");
                var stack = MakeStack(feed, id, side, entryW);
                if (stack == null) continue;
                col.Add(stack.Root);
                stack.HookTips(TipSide.Below, entry);
                entryRatio.Add(stack.Art && stack.Height > 0f ? stack.Height / entryW : 0f);
                var mon = Shown(id);
                bool caught = Caught(feed, id);   // 잡힌 것(무리)은 「기절」이 아니라 「포획」
                bool fainted = caught || (mon.Exists && mon.Fainted) || (feed.Kind == BattleKind.Swarm && SwarmOver(feed, id));
                string tag = caught ? "screen.battle.entry.caught" : fainted ? "screen.info.fainted" : id == active ? "screen.battle.entry.active" : "screen.battle.entry.wait";
                var badge = Add(col, Kit.Badge(stage.T(tag), caught ? KitBadgeKind.Ok : fainted ? KitBadgeKind.Red : id == active ? KitBadgeKind.Gold : KitBadgeKind.Plain, Kit.F2), EntryPanel + "-" + (i + 1).ToString(inv) + "-tag", "sn-battle-entry-tag");
                badge.userData = tag;
                if (id == active && !fainted) col.AddToClassList(EntryNowClass);   // 「배틀 중」 카드는 노란 테(목업 10차)
                if (fainted)
                {
                    col.AddToClassList(RosterDoneClass);
                    stack.Root.AddToClassList("sn-card-out");
                }
            }
        }

        /// <summary>엔트리 판 카드 폭을 잴 때의 칸 수(판에 카드가 적어도 이 수만큼 들어갈 크기 — 파티 상한, 목업 10차 「여섯 장이 한 줄」).</summary>
        const int EntrySlots = 6;

        // 판 안 카드 폭: 여섯 장이 한 줄에 드는 크기(적으면 판이 좁아진다) · 쌓은 높이 + 꼬리표가 판 높이 안
        void FitEntry()
        {
            if (entry == null || entryCards == null || entryCards.panel == null || entryShown.Count == 0) return;
            var r = entryCards.layout;
            float room = entry.resolvedStyle.maxWidth.value - entry.resolvedStyle.paddingLeft - entry.resolvedStyle.paddingRight - entry.resolvedStyle.borderLeftWidth - entry.resolvedStyle.borderRightWidth;
            if (float.IsNaN(room) || room <= 0f || float.IsNaN(r.height) || r.height <= 0f) return;
            // 내용 높이 판(무리 마우스 오버)은 카드 칸 높이가 카드에서 나온다 — 높이 상한은 기둥 높이에서 판 머리 · 여백을 뺀 것
            if (entryHover && info != null && !float.IsNaN(entry.layout.height)) r.height = info.worldBound.height - (entry.layout.height - r.height);
            float gap = 0f, tag = 0f;
            if (entryCards.childCount > 0)
            {
                var first = entryCards[0];
                gap = first.resolvedStyle.marginRight + first.resolvedStyle.marginLeft + first.resolvedStyle.borderLeftWidth + first.resolvedStyle.borderRightWidth
                    + first.resolvedStyle.paddingLeft + first.resolvedStyle.paddingRight;   // 칸마다 「배틀 중」 테 자리
                var badge = first.Q(className: "sn-battle-entry-tag");
                if (badge != null && !float.IsNaN(badge.layout.height)) tag = badge.layout.height + badge.resolvedStyle.marginTop + badge.resolvedStyle.marginBottom;
            }
            int slots = Math.Max(entryShown.Count, EntrySlots);
            float w = (room - gap * slots) / slots;
            float ratio = 0f;
            foreach (float x in entryRatio) ratio = Math.Max(ratio, x);
            if (ratio > 0f) w = Math.Min(w, (r.height - tag - 2f - (entryCards.childCount > 0 ? entryCards[0].resolvedStyle.borderTopWidth + entryCards[0].resolvedStyle.borderBottomWidth + entryCards[0].resolvedStyle.paddingTop + entryCards[0].resolvedStyle.paddingBottom : 0f)) / ratio);
            w = Mathf.Floor(w);
            if (w < 8f || Math.Abs(w - entryW) < 1f) return;
            entryW = w;
            EntryCards();
        }

        /// <summary>엔트리 판의 n번째 카드(1부터)의 꼬리표 키(<c>screen.battle.entry.active</c> · <c>.wait</c> · <c>screen.info.fainted</c>) — 없으면 null(확인용).</summary>
        public string EntryTag(int n) => entry != null ? entry.Q(EntryPanel + "-" + n.ToString(CultureInfo.InvariantCulture) + "-tag")?.userData as string : null;
    }
}
