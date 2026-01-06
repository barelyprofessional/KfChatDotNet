using KfChatDotNetBot.Models.DbModels;
using Money = KfChatDotNetBot.Services.Money;

namespace KfChatDotNetBot.Models;

public class BlackjackGameMetaModel
{
    /// <summary>
    /// Player's hands (multiple if split)
    /// </summary>
    public required List<List<Card>> PlayerHands { get; set; }
    
    /// <summary>
    /// Dealer's hand
    /// </summary>
    public required List<Card> DealerHand { get; set; }
    
    /// <summary>
    /// Remaining cards in the deck
    /// </summary>
    public required List<Card> Deck { get; set; }
    
    /// <summary>
    /// Whether each hand has doubled down (can only hit once more)
    /// </summary>
    public required List<bool> HasDoubledDown { get; set; }
    
    /// <summary>
    /// Current hand being played (for split hands)
    /// </summary>
    public int CurrentHandIndex { get; set; } = 0;
    
    /// <summary>
    /// Original wager amount (per hand)
    /// </summary>
    public decimal OriginalWagerAmount { get; set; }
}

public class Card
{
    /// <summary>
    /// Card rank (2-10, J, Q, K, A)
    /// </summary>
    public required string Rank { get; set; }
    
    /// <summary>
    /// Card suit (♠, ♥, ♦, ♣)
    /// </summary>
    public required string Suit { get; set; }
    
    /// <summary>
    /// Get the blackjack value of this card
    /// </summary>
    public int GetValue()
    {
        return Rank switch
        {
            "A" => 11, // Aces are handled specially in hand calculation
            "K" or "Q" or "J" => 10,
            _ => int.Parse(Rank)
        };
    }
    
    /// <summary>
    /// Display card as string
    /// </summary>
    public override string ToString()
    {
        return $"{Rank}{Suit}";
    }
}

public static class BlackjackHelper
{
    private static readonly string[] Ranks = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };
    private static readonly string[] Suits = { "♠", "♥", "♦", "♣" };
    
    /// <summary>
    /// Create a new shuffled deck
    /// </summary>
    public static List<Card> CreateDeck(GamblerDbModel gambler)
    {
        var deck = new List<Card>();
        foreach (var suit in Suits)
        {
            foreach (var rank in Ranks)
            {
                deck.Add(new Card { Rank = rank, Suit = suit });
            }
        }
        
        // Shuffle using Fisher-Yates
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Money.GetRandomNumber(gambler, 0, i + 1, incrementMaxParam: false);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        
        return deck;
    }
    
    /// <summary>
    /// Calculate hand value with proper Ace handling
    /// </summary>
    public static int CalculateHandValue(List<Card> hand)
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
        
        // Convert Aces from 11 to 1 if needed to avoid bust
        while (value > 21 && aces > 0)
        {
            value -= 10;
            aces--;
        }
        
        return value;
    }
    
    /// <summary>
    /// Check if hand is blackjack (21 with 2 cards)
    /// </summary>
    public static bool IsBlackjack(List<Card> hand)
    {
        return hand.Count == 2 && CalculateHandValue(hand) == 21;
    }
    
    /// <summary>
    /// Check if a hand can be split (two cards of same rank)
    /// </summary>
    public static bool CanSplit(List<Card> hand)
    {
        if (hand.Count != 2)
            return false;
        
        // Check if both cards have the same value (not rank, to allow 10/J/Q/K splits)
        return hand[0].GetValue() == hand[1].GetValue();
    }
    
    /// <summary>
    /// Format hand for display
    /// </summary>
    public static string FormatHand(List<Card> hand, bool hideFirstCard = false)
    {
        if (hideFirstCard && hand.Count > 0)
        {
            return $"[HIDDEN] {string.Join(" ", hand.Skip(1).Select(c => c.ToString()))}";
        }
        return string.Join(" ", hand.Select(c => c.ToString()));
    }
}