using System;
using System.Collections.Generic;
using Sinnoh.Ai;
using Sinnoh.Client;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M9 다듬기 7(P22 · P23 · P31 — <c>Logs/Review/m9/polish/README.md</c>, 결정 116)의 장면.
    /// P31 = 배틀 결과 창 조건 줄의 작은 도장을 −8° 기울인다(스탬프 판 · 47행 덮개의 도장은 P12가 이미 기울였다).
    /// P22 = 사당 교환의 야생 카드 긴 목록 — 이름 찾기 · 타입 거르기 · 쪽 넘김 + 야생 카드 마우스 오버 카드(모습 · 스펙).
    /// P23 = Esc가 게임 메뉴 · 배틀 독의 가방 줄을 한 단계씩 닫는다(키 동작 — 그림은 닫힌 뒤의 화면).
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] Polish7Screens =
        {
            Screen("p23-menu-esc", s => MenuEsc23(s)),   // 게임 메뉴 → 나가기 물음 → Esc(메뉴) → [설정] → Esc(메뉴) → Esc(보드)
            Screen("p23-bag-esc", s => BagEsc23(s)),     // 배틀 [가방] → Esc(기술 줄) → Esc는 더 받지 않는다
            Screen("p31-result-seal", s => ResultSeal31(s)),   // 이긴 판의 결과 창 — 스탬프 조건 줄의 도장이 −8° 기울어 있다
            Screen("p22-list", s => Check22(s, Wild22(s))),   // 교환할 야생 카드 목록: 찾기 칸 · 타입 칩 줄 · 쪽 넘김(거르기 앞)
            Screen("p22-find", s => Find22(s)),               // 이름의 한 부분으로 찾은 때(목록이 줄고 쪽 수가 준다)
            Screen("p22-type", s => Type22(s)),               // 타입 칩 하나를 고른 때(그 타입의 카드만) → 고르고 [확정]이 켜진다
            Screen("p22-none", s => None22(s)),               // 맞는 카드가 없는 때(빈 줄 글 · [확정] 꺼짐)
            Screen("p22-hover", s => Hover22(s)),             // 야생 카드에 마우스를 올린 때: 모습 · 타입 · 체력 · 스피드 · 등급 · 포획률 · 고유 기술 카드
        };

        // P22: 동등한 교환의 「새 포켓몬」 목록(좌석 둘 판 — 적당한 등급 이하의 야생 카드 90여 장)
        static ShrineScreen Wild22(UiShot s)
        {
            var first = ShrineSeats(s, 2, ShrineScreen.Mode.Exchange, null, Sinnoh.Rules.ShrineRules.EquivalentExchange);
            if (!first.PickOption(0) || !first.Confirm()) throw new InvalidOperationException("교환할 포켓몬 고르기가 거절됐다");
            ShrineSettle(s, s.Made, f => f.Stage.Current is ShrineScreen sh && sh.Shown == ShrineScreen.Mode.ExchangeCard && sh.Open);
            var shrine = (ShrineScreen)s.Made.Stage.Current;
            s.Panel.Tick(3);
            if (!shrine.Filtering || shrine.Pages < 2) throw new InvalidOperationException("긴 야생 카드 목록에 찾기 줄 · 쪽 넘김이 없다(답 " + shrine.Options.Count + ")");
            if (shrine.ShownOptions.Count != shrine.Options.Count) throw new InvalidOperationException("거르기 앞인데 줄이 빠졌다");
            return shrine;
        }

        // 판이 화면 안 · 아래 판 위에 있고, 목록 줄이 판 안에 들고, 글이 넘치지 않는다
        static void Check22(UiShot s, ShrineScreen shrine)
        {
            s.Panel.Tick(3);
            var root = s.Made.Stage.Root;
            var panel = root.Q("board-shrine-panel");
            var box = panel.worldBound;
            float dock = root.Q("board-hud-dock").worldBound.yMin;
            if (box.yMax > dock + 0.5f || box.xMax > root.worldBound.xMax + 0.5f) throw new InvalidOperationException("사당 판이 아래 판을 덮거나 화면 밖으로 나간다(" + box.yMax + " > " + dock + ")");
            foreach (var e in panel.Q("board-shrine-list").Children())
                if (e.worldBound.xMin < box.xMin - 0.5f || e.worldBound.xMax > box.xMax + 0.5f) throw new InvalidOperationException("목록 줄 " + e.name + "이 판 밖으로 나간다");
            var types = panel.Q("board-shrine-types");
            if (types == null || panel.Q("board-shrine-find") == null) throw new InvalidOperationException("찾기 칸 · 타입 칩 줄이 없다");
            foreach (var e in types.Children())
                if (e.worldBound.xMax > box.xMax + 0.5f) throw new InvalidOperationException("타입 칩 " + e.name + "이 판 밖으로 나간다");
            int rows = 0;
            foreach (var e in panel.Q("board-shrine-list").Children()) if (e.name != null && e.name.StartsWith("board-shrine-opt-", StringComparison.Ordinal)) rows++;
            int want = Math.Min(shrine.PageRows, shrine.ShownOptions.Count - shrine.Page * shrine.PageRows);
            if (rows != want) throw new InvalidOperationException("목록 줄 " + rows + " ≠ 이 쪽의 답 " + want);
            var bad = WorkUi.Overflows(panel);
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
            Debug.Log($"[UiShots] p22 {s.Grade.Height}: 답 {shrine.Options.Count} · 보임 {shrine.ShownOptions.Count} · 쪽 {shrine.Page + 1}/{shrine.Pages} · 타입 칩 {types.childCount - 1}");
        }

        static bool Shown22(ShrineScreen shrine, int index)
        {
            foreach (int i in shrine.ShownOptions) if (i == index) return true;
            return false;
        }

        static void Find22(UiShot s)
        {
            var shrine = Wild22(s);
            var flow = s.Made;
            // 넷째 답의 화면 이름 앞 두 글자로 찾는다(두 언어 공통 — 이름의 한 부분)
            string name = flow.Stage.T("pokemon." + flow.Ctx.Cards.Pokemon[shrine.Options[3].Target].Key);
            string part = name.Substring(0, Math.Min(2, name.Length));
            if (!shrine.Find(part)) throw new InvalidOperationException("찾기가 거절됐다");
            s.Panel.Root.Q<TextField>("board-shrine-find").SetValueWithoutNotify(part);   // 사람이 친 글 그대로(칸은 다시 만들어지지 않는다)
            if (shrine.ShownOptions.Count == 0 || shrine.ShownOptions.Count >= shrine.Options.Count || !Shown22(shrine, 3)) throw new InvalidOperationException("「" + part + "」로 찾은 줄 " + shrine.ShownOptions.Count);
            foreach (int i in shrine.ShownOptions)
            {
                var def = flow.Ctx.Cards.Pokemon[shrine.Options[i].Target];
                if (flow.Stage.T("pokemon." + def.Key).IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0 && def.Name.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) throw new InvalidOperationException("찾는 글이 없는 카드가 보인다: " + def.Key);
            }
            if (!shrine.PickOption(3)) throw new InvalidOperationException("찾은 카드 고르기가 거절됐다");
            if (shrine.Search != part || s.Panel.Root.Q<TextField>("board-shrine-find").value != part) throw new InvalidOperationException("고른 뒤 찾는 글이 사라졌다");
            if (!s.Panel.Root.Q<Button>("board-shrine-go").enabledSelf) throw new InvalidOperationException("찾은 카드를 골랐는데 [확정]이 꺼져 있다");
            Check22(s, shrine);
        }

        static void Type22(UiShot s)
        {
            var shrine = Wild22(s);
            var flow = s.Made;
            var type = flow.Ctx.Cards.Pokemon[shrine.Options[0].Target].Types[0];
            if (!shrine.PickType(type)) throw new InvalidOperationException("타입 거르기가 거절됐다");
            if (shrine.ShownOptions.Count == 0 || shrine.ShownOptions.Count >= shrine.Options.Count) throw new InvalidOperationException(type + " 타입으로 거른 줄 " + shrine.ShownOptions.Count);
            foreach (int i in shrine.ShownOptions)
            {
                bool has = false;
                foreach (var t in flow.Ctx.Cards.Pokemon[shrine.Options[i].Target].Types) has |= t == type;
                if (!has) throw new InvalidOperationException(type + " 타입이 아닌 카드가 보인다");
            }
            var chip = s.Panel.Root.Q("board-shrine-type-" + type.ToString().ToLowerInvariant());
            if (chip == null || !chip.ClassListContains("sn-shrine-chip-picked") || s.Panel.Root.Q("board-shrine-type-all").ClassListContains("sn-shrine-chip-picked")) throw new InvalidOperationException("고른 타입 칩이 칠해지지 않았다");
            if (!shrine.PickOption(shrine.ShownOptions[0])) throw new InvalidOperationException("거른 카드 고르기가 거절됐다");
            if (shrine.TypeFilter != type) throw new InvalidOperationException("고른 뒤 타입 거르기가 풀렸다");
            Check22(s, shrine);
        }

        static void None22(UiShot s)
        {
            var shrine = Wild22(s);
            if (!shrine.PickOption(1)) throw new InvalidOperationException("야생 카드 고르기가 거절됐다");
            const string part = "zzzz";
            if (!shrine.Find(part)) throw new InvalidOperationException("찾기가 거절됐다");
            s.Panel.Root.Q<TextField>("board-shrine-find").SetValueWithoutNotify(part);
            s.Panel.Tick(3);
            if (shrine.ShownOptions.Count != 0 || s.Panel.Root.Q("board-shrine-none") == null) throw new InvalidOperationException("맞는 카드가 없는데 빈 줄 글이 없다");
            if (s.Panel.Root.Q<Button>("board-shrine-go").enabledSelf || shrine.Confirm()) throw new InvalidOperationException("걸러져 안 보이는 카드로 [확정]이 됐다");
            Check22(s, shrine);
        }

        static void Hover22(UiShot s)
        {
            var shrine = Wild22(s);
            var flow = s.Made;
            int index = shrine.ShownOptions[5];
            if (!shrine.HoverMon(index)) throw new InvalidOperationException("마우스 오버 카드가 뜨지 않았다");
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var tip = root.Q("mon-tip");
            if (tip == null || tip.resolvedStyle.display == DisplayStyle.None || tip.resolvedStyle.visibility != Visibility.Visible) throw new InvalidOperationException("마우스 오버 카드가 보이지 않는다");
            if (shrine.MonShown != shrine.Options[index].Target) throw new InvalidOperationException("다른 포켓몬의 카드가 떴다");
            Rect box = tip.worldBound, panel = root.Q("board-shrine-panel").worldBound, all = root.worldBound;
            if (box.xMin < all.xMin - 0.5f || box.yMin < all.yMin - 0.5f || box.xMax > all.xMax + 0.5f || box.yMax > all.yMax + 0.5f) throw new InvalidOperationException("마우스 오버 카드가 화면 밖으로 나간다");
            if (box.Overlaps(panel)) throw new InvalidOperationException("마우스 오버 카드가 사당 판을 덮는다");
            var def = flow.Ctx.Cards.Pokemon[shrine.MonShown];
            // 결정 206 ①(목업 B-wild): 원작 카드 앞면(이름 · 스펙 · 고유 기술이 카드에 인쇄돼 있다) + 카드에 없는 포획률 칩
            if (tip.Q("card-face") == null || tip.Q("card-strip-1") != null) throw new InvalidOperationException("야생 카드 앞면이 없다(" + def.Key + " — 도트 카드로 대신했거나 띠가 붙었다)");
            var rate = tip.Q("mon-tip-rate") ?? throw new InvalidOperationException("카드에 포획률 칩이 없다");
            if (rate.worldBound.yMin < tip.Q("card-face").worldBound.yMax - 0.5f) throw new InvalidOperationException("포획률 칩이 카드 앞면 아래에 있지 않다");
            var bad = WorkUi.Overflows(tip);
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + string.Join(" / ", bad));
            Check22(s, shrine);
            Debug.Log($"[UiShots] p22-hover {s.Grade.Height}: {def.Key} 체력 {def.Health} · 스피드 {def.Initiative} · 카드 {box.width}x{box.height}");
        }

        // P31: 결과 창 조건 줄의 도장(StampScreen.Seal — USS sn-stamp-icon)이 −8° 돌아가 있고 줄 · 글은 그대로(P19의 단정을 그대로 지난다)
        static void ResultSeal31(UiShot s)
        {
            StampReward19(s);
            var cond = s.Made.Stage.Root.Q("battle-result-stamp-cond") ?? throw new InvalidOperationException("결과 창에 스탬프 조건 줄이 없다");
            var seal = cond.Q(className: "sn-stamp-icon") ?? throw new InvalidOperationException("조건 줄에 도장이 없다");
            float deg = seal.resolvedStyle.rotate.angle.ToDegrees();
            if (Mathf.Abs(deg + 8f) > 0.01f) throw new InvalidOperationException("도장의 기울기 " + deg + "° ≠ −8°");
            Debug.Log($"[UiShots] p31-result-seal {s.Grade.Height}: 도장 {deg}°");
        }

        static void MenuEsc23(UiShot s)
        {
            var flow = ToBoard(s, BoardReadyMs);
            flow.OpenMenu();
            var menu = flow.Stage.Current as BoardMenuScreen ?? throw new InvalidOperationException("게임 메뉴가 열리지 않았다");
            menu.AskQuit();
            if (!flow.Escape() || menu.Asking || !ReferenceEquals(flow.Stage.Current, menu)) throw new InvalidOperationException("Esc가 나가기 물음만 물리지 않았다");
            menu.OpenSettings();
            if (!flow.Escape() || !(flow.Stage.Current is BoardMenuScreen)) throw new InvalidOperationException("Esc가 설정에서 메뉴로 돌아가지 않았다");
            if (!flow.Escape() || flow.Stage.Current != null || flow.MenuUp) throw new InvalidOperationException("Esc가 게임 메뉴를 닫지 않았다");
            Debug.Log($"[UiShots] p23-menu-esc {s.Grade.Height}: 물음 → 메뉴 → 설정 → 메뉴 → 보드");
        }

        static void BagEsc23(UiShot s)
        {
            var flow = Trail(s, SoloTrail.Catch, f => Battle(f, b => b.BagOn));
            var battle = (BattleScreen)flow.Stage.Current;
            int commands = flow.Session.CommandCount;
            if (!battle.OpenBag() || flow.Stage.Root.Q("battle-bag-back") == null) throw new InvalidOperationException("[가방]이 열리지 않았다");
            if (!flow.Escape() || battle.BagOpen || flow.Stage.Root.Q("battle-bag-back") != null) throw new InvalidOperationException("Esc가 가방 줄을 닫지 않았다");
            if (!ReferenceEquals(flow.Stage.Current, battle) || flow.Escape()) throw new InvalidOperationException("줄이 닫힌 배틀 화면이 Esc를 받았다");
            if (flow.Session.CommandCount != commands) throw new InvalidOperationException("Esc가 답을 냈다");
            Debug.Log($"[UiShots] p23-bag-esc {s.Grade.Height}: 가방 줄 → 기술 줄(기술 {battle.Moves.Count})");
        }
    }
}
