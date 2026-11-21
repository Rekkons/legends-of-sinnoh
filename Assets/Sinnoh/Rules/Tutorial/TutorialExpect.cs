using System;

namespace Sinnoh.Rules.Tutorial
{
    /// <summary>
    /// 각본 `expect` 키 → 엔진 값(07 §7). 좌석 값은 내 좌석(0). 키를 더하면 07 §7 키 목록과 데이터 검사(M65_데이터검사)를 같이 고친다.
    /// <list type="bullet">
    /// <item>좌석: `movesLeft` · `jp` · `apricorn.&lt;색&gt;`(red·blue·yellow·pink·green·white·black) · `balls.&lt;등급&gt;`(regular·greater·ultimate·keystone) ·
    /// `party`(파티 수) · `hp.&lt;파티 칸 0~5&gt;`(남은 체력) · `shards` · `fateHand` · `stamps` · `tier`(트레이너 카드 등급)</item>
    /// <item>판: `act`(신오의 심장 막, 서막 0) · `round` · `blessing`(울트라 버스트 축복) · `breach`(균열 강도 % — UbTurn.Breach) · `prism`(네크로즈마 쪽 프리즘 아머 — UbState.PrismUltraBurst)</item>
    /// <item>그 단계의 굴림(진행기가 이벤트에서 읽는다 — 배틀이 끝나도 남는다): `power`·`diceSum`(내 쪽 마지막 공격 굴림) · `matchup`·`damage`(그 공격의 상성 값 · 최종 데미지 — 결정 104 ②) ·
    /// `captureRate`·`captureSum`(마지막 포획 굴림). 굴림은 지정 행동 단계에서 일어나고 뒤따르는 [다음] 단계까지 남는다(굴린 뒤에 값을 말하는 대사 — 07 T192)</item>
    /// </list>
    /// </summary>
    public static class TutorialExpect
    {
        /// <summary>굴림 키(상태가 아니라 이벤트 값 — 진행기가 채운다).</summary>
        public static bool IsRollKey(string key) => key == "power" || key == "diceSum" || key == "matchup" || key == "damage" || key == "captureRate" || key == "captureSum";

        /// <summary>아는 키인가(값 읽기 없이 문법만 — 데이터 검사용).</summary>
        public static bool IsKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (IsRollKey(key)) return true;
            switch (key)
            {
                case "movesLeft": case "jp": case "party": case "shards": case "fateHand": case "stamps": case "tier":
                case "act": case "round": case "blessing": case "breach": case "prism":
                    return true;
            }
            int dot = key.IndexOf('.');
            if (dot < 0) return false;
            string head = key.Substring(0, dot), tail = key.Substring(dot + 1);
            switch (head)
            {
                case "apricorn": return Enum.TryParse(tail, true, out ApricornColor _) && !char.IsDigit(tail[0]);
                case "balls": return Enum.TryParse(tail, true, out Ball _) && !char.IsDigit(tail[0]);
                case "hp": return int.TryParse(tail, out int slot) && slot >= 0 && slot < 6;
            }
            return false;
        }

        /// <summary>상태 키의 값(굴림 키는 ArgumentException — 진행기가 따로 읽는다).</summary>
        public static int Read(GameState s, Ctx ctx, int seat, string key)
        {
            if (!IsKey(key) || IsRollKey(key)) throw new ArgumentException($"상태에서 읽는 expect 키가 아니다: {key}", nameof(key));
            var me = s.Seats[seat];
            switch (key)
            {
                case "movesLeft": return me.MovesLeft;
                case "jp": return me.Jp;
                case "party": return Count(me.Party);
                case "shards": return Count(me.Shards);
                case "fateHand": return me.FateHand.Count;
                case "stamps": return Count(me.Stamps);
                case "tier": return me.TrainerCardTier;
                case "act": return s.Hos?.Act ?? throw new InvalidOperationException("act: 신오의 심장 판이 아니다");
                case "round": return s.Round;
                case "blessing": return s.Ub?.Blessings ?? throw new InvalidOperationException("blessing: 울트라 버스트 판이 아니다");
                case "breach": return s.Ub != null ? UbTurn.Breach(s) : throw new InvalidOperationException("breach: 울트라 버스트 판이 아니다");
                case "prism": return s.Ub?.PrismUltraBurst ?? throw new InvalidOperationException("prism: 울트라 버스트 판이 아니다");
            }
            int dot = key.IndexOf('.');
            string head = key.Substring(0, dot), tail = key.Substring(dot + 1);
            switch (head)
            {
                case "apricorn": return me.Apricorns[(int)(ApricornColor)Enum.Parse(typeof(ApricornColor), tail, true)];
                case "balls": return me.Balls[(int)(Ball)Enum.Parse(typeof(Ball), tail, true)];
                default:
                {
                    int id = me.Party[int.Parse(tail)];
                    if (id < 0) throw new InvalidOperationException($"{key}: 파티 칸이 비었다");
                    var p = s.Pokemon[id];
                    return PokemonRules.MaxHp(ctx, p) - p.Damage;
                }
            }
        }

        static int Count(int[] party)
        {
            int n = 0;
            foreach (int id in party)
                if (id >= 0) n++;
            return n;
        }

        static int Count(bool[] flags)
        {
            int n = 0;
            foreach (bool f in flags)
                if (f) n++;
            return n;
        }
    }
}
