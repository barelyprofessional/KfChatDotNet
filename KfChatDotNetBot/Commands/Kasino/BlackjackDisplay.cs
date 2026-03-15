using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;

namespace KfChatDotNetBot.Commands.Kasino;

/// <summary>
/// Builds every chat message string for the blackjack game.
/// No game logic lives here — only presentation.
/// <para>
/// Keeping all string construction in one place means tweaking the UI never
/// requires touching the game-flow code in <see cref="BlackjackCommand"/>.
/// </para>
/// </summary>
internal static class BlackjackDisplay
{
    // ─────────────────────────────────────────────────────────────────────────
    // Primitive helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// Wraps ♥ and ♦ in the game's losing-text red so suit glyphs render in red.
    /// Applied to every hand string so the color is consistent with loss messages.
    internal static string ColorizeSuits(string text, string redHex) =>
        text.Replace("♥", $"[COLOR={redHex}]♥[/COLOR]")
            .Replace("♦", $"[COLOR={redHex}]♦[/COLOR]");

    private static string FmtHand(List<Card> hand, string redHex, bool hideFirst = false) =>
        ColorizeSuits(BlackjackHelper.FormatHand(hand, hideFirstCard: hideFirst), redHex);

    private static string FmtCard(Card card, string redHex) =>
        ColorizeSuits(card.ToString()!, redHex);

