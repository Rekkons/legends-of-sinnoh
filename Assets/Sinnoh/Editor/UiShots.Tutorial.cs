using System;
using System.Collections.Generic;
using System.Text;
using Rekkon.UiKit;
using Sinnoh.Client;
using Sinnoh.Rules;
using Sinnoh.Rules.Tutorial;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// M6.5 확인 장면: 「마박사 그림」(<c>tut-rowan</c> — 그림 둘만 정수배로) · 「화면 ① 튜토리얼 층」(<c>tut-layer-*</c> — 본편 견본 각본 <c>main.json</c>을 실제 흐름에 띄운다, 07 §9-1).
    /// 층 장면은 보드 위 화면(<see cref="OverBoard"/>)이다: 가운데 모드(앵커 없는 [다음] 단계) · 앵커 요소 구멍(HUD [찾아보기]) · 보드 칸 구멍(<c>cell:32</c>) · 거절 hint · 앵커를 못 찾은 가운데 모드 · 봇 턴 띠(좌석 둘 각본) ·
    /// 말풍선 줄바꿈(각본 대사·hint가 모두 2줄 안에 드는지 — 07 T24, 넘치면 실패).
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] TutorialScreens =
        {
            Screen("tut-rowan", RowanSample),
            Screen("tut-layer-center", s => TutAt(s, "S001")),
            Screen("tut-layer-hole", s => TutAt(s, "S303")),
            Screen("tut-layer-cell", s => TutAt(s, "S302")),
            Screen("tut-layer-hint", TutHint),
            Screen("tut-layer-lost", TutLost),
            Screen("tut-layer-bot", TutBot),
            Screen("tut-layer-lines", TutLines),
            // 합침·잇기(07 T60~): 목차의 본편 장마다 첫 단계(장 고르기 = 빨리감기 → 화면에 넘김 → 층) · 7장은 이어 여는 각본 preview2·preview3의 첫 보이는 단계도
            Screen("tut-main-ch0", s => TutChapter(s, "main0")),
            Screen("tut-main-ch1", s => TutChapter(s, "main1")),
            Screen("tut-main-ch2", s => TutChapter(s, "main2")),
            Screen("tut-main-ch3", s => TutChapter(s, "main3")),
            Screen("tut-main-ch4", s => TutChapter(s, "main4")),
            Screen("tut-main-ch5", s => TutChapter(s, "main5")),
            Screen("tut-main-ch6", s => TutChapter(s, "main6")),
            Screen("tut-main-ch7", s => TutChapter(s, "main7")),
            Screen("tut-main-ch7-preview2", s => TutChapter(s, "main7", "preview2")),
            Screen("tut-main-ch7-preview3", s => TutChapter(s, "main7", "preview3")),
            // M6.5 마감(07 T160): 막 앞 판 확장 장의 처음 뜨는 말풍선 = 장 첫 말풍선(판 차리기 뒤로 옮김) — 손패 창이 열린 E1 · 혼자 E2 · 셋이서 E8
            Screen("tut-ext-e01", s => TutChapter(s, "e01")),
            Screen("tut-ext-e02", s => TutChapter(s, "e02")),
            Screen("tut-ext-e08", s => TutChapter(s, "e08")),
            Screen("tut-anchors", TutAnchors),
            // 클릭 경로·앵커 표(07 T130~T138): 고친 단계를 사람이 본 꼴로 — 앞 단계가 연 창(제작 · 마을 · 정비 · 손패 · 가방)은 미리 연다(자동 진행은 창을 열지 않는다)
            Screen("tut-click-s101", s => TutClick(s, "main", "S101")),
            Screen("tut-click-s104", s => TutClick(s, "main", "S104")),
            Screen("tut-click-s308", s => TutClick(s, "main", "S308")),
            Screen("tut-click-s309", s => TutClick(s, "main", "S309")),
            Screen("tut-click-s404", s => TutClick(s, "main", "S404", f => f.OpenCraft())),
            Screen("tut-click-s406", s => TutClick(s, "main", "S406", f => (f.Stage.Current as BattleScreen)?.OpenBag(), 1)),   // [가방]을 누른 뒤 — 볼 줄만 비춘다(결정 99 ①)
            Screen("tut-click-s407", s => TutClick(s, "main", "S407")),
            Screen("tut-click-s504", s => TutClick(s, "main", "S504", f => f.OpenVillage())),
            Screen("tut-click-s506", s => TutClick(s, "main", "S506", f => f.OpenParty())),
            Screen("tut-click-s610", s => TutClick(s, "main", "S610")),
            Screen("tut-click-s701", s => TutClick(s, "main", "S701")),
            Screen("tut-click-e01-s004", s => TutClick(s, "e01", "S004")),
            Screen("tut-click-e01-s007", s => TutClick(s, "e01", "S007", f => f.OpenFate())),
            Screen("tut-click-e01-s014", s => TutClick(s, "e01", "S014")),
            // 결정 99 ①(07 T180): 앵커 부분 여럿은 지금 누를 한 부분만 비추고, 누르면 다음 부분으로 — 둘째·셋째 부분이 비춰진 꼴(첫 부분은 위 tut-click-*)
            Screen("tut-part-s101-2", s => TutClick(s, "main", "S101", null, 1)),
            Screen("tut-part-s104-2", s => TutClick(s, "main", "S104", null, 1)),
            Screen("tut-part-s104-3", s => TutClick(s, "main", "S104", null, 2)),
            Screen("tut-part-s404-2", s => TutClick(s, "main", "S404")),
            Screen("tut-part-e01-s014-2", s => TutClick(s, "e01", "S014", null, 1)),
            Screen("tut-part-e01-s014-3", s => TutClick(s, "e01", "S014", null, 2)),
            Screen("tut-part-e10-s008-2", s => TutClick(s, "e10", "S008", f => f.OpenTrade(), 1)),
            // 결정 96 반영(M6.5 마감 질문 답): 마박사 = 작은 얼굴 · 전신은 장 첫 말풍선에만(Q2 다) · 봇 턴 띠의 봇 얼굴(Q5 나 — 용식 = Pt 라이벌 · 빛나 = Pt 여자 주인공) · 막 맛보기 7-2는 대사로만(Q9 나)
            Screen("tut-a96-full", s => TutBody(s, "main", "S301", true)),
            Screen("tut-a96-face", s => TutBody(s, "main", "S302", false)),
            Screen("tut-a96-face-center", s => TutBody(s, "main", "S002", false)),
            Screen("tut-a96-band-yongsik", s => TutBand(s, "e10", "S005", Sinnoh.Rules.Session.Protagonists.Rival)),
            Screen("tut-a96-band-bitna", s => TutBand(s, "e08", "S008", Sinnoh.Rules.Session.Protagonists.Dawn)),
            Screen("tut-a96-preview2-act", s => TutBody(s, "preview2", "S712", false)),
            Screen("tut-a96-preview2-lake", s => TutBody(s, "preview2", "S713", false)),
        };

        // 마박사 그림(결정 96 Q2 다 — 07 T172): 그 단계의 말풍선 옆이 전신인지 작은 얼굴인지, 그림 크기가 그 도트의 정수배인지, 구멍을 덮지 않는지
        static void TutBody(UiShot s, string script, string stepId, bool full)
        {
            var t = TutStart(s, TutorialScriptAsset(s.Board.Ctx, script), stepId);
            for (int i = 0; i < 4; i++) TutSettle(s);
            if (t.FullBody != full) throw new InvalidOperationException($"{script}.{stepId}: 마박사 전신 {t.FullBody}(바란 것 {full} — 전신은 장 첫 말풍선에만)");
            var tex = GenAssets.Load<Texture2D>(full ? GenAssets.RowanFull : GenAssets.RowanFace) ?? throw new InvalidOperationException("Gen 없음: 마박사 그림 — node Tools/assets/build.mjs --only tutorial");
            var rowan = t.Element.Q<Image>("tut-layer-rowan");
            int k = s.Grade.TokenHeight / 360;
            var box = rowan.worldBound;
            if (rowan.image != tex || Mathf.Abs(box.width - tex.width * k) > 0.5f || Mathf.Abs(box.height - tex.height * k) > 0.5f)
                throw new InvalidOperationException($"{script}.{stepId}: 마박사 그림이 {(full ? "전신" : "얼굴")} 도트의 ×{k}가 아니다({box.width}×{box.height} · 그림 {tex.width}×{tex.height})");
            Debug.Log($"[UiShots] tut-a96 {s.Grade.Height} {script}.{stepId}: 마박사 {(full ? "전신" : "얼굴")} {box.width}×{box.height} · 자리 {t.DockAt} · 구멍 {t.Holes.Count}");
            TutApart(s, t);
        }

        // 봇 턴 띠의 봇 얼굴(결정 96 Q5 나 — 07 T173·T174): 띠의 얼굴 상자 · 그 좌석 칩의 얼굴 · 좌석 인물이 모두 이름에 맞는 Pt 인물이고, 띠 글이 띠 안에 든다
        static void TutBand(UiShot s, string script, string stepId, string who)
        {
            var t = TutStart(s, TutorialScriptAsset(s.Board.Ctx, script), stepId);
            var flow = s.Made;
            int seat = t.Runner.Step.BotSeat;
            if (!t.BandShown) throw new InvalidOperationException($"{script}.{stepId}: 봇 턴 띠가 없다");
            if (t.BandFace != who || flow.Protagonist(seat) != who) throw new InvalidOperationException($"{script}.{stepId}: 봇 턴 띠의 얼굴 {t.BandFace ?? "없음"} · 좌석 인물 {flow.Protagonist(seat)}(바란 것 {who})");
            var chip = flow.Stage.Root.Q("board-hud-seat-" + (seat + 1));
            string chipFace = chip != null ? FaceBox.Shown(chip.Q(className: "sn-face")) : null;
            if (chipFace != who) throw new InvalidOperationException($"{script}.{stepId}: 좌석 칩의 얼굴 {chipFace ?? "없음"}(바란 것 {who})");
            var band = t.Element.Q("tut-layer-band").worldBound;
            var face = t.Element.Q("tut-layer-band-face").worldBound;
            var text = t.Element.Q("tut-layer-band-text").worldBound;
            if (face.yMin < band.yMin - 0.5f || face.yMax > band.yMax + 0.5f || text.yMin < band.yMin - 0.5f || text.yMax > band.yMax + 0.5f || face.xMax > text.xMin + 0.5f)
                throw new InvalidOperationException($"{script}.{stepId}: 띠의 얼굴·글이 띠 밖이거나 겹친다(띠 {band} · 얼굴 {face} · 글 {text})");
            Debug.Log($"[UiShots] tut-a96 {s.Grade.Height} {script}.{stepId}: 띠 「{t.BandText}」 얼굴 {t.BandFace} · 띠 높이 {band.height} · 좌석 {seat + 1} 칩 얼굴 {chipFace}");
            TutApart(s, t);
        }

        /// <summary>
        /// 고친 단계 하나(「클릭 경로·앵커 표」 묶음): 각본을 그 단계까지 진행기 자동 진행으로 보내고(<see cref="TutStart"/>) 사람이 그때 보고 있을 창을 <paramref name="prep"/>로 연 뒤
        /// 층의 자리 고르기(07 T133 — 프레임마다 해 본다)가 서도록 몇 번 보낸다. 구멍이 없거나 마박사·말풍선이 구멍을 덮으면 실패(<see cref="TutApart"/>), 로그 <c>[UiShots] tut-click</c>.
        /// </summary>
        static void TutClick(UiShot s, string script, string stepId, Action<GameFlow> prep = null, int press = 0)
        {
            var t = TutStart(s, TutorialScriptAsset(s.Board.Ctx, script), stepId);
            prep?.Invoke(s.Made);
            for (int i = 0; i < 4; i++) TutSettle(s);
            // 앞 부분을 누른 것으로 치고 다음 부분으로(결정 99 ① — 한 번에 한 부분만 비춘다)
            for (int i = 0; i < press; i++)
            {
                if (!t.PressPart()) throw new InvalidOperationException($"{script}.{stepId}: 부분 {t.Part + 1}({t.PartName})에서 다음 부분으로 옮기지 못했다(앵커 {t.Runner.Step.Anchor})");
                for (int k = 0; k < 4; k++) TutSettle(s);
            }
            Debug.Log($"[UiShots] tut-click {s.Grade.Height} {script}.{stepId}: 앵커 {t.Runner.Step.Anchor} · 비추는 부분 {t.Part + 1}({t.PartName}) · 구멍 {t.Holes.Count} · 자리 {t.DockAt} · 맨 위 {s.Made.Top?.Name ?? "-"}");
            if (t.Holes.Count == 0) throw new InvalidOperationException($"{script}.{stepId}: 구멍이 없다(앵커 {t.Runner.Step.Anchor})");
            // 한 번에 한 부분만(칸 여럿을 한 부분으로 적은 것만 구멍이 여럿)
            if (t.Holes.Count > 1 && !t.PartName.StartsWith(TutorialLayerAnchor.CellPrefix, StringComparison.Ordinal)) throw new InvalidOperationException($"{script}.{stepId}: 구멍이 {t.Holes.Count}개다 — 지금 누를 한 부분만 비춰야 한다(결정 99 ①)");
            TutApart(s, t);
        }

        /// <summary>
        /// 본편 각본 셋(main 0장 → 7장 → preview2 → preview3)을 화면 흐름에서 처음부터 끝 창까지 진행기 자동 진행으로 지나며 단계마다 앵커가 잡히는지 로그로 남긴다
        /// (<c>[UiShots] tut-anchor</c> 한 줄씩 — 잡힘 · 화면 전체(구멍이 화면을 다 덮는다) · 못 찾음(2초 뒤 가운데 모드). 「클릭 경로·앵커 표」 묶음 전의 확인 목록이라 실패로 치지 않는다).
        /// <c>screen:</c> 단계는 사람이 그 창을 연 것처럼 화면 흐름 함수로 연다(자동 진행은 창을 열지 않는다). 찍는 그림은 끝 창.
        /// </summary>
        static void TutAnchors(UiShot s)
        {
            var flow = s.Flow();
            flow.Layout(s.Width, s.Height);
            var tut = flow.Tutorial;
            if (!tut.Start("main0")) throw new InvalidOperationException("본편 0장을 시작하지 못했다");
            int found = 0, full = 0, lost = 0;
            for (int guard = 0; !tut.Ending; guard++)
            {
                if (guard > 200) throw new InvalidOperationException("본편이 끝나지 않는다: " + tut.Script?.Id + "." + tut.Runner?.Step?.Id);
                for (int i = 0; i < 3000 && !tut.Ending && !(tut.Layer != null && tut.Layer.Shown); i++)
                {
                    // 배틀 결과 창은 그 단계가 배틀 밖 앵커를 가리킬 때만 닫는다(사람도 그때 [확인]을 누른다 — 앵커 없는 단계 · battle-* 앵커는 결과 창 위에서 본다)
                    var next = tut.Runner?.Step;
                    if (flow.BattleOver && next != null && next.Anchor != null && !next.Anchor.StartsWith("battle-", StringComparison.Ordinal)) flow.CloseBattle();
                    Pass(flow, 100);
                }
                if (tut.Ending) break;
                TutSettle(s);
                var t = tut.Layer;
                var step = t.Runner.Step;
                if (step.Anchor != null)
                {
                    if (t.Holes.Count == 0)
                    {
                        for (int i = 0; i < TutorialLayer.LostMs / 100 + 2 && t.Holes.Count == 0; i++) Pass(flow, 100);
                        TutSettle(s);
                    }
                    var root = s.Panel.Root.worldBound;
                    bool whole = t.Holes.Count > 0 && t.Holes[0].width >= root.width - 1f && t.Holes[0].height >= root.height - 1f;
                    string mark = t.Holes.Count == 0 ? "못 찾음" : whole ? "화면 전체" : "잡힘";
                    if (t.Holes.Count == 0) lost++;
                    else if (whole) full++;
                    else found++;
                    string top = flow.Top != null ? flow.Top.Name : flow.Stage.Current != null ? flow.Stage.Current.Name : "-";
                    Debug.Log($"[UiShots] tut-anchor {s.Grade.Height} {tut.Script.Id}.{step.Id} {step.Anchor}: {mark}(구멍 {t.Holes.Count} · 맨 위 창 {top})");
                }
                if (step.Advance == StepAdvance.Screen)
                {
                    for (int i = 0; i < 3 && flow.Top != null; i++) flow.Escape();   // 앞 단계의 창(마을 등)을 닫고 연다 — 사람도 닫아야 한다
                    bool opened = step.Screen == "board-craft" ? flow.OpenCraft() : step.Screen == "board-village" ? flow.OpenVillage() : step.Screen == "board-party" && flow.OpenParty();
                    if (!opened) throw new InvalidOperationException($"{step.Id}: 창 {step.Screen}을 열지 못했다");
                    for (int i = 0; i < 50 && t.Runner.Step == step; i++) Pass(flow, 100);   // 층이 그 창을 보고 단계를 끝낸다
                    if (t.Runner.Step == step) throw new InvalidOperationException($"{step.Id}: 창 {step.Screen}을 열었는데 단계가 끝나지 않는다(맨 위 {flow.Top?.Name})");
                }
                else tut.Runner.Play();
            }
            Debug.Log($"[UiShots] tut-anchor {s.Grade.Height} 합계: 잡힘 {found} · 화면 전체 {full} · 못 찾음 {lost}");
            flow.Layout(s.Width, s.Height);
            flow.Stage.Tweens.Advance(BoardReadyMs);
        }


        public const string TutorialMainPath = "Assets/Sinnoh/Data/Json/tutorial/main.json";

        /// <summary>
        /// 목차의 그 장을 고르고(<see cref="TutorialMenu.Start"/> — 층이 붙는 한 길, 07 T60) 첫 단계가 뜰 때까지 보낸다. <paramref name="script"/>를 주면 그 각본(장이 이어 여는 각본)의
        /// 첫 보이는 단계까지 진행기 자동 진행으로 간다. 앵커를 못 찾으면 2초 보내 가운데 모드로 둔 채 찍고(경고 로그), 그 단계·앵커·자리·구멍 수·겹침을 로그 한 줄로 남긴다
        /// (<c>[UiShots] tut-main</c> — 앵커 표 묶음 전의 확인 목록이라 실패로 치지 않는다).
        /// </summary>
        static void TutChapter(UiShot s, string chapterId, string script = null)
        {
            var flow = s.Flow();
            flow.Layout(s.Width, s.Height);
            var tut = flow.Tutorial;
            if (!tut.Start(chapterId)) throw new InvalidOperationException("장을 시작하지 못했다: " + chapterId);
            TutSettle(s);
            for (int guard = 0; script != null && tut.Script.Id != script; guard++)
            {
                if (guard > 60) throw new InvalidOperationException($"{chapterId}: 각본 {script}이 열리지 않는다(지금 {tut.Script.Id} {tut.Runner.Step?.Id})");
                tut.Runner.Play();
                TutSettle(s);
            }
            var t = tut.Layer;
            var step = t.Runner.Step;
            if (step.Anchor != null && t.Holes.Count == 0)
            {
                for (int i = 0; i < TutorialLayer.LostMs / 100 + 2; i++) Pass(flow, 100);
                TutSettle(s);
            }
            string apart = null;
            try { TutApart(s, t); }
            catch (InvalidOperationException e) { apart = e.Message; }
            Debug.Log($"[UiShots] tut-main {s.Grade.Height} {chapterId} {tut.Script.Id}.{step.Id}: 앵커 {step.Anchor ?? "-"} · 구멍 {t.Holes.Count} · 가운데 {(t.Center ? "예(못 찾음)" : "아니오")} · 자리 {t.DockAt}" + (apart != null ? " · 겹침 " + apart : ""));
        }

        static void RowanSample(UiShot s)
        {
            var full = GenAssets.Load<Texture2D>(GenAssets.RowanFull) ?? throw new InvalidOperationException("Gen 없음: " + GenAssets.RowanFull + " — node Tools/assets/build.mjs --only tutorial");
            var face = GenAssets.Load<Texture2D>(GenAssets.RowanFace) ?? throw new InvalidOperationException("Gen 없음: " + GenAssets.RowanFace);
            int k = s.Grade.TokenHeight / 360, w = s.Grade.TokenHeight * 16 / 9, h = s.Grade.TokenHeight, gap = 16 * k;
            var root = s.Panel.Root;
            root.style.backgroundColor = (Color)new Color32(0xC8, 0xD0, 0xE0, 0xFF);
            int x = (w - (full.width + face.width) * k - gap) / 2, y = (h - full.height * k) / 2;
            root.Add(Art("tut-rowan-full", full, k, x, y));
            root.Add(Art("tut-rowan-face", face, k, x + full.width * k + gap, y));
        }

        static Image Art(string name, Texture2D tex, int k, int x, int y)
        {
            var img = new Image { name = name, image = tex, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            img.style.position = Position.Absolute;
            img.style.left = x;
            img.style.top = y;
            img.style.width = tex.width * k;
            img.style.height = tex.height * k;
            return img;
        }

        /// <summary>본편 각본(데이터 빌드가 만든 <c>Data/Json/tutorial/main.json</c>).</summary>
        internal static TutorialScript TutorialMain(Ctx ctx) => TutorialScriptAsset(ctx, "main");

        /// <summary>각본 하나(데이터 빌드가 만든 <c>Data/Json/tutorial/&lt;id&gt;.json</c>).</summary>
        internal static TutorialScript TutorialScriptAsset(Ctx ctx, string id)
        {
            string path = "Assets/Sinnoh/Data/Json/tutorial/" + id + ".json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path) ?? throw new InvalidOperationException("각본 없음: " + path + " — node Tools/data/build.mjs");
            return Sinnoh.Data.TutorialLoader.Load(asset.text, ctx, "tutorial/" + id + ".json");
        }

        // 각본을 목차와 같은 길(TutorialMenu.Play — 07 T60)로 띄우고 그 단계까지 진행기 자동 진행(같은 길 — 07 §6-4)으로 보낸 뒤 층이 그 단계를 띄울 때까지 보낸다
        static TutorialLayer TutStart(UiShot s, TutorialScript script, string stepId)
        {
            var flow = s.Flow();
            flow.Layout(s.Width, s.Height);
            var tut = flow.Tutorial;
            if (!tut.Play(script)) throw new InvalidOperationException("각본을 열지 못했다: " + script.Id);
            for (int guard = 0; tut.Runner.Step != null && tut.Runner.Step.Id != stepId; guard++)
            {
                if (guard > script.Steps.Count) throw new InvalidOperationException("단계 없음: " + stepId);
                TutSettle(s);
                // 앵커의 창 닫기 부분(07 T130 — 배틀 결과 [확인] · 창 [닫기])은 사람처럼 닫고 넘어간다(자동 진행은 창을 닫지 않는다)
                foreach (string part in TutorialLayerAnchor.Parts(tut.Runner.Step.Anchor))
                    if (part == "battle-result-ok") { if (flow.BattleOver) flow.CloseBattle(); }
                    else if (TutorialLayerAnchor.IsDismiss(part) && flow.Top != null) flow.Escape();
                tut.Runner.Play();
            }
            if (tut.Runner.Step == null) throw new InvalidOperationException("단계 없음: " + stepId);
            TutSettle(s);
            return tut.Layer;
        }

        // 지금 층이 지금 단계를 띄울 때까지(연출 대기열이 비고 입력이 열린다 — 각본이 끝났으면 목차가 다음 각본을 연다) 보내고, 배치를 세워 앵커를 다시 찾는다.
        // 배틀 결과 창은 [확인]으로 닫는다(마박사가 대신 둔 배틀 — 07 T63)
        static void TutSettle(UiShot s)
        {
            var flow = s.Made;
            var tut = flow.Tutorial;
            bool Up() => tut.Layer != null && tut.Layer.Shown;
            for (int i = 0; i < 3000 && !Up(); i++)
            {
                // 결과 창이 떠 있는 동안에는 그 창을 말하는 단계만 뜬다(결정 104 ① · 07 T190) — 나머지는 사람처럼 [확인]으로 닫아야 뜬다
                if (flow.BattleOver && (i > 600 || !TutorialLayerAnchor.InResult(tut.Runner?.Step?.Anchor))) flow.CloseBattle();
                Pass(flow, 100);
            }
            if (!Up()) throw new InvalidOperationException($"튜토리얼 층이 {tut.Script?.Id}.{tut.Runner?.Step?.Id}를 띄우지 않는다");
            for (int i = 0; i < 3; i++)
            {
                flow.Layout(s.Width, s.Height);
                s.Panel.Tick(2);
                Pass(flow, 16);
            }
        }

        static void TutAt(UiShot s, string stepId)
        {
            var t = TutStart(s, TutorialMain(s.Board.Ctx), stepId);
            var step = t.Runner.Step;
            if (step.Anchor == null && !(t.DockAt == "center" && t.Holes.Count == 0)) throw new InvalidOperationException($"{stepId}: 앵커 없는 [다음] 단계는 가운데 모드다(자리 {t.DockAt} · 구멍 {t.Holes.Count})");
            if (step.Anchor != null && t.Holes.Count == 0) throw new InvalidOperationException($"{stepId}: 앵커 「{step.Anchor}」의 구멍이 없다");
            TutApart(s, t);
        }

        // 거절 hint: [찾아보기] 단계에서 [턴 종료]를 낸다 → 거르개가 막고(RejectReason.Tutorial) 말풍선이 hint 글로 바뀐다
        static void TutHint(UiShot s)
        {
            var t = TutStart(s, TutorialMain(s.Board.Ctx), "S303");
            var flow = s.Made;
            int blocked = t.Runner.Blocked;
            flow.Hud.Press(BoardHud.EndTurn);
            Pass(flow, 16);
            if (t.Runner.Blocked != blocked + 1 || !t.Hinted) throw new InvalidOperationException($"거절 hint가 없다(막힘 {t.Runner.Blocked - blocked} · hint {t.Hinted})");
            if (t.ShownText != flow.Stage.T(t.Runner.Step.Hint)) throw new InvalidOperationException("말풍선이 hint 글이 아니다: " + t.ShownText);
            TutApart(s, t);
        }

        // 앵커를 못 찾은 단계: 2초 뒤 가운데 모드(경고 로그) — 이동력 단계의 앵커를 없는 이름으로 바꾼 각본
        static void TutLost(UiShot s)
        {
            var script = TutorialMain(s.Board.Ctx);
            script.Steps.Find(x => x.Id == "S301").Anchor = "no-such-anchor";
            var t = TutStart(s, script, "S301");
            for (int i = 0; i < 25; i++) Pass(s.Made, 100);
            if (!t.Center || t.DockAt != "center") throw new InvalidOperationException($"앵커를 못 찾았는데 가운데 모드가 아니다(자리 {t.DockAt})");
            TutSettle(s);
        }

        // 봇 턴 띠: 좌석 둘(나 · 용식) 각본 — 첫 마을 고르기가 나(0)인 시드, 셋업 뒤 봇 좌석의 턴 단계
        static void TutBot(UiShot s)
        {
            var ctx = s.Board.Ctx;
            ulong seed = 1;
            for (; seed < 200; seed++)
            {
                var g = Engine.NewGame(ctx, new RuleOptions(), 2, seed);
                Engine.Start(g, ctx, null);
                if (g.Pending.Count == 1 && g.Pending[0].Seat == 0) break;
            }
            int canalave = ctx.Fixtures.Ids.IndexOf("village.canalave"), other = canalave == 0 ? 1 : 0;
            var script = new TutorialScript { Id = "botband" };
            script.Start.Seats = 2;
            script.Start.Seed = seed;
            script.Start.TrainerCards = new[] { new[] { "hiker", "picnicker", "ace_trainer" }, new[] { "youngster", "lady", "fisherman" } };
            int lady = ctx.Cards.TrainerCardIds.IndexOf("lady"), hiker = ctx.Cards.TrainerCardIds.IndexOf("hiker");
            script.Bots = new[]
            {
                null,
                new List<AnswerPattern>
                {
                    AnswerPattern.Pick(ChoiceKind.VillagePick, other), AnswerPattern.Pick(ChoiceKind.StarterPick), AnswerPattern.Pick(ChoiceKind.TrainerCardPick, lady), AnswerPattern.Of(AnswerKind.EndTurn),
                },
            };
            TutorialStep Cmd(string id, params AnswerPattern[] auto)
            {
                var st = new TutorialStep { Id = id, Advance = StepAdvance.Command, Text = "tutorial.main.S101", Ref = { "07 §5-1" } };
                st.Allow.AddRange(auto);
                st.Auto.AddRange(auto);
                return st;
            }
            script.Steps.Add(Cmd("B001", AnswerPattern.Pick(ChoiceKind.VillagePick, canalave)));
            script.Steps.Add(new TutorialStep { Id = "B002", Advance = StepAdvance.Bot, BotSeat = 1, BotAnswers = 1, Text = "tutorial.main.S001" });
            script.Steps.Add(Cmd("B003", AnswerPattern.Pick(ChoiceKind.StarterPick), AnswerPattern.Pick(ChoiceKind.TrainerCardPick, hiker)));
            script.Steps.Add(new TutorialStep { Id = "B004", Advance = StepAdvance.Bot, BotSeat = 1, BotAnswers = 2, Text = "tutorial.main.S001" });
            script.Steps.Add(new TutorialStep { Id = "B005", Advance = StepAdvance.Bot, BotSeat = 1, BotAnswers = 1, Text = "tutorial.main.S306" });
            script.Steps.Add(Cmd("B006", AnswerPattern.Of(AnswerKind.EndTurn)));
            var t = TutStart(s, script, "B005");
            if (!t.BandShown || t.BandText != s.Made.Stage.T("screen.tutorial.layer.bot_turn").Replace("{name}", s.Made.Stage.T("screen.tutorial.layer.bot.1")))
                throw new InvalidOperationException($"봇 턴 띠가 없다(띠 {t.BandShown} 「{t.BandText}」 · 차례 좌석 {s.Made.Client.View.ActiveSeat} · 단계 {t.Runner.Step?.Id})");
            if (t.BandFace != Sinnoh.Rules.Session.Protagonists.Rival) throw new InvalidOperationException("봇 턴 띠의 얼굴이 용식(Pt 라이벌)이 아니다: " + (t.BandFace ?? "없음"));   // 결정 96 Q5 나
            TutApart(s, t);
        }

        // 말풍선 줄바꿈(07 T24 · T210): 각본 18개의 대사·hint를 하나씩 말풍선에 넣어 실제로 몇 줄이 되는지 센다 — 2줄을 넘으면 실패. 결과는 로그 [UiShots] tut-layer-lines 줄
        static void TutLines(UiShot s)
        {
            var t = TutStart(s, TutorialMain(s.Board.Ctx), "S001");
            var stage = s.Made.Stage;
            var label = t.Element.Q<Label>("tut-layer-text");
            var probe = Kit.Text("가", Kit.F2);
            label.parent.Add(probe);
            label.style.minHeight = 0;
            var report = new StringBuilder();
            var over = new List<string>();
            var keys = new List<string>();
            // 각본 18개 모두(본편 · 막 맛보기 둘 · 확장 15 — M9 「튜토리얼 잇기」: 영어 표 `-uiLang en`로도 같은 단정)
            var ids = new List<string> { "main", "preview2", "preview3" };
            for (int e = 1; e <= 15; e++) ids.Add("e" + e.ToString("00"));
            foreach (string id in ids)
                foreach (var st in TutorialScriptAsset(s.Board.Ctx, id).Steps)
                {
                    if (st.Text != null) keys.Add(st.Text);   // 말없는 판 차리기 단계는 뜨지 않는다(07 T160)
                    if (st.Hint != null) keys.Add(st.Hint);
                }
            int longest = 0;
            foreach (string key in keys)
            {
                label.text = KoText.JoinWords(stage.T(key));
                s.Panel.Tick(2);
                float line = probe.layout.height;
                int lines = Mathf.RoundToInt(label.layout.height / line);
                int chars = System.Text.RegularExpressions.Regex.Replace(stage.T(key), "<[^>]+>", "").Length;
                longest = Math.Max(longest, chars);
                report.Append(key).Append('=').Append(lines).Append("줄/").Append(chars).Append("자 ");
                if (lines > 2) over.Add($"{key} {lines}줄({chars}자)");
            }
            Debug.Log($"[UiShots] tut-layer-lines {s.Grade.Height}: {report}(가장 긴 {longest}자)");
            if (over.Count > 0) throw new InvalidOperationException("말풍선 2줄을 넘는다: " + string.Join(", ", over));
            probe.RemoveFromHierarchy();
            label.style.minHeight = StyleKeyword.Null;
            // 가장 긴 대사를 넣은 채 찍는다(줄바꿈 확인 그림)
            string widest = keys[0];
            foreach (string key in keys) if (stage.T(key).Length > stage.T(widest).Length) widest = key;
            label.text = KoText.JoinWords(stage.T(widest));
            s.Panel.Tick(2);
        }

        // 마박사·말풍선이 구멍을 덮지 않고, 말풍선이 화면 안에 있다
        static void TutApart(UiShot s, TutorialLayer t)
        {
            var bubble = t.Element.Q("tut-layer-bubble").worldBound;
            var rowan = t.Element.Q("tut-layer-rowan").worldBound;
            var root = s.Panel.Root.worldBound;
            if (bubble.xMin < root.xMin - 0.5f || bubble.yMin < root.yMin - 0.5f || bubble.xMax > root.xMax + 0.5f || bubble.yMax > root.yMax + 0.5f)
                throw new InvalidOperationException($"말풍선이 화면 밖으로 나간다({bubble} · 화면 {root})");
            foreach (var h in t.Holes)
                if (h.Overlaps(bubble) || h.Overlaps(rowan)) throw new InvalidOperationException($"마박사·말풍선이 구멍 {h}을 덮는다(말풍선 {bubble} · 마박사 {rowan})");
        }
    }
}
