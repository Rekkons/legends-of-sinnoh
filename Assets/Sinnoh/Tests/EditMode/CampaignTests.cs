using System;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    public class CampaignTests
    {
        [Test]
        public void A3_파티슬롯3더하기조각수_UB6()
        {
            for (int shards = 0; shards <= 3; shards++)
                Assert.AreEqual(3 + shards, CampaignRules.OpenPartySlots(Campaign.HeartOfSinnoh, shards), $"조각 {shards}개");
            Assert.AreEqual(6, CampaignRules.OpenPartySlots(Campaign.UltraBurst, 0), "울트라 버스트는 6칸 모두 열림");
            Assert.Throws<ArgumentOutOfRangeException>(() => CampaignRules.OpenPartySlots(Campaign.HeartOfSinnoh, 4));
        }

        [Test]
        public void A14_축복신인3_베테랑1_마스터0()
        {
            Assert.AreEqual(UbDifficulty.NewTrainer, new RuleOptions().UbDifficulty, "기본값 = 결정 29 ⑤");
            Assert.AreEqual(3, CampaignRules.StartingBlessings(UbDifficulty.NewTrainer));
            Assert.AreEqual(1, CampaignRules.StartingBlessings(UbDifficulty.VeteranTrainer));
            Assert.AreEqual(0, CampaignRules.StartingBlessings(UbDifficulty.MasterTrainer), "한글 룰북 「마스터 1」은 오기");
        }
    }
}
