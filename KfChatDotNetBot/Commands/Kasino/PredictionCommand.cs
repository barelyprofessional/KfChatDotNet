using System.Text.Json;
using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;
using NLog;
using StackExchange.Redis;

namespace KfChatDotNetBot.Commands.Kasino;

/// <summary>
/// Helper class for prediction name matching
/// </summary>
public static class PredictionHelper
{
    /// <summary>
    /// Find a prediction by fuzzy matching on the description
    /// </summary>
    public static async Task<(string? predictionId, PredictionData? prediction)> FindPredictionByName(
        IDatabase redisDb, string searchTerm, RedisValue[] activePredictionIds)
    {
        if (activePredictionIds.Length == 0)
            return (null, null);

        // If only one active prediction, return it
        if (activePredictionIds.Length == 1)
        {
            var singleId = activePredictionIds[0].ToString();
            var json = await redisDb.StringGetAsync($"prediction:{singleId}");
            if (json.IsNullOrEmpty) return (null, null);
            var pred = JsonSerializer.Deserialize<PredictionData>(json!.ToString());
            return (singleId, pred);
        }

        // Load all active predictions
        var predictions = new List<(string id, PredictionData data)>();
        foreach (var id in activePredictionIds)
        {
            var json = await redisDb.StringGetAsync($"prediction:{id}");
            if (!json.IsNullOrEmpty)
            {
                var pred = JsonSerializer.Deserialize<PredictionData>(json!.ToString());
                if (pred != null && pred.State == PredictionState.Active)
                {
                    predictions.Add((id.ToString()!, pred));
                }
            }
        }

        if (predictions.Count == 0)
            return (null, null);

        // Fuzzy match - case insensitive partial match
        var searchLower = searchTerm.ToLower();
        var matches = predictions.Where(p => p.data.Description.ToLower().Contains(searchLower)).ToList();

        if (matches.Count == 0)
            return (null, null);

        if (matches.Count == 1)
            return (matches[0].id, matches[0].data);

        // Multiple matches - return the best match (shortest description containing the search term)
        var bestMatch = matches.OrderBy(m => m.data.Description.Length).First();
        return (bestMatch.id, bestMatch.data);
    }

    /// <summary>
    /// List all active predictions with their IDs and descriptions
    /// </summary>
    public static async Task<List<(string id, string description)>> ListActivePredictions(IDatabase redisDb)
    {
        var activePredictionIds = await redisDb.SetMembersAsync("predictions:active");
        var result = new List<(string id, string description)>();

        foreach (var id in activePredictionIds)
        {
            var json = await redisDb.StringGetAsync($"prediction:{id}");
            if (!json.IsNullOrEmpty)
            {
                var pred = JsonSerializer.Deserialize<PredictionData>(json!.ToString());
                if (pred != null && pred.State == PredictionState.Active)
                {
                    result.Add((id.ToString()!, pred.Description));
                }
            }
        }

        return result;
    }
}

/// <summary>
/// Command to start a new prediction
/// </summary>
[KasinoCommand]
public class PredictionStartCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^prediction start (.+)$", RegexOptions.IgnoreCase),
        new Regex(@"^pred start (.+)$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!prediction start \"description\" \"option1\" \"option2\" [\"option3\"] ... - Start a new prediction";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;

    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public bool WhisperCanInvoke => true;

    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.BotRedisConnectionString
        ]);

        if (string.IsNullOrEmpty(settings[BuiltIn.Keys.BotRedisConnectionString].Value))
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, predictions are not available at this time");
            return;
        }

        var redis = await ConnectionMultiplexer.ConnectAsync(settings[BuiltIn.Keys.BotRedisConnectionString].Value!);
        var redisDb = redis.GetDatabase();

        // Parse quoted strings from the message content
        var messageText = message.Message;
        var startMatch = Regex.Match(messageText, @"^.(?:prediction|pred)\s+start\s+(.+)$", RegexOptions.IgnoreCase);
        if (!startMatch.Success)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, invalid syntax. Use: !prediction start \"description\" \"option1\" \"option2\" [\"option3\" ...]");
            return;
        }

        var argsText = startMatch.Groups[1].Value;
        // unescape escaped "&quot; and other HTML entities that might be in the input
        argsText = System.Net.WebUtility.HtmlDecode(argsText);

        var quotedStrings = new List<string>();
        var regex = new Regex(@"""([^""]*)""");
        var matches = regex.Matches(argsText);

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                quotedStrings.Add(match.Groups[1].Value);
            }
        }

        if (quotedStrings.Count < 3)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, you need a description and at least 2 options. Use: !prediction start \"description\" \"option1\" \"option2\" [\"option3\" ...]");
            return;
        }

        var description = quotedStrings[0];
        var options = quotedStrings.Skip(1).ToList();

        if (options.Count < 2)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, you need at least 2 options for a prediction");
            return;
        }

        // Generate unique ID for the prediction
        var predictionId = Guid.NewGuid().ToString("N")[..8];

        // Create prediction object
        var prediction = new PredictionData
        {
            Id = predictionId,
            Description = description,
            Options = options.Select((opt, idx) => new PredictionOption
            {
                Index = idx + 1,
                Text = opt,
                TotalBet = 0
            }).ToList(),
            CreatedBy = user.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            State = PredictionState.Active
        };

        // Store in Redis
        await redisDb.StringSetAsync($"prediction:{predictionId}", JsonSerializer.Serialize(prediction));
        await redisDb.SetAddAsync("predictions:active", predictionId);

        // Build response message
        var optionsText = string.Join("[br]", prediction.Options.Select(o => $"  {o.Index}. {o.Text}"));

        // Check how many active predictions there are now
        var allActive = await redisDb.SetMembersAsync("predictions:active");
        var betInstructions = allActive.Length > 1
            ? $"Use !bet \"<prediction name>\" <amount> <option> to bet (partial names work!)"
            : $"Use !bet <amount> <option> to bet";

        await botInstance.SendChatMessageAsync(
            $":!: NEW PREDICTION [{predictionId}] :!:[br]{description}[br][br]Options:[br]{optionsText}[br][br]{betInstructions}",
            true);
    }
}

