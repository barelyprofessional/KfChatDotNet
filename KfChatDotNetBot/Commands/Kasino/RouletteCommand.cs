using System.Net.Http.Headers;
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
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using StackExchange.Redis;

namespace KfChatDotNetBot.Commands.Kasino;

[KasinoCommand]
[WagerCommand]
public class RouletteCommand : ICommand
{
    private static int _nextRoundId = 1;
    
    public List<Regex> Patterns => [
        new Regex(@"^roulette (?<amount>\d+(?:\.\d+)?) (?<bet>.+)$", RegexOptions.IgnoreCase),
        new Regex(@"^rl (?<amount>\d+(?:\.\d+)?) (?<bet>.+)$", RegexOptions.IgnoreCase),
        new Regex(@"^roulette (?<action>refund|cancel)$", RegexOptions.IgnoreCase),
        new Regex(@"^rl (?<action>refund|cancel)$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!roulette <amount> <bet> - Bet types: number (0-36), red/black, odd/even, low/high, 1st12/2nd12/3rd12, col1/col2/col3";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 10,
        Window = TimeSpan.FromSeconds(30),
        Flags = RateLimitFlags.NoAutoDeleteCooldownResponse
    };

    private IDatabase? _redisDb;

    private ApplicationDbContext _dbContext = new();

    // European Roulette wheel configuration
    private static readonly HashSet<int> BlackNumbers = new() 
        { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
    
    private static readonly HashSet<int> RedNumbers = new() 
        { 2, 4, 6, 8, 10, 11, 13, 15, 17, 20, 22, 24, 26, 28, 29, 31, 33, 35 };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay,
            BuiltIn.Keys.KasinoRouletteEnabled,
            BuiltIn.Keys.KasinoRouletteCountdownDuration,
            BuiltIn.Keys.BotRedisConnectionString
        ]);
        
