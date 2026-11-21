using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 포켓몬 도감(S42 — 사용자 결정 105 ③ · 「도감은 신오지방세대 도감으로」, 04 §3-14)의 확인 장면: 타이틀 줄 · 목록(왼쪽 큰 그림 + 오른쪽 굴러가는 판) · 덱 거르기 · 상세 쪽 · 다른 폼 ·
    /// 타입 거르기 · 빈 목록 · 보드 위 [도감] 단추와 게임 중 화면. 모든 장면이 글 넘침(<see cref="WorkUi.Overflows"/>)과 칸이 화면·띠 안에 드는지를 단정하고,
    /// <c>dex-all</c>은 모든 카드를 하나씩 고르고 상세 쪽(다른 폼까지)을 세워 본다. 그림은 <c>Logs/Review/dex/</c>에 모은다.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] DexScreens =
        {
            Screen("dex-title", s => DexTitle(s)),                                          // 타이틀의 「포켓몬 도감」 줄(고른 줄 · 설명 두 줄 · 메뉴 판이 설명 판과 겹치지 않는다)
            Screen("dex-list", s => DexOpen(s, null)),                                      // 타이틀 → 도감: 전체 · 전국도감 번호 순 · 첫 카드
            Screen("dex-list-mid", s => DexOpen(s, d => d.Pick(d.Shown.Count / 2))),        // 목록 가운데로 굴린 꼴(고른 판이 가운데 줄에서 튀어나온다)
            Screen("dex-list-end", s => DexOpen(s, d => d.Pick(d.Shown.Count - 1))),        // 목록 끝
            Screen("dex-deck-evolution", s => DexOpen(s, d => { d.PickDeck(DexDeck.Evolution); DexPickCopies(s, d); })),   // 진화 덱(장수 배지 ×2)
            Screen("dex-deck-galactic", s => DexOpen(s, d => { d.PickDeck(DexDeck.Galactic); d.Pick(d.Shown.Count - 1); d.OpenDetail(); })),   // 갤럭시단 상세: 「포획 불가」 · 트레이너 줄
            Screen("dex-detail", s => DexOpen(s, d => { d.PickDeck(DexDeck.Starter); d.Pick(1); d.OpenDetail(); })),   // 상세 쪽(스타팅 — 진화 줄 + 비용 · 든 덱)
            Screen("dex-detail-faces", s => DexOpen(s, d => { d.PickDeck(DexDeck.Legendary); DexPickFaces(s, d, 1); })),   // 전설 덱: 면이 여럿인 카드의 상세 + 다른 폼을 세움
            Screen("dex-filter", s => DexOpen(s, d => { d.PickDeck(DexDeck.Strong); d.PickType(PokemonType.Fire); })),   // 타입 거르기(불꽃) — 「n종 찾음」
            Screen("dex-empty", s => DexOpen(s, d => { d.PickDeck(DexDeck.Starter); d.PickType(PokemonType.Dragon); d.Find("zzz"); })),   // 맞는 카드 없음
            Screen("dex-all", s => DexOpen(s, d => DexAll(s, d))),                          // 모든 카드를 고르고 상세 쪽을 세워 글 넘침을 본다(끝 모습 = 마지막 카드의 상세 쪽)
            Screen("dex-hud", s =>                                                          // 보드 HUD 위 띠의 [도감] 단추 + 마우스 오버
            {
                var flow = ToBoard(s, BoardReadyMs);
                s.Panel.Tick(3);
                var button = flow.Stage.Root.Q("board-hud-dex") ?? throw new InvalidOperationException("[도감] 단추가 없다");
                Apart(flow.Stage.Root, "board-hud-dex", "board-hud-menu");
                flow.Stage.Tips.Show(button);
            }),
            Screen("dex-board", s =>                                                        // 게임 중(내 턴)에 연 도감: 화면을 덮는다
            {
                var flow = ToBoard(s, BoardReadyMs);
                if (!flow.OpenDex() || !(flow.Stage.Current is DexScreen dex) || !dex.InGame) throw new InvalidOperationException("게임 중 도감이 열리지 않았다");
                dex.PickDeck(DexDeck.Moderate);
                dex.Pick(12);
                DexCheck(s, flow);
            }),
        };

        static void DexTitle(UiShot s)
        {
            var flow = s.Flow();
            flow.OpenTitle(TitleScreen.Dex);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            if (root.Q("title-item-dex") == null) throw new InvalidOperationException("타이틀에 도감 줄이 없다");
            Apart(root, "title-menu", "title-desc");
        }

        static void DexOpen(UiShot s, Action<DexScreen> then)
        {
            var flow = s.Flow();
            flow.OpenTitle();
            flow.OpenTitleDex();
            var dex = flow.Stage.Current as DexScreen ?? throw new InvalidOperationException("도감이 열리지 않았다");
            then?.Invoke(dex);
            DexCheck(s, flow);
        }

        // 장수가 둘 이상인 첫 카드를 고른다
        static void DexPickCopies(UiShot s, DexScreen dex)
        {
            for (int i = 0; i < dex.Shown.Count; i++)
                if (dex.Shown[i].Copies > 1)
                {
                    dex.Pick(i);
                    return;
                }
            throw new InvalidOperationException("장수가 둘 이상인 카드가 없다");
        }

        // 면이 여럿인 첫 카드의 상세 쪽을 열고 그 n번째 면을 세운다
        static void DexPickFaces(UiShot s, DexScreen dex, int face)
        {
            var ctx = s.Board.Ctx;
            for (int i = 0; i < dex.Shown.Count; i++)
            {
                var faces = DexBook.Faces(ctx, dex.Shown[i].Def);
                if (faces.Count <= face) continue;
                if (!dex.Pick(i) || !dex.OpenDetail() || !dex.Face(faces[face]) || dex.Viewed != faces[face]) throw new InvalidOperationException("다른 폼을 세우지 못했다");
                return;
            }
            throw new InvalidOperationException("면이 여럿인 카드가 없다");
        }

        // 전체 목록의 카드를 하나씩 고르고(목록 꼴) 상세 쪽을 연다(다른 폼도) — 그때마다 글 넘침을 본다
        static void DexAll(UiShot s, DexScreen dex)
        {
            var ctx = s.Board.Ctx;
            int seen = 0;
            for (int i = 0; i < dex.Shown.Count; i++)
            {
                if (!dex.Pick(i)) throw new InvalidOperationException("카드를 고르지 못했다");
                if (i % 9 == 0) DexCheck(s, s.Made);   // 목록 꼴은 한 화면(아홉 줄)마다
                if (!dex.OpenDetail()) throw new InvalidOperationException("상세 쪽이 열리지 않았다");
                DexCheck(s, s.Made);
                foreach (int f in DexBook.Faces(ctx, dex.Shown[i].Def))
                {
                    if (f == dex.Shown[i].Def) continue;
                    dex.Face(f);
                    DexCheck(s, s.Made);
                    seen++;
                }
                seen++;
                if (i < dex.Shown.Count - 1) dex.Back();
            }
            Debug.Log("[UiShots] dex-all 카드 " + dex.Shown.Count + "장 · 면 " + seen + "개의 상세 쪽을 세웠다");
        }

        // 화면 틀이 화면 안 · 글이 잘리거나 겹치지 않는다 · 목록 판이 목록 안 · 띠의 칸들이 띠 안 · 상세 쪽의 칸이 상세 쪽 안
        static void DexCheck(UiShot s, GameFlow flow)
        {
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var page = root.Q("dex") ?? throw new InvalidOperationException("도감 화면 요소가 없다");
            var dex = (DexScreen)flow.Stage.Current;
            string who = dex.Viewed >= 0 ? flow.Ctx.Cards.Pokemon[dex.Viewed].Key : "-";
            DexInside(page.worldBound, root.Q("dex-panel").worldBound, "도감 틀", who);
            var band = root.Q(className: "sn-dex-band").worldBound;
            DexInside(band, root.Q("dex-pic").worldBound, "그림 칸", who);
            DexInside(band, root.Q("dex-open").worldBound, "[상세 보기]", who);
            var list = root.Q("dex-list");
            if (list != null)
            {
                if (dex.Detail) throw new InvalidOperationException("상세 쪽인데 목록이 떠 있다");
                DexInside(band, list.worldBound, "목록", who);
                DexInside(band, root.Q("dex-pos").worldBound, "자리 글", who);
                var inner = list.worldBound;
                int plates = 0;
                for (int i = 0; ; i++)
                {
                    var plate = root.Q("dex-plate-" + i);
                    if (plate == null) break;
                    DexInside(inner, plate.worldBound, "판 " + i, who);
                    plates++;
                }
                if (plates != Math.Min(DexScreen.Rows, dex.Shown.Count)) throw new InvalidOperationException("판 수 " + plates + "(카드 " + dex.Shown.Count + ")");
                if (dex.Picked >= 0 && !root.Q("dex-plate-" + (dex.Picked - dex.Top)).ClassListContains("sn-dex-plate-picked")) throw new InvalidOperationException("고른 판이 보이지 않는다");
            }
            var detail = root.Q("dex-detail");
            if (detail != null)
            {
                DexInside(band, detail.worldBound, "상세 쪽", who);
                foreach (var child in detail.Children()) DexInside(detail.worldBound, child.worldBound, "상세 쪽의 칸", who);
            }
            var bad = WorkUi.Overflows(page);
            if (bad.Count > 0) throw new InvalidOperationException("글자 넘침 " + bad.Count + "곳(" + who + "): " + string.Join(" / ", bad));
        }

        static void DexInside(Rect outer, Rect b, string what, string who)
        {
            if (b.width < 1 || b.xMin < outer.xMin - 0.5f || b.xMax > outer.xMax + 0.5f || b.yMin < outer.yMin - 0.5f || b.yMax > outer.yMax + 0.5f)
                throw new InvalidOperationException(what + "이(가) 밖으로 나갔다(" + who + ") " + b + " / " + outer);
        }
    }
}