/// <summary>
/// Command to place a bet on a prediction
/// </summary>
[KasinoCommand]
[WagerCommand]
public class PredictionBetCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^bet (.+)$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!bet [\"prediction name\"] <amount> <option_name> - Bet on a prediction outcome (supports fuzzy matching)";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);
    public RateLimitOptionsModel? RateLimitOptions => new()
    {
        MaxInvocations = 10,
        Window = TimeSpan.FromSeconds(30)
    };

    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public bool WhisperCanInvoke => true;

    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.BotRedisConnectionString
        ]);

        if (string.IsNullOrEmpty(settings[BuiltIn.Keys.BotRedisConnectionString].Value))
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, predictions are not available at this time");
            return;
        }

        var redis = await ConnectionMultiplexer.ConnectAsync(settings[BuiltIn.Keys.BotRedisConnectionString].Value!);
        var redisDb = redis.GetDatabase();

        // Parse the bet command - supports:
        // !bet <amount> <option_name> (when 0-1 predictions active)
        // !bet "prediction name" <amount> <option_name> (when multiple active)
        // !bet prediction name <amount> <option_name> (partial name without quotes)
        var messageText = message.Message;
        var betMatch = Regex.Match(messageText, @"^.bet\s+(.+)$", RegexOptions.IgnoreCase);
        if (!betMatch.Success)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, invalid bet syntax");
            return;
        }

        var argsText = betMatch.Groups[1].Value.Trim();
        argsText = System.Net.WebUtility.HtmlDecode(argsText);
        var activePredictions = await redisDb.SetMembersAsync("predictions:active");

        if (activePredictions.Length == 0)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, there are no active predictions");
            return;
        }

        string? predictionId = null;
        PredictionData? prediction = null;
        decimal amount;
        int optionIndex;

        // Try to parse as: "prediction name" amount option_name OR amount option_name
        var quotedMatch = Regex.Match(argsText, @"^""([^""]+)""\s+(\d+(?:\.\d+)?)\s+(.+)$");
        if (quotedMatch.Success)
        {
            // Has quoted prediction name
            var searchTerm = quotedMatch.Groups[1].Value;
            amount = Convert.ToDecimal(quotedMatch.Groups[2].Value);
            var optionNamePart = quotedMatch.Groups[3].Value.Trim();

            (predictionId, prediction) = await PredictionHelper.FindPredictionByName(redisDb, searchTerm, activePredictions);

            if (predictionId == null || prediction == null)
            {
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find prediction matching '{searchTerm}'");
                return;
            }

            // Find option by name using fuzzy matching
            var optionSearchLower = optionNamePart.ToLower();
            var matchingOptions = prediction.Options
                .Where(o => o.Text.ToLower().Contains(optionSearchLower))
                .ToList();

            if (matchingOptions.Count == 0)
            {
                var optionsText = string.Join(", ", prediction.Options.Select(o => $"{o.Index}. {o.Text}"));
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find option matching '{optionNamePart}'. Options: {optionsText}");
                return;
            }

            if (matchingOptions.Count > 1)
            {
                var optionsText = string.Join(", ", matchingOptions.Select(o => $"{o.Index}. {o.Text}"));
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, multiple options match '{optionNamePart}': {optionsText}. Please be more specific.");
                return;
            }

            optionIndex = matchingOptions[0].Index;
        }
        else
        {
            // Try: unquoted prediction name amount option_name
            // Find the first number as the amount
            var amountMatch = Regex.Match(argsText, @"\d+(?:\.\d+)?");
            if (amountMatch.Success)
            {
                var predictionNamePart = argsText.Substring(0, amountMatch.Index).Trim();
                amount = Convert.ToDecimal(amountMatch.Value);
                var optionNamePart = argsText.Substring(amountMatch.Index + amountMatch.Length).Trim();

                if (string.IsNullOrWhiteSpace(optionNamePart))
                {
                    // No option name provided
                    await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, invalid bet format. Use: !bet [\"prediction name\"] <amount> <option_name>");
                    return;
                }

                // Find prediction (or use the only active one if no name given)
                if (string.IsNullOrWhiteSpace(predictionNamePart))
                {
                    if (activePredictions.Length == 1)
                    {
                        predictionId = activePredictions[0].ToString();
                        var json = await redisDb.StringGetAsync($"prediction:{predictionId}");
                        prediction = json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<PredictionData>(json!.ToString());
                    }
                    else
                    {
                        // Multiple predictions but no name specified
                        var activeList = await PredictionHelper.ListActivePredictions(redisDb);
                        var listText = string.Join("[br]", activeList.Select(p => $"  [{p.id}] {p.description}"));
                        await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, multiple predictions active. Specify which one (keywords are enough):[br]{listText}[br]Use: !bet \"<name>\" <amount> <option>");
                        return;
                    }
                }
                else
                {
                    (predictionId, prediction) = await PredictionHelper.FindPredictionByName(redisDb, predictionNamePart, activePredictions);

                    if (predictionId == null || prediction == null)
                    {
                        // Show list of active predictions
                        var activeList = await PredictionHelper.ListActivePredictions(redisDb);
                        var listText = string.Join("[br]", activeList.Select(p => $"  [{p.id}] {p.description}"));
                        await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find prediction matching '{predictionNamePart}'. Active predictions:[br]{listText}");
                        return;
                    }
                }

                // Find option by name using fuzzy matching
                if (prediction == null)
                {
                    await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, prediction data is invalid");
                    return;
                }

                var optionSearchLower = optionNamePart.ToLower();
                var matchingOptions = prediction.Options
                    .Where(o => o.Text.ToLower().Contains(optionSearchLower))
                    .ToList();

                if (matchingOptions.Count == 0)
                {
                    var optionsText = string.Join(", ", prediction.Options.Select(o => $"{o.Index}. {o.Text}"));
                    await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find option matching '{optionNamePart}'. Options: {optionsText}");
                    return;
                }

                if (matchingOptions.Count > 1)
                {
                    var optionsText = string.Join(", ", matchingOptions.Select(o => $"{o.Index}. {o.Text}"));
                    await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, multiple options match '{optionNamePart}': {optionsText}. Please be more specific.");
                    return;
                }

                optionIndex = matchingOptions[0].Index;
            }
            else
            {
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, invalid bet format. Use: !bet [\"prediction name\"] <amount> <option_name>");
                return;
            }
        }

        if (prediction == null || prediction.State != PredictionState.Active)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, this prediction is not active");
            return;
        }

        // Check if betting is closed
        if (prediction.BettingClosed)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, betting is closed for this prediction");
            return;
        }

        // Validate option
        var option = prediction.Options.FirstOrDefault(o => o.Index == optionIndex);
        if (option == null)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, invalid option. Choose from 1-{prediction.Options.Count}");
            return;
        }

        // Get gambler and check balance
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }

        if (gambler.Balance < amount)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, your balance of {await gambler.Balance.FormatKasinoCurrencyAsync()} isn't enough for this bet.");
            return;
        }

        if (amount <= 0)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, you must bet more than 0");
            return;
        }

        // Check if user already has a bet
        var existingBetJson = await redisDb.HashGetAsync($"prediction:{predictionId}:bets", user.Id.ToString());
        if (!existingBetJson.IsNullOrEmpty)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, you've already placed a bet on this prediction");
            return;
        }

        // Place the incomplete wager (deduct money but don't record win/loss yet)
        var newBalance = await Money.NewWagerAsync(gambler.Id, amount, -amount, WagerGame.Prediction,
            isComplete: false, ct: ctx);

        // Store bet in Redis
        var bet = new PredictionBet
        {
            UserId = user.Id,
            Username = user.KfUsername,
            Amount = amount,
            OptionIndex = optionIndex,
            PlacedAt = DateTimeOffset.UtcNow
        };

        await redisDb.HashSetAsync($"prediction:{predictionId}:bets", user.Id.ToString(), JsonSerializer.Serialize(bet));

        // Update option total
        option.TotalBet += amount;
        await redisDb.StringSetAsync($"prediction:{predictionId}", JsonSerializer.Serialize(prediction));

        await botInstance.SendWhisperAsync(user.KfId,
            $"{user.KfUsername}, you bet {await amount.FormatKasinoCurrencyAsync()} on option {optionIndex} ({option.Text}). " +
            $"Your new balance is {await newBalance.FormatKasinoCurrencyAsync()}");
    }
}