        // Check if roulette is enabled
        var rouletteEnabled = settings[BuiltIn.Keys.KasinoRouletteEnabled].ToBoolean();
        if (!rouletteEnabled)
        {
            var gameDisabledCleanupDelay = TimeSpan.FromMilliseconds(
                settings[BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay].ToType<int>());
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, roulette is currently disabled.", 
                true, autoDeleteAfter: gameDisabledCleanupDelay);
            return;
        }
        
        if (string.IsNullOrEmpty(settings[BuiltIn.Keys.BotRedisConnectionString].Value))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, roulette is not available at this time", true,
                autoDeleteAfter: TimeSpan.FromSeconds(15));
            return;
        }

        var redis = await ConnectionMultiplexer.ConnectAsync(settings[BuiltIn.Keys.BotRedisConnectionString].Value!);
        _redisDb = redis.GetDatabase();
        
        var countdownDuration = TimeSpan.FromSeconds(
            settings[BuiltIn.Keys.KasinoRouletteCountdownDuration].ToType<int>());
        
        // Handle actions (refund/cancel)
        if (arguments.TryGetValue("action", out var actionGroup))
        {
            var action = actionGroup.Value.ToLower();
            if (action == "refund")
            {
                await HandleRefund(botInstance, user, ctx);
                return;
            }

            if (action == "cancel")
            {
                // Check if user has admin rights
                if (user.UserRight < UserRight.TrueAndHonest)
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, you don't have permission to cancel the roulette round.",
                        true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                    return;
                }
                await HandleCancel(botInstance, user, ctx);
                return;
            }
        }

        // Handle placing a bet
        if (!arguments.TryGetValue("amount", out var amountGroup) || !arguments.TryGetValue("bet", out var betGroup))
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, invalid syntax. Use: !roulette <amount> <bet>",
                true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        await PlaceBet(botInstance, user, amountGroup.Value, betGroup.Value.Trim(), countdownDuration, ctx);
    }

    private async Task PlaceBet(ChatBot botInstance, UserDbModel user, string amountStr, string betStr,
        TimeSpan countdownDuration, CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var wager = Convert.ToDecimal(amountStr);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        
        // Check if user has enough balance
        if (gambler.Balance < wager)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this wager.",
                true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }
        
        if (wager == 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you have to wager more than {await wager.FormatKasinoCurrencyAsync()}", true,
                autoDeleteAfter: TimeSpan.FromSeconds(10));
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        // Parse and validate bet
        var betInfo = ParseBet(betStr);
        if (betInfo == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, invalid bet. Valid bets: 0-36, red/black, odd/even, low/high, 1st12/2nd12/3rd12, col1/col2/col3",
                true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            RateLimitService.RemoveMostRecentEntry(user, this);
            return;
        }

        int roundId;
        bool isFirstBet = false;
        var activeRound = await GetRound();
        
        // Check if there's an active round
        if (activeRound == null)
        {
            // Start a new round
            isFirstBet = true;
            activeRound = new RouletteRound
            {
                RoundId = _nextRoundId++,
                StartTime = DateTimeOffset.UtcNow,
                Bets = []
            };
            await SaveRound(activeRound);
        }
        roundId = activeRound.RoundId;

        // Create incomplete wager
        var gameMeta = new RouletteWagerMetaModel
        {
            RoundId = roundId,
            BetType = betInfo.Value.BetType,
            BetValue = betInfo.Value.BetValue
        };

        await Money.NewWagerAsync(
            gambler.Id,
            wager,
            -wager,
            WagerGame.Roulette,
            autoModifyBalance: true,
            gameMeta: gameMeta,
            isComplete: false,
            ct: ctx
        );

        var newWager = await _dbContext.Wagers
            .OrderBy(x => x.Id)
            .LastOrDefaultAsync(w => w.Gambler.Id == gambler.Id && 
                                      w.Game == WagerGame.Roulette && 
                                      !w.IsComplete,
                cancellationToken: ctx);

        if (newWager == null)
        {
            throw new InvalidOperationException("Failed to create roulette wager");
        }

        // Add bet to active round
        if (activeRound.RoundId == roundId)
        {
            activeRound.Bets.Add(new RouletteBetInfo
            {
                WagerId = newWager.Id,
                GamblerId = gambler.Id,
                Username = user.KfUsername,
                Amount = wager,
                BetType = betInfo.Value.BetType,
                BetValue = betInfo.Value.BetValue
            });
        }

        await SaveRound(activeRound);

        logger.Info($"User {user.KfUsername} placed roulette bet: {wager} on {betInfo.Value.BetType} {betInfo.Value.BetValue}");
        
        // If this is the first bet, start the countdown
        if (isFirstBet)
        {
            _ = Task.Run(async () => await RunCountdown(botInstance, countdownDuration), CancellationToken.None);
            return;
        }

        await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, your bet has been accepted", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
    }

    private async Task RunCountdown(ChatBot botInstance, TimeSpan countdownDuration)
    {
        var logger = LogManager.GetCurrentClassLogger();
        
        try
        {
            var endTime = DateTimeOffset.UtcNow.Add(countdownDuration);
            
            // Send initial countdown message
            var initialMessage = await FormatCountdownMessage(endTime);
            var countdownMessage = await botInstance.SendChatMessageAsync(initialMessage, true);
            var activeRound = await GetRound();
            
            if (activeRound != null)
            {
                activeRound.CountdownMessageId = countdownMessage.ChatMessageId;
                await SaveRound(activeRound);
            }

            // Wait until message is fully sent
            logger.Debug("Waiting for countdown message to be sent...");
            var success = await botInstance.WaitForChatMessageAsync(countdownMessage, TimeSpan.FromSeconds(30));

            if (!success)
            {
                logger.Error("Ran out of patience for this shitty chat");
                return;
            }

            logger.Info($"Countdown started, will end at {endTime:HH:mm:ss}");

            // Update countdown every second for real-time display
            var lastUpdate = DateTimeOffset.UtcNow;
            while (DateTimeOffset.UtcNow < endTime)
            {
                var remaining = endTime - DateTimeOffset.UtcNow;
                if (remaining.TotalSeconds <= 0) break;
                
                // Wait 1 second between updates
                await Task.Delay(TimeSpan.FromSeconds(1));
                
                try
                {
                    var updatedMessage = await FormatCountdownMessage(endTime);
                    await botInstance.KfClient.EditMessageAsync(countdownMessage.ChatMessageId!.Value, updatedMessage);
                    
                    var timeSinceLastUpdate = DateTimeOffset.UtcNow - lastUpdate;
                    logger.Debug($"Countdown updated (elapsed: {timeSinceLastUpdate.TotalSeconds:F1}s, remaining: {remaining.TotalSeconds:F0}s)");
                    lastUpdate = DateTimeOffset.UtcNow;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error updating countdown message, will retry on next interval");
                }

                var isCanceled = await CountdownCanceled();
                if (!isCanceled) continue;
                // Reset flag
                await SetCountdownState(false);
                throw new TaskCanceledException();
            }

            logger.Info("Countdown complete, spinning wheel...");

            // Countdown complete, spin the wheel
            await SpinWheel(botInstance);
        }
        catch (TaskCanceledException)
        {
            logger.Info("Roulette countdown cancelled");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error in roulette countdown");
        }
    }

    private async Task<string> FormatCountdownMessage(DateTimeOffset endTime)
    {
        var remaining = endTime - DateTimeOffset.UtcNow;
        if (remaining.TotalSeconds < 0) remaining = TimeSpan.Zero;

        var minutes = (int)remaining.TotalMinutes;
        var seconds = remaining.Seconds;

        var message = $"🎰 [B]ROULETTE ROUND STARTING[/B] 🎰[br]" +
                      $"⏱️ Time remaining: [B]{minutes:00}:{seconds:00}[/B][br][br]";
        var activeRound = await GetRound();

        if (activeRound != null && activeRound.Bets.Count > 0)
        {
            message += "[B]Current Bets:[/B][br]";
            
            // Group bets by user
            var betsByUser = activeRound.Bets
                .GroupBy(b => b.Username)
                .OrderBy(g => g.Key);

            foreach (var userGroup in betsByUser)
            {
                message += $"[B]{userGroup.Key}:[/B] ";
                var userBets = userGroup.Select(b => $"{b.Amount:F2} on {FormatBetDisplay(b.BetType, b.BetValue)}");
                message += string.Join(", ", userBets) + "[br]";
            }
            
            message += $"[br][B]Total bets:[/B] {activeRound.Bets.Count}";
            return message;
        }

        message += "[I]No bets placed yet. Use !roulette <amount> <bet> to join![/I]";
        return message;
    }

    private string FormatBetDisplay(string betType, string betValue)
    {
        // All bet types display the same way - just return the value
        return betValue;
    }

    private async Task SpinWheel(ChatBot botInstance)
    {
        var logger = LogManager.GetCurrentClassLogger();
        RouletteRound? round = await GetRound();
        // Delete round persistent data
        await DeleteRound();

        if (round == null || round.Bets.Count == 0)
        {
            logger.Info("No bets placed in roulette round, ending");
            return;
        }

        try
        {
            // Generate winning number using first gambler's seed
            var firstGambler = await _dbContext.Gamblers
                .FirstOrDefaultAsync(g => g.Id == round.Bets[0].GamblerId);
            
            if (firstGambler == null)
            {
                throw new InvalidOperationException("Could not find first gambler for roulette round");
            }
            
            var winningNumber = Money.GetRandomNumber(firstGambler, 0, 36);
            logger.Info($"Roulette round {round.RoundId} winning number: {winningNumber}");

            // Generate animation
            logger.Info($"Generating roulette animation for round {round.RoundId}");
            var (animationDuration, animationBytes) = RouletteAnimationGenerator.GenerateAnimation(winningNumber);
            logger.Info($"Animation generated: {animationBytes.Length} bytes, duration: {animationDuration}s");

            // Upload animation to Zipline
            logger.Info("Uploading animation to Zipline");
            using var animationStream = new MemoryStream(animationBytes);
            var animationUrl = await Zipline.Upload(
                animationStream, 
                new MediaTypeHeaderValue("image/webp"), 
                expiration: "1h");

            if (string.IsNullOrEmpty(animationUrl))
            {
                throw new InvalidOperationException("Failed to upload animation to Zipline");
            }

            logger.Info($"Animation uploaded: {animationUrl}");

            // Update countdown message to show it's spinning
            if (round.CountdownMessageId.HasValue)
            {
                var spinningMessage = $"🎰 [B]SPINNING THE WHEEL...[/B] 🎰[br][br]" +
                                     "Watch the animation below!";
                await botInstance.KfClient.EditMessageAsync(
                    round.CountdownMessageId.Value, 
                    spinningMessage);
            }

            // Post animation as a new message
            var animationMessage = $"[img]{animationUrl}[/img]";
            await botInstance.SendChatMessageAsync(animationMessage, true);

            // Wait for animation duration before revealing results
            logger.Info($"Waiting {animationDuration} seconds for animation to complete");
            await Task.Delay(TimeSpan.FromSeconds(animationDuration));

            // Process all bets and show results
            await ProcessBets(botInstance, round, winningNumber);
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Error spinning roulette wheel for round {round.RoundId}");
            
            // Cancel the round and refund all bets
            await CancelRoundDueToError(botInstance, round, ex.Message);
        }
    }

    private async Task CancelRoundDueToError(ChatBot botInstance, RouletteRound round, string errorMessage)
    {
        var logger = LogManager.GetCurrentClassLogger();
        logger.Error($"Cancelling roulette round {round.RoundId} due to error: {errorMessage}");

        // Refund all bets
        decimal totalRefunded = 0;
        foreach (var bet in round.Bets)
        {
            try
            {
                var wager = await _dbContext.Wagers
                    .Include(w => w.Gambler)
                    .FirstOrDefaultAsync(w => w.Id == bet.WagerId);

                if (wager != null)
                {
                    wager.IsComplete = true;
                    wager.WagerEffect = 0;
                    wager.Multiplier = 1;
                    await _dbContext.SaveChangesAsync();

                    await Money.ModifyBalanceAsync(
                        wager.Gambler.Id,
                        wager.WagerAmount,
                        TransactionSourceEventType.Gambling,
                        $"Roulette round {round.RoundId} cancelled due to error, wager {wager.Id} refunded");

                    totalRefunded += wager.WagerAmount;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error refunding bet {bet.WagerId} during error cancellation");
            }
        }


        var cancelMessage = $"🎰 [B]ROULETTE ROUND ERROR[/B] 🎰[br]" +
                           $"Round {round.RoundId} has been cancelled due to a technical error.[br]" +
                           $"All {round.Bets.Count} bet(s) have been refunded (total: {await totalRefunded.FormatKasinoCurrencyAsync()}).[br]" +
                           $"Please try again.";

        if (round.CountdownMessageId.HasValue)
        {
            await botInstance.KfClient.EditMessageAsync(
                round.CountdownMessageId.Value,
                cancelMessage);
        }
        else
        {
            await botInstance.SendChatMessageAsync(cancelMessage, true);
        }
    }

    private async Task ProcessBets(ChatBot botInstance, RouletteRound round, int winningNumber)
    {
        var logger = LogManager.GetCurrentClassLogger();
        var colors = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KiwiFarmsGreenColor, 
            BuiltIn.Keys.KiwiFarmsRedColor
        ]);

        var resultMessage = $"🎰 [B]ROULETTE RESULTS - Round {round.RoundId}[/B] 🎰[br][br]";
        resultMessage += $"[B]Winning Number: {winningNumber}[/B] ({GetNumberColor(winningNumber)})[br][br]";

        var winnersByUser = new Dictionary<string, (decimal netWin, List<string> winningBets)>();

        foreach (var bet in round.Bets)
        {
            try
            {
                var wager = await _dbContext.Wagers
                    .Include(w => w.Gambler)
                    .FirstOrDefaultAsync(w => w.Id == bet.WagerId);

                if (wager == null)
                {
                    logger.Error($"Could not find wager {bet.WagerId}");
                    continue;
                }

                var isWin = CheckWin(bet.BetType, bet.BetValue, winningNumber);
                var payout = isWin ? CalculatePayout(bet.BetType, bet.Amount) : 0;
                var effect = payout - bet.Amount; // Net win/loss

                // Update wager
                wager.IsComplete = true;
                wager.WagerEffect = effect;
                wager.Multiplier = payout / bet.Amount;

                await _dbContext.SaveChangesAsync();

                // Update balance
                var balanceAdjustment = payout;
                await Money.ModifyBalanceAsync(
                    wager.Gambler.Id, 
                    balanceAdjustment, 
                    TransactionSourceEventType.Gambling,
                    $"Roulette outcome from wager {wager.Id}");

                // Track results by user
                if (!winnersByUser.ContainsKey(bet.Username))
                {
                    winnersByUser[bet.Username] = (0, new List<string>());
                }
                
                var userData = winnersByUser[bet.Username];
                userData.netWin += effect;
                
                if (isWin)
                {
                    userData.winningBets.Add($"{FormatBetDisplay(bet.BetType, bet.BetValue)} (+{await effect.FormatKasinoCurrencyAsync()})");
                }
                
                winnersByUser[bet.Username] = userData;

                logger.Info($"Processed bet {bet.WagerId}: {bet.Username} bet {bet.Amount} on {bet.BetType} {bet.BetValue}, " +
                           $"win: {isWin}, payout: {payout}, effect: {effect}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error processing bet {bet.WagerId}");
            }
        }

        // Format results
        resultMessage += "[B]Results:[/B][br]";
        foreach (var (username, (netWin, winningBets)) in winnersByUser.OrderByDescending(x => x.Value.netWin))
        {
            var winColor = netWin >= 0 
                ? colors[BuiltIn.Keys.KiwiFarmsGreenColor].Value 
                : colors[BuiltIn.Keys.KiwiFarmsRedColor].Value;
            
            resultMessage += $"[B]{username}:[/B] [COLOR={winColor}]{(netWin >= 0 ? "+" : "")}{await netWin.FormatKasinoCurrencyAsync()}[/COLOR]";
            
            if (winningBets.Count > 0)
            {
                resultMessage += $" ({string.Join(", ", winningBets)})";
            }
            
            resultMessage += "[br]";
        }

        // Post results as a new message
        await botInstance.SendChatMessageAsync(resultMessage, true);
    }

    private async Task HandleRefund(ChatBot botInstance, UserDbModel user, CancellationToken ctx)
    {
        var activeRound = await GetRound();
        if (activeRound == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, there's no active roulette round.",
                true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }

        // Find all incomplete roulette wagers for this user
        var userWagers = await _dbContext.Wagers
            .Where(w => w.Gambler.Id == gambler.Id && 
                       w.Game == WagerGame.Roulette && 
                       !w.IsComplete)
            .ToListAsync(cancellationToken: ctx);

        if (userWagers.Count == 0)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you don't have any active roulette bets to refund.",
                true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        decimal totalRefund = 0;
        
        foreach (var wager in userWagers)
        {
            wager.IsComplete = true;
            wager.WagerEffect = 0; // No loss
            wager.Multiplier = 1; // Break even
            totalRefund += wager.WagerAmount;
            await _dbContext.SaveChangesAsync(ctx);

            // Refund the wager amount
            await Money.ModifyBalanceAsync(
                gambler.Id,
                wager.WagerAmount,
                TransactionSourceEventType.Gambling,
                $"Roulette bet refund for wager {wager.Id}",
                ct: ctx);
        }


        // Remove bets from active round
        activeRound.Bets.RemoveAll(b => b.GamblerId == gambler.Id);
        await SaveRound(activeRound);

        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, refunded {userWagers.Count} bet(s) totaling {await totalRefund.FormatKasinoCurrencyAsync()}",
            true, autoDeleteAfter: TimeSpan.FromSeconds(15));
    }

    private async Task HandleCancel(ChatBot botInstance, UserDbModel user, CancellationToken ctx)
    {
        var round = await GetRound();

        if (round == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, there's no active roulette round to cancel.",
                true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        await DeleteRound();


        // Cancel countdown
        await SetCountdownState(true);

        // Refund all bets
        decimal totalRefunded = 0;
        foreach (var bet in round.Bets)
        {
            try
            {
                var wager = await _dbContext.Wagers
                    .Include(w => w.Gambler)
                    .FirstOrDefaultAsync(w => w.Id == bet.WagerId, cancellationToken: ctx);

                if (wager != null)
                {
                    wager.IsComplete = true;
                    wager.WagerEffect = 0;
                    wager.Multiplier = 1;
                    await _dbContext.SaveChangesAsync(ctx);

                    await Money.ModifyBalanceAsync(
                        wager.Gambler.Id,
                        wager.WagerAmount,
                        TransactionSourceEventType.Gambling,
                        $"Roulette round {round.RoundId} cancelled, wager {wager.Id} refunded",
                        ct: ctx);

                    totalRefunded += wager.WagerAmount;
                }
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Error(ex, $"Error refunding bet {bet.WagerId}");
            }
        }


        var cancelMessage = $"🎰 [B]ROULETTE ROUND CANCELLED[/B] 🎰[br]" +
                           $"Cancelled by {user.FormatUsername()}[br]" +
                           $"Refunded {round.Bets.Count} bet(s) totaling {await totalRefunded.FormatKasinoCurrencyAsync()}";

        if (round.CountdownMessageId.HasValue)
        {
            await botInstance.KfClient.EditMessageAsync(
                round.CountdownMessageId.Value,
                cancelMessage);
        }
        else
        {
            await botInstance.SendChatMessageAsync(cancelMessage, true);
        }
    }

    private (string BetType, string BetValue)? ParseBet(string betStr)
    {
        betStr = betStr.ToLower().Trim();

        // Try to parse as number (0-36)
        if (int.TryParse(betStr, out var number) && number >= 0 && number <= 36)
        {
            return ("number", number.ToString());
        }

        // Color bets
        if (betStr is "red" or "r")
            return ("color", "RED");
        if (betStr is "black" or "b")
            return ("color", "BLACK");

        // Odd/Even
        if (betStr is "odd" or "o")
            return ("oddeven", "ODD");
        if (betStr is "even" or "e")
            return ("oddeven", "EVEN");

        // Low/High
        if (betStr is "low" or "1-18")
            return ("lowhigh", "LOW");
        if (betStr is "high" or "19-36")
            return ("lowhigh", "HIGH");

        // Dozens
        if (betStr is "1st12" or "d1" or "1-12")
            return ("dozen", "1ST");
        if (betStr is "2nd12" or "d2" or "13-24")
            return ("dozen", "2ND");
        if (betStr is "3rd12" or "d3" or "25-36")
            return ("dozen", "3RD");

        // Columns
        if (betStr is "col1" or "c1")
            return ("column", "COL1");
        if (betStr is "col2" or "c2")
            return ("column", "COL2");
        if (betStr is "col3" or "c3")
            return ("column", "COL3");

        return null;
    }

    private bool CheckWin(string betType, string betValue, int winningNumber)
    {
        return betType switch
        {
            "number" => int.Parse(betValue) == winningNumber,
            "color" => CheckColorWin(betValue, winningNumber),
            "oddeven" => CheckOddEvenWin(betValue, winningNumber),
            "lowhigh" => CheckLowHighWin(betValue, winningNumber),
            "dozen" => CheckDozenWin(betValue, winningNumber),
            "column" => CheckColumnWin(betValue, winningNumber),
            _ => false
        };
    }

    private bool CheckColorWin(string color, int number)
    {
        if (number == 0) return false;
        return color == "RED" ? RedNumbers.Contains(number) : BlackNumbers.Contains(number);
    }

    private bool CheckOddEvenWin(string oddEven, int number)
    {
        if (number == 0) return false;
        return oddEven == "ODD" ? number % 2 == 1 : number % 2 == 0;
    }

    private bool CheckLowHighWin(string lowHigh, int number)
    {
        if (number == 0) return false;
        return lowHigh == "LOW" ? number <= 18 : number >= 19;
    }

    private bool CheckDozenWin(string dozen, int number)
    {
        if (number == 0) return false;
        return dozen switch
        {
            "1ST" => number >= 1 && number <= 12,
            "2ND" => number >= 13 && number <= 24,
            "3RD" => number >= 25 && number <= 36,
            _ => false
        };
    }

    private bool CheckColumnWin(string column, int number)
    {
        if (number == 0) return false;
        return column switch
        {
            "COL1" => number % 3 == 1,
            "COL2" => number % 3 == 2,
            "COL3" => number % 3 == 0,
            _ => false
        };
    }

    private decimal CalculatePayout(string betType, decimal wagerAmount)
    {
        var multiplier = betType switch
        {
            "number" => 35m,      // 35:1
            "color" => 1m,        // 1:1
            "oddeven" => 1m,      // 1:1
            "lowhigh" => 1m,      // 1:1
            "dozen" => 2m,        // 2:1
            "column" => 2m,       // 2:1
            _ => 0m
        };

        return wagerAmount * (multiplier + 1); // Return original wager + winnings
    }

    private string GetNumberColor(int number)
    {
        if (number == 0) return "GREEN";
        if (RedNumbers.Contains(number)) return "RED";
        if (BlackNumbers.Contains(number)) return "BLACK";
        return "???";
    }

    private async Task<RouletteRound?> GetRound()
    {
        if (_redisDb == null) throw new InvalidOperationException("Redis service isn't initialized");
        var json = await _redisDb.StringGetAsync("Roulette.State");
        if (string.IsNullOrEmpty(json)) return null;
        var data = JsonSerializer.Deserialize<RouletteRound>(json.ToString());
        return data;
    }

    private async Task DeleteRound()
    {
        if (_redisDb == null) throw new InvalidOperationException("Redis service isn't initialized");
        await _redisDb.KeyDeleteAsync("Roulette.State");
    }

    private async Task SaveRound(RouletteRound data)
    {
        if (_redisDb == null) throw new InvalidOperationException("Redis service isn't initialized");
        var json = JsonSerializer.Serialize(data);
        await _redisDb.StringSetAsync("Roulette.State", json, null, When.Always);
    }

    private async Task<bool> CountdownCanceled()
    {
        if (_redisDb == null) throw new InvalidOperationException("Redis service isn't initialized");
        return await _redisDb.StringGetBitAsync("Roulette.Cancel", 0);
    }

    private async Task SetCountdownState(bool canceled)
    {
        if (_redisDb == null) throw new InvalidOperationException("Redis service isn't initialized");
        await _redisDb.StringSetBitAsync("Roulette.Cancel", 0, canceled);
    }

    private class RouletteRound
    {
        public required int RoundId { get; init; }
        public required DateTimeOffset StartTime { get; init; }
        public int? CountdownMessageId { get; set; }
        public required List<RouletteBetInfo> Bets { get; init; }
    }

    private class RouletteBetInfo
    {
        public required int WagerId { get; init; }
        public required int GamblerId { get; init; }
        public required string Username { get; init; }
        public required decimal Amount { get; init; }
        public required string BetType { get; init; }
        public required string BetValue { get; init; }
    }
}

