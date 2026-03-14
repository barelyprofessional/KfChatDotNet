using System.Text.RegularExpressions;
using KfChatDotNetBot.Extensions;
using KfChatDotNetBot.Models;
using KfChatDotNetBot.Models.DbModels;
using KfChatDotNetBot.Services;
using KfChatDotNetBot.Settings;
using KfChatDotNetWsClient.Models.Events;

namespace KfChatDotNetBot.Commands.Kasino;

public class ShopGlobalResetCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^admin shop global reset", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(120)
    };


    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }

        await botInstance.BotServices.KasinoShop.ResetProfiles();
    }
}
public class ShopGlobalLoanResetCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^admin shop global loan reset", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(120)
    };


    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }

        await botInstance.BotServices.KasinoShop.ResetAllLoans();
    }
}

public class ShopHelpCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^shop help$", RegexOptions.IgnoreCase),
        new Regex(@"^help shop$", RegexOptions.IgnoreCase),
        new Regex(@"^shop$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(120)
    };

    private const string KasinoShopHelpLink = "https://i.ddos.lgbt/raw/pgTFLJ.html";
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        await botInstance.SendChatMessageAsync(
            $"{user.FormatUsername()} - Kasino Shop Help Tool: {KasinoShopHelpLink}", true, autoDeleteAfter: TimeSpan.FromSeconds(15));
        //just gonna link to an html hosted on iddos
    }
}

public class ShopListCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^my (?<choice>loans|assets|investments|rtp)$", RegexOptions.IgnoreCase),
        new Regex(@"^list (?<choice>loans|assets|investments|rtp)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop list (?<choice>loans|assets|investments|rtp)$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(120)
    };

    
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        if (!arguments.TryGetValue("choice", out var choice))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !list <loans|assets|investments|rtp>", true, autoDeleteAfter: cleanupDelay);
            return;
        }

        switch (choice.Value)
        {
            case "loans":
                await botInstance.BotServices.KasinoShop.PrintLoansList(gambler);
                break;
            case "assets":
                await botInstance.BotServices.KasinoShop.PrintAssets(gambler);
                break;
            case "investments":
                await botInstance.BotServices.KasinoShop.PrintInvestments(gambler);
                break;
            case "rtp":
                await botInstance.BotServices.KasinoShop.PrintRtp(gambler);
                break;
            default: await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, invalid choice. !list <loans|assets|investments|rtp>", true, autoDeleteAfter: cleanupDelay); return;
        }
    }
}

public class ShopSellCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^sell (?<id>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop sell (?<id>\d+)$", RegexOptions.IgnoreCase),
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(120)
    };


    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        if (!arguments.TryGetValue("id", out var id))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, need the item ID of the item you want to sell. !sell <id>", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        int item = Convert.ToInt32(id.Value);
        await botInstance.BotServices.KasinoShop.ProcessAssetSale(gambler, item);
    }
}
public class BegCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^beg$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 1,
        Window = TimeSpan.FromSeconds(120)
    };
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        await botInstance.BotServices.KasinoShop.ProcessBeg(gambler);
    }
}

public class LoanCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^loan (?<reciever>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^loan$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        if (!arguments.TryGetValue("reciever", out var reciever) || !arguments.TryGetValue("amount", out var amount)) //list users active loans
        {
            await botInstance.BotServices.KasinoShop.PrintLoansList(gambler);
            //await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !loan <id> <amount>", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        int id = Convert.ToInt32(reciever.Value);
        if (!botInstance.BotServices.KasinoShop.Gambler_Profiles.ContainsKey(id))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, could not find a kasino shop profile for {id}.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        await botInstance.BotServices.KasinoShop.ProcessLoan(id, Convert.ToDecimal(amount.Value), gambler, gambler.Id);
    }
}

