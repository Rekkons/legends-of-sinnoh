using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 남의 행동을 내 턴과 같은 창으로 보여 주는 보기 전용 창(결정 120 ① · 플레이 시험 T6 · T7 · T9): 제작 · 은행상회 · 진화 · 사당(공물 · 카드 고르기 · 축복) · 조우 고르기.
    /// 누를 단추가 없고(보기 전용) 볼 것이 끝나면(그 단계가 끝났다 · 그 사람이 다음으로 넘어갔다) 저절로 닫힌다.
    /// </summary>
    public interface IWatchScreen : IUiScreen
    {
        /// <summary>보는 좌석(−1 = 내 창 — 보기 전용이 아니다).</summary>
        int Watched { get; }

        /// <summary>아직 보일 것이 남았나(보는 단계 · 그 좌석의 공개 선택).</summary>
        bool Live { get; }

        void Refresh();

        void ShowFx(FxSequencer seq);
    }

    /// <summary>
    /// 남의 행동 보기(결정 120 ①): 무대의 화면(<see cref="UiStage.Current"/>)이 아니라 보드 HUD 요소의 자식으로 얹는다(<c>board-watch</c>) — 내 창(정비 · 트레이너 · 운명의 변화 · 기록 …)이나
    /// 게임 메뉴 · 배틀 화면이 떠 있으면 그 아래로 숨고(하던 일을 덮지 않는다 — 결정 22 Q6), 닫으면 아직 볼 것이 남았을 때 다시 보인다. 그래서 내 창을 여는 길 · 답해야 하는 창의 자동 열기는 그대로다.
    /// 여는 때: 그 좌석의 공개 선택(조우 고르기 — 카드 공개 21행이 닿은 뒤 · 사당 카드 고르기)이 열렸거나, 남 좌석의 그 행 단계(<see cref="OtherActs"/> — 제작 36·37 · 은행상회 18 · 진화 39 · 공물 31 · 축복 32)가 재생 중일 때.
    /// 길이는 새로 만들지 않는다 — 남 좌석의 단계는 방장 멈춤과 같은 길이로 이미 대기열에 있다(<c>PlayTiming</c>). [닫기] · Esc로 접으면 그 좌석의 그 갈래는 그 턴 동안 다시 열지 않는다.
    /// 트레이너 카드 해금 · 바꾸기 · 능력(48 · 49 · 50행)은 창이 아니라 덮개 <see cref="CardFx"/>가 내 것과 같게 그린다(<see cref="ShowsOthers"/>). 좌석 하나 판 · 튜토리얼은 열지 않는다.
    /// </summary>
    public sealed partial class GameFlow
    {
        IWatchScreen watch;
        VisualElement watchPage;
        string watchKey, watchShut;

        /// <summary>지금 얹힌 보기 창(없으면 null — 숨겨져 있을 수 있다, <see cref="WatchShown"/>).</summary>
        public IWatchScreen Watch => watch;

        /// <summary>보기 창이 지금 화면에 보이나.</summary>
        public bool WatchShown { get; private set; }

        /// <summary>남의 행동을 보기 창으로 보이는 판인가: 보드 · 좌석 둘 이상 · 튜토리얼이 아니다.</summary>
        bool WatchOthers => OnBoard && Client.View.Seats.Length >= 2 && !Tutorial.Running;

        /// <summary>남 좌석의 덮개 연출(트레이너 카드 48 · 49 · 50행)을 내 것처럼 그릴 때인가: 보기 판 · 내 창 · 메뉴가 없다.</summary>
        public bool ShowsOthers => WatchOthers && Stage.Current == null && !MenuUp;

        /// <summary>그 좌석이 그 자리에서 가리키는 것(방 판의 「남의 고르기 보기」 — 없으면 null).</summary>
        public RoomPeek PeekOf(int seat, PeekSpot spot)
        {
            var live = Room != null ? Room.Live : null;
            var m = live != null ? live.PeekOf(seat) : null;
            return m != null && m.Spot == spot ? m : null;
        }

        // 갱신 · 프레임마다: 끝난 보기 창을 닫고, 볼 것이 있으면 연다. 내 창 · 메뉴가 떠 있으면 숨긴다
        void SyncWatch()
        {
            var hud = Hud != null && Stage.Root != null ? Stage.Root.Q(Hud.Name) : null;
            if (watch != null && (!WatchOthers || hud == null || watchPage == null || (watchPage.parent != null && watchPage.parent != hud) || !watch.Live)) ShutWatch();
            if (watch == null && hud != null && WatchOthers && Stage.Current == null && !MenuUp)
            {
                var next = NextWatch(out string key);
                if (next != null && key != watchShut) OpenWatch(next, key, hud);
            }
            if (watch == null)
            {
                WatchShown = false;
                return;
            }
            if (watchPage.parent == null) hud.Add(watchPage);
            bool show = Stage.Current == null && !MenuUp;
            watchPage.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            WatchShown = show;
        }

        // 지금 볼 남의 행동(없으면 null): 공개 선택이 먼저, 그다음 재생 중인 남 좌석 단계
        IWatchScreen NextWatch(out string key)
        {
            key = null;
            var view = Client.View;
            int me = Client.Seat, turn = view.TurnId;
            var seq = Fx != null ? Fx.Sequencer : null;
            for (int s = 0; s < view.Seats.Length; s++)
            {
                if (s == me) continue;
                var c = PeekRules.Open(view, s, PeekSpot.Encounter);
                if (c != null && c.Options.Count > 0 && !c.Options.Contains(Engine.Unknown) && EnterRows.Reached(seq, EnterRows.Reveal))
                {
                    key = "encounter|" + s + "|" + turn;
                    return new EncounterScreen(this, s);
                }
                if (PartyPlaying(seq, s)) continue;   // 그 좌석의 판 보기(정비 · 사당 보상 380행)가 먼저 — 아래 단계 고리가 연다
                c = PeekRules.Open(view, s, PeekSpot.Shrine);
                if (c != null && c.Options.Count > 0 && !c.Options.Contains(Engine.Unknown))
                {
                    key = "shrine|" + s + "|" + turn;
                    return new ShrineScreen(this, false, watch: s);
                }
            }
            var cur = seq != null ? seq.Current : null;
            if (cur == null) return null;
            foreach (var step in cur.Steps)
            {
                if (seq.LocalMs < step.StartMs || seq.LocalMs >= step.EndMs || !OtherActs.Shows(step, me, view.Seats.Length)) continue;
                int s = WorkRows.SeatOf(step);
                switch (step.Row)
                {
                    case WorkRows.Craft:
                    case WorkRows.Stew:
                        key = "craft|" + s + "|" + turn;
                        return new CraftScreen(this, s);
                    case WorkRows.Bank:
                        key = "bank|" + s + "|" + turn;
                        return new VillageScreen(this, watch: s);
                    case WorkRows.Maint:
                        key = "maint|" + s + "|" + turn;
                        return new PartyScreen(this, s, true);
                    case WorkRows.Evolve:
                        key = "evolve|" + s + "|" + turn;
                        return new EvolveScreen(this, s);
                    case ShrineRows.Offer:
                    case ShrineRows.Blessing:
                        key = "shrine|" + s + "|" + turn;
                        return new ShrineScreen(this, false, watch: s);
                }
            }
            return null;
        }

        /// <summary>그 좌석의 정비 보기 단계(380행 — 정비 · 사당의 그다음 고르기 결과)가 지금 재생 중인가.</summary>
        public static bool PartyPlaying(FxSequencer seq, int seat)
        {
            var step = WorkRows.Playing(seq, WorkRows.Maint, out int local);
            return step != null && local < step.Ms && WorkRows.SeatOf(step) == seat;
        }

        void OpenWatch(IWatchScreen screen, string key, VisualElement hud)
        {
            watch = screen;
            watchKey = key;
            watchPage = screen.Build(Stage);
            watchPage.name = "board-watch";
            watchPage.AddToClassList("sn-screen");
            watchPage.pickingMode = PickingMode.Ignore;
            hud.Add(watchPage);
        }

        void ShutWatch()
        {
            if (watch == null) return;
            Stage.Tips?.Hide();
            watchPage?.RemoveFromHierarchy();
            var was = watch;
            watch = null;
            watchPage = null;
            watchKey = null;
            WatchShown = false;
            was.Close();
        }

        /// <summary>보기 창의 [닫기] · Esc: 접는다 — 그 좌석의 그 갈래는 그 턴 동안 다시 열지 않는다. 접었으면 true.</summary>
        public bool CloseWatch()
        {
            if (watch == null) return false;
            watchShut = watchKey;
            ShutWatch();
            return true;
        }
    }
}
