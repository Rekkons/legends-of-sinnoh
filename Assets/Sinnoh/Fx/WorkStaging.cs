using System;
using System.Collections.Generic;
using Rekkon.UiKit;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 제작·은행·진화 화면 묶음(M5)의 연출 행(04 §8-8 18·36·37·39행): 은행상회 거래 · 제작 · 스튜 효과 · 진화. 길이는 방장과 같은 값(<c>PlayTiming</c> 180·360·370·390행)이고
    /// 박자는 아래 <see cref="BankBeats"/>·<see cref="CraftBeats"/>·<see cref="StewBeats"/>·<see cref="EvolveBeats"/>가 정한다. 넷 다 그 행동을 낸 좌석의 창(마을 창 · 제작 창 · 진화 창)이 그린다 —
    /// 남 좌석의 것은 창이 그리지 않고 그 좌석 칩의 꼬리표(<see cref="OtherActs"/> — 결정 80 N2 나)로 보인다. 내 것인데 그릴 창이 없으면(화려한 만찬의 스튜 등) 기록 줄만 남고 대기열에서 덜어 낸다.
    /// </summary>
    public static class WorkRows
    {
        public const int Bank = 180, Craft = 360, Stew = 370, Evolve = 390;
        /// <summary>정비 보기(38·40·41행 — 남 좌석의 지닌물건 · 기술 · 놓아주기 · 순서, 결정 120): 남 화면만 길이가 있다(PlayTiming.MaintRow).</summary>
        public const int Maint = PlayTiming.MaintRow;

        /// <summary>이 묶음이 그리는 행인가.</summary>
        public static bool Built(int row) => row == Bank || row == Craft || row == Stew || row == Evolve || row == Maint;

        /// <summary>그 정비 보기 단계가 사당의 그다음 고르기 결과(동등한 교환 · 뜻밖의 선물의 규토리)인가 — 꼬리표 · 제목이 「축복」 쪽이다.</summary>
        public static bool Gift(FxStep step)
        {
            if (step == null || step.Row != Maint) return false;
            foreach (var e in step.Events) if (PlayTiming.IsShrineGift(e)) return true;
            return false;
        }

        /// <summary>정비 단계에서 바뀐 파티 포켓몬(지닌물건을 받은 · 내준 · 기술이 바뀐 — 보기 창이 그 카드에 좌석 색 테두리를 두른다).</summary>
        public static List<int> MaintChanged(FxStep step)
        {
            var ids = new List<int>();
            if (step == null || step.Row != Maint) return ids;
            foreach (var e in step.Events)
                switch (e)
                {
                    case ItemHeld h:
                        if (h.PokemonId >= 0) ids.Add(h.PokemonId);
                        if (h.FromPokemonId >= 0) ids.Add(h.FromPokemonId);
                        break;
                    case MoveTaught t: if (t.PokemonId >= 0) ids.Add(t.PokemonId); break;
                    case MoveForgotten f: if (f.PokemonId >= 0) ids.Add(f.PokemonId); break;
                    case PokemonExchanged x: if (x.PokemonId >= 0) ids.Add(x.PokemonId); break;
                }
            return ids;
        }

        /// <summary>그 단계의 행동을 낸 좌석(단계의 첫 이벤트 좌석 — 없으면 −1).</summary>
        public static int SeatOf(FxStep step) => step != null && step.Events.Count > 0 ? step.Events[0].Seat : -1;

        /// <summary>그 단계에서 그 종류의 이벤트(없으면 null — 은행상회는 두 줄이면 첫 줄).</summary>
        public static T EventOf<T>(FxStep step) where T : GameEvent
        {
            if (step == null) return null;
            foreach (var e in step.Events)
                if (e is T t) return t;
            return null;
        }

        /// <summary>지금 재생 중인 항목의 그 행 단계와 단계 안 시각(없으면 null · −1).</summary>
        public static FxStep Playing(FxSequencer seq, int row, out int localMs)
        {
            localMs = -1;
            var cur = seq != null ? seq.Current : null;
            var step = cur != null ? cur.Step(row) : null;
            if (step == null || seq.LocalMs < step.StartMs) return null;
            localMs = Math.Min(seq.LocalMs - step.StartMs, step.Ms);
            return step;
        }
    }

    /// <summary>
    /// 남 좌석의 공물 310 · 축복 320 · 은행상회 180 · 제작 360 · 스튜 370 · 진화 390 · 트레이너 카드 해금 480 · 바꾸기 490 · 능력 500(결정 80 N2 나): 그 좌석의 창·사당 판은 내 화면에 없으니
    /// 단계를 대기열에 그대로 두고(방장과 같은 길이 — 새 행을 만들지 않는다) 그동안 그 좌석 칩 아래에 짧은 꼬리표를 건다(<c>BoardHud</c>).
    /// 좌석이 하나뿐인 판 · 내 것 · 내가 나눠 준 축복(사당 판이 그린다)은 꼬리표가 아니다.
    /// </summary>
    public static class OtherActs
    {
        /// <summary>꼬리표를 거는 행이면 화면 문구 키 꼬리(<c>screen.board.did.&lt;키&gt;</c>), 아니면 null.</summary>
        public static string Key(int row)
        {
            switch (row)
            {
                case ShrineRows.Offer: return "offer";
                case ShrineRows.Blessing: return "bless";
                case WorkRows.Bank: return "bank";
                case WorkRows.Craft: case WorkRows.Stew: return "craft";
                case WorkRows.Evolve: return "evolve";
                case WorkRows.Maint: return "maint";
                case CardRows.Unlock: return "unlock";   // M6 「트레이너 카드 해금 연출」: 남 좌석의 해금 · 카드 바꾸기 · 능력 사용(결정 80 마 꼴)
                case CardRows.Swap: return "swap";
                case CardRows.Ability: return "ability";
                default: return null;
            }
        }

        /// <summary>그 단계의 꼬리표 문구 키 꼬리: <see cref="Key"/>와 같고 해금만 오른 등급을 붙인다(「unlock_2」 = 실버 · 「unlock_3」 = 골드).</summary>
        public static string KeyOf(FxStep step)
        {
            if (step == null) return null;
            var u = step.Row == CardRows.Unlock ? WorkRows.EventOf<CardUnlocked>(step) : null;
            if (u != null) return "unlock_" + Math.Max(2, Math.Min(3, u.TierAfter)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return WorkRows.Gift(step) ? "bless" : Key(step.Row);   // 사당의 그다음 고르기 결과는 「축복」
        }

        /// <summary>그 단계가 남 좌석의 꼬리표인가(좌석 me의 화면 · 판의 좌석 수 seats).</summary>
        public static bool Shows(FxStep step, int me, int seats)
        {
            if (step == null || seats < 2 || Key(step.Row) == null) return false;
            int seat = WorkRows.SeatOf(step);
            if (seat < 0 || seat == me) return false;
            foreach (var e in step.Events)
                if (e.Seat == me || (e is ShrineBlessing b && b.From == me)) return false;
            return true;
        }

        /// <summary>지금 재생 중인 남 좌석 꼬리표 단계와 단계 안 시각(없으면 null · −1).</summary>
        public static FxStep Playing(FxSequencer seq, int me, int seats, out int localMs)
        {
            localMs = -1;
            var cur = seq != null ? seq.Current : null;
            if (cur == null) return null;
            foreach (var step in cur.Steps)
            {
                if (seq.LocalMs < step.StartMs || seq.LocalMs >= step.EndMs || !Shows(step, me, seats)) continue;
                localMs = seq.LocalMs - step.StartMs;
                return step;
            }
            return null;
        }

        /// <summary>꼬리표가 나타나고 사라지는 시간(ms) · 떠오르는 높이(1배 px).</summary>
        public const int InMs = 160, OutMs = 240, RisePx = 6;

        /// <summary>단계 안 시각 t(길이 ms)의 꼬리표 불투명도(0~1)와 떠오른 정도(0~1 — 나타날 때만).</summary>
        public static float Alpha(int t, int ms, out float rise)
        {
            rise = t < InMs ? 1f - Math.Max(0, t) / (float)InMs : 0f;
            if (t < InMs) return Math.Max(0, t) / (float)InMs;
            int left = ms - t;
            return left < OutMs ? Math.Max(0, left) / (float)OutMs : 1f;
        }
    }

    /// <summary>
    /// 제작(04 §8-8 36행, S26 — 볼 700 · 그 밖 1000, 플레이 시험 T11 — 옛 1500을 줄이며 박자도 당김)의 박자: 예비 = 레시피 칸이 빛남 → 움직임 = 재료 규토리가 색마다 완성품 쪽으로 빨려 듦(<see cref="SuckEveryMs"/> 어긋남 — 04 표의 80ms보다 짧다: 재료 여섯도 펑 전에 닿게) →
    /// 맞음 = 완성품 둘레가 하얗게 펑(<see cref="FlashAt"/>) → 착지 = 완성품이 커졌다 돌아오고 「+1」이 떠오름. 스튜의 김은 그리지 않는다 [가정].
    /// </summary>
    public static class CraftBeats
    {
        public const int Row = WorkRows.Craft;
        public const int GlowMs = 200, SuckFromMs = 100, SuckEveryMs = 30, SuckMs = 180, FlashAt = 460, FlashMs = 150, PopMs = 220, PlusMs = 300;

        /// <summary>레시피 칸의 빛(0 ~ 1): 처음 100ms에 차오르고 GlowMs까지 반으로 줄어 완성까지 남는다.</summary>
        public static float Glow(int ms)
        {
            if (ms < 0) return 0f;
            if (ms < 100) return Tween.At(ms, 0, 100, 0f, 1f, Ease.OutQuad);
            if (ms < GlowMs) return Tween.At(ms, 100, GlowMs - 100, 1f, 0.5f);
            return ms < FlashAt + FlashMs ? 0.5f : Tween.At(ms, FlashAt + FlashMs, 200, 0.5f, 0f);
        }

        /// <summary>i번째(레시피 순서) 재료가 빨려 든 만큼(0 = 제자리 ~ 1 = 완성품에 닿아 사라짐).</summary>
        public static float Suck(int ms, int i) => Tween.At(ms, SuckFromMs + SuckEveryMs * Math.Max(0, i), SuckMs, 0f, 1f, Ease.InQuad);

        /// <summary>완성품 둘레의 흰 빛(0 ~ 1).</summary>
        public static float Flash(int ms) => ms < FlashAt || ms >= FlashAt + FlashMs ? 0f : Tween.At(ms, FlashAt, FlashMs, 1f, 0f, Ease.OutQuad);

        /// <summary>완성품의 배율(1 = 그대로): 펑 뒤 1.5까지 커졌다 돌아온다.</summary>
        public static float Pop(int ms)
        {
            if (ms < FlashAt) return 1f;
            int t = ms - FlashAt;
            if (t < PopMs / 3) return Tween.At(t, 0, PopMs / 3, 1f, 1.5f, Ease.OutQuad);
            return Tween.At(t, PopMs / 3, PopMs - PopMs / 3, 1.5f, 1f, Ease.InOutQuad);
        }

        /// <summary>「+1」이 보이는 만큼(0 ~ 1)과 떠오른 만큼(0 ~ 1).</summary>
        public static float Plus(int ms) => ms < FlashAt ? 0f : Tween.At(ms, FlashAt, 120, 0f, 1f);

        public static float PlusRise(int ms) => ms < FlashAt ? 0f : Tween.At(ms, FlashAt, PlusMs, 0f, 1f, Ease.OutQuad);
    }

    /// <summary>
    /// 스튜 효과(04 §8-8 37행 — 1500)의 박자: 스튜 그릇이 통통 두 번 튀고(<see cref="Bounce"/>) 효과 꼬리표가 톡 뜬다(<see cref="BadgeAt"/>).
    /// 회복스튜의 체력이 차오르는 것은 뒤따르는 회복 행(17행 — HUD 파티 칸)이 그린다. 이동력 칩 · 하트 배지 · 진화 비용이 쪼개지는 그림은 없다 — 꼬리표가 효과를 적는다 [가정].
    /// </summary>
    public static class StewBeats
    {
        public const int Row = WorkRows.Stew;
        public const int FirstMs = 260, SecondMs = 200, BadgeAt = 480, BadgeMs = 160;

        /// <summary>그릇이 떠오른 높이(0 ~ 1): 첫 튐 1 · 둘째 튐 0.5.</summary>
        public static float Bounce(int ms)
        {
            if (ms < 0 || ms >= FirstMs + SecondMs) return 0f;
            if (ms < FirstMs) return (float)Math.Sin(Math.PI * ms / FirstMs);
            return 0.5f * (float)Math.Sin(Math.PI * (ms - FirstMs) / SecondMs);
        }

        /// <summary>효과 꼬리표가 보이는 만큼(0 ~ 1)과 배율(0.6 → 1).</summary>
        public static float Badge(int ms) => ms < BadgeAt ? 0f : Tween.At(ms, BadgeAt, BadgeMs, 0f, 1f, Ease.OutQuad);

        public static float BadgeScale(int ms) => 0.6f + 0.4f * Badge(ms);
    }

    /// <summary>
    /// 은행상회 거래(04 §8-8 18행, S24 — 2500)의 박자: 예비 = 고른 줄이 빛남 → 움직임 = 낸 규토리가 왼쪽으로 빨려 듦(80ms 어긋남) →
    /// 맞음 = 받은 규토리가 오른쪽에서 튀어나옴 → 착지 = 순증감 숫자 · 거래 카드가 짧게 흔들리며 섞이고 새 맨 위 카드로 뒤집힘(<see cref="FlipAt"/>의 가운데에서 바뀜).
    /// 두 줄을 다 하면 두 줄의 규토리가 한 줄로 이어 흐른다(같은 단계 — 행마다 숫자 하나).
    /// </summary>
    public static class BankBeats
    {
        public const int Row = WorkRows.Bank;
        public const int GlowMs = 300, GiveFromMs = 300, GetFromMs = 1100, EveryMs = 80, IconMs = 300, NetAt = 1800, ShakeAt = 1900, ShakeMs = 300, FlipAt = 2200, FlipMs = 300;

        public static float Glow(int ms) => ms < 0 ? 0f : ms < 120 ? Tween.At(ms, 0, 120, 0f, 1f) : ms < NetAt ? Tween.At(ms, 120, GlowMs, 1f, 0.6f) : Tween.At(ms, NetAt, 200, 0.6f, 0f);

        /// <summary>낸 i번째 규토리가 빨려 든 만큼(0 ~ 1).</summary>
        public static float Give(int ms, int i) => Tween.At(ms, GiveFromMs + EveryMs * Math.Max(0, i), IconMs, 0f, 1f, Ease.InQuad);

        /// <summary>받은 j번째 규토리가 튀어나온 만큼(0 = 아직 ~ 1 = 제자리).</summary>
        public static float Get(int ms, int j) => Tween.At(ms, GetFromMs + EveryMs * Math.Max(0, j), IconMs, 0f, 1f, Ease.OutQuad);

        /// <summary>받은 j번째 규토리가 튀어나오기 시작하는 단계 안 시각.</summary>
        public static int GetStart(int j) => GetFromMs + EveryMs * Math.Max(0, j);

        /// <summary>순증감 숫자가 보이는 만큼.</summary>
        public static float Net(int ms) => ms < NetAt ? 0f : Tween.At(ms, NetAt, 150, 0f, 1f);

        /// <summary>섞일 때 카드가 옆으로 흔들린 만큼(−1 ~ 1 — 화면이 px로 옮긴다).</summary>
        public static float Shake(int ms) => ms < ShakeAt || ms >= ShakeAt + ShakeMs ? 0f : (float)Math.Sin(Math.PI * 4 * (ms - ShakeAt) / ShakeMs) * (1f - (ms - ShakeAt) / (float)ShakeMs);

        /// <summary>새 맨 위 카드로 뒤집히는 가로 배율(1 → 0 → 1)과 새 카드가 보이나(뒤집기 가운데부터).</summary>
        public static float Flip(int ms, out bool fresh)
        {
            fresh = ms >= FlipAt + FlipMs / 2;
            if (ms < FlipAt || ms >= FlipAt + FlipMs) return 1f;
            float t = (ms - FlipAt) / (float)FlipMs;
            return Math.Abs(1f - 2f * t);
        }
    }

    /// <summary>진화 한 순간(<see cref="EvolveBeats.At"/>): 어느 모습을 그리나(false = 진화 전 · true = 진화 뒤) · 흰 실루엣 정도 · 화면을 덮는 흰 빛 · 메시지(0 = 없음 · 1 = 「…오잉!?」 · 2 = 「축하합니다!」) · JP가 빠져나간 만큼.</summary>
    public struct EvolvePose
    {
        public bool To;
        public float White, Screen, JpOut;
        public int Message;
    }

    /// <summary>
    /// 진화(04 §8-8 39행, S27 — 본인 4800 · 남 2000)의 박자(길이의 천분율 — 두 길이가 같은 모양): 예비 = JP가 비용만큼 칩에서 빠져나감 + 「…오잉!? ○○의 모습이…!」 →
    /// 움직임 = 진화 전 모습이 하얗게 물든 뒤(Pt식 흰 실루엣 — 결정 75 C2 가) 두 모습 사이를 점점 빠르게 오감(8번 — 결정 84 L11) → 맞음 = 화면이 하얗게 터지고 진화 뒤 모습이 제 색으로 →
    /// 착지 = 「축하합니다! ○○는 △△로 진화했습니다!」. 진화 창에서 스페이스 바를 누르면 착지(<see cref="SkipAt"/>)로 건너뛴다. 팡파르 · 울음 · 진화 곡은 M9 [가정].
    /// </summary>
    public static class EvolveBeats
    {
        public const int Row = WorkRows.Evolve;
        public const int JpPermille = 150, WhiteFromPermille = 112, WhitePermille = 275, SwapEndPermille = 625, FlashPermille = 700, HoldPermille = 775, ClearPermille = 900;

        /// <summary>오가는 횟수(진화 전 → 뒤 → 전 …, 점점 빨라진다 — 결정 84 L11, 옛 값 15).</summary>
        public const int Swaps = 8;

        static int Per(int total, int permille) => (int)((long)Math.Max(0, total) * permille / 1000);

        /// <summary>흰 빛이 가장 밝아지는(진화 뒤 모습으로 바뀌는) 단계 안 시각.</summary>
        public static int FlashAt(int total) => Per(total, FlashPermille);

        /// <summary>건너뛰기(결정 84 L11 — 진화 창의 스페이스 바)가 가는 단계 안 시각: 흰 빛이 다 걷혀 진화 뒤 모습이 제 색 + 「축하합니다!」인 착지. 남은 꼬리는 그대로 돈다.</summary>
        public static int SkipAt(int total) => Per(total, ClearPermille);

        public static EvolvePose At(int ms, int total)
        {
            var p = new EvolvePose { Message = 1 };
            if (total <= 0 || ms >= total)
            {
                p.To = true;
                p.Message = 2;
                p.JpOut = 1f;
                return p;
            }
            if (ms < 0) ms = 0;
            p.JpOut = Tween.At(ms, 0, Per(total, JpPermille), 0f, 1f, Ease.OutQuad);
            int whiteFrom = Per(total, WhiteFromPermille), whiteTo = Per(total, WhitePermille), swapEnd = Per(total, SwapEndPermille);
            int flash = Per(total, FlashPermille), hold = Per(total, HoldPermille), clear = Per(total, ClearPermille);
            if (ms < whiteTo)
            {
                p.White = Tween.At(ms, whiteFrom, whiteTo - whiteFrom, 0f, 1f);
                return p;
            }
            if (ms < swapEnd)
            {
                // 점점 빨라진다: 지난 횟수 = Swaps × 진행²(빠르기가 진행에 비례해 는다)
                float t = (ms - whiteTo) / (float)Math.Max(1, swapEnd - whiteTo);
                int swaps = (int)(Swaps * t * t);
                p.White = 1f;
                p.To = swaps % 2 == 1;
                return p;
            }
            if (ms < flash)
            {
                p.White = 1f;
                p.To = true;
                p.Screen = Tween.At(ms, swapEnd, flash - swapEnd, 0f, 1f, Ease.InQuad);
                return p;
            }
            p.To = true;
            p.Message = 2;
            p.Screen = ms < hold ? 1f : Tween.At(ms, hold, clear - hold, 1f, 0f, Ease.OutQuad);
            p.White = Tween.At(ms, hold, clear - hold, 1f, 0f);
            return p;
        }
    }
}
