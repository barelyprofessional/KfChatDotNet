using KfChatDotNetBot.Models;

namespace KfChatDotNetBot.Tests.EdgeCases;

/// <summary>
/// Tests for Blackjack game edge cases, state management, and boundary conditions.
/// These tests verify proper handling of unusual game states and input validation.
/// </summary>
public class BlackjackStateTests
{
    #region Card Value Edge Cases

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    [InlineData("4", 4)]
    [InlineData("5", 5)]
    [InlineData("6", 6)]
    [InlineData("7", 7)]
    [InlineData("8", 8)]
    [InlineData("9", 9)]
    [InlineData("10", 10)]
    [InlineData("J", 10)]
    [InlineData("Q", 10)]
    [InlineData("K", 10)]
    [InlineData("A", 11)]
    public void Card_GetValue_AllRanks_ReturnCorrectValue(string rank, int expectedValue)
    {
        var card = new Card { Rank = rank, Suit = "♠" };
        card.GetValue().Should().Be(expectedValue);
    }

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("abc")]
    public void Card_GetValue_NonNumericRank_ThrowsFormatException(string rank)
    {
        var card = new Card { Rank = rank, Suit = "♠" };

        // The switch falls through to int.Parse which will throw for non-numeric ranks
        Action getValue = () => card.GetValue();
        getValue.Should().Throw<FormatException>();
    }

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("11", 11)]
    [InlineData("-5", -5)]
    [InlineData("100", 100)]
    public void Card_GetValue_InvalidNumericRank_ParsesWithoutValidation(string rank, int expectedValue)
    {
        // ISSUE: Card.GetValue doesn't validate that the rank is in valid range 2-10
        // Invalid numeric ranks parse successfully but produce incorrect game behavior
        var card = new Card { Rank = rank, Suit = "♠" };

        // These parse fine because they're valid integers
        card.GetValue().Should().Be(expectedValue,
            "Card class doesn't validate rank - any parseable integer is accepted");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void Card_GetValue_NullRank_ThrowsException()
    {
        var card = new Card { Rank = null!, Suit = "♠" };

        Action getValue = () => card.GetValue();
        getValue.Should().Throw<Exception>(); // ArgumentNullException or NullReferenceException
    }

    #endregion

    #region Hand Value Calculation Edge Cases

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateHandValue_EmptyHand_ReturnsZero()
    {
        var hand = new List<Card>();
        var value = BlackjackHelper.CalculateHandValue(hand);
        value.Should().Be(0, "Empty hand should have value 0");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateHandValue_SingleCard_ReturnsCardValue()
    {
        var hand = new List<Card> { new() { Rank = "K", Suit = "♠" } };
        var value = BlackjackHelper.CalculateHandValue(hand);
        value.Should().Be(10);
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateHandValue_SingleAce_Returns11()
    {
        var hand = new List<Card> { new() { Rank = "A", Suit = "♠" } };
        var value = BlackjackHelper.CalculateHandValue(hand);
        value.Should().Be(11);
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateHandValue_TwoAces_Returns12()
    {
        // Two aces: 11 + 11 = 22, one converts to 1, so 12
        var hand = new List<Card>
        {
            new() { Rank = "A", Suit = "♠" },
            new() { Rank = "A", Suit = "♥" }
        };
        var value = BlackjackHelper.CalculateHandValue(hand);
        value.Should().Be(12, "Two aces should be 11 + 1 = 12");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateHandValue_ThreeAces_Returns13()
    {
        // Three aces: 11 + 11 + 11 = 33, two convert to 1, so 13
        var hand = new List<Card>
        {
            new() { Rank = "A", Suit = "♠" },
            new() { Rank = "A", Suit = "♥" },
            new() { Rank = "A", Suit = "♦" }
        };
        var value = BlackjackHelper.CalculateHandValue(hand);
        value.Should().Be(13, "Three aces should be 11 + 1 + 1 = 13");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateHandValue_FourAces_Returns14()
    {
        // Four aces: only one can be 11, rest are 1
        var hand = new List<Card>
        {
            new() { Rank = "A", Suit = "♠" },
            new() { Rank = "A", Suit = "♥" },
            new() { Rank = "A", Suit = "♦" },
            new() { Rank = "A", Suit = "♣" }
        };
        var value = BlackjackHelper.CalculateHandValue(hand);
        value.Should().Be(14, "Four aces should be 11 + 1 + 1 + 1 = 14");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateHandValue_AceConversion_BustPrevention()
    {
        // Ace + King + 5 = 11 + 10 + 5 = 26, ace converts to 1 = 16
        var hand = new List<Card>
        {
            new() { Rank = "A", Suit = "♠" },
            new() { Rank = "K", Suit = "♥" },
            new() { Rank = "5", Suit = "♦" }
        };
        var value = BlackjackHelper.CalculateHandValue(hand);
        value.Should().Be(16, "Ace should convert to 1 to avoid bust");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateHandValue_MultipleAceConversions()
    {
        // 2 Aces + King + 5 = 22 + 15 = 37, both aces convert = 2 + 15 = 17
        var hand = new List<Card>
        {
            new() { Rank = "A", Suit = "♠" },
            new() { Rank = "A", Suit = "♥" },
            new() { Rank = "K", Suit = "♦" },
            new() { Rank = "5", Suit = "♣" }
        };
        var value = BlackjackHelper.CalculateHandValue(hand);
        value.Should().Be(17, "Both aces should convert to 1");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CalculateHandValue_MaxPossibleBust()
    {
        // Four 10s = 40 (max possible without aces)
        var hand = new List<Card>
        {
            new() { Rank = "10", Suit = "♠" },
            new() { Rank = "10", Suit = "♥" },
            new() { Rank = "10", Suit = "♦" },
            new() { Rank = "10", Suit = "♣" }
        };
        var value = BlackjackHelper.CalculateHandValue(hand);
        value.Should().Be(40, "Four 10s should be 40 (bust)");
    }

    #endregion

    #region IsBlackjack Edge Cases

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void IsBlackjack_AceKing_ReturnsTrue()
    {
        var hand = new List<Card>
        {
            new() { Rank = "A", Suit = "♠" },
            new() { Rank = "K", Suit = "♥" }
        };
        BlackjackHelper.IsBlackjack(hand).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void IsBlackjack_KingAce_ReturnsTrue()
    {
        // Order shouldn't matter
        var hand = new List<Card>
        {
            new() { Rank = "K", Suit = "♥" },
            new() { Rank = "A", Suit = "♠" }
        };
        BlackjackHelper.IsBlackjack(hand).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void IsBlackjack_Ace10_ReturnsTrue()
    {
        var hand = new List<Card>
        {
            new() { Rank = "A", Suit = "♠" },
            new() { Rank = "10", Suit = "♥" }
        };
        BlackjackHelper.IsBlackjack(hand).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void IsBlackjack_ThreeCardsEqual21_ReturnsFalse()
    {
        // 21 with 3 cards is NOT blackjack
        var hand = new List<Card>
        {
            new() { Rank = "7", Suit = "♠" },
            new() { Rank = "7", Suit = "♥" },
            new() { Rank = "7", Suit = "♦" }
        };
        BlackjackHelper.IsBlackjack(hand).Should().BeFalse("21 with 3 cards is not blackjack");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void IsBlackjack_TwoNonFaceCardsEqual21_ReturnsFalse()
    {
        // This is impossible in real blackjack, but test the logic
        var hand = new List<Card>
        {
            new() { Rank = "9", Suit = "♠" },
            new() { Rank = "K", Suit = "♥" }
        };
        BlackjackHelper.IsBlackjack(hand).Should().BeFalse("9 + K = 19, not blackjack");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void IsBlackjack_EmptyHand_ReturnsFalse()
    {
        var hand = new List<Card>();
        BlackjackHelper.IsBlackjack(hand).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void IsBlackjack_SingleCard_ReturnsFalse()
    {
        var hand = new List<Card> { new() { Rank = "A", Suit = "♠" } };
        BlackjackHelper.IsBlackjack(hand).Should().BeFalse();
    }

    #endregion

    #region CanSplit Edge Cases

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CanSplit_TwoSameRank_ReturnsTrue()
    {
        var hand = new List<Card>
        {
            new() { Rank = "8", Suit = "♠" },
            new() { Rank = "8", Suit = "♥" }
        };
        BlackjackHelper.CanSplit(hand).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CanSplit_TwoAces_ReturnsTrue()
    {
        var hand = new List<Card>
        {
            new() { Rank = "A", Suit = "♠" },
            new() { Rank = "A", Suit = "♥" }
        };
        BlackjackHelper.CanSplit(hand).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CanSplit_TenAndJack_ReturnsTrue()
    {
        // 10 and J have same VALUE (10), so can split
        var hand = new List<Card>
        {
            new() { Rank = "10", Suit = "♠" },
            new() { Rank = "J", Suit = "♥" }
        };
        BlackjackHelper.CanSplit(hand).Should().BeTrue("10 and J have same value");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CanSplit_KingAndQueen_ReturnsTrue()
    {
        var hand = new List<Card>
        {
            new() { Rank = "K", Suit = "♠" },
            new() { Rank = "Q", Suit = "♥" }
        };
        BlackjackHelper.CanSplit(hand).Should().BeTrue("K and Q have same value");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CanSplit_DifferentValues_ReturnsFalse()
    {
        var hand = new List<Card>
        {
            new() { Rank = "8", Suit = "♠" },
            new() { Rank = "9", Suit = "♥" }
        };
        BlackjackHelper.CanSplit(hand).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CanSplit_SingleCard_ReturnsFalse()
    {
        var hand = new List<Card> { new() { Rank = "8", Suit = "♠" } };
        BlackjackHelper.CanSplit(hand).Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CanSplit_ThreeCards_ReturnsFalse()
    {
        var hand = new List<Card>
        {
            new() { Rank = "8", Suit = "♠" },
            new() { Rank = "8", Suit = "♥" },
            new() { Rank = "8", Suit = "♦" }
        };
        BlackjackHelper.CanSplit(hand).Should().BeFalse("Cannot split with 3 cards");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void CanSplit_EmptyHand_ReturnsFalse()
    {
        var hand = new List<Card>();
        BlackjackHelper.CanSplit(hand).Should().BeFalse();
    }

    #endregion

    #region FormatHand Edge Cases

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void FormatHand_EmptyHand_ReturnsEmptyString()
    {
        var hand = new List<Card>();
        var formatted = BlackjackHelper.FormatHand(hand);
        formatted.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void FormatHand_SingleCard_ReturnsCardString()
    {
        var hand = new List<Card> { new() { Rank = "A", Suit = "♠" } };
        var formatted = BlackjackHelper.FormatHand(hand);
        formatted.Should().Be("A♠");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void FormatHand_HideFirstCard_ShowsHidden()
    {
        var hand = new List<Card>
        {
            new() { Rank = "K", Suit = "♠" },
            new() { Rank = "5", Suit = "♥" }
        };
        var formatted = BlackjackHelper.FormatHand(hand, hideFirstCard: true);
        formatted.Should().Be("[HIDDEN] 5♥");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void FormatHand_HideFirstCard_EmptyHand_ReturnsEmptyString()
    {
        var hand = new List<Card>();
        var formatted = BlackjackHelper.FormatHand(hand, hideFirstCard: true);
        formatted.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void FormatHand_HideFirstCard_SingleCard_ShowsHiddenWithTrailingSpace()
    {
        // Note: Current implementation produces trailing space when no cards remain
        var hand = new List<Card> { new() { Rank = "A", Suit = "♠" } };
        var formatted = BlackjackHelper.FormatHand(hand, hideFirstCard: true);
        formatted.Should().Be("[HIDDEN] ", "Implementation includes trailing space - potential display issue");
    }

    #endregion

    #region BlackjackGameMetaModel State Edge Cases

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GameState_CurrentHandIndex_DefaultsToZero()
    {
        var state = new BlackjackGameMetaModel
        {
            PlayerHands = new List<List<Card>> { new() },
            DealerHand = new List<Card>(),
            Deck = new List<Card>(),
            HasDoubledDown = new List<bool> { false }
        };

        state.CurrentHandIndex.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GameState_CurrentHandIndex_OutOfBounds_NoProtection()
    {
        var state = new BlackjackGameMetaModel
        {
            PlayerHands = new List<List<Card>> { new() },
            DealerHand = new List<Card>(),
            Deck = new List<Card>(),
            HasDoubledDown = new List<bool> { false },
            CurrentHandIndex = 5 // Out of bounds for single-hand game
        };

        // This demonstrates the vulnerability - no bounds checking
        Action accessOutOfBounds = () => { var _ = state.PlayerHands[state.CurrentHandIndex]; };
        accessOutOfBounds.Should().Throw<ArgumentOutOfRangeException>(
            "ISSUE: CurrentHandIndex can be set to invalid value without validation");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GameState_EmptyPlayerHands_AccessThrows()
    {
        var state = new BlackjackGameMetaModel
        {
            PlayerHands = new List<List<Card>>(), // Empty!
            DealerHand = new List<Card>(),
            Deck = new List<Card>(),
            HasDoubledDown = new List<bool>()
        };

        Action accessEmptyHands = () => { var _ = state.PlayerHands[0]; };
        accessEmptyHands.Should().Throw<ArgumentOutOfRangeException>(
            "Accessing PlayerHands[0] on empty list should throw");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GameState_MismatchedDoubledDownList_NoProtection()
    {
        // HasDoubledDown list should match PlayerHands count, but no validation
        var state = new BlackjackGameMetaModel
        {
            PlayerHands = new List<List<Card>>
            {
                new() { new() { Rank = "A", Suit = "♠" } },
                new() { new() { Rank = "K", Suit = "♥" } }
            },
            DealerHand = new List<Card>(),
            Deck = new List<Card>(),
            HasDoubledDown = new List<bool> { false } // Only 1 entry for 2 hands!
        };

        state.PlayerHands.Count.Should().NotBe(state.HasDoubledDown.Count,
            "ISSUE: No validation that HasDoubledDown matches PlayerHands count");
    }

    #endregion

    #region Deck Boundary Tests

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void DeckRemoveRange_InsufficientCards_ThrowsArgumentException()
    {
        var deck = new List<Card>
        {
            new() { Rank = "A", Suit = "♠" },
            new() { Rank = "K", Suit = "♥" },
            new() { Rank = "Q", Suit = "♦" }
        };

        // BlackjackCommand.cs line 145-147: deck.RemoveRange(0, 4) without check
        // This demonstrates the issue - trying to deal 4 cards from 3-card deck
        Action removeRange = () => deck.RemoveRange(0, 4);
        removeRange.Should().Throw<ArgumentException>(
            "ISSUE: RemoveRange(0, 4) on 3-card deck should throw");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void DeckRemoveRange_ExactlyEnoughCards_Succeeds()
    {
        var deck = new List<Card>
        {
            new() { Rank = "A", Suit = "♠" },
            new() { Rank = "K", Suit = "♥" },
            new() { Rank = "Q", Suit = "♦" },
            new() { Rank = "J", Suit = "♣" }
        };

        Action removeRange = () => deck.RemoveRange(0, 4);
        removeRange.Should().NotThrow();
        deck.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void DeckRemoveRange_MoreThanEnoughCards_Succeeds()
    {
        var deck = new List<Card>
        {
            new() { Rank = "A", Suit = "♠" },
            new() { Rank = "K", Suit = "♥" },
            new() { Rank = "Q", Suit = "♦" },
            new() { Rank = "J", Suit = "♣" },
            new() { Rank = "10", Suit = "♠" }
        };

        deck.RemoveRange(0, 4);
        deck.Should().HaveCount(1);
        deck[0].Rank.Should().Be("10");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void EmptyDeck_DrawCard_Throws()
    {
        var deck = new List<Card>();

        Action drawCard = () => { var _ = deck[0]; };
        drawCard.Should().Throw<ArgumentOutOfRangeException>(
            "Drawing from empty deck should throw");
    }

    #endregion

    #region Wager Amount Edge Cases

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void GameState_ZeroOrNegativeWager_Allowed(decimal wager)
    {
        // The model doesn't validate wager amounts
        var state = new BlackjackGameMetaModel
        {
            PlayerHands = new List<List<Card>> { new() },
            DealerHand = new List<Card>(),
            Deck = new List<Card>(),
            HasDoubledDown = new List<bool> { false },
            OriginalWagerAmount = wager
        };

        state.OriginalWagerAmount.Should().Be(wager,
            "Model allows zero/negative wager - validation should be done elsewhere");
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void GameState_VeryLargeWager_Allowed()
    {
        var state = new BlackjackGameMetaModel
        {
            PlayerHands = new List<List<Card>> { new() },
            DealerHand = new List<Card>(),
            Deck = new List<Card>(),
            HasDoubledDown = new List<bool> { false },
            OriginalWagerAmount = decimal.MaxValue
        };

        state.OriginalWagerAmount.Should().Be(decimal.MaxValue);
    }

    #endregion

    #region Card Suit Edge Cases

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData("♠")]
    [InlineData("♥")]
    [InlineData("♦")]
    [InlineData("♣")]
    public void Card_ValidSuits_WorkCorrectly(string suit)
    {
        var card = new Card { Rank = "A", Suit = suit };
        card.Suit.Should().Be(suit);
        card.ToString().Should().Be($"A{suit}");
    }

    [Theory]
    [Trait("Category", "EdgeCase")]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("spades")]
    public void Card_InvalidSuit_NoValidation(string suit)
    {
        // Card doesn't validate suits - any string is accepted
        var card = new Card { Rank = "A", Suit = suit };
        card.Suit.Should().Be(suit);
        // This could cause display issues but won't throw
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void Card_NullSuit_AllowedButMayBreak()
    {
        var card = new Card { Rank = "A", Suit = null! };

        // ToString will concatenate null
        Action toString = () => { var _ = card.ToString(); };
        toString.Should().NotThrow("null suit doesn't throw, just produces 'A' + null");
    }

    #endregion

    #region Split Mechanics Edge Cases

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void SplitHand_IndexManipulation_NoValidation()
    {
        var state = new BlackjackGameMetaModel
        {
            PlayerHands = new List<List<Card>>
            {
                new() { new() { Rank = "8", Suit = "♠" } },
                new() { new() { Rank = "8", Suit = "♥" } }
            },
            DealerHand = new List<Card>(),
            Deck = new List<Card>(),
            HasDoubledDown = new List<bool> { false, false },
            CurrentHandIndex = 0
        };

        // Player can manually set index to any value
        state.CurrentHandIndex = -1;
        state.CurrentHandIndex.Should().Be(-1, "No bounds validation on CurrentHandIndex");

        Action accessNegativeIndex = () => { var _ = state.PlayerHands[state.CurrentHandIndex]; };
        accessNegativeIndex.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    [Trait("Category", "EdgeCase")]
    public void SplitHand_MaxSplits_NoLimit()
    {
        // Some casinos limit splits to 3-4 hands max
        var hands = new List<List<Card>>();
        for (int i = 0; i < 10; i++)
        {
            hands.Add(new List<Card> { new() { Rank = "8", Suit = "♠" } });
        }

        var state = new BlackjackGameMetaModel
        {
            PlayerHands = hands,
            DealerHand = new List<Card>(),
            Deck = new List<Card>(),
            HasDoubledDown = Enumerable.Repeat(false, 10).ToList()
        };

        state.PlayerHands.Count.Should().Be(10, "No limit on number of hands");
    }

    #endregion
}
