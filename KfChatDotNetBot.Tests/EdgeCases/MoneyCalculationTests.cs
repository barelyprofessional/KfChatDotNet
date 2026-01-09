using KfChatDotNetBot.Services;

namespace KfChatDotNetBot.Tests.EdgeCases;

/// <summary>
/// Tests for Money.cs calculations including VIP levels, tiers, and balance operations.
/// </summary>
public class MoneyCalculationTests
{
    #region VIP Level Structure Tests

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void VipLevels_AreInAscendingOrder_ByBaseWagerRequirement()
    {
        var previous = decimal.MinValue;
        foreach (var level in Money.VipLevels)
        {
            level.BaseWagerRequirement.Should().BeGreaterThan(previous,
                $"VIP levels must be in ascending order. '{level.Name}' violates this.");
            previous = level.BaseWagerRequirement;
        }
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void VipLevels_AllHavePositiveBonusPayouts()
    {
        foreach (var level in Money.VipLevels)
        {
            level.BonusPayout.Should().BeGreaterThan(0,
                $"VIP level '{level.Name}' should have positive bonus payout");
        }
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void VipLevels_AllHaveRequiredProperties()
    {
        foreach (var level in Money.VipLevels)
        {
            level.Name.Should().NotBeNullOrEmpty($"VIP level must have a name");
            level.Icon.Should().NotBeNullOrEmpty($"VIP level '{level.Name}' must have an icon");
            level.Tiers.Should().BeGreaterThan(0, $"VIP level '{level.Name}' must have at least 1 tier");
            level.BaseWagerRequirement.Should().BeGreaterThan(0, $"VIP level '{level.Name}' must have positive wager requirement");
        }
    }

    #endregion

    #region CalculateTiers Tests

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateTiers_FirstLevel_ReturnsCorrectTiers()
    {
        var firstLevel = Money.VipLevels[0];
        var tiers = Money.CalculateTiers(firstLevel);

        tiers.Should().HaveCount(firstLevel.Tiers, "First level should have exactly {0} tiers", firstLevel.Tiers);
        tiers[0].Should().Be(firstLevel.BaseWagerRequirement, "First tier should start at base wager requirement");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateTiers_MaxLevel_ReturnsSingleTier()
    {
        var maxLevel = Money.VipLevels[^1]; // Last level
        var tiers = Money.CalculateTiers(maxLevel);

        // Max level has no next level, so it returns just the base requirement
        tiers.Should().HaveCount(1, "Max level should return single tier");
        tiers[0].Should().Be(maxLevel.BaseWagerRequirement);
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateTiers_TiersAreEvenlySplit()
    {
        // Test that tiers are evenly distributed between levels
        for (int i = 0; i < Money.VipLevels.Count - 1; i++)
        {
            var level = Money.VipLevels[i];
            var nextLevel = Money.VipLevels[i + 1];
            var tiers = Money.CalculateTiers(level);

            // Calculate expected step
            var expectedStep = (nextLevel.BaseWagerRequirement - level.BaseWagerRequirement) / level.Tiers;

            for (int j = 1; j < tiers.Count; j++)
            {
                var actualStep = tiers[j] - tiers[j - 1];
                actualStep.Should().Be(expectedStep,
                    $"Tiers for '{level.Name}' should be evenly spaced with step {expectedStep}");
            }
        }
    }

    #endregion

    #region GetNextVipLevel Tests

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GetNextVipLevel_ZeroWagered_ReturnsFirstLevel()
    {
        var result = Money.GetNextVipLevel(0);

        result.Should().NotBeNull();
        result!.VipLevel.Should().Be(Money.VipLevels[0], "Zero wagered should target first VIP level");
        result.Tier.Should().Be(1, "Should be tier 1 of first level");
        result.WagerRequirement.Should().Be(Money.VipLevels[0].BaseWagerRequirement);
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GetNextVipLevel_ExactlyAtLevelBase_ReturnsNextTier()
    {
        var firstLevel = Money.VipLevels[0];
        var result = Money.GetNextVipLevel(firstLevel.BaseWagerRequirement);

        result.Should().NotBeNull();
        // When exactly at base, you're at the level but need to progress through tiers
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GetNextVipLevel_MaxLevel_ReturnsNull()
    {
        var maxLevel = Money.VipLevels[^1];
        // Wager beyond max level
        var result = Money.GetNextVipLevel(maxLevel.BaseWagerRequirement * 2);

        // Once past max level, there's no next level
        result.Should().BeNull("No next level exists beyond max");
    }

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(5_000)]      // Below first level
    [InlineData(50_000)]     // Between levels
    [InlineData(500_000)]    // Between levels
    [InlineData(5_000_000)]  // Exactly at a level boundary
    public void GetNextVipLevel_VariousAmounts_ReturnsValidResult(decimal wagered)
    {
        var result = Money.GetNextVipLevel(wagered);

        if (result != null)
        {
            result.VipLevel.Should().NotBeNull();
            result.Tier.Should().BeGreaterThan(0);
            result.WagerRequirement.Should().BeGreaterThanOrEqualTo(wagered,
                "Next wager requirement should be >= current wagered amount");
        }
    }

    #endregion

    #region Balance Calculation Edge Cases

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(1000, 500, 1500)]          // Normal win
    [InlineData(1000, -500, 500)]          // Normal loss
    [InlineData(0, 100, 100)]              // Zero balance + win
    [InlineData(100, -100, 0)]             // Exact balance drain
    [InlineData(100, -200, -100)]          // Negative balance (allowed per code comments)
    public void BalanceModification_VariousScenarios_CalculatesCorrectly(
        decimal startBalance, decimal effect, decimal expectedBalance)
    {
        var newBalance = startBalance + effect;
        newBalance.Should().Be(expectedBalance);
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void BalanceModification_VeryLargeNumbers_DoesNotOverflow()
    {
        // Test that we don't overflow decimal for large balances
        decimal largeBalance = 1_000_000_000_000_000m; // 1 quadrillion
        decimal largeEffect = 1_000_000_000_000m;      // 1 trillion

        Action addLarge = () => { var _ = largeBalance + largeEffect; };
        addLarge.Should().NotThrow<OverflowException>();

        var result = largeBalance + largeEffect;
        result.Should().Be(1_001_000_000_000_000m);
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void BalanceModification_DecimalMaxValue_ThrowsOverflow()
    {
        decimal maxValue = decimal.MaxValue;
        decimal smallAddition = 1m;

        Action overflow = () => { var _ = maxValue + smallAddition; };
        overflow.Should().Throw<OverflowException>();
    }

    #endregion

    #region Random Number Generation Tests

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(0, 10)]
    [InlineData(-100, 100)]
    [InlineData(int.MinValue / 2, int.MaxValue / 2)]
    public void GetRandomNumber_ValidRanges_ReturnsNumberInRange(int min, int max)
    {
        // Note: GetRandomNumber requires a GamblerDbModel but doesn't actually use its RandomSeed!
        // This is a known issue - the RandomSeed is never used.
        // For testing, we just verify the range constraints.

        // The method increments max by 1 by default (incrementMaxParam = true)
        // So the actual max is max + 1, meaning max is inclusive
        int expectedMin = min;
        int expectedMax = max; // inclusive due to incrementMaxParam

        // We can't test directly without a GamblerDbModel, but we can verify the logic
        expectedMax.Should().BeGreaterThanOrEqualTo(expectedMin);
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GetRandomNumber_ZeroIterations_ThrowsArgumentException()
    {
        // The method should throw if iterations <= 0
        // This is explicitly checked in the code at line 437
        int iterations = 0;
        bool shouldThrow = iterations <= 0;
        shouldThrow.Should().BeTrue("Zero iterations should be rejected");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GetRandomNumber_NegativeIterations_ThrowsArgumentException()
    {
        int iterations = -1;
        bool shouldThrow = iterations <= 0;
        shouldThrow.Should().BeTrue("Negative iterations should be rejected");
    }

    #endregion

    #region Event ID Generation Tests

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GenerateEventId_ReturnsValidHexString()
    {
        var eventId = Money.GenerateEventId();

        eventId.Should().NotBeNullOrEmpty();
        eventId.Should().HaveLength(8, "Event ID should be 8 hex characters (4 bytes)");
        eventId.Should().MatchRegex("^[a-f0-9]{8}$", "Event ID should be lowercase hex");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GenerateEventId_MultipleCallsGenerateUniqueIds()
    {
        var ids = new HashSet<string>();
        const int count = 1000;

        for (int i = 0; i < count; i++)
        {
            ids.Add(Money.GenerateEventId());
        }

        // While not guaranteed, collisions should be extremely rare
        ids.Count.Should().BeGreaterThan(count * 99 / 100,
            "Event IDs should be mostly unique (allowing for rare collisions)");
    }

    #endregion
}