public class RouletteWagerMetaModel
{
    public int RoundId { get; set; }
    public string BetType { get; set; } = "";
    public string BetValue { get; set; } = "";
}

public static class RouletteAnimationGenerator
{
    // European Wheel Sequence
    private static readonly int[] WheelNumbers = {
        0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26
    };

    /// <summary>
    /// Generates an animated roulette wheel that lands on the specified winning number
    /// </summary>
    /// <param name="winningNumber">The number (0-36) that the ball should land on</param>
    /// <returns>A tuple containing the animation duration in seconds and the WebP animation bytes</returns>
    public static (int durationSeconds, byte[] animationBytes) GenerateAnimation(int winningNumber)
    {
        if (winningNumber < 0 || winningNumber > 36)
        {
            throw new ArgumentOutOfRangeException(nameof(winningNumber), "Winning number must be between 0 and 36");
        }

        using var board = DrawWheelBase();
        int fps = 20;
        int duration = Random.Shared.Next(6, 9);
        int totalFrames = fps * duration;
        using var animation = new Image<Rgba32>(500, 500);

        // Find the index of the winning number in the wheel sequence
        int winningIndex = Array.IndexOf(WheelNumbers, winningNumber);
        if (winningIndex == -1)
        {
            throw new InvalidOperationException($"Winning number {winningNumber} not found in wheel sequence");
        }

        // Set the "Journey"
        float endWheelRotation = 720f + Random.Shared.Next(0, 360);
        float sliceAngle = 360f / 37f;
        
        // The pocket index 'i' is located at (i * sliceAngle) degrees relative to wheel zero.
        // Our '0' pocket was drawn at -90 degrees.
        float pocketOffsetOnWheel = (winningIndex * sliceAngle) - 90;
        
        // This is where the ball MUST be at the end of the video
        float finalBallAngle = endWheelRotation + pocketOffsetOnWheel;

        // Render frames
        for (int i = 0; i < totalFrames; i++)
        {
            float progress = (float)i / totalFrames;
            float ease = 1f - MathF.Pow(1f - progress, 3); // Smooth stop

            // Wheel rotates Clockwise (Adding degrees)
            float currentWheelAngle = endWheelRotation * ease;
            
            // Ball rotates Counter-Clockwise (Starting high and subtracting)
            // We start with 5 extra laps (1800 degrees) and "go back" to the final angle
            float startBallAngle = finalBallAngle + 1800f;
            float currentBallAngle = startBallAngle - ((startBallAngle - finalBallAngle) * ease);

            var frame = new Image<Rgba32>(500, 500);
            frame.Mutate(ctx => {
                // Draw Wheel
                using var rotatedBoard = board.Clone(b => b.Rotate(currentWheelAngle));
                int ox = 250 - (rotatedBoard.Width / 2);
                int oy = 250 - (rotatedBoard.Height / 2);
                ctx.DrawImage(rotatedBoard, new Point(ox, oy), 1f);

                // Ball Radius (Physics)
                float dropT = MathF.Max(0, (progress - 0.7f) / 0.3f);
                float radius = 230 - (45 * MathF.Pow(dropT, 2));
                
                float rads = currentBallAngle * MathF.PI / 180;
                float bx = 250 + (radius * MathF.Cos(rads));
                float by = 250 + (radius * MathF.Sin(rads));
                
                ctx.Fill(Color.White, new EllipsePolygon(bx, by, 14));
            });

            frame.Frames.RootFrame.Metadata.GetWebpMetadata().FrameDelay = (uint)(1000 / fps);
            animation.Frames.AddFrame(frame.Frames.RootFrame);
            frame.Dispose();
        }

        animation.Frames.RemoveFrame(0);
        using var ms = new MemoryStream();
        animation.SaveAsWebp(ms, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = 50 });
        