/// <summary>
/// Command to end a prediction and pay out winners
/// </summary>
[KasinoCommand]
public class PredictionEndCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^prediction end (.+)$", RegexOptions.IgnoreCase),
        new Regex(@"^pred end (.+)$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!prediction end [\"prediction name\"] <winning_option_name> - End a prediction and pay winners (supports fuzzy matching)";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;

    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public bool WhisperCanInvoke => true;

    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.BotRedisConnectionString
        ]);

        if (string.IsNullOrEmpty(settings[BuiltIn.Keys.BotRedisConnectionString].Value))
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, predictions are not available at this time");
            return;
        }

        var redis = await ConnectionMultiplexer.ConnectAsync(settings[BuiltIn.Keys.BotRedisConnectionString].Value!);
        var redisDb = redis.GetDatabase();

        // Parse: "prediction name" winning_option_name OR winning_option_name OR name winning_option_name
        var messageText = message.Message;
        var endMatch = Regex.Match(messageText, @"^.(?:prediction|pred)\s+end\s+(.+)$", RegexOptions.IgnoreCase);
        if (!endMatch.Success)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, invalid syntax");
            return;
        }

        var argsText = endMatch.Groups[1].Value.Trim();
        argsText = System.Net.WebUtility.HtmlDecode(argsText);
        var activePredictions = await redisDb.SetMembersAsync("predictions:active");

        if (activePredictions.Length == 0)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, there are no active predictions");
            return;
        }

        string? predictionId = null;
        PredictionData? prediction = null;
        int winningOptionIndex;

        // Try quoted format: "prediction name" winning_option_name
        var quotedMatch = Regex.Match(argsText, @"^""([^""]+)""\s+(.+)$");
        if (quotedMatch.Success)
        {
            var searchTerm = quotedMatch.Groups[1].Value;
            var optionNamePart = quotedMatch.Groups[2].Value.Trim();
            (predictionId, prediction) = await PredictionHelper.FindPredictionByName(redisDb, searchTerm, activePredictions);

            if (predictionId == null || prediction == null)
            {
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find prediction matching '{searchTerm}'");
                return;
            }

            // Find option by name using fuzzy matching
            var optionSearchLower = optionNamePart.ToLower();
            var matchingOptions = prediction.Options
                .Where(o => o.Text.ToLower().Contains(optionSearchLower))
                .ToList();

            if (matchingOptions.Count == 0)
            {
                var optionsText = string.Join(", ", prediction.Options.Select(o => $"{o.Index}. {o.Text}"));
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find option matching '{optionNamePart}'. Options: {optionsText}");
                return;
            }

            if (matchingOptions.Count > 1)
            {
                var optionsText = string.Join(", ", matchingOptions.Select(o => $"{o.Index}. {o.Text}"));
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, multiple options match '{optionNamePart}': {optionsText}. Please be more specific.");
                return;
            }

            winningOptionIndex = matchingOptions[0].Index;
        }
        else if (activePredictions.Length == 1)
        {
            // Only one prediction - entire argsText is the option name
            predictionId = activePredictions[0].ToString();
            var json = await redisDb.StringGetAsync($"prediction:{predictionId}");
            prediction = json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<PredictionData>(json!.ToString());

            if (prediction == null)
            {
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, prediction data is invalid");
                return;
            }

            // Find option by name using fuzzy matching
            var optionSearchLower = argsText.ToLower();
            var matchingOptions = prediction.Options
                .Where(o => o.Text.ToLower().Contains(optionSearchLower))
                .ToList();

            if (matchingOptions.Count == 0)
            {
                var optionsText = string.Join(", ", prediction.Options.Select(o => $"{o.Index}. {o.Text}"));
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find option matching '{argsText}'. Options: {optionsText}");
                return;
            }

            if (matchingOptions.Count > 1)
            {
                var optionsText = string.Join(", ", matchingOptions.Select(o => $"{o.Index}. {o.Text}"));
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, multiple options match '{argsText}': {optionsText}. Please be more specific.");
                return;
            }

            winningOptionIndex = matchingOptions[0].Index;
        }
        else
        {
            // Multiple predictions - try to split prediction name and option name
            // We need to find where the prediction name ends and option name begins
            // Strategy: try to find a prediction that matches a prefix of argsText
            var allActivePredictions = new List<(string id, PredictionData data)>();
            foreach (var id in activePredictions)
            {
                var json = await redisDb.StringGetAsync($"prediction:{id}");
                if (!json.IsNullOrEmpty)
                {
                    var pred = JsonSerializer.Deserialize<PredictionData>(json!.ToString());
                    if (pred != null && pred.State == PredictionState.Active)
                    {
                        allActivePredictions.Add((id.ToString()!, pred));
                    }
                }
            }

            // Try to find the longest prediction name match at the start of argsText
            var bestPredictionMatch = allActivePredictions
                .Where(p => argsText.ToLower().StartsWith(p.data.Description.ToLower()))
                .OrderByDescending(p => p.data.Description.Length)
                .FirstOrDefault();

            if (bestPredictionMatch != default)
            {
                // Found a prediction name at the start
                predictionId = bestPredictionMatch.id;
                prediction = bestPredictionMatch.data;
                var optionNamePart = argsText.Substring(bestPredictionMatch.data.Description.Length).Trim();

                if (string.IsNullOrWhiteSpace(optionNamePart))
                {
                    await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, please specify the winning option. Use: !prediction end \"{prediction.Description}\" <option_name>");
                    return;
                }

                // Find option by name using fuzzy matching
                var optionSearchLower = optionNamePart.ToLower();
                var matchingOptions = prediction.Options
                    .Where(o => o.Text.ToLower().Contains(optionSearchLower))
                    .ToList();

                if (matchingOptions.Count == 0)
                {
                    var optionsText = string.Join(", ", prediction.Options.Select(o => $"{o.Index}. {o.Text}"));
                    await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find option matching '{optionNamePart}'. Options: {optionsText}");
                    return;
                }

                if (matchingOptions.Count > 1)
                {
                    var optionsText = string.Join(", ", matchingOptions.Select(o => $"{o.Index}. {o.Text}"));
                    await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, multiple options match '{optionNamePart}': {optionsText}. Please be more specific.");
                    return;
                }

                winningOptionIndex = matchingOptions[0].Index;
            }
            else
            {
                // Couldn't determine prediction - show list
                var activeList = await PredictionHelper.ListActivePredictions(redisDb);
                var listText = string.Join("[br]", activeList.Select(p => $"  [{p.id}] {p.description}"));
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't parse command. Multiple predictions active:[br]{listText}[br]Use: !prediction end \"<name>\" <option_name>");
                return;
            }
        }

        if (predictionId == null || prediction == null)
        {
            var activeList = await PredictionHelper.ListActivePredictions(redisDb);
            var listText = string.Join("[br]", activeList.Select(p => $"  [{p.id}] {p.description}"));
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find that prediction. Active predictions:[br]{listText}");
            return;
        }

        if (prediction.State != PredictionState.Active)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, this prediction is not active");
            return;
        }
        var winningOption = prediction.Options.FirstOrDefault(o => o.Index == winningOptionIndex);
        if (winningOption == null)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, invalid winning option. Choose from 1-{prediction.Options.Count}");
            return;
        }

        // Get all bets
        var allBets = await redisDb.HashGetAllAsync($"prediction:{predictionId}:bets");
        var bets = allBets.Select(entry =>
            JsonSerializer.Deserialize<PredictionBet>(entry.Value!.ToString())).ToList();

        // Calculate total pot and winning pot
        var totalPot = bets.Sum(b => b!.Amount);
        var winningPot = bets.Where(b => b!.OptionIndex == winningOptionIndex).Sum(b => b!.Amount);

        _logger.Info($"Prediction {predictionId} ending. Total pot: {totalPot}, Winning pot: {winningPot}");

        var winners = bets.Where(b => b!.OptionIndex == winningOptionIndex).ToList();
        var winnersList = new List<string>();

        // Pay out winners
        foreach (var bet in winners)
        {
            if (bet == null) continue;

            var winShare = totalPot > 0 && winningPot > 0 ? (bet.Amount / winningPot) * totalPot : bet.Amount;
            var profit = winShare - bet.Amount;

            _logger.Info($"User {bet.Username} won {winShare:N} (profit: {profit:N}) from bet of {bet.Amount:N}");

            // Get the gambler entity to update their balance
            var gambler = await Money.GetGamblerEntityAsync(bet.UserId, ct: ctx);
            if (gambler == null)
            {
                _logger.Error($"Could not find gambler for user ID {bet.UserId} ({bet.Username}) when paying out prediction");
                continue;
            }

            // Update balance with the complete wager
            var newBalance = await Money.ModifyBalanceAsync(gambler.Id, winShare,
                TransactionSourceEventType.Gambling,
                $"Prediction win: {prediction.Description}", ct: ctx);

            winnersList.Add($"@{bet.Username}: +{await profit.FormatKasinoCurrencyAsync()}");
        }

        // Mark prediction as complete
        prediction.State = PredictionState.Complete;
        prediction.WinningOptionIndex = winningOptionIndex;
        prediction.CompletedAt = DateTimeOffset.UtcNow;
        await redisDb.StringSetAsync($"prediction:{predictionId}", JsonSerializer.Serialize(prediction));
        await redisDb.SetRemoveAsync("predictions:active", predictionId);

        // Cleanup after 24 hours
        await redisDb.KeyExpireAsync($"prediction:{predictionId}", TimeSpan.FromHours(24));
        await redisDb.KeyExpireAsync($"prediction:{predictionId}:bets", TimeSpan.FromHours(24));

        var winnersText = winners.Count > 0
            ? $"[br][br]Winners:[br]{string.Join("[br]", winnersList)}"
            : "[br][br]No one bet on the winning option!";

        await botInstance.SendChatMessageAsync(
            $":!: PREDICTION ENDED :!:[br]{prediction.Description}[br]" +
            $"Winning option: {winningOption.Index}. {winningOption.Text}[br]" +
            $"Total pot: {await totalPot.FormatKasinoCurrencyAsync()}[br]" +
            $"Winners: {winners.Count}{winnersText}",
            true);
    }
}