public class RepaymentCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^repay (?<reciever>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^repay$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!beg to beg for a loan";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        if (!arguments.TryGetValue("reciever", out var reciever) || !arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, not enough arguments. !repay <id> <amount>", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        int id = Convert.ToInt32(reciever.Value);
        if (!botInstance.BotServices.KasinoShop.Gambler_Profiles.ContainsKey(id))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, could not find a kasino shop profile for {id}.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        decimal amountToPay = Convert.ToDecimal(amount.Value);

        await botInstance.BotServices.KasinoShop.ProcessRepayment(gambler, user, id, amountToPay);
    }
}


public class ShopSmashableCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^shop buy assets (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop buy smashable (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop assets (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop smashable (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop smashable$", RegexOptions.IgnoreCase),
        new Regex(@"^shop assets$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!shop to get a list of shop commands";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        if (!arguments.TryGetValue("num", out var num))
        {
            await botInstance.BotServices.KasinoShop.PrintSmashableShop(gambler);
            return;
        }
        int smashable = Convert.ToInt32(num.Value);
        if (smashable < 1 || smashable > 3)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, invalid smashable pick, must pick 1, 2, or 3.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        await botInstance.BotServices.KasinoShop.ProcessSmashablePurchase(gambler, smashable);
    }
}

public class ShopInvestmentsCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^buy investments (?<num>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^buy investments (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^buy investments$", RegexOptions.IgnoreCase),
        new Regex(@"^shop investments (?<num>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop investments (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop investments$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!shop investments to look at the investments for sale. 1 2 or 3 and an optional amount at the end to buy the investment (default you will buy 1)";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        if (!arguments.TryGetValue("num", out var num))
        {
            await botInstance.SendChatMessageAsync($"1: Gold - ${KasinoShop.GoldBasePriceOz}KKK/oz[br]" +
                                                   $"2: Silver - ${KasinoShop.SilverBasePriceOz}KKK/oz[br]" +
                                                   $"3: House - ${KasinoShop.BaseHousePrice} KKK", true, autoDeleteAfter: TimeSpan.FromSeconds(10));
            return;
        }
        int item = Convert.ToInt32(num.Value);
        decimal investment;
        if (!arguments.TryGetValue("amount", out var amount))
        {
            switch (item)
            {
                case 1: investment = 300000; break;
                case 2: investment = 10000; break;
                case 3: investment = 100000000; break;
                default: await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, invalid pick, must pick 1, 2, or 3.", true, autoDeleteAfter: cleanupDelay); return;
            }
        }
        else investment = Convert.ToDecimal(amount.Value);
        
        await botInstance.BotServices.KasinoShop.ProcessInvestment(gambler, item, investment);
    }
}

public class ShopUpdateGambler : ICommand
{
    public List<Regex> Patterns => [new Regex(@"^shop update gambler$", RegexOptions.IgnoreCase)];
    public string? HelpText => "update profile to new gambler id if you abandon your gambler account";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };
    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments, CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        await botInstance.BotServices.KasinoShop.UpdateGambler(gambler);
    }
}

public class ShopShoeCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^shop shoes (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^buy shoes (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop buy shoes (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop shoes (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^buy shoes (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop buy shoes (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop shoes$", RegexOptions.IgnoreCase),
        new Regex(@"^buy shoes$", RegexOptions.IgnoreCase),
        new Regex(@"^shop buy shoes$", RegexOptions.IgnoreCase),
    ];
    public string? HelpText => "!shop investments to look at the investments for sale. 1 2 or 3 and an optional amount at the end to buy the investment (default you will buy 1)";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        
        if (!arguments.TryGetValue("num", out var num))
        {
            await botInstance.BotServices.KasinoShop.PrintShoeMarket(gambler);
            return;
        }
        
        int shoe = Convert.ToInt32(num.Value);
        if (shoe < 1 || shoe > 3)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, invalid shoe pick, must pick 1, 2, or 3.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        await botInstance.BotServices.KasinoShop.ProcessShoePurchase(gambler, shoe);
    }
}

public class ShopSkinCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^shop skin (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^buy skin (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop buy skin (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop skin$", RegexOptions.IgnoreCase),
        new Regex(@"^buy skin$", RegexOptions.IgnoreCase),
        new Regex(@"^shop buy skin$", RegexOptions.IgnoreCase),
    ];
    public string? HelpText => "!shop investments to look at the investments for sale. 1 2 or 3 and an optional amount at the end to buy the investment (default you will buy 1)";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        if (!arguments.TryGetValue("num", out var num))
        {
            await botInstance.BotServices.KasinoShop.PrintSkinMarket(gambler);
            return;
        }

        int skin = Convert.ToInt32(num.Value);
        
        await botInstance.BotServices.KasinoShop.ProcessSkinPurchase(gambler, skin);
        
    }
}
public class ShopStakeCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^stake$", RegexOptions.IgnoreCase),
        new Regex(@"^shop stake$", RegexOptions.IgnoreCase),
        new Regex(@"^stake (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop stake (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
    ];
    public string? HelpText => "!shop investments to look at the investments for sale. 1 2 or 3 and an optional amount at the end to buy the investment (default you will buy 1)";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, stake your crypto to earn a small amount of interest every day. Minimum stake time: 1 week. !stake <amount>", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        await botInstance.BotServices.KasinoShop.ProcessStake(gambler, Convert.ToDecimal(amount.Value));
    }
}


public class ShopUnstakeCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^unstake$", RegexOptions.IgnoreCase),
        new Regex(@"^shop unstake$", RegexOptions.IgnoreCase),
        new Regex(@"^unstake (?<all>all)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop unstake (?<all>all)$", RegexOptions.IgnoreCase),
        new Regex(@"^unstake (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop unstake (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^unstake (?<id>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop unstake (?<id>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^unstake (?<id>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop unstake (?<id>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
    ];
    public string? HelpText => "!shop investments to look at the investments for sale. 1 2 or 3 and an optional amount at the end to buy the investment (default you will buy 1)";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        bool containsId = arguments.TryGetValue("id", out var id);
        bool containsAmount = arguments.TryGetValue("amount", out var amount);
        bool containsAll = arguments.TryGetValue("all", out var all);
        decimal amountUnstake = -1;
        int unstakeId = -1;
        if (containsId) unstakeId = Convert.ToInt32(id!.Value);
        if (containsAmount) amountUnstake = Convert.ToDecimal(amount!.Value);
        if (!containsId && !containsAmount && !containsAll)
        {
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you must specify an id or amount to unstake, or all to unstake everything.", true,
                autoDeleteAfter: cleanupDelay);
            return;
        }

        await botInstance.BotServices.KasinoShop.UnStake(gambler, amountUnstake, unstakeId, containsAll);
    }
}

