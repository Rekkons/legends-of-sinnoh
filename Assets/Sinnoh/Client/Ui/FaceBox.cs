using System;
using Rekkon.UiKit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 얼굴 상자(결정 54 ④·61 ③, 04 §3-6): 좌석 칩(보드·배틀 위 띠) · 넘김 띠 · 내 턴 컷신 · 배틀 기둥이 같이 쓰는 부품 하나.
    /// 그 좌석 주인공의 트레이너 앞모습 도트에서 얼굴 조각 30×30(Gen <c>Trainers/trainers.json</c>의 heroes 행 <c>face</c> — 추적 표 <c>Tools/assets/tables/hero_faces.json</c>)만 그린다.
    /// 조각은 정수배로만 늘린다(작은 상자 = 720 벌 ×1 · 1080 벌 ×2(결정 64), 큰 상자 = 720 벌 ×2 · 1080 벌 ×3 — USS <c>sn-face-art</c>). 좌석은 테두리 색(좌석 색)과 그림 오른쪽의 번호 배지(좌석 색 칸)로 구분한다 [가정].
    /// 겹: 상자(<c>sn-face</c> — 짙은 바깥 선, 턴인 좌석은 주황) → 안(<c>sn-face-in</c> — 좌석 색 테두리) → 그림(<c>sn-face-art</c>) + 번호 배지(<c>sn-face-seat</c>).
    /// 그림이 없으면(Gen 본체 없음·모르는 주인공) 안을 좌석 색으로 채우고 번호만 보인다.
    /// </summary>
    public static class FaceBox
    {
        [Serializable] sealed class Hero { public string key; public string file; public int[] face; }
        [Serializable] sealed class Table { public Hero[] heroes; }

        static Hero[] heroes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => heroes = null;

        static Hero Find(string protagonist)
        {
            if (protagonist == null) return null;
            if (heroes == null)
            {
                var json = GenAssets.Load<TextAsset>(BoardPieces.TableKey);
                if (json == null) return null;
                heroes = JsonUtility.FromJson<Table>(json.text).heroes ?? new Hero[0];
            }
            return Array.Find(heroes, h => h.key == protagonist);
        }

        /// <summary>그 주인공의 앞모습 그림 키(Gen 아래). 표에 없으면 null.</summary>
        public static string ArtKey(string protagonist)
        {
            var hero = Find(protagonist);
            return hero != null ? "Trainers/" + hero.file : null;
        }

        /// <summary>그 주인공의 얼굴 조각(그림 안 px, 왼쪽 위 원점). 표에 없으면 폭 0.</summary>
        public static RectInt Cut(string protagonist)
        {
            var hero = Find(protagonist);
            return hero != null && hero.face != null && hero.face.Length == 4 ? new RectInt(hero.face[0], hero.face[1], hero.face[2], hero.face[3]) : default;
        }

        /// <summary>빈 얼굴 상자를 만든다. big = 큰 상자(넘김 띠·내 턴 컷신).</summary>
        public static VisualElement Make(bool big = false)
        {
            var face = new VisualElement();
            face.AddToClassList("sn-face");
            if (big) face.AddToClassList("sn-face-big");
            var inside = new VisualElement();
            inside.AddToClassList("sn-face-in");
            face.Add(inside);
            var art = new Image { scaleMode = ScaleMode.StretchToFill };
            art.AddToClassList("sn-face-art");
            inside.Add(art);
            var seat = Kit.Text("", Kit.F2);
            seat.AddToClassList("sn-face-seat");
            inside.Add(seat);
            return face;
        }

        /// <summary>얼굴 상자를 그 좌석으로 칠한다: 주인공 얼굴 조각 + 좌석 색 테두리 + 좌석 번호 배지(번호 글자색은 좌석 색에 맞춰).</summary>
        public static void Paint(VisualElement face, int seat, Color32 color, string protagonist)
        {
            var inside = face.Q(className: "sn-face-in");
            var art = face.Q<Image>(className: "sn-face-art");
            var badge = face.Q<Label>(className: "sn-face-seat");
            var tone = new StyleColor((Color)color);
            inside.style.borderLeftColor = inside.style.borderRightColor = inside.style.borderTopColor = inside.style.borderBottomColor = tone;
            badge.text = (seat + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            badge.style.backgroundColor = tone;
            badge.style.color = new StyleColor((Color)SeatColors.Ink(color));

            var cut = Cut(protagonist);
            string key = cut.width > 0 ? ArtKey(protagonist) : null;
            var tex = key != null ? GenAssets.Load<Texture2D>(key) : null;
            art.image = tex;
            art.userData = tex != null ? protagonist : null;
            if (tex != null) art.sourceRect = new Rect(cut.x, cut.y, cut.width, cut.height);
            art.style.visibility = tex != null ? Visibility.Visible : Visibility.Hidden;
            inside.style.backgroundColor = tex != null ? new StyleColor(StyleKeyword.Null) : tone;
        }

        /// <summary>남 좌석 창 · 덮개 머리의 「누구」 표시(결정 120 — plan §5 #707): 마우스를 받지 않는 작은 얼굴 상자(두 벌 모두 ×1 — USS <c>sn-face-tag</c>). 이름은 <c>owner-face</c>.</summary>
        public static VisualElement Tag()
        {
            var face = Make();
            face.name = OwnerName;
            face.AddToClassList("sn-face-tag");
            face.pickingMode = PickingMode.Ignore;
            face.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            return face;
        }

        /// <summary>얼굴 상자 이름(남 좌석 창 · 덮개 머리 — 확인용).</summary>
        public const string OwnerName = "owner-face";

        /// <summary>남 좌석 창의 제목(또는 안내 줄) 앞에 그 좌석의 작은 얼굴 상자(좌석 색 테두리 · 번호 · 주인공 얼굴)를 붙여 한 줄(<c>sn-owner</c>)로 묶는다 — 제목이 있던 자리에 그 줄이 선다.</summary>
        public static VisualElement Owner(GameFlow flow, int seat, VisualElement title)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("sn-owner");
            var face = Tag();
            Paint(face, seat, flow.SeatColor(seat), flow.Protagonist(seat));
            row.Add(face);
            var parent = title.parent;
            if (parent != null) parent.Insert(parent.IndexOf(title), row);
            row.Add(title);
            return row;
        }

        /// <summary>그 얼굴 상자에 그려진 주인공(키). 그림이 없으면 null — 시험·확인이 본다.</summary>
        public static string Shown(VisualElement face) => face?.Q<Image>(className: "sn-face-art")?.userData as string;

        /// <summary>그 얼굴 상자의 좌석 색(테두리) — 시험·확인이 본다.</summary>
        public static Color32 SeatColorOf(VisualElement face) => (Color32)face.Q(className: "sn-face-in").style.borderTopColor.value;
    }
}