/// <summary>
/// Command to check current prediction status
/// </summary>
[KasinoCommand]
public class PredictionStatusCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^prediction status(?:\s+(.+))?$", RegexOptions.IgnoreCase),
        new Regex(@"^pred status(?:\s+(.+))?$", RegexOptions.IgnoreCase),
        new Regex(@"^prediction$", RegexOptions.IgnoreCase),
        new Regex(@"^pred$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!prediction status [\"prediction name\"] - Check current prediction status";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);
    public RateLimitOptionsModel? RateLimitOptions => null;

    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public bool WhisperCanInvoke => true;

    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.BotRedisConnectionString
        ]);

        if (string.IsNullOrEmpty(settings[BuiltIn.Keys.BotRedisConnectionString].Value))
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, predictions are not available at this time");
            return;
        }

        var redis = await ConnectionMultiplexer.ConnectAsync(settings[BuiltIn.Keys.BotRedisConnectionString].Value!);
        var redisDb = redis.GetDatabase();

        var activePredictions = await redisDb.SetMembersAsync("predictions:active");

        if (activePredictions.Length == 0)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, there are no active predictions");
            return;
        }

        // Check if user specified a prediction name
        var messageText = message.Message;
        var statusMatch = Regex.Match(messageText, @"^.(?:prediction|pred)(?:\s+status)?(?:\s+(.+))?$", RegexOptions.IgnoreCase);

        string? predictionId = null;
        PredictionData? prediction = null;

        if (statusMatch.Success && statusMatch.Groups.Count > 1 && !string.IsNullOrWhiteSpace(statusMatch.Groups[1].Value))
        {
            // User specified a name
            var searchTerm = statusMatch.Groups[1].Value.Trim();
            searchTerm = System.Net.WebUtility.HtmlDecode(searchTerm);
            searchTerm = searchTerm.Trim('"');

            if (searchTerm.ToLower() != "status") // Ignore if they just typed "prediction status"
            {
                (predictionId, prediction) = await PredictionHelper.FindPredictionByName(redisDb, searchTerm, activePredictions);

                if (predictionId == null)
                {
                    await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find prediction matching '{searchTerm}'");
                    return;
                }
            }
        }

        // If no specific prediction or just "!prediction", handle based on count
        if (predictionId == null)
        {
            if (activePredictions.Length == 1)
            {
                // Show the only prediction
                predictionId = activePredictions[0].ToString();
                var json = await redisDb.StringGetAsync($"prediction:{predictionId}");
                prediction = json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<PredictionData>(json!.ToString());
            }
            else
            {
                // List all predictions
                var activeList = await PredictionHelper.ListActivePredictions(redisDb);
                var listText = string.Join("[br]", activeList.Select(p => $"  [{p.id}] {p.description}"));
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, {activePredictions.Length} active predictions:[br]{listText}[br]Use !prediction status \"<name>\" for details");
                return;
            }
        }

        if (prediction == null)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, prediction data is invalid");
            return;
        }

        // Get all bets and calculate totals
        var allBets = await redisDb.HashGetAllAsync($"prediction:{predictionId}:bets");
        var bets = allBets.Select(entry =>
            JsonSerializer.Deserialize<PredictionBet>(entry.Value!.ToString())).ToList();

        var totalPot = bets.Sum(b => b!.Amount);

        // Update option totals
        foreach (var option in prediction.Options)
        {
            option.TotalBet = bets.Where(b => b!.OptionIndex == option.Index).Sum(b => b!.Amount);
        }

        var optionsText = string.Join("[br]", prediction.Options.Select(o =>
            $"  {o.Index}. {o.Text}: {o.TotalBet.FormatKasinoCurrencyAsync().Result} " +
            $"({(totalPot > 0 ? (o.TotalBet / totalPot * 100).ToString("F1") : "0")}%)"));

        var bettingStatus = prediction.BettingClosed ? "[COLOR=#ff0000]BETTING CLOSED[/COLOR]" : "[COLOR=#00ff00]Betting Open[/COLOR]";
        var betInstructions = prediction.BettingClosed
            ? ""
            : $"[br]use !bet {(activePredictions.Length > 1 ? $"\"{prediction.Description}\" " : "")}<amount> <option>";

        await botInstance.SendChatMessageAsync(
            $"Prediction [{predictionId}]: {prediction.Description}[br]" +
            $"Options:[br]{optionsText}[br]" +
            $"Status: {bettingStatus}, Total pot: {await totalPot.FormatKasinoCurrencyAsync()}, total bets: {bets.Count}{betInstructions}",
            true);
    }
}

