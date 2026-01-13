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
public class RouletteCommand : ICommand
{
    private static RouletteRound? _activeRound = null;
    private static readonly object _roundLock = new object();
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

    private ApplicationDbContext _dbContext = new();

    // European Roulette wheel configuration
    private static readonly HashSet<int> RedNumbers = new() 
        { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
    
    private static readonly HashSet<int> BlackNumbers = new() 
        { 2, 4, 6, 8, 10, 11, 13, 15, 17, 20, 22, 24, 26, 28, 29, 31, 33, 35 };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.KasinoGameDisabledMessageCleanupDelay,
            BuiltIn.Keys.KasinoRouletteEnabled,
            BuiltIn.Keys.KasinoRouletteCountdownDuration
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
            else if (action == "cancel")
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
            return;
        }

        // Parse and validate bet
        var betInfo = ParseBet(betStr);
        if (betInfo == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, invalid bet. Valid bets: 0-36, red/black, odd/even, low/high, 1st12/2nd12/3rd12, col1/col2/col3",
                true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        int roundId;
        bool isFirstBet = false;
        
        lock (_roundLock)
        {
            // Check if there's an active round
            if (_activeRound == null)
            {
                // Start a new round
                isFirstBet = true;
                _activeRound = new RouletteRound
                {
                    RoundId = _nextRoundId++,
                    StartTime = DateTimeOffset.UtcNow,
                    Bets = new List<RouletteBetInfo>(),
                    CancellationTokenSource = new CancellationTokenSource()
                };
            }
            roundId = _activeRound.RoundId;
        }

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
        lock (_roundLock)
        {
            if (_activeRound?.RoundId == roundId)
            {
                _activeRound.Bets.Add(new RouletteBetInfo
                {
                    WagerId = newWager.Id,
                    GamblerId = gambler.Id,
                    Username = user.KfUsername,
                    Amount = wager,
                    BetType = betInfo.Value.BetType,
                    BetValue = betInfo.Value.BetValue
                });
            }
        }

        logger.Info($"User {user.KfUsername} placed roulette bet: {wager} on {betInfo.Value.BetType} {betInfo.Value.BetValue}");
        
        // If this is the first bet, start the countdown
        if (isFirstBet)
        {
            _ = Task.Run(async () => await RunCountdown(botInstance, countdownDuration, _activeRound.CancellationTokenSource.Token));
        }
    }

