using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 표시 상태(04 §8-3): 화면이 보기(<c>ViewFor</c>)와 따로 갖는 「표시 값」. 보기는 갱신을 받는 순간 최종 값이 되지만, 화면의 숫자·막대는
    /// 연출 단서가 닿을 때 이벤트의 Before → After로 한 칸씩 간다(착지 = 보이는 값이 바뀌는 순간, P9).
    /// 항목이 대기열에 들어올 때 그 항목이 바꿀 값을 Before로 붙잡고(<see cref="Hold"/>), 단서가 닿으면 After로 바꾸고(<see cref="Land"/>),
    /// 대기열이 비면 모두 놓는다(<see cref="Clear"/>) — 그때부터 보기와 같다. 어긋나면 보기를 따른다.
    /// 화면 부품은 값을 그릴 때 늘 <see cref="Get"/>(키, 보기의 값)으로 읽는다 — 기능마다 숨기기·옛 값 붙잡기를 따로 짜지 않는다.
    /// </summary>
    public sealed class FxShown
    {
        readonly Dictionary<ShownKey, int> held = new Dictionary<ShownKey, int>();

        /// <summary>붙잡고 있는 값의 수(0이면 화면이 보기와 같다).</summary>
        public int Count => held.Count;

        /// <summary>화면에 그릴 값: 붙잡은 값이 있으면 그것, 없으면 보기의 값.</summary>
        public int Get(ShownKey key, int viewValue) => held.TryGetValue(key, out int v) ? v : viewValue;

        public bool Holds(ShownKey key) => held.ContainsKey(key);

        /// <summary>아직 재생하지 않은 항목이 바꿀 값을 그 전 값으로 붙잡는다. 이미 붙잡은 키는 그대로 둔다(앞 항목의 값이 먼저다).</summary>
        public void Hold(ShownKey key, int before)
        {
            if (!held.ContainsKey(key)) held.Add(key, before);
        }

        /// <summary>단서가 닿았다: 그 값이 After가 된다.</summary>
        public void Land(ShownKey key, int after) => held[key] = after;

        /// <summary>대기열이 비었다: 붙잡은 값을 모두 놓는다(보기와 같아진다).</summary>
        public void Clear() => held.Clear();

        /// <summary>
        /// 이벤트가 싣고 온 Before → After(01 §3-6 ③)를 항목의 표시 값 단서로 단다. 지금 아는 값: HP(<c>HpChanged</c>·<c>Healed</c>, 독·혼란 피해는 <c>StatusTicked</c> —
        /// 그 피해로 HpChanged가 따로 오지 않는다), 피로 칸(<c>FatigueChanged</c>), 좌석 자원(<c>Changes[]</c> — 규토리·JP·볼·운명 장수·아이템, 주운 규토리는 <c>ApricornsTaken</c>의 필드).
        /// 말 위치·상태 아이콘·덱 장수 같은 나머지는 그 화면을 만들 때 여기에 더한다. 남에게 가려진 값(null)·채우지 않은 값(U)은 건너뛴다.
        /// Before/After를 싣는 이벤트 타입을 새로 만들면 여기에 더하거나 시험 M4_값을싣는이벤트는모두단서가되거나제외의 제외 목록에 이유와 함께 적는다.
        /// </summary>
        public static void AddCues(FxEntry entry, GameEvent e, int atMs)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            switch (e)
            {
                case HpChanged h:
                    entry.AddShown(atMs, ShownKey.Hp(h.PokemonId), h.Before, h.After);
                    break;
                case StatusTicked t:
                    if (t.HpBefore != GameEvent.U && t.HpAfter != GameEvent.U) entry.AddShown(atMs, ShownKey.Hp(t.PokemonId), t.HpBefore, t.HpAfter);
                    break;
                case FatigueChanged f:
                    entry.AddShown(atMs, ShownKey.Fatigue(f.PokemonId), f.Before, f.After);
                    break;
                case Healed heal:
                    if (heal.Pokemon != null)
                        foreach (var p in heal.Pokemon)
                            if (p != null) entry.AddShown(atMs, ShownKey.Hp(p.Id), p.HpBefore, p.HpAfter);
                    break;
                case ApricornsTaken a:
                    if (a.Before != GameEvent.U && a.After != GameEvent.U) entry.AddShown(atMs, ShownKey.Resource(a.Seat, ResKind.Apricorn, a.Color), a.Before, a.After);
                    break;
                case ApricornSpent _:
                case ApricornsGained _:
                case WeatherSet _:
                case Fled _:
                case CaptureAttempt _:
                case BattleEnded _:
                case ActGoalMet _:
                case Crafted _:
                case GinkgoTraded _:
                case SpoilsTaken _:
                case ShrineOffered _:
                case ShrineBlessing _:
                case StampGained _:
                case TradeDone _:
                    Res(entry, ChangesOf(e), atMs);   // 좌석 자원 Changes[](날씨 바꾸기의 JP 3 — parasol_lady B, rulings A41 · 거래 — rulings A29)
                    break;
                // 운명 손패 장수(M5 「운명·응답 화면」 — [운명] 단추의 장수는 카드가 손에 닿을 때 바뀐다)
                case FateDealt x:
                    if (x.PerSeat != null)
                        foreach (var d in x.PerSeat)
                            if (d != null) entry.AddShown(atMs, ShownKey.FateHand(d.Seat), d.Before, d.After);
                    break;
                case FateDiscarded x: entry.AddShown(atMs, ShownKey.FateHand(x.Seat), x.Before, x.After); break;
                case FatePlayed x: entry.AddShown(atMs, ShownKey.FateHand(x.Seat), x.Before, x.After); break;
                case Evolved x:
                    Res(entry, ChangesOf(e), atMs);
                    if (x.Pokemon != null)
                        foreach (var p in x.Pokemon)
                            if (p != null) entry.AddShown(atMs, ShownKey.Hp(p.Id), p.HpBefore, p.HpAfter);
                    break;
            }
        }

        /// <summary>그 이벤트가 싣는 좌석 자원 변화(Changes[] — 없으면 null). 단서(<see cref="AddCues"/>)와 이벤트 없는 여행 점수 변화 알림(<see cref="JpDelta"/>)이 같이 쓴다.</summary>
        public static ResChange[] ChangesOf(GameEvent e)
        {
            switch (e)
            {
                case ApricornSpent x: return x.Changes;
                case ApricornsGained x: return x.Changes;
                case WeatherSet x: return x.Changes;
                case Fled x: return x.Changes;
                case CaptureAttempt x: return x.Changes;
                case BattleEnded x: return x.Changes;
                case ActGoalMet x: return x.Changes;
                case Crafted x: return x.Changes;
                case GinkgoTraded x: return x.Changes;
                case SpoilsTaken x: return x.Changes;
                case ShrineOffered x: return x.Changes;
                case ShrineBlessing x: return x.Changes;
                case StampGained x: return x.Changes;
                case TradeDone x: return x.Changes;
                case Evolved x: return x.Changes;
                default: return null;
            }
        }

        /// <summary>그 이벤트들이 실은 그 좌석의 여행 점수 변화 합(M6 — 화면이 보기의 차이에서 빼서 이벤트 없는 변화를 알린다).</summary>
        public static int JpDelta(IReadOnlyList<GameEvent> events, int seat)
        {
            int sum = 0;
            if (events == null) return 0;
            foreach (var e in events)
            {
                var changes = ChangesOf(e);
                if (changes == null) continue;
                foreach (var c in changes)
                    if (c != null && c.Seat == seat && c.Res == ResKind.Jp && c.Before != GameEvent.U && c.After != GameEvent.U) sum += c.After - c.Before;
            }
            return sum;
        }

        static void Res(FxEntry entry, ResChange[] changes, int atMs)
        {
            if (changes == null) return;
            foreach (var c in changes)
                if (c != null) entry.AddShown(atMs, ShownKey.Resource(c.Seat, c.Res, c.Key), c.Before, c.After);
        }
    }
}
