using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 묶음 표지(결정 66 ①, 04 §3-1 「한 칸에 말 여럿」): 한 칸에 말이 둘 이상이면 세워 둔 말의 머리 위(좌석 번호 핀 위 — <see cref="BoardPieces.PinTop"/>)에 뜨는 동그란 표지.
    /// 안에는 그 칸의 말들이 한 명씩 돌아가며 보이고(그림 <see cref="StackArt"/> — 정수배 도트, 테두리 = 그 말의 좌석 색. 누가 보이나는 시각만 받는 <see cref="PieceStacks.Shown"/>),
    /// 오른쪽 위 밖에 사람 수 배지(「×3」)가 붙는다(옆 칸으로 걸어 나가는 말의 번호 핀과 맞닿지 않는 높이). 보드 HUD의 가장 아래 층에 그려 HUD 판·창이 표지를 덮는다.
    /// 마우스를 받지 않는다(요소마다 <c>PickingMode.Ignore</c>) — 표지가 칸 고르기·휠을 막지 않는다. 마우스가 표지 위인지는 사각형으로만 센다(<see cref="CellAt"/> —
    /// 그 칸의 공개 정보 카드가 뜬다).
    /// 요소 이름: <c>board-stacks</c>·<c>board-stack-&lt;칸&gt;</c>·<c>board-stack-art</c>·<c>board-stack-count</c>.
    /// </summary>
    public sealed class StackBadges
    {
        /// <summary>핀과 표지 사이, 표지와 그 위에 띄우는 것(「!」·뜬 숨겨진 장소 토큰) 사이의 틈(패널 px).</summary>
        public const float Gap = 2f;
        /// <summary>사람 수 배지의 머리 기호(「×3」 — 글이 아니라 기호라 문자열 표에 두지 않는다).</summary>
        public const string CountMark = "×";

        sealed class Badge
        {
            public VisualElement Root;
            public Image Art;
            public Label Count;
            public int Cell = -1, Seat = -1, Total;
            public Rect Rect;
            public bool On;
        }

        readonly UiStage stage;
        readonly VisualElement layer;
        readonly List<Badge> badges = new List<Badge>();

        public VisualElement Element => layer;

        public StackBadges(UiStage stage)
        {
            this.stage = stage;
            layer = new VisualElement { name = "board-stacks", pickingMode = PickingMode.Ignore };
            layer.AddToClassList("sn-stacks");
        }

        /// <summary>세워 둔 말의 그림 양식으로 본 표지의 한 변(패널 px) — 머리 위에 띄우는 것들이 이만큼 더 올라간다.</summary>
        public static float Size(BoardPieces.Piece stand, int tokenHeight, int zoomStep) => stand != null ? StackArt.Size(stand) * StackArt.Mul(stand.Style, tokenHeight, zoomStep) : 0f;

        /// <summary>지금 보이는 표지 수.</summary>
        public int Count
        {
            get
            {
                int n = 0;
                foreach (var b in badges) if (b.On) n++;
                return n;
            }
        }

        /// <summary>그 칸의 표지에 지금 보이는 좌석(표지가 없으면 −1).</summary>
        public int ShownSeat(int cell) => Find(cell)?.Seat ?? -1;

        /// <summary>그 칸의 표지에 적힌 사람 수(표지가 없으면 0).</summary>
        public int Total(int cell) => Find(cell)?.Total ?? 0;

        /// <summary>그 칸 표지의 사각형(패널 px, 왼쪽 위 기준). 표지가 없으면 false.</summary>
        public bool RectOf(int cell, out Rect rect)
        {
            var b = Find(cell);
            rect = b != null ? b.Rect : default;
            return b != null;
        }

        /// <summary>그 칸 표지와 사람 수 배지를 함께 두른 사각형(패널 px) — 공개 정보 카드가 배지까지 덮지 않게 비켜 서는 데 쓴다. 표지가 없으면 false.</summary>
        public bool BoundsOf(int cell, out Rect rect)
        {
            var b = Find(cell);
            rect = b != null ? b.Rect : default;
            if (b == null) return false;
            var count = b.Count.worldBound;
            if (!float.IsNaN(count.width) && count.width > 0f && layer.panel != null)
            {
                count = layer.WorldToLocal(count);
                rect = Rect.MinMaxRect(Mathf.Min(rect.xMin, count.xMin), Mathf.Min(rect.yMin, count.yMin), Mathf.Max(rect.xMax, count.xMax), Mathf.Max(rect.yMax, count.yMax));
            }
            return true;
        }

        /// <summary>그 점(패널 px) 아래의 표지가 선 칸(없으면 −1). 표지는 동그랗다 — 원 안만 친다.</summary>
        public int CellAt(Vector2 panel)
        {
            for (int i = badges.Count - 1; i >= 0; i--)
            {
                var b = badges[i];
                if (!b.On) continue;
                float r = b.Rect.width * 0.5f;
                if ((panel - b.Rect.center).sqrMagnitude <= r * r) return b.Cell;
            }
            return -1;
        }

        Badge Find(int cell)
        {
            foreach (var b in badges) if (b.On && b.Cell == cell) return b;
            return null;
        }

        /// <summary>지금 묶음·카메라·시각(<see cref="GameFlow.StackMs"/>)에 맞춰 표지를 놓는다(프레임마다).</summary>
        public void Show(GameFlow flow)
        {
            var pieces = flow.OnBoard ? flow.BoardPieces : null;
            var stacks = pieces != null ? pieces.Stacks : null;
            int n = stacks != null ? stacks.Count : 0;
            while (badges.Count < n) badges.Add(Make());
            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < badges.Count; i++)
            {
                var b = badges[i];
                PieceStack st = i < n ? stacks[i] : null;
                BoardPieces.Piece stand = st != null ? pieces.Of(st.Stand) : null, who = null;
                Texture2D tex = null;
                Vector2 at = default;
                if (stand != null)
                {
                    who = pieces.Of(PieceStacks.Shown(st, flow.StackMs));
                    tex = StackArt.Face(who);
                }
                bool on = tex != null && flow.HeadPoint(stand, out at);
                if (on != b.On)
                {
                    b.On = on;
                    b.Root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (!on) continue;
                float size = tex.width * StackArt.Mul(who.Style, stage.Grade.TokenHeight, flow.ZoomStep);
                var rect = new Rect(Mathf.Round(at.x - size * 0.5f), at.y - Gap - size, size, size);
                if (b.Cell != st.Cell)
                {
                    b.Cell = st.Cell;
                    b.Root.name = "board-stack-" + st.Cell.ToString(inv);
                }
                if (b.Seat != who.Seat || b.Art.image != tex)
                {
                    b.Seat = who.Seat;
                    b.Art.image = tex;
                }
                if (b.Total != st.Count)
                {
                    b.Total = st.Count;
                    b.Count.text = CountMark + st.Count.ToString(inv);
                }
                if (rect != b.Rect)
                {
                    b.Rect = rect;
                    b.Root.style.left = rect.x;
                    b.Root.style.top = rect.y;
                    b.Root.style.width = rect.width;
                    b.Root.style.height = rect.height;
                }
            }
        }

        Badge Make()
        {
            var b = new Badge { Root = new VisualElement { pickingMode = PickingMode.Ignore } };
            b.Root.AddToClassList("sn-stack");
            b.Root.style.display = DisplayStyle.None;
            b.Art = new Image { name = "board-stack-art", scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            b.Art.AddToClassList("sn-stack-art");
            b.Root.Add(b.Art);
            b.Count = Kit.Text("", Kit.F2);   // 벌을 따라 커진다(720 벌 12px · 1080 벌 24px) — 표지가 커지는 만큼 수도 읽히게
            b.Count.name = "board-stack-count";
            b.Count.pickingMode = PickingMode.Ignore;
            b.Count.AddToClassList("sn-stack-count");
            b.Root.Add(b.Count);
            layer.Add(b.Root);
            return b;
        }
    }
}