    private async Task RunCountdown(ChatBot botInstance, TimeSpan countdownDuration, CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        
        try
        {
            var endTime = DateTimeOffset.UtcNow.Add(countdownDuration);
            
            // Send initial countdown message
            var initialMessage = FormatCountdownMessage(endTime);
            var countdownMessage = await botInstance.SendChatMessageAsync(initialMessage, true);
            
            lock (_roundLock)
            {
                if (_activeRound != null)
                {
                    _activeRound.CountdownMessageId = countdownMessage.ChatMessageId;
                }
            }

            // Wait until message is fully sent
            while (countdownMessage.Status != SentMessageTrackerStatus.ResponseReceived && !ctx.IsCancellationRequested)
            {
                await Task.Delay(100, ctx);
            }

            // Update countdown every 5 seconds
            while (DateTimeOffset.UtcNow < endTime && !ctx.IsCancellationRequested)
            {
                var remaining = endTime - DateTimeOffset.UtcNow;
                if (remaining.TotalSeconds <= 0) break;
                
                await Task.Delay(TimeSpan.FromSeconds(5), ctx);
                
                if (countdownMessage.ChatMessageId.HasValue)
                {
                    var updatedMessage = FormatCountdownMessage(endTime);
                    await botInstance.KfClient.EditMessageAsync(countdownMessage.ChatMessageId.Value, updatedMessage);
                }
            }

            if (ctx.IsCancellationRequested)
            {
                return;
            }

            // Countdown complete, spin the wheel
            await SpinWheel(botInstance, ctx);
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

    private string FormatCountdownMessage(DateTimeOffset endTime)
    {
        var remaining = endTime - DateTimeOffset.UtcNow;
        if (remaining.TotalSeconds < 0) remaining = TimeSpan.Zero;

        var minutes = (int)remaining.TotalMinutes;
        var seconds = remaining.Seconds;

        var message = $"🎰 [B]ROULETTE ROUND STARTING[/B] 🎰[br]" +
                      $"⏱️ Time remaining: [B]{minutes:00}:{seconds:00}[/B][br][br]";

        lock (_roundLock)
        {
            if (_activeRound != null && _activeRound.Bets.Count > 0)
            {
                message += "[B]Current Bets:[/B][br]";
                
                // Group bets by user
                var betsByUser = _activeRound.Bets
                    .GroupBy(b => b.Username)
                    .OrderBy(g => g.Key);

                foreach (var userGroup in betsByUser)
                {
                    message += $"[B]{userGroup.Key}:[/B] ";
                    var userBets = userGroup.Select(b => $"{b.Amount:F2} on {FormatBetDisplay(b.BetType, b.BetValue)}");
                    message += string.Join(", ", userBets) + "[br]";
                }
                
                message += $"[br][B]Total bets:[/B] {_activeRound.Bets.Count}";
            }
            else
            {
                message += "[I]No bets placed yet. Use !roulette <amount> <bet> to join![/I]";
            }
        }

        return message;
    }

    private string FormatBetDisplay(string betType, string betValue)
    {
        return betType switch
        {
            "number" => betValue,
            "color" => betValue,
            "oddeven" => betValue,
            "lowhigh" => betValue,
            "dozen" => betValue,
            "column" => betValue,
            _ => betValue
        };
    }

    private async Task SpinWheel(ChatBot botInstance, CancellationToken ctx)
    {
        var logger = LogManager.GetCurrentClassLogger();
        RouletteRound? round;
        
        lock (_roundLock)
        {
            round = _activeRound;
            _activeRound = null; // Clear active round
        }

        if (round == null || round.Bets.Count == 0)
        {
            logger.Info("No bets placed in roulette round, ending");
            return;
        }

        try
        {
            // TODO: Replace with actual animation API call when ready
            // var animationUrl = await GetRouletteAnimationUrl();
            // var winningNumber = await GetWinningNumberFromAnimation(animationUrl);
            
            // For now, generate random winning number using first gambler's seed
            var firstGambler = await _dbContext.Gamblers
                .FirstOrDefaultAsync(g => g.Id == round.Bets[0].GamblerId, cancellationToken: ctx);
            
            var winningNumber = Money.GetRandomNumber(firstGambler!, 0, 36);
            
            logger.Info($"Roulette round {round.RoundId} result: {winningNumber}");

            // Update countdown message with result
            if (round.CountdownMessageId.HasValue)
            {
                // TODO: Uncomment when animation API is ready
                // var resultMessage = $"[img]{animationUrl}[/img][br][br]";
                var resultMessage = $"🎰 [B]ROULETTE RESULT[/B] 🎰[br][br]";
                resultMessage += $"[B]Winning Number: {winningNumber}[/B] ({GetNumberColor(winningNumber)})[br][br]";
                resultMessage += "Calculating payouts...";
                
                await botInstance.KfClient.EditMessageAsync(
                    round.CountdownMessageId.Value, 
                    resultMessage);
            }

            // Small delay for dramatic effect
            await Task.Delay(2000, ctx);

            // Process all bets
            await ProcessBets(botInstance, round, winningNumber, ctx);
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Error spinning roulette wheel for round {round.RoundId}");
        }
    }

    private async Task ProcessBets(ChatBot botInstance, RouletteRound round, int winningNumber, CancellationToken ctx)
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
                    .FirstOrDefaultAsync(w => w.Id == bet.WagerId, cancellationToken: ctx);

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

                await _dbContext.SaveChangesAsync(ctx);

                // Update balance
                var balanceAdjustment = payout;
                await Money.ModifyBalanceAsync(
                    wager.Gambler.Id, 
                    balanceAdjustment, 
                    TransactionSourceEventType.Gambling,
                    $"Roulette outcome from wager {wager.Id}",
                    ct: ctx);

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

        // Update countdown message with final results
        if (round.CountdownMessageId.HasValue)
        {
            await botInstance.KfClient.EditMessageAsync(
                round.CountdownMessageId.Value,
                resultMessage);
        }
        else
        {
            await botInstance.SendChatMessageAsync(resultMessage, true);
        }
    }

    private async Task HandleRefund(ChatBot botInstance, UserDbModel user, CancellationToken ctx)
    {
        lock (_roundLock)
        {
            if (_activeRound == null)
            {
                _ = botInstance.SendChatMessageAsync(
                    $"{user.FormatUsername()}, there's no active roulette round.",
                    true, autoDeleteAfter: TimeSpan.FromSeconds(10));
                return;
            }
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

            // Refund the wager amount
            await Money.ModifyBalanceAsync(
                gambler.Id,
                wager.WagerAmount,
                TransactionSourceEventType.Gambling,
                $"Roulette bet refund for wager {wager.Id}",
                ct: ctx);
        }

        await _dbContext.SaveChangesAsync(ctx);

        // Remove bets from active round
        lock (_roundLock)
        {
            if (_activeRound != null)
            {
                _activeRound.Bets.RemoveAll(b => b.GamblerId == gambler.Id);
            }
        }

        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()}, refunded {userWagers.Count} bet(s) totaling {await totalRefund.FormatKasinoCurrencyAsync()}",
            true, autoDeleteAfter: TimeSpan.FromSeconds(15));
    }

    private async Task HandleCancel(ChatBot botInstance, UserDbModel user, CancellationToken ctx)
    {
        RouletteRound? round;
        
        lock (_roundLock)
        {
            round = _activeRound;
            _activeRound = null;
        }

        if (round == null)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, there's no active roulette round to cancel.",
                true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }

        // Cancel countdown
        round.CancellationTokenSource?.Cancel();

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

        await _dbContext.SaveChangesAsync(ctx);

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
        return RedNumbers.Contains(number) ? "RED" : "BLACK";
    }

    private class RouletteRound
    {
        public int RoundId { get; set; }
        public DateTimeOffset StartTime { get; set; }
        public int? CountdownMessageId { get; set; }
        public List<RouletteBetInfo> Bets { get; set; } = new();
        public CancellationTokenSource? CancellationTokenSource { get; set; }
    }

    private class RouletteBetInfo
    {
        public int WagerId { get; set; }
        public int GamblerId { get; set; }
        public string Username { get; set; } = "";
        public decimal Amount { get; set; }
        public string BetType { get; set; } = "";
        public string BetValue { get; set; } = "";
    }
}

public class RouletteWagerMetaModel
{
    public int RoundId { get; set; }
    public string BetType { get; set; } = "";
    public string BetValue { get; set; } = "";
}