public class SmashCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^smash", RegexOptions.IgnoreCase),
    ];
    
    public string? HelpText => "destroy your <asset>, show your loaners you're really gonna do it dewd!!!";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        await botInstance.BotServices.KasinoShop.ProcessSmash(gambler);
    }
}

public class SponsorShipCommand : ICommand
{
    public List<Regex> Patterns => [new Regex(@"^shop sponsorship$", RegexOptions.IgnoreCase)];
    public string? HelpText => "buy drugs";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        await botInstance.BotServices.KasinoShop.ProcessSponsorship(gambler);
    }
}

public class SponsorShipBonusCommand : ICommand
{
    public List<Regex> Patterns => [new Regex(@"^sponsor bonus$", RegexOptions.IgnoreCase)];
    public string? HelpText => "buy drugs";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        await botInstance.BotServices.KasinoShop.ProcessSponsorBonus(gambler);
    }
}

public class EndSponsorShipCommand : ICommand
{
    public List<Regex> Patterns => [new Regex(@"^shop end sponsorship$", RegexOptions.IgnoreCase)];
    public string? HelpText => "buy drugs";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        await botInstance.BotServices.KasinoShop.ProcessSponsorshipEnd(gambler);
    }
}
public class ShopDrugsCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^smoke (?<choice>crack|weed|nugs|floor nugs) (?<amount>\d+(?:\.\d+)?)$"),
        new Regex(@"^shop drugs (?<num>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^buy drugs (?<num>\d+) (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^smoke (?<choice>crack|weed|nugs|floor nugs) (?<amount>\d+(?:\.\d+)?)$"),
        new Regex(@"^shop drugs (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^buy drugs (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop drugs$", RegexOptions.IgnoreCase),
        new Regex(@"^call derrick$", RegexOptions.IgnoreCase),
        new Regex(@"^buy drugs$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "buy drugs";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        int drug;
        decimal amount;
        if (arguments.TryGetValue("num", out var num))
        {
            drug = Convert.ToInt32(num.Value);
            if (drug == 1)
            {
                amount = botInstance.BotServices.KasinoShop.GetCurrentCrackPrice(gambler);
                await botInstance.BotServices.KasinoShop.ProcessDrugUse(gambler, amount, drug);
            }
            else
            {
                if (!arguments.TryGetValue("amount", out var amountt))
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, you must specify how much drug you want to buy. !buy drugs weed 1000 !shop drugs weed 1000 !smoke nugs 10",
                        true, autoDeleteAfter: cleanupDelay);
                    return;
                }
                amount = Convert.ToDecimal(amountt.Value);
                await botInstance.BotServices.KasinoShop.ProcessDrugUse(gambler, amount, drug);
                
            }
        }
        else if (arguments.TryGetValue("choice", out var choice))
        {
            string ch = choice.Value.ToLower();
            if (!arguments.TryGetValue("amount", out var amountArg))
            {
                //if they didn't put an amount they must be trying to smoke crack
                if (ch != "crack")
                {
                    await botInstance.SendChatMessageAsync(
                        $"{user.FormatUsername()}, you must specify how much drug you want to buy. !buy drugs weed 1000 !shop drugs weed 1000 !smoke nugs 10",
                        true, autoDeleteAfter: cleanupDelay);
                    return;
                }

                amount = botInstance.BotServices.KasinoShop.GetCurrentCrackPrice(gambler);
                drug = 1;
            }
            else if (ch == "crack")
            {
                amount = botInstance.BotServices.KasinoShop.GetCurrentCrackPrice(gambler);
                drug = 1;
            }
            else if (ch == "weed")
            {
                amount = Convert.ToDecimal(amountArg.Value);
                drug = 2;
            }
            else //nugs or floor nugs
            {
                amount = Convert.ToInt32(amountArg.Value);
                drug = 3;
            }

            if (amount < 1)
            {
                await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you have to spend money to buy drugs.", true, autoDeleteAfter: cleanupDelay);
                return;
            }
            await botInstance.BotServices.KasinoShop.ProcessDrugUse(gambler, amount, drug);
        }
        else
        {
            await botInstance.BotServices.KasinoShop.PrintDrugMarket(gambler);
        }
    }
}

