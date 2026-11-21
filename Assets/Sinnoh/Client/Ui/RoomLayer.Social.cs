using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Fx;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 방 판 덮개 층의 ⑤ 소통(결정 97 · 102, 02 §4-8 — isekai `Client/Game/Social/`을 옮김). 혼자 하기 · 튜토리얼에는 이 층이 없다(단추 · 입력이 없다).
    /// <list type="bullet">
    /// <item>핑: Alt를 누른 채 누르면 그 자리에 — 보드는 누른 자리 그대로(가장 가까운 칸 + 칸 안 위치), 보드 밖은 공개 대상 허용 목록(<see cref="PingSpots"/>)의 요소와 그 사각형 안 상대 위치.
    /// 바로 떼면 「여기 봐」, <see cref="HoldMs"/> 넘게 누르고 있거나 끌면 종류 고리(위 여기 봐 · 오른쪽 ? · 아래 좋아 · 왼쪽 조심 — 가운데에서 떼면 취소) [가정 M7-81].
    /// 받은 핑은 받는 화면의 같은 요소 위에(없으면 대신할 요소 · 주인 좌석의 칩) 좌석 색 고리 · 종류 글자 · 「이름 · 종류」로 4초 — <c>room-ping-&lt;n&gt;</c>(+ <c>-name</c> · <c>-glyph</c>), 고리 <c>room-ping-wheel</c>(+ <c>-&lt;n&gt;</c>).</item>
    /// <item>감정 표현: 내 좌석 칩 오른쪽 클릭(결정 112 Q12 — 가운데 줄의 단추를 뺐다, <see cref="ChipMenu"/>) → 칩 아래 메뉴 <c>room-emote-menu</c>(문구 일곱 줄 <c>room-emote-line-&lt;n&gt;</c> · 연타 제한 안내 <c>room-emote-wait</c> ·
    /// 상대별 [반응 끄기]·[핑 끄기] <c>room-emote-mute-&lt;좌석&gt;</c>·<c>room-ping-mute-&lt;좌석&gt;</c>) → 보낸 좌석의 칩 얼굴 아래 말풍선 <c>room-emote-&lt;좌석&gt;</c>(+ <c>-text</c>).</item>
    /// <item>도장: 남의 행동 기록 줄(HUD 기록 판 · 기록 창)을 누르면 <c>room-mark-menu</c>(<c>room-mark-pick-&lt;n&gt;</c> 대단해 · 헉 · 아깝다) → 그 행동의 첫 줄 오른쪽 끝에 좌석 색 칩 <c>room-mark-&lt;i&gt;</c>.</item>
    /// </list>
    /// 글은 <c>screen.room.ping.*</c>·<c>emote.*</c>·<c>mark.*</c>, 자리 · 색은 <c>sn-room-ping*</c>·<c>sn-room-wheel*</c>·<c>sn-room-emote*</c>·<c>sn-room-mark*</c>.
    /// </summary>
    public sealed partial class RoomLayer
    {
        /// <summary>이만큼 누르고 있으면 종류 고리(ms) · 이만큼 끌어도 고리(패널 px).</summary>
        public const long HoldMs = 300;
        public const float MoveTol = 8f, WheelDead = 18f;
        /// <summary>화면 밖 칸의 핑을 붙여 두는 가장자리 여백(720 토큰 px) [가정 M7-69].</summary>
        public const float PingEdge = 24f, PingEdgeTop = 64f;
        /// <summary>도장 메뉴가 저절로 닫히는 시간(ms).</summary>
        public const long MarkMenuMs = 6000;

        static readonly string[] PingGlyphs = { "", "?", "○", "!" };

        /// <summary>핑 종류의 이름(0 여기 봐 · 1 ? · 2 좋아 · 3 조심). 「?」는 글이 아니라 기호라 문자열 표에 없다.</summary>
        public string PingKind(int code) => code == 1 ? "?" : Stage.T("screen.room.ping.kind." + code.ToString(CultureInfo.InvariantCulture));

        readonly List<VisualElement> pingMarks = new List<VisualElement>();
        readonly List<Vector2> pingPoints = new List<Vector2>();
        readonly Dictionary<int, VisualElement> bubbles = new Dictionary<int, VisualElement>();
        readonly List<VisualElement> markChips = new List<VisualElement>();
        readonly List<VisualElement> rowsFound = new List<VisualElement>();
        readonly List<Rect> bubbleRects = new List<Rect>();
        VisualElement wheel, emoteMenu, markMenu, hooked, over;
        int pingsHeard, emotesHeard, marksHeard;
        string emoteKey, marksKey;

        // 핑 누름(Alt + 누르기)
        bool pressing, wasLeft, wheelUp;
        Vector2 downAt;
        long downMs;
        PingTarget downTarget;
        int sector = -1;

        // 도장 메뉴
        BoardLogLine markLine;
        VisualElement markRow;
        long markAt;

        /// <summary>기록 줄 요소의 이름(행동 번호 — 핑 허용 목록의 「기록 줄」 줄).</summary>
        public static string LogRowName(int seq) => PingSpots.Rows[PingSpots.LogRow].Name + seq.ToString(CultureInfo.InvariantCulture);

        /// <summary>지금 보이는 핑 표시 수(시험 · 확인).</summary>
        public int PingsShown { get; private set; }

        /// <summary>i번째 핑 표시의 자리(패널 좌표 — 화면 밖 칸은 가장자리에 붙인 자리).</summary>
        public Vector2 PingPoint(int i) => pingPoints[i];

        /// <summary>종류 고리가 떠 있나 · 지금 가리키는 종류(가운데 = −1).</summary>
        public bool WheelUp => wheelUp;
        public int WheelSector => sector;

        /// <summary>감정 표현 메뉴가 열려 있나.</summary>
        public bool EmotesOpen { get; private set; }

        /// <summary>도장 메뉴가 열린 행동 번호(닫혀 있으면 −1).</summary>
        public int MarksOpenFor => markLine != null ? markLine.Seq : -1;

        /// <summary>지금 그려 둔 도장 칩 수(시험 · 확인).</summary>
        public int MarksShown { get; private set; }

        RoomLive Live => flow.Room.InGame ? flow.Room.Live : null;

        void SocialEnsure()
        {
            // 가운데 줄보다 위에 그릴 것(말풍선 · 도장 칩 · 종류 고리 — 마우스를 받지 않는다)
            over = Add(layer, new VisualElement { pickingMode = PickingMode.Ignore }, "room-social", "sn-room-layer");
            wheel = Add(over, new VisualElement { pickingMode = PickingMode.Ignore }, "room-ping-wheel", "sn-room-wheel");
            Add(wheel, new VisualElement { pickingMode = PickingMode.Ignore }, "room-ping-wheel-hub", "sn-room-wheel-hub");
            for (int i = 0; i < SocialCodes.Ping; i++)
            {
                var item = Add(wheel, Kit.Text(PingKind(i), Kit.F2), "room-ping-wheel-" + i.ToString(CultureInfo.InvariantCulture), "sn-room-wheel-item");
                item.pickingMode = PickingMode.Ignore;
                item.AddToClassList("sn-room-wheel-" + i.ToString(CultureInfo.InvariantCulture));
            }
            wheel.style.display = DisplayStyle.None;
            markMenu = Add(layer, new VisualElement(), "room-mark-menu", "sn-room-mark-menu");
            for (int i = 0; i < SocialCodes.Mark; i++)
            {
                int code = i;
                Add(markMenu, Kit.Button(Stage.T("screen.room.mark.kind." + i.ToString(CultureInfo.InvariantCulture)), KitButtonKind.Normal, () => PressMark(code)), "room-mark-pick-" + i.ToString(CultureInfo.InvariantCulture), "sn-room-mark-btn");
            }
            markMenu.style.display = DisplayStyle.None;
            // 감정 표현 메뉴(결정 112 Q12): 내 좌석 칩 바로 아래에 뜬다(자리는 프레임마다 칩을 따라간다)
            emoteMenu = Add(layer, Kit.Panel(Stage.Skin), "room-emote-menu", "sn-room-emote-menu");
            emoteMenu.style.display = DisplayStyle.None;
        }

        // 방 판을 떠났다: 층을 걷을 때 소통의 열린 것을 닫는다
        void SocialOff()
        {
            Unhook();
            PingsShown = 0;
            MarksShown = 0;
            pingPoints.Clear();
            pressing = wasLeft = wheelUp = false;
            sector = -1;
            EmotesOpen = false;
            emoteKey = marksKey = null;
            markLine = null;
            markRow = null;
            if (markMenu != null) markMenu.style.display = DisplayStyle.None;
            if (wheel != null) wheel.style.display = DisplayStyle.None;
        }

        void Social(RoomLive live, bool covered)
        {
            Hook();
            live.DropOldPings();
            live.DropOldEmotes();
            // 소리(M9 소리 ③ — 핑 · 감정 표현 · 도장 키, 사용자 고르기는 소리 ④): 새 핑 「!」 · 새 감정 표현 · 새 도장
            if (live.PingsSeen != pingsHeard)
            {
                if (live.PingsSeen > pingsHeard) Stage.Tap(SoundKeys.Ping);
                pingsHeard = live.PingsSeen;
            }
            if (live.EmotesSeen != emotesHeard)
            {
                if (live.EmotesSeen > emotesHeard) Stage.Tap(SoundKeys.Emote);
                emotesHeard = live.EmotesSeen;
            }
            if (live.MarksSeen != marksHeard)
            {
                if (live.MarksSeen > marksHeard) Stage.Tap(SoundKeys.Mark);
                marksHeard = live.MarksSeen;
                marksKey = null;
            }
            Pings(live, covered);
            PaintWheel();
            Emotes(live, covered);
            Marks(live, covered);
        }

        // ── Alt를 누르고 있는 동안 PC 층의 단추 · 칸이 눌리지 않게(핑의 누름이다) ──

        void Hook()
        {
            var top = Stage.Root != null && Stage.Root.panel != null ? Stage.Root.panel.visualTree : null;
            if (ReferenceEquals(top, hooked)) return;
            Unhook();
            hooked = top;
            if (top == null) return;
            top.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            top.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            top.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
        }

        void Unhook()
        {
            if (hooked == null) return;
            hooked.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            hooked.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            hooked.UnregisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            hooked = null;
        }

        void OnDown(PointerDownEvent e)
        {
            // 내 좌석 칩 오른쪽 클릭 = 감정 표현 메뉴(결정 112 Q12) — 칩은 갱신마다 다시 만들어져 콜백을 걸지 않고 여기서 이름으로 찾는다
            if (e.button == 1 && !flow.PingKey && !pressing && Live != null && e.target is VisualElement t && ChipOf(t, out int seat) && ChipMenu(seat))
            {
                e.StopImmediatePropagation();
                return;
            }
            Swallow(e);
        }

        // 그 요소에서 뿌리 쪽으로 올라가 좌석 칩을 찾는다
        static bool ChipOf(VisualElement e, out int seat)
        {
            seat = -1;
            for (; e != null; e = e.parent)
                if (!string.IsNullOrEmpty(e.name) && SeatChip(e.name, out seat)) return true;
            return false;
        }
        void OnUp(PointerUpEvent e) => Swallow(e);
        void OnClick(ClickEvent e) => Swallow(e);

        void Swallow(EventBase e)
        {
            if ((flow.PingKey || pressing) && flow.Room.InGame) e.StopImmediatePropagation();
        }

        // ── 핑: 대상 찾기(보내는 쪽) ──

        static bool SeatChip(string name, out int seat)
        {
            seat = -1;
            const string head = "board-hud-seat-";
            if (!name.StartsWith(head, StringComparison.Ordinal)) return false;
            if (!int.TryParse(name.Substring(head.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1) return false;
            seat = n - 1;
            return true;
        }

        static string ChipName(int seat) => "board-hud-seat-" + (seat + 1).ToString(CultureInfo.InvariantCulture);

        // 어느 좌석의 것을 보이는 창이 맨 위에 떠 있으면 그 좌석(정비 판 · 트레이너 카드), 아니면 −1
        int OwnedSeat()
        {
            var top = flow.Top;
            return top is PartyScreen party ? party.Seat : top is TrainerScreen card ? card.Seat : -1;
        }

        /// <summary>
        /// 그 점(패널 좌표) 아래의 핑 대상: PC 층 요소 위면 허용 목록에서 찾고(요소에서 뿌리 쪽으로 — 가장 안쪽의 허용된 요소), 목록에 없는 요소(숨은 것 · 창 바탕)면 null,
        /// 아무 요소도 없고 보드가 보이면 보드의 누른 자리(가장 가까운 칸 + 칸 안 위치).
        /// </summary>
        public PingTarget TargetAt(Vector2 p)
        {
            var live = Live;
            var root = Stage.Root;
            if (live == null || live.Seat < 0 || root == null || root.panel == null) return null;
            var picked = root.panel.Pick(p);
            if (picked != null && picked != root && picked != root.parent)
            {
                for (var e = picked; e != null && e != root; e = e.parent)
                {
                    if (e == layer) return null;
                    string name = e.name;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (SeatChip(name, out int chip)) return Placed(PingTarget.OfSeat(chip), e, p);
                    int row = PingSpots.Find(name, out int k);
                    if (row >= 0)
                    {
                        var r = PingSpots.Rows[row];
                        int owner = -1;
                        if (r.Scope == SpotScope.Mine) owner = live.Seat;
                        else if (r.Scope == SpotScope.Owned)
                        {
                            owner = OwnedSeat();
                            if (owner < 0) continue;
                        }
                        else if (r.Scope == SpotScope.Side)
                        {
                            var feed = flow.Battle;
                            if (feed == null) continue;
                            k = name.EndsWith("-foe", StringComparison.Ordinal) ? 1 - feed.MySide : feed.MySide;
                        }
                        return Placed(PingTarget.Spot(row, k, owner), e, p);
                    }
                    if (Array.IndexOf(PingSpots.ToSeat, name) >= 0) return PingTarget.OfSeat(live.Seat);
                }
                return null;
            }
            if (!flow.OnBoard || Stage.Current != null) return null;
            float scale = Stage.Grade.PanelScale;
            if (!flow.PingCellAt(p.x * scale, flow.LaidFor.y - p.y * scale, out int cell, out int dx, out int dz)) return null;
            return PingTarget.Cell(cell).At2(dx, dz);
        }

        static PingTarget Placed(PingTarget t, VisualElement e, Vector2 p)
        {
            var b = e.worldBound;
            if (float.IsNaN(b.width) || b.width <= 0f || b.height <= 0f) return t;
            return t.At2(Mathf.RoundToInt((p.x - b.xMin) / b.width * PingRules.Unit), Mathf.RoundToInt((p.y - b.yMin) / b.height * PingRules.Unit));
        }

        /// <summary>그 점(패널 좌표)에 그 종류의 핑을 찍는다(대상이 없거나 연타 제한이면 false).</summary>
        public bool PingHere(Vector2 p, int code) => SendPing(TargetAt(p), code);

        bool SendPing(PingTarget target, int code)
        {
            if (target == null || !flow.Room.Ping(target, code)) return false;
            Stage.Tap(SoundKeys.Select);
            return true;
        }

        /// <summary>
        /// 마우스 자리(물리 px, 왼쪽 아래가 (0, 0))와 왼쪽 단추 · Alt를 받는다(프레임마다 — <see cref="GameFlow.Pointer(float, float, bool, bool)"/>가 부른다). 이 누름이 핑의 것이면 true(보드 · 칩으로 새지 않는다).
        /// Alt를 누른 채 누르면 시작 → 바로 떼면 「여기 봐」, <see cref="HoldMs"/> 넘게 누르고 있거나 <see cref="MoveTol"/> 넘게 끌면 종류 고리 → 뗀 쪽의 종류(가운데 = 취소).
        /// held가 아니면(한 번에 누르고 뗀 것 — 길잡이 · 시험) 그 자리에 「여기 봐」.
        /// </summary>
        public bool PingInput(float x, float y, bool left, bool held, bool key)
        {
            var live = Live;
            float scale = Stage.Grade.PanelScale;
            var p = new Vector2(x / scale, (flow.LaidFor.y - y) / scale);
            bool began = left && !wasLeft;
            wasLeft = held && left;
            if (live == null || live.Seat < 0)
            {
                pressing = wheelUp = false;
                return false;
            }
            if (!pressing)
            {
                // 메뉴 밖을 누르면 열린 메뉴를 닫는다(메뉴 안의 누름은 단추가 받는다)
                if (began && !key) CloseMenusOutside(p);
                if (!key || !(held ? began : left)) return false;
                if (!held)
                {
                    PingHere(p, 0);
                    return true;
                }
                pressing = true;
                wheelUp = false;
                sector = -1;
                downAt = p;
                downMs = live.NowMs;
                downTarget = TargetAt(p);
                return true;
            }
            if (left)
            {
                if (!wheelUp && downTarget != null && (live.NowMs - downMs >= HoldMs || Vector2.Distance(p, downAt) > MoveTol)) wheelUp = true;
                if (wheelUp) sector = SectorOf(p - downAt);
                return true;
            }
            pressing = false;
            if (!wheelUp) SendPing(downTarget, 0);
            else if (sector >= 0) SendPing(downTarget, sector);
            wheelUp = false;
            sector = -1;
            downTarget = null;
            return true;
        }

        /// <summary>고리 가운데에서 그만큼 떨어진 점이 가리키는 종류: 위 0 여기 봐 · 오른쪽 1 ? · 아래 2 좋아 · 왼쪽 3 조심, 가운데(<see cref="WheelDead"/> 안) = −1.</summary>
        public static int SectorOf(Vector2 d)
        {
            if (d.magnitude < WheelDead) return -1;
            if (Mathf.Abs(d.x) > Mathf.Abs(d.y)) return d.x > 0f ? 1 : 3;
            return d.y < 0f ? 0 : 2;
        }

        void PaintWheel()
        {
            wheel.style.display = wheelUp ? DisplayStyle.Flex : DisplayStyle.None;
            if (!wheelUp) return;
            wheel.style.left = downAt.x;
            wheel.style.top = downAt.y;
            for (int i = 0; i < SocialCodes.Ping; i++) wheel[i + 1].EnableInClassList("sn-room-wheel-on", i == sector);
        }

        // ── 핑: 그리기(받는 쪽) ──

        static bool Shown(VisualElement e)
        {
            if (e == null || e.panel == null) return false;
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None) return false;
            var b = e.worldBound;
            return !float.IsNaN(b.width) && b.width > 0f && b.height > 0f;
        }

        // 다른 창이 그 요소를 덮고 있나(기록 창이 HUD 기록 판을 덮을 때 — 가운데 점에서 집히는 것이 그 요소 안이 아니다). 이 층의 것(도장 메뉴)은 덮는 것으로 치지 않는다
        bool Hidden(VisualElement e)
        {
            var hit = e.panel.Pick(e.worldBound.center);
            if (hit == null) return false;
            for (var p = hit; p != null; p = p.parent)
                if (p == e || p == layer) return false;
            return true;
        }

        // 그 이름의 요소 가운데 보이는 마지막 것(위에 뜬 창의 것이 뒤에 있다). 이 층 안의 요소는 치지 않는다
        VisualElement Find(string name)
        {
            VisualElement found = null;
            Stage.Root.Query<VisualElement>(name).ForEach(e =>
            {
                for (var p = e; p != null; p = p.parent)
                    if (p == layer) return;
                if (Shown(e)) found = e;
            });
            return found;
        }

        /// <summary>
        /// 받은 대상이 내 화면에서 가리키는 점(패널 좌표). 보드 칸은 내 카메라로 푼 그 땅, 보드 밖 대상은 같은 요소의 같은 비율 지점 —
        /// 요소가 내 화면에 없으면 대신할 요소(허용 목록의 Fallback)나 주인 좌석의 칩 가운데. 어디에도 없으면 false. board = 보드 위의 점인가.
        /// </summary>
        public bool PingPointOf(PingTarget t, out Vector2 at, out bool board)
        {
            at = default;
            board = false;
            if (t == null || Stage.Root == null) return false;
            int me = flow.Room.Live != null ? flow.Room.Live.Seat : -1;
            VisualElement e = null;
            bool exact = true, flip = false;
            switch (t.At)
            {
                case PingAt.Cell:
                    board = true;
                    return t.Pos ? flow.CellSpot(t.N, t.X, t.Y, out at) : flow.CellPoint(t.N, out at);
                case PingAt.Seat:
                    e = Find(ChipName(t.Seat));
                    break;
                case PingAt.Spot:
                {
                    if (t.N < 0 || t.N >= PingSpots.Rows.Length) return false;
                    var r = PingSpots.Rows[t.N];
                    switch (r.Scope)
                    {
                        case SpotScope.Mine:
                            if (t.Seat == me) e = Find(PingSpots.NameOf(t.N, t.K));
                            break;
                        case SpotScope.Owned:
                            if (OwnedSeat() == t.Seat) e = Find(PingSpots.NameOf(t.N, t.K));
                            break;
                        case SpotScope.Side:
                        {
                            var feed = flow.Battle;
                            if (feed == null) break;
                            if (r.Name.EndsWith("-", StringComparison.Ordinal)) e = Find(r.Name + (t.K == feed.MySide ? "me" : "foe"));
                            else
                            {
                                e = Find(r.Name);
                                flip = t.K != feed.MySide;   // 맞은편에서 보는 화면: 내 포켓몬(앞 왼쪽)과 상대(뒤 오른쪽)가 바뀌어 있다 [가정 M7-82]
                            }
                            break;
                        }
                        default:
                            e = Find(PingSpots.NameOf(t.N, t.K));
                            break;
                    }
                    if (e == null && r.Fallback != null)
                    {
                        e = Find(r.Fallback);
                        exact = false;
                    }
                    if (e == null && t.Seat >= 0)
                    {
                        e = Find(ChipName(t.Seat));
                        exact = false;
                    }
                    break;
                }
            }
            if (e == null) return false;
            var b = e.worldBound;
            if (!t.Pos || !exact)
            {
                at = b.center;
                return true;
            }
            float fx = t.X / (float)PingRules.Unit, fy = t.Y / (float)PingRules.Unit;
            if (flip)
            {
                // DS 화면의 무대(위 3/4)만 가운데 점 대칭으로 뒤집는다 — 아래 메시지 창은 그대로
                const float stage = 0.75f;
                fx = 1f - fx;
                if (fy < stage) fy = stage - fy;
            }
            at = new Vector2(b.xMin + fx * b.width, b.yMin + fy * b.height);
            return true;
        }

        // 받은 핑을 그린다: 보드 칸의 핑은 보드가 보일 때만(화면이 덮고 있으면 숨긴다 · 화면 밖이면 가장자리), 보드 밖 대상의 핑은 그 요소가 보이면 어느 화면 위에서든
        void Pings(RoomLive live, bool covered)
        {
            pingPoints.Clear();
            int n = 0;
            if (!covered)
            {
                long now = live.NowMs;
                var size = layer.layout;
                bool sized = !float.IsNaN(size.width) && size.width > 0f;
                bool boardShown = flow.OnBoard && Stage.Current == null;
                foreach (var p in live.Pings)
                {
                    if (!PingPointOf(p.Target, out var at, out bool onBoard)) continue;
                    if (onBoard)
                    {
                        if (!boardShown) continue;
                        if (sized)
                        {
                            at.x = Mathf.Clamp(at.x, PingEdge, size.width - PingEdge);
                            at.y = Mathf.Clamp(at.y, PingEdgeTop, size.height - PingEdge);
                        }
                    }
                    var mark = n < pingMarks.Count ? pingMarks[n] : MakePing(n);
                    if (mark.parent != pings) pings.Add(mark);
                    PaintPing(mark, live, p, at, now - p.AtMs);
                    pingPoints.Add(at);
                    n++;
                }
            }
            for (int i = n; i < pingMarks.Count; i++)
                if (pingMarks[i].parent != null) pingMarks[i].RemoveFromHierarchy();
            PingsShown = n;
        }

        VisualElement MakePing(int i)
        {
            var mark = new VisualElement { name = "room-ping-" + i, pickingMode = PickingMode.Ignore };
            mark.AddToClassList("sn-room-ping");
            foreach (var cls in new[] { "sn-room-ping-wave", "sn-room-ping-back", "sn-room-ping-ring", "sn-room-ping-dot" })
                Add(mark, new VisualElement { pickingMode = PickingMode.Ignore }, null, cls);
            var glyph = Add(mark, Kit.Text("", Kit.F2), "room-ping-" + i + "-glyph", "sn-room-ping-glyph");
            glyph.pickingMode = PickingMode.Ignore;
            var tag = Add(mark, new VisualElement { pickingMode = PickingMode.Ignore }, null, "sn-room-ping-tag");
            var name = Add(tag, Kit.Text("", Kit.F2), "room-ping-" + i + "-name", "sn-room-ping-name");
            name.pickingMode = PickingMode.Ignore;
            pingMarks.Add(mark);
            return mark;
        }

        // 나이(ms)에 따른 모양: 처음 0.25초 고리가 커졌다 제자리 · 1초마다 물결이 퍼지며 옅어짐 · 마지막 0.5초 전체가 옅어짐. 종류는 가운데 글자(여기 봐 = 점)와 이름 띠 「이름 · 종류」
        void PaintPing(VisualElement mark, RoomLive live, RoomPing p, Vector2 at, long age)
        {
            mark.style.left = at.x;
            mark.style.top = at.y;
            Color color = flow.SeatColor(p.Seat);
            var wave = mark[0];
            var ring = mark[2];
            var dot = mark[3];
            var glyph = (Label)mark[4];
            foreach (var e in new[] { wave, ring })
            {
                e.style.borderTopColor = color;
                e.style.borderBottomColor = color;
                e.style.borderLeftColor = color;
                e.style.borderRightColor = color;
            }
            dot.style.backgroundColor = color;
            int code = p.Code >= 0 && p.Code < PingGlyphs.Length ? p.Code : 0;
            string g = PingGlyphs[code];
            dot.style.display = g.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            glyph.style.display = g.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (glyph.text != g) glyph.text = g;
            glyph.style.backgroundColor = color;
            glyph.style.color = new StyleColor((Color)SeatColors.Ink(flow.SeatColor(p.Seat)));
            float pop = age < 250 ? 1f + 0.6f * (1f - age / 250f) : 1f;
            ring.style.scale = new Scale(new Vector3(pop, pop, 1f));
            mark[1].style.scale = new Scale(new Vector3(pop, pop, 1f));
            float t = (age % 1000) / 1000f;
            float spread = 1f + 1.4f * t;
            wave.style.scale = new Scale(new Vector3(spread, spread, 1f));
            wave.style.opacity = 0.85f * (1f - t);
            long left = PingRules.ShowMs - age;
            mark.style.opacity = left < 500 ? Mathf.Clamp01(left / 500f) : 1f;
            var name = mark.Q<Label>(className: "sn-room-ping-name");
            string who = Name(live, p.Seat) + " · " + PingKind(code);
            if (name.text != who) name.text = who;
        }

        // ── 감정 표현 ──

        /// <summary>좌석 칩 오른쪽 클릭(결정 112 Q12): 내 칩이면 감정 표현 메뉴를 열고 닫는다(남의 칩 · 좌석 없음이면 false — 눌림을 가로채지 않는다).</summary>
        public bool ChipMenu(int seat)
        {
            var live = Live;
            return live != null && live.Seat >= 0 && seat == live.Seat && ToggleEmotes();
        }

        /// <summary>감정 표현 메뉴를 열고 닫는다. 좌석이 없으면 열리지 않는다.</summary>
        public bool ToggleEmotes()
        {
            var live = Live;
            if (live == null || live.Seat < 0) return false;
            EmotesOpen = !EmotesOpen;
            emoteKey = null;
            return true;
        }

        /// <summary>메뉴의 문구 한 줄을 보낸다(연타 제한 — 2초에 1번 · 20초에 5번 — 이면 false, 메뉴는 그대로). 보냈으면 메뉴를 닫는다.</summary>
        public bool PressEmote(int code)
        {
            if (!flow.Room.Emote(code)) return false;
            EmotesOpen = false;
            emoteKey = null;
            return true;
        }

        /// <summary>메뉴의 상대별 [반응 끄기]·[핑 끄기]: 그 좌석의 감정 표현 · 도장(pings = false) 또는 핑(true)을 내 화면에서 숨기고 다시 보인다.</summary>
        public bool ToggleMute(int seat, bool pings)
        {
            var live = Live;
            if (live == null || seat == live.Seat || live.Room == null || seat < 0 || seat >= live.Room.Seats.Count) return false;
            live.SetMuted(seat, pings, !live.Muted(seat, pings));
            emoteKey = marksKey = null;
            return true;
        }

        void Emotes(RoomLive live, bool covered)
        {
            // 메뉴는 내 칩 아래 — 칩이 안 보이면(덮개 · 좌석 없음) 닫는다
            var myChip = live.Seat >= 0 && !covered ? Find(ChipName(live.Seat)) : null;
            if (myChip == null) EmotesOpen = false;
            long wait = EmotesOpen ? live.SocialWaitMs(SocialKind.Emote) : 0;
            int seats = live.Room != null ? live.Room.Seats.Count : 0;
            var key = new System.Text.StringBuilder();
            key.Append(EmotesOpen).Append('|').Append(wait > 0).Append('|').Append(seats);
            for (int s = 0; s < seats; s++) key.Append('|').Append(Name(live, s)).Append(live.Muted(s, false) ? 'r' : '-').Append(live.Muted(s, true) ? 'p' : '-');
            string k = key.ToString();
            if (k != emoteKey)
            {
                emoteKey = k;
                emoteMenu.Clear();
                emoteMenu.style.display = EmotesOpen ? DisplayStyle.Flex : DisplayStyle.None;
                if (EmotesOpen)
                {
                    var inv = CultureInfo.InvariantCulture;
                    for (int i = 0; i < SocialCodes.Emote; i++)
                    {
                        int code = i;
                        var b = Add(emoteMenu, Kit.Button(Stage.T("screen.room.emote.line." + i.ToString(inv)), KitButtonKind.Normal, () => PressEmote(code)), "room-emote-line-" + i.ToString(inv), "sn-room-emote-line");
                        b.SetEnabled(wait <= 0);
                    }
                    var note = Add(emoteMenu, Kit.Text(Stage.T(wait > 0 ? "screen.room.emote.wait" : "screen.room.emote.limit"), Kit.F2), "room-emote-wait", "kit-muted");
                    Stage.Tips.Hook(note, null, Stage.T("screen.room.emote.limit.tip"), TipSide.Below, reading: true);
                    for (int s = 0; s < seats; s++)
                    {
                        if (s == live.Seat || live.Room.Seats[s] == null) continue;
                        int seat = s;
                        var row = Add(emoteMenu, new VisualElement(), "room-emote-seat-" + s.ToString(inv), "sn-room-emote-seat");
                        Add(row, Kit.Text(Name(live, s), Kit.F2), null, "sn-room-emote-who");
                        var react = Add(row, Kit.Button(Stage.T(live.Muted(s, false) ? "screen.room.emote.unmute" : "screen.room.emote.mute"), KitButtonKind.Normal, () => ToggleMute(seat, false)), "room-emote-mute-" + s.ToString(inv), "sn-room-emote-mute");
                        Stage.Tips.Hook(react, null, Stage.T("screen.room.emote.mute.tip"), TipSide.Below, reading: true);
                        var ping = Add(row, Kit.Button(Stage.T(live.Muted(s, true) ? "screen.room.ping.unmute" : "screen.room.ping.mute"), KitButtonKind.Normal, () => ToggleMute(seat, true)), "room-ping-mute-" + s.ToString(inv), "sn-room-emote-mute");
                        Stage.Tips.Hook(ping, null, Stage.T("screen.room.ping.mute.tip"), TipSide.Below, reading: true);
                    }
                }
            }
            if (EmotesOpen)
            {
                var size0 = layer.layout;
                float mw = emoteMenu.layout.width;
                float mx = myChip.worldBound.xMin;
                if (!float.IsNaN(mw) && mw > 0f && !float.IsNaN(size0.width) && size0.width > 0f) mx = Mathf.Max(4f, Mathf.Min(mx, size0.width - mw - 4f));
                emoteMenu.style.left = mx;
                emoteMenu.style.top = myChip.worldBound.yMax + 2f;
                if (layer.IndexOf(emoteMenu) != layer.childCount - 1) emoteMenu.BringToFront();
            }
            // 말풍선: 보낸 좌석의 칩 얼굴 아래(칩이 안 보이는 화면이면 왼쪽 위에 「이름: 말」로 쌓는다)
            var size = layer.layout;
            long now = live.NowMs;
            foreach (var kv in bubbles) kv.Value.userData = null;
            int loose = 0;
            bubbleRects.Clear();
            if (!covered)
                foreach (var e in live.Emotes)
                {
                    if (!bubbles.TryGetValue(e.Seat, out var bubble)) bubbles[e.Seat] = bubble = MakeBubble(e.Seat);
                    bubble.userData = e;
                    if (bubble.parent != over) over.Add(bubble);
                    var label = (Label)bubble[0];
                    string line = Stage.T("screen.room.emote.line." + Mathf.Clamp(e.Code, 0, SocialCodes.Emote - 1).ToString(CultureInfo.InvariantCulture));
                    var chip = Find(ChipName(e.Seat));
                    float left, top;
                    if (chip != null)
                    {
                        left = chip.worldBound.xMin;
                        top = chip.worldBound.yMax + 2f;
                    }
                    else
                    {
                        line = Name(live, e.Seat) + ": " + line;
                        left = 8f;
                        top = PingEdgeTop + 26f * loose++;
                    }
                    if (label.text != line) label.text = line;
                    float w = bubble.layout.width, h = bubble.layout.height;
                    if (!float.IsNaN(w) && w > 0f && !float.IsNaN(size.width) && size.width > 0f)
                    {
                        left = Mathf.Max(4f, Mathf.Min(left, size.width - w - 4f));
                        // 옆 좌석의 말풍선과 겹치면 한 줄 아래로(여럿이 한꺼번에 보낼 때)
                        var box = new Rect(left, top, w, h);
                        for (int tries = 0; tries < SocialCodes.Emote && bubbleRects.Exists(o => o.Overlaps(box)); tries++) box.y += h + 2f;
                        top = box.y;
                        bubbleRects.Add(box);
                    }
                    bubble.style.left = left;
                    bubble.style.top = top;
                    Color color = flow.SeatColor(e.Seat);
                    bubble.style.borderTopColor = color;
                    bubble.style.borderBottomColor = color;
                    bubble.style.borderLeftColor = color;
                    bubble.style.borderRightColor = color;
                    long age = now - e.AtMs, rest = RoomLive.EmoteMs - age;
                    bubble.style.opacity = rest < 300 ? Mathf.Clamp01(rest / 300f) : 1f;
                    float pop = age < 180 ? 0.8f + 0.2f * (age / 180f) : 1f;
                    bubble.style.scale = new Scale(new Vector3(pop, pop, 1f));
                }
            foreach (var kv in bubbles)
                if (kv.Value.userData == null && kv.Value.parent != null) kv.Value.RemoveFromHierarchy();
        }

        VisualElement MakeBubble(int seat)
        {
            var bubble = new VisualElement { name = "room-emote-" + seat.ToString(CultureInfo.InvariantCulture), pickingMode = PickingMode.Ignore };
            bubble.AddToClassList("sn-room-emote");
            var label = Add(bubble, Kit.Text("", Kit.F2), bubble.name + "-text", "sn-room-emote-text");
            label.pickingMode = PickingMode.Ignore;
            return bubble;
        }

        /// <summary>그 좌석의 말풍선에 지금 떠 있는 글(없으면 null — 시험 · 확인).</summary>
        public string EmoteShown(int seat) => bubbles.TryGetValue(seat, out var b) && b.parent != null ? ((Label)b[0]).text : null;

        // ── 도장 ──

        /// <summary>기록 줄을 눌렀다(<see cref="LogScreen.Row"/>가 건다): 방 판에서 남의 행동 줄이면 도장 메뉴를 그 줄 아래에 연다(같은 줄을 다시 누르면 닫는다). 열었으면 true.</summary>
        public bool RowClicked(BoardLogLine line, VisualElement row)
        {
            var live = Live;
            if (live == null || layer == null || line == null || flow.PingKey) return false;
            if (markLine != null && markLine.Seq == line.Seq && markLine.Seat == line.Seat)
            {
                CloseMarks();
                return false;
            }
            if (live.Seat < 0 || line.Seq < 0 || line.Seat < 0 || line.Seat == live.Seat) return false;
            markLine = line;
            markRow = row;
            markAt = live.NowMs;
            return true;
        }

        /// <summary>도장 메뉴를 그 행동의 그 좌석 줄에 연다(확인 · 시험 — 보이는 줄 가운데 맨 위에 뜬 창의 것). 남의 행동이 아니거나 줄이 안 보이면 false.</summary>
        public bool OpenMarks(int seq, int owner)
        {
            if (Stage.Root == null) return false;
            VisualElement row = null;
            Stage.Root.Query<VisualElement>(LogRowName(seq)).ForEach(e =>
            {
                if (e.userData is BoardLogLine l && l.Seat == owner && Shown(e) && !Hidden(e)) row = e;
            });
            return row != null && RowClicked((BoardLogLine)row.userData, row);
        }

        public void CloseMarks()
        {
            markLine = null;
            markRow = null;
        }

        /// <summary>도장 메뉴의 단추: 열어 둔 행동에 그 도장(0 대단해 · 1 헉 · 2 아깝다)을 찍고 메뉴를 닫는다. 연타 제한(1초에 1번 · 10초에 5번)이면 false(메뉴는 그대로).</summary>
        public bool PressMark(int code)
        {
            if (markLine == null || !flow.Room.Mark(markLine.Seq, markLine.Seat, code)) return false;
            CloseMarks();
            return true;
        }

        void CloseMenusOutside(Vector2 p)
        {
            if (markLine != null && !markMenu.worldBound.Contains(p) && (markRow == null || !markRow.worldBound.Contains(p))) CloseMarks();
            if (EmotesOpen && !emoteMenu.worldBound.Contains(p))
            {
                EmotesOpen = false;
                emoteKey = null;
            }
        }

        void Marks(RoomLive live, bool covered)
        {
            // 메뉴: 줄이 사라졌거나(다시 그림 · 창 닫힘) 시간이 지나면 닫는다
            if (markLine != null && (covered || live.NowMs - markAt >= MarkMenuMs || markRow == null || !Shown(markRow))) CloseMarks();
            markMenu.style.display = markLine != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (markLine != null)
            {
                // 누른 줄 위에 겹쳐(오른쪽 끝에 맞춰) — 어느 줄에 찍는지 보이게, 줄 글의 앞쪽은 가리지 않는다
                var b = markRow.worldBound;
                float w = markMenu.layout.width, h = markMenu.layout.height;
                bool sized = !float.IsNaN(w) && w > 0f && !float.IsNaN(h) && h > 0f;
                markMenu.style.left = sized ? Mathf.Max(b.xMin, b.xMax - w) : b.xMin;
                markMenu.style.top = sized ? b.yMin + (b.height - h) / 2f : b.yMin;
            }
            // 칩: 보이는 기록 줄 가운데 도장이 찍힌 행동의 첫 줄(판마다) 오른쪽 끝
            rowsFound.Clear();
            if (!covered && Stage.Root != null)
                Stage.Root.Query<VisualElement>(className: "sn-log-row").ForEach(e =>
                {
                    if (e.userData is BoardLogLine line && live.MarksOf(line.Seq, line.Seat) != null && live.MarksOf(line.Seq, line.Seat).Count > 0 && Shown(e) && !Hidden(e)) rowsFound.Add(e);
                });
            for (int i = rowsFound.Count - 1; i > 0; i--)
                for (int j = 0; j < i; j++)
                    if (rowsFound[j].parent == rowsFound[i].parent && ((BoardLogLine)rowsFound[j].userData).Seq == ((BoardLogLine)rowsFound[i].userData).Seq && ((BoardLogLine)rowsFound[j].userData).Seat == ((BoardLogLine)rowsFound[i].userData).Seat)
                    {
                        rowsFound.RemoveAt(i);
                        break;
                    }
            var key = new System.Text.StringBuilder();
            foreach (var e in rowsFound)
            {
                var b = e.worldBound;
                var l = (BoardLogLine)e.userData;
                key.Append(l.Seq).Append('/').Append(l.Seat).Append('@').Append(Mathf.RoundToInt(b.xMax)).Append(',').Append(Mathf.RoundToInt(b.yMin)).Append(':');
                foreach (var kv in live.MarksOf(l.Seq, l.Seat)) key.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            }
            string k = key.ToString();
            if (k == marksKey) return;
            marksKey = k;
            foreach (var c in markChips) c.RemoveFromHierarchy();
            markChips.Clear();
            var inv = CultureInfo.InvariantCulture;
            foreach (var e in rowsFound)
            {
                var line = (BoardLogLine)e.userData;
                var b = e.worldBound;
                var holder = Add(over, new VisualElement { pickingMode = PickingMode.Ignore }, "room-mark-" + markChips.Count.ToString(inv), "sn-room-mark");
                holder.userData = line;
                holder.style.left = b.xMax;
                holder.style.top = b.yMin;
                holder.style.height = b.height;
                // 칩 글 크기는 그 줄의 글 크기를 따른다(HUD 기록 판은 작은 글 · 기록 창은 본문 글)
                var text = e.Q<Label>(className: "sn-log-text");
                string size = text != null && text.ClassListContains(Kit.F2) ? Kit.F2 : Kit.F1;
                foreach (var kv in live.MarksOf(line.Seq, line.Seat))
                {
                    Color32 color = flow.SeatColor(kv.Key);
                    var chip = Add(holder, Kit.Text(Stage.T("screen.room.mark.kind." + Mathf.Clamp(kv.Value, 0, SocialCodes.Mark - 1).ToString(inv)), size), holder.name + "-seat-" + kv.Key.ToString(inv), "sn-room-mark-chip");
                    chip.pickingMode = PickingMode.Ignore;
                    chip.style.backgroundColor = new StyleColor((Color)color);
                    chip.style.color = new StyleColor((Color)SeatColors.Ink(color));
                }
                markChips.Add(holder);
            }
            MarksShown = markChips.Count;
        }

        /// <summary>그 행동의 그 좌석 줄에 붙은 도장 칩의 글들(찍은 좌석 순 — 없으면 빈 목록. 시험 · 확인).</summary>
        public List<string> MarkShown(int seq, int owner)
        {
            var found = new List<string>();
            foreach (var holder in markChips)
            {
                if (!(holder.userData is BoardLogLine l) || l.Seq != seq || l.Seat != owner) continue;
                foreach (var c in holder.Children()) found.Add(((Label)c).text);
                break;
            }
            return found;
        }
    }
}
