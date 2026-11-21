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
    /// 맵 결과 연출 층(M5 「맵 결과·배틀 안 연출」 — 04 §8-8 23·26~30·80행): 보드 HUD 위에 얹는 판 하나. 숨겨진 장소 결과의 꼬리표(「빨강 규토리 +3」·「야영지」·「떨어진 별 5개」·
    /// 「우두머리 포켓몬!」·유물 이름) · 날아가는 그림(규토리 · 별 · 유물 토큰) · 토큰 둘레의 빛(우두머리 붉은 섬광 · 유물 빛기둥) · 떨어진 별 4·8개째의 띠 빛 ·
    /// 전설 장소의 유물 공명(23행) · 신 격파의 「한 번 더!」(80행)를 그린다. 값은 모두 지금 항목과 그 안 시각에서만 나온다(<see cref="SpotResultBeats"/> · <see cref="GodBeats"/> · <see cref="LegendSiteBeats"/>).
    /// 토큰 자체(부풀기 · 사라짐)는 보드의 <c>BoardTokens</c>가 그린다(<c>GameFlow.RaiseSpot</c>).
    /// 요소 이름: <c>board-result-fx</c> · <c>board-result-label</c> · <c>board-result-fly-&lt;i&gt;</c> · <c>board-result-glow</c> · <c>board-result-pillar</c> · <c>board-result-band</c> · <c>board-result-banner</c>.
    /// </summary>
    public sealed class ResultFx
    {
        public const int MaxFly = 6;

        /// <summary>720 화면 px: 꼬리표가 떠오르는 높이 · 규토리가 튀는 높이·퍼지는 간격 · 날아가는 그림의 포물선 높이 · 땅울림.</summary>
        public const float RisePx = 8f, HopPx = 26f, SpreadPx = 18f, ArcPx = 60f, ShakePx = 3f, LabelAbovePx = 34f;

        readonly GameFlow flow;
        readonly List<Image> fly = new List<Image>();
        readonly VisualElement label, labelIcon, glow, pillar, band, banner, moved;
        readonly Label labelText, labelSub, bannerText, movedText;

        public readonly VisualElement Element;

        /// <summary>지금 보이는 꼬리표의 글(없으면 null) · 날아가는 그림 수 · 빛(0 ~ 1) · 「한 번 더!」가 보이나 — 확인용.</summary>
        public string LabelShown { get; private set; }
        public string SubShown { get; private set; }
        public int FlyShown { get; private set; }
        public float GlowShown { get; private set; }
        public bool BannerShown { get; private set; }

        /// <summary>1막 해방의 「이동력 +2」 칩(94행 941)이 보이나 · 그 자리(이 층의 좌표) · 자리로 간 만큼(0 = 마을 칸 위 ~ 1 = 그 좌석의 자리) — 확인용.</summary>
        public bool MoveShown { get; private set; }
        public Vector2 MoveAt { get; private set; }
        public float MoveFly { get; private set; }

        /// <summary>꼬리표에 유물 그림이 보이나(결정 80 L14 나) — 확인용.</summary>
        public bool LabelIconShown { get; private set; }

        public ResultFx(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Element = new VisualElement { name = "board-result-fx", pickingMode = PickingMode.Ignore };
            Element.AddToClassList("sn-result-fx");
            band = Box("board-result-band", "sn-result-band");
            pillar = Box("board-result-pillar", "sn-result-pillar");
            glow = Box("board-result-glow", "sn-result-glow");
            for (int i = 0; i < MaxFly; i++)
            {
                var img = new Image { name = "board-result-fly-" + i.ToString(CultureInfo.InvariantCulture), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                img.AddToClassList("sn-result-fly");
                Element.Add(img);
                FxUi.Fade(img, 0f);
                fly.Add(img);
            }
            label = Box("board-result-label", "sn-result-label");
            // 이름 줄: (유물이면) 유물 그림 + 이름(결정 80 L14 나)
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("sn-result-label-head");
            label.Add(head);
            labelIcon = new VisualElement { name = "board-result-label-icon", pickingMode = PickingMode.Ignore };
            labelIcon.AddToClassList("sn-icon");
            head.Add(labelIcon);
            labelText = Badge(head, "board-result-label-text", "sn-result-label-text", KitBadgeKind.Plain, Kit.F2);
            labelSub = Badge(label, "board-result-label-sub", "sn-result-label-sub", KitBadgeKind.Gold, Kit.F2);
            banner = Box("board-result-banner", "sn-result-banner");
            bannerText = Badge(banner, "board-result-banner-text", "sn-result-banner-text", KitBadgeKind.Gold, Kit.F3);
            motes = new ScatterMotes(Element, "board-result-mote-");
            moved = Box("board-result-move", "sn-result-banner");
            movedText = Badge(moved, "board-result-move-text", "sn-result-banner-text", KitBadgeKind.Gold, Kit.F3);
        }

        readonly ScatterMotes motes;

        /// <summary>빛으로 흩어지는 알갱이 수(80행 신 격파 — P28, 확인용).</summary>
        public int MotesShown => motes.Shown;

        VisualElement Box(string name, string cls)
        {
            var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            Element.Add(e);
            FxUi.Fade(e, 0f);
            return e;
        }

        // 키트 배지(글자 높이·테두리는 키트 토큰 — 직접 테두리를 두르면 고정 높이 안에서 글이 잘린다)
        static Label Badge(VisualElement parent, string name, string cls, KitBadgeKind kind, string size)
        {
            var t = Kit.Badge("", kind, size);
            t.name = name;
            t.pickingMode = PickingMode.Ignore;
            t.AddToClassList(cls);
            parent.Add(t);
            return t;
        }

        float Unit => flow.Stage.Grade.TokenHeight >= 1080 ? 1.5f : 1f;

        readonly Dictionary<string, Vector2> centres = new Dictionary<string, Vector2>(StringComparer.Ordinal);

        // HUD 요소의 가운데(이 층의 좌표). 다시 지은 요소가 아직 배치 전이면 앞 자리(HUD는 답마다 다시 그려진다 — 첫 프레임에는 배치가 없다)
        Vector2? Centre(string name)
        {
            var root = Element.parent;
            var e = root != null ? root.Q(name) : null;
            var box = e != null ? e.worldBound : default(Rect);
            if (e == null || float.IsNaN(box.width) || box.width <= 0f) return centres.TryGetValue(name, out var was) ? was : (Vector2?)null;
            var at = Element.WorldToLocal(box.center);
            centres[name] = at;
            return at;
        }

        /// <summary>날아가는 그림이 닿는 곳(이 층의 좌표) · 그 HUD 자리(위 띠 · 내 판)를 읽었나 — 확인용.</summary>
        public Vector2 FlyTo { get; private set; }
        public bool TargetFound { get; private set; }

        /// <summary>화면 대기열의 지금 순간으로 보낸다(HUD가 떠 있을 때 <c>GameFlow.ShowFx</c>가 부른다 — 처음 부를 때 HUD 판 위에 얹는다).</summary>
        public void Show(FxSequencer seq, VisualElement hudRoot)
        {
            if (hudRoot != null && Element.parent != hudRoot) hudRoot.Add(Element);
            LabelShown = SubShown = null;
            LabelIconShown = false;
            FlyShown = 0;
            GlowShown = 0f;
            BannerShown = false;
            bool spot = ShowSpot(seq);
            bool legend = !spot && ShowLegend(seq);
            if (!spot && !legend)
            {
                foreach (var f in fly) FxUi.Fade(f, 0f);
                FxUi.Fade(label, 0f);
                FxUi.Fade(glow, 0f);
                FxUi.Fade(pillar, 0f);
                FxUi.Fade(band, 0f);
            }
            ShowGod(seq);
            ShowFreed(seq);
        }

        // 1막 해방(94행 941 — M9 다듬기 P18): 조무래기 토큰이 날아간 마을 칸 위에 「이동력 +2」 칩이 튀어나와 머물다 그 좌석의 자리(내 것 = 남은 이동력 배지 · 남 = 좌석 칩)로 날아가 붙는다
        void ShowFreed(FxSequencer seq)
        {
            MoveShown = false;
            MoveFly = 0f;
            var step = LiberateBeats.StepOf(seq != null ? seq.Current : null);
            var gone = LiberateBeats.Removed(step);
            if (gone == null || seq.LocalMs < step.StartMs || seq.LocalMs >= step.EndMs || !(flow.CellPoint(gone.Cell, out var from) || flow.PiecePoint(gone.Seat, out from)))
            {
                FxUi.Fade(moved, 0f);
                return;
            }
            LiberateBeats.Chip(seq.LocalMs - step.StartMs, step.Ms, out float show, out float scale, out float fly);
            float unit = Unit;
            if (movedText.text.Length == 0) movedText.text = flow.Stage.T("screen.board.goal.move_bonus");
            from.y -= 70f * unit;
            bool mine = flow.Client != null && gone.Seat == flow.Client.Seat;
            var to = Centre(mine ? "board-hud-moves" : "board-hud-seat-" + (gone.Seat + 1).ToString(CultureInfo.InvariantCulture)) ?? from;
            var at = Vector2.Lerp(from, to, fly);
            FxUi.Move(moved, at.x, at.y);
            FxUi.Scale(moved, scale, scale);
            FxUi.Fade(moved, show);
            MoveShown = show > 0f;
            MoveAt = at;
            MoveFly = fly;
        }

        // 숨겨진 장소 결과(26~30행)
        bool ShowSpot(FxSequencer seq)
        {
            var entry = seq != null ? seq.Current : null;
            var step = ResultRows.SpotStep(entry);
            var e = ResultRows.EventOf<SpotResolved>(step);
            if (e == null || seq.LocalMs < step.StartMs || seq.LocalMs >= step.EndMs) return false;
            int t = seq.LocalMs - step.StartMs;
            float unit = Unit;
            // 토큰이 떠 있는 자리 = 그 좌석 말의 머리 위(보드 없이 도는 흐름이면 화면 가운데 위)
            if (!flow.PiecePoint(e.Seat, out var head)) return false;
            var token = head - new Vector2(0f, 10f * unit);
            var pose = SpotResultBeats.At(e, t, step.Ms);
            var ctx = flow.Ctx;
            string key = e.SpotKey >= 0 && e.SpotKey < ctx.Items.Items.Count ? ctx.Items.Items[e.SpotKey].Key : null;
            var inv = CultureInfo.InvariantCulture;
            var stage = flow.Stage;

            // 꼬리표
            string text = null, sub = null;
            switch (e.Result)
            {
                case SpotResult.Stash:
                    HiddenSpotRules.Stash(ctx, e.SpotKey, out var color, out _);
                    text = stage.T("screen.board.spot.stash").Replace("{color}", stage.T(BoardLog.ApricornKey((int)color))).Replace("{n}", e.Count.ToString(inv));
                    break;
                case SpotResult.Camp:
                    text = stage.T("screen.board.spot.camp");
                    sub = stage.T("screen.board.spot.camp.sub");
                    break;
                case SpotResult.Star:
                    text = stage.T("screen.board.spot.star").Replace("{n}", e.Count.ToString(inv));
                    if (e.Count > 0 && e.Count % 4 == 0) sub = stage.T("screen.board.spot.star.bonus").Replace("{n}", (e.Count / 4).ToString(inv));
                    break;
                case SpotResult.Alpha:
                    text = stage.T("screen.board.spot.alpha");
                    if (pose.Count > 0) sub = stage.T("screen.board.spot.alpha.hp").Replace("{n}", pose.Count.ToString(inv));
                    break;
                case SpotResult.AlphaWarded:
                    text = stage.T("screen.board.spot.alpha");
                    sub = stage.T("screen.board.spot.alpha.warded");
                    break;
                case SpotResult.Relic:
                    text = key != null ? stage.T(key) : "";
                    sub = RelicLine(e.SpotKey);
                    break;
            }
            if (labelText.text != text) labelText.text = text ?? "";
            if (labelSub.text != (sub ?? "")) labelSub.text = sub ?? "";
            labelSub.style.display = string.IsNullOrEmpty(sub) ? DisplayStyle.None : DisplayStyle.Flex;
            var relicIcon = e.Result == SpotResult.Relic ? GenAssets.Load<Texture2D>(GenAssets.RelicIconKey(key)) : null;
            labelIcon.style.backgroundImage = relicIcon != null ? new StyleBackground(relicIcon) : new StyleBackground(StyleKeyword.None);
            labelIcon.style.display = relicIcon != null ? DisplayStyle.Flex : DisplayStyle.None;
            LabelIconShown = relicIcon != null && pose.Label > 0f;
            float shake = pose.Shake * ShakePx * unit;
            FxUi.Move(label, token.x + shake, token.y - (LabelAbovePx + pose.Rise * RisePx) * unit);
            FxUi.Fade(label, pose.Label);
            if (pose.Label > 0f) { LabelShown = text; SubShown = sub; }

            // 날아가는 그림: 규토리(말 쪽으로 모인다) · 별(위 띠로) · 유물(내 판으로)
            int n = SpotResultBeats.Flyers(e);
            Texture2D art = null;
            Vector2 to = head;
            if (e.Result == SpotResult.Stash && HiddenSpotRules.Stash(ctx, e.SpotKey, out var c2, out _)) art = GenAssets.Load<Texture2D>(EncounterScreen.ApricornKey(c2));
            else if (e.Result == SpotResult.Star)
            {
                art = GenAssets.Load<Texture2D>("Board/Icons/hidden_spot_fallen_star.png");
                var status = Centre("board-hud-status");
                TargetFound = status != null;
                to = status ?? new Vector2(head.x, 24f);
            }
            else if (e.Result == SpotResult.Relic)
            {
                art = GenAssets.Load<Texture2D>(GenAssets.SpotTokenKey(key));
                var panel = Centre(e.Seat == flow.Client.Seat ? "board-hud-party" : "board-hud-seat-" + (e.Seat + 1).ToString(inv));
                TargetFound = panel != null;
                to = panel ?? (e.Seat == flow.Client.Seat ? new Vector2(head.x, head.y + 300f) : head);
            }
            FlyTo = to;
            for (int i = 0; i < fly.Count; i++)
            {
                var img = fly[i];
                if (i >= n || art == null) { FxUi.Fade(img, 0f); continue; }
                var p = SpotResultBeats.At(e, t, step.Ms, i);
                if (p.FlyShow <= 0f) { FxUi.Fade(img, 0f); continue; }
                img.image = art;
                Vector2 from = token;
                float lift;
                if (e.Result == SpotResult.Stash)
                {
                    // 터지며 양옆으로 튀어 내려앉았다가 말 쪽으로 모인다
                    float side = (i % 2 == 0 ? -1f : 1f) * (i / 2 + 1);
                    float outT = Mathf.Clamp01((t - SpotResultBeats.PopMs - SpotResultBeats.EachMs * i) / 300f);
                    from = token + new Vector2(side * SpreadPx * unit * outT, 16f * unit * outT);
                    lift = p.Arc * HopPx * unit;
                }
                else lift = p.Arc * ArcPx * unit;
                var at = Vector2.Lerp(from, to, p.Fly) - new Vector2(0f, lift);
                FxUi.Move(img, at.x, at.y);
                float s = e.Result == SpotResult.Stash ? 1f : Mathf.Lerp(1.6f, 0.9f, p.Fly) * (p.Fly >= 1f ? p.Pop : 1f);   // 별·유물은 토큰 크기에서 줄어들며 난다
                FxUi.Scale(img, s, s);
                FxUi.Fade(img, p.FlyShow);
                FlyShown++;
            }

            // 토큰 둘레의 빛: 우두머리 = 붉은 고리 · 유물 = 빛기둥
            glow.EnableInClassList("sn-result-glow-red", e.Result == SpotResult.Alpha);
            FxUi.Move(glow, token.x + shake, token.y);
            FxUi.Fade(glow, e.Result == SpotResult.Alpha ? pose.Glow : 0f);
            float ring = e.Result == SpotResult.Alpha ? 1f + 0.5f * pose.Glow : 1f;
            FxUi.Scale(glow, ring, ring);
            FxUi.Move(pillar, token.x, token.y);
            FxUi.Fade(pillar, e.Result == SpotResult.Relic ? pose.Glow * 0.8f : 0f);
            // 떨어진 별 4·8개째: 위 띠의 상태 칸 둘레가 한 번 빛난다
            var bandAt = Centre("board-hud-status");
            if (bandAt != null) FxUi.Move(band, bandAt.Value.x, bandAt.Value.y);
            FxUi.Fade(band, bandAt != null ? pose.Band : 0f);
            GlowShown = Mathf.Max(e.Result == SpotResult.Alpha || e.Result == SpotResult.Relic ? pose.Glow : 0f, pose.Band);
            return true;
        }

        // 유물의 둘째 줄: 위력이 오르는 타입(지닐 때) — 천상의 유물은 막는 오라의 신
        string RelicLine(int item)
        {
            var ctx = flow.Ctx;
            var stage = flow.Stage;
            var types = RelicRules.TypesOf(ctx, item);
            if (types != null && types.Count > 0)
            {
                var names = new List<string>();
                foreach (var t in types) names.Add(stage.T("ui.types." + t));
                return stage.T("screen.board.spot.relic.types").Replace("{types}", string.Join("·", names));
            }
            return RelicRules.GodOf(ctx, item) != SinnohGod.None ? stage.T("screen.board.spot.relic.celestial") : null;
        }

        // 전설 장소 찾아보기(23행 — 조우 전환의 앞): 내 판에서 유물이 칸으로 날아가 공명하고 땅이 한 번 울린다
        bool ShowLegend(FxSequencer seq)
        {
            var entry = seq != null ? seq.Current : null;
            var step = LegendSiteBeats.StepOf(entry, out var started);
            if (step == null || seq.LocalMs < step.StartMs || seq.LocalMs >= step.StartMs + LegendSiteBeats.Ms) return false;
            int seat = started.Sides != null && started.Sides.Length > 0 && started.Sides[0] != null ? started.Sides[0].Owner : -1;
            if (seat < 0 || !flow.PiecePoint(seat, out var head)) return false;
            int t = seq.LocalMs - step.StartMs;
            LegendSiteBeats.At(t, out float go, out float lit, out float quake);
            float unit = Unit;
            int relic = SiteRelic(started.Cell, seat);
            var art = relic >= 0 ? GenAssets.Load<Texture2D>(GenAssets.SpotTokenKey(flow.Ctx.Items.Items[relic].Key)) : null;
            for (int i = 0; i < fly.Count; i++) FxUi.Fade(fly[i], 0f);
            var at = head + new Vector2(quake * ShakePx * unit, 0f);
            if (art != null)
            {
                var from = seat == flow.Client.Seat ? Centre("board-hud-party") ?? new Vector2(head.x, head.y + 300f) : head;
                var p = Vector2.Lerp(from, at, go) - new Vector2(0f, Mathf.Sin(go * Mathf.PI) * ArcPx * unit);
                fly[0].image = art;
                FxUi.Move(fly[0], p.x, p.y);
                FxUi.Scale(fly[0], 1f, 1f);
                FxUi.Fade(fly[0], 1f);
                FlyShown = 1;
            }
            glow.EnableInClassList("sn-result-glow-red", false);
            FxUi.Move(glow, at.x, at.y);
            FxUi.Scale(glow, 1f + 0.6f * lit, 1f + 0.6f * lit);
            FxUi.Fade(glow, lit);
            FxUi.Fade(pillar, 0f);
            FxUi.Fade(band, 0f);
            FxUi.Fade(label, 0f);
            GlowShown = lit;
            return true;
        }

        // 그 칸의 전설 장소를 여는 유물 가운데 그 좌석이 가진 첫 것(없으면 −1 — 전설 사당·신은 유물이 없다)
        int SiteRelic(int cell, int seat)
        {
            var view = flow.Client != null ? flow.Client.View : null;
            if (view == null) return -1;
            foreach (var f in flow.Ctx.Fixtures.Fixtures)
            {
                if (f.Kind != FixtureKind.LegendarySite || f.Cell != cell || f.Artifact == null) continue;
                foreach (int item in f.Artifact)
                    if (RelicRules.Owns(view, flow.Ctx, seat, item)) return item;
            }
            return -1;
        }

        // 신 격파(80행): 시작의 사당 위에서 빛이 흩어지고 「한 번 더!」
        void ShowGod(FxSequencer seq)
        {
            var step = GodBeats.StepOf(seq != null ? seq.Current : null);
            var gone = GodBeats.Removed(step);
            if (gone == null || seq.LocalMs < step.StartMs || seq.LocalMs >= step.EndMs || !flow.CellPoint(gone.Cell, out var at))
            {
                if (gone == null || !(seq.LocalMs >= step.StartMs && seq.LocalMs < step.EndMs) || !flow.PiecePoint(gone.Seat, out at))
                {
                    FxUi.Fade(banner, 0f);
                    motes.Show(-1, 0f, 0f, 1f);
                    return;
                }
            }
            motes.Show(seq.LocalMs - step.StartMs, at.x, at.y - ScatterMotes.AbovePx * Unit, Unit);   // 신 피규어가 빛으로 흩어진다(P28 — 피규어는 GameFlow.SyncFigures가 줄인다)
            GodBeats.At(seq.LocalMs - step.StartMs, step.Ms, out float burst, out float show, out float scale);
            float unit = Unit;
            if (bannerText.text.Length == 0) bannerText.text = flow.Stage.T("screen.board.god.again");
            FxUi.Move(banner, at.x, at.y - 70f * unit);
            FxUi.Scale(banner, scale, scale);
            FxUi.Fade(banner, show);
            glow.EnableInClassList("sn-result-glow-red", false);
            FxUi.Move(glow, at.x, at.y - 20f * unit);
            FxUi.Scale(glow, 1f + 2f * (1f - burst), 1f + 2f * (1f - burst));
            FxUi.Fade(glow, burst);
            GlowShown = Mathf.Max(GlowShown, burst);
            BannerShown = show > 0f;
        }
    }
}
