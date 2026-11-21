using System;
using System.Collections.Generic;
using System.Globalization;
using Rekkon.UiKit;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// S04 저장 목록(04 §1·§2·§9-12, 02 §7 — 솔로 저장 + 방 저장(M7 화면 ② — 「방 n명」, [이어하기] = S05 「방 다시 열기」 → S10)): 타이틀 「이어하기」로 연다. 저장 하나 = 칸 하나(새것부터, 읽을 수 없는 것은 뒤).
    /// 칸: 주인공 얼굴 상자(<see cref="FaceBox"/> — 좌석 색 = 고향 마을 색) · 「캠페인 · 막 · 라운드」 · 「고향 마을 · 저장 시각(현지)」 · [이어하기] [삭제] · 아래 줄에 파티 도트(정지 앞모습).
    /// [삭제]는 그 칸에서 한 번 더 묻는다(「이 저장을 삭제하겠습니까?」 [삭제] [취소]). 읽을 수 없는 저장은 흐리게 두고 까닭 한 줄과 저장 이름을 적으며 [삭제]만 있다.
    /// 끝난 판은 「끝난 게임」을 붙이고 [이어하기]가 없다 [가정]. 이어 받기가 실패하면(목록을 연 뒤 파일이 깨졌다) 그 칸에 「이 저장을 불러오지 못했습니다.」를 적고 화면은 그대로다.
    /// 한 쪽에 <see cref="PageRows"/>칸 — 넘으면 [이전]·[다음](다음 쪽 = 더 오래된 저장)(스크롤 막대를 쓰지 않는다 — 기록 창과 같다). 저장 목록은 화면을 만들 때 한 번, 그리고 지운 뒤에 다시 읽는다(머리만).
    /// 뒤의 보드 전경은 타이틀과 같다. 자리·크기는 <c>Styles~/Sinnoh.uss</c>의 <c>sn-save-*</c>, 글은 문자열 표 <c>screen.saves.*</c>.
    /// 요소 이름: <c>saves</c>·<c>saves-panel</c>·<c>saves-row-&lt;n&gt;</c>(그 쪽의 n째 칸)·<c>saves-face-&lt;n&gt;</c>·<c>saves-resume-&lt;n&gt;</c>·<c>saves-delete-&lt;n&gt;</c>·<c>saves-delete-yes</c>·<c>saves-delete-no</c>·
    /// <c>saves-empty</c>·<c>saves-page</c>·<c>saves-prev</c>·<c>saves-next</c>·<c>saves-back</c>.
    /// </summary>
    public sealed class SaveListScreen : IUiScreen, IOverBoard
    {
        /// <summary>한 쪽의 칸 수.</summary>
        public const int PageRows = 4;

        /// <summary>판 오른쪽 변(720 토큰 px — <c>sn-save-col</c>의 left 60 + width 500).</summary>
        public const float ColumnRight = 560f;

        readonly GameFlow flow;
        readonly List<SaveEntry> entries = new List<SaveEntry>();
        readonly List<string> lines = new List<string>();
        UiStage stage;
        VisualElement panel, foot;
        MonArt art;

        // 방 저장(M7 화면 ② — 방 저장 폴더 room-saves, 02 §7 [가정 M7-47])의 이름. 솔로 저장과 한 목록에 섞어 새것부터 보인다
        readonly HashSet<string> rooms = new HashSet<string>();

        public SaveListScreen(GameFlow flow)
        {
            this.flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Load();
        }

        void Load()
        {
            entries.Clear();
            rooms.Clear();
            entries.AddRange(flow.Saves());
            foreach (var e in flow.Room.Saves())
            {
                rooms.Add(e.SaveId);
                entries.Add(e);
            }
            SaveStore.Sort(entries);
        }

        /// <summary>그 저장이 방 저장인가(S04 「방 n명」 칸 — [이어하기]는 S05 「방 다시 열기」로).</summary>
        public bool IsRoom(string saveId) => saveId != null && rooms.Contains(saveId);

        public string Name => "saves";

        public float PanelRight => ColumnRight;

        /// <summary>목록(새것부터, 읽을 수 없는 것은 뒤).</summary>
        public IReadOnlyList<SaveEntry> Entries => entries;

        /// <summary>지금 보는 쪽(0부터)과 쪽 수.</summary>
        public int Page { get; private set; }
        public int Pages => Math.Max(1, (entries.Count + PageRows - 1) / PageRows);

        /// <summary>지울지 묻고 있는 저장(이름). 묻고 있지 않으면 null.</summary>
        public string Asking { get; private set; }

        /// <summary>이어 받기에 실패한 저장(이름) — 그 칸에 안내가 적힌다. 없으면 null.</summary>
        public string Failed { get; private set; }

        /// <summary>지금 쪽의 칸마다 첫 줄 글(위에서 아래로) — 시험·확인이 본다.</summary>
        public IReadOnlyList<string> Lines => lines;

        public VisualElement Build(UiStage stage)
        {
            this.stage = stage;
            art = MonArt.Load();
            var page = new VisualElement();
            var col = Add(page, new VisualElement(), "saves-col", "sn-save-col");
            Add(col, Kit.Text(stage.T("screen.saves.title"), Kit.FTitle), "saves-head", "sn-head");
            panel = Add(col, Kit.Panel(stage.Skin), "saves-panel", "sn-save-panel");
            foot = Add(col, new VisualElement(), "saves-foot", "sn-foot-row");
            Fill();
            return page;
        }

        public void Close()
        {
            stage = null;
        }

        /// <summary>[이어하기]: 그 저장을 이어 받아 보드로 간다. 이어 받을 수 없는 줄이거나 읽지 못하면 화면은 그대로다(false — 그 칸에 안내).</summary>
        public bool Resume(string saveId)
        {
            var entry = entries.Find(e => e.SaveId == saveId);
            if (entry == null || !GameFlow.CanResume(entry)) return false;
            if (IsRoom(saveId) ? flow.Room.OpenResume(saveId) : flow.Resume(saveId)) return true;   // 이 화면은 닫혔다(방 저장은 S05 「방 다시 열기」)
            Failed = saveId;
            Asking = null;
            Fill();
            return false;
        }

        /// <summary>[삭제]: 그 칸에서 한 번 더 묻는다.</summary>
        public void AskDelete(string saveId)
        {
            if (entries.Find(e => e.SaveId == saveId) == null) return;
            Asking = saveId;
            Fill();
        }

        /// <summary>[취소]: 묻기를 닫는다.</summary>
        public void CancelDelete()
        {
            Asking = null;
            Fill();
        }

        /// <summary>묻는 칸의 [삭제]: 그 저장을 지우고 목록을 다시 읽는다. 묻고 있지 않거나 지우지 못하면 false.</summary>
        public bool Delete()
        {
            if (Asking == null) return false;
            bool done = IsRoom(Asking) ? flow.Room.DeleteSave(Asking) : flow.DeleteSave(Asking);
            Asking = null;
            if (done)
            {
                Failed = null;
                Load();
                Page = Mathf.Clamp(Page, 0, Pages - 1);
            }
            Fill();
            return done;
        }

        /// <summary>쪽을 넘긴다(+1 = 다음 쪽 — 더 오래된 저장). 넘어갔으면 true.</summary>
        public bool Turn(int by)
        {
            int next = Mathf.Clamp(Page + by, 0, Pages - 1);
            if (next == Page) return false;
            Page = next;
            Asking = null;
            Fill();
            return true;
        }

        /// <summary>[돌아가기]: 타이틀의 「이어하기」 줄로(저장이 다 지워졌으면 타이틀이 그 줄을 끈다).</summary>
        public void Back() => flow.OpenTitle(TitleScreen.Continue);

        void Fill()
        {
            if (stage == null) return;
            stage.Tips?.Hide();
            panel.Clear();
            foot.Clear();
            lines.Clear();
            if (entries.Count == 0) Add(panel, Kit.Text(stage.T("screen.saves.empty"), Kit.F2), "saves-empty", "kit-muted");
            int start = Page * PageRows, end = Math.Min(entries.Count, start + PageRows);
            for (int i = start; i < end; i++) panel.Add(Row(entries[i], i - start));

            Add(foot, Kit.Button(stage.T("screen.common.back"), KitButtonKind.Normal, Back), "saves-back", null);
            if (Pages > 1)
            {
                var inv = CultureInfo.InvariantCulture;
                var prev = Add(foot, Kit.Button(stage.T("screen.log.older"), KitButtonKind.Normal, () => Turn(-1)), "saves-prev", null);
                var next = Add(foot, Kit.Button(stage.T("screen.log.newer"), KitButtonKind.Normal, () => Turn(1)), "saves-next", null);
                prev.SetEnabled(Page > 0);
                next.SetEnabled(Page < Pages - 1);
                Add(foot, Kit.Text((Page + 1).ToString(inv) + "/" + Pages.ToString(inv), Kit.F2), "saves-page", "sn-save-page");
            }
        }

        VisualElement Row(SaveEntry entry, int n)
        {
            string id = entry.SaveId;
            bool asking = Asking == id, failed = Failed == id;
            var row = new VisualElement { name = "saves-row-" + n, userData = id };
            row.AddToClassList("sn-save-row");
            var top = Add(row, new VisualElement(), null, "sn-save-top");
            string first, second, names = null, roomTip = null;
            if (entry.Readable)
            {
                var head = entry.Head;
                var seat = head.Summary.Seats.Count > SoloGame.Seat ? head.Summary.Seats[SoloGame.Seat] : null;
                string village = seat != null && seat.HomeVillage >= 0 && seat.HomeVillage < flow.Ctx.Fixtures.Fixtures.Count ? flow.Ctx.Fixtures.Fixtures[seat.HomeVillage].Key : null;
                string hero = head.Protagonists.Count > SoloGame.Seat ? head.Protagonists[SoloGame.Seat] : null;
                var face = Add(top, FaceBox.Make(), "saves-face-" + n, null);
                FaceBox.Paint(face, SoloGame.Seat, village != null ? SeatColors.ForVillage(village) : SeatColors.Unassigned, Protagonists.Has(hero) ? hero : Protagonists.Default);
                first = flow.SaveLine(head);
                if (head.Summary.Finished) first += " · " + stage.T("screen.saves.finished");
                string when = GameFlow.SaveTime(head.Summary.SavedAtMs);   // 날짜로 바꿀 수 없는 값이면 빈 글
                second = village != null && when.Length > 0 ? stage.T(village) + " · " + when : village != null ? stage.T(village) : when;
                if (IsRoom(id) && head.Room != null)
                {
                    // 방 저장: 「방 n명 · 캠페인 · 막 · 라운드」 / 함께 한 닉네임(결정 112 Q7 — 넘치면 「…」, 전체는 칸의 마우스 오버) / 저장 시각 [가정 M7-53]
                    first = stage.T("screen.saves.room").Replace("{n}", head.SeatCount.ToString(CultureInfo.InvariantCulture)) + " · " + first;
                    second = when;
                    var who = new List<string>();
                    foreach (var s in head.Room.Seats) who.Add(s.Nickname ?? "");
                    names = string.Join(" · ", who);
                    roomTip = names;
                }
            }
            else
            {
                row.AddToClassList("sn-off");
                first = stage.T("screen.saves.unreadable");
                second = stage.T(entry.Problem == SaveProblem.Broken ? "screen.saves.problem.broken" : entry.Problem == SaveProblem.DataChanged ? "screen.saves.problem.data" : "screen.saves.problem.version");
            }
            lines.Add(first);
            var info = Add(top, new VisualElement(), null, "sn-save-info");
            if (!entry.Readable) info.AddToClassList("sn-save-bare");
            if (roomTip != null) stage.Tips.Hook(info, null, roomTip, TipSide.Below, reading: true, clear: panel);
            Add(info, Kit.Text(first, Kit.F2), null, entry.Readable ? "kit-title" : null);
            if (asking) Add(info, Kit.Text(stage.T("screen.saves.delete.ask"), Kit.F2), "saves-ask", null);
            else if (failed) Add(info, Kit.Text(stage.T("screen.saves.failed"), Kit.F2), "saves-failed", "sn-save-warn");
            else
            {
                if (names != null) Add(info, Kit.Text(names, Kit.F2), "saves-names-" + n, "sn-save-names");
                Add(info, Kit.Text(second, Kit.F2), null, "kit-muted");
            }

            var btns = Add(top, new VisualElement(), null, "sn-save-btns");
            if (asking)
            {
                Add(btns, Kit.Button(stage.T("screen.saves.delete"), KitButtonKind.End, () => Delete()), "saves-delete-yes", null);
                Add(btns, Kit.Button(stage.T("screen.common.cancel"), KitButtonKind.Normal, CancelDelete), "saves-delete-no", null);
            }
            else
            {
                if (GameFlow.CanResume(entry) && !failed) Add(btns, Kit.Button(stage.T("screen.saves.resume"), KitButtonKind.Main, () => Resume(id)), "saves-resume-" + n, null);
                Add(btns, Kit.Button(stage.T("screen.saves.delete"), KitButtonKind.Normal, () => AskDelete(id)), "saves-delete-" + n, null);
            }

            // 아래 줄: 파티 도트(읽을 수 없는 저장은 저장 이름 — 어느 폴더인지 알 수 있게)
            var party = Add(row, new VisualElement(), "saves-party-" + n, "sn-save-party");
            if (entry.Readable)
            {
                party.AddToClassList("sn-save-dots");
                var seat = entry.Head.Summary.Seats.Count > SoloGame.Seat ? entry.Head.Summary.Seats[SoloGame.Seat] : null;
                if (seat != null && art != null)
                    foreach (int mon in seat.Party)
                        if (mon >= 0 && mon < flow.Ctx.Cards.Pokemon.Count) party.Add(art.Front(flow.Ctx.Cards.Pokemon[mon].Key));
            }
            else Add(party, Kit.Text(id, Kit.F1), null, null);
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