public class ShopCarCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^buy car (?<choice>civic|type r|type-r|bentley|BMW|Audi)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop car (?<choice>civic|type r|type-r|bentley|BMW|Audi)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop car (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^buy car (?<num>\d+)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop car$", RegexOptions.IgnoreCase),
        new Regex(@"^buy car$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!shop to get a list of shop commands";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    { //civic audi bentley bmw
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        int car;
        if (arguments.TryGetValue("num", out var num))
        {
            car = Convert.ToInt32(num.Value);
            if (car < 1 || car > 4)
            {
                await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, invalid car choice, must pick 1, 2, 3, or 4 (civic, audi, bentley, bmw)", true, autoDeleteAfter: cleanupDelay);
                return;
            }
            await botInstance.BotServices.KasinoShop.ProcessCarPurchase(gambler, car);
        }
        else if (arguments.TryGetValue("choice", out var choice))
        {
            string carChoice = choice.Value.ToLower();
            if (carChoice == "bmw") car = 4;
            else if (carChoice == "audi") car = 3;
            else if (carChoice == "bentley") car = 2;
            else if (carChoice == "civic" || carChoice.Contains("r")) car = 1;
            else
            {
                await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, invalid car choice, must pick 1, 2, 3, or 4 (civic, audi, bentley, bmw)", true, autoDeleteAfter: cleanupDelay);
                return;
            }
            await botInstance.BotServices.KasinoShop.ProcessCarPurchase(gambler, car);
        }
        else
        {
            string str = "";
            for (int i = 1; i <= KasinoShop.DefaultCars.Count; i++)
            {
                str += $"{i}: {KasinoShop.DefaultCars.ElementAt(i - 1).Value}[br]";
            }
            await botInstance.SendChatMessageAsync(str, true, autoDeleteAfter: cleanupDelay);
        }
    }
}


public class ShopDepositCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^shop deposit (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop depo (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^deposit (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^depo (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^shop deposit$", RegexOptions.IgnoreCase),
        new Regex(@"^shop depo$", RegexOptions.IgnoreCase),
        new Regex(@"^deposit$", RegexOptions.IgnoreCase),
        new Regex(@"^depo$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "!shop to get a list of shop commands";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        if (!arguments.TryGetValue("amount", out var amount))
        {
            await botInstance.BotServices.KasinoShop.PrintBalance(gambler);
        }
        decimal depo = Convert.ToDecimal(amount!.Value);
        await botInstance.BotServices.KasinoShop.ProcessDeposit(gambler, depo);
    }
}

public class ShopCarJobCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^work job$", RegexOptions.IgnoreCase),
        new Regex(@"^shop job$", RegexOptions.IgnoreCase),
        new Regex(@"^shop work$", RegexOptions.IgnoreCase),
        new Regex(@"^work$", RegexOptions.IgnoreCase),
        new Regex(@"^job$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "work a job if you have a car";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);

        await botInstance.BotServices.KasinoShop.ProcessWorkJob(gambler);
    }
}

public class ShopWithdrawCommand : ICommand
{
    public List<Regex> Patterns =>
    [
        new Regex(@"^withdraw (?<amount>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase),
        new Regex(@"^withdraw$", RegexOptions.IgnoreCase)
    ];
    public string? HelpText => "withdraw from kasino to shop";
    public UserRight RequiredRight => UserRight.Loser;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        var gambler = await Money.GetGamblerEntityAsync(user.Id, ct: ctx);
        if (gambler == null)
        {
            throw new InvalidOperationException($"Caught a null when retrieving gambler for {user.KfUsername}");
        }
        await GlobalShopFunctions.CheckProfile(botInstance, user, gambler);
        if (!arguments.TryGetValue("amount", out var amount))
        {
            string wagerLock = "";
            if (!botInstance.BotServices.KasinoShop.CheckWagerReq(gambler))
            {
                wagerLock +=
                    $", and you need to finish your wager requirement before you can withdraw. {await botInstance.BotServices.KasinoShop.RemainingWagerReq(gambler).FormatKasinoCurrencyAsync()} remaining to be wagered before you can withdraw.";
            }
            await botInstance.SendChatMessageAsync(
                $"{user.FormatUsername()}, you can withdraw from your kasino balance of {await gambler.Balance.FormatKasinoCurrencyAsync()}. You must withdraw a minimum of {await 5000m.FormatKasinoCurrencyAsync()}{wagerLock}.",
                true, autoDeleteAfter: cleanupDelay);
            return;
        }
        decimal withdraw = Convert.ToDecimal(amount!.Value);
        if (withdraw < 5000)
        {
            await botInstance.SendChatMessageAsync($"{user.FormatUsername()}, you must withdraw a minimum of {await 5000m.FormatKasinoCurrencyAsync()}.", true, autoDeleteAfter: cleanupDelay);
        }
        await botInstance.BotServices.KasinoShop.ProcessWithdraw(gambler, withdraw);
    }
}

/// <summary>
/// RIGGING
/// </summary>
//--------------------------------------------------

public class FlipSwitchCommand : ICommand
{
    private Rigging type = Rigging.Switch;

    public List<Regex> Patterns =>
    [
        new Regex(@"^flip switch")
    ];
    public string? HelpText => "flips a switch";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }

        await botInstance.BotServices.KasinoShop!.ProcessRigging(type);
    }
}

public class PushButtonCommand : ICommand
{
    private Rigging type = Rigging.Button;
    public List<Regex> Patterns =>
    [
        new Regex(@"^push button")
    ];
    public string? HelpText => "flips a switch";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }

        await botInstance.BotServices.KasinoShop!.ProcessRigging(type);
    }
}