        return (duration, ms.ToArray());
    }

    private static Image<Rgba32> DrawWheelBase()
    {
        var img = new Image<Rgba32>(500, 500);
        float centerX = 250, centerY = 250, outerRadius = 245, innerRadius = 170, step = 360f / 37f;
        
        img.Mutate(ctx => {
            for (int i = 0; i < 37; i++) {
                float startAngle = i * step - (step / 2) - 90;
                var color = WheelNumbers[i] == 0 ? Color.Green : (i % 2 == 0 ? Color.DarkRed : Color.Black);
                var path = new PathBuilder().AddArc(centerX, centerY, outerRadius, outerRadius, 0, startAngle, step)
                    .AddArc(centerX, centerY, innerRadius, innerRadius, 0, startAngle + step, -step).Build();
                ctx.Fill(color, path);
                ctx.Draw(Color.Gold, 1, path);
                
                string text = WheelNumbers[i].ToString();
                float textAngle = (startAngle + (step / 2)) * MathF.PI / 180;
                float tx = centerX + ((outerRadius + innerRadius) / 2) * MathF.Cos(textAngle);
                float ty = centerY + ((outerRadius + innerRadius) / 2) * MathF.Sin(textAngle);
                
                try {
                    var font = SystemFonts.CreateFont("Arial", 14, FontStyle.Bold);
                    ctx.DrawText(
                        new DrawingOptions { 
                            Transform = Matrix3x2Extensions.CreateRotationDegrees(startAngle + (step / 2) + 90, new PointF(tx, ty)) 
                        }, 
                        text, 
                        font, 
                        Color.White, 
                        new PointF(tx - 6, ty - 9));
                } catch { 
                    // Font loading failed, skip text rendering
                }
            }
            ctx.Fill(Color.DarkSlateGray, new EllipsePolygon(centerX, centerY, innerRadius - 5));
        });
        
        return img;
    }
}