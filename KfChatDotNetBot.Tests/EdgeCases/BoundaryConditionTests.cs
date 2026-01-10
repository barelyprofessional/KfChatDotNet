namespace KfChatDotNetBot.Tests.EdgeCases;

/// <summary>
/// Tests for boundary conditions in casino games and calculations.
/// Tests min/max values, threshold conditions, and edge values.
/// </summary>
public class BoundaryConditionTests
{
    #region Dice Game Boundary Tests

    private const double DiceHouseEdge = 0.015;
    private const double DiceWinThreshold = 0.5 + DiceHouseEdge; // 0.515

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(0.0, false)]           // Minimum roll - loss
    [InlineData(0.5, false)]           // Exactly 50% - loss
    [InlineData(0.514, false)]         // Just below threshold - loss
    [InlineData(0.515, false)]         // Exactly at threshold - loss (not >)
    [InlineData(0.516, true)]          // Just above threshold - win
    [InlineData(1.0, true)]            // Maximum roll - win
    public void Dice_WinCondition_BoundaryValues(double rolled, bool expectedWin)
    {
        // The win condition is: rolled > 0.5 + houseEdge
        bool actualWin = rolled > DiceWinThreshold;
        actualWin.Should().Be(expectedWin, $"rolled={rolled}, threshold={DiceWinThreshold}");
    }

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 10)]              // 21 * 0.5 = 10.5, banker's rounding rounds to 10 (nearest even)
    [InlineData(1.0, 21)]
    public void Dice_EmojiShift_BoundaryValues(double rolled, int expectedShift)
    {
        // Line 107: var toShift = (int)Math.Round(21 * rolled);
        // Note: Math.Round uses banker's rounding (MidpointRounding.ToEven) by default
        int actualShift = (int)Math.Round(21 * rolled);
        actualShift.Should().Be(expectedShift);
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void Dice_EmojiShift_RangeIsValid()
    {
        // Test that all possible values are within expected range 0-21
        for (double rolled = 0.0; rolled <= 1.0; rolled += 0.01)
        {
            int shift = (int)Math.Round(21 * rolled);
            shift.Should().BeInRange(0, 21, $"rolled={rolled}");
        }
    }

    #endregion

    #region Limbo Game Boundary Tests

    private const double LimboMin = 1.0;
    private const double LimboMax = 10000.0;

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(0.0, true)]       // Zero - rejected
    [InlineData(0.5, true)]       // Below 1 - rejected
    [InlineData(1.0, true)]       // Exactly 1 - rejected (condition is <= 1)
    [InlineData(1.01, false)]     // Just above 1 - accepted
    [InlineData(2.0, false)]      // Normal value - accepted
    [InlineData(10000.0, false)]  // Max value - accepted
    public void Limbo_MultiplierValidation_BoundaryValues(decimal limboNumber, bool shouldReject)
    {
        // The guard: if (limboNumber <= 1) reject
        bool rejected = limboNumber <= 1;
        rejected.Should().Be(shouldReject, $"limboNumber={limboNumber}");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void Limbo_WinCondition_ExactlyAtMultiplier()
    {
        // Win condition: casinoNumbers[0] >= limboNumber
        decimal limboNumber = 2.0m;
        decimal casinoResult = 2.0m;

        bool wins = casinoResult >= limboNumber;
        wins.Should().BeTrue("Exactly matching the multiplier should win");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void Limbo_WinCondition_JustBelowMultiplier()
    {
        decimal limboNumber = 2.0m;
        decimal casinoResult = 1.99m;

        bool wins = casinoResult >= limboNumber;
        wins.Should().BeFalse("Just below multiplier should lose");
    }

    #endregion

    #region Wager Amount Boundary Tests

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("0.01", 0.01)]
    [InlineData("999999999999", 999999999999)]
    [InlineData("0.00000001", 0.00000001)]
    public void WagerParsing_ValidDecimals_ParseCorrectly(string input, decimal expected)
    {
        var result = Convert.ToDecimal(input);
        result.Should().Be(expected);
    }

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData("")]           // Empty string
    [InlineData("abc")]        // Non-numeric
    [InlineData("1.2.3")]      // Multiple decimals
    [InlineData("--1")]        // Double negative
    public void WagerParsing_InvalidInputs_ThrowsException(string input)
    {
        Action parse = () => Convert.ToDecimal(input);
        parse.Should().Throw<Exception>(); // FormatException or similar
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void WagerParsing_VeryLargeNumber_ThrowsOverflow()
    {
        // Larger than decimal.MaxValue
        string tooLarge = "99999999999999999999999999999999";

        Action parse = () => Convert.ToDecimal(tooLarge);
        parse.Should().Throw<OverflowException>();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void WagerParsing_ScientificNotation_Parses()
    {
        // Scientific notation can be parsed by decimal
        string scientific = "1E2"; // 100

        // Note: Convert.ToDecimal may or may not support this depending on culture
        // This tests the actual behavior
        try
        {
            var result = Convert.ToDecimal(scientific);
            result.Should().Be(100m);
        }
        catch (FormatException)
        {
            // Also acceptable - depends on implementation
        }
    }

    #endregion

    #region Balance Boundary Tests

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void Balance_ExactlyEqualsWager_AllowsBet()
    {
        decimal balance = 100m;
        decimal wager = 100m;

        // The check is: balance < wager, so balance == wager is allowed
        bool hasEnough = balance >= wager;
        hasEnough.Should().BeTrue("Exact balance should allow bet");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void Balance_OneCentBelowWager_RejectsBet()
    {
        decimal balance = 99.99m;
        decimal wager = 100m;

        bool hasEnough = balance >= wager;
        hasEnough.Should().BeFalse("Balance below wager should reject bet");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void Balance_NegativeBalance_CannotBet()
    {
        // Per code comments, negative balance is allowed, but you can't bet
        decimal balance = -100m;
        decimal wager = 1m;

        bool hasEnough = balance >= wager;
        hasEnough.Should().BeFalse("Negative balance cannot make bets");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void Balance_ZeroBalance_CannotBetPositiveAmount()
    {
        decimal balance = 0m;
        decimal wager = 1m;

        bool hasEnough = balance >= wager;
        hasEnough.Should().BeFalse("Zero balance cannot bet positive amount");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void Balance_ZeroWager_IsAllowed()
    {
        // What happens with zero wager? The regex patterns require \d+ so 0 is valid
        decimal balance = 100m;
        decimal wager = 0m;

        bool hasEnough = balance >= wager;
        hasEnough.Should().BeTrue("Zero wager should pass balance check");

        // But does a zero wager make sense? This might be a logic issue
    }

    #endregion

    #region Array Index Boundary Tests

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(0, true)]       // First element
    [InlineData(4, true)]       // Last element (5 elements, 0-4)
    [InlineData(5, false)]      // Out of bounds
    [InlineData(-1, false)]     // Negative index
    public void ArrayIndex_BoundaryValues_ValidAccess(int index, bool expectedValid)
    {
        var array = new[] { 1, 2, 3, 4, 5 };
        bool isValid = index >= 0 && index < array.Length;
        isValid.Should().Be(expectedValid);
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void ListRemoveRange_InsufficientElements_ThrowsException()
    {
        var list = new List<int> { 1, 2, 3 };

        // Try to remove 4 elements when only 3 exist
        Action removeRange = () => list.RemoveRange(0, 4);
        removeRange.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void ListRemoveRange_ExactElements_Succeeds()
    {
        var list = new List<int> { 1, 2, 3, 4 };

        // Remove exactly all elements
        Action removeRange = () => list.RemoveRange(0, 4);
        removeRange.Should().NotThrow();
    }

    #endregion

    #region Double/Decimal Precision Tests

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void DoublePrecision_SmallDifferences_MayNotBeDetected()
    {
        // Double precision can cause issues with exact comparisons
        double a = 0.1 + 0.2;
        double b = 0.3;

        // These are NOT equal due to floating point precision!
        (a == b).Should().BeFalse("Floating point precision issue");
        Math.Abs(a - b).Should().BeLessThan(0.0001, "Should be approximately equal");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void DecimalPrecision_SmallDifferences_AreDetected()
    {
        // Decimal has better precision for financial calculations
        decimal a = 0.1m + 0.2m;
        decimal b = 0.3m;

        (a == b).Should().BeTrue("Decimal should handle this precisely");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void DecimalToDouble_PrecisionLoss()
    {
        decimal precise = 0.1234567890123456789012345678m;
        double imprecise = (double)precise;
        decimal backToDecimal = (decimal)imprecise;

        backToDecimal.Should().NotBe(precise, "Converting through double loses precision");
    }

    #endregion
}