/// <summary>
/// Command to close betting on a prediction
/// </summary>
[KasinoCommand]
public class PredictionCloseBetsCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^prediction close(?:\s+(.+))?$", RegexOptions.IgnoreCase),
        new Regex(@"^pred close(?:\s+(.+))?$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!prediction close [\"prediction name\"] - Close betting on a prediction (no new bets accepted)";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;

    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public bool WhisperCanInvoke => true;
    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.BotRedisConnectionString
        ]);

        if (string.IsNullOrEmpty(settings[BuiltIn.Keys.BotRedisConnectionString].Value))
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, predictions are not available at this time");
            return;
        }

        var redis = await ConnectionMultiplexer.ConnectAsync(settings[BuiltIn.Keys.BotRedisConnectionString].Value!);
        var redisDb = redis.GetDatabase();

        var activePredictions = await redisDb.SetMembersAsync("predictions:active");

        if (activePredictions.Length == 0)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, there are no active predictions");
            return;
        }

        // Parse: "prediction name" OR name OR nothing (if only one)
        var messageText = message.Message;
        var closeMatch = Regex.Match(messageText, @"^.(?:prediction|pred)\s+close(?:\s+(.+))?$", RegexOptions.IgnoreCase);

        string? predictionId = null;
        PredictionData? prediction = null;

        if (closeMatch.Success && closeMatch.Groups.Count > 1 && !string.IsNullOrWhiteSpace(closeMatch.Groups[1].Value))
        {
            // User specified a name
            var searchTerm = closeMatch.Groups[1].Value.Trim();
            searchTerm = System.Net.WebUtility.HtmlDecode(searchTerm);
            searchTerm = searchTerm.Trim('"');

            (predictionId, prediction) = await PredictionHelper.FindPredictionByName(redisDb, searchTerm, activePredictions);

            if (predictionId == null)
            {
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find prediction matching '{searchTerm}'");
                return;
            }
        }
        else if (activePredictions.Length == 1)
        {
            // Only one prediction, use it
            predictionId = activePredictions[0].ToString();
            var json = await redisDb.StringGetAsync($"prediction:{predictionId}");
            prediction = json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<PredictionData>(json!.ToString());
        }
        else
        {
            // Multiple predictions, need to specify
            var activeList = await PredictionHelper.ListActivePredictions(redisDb);
            var listText = string.Join("[br]", activeList.Select(p => $"  [{p.id}] {p.description}"));
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, multiple predictions active. Specify which one (keywords are enough):[br]{listText}");
            return;
        }

        if (prediction == null)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, prediction data is invalid");
            return;
        }

        if (prediction.State != PredictionState.Active)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, this prediction is not active");
            return;
        }

        if (prediction.BettingClosed)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, betting is already closed on this prediction");
            return;
        }

        // Close betting
        prediction.BettingClosed = true;
        await redisDb.StringSetAsync($"prediction:{predictionId}", JsonSerializer.Serialize(prediction));

        _logger.Info($"Betting closed on prediction {predictionId} by {user.KfUsername}");

        await botInstance.SendChatMessageAsync(
            $"{user.KfUsername}, betting is now closed for prediction '{prediction.Description}'. No new bets will be accepted.",
            true);
    }
}

