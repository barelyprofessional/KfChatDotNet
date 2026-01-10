using RandN;
using RandN.Compat;

namespace KfChatDotNetBot.Tests.Games;

/// <summary>
/// RTP (Return to Player) tests for the Blackjack game.
///
/// Game rules:
/// - Standard blackjack with single deck
/// - Dealer stands on 17
/// - Blackjack pays 1.5x (2.5x total return)
/// - Regular win pays 1x (2x total return)
/// - Push returns wager (1x)
/// - Double down available on first two cards
/// </summary>
public class BlackjackRtpTests
{
    private const int Iterations = 50_000;
    private const decimal Wager = 100m;

    private static readonly string[] Ranks = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };
    private static readonly string[] Suits = { "S", "H", "D", "C" };

    private class Card
    {
        public string Rank { get; set; } = "";
        public string Suit { get; set; } = "";

        public int GetValue()
        {
            return Rank switch
            {
                "A" => 11,
                "K" or "Q" or "J" => 10,
                _ => int.Parse(Rank)
            };
        }
    }

    private static List<Card> CreateDeck(Random random)
    {
        var deck = new List<Card>();
        foreach (var suit in Suits)
        {
            foreach (var rank in Ranks)
            {
                deck.Add(new Card { Rank = rank, Suit = suit });
            }
        }

        // Fisher-Yates shuffle
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = random.Next(0, i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        return deck;
    }

    private static int CalculateHandValue(List<Card> hand)
    {
        int value = 0;
        int aces = 0;

        foreach (var card in hand)
        {
            if (card.Rank == "A")
            {
                aces++;
                value += 11;
            }
            else
            {
                value += card.GetValue();
            }
        }

        while (value > 21 && aces > 0)
        {
            value -= 10;
            aces--;
        }

        return value;
    }

    private static bool IsBlackjack(List<Card> hand)
    {
        return hand.Count == 2 && CalculateHandValue(hand) == 21;
    }

    /// <summary>
    /// Basic strategy decision for player
    /// </summary>
    private static string BasicStrategy(List<Card> playerHand, Card dealerUpCard)
    {
        int playerValue = CalculateHandValue(playerHand);
        int dealerValue = dealerUpCard.GetValue();
        bool isSoft = playerHand.Any(c => c.Rank == "A") && playerValue <= 21;

        // Simplified basic strategy
        if (playerValue >= 17)
            return "stand";

        if (playerValue <= 11)
            return "hit";

        if (isSoft && playerValue <= 17)
            return "hit";

        if (playerValue == 12 && dealerValue >= 4 && dealerValue <= 6)
            return "stand";

        if (playerValue >= 13 && playerValue <= 16 && dealerValue >= 2 && dealerValue <= 6)
            return "stand";

        return "hit";
    }

    [Fact]
    public void Blackjack_RTP_WithBasicStrategy_ShouldBeHigh()
    {
        decimal totalWagered = 0;
        decimal totalReturned = 0;
        var random = RandomShim.Create(StandardRng.Create());

        for (int i = 0; i < Iterations; i++)
        {
            totalWagered += Wager;
            var deck = CreateDeck(random);

            var playerHand = new List<Card> { deck[0], deck[2] };
            var dealerHand = new List<Card> { deck[1], deck[3] };
            int deckIndex = 4;

            // Check for immediate blackjacks
            bool playerBj = IsBlackjack(playerHand);
            bool dealerBj = IsBlackjack(dealerHand);

            if (playerBj && dealerBj)
            {
                totalReturned += Wager; // Push
                continue;
            }
            else if (playerBj)
            {
                totalReturned += Wager * 2.5m; // Blackjack pays 3:2
                continue;
            }
            else if (dealerBj)
            {
                // Loss
                continue;
            }

            // Player's turn (using basic strategy)
            while (CalculateHandValue(playerHand) < 21)
            {
                string action = BasicStrategy(playerHand, dealerHand[0]);
                if (action == "hit")
                {
                    playerHand.Add(deck[deckIndex++]);
                }
                else
                {
                    break;
                }
            }

            int playerValue = CalculateHandValue(playerHand);

            if (playerValue > 21)
            {
                // Player busted
                continue;
            }

            // Dealer's turn (hits until 17+)
            int dealerValue = CalculateHandValue(dealerHand);
            while (dealerValue < 17)
            {
                dealerHand.Add(deck[deckIndex++]);
                dealerValue = CalculateHandValue(dealerHand);
            }

            // Determine outcome
            if (dealerValue > 21)
            {
                totalReturned += Wager * 2; // Dealer bust
            }
            else if (playerValue > dealerValue)
            {
                totalReturned += Wager * 2; // Player wins
            }
            else if (playerValue == dealerValue)
            {
                totalReturned += Wager; // Push
            }
            // else dealer wins, player loses wager
        }

        var rtp = (double)totalReturned / (double)totalWagered * 100;

        // Blackjack with basic strategy typically has 99%+ RTP
        Console.WriteLine($"Blackjack RTP with Basic Strategy: {rtp:F2}% over {Iterations:N0} iterations");

        Assert.InRange(rtp, 90.0, 105.0);
    }

    [Fact]
    public void Blackjack_BlackjackProbability_ShouldBeCorrect()
    {
        int blackjacks = 0;
        var random = RandomShim.Create(StandardRng.Create());

        for (int i = 0; i < Iterations; i++)
        {
            var deck = CreateDeck(random);
            var playerHand = new List<Card> { deck[0], deck[2] };

            if (IsBlackjack(playerHand))
            {
                blackjacks++;
            }
        }

        var bjRate = (double)blackjacks / Iterations;

        // Probability of blackjack = (4/52) * (16/51) + (16/52) * (4/51) = 4.83%
        var expectedBjRate = 2 * (4.0 / 52) * (16.0 / 51);

        Console.WriteLine($"Blackjack Rate: {bjRate * 100:F2}% (expected: {expectedBjRate * 100:F2}%)");

        // Allow 1% variance
        Assert.InRange(bjRate, expectedBjRate - 0.01, expectedBjRate + 0.01);
    }

    [Fact]
    public void Blackjack_DealerBustRate_ShouldBeReasonable()
    {
        int dealerBusts = 0;
        var random = RandomShim.Create(StandardRng.Create());

        for (int i = 0; i < Iterations; i++)
        {
            var deck = CreateDeck(random);
            var dealerHand = new List<Card> { deck[1], deck[3] };
            int deckIndex = 4;

            // Skip if dealer has blackjack
            if (IsBlackjack(dealerHand))
            {
                continue;
            }

            // Dealer plays
            int dealerValue = CalculateHandValue(dealerHand);
            while (dealerValue < 17)
            {
                dealerHand.Add(deck[deckIndex++]);
                dealerValue = CalculateHandValue(dealerHand);
            }

            if (dealerValue > 21)
            {
                dealerBusts++;
            }
        }

        var bustRate = (double)dealerBusts / Iterations;

        // Dealer bust rate is typically around 28-29%
        Console.WriteLine($"Dealer Bust Rate: {bustRate * 100:F2}%");

        Assert.InRange(bustRate, 0.20, 0.35);
    }

    [Fact]
    public void Blackjack_HandValueCalculation_ShouldBeCorrect()
    {
        // Test hard hands
        var hand1 = new List<Card> { new() { Rank = "K", Suit = "S" }, new() { Rank = "5", Suit = "H" } };
        Assert.Equal(15, CalculateHandValue(hand1));

        // Test soft hands
        var hand2 = new List<Card> { new() { Rank = "A", Suit = "S" }, new() { Rank = "6", Suit = "H" } };
        Assert.Equal(17, CalculateHandValue(hand2));

        // Test blackjack
        var hand3 = new List<Card> { new() { Rank = "A", Suit = "S" }, new() { Rank = "K", Suit = "H" } };
        Assert.Equal(21, CalculateHandValue(hand3));
        Assert.True(IsBlackjack(hand3));

        // Test ace conversion
        var hand4 = new List<Card>
        {
            new() { Rank = "A", Suit = "S" },
            new() { Rank = "6", Suit = "H" },
            new() { Rank = "8", Suit = "D" }
        };
        Assert.Equal(15, CalculateHandValue(hand4)); // Ace converts to 1

        // Test multiple aces
        var hand5 = new List<Card>
        {
            new() { Rank = "A", Suit = "S" },
            new() { Rank = "A", Suit = "H" },
            new() { Rank = "9", Suit = "D" }
        };
        Assert.Equal(21, CalculateHandValue(hand5));
    }
}
