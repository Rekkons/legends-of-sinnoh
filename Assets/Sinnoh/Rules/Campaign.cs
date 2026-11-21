using System;

namespace Sinnoh.Rules
{
    /// <summary>캠페인(00 §5 용어 2·3).</summary>
    public enum Campaign
    {
        HeartOfSinnoh,
        UltraBurst,
    }

    /// <summary>캠페인 셋업 값: 파티 칸(A3), 아르세우스의 축복(A14).</summary>
    public static class CampaignRules
    {
        /// <summary>파티 칸 수(6칸 패널).</summary>
        public const int MaxPartySlots = 6;

        /// <summary>
        /// 열린 파티 칸 수(A3). 신오의 심장 = 3 + 가진 조각 수(조각 1·2·3개에 4·5·6번째 칸, A2), 울트라 버스트 = 6(패널 뒷면).
        /// </summary>
        public static int OpenPartySlots(Campaign campaign, int shards)
        {
            if (shards < 0 || shards > 3) throw new ArgumentOutOfRangeException(nameof(shards), shards, "조각은 0~3개");
            return campaign == Campaign.UltraBurst ? MaxPartySlots : 3 + shards;
        }

        /// <summary>시작 아르세우스의 축복 수(A14, 룰북 p.23): New 3 / Veteran 1 / Master 0. 한글 룰북의 「마스터 1」은 오기.</summary>
        public static int StartingBlessings(UbDifficulty difficulty)
        {
            switch (difficulty)
            {
                case UbDifficulty.NewTrainer: return 3;
                case UbDifficulty.VeteranTrainer: return 1;
                case UbDifficulty.MasterTrainer: return 0;
                default: throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, null);
            }
        }
    }
}