    /// Compact action-hint line. Only advertises actions the player can actually take right now.
    /// ✦ marks double-down; ✂ marks split — both are hidden once unavailable.
    private static string ActionHints(bool canDouble = false, bool canSplit = false)
    {
        var parts = new List<string> { "[B]hit[/B]", "[B]stand[/B]" };
        if (canDouble) parts.Add("[B]double[/B] ✦");
        if (canSplit)  parts.Add("[B]split[/B] ✂");
        return "!bj: " + string.Join(" · ", parts);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Game-start (fresh deal)
    // Two lines: hand state + action hints.
    // Double is always shown — balance check happens inside HandleDouble if attempted.
    // ─────────────────────────────────────────────────────────────────────────

    public static async Task<string> GameStart(
        UserDbModel user, decimal wager,
        List<Card> playerHand, int playerValue,
        List<Card> dealerHand,
        bool canSplit, string redHex)
    {
        return
            $"🃏 [B]{user.FormatUsername()}[/B] · {await wager.FormatKasinoCurrencyAsync()} — " +
            $"[B]You:[/B] {FmtHand(playerHand, redHex)} ({playerValue}) " +
            $"[I]vs[/I] [B]Dealer:[/B] {FmtHand(dealerHand, redHex, hideFirst: true)}[br]" +
            ActionHints(canDouble: true, canSplit: canSplit);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Hit still in progress (hand not yet resolved)
    // Two lines: drew-card + updated state + action hints.
    // ─────────────────────────────────────────────────────────────────────────

    public static string HitInProgress(
        UserDbModel user, Card drawnCard,
        List<Card> currentHand, int handValue,
        List<Card> dealerHand,
        string handLabel, string redHex)
    {
        return
            $"{user.FormatUsername()}{handLabel} drew {FmtCard(drawnCard, redHex)} — " +
            $"[B]You:[/B] {FmtHand(currentHand, redHex)} ({handValue}) " +
            $"[I]vs[/I] [B]Dealer:[/B] {FmtHand(dealerHand, redHex, hideFirst: true)}[br]" +
            ActionHints();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Double-down confirmation
    // One line, shown once before the auto-hit silently proceeds to resolution.
    // ─────────────────────────────────────────────────────────────────────────

    public static async Task<string> DoubledDown(UserDbModel user, decimal newTotalWager) =>
        $"{user.FormatUsername()} doubled down · Wager: [B]{await newTotalWager.FormatKasinoCurrencyAsync()}[/B]";

    // ─────────────────────────────────────────────────────────────────────────
    // Split: initial deal display
    // Three lines: wager header, both hands side-by-side, action hints for Hand 1.
    // ─────────────────────────────────────────────────────────────────────────

    public static async Task<string> SplitDeal(
        UserDbModel user, decimal totalWager,
        List<Card> hand1, int value1,
        List<Card> hand2, int value2,
        string redHex)
    {
        return
            $"{user.FormatUsername()} split · Wager: [B]{await totalWager.FormatKasinoCurrencyAsync()}[/B][br]" +
            $"[B]H1:[/B] {FmtHand(hand1, redHex)} ({value1}) · [B]H2:[/B] {FmtHand(hand2, redHex)} ({value2})[br]" +
            $"Playing [B]H1[/B] — {ActionHints()}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Split: hand transition
    // Two lines combining "what happened to the finished hand" and "what you
    // have on the next hand" into one message, saving a separate chat post.
    // ─────────────────────────────────────────────────────────────────────────

    public static string SplitTransition(
        int finishedIndex, List<Card> finishedHand, int finishedValue, bool busted,
        int nextIndex, List<Card> nextHand, int nextValue,
        string redHex)
    {
        var outcome = busted
            ? $"[B][COLOR={redHex}]BUST[/COLOR][/B]"
            : $"stood [B]{finishedValue}[/B]";

        return
            $"[B]H{finishedIndex + 1}:[/B] {FmtHand(finishedHand, redHex)} ({finishedValue}) — {outcome} " +
            $"→ [B]H{nextIndex + 1}:[/B] {FmtHand(nextHand, redHex)} ({nextValue})[br]" +
            ActionHints();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Final result
    //   Single hand → 2 lines: You vs Dealer — RESULT / Net · Balance
    //   Split game  → 3 lines: header / H1 — R · H2 — R / Dealer · Net · Balance
    // ─────────────────────────────────────────────────────────────────────────

    public static async Task<string> FinalResult(
        UserDbModel user,
        IReadOnlyList<HandResultData> results,
        List<Card> dealerHand, int dealerValue,
        decimal totalEffect, decimal newBalance,
        bool isSplitGame, string greenHex, string redHex)
    {
        var sb = new System.Text.StringBuilder();
        var sign = totalEffect >= 0 ? "+" : "";
        var netLine =
            $"[U]Net {sign}{await totalEffect.FormatKasinoCurrencyAsync()} · " +
            $"Balance {await newBalance.FormatKasinoCurrencyAsync()}[/U]";

        if (!isSplitGame)
        {
            // ── Single hand: hand + dealer + result all on one line ──────────
            var r = results[0];
            sb.Append(
                $"🃏 [B]{user.FormatUsername()}[/B] · " +
                $"[B]You:[/B] {FmtHand(r.Hand, redHex)} ({r.PlayerValue}) " +
                $"[I]vs[/I] [B]Dealer:[/B] {FmtHand(dealerHand, redHex)} ({dealerValue}) — " +
                $"{await FormatOutcomeTag(r, greenHex, redHex)}[br]" +
                netLine);
        }
        else
        {
            // ── Split game: header, then both hands on one line, dealer + net ─
            sb.Append($"🃏 [B]{user.FormatUsername()}[/B][br]");

            var handParts = new List<string>();
            foreach (var r in results)
            {
                handParts.Add(
                    $"[B]H{r.HandIndex + 1}:[/B] {FmtHand(r.Hand, redHex)} ({r.PlayerValue}) — " +
                    $"{await FormatOutcomeTag(r, greenHex, redHex)}");
            }

            sb.Append(string.Join(" · ", handParts) + "[br]");
            sb.Append($"[B]Dealer:[/B] {FmtHand(dealerHand, redHex)} ({dealerValue}) · {netLine}");
        }

        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Outcome classification
    // Called by BlackjackCommand.ResolveGame to populate HandResultData before
    // passing it here for display. Keeping it in this file co-locates it with
    // the outcome tags it feeds into.
    // ─────────────────────────────────────────────────────────────────────────

    internal static (HandOutcome Outcome, decimal Effect) ClassifyHand(
        int playerValue, bool playerBlackjack,
        int dealerValue, bool dealerBlackjack,
        decimal handWager)
    {
        if (playerBlackjack && dealerBlackjack) return (HandOutcome.Push, 0);
        if (playerBlackjack)                    return (HandOutcome.Blackjack, handWager * 1.5m);
        if (dealerBlackjack)                    return (HandOutcome.DealerBlackjack, -handWager);
        if (playerValue > 21)                   return (HandOutcome.Bust, -handWager);
        if (dealerValue > 21)                   return (HandOutcome.DealerBust, handWager);
        if (playerValue > dealerValue)          return (HandOutcome.Win, handWager);
        if (playerValue < dealerValue)          return (HandOutcome.Lose, -handWager);
        return (HandOutcome.Push, 0);
    }

    private static async Task<string> FormatOutcomeTag(HandResultData r, string greenHex, string redHex)
    {
        var amt = await Math.Abs(r.Effect).FormatKasinoCurrencyAsync();
        return r.Outcome switch
        {
            HandOutcome.Blackjack       => $"[B][COLOR={greenHex}]BLACKJACK! +{amt}[/COLOR][/B]",
            HandOutcome.Win             => $"[B][COLOR={greenHex}]WIN! +{amt}[/COLOR][/B]",
            HandOutcome.DealerBust      => $"[B][COLOR={greenHex}]DEALER BUST! +{amt}[/COLOR][/B]",
            HandOutcome.Lose            => $"[B][COLOR={redHex}]LOSE! -{amt}[/COLOR][/B]",
            HandOutcome.Bust            => $"[B][COLOR={redHex}]BUST! -{amt}[/COLOR][/B]",
            HandOutcome.DealerBlackjack => $"[B][COLOR={redHex}]DEALER BLACKJACK! -{amt}[/COLOR][/B]",
            HandOutcome.Push            => "[B][COLOR=orange]PUSH[/COLOR][/B]",
            _                           => "?"
        };
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Supporting types used across BlackjackDisplay and BlackjackCommand
// ─────────────────────────────────────────────────────────────────────────────

internal enum HandOutcome
{
    Blackjack, DealerBlackjack, Win, Lose, Bust, DealerBust, Push
}

/// Pre-computed per-hand result data passed from BlackjackCommand.ResolveGame
/// to BlackjackDisplay.FinalResult for rendering.
internal record HandResultData(
    int HandIndex,
    List<Card> Hand,
    int PlayerValue,
    HandOutcome Outcome,
    decimal Effect);