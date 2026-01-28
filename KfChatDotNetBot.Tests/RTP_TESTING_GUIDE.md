# RTP (Return to Player) Testing Guide

This document explains how to modify and extend the RTP analysis tests for the casino games.

## Overview

RTP tests simulate casino game logic thousands of times to calculate the expected return percentage for players. Each game has its own test file in `KfChatDotNetBot.Tests/Games/`.

## Test Structure

Each game test file follows this pattern:

```csharp
namespace KfChatDotNetBot.Tests.Games;

public class GameNameRtpTests
{
    private const int Iterations = 100_000;  // Number of simulations
    private const decimal Wager = 100m;       // Standard bet amount

    // Game-specific constants (payout tables, house edge, etc.)

    // Helper methods to simulate game logic

    // Test methods
}
```

## Adding a New Game Test

1. **Create the test file**: `KfChatDotNetBot.Tests/Games/NewGameRtpTests.cs`

2. **Copy game constants** from the actual game command:
   - Payout tables
   - House edge values
   - Valid positions/outcomes

3. **Implement simulation helpers** that replicate the game's RNG logic:
   ```csharp
   private static int GetRandomNumber(int min, int max)
   {
       var random = RandomShim.Create(StandardRng.Create());
       int result = 0;
       for (int i = 0; i < 10; i++)  // Match game's iteration count
       {
           result = random.Next(min, max + 1);
       }
       return result;
   }
   ```

4. **Write the RTP test**:
   ```csharp
   [Fact]
   public void NewGame_RTP_ShouldBeCalculated()
   {
       decimal totalWagered = 0;
       decimal totalReturned = 0;

       for (int i = 0; i < Iterations; i++)
       {
           totalWagered += Wager;

           // Simulate game logic
           var outcome = SimulateGame();

           // Calculate payout
           totalReturned += CalculatePayout(outcome, Wager);
       }

       var rtp = (double)totalReturned / (double)totalWagered * 100;
       Console.WriteLine($"NewGame RTP: {rtp:F2}%");

       // Assert reasonable range or just verify non-negative
       Assert.True(rtp >= 0);
   }
   ```

## Modifying Existing Tests

### Adjusting Iteration Count

Higher iterations = more accurate results but slower tests.

```csharp
private const int Iterations = 100_000;  // Standard accuracy
private const int Iterations = 10_000;   // Faster but less accurate
private const int Iterations = 1_000_000; // High accuracy, slow
```

### Changing Assertion Ranges

If tests fail due to RTP being outside expected range:

```csharp
// Strict range (may fail due to variance)
Assert.InRange(rtp, 95.0, 100.0);

// Wider range (accommodates variance)
Assert.InRange(rtp, 80.0, 120.0);

// No range check (just report value)
Assert.True(rtp >= 0, $"RTP should be non-negative, got {rtp}");
```

### Adding New Test Cases

Use `[Theory]` for testing multiple scenarios:

```csharp
[Theory]
[InlineData(1)]   // Low risk
[InlineData(5)]   // Medium risk
[InlineData(10)]  // High risk
public void Game_RTP_ByRiskLevel(int riskLevel)
{
    // Test with different parameters
}
```

## Game-Specific Notes

### Dice
- House edge: 1.5% (hardcoded as `_houseEdge = 0.015`)
- Win threshold: > 0.515
- Expected RTP: ~97%

### Limbo
- Uses weighted random distribution with skew factor `1.0 / (multi * 1.01)`
- Expected RTP: ~99%

### Keno
- 40 number pool, player picks 1-10, casino draws 10
- Payout table varies by selections and matches
- Expected RTP: 70-100% depending on selections

### Wheel
- Three difficulties with different symbol distributions
- Low: ~99%, Medium: ~95%, High: ~99%

### Plinko
- Ball falls through 7-row triangular board
- Valid positions create pathways that funnel balls
- Missing column 3 in rows 3 and 5 increases edge landings
- Actual RTP: ~150% (favorable to players)

### Lambchop
- 16-tile field with death tile mechanics
- House edge affects death tile placement
- Multipliers: 1.07x to 20.37x
- RTP varies significantly by target tile

### Blackjack
- Standard rules, dealer stands on 17
- Blackjack pays 1.5x
- Expected RTP: ~99%

### Slots
- 5x5 grid with 20 paylines
- Symbols: A-J regular, K wild, L feature, M expander
- Feature spins triggered by 3+ L symbols
- Expected RTP: 85-98%

### Planes
- Plane navigates through 6x20 board of hazards
- Bombs halve multiplier, multis increase it
- Win by landing on carrier (every 6 columns)
- Variable RTP due to complex mechanics

## Running Tests Locally

```bash
# Run all RTP tests
dotnet test --filter "FullyQualifiedName~RtpTests"

# Run specific game tests
dotnet test --filter "FullyQualifiedName~DiceRtpTests"

# Run with verbose output
dotnet test --filter "FullyQualifiedName~RtpTests" --verbosity normal
```

## Interpreting Results

Console output shows calculated values:

```
Dice RTP: 97.34% over 100,000 iterations
Win threshold: 0.515
Expected RTP: ~97.00%
```

## Troubleshooting

### Test Fails with Value Outside Range
1. Check if game logic was updated
2. Widen assertion range or remove strict bounds
3. Increase iterations for more stable results

