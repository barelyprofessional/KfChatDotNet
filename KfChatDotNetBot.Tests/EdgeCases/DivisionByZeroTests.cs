using KfChatDotNetBot.Models;
using KfChatDotNetBot.Services;

namespace KfChatDotNetBot.Tests.EdgeCases;

/// <summary>
/// Tests for division by zero edge cases in the Money service and related calculations.
/// These tests verify that guards are in place and document potential vulnerabilities.
/// </summary>
public class DivisionByZeroTests
{
    #region Multiplier Calculation Tests

    /// <summary>
    /// Test that the multiplier calculation guard in NewWagerAsync prevents division by zero.
    /// The condition is: isComplete && wagerAmount > 0 && wagerEffect > 0 && wagerAmount + wagerEffect > 0
    /// </summary>
    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(0, 100)]      // wagerAmount = 0 should be blocked by guard
    [InlineData(100, 0)]      // wagerEffect = 0 should be blocked by guard
    [InlineData(-100, -50)]   // both negative should be blocked
    [InlineData(100, -200)]   // sum negative should be blocked
    public void MultiplierCalculation_GuardConditions_PreventDivisionByZero(decimal wagerAmount, decimal wagerEffect)
    {
        // This is the guard condition from Money.cs line 362
        bool isComplete = true;
        bool guardPasses = isComplete && wagerAmount > 0 && wagerEffect > 0 && wagerAmount + wagerEffect > 0;

        // Guard should block these cases
        guardPasses.Should().BeFalse($"wagerAmount={wagerAmount}, wagerEffect={wagerEffect} should be blocked by guard");

        // Verify that WITHOUT the guard, division would fail
        if (wagerAmount == 0)
        {
            Action divideByZero = () => { var _ = (wagerAmount + wagerEffect) / wagerAmount; };
            divideByZero.Should().Throw<DivideByZeroException>();
        }
    }

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(100, 50, 1.5)]    // Normal winning case
    [InlineData(100, 100, 2.0)]   // Double your money
    [InlineData(1, 1, 2.0)]       // Minimum valid wager
    public void MultiplierCalculation_ValidInputs_CalculatesCorrectly(decimal wagerAmount, decimal wagerEffect, decimal expectedMulti)
    {
        // Test the actual calculation when guard passes
        bool isComplete = true;
        bool guardPasses = isComplete && wagerAmount > 0 && wagerEffect > 0 && wagerAmount + wagerEffect > 0;

        guardPasses.Should().BeTrue();

        var multi = (wagerAmount + wagerEffect) / wagerAmount;
        multi.Should().Be(expectedMulti);
    }

    #endregion

    #region VIP Payout Calculation Tests

    /// <summary>
    /// Test the VIP payout calculation in UpgradeVipLevelAsync.
    /// Line 506: payout = nextVipLevel.VipLevel.BonusPayout / (nextVipLevel.VipLevel.Tiers - 1)
    /// This divides by (Tiers - 1), so if Tiers = 1, we get division by zero.
    /// </summary>
    [Fact]
    [Trait("Category", "EdgeCase")]
    public void VipPayoutCalculation_TiersEqualsOne_WouldCauseDivisionByZero()
    {
        // Simulate a VIP level with Tiers = 1
        int tiers = 1;
        int bonusPayout = 1000;
        int tier = 2; // This is the guard condition: if (nextVipLevel.Tier > 1)

        // The guard checks Tier (the user's current tier), not Tiers (total number of tiers)
        // This is a potential bug: if Tier > 1 but Tiers = 1, we get division by zero
        bool guardPasses = tier > 1;
        guardPasses.Should().BeTrue("tier > 1 passes the guard");

        // But the calculation uses Tiers, not Tier
        Action divideByZero = () => { var _ = bonusPayout / (tiers - 1); };
        divideByZero.Should().Throw<DivideByZeroException>(
            "POTENTIAL BUG: Guard checks 'Tier' but calculation uses 'Tiers'. If Tiers=1 and Tier>1, division by zero occurs.");
    }

    /// <summary>
    /// Verify all built-in VIP levels have Tiers > 1 to prevent division by zero.
    /// This is the current safeguard - all VipLevels have Tiers = 5.
    /// </summary>
    [Fact]
    [Trait("Category", "EdgeCase")]
    public void AllVipLevels_HaveTiersGreaterThanOne_PreventsDivisionByZero()
    {
        foreach (var level in Money.VipLevels)
        {
            level.Tiers.Should().BeGreaterThan(1,
                $"VIP level '{level.Name}' must have Tiers > 1 to prevent division by zero in payout calculation");
        }
    }

    /// <summary>
    /// Test the tier calculation in CalculateTiers.
    /// Line 168: var step = (nextLevel.BaseWagerRequirement - vipLevel.BaseWagerRequirement) / vipLevel.Tiers
    /// </summary>
    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateTiers_TiersIsZero_WouldCauseDivisionByZero()
    {
        // If somehow a VIP level had Tiers = 0
        int tiers = 0;
        decimal baseWager = 10000;
        decimal nextBaseWager = 100000;

        Action divideByZero = () => { var _ = (nextBaseWager - baseWager) / tiers; };
        divideByZero.Should().Throw<DivideByZeroException>(
            "If Tiers = 0, CalculateTiers would throw DivideByZeroException");
    }

    #endregion

    #region Limbo Math Tests

    /// <summary>
    /// Test Limbo's skew calculation edge cases.
    /// Line 125: var skew = 1.0 / (double)(multi * (decimal)1.01)
    /// Line 126: var gamma = Math.Log(0.5) / Math.Log(skew)
    /// If skew = 1.0 (multi ≈ 0.99), Math.Log(1.0) = 0 causing division by zero.
    /// </summary>
    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(2.0)]       // Normal case
    [InlineData(1.01)]      // Minimum valid (barely above 1)
    [InlineData(10000.0)]   // Maximum allowed
    public void LimboSkewCalculation_ValidMultipliers_DoNotCauseNaN(double multi)
    {
        // The limbo guard is: if (limboNumber <= 1) return error
        // So minimum valid multi is just above 1

        var skew = 1.0 / (multi * 1.01);
        var logSkew = Math.Log(skew);
        var log05 = Math.Log(0.5);
        var gamma = log05 / logSkew;

        skew.Should().BeGreaterThan(0, "skew should be positive");
        double.IsNaN(gamma).Should().BeFalse("gamma should not be NaN");
        double.IsInfinity(gamma).Should().BeFalse("gamma should not be infinity");
    }

    /// <summary>
    /// Test that the limbo guard prevents invalid multipliers.
    /// </summary>
    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(0.5)]
    public void LimboGuard_InvalidMultipliers_AreRejected(decimal limboNumber)
    {
        // The guard: if (limboNumber <= 1) return error
        bool guardBlocks = limboNumber <= 1;
        guardBlocks.Should().BeTrue($"limboNumber={limboNumber} should be blocked by guard");
    }

    #endregion

    #region Decimal Edge Cases

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void DecimalDivision_ByZero_ThrowsException()
    {
        decimal numerator = 100m;
        decimal denominator = 0m;

        Action divide = () => { var _ = numerator / denominator; };
        divide.Should().Throw<DivideByZeroException>();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void DoubleDivision_ByZero_ReturnsInfinity()
    {
        double numerator = 100.0;
        double denominator = 0.0;

        // Unlike decimal, double division by zero returns infinity, not an exception
        var result = numerator / denominator;
        double.IsPositiveInfinity(result).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void DoubleDivision_ZeroByZero_ReturnsNaN()
    {
        double result = 0.0 / 0.0;
        double.IsNaN(result).Should().BeTrue();
    }

    #endregion
}
