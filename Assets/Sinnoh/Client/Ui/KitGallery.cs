using Rekkon.UiKit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 키트 갤러리: 공용 UI 키트의 기본 요소(창틀 판·단추·알약·배지·글자 크기·여러 줄 글·덮는 막·툴팁)를 한 장에 모은 확인용 화면(04 §9, plan M4-3 「PC 층 기반」).
    /// 게임 화면이 아니다 — 배치 캡처가 등급 넷으로 찍어 겉모양을 본다. 화면(S01~)은 이 요소들로 따로 만든다.
    /// 요소 이름(<c>gallery-…</c>)은 캡처·시험이 찾는 손잡이다.
    /// </summary>
    public static class KitGallery
    {
        public const string HealthIconKey = "Cards/Generator/stats_icons/health.png";
        public const string SpeedIconKey = "Cards/Generator/stats_icons/initiative.png";

        /// <summary>
        /// <paramref name="root"/>(패널을 덮는 요소)에 키트를 걸고 갤러리를 만든다. 돌려주는 툴팁에는 단추·알약이 걸려 있다 —
        /// 캡처는 <c>Show(root.Q("gallery-btn-end"))</c>처럼 올린 상태를 만든다.
        /// </summary>
        public static KitTooltip Build(VisualElement root, KitSkin skin, UiGrade grade)
        {
            Kit.Attach(root, skin, grade);
            var heart = GenAssets.Load<Texture2D>(HealthIconKey);
            var speed = GenAssets.Load<Texture2D>(SpeedIconKey);
            var tips = new KitTooltip(root, skin);

            var page = new VisualElement { name = "gallery" };
            page.style.position = Position.Absolute;
            page.style.left = page.style.top = page.style.right = page.style.bottom = 0;
            page.style.backgroundColor = new StyleColor(new Color32(0x58, 0x78, 0xA0, 0xFF));   // 목업 로비 바탕(판이 놓일 자리의 색)
            page.style.flexDirection = FlexDirection.Row;
            page.style.flexWrap = Wrap.Wrap;
            page.style.alignContent = Align.FlexStart;
            root.Insert(0, page);

            // 단추: 보통 · 주 행동 · 턴 종료 · 꺼짐
            var buttons = Section(page, skin, grade, "gallery-buttons", "단추");
            var row = Row(buttons);
            row.Add(Named(Kit.Button("가방"), "gallery-btn"));
            row.Add(Named(Kit.Button("이동", KitButtonKind.Main), "gallery-btn-main"));
            var end = Named(Kit.Button("턴 종료", KitButtonKind.End), "gallery-btn-end");
            row.Add(end);
            var off = Named(Kit.Button("교체"), "gallery-btn-off");
            off.SetEnabled(false);
            row.Add(off);
            tips.Hook(end, "턴 종료", "다음 플레이어에게 턴을 넘깁니다.", TipSide.Below);

            // 알약: 체력 · 스피드, 숫자 색 기본 · 낮음 · 높음
            var pills = Section(page, skin, grade, "gallery-pills", "체력 · 스피드");
            row = Row(pills, "kit-gap2");
            row.Add(Kit.Pill(heart, "8", KitTone.Base, "8"));
            var speedPill = Named(Kit.Pill(speed, "9", KitTone.Low), "gallery-pill-speed");
            row.Add(speedPill);
            row = Row(pills, "kit-gap2");
            var lowHeart = Named(Kit.Pill(heart, "3", KitTone.Low, "8"), "gallery-pill-heart-low");
            row.Add(lowHeart);
            row.Add(Kit.Pill(speed, "10"));
            row.Add(Kit.Pill(speed, "12", KitTone.High));
            tips.Hook(speedPill, "스피드", "기본 10 · 마비 −3 · 민첩의 띠 +2", TipSide.Above);   // 위에 자리가 없어 아래로 넘어간다
            tips.Hook(lowHeart, "체력", null, TipSide.Above);

            // 배지
            var badges = Section(page, skin, grade, "gallery-badges", "배지");
            row = Row(badges);
            row.Add(Kit.Badge("야생 배틀"));
            row.Add(Kit.Badge("약함", KitBadgeKind.Ok));
            row.Add(Kit.Badge("강함", KitBadgeKind.Warn));
            row.Add(Kit.Badge("내 배틀", KitBadgeKind.Red));
            row = Row(badges);
            row.Add(Kit.Badge("신오의 심장", KitBadgeKind.Gold));
            row.Add(Kit.Badge("엘리트 트레이너 · 실버", KitBadgeKind.Silver));
            row.Add(Kit.Badge("남은 이동력 3", KitBadgeKind.Gold, Kit.F2));

            // 글자 크기 셋과 여러 줄 글(낱말 가운데서 끊기지 않는다)
            var text = Section(page, skin, grade, "gallery-text", "글자");
            text.Add(Kit.Text("작은 글 12px: 스피드 10 (기본 8 · 민첩의 띠 +2)", Kit.F1));
            text.Add(Kit.Text("본문: 720 토큰 12px · 1080 토큰 24px", Kit.F2));
            text.Add(Kit.Text("큰 글 24px: 신오의 전설들", Kit.F3));
            text.Add(Named(Kit.Text("창 제목: 제작", Kit.FTitle), "gallery-title"));
            var wrapped = Named(Kit.Wrapped("졸음: 일반 타격 1개가 상태 이상 심볼로 바뀝니다. 최소 데미지 4: 효과 +2 · 상성 +2", Kit.F1), "gallery-wrapped");
            wrapped.AddToClassList("kit-mt4");
            wrapped.AddToClassList("kit-muted");
            text.Add(wrapped);

            // 덮는 막: 판 하나를 통째로 덮은 모습
            var scrimmed = Section(page, skin, grade, "gallery-scrim", "덮는 막");
            scrimmed.Add(Kit.Text("창 뒤 화면은 이만큼 어두워집니다.", Kit.F1));
            row = Row(scrimmed);
            row.Add(Kit.Button("확인", KitButtonKind.Main));
            row.Add(Kit.Badge("대기 중"));
            var scrim = new VisualElement { name = "gallery-scrim-cover", pickingMode = PickingMode.Ignore };
            scrim.AddToClassList("kit-scrim");
            scrimmed.Add(scrim);

            return tips;
        }

        static VisualElement Section(VisualElement page, KitSkin skin, UiGrade grade, string name, string title)
        {
            var panel = Kit.Panel(skin);
            panel.name = name;
            // 갤러리 자리 잡기: 720 토큰 기준 px를 토큰 벌에 맞춰 키운다(1080 토큰은 글자가 두 배라 판도 넓어야 한다)
            panel.style.width = 400 * grade.TokenHeight / 720;
            panel.style.marginLeft = panel.style.marginTop = 12 * grade.TokenHeight / 720;
            var head = Kit.Text(title, Kit.F2);
            head.AddToClassList("kit-title");
            panel.Add(head);
            page.Add(panel);
            return panel;
        }

        static VisualElement Row(VisualElement parent, string gap = "kit-gap4")
        {
            var row = new VisualElement();
            row.AddToClassList("kit-row");
            row.AddToClassList(gap);
            row.AddToClassList("kit-mt4");
            parent.Add(row);
            return row;
        }

        static T Named<T>(T element, string name) where T : VisualElement
        {
            element.name = name;
            return element;
        }
    }
}