public class PullLeverCommand : ICommand
{
    private Rigging type = Rigging.Lever;
    public List<Regex> Patterns =>
    [
        new Regex(@"^pull lever")
    ];
    public string? HelpText => "flips a switch";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        await botInstance.BotServices.KasinoShop!.ProcessRigging(type);
    }
}

public class DialCommand : ICommand
{
    private Rigging type = Rigging.Dial;
    public List<Regex> Patterns =>
    [
        new Regex(@"^dial (?<choice>up|down)")
    ];
    public string? HelpText => "flips a switch";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        if (!arguments.TryGetValue("choice", out var choice)) return;
        bool dialUp = choice.Value.ToLower() == "up";
        await botInstance.BotServices.KasinoShop!.ProcessRigging(type, dial: dialUp);
    }
}

public class KeypadCommand : ICommand
{
    private Rigging type = Rigging.Dial;
    public List<Regex> Patterns =>
    [
        new Regex(@"^keypad (?<num>\d+(?:\.\d+)?)")
    ];
    public string? HelpText => "flips a switch";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }

        if (!arguments.TryGetValue("num", out var num)) return;
        var number = Convert.ToDecimal(num.Value);
        
        await botInstance.BotServices.KasinoShop!.ProcessRigging(type, number);
    }
}

public class PanelCommand : ICommand
{
    private Rigging type = Rigging.Dial;
    public List<Regex> Patterns =>
    [
        new Regex(@"^panel")
    ];
    public string? HelpText => "flips a switch";
    public UserRight RequiredRight => UserRight.TrueAndHonest;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public RateLimitOptionsModel? RateLimitOptions => new RateLimitOptionsModel
    {
        MaxInvocations = 2,
        Window = TimeSpan.FromSeconds(60)
    };

    public async Task RunCommand(ChatBot botInstance, MessageModel message, UserDbModel user, GroupCollection arguments,
        CancellationToken ctx)
    {
        var cleanupDelay = TimeSpan.FromSeconds(10);
        if (botInstance.BotServices.KasinoShop == null)
        {
            await botInstance.SendChatMessageAsync("KasinoShop is not currently running.", true, autoDeleteAfter: cleanupDelay);
            return;
        }
        
        await botInstance.BotServices.KasinoShop!.GetCurrentRiggingState();
    }
}








public static class GlobalShopFunctions
{
    public static async Task CheckProfile(ChatBot botInstance, UserDbModel user, GamblerDbModel gambler) //checks if the user trying to run the command has a profile. if they do not, creates a profile for them
    {
        if (!botInstance.BotServices.KasinoShop!.Gambler_Profiles.ContainsKey(user.KfId))
        {
            await botInstance.BotServices.KasinoShop.CreateProfile(gambler);
            await botInstance.SendChatMessageAsync($"Created kasino shop profile for {user.FormatUsername()}({user.KfId})", true);
        }
        
    }
}