/// <summary>
/// Command to cancel a prediction and refund all bets
/// </summary>
[KasinoCommand]
public class PredictionCancelCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^prediction cancel(?:\s+(.+))?$", RegexOptions.IgnoreCase),
        new Regex(@"^pred cancel(?:\s+(.+))?$", RegexOptions.IgnoreCase)
    ];

    public string? HelpText => "!prediction cancel [\"prediction name\"] - Cancel a prediction and refund all bets";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public RateLimitOptionsModel? RateLimitOptions => null;

    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public bool WhisperCanInvoke => true;

    public async Task RunCommand(ChatBot botInstance, BotCommandMessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var settings = await SettingsProvider.GetMultipleValuesAsync([
            BuiltIn.Keys.BotRedisConnectionString
        ]);

        if (string.IsNullOrEmpty(settings[BuiltIn.Keys.BotRedisConnectionString].Value))
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, predictions are not available at this time");
            return;
        }

        var redis = await ConnectionMultiplexer.ConnectAsync(settings[BuiltIn.Keys.BotRedisConnectionString].Value!);
        var redisDb = redis.GetDatabase();

        var activePredictions = await redisDb.SetMembersAsync("predictions:active");

        if (activePredictions.Length == 0)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, there are no active predictions");
            return;
        }

        // Parse: "prediction name" OR name OR nothing (if only one)
        var messageText = message.Message;
        var cancelMatch = Regex.Match(messageText, @"^.(?:prediction|pred)\s+cancel(?:\s+(.+))?$", RegexOptions.IgnoreCase);

        string? predictionId = null;
        PredictionData? prediction = null;

        if (cancelMatch.Success && cancelMatch.Groups.Count > 1 && !string.IsNullOrWhiteSpace(cancelMatch.Groups[1].Value))
        {
            // User specified a name
            var searchTerm = cancelMatch.Groups[1].Value.Trim().Trim('"');
            (predictionId, prediction) = await PredictionHelper.FindPredictionByName(redisDb, searchTerm, activePredictions);

            if (predictionId == null)
            {
                await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, couldn't find prediction matching '{searchTerm}'");
                return;
            }
        }
        else if (activePredictions.Length == 1)
        {
            // Only one prediction, use it
            predictionId = activePredictions[0].ToString();
            var json = await redisDb.StringGetAsync($"prediction:{predictionId}");
            prediction = json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<PredictionData>(json!.ToString());
        }
        else
        {
            // Multiple predictions, need to specify
            var activeList = await PredictionHelper.ListActivePredictions(redisDb);
            var listText = string.Join("[br]", activeList.Select(p => $"  [{p.id}] {p.description}"));
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, multiple predictions active. Specify which one (keywords are enough):[br]{listText}");
            return;
        }

        if (prediction == null)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, prediction data is invalid");
            return;
        }

        if (prediction.State != PredictionState.Active)
        {
            await botInstance.SendWhisperAsync(user.KfId, $"{user.KfUsername}, this prediction is not active");
            return;
        }

        // Get all bets
        var allBets = await redisDb.HashGetAllAsync($"prediction:{predictionId}:bets");
        var bets = allBets.Select(entry =>
            JsonSerializer.Deserialize<PredictionBet>(entry.Value!.ToString())).ToList();

        _logger.Info($"Cancelling prediction {predictionId} and refunding {bets.Count} bets");

        // Refund all bets
        foreach (var bet in bets)
        {
            if (bet == null) continue;

            // Get the gambler entity to refund their balance
            var gambler = await Money.GetGamblerEntityAsync(bet.UserId, ct: ctx);
            if (gambler == null)
            {
                _logger.Error($"Could not find gambler for user ID {bet.UserId} ({bet.Username}) when refunding prediction");
                continue;
            }

            await Money.ModifyBalanceAsync(gambler.Id, bet.Amount,
                TransactionSourceEventType.Gambling,
                $"Prediction cancelled: {prediction.Description}", ct: ctx);

            _logger.Info($"Refunded {bet.Amount:N} to user {bet.Username}");
        }

        // Mark prediction as cancelled
        prediction.State = PredictionState.Cancelled;
        prediction.CompletedAt = DateTimeOffset.UtcNow;
        await redisDb.StringSetAsync($"prediction:{predictionId}", JsonSerializer.Serialize(prediction));
        await redisDb.SetRemoveAsync("predictions:active", predictionId);

        // Cleanup after 1 hour
        await redisDb.KeyExpireAsync($"prediction:{predictionId}", TimeSpan.FromHours(1));
        await redisDb.KeyExpireAsync($"prediction:{predictionId}:bets", TimeSpan.FromHours(1));

        await botInstance.SendChatMessageAsync(
            $"{user.KfUsername}, prediction cancelled. {bets.Count} bets have been refunded.",
            true);
    }
}

// Data models for predictions

public enum PredictionState
{
    Active,
    Complete,
    Cancelled
}

public class PredictionData
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<PredictionOption> Options { get; set; } = new();
    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public PredictionState State { get; set; }
    public int? WinningOptionIndex { get; set; }
    public bool BettingClosed { get; set; } = false;
}

public class PredictionOption
{
    public int Index { get; set; }
    public string Text { get; set; } = string.Empty;
    public decimal TotalBet { get; set; }
}

public class PredictionBet
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int OptionIndex { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
}