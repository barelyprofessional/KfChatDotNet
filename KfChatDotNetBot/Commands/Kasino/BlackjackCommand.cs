using System.Text.Json;
using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class BlackjackCommand : ICommand
{
    private static readonly TimeSpan GameTimeout = TimeSpan.FromMinutes(5);

    // Colors fetched once per user action and threaded through to all helpers,
    // so we never fetch them redundantly mid-game-flow.
    private record GameColors(string Green, string Red);

    public List<Regex> Patterns => [
        new Regex(@"^blackjack (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^blackjack (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^bj (?<amount>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^bj (?<amount>\d+\.\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^blackjack (?<action>hit|stand|double|split)$", RegexOptions.IgnoreCase),
        new Regex(@"^bj (?<action>hit|stand|double|split)$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!blackjack <amount> or !bj <amount> to start, then !bj hit/stand/double/split";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(15);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 5,
        Window = TimeSpan.FromSeconds(20),
        Flags = RateLimitFlags.NoAutoDeleteCooldownResponse
    };

    private ApplicationDbContext _dbContext = new();

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay, BuiltIn.Keys.KasinoBlackjackCleanupDelay,
            BuiltIn.Keys.KasinoBlackjackEnabled
        ]);

        var blackjackEnabled = settings[BuiltIn.Keys.KasinoBlackjackEnabled].ToBoolean();
        if (!blackjackEnabled)
        {
            var gameDisabledCleanupDelay = TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay].ToType<int>());
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, blackjack is currently disabled.",
                true, autoDeleteAfter: gameDisabledCleanupDelay);
            return;
        }

        var cleanupDelay = TimeSpan.FromMilliseconds(settings[BuiltIn.Keys.KasinoBlackjackCleanupDelay].ToType<int>());

        if (arguments.TryGetValue("amount", out var amountGroup))
        {
            await StartNewGame(botInstance, user, amountGroup.Value, cleanupDelay, ctx);
            return;
        }

        if (arguments.TryGetValue("action", out var actionGroup))
        {
            await ContinueGame(botInstance, user, actionGroup.Value.ToLower(), cleanupDelay, ctx);
            return;
        }

        throw new InvalidOperationException($"User {user.KfUsername} somehow ran blackjack without an amount or action: {message.MessageRaw}");
    }


    private async Task StartNewGame(ChatBot botInstance, UserDbModel user, string amountStr,
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();

        // Fetch colors upfront — needed for both the immediate-blackjack ResolveGame path
        // and the normal GameStart display path.
        var colorSettings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
        ]);
        var colors = new GameColors(
            colorSettings[BuiltIn.Keys.KiwiFarmsGreenColor].Value,
            colorSettings[BuiltIn.Keys.KiwiFarmsRedColor].Value);

        var wager = Convert.ToDecimal(amountStr);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);

        if (gambler == null)
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");

        if (wager == 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you have to wager more than {await wager.FormatKasinoCurrencyAsync()}",
                true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        if (gambler.Balance < wager)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this wager.",
                true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        // Check for an existing incomplete game
        var existingGame = await _dbContext.Wagers
            .OrderBy(x => x.Id)
            .LastOrDefaultAsync(w => w.Gambler.Id == gambler.Id &&
                                      w.Game == WagerGame.Blackjack &&
                                      !w.IsComplete && w.GameMeta != null,
                cancellationToken: ctx);

        if (existingGame != null)
        {
            try
            {
                _ = JsonSerializer.Deserialize<BlackjackGameMetaModel>(existingGame.GameMeta!) ??
                    throw new InvalidOperationException();
            }
            catch (Exception e)
            {
                logger.Error($"Caught error when deserializing meta for wager ID {existingGame.Id}");
                logger.Error(e);
                await botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, somehow your previous blackjack game state got messed up. Please try again",
                    true, autoDeleteAfter: cleanupDelay);
                existingGame.IsComplete = true;
                await _dbContext.SaveChangesAsync(ctx);
                throw;
            }

            var timeSinceStart = DateTimeOffset.UtcNow - existingGame.Time;
            if (timeSinceStart > GameTimeout)
            {
                await ForfeitGame(botInstance, user, gambler, existingGame, cleanupDelay, ctx);
                return;
            }

            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you already have an active blackjack game. Use !bj hit or !bj stand to continue.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        // Deal initial hands
        var deck = BlackjackHelper.CreateDeck(gambler);
        var playerHand = new List<Card> { deck[0], deck[2] };
        var dealerHand = new List<Card> { deck[1], deck[3] };
        deck.RemoveRange(0, 4);

        var newGameState = new BlackjackGameMetaModel
        {
            PlayerHands = new List<List<Card>> { playerHand },
            DealerHand = dealerHand,
            Deck = deck,
            HasDoubledDown = false,
            CurrentHandIndex = 0,
            OriginalWagerAmount = wager
        };

        await Money.NewWagerAsync(
            gambler.Id, wager, -wager,
            WagerGame.Blackjack,
            autoModifyBalance: true,
            gameMeta: newGameState,
            isComplete: false,
            ct: ctx);

        var createdWager = await _dbContext.Wagers
            .OrderBy(x => x.Id)
            .LastOrDefaultAsync(
                w => w.Gambler.Id == gambler.Id && w.Game == WagerGame.Blackjack && !w.IsComplete && w.GameMeta != null,
                cancellationToken: ctx) ?? throw new InvalidOperationException();
        createdWager.GameMeta = JsonSerializer.Serialize(newGameState);
        await _dbContext.SaveChangesAsync(ctx);

        // Immediate blackjack check — goes straight to resolution
        if (BlackjackHelper.IsBlackjack(playerHand) || BlackjackHelper.IsBlackjack(dealerHand))
        {
            await ResolveGame(botInstance, user, gambler, createdWager, newGameState, colors, cleanupDelay, ctx);
            return;
        }

        var playerValue = BlackjackHelper.CalculateHandValue(playerHand);
        var canSplit = BlackjackHelper.CanSplit(playerHand);

        await botInstance.SendChatMessageAsync(
            await BlackjackDisplay.GameStart(user, wager, playerHand, playerValue, dealerHand, canSplit, colors.Red),
            true, autoDeleteAfter: cleanupDelay);
    }


    private async Task ContinueGame(ChatBot botInstance, UserDbModel user, string action,
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        // Fetch colors once here; pass them to every downstream method
        var colorSettings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KiwiFarmsGreenColor, BuiltIn.Keys.KiwiFarmsRedColor
        ]);
        var colors = new GameColors(
            colorSettings[BuiltIn.Keys.KiwiFarmsGreenColor].Value,
            colorSettings[BuiltIn.Keys.KiwiFarmsRedColor].Value);

        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);

        if (gambler == null)
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");

        var activeWager = await _dbContext.Wagers
            .OrderBy(x => x.Id)
            .LastOrDefaultAsync(w => w.Gambler.Id == gambler.Id &&
                                      w.Game == WagerGame.Blackjack &&
                                      !w.IsComplete && w.GameMeta != null,
                cancellationToken: ctx);

        if (activeWager == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you don't have an active blackjack game. Start one with !bj <amount>",
                true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        var currentGameState = JsonSerializer.Deserialize<BlackjackGameMetaModel>(activeWager.GameMeta!);
        if (currentGameState == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your game data is corrupted. Please start a new game.",
                true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            activeWager.IsComplete = true;
            await _dbContext.SaveChangesAsync(ctx);
            return;
        }

        var timeSinceStart = DateTimeOffset.UtcNow - activeWager.Time;
        if (timeSinceStart > GameTimeout)
        {
            await ForfeitGame(botInstance, user, gambler, activeWager, cleanupDelay, ctx);
            return;
        }

        switch (action)
        {
            case "hit":
                await HandleHit(botInstance, user, gambler, activeWager, currentGameState, colors, cleanupDelay, ctx);
                break;
            case "stand":
                await HandleStand(botInstance, user, gambler, activeWager, currentGameState, colors, cleanupDelay, ctx);
                break;
            case "double":
                await HandleDouble(botInstance, user, gambler, activeWager, currentGameState, colors, cleanupDelay, ctx);
                break;
            case "split":
                await HandleSplit(botInstance, user, gambler, activeWager, currentGameState, colors, cleanupDelay, ctx);
                break;
        }
    }


    private async Task HandleHit(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, GameColors colors,
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        if (gameState.Deck.Count == 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, game error: no cards left in deck. Game forfeited.",
                true, autoDeleteAfter: cleanupDelay);
            await ForfeitGame(botInstance, user, gambler, wager, cleanupDelay, ctx);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        var currentHand = gameState.PlayerHands[gameState.CurrentHandIndex];
        var handLabel = gameState.PlayerHands.Count > 1 ? $" (H{gameState.CurrentHandIndex + 1})" : "";

        var card = gameState.Deck[0];
        gameState.Deck.RemoveAt(0);
        currentHand.Add(card);

        var playerValue = BlackjackHelper.CalculateHandValue(currentHand);
        bool handEnded = playerValue > 21 || playerValue == 21 || gameState.HasDoubledDown;

        if (!handEnded)
        {
            // Hand is still live — show updated state and prompt for next action
            wager.GameMeta = JsonSerializer.Serialize(gameState);
            await _dbContext.SaveChangesAsync(ctx);
            await botInstance.SendChatMessageAsync(
                BlackjackDisplay.HitInProgress(user, card, currentHand, playerValue, gameState.DealerHand, handLabel, colors.Red),
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        // Hand ended (bust / 21 / post-double auto-stand).
        // MoveToNextHandOrResolve sends the combined transition message when moving
        // to the next split hand, or falls through silently to ResolveGame.
        await MoveToNextHandOrResolve(botInstance, user, gambler, wager, gameState,
            currentHand, busted: playerValue > 21, colors, cleanupDelay, ctx);
    }


    private async Task HandleStand(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, GameColors colors,
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var currentHand = gameState.PlayerHands[gameState.CurrentHandIndex];

        // No stand message needed here — MoveToNextHandOrResolve handles all output:
        // a combined split-transition message when moving to the next hand, and
        // silence when falling through to the final resolution.
        await MoveToNextHandOrResolve(botInstance, user, gambler, wager, gameState,
            currentHand, busted: false, colors, cleanupDelay, ctx);
    }


    private async Task HandleDouble(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, GameColors colors,
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var currentHand = gameState.PlayerHands[gameState.CurrentHandIndex];

        if (currentHand.Count != 2)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you can only double down on your first action.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        if (gameState.PlayerHands.Count > 1)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you cannot double down after splitting.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        if (gambler.Balance < gameState.OriginalWagerAmount)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you don't have enough balance to double down.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }

        var additionalWager = gameState.OriginalWagerAmount;
        await Money.ModifyBalanceAsync(gambler.Id, -additionalWager, TransactionSourceEventType.Gambling,
            $"Double down for {wager.Id}", ct: ctx);
        wager.WagerAmount += additionalWager;
        wager.WagerEffect -= additionalWager;
        gameState.HasDoubledDown = true;
        await _dbContext.SaveChangesAsync(ctx);

        // Confirm the double, then let HandleHit draw the one card and auto-stand.
        // HasDoubledDown is now true, so HandleHit treats the hand as ended and falls
        // through silently to ResolveGame — just two total messages: this + the result.
        await botInstance.SendChatMessageAsync(
            await BlackjackDisplay.DoubledDown(user, wager.WagerAmount),
            true, autoDeleteAfter: cleanupDelay);

        await HandleHit(botInstance, user, gambler, wager, gameState, colors, cleanupDelay, ctx);
    }


    private async Task HandleSplit(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, GameColors colors,
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var currentHand = gameState.PlayerHands[gameState.CurrentHandIndex];

        if (!BlackjackHelper.CanSplit(currentHand))
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you can only split with two cards of the same rank.",
                true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        if (gameState.PlayerHands.Count > 1)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you can only split once per game.",
                true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        if (gambler.Balance < gameState.OriginalWagerAmount)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you don't have enough balance to split.",
                true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        if (gameState.Deck.Count < 2)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, not enough cards in deck to split.",
                true, autoDeleteAfter: cleanupDelay);
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        var card1 = currentHand[0];
        var card2 = currentHand[1];
        var hand1 = new List<Card> { card1, gameState.Deck[0] };
        var hand2 = new List<Card> { card2, gameState.Deck[1] };
        gameState.Deck.RemoveRange(0, 2);

        gameState.PlayerHands = new List<List<Card>> { hand1, hand2 };
        gameState.HasDoubledDown = false;
        gameState.CurrentHandIndex = 0;

        var additionalWager = gameState.OriginalWagerAmount;
        await Money.ModifyBalanceAsync(gambler.Id, -additionalWager, TransactionSourceEventType.Gambling,
            $"Split for {wager.Id}", ct: ctx);
        wager.WagerAmount += additionalWager;
        wager.WagerEffect -= additionalWager;

        wager.GameMeta = JsonSerializer.Serialize(gameState);
        await _dbContext.SaveChangesAsync(ctx);

        var value1 = BlackjackHelper.CalculateHandValue(hand1);
        var value2 = BlackjackHelper.CalculateHandValue(hand2);

        await botInstance.SendChatMessageAsync(
            await BlackjackDisplay.SplitDeal(user, wager.WagerAmount, hand1, value1, hand2, value2, colors.Red),
            true, autoDeleteAfter: cleanupDelay);
    }


    /// <summary>
    /// Advances to the next split hand, or kicks off dealer play and resolution.
    /// </summary>
    /// <param name="finishedHand">The hand that just ended (bust, stand, or doubled auto-stand).</param>
    /// <param name="busted">True if the finished hand went over 21.</param>
    private async Task MoveToNextHandOrResolve(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState,
        List<Card> finishedHand, bool busted,
        GameColors colors, TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var finishedIndex = gameState.CurrentHandIndex;
        gameState.CurrentHandIndex++;

        if (gameState.CurrentHandIndex < gameState.PlayerHands.Count)
        {
            // More split hands to play — one combined message covers both
            // "what happened to the hand that just ended" and "here's your next hand".
            wager.GameMeta = JsonSerializer.Serialize(gameState);
            await _dbContext.SaveChangesAsync(ctx);

            var finishedValue = BlackjackHelper.CalculateHandValue(finishedHand);
            var nextHand = gameState.PlayerHands[gameState.CurrentHandIndex];
            var nextValue = BlackjackHelper.CalculateHandValue(nextHand);

            await botInstance.SendChatMessageAsync(
                BlackjackDisplay.SplitTransition(
                    finishedIndex, finishedHand, finishedValue, busted,
                    gameState.CurrentHandIndex, nextHand, nextValue,
                    colors.Red),
                true, autoDeleteAfter: cleanupDelay);
        }
        else
        {
            await PlayDealerAndResolve(botInstance, user, gambler, wager, gameState, colors, cleanupDelay, ctx);
        }
    }


    private async Task PlayDealerAndResolve(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, GameColors colors,
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        // Dealer only plays when at least one player hand hasn't busted
        bool allHandsBusted = gameState.PlayerHands.All(hand => BlackjackHelper.CalculateHandValue(hand) > 21);

        if (!allHandsBusted)
        {
            var dealerValue = BlackjackHelper.CalculateHandValue(gameState.DealerHand);
            while (dealerValue < 17)
            {
                if (gameState.Deck.Count == 0)
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, game error: dealer ran out of cards. Game forfeited.",
                        true, autoDeleteAfter: cleanupDelay);
                    await ForfeitGame(botInstance, user, gambler, wager, cleanupDelay, ctx);
                    return;
                }

                var card = gameState.Deck[0];
                gameState.Deck.RemoveAt(0);
                gameState.DealerHand.Add(card);
                dealerValue = BlackjackHelper.CalculateHandValue(gameState.DealerHand);
            }
        }

        await ResolveGame(botInstance, user, gambler, wager, gameState, colors, cleanupDelay, ctx);
    }


    private async Task ResolveGame(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, BlackjackGameMetaModel gameState, GameColors colors,
        TimeSpan cleanupDelay, CancellationToken ctx)
    {
        var dealerValue = BlackjackHelper.CalculateHandValue(gameState.DealerHand);
        var dealerBlackjack = BlackjackHelper.IsBlackjack(gameState.DealerHand);
        bool isSplitGame = gameState.PlayerHands.Count > 1;

        decimal totalEffect = 0;
        var results = new List<HandResultData>();

        for (int i = 0; i < gameState.PlayerHands.Count; i++)
        {
            var hand = gameState.PlayerHands[i];
            var playerValue = BlackjackHelper.CalculateHandValue(hand);
            var playerBlackjack = BlackjackHelper.IsBlackjack(hand);
            // Split hands each pay the original per-hand wager; a single hand pays the full
            // (possibly doubled) wager amount already tracked in wager.WagerAmount.
            var handWager = isSplitGame ? gameState.OriginalWagerAmount : wager.WagerAmount;

            var (outcome, effect) = BlackjackDisplay.ClassifyHand(
                playerValue, playerBlackjack, dealerValue, dealerBlackjack, handWager);

            results.Add(new HandResultData(i, hand, playerValue, outcome, effect));
            totalEffect += effect;
        }

        wager.IsComplete = true;
        wager.WagerEffect = totalEffect;
        wager.Multiplier = (totalEffect + wager.WagerAmount) / wager.WagerAmount;
        await _dbContext.SaveChangesAsync(ctx);

        var balanceAdjustment = totalEffect + wager.WagerAmount;
        var newBalance = await Money.ModifyBalanceAsync(gambler.Id, balanceAdjustment,
            TransactionSourceEventType.Gambling, $"Blackjack outcome from wager {wager.Id}", null, ctx);

        await botInstance.SendChatMessageAsync(
            await BlackjackDisplay.FinalResult(
                user, results, gameState.DealerHand, dealerValue,
                totalEffect, newBalance, isSplitGame, colors.Green, colors.Red),
            true, autoDeleteAfter: cleanupDelay);
    }


    private async Task ForfeitGame(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler,
        WagerDbModel wager, TimeSpan cleanupDelay, CancellationToken ctx)
    {
        wager.IsComplete = true;
        await _dbContext.SaveChangesAsync(ctx);

        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, your blackjack game timed out and you forfeited {await wager.WagerAmount.FormatKasinoCurrencyAsync()}",
            true, autoDeleteAfter: cleanupDelay);
    }
}

